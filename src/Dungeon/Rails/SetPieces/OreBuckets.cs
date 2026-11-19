using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ore buckets on a cable over the party line, hung between two gantries about <see cref="Span"/> metres apart. While their
/// ride is armed (<see cref="Tick"/>), one bucket tips every <see cref="Every"/> seconds: it creaks and glows for
/// <see cref="Creak"/> seconds, then pours a <see cref="CinderPatch"/> onto the line beneath it, which burns for
/// <see cref="PatchSeconds"/>. The <see cref="RailEncounterController"/> judges the party's cart against <see cref="Patches"/>.
/// The schedule is <see cref="SetPieceRules.BucketStateAt"/>: the server ticks it on its own clock from <see cref="Arm"/>, and a client's
/// copy runs the same schedule from the server's arm time (<see cref="ClientTick"/>), pouring visual-only patches (a client judges nothing).
/// </summary>
public partial class OreBuckets : Node3D
{
    public const float Span = 10f;
    public const float Every = SetPieceRules.BucketEvery;
    public const float Creak = SetPieceRules.BucketCreak;
    public const float PatchSeconds = SetPieceRules.BucketPatchSeconds;
    /// <summary>The cable hangs this high above the track; a bucket's bottom is about half a metre below it, clear of seated heads.</summary>
    public const float CableHeight = 2.95f;
    const float BucketSize = 0.42f;
    const float TipBack = 1f; // a tipped bucket rights itself this long after it pours
    const float GantryOut = 1.35f;
    /// <summary>The buckets' places along the line, from the middle; they tip in <see cref="Order"/>.</summary>
    static readonly float[] Offsets = { -3.75f, -1.25f, 1.25f, 3.75f };
    static readonly int[] Order = { 1, 3, 0, 2 };

    /// <summary>The party line's S at the middle of the cable.</summary>
    public float S { get; init; }
    public int Floor { get; init; } = 1;
    /// <summary>The live cinder patches on the line.</summary>
    public List<CinderPatch> Patches { get; } = new();
    /// <summary>Buckets poured since the ride armed (tests and the smoke read it).</summary>
    public int Poured { get; private set; }

