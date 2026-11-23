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
// The railed jailbreak on the built-in campaign's floor 12 at lethality 3, scripted end to end, for the train from the landing (plan
// 14). The floor check: a cage at each intermediate stop, two caged crew per cage, W1..Wn coupled and empty at stop 0 and a gate down at
// every guarded stop (every count from LineShape: four cages, eight crew, W1..W4 on the shipped line). The cart rides to each stop, the
// stop's guards die by a debug kill and the gate lifts, the cage opens free (the orc's keys from Grisel's body when she was condemned,
// Grisel's own voice from the bench when she was forgiven), and its living, up crew get out at its door and walk to the first empty
// seats of the train (TrainRules.SeatOrder): each must sit in before the departure, which comes after the last of them sits and at most
// BoardMax + 0.5 s after the countdown started. Ride 1 passes r1_beam with every seated head (Grisel's on the bench when forgiven) clear
// by 0.05 m. A save and a load between stops 2 and 3 bring every AboardCrew entry back seat for seat; one crew member taken out of W2
// goes down, a save and a load find it lying where it lay (not back in its cage), and it bleeds out (lost). At stop 3 one freed crew member is held 30 m off while the countdown runs: its booking ends, it is on
// foot and following at the departure, it is held there through ride 4 (the collapsing bridge is no place to walk) and let go at stop
// 4's arrival, where it is beside the train again and boards. Every stop from 3 on to the last crew stop is ridden to and its cage
// opened the same way, and the end of the line brings the rest home and opens the stairs. Then the floor is built again: the cart is held
// at stop 1 by its locked cage (the cage hold, plan 7: no countdown, "Cart — cage locked", the hold prompt) until the lead leaves the
// prisoners behind, who then count as lost, and W1 leaves stop 1 empty; thin plating and a brute wreck the cart between stops 1 and 2 and
// a spare takes over; a save and a load at stop 2 keep stop 1's prisoners left behind while stop 2's cage holds the cart again; the
// interact ray, aimed through a coupled wagon (its prompt empty), reaches what lies past it (a cage when one is within the ray's reach
// past a wagon, else the cart); stop 2's cage opens by the test's hook and its crew take W1's seats 4 and 5 (stop 1's were left). Forgiven,
// Grisel says each of her ride calls (ride1 to ride5 on the shipped line: one per ride) exactly once and her cage call at least once, all
// through her own line (Npc.Said), never as narration; condemned, she says nothing. Prints [crew] lines and [crew] RESULT.
public partial class DebugTools
{
    private bool _testCrew;
    private bool _crewForgiven;
    private int _crewStep;
    /// <summary>Section 1 from stop 3 on: the crew stop the cart rides to next (3 up to the last crew stop).</summary>
    private int _crewStop = 3;
    private float _crewClock;
    private bool _crewSection2;
    private RailController? _crewRails;
    private int _crewKeys;
    private bool _crewOrcKeys;
    private string? _crewLostId;
    private Enemies.Enemy? _crewBrute;
    private Vector3 _crewBruteAt;
    private const string CrewSlot = "crewtest";
    private double _crewLoadAt = -1;
    private bool _crewListening;
    /// <summary>Grisel's calls heard through Npc.Said, by key ("?" for a line that is none of her calls).</summary>
    private readonly Dictionary<string, int> _crewGriselSaid = new();
    /// <summary>
    /// The first narration line that was Grisel's (one of her calls' words, bare or under her name; condemned, anything under her
    /// name): the old channel. Forgiven, her escort's completion is told as narration under her name, as it always was.
    /// </summary>
    private string? _crewGriselNarrated;
    /// <summary>Section 2 (the cage hold): "The cage here is still locked." heard since the rebuild.</summary>
    private int _crewLockedHeard;
    // ---- plan 14: the crew walk to the train
    /// <summary>The flow's own clock (s since it started), for the walk's and the countdown's times.</summary>
    private float _crewTime;
    /// <summary>Each opened cage's crew and the seats they must take (the first empty ones at the opening), by stop.</summary>
    private readonly Dictionary<int, List<(Npc Npc, int Wagon, int Seat)>> _crewWant = new();
    /// <summary>When each cage opened, by stop (<see cref="_crewTime"/>).</summary>
    private readonly Dictionary<int, float> _crewOpenedAt = new();
    /// <summary>When each crew member was first seen in its wanted seat (<see cref="_crewTime"/>).</summary>
    private readonly Dictionary<Npc, float> _crewSatAt = new();
    /// <summary>When the countdown at the stop held now was first seen running, or −1.</summary>
    private float _crewCountdownAt = -1f;
    /// <summary>The crew seated in the train at the save, by id: (wagon, seat).</summary>
    private readonly Dictionary<string, (int Wagon, int Seat)> _crewSavedSeats = new();
    /// <summary>Stop 3's straggler: held 30 m off at <see cref="_crewStragglerAt"/> until stop 4's arrival.</summary>
    private Npc? _crewStraggler;
    private Vector3 _crewStragglerAt;
    private bool _crewStragglerHeld, _crewStragglerBooked, _crewStragglerEnded, _crewStragglerJumped;
    private float _crewStragglerEndedAt = -1f, _crewStragglerReleasedAt = -1f;
    private Vector3 _crewStragglerLast;
    /// <summary>Ride 1's beam over the train's seated heads (Grisel's on the bench among them when forgiven).</summary>
    private readonly BeamHeads _crewBeam = new();
    /// <summary>Section 2: stop 2's crew (ids), freed by the test's hook after the load there.</summary>
    private readonly HashSet<string> _crewWreckCage2 = new();
    /// <summary>Section 1, step 6: where the downed crew member lay at its save (null before it), and whether the load was judged.</summary>
    private Vector3? _crewDownSavedAt;
    private bool _crewDownLoaded;

    /// <summary>Hear Grisel on the real path (Npc.Said) and on the narration channel, once.</summary>
    private void CrewListen()
    {
        if (_crewListening) return;
        _crewListening = true;
        Action<Npc, Line> said = (npc, line) =>
        {
            if (_done || !_testCrew || npc.Id != CrewRescueController.GriselId) return;
            var calls = Main.Instance?.Builder.Contents?.Plan?.Npcs.FirstOrDefault(n => n.Id == CrewRescueController.GriselId)?.Calls ?? new List<NpcCallPlan>();
            var key = calls.FirstOrDefault(c => c.Line.Text == line.Text)?.Key ?? "?";
            _crewGriselSaid[key] = _crewGriselSaid.GetValueOrDefault(key) + 1;
            GD.Print($"[crew] grisel said {key}{(key == "?" ? $" \"{line.Text}\"" : "")}");
        };
        Npc.Said += said;
        _staticUnhooks.Add(() => Npc.Said -= said);
        EventBus.Instance.Message += text => { if (_crewSection2 && text == "The cage here is still locked.") _crewLockedHeard++; };
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
        var rides = RailController.Instance is { } rc ? Shape(rc).Rides : 0;
        for (var n = 1; n <= rides; n++)
            if (_crewGriselSaid.GetValueOrDefault($"ride{n}") != 1) return $"Grisel said ride{n} {_crewGriselSaid.GetValueOrDefault($"ride{n}")} times, not once (heard [{heard}])";
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
        var frame = (float)GetProcessDeltaTime();
        _crewClock += frame;
        _crewTime += frame;
        HoldTheRunaway("crew"); // Ruling 22: not this flow's business
        void Fail(string why) { GD.Print($"[crew] RESULT FAIL ({CrewBranch}): {why}"); LockRules.ForcedChance = null; _done = true; GetTree().Quit(1); }
        var party = main.Party;
        var rc = RailController.Instance;
        var ctrl = ObjectiveController.Instance as CrewRescueController;
        if (rc == null || ctrl == null) { if (_crewClock > 3f) Fail($"floor 12 has {(rc == null ? "no RailController" : "")}{(rc == null && ctrl == null ? " and " : "")}{(ctrl == null ? $"no CrewRescueController ({ObjectiveController.Instance?.GetType().Name ?? "no objective"})" : "")}"); return; }
        if (_crewSection2 && rc == _crewRails) return; // the floor is being built again: wait for the new one
        CrewListen();
        SoundListen();

