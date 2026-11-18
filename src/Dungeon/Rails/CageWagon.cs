using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A cage wagon on a railed jailbreak floor: a flat bed on four wheels, the size of the party's cart, with an iron-bar
/// cage on it, a barred door and a padlock on the side that faces the platform, and room for two crew standing inside.
/// A dumb prop like <see cref="MineCart"/>: the crew controller answers the interact key through <see cref="PromptFor"/>
/// and <see cref="OnUse"/>; it stands on its siding at a stop until the RailController couples it behind the cart and
/// poses it (<see cref="Pose"/>). Its origin sits on the track (or the siding's floor); forward is −Z. Only NPC riders:
/// its seat book has no hero seats in use, and the two bench seats (4, 5) are the crew's.
/// </summary>
public partial class CageWagon : AnimatableBody3D, IInteractable, ICartBody
{
    public const float BedBottom = MineCart.TubBottom;
    public const float BedThickness = 0.12f;
    /// <summary>The bed's top: a rider's feet (and body origin) rest here.</summary>
    public const float BedTop = BedBottom + BedThickness;
    public const float Width = MineCart.TubWidth;
    public const float Length = MineCart.TubLength;
    /// <summary>The bars stand this tall above the bed: the crew stand inside (a 1.8 m body), so the roof clears their heads.</summary>
    public const float CageHeight = 1.95f;
    const float Bar = 0.04f;
    const float BarEvery = 0.18f;
    const float Post = 0.08f;
    const float WheelRadius = 0.15f;
    const float RailTop = 0.24f; // RailTrack: sleeper 0.12 + rail 0.12

