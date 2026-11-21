using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-switch-back[=wrong|stall|lone|fixedafter] ----------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo. Every enemy is culled as it appears. The build: ride 3 has the broken bridge,
// its gap on the slag line (a chasm, no footing: a ray down finds nothing) between deck cells (a ray down finds their footing), the enemy
// track's rails and sleepers cut bare over the gap and no timber frame over it, the party lane beside it and at r3_buckets solid floor
// with its rails whole. A, traffic, with the cart held at stop 2 and nobody aboard (rc.Restore(2)): A1 with the party far off (stop 0)
// and A2 with it on its own lane 5 cells short of the gap, a traffic cart is started 30 m short of the gap with a rider aboard; within 8
// s it drops through and is gone, its rider dead and hidden below the gap's lip, quietly: no "The slag cart derails!" and no derail
// taunt, and "The slag cart plunges through the broken bridge." only in A2 (a hero within 40 m of the gap). A3: the cart restored at stop
// 4 (past the gap) and the traffic clock run out, the next traffic cart starts just past the gap's far edge. B: the cart restored at stop
// 2, everyone boards and the cart departs; the seated lead throws r3_switch_out from the seat (the seated window, through the dispatch E
// uses); the cart crosses with its return open (r3_switch_back is live: deadly, not fixed). The early call ("switch back", RailCalls) comes
// with the front 25-30 m before the back's lever, and until a human's press the lever stays straight with nobody's hand on it (no
// companion moves it). As the front reaches the lever's S a layer-1 ray from the seated lead's eye to the broken bridge's red lamp is
// clear, and one to the back's own red signal lamp. The thrown branch (the default): the seated window offers the back ("Throw the switch") and the lead presses it through the
// same dispatch; at its points the cart takes it (back from its points), is home on its own lane (blend <= 0.01) by the points + 8.1 m,
// never falls and arrives at stop 3. The wrong branch (=wrong): nobody presses; the last call ("now") comes 5-6.5 m before the points,
// the cart passes the back straight and stays across; the train falls with its front within 0.6 m of the gap and "The slag line ends at
// the broken bridge!" logged; every rider is fallen within 1 s; the wipe follows and rises at stop 2, every hero up within 10 m of a whole
// cart. After every step nobody fell but the train's riders (from its fall to the rise). With --screenshot=PATH (windowed) the view from
// the seat on the slag line is saved as the front reaches the back's lever (the thrown branch: PATH; the wrong branch: PATH_lever), and
// in the wrong branch as the front comes within 12 m of the gap (PATH) and 4 m (PATH_near). On both wrong branches the seated window
// must have offered the back to the lead before its points (nobody pressed). =stall is the wrong branch with the cart caught
// (RailController.DebugStall) four times: 25 m before the lever (short of the window), where the watchdog sets it at the window's start
// (the offer and the calls to come); 8 m before the lever (inside the window, short of the last call), where it sets it at the last
// call's mark, 6 m before the points, and the last call is heard after it (final review M1); 3 m short of the back's points, where it
// sets it at the points (the back taken as it stands: straight); and 5 m past them, where it grinds it into the gap, where it falls. It
// never carries it across. =lone (ruling R-lone): nobody presses, and the lead, the only human, is downed in the seat just before the
// window (held down until the back is taken; possession off, so no companion becomes the human); at the points the cart takes the safe
// line with the lever straight ("No one could reach the switch — the cart takes the safe line."), comes home and arrives at stop 3 with
// no fall. =fixedafter (Ruling 20): a fixed switch back r3_switch_back2 is added 8 m before the gap
// (RailEncounterController.DebugFixedBackBeforeGap), the fall still armed; nobody presses the live back and the cart passes it straight;
// with the front 3 m past its points the cart is caught (RailController.DebugStall), and the watchdog sets its front at r3_switch_back2's
// points, never past them; the fixed back takes it home there (back from its points), it is on its own lane (blend <= 0.01) with the front
// 1 m short of the gap, never falls and arrives at stop 3. Prints [switchback] lines and a RESULT.
public partial class DebugTools
{
    private bool _testSwitchBack;
    private string _sbVariant = "thrown";
    /// <summary>The front's S at the back's early and last calls (NaN until heard), and what the sight ray from the lever found.</summary>
    private float _sbEarlyFront = float.NaN, _sbLastFront = float.NaN;
    private string? _sbSight;
    private bool _sbBackPressed, _sbBackTaken, _sbLeverShot, _sbHome, _sbWindowSeen;
    private float _sbPressAhead;
    private int _sbFallsBefore;
    /// <summary>=stall: the cart caught so far (0, 1 short of the back's points, 2 past them), and the watchdog's grinds as the train fell.</summary>
    private int _sbStalls, _sbGroundAtWreck = -1, _sbGroundBefore;
    private bool WrongBranch => _sbVariant is "wrong" or "stall";
    private bool LoneBranch => _sbVariant == "lone";
    /// <summary>=fixedafter: the live back left straight, a fixed one past it (<see cref="RailEncounterController.DebugFixedBackBeforeGap"/>) takes the cart home.</summary>
    private bool FixedAfterBranch => _sbVariant == "fixedafter";
    /// <summary>=fixedafter: the cart caught past the live back, the watchdog's grind seen, and the front 1 m short of the gap checked home.</summary>
    private bool _sbFaStalled, _sbFaGround, _sbFaNearGap;
    /// <summary>=lone: the lead downed in the seat, and the safe-line line heard.</summary>
    private bool _sbLoneDowned, _sbLoneLogged;
    private int _sbStep;
    private float _sbClock;
    private bool _sbListening, _sbFallLogged, _sbWiped;
    private int _sbShots;
    private RailController? _sbHooked, _sbWipeRc;
    private SlagCart? _sbTraffic;
    /// <summary>A's rider on the traffic cart: lost with it at the gap.</summary>
    private Enemies.Enemy? _sbRider;
    private int _sbDropsBefore;
    /// <summary>The gap's middle on its floor (the plunge's 40 m are measured from it).</summary>
    private Vector3 _sbGapAt;
    /// <summary>Heard since the flow began: the derail line, a derail taunt, the plunge line; and their counts before A's drop.</summary>
    private int _sbDerailLogs, _sbDerailTaunts, _sbPlungeLogs, _sbDerailsBefore, _sbTauntsBefore, _sbPlungesBefore;
    private string[] _sbDerailLines = Array.Empty<string>();
    /// <summary>The fall as the train's Wrecked event found it: the front's S, or NaN before it.</summary>
    private float _sbFallFront = float.NaN;
    private int _sbFallsAtWreck, _sbFallenAtWreck;
    private readonly List<Adventurer> _sbRiders = new();
    /// <summary>The heroes (by name) the flow lets fall now: the train's riders, from its fall to the rise.</summary>
    private readonly HashSet<string> _sbMayFall = new();

