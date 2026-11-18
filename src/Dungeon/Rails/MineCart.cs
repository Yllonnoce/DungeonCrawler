using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party's mine cart: an iron tub on four wheels with two rows of two seats and a rear bench. A dumb prop: the
/// RailController poses it, fills <see cref="Seats"/>, and answers the interact key through <see cref="PromptFor"/>
/// and <see cref="OnUse"/>. Its origin sits on the track (the floor under the rails); forward is −Z.
/// </summary>
public partial class MineCart : AnimatableBody3D, IInteractable
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

    // once per cart, not static: a static resource outlives the scene tree and is reported as leaked at exit
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.8f, Roughness = 0.5f };
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.24f, 0.16f, 0.1f), Roughness = 0.85f };

    public CartSeats Seats { get; } = new();
    /// <summary>Speed along the line (m/s), set by the RailController each tick.</summary>
    public float Speed { get; set; }
    /// <summary>How hard the line bends here (1/m, signed), set by the RailController each tick.</summary>
    public float Curvature { get; set; }
    /// <summary>The cart's velocity in the world: forward (−Z) at <see cref="Speed"/>.</summary>
    public Vector3 Velocity => -GlobalTransform.Basis.Z * Speed;

    /// <summary>The prompt for the hero looking at the cart; null or "" offers nothing. Set by the RailController.</summary>
    public Func<Adventurer, string>? PromptFor { get; set; }
    /// <summary>A hero pressed interact on the cart. Set by the RailController.</summary>
    public Action<Adventurer>? OnUse { get; set; }

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

    public void Interact(PartyController party, Adventurer actor) => OnUse?.Invoke(actor);

    /// <summary>Where a rider in <paramref name="seat"/> sits, in world space: the origin on the tub floor under the seat, facing forward (−Z).</summary>
    public Transform3D SeatTransform(int seat)
    {
        Build();
        return _seats[Math.Clamp(seat, 0, _seats.Length - 1)].GlobalTransform;
    }

    /// <summary>Put the cart on a curve sample (world space; −Z along the line, Y up from the track).</summary>
    public void Pose(Transform3D trackFrame) => GlobalTransform = new Transform3D(trackFrame.Basis.Orthonormalized(), trackFrame.Origin);

    private void Build()
    {
        if (_built) return;
        _built = true;
        float halfW = TubWidth / 2f, halfL = TubLength / 2f, top = TubBottom + TubHeight;

        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(TubWidth, TubHeight, TubLength) },
            Position = new Vector3(0, TubBottom + TubHeight / 2f, 0),
        });

        var body = new Node3D { Name = "Body" };
        AddChild(body);
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
        // the chassis: two wooden beams under the tub, and the axles
        foreach (var x in new[] { -RailTrack.Gauge / 2f + 0.12f, RailTrack.Gauge / 2f - 0.12f })
            Box(body, "Beam", new Vector3(0.12f, 0.1f, TubLength - 0.1f), new Vector3(x, TubBottom - 0.05f, 0), _wood);
        var axleY = RailTop + WheelRadius;
        foreach (var z in new[] { -halfL + 0.35f, halfL - 0.35f })
        {
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
        var plank = new Vector3(TubWidth - Wall * 2f, 0.05f, 0.3f);
        foreach (var row in new[] { 0, 2 })
            Box(body, "Seat", plank, new Vector3(0, InsideFloor + SeatHeight - 0.025f, SeatSpots[row].Y + 0.12f), _wood);
        var deckDepth = halfL - (SeatSpots[4].Y - 0.25f) - Wall;
        Box(body, "BenchDeck", new Vector3(TubWidth - Wall * 2f, BenchDeck, deckDepth), new Vector3(0, InsideFloor + BenchDeck / 2f, halfL - Wall - deckDepth / 2f), _wood);
        Box(body, "Bench", plank, new Vector3(0, InsideFloor + BenchDeck + SeatHeight - 0.025f, SeatSpots[4].Y + 0.12f), _wood);

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
    }

    private static void Box(Node3D parent, string name, Vector3 size, Vector3 at, Material material)
        => parent.AddChild(new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at });
}