    private readonly Node3D[] _pivots = new Node3D[4];
    private readonly Transform3D[] _hang = new Transform3D[4]; // each pivot's resting local transform
    private readonly MeshInstance3D[] _bodies = new MeshInstance3D[4];
    private readonly OmniLight3D[] _glows = new OmniLight3D[4];
    private readonly float[] _tipped = new float[4]; // seconds since each poured, or −1
    private readonly float[] _bucketS = new float[4];
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.2f, 0.17f, 0.15f), Metallic = 0.6f, Roughness = 0.6f };
    private readonly StandardMaterial3D _hot = new() { AlbedoColor = new Color(0.5f, 0.15f, 0.05f), Metallic = 0.4f, EmissionEnabled = true, Emission = new Color(1f, 0.4f, 0.08f), EmissionEnergyMultiplier = 2.5f };
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.27f, 0.18f, 0.1f), Roughness = 0.9f };
    private TrackSampler _track = null!;
    private float _clock;
    private int _next;
    private int _creaking = -1;
    private bool _armed;

    /// <summary>Hang the cable over <paramref name="track"/> (the party line) around <see cref="S"/>. After adding it to the tree.</summary>
    public void Place(TrackSampler track)
    {
        _track = track;
        GlobalTransform = new Transform3D(Basis.Identity, track.Point(S));
        var prev = Hang(S - Span / 2f - 0.5f, true);
        for (var i = 0; i < 4; i++)
        {
            _bucketS[i] = S + Offsets[i];
            var at = Hang(_bucketS[i], false);
            Cable(prev, at);
            prev = at;
            var frame = track.Frame(_bucketS[i]);
            var pivot = new Node3D { Name = $"Bucket{i}" };
            AddChild(pivot);
            pivot.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), at);
            pivot.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 0.2f, RadialSegments = 6 }, MaterialOverride = _iron, Position = new Vector3(0, -0.1f, 0) });
            var body = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = BucketSize / 2f, BottomRadius = BucketSize * 0.38f, Height = BucketSize, RadialSegments = 10 }, MaterialOverride = _iron, Position = new Vector3(0, -0.2f - BucketSize / 2f, 0) };
            pivot.AddChild(body);
            var glow = new OmniLight3D { OmniRange = 3f, LightEnergy = 0f, LightColor = new Color(1f, 0.45f, 0.1f), ShadowEnabled = false, Position = new Vector3(0, -0.5f, 0) };
            pivot.AddChild(glow);
            _pivots[i] = pivot;
            _hang[i] = pivot.Transform;
            _bodies[i] = body;
            _glows[i] = glow;
            _tipped[i] = -1f;
        }
        Cable(prev, Hang(S + Span / 2f + 0.5f, true));
    }

    /// <summary>The cable's point above the line at <paramref name="s"/>; a gantry (two posts and a cap, no collision overhead) at the ends.</summary>
    private Vector3 Hang(float s, bool gantry)
    {
        var frame = _track.Frame(s);
        var at = frame.Origin + frame.Basis.Y.Normalized() * CableHeight;
        if (!gantry) return at;
        var x = frame.Basis.X.Normalized();
        var up = frame.Basis.Y.Normalized();
        var height = CableHeight + 0.2f;
        foreach (var side in new[] { -1f, 1f })
        {
            var post = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            post.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.22f, height, 0.22f) } });
            post.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.22f, height, 0.22f) }, MaterialOverride = _wood });
            AddChild(post);
            post.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + x * (side * GantryOut) + up * (height / 2f));
        }
        var cap = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(GantryOut * 2f + 0.4f, 0.2f, 0.22f) }, MaterialOverride = _wood };
        AddChild(cap);
        cap.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + up * (height + 0.1f));
        return at;
    }

    private void Cable(Vector3 a, Vector3 b)
    {
        var length = a.DistanceTo(b);
        if (length < 0.05f) return;
        var mesh = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = length, RadialSegments = 6 }, MaterialOverride = _iron };
        AddChild(mesh);
        var dir = (b - a) / length;
        var side = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        var x = side.Cross(dir).Normalized();
        mesh.GlobalTransform = new Transform3D(new Basis(x, dir, x.Cross(dir)), (a + b) / 2f); // the cylinder's axis (Y) along the cable
    }

    /// <summary>The ride armed: the buckets start their round from the first, nothing tipped.</summary>
    public void Arm()
    {
        Clear();
        _armed = true;
    }

    /// <summary>The ride is over or undone: the patches go, the buckets hang still.</summary>
    public void Clear()
    {
        _armed = false;
        _clock = 0f;
        _next = 0;
        _creaking = -1;
        Poured = 0;
        foreach (var p in Patches) if (IsInstanceValid(p)) p.QueueFree();
        Patches.Clear();
        for (var i = 0; i < 4; i++)
        {
            _tipped[i] = -1f;
            Look(i, false);
            if (_pivots[i] != null) _pivots[i].Transform = _hang[i]; // hanging still
        }
    }

    /// <summary>One physics tick while the ride is armed (server): a bucket creaks, pours, rights itself; the old patches burn out.</summary>
    public void Tick(float dt)
    {
        if (!_armed || _track == null || !_track.Valid) return;
        _clock += dt;
        Step(dt, _clock);
    }

    /// <summary>
    /// A client's copy, each rendered frame while its ride is armed here: the server's schedule at <paramref name="secondsSinceArmed"/>
    /// (the server's clock now less its arm time). Buckets whose patch would already have burnt out (a late joiner, a long frame) are
    /// passed over silently; the rest creak and pour as on the server. Its patches only show: a client's cinders burn nobody.
    /// </summary>
    public void ClientTick(double secondsSinceArmed, float dt)
    {
        if (!_armed || _track == null || !_track.Valid) return;
        while (_creaking < 0 && SetPieceRules.BucketStateAt(secondsSinceArmed, _next) is { Phase: BucketPhase.Poured } gone && gone.SecondsIn >= PatchSeconds) _next++;
        Step(dt, secondsSinceArmed);
    }

    /// <summary>The buckets' work for one step at <paramref name="clock"/> seconds since the arming (the server's <see cref="Tick"/> and a client's <see cref="ClientTick"/>).</summary>
    private void Step(float dt, double clock)
    {
        for (var i = Patches.Count - 1; i >= 0; i--)
        {
            var p = Patches[i];
            if (!IsInstanceValid(p)) { Patches.RemoveAt(i); continue; }
            p.Life -= dt;
            if (p.Life <= 0f) { p.QueueFree(); Patches.RemoveAt(i); }
        }
        for (var i = 0; i < 4; i++)
            if (_tipped[i] >= 0f && (_tipped[i] += dt) >= TipBack) _tipped[i] = -1f;
        // bucket n creaks from n·Every and pours Creak seconds later (SetPieceRules.BucketStateAt: the same schedule on every peer)
        if (_creaking < 0 && SetPieceRules.BucketStateAt(clock, _next).Phase != BucketPhase.Hanging)
        {
            _creaking = Order[_next % Order.Length];
            Look(_creaking, true);
            if (!NetSession.IsClient) EventBus.Instance.PlaySfx("bucket_creak"); // placeholder: plan 6 adds the sound (a server's sfx reach every client)
        }
        if (_creaking >= 0 && SetPieceRules.BucketStateAt(clock, _next) is { Phase: BucketPhase.Poured } poured)
        {
            Pour(_creaking, (float)poured.SecondsIn);
            Look(_creaking, false);
            _creaking = -1;
            _next++;
            if (NetSession.Instance?.TestRide == true) GD.Print(FormattableString.Invariant($"[{(NetSession.IsClient ? "net" : "net-server")}] ride bucket pour n={_next} t={(NetSession.IsClient ? NetSession.Instance.Client?.ServerNow ?? 0 : NetSession.Instance.Now):0.000}"));
        }
    }

    /// <summary>Bucket <paramref name="i"/> pours <paramref name="late"/> seconds after its schedule said (0 on the server's tick; a client's frame or catch-up may be later).</summary>
    private void Pour(int i, float late = 0f)
    {
        _tipped[i] = NetSession.IsClient ? Mathf.Min(late, TipBack) : 0f;
        Poured++;
        var frame = _track.Frame(_bucketS[i]);
        var patch = new CinderPatch { Name = $"CinderPatch{Poured}", S = _bucketS[i], Life = NetSession.IsClient ? PatchSeconds - late : PatchSeconds, Floor = Floor };
        AddChild(patch);
        patch.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin);
        Patches.Add(patch);
        if (!NetSession.IsClient) EventBus.Instance.PlaySfx("cinder_pour"); // placeholder: plan 6 adds the sound
    }

    private void Look(int i, bool creaking)
    {
        if (_bodies[i] == null) return;
        _bodies[i].MaterialOverride = creaking ? _hot : _iron;
        _glows[i].LightEnergy = creaking ? 1.6f : 0f;
    }

    public override void _Process(double delta)
    {
        if (!_armed || _pivots[0] == null) return; // still while the ride is not armed (Clear hangs them straight)
        var t = (float)Time.GetTicksMsec() / 1000f;
        for (var i = 0; i < 4; i++)
        {
            // a creaking bucket rocks on its hook; a poured one hangs tipped over, then swings back
            float angle = i == _creaking ? 0.18f * Mathf.Sin(t * 14f) : _tipped[i] >= 0f ? 1.9f * (1f - _tipped[i] / TipBack) : 0f;
            _pivots[i].Transform = new Transform3D(_hang[i].Basis * new Basis(Vector3.Back, angle), _hang[i].Origin); // about the line: it tips over sideways
        }
    }
}

