using DungeonCrawler.Adventure;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Runs the party's mine cart on a railed floor: a node under the floor root (it dies with the floor). It seats and
/// unseats heroes, counts down the boarding, reads the riders' inputs, steps the cart's <see cref="CartRun"/> from stop
/// to stop on the server (or solo), applies what <see cref="RunOverSweep"/> finds (<see cref="RunOverEffects"/>), keeps
/// the HUD line and the save, and poses the cart and its riders every rendered frame. The gates (<see cref="StopGates"/>),
/// the spare carts (<see cref="SpareYard"/>) and the coupled wagons (<see cref="CartTrain"/>) are its parts. A client
/// follows the server's cart state (<see cref="ApplyNet"/>, a <see cref="CartFollower"/>) and poses the cart, the train and
/// the riders by it, with the HUD line. Every change of the cart's state is told through <see cref="CartEvents"/> (server and solo).
/// </summary>
public partial class RailController : Node
{
    public static RailController? Instance { get; private set; }

    /// <summary>A hero mangled by the cart carries this as MangledBy: no hazard has it, so it always counts as armed.</summary>
    public const int HazardIndex = 1000;
    /// <summary>A player's own companions this close to them hop in when they board (single player: every companion; online: their bots).</summary>
    public const float CompanionBoardRange = 25f;
    /// <summary>The cart's HUD line shows while a hero is seated or this close.</summary>
    public const float HudRange = 12f;
    const float HudEvery = 0.25f;
    const string SourceName = RunOverRules.PartyCartSource;

    public MineCart Cart { get; private set; } = null!;
    /// <summary>The party's cart on the party track: its state, stepping and pose.</summary>
    public CartRun Party { get; private set; } = null!;
    public CartState State => Party.State;
    /// <summary>The stop the cart is at, or last left.</summary>
    public int StopIndex { get; private set; }
    /// <summary>Each stop's distance along the party track, ascending.</summary>
    public float[] StopS { get; private set; } = Array.Empty<float>();
    /// <summary>The cart's status line, or null when it is not shown.</summary>
    public string? HudText { get; private set; }
    public Boarding Boarding { get; } = new();
    /// <summary>Seconds since the stop's countdown started, any hold included (<see cref="Boarding.Elapsed"/>); 0 when no countdown runs.</summary>
    public float BoardingElapsed => Boarding.Running ? Boarding.Elapsed : 0f;
    /// <summary>
    /// The crew controller's hold on the departure from <paramref name="stop"/> (plan 14, Ruling 6: crew on the way to their seats, under
    /// <see cref="TrainRules.BoardMax"/>): passed as <see cref="Boarding.Tick"/>'s hold. Set by the crew controller; null holds nothing.
    /// </summary>
    public Func<int, bool>? CrewBoardingHolds { get; set; }

    // ---- the cage hold (plan 7) ------------------------------------------------------------------

    /// <summary>The stop's platform cage is locked with living crew (<see cref="CageHoldRules.Holds"/>). Set by the crew controller; null holds nothing.</summary>
    public Func<int, bool>? CageHolds { get; set; }
    /// <summary>
    /// The stops whose prisoners a player left behind (held interact in the seat). Not saved: a load holds the cart again at the stop
    /// it is restored at, and counts every stop before it as left (<see cref="CageHoldRules.LeftOnLoad"/>).
    /// </summary>
    private readonly HashSet<int> _crewLeftAt = new();
    /// <summary>The stops whose prisoners a player left behind (their crew count as lost).</summary>
    internal IReadOnlyCollection<int> CrewLeftAt => _crewLeftAt;
    /// <summary>The call of the first countdown the cage held back (kept by ??=): it starts with that call once the hold ends. Null when none waits.</summary>
    private bool? _heldStartCall;

    /// <summary>
    /// The cart stands at a stop whose platform cage is locked with living crew and nobody has left them behind: no countdown
    /// (<see cref="CageHoldRules.Held"/>). It does not depend on the gate (the HUD shows a gate down first). A client reads the server's flag.
    /// </summary>
    public bool CageHeld => NetSession.IsClient ? (_netFlags & (byte)CartFlags.CageHeld) != 0
        : CageHoldRules.Held(State.Mode, Halted, StopIndex, StopS.Length, _crewLeftAt.Contains(StopIndex), CageHolds?.Invoke(StopIndex) == true);

    /// <summary><paramref name="hero"/> may leave the prisoners behind now (seated in the held cart, on their feet): the prompt and the held interact ask it.</summary>
    private bool HoldFor(Adventurer hero) => CageHoldRules.MayLeaveBehind(hero.Cart == Cart, hero.Downed, CageHeld);

    private void Hold(Adventurer actor)
    {
        LeaveCrew(actor);
        Flush();
    }

    /// <summary>
    /// A seated player held interact (server or solo; a remote player's through its hold:cart:0 intent): the stop's prisoners are left
    /// behind, so the cage no longer holds the cart and the countdown held back starts (with its own call), or the gate's lift starts it.
    /// </summary>
    private void LeaveCrew(Adventurer actor)
    {
        if (NetSession.IsClient || !Current || !IsPlayer(actor) || !HoldFor(actor)) return;
        _crewLeftAt.Add(StopIndex);
        EventBus.Instance.Log($"{actor.State.Name} leaves the prisoners behind.");
        GD.Print($"[rails] the prisoners at stop {StopIndex} are left behind ({actor.State.Name})");
        if (!Boarding.Running && GateClear && !(Halted && LineBlocked) && PlayerAboard()) StartBoarding(_heldStartCall ?? true);
        RefreshHud();
    }

    // ---- the gates (StopGates) -------------------------------------------------------------------

    private StopGates _gates = null!;
    /// <summary>A post stands this far out from each lane's centre line (<see cref="StopGates.GateSide"/>).</summary>
    public const float GateSide = StopGates.GateSide;
    /// <summary>The winch works when no living holder of the stop is within this horizontal range of the gate (<see cref="StopGates.WinchClearRange"/>).</summary>
    public const float WinchClearRange = StopGates.WinchClearRange;

    /// <summary>The stop's gate stands open (or the stop has none). A stop's gate starts down while any of its mobs live.</summary>
    public bool GateUp(int stopIndex) => _gates.Up(stopIndex);

    /// <summary>The intermediate stops (indices) whose gate is up, for the save. Stops with no gate are not listed.</summary>
    public IReadOnlyList<int> GatesUpList => _gates.UpList;

    /// <summary>The stop's gate ("gate:{stop}"), or null.</summary>
    public RailGate? GateAt(int stopIndex) => _gates.At(stopIndex);

    /// <summary>Back from a save: the listed gates stand up at once (no animation, no log). Gates not listed stay as they are.</summary>
    public void RestoreGates(IEnumerable<int> up)
    {
        _gates.Restore(up);
        RefreshHud();
    }

    /// <summary>A gate went up (the check or the winch): riders waiting at its stop start the boarding.</summary>
    private void OnGateLifted(RailGate g)
    {
        if (g.Stop == StopIndex && State.Mode == CartMode.Held && !Halted && !Boarding.Running && PlayerAboard()) StartBoarding(call: true); // held at that stop, not halted on the line past it
        RefreshHud();
    }

    // ---- the train (CartTrain) -------------------------------------------------------------------

    private CartTrain _train = null!;
    private readonly List<TrainWagon> _wagons = new();
    /// <summary>Every wagon of the party's train built on this floor (<see cref="BuildTrain"/>), by number: W1 first. Coupled or lost.</summary>
    public IReadOnlyList<TrainWagon> Wagons => _wagons;
    /// <summary>The wagons coupled behind the cart, nearest first (W1..Wn, or none after a fall): the RailController poses them on the line behind it.</summary>
    public IReadOnlyList<TrainWagon> Train => _train.Wagons;
    /// <summary>From one body's middle to the next along the train (<see cref="TrainRules.Spacing"/>).</summary>
    public const float WagonSpacing = CartTrain.WagonSpacing;

    /// <summary>The train's wagon <paramref name="number"/> (1-based), coupled or lost, or null when there is none.</summary>
    public TrainWagon? WagonAt(int number) => number >= 1 && number <= _wagons.Count && IsInstanceValid(_wagons[number - 1]) ? _wagons[number - 1] : null;

    /// <summary>A cage's or a spare's siding cell: riders getting out (and enemies knocked aside) are never set down on it.</summary>
    public void ReserveSiding(GridPos cell) => _train.ReserveSiding(cell);

    /// <summary>
    /// The crew controller's offer on a coupled wagon to <paramref name="hero"/> ("Revive {name}" for a downed crew member seated in it), or "" or null for none; the wagon's prompt
    /// (<see cref="WagonPrompt"/>) is the rail controller's and asks it. Set by the crew controller.
    /// </summary>
    public Func<TrainWagon, Adventurer, string>? WagonCrewPrompt { get; set; }
    /// <summary>The crew controller's use of a coupled wagon (what <see cref="WagonCrewPrompt"/> offered). Set by the crew controller.</summary>
    public Action<TrainWagon, Adventurer>? WagonCrewUse { get; set; }

