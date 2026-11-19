using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-crew[=forgiven|condemned] ----------------------------------------------------------------------
// The railed jailbreak on the built-in campaign's floor 12 at lethality 3, scripted end to end: the cart rides to each
// stop, the stop's guards die by a debug kill and the gate lifts, the cage opens free (the orc's keys from Grisel's body
// when she was condemned, Grisel's own voice from the bench when she was forgiven), the wagon couples on at the departure,
// a save and a load between stops 2 and 3 keep the train, one crew member let out at stop 2 bleeds out and is lost, and
// the end of the line brings the rest home and opens the stairs. Then the floor is built again and the cart is wrecked
// between stops 1 and 2: a spare takes over. Forgiven, Grisel says each of her ride1-ride3 calls exactly once and her cage
// call at least once, all through her own line (Npc.Said), never as narration; condemned, she says nothing. Prints [crew]
// lines and [crew] RESULT.
public partial class DebugTools
{
    private bool _testCrew;
    private bool _crewForgiven;
    private int _crewStep;
    private float _crewClock;
    private bool _crewSection2;
    private RailController? _crewRails;
    private int _crewKeys;
    private bool _crewOrcKeys;
    private string? _crewLostId;
    private Enemies.Enemy? _crewBrute;
    private Vector3 _crewBruteAt;
    private const string CrewSlot = "crewtest";
    private bool _crewListening;
    /// <summary>When the cart was first seen held at the stop while the test waits for Grisel's call there; the longest it waits.</summary>
    private float? _crewHeldClock;
    private const float CrewCallWait = 30f;
    /// <summary>Grisel's calls heard through Npc.Said, by key ("?" for a line that is none of her calls).</summary>
    private readonly Dictionary<string, int> _crewGriselSaid = new();
    /// <summary>
    /// The first narration line that was Grisel's (one of her calls' words, bare or under her name; condemned, anything under her
    /// name): the old channel. Forgiven, her escort's completion is told as narration under her name, as it always was.
    /// </summary>
    private string? _crewGriselNarrated;