        // the test owns the floor's fights: everything is culled as it appears except the guards that hold the stops
        // (section 1: they die by the test's hand at their stop) and the brute of section 2
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsAlive && e != _crewBrute && (_crewSection2 || !HoldsAStop(main, layout, e))) e.Kill();
        if (rc.State.Mode == CartMode.Rolling) Pin(_crewBrute, _crewBruteAt);
        // ride 3's heap blocks the party lane: the seated lead throws the switch_out lever from the seat as the seated window offers it
        // (no aim, beyond the look ray's reach, through the dispatch E uses), so the cart goes round on the enemy lane, and the live
        // switch back as its window offers it, so the cart comes home before the broken bridge
        if (root.GetNodeOrNull<RailEncounterController>("RailEncounterController") is { ArmedRide: 3 } enc && enc.SetPieceNode("r3_switch_out") is TrackSwitch r3)
        {
            if (r3 is { Taken: true, Thrown: false }) { Fail("ride 3's switch_out lever was never offered to the seated lead before its points"); return; }
            if (rc.State.Mode == CartMode.Rolling && r3 is { Thrown: false, Taken: false } && party.Local is { } me && me.Cart == rc.Cart && party.LookingAt == r3)
            {
                var dist = me.GlobalPosition.DistanceTo(r3.GlobalPosition);
                var ahead = r3.LeverS - rc.Party.FrontS;
                var prompt = r3.Prompt;
                party.VrUse(true);
                party.VrUse(false);
                GD.Print($"[crew] ride 3: the seated lead throws the switch_out lever from the seat (the seated window, prompt \"{prompt}\", {ahead:0.0} m before the lever, {dist:0.0} m off): thrown={r3.Thrown}, still seated={me.Cart == rc.Cart}");
                if (prompt != "Throw the switch") { Fail($"the seated window offered ride 3's lever as \"{prompt}\", not \"Throw the switch\""); return; }
                if (dist <= PropPromptRules.InteractReach) { Fail($"the seated window first offered ride 3's lever {dist:0.00} m off, inside the look ray's reach"); return; }
                if (!r3.Thrown) { Fail("the seated press did not throw ride 3's switch_out lever"); return; }
                if (me.Cart != rc.Cart) { Fail("the seated press on ride 3's lever got the lead out of the cart"); return; }
            }
            // across on the slag line with its return open, the live switch back is the seated lead's too: thrown from the seat in its window
            // (a companion never moves it), or the train runs on to the broken bridge
            if (enc.SetPieceNode("r3_switch_back") is TrackSwitch { Deadly: true } back)
            {
                var across = rc.Crossing && float.IsNaN(rc.CrossBackS);
                if (back is { Taken: true, Thrown: false } && across) { Fail("ride 3's live switch_back was never offered to the seated lead before its points: the cart stays on the slag line"); return; }
                if (!back.Thrown && back.LastToggledBy != null) { Fail($"ride 3's live switch_back was set back by {back.LastToggledBy}"); return; }
                if (across && rc.State.Mode == CartMode.Rolling && back is { Thrown: false, Taken: false } && party.Local is { } rider && rider.Cart == rc.Cart && party.LookingAt == back)
                {
                    var ahead = back.LeverS - rc.Party.FrontS;
                    var prompt = back.Prompt;
                    party.VrUse(true);
                    party.VrUse(false);
                    GD.Print($"[crew] ride 3: the seated lead throws the switch_back lever from the seat (the seated window, prompt \"{prompt}\", {ahead:0.0} m before the lever, the cart across with its return open): thrown={back.Thrown} by {back.LastToggledBy}, still seated={rider.Cart == rc.Cart}");
                    if (prompt != "Throw the switch" || !back.Thrown || back.LastToggledBy != rider.State.Name || rider.Cart != rc.Cart) { Fail("the seated press did not throw ride 3's live switch_back, or got the lead out"); return; }
                }
            }
        }

        ThrowPitOnTheWay(main, rc, "crew"); // ride 5's lava pit: the seated lead throws its switch from the seat on the way (plan 15 Ruling 21)
        if (_crewSection2) { RunCrewWreck(main, layout, rc, Fail); return; }
        var cages = ctrl.Cages;
        var lead = party.Local;
        CrewTrackSeats(rc);
        if (CrewHoldStraggler(rc, ctrl, Fail)) return;

        switch (_crewStep)
        {
            case 0 when _crewClock >= 1.5f:
            {
                if (StopsCheck("crew", root, rc) is { } near) { Fail(near); return; }
                // 1. the floor: a cage at each intermediate stop, two caged crew per cage, the train coupled and empty from the landing
                var crew = ctrl.Crew;
                var caged = crew.Count(c => ctrl.StateOf(c) == CrewState.Caged && c.Cart is PlatformCage);
                var shape = Shape(rc);
                var gatesDown = Enumerable.Range(1, shape.Intermediate).Where(i => !rc.GateUp(i)).ToList();
                var guarded = CrewGuardedStops(main, layout);
                var train = string.Join(",", rc.Train.Select(w => w.Number));
                var riders = rc.Wagons.Sum(w => w.Riders.Count);
                var perCage = cages.Select(c => crew.Count(n => n.Cart == c)).ToList();
                var grisel = GriselNpc(root);
                GD.Print($"[crew] 1 floor 12 ({CrewBranch}): a cage at each intermediate stop ({cages.Count}), two caged crew per cage ({caged}), W1..W{rc.Wagons.Count} coupled and empty at stop {rc.StopIndex} (train [{train}], {riders} rider(s)), and a gate down at [{string.Join(",", gatesDown)}] (the guarded stops [{string.Join(",", guarded.OrderBy(x => x))}]); cages [{string.Join(", ", cages.Select(c => $"stop {c.Stop} cell {c.Cell}"))}] lethality={TierRules.Lethality} grisel={(grisel == null ? "none" : $"{grisel.NpcName} dead={grisel.Dead}")} body={(ctrl.Body == null ? "none" : "on the floor")} orcKeys={ctrl.OrcKeys}");
                if (rc.StopS.Length != Floor12Stops) { Fail($"{rc.StopS.Length} stops, not {Floor12Stops}"); return; }
                if (cages.Count != shape.Intermediate || !cages.Select(c => c.Stop).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, shape.Intermediate))) { Fail($"{cages.Count} cages at stops [{string.Join(",", cages.Select(c => c.Stop))}], not one at each of stops 1 to {shape.Intermediate}"); return; }
                if (crew.Count != 2 * shape.Intermediate || caged != 2 * shape.Intermediate || perCage.Any(n => n != 2)) { Fail($"crew={crew.Count} caged={caged} per cage [{string.Join(",", perCage)}] (want {2 * shape.Intermediate}, {2 * shape.Intermediate}, two in each)"); return; }
                if (rc.StopIndex != 0 || train != string.Join(",", Enumerable.Range(1, shape.Intermediate)) || rc.Wagons.Count != shape.Intermediate || riders != 0) { Fail($"W1..W{shape.Intermediate} are not coupled and empty at stop 0 (stop {rc.StopIndex}, wagons {rc.Wagons.Count}, train [{train}], {riders} rider(s))"); return; }
                if (!gatesDown.ToHashSet().SetEquals(guarded)) { Fail($"gates down at [{string.Join(",", gatesDown)}], not the guarded stops [{string.Join(",", guarded.OrderBy(x => x))}]"); return; }
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
                GD.Print($"[crew] 2 departed the landing after {_crewClock:0.0} s: {party.Active.Count(a => a.Seated)} heroes seated, bench [{bench}]{(GriselNpc(root) is { } gs ? $", Grisel sitting={gs.Sitting} (head {gs.HeadHeight:0.00} m above her seat)" : "")}");
                if (_crewForgiven && GriselNpc(root)?.Cart != rc.Cart) { Fail("forgiven, but Grisel did not take the bench at the landing"); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 3:
                if (CrewBeam(rc, Fail)) return;
                if (CrewArriveAndKill(main, layout, rc, 1, Fail)) { _crewStep++; _crewClock = 0f; }
                break;
            case 4:
            {
                if (!rc.GateUp(1)) { if (_crewClock > 2f) Fail("stop 1's gate did not lift with its guards dead"); break; }
                if (!_crewBeam.Done) { Fail("ride 1's beam never measured the train's seated heads"); return; }
                GD.Print($"[crew] 2 stop 1's gate lifted {_crewClock:0.00} s after the kill; boarding={rc.Boarding.Running} left={rc.Boarding.Left:0.0}");
                // 3. cage 1, free
                if (!_crewForgiven)
                {
                    if (root.GetNodeOrNull<OrcKeyring>("OrcKeyring") is not { } ring) { Fail("no keyring at Grisel's belt"); return; }
                    var ringPrompt = ring.Prompt;
                    ring.Interact(party, lead!);
                    GD.Print($"[crew] 3 the orc's keys: prompt \"{ringPrompt}\" -> taken={ctrl.OrcKeys}, the keyring now \"{ring.Prompt}\"");
                    if (!ctrl.OrcKeys) { Fail("taking the keyring did not give the party the orc's keys"); return; }
                }
                if (!CrewOpen(main, ctrl, cages.First(c => c.Stop == 1), _crewForgiven ? "Open the cage (Grisel)" : "Open the cage (the orc's keys)", Fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 5: if (CrewDepart(rc, ctrl, 1, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 6: if (CrewArriveAndKill(main, layout, rc, 2, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 7:
            {
                if (!rc.GateUp(2)) { if (_crewClock > 2f) Fail("stop 2's gate did not lift with its guards dead"); break; }
                if (!CrewOpen(main, ctrl, cages.First(c => c.Stop == 2), _crewForgiven ? "Open the cage (Grisel)" : "Open the cage (the orc's keys)", Fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 8: if (CrewDepart(rc, ctrl, 2, Fail)) { _crewStep++; _crewClock = 0f; } break;
            case 9:
            {
                // 5. save and load between stops 2 and 3: the train's seats are saved (AboardCrew)
                if (rc.State.Mode == CartMode.Rolling && rc.State.S >= rc.StopS[2] + 0.4f * (rc.StopS[3] - rc.StopS[2]))
                {
                    _crewOrcKeys = ctrl.OrcKeys;
                    _crewSavedSeats.Clear();
                    foreach (var c in ctrl.Crew)
                        if (!c.Dead && c.Cart is TrainWagon { Coupled: true } w) _crewSavedSeats[c.Id] = (w.Number, c.Seat);
                    GD.Print($"[crew] 5 saved mid-ride at S={rc.State.S:0.0} (stop 2 {rc.StopS[2]:0.0}, stop 3 {rc.StopS[3]:0.0}): train={rc.Train.Count} orcKeys={ctrl.OrcKeys} seated in the train [{CrewSeats(ctrl)}] {CrewStates(ctrl)}");
                    main.SaveGame(CrewSlot);
                    if (!main.LoadGame(CrewSlot)) { Fail("the save would not load"); return; }
                    _crewLoadAt = RideNow;
                    _crewStep++;
                    _crewClock = 0f;
                }
                else if (_crewClock > 40f) Fail($"the cart never got halfway to stop 3 (mode {rc.State.Mode}, S={rc.State.S:0.0})");
                break;
            }
            case 10 when _crewClock >= 1f:
            {
                var fails = new List<string>();
                var train = string.Join(",", rc.Train.Select(w => w.Number));
                var shape = Shape(rc);
                var guarded = CrewGuardedStops(main, layout);
                var gatesUp = string.Join(",", Enumerable.Range(1, shape.Intermediate).Select(k => rc.GateUp(k)));
                GD.Print($"[crew] 5 after the load: mode={rc.State.Mode} stop={rc.StopIndex} train=[{train}] seated in the train [{CrewSeats(ctrl)}] orcKeys={ctrl.OrcKeys} (saved {_crewOrcKeys}) gates up {gatesUp} cages open [{string.Join(",", cages.Where(c => c.Open).Select(c => c.Stop))}] {CrewStates(ctrl)}; Grisel {GriselWhere(root, layout, rc)}");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 2) fails.Add($"the cart came back {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 2");
                if (train != string.Join(",", Enumerable.Range(1, shape.Intermediate))) fails.Add($"the train is [{train}], not W1..W{shape.Intermediate}");
                var back = new List<string>();
                foreach (var (id, seat) in _crewSavedSeats.OrderBy(k => k.Value.Wagon).ThenBy(k => k.Value.Seat))
                {
                    var c = ctrl.Crew.First(n => n.Id == id);
                    var now = c.Cart is TrainWagon { Coupled: true } w ? (w.Number, c.Seat) : (0, -1);
                    back.Add($"{c.NpcName} W{seat.Wagon} seat {seat.Seat}{(now == seat ? "" : now.Item1 == 0 ? " -> on foot" : $" -> W{now.Item1} seat {now.Item2}")}");
                    if (now != seat) fails.Add($"{c.NpcName} saved in W{seat.Wagon} seat {seat.Seat} came back {(now.Item1 == 0 ? "out of the train" : $"in W{now.Item1} seat {now.Item2}")}");
                }
                if (_crewSavedSeats.Count != 4) fails.Add($"{_crewSavedSeats.Count} crew were seated in the train at the save, not stops 1 and 2's four");
                if (ctrl.OrcKeys != _crewOrcKeys) fails.Add($"OrcKeys came back {ctrl.OrcKeys}, saved {_crewOrcKeys}");
                if (!rc.GateUp(1) || !rc.GateUp(2) || Enumerable.Range(3, shape.Intermediate - 2).Any(k => rc.GateUp(k) == guarded.Contains(k))) fails.Add($"gates up {gatesUp}: stops 1 and 2 should be up, every later guarded stop down");
                if (cages.Any(c => c.Stop >= 3 && c.Open) || ctrl.Crew.Count(c => ctrl.StateOf(c) == CrewState.Caged) != 2 * (shape.Intermediate - 2)) fails.Add("the cages from stop 3 on are not still locked on their crew");
                if (_crewForgiven && GriselNpc(root) is { } gl && layout.IsRail(DungeonBuilder.WorldToCell(gl.GlobalPosition))) fails.Add($"Grisel stands on a rail cell {DungeonBuilder.WorldToCell(gl.GlobalPosition)} after the load");
                if (fails.Count > 0) { Fail(string.Join("; ", fails)); return; }
                GD.Print($"[crew] load: AboardCrew [{string.Join(", ", back)}] back seat for seat");
                // 6. a crew member is taken out of W2 and goes down: nobody revives them
                var lost = ctrl.Crew.FirstOrDefault(c => c.Cart is TrainWagon { Number: 2 } && !c.Dead);
                if (lost == null) { Fail("nobody in W2 to take out"); return; }
                var cell = CrewFreeCell(main, layout, lost.GlobalPosition);
                lost.GetOut(main.Builder.Surface(cell) + Vector3.Up * 0.1f);
                if (!lost.Escorting) lost.StartFollowing();
                lost.GoDown();
                _crewLostId = lost.Id;
                GD.Print($"[crew] 6 {lost.NpcName} taken out of W2 onto {cell} and down: downed={lost.Downed} seated={lost.Seated} clock={lost.DownedSeconds:0.0}");
                if (!lost.Downed || lost.Seated) { Fail($"{lost.NpcName} is not down on foot"); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 11 when _crewClock >= 0.5f:
            {
                var lost = ctrl.Crew.First(c => c.Id == _crewLostId);
                if (_crewDownSavedAt == null)
                {
                    // a save and a load while it lies down on foot, away from its cage: it must come back lying where the save had it
                    _crewDownSavedAt = lost.GlobalPosition;
                    GD.Print($"[crew] 6 saved while {lost.NpcName} lies down on foot at {DungeonBuilder.WorldToCell(lost.GlobalPosition)}, {Flat(lost.GlobalPosition, ctrl.Cages.First(c => c.Stop == 2).GlobalPosition):0.0} m from cage 2 (escorting={lost.Escorting})");
                    main.SaveGame(CrewSlot);
                    if (!main.LoadGame(CrewSlot)) { Fail("the save with a crew member down on foot would not load"); return; }
                    _crewClock = 0f;
                    break;
                }
                if (!_crewDownLoaded)
                {
                    if (_crewClock < 1f) break;
                    _crewDownLoaded = true;
                    var off = Flat(lost.GlobalPosition, _crewDownSavedAt.Value);
                    GD.Print($"[crew] 6 after the load {lost.NpcName} lies down on foot at {DungeonBuilder.WorldToCell(lost.GlobalPosition)}, {off:0.00} m from where the save had it: downed={lost.Downed} seated={lost.Seated}{(lost.Seated ? $" in {lost.Ride?.Name}" : "")} escorting={lost.Escorting} state={ctrl.StateOf(lost)} (not back in cage 2)");
                    if (!lost.Downed || lost.Seated || lost.Cart is PlatformCage || off > 1f) { Fail($"after the load {lost.NpcName}, down on foot at the save, is {(lost.Seated ? $"seated in {lost.Ride?.Name}" : $"on foot {off:0.00} m from where it lay")} (downed={lost.Downed})"); return; }
                }
                if (ctrl.StateOf(lost) != CrewState.Downed) { Fail($"{lost.NpcName} down on foot counts as {ctrl.StateOf(lost)}, not Downed"); return; }
                if (ctrl.BookingOf(lost) is { } booked) { Fail($"{lost.NpcName}, down on foot, books W{booked.Wagon} seat {booked.Seat}"); return; }
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
                GD.Print($"[crew] 6 re-boarded at stop 2 and departed: train={rc.Train.Count} bench [{BenchNames(root, rc)}] seated in the train [{CrewSeats(ctrl)}]");
                if (rc.Train.Count != rc.Wagons.Count) { Fail($"the train left stop 2 with {rc.Train.Count} of {rc.Wagons.Count} wagons"); return; }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            // stops 3 to the last crew stop, each ridden to for real: its guards die, its cage opens, its crew walk to the train
            case 14:
            {
                if (!CrewArriveAndKill(main, layout, rc, _crewStop, Fail, wait: _crewStop == 4 && !CrewStragglerHome(rc, ctrl, Fail))) break;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 15:
            {
                if (!rc.GateUp(_crewStop)) { if (_crewClock > 2f) Fail($"stop {_crewStop}'s gate did not lift with its guards dead"); break; }
                if (!CrewOpen(main, ctrl, cages.First(c => c.Stop == _crewStop), _crewForgiven ? "Open the cage (Grisel)" : "Open the cage (the orc's keys)", Fail)) return;
                if (_crewStop == 3 && !CrewStragglerOff(main, layout, rc, ctrl, Fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 16:
                if (!CrewDepart(rc, ctrl, _crewStop, Fail)) break;
                _crewClock = 0f;
                if (_crewStop < Shape(rc).Intermediate) { _crewStop++; _crewStep = 14; } // on to the next crew stop
                else _crewStep++;
                break;
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
                var crewN = 2 * Shape(rc).Intermediate;
                GD.Print($"[crew] 7 the end of the line after {_crewClock:0.0} s: completed={ctrl.Completed} home={home}/{living.Count} lost={lostN} stairs locked={stairs?.Locked} cages open={cages.Count(c => c.Open)}/{cages.Count} train={rc.Train.Count} seated in the train [{CrewSeats(ctrl)}] {CrewStates(ctrl)}");
                if (home != living.Count || living.Count != crewN - 1 || lostN != 1) { Fail($"{home} of {living.Count} living crew home, {lostN} lost (want {crewN - 1} of {crewN - 1}, 1 lost)"); return; }
                if (stairs == null || stairs.Locked) { Fail("the down stairs are still locked"); return; }
                _crewStep++;
                _crewClock = 0f;
                if (!_crewForgiven) { CrewSection2(main); break; }
                // forgiven: Grisel gets down with the party and walks with them to the stairs
                if (lead!.Cart == rc.Cart) rc.Cart.Interact(party, lead);
                // onto the head of the flight (its upper treads, short of the landing that asks): Grisel stops Npc.FollowDistance short of
                // the lead, in front of the flight and inside the 4.5 m its foot counts, wherever the cart stops beside the stairs room
                var near = DungeonBuilder.WalkableNeighbor(layout, layout.DownStairs);
                var at = stairs.GlobalTransform * new Vector3(0f, 0.3f, 1.2f);
                party.Teleport(at, main.Builder.Surface(near));
                GD.Print($"[crew] 7 the party gets out and walks to the head of the stairs ({DungeonBuilder.WorldToCell(at)}); Grisel {GriselWhere(root, layout, rc)}");
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

    /// <summary>
    /// The cart is held at <paramref name="stop"/> with its gate down and no boarding; the guards of the stop die by the test's hand (not
    /// yet while <paramref name="wait"/>: the gate stays down, so no countdown starts, until the test is ready).
    /// </summary>
    private bool CrewArriveAndKill(Main main, FloorLayout layout, RailController rc, int stop, Action<string> fail, bool wait = false)
    {
        if (rc.State.Mode == CartMode.Rolling) { if (_crewClock > 60f) fail($"the cart was not held at stop {stop} within 60 s (S={rc.State.S:0.0})"); return false; }
        if (rc.State.Mode != CartMode.Held || rc.StopIndex != stop) { fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex}, not held at stop {stop}"); return false; }
        var room = layout.Rail!.Stops[stop].Room;
        var guards = StopGuards(main, room).ToList();
        if (wait)
        {
            // the guards are the test's to kill: while it waits they stand frozen where they are (alive, so the gate stays down), neither
            // struck by the seated companions nor striking them
            foreach (var e in guards)
                if (e.IsPhysicsProcessing()) { e.SetUntargetable(true); e.SetPhysicsProcess(false); e.SetProcess(false); e.Velocity = Vector3.Zero; }
            return false;
        }
        foreach (var e in guards) if (!e.IsPhysicsProcessing()) { e.SetPhysicsProcess(true); e.SetProcess(true); e.SetUntargetable(false); }
        var ids = string.Join(",", guards.Select(e => e.Group?.Id).Distinct());
        GD.Print($"[crew] {(stop == 1 ? 2 : stop == 2 ? 4 : 7)} held at stop {stop} (room {room}) after {_crewClock:0.0} s: S={rc.State.S:0.0} gate up={rc.GateUp(stop)} boarding={rc.Boarding.Running} hud=\"{rc.HudText}\" guards alive={guards.Count} [{ids}] heroes seated={main.Party.Active.Count(a => a.Seated)} hp=[{string.Join(" ", main.Party.Active.Select(a => $"{a.State.Hp:0}"))}]");
        if (rc.GateUp(stop)) { fail($"stop {stop}'s gate was already up with its guards alive"); return false; }
        if (rc.Boarding.Running) { fail($"the boarding ran at stop {stop} with the gate down"); return false; }
        if (guards.Count == 0) { fail($"stop {stop} has no living guards to hold its gate"); return false; }
        foreach (var e in guards) e.Kill();
        _crewCountdownAt = -1f;
        return true;
    }

    /// <summary>
    /// The cart leaves <paramref name="stop"/> with its whole train, and every one of the stop's opened cage's living, up crew sat in its
    /// wanted seat (the first empty ones) before the departure, which came after the last of them sat in and at most
    /// <see cref="TrainRules.BoardMax"/> + 0.5 s after the countdown started.
    /// </summary>
    private bool CrewDepart(RailController rc, CrewRescueController ctrl, int stop, Action<string> fail)
    {
        if (rc.State.Mode != CartMode.Rolling) { if (_crewClock > 14f) fail($"the cart did not leave stop {stop} (mode {rc.State.Mode}, boarding {rc.Boarding.Running}, {rc.Boarding.Elapsed:0.0} s of countdown, crew on the way {ctrl.OnTheWay(stop)})"); return false; }
        var at = _crewTime;
        var countdown = rc.Boarding.Elapsed; // the countdown just ended in the departure: its whole length, any hold included
        var want = _crewWant.TryGetValue(stop, out var list) ? list : new List<(Npc Npc, int Wagon, int Seat)>();
        var opened = _crewOpenedAt.GetValueOrDefault(stop, at);
        var lines = want.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}").ToList();
        var off = want.Where(c => !IsInstanceValid(c.Npc) || c.Npc.Dead || c.Npc.Cart != rc.WagonAt(c.Wagon) || c.Npc.Seat != c.Seat || !_crewSatAt.ContainsKey(c.Npc))
            .Select(c => $"{c.Npc.NpcName} ({(c.Npc.Seated ? $"in {c.Npc.Ride?.Name} seat {c.Npc.Seat}" : "on foot")}, want W{c.Wagon} seat {c.Seat})").ToList();
        var lastSat = want.Count == 0 ? opened : want.Max(c => _crewSatAt.GetValueOrDefault(c.Npc, float.MaxValue));
        GD.Print($"[crew] 4 departed stop {stop} after {_crewClock:0.0} s: train=[{string.Join(",", rc.Train.Select(w => w.Number))}] seated in the train [{CrewSeats(ctrl)}]");
        if (rc.Train.Count != rc.Wagons.Count) { fail($"the train left stop {stop} with {rc.Train.Count} of {rc.Wagons.Count} wagons"); return false; }
        if (want.Count == 0) { fail($"stop {stop}'s cage let nobody out to walk to the train"); return false; }
        if (off.Count > 0) { fail($"at the departure from stop {stop}, [{string.Join(", ", off)}] did not sit in the first empty seats"); return false; }
        GD.Print($"[crew] stop {stop}: crew [{string.Join(", ", lines)}] seated {lastSat - opened:0.0} s after the cage opened, before the departure");
        var started = _crewCountdownAt >= 0f ? _crewCountdownAt : at - countdown;
        GD.Print($"[crew] stop {stop}: the countdown ran {countdown:0.0} s (started {started - opened:+0.0;-0.0;0.0} s from the cage's opening); the departure came {at - lastSat:0.00} s after the last of them sat in (BoardMax {TrainRules.BoardMax:0} s)");
        if (at < lastSat) { fail($"the departure from stop {stop} came before the last of its crew sat in"); return false; }
        if (countdown > TrainRules.BoardMax + 0.5f) { fail($"the departure from stop {stop} came {countdown:0.0} s after its countdown started, more than BoardMax + 0.5 s"); return false; }
        _crewCountdownAt = -1f;
        return true;
    }

    /// <summary>
    /// The cage opens on one use, by the way <paramref name="expect"/> names (its prompt), with no iron key spent and no pick; its two crew
    /// are freed and get out at its door, each with the first empty seat of the train (that nobody sits in or walks to) as its want.
    /// </summary>
    private bool CrewOpen(Main main, CrewRescueController ctrl, PlatformCage cage, string expect, Action<string> fail)
    {
        var party = main.Party;
        var rc = RailController.Instance!;
        if (_crewForgiven)
        {
            // forgiven: Grisel opens every cage herself, from the bench of the cart held at the cage's stop or on foot within reach
            var g = main.Builder.FloorRoot is { } root ? GriselNpc(root) : null;
            var onBench = g != null && g.Cart == rc.Cart && rc.State.Mode == CartMode.Held && rc.StopIndex == cage.Stop;
            var near = g is { Escorting: true, Seated: false } && g.GlobalPosition.DistanceTo(cage.GlobalPosition) <= CrewRescueController.GriselReach;
            if (g == null || g.Dead || g.Downed || !(onBench || near))
            { fail($"forgiven, but Grisel cannot open cage {cage.Stop}: {(g == null ? "none" : $"dead={g.Dead} downed={g.Downed} bench={g.Cart == rc.Cart} escorting={g.Escorting} {g.GlobalPosition.DistanceTo(cage.GlobalPosition):0.0} m from the cage")}"); return false; }
            GD.Print($"[crew] 3 Grisel at cage {cage.Stop}: {(onBench ? "on the bench" : $"on foot {g.GlobalPosition.DistanceTo(cage.GlobalPosition):0.0} m away")}");
        }
        var crew = ctrl.Crew.Where(c => c.Ride == cage && !c.Dead).ToList();
        var want = TrainRules.SeatOrder(rc.Wagons.Count).Where(p => rc.WagonAt(p.Wagon) is { Coupled: true } w && w.Seats.OccupantOf(p.Seat) == null
            && !ctrl.Crew.Any(n => ctrl.BookingOf(n) == p)).Take(crew.Count).ToList();
        var prompt = cage.Prompt;
        if (prompt != expect) { fail($"cage {cage.Stop} offers \"{prompt}\", not \"{expect}\""); return false; }
        cage.Interact(party, party.Local!);
        var keys = party.Bag.Count("item.iron_key");
        GD.Print($"[crew] 3 cage {cage.Stop} by \"{prompt}\": open={cage.Open} iron keys {_crewKeys}->{keys} crew [{string.Join(", ", crew.Select(c => $"{c.NpcName}:{ctrl.StateOf(c)} {(c.Seated ? $"in {c.Ride?.Name}" : "out")}{(ctrl.BookingOf(c) is { } b ? $" booked W{b.Wagon} seat {b.Seat}" : "")}"))}] hud=\"{ctrl.HudText}\"; cage now offers \"{cage.Prompt}\"");
        if (!cage.Open) { fail($"cage {cage.Stop} did not open"); return false; }
        if (keys != _crewKeys) { fail($"opening cage {cage.Stop} spent an iron key ({_crewKeys} -> {keys})"); return false; }
        if (crew.Count != CrewRules.PerWagon || crew.Any(c => ctrl.StateOf(c) != CrewState.Freed)) { fail($"cage {cage.Stop}'s crew are not two freed"); return false; }
        if (crew.Any(c => c.Cart == cage)) { fail($"cage {cage.Stop} opened, but [{string.Join(", ", crew.Where(c => c.Cart == cage).Select(c => c.NpcName))}] stayed in it"); return false; }
        if (want.Count != crew.Count) { fail($"the train has {want.Count} empty seat(s) for cage {cage.Stop}'s {crew.Count} crew"); return false; }
        if (cage.Prompt != "") { fail($"open cage {cage.Stop} with nobody down in it offers \"{cage.Prompt}\" (\"Let them out\" is gone)"); return false; }
        _crewWant[cage.Stop] = crew.Select((c, i) => (c, want[i].Wagon, want[i].Seat)).ToList();
        _crewOpenedAt[cage.Stop] = _crewTime;
        GD.Print($"[crew] 3 cage {cage.Stop}'s crew run to the first empty seats [{string.Join(", ", _crewWant[cage.Stop].Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}]");
        return true;
    }

    /// <summary>Per frame: when each wanted crew member is first seen in its wanted seat, and when the held stop's countdown started.</summary>
    private void CrewTrackSeats(RailController rc)
    {
        if (rc.State.Mode == CartMode.Held && rc.Boarding.Running && _crewCountdownAt < 0f) _crewCountdownAt = _crewTime - rc.Boarding.Elapsed;
        foreach (var list in _crewWant.Values)
            foreach (var (npc, wagon, seat) in list)
                if (!_crewSatAt.ContainsKey(npc) && IsInstanceValid(npc) && npc.Cart == rc.WagonAt(wagon) && npc.Seat == seat)
                {
                    _crewSatAt[npc] = _crewTime;
                    GD.Print($"[crew] {npc.NpcName} sits in W{wagon} seat {seat} at t={_crewTime:0.0} (stop {rc.StopIndex}, the countdown {(rc.Boarding.Running ? $"at {rc.Boarding.Elapsed:0.0} s" : "not running")})");
                }
    }

    /// <summary>Who sits where in the train now: name W{k} seat {n}, by wagon then seat.</summary>
    private static string CrewSeats(CrewRescueController ctrl) =>
        string.Join(", ", ctrl.Crew.Where(c => !c.Dead && c.Cart is TrainWagon).Select(c => (c, w: ((TrainWagon)c.Cart!).Number)).OrderBy(x => x.w).ThenBy(x => x.c.Seat).Select(x => $"{x.c.NpcName} W{x.w} seat {x.c.Seat}"));

    /// <summary>Ride 1: r1_beam over the train's seated heads (Grisel's on the bench among them when forgiven), as <c>--test-chase</c> measures it. True when it failed.</summary>
    private bool CrewBeam(RailController rc, Action<string> fail)
    {
        if (_crewBeam.Done || RailEncounterController.Instance is not { ArmedRide: 1 } enc) return false;
        if (enc.SetPieceNode("r1_beam") is not LowBeam beam) { fail("floor 12 lacks r1_beam"); return true; }
        if (!_crewBeam.Sample(beam, rc)) return false;
        _crewBeam.Done = true;
        GD.Print($"[crew] 1 beam: r1_beam clears every seated head in the train by {_crewBeam.Min:0.00} m ({_crewBeam.Heads} sample(s) under it at S={beam.S:0.0}, {_crewBeam.Grisel} of them Grisel sitting on the bench)");
        if (_crewBeam.Heads == 0) { fail("no seated head passed under r1_beam"); return true; }
        if (_crewForgiven && _crewBeam.Grisel == 0) { fail("forgiven, but Grisel's head on the bench was never measured under r1_beam"); return true; }
        if (_crewBeam.Min < 0.05f) { fail($"r1_beam clears a seated head by {_crewBeam.Min:0.00} m, under 0.05"); return true; }
        return false;
    }

    // ---- the interact ray through a wagon (the Task 1 carry) --------------------------------------------------

    /// <summary>The look's target (a cage past a wagon, else the cart past W1), where the lead stood before it, and what it must offer.</summary>
    private IInteractable? _crewLookTarget;
    private Vector3 _crewLookBack;
    private string _crewLookWhat = "";

    /// <summary>
    /// Section 2 at stop 2: the lead on foot stands with a coupled wagon (its prompt empty) between its eye and something to use, and aims
    /// at that through the wagon (the real interact ray, read next frame). A cage when one stands within the ray's 3 m past a wagon's far
    /// side; on this line the cages stand beside the platform's middle, a cell out from the lane, so none does (printed), and the target is
    /// the cart, seen from the coupling gap behind W1 through W1.
    /// </summary>
    private bool CrewLookSetUp(Main main, RailController rc, CrewRescueController ctrl, Action<string> fail)
    {
        if (main.Party.Local is not { } me || me.Seated) { fail("the lead is not on foot at stop 2 for the look through a wagon"); return false; }
        _crewLookBack = me.GlobalPosition;
        const float ray = 3f; // the InteractRay's reach (scenes/Adventurer.tscn)
        var cage = ctrl.Cages.First(c => c.Stop == rc.StopIndex);
        // the nearest coupled wagon's far side to the cage's near bars, along the line between them
        TrainWagon? near = null;
        var span = float.MaxValue;
        foreach (var w in rc.Train)
        {
            var d = Flat(w.GlobalPosition, cage.GlobalPosition) - MineCart.TubWidth / 2f - PlatformCage.Width / 2f + MineCart.TubWidth; // from the wagon's far side
            if (d < span) { span = d; near = w; }
        }
        GD.Print($"[crew] look: cage {cage.Stop} stands {span:0.00} m past the far side of the nearest wagon (W{near?.Number}); the interact ray reaches {ray:0} m");
        Vector3 eye, aim;
        if (near != null && span < ray - 0.2f)
        {
            var dir = new Vector3(cage.GlobalPosition.X - near.GlobalPosition.X, 0f, cage.GlobalPosition.Z - near.GlobalPosition.Z).Normalized();
            eye = near.GlobalPosition - dir * (MineCart.TubWidth / 2f + 0.3f);
            aim = cage.GlobalPosition + Vector3.Up * 1.0f;
            _crewLookTarget = cage;
            _crewLookWhat = $"cage {cage.Stop} through W{near.Number}";
        }
        else
        {
            if (rc.WagonAt(1) is not { Coupled: true } w1 || rc.WagonAt(2) is not { Coupled: true } w2) { fail("no W1 and W2 coupled for the look through a wagon"); return false; }
            eye = (w1.GlobalPosition + w2.GlobalPosition) / 2f; // the coupling gap behind W1
            aim = rc.Cart.GlobalTransform * new Vector3(0f, 1.0f, CartRules.HalfLength); // the cart's back, at the tub's middle height
            _crewLookTarget = rc.Cart;
            _crewLookWhat = "the cart through W1 (no cage within the ray past a wagon)";
        }
        me.Teleport(main.Builder.Surface(DungeonBuilder.WorldToCell(eye)) with { X = eye.X, Z = eye.Z });
        me.AimAt(aim);
        return true;
    }

    /// <summary>The look set up a frame ago: the real interact ray offers the target past the wagon, having passed the wagon (its prompt empty). The lead goes back.</summary>
    private bool CrewLookRead(Main main, RailController rc, Action<string> fail)
    {
        var me = main.Party.Local!;
        var looking = me.LookingAt;
        var passed = me.LookedThrough;
        var first = me.LookedThroughFirst;
        var prompt = looking?.Prompt ?? "";
        GD.Print($"[crew] look: aimed at {_crewLookWhat}: the ray offers {(looking == null ? "nothing" : $"{(looking as Node)?.Name} \"{prompt}\"")} past {passed} thing(s) offering nothing (first {(first as Node)?.Name ?? "none"})");
        me.Teleport(_crewLookBack);
        if (looking != _crewLookTarget || prompt == "") { fail($"aimed at {_crewLookWhat}, the interact ray offers {(looking == null ? "nothing" : $"{(looking as Node)?.Name}")}: a wagon offering nothing hid what lies past it"); return false; }
        if (passed < 1 || first is not TrainWagon) { fail($"the look at {_crewLookWhat} did not pass a wagon (passed {passed}, first {(first as Node)?.Name ?? "none"}): it proves nothing"); return false; }
        return true;
    }

    // ---- the straggler (Review Focus 5) ------------------------------------------------------------------------

    /// <summary>
    /// Stop 3, just after its cage opened: its second crew member (booked at the opening) is set down on a walkable cell 30 m off the cart
    /// and held there by a test MoveTarget, so its booking ends with it too far, it stays on foot through the departure, and (held through
    /// ride 4: the collapsing bridge is no place to walk) it is let go at stop 4's arrival.
    /// </summary>
    private bool CrewStragglerOff(Main main, FloorLayout layout, RailController rc, CrewRescueController ctrl, Action<string> fail)
    {
        var want = _crewWant[3];
        var npc = want[^1].Npc;
        _crewStragglerBooked = ctrl.BookingOf(npc) is not null;
        var from = rc.Cart.GlobalPosition;
        var level = layout.LevelOf(DungeonBuilder.WorldToCell(from));
        GridPos? best = null;
        var bestErr = float.MaxValue;
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var c = new GridPos(x, y);
                if (!layout.IsWalkable(c) || layout.IsRail(c) || main.Builder.IsCellBlocked(c) || !CrevasseRules.SetDownOk(layout, c) || layout.LevelOf(c) != level) continue;
                var at = main.Builder.Surface(c);
                var d = new Vector2(at.X - from.X, at.Z - from.Z).Length();
                if (d < 30f || Mathf.Abs(at.Y - from.Y) > 1.5f) continue;
                if (d - 30f < bestErr) { bestErr = d - 30f; best = c; }
            }
        if (best is not { } cell) { fail("no walkable cell 30 m off the cart at stop 3 to hold the straggler on"); return false; }
        _crewStraggler = npc;
        _crewStragglerAt = main.Builder.Surface(cell) + Vector3.Up * 0.1f;
        _crewStragglerHeld = true;
        npc.GlobalPosition = _crewStragglerAt;
        npc.MoveTarget = _crewStragglerAt;
        npc.MoveOverride = true;
        want.RemoveAt(want.Count - 1); // it is not one of stop 3's to sit in before the departure
        GD.Print($"[crew] straggler: {npc.NpcName} (booked at the opening: {_crewStragglerBooked}) held at {cell}, {Flat(npc.GlobalPosition, from):0.0} m off the cart, while the countdown runs");
        if (!_crewStragglerBooked) { fail($"{npc.NpcName} did not book a seat when cage 3 opened"); return false; }
        return true;
    }

    /// <summary>
    /// Per frame while the straggler is held: it stays where the test holds it; its booking's end is noted; at the departure from stop 3 it
    /// must be on foot, unbooked and following. True when it failed.
    /// </summary>
    private bool CrewHoldStraggler(RailController rc, CrewRescueController ctrl, Action<string> fail)
    {
        if (_crewStraggler is not { } npc) return false;
        if (!IsInstanceValid(npc) || npc.Dead) { fail("the straggler died"); return true; }
        if (_crewStragglerHeld)
        {
            npc.MoveTarget = _crewStragglerAt;
            npc.MoveOverride = true;
            if (!_crewStragglerEnded && ctrl.BookingOf(npc) == null) { _crewStragglerEnded = true; _crewStragglerEndedAt = _crewTime; }
            if (_crewStep == 16 && rc.State.Mode == CartMode.Rolling && _crewStop == 3 && _crewStragglerReleasedAt < 0f)
            {
                _crewStragglerReleasedAt = 0f; // judged once, at the departure
                var off = Flat(npc.GlobalPosition, rc.Cart.GlobalPosition);
                GD.Print($"[crew] straggler: at the departure from stop 3 {npc.NpcName} is {(npc.Seated ? $"seated in {npc.Ride?.Name}" : "on foot")}, {off:0.0} m off, booking {(ctrl.BookingOf(npc) is { } b ? $"W{b.Wagon} seat {b.Seat}" : "none")}, escorting={npc.Escorting}; its booking ended {(_crewStragglerEnded ? $"{_crewTime - _crewStragglerEndedAt:0.0} s before the departure" : "never")}");
                if (!_crewStragglerEnded || npc.Seated || !npc.Escorting) { fail($"the straggler {npc.NpcName} is not on foot and following with its booking ended at the departure from stop 3"); return true; }
            }
            if (rc.State.Mode == CartMode.Held && rc.StopIndex == 4)
            {
                _crewStragglerHeld = false;
                npc.MoveTarget = null;
                npc.MoveOverride = false;
                _crewStragglerReleasedAt = _crewTime;
                _crewStragglerLast = npc.GlobalPosition;
                GD.Print($"[crew] straggler: let go at stop 4's arrival, {Flat(npc.GlobalPosition, rc.Cart.GlobalPosition):0.0} m from the cart; it follows on foot");
            }
            return false;
        }
        if (_crewStragglerReleasedAt > 0f && !_crewStragglerJumped)
        {
            if (npc.GlobalPosition.DistanceTo(_crewStragglerLast) > 15f) _crewStragglerJumped = true; // set down beside the cart (Npc.SeatedLeadCatchUp)
            _crewStragglerLast = npc.GlobalPosition;
        }
        return false;
    }

    /// <summary>Stop 4: the straggler has come back to the train and sits in the first empty seat before the guards die (the gate down, no countdown yet). False while it walks.</summary>
    private bool CrewStragglerHome(RailController rc, CrewRescueController ctrl, Action<string> fail)
    {
        if (_crewStraggler is not { } npc || _crewStragglerHeld) return _crewStraggler == null;
        if (npc.Cart is TrainWagon { Coupled: true } w)
        {
            if (_crewStraggler != null && !_crewSatAt.ContainsKey(npc))
            {
                _crewSatAt[npc] = _crewTime;
                GD.Print($"[crew] straggler: {npc.NpcName} boards at stop 4, in W{w.Number} seat {npc.Seat}, {_crewTime - _crewStragglerReleasedAt:0.0} s after it was let go ({(_crewStragglerJumped ? "set down beside the cart by SeatedLeadCatchUp" : "on foot all the way")})");
                var first = TrainRules.SeatOrder(rc.Wagons.Count).First(p => p == (w.Number, npc.Seat) || rc.WagonAt(p.Wagon)?.Seats.OccupantOf(p.Seat) == null);
                if (first != (w.Number, npc.Seat)) { fail($"the straggler sat in W{w.Number} seat {npc.Seat}, not the first empty seat W{first.Wagon} seat {first.Seat}"); return false; }
            }
            return true;
        }
        if (_crewTime - _crewStragglerReleasedAt > 60f) { fail($"the straggler {npc.NpcName} did not board at stop 4 within 60 s of being let go ({Flat(npc.GlobalPosition, rc.Cart.GlobalPosition):0.0} m from the cart, booking {(ctrl.BookingOf(npc) is { } b ? $"W{b.Wagon} seat {b.Seat}" : "none")})"); return false; }
        return false;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>A living enemy that holds a stop's gate: a guard or defender of a collective in an intermediate stop's room.</summary>
    private static bool HoldsAStop(Main main, FloorLayout layout, Enemies.Enemy e)
    {
        if (e.Group is not { Goal: GoalKind.Guard or GoalKind.Defend } g || layout.Rail is not { } rail) return false;
        for (var i = 1; i < rail.Stops.Count - 1; i++) if (rail.Stops[i].Room == g.Room) return true;
        return false;
    }

    /// <summary>The intermediate stops whose room a Guard or Defend group of the floor's contents holds: the gates that start down.</summary>
    private static HashSet<int> CrewGuardedStops(Main main, FloorLayout layout)
    {
        var rooms = (main.Builder.Contents?.Collectives ?? new List<CollectiveContents>()).Where(c => c.Goal is GoalKind.Guard or GoalKind.Defend).Select(c => c.Room).ToHashSet();
        var stops = new HashSet<int>();
        if (layout.Rail is { } rail)
            for (var i = 1; i < rail.Stops.Count - 1; i++) if (rooms.Contains(rail.Stops[i].Room)) stops.Add(i);
        return stops;
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
        _crewLockedHeard = 0;
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
                if (rc.SparesUsed.Count != 0 || rc.StopIndex != 0 || rc.SparesLeft != Shape(rc).Intermediate) { fail($"the rebuilt floor is not fresh (stop {rc.StopIndex}, spares used {rc.SparesUsed.Count}, left {rc.SparesLeft})"); return; }
                _crewStep++;
                _crewClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                break;
            }
            case 1:
            {
                // the cage hold: the rebuilt floor rides into stop 1 with cage 1 locked (its guards culled, its gate up), and the cart waits
                var lead = party.Local;
                if (rc.State.Mode == CartMode.Held && rc.StopIndex == 1 && rc.GateUp(1) && lead != null && lead.Cart == rc.Cart)
                {
                    _crewStep++; // the hold's clock starts now
                    _crewClock = 0f;
                }
                else if (_crewClock > 60f) fail($"the cart was not held at stop 1 with its gate up and the lead seated within 60 s (mode {rc.State.Mode}, stop {rc.StopIndex}, gate up {rc.GateUp(1)}, lead seated {lead?.Cart == rc.Cart})");
                break;
            }
            case 2:
            {
                // still held a countdown and two seconds later: no countdown runs, the bar says the cage is locked, the seat offers the hold
                if (rc.State.Mode != CartMode.Held || rc.Boarding.Running) { fail($"the cart is {rc.State.Mode} (boarding {rc.Boarding.Running}) {_crewClock:0.0} s into the hold at stop 1 with cage 1 locked"); return; }
                if (_crewClock < Boarding.Countdown + 2f) break;
                if (party.Local is not { } lead || lead.Cart != rc.Cart) { fail("the lead is not seated in the cart held at stop 1"); return; }
                var hud = rc.HudText;
                var prompt = rc.Cart.Prompt;
                if (!rc.CageHeld) { fail("CageHeld is false at stop 1 with cage 1 locked"); return; }
                if (hud != "Cart — cage locked") { fail($"the bar reads \"{hud}\", not \"Cart — cage locked\""); return; }
                if (prompt != CageHoldRules.SeatedPrompt || !rc.Cart.HoldOffered) { fail($"the seat offers \"{prompt}\" (hold offered {rc.Cart.HoldOffered}), not \"{CageHoldRules.SeatedPrompt}\""); return; }
                if (_crewLockedHeard != 1) { fail($"\"The cage here is still locked.\" was heard {_crewLockedHeard} times since the rebuild, not once"); return; }
                rc.Cart.HoldInteract(party, lead);
                GD.Print($"[crew] 8 held at stop 1 by its locked cage for {_crewClock:0} s (hud \"{hud}\", prompt \"{prompt}\"); the lead leaves the prisoners behind");
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_crewClock > 6f) fail($"the cart did not leave stop 1 within 6 s of leaving the prisoners (mode {rc.State.Mode}, boarding {rc.Boarding.Running}, CageHeld {rc.CageHeld})"); break; }
                var ctrl = (CrewRescueController)ObjectiveController.Instance!;
                var wagon = ctrl.Cages.First(w => w.Stop == 1);
                var crew = ctrl.Crew.Where(c => c.Ride == wagon).ToList();
                if (wagon.Open) { fail("cage 1 is open"); return; }
                var w1 = rc.WagonAt(1);
                if (w1 is not { Coupled: true } || w1.Riders.Count != 0 || rc.Train.Any(t => t.Riders.Count != 0)) { fail($"W1 left stop 1 with {w1?.Riders.Count} rider(s) (the train's riders {rc.Train.Sum(t => t.Riders.Count)}): stop 1's crew were left, so the train must leave empty"); return; }
                if (!rc.CrewLeftAt.Contains(1)) { fail("CrewLeftAt does not list stop 1"); return; }
                if (crew.Count != CrewRules.PerWagon || crew.Any(c => ctrl.StateOf(c) != CrewState.Lost)) { fail($"wagon 1's crew are not two lost: [{string.Join(", ", crew.Select(c => $"{c.NpcName}:{ctrl.StateOf(c)}"))}]"); return; }
                var line = $"Crew freed 0/{ctrl.Crew.Count}, home 0, 2 lost.";
                if (ctrl.HudText != line) { fail($"the crew line reads \"{ctrl.HudText}\", not \"{line}\" (a left cage's crew are lost, never freed)"); return; }
                if (ctrl.Completed) { fail("the floor is complete with the cages from stop 2 on still locked"); return; }
                GD.Print($"[crew] 8 departed stop 1 without its prisoners: cage 1 locked, its crew Lost (hud \"{ctrl.HudText}\"), W1..W{rc.Train.Count} empty");
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 4:
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
            case 5:
            {
                if (rc.State.Mode == CartMode.Rolling) { if (_crewClock > 15f) fail($"the cart was not wrecked within 15 s (plating {rc.State.Plating:0}, S={rc.State.S:0.0})"); break; }
                GD.Print($"[crew] 8 wrecked after {_crewClock:0.0} s: mode={rc.State.Mode} stop={rc.StopIndex} S={rc.State.S:0.0} (stop {rc.StopS[Math.Clamp(rc.StopIndex, 0, rc.StopS.Length - 1)]:0.0}) plating={rc.State.Plating:0} SparesUsed=[{string.Join(",", rc.SparesUsed)}] left={rc.SparesLeft} seated={party.Active.Count(a => a.Seated)}");
                if (rc.State.Mode != CartMode.Held) { fail($"after the wreck the cart is {rc.State.Mode}, not held at a stop by a spare"); return; }
                if (rc.SparesUsed.Count != 1 || !rc.SparesUsed.Contains(rc.StopIndex)) { fail($"spares used [{string.Join(",", rc.SparesUsed)}], the cart at stop {rc.StopIndex}"); return; }
                if (rc.StopIndex is not (1 or 2)) { fail($"the spare stands at stop {rc.StopIndex}, not stop 1 or 2"); return; }
                if (Mathf.Abs(rc.State.S - rc.StopS[rc.StopIndex]) > 0.05f || rc.State.Plating < CartRules.MaxPlating - 0.01f) { fail("the spare is not a whole cart at its stop"); return; }
                if (CrewGriselVerdict() is { } wrong) { fail(wrong); return; }
                // the save and load between stops 2 and 3 were quiet: no prop's cue, no hazard's warning within 2 s; no rail cue on the plain path
                var props = SoundsAfter(_crewLoadAt, 2.0, "gate_lift", "timber_collapse", "timber_smash", "crate_break", "steam_burst", "switch_clunk");
                var hazards = SoundsAfter(_crewLoadAt, 2.0, "hammer_wind", "pour_hiss");
                GD.Print($"[crew] sound: within 2 s of the load props [{string.Join(", ", props)}] hazards [{string.Join(", ", hazards)}]; rail cues relayed on the plain path [{string.Join(", ", _soundRelayed)}]; one-shots heard {_soundPlays.Count}");
                if (props.Count > 0 || hazards.Count > 0) { fail($"the load was not quiet: {string.Join(", ", props.Concat(hazards))}"); return; }
                if (_soundRelayed.Count > 0) { fail($"rail cues went through EventBus.Sfx: {string.Join(", ", _soundRelayed)}"); return; }
                GD.Print($"[crew] Grisel's calls heard: [{string.Join(", ", _crewGriselSaid.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"))}]");
                // on to stop 2 (the brute dies first: it stands on the lane), where a save and a load must keep stop 1's prisoners left behind
                _crewBrute?.Kill();
                _crewBrute = null;
                if (rc.StopIndex == 1)
                {
                    if (rc.CageHeld) { fail("the spare at stop 1 is held by cage 1, whose prisoners were left behind"); return; }
                    if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                }
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 6:
            {
                // 9. held at stop 2 (its cage locked, so the cage holds the cart): save and load there, past stop 1
                if (rc.State.Mode == CartMode.Held && rc.StopIndex == 2 && Mathf.Abs(rc.State.S - rc.StopS[2]) < 0.05f)
                {
                    GD.Print($"[crew] 9 held at stop 2 (CageHeld {rc.CageHeld}, left behind at [{string.Join(",", rc.CrewLeftAt.OrderBy(x => x))}], hud \"{((CrewRescueController)ObjectiveController.Instance!).HudText}\"): save and load");
                    main.SaveGame(CrewSlot);
                    if (!main.LoadGame(CrewSlot)) { fail("the save at stop 2 would not load"); return; }
                    _crewStep++;
                    _crewClock = 0f;
                }
                else if (_crewClock > 90f) fail($"the cart never stood at stop 2 within 90 s (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0}, stop 2 at {rc.StopS[2]:0.0})");
                break;
            }
            case 7 when _crewClock >= 1f:
            {
                // after the load: stop 1's cage still counts as left (its crew lost, it blocks nothing), stop 2's (where the cart stands) holds again
                var ctrl = (CrewRescueController)ObjectiveController.Instance!;
                var wagon1 = ctrl.Cages.First(w => w.Stop == 1);
                var crew1 = ctrl.Crew.Where(c => c.Ride == wagon1).ToList();
                var left = string.Join(",", rc.CrewLeftAt.OrderBy(x => x));
                GD.Print($"[crew] 9 after the load at stop 2: mode={rc.State.Mode} stop={rc.StopIndex} left behind at [{left}] cages blocking {ctrl.CagesBlockingNow} CageHeld {rc.CageHeld} wagon 1 open={wagon1.Open} [{string.Join(", ", crew1.Select(c => $"{c.NpcName}:{ctrl.StateOf(c)}"))}] (hud \"{ctrl.HudText}\")");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 2) { fail($"the cart came back {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 2"); return; }
                if (wagon1.Open) { fail("cage 1 came back open"); return; }
                if (left != "1") { fail($"after the load the prisoners are left behind at [{left}], not stop 1 alone"); return; }
                if (crew1.Count != CrewRules.PerWagon || crew1.Any(c => ctrl.StateOf(c) != CrewState.Lost)) { fail($"after the load wagon 1's crew are not two lost: [{string.Join(", ", crew1.Select(c => $"{c.NpcName}:{ctrl.StateOf(c)}"))}]"); return; }
                if (ctrl.CagesBlockingNow != ctrl.Cages.Count - 1) { fail($"{ctrl.CagesBlockingNow} cages block the stairs after the load, not {ctrl.Cages.Count - 1} (cage 1 was left behind)"); return; }
                var line = $"Crew freed 0/{ctrl.Crew.Count}, home 0, 2 lost.";
                if (ctrl.HudText != line) { fail($"after the load the crew line reads \"{ctrl.HudText}\", not \"{line}\""); return; }
                if (!rc.CageHeld) { fail("cage 2 does not hold the cart restored at stop 2"); return; }
                GD.Print("[crew] 9 the load kept stop 1's prisoners left behind; the cage at stop 2, where the cart stands, holds again");
                if (!CrewLookSetUp(main, rc, ctrl, fail)) return;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 8 when _crewClock >= 0.4f:
            {
                var ctrl = (CrewRescueController)ObjectiveController.Instance!;
                if (!CrewLookRead(main, rc, fail)) return;
                // 10. stop 2's cage opens (the test's hook, as a key would): its crew walk to the first empty seats, W1's (stop 1's were left)
                _crewWreckCage2.Clear();
                foreach (var c in ctrl.Crew) if (c.Cart is PlatformCage { Stop: 2 }) _crewWreckCage2.Add(c.Id);
                if (_crewWreckCage2.Count != CrewRules.PerWagon) { fail($"cage 2 holds {_crewWreckCage2.Count} crew, not two"); return; }
                if (!ctrl.DebugOpen(2)) { fail("the test could not open cage 2"); return; }
                _crewOpenedAt[2] = _crewTime;
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 9:
            {
                var ctrl = (CrewRescueController)ObjectiveController.Instance!;
                var crew2 = ctrl.Crew.Where(c => !c.Dead && _crewWreckCage2.Contains(c.Id)).ToList();
                var w1 = rc.WagonAt(1)!;
                var seated = crew2.Where(c => c.Cart == w1).OrderBy(c => c.Seat).ToList();
                if (seated.Count < CrewRules.PerWagon) { if (_crewClock > 12f) fail($"stop 2's crew did not take W1 within 12 s: [{string.Join(", ", crew2.Select(c => $"{c.NpcName} {(c.Seated ? $"in {c.Ride?.Name} seat {c.Seat}" : "on foot")}{(ctrl.BookingOf(c) is { } b ? $" booked W{b.Wagon} seat {b.Seat}" : "")}"))}]"); break; }
                var seats = string.Join(", ", seated.Select(c => $"{c.NpcName} W1 seat {c.Seat}"));
                if (!seated.Select(c => c.Seat).SequenceEqual(TrainRules.CrewSeats)) { fail($"stop 2's crew sit in W1 as [{seats}], not seats 4 and 5"); return; }
                GD.Print($"[crew] 10 stop 2's crew took W1 (stop 1's were left): [{seats}] {_crewTime - _crewOpenedAt[2]:0.0} s after the cage opened");
                // the last wrecks: the cart is wrecked where it stands, with the local hero beside it, until no spare is left to take
                // over; the wreck is then hidden, and the cart bar must show nothing over it (not "Cart — wrecked")
                if (party.Local is not { } me) { fail("no local hero for the last wreck"); return; }
                var more = 0;
                while (rc.Cart.Visible && more < Shape(rc).Intermediate)
                {
                    me.Teleport(rc.Cart.GlobalPosition + rc.Cart.GlobalTransform.Basis.X * 3f);
                    rc.DebugWreck();
                    more++;
                }
                if (rc.Cart.Visible) { fail($"the cart still stands after {more} more wreck(s) (spares used [{string.Join(",", rc.SparesUsed)}])"); return; }
                GD.Print($"[crew] 8 no spare left after {more} more wreck(s): spares used [{string.Join(",", rc.SparesUsed)}], the wreck hidden at S={rc.State.S:0.0}");
                _crewStep++;
                _crewClock = 0f;
                break;
            }
            case 10 when _crewClock >= 0.5f:
            {
                // half a second on: the bar has been refreshed (every 0.25 s) with the hero still within its range of the hidden wreck
                var distance = party.Local is { } me ? me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) : float.PositiveInfinity;
                GD.Print($"[crew] hud after the last wreck: {(rc.HudText is { } hud ? $"\"{hud}\"" : "none")} (the local hero {distance:0.0} m from the hidden cart)");
                if (distance > RailController.HudRange) { fail($"the local hero stands {distance:0.0} m from the hidden wreck, beyond the bar's {RailController.HudRange} m: the check means nothing"); return; }
                if (rc.HudText != null) { fail($"the cart bar shows \"{rc.HudText}\" over a hidden wreck"); return; }
                LockRules.ForcedChance = null;
                GD.Print($"[crew] RESULT ok ({CrewBranch})");
                _done = true;
                GetTree().Quit();
                break;
            }
        }
    }
}
