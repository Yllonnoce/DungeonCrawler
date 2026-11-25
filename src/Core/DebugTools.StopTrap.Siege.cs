using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-stop-trap=siege (plan 16 Task 3) -----------------------------------------------------------------------------------
// The stop siege, in order (any step out of its window fails):
//  (a) the load restart: rc.Restore(1), the party beside the cart, stop 1's cage opened (its crew are the warning's speakers on the
//      condemned branch): no wave for 10 s, then wave 1 at 10 +- 0.5 s with 1 portal x 2, SiegeFirstLog logged and the warning said
//      within 3 s of wave 1's first creature, once (the same at stop 2's first arrival, never again on a restart); every site of every
//      wave in its stop's room or within 3 cells of it, 6 m or more from every up hero, 4 m from the cage and every platform cell, never
//      a rail or pocket cell, and the waves' portals 8 m apart; PressureSeconds still while any siege runs.
//  (b) nobody fighting (the companions hold fire, the lead idle on the platform): wave 2 at +14 +- 0.5 s, wave 3 at +28, wave 4 waits
//      (alive 6/6); the alive count never over the stop's cap.
//  (c) rc.DebugKillHolders(1): the gate up within 1 s with siege creatures alive.
//  (d) the lead boards (RideOnTo) and the train departs: "siege at stop 1: off (the train left)", the siege creatures rooted where they
//      stand (DebugHoldSiegeCreatures) and kept alive through the ride (DebugHoldHp, DebugHazardProof: no cart runs them down), no wave
//      on ride 2, one straggler freed per rooted creature before the arrival at stop 2.
//  (e) stop 2's arrival: wave 1 at 4 +- 0.5 s with 2 portals x 2, wave 2 12 +- 0.5 s later.
//  (f) at 8/8, DebugClearAlive, Reinforcements 2 and RefreshSilence: the next gap 8 +- 0.5 s.
//  (f2) wave 4 hurried to 8/8, one creature killed (7/8): the next wave sends exactly 1 creature through 1 portal.
//  (g) a save and a load at stop 2: no wave for 10 s, then wave 1 with 2 portals and SiegeWaves 1.
//  (h) every hero to 0 HP: after the rise at stop 2 (Main.RoseAtCart), no wave for 15 s, then wave 1.
//  (i) Reinforcements 0 and RefreshSilence: "off (reinforcements are off in Settings)" within 2 s, no portal opens for 20 s.
// Finally each stop's warning was said once (stops 1 and 2: never again on a restart).
// Stops 3 and 4 are not ridden here; --test-perf=siege runs stop 4's real siege.
public partial class DebugTools
{
    private const string StSlot = "stoptraptest";
    private const float StWindow = 0.5f;

    private sealed record StWave(double At, int Stop, int Wave, List<GridPos> Sites, List<int> Counts, int Alive, int Cap, string? Log);

    private bool _stListening;
    private readonly List<StWave> _stWaves = new();
    private readonly List<(double At, int Stop, int Wave, string Why)> _stWaits = new();
    private readonly List<(double At, int Stop, float Delay, bool Rose)> _stStarts = new();
    private readonly List<(double At, int Stop, string Why, int Waves)> _stEnds = new();
    private readonly List<(double At, string Name)> _stFreed = new();
    private readonly List<(double At, int Stop, string Speaker, string Text)> _stWarnings = new();
    private readonly List<double> _stPortalsOpened = new();
    private readonly List<string> _stSiteFaults = new();
    private readonly Dictionary<int, double> _stArrivedAt = new();
    private string? _stLastLog;
    private RailController? _stRc;
    private PortalSpawner? _stPressureOf;
    private float _stPressure;
    private int _stMaxOver;
    private string? _stOverWhy;
    private double _stMark;
    private int _stMarkWaves, _stRooted;
    /// <summary>The step's own sub-step (back to 0 at every step).</summary>
    private int _stSub;
    private bool _stDeparted;
    /// <summary>The siege creatures rooted at the departure (Ruling 24), by node name, and those of them that died meanwhile (a cart ran them over).</summary>
    private readonly HashSet<string> _stRootedNames = new(), _stRootedDied = new();
    private int _stFreedOnRide;
    private StWave? _stW1;

