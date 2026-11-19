using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party's mine cart: an iron tub on four wheels with two rows of two seats and a rear bench. A dumb prop: its
/// owner (the RailController, through a <see cref="CartRun"/>) poses it, books <see cref="Seats"/>, and answers the
/// interact key through <see cref="PromptFor"/> and <see cref="OnUse"/>; it keeps who rides it in <see cref="Riders"/>.
/// Its origin sits on the track (the floor under the rails); forward is −Z.
/// </summary>
public partial class MineCart : AnimatableBody3D, IInteractable, ICartBody
{
    /// <summary>The tub's outside bottom, above the track; the wheels hang below it.</summary>
    public const float TubBottom = 0.5f;
    public const float TubHeight = 0.9f;
    public const float TubWidth = 1.4f;
    public const float TubLength = CartRules.HalfLength * 2f;
    /// <summary>The floor inside the tub: a seated rider's feet (and body origin) rest here.</summary>
    public const float InsideFloor = TubBottom + 0.14f;
    /// <summary>The seat planks stand this high above the inside floor.</summary>
    public const float SeatHeight = 0.35f;
    /// <summary>The rear bench is on a raised plank deck at the back of the tub.</summary>
    public const float BenchDeck = 0.15f;
    const float Wall = 0.06f;
    const float WheelRadius = 0.15f;
    const float RailTop = 0.24f; // RailTrack: sleeper 0.12 + rail 0.12

    /// <summary>Seat positions in the cart's space (x, z): seats 0-1 the front row (left, right), 2-3 the back row, 4-5 the rear bench.</summary>
    static readonly Vector2[] SeatSpots =
    {
        new(-0.28f, -0.62f), new(0.28f, -0.62f),
        new(-0.28f, -0.07f), new(0.28f, -0.07f),
        new(-0.28f, 0.62f), new(0.28f, 0.62f),
    };

    // once per cart (made in Build), not static: a static resource outlives the scene tree and is reported as leaked at exit
    private StandardMaterial3D _iron = null!, _wood = null!;

    /// <summary>
    /// The mine pack's tub (<see cref="MinePack"/>) this cart wears in place of its box tub, or null to keep the boxes: the party's cart,
    /// its spares and chasers keep them, since the seats and the bench are laid out in them.
    /// </summary>
    protected virtual string? TubModel => null;

    private Net.CartKind _kind = Net.CartKind.Party;
    /// <summary>What it is on the wire: the party's cart (a spare is one too), a chaser (a spare running after the train, <see cref="AsChaser"/>), or a runaway wagon.</summary>
    public virtual Net.CartKind Kind => _kind;
    /// <summary>This tub is a chaser (a co-op player's spare running after the train), as built: its kind on the wire.</summary>
    public MineCart AsChaser()
    {
        _kind = Net.CartKind.Chaser;
        return this;
    }
    /// <summary>Its id on the wire (<see cref="Net.CartIds"/>): 0 the party's, the server's count for a runaway wagon, ChaserBase + its stop for a chaser.</summary>
    public int CartId { get; set; }
    public CartSeats Seats { get; } = new();
    /// <summary>Who rides this cart, by <see cref="ICartRider.RiderId"/>: entered by <see cref="Admit"/> (the rider's SitIn), removed by <see cref="Release"/>.</summary>
    public Dictionary<int, ICartRider> Riders { get; } = new();
    /// <summary>A rider got in (true) or out (false), whoever seated or unseated it: the RailController relays it as a CartEvent.</summary>
    public event Action<ICartRider, bool>? RiderChanged;
    /// <summary>Speed along the line (m/s), set by the RailController each tick.</summary>
    public float Speed { get; set; }
    /// <summary>How hard the line bends here (1/m, signed), set by the RailController each tick.</summary>
    public float Curvature { get; set; }
    /// <summary>The cart's velocity in the world: forward (−Z) at <see cref="Speed"/>.</summary>
    public Vector3 Velocity => -GlobalTransform.Basis.Z * Speed;
    /// <summary>
    /// A co-op client's copy: the run it is posed on and the follower that moves it (set by whoever follows it), so a seated
    /// headset's comfort vignette reads the server's ride (<see cref="CartFollower.AuthV"/>, <see cref="CartRun.MotionAt"/>),
    /// not the rendered corrections, and knows when the pose snapped. Null on a server and alone.
    /// </summary>
    public CartRun? NetRun { get; set; }
    public CartFollower? NetFollow { get; set; }

    /// <summary>The prompt for the hero looking at the cart; null or "" offers nothing. Set by the RailController.</summary>
    public Func<Adventurer, string>? PromptFor { get; set; }
    /// <summary>A hero pressed interact on the cart. Set by the RailController.</summary>
    public Action<Adventurer>? OnUse { get; set; }

