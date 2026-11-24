using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-island=parked (plan 15, Task 5) -------------------------------------------------------------------------------------------
// Ride 5's parked train (spec B.5; Rulings 23-25), under --test-island's conventions (solo, lethality 3, every enemy culled, Shape(rc) for every
// stop and id). rc.Restore(Shape(rc).LastRideStop) with stop 2's crew in W2 (ctrl.DebugSeatCrew), then:
// The build: the parked train's rear (its last tub's back, from the body) at ParkedRules.RearS(ExitFromS, 4) +- 0.1, in the way, standing, its five
// bodies solid (layers 1|8), its tubs OfLift with their own wire names and none of them in rc.Wagons.
// In the way: at the stop, held, the lead teleported beside it and looking at it is offered "" and never "Board the cart". The guard (no parked
// tub is the party's): the lead seated in its W1 is offered no Get out and no move and is in no car of the train; a crew member downed in its W2 offers no revive
// and is not in the party's train; neither a press on them nor on the party's cart from them is a move. Everyone boards; the lava pit's switch is
// thrown from the seat on the way. Rolling: a companion in W1 (it pays at the smash: the whole train does), one held at 5% HP, one seated in the parked
// train's cart (DebugSeatParked: the race); a press on a parked body is never a move ("not a car of the train").
// The sight: a layer-1 ray from the seated lead's eye with the front 15 m short of its rear to its lantern is clear. The call (bark or log) with
// the front 33-35 m from its rear, its alarm on, the bell rung at it. The race: the companion aboard is thrown clear onto a cell that may take a
// body, HP >= 1, never fallen. The smash: plating -25, each other seated up hero -15% of max HP (the 5% one left with 1 HP or more, the W1 one paid),
// the crew's HP unchanged, V x 0.8 +- 0.05, five tubs tumbling, ParkedNow Gone. Standing again: within 1 s of the train's rear passing ExitToS
// ParkedNow is Lift and "Another cart rolls out..." is logged. A restore at the last ride's stop stands it in the way again.
// Last, a smash that wrecks the cart: every spare spent (rc.DebugSpendSpares), the last ride ridden again with the plating
// set to ParkedRules.SmashDent 3 m short of its rear, so the smash wrecks the cart with no spare left. On the frame after: the cart wrecked and
// hidden (not inside, no spare spent), the parked train Gone "the smash", five tubs tumbling, its PropEvt "smashed" told and no "standing"; Gone
// every frame while ParkedTumbleLeft > 0; then a lift (ParkedWhy "a lift is due") within 1 s of the tumble's end, "standing" told after "smashed";
// each smash played timber_smash once at its lantern, and "Another cart rolls out..." was logged once a ride (twice: the restore between).
public partial class DebugTools
{
    private bool _ipListening;
    private float _ipCallGap = float.NaN, _ipRearPassT = float.NaN, _ipLiftLogT = float.NaN;
    private bool _ipAlarmSeen, _ipBellSeen, _ipSmashLogged, _ipSightDone, _ipMovePinned;
    private float _ipPlatingBefore, _ipVBefore;
    private int _ipLookFrames;
    private Adventurer? _ipRacer, _ipWagonHero, _ipLow;
    private float _ipLowMax;
    private readonly Dictionary<Adventurer, (float Hp, float Max)> _ipHpBefore = new();
    private readonly Dictionary<Npc, float> _ipCrewBefore = new();
    private readonly List<Npc> _ipCrew = new();
    private float _ipRacerWatch;
    /// <summary>The smash that wrecks the cart: the parked train's PropEvt words from the second ride on, the smashes before it, and when its smash wrecked the cart.</summary>
    private readonly List<string> _ipTold = new();
    private int _ipSmashesBefore;
    private float _ipWreckSmashT = float.NaN;
    /// <summary>The smash's timber_smash heard at its lantern (server and solo), and how often "Another cart rolls out..." was logged.</summary>
    private int _ipSmashSounds, _ipLiftLogs;

