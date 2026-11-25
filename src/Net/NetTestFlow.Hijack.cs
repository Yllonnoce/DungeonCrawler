using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using Godot;
using Rails = DungeonCrawler.Dungeon.Rails;

namespace DungeonCrawler.Net;

/// <summary>
/// The RIDE pass's hijack steps (plan 16 Task 9, Ruling 21), both at stop 3 before the joiner boards again there. The RIDE server has no siege,
/// wall ambush or shooters (Ruling 2): <see cref="Rails.RailController.DebugHijack"/> is the only way a hijack arises in it.
/// <list type="bullet">
/// <item>Won back: the first client, its prisoners left behind, gets out through its intent (cart:0); the joiner is on foot beside the cart (put there
/// after its throw). The server's hook seats a cinder on the driver spot at once; it glows 3 s and the train rolls back. The joiner presses Board
/// through its intent while it rolls back and is seated; the server lands three blows on the driver for the joiner, 0.5 s apart
/// (<see cref="Rails.RailController.DebugStrikeDriver"/>, counted as that member's): grip 1, 2, then off. The brake, the return, held again; the
/// knocked-off driver is killed by the test's hand a second on (it would fight the crew on foot).</item>
/// <item>Got away: the joiner gets out; with both on foot (and no crew a hijack put off still on the way) a second hook rolls the train to its limit
/// with its driver: gone (the cart hidden on both clients within 1 s), the driver killed by the test's hand a second on; the relief train 12 s
/// later, held at StopS[3] on both within 1 s. The server puts both beside it; the first client boards, then the joiner (the old step's line), the
/// stop's holders die, the departure waits for the thrown crew (booked from 60 m), and the train leaves stop 3 with both, the thrown crew seated.</item>
/// </list>
/// Each client prints the phases, the driver at the spot and off it, the cart hidden and the holds again; both sides print a hijack line every
/// half second of the server's clock (RailController / RailNet), which the script pairs by t.
/// </summary>
public partial class NetTestFlow
{
    /// <summary>The stop the RIDE pass's hijacks run at (ride 4 leaves it on the shipped floor).</summary>
    private const int RideHjStop = 3;
    /// <summary>The server's blows on the driver come this far apart (s): the brief's 0.5.</summary>
    private const double RideHjBlowEvery = 0.5;

    // ---- the server -----------------------------------------------------------------------------------------------------------

    /// <summary>0 wait for both on foot; 1 the won-back driver, wait the joiner seated; 2 the blows; 3 wait held again; 4 wait both on foot again;
    /// 5 the got-away driver, wait gone; 6 wait the relief held; 7 wait the departure; 10 done; −1 failed.</summary>
    private int _hsStep;
    private double _hsAt, _hsKillAt = -1;
    private Enemies.Enemy? _hsDriver;
    private int _hsBlows, _hsMember = -1;
    private bool _hsOk = true, _hsSkipped, _hsReliefTold;
    private string _hsWhy = "";
    private readonly List<string> _hsThrown = new();

    /// <summary>The joiner's boarding again at <paramref name="stop"/> waits for the hijack steps (RideJoinerOn): its line and the holders' deaths come after them.</summary>
    private bool RideHijackHolds(int stop) => stop == RideHjStop && _hsStep is >= 0 and < 7 && !_hsSkipped;

    /// <summary>The hijack steps are through (or not on this line: a four-ride line's stop 3 is the kiln's), and every check held.</summary>
    private bool RideHijackOk => _hsSkipped || _hsStep == 10 && _hsOk;

    private string RideHijackSummary => _hsSkipped ? "not on this line" : _hsStep == 10 ? (_hsOk ? "won back and got away (ok)" : $"FAILED: {_hsWhy}") : _hsStep < 0 ? $"FAILED: {_hsWhy}" : $"stopped at step {_hsStep}";

    private void HsFail(string why)
    {
        _hsOk = false;
        _hsWhy = why;
        _hsStep = -1;
        GD.Print($"[net-server] ride: hijack: {why} (FAIL)");
    }

