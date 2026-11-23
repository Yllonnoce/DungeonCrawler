using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-island=onfoot|stranded|grisel|chest (plan 15, Task 7) ----------------------------------------------------------------------
// Home and lost on the island floor and the win's reward chest (Rulings 32-37, 41, 44), under --test-island's conventions (solo, lethality 3,
// every enemy culled, Shape(rc) for every stop and id, gathering on; _quitAfter 450 for =onfoot, 300 for the rest).
// =onfoot (Review Focus 1, B.10's on-foot win): rc.Restore(2), stops 1-2's crew seated in W1 and W2, every spare spent; ridden out and wrecked on
// ride 3 with no spare ("No carts left. The rest is on foot.", the crew let out to follow); the party and those crew put beside the parked train
// (stop 4's holders spared by the cull, its cage locked). The lead's "Board the cart" takes over: the countdown within 1 s of the lead sitting with
// stop 4's holders alive and its cage locked; the crew in W1 and W2 at the departure; the arrival at the last stop; once the lead is inside, the
// cages at stops 3 and 4 left behind (never opened) and their crew lost; stops 1-2's crew home; the win (jailbreak_freed_all, the stairs
// unsealed, the reward chest stood).
// =stranded (Review Focus 2): the last ride with every cage's crew seated; once the train's rear is past the exit bridge (so past the pit's far
// edge: pre-flight F11) one crew member is downed in W1 (ctrl.DebugDown) and one put on foot outside beside the parked train (ctrl.DebugPutOnFoot).
// At the arrival the outsider is Lost within 1 s and stays outside and Lost for 12 s, its catch-up held, never teleported; the lead outside: Freed
// within 1 s; back inside: Lost within 1 s; the downed one revived (its bleed clock under 50 s: F11) wins the floor, and the lead outside afterwards
// leaves it won.
// =grisel (Review Focus 2): forgiven, Grisel escorting; the last ride with every cage's crew seated; as the train's rear passes the exit bridge she
// is put on foot outside on ride 5's lane at S 1150. Held: outside, never teleported, until "Grisel picks her way in over the bridge behind you."
// is logged, within 1 s of the arrival with the lead inside, and she stands on an island cell beside the train's last body. The party at the head
// of the stairs: she follows and q_grisel_out completes.
// =chest: the last ride with every cage's crew seated: within 1 s of the win the reward chest stands in room 9 (table.vault, index 9000, no
// mimic, no lock) and its line is logged; room 10's door revealed and open; the lead opens it and an item drops; a save and a load keep it,
// opened; with the party in the safe room 40 s a group gathers inside the island (the cull spares the gathering's creatures: F12).
public partial class DebugTools
{
    private Npc? _ihA, _ihB;
    private Adventurer? _ihOnChest;
    private Vector3 _ihLast;
    private float _ihT = float.NaN, _ihArriveT = float.NaN;
    private bool _ihHeldSeen, _ihAHome, _ihLetOut, _ihCountdownOk, _ihListening, _ihGriselLogged, _ihChestLogged, _ihSpareGather;
    private int _ihLootBefore, _ihGatherBefore;
    private readonly List<string> _ihCrew = new();

