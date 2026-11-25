using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Combat.Fx;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The hijack's reverse roll (plan 16 Task 5, spec section 3): the party's train, held at a stop, rolls backwards along the line. Its run then
/// travels toward lower S (<see cref="CartRun.Dir"/> −1) while it faces forward (<see cref="CartRun.Reversing"/>: <see cref="CartRun.Facing"/> +1),
/// so the cart is unturned and its wagons stay on the lower-S side, leading; every peer poses it so (the wire's <c>CartStateDto.Hijack</c>).
/// It ramps at 0.5 m/s² to 4 m/s toward its limit (<see cref="HijackRules.LimitS"/>: 40 m back, or 5 m short of a hole or a live hazard of the
/// ride behind), judges run-overs and warns from its leading face (the last wagon's back, <see cref="HijackLead"/>), brakes when told
/// (<see cref="BrakeRollBack"/>, or at its limit with no driver: Ruling 11), then rolls forward to its stop at 2 m/s and is held there again.
/// It raises <see cref="CartEventKind.Hijacked"/> and <see cref="CartEventKind.HijackOver"/>, never a departure or an arrival: no ride
/// arms, no snapshot is taken, no stop is counted, and the watchdog, the jump, the obstacles and the arrival are never judged. Not saved: a load
/// or a wreck stands the train whole and forward (<see cref="ResetHijack"/>). Server and solo; a client follows the wire.
/// Task 6 puts a driver in it: a hijacker the stop trap hands over (<see cref="TakeDriver"/>, only while <see cref="HijackRules.MayStart"/> holds)
/// is carried at the cart's driver spot and glows for <see cref="HijackRules.BrakeGlow"/> (the Glow phase, the train still held), then lets the
/// brake off. The driver has the wagon boarder's grip (<see cref="OnDriverHurt"/>): knocked off, or dead, the brake goes on and the train
/// returns to its stop. The cart may be boarded on foot while it rolls back, brakes or returns at walking speeds (<see cref="HijackRules.MayBoard"/>).
/// Task 7 lets it get away: while it rolls back with its driver the prisoners are put off alive one every 3 s (<see cref="TickThrows"/>); at its
/// limit with the driver still aboard the train is gone (<see cref="GoneAtLimit"/>: the rest set down, the driver and the boarders leap off, the
/// cart hidden and the wagons uncoupled), and 12 s later a relief train rolls in from the limit and is held at the stop (<see cref="SendHijackRelief"/>).
/// </summary>
public partial class RailController
{
    /// <summary>The hijack's phase (a client's from the wire).</summary>
    public HijackPhase Hijack { get; private set; }
    /// <summary>A hijack is under way (any phase but None).</summary>
    public bool HijackLive => Hijack != HijackPhase.None;
    /// <summary>The live roll back's limit (the cart's middle S), or NaN.</summary>
    public float HijackLimitS { get; private set; } = float.NaN;
    /// <summary>
    /// How far the leading face stands past the cart's own front in its way of travel: the train's wagons (<c>wagons × Spacing</c>) while it
    /// rolls back, else 0 (the cart's own front leads).
    /// </summary>
    public float HijackLead => Party.Reversing ? _train.Count * TrainRules.Spacing : 0f;
    /// <summary>The stop the live hijack rolled back from (and returns to), or −1.</summary>
    internal int HijackStop { get; private set; } = -1;
    /// <summary>Why the roll back's brake went on (the return's held line names it).</summary>
    private string _hijackWhy = "";
    private readonly List<(float From, float To)> _rollBackHazards = new();
    /// <summary>Seconds of the driver's glow left before the brake comes off (the Glow phase).</summary>
    private float _glowLeft;
    /// <summary>A driver was handed the cart in this hijack and has not yet been knocked off or died (its loss brakes the roll).</summary>
    private bool _driverHeld;
    /// <summary>The driver's grip taken so far (<see cref="BoarderRules.GripTaken"/> of each counted, striking hit) and the hits that took it.</summary>
    private int _driverGrip, _driverHits;
    /// <summary>Seconds to the glow's next BoarderThrow telegraph (a client's glow lasts 1.5 s: <see cref="HijackRules.GlowRetell"/>).</summary>
    private float _glowTellLeft;
    /// <summary>Hijacks handed a driver on this floor build (each crew bark's key is its own).</summary>
    private int _hijacksTaken;
    /// <summary>Seconds left of a wipe's rise's grace (the restore after a rise at the cart): no hijack starts meanwhile.</summary>
    private float _riseGraceLeft;
    /// <summary>Seconds of the roll back with a driver aboard (the throws are timed by it: <see cref="HijackRules.ThrowDue"/>).</summary>
    private float _rollSeconds;
    /// <summary>Crew put off the rolling train on the way, and set down where it is gone, in this hijack.</summary>
    private int _putOffOnWay, _putOffAtLimit;
    /// <summary>Seconds since the train was gone at its limit (the relief comes at <see cref="HijackRules.ReliefAfter"/>).</summary>
    private float _goneSeconds;
    private readonly List<(int Wagon, bool Seat4, bool Seat5)> _throwView = new();
    private readonly List<Enemy> _leapingOff = new();
    /// <summary>The return's and the relief train's "Cart coming!" throttle (by hero), as a chaser's.</summary>
    private readonly Dictionary<int, double> _reliefWarned = new();