    private void HsNext(int step) { _hsStep = step; _hsAt = _elapsed; }

    /// <summary>The crew seated in the train's wagons, by name.</summary>
    private static List<string> CrewAboard(Rails.RailController rc)
    {
        var names = new List<string>();
        foreach (var w in rc.Train)
            if (IsInstanceValid(w))
                foreach (var r in w.Riders.Values)
                    if (r is Npc { Crew: true, Dead: false } npc && IsInstanceValid(npc) && npc.Cart == w) names.Add(npc.NpcName);
        names.Sort(string.CompareOrdinal);
        return names;
    }

    /// <summary>The server's hijack steps, every tick of the riding (the first client's block of RideServerTick).</summary>
    private void RideHijackServer(ServerWorld world, Rails.RailController rc, Party.Adventurer rider, Party.PartyController party)
    {
        RideHijackKillDriver();
        if (_hsStep < 0 || _hsStep >= 10) return;
        RideHijackCrewTopUp(rc);
        var line = Line(rc);
        if (line.LastRideStop <= RideHjStop) { _hsSkipped = true; _hsStep = 10; return; }
        if (world.Registry.Online.FirstOrDefault(sl => sl.BotOwner == null && sl.Name == RideJoinerName) is not { } js || js.MemberIndex >= party.Adventurers.Length) return;
        var joiner = party.Adventurers[js.MemberIndex];
        var now = NetSession.Instance.Now;
        var held = rc.State.Mode == Rails.CartMode.Held && !rc.Halted && rc.StopIndex == RideHjStop;
        var t = _elapsed - _hsAt;
        switch (_hsStep)
        {
            case 0: // both on foot at stop 3, the joiner put beside the cart (RideJoinerOn): a second's settle, then the won-back driver
                if (!held || _joinOnShoved != RideHjStop || joiner.Seated || rider.Seated || !rc.HijackMayStart()) { _hsAt = _elapsed; return; }
                if (t < 1.0) return;
                RideHijackStart(rc, rider, 1);
                return;
            case 1: // the joiner boards the rolling-back cart through its intent
                if (joiner.Seated && joiner.Cart == rc.Cart)
                {
                    var v = rc.State.V;
                    var ok = rc.State.Mode == Rails.CartMode.Rolling && Rails.HijackRules.MayBoard(rc.Hijack, v);
                    GD.Print(FormattableString.Invariant($"[net-server] ride: {RideJoinerName} boarded the rolling-back cart through the intent (V={v:0.0}) in seat {joiner.Seat} at S={rc.State.S:0.00}, t={now:0.000} (the cart {rc.State.Mode}, hijack {rc.Hijack}; {(ok ? "ok" : "FAIL")})"));
                    if (!ok) { HsFail($"the joiner sat in the cart {rc.State.Mode} in its {rc.Hijack} at {v:0.00} m/s (want rolling back at {Rails.HijackRules.BoardMaxSpeed} m/s or less)"); return; }
                    _hsMember = js.MemberIndex;
                    _hsBlows = 0;
                    HsNext(2);
                    return;
                }
                if (rc.Hijack is (Rails.HijackPhase.Gone or Rails.HijackPhase.None) && t > 5.0) HsFail($"the roll back went {rc.Hijack} before the joiner boarded it");
                else if (t > 25.0) HsFail($"the joiner never boarded the rolling-back cart (the cart {rc.State.Mode}, hijack {rc.Hijack}, V={rc.State.V:0.00})");
                return;
            case 2: // the blows, 0.5 s apart from the boarding: grip 1, 2, then off
            {
                if (t < RideHjBlowEvery) return;
                _hsAt = _elapsed;
                _hsBlows++;
                var id = _hsDriver != null && IsInstanceValid(_hsDriver) ? _hsDriver.NetId : -1;
                rc.DebugStrikeDriver(_hsMember);
                var off = rc.Cart.Driver == null;
                GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: blow {_hsBlows} on the driver {id} for {RideJoinerName} (DebugStrikeDriver, member {_hsMember}, seated in car {rc.CarOf(joiner)}) at t={now:0.000}: {(off ? "off" : $"grip {rc.DriverGrip}")}"));
                if (off)
                {
                    GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the driver {id} knocked off the cart at t={now:0.000} after {_hsBlows} blow(s) (hijack {rc.Hijack}, S={rc.State.S:0.00}, V={rc.State.V:0.00})"));
                    if (_hsBlows != 3) { HsFail($"the driver was knocked off after {_hsBlows} blow(s), not 3"); return; }
                    _hsKillAt = _elapsed + 1.0;
                    HsNext(3);
                    return;
                }
                if (rc.DriverGrip != _hsBlows) { HsFail($"blow {_hsBlows} left the driver's grip at {rc.DriverGrip}"); return; }
                if (_hsBlows >= 3) HsFail("three blows did not knock the driver off");
                return;
            }
            case 3: // the brake, the return, held again
                if (rc.Hijack == Rails.HijackPhase.None && held)
                {
                    GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: won back: held again at stop {RideHjStop} at t={now:0.000} (S={rc.State.S:0.000}, StopS {rc.StopS[RideHjStop]:0.000}); {RideJoinerName} seated {Yes(joiner.Seated && joiner.Cart == rc.Cart)}; the crew aboard [{string.Join(", ", CrewAboard(rc))}]"));
                    HsNext(4);
                    _hsSettle = _elapsed;
                    return;
                }
                if (t > 30.0) HsFail($"the train never came back to stop {RideHjStop} after the knock-off (hijack {rc.Hijack}, {rc.State.Mode}, S={rc.State.S:0.00})");
                return;
            case 4: // the joiner gets out; both on foot, nobody put off still on the way back: a second's settle, then the got-away driver
            {
                var thrown = ObjectiveController.Instance is CrewRescueController crew ? crew.ThrownOnTheWay(RideHjStop) : 0;
                if (!held || joiner.Seated || rider.Seated || thrown > 0 || !rc.HijackMayStart())
                {
                    if (t > 45.0) HsFail($"the second hijack never could start (held {held}, {RideJoinerName} seated {joiner.Seated}, {rider.State.Name} seated {rider.Seated}, {thrown} crew on the way back, may start {rc.HijackMayStart()})");
                    _hsSettle = _elapsed;
                    return;
                }
                if (_elapsed - _hsSettle < 1.0) return;
                RideHijackStart(rc, rider, 2);
                return;
            }
            case 5: // to its limit with the driver aboard: gone
                if (rc.Hijack == Rails.HijackPhase.Gone)
                {
                    _hsThrown.Clear();
                    if (ObjectiveController.Instance is CrewRescueController crew)
                        foreach (var id in crew.ThrownIds)
                            if (crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == id) is { } npc) _hsThrown.Add(npc.NpcName);
                    _hsThrown.Sort(string.CompareOrdinal);
                    GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the train is gone at its limit at t={now:0.000} (S={rc.State.S:0.000}; {rc.PutOffOnWay} crew put off on the way, {rc.PutOffAtLimit} at the limit; thrown [{string.Join(", ", _hsThrown)}]; the cart {(rc.Cart.Visible ? "SHOWN" : "hidden")}, {rc.Train.Count} wagon(s) coupled)"));
                    if (_hsThrown.Count == 0) { HsFail("the train got away with nobody put off (no crew aboard it)"); return; }
                    _hsKillAt = _elapsed + 1.0;
                    _hsReliefTold = false;
                    HsNext(6);
                    return;
                }
                if (rc.Hijack == Rails.HijackPhase.None && t > 4.0) HsFail("the second hijack ended before the train got away");
                else if (t > 40.0) HsFail($"the train never got away (hijack {rc.Hijack}, S={rc.State.S:0.00})");
                return;
            case 6: // the relief train rolls in and is held at the stop: both are put beside it
                if (!_hsReliefTold && rc.Hijack == Rails.HijackPhase.ReliefIn)
                {
                    _hsReliefTold = true;
                    GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the relief train rolls in at t={now:0.000} from S={rc.State.S:0.000} ({t:0.0} s after the train went; train [{string.Join(", ", rc.Train.Select(w => w.Number))}])"));
                }
                if (rc.Hijack == Rails.HijackPhase.None && held)
                {
                    var stopS = rc.StopS[RideHjStop];
                    var ok = Mathf.Abs(rc.State.S - stopS) <= 0.05f && _hsReliefTold;
                    GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the relief train is held at stop {RideHjStop} at t={now:0.000} (S={rc.State.S:0.000}, StopS {stopS:0.000}; {t:0.0} s after the train went; train [{string.Join(", ", rc.Train.Select(w => w.Number))}]; {(ok ? "ok" : "FAIL")})"));
                    if (!ok) { HsFail("the relief train was not held at its stop"); return; }
                    var beside = WithinReach(rc);
                    var along = rc.Cart.GlobalTransform.Basis.Z with { Y = 0f };
                    along = along.LengthSquared() > 1e-4f ? along.Normalized() : Vector3.Forward;
                    rider.Shove(beside + along * 0.9f);
                    joiner.Shove(beside - along * 0.9f);
                    GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: {rider.State.Name} and {RideJoinerName} are put beside the relief train at stop {RideHjStop} ({rc.Cart.TubDistance(rider):0.00} and {rc.Cart.TubDistance(joiner):0.00} m from its tub) to board it"));
                    HsNext(7);
                    return;
                }
                if (t > 60.0) HsFail($"no relief train was held at stop {RideHjStop} 60 s after the train went (hijack {rc.Hijack}, {rc.State.Mode}, S={rc.State.S:0.00})");
                return;
            case 7: // the departure (after both boarded, the holders' deaths and the thrown crew's wait): the thrown crew seated in the train
            {
                if (!(rc.State.Mode == Rails.CartMode.Rolling && !rc.HijackLive && rc.StopIndex == RideHjStop))
                {
                    if (t > 90.0) HsFail($"the train never left stop {RideHjStop} after the relief ({rc.State.Mode}, hijack {rc.Hijack})");
                    return;
                }
                var seated = new List<string>();
                var missing = new List<string>();
                if (ObjectiveController.Instance is CrewRescueController crew)
                    foreach (var name in _hsThrown)
                    {
                        var npc = crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.NpcName == name);
                        if (npc is { Dead: false, Seated: true } && npc.Cart is Rails.TrainWagon { Coupled: true } w) seated.Add($"{name} in W{w.Number} seat {npc.Seat}");
                        else missing.Add($"{name} {(npc == null ? "gone" : npc.Dead ? "dead" : "on foot")}");
                    }
                var ok = missing.Count == 0 && seated.Count > 0;
                GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the thrown crew [{string.Join(", ", _hsThrown)}] seated in the train at the departure from stop {RideHjStop} at t={now:0.000}: [{string.Join(", ", seated)}]{(missing.Count > 0 ? $", NOT [{string.Join(", ", missing)}]" : "")} ({(ok ? "ok" : "FAIL")})"));
                if (!ok) { HsFail("the thrown crew were not all seated at the departure"); return; }
                _hsStep = 10;
                return;
            }
        }
    }

    private double _hsSettle;
    private readonly Dictionary<Npc, double> _hsTopUpTried = new();

    /// <summary>
    /// The crew a hijack put off, on foot by the stop's holders until the train is back, are kept above half their hp (a downed one revived) as the
    /// train's own crew are (RideCrewGuard): the pass checks the wire, not the fight. Each top-up is logged.
    /// </summary>
    private void RideHijackCrewTopUp(Rails.RailController rc)
    {
        if (ObjectiveController.Instance is not CrewRescueController crew || crew.ThrownIds.Count == 0) return;
        foreach (var npc in crew.Crew)
        {
            if (!IsInstanceValid(npc) || npc.Dead || npc.Seated || !crew.ThrownIds.Contains(npc.Id)) continue;
            if (npc.Downed)
            {
                if (_hsTopUpTried.TryGetValue(npc, out var tried) && _elapsed - tried < 1.0) continue; // a refused revive (a hostile too close): once a second
                _hsTopUpTried[npc] = _elapsed;
                if (!npc.TryRevive()) continue;
                npc.RestoreHp(1f);
            }
            else if (npc.Hp < npc.MaxHp * 0.5f) npc.RestoreHp(1f);
            else continue;
            GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: crew top-up: {npc.NpcName}, put off by the hijack, on foot at {npc.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart (stop {rc.StopIndex})"));
        }
    }

    /// <summary>A hijack by the server's hook (<paramref name="n"/> 1: to be won back, 2: to get away), with both heroes on foot.</summary>
    private void RideHijackStart(Rails.RailController rc, Party.Adventurer rider, int n)
    {
        var aboard = CrewAboard(rc);
        var d = rc.DebugHijack();
        if (d == null) { HsFail($"DebugHijack {n} made no driver at stop {rc.StopIndex} (may start {rc.HijackMayStart()}, hijack {rc.Hijack})"); return; }
        _hsDriver = d;
        GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the test's driver {d.Name} (net id {d.NetId}) takes the cart at stop {rc.StopIndex} at t={NetSession.Instance.Now:0.000} (DebugHijack {n}: {(n == 1 ? "won back" : "got away")}); {rider.State.Name} and {RideJoinerName} on foot; the crew aboard [{string.Join(", ", aboard)}]; the hijack {rc.Hijack}"));
        HsNext(n == 1 ? 1 : 5);
    }

    /// <summary>A second after its knock-off (or its train's going), the test's driver is killed by the test's hand: on foot it would fight the crew.</summary>
    private void RideHijackKillDriver()
    {
        if (_hsKillAt < 0 || _elapsed < _hsKillAt) return;
        _hsKillAt = -1;
        if (_hsDriver is not { } d || !IsInstanceValid(d) || !d.IsAlive) return;
        var carried = d.Carried;
        GD.Print(FormattableString.Invariant($"[net-server] ride: hijack: the test's driver {d.Name} (net id {d.NetId}) is killed by the test's hand (carried {carried}, {d.State.Name})"));
        if (carried) return; // never met: off the cart by now
        d.DebugHoldHp = false;
        d.Kill();
    }

    // ---- a client: the phases, the driver, the cart hidden, held again (both clients) --------------------------------------------

    private Rails.HijackPhase _hwPhase = Rails.HijackPhase.None, _hwWas = Rails.HijackPhase.None;
    private int _hwDriver = -1, _hwDriverTold = -1;
    private double _hwDriverSince, _hwPendingAt = -1;
    private bool _hwHiddenTold, _hwWonHeld, _hwReliefHeld;
    /// <summary>Every enemy seen driving the party's cart here (the throw check never takes their telegraphs).</summary>
    private readonly HashSet<int> _hwDrivers = new();

    /// <summary>Each RIDE client, every tick on floor 12: the hijack's phases as the server's state tells them, its driver at the spot and off it, the cart hidden, held again.</summary>
    private void RideHijackWatch(ClientWorld client, Main main, Rails.RailController rc)
    {
        if (main.Rebuilding || !rc.Following || rc.Follower is not { HasState: true } f) return;
        var now = client.ServerNow;
        var phase = rc.Hijack;
        if (phase != _hwPhase)
        {
            GD.Print(FormattableString.Invariant($"[net] ride: hijack phase {phase} here from t={now:0.000} (was {_hwPhase}; S={f.S:0.00}, the cart {rc.State.Mode} at stop {rc.StopIndex}, {(rc.Cart.Visible ? "shown" : "hidden")}, train [{string.Join(", ", rc.Train.Select(w => w.Number))}])"));
            if (phase == Rails.HijackPhase.None) { _hwWas = _hwPhase; _hwPendingAt = now; }
            if (phase == Rails.HijackPhase.Gone) _hwHiddenTold = false;
            _hwPhase = phase;
        }
        if (_hwPendingAt >= 0 && phase == Rails.HijackPhase.None && rc.State.Mode == Rails.CartMode.Held && rc.StopIndex < rc.StopS.Length)
        {
            var stopS = rc.StopS[rc.StopIndex];
            if (Mathf.Abs(f.S - stopS) <= 0.05f || now - _hwPendingAt >= 1.0)
            {
                var relief = _hwWas == Rails.HijackPhase.ReliefIn;
                GD.Print(FormattableString.Invariant($"[net] ride: held again at stop {rc.StopIndex} here at t={now:0.000} (S={f.S:0.000}, StopS {stopS:0.000}; {(relief ? "the relief train" : "the return")}; the cart {(rc.Cart.Visible ? "shown" : "HIDDEN")}, train [{string.Join(", ", rc.Train.Select(w => w.Number))}])"));
                if (relief) _hwReliefHeld = true;
                else _hwWonHeld = true;
                _hwPendingAt = -1;
            }
        }
        else if (phase != Rails.HijackPhase.None) _hwPendingAt = -1;
        if (phase == Rails.HijackPhase.Gone && !_hwHiddenTold && !rc.Cart.Visible)
        {
            _hwHiddenTold = true;
            GD.Print(FormattableString.Invariant($"[net] ride: the cart hidden here at t={now:0.000} (Gone; S={f.S:0.000}, train [{string.Join(", ", rc.Train.Select(w => w.Number))}], {rc.Wagons.Count(w => IsInstanceValid(w) && w.Visible)} wagon(s) shown)"));
        }
        var driver = rc.Cart.Driver is { } d && IsInstanceValid(d) && d.Carrier == rc.Cart && d.IsAlive ? d : null;
        var id = driver?.NetId ?? -1;
        if (id != _hwDriver)
        {
            if (_hwDriver >= 0) GD.Print(FormattableString.Invariant($"[net] ride: the driver {_hwDriver} off the cart here at t={now:0.000} (hijack {phase}, S={f.S:0.00})"));
            _hwDriver = id;
            _hwDriverSince = now;
            if (id >= 0) _hwDrivers.Add(id);
        }
        if (driver != null && _hwDriverTold != id)
        {
            var off = driver.GlobalPosition.DistanceTo(rc.Cart.DriverSpotTransform().Origin);
            if (off <= 0.5f || now - _hwDriverSince >= 1.0)
            {
                _hwDriverTold = id;
                GD.Print(FormattableString.Invariant($"[net] ride: the driver {id} aboard the cart here at t={now:0.000}, {off:0.00} m from the driver spot ({driver.DisplayName}; hijack {phase}) ({(off <= 0.5f ? "ok" : "FAIL")})"));
            }
        }
    }

    // ---- the first client ------------------------------------------------------------------------------------------------------

    private int _hrStep;
    private double _hrAt;

    private void HrNext(int step) { _hrStep = step; _hrAt = _clock; }

    /// <summary>
    /// The first client in step 53 at stop 3 (seated from stop 3 on): once its prisoners are left behind (RideLeaveCrew, the normal tick), it gets
    /// out through the intent (cart:0) and stays on foot through both hijacks; once the relief train is held there it boards again through the
    /// intent. True while it owns the tick (the step's own "in the cart" check is waived), false otherwise.
    /// </summary>
    private bool RideHijackRider(NetSession net, Main main, Rails.RailController rc, Party.Adventurer me)
    {
        if (_hrStep >= 9) return false;
        if (Line(rc).LastRideStop <= RideHjStop) { _hrStep = 9; return false; }
        if (rc.StopIndex != RideHjStop) return _hrStep > 0 && RideFail(net, $"hijack: the cart left stop {RideHjStop} here with the rider's hijack steps at step {_hrStep} (stop {rc.StopIndex}, {rc.State.Mode})");
        var t = _clock - _hrAt;
        switch (_hrStep)
        {
            case 0:
                if (rc.State.Mode != Rails.CartMode.Held || rc.Halted || !me.Seated || me.Cart != rc.Cart || !_rideLeftAt.Contains(RideHjStop) || rc.CageHeld) return false;
                if (NetRefResolver.RefOf(rc.Cart) is not { } cartRef) return RideFail(net, "hijack: the party's cart has no net ref here");
                net.Send(new InteractIntent(cartRef));
                GD.Print($"[net] ride: hijack: at stop {RideHjStop}, its prisoners left behind, the rider gets out through the intent ({cartRef}; the cart offers \"{rc.Cart.Prompt}\") for the hijack steps");
                HrNext(1);
                return true;
            case 1:
                if (!me.Seated)
                {
                    GD.Print(FormattableString.Invariant($"[net] ride: hijack: the rider is on foot at stop {RideHjStop} here, {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.00} m from the cart ({t:0.00} s after the press)"));
                    HrNext(2);
                    return true;
                }
                return t > 5 && RideFail(net, $"hijack: the rider was not let out at stop {RideHjStop} 5 s after its press (the cart offers \"{rc.Cart.Prompt}\")");
            case 2: // on foot through both hijacks
                if (!_hwReliefHeld) return t > 150 && RideFail(net, $"hijack: no relief train held at stop {RideHjStop} here 150 s after the rider got out (hijack {rc.Hijack}, won back held {Yes(_hwWonHeld)})");
                _rideAskedAt = -1; // TryBoard's 2 s throttle reads _clock
                HrNext(3);
                return true;
            case 3: // the relief train held: board it (the server put us beside it)
                if (me.Seated && me.Cart == rc.Cart)
                {
                    GD.Print($"[net] ride: hijack: the rider boarded the relief train at stop {RideHjStop} here through the intent in seat {me.Seat}");
                    _hrStep = 9;
                    return false;
                }
                TryBoard(net, rc, me, $"stop {RideHjStop} (the relief train)");
                return t > 20 && RideFail(net, FormattableString.Invariant($"hijack: the rider never boarded the relief train at stop {RideHjStop} (the cart offers \"{rc.Cart.Prompt}\", {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.00} m)"));
        }
        return false;
    }

    // ---- the joiner ------------------------------------------------------------------------------------------------------------

    private int _hjcStep;
    private double _hjcAt;
    private bool _hjcResent;

    private void HjcNext(int step) { _hjcStep = step; _hjcAt = _clock; _hjcResent = false; }

    /// <summary>
    /// The joiner in step 75 at stop 3 (on foot, put beside the cart after its throw): it waits through the glow, presses Board through the intent
    /// as the train rolls back and rides the roll, the knock-off and the return seated; held again it gets out through the intent, and on foot
    /// through the got-away; once the relief train is held there and the first client sits in it, it boards it through the intent (the step's
    /// own "boarded again" line follows). True while it owns the tick, false otherwise.
    /// </summary>
    private bool RideHijackJoiner(NetSession net, Main main, Rails.RailController rc, Party.Adventurer me)
    {
        if (_hjcStep >= 9) return false;
        if (Line(rc).LastRideStop <= RideHjStop) { _hjcStep = 9; return false; }
        if (rc.StopIndex != RideHjStop) return _hjcStep > 0 && RideFail(net, $"hijack: the cart left stop {RideHjStop} here with the joiner's hijack steps at step {_hjcStep} (stop {rc.StopIndex}, {rc.State.Mode})");
        var t = _clock - _hjcAt;
        var cartRef = NetRefResolver.RefOf(rc.Cart);
        if (cartRef == null) return RideFail(net, "hijack: the party's cart has no net ref here");
        switch (_hjcStep)
        {
            case 0: // held at stop 3, on foot: no boarding until the roll back
                if (rc.State.Mode != Rails.CartMode.Held || rc.Halted || me.Seated) return false;
                GD.Print(FormattableString.Invariant($"[net] ride: hijack: the joiner waits on foot at stop {RideHjStop}, {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.00} m from the cart (it offers \"{rc.Cart.Prompt}\"), for the roll back"));
                HjcNext(1);
                return true;
            case 1: // the roll back: Board pressed on the first frame it offers it
            {
                if (me.Seated) return RideFail(net, $"hijack: the joiner sat in a cart at stop {RideHjStop} before the roll back (hijack {rc.Hijack})");
                var prompt = rc.Cart.Prompt;
                if (rc.Hijack != Rails.HijackPhase.RollingBack || prompt != "Board the cart")
                    return t > 60 && RideFail(net, FormattableString.Invariant($"hijack: no roll back offered \"Board the cart\" here 60 s on (hijack {rc.Hijack}, the cart offers \"{prompt}\", {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.00} m)"));
                net.Send(new InteractIntent(cartRef));
                GD.Print(FormattableString.Invariant($"[net] ride: hijack: the cart rolls back here at t={_wireClient?.ServerNow ?? 0:0.000} (V={rc.State.V:0.00}, S={rc.State.S:0.00}, {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.00} m from us); it offers \"{prompt}\"; pressed through the intent ({cartRef})"));
                HjcNext(2);
                return true;
            }
            case 2:
                if (me.Seated && me.Cart == rc.Cart)
                {
                    GD.Print(FormattableString.Invariant($"[net] ride: boarded the rolling-back cart here (seat {me.Seat}; the cart {rc.State.Mode} in its {rc.Hijack} at V={rc.State.V:0.00}, S={rc.State.S:0.00}) at t={_wireClient?.ServerNow ?? 0:0.000}"));
                    HjcNext(3);
                    return true;
                }
                if (t > 1.0 && !_hjcResent && rc.Cart.Prompt == "Board the cart") { _hjcResent = true; net.Send(new InteractIntent(cartRef)); GD.Print("[net] ride: hijack: not seated 1 s after the press; pressed Board once more"); }
                return t > 5 && RideFail(net, FormattableString.Invariant($"hijack: the joiner was not seated 5 s after pressing Board on the rolling-back cart (hijack {rc.Hijack}, V={rc.State.V:0.00}, the cart offers \"{rc.Cart.Prompt}\")"));
            case 3: // seated through the knock-off and the return; held again: get out
                if (me.Cart != rc.Cart) return RideFail(net, $"hijack: the joiner left the cart before it was held again (hijack {rc.Hijack})");
                if (!_hwWonHeld) return t > 40 && RideFail(net, $"hijack: the train was not held again at stop {RideHjStop} here 40 s after the joiner boarded it (hijack {rc.Hijack}, {rc.State.Mode})");
                net.Send(new InteractIntent(cartRef));
                GD.Print($"[net] ride: hijack: held again at stop {RideHjStop} with us seated; the cart offers \"{rc.Cart.Prompt}\"; asked to get out through the intent for the second hijack");
                HjcNext(4);
                return true;
            case 4:
                if (!me.Seated) { GD.Print($"[net] ride: hijack: the joiner is on foot again at stop {RideHjStop}"); HjcNext(5); return true; }
                if (t > 1.5 && !_hjcResent) { _hjcResent = true; net.Send(new InteractIntent(cartRef)); }
                return t > 5 && RideFail(net, $"hijack: the joiner was not let out at stop {RideHjStop} after the return (the cart offers \"{rc.Cart.Prompt}\")");
            case 5: // on foot through the got-away
                if (!_hwReliefHeld) return t > 120 && RideFail(net, $"hijack: no relief train held at stop {RideHjStop} here 120 s after the joiner got out (hijack {rc.Hijack})");
                _rideAskedAt = -1;
                HjcNext(6);
                return true;
            case 6: // the first client boards first; then the relief train through the intent (the step's own line follows on the next pass)
                if (me.Seated && me.Cart == rc.Cart) { _hjcStep = 9; return false; }
                if (main.Party.Adventurers.Any(a => IsInstanceValid(a) && a != me && !a.Parked && a.Cart == rc.Cart)) TryBoard(net, rc, me, $"stop {RideHjStop} (the relief train)");
                return t > 30 && RideFail(net, FormattableString.Invariant($"hijack: the joiner never boarded the relief train at stop {RideHjStop} (the cart offers \"{rc.Cart.Prompt}\", {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.00} m)"));
        }
        return false;
    }
}