    private void IslandParkedListen()
    {
        if (_ipListening) return;
        _ipListening = true;
        Action<BarkTrigger, string?> called = (t, by) =>
        {
            if (_done || !_testIsland || t != BarkTrigger.ParkedCart || RailController.Instance is not { } r || RailEncounterController.Instance?.Parked is not { } train) return;
            _ipCallGap = train.RearFaceS - r.Party.FrontS;
            GD.Print($"[island] parked: the call by {by ?? "none (logged)"} with the front {_ipCallGap:0.0} m from its rear");
        };
        RailCalls.Called += called;
        Action<string, Vector3, bool> played = (cue, at, _) =>
        {
            if (_done || !_testIsland || RailEncounterController.Instance?.Parked is not { } train || at.DistanceTo(train.SoundPoint) >= 1f) return;
            if (cue == "cart_bell") _ipBellSeen = true;
            if (cue == "timber_smash") _ipSmashSounds++;
        };
        Audio.AudioManager.PlayedAt += played;
        Action<string, string> told = (netRef, what) =>
        {
            if (!_done && _testIsland && RailEncounterController.Instance?.Parked is { } train && netRef == train.NetRef) _ipTold.Add(what);
        };
        EventBus.Instance.PropChanged += told;
        _staticUnhooks.Add(() => { RailCalls.Called -= called; Audio.AudioManager.PlayedAt -= played; EventBus.Instance.PropChanged -= told; });
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testIsland || RailController.Instance is not { } r) return;
            if (text == ParkedRules.LiftLog) _ipLiftLogs++;
            if (text == ParkedRules.LiftLog && float.IsNaN(_ipLiftLogT)) _ipLiftLogT = _isTime;
            if (text != ParkedRules.SmashLog) return;
            // logged before the riders are thrown clear and before the cost (SmashParked): every value here is the one the smash met
            _ipSmashLogged = true;
            _ipPlatingBefore = r.State.Plating;
            _ipVBefore = r.State.V;
            _ipHpBefore.Clear();
            foreach (var a in Main.Instance.Party.Active)
                if (r.CarOf(a) >= 0 && !a.Downed) _ipHpBefore[a] = (a.State.Hp, a.State.Stats[Characters.Stat.MaxHp]);
            _ipCrewBefore.Clear();
            foreach (var n in _ipCrew) if (IsInstanceValid(n)) _ipCrewBefore[n] = n.Hp;
        };
    }

    private void IslandParked(Main main, FloorLayout layout, RailIsland island, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Node3D root, Action<string> Fail, Action<string> Pass)
    {
        var shape = Shape(rc);
        IslandParkedListen();
        if (enc.Parked is not { } train) { Fail("floor 12 has no live parked train"); return; }
        ThrowPitOnTheWay(main, rc, "island"); // round the lava pit (plan 15 Ruling 21)
        var nearRear = rc.Party.FrontS >= train.RearFaceS - 6f; // past the hammers (S 1154-1160): only the smash is left before the rear
        if (_isStep is >= 3 and < 5 && !_ipSmashLogged && rc.State.Mode == CartMode.Rolling && rc.Party.FrontS < train.RearFaceS - 3f) rc.DebugSetPlating(CartRules.MaxPlating); // the flow owns the plating up to the smash
        if (_ipLow != null && nearRear && !_ipSmashLogged && IsInstanceValid(_ipLow) && !_ipLow.Downed) _ipLow.State.Hp = _ipLowMax * 0.05f; // held at 5% from past the hammers to the smash (a potion would lift it)
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_isStep} at {DungeonBuilder.WorldToCell(fell.GlobalPosition)}"); return; }
        if (train.Alarm) _ipAlarmSeen = true;
        switch (_isStep)
        {
            case 0:
            {
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                rc.RestoreGates(Enumerable.Range(1, shape.Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(shape.LastRideStop);
                if (ctrl.DebugSeatCrew(2, wagon: 2) is not { Count: > 0 } crew) { Fail("stop 2's crew could not be seated in W2"); return; }
                _ipCrew.Clear();
                _ipCrew.AddRange(crew.Select(c => c.Npc));
                var last = train.Wagons[^1];
                var rear = rc.Party.Track.ClosestS(last.GlobalPosition) - CartRules.HalfLength;
                var want = ParkedRules.RearS(enc.ExitFromS, ParkedRules.Wagons);
                var problems = new List<string>();
                if (Mathf.Abs(rear - want) > 0.1f) problems.Add($"its rear stands at S={rear:0.00}, not RearS(ExitFromS={enc.ExitFromS:0.00}, 4) = {want:0.00}");
                if (enc.ParkedNow != ParkedState.InTheWay || !train.Standing) problems.Add($"it is {enc.ParkedNow}, standing={train.Standing}, not in the way");
                if (train.Cart.CollisionLayer != ParkedTrain.StandingLayers || train.Wagons.Any(w => w.CollisionLayer != ParkedTrain.StandingLayers || !w.Visible)) problems.Add("a body of it is not solid (layers 1|8) or not shown");
                if (train.Cart.CartId != CartIds.Lift || train.Cart.Kind != CartKind.Chaser) problems.Add($"its cart is id {train.Cart.CartId} kind {train.Cart.Kind}, not the lift's ({CartIds.Lift}, Chaser)");
                for (var k = 1; k <= train.Wagons.Count; k++)
                {
                    var w = train.Wagons[k - 1];
                    if (!w.OfLift || w.Number != k || w.NetRef != NetRefs.LiftWagon(k) || rc.Wagons.Contains(w) || rc.WagonAt(k) == w) problems.Add($"its W{k} is OfLift={w.OfLift}, number {w.Number}, ref {w.NetRef}, in the party's train={rc.Wagons.Contains(w)}");
                }
                if (NetRefResolver.RefOf(train.Cart) != NetRefs.Parked(enc.ParkedIndex) || NetRefResolver.Resolve(root, NetRefs.Parked(enc.ParkedIndex)) != train.Cart || NetRefResolver.Resolve(root, NetRefs.LiftWagon(2)) != train.Wagons[1])
                    problems.Add($"its wire names: the cart {NetRefResolver.RefOf(train.Cart) ?? "none"}, parked:{enc.ParkedIndex} resolves to {NetRefResolver.Resolve(root, NetRefs.Parked(enc.ParkedIndex))?.Name}");
                GD.Print($"[island] parked: {enc.ParkedId} built: rear S={rear:0.00} (RearS {want:0.00}), cart S={train.CartS:0.0}, the exit bridge from {enc.ExitFromS:0.0}; {enc.ParkedNow}, layers cart {train.Cart.CollisionLayer} wagons [{string.Join(",", train.Wagons.Select(w => w.CollisionLayer))}]; restored at stop {rc.StopIndex} with [{string.Join(", ", crew.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}]");
                if (problems.Count > 0) { Fail($"the build: {string.Join("; ", problems)}"); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 1 when _isClock >= 1f:
            {
                // in the way, at the stop, held: the lead beside it on foot, looking at it
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.LastRideStop) { Fail($"the cart is {rc.State.Mode} at stop {rc.StopIndex}, not held at stop {shape.LastRideStop}"); return; }
                if (_ipLookFrames == 0)
                {
                    var beside = rc.StandBesideLane(train.Cart.GlobalPosition);
                    me.Teleport(beside, train.Cart.GlobalPosition + Vector3.Up * 0.9f);
                    GD.Print($"[island] parked: in the way: the lead teleported beside it at {DungeonBuilder.WorldToCell(beside)} ({beside.DistanceTo(train.Cart.GlobalPosition):0.0} m from its cart)");
                }
                BarricadeAim(me, train.Cart.GlobalPosition + Vector3.Up * 0.9f);
                _ipLookFrames++;
                var offered = new List<string>();
                if (train.Cart.Prompt != "") offered.Add($"its cart \"{train.Cart.Prompt}\"");
                foreach (var w in train.Wagons) if (w.Prompt != "") offered.Add($"W{w.Number} \"{w.Prompt}\"");
                if (me.LookingAt is { } look && look.Prompt == ParkedRules.BoardPrompt) offered.Add($"the look ({(look as Node)?.Name}) \"{look.Prompt}\"");
                var rule = ParkedRules.Prompt(enc.ParkedNow, rc.Chaser != null, onFootPlayer: !me.Seated, inReach: true);
                if (rule != "") offered.Add($"ParkedRules.Prompt \"{rule}\"");
                if (offered.Count > 0) { Fail($"in the way, the lead beside it is offered {string.Join(", ", offered)}"); return; }
                if (_ipLookFrames < 30) break;
                GD.Print($"[island] parked: in the way: {_ipLookFrames} frames beside it, the lead looking at {(me.LookingAt as Node)?.Name ?? "nothing"}: offered \"\" by its cart and its four tubs, never \"{ParkedRules.BoardPrompt}\"");
                if (IslandParkedGuard(rc, enc, train, me) is { } broke) { Fail(broke); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 2 when _isClock >= 0.5f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 3:
            {
                if (rc.State.Mode != CartMode.Rolling) { if (_isClock > 15f) Fail($"the last ride did not depart (mode {rc.State.Mode}, stop {rc.StopIndex})"); break; }
                if (!me.Seated || me.Cart != rc.Cart) { Fail("the lead is not seated in the cart"); return; }
                var companions = party.Active.Where(a => a != me && a.Cart == rc.Cart && !a.Downed).ToList();
                if (companions.Count < 3) { Fail($"only {companions.Count} companion(s) rode with the lead"); return; }
                _ipWagonHero = companions[0];
                _ipLow = companions[1];
                _ipRacer = companions[2];
                if (!rc.DebugSeat(_ipWagonHero, 1)) { Fail($"{_ipWagonHero.State.Name} could not be seated in W1"); return; }
                _ipLowMax = _ipLow.State.Stats[Characters.Stat.MaxHp];
                if (!enc.DebugSeatParked(_ipRacer)) { Fail($"{_ipRacer.State.Name} could not be seated in the parked train's cart"); return; }
                GD.Print($"[island] parked: departed stop {rc.StopIndex} at S={rc.State.S:0.0}: {_ipWagonHero.State.Name} in W1, {_ipLow.State.Name} to be held at 5% of {_ipLowMax:0} HP from 6 m short of the smash, in car {rc.CarOf(_ipLow)}, {_ipRacer.State.Name} seated in the parked train's cart (the race)");
                _isStep++; _isClock = 0f;
                break;
            }
            case 4:
            {
                if (rc.State.Mode == CartMode.Wrecked || rc.StopIndex != shape.LastRideStop) { Fail($"the last ride ended before the smash (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})"); return; }
                var front = rc.Party.FrontS;
                var rearFace = train.RearFaceS;
                if (!_ipMovePinned && rc.State.Mode == CartMode.Rolling && rc.State.V > 1f && !rc.MovesRefused)
                {
                    _ipMovePinned = true;
                    // a press on a parked body from the train is never a move, nor offered
                    var bodies = new List<ICartBody> { train.Cart };
                    bodies.AddRange(train.Wagons);
                    foreach (var b in bodies)
                    {
                        var name = (b as Node)?.Name;
                        if (rc.IsMoveAsk(me, b)) { Fail($"rolling, a press on the parked train's {name} from the cart is a move ask"); return; }
                        if (rc.WhyMoveRefused(me, b) != "not a car of the train") { Fail($"rolling, a move to the parked train's {name} is judged \"{rc.WhyMoveRefused(me, b) ?? "taken"}\""); return; }
                        if (rc.CarCarrying(b as Node) != -1 || rc.PlainPromptOf(b, me) != null) { Fail($"rolling, the parked train's {name} reads as car {rc.CarCarrying(b as Node)} of the train"); return; }
                    }
                    var taken = rc.Move(me, train.Wagons[0], "test");
                    if (taken || rc.LastMoveRefused != "not a car of the train") { Fail($"a forced move to the parked train's W1 was {(taken ? "taken" : $"refused \"{rc.LastMoveRefused}\"")}"); return; }
                    if (rc.MoveOfferFor(me) is { } offer && bodies.Contains(offer)) { Fail("the lead is offered a move to a parked body"); return; }
                    GD.Print($"[island] parked: rolling at S={rc.State.S:0.0}: a press on any of its five bodies is no move ask, a forced move to its W1 refused \"{rc.LastMoveRefused}\"");
                }
                if (!_ipSightDone && front >= rearFace - 15f)
                {
                    _ipSightDone = true;
                    var track = rc.Party.Track;
                    var eyeLocal = track.Frame(rc.State.S).AffineInverse() * me.Camera.GlobalPosition;
                    var eye = track.Frame(rearFace - 15f - CartRules.HalfLength) * eyeLocal; // the seat's eye with the front 15 m short of its rear
                    var skip = new Godot.Collections.Array<Rid> { train.Cart.GetRid(), rc.Cart.GetRid() };
                    foreach (var w in train.Wagons) skip.Add(w.GetRid());
                    foreach (var w in rc.Train) skip.Add(w.GetRid());
                    var hit = me.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, train.RearLantern, 1, skip));
                    var sight = hit.Count == 0 ? "clear" : $"blocked by {(hit["collider"].AsGodotObject() as Node)?.Name ?? "something"} at {((Vector3)hit["position"]).DistanceTo(eye):0.0} m";
                    GD.Print($"[island] parked: the sight from the seat 15 m short of its rear ({eye.DistanceTo(train.RearLantern):0.0} m to its lantern): {sight}");
                    if (hit.Count > 0) { Fail($"its lantern is not seen from the seat 15 m short: {sight}"); return; }
                }
                if (!_ipSmashLogged) { if (_isClock > 120f) Fail($"the cart never smashed the parked train (front {front:0.0}, its rear {rearFace:0.0}, {enc.ParkedNow})"); break; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 5:
            {
                // the frame after the smash: the riders thrown, the dent, the slowing and the hurts are in
                var drop = _ipPlatingBefore - rc.State.Plating;
                var kept = _ipVBefore > 0f ? rc.State.V / _ipVBefore : float.NaN;
                var fraction = BarricadeRules.SmashFraction(TierRules.PartyDamageScale);
                var lines = new List<string>();
                var problems = new List<string>();
                if (float.IsNaN(_ipCallGap) || _ipCallGap < 33f || _ipCallGap > 35f) problems.Add($"the call came with the front {_ipCallGap:0.0} m from its rear, not 33-35 m");
                if (!_ipAlarmSeen || !_ipBellSeen) problems.Add($"its alarm on {_ipAlarmSeen}, its bell rung {_ipBellSeen}");
                foreach (var (hero, (hp, max)) in _ipHpBefore)
                {
                    var lost = (hp - hero.State.Hp) / max;
                    lines.Add($"{hero.State.Name} car {rc.CarOf(hero)} {hp:0.0}->{hero.State.Hp:0.0}/{max:0} ({lost:P1})");
                    if (hero == _ipLow) { if (hero.Downed || hero.State.Hp < 0.999f) problems.Add($"{hero.State.Name} at 5% went down or below 1 HP ({hero.State.Hp:0.00})"); }
                    else if (Mathf.Abs(lost - fraction) > 0.005f) problems.Add($"{hero.State.Name} lost {lost:P1}, not {fraction:P0}");
                }
                if (_ipWagonHero == null || !_ipHpBefore.ContainsKey(_ipWagonHero) || rc.CarOf(_ipWagonHero) != 1) problems.Add("the W1 hero was not seated in W1 at the smash (pre-flight F4)");
                if (_ipLow == null || !_ipHpBefore.ContainsKey(_ipLow)) problems.Add("the 5% companion was not seated at the smash");
                var crewLines = new List<string>();
                foreach (var (n, hp) in _ipCrewBefore)
                {
                    crewLines.Add($"{n.NpcName} {hp:0.0}->{n.Hp:0.0}");
                    if (!Mathf.IsEqualApprox(n.Hp, hp)) problems.Add($"{n.NpcName}'s HP went {hp:0.0} -> {n.Hp:0.0}");
                }
                if (_ipCrewBefore.Count == 0) problems.Add("no crew rode at the smash");
                if (Mathf.Abs(drop - ParkedRules.SmashDent) > 0.01f) problems.Add($"the plating dropped by {drop:0.00}, not {ParkedRules.SmashDent:0}");
                if (float.IsNaN(kept) || Mathf.Abs(kept - ParkedRules.SpeedKept) > 0.05f) problems.Add($"v went {_ipVBefore:0.00} -> {rc.State.V:0.00} (x{kept:0.00}), not x{ParkedRules.SpeedKept:0.0}");
                var tumbles = root.GetChildren().Count(n => n is RigidBody3D && n.Name.ToString().StartsWith(ParkedTrain.TumbleName, StringComparison.Ordinal));
                if (tumbles != 5) problems.Add($"{tumbles} tub(s) tumbling, not 5");
                if (enc.ParkedNow != ParkedState.Gone || train.Standing || train.Cart.CollisionLayer != 0 || train.Wagons.Any(w => w.Visible || w.CollisionLayer != 0)) problems.Add($"it is {enc.ParkedNow}, standing={train.Standing}, its bodies still shown or solid");
                if (enc.ParkedSmashes != 1) problems.Add($"{enc.ParkedSmashes} smash(es) counted");
                if (_ipSmashSounds != 1) problems.Add($"timber_smash played {_ipSmashSounds} time(s) at its lantern, not once");
                var racer = _ipRacer!;
                var racerCell = DungeonBuilder.WorldToCell(racer.GlobalPosition);
                if (racer.Seated || !CrevasseRules.SetDownOk(layout, racerCell) || layout.InIsland(racerCell) || racer.State.Hp < 1f || racer.State.Fallen) problems.Add($"{racer.State.Name} (the race) seated={racer.Seated} at {racerCell} (may take a body {CrevasseRules.SetDownOk(layout, racerCell)}, island {layout.InIsland(racerCell)}) HP {racer.State.Hp:0.0} fallen={racer.State.Fallen}");
                GD.Print($"[island] parked: smashed (the call {_ipCallGap:0.0} m from its rear, alarm {_ipAlarmSeen}, bell {_ipBellSeen}): plating {_ipPlatingBefore:0.0} -> {rc.State.Plating:0.0} (-{drop:0.0}); v {_ipVBefore:0.00} -> {rc.State.V:0.00} (x{kept:0.00}); heroes [{string.Join(", ", lines)}]; crew [{string.Join(", ", crewLines)}]; {tumbles} tumbling; {racer.State.Name} thrown clear onto {racerCell}; {enc.ParkedNow}");
                if (problems.Count > 0) { Fail($"the smash: {string.Join("; ", problems)}"); return; }
                _ipRacerWatch = 0f;
                _isStep++; _isClock = 0f;
                break;
            }
            case 6:
            {
                // the race's rider stays up on the bank (never fallen), and the train goes on past the far edge: a lift within 1 s
                if (_ipRacer is { } racer && _ipRacerWatch < 3f)
                {
                    _ipRacerWatch += (float)GetProcessDeltaTime();
                    if (racer.State.Fallen || racer.State.Hp < 1f) { Fail($"{racer.State.Name}, thrown clear, fell or dropped under 1 HP (HP {racer.State.Hp:0.0}) at {DungeonBuilder.WorldToCell(racer.GlobalPosition)}"); return; }
                }
                if (rc.State.Mode == CartMode.Wrecked) { Fail($"the train was wrecked after the smash at S={rc.State.S:0.0}"); return; }
                if (rc.State.Mode == CartMode.Rolling && rc.State.Plating < CartRules.MaxPlating) rc.DebugSetPlating(CartRules.MaxPlating); // past the smash the flow owns the plating again
                var rear = RunawayRules.TrainRearS(rc.Party.BackS, rc.Train.Count, RailController.WagonSpacing); // the train's rear as the controller reads it
                if (float.IsNaN(_ipRearPassT))
                {
                    if (rear > enc.ExitToS) { _ipRearPassT = _isTime; GD.Print($"[island] parked: the train's rear passed the far edge at S={rear:0.0} (ExitToS {enc.ExitToS:0.0})"); }
                    else if (enc.ParkedNow != ParkedState.Gone) { Fail($"before the train's rear passed the far edge (rear S={rear:0.0}, the far edge {enc.ExitToS:0.0}) it is {enc.ParkedNow}"); return; }
                    else { if (_isClock > 90f) Fail($"the train's rear never passed the far edge (rear S={rear:0.0})"); break; }
                }
                var since = _isTime - _ipRearPassT;
                if (enc.ParkedNow != ParkedState.Lift || float.IsNaN(_ipLiftLogT))
                {
                    if (since > 1f) Fail($"{since:0.00} s after the rear passed the far edge it is {enc.ParkedNow} (\"{ParkedRules.LiftLog}\" logged={!float.IsNaN(_ipLiftLogT)})");
                    break;
                }
                if (!train.Standing || train.Cart.CollisionLayer != ParkedTrain.StandingLayers) { Fail("a lift, but its bodies are not standing solid"); return; }
                GD.Print($"[island] parked: a lift {Mathf.Max(0f, _ipLiftLogT - _ipRearPassT):0.00} s after the rear passed the far edge, \"{ParkedRules.LiftLog}\" logged; standing solid at cart S={train.CartS:0.0}");
                _isStep++; _isClock = 0f;
                break;
            }
            case 7:
            {
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != shape.Rides) { if (_isClock > 60f) Fail($"the train did not arrive at stop {shape.Rides} (mode {rc.State.Mode}, stop {rc.StopIndex})"); break; }
                if (enc.ParkedNow != ParkedState.Lift) { Fail($"held at the last stop it is {enc.ParkedNow}, not a lift"); return; }
                rc.Restore(shape.LastRideStop);
                _isStep++; _isClock = 0f;
                break;
            }
            case 8 when _isClock >= 0.5f:
            {
                if (enc.ParkedNow != ParkedState.InTheWay || !train.Standing || rc.StopIndex != shape.LastRideStop) { Fail($"after rc.Restore({shape.LastRideStop}) it is {enc.ParkedNow}, standing={train.Standing}, the cart at stop {rc.StopIndex}"); return; }
                // ridden again with every spare spent, the smash wrecks the cart
                rc.DebugSpendSpares();
                _ipSmashesBefore = enc.ParkedSmashes;
                GD.Print($"[island] parked: in the way again after the restore; every spare spent ({rc.SparesLeft} left): the last ride again, its smash to wreck the cart");
                _isStep++; _isClock = 0f;
                break;
            }
            case 9 when _isClock >= 0.5f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _isStep++; _isClock = 0f;
                break;
            }
            case 10:
            {
                if (enc.ParkedSmashes == _ipSmashesBefore)
                {
                    if (rc.State.Mode == CartMode.Wrecked) { Fail($"the last ride was wrecked at S={rc.State.S:0.0} before the smash"); return; }
                    if (rc.State.Mode == CartMode.Rolling) rc.DebugSetPlating(rc.Party.FrontS < train.RearFaceS - 3f ? CartRules.MaxPlating : ParkedRules.SmashDent); // the smash's dent wrecks it
                    _ipTold.Clear(); // only the words from the smash on are judged
                    if (_isClock > 120f) Fail($"the cart never smashed the parked train again (front {rc.Party.FrontS:0.0}, its rear {train.RearFaceS:0.0}, {enc.ParkedNow})");
                    break;
                }
                var tumbles = root.GetChildren().Count(n => n is RigidBody3D && n.Name.ToString().StartsWith(ParkedTrain.TumbleName, StringComparison.Ordinal));
                var problems = new List<string>();
                if (rc.State.Mode != CartMode.Wrecked || rc.Cart.Visible || rc.LastWreckInside || rc.SparesLeft != 0) problems.Add($"the cart is {rc.State.Mode}, shown {rc.Cart.Visible}, inside {rc.LastWreckInside}, {rc.SparesLeft} spare(s) left (want a no-spare wreck at the smash)");
                if (enc.ParkedNow != ParkedState.Gone || enc.ParkedWhy != "the smash" || train.Standing) problems.Add($"the parked train is {enc.ParkedNow} (\"{enc.ParkedWhy}\"), standing={train.Standing}, not Gone by the smash");
                if (tumbles != 5) problems.Add($"{tumbles} tub(s) tumbling, not 5");
                if (!_ipTold.Contains("smashed") || _ipTold.Contains("standing")) problems.Add($"told [{string.Join(", ", _ipTold)}], not \"smashed\" without \"standing\"");
                if (enc.ParkedTumbleLeft < ParkedRules.TumbleSeconds - 0.5f) problems.Add($"its tubs tumble {enc.ParkedTumbleLeft:0.00} s more, not about {ParkedRules.TumbleSeconds:0} s");
                GD.Print($"[island] parked: the smash wrecked the cart: {rc.State.Mode} at S={rc.State.S:0.0}, shown {rc.Cart.Visible}, inside {rc.LastWreckInside}, {rc.SparesLeft} spare(s) left; the parked train {enc.ParkedNow} (\"{enc.ParkedWhy}\"), {tumbles} tumbling for {enc.ParkedTumbleLeft:0.00} s more; told [{string.Join(", ", _ipTold)}]");
                if (problems.Count > 0) { Fail($"the smash that wrecks the cart: {string.Join("; ", problems)}"); return; }
                _ipWreckSmashT = _isTime;
                _isStep++; _isClock = 0f;
                break;
            }
            case 11:
            {
                var since = _isTime - _ipWreckSmashT;
                if (enc.ParkedNow != ParkedState.Lift)
                {
                    if (enc.ParkedNow != ParkedState.Gone) { Fail($"{since:0.00} s after the smash the parked train is {enc.ParkedNow}, not Gone"); return; }
                    if (_ipTold.Contains("standing")) { Fail($"{since:0.00} s after the smash \"standing\" was told while it is Gone"); return; }
                    if (enc.ParkedTumbleLeft <= 0f && since > ParkedRules.TumbleSeconds + 1f) Fail($"{since:0.00} s after the smash, its tubs gone, there is no lift");
                    break;
                }
                var problems = new List<string>();
                if (enc.ParkedTumbleLeft > 0f || since < ParkedRules.TumbleSeconds - 0.1f) problems.Add($"a lift {since:0.00} s after the smash with its tubs tumbling {enc.ParkedTumbleLeft:0.00} s more");
                if (enc.ParkedWhy != "a lift is due") problems.Add($"its reason \"{enc.ParkedWhy}\", not \"a lift is due\"");
                var smashedAt = _ipTold.IndexOf("smashed");
                var standingAt = _ipTold.IndexOf("standing");
                if (smashedAt < 0 || standingAt < smashedAt) problems.Add($"told [{string.Join(", ", _ipTold)}]: \"standing\" not after \"smashed\"");
                if (!train.Standing || train.Cart.CollisionLayer != ParkedTrain.StandingLayers) problems.Add("a lift, but its bodies are not standing solid");
                if (_ipLiftLogs != 2 || _ipSmashSounds != 2) problems.Add($"\"{ParkedRules.LiftLog}\" logged {_ipLiftLogs} time(s) and timber_smash played {_ipSmashSounds} time(s) over the two rides, not twice each (once a ride: the restore between lets the line come again)");
                GD.Print($"[island] parked: a lift {since:0.00} s after the smash that wrecked the cart (\"{enc.ParkedWhy}\"); told [{string.Join(", ", _ipTold)}]; \"{ParkedRules.LiftLog}\" logged {_ipLiftLogs} time(s), timber_smash {_ipSmashSounds} time(s)");
                if (problems.Count > 0) { Fail($"after the smash that wrecked the cart: {string.Join("; ", problems)}"); return; }
                Pass($"the build: {enc.ParkedId}'s rear at RearS(ExitFromS, 4) (S {train.RearFaceS:0.0}), its five bodies solid, its tubs never the party's; in the way at stop {shape.LastRideStop}, held, the lead beside it offered nothing, and no parked tub offered a revive, a Get out or a climb, nor took a press as a move; its lantern seen from the seat 15 m short; called {_ipCallGap:0.0} m from its rear, its alarm on and its bell rung; {_ipRacer!.State.Name}, seated in it, thrown clear onto a cell that may take a body; the smash: plating -25, each other seated up hero -15% of max HP (W1's {_ipWagonHero!.State.Name} too; {_ipLow!.State.Name} at 5% left with {_ipLow.State.Hp:0.0} HP), the crew unhurt, V x0.8, five tubs tumbling, Gone; a lift within 1 s of the rear passing the far edge; a restore stood it in the way again; with no spare left a smash that wrecked the cart was told \"smashed\" with five tubs tumbling, the place Gone for their {ParkedRules.TumbleSeconds:0} s, then \"standing\" ({since:0.00} s after the smash, \"{enc.ParkedWhy}\")");
                return;
            }
        }
    }

    /// <summary>
    /// The guard's pin (no parked tub is the party's), at the stop: the lead seated in the parked train's W1 is offered nothing (no Get out, no move), is in no car
    /// of the train, and a press on the party's cart from there is no move; a crew member downed in its W2 offers the lead no revive and is not
    /// in the party's train. Everyone goes back where they were. Null, or what broke.
    /// </summary>
    private string? IslandParkedGuard(RailController rc, RailEncounterController enc, ParkedTrain train, Adventurer me)
    {
        if (ObjectiveController.Instance is not CrewRescueController ctrl || _ipCrew.Count == 0) return "no crew to down in a parked tub";
        var w1 = train.Wagons[0];
        var w2 = train.Wagons[1];
        var stand = me.GlobalPosition;
        if (!w1.Seats.Put(me.RiderId, TrainRules.HeroPlace)) return "the lead could not sit in the parked W1";
        me.SitIn(w1, TrainRules.HeroPlace);
        var npc = _ipCrew[0];
        npc.GetOut(npc.GlobalPosition);
        if (!w2.Seats.Put(npc.RiderId, TrainRules.CrewSeats[0])) return $"{npc.NpcName} could not sit in the parked W2";
        npc.SitIn(w2, TrainRules.CrewSeats[0]);
        npc.GoDown();
        var problems = new List<string>();
        if (w1.Prompt != "" || rc.PlainPromptOf(w1, me) != null) problems.Add($"seated in its W1 the lead is offered \"{w1.Prompt}\" (plain {rc.PlainPromptOf(w1, me) ?? "none"})");
        if (rc.CarOf(me) != -1 || rc.MoveOfferFor(me) != null) problems.Add($"seated in its W1 the lead is in car {rc.CarOf(me)}, offered a move to {(rc.MoveOfferFor(me) as Node)?.Name ?? "nothing"}");
        if (rc.IsMoveAsk(me, rc.Cart) || rc.IsMoveAsk(me, w2)) problems.Add("from its W1 a press on the party's cart or its W2 is a move ask");
        if (w2.Prompt != "" || rc.PlainPromptOf(w2, me) != null) problems.Add($"with {npc.NpcName} downed in its W2 the lead is offered \"{w2.Prompt}\" (plain {rc.PlainPromptOf(w2, me) ?? "none"})");
        if (rc.CarCarrying(w2) != -1) problems.Add($"its W2 reads as car {rc.CarCarrying(w2)} of the train");
        GD.Print($"[island] parked: the guard (F7): the lead in its W1 offered \"{w1.Prompt}\" (car {rc.CarOf(me)}, move {(rc.MoveOfferFor(me) as Node)?.Name ?? "none"}); {npc.NpcName} downed in its W2 (revive taken {npc.ReviveTaken}) offers \"{w2.Prompt}\" (plain {rc.PlainPromptOf(w2, me) ?? "none"})");
        npc.TryRevive();
        npc.GetOut(npc.GlobalPosition);
        me.GetOut(stand, Vector3.Zero);
        if (ctrl.DebugSeatCrew(2, wagon: 2) is not { Count: > 0 }) problems.Add($"{npc.NpcName} could not be seated back in the party's W2");
        if (npc.Cart != rc.WagonAt(2)) problems.Add($"{npc.NpcName} is not back in the party's W2 ({(npc.Cart as Node)?.Name ?? "on foot"})");
        return problems.Count > 0 ? $"the guard: {string.Join("; ", problems)}" : null;
    }
}
