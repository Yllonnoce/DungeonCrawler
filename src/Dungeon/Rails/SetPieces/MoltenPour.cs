using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A slag pour over the party line: an iron trough on two posts above the track, a feed pipe down one post to a
/// <see cref="QuenchValve"/> beside the line. It runs on the floor's clock (<see cref="SetPieceRules.Pour"/> on
/// <see cref="DungeonBuilder.FloorTick"/>): dry, a 1.5 s warning (the trough's lip glows and the light swells), then a falling
/// stream of molten slag onto a <see cref="Zone"/> of the line. A broken valve quenches it for good (<see cref="Quenched"/>).
/// Dressing: the <see cref="RailEncounterController"/> judges the cart and walkers under the stream. Built on the server (a
/// client's FloorTick follows the server's floor clock, so a client copy pours in step with the server's).
/// </summary>
public partial class MoltenPour : Node3D
{
    /// <summary>The stream lands on the line within this of <see cref="S"/> (a 2 m stretch).</summary>
    public const float Zone = 1f;
    /// <summary>The trough's underside above the track.</summary>
    public const float TroughHeight = 3.1f;
    /// <summary>The valve stands this far short of the pour, beside the line.</summary>
    public const float ValveLead = 3.5f;
    public const float ValveOut = 1.5f;
    const float PostOut = 1.35f, PostSize = 0.24f;

