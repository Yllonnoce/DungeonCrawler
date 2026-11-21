using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-runaway[=tail|ram] ------------------------------------------------------------------------------------
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
        void Fail(string why) { GD.Print($"[runaway] RESULT FAIL ({_rwVariant}): {why}"); party.ForcedMoveInput = null; PumperCap = null; _done = true; GetTree().Quit(1); }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_rwClock > 3f) Fail("floor 12 has no line"); return; }
        RunawayListen(rc);
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill(); // the flow owns the plating and the HP
        if (rc.StopIndex == 3) RideWaiveCages(main, rc, "runaway"); // stop 4's cage holds the train while the runaway comes up behind it
        enc.RunawayClock = 0f; // no traffic runaway on the party line while the cart stands
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_rwStep} (cell {DungeonBuilder.WorldToCell(fell.GlobalPosition)}): nobody falls"); return; }
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
                if (_rwPass == 2)
                {
                    rc.DebugSetPlating(30f);
                    (_rwWrecksBefore, _rwSparesBefore) = (rc.Wrecks, rc.SparesUsed.Count);
                }
                GD.Print($"[runaway] pass {_rwPass} departed stop 3 at S={rc.State.S:0.0}: plating {rc.State.Plating:0}, {rc.Train.Count} wagon(s), lead pumping={!RamBranch}");
                _rwStep++; _rwClock = 0f;
                break;
            }
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
}
