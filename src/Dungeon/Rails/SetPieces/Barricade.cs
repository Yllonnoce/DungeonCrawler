using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A timber barricade across a line (plan 11): two posts and three crossed planks, 1.8 m wide by 1.2 m tall, a lantern on a stake
/// in front of it on the cart's side, as the ore heap's. Solid (layer 1) and something to hit (layer 4): <see cref="BarricadeRules.Toughness"/>
/// blade or arrow hits, or one spell, cut it down (<see cref="ReceiveAttack"/>). A cart whose front reaches <see cref="FaceS"/> on its
/// lane while it stands smashes through it (the <see cref="RailEncounterController"/> judges that and calls <see cref="Smash"/>).
/// <see cref="Alarm"/> blinks the lantern while a cart bears down; <see cref="Restand"/> stands it again. The server judges it and tells
/// "cut", "smashed" and "standing" (<see cref="NetRef"/>); a client's copy only shows them (<see cref="ShowCut"/>, <see cref="ShowSmashed"/>,
/// <see cref="ShowStanding"/>).
/// </summary>
public partial class Barricade : StaticBody3D, ICombatTarget, ISoundPoint
{
    /// <summary>Where its sounds (the bell, the cut, the smash) are heard, on every role: at the planks.</summary>
    public Vector3 SoundPoint => GlobalPosition + Vector3.Up * 0.6f;

    const float Width = 1.8f, Height = 1.2f;
    /// <summary>The lantern's stake stands this far out to the side and toward the cart (+Z) from the middle, clear of the cart's body.</summary>
    static readonly Vector3 LanternAt = new(1.05f, 1.45f, 0.75f);

    /// <summary>The S of its middle on its own line (the party's, or the enemy line's on the slag line).</summary>
    public float S { get; init; }
    /// <summary>It stands across the slag line (the enemy track), not the party's.</summary>
    public bool SlagLine { get; init; }
    /// <summary>Its wire name ("barricade:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";
    /// <summary>Where a cart's front meets it, in its own line's S.</summary>
    public float FaceS => S - BarricadeRules.HalfDepth;
    /// <summary>What is left of it: <see cref="BarricadeRules.Toughness"/> whole, 0 down.</summary>
    public int Left { get; private set; } = BarricadeRules.Toughness;
    public bool IsStanding => Left > 0;
    /// <summary>A cart is bearing down on it: the lantern blinks.</summary>
    public bool Alarm { get; set; }
    /// <summary>Who cut it down last ("" until then, and after a smash).</summary>
    public string CutBy { get; private set; } = "";
    /// <summary>It went down: "cut" or "smashed". Server only.</summary>
    public event Action<Barricade, string>? Down;

