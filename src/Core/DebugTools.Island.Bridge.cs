using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-island=bridge|wreck (plan 15, Task 3) -------------------------------------------------------------------------------------
// =bridge: rc.Restore(Shape(rc).LastRideStop), everyone boards (a companion moved to W1 at the departure), and the last ride is ridden (the
// lava pit's switch thrown from the seat on the way: ThrowPitOnTheWay, plan 15 Task 4). As the train rolls, a crew member (stop 1's cage's) is put on foot 40 m
// behind the cart, following (ctrl.DebugPutOnFoot). Before the bridge, rolling, the lead looking back is offered a move (the control).
// The hold: on every frame any car is over the exit bridge the lead's own cart prompt is "" (rc.Cart.Prompt, not the look,
// which shows the spur's lever), MoveOfferFor is null for every hero; once, a forced Get out (cart and W1) does nothing and a forced Move is
// refused "the train is on the exit bridge". Braked on the span: at the cart's front ExitFromS + 6 the train is stopped and the lead holds the
// brake (nobody pumps), and the crew member is put on foot again at the last ride's stop; for 8 s nobody can get out (the presses
// again at 4 s and 8 s), and the crew member is never on a drop: out of reach of the seated lead it is caught up beside the cart (Ruling 12
// lands it off the bridge) once at least (before the brake, never in a chasm: following the train it may walk the cavern's decks). The
// save there and a load: the cart Held at the last ride's stop, every hero and crew member on a cell that may take a body, the crewman put on
// foot outside still outside (no catch-up crosses the lava; it is never inside the island at any frame). Let go: boarded again
// (a companion in W1) and ridden: on every frame with the cart's back past the far edge and the last wagon's back short of it (judged from
// the bodies) the train still holds, a round of presses is refused there, and the train arrives at the last stop.
// =wreck (Review Focus 3): rc.Restore(Shape(rc).LastRideStop) with stop 1's crew in W1 and stop 2's in W2 (ctrl.DebugSeatCrew) and the spares
// left; the lava pit's switch thrown from the seat on the way (ThrowPitOnTheWay); the cart is wrecked (rc.DebugWreck) as its front passes ExitFromS + 0.5. Within 1 s: no spare took over, the stop is the last,
// LastWreckInside, the cart and every party wagon hidden and not solid, every hero and crew member who rode on an island cell that may take
// a body, and (plan 15 Task 5) the parked train, smashed on the way, standing as a lift with no party body shown within its span; 3 s later none fallen and still there.
// Then (plan 15 Task 6) the lead, put outside beside the parked train, boards the lift: no takeover (the wreck was inside), it rides in and sets the lead
// down on an island cell. Then a save and a load give the cart Held at the last stop.
// Plan 15 Task 7 adds to =wreck (Review Focus 3): within 1 s the crew set down inside are Home, the unopened cages are left behind through
// LastWreckInside (Ruling 33), the win fires (the reward chest stood), and the arrival snapshot is at the last stop (a wipe after it rises inside).
public partial class DebugTools
{
    private const string IslandSlot = "islandtest";
    private RailController? _isRc;
    private Npc? _isOnFoot;
    private string? _isOnFootId;
    private int _isWagonsOnlyFrames;
    private bool _isRide2Seated, _isRide2Pressed;
    private Adventurer? _isWagonRider;
    private bool _isHoldChecked, _isMoveControl, _isBraked;
    private int _isHoldFrames, _isPresses, _isCatchUps, _isSparesBefore;
    /// <summary>The held frames of the first ride (rolling on and the 8 s stop), kept as it ends: <c>_isHoldFrames</c> runs on through the second.</summary>
    private int _isHoldFirst;
    private float _isBrakeT = float.NaN, _isWreckT = float.NaN;
    /// <summary>=wreck: the parked train's tubs still tumbling at the wreck (smashed on the way): its place stays Gone that long, so "smashed" is told before "standing".</summary>
    private float _isTumbleAtWreck;
    private Vector3 _isOnFootLast;
    private readonly List<TrainWagon> _isWagons = new();
    private readonly List<Node3D> _isRode = new();

