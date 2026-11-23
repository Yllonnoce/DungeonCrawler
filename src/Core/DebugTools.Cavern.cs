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
// seated lead's prompt is "" and a "Get out" press is refused, and (plan 14, Task 3) no hero in the train is offered a move car to car
// (rc.MoveOfferFor, the lead looking back 120 degrees; its control on the ride before the bridge is offered) and a forced rc.Move is refused; every party cell is gone within 6 s of the arrival at stop 4 (GoneBridges
// holds them, their Deck_ shapes disabled; the flow says how long before or after the arrival the last went); a following rc.Restore(3)
// stands it whole. =brake: the lead brakes from 2.0 s after the collapse starts (past the reaction time, BridgeRules.ReactionTime): "Don't
// stop" is called before the take, the take comes 1.5 s or more after the call by a press made 1.5 s or more after it, rc.Falls rises by 1 with every rider fallen within 1 s
// (plan 14's final review M2: a companion rides W1 into the first pass's take, seated by rc.DebugSeat at the departure; its look rides down with
// a WagonDrop body and the lead's with the CartDrop: "[cavern] M2: at the take ..."), the wipe rises at stop 3 with the bridge whole, and the
// next departure, unbraked, reaches stop 4. =stall: the train is caught (rc.DebugStall) once the collapse has started (its rear 6 m or more on
// the bridge), nobody braking: the bridge is held by the stall guard, never takes the train, and the watchdog frees it to stop 4. =runaway:
// the runaway is real, the split left straight, the lead pumps: the runaway goes down with the bridge with its front within 8 m of the
// bridge's start, its body neither rammed nor plunged as it went; no ram; stop 4. =early-brake (Task 3 review I1): the lead brakes with the
// train's rear a cart's length short of the bridge (as the cart alone groaned onto it: plan 14 re-pinned it for the 12.6 m train) and holds it through the call and 3 s past it: held short (the front never past the bridge's start or 2 m behind the
// rear, whichever is further on), alive, no take; held there (enc.HoldsRiders, at any speed) the lead's prompt is "" and a "Get out" press
// is refused (the final review's I2); lets go
// for 1 s (no take), then a fresh brake: the take comes only after that press (made past the reaction time), then as =brake. =leap (Task 4):
// r4_slag's riders are spared (stunned and shielded: harmless, still targetable), the rest culled; the build has r4_leap live (stretch party
// S 913.1..975.1); "Leap across" is offered to the lead within 2 s of the stretch's start with r4_slag's along offset 1.2 m or less (printed),
// and never to a companion over the whole stretch, none of whom ever boards it; the lead presses it (LookingAt.Interact): seated on the slag
// cart's deck seat 0, out of the party cart's book, and its stick pumps and brakes nothing of the train; a seated blow from the deck lowers a
// rider's HP; "Leap back" is pressed (Task 5: on the slag line's call) and the lead sits in a party hero seat; stop 4 with Leaps == LeapsBack == 1. =wreck:
// the lead leaps across, then the train is wrecked (rc.DebugWreck) once the collapse has started (1 s in, past its sight): the lead is set down off the freed slag
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
// Task 6 (seen from the seat, Ruling 23), every variant, headless too: layer-1 rays from the seated eye, each with a control to 0.3 m under
// the deck at its target that must be blocked no more than 1 m short of where it crosses the deck's top: the bridge's front (+0.5 m) 1 s
// after the collapse starts (from the lead's eye), the slag cart's deck seat 0 on the first frame "Leap across" is offered, and the drop's
// near edge (+0.3 m) at the slag line's call from the leaper's eye (=leap, =stay); "[cavern] sight: {name} {clear at d m|blocked by X at d m}"
// and "... control blocked by X at d m"; a blocked ray, a vacuous control or a sight the variant never ran fails it. Windowed with
// --screenshot=PATH: =leap saves held A/B twins (the party's cart and the ride's slag carts pinned in S and V from each place until its shots
// are saved, 30 frames drawn before each, the HUD hidden) as PATH_{name}.png: behind/behind_standing (front at the bridge's start + 30 m,
// once the collapse's front holds short), side/side_nocart (the first offer), drop/drop_standing (the slag line's call), and judges
// "[cavern] shots: behind diff a px; side diff b px; drop diff c px, drop ember e px" (luma moved > 0.1 over x 20-80 %, y 16-80 %; a, b,
// c >= 400, e >= 20); for viewing it also saves spawn_before/spawn_after (party S 847 and 849), the joins (bridge_lip_from, bridge_lip_to,
// drop_lip_near, drop_joint, drop_lip_far, both pieces whole), deckseats/deckseats_marked (after the leap) and stop4/stop4_traffic (at stop
// 4, looking back at traffic's start past the dropped drop). =brake windowed shoots its take as it is seen falling (take_seat, take_side,
// take_seat_late).
public partial class DebugTools
{
    private bool _testCavern;
    private string _cvVariant = "bridge";
    private int _cvStep, _cvPass = 1;
    private float _cvClock, _cvTime;
    private bool _cvListening, _cvWiped, _cvHoldChecked, _cvBraking, _cvStalled, _cvMoveControl;
    private RailController? _cvHooked, _cvWipeRc;
    /// <summary>=brake's first pass: the companion seated in W1 for the take (the final review's M2), and where its look rode down.</summary>
    private Adventurer? _cvWagonRider, _cvTakeLead;
    private string? _cvWagonLook;
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
    /// <summary>=brake: the lead brakes this long after the collapse starts, 0.5 s past the reaction time (the final review's M2: 0.1 s was too
    /// little margin across the flow's clock and the session's).</summary>
    private const float CvBrakeAfter = BridgeRules.ReactionTime + 0.5f;
    /// <summary>=leap, =wreck, =stay and =lone: r4_slag's riders are spared (harmless and targetable) and the lead leaps across.</summary>
    private bool LeapBranch => _cvVariant is "leap" or "wreck" or "stay" or "lone" or "creep";
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
            if (CavernShooting && _cvPass == 1 && _cvVariant == "brake") _cvTakeView = TakeView(rc); // where the side view stands, as the take comes
            if (_cvWagonRider is { } w && Main.Instance.Builder.FloorRoot is { } floor)
            {
                // M2: the wagon rider's look rides down with a wagon's drop, the cart's riders' with the cart's
                var look = $"{w.State.Name}FallLook";
                var drop = floor.GetChildren().OfType<RigidBody3D>().FirstOrDefault(b => b.GetNodeOrNull(look) != null);
                var lead = _cvTakeLead; // the take may pass the player's control on as the riders fall
                var leadDrop = lead == null ? null : floor.GetChildren().OfType<RigidBody3D>().FirstOrDefault(b => b.GetNodeOrNull($"{lead.State.Name}FallLook") != null);
                _cvWagonLook = $"{w.State.Name}'s look (it rode W1) rides down with {drop?.Name ?? "nothing"}, the lead's with {leadDrop?.Name ?? "nothing"}";
                GD.Print($"[cavern] M2: at the take {_cvWagonLook}");
                if (drop == null || !drop.Name.ToString().StartsWith("WagonDrop", StringComparison.Ordinal) || leadDrop == null || !leadDrop.Name.ToString().StartsWith("CartDrop", StringComparison.Ordinal)) _cvWagonLook = $"FAIL: {_cvWagonLook}";
            }
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
        void Pass(string what)
        {
            if (CavernSightsMissing() is { } blind) { Fail(blind); return; }
            if (CavernShooting && CvWanted.Where(n => !_cvImages.ContainsKey(n)).ToList() is { Count: > 0 } unshot) { Fail($"the shots not saved: {string.Join(", ", unshot)}"); return; }
            GD.Print($"[cavern] RESULT ok ({v}): {what}; seen from the seat: {string.Join("; ", _cvSeen)}{(CavernShooting ? $"; {_cvImages.Count} shots saved" : "")}");
            party.ForcedMoveInput = null; _done = true; GetTree().Quit(0);
        }
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
        if (_cvStep == 4 && _cvPass == 1 && CavernSights(rc, enc, _cvLead ?? me) is { } unseen) { Fail(unseen); return; } // Task 6: every variant, headless too
        if (CavernShooting && CavernShotTick(rc) is { } badShot) { Fail(badShot); return; }
        var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
        var from = enc.BridgeFromS;

