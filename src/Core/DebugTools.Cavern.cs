using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-cavern[=bridge|brake|stall|runaway] (plan 13, Task 3) ---------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo; every enemy is culled as it appears. The build: r4_bridge is live over the cavern's
// party deck (party S 903.1..991.1, 22 cells), the party track bare over its span and r5_jump's gap ("[cavern] party track bare over: ..."),
// and its rails meet the track's at both lips within 1 mm ("[cavern] join: ..."). The cart is restored at stop 3 (rc.Restore(3)), everyone
// boards and the cart departs on ride 4; ride 4's runaway is held (enc.DebugHoldRunaway(4)) except in =runaway. Every variant: nobody falls
// but the train's riders. =bridge (the default): nobody presses anything; the bridge's call comes with the collapse's start, 1.5 s or more
// after the train's rear came onto it; its front never comes within 2 m of the rear; while the train is held on it (enc.HoldsRiders) the
// seated lead's prompt is "" and a "Get out" press is refused; every party cell is gone within 6 s of the arrival at stop 4 (GoneBridges
// holds them, their Deck_ shapes disabled; the flow says how long before or after the arrival the last went); a following rc.Restore(3)
// stands it whole. =brake: the lead brakes from 1.6 s after the collapse starts (past the reaction time, BridgeRules.ReactionTime): "Don't
// stop" is called before the take, the take comes 1.5 s or more after the call by a press made 1.5 s or more after it, rc.Falls rises by 1 with every rider fallen within 1 s, the wipe rises at stop 3 with the bridge whole, and the
// next departure, unbraked, reaches stop 4. =stall: the train is caught (rc.DebugStall) once the collapse has started (its rear 6 m or more on
// the bridge), nobody braking: the bridge is held by the stall guard, never takes the train, and the watchdog frees it to stop 4. =runaway:
// the runaway is real, the split left straight, the lead pumps: the runaway goes down with the bridge with its front within 8 m of the
// bridge's start, its body neither rammed nor plunged as it went; no ram; stop 4. =early-brake (Task 3 review I1): the lead brakes as the
// bridge groans and holds it through the call and 3 s past it: held short (the front never past the bridge's start or 2 m behind the
// rear, whichever is further on), alive, no take; lets go
// for 1 s (no take), then a fresh brake: the take comes only after that press (made past the reaction time), then as =brake. =leap (Task 4):
// r4_slag's riders are spared (stunned and shielded: harmless, still targetable), the rest culled; the build has r4_leap live (stretch party
// S 913.1..975.1); "Leap across" is offered to the lead within 2 s of the stretch's start with r4_slag's along offset 1.2 m or less (printed),
// and never to a companion over the whole stretch, none of whom ever boards it; the lead presses it (LookingAt.Interact): seated on the slag
// cart's deck seat 0, out of the party cart's book, and its stick pumps and brakes nothing of the train; a seated blow from the deck lowers a
// rider's HP; "Leap back" is pressed (Task 5: on the slag line's call) and the lead sits in a party hero seat; stop 4 with Leaps == LeapsBack == 1. =wreck:
// the lead leaps across, then the train is wrecked (rc.DebugWreck) once the collapse has started: the lead is set down off the freed slag
// cart up, not fallen, not seated, on a cell where SetDownOk holds, and the collapse's front has not moved 1 s later. =leap goes on (Task 5): the
// lead leaps back on its first frame offered after the slag line's call (not 3 s after the leap); the call is heard 1.5 s or more before r4_slag
// goes over the drop with 0 heroes fallen and its riders dead; the drop's Deck_ shapes disabled, GoneBridges holding both twins, its lamp lit;
// stop 4. =stay: the lead leaps across and stays: the call heard 1.5 s or more before the drop, the lead falls with it (Fallen), nothing of the
// train falls (rc.Falls unchanged), stop 4, and the lead is back within 1 s of the arrival at 25 % HP (FallenReturn). =lone: possession off, the
// lead leaps across and is downed on the deck (Mangled, HP 0) before the call; at the drop the lead is hauled back into a party hero seat, not
// fallen, "{companion} hauls {lead} back across!" logged; stop 4. The drop's timing line: "[cavern] timing: drop call t=..., drop t=..." (s
// since the stretch's start). Prints [cavern] lines, the timing line
// ("[cavern] timing: bridge call t=..., earliest take t=...", seconds since the rear came onto the bridge; the earliest take is the call
// + the reaction time: no brake pressed before it counts) and a RESULT.
public partial class DebugTools
{
    private bool _testCavern;
    private string _cvVariant = "bridge";
    private int _cvStep, _cvPass = 1;
    private float _cvClock, _cvTime;
    private bool _cvListening, _cvWiped, _cvHoldChecked, _cvBraking, _cvStalled;
    private RailController? _cvHooked, _cvWipeRc;
    private readonly List<Adventurer> _cvRiders = new();
    private readonly HashSet<string> _cvMayFall = new();
    private float _cvRearOnT = float.NaN, _cvCollapseT = float.NaN, _cvCallT = float.NaN, _cvBrakeCallT = float.NaN, _cvTakeT = float.NaN;
    private float _cvArriveT = float.NaN, _cvAllGoneT = float.NaN, _cvEarliestTakeT = float.NaN, _cvRunawayT = float.NaN;
    private float _cvMinGap = float.PositiveInfinity;
    private int _cvFallsBefore, _cvFallenAtWreck, _cvFallsAtWreck, _cvGroundBefore;
    private float _cvFrontAtArrival = float.NaN;
    /// <summary>=brake: the first pass's take, said in the RESULT after the second pass.</summary>
    private string _cvTakeSummary = "";
    /// <summary>=early-brake: 0 before the groan, 1 braking from it, 2 let go, 3 braking afresh; the fresh press's time.</summary>
    private int _cvEbPhase;
    private float _cvEbAt = float.NaN, _cvFreshT = float.NaN;
    private bool BrakeBranch => _cvVariant is "brake" or "early-brake";
    /// <summary>=leap, =wreck, =stay and =lone: r4_slag's riders are spared (harmless and targetable) and the lead leaps across.</summary>
    private bool LeapBranch => _cvVariant is "leap" or "wreck" or "stay" or "lone";
    /// <summary>The drop (Task 5): the call heard, the drop's call seen, r4_slag gone over, the arrival; the lead (party.Local changes on a fall); r4_slag's riders at the call; the haul's log line.</summary>
    private float _cvDropHeardT = float.NaN, _cvDropCallT = float.NaN, _cvDropT = float.NaN, _cvLeapArriveT = float.NaN;
    private Adventurer? _cvLead;
    private readonly List<Enemies.Enemy> _cvSlagRiders = new();
    private string? _cvDropCalledBy;
    private bool _cvHaulLog, _cvGoesOverLog, _cvFellChecked;
    private float _cvBackHp = float.NaN;
    /// <summary>The leap flows: the phase, the stretch's start, the leap's, the slag cart leapt onto, the blow's target and its HP, the wreck's front.</summary>
    private int _cvLeapPhase;
    private float _cvStretchT = float.NaN, _cvAcrossT = float.NaN, _cvPhaseT = float.NaN, _cvWreckFront = float.NaN, _cvBlowHpBefore;
    private SlagCart? _cvSlag;
    private Enemies.Enemy? _cvBlowTarget;
    private bool _cvBlowHit, _cvHurtHooked, _cvWreckDone;
    private int _cvSwings;
    private string _cvLeapSummary = "";