    /// <summary>Test hook: the crew put off the rolling train on the way in the live (or last) hijack.</summary>
    internal int PutOffOnWay => _putOffOnWay;
    /// <summary>Test hook: the crew set down where the train was gone in the live (or last) hijack.</summary>
    internal int PutOffAtLimit => _putOffAtLimit;

    /// <summary>A wipe's rise at the cart was this many seconds ago or less (<see cref="StopSiegeRules.RiseGrace"/>): no hijack starts and no runner runs.</summary>
    internal bool InRiseGrace => _riseGraceLeft > 0f;
    /// <summary>Test hook: the driver's grip taken so far (0 with no driver).</summary>
    internal int DriverGrip => Cart.Driver != null ? _driverGrip : 0;

    /// <summary>The heroes seated anywhere in the train, up or downed (Ruling 8: a downed body in a seat guards it too), parked bodies aside.</summary>
    internal int HeroesSeatedInTrain()
    {
        var n = 0;
        if (Main.Instance?.Party is not { } party) return 0;
        foreach (var a in party.Adventurers)
            if (IsInstanceValid(a) && !a.Parked && CarOf(a) >= 0) n++;
        return n;
    }

    /// <summary>The stop the train is held at has room to roll back (<see cref="HijackRules.RollRoom"/>), as the line stands now.</summary>
    internal bool RollRoomAt(int stop) => stop >= 0 && stop < StopS.Length && HijackRules.RollRoom(StopS[stop], RollBackLimitAt(stop, out _));

    /// <summary>A hijack may start now (<see cref="HijackRules.MayStart"/> on the train as it stands): the climb's end asks it, the roll's start too.</summary>
    internal bool HijackMayStart()
        => HijackRules.MayStart(StopIndex, StopS.Length, State.Mode == CartMode.Held, Halted, StandingPast, HeroesSeatedInTrain(), ChaseLive, InRiseGrace, HijackLive, RollRoomAt(StopIndex));

    /// <summary>
    /// The roll back's limit from <paramref name="stop"/> as the line stands now (<see cref="HijackRules.LimitS"/> over the encounter controller's
    /// <see cref="RailEncounterController.CollectRollBackHazards"/>), and what cut it short of the 40 m (null when nothing did).
    /// </summary>
    internal float RollBackLimitAt(int stop, out string? cut) => RollBackLimitAt(stop, out cut, out _);

