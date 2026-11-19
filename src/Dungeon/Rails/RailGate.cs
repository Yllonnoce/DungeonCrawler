using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// An iron gate across both tracks at a stop: two posts, a bar chained to a winch post. It holds the cart at the stop
/// until the stop's mobs are dead; <see cref="Lift"/> winches the bar up. Procedural boxes; only the posts collide
/// (the cart is code-driven and walkers can duck under the bar). The server tells "up" ("gate:{stop}"); a client's copy
/// shows it (<see cref="ShowUp"/>).
/// </summary>
public partial class RailGate : Node3D, ISoundPoint
{
    /// <summary>Where the gate's lift is heard, on every role: above the bar's middle.</summary>
    public Vector3 SoundPoint => GlobalPosition + Vector3.Up * 2f;

    public const float BarHeight = 1.0f;
    /// <summary>The lifted bar's middle: its underside (2.88 m) clears a coupled cage wagon's roof (2.57 m).</summary>
    public const float LiftedHeight = 2.95f;
    public const float LiftSeconds = 1.5f;
    public const float PostHeight = 3.1f;

    public int Stop { get; }
    public bool Up { get; private set; }
    /// <summary>Set by the RailController: true when no living holder of the stop stands within range of the gate (the winch then works).</summary>
    public Func<RailGate, bool>? CanCrank { get; set; }
    /// <summary>The gate has been lifted (by the kill check or the winch; not by a restore).</summary>
    public event Action<RailGate>? Lifted;
    private double _refusedAt = -100;

    private readonly float _left, _right;
    private Node3D _bar = null!;
    /// <summary>The links from the bar's end up to the winch head: wound in (squashed onto the bar) as it rises.</summary>
    private Node3D _chain = null!;
    const float WoundChain = 0.02f;
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.8f, Roughness = 0.5f };
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.24f, 0.16f, 0.1f), Roughness = 0.85f };
    private readonly StandardMaterial3D _warn = new() { AlbedoColor = new Color(0.55f, 0.12f, 0.08f), Roughness = 0.7f };

    /// <param name="left">Lateral extent of the gate to the left of its origin (negative or zero, in the node's X).</param>
    /// <param name="right">Lateral extent to the right.</param>
    /// <param name="winchRight">The winch post stands at the right end (else the left).</param>
    public RailGate(int stop, float left, float right, bool winchRight)
    {
        Stop = stop;
        _left = left;
        _right = right;
        _winchRight = winchRight;
        Name = $"RailGate{stop}";
    }

    private readonly bool _winchRight;

    public override void _Ready()
    {
        float span = _right - _left, mid = (_left + _right) / 2f;
        Post("PostL", _left, _winchRight);
        Post("PostR", _right, !_winchRight);
        _bar = new Node3D { Name = "Bar", Position = new Vector3(0, BarHeight, 0) };
        AddChild(_bar);
        Box(_bar, "Iron", new Vector3(span, 0.12f, 0.12f), new Vector3(mid, 0, 0), _iron);
        // red-banded warning blocks along the bar
        for (var x = _left + 0.5f; x < _right - 0.3f; x += 1.2f)
            Box(_bar, "Band", new Vector3(0.3f, 0.14f, 0.14f), new Vector3(x, 0, 0), _warn);
        // the chain: small links from the winch post's head down to the bar's end (they ride the bar up, wound in as it rises)
        var winchX = _winchRight ? _right : _left;
        var rise = PostHeight - 0.2f - BarHeight;
        _chain = new Node3D { Name = "Chain" };
        _bar.AddChild(_chain);
        for (var k = 0; k < 6; k++)
            Box(_chain, $"Link{k}", new Vector3(0.06f, 0.14f, 0.06f), new Vector3(winchX - Mathf.Sign(winchX) * 0.1f, rise * (k + 0.5f) / 6f, 0.0f), _iron);
        if (Up) SetLifted();
    }

    /// <summary>The winch post ("winch:{stop}"), once the gate is in the tree.</summary>
    internal WinchPost? Winch { get; private set; }

    private void Post(string name, float x, bool winch)
    {
        var body = winch ? (Winch = new WinchPost(this) { Name = name, Position = new Vector3(x, 0, 0), CollisionLayer = 1 | 8, CollisionMask = 0 })
            : new StaticBody3D { Name = name, Position = new Vector3(x, 0, 0), CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.3f, PostHeight, 0.3f) }, Position = new Vector3(0, PostHeight / 2f, 0) });
        Box(body, "Post", new Vector3(0.28f, PostHeight, 0.28f), new Vector3(0, PostHeight / 2f, 0), _wood);
        if (winch) Box(body, "Winch", new Vector3(0.5f, 0.2f, 0.2f), new Vector3(0, PostHeight - 0.2f, 0), _iron);
        AddChild(body);
    }

    private static void Box(Node3D parent, string name, Vector3 size, Vector3 at, Material material)
        => parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });

    /// <summary>
    /// The winch post ("winch:{stop}"): looked at, it offers "Crank the winch" while the gate is down, on every peer (a client's gate
    /// is up when the server told it). A use reaches <see cref="Crank"/> on the server, a remote player's through its InteractIntent.
    /// </summary>
    internal partial class WinchPost : StaticBody3D, IInteractable
    {
        private readonly RailGate _gate;
        public WinchPost(RailGate gate) => _gate = gate;
        /// <summary>The gate it winds.</summary>
        public RailGate Gate => _gate;
        public string Prompt => PropPromptRules.WinchPrompt(_gate.Up);
        public void Interact(PartyController party, Adventurer actor)
        {
            if (!actor.Downed) _gate.Crank();
        }
    }

    /// <summary>Server: the winch lifts the gate when none of its holders is near; otherwise the crew says no (once per 3 s).</summary>
    public void Crank()
    {
        if (NetSession.IsClient || Up) return;
        if (CanCrank?.Invoke(this) ?? true) { Lift(); return; }
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _refusedAt < 3.0) return;
        _refusedAt = now;
        EventBus.Instance.Log("Not while they're still here.");
    }

    /// <summary>The bar goes up over <see cref="LiftSeconds"/>; "The gate lifts." is logged.</summary>
    public void Lift()
    {
        if (Up) return;
        Up = true;
        EventBus.Instance.Log("The gate lifts.");
        RideAudio.At("gate_lift", SoundPoint); // a client's comes from its "up" (RideAudio.OnProp), at the same point
        Lifted?.Invoke(this);
        Tell("up");
        Wind();
    }

    /// <summary>Up at once, with no animation, log or sound (a restored save, the quiet lift after one): told as the quiet "raised", which no peer hears.</summary>
    public void SetUp()
    {
        var was = Up;
        Up = true;
        SetLifted();
        if (!was) Tell("raised");
    }

    /// <summary>Client: the gate goes up as on the server (the bar winds up, <paramref name="animate"/>), with no log, cue or event.</summary>
    public void ShowUp(bool animate = true)
    {
        if (Up) return;
        Up = true;
        if (animate) Wind(); else SetLifted();
    }

    private void Tell(string what)
    {
        if (!NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRefs.Gate(Stop), what);
    }

    private void Wind()
    {
        if (_bar == null || !IsInsideTree()) return;
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(_bar, "position:y", LiftedHeight, LiftSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_chain, "scale:y", WoundChain, LiftSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void SetLifted()
    {
        if (_bar != null) _bar.Position = new Vector3(0, LiftedHeight, 0);
        if (_chain != null) _chain.Scale = new Vector3(1f, WoundChain, 1f);
    }
}
