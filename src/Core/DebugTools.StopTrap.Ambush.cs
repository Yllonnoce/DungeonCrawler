using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-stop-trap=ambush (plan 16 Task 4) ------------------------------------------------------------------------------------
// The stop-wall ambushers, in order (any step out of its window fails):
//  (a) placement: the controller's points equal a fresh StopAmbushRules.Place on the same layout, cages and seed (stop 1 has 2, stop 2 has 3),
//      every one in its stop's room; AddStopPoints returned the corridor count and the spawner's first points equal a fresh
//      AmbushRules.Place (Ruling 33: the saved SprungAmbushes indices keep their points).
//  (b) rc.Restore(1) arms nothing: the lead boards and gets out at stop 1 and no wall bursts in 7.5 s. Then rc.Restore(0), the lead boards,
//      the train departs and rides ride 1 seated; at the Arrived at stop 1 the lead gets out: exactly 2 creatures burst 1.5 +- 0.2 s after
//      its Unseated, each with "bursts from the wall" or "rubble" logged, an AmbushSprung raised per point, every one hunting (ForceAggro).
//  (c) a save and a load at stop 1: the sprung points stay sprung, and the lead gets out and in twice over 10 s with no burst.
//  (d) stop 1's ambushers die by the test's hand, the holders too (RideOnTo), the lead stays seated and rides to stop 2: nobody steps off,
//      and 3 creatures burst 6 +- 0.2 s after the Arrived.
public partial class DebugTools
{
    private const float AmWindow = 0.2f;

    private bool _amListening;
    private RailController? _amRc;
    private readonly List<(double At, int Index)> _amSprung = new();
    private readonly List<(double At, string Text)> _amBurstLogs = new();
    private readonly List<(double At, int Stop)> _amArrived = new();
    private readonly List<(double At, int Rider, bool Held, int Stop)> _amUnseated = new();
    private int _amFirst = -1, _amStop1, _amStop2;
    private double _amMark;
    private int _amToggles, _amOuts;

    private void AmbushListen()
    {
        if (_amListening) return;
        _amListening = true;
        var bus = EventBus.Instance;
        Action<int> sprung = i => { _amSprung.Add((StNow, i)); GD.Print($"[stoptrap] heard: ambush point {i} sprung"); };
        Action<string> message = text => { if (text.Contains("bursts from the")) _amBurstLogs.Add((StNow, text)); };
        bus.AmbushSprung += sprung;
        bus.Message += message;
        _staticUnhooks.Add(() => { bus.AmbushSprung -= sprung; bus.Message -= message; });
    }

    /// <summary>Each frame: the current line's arrivals and get-outs are heard (a load builds a new one).</summary>
    private void AmbushWatchRails(RailController rc)
    {
        if (rc == _amRc) return;
        _amRc = rc;
        rc.CartEvents += e =>
        {
            if (e.Kind == CartEventKind.Arrived) { _amArrived.Add((StNow, e.Stop)); GD.Print($"[stoptrap] heard: arrived at stop {e.Stop}"); }
            if (e.Kind == CartEventKind.Unseated) _amUnseated.Add((StNow, e.Rider, rc.State.Mode == CartMode.Held, rc.StopIndex));
        };
    }

    /// <summary>The stop-wall points of <paramref name="stop"/> sprung since <paramref name="since"/> (spawner indices from <see cref="_amFirst"/>).</summary>
    private List<(double At, int Index)> AmSprungAt(AmbushSpawner spawner, int stop, double since)
        => _amSprung.Where(s => s.At >= since && s.Index >= _amFirst && s.Index < spawner.Points.Count && spawner.Points[s.Index].Stop == stop).ToList();

    private List<(double At, int Index)> AmStopSprungSince(double since) => _amSprung.Where(s => s.At >= since && _amFirst >= 0 && s.Index >= _amFirst).ToList();

    /// <summary>The lead gets out of the party's cart (its own "Get out"); null when they did, else why not.</summary>
    private static string? AmGetOut(Main main, RailController rc)
    {
        if (main.Party.Local is not { } me) return "no local hero";
        if (me.Cart != rc.Cart) return "the lead is not in the party's cart";
        rc.Cart.Interact(main.Party, me);
        return me.Cart == rc.Cart ? $"the lead's Get out did nothing (prompt \"{rc.Cart.Prompt}\")" : null;
    }