    /// <summary>Its sound (a child once built): fed by whoever runs it; a spare's is never fed and stays silent.</summary>
    public CartSound? Sound { get; private set; }

    private readonly Marker3D[] _seats = new Marker3D[CartSeats.HeroSeats + CartSeats.BenchSeats];
    private bool _built;

    public MineCart()
    {
        Name = "MineCart";
        SyncToPhysics = false;
        CollisionLayer = 1 | 8; // solid like a wall, and a thing to use: the interact ray only sees layer 8 (as a Door's 9)
        CollisionMask = 0;
    }

    public override void _Ready() => Build();

    /// <summary>The prompt asks about the hero this machine plays, since a prompt has no actor.</summary>
    public string Prompt => Main.Instance?.Party?.Local is { } me ? PromptFor?.Invoke(me) ?? "" : "";

    /// <summary>How far <paramref name="world"/> lies from the tub's box (its nearest point; 0 inside): a hero's boarding reach is measured to it.</summary>
    public float TubDistance(Vector3 world)
    {
        var local = GlobalTransform.AffineInverse() * world;
        return RideInputRules.TubDistance(local.X, local.Y, local.Z, TubWidth / 2f, CartRules.HalfLength, TubBottom, TubBottom + TubHeight);
    }

    /// <summary>The nearer of <paramref name="hero"/>'s body and eye to the tub (<see cref="TubDistance"/>), as the reach of a boarding is judged.</summary>
    public float TubDistance(Adventurer hero) => Mathf.Min(TubDistance(hero.GlobalPosition), TubDistance(hero.Camera.GlobalPosition));

    public void Interact(PartyController party, Adventurer actor) => OnUse?.Invoke(actor);

    /// <summary>Where a rider in <paramref name="seat"/> sits, in world space: the origin on the tub floor under the seat, facing forward (−Z).</summary>
    public Transform3D SeatTransform(int seat)
    {
        Build();
        return _seats[Math.Clamp(seat, 0, _seats.Length - 1)].GlobalTransform;
    }

    /// <summary>How far above the rim a boarding hound's feet are: it clings to the lip with its forepaws over the wall.</summary>
    public const float BoarderLift = 0.25f;
    /// <summary>The two boarder spots on the rim in the cart's space (x, z): 0 front-left, 1 back-right.</summary>
    static readonly Vector2[] BoarderSpots = { new(-TubWidth / 2f + 0.08f, -0.55f), new(TubWidth / 2f - 0.08f, 0.55f) };
    public const int BoarderCount = SetPieceRules.RimSpots;
    /// <summary>Who holds each boarder spot (reserved at a leap's start, so two leaps never take one spot), null when free.</summary>
    public Enemy?[] Boarders { get; } = new Enemy?[BoarderCount];

