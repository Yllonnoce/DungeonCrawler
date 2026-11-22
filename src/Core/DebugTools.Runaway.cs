using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-runaway[=tail|ram], --test-split[=after|early|hold|stall|lone] -------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo; every enemy is culled as it appears. The build: r4_runaway is a marker at its
// trigger and r4_split a live split at the layout's points on ride 4. The cart is restored at stop 3 (rc.Restore(3)), everyone boards
// and the cart departs on ride 4; nobody presses anything (a bare --test-runaway runs the split's =after). =tail: the lead pumps throughout; the runaway breaks loose with
// its front 14-16 m behind the train's last body and its call is heard (RailCalls, BarkTrigger.Runaway); until stop 4 the gap never
// shrinks below 3 m; the train arrives at stop 4 and the runaway halts with its front 2.5-4 m behind the last body and is gone within 2 s
// of its halt; nobody is run over. =ram: the crew wagons of stops 1-3 coupled before the restore, the pumpers capped at 0 (nobody
// pumps); the runaway breaks loose 14-16 m behind and rams the rolling train once: the plating drops by 40 (+-0.5), each seated hero who
// is up loses 20% (+-1) of max HP and none goes below 1 HP, the NPCs seated in the train are unhurt, nobody leaves a seat, RunawayRams is
// 1 and stays 1 to stop 4; then the cart is restored at stop 3 again and departs with its plating set to 30: the ram wrecks it, the
// runaway is gone by the wreck's event, and a spare takes over as today. Every variant: nobody falls. Prints [runaway] lines and a RESULT.
// The split's variants (--test-split, or --test-runaway with one; the default is after) ride the same way with the lead pumping and judge
// r4_split (RunSplitRide): =after, the seat offers it from the window's start (ruling R-bend) with the wait call there, the now call 1 m past
// the points, and the lead throws it from the seat 1-3 m past them: no cross, the runaway takes the spur and goes off it into the crevasse
// within 3 s, no ram, stop 4. =early: a press 9 m short (a metre inside the server's early lead, SplitRules.PressReach) throws nothing (the seat does not offer it there); the test's hook throws it, "not
// yet" is called, the train takes the spur and falls with its front within 1 m of the points + 7, every rider fallen within 1 s, the wipe
// rises at stop 3 with every hero up within 10 m of the whole cart, and the next departure sends a runaway again. =hold: the hook throws it 9 m
// short ("not yet"), the lead sets it back from the seat at the window's start (taken at once), then throws it from the seat short of the
// points: held, thrown just after the train passes straight; the runaway takes the spur. =stall: the hook throws it, the train is caught 7 m
// short (RailController.DebugStall) and the watchdog sets the split back and grinds on to stop 4: no fall. =lone: the hook throws it and the
// only human (possession off) is downed in the seat: at the points the safe line, the lever left thrown, the runaway takes the spur. Every
// split variant: nobody falls but the train's riders. Prints [split] lines and a RESULT.
// The legibility gate (Ruling 23), every variant, headless too: at the build every RailLamp{i} inside the dark is hidden with its lantern dark
// (and some lamp outside it lit); layer-1 rays from the seated lead's eye with the front 8 m short of the points to the split's amber and red
// lamps, and 3 s after the runaway breaks loose to its lamp ("[runaway] sight: ..."): a blocked ray fails the flow. =after also checks the light
// is all back at stop 4, then (pass 2, the runaway held) rides into the dark's middle and leaves for floor 13 from there, and checks it again
// before its RESULT. Windowed =after with --screenshot=PATH saves the seat's views as PATH_lit, _dark (HUD hidden), _behind (the rearmost view,
// turned back), _behind_nolamp (the same with the runaway's light hidden: the orange must drop by 20), _behind_seat (the lead's eye turned
// 180°, shown only), _split, _window and _lever, and judges them (ShotsStep). A sight check that never ran fails the variant.
// Ruling 22's hold for the other flows is here too (HoldTheRunaway).
public partial class DebugTools
{
    private bool _testRunaway;
    private string _rwVariant = "tail";
    private int _rwStep, _rwPass = 1;
    private float _rwClock;
    private bool _rwListening;
    private RailController? _rwHooked;
    private int _rwCalls, _rwRunOver;
    private bool _rwLoose;
    private float _rwMinGap = float.PositiveInfinity, _rwHaltGap = float.NaN, _rwHaltAt = float.NaN;
    /// <summary>=ram: the values the ram met (read as it is logged, before the dent and the hits), and whether they were judged.</summary>
    private bool _rwRamLogged, _rwRamChecked;
    private float _rwPlatingBefore;
    private int _rwSeatedBefore;
    private readonly Dictionary<Adventurer, (float Hp, float Max)> _rwHpBefore = new();
    private readonly Dictionary<Npc, float> _rwNpcHpBefore = new();
    /// <summary>=ram's second pass: the wreck's event seen, the runaway gone by it, and the wrecks and spares before it.</summary>
    private bool _rwWreckSeen, _rwGoneAtWreck;
    private int _rwWrecksBefore, _rwSparesBefore;
    private bool RamBranch => _rwVariant == "ram";
    /// <summary>The split's variants (after, early, hold, stall, lone): their lines and RESULT are tagged [split].</summary>
    private bool SplitBranch => _rwVariant is "after" or "early" or "hold" or "stall" or "lone";
    private string RwTag => SplitBranch ? "split" : "runaway";

    // ---- Ruling 22: the other flows hold the runaway -------------------------------------------------------------

    private RailEncounterController? _rwHeldFor;

    /// <summary>A flow that is not about the runaway holds ride 4's (each floor build's controller), and says so once per build.</summary>
    private void HoldTheRunaway(string tag)
    {
        if (RailEncounterController.Instance is not { } enc || enc == _rwHeldFor) return;
        _rwHeldFor = enc;
        enc.DebugHoldRunaway(4);
        GD.Print($"[{tag}] the runaway on ride 4 held by the test");
    }

    // ---- the flow ------------------------------------------------------------------------------------------------

    private void RunawayListen(RailController rc)
    {
        if (!_rwListening)
        {
            _rwListening = true;
            Action<BarkTrigger, string?> called = (t, by) =>
            {
                if (_done || !_testRunaway || t != BarkTrigger.Runaway || RailController.Instance is not { } r) return;
                _rwCalls++;
                GD.Print($"[runaway] heard the runaway's call (by {by ?? "the log"}) at front S={r.Party.FrontS:0.0}");
            };
            RailCalls.Called += called;
            _staticUnhooks.Add(() => RailCalls.Called -= called);
            EventBus.Instance.Message += text =>
            {
                if (_done || !_testRunaway || RailController.Instance is not { } r) return;
                if (text.Contains(RunOverRules.RunawaySource + " " + HazardRules.Verb(HazardDamage.RunOver))) { _rwRunOver++; GD.Print($"[runaway] run over: \"{text}\""); }
                if (text != "The runaway wagon rams the train!") return;
                // logged before the dent and the hits (Ram): every value here is the one the ram met
                _rwRamLogged = true;
                _rwPlatingBefore = r.State.Plating;
                _rwHpBefore.Clear();
                foreach (var a in Main.Instance.Party.Active)
                    if (a.Cart == r.Cart && !a.Downed) _rwHpBefore[a] = (a.State.Hp, a.State.Stats[Characters.Stat.MaxHp]);
                _rwSeatedBefore = Main.Instance.Party.Active.Count(a => a.Seated);
                _rwNpcHpBefore.Clear();
                foreach (var n in Main.Instance.Builder.FloorRoot!.GetChildren().OfType<Npc>())
                    if (n.Seated && !n.Dead) _rwNpcHpBefore[n] = n.Hp;
            };
        }
        if (_rwHooked == rc) return;
        _rwHooked = rc; // each floor build's controller
        rc.CartEvents += e =>
        {
            if (e.Kind != CartEventKind.Wrecked || _done || !_testRunaway || _rwPass != 2 || _rwWreckSeen) return;
            _rwWreckSeen = true;
            _rwGoneAtWreck = RailEncounterController.Instance?.RideRunawayCart == null;
            GD.Print($"[runaway] ram 2 the cart's Wrecked event: the runaway gone={_rwGoneAtWreck}");
        };
    }