    /// <summary>The party line's S under the stream.</summary>
    public float S { get; init; }
    /// <summary>The valve is broken: no more pours.</summary>
    public bool Quenched { get; private set; }
    /// <summary>Its valve's wire name ("valve:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";
    /// <summary>The phase now (Dry while quenched).</summary>
    public PourPhase Phase { get; private set; } = PourPhase.Dry;
    public QuenchValve Valve { get; private set; } = null!;

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.27f, 0.18f, 0.1f), Roughness = 0.9f };
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.15f, 0.13f, 0.12f), Metallic = 0.75f, Roughness = 0.55f };
    // Lava004, glowing by its emission map (made in Place): the lip's glow swells with the warning, the stream and puddle burn at 4
    private StandardMaterial3D _lip = null!, _molten = null!;
    private MeshInstance3D _stream = null!, _puddle = null!;
    private OmniLight3D _light = null!;

    /// <summary>Stand over the line at <paramref name="frame"/> (the track's frame at <see cref="S"/>); the valve goes on the side <paramref name="valveSide"/> (+1 the frame's +X) at <paramref name="valveFrame"/>.</summary>
    public void Place(Transform3D frame, Transform3D valveFrame, float valveSide)
    {
        _lip = DungeonBuilder.Lava(new Color(0.3f, 0.1f, 0.04f), new Color(1f, 0.4f, 0.08f), 0.4f);
        _molten = DungeonBuilder.Lava(new Color(1f, 0.45f, 0.08f), new Color(1f, 0.42f, 0.06f), 4f);
        GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin);
        var height = TroughHeight + 0.5f;
        foreach (var side in new[] { -1f, 1f })
        {
            var post = new StaticBody3D { Name = side < 0 ? "PostL" : "PostR", CollisionLayer = 1, CollisionMask = 0, Position = new Vector3(side * PostOut, height / 2f, 0) };
            post.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(PostSize, height, PostSize) } });
            post.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(PostSize, height, PostSize) }, MaterialOverride = _wood });
            AddChild(post);
        }
        // the trough: an iron box across the line, its lip glowing, a spout over the middle
        AddChild(new MeshInstance3D { Name = "Trough", Mesh = new BoxMesh { Size = new Vector3(PostOut * 2f + 0.5f, 0.4f, 0.8f) }, MaterialOverride = _iron, Position = new Vector3(0, TroughHeight + 0.2f, 0) });
        AddChild(new MeshInstance3D { Name = "Lip", Mesh = new BoxMesh { Size = new Vector3(PostOut * 2f + 0.3f, 0.06f, 0.6f) }, MaterialOverride = _lip, Position = new Vector3(0, TroughHeight + 0.42f, 0) });
        AddChild(new MeshInstance3D { Name = "Spout", Mesh = new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.2f, Height = 0.3f, RadialSegments = 10 }, MaterialOverride = _iron, Position = new Vector3(0, TroughHeight - 0.1f, 0) });
        _stream = new MeshInstance3D { Name = "Stream", Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = TroughHeight - 0.25f, RadialSegments = 10 }, MaterialOverride = _molten, Position = new Vector3(0, (TroughHeight - 0.25f) / 2f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_stream);
        _puddle = new MeshInstance3D { Name = "Puddle", Mesh = new CylinderMesh { TopRadius = Zone + 0.2f, BottomRadius = Zone + 0.2f, Height = 0.04f, RadialSegments = 16 }, MaterialOverride = _molten, Position = new Vector3(0, 0.27f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_puddle);
        _light = new OmniLight3D { Name = "Glow", OmniRange = 7f, LightEnergy = 0.3f, LightColor = new Color(1f, 0.45f, 0.1f), ShadowEnabled = false, Position = new Vector3(0, TroughHeight - 0.4f, 0) };
        AddChild(_light);

        // the feed pipe: down the valve-side post and along the line to the valve
        var pipeTop = new Vector3(valveSide * (PostOut + 0.2f), TroughHeight + 0.2f, 0);
        AddChild(new MeshInstance3D { Name = "Pipe", Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = TroughHeight + 0.2f - QuenchValve.WheelHeight, RadialSegments = 8 }, MaterialOverride = _iron, Position = new Vector3(pipeTop.X, (TroughHeight + 0.2f + QuenchValve.WheelHeight) / 2f, 0) });
        var valveLocal = GlobalTransform.AffineInverse() * (valveFrame.Origin + valveFrame.Basis.X.Normalized() * (valveSide * ValveOut));
        var run = new Vector3(valveLocal.X, QuenchValve.WheelHeight, valveLocal.Z) - new Vector3(pipeTop.X, QuenchValve.WheelHeight, 0);
        if (run.Length() > 0.1f)
        {
            var pipe = new MeshInstance3D { Name = "FeedPipe", Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = run.Length(), RadialSegments = 8 }, MaterialOverride = _iron };
            AddChild(pipe);
            var dir = run.Normalized();
            var x = Vector3.Up.Cross(dir).Normalized();
            pipe.Transform = new Transform3D(new Basis(x, dir, x.Cross(dir)), new Vector3(pipeTop.X, QuenchValve.WheelHeight, 0) + run / 2f);
        }
        Valve = new QuenchValve { Name = "QuenchValve", NetRef = NetRef, Pour = this };
        AddChild(Valve);
        Valve.GlobalTransform = new Transform3D(valveFrame.Basis.Orthonormalized(), valveFrame.Origin + valveFrame.Basis.X.Normalized() * (valveSide * ValveOut));
        Valve.Broken += () => Quenched = true;
    }

    /// <summary>Broken without the burst (a load): the pour is out for good and the valve stands burst.</summary>
    public void Quench()
    {
        Quenched = true;
        Valve.BreakQuietly();
    }

    /// <summary>Whole again, pouring on its clock (a wreck or a load undid the ride).</summary>
    public void Rebuild()
    {
        Quenched = false;
        Valve.Rebuild();
    }

    /// <summary>Client: whole again as on the server (the valve's "rebuilt").</summary>
    internal void ShowRebuilt()
    {
        Quenched = false;
        Valve.ShowRebuilt();
    }

    /// <summary>
    /// The pour's cues (its hiss, its stream's loop) carry this far: a walker near it hears it, a cart held at stop 3 does not
    /// (<see cref="SetPieceRules.CueRange"/> of its distance to stop 3, set at the floor's build on every role; 30 m until then).
    /// </summary>
    public float CueRange { get; set; } = SetPieceRules.MaxCueRange;
    /// <summary>A phase seen this late (a floor clock that jumped: a load, a client's catch-up) is shown silently.</summary>
    const int LateTicks = 30;
    private PourPhase _heard;
    private int _heardTick = int.MinValue;
    private AudioStreamPlayer3D? _loop;

    public override void _Process(double delta)
    {
        if (_stream == null || Main.Instance?.Builder is not { } builder) return;
        float into = 0f;
        Phase = Quenched ? PourPhase.Dry : SetPieceRules.Pour(builder.FloorTick, out into);
        Hear(Phase, builder.FloorTick);
        var pouring = Phase == PourPhase.Pouring;
        _stream.Visible = pouring;
        _puddle.Visible = pouring;
        var warn = Phase == PourPhase.Warn ? Mathf.Clamp(into / 1.5f, 0f, 1f) : 0f;
        _lip.EmissionEnergyMultiplier = Quenched ? 0.05f : pouring ? 4f : 0.4f + 3.6f * warn * (0.8f + 0.2f * Mathf.Sin(into * 20f));
        _light.LightEnergy = Quenched ? 0.05f : pouring ? 3.5f : 0.3f + 2.5f * warn;
    }

    /// <summary>
    /// The hiss as the warning starts, at the trough, and the molten stream's loop while it pours (its own player, every role but a dedicated
    /// server, armed ride or not). The first frame after a build or a load, and a floor clock that jumped, only note the phase.
    /// </summary>
    private void Hear(PourPhase phase, int tick)
    {
        if (NetSession.IsServer) return;
        var quiet = _heardTick == int.MinValue || tick < _heardTick || tick - _heardTick > LateTicks;
        var was = _heard;
        _heard = phase;
        _heardTick = tick;
        if (!quiet && phase == PourPhase.Warn && was != PourPhase.Warn) RideAudio.At("pour_hiss", GlobalPosition + Vector3.Up * TroughHeight, 0f, CueRange);
        if (phase == PourPhase.Pouring)
        {
            if (_loop == null)
            {
                _loop = new AudioStreamPlayer3D { Name = "MoltenLoop", Bus = "SFX", MaxDistance = CueRange, UnitSize = 6f, VolumeDb = -4f, Position = new Vector3(0, 1.5f, 0) };
                AddChild(_loop);
            }
            _loop.Stream ??= Audio.AudioManager.Instance?.Stream("molten_loop");
            if (!_loop.Playing && _loop.Stream != null) _loop.Play();
        }
        else if (_loop is { Playing: true }) _loop.Stop();
    }
}

