using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-switch-back=wrong ------------------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo. Every enemy is culled as it appears. The build: ride 3 has the broken bridge,
// its gap on the slag line (a chasm, no footing: a ray down finds nothing) between deck cells (a ray down finds their footing), the enemy
// track's rails and sleepers cut bare over the gap and no timber frame over it, the party lane beside it and at r3_buckets solid floor
// with its rails whole. A, traffic, with the cart held at stop 2 and nobody aboard (rc.Restore(2)): A1 with the party far off (stop 0)
// and A2 with it on its own lane 5 cells short of the gap, a traffic cart is started 30 m short of the gap with a rider aboard; within 8
// s it drops through and is gone, its rider dead and hidden below the gap's lip, quietly: no "The slag cart derails!" and no derail
// taunt, and "The slag cart plunges through the broken bridge." only in A2 (a hero within 40 m of the gap). A3: the cart restored at stop
// 4 (past the gap) and the traffic clock run out, the next traffic cart starts just past the gap's far edge. B, the wrong branch (by the
// test's hook for now: the switch back is still fixed): the cart restored at stop 2, everyone boards and the cart departs; the seated
// lead throws r3_switch_out from the seat (the seated window, through the dispatch E uses); once the cart takes it, rc.DebugStayAcross()
// leaves it across on the slag line; the train falls with its front within 0.6 m of the gap and "The slag line ends at the broken
// bridge!" logged; every rider is fallen within 1 s; the wipe follows and rises at stop 2, every hero up within 10 m of a whole cart.
// After every step nobody fell but the train's riders (from its fall to the rise). With --screenshot=PATH (windowed) the view from the
// seat on the slag line is saved as the front comes within 12 m of the gap, and again (PATH_near) at 4 m. Prints [switchback] lines and a
// RESULT.
public partial class DebugTools
{
    private bool _testSwitchBack;
    private string _sbVariant = "wrong";
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
    }

    private void RunSwitchBackTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _sbClock += (float)GetProcessDeltaTime();
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
                var backBefore = rc.CrossBackS;
                rc.DebugStayAcross();
                GD.Print($"[switchback] B the cart took the switch at S={rc.CrossOutS:0.0} (back set at {backBefore:0.0}); DebugStayAcross: back now {rc.CrossBackS:0.0}, the cart stays on the slag line");
                if (!float.IsNaN(rc.CrossBackS) || !rc.Crossing) { Fail("B: DebugStayAcross left a return set"); return; }
                _sbStep++; _sbClock = 0f;
                break;
            }
            case SbFall:
            {
                if (!float.IsNaN(_sbFallFront)) { _sbStep++; _sbClock = 0f; break; }
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
                GD.Print($"[switchback] RESULT ok ({_sbVariant}): A traffic dropped through the broken bridge quietly (told only with a hero near) and started past it from stop 4; B the train kept on the slag line past the switch back fell at the gap (front {_sbFallFront:0.0}, gap {enc.BreakGapS:0.0}) and the wipe rose at stop 2; nobody fell but the train's");
                _done = true;
                GetTree().Quit(0);
                return;
            }
        }
    }

    /// <summary>The ride step of B: the train rolls across from the switch until it falls.</summary>
    private const int SbFall = 10;

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
        GD.Print($"[switchback] the BrokenBridge dressing at {DungeonBuilder.WorldToCell(dressing.GlobalPosition)}: {dressing.GetChildCount()} parts, the lamp {lamp?.OmniRange:0} m, energy {lamp?.LightEnergy:0.0}, shadow {lamp?.ShadowEnabled}");
        if (lamp == null || lamp.ShadowEnabled) return "the broken bridge's red lamp is missing or casts shadows";
        return null;
    }
}