    private void StopTrapListen(Main main)
    {
        if (_stListening) return;
        _stListening = true;
        Action<int, float, int> started = (stop, delay, humans) =>
        {
            _stStarts.Add((StNow, stop, delay, Main.Instance?.RoseAtCart == true));
            GD.Print($"[stoptrap] heard: siege at stop {stop} on, first wave in {delay:0.0} s, {humans} human(s), rose at the cart {Main.Instance?.RoseAtCart == true}");
        };
        Action<int, int, IReadOnlyList<GridPos>, IReadOnlyList<int>, int, int> opened = (stop, wave, sites, counts, alive, cap) =>
        {
            var w = new StWave(StNow, stop, wave, sites.ToList(), counts.ToList(), alive, cap, _stLastLog);
            _stWaves.Add(w);
            StopTrapJudgeSites(w);
        };
        Action<int, int, string> waited = (stop, wave, why) => _stWaits.Add((StNow, stop, wave, why));
        Action<int, string, int> ended = (stop, why, waves) => _stEnds.Add((StNow, stop, why, waves));
        Action<Enemies.Enemy, float, float> freed = (e, _, _) => _stFreed.Add((StNow, e.Name));
        Action<Npc, Line> said = (npc, line) =>
        {
            if (line.Text is not (StopTrapLines.GriselSiegeWarning or StopTrapLines.CrewSiegeWarning)) return;
            var stop = PortalSpawner.Instance?.SiegeStop ?? -1;
            _stWarnings.Add((StNow, stop, npc.NpcName, line.Text));
            GD.Print($"[stoptrap] heard: {npc.NpcName} warns at stop {stop}: \"{line.Text}\" (voice {line.VoiceId ?? "none"})");
        };
        PortalSpawner.DebugSiegeStarted += started;
        PortalSpawner.DebugWaveOpened += opened;
        PortalSpawner.DebugWaveWaited += waited;
        PortalSpawner.DebugSiegeEnded += ended;
        PortalSpawner.DebugStragglerFreed += freed;
        Npc.Said += said;
        _staticUnhooks.Add(() =>
        {
            PortalSpawner.DebugSiegeStarted -= started;
            PortalSpawner.DebugWaveOpened -= opened;
            PortalSpawner.DebugWaveWaited -= waited;
            PortalSpawner.DebugSiegeEnded -= ended;
            PortalSpawner.DebugStragglerFreed -= freed;
            Npc.Said -= said;
        });
        Action<string> message = text => _stLastLog = text;
        Action<Vector3> portal = _ => _stPortalsOpened.Add(StNow);
        Action<Enemies.Enemy> died = e => { if (_stRootedNames.Contains(e.Name)) _stRootedDied.Add(e.Name); };
        var bus = EventBus.Instance;
        bus.Message += message;
        bus.PortalOpened += portal;
        bus.EnemyDied += died;
        _staticUnhooks.Add(() => { bus.Message -= message; bus.PortalOpened -= portal; bus.EnemyDied -= died; });
    }

    /// <summary>Each siege creature through (physics time, stop, wave), heard on each floor's spawner.</summary>
    private readonly List<(double At, int Stop, int Wave)> _stSpawns = new();
    private PortalSpawner? _stSpawnsOf;

    /// <summary>Each frame: the current spawner's siege creatures are heard (a load or a rise builds a new one).</summary>
    private void StopTrapWatchSpawns(PortalSpawner portals)
    {
        if (portals == _stSpawnsOf) return;
        _stSpawnsOf = portals;
        portals.SiegeSpawned += (_, stop, wave) => _stSpawns.Add((StNow, stop, wave));
    }

    /// <summary>
    /// The stop's warning came with its first siege (said within 3 s of wave 1's first creature, opened after <paramref name="since"/>) and once
    /// only; null when it did, else why not.
    /// </summary>
    private string? StopTrapWarnedAtWave1(int stop, double since)
    {
        var first = _stSpawns.FirstOrDefault(x => x.Stop == stop && x.Wave == 1 && x.At >= since);
        if (first.Wave == 0) return $"no creature of stop {stop}'s wave 1";
        var warned = _stWarnings.Where(x => x.Stop == stop).ToList();
        if (warned.Count != 1) return $"stop {stop}'s warning was said {warned.Count} time(s), not once";
        var after = warned[0].At - first.At;
        GD.Print($"[stoptrap] stop {stop}'s warning: {warned[0].Speaker} {after:0.00} s after wave 1's first creature");
        return after is < -0.01 or > 3.0 ? $"stop {stop}'s warning came {after:0.00} s after wave 1's first creature, not within 3 s" : null;
    }