/// <summary>
/// Hot cinders poured onto the line from an ore bucket: scorched ground and low flames 2 m across, burning walkers who stand
/// in it as a <see cref="FloorFire"/> does (seated riders are the <see cref="RailEncounterController"/>'s to burn, once per patch).
/// </summary>
public partial class CinderPatch : Area3D
{
    public const float Radius = 1f;
    const float BurnEvery = 0.8f;

    /// <summary>The party line's S under it.</summary>
    public float S { get; init; }
    public int Floor { get; init; } = 1;
    /// <summary>Seconds left to burn (the buckets count it down).</summary>
    public float Life { get; set; }
    /// <summary>The party's cart has been burned by it.</summary>
    public bool HitCart { get; set; }
    private float _t;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 6; // adventurers and enemies, as a floor fire sees them
        AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = Radius, Height = 1.6f }, Position = new Vector3(0, 0.8f, 0) });
        AddChild(new MeshInstance3D
        {
            Name = "Scorch",
            Mesh = new CylinderMesh { TopRadius = Radius, BottomRadius = Radius, Height = 0.03f, RadialSegments = 14 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.06f, 0.02f), EmissionEnabled = true, Emission = new Color(0.9f, 0.3f, 0.05f), EmissionEnergyMultiplier = 1.2f, Roughness = 1f },
            Position = new Vector3(0, 0.3f, 0),
        });
        var flames = Fire.Create(0.55f, 1.1f, 2f, 6f);
        flames.Name = "Flames";
        flames.Position = new Vector3(0, 0.3f, 0);
        AddChild(flames);
        _t = BurnEvery * 0.5f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        _t -= (float)delta;
        if (_t > 0f) return;
        _t = BurnEvery;
        var attack = TrapRules.FloorFire(Floor);
        foreach (var body in GetOverlappingBodies())
        {
            if (body is Adventurer { Downed: false, Seated: false } adv) adv.ReceiveAttack(Trap.TrapStats, attack, "A cinder");
            else if (body is Enemy { IsAlive: true, Carried: false } enemy && Main.Instance != null) enemy.ReceiveAttack(Trap.TrapStats, attack, new HitContext(), Main.Instance.Party.Rng, "a cinder");
        }
    }
}