    /// <summary>Hear Grisel on the real path (Npc.Said) and on the narration channel, once.</summary>
    private void CrewListen()
    {
        if (_crewListening) return;
        _crewListening = true;
        Npc.Said += (npc, line) =>
        {
            if (_done || !_testCrew || npc.Id != CrewRescueController.GriselId) return;
            var calls = Main.Instance?.Builder.Contents?.Plan?.Npcs.FirstOrDefault(n => n.Id == CrewRescueController.GriselId)?.Calls ?? new List<NpcCallPlan>();
            var key = calls.FirstOrDefault(c => c.Line.Text == line.Text)?.Key ?? "?";
            _crewGriselSaid[key] = _crewGriselSaid.GetValueOrDefault(key) + 1;
            GD.Print($"[crew] grisel said {key}{(key == "?" ? $" \"{line.Text}\"" : "")}");
        };
        EventBus.Instance.NarrationEx += (text, _, _, _) =>
        {
            if (_done || !_testCrew || _crewGriselNarrated != null || text == null) return;
            var calls = Main.Instance?.Builder.Contents?.Plan?.Npcs.FirstOrDefault(n => n.Id == CrewRescueController.GriselId)?.Calls ?? new List<NpcCallPlan>();
            if ((!_crewForgiven && text.StartsWith("Grisel Thorn:")) || calls.Any(c => text == c.Line.Text || text.EndsWith(": " + c.Line.Text))) _crewGriselNarrated = text;
        };
    }

    /// <summary>What Grisel said over the run, judged at its end: null when it is right for the branch, else why not.</summary>
    private string? CrewGriselVerdict()
    {
        var heard = string.Join(", ", _crewGriselSaid.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"));
        if (_crewGriselNarrated != null) return $"Grisel was heard as narration: \"{_crewGriselNarrated}\"";
        if (!_crewForgiven) return _crewGriselSaid.Count == 0 ? null : $"condemned, but Grisel said [{heard}]";
        foreach (var key in new[] { "ride1", "ride2", "ride3" })
            if (_crewGriselSaid.GetValueOrDefault(key) != 1) return $"Grisel said {key} {_crewGriselSaid.GetValueOrDefault(key)} times, not once (heard [{heard}])";
        if (_crewGriselSaid.GetValueOrDefault("cage") < 1) return $"Grisel never said her cage call (heard [{heard}])";
        return null;
    }

    private string CrewBranch => _crewForgiven ? "forgiven" : "condemned";

    /// <summary>
    /// A scripted ride (--test-ride, --test-ride-edges, --test-crew, --test-chase) that has not printed its RESULT by 10 s before its
    /// --quit-after fails here with exit code 1, instead of being quit quietly with exit 0 by the generic timeout (a floor
    /// that never came up, a step that waits for ever). Step numbers of the crew test's wreck section are 100 + its step.
    /// </summary>
    private bool ScriptStalled(string tag, int step)
    {
        if (_done || _quitAfter <= 0 || _elapsed < _quitAfter - 10.0) return false;
        GD.Print($"[{tag}] RESULT FAIL: timed out at step {step} after {_elapsed:0} s (floor {Game.Instance?.CurrentFloor}, in game {Main.Instance?.InGame})");
        LockRules.ForcedChance = null;
        _done = true;
        GetTree().Quit(1);
        return true;
    }

    private void RunCrewTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _crewClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[crew] RESULT FAIL ({CrewBranch}): {why}"); LockRules.ForcedChance = null; _done = true; GetTree().Quit(1); }
        var party = main.Party;
        var rc = RailController.Instance;
        var ctrl = ObjectiveController.Instance as CrewRescueController;
        if (rc == null || ctrl == null) { if (_crewClock > 3f) Fail($"floor 12 has {(rc == null ? "no RailController" : "")}{(rc == null && ctrl == null ? " and " : "")}{(ctrl == null ? $"no CrewRescueController ({ObjectiveController.Instance?.GetType().Name ?? "no objective"})" : "")}"); return; }
        if (_crewSection2 && rc == _crewRails) return; // the floor is being built again: wait for the new one
        CrewListen();

        // the test owns the floor's fights: everything is culled as it appears except the guards that hold the stops
        // (section 1: they die by the test's hand at their stop, never by a companion's shot from the cart) and the brute of section 2
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
        {
            if (!e.IsAlive || e == _crewBrute) continue;
            if (_crewSection2 || !HoldsAStop(main, layout, e)) e.Kill();
            else if (_crewForgiven) e.SetUntargetable(true); // forgiven, the test waits at a stop for Grisel's call (below)
        }
        if (rc.State.Mode == CartMode.Rolling) Pin(_crewBrute, _crewBruteAt);
        // ride 3's heap blocks the party lane: the test throws the switch_out lever as the ride arms, so the cart goes round on the enemy lane
        if (rc.State.Mode == CartMode.Rolling && root.GetNodeOrNull<RailEncounterController>("RailEncounterController") is { ArmedRide: 3 } enc && enc.DebugThrowSwitches(3) > 0)
            GD.Print($"[crew] ride 3 armed at S={rc.State.S:0.0}: the switch_out lever is thrown (the ore heap blocks the party lane)");

        if (_crewSection2) { RunCrewWreck(main, layout, rc, Fail); return; }
        var wagons = ctrl.Wagons;
        var lead = party.Local;

        switch (_crewStep)
        {
            case 0 when _crewClock >= 1.5f:
            {
                // 1. guard
                var crew = ctrl.Crew;
                var caged = crew.Count(c => ctrl.StateOf(c) == CrewState.Caged && c.Seated);
                var gatesDown = Enumerable.Range(1, Math.Max(0, rc.StopS.Length - 2)).Count(i => !rc.GateUp(i));
                var grisel = GriselNpc(root);
                GD.Print($"[crew] 1 floor 12 ({CrewBranch}): stops={rc.StopS.Length} wagons={wagons.Count} [{string.Join(", ", wagons.Select(w => $"stop {w.Stop} cell {w.Cell}"))}] crew={crew.Count} caged={caged} gates down={gatesDown} train={rc.Train.Count} lethality={TierRules.Lethality} grisel={(grisel == null ? "none" : $"{grisel.NpcName} dead={grisel.Dead}")} body={(ctrl.Body == null ? "none" : "on the floor")} orcKeys={ctrl.OrcKeys}");
                if (rc.StopS.Length != 5) { Fail($"{rc.StopS.Length} stops, not 5"); return; }
                if (wagons.Count != 3 || crew.Count != 6 || caged != 6 || gatesDown != 3) { Fail($"wagons={wagons.Count} crew={crew.Count} caged={caged} gates down={gatesDown} (want 3, 6, 6, 3)"); return; }
                if (wagons.Select(w => w.Stop).OrderBy(s => s).SequenceEqual(new[] { 1, 2, 3 }) == false) { Fail("the wagons do not stand at stops 1, 2 and 3"); return; }
                if (_crewForgiven)
                {
                    if (grisel == null || grisel.Dead) { Fail("forgiven, but Grisel is not on floor 12 alive"); return; }
                    if (ctrl.Body != null) { Fail("forgiven, but Grisel's body lies by the cages"); return; }
                    if (AdventureDirector.Instance?.QuestGivenBy(CrewRescueController.GriselId) is not { } q) { Fail("forgiven, but Grisel offers no quest"); return; }
                    AdventureDirector.Instance.Apply(new QuestAccepted(q.Id)); // her escort, as her dialogue's accept does
                    GD.Print($"[crew] 1 Grisel's quest {q.Id} \"{q.Title}\" {q.Status}; escorting={grisel.Escorting} at cell {DungeonBuilder.WorldToCell(grisel.GlobalPosition)} ({grisel.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart)");
                    if (q.Status != QuestStatus.Active || !grisel.Escorting) { Fail($"Grisel's escort did not start (quest {q.Status}, escorting {grisel.Escorting})"); return; }
                }
                else
                {
                    if (grisel is { Dead: false }) { Fail("condemned, but a living Grisel stands on floor 12"); return; }
                    if (ctrl.Body is not { Dead: true } || ctrl.OrcKeys) { Fail($"condemned, but no dead Grisel with the orc's keys (body {(ctrl.Body == null ? "none" : $"dead={ctrl.Body.Dead}")}, keys taken {ctrl.OrcKeys})"); return; }
                }
                // a spare key in the bag and every pick fails: a cage that opens without spending the key opened free
                if (party.Bag.Count("item.iron_key") == 0) party.Bag.TryAdd(Game.Instance.Catalog.Create("item.iron_key"));
                _crewKeys = party.Bag.Count("item.iron_key");
                LockRules.ForcedChance = 0f;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 1 when _crewClock >= 0.5f:
            {
                // 2. board at the landing
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 2:
            {
                if (_crewForgiven && GriselNpc(root) is { } g && g.Cart != rc.Cart && _crewClock < 1f) break; // she takes the bench within a hop-in tick
                if (rc.State.Mode != CartMode.Rolling) { if (_crewClock > 8f) Fail($"the cart did not leave the landing (mode {rc.State.Mode}, seated {party.Active.Count(a => a.Seated)})"); break; }
                var bench = BenchNames(root, rc);
                GD.Print($"[crew] 2 departed the landing after {_crewClock:0.0} s: {party.Active.Count(a => a.Seated)} heroes seated, bench [{bench}]");
                if (_crewForgiven && GriselNpc(root)?.Cart != rc.Cart) { Fail("forgiven, but Grisel did not take the bench at the landing"); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 3: if (CrewArriveAndKill(main, layout, rc, 1, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 4:
            {
                if (!rc.GateUp(1)) { if (_crewClock > 2f) Fail("stop 1's gate did not lift with its guards dead"); break; }
                GD.Print($"[crew] 2 stop 1's gate lifted {_crewClock:0.00} s after the kill; boarding={rc.Boarding.Running} left={rc.Boarding.Left:0.0}");
                // 3. cage 1, free
                var wagon = wagons.First(w => w.Stop == 1);
                if (!_crewForgiven)
                {
                    if (root.GetNodeOrNull<OrcKeyring>("OrcKeyring") is not { } ring) { Fail("no keyring at Grisel's belt"); return; }
                    var ringPrompt = ring.Prompt;
                    ring.Interact(party, lead!);
                    GD.Print($"[crew] 3 the orc's keys: prompt \"{ringPrompt}\" -> taken={ctrl.OrcKeys}, the keyring now \"{ring.Prompt}\"");
                    if (!ctrl.OrcKeys) { Fail("taking the keyring did not give the party the orc's keys"); return; }
                }
                if (!CrewOpen(main, ctrl, wagon, _crewForgiven ? "Open the cage (Grisel)" : "Open the cage (the orc's keys)", Fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 5: if (CrewDepart(rc, ctrl, 1, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 6: if (CrewArriveAndKill(main, layout, rc, 2, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 7:
            {
                if (!rc.GateUp(2)) { if (_crewClock > 2f) Fail("stop 2's gate did not lift with its guards dead"); break; }
                if (!CrewOpen(main, ctrl, wagons.First(w => w.Stop == 2), _crewForgiven ? "Open the cage (Grisel)" : "Open the cage (the orc's keys)", Fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 8: if (CrewDepart(rc, ctrl, 2, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 9:
            {
                // 5. save and load between stops 2 and 3
                if (rc.State.Mode == CartMode.Rolling && rc.State.S >= rc.StopS[2] + 0.4f * (rc.StopS[3] - rc.StopS[2]))
                {
                    _crewOrcKeys = ctrl.OrcKeys;
                    GD.Print($"[crew] 5 saved mid-ride at S={rc.State.S:0.0} (stop 2 {rc.StopS[2]:0.0}, stop 3 {rc.StopS[3]:0.0}): train={rc.Train.Count} orcKeys={ctrl.OrcKeys} {CrewStates(ctrl)}");
                    main.SaveGame(CrewSlot);
                    if (!main.LoadGame(CrewSlot)) { Fail("the save would not load"); return; }
                    _crewStep++;
                    _crewClock = 0f;
                }
                else if (_crewClock > 40f) Fail($"the cart never got halfway to stop 3 (mode {rc.State.Mode}, S={rc.State.S:0.0})");
                break;
            }
            case 10 when _crewClock >= 1f:
            {
                var fails = new List<string>();
                var train = string.Join(",", rc.Train.Select(w => w.Stop));
                var inWagons = ctrl.Crew.Where(c => !c.Dead && c.Ride is CageWagon { Stop: 1 or 2, Coupled: true }).Select(c => c.NpcName).ToList();
                var gatesUp = $"{rc.GateUp(1)},{rc.GateUp(2)},{rc.GateUp(3)}";
                GD.Print($"[crew] 5 after the load: mode={rc.State.Mode} stop={rc.StopIndex} train=[{train}] riding the train [{string.Join(", ", inWagons)}] orcKeys={ctrl.OrcKeys} (saved {_crewOrcKeys}) gates up {gatesUp} cages open [{string.Join(",", wagons.Where(w => w.Open).Select(w => w.Stop))}] {CrewStates(ctrl)}; Grisel {GriselWhere(root, layout, rc)}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 2) fails.Add($"the cart came back {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 2");
                if (train != "1,2") fails.Add($"the train is [{train}], not wagons 1 and 2");
                if (rc.Train.Any(w => !w.Coupled || !w.Open)) fails.Add("a train wagon is not coupled and open");
                if (inWagons.Count != 4) fails.Add($"{inWagons.Count} crew ride the train, not 4");
                if (ctrl.OrcKeys != _crewOrcKeys) fails.Add($"OrcKeys came back {ctrl.OrcKeys}, saved {_crewOrcKeys}");
                if (!rc.GateUp(1) || !rc.GateUp(2) || rc.GateUp(3)) fails.Add($"gates up {gatesUp}: stops 1 and 2 should be up, stop 3 down");
                if (wagons.First(w => w.Stop == 3).Open || ctrl.Crew.Count(c => ctrl.StateOf(c) == CrewState.Caged) != 2) fails.Add("cage 3 is not still locked on its two crew");
                if (_crewForgiven && GriselNpc(root) is { } gl && layout.IsRail(DungeonBuilder.WorldToCell(gl.GlobalPosition))) fails.Add($"Grisel stands on a rail cell {DungeonBuilder.WorldToCell(gl.GlobalPosition)} after the load");
                if (fails.Count > 0) { Fail(string.Join("; ", fails)); return; }
                // 6. a crew member is let out at stop 2 and goes down: nobody revives them
                var lost = ctrl.Crew.FirstOrDefault(c => c.Ride is CageWagon { Stop: 2 } && !c.Dead);
                if (lost == null) { Fail("nobody in wagon 2 to let out"); return; }
                var cell = CrewFreeCell(main, layout, lost.GlobalPosition);
                lost.GetOut(main.Builder.Surface(cell) + Vector3.Up * 0.1f);
                if (!lost.Escorting) lost.StartFollowing();
                lost.GoDown();
                _crewLostId = lost.Id;
                GD.Print($"[crew] 6 {lost.NpcName} let out of wagon 2 onto {cell} and down: downed={lost.Downed} seated={lost.Seated} clock={lost.DownedSeconds:0.0}");
                if (!lost.Downed || lost.Seated) { Fail($"{lost.NpcName} is not down on foot"); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 11 when _crewClock >= 0.5f:
            {
                var lost = ctrl.Crew.First(c => c.Id == _crewLostId);
                if (ctrl.StateOf(lost) != CrewState.Downed) { Fail($"{lost.NpcName} down on foot counts as {ctrl.StateOf(lost)}, not Downed"); return; }
                var before = lost.DownedSeconds;
                lost.DebugBleed(CrewRules.BleedOutSeconds - 0.5f); // the last half second of the minute runs for real
                GD.Print($"[crew] 6 {lost.NpcName} has bled {before:0.00} s; the clock is run on to {lost.DownedSeconds:0.0} s of {CrewRules.BleedOutSeconds:0} s");
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 12:
            {
                var lost = ctrl.Crew.First(c => c.Id == _crewLostId);
                if (!lost.Dead || ctrl.StateOf(lost) != CrewState.Lost) { if (_crewClock > 3f) Fail($"{lost.NpcName} did not bleed out (dead={lost.Dead}, state {ctrl.StateOf(lost)}, clock {lost.DownedSeconds:0.0})"); break; }
                GD.Print($"[crew] 6 {lost.NpcName} bled out {_crewClock:0.00} s later: dead={lost.Dead} state={ctrl.StateOf(lost)} completed={ctrl.Completed} hud=\"{ctrl.HudText}\"");
                if (ctrl.Completed) { Fail("the floor completed with a cage still locked"); return; }
                _crewStep++;
                _crewClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                break;
            }
            case 13:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_crewClock > 8f) Fail($"re-boarded at stop 2, but the cart did not depart (mode {rc.State.Mode})"); break; }
                GD.Print($"[crew] 6 re-boarded at stop 2 and departed: train={rc.Train.Count} bench [{BenchNames(root, rc)}]");
                if (rc.Train.Count != 2) { Fail($"the train left stop 2 with {rc.Train.Count} wagons"); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 14: if (CrewArriveAndKill(main, layout, rc, 3, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 15:
            {
                if (!rc.GateUp(3)) { if (_crewClock > 2f) Fail("stop 3's gate did not lift with its guards dead"); break; }
                if (!CrewOpen(main, ctrl, wagons.First(w => w.Stop == 3), _crewForgiven ? "Open the cage (Grisel)" : "Open the cage (the orc's keys)", Fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 16: if (CrewDepart(rc, ctrl, 3, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 17:
            {
                // 7. the end of the line
                if (rc.State.Mode == CartMode.Rolling) { if (_crewClock > 60f) Fail($"the cart did not reach the end of the line within 60 s (S={rc.State.S:0.0})"); break; }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != rc.StopS.Length - 1) { Fail($"the cart stopped {rc.State.Mode} at stop {rc.StopIndex}, not the end of the line"); return; }
                if (!ctrl.Completed) { if (_crewClock > 3f) Fail($"held at the end of the line but the floor is not complete: {CrewStates(ctrl)}"); break; }
                var stairs = root.GetNodeOrNull<Stairs>("DownStairs");
                var living = ctrl.Crew.Where(c => !c.Dead).ToList();
                var home = living.Count(c => ctrl.StateOf(c) == CrewState.Home);
                var lostN = ctrl.Crew.Count(c => ctrl.StateOf(c) == CrewState.Lost);
                GD.Print($"[crew] 7 the end of the line after {_crewClock:0.0} s: completed={ctrl.Completed} home={home}/{living.Count} lost={lostN} stairs locked={stairs?.Locked} cages open={wagons.Count(w => w.Open)}/3 train={rc.Train.Count} {CrewStates(ctrl)}");
                if (home != living.Count || living.Count != 5 || lostN != 1) { Fail($"{home} of {living.Count} living crew home, {lostN} lost (want 5 of 5, 1 lost)"); return; }
                if (stairs == null || stairs.Locked) { Fail("the down stairs are still locked"); return; }
                _crewStep++;
                _crewClock = 0f;
                if (!_crewForgiven) { CrewSection2(main); break; }
                // forgiven: Grisel gets down with the party and walks with them to the stairs
                if (lead!.Cart == rc.Cart) rc.Cart.Interact(party, lead);
                var near = DungeonBuilder.WalkableNeighbor(layout, layout.DownStairs);
                party.Teleport(main.Builder.Surface(near) + Vector3.Up * 0.3f, main.Builder.Surface(layout.DownStairs));
                GD.Print($"[crew] 7 the party gets out and walks to the stairs ({near}); Grisel {GriselWhere(root, layout, rc)}");
                break;
            }
            case 18:
            {
                var q = Game.Instance.Adventure.Quests.Values.FirstOrDefault(x => x.Floor == 12 && x.GiverNpc == CrewRescueController.GriselId);
                if (q?.Status != QuestStatus.Completed) { if (_crewClock > 25f) Fail($"q_grisel_out is {q?.Status.ToString() ?? "missing"}: Grisel {GriselWhere(root, layout, rc)}"); break; }
                GD.Print($"[crew] 7 {q.Id} completed {_crewClock:0.0} s after the party reached the stairs: Grisel {GriselWhere(root, layout, rc)}");
                CrewSection2(main);
                break;
            }
        }
    }

    /// <summary>The cart is held at <paramref name="stop"/> with its gate down and no boarding; the guards of the stop die by the test's hand.</summary>
    private bool CrewArriveAndKill(Main main, FloorLayout layout, RailController rc, int stop, Action<string> fail)
    {
        if (rc.State.Mode == CartMode.Rolling) { if (_crewClock > 60f) fail($"the cart was not held at stop {stop} within 60 s (S={rc.State.S:0.0})"); return false; }
        if (rc.State.Mode != CartMode.Held || rc.StopIndex != stop) { fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex}, not held at stop {stop}"); return false; }
        // forgiven: the stop's fight takes as long as Grisel's call for this ride still waits for the channel (the test's kill is
        // otherwise at once, and the cart would leave before she spoke). A call that never comes fails at the end.
        if (_crewForgiven && _crewGriselSaid.GetValueOrDefault($"ride{stop}") == 0)
        {
            _crewHeldClock ??= _crewClock;
            if (_crewClock - _crewHeldClock < CrewCallWait) return false;
        }
        if (_crewHeldClock is { } heldAt) GD.Print($"[crew] waited {_crewClock - heldAt:0.0} s at stop {stop} for Grisel's ride{stop} call (heard {_crewGriselSaid.GetValueOrDefault($"ride{stop}")})");
        _crewHeldClock = null;
        var room = layout.Rail!.Stops[stop].Room;
        var guards = StopGuards(main, room).ToList();
        var ids = string.Join(",", guards.Select(e => e.Group?.Id).Distinct());
        GD.Print($"[crew] {(stop == 1 ? 2 : stop == 2 ? 4 : 7)} held at stop {stop} (room {room}) after {_crewClock:0.0} s: S={rc.State.S:0.0} gate up={rc.GateUp(stop)} boarding={rc.Boarding.Running} hud=\"{rc.HudText}\" guards alive={guards.Count} [{ids}] heroes seated={main.Party.Active.Count(a => a.Seated)} hp=[{string.Join(" ", main.Party.Active.Select(a => $"{a.State.Hp:0}"))}]");
        if (rc.GateUp(stop)) { fail($"stop {stop}'s gate was already up with its guards alive"); return false; }
        if (rc.Boarding.Running) { fail($"the boarding ran at stop {stop} with the gate down"); return false; }
        if (guards.Count == 0) { fail($"stop {stop} has no living guards to hold its gate"); return false; }
        foreach (var e in guards) e.Kill();
        return true;
    }

    /// <summary>The cart leaves <paramref name="stop"/>: the open wagon there couples on behind and the train has <paramref name="stop"/> wagons.</summary>
    private bool CrewDepart(RailController rc, CrewRescueController ctrl, int stop, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) { if (_crewClock > 8f) fail($"the cart did not leave stop {stop} (mode {rc.State.Mode}, boarding {rc.Boarding.Running})"); return false; }
        var wagon = ctrl.Wagons.First(w => w.Stop == stop);
        var aboard = ctrl.Crew.Where(c => c.Ride == wagon).Select(c => c.NpcName).ToList();
        GD.Print($"[crew] 4 departed stop {stop} after {_crewClock:0.0} s: wagon {stop} coupled={wagon.Coupled} CrewWagons={rc.Train.Count} train=[{string.Join(",", rc.Train.Select(w => w.Stop))}] aboard it [{string.Join(", ", aboard)}]");
        if (!wagon.Coupled || rc.Train.Count != stop || rc.Train[^1] != wagon) { fail($"wagon {stop} did not couple on at the departure (train {rc.Train.Count})"); return false; }
        if (aboard.Count == 0) { fail($"wagon {stop} left with nobody in it"); return false; }
        return true;
    }

    /// <summary>The cage opens on one use, by the way <paramref name="expect"/> names (its prompt), with no iron key spent and no pick.</summary>
    private bool CrewOpen(Main main, CrewRescueController ctrl, CageWagon wagon, string expect, Action<string> fail)
    {
        var party = main.Party;
        if (_crewForgiven)
        {
            // forgiven: Grisel opens every cage herself, from the bench of the cart held at the cage's stop or on foot within reach
            var g = main.Builder.FloorRoot is { } root ? GriselNpc(root) : null;
            var rc = RailController.Instance!;
            var onBench = g != null && g.Cart == rc.Cart && rc.State.Mode == CartMode.Held && rc.StopIndex == wagon.Stop;
            var near = g is { Escorting: true, Seated: false } && g.GlobalPosition.DistanceTo(wagon.GlobalPosition) <= CrewRescueController.GriselReach;
            if (g == null || g.Dead || g.Downed || !(onBench || near))
            { fail($"forgiven, but Grisel cannot open cage {wagon.Stop}: {(g == null ? "none" : $"dead={g.Dead} downed={g.Downed} bench={g.Cart == rc.Cart} escorting={g.Escorting} {g.GlobalPosition.DistanceTo(wagon.GlobalPosition):0.0} m from the cage")}"); return false; }
            GD.Print($"[crew] 3 Grisel at cage {wagon.Stop}: {(onBench ? "on the bench" : $"on foot {g.GlobalPosition.DistanceTo(wagon.GlobalPosition):0.0} m away")}");
        }
        var prompt = wagon.Prompt;
        if (prompt != expect) { fail($"cage {wagon.Stop} offers \"{prompt}\", not \"{expect}\""); return false; }
        wagon.Interact(party, party.Local!);
        var keys = party.Bag.Count("item.iron_key");
        var crew = ctrl.Crew.Where(c => c.Ride == wagon).ToList();
        GD.Print($"[crew] 3 cage {wagon.Stop} by \"{prompt}\": open={wagon.Open} iron keys {_crewKeys}->{keys} crew [{string.Join(", ", crew.Select(c => $"{c.NpcName}:{ctrl.StateOf(c)}"))}] hud=\"{ctrl.HudText}\"");
        if (!wagon.Open) { fail($"cage {wagon.Stop} did not open"); return false; }
        if (keys != _crewKeys) { fail($"opening cage {wagon.Stop} spent an iron key ({_crewKeys} -> {keys})"); return false; }
        if (crew.Count != CrewRules.PerWagon || crew.Any(c => ctrl.StateOf(c) != CrewState.Freed)) { fail($"cage {wagon.Stop}'s crew are not two freed riders"); return false; }
        return true;
    }

    /// <summary>A living enemy that holds a stop's gate: a guard or defender of a collective in an intermediate stop's room.</summary>
    private static bool HoldsAStop(Main main, FloorLayout layout, Enemies.Enemy e)
    {
        if (e.Group is not { Goal: GoalKind.Guard or GoalKind.Defend } g || layout.Rail is not { } rail) return false;
        for (var i = 1; i < rail.Stops.Count - 1; i++) if (rail.Stops[i].Room == g.Room) return true;
        return false;
    }

    private static IEnumerable<Enemies.Enemy> StopGuards(Main main, int room)
    {
        if (main.Builder.Collectives is not { } reg) yield break;
        foreach (var c in reg.Collectives)
            if (c.Room == room && c.Goal is GoalKind.Guard or GoalKind.Defend)
                foreach (var e in c.Alive.ToList()) yield return e;
    }

    private static Npc? GriselNpc(Node3D root) => root.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == CrewRescueController.GriselId);

    private static string BenchNames(Node3D root, RailController rc) =>
        string.Join(", ", root.GetChildren().OfType<Npc>().Where(n => n.Cart == rc.Cart).Select(n => $"{n.NpcName}:{n.Seat}"));

    private static string GriselWhere(Node3D root, FloorLayout layout, RailController rc)
    {
        if (GriselNpc(root) is not { } g) return "none";
        var cell = DungeonBuilder.WorldToCell(g.GlobalPosition);
        return $"cell {cell} rail={layout.IsRail(cell)} seated={(g.Cart == rc.Cart ? "bench" : g.Seated ? "other" : "no")} escorting={g.Escorting} dead={g.Dead} {g.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart";
    }

    private static string CrewStates(CrewRescueController ctrl) =>
        $"crew [{string.Join(", ", ctrl.Crew.Select(c => $"{c.NpcName}:{ctrl.StateOf(c)}"))}]";

    /// <summary>A walkable cell off the lanes near <paramref name="at"/> that nothing blocks.</summary>
    private static GridPos CrewFreeCell(Main main, FloorLayout layout, Vector3 at)
    {
        var here = DungeonBuilder.WorldToCell(at);
        for (var r = 1; r <= 4; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var c = new GridPos(here.X + dx, here.Y + dy);
                    if (layout.IsWalkable(c) && !layout.IsRail(c) && !main.Builder.IsCellBlocked(c) && layout.LevelOf(c) == layout.LevelOf(here)) return c;
                }
        return DungeonBuilder.WalkableNeighbor(layout, here);
    }

    // ---- 8. the wreck and the spare, on floor 12 built again -------------------------------------------------

    private void CrewSection2(Main main)
    {
        _crewSection2 = true;
        _crewRails = RailController.Instance;
        _crewStep = 0;
        _crewClock = 0f;
        Game.Instance.Floors.Remove(12); // a fresh floor, not the one just won
        GD.Print("[crew] 8 floor 12 is built again for the wreck");
        Game.Instance.JumpToFloor(12);
    }

    private void RunCrewWreck(Main main, FloorLayout layout, RailController rc, Action<string> fail)
    {
        var party = main.Party;
        switch (_crewStep)
        {
            case 0 when _crewClock >= 1.5f:
            {
                foreach (var a in party.Adventurers)
                    if (IsInstanceValid(a) && !a.Parked) { a.State.Hp = a.State.Stats[Characters.Stat.MaxHp]; a.Revive(); }
                GD.Print($"[crew] 8 rebuilt: mode={rc.State.Mode} stop={rc.StopIndex} spares left={rc.SparesLeft} used={rc.SparesUsed.Count} train={rc.Train.Count}");
                if (rc.SparesUsed.Count != 0 || rc.StopIndex != 0 || rc.SparesLeft != 3) { fail($"the rebuilt floor is not fresh (stop {rc.StopIndex}, spares used {rc.SparesUsed.Count}, left {rc.SparesLeft})"); return; }
                _crewStep++;
                _crewClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 1:
            {
                // through stop 1 (its guards culled, its gate up) and on: thin plating and a brute between stops 1 and 2
                if (rc.State.Mode == CartMode.Rolling && rc.StopIndex == 1 && rc.State.S > rc.StopS[1] + 1f)
                {
                    var ahead = Mathf.Min(20f, rc.StopS[2] - rc.State.S - CartRules.HalfLength - 3f);
                    if (ahead < 5f) { fail($"no room for a brute between S={rc.State.S:0.0} and stop 2 at {rc.StopS[2]:0.0}"); return; }
                    rc.DebugSetPlating(15f);
                    _crewBrute = RideSpawnAhead(main, layout, rc, "enemy.horde_cinder_brute", ahead, out _crewBruteAt, out var cell);
                    if (_crewBrute == null) { fail("could not spawn enemy.horde_cinder_brute on the lane ahead"); return; }
                    GD.Print($"[crew] 8 departed stop 1; plating {rc.State.Plating:0}; {_crewBrute.Name} on lane cell {cell} at S={rc.State.S + ahead:0.0} (stop 2 at {rc.StopS[2]:0.0})");
                    _crewStep++;
                    _crewClock = 0f;
                }
                else if (_crewClock > 60f) fail($"the cart never left stop 1 (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})");
                break;
            }
            case 2:
            {
                if (rc.State.Mode == CartMode.Rolling) { if (_crewClock > 15f) fail($"the cart was not wrecked within 15 s (plating {rc.State.Plating:0}, S={rc.State.S:0.0})"); break; }
                GD.Print($"[crew] 8 wrecked after {_crewClock:0.0} s: mode={rc.State.Mode} stop={rc.StopIndex} S={rc.State.S:0.0} (stop {rc.StopS[Math.Clamp(rc.StopIndex, 0, rc.StopS.Length - 1)]:0.0}) plating={rc.State.Plating:0} SparesUsed=[{string.Join(",", rc.SparesUsed)}] left={rc.SparesLeft} seated={party.Active.Count(a => a.Seated)}");
                if (rc.State.Mode != CartMode.Held) { fail($"after the wreck the cart is {rc.State.Mode}, not held at a stop by a spare"); return; }
                if (rc.SparesUsed.Count != 1 || !rc.SparesUsed.Contains(rc.StopIndex)) { fail($"spares used [{string.Join(",", rc.SparesUsed)}], the cart at stop {rc.StopIndex}"); return; }
                if (rc.StopIndex is not (1 or 2)) { fail($"the spare stands at stop {rc.StopIndex}, not stop 1 or 2"); return; }
                if (Mathf.Abs(rc.State.S - rc.StopS[rc.StopIndex]) > 0.05f || rc.State.Plating < CartRules.MaxPlating - 0.01f) { fail("the spare is not a whole cart at its stop"); return; }
                if (CrewGriselVerdict() is { } wrong) { fail(wrong); return; }
                GD.Print($"[crew] Grisel's calls heard: [{string.Join(", ", _crewGriselSaid.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"))}]");
                LockRules.ForcedChance = null;
                GD.Print($"[crew] RESULT ok ({CrewBranch})");
                _done = true;
                GetTree().Quit();
                break;
            }
        }
    }
}
