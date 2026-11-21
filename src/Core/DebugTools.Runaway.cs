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
// and the cart departs on ride 4; nobody presses anything. =tail (the default): the lead pumps throughout; the runaway breaks loose with
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
// within 3 s, no ram, stop 4. =early: a press 9 m short throws nothing (the seat does not offer it there); the test's hook throws it, "not
// yet" is called, the train takes the spur and falls with its front within 1 m of the points + 7, every rider fallen within 1 s, the wipe
// rises at stop 3 with every hero up within 10 m of the whole cart, and the next departure sends a runaway again. =hold: the hook throws it 9 m
// short ("not yet"), the lead sets it back from the seat at the window's start (taken at once), then throws it from the seat short of the
// points: held, thrown just after the train passes straight; the runaway takes the spur. =stall: the hook throws it, the train is caught 7 m
// short (RailController.DebugStall) and the watchdog sets the split back and grinds on to stop 4: no fall. =lone: the hook throws it and the
// only human (possession off) is downed in the seat: at the points the safe line, the lever left thrown, the runaway takes the spur. Every
// split variant: nobody falls but the train's riders. Prints [split] lines and a RESULT.
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

        switch (_rwStep)
        {
            case 0 when _rwClock >= 1.5f:
            {
                if (enc.SetPieceNode("r4_runaway") is null || enc.SetPieceNode("r4_split") is null) { Fail("floor 12 lacks r4_runaway or r4_split"); return; }
                if (enc.SplitRide != 4 || float.IsNaN(enc.SplitPointsS) || !enc.SplitIsLive) { Fail($"ride 4's split is not live (ride {enc.SplitRide}, points S={enc.SplitPointsS:0.0}, live {enc.SplitIsLive})"); return; }
                GD.Print($"[runaway] floor 12 ({_rwVariant}): r4_split live at party S={enc.SplitPointsS:0.0}; stops [{string.Join(",", rc.StopS.Select(s => s.ToString("0.0")))}]; lethality {TierRules.Lethality}, party damage scale {TierRules.PartyDamageScale:0.00}");
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
    /// =early: a thrown split (a test hook, 9 m short: the seat never offers it there) takes the train down the spur into the crevasse, the wipe
    /// rises at stop 3 and the next departure sends a runaway again; =hold: thrown by the hook, set back from the seat, a throw from the seat
    /// short of the points held to them; =stall: thrown by the hook, the train stuck short, the watchdog sets it back and grinds on; =lone: thrown
    /// by the hook, the only human downed in the seat, the safe line. Nobody falls but the train's riders.
    /// </summary>
    private void RunSplitRide(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, float rear, CartRun? run, Action<string> Fail)
    {
        _spTime += (float)GetProcessDeltaTime();
        var v = _rwVariant;
        var pump = new Vector2(0, -1);
        if (enc.SplitSwitchNode is not { } split)
        {
            if (_rwStep == 4 && _rwPass == 1) Fail("ride 4's split is not built live");
            else if (_rwClock > 60f) Fail("no live split after the rise");
            return;
        }
        var lever = split.Lever;
        var points = enc.SplitPointsS;
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
            GD.Print($"[split] RESULT ok (early): a split thrown 9 m short (not by the seat: it was not offered there; \"Not yet\" called at front S={_spNotYetFront:0.0}) took the train down the spur (\"The cart takes the spur!\") and it fell with its front at S={_spFallFront:0.0} (the points + {_spFallFront - points:0.0} m), every rider fallen, the wipe rose at stop 3; boarded and departed again, the runaway broke loose again; nobody fell but the train's");
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
                        if (!_spThrown9 && rolling && front >= points - 9f)
                        {
                            if (front > points - 8f) { Fail($"the front passed 9 m short unseen (S={front:0.0})"); return; }
                            if (v == "early")
                            {
                                // a press 9 m short is never thrown: the seat does not offer it there, and the lever itself refuses it (or holds it)
                                party.VrUse(true);
                                party.VrUse(false);
                                lever.Interact(party, me);
                                GD.Print($"[split] early: the lead presses 9 m short (front S={front:0.0}; the seat offers {(offered ? "it" : "nothing")}, the lever's prompt \"{lever.Prompt}\"): thrown={lever.Thrown}, held by {lever.HeldBy ?? "nobody"}");
                                if (lever.Thrown) { Fail("a press 9 m short threw the split"); return; }
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
                        if (prompt != "Throw the split — we take the spur!") { Fail($"short of the points the seat offers \"{prompt}\""); return; }
                        party.VrUse(true);
                        party.VrUse(false);
                        _spHeldPressed = true;
                        GD.Print($"[split] hold: the lead throws the split from the seat at front S={front:0.0} ({points - front:0.0} m short; prompt \"{prompt}\"): thrown={lever.Thrown}, held by {lever.HeldBy ?? "nobody"}");
                        if (lever.Thrown || lever.HeldBy != me.State.Name) { Fail("the throw short of the points was not held to them"); return; }
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
                if (v == "lone" && _spThrown9 && !_spLoneDowned && rolling && front >= points - 8f)
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
                    switch (v)
                    {
                        case "after":
                            if (!_spPressedAfter || float.IsNaN(_spPlungeAt) || rams != 0) { Fail($"after: pressed {_spPressedAfter}, the runaway plunged {!float.IsNaN(_spPlungeAt)}, rams {rams}"); return; }
                            GD.Print($"[split] RESULT ok (after): the runaway broke loose behind; the seat offered the split from S={split.WindowStartS:0.0} (the straight before the points, {split.WindowAhead:0.0} m) with \"Let us pass the split\" called at S={_spWaitFront:0.0}; the train passed the points straight (S={points:0.0}), \"Now\" called at S={_spNowFront:0.0}, the lead threw it from the seat {_spThrownAtFront - points:0.0} m past the points; no cross; the runaway took the spur and went off it into the crevasse {_spPlungeAt - _spOnSpurAt:0.00} s later; no ram; arrived at stop 4; nobody fell");
                            break;
                        case "hold":
                            if (float.IsNaN(_spPlungeAt)) { Fail("hold: the runaway did not take the spur"); return; }
                            GD.Print($"[split] RESULT ok (hold): thrown 9 m short by the test's hook (\"Not yet\" at S={_spNotYetFront:0.0}), set back from the seat at once (no refusal) in the window, thrown again from the seat short of the points and held to them; the train passed straight, the held throw thrown just after; the runaway took the spur and went into the crevasse; arrived at stop 4; nobody fell");
                            break;
                        case "lone":
                            if (float.IsNaN(_spPlungeAt)) { Fail("lone: the runaway did not take the spur"); return; }
                            GD.Print($"[split] RESULT ok (lone): thrown 9 m short by the test's hook (\"Not yet\" at S={_spNotYetFront:0.0}), the only human downed in the seat; at the points \"{SplitRules.SafeLineLog}\", the main line, the split left thrown; the runaway took the spur and went into the crevasse; arrived at stop 4; nobody fell");
                            break;
                        case "stall":
                            if (!_spSetBackSeen || rc.GroundFree <= _spGroundBefore || lever.Thrown) { Fail($"stall: the watchdog's set-back seen {_spSetBackSeen}, grinds {rc.GroundFree - _spGroundBefore}"); return; }
                            GD.Print($"[split] RESULT ok (stall): thrown 9 m short by the test's hook (\"Not yet\" at S={_spNotYetFront:0.0}), the train caught 7 m short of the points; the watchdog set the split back and ground the train on to stop 4, never onto the spur; no fall; rams {rams}; nobody fell");
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
}
