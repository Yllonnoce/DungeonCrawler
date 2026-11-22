using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A wagon of the party's train (plan 14): an open tub the cart's size, with no cage and no roof, coupled behind the cart from the landing
/// (<see cref="RailController.BuildTrain"/>), W1 nearest. A dumb prop like <see cref="MineCart"/>: the rail controller owns it, poses it
/// (<see cref="Pose"/>) and answers the interact key through <see cref="PromptFor"/> and <see cref="OnUse"/>. Its seat book has one hero
/// place (<see cref="TrainRules.HeroPlace"/>, at the cart's seat-0 spot) and the two crew seats of the bench (4, 5), and its rim one boarder
/// spot (the cart's rim spot 1, back right). Coupled it is shown, a thing to use (layer 8) and not solid; uncoupled it is lost: hidden, on
/// no layer, standing still. Its origin sits on the track; forward is −Z.
/// </summary>
public partial class TrainWagon : AnimatableBody3D, IInteractable, ICartBody
{
    /// <summary>The wagon's number in the train: 1 nearest the cart.</summary>
    public int Number { get; init; }
    /// <summary>Coupled behind the party's cart: the RailController poses it. Uncoupled it is lost (hidden, not solid).</summary>
    public bool Coupled { get; private set; }

    public CartSeats Seats { get; } = new();
    public Dictionary<int, ICartRider> Riders { get; } = new();
    public event Action<ICartRider, bool>? RiderChanged;
    public float Speed { get; set; }
    public float Curvature { get; set; }
    public Vector3 Velocity => -GlobalTransform.Basis.Z * Speed;

    /// <summary>The prompt for the hero looking at the wagon; null or "" offers nothing. Set by the RailController.</summary>
    public Func<Adventurer, string>? PromptFor { get; set; }
    /// <summary>A hero pressed interact on the wagon. Set by the RailController.</summary>
    public Action<Adventurer>? OnUse { get; set; }
    /// <summary>Its wire name: "wagon:{number}".</summary>
    public string NetRef => Net.NetRefs.Wagon(Number);

    /// <summary>The wagon's hero place is free.</summary>
    public bool HeroPlaceFree => Seats.OccupantOf(TrainRules.HeroPlace) == null;

    private readonly Marker3D[] _seats = new Marker3D[CartSeats.HeroSeats + CartSeats.BenchSeats];
    private StandardMaterial3D _iron = null!, _wood = null!;
    private bool _built;

    public TrainWagon()
    {
        SyncToPhysics = false;
        CollisionLayer = 0; // lost until coupled (the build couples every wagon)
        CollisionMask = 0;
        Visible = false;
    }

    public override void _Ready() => Build();

    public string Prompt => Main.Instance?.Party?.Local is { } me ? PromptFor?.Invoke(me) ?? "" : "";

    public void Interact(PartyController party, Adventurer actor) => OnUse?.Invoke(actor);

    /// <summary>
    /// On (true) the train behind the cart: shown and a thing to use (layer 8; the party walks through the train rather than get caught in it).
    /// Off (false): lost, hidden, on no layer, standing still.
    /// </summary>
    public void SetCoupled(bool coupled)
    {
        Coupled = coupled;
        Visible = coupled;
        CollisionLayer = coupled ? 8u : 0u;
        if (!coupled) Speed = 0f;
    }

    /// <summary>Where a rider in <paramref name="seat"/> sits, in world space, facing forward: the hero place and the bench's two (a seat it lacks reads the hero place).</summary>
    public Transform3D SeatTransform(int seat)
    {
        Build();
        var i = seat >= CartSeats.HeroSeats ? Math.Clamp(seat, CartSeats.HeroSeats, _seats.Length - 1) : TrainRules.HeroPlace;
        return _seats[i].GlobalTransform;
    }

    /// <summary>
    /// The rider (its seat booked, its own SitIn under way) rides here now. A crew member's name is raised by the bench seat it sits at
    /// (<see cref="CrewRules.LabelLift"/>), as a cage's, so the two names over a wagon stand apart.
    /// </summary>
    public void Admit(ICartRider rider)
    {
        Riders[rider.RiderId] = rider;
        if (rider is Npc n) n.SetLabelLift(CrewRules.LabelLift(Math.Max(0, n.Seat - CartSeats.HeroSeats)));
        RiderChanged?.Invoke(rider, true);
    }

    /// <summary>The rider leaves the book (its GetOut or a teleport): its seat is free again, and a crew member's name drops back in place.</summary>
    public void Release(ICartRider rider)
    {
        if (!Riders.TryGetValue(rider.RiderId, out var aboard) || aboard != rider) return;
        Riders.Remove(rider.RiderId);
        Seats.Leave(rider.RiderId);
        if (rider is Npc n) n.SetLabelLift(0f);
        RiderChanged?.Invoke(rider, false);
    }

    /// <summary>The rim spot a boarder takes: the cart's rim spot 1 (back right).</summary>
    private const int RimSpot = 1;
    public const int BoarderCount = 1;
    /// <summary>Who holds the rim's boarder spot (reserved at a leap's start), null when free.</summary>
    public Enemy?[] Boarders { get; } = new Enemy?[BoarderCount];

    /// <summary>The rim's boarder spot in world space: the cart's rim spot 1, <see cref="MineCart.BoarderLift"/> above the rim, facing in across the tub.</summary>
    public Transform3D BoarderSpotTransform(int i = 0) => GlobalTransform * MineCart.BoarderSpot(RimSpot);

    /// <summary>Take the rim spot for <paramref name="who"/>: 0, or −1 when it is held (a holder that is gone or dead frees it).</summary>
    public int ReserveBoarderSpot(Enemy who)
    {
        if (Boarders[0] == null || !IsInstanceValid(Boarders[0]) || Boarders[0]!.IsQueuedForDeletion() || !Boarders[0]!.IsAlive) { Boarders[0] = who; return 0; }
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
        // once per wagon, not static: a static resource outlives the scene tree and is reported as leaked at exit
        _iron = DungeonBuilder.Material("metal_plate_02", new Color(0.3f, 0.28f, 0.26f), 1f);
        _iron.Metallic = 0.8f;
        _wood = DungeonBuilder.Material("old_planks_02", new Color(0.45f, 0.33f, 0.22f), 1f);

        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new BoxShape3D { Size = new Vector3(MineCart.TubWidth, MineCart.TubHeight, MineCart.TubLength) },
            Position = new Vector3(0, MineCart.TubBottom + MineCart.TubHeight / 2f, 0),
        });
        var body = new Node3D { Name = "Body" }; // the fall's plunge copies it
        AddChild(body);
        MineCart.AddBoxTub(body, _iron, _wood);
        MineCart.AddChassis(body, _iron, _wood);
        MineCart.AddSeats(body, _wood, rows: 1); // the hero place's plank and the crew's bench

        for (var i = 0; i < _seats.Length; i++)
        {
            _seats[i] = new Marker3D { Name = $"Seat{i}", Position = MineCart.SeatSpot(i) };
            AddChild(_seats[i]);
        }
    }
}
