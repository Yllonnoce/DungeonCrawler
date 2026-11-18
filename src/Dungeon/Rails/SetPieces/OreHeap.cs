using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A heap of spilled ore boxes across the party line, a lantern on a stake in front of it. Solid (layer 1) and something to
/// hit (layer 4): <see cref="Hits"/> hits of anything clear it (<see cref="Cleared"/>). A cart that reaches it on the party lane
/// crashes into it (the <see cref="RailController"/> stops the cart at <see cref="FaceS"/>). <see cref="Alarm"/> blinks the
/// lantern while a cart bears down. <see cref="Rebuild"/> stacks it again. The server judges it and tells "cleared" and
/// "rebuilt" (<see cref="NetRef"/>); a client's copy only shows them (<see cref="ShowCleared"/>, <see cref="ShowRebuilt"/>).
/// </summary>
public partial class OreHeap : StaticBody3D, ICombatTarget
{
    public const int Hits = 3;
    /// <summary>From the heap's middle to its face along the line.</summary>
    public const float HalfDepth = 0.6f;
    const float Width = 1.8f;

    /// <summary>The party line's S at the heap's middle.</summary>
    public float S { get; init; }
    /// <summary>Where a cart's front meets it.</summary>
    public float FaceS => S - HalfDepth;
    public int Left { get; private set; } = Hits;
    public bool IsAlive => Left > 0;
    /// <summary>A cart is bearing down on it: the lantern blinks.</summary>
    public bool Alarm { get; set; }
    /// <summary>The heap was cleared (by a hit): told once per build.</summary>
    public event Action<OreHeap>? Cleared;
    /// <summary>Its wire name ("heap:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.32f, 0.22f, 0.13f), Roughness = 0.9f };
    private readonly StandardMaterial3D _ore = new() { AlbedoColor = new Color(0.22f, 0.2f, 0.19f), Metallic = 0.3f, Roughness = 0.85f };
    private readonly StandardMaterial3D _glass = new() { AlbedoColor = new Color(1f, 0.55f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 3f };
    private Node3D _pile = null!;
    private OmniLight3D _lantern = null!;
    private CollisionShape3D _shape = null!;
    private float _blink;

    public OreHeap()
    {
        CollisionMask = 0;
    }

    public override void _Ready()
    {
        _shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, 1.2f, HalfDepth * 2f) }, Position = new Vector3(0, 0.6f, 0) };
        AddChild(_shape);
        _pile = new Node3D { Name = "Pile" };
        AddChild(_pile);
        // boxes tumbled across the line, a few spilled lumps of ore
        (Vector3 at, Vector3 size, float yaw)[] boxes =
        {
            (new(-0.5f, 0.25f, 0.1f), new(0.6f, 0.5f, 0.5f), 0.2f), (new(0.35f, 0.25f, -0.1f), new(0.6f, 0.5f, 0.6f), -0.35f),
            (new(0.05f, 0.7f, 0.05f), new(0.55f, 0.45f, 0.5f), 0.6f), (new(0.75f, 0.2f, 0.3f), new(0.4f, 0.4f, 0.4f), 0.9f),
            (new(-0.85f, 0.18f, -0.25f), new(0.4f, 0.36f, 0.45f), -0.7f),
        };
        foreach (var (at, size, yaw) in boxes)
            _pile.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = _wood, Position = at, Rotation = new Vector3(0, yaw, 0.08f) });
        for (var i = 0; i < 7; i++)
            _pile.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.14f + 0.03f * (i % 3), Height = 0.2f + 0.04f * (i % 2), RadialSegments = 7, Rings = 4 },
                MaterialOverride = _ore,
                Position = new Vector3(-0.8f + i * 0.27f, 0.1f + 0.05f * (i % 2), 0.35f - 0.12f * (i % 3)),
            });
        // the lantern on a stake in front, on the cart's side (+Z: toward lower S, where the cart comes from)
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 1.3f, 0.06f) }, MaterialOverride = _wood, Position = new Vector3(0.95f, 0.65f, 0.9f) });
        AddChild(new MeshInstance3D { Name = "Lantern", Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.24f, 0.18f) }, MaterialOverride = _glass, Position = new Vector3(0.95f, 1.4f, 0.9f) });
        _lantern = new OmniLight3D { Name = "LanternLight", OmniRange = 7f, LightEnergy = 1.8f, LightColor = new Color(1f, 0.45f, 0.1f), ShadowEnabled = false, Position = new Vector3(0.95f, 1.45f, 0.9f) };
        AddChild(_lantern);
        Stack();
    }

    public override void _Process(double delta)
    {
        if (!IsAlive) return;
        if (!Alarm) { _lantern.LightEnergy = 1.8f; return; }
        _blink += (float)delta * 6f;
        _lantern.LightEnergy = Mathf.Sin(_blink * Mathf.Pi) > 0f ? 3f : 0.4f;
    }

    public string DisplayName => "ore heap";
    public bool IsUnaware => false;
    public bool IsCreature => false;
    public void ApplyStatus(StatusApplication status) { }

    /// <summary>Any hit knocks a box off; the last clears the line.</summary>
    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (!IsAlive || NetSession.IsClient) return DamageResult.Miss(attack.Type);
        Left--;
        EventBus.Instance.PlaySfx("crate_break"); // placeholder: plan 6 adds the sound
        if (Left > 0)
        {
            EventBus.Instance.Log($"{attackerName} knocks boxes off the ore heap.");
            if (_pile.GetChildCount() > 0) _pile.GetChild<Node3D>(Left % _pile.GetChildCount()).Visible = false;
        }
        else
        {
            EventBus.Instance.Log($"{attackerName} clears the ore heap off the line.");
            Callable.From(Clear).CallDeferred(); // a hit can land inside a physics query
        }
        return new DamageResult(true, false, false, false, 1, attack.Type, Array.Empty<StatusApplication>());
    }

    private void Clear()
    {
        Knock();
        Cleared?.Invoke(this);
        Tell("cleared");
    }

    /// <summary>Gone from the line: no pile, nothing solid, the lantern low.</summary>
    private void Knock()
    {
        Left = 0;
        Alarm = false;
        _pile.Visible = false;
        _shape.Disabled = true;
        CollisionLayer = 0;
        _lantern.LightEnergy = 0.6f;
    }

    /// <summary>Stacked again, whole: the ride runs again after a wreck or a load.</summary>
    public void Rebuild()
    {
        var was = IsAlive;
        Stack();
        if (!was) Tell("rebuilt");
    }

    /// <summary>Client: cleared off the line as on the server, silently (no event: the line's block is the server's).</summary>
    public void ShowCleared() { if (IsAlive) Knock(); }
    /// <summary>Client: stacked again.</summary>
    public void ShowRebuilt() { if (!IsAlive) Stack(); }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    private void Stack()
    {
        Left = Hits;
        Alarm = false;
        _pile.Visible = true;
        foreach (var child in _pile.GetChildren()) if (child is Node3D n) n.Visible = true;
        _shape.Disabled = false;
        CollisionLayer = 1u | 4u;
        _lantern.LightEnergy = 1.8f;
    }

    /// <summary>Server test hook: clear it at once.</summary>
    internal void DebugClear() { if (IsAlive) Clear(); }
}