    /// <summary>The calls, the wipe and the train's fall, heard once per run (the cart's events per floor build).</summary>
    private void CavernListen(RailController rc)
    {
        if (!_cvListening)
        {
            _cvListening = true;
            Action<BarkTrigger, string?> called = (t, by) =>
            {
                if (_done || !_testCavern || RailController.Instance is not { } r) return;
                if (t == BarkTrigger.BridgeGoing && float.IsNaN(_cvCallT)) _cvCallT = _cvTime;
                else if (t == BarkTrigger.BridgeBrake && float.IsNaN(_cvBrakeCallT)) _cvBrakeCallT = _cvTime;
                else if (t == BarkTrigger.SlagDrop && float.IsNaN(_cvDropHeardT))
                {
                    _cvDropHeardT = _cvTime;
                    _cvDropCalledBy = by;
                    GD.Print($"[cavern] heard the slag line's call (by {by ?? "the log"}) at front S={r.Party.FrontS:0.0}");
                    return;
                }
                else return;
                GD.Print($"[cavern] heard the {(t == BarkTrigger.BridgeGoing ? "bridge's" : "brake's")} call (by {by ?? "the log"}) at front S={r.Party.FrontS:0.0}, t={_cvTime - _cvRearOnT:0.00} s after the rear came on");
            };
            RailCalls.Called += called;
            _staticUnhooks.Add(() => RailCalls.Called -= called);
            EventBus.Instance.Message += text =>
            {
                if (_done || !_testCavern || _cvLead is not { } lead) return;
                if (text.EndsWith($" hauls {lead.State.Name} back across!", StringComparison.Ordinal)) { _cvHaulLog = true; GD.Print($"[cavern] logged: \"{text}\""); }
                if (text == "The slag cart goes over into the crevasse!") _cvGoesOverLog = true;
                if (text == $"{lead.State.Name} climbs back out of the dark.") _cvBackHp = lead.State.Hp; // ReturnFromCrevasse logs right after it sets the HP
            };
            EventBus.Instance.PartyWiped += () =>
            {
                if (_done || !_testCavern) return;
                _cvWiped = true;
                _cvWipeRc = RailController.Instance;
            };
        }
        if (_cvHooked == rc) return;
        _cvHooked = rc;
        rc.CartEvents += e =>
        {
            if (e.Kind != CartEventKind.Wrecked || _done || !_testCavern || !float.IsNaN(_cvTakeT)) return;
            _cvTakeT = _cvTime;
            _cvFallsAtWreck = rc.Falls;
            _cvFallenAtWreck = _cvRiders.Count(a => a.State.Fallen);
            foreach (var a in _cvRiders) _cvMayFall.Add(a.State.Name);
        };
    }