    /// <summary><see cref="RollBackLimitAt(int, out string?)"/>, with the span that cut it (null: the 40 m cap): the =limits flow prints it.</summary>
    internal float RollBackLimitAt(int stop, out string? cut, out (float From, float To)? cutBy)
    {
        cut = null;
        cutBy = null;
        if (stop < 0 || stop >= StopS.Length) return float.NaN;
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) && e.GetParent() == GetParent() ? e : null;
        if (enc != null) enc.CollectRollBackHazards(_rollBackHazards);
        else _rollBackHazards.Clear();
        var stopS = StopS[stop];
        var limit = HijackRules.LimitS(stopS, _train.Count, _rollBackHazards);
        if (limit > stopS - HijackRules.RollBackReach)
            foreach (var (from, to) in _rollBackHazards)
                if (from < stopS && Mathf.Abs(HijackRules.LimitS(stopS, _train.Count, new[] { (from, to) }) - limit) < 1e-3f) { cut = $"cut by the hazard at S={from:0.0}..{to:0.0}"; cutBy = (from, to); break; }
        return limit;
    }

    /// <summary>
    /// The brake comes off (server and solo): the train held at its stop starts rolling back toward its limit with <paramref name="driver"/> (null:
    /// none, the debug roll) holding the cart. A Hijacked event is raised; the countdown, if any, is cancelled.
    /// </summary>
    internal void BeginRollBack(Enemy? driver)
    {
        if (NetSession.IsClient || !Current || StopS.Length == 0) return;
        if (Hijack is not (HijackPhase.None or HijackPhase.Glow)) return;
        var k = StopIndex;
        string? cut = null;
        var limit = k >= 0 && k < StopS.Length ? RollBackLimitAt(k, out cut) : float.NaN;
        var room = k >= 0 && k < StopS.Length && HijackRules.RollRoom(StopS[k], limit);
        if (!HijackRules.MayRollBack(k, StopS.Length, State.Mode == CartMode.Held, Halted, StandingPast, room)) // the T5 review's M3: never off a stop's range, a halt, the island or a stop with no room
        {
            GD.Print($"[rails] hijack: no roll back from stop {k} ({State.Mode}, halted {Halted}, standing past {StandingPast}, room {room})");
            return;
        }
        Boarding.Cancel();
        Cart.Driver = driver;
        Hijack = HijackPhase.RollingBack;
        _rollSeconds = 0f;
        _putOffOnWay = _putOffAtLimit = 0;
        HijackStop = k;
        HijackLimitS = limit;
        _hijackWhy = "";
        Party.Dir = -1;
        Party.Reversing = true;
        Party.Limit = limit;
        Party.State = State with { Mode = CartMode.Rolling, V = 0f };
        SetCartLayer();
        Party.Settle(); // it sets off from a standstill
        Party.LastInput = HijackRules.InputFor(Hijack, Party.SlopeAhead, _train.Count, limit) with { StopAt = limit }; // the state sent before the first step is the roll's (the T5 review's M13)
        _hitAt.Clear();
        _warnedAt.Clear();
        _partyWatch.Reset();
        Raise(new CartEvent(CartEventKind.Hijacked, Stop: k));
        GD.Print($"[rails] hijack: the train rolls back from stop {k} (limit S={limit:0.0}, {StopS[k] - limit:0.0} m of roll{(cut != null ? $"; {cut}" : "")})");
        RefreshHud();
    }

    /// <summary>The roll back's brake goes on (<paramref name="why"/>: a knock-off, a death, the limit, the test): it stops, then returns to its stop.</summary>
    internal void BrakeRollBack(string why)
    {
        if (NetSession.IsClient || Hijack != HijackPhase.RollingBack) return;
        Hijack = HijackPhase.Braking;
        _hijackWhy = why;
        GD.Print($"[rails] hijack: the brake goes on at S={State.S:0.0} at {State.V:0.00} m/s ({why})");
        RefreshHud();
    }

    /// <summary>
    /// Each physics tick while a hijack is live, in place of the held and rolling ticks (server and solo): the phase's input steps the run, the
    /// run-over is judged from the leading face and the warning looks the way of travel. Never the obstacles, the jump, the arrival or the watchdog.
    /// </summary>
    private void TickHijack(PartyController party, float dt)
    {
        _partyWatch.Reset(); // no watchdog: the roll grinds nowhere
        _anyBrake = false;
        if (_driverHeld && (Cart.Driver is not { } d || !IsInstanceValid(d) || !d.IsAlive || d.Carrier != Cart)) { DriverLost(Cart.Driver, "the driver is dead"); if (!HijackLive) return; } // a death drops it and brakes (Ruling 11's brake)
        if (Hijack == HijackPhase.Glow) { TickGlow(dt); return; } // held under the glow: the brake comes off at its end
        if (Hijack is HijackPhase.Gone or HijackPhase.ReliefIn) _goneSeconds += dt; // since the train went: the relief's wait and its hold's line
        if (Hijack == HijackPhase.Gone) { TickGone(); return; } // the train is gone: the relief train comes after its wait
        if (State.Mode == CartMode.Wrecked) return;
        if ((Hijack is HijackPhase.Braking or HijackPhase.Returning or HijackPhase.ReliefIn) && State.Mode == CartMode.Held) { NextHijackPhase(); return; } // held at its limit as the brake went on, or at its stop
        Party.Step(HijackRules.InputFor(Hijack, Party.SlopeAhead, _train.Count, Party.Limit), dt);
        var lead = HijackLead;
        RunOver(party, lead);
        if (State.Plating <= 0f) { Wreck(); return; } // a big body run over took the last of it
        if (Hijack == HijackPhase.RollingBack && _driverHeld) TickThrows(dt); // the prisoners put off one every 3 s, the last wagon's rear seat first
        if (State.Mode == CartMode.Held || Hijack == HijackPhase.Braking && State.V <= 0f) { NextHijackPhase(); return; }
        if ((Hijack is HijackPhase.Returning or HijackPhase.ReliefIn) && RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.GetParent() == GetParent())
            enc.WarnPartyLane(Party, Cart, _reliefWarned, party); // forward, back to its stop (the return, the relief train): it warns as a party-lane chaser does, so the crew it put off, walking back on the line, step to a refuge
        else Warn(party, lead);
    }

    // ---- the got-away (Task 7) ---------------------------------------------------------------------

    /// <summary>
    /// While the train rolls back with a driver (server and solo; Ruling 13): at 3 s of the roll and every 3 s after, the next prisoner by
    /// <see cref="HijackRules.NextThrow"/> (the last coupled wagon first, its seat 5 then 4) is put off alive beside its wagon
    /// (<see cref="CrewRescueController.PutOffAlive"/>, never the lethal throw) and follows the party.
    /// </summary>
    private void TickThrows(float dt)
    {
        _rollSeconds += dt;
        if (!HijackRules.ThrowDue(_rollSeconds, _putOffOnWay)) return;
        if (PutOffNext() is { } npc) { _putOffOnWay++; GD.Print($"[rails] hijack: {npc.NpcName} put off {_rollSeconds:0.00} s into the roll at S={State.S:0.0}"); }
    }

    /// <summary>The next prisoner by <see cref="HijackRules.NextThrow"/> put off alive (<see cref="CrewRescueController.PutOffAlive"/>); null with nobody aboard.</summary>
    private Npc? PutOffNext()
    {
        if (Crew is not { } crew) return null;
        _throwView.Clear();
        foreach (var wagon in _train.Wagons)
            if (IsInstanceValid(wagon)) _throwView.Add((wagon.Number, CrewIn(wagon, TrainRules.CrewSeats[0]) != null, CrewIn(wagon, TrainRules.CrewSeats[1]) != null));
        if (HijackRules.NextThrow(_throwView) is not { } next || WagonAt(next.Wagon) is not { } w || CrewIn(w, next.Seat) is not { } npc) return null;
        crew.PutOffAlive(npc, w);
        return npc.Seated ? null : npc; // never met: PutOffAlive always unseats
    }

    /// <summary>The crew member seated in <paramref name="seat"/> of <paramref name="wagon"/>, or null.</summary>
    private static Npc? CrewIn(TrainWagon wagon, int seat)
        => wagon.Seats.OccupantOf(seat) is int id && wagon.Riders.TryGetValue(id, out var rider) && rider is Npc { Crew: true, Dead: false } npc && IsInstanceValid(npc) ? npc : null;

    /// <summary>This floor's crew controller, or null.</summary>
    private CrewRescueController? Crew => ObjectiveController.Instance is CrewRescueController crew && IsInstanceValid(crew) && crew.GetParent() == GetParent() ? crew : null;

    /// <summary>
    /// The roll back reached its limit with a driver aboard (Rulings 13 and 14): it got away. The crew still aboard are set down alive beside their
    /// wagons, a hero seated in the train beside the lane, and the driver and every boarder leap off into the fight (alive, aggro on). The cart is
    /// hidden (not solid) and every wagon uncoupled and hidden: the train rolls on into the dark, and <see cref="TickGone"/> brings a relief train
    /// <see cref="HijackRules.ReliefAfter"/> later. No spare is spent.
    /// </summary>
    private void GoneAtLimit()
    {
        var k = HijackStop;
        while (_putOffAtLimit < _train.Count * TrainRules.CrewSeats.Length && PutOffNext() != null) _putOffAtLimit++; // bounded: each call empties a seat
        _exitTaken.Clear();
        foreach (var rider in Aboard()) GetOut(rider); // held at V 0: beside the lane, no throw
        SetDownWagonHeroes(wreckToll: false);
        var driver = Cart.Driver;
        SetDownDriver(); // beside the lane, aggro on
        if (driver != null && IsInstanceValid(driver) && driver.IsAlive) driver.ChangeState(new Enemies.Ai.ChaseState()); // a fresh chase: a carried one lapses after 9 s unseen (unaware), and nothing in the roll needs it hunting
        var boarders = LeapOffBoarders();
        HideWreck();
        _train.UncoupleAll();
        Cart.Speed = 0f;
        Hijack = HijackPhase.Gone;
        _goneSeconds = 0f;
        EventBus.Instance.Log(StopTrapLines.GoneLog);
        GD.Print($"[rails] hijack: {_putOffOnWay} crew put off on the way, {_putOffAtLimit} set down at the limit; the train is gone at S={State.S:0.0}");
        GD.Print($"[rails] hijack: {(driver != null && IsInstanceValid(driver) ? driver.Name : "no driver")} and {boarders} boarder(s) leap off at the limit (stop {k})");
        RefreshHud();
    }

    /// <summary>
    /// Every enemy carried by the train but its driver (a wagon's boarder, a spared hijacker, a cart rim's) set down beside the lane alive, in a fresh
    /// chase (a carried chase may have lapsed: its 9 s without sight). The encounter controller lets each leap go at its next tick. Returns how many.
    /// </summary>
    private int LeapOffBoarders()
    {
        _leapingOff.Clear();
        foreach (var wagon in _train.Wagons)
            if (IsInstanceValid(wagon))
                foreach (var b in wagon.Boarders) if (b != null && IsInstanceValid(b) && b.IsAlive && b.Carrier == wagon) _leapingOff.Add(b);
        foreach (var b in Cart.Boarders) if (b != null && IsInstanceValid(b) && b.IsAlive && b.Carrier == Cart && b != Cart.Driver) _leapingOff.Add(b);
        foreach (var b in _leapingOff)
        {
            b.Drop(StandBesideLane(b.GlobalPosition));
            b.BoardTarget = null;
            b.DeathDrop = null;
            b.ChangeState(new Enemies.Ai.ChaseState()); // a fresh chase, as the driver's
        }
        var n = _leapingOff.Count;
        _leapingOff.Clear();
        return n;
    }

    /// <summary>The train is gone (server and solo): after <see cref="HijackRules.ReliefAfter"/> the relief train is shown at the limit and rolls in.</summary>
    private void TickGone()
    {
        if (HijackRules.ReliefDue(_goneSeconds)) SendHijackRelief();
    }

    /// <summary>
    /// The relief train after a got-away (Ruling 14): the cart shown again at the limit with full plating, every wagon coupled fresh and empty
    /// (<see cref="CoupleFresh"/>), rolling forward (the ReliefIn phase, 4 m/s, judged as any forward run and warned as a party-lane chaser is: the
    /// crew it comes for, walking back on the line, step to a refuge) to be held at its stop:
    /// <see cref="EndHijack"/> raises HijackOver there. It is not an arrival, and no spare is used.
    /// </summary>
    private void SendHijackRelief()
    {
        var k = HijackStop;
        var at = State.S;
        Cart.Visible = true;
        Party.Dir = 1;
        Party.Reversing = false;
        Cart.Reversing = false;
        Party.Limit = StopS[k];
        Party.Reset(new CartState(at, 0f, CartMode.Rolling, CartRules.MaxPlating));
        Hijack = HijackPhase.ReliefIn;
        SetCartLayer();
        _hitAt.Clear();
        _warnedAt.Clear();
        _reliefWarned.Clear();
        Party.Sync();
        Party.PoseAt(at);
        CoupleFresh("a relief train after the hijack");
        Party.LastInput = HijackRules.InputFor(Hijack, Party.SlopeAhead, _train.Count, Party.Limit) with { StopAt = Party.Limit };
        GD.Print($"[rails] hijack: a relief train rolls in from S={at:0.0} to stop {k} ({StopS[k] - at:0.0} m), {_goneSeconds:0.0} s after the train went");
        RefreshHud();
    }

    /// <summary>The run came to a stand: at its limit (it brakes there), stopped by the brake (it returns), or held at its stop again (over).</summary>
    private void NextHijackPhase()
    {
        switch (Hijack)
        {
            case HijackPhase.RollingBack: OnLimit(); break;
            case HijackPhase.Braking: StartReturn(); break;
            case HijackPhase.Returning or HijackPhase.ReliefIn: EndHijack(); break;
        }
    }

    /// <summary>
    /// The roll back reached its limit: with a driver aboard the train is gone (<see cref="GoneAtLimit"/>); with none (the debug roll) it brakes and
    /// comes back for good (Ruling 11).
    /// </summary>
    private void OnLimit()
    {
        GD.Print($"[rails] hijack: the train reaches its limit at S={State.S:0.0} (stop {HijackStop} at S={StopS[HijackStop]:0.0}){(_driverHeld ? " with its driver" : "")}");
        if (_driverHeld && Cart.Driver != null) GoneAtLimit();
        else BrakeRollBack("the limit");
    }

    /// <summary>Stopped on the brake: the run turns to its way of travel again and rolls forward to its stop at walking pace.</summary>
    private void StartReturn()
    {
        var k = HijackStop;
        Hijack = HijackPhase.Returning;
        Party.Dir = 1;
        Party.Reversing = false;
        Party.Limit = StopS[k];
        HijackLimitS = float.NaN;
        GD.Print($"[rails] hijack: the train stops at S={State.S:0.0} and rolls forward to stop {k} ({StopS[k] - State.S:0.0} m)");
        EventBus.Instance.Log(StopTrapLines.ReturnLog);
        if (StopS[k] - State.S < 0.02f) { Party.State = State with { S = StopS[k], V = 0f, Mode = CartMode.Held }; EndHijack(); return; }
        Party.State = State with { Mode = CartMode.Rolling, V = 0f };
        SetCartLayer();
        Party.Settle();
        Party.LastInput = HijackRules.InputFor(Hijack, Party.SlopeAhead, _train.Count, Party.Limit) with { StopAt = Party.Limit }; // no Brake left over from the brake's tick (the T5 review's M13)
        _reliefWarned.Clear();
        RefreshHud();
    }

    /// <summary>Held at its stop again: as it was before the hijack (the gate, the cage hold and the countdown work as before). HijackOver is raised.</summary>
    private void EndHijack()
    {
        var k = HijackStop;
        var relief = Hijack == HijackPhase.ReliefIn; // the relief train after a got-away (Task 7)
        SetDownDriver(); // a driver still aboard (one that rode the roll to its limit) gets down beside the lane, aggro on (the T5 review's M8)
        Hijack = HijackPhase.None;
        HijackStop = -1;
        HijackLimitS = float.NaN;
        Party.Dir = 1;
        Party.Reversing = false;
        Party.Limit = StopS[k];
        Party.State = State with { S = StopS[k], V = 0f, Mode = CartMode.Held };
        Party.Settle();
        Party.LastInput = default;
        SetCartLayer();
        Cart.Speed = 0f;
        if (relief) _hijackWhy = "the relief train";
        Raise(new CartEvent(CartEventKind.HijackOver, Stop: k));
        if (relief)
        {
            GD.Print($"[rails] hijack: the relief train is held at stop {k} ({_goneSeconds:0.0} s after the train went)");
            EventBus.Instance.Log(StopTrapLines.ReliefLog);
            var who = CallCrew(StopTrapLines.CrewRelief, $"relief:{k}:{_hijacksTaken}");
            GD.Print($"[rails] hijack: \"{StopTrapLines.CrewRelief}\" ({who})");
        }
        GD.Print($"[rails] hijack over at stop {k}: the train held again ({(_hijackWhy.Length > 0 ? _hijackWhy : "no brake")})");
        if (PlayerAboard() && GateClear && !Boarding.Running && StopIndex < StopS.Length - 1) StartBoarding();
        RefreshHud();
    }

    /// <summary>
    /// A load, a rise or a wreck: no hijack (it is not saved). The run faces and travels forward again, and a driver still carried is set down
    /// beside the lane. The caller stands the train (<see cref="Restore"/>, a spare's takeover).
    /// </summary>
    private void ResetHijack(string why)
    {
        if (Cart.Driver is { } driver && IsInstanceValid(driver) && driver.Carrier == Cart) driver.Drop(StandBesideLane(driver.GlobalPosition));
        if (Cart.Driver is { } gone && IsInstanceValid(gone)) gone.DeathDrop = null;
        Cart.FreeDriver();
        _driverHeld = false;
        _glowLeft = 0f;
        _rollSeconds = _goneSeconds = 0f;
        var was = Hijack;
        Hijack = HijackPhase.None;
        HijackStop = -1;
        HijackLimitS = float.NaN;
        _hijackWhy = "";
        Party.Dir = 1;
        Party.Reversing = false;
        Cart.Reversing = false;
        if (was != HijackPhase.None) GD.Print($"[rails] hijack ends ({why}): it was {was}");
    }

    // ---- the driver (Task 6) -----------------------------------------------------------------------

    /// <summary>
    /// A hijacker's climb into the cart is over (server and solo; the stop trap's runner): only while <see cref="HijackMayStart"/> holds, the cart
    /// carries <paramref name="e"/> at its driver spot (its death drops it beside the lane), the Glow phase starts (the countdown, if any, cancelled),
    /// its hands glow for <see cref="HijackRules.BrakeGlow"/> (the BoarderThrow telegraph to clients), and the nearest up freed crew member shouts
    /// <see cref="StopTrapLines.CrewCartTaken"/> (once a hijack: the key <c>hijack:{stop}:{n}</c>; with nobody to shout it, it is logged).
    /// <see cref="TickGlow"/> lets the brake off at the glow's end. False (nothing changed) when refused.
    /// </summary>
    internal bool TakeDriver(Enemy e)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion() || e.Carried || e.Leaping) return false;
        if (Cart.Driver != null || !HijackMayStart())
        {
            GD.Print($"[rails] hijack: {e.Name} may not take the cart at stop {StopIndex} ({State.Mode}, hijack {Hijack}, {HeroesSeatedInTrain()} hero(es) seated, chase {ChaseLive}, rise grace {InRiseGrace})");
            return false;
        }
        var k = StopIndex;
        Boarding.Cancel(); // a countdown never runs under a glowing driver
        Cart.Driver = e;
        e.Carry(Cart, () => IsInstanceValid(Cart) ? Cart.DriverSpotTransform() : new Transform3D(Basis.Identity, e.GlobalPosition));
        e.DeathDrop = () => StandBesideLane(e.GlobalPosition);
        e.BoardTarget = null;
        if (e.State is not Enemies.Ai.ChaseState) e.ChangeState(new Enemies.Ai.ChaseState()); // carried, it bites what comes in reach
        Hijack = HijackPhase.Glow;
        HijackStop = k;
        HijackLimitS = float.NaN;
        _hijackWhy = "";
        _glowLeft = HijackRules.BrakeGlow;
        _glowTellLeft = HijackRules.GlowRetell;
        _driverHeld = true;
        _driverGrip = 0;
        _driverHits = 0;
        _hijacksTaken++;
        TelegraphFx.HandGlow(e, HijackRules.BrakeGlow);
        EventBus.Instance.RaiseEnemyTelegraph(e, TelegraphRules.BoarderThrow); // clients glow it too (EnemyStateMsg), as a wagon boarder's crouch
        var who = CallCartTaken(k);
        GD.Print($"[rails] hijack: {e.Name} takes the cart at stop {k} (S={State.S:0.0}); the brake comes off in {HijackRules.BrakeGlow:0.0} s; {who}");
        RefreshHud();
        return true;
    }

    /// <summary>The crew's bark at a hijack's start: the nearest up freed crew member's (subtitle only), else the log's. Says who.</summary>
    private string CallCartTaken(int stop) => CallCrew(StopTrapLines.CrewCartTaken, $"hijack:{stop}:{_hijacksTaken}");

    /// <summary>A crew bark (<paramref name="text"/>, subtitle only) said by the nearest up freed crew member under <paramref name="key"/>, else logged. Says who.</summary>
    private string CallCrew(string text, string key)
    {
        Npc? speaker = null;
        if (Crew is { } crew)
        {
            var at = Cart.GlobalPosition;
            var best = float.MaxValue;
            foreach (var n in crew.Crew)
            {
                if (!IsInstanceValid(n) || n.Dead || n.Downed || crew.StateOf(n) != CrewState.Freed) continue;
                var d = n.GlobalPosition.DistanceSquaredTo(at);
                if (d < best) { best = d; speaker = n; }
            }
        }
        if (speaker != null && AdventureDirector.Instance?.SayAs(speaker, StopTrapLines.Say(text), key, urgent: true) == true)
            return $"{speaker.NpcName} shouts";
        EventBus.Instance.Log(text);
        return "the log calls it";
    }

    /// <summary>The Glow phase's tick: the driver glows on the held train; at the glow's end the brake comes off (<see cref="BeginRollBack"/>).</summary>
    private void TickGlow(float dt)
    {
        _glowLeft -= dt;
        _glowTellLeft -= dt;
        if (_glowLeft > 0f)
        {
            if (_glowTellLeft <= 0f && Cart.Driver is { } glowing && IsInstanceValid(glowing))
            {
                _glowTellLeft += HijackRules.GlowRetell;
                EventBus.Instance.RaiseEnemyTelegraph(glowing, TelegraphRules.BoarderThrow); // told again: a client's glow (1.5 s) runs the whole 3 s
            }
            return;
        }
        var driver = Cart.Driver;
        var k = HijackStop;
        BeginRollBack(driver);
        if (Hijack == HijackPhase.RollingBack)
        {
            if (driver != null && IsInstanceValid(driver)) EventBus.Instance.Log(string.Format(StopTrapLines.BrakeOffLog, StopTrapLines.AName(driver.DisplayName)));
            return;
        }
        GD.Print($"[rails] hijack at stop {k}: the roll back did not start; the driver gets down"); // the stop's room went meanwhile: nothing to roll
        EndHijack();
    }

    /// <summary>
    /// <see cref="EventBus.EnemyHurt"/> (server and solo): a party member's hit on the cart's driver that it lives through, struck from foot or
    /// from the cart itself (<see cref="BoarderRules.Strikes"/>: never from a wagon), takes <see cref="BoarderRules.GripTaken"/> of its kind off its
    /// grip; at <see cref="BoarderRules.KnockedOff"/> it is knocked off. A killing blow is left to its death (<see cref="DriverLost"/>).
    /// </summary>
    private void OnDriverHurt(Enemy e, float amount, Combat.HitFlavor flavor, int byMember)
    {
        if (NetSession.IsClient || !Current || !_driverHeld || e != Cart.Driver || !IsInstanceValid(e) || !e.IsAlive || !BoarderRules.Counts(byMember)) return;
        if (Main.Instance?.Party is not { } party) return;
        var striker = byMember < party.Adventurers.Length && IsInstanceValid(party.Adventurers[byMember]) ? party.Adventurers[byMember] : null;
        var car = striker != null ? CarOf(striker) : -1;
        if (!BoarderRules.Strikes(car >= 0, car, 0)) return; // a wagon's seat never reaches the cart's driver
        _driverGrip += BoarderRules.GripTaken(e.LastHitKind);
        _driverHits++;
        var by = byMember < party.Members.Length ? party.Members[byMember].Name : $"member {byMember}";
        GD.Print($"[rails] hijack: {e.Name} on the cart: grip {_driverGrip}/{BoarderRules.Grip} after a {e.LastHitKind} hit by {by} ({(car >= 0 ? $"from car {car}" : "on foot")}; hp {e.Hp:0}/{e.MaxHp:0})");
        if (!BoarderRules.KnockedOff(_driverGrip)) return;
        GD.Print($"[rails] hijack: {e.Name} knocked off the cart by {by} after {_driverHits} hit(s)");
        EventBus.Instance.Log(string.Format(StopTrapLines.KnockedOffCartLog, by, StopTrapLines.Lower(e.DisplayName)));
        DriverLost(e, "the driver is knocked off");
    }

    /// <summary>
    /// The driver is gone from the cart (knocked off alive, or dead): it is set down beside the lane (a living one aggro on), the cart has no driver,
    /// and the hijack is won back: in the glow it is over at once, on the roll the brake goes on (the return follows).
    /// </summary>
    private void DriverLost(Enemy? driver, string why)
    {
        _driverHeld = false;
        SetDownDriver();
        GD.Print($"[rails] hijack at stop {HijackStop}: {why} ({Hijack})");
        if (Hijack == HijackPhase.Glow) { _hijackWhy = why; EndHijack(); }
        else BrakeRollBack(why);
        RefreshHud();
    }

    /// <summary>The cart's driver, if any, off the cart beside the lane: a living one on its feet, aggro on, its death drop let go; none after.</summary>
    private void SetDownDriver()
    {
        if (Cart.Driver is { } driver && IsInstanceValid(driver))
        {
            if (driver.Carrier == Cart) driver.Drop(StandBesideLane(driver.GlobalPosition));
            driver.DeathDrop = null;
            if (driver.IsAlive && driver.State is not Enemies.Ai.ChaseState) driver.ChangeState(new Enemies.Ai.ChaseState());
        }
        Cart.FreeDriver();
        _driverHeld = false;
    }

    /// <summary>Each physics tick (server and solo): a wipe's rise's grace runs out.</summary>
    private void TickRiseGrace(float dt) { if (_riseGraceLeft > 0f) _riseGraceLeft -= dt; }

    /// <summary>A restore: a wipe's rise at the cart starts its grace (no hijack meanwhile); a load does not.</summary>
    private void StartRiseGrace() => _riseGraceLeft = Main.Instance?.RoseAtCart == true ? StopSiegeRules.RiseGrace : 0f;

    // ---- test hooks ------------------------------------------------------------------------------

    /// <summary>
    /// Test hook (--test-stop-trap=roll; the RIDE server's): a driverless roll back from the stop the train is held at, only at stops 1..N−2 with
    /// room to roll (<see cref="HijackRules.RollRoom"/>). False when none started.
    /// </summary>
    internal bool DebugRollBack()
    {
        if (NetSession.IsClient || NetSession.IsNetworked && NetSession.Instance is not { TestServer: true, TestRide: true }) return false;
        if (HijackLive || State.Mode != CartMode.Held || Halted || StopIndex < 1 || StopIndex > StopS.Length - 2) return false;
        if (!HijackRules.RollRoom(StopS[StopIndex], RollBackLimitAt(StopIndex, out _))) return false;
        GD.Print($"[rails] test: a driverless roll back from stop {StopIndex}");
        BeginRollBack(null);
        return Hijack == HijackPhase.RollingBack;
    }

    /// <summary>
    /// Test hook (--test-stop-trap=wonback): <see cref="BeginRollBack"/> asked straight, with no driver and none of <see cref="DebugRollBack"/>'s own
    /// checks, so a flow sees its guard refuse (the landing, the last stop). True when a roll started.
    /// </summary>
    internal bool DebugBeginRollBack()
    {
        if (NetSession.IsClient || HijackLive) return false;
        BeginRollBack(null);
        return Hijack == HijackPhase.RollingBack;
    }

    /// <summary>Test hook: the live roll back's brake goes on (<see cref="BrakeRollBack"/>, "the test").</summary>
    internal void DebugEndRollBack() => BrakeRollBack("the test");
}