    /// <summary>
    /// The party's train (plan 14), on every role, once per build of the floor (the crew controller asks, one wagon per cage placed:
    /// <see cref="TrainRules.WagonsFor"/>): Wagon1..Wagon{count} under the floor root, all coupled behind the cart where it stands. A floor
    /// with no cage has no train.
    /// </summary>
    public void BuildTrain(int count)
    {
        if (_wagons.Count > 0 || count <= 0 || GetParent() is not Node3D root) return;
        for (var k = 1; k <= count; k++)
        {
            var wagon = new TrainWagon { Name = $"Wagon{k}", Number = k };
            wagon.PromptFor = hero => WagonPrompt(wagon, hero);
            wagon.OnUse = hero => UseWagon(wagon, hero);
            root.AddChild(wagon);
            _wagons.Add(wagon);
        }
        if (NetSession.IsClient) _train.CoupleAll(_wagons); // as the server builds it: the cart states keep it the server's from here
        else CoupleFresh("the build");
    }

    /// <summary>
    /// The cart restored whole (server and solo; the build, a <see cref="Restore"/>, a spare's takeover after a fall): every wagon not coupled
    /// is emptied (a rider left in it is set down quietly beside the lane), shown and coupled again, the train W1..Wn in number order.
    /// </summary>
    public void CoupleFresh(string why)
    {
        if (NetSession.IsClient || _wagons.Count == 0) return;
        foreach (var wagon in _wagons)
            if (IsInstanceValid(wagon) && !wagon.Coupled) EmptyWagon(wagon);
        _train.CoupleAll(_wagons);
        GD.Print($"[rails] train: W1..W{_wagons.Count} coupled at stop {StopIndex} ({why})");
    }

    /// <summary>A lost wagon coupled fresh: anyone still in its book is set down beside the lane where they are (no throw, no log), its boarder spot free.</summary>
    private void EmptyWagon(TrainWagon wagon)
    {
        _fallRiders.Clear();
        _fallRiders.AddRange(wagon.Riders.Values);
        foreach (var rider in _fallRiders)
            if (rider is Node3D body && IsInstanceValid(body)) rider.GetOut(StandBesideLane(body.GlobalPosition), Vector3.Zero);
        _fallRiders.Clear();
        foreach (var occupant in wagon.Seats.Occupants) wagon.Seats.Leave(occupant);
        wagon.Riders.Clear();
        for (var i = 0; i < wagon.Boarders.Length; i++) wagon.Boarders[i] = null;
    }

    /// <summary>The car <paramref name="rider"/> sits in: 0 the party's cart, k the coupled wagon Wk, −1 none (on foot, or another cart).</summary>
    public int CarOf(ICartRider rider)
    {
        if (rider.Cart == null) return -1;
        if (rider.Cart == Cart) return 0;
        return rider.Cart is TrainWagon { Coupled: true } wagon && WagonAt(wagon.Number) == wagon ? wagon.Number : -1;
    }

    /// <summary>A wagon's prompt for <paramref name="hero"/>, on every peer: a lost wagon offers nothing; a coupled one what the crew controller offers.</summary>
    private string WagonPrompt(TrainWagon wagon, Adventurer hero) => wagon.Coupled ? WagonCrewPrompt?.Invoke(wagon, hero) ?? "" : "";

    /// <summary>A hero pressed interact on a wagon: the crew controller's use (<see cref="WagonCrewUse"/>) of a coupled one.</summary>
    private void UseWagon(TrainWagon wagon, Adventurer actor)
    {
        if (!wagon.Coupled) return;
        WagonCrewUse?.Invoke(wagon, actor);
        Flush();
    }

    // ---- spare carts (SpareYard) -----------------------------------------------------------------

    private SpareYard _spares = null!;
    /// <summary>Intermediate stops (indices) whose spare has been used, for the save.</summary>
    public IReadOnlyCollection<int> SparesUsed => _spares.Used;
    /// <summary>Spares still parked.</summary>
    public int SparesLeft => _spares.Left;
    /// <summary>The stop's spare cart ("spare:{stop}"), or null.</summary>
    public MineCart? SpareAt(int stop) => _spares.At(stop);
    /// <summary>Client: <paramref name="spare"/> spent or parked again, as the server said. True when it changed.</summary>
    public bool ShowSpare(MineCart spare, bool used) => _spares.StopOf(spare) is var stop and >= 0 && _spares.Show(stop, used);
    /// <summary>Client: the stop's spare spent or parked again (the prop check's repair). True when it changed.</summary>
    public bool ShowSpare(int stop, bool used) => _spares.Show(stop, used);

    /// <summary>
    /// Every change of the cart's state: departed, arrived, hit, wrecked, a rider seated or unseated (whatever seated or
    /// unseated it: the cart tells), restored from a save. Queued as they happen and told at the end of the physics tick
    /// (or of the call that seated, unseated or restored), never in the middle of a pass over the riders.
    /// </summary>
    public event Action<CartEvent>? CartEvents;

    private RailTrack _track = null!;
    private FloorLayout _layout = null!;
    private DungeonBuilder _builder = null!;
    private float _hudTimer;
    /// <summary>Physics time (seconds since the controller's first tick): the run-over and warning throttles count in it.</summary>
    private double _now;
    private readonly HashSet<GridPos> _exitTaken = new();
    private readonly Dictionary<ulong, double> _hitAt = new();
    private readonly Dictionary<int, double> _warnedAt = new();
    private readonly List<Node3D> _bodies = new();
    private readonly List<(Node3D body, TrackBody tb)> _hits = new();
    private readonly Func<Vector3, Vector3> _setAside;
    private readonly Action<Node3D> _onHit;
    private readonly List<ICartRider> _aboard = new();
    private readonly List<int> _gone = new();
    private readonly List<CartEvent> _pending = new();
    /// <summary>A rider is being thrown from the moving cart: its unseat event says so.</summary>
    private bool _throwing;
    /// <summary>Hero indices the save had seated in the cart: the deferred pass after a load sets them down beside it.</summary>
    private readonly HashSet<int> _restoredRiders = new();
    const float WarnEvery = RideSound.BellEvery;
    const float WreckHpFraction = 0.25f;
    const float BodyRadius = RunOverSweep.BodyRadius;

    public RailController()
    {
        Name = "RailController";
        ProcessPriority = -100; // pose the cart before the heroes' own _Process (their cameras) runs
        _benchOrder = BenchOrder;
        _setAside = pos => _builder.Surface(ExitCellNear(pos, true, false));
        _onHit = body => Raise(new CartEvent(CartEventKind.Hit, Target: body.GetInstanceId()));
    }