    private void RunCavernTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        var dt = (float)GetProcessDeltaTime();
        _cvClock += dt;
        _cvTime += dt;
        var party = main.Party;
        var v = _cvVariant;
        void Fail(string why) { GD.Print($"[cavern] RESULT FAIL ({v}): {why}"); party.ForcedMoveInput = null; _done = true; GetTree().Quit(1); }
        void Pass(string what) { GD.Print($"[cavern] RESULT ok ({v}): {what}"); party.ForcedMoveInput = null; _done = true; GetTree().Quit(0); }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_cvClock > 3f) Fail("floor 12 has no line"); return; }
        CavernListen(rc);
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
        {
            if (!e.IsAlive) continue;
            if (LeapBranch && e.Carrier is SlagCart { EncounterId: "r4_slag" }) { CavernSpare(e); continue; } // the leap's fight: harmless, still targetable
            e.Kill(); // the flow owns the plating and the HP
        }
        RideWaiveCages(main, rc, "cavern");
        enc.RunawayClock = 0f; // no traffic runaway on the party line
        if (v != "runaway") HoldTheRunaway("cavern");
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (party.Active.FirstOrDefault(a => a.State.Fallen && !_cvMayFall.Contains(a.State.Name)) is { } fell) { Fail($"{fell.State.Name} fell at step {_cvStep} (cell {DungeonBuilder.WorldToCell(fell.GlobalPosition)}): nobody falls but the train's riders"); return; }
        var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
        var from = enc.BridgeFromS;

        switch (_cvStep)
        {
            case 0 when _cvClock >= 1.5f:
            {
                if (CavernBuild(rc, enc) is { } bad) { Fail(bad); return; }
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(3);
                GD.Print($"[cavern] restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Stop))}]; the bridge's pace {BridgeRules.Pace(rc.Train.Count):0.00} m/s, the train's unpumped {CartPhysics.TargetSpeed(0, rc.Train.Count):0.00}");
                _cvStep++; _cvClock = 0f;
                break;
            }
            case 1 when _cvClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _cvStep++; _cvClock = 0f;
                break;
            }
            case 2:
            {
                var heroes = party.Active.Count();
                var seated = party.Active.Count(a => a.Cart == rc.Cart);
                if ((!me.Seated || seated < heroes) && _cvClock < 3f) break;
                if (!me.Seated || me.Cart != rc.Cart) { Fail("the lead did not take a seat"); return; }
                GD.Print($"[cavern] pass {_cvPass} boarded at stop {rc.StopIndex}: {seated} of {heroes} heroes seated");
                _cvStep++; _cvClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 4) { if (_cvClock > 15f) Fail($"ride 4 did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                if (v == "runaway") party.ForcedMoveInput = new Vector2(0, -1); // the stick forward: the lead pumps
                _cvRiders.Clear();
                _cvRiders.AddRange(party.Active.Where(a => a.Cart == rc.Cart));
                _cvFallsBefore = rc.Falls;
                _cvGroundBefore = rc.GroundFree;
                _cvRearOnT = _cvCollapseT = _cvCallT = _cvBrakeCallT = _cvTakeT = _cvArriveT = _cvAllGoneT = _cvEarliestTakeT = _cvRunawayT = float.NaN;
                _cvMinGap = float.PositiveInfinity;
                _cvHoldChecked = _cvBraking = _cvStalled = false;
                _cvEbPhase = 0;
                _cvEbAt = _cvFreshT = float.NaN;
                GD.Print($"[cavern] pass {_cvPass} departed stop 3 at S={rc.State.S:0.0}: {_cvRiders.Count} rider(s), {rc.Train.Count} wagon(s), the lead pumping={v == "runaway"}");
                _cvStep++; _cvClock = 0f;
                break;
            }
            case 4 when _cvPass == 2:
            {
                // =brake after the rise: the next departure, unbraked, reaches stop 4
                party.ForcedMoveInput = null;
                if (float.IsNaN(_cvRearOnT) && rc.State.Mode == CartMode.Rolling && rear >= from) _cvRearOnT = _cvTime;
                if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == 4)
                {
                    if (enc.BridgeTakes != 0 || rc.Falls != _cvFallsBefore) { Fail($"after the rise: takes {enc.BridgeTakes}, falls {rc.Falls - _cvFallsBefore}"); return; }
                    Pass($"{_cvTakeSummary}; every rider fell; the wipe rose at stop 3 with the bridge whole; the next departure, unbraked, reached stop 4");
                    return;
                }
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != 3) { Fail($"after the rise the train left ride 4 another way (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
                if (_cvClock > 120f) Fail($"after the rise ride 4 did not reach stop 4 in 120 s (S={rc.State.S:0.0})");
                break;
            }
            case 4 when LeapBranch:
                CavernLeap(layout, rc, enc, party, me, Fail, Pass);
                break;
            case 4:
                CavernRide(rc, enc, party, me, rear, from, Fail);
                break;
            case 5:
                CavernAfterArrival(layout, rc, enc, Fail, Pass);
                break;
            case 6:
            {
                // =brake: the take, every rider fallen within 1 s (the wipe may have built the floor again already: the falls are the wreck's)
                if (_cvFallsAtWreck != _cvFallsBefore + 1) { Fail($"the take: falls {_cvFallsAtWreck - _cvFallsBefore}, not 1"); return; }
                if (_cvWiped && _cvFallenAtWreck < _cvRiders.Count) { Fail($"{_cvRiders.Count - _cvFallenAtWreck} of {_cvRiders.Count} riders not fallen at the take, and the party wiped"); return; }
                var standing = _cvFallenAtWreck == _cvRiders.Count || _cvWiped ? new List<Adventurer>() : _cvRiders.Where(a => !a.State.Fallen).ToList();
                if (standing.Count > 0) { if (_cvClock > 1f) Fail($"within 1 s of the take {string.Join(", ", standing.Select(a => a.State.Name))} not fallen"); break; }
                GD.Print($"[cavern] {_cvVariant}: every rider fallen [{string.Join(", ", _cvRiders.Select(a => a.State.Name))}] ({_cvFallenAtWreck} of {_cvRiders.Count} at the take itself)");
                party.ForcedMoveInput = null;
                _cvStep = 7; _cvClock = 0f;
                break;
            }
            case 7:
                if (!_cvWiped) { if (_cvClock > 5f) Fail("the party did not wipe within 5 s of the take"); break; }
                GD.Print($"[cavern] {_cvVariant}: the party has fallen: the wipe");
                _cvStep = 8; _cvClock = 0f;
                break;
            case 8:
            {
                if (rc == _cvWipeRc || main.Rebuilding) { if (_cvClock > 10f) Fail("the floor was not built again within 10 s of the wipe"); break; }
                _cvMayFall.Clear(); // risen: nobody may fall now
                var problems = new List<string>();
                if (rc.StopIndex != 3 || rc.State.Mode != CartMode.Held) problems.Add($"the cart {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 3");
                foreach (var a in party.Active)
                    if (a.State.Fallen || a.Downed) problems.Add($"{a.State.Name} fallen={a.State.Fallen} downed={a.Downed}");
                if (BridgeWhole(layout, enc) is { } broken) problems.Add(broken);
                if (problems.Count > 0) { if (_cvClock > 10f) Fail($"the rise: {string.Join("; ", problems)}"); break; }
                GD.Print($"[cavern] {_cvVariant}: the rise at stop {rc.StopIndex} after {_cvClock:0.0} s with the bridge whole ({enc.BridgeCells} cells, none gone)");
                _cvPass = 2;
                _cvStep = 1; _cvClock = 0f; // board and depart again
                break;
            }
        }
    }

    /// <summary>The build (every variant): the live bridge, the party track's gaps and the join at both lips. Null, or why not.</summary>
    private string? CavernBuild(RailController rc, RailEncounterController enc)
    {
        if (enc.SetPieceNode("r4_bridge") is not CavernBridge bridge) return "r4_bridge is not built live";
        GD.Print($"[cavern] floor 12 ({_cvVariant}): r4_bridge live at party S={enc.BridgeFromS:0.0}..{enc.BridgeToS:0.0}, {enc.BridgeCells} cells; stops [{string.Join(",", rc.StopS.Select(s => s.ToString("0.0")))}]; lethality {TierRules.Lethality}");
        if (Mathf.Abs(enc.BridgeFromS - 903.1f) > 0.1f || Mathf.Abs(enc.BridgeToS - 991.1f) > 0.1f || enc.BridgeCells != 22) return $"the bridge spans S={enc.BridgeFromS:0.0}..{enc.BridgeToS:0.0} with {enc.BridgeCells} cells, not 903.1..991.1 with 22";
        var track = rc.Party.Track.Track;
        var gaps = track.Gaps.OrderBy(g => g.from).ToList();
        GD.Print($"[cavern] party track bare over: {string.Join(", ", gaps.Select(g => $"{g.from:0.0}..{g.to:0.0}"))}");
        if (!gaps.Any(g => g.from == enc.BridgeFromS && g.to == enc.BridgeToS)) return "the party track is not bare over the bridge's span";
        var jump = enc.SetPieceNode("r5_jump") is RailGap gap ? gap : null;
        if (jump == null || !gaps.Any(g => Mathf.Abs(g.from - jump.From) < 0.01f && Mathf.Abs(g.to - jump.To) < 0.01f)) return "the party track is not bare over r5_jump's gap";
        if (Main.Instance.Builder.EnemyTrack is { } enemy) GD.Print($"[cavern] enemy track bare over: {string.Join(", ", enemy.Gaps.OrderBy(g => g.from).Select(g => $"{g.from:0.0}..{g.to:0.0}"))}");
        var (fromMm, toMm) = MeasureJoin(bridge, track);
        GD.Print($"[cavern] join: the bridge's first rail ring against the track's at S={bridge.From:0.0}: {fromMm:0.000} mm; its last at S={bridge.To:0.0}: {toMm:0.000} mm");
        if (!(fromMm <= 1f) || !(toMm <= 1f)) return $"the bridge's rails do not meet the track's within 1 mm (from {fromMm:0.000}, to {toMm:0.000})";
        if (enc.BridgeGone != 0 || ChasmDressing.Instance is null) return $"the bridge is not whole at the build ({enc.BridgeGone} gone) or there is no chasm dressing";
        if (!LeapBranch) return null;
        GD.Print($"[cavern] r4_leap: stretch party S={enc.LeapFromS:0.0}..{enc.LeapDropS:0.0} ({enc.LeapDropS - enc.LeapFromS:0.0} m), the slag line's drop at enemy S={enc.LeapDropEnemyS:0.0}..{enc.LeapDropFarEnemyS:0.0}");
        if (!(Mathf.Abs(enc.LeapFromS - 913.1f) <= 0.1f) || !(Mathf.Abs(enc.LeapDropS - 975.1f) <= 0.1f) || float.IsNaN(enc.LeapDropEnemyS)) return $"r4_leap is not live over party S 913.1..975.1 (S={enc.LeapFromS:0.0}..{enc.LeapDropS:0.0}, drop enemy S={enc.LeapDropEnemyS:0.0})";
        return null;
    }

    /// <summary>Ride 4 from the departure at stop 3: the rear on, the collapse's start and its call, each variant's own, the arrival at stop 4.</summary>
    private void CavernRide(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, float rear, float from, Action<string> Fail)
    {
        var v = _cvVariant;
        if (v == "runaway") party.ForcedMoveInput = new Vector2(0, -1);
        if (float.IsNaN(_cvRearOnT) && rc.State.Mode == CartMode.Rolling && rear >= from) { _cvRearOnT = _cvTime; GD.Print($"[cavern] the train's rear on the bridge (S={rear:0.0}, front S={rc.Party.FrontS:0.0}, v {rc.State.V:0.00})"); }
        var going = !float.IsNaN(enc.CollapseS);
        if (going && float.IsNaN(_cvCollapseT))
        {
            _cvCollapseT = _cvTime;
            _cvEarliestTakeT = _cvTime + BridgeRules.ReactionTime; // no brake pressed before the call + the reaction time counts
            var since = _cvTime - _cvRearOnT;
            GD.Print($"[cavern] timing: bridge call t={(_cvCallT - _cvRearOnT):0.00}, earliest take t={(_cvEarliestTakeT - _cvRearOnT):0.00} (s since the rear came on; the collapse started at t={since:0.00} with the rear at S={rear:0.0})");
            if (float.IsNaN(_cvCallT) || Mathf.Abs(_cvCallT - _cvCollapseT) > 0.1f) { Fail($"the bridge's call did not come with the collapse's start (call {_cvCallT - _cvRearOnT:0.00}, start {since:0.00})"); return; }
            if (float.IsNaN(_cvRearOnT) || since < BridgeRules.Grace - 0.05f) { Fail($"the collapse started {since:0.00} s after the rear came on, under {BridgeRules.Grace} s"); return; }
        }
        if (going && rc.State.Mode == CartMode.Rolling && rc.StopIndex == 3 && rear <= enc.BridgeToS) _cvMinGap = Mathf.Min(_cvMinGap, rear - enc.CollapseS);
        if (going && float.IsNaN(_cvAllGoneT) && enc.BridgeGone == enc.BridgeCells) _cvAllGoneT = _cvTime;

        switch (v)
        {
            case "bridge":
                if (going && _cvMinGap < BridgeRules.HoldShort - 0.01f) { Fail($"the bridge's front came {_cvMinGap:0.00} m from the train's rear, under {BridgeRules.HoldShort} m"); return; }
                if (enc.HoldsRiders() && !_cvHoldChecked)
                {
                    _cvHoldChecked = true;
                    var prompt = rc.Cart.Prompt;
                    rc.Cart.Interact(party, me); // a "Get out" press, as the key would
                    GD.Print($"[cavern] bridge: held on it at front S={rc.Party.FrontS:0.0} (v {rc.State.V:0.00}): the seated lead's prompt \"{prompt}\", a get-out press leaves them seated={me.Cart == rc.Cart}");
                    if (prompt != "" || me.Cart != rc.Cart) { Fail("on the bridge at speed the lead was offered or let out"); return; }
                }
                break;
            case "brake":
                if (going && !_cvBraking && _cvTime - _cvCollapseT >= BridgeRules.ReactionTime + 0.1f && float.IsNaN(_cvTakeT))
                {
                    _cvBraking = true;
                    GD.Print($"[cavern] brake: the lead brakes {_cvTime - _cvCollapseT:0.00} s after the collapse started (front S={enc.CollapseS:0.0}, rear S={rear:0.0}, v {rc.State.V:0.00})");
                }
                if (_cvBraking && float.IsNaN(_cvTakeT)) party.ForcedMoveInput = new Vector2(0, 1);
                if (!float.IsNaN(_cvTakeT))
                {
                    if (TakeJudged(rc, enc, party, Fail) is not { } said) return;
                    _cvTakeSummary = $"the lead braked {BridgeRules.ReactionTime + 0.1f:0.0} s after the collapse started (its call at t={_cvCallT - _cvRearOnT:0.00} s after the rear came on, \"Don't stop\" at t={_cvBrakeCallT - _cvRearOnT:0.00}); {said}";
                    _cvStep = 6; _cvClock = 0f;
                    return;
                }
                break;
            case "early-brake":
            {
                // held short: the front never past the bridge's start or HoldShort behind the rear, whichever is further on (it starts at the start)
                var over = going && rc.State.Mode == CartMode.Rolling && rear <= enc.BridgeToS ? enc.CollapseS - Mathf.Max(from, rear - BridgeRules.HoldShort) : float.NegativeInfinity;
                if (_cvEbPhase == 0 && rc.State.Mode == CartMode.Rolling && rc.Party.FrontS >= from)
                {
                    _cvEbPhase = 1;
                    GD.Print($"[cavern] early-brake: the lead brakes as the bridge groans (front S={rc.Party.FrontS:0.0}, rear S={rear:0.0}, v {rc.State.V:0.00})");
                }
                if (_cvEbPhase is 1 or 2 && float.IsNaN(_cvTakeT) && over > 0.01f) { Fail($"braked before the window, the front ran {over:0.00} m past its hold (front S={enc.CollapseS:0.0}, rear S={rear:0.0})"); return; }
                if (_cvEbPhase is 1 or 2 && !float.IsNaN(_cvTakeT)) { Fail($"the bridge took the train braked before its window (phase {_cvEbPhase}, {_cvTakeT - _cvCallT:0.00} s after the call)"); return; }
                if (_cvEbPhase == 1 && !going && _cvClock > 40f) { Fail("the collapse never started under the braked train"); return; }
                if (_cvEbPhase == 1 && going && _cvTime - _cvCallT >= BridgeRules.ReactionTime + 1.5f)
                {
                    _cvEbPhase = 2;
                    _cvEbAt = _cvTime;
                    GD.Print($"[cavern] early-brake: braked from the groan to {_cvTime - _cvCallT:0.00} s after the call: held short and alive (front S={enc.CollapseS:0.0}, rear S={rear:0.0}, gap {rear - enc.CollapseS:0.00} m, v {rc.State.V:0.00}, takes {enc.BridgeTakes}, falls {rc.Falls - _cvFallsBefore}, \"Don't stop\" heard {!float.IsNaN(_cvBrakeCallT)}); the lead lets go");
                }
                if (_cvEbPhase == 2 && _cvTime - _cvEbAt >= 1f)
                {
                    _cvEbPhase = 3;
                    _cvFreshT = _cvTime;
                    GD.Print($"[cavern] early-brake: let go 1 s, no take (front S={enc.CollapseS:0.0}, rear S={rear:0.0}, v {rc.State.V:0.00}); a fresh brake {_cvTime - _cvCallT:0.00} s after the call");
                }
                party.ForcedMoveInput = _cvEbPhase is 1 or 3 && float.IsNaN(_cvTakeT) ? new Vector2(0, 1) : null;
                if (_cvEbPhase == 3 && !float.IsNaN(_cvTakeT))
                {
                    if (TakeJudged(rc, enc, party, Fail) is not { } said) return;
                    if (_cvTakeT < _cvFreshT) { Fail("the take came before the fresh brake"); return; }
                    _cvTakeSummary = $"braked from the groan through the call and 3 s past it, the train was held short and alive, no take; let go 1 s, no take; a fresh brake {_cvFreshT - _cvCallT:0.00} s after the call: {said}, {_cvTakeT - _cvFreshT:0.00} s after the fresh press";
                    _cvStep = 6; _cvClock = 0f;
                    return;
                }
                if (_cvEbPhase == 3 && _cvTime - _cvFreshT > 15f) { Fail("no take within 15 s of the fresh brake"); return; }
                break;
            }
            case "stall":
                if (going && !_cvStalled && rear >= from + 6f)
                {
                    _cvStalled = true;
                    rc.DebugStall = true;
                    GD.Print($"[cavern] stall: the train caught with its rear at S={rear:0.0} ({rear - from:0.0} m on the bridge), the collapse's front at S={enc.CollapseS:0.0}");
                }
                break;
            case "runaway":
                if (enc.BridgeRunawayTaken is { } taken && float.IsNaN(_cvRunawayT))
                {
                    _cvRunawayT = _cvTime;
                    GD.Print($"[cavern] runaway: it went down with the bridge with its front at S={taken.FrontS:0.0} ({taken.FrontS - from:0.0} m past the start), rammed={taken.Rammed} plunged={taken.Plunged}; rams {enc.RunawayRams}");
                    if (taken.FrontS < from || taken.FrontS > from + 8f) { Fail($"the runaway went down with its front {taken.FrontS - from:0.0} m past the bridge's start, not 0-8"); return; }
                    if (taken.Rammed || taken.Plunged) { Fail("the runaway taken by the bridge was flagged rammed or plunged (a client would show a ram)"); return; }
                    if (enc.RideRunawayCart != null) { Fail("the runaway outlived the bridge"); return; }
                }
                break;
        }
        if (enc.BridgeTakes > 0 && !BrakeBranch) { Fail($"the bridge took the train ({enc.BridgeTakes})"); return; }
        if (rc.Falls != _cvFallsBefore && !BrakeBranch) { Fail("the train fell"); return; }
        if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == 4)
        {
            party.ForcedMoveInput = null;
            _cvArriveT = _cvTime;
            _cvFrontAtArrival = enc.CollapseS;
            GD.Print($"[cavern] {v}: arrived at stop 4 after {_cvClock:0.0} s: the bridge {enc.BridgeGone}/{enc.BridgeCells} gone (front S={enc.CollapseS:0.0}), the front's least gap to the rear {_cvMinGap:0.00} m, takes {enc.BridgeTakes}, rams {enc.RunawayRams}, grinds {rc.GroundFree - _cvGroundBefore}");
            if (float.IsNaN(_cvCollapseT)) { Fail("the bridge never started to go"); return; }
            switch (v)
            {
                case "bridge" when !_cvHoldChecked: Fail("the train was never held on the bridge (HoldsRiders)"); return;
                case "stall" when !_cvStalled || !enc.BridgeHeldByStall || rc.GroundFree <= _cvGroundBefore: Fail($"stall: stalled {_cvStalled}, the hold line {enc.BridgeHeldByStall}, grinds {rc.GroundFree - _cvGroundBefore}"); return;
                case "runaway" when float.IsNaN(_cvRunawayT) || enc.RunawayRams != 0: Fail($"runaway: taken by the bridge {!float.IsNaN(_cvRunawayT)}, rams {enc.RunawayRams}"); return;
            }
            _cvStep = 5; _cvClock = 0f;
            return;
        }
        if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != 3) { Fail($"the train left ride 4 another way (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
        if (_cvClock > 120f) Fail($"ride 4 did not reach stop 4 in 120 s (S={rc.State.S:0.0}, front S={rc.Party.FrontS:0.0})");
    }

    /// <summary>
    /// A brake variant's take, the frame after it: once, "Don't stop" called before it, at or after the call + the reaction time, by a press
    /// made at or after the call + the reaction time. What to say of it, or null (failed).
    /// </summary>
    private string? TakeJudged(RailController rc, RailEncounterController enc, PartyController party, Action<string> Fail)
    {
        party.ForcedMoveInput = null;
        var pressed = rc.BrakePressedAt - enc.BridgeCalledAt;
        GD.Print($"[cavern] {_cvVariant}: the bridge took the train {_cvTakeT - _cvCallT:0.00} s after its call (t={_cvTakeT - _cvRearOnT:0.00}; earliest take t={_cvEarliestTakeT - _cvRearOnT:0.00}), the brake pressed {pressed:0.00} s after the call; \"Don't stop\" heard at t={_cvBrakeCallT - _cvRearOnT:0.00}; takes {enc.BridgeTakes}, falls {_cvFallsAtWreck - _cvFallsBefore}");
        if (enc.BridgeTakes != 1) { Fail($"the bridge's takes are {enc.BridgeTakes}, not 1"); return null; }
        if (float.IsNaN(_cvBrakeCallT) || _cvBrakeCallT > _cvTakeT) { Fail("\"Don't stop\" was not called before the take"); return null; }
        if (_cvTakeT - _cvCallT < BridgeRules.ReactionTime) { Fail($"the take came {_cvTakeT - _cvCallT:0.00} s after the call, under {BridgeRules.ReactionTime} s"); return null; }
        if (!(pressed >= BridgeRules.ReactionTime)) { Fail($"the taking brake was pressed {pressed:0.00} s after the call, under the reaction time {BridgeRules.ReactionTime} s"); return null; }
        return $"the bridge took the train {_cvTakeT - _cvCallT:0.00} s after its call, the brake pressed {pressed:0.00} s after it";
    }

    /// <summary>After the arrival at stop 4: every party cell gone within 6 s, then (=bridge) a restore stands it whole.</summary>
    private void CavernAfterArrival(FloorLayout layout, RailController rc, RailEncounterController enc, Action<string> Fail, Action<string> Pass)
    {
        if (float.IsNaN(_cvAllGoneT) && enc.BridgeGone == enc.BridgeCells) _cvAllGoneT = _cvTime;
        // within 6 s of the arrival (=bridge, =runaway); after a stall's grind the front runs on from where it stood to the end at its pace (Ruling 5)
        var within = _cvVariant == "stall" ? (enc.BridgeToS - _cvFrontAtArrival) / BridgeRules.Pace(rc.Train.Count) + 1f : 6f;
        if (float.IsNaN(_cvAllGoneT)) { if (_cvTime - _cvArriveT > within) Fail($"{enc.BridgeGone}/{enc.BridgeCells} cells gone {within:0.0} s after the arrival (front S={enc.CollapseS:0.0})"); return; }
        if (enc.SetPieceNode("r4_bridge") is not CavernBridge bridge) { Fail("the bridge is gone"); return; }
        var notGone = bridge.Cells.Where(c => !layout.GoneBridges.Contains(c.Cell) || DeckShape(c.Cell) is not { Disabled: true }).Select(c => c.Cell.ToString()).ToList();
        GD.Print($"[cavern] {_cvVariant}: the last cell went {_cvAllGoneT - _cvArriveT:+0.00;-0.00} s from the arrival; GoneBridges {layout.GoneBridges.Count}, cells not gone or with a live footing [{string.Join(", ", notGone)}]");
        if (notGone.Count > 0) { Fail($"cells not gone: {string.Join(", ", notGone)}"); return; }
        if (_cvVariant != "bridge")
        {
            Pass(_cvVariant switch
            {
                "stall" => $"the train caught on the bridge after the collapse started, nobody braking: the stall guard held the front 2 m short (least gap {_cvMinGap:0.00} m), no take; the watchdog freed the train to stop 4; every cell gone",
                _ => $"the runaway went down with the bridge, neither rammed nor plunged; no ram; arrived at stop 4; every cell gone",
            });
            return;
        }
        rc.Restore(3);
        if (BridgeWhole(layout, enc) is { } broken) { Fail($"a restore after the collapse: {broken}"); return; }
        GD.Print($"[cavern] bridge: rc.Restore(3) stands it whole (GoneBridges {layout.GoneBridges.Count}, every Deck_ shape enabled)");
        Pass($"the call came with the collapse's start {(_cvCollapseT - _cvRearOnT):0.00} s after the rear came on; its front never closer than {_cvMinGap:0.00} m to the rear; no Get out on it at speed; every cell gone {_cvAllGoneT - _cvArriveT:+0.00;-0.00} s from the arrival; a restore stands it whole");
    }

    /// <summary>Null when the bridge stands whole (none gone, no cell in GoneBridges, every Deck_ shape enabled), else what is not.</summary>
    private static string? BridgeWhole(FloorLayout layout, RailEncounterController enc)
    {
        if (enc.SetPieceNode("r4_bridge") is not CavernBridge bridge) return "no live bridge";
        var bad = bridge.Cells.Where(c => layout.GoneBridges.Contains(c.Cell) || DeckShape(c.Cell) is not { Disabled: false }).Select(c => c.Cell.ToString()).ToList();
        return enc.BridgeGone == 0 && bad.Count == 0 ? null : $"the bridge not whole ({enc.BridgeGone} gone; cells [{string.Join(", ", bad)}])";
    }

    /// <summary>
    /// The join at both lips, in the engine: the largest distance (mm) from a vertex of the bridge's first cell's first rail ring at its
    /// <c>From</c>, and of its last cell's last ring at its <c>To</c>, to the nearest vertex of <paramref name="track"/>'s own rail meshes. Headless
    /// (no laid cells) the two end cells are laid into a probe node with the real <see cref="RailTrack.LaySpan"/> and freed after; NaN when
    /// nothing can be measured.
    /// </summary>
    private static (float FromMm, float ToMm) MeasureJoin(CavernBridge bridge, Dungeon.Props.RailTrack track)
    {
        if (bridge.Count == 0 || track.GetNodeOrNull<Node3D>("Rails") is not { } rails) return (float.NaN, float.NaN);
        var probe = new Node3D { Name = "JoinProbe" };
        bridge.AddChild(probe);
        try
        {
            var first = SpanRails(bridge, track, probe, 0, "JoinFrom");
            var last = SpanRails(bridge, track, probe, bridge.Count - 1, "JoinTo");
            var trackVerts = new List<Vector3>();
            foreach (var child in rails.GetChildren())
                if (child is MeshInstance3D mi) trackVerts.AddRange(Vertices(mi));
            return (RingGap(track, first, bridge.From, trackVerts), RingGap(track, last, bridge.To, trackVerts));
        }
        finally
        {
            bridge.RemoveChild(probe);
            probe.Free();
        }
    }

    /// <summary>Cell <paramref name="k"/>'s rail vertices in global space: its laid mesh, or one laid into <paramref name="probe"/> now.</summary>
    private static List<Vector3> SpanRails(CavernBridge bridge, Dungeon.Props.RailTrack track, Node3D probe, int k, string name)
    {
        var mi = bridge.LookOf(k)?.GetNodeOrNull<MeshInstance3D>("Deck_Rails");
        if (mi == null)
        {
            track.LaySpan(probe, bridge.Cells[k].FromS, bridge.Cells[k].ToS, name);
            mi = probe.GetNodeOrNull<MeshInstance3D>($"{name}_Rails");
        }
        return mi == null ? new List<Vector3>() : Vertices(mi);
    }

    private static List<Vector3> Vertices(MeshInstance3D mi)
    {
        var verts = new List<Vector3>();
        if (mi.Mesh is not { } mesh) return verts;
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            foreach (var v in mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) verts.Add(mi.GlobalTransform * v);
        return verts;
    }

    /// <summary>The largest distance (mm) from a span vertex of the ring at <paramref name="s"/> (within 1 cm of its plane) to the nearest track vertex; NaN with none.</summary>
    private static float RingGap(Dungeon.Props.RailTrack track, List<Vector3> span, float s, List<Vector3> trackVerts)
    {
        var toFrame = (track.GlobalTransform * track.Curve.SampleBakedWithRotation(s, true, true)).AffineInverse();
        var worst = float.NaN;
        foreach (var v in span)
        {
            if (Mathf.Abs((toFrame * v).Z) > 0.01f) continue; // not the ring at s
            var best = float.MaxValue;
            foreach (var t in trackVerts) best = Mathf.Min(best, t.DistanceSquaredTo(v));
            var mm = Mathf.Sqrt(best) * 1000f;
            worst = float.IsNaN(worst) ? mm : Mathf.Max(worst, mm);
        }
        return worst;
    }

    /// <summary>The footing box of cavern deck <paramref name="cell"/>, or null.</summary>
    private static CollisionShape3D? DeckShape(GridPos cell) => ChasmDressing.Instance?.GetNodeOrNull<CollisionShape3D>($"Footings/Deck_{cell.X}_{cell.Y}");

    // ---- =leap and =wreck (Task 4) -----------------------------------------------------------------------------------------

    /// <summary>One of r4_slag's riders, each frame: stunned (it strikes nobody) and shielded (the companions' shots do not kill it), unless it is the blow's target.</summary>
    private void CavernSpare(Enemies.Enemy e)
    {
        e.ApplyStatus(new Combat.StatusApplication(Combat.StatusKind.Stun, 0.5f, 0f));
        if (e != _cvBlowTarget) e.ApplyStatus(new Combat.StatusApplication(Combat.StatusKind.Shield, 1f, Combat.StatusEffectController.MaxShield));
    }

    /// <summary>
    /// Ride 4 for =leap and =wreck: the stretch's start, the offer to the lead (and never to a companion), the leap across and its checks, then
    /// =leap's pump and brake, the blow, the leap back and the arrival; =wreck's wreck and the set-down.
    /// </summary>
    private void CavernLeap(FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail, Action<string> Pass)
    {
        var v = _cvVariant;
        if (!_cvHurtHooked)
        {
            _cvHurtHooked = true;
            Action<Enemies.Enemy, float, Combat.HitFlavor, int> hurt = (e, amount, _, by) =>
            {
                if (_done || !_testCavern || e != _cvBlowTarget || amount <= 0f || Main.Instance?.Party?.Local is not { } lead || by != lead.Index) return;
                _cvBlowHit = true;
            };
            EventBus.Instance.EnemyHurt += hurt;
            _staticUnhooks.Add(() => EventBus.Instance.EnemyHurt -= hurt);
        }
        if (_cvLeapPhase == 0)
        {
            _cvStretchT = _cvAcrossT = _cvPhaseT = _cvWreckFront = _cvDropHeardT = _cvDropCallT = _cvDropT = _cvLeapArriveT = float.NaN;
            _cvSlag = null; _cvBlowTarget = null; _cvBlowHit = _cvWreckDone = _cvHaulLog = _cvGoesOverLog = _cvFellChecked = false; _cvSwings = 0; _cvLeapPhase = 1;
            _cvLead = me; // party.Local passes on when the lead falls (=stay): the lead is this hero throughout
            _cvSlagRiders.Clear();
        }
        me = _cvLead!;
        var s = rc.State.S;
        if (v != "wreck" && CavernDropWatch(layout, rc, enc, Fail) == false) return;
        var inStretch = s >= enc.LeapFromS && s < enc.LeapDropS;
        if (inStretch && float.IsNaN(_cvStretchT)) { _cvStretchT = _cvTime; GD.Print($"[cavern] {v}: the party's cart reaches the stretch at S={s:0.0} (v {rc.State.V:0.00}); slag carts on the line {enc.SlagCarts.Count}"); }
        if (inStretch || _cvLeapPhase < 6)
            foreach (var mate in party.Active)
            {
                if (mate.Control != ControlKind.Companion) continue;
                if (inStretch && enc.LeapOfferFor(mate) != null) { Fail($"companion {mate.State.Name} was offered a leap at S={s:0.0}"); return; }
                if (mate.Cart is SlagCart) { Fail($"companion {mate.State.Name} boarded a slag cart"); return; }
            }
        if (rc.Falls != _cvFallsBefore && v != "wreck") { Fail("the train fell"); return; }
        if (enc.BridgeTakes > 0) { Fail($"the bridge took the train ({enc.BridgeTakes})"); return; }
        switch (_cvLeapPhase)
        {
            case 1: // the offer, then the leap across
            {
                if (float.IsNaN(_cvStretchT)) { if (rc.StopIndex != 3 || rc.State.Mode != CartMode.Rolling || _cvClock > 120f) Fail($"the train never reached the stretch (S={s:0.0}, mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
                if (enc.LeapOfferFor(me) is not SlagCart slag || me.LookingAt != slag)
                {
                    if (_cvTime - _cvStretchT > 2f)
                    {
                        var r4 = enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r4_slag");
                        var gap = r4 != null ? enc.LeapGap(r4) : (float.NaN, float.NaN);
                        Fail($"\"Leap across\" not offered within 2 s of the stretch's start (S={s:0.0}; r4_slag {(r4 == null ? "absent" : $"lateral {gap.Item1:0.00} m, along {gap.Item2:0.00} m, leapers {r4.Leapers}")}; looking at {me.LookingAt?.GetType().Name ?? "nothing"})");
                    }
                    return;
                }
                var (lateral, along) = enc.LeapGap(slag);
                var prompt = slag.Prompt;
                GD.Print($"[cavern] {v}: \"{prompt}\" offered {_cvTime - _cvStretchT:0.00} s after the stretch's start at S={s:0.0}: {slag.EncounterId} (cart {slag.CartId}) lateral {lateral:0.00} m (rim gap {Mathf.Abs(lateral) - 2f * CartRules.HalfWidth:0.00}), along {along:+0.00;-0.00} m");
                if (slag.EncounterId != "r4_slag" || !(Mathf.Abs(along) <= LeapRules.AlongReach) || prompt != LeapRules.AcrossPrompt) { Fail($"the offer: {slag.EncounterId}, along {along:0.00} m, prompt \"{prompt}\""); return; }
                me.LookingAt.Interact(party, me);
                var problems = new List<string>();
                if (me.Cart != slag) problems.Add($"the lead rides {(me.Cart as Node)?.Name ?? "nothing"}, not the slag cart");
                if (slag.Seats.OccupantOf(0) != me.Index) problems.Add($"deck seat 0 holds {slag.Seats.OccupantOf(0)?.ToString() ?? "nobody"}");
                if (rc.Cart.Riders.ContainsKey(me.RiderId) || rc.Cart.Seats.SeatOf(me.RiderId) != null) problems.Add("the lead is still in the party cart's book");
                if (enc.Leaps != 1 || slag.Leapers != 1) problems.Add($"Leaps {enc.Leaps}, leapers {slag.Leapers}");
                if (problems.Count > 0) { Fail($"after the leap across: {string.Join("; ", problems)}"); return; }
                _cvSlag = slag;
                _cvAcrossT = _cvPhaseT = _cvTime;
                GD.Print($"[cavern] {v}: the lead leapt across at S={s:0.0}: on {slag.EncounterId}'s deck seat {me.Seat}, out of the party cart's book ({rc.Cart.Riders.Count} rider(s) there)");
                if (v == "wreck") party.ForcedMoveInput = null;
                _cvLeapPhase = v switch { "wreck" => 10, "stay" => 20, "lone" => 30, _ => 2 };
                if (v == "stay") _cvMayFall.Add(me.State.Name); // the lead stays on the deck and goes over with it
                if (v == "lone")
                {
                    party.AllowPossession = false; // else solo hands the lead to a seated companion: the downed lead stays the human, judged at the drop
                    me.State.Mangled = true;       // held down until the drop (calm auto-revive never stands a Mangled member up)
                    me.State.Hp = 0;
                    party.OnMemberDowned(me.Index);
                    GD.Print($"[cavern] lone: the lead {me.State.Name} downed on the deck at S={s:0.0}: downed={me.Downed} on the slag cart={me.Cart == slag} control={me.Control}");
                    if (!me.Downed || me.Cart != slag) { Fail("the lead is not downed on the slag cart's deck"); return; }
                }
                return;
            }
            case 2: // the leaper's stick pumps nothing, then brakes nothing
            {
                var t = _cvTime - _cvPhaseT;
                party.ForcedMoveInput = t < 0.5f ? new Vector2(0, -1) : t < 1f ? new Vector2(0, 1) : null;
                if (t > 0.15f && t < 0.5f && (rc.PlayersPumping != 0 || me.Pumping)) { Fail($"the leaper's pump reached the train (players pumping {rc.PlayersPumping}, lead pumping {me.Pumping})"); return; }
                if (t > 0.65f && t < 1f && rc.Braking) { Fail("the leaper's brake reached the train"); return; }
                if (t < 1.05f) return;
                GD.Print($"[cavern] {v}: on the deck the lead's stick held forward 0.5 s then back 0.5 s: the train read no pump (players pumping {rc.PlayersPumping}) and no brake");
                _cvLeapPhase = 3; _cvPhaseT = _cvTime;
                return;
            }
            case 3: // a seated blow from the deck
            {
                if (_cvBlowHit && _cvBlowTarget is { } hit)
                {
                    GD.Print($"[cavern] {v}: a seated blow from the deck hit {hit.DisplayName}: HP {_cvBlowHpBefore:0} -> {hit.Hp:0} after {_cvSwings} swing(s)");
                    if (!(hit.Hp < _cvBlowHpBefore)) { Fail($"the blow did not lower {hit.DisplayName}'s HP ({_cvBlowHpBefore:0} -> {hit.Hp:0})"); return; }
                    _cvBlowTarget = null;
                    _cvLeapPhase = 4;
                    return;
                }
                if (_cvTime - _cvAcrossT > 2.9f) { Fail($"no seated blow from the deck landed in {_cvSwings} swing(s) (target {_cvBlowTarget?.DisplayName ?? "none"})"); return; }
                if (_cvBlowTarget == null || !_cvBlowTarget.IsAlive)
                {
                    _cvBlowTarget = _cvSlag!.Aboard.Where(e => IsInstanceValid(e) && e.IsAlive).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(me.Camera.GlobalPosition)).FirstOrDefault();
                    if (_cvBlowTarget == null) { Fail("r4_slag has no living rider to strike"); return; }
                    _cvBlowTarget.Effects.Remove(Combat.StatusKind.Shield);
                    _cvBlowHpBefore = _cvBlowTarget.Hp;
                }
                if (me.State.CooldownRemaining > 0f || me.BladeSwinging || _cvTime - _cvPhaseT < 0.1f) return;
                BarricadeAim(me, _cvBlowTarget.GlobalPosition + Vector3.Up * 0.9f);
                party.Attack(Array.IndexOf(party.Adventurers, me));
                _cvSwings++;
                _cvPhaseT = _cvTime;
                return;
            }
            case 4: // the slag line's call: "Leap back" on its first frame offered after it
            {
                if (float.IsNaN(_cvDropCallT))
                {
                    if (_cvTime - _cvAcrossT > 15f) Fail($"the slag line's drop was not called within 15 s of the leap (party front S={rc.Party.FrontS:0.0}, the drop's party S {enc.LeapDropS:0.0})");
                    return;
                }
                if (enc.LeapOfferFor(me) != rc.Cart || me.LookingAt != rc.Cart)
                {
                    if (_cvTime - _cvDropCallT > 0.5f) Fail($"\"Leap back\" not offered 0.5 s after the slag line's call (offer {(enc.LeapOfferFor(me) as Node)?.Name ?? "none"}, looking at {me.LookingAt?.GetType().Name ?? "nothing"}, slag front S={_cvSlag?.Run.FrontS:0.0}, drop {enc.LeapDropEnemyS:0.0})");
                    return;
                }
                var prompt = rc.Cart.Prompt;
                var front = _cvSlag!.Run.FrontS;
                me.LookingAt.Interact(party, me);
                var seat = rc.Cart.Seats.SeatOf(me.RiderId);
                GD.Print($"[cavern] {v}: \"{prompt}\" pressed {_cvTime - _cvAcrossT:0.00} s after the leap, {_cvTime - _cvDropCallT:0.00} s after the slag line's call, at S={s:0.0} (the slag cart's front at enemy S={front:0.0}, {enc.LeapDropEnemyS - front:0.0} m short of the drop): the lead in the party cart's seat {seat?.ToString() ?? "none"}");
                if (prompt != LeapRules.BackPrompt || me.Cart != rc.Cart || seat is not (>= 0 and < CartSeats.HeroSeats) || _cvSlag.Riders.ContainsKey(me.RiderId) || enc.LeapsBack != 1) { Fail($"the leap back: prompt \"{prompt}\", in the party cart {me.Cart == rc.Cart}, seat {seat}, still on the deck {_cvSlag.Riders.ContainsKey(me.RiderId)}, LeapsBack {enc.LeapsBack}"); return; }
                _cvLeapSummary = $"\"Leap across\" offered {_cvAcrossT - _cvStretchT:0.00} s into the stretch and taken; a seated blow from the deck in {_cvSwings} swing(s); \"Leap back\" {_cvTime - _cvDropCallT:0.00} s after the slag line's call into seat {seat}";
                _cvLeapPhase = 5;
                return;
            }
            case 5: // r4_slag goes over the drop with nobody aboard, then on to stop 4
            {
                if (inStretch && enc.LeapOfferFor(me) != null) { Fail($"after the call the lead, back in the party cart, was offered {(enc.LeapOfferFor(me) as Node)?.Name} at S={s:0.0}"); return; } // no "Leap across" after the call
                if (float.IsNaN(_cvDropT)) return; // CavernDropWatch judges the drop
                if (enc.LeapDropFalls != 0 || me.State.Fallen || me.Cart != rc.Cart) { Fail($"after the drop: {enc.LeapDropFalls} hero(es) fell, the lead fallen={me.State.Fallen}, in the party cart {me.Cart == rc.Cart}"); return; }
                if (!CavernArrived(rc, enc, s, Fail)) return;
                GD.Print($"[cavern] {v}: arrived at stop 4: Leaps {enc.Leaps}, LeapsBack {enc.LeapsBack}, the lead seated in the party cart {me.Cart == rc.Cart}");
                if (enc.Leaps != 1 || enc.LeapsBack != 1) { Fail($"Leaps {enc.Leaps}, LeapsBack {enc.LeapsBack}"); return; }
                Pass($"{_cvLeapSummary}; the call heard {_cvDropT - _cvDropHeardT:0.00} s before r4_slag went over the drop with 0 heroes fallen and its {_cvSlagRiders.Count} rider(s) dead; the drop's decks gone, its lamp lit; no companion offered; stop 4 with one leap each way");
                return;
            }
            case 20: // =stay: the lead stays on the deck: over the drop with it, then back at stop 4
            {
                if (float.IsNaN(_cvDropT)) return;
                if (!_cvFellChecked)
                {
                    _cvFellChecked = true; // the frame of the drop: the lead fell with it
                    GD.Print($"[cavern] stay: at the drop the lead {me.State.Name} fallen={me.State.Fallen} ({enc.LeapDropFalls} hero(es) fell with it), seated in {(me.Cart as Node)?.Name ?? "nothing"}; falls {rc.Falls - _cvFallsBefore}; the local hero now {party.Local?.State.Name}");
                    if (!me.State.Fallen || enc.LeapDropFalls != 1 || me.Seated) { Fail("stay: the lead did not fall with the slag cart"); return; }
                }
                if (float.IsNaN(_cvLeapArriveT))
                {
                    if (!CavernArrived(rc, enc, s, Fail)) return;
                    _cvLeapArriveT = _cvTime;
                    GD.Print($"[cavern] stay: arrived at stop 4 {_cvTime - _cvDropT:0.00} s after the drop: falls {rc.Falls - _cvFallsBefore}, the lead fallen={me.State.Fallen}");
                    if (rc.Falls != _cvFallsBefore) { Fail($"the train fell ({rc.Falls - _cvFallsBefore})"); return; }
                }
                if (me.State.Fallen) { if (_cvTime - _cvLeapArriveT > 1f) Fail("stay: the lead not back within 1 s of the arrival at stop 4"); return; }
                var want = MathF.Max(1f, me.State.Stats[Characters.Stat.MaxHp] * CrevasseRules.ReturnHpFraction);
                GD.Print($"[cavern] stay: the lead {me.State.Name} back {_cvTime - _cvLeapArriveT:0.00} s after the arrival at HP {_cvBackHp:0.0} of {me.State.Stats[Characters.Stat.MaxHp]:0} as it climbed out (want {want:0.0}; {me.State.Hp:0.0} now), at cell {DungeonBuilder.WorldToCell(me.GlobalPosition)}");
                if (!(Mathf.Abs(_cvBackHp - want) <= 1f) || me.Downed) { Fail($"stay: the lead came back at HP {_cvBackHp:0.0}, not {want:0.0} (downed {me.Downed})"); return; }
                Pass($"the lead leapt across and stayed; the call heard {_cvDropT - _cvDropHeardT:0.00} s before the drop; the lead fell with the slag cart, nothing of the train fell; stop 4, the lead back {_cvTime - _cvLeapArriveT:0.00} s after the arrival at 25 % HP");
                return;
            }
            case 30: // =lone: downed on the deck before the call, hauled back at the drop
            {
                if (float.IsNaN(_cvDropCallT)) { if (!me.Downed || me.Cart != _cvSlag) { Fail($"lone: before the call the lead downed={me.Downed}, on the slag cart={me.Cart == _cvSlag}"); return; } return; }
                if (float.IsNaN(_cvDropT)) return;
                var seat = rc.Cart.Seats.SeatOf(me.RiderId);
                GD.Print($"[cavern] lone: at the drop the lead is in the party cart's seat {seat?.ToString() ?? "none"} (in its book {me.Cart == rc.Cart}), fallen={me.State.Fallen}, downed={me.Downed}; hauled {enc.LeapHauls}, fell {enc.LeapDropFalls}; the hauler's line logged {_cvHaulLog}");
                if (me.Cart != rc.Cart || seat is not (>= 0 and < CartSeats.HeroSeats) || me.State.Fallen || enc.LeapHauls != 1 || enc.LeapDropFalls != 0 || !_cvHaulLog) { Fail("lone: the downed lead was not hauled back into a party hero seat"); return; }
                me.State.Mangled = false; // past: calm auto-revive may stand the lead up again
                party.AllowPossession = true;
                _cvLeapPhase = 31;
                return;
            }
            case 31:
                if (!CavernArrived(rc, enc, s, Fail)) return;
                if (me.Cart != rc.Cart || me.State.Fallen) { Fail($"lone: at stop 4 the lead in the party cart {me.Cart == rc.Cart}, fallen {me.State.Fallen}"); return; }
                Pass($"possession off, the lead leapt across and was downed on the deck before the call; at the drop {(_cvHaulLog ? "a companion hauled" : "nobody hauled")} the lead back into a party hero seat, not fallen; r4_slag went over with its {_cvSlagRiders.Count} rider(s); stop 4");
                return;
            case 10: // =wreck: once the collapse has started, the train is wrecked under the lead on the slag cart
                if (float.IsNaN(enc.CollapseS) && _cvTime - _cvAcrossT < 6f) return;
                GD.Print($"[cavern] wreck: the train is wrecked at S={s:0.0} with the lead on {_cvSlag!.EncounterId}'s deck (the collapse's front S={enc.CollapseS:0.0})");
                rc.DebugWreck();
                _cvLeapPhase = 11; _cvPhaseT = _cvTime;
                return;
            case 11: // the wreck's event told (the slag cart freed, its leaper set down): the front is read, then 1 s later again
            {
                if (_cvSlag != null && IsInstanceValid(_cvSlag) && !_cvSlag.IsQueuedForDeletion())
                {
                    if (_cvTime - _cvPhaseT > 1f) Fail("the slag cart was not freed within 1 s of the wreck");
                    return;
                }
                if (!_cvWreckDone)
                {
                    _cvWreckDone = true;
                    _cvWreckFront = enc.CollapseS;
                    _cvPhaseT = _cvTime;
                    return;
                }
                var cell = DungeonBuilder.WorldToCell(me.GlobalPosition);
                var problems = new List<string>();
                if (me.Downed) problems.Add("downed");
                if (me.State.Fallen) problems.Add("fallen");
                if (me.Seated) problems.Add($"seated in {(me.Cart as Node)?.Name}");
                if (!CrevasseRules.SetDownOk(layout, cell)) problems.Add($"on cell {cell}, where no body may be set down");
                if (problems.Count > 0) { Fail($"after the wreck the lead is {string.Join(", ", problems)}"); return; }
                if (_cvTime - _cvPhaseT < 1f) return;
                var same = float.IsNaN(_cvWreckFront) ? float.IsNaN(enc.CollapseS) : Mathf.Abs(enc.CollapseS - _cvWreckFront) < 1e-4f;
                GD.Print($"[cavern] wreck: the lead set down up on cell {cell} (SetDownOk, not fallen, not seated); the collapse's front S={_cvWreckFront:0.0} at the wreck, S={enc.CollapseS:0.0} 1 s later ({(float.IsNaN(_cvWreckFront) ? "the spare's takeover stood the bridge again" : "stopped where it stood")}); the cart {rc.State.Mode} at stop {rc.StopIndex}");
                if (!same) { Fail($"the collapse's front moved after the wreck ({_cvWreckFront:0.0} -> {enc.CollapseS:0.0})"); return; }
                Pass($"the lead leapt across, the train was wrecked: the lead was set down off the freed slag cart up on cell {cell} (SetDownOk), not fallen, not seated; the collapse's front did not move in the 1 s after");
                return;
            }
        }
    }

    /// <summary>
    /// The leap flows' drop, each frame (Task 5): the call seen (r4_slag's riders noted), then r4_slag gone over: the timing line (the call heard
    /// 1.5 s or more before it), its riders dead, "The slag cart goes over into the crevasse!" logged, both twins in GoneBridges with their Deck_
    /// shapes disabled and the drop's lamp lit. False when the flow failed.
    /// </summary>
    private bool CavernDropWatch(FloorLayout layout, RailController rc, RailEncounterController enc, Action<string> Fail)
    {
        if (float.IsNaN(_cvDropCallT) && enc.LeapCalled)
        {
            _cvDropCallT = _cvTime;
            if (_cvSlag != null && IsInstanceValid(_cvSlag)) _cvSlagRiders.AddRange(_cvSlag.Aboard.Where(e => IsInstanceValid(e) && e.IsAlive));
            GD.Print($"[cavern] {_cvVariant}: the slag line's drop called at party front S={rc.Party.FrontS:0.0} ({enc.LeapDropS - rc.Party.FrontS:0.0} m short of it), by {_cvDropCalledBy ?? "the log"}; r4_slag's front at enemy S={_cvSlag?.Run.FrontS:0.0}, {_cvSlagRiders.Count} rider(s) aboard; the drop down {enc.Drop?.Dropped}");
            if (enc.Drop is not { Dropped: true }) { Fail("the drop was called but did not give way"); return false; }
        }
        if (float.IsNaN(_cvDropCallT) || !float.IsNaN(_cvDropT) || _cvSlag == null) return true;
        if (IsInstanceValid(_cvSlag) && !_cvSlag.IsQueuedForDeletion() && !_cvSlag.Derailed)
        {
            if (_cvTime - _cvDropCallT > 15f) { Fail($"r4_slag did not go over the drop within 15 s of the call (its front at enemy S={_cvSlag.Run.FrontS:0.0})"); return false; }
            return true;
        }
        _cvDropT = _cvTime;
        GD.Print($"[cavern] timing: drop call t={_cvDropHeardT - _cvStretchT:0.00}, drop t={_cvDropT - _cvStretchT:0.00} (s since the stretch's start; the call heard {_cvDropT - _cvDropHeardT:0.00} s before r4_slag went over)");
        var problems = new List<string>();
        if (float.IsNaN(_cvDropHeardT) || _cvDropT - _cvDropHeardT < 1.5f) problems.Add($"the call heard {_cvDropT - _cvDropHeardT:0.00} s before the drop, under 1.5 s");
        if (!_cvGoesOverLog) problems.Add("\"The slag cart goes over into the crevasse!\" not logged");
        var alive = _cvSlagRiders.Count(e => IsInstanceValid(e) && e.IsAlive);
        if (_cvSlagRiders.Count == 0 || alive > 0) problems.Add($"{alive} of r4_slag's {_cvSlagRiders.Count} rider(s) alive");
        if (enc.Drop is not { } drop) problems.Add("no live drop");
        else
        {
            var notGone = drop.Twins.Where(c => !layout.GoneBridges.Contains(c) || DeckShape(c) is not { Disabled: true }).Select(c => c.ToString()).ToList();
            GD.Print($"[cavern] {_cvVariant}: the drop's twins [{string.Join(", ", drop.Twins)}]: GoneBridges {layout.GoneBridges.Count} (the bridge's {enc.BridgeGone}), not gone or with a live footing [{string.Join(", ", notGone)}]; its lamp lit {drop.LampLit}; drops {enc.LeapDrops}, fell {enc.LeapDropFalls}, hauled {enc.LeapHauls}");
            if (drop.Twins.Count != LeapRules.DropCells || notGone.Count > 0) problems.Add($"the drop's cells not gone: [{string.Join(", ", notGone)}] of {drop.Twins.Count}");
            if (!drop.LampLit) problems.Add("the drop's lamp is dark");
        }
        if (problems.Count > 0) { Fail($"at the drop: {string.Join("; ", problems)}"); return false; }
        return true;
    }

    /// <summary>The leap flows' arrival: true once the party's cart is held at stop 4; fails if it left ride 4 another way or took 120 s.</summary>
    private bool CavernArrived(RailController rc, RailEncounterController enc, float s, Action<string> Fail)
    {
        if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == 4) return true;
        if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != 3) Fail($"the train left ride 4 another way (mode {rc.State.Mode}, stop {rc.StopIndex})");
        else if (_cvClock > 120f) Fail($"ride 4 did not reach stop 4 in 120 s (S={s:0.0})");
        return false;
    }
}
