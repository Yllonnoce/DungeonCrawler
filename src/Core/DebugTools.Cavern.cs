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
// stands it whole. =brake: the lead brakes from 1 s after the collapse starts: "Don't stop" is called before the take, the take comes 1.5 s
// or more after the call, rc.Falls rises by 1 with every rider fallen within 1 s, the wipe rises at stop 3 with the bridge whole, and the
// next departure, unbraked, reaches stop 4. =stall: the train is caught (rc.DebugStall) once the collapse has started (its rear 6 m or more on
// the bridge), nobody braking: the bridge is held by the stall guard, never takes the train, and the watchdog frees it to stop 4. =runaway:
// the runaway is real, the split left straight, the lead pumps: the runaway goes down with the bridge with its front within 8 m of the
// bridge's start, its body neither rammed nor plunged as it went; no ram; stop 4. Prints [cavern] lines, the timing line
// ("[cavern] timing: bridge call t=..., earliest take t=...", seconds since the rear came onto the bridge; the earliest take is a train
// stopped dead at the call) and a RESULT.
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
                else return;
                GD.Print($"[cavern] heard the {(t == BarkTrigger.BridgeGoing ? "bridge's" : "brake's")} call (by {by ?? "the log"}) at front S={r.Party.FrontS:0.0}, t={_cvTime - _cvRearOnT:0.00} s after the rear came on");
            };
            RailCalls.Called += called;
            _staticUnhooks.Add(() => RailCalls.Called -= called);
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
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill(); // the flow owns the plating and the HP
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
                GD.Print($"[cavern] brake: every rider fallen [{string.Join(", ", _cvRiders.Select(a => a.State.Name))}] ({_cvFallenAtWreck} of {_cvRiders.Count} at the take itself)");
                party.ForcedMoveInput = null;
                _cvStep = 7; _cvClock = 0f;
                break;
            }
            case 7:
                if (!_cvWiped) { if (_cvClock > 5f) Fail("the party did not wipe within 5 s of the take"); break; }
                GD.Print("[cavern] brake: the party has fallen: the wipe");
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
                GD.Print($"[cavern] brake: the rise at stop {rc.StopIndex} after {_cvClock:0.0} s with the bridge whole ({enc.BridgeCells} cells, none gone)");
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
        var (fromMm, toMm) = bridge.MeasureJoin(track);
        GD.Print($"[cavern] join: the bridge's first rail ring against the track's at S={bridge.From:0.0}: {fromMm:0.000} mm; its last at S={bridge.To:0.0}: {toMm:0.000} mm");
        if (!(fromMm <= 1f) || !(toMm <= 1f)) return $"the bridge's rails do not meet the track's within 1 mm (from {fromMm:0.000}, to {toMm:0.000})";
        if (enc.BridgeGone != 0 || ChasmDressing.Instance is null) return $"the bridge is not whole at the build ({enc.BridgeGone} gone) or there is no chasm dressing";
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
            _cvEarliestTakeT = _cvTime + (rear - from) / BridgeRules.Pace(rc.Train.Count); // a train stopped dead at the call
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
                if (going && !_cvBraking && _cvTime - _cvCollapseT >= 1f && float.IsNaN(_cvTakeT))
                {
                    _cvBraking = true;
                    GD.Print($"[cavern] brake: the lead brakes 1 s after the collapse started (front S={enc.CollapseS:0.0}, rear S={rear:0.0}, v {rc.State.V:0.00})");
                }
                if (_cvBraking && float.IsNaN(_cvTakeT)) party.ForcedMoveInput = new Vector2(0, 1);
                if (!float.IsNaN(_cvTakeT))
                {
                    party.ForcedMoveInput = null;
                    GD.Print($"[cavern] brake: the bridge took the train {_cvTakeT - _cvCallT:0.00} s after its call (t={_cvTakeT - _cvRearOnT:0.00}; earliest take t={_cvEarliestTakeT - _cvRearOnT:0.00}); \"Don't stop\" heard at t={_cvBrakeCallT - _cvRearOnT:0.00}; takes {enc.BridgeTakes}, falls {rc.Falls - _cvFallsBefore}");
                    if (enc.BridgeTakes != 1) { Fail($"the bridge's takes are {enc.BridgeTakes}, not 1"); return; }
                    if (float.IsNaN(_cvBrakeCallT) || _cvBrakeCallT > _cvTakeT) { Fail("\"Don't stop\" was not called before the take"); return; }
                    if (_cvTakeT - _cvCallT < BridgeRules.Grace) { Fail($"the take came {_cvTakeT - _cvCallT:0.00} s after the call, under {BridgeRules.Grace} s"); return; }
                    _cvTakeSummary = $"the lead braked 1 s after the collapse started (its call at t={_cvCallT - _cvRearOnT:0.00} s after the rear came on, \"Don't stop\" at t={_cvBrakeCallT - _cvRearOnT:0.00}); the bridge took the train {_cvTakeT - _cvCallT:0.00} s after its call";
                    _cvStep = 6; _cvClock = 0f;
                    return;
                }
                break;
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
        if (enc.BridgeTakes > 0 && v != "brake") { Fail($"the bridge took the train ({enc.BridgeTakes})"); return; }
        if (rc.Falls != _cvFallsBefore && v != "brake") { Fail("the train fell"); return; }
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

    /// <summary>The footing box of cavern deck <paramref name="cell"/>, or null.</summary>
    private static CollisionShape3D? DeckShape(GridPos cell) => ChasmDressing.Instance?.GetNodeOrNull<CollisionShape3D>($"Footings/Deck_{cell.X}_{cell.Y}");
}