    /// <summary>Each frame: the current line's Arrived events are heard (a load or a rise builds a new one).</summary>
    private void StopTrapWatchRails(RailController rc)
    {
        if (rc == _stRc) return;
        _stRc = rc;
        rc.CartEvents += e => { if (e.Kind == CartEventKind.Arrived) { _stArrivedAt[e.Stop] = StNow; GD.Print($"[stoptrap] heard: arrived at stop {e.Stop}"); } };
    }

    /// <summary>Ruling 1's site rules, judged from the wave's own moment: the room or 3 cells round it, the clearances, the spread, no line or pocket.</summary>
    private void StopTrapJudgeSites(StWave w)
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { Rail: { } rail } layout || w.Stop < 1 || w.Stop >= rail.Stops.Count) return;
        var stop = rail.Stops[w.Stop];
        var room = layout.Rooms[stop.Room];
        var heroes = StopTrapHeroCells(main);
        GridPos? cage = ObjectiveController.Instance is CrewRescueController crew ? crew.Cages.FirstOrDefault(c => c.Stop == w.Stop)?.Cell : null;
        foreach (var s in w.Sites)
        {
            var near = s.X >= room.X - StopSiegeRules.Around && s.X <= room.Right + StopSiegeRules.Around && s.Y >= room.Y - StopSiegeRules.Around && s.Y <= room.Bottom + StopSiegeRules.Around;
            if (!near) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {s} is not in room {stop.Room} or within 3 cells of it");
            foreach (var h in heroes) if (StopTrapMetres(s, h) < StopSiegeRules.HeroClear) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {s} is {StopTrapMetres(s, h):0.0} m from a hero at {h}");
            if (cage is { } k && StopTrapMetres(s, k) < StopSiegeRules.CageClear) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {s} is {StopTrapMetres(s, k):0.0} m from the cage at {k}");
            foreach (var p in stop.Platform) if (StopTrapMetres(s, p) < StopSiegeRules.PlatformClear) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {s} is {StopTrapMetres(s, p):0.0} m from the platform cell {p}");
            if (layout.IsRail(s) || layout.IsRailPocket(s)) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {s} is a rail or pocket cell");
        }
        for (var i = 0; i < w.Sites.Count; i++)
            for (var j = i + 1; j < w.Sites.Count; j++)
                if (StopTrapMetres(w.Sites[i], w.Sites[j]) < StopSiegeRules.Spread) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {w.Sites[i]} and {w.Sites[j]} are {StopTrapMetres(w.Sites[i], w.Sites[j]):0.0} m apart");
        var log = w.Wave == 1 ? StopTrapLines.SiegeFirstLog : StopTrapLines.SiegeLaterLog;
        if (w.Log != log) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: the log said \"{w.Log}\", not \"{log}\"");
        if (w.Alive + w.Counts.Sum() > w.Cap) _stSiteFaults.Add($"stop {w.Stop} wave {w.Wave}: {w.Alive} alive + {w.Counts.Sum()} sent is over the cap {w.Cap}");
        GD.Print($"[stoptrap] wave seen: stop {w.Stop} wave {w.Wave} {w.Sites.Count} portal(s) x [{string.Join(",", w.Counts)}] at [{string.Join(" ", w.Sites)}] alive {w.Alive}/{w.Cap}; heroes at [{string.Join(" ", heroes)}], cage {cage?.ToString() ?? "none"}");
    }

    /// <summary>The waves opened at <paramref name="stop"/> since <paramref name="since"/>.</summary>
    private List<StWave> StWavesSince(int stop, double since) => _stWaves.Where(w => w.Stop == stop && w.At >= since).ToList();

    private void RunStopTrapSiege(Main main, FloorLayout layout, Node3D root, RailController rc, PortalSpawner portals)
    {
        var party = main.Party;
        void Fail(string why) => StopTrapFail(why);
        StopTrapWatchRails(rc);
        StopTrapWatchSpawns(portals);
        foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true; // nobody of the party fights (a load keeps the bodies; set again each frame)
        if (_stSiteFaults.Count > 0) { Fail(string.Join("; ", _stSiteFaults.Take(4))); return; }

        // the floor-wide pressure stands still while any siege runs (a new spawner after a load or a rise starts from its own value)
        if (portals != _stPressureOf || portals.SiegeStop < 1) { _stPressureOf = portals; _stPressure = portals.PressureSeconds; }
        else if (Mathf.Abs(portals.PressureSeconds - _stPressure) > 1e-4f) { Fail($"PressureSeconds moved from {_stPressure:0.00} to {portals.PressureSeconds:0.00} during the siege at stop {portals.SiegeStop}"); return; }
        // the hard cap: never more alive than the siege's cap while it runs
        if (portals.SiegeStop >= 1)
        {
            var cap = StopSiegeRules.AliveCap(StopSiegeRules.For(portals.SiegeStop), 1);
            if (portals.Alive > cap && _stOverWhy == null) _stOverWhy = $"{portals.Alive} alive at stop {portals.SiegeStop}, over its cap {cap}";
            _stMaxOver = Math.Max(_stMaxOver, portals.Alive);
        }
        if (_stOverWhy != null) { Fail(_stOverWhy); return; }

        switch (_stStep)
        {
            case 0 when _stClock >= 1.5f:
            {
                if (rc.StopS.Length != Floor12Stops) { Fail($"floor 12's line has {rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                if (StopTrapController.Instance is not { } trap || trap.GetParent() != root) { Fail("floor 12 has no StopTrapController"); return; }
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("floor 12 has no CrewRescueController"); return; }
                if (portals.SiegeStop != -1) { Fail($"a siege runs at stop {portals.SiegeStop} before any arrival"); return; }
                StopTrapListen(main);
                _stReinforcementsWas = Settings.Reinforcements;
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.Stance = Characters.Stance.Hold;
                _stMark = StNow;
                rc.Restore(1);
                if (PerfBeside(main, layout, rc.Cart.GlobalPosition) is not { } stand) { Fail("no walkable cell beside the cart at stop 1"); return; }
                party.Teleport(stand + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                var opened = ctrl.DebugOpen(1); // the condemned branch has no Grisel: stop 1's freed crew are the warning's speakers
                var start = _stStarts.LastOrDefault();
                GD.Print($"[stoptrap] (a) rc.Restore(1); the party beside the cart at {DungeonBuilder.WorldToCell(stand)}; stop 1's cage opened {opened}; StopS=[{string.Join(", ", rc.StopS.Select(x => x.ToString("0.0")))}]; pressure {portals.PressureSeconds:0.0} s; lethality {TierRules.Lethality}");
                if (_stStarts.Count != 1 || start.Stop != 1 || Mathf.Abs(start.Delay - StopSiegeRules.LoadGrace) > 1e-3f || start.Rose) { Fail($"the restore did not start stop 1's siege with the load's grace ({string.Join("; ", _stStarts)})"); return; }
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 1: // (a) the load restart's first wave
            {
                var waves = StWavesSince(1, _stMark);
                if (waves.Count == 0) { if (_stClock > 15f) Fail("no wave at stop 1 within 15 s of the restore"); break; }
                var w = waves[0];
                var after = w.At - _stMark;
                GD.Print($"[stoptrap] (a) wave 1 at stop 1 {after:0.00} s after the restore: {w.Sites.Count} portal(s) x [{string.Join(",", w.Counts)}] log \"{w.Log}\"");
                if (Math.Abs(after - StopSiegeRules.LoadGrace) > StWindow) { Fail($"wave 1 opened {after:0.00} s after the restore, not 10 +- 0.5"); return; }
                if (w.Wave != 1 || w.Sites.Count != 1 || !w.Counts.SequenceEqual(new[] { 2 })) { Fail($"wave {w.Wave} at stop 1: {w.Sites.Count} portal(s) x [{string.Join(",", w.Counts)}], not 1 x 2"); return; }
                _stW1 = w;
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 2: // (b) gaps and cap
            {
                var w1 = _stW1!;
                var waves = StWavesSince(1, w1.At);
                var wait4 = _stWaits.FirstOrDefault(x => x.Stop == 1 && x.Wave == 4 && x.At > w1.At);
                if (wait4.Why == null) { if (_stClock > 50f) Fail($"stop 1's wave 4 never waited within 50 s of wave 1 (waves [{string.Join(", ", waves.Select(x => $"{x.Wave}@{x.At - w1.At:0.0}"))}], alive {portals.Alive})"); break; }
                if (waves.Count != 3) { Fail($"{waves.Count} waves at stop 1 by wave 4's wait, not 3 (alive {portals.Alive})"); return; }
                var g2 = waves[1].At - w1.At;
                var g3 = waves[2].At - w1.At;
                GD.Print($"[stoptrap] (b) wave 2 at +{g2:0.00} s, wave 3 at +{g3:0.00} s, wave 4 waits ({wait4.Why}) at +{wait4.At - w1.At:0.00} s; most alive {_stMaxOver}; the warning said {_stWarnings.Count(x => x.Stop == 1)} time(s) at stop 1");
                if (Math.Abs(g2 - 14.0) > StWindow) { Fail($"wave 2 at +{g2:0.00} s, not 14 +- 0.5"); return; }
                if (Math.Abs(g3 - 28.0) > StWindow) { Fail($"wave 3 at +{g3:0.00} s, not 28 +- 0.5"); return; }
                if (wait4.Why != "alive 6/6") { Fail($"wave 4 waits ({wait4.Why}), not (alive 6/6)"); return; }
                if (StopTrapWarnedAtWave1(1, w1.At) is { } unwarned) { Fail($"(a) {unwarned}"); return; }
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 3 when _stClock >= 0.5f: // (c) the gate
            {
                if (_stSub == 0)
                {
                    var killed = rc.DebugKillHolders(1);
                    _stMark = StNow;
                    _stSub = 1;
                    GD.Print($"[stoptrap] (c) {killed} holder(s) of stop 1 die by the test's hand; {portals.DebugSiegeCounts().Current} siege creature(s) alive");
                    if (killed == 0) { Fail("stop 1 had no holders to kill"); return; }
                    break;
                }
                if (!rc.GateUp(1)) { if (StNow - _stMark > 1.0) Fail("stop 1's gate did not lift within 1 s of its holders dying"); break; }
                var alive = portals.DebugSiegeCounts().Current;
                GD.Print($"[stoptrap] (c) stop 1's gate up {StNow - _stMark:0.00} s after its holders died, with {alive} siege creature(s) alive");
                if (alive == 0) { Fail("no siege creature alive when the gate lifted"); return; }
                _stStep++; _stClock = 0f; _stSub = 0; _rideOnStop = -1; _rideOnPhase = 0; _rideOnClock = 0f;
                break;
            }
            case 4: // (d) the departure, the stragglers, ride 2
            {
                if (!_stDeparted && rc.State.Mode == CartMode.Rolling)
                {
                    _stDeparted = true;
                    _stMark = StNow;
                    var end = _stEnds.LastOrDefault();
                    portals.DebugHoldSiegeCreatures(true);
                    _stRooted = portals.DebugSiegeCounts().Stragglers;
                    foreach (var e in Enemies.Enemy.Live)
                        if (IsInstanceValid(e) && e.IsAlive && portals.IsSiegeCreature(e))
                        {
                            _stRootedNames.Add(e.Name);
                            e.DebugHoldHp = true; // Ruling 24: they live through the ride (no blow, no cart, no slag cart kills them) to be freed
                            e.DebugHazardProof = true;
                        }
                    _stMarkWaves = _stWaves.Count;
                    GD.Print($"[stoptrap] (d) departed stop 1: siege end heard \"{end.Why}\" at stop {end.Stop} after {end.Waves} wave(s); {_stRooted} straggler(s) rooted where they stand");
                    if (end.Stop != 1 || end.Why != "the train left" || portals.SiegeStop != -1) { Fail($"the departure did not end stop 1's siege with \"the train left\" (last end: stop {end.Stop} \"{end.Why}\"; siege stop {portals.SiegeStop})"); return; }
                    if (_stRooted == 0) { Fail("no siege creature left alive to straggle"); return; }
                }
                if (_stDeparted && _stWaves.Count > _stMarkWaves && rc.State.Mode == CartMode.Rolling) { Fail($"a wave opened on ride 2 (stop {_stWaves[^1].Stop} wave {_stWaves[^1].Wave})"); return; }
                if (!RideOnTo(main, rc, 2, "stoptrap", Fail)) break;
                if (!_stArrivedAt.TryGetValue(2, out var arrived)) { Fail("held at stop 2 with no Arrived heard"); return; }
                // Ruling 24: one freed line per rooted creature (they are kept alive through the ride: none dies on the way)
                var freedLines = _stFreed.Where(f => f.At >= _stMark && f.At <= arrived).Select(f => f.Name).ToList();
                var freedNames = freedLines.ToHashSet();
                var notFreed = _stRootedNames.Where(n => !freedNames.Contains(n)).ToList();
                _stFreedOnRide = freedLines.Count;
                GD.Print($"[stoptrap] (d) arrived at stop 2 {arrived - _stMark:0.0} s after the departure: of {_stRooted} rooted, {freedLines.Count} freed as stragglers [{string.Join(",", freedLines)}], died on the way [{string.Join(",", _stRootedDied)}]; {portals.DebugSiegeCounts().Stragglers} left");
                if (_stRootedNames.Count != _stRooted) { Fail($"{_stRootedNames.Count} rooted creatures named, {_stRooted} counted"); return; }
                if (_stRootedDied.Count > 0) { Fail($"rooted creatures [{string.Join(",", _stRootedDied)}] died on the way instead of straggling"); return; }
                if (notFreed.Count > 0 || freedLines.Count != _stRooted || freedNames.Any(n => !_stRootedNames.Contains(n))) { Fail($"{freedLines.Count} straggler line(s) [{string.Join(",", freedLines)}] before the arrival at stop 2, not one for each of the {_stRooted} rooted [{string.Join(",", _stRootedNames)}]"); return; }
                _stMark = arrived;
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 5: // (e) the real arrival
            {
                var waves = StWavesSince(2, _stMark);
                if (waves.Count < 2) { if (_stClock > 25f) Fail($"{waves.Count} wave(s) at stop 2 within 25 s of the arrival, not 2"); break; }
                var (w1, w2) = (waves[0], waves[1]);
                GD.Print($"[stoptrap] (e) stop 2: wave 1 {w1.At - _stMark:0.00} s after the arrival, {w1.Sites.Count} portal(s) x [{string.Join(",", w1.Counts)}] at [{string.Join(" ", w1.Sites)}]; wave 2 {w2.At - w1.At:0.00} s later");
                if (Math.Abs(w1.At - _stMark - 4.0) > StWindow) { Fail($"stop 2's wave 1 opened {w1.At - _stMark:0.00} s after the arrival, not 4 +- 0.5"); return; }
                if (w1.Sites.Count != 2 || !w1.Counts.SequenceEqual(new[] { 2, 2 })) { Fail($"stop 2's wave 1: {w1.Sites.Count} portal(s) x [{string.Join(",", w1.Counts)}], not 2 x 2"); return; }
                if (StopTrapMetres(w1.Sites[0], w1.Sites[1]) < StopSiegeRules.Spread) { Fail("stop 2's wave 1's portals are under 8 m apart"); return; }
                if (Math.Abs(w2.At - w1.At - 12.0) > StWindow) { Fail($"stop 2's wave 2 came {w2.At - w1.At:0.00} s after wave 1, not 12 +- 0.5"); return; }
                if (StopTrapWarnedAtWave1(2, w1.At) is { } unwarned) { Fail($"(e) {unwarned}"); return; }
                _stMark = w2.At;
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 6: // (f) heavy
            {
                if (_stSub == 0 && portals.Alive < 8) { if (_stClock > 4f) Fail($"stop 2 stands at {portals.Alive}/8 after two waves, not 8/8"); break; }
                if (_stSub == 0)
                {
                    portals.DebugClearAlive();
                    Settings.Reinforcements = 2f;
                    portals.RefreshSilence();
                    _stSub = 1;
                    GD.Print($"[stoptrap] (f) stop 2 at 8/8: every siege creature cleared, Reinforcements 2 (intensity {portals.Intensity:0.0})");
                    break;
                }
                var w3 = StWavesSince(2, _stMark + 0.01).FirstOrDefault();
                if (w3 == null) { if (_stClock > 12f) Fail("no wave at stop 2 within 12 s of heavy"); break; }
                GD.Print($"[stoptrap] (f) heavy: wave {w3.Wave} came {w3.At - _stMark:0.00} s after wave 2");
                if (Math.Abs(w3.At - _stMark - 8.0) > StWindow) { Fail($"the heavy gap was {w3.At - _stMark:0.00} s, not 8 +- 0.5"); return; }
                _stMark = w3.At;
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 7: // (f2) the hard cap
            {
                var counts = portals.DebugSiegeCounts();
                switch (_stSub)
                {
                    case 0: // wave 3's four through: hurry wave 4
                        if (counts.Current < 4) { if (_stClock > 4f) Fail($"wave 3 brought {counts.Current} of 4"); break; }
                        portals.DebugSiegeNow();
                        _stSub = 1; _stClock = 0f;
                        break;
                    case 1: // 8/8: one dies
                    {
                        if (counts.Current < 8 || portals.Alive < 8) { if (_stClock > 5f) Fail($"wave 4 left stop 2 at {portals.Alive}/8, not 8/8"); break; }
                        var victim = Enemies.Enemy.Live.FirstOrDefault(e => IsInstanceValid(e) && e.IsAlive && portals.IsSiegeCreature(e));
                        if (victim == null) { Fail("no siege creature to kill"); return; }
                        victim.Kill();
                        _stSub = 2; _stClock = 0f;
                        break;
                    }
                    case 2 when _stClock >= 0.2f: // 7/8: hurry the next
                        if (portals.Alive != 7) { Fail($"{portals.Alive}/8 alive after one died, not 7/8"); return; }
                        _stMark = StNow;
                        portals.DebugSiegeNow();
                        _stSub = 3; _stClock = 0f;
                        break;
                    case 3:
                    {
                        var w = StWavesSince(2, _stMark).FirstOrDefault();
                        if (w == null) { if (_stClock > 3f) Fail("no wave at 7/8 within 3 s of the hurry"); break; }
                        GD.Print($"[stoptrap] (f2) at {w.Alive}/{w.Cap}: wave {w.Wave} sent {w.Sites.Count} portal(s) x [{string.Join(",", w.Counts)}]");
                        if (w.Alive != 7 || w.Sites.Count != 1 || !w.Counts.SequenceEqual(new[] { 1 })) { Fail($"at {w.Alive}/{w.Cap} the wave sent {w.Sites.Count} portal(s) x [{string.Join(",", w.Counts)}], not exactly 1 through 1"); return; }
                        _stSub = 4; _stClock = 0f;
                        break;
                    }
                    case 4 when _stClock >= 2f: // (g) the save and the load
                    {
                        if (portals.Alive != 8) { Fail($"{portals.Alive}/8 after the hard cap's wave"); return; }
                        Settings.Reinforcements = _stReinforcementsWas ?? 1f;
                        main.SaveGame(StSlot);
                        _stMark = StNow;
                        var starts = _stStarts.Count;
                        if (!main.LoadGame(StSlot)) { Fail("the save at stop 2 would not load"); return; }
                        GD.Print($"[stoptrap] (g) saved and loaded at stop 2 (Reinforcements back to {Settings.Reinforcements:0.0}); siege starts heard {_stStarts.Count - starts}");
                        _stStep++; _stClock = 0f; _stSub = 0;
                        break;
                    }
                }
                break;
            }
            case 8: // (g) after the load
            {
                if (_stClock < 0.2f) break;
                var start = _stStarts.LastOrDefault();
                if (start.At < _stMark || start.Stop != 2 || Mathf.Abs(start.Delay - StopSiegeRules.LoadGrace) > 1e-3f || start.Rose) { Fail($"the load did not restart stop 2's siege with the load's grace (last start: stop {start.Stop}, {start.Delay:0.0} s, rose {start.Rose})"); return; }
                var w = StWavesSince(2, start.At).FirstOrDefault();
                if (w == null) { if (_stClock > 15f) Fail("no wave at stop 2 within 15 s of the load"); break; }
                GD.Print($"[stoptrap] (g) after the load: wave {w.Wave} {w.At - start.At:0.00} s after the restart, {w.Sites.Count} portal(s) x [{string.Join(",", w.Counts)}]; SiegeWaves {portals.SiegeWaves}");
                if (Math.Abs(w.At - start.At - StopSiegeRules.LoadGrace) > StWindow) { Fail($"the load's wave 1 came {w.At - start.At:0.00} s after the restart, not 10 +- 0.5"); return; }
                if (w.Wave != 1 || w.Sites.Count != 2 || portals.SiegeWaves != 1) { Fail($"the load's first wave is wave {w.Wave} with {w.Sites.Count} portal(s), SiegeWaves {portals.SiegeWaves} (want wave 1, 2 portals, 1)"); return; }
                _stMark = StNow;
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 9 when _stClock >= 2f: // (h) the wipe
            {
                var killer = Enemies.Enemy.Live.FirstOrDefault(e => IsInstanceValid(e) && e.IsAlive && portals.IsSiegeCreature(e));
                if (killer == null) { Fail("no siege creature to wipe the party with"); return; }
                _stNoTopUp = true;
                _stMark = StNow;
                foreach (var a in party.Active)
                    for (var i = 0; i < 12 && !a.Downed; i++) a.ReceiveAttack(killer.Stats, new Combat.AttackProfile { MinDamage = 9999, MaxDamage = 9999 }, killer.DisplayName, killer);
                GD.Print($"[stoptrap] (h) {killer.DisplayName} strikes every hero down: {party.Active.Count(a => a.Downed)} of {party.Active.Count()} down");
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 10: // (h) the rise
            {
                var start = _stStarts.LastOrDefault();
                if (start.At < _stMark) { if (_stClock > 15f) Fail($"no siege restart within 15 s of the wipe (RoseAtCart {main.RoseAtCart})"); break; }
                if (_stNoTopUp) { _stNoTopUp = false; main.Hud?.CloseCard(); }
                if (start.Stop != 2 || !start.Rose || Mathf.Abs(start.Delay - StopSiegeRules.RiseGrace) > 1e-3f) { Fail($"the rise restarted stop {start.Stop}'s siege with {start.Delay:0.0} s (rose {start.Rose}), not stop 2's with 15 s after a rise"); return; }
                var w = StWavesSince(2, start.At).FirstOrDefault();
                if (w == null) { if (StNow - start.At > 20.0) Fail("no wave within 20 s of the rise"); break; }
                GD.Print($"[stoptrap] (h) the rise at stop {rc.StopIndex} (RoseAtCart {main.RoseAtCart}): wave {w.Wave} {w.At - start.At:0.00} s after the restart, {w.Sites.Count} portal(s)");
                if (w.Wave != 1 || Math.Abs(w.At - start.At - StopSiegeRules.RiseGrace) > StWindow) { Fail($"after the rise wave {w.Wave} came {w.At - start.At:0.00} s after the restart, not wave 1 at 15 +- 0.5"); return; }
                _stStep++; _stClock = 0f; _stSub = 0;
                break;
            }
            case 11 when _stClock >= 1f: // (i) off
            {
                if (_stSub == 0)
                {
                    Settings.Reinforcements = 0f;
                    portals.RefreshSilence();
                    _stMark = StNow;
                    _stSub = 1;
                    GD.Print("[stoptrap] (i) Reinforcements 0");
                    break;
                }
                var end = _stEnds.LastOrDefault();
                if (end.At < _stMark)
                {
                    if (StNow - _stMark > 2.05) Fail($"stop 2's siege was not off within 2 s of Reinforcements 0 (siege stop {portals.SiegeStop})");
                    break;
                }
                if (end.Stop != 2 || end.Why != "reinforcements are off in Settings") { Fail($"the siege ended at stop {end.Stop} with \"{end.Why}\""); return; }
                if (StNow - _stMark < 20.0) break;
                var opened = _stPortalsOpened.Count(t => t > end.At);
                GD.Print($"[stoptrap] (i) off {end.At - _stMark:0.00} s after Reinforcements 0 (\"{end.Why}\"); {opened} portal(s) opened in the 20 s after");
                if (opened > 0) { Fail($"{opened} portal(s) opened with reinforcements off"); return; }
                var perStop = _stWarnings.GroupBy(x => x.Stop).ToDictionary(g => g.Key, g => g.Count());
                GD.Print($"[stoptrap] warnings: [{string.Join(", ", _stWarnings.Select(x => $"stop {x.Stop} {x.Speaker}"))}]; heroes stood up by the top-up {_stRevived}");
                if (perStop.GetValueOrDefault(1) != 1 || perStop.GetValueOrDefault(2) != 1 || perStop.Keys.Any(k => k is not (1 or 2))) { Fail($"the warnings per stop: [{string.Join(", ", perStop.Select(kv => $"{kv.Key}: {kv.Value}"))}], not once at stops 1 and 2"); return; }
                StopTrapOk($"stop 1's restart, gaps and cap; the gate; the departure and {_stFreedOnRide} of {_stRooted} rooted freed as stragglers; stop 2's arrival, heavy, the hard cap, the load, the rise and the silence");
                break;
            }
        }
    }
}