/// <summary>
/// The pour's quench valve: an iron wheel on the feed pipe beside the line, on layer 4 only (a small prop: a blade, a shove or an
/// arrow finds it and nothing blocks the sight ray to it). Any hit bursts it: steam, the pour gutters out for good. The server
/// judges it and tells "broken" and "rebuilt" (<see cref="NetRef"/>); a client's copy only shows them (<see cref="ShowBroken"/>).
/// </summary>
public partial class QuenchValve : StaticBody3D, ICombatTarget, ISoundPoint
{
    /// <summary>Where the burst is heard, on every role: at the wheel.</summary>
    public Vector3 SoundPoint => GlobalPosition + Vector3.Up * WheelHeight;

    /// <summary>The wheel's height above the track: about a seated hero's hand.</summary>
    public const float WheelHeight = 1.1f;

    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.15f, 0.13f, 0.12f), Metallic = 0.75f, Roughness = 0.55f };
    private readonly StandardMaterial3D _wheel = new() { AlbedoColor = new Color(0.6f, 0.1f, 0.06f), Metallic = 0.4f, Roughness = 0.5f, EmissionEnabled = true, Emission = new Color(0.9f, 0.15f, 0.05f), EmissionEnergyMultiplier = 0.6f };
    private Node3D _wheelNode = null!;
    private CollisionShape3D _shape = null!;

    /// <summary>It burst (told once per build).</summary>
    public event Action? Broken;
    /// <summary>Its wire name ("valve:{piece}"); empty: not told.</summary>
    public string NetRef { get; init; } = "";
    /// <summary>The pour it feeds.</summary>
    internal MoltenPour? Pour { get; init; }

    public QuenchValve()
    {
        CollisionLayer = 4; // what a hero's melee cast, blade sweep and arrows look for; not solid, so it never blocks a sight ray
        CollisionMask = 0;
    }

    public override void _Ready()
    {
        _shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.55f, 0.6f, 0.55f) }, Position = new Vector3(0, WheelHeight, 0) }; // bigger than it looks: easier to hit from a moving cart
        AddChild(_shape);
        AddChild(new MeshInstance3D { Name = "Stand", Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = WheelHeight, RadialSegments = 8 }, MaterialOverride = _iron, Position = new Vector3(0, WheelHeight / 2f, 0) });
        AddChild(new MeshInstance3D { Name = "Body", Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.22f, 0.22f) }, MaterialOverride = _iron, Position = new Vector3(0, WheelHeight, 0) });
        _wheelNode = new Node3D { Name = "Wheel", Position = new Vector3(0, WheelHeight + 0.18f, 0) };
        AddChild(_wheelNode);
        _wheelNode.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.15f, OuterRadius = 0.2f, Rings = 16, RingSegments = 6 }, MaterialOverride = _wheel });
        _wheelNode.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.03f, 0.03f) }, MaterialOverride = _wheel });
        _wheelNode.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.36f) }, MaterialOverride = _wheel });
        AddChild(new OmniLight3D { Name = "Tell", OmniRange = 1.6f, LightEnergy = 0.5f, LightColor = new Color(1f, 0.25f, 0.1f), ShadowEnabled = false, Position = new Vector3(0, WheelHeight + 0.3f, 0) });
    }

    public string DisplayName => "quench valve";
    public bool IsAlive { get; private set; } = true;
    public bool IsUnaware => false;
    public bool IsCreature => false;
    public void ApplyStatus(StatusApplication status) { }

    /// <summary>Any hit bursts it (deferred: a hit can land inside a physics query).</summary>
    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (!IsAlive || NetSession.IsClient) return DamageResult.Miss(attack.Type);
        IsAlive = false;
        GD.Print($"[rails] {attackerName} hits the quench valve");
        Callable.From(Burst).CallDeferred();
        return new DamageResult(true, false, false, false, 1, attack.Type, Array.Empty<StatusApplication>());
    }

    private void Burst()
    {
        BurstLook(steam: true);
        EventBus.Instance.Log("The quench valve bursts — the pour gutters out.");
        RideAudio.At("steam_burst", SoundPoint); // a client's comes from its "broken" (RideAudio.OnProp), at the same point
        Broken?.Invoke();
        Tell("broken");
    }

    /// <summary>Burst to look at: nothing to hit, the wheel gone, and (<paramref name="steam"/>) a cloud of steam.</summary>
    private void BurstLook(bool steam)
    {
        IsAlive = false;
        if (_shape == null) return;
        _shape.Disabled = true;
        CollisionLayer = 0;
        _wheelNode.Visible = false;
        if (steam && GetParent() is Node3D parent && IsInsideTree())
        {
            var cloud = new GpuParticles3D
            {
                Name = "Steam", Amount = 60, Lifetime = 1.6, OneShot = true, Explosiveness = 0.85f, Emitting = true,
                ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 35f, InitialVelocityMin = 1.5f, InitialVelocityMax = 3.5f, Gravity = new Vector3(0, 0.6f, 0), ScaleMin = 0.4f, ScaleMax = 1.2f },
                DrawPass1 = new QuadMesh { Size = new Vector2(0.8f, 0.8f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.85f, 0.85f, 0.25f), AlbedoTexture = Props.Fire.Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            };
            parent.AddChild(cloud);
            cloud.GlobalPosition = GlobalPosition + Vector3.Up * (WheelHeight + 0.2f);
            GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(cloud)) cloud.QueueFree(); };
        }
    }

    /// <summary>Burst without steam, sound or a message (a load puts it back as it was): told as the quiet "burst-quiet", which no peer hears.</summary>
    public void BreakQuietly()
    {
        var was = IsAlive;
        BurstLook(steam: false);
        if (was) Tell("burst-quiet");
    }

    /// <summary>Whole again.</summary>
    public void Rebuild()
    {
        var was = IsAlive;
        Mend();
        if (!was) Tell("rebuilt");
    }

    /// <summary>Client: burst as on the server, with its steam (<paramref name="steam"/>) but no sound or message; the pour is quenched.</summary>
    public void ShowBroken(bool steam = true)
    {
        if (IsAlive) BurstLook(steam);
        Broken?.Invoke(); // the pour is quenched
    }

    /// <summary>Client: whole again, the pour back on its clock.</summary>
    public void ShowRebuilt()
    {
        if (!IsAlive) Mend();
        if (Pour is { Quenched: true } pour) pour.ShowRebuilt(); // which comes back here with the valve whole
    }

    private void Mend()
    {
        IsAlive = true;
        if (_shape == null) return;
        _shape.Disabled = false;
        CollisionLayer = 4;
        _wheelNode.Visible = true;
    }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }
}
