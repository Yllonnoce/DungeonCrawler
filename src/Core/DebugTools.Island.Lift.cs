using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-island=lift|takeover (plan 15, Task 6) ---------------------------------------------------------------------------------------
// Ride 5's parked train as the lift (spec B.5, B.8; Rulings 25-31, 35, 41), under --test-island's conventions (solo, lethality 3, every enemy
// culled, Shape(rc) for every stop and id, _quitAfter 300).
// =lift: first (plan 15 Task 8, Task 6's ⚠️6) ClearTheLiftsSpan end to end: every spare spent, the last ride ridden from its stop through the parked
// train's smash and wrecked by the test with the train's front in the parked train's span (S ParkedRearS..ExitFromS): a no-spare plain wreck, no
// spare spent, not inside; within 1 s every party wagon is uncoupled (hidden, not solid), no party body shown in the span and the parked train
// stands as a lift. Then rc.Restore(Shape(rc).LastRideStop) and the last ride ridden to its end (the pit thrown on the way: the parked train smashed, a lift again).
// Held at the last stop: the lead and one companion are put beside the parked train, a second companion 40 m short of it (no longer holding: it
// regroups), and stop 1's first crew member put on foot 10 m short of its cart (ctrl.DebugPutOnFoot). The prompt: "Board the cart" from its W4 and
// from its cart alike. The press (its cart's): the lead and the near companion in its cart, the crew member in its W1 seat 4 at once and not Home
// there, the far one on foot. The countdown: 5 s, Get out closed for its first 2 s. Getting out in it (Task 8, Task 6's ⚠️5): on the first frame
// Get out is offered the lead presses it: set down outside with nobody left in the lift, which stands on at its place (no run); the lead boards
// again by whichever of its bodies offers "Board the cart", and the countdown is judged again from that press. The bridge: while the lift is over it the lead's prompt is ""
// and a forced Get out does nothing. The save (Review Focus 4) at mid-bridge and a load: the lead beside the parked train outside, up and not
// fallen, the lift standing; the lead boards again. The halt: 1.5 +- 1.5 m behind the held train's last body, inside, every rider on island cells;
// hidden 2 s later and standing again 1 s after that. Regroup (Ruling 35): the far companion on an island cell that may take a body within 10 s.
// =takeover (Review Focus 1): rc.Restore(2) with every spare spent (rc.DebugSpendSpares), ridden out and wrecked on ride 3 (rc.DebugWreck: "No
// carts left. The rest is on foot."); the party put beside the parked train; stop 4's holders are spared by the cull (its gate stays down) and its
// cage stays locked. Refused while a chaser stands (rc.DebugPlaceChaser, then rc.DebugEndChase). The double press (Review Focus 5) in one tick:
// one takeover, both in the party's cart, nobody in the lift. The new cart: Held at the lift's cart S, W1..W4 coupled fresh, StopIndex the last
// ride's stop, StandingPast; ParkedNow Gone from the press to the arrival (the latch), TakenOver cleared on it. The countdown starts within 1 s
// though stop 4's gate is down and its cage locked, no stop left behind. The departure arms the last ride from its place with the golem, the
// hounds and the pit passed; it arrives at the last stop as the party's train.
public partial class DebugTools
{
    private Adventurer? _ilNear, _ilFar;
    private string? _ilCrewId;
    private float _ilPressT = float.NaN, _ilRollT = float.NaN, _ilHaltT = float.NaN, _ilHideT = float.NaN, _ilSetDownT = float.NaN;
    private bool _ilGetOutClosedSeen, _ilGetOutOpenSeen, _ilBridgeSeen, _ilSaved, _ilRefused, _ilNoCartLogged;
    private int _ilBridgeFrames, _ilLiftsBefore, _ilGoneFrames;
    private float _ilWreckS = float.NaN;
    private bool _ilListening;
    /// <summary>Task 8: the lead got out during the countdown (once); the parked smashes before the ClearTheLiftsSpan ride, its wreck's time, and the wagons it wrecked.</summary>
    private bool _ilOutDone;
    private int _ilSmashesBefore;
    private float _ilSpanWreckT = float.NaN;
    private readonly List<TrainWagon> _ilSpanWagons = new();
    private readonly List<Node3D> _ilRiders = new();

    private void IslandLiftListen()
    {
        if (_ilListening) return;
        _ilListening = true;
        EventBus.Instance.Message += text =>
        {
            if (!_done && _testIsland && text == "No carts left. The rest is on foot.") _ilNoCartLogged = true;
        };
    }

