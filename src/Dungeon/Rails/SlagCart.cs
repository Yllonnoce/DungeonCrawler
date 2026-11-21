using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A slag cart on the enemy line: a rust-red tub heaped with slag and glowing coals, with up to three creatures riding on
/// the heap (carried: <see cref="Enemy.Carry"/>). A dumb prop like <see cref="MineCart"/>: the
/// <see cref="RailEncounterController"/> paces its <see cref="Run"/> on the enemy track, poses it and its riders, and judges
/// what its front runs over. A glowing <see cref="BrakeShoe"/> on the side that faces the party's lane breaks to one hit and
/// derails it (<see cref="Derail"/>). Its origin sits on the track; forward is −Z. A client builds the same cart as dressing from
/// the server's cart state (RailNet): posed by its follower, its riders bound by net id (<see cref="BoardAt"/>), its derail shown
/// (<see cref="ShowDerailed"/>); it judges, paces and warns nothing, and its shoe never breaks to a client's hit.
/// </summary>
public partial class SlagCart : AnimatableBody3D, ICartBody
{
    /// <summary>A hero mangled by a slag cart carries this as MangledBy: no hazard has it, so it always counts as armed.</summary>
    public const int HazardIndex = 1001;
    public const string SourceName = RunOverRules.SlagCartSource;
    public const int MaxRiders = 3;

    public const float TubBottom = 0.45f;
    public const float TubHeight = 0.7f;
    public const float TubWidth = MineCart.TubWidth;
    public const float TubLength = MineCart.TubLength;
    /// <summary>Riders stand on the slag heap, this high above the track.</summary>
    public const float StandOn = TubBottom + TubHeight - 0.1f;
    const float Wall = 0.06f;
    const float WheelRadius = 0.15f;
    const float RailTop = 0.24f; // RailTrack: sleeper 0.12 + rail 0.12
    const float TumbleSeconds = 2f;

    /// <summary>The riders' spots in the cart's space (x, z): front, middle, back, staggered across the heap.</summary>
    static readonly Vector2[] Spots = { new(-0.25f, -0.62f), new(0.25f, 0f), new(-0.2f, 0.62f) };