    private void RunRunawayTest()
    {
        if (_rwStep == LeftFloorStep) { LightAfterLeaving(); return; } // =after's pass 2 left floor 12 from the dark
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _rwClock += (float)GetProcessDeltaTime();
        var party = main.Party;
        void Fail(string why) { GD.Print($"[{RwTag}] RESULT FAIL ({_rwVariant}): {why}"); party.ForcedMoveInput = null; PumperCap = null; party.AllowPossession = true; _done = true; GetTree().Quit(1); }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_rwClock > 3f) Fail("floor 12 has no line"); return; }
        RunawayListen(rc);
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill(); // the flow owns the plating and the HP
        if (rc.StopIndex == 3) RideWaiveCages(main, rc, "runaway"); // stop 4's cage holds the train while the runaway comes up behind it
        enc.RunawayClock = 0f; // no traffic runaway on the party line while the cart stands
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (SplitBranch) SplitListen(rc);
        if (party.Active.FirstOrDefault(a => a.State.Fallen && !_spMayFall.Contains(a.State.Name)) is { } fell) { Fail($"{fell.State.Name} fell at step {_rwStep} (cell {DungeonBuilder.WorldToCell(fell.GlobalPosition)}): nobody falls{(SplitBranch ? " but the train's" : "")}"); return; }
        var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
        var run = enc.RideRunawayRun;
        if (_rwStep >= 4 && _rwPass == 1 && Legibility(rc, enc, me, run, Fail)) return;

        switch (_rwStep)
        {
            case 0 when _rwClock >= 1.5f:
            {
                if (enc.SetPieceNode("r4_runaway") is null || enc.SetPieceNode("r4_split") is null) { Fail("floor 12 lacks r4_runaway or r4_split"); return; }
                if (enc.SplitRide != 4 || float.IsNaN(enc.SplitPointsS) || !enc.SplitIsLive) { Fail($"ride 4's split is not live (ride {enc.SplitRide}, points S={enc.SplitPointsS:0.0}, live {enc.SplitIsLive})"); return; }
                GD.Print($"[runaway] floor 12 ({_rwVariant}): r4_split live at party S={enc.SplitPointsS:0.0}; stops [{string.Join(",", rc.StopS.Select(s => s.ToString("0.0")))}]; lethality {TierRules.Lethality}, party damage scale {TierRules.PartyDamageScale:0.00}");
                if (DarkLampsOut(root, enc) is { } lit) { Fail(lit); return; }
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                if (RamBranch)
                {
                    if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                    for (var stop = 1; stop <= 3; stop++)
                        if (!ctrl.DebugCouple(stop)) { Fail($"wagon {stop} could not be coupled"); return; }
                    PumperCap = 0; // nobody pumps: the runaway closes
                }
                rc.Restore(3);
                GD.Print($"[runaway] restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Stop))}], pumpers capped at {PumperCap?.ToString() ?? "none"}; the runaway's target {RunawayRules.Target(rc.Train.Count):0.00} m/s, the train's own unpumped {CartPhysics.TargetSpeed(0, rc.Train.Count):0.00}, one pumper {CartPhysics.TargetSpeed(1, rc.Train.Count):0.00}");
                if (RamBranch && rc.Train.Count != 3) { Fail($"the train has {rc.Train.Count} wagons, not 3"); return; }
                _rwStep++; _rwClock = 0f;
                break;
            }
            case 1 when _rwClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _rwStep++; _rwClock = 0f;
                break;
            }
            case 2:
            {
                var heroes = party.Active.Count();
                var seated = party.Active.Count(a => a.Cart == rc.Cart);
                if ((!me.Seated || seated < heroes) && _rwClock < 3f) break;
                if (!me.Seated || me.Cart != rc.Cart) { Fail("the lead did not take a seat"); return; }
                GD.Print($"[runaway] pass {_rwPass} boarded at stop {rc.StopIndex}: {seated} of {heroes} heroes seated, train=[{string.Join(",", rc.Train.Select(w => w.Stop))}]");
                _rwStep++; _rwClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 4) { if (_rwClock > 15f) Fail($"ride 4 did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                if (!RamBranch) party.ForcedMoveInput = new Vector2(0, -1); // the stick forward: the lead pumps
                if (RamBranch && _rwPass == 2)
                {
                    rc.DebugSetPlating(30f);
                    (_rwWrecksBefore, _rwSparesBefore) = (rc.Wrecks, rc.SparesUsed.Count);
                }
                GD.Print($"[runaway] pass {_rwPass} departed stop 3 at S={rc.State.S:0.0}: plating {rc.State.Plating:0}, {rc.Train.Count} wagon(s), lead pumping={!RamBranch}");
                if (SplitBranch) SplitDeparted(rc);
                _rwStep++; _rwClock = 0f;
                break;
            }
            case >= 4 when SplitBranch:
                RunSplitRide(main, layout, rc, enc, party, me, rear, run, Fail);
                break;
            case 4:
            {
                if (!RamBranch) party.ForcedMoveInput = new Vector2(0, -1);
                if (run != null && !_rwLoose)
                {
                    _rwLoose = true;
                    var gap = rear - run.FrontS;
                    GD.Print($"[runaway] pass {_rwPass} the runaway broke loose: its front at S={run.FrontS:0.0}, {gap:0.0} m behind the last body (rear S={rear:0.0}), v {run.State.V:0.00} m/s; the train's front S={rc.Party.FrontS:0.0}, v {rc.State.V:0.00}; calls heard {_rwCalls}");
                    if (gap < 14f || gap > 16f) { Fail($"it broke loose {gap:0.0} m behind the last body, not 14-16"); return; }
                    if (_rwCalls < _rwPass) { Fail("its call was not heard"); return; }
                }
                if (run != null && rc.State.Mode == CartMode.Rolling && rc.StopIndex == 3 && run.State.Mode == CartMode.Rolling) _rwMinGap = Mathf.Min(_rwMinGap, rear - run.FrontS);
                if (RamBranch && _rwPass == 1 && _rwRamLogged && !_rwRamChecked)
                {
                    if (RamOnce(rc, enc, party) is { } bad) { Fail(bad); return; }
                    _rwRamChecked = true;
                }
                if (RamBranch && _rwPass == 2)
                {
                    if (!_rwWreckSeen) { if (_rwClock > 120f) Fail($"ram 2: no wreck in 120 s (rams {enc.RunawayRams}, plating {rc.State.Plating:0})"); break; }
                    var spares = rc.SparesUsed.Count;
                    GD.Print($"[runaway] ram 2: rams {enc.RunawayRams}, wrecks {_rwWrecksBefore}->{rc.Wrecks}, the runaway gone at the wreck's event={_rwGoneAtWreck} (now {enc.RideRunawayCart == null}); spares used {_rwSparesBefore}->{spares}, the cart {rc.State.Mode} at stop {rc.StopIndex}, plating {rc.State.Plating:0}");
                    if (enc.RunawayRams != 2 || rc.Wrecks != _rwWrecksBefore + 1) { Fail("ram 2 did not wreck the cart once"); return; }
                    if (!_rwGoneAtWreck || enc.RideRunawayCart != null) { Fail("the runaway outlived the wreck"); return; }
                    if (spares != _rwSparesBefore + 1 || rc.State.Mode != CartMode.Held || rc.State.Plating < CartRules.MaxPlating) { Fail("no spare took over after the wreck"); return; }
                    PumperCap = null;
                    if (SightsMissing() is { } unseen) { Fail(unseen); return; }
                    GD.Print($"[runaway] RESULT ok (ram)");
                    _done = true;
                    GetTree().Quit(0);
                    return;
                }
                if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == 4)
                {
                    party.ForcedMoveInput = null;
                    GD.Print($"[runaway] pass {_rwPass} arrived at stop 4 after {_rwClock:0.0} s: rams {enc.RunawayRams}, the gap's least {_rwMinGap:0.0} m, bells {enc.RunawayBells}, runaway {(run == null ? "gone" : $"at S={run.State.S:0.0} ({rear - run.FrontS:0.0} m behind)")}");
                    if (!_rwLoose) { Fail("the runaway never broke loose"); return; }
                    if (RamBranch && (!_rwRamChecked || enc.RunawayRams != 1)) { Fail($"ram 1: rams {enc.RunawayRams} (want exactly 1, judged {_rwRamChecked})"); return; }
                    if (!RamBranch && (enc.RunawayRams != 0 || _rwMinGap < 3f)) { Fail($"tail: rams {enc.RunawayRams}, the gap's least {_rwMinGap:0.0} m (want none, >= 3)"); return; }
                    _rwStep++; _rwClock = 0f;
                    break;
                }
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != 3) { Fail($"the train left ride 4 another way (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
                if (_rwClock > 120f) Fail($"ride 4 did not reach stop 4 in 120 s (S={rc.State.S:0.0})");
                break;
            }
            case 5:
            {
                if (RamBranch)
                {
                    if (_rwClock < 3f) break;
                    if (enc.RunawayRams != 1) { Fail($"a second ram ({enc.RunawayRams})"); return; }
                    // the second pass: the plating set low before the trigger, so the ram wrecks the cart
                    _rwPass = 2;
                    _rwLoose = false;
                    rc.Restore(3);
                    GD.Print($"[runaway] ram 1 ok; pass 2: restored at stop {rc.StopIndex}, train=[{string.Join(",", rc.Train.Select(w => w.Stop))}]");
                    _rwStep = 1; _rwClock = 0f;
                    break;
                }
                if (run != null && run.State.Mode == CartMode.Held && float.IsNaN(_rwHaltGap))
                {
                    _rwHaltGap = rear - run.FrontS;
                    _rwHaltAt = _rwClock;
                    GD.Print($"[runaway] tail: the runaway halted {_rwHaltGap:0.00} m behind the last body, {_rwClock:0.0} s after the arrival");
                    if (_rwHaltGap < 2.5f || _rwHaltGap > 4f) { Fail($"it halted {_rwHaltGap:0.00} m behind, not 2.5-4"); return; }
                }
                if (run == null)
                {
                    if (float.IsNaN(_rwHaltGap)) { Fail("the runaway was gone before it halted behind the train"); return; }
                    var after = _rwClock - _rwHaltAt;
                    GD.Print($"[runaway] tail: the runaway gone {after:0.00} s after its halt; run over {_rwRunOver}, rams {enc.RunawayRams}");
                    if (after > 2f) { Fail($"it lingered {after:0.00} s after its halt (want within 2)"); return; }
                    if (_rwRunOver > 0) { Fail($"the runaway ran over {_rwRunOver} bodies"); return; }
                    if (SightsMissing() is { } unseen) { Fail(unseen); return; }
                    GD.Print("[runaway] RESULT ok (tail)");
                    _done = true;
                    GetTree().Quit(0);
                    return;
                }
                if (_rwClock > 20f) Fail($"the runaway neither halted nor went in 20 s (mode {run.State.Mode}, {rear - run.FrontS:0.0} m behind)");
                break;
            }
        }
    }

    /// <summary>=ram's first ram, the frame after its log: the plating -40, each seated hero -20% (never below 1 HP), the seated NPCs unhurt, nobody unseated, the runaway gone. Null, or why not.</summary>
    private string? RamOnce(RailController rc, RailEncounterController enc, PartyController party)
    {
        var dent = _rwPlatingBefore - rc.State.Plating;
        var lines = new List<string>();
        var bad = new List<string>();
        foreach (var (a, (hp, max)) in _rwHpBefore)
        {
            var lost = (hp - a.State.Hp) / max;
            lines.Add($"{a.State.Name} {hp:0}->{a.State.Hp:0}/{max:0} (-{lost:P1})");
            if (a.State.Hp < 1f || a.Downed) bad.Add($"{a.State.Name} at {a.State.Hp:0.0} HP");
            else if (hp - 1f > max * RunawayRules.RamHpFraction && Mathf.Abs(lost - RunawayRules.RamHpFraction) > 0.01f) bad.Add($"{a.State.Name} lost {lost:P1}");
        }
        foreach (var (n, hp) in _rwNpcHpBefore)
            if (IsInstanceValid(n) && Mathf.Abs(n.Hp - hp) > 0.01f) bad.Add($"{n.NpcName} (seated) {hp:0.0}->{n.Hp:0.0}");
        var seated = party.Active.Count(a => a.Seated);
        GD.Print($"[runaway] ram 1: plating {_rwPlatingBefore:0.0}->{rc.State.Plating:0.0} (-{dent:0.0}); [{string.Join(", ", lines)}]; {_rwNpcHpBefore.Count} NPC(s) seated; seated heroes {_rwSeatedBefore}->{seated}; rams {enc.RunawayRams}; the runaway gone={enc.RideRunawayCart == null}");
        if (Mathf.Abs(dent - RunawayRules.RamDent) > 0.5f) return $"ram 1 dented the plating by {dent:0.0}, not 40";
        if (_rwHpBefore.Count == 0) return "ram 1 found no seated hero";
        if (bad.Count > 0) return $"ram 1: {string.Join("; ", bad)}";
        if (seated != _rwSeatedBefore) return $"ram 1 unseated someone ({_rwSeatedBefore} -> {seated})";
        if (enc.RunawayRams != 1 || enc.RideRunawayCart != null) return $"ram 1: rams {enc.RunawayRams}, the runaway still on the line={enc.RideRunawayCart != null}";
        return null;
    }
    // ---- the split's variants (--test-split / --test-runaway=after|early|hold|stall|lone) -------------------------

    private readonly HashSet<string> _spMayFall = new();
    private readonly List<Adventurer> _spRiders = new();
    private int _spHeldLogs;
    private bool _spLeadChecked;
    private bool _spListening, _spWiped, _spTakesSpurLog, _spFallLog, _spLoneLog, _spPlungeLog;
    private RailController? _spHooked, _spWipeRc;
    private float _spTime, _spWaitFront = float.NaN, _spNotYetFront = float.NaN, _spNowFront = float.NaN;
    private float _spFallFront = float.NaN, _spOnSpurAt = float.NaN, _spPlungeAt = float.NaN, _spSetBackAt = float.NaN, _spThrownAtFront = float.NaN;
    private int _spFallenAtWreck, _spFallsAtWreck, _spFallsBefore, _spGroundBefore, _spRamsBefore;
    private bool _spOfferedSeen, _spPressedAfter, _spThrown9, _spSetBackDone, _spHeldPressed, _spStalled, _spSetBackSeen, _spLoneDowned, _spPointsPassed;

    /// <summary>The split's calls, its log lines, the wipe and the train's fall, heard once per run (the cart's events per floor build).</summary>
    private void SplitListen(RailController rc)
    {
        if (!_spListening)
        {
            _spListening = true;
            Action<BarkTrigger, string?> called = (t, by) =>
            {
                if (_done || !_testRunaway || !SplitBranch || RailController.Instance is not { } r) return;
                var front = r.Party.FrontS;
                if (t == BarkTrigger.SplitWait && float.IsNaN(_spWaitFront)) _spWaitFront = front;
                else if (t == BarkTrigger.SplitNotYet && float.IsNaN(_spNotYetFront)) _spNotYetFront = front;
                else if (t == BarkTrigger.SplitNow && float.IsNaN(_spNowFront)) _spNowFront = front;
                else return;
                GD.Print($"[split] heard the {(t == BarkTrigger.SplitWait ? "wait" : t == BarkTrigger.SplitNotYet ? "not yet" : "now")} call (by {by ?? "the log"}) at front S={front:0.0}");
            };
            RailCalls.Called += called;
            _staticUnhooks.Add(() => RailCalls.Called -= called);
            EventBus.Instance.Message += text =>
            {
                if (_done || !_testRunaway || !SplitBranch) return;
                if (text == "The cart takes the spur!") _spTakesSpurLog = true;
                if (text == "The cart runs off the spur into the crevasse!") _spFallLog = true;
                if (text == SplitRules.SafeLineLog) _spLoneLog = true;
                if (text == "The runaway goes off the spur into the crevasse!") _spPlungeLog = true;
                if (text == SplitRules.HeldLog) _spHeldLogs++;
            };
            EventBus.Instance.PartyWiped += () =>
            {
                if (_done || !_testRunaway || !SplitBranch) return;
                _spWiped = true;
                _spWipeRc = RailController.Instance;
            };
        }
        if (_spHooked == rc) return;
        _spHooked = rc;
        rc.CartEvents += e =>
        {
            if (e.Kind != CartEventKind.Wrecked || _done || !_testRunaway || !SplitBranch || !float.IsNaN(_spFallFront) || RailController.Instance is not { } r) return;
            _spFallFront = r.Party.FrontS;
            _spFallsAtWreck = r.Falls;
            _spFallenAtWreck = _spRiders.Count(a => a.State.Fallen); // told after the train is taken (Fall flushes its events)
            foreach (var a in _spRiders) _spMayFall.Add(a.State.Name);
        };
    }

    /// <summary>A pass departs stop 3: who rides, and the counts the ride is judged against.</summary>
    private void SplitDeparted(RailController rc)
    {
        _spRiders.Clear();
        _spRiders.AddRange(Main.Instance.Party.Active.Where(a => a.Cart == rc.Cart));
        _spFallsBefore = rc.Falls;
        _spGroundBefore = rc.GroundFree;
        _spRamsBefore = RailEncounterController.Instance?.RunawayRams ?? 0;
    }

    /// <summary>
    /// Ride 4 with the split live, from the departure at stop 3 (steps 4 on). =after: the lead throws it from the seat 1-3 m past the points;
    /// =early: a thrown split (a test hook, a metre inside the early lead, 9 m short on floor 12: the seat never offers it there) takes the train down the spur into the crevasse, the wipe
    /// rises at stop 3 and the next departure sends a runaway again; =hold: thrown by the hook, set back from the seat, a throw from the seat
    /// short of the points held to them; =stall: thrown by the hook, the train stuck short, the watchdog sets it back and grinds on; =lone: thrown
    /// by the hook, the only human downed in the seat, the safe line. Nobody falls but the train's riders.
    /// </summary>
    private void RunSplitRide(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, float rear, CartRun? run, Action<string> Fail)
    {
        _spTime += (float)GetProcessDeltaTime();
        var v = _rwVariant;
        var pump = new Vector2(0, -1);
        if (_rwPass == 2 && v == "after") { LeaveFromTheDark(rc, enc, party, me, Fail); return; }
        if (enc.SplitSwitchNode is not { } split)
        {
            if (_rwStep == 4 && _rwPass == 1) Fail("ride 4's split is not built live");
            else if (_rwClock > 60f) Fail("no live split after the rise");
            return;
        }
        var lever = split.Lever;
        var points = enc.SplitPointsS;
        // the early marks, from the server's early lead (SplitRules.PressReach of the window: 10 m on floor 12): =early's check of the lead
        // half a metre short of it, and the press (the hook's throw on every variant but =after) a metre inside it, still short of the window
        var reach = SplitRules.PressReach(split.WindowAhead);
        float leadCheckShort = reach + 0.5f, pressShort = reach - 1f;
        var front = rc.Party.FrontS;
        var rolling = rc.State.Mode == CartMode.Rolling;

        if (_rwStep == 4 && run != null && !_rwLoose)
        {
            _rwLoose = true;
            var gap = rear - run.FrontS;
            GD.Print($"[split] pass {_rwPass} the runaway broke loose: its front at S={run.FrontS:0.0}, {gap:0.0} m behind the last body; the train's front S={front:0.0}; runaway calls heard {_rwCalls}");
            if (gap < 14f || gap > 16f) { Fail($"it broke loose {gap:0.0} m behind the last body, not 14-16"); return; }
            if (_rwCalls < _rwPass) { Fail("the runaway's call was not heard"); return; }
        }
        if (_rwPass == 2)
        {
            // =early after the rise: the next departure sends a runaway again
            party.ForcedMoveInput = pump;
            if (!_rwLoose) { if (_rwClock > 60f) Fail($"after the rise the runaway did not break loose again (front S={front:0.0}, mode {rc.State.Mode})"); return; }
            party.ForcedMoveInput = null;
            if (SightsMissing() is { } unseen) { Fail(unseen); return; }
            GD.Print($"[split] RESULT ok (early): a split thrown {pressShort:0.0} m short (not by the seat: it was not offered there; \"Not yet\" called at front S={_spNotYetFront:0.0}) took the train down the spur (\"The cart takes the spur!\") and it fell with its front at S={_spFallFront:0.0} (the points + {_spFallFront - points:0.0} m), every rider fallen, the wipe rose at stop 3; boarded and departed again, the runaway broke loose again; nobody fell but the train's");
            _done = true;
            GetTree().Quit(0);
            return;
        }

        switch (_rwStep)
        {
            case 4:
            {
                // who pumps: the lead, but not in =hold once the split is thrown (the window's two presses want time), nor a downed lead
                party.ForcedMoveInput = (v == "hold" && _spThrown9) || me.Downed ? null : pump;
                if (rc.Crossing && v != "early") { Fail($"the train crossed onto the slag line at S={rc.CrossOutS:0.0}"); return; }
                if (rc.Falls != _spFallsBefore && v != "early") { Fail("the train fell"); return; }
                if (rc.State.Mode == CartMode.Wrecked && v != "early") { Fail("the train was wrecked"); return; }

                // the seated window: offered from its start (ruling R-bend) while the runaway comes, never before
                var offered = enc.SeatedLeverFor(me) == lever;
                if (offered && !_spOfferedSeen)
                {
                    _spOfferedSeen = true;
                    GD.Print($"[split] the seat offers the split at front S={front:0.0} (window from S={split.WindowStartS:0.0}, {points - front:0.0} m short of the points): \"{lever.Prompt}\"");
                    if (front < split.WindowStartS - 0.05f) { Fail($"offered at front S={front:0.0}, before the window's start {split.WindowStartS:0.0}"); return; }
                }
                if (!offered && rolling && !me.Downed && !lever.Taken && enc.RunawayComing && front >= split.WindowStartS + 0.5f && front < points + 3f)
                { Fail($"the seat did not offer the split at front S={front:0.0} (window from {split.WindowStartS:0.0})"); return; }

                switch (v)
                {
                    case "after":
                        if (!float.IsNaN(_spNotYetFront)) { Fail("\"Not yet\" was called with the lever never thrown"); return; }
                        if (!_spPressedAfter && rolling && front >= points + 1f && offered && party.LookingAt == lever)
                        {
                            var prompt = lever.Prompt;
                            if (prompt != "Throw the split behind us") { Fail($"past the points the seat offers \"{prompt}\", not \"Throw the split behind us\""); return; }
                            party.VrUse(true);
                            party.VrUse(false);
                            _spPressedAfter = true;
                            _spThrownAtFront = front;
                            GD.Print($"[split] after: the lead throws the split from the seat with the front at S={front:0.0} (the points + {front - points:0.0} m; prompt \"{prompt}\"): thrown={lever.Thrown} by {lever.LastToggledBy}; \"Now\" called at S={_spNowFront:0.0}");
                            if (!lever.Thrown || lever.LastToggledBy != me.State.Name || me.Cart != rc.Cart) { Fail("the seated press past the points did not throw the split, or got the lead out"); return; }
                            if (front > points + 3f) { Fail($"the press came {front - points:0.0} m past the points, not 1-3"); return; }
                            if (float.IsNaN(_spNowFront)) { Fail("\"Now\" was not called before the press"); return; }
                        }
                        if (!_spPressedAfter && front > points + 3f && rolling) { Fail($"no press 1-3 m past the points (front S={front:0.0}, offered {offered})"); return; }
                        break;
                    default:
                        if (pressShort <= split.WindowAhead) { Fail($"the early lead ({reach:0.0} m) leaves no press short of the window ({split.WindowAhead:0.0} m)"); return; }
                        if (v == "early" && !_spLeadChecked && rolling && front >= points - leadCheckShort)
                        {
                            // short of the server's early lead (the window less AcceptLead) a remote press is not taken
                            _spLeadChecked = true;
                            var from = points - reach;
                            var window = front < from && enc.InSeatedWindow(me, lever);
                            GD.Print($"[split] early: short of the early lead (front S={front:0.0}, the lead from S={from:0.0}): the server's seated window {(front < from ? (window ? "yes" : "no") : "not checked (the front already in the lead)")}");
                            if (window) { Fail("the server takes a press short of its early lead"); return; }
                        }
                        if (!_spThrown9 && rolling && front >= points - pressShort)
                        {
                            if (front > points - pressShort + 1f) { Fail($"the front passed {pressShort:0.0} m short unseen (S={front:0.0})"); return; }
                            if (v == "early")
                            {
                                // a press there (inside the early lead, short of the window) is never thrown: the seat does not offer it there; a remote seat's press, judged as the server judges
                                // an InteractIntent (ServerWorld: InSeatedWindow, the late check, then Interact), is taken in the early lead and held
                                party.VrUse(true);
                                party.VrUse(false);
                                if (lever.Thrown || lever.HeldBy != null) { Fail($"the seat's press {pressShort:0.0} m short threw or held the split (the window is not open there)"); return; }
                                var window = enc.InSeatedWindow(me, lever);
                                var late = enc.LateFor(lever);
                                GD.Print($"[split] early: a remote seat's press as the server takes it, {pressShort:0.0} m short (front S={front:0.0}; the seat offers {(offered ? "it" : "nothing")}, the prompt \"{lever.Prompt}\"): seated window {(window ? "yes" : "no")} (from S={points - reach:0.0}), late {late}");
                                if (!window || late != LatePress.None) { Fail($"the server would drop a remote press {pressShort:0.0} m short, in its early lead"); return; }
                                lever.Interact(party, me); // ServerWorld's next step for a press in the window
                                GD.Print($"[split] early: the press taken: thrown={lever.Thrown}, held by {lever.HeldBy ?? "nobody"}");
                                if (lever.Thrown || lever.HeldBy != me.State.Name) { Fail($"the server's press {pressShort:0.0} m short was not held to the points"); return; }
                            }
                            lever.DebugThrow(); // the test's hook: a lever thrown short of its window (no press can do it)
                            _spThrown9 = true;
                            _spThrownAtFront = front;
                            GD.Print($"[split] {v}: the split thrown by the test's hook with the front at S={front:0.0} ({points - front:0.0} m short of the points)");
                        }
                        if (_spThrown9 && float.IsNaN(_spNotYetFront) && front >= _spThrownAtFront + 1.5f) { Fail("\"Not yet\" was not called at once on the early throw"); return; }
                        break;
                }

                if (v == "hold" && _spThrown9)
                {
                    if (!_spSetBackDone && offered && party.LookingAt == lever)
                    {
                        var prompt = lever.Prompt;
                        if (prompt != "Set the split back") { Fail($"the window offers the thrown split as \"{prompt}\", not \"Set the split back\""); return; }
                        party.VrUse(true);
                        party.VrUse(false);
                        _spSetBackDone = true;
                        _spSetBackAt = _spTime;
                        GD.Print($"[split] hold: the lead sets the split back from the seat at front S={front:0.0} ({points - front:0.0} m short): thrown={lever.Thrown}, held by {lever.HeldBy ?? "nobody"}");
                        if (lever.Thrown || lever.HeldBy != null) { Fail("the set-back short of the points was not taken at once"); return; }
                    }
                    if (_spSetBackDone && !_spHeldPressed && _spTime - _spSetBackAt >= SeatedSwitchRules.RepressGuardSeconds + 0.05 && offered && party.LookingAt == lever && front < points)
                    {
                        var prompt = lever.Prompt;
                        if (prompt != "Throw the split — it throws as we pass") { Fail($"short of the points the seat offers \"{prompt}\""); return; }
                        var heldLogs = _spHeldLogs;
                        party.VrUse(true);
                        party.VrUse(false);
                        _spHeldPressed = true;
                        GD.Print($"[split] hold: the lead throws the split from the seat at front S={front:0.0} ({points - front:0.0} m short; prompt \"{prompt}\"): thrown={lever.Thrown}, held by {lever.HeldBy ?? "nobody"}");
                        if (lever.Thrown || lever.HeldBy != me.State.Name) { Fail("the throw short of the points was not held to them"); return; }
                        if (_spHeldLogs != heldLogs + 1) { Fail($"the held throw was not shown (\"{SplitRules.HeldLog}\" logged {_spHeldLogs - heldLogs} time(s))"); return; }
                    }
                    if (front >= points && rolling && !_spHeldPressed && !_spPointsPassed) { Fail($"the set-back and the held throw did not both come short of the points (set back {_spSetBackDone})"); return; }
                }
                if (v == "stall" && _spThrown9)
                {
                    if (!_spStalled && rolling && front >= points - 7f)
                    {
                        rc.DebugStall = true;
                        _spStalled = true;
                        GD.Print($"[split] stall: the train caught with its front at S={front:0.0} ({points - front:0.0} m short of the points), the split thrown");
                    }
                    if (_spStalled && !_spSetBackSeen && rc.GroundFree > _spGroundBefore)
                    {
                        _spSetBackSeen = true;
                        GD.Print($"[split] stall: the watchdog freed the train (grinds {rc.GroundFree - _spGroundBefore}); the split thrown={lever.Thrown}, the cart {rc.State.Mode} at stop {rc.StopIndex}, crossing={rc.Crossing}, falls {rc.Falls - _spFallsBefore}");
                        if (lever.Thrown) { Fail("the watchdog freed the train with the split still thrown"); return; }
                    }
                }
                if (v == "lone" && _spThrown9 && !_spLoneDowned && rolling && front >= points - pressShort + 1f)
                {
                    party.AllowPossession = false; // else solo hands the lead to a seated companion, who would then be the human able to set it back
                    me.State.Mangled = true;       // held down until the points (calm auto-revive never stands a Mangled member up)
                    me.State.Hp = 0;
                    party.OnMemberDowned(me.Index);
                    _spLoneDowned = true;
                    var able = _spRiders.Count(a => SwitchBackRules.AbleToThrow(a.Control, a.Downed, a.Seated && a.Cart == rc.Cart));
                    GD.Print($"[split] lone: the lead {me.State.Name} downed in the seat at front S={front:0.0}: downed={me.Downed} seated={me.Cart == rc.Cart}, {able} rider(s) able to set the split back");
                    if (!me.Downed || me.Cart != rc.Cart || able != 0) { Fail("the lead is not downed in the seat, or someone aboard could still set the split back"); return; }
                }

                // the party's points
                if (!_spPointsPassed && rolling && front >= points)
                {
                    _spPointsPassed = true;
                    GD.Print($"[split] {v}: the train's front at the points (S={front:0.0}): crossing={rc.Crossing} (out at {rc.CrossOutS:0.0}), the split thrown={lever.Thrown} by {lever.LastToggledBy ?? "nobody"}, held by {lever.HeldBy ?? "nobody"}; the runaway {(run == null ? "gone" : $"{rear - run.FrontS:0.0} m behind")}");
                    switch (v)
                    {
                        case "early":
                            if (!rc.Crossing || Mathf.Abs(rc.CrossOutS - points) > 0.01f) { Fail("the thrown split did not take the train down the spur"); return; }
                            _rwStep = 6; _rwClock = 0f;
                            return;
                        case "hold":
                            if (rc.Crossing || !lever.Thrown || lever.LastToggledBy != me.State.Name || lever.HeldBy != null) { Fail("the held throw was not applied just after the train passed straight"); return; }
                            {
                                // a second press just past the points finds it thrown by the hold: it must not undo it (fix round 1 (b))
                                var offeredNow = enc.SeatedLeverFor(me) == lever && party.LookingAt == lever;
                                party.VrUse(true);
                                party.VrUse(false);
                                GD.Print($"[split] hold: the lead presses again just past the points (front S={front:0.0}, offered {offeredNow}, prompt \"{lever.Prompt}\"): thrown={lever.Thrown}");
                                if (!offeredNow || !lever.Thrown) { Fail("a second press just past the points undid the held throw (or was not offered)"); return; }
                            }
                            break;
                        case "lone":
                            if (rc.Crossing || !lever.Thrown || !_spLoneLog) { Fail($"lone: crossing={rc.Crossing}, the split thrown={lever.Thrown}, \"{SplitRules.SafeLineLog}\" logged={_spLoneLog}"); return; }
                            me.State.Mangled = false; // past: calm auto-revive may stand the lead up again
                            party.AllowPossession = true;
                            break;
                        case "after":
                            if (rc.Crossing || lever.Thrown) { Fail("the train did not pass the points straight"); return; }
                            if (float.IsNaN(_spWaitFront) || Mathf.Abs(_spWaitFront - split.WindowStartS) > 0.6f) { Fail($"\"Let us pass the split\" was not called at the window's start (heard at {_spWaitFront:0.0}, window {split.WindowStartS:0.0})"); return; }
                            break;
                    }
                }

                // the runaway at its points: thrown, down the spur and off its end within 3 s
                if (enc.RideRunawayOnSpur && float.IsNaN(_spOnSpurAt))
                {
                    _spOnSpurAt = _spTime;
                    GD.Print($"[split] {v}: the runaway takes the spur (lever taken={lever.Taken}, thrown={lever.Thrown}); the train's front S={front:0.0}");
                }
                if (enc.SplitPlunges > 0 && float.IsNaN(_spPlungeAt))
                {
                    _spPlungeAt = _spTime;
                    GD.Print($"[split] {v}: the runaway went off the spur into the crevasse {_spPlungeAt - _spOnSpurAt:0.00} s after taking it (told in the log: {_spPlungeLog}); rams {enc.RunawayRams - _spRamsBefore}");
                    if (float.IsNaN(_spOnSpurAt) || _spPlungeAt - _spOnSpurAt > 3f) { Fail("the runaway did not go off the spur within 3 s of taking it"); return; }
                }

                if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == 4)
                {
                    party.ForcedMoveInput = null;
                    var rams = enc.RunawayRams - _spRamsBefore;
                    GD.Print($"[split] {v}: arrived at stop 4 after {_rwClock:0.0} s: falls {rc.Falls - _spFallsBefore}, rams {rams}, the runaway {(enc.RideRunawayCart == null ? "gone" : "still on the line")}, plunges {enc.SplitPlunges}");
                    if (rc.Falls != _spFallsBefore) { Fail("a fall on the way"); return; }
                    if (SightsMissing() is { } unseen) { Fail(unseen); return; }
                    switch (v)
                    {
                        case "after":
                            if (!_spPressedAfter || float.IsNaN(_spPlungeAt) || rams != 0) { Fail($"after: pressed {_spPressedAfter}, the runaway plunged {!float.IsNaN(_spPlungeAt)}, rams {rams}"); return; }
                            if (Shooting && !_lgShotsJudged) { Fail($"the legibility shots were not all taken ({string.Join(", ", _lgStats.Keys)})"); return; }
                            if (LightBack(enc, "after the span") is { } dim) { Fail(dim); return; }
                            _spResult = $"[split] RESULT ok (after): the runaway broke loose behind; the seat offered the split from S={split.WindowStartS:0.0} (the straight before the points, {split.WindowAhead:0.0} m) with \"Let us pass the split\" called at S={_spWaitFront:0.0}; the train passed the points straight (S={points:0.0}), \"Now\" called at S={_spNowFront:0.0}, the lead threw it from the seat {_spThrownAtFront - points:0.0} m past the points; no cross; the runaway took the spur and went off it into the crevasse {_spPlungeAt - _spOnSpurAt:0.00} s later; no ram; arrived at stop 4; nobody fell";
                            // pass 2: the light comes back when the party leaves floor 12 from the dark's middle (the runaway held)
                            enc.DebugHoldRunaway(4);
                            _rwPass = 2;
                            _rwLoose = false;
                            rc.Restore(3);
                            GD.Print($"[split] after: pass 2: restored at stop {rc.StopIndex} with ride 4's runaway held, to leave floor 12 from the dark's middle");
                            _rwStep = 1; _rwClock = 0f;
                            return;
                        case "hold":
                            if (float.IsNaN(_spPlungeAt)) { Fail("hold: the runaway did not take the spur"); return; }
                            GD.Print($"[split] RESULT ok (hold): thrown {pressShort:0.0} m short by the test's hook (\"Not yet\" at S={_spNotYetFront:0.0}), set back from the seat at once (no refusal) in the window, thrown again from the seat short of the points and held to them (\"{SplitRules.HeldLog}\" shown); the train passed straight, the held throw thrown just after, a second press just past the points ignored; the runaway took the spur and went into the crevasse; arrived at stop 4; nobody fell");
                            break;
                        case "lone":
                            if (float.IsNaN(_spPlungeAt)) { Fail("lone: the runaway did not take the spur"); return; }
                            GD.Print($"[split] RESULT ok (lone): thrown {pressShort:0.0} m short by the test's hook (\"Not yet\" at S={_spNotYetFront:0.0}), the only human downed in the seat; at the points \"{SplitRules.SafeLineLog}\", the main line, the split left thrown; the runaway took the spur and went into the crevasse; arrived at stop 4; nobody fell");
                            break;
                        case "stall":
                            if (!_spSetBackSeen || rc.GroundFree <= _spGroundBefore || lever.Thrown) { Fail($"stall: the watchdog's set-back seen {_spSetBackSeen}, grinds {rc.GroundFree - _spGroundBefore}"); return; }
                            GD.Print($"[split] RESULT ok (stall): thrown {pressShort:0.0} m short by the test's hook (\"Not yet\" at S={_spNotYetFront:0.0}), the train caught 7 m short of the points; the watchdog set the split back and ground the train on to stop 4, never onto the spur; no fall; rams {rams}; nobody fell");
                            break;
                        default:
                            Fail($"{v}: arrived at stop 4");
                            return;
                    }
                    _done = true;
                    GetTree().Quit(0);
                    return;
                }
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != 3) { Fail($"the train left ride 4 another way (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
                if (_rwClock > 120f) Fail($"ride 4 did not reach stop 4 in 120 s (front S={front:0.0})");
                break;
            }
            case 6:
            {
                // =early: down the spur, the fall with the front at the points + 7, every rider fallen within 1 s
                if (float.IsNaN(_spFallFront)) { if (_rwClock > 10f) Fail($"the train did not fall off the spur within 10 s (front S={front:0.0}, mode {rc.State.Mode})"); break; }
                var off = Mathf.Abs(_spFallFront - (points + SplitRules.FallPast));
                if (_spFallsAtWreck <= _spFallsBefore) { Fail("a wreck, not a fall"); return; }
                if (off > 1f) { Fail($"the train fell with its front at S={_spFallFront:0.00}, {off:0.00} m from the points + 7 ({points + SplitRules.FallPast:0.00})"); return; }
                if (!_spTakesSpurLog || !_spFallLog) { Fail($"\"The cart takes the spur!\" logged {_spTakesSpurLog}, \"The cart runs off the spur into the crevasse!\" logged {_spFallLog}"); return; }
                if (_spWiped && _spFallenAtWreck < _spRiders.Count) { Fail($"{_spRiders.Count - _spFallenAtWreck} of {_spRiders.Count} riders not fallen at the fall, and the party wiped"); return; }
                var standing = _spFallenAtWreck == _spRiders.Count || _spWiped ? new List<Adventurer>() : _spRiders.Where(a => !a.State.Fallen).ToList();
                if (standing.Count > 0) { if (_rwClock > 1f) Fail($"within 1 s of the fall {string.Join(", ", standing.Select(a => a.State.Name))} not fallen"); break; }
                GD.Print($"[split] early: the train fell with its front at S={_spFallFront:0.00} ({off:0.00} m from the points + 7), \"The cart takes the spur!\" and \"The cart runs off the spur into the crevasse!\" logged; every rider fallen [{string.Join(", ", _spRiders.Select(a => a.State.Name))}] ({_spFallenAtWreck} of {_spRiders.Count} at the fall itself)");
                party.ForcedMoveInput = null;
                _rwStep = 7; _rwClock = 0f;
                break;
            }
            case 7:
                if (!_spWiped) { if (_rwClock > 5f) Fail("the party did not wipe within 5 s of the fall"); break; }
                GD.Print("[split] early: the party has fallen: the wipe");
                _rwStep = 8; _rwClock = 0f;
                break;
            case 8:
            {
                if (rc == _spWipeRc || main.Rebuilding) { if (_rwClock > 10f) Fail("the floor was not built again within 10 s of the wipe"); break; }
                _spMayFall.Clear(); // risen: nobody may fall now
                var problems = new List<string>();
                if (rc.StopIndex != 3 || rc.State.Mode != CartMode.Held) problems.Add($"the cart {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 3");
                if (rc.State.Plating < CartRules.MaxPlating || !rc.Cart.Visible) problems.Add($"plating {rc.State.Plating:0}/{CartRules.MaxPlating:0}, visible={rc.Cart.Visible}");
                foreach (var a in party.Active)
                    if (a.State.Fallen || a.Downed || a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) > 10f || !CrevasseRules.SetDownOk(layout, DungeonBuilder.WorldToCell(a.GlobalPosition)))
                        problems.Add($"{a.State.Name} fallen={a.State.Fallen} downed={a.Downed} {a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart");
                if (problems.Count > 0) { if (_rwClock > 10f) Fail($"the rise: {string.Join("; ", problems)}"); break; }
                GD.Print($"[split] early: the rise at stop {rc.StopIndex} after {_rwClock:0.0} s: the cart {rc.State.Mode}, plating {rc.State.Plating:0}; heroes [{string.Join(", ", party.Active.Select(a => $"{a.State.Name} {a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m"))}]");
                _rwPass = 2;
                _rwLoose = false;
                _rwStep = 1; _rwClock = 0f; // board and depart again
                break;
            }
        }
    }

    // ---- Task 7: seen in the dark (Ruling 23, the legibility gate) -------------------------------------------------

    /// <summary>The step =after's pass 2 waits in once it left floor 12 from the dark's middle (the flow's floor-12 guard skipped).</summary>
    private const int LeftFloorStep = 20;
    /// <summary>The scene area a shot's luminance is judged on, as shares of the image: the HUD cropped out (the floor and objective lines top
    /// left, the minimap top right, and the log, the subtitles and the portraits below the middle).</summary>
    private const float CropX0 = 0.2f, CropX1 = 0.8f, CropY0 = 0.16f, CropY1 = 0.53f;
    /// <summary>A lamp's pixels are counted within this share of the image's height either way of where the lamp projects.</summary>
    private const float LampWindow = 1f / 18f;
    /// <summary>The orange about the runaway's lamp must drop by this much when its light is hidden (behind against behind_nolamp).</summary>
    private const int RunawayLightDrop = 20;
    /// <summary>Every shot the windowed =after run takes, in the order of the line; the gate is judged once all are in.</summary>
    private static readonly string[] LgShotNames = { "lit", "behind", "behind_nolamp", "behind_seat", "dark", "split", "window", "lever" };
    private bool _lgSplitSight, _lgRunawaySight, _lgBaseTaken, _lgShotsJudged;
    /// <summary>=after's pass 2 saw this peer's view dark (darkness over 0.5) in the dark's middle: only then does its RESULT claim the light came back.</summary>
    private bool _lgDarkSeen;
    private float _lgTime, _lgLooseAt = float.NaN, _lgOnFloorAt = float.NaN;
    private float _lgBaseAmbient = float.NaN;
    private readonly Dictionary<Adventurer, float> _lgTorchBase = new();
    /// <summary>=after's RESULT line, printed once the light is back after leaving floor 12 (pass 2).</summary>
    private string? _spResult;
    private readonly HashSet<string> _lgQueued = new();
    private readonly Queue<LgShot> _lgShots = new();
    private LgShot? _lgShot;
    private readonly Dictionary<string, (float Luma, int Px)> _lgStats = new();

    private sealed class LgShot
    {
        public string Name = "";
        /// <summary>Sets the view up and returns the camera that draws it.</summary>
        public Func<Camera3D> Pose = null!;
        /// <summary>The lamp whose pixels are counted (null: none), and its colour test (red, else orange).</summary>
        public Func<Vector3?>? Lamp;
        public bool Red;
        /// <summary>Gives the view back after the shot (handed the shot's camera).</summary>
        public Action<Camera3D>? Done;
        public Camera3D? Camera;
        public int Frames;
    }

    /// <summary>This peer draws (a windowed run): the dark darkens its view and torches.</summary>
    private static bool Drawn => !DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase);
    /// <summary>The windowed =after run with --screenshot=PATH saves the seat's shots and judges them.</summary>
    private bool Shooting => _screenshotPath != null && _rwVariant == "after" && Drawn;
    private static Godot.Environment? Env() => Main.Instance?.GetNodeOrNull<WorldEnvironment>("World/Env")?.Environment;
    /// <summary>One of Main's private light values (the view's darkness, the floor's base ambient and fog emission), read for the check; NaN if it is gone.</summary>
    private static float MainFloat(string field) =>
        typeof(Main).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(Main.Instance) is float f ? f : float.NaN;

    /// <summary>The light values the checks read from Main: null when all are there, else the names Main lacks (renamed or retyped).</summary>
    private static string? MainFieldsMissing()
    {
        var missing = new[] { "_darkness", "_baseAmbientEnergy", "_baseFogEmission" }
            .Where(f => typeof(Main).GetField(f, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.FieldType != typeof(float)).ToList();
        return missing.Count == 0 ? null : $"Main has no float field {string.Join(", ", missing)} (the light checks read it)";
    }

    /// <summary>
    /// The dark's pocket lamps are out in the built scene (Ruling 16), not just counted: every RailLamp{i} inside the span (the darkness at it
    /// 1) is hidden and its RailLantern{i} has no emissive material left; some lamp outside the dark is still lit. Null, or why not.
    /// </summary>
    private static string? DarkLampsOut(Node3D root, RailEncounterController enc)
    {
        if (root.GetNodeOrNull<Node3D>("Rails") is not { } rails) return "floor 12 has no Rails node";
        int inside = 0, ramps = 0, outsideLit = 0;
        var lit = new List<string>();
        var names = new List<string>();
        foreach (var light in rails.GetChildren().OfType<Light3D>())
        {
            var name = light.Name.ToString();
            if (!name.StartsWith("RailLamp")) continue;
            var d = enc.DarknessAt(light.GlobalPosition);
            var glows = rails.GetNodeOrNull<Node>("RailLantern" + name["RailLamp".Length..]) is { } lantern && Emissive(lantern);
            if (d >= 1f)
            {
                inside++;
                names.Add(name);
                if (light.Visible || glows) lit.Add($"{name} (light visible {light.Visible}, lantern glowing {glows})");
            }
            else if (d > 0f) ramps++;
            else if (light.Visible) outsideLit++;
        }
        GD.Print($"[runaway] dark lamps in the built scene: {inside} RailLamp(s) inside the span (darkness 1 at the lamp: {string.Join(", ", names)}), {inside - lit.Count} hidden with their lanterns dark; {ramps} in the ramps; {outsideLit} lit outside the dark");
        if (inside == 0) return "no RailLamp lies inside the dark span";
        if (lit.Count > 0) return $"lamps inside the dark are lit: {string.Join(", ", lit)}";
        if (outsideLit == 0) return "no RailLamp outside the dark is lit";
        return null;
    }

    /// <summary>Some mesh under <paramref name="node"/> draws with an emissive material.</summary>
    private static bool Emissive(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
                if (mi.GetActiveMaterial(s) is BaseMaterial3D { EmissionEnabled: true }) return true;
        foreach (var child in node.GetChildren()) if (Emissive(child)) return true;
        return false;
    }

    /// <summary>A layer-1 ray from <paramref name="eye"/> to <paramref name="to"/>: "clear", or "blocked by" the first collider and how far along.</summary>
    private static string Sight(Node3D from, Vector3 eye, Vector3 to)
    {
        var hit = from.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, to, 1));
        return hit.Count == 0 ? "clear" : $"blocked by {(hit["collider"].AsGodotObject() as Node)?.Name} at {hit["position"].AsVector3().DistanceTo(eye):0.0} m";
    }

    /// <summary>
    /// Ride 4's legibility (Ruling 23), pass 1 from the departure at stop 3: the light before the dark; layer-1 rays from the seated lead's eye
    /// to the split's two lamps with the front 8 m short of the points and to the runaway's lamp 3 s after it breaks loose (every variant,
    /// headless too: a blocked ray fails the flow); and, windowed =after with --screenshot, the seat's shots and their checks. True when it failed.
    /// </summary>
    private bool Legibility(RailController rc, RailEncounterController enc, Adventurer me, CartRun? run, Action<string> fail)
    {
        _lgTime += (float)GetProcessDeltaTime();
        var front = rc.Party.FrontS;
        var rolling = rc.State.Mode == CartMode.Rolling;
        if (!_lgBaseTaken)
        {
            _lgBaseTaken = true;
            _lgBaseAmbient = Env()?.AmbientLightEnergy ?? float.NaN;
            _lgTorchBase.Clear();
            foreach (var a in Main.Instance.Party.Adventurers) if (IsInstanceValid(a)) _lgTorchBase[a] = a.Torch.LightEnergy;
            GD.Print($"[runaway] light before the dark (front S={front:0.0}, the dark from S={enc.DarkFromS:0.0}): ambient {_lgBaseAmbient:0.000}, torches [{string.Join(", ", _lgTorchBase.Select(t => $"{t.Key.State.Name} {t.Value:0.00}"))}]; this peer {(Drawn ? "draws" : "is headless (no darkness drawn)")}");
        }
        if (!_lgSplitSight && rolling && enc.SplitSwitchNode is { } split && front >= enc.SplitPointsS - 8f)
        {
            _lgSplitSight = true;
            var eye = me.Camera.GlobalPosition;
            var lever = Sight(me, eye, split.AmberLampPoint);
            var spur = Sight(me, eye, split.RedLampPoint);
            GD.Print($"[runaway] sight: lever {lever}, spur end {spur}");
            GD.Print($"[runaway] sight from the seated lead's eye with the front at S={front:0.0} ({enc.SplitPointsS - front:0.0} m short of the points): the lever's amber lamp {eye.DistanceTo(split.AmberLampPoint):0.0} m off, the spur's red lamp {eye.DistanceTo(split.RedLampPoint):0.0} m off");
            if (lever != "clear" || spur != "clear") { fail($"the split's lamps are not in sight from the seat 8 m short of the points (lever {lever}, spur end {spur})"); return true; }
        }
        if (run != null && float.IsNaN(_lgLooseAt)) _lgLooseAt = _lgTime;
        if (!_lgRunawaySight && !float.IsNaN(_lgLooseAt) && _lgTime - _lgLooseAt >= 3f)
        {
            _lgRunawaySight = true;
            if (enc.RideRunawayCart?.GetNodeOrNull<Node3D>("LampLens") is not { } lens) { fail("3 s after it broke loose the runaway (or its lamp) is gone"); return true; }
            var eye = me.Camera.GlobalPosition;
            var sight = Sight(me, eye, lens.GlobalPosition);
            GD.Print($"[runaway] sight: runaway lamp {sight} at {eye.DistanceTo(lens.GlobalPosition):0.0} m");
            if (sight != "clear") { fail($"the runaway's lamp is not in sight from the seat 3 s after it broke loose: {sight}"); return true; }
            if (Shooting) QueueBehindShots(rc, enc, me);
        }
        return Shooting && ShotsStep(rc, enc, me, front, rolling, fail);
    }

    /// <summary>Both sight checks ran (the split's lamps 8 m short of the points, the runaway's 3 s after it broke loose): null, or which did not.</summary>
    private string? SightsMissing()
    {
        var missing = new List<string>();
        if (!_lgSplitSight) missing.Add("the split's lamps (8 m short of the points)");
        if (!_lgRunawaySight) missing.Add("the runaway's lamp (3 s after it broke loose)");
        return missing.Count == 0 ? null : $"the sight check never ran: {string.Join("; ", missing)}";
    }

    /// <summary>Queue a shot once by its name.</summary>
    private void QueueShot(string name, Func<Camera3D> pose, Func<Vector3?>? lamp = null, bool red = false, Action<Camera3D>? done = null)
    {
        if (_lgQueued.Add(name)) _lgShots.Enqueue(new LgShot { Name = name, Pose = pose, Lamp = lamp, Red = red, Done = done });
    }

    /// <summary>The seated lead faces ahead along the seat, level.</summary>
    private static Func<Camera3D> Ahead(RailController rc, Adventurer me) => () =>
    {
        var forward = -rc.Cart.SeatTransform(me.Seat).Basis.Z;
        forward.Y = 0f;
        me.AimAt(me.Camera.GlobalPosition + forward.Normalized() * 20f);
        return me.Camera;
    };

    /// <summary>A luminance shot is of the scene alone: the HUD is hidden while it is drawn (a level-up banner or a subtitle in the crop would light it).</summary>
    private static Func<Camera3D> Bare(Func<Camera3D> pose) => () =>
    {
        Main.Instance.Hud.Visible = false;
        return pose();
    };

    private static void HudBack(Camera3D _) => Main.Instance.Hud.Visible = true;

    /// <summary>A camera that draws as <paramref name="like"/> does, for a view the seat cannot turn to.</summary>
    private static Camera3D TempCamera(Camera3D like) => new()
    {
        Name = "LegibilityCamera", Fov = like.Fov, Near = like.Near, Far = like.Far, CullMask = like.CullMask, Environment = like.Environment,
        Attributes = like.Attributes, KeepAspect = like.KeepAspect,
    };

    /// <summary>
    /// The runaway behind, 3 s after it broke loose: "behind" (judged) is the rearmost view, the seated eye's height at the party cart's back,
    /// turned to look back down the line (a seat turns 150° at most, and from the front row the riders behind fill a view turned right round);
    /// "behind_nolamp" is the same view at once after with the runaway's light (its Lamp, LampGlow and LampLens) hidden, then shown again: the
    /// orange about the lamp must drop by <see cref="RunawayLightDrop"/>, so what is counted is the runaway's light (lens and beam), not the line's
    /// own glints; "behind_seat" (shown only) is the seated lead's own eye turned 180°.
    /// </summary>
    private void QueueBehindShots(RailController rc, RailEncounterController enc, Adventurer me)
    {
        Vector3? Lamp() => enc.RideRunawayCart?.GetNodeOrNull<Node3D>("LampLens")?.GlobalPosition;
        void Back(Camera3D cam) { me.Camera.MakeCurrent(); cam.QueueFree(); }
        Camera3D Rear()
        {
            var cam = TempCamera(me.Camera);
            rc.Cart.AddChild(cam);
            var eyeY = rc.Cart.ToLocal(rc.Cart.SeatTransform(me.Seat).Origin).Y + Adventurer.SeatedEyeHeight;
            cam.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0f, eyeY, CartRules.HalfLength + 0.05f));
            cam.MakeCurrent();
            return cam;
        }
        void RunawayLight(bool on)
        {
            if (enc.RideRunawayCart is not { } wagon) return;
            foreach (var name in new[] { "Lamp", "LampGlow", "LampLens" })
                if (wagon.GetNodeOrNull<Node3D>(name) is { } part) part.Visible = on;
        }
        QueueShot("behind", Rear, Lamp, false, Back);
        QueueShot("behind_nolamp", () => { RunawayLight(false); return Rear(); }, Lamp, false, cam => { RunawayLight(true); Back(cam); });
        QueueShot("behind_seat", () =>
        {
            var cam = TempCamera(me.Camera);
            me.Camera.AddChild(cam);
            cam.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);
            cam.MakeCurrent();
            return cam;
        }, Lamp, false, Back);
    }

    /// <summary>
    /// The windowed =after run's shots from the seat (each saved as PATH_name.png): "lit" with the front 10 m before the dark and "dark" in its
    /// middle (both facing ahead, level, the HUD hidden), "split" 8 m short of the points facing ahead, "window" at the window's start and "lever" at the lever's S
    /// looking at the spur's end and its red lamp, and the two behind (<see cref="QueueBehindShots"/>). Each is posed, drawn for three frames and
    /// saved; then the gate: the dark's luminance over the lit's on the scene crop at most 0.6, at least 20 red pixels about the split's red lamp,
    /// and at least 20 orange pixels about the runaway's lamp that go when its light is hidden. True when it failed.
    /// </summary>
    private bool ShotsStep(RailController rc, RailEncounterController enc, Adventurer me, float front, bool rolling, Action<string> fail)
    {
        if (rolling && enc.SplitSwitchNode is { } split)
        {
            var spurEnd = split.SpurEnd;
            var red = split.RedLampPoint;
            Vector3? Red() => red;
            if (front >= enc.DarkFromS - 10f) QueueShot("lit", Bare(Ahead(rc, me)), done: HudBack);
            if (front >= enc.DarkFromS + DarkRules.Length / 2f) QueueShot("dark", Bare(Ahead(rc, me)), done: HudBack);
            if (front >= enc.SplitPointsS - 8f) QueueShot("split", Ahead(rc, me), Red, true);
            if (front >= split.WindowStartS) QueueShot("window", () => { me.AimAt((spurEnd + red) / 2f); return me.Camera; }, Red, true);
            if (front >= split.LeverS) QueueShot("lever", () => { me.AimAt(spurEnd); return me.Camera; }, Red, true, _ => Ahead(rc, me)());
        }
        if (_lgShot == null && _lgShots.TryDequeue(out var next))
        {
            _lgShot = next;
            next.Camera = next.Pose();
            next.Frames = 3;
        }
        else if (_lgShot is { } shot && --shot.Frames <= 0)
        {
            _lgShot = null;
            var img = GetViewport().GetTexture().GetImage();
            var path = System.IO.Path.ChangeExtension(_screenshotPath!, null) + "_" + shot.Name + ".png";
            var err = img.SavePng(path);
            Vector2? at = null;
            if (shot.Lamp?.Invoke() is { } lamp && shot.Camera is { } cam && IsInstanceValid(cam) && !cam.IsPositionBehind(lamp))
                at = cam.UnprojectPosition(lamp) * (img.GetWidth() / GetViewport().GetVisibleRect().Size.X);
            var (luma, px) = ShotStats(img, at, shot.Red);
            _lgStats[shot.Name] = (luma, px);
            var counted = shot.Lamp == null ? "" : $"; {(shot.Red ? "red" : "orange")} px {px} within {(int)(img.GetHeight() * LampWindow)} px of the lamp {(at is { } a ? $"at ({a.X:0},{a.Y:0})" : "(off the view)")}";
            GD.Print($"[runaway] shot {shot.Name} ({err}) {path}: the front at S={front:0.0}, {img.GetWidth()}x{img.GetHeight()}; the scene's mean luminance {luma:0.000}{counted}");
            if (shot.Camera is { } used) shot.Done?.Invoke(used);
            if (shot.Name == "dark" && DarkHere(enc, me) is { } light) { fail(light); return true; }
        }
        if (!_lgShotsJudged && LgShotNames.All(_lgStats.ContainsKey))
        {
            _lgShotsJudged = true;
            var r = _lgStats["dark"].Luma / _lgStats["lit"].Luma;
            int n = _lgStats["split"].Px, lampOn = _lgStats["behind"].Px, lampOff = _lgStats["behind_nolamp"].Px, m = lampOn - lampOff;
            GD.Print($"[runaway] shots: dark/lit luminance {r:0.00}; split red px {n}; behind orange px {m} (the runaway's light: {lampOn} with it, {lampOff} with its Lamp, LampGlow and LampLens hidden)");
            GD.Print($"[runaway] shots judged on the scene crop x {CropX0:P0}-{CropX1:P0}, y {CropY0:P0}-{CropY1:P0} of the image (the HUD hidden and cropped out; lit {_lgStats["lit"].Luma:0.000} and dark {_lgStats["dark"].Luma:0.000} both from the seat facing ahead, level; Rec. 709 luma of the image's values); lamp pixels counted about where each lamp projects (window {_lgStats["window"].Px} red, lever {_lgStats["lever"].Px} red, behind_seat {_lgStats["behind_seat"].Px} orange: shown, not judged)");
            if (r > 0.6f || n < 20 || m < RunawayLightDrop) { fail($"the dark does not read: dark/lit luminance {r:0.00} (want <= 0.6), split red px {n} (want >= 20), behind orange px {m} from the runaway's light (want >= {RunawayLightDrop})"); return true; }
        }
        return false;
    }

    /// <summary>A shot's mean luminance over the scene crop, and the red (or orange) pixels within <see cref="LampWindow"/> of <paramref name="lamp"/> (none without one).</summary>
    private static (float Luma, int Px) ShotStats(Image shot, Vector2? lamp, bool red)
    {
        var img = (Image)shot.Duplicate();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        var data = img.GetData();
        int x0 = (int)(w * CropX0), x1 = (int)(w * CropX1), y0 = (int)(h * CropY0), y1 = (int)(h * CropY1);
        double sum = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * w + x) * 4;
                sum += 0.2126 * data[i] + 0.7152 * data[i + 1] + 0.0722 * data[i + 2];
            }
        var luma = (float)(sum / 255.0 / ((x1 - x0) * (y1 - y0)));
        var px = 0;
        if (lamp is { } c)
        {
            var r = (int)(h * LampWindow);
            for (var y = Math.Max(0, (int)c.Y - r); y < Math.Min(h, (int)c.Y + r); y++)
                for (var x = Math.Max(0, (int)c.X - r); x < Math.Min(w, (int)c.X + r); x++)
                {
                    var i = (y * w + x) * 4;
                    float cr = data[i] / 255f, cg = data[i + 1] / 255f, cb = data[i + 2] / 255f;
                    if (red ? cr > 0.6f && cg < 0.3f && cb < 0.3f : cr > 0.7f && cg > 0.3f && cg < 0.65f && cb < 0.35f) px++;
                }
        }
        return (luma, px);
    }

    /// <summary>The control at the dark shot: in the dark's middle the view is wholly dark, the ambient at its share and the lead's torch at its. Null, or why not.</summary>
    private string? DarkHere(RailEncounterController enc, Adventurer me)
    {
        if (MainFieldsMissing() is { } missing) return missing;
        var d = MainFloat("_darkness");
        var ambient = Env()?.AmbientLightEnergy ?? float.NaN;
        var torch = me.Torch.LightEnergy;
        var torchBase = _lgTorchBase.TryGetValue(me, out var t) ? t : float.NaN;
        GD.Print($"[runaway] light in the dark's middle: the view's darkness {d:0.00} (at the lead {enc.DarknessAt(me.GlobalPosition):0.00}); ambient {ambient:0.000} = x{ambient / _lgBaseAmbient:0.00} of before (want x{DarkRules.AmbientKept:0.00}); the lead's torch {torch:0.00} = x{torch / torchBase:0.00} (want x{DarkRules.TorchKept:0.00})");
        if (!(d >= 0.99f) || !(Mathf.Abs(ambient / _lgBaseAmbient - DarkRules.AmbientKept) <= 0.01f) || !(Mathf.Abs(torch / torchBase - DarkRules.TorchKept) <= 0.01f))
            return "the dark's middle is not dark: the view, the ambient or the lead's torch is not at its share";
        return null;
    }

    /// <summary>
    /// The light is all back <paramref name="when"/>: the view's darkness 0 (and none at the camera), the ambient energy and the volumetric fog's
    /// emission at the floor's base (Main's), each hero's torch at its energy before the dark, and on floor 12 (<paramref name="sameFloor"/>) the
    /// ambient as it was before the dark. Null, or why not.
    /// </summary>
    private string? LightBack(RailEncounterController? enc, string when, bool sameFloor = true)
    {
        if (MainFieldsMissing() is { } missing) return missing;
        var env = Env();
        var d = MainFloat("_darkness");
        float baseAmbient = MainFloat("_baseAmbientEnergy"), baseFog = MainFloat("_baseFogEmission");
        float ambient = env?.AmbientLightEnergy ?? float.NaN, fog = env?.VolumetricFogEmissionEnergy ?? float.NaN;
        var here = enc != null && IsInstanceValid(enc) && GetViewport().GetCamera3D() is { } cam ? enc.DarknessAt(cam.GlobalPosition) : 0f;
        var bad = new List<string>();
        var torches = new List<string>();
        foreach (var (a, e) in _lgTorchBase)
        {
            if (!IsInstanceValid(a)) continue;
            torches.Add($"{a.State.Name} {a.Torch.LightEnergy:0.00}/{e:0.00}");
            if (!Mathf.IsEqualApprox(a.Torch.LightEnergy, e)) bad.Add($"{a.State.Name}'s torch {a.Torch.LightEnergy:0.00}, not {e:0.00}");
        }
        if (d != 0f || here != 0f) bad.Add($"the view's darkness {d:0.00} (at the camera {here:0.00})");
        if (!Mathf.IsEqualApprox(ambient, baseAmbient)) bad.Add($"ambient {ambient:0.000}, not the floor's {baseAmbient:0.000}");
        if (sameFloor && !Mathf.IsEqualApprox(ambient, _lgBaseAmbient)) bad.Add($"ambient {ambient:0.000}, not {_lgBaseAmbient:0.000} as before the dark");
        if (env is { VolumetricFogEnabled: true } && !Mathf.IsEqualApprox(fog, baseFog)) bad.Add($"fog emission {fog:0.000}, not the floor's {baseFog:0.000}");
        GD.Print($"[runaway] light {when} (floor {Game.Instance.CurrentFloor}): the view's darkness {d:0.00} (at the camera {here:0.00}); ambient {ambient:0.000} (the floor's base {baseAmbient:0.000}{(sameFloor ? $", before the dark {_lgBaseAmbient:0.000}" : "")}); fog emission {fog:0.000} (base {baseFog:0.000}, volumetric {env?.VolumetricFogEnabled}); torches now/before [{string.Join(", ", torches)}]: {(bad.Count == 0 ? "all back" : "NOT back")}");
        return bad.Count == 0 ? null : $"the light {when} is not back: {string.Join("; ", bad)}";
    }

    /// <summary>=after's pass 2 (the runaway held): pump to the dark's middle, see the view dark there (when drawn), and leave floor 12 for floor 13 from it.</summary>
    private void LeaveFromTheDark(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> fail)
    {
        party.ForcedMoveInput = new Vector2(0, -1);
        if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != 3) { fail($"pass 2 left ride 4 another way (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
        var front = rc.Party.FrontS;
        if (front < enc.DarkFromS + DarkRules.Length / 2f) { if (_rwClock > 60f) fail($"pass 2 did not reach the dark's middle in 60 s (front S={front:0.0})"); return; }
        party.ForcedMoveInput = null;
        if (MainFieldsMissing() is { } missing) { fail(missing); return; }
        var d = MainFloat("_darkness");
        _lgDarkSeen = d > 0.5f;
        var torch = me.Torch.LightEnergy;
        var torchBase = _lgTorchBase.TryGetValue(me, out var t) ? t : float.NaN;
        GD.Print($"[runaway] pass 2 in the dark's middle (front S={front:0.0}): the view's darkness {d:0.00}, ambient {Env()?.AmbientLightEnergy:0.000}, the lead's torch {torch:0.00} (before {torchBase:0.00}); leaving floor 12 for floor 13 from here");
        if (Drawn && (!(d >= 0.99f) || !(torch < torchBase))) { fail("pass 2: the view or the lead's torch is not dark in the dark's middle, so leaving from it proves nothing"); return; }
        _rwStep = LeftFloorStep; _rwClock = 0f;
        Game.Instance.ChangeFloor(+1);
    }

    /// <summary>=after once it left floor 12 from the dark: a second after floor 13 is built, the light is all back (<see cref="LightBack"/>); then the RESULT.</summary>
    private void LightAfterLeaving()
    {
        _rwClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[split] RESULT FAIL (after): {why}"); _done = true; GetTree().Quit(1); }
        var main = Main.Instance;
        if (Game.Instance.CurrentFloor != 13 || main == null || !main.InGame || main.Rebuilding || main.Builder.CurrentFloor != 13 || main.Builder.FloorRoot == null)
        {
            _lgOnFloorAt = float.NaN;
            if (_rwClock > 30f) Fail($"floor 13 was not built within 30 s of leaving floor 12 (floor {Game.Instance.CurrentFloor}, built {main?.Builder.CurrentFloor})");
            return;
        }
        if (float.IsNaN(_lgOnFloorAt)) _lgOnFloorAt = _rwClock;
        if (_rwClock - _lgOnFloorAt < 1f) return;
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) ? e : null;
        if (LightBack(enc, "after leaving floor 12 from the dark's middle", sameFloor: false) is { } dim) { Fail(dim); return; }
        GD.Print(_spResult + (_lgDarkSeen
            ? "; the light back after the dark and after leaving floor 12 from its middle"
            : "; the light's return not judged headless (the dark is not drawn on this peer)"));
        _done = true;
        GetTree().Quit(0);
    }
}