    private void IslandBridge(Main main, FloorLayout layout, RailIsland island, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        if (_isStep < 6 && rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating); // the flow owns the plating: the kiln's hazards do not end the ride
        ThrowPitOnTheWay(main, rc, "island"); // round the lava pit on both rides (plan 15 Ruling 21)
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {DungeonBuilder.WorldToCell(fell.GlobalPosition)}"); return; }
        if (_isOnFoot is { } walker && IsInstanceValid(walker) && !walker.Seated && _isStep is >= 2 and < 6)
        {
            var cell = DungeonBuilder.WorldToCell(walker.GlobalPosition);
            if (_isOnFootLast != default && walker.GlobalPosition.DistanceTo(_isOnFootLast) > 8f) { _isCatchUps++; GD.Print($"[island] bridge: {walker.NpcName} caught up beside the cart at {cell} (front S={rc.Party.FrontS:0.0})"); }
            _isOnFootLast = walker.GlobalPosition;
            // a deck is a drop that holds a walker: following the train it may cross the cavern's; braked on the exit bridge (its catch-up
            // beside the cart: Ruling 12) it is never on any drop
            var braked = _isStep == 4;
            if ((braked ? CrevasseRules.IsDrop(layout, cell) : layout.IsChasm(cell)) || walker.Dead) { Fail($"{walker.NpcName}, on foot, is on {(braked ? "a drop" : "a chasm")} at {cell} (dead={walker.Dead}, front S={rc.Party.FrontS:0.0})"); return; }
            // put on foot outside, it is never set down inside the island (its catch-up keeps its side)
            if (layout.InIsland(cell)) { Fail($"{walker.NpcName}, put on foot outside, stands inside the island at {cell} (front S={rc.Party.FrontS:0.0}, {_isCatchUps} catch-up(s))"); return; }
        }
        var holds = enc.ExitBridgeHolds();
        switch (_isStep)
        {
            case 0:
            {
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(shape.LastRideStop);
                GD.Print($"[island] bridge: restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Number))}]; the exit bridge S={enc.ExitFromS:0.0}..{enc.ExitToS:0.0}");
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
                if (rc.State.Mode != CartMode.Rolling) { if (_isClock > 15f) Fail($"the last ride did not depart (mode {rc.State.Mode}, stop {rc.StopIndex})"); break; }
                if (!me.Seated || me.Cart != rc.Cart) { Fail("the lead is not seated in the cart"); return; }
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                _isWagonRider = party.Active.LastOrDefault(a => a != me && a.Cart == rc.Cart && !a.Downed);
                if (_isWagonRider == null || rc.Train.Count == 0 || !rc.DebugSeat(_isWagonRider, 1)) { Fail("no companion could be seated in W1"); return; }
                _isOnFoot = ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && !n.Dead && ctrl.Cages.FirstOrDefault(c => c.Stop == 1) is { } cage && n.Ride == cage);
                if (_isOnFoot == null) { Fail("no crew member in stop 1's cage to put on foot"); return; }
                var behind = rc.StandBesideLane(rc.Party.Track.Point(rc.State.S - 40f));
                ctrl.DebugPutOnFoot(_isOnFoot.Id, behind);
                _isOnFootId = _isOnFoot.Id;
                _isOnFootLast = default;
                GD.Print($"[island] bridge: departed stop {rc.StopIndex} at S={rc.State.S:0.0}, {rc.Train.Count} wagon(s), {_isWagonRider.State.Name} in W1; {_isOnFoot.NpcName} on foot 40 m behind at {DungeonBuilder.WorldToCell(behind)}, following={_isOnFoot.Escorting}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != shape.LastRideStop) { Fail($"the last ride ended another way (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})"); return; }
                if (!_isMoveControl && !holds && !rc.MovesRefused && rc.State.Mode == CartMode.Rolling && rc.State.V > 1f && rc.Party.FrontS < enc.ExitFromS)
                {
                    _isMoveControl = true;
                    var offer = CavernMoveOffer(rc, me);
                    GD.Print($"[island] bridge: before it, rolling at front S={rc.Party.FrontS:0.0}, the lead looking back is offered a move to {offer}");
                    if (offer == "none") { Fail("rolling before the bridge, the lead looking back was offered no move (the control)"); return; }
                }
                if (holds && IslandHoldBreaks(rc, enc, party, me) is { } broke) { Fail(broke); return; }
                if (holds && !_isHoldChecked)
                {
                    _isHoldChecked = true;
                    if (!_isMoveControl) { Fail("the move's control before the bridge never ran"); return; }
                    if (IslandPresses(rc, party, me, "rolling onto it") is { } pressed) { Fail(pressed); return; }
                }
                if (rc.Party.FrontS >= enc.ExitFromS + 6f)
                {
                    if (!_isHoldChecked) { Fail($"the front reached S={rc.Party.FrontS:0.0} with the bridge never holding its riders"); return; }
                    rc.Party.State = rc.State with { V = 0f }; // braked to a stop here
                    PumperCap = 0; // nobody pumps
                    party.ForcedMoveInput = new Vector2(0, 1); // the stick back: the brake
                    _isBrakeT = _isTime;
                    // the crew member put on foot again back at the last ride's stop, far out of the seated lead's reach (no path onto the
                    // bridge): its catch-up sets it down beside the cart, which stands over the lava, and Ruling 12 lands it off the bridge
                    if (ObjectiveController.Instance is CrewRescueController onFoot && _isOnFoot is { } crew)
                    {
                        var behind = rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[shape.LastRideStop]));
                        onFoot.DebugPutOnFoot(crew.Id, behind);
                        _isOnFootLast = crew.GlobalPosition; // a catch-up on the next physics step is a jump from here
                        _isCatchUps = 0;
                        GD.Print($"[island] bridge: {crew.NpcName} put on foot again at stop {shape.LastRideStop}'s platform, {behind.DistanceTo(me.GlobalPosition):0} m from the lead, at {DungeonBuilder.WorldToCell(behind)}");
                    }
                    GD.Print($"[island] bridge: braked to a stop with the front at S={rc.Party.FrontS:0.0} (the bridge {enc.ExitFromS:0.0}..{enc.ExitToS:0.0}, the rear at {RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing):0.0}), mode {rc.State.Mode}");
                    _isPresses = 0;
                    _isStep++; _isClock = 0f;
                    break;
                }
                if (_isClock > 90f) Fail($"the train did not come onto the exit bridge in 90 s (front S={rc.Party.FrontS:0.0})");
                break;
            }
            case 4:
            {
                party.ForcedMoveInput = new Vector2(0, 1);
                if (rc.State.Mode != CartMode.Rolling || !(rc.State.V < 0.05f)) { Fail($"braked on the bridge the train is {rc.State.Mode} at v {rc.State.V:0.00}"); return; }
                if (!holds) { Fail($"braked on the bridge (front S={rc.Party.FrontS:0.0}) the bridge does not hold its riders"); return; }
                if (IslandHoldBreaks(rc, enc, party, me) is { } broke) { Fail(broke); return; }
                if (_isPresses < 3 && _isClock >= 4f * _isPresses)
                {
                    if (IslandPresses(rc, party, me, $"stopped on it {_isClock:0.0} s") is { } pressed) { Fail(pressed); return; }
                    _isPresses++;
                }
                if (_isClock < 8f) break;
                if (_isCatchUps < 1) { Fail($"in 8 s stopped on the bridge {_isOnFoot?.NpcName}, on foot far behind, never caught up beside the cart (at {(_isOnFoot == null ? "?" : DungeonBuilder.WorldToCell(_isOnFoot.GlobalPosition).ToString())})"); return; }
                _isHoldFirst = _isHoldFrames;
                GD.Print($"[island] bridge: 8 s stopped on the bridge (front S={rc.Party.FrontS:0.0}): nobody got out ({_isHoldFrames} frames held on the first ride, rolling on and stopped, {_isPresses} presses), {_isOnFoot?.NpcName} on foot never on a drop ({_isCatchUps} catch-up(s) beside the cart)");
                if (rc.State.Mode != CartMode.Rolling) { Fail("the train is not mid-ride for the save"); return; }
                _isRc = rc;
                main.SaveGame(IslandSlot);
                if (!main.LoadGame(IslandSlot)) { Fail("the save on the bridge would not load"); return; }
                party.ForcedMoveInput = null;
                PumperCap = null;
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            {
                if (rc == _isRc) { if (_isClock > 10f) Fail("the floor was not built again within 10 s of the load"); break; }
                if (_isClock < 1f) break; // the load's deferred set-down
                var problems = new List<string>();
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.LastRideStop) problems.Add($"the cart is {rc.State.Mode} at stop {rc.StopIndex}, not Held at stop {shape.LastRideStop}");
                foreach (var a in party.Active) if (!StandsOk(layout, a) || a.State.Fallen) problems.Add($"{a.State.Name} at {DungeonBuilder.WorldToCell(a.GlobalPosition)} fallen={a.State.Fallen}");
                foreach (var n in IslandCrew()) if (!StandsOk(layout, n)) problems.Add($"{n.NpcName} at {DungeonBuilder.WorldToCell(n.GlobalPosition)}");
                // with the train Held at the last ride's stop, the crewman put on foot outside is still outside
                var outsider = IslandCrew().FirstOrDefault(n => n.Id == _isOnFootId);
                if (outsider == null) problems.Add($"the crewman put on foot ({_isOnFootId}) is gone after the load");
                else if (layout.InIsland(DungeonBuilder.WorldToCell(outsider.GlobalPosition))) problems.Add($"{outsider.NpcName}, put on foot outside, is inside the island at {DungeonBuilder.WorldToCell(outsider.GlobalPosition)}");
                if (problems.Count > 0) { if (_isClock > 5f) Fail($"after the load: {string.Join("; ", problems)}"); break; }
                GD.Print($"[island] bridge: after the load the cart is Held at stop {rc.StopIndex} (S={rc.State.S:0.0}); every hero [{string.Join(", ", party.Active.Select(a => $"{a.State.Name} {DungeonBuilder.WorldToCell(a.GlobalPosition)}"))}] and {IslandCrew().Count()} crew on cells that may take a body; {outsider!.NpcName}, put on foot outside, at {DungeonBuilder.WorldToCell(outsider.GlobalPosition)} (island {layout.InIsland(DungeonBuilder.WorldToCell(outsider.GlobalPosition))})");
                _isWagonRider = null;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 6:
            {
                if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == shape.Rides)
                {
                    if (_isWagonsOnlyFrames < 1 || !_isRide2Pressed) { Fail($"ridden again, the wagons-only hold was never seen ({_isWagonsOnlyFrames} frames, presses {_isRide2Pressed})"); return; }
                    Pass($"the first ride: from the cart's front onto the exit bridge to its stop at the start + 6 m, and 8 s stopped there, no Get out (cart and W1) and no move car to car ({_isHoldFirst} held frames on the first ride, {_isHoldFrames} in all with the second's, {_isPresses + 1} rounds of presses on the first, the move refused \"the train is on the exit bridge\"); the crewman on foot never on a drop and never inside the island ({_isCatchUps} catch-up(s) beside the cart, on its own side); the save there loaded Held at stop {shape.LastRideStop} with everyone on footing and the crewman outside; ridden again, {_isWagonsOnlyFrames} frames with the cart past the far edge and only wagons over it still held (a round of presses refused there); the train arrived at stop {shape.Rides}");
                    return;
                }
                if (rc.State.Mode == CartMode.Rolling)
                {
                    if (!_isRide2Seated)
                    {
                        _isRide2Seated = true;
                        _isWagonRider = party.Active.LastOrDefault(a => a != me && a.Cart == rc.Cart && !a.Downed);
                        if (_isWagonRider == null || rc.Train.Count == 0 || !rc.DebugSeat(_isWagonRider, 1)) { Fail("ridden again, no companion could be seated in W1"); return; }
                    }
                    // the hold's other end, judged from the bodies: the cart's back past the far edge, the last wagon's back still short of it
                    var last = rc.Train.Count > 0 ? rc.Train[^1] : null;
                    var rearS = last == null ? float.NaN : rc.Party.Track.ClosestS(last.GlobalPosition) - CartRules.HalfLength;
                    if (rc.Party.BackS > enc.ExitToS + 0.5f && rearS < enc.ExitToS - 0.5f)
                    {
                        if (!holds) { Fail($"only wagons over the exit bridge (the cart's back at S={rc.Party.BackS:0.0}, the last wagon's back at {rearS:0.0}) and it does not hold"); return; }
                        if (IslandHoldBreaks(rc, enc, party, me) is { } broke) { Fail(broke); return; }
                        _isWagonsOnlyFrames++;
                        if (!_isRide2Pressed)
                        {
                            _isRide2Pressed = true;
                            if (IslandPresses(rc, party, me, "only wagons over it") is { } pressed) { Fail(pressed); return; }
                        }
                    }
                    else if (holds && IslandHoldBreaks(rc, enc, party, me) is { } broke2) { Fail(broke2); return; }
                }
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex > shape.LastRideStop && rc.StopIndex != shape.Rides) { Fail($"ridden again the train is {rc.State.Mode} at stop {rc.StopIndex}"); return; }
                rc.DebugSetPlating(CartRules.MaxPlating);
                if (_isClock > 120f) Fail($"ridden again the train did not arrive at stop {shape.Rides} in 120 s (S={rc.State.S:0.0}, mode {rc.State.Mode})");
                break;
            }
        }
    }

    /// <summary>Every frame the bridge holds: the lead's own cart prompt is "" and no hero is offered a move car to car. Null, or what broke.</summary>
    private string? IslandHoldBreaks(RailController rc, RailEncounterController enc, PartyController party, Adventurer me)
    {
        _isHoldFrames++;
        var prompt = rc.Cart.Prompt;
        if (prompt != "") return $"over the exit bridge (front S={rc.Party.FrontS:0.0}) the lead's cart prompt is \"{prompt}\"";
        foreach (var a in party.Active.Where(a => rc.CarOf(a) >= 0))
            if (CavernMoveOffer(rc, a) is var offer && offer != "none") return $"over the exit bridge {a.State.Name} is offered a move to {offer}";
        if (enc.HoldReason() != "the train is on the exit bridge") return $"the hold's reason is \"{enc.HoldReason()}\"";
        return null;
    }

    /// <summary>A forced Get out from the cart and from W1 (its own rider's), and a forced move: all refused, everyone where they sat. Null, or what broke.</summary>
    private string? IslandPresses(RailController rc, PartyController party, Adventurer me, string when)
    {
        rc.Cart.Interact(party, me); // a "Get out" press, as the key would
        var w1 = rc.Train.Count > 0 ? rc.Train[0] : null;
        if (w1 != null && _isWagonRider is { } rider) w1.Interact(party, rider); // W1's own rider presses Get out
        var forced = w1 != null && rc.Move(me, w1, "test");
        GD.Print($"[island] bridge ({when}, front S={rc.Party.FrontS:0.0}, v {rc.State.V:0.00}): a Get out from the cart leaves the lead in car {rc.CarOf(me)}, from W1 leaves {_isWagonRider?.State.Name} in car {(_isWagonRider == null ? -1 : rc.CarOf(_isWagonRider))}; a forced move taken={forced} (refused: {rc.LastMoveRefused ?? "-"})");
        if (rc.CarOf(me) != 0) return $"{when}: a Get out press let the lead out";
        if (_isWagonRider != null && rc.CarOf(_isWagonRider) != 1) return $"{when}: W1's own rider got out";
        if (forced || rc.LastMoveRefused != "the train is on the exit bridge") return $"{when}: the forced move was {(forced ? "taken" : $"refused \"{rc.LastMoveRefused}\"")}, not refused \"the train is on the exit bridge\"";
        return null;
    }

    private void IslandWreck(Main main, FloorLayout layout, RailIsland island, RailController rc, RailEncounterController enc, PartyController party, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        if (_isStep < 3 && rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(CartRules.MaxPlating);
        ThrowPitOnTheWay(main, rc, "island"); // round the lava pit (plan 15 Ruling 21)
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {DungeonBuilder.WorldToCell(fell.GlobalPosition)}"); return; }
        switch (_isStep)
        {
            case 0:
            {
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate));
                rc.Restore(shape.LastRideStop);
                var w1 = ctrl.DebugSeatCrew(1, wagon: 1);
                var w2 = ctrl.DebugSeatCrew(2, wagon: 2);
                if (w1 is not { Count: > 0 } || w2 is not { Count: > 0 }) { Fail($"stops 1 and 2's crew not seated in W1 and W2 ({w1?.Count}, {w2?.Count})"); return; }
                _isSparesBefore = rc.SparesUsed.Count;
                GD.Print($"[island] wreck: restored at stop {rc.StopIndex}, crew [{string.Join(", ", w1.Concat(w2).Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}], spares used {_isSparesBefore} of {shape.Intermediate}");
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
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != shape.LastRideStop) { Fail($"the last ride ended before the bridge (mode {rc.State.Mode}, stop {rc.StopIndex})"); return; }
                if (rc.State.Mode != CartMode.Rolling || rc.Party.FrontS < enc.ExitFromS + 0.5f) { if (_isClock > 120f) Fail($"the front never passed S={enc.ExitFromS + 0.5f:0.0} (at {rc.Party.FrontS:0.0})"); break; }
                _isWagons.Clear();
                _isWagons.AddRange(rc.Train);
                _isRode.Clear();
                _isRode.AddRange(party.Active.Where(a => rc.CarOf(a) >= 0));
                _isRode.AddRange(IslandCrew().Where(n => n.Ride is TrainWagon || n.Cart == rc.Cart));
                var front = rc.Party.FrontS;
                _isTumbleAtWreck = enc.ParkedTumbleLeft;
                rc.DebugWreck();
                _isWreckT = _isTime;
                GD.Print($"[island] wreck: the cart wrecked as its front passed S={front:0.0} (the bridge from {enc.ExitFromS:0.0}): {_isRode.Count} rode ({string.Join(", ", _isRode.Select(b => b.Name))}), wagons [{string.Join(",", _isWagons.Select(w => w.Number))}]");
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            case 4:
            {
                var problems = new List<string>();
                if (rc.SparesUsed.Count != _isSparesBefore) problems.Add($"a spare took over ({rc.SparesUsed.Count} used, {_isSparesBefore} before)");
                if (rc.StopIndex != shape.Rides) problems.Add($"the stop is {rc.StopIndex}, not the last ({shape.Rides})");
                if (!rc.LastWreckInside) problems.Add("LastWreckInside is not set");
                if (rc.Cart.Visible || rc.Cart.CollisionLayer != 0) problems.Add($"the cart visible={rc.Cart.Visible} layer={rc.Cart.CollisionLayer}");
                foreach (var w in _isWagons) if (IsInstanceValid(w) && (w.Visible || w.CollisionLayer != 0 || w.Coupled)) problems.Add($"W{w.Number} visible={w.Visible} layer={w.CollisionLayer} coupled={w.Coupled}");
                foreach (var b in _isRode)
                {
                    if (!IsInstanceValid(b)) { problems.Add("a rider is gone"); continue; }
                    var cell = DungeonBuilder.WorldToCell(b.GlobalPosition);
                    var seated = b is Adventurer h ? h.Seated : b is Npc n && n.Seated;
                    var gone = b is Adventurer f ? f.State.Fallen : b is Npc d && d.Dead;
                    if (seated || gone || !layout.InIsland(cell) || !CrevasseRules.SetDownOk(layout, cell)) problems.Add($"{b.Name} at {cell} seated={seated} fallen/dead={gone} island={layout.InIsland(cell)}");
                }
                // plan 15 Task 5: the parked train (smashed on the way) stands as a lift within 1 s of its tubs' tumble ending (the wreck comes a few
                // seconds after the smash: its place stays Gone till then), never before, with no party body shown within its span
                var liftLate = _isTime - _isWreckT - _isTumbleAtWreck;
                if (enc.ParkedNow != ParkedState.Lift && liftLate > 1f) { Fail($"the parked train is {enc.ParkedNow}, not a lift, {liftLate:0.00} s after its tubs' tumble ended"); return; }
                if (enc.ParkedNow == ParkedState.Lift && liftLate < -0.1f) { Fail($"the parked train stood as a lift {-liftLate:0.00} s before its tubs' tumble ended"); return; }
                foreach (var body in _isWagons.Cast<Node3D>().Prepend(rc.Cart))
                    if (IsInstanceValid(body) && body.Visible && rc.Party.Track.ClosestS(body.GlobalPosition) is var bs && bs > enc.ParkedRearS - CartRules.HalfLength && bs < enc.ParkedFrontS + CartRules.HalfLength)
                        problems.Add($"the party's {body.Name} is shown within the parked train's span (S={bs:0.0})");
                // plan 15 Task 7 (Review Focus 3): the set-down crew Home, the unopened cages resolved through LastWreckInside, the win, and the
                // arrival snapshot taken at the last stop (a wipe now rises inside)
                if (_isStep == 3 && ObjectiveController.Instance is CrewRescueController crew)
                {
                    foreach (var n in _isRode.OfType<Npc>()) if (IsInstanceValid(n) && crew.StateOf(n) != CrewState.Home) problems.Add($"{n.NpcName} set down inside is {crew.StateOf(n)}, not Home");
                    foreach (var cage in crew.Cages.Where(c => !c.Open)) if (!rc.CrewLeftAt.Contains(cage.Stop)) problems.Add($"the unopened cage at stop {cage.Stop} is not left behind");
                    // resolved by ResolveUnopened through LastWreckInside (Wrecked: the train is not held at the end), not only left by the
                    // restore's LeftOnLoad and the waiver at stop 4
                    // the locked cages by the line's shape: the intermediate stops' cages still shut (3 and 4 on the shipped floor)
                    var locked = Enumerable.Range(1, shape.Intermediate).Where(k => crew.Cages.Any(c => c.Stop == k && !c.Open)).ToList();
                    if (locked.Count == 0) problems.Add("no cage was left locked to resolve");
                    foreach (var stop in locked) if (!crew.CagesResolved.Contains(stop)) problems.Add($"the unopened cage at stop {stop} was not resolved through LastWreckInside (resolved [{string.Join(",", crew.CagesResolved.OrderBy(x => x))}])");
                    if (crew.CagesBlockingNow != 0) problems.Add($"{crew.CagesBlockingNow} cage(s) still block the stairs");
                    if (IslandWon(crew, main.Builder.FloorRoot!) is { } notWon) problems.Add($"no win ({notWon})");
                    if (rc.ArrivalSnapshot?.RailStop != shape.Rides) problems.Add($"the arrival snapshot is at stop {rc.ArrivalSnapshot?.RailStop?.ToString() ?? "none"}, not {shape.Rides}");
                }
                var within = _isStep == 3 ? 1f : 3f;
                if (problems.Count > 0) { if (_isClock > within) Fail($"{(_isStep == 3 ? "within 1 s of" : "3 s after")} the wreck: {string.Join("; ", problems)}"); break; }
                if (_isStep == 3)
                {
                    GD.Print($"[island] wreck: {_isClock:0.00} s after it no spare took over (spares used {rc.SparesUsed.Count}), stop {rc.StopIndex}, LastWreckInside, the cart and W[{string.Join(",", _isWagons.Select(w => w.Number))}] hidden and not solid; on island cells: {string.Join(", ", _isRode.Select(b => $"{b.Name} {DungeonBuilder.WorldToCell(b.GlobalPosition)}"))}; the parked train {enc.ParkedNow} at S={enc.ParkedRearS:0.0}..{enc.ParkedFrontS:0.0} ({enc.ParkedSmashes} smash(es) on the way), no party body shown within it");
                    if (ObjectiveController.Instance is CrewRescueController won)
                        GD.Print($"[island] wreck: the set-down crew Home [{string.Join(", ", _isRode.OfType<Npc>().Select(n => $"{n.NpcName}={won.StateOf(n)}"))}], the unopened cages [{string.Join(",", won.Cages.Where(c => !c.Open).Select(c => c.Stop))}] left behind (CrewLeftAt [{string.Join(",", rc.CrewLeftAt.OrderBy(x => x))}], resolved through LastWreckInside [{string.Join(",", won.CagesResolved.OrderBy(x => x))}]), the win (reward chest {won.RewardChest != null}); the arrival snapshot at stop {rc.ArrivalSnapshot?.RailStop}");
                    _isStep++; _isClock = 0f;
                    break;
                }
                if (_isClock < 3f || enc.ParkedNow != ParkedState.Lift) break;
                GD.Print($"[island] wreck: 3 s on nobody has fallen and everyone stands inside; the parked train a lift {_isTime - _isWreckT - _isTumbleAtWreck:0.00} s after its tubs' tumble ended ({_isTumbleAtWreck:0.00} s of it left at the wreck)");
                // plan 15 Task 6 (Ruling 27's wreckedInside): the lead, put outside beside the parked train, boards the lift: no takeover, it rides in
                if (enc.Parked is not { } lift) { Fail("no parked train for the lift leg"); return; }
                if (party.Local is not { } lead) { Fail("no lead"); return; }
                lead.Teleport(BesideLift(main, layout, rc, enc, lift.Cart), lift.Cart.GlobalPosition + Vector3.Up * 0.9f);
                var offer = lift.Cart.Prompt;
                lift.Cart.Interact(party, lead);
                GD.Print($"[island] wreck: the lead put beside the lift outside at {DungeonBuilder.WorldToCell(lead.GlobalPosition)}, offered \"{offer}\", pressed: seated in the lift {lead.Cart == lift.Cart}, takeovers {rc.Takeovers}");
                if (offer != ParkedRules.BoardPrompt || lead.Cart != lift.Cart || rc.Takeovers != 0 || rc.Lift == null) { Fail($"after the inside wreck the lift offered \"{offer}\", the lead seated in it {lead.Cart == lift.Cart}, {rc.Takeovers} takeover(s)"); return; }
                _isStep = 6; _isClock = 0f;
                break;
            }
            case 6:
            {
                if (rc.LiftsDone < 1) { if (_isClock > 40f) Fail($"the lift never halted inside (S={rc.LiftS:0.0})"); break; }
                if (party.Local is not { } lead) { Fail("no lead"); return; }
                var cell = DungeonBuilder.WorldToCell(lead.GlobalPosition);
                if (lead.Seated || !layout.InIsland(cell) || !CrevasseRules.SetDownOk(layout, cell)) { Fail($"after the lift's halt the lead at {cell} seated={lead.Seated} island={layout.InIsland(cell)}"); return; }
                if (rc.Takeovers != 0) { Fail($"{rc.Takeovers} takeover(s) after an inside wreck"); return; }
                GD.Print($"[island] wreck: the lift halted at S={rc.LastLiftHaltS:0.0} ({rc.LastLiftGap:0.00} m short of what stopped it) and set the lead down inside at {cell}; no takeover");
                _isRc = rc;
                main.SaveGame(IslandSlot);
                if (!main.LoadGame(IslandSlot)) { Fail("the save after the wreck would not load"); return; }
                _isStep = 5; _isClock = 0f;
                break;
            }
            case 5:
            {
                if (rc == _isRc) { if (_isClock > 10f) Fail("the floor was not built again within 10 s of the load"); break; }
                if (_isClock < 1f) break;
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides || !rc.Cart.Visible) { Fail($"after the load the cart is {rc.State.Mode} at stop {rc.StopIndex} visible={rc.Cart.Visible}, not Held at stop {shape.Rides}"); return; }
                Pass($"the wreck past the exit bridge's start (S {enc.ExitFromS:0.0} + 0.5) spent no spare, took stop {shape.Rides}, hid the cart and W[{string.Join(",", _isWagons.Select(w => w.Number))}], and set {_isRode.Count} riders (crew too) down inside within 1 s, none fallen 3 s on; within 1 s of its tubs' tumble ending ({_isTumbleAtWreck:0.0} s of it left at the wreck), never before, the parked train stood as a lift with no party body shown within its span; the lead, put outside, boarded the lift (no takeover), rode it in and was set down inside; the save and load gave the cart Held at stop {shape.Rides}");
                return;
            }
        }
    }
}