    /// <summary>The two crew spots in the wagon's space (x, z).</summary>
    static readonly Vector2[] SeatSpots = { new(0f, -0.6f), new(0f, 0.6f) };

    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.8f, Roughness = 0.5f };
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.24f, 0.16f, 0.1f), Roughness = 0.85f };
    private readonly StandardMaterial3D _brass = new() { AlbedoColor = new Color(0.55f, 0.42f, 0.18f), Metallic = 0.9f, Roughness = 0.35f };

    /// <summary>The intermediate stop this wagon stands at (1-based, as the RailController's stop index).</summary>
    public int Stop { get; init; }
    /// <summary>The wagon's side (+1 its local +X, −1 its −X) that faces the platform: the door and padlock are there, and the crew look out that way.</summary>
    public int PlatformSide { get; init; } = 1;
    /// <summary>The cell of its siding (where it stood before it was coupled).</summary>
    public Dungeon.Generation.GridPos Cell { get; init; }

    /// <summary>The cage is unlocked: its door stands open, the padlock gone.</summary>
    public bool Open { get; private set; }
    /// <summary>Coupled behind the party's cart: the RailController poses it.</summary>
    public bool Coupled { get; private set; }

    public CartSeats Seats { get; } = new();
    public Dictionary<int, ICartRider> Riders { get; } = new();
    public event Action<ICartRider, bool>? RiderChanged;
    public float Speed { get; set; }
    public float Curvature { get; set; }
    public Vector3 Velocity => -GlobalTransform.Basis.Z * Speed;

    /// <summary>The prompt for the hero looking at the wagon; null or "" offers nothing. Set by the crew controller.</summary>
    public Func<Adventurer, string>? PromptFor { get; set; }
    /// <summary>A hero pressed interact on the wagon. Set by the crew controller.</summary>
    public Action<Adventurer>? OnUse { get; set; }

    private readonly Marker3D[] _seats = new Marker3D[CartSeats.BenchSeats];
    private Node3D? _door, _padlock;
    private bool _built;

    public CageWagon()
    {
        SyncToPhysics = false;
        CollisionLayer = 1 | 8; // solid on its siding, and a thing to use (the interact ray sees layer 8), as the cart
        CollisionMask = 0;
    }

    public override void _Ready() => Build();

    public string Prompt => Main.Instance?.Party?.Local is { } me ? PromptFor?.Invoke(me) ?? "" : "";

    public void Interact(PartyController party, Adventurer actor) => OnUse?.Invoke(actor);

    /// <summary>Unlock (or, on a load or a client, show) the cage: the door swings out and the padlock is gone.</summary>
    public void SetOpen(bool open)
    {
        Open = open;
        Build();
        if (_door != null) _door.Rotation = new Vector3(0, open ? PlatformSide * 1.9f : 0f, 0);
        if (_padlock != null) _padlock.Visible = !open;
    }

    /// <summary>On (true) the train behind the cart, or back on a siding. A coupled wagon is not solid: the party walks through the train rather than get caught in it.</summary>
    public void SetCoupled(bool coupled)
    {
        Coupled = coupled;
        CollisionLayer = coupled ? 8u : 1u | 8u;
        if (!coupled) Speed = 0f;
    }

    /// <summary>
    /// Where crew rider <paramref name="seat"/> stands, in world space: on the bed, looking out through the platform side.
    /// Takes the seat book's bench seats (4, 5) or a plain 0..1.
    /// </summary>
    public Transform3D SeatTransform(int seat)
    {
        Build();
        var i = seat >= CartSeats.HeroSeats ? seat - CartSeats.HeroSeats : seat;
        return _seats[Math.Clamp(i, 0, _seats.Length - 1)].GlobalTransform;
    }

    public void Admit(ICartRider rider)
    {
        Riders[rider.RiderId] = rider;
        RiderChanged?.Invoke(rider, true);
    }

    public void Release(ICartRider rider)
    {
        if (!Riders.TryGetValue(rider.RiderId, out var aboard) || aboard != rider) return;
        Riders.Remove(rider.RiderId);
        Seats.Leave(rider.RiderId);
        RiderChanged?.Invoke(rider, false);
    }

    public const float BoarderLift = 0.05f;
    public const int BoarderCount = 1;
    /// <summary>Who holds the boarder spot on the roof (reserved at a leap's start), null when free.</summary>
    public Enemy?[] Boarders { get; } = new Enemy?[BoarderCount];

    /// <summary>The roof's boarder spot in world space: on top of the cage, at its middle, facing forward.</summary>
    public Transform3D BoarderSpotTransform(int i = 0)
        => GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, BedTop + CageHeight + BoarderLift, 0));

    /// <summary>Take the roof spot for <paramref name="who"/>: 0, or −1 when it is held (a holder that is gone or dead frees it).</summary>
    public int ReserveBoarderSpot(Enemy who)
    {
        if (Boarders[0] == null || !GodotObject.IsInstanceValid(Boarders[0]) || Boarders[0]!.IsQueuedForDeletion() || !Boarders[0]!.IsAlive) { Boarders[0] = who; return 0; }
        return -1;
    }

    public void FreeBoarderSpot(Enemy who)
    {
        for (var i = 0; i < Boarders.Length; i++) if (Boarders[i] == who) Boarders[i] = null;
    }

    /// <summary>Put the wagon on a curve sample (world space; −Z along the line, Y up from the track).</summary>
    public void Pose(Transform3D trackFrame) => GlobalTransform = new Transform3D(trackFrame.Basis.Orthonormalized(), trackFrame.Origin);

    private void Build()
    {
        if (_built) return;
        _built = true;
        float halfW = Width / 2f, halfL = Length / 2f, top = BedTop + CageHeight;

        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(Width, top - BedBottom, Length) },
            Position = new Vector3(0, (BedBottom + top) / 2f, 0),
        });

        var body = new Node3D { Name = "Body" };
        AddChild(body);
        // the bed: planks on two iron-shod beams, and a wooden buffer at either end
        Box(body, "Bed", new Vector3(Width, BedThickness, Length), new Vector3(0, BedBottom + BedThickness / 2f, 0), _wood);
        Box(body, "BedRimL", new Vector3(0.06f, 0.08f, Length), new Vector3(-halfW + 0.03f, BedTop + 0.04f, 0), _iron);
        Box(body, "BedRimR", new Vector3(0.06f, 0.08f, Length), new Vector3(halfW - 0.03f, BedTop + 0.04f, 0), _iron);
        foreach (var z in new[] { -halfL - 0.05f, halfL + 0.05f })
            Box(body, "Buffer", new Vector3(Width + 0.1f, 0.25f, 0.08f), new Vector3(0, BedBottom + 0.05f, z), _wood);
        foreach (var x in new[] { -RailTrack.Gauge / 2f + 0.12f, RailTrack.Gauge / 2f - 0.12f })
            Box(body, "Beam", new Vector3(0.12f, 0.1f, Length - 0.1f), new Vector3(x, BedBottom - 0.05f, 0), _wood);
        // four wheels on two axles, as the cart's
        var axleY = RailTop + WheelRadius;
        foreach (var z in new[] { -halfL + 0.35f, halfL - 0.35f })
        {
            body.AddChild(new MeshInstance3D
            {
                Name = "Axle",
                Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = RailTrack.Gauge + 0.1f, RadialSegments = 8, Rings = 1 },
                MaterialOverride = _iron,
                Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(0, axleY, z)),
            });
            foreach (var x in new[] { -RailTrack.Gauge / 2f, RailTrack.Gauge / 2f })
                body.AddChild(new MeshInstance3D
                {
                    Name = "Wheel",
                    Mesh = new CylinderMesh { TopRadius = WheelRadius, BottomRadius = WheelRadius, Height = 0.07f, RadialSegments = 14, Rings = 1 },
                    MaterialOverride = _iron,
                    Transform = new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2f), new Vector3(x, axleY, z)),
                });
        }

        // the cage: corner posts, a top frame, vertical bars every BarEvery on the ends and the far side, a barred roof
        var barH = CageHeight;
        var barY = BedTop + barH / 2f;
        foreach (var x in new[] { -halfW + Post / 2f, halfW - Post / 2f })
            foreach (var z in new[] { -halfL + Post / 2f, halfL - Post / 2f })
                Box(body, "Post", new Vector3(Post, barH, Post), new Vector3(x, barY, z), _iron);
        var frameY = top - 0.03f;
        Box(body, "FrameL", new Vector3(0.06f, 0.06f, Length), new Vector3(-halfW + 0.03f, frameY, 0), _iron);
        Box(body, "FrameR", new Vector3(0.06f, 0.06f, Length), new Vector3(halfW - 0.03f, frameY, 0), _iron);
        Box(body, "FrameF", new Vector3(Width, 0.06f, 0.06f), new Vector3(0, frameY, -halfL + 0.03f), _iron);
        Box(body, "FrameB", new Vector3(Width, 0.06f, 0.06f), new Vector3(0, frameY, halfL - 0.03f), _iron);
        // a middle rail round the cage, where the bars are riveted
        var midY = BedTop + barH * 0.5f;
        Box(body, "BandF", new Vector3(Width, 0.04f, 0.05f), new Vector3(0, midY, -halfL + 0.03f), _iron);
        Box(body, "BandB", new Vector3(Width, 0.04f, 0.05f), new Vector3(0, midY, halfL - 0.03f), _iron);
        var far = -PlatformSide * (halfW - 0.03f);
        Box(body, "BandFar", new Vector3(0.05f, 0.04f, Length), new Vector3(far, midY, 0), _iron);
        // the ends' bars, across the wagon
        for (var x = -halfW + BarEvery; x < halfW - BarEvery / 2f; x += BarEvery)
        {
            Box(body, "Bar", new Vector3(Bar, barH, Bar), new Vector3(x, barY, -halfL + 0.03f), _iron);
            Box(body, "Bar", new Vector3(Bar, barH, Bar), new Vector3(x, barY, halfL - 0.03f), _iron);
        }
        // the far side's bars, along the wagon
        for (var z = -halfL + BarEvery; z < halfL - BarEvery / 2f; z += BarEvery)
            Box(body, "Bar", new Vector3(Bar, barH, Bar), new Vector3(far, barY, z), _iron);
        // the roof: bars across, on the top frame
        for (var z = -halfL + BarEvery; z < halfL - BarEvery / 2f; z += BarEvery)
            Box(body, "RoofBar", new Vector3(Width, Bar, Bar), new Vector3(0, top - 0.03f, z), _iron);

        // the platform side: fixed bars at either end, and a barred door between them hinged at its front edge
        var near = PlatformSide * (halfW - 0.03f);
        Box(body, "BandNear", new Vector3(0.05f, 0.04f, Length), new Vector3(near, midY, 0), _iron);
        const float doorHalf = 0.5f;
        for (var z = -halfL + BarEvery; z < halfL - BarEvery / 2f; z += BarEvery)
            if (Mathf.Abs(z) > doorHalf + 0.02f) Box(body, "Bar", new Vector3(Bar, barH, Bar), new Vector3(near, barY, z), _iron);
        _door = new Node3D { Name = "Door", Position = new Vector3(near, 0, -doorHalf) }; // the hinge
        body.AddChild(_door);
        for (var z = 0.08f; z < doorHalf * 2f - 0.04f; z += BarEvery)
            Box(_door, "DoorBar", new Vector3(Bar, barH - 0.1f, Bar), new Vector3(0, barY, z), _iron);
        Box(_door, "DoorTop", new Vector3(0.05f, 0.05f, doorHalf * 2f), new Vector3(0, top - 0.1f, doorHalf), _iron);
        Box(_door, "DoorMid", new Vector3(0.05f, 0.05f, doorHalf * 2f), new Vector3(0, midY, doorHalf), _iron);
        Box(_door, "DoorFoot", new Vector3(0.05f, 0.05f, doorHalf * 2f), new Vector3(0, BedTop + 0.08f, doorHalf), _iron);
        // the padlock: a brass block and its iron shackle on the door's free edge, outside the bars
        _padlock = new Node3D { Name = "Padlock", Position = new Vector3(near + PlatformSide * 0.06f, midY, doorHalf - 0.02f) };
        body.AddChild(_padlock);
        Box(_padlock, "Lock", new Vector3(0.06f, 0.14f, 0.12f), new Vector3(0, -0.08f, 0), _brass);
        Box(_padlock, "Shackle", new Vector3(0.02f, 0.08f, 0.08f), new Vector3(0, 0.03f, 0), _iron);
        SetOpen(Open);

        // the crew's spots: on the bed, facing out through the platform side
        var yaw = -PlatformSide * Mathf.Pi / 2f;
        for (var i = 0; i < _seats.Length; i++)
        {
            _seats[i] = new Marker3D { Name = $"Seat{i}", Position = new Vector3(SeatSpots[i].X, BedTop, SeatSpots[i].Y), Basis = new Basis(Vector3.Up, yaw) };
            AddChild(_seats[i]);
        }
    }

    private static void Box(Node3D parent, string name, Vector3 size, Vector3 at, Material material)
        => parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });
}