    /// <summary>Read the stops off the track and park the cart at the first. Call before adding the controller to the tree.</summary>
    public void Setup(DungeonBuilder builder, FloorLayout layout, RailTrack track, Node3D root)
    {
        _builder = builder;
        _layout = layout;
        _track = track;
        // stop i is marker RailStop{i}: the save keeps a stop index, so the order is the markers' and is never re-sorted
        var stops = new List<(int Marker, float S)>();
        var problem = (string?)null;
        for (var i = 0; i < (layout.Rail?.Stops.Count ?? 0); i++)
        {
            if (root.GetNodeOrNull<Node3D>($"RailStop{i}") is not { } marker) { problem = $"marker RailStop{i} is missing"; break; }
            var s = track.Curve.GetClosestOffset(track.ToLocal(marker.GlobalPosition));
            if (stops.Count > 0 && s <= stops[^1].S) { problem = $"RailStop{i} lies at S={s:0.0}, not past RailStop{stops[^1].Marker} at S={stops[^1].S:0.0}"; break; }
            stops.Add((i, s));
        }
        if (problem != null)
        {
            GD.PushError($"[RailController] the stops do not run along the party track ({problem}): no cart on this floor");
            stops.Clear();
        }
        StopS = stops.Select(p => p.S).ToArray();
        Cart = new MineCart { PromptFor = PromptFor, OnUse = Use, HoldFor = HoldFor, OnHold = Hold };
        var start = StopS.Length > 0 ? StopS[0] : 0f;
        Party = new CartRun(new TrackSampler(track), Cart, new CartState(start, 0f, CartMode.Held, CartRules.MaxPlating)) { Limit = start };
        Cart.RiderChanged += OnRiderChanged;
        root.AddChild(Cart);
        SetCartLayer();
        _train = new CartTrain(Party);
        _gates = new StopGates(builder, layout, OnGateLifted, RefreshHud);
        _gates.Build(root, StopS, Party.Track);
        _spares = new SpareYard(builder, layout, track, _train.Sidings);
        // online a spare is a thing to use too: a co-op player left behind takes it after the train (RailController.Chase); solo it is looks only
        if (NetSession.IsNetworked) _spares.Build(root, StopS.Length, SparePrompt, UseSpare);
        else _spares.Build(root, StopS.Length);
    }

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Party.PoseAt(State.S);
        AddSoundscape();
    }

    public override void _ExitTree()
    {
        if (Instance != this) return;
        Instance = null;
        Main.Instance?.Hud?.SetCart(null, null);
    }

    /// <summary>The floor this controller belongs to is still the one built (a cleared floor waits a frame to be freed).</summary>
    private bool Current => Main.Instance is { InGame: true } main && main.Builder.PartyTrack == _track && IsInstanceValid(Cart);

    private void Raise(CartEvent e) => _pending.Add(e);

    /// <summary>
    /// Tell the queued events, in order (one a listener raises meanwhile is told too). The orc taunts a ride's start and a wreck
    /// (server and solo: the director's RailTaunt; both urgent, the start held for a line being said), unless a rider's own call
    /// carries the departure (<see cref="ClaimDeparture"/>): the taunt is raised after the listeners have heard the event. A listener that
    /// throws is logged and costs neither that event's taunt nor the rest of the batch.
    /// </summary>
    private void Flush()
    {
        if (_pending.Count == 0) return;
        try
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                var e = _pending[i];
                _departureClaimed = false;
                try { CartEvents?.Invoke(e); }
                catch (Exception ex) { GD.PushError($"[rails] a CartEvents listener threw on {e.Kind} (stop {e.Stop}): {ex}"); }
                if (e.Kind is CartEventKind.Arrived or CartEventKind.Restored) SnapshotSoon();
                if (RideLines.TauntFor(e.Kind, e.Stop, StopS.Length) is { } taunt)
                {
                    if (taunt == Adventure.Objectives.TauntTrigger.RideStart && _departureClaimed)
                    {
                        GD.Print($"[rails] departed stop {e.Stop}: a rider's call carries the departure, no ride-start taunt");
                        continue;
                    }
                    var told = AdventureDirector.Instance?.RailTaunt(taunt, urgent: true) == true;
                    if (taunt == Adventure.Objectives.TauntTrigger.RideStart)
                    {
                        RideStartTaunted = told;
                        GD.Print($"[rails] departed stop {e.Stop}: ride-start taunt said or held={told}");
                    }
                }
            }
        }
        finally { _pending.Clear(); }
    }

    private bool _departureClaimed;

    /// <summary>
    /// A listener of the departure being told (Grisel naming the cage ahead) says its own line for it: the orc's RideStart taunt is not
    /// raised for this one. It works only when called synchronously inside a <see cref="CartEvents"/> handler, while <see cref="Flush"/>
    /// tells that Departed event: the claim is cleared before each event and read right after its listeners return, so a call made
    /// later (a deferred call, a timer, a held line's callback) claims nothing.
    /// </summary>
    public void ClaimDeparture() => _departureClaimed = true;

    /// <summary>Whether the last departure's RideStart taunt was said or held (false: dropped; null: no ride has started). For the scripted checks.</summary>
    public bool? RideStartTaunted { get; private set; }

    /// <summary>The cart's book changed, by any path (boarding, getting out, a teleport, a takeover, a load).</summary>
    private void OnRiderChanged(ICartRider rider, bool seated) =>
        Raise(new CartEvent(seated ? CartEventKind.Seated : CartEventKind.Unseated, Rider: rider.RiderId, Thrown: !seated && _throwing));

    /// <summary>The cart's riders, in rider-id order (heroes by party index), for a pass that may unseat them. Freed riders are dropped from the book.</summary>
    private List<ICartRider> Aboard()
    {
        _aboard.Clear();
        _gone.Clear();
        foreach (var (id, rider) in Cart.Riders)
            if (rider is GodotObject o && !IsInstanceValid(o)) _gone.Add(id);
            else _aboard.Add(rider);
        foreach (var id in _gone) { Cart.Riders.Remove(id); Cart.Seats.Leave(id); Raise(new CartEvent(CartEventKind.Unseated, Rider: id)); }
        for (var i = 1; i < _aboard.Count; i++) // insertion sort: six riders at most, and no comparer to allocate
            for (var j = i; j > 0 && _aboard[j - 1].RiderId > _aboard[j].RiderId; j--) (_aboard[j - 1], _aboard[j]) = (_aboard[j], _aboard[j - 1]);
        return _aboard;
    }

    // ---- save ------------------------------------------------------------------------------------

    /// <summary>What the save keeps: the stop the cart has reached (a ride under way keeps the stop it left).</summary>
    public int Capture() => StopIndex;

    /// <summary>The stops whose spare cart is spent, or null when none is.</summary>
    public List<int>? CaptureSpares() => _spares.Capture();

    /// <summary>The heroes seated in the cart (their indices), or null when nobody is: a load sets them down beside the cart.</summary>
    public List<int>? CaptureRiders()
    {
        List<int>? riders = null;
        if (Main.Instance?.Party is { } party)
            foreach (var hero in party.Adventurers)
                if (IsInstanceValid(hero) && hero.Seated && (hero.Cart == Cart || hero.Cart == Chaser)) (riders ??= new()).Add(hero.Index); // a chaser's riders too: the chase is not saved, a load sets them down
        return riders;
    }

    /// <summary>
    /// Back from a save: the cart Held at the stop it had reached (clamped to the line), plating full, nobody aboard.
    /// A wrecked cart comes back whole at its last stop (no spare is spent for it). The heroes saved as riders (their saved
    /// poses were seats, mid-line on a ride under way), and anyone set down on a rail cell, step out beside the cart.
    /// </summary>
    public void Restore(int stopIndex, IReadOnlyCollection<int>? riders = null, IEnumerable<int>? sparesUsed = null)
    {
        if (NetSession.IsClient) return; // a join or revisit: a client's cart stays parked at the first stop and nobody's seat is its to change (co-op riding is plan 5)
        EndChase(atOnce: true); // a load ends a chase at once (its riders set down beside the chaser); the chase is not saved, and the spares stand as the save has them (_spares.Restore below)
        FreeChaser();
        ResetRelief(); // a relief is not saved either: the clock starts clean and the standing cart has met nobody yet
        LastWreckFell = false;
        _arrivedStop = -1;
        _spares.Restore(sparesUsed);
        _restoredRiders.Clear();
        if (riders != null) foreach (var i in riders) _restoredRiders.Add(i);
        if (StopS.Length == 0) return;
        StopIndex = Math.Clamp(stopIndex, 0, StopS.Length - 1);
        Party.Reset(new CartState(StopS[StopIndex], 0f, CartMode.Held, CartRules.MaxPlating));
        Cart.Visible = true; // a cart hidden by a wreck with no spare is back
        SetCartLayer();
        Party.Limit = StopS[StopIndex];
        ResetLine();
        Boarding.Cancel();
        _crewLeftAt.Clear(); // the waiver is not saved: the cage at the stop the cart is restored at holds again,
        foreach (var s in CageHoldRules.LeftOnLoad(StopIndex)) _crewLeftAt.Add(s); // but a cage still locked at a stop the cart is past was left behind
        _heldStartCall = null;
        _hitAt.Clear();
        _warnedAt.Clear();
        foreach (var rider in Aboard()) // everyone still in a seat gets up where they sit (as a teleport does); ClearRiders sets them down
        {
            if (rider is Adventurer hero) hero.Teleport(hero.GlobalPosition);
            else rider.GetOut(((Node3D)rider).GlobalPosition, Vector3.Zero);
        }
        foreach (var occupant in Cart.Seats.Occupants) Cart.Seats.Leave(occupant); // and nothing is left in the book
        Cart.Riders.Clear();
        Party.Sync();
        Party.PoseAt(State.S);
        Cart.Sound?.Prime(); // back from a save: the jump to the stop makes no sound
        CoupleFresh("restored whole"); // the cart comes back whole, its train with it (wagons a fall took couple fresh, empty)
        Raise(new CartEvent(CartEventKind.Restored, Stop: StopIndex));
        ClearRiders();
        ReturnAllFallen();
        Flush();
        Callable.From(() => { ClearRiders(); ReturnAllFallen(); _restoredRiders.Clear(); Flush(); }).CallDeferred(); // the party is set down after the floor state is applied: look again once it stands
    }

    // ---- the wipe's rise (ruling 14, 15) -----------------------------------------------------------

    /// <summary>
    /// The floor as it stood just after the cart last arrived at a stop or was restored there (server and solo; null on a client and before
    /// the first): a wipe on this floor rises there with the train as it was then (<see cref="Save.RailArrival.Merge"/>).
    /// </summary>
    public Save.FloorStateDto? ArrivalSnapshot { get; private set; }
    private bool _snapshotDue;

    /// <summary>An Arrived or Restored event was told: the floor is captured once the frame's work is done (the set-downs, the crew's seats).</summary>
    private void SnapshotSoon()
    {
        if (NetSession.IsClient || _snapshotDue) return;
        _snapshotDue = true;
        Callable.From(TakeArrivalSnapshot).CallDeferred();
    }

    private void TakeArrivalSnapshot()
    {
        _snapshotDue = false;
        if (!IsInstanceValid(this) || !Current || Main.Instance.Builder.FloorRoot is not { } root || GetParent() != root || Main.Instance.Exploration is not { } exploration) return;
        ArrivalSnapshot = Save.FloorStateCapture.Capture(root, _layout, Main.Instance.Builder.Contents, exploration);
        var alive = 0;
        foreach (var npc in ArrivalSnapshot.NpcStates) if (!npc.Dead) alive++;
        GD.Print($"[rails] arrival snapshot at stop {StopIndex}: train [{string.Join(",", _train.Wagons.Select(w => w.Number))}] npcs alive {alive}");
    }

    /// <summary>
    /// The wipe's rise (server and solo, after the rebuild's <see cref="Restore"/> and its deferred set-down): every hero in the world
    /// (remote players' too: the server places them before their clients are ready, and the floor-ready ForcePose lands them there)
    /// and every escort on foot, each on its own cell beside the cart (<see cref="ExitCellNear"/>: never a drop, a rail or a siding),
    /// facing it. No spare is spent. Returns the heroes placed.
    /// </summary>
    public int RiseBesideCart()
    {
        if (NetSession.IsClient || !Current || _layout?.Rail == null || StopS.Length == 0) return 0;
        _exitTaken.Clear();
        var placed = 0;
        foreach (var hero in Main.Instance.Party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked) continue;
            var cell = ExitCellNear(Cart.GlobalPosition, atStop: true, useTaken: true);
            _exitTaken.Add(cell);
            var at = _builder.Surface(cell) + Vector3.Up * 0.3f;
            hero.Teleport(at, Cart.GlobalPosition); // out of any seat first (a teleport frees it)
            if (hero.Remote != null) { hero.Remote.Position = at; hero.Remote.Velocity = Vector3.Zero; }
            placed++;
        }
        foreach (var child in GetParent().GetChildren())
            if (child is Npc { Dead: false, Escorting: true, Seated: false } npc)
            {
                var cell = ExitCellNear(Cart.GlobalPosition, atStop: true, useTaken: true);
                _exitTaken.Add(cell);
                npc.GlobalPosition = _builder.Surface(cell);
                npc.Velocity = Vector3.Zero;
                npc.FaceToward(Cart.GlobalPosition);
            }
        _exitTaken.Clear();
        GD.Print($"[rails] wipe-rise: {placed} hero(es) beside the cart at stop {StopIndex}");
        RefreshHud();
        return placed;
    }

    /// <summary>A restore (a load, a wipe's rise) brings every fallen hero back beside the cart at its stop (ruling 8).</summary>
    private void ReturnAllFallen()
    {
        if (Current) ReturnFallen(Main.Instance.Party, Cart.GlobalPosition, $"the cart restored at stop {StopIndex}");
    }

    /// <summary>
    /// Nobody rides a restored cart: a hero still holding a seat (this cart's or a freed one's) is unseated where it stands,
    /// and a body the save had seated (its pose was a seat, maybe mid-line), set down inside the parked cart, or standing on
    /// a rail cell (party or enemy lane) steps out beside the cart at its stop, each on its own cell.
    /// </summary>
    private void ClearRiders()
    {
        if (!Current) return;
        _exitTaken.Clear();
        foreach (var hero in Main.Instance.Party.Adventurers)
        {
            if (!IsInstanceValid(hero)) continue;
            if (hero.Seated) hero.Teleport(hero.GlobalPosition); // Teleport frees the seat and stands the body up
            if (hero.Parked || hero.Control == ControlKind.Remote) continue;
            var pos = hero.GlobalPosition;
            if (!_restoredRiders.Contains(hero.Index) && !InsideCart(pos) && !_layout.IsRail(DungeonBuilder.WorldToCell(pos))) continue;
            var cell = ExitCell();
            _exitTaken.Add(cell);
            hero.Teleport(_builder.Surface(cell) + Vector3.Up * 0.3f, Cart.GlobalPosition);
        }
        // an escort (Grisel, an ally) left standing on a rail cell by the save steps out beside the cart as well
        foreach (var child in GetParent().GetChildren())
            if (child is Npc { Dead: false, Escorting: true, Seated: false } npc && _layout.IsRail(DungeonBuilder.WorldToCell(npc.GlobalPosition)))
            {
                var cell = ExitCell();
                _exitTaken.Add(cell);
                npc.GlobalPosition = _builder.Surface(cell);
            }
        RefreshHud();
    }

    private bool InsideCart(Vector3 pos)
    {
        var local = Cart.GlobalTransform.AffineInverse() * pos;
        return Mathf.Abs(local.X) <= MineCart.TubWidth / 2f + BodyRadius && Mathf.Abs(local.Z) <= CartRules.HalfLength + BodyRadius
            && local.Y > -1f && local.Y < MineCart.TubBottom + MineCart.TubHeight + 1f;
    }

    /// <summary>Test hook (--test-ride): set the cart's plating directly.</summary>
    internal void DebugSetPlating(float plating)
    {
        Party.State = State with { Plating = Mathf.Clamp(plating, 0f, CartRules.MaxPlating) };
        RefreshHud();
    }

    // ---- boarding --------------------------------------------------------------------------------

    /// <summary>
    /// The countdown starts at the stop the cart is held at (a Boarding event says where). With <paramref name="call"/> (someone got
    /// in, or the stop's gate lifted: the party may be out fighting) a seated hero calls all aboard (<see cref="RailCalls"/>); a
    /// countdown restarted under riders who never got out (an arrival, a cleared halt) goes uncalled. At a stop whose locked cage holds
    /// the cart (<see cref="CageHeld"/>) nothing starts: a CageHeld event is raised and the first such start's call is kept for later.
    /// </summary>
    private void StartBoarding(bool call = false)
    {
        if (Halted && LineBlocked) return; // held at an obstacle on the line: nobody leaves until it is cleared
        if (CageHeld) { _heldStartCall ??= call; Raise(new CartEvent(CartEventKind.CageHeld, Stop: StopIndex)); return; }
        _heldStartCall = null;
        Boarding.Start();
        Raise(new CartEvent(CartEventKind.Boarding, Stop: Halted ? -1 : StopIndex));
        if (call && RideLines.BarkFor(CartEventKind.Boarding) is { } bark && Main.Instance?.Party is { } party) RailCalls.Say(party, Cart, bark, "All aboard!");
    }

    /// <summary>The cart's prompt for <paramref name="hero"/>, on every peer (a client's state is the server's: its mode, stop and seat book).</summary>
    private string PromptFor(Adventurer hero)
    {
        if (LeapOffered(hero)) return LeapRules.BackPrompt; // first: from a slag cart's deck, whatever the aim (plan 13)
        if (hero.Seated && hero.Cart != Cart) return ""; // in another seat book
        if (HoldFor(hero)) return CageHoldRules.SeatedPrompt; // a tap still gets out; a held press leaves the prisoners behind
        var seatedHere = hero.Cart == Cart;
        var inReach = seatedHere || RideInputRules.InBoardReach(Cart.TubDistance(hero)); // the server's intent check measures the same
        return RideInputRules.BoardPrompt(seatedHere, State.Mode == CartMode.Held, StopIndex >= StopS.Length - 1, HeroSeatFree(), inReach, holdOn: HoldsRiders);
    }

    /// <summary>Ride 4's leap offers <paramref name="hero"/> this cart back from a slag cart's deck (plan 13), on every role.</summary>
    private bool LeapOffered(Adventurer hero) => RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.LeapOfferFor(hero) == Cart;

    /// <summary>The train rolls on ride 4's collapsing bridge (plan 13, Ruling 6): "Get out" is neither offered nor taken, on every role.</summary>
    private static bool HoldsRiders => RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.HoldsRiders();

    /// <summary>A hero seat (0-3) of the cart's book is free. No allocation.</summary>
    private bool HeroSeatFree()
    {
        for (var i = 0; i < CartSeats.HeroSeats; i++) if (Cart.Seats.OccupantOf(i) == null) return true;
        return false;
    }

    /// <summary>A player's hero: the one played here (solo) or another player's on the server.</summary>
    private static bool IsPlayer(Adventurer hero) => hero.Control is ControlKind.LocalHuman or ControlKind.Remote;

    /// <summary>
    /// <paramref name="mate"/> is <paramref name="player"/>'s own companion: solo every companion is; online, a hero bot whose owner is
    /// that player (one player's bots never ride off in another's cart).
    /// </summary>
    private static bool OwnCompanion(Adventurer mate, Adventurer player)
    {
        if (mate.Control != ControlKind.Companion) return false;
        if (!NetSession.IsServer || NetSession.Instance.Server is not { } world) return true;
        Net.PlayerSlot? bot = null, owner = null;
        var slots = world.Registry.All; // by index: no allocation (the hop-in asks four times a second)
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].MemberIndex == mate.Index) bot ??= slots[i];
            if (slots[i].MemberIndex == player.Index) owner ??= slots[i];
        }
        return bot is { IsBot: true } && owner != null && string.Equals(bot.BotOwner, owner.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A seated, awake player in this cart owns <paramref name="mate"/> (<see cref="OwnCompanion"/>). No allocation.</summary>
    private bool OwnerAboard(Adventurer mate)
    {
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer player && IsInstanceValid(player) && !player.Downed && !player.Parked && IsPlayer(player) && OwnCompanion(mate, player)) return true;
        return false;
    }

    private void Use(Adventurer actor)
    {
        if (actor.Cart is SlagCart) // "Leap back" (plan 13): the leap's rules judge it (LeapAccepts), the server's slack included; it is never a boarding
        {
            if (!NetSession.IsClient && RailEncounterController.Instance is { } enc && IsInstanceValid(enc)) enc.LeapBack(actor);
            Flush();
            return;
        }
        UseCart(actor);
        Flush();
    }

    /// <summary>
    /// A hero uses the cart (server or solo; a remote player's through its InteractIntent, reach checked there): seated, it gets out;
    /// on foot, it boards a Held cart short of the end of the line with a hero seat free. A player who boards brings their own
    /// companions within <see cref="CompanionBoardRange"/> and the escorts onto the bench; the gate is judged only to start the countdown.
    /// </summary>
    private void UseCart(Adventurer actor)
    {
        if (NetSession.IsClient || !Current) return;
        if (actor.Cart == Cart)
        {
            if (RideInputRules.MayGetOut(seatedHere: true, actor.Downed, holdOn: HoldsRiders)) GetOutWithParty(actor); // a downed rider stays slumped in the seat; nobody gets out on the collapsing bridge while it rolls (solo and a remote seat's press alike)
            return;
        }
        if (actor.Seated || actor.Downed || actor.Parked || State.Mode != CartMode.Held) return;
        if (StopIndex >= StopS.Length - 1) return; // the end of the line: nowhere left to ride (the prompt offers nothing either)
        var player = IsPlayer(actor);
        if (!player && !PlayerAboard()) return; // a companion rides only with a player
        if (!SeatHero(actor)) return;
        if (player)
        {
            foreach (var mate in Main.Instance.Party.Active)
                if (mate != actor && !mate.Downed && !mate.Seated && OwnCompanion(mate, actor)
                    && mate.GlobalPosition.DistanceTo(actor.GlobalPosition) <= CompanionBoardRange)
                    SeatHero(mate); // they hop in; walking to the cart is polish for later
            SeatBench(actor.GlobalPosition);
        }
        if (!Boarding.Running && StopIndex < StopS.Length - 1 && GateClear) StartBoarding(call: true);
        RefreshHud();
    }

    /// <summary>
    /// A companion on foot whose lead rides this cart asks for a seat instead of regrouping onto the lead (into the tub,
    /// or ahead of a moving cart): it hops in, as boarding seats it, while the cart is held at a stop short of the end of
    /// the line with a player aboard. False otherwise (the cart rolling, wrecked, at the end, full, or no player in it).
    /// </summary>
    public bool TryHopIn(Adventurer hero)
    {
        var seated = HopIn(hero);
        Flush();
        return seated;
    }

    private bool HopIn(Adventurer hero)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(hero)) return false;
        if (State.Mode != CartMode.Held || StopIndex >= StopS.Length - 1) return false;
        if (hero.Control != ControlKind.Companion || hero.Parked || hero.Seated || hero.Downed) return false;
        if (!PlayerAboard() || !SeatHero(hero)) return false;
        if (!Boarding.Running && GateClear) StartBoarding(call: true);
        RefreshHud();
        return true;
    }

    /// <summary>A player's hero (played here, or another player's on the server) sits in this cart, awake: the cart only leaves (and companions only board) with one aboard.</summary>
    private bool PlayerAboard()
    {
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero) && !hero.Downed && !hero.Parked && IsPlayer(hero)) return true;
        return false;
    }

    /// <summary>
    /// Server: a player left the game while seated (before their body is parked): it steps out beside the lane where the cart is, with
    /// no throw, no run-over and no log, and the seat is freed (the SeatMsg goes out with the tick). The parked pose is that spot.
    /// </summary>
    public void UnseatQuietly(Adventurer hero)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(hero)) return;
        if (hero.Cart != Cart)
        {
            if (hero.Cart == Chaser) { UnseatChaserQuietly(hero); RefreshHud(); Flush(); }
            return;
        }
        var at = _builder.Surface(ExitCellNear(Cart.GlobalPosition, atStop: true, useTaken: false));
        hero.GetOut(at, Vector3.Zero); // the cart tells it (Unseated), never as a throw
        RefreshHud();
        Flush();
    }

    /// <summary>Every rider steps out beside the lane at the stop (the cart is held there): nobody rides on without a player.</summary>
    private void SetDownRiders()
    {
        if (Cart.Riders.Count == 0) return;
        _exitTaken.Clear();
        foreach (var rider in Aboard()) GetOut(rider);
        RefreshHud();
    }

    /// <summary>Companions on foot who come this close to a held cart with a player aboard hop in.</summary>
    const float HopInRange = 3f;
    const float HopEvery = 0.25f;
    private float _hopTimer;

    private void HopInNearby(PartyController party)
    {
        if (StopIndex >= StopS.Length - 1 || !PlayerAboard()) return;
        var at = Cart.GlobalPosition;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Control != ControlKind.Companion || hero.Parked || hero.Seated || hero.Downed) continue;
            if (hero.GlobalPosition.DistanceSquaredTo(at) > HopInRange * HopInRange) continue;
            if (!OwnerAboard(hero)) continue; // another player's bot waits for its own player (solo: the one player owns every companion)
            if (SeatHero(hero)) RefreshHud();
        }
        SeatBench(at);
    }

    /// <summary>
    /// Escorts and allies ride the rear bench (seats 4-5): a living, up NPC on foot, following the party, within
    /// <see cref="CompanionBoardRange"/> of <paramref name="from"/>, Grisel first and then by distance. Crew ride their wagons instead.
    /// </summary>
    private void SeatBench(Vector3 from)
    {
        if (StopIndex >= StopS.Length - 1 || !PlayerAboard()) return;
        SeatBench(Cart, from);
    }

    /// <summary>
    /// The bench of <paramref name="cart"/> (the party's, or a left-behind player's chaser) takes the escorts and allies on foot near
    /// <paramref name="from"/>, as <see cref="SeatBench(Vector3)"/> describes: one rule for both carts.
    /// </summary>
    private void SeatBench(MineCart cart, Vector3 from)
    {
        if (GetParent() is not { } floor) return;
        _bench.Clear();
        for (var i = 0; i < floor.GetChildCount(); i++)
            if (floor.GetChild(i) is Npc { Dead: false, Downed: false, Escorting: true, Seated: false, Crew: false } npc
                && npc.GlobalPosition.DistanceTo(from) <= CompanionBoardRange) _bench.Add(npc);
        if (_bench.Count == 0) return;
        _benchFrom = from;
        _bench.Sort(_benchOrder);
        foreach (var npc in _bench)
        {
            if (cart.Seats.Take(npc.RiderId, isHero: false) is not int seat) break; // the bench is full
            npc.SitIn(cart, seat);
            RefreshHud();
        }
    }
    private static bool IsGrisel(Npc npc) => npc.Id == Adventure.Objectives.CrewRescueController.GriselId;
    private readonly List<Npc> _bench = new();
    private Vector3 _benchFrom;
    private readonly Comparison<Npc> _benchOrder;
    private int BenchOrder(Npc a, Npc b)
    {
        var byGrisel = IsGrisel(b).CompareTo(IsGrisel(a)); // Grisel first
        return byGrisel != 0 ? byGrisel : a.GlobalPosition.DistanceSquaredTo(_benchFrom).CompareTo(b.GlobalPosition.DistanceSquaredTo(_benchFrom));
    }

    private bool SeatHero(Adventurer hero)
    {
        if (Cart.Seats.Take(hero.RiderId, isHero: true) is not int seat) return false;
        hero.SitIn(Cart, seat); // the cart tells it (Seated)
        return true;
    }

    /// <summary>
    /// The actor gets out; when a player steps out of a slow cart, their own seated companions follow, and the bench too once no player
    /// is left aboard (a player thrown out at speed leaves them riding).
    /// </summary>
    private void GetOutWithParty(Adventurer actor)
    {
        _exitTaken.Clear();
        var slow = State.V < RunOverRules.ThrownAtSpeed;
        GetOut(actor);
        if (IsPlayer(actor) && slow)
        {
            var noPlayer = !PlayerAboard();
            foreach (var rider in Aboard())
                if (rider is Adventurer mate && OwnCompanion(mate, actor) || rider is Npc && noPlayer) GetOut(rider);
        }
        RefreshHud();
    }

    /// <summary>A rider thrown from the moving cart lands this far behind its back end (past the body's own radius).</summary>
    const float ThrownBehind = 0.2f;

    /// <summary>The rider leaves its seat (which frees it in the cart's book): beside the lane from a slow cart, onto the line behind a fast one.</summary>
    private void GetOut(ICartRider rider)
    {
        var thrown = State.Mode == CartMode.Rolling && State.V >= RunOverRules.ThrownAtSpeed;
        if (!thrown)
        {
            var cell = ExitCell();
            _exitTaken.Add(cell);
            rider.GetOut(_builder.Surface(cell), Cart.Velocity); // the cart tells it (Unseated)
            return;
        }
        // thrown: down on the line just behind the cart, carried a little by the ride; the cart has had them (once) and rolls on away
        EventBus.Instance.Log($"{(rider is Adventurer hero ? hero.State.Name : ((Node)rider).Name)} jumps from the moving cart!");
        _throwing = true;
        try { rider.GetOut(LanePoint(Party.BackS - Party.Dir * BodyRadius - Party.Dir * ThrownBehind) + Vector3.Up * 0.05f, Cart.Velocity * 0.3f); }
        finally { _throwing = false; }
        if (rider is GodotObject o) _hitAt[o.GetInstanceId()] = _now;
        if (rider is IHazardVictim victim)
            victim.ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(victim.HazardVictimKind, false, TierRules.Lethality), SourceName, HazardIndex);
    }

    /// <summary>Where a rider gets out of a slow or stopped cart: a free walkable cell beside the lane that is no rail (never the enemy lane, never a cell another rider just took).</summary>
    private GridPos ExitCell() => ExitCellNear(Cart.GlobalPosition, true, true);

    /// <summary>A walkable cell beside the lane near the cart, off the rails and the sidings (as a rider steps out at a stop): where a follower left far behind a riding lead is set down.</summary>
    public Vector3 StandBesideCart() => _builder.Surface(ExitCellNear(Cart.GlobalPosition, true, false)) + Vector3.Up * 0.1f;

    /// <summary>
    /// Where a big body a slag cart knocked aside lands: a walkable cell beside the enemy lane near <paramref name="at"/>, off
    /// the rails and the sidings (a pocket, the platform). No such cell near that stretch: beside the party lane instead.
    /// </summary>
    public Vector3 SetAsideBesideEnemyLane(Vector3 at)
    {
        var lane = _layout.Rail!.Enemy;
        var cell = CartSeats.ExitCell(lane, NearestOf(lane, DungeonBuilder.WorldToCell(at)), c => CrevasseRules.SetDownOk(_layout, c) && !_layout.IsRail(c) && !_train.Sidings.Contains(c), true);
        if (_layout.IsRail(cell)) cell = ExitCellNear(at, true, false);
        return _builder.Surface(cell);
    }

    /// <summary>A walkable cell beside the PARTY lane near <paramref name="at"/>, off the rails and the sidings: where a hound that missed the cart lands.</summary>
    public Vector3 StandBesideLane(Vector3 at) => _builder.Surface(ExitCellNear(at, true, false)) + Vector3.Up * 0.1f;

    /// <summary>
    /// Something on the cart (a boarding hound) bites the plating for <paramref name="amount"/>: the HUD follows, and plating gone
    /// while the cart rolls wrecks it as a run-over would. Server only.
    /// </summary>
    public void DentPlating(float amount)
    {
        if (NetSession.IsClient || amount <= 0f || State.Mode == CartMode.Wrecked) return;
        Party.State = State with { Plating = Mathf.Max(0f, State.Plating - amount) };
        RefreshHud();
        if (State.Plating <= 0f && State.Mode == CartMode.Rolling) Wreck();
    }

    /// <summary>
    /// The rolling cart smashes through something on the line (a barricade): the plating takes <paramref name="dent"/> (which wrecks
    /// it at 0, as any dent), the cart keeps <paramref name="speedKept"/> of its speed, and the riders and the ground feel it. Server only.
    /// </summary>
    internal void Smash(float dent, float speedKept)
    {
        if (NetSession.IsClient || State.Mode != CartMode.Rolling) return;
        DentPlating(dent);
        if (State.Mode == CartMode.Rolling) Party.State = State with { V = State.V * speedKept };
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.1f, 0.5f);
        Props.Tremor.ShakeOnly(0.04f, 0.4f);
    }

    /// <summary>The index of the lane cell nearest <paramref name="here"/> (squared grid distance, first wins).</summary>
    private static int NearestOf(IReadOnlyList<GridPos> lane, GridPos here)
    {
        int best = 0, bestD = int.MaxValue;
        for (var i = 0; i < lane.Count; i++)
        {
            int dx = lane[i].X - here.X, dy = lane[i].Y - here.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private GridPos ExitCellNear(Vector3 at, bool atStop, bool useTaken)
    {
        bool Free(GridPos c) => CrevasseRules.SetDownOk(_layout, c) && !_layout.IsRail(c) && !_train.Sidings.Contains(c); // never a drop: a plank is walkable, not a place to put someone
        var lane = _layout.Rail!.Party;
        var best = NearestOf(lane, DungeonBuilder.WorldToCell(at));
        if (!useTaken) return CartSeats.ExitCell(lane, best, Free, atStop);
        var cell = CartSeats.ExitCell(lane, best, c => Free(c) && !_exitTaken.Contains(c), atStop);
        // every free cell nearby taken: share the first one rather than land on the track
        if (atStop && lane.Contains(cell) && _exitTaken.Count > 0) cell = CartSeats.ExitCell(lane, best, Free, true);
        return cell;
    }

    // ---- the ride --------------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        _now += delta;
        if (!NetSession.IsClient && Current) Tick((float)delta);
        else if (NetSession.IsClient && Current) SendRideIntent(delta);
        Flush();
    }

    private void Tick(float dt)
    {
        var party = Main.Instance.Party;
        if (State.Mode != CartMode.Rolling) Party.Settle(); // a cart that does not roll is posed where it stands
        _gates.Tick(dt);

        if (State.Mode == CartMode.Held)
        {
            var playerAboard = PlayerAboard();
            if (!playerAboard) SetDownRiders(); // no player aboard: companions do not ride off on their own
            else if ((_hopTimer -= dt) <= 0f) { _hopTimer = HopEvery; HopInNearby(party); }
            CountHeroes(party, out var seated, out var living);
            // a countdown the cage held back starts once the hold ends with players sitting ready (the cage opened, or its last crew died)
            if (_heldStartCall is { } held && playerAboard && seated > 0 && GateClear && !(Halted && LineBlocked) && !Boarding.Running && !CageHeld) StartBoarding(held);
            if (!playerAboard || seated == 0 || !GateClear || Halted && LineBlocked || CageHeld) // the cart does not leave empty, with only the fallen, without a player, into the obstacle it stopped at, or from its stop's locked cage
            {
                if (Boarding.Running)
                {
                    if (CageHeld) _heldStartCall ??= false; // it restarts by itself once the hold ends
                    Boarding.Cancel();
                }
            }
            else if (Boarding.Tick(dt, seated, living, hold: CrewBoardingHolds?.Invoke(StopIndex) == true) && StopIndex < StopS.Length - 1)
            {
                Party.Limit = StopS[StopIndex + 1]; // the next stop is ahead of S before the cart rolls
                Party.State = State with { Mode = CartMode.Rolling };
                SetCartLayer();
                Party.Settle(); // it sets off from a standstill: nothing banked from the wait
                Raise(new CartEvent(Halted ? CartEventKind.Resumed : CartEventKind.Departed, Stop: StopIndex)); // a halt left is not a departure: the ride stays armed
                _heldStartCall = null;
                Halted = false;
                RefreshHud();
            }
        }

        if (State.Mode == CartMode.Rolling)
        {
            var input = Inputs(party, new CartInput(Party.SlopeAhead, false, 0, _train.Count, Party.Limit));
            Party.Step(input, dt);
            if (DebugStall) Party.State = State with { S = Party.PrevS, V = 0f }; // a test's stuck cart: it rolls and goes nowhere
            var crashed = HitBlock(); // an obstacle on the party lane: the cart is put back at its face
            var droppedIn = JudgeJump(); // a gap's lip: the cart flies, or drops in with its plating gone (wrecked, or over a crevasse fallen, below)
            RunOver(party); // the arrival tick too: the last stretch before the stop is swept
            if (droppedIn && _gapOverCrevasse) FallNow("The cart drops into the crevasse!"); // the train goes over with every rider and wagon
            else if (State.Plating <= 0f) Wreck(); // before Arrive: a cart wrecked as it rolls in keeps the stop it left, and its riders take the wreck as anywhere
            else if (crashed) Halt();
            else if (State.Mode == CartMode.Held) { SetCartLayer(); Arrive(); }
            else { Warn(party); Watch(dt); }
        }
        else { _partyWatch.Reset(); _anyBrake = false; } // a brake held into a stop is a fresh press on the next ride's first rolling tick (BridgeRules.NewPress)

        Party.Sync();
        _train.Sync();
        TickChase(party, dt); // after the train has moved: the chaser's limit is where its tail stands now
        TickRelief(party, dt); // solo: a player left behind gets a relief cart (plan 7)
        TickFallen(party); // after the arrival: the fallen come back at the stop the cart rolled into, or the platform a hero stepped onto
    }

    // ---- run-over --------------------------------------------------------------------------------

    /// <summary>Judge the bodies near the cart's front (<see cref="RunOverSweep"/>): hit ones take the run-over outcome (<see cref="RunOverEffects"/>), big enemies are also knocked aside and dent the plating, and the riders feel it.</summary>
    private void RunOver(PartyController party)
    {
        _bodies.Clear();
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && !hero.Parked && !hero.Downed) _bodies.Add(hero); // riders are skipped by the sweep
        if (Party.Moving)
        {
            var parent = GetParent();
            for (var i = 0; i < parent.GetChildCount(); i++)
            {
                var child = parent.GetChild(i);
                if (child is Enemy { IsAlive: true, Carried: false, Leaping: false } enemy) _bodies.Add(enemy); // a slag cart's riders ride their own cart
                else if (child is Npc { Dead: false, Downed: false, Seated: false } npc) _bodies.Add(npc);
            }
        }
        var run = JudgedRun(); // the lane the front is on
        RunOverSweep.Judge(run, _bodies, _now, _hitAt, _hits);
        if (_hits.Count == 0) return;

        var damage = RunOverEffects.Apply(_hits, SourceName, TierRules.Lethality, SetAsideFor(run), _onHit);
        if (damage <= 0f) return;
        Party.State = State with { Plating = Mathf.Max(0f, State.Plating - damage) };
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.06f, 0.4f);
        RefreshHud();
    }

    /// <summary>
    /// "Cart coming!" for a hero on foot with the cart bearing down (<see cref="RailWarn.Ahead"/>): a seated hero calls it in their own words
    /// (<see cref="RailCalls"/>, else the log), once per hero per few seconds. The bell is the cart's own (<see cref="CartSound"/>).
    /// </summary>
    private void Warn(PartyController party)
    {
        var run = JudgedRun(); // the lane the front is on
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Seated || hero.Downed) continue;
            if (!RailWarn.Ahead(run.Track, run.State.S, State.V, hero.GlobalPosition)) continue;
            if (_warnedAt.TryGetValue(hero.Index, out var t) && _now - t < WarnEvery) continue;
            _warnedAt[hero.Index] = _now;
            RailCalls.Say(party, Cart, BarkTrigger.CartComing, "Cart coming!");
        }
    }

    /// <summary>
    /// The cart is solid (layer 1) while it stands, so nobody walks through it, and only a thing to use (layer 8) while it rolls: a
    /// rolling cart runs a body over (<see cref="RunOver"/>) instead of shoving it down the line.
    /// </summary>
    private void SetCartLayer() => Cart.CollisionLayer = State.Mode == CartMode.Rolling ? 8u : 1u | 8u;

    /// <summary>
    /// Where the cart was last wrecked (its body's place before a spare took over), or null before any wreck. A wreck judged
    /// inside another node's tick is told at this controller's next flush, after the spare has moved the cart: whoever clears
    /// the ride then (the boarders) reads the wreck's place here.
    /// </summary>
    public Vector3? WreckedAt { get; private set; }

    /// <summary>
    /// Plating gone: the cart stops for good, riders climb out beside the lane and are hurt. With <paramref name="fall"/> (<see cref="Fall"/>)
    /// the train goes into the crevasse instead: no tumble but the plunge, every rider and every coupled wagon's riders and the hounds
    /// aboard taken by the crevasse (<see cref="CrevasseWatch.Take"/>), the wagons hidden and lost off an uncoupled train, and a spare takes
    /// over only while a hero still stands (<see cref="CrevasseRules.SpareAfterFall"/>): a fall that takes everyone spends none.
    /// </summary>
    private void Wreck(bool fall = false, bool seen = false)
    {
        EndChase(atOnce: true); // a chase ends with the train's wreck: its riders step out beside the chaser
        Wrecks++;
        WreckedAt = Cart.GlobalPosition;
        LastWreckFell = fall;
        var lost = fall ? new List<TrainWagon>(_train.Wagons) : null;
        Party.State = State with { Mode = CartMode.Wrecked, V = 0f, Plating = 0f };
        SetCartLayer();
        ResetLine(); // a wreck ends the ride: the cart (or its spare) stands on its own lane
        if (lost != null && seen) SeenFall(lost, cart: true, localRode: Main.Instance?.Party?.Local is { } me && IsInstanceValid(me) && me.Cart == Cart); // the bridge's take: the train drops through the gone cells, its riders with it
        else if (lost != null) Plunge(lost, cart: true); // before the speed is zeroed: the train goes over the way it was going
        else Tumble(); // before the speed is zeroed: the tub is thrown the way the cart was going
        RideAudio.At("cart_wreck", Cart.GlobalPosition); // once, here (at the lip, for a fall): the cart's sound sees no wreck (a spare takes over in this call)
        Cart.Speed = 0f; // the riders climb out of a stopped cart, not one still carrying them at speed
        if (lost == null) EventBus.Instance.Log("The cart is wrecked!"); // a fall has said why
        Raise(new CartEvent(CartEventKind.Wrecked, Stop: StopIndex));
        _exitTaken.Clear();
        if (lost != null) TakeTheTrain(lost);
        else
            foreach (var rider in Aboard())
            {
                GetOut(rider); // V is 0: no thrown run-over
                if (rider is IHazardVictim victim && !(rider is Adventurer { Downed: true }) && !(rider is Npc { Downed: true }))
                    victim.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, WreckHpFraction, 0f), SourceName, HazardIndex);
            }
        Boarding.Cancel();
        if (lost == null || CrevasseRules.SpareAfterFall(StandingHeroes())) SpareTakesOver();
        else
        {
            HideWreck(); // nobody stands: no spare is spent before the wipe
            EventBus.Instance.Log("The train is gone into the dark.");
        }
        RefreshHud();
    }

    private const float TumbleSeconds = 6f;

    /// <summary>
    /// The tub's look on a rigid body thrown along the cart's way and up, with a little spin, gone after <see cref="TumbleSeconds"/>.
    /// Cosmetic: layer 0, the floor only, and silent (the wreck's cue plays beside it, once per wreck on each peer).
    /// </summary>
    private void Tumble()
    {
        if (!IsInsideTree() || GetParent() is not Node3D root || Cart.GetNodeOrNull<Node3D>("Body") is not { } body) return;
        var tub = new RigidBody3D { Name = "CartTumble", CollisionLayer = 0, CollisionMask = 1, Mass = 300f };
        tub.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(MineCart.TubWidth, MineCart.TubHeight, MineCart.TubLength) }, Position = new Vector3(0, MineCart.TubBottom + MineCart.TubHeight / 2f, 0) });
        tub.AddChild(body.Duplicate());
        root.AddChild(tub);
        tub.GlobalTransform = Cart.GlobalTransform;
        var basis = Cart.GlobalTransform.Basis;
        tub.LinearVelocity = Cart.Velocity + Vector3.Up * 4f;
        tub.AngularVelocity = -basis.Z * 3f + basis.X * 1.5f + Vector3.Up * 1f;
        GetTree().CreateTimer(TumbleSeconds).Timeout += () => { if (IsInstanceValid(tub)) tub.QueueFree(); };
    }

    /// <summary>No spare took over: the real cart's body is hidden and not solid, and kept for a Restore.</summary>
    private void HideWreck()
    {
        Cart.Visible = false;
        Cart.CollisionLayer = 0;
    }

    /// <summary>
    /// The nearest intermediate stop (by distance along the line, behind or at most the next stop ahead) with an unused spare provides a new cart:
    /// the spare disappears and the party's cart (same node and run) stands Held there with full plating, wagons still coupled
    /// behind it (after a fall that took them, fresh empty ones: <see cref="CoupleFresh"/>). None left: the rest is on foot (an OnFoot event).
    /// </summary>
    private void SpareTakesOver()
    {
        var pick = _spares.Pick(StopS, StopIndex, State.S); // the stop last left, earlier ones, or the very next: never past a gate not yet crossed
        if (pick < 0)
        {
            EventBus.Instance.Log("No carts left. The rest is on foot.");
            Raise(new CartEvent(CartEventKind.OnFoot, Stop: StopIndex));
            HideWreck(); // the tumble was the wreck: nothing is left standing on the line (the node stays for a Restore)
            return;
        }
        _spares.Use(pick);
        StopIndex = pick;
        Party.Reset(new CartState(StopS[pick], 0f, CartMode.Held, CartRules.MaxPlating));
        SetCartLayer();
        Party.Limit = StopS[pick];
        Cart.Speed = 0f;
        _hitAt.Clear();
        _warnedAt.Clear();
        Party.Sync();
        Party.PoseAt(State.S);
        _train.Pose(State.S);
        if (Train.Count < Wagons.Count) CoupleFresh("a spare takes over"); // a fall took the wagons: the spare brings fresh empty ones
        EventBus.Instance.Log($"A spare cart waits at stop {pick}.");
        Raise(new CartEvent(CartEventKind.Arrived, Stop: pick));
        if (!GateUp(pick)) EventBus.Instance.Log("The gate is down. Clear the stop.");
    }

    /// <summary>When a seated player in the party's cart last braked (<c>NetSession.Instance.Now</c>; server and solo), or NaN: ride 4's bridge reads it (<see cref="BridgeRules.Braked"/>).</summary>
    public double LastBrakeAt { get; private set; } = double.NaN;
    /// <summary>When the brake held last (or now) was pressed (<c>NetSession.Instance.Now</c>; <see cref="BridgeRules.NewPress"/>), or NaN: the bridge counts only a press made after its warning (<see cref="BridgeRules.Counts"/>).</summary>
    public double BrakePressedAt { get; private set; } = double.NaN;
    /// <summary>Players in the party's cart pumping at the last input (no brake held): a leaper on a slag cart's deck is never one (tests read it).</summary>
    internal int PlayersPumping => _playersPumping;
    /// <summary>A seated player holds the brake of the rolling cart now (server and solo).</summary>
    internal bool Braking => _anyBrake && State.Mode == CartMode.Rolling;

    /// <summary>
    /// Each seated player brakes (back) or pumps (forward): the one played here from its stick, another player's (on the server) from
    /// its last <see cref="RideIntent"/>. Seated companions pump on a climb, when slow or before a jump. <see cref="RideInputRules.Inputs"/>
    /// decides: any player's brake cancels every pump, players pump first, companions fill up to two. Every pumping rider, player or
    /// companion, is marked <see cref="Adventurer.Pumping"/> (the snapshot shows it). No allocation.
    /// </summary>
    private CartInput Inputs(PartyController party, CartInput input)
    {
        var playersPumping = 0;
        var anyBrake = false;
        var companions = 0;
        var server = NetSession.IsServer ? NetSession.Instance.Server : null;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Cart != Cart || hero.Downed) continue;
            if (hero.Control == ControlKind.Companion) { companions++; continue; }
            var (pump, brake) = PlayerRide(hero, party, server);
            if (pump) playersPumping++;
            anyBrake |= brake;
        }
        var companionsWant = input.Slope > CartRules.ClimbSlope || State.V < 0.7f * CartRules.Cruise || GapAhead(PumpForJump); // a jump ahead wants every arm on the lever
        var most = DebugTools.Instance?.PumperCap ?? RideInputRules.MaxPumpers; // a test's cap (--test-chase's one-pumper jump); the game's is two
        var (pumpers, brakeOn) = RideInputRules.Inputs(playersPumping, anyBrake, companionsWant, companions, most);
        if (anyBrake && NetSession.Instance is { } session) // ride 4's bridge goes under a train braked within BridgeRules.BrakeMemory by a press made after its warning
        {
            var now = session.Now;
            if (BridgeRules.NewPress(_anyBrake)) BrakePressedAt = now;
            LastBrakeAt = now;
        }
        _anyBrake = anyBrake; // the watchdog: a rider holding the brake stops the cart on purpose
        _playersPumping = brakeOn ? 0 : playersPumping; // the cart sender's dirty test: a player's pump or brake goes out at once
        var companionPumps = pumpers - (brakeOn ? 0 : playersPumping);
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Cart != Cart) continue;
            var pumping = false;
            if (!hero.Downed && !brakeOn)
            {
                if (hero.Control == ControlKind.Companion) { if (companionPumps > 0) { companionPumps--; pumping = true; } }
                else pumping = PlayerRide(hero, party, server).Pump;
            }
            hero.Pumping = pumping;
        }
        return input with { Brake = brakeOn, Pumpers = pumpers };
    }

    /// <summary>A seated player's pump and brake: the stick of the one played here, the last ride intent of another player's (server), else none.</summary>
    private static (bool Pump, bool Brake) PlayerRide(Adventurer hero, PartyController party, ServerWorld? server)
    {
        if (hero.Control == ControlKind.LocalHuman) return hero == party.Local ? RideInputRules.FromMove(hero.Intent.Move.Y) : (false, false); // forward is −Y (move_forward is the vector's negative Y)
        if (hero.Control == ControlKind.Remote && server != null) return server.RideInputOf(hero.Index);
        return (false, false);
    }

    /// <summary>
    /// The cart rolls into the next stop: it is mended there to full plating (as a load would give it), so each ride has its
    /// own budget and one ride's mistake does not wreck the next. A halt on the line is not an arrival and mends nothing.
    /// </summary>
    private void Arrive()
    {
        StopIndex++;
        _arrivedStop = StopIndex; // the fallen come back here (TickFallen); a spare's takeover never sets it
        Party.State = State with { Plating = CartRules.MaxPlating };
        ResetLine();
        foreach (var rider in Cart.Riders.Values) if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Pumping = false;
        var last = StopIndex >= StopS.Length - 1;
        EventBus.Instance.Log(last ? "The cart reaches the end of the line." : $"The cart rolls into stop {StopIndex}.");
        Raise(new CartEvent(CartEventKind.Arrived, Stop: StopIndex));
        Cart.Speed = 0f;
        if (!PlayerAboard()) SetDownRiders(); // the player was thrown or got out on the way: the companions step out here and the cart stays
        else if (!last && State.Mode == CartMode.Held && GateUp(StopIndex)) StartBoarding();
        if (!GateUp(StopIndex)) EventBus.Instance.Log("The gate is down. Clear the stop.");
        RefreshHud();
    }

    /// <summary>Living heroes in the world (as <see cref="PartyController.Active"/>), and how many of them sit in this cart. No allocation.</summary>
    private void CountHeroes(PartyController party, out int seated, out int living)
    {
        seated = living = 0;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Downed) continue;
            living++;
            if (hero.Cart == Cart) seated++;
        }
    }

    // ---- posing ----------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Current) return;
        if (NetSession.IsClient) { ClientPose((float)delta); return; }
        var s = Party.RenderS((float)Engine.GetPhysicsInterpolationFraction());
        Party.PoseAt(s);
        _train.Pose(s);
        foreach (var rider in Cart.Riders.Values)
            if (rider is not GodotObject o || IsInstanceValid(o)) rider.FollowSeat();
        _train.Follow();
        FeedPartySound(s);
        PoseChaser();
        _hudTimer -= (float)delta;
        if (_hudTimer <= 0f) RefreshHud();
    }

    // ---- HUD -------------------------------------------------------------------------------------

    /// <summary>
    /// The cart's bar and line (<see cref="RailHud"/>): the party cart's while the local hero rides it or stands within <see cref="HudRange"/>,
    /// nothing over a hidden cart (a wreck no spare replaced), and the chaser's line, with no plating bar, while the hero rides the chaser
    /// however far behind the train.
    /// </summary>
    private void RefreshHud()
    {
        _hudTimer = HudEvery;
        var me = Main.Instance?.Party?.Local;
        if (me != null && !IsInstanceValid(me)) me = null;
        var chaserMode = CartMode.Held;
        var chaserLeft = -1f;
        var relief = false;
        var inChaser = me != null && ChaserHud(me, out chaserMode, out chaserLeft, out relief);
        var show = inChaser || me != null && (me.Cart == Cart || me.GlobalPosition.DistanceTo(Cart.GlobalPosition) <= HudRange);
        HudText = show ? RailHud.Text(HudState(inChaser, chaserMode, chaserLeft, relief)) : null;
        Main.Instance?.Hud?.SetCart(HudText != null && !inChaser ? State.Plating / CartRules.MaxPlating : null, HudText);
    }

    /// <summary>The HUD's view of the cart: from the cart's own state on the server or solo, from the server's flags and countdown on a client.</summary>
    private RailHudState HudState(bool inChaser, CartMode chaserMode, float chaserLeft, bool relief)
    {
        var left = NetSession.IsClient ? NetBoardingLeft() : Boarding.Running ? Mathf.Max(0f, Boarding.Left) : -1f;
        return new RailHudState(State.Mode, Hidden: !Cart.Visible, Halted, LineBlocked, GateClear, Boarding: left >= 0f, BoardingLeft: left,
            Plating01: State.Plating / CartRules.MaxPlating, StopIndex, StopS.Length, inChaser, chaserMode, chaserLeft, CageHeld: CageHeld, ChaserRelief: relief);
    }

    /// <summary>
    /// <paramref name="me"/> sits in the live chase's chaser (a client's copy on a client): its mode and countdown (−1 none) as this peer shows them,
    /// and whether it is a relief cart (single player only: a client's chaser never is).
    /// </summary>
    private bool ChaserHud(Adventurer me, out CartMode mode, out float left, out bool relief)
    {
        mode = CartMode.Held;
        left = -1f;
        relief = false;
        if (!ChaseLive || me.Cart is not MineCart seat) return false;
        if (NetSession.IsClient)
        {
            if (RailNet.Instance is not { } net || !IsInstanceValid(net) || net.ClientChaserHud(seat) is not { } hud) return false;
            (mode, left) = hud;
            return true;
        }
        if (_chase is not { Ended: false } c || !IsInstanceValid(c.Cart) || seat != c.Cart) return false;
        mode = c.Run.State.Mode;
        left = ChaserBoardingLeft;
        relief = c.Relief;
        return true;
    }
}