    private readonly StandardMaterial3D _rust = new() { AlbedoColor = new Color(0.4f, 0.13f, 0.07f), Metallic = 0.45f, Roughness = 0.8f };
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.14f, 0.12f, 0.11f), Metallic = 0.8f, Roughness = 0.55f };
    private readonly StandardMaterial3D _slag = new() { AlbedoColor = new Color(0.1f, 0.09f, 0.08f), Roughness = 1f };
    private StandardMaterial3D _coal = null!; // Lava004, glowing (made in Build)

    /// <summary>The cart on the enemy track (<see cref="Setup"/>): its state, stepping and pose.</summary>
    public CartRun Run { get; private set; } = null!;
    /// <summary>Metres ahead (+) or behind (−) the party's cart it keeps pace at.</summary>
    public float Offset { get; private set; }
    /// <summary>The encounter it came from (its plan id), or "traffic".</summary>
    public string EncounterId { get; init; } = "";
    /// <summary>Its id on the wire (<see cref="Net.CartIds"/>): the server's count of the floor's carts.</summary>
    public int CartId { get; set; }
    /// <summary>The server's watchdog window for this cart (<see cref="CartWatchdog"/>): a ride's pacing cart or the traffic cart.</summary>
    internal CartWatchdog.Window Watch;
    /// <summary>A ride's slag cart, or a traffic cart.</summary>
    public Net.CartKind Kind => EncounterId == "traffic" ? Net.CartKind.Traffic : Net.CartKind.Slag;
    /// <summary>The side (+1 its local +X, −1 its −X) that faces the party's lane: the brake shoe is there.</summary>
    public int InnerSide { get; init; } = 1;
    /// <summary>The creatures riding it, in spot order (a dead one rides on until its corpse is gone).</summary>
    public List<Enemy> Aboard { get; } = new();
    /// <summary>Off the rails: it is being freed and its run is done.</summary>
    public bool Derailed { get; private set; }
    /// <summary>When each body was last run over (<see cref="RunOverSweep.Judge"/> keeps it).</summary>
    public Dictionary<ulong, double> HitAt { get; } = new();
    public BrakeShoe Shoe { get; private set; } = null!;

    // ICartBody: its riders are carried creatures, not ICartRiders, so the seat book stays empty
    public Dictionary<int, ICartRider> Riders { get; } = new();
    public CartSeats Seats { get; } = new();
    public event Action<ICartRider, bool>? RiderChanged;
    public float Speed { get; set; }
    public float Curvature { get; set; }
    public Vector3 Velocity => -GlobalTransform.Basis.Z * Speed;
    public Transform3D SeatTransform(int seat) => RiderSpot(seat);
    public void Admit(ICartRider rider) => RiderChanged?.Invoke(rider, true);
    public void Release(ICartRider rider) { }

    /// <summary>Its sound (a child once built): fed by its run on the server and in single player, by its follower on a client.</summary>
    public CartSound? Sound { get; private set; }
    private Node3D? _body;
    private bool _built;

    public SlagCart()
    {
        SyncToPhysics = false;
        CollisionLayer = 8; // never solid: it runs a body over rather than shoving it (the sweep judges the hit)
        CollisionMask = 0;
    }

    /// <summary>Put the cart on <paramref name="track"/> in <paramref name="state"/>, running toward higher S to the line's end. Before adding it to the tree.</summary>
    public void Setup(TrackSampler track, CartState state, float offset)
    {
        Run = new CartRun(track, this, state, 1) { Limit = track.Length };
        Offset = offset;
    }

    public override void _Ready() => Build();

    /// <summary>
    /// Build one cart off the tree and free it, as the floor is built (under the loading card): the ride's first slag cart then
    /// spawns without its type's first-use cost (~5 ms of JIT and static set-up, on the physics tick it appears in).
    /// </summary>
    public static void Prewarm(TrackSampler track)
    {
        var cart = new SlagCart { Name = "SlagCart_prewarm" };
        cart.Setup(track, new CartState(0f, 0f, CartMode.Rolling, CartRules.MaxPlating), 0f);
        cart.Build();
        cart.Free(); // off the tree: its children go with it
    }

    /// <summary>Where rider <paramref name="i"/> stands, in world space (−Z forward): on the heap at its spot.</summary>
    public Transform3D RiderSpot(int i)
    {
        var spot = Spots[Math.Clamp(i, 0, Spots.Length - 1)];
        return GlobalTransform * new Transform3D(Basis.Identity, new Vector3(spot.X, StandOn, spot.Y));
    }

    /// <summary>Carry <paramref name="enemy"/> at the next free spot. False when all three are taken.</summary>
    public bool Board(Enemy enemy) => Aboard.Count < MaxRiders && BoardAt(enemy, Aboard.Count);

    /// <summary>Who was boarded at each spot (spots are by boarding order; a rider leaving the list keeps no one else's spot).</summary>
    private readonly Enemy?[] _bySpot = new Enemy?[MaxRiders];

    /// <summary>Carry <paramref name="enemy"/> at spot <paramref name="spot"/> (a client binds the server's riders where the server has them).</summary>
    public bool BoardAt(Enemy enemy, int spot)
    {
        if (spot < 0 || spot >= MaxRiders) return false;
        if (!Aboard.Contains(enemy)) Aboard.Add(enemy);
        _bySpot[spot] = enemy;
        enemy.Carry(this, () => RiderSpot(spot));
        return true;
    }

    /// <summary>The net id of the creature riding spot <paramref name="spot"/> (alive or a corpse), or −1 when none rides it.</summary>
    public int RiderNetIdAt(int spot)
    {
        var e = spot >= 0 && spot < MaxRiders ? _bySpot[spot] : null;
        return e != null && IsInstanceValid(e) && !e.IsQueuedForDeletion() && e.Carrier == this && Aboard.Contains(e) ? e.NetId : -1;
    }

    /// <summary>The creature bound at spot <paramref name="spot"/> on this cart, or null.</summary>
    public Enemy? RiderAt(int spot)
    {
        var e = spot >= 0 && spot < MaxRiders ? _bySpot[spot] : null;
        return e != null && IsInstanceValid(e) && e.Carrier == this ? e : null;
    }

    /// <summary>Client: the creature at spot <paramref name="spot"/> no longer rides here (the server's state stopped naming it): it stands where it is and the snapshots move it.</summary>
    public void Unbind(int spot)
    {
        if (spot < 0 || spot >= MaxRiders || _bySpot[spot] is not { } e) return;
        _bySpot[spot] = null;
        Aboard.Remove(e);
        if (IsInstanceValid(e) && e.Carrier == this) e.Drop(e.GlobalPosition);
    }

    /// <summary>Pose the riders at their spots (after the cart is posed); a corpse that has been freed leaves the list.</summary>
    public void FollowRiders()
    {
        for (var i = Aboard.Count - 1; i >= 0; i--)
        {
            var e = Aboard[i];
            if (!IsInstanceValid(e) || e.IsQueuedForDeletion()) { Aboard.RemoveAt(i); continue; } // spots are by index: a later rider keeps its own
            if (e.Carrier == this) e.FollowCarrier();
        }
    }

    /// <summary>
    /// The brake shoe broke: the cart jumps the rails in a shower of sparks, its living riders are thrown under it and die,
    /// the tub tumbles off the line (a cosmetic copy) and the cart is freed. Server only.
    /// </summary>
    public void Derail()
    {
        if (Derailed || NetSession.IsClient || IsQueuedForDeletion() || !IsInsideTree() || GetParent() is not Node3D root) return; // a derail queued on a cart already freed does nothing
        Derailed = true;
        var at = GlobalPosition;
        Sparks(root, at + Vector3.Up * 0.4f);
        Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        RideAudio.At("cart_crash", at); // its sound goes with it in this call: the crash is a one-shot
        Props.Tremor.ShakeOnly(0.03f, 0.4f);
        EventBus.Instance.Log("The slag cart derails!"); // relayed to clients as a log line
        Adventure.AdventureDirector.Instance?.RailTaunt(Adventure.Objectives.TauntTrigger.SlagDerailed);
        var groundY = Run.Track.Point(Run.State.S).Y;
        foreach (var rider in Aboard)
        {
            if (!IsInstanceValid(rider)) continue;
            var p = rider.GlobalPosition;
            rider.Drop(new Vector3(p.X, groundY + 0.05f, p.Z)); // down on the line where it stood
            if (!rider.IsAlive) continue;
            EventBus.Instance.Log($"{rider.DisplayName} is thrown under the cart.");
            rider.KillByEnvironment(SourceName, HazardDamage.RunOver, quiet: true); // the line above tells it
        }
        Aboard.Clear();
        Tumble(root);
        Visible = false;
        CollisionLayer = 0;
        Shoe.Break();
        QueueFree();
    }

    /// <summary>
    /// Client: the server derailed this cart. The same sparks, thud, crash, tremor and tumble; its riders are set down on the line where they
    /// stand (the server kills them: their deaths come as their own messages); then it is freed. No log line (the server's comes).
    /// </summary>
    public void ShowDerailed()
    {
        if (Derailed || IsQueuedForDeletion() || !IsInsideTree() || GetParent() is not Node3D root) return;
        Derailed = true;
        var at = GlobalPosition;
        Sparks(root, at + Vector3.Up * 0.4f);
        Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        RideAudio.At("cart_crash", at);
        Props.Tremor.ShakeOnly(0.03f, 0.4f);
        var groundY = Run.Track.Valid ? Run.Track.Point(Run.State.S).Y : at.Y;
        foreach (var rider in Aboard)
        {
            if (!IsInstanceValid(rider) || rider.Carrier != this) continue;
            var p = rider.GlobalPosition;
            rider.Drop(new Vector3(p.X, groundY + 0.05f, p.Z));
        }
        Aboard.Clear();
        Array.Clear(_bySpot);
        Tumble(root);
        Visible = false;
        CollisionLayer = 0;
        Shoe.Break();
        QueueFree();
    }

    /// <summary>
    /// Server: the cart ran off the broken bridge's gap (<see cref="RailEncounterController"/> judges it and has given its living riders to the
    /// crevasse). Quiet, for it happens unseen as often as not: no log line, no taunt and no tremor, only the crash at the gap. Every rider still
    /// carried is laid hidden at <paramref name="under"/> (in the dark under the gap), the tub tumbles into the drop and the cart is freed.
    /// <see cref="Derailed"/> is set, so clients hear it gone with the tumble; they read the plunge from where it went (<see cref="SwitchBackRules.PlungedAt"/>).
    /// </summary>
    public void PlungeThrough(Vector3 under)
    {
        if (Derailed || NetSession.IsClient || IsQueuedForDeletion() || !IsInsideTree() || GetParent() is not Node3D root) return;
        Plunge(root, under);
    }

    /// <summary>Client: the server's cart went through the broken bridge's gap. The same crash and tumble, no tremor; its riders are laid hidden at <paramref name="under"/>.</summary>
    public void ShowPlunged(Vector3 under)
    {
        if (Derailed || IsQueuedForDeletion() || !IsInsideTree() || GetParent() is not Node3D root) return;
        Plunge(root, under);
    }

    private void Plunge(Node3D root, Vector3 under)
    {
        Derailed = true;
        var at = GlobalPosition;
        Audio.Synth.PlayAt(root, Audio.Synth.Thud(), at, -1f);
        RideAudio.At("cart_crash", at); // positional: heard only near the gap
        foreach (var rider in Aboard)
        {
            if (!IsInstanceValid(rider)) continue;
            if (rider.Carrier == this) rider.Drop(under);
            rider.Visible = false; // gone into the dark: nothing is left on the line or over the drop
        }
        Aboard.Clear();
        Array.Clear(_bySpot);
        Tumble(root);
        Visible = false;
        CollisionLayer = 0;
        Shoe.Break();
        QueueFree();
    }

    /// <summary>A one-shot burst of orange sparks.</summary>
    private void Sparks(Node3D root, Vector3 at)
    {
        var sparks = new GpuParticles3D
        {
            Name = "SlagSparks", Amount = 90, Lifetime = 0.9, OneShot = true, Explosiveness = 0.95f, Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 75f, InitialVelocityMin = 3f, InitialVelocityMax = 7.5f, Gravity = new Vector3(0, -9f, 0),
                ScaleMin = 0.5f, ScaleMax = 1.2f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.7f, 0.2f, 1f),
            },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.07f, 0.07f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.6f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.5f, 0.1f), EmissionEnergyMultiplier = 4f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        root.AddChild(sparks);
        sparks.GlobalPosition = at;
        GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(sparks)) sparks.QueueFree(); };
    }

    /// <summary>The tub's look on a rigid body, thrown off the line away from the party's lane, gone after a couple of seconds.</summary>
    private void Tumble(Node3D root)
    {
        if (_body == null) return;
        var tub = new RigidBody3D { Name = "SlagCartTumble", CollisionLayer = 0, CollisionMask = 1, Mass = 300f };
        tub.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(TubWidth, TubHeight, TubLength) }, Position = new Vector3(0, TubBottom + TubHeight / 2f, 0) });
        tub.AddChild(_body.Duplicate());
        root.AddChild(tub);
        tub.GlobalTransform = GlobalTransform;
        var basis = GlobalTransform.Basis;
        tub.LinearVelocity = Velocity + basis.X * (-InnerSide * 2.5f) + Vector3.Up * 3f;
        tub.AngularVelocity = -basis.Z * (-InnerSide * 4f) + Vector3.Up * 1.5f;
        GetTree().CreateTimer(TumbleSeconds).Timeout += () => { if (IsInstanceValid(tub)) tub.QueueFree(); };
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        float halfW = TubWidth / 2f, halfL = TubLength / 2f, top = TubBottom + TubHeight, floor = TubBottom + 0.1f;

        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(TubWidth, TubHeight, TubLength) },
            Position = new Vector3(0, TubBottom + TubHeight / 2f, 0),
        });

        var body = new Node3D { Name = "Body" };
        AddChild(body);
        _body = body;
        _coal = DungeonBuilder.Lava(new Color(0.25f, 0.07f, 0.02f), new Color(1f, 0.35f, 0.05f), 2.5f, 0.5f);
        // the mine pack's low tub (Cart_2) over the box tub's size; the boxes stand in without it
        var tubModel = MinePack.Piece("Cart_2", new Vector3(TubWidth, TubHeight, TubLength), keepAspect: false);
        var firstTubBox = body.GetChildCount();
        // the tub: a rust-red floor and four walls, iron bands and a lip
        Box(body, new Vector3(TubWidth, floor - TubBottom, TubLength), new Vector3(0, (TubBottom + floor) / 2f, 0), _rust);
        var wallH = top - floor;
        var wallY = (floor + top) / 2f;
        Box(body, new Vector3(Wall, wallH, TubLength), new Vector3(-halfW + Wall / 2f, wallY, 0), _rust);
        Box(body, new Vector3(Wall, wallH, TubLength), new Vector3(halfW - Wall / 2f, wallY, 0), _rust);
        Box(body, new Vector3(TubWidth, wallH, Wall), new Vector3(0, wallY, -halfL + Wall / 2f), _rust);
        Box(body, new Vector3(TubWidth, wallH, Wall), new Vector3(0, wallY, halfL - Wall / 2f), _rust);
        const float rim = 0.09f, rimH = 0.05f;
        Box(body, new Vector3(rim, rimH, TubLength + 0.04f), new Vector3(-halfW, top - rimH / 2f, 0), _iron);
        Box(body, new Vector3(rim, rimH, TubLength + 0.04f), new Vector3(halfW, top - rimH / 2f, 0), _iron);
        Box(body, new Vector3(TubWidth + 0.04f, rimH, rim), new Vector3(0, top - rimH / 2f, -halfL), _iron);
        Box(body, new Vector3(TubWidth + 0.04f, rimH, rim), new Vector3(0, top - rimH / 2f, halfL), _iron);
        foreach (var z in new[] { -halfL * 0.5f, halfL * 0.5f })
        {
            Box(body, new Vector3(0.03f, TubHeight, 0.08f), new Vector3(-halfW - 0.015f, TubBottom + TubHeight / 2f, z), _iron);
            Box(body, new Vector3(0.03f, TubHeight, 0.08f), new Vector3(halfW + 0.015f, TubBottom + TubHeight / 2f, z), _iron);
        }
        if (tubModel != null)
        {
            for (var i = firstTubBox; i < body.GetChildCount(); i++) body.GetChild<Node3D>(i).Visible = false;
            body.AddChild(tubModel);
            tubModel.Position = new Vector3(0, TubBottom, 0);
        }
        // the chassis and wheels, as the party's cart
        foreach (var x in new[] { -RailTrack.Gauge / 2f + 0.12f, RailTrack.Gauge / 2f - 0.12f })
            Box(body, new Vector3(0.12f, 0.1f, TubLength - 0.1f), new Vector3(x, TubBottom - 0.05f, 0), _iron);
        var axleY = RailTop + WheelRadius;
        foreach (var z in new[] { -halfL + 0.35f, halfL - 0.35f })
        {
            if (MineCart.Axle(z < 0f) is { } axle)
            {
                body.AddChild(axle); // the mine pack's axle and its two wheels, in place of the rod and the wheel cylinders
                axle.Position = new Vector3(0, RailTop, z);
                continue;
            }
            body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = RailTrack.Gauge + 0.1f, RadialSegments = 8, Rings = 1 },
                MaterialOverride = _iron,
                Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(0, axleY, z)),
            });
            foreach (var x in new[] { -RailTrack.Gauge / 2f, RailTrack.Gauge / 2f })
                body.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = WheelRadius, BottomRadius = WheelRadius, Height = 0.07f, RadialSegments = 14, Rings = 1 },
                    MaterialOverride = _iron,
                    Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(x, axleY, z)),
                });
        }
        // the slag heap: a dark mound proud of the rim, glowing coals scattered on it
        body.AddChild(new MeshInstance3D
        {
            Name = "Slag",
            Mesh = new SphereMesh { Radius = 0.5f, Height = 0.5f, RadialSegments = 14, Rings = 6 },
            MaterialOverride = _slag,
            Transform = new Transform3D(Basis.FromScale(new Vector3(1.3f, 0.7f, 2f)), new Vector3(0, top - 0.12f, 0)),
        });
        var coalAt = new[] { new Vector3(0.3f, 0.12f, -0.35f), new Vector3(-0.32f, 0.1f, 0.25f), new Vector3(0.1f, 0.15f, 0.45f), new Vector3(-0.05f, 0.16f, -0.1f), new Vector3(0.35f, 0.08f, 0.7f) };
        for (var i = 0; i < coalAt.Length; i++)
            body.AddChild(new MeshInstance3D
            {
                Name = "Coal",
                Mesh = new SphereMesh { Radius = 0.09f + 0.02f * (i % 3), Height = 0.12f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = _coal,
                Position = coalAt[i] + new Vector3(0, top - 0.05f, 0),
            });
        AddChild(new OmniLight3D { Name = "CoalGlow", Position = new Vector3(0, top + 0.4f, 0), OmniRange = 4f, LightEnergy = 1.2f, LightColor = new Color(1f, 0.45f, 0.15f), ShadowEnabled = false });
        // its head lamp, an enemy's orange, at the front wall's middle (on the cart node: the tumble copies the Body only)
        CartLamp.Attach(this, new Vector3(0, TubBottom + 0.4f, -halfL - 0.08f), enemy: true);

        // the brake shoe: on the inner side by the back wheel, where the heroes in the cart can see and hit it
        Shoe = new BrakeShoe(this) { Name = "BrakeShoe", Position = new Vector3(InnerSide * (halfW + 0.12f), axleY + 0.05f, halfL - 0.35f) };
        AddChild(Shoe);
        Sound = new CartSound();
        AddChild(Sound);
    }

    private static void Box(Node3D parent, Vector3 size, Vector3 at, Material material)
        => parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });
}