    /// <summary>
    /// Where a body on foot stands in reach of the lift's <paramref name="tub"/> (its cart or a wagon): the nearest walkable cell round it, off the rails
    /// and props, outside the island, whose floor point is within <see cref="RideInputRules.BoardReach"/> of the tub; else the lane just ahead of the
    /// lift's cart front, short of the bridge (the parked train stands by the lava, with few cells beside it).
    /// </summary>
    internal static Vector3 BesideLift(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, ICartBody tub)
    {
        var at = ((Node3D)tub).GlobalPosition;
        float Tub(Vector3 p) => tub is TrainWagon w ? w.TubDistance(p) : ((MineCart)tub).TubDistance(p);
        float Reach(Vector3 p) => Mathf.Min(Tub(p + Vector3.Up * 0.3f), Tub(p + Vector3.Up * 1.6f)); // its body and its eye, as the reach is judged
        var here = DungeonBuilder.WorldToCell(at);
        Vector3? best = null;
        var bestD = float.MaxValue;
        var lane = rc.StandBesideLane(at) - Vector3.Up * 0.1f; // the lane's own exit cell beside it, a candidate too
        if (!layout.InIsland(DungeonBuilder.WorldToCell(lane))) { best = lane; bestD = Reach(lane); }
        for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                if (!CrevasseRules.SetDownOk(layout, c) || layout.IsRail(c) || layout.InIsland(c) || main.Builder.IsCellBlocked(c)) continue;
                var p = main.Builder.Surface(c);
                var d = Reach(p);
                if (d < bestD) { bestD = d; best = p; }
            }
        if (best is { } spot && bestD <= RideInputRules.BoardReach - 0.05f) return spot + Vector3.Up * 0.3f;
        var ahead = rc.Party.Track.Point(ParkedRules.FrontS(enc.ExitFromS) + 0.5f); // on the lane between its cart's front and the bridge's near edge
        return ahead + Vector3.Up * 0.3f;
    }

    private static string Who(Node3D b) => b is Adventurer h ? h.State.Name : b is Npc n ? n.NpcName : b.Name;

    // ---- =lift -------------------------------------------------------------------------------------------------------------------

    private void IslandLift(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        if (enc.Parked is not { } train) { Fail("floor 12 has no live parked train"); return; }
        if (_isStep < 3 && rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        ThrowPitOnTheWay(main, rc, "island");
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {DungeonBuilder.WorldToCell(fell.GlobalPosition)}"); return; }
        switch (_isStep)
        {
            case 0:
            {
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate));
                rc.Restore(shape.LastRideStop);
                var cage = ctrl.Cages.FirstOrDefault(c => c.Stop == 1);
                _ilCrewId = ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && !n.Dead && cage != null && n.Ride == cage)?.Id;
                if (_ilCrewId == null) { Fail("no crew member in stop 1's cage"); return; }
                GD.Print($"[island] lift: restored at stop {rc.StopIndex}; {_ilCrewId} (stop 1's cage) will be put on foot by the parked train");
                // Task 8 (Task 6's ⚠️6): first ClearTheLiftsSpan end to end, with every spare spent (steps 20-22), then the lift's own steps from step 1
                rc.DebugSpendSpares();
                _ilSmashesBefore = enc.ParkedSmashes;
                _isStep = 20; _isClock = 0f;
                break;
            }
            case 20 when _isClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 21:
            {
                // ride the last ride through the parked train's smash; the test wrecks it with the train's front in the parked train's span, short of the bridge
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the last ride was wrecked at S={rc.State.S:0.0} before the test's wreck"); return; }
                var front = rc.Party.FrontS;
                if (rc.State.Mode != CartMode.Rolling || enc.ParkedSmashes <= _ilSmashesBefore || front < enc.ParkedRearS + 0.5f)
                {
                    if (front > enc.ExitFromS - 0.5f || _isClock > 120f) Fail($"the train never stood in the parked train's span past its smash (front S={front:0.0}, smashes {enc.ParkedSmashes - _ilSmashesBefore}, its rear face {enc.ParkedRearS:0.0})");
                    break;
                }
                if (front > enc.ExitFromS - 0.5f) { Fail($"the front passed S={front:0.0}, too near the bridge's start {enc.ExitFromS:0.0} for a plain wreck in the span"); return; }
                var used = rc.SparesUsed.Count;
                _ilSpanWagons.Clear();
                _ilSpanWagons.AddRange(rc.Train);
                var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing);
                rc.DebugWreck();
                _ilSpanWreckT = _isTime;
                GD.Print($"[island] lift: span: the test wrecked the train with its front at S={front:0.0}, its rear at S={rear:0.0}, in the parked train's span S={enc.ParkedRearS:0.0}..{enc.ParkedFrontS:0.0} (the bridge from {enc.ExitFromS:0.0}); spares left {rc.SparesLeft}, used {used} -> {rc.SparesUsed.Count}; W[{string.Join(",", _ilSpanWagons.Select(w => w.Number))}]");
                _isStep++; _isClock = 0f;
                break;
            }
            case 22:
            {
                // within 1 s: a plain wreck (no spare, not inside), every party wagon uncoupled (hidden, not solid), no party body shown in the span, the lift standing
                var problems = new List<string>();
                if (rc.State.Mode != CartMode.Wrecked || rc.LastWreckInside) problems.Add($"the cart is {rc.State.Mode}, inside {rc.LastWreckInside} (want a plain wreck)");
                if (rc.SparesLeft != 0) problems.Add($"{rc.SparesLeft} spare(s) left");
                if (rc.Train.Count != 0) problems.Add($"the train still has W[{string.Join(",", rc.Train.Select(w => w.Number))}] coupled");
                foreach (var w in _ilSpanWagons) if (IsInstanceValid(w) && (w.Coupled || w.Visible || w.CollisionLayer != 0)) problems.Add($"W{w.Number} coupled={w.Coupled} visible={w.Visible} layer={w.CollisionLayer}");
                foreach (var body in _ilSpanWagons.Cast<Node3D>().Prepend(rc.Cart))
                    if (IsInstanceValid(body) && body.Visible && rc.Party.Track.ClosestS(body.GlobalPosition) is var bs && bs > enc.ParkedRearS - CartRules.HalfLength && bs < enc.ParkedFrontS + CartRules.HalfLength)
                        problems.Add($"the party's {body.Name} is shown within the parked train's span (S={bs:0.0})");
                if (enc.ParkedNow != ParkedState.Lift || !train.Standing) problems.Add($"the parked train is {enc.ParkedNow}, standing={train.Standing}");
                if (problems.Count > 0) { if (_isClock > 1f) Fail($"1 s after the no-spare wreck in the parked train's span: {string.Join("; ", problems)}"); break; }
                GD.Print($"[island] lift: span: {_isTime - _ilSpanWreckT:0.00} s after the no-spare plain wreck the party's W[{string.Join(",", _ilSpanWagons.Select(w => w.Number))}] stand uncoupled (hidden, not solid), no party body shown in S={enc.ParkedRearS:0.0}..{enc.ParkedFrontS:0.0}, and the parked train stands as a lift ({enc.ParkedNow}, standing)");
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate));
                rc.Restore(shape.LastRideStop);
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && !a.State.Fallen && !a.Downed) a.State.FullRestore(); // the wreck's knocks off: the lift's steps want two companions up
                if (party.Active.Any(a => a.Downed)) { Fail($"after the wreck {string.Join(", ", party.Active.Where(a => a.Downed).Select(a => a.State.Name))} lie down"); return; }
                _isStep = 1; _isClock = 0f;
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
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides) { if (_isClock > 120f) Fail($"the train did not arrive at stop {shape.Rides} (stop {rc.StopIndex}, mode {rc.State.Mode})"); break; }
                if (enc.ParkedNow != ParkedState.Lift) { if (_isClock > 3f) Fail($"held at the last stop the parked train is {enc.ParkedNow}, not a lift"); break; }
                if (_isClock < 1f) break;
                if (ObjectiveController.Instance is not CrewRescueController ctrl || ctrl.Crew.FirstOrDefault(n => n.Id == _ilCrewId) is not { } crew) { Fail("the crew member is gone"); return; }
                var mates = party.Active.Where(a => a != me && !a.Downed).ToList();
                if (mates.Count < 2) { Fail($"only {mates.Count} companion(s) up"); return; }
                _ilNear = mates[0];
                _ilFar = mates[1];
                _ilFar.Stance = Characters.Stance.Defensive; // it follows (and regroups) again: Ruling 35's "nobody stranded"
                // the prompt from W4 and from its cart (Ruling 28), the lead on foot beside each in turn
                var w4 = train.Wagons[^1];
                me.Teleport(BesideLift(main, layout, rc, enc, w4), w4.GlobalPosition + Vector3.Up * 0.9f);
                var fromW4 = w4.Prompt;
                GD.Print($"[island] lift: the lead beside its W4 at {DungeonBuilder.WorldToCell(me.GlobalPosition)} ({w4.TubDistance(me):0.0} m from its tub): \"{fromW4}\"");
                var beside = BesideLift(main, layout, rc, enc, train.Cart);
                me.Teleport(beside, train.Cart.GlobalPosition + Vector3.Up * 0.9f);
                var fromCart = train.Cart.Prompt;
                GD.Print($"[island] lift: the lead beside its cart at {DungeonBuilder.WorldToCell(beside)} ({train.Cart.TubDistance(me):0.0} m from its tub)");
                GD.Print($"[island] lift: the prompt: from its W4 \"{fromW4}\", from its cart \"{fromCart}\"");
                if (fromW4 != ParkedRules.BoardPrompt || fromCart != ParkedRules.BoardPrompt) { Fail($"the lift's W4 offers \"{fromW4}\" and its cart \"{fromCart}\", not \"{ParkedRules.BoardPrompt}\""); return; }
                _ilNear.Teleport(rc.StandBesideLane(train.Wagons[0].GlobalPosition), train.Cart.GlobalPosition);
                var farAt = rc.StandBesideLane(rc.Party.Track.Point(train.CartS - 40f));
                _ilFar.Teleport(farAt, train.Cart.GlobalPosition);
                ctrl.DebugPutOnFoot(crew.Id, rc.StandBesideLane(rc.Party.Track.Point(train.CartS - 10f)));
                GD.Print($"[island] lift: the lead and {_ilNear.State.Name} beside it, {_ilFar.State.Name} 40 m short at {DungeonBuilder.WorldToCell(farAt)}, {crew.NpcName} on foot {crew.GlobalPosition.DistanceTo(train.Cart.GlobalPosition):0.0} m from its cart");
                train.Cart.Interact(party, me); // the key's press on its cart
                _ilPressT = _isTime;
                var problems = new List<string>();
                if (me.Cart != train.Cart) problems.Add($"the lead sits in {(me.Cart as Node)?.Name ?? "nothing"}");
                if (_ilNear.Cart != train.Cart) problems.Add($"{_ilNear.State.Name} (near) sits in {(_ilNear.Cart as Node)?.Name ?? "nothing"}");
                if (_ilFar.Seated) problems.Add($"{_ilFar.State.Name} (40 m short) was seated");
                if (crew.Cart != train.Wagons[0] || crew.Seat != TrainRules.CrewSeats[0]) problems.Add($"{crew.NpcName} sits in {(crew.Cart as Node)?.Name ?? "nothing"} seat {crew.Seat}, not the lift's W1 seat 4");
                if (ctrl.StateOf(crew) == CrewState.Home) problems.Add($"{crew.NpcName} is Home in the lift");
                if (rc.Lift == null) problems.Add("no lift runs");
                if (problems.Count > 0) { Fail($"the press: {string.Join("; ", problems)}"); return; }
                GD.Print($"[island] lift: the press seated the lead and {_ilNear.State.Name} in its cart and {crew.NpcName} in its W1 seat {crew.Seat} ({ctrl.StateOf(crew)}); {_ilFar.State.Name} stays on foot");
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                // the countdown: 5 s, Get out closed for its first 2 s
                if (rc.Lift == null) { Fail("the lift went before it rolled"); return; }
                var since = _isTime - _ilPressT;
                var prompt = train.Cart.Prompt;
                if (rc.State.Mode == CartMode.Held && rc.LiftS < ParkedRules.CartS(enc.ExitFromS) + 0.01f && rc.ChaserBoardingLeft >= 0f)
                {
                    if (since < 1.9f) { if (prompt != "") { Fail($"{since:0.00} s into the countdown the lead is offered \"{prompt}\""); return; } _ilGetOutClosedSeen = true; }
                    if (since > 2.1f) { if (prompt != "Get out") { Fail($"{since:0.00} s into the countdown the lead is offered \"{prompt}\", not \"Get out\""); return; } _ilGetOutOpenSeen = true; }
                }
                if (_ilGetOutOpenSeen && !_ilOutDone && IslandLiftGetOut(main, layout, rc, enc, train, party, me, since, Fail)) break;
                if (float.IsNaN(rc.LiftS) || rc.LiftS <= ParkedRules.CartS(enc.ExitFromS) + 0.01f) { if (since > 8f) Fail($"the lift did not roll in 8 s (left {rc.ChaserBoardingLeft:0.0})"); break; }
                _ilRollT = _isTime;
                GD.Print($"[island] lift: it rolled {_ilRollT - _ilPressT:0.00} s after the press (Get out closed seen {_ilGetOutClosedSeen}, open after 2 s {_ilGetOutOpenSeen}); {enc.ParkedNow}");
                if (_ilRollT - _ilPressT < 4.8f || _ilRollT - _ilPressT > 5.4f || !_ilGetOutClosedSeen || !_ilGetOutOpenSeen) { Fail($"the countdown ran {_ilRollT - _ilPressT:0.00} s (want 5), Get out closed for 2 s {_ilGetOutClosedSeen}, open after {_ilGetOutOpenSeen}"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                // the bridge: no Get out while any body of the lift is over it; the save at mid-bridge
                if (rc.Lift == null) { Fail("the lift went before the bridge"); return; }
                if (rc.LiftHoldsRiders)
                {
                    _ilBridgeFrames++;
                    if (train.Cart.Prompt != "") { Fail($"over the bridge (S={rc.LiftS:0.0}) the lead is offered \"{train.Cart.Prompt}\""); return; }
                    if (!_ilBridgeSeen)
                    {
                        _ilBridgeSeen = true;
                        train.Cart.Interact(party, me); // a forced Get out
                        if (me.Cart != train.Cart) { Fail("over the bridge a forced Get out let the lead out"); return; }
                    }
                    if (rc.LiftS >= (enc.ExitFromS + enc.ExitToS) / 2f)
                    {
                        GD.Print($"[island] lift: {_ilBridgeFrames} frames over the bridge held (a forced Get out did nothing); saved at mid-bridge, S={rc.LiftS:0.0}");
                        _isRc = rc;
                        _ilSaved = true;
                        main.SaveGame(IslandSlot);
                        if (!main.LoadGame(IslandSlot)) { Fail("the save at mid-bridge would not load"); return; }
                        _isStep++; _isClock = 0f;
                        break;
                    }
                }
                if (_isClock > 20f) Fail($"the lift never reached mid-bridge (S={rc.LiftS:0.0}, held frames {_ilBridgeFrames})");
                break;
            }
            case 5:
            {
                if (rc == _isRc) { if (_isClock > 10f) Fail("the floor was not built again within 10 s of the load"); break; }
                if (_isClock < 1.5f) break; // the load's deferred set-down, and the lift standing on its first tick
                var cell = DungeonBuilder.WorldToCell(me.GlobalPosition);
                var problems = new List<string>();
                if (me.Seated || me.Downed || me.State.Fallen || layout.InIsland(cell) || !CrevasseRules.SetDownOk(layout, cell)) problems.Add($"the lead at {cell} seated={me.Seated} downed={me.Downed} fallen={me.State.Fallen} island={layout.InIsland(cell)}");
                if (me.GlobalPosition.DistanceTo(train.Cart.GlobalPosition) > 6f) problems.Add($"the lead stands {me.GlobalPosition.DistanceTo(train.Cart.GlobalPosition):0.0} m from the parked train's cart");
                if (enc.ParkedNow != ParkedState.Lift || !train.Standing) problems.Add($"the parked train is {enc.ParkedNow}, standing={train.Standing}");
                if (problems.Count > 0) { Fail($"after the load: {string.Join("; ", problems)}"); return; }
                _ilNear = party.Active.FirstOrDefault(a => a.State.Name == _ilNear?.State.Name);
                _ilFar = party.Active.FirstOrDefault(a => a.State.Name == _ilFar?.State.Name);
                GD.Print($"[island] lift: after the load the lead stands beside the parked train outside at {cell} ({me.GlobalPosition.DistanceTo(train.Cart.GlobalPosition):0.0} m from its cart), up; {_ilNear?.State.Name} at {(_ilNear == null ? "?" : DungeonBuilder.WorldToCell(_ilNear.GlobalPosition).ToString())}; the lift {enc.ParkedNow}, the cart {rc.State.Mode} at stop {rc.StopIndex}");
                _ilLiftsBefore = rc.LiftsDone;
                if (_ilFar == null) { Fail("the far companion is gone after the load"); return; }
                _ilFar.Teleport(rc.StandBesideLane(rc.Party.Track.Point(train.CartS - 40f)), train.Cart.GlobalPosition); // 40 m short again (it followed the lead since the load): it must regroup, not ride
                // the lead boards again, pressing whichever of its five bodies offers it "Board the cart" from where the load stood it (Ruling 28)
                var bodies = new List<ICartBody> { train.Cart };
                bodies.AddRange(train.Wagons);
                var offering = bodies.FirstOrDefault(b => b is MineCart m ? m.Prompt == ParkedRules.BoardPrompt : ((TrainWagon)b).Prompt == ParkedRules.BoardPrompt);
                if (offering == null) { Fail($"after the load none of the lift's bodies offers the lead \"{ParkedRules.BoardPrompt}\" (its cart {train.Cart.TubDistance(me):0.0} m, its W4 {train.Wagons[^1].TubDistance(me):0.0} m)"); return; }
                var offer = (offering as Node)?.Name.ToString() ?? "?";
                if (offering is MineCart pressCart) pressCart.Interact(party, me); else ((TrainWagon)offering).Interact(party, me);
                if (me.Cart != train.Cart) { Fail($"after the load the lead pressed {offer} and was not seated in the lift's cart"); return; }
                var book = new List<ICartRider>();
                train.CollectRiders(book);
                _ilRiders.Clear();
                _ilRiders.AddRange(book.OfType<Node3D>());
                GD.Print($"[island] lift: the lead boarded again by its {offer}: [{string.Join(", ", _ilRiders.Select(Who))}] aboard");
                _isStep++; _isClock = 0f;
                break;
            }
            case 6:
            {
                if (rc.LiftsDone == _ilLiftsBefore) { if (_isClock > 40f) Fail($"the lift never halted (S={rc.LiftS:0.0})"); break; }
                _ilHaltT = _isTime;
                var problems = new List<string>();
                if (!(rc.LastLiftGap >= 0f && rc.LastLiftGap <= 3f)) problems.Add($"it halted {rc.LastLiftGap:0.00} m behind the held train's last body (want 1.5 +- 1.5)");
                if (rc.LastLiftHaltS - CartRules.HalfLength - ParkedRules.Wagons * TrainRules.Spacing <= enc.ExitToS) problems.Add($"its rear stands at S={rc.LastLiftHaltS - CartRules.HalfLength - ParkedRules.Wagons * TrainRules.Spacing:0.0}, not past the far edge {enc.ExitToS:0.0}");
                var riders = _ilRiders.Where(b => IsInstanceValid(b)).ToList();
                if (riders.Count != rc.LastLiftSetDown) problems.Add($"{rc.LastLiftSetDown} rider(s) set down, {riders.Count} rode");
                foreach (var b in riders)
                {
                    var c = DungeonBuilder.WorldToCell(b.GlobalPosition);
                    var seated = b is Adventurer h ? h.Seated : b is Npc n && n.Seated;
                    if (seated || !layout.InIsland(c) || !CrevasseRules.SetDownOk(layout, c)) problems.Add($"{Who(b)} at {c} seated={seated} island={layout.InIsland(c)}");
                }
                if (problems.Count > 0) { Fail($"the halt: {string.Join("; ", problems)}"); return; }
                _ilSetDownT = _isTime;
                GD.Print($"[island] lift: halted at S={rc.LastLiftHaltS:0.0}, {rc.LastLiftGap:0.00} m behind the held train's last body; {rc.LastLiftSetDown} rider(s) set down inside: {string.Join(", ", riders.Select(b => $"{Who(b)} {DungeonBuilder.WorldToCell(b.GlobalPosition)}"))}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 7:
            {
                // hidden 2 s after the halt, standing again 1 s after that; the far companion regroups inside within 10 s of the lead's set-down
                if (float.IsNaN(_ilHideT) && rc.Lift == null)
                {
                    _ilHideT = _isTime;
                    var after = _ilHideT - _ilHaltT;
                    GD.Print($"[island] lift: hidden {after:0.00} s after the halt");
                    if (after < SpareRules.HideSeconds - 0.2f || after > SpareRules.HideSeconds + 0.3f) { Fail($"the lift was hidden {after:0.00} s after the halt, not {SpareRules.HideSeconds:0} s"); return; }
                }
                var standing = !float.IsNaN(_ilHideT) && enc.ParkedNow == ParkedState.Lift && train.Standing && train.Cart.GlobalPosition.DistanceTo(rc.Party.Track.Point(ParkedRules.CartS(enc.ExitFromS))) < 1f;
                if (!float.IsNaN(_ilHideT) && !standing && _isTime - _ilHideT > 1f) { Fail($"{_isTime - _ilHideT:0.00} s after it was hidden it is {enc.ParkedNow}, standing={train.Standing}"); return; }
                var far = _ilFar;
                var farCell = far != null ? DungeonBuilder.WorldToCell(far.GlobalPosition) : default;
                var regrouped = far != null && layout.InIsland(farCell) && CrevasseRules.SetDownOk(layout, farCell) && !far.State.Fallen;
                if (far != null && _ilRiders.Contains(far)) { Fail($"{far.State.Name}, put 40 m short, rode the lift in: Regroup is not tested"); return; }
                if (!regrouped && _isTime - _ilSetDownT > 10f) { Fail($"{far?.State.Name} (40 m short) did not regroup inside within 10 s of the lead's set-down (at {farCell}, fallen {far?.State.Fallen})"); return; }
                if (!standing || !regrouped) { if (_isClock > 15f) Fail("the lift never stood again or the far companion never regrouped"); break; }
                GD.Print($"[island] lift: standing again {_isTime - _ilHideT:0.00} s or less after it was hidden; {far!.State.Name} regrouped onto {farCell} inside");
                Pass($"a no-spare plain wreck in the parked train's span uncoupled the party's wagons and the lift stood (ClearTheLiftsSpan); the prompt from its W4 and its cart; the press seated the lead and a companion in its cart and a crew member on foot in its W1 seat 4 at once (not Home there), the far companion left on foot; the lead got out in the countdown (set down outside, the lift standing on, no run) and boarded again; a 5 s countdown with Get out closed for its first 2 s; over the bridge no prompt and a forced Get out did nothing; a save at mid-bridge loaded the lead beside the parked train outside with the lift standing, and the lead boarded again; it halted {rc.LastLiftGap:0.00} m behind the held train's last body inside, every rider set down on island cells; hidden 2 s later and standing again within 1 s; the far companion regrouped inside");
                return;
            }
        }
    }

    /// <summary>
    /// =lift step 3 (Task 8, Task 6's ⚠️5): once, on the first frame Get out is offered in the countdown, the lead presses it: set down outside, nobody
    /// left in the lift and no run, which stands on at its place as a lift; then the lead boards again by whichever of its bodies offers "Board the cart"
    /// (beside its cart first if none does), and the countdown is judged again from that press. True when the step should wait a frame (or failed).
    /// </summary>
    private bool IslandLiftGetOut(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, ParkedTrain train, PartyController party, Adventurer me, float since, Action<string> Fail)
    {
        _ilOutDone = true;
        train.Cart.Interact(party, me); // its own rider's "Get out", 2 s or more into the countdown
        var cell = DungeonBuilder.WorldToCell(me.GlobalPosition);
        var place = rc.Party.Track.Point(ParkedRules.CartS(enc.ExitFromS));
        var anyone = train.Cart.Riders.Count + train.Wagons.Sum(w => w.Riders.Count);
        var problems = new List<string>();
        if (me.Seated) problems.Add($"the lead still sits in {(me.Cart as Node)?.Name}");
        if (layout.InIsland(cell) || !CrevasseRules.SetDownOk(layout, cell)) problems.Add($"the lead was set down at {cell} (island {layout.InIsland(cell)}, may take a body {CrevasseRules.SetDownOk(layout, cell)})");
        if (rc.Lift != null) problems.Add($"the lift still runs at S={rc.LiftS:0.0}");
        if (anyone > 0) problems.Add($"{anyone} still seated in it");
        if (enc.ParkedNow != ParkedState.Lift || !train.Standing || train.Cart.GlobalPosition.DistanceTo(place) > 0.5f) problems.Add($"the parked train is {enc.ParkedNow}, standing={train.Standing}, its cart {train.Cart.GlobalPosition.DistanceTo(place):0.00} m from its place");
        if (problems.Count > 0) { Fail($"the Get out {since:0.00} s into the countdown: {string.Join("; ", problems)}"); return true; }
        GD.Print($"[island] lift: the lead got out {since:0.00} s into the countdown: set down outside at {cell}, nobody left in the lift and no run; it stands on at its place ({enc.ParkedNow})");
        var bodies = new List<ICartBody>(train.Wagons) { train.Cart };
        bool Offers(ICartBody b) => (b is MineCart m ? m.Prompt : ((TrainWagon)b).Prompt) == ParkedRules.BoardPrompt;
        if (!bodies.Any(Offers)) me.Teleport(BesideLift(main, layout, rc, enc, train.Cart), train.Cart.GlobalPosition + Vector3.Up * 0.9f);
        if (bodies.FirstOrDefault(Offers) is not { } offering) { Fail($"after the Get out none of the lift's bodies offers the lead \"{ParkedRules.BoardPrompt}\""); return true; }
        var name = (offering as Node)?.Name.ToString() ?? "?";
        if (offering is MineCart pressCart) pressCart.Interact(party, me); else ((TrainWagon)offering).Interact(party, me);
        if (me.Cart != train.Cart || rc.Lift == null || rc.ChaserBoardingLeft < 0f) { Fail($"after the Get out the lead pressed its {name}: seated in the lift's cart {me.Cart == train.Cart}, the lift runs {rc.Lift != null}, its countdown {rc.ChaserBoardingLeft:0.0}"); return true; }
        var book = new List<ICartRider>();
        train.CollectRiders(book);
        GD.Print($"[island] lift: the lead boarded again by its {name}: [{string.Join(", ", book.OfType<Node3D>().Select(Who))}] aboard; the countdown starts again");
        _ilPressT = _isTime;
        _ilGetOutClosedSeen = _ilGetOutOpenSeen = false;
        return true;
    }

    // ---- =takeover ---------------------------------------------------------------------------------------------------------------

    private void IslandTakeover(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        IslandLiftListen();
        if (enc.Parked is not { } train) { Fail("floor 12 has no live parked train"); return; }
        if (rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {DungeonBuilder.WorldToCell(fell.GlobalPosition)}"); return; }
        switch (_isStep)
        {
            case 0:
            {
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate).Where(k => k != shape.LastRideStop)); // stop 4's gate stays down: its holders are spared
                rc.Restore(2);
                rc.DebugSpendSpares();
                GD.Print($"[island] takeover: restored at stop {rc.StopIndex}, {rc.SparesLeft} spare(s) left, stop {shape.LastRideStop}'s gate up {rc.GateUp(shape.LastRideStop)}");
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
                _ilWreckS = rc.State.S;
                rc.DebugWreck();
                if (rc.State.Mode != CartMode.Wrecked || rc.Cart.Visible || !_ilNoCartLogged) { Fail($"the wreck on ride 3: mode {rc.State.Mode}, the cart shown {rc.Cart.Visible}, \"No carts left. The rest is on foot.\" logged {_ilNoCartLogged}"); return; }
                GD.Print($"[island] takeover: wrecked on ride 3 at S={_ilWreckS:0.0} with no spare left: \"No carts left. The rest is on foot.\"");
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                if (enc.ParkedNow != ParkedState.Lift) { if (_isClock > 1f) Fail($"1 s after the no-spare wreck the parked train is {enc.ParkedNow}, not a lift"); break; }
                var cart = train.Cart;
                party.Teleport(BesideLift(main, layout, rc, enc, cart), cart.GlobalPosition + Vector3.Up * 0.9f);
                GD.Print($"[island] takeover: the party put beside the lift at {DungeonBuilder.WorldToCell(me.GlobalPosition)} ({cart.TubDistance(me):0.0} m from its cart's tub)");
                var mate = party.Active.FirstOrDefault(a => a != me && !a.Downed);
                if (mate == null) { Fail("no companion up"); return; }
                // refused while a chaser stands (Ruling 26)
                rc.DebugPlaceChaser(shape.LastRideStop);
                if (rc.Chaser == null) { Fail("DebugPlaceChaser placed no chaser"); return; }
                var offered = cart.Prompt;
                var took = rc.DebugBoardLift(mate);
                var anyone = cart.Riders.Count + train.Wagons.Sum(w => w.Riders.Count);
                GD.Print($"[island] takeover: with stop {shape.LastRideStop}'s chaser on the line its cart offers \"{offered}\"; {mate.State.Name}'s press taken={took}, {anyone} seated in the lift");
                if (offered != "" || took || anyone > 0 || mate.Seated) { Fail($"with a chaser on the line the lift offered \"{offered}\", the press was taken {took}, {anyone} seated"); return; }
                rc.DebugEndChase();
                if (rc.Chaser != null) { Fail("DebugEndChase left the chaser on the line"); return; }
                _ilRefused = true;
                // the double press (Review Focus 5): the lead's key and the companion's remote press in one tick
                if (cart.Prompt != ParkedRules.BoardPrompt) { Fail($"the lead beside the lift is offered \"{cart.Prompt}\" once the chaser is gone"); return; }
                cart.Interact(party, me);
                rc.DebugBoardLift(mate);
                _ilPressT = _isTime;
                var problems = new List<string>();
                if (rc.Takeovers != 1) problems.Add($"{rc.Takeovers} takeover(s)");
                if (me.Cart != rc.Cart || mate.Cart != rc.Cart) problems.Add($"the lead in {(me.Cart as Node)?.Name ?? "nothing"}, {mate.State.Name} in {(mate.Cart as Node)?.Name ?? "nothing"}");
                if (cart.Riders.Count + train.Wagons.Sum(w => w.Riders.Count) > 0) problems.Add("someone sits in the lift");
                var place = ParkedRules.CartS(enc.ExitFromS);
                if (rc.State.Mode != CartMode.Held || Mathf.Abs(rc.State.S - place) > 0.01f || rc.State.Plating < CartRules.MaxPlating) problems.Add($"the party's cart is {rc.State.Mode} at S={rc.State.S:0.00} (the lift's cart S {place:0.00}), plating {rc.State.Plating:0}");
                if (rc.Train.Count != 4 || rc.Train.Any(w => !w.Coupled || !w.Visible) || rc.Train.Select(w => w.Number).SequenceEqual(new[] { 1, 2, 3, 4 }) == false) problems.Add($"the train is [{string.Join(",", rc.Train.Select(w => w.Number))}]");
                if (rc.StopIndex != shape.LastRideStop || !rc.StandingPast) problems.Add($"stop {rc.StopIndex}, StandingPast {rc.StandingPast}");
                if (enc.ParkedNow != ParkedState.Gone || !enc.TakenOver || train.Standing) problems.Add($"the parked train is {enc.ParkedNow}, TakenOver {enc.TakenOver}, standing {train.Standing}");
                if (problems.Count > 0) { Fail($"the takeover: {string.Join("; ", problems)}"); return; }
                GD.Print($"[island] takeover: one takeover; the lead and {mate.State.Name} in the party's cart, Held at S={rc.State.S:0.0} with W[{string.Join(",", rc.Train.Select(w => w.Number))}] coupled fresh, stop {rc.StopIndex}, StandingPast; the parked train {enc.ParkedNow}, TakenOver");
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                if (enc.ParkedNow != ParkedState.Gone) { Fail($"before the departure the parked train is {enc.ParkedNow}"); return; }
                if (rc.CrewLeftAt.Contains(shape.LastRideStop)) { Fail($"stop {shape.LastRideStop}'s prisoners counted as left behind"); return; }
                if (rc.State.Mode == CartMode.Held)
                {
                    if (!rc.Boarding.Running && _isTime - _ilPressT > 1f) { Fail($"no countdown 1 s after the lead sat (gate {rc.GateUp(shape.LastRideStop)}, cage held {rc.CageHeld})"); return; }
                    if (_isClock > 15f) Fail("the taken-over cart never departed");
                    break;
                }
                var cage = ObjectiveController.Instance is CrewRescueController ctrl ? ctrl.Cages.FirstOrDefault(c => c.Stop == shape.LastRideStop) : null;
                var passed = enc.ArmedFromPassed;
                GD.Print($"[island] takeover: departed {_isTime - _ilPressT:0.00} s after the press, stop {shape.LastRideStop}'s gate up {rc.GateUp(shape.LastRideStop)}, its cage open {cage?.Open}, left behind [{string.Join(",", rc.CrewLeftAt)}]; armed from its place with [{string.Join(", ", passed)}] passed");
                if (rc.GateUp(shape.LastRideStop) || cage is not { Open: false }) { Fail($"stop {shape.LastRideStop}'s gate up {rc.GateUp(shape.LastRideStop)}, its cage {(cage == null ? "missing" : cage.Open ? "open" : "locked")}: the takeover was not tested past a held stop"); return; }
                foreach (var id in new[] { "r5_golem", "r5_hounds", "r5_pit" })
                    if (!passed.Contains(id)) { Fail($"the departure did not count {id} as passed ([{string.Join(", ", passed)}])"); return; }
                if (rc.StandingPast) { Fail("StandingPast is still set after the departure"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            {
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the taken-over train was wrecked at S={rc.State.S:0.0}"); return; }
                if (rc.State.Mode == CartMode.Held && rc.StopIndex == shape.Rides)
                {
                    if (enc.TakenOver) { Fail("TakenOver is still set at the arrival"); return; }
                    if (_ilGoneFrames < 1) { Fail("the train's rear never passed the far edge while the latch held"); return; }
                    Pass($"refused while a chaser stood; the double press took over once, both in the party's cart, nobody in the lift; the cart Held at the lift's cart S with W1..W4 coupled fresh, stop {shape.LastRideStop}, StandingPast; its countdown at once though stop {shape.LastRideStop}'s gate was down and its cage locked, nobody left behind; armed from its place with the golem, the hounds and the pit passed; the parked train Gone through the rear passing the far edge ({_ilGoneFrames} frames) to the arrival; it arrived at stop {shape.Rides} as the party's train and TakenOver cleared");
                    return;
                }
                if (enc.ParkedNow != ParkedState.Gone) { Fail($"rolling at S={rc.State.S:0.0} the parked train is {enc.ParkedNow}, not Gone (the latch)"); return; }
                if (RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing) > enc.ExitToS) _ilGoneFrames++;
                if (_isClock > 60f) Fail($"the taken-over train did not arrive at stop {shape.Rides} (S={rc.State.S:0.0}, mode {rc.State.Mode})");
                break;
            }
        }
    }
}