    private void RunStopTrapAmbush(Main main, FloorLayout layout, Node3D root, RailController rc, PortalSpawner portals)
    {
        var party = main.Party;
        void Fail(string why) => StopTrapFail(why);
        AmbushWatchRails(rc);
        if (root.GetChildren().OfType<AmbushSpawner>().FirstOrDefault() is not { } spawner) { if (_stClock > 3f) Fail("floor 12 has no ambush spawner"); return; }
        if (StopTrapController.Instance is not { } trap || !IsInstanceValid(trap) || trap.GetParent() != root) { if (_stClock > 3f) Fail("floor 12 has no StopTrapController"); return; }
        var me = party.Local;

        switch (_stStep)
        {
            case 0 when _stClock >= 1.5f: // (a) the placement
            {
                if (rc.StopS.Length != Floor12Stops) { Fail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("floor 12 has no CrewRescueController"); return; }
                if (!Settings.Ambushes) { Fail("Settings.Ambushes is off"); return; }
                AmbushListen();
                var rail = layout.Rail!;
                var count = AmbushRules.Count(12, Adventure.Run.ClauseRules.Active.AmbushesDoubled);
                var corridor = AmbushRules.Place(layout, main.Builder.RegularEnemies(12), layout.Seed, count);
                var cages = ctrl.Cages.ToDictionary(c => c.Stop, c => c.Cell);
                var fresh = StopAmbushRules.Place(layout, rail.Stops, cages, layout.Seed);
                _amFirst = trap.AmbushFirst;
                var mine = spawner.Points.Skip(Math.Max(0, _amFirst)).ToList();
                foreach (var p in mine) GD.Print($"[stoptrap] (a) point {spawner.Points.ToList().IndexOf(p)}: stop {p.Stop} {p.EnemyId} at {p.Trigger} from {(p.FromRubble ? "rubble" : "wall")} {p.Cover}");
                _amStop1 = mine.Count(p => p.Stop == 1);
                _amStop2 = mine.Count(p => p.Stop == 2);
                GD.Print($"[stoptrap] (a) {corridor.Count} corridor point(s) (count {count}), AddStopPoints returned {_amFirst}; {mine.Count} stop point(s): [{string.Join(", ", mine.GroupBy(p => p.Stop).Select(g => $"stop {g.Key}: {g.Count()}"))}]; cages [{string.Join(", ", cages.Select(kv => $"{kv.Key}@{kv.Value}"))}]");
                if (_amFirst != corridor.Count) { Fail($"AddStopPoints returned {_amFirst}, not the corridor count {corridor.Count}"); return; }
                if (!spawner.Points.Take(_amFirst).SequenceEqual(corridor)) { Fail("the spawner's corridor points are not a fresh AmbushRules.Place's"); return; }
                if (!mine.SequenceEqual(fresh) || !trap.AmbushPoints.SequenceEqual(fresh)) { Fail($"the stop points ({mine.Count}) are not a fresh StopAmbushRules.Place's ({fresh.Count})"); return; }
                if (_amStop1 != 2 || _amStop2 != 3) { Fail($"stop 1 has {_amStop1} point(s) and stop 2 has {_amStop2}, not 2 and 3"); return; }
                foreach (var p in mine)
                    if (p.Stop < 1 || !layout.Rooms[rail.Stops[p.Stop].Room].Contains(p.Trigger) || layout.IsRail(p.Trigger)) { Fail($"point at {p.Trigger} (stop {p.Stop}) is not in its stop's room or lies on the line"); return; }
                // (b) the restore arms nothing
                rc.Restore(1);
                if (PerfBeside(main, layout, rc.Cart.GlobalPosition) is not { } stand) { Fail("no walkable cell beside the cart at stop 1"); return; }
                party.Teleport(stand + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                _amMark = StNow;
                GD.Print($"[stoptrap] (b) rc.Restore(1): armed stop {trap.AmbushArmedStop}");
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 1: // (b) after the restore: board, get out, and nothing bursts
            {
                if (_stSub == 0 && _stClock >= 1f)
                {
                    if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                    _stSub = 1;
                    break;
                }
                if (_stSub == 1 && _stClock >= 2.5f)
                {
                    if (AmGetOut(main, rc) is { } why) { Fail($"(b) after the restore: {why}"); return; }
                    _stSub = 2;
                    break;
                }
                if (_stSub < 2 || _stClock < 7.5f) break;
                var outs = _amUnseated.Count(u => u.At >= _amMark && u.Held && u.Stop == 1);
                var burst = AmStopSprungSince(_amMark);
                GD.Print($"[stoptrap] (b) 7.5 s after rc.Restore(1): {outs} get-out(s) held at stop 1, {burst.Count} wall burst(s), armed stop {trap.AmbushArmedStop}");
                if (outs == 0) { Fail("nobody got out at stop 1 after the restore"); return; }
                if (burst.Count > 0 || trap.AmbushArmedStop != -1) { Fail($"the restore armed the walls: {burst.Count} burst(s), armed stop {trap.AmbushArmedStop}"); return; }
                rc.Restore(0);
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 2 when _stClock >= 1f: // (b) the lead boards at the landing
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 3: // (b) ride 1, seated
            {
                if (_stSub == 0)
                {
                    if (rc.State.Mode != CartMode.Rolling) { if (_stClock > 15f) Fail($"ride 1 did not depart (mode {rc.State.Mode}, lead seated {me?.Seated})"); break; }
                    GD.Print($"[stoptrap] (b) departed stop 0 with {party.Active.Count(a => a.Seated)} hero(es) seated");
                    _stSub = 1; _stClock = 0f;
                    break;
                }
                if (rc.State.Mode == CartMode.Wrecked) { Fail("the cart wrecked on ride 1"); return; }
                if (rc.State.Mode != CartMode.Held || rc.Halted || rc.StopIndex != 1) { if (_stClock > RideOnRideSeconds) Fail("ride 1 never reached stop 1"); break; }
                var arrived = _amArrived.LastOrDefault(a => a.Stop == 1);
                if (arrived.Stop != 1) { Fail("held at stop 1 with no Arrived heard"); return; }
                if (trap.AmbushArmedStop != 1) { Fail($"the arrival at stop 1 armed stop {trap.AmbushArmedStop}, not 1"); return; }
                if (me == null || !me.Seated) { Fail("the lead was not seated at the arrival at stop 1"); return; }
                var before = _amUnseated.Count(u => u.At >= arrived.At && u.Held && u.Stop == 1 && u.Rider < 100);
                if (AmGetOut(main, rc) is { } why) { Fail($"(b) at stop 1: {why}"); return; }
                GD.Print($"[stoptrap] (b) held at stop 1 {StNow - arrived.At:0.00} s after the Arrived; the lead gets out ({before} hero get-out(s) before it)");
                _amMark = arrived.At;
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 4: // (b) the burst
            {
                if (_stClock < 3.5f) break;
                var heroOut = _amUnseated.Where(u => u.At >= _amMark && u.Held && u.Stop == 1 && u.Rider < 100).ToList();
                if (heroOut.Count == 0) { Fail("no hero's Unseated heard at stop 1"); return; }
                var first = heroOut[0];
                var leadOut = heroOut.FirstOrDefault(u => u.Rider == me?.RiderId);
                var sprung = AmSprungAt(spawner, 1, _amMark);
                var logs = _amBurstLogs.Where(l => l.At >= _amMark).ToList();
                var ambushers = Enemies.Enemy.Live.Where(e => IsInstanceValid(e) && e.IsAlive && spawner.IsStopAmbusher(e)).ToList();
                GD.Print($"[stoptrap] (b) the first hero out (rider {first.Rider}) {first.At - _amMark:0.00} s after the Arrived, the lead's {(leadOut.Rider == me?.RiderId && leadOut.At > 0 ? $"{leadOut.At - _amMark:0.00}" : "never")}; "
                    + $"{sprung.Count} stop-1 point(s) sprung at [{string.Join(", ", sprung.Select(s => $"{s.Index}@+{s.At - first.At:0.00}"))}] after it; logs [{string.Join(" | ", logs.Select(l => l.Text))}]; "
                    + $"ambushers [{string.Join(", ", ambushers.Select(e => $"{e.DisplayName}:{e.State.Name}"))}]");
                if (first.Rider != me?.RiderId) { Fail($"rider {first.Rider} got out before the lead"); return; }
                if (sprung.Count != _amStop1 || sprung.Select(s => s.Index).Distinct().Count() != _amStop1) { Fail($"{sprung.Count} AmbushSprung event(s) for stop 1, not {_amStop1}"); return; }
                foreach (var s in sprung)
                    if (Math.Abs(s.At - first.At - StopAmbushRules.SpringAfterStepOff) > AmWindow) { Fail($"point {s.Index} burst {s.At - first.At:0.00} s after the lead's Unseated, not 1.5 +- 0.2"); return; }
                if (AmStopSprungSince(_amMark).Count != _amStop1) { Fail($"{AmStopSprungSince(_amMark).Count} stop-wall bursts since the arrival, not {_amStop1}"); return; }
                if (logs.Count != _amStop1 || logs.Any(l => !(l.Text.Contains("bursts from the wall") || l.Text.Contains("bursts from the rubble")))) { Fail($"{logs.Count} burst log(s), not {_amStop1} of \"bursts from the wall\" or \"rubble\""); return; }
                if (ambushers.Count != _amStop1 || ambushers.Any(e => e.State.Unaware)) { Fail($"{ambushers.Count} stop ambusher(s) alive, hunting {ambushers.Count(e => !e.State.Unaware)}; want {_amStop1} hunting"); return; }
                if (sprung.Any(s => !spawner.Sprung.Contains(s.Index))) { Fail("a sprung point is not in the spawner's Sprung"); return; }
                // (c) the save and the load
                main.SaveGame(StSlot);
                _amMark = StNow;
                if (!main.LoadGame(StSlot)) { Fail("the save at stop 1 would not load"); return; }
                GD.Print("[stoptrap] (c) saved and loaded at stop 1");
                _stStep++; _stClock = 0f; _stSub = 0; _amToggles = 0; _amOuts = 0;
                break;
            }
            case 5: // (c) after the load: out and in twice, no burst
            {
                if (_stSub == 0)
                {
                    if (_stClock < 0.5f) break;
                    if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1) { Fail($"after the load the cart is {rc.State.Mode} at stop {rc.StopIndex}"); return; }
                    var stop1 = Enumerable.Range(_amFirst, spawner.Points.Count - _amFirst).Where(i => spawner.Points[i].Stop == 1).ToList();
                    GD.Print($"[stoptrap] (c) after the load: AmbushFirst {trap.AmbushFirst}, stop 1's points [{string.Join(",", stop1)}] sprung [{string.Join(",", stop1.Where(spawner.Sprung.Contains))}], armed stop {trap.AmbushArmedStop}, the lead {(me?.Seated == true ? "seated" : "on foot")}");
                    if (trap.AmbushFirst != _amFirst) { Fail($"after the load AddStopPoints returned {trap.AmbushFirst}, not {_amFirst}"); return; }
                    if (stop1.Any(i => !spawner.Sprung.Contains(i))) { Fail("stop 1's points are not sprung after the load"); return; }
                    if (trap.AmbushArmedStop != -1) { Fail($"the load armed stop {trap.AmbushArmedStop}"); return; }
                    _amMark = StNow;
                    _stSub = 1; _stClock = 0f;
                    break;
                }
                if (_stSub == 1)
                {
                    // a toggle every 1.5 s: get out when seated, board when on foot; done after two get-outs, seated again
                    if (_stClock >= 1f + 1.5f * _amToggles && _amToggles < 6 && !(_amOuts >= 2 && me?.Seated == true))
                    {
                        if (me?.Seated == true) { if (AmGetOut(main, rc) is { } why) { Fail($"(c) {why}"); return; } _amOuts++; }
                        else if (!RideBoard(main, layout, rc, out var why)) { Fail($"(c) {why}"); return; }
                        _amToggles++;
                    }
                    if (_stClock < 10f) break;
                    var outs = _amUnseated.Count(u => u.At >= _amMark && u.Held && u.Stop == 1 && u.Rider == me?.RiderId);
                    var burst = AmStopSprungSince(_amMark);
                    GD.Print($"[stoptrap] (c) 10 s after the load: the lead got out {outs} time(s) ({_amToggles} toggles), is {(me?.Seated == true ? "seated" : "on foot")}; {burst.Count} wall burst(s), armed stop {trap.AmbushArmedStop}");
                    if (outs < 2) { Fail($"the lead got out {outs} time(s) after the load, not twice"); return; }
                    if (me?.Seated != true) { Fail("the lead is not seated again"); return; }
                    if (burst.Count > 0 || trap.AmbushArmedStop != -1) { Fail($"after the load the walls burst {burst.Count} time(s) (armed stop {trap.AmbushArmedStop})"); return; }
                    // (d) stop 1's ambushers go, then ride on seated
                    var killed = 0;
                    foreach (var e in Enemies.Enemy.Live.ToList()) if (IsInstanceValid(e) && e.IsAlive && spawner.IsStopAmbusher(e)) { e.Kill(); killed++; }
                    GD.Print($"[stoptrap] (d) {killed} stop ambusher(s) die by the test's hand; ride on to stop 2 seated");
                    _stStep++; _stClock = 0f; _stSub = 0; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                }
                break;
            }
            case 6: // (d) ride 2
            {
                if (!RideOnTo(main, rc, 2, "stoptrap", Fail)) break;
                var arrived = _amArrived.LastOrDefault(a => a.Stop == 2);
                if (arrived.Stop != 2) { Fail("held at stop 2 with no Arrived heard"); return; }
                if (trap.AmbushArmedStop != 2) { Fail($"the arrival at stop 2 armed stop {trap.AmbushArmedStop}, not 2"); return; }
                if (me?.Seated != true) { Fail("the lead is not seated at stop 2"); return; }
                _amMark = arrived.At;
                GD.Print($"[stoptrap] (d) held at stop 2 {StNow - arrived.At:0.00} s after the Arrived, the lead seated");
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 7: // (d) nobody steps off: the walls at 6 s
            {
                if (StNow - _amMark < StopAmbushRules.SpringAfterArrival + 2.0) break;
                var heroOut = _amUnseated.Where(u => u.At >= _amMark && u.Held && u.Stop == 2 && u.Rider < 100).ToList();
                var sprung = AmSprungAt(spawner, 2, _amMark);
                var ambushers = Enemies.Enemy.Live.Where(e => IsInstanceValid(e) && e.IsAlive && spawner.IsStopAmbusher(e)).ToList();
                GD.Print($"[stoptrap] (d) stop 2: {heroOut.Count} hero get-out(s); {sprung.Count} point(s) sprung at [{string.Join(", ", sprung.Select(s => $"{s.Index}@+{s.At - _amMark:0.00}"))}] after the Arrived; ambushers [{string.Join(", ", ambushers.Select(e => $"{e.DisplayName}:{e.State.Name}"))}]");
                if (heroOut.Count > 0 && heroOut[0].At < (sprung.Count > 0 ? sprung[0].At : StNow)) { Fail($"rider {heroOut[0].Rider} got out at stop 2 before the burst"); return; }
                if (sprung.Count != _amStop2) { Fail($"{sprung.Count} stop-2 point(s) sprung, not {_amStop2}"); return; }
                foreach (var s in sprung)
                    if (Math.Abs(s.At - _amMark - StopAmbushRules.SpringAfterArrival) > AmWindow) { Fail($"point {s.Index} burst {s.At - _amMark:0.00} s after the Arrived, not 6 +- 0.2"); return; }
                if (ambushers.Count != _amStop2 || ambushers.Any(e => e.State.Unaware)) { Fail($"{ambushers.Count} stop ambusher(s) alive at stop 2 (hunting {ambushers.Count(e => !e.State.Unaware)}), not {_amStop2} hunting"); return; }
                StopTrapOk($"the placement ({_amStop1}/{_amStop2} at stops 1/2 after {_amFirst} corridor points); the restore arms nothing; 2 burst 1.5 s after the step-off; none after the load; 3 burst 6 s after the arrival with nobody off");
                break;
            }
        }
    }
}