/// <summary>
/// A slag cart's brake shoe: a small block glowing orange against the wheel, on layer 4 so a hero's blade, a shove of
/// steel or an arrow finds it (a prop, not a creature). One hit breaks it and derails its cart.
/// </summary>
public partial class BrakeShoe : AnimatableBody3D, ICombatTarget
{
    private readonly SlagCart _cart;
    private readonly StandardMaterial3D _glow = new() { AlbedoColor = new Color(0.9f, 0.35f, 0.05f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.08f), EmissionEnergyMultiplier = 3f, Roughness = 0.6f };

    public BrakeShoe(SlagCart cart)
    {
        _cart = cart;
        SyncToPhysics = false;
        CollisionLayer = 4; // what a hero's melee cast, blade sweep and arrows look for
        CollisionMask = 0;
    }

    /// <summary>For Godot's construction of the script class; a shoe is always built with its cart.</summary>
    public BrakeShoe() : this(null!) { }

    public string DisplayName => "brake shoe";
    public bool IsAlive { get; private set; } = true;
    public bool IsUnaware => false;
    public bool IsCreature => false;
    public void ApplyStatus(StatusApplication status) { }

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.4f, 0.45f, 0.6f) } }); // a little bigger than it looks: easier to hit from a moving cart
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.24f, 0.42f) }, MaterialOverride = _glow });
        AddChild(new OmniLight3D { OmniRange = 1.8f, LightEnergy = 0.9f, LightColor = new Color(1f, 0.5f, 0.1f), ShadowEnabled = false, Position = new Vector3(0, 0.1f, 0) });
    }

    /// <summary>Any hit breaks it: the cart derails (deferred: a hit can land inside a physics query).</summary>
    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (!IsAlive || NetSession.IsClient || _cart == null) return DamageResult.Miss(attack.Type);
        IsAlive = false;
        EventBus.Instance.Log($"{attackerName} breaks the slag cart's brake shoe!");
        Callable.From(_cart.Derail).CallDeferred();
        return new DamageResult(true, false, false, false, 1, attack.Type, Array.Empty<StatusApplication>());
    }

    /// <summary>Gone with its cart.</summary>
    public void Break()
    {
        IsAlive = false;
        CollisionLayer = 0;
    }
}
