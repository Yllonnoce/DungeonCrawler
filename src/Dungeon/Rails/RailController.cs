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
/// only shows the cart parked at the first stop. Every change of the cart's state is told through <see cref="CartEvents"/>.
/// </summary>
public partial class RailController : Node
{
    public static RailController? Instance { get; private set; }

    /// <summary>A hero mangled by the cart carries this as MangledBy: no hazard has it, so it always counts as armed.</summary>
    public const int HazardIndex = 1000;
    /// <summary>Companions this close to the human who boards hop in with them.</summary>
    public const float CompanionBoardRange = 25f;
    /// <summary>The cart's HUD line shows while a hero is seated or this close.</summary>
    public const float HudRange = 12f;
    const float HudEvery = 0.25f;
    const string SourceName = "the warden cart";

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

    /// <summary>Back from a save: the listed gates stand up at once (no animation, no log). Gates not listed stay as they are.</summary>
    public void RestoreGates(IEnumerable<int> up)
    {
        _gates.Restore(up);
        RefreshHud();
    }

    /// <summary>A gate went up (the check or the winch): riders waiting at its stop start the boarding.</summary>
    private void OnGateLifted(RailGate g)
    {
        if (g.Stop == StopIndex && State.Mode == CartMode.Held && !Boarding.Running && PlayerAboard()) StartBoarding();
        RefreshHud();
    }

    // ---- the train (CartTrain) -------------------------------------------------------------------

    private CartTrain _train = null!;
    /// <summary>Cage wagons coupled behind the cart, nearest first: the RailController poses them on the line behind it.</summary>
    public IReadOnlyList<CageWagon> Train => _train.Wagons;
    /// <summary>From one body's middle to the next along the train (<see cref="CartTrain.WagonSpacing"/>).</summary>
    public const float WagonSpacing = CartTrain.WagonSpacing;

    /// <summary>A cage wagon's siding cell: riders getting out (and enemies knocked aside) are never set down on it.</summary>
    public void ReserveSiding(GridPos cell) => _train.ReserveSiding(cell);

    /// <summary>Couple <paramref name="wagon"/> on behind the last body of the train (server, at a departure or a load). It is posed behind the cart at once.</summary>
    public void Couple(CageWagon wagon)
    {
        if (NetSession.IsClient) return; // the train is the server's
        _train.Couple(wagon);
    }

    // ---- spare carts (SpareYard) -----------------------------------------------------------------

