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
    /// <summary>Hijacks handed a driver on this floor build (each crew bark's key is its own).</summary>
    private int _hijacksTaken;
    /// <summary>Seconds left of a wipe's rise's grace (the restore after a rise at the cart): no hijack starts meanwhile.</summary>
    private float _riseGraceLeft;

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
    internal float RollBackLimitAt(int stop, out string? cut)
    {
        cut = null;
        if (stop < 0 || stop >= StopS.Length) return float.NaN;
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) && e.GetParent() == GetParent() ? e : null;
        if (enc != null) enc.CollectRollBackHazards(_rollBackHazards);
        else _rollBackHazards.Clear();
        var stopS = StopS[stop];
        var limit = HijackRules.LimitS(stopS, _train.Count, _rollBackHazards);
        if (limit > stopS - HijackRules.RollBackReach)
            foreach (var (from, to) in _rollBackHazards)
                if (from < stopS && Mathf.Abs(HijackRules.LimitS(stopS, _train.Count, new[] { (from, to) }) - limit) < 1e-3f) { cut = $"cut by the hazard at S={from:0.0}..{to:0.0}"; break; }
        return limit;
    }

    /// <summary>
    /// The brake comes off (server and solo): the train held at its stop starts rolling back toward its limit with <paramref name="driver"/> (null:
    /// none, the debug roll) holding the cart. A Hijacked event is raised; the countdown, if any, is cancelled.
    /// </summary>
    internal void BeginRollBack(Enemy? driver)
    {
        if (NetSession.IsClient || !Current || StopS.Length == 0) return;
        if (State.Mode != CartMode.Held || Halted || StandingPast || Hijack is not (HijackPhase.None or HijackPhase.Glow)) return;
        var k = StopIndex;
        if (k < 1 || k > StopS.Length - 2) { GD.Print($"[rails] hijack: no roll back from stop {k} (the landing or the last stop)"); return; } // the T5 review's M3: never off a stop's range
        var limit = RollBackLimitAt(k, out var cut);
        if (!HijackRules.RollRoom(StopS[k], limit)) { GD.Print($"[rails] hijack: no roll back from stop {k} (only {StopS[k] - limit:0.0} m of room)"); return; }
        Boarding.Cancel();
        Cart.Driver = driver;
        Hijack = HijackPhase.RollingBack;
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
        if (Hijack == HijackPhase.Gone || State.Mode == CartMode.Wrecked) return; // gone (T7)
        if (Hijack is HijackPhase.Braking or HijackPhase.Returning or HijackPhase.ReliefIn && State.Mode == CartMode.Held) { NextHijackPhase(); return; } // held at its limit as the brake went on, or at its stop
        Party.Step(HijackRules.InputFor(Hijack, Party.SlopeAhead, _train.Count, Party.Limit), dt);
        var lead = HijackLead;
        RunOver(party, lead);
        if (State.Plating <= 0f) { Wreck(); return; } // a big body run over took the last of it
        if (State.Mode == CartMode.Held || Hijack == HijackPhase.Braking && State.V <= 0f) { NextHijackPhase(); return; }
        Warn(party, lead);
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

    /// <summary>The roll back reached its limit. With no driver aboard the train brakes and comes back for good (Ruling 11; T7's got-away needs a driver).</summary>
    private void OnLimit()
    {
        GD.Print($"[rails] hijack: the train reaches its limit at S={State.S:0.0} (stop {HijackStop} at S={StopS[HijackStop]:0.0})");
        BrakeRollBack("the limit");
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
        RefreshHud();
    }

    /// <summary>Held at its stop again: as it was before the hijack (the gate, the cage hold and the countdown work as before). HijackOver is raised.</summary>
    private void EndHijack()
    {
        var k = HijackStop;
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
        Raise(new CartEvent(CartEventKind.HijackOver, Stop: k));
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
    private string CallCartTaken(int stop)
    {
        Npc? speaker = null;
        if (ObjectiveController.Instance is CrewRescueController crew && IsInstanceValid(crew) && crew.GetParent() == GetParent())
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
        if (speaker != null && AdventureDirector.Instance?.SayAs(speaker, StopTrapLines.Say(StopTrapLines.CrewCartTaken), $"hijack:{stop}:{_hijacksTaken}", urgent: true) == true)
            return $"{speaker.NpcName} shouts";
        EventBus.Instance.Log(StopTrapLines.CrewCartTaken);
        return "the log calls it";
    }

    /// <summary>The Glow phase's tick: the driver glows on the held train; at the glow's end the brake comes off (<see cref="BeginRollBack"/>).</summary>
    private void TickGlow(float dt)
    {
        _glowLeft -= dt;
        if (_glowLeft > 0f) return;
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

    /// <summary>Test hook: the live roll back's brake goes on (<see cref="BrakeRollBack"/>, "the test").</summary>
    internal void DebugEndRollBack() => BrakeRollBack("the test");
}