        switch (_cvStep)
        {
            case 0 when _cvClock >= 1.5f:
            {
                if (CavernBuild(rc, enc) is { } bad) { Fail(bad); return; }
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(3);
                GD.Print($"[cavern] restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Number))}]; the bridge's pace {BridgeRules.Pace(rc.Train.Count):0.00} m/s, the train's unpumped {CartPhysics.TargetSpeed(0, rc.Train.Count):0.00}");
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
                _cvWagonRider = null;
                _cvWagonLook = null;
                if (v == "brake" && _cvPass == 1)
                {
                    // plan 14's final review M2: a companion rides W1 into the take, and its look must ride down with W1's drop
                    _cvWagonRider = party.Active.LastOrDefault(a => a != me && a.Cart == rc.Cart && !a.Downed);
                    _cvTakeLead = me;
                    if (_cvWagonRider == null || !rc.DebugSeat(_cvWagonRider, 1)) { Fail("no companion could be seated in W1 for the take"); return; }
                }
                _cvRiders.Clear();
                _cvRiders.AddRange(party.Active.Where(a => rc.CarOf(a) >= 0));
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
                // Task 6 review I1: the seen fall's eye is gone and the view is the local hero's own (not another hero's camera the viewport fell back on)
                var view = GetViewport().GetCamera3D();
                if (party.Local is { } riser && view != riser.Camera) problems.Add($"the view's camera is {(view == null ? "none" : $"{view.Name} of {(view.GetParent() as Node)?.Name}")}, not the local hero {riser.State.Name}'s");
                if (problems.Count > 0) { if (_cvClock > 10f) Fail($"the rise: {string.Join("; ", problems)}"); break; }
                GD.Print($"[cavern] {_cvVariant}: the rise's view is the local hero {party.Local?.State.Name}'s camera");
                GD.Print($"[cavern] {_cvVariant}: the rise at stop {rc.StopIndex} after {_cvClock:0.0} s with the bridge whole ({enc.BridgeCells} cells, none gone)");
                _cvPass = 2;
                _cvStep = 1; _cvClock = 0f; // board and depart again
                break;
            }
        }
    }

    /// <summary>
    /// The move car to car offered to <paramref name="hero"/> looking back over its shoulder (120 degrees; plan 14): "car N" or "none". The yaw is
    /// put back at once, before any view tick reads it.
    /// </summary>
    private static string CavernMoveOffer(RailController rc, Adventurer hero)
    {
        var yaw = hero.SeatYaw;
        hero.SeatYaw = Mathf.DegToRad(120f);
        var offer = rc.MoveOfferFor(hero);
        hero.SeatYaw = yaw;
        return offer switch { null => "none", TrainWagon w => $"car {w.Number}", _ => "car 0" };
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
        // plan 15 (spec B.10): the exit bridge's track is laid (no gap over it) with no gravel
        if (float.IsNaN(enc.ExitFromS)) return "no exit bridge on floor 12";
        if (gaps.FirstOrDefault(g => g.from < enc.ExitToS && g.to > enc.ExitFromS) is var over && over != default) return $"a gap {over.from:0.0}..{over.to:0.0} overlaps the exit bridge {enc.ExitFromS:0.0}..{enc.ExitToS:0.0}";
        if (!track.BallastOmitted.Contains((enc.ExitFromS, enc.ExitToS))) return $"the exit bridge {enc.ExitFromS:0.0}..{enc.ExitToS:0.0} keeps its gravel";
        GD.Print($"[cavern] exit bridge laid: party S={enc.ExitFromS:0.0}..{enc.ExitToS:0.0} not bare, no gravel");
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
                if (!_cvMoveControl && _cvPass == 1 && rc.State.Mode == CartMode.Rolling && rc.State.V > 1f && float.IsNaN(_cvRearOnT) && !enc.HoldsRiders() && !rc.MovesRefused)
                {
                    // plan 14, Task 3: the control: off the bridge, rolling, the lead looking back is offered a move (so the check on it means something)
                    _cvMoveControl = true;
                    var offer = CavernMoveOffer(rc, me);
                    GD.Print($"[cavern] bridge: before it, rolling at S={rc.State.S:0.0}, the lead looking back is offered a move to {offer}");
                    if (offer == "none") { Fail("rolling before the bridge, the lead looking back was offered no move (the control)"); return; }
                }
                if (enc.HoldsRiders() && !_cvHoldChecked)
                {
                    _cvHoldChecked = true;
                    var prompt = rc.Cart.Prompt;
                    rc.Cart.Interact(party, me); // a "Get out" press, as the key would
                    GD.Print($"[cavern] bridge: held on it at front S={rc.Party.FrontS:0.0} (v {rc.State.V:0.00}): the seated lead's prompt \"{prompt}\", a get-out press leaves them seated={me.Cart == rc.Cart}");
                    if (prompt != "" || me.Cart != rc.Cart) { Fail("on the bridge while it rolls the lead was offered or let out"); return; }
                    // plan 14, Task 3: no move car to car on the live bridge, offered to anyone in the train or forced
                    if (!_cvMoveControl) { Fail("the move's control before the bridge never ran"); return; }
                    var offered = party.Active.Where(a => rc.CarOf(a) >= 0).Select(a => (a.State.Name, Offer: CavernMoveOffer(rc, a))).ToList();
                    var forced = rc.Train.Count > 0 && rc.Move(me, rc.Train[0], "test");
                    GD.Print($"[cavern] bridge: moves on it: offered [{string.Join(", ", offered.Select(o => $"{o.Name}: {o.Offer}"))}], a forced move to wagon 1 taken={forced} (refused: {rc.LastMoveRefused ?? "-"}), the lead in car {rc.CarOf(me)}");
                    if (rc.Train.Count == 0 || offered.Count == 0 || offered.Any(o => o.Offer != "none") || forced || rc.CarOf(me) != 0) { Fail("on the live bridge a move car to car was offered or taken"); return; }
                }
                break;
            case "brake":
                if (going && !_cvBraking && _cvTime - _cvCollapseT >= CvBrakeAfter && float.IsNaN(_cvTakeT))
                {
                    _cvBraking = true;
                    GD.Print($"[cavern] brake: the lead brakes {_cvTime - _cvCollapseT:0.00} s after the collapse started (front S={enc.CollapseS:0.0}, rear S={rear:0.0}, v {rc.State.V:0.00})");
                }
                if (_cvBraking && float.IsNaN(_cvTakeT)) party.ForcedMoveInput = new Vector2(0, 1);
                if (!float.IsNaN(_cvTakeT))
                {
                    if (CavernShooting && _cvPass == 1 && !CvTakeShots(rc, enc)) return; // windowed: the take seen first, held (Task 6)
                    if (TakeJudged(rc, enc, party, Fail) is not { } said) return;
                    if (_cvWagonLook == null || _cvWagonLook.StartsWith("FAIL", StringComparison.Ordinal)) { Fail($"the wagon rider's fall was not seen with its wagon ({_cvWagonLook ?? "never judged"})"); return; }
                    said += $"; {_cvWagonLook}";
                    _cvTakeSummary = $"the lead braked {CvBrakeAfter:0.0} s after the collapse started (its call at t={_cvCallT - _cvRearOnT:0.00} s after the rear came on, \"Don't stop\" at t={_cvBrakeCallT - _cvRearOnT:0.00}); {said}";
                    _cvStep = 6; _cvClock = 0f;
                    return;
                }
                break;
            case "early-brake":
            {
                // held short: the front never past the bridge's start or HoldShort behind the rear, whichever is further on (it starts at the start)
                var over = going && rc.State.Mode == CartMode.Rolling && rear <= enc.BridgeToS ? enc.CollapseS - Mathf.Max(from, rear - BridgeRules.HoldShort) : float.NegativeInfinity;
                // the brake goes on with the train's rear a cart's length short of the bridge's start: where it was when the cart alone groaned onto
                // it (plan 14 re-pin: with W1..W4 from the landing a brake at the groan, the front at the start, stops the rear 10 m short and nothing starts)
                if (_cvEbPhase == 0 && rc.State.Mode == CartMode.Rolling && rear >= from - 2f * CartRules.HalfLength)
                {
                    _cvEbPhase = 1;
                    GD.Print($"[cavern] early-brake: the lead brakes with the rear a cart's length short of the bridge (front S={rc.Party.FrontS:0.0}, rear S={rear:0.0}, v {rc.State.V:0.00}; the groan came with the front at S={from:0.0})");
                }
                if (_cvEbPhase is 1 or 2 && float.IsNaN(_cvTakeT) && over > 0.01f) { Fail($"braked before the window, the front ran {over:0.00} m past its hold (front S={enc.CollapseS:0.0}, rear S={rear:0.0})"); return; }
                if (_cvEbPhase is 1 or 2 && !float.IsNaN(_cvTakeT)) { Fail($"the bridge took the train braked before its window (phase {_cvEbPhase}, {_cvTakeT - _cvCallT:0.00} s after the call)"); return; }
                if (_cvEbPhase == 1 && !going && _cvClock > 40f) { Fail("the collapse never started under the braked train"); return; }
                if (_cvEbPhase == 1 && going && !_cvHoldChecked && enc.HoldsRiders()) // the final review's I2: held short on the live bridge, slow or stood, still no Get out
                {
                    _cvHoldChecked = true;
                    var prompt = rc.Cart.Prompt;
                    rc.Cart.Interact(party, me); // a "Get out" press, as the key would
                    GD.Print($"[cavern] early-brake: held short on the bridge at front S={rc.Party.FrontS:0.0} (v {rc.State.V:0.00}, braked): the seated lead's prompt \"{prompt}\", a get-out press leaves them seated={me.Cart == rc.Cart}");
                    if (prompt != "" || me.Cart != rc.Cart) { Fail("held short on the bridge the lead was offered Get out or let out"); return; }
                }
                if (_cvEbPhase == 1 && going && _cvTime - _cvCallT >= BridgeRules.ReactionTime + 1.5f)
                {
                    if (!_cvHoldChecked) { Fail("the braked train was never held on the bridge (HoldsRiders) before the let-go"); return; }
                    _cvEbPhase = 2;
                    _cvEbAt = _cvTime;
                    GD.Print($"[cavern] early-brake: braked from a cart's length short to {_cvTime - _cvCallT:0.00} s after the call: held short and alive (front S={enc.CollapseS:0.0}, rear S={rear:0.0}, gap {rear - enc.CollapseS:0.00} m, v {rc.State.V:0.00}, takes {enc.BridgeTakes}, falls {rc.Falls - _cvFallsBefore}, \"Don't stop\" heard {!float.IsNaN(_cvBrakeCallT)}); the lead lets go");
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
                    _cvTakeSummary = $"braked from a cart's length short of the bridge through the call and 3 s past it, the train was held short and alive, no take; let go 1 s, no take; a fresh brake {_cvFreshT - _cvCallT:0.00} s after the call: {said}, {_cvTakeT - _cvFreshT:0.00} s after the fresh press";
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
        Pass($"the call came with the collapse's start {(_cvCollapseT - _cvRearOnT):0.00} s after the rear came on; its front never closer than {_cvMinGap:0.00} m to the rear; no Get out on it while it rolls; every cell gone {_cvAllGoneT - _cvArriveT:+0.00;-0.00} s from the arrival; a restore stands it whole");
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
        if (v == "creep" && !CavernCreep(rc, enc, party, Fail)) return; // stop, creep and stand before the cavern, then as =leap
        if (CavernShooting && v == "leap")
        {
            CavernPlaces(rc, enc, me);
            if (CavernBusy)
            {
                // a held place: the leap's steps wait, and their clocks with them (the windowed run's timings leave the holds out)
                var held = (float)GetProcessDeltaTime();
                _cvStretchT += held; _cvAcrossT += held; _cvPhaseT += held; _cvDropCallT += held; _cvDropHeardT += held;
                return;
            }
        }
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
                if (CavernShooting && v == "leap" && !_cvQueued.Contains("side")) { CvSideShots(rc, me, slag); return; } // the leap waits for the side place's shots
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
                if (CavernShooting && v == "leap") CvDeckSeatShots(rc, slag); // still held at the side place
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
                if (CavernShooting && !CvStopShots(rc, enc, me)) return; // held at stop 4: looking back at traffic's new start
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
            case 10: // =wreck: once the collapse has started (and its sight from the seat has run, 1 s in), the train is wrecked under the lead on the slag cart
                if ((float.IsNaN(enc.CollapseS) || !_cvSawBridge) && _cvTime - _cvAcrossT < 6f) return;
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

    // ---- =creep (Task 6 review I2) ------------------------------------------------------------------------------------------

    /// <summary>=creep stops the train once its S reaches this (after r4_slag's start at S 848, before the split and the cavern).</summary>
    private const float CreepAt = 855f;
    /// <summary>=creep's creep: the brake let go until the train has moved this far, then held again for <see cref="CreepStand"/> s.</summary>
    private const float CreepMetres = 0.6f, CreepStand = 12f;
    private int _cvCreep;
    private float _cvCreepT, _cvCreepS;

    /// <summary>
    /// =creep: at S <see cref="CreepAt"/> the lead brakes the train to a stand, stands 2 s, lets go until it has crept <see cref="CreepMetres"/>,
    /// brakes again and stands <see cref="CreepStand"/> s (long past the slag watchdog's 10 s window), then lets go: r4_slag, waiting ahead
    /// of its pace point, must still be on the line (the watchdog does not free a side-by-side cart that waits ahead). True once done (the
    /// flow then runs on as =leap), false while it holds the train.
    /// </summary>
    private bool CavernCreep(RailController rc, RailEncounterController enc, PartyController party, Action<string> Fail)
    {
        if (_cvCreep >= 5) return true;
        var s = rc.State.S;
        var r4 = enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r4_slag" && IsInstanceValid(c) && !c.Derailed);
        switch (_cvCreep)
        {
            case 0:
                if (s < CreepAt) return true;
                if (r4 == null) { Fail($"creep: r4_slag is not on the line at S={s:0.0}"); return false; }
                _cvCreep = 1; _cvCreepT = _cvTime;
                GD.Print($"[cavern] creep: the lead brakes at S={s:0.0} (v {rc.State.V:0.00}); r4_slag at enemy S={r4.Run.State.S:0.0}, its pace point at {enc.Lanes.EnemyS(s):0.0}");
                break;
            case 1:
                if (rc.State.V > 0.05f) { if (_cvTime - _cvCreepT > 6f) { Fail("creep: the train did not stop within 6 s of the brake"); return false; } break; }
                _cvCreep = 2; _cvCreepT = _cvTime; _cvCreepS = s;
                GD.Print($"[cavern] creep: stopped at S={s:0.0}");
                break;
            case 2:
                if (_cvTime - _cvCreepT < 2f) break;
                _cvCreep = 3; _cvCreepT = _cvTime;
                break;
            case 3:
                if (s - _cvCreepS < CreepMetres) { party.ForcedMoveInput = null; if (_cvTime - _cvCreepT > 6f) { Fail("creep: the train did not creep"); return false; } return false; }
                GD.Print($"[cavern] creep: crept {s - _cvCreepS:0.00} m in {_cvTime - _cvCreepT:0.0} s to S={s:0.0}; the brake again, standing {CreepStand:0} s");
                _cvCreep = 4; _cvCreepT = _cvTime;
                break;
            case 4:
                if (_cvTime - _cvCreepT < CreepStand + 1f || rc.State.V > 0.05f) break;
                party.ForcedMoveInput = null;
                _cvCreep = 5;
                GD.Print($"[cavern] creep: let go at S={s:0.0} after standing {_cvTime - _cvCreepT:0.0} s; r4_slag {(r4 == null ? "GONE" : $"still on the line at enemy S={r4.Run.State.S:0.0} ({r4.Run.State.S - enc.Lanes.EnemyS(s):0.0} m ahead of its pace point)")}");
                if (r4 == null) { Fail("creep: r4_slag was freed while it waited ahead of the stopped train"); return false; }
                _cvClock = 0f; // the ride's 120 s budget from here
                return true;
        }
        party.ForcedMoveInput = new Vector2(0, 1); // the brake
        return false;
    }

    // ---- Task 6: seen from the seat (Ruling 23, the legibility gate) -------------------------------------------------------

    /// <summary>A sight's control ends this far under the deck at its target.</summary>
    private const float CvControlUnder = 0.3f;
    /// <summary>
    /// A control must be stopped no further short of its end than where it crosses the deck's top (<see cref="CvControlUnder"/> over the sine
    /// of its descent), plus this: else something short of the target stopped it, and the sight proves nothing.
    /// </summary>
    private const float CvControlNear = 1f;
    /// <summary>The gate's scene area, as shares of the image (x 20-80 %, y 16-80 %): the twins' diffs and the drop's embers are counted over it.</summary>
    private const float CvCropX0 = 0.2f, CvCropX1 = 0.8f, CvCropY0 = 0.16f, CvCropY1 = 0.8f;
    /// <summary>A pixel differs between twins when its luminance moves by more than this; a pair passes with <see cref="CvDiffMin"/> or more, the drop with <see cref="CvEmberMin"/> embers.</summary>
    private const float CvDiffLuma = 0.1f;
    private const int CvDiffMin = 400, CvEmberMin = 20;
    /// <summary>
    /// The behind pair's bound, as a share of the scene crop (so a window of any size is judged alike; Task 6 review Minor 6): about half its
    /// measured diff once the gone cells' embers glow (71014 px of the 1280x720 crop's 353,894 = 0.20; the legibility fix round, a tightening of 400 px).
    /// </summary>
    private const float CvBehindDiffShare = 0.1f;
    /// <summary>The drop's rect on the drop shot reaches this far under the deck (the hole's walls and its embers' haze).</summary>
    private const float CvDropRectDepth = 2f;
    /// <summary>The drop's two cells' corners (the deck's level and <see cref="CvDropRectDepth"/> down), and their rect on the drop shot (image pixels).</summary>
    private Vector3[] _cvDropCorners = Array.Empty<Vector3>();
    private Rect2I? _cvDropRect;
    /// <summary>The behind place: the party's front this far past the bridge's start. Its shots wait there (at most <see cref="CvBehindWait"/> s) for the collapse's front to hold short of the rear.</summary>
    private const float CvBehindAt = 30f, CvBehindWait = 6f;
    /// <summary>r4_slag's spawn is looked for from the cart held at these party S (its At is S 848.0; it starts <see cref="RailEncounterController.AlongsideSpawnAhead"/> ahead of its pace point).</summary>
    private const float CvSpawnBefore = 847f, CvSpawnAfter = 849f;
    /// <summary>=brake's take: when its shots are taken, after it (the train is seen falling for BridgeRules.FallSeen).</summary>
    private const float CvTakeSeat = 0.35f, CvTakeLate = 1.0f;
    /// <summary>=brake's take: the side view's place and aim, fixed at the take.</summary>
    private (Vector3 From, Vector3 At)? _cvTakeView;

    /// <summary>The sights that ran (each once, pass 1), what they saw for the RESULT, and when the collapse was first seen going.</summary>
    private bool _cvSawBridge, _cvSawSlag, _cvSawDrop, _cvSawSpawn;
    private readonly List<string> _cvSeen = new();
    private float _cvGoingT = float.NaN;

    /// <summary>The windowed =leap (the gate's shots) or =brake (the take's) with --screenshot=PATH.</summary>
    private bool CavernShooting => _testCavern && _screenshotPath != null && Drawn && _cvVariant is "leap" or "brake";

    /// <summary>
    /// The sights from the seat, each frame of ride 4's pass 1 (every variant, headless too): the bridge's front 1 s after the collapse started
    /// (from the lead's eye), the slag cart on the first frame "Leap across" is offered (the lead's eye to its <see cref="SlagCart.DeckSeat"/> 0)
    /// and the drop at the slag line's call (from the leaper's eye, up on the slag cart's deck, to its near edge). Null, or why it failed.
    /// </summary>
    private string? CavernSights(RailController rc, RailEncounterController enc, Adventurer lead)
    {
        if (float.IsNaN(_cvGoingT) && !float.IsNaN(enc.CollapseS)) _cvGoingT = _cvTime;
        if (!_cvSawSpawn && enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r4_slag") is { } r4 && CavernSpawnUnseen(rc, r4) is { } seen) return seen;
        if (!_cvSawBridge && _cvTime - _cvGoingT >= 1f && !float.IsNaN(enc.CollapseS))
        {
            _cvSawBridge = true;
            var at = rc.Party.Track.Point(enc.CollapseS); // the curve lies on the bed: the deck's top on a flat cavern
            if (CavernSight(lead, "bridge front", at + Vector3.Up * 0.5f, at + Vector3.Down * CvControlUnder, $"the collapse's front at S={enc.CollapseS:0.0} + 0.5 m, {_cvTime - _cvGoingT:0.00} s after it started (the train's rear S={RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing):0.0})") is { } bad) return bad;
        }
        if (!_cvSawSlag && enc.LeapOfferFor(lead) is SlagCart slag)
        {
            _cvSawSlag = true;
            var seat = slag.DeckSeat(0).Origin;
            var deck = slag.Run.Track.Point(slag.Run.State.S).Y; // the slag lane's deck under the cart: its bed
            if (CavernSight(lead, "slag cart", seat, new Vector3(seat.X, deck - CvControlUnder, seat.Z), $"{slag.EncounterId}'s deck seat 0 (party S={rc.State.S:0.0})") is { } bad) return bad;
        }
        if (!_cvSawDrop && enc.LeapCalled && lead.Cart is SlagCart on && !lead.Downed && enc.Drop is { } drop)
        {
            _cvSawDrop = true;
            var near = on.Run.Track.Point(drop.From);
            if (CavernSight(lead, "drop", near + Vector3.Up * 0.3f, near + Vector3.Down * CvControlUnder, $"the drop's near edge at enemy S={drop.From:0.0} + 0.3 m (its lamp lit {drop.LampLit}), the slag cart's front {drop.From - on.Run.FrontS:0.0} m short of it") is { } bad) return bad;
        }
        return null;
    }

    /// <summary>A spawn ray stopped nearer the eye than this proves nothing (the seat's own rim, a companion's head): it fails the check.</summary>
    private const float CvSpawnNear = 3f;
    /// <summary>r4_slag must start at least this far (enemy S) past the split spur's end, clear of the spur and its crevasse.</summary>
    private const float CvSpurClear = 2f;

    /// <summary>
    /// r4_slag's spawn is seen by no seat (the legibility fix round; Task 6 review Minors 4 and 7): on the first frame it is on the line, a
    /// layer-1 ray from every seated eye in the party's cart to its tub (0.8 m over its bed), its riders' heads (2.3 m) and its two ends
    /// (0.8 m up) must be blocked, and no farther short of the eye than <see cref="CvSpawnNear"/>; and its start lies
    /// <see cref="CvSpurClear"/> or more past the split spur's end on the enemy line. Null, or why not.
    /// </summary>
    private string? CavernSpawnUnseen(RailController rc, SlagCart r4)
    {
        _cvSawSpawn = true;
        var eyes = Main.Instance.Party.Active.Where(a => a.Cart == rc.Cart).ToList();
        var fwd = -r4.GlobalTransform.Basis.Z.Normalized();
        var points = new (string Name, Vector3 At)[]
        {
            ("tub", r4.GlobalPosition + Vector3.Up * 0.8f), ("riders' heads", r4.GlobalPosition + Vector3.Up * 2.3f),
            ("front", r4.GlobalPosition + Vector3.Up * 0.8f + fwd * CartRules.HalfLength), ("back", r4.GlobalPosition + Vector3.Up * 0.8f - fwd * CartRules.HalfLength),
        };
        var bad = new List<string>();
        var blockers = new SortedSet<string>();
        float nearest = float.PositiveInfinity, nearestBlock = float.PositiveInfinity;
        foreach (var hero in eyes)
            foreach (var (name, at) in points)
            {
                var eye = hero.Camera.GlobalPosition;
                nearest = Mathf.Min(nearest, eye.DistanceTo(at));
                var hit = hero.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, at, 1));
                if (hit.Count == 0) { bad.Add($"{hero.State.Name}'s eye to its {name} is clear ({eye.DistanceTo(at):0.0} m)"); continue; }
                var by = (hit["collider"].AsGodotObject() as Node)?.Name.ToString() ?? "?";
                var d = hit["position"].AsVector3().DistanceTo(eye);
                blockers.Add(by);
                nearestBlock = Mathf.Min(nearestBlock, d);
                if (d < CvSpawnNear) bad.Add($"{hero.State.Name}'s eye to its {name} is stopped by {by} only {d:0.0} m from the eye");
            }
        GD.Print($"[cavern] sight: r4_slag's spawn {(bad.Count == 0 ? "blocked" : "NOT PROVEN UNSEEN")} from {eyes.Count} seated eye(s) to its tub, heads and ends at enemy S={r4.Run.State.S:0.0} (party S={rc.State.S:0.0}; the nearest eye {nearest:0.0} m off; every ray blocked by [{string.Join(", ", blockers)}], the nearest {nearestBlock:0.0} m from its eye){(bad.Count == 0 ? "" : $": {string.Join("; ", bad)}")}");
        if (eyes.Count == 0) return "r4_slag spawned with nobody seated in the party's cart to see it";
        if (bad.Count > 0) return $"r4_slag's spawn is not proven out of sight: {string.Join("; ", bad)}";
        // the flow runs only on floor 12, where ride 4's split is live: a missing split is a failure, never a skipped bound (the final review's M9)
        if (RailEncounterController.Instance?.SplitSwitchNode is not { } split || !IsInstanceValid(split)) return "floor 12 has no live split (r4_split): r4_slag's spur clearance cannot be judged";
        var spurS = r4.Run.Track.ClosestS(split.SpurEnd);
        GD.Print($"[cavern] r4_slag starts at enemy S={r4.Run.State.S:0.0}, {r4.Run.State.S - spurS:0.0} m past the split spur's end (enemy S={spurS:0.0})");
        if (r4.Run.State.S - spurS < CvSpurClear) return $"r4_slag starts {r4.Run.State.S - spurS:0.0} m past the split spur's end, under {CvSpurClear} m";
        _cvSeen.Add($"r4_slag's spawn blocked from {eyes.Count} seat(s)");
        return null;
    }

    /// <summary>
    /// One sight: a layer-1 ray from <paramref name="from"/>'s eye to <paramref name="target"/> must be clear, and its control, from the same eye
    /// to <paramref name="under"/> (<see cref="CvControlUnder"/> under the deck at the target), must be blocked near its end. Null, or why not.
    /// </summary>
    private string? CavernSight(Adventurer from, string name, Vector3 target, Vector3 under, string where)
    {
        var eye = from.Camera.GlobalPosition;
        var sight = Sight(from, eye, target);
        var said = sight == "clear" ? $"clear at {eye.DistanceTo(target):0.0} m" : sight;
        GD.Print($"[cavern] sight: {name} {said}");
        GD.Print($"[cavern] sight: {name}: from {from.State.Name}'s eye (in {(from.Cart as Node)?.Name ?? "nothing"}) to {where}; the target {target.Y - eye.Y:+0.00;-0.00} m from the eye's height");
        if (sight != "clear") return $"the {name} is not in sight from the seat: {sight}";
        var hit = from.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, under, 1));
        var length = eye.DistanceTo(under);
        var descent = (eye.Y - under.Y) / length;
        var crossing = descent > 0f ? CvControlUnder / descent : float.PositiveInfinity; // how far short of its end the control crosses the deck's top
        if (hit.Count == 0)
        {
            GD.Print($"[cavern] sight: {name} control clear ({length:0.0} m)");
            return $"the {name}'s sight is vacuous: its control, {CvControlUnder} m under the deck at the target, is clear";
        }
        var at = hit["position"].AsVector3();
        var by = (hit["collider"].AsGodotObject() as Node)?.Name;
        var d = eye.DistanceTo(at);
        GD.Print($"[cavern] sight: {name} control blocked by {by} at {d:0.0} m ({length - d:0.00} m short of its end; it crosses the deck's top {crossing:0.00} m short of it)");
        if (length - d > crossing + CvControlNear) return $"the {name}'s sight is vacuous: its control is stopped by {by} {length - d:0.00} m short of its end, more than {CvControlNear:0.0} m before it crosses the deck's top at the target ({crossing:0.00} m short)";
        _cvSeen.Add($"{name} {said}");
        return null;
    }

    /// <summary>The sights this variant must run that never did: null, or the failure.</summary>
    private string? CavernSightsMissing()
    {
        var missing = new List<string>();
        if (!_cvSawSpawn) missing.Add("r4_slag's spawn (from every seated eye)");
        if (!_cvSawBridge) missing.Add("the bridge front (1 s after the collapse started)");
        if (LeapBranch && !_cvSawSlag) missing.Add("the slag cart (the first frame \"Leap across\" was offered)");
        if (_cvVariant is "leap" or "stay" or "creep" && !_cvSawDrop) missing.Add("the drop (at the slag line's call, from the leaper's eye)");
        return missing.Count == 0 ? null : $"the sight check never ran: {string.Join("; ", missing)}";
    }

    // the windowed shots: a queue posed one at a time, each drawn HeldFrames (30) frames with the HUD hidden, then saved as PATH_name.png;
    // a place's twins are queued back to back and taken with the party's cart pinned (S and V, each frame) from the place until all its
    // shots are saved, then let go at the speed it had

    private sealed class CvShot
    {
        public string Name = "";
        /// <summary>Sets the view up and returns the camera that draws it.</summary>
        public Func<Camera3D> Pose = null!;
        /// <summary>Gives the view back after the shot (shown again what it hid).</summary>
        public Action? Done;
        /// <summary>Run each frame the shot is drawn (a cart held still in it).</summary>
        public Action? Each;
        public int Frames;
        public Camera3D? Camera, Was;
    }

    private readonly Queue<CvShot> _cvShots = new();
    private CvShot? _cvShot;
    private readonly HashSet<string> _cvQueued = new();
    private readonly Dictionary<string, Image> _cvImages = new();
    private readonly Dictionary<string, float> _cvShotS = new();
    /// <summary>The held place: the cart's S pinned (NaN: none), its speed to give back, the shots it waits for, its name and when it began.</summary>
    private float _cvHoldS = float.NaN, _cvHoldV, _cvHoldT;
    private string[] _cvHoldFor = Array.Empty<string>();
    private string _cvHoldName = "";
    /// <summary>The ride's slag carts held with the party's cart: each one's S and the speed it is let go at.</summary>
    private readonly List<(SlagCart Cart, float S, float V)> _cvHeldSlag = new();
    private bool _cvShotsJudged;
    /// <summary>The twins taken at one S (the first is the scene as it is, the second with one thing changed); the gate's three are judged.</summary>
    private static readonly (string A, string B)[] CvTwins = { ("behind", "behind_standing"), ("side", "side_nocart"), ("drop", "drop_standing"), ("deckseats", "deckseats_marked"), ("stop4", "stop4_traffic") };
    /// <summary>Every shot a windowed variant must save before its RESULT.</summary>
    private string[] CvWanted => _cvVariant == "brake"
        ? new[] { "take_seat", "take_side", "take_seat_late" }
        : new[] { "spawn_before", "bridge_lip_from", "bridge_lip_to", "drop_lip_near", "drop_joint", "drop_lip_far", "spawn_after", "side", "side_nocart", "deckseats", "deckseats_marked", "behind", "behind_standing", "drop", "drop_standing", "stop4", "stop4_traffic" };

    /// <summary>
    /// A shot is queued or drawn, or a place is held (but the first offer's between its side twins and the leap that queues its deckseats
    /// twins): the leap's steps wait.
    /// </summary>
    private bool CavernBusy => _cvShot != null || _cvShots.Count > 0 || !float.IsNaN(_cvHoldS) && _cvHoldName != SidePlace;
    private const string SidePlace = "the first offer";

    private void CvQueue(string name, Func<Camera3D> pose, Action? done = null, Action? each = null, int frames = HeldFrames)
    {
        if (_cvQueued.Add(name)) _cvShots.Enqueue(new CvShot { Name = name, Pose = pose, Done = done, Each = each, Frames = frames });
    }

    /// <summary>Hold the party's cart at <paramref name="s"/> until <paramref name="names"/> are all saved.</summary>
    private void CvHold(RailController rc, string place, float s, params string[] names)
    {
        if (float.IsNaN(_cvHoldS))
        {
            _cvHoldV = rc.State.V;
            _cvHoldT = _cvTime;
            _cvHeldSlag.Clear(); // the ride's slag carts are held too, or one closes in on the held cart and takes the offer (r4_hounds did)
            if (RailEncounterController.Instance is { } enc) foreach (var c in enc.SlagCarts) if (IsInstanceValid(c)) _cvHeldSlag.Add((c, c.Run.State.S, c.Run.State.V));
        }
        _cvHoldS = s;
        _cvHoldFor = names;
        _cvHoldName = place;
        GD.Print($"[cavern] shots: held at {place}: the cart pinned at S={s:0.00} (front S={s + rc.Party.Dir * CartRules.HalfLength:0.00}, it rolled at {_cvHoldV:0.00} m/s) for {string.Join(", ", names)}");
    }

    /// <summary>A camera at <paramref name="who"/>'s seated eye looking at <paramref name="at"/>: a child of its camera, so it rides with the seat.</summary>
    private static Camera3D CvFromSeat(Adventurer who, Vector3 at)
    {
        var cam = TempCamera(who.Camera);
        who.Camera.AddChild(cam);
        var eye = who.Camera.GlobalPosition;
        cam.GlobalTransform = new Transform3D(Basis.Identity, eye).LookingAt(at, Vector3.Up);
        cam.MakeCurrent();
        return cam;
    }

    /// <summary>A camera standing in the world at <paramref name="from"/> looking at <paramref name="at"/> (a view no seat has), under <paramref name="root"/>.</summary>
    private static Camera3D CvFrom(Node3D root, Camera3D like, Vector3 from, Vector3 at)
    {
        var cam = TempCamera(like);
        root.AddChild(cam);
        cam.GlobalTransform = new Transform3D(Basis.Identity, from).LookingAt(at, Vector3.Up);
        cam.MakeCurrent();
        return cam;
    }

    /// <summary>The close looks at the joins, taken while the bridge and the drop stand whole (for viewing, not judged).</summary>
    private static readonly string[] CvLipNames = { "bridge_lip_from", "bridge_lip_to", "drop_lip_near", "drop_joint", "drop_lip_far" };

    /// <summary>
    /// Close looks at where the live pieces' own rails meet the track's, the bridge and the drop both standing (=leap, held at r4_slag's spawn):
    /// the bridge's two lips on the party line, and the drop's near lip, the joint between its two cells and its far lip on the slag line; each
    /// from 2.2 m back along the line, 1 m to its side and 1.1 m up, at the rails there.
    /// </summary>
    private void CvLipShots(RailController rc, RailEncounterController enc, TrackSampler enemy)
    {
        if (Main.Instance.Builder.FloorRoot is not { } root || enc.Drop is not { } drop) return;
        var like = Main.Instance.Party.Local!.Camera;
        void Lip(string name, TrackSampler track, float s)
        {
            var f = track.Frame(s);
            var at = f.Origin;
            var forward = -f.Basis.Z.Normalized();
            var from = at - forward * 2.2f + f.Basis.X.Normalized() * 1f + Vector3.Up * 1.1f;
            CvQueue(name, () => CvFrom(root, like, from, at + Vector3.Up * 0.1f));
        }
        GD.Print($"[cavern] shots: the joins: the bridge's lips at party S={enc.BridgeFromS:0.0} and {enc.BridgeToS:0.0}; the drop's near lip, joint and far lip at enemy S={drop.From:0.0}, {(drop.From + drop.To) / 2f:0.0} and {drop.To:0.0}");
        Lip("bridge_lip_from", rc.Party.Track, enc.BridgeFromS);
        Lip("bridge_lip_to", rc.Party.Track, enc.BridgeToS);
        Lip("drop_lip_near", enemy, drop.From);
        Lip("drop_joint", enemy, (drop.From + drop.To) / 2f);
        Lip("drop_lip_far", enemy, drop.To);
    }

    /// <summary>A seated eye's height at the party cart's back, turned to look back down the line, level (a child of the cart).</summary>
    private static Camera3D CvPartyRear(RailController rc, Camera3D like)
    {
        var cam = TempCamera(like);
        rc.Cart.AddChild(cam);
        var eyeY = rc.Cart.ToLocal(rc.Cart.SeatTransform(0).Origin).Y + Adventurer.SeatedEyeHeight;
        cam.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0f, eyeY, CartRules.HalfLength + 0.05f));
        cam.MakeCurrent();
        return cam;
    }

    /// <summary>The way the party's cart travels at its S, level.</summary>
    private static Vector3 CvTravel(RailController rc)
    {
        var f = -rc.Party.Track.Frame(rc.State.S).Basis.Z * rc.Party.Dir;
        f.Y = 0f;
        return f.Normalized();
    }

    /// <summary>
    /// Each frame of a windowed run: the held cart pinned, then let go at its speed once its shots are saved; the next shot posed, or the one
    /// in hand saved after its frames (its twin's S checked); and, once the gate's six are in, the gate judged. Null, or why it failed.
    /// </summary>
    private string? CavernShotTick(RailController rc)
    {
        if (!float.IsNaN(_cvHoldS))
        {
            if (_cvHoldFor.All(_cvImages.ContainsKey))
            {
                rc.Party.State = rc.State with { S = _cvHoldS, V = _cvHoldV };
                foreach (var (c, cs, cv) in _cvHeldSlag) if (IsInstanceValid(c) && !c.Derailed) c.Run.State = c.Run.State with { S = cs, V = cv };
                GD.Print($"[cavern] shots: let go at {_cvHoldName} after {_cvTime - _cvHoldT:0.00} s, at {_cvHoldV:0.00} m/s");
                _cvHoldS = float.NaN;
            }
            else
            {
                rc.Party.State = rc.State with { S = _cvHoldS, V = 0f }; // a long frame's creep taken back: the twins are taken at one S
                foreach (var (c, cs, _) in _cvHeldSlag) if (IsInstanceValid(c) && !c.Derailed) c.Run.State = c.Run.State with { S = cs, V = 0f };
            }
        }
        if (_cvShot == null && _cvShots.TryDequeue(out var next))
        {
            Main.Instance.Hud.Visible = false;
            next.Was = GetViewport().GetCamera3D();
            next.Camera = next.Pose();
            _cvShot = next;
        }
        else if (_cvShot is { } shot)
        {
            shot.Each?.Invoke();
            if (--shot.Frames > 0) return null;
            _cvShot = null;
            var img = GetViewport().GetTexture().GetImage();
            var path = System.IO.Path.ChangeExtension(_screenshotPath!, null) + "_" + shot.Name + ".png";
            var err = img.SavePng(path);
            _cvImages[shot.Name] = img;
            _cvShotS[shot.Name] = rc.State.S;
            if (shot.Name == "drop" && shot.Camera is { } dropCam && IsInstanceValid(dropCam) && _cvDropCorners.All(c => !dropCam.IsPositionBehind(c)))
            {
                var scale = img.GetWidth() / GetViewport().GetVisibleRect().Size.X;
                var px = _cvDropCorners.Select(c => dropCam.UnprojectPosition(c) * scale).ToList();
                int rx0 = (int)px.Min(p => p.X), ry0 = (int)px.Min(p => p.Y), rx1 = (int)Mathf.Ceil(px.Max(p => p.X)), ry1 = (int)Mathf.Ceil(px.Max(p => p.Y));
                _cvDropRect = new Rect2I(rx0, ry0, rx1 - rx0, ry1 - ry0);
            }
            GD.Print($"[cavern] shot {shot.Name} ({err}) {path}: {img.GetWidth()}x{img.GetHeight()}, the cart at S={rc.State.S:0.00} (front S={rc.Party.FrontS:0.00}, {rc.State.Mode}, v {rc.State.V:0.00})");
            shot.Done?.Invoke();
            if (shot.Camera is { } cam && cam != shot.Was && IsInstanceValid(cam)) cam.QueueFree();
            if (shot.Was is { } was && IsInstanceValid(was)) was.MakeCurrent();
            else if (Main.Instance.Party.Local is { } local) local.Camera.MakeCurrent();
            if (_cvShots.Count == 0) Main.Instance.Hud.Visible = true;
            if (err != Error.Ok) return $"the {shot.Name} shot was not saved ({err})";
            foreach (var (a, b) in CvTwins)
                if ((shot.Name == a || shot.Name == b) && _cvShotS.TryGetValue(a, out var sa) && _cvShotS.TryGetValue(b, out var sb) && Mathf.Abs(sa - sb) > HeldSlack)
                    return $"the {a} twins were not taken at one S ({sa:0.00} and {sb:0.00}): the hold slipped";
        }
        if (!_cvShotsJudged && _cvVariant == "leap" && new[] { "behind", "behind_standing", "side", "side_nocart", "drop", "drop_standing" }.All(_cvImages.ContainsKey))
        {
            _cvShotsJudged = true;
            int a = CvDiff(_cvImages["behind"], _cvImages["behind_standing"]), b = CvDiff(_cvImages["side"], _cvImages["side_nocart"]), c = CvDiff(_cvImages["drop"], _cvImages["drop_standing"]);
            if (_cvDropRect is not { } rect) return "the drop's rect on the drop shot was not found (its corners are behind the camera)";
            var e = CvEmbers(_cvImages["drop"], rect);
            GD.Print($"[cavern] shots: behind diff {a} px; side diff {b} px; drop diff {c} px, drop ember {e} px");
            var bw = _cvImages["behind"].GetWidth(); var bh = _cvImages["behind"].GetHeight();
            var behindMin = (int)(CvBehindDiffShare * (((int)(bw * CvCropX1) - (int)(bw * CvCropX0)) * ((int)(bh * CvCropY1) - (int)(bh * CvCropY0))));
            GD.Print($"[cavern] shots: the bounds: behind >= {behindMin} ({CvBehindDiffShare:P0} of the crop; the diff is {a / (float)Math.Max(1, behindMin) * CvBehindDiffShare:P1}), side and drop >= {CvDiffMin}, ember >= {CvEmberMin}; the drop's embers counted in its projected rect only ({rect.Position.X},{rect.Position.Y} {rect.Size.X}x{rect.Size.Y} px: the hole's two cells, the deck's level to {CvDropRectDepth:0} m down), never the slag cart's heap");
            GD.Print($"[cavern] shots judged on the scene crop x {CvCropX0:P0}-{CvCropX1:P0}, y {CvCropY0:P0}-{CvCropY1:P0} (a pixel differs when its Rec. 709 luma moves by more than {CvDiffLuma}); the drop's embers r > 0.7, 0.3 < g < 0.65, b < 0.35 (shown, not judged: deckseats diff {CvDiffOf("deckseats", "deckseats_marked")} px, stop4 diff {CvDiffOf("stop4", "stop4_traffic")} px, spawn diff {CvDiffOf("spawn_before", "spawn_after")} px)");
            if (a < behindMin || b < CvDiffMin || c < CvDiffMin || e < CvEmberMin) return $"the cavern does not read from the seat: behind diff {a} px (want >= {behindMin}, {CvBehindDiffShare:P0} of the crop), side diff {b} px, drop diff {c} px (want >= {CvDiffMin} each), drop ember {e} px in the drop's rect (want >= {CvEmberMin})";
        }
        return null;
    }

    private string CvDiffOf(string a, string b) => _cvImages.TryGetValue(a, out var x) && _cvImages.TryGetValue(b, out var y) ? CvDiff(x, y).ToString() : "-";

    /// <summary>The scene crop's pixels whose luminance differs between <paramref name="a"/> and <paramref name="b"/> by more than <see cref="CvDiffLuma"/> (−1 when their sizes differ).</summary>
    private static int CvDiff(Image a, Image b)
    {
        var (da, w, h) = CvRgba(a);
        var (db, wb, hb) = CvRgba(b);
        if (w != wb || h != hb) return -1;
        var n = 0;
        for (var y = (int)(h * CvCropY0); y < (int)(h * CvCropY1); y++)
            for (var x = (int)(w * CvCropX0); x < (int)(w * CvCropX1); x++)
            {
                var i = (y * w + x) * 4;
                if (Mathf.Abs(CvLuma(da, i) - CvLuma(db, i)) > CvDiffLuma) n++;
            }
        return n;
    }

    /// <summary>The orange pixels (r > 0.7, 0.3 < g < 0.65, b < 0.35: the crevasse's embers) in <paramref name="rect"/> (image pixels) within the scene crop.</summary>
    private static int CvEmbers(Image img, Rect2I rect)
    {
        var (d, w, h) = CvRgba(img);
        var n = 0;
        int x0 = Math.Max((int)(w * CvCropX0), rect.Position.X), x1 = Math.Min((int)(w * CvCropX1), rect.End.X);
        int y0 = Math.Max((int)(h * CvCropY0), rect.Position.Y), y1 = Math.Min((int)(h * CvCropY1), rect.End.Y);
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * w + x) * 4;
                float r = d[i] / 255f, g = d[i + 1] / 255f, bl = d[i + 2] / 255f;
                if (r > 0.7f && g > 0.3f && g < 0.65f && bl < 0.35f) n++;
            }
        return n;
    }

    private static (byte[] Data, int W, int H) CvRgba(Image img)
    {
        var copy = (Image)img.Duplicate();
        if (copy.GetFormat() != Image.Format.Rgba8) copy.Convert(Image.Format.Rgba8);
        return (copy.GetData(), copy.GetWidth(), copy.GetHeight());
    }

    private static float CvLuma(byte[] d, int i) => (0.2126f * d[i] + 0.7152f * d[i + 1] + 0.0722f * d[i + 2]) / 255f;

    /// <summary>The bridge's gone cells' looks (their rails and sleepers, and the cavern's deck) shown or hidden again, the footing untouched (a view only).</summary>
    private static void CvBridgeLooks(RailEncounterController enc, bool shown)
    {
        if (enc.SetPieceNode("r4_bridge") is not CavernBridge bridge) return;
        for (var k = 0; k < bridge.Gone; k++)
        {
            if (bridge.LookOf(k) is { } look) look.Visible = shown;
            if (CvDeckLook(bridge.Cells[k].Cell) is { } deck) deck.Visible = shown;
        }
        // the embers under the gone cells go out with the deck shown again, and come back as they were (the newest cells' light, every haze)
        var c = bridge.Cells;
        for (var k = 0; k < bridge.Gone; k++)
        {
            if (bridge.GetNodeOrNull<Node3D>($"GoneHaze_{c[k].Cell.X}_{c[k].Cell.Y}") is { } haze) haze.Visible = !shown && k >= bridge.Gone - CavernBridge.HazeCells;
            if (bridge.GetNodeOrNull<Node3D>($"GoneGlow_{c[k].Cell.X}_{c[k].Cell.Y}") is { } glow) glow.Visible = !shown && k >= bridge.Gone - CavernBridge.GlowCells;
        }
    }

    private static Node3D? CvDeckLook(GridPos cell) => ChasmDressing.Instance?.GetNodeOrNull<Node3D>($"DeckLook_{cell.X}_{cell.Y}");

    /// <summary>
    /// =leap's places, each frame of ride 4 (windowed with --screenshot): r4_slag's spawn from the seat held at party S 847 and 849, looking
    /// at its pace point on the slag lane (spawn_before, spawn_after); behind, with the front held at the bridge's start + 30 m once the
    /// collapse's front holds short of the rear: the lead's view turned 180° (behind; from the party cart's rearmost seated eye while the lead is on the slag deck) and its twin with the gone cells' looks shown again
    /// (behind_standing); and the drop at the S of the slag line's call: the leaper's view forward at the drop's near edge (drop) and its twin
    /// with the drop's looks shown again and its lamp off (drop_standing). The side place is queued by the offer itself (<see cref="CvSideShots"/>).
    /// </summary>
    private void CavernPlaces(RailController rc, RailEncounterController enc, Adventurer lead)
    {
        if (rc.State.Mode != CartMode.Rolling || rc.StopIndex != 3) return;
        var s = rc.State.S;
        var enemy = new TrackSampler(Main.Instance.Builder.EnemyTrack!);
        // where r4_slag starts (or has started): the slag line AlongsideSpawnAhead past the pace point
        Func<Camera3D> AtPacePoint() => () => CvFromSeat(lead, (enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r4_slag") is { } r4 ? r4.GlobalPosition
            : enemy.Point(enc.Lanes.EnemyS(rc.State.S) + RailEncounterController.AlongsideSpawnAhead)) + Vector3.Up * 0.8f);
        if (!_cvQueued.Contains("spawn_before") && s >= CvSpawnBefore)
        {
            CvHold(rc, "r4_slag's spawn, before", CvSpawnBefore, new[] { "spawn_before" }.Concat(CvLipNames).ToArray());
            CvQueue("spawn_before", AtPacePoint());
            CvLipShots(rc, enc, enemy); // the bridge and the drop both still whole
        }
        if (_cvImages.ContainsKey("spawn_before") && !_cvQueued.Contains("spawn_after") && s >= CvSpawnAfter)
        {
            var r4 = enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r4_slag");
            GD.Print($"[cavern] shots: r4_slag at party S={s:0.0}: {(r4 == null ? "not spawned" : $"spawned, its front at enemy S={r4.Run.FrontS:0.0}, {lead.Camera.GlobalPosition.DistanceTo(r4.GlobalPosition):0.0} m from the lead's eye")}");
            CvHold(rc, "r4_slag's spawn, after", CvSpawnAfter, "spawn_after");
            CvQueue("spawn_after", AtPacePoint());
        }
        var behindAt = enc.BridgeFromS + CvBehindAt;
        if (!_cvQueued.Contains("behind") && (_cvHoldName == "behind" || rc.Party.FrontS >= behindAt))
        {
            if (_cvHoldName != "behind") CvHold(rc, "behind", behindAt - rc.Party.Dir * CartRules.HalfLength, "behind", "behind_standing");
            var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
            var gap = rear - enc.CollapseS;
            if (!(gap <= BridgeRules.HoldShort + 0.05f) && _cvTime - _cvHoldT < CvBehindWait) return; // the collapse's front closes in on the held train
            GD.Print($"[cavern] shots: behind: the collapse's front S={enc.CollapseS:0.0}, {gap:0.00} m behind the rear (S={rear:0.0}) after {_cvTime - _cvHoldT:0.00} s held; {enc.BridgeGone}/{enc.BridgeCells} cells gone");
            // the lead's eye turned 180°; but a leaper on the slag deck has its riders right behind it (they filled the view), so then it is
            // the party cart's own rearmost seated eye, at its back, turned to look back down the line (plan 12's behind)
            Camera3D Back() => lead.Cart == rc.Cart ? CvFromSeat(lead, lead.Camera.GlobalPosition - CvTravel(rc) * 20f) : CvPartyRear(rc, lead.Camera);
            CvQueue("behind", Back);
            CvQueue("behind_standing", () => { CvBridgeLooks(enc, true); return Back(); }, () => CvBridgeLooks(enc, false));
        }
        if (!_cvQueued.Contains("drop") && enc.LeapCalled && lead.Cart is SlagCart on && enc.Drop is { } drop)
        {
            CvHold(rc, "the drop's call", s, "drop", "drop_standing");
            var near = on.Run.Track.Point(drop.From);
            var corners = new List<Vector3>();
            foreach (var at in new[] { drop.From, drop.To })
            {
                var f = on.Run.Track.Frame(at);
                foreach (var side in new[] { -1f, 1f })
                    foreach (var down in new[] { 0f, CvDropRectDepth })
                        corners.Add(f.Origin + f.Basis.X.Normalized() * (side * DungeonBuilder.CellSize / 2f) + Vector3.Down * down);
            }
            _cvDropCorners = corners.ToArray();
            var lamp = drop.GetNodeOrNull<MeshInstance3D>("DropLamp");
            var light = drop.GetNodeOrNull<Light3D>("DropLampLight");
            var lit = lamp?.MaterialOverride;
            var dark = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.05f, 0.04f), Roughness = 0.6f }; // as the lamp's own unlit
            void Looks(bool shown)
            {
                for (var k = 0; k < drop.Twins.Count; k++)
                {
                    if (drop.LookOf(k) is { } look) look.Visible = shown;
                    if (CvDeckLook(drop.Twins[k]) is { } deck) deck.Visible = shown;
                }
                if (light != null) light.Visible = !shown;
                if (lamp != null) lamp.MaterialOverride = shown ? dark : lit;
                foreach (var glow in drop.GetChildren().OfType<Node3D>().Where(n => n.Name.ToString() is "DropLampHalo" || n.Name.ToString().StartsWith("DropGlow_") || n.Name.ToString().StartsWith("DropHaze_")))
                    glow.Visible = !shown; // the drop standing: no halo, no embers
            }
            GD.Print($"[cavern] shots: the drop: the slag cart's front {drop.From - on.Run.FrontS:0.0} m short of its near edge (enemy S={drop.From:0.0}), the lead {lead.Camera.GlobalPosition.DistanceTo(near):0.0} m from it, its lamp lit {drop.LampLit}");
            CvQueue("drop", () => CvFromSeat(lead, near));
            CvQueue("drop_standing", () => { Looks(true); return CvFromSeat(lead, near); }, () => Looks(false));
        }
    }

    /// <summary>
    /// =leap's side place, on the first frame "Leap across" is offered: the cart held there; the lead's view turned toward the slag cart (side)
    /// and its twin with the slag cart and its riders hidden (side_nocart). The leap waits for them.
    /// </summary>
    private void CvSideShots(RailController rc, Adventurer lead, SlagCart slag)
    {
        CvHold(rc, SidePlace, rc.State.S, "side", "side_nocart", "deckseats", "deckseats_marked");
        var at = slag.GlobalPosition + Vector3.Up * 1f; // the heap and its riders
        void Shown(bool on)
        {
            if (!IsInstanceValid(slag)) return;
            slag.Visible = on;
            foreach (var e in slag.Aboard) if (IsInstanceValid(e)) e.Visible = on;
        }
        CvQueue("side", () => CvFromSeat(lead, at));
        CvQueue("side_nocart", () => { Shown(false); return CvFromSeat(lead, at); }, () => Shown(true));
    }

    /// <summary>
    /// =leap, right after the leap across, still held at the side place: both carts from behind and above (deckseats) and its twin with the
    /// seats marked (deckseats_marked: green the slag cart's deck seats, red its riders' spots, blue the party cart's hero seats).
    /// </summary>
    private void CvDeckSeatShots(RailController rc, SlagCart slag)
    {
        var root = Main.Instance.Builder.FloorRoot!;
        var mid = (rc.Cart.GlobalPosition + slag.GlobalPosition) / 2f;
        var from = mid - CvTravel(rc) * 4.5f + Vector3.Up * 3.2f;
        var like = Main.Instance.Party.Local!.Camera;
        var marks = new List<Node3D>();
        void Mark(Vector3 at, Color colour)
        {
            var m = new MeshInstance3D
            {
                Name = "SeatMark", Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = colour, NoDepthTest = true },
            };
            root.AddChild(m);
            m.GlobalPosition = at;
            marks.Add(m);
        }
        var seats = string.Join(", ", Enumerable.Range(0, LeapRules.MaxLeapers).Select(k => $"deck seat {k} {slag.DeckSeat(k).Origin - slag.GlobalPosition:0.00}"));
        var spots = string.Join(", ", Enumerable.Range(0, SlagCart.MaxRiders).Select(k => $"spot {k} {slag.RiderSpot(k).Origin - slag.GlobalPosition:0.00}"));
        GD.Print($"[cavern] shots: deckseats: on {slag.EncounterId} (InnerSide {slag.InnerSide}), from its origin: {seats}; its riders' {spots}; the party cart {rc.Cart.GlobalPosition.DistanceTo(slag.GlobalPosition):0.00} m off");
        CvQueue("deckseats", () => CvFrom(root, like, from, mid + Vector3.Up * 0.8f));
        CvQueue("deckseats_marked", () =>
        {
            if (IsInstanceValid(slag))
            {
                for (var k = 0; k < LeapRules.MaxLeapers; k++) Mark(slag.DeckSeat(k).Origin, new Color(0.1f, 1f, 0.2f));
                for (var k = 0; k < SlagCart.MaxRiders; k++) Mark(slag.RiderSpot(k).Origin, new Color(1f, 0.15f, 0.1f));
            }
            for (var k = 0; k < CartSeats.HeroSeats; k++) Mark(rc.Cart.SeatTransform(k).Origin, new Color(0.2f, 0.4f, 1f));
            return CvFrom(root, like, from, mid + Vector3.Up * 0.8f);
        }, () => { foreach (var m in marks) if (IsInstanceValid(m)) m.QueueFree(); });
    }

    /// <summary>
    /// =leap at stop 4, the cart held there: looking back from the seat at the traffic cart's start past the dropped drop
    /// (<see cref="LeapRules.TrafficStartS"/>), bare (stop4) and with a traffic cart standing there (stop4_traffic, held still at its start
    /// while it is drawn). True once both are saved.
    /// </summary>
    private bool CvStopShots(RailController rc, RailEncounterController enc, Adventurer lead)
    {
        if (!_cvQueued.Contains("stop4"))
        {
            var start = LeapRules.TrafficStartS(SwitchBackRules.TrafficStartS(rc.State.S, enc.BreakGapS, enc.BreakGapFarEnemyS), rc.State.S, enc.LeapDropS, enc.LeapDropFarEnemyS, enc.Drop is { Dropped: true });
            var enemy = new TrackSampler(Main.Instance.Builder.EnemyTrack!);
            var at = enemy.Point(start) + Vector3.Up * 0.8f;
            GD.Print($"[cavern] shots: stop 4 (S={rc.State.S:0.0}): traffic now starts at enemy S={start:0.0} (the drop down {enc.Drop?.Dropped}), {lead.Camera.GlobalPosition.DistanceTo(at):0.0} m from the lead's eye");
            CvQueue("stop4", () => CvFromSeat(lead, at));
            CvQueue("stop4_traffic", () =>
            {
                enc.DebugSpawnTrafficAt(start);
                return CvFromSeat(lead, at);
            }, () => GD.Print($"[cavern] shots: stop4_traffic: the traffic cart {(enc.TrafficCart is { } t ? $"at enemy S={t.Run.State.S:0.0}" : "gone")}"),
            () => { if (enc.TrafficCart is { } t) t.Run.State = t.Run.State with { S = start, V = 0f }; });
        }
        return _cvImages.ContainsKey("stop4") && _cvImages.ContainsKey("stop4_traffic");
    }

    /// <summary>
    /// =brake's take (Task 3 Minor 3; the legibility fix round): the train is seen falling through the gone cells for
    /// <see cref="BridgeRules.FallSeen"/> before the wipe. <see cref="CvTakeSeat"/> s after it, the view the player has (take_seat: the
    /// fall's own eye riding down with the cart) and the side view from across the slag lane (take_side, made current each frame over the
    /// fall's eye); <see cref="CvTakeLate"/> s after it the player's view again (take_seat_late). True once all are saved.
    /// </summary>
    private bool CvTakeShots(RailController rc, RailEncounterController enc)
    {
        if (!_cvQueued.Contains("take_seat") && _cvTime - _cvTakeT >= CvTakeSeat && Main.Instance.Builder.FloorRoot is { } root && _cvTakeView is { } view)
        {
            var fallen = _cvRiders.Count(a => a.State.Fallen);
            var current = GetViewport().GetCamera3D();
            GD.Print($"[cavern] shots: the take, {_cvTime - _cvTakeT:0.00} s after it: the view's camera {current?.Name}; the cart {rc.State.Mode}; {enc.BridgeGone}/{enc.BridgeCells} cells gone; {fallen} of {_cvRiders.Count} riders fallen; the party wiped {_cvWiped}");
            CvQueue("take_seat", () => GetViewport().GetCamera3D()!, frames: 3);
            Camera3D? side = null;
            CvQueue("take_side", () => side = CvFrom(root, current!, view.From, view.At), each: () => { if (side != null && IsInstanceValid(side) && !side.Current) side.MakeCurrent(); }, frames: 3);
        }
        if (_cvImages.ContainsKey("take_side") && !_cvQueued.Contains("take_seat_late") && _cvTime - _cvTakeT >= CvTakeLate)
            CvQueue("take_seat_late", () => GetViewport().GetCamera3D()!, frames: 3);
        return CvWanted.All(_cvImages.ContainsKey);
    }

    /// <summary>The take's side view: 6 m out over the slag lane, 3 m back and 2.5 m up from the train's middle, looking at it under the deck.</summary>
    private static (Vector3 From, Vector3 At) TakeView(RailController rc)
    {
        var enemy = new TrackSampler(Main.Instance.Builder.EnemyTrack!);
        var enc = RailEncounterController.Instance!;
        var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
        var mid = rc.Party.Track.Point((rc.Party.FrontS + rear) / 2f);
        var across = enemy.Point(enc.Lanes.EnemyS(rc.State.S)) - rc.Cart.GlobalPosition;
        across.Y = 0f;
        return (mid + across.Normalized() * 6f - CvTravel(rc) * 3f + Vector3.Up * 2.5f, mid + Vector3.Down * 1.5f);
    }
}