    /// <summary>The boarder spot <paramref name="i"/> in world space: on the rim, facing in across the tub.</summary>
    public Transform3D BoarderSpotTransform(int i)
    {
        i = Math.Clamp(i, 0, BoarderCount - 1);
        var at = BoarderSpots[i];
        var yaw = i == 0 ? -Mathf.Pi / 2f : Mathf.Pi / 2f; // −Z (forward) turned to point at the other wall
        return GlobalTransform * new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(at.X, TubBottom + TubHeight + BoarderLift, at.Y));
    }

    /// <summary>
    /// Take a free boarder spot for <paramref name="who"/>: its index, or −1 when none is free (a held spot whose holder is gone is free).
    /// <paramref name="open"/>: how many spots may be held at once (<see cref="SetPieceRules.RimSpotsOpen"/>; the party cart's awake riders).
    /// </summary>
    public int ReserveBoarderSpot(Enemy who, int open = BoarderCount)
    {
        var held = 0;
        for (var i = 0; i < Boarders.Length; i++) if (!SpotFree(i)) held++;
        if (held >= open) return -1;
        for (var i = 0; i < Boarders.Length; i++)
            if (SpotFree(i)) { Boarders[i] = who; return i; }
        return -1;
    }

    private bool SpotFree(int i) => Boarders[i] == null || !GodotObject.IsInstanceValid(Boarders[i]) || Boarders[i]!.IsQueuedForDeletion() || !Boarders[i]!.IsAlive;

    /// <summary>Heroes seated in this cart who are awake (not downed, not parked). No allocation.</summary>
    public int AwakeHeroesSeated()
    {
        var n = 0;
        foreach (var rider in Riders.Values)
            if (rider is Adventurer hero && GodotObject.IsInstanceValid(hero) && !hero.Downed && !hero.Parked) n++;
        return n;
    }

    /// <summary>Give the spot back (only if <paramref name="who"/> still holds it).</summary>
    public void FreeBoarderSpot(Enemy who)
    {
        for (var i = 0; i < Boarders.Length; i++) if (Boarders[i] == who) Boarders[i] = null;
    }

    /// <summary>The rider (its seat booked in <see cref="Seats"/>, its own SitIn under way) rides here now.</summary>
    public void Admit(ICartRider rider)
    {
        Riders[rider.RiderId] = rider;
        RiderChanged?.Invoke(rider, true);
    }

    /// <summary>
    /// The rider's seat is free again and it no longer rides here: the book only (the rider's own GetOut, or a teleport,
    /// calls this as it leaves the seat). A rider that is not the one aboard under its id changes nothing.
    /// </summary>
    public void Release(ICartRider rider)
    {
        if (!Riders.TryGetValue(rider.RiderId, out var aboard) || aboard != rider) return;
        Riders.Remove(rider.RiderId);
        Seats.Leave(rider.RiderId);
        RiderChanged?.Invoke(rider, false);
    }

    /// <summary>Put the cart on a curve sample (world space; −Z along the line, Y up from the track).</summary>
    public void Pose(Transform3D trackFrame) => GlobalTransform = new Transform3D(trackFrame.Basis.Orthonormalized(), trackFrame.Origin);

    private void Build()
    {
        if (_built) return;
        _built = true;
        float halfW = TubWidth / 2f, halfL = TubLength / 2f, top = TubBottom + TubHeight;
        _iron = DungeonBuilder.Material("metal_plate_02", new Color(0.3f, 0.28f, 0.26f), 1f);
        _iron.Metallic = 0.8f;
        _wood = DungeonBuilder.Material("old_planks_02", new Color(0.45f, 0.33f, 0.22f), 1f);

        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(TubWidth, TubHeight, TubLength) },
            Position = new Vector3(0, TubBottom + TubHeight / 2f, 0),
        });

        var body = new Node3D { Name = "Body" };
        AddChild(body);
        var tubModel = TubModel is { } tubName ? MinePack.Piece(tubName, new Vector3(TubWidth, TubHeight, TubLength), keepAspect: false) : null;
        var firstTubBox = body.GetChildCount();
        // the tub: a floor and four iron walls
        Box(body, "Floor", new Vector3(TubWidth, InsideFloor - TubBottom, TubLength), new Vector3(0, (TubBottom + InsideFloor) / 2f, 0), _iron);
        var wallH = top - InsideFloor;
        var wallY = (InsideFloor + top) / 2f;
        Box(body, "WallL", new Vector3(Wall, wallH, TubLength), new Vector3(-halfW + Wall / 2f, wallY, 0), _iron);
        Box(body, "WallR", new Vector3(Wall, wallH, TubLength), new Vector3(halfW - Wall / 2f, wallY, 0), _iron);
        Box(body, "WallF", new Vector3(TubWidth, wallH, Wall), new Vector3(0, wallY, -halfL + Wall / 2f), _iron);
        Box(body, "WallB", new Vector3(TubWidth, wallH, Wall), new Vector3(0, wallY, halfL - Wall / 2f), _iron);
        // the rim: a thicker iron lip round the top
        const float rim = 0.1f, rimH = 0.06f;
        var rimY = top - rimH / 2f;
        Box(body, "RimL", new Vector3(rim, rimH, TubLength + 0.04f), new Vector3(-halfW, rimY, 0), _iron);
        Box(body, "RimR", new Vector3(rim, rimH, TubLength + 0.04f), new Vector3(halfW, rimY, 0), _iron);
        Box(body, "RimF", new Vector3(TubWidth + 0.04f, rimH, rim), new Vector3(0, rimY, -halfL), _iron);
        Box(body, "RimB", new Vector3(TubWidth + 0.04f, rimH, rim), new Vector3(0, rimY, halfL), _iron);
        // iron bands down the sides
        foreach (var z in new[] { -halfL * 0.5f, halfL * 0.5f })
        {
            Box(body, "BandL", new Vector3(0.03f, TubHeight, 0.08f), new Vector3(-halfW - 0.015f, TubBottom + TubHeight / 2f, z), _iron);
            Box(body, "BandR", new Vector3(0.03f, TubHeight, 0.08f), new Vector3(halfW + 0.015f, TubBottom + TubHeight / 2f, z), _iron);
        }
        // the front board: a wooden buffer across the nose
        Box(body, "FrontBoard", new Vector3(TubWidth + 0.1f, 0.3f, 0.08f), new Vector3(0, TubBottom + 0.25f, -halfL - 0.05f), _wood);
        var tubBoxes = body.GetChildCount() - firstTubBox;
        // the chassis: two wooden beams under the tub, and the axles
        foreach (var x in new[] { -RailTrack.Gauge / 2f + 0.12f, RailTrack.Gauge / 2f - 0.12f })
            Box(body, "Beam", new Vector3(0.12f, 0.1f, TubLength - 0.1f), new Vector3(x, TubBottom - 0.05f, 0), _wood);
        var axleY = RailTop + WheelRadius;
        foreach (var z in new[] { -halfL + 0.35f, halfL - 0.35f })
        {
            if (Axle(z < 0f) is { } model)
            {
                // the mine pack's axle, its two wheels on the rails, in place of the rod and the two wheel cylinders
                body.AddChild(model);
                model.Position = new Vector3(0, RailTop, z);
                continue;
            }
            var axle = new MeshInstance3D
            {
                Name = "Axle",
                Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = RailTrack.Gauge + 0.1f, RadialSegments = 8, Rings = 1 },
                MaterialOverride = _iron,
                Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(0, axleY, z)),
            };
            body.AddChild(axle);
            foreach (var x in new[] { -RailTrack.Gauge / 2f, RailTrack.Gauge / 2f })
                body.AddChild(new MeshInstance3D
                {
                    Name = "Wheel",
                    Mesh = new CylinderMesh { TopRadius = WheelRadius, BottomRadius = WheelRadius, Height = 0.07f, RadialSegments = 14, Rings = 1 },
                    MaterialOverride = _iron,
                    Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(x, axleY, z)),
                });
        }
        // the seats: a plank per row, the rear bench on its raised deck
        var firstSeat = body.GetChildCount();
        var plank = new Vector3(TubWidth - Wall * 2f, 0.05f, 0.3f);
        foreach (var row in new[] { 0, 2 })
            Box(body, "Seat", plank, new Vector3(0, InsideFloor + SeatHeight - 0.025f, SeatSpots[row].Y + 0.12f), _wood);
        var deckDepth = halfL - (SeatSpots[4].Y - 0.25f) - Wall;
        Box(body, "BenchDeck", new Vector3(TubWidth - Wall * 2f, BenchDeck, deckDepth), new Vector3(0, InsideFloor + BenchDeck / 2f, halfL - Wall - deckDepth / 2f), _wood);
        Box(body, "Bench", plank, new Vector3(0, InsideFloor + BenchDeck + SeatHeight - 0.025f, SeatSpots[4].Y + 0.12f), _wood);
        if (tubModel != null)
        {
            // the model's tub stands in the box tub's place, over the same size: the boxes (walls, rim, bands, front board, seats) are hidden
            for (var i = 0; i < tubBoxes; i++) body.GetChild<Node3D>(firstTubBox + i).Visible = false;
            for (var i = firstSeat; i < body.GetChildCount(); i++) body.GetChild<Node3D>(i).Visible = false;
            body.AddChild(tubModel);
            tubModel.Position = new Vector3(0, TubBottom, 0);
        }

        // the lamp: a warm spot on the front board aimed down the track, tilted a little to the floor, and a small glow round it
        var lampAt = new Vector3(0, TubBottom + 0.45f, -halfL - 0.12f);
        AddChild(new SpotLight3D
        {
            Name = "Lamp",
            Position = lampAt,
            RotationDegrees = new Vector3(-8f, 0, 0),
            SpotRange = 18f,
            SpotAngle = 35f,
            LightEnergy = 2f,
            LightColor = new Color(1f, 0.85f, 0.6f),
            ShadowEnabled = false,
        });
        AddChild(new OmniLight3D
        {
            Name = "LampGlow",
            Position = lampAt,
            OmniRange = 3f,
            LightEnergy = 0.6f,
            LightColor = new Color(1f, 0.85f, 0.6f),
            ShadowEnabled = false,
        });

        for (var i = 0; i < _seats.Length; i++)
        {
            var y = InsideFloor + (i >= CartSeats.HeroSeats ? BenchDeck : 0f);
            _seats[i] = new Marker3D { Name = $"Seat{i}", Position = new Vector3(SeatSpots[i].X, y, SeatSpots[i].Y) };
            AddChild(_seats[i]);
        }
        Sound = new CartSound();
        AddChild(Sound);
    }

    private static void Box(Node3D parent, string name, Vector3 size, Vector3 at, Material material)
        => parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });

    /// <summary>The mine pack's front (CartWheels_1) or rear (CartWheel_2) axle, its wheels on the gauge at this cart's wheel size; null without the pack.</summary>
    internal static Node3D? Axle(bool front)
        => MinePack.Piece(front ? "CartWheels_1" : "CartWheel_2", new Vector3(RailTrack.Gauge + 0.07f, WheelRadius * 2f, WheelRadius * 2f), keepAspect: false);
}