    private void IslandHomeListen()
    {
        if (_ihListening) return;
        _ihListening = true;
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testIsland) return;
            if (text == CrewRescueController.GriselInLine) _ihGriselLogged = true;
            if (text == CrewRescueController.RewardChestLine) _ihChestLogged = true;
        };
    }

    /// <summary>=chest's gathering step: the cull spares the creatures holding at the safe room's gather cell (pre-flight F12).</summary>
    private bool IslandGathering(FloorLayout layout, Enemies.Enemy e)
        => _ihSpareGather && SafeRoomRules.GatherCell(layout) is { } c && DungeonBuilder.WorldToCell(e.GlobalPosition).ManhattanDistance(c) <= 2;

    private static float IslandRearS(RailController rc) => RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
    private static bool Inside(FloorLayout layout, Node3D body) => layout.InIsland(DungeonBuilder.WorldToCell(body.GlobalPosition));
    private static string At(Node3D body) => DungeonBuilder.WorldToCell(body.GlobalPosition).ToString();

    /// <summary>The crew of stop <paramref name="stop"/>'s cage seated in wagon <paramref name="stop"/>, for every intermediate stop. Null, or why not.</summary>
    private static string? IslandSeatEveryCage(CrewRescueController ctrl, LineShape shape)
    {
        for (var k = 1; k <= shape.Intermediate; k++)
            if (ctrl.DebugSeatCrew(k, wagon: k) is not { Count: > 0 }) return $"stop {k}'s crew not seated in W{k}";
        return null;
    }

    /// <summary>The win as the flows read it: completed, jailbreak_freed_all set, the stairs unsealed and the reward chest standing. Null, or what is missing.</summary>
    private static string? IslandWon(CrewRescueController ctrl, Node3D root)
    {
        var missing = new List<string>();
        if (!ctrl.Completed) missing.Add("not completed");
        if (AdventureDirector.Instance?.HasFlag(CrewRescueController.FreedAllFlag) != true) missing.Add($"{CrewRescueController.FreedAllFlag} not set");
        if (root.GetNodeOrNull<Stairs>("DownStairs") is not { Locked: false }) missing.Add("the stairs still sealed");
        if (ctrl.RewardChest == null) missing.Add("no reward chest");
        return missing.Count == 0 ? null : string.Join(", ", missing);
    }

    private static string IslandCrewStates(CrewRescueController ctrl)
        => string.Join(", ", ctrl.Crew.Where(IsInstanceValid).Select(n => $"{n.NpcName}={ctrl.StateOf(n)}{(n.Seated ? " seated" : "")}"));

    // ---- =onfoot --------------------------------------------------------------------------------------------------------------------

    private void IslandOnFoot(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Node3D root, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        IslandLiftListen();
        if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
        if (enc.Parked is not { } train) { Fail("floor 12 has no live parked train"); return; }
        if (rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {At(fell)}"); return; }
        var cage4 = ctrl.Cages.FirstOrDefault(c => c.Stop == shape.LastRideStop);
        int Holders() => root.GetChildren().OfType<Enemies.Enemy>().Count(e => e.IsAlive && e.Group?.Id == "col_stop4");
        switch (_isStep)
        {
            case 0:
            {
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate).Where(k => k != shape.LastRideStop)); // stop 4's gate stays down: its holders are spared
                rc.Restore(2);
                var w1 = ctrl.DebugSeatCrew(1, wagon: 1);
                var w2 = ctrl.DebugSeatCrew(2, wagon: 2);
                if (w1 is not { Count: > 0 } || w2 is not { Count: > 0 }) { Fail($"stops 1 and 2's crew not seated in W1 and W2 ({w1?.Count}, {w2?.Count})"); return; }
                rc.DebugSpendSpares();
                _ihCrew.Clear();
                _ihCrew.AddRange(w1.Concat(w2).Select(c => c.Npc.Id));
                GD.Print($"[island] onfoot: restored at stop {rc.StopIndex}, crew [{string.Join(", ", w1.Concat(w2).Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}], {rc.SparesLeft} spare(s) left, stop {shape.LastRideStop}'s gate up {rc.GateUp(shape.LastRideStop)}");
                if (rc.SparesLeft != 0) { Fail($"{rc.SparesLeft} spare(s) left after DebugSpendSpares"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 1 when _isClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 2:
            {
                if (rc.State.Mode != CartMode.Rolling || rc.StopIndex != 2 || _isClock < 3f) { if (_isClock > 30f) Fail($"ride 3 never got under way (stop {rc.StopIndex}, mode {rc.State.Mode})"); break; }
                var s = rc.State.S;
                rc.DebugWreck();
                if (rc.State.Mode != CartMode.Wrecked || rc.Cart.Visible || !_ilNoCartLogged) { Fail($"the wreck on ride 3: mode {rc.State.Mode}, the cart shown {rc.Cart.Visible}, \"No carts left. The rest is on foot.\" logged {_ilNoCartLogged}"); return; }
                GD.Print($"[island] onfoot: wrecked on ride 3 at S={s:0.0} with no spare left: \"No carts left. The rest is on foot.\"");
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                // the OnFoot event lets the train's crew out at the controller's flush: they follow on foot
                var crew0 = IslandCrew().Where(n => _ihCrew.Contains(n.Id)).ToList();
                if (crew0.Count != _ihCrew.Count || crew0.Any(n => n.Seated || !n.Escorting)) { if (_isClock > 1f) Fail($"1 s after the wreck the train's crew were not let out to follow [{string.Join(", ", crew0.Select(n => $"{n.NpcName} seated={n.Seated} following={n.Escorting}"))}]"); break; }
                if (!_ihLetOut) { _ihLetOut = true; GD.Print($"[island] onfoot: the crew let out [{string.Join(", ", crew0.Select(n => $"{n.NpcName} {At(n)} following={n.Escorting}"))}]"); }
                if (enc.ParkedNow != ParkedState.Lift) { if (_isClock > 1f) Fail($"1 s after the no-spare wreck the parked train is {enc.ParkedNow}, not a lift"); break; }
                var cart = train.Cart;
                party.Teleport(BesideLift(main, layout, rc, enc, cart), cart.GlobalPosition + Vector3.Up * 0.9f);
                var here = DungeonBuilder.WorldToCell(me.GlobalPosition);
                // each pair beside the parked tub its wagon will stand at once the lift takes over (W1, W2): where it would step out, outside
                var crew = IslandCrew().Where(n => _ihCrew.Contains(n.Id)).ToList();
                for (var i = 0; i < crew.Count; i++)
                    ctrl.DebugPutOnFoot(crew[i].Id, rc.StandBesideLane(train.Wagons[Math.Min(i / 2, train.Wagons.Count - 1)].GlobalPosition));
                GD.Print($"[island] onfoot: the party beside the lift at {here} ({cart.TubDistance(me):0.0} m from its cart's tub), the crew at [{string.Join(", ", crew.Select(n => $"{n.NpcName} {At(n)}"))}]; stop {shape.LastRideStop}'s holders alive {Holders()}, its cage open {cage4?.Open}");
                if (cart.Prompt != ParkedRules.BoardPrompt) { Fail($"the lead beside the lift is offered \"{cart.Prompt}\""); return; }
                cart.Interact(party, me);
                _ihT = _isTime;
                if (rc.Takeovers != 1 || me.Cart != rc.Cart || !rc.StandingPast) { Fail($"the press: {rc.Takeovers} takeover(s), the lead in {(me.Cart as Node)?.Name ?? "nothing"}, StandingPast {rc.StandingPast}"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                if (rc.State.Mode == CartMode.Held)
                {
                    if (!_ihCountdownOk && rc.Boarding.Running)
                    {
                        _ihCountdownOk = true;
                        var holders = Holders();
                        GD.Print($"[island] onfoot: the takeover's countdown started {_isTime - _ihT:0.00} s after the lead sat; stop {shape.LastRideStop}'s holders alive {holders}, its cage open {cage4?.Open}");
                        if (_isTime - _ihT > 1f || holders == 0 || cage4 is not { Open: false }) { Fail($"the countdown {_isTime - _ihT:0.00} s after the lead sat with {holders} holder(s) alive and stop {shape.LastRideStop}'s cage {(cage4?.Open == true ? "open" : "locked")}"); return; }
                    }
                    if (!_ihCountdownOk && _isTime - _ihT > 1f) { Fail($"no countdown 1 s after the lead sat (cage held {rc.CageHeld})"); return; }
                    if (_isClock > 20f) Fail("the taken-over cart never departed");
                    break;
                }
                if (!_ihCountdownOk) { Fail($"the taken-over cart is {rc.State.Mode} with the countdown's check never made (fix round 1, M1)"); return; }
                var crew = IslandCrew().Where(n => _ihCrew.Contains(n.Id)).ToList();
                var seats = crew.Select(n => (n, w: n.Ride as TrainWagon)).ToList();
                GD.Print($"[island] onfoot: departed {_isTime - _ihT:0.00} s after the press with the crew [{string.Join(", ", seats.Select(p => $"{p.n.NpcName} {(p.w == null ? "on foot" : $"W{p.w.Number}")}"))}]");
                if (seats.Any(p => p.w is not { Number: 1 or 2 } w || rc.WagonAt(w.Number) != w)) { Fail("stops 1-2's crew are not all seated in W1 and W2 at the departure"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            {
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the taken-over train was wrecked at S={rc.State.S:0.0}"); return; }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides) { if (_isClock > 60f) Fail($"the taken-over train did not arrive at stop {shape.Rides} (S={rc.State.S:0.0}, mode {rc.State.Mode})"); break; }
                GD.Print($"[island] onfoot: arrived at stop {rc.StopIndex} with the lead inside {Inside(layout, me)}; {IslandCrewStates(ctrl)}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 6:
            {
                var problems = new List<string>();
                foreach (var stop in new[] { 3, shape.LastRideStop })
                {
                    if (!rc.CrewLeftAt.Contains(stop)) problems.Add($"stop {stop}'s cage not left behind");
                    foreach (var n in ctrl.Crew.Where(n => IsInstanceValid(n) && ctrl.Cages.FirstOrDefault(c => c.Stop == stop) is { } cg && n.Ride == cg))
                        if (ctrl.StateOf(n) != CrewState.Lost) problems.Add($"{n.NpcName} (stop {stop}) is {ctrl.StateOf(n)}");
                }
                foreach (var n in IslandCrew().Where(n => _ihCrew.Contains(n.Id)))
                    if (ctrl.StateOf(n) != CrewState.Home) problems.Add($"{n.NpcName} is {ctrl.StateOf(n)}");
                if (IslandWon(ctrl, root) is { } notWon) problems.Add(notWon);
                if (problems.Count > 0) { if (_isClock > 2f) Fail($"after the arrival: {string.Join("; ", problems)} ({IslandCrewStates(ctrl)})"); break; }
                Pass($"wrecked on ride 3 with no spare, the party and stops 1-2's crew beside the parked train; the lead's press took over and its countdown started at once with stop {shape.LastRideStop}'s holders alive and its cage locked; the crew rode in W1 and W2 to stop {shape.Rides}; with the lead inside the cages at stops 3 and {shape.LastRideStop} were left behind (never opened) and their crew lost; stops 1-2's crew home; the win: {CrewRescueController.FreedAllFlag}, the stairs unsealed, the reward chest stood ({_isClock:0.00} s after the arrival)");
                return;
            }
        }
    }

    // ---- =stranded ------------------------------------------------------------------------------------------------------------------

    private void IslandStranded(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
        if (_isStep < 3 && rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        if (_isStep < 3) ThrowPitOnTheWay(main, rc, "island");
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {At(fell)}"); return; }
        var root = main.Builder.FloorRoot!;
        // the outsider, once on foot outside: never inside, never teleported (a jump over 3 m in a frame) before the lead goes out
        if (_ihB is { } b && IsInstanceValid(b) && _isStep is >= 3 and <= 4)
        {
            if (b.Dead) { Fail($"{b.NpcName}, on foot outside, died"); return; }
            if (Inside(layout, b)) { Fail($"{b.NpcName}, on foot outside, stands inside the island at {At(b)}"); return; }
            if (b.GlobalPosition.DistanceTo(_ihLast) > 3f) { Fail($"{b.NpcName}, on foot outside, was teleported from {DungeonBuilder.WorldToCell(_ihLast)} to {At(b)}"); return; }
            _ihLast = b.GlobalPosition;
            if (b.CatchUpHeld) _ihHeldSeen = true;
        }
        switch (_isStep)
        {
            case 0:
            {
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate));
                rc.Restore(shape.LastRideStop);
                if (IslandSeatEveryCage(ctrl, shape) is { } bad) { Fail(bad); return; }
                GD.Print($"[island] stranded: restored at stop {rc.StopIndex} with every cage's crew seated: {IslandCrewStates(ctrl)}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 1 when _isClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 2:
            {
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != shape.LastRideStop && rc.State.Mode == CartMode.Held) { Fail($"the last ride ended another way (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
                var rear = IslandRearS(rc);
                if (rc.State.Mode != CartMode.Rolling || !(rear > enc.ExitToS + 0.5f)) { if (_isClock > 120f) Fail($"the train's rear never passed the exit bridge (at S={rear:0.0})"); break; }
                if (!(rear > enc.PitFarS)) { Fail($"the down would fire with the rear at S={rear:0.0}, short of the pit's far edge {enc.PitFarS:0.0} (F11)"); return; }
                if (enc.Parked is not { } parked) { Fail("no parked train to stand the outsider beside"); return; }
                _ihA = ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && !n.Dead && n.Ride is TrainWagon { Number: 1 });
                _ihB = ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && !n.Dead && n.Ride is TrainWagon { Number: 2 });
                if (_ihA == null || _ihB == null) { Fail("no crew in W1 and W2 to down and to put on foot"); return; }
                ctrl.DebugDown(_ihA.Id);
                ctrl.DebugPutOnFoot(_ihB.Id, BesideLift(main, layout, rc, enc, parked.Cart));
                _ihLast = _ihB.GlobalPosition;
                GD.Print($"[island] stranded: with the train's rear at S={rear:0.0} (the pit's far edge {enc.PitFarS:0.0}, the bridge's {enc.ExitToS:0.0}) {_ihA.NpcName} downed in W1 (Downed {_ihA.Downed}) and {_ihB.NpcName} on foot outside beside the parked train at {At(_ihB)} (inside {Inside(layout, _ihB)})");
                if (!_ihA.Downed || Inside(layout, _ihB) || _ihB.Seated) { Fail("the setup did not take"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the train was wrecked at S={rc.State.S:0.0}"); return; }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides) { if (_isClock > 60f) Fail($"the train did not arrive at stop {shape.Rides}"); break; }
                if (_ihArriveT is float.NaN) { _ihArriveT = _isTime; GD.Print($"[island] stranded: arrived at stop {rc.StopIndex} with the lead inside {Inside(layout, me)}; {IslandCrewStates(ctrl)}"); }
                if (ctrl.StateOf(_ihB!) != CrewState.Lost) { if (_isTime - _ihArriveT > 1f) Fail($"{_ihB!.NpcName}, stranded outside, is {ctrl.StateOf(_ihB)} 1 s after the arrival"); break; }
                GD.Print($"[island] stranded: {_ihB!.NpcName} Lost {_isTime - _ihArriveT:0.00} s after the arrival, at {At(_ihB)}; {_ihA!.NpcName} {ctrl.StateOf(_ihA)}; completed {ctrl.Completed}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                if (ctrl.StateOf(_ihB!) != CrewState.Lost) { Fail($"{_ihB!.NpcName} is {ctrl.StateOf(_ihB)} {_isClock:0.0} s into the 12 s stranded"); return; }
                if (ctrl.Completed || ctrl.StateOf(_ihA!) != CrewState.Downed) { Fail($"while {_ihA!.NpcName} is down the floor is completed {ctrl.Completed} ({ctrl.StateOf(_ihA)})"); return; }
                if (_isClock < 12f) break;
                if (!_ihHeldSeen) { Fail($"{_ihB!.NpcName}'s catch-up was never held in 12 s stranded"); return; }
                GD.Print($"[island] stranded: 12 s on {_ihB!.NpcName} is still outside at {At(_ihB)} and Lost, its catch-up held, never teleported");
                me.Teleport(BesideLift(main, layout, rc, enc, enc.Parked!.Cart), enc.Parked.Cart.GlobalPosition);
                _ihT = _isTime;
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            {
                if (Inside(layout, me)) { Fail($"the lead teleported outside stands inside at {At(me)}"); return; }
                if (ctrl.StateOf(_ihB!) != CrewState.Freed) { if (_isClock > 1f) Fail($"with the lead outside at {At(me)} {_ihB!.NpcName} is {ctrl.StateOf(_ihB)} 1 s on"); break; }
                GD.Print($"[island] stranded: the lead outside at {At(me)}: {_ihB!.NpcName} Freed {_isClock:0.00} s on");
                me.Teleport(rc.StandBesideCart(), rc.Cart.GlobalPosition);
                _isStep++; _isClock = 0f;
                break;
            }
            case 6:
            {
                if (!Inside(layout, me)) { Fail($"the lead teleported back stands outside at {At(me)}"); return; }
                if (ctrl.StateOf(_ihB!) != CrewState.Lost) { if (_isClock > 1f) Fail($"with the lead back inside at {At(me)} {_ihB!.NpcName} is {ctrl.StateOf(_ihB)} 1 s on"); break; }
                GD.Print($"[island] stranded: the lead back inside at {At(me)}: {_ihB!.NpcName} Lost {_isClock:0.00} s on; the lead goes down there (nobody else outside, no human up)");
                // fix round 1 (I2): the lead down inside, held down (no calm auto-revive, no possession of a companion): no human is up
                party.AllowPossession = false;
                me.State.Mangled = true;
                me.State.Hp = 0;
                party.OnMemberDowned(me.Index);
                _isStep++; _isClock = 0f;
                break;
            }
            case 7:
            {
                if (!me.Downed) { Fail("the lead did not stay down"); return; }
                if (ctrl.StateOf(_ihB!) == CrewState.Lost) { if (_isClock > 1f) Fail($"with no human up {_ihB!.NpcName} is still Lost 1 s on"); break; }
                var bleed = _ihA!.DownedSeconds;
                GD.Print($"[island] stranded: the lead down inside: {_ihB!.NpcName} {ctrl.StateOf(_ihB)} {_isClock:0.00} s on; {_ihA.NpcName} revived with its bleed clock at {bleed:0.0} s (F11: under 50)");
                if (bleed >= 50f || _ihA.Dead) { Fail($"{_ihA.NpcName}'s bleed clock reached {bleed:0.0} s (dead {_ihA.Dead})"); return; }
                if (!_ihA.TryRevive()) { Fail($"{_ihA.NpcName}'s revive was refused"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 8:
            {
                // fix round 2 (item 7): the revived crewman is Home (seated in his wagon at the end) within 1 s of his revive
                if (ctrl.StateOf(_ihA!) != CrewState.Home) { if (_isClock > 1f) { Fail($"{_ihA!.NpcName} is {ctrl.StateOf(_ihA)} 1 s after his revive"); return; } }
                else if (!_ihAHome) { _ihAHome = true; GD.Print($"[island] stranded: {_ihA!.NpcName} Home {_isClock:0.00} s after his revive"); }
                // every other crew member home: a Lost here would win the floor
                if (ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && ctrl.StateOf(n) == CrewState.Lost) is { } lost) { Fail($"with the lead down and no human up {lost.NpcName} is Lost ({IslandCrewStates(ctrl)})"); return; }
                if (ctrl.Completed) { Fail($"with the lead down and no human up the floor was won ({IslandCrewStates(ctrl)})"); return; }
                if (_isClock < 2f || !_ihAHome) break;
                GD.Print($"[island] stranded: 2 s with the lead down and {_ihA!.NpcName} up: no crew member Lost, no win ({IslandCrewStates(ctrl)}); the lead gets up");
                me.State.Mangled = false;
                me.Revive();
                me.State.Hp = Math.Max(me.State.Hp, me.State.Stats[Characters.Stat.MaxHp] * 0.5f);
                party.AllowPossession = true;
                _isStep++; _isClock = 0f;
                break;
            }
            case 9:
            {
                if (IslandWon(ctrl, root) is { } notWon) { if (_isClock > 1f) Fail($"1 s after the lead got up inside: {notWon} ({IslandCrewStates(ctrl)})"); break; }
                GD.Print($"[island] stranded: the win {_isClock:0.00} s after the lead got up inside ({IslandCrewStates(ctrl)}); the lead goes outside again");
                me.Teleport(BesideLift(main, layout, rc, enc, enc.Parked!.Cart), enc.Parked.Cart.GlobalPosition);
                _isStep++; _isClock = 0f;
                break;
            }
            case 10:
            {
                if (!ctrl.Completed) { Fail($"with the lead outside after the win the floor is no longer completed ({IslandCrewStates(ctrl)})"); return; }
                if (_isClock < 2f) break;
                Pass($"at the arrival {_ihB!.NpcName}, on foot outside, was Lost and stayed outside and Lost 12 s with its catch-up held, never teleported; the lead outside made it Freed, back inside Lost again; the lead down inside (no human up): not Lost, and with {_ihA!.NpcName} revived (downed past the bridge, its bleed clock under 50 s) no crew member Lost and no win for 2 s; the downed lead up again won the floor, and the lead outside afterwards left the win standing ({ctrl.StateOf(_ihB)})");
                return;
            }
        }
    }

    // ---- =grisel --------------------------------------------------------------------------------------------------------------------

    private void IslandGrisel(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Node3D root, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        IslandHomeListen();
        if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
        if (root.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == CrewRescueController.GriselId) is not { Dead: false } g) { Fail("no living Grisel on floor 12 (forgiven)"); return; }
        if (_isStep < 3 && rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        if (_isStep < 3) ThrowPitOnTheWay(main, rc, "island");
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {At(fell)}"); return; }
        switch (_isStep)
        {
            case 0:
            {
                if (AdventureDirector.Instance?.QuestGivenBy(CrewRescueController.GriselId) is not { } q) { Fail("forgiven, but Grisel offers no quest"); return; }
                AdventureDirector.Instance.Apply(new QuestAccepted(q.Id)); // her escort, as her dialogue's accept does
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate));
                rc.Restore(shape.LastRideStop);
                if (IslandSeatEveryCage(ctrl, shape) is { } bad) { Fail(bad); return; }
                GD.Print($"[island] grisel: quest {q.Id} {q.Status}, escorting {g.Escorting}; restored at stop {rc.StopIndex} with every cage's crew seated");
                if (q.Status != QuestStatus.Active || !g.Escorting) { Fail($"Grisel's escort did not start (quest {q.Status}, escorting {g.Escorting})"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 1 when _isClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 2:
            {
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the last ride was wrecked at S={rc.State.S:0.0}"); return; }
                var rear = IslandRearS(rc);
                if (rc.State.Mode != CartMode.Rolling || !(rear > enc.ExitToS + 0.5f)) { if (_isClock > 120f) Fail($"the train's rear never passed the exit bridge (at S={rear:0.0})"); break; }
                var spot = rc.StandBesideLane(rc.Party.Track.Point(1150f));
                var was = g.Seated ? $"seated in {(g.Ride as Node)?.Name}" : $"on foot at {At(g)}";
                if (g.Seated) g.GetOut(spot);
                else { g.GlobalPosition = spot; g.Velocity = Vector3.Zero; }
                _ihLast = g.GlobalPosition;
                GD.Print($"[island] grisel: with the train's rear at S={rear:0.0} Grisel ({was}) is left outside on ride {shape.Rides}'s lane at S 1150, {At(g)} (inside {Inside(layout, g)}, escorting {g.Escorting})");
                if (Inside(layout, g) || g.Seated || !g.Escorting) { Fail("Grisel was not left outside, on foot and escorting"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the train was wrecked at S={rc.State.S:0.0}"); return; }
                var arrived = rc.State.Mode == CartMode.Held && rc.StopIndex == shape.Rides && Inside(layout, me);
                if (arrived && _ihArriveT is float.NaN) _ihArriveT = _isTime;
                if (!_ihGriselLogged)
                {
                    if (Inside(layout, g)) { Fail($"Grisel stands inside at {At(g)} before the line"); return; }
                    if (g.GlobalPosition.DistanceTo(_ihLast) > 3f) { Fail($"Grisel was teleported from {DungeonBuilder.WorldToCell(_ihLast)} to {At(g)} before the line"); return; }
                    if (g.CatchUpHeld) _ihHeldSeen = true;
                    _ihLast = g.GlobalPosition;
                    if (!(_ihArriveT is float.NaN) && _isTime - _ihArriveT > 1f) { Fail($"1 s after the arrival with the lead inside Grisel is still outside at {At(g)}, not brought in"); return; }
                    if (_isClock > 90f) Fail($"the train did not arrive at stop {shape.Rides}");
                    break;
                }
                if (_ihArriveT is float.NaN) { Fail("Grisel was brought in before the arrival with the lead inside"); return; }
                var last = rc.Train.LastOrDefault(w => IsInstanceValid(w) && w.Coupled) as Node3D ?? rc.Cart;
                var d = new Vector2(g.GlobalPosition.X - last.GlobalPosition.X, g.GlobalPosition.Z - last.GlobalPosition.Z).Length();
                GD.Print($"[island] grisel: brought in {_isTime - _ihArriveT:0.00} s after the arrival at {At(g)}, {d:0.0} m from the train's last body {last.Name}; her catch-up held before it {_ihHeldSeen}");
                if (!Inside(layout, g) || !StandsOk(layout, g) || d > 6f) { Fail($"Grisel brought in at {At(g)} (inside {Inside(layout, g)}), {d:0.0} m from {last.Name}"); return; }
                if (!_ihHeldSeen) { Fail("Grisel's catch-up was never held while she was outside"); return; }
                // the party out at the head of the stairs (as --test-crew's forgiven branch): she follows them down there
                if (me.Cart == rc.Cart) rc.Cart.Interact(party, me);
                var stairs = root.GetNodeOrNull<Stairs>("DownStairs");
                if (stairs == null) { Fail("no down stairs"); return; }
                party.Teleport(stairs.GlobalTransform * new Vector3(0f, 0.3f, 1.2f), main.Builder.Surface(DungeonBuilder.WalkableNeighbor(layout, layout.DownStairs)));
                _ihLast = g.GlobalPosition;
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                // fix round 2 (item 3): set down on her lead's side she is no longer held (the side gate is read every frame)
                if (g.CatchUpHeld && _isClock > 0.2f) { Fail($"Grisel, brought in beside the party, is still held {_isClock:0.0} s on"); return; }
                // fix round 1 (M2): she walks there: never a jump of over 3 m in a frame (a catch-up)
                if (g.GlobalPosition.DistanceTo(_ihLast) > 3f) { Fail($"Grisel was teleported from {DungeonBuilder.WorldToCell(_ihLast)} to {At(g)} on her way to the stairs"); return; }
                _ihLast = g.GlobalPosition;
                var q = Game.Instance.Adventure.Quests.Values.FirstOrDefault(x => x.Floor == 12 && x.GiverNpc == CrewRescueController.GriselId);
                if (q?.Status != QuestStatus.Completed) { if (_isClock > 30f) Fail($"q_grisel_out is {q?.Status.ToString() ?? "missing"}: Grisel at {At(g)} (escorting {g.Escorting}, her catch-up held {g.CatchUpHeld}; the lead at {At(me)})"); break; }
                Pass($"Grisel, left outside on ride {shape.Rides}'s lane as the train passed the bridge, held there (never teleported, never inside, her catch-up held) until the arrival with the lead inside; brought in beside the train's last body within 1 s and \"{CrewRescueController.GriselInLine}\" logged; she walked with the party to the down stairs and {q.Id} completed {_isClock:0.0} s later");
                return;
            }
        }
    }

    // ---- =chest ---------------------------------------------------------------------------------------------------------------------

    private void IslandChest(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Node3D root, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        IslandHomeListen();
        if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
        if (_isStep < 3 && rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        if (_isStep < 3) ThrowPitOnTheWay(main, rc, "island");
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {At(fell)}"); return; }
        int Loot() => root.GetChildren().OfType<Items.LootDrop>().Count(l => !l.IsQueuedForDeletion());
        switch (_isStep)
        {
            case 0:
            {
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate));
                rc.Restore(shape.LastRideStop);
                if (IslandSeatEveryCage(ctrl, shape) is { } bad) { Fail(bad); return; }
                if (ctrl.RewardChest != null) { Fail("a reward chest stands before the win"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 1 when _isClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 2:
            {
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the last ride was wrecked at S={rc.State.S:0.0}"); return; }
                // fix round 2 (M7): with the train past the bridge, a companion is stood on the chest's cell before the win: the chest must not stand on it
                if (!ctrl.Completed && _ihOnChest == null && rc.State.Mode == CartMode.Rolling && IslandRearS(rc) > enc.ExitToS + 0.5f && ctrl.RewardChestCell() is { } chestCell
                    && party.Active.FirstOrDefault(a => a != me && a.Cart == rc.Cart && !a.Downed) is { } mate)
                {
                    _ihOnChest = mate;
                    mate.Teleport(main.Builder.Surface(chestCell) + Vector3.Up * 0.3f);
                    GD.Print($"[island] chest: {mate.State.Name} stood on the chest's cell {chestCell} before the win (at {At(mate)})");
                }
                if (!ctrl.Completed) { if (ctrl.RewardChest != null) { Fail("the reward chest stands before the win"); return; } if (_isClock > 150f) Fail($"no win (stop {rc.StopIndex}, {IslandCrewStates(ctrl)})"); break; }
                if (_ihT is float.NaN) _ihT = _isTime;
                var chest = ctrl.RewardChest;
                var vault = layout.Vaults.FirstOrDefault(v => v.Index == layout.Objective!.RewardVault);
                var door = vault == null ? null : root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == vault.Index);
                var problems = new List<string>();
                if (chest == null) problems.Add("no reward chest");
                else
                {
                    var cell = DungeonBuilder.WorldToCell(chest.GlobalPosition);
                    if (!layout.Rooms[9].Contains(cell)) problems.Add($"the chest at {cell} is not in room 9");
                    if (chest.TableId != ChestRules.VaultTable || chest.SpawnIndex != CrewRules.RewardChestIndex || chest.IsMimic || chest.Locked || chest.Opened) problems.Add($"the chest: table {chest.TableId}, index {chest.SpawnIndex}, mimic {chest.IsMimic}, locked {chest.Locked}, opened {chest.Opened}");
                }
                if (!_ihChestLogged) problems.Add("its line not logged");
                if (_ihOnChest == null) problems.Add("no companion was stood on the chest's cell");
                else if (chest != null && DungeonBuilder.WorldToCell(_ihOnChest.GlobalPosition) == DungeonBuilder.WorldToCell(chest.GlobalPosition)) problems.Add($"{_ihOnChest.State.Name} still stands on the chest's cell");
                if (door is not { Detected: true, IsOpen: true } || vault == null || layout.Rooms.IndexOf(vault.Room) != 10) problems.Add($"room 10's door: vault {vault?.Index} in room {(vault == null ? -1 : layout.Rooms.IndexOf(vault.Room))}, revealed {door?.Detected}, open {door?.IsOpen}");
                if (problems.Count > 0) { if (_isTime - _ihT > 1f) Fail($"1 s after the win: {string.Join("; ", problems)}"); break; }
                GD.Print($"[island] chest: {_ihOnChest!.State.Name} stepped off its cell to {At(_ihOnChest)}");
                GD.Print($"[island] chest: {_isTime - _ihT:0.00} s after the win the reward chest stands in room 9 at {At(chest!)} ({chest!.GlobalPosition.DistanceTo(main.Builder.Surface(layout.DownStairs)):0.0} m from the stairs at {layout.DownStairs}), table {chest.TableId}, its line logged; room 10's door revealed and open");
                if (me.Cart == rc.Cart) rc.Cart.Interact(party, me);
                var here = DungeonBuilder.WorldToCell(chest.GlobalPosition);
                var beside = CrevasseRules.SetDownCells(layout, here, 25).FirstOrDefault(c => c != here && CrevasseRules.SetDownOk(layout, c) && !layout.IsRail(c) && !main.Builder.IsCellBlocked(c));
                me.Teleport(main.Builder.Surface(beside) + Vector3.Up * 0.3f, chest.GlobalPosition);
                _ihLootBefore = Loot();
                chest.Interact(party, me);
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                if (_isClock < 0.5f) break;
                var chest = ctrl.RewardChest!;
                var drops = Loot() - _ihLootBefore;
                GD.Print($"[island] chest: the lead opened it: opened {chest.Opened}, {drops} drop(s) on the floor");
                if (!chest.Opened || drops < 1) { Fail($"the lead's open: opened {chest.Opened}, {drops} drop(s)"); return; }
                _isRc = rc;
                main.SaveGame(IslandSlot);
                if (!main.LoadGame(IslandSlot)) { Fail("the save after the open would not load"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                if (rc == _isRc) { if (_isClock > 10f) Fail("the floor was not built again within 10 s of the load"); break; }
                if (_isClock < 1f) break;
                if (ctrl.RewardChest is not { Opened: true } chest) { Fail($"after the load the reward chest {(ctrl.RewardChest == null ? "is gone" : "stands closed")}"); return; }
                if (!ctrl.Completed) { Fail("after the load the floor is not completed"); return; }
                if (layout.SafeRoom < 0 || SafeRoomController.Instance is not { } safe) { Fail("floor 12 has no safe room"); return; }
                var room = layout.Rooms[layout.SafeRoom];
                var gather = SafeRoomRules.GatherCell(layout);
                GD.Print($"[island] chest: after the load the reward chest stands at {At(chest)}, opened; the safe room is room {layout.SafeRoom} (in the island {layout.InIsland(room.Center)}), its gather cell {gather} (in the island {(gather is { } gc && layout.InIsland(gc))})");
                if (gather is not { } cell || !layout.InIsland(cell)) { Fail($"the safe room's gather cell {gather} is not inside the island"); return; }
                party.Teleport(main.Builder.Surface(room.Center) + Vector3.Up * 0.3f);
                _ihGatherBefore = safe.Gathered;
                _ihSpareGather = true;
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            {
                if (SafeRoomController.Instance is not { } safe) { Fail("the safe room's controller is gone"); return; }
                if (_isClock > 1f && !safe.Occupied) { Fail($"the party is not in the safe room (lead at {At(me)})"); return; }
                if (_isClock < 40f) break;
                var gather = SafeRoomRules.GatherCell(layout)!.Value;
                var creatures = root.GetChildren().OfType<Enemies.Enemy>().Where(e => e.IsAlive && DungeonBuilder.WorldToCell(e.GlobalPosition).ManhattanDistance(gather) <= 2).ToList();
                GD.Print($"[island] chest: 40 s in the safe room: groups {safe.Gathered} (from {_ihGatherBefore}), creatures {safe.GatheredCreatures}; at the gather cell {gather} [{string.Join(", ", creatures.Select(e => $"{e.Name} {At(e)} inside {Inside(layout, e)}"))}]");
                if (safe.Gathered <= _ihGatherBefore || creatures.Count == 0 || creatures.Any(e => !Inside(layout, e))) { Fail("no group gathered inside the island in 40 s"); return; }
                _ihSpareGather = false;
                Pass($"within 1 s of the win the reward chest stood in room 9 (table.vault, index {CrewRules.RewardChestIndex}) and its line was logged; room 10's door revealed and open; the lead opened it and an item dropped; a save and a load kept it, opened; 40 s in the safe room a group of {creatures.Count} gathered at {gather}, inside the island");
                return;
            }
        }
    }
}