    private readonly StandardMaterial3D _glass = new() { AlbedoColor = new Color(1f, 0.55f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 3f };
    private Material _wood = null!;
    private Node3D _look = null!;
    private OmniLight3D _lantern = null!;
    private CollisionShape3D _shape = null!;
    private readonly List<MeshInstance3D> _planks = new();
    /// <summary>Its posts and planks, for the timbers a smash throws (box size, place in its own space, material).</summary>
    private readonly List<(Vector3 size, Transform3D local, Material material)> _parts = new();
    private float _blink;

    public Barricade()
    {
        CollisionMask = 0;
    }

    public override void _Ready()
    {
        _wood = DungeonBuilder.Material("weathered_planks", new Color(0.5f, 0.38f, 0.24f), 1.6f);
        _shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, Height, BarricadeRules.HalfDepth * 2f) }, Position = new Vector3(0, Height / 2f, 0) };
        AddChild(_shape);
        _look = new Node3D { Name = "Look" };
        AddChild(_look);
        // two posts at its ends, set in the ballast
        foreach (var side in new[] { -1f, 1f })
            Part(new Vector3(0.14f, Height, 0.14f), new Transform3D(Basis.Identity, new Vector3(side * (Width / 2f - 0.07f), Height / 2f, 0f)), plank: false);
        // three planks nailed across them on the cart's side (+Z): a rail along the top, and two crossed under it
        Part(new Vector3(Width, 0.2f, 0.06f), new Transform3D(Basis.Identity, new Vector3(0f, Height - 0.15f, 0.1f)), plank: true);
        const float rise = 0.62f, run = Width - 0.2f;
        var tilt = Mathf.Atan2(rise, run);
        var length = Mathf.Sqrt(rise * rise + run * run) + 0.1f;
        foreach (var (sign, z) in new[] { (1f, 0.14f), (-1f, 0.18f) })
            Part(new Vector3(length, 0.2f, 0.05f), new Transform3D(new Basis(Vector3.Back, sign * tilt), new Vector3(0f, 0.5f, z)), plank: true);
        // the lantern on a stake in front, on the cart's side (+Z: toward lower S, where the cart comes from)
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, LanternAt.Y - 0.1f, 0.06f) }, MaterialOverride = _wood, Position = new Vector3(LanternAt.X, (LanternAt.Y - 0.1f) / 2f, LanternAt.Z) });
        AddChild(new MeshInstance3D { Name = "Lantern", Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.24f, 0.18f) }, MaterialOverride = _glass, Position = LanternAt });
        _lantern = new OmniLight3D { Name = "LanternLight", OmniRange = 7f, LightEnergy = 1.8f, LightColor = new Color(1f, 0.45f, 0.1f), ShadowEnabled = false, Position = LanternAt + Vector3.Up * 0.05f };
        AddChild(_lantern);
        Stand();
    }

    /// <summary>The lantern's place in the world (the flows' sight check aims at it).</summary>
    public Vector3 LanternPoint => ToGlobal(LanternAt);

    private void Part(Vector3 size, Transform3D local, bool plank)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = _wood, Transform = local };
        _look.AddChild(mesh);
        if (plank) _planks.Add(mesh);
        _parts.Add((size, local, _wood));
    }

    public override void _Process(double delta)
    {
        if (!IsStanding) return;
        if (!Alarm) { _lantern.LightEnergy = 1.8f; return; }
        _blink += (float)delta * 6f;
        _lantern.LightEnergy = Mathf.Sin(_blink * Mathf.Pi) > 0f ? 3f : 0.4f;
    }

    public string DisplayName => "barricade";
    public bool IsAlive => IsStanding;
    public bool IsUnaware => false;
    public bool IsCreature => false;
    public void ApplyStatus(StatusApplication status) { }

    /// <summary>A blade, an arrow or a shove knocks a plank off; a spell takes it all (<see cref="BarricadeRules.HitWeight"/>). The last cuts it down.</summary>
    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (!IsStanding || NetSession.IsClient) return DamageResult.Miss(attack.Type);
        Left = Math.Max(0, Left - BarricadeRules.HitWeight(attack.Kind));
        if (Left > 0)
        {
            EventBus.Instance.Log($"{attackerName} hacks at the barricade.");
            ShowLeft();
        }
        else Callable.From(() => Cut(attackerName)).CallDeferred(); // a hit can land inside a physics query
        return new DamageResult(true, false, false, false, 1, attack.Type, Array.Empty<StatusApplication>());
    }

    /// <summary>The planks still nailed on: one goes with each hit short of the last.</summary>
    private void ShowLeft()
    {
        for (var i = 0; i < _planks.Count; i++) _planks[i].Visible = i < Left;
    }

    private void Cut(string who)
    {
        if (IsStanding || !_look.Visible) return; // stood again before the deferred cut came (a restore in between), or already down
        Knock();
        CutBy = who;
        EventBus.Instance.Log($"{who} cuts the barricade down.");
        if (GetParent() is Node3D root) TimberDebris.Burst(root, new Transform3D(GlobalBasis, SoundPoint), Width, _wood);
        RideAudio.At("crate_break", SoundPoint); // once, as it falls: a client's comes from its "cut" (RideAudio.OnProp), at the same point
        Down?.Invoke(this, "cut");
        Tell("cut");
    }

    /// <summary>A cart's front meets it standing: gone in splinters, its timbers thrown ahead of <paramref name="cartVelocity"/> (<paramref name="debris"/>), with the smash's cue. Server.</summary>
    public void Smash(Node3D root, Vector3 cartVelocity, bool debris = true)
    {
        if (!IsStanding) return;
        Knock();
        CutBy = "";
        if (debris) Break(root, cartVelocity);
        RideAudio.At("timber_smash", SoundPoint); // a client's comes from its "smashed" (RideAudio.OnProp), at the same point
        Down?.Invoke(this, "smashed");
        Tell("smashed");
    }

    private void Break(Node3D root, Vector3 cartVelocity)
    {
        TimberDebris.Burst(root, new Transform3D(GlobalBasis, SoundPoint), Width, _wood);
        TimberDebris.Tumble(root, GlobalTransform, _parts, cartVelocity, "BarricadeTimber");
    }

    /// <summary>Down: nothing solid, nothing to hit, the planks and posts gone, the lantern low.</summary>
    private void Knock()
    {
        Left = 0;
        Alarm = false;
        _look.Visible = false;
        _shape.Disabled = true;
        CollisionLayer = 0;
        _lantern.LightEnergy = 0.6f;
    }

    /// <summary>Standing again, whole: its ride runs again after a wreck or a load. Server (told "standing").</summary>
    public void Restand()
    {
        var was = IsStanding;
        Stand();
        if (!was) Tell("standing");
    }

    /// <summary>Client: cut down as on the server, with its splinters, silently (the cue comes from the PropEvt).</summary>
    public void ShowCut()
    {
        if (!IsStanding) return;
        Knock();
        if (GetParent() is Node3D root) TimberDebris.Burst(root, new Transform3D(GlobalBasis, SoundPoint), Width, _wood);
    }

    /// <summary>Client: smashed as on the server, with its splinters and timbers (<paramref name="debris"/>; the prop state's catch-up is quiet).</summary>
    public void ShowSmashed(Node3D root, bool debris = true)
    {
        if (!IsStanding) return;
        Knock();
        if (debris) Break(root, -GlobalBasis.Z * CartRules.Cruise); // the cart's own speed is not ours to know here: on along the line at cruise
    }

    /// <summary>Client: standing again.</summary>
    public void ShowStanding() { if (!IsStanding) Stand(); }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    private void Stand()
    {
        Left = BarricadeRules.Toughness;
        Alarm = false;
        CutBy = "";
        _look.Visible = true;
        ShowLeft();
        _shape.Disabled = false;
        CollisionLayer = 1u | 4u;
        _lantern.LightEnergy = 1.8f;
    }

    /// <summary>Server test hook: cut it down at once, by <paramref name="who"/>.</summary>
    internal void DebugCut(string who) { if (IsStanding) { Left = 0; Cut(who); } }
}