    private void SwitchBackListen()
    {
        if (_sbListening) return;
        _sbListening = true;
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testSwitchBack) return;
            if (text == "The slag line ends at the broken bridge!") _sbFallLogged = true;
            if (text == "The slag cart derails!") _sbDerailLogs++;
            if (text == "The slag cart plunges through the broken bridge.") _sbPlungeLogs++;
            if (text == SwitchBackRules.SafeLineLog) _sbLoneLogged = true;
        };
        // Morg's derail taunts, as the campaign has them (said and clean): any of them said is a derail heard
        _sbDerailLines = (Game.Instance.Adventure.Campaign?.Taunts ?? new List<Adventure.TauntPlan>())
            .Where(t => t.Trigger == Adventure.Objectives.TauntTriggers.Id(Adventure.Objectives.TauntTrigger.SlagDerailed))
            .SelectMany(t => new[] { t.Line.Text, t.CleanLine?.Text }).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).ToArray();
        EventBus.Instance.Narration += (text, _) =>
        {
            if (!_done && _testSwitchBack && _sbDerailLines.Any(text.Contains)) _sbDerailTaunts++;
        };
        EventBus.Instance.PartyWiped += () =>
        {
            if (_done || !_testSwitchBack) return;
            _sbWiped = true;
            _sbWipeRc = RailController.Instance;
        };
        // the live back's two calls, said or only logged: where the front was
        Action<Adventure.BarkTrigger, string?> called = (t, by) =>
        {
            if (_done || !_testSwitchBack || RailController.Instance is not { } r) return;
            if (t == Adventure.BarkTrigger.SwitchBack && float.IsNaN(_sbEarlyFront)) _sbEarlyFront = r.Party.FrontS;
            if (t == Adventure.BarkTrigger.SwitchBackNow && float.IsNaN(_sbLastFront)) _sbLastFront = r.Party.FrontS;
            if (t is Adventure.BarkTrigger.SwitchBack or Adventure.BarkTrigger.SwitchBackNow)
                GD.Print($"[switchback] B heard the {(t == Adventure.BarkTrigger.SwitchBack ? "early" : "last")} call (by {by ?? "the log"}) at front S={r.Party.FrontS:0.0}");
        };
        RailCalls.Called += called;
        _staticUnhooks.Add(() => RailCalls.Called -= called);
    }

    private void RunSwitchBackTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _sbClock += (float)GetProcessDeltaTime();
        HoldTheRunaway("switchback"); // Ruling 22: not this flow's business
        var party = main.Party;
        void Fail(string why) { GD.Print($"[switchback] RESULT FAIL ({_sbVariant}): {why}"); party.ForcedMoveInput = null; _done = true; GetTree().Quit(1); }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc || layout.Rail is not { } rail) { if (_sbClock > 3f) Fail("floor 12 has no line"); return; }
        SwitchBackListen();
        if (_sbHooked != rc)
        {
            _sbHooked = rc; // each floor build's controller
            rc.CartEvents += e =>
            {
                if (e.Kind != CartEventKind.Wrecked || _done || _sbStep != SbFall || !float.IsNaN(_sbFallFront) || RailController.Instance is not { } r) return;
                _sbFallFront = r.Party.FrontS;
                _sbFallsAtWreck = r.Falls;
                _sbGroundAtWreck = r.GroundFree;
                _sbFallenAtWreck = _sbRiders.Count(a => a.State.Fallen); // told after the train is taken (Fall flushes its events): the wipe may rebuild the floor before the next frame
                foreach (var a in _sbRiders) _sbMayFall.Add(a.State.Name); // the fall takes them
            };
        }
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive && e != _sbRider) e.Kill(); // the flow owns the fights (A's rider rides on to the gap)
        RideWaiveCages(main, rc, "switchback");
        enc.RunawayClock = 0f; // no runaway wagon on the party line while the cart stands at stop 2 with nobody aboard
        if (party.Local is not { } me) { Fail("no human"); return; }
        // nobody falls but the train's riders, at any step
        if (party.Active.FirstOrDefault(a => a.State.Fallen && !_sbMayFall.Contains(a.State.Name)) is { } stray) { Fail($"{stray.State.Name} fell at step {_sbStep} (cell {DungeonBuilder.WorldToCell(stray.GlobalPosition)}): nobody fell but the train's"); return; }

        switch (_sbStep)
        {
            case 0 when _sbClock >= 1.5f:
            {
                if (enc.BreakRide != 3 || float.IsNaN(enc.BreakGapS) || float.IsNaN(enc.BreakGapEnemyS) || rail.Break is not { } brk) { Fail($"floor 12 has no broken bridge on ride 3 (ride {enc.BreakRide}, gap S {enc.BreakGapS:0.0})"); return; }
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch back || enc.SetPieceNode("r3_switch_out") is not TrackSwitch) { Fail("floor 12 lacks ride 3's switches"); return; }
                GD.Print($"[switchback] floor 12 ({_sbVariant}): broken bridge on ride {enc.BreakRide}, gap at party S={enc.BreakGapS:0.0} (enemy S={enc.BreakGapEnemyS:0.0}), r3_switch_back points S={back.PointsS:0.0} ({enc.BreakGapS - back.PointsS:0.0} m before the gap, fixed={back.Fixed}); stops [{string.Join(",", rc.StopS.Select(s => s.ToString("0.0")))}]; lethality {TierRules.Lethality}");
                if (SwitchBackBuild(main, root, layout, rail, brk, rc, enc) is { } bad) { Fail(bad); return; }
                if (FixedAfterBranch)
                {
                    if (enc.SetPieceNode("r3_switch_back2") is not TrackSwitch fixedBack) { Fail("fixedafter: no r3_switch_back2 built from the hook"); return; }
                    GD.Print($"[switchback] fixedafter: r3_switch_back2 points S={fixedBack.PointsS:0.0} ({enc.BreakGapS - fixedBack.PointsS:0.0} m before the gap, {fixedBack.PointsS - back.PointsS:0.0} m past r3_switch_back), fixed={fixedBack.Fixed} deadly={fixedBack.Deadly} thrown={fixedBack.Thrown}; the fall armed {enc.BreakArmed}");
                    if (!fixedBack.Fixed || fixedBack.Deadly || !fixedBack.Thrown || !back.Deadly || !enc.BreakArmed) { Fail("fixedafter: r3_switch_back2 is not a fixed, thrown back past a live r3_switch_back, or the fall is disarmed"); return; }
                }
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(2);
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 1 when _sbClock >= 1f:
            case 3 when _sbClock >= 1f:
            {
                // A1 (the party far off, at stop 0) and A2 (the party on its lane 5 cells short of the gap): traffic on the slag line runs into the gap
                var near = _sbStep == 3;
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 2 || rc.Cart.Riders.Count > 0) { Fail($"A: the cart is {rc.State.Mode} at stop {rc.StopIndex} with {rc.Cart.Riders.Count} aboard, not held empty at stop 2"); return; }
                if (near)
                {
                    var stand = rail.Party[rail.Break!.Run.From - 2];
                    party.Teleport(main.Builder.Surface(stand) + Vector3.Up * 0.3f, _sbGapAt);
                    GD.Print($"[switchback] A2 the party set down on its own lane at {stand}, {main.Builder.Surface(stand).DistanceTo(_sbGapAt):0.0} m from the gap");
                }
                _sbDropsBefore = enc.BreakDrops;
                (_sbDerailsBefore, _sbTauntsBefore, _sbPlungesBefore) = (_sbDerailLogs, _sbDerailTaunts, _sbPlungeLogs);
                enc.DebugSpawnTrafficAt(enc.BreakGapEnemyS - 30f);
                _sbTraffic = enc.TrafficCart;
                if (_sbTraffic == null) { Fail("A: no traffic cart started"); return; }
                // a rider aboard it, as a ride's slag cart carries them: lost to the crevasse with the cart
                _sbRider = main.Builder.SpawnEnemy(main.Builder.RegularEnemies(main.Builder.CurrentFloor)[0], _sbTraffic.RiderSpot(0).Origin, main.Builder.CurrentFloor);
                if (_sbRider == null || !_sbTraffic.Board(_sbRider)) { Fail("A: no rider boarded the traffic cart"); return; }
                _sbRider.IsReinforcement = true;
                GD.Print($"[switchback] A{(near ? 2 : 1)} the cart held empty at stop 2 (S={rc.State.S:0.0}); a traffic cart starts on the enemy line at S={_sbTraffic.Run.State.S:0.0}, 30 m short of the gap, at v {_sbTraffic.Run.State.V:0.0}, {_sbRider.DisplayName} aboard");
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 2:
            case 4:
            {
                var near = _sbStep == 4;
                var gone = !IsInstanceValid(_sbTraffic) && enc.TrafficCart == null;
                if (enc.BreakDrops <= _sbDropsBefore || !gone) { if (_sbClock > 8f) Fail($"A: the traffic cart did not drop through within 8 s (drops {enc.BreakDrops - _sbDropsBefore}, gone={gone}, at S={(IsInstanceValid(_sbTraffic) ? _sbTraffic!.Run.State.S : float.NaN):0.0})"); break; }
                if (_sbClock < 0.5f) break; // a moment for any line or taunt it would raise
                var fallen = party.Active.Count(a => a.State.Fallen);
                // its rider: dead, carried by nothing, hidden in the dark under the gap (nothing left on the line or hanging over the drop)
                string rider;
                if (!IsInstanceValid(_sbRider)) rider = "freed";
                else
                {
                    var r = _sbRider!;
                    var cell = DungeonBuilder.WorldToCell(r.GlobalPosition);
                    var lip = main.Builder.Surface(cell).Y;
                    rider = $"{r.DisplayName} alive={r.IsAlive} carried={r.Carried} visible={r.Visible} at {cell} (a drop {CrevasseRules.IsDrop(layout, cell)}) y {r.GlobalPosition.Y:0.0}, lip {lip:0.0}";
                    if (r.IsAlive || r.Carried || r.Visible || !CrevasseRules.Fell(layout, cell, r.GlobalPosition.Y, lip)) { Fail($"A: the traffic cart's rider was not lost into the drop: {rider}"); return; }
                }
                // quiet: no derail line and no derail taunt; the plunge told in the log only with a hero within 40 m of the gap
                var nearest = party.Active.Where(a => !a.Parked).Select(a => a.GlobalPosition.DistanceTo(_sbGapAt)).DefaultIfEmpty(float.PositiveInfinity).Min();
                int derails = _sbDerailLogs - _sbDerailsBefore, taunts = _sbDerailTaunts - _sbTauntsBefore, plunges = _sbPlungeLogs - _sbPlungesBefore;
                var heard = $"\"The slag cart derails!\" {derails}x, derail taunts {taunts}x, \"The slag cart plunges through the broken bridge.\" {plunges}x, the nearest hero {nearest:0.0} m from the gap";
                if (derails > 0 || taunts > 0) { Fail($"A: the drop was not quiet: {heard}"); return; }
                if (near != (nearest <= SwitchBackRules.PlungeHeard)) { Fail($"A{(near ? 2 : 1)}: the party is not {(near ? "within" : "beyond")} {SwitchBackRules.PlungeHeard:0} m of the gap ({nearest:0.0} m)"); return; }
                if (plunges != (near ? 1 : 0)) { Fail($"A{(near ? 2 : 1)}: the plunge was {(near ? "not told with a hero near" : "told with every hero far off")}: {heard}"); return; }
                GD.Print($"[switchback] A{(near ? 2 : 1)} ok: the traffic cart dropped through the broken bridge after {_sbClock:0.0} s and is gone (freed); its rider lost ({rider}); quiet: {heard}; {fallen} hero(es) fallen");
                _sbRider = null;
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 5 when _sbClock >= 0.5f:
            {
                // A3: the cart held at stop 4, past the gap: the next traffic cart starts past the gap's far edge (stops 4 and 5 keep their traffic)
                rc.Restore(4);
                enc.TrafficClock = SetPieceRules.SlagTrafficEvery; // due at once
                GD.Print($"[switchback] A3 the cart restored at stop 4 (S={rc.State.S:0.0}, past the gap at {enc.BreakGapS:0.0}); the traffic clock run out");
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 6:
            {
                if (enc.TrafficCart is not { } t) { if (_sbClock > 3f) Fail($"A3: no traffic cart started at stop 4 (clock {enc.TrafficClock:0.0}, armed {enc.ArmedRide}, mode {rc.State.Mode})"); break; }
                var want = SwitchBackRules.TrafficStartS(rc.State.S, enc.BreakGapS, enc.BreakGapFarEnemyS);
                var s = t.Run.State.S;
                GD.Print($"[switchback] A3 ok: with the cart held at stop {rc.StopIndex}, a traffic cart starts on the enemy line at S={s:0.0} ({s - enc.BreakGapFarEnemyS:0.0} m past the gap's far edge at {enc.BreakGapFarEnemyS:0.0}; the rule's {want:0.0})");
                if (s < enc.BreakGapFarEnemyS + CartRules.HalfLength || Mathf.Abs(s - want) > 1f) { Fail("A3: the traffic cart did not start just past the gap"); return; }
                rc.Restore(2);
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 7 when _sbClock >= 1f:
            {
                // B: everyone boards at stop 2
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 8:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 3) { if (_sbClock > 20f) Fail($"B: ride 3 did not depart (mode {rc.State.Mode}, stop {rc.StopIndex}, armed {enc.ArmedRide})"); break; }
                _sbRiders.Clear();
                _sbRiders.AddRange(party.Active.Where(a => a.Cart == rc.Cart));
                if (_sbRiders.Count != party.Active.Count() || me.Cart != rc.Cart) { Fail($"B: {_sbRiders.Count} of {party.Active.Count()} heroes aboard at the departure (the lead seated={me.Cart == rc.Cart})"); return; }
                _sbFallsBefore = rc.Falls;
                _sbGroundBefore = rc.GroundFree;
                GD.Print($"[switchback] B departed stop 2 with [{string.Join(", ", _sbRiders.Select(a => a.State.Name))}] aboard; plating {rc.State.Plating:0}");
                _sbStep++; _sbClock = 0f;
                break;
            }
            case 9:
            {
                if (enc.SetPieceNode("r3_switch_out") is not TrackSwitch r3) { Fail("no r3_switch_out"); return; }
                if (r3 is { Taken: true, Thrown: false }) { Fail("B: ride 3's switch_out lever was never offered to the seated lead before its points"); return; }
                if (rc.State.Mode == CartMode.Rolling && r3 is { Thrown: false, Taken: false } && me.Cart == rc.Cart && party.LookingAt == r3)
                {
                    var ahead = r3.LeverS - rc.Party.FrontS;
                    var prompt = r3.Prompt;
                    party.VrUse(true);
                    party.VrUse(false);
                    GD.Print($"[switchback] B the seated lead throws r3_switch_out from the seat (prompt \"{prompt}\", {ahead:0.0} m before the lever): thrown={r3.Thrown}, still seated={me.Cart == rc.Cart}");
                    if (!r3.Thrown || me.Cart != rc.Cart) { Fail("B: the seated press did not throw r3_switch_out, or got the lead out"); return; }
                }
                if (!rc.Crossing) { if (_sbClock > 90f) Fail($"B: the cart never took r3_switch_out (front S={rc.Party.FrontS:0.0}, thrown={r3.Thrown}, mode {rc.State.Mode})"); break; }
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch back) { Fail("no r3_switch_back"); return; }
                GD.Print($"[switchback] B the cart took the switch at S={rc.CrossOutS:0.0}: its return {(float.IsNaN(rc.CrossBackS) ? "open (no back set)" : $"set at {rc.CrossBackS:0.0}")}; r3_switch_back deadly={back.Deadly} fixed={back.Fixed} thrown={back.Thrown}");
                if (!float.IsNaN(rc.CrossBackS)) { Fail("B: with the live back the cart crossed with a return already set"); return; }
                if (!back.Deadly || back.Fixed || back.Thrown) { Fail("B: r3_switch_back is not a straight live lever (deadly, not fixed)"); return; }
                _sbStep = LoneBranch ? SbLone : FixedAfterBranch ? SbFixedAfter : WrongBranch ? SbFall : SbHome; _sbClock = 0f;
                break;
            }
            case SbHome:
            {
                // the thrown branch: the lead throws the back from the seat as the window offers it; the cart takes it at its points
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch back) { Fail("no r3_switch_back"); return; }
                if (SwitchBackRide(main, rc, enc, back, me, Fail)) return;
                if (!_sbBackPressed && rc.State.Mode == CartMode.Rolling && me.Cart == rc.Cart && party.LookingAt == back && !back.Taken)
                {
                    var prompt = back.Prompt;
                    _sbPressAhead = back.LeverS - rc.Party.FrontS;
                    if (prompt != "Throw the switch") { Fail($"B: the seated window offered r3_switch_back as \"{prompt}\", not \"Throw the switch\""); return; }
                    party.VrUse(true);
                    party.VrUse(false);
                    _sbBackPressed = true;
                    GD.Print($"[switchback] B the seated lead throws r3_switch_back from the seat (prompt \"{prompt}\", {_sbPressAhead:0.0} m before the lever, early call heard at {back.LeverS - _sbEarlyFront:0.0} m before it): thrown={back.Thrown} by {back.LastToggledBy}, still seated={me.Cart == rc.Cart}");
                    if (!back.Thrown || back.LastToggledBy != me.State.Name || me.Cart != rc.Cart) { Fail("B: the seated press did not throw r3_switch_back, or got the lead out"); return; }
                }
                if (!_sbBackTaken) break;
                if (!_sbBackPressed) { Fail("B: the seated window never offered r3_switch_back before its points"); return; }
                if (!(Mathf.Abs(rc.CrossBackS - back.PointsS) < 0.01f)) { Fail($"B: the cart took the thrown back but its return is {rc.CrossBackS:0.0}, not its points {back.PointsS:0.0}"); return; }
                _sbStep++; _sbClock = 0f;
                break;
            }
            case SbHome + 1:
            {
                // home on its own lane past the crossover, then on to stop 3: no fall
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch back) { Fail("no r3_switch_back"); return; }
                if (rc.State.Mode == CartMode.Wrecked || rc.Falls != _sbFallsBefore) { Fail($"B: the train was wrecked or fell (falls {rc.Falls - _sbFallsBefore}) after the back was thrown"); return; }
                var home = back.PointsS + RailController.SwitchStretch + CartRules.HalfLength + 1f;
                if (!_sbHome && rc.State.Mode == CartMode.Rolling && rc.Party.FrontS >= home)
                {
                    GD.Print($"[switchback] B the front at S={rc.Party.FrontS:0.0} (the points + {rc.Party.FrontS - back.PointsS:0.0} m): lane blend {rc.LaneBlend:0.000}, across at the gap {rc.LaneBlendAt(enc.BreakGapS):0.000}");
                    if (rc.LaneBlend > 0.01f) { Fail($"B: the cart is not home on its own lane by the points + {home - back.PointsS:0.0} m (blend {rc.LaneBlend:0.000})"); return; }
                    _sbHome = true;
                }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 3) { if (_sbClock > 60f) Fail($"B: the cart did not arrive at stop 3 (mode {rc.State.Mode}, stop {rc.StopIndex}, front S={rc.Party.FrontS:0.0})"); break; }
                if (!_sbHome || _sbSight != "clear") { Fail($"B: the cart reached stop 3 without the home check ({_sbHome}) or the sight from the lever ({_sbSight ?? "never cast"})"); return; }
                GD.Print($"[switchback] B the cart arrived at stop 3 (S={rc.State.S:0.0}) with no fall: plating {rc.State.Plating:0}, riders [{string.Join(", ", _sbRiders.Select(a => $"{a.State.Name}{(a.State.Fallen ? " FALLEN" : "")}"))}]");
                if (LoneBranch)
                    GD.Print($"[switchback] RESULT ok (lone): A traffic dropped through the broken bridge quietly (told only with a hero near) and started past it from stop 4; B the live back called early {back.LeverS - _sbEarlyFront:0.0} m before its lever, never thrown; the lead, the only human, downed in the seat before the window; at its points the cart took the safe line with the lever straight (\"{SwitchBackRules.SafeLineLog}\", back from {back.PointsS:0.0}), home by the gap, no fall, arrived at stop 3; sight from the lever to the bridge's lamp clear; nobody fell");
                else
                    GD.Print($"[switchback] RESULT ok (thrown): A traffic dropped through the broken bridge quietly (told only with a hero near) and started past it from stop 4; B the live back called early {back.LeverS - _sbEarlyFront:0.0} m before its lever, straight with no hand on it until the lead threw it from the seat {_sbPressAhead:0.0} m before the lever, taken at its points (back from {back.PointsS:0.0}), home by the gap, no fall, arrived at stop 3; sight from the lever to the bridge's lamp clear; nobody fell");
                _done = true;
                GetTree().Quit(0);
                return;
            }
            case SbLone:
            {
                // =lone: nobody presses; the lead, the only human, is downed in the seat just before the window, so no human aboard can throw the back
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch back) { Fail("no r3_switch_back"); return; }
                if (SwitchBackRide(main, rc, enc, back, me, Fail)) return;
                if (rc.State.Mode == CartMode.Wrecked || rc.Falls != _sbFallsBefore) { Fail($"lone: the train was wrecked or fell (falls {rc.Falls - _sbFallsBefore}) with no human able to throw the back"); return; }
                var windowStart = back.LeverS - SeatedSwitchRules.WindowAhead;
                if (!_sbLoneDowned && rc.State.Mode == CartMode.Rolling && rc.Party.FrontS >= windowStart - 3f)
                {
                    party.AllowPossession = false; // else solo hands the lead to a seated companion, who would then be the human able to throw it
                    me.State.Mangled = true;       // held down until the back is taken (calm auto-revive never stands a Mangled member up)
                    me.State.Hp = 0;
                    party.OnMemberDowned(me.Index);
                    _sbLoneDowned = true;
                    var able = _sbRiders.Count(a => SwitchBackRules.AbleToThrow(a.Control, a.Downed, a.Seated && a.Cart == rc.Cart));
                    GD.Print($"[switchback] lone: the lead {me.State.Name} downed in the seat at front S={rc.Party.FrontS:0.0} ({windowStart - rc.Party.FrontS:0.0} m short of the window's start {windowStart:0.0}): downed={me.Downed} seated={me.Cart == rc.Cart}, {able} rider(s) able to throw the back");
                    if (!me.Downed || me.Cart != rc.Cart || able != 0) { Fail("lone: the lead is not downed in the seat, or someone aboard could still throw the back"); return; }
                }
                if (!_sbBackTaken) { if (_sbClock > 90f) Fail($"lone: the cart never reached r3_switch_back's points (front S={rc.Party.FrontS:0.0}, mode {rc.State.Mode})"); break; }
                if (!_sbLoneDowned) { Fail("lone: the back was taken before the lead was downed"); return; }
                me.State.Mangled = false; // taken: calm auto-revive may stand the lead up again
                party.AllowPossession = true;
                _sbStep = SbHome + 1; _sbClock = 0f; // home on its own lane, then on to stop 3, as the thrown branch
                break;
            }
            case SbFixedAfter:
            {
                // =fixedafter: nobody presses the live back (passed straight); caught 3 m past its points, the watchdog sets the front at the
                // fixed back's points, which take the cart home
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch liveBack || enc.SetPieceNode("r3_switch_back2") is not TrackSwitch fixedBack) { Fail("fixedafter: no r3_switch_back or r3_switch_back2"); return; }
                if (SwitchBackRide(main, rc, enc, liveBack, me, Fail)) return;
                if (rc.State.Mode == CartMode.Wrecked || rc.Falls != _sbFallsBefore) { Fail($"fixedafter: the train was wrecked or fell (falls {rc.Falls - _sbFallsBefore}) with a fixed back before the gap"); return; }
                var at = rc.Party.FrontS;
                if (!_sbFaStalled && _sbBackTaken && rc.State.Mode == CartMode.Rolling && at >= liveBack.PointsS + 3f)
                {
                    if (!float.IsNaN(rc.CrossBackS) || fixedBack.Taken) { Fail($"fixedafter: past the live back the cart's return is {(float.IsNaN(rc.CrossBackS) ? "open" : $"set at {rc.CrossBackS:0.0}")} and r3_switch_back2 taken {fixedBack.Taken}, not open and untaken"); return; }
                    _sbFaStalled = true;
                    rc.DebugStall = true;
                    GD.Print($"[switchback] fixedafter: the cart caught at front S={at:0.0}, {at - liveBack.PointsS:0.0} m past r3_switch_back's points (passed straight) and {fixedBack.PointsS - at:0.0} m short of r3_switch_back2's; its return open");
                }
                if (_sbFaStalled && !_sbFaGround && rc.GroundFree - _sbGroundBefore >= 1)
                {
                    // freed to the fixed back's points, never past them: taken there (this tick or the next)
                    _sbFaGround = true;
                    GD.Print($"[switchback] fixedafter: the watchdog freed the cart to front S={at:0.0} (r3_switch_back2's points {fixedBack.PointsS:0.0}); r3_switch_back2 taken {fixedBack.Taken}, the return {(float.IsNaN(rc.CrossBackS) ? "open" : $"set at {rc.CrossBackS:0.0}")}");
                    if (at < fixedBack.PointsS - 0.05f || at > fixedBack.PointsS + 1.5f) { Fail($"fixedafter: the watchdog set the front at S={at:0.0}, not r3_switch_back2's points {fixedBack.PointsS:0.0}"); return; }
                }
                if (_sbFaGround && fixedBack.Taken && !(Mathf.Abs(rc.CrossBackS - fixedBack.PointsS) < 0.01f)) { Fail($"fixedafter: r3_switch_back2 was taken but the cart's return is {rc.CrossBackS:0.0}, not its points {fixedBack.PointsS:0.0}"); return; }
                if (_sbFaGround && fixedBack.Taken && !_sbFaNearGap && rc.State.Mode == CartMode.Rolling && at >= enc.BreakGapS - 1f)
                {
                    GD.Print($"[switchback] fixedafter: the front at S={at:0.0} ({enc.BreakGapS - at:0.0} m short of the gap): lane blend {rc.LaneBlend:0.000}, across at the gap {rc.LaneBlendAt(enc.BreakGapS):0.000}");
                    if (rc.LaneBlend > 0.01f || at > enc.BreakGapS) { Fail($"fixedafter: the cart is not home on its own lane 1 m short of the gap (blend {rc.LaneBlend:0.000}, front S={at:0.0})"); return; }
                    _sbFaNearGap = true;
                }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 3) { if (_sbClock > 120f) Fail($"fixedafter: the cart did not arrive at stop 3 (mode {rc.State.Mode}, stop {rc.StopIndex}, front S={at:0.0}, stalled {_sbFaStalled}, ground {_sbFaGround}, r3_switch_back2 taken {fixedBack.Taken})"); break; }
                if (!_sbFaStalled || !_sbFaGround || !_sbFaNearGap || _sbSight != "clear") { Fail($"fixedafter: the cart reached stop 3 without the stall ({_sbFaStalled}), the grind ({_sbFaGround}), the home check by the gap ({_sbFaNearGap}) or the sight from the lever ({_sbSight ?? "never cast"})"); return; }
                GD.Print($"[switchback] B the cart arrived at stop 3 (S={rc.State.S:0.0}) with no fall: plating {rc.State.Plating:0}, riders [{string.Join(", ", _sbRiders.Select(a => $"{a.State.Name}{(a.State.Fallen ? " FALLEN" : "")}"))}]");
                GD.Print($"[switchback] RESULT ok (fixedafter): A traffic dropped through the broken bridge quietly (told only with a hero near) and started past it from stop 4; B the live back called early and last, never thrown, passed straight; caught 3 m past its points, the watchdog set the front at r3_switch_back2's points ({fixedBack.PointsS:0.0}, {enc.BreakGapS - fixedBack.PointsS:0.0} m before the gap), which took the cart home (back from {fixedBack.PointsS:0.0}); home with the front 1 m short of the gap, no fall, arrived at stop 3; nobody fell");
                _done = true;
                GetTree().Quit(0);
                return;
            }
            case SbFall:
            {
                if (!float.IsNaN(_sbFallFront)) { _sbStep++; _sbClock = 0f; break; }
                if (enc.SetPieceNode("r3_switch_back") is not TrackSwitch wrongBack) { Fail("no r3_switch_back"); return; }
                if (SwitchBackRide(main, rc, enc, wrongBack, me, Fail)) return;
                if (_sbVariant == "stall" && rc.State.Mode == CartMode.Rolling)
                {
                    // the cart caught short of the back's points, and again past them on the slag line: the watchdog frees it each time
                    var at = rc.Party.FrontS;
                    var windowStart = wrongBack.LeverS - SeatedSwitchRules.WindowAhead;
                    if (_sbStalls == 0 && at >= wrongBack.LeverS - 25f)
                    {
                        _sbStalls = 1;
                        rc.DebugStall = true;
                        GD.Print($"[switchback] stall: the cart caught at front S={at:0.0}, {windowStart - at:0.0} m short of r3_switch_back's window (it opens at {windowStart:0.0}; the lever {(wrongBack.Thrown ? "thrown" : "straight")}, the window seen {_sbWindowSeen})");
                    }
                    else if (_sbStalls == 1 && rc.GroundFree - _sbGroundBefore >= 1)
                    {
                        // freed to the window's start, never past it: the offer and the calls still to come
                        if (at > windowStart + 0.5f || wrongBack.Taken) { Fail($"stall: the watchdog freed the cart short of the window to front S={at:0.0}, not the window's start {windowStart:0.0} (taken {wrongBack.Taken})"); return; }
                        _sbStalls = 2;
                        GD.Print($"[switchback] stall: the watchdog freed the cart to front S={at:0.0}, the window's start ({windowStart:0.0}); the back untaken, straight");
                    }
                    else if (_sbStalls == 2 && at >= wrongBack.LeverS - 8f)
                    {
                        // inside the window, short of the last call (final review M1)
                        if (!float.IsNaN(_sbLastFront) || wrongBack.Taken) { Fail($"stall: the last call came (at {_sbLastFront:0.0}) or the back was taken before the cart reached front S={at:0.0}"); return; }
                        _sbStalls = 3;
                        rc.DebugStall = true;
                        GD.Print($"[switchback] stall: the cart caught at front S={at:0.0} inside r3_switch_back's window, {wrongBack.PointsS - at:0.0} m short of its points (the last call not yet heard)");
                    }
                    else if (_sbStalls == 3 && rc.GroundFree - _sbGroundBefore >= 2)
                    {
                        // freed to the last call's mark, never past it unheard
                        var mark = wrongBack.PointsS - SwitchBackRules.CallLast;
                        if (at < mark || at > mark + 0.5f || wrongBack.Taken) { Fail($"stall: the watchdog freed the cart inside the window to front S={at:0.0}, not the last call's mark {mark:0.0} (taken {wrongBack.Taken})"); return; }
                        _sbStalls = 4;
                        GD.Print($"[switchback] stall: the watchdog freed the cart to front S={at:0.0}, the last call's mark ({mark:0.0}, {wrongBack.PointsS - at:0.0} m short of the points); the back untaken, straight");
                    }
                    else if (_sbStalls == 4 && !wrongBack.Taken && at >= wrongBack.PointsS - 3f)
                    {
                        if (float.IsNaN(_sbLastFront)) { Fail($"stall: freed to the last call's mark, the cart reached front S={at:0.0} with no last call heard"); return; }
                        _sbStalls = 5;
                        rc.DebugStall = true;
                        GD.Print($"[switchback] stall: the cart caught at front S={at:0.0}, {wrongBack.PointsS - at:0.0} m short of r3_switch_back's points (the lever {(wrongBack.Thrown ? "thrown" : "straight")}, untaken, the window seen {_sbWindowSeen}, the last call heard at {wrongBack.PointsS - _sbLastFront:0.0} m)");
                    }
                    else if (_sbStalls == 5 && wrongBack.Taken && at >= wrongBack.PointsS + 5f)
                    {
                        _sbStalls = 6;
                        rc.DebugStall = true;
                        GD.Print($"[switchback] stall: the cart caught again at front S={at:0.0} across on the slag line, {enc.BreakGapS - at:0.0} m short of the gap (its return {(float.IsNaN(rc.CrossBackS) ? "open" : $"set at {rc.CrossBackS:0.0}")}; ground free {rc.GroundFree - _sbGroundBefore} time(s) so far)");
                    }
                }
                // the view from the seat on the slag line: at 12 m from the gap, and (a second shot, "_near") at 4 m
                var toGap = enc.BreakGapS - rc.Party.FrontS;
                if (rc.State.Mode == CartMode.Rolling && _screenshotPath != null && _sbShots < 2 && rc.FrontOnEnemyLane && toGap <= (_sbShots == 0 ? 12f : 4f))
                {
                    var path = _sbShots == 0 ? _screenshotPath : System.IO.Path.ChangeExtension(_screenshotPath, null) + "_near.png";
                    _sbShots++;
                    GD.Print($"[switchback] the view from the seat on the slag line, {toGap:0.0} m short of the gap");
                    SaveScreenshot(path);
                }
                if (rc.State.Mode == CartMode.Held && rc.StopIndex > 2) { Fail($"B: the cart reached stop {rc.StopIndex}: it did not fall"); return; }
                if (rc.State.Mode == CartMode.Wrecked) { Fail("B: the cart was wrecked, not fallen"); return; }
                if (rc.State.Mode == CartMode.Rolling && rc.Party.FrontS > enc.BreakGapS + 2f) { Fail($"B: the front passed the gap (S={rc.Party.FrontS:0.0}, blend there {rc.LaneBlendAt(enc.BreakGapS):0.00}) and the train did not fall"); return; }
                if (_sbClock > 120f) Fail($"B: the train did not fall within 120 s (mode {rc.State.Mode}, front S={rc.Party.FrontS:0.0})");
                break;
            }
            case SbFall + 1:
            {
                if (_sbVariant == "stall" && (_sbStalls != 6 || _sbGroundAtWreck - _sbGroundBefore != 4)) { Fail($"stall: the train fell after {_sbStalls} step(s) and {_sbGroundAtWreck - _sbGroundBefore} grind(s) free, not 6 and 4 (the watchdog set it at the window's start, then at the last call's mark, then at the back's points, then ground it into the gap)"); return; }
                if (!_sbBackTaken || _sbSight != "clear") { Fail($"B: the train fell before the back's points were judged ({_sbBackTaken}) or the sight from the lever was cast ({_sbSight ?? "never"})"); return; }
                var off = Mathf.Abs(_sbFallFront - enc.BreakGapS);
                if (_sbFallsAtWreck < 1) { Fail("B: a wreck, not a fall"); return; }
                if (off > 0.6f) { Fail($"B: the train fell with its front at S={_sbFallFront:0.00}, {off:0.00} m from the gap at {enc.BreakGapS:0.00}"); return; }
                if (!_sbFallLogged) { Fail("B: \"The slag line ends at the broken bridge!\" was not logged"); return; }
                // every rider fallen within 1 s: at the fall itself, or (the wipe not yet come) by the frames after it
                var standing = _sbFallenAtWreck == _sbRiders.Count || _sbWiped ? new List<Adventurer>() : _sbRiders.Where(a => !a.State.Fallen).ToList();
                if (_sbWiped && _sbFallenAtWreck < _sbRiders.Count) { Fail($"B: {_sbRiders.Count - _sbFallenAtWreck} of {_sbRiders.Count} riders not fallen at the fall, and the party wiped"); return; }
                if (standing.Count > 0) { if (_sbClock > 1f) Fail($"B: within 1 s of the fall {string.Join(", ", standing.Select(a => a.State.Name))} not fallen"); break; }
                GD.Print($"[switchback] B the train fell with its front at S={_sbFallFront:0.00} ({off:0.00} m from the gap at {enc.BreakGapS:0.00}), \"The slag line ends at the broken bridge!\" logged; every rider fallen [{string.Join(", ", _sbRiders.Select(a => a.State.Name))}] ({_sbFallenAtWreck} of {_sbRiders.Count} at the fall itself, {_sbClock:0.00} s on)");
                _sbStep++; _sbClock = 0f;
                break;
            }
            case SbFall + 2:
            {
                if (!_sbWiped) { if (_sbClock > 5f) Fail("B: the party did not wipe within 5 s of the fall"); break; }
                GD.Print("[switchback] B the party has fallen: the wipe");
                _sbStep++; _sbClock = 0f;
                break;
            }
            case SbFall + 3:
            {
                if (rc == _sbWipeRc || main.Rebuilding) { if (_sbClock > 10f) Fail("B: the floor was not built again within 10 s of the wipe"); break; }
                _sbMayFall.Clear(); // risen: nobody may fall now
                var heroes = party.Active.Select(a => $"{a.State.Name} {a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m{(a.State.Fallen ? " FALLEN" : "")}{(a.Downed ? " down" : "")}").ToList();
                var problems = new List<string>();
                if (rc.StopIndex != 2 || rc.State.Mode != CartMode.Held) problems.Add($"the cart {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 2");
                if (rc.State.Plating < CartRules.MaxPlating || !rc.Cart.Visible) problems.Add($"plating {rc.State.Plating:0}/{CartRules.MaxPlating:0}, visible={rc.Cart.Visible}");
                foreach (var a in party.Active)
                    if (a.State.Fallen || a.Downed || a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) > 10f || !CrevasseRules.SetDownOk(layout, DungeonBuilder.WorldToCell(a.GlobalPosition)))
                        problems.Add($"{a.State.Name} fallen={a.State.Fallen} downed={a.Downed} {a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart at {DungeonBuilder.WorldToCell(a.GlobalPosition)}");
                if (problems.Count > 0) { if (_sbClock > 10f) Fail($"B the rise: {string.Join("; ", problems)}"); break; } // judged as the rise sets them down (after Restore's deferred set-down): a companion may then walk on to its leader's side
                GD.Print($"[switchback] B the rise at stop {rc.StopIndex} after {_sbClock:0.0} s: the cart {rc.State.Mode}, plating {rc.State.Plating:0}/{CartRules.MaxPlating:0}; heroes [{string.Join(", ", heroes)}]");
                GD.Print($"[switchback] RESULT ok ({_sbVariant}): A traffic dropped through the broken bridge quietly (told only with a hero near) and started past it from stop 4; B the live back called early and last ({_sbLastFront - _sbEarlyFront:0.0} m apart), never thrown (no hook), passed straight{(_sbVariant == "stall" ? "; caught short of its window, inside it, short of its points and past them, the watchdog set the cart at the window's start, then at the last call's mark (the last call still heard), then at the points, then ground it into the gap" : "")}; the window offered it to the lead; the train kept on the slag line fell at the gap (front {_sbFallFront:0.0}, gap {enc.BreakGapS:0.0}) and the wipe rose at stop 2; sight from the lever to the bridge's lamp clear; nobody fell but the train's");
                _done = true;
                GetTree().Quit(0);
                return;
            }
        }
    }

    /// <summary>The ride step of B on the wrong branch: the train rolls across from the switch until it falls.</summary>
    private const int SbFall = 10;
    /// <summary>The ride steps of B on the thrown branch: the back thrown from the seat and taken, then home and on to stop 3.</summary>
    private const int SbHome = 20;
    /// <summary>The ride step of B on the lone branch: the lead downed before the window, the back taken home anyway; then <see cref="SbHome"/> + 1.</summary>
    private const int SbLone = 30;
    /// <summary>The ride step of B on the fixedafter branch: the live back passed straight, the stall ground to the fixed back, home and on to stop 3.</summary>
    private const int SbFixedAfter = 40;

    /// <summary>
    /// Each rolling tick of B after the cross, both branches: no hand on the live back until the lead's press (a companion never moves it),
    /// the early call 25-30 m before the lever; at the lever's S the sight ray to the bridge's lamp (and, windowed, the view); at its points the
    /// take as the lever stands (the wrong branch: the last call 5-6.5 m before them, never thrown, passed straight). True when it failed.
    /// </summary>
    private bool SwitchBackRide(Main main, RailController rc, RailEncounterController enc, TrackSwitch back, Adventurer me, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) return false;
        var front = rc.Party.FrontS;
        if (me.Cart == rc.Cart && Main.Instance.Party.LookingAt == back && back.Prompt == "Throw the switch") _sbWindowSeen = true;
        if (!_sbBackPressed && (back.Thrown || back.LastToggledBy != null)) { fail($"B: r3_switch_back moved before any human's press (thrown={back.Thrown}, by {back.LastToggledBy ?? "nobody"})"); return true; }
        if (!float.IsNaN(_sbEarlyFront) && (back.LeverS - _sbEarlyFront < 25f || back.LeverS - _sbEarlyFront > 30f)) { fail($"B: the early call came {back.LeverS - _sbEarlyFront:0.0} m before the lever, not 25-30 m"); return true; }
        if (_screenshotPath != null && (!_sbLeverShot || WrongBranch && _sbShots < 2) && front >= back.LeverS - 4f
            && main.Builder.FloorRoot?.GetNodeOrNull<BrokenBridge>($"BrokenBridge_r{enc.BreakRide}") is { } ahead)
            me.AimAt(ahead.ToGlobal(new Vector3(0f, -2f, -ahead.GapLength / 2f))); // windowed: the view from the seat looks down the line, the gap just above the sights (clear of the blade and the prompt)
        if (front >= back.LeverS && _sbSight == null)
        {
            // the broken end in sight from the lever (Ruling 19): a layer-1 ray from the seated lead's eye to the bridge's red lamp
            if (main.Builder.FloorRoot?.GetNodeOrNull<BrokenBridge>($"BrokenBridge_r{enc.BreakRide}") is not { } dressing) { fail("B: no BrokenBridge dressing"); return true; }
            var eye = me.Camera.GlobalPosition;
            var lamp = dressing.LampPoint;
            var hit = me.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, lamp, 1));
            _sbSight = hit.Count == 0 ? "clear" : $"blocked by {(hit["collider"].AsGodotObject() as Node)?.Name} at {hit["position"].AsVector3().DistanceTo(eye):0.0} m";
            GD.Print($"[switchback] B sight from the lever: the front at S={front:0.0} (the lever at {back.LeverS:0.0}), a layer-1 ray from the seated lead's eye to the broken bridge's lamp ({eye.DistanceTo(lamp):0.0} m, the gap {enc.BreakGapS - front:0.0} m ahead): {_sbSight}");
            if (_sbSight != "clear") { fail($"B: the broken bridge's lamp is not in sight from the lever: {_sbSight}"); return true; }
            // and the back's own red signal lamp, past its points on the slag line's outer side
            if (main.Builder.FloorRoot?.GetNodeOrNull<Node3D>("Signal_r3_switch_back") is not { } signal) { fail("B: r3_switch_back has no signal lamp"); return true; }
            var signalLamp = signal.GetNode<Node3D>("Lamp").GlobalPosition;
            var signalHit = me.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, signalLamp, 1));
            var signalSight = signalHit.Count == 0 ? "clear" : $"blocked by {(signalHit["collider"].AsGodotObject() as Node)?.Name} at {signalHit["position"].AsVector3().DistanceTo(eye):0.0} m";
            var signalCell = DungeonBuilder.WorldToCell(signal.GlobalPosition);
            GD.Print($"[switchback] B the back's signal lamp at {signalCell} ({main.Builder.Layout![signalCell]}), {eye.DistanceTo(signalLamp):0.0} m from the seated lead's eye: {signalSight}");
            if (signalSight != "clear") { fail($"B: r3_switch_back's signal lamp is not in sight from the lever: {signalSight}"); return true; }
            if (_screenshotPath != null && !_sbLeverShot)
            {
                _sbLeverShot = true;
                var path = WrongBranch ? System.IO.Path.ChangeExtension(_screenshotPath, null) + "_lever.png" : _screenshotPath;
                GD.Print($"[switchback] the view from the seat at the lever's S, the gap {enc.BreakGapS - front:0.0} m ahead");
                SaveScreenshot(path);
            }
        }
        if (_sbBackTaken || front < back.PointsS) return false;
        // the front reached the back's points: taken as its lever stands (by the encounter's tick: this one or the next)
        if (!back.Taken) { if (front > back.PointsS + 2f) { fail("B: the front passed r3_switch_back's points and it is not taken"); return true; } return false; }
        _sbBackTaken = true;
        if (float.IsNaN(_sbEarlyFront)) { fail("B: the early call was never heard before the back's points"); return true; }
        var home = !float.IsNaN(rc.CrossBackS);
        GD.Print($"[switchback] B the front at S={front:0.0} reached r3_switch_back's points ({back.PointsS:0.0}): the lever {(back.Thrown ? "thrown" : "straight")}, {(home ? $"taken: back from {rc.CrossBackS:0.0}" : "passed straight: the cart stays on the slag line")}; calls early at {back.LeverS - _sbEarlyFront:0.0} m before the lever, last {(float.IsNaN(_sbLastFront) ? "none" : $"at {back.PointsS - _sbLastFront:0.0} m before the points")}");
        if (LoneBranch)
        {
            if (back.Thrown || !home) { fail($"lone: with no human aboard able to throw it, the back was thrown ({back.Thrown}) or the cart stayed across (home {home})"); return true; }
            if (!(Mathf.Abs(rc.CrossBackS - back.PointsS) < 0.01f)) { fail($"lone: the cart's return is {rc.CrossBackS:0.0}, not the back's points {back.PointsS:0.0}"); return true; }
            if (!_sbLoneLogged) { fail($"lone: \"{SwitchBackRules.SafeLineLog}\" was not logged at the take"); return true; }
        }
        else if (WrongBranch || FixedAfterBranch)
        {
            if (back.Thrown || home) { fail("B: nobody pressed, but the back was thrown or the cart crossed home"); return true; }
            if (!_sbWindowSeen) { fail("B: the seated window never offered r3_switch_back to the lead before its points"); return true; }
            if (float.IsNaN(_sbLastFront) || back.PointsS - _sbLastFront < 5f || back.PointsS - _sbLastFront > 6.5f) { fail($"B: the last call came {(float.IsNaN(_sbLastFront) ? "never" : $"{back.PointsS - _sbLastFront:0.0} m before the points")}, not 5-6.5 m"); return true; }
        }
        else if (!home) { fail(_sbBackPressed ? "B: the back was thrown but the cart stayed across" : "B: the seated window never offered r3_switch_back before its points"); return true; }
        return false;
    }

    /// <summary>
    /// The broken bridge as built: the gap a chasm with no footing under a ray, its deck cells with one; the enemy track's rails and
    /// sleepers bare over the gap; the party lane beside the gap and at r3_buckets solid floor (a body may be set down), its rails whole.
    /// Null when all holds, else what does not.
    /// </summary>
    private string? SwitchBackBuild(Main main, Node3D root, FloorLayout layout, RailLine rail, RailBreak brk, RailController rc, RailEncounterController enc)
    {
        var space = root.GetWorld3D().DirectSpaceState;
        bool Footing(GridPos c)
        {
            var top = main.Builder.Surface(c);
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(top + Vector3.Up, top + Vector3.Down, 1));
            return hit.Count > 0;
        }
        GridPos? Twin(GridPos p) => p.Neighbours4().Cast<GridPos?>().FirstOrDefault(n => layout.IsRail(n!.Value) && !layout.IsPartyRail(n.Value));
        var lines = new List<string>();
        GridPos? gap = null;
        for (var i = brk.Run.From; i <= brk.Run.To; i++)
        {
            var p = rail.Party[i];
            if (Twin(p) is not { } t) return $"party lane {i} {p} has no slag-line twin";
            var isGap = i == brk.Gap;
            var foot = Footing(t);
            var partyFoot = Footing(p);
            lines.Add($"{i}:{(isGap ? "GAP" : layout.IsBridge(t) ? layout.Bridges[t].ToString() : layout[t].ToString())}{(foot ? "+" : "-")}");
            if (isGap) { gap = t; _sbGapAt = main.Builder.Surface(t); if (!layout.IsChasm(t) || layout.IsBridge(t) || foot) return $"the gap {t} is not a chasm with no footing (chasm {layout.IsChasm(t)}, bridge {layout.IsBridge(t)}, footing {foot})"; }
            else if (!layout.IsBridge(t) || layout.Bridges[t] != BridgeKind.Deck || !foot) return $"slag-line cell {t} beside lane {i} is not a deck with its footing (bridge {layout.IsBridge(t)}, footing {foot})";
            if (!CrevasseRules.SetDownOk(layout, p) || !partyFoot) return $"party lane {i} {p} beside the break is not solid floor (set-down {CrevasseRules.SetDownOk(layout, p)}, footing {partyFoot})";
        }
        GD.Print($"[switchback] the break run {brk.Run.From}..{brk.Run.To} on the slag line (+ a footing under a ray, - none): [{string.Join(" ", lines)}]; the party lane beside it solid");
        // the meshes: the enemy track bare over the gap, the party track whole beside it and at the buckets
        int Verts(RailTrack track, string part, GridPos cell, float inset)
        {
            if (track.GetNodeOrNull<Node3D>(part) is not { } holder) return -1;
            var y = main.Builder.Surface(cell).Y;
            float x0 = cell.X * DungeonBuilder.CellSize + inset, x1 = (cell.X + 1) * DungeonBuilder.CellSize - inset;
            float z0 = cell.Y * DungeonBuilder.CellSize + inset, z1 = (cell.Y + 1) * DungeonBuilder.CellSize - inset;
            var n = 0;
            foreach (var mi in holder.GetChildren().OfType<MeshInstance3D>())
            {
                if (mi.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() == 0) continue;
                var xf = mi.GlobalTransform;
                foreach (var v in mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    var w = xf * v;
                    if (w.X > x0 && w.X < x1 && w.Z > z0 && w.Z < z1 && Mathf.Abs(w.Y - y) < 1.5f) n++;
                }
            }
            return n;
        }
        if (main.Builder.EnemyTrack is not { } enemyTrack || main.Builder.PartyTrack is not { } partyTrack) return "no tracks";
        var gapRails = Verts(enemyTrack, "Rails", gap!.Value, 0.3f);
        var gapSleepers = Verts(enemyTrack, "Sleepers", gap.Value, 0.3f);
        var partyCell = rail.Party[brk.Gap];
        var besideRails = Verts(partyTrack, "Rails", partyCell, 0.3f);
        var bucketsS = enc.SetPieceNode("r3_buckets") is { } buckets ? rc.Party.Track.ClosestS(buckets.GlobalPosition) : float.NaN;
        var bucketsCell = DungeonBuilder.WorldToCell(rc.Party.Track.Point(float.IsNaN(bucketsS) ? 0f : bucketsS));
        var bucketsRails = Verts(partyTrack, "Rails", bucketsCell, 0.3f);
        var bucketsOk = !float.IsNaN(bucketsS) && CrevasseRules.SetDownOk(layout, bucketsCell) && layout.IsPartyRail(bucketsCell);
        GD.Print($"[switchback] meshes: the slag line over the gap {gap}: {gapRails} rail and {gapSleepers} sleeper vertices; the party lane beside it {partyCell}: {besideRails} rail vertices; r3_buckets at party S={bucketsS:0.0} on {bucketsCell} (solid party lane {bucketsOk}): {bucketsRails} rail vertices, {bucketsS - enc.BreakGapS:0.0} m past the gap");
        if (gapRails != 0 || gapSleepers != 0) return "the slag line's track is not cut over the gap";
        if (besideRails <= 0 || bucketsRails <= 0) return "the party lane's rails are not whole beside the gap and at the buckets";
        if (!bucketsOk) return "r3_buckets do not stand on solid party lane";
        var gapFloor = main.Builder.Surface(gap.Value).Y;
        var timberOver = root.GetNodeOrNull<Node3D>("Rails/Timbering")?.GetChildren().OfType<MeshInstance3D>().Count(m => m.IsVisibleInTree() && DungeonBuilder.WorldToCell(m.GlobalPosition) == gap.Value && m.GlobalPosition.Y > gapFloor - 0.5f && m.GlobalPosition.Y < gapFloor + DungeonBuilder.LevelHeight) ?? 0;
        GD.Print($"[switchback] timber frame parts standing over the gap: {timberOver}");
        if (timberOver > 0) return "a timber frame hangs over the gap";
        if (root.GetNodeOrNull<BrokenBridge>($"BrokenBridge_r{brk.Ride}") is not { } dressing) return "no BrokenBridge dressing";
        var lamp = dressing.GetNodeOrNull<OmniLight3D>("LampLight");
        var far = dressing.GetNodeOrNull<OmniLight3D>("FarLampLight");
        var glow = dressing.GetNodeOrNull<OmniLight3D>("GapGlow");
        var haze = dressing.GetNodeOrNull<MeshInstance3D>("GapHaze");
        GD.Print($"[switchback] the BrokenBridge dressing at {DungeonBuilder.WorldToCell(dressing.GlobalPosition)}: {dressing.GetChildCount()} parts, the lamp {lamp?.OmniRange:0} m, energy {lamp?.LightEnergy:0.0}, shadow {lamp?.ShadowEnabled}; the far lamp {far?.OmniRange:0} m; the glow out of the gap {glow?.OmniRange:0} m, energy {glow?.LightEnergy:0.0}, {glow?.Position.Y:0.0} m under the deck; its haze {(haze != null ? "standing" : "MISSING")}");
        if (lamp == null || lamp.ShadowEnabled || far == null || far.ShadowEnabled) return "the broken bridge's red lamps are missing or cast shadows";
        if (glow == null || glow.ShadowEnabled || haze == null) return "the broken bridge's gap has no glow (light and haze) to read from the lever";
        return null;
    }
}