    private SpareYard _spares = null!;
    /// <summary>Intermediate stops (indices) whose spare has been used, for the save.</summary>
    public IReadOnlyCollection<int> SparesUsed => _spares.Used;
    /// <summary>Spares still parked.</summary>
    public int SparesLeft => _spares.Left;

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
    const float WarnEvery = 4f;
    const float WarnSeconds = 3f;
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
        Cart = new MineCart { PromptFor = PromptFor, OnUse = Use };
        var start = StopS.Length > 0 ? StopS[0] : 0f;
        Party = new CartRun(new TrackSampler(track), Cart, new CartState(start, 0f, CartMode.Held, CartRules.MaxPlating)) { Limit = start };
        Cart.RiderChanged += OnRiderChanged;
        root.AddChild(Cart);
        _train = new CartTrain(Party);
        _gates = new StopGates(builder, layout, OnGateLifted, RefreshHud);
        _gates.Build(root, StopS, Party.Track);
        _spares = new SpareYard(builder, layout, track, _train.Sidings);
        _spares.Build(root, StopS.Length);
    }

    public override void _EnterTree() => Instance = this;

    public override void _Ready() => Party.PoseAt(State.S);

    public override void _ExitTree()
    {
        if (Instance != this) return;
        Instance = null;
        Main.Instance?.Hud?.SetCart(null, null);
    }

    /// <summary>The floor this controller belongs to is still the one built (a cleared floor waits a frame to be freed).</summary>
    private bool Current => Main.Instance is { InGame: true } main && main.Builder.PartyTrack == _track && IsInstanceValid(Cart);

    private void Raise(CartEvent e) => _pending.Add(e);

    /// <summary>Tell the queued events, in order (one a listener raises meanwhile is told too).</summary>
    private void Flush()
    {
        if (_pending.Count == 0) return;
        try { for (var i = 0; i < _pending.Count; i++) CartEvents?.Invoke(_pending[i]); }
        finally { _pending.Clear(); }
    }

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
                if (IsInstanceValid(hero) && hero.Cart == Cart) (riders ??= new()).Add(hero.Index);
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
        _spares.Restore(sparesUsed);
        _restoredRiders.Clear();
        if (riders != null) foreach (var i in riders) _restoredRiders.Add(i);
        if (StopS.Length == 0) return;
        StopIndex = Math.Clamp(stopIndex, 0, StopS.Length - 1);
        Party.Reset(new CartState(StopS[StopIndex], 0f, CartMode.Held, CartRules.MaxPlating));
        Party.Limit = StopS[StopIndex];
        Boarding.Cancel();
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
        Raise(new CartEvent(CartEventKind.Restored, Stop: StopIndex));
        ClearRiders();
        Flush();
        Callable.From(() => { ClearRiders(); _restoredRiders.Clear(); Flush(); }).CallDeferred(); // the party is set down after the floor state is applied: look again once it stands
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

    /// <summary>The countdown starts at the stop the cart is held at (a Boarding event says where).</summary>
    private void StartBoarding()
    {
        Boarding.Start();
        Raise(new CartEvent(CartEventKind.Boarding, Stop: StopIndex));
    }

    private string PromptFor(Adventurer hero)
    {
        if (NetSession.IsClient || hero.Control == ControlKind.Remote) return ""; // co-op riding is plan 5
        if (hero.Cart == Cart) return "Get out";
        if (StopIndex >= StopS.Length - 1) return ""; // the end of the line: nowhere left to ride
        return State.Mode == CartMode.Held && !hero.Seated ? "Board the cart" : "";
    }

    private void Use(Adventurer actor)
    {
        UseCart(actor);
        Flush();
    }

    private void UseCart(Adventurer actor)
    {
        if (NetSession.IsClient || actor.Control == ControlKind.Remote || !Current) return;
        if (actor.Cart == Cart) { GetOutWithParty(actor); return; }
        if (actor.Seated || actor.Downed || State.Mode != CartMode.Held) return;
        if (StopIndex >= StopS.Length - 1) return; // the end of the line: nowhere left to ride (the prompt offers nothing either)
        if (actor.Control != ControlKind.LocalHuman && !PlayerAboard()) return; // a companion rides only with a player
        if (!SeatHero(actor)) return;
        if (actor.Control == ControlKind.LocalHuman)
            foreach (var mate in Main.Instance.Party.Active)
                if (mate != actor && mate.Control == ControlKind.Companion && !mate.Downed && !mate.Seated
                    && mate.GlobalPosition.DistanceTo(actor.GlobalPosition) <= CompanionBoardRange)
                    SeatHero(mate); // they hop in; walking to the cart is polish for later
        if (actor.Control == ControlKind.LocalHuman) SeatBench(actor.GlobalPosition);
        if (!Boarding.Running && StopIndex < StopS.Length - 1 && GateUp(StopIndex)) StartBoarding();
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
        if (!Boarding.Running && GateUp(StopIndex)) StartBoarding();
        RefreshHud();
        return true;
    }

    /// <summary>A player's hero sits in this cart, awake: the cart only leaves (and companions only board) with one aboard. Remote players count from plan 5.</summary>
    private bool PlayerAboard()
    {
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero) && !hero.Downed && hero.Control == ControlKind.LocalHuman) return true;
        return false;
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
        if (StopIndex >= StopS.Length - 1 || !PlayerAboard() || GetParent() is not { } floor) return;
        _bench.Clear();
        for (var i = 0; i < floor.GetChildCount(); i++)
            if (floor.GetChild(i) is Npc { Dead: false, Downed: false, Escorting: true, Seated: false, Crew: false } npc
                && npc.GlobalPosition.DistanceTo(from) <= CompanionBoardRange) _bench.Add(npc);
        if (_bench.Count == 0) return;
        _benchFrom = from;
        _bench.Sort(_benchOrder);
        foreach (var npc in _bench)
        {
            if (Cart.Seats.Take(npc.RiderId, isHero: false) is not int seat) break; // the bench is full
            npc.SitIn(Cart, seat);
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

    /// <summary>The actor gets out; when the human steps out of a slow cart, the seated companions follow (a hero thrown out at speed leaves them riding).</summary>
    private void GetOutWithParty(Adventurer actor)
    {
        _exitTaken.Clear();
        var slow = State.V < RunOverRules.ThrownAtSpeed;
        GetOut(actor);
        if (actor.Control == ControlKind.LocalHuman && slow)
            foreach (var rider in Aboard())
                if (rider is Adventurer { Control: ControlKind.Companion } or Npc) GetOut(rider);
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
        try { rider.GetOut(Party.Track.Point(Party.BackS - Party.Dir * BodyRadius - Party.Dir * ThrownBehind) + Vector3.Up * 0.05f, Cart.Velocity * 0.3f); }
        finally { _throwing = false; }
        if (rider is GodotObject o) _hitAt[o.GetInstanceId()] = _now;
        if (rider is IHazardVictim victim)
            victim.ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(victim.HazardVictimKind, false, TierRules.Lethality), SourceName, HazardIndex);
    }

    /// <summary>Where a rider gets out of a slow or stopped cart: a free walkable cell beside the lane that is no rail (never the enemy lane, never a cell another rider just took).</summary>
    private GridPos ExitCell() => ExitCellNear(Cart.GlobalPosition, true, true);

    /// <summary>A walkable cell beside the lane near the cart, off the rails and the sidings (as a rider steps out at a stop): where a follower left far behind a riding lead is set down.</summary>
    public Vector3 StandBesideCart() => _builder.Surface(ExitCellNear(Cart.GlobalPosition, true, false)) + Vector3.Up * 0.1f;

    private GridPos ExitCellNear(Vector3 at, bool atStop, bool useTaken)
    {
        bool Free(GridPos c) => _layout.IsWalkable(c) && !_layout.IsRail(c) && !_train.Sidings.Contains(c);
        var lane = _layout.Rail!.Party;
        var here = DungeonBuilder.WorldToCell(at);
        int best = 0, bestD = int.MaxValue;
        for (var i = 0; i < lane.Count; i++)
        {
            int dx = lane[i].X - here.X, dy = lane[i].Y - here.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
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
            if (!playerAboard || seated == 0 || !GateUp(StopIndex)) { if (Boarding.Running) Boarding.Cancel(); } // the cart does not leave empty, with only the fallen, or without a player
            else if (Boarding.Tick(dt, seated, living) && StopIndex < StopS.Length - 1)
            {
                Party.Limit = StopS[StopIndex + 1]; // the next stop is ahead of S before the cart rolls
                Party.State = State with { Mode = CartMode.Rolling };
                Party.Settle(); // it sets off from a standstill: nothing banked from the wait
                Raise(new CartEvent(CartEventKind.Departed, Stop: StopIndex));
                RefreshHud();
            }
        }

        if (State.Mode == CartMode.Rolling)
        {
            var input = Inputs(party, new CartInput(Party.SlopeAhead, false, 0, _train.Count, Party.Limit));
            Party.Step(input, dt);
            RunOver(party); // the arrival tick too: the last stretch before the stop is swept
            if (State.Plating <= 0f) Wreck(); // before Arrive: a cart wrecked as it rolls in keeps the stop it left, and its riders take the wreck as anywhere
            else if (State.Mode == CartMode.Held) Arrive();
            else Warn(party);
        }

        Party.Sync();
        _train.Sync();
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
                if (child is Enemy { IsAlive: true } enemy) _bodies.Add(enemy);
                else if (child is Npc { Dead: false, Downed: false, Seated: false } npc) _bodies.Add(npc);
            }
        }
        RunOverSweep.Judge(Party, _bodies, _now, _hitAt, _hits);
        if (_hits.Count == 0) return;

        var damage = RunOverEffects.Apply(_hits, SourceName, TierRules.Lethality, _setAside, _onHit);
        if (damage <= 0f) return;
        Party.State = State with { Plating = Mathf.Max(0f, State.Plating - damage) };
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.06f, 0.4f);
        RefreshHud();
    }

    /// <summary>"Cart coming!" for a hero on foot with the cart bearing down: a seated companion says it (else the log), once per hero per few seconds.</summary>
    private void Warn(PartyController party)
    {
        var look = Mathf.Max(State.V, RunOverRules.WarnMinSpeed) * WarnSeconds;
        float hint = Party.FrontS + look / 2f, window = look / 2f + 2f;
        // a warned body stands within 2 m of the line at most look + HalfLength ahead, and no stretch of line is shorter than the straight way
        var reach = look + CartRules.HalfLength + 3f;
        var at = Party.Track.Point(State.S);
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Seated || hero.Downed) continue;
            var pos = hero.GlobalPosition;
            if (pos.DistanceSquaredTo(at) > reach * reach) continue;
            if (!Party.Track.Measure(pos, hint, window, out var b)) continue;
            if (!RunOverRules.InWarning(State.S, State.V, b with { Radius = BodyRadius }, WarnSeconds)) continue;
            if (_warnedAt.TryGetValue(hero.Index, out var t) && _now - t < WarnEvery) continue;
            _warnedAt[hero.Index] = _now;
            Adventurer? speaker = null;
            foreach (var mate in party.Adventurers)
                if (IsInstanceValid(mate) && mate.Cart == Cart && mate.Control == ControlKind.Companion && !mate.Downed) { speaker = mate; break; }
            if (speaker != null)
            {
                var name = speaker.State.Name;
                EventBus.Instance.Log($"{name}: \"Cart coming!\"");
                EventBus.Instance.RaiseBark(name, new Adventure.Line("Cart coming!"));
                Main.Instance?.Hud?.ShowBark(name, "Cart coming!", 2f);
            }
            else EventBus.Instance.Log("Cart coming!");
            EventBus.Instance.PlaySfx("cart_bell"); // no clip yet: AudioManager.Play ignores unknown cues; plan 6 adds the sound
        }
    }

    /// <summary>Plating gone: the cart stops for good, riders climb out beside the lane and are hurt.</summary>
    private void Wreck()
    {
        Party.State = State with { Mode = CartMode.Wrecked, V = 0f, Plating = 0f };
        Cart.Speed = 0f; // the riders climb out of a stopped cart, not one still carrying them at speed
        EventBus.Instance.Log("The cart is wrecked!");
        Raise(new CartEvent(CartEventKind.Wrecked, Stop: StopIndex));
        _exitTaken.Clear();
        foreach (var rider in Aboard())
        {
            GetOut(rider); // V is 0: no thrown run-over
            if (rider is IHazardVictim victim && !(rider is Adventurer { Downed: true }) && !(rider is Npc { Downed: true }))
                victim.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, WreckHpFraction, 0f), SourceName, HazardIndex);
        }
        Boarding.Cancel();
        SpareTakesOver();
        RefreshHud();
    }

    /// <summary>
    /// The nearest intermediate stop (by distance along the line, behind or at most the next stop ahead) with an unused spare provides a new cart:
    /// the spare disappears and the party's cart (same node and run) stands Held there with full plating, wagons still coupled
    /// behind it. None left: the rest is on foot (an OnFoot event: the crew in coupled wagons get out and follow).
    /// </summary>
    private void SpareTakesOver()
    {
        var pick = _spares.Pick(StopS, StopIndex, State.S); // the stop last left, earlier ones, or the very next: never past a gate not yet crossed
        if (pick < 0)
        {
            EventBus.Instance.Log("No carts left. The rest is on foot.");
            Raise(new CartEvent(CartEventKind.OnFoot, Stop: StopIndex));
            return;
        }
        _spares.Use(pick);
        StopIndex = pick;
        Party.Reset(new CartState(StopS[pick], 0f, CartMode.Held, CartRules.MaxPlating));
        Party.Limit = StopS[pick];
        Cart.Speed = 0f;
        _hitAt.Clear();
        _warnedAt.Clear();
        Party.Sync();
        Party.PoseAt(State.S);
        _train.Pose(State.S);
        EventBus.Instance.Log($"A spare cart waits at stop {pick}.");
        Raise(new CartEvent(CartEventKind.Arrived, Stop: pick));
        if (!GateUp(pick)) EventBus.Instance.Log("The gate is down. Clear the stop.");
    }

    /// <summary>The seated human brakes (back) or pumps (forward); seated companions pump on a climb or when slow, two pumpers at most.</summary>
    private CartInput Inputs(PartyController party, CartInput input)
    {
        bool brake = false;
        var pumpers = 0;
        if (party.Local is { } me && me.Cart == Cart && !me.Downed && me.Control == ControlKind.LocalHuman)
        {
            var move = me.Intent.Move; // forward is −Y (move_forward is the vector's negative Y)
            brake = move.Y > 0.5f;
            if (move.Y < -0.5f) pumpers = 1;
        }
        var companionsPump = input.Slope > CartRules.ClimbSlope || State.V < 0.7f * CartRules.Cruise;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Cart != Cart) continue;
            var pump = companionsPump && !brake && pumpers < 2 && hero.Control == ControlKind.Companion && !hero.Downed;
            if (pump) pumpers++;
            hero.Pumping = pump;
        }
        return input with { Brake = brake, Pumpers = pumpers };
    }

    private void Arrive()
    {
        StopIndex++;
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
        var s = Party.RenderS(NetSession.IsClient ? 1f : (float)Engine.GetPhysicsInterpolationFraction());
        Party.PoseAt(s);
        _train.Pose(s);
        foreach (var rider in Cart.Riders.Values)
            if (rider is not GodotObject o || IsInstanceValid(o)) rider.FollowSeat();
        _train.Follow();
        if (NetSession.IsClient) return;
        _hudTimer -= (float)delta;
        if (_hudTimer <= 0f) RefreshHud();
    }

    // ---- HUD -------------------------------------------------------------------------------------

    private void RefreshHud()
    {
        _hudTimer = HudEvery;
        var me = Main.Instance?.Party?.Local;
        var show = me != null && (me.Cart == Cart || me.GlobalPosition.DistanceTo(Cart.GlobalPosition) <= HudRange);
        HudText = show ? Line() : null;
        Main.Instance?.Hud?.SetCart(show ? State.Plating / CartRules.MaxPlating : null, HudText);
    }

    private string Line()
    {
        if (State.Mode == CartMode.Wrecked) return "Cart — wrecked";
        if (State.Mode == CartMode.Held && !GateUp(StopIndex)) return "Cart — gate down";
        if (Boarding.Running) return $"Cart — {Mathf.RoundToInt(State.Plating / CartRules.MaxPlating * 100f)}% — departs in {Mathf.CeilToInt(Boarding.Left)}";
        if (State.Mode == CartMode.Rolling) return "Cart — rolling";
        if (StopIndex == 0) return "Cart — at the landing";
        return StopIndex >= StopS.Length - 1 ? "Cart — end of the line" : $"Cart — at stop {StopIndex}";
    }
}
