using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders[=move|reach|knock|throw|lone|pause|groups] (plan 14, Tasks 3-7) ------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo; every enemy is culled as it appears but the hounds the flow puts on wagons
// (RailEncounterController.DebugBoard), and the flow keeps the party's HP up. The variants are plan 14's (Ruling 22): =move (Task 3), =reach
// and =knock (Task 4) are built; throw, lone, pause and groups are later tasks' and, like an unknown name, print an ERROR and fail.
// =move (the default): the cart is restored at stop 0 (rc.Restore(0)), everyone boards and the train departs on ride 1. The lead's view is
// turned by its seat yaw (SeatYaw) and every press is the VR grip's (party.VrUse, as E reaches LookingAt.Interact).
//  - The first move: looking back 120 degrees the prompt is "Climb back to wagon 1"; the press leaves the lead in the cart for 0.6 s within one
//    physics tick (counted in the rail controller's physics clock), then in W1 seat 0, and the log says "{lead} climbs back to wagon 1.".
//  - The cooldown: a second press 0.3 s after the first does nothing (no move taken; still in W1 after the landing, not on foot). The next move
//    is offered again at the cooldown (1 s after the first press, within the frames' slack), "Climb back to wagon 2", and taken.
//  - Passing: a companion is seated in W3's place (rc.DebugSeat(mate, 3)), so the lead's next back move from W2 goes to W4: one move.
//  - Forward: from W4, looking forward, "Climb forward to wagon 2", then "... wagon 1", then "Climb forward into the cart", each taken.
//  - The beam: the lead climbs back to W2 (past W1) and rides there under r1_beam; every seated head in the train clears it by 0.05 m or more,
//    the lead's in W2 among them.
//  - The double press (Review Focus 1): from W2 the lead's press forward and a cart companion's forced rc.Move(mate, W1, "test") in one frame
//    both ask for W1's free place: the press books it and lands; the forced move is refused (the place is booked).
//  - The mid-hop down (Review Focus 2): from W1 the lead presses back toward W2 and is downed (Mangled) 0.2 s into the hop: the booking is
//    dropped, the lead stays slumped in W1 seat 0, W2's place is free again, and it never lands; no move is offered while downed. Revived.
//  - Never offered: at the arrival at stop 1 (held: W1's prompt is "Get out"), while downed (above), on foot (out of W1 at stop 1).
//  - Fix round 1: at the restore, a point 4.1 m end-on from W1's middle reads 3.0 m from its tub (TrainWagon.TubDistance, the server's reach);
//    in the double press a companion's real press on the booked W1 (W1.Interact) is refused out loud as a move, with no plain use of the wagon;
//    at stop 1, with the lead in W1, a companion set on foot beside W3 (more than 3 m from the cart) hops in by its own brain.
// =reach and =knock are restored at stop 1 with its cage opened by the test's hook (DebugSeatCrew: its crew sit in W1 at once).
// =reach (Task 4, Ruling 13): restored at stop 1; on ride 2 the lead sits in W2 (rc.DebugSeat) and hounds that never drop below 1 HP are put on W1,
// W2 and W3; companions hold fire and nobody answers (DebugHoldAnswers).
//  - The reach: from W2's seat 0 to each rim spot, read off the wagons' poses every frame (the bodies' own spread printed beside it), on the straight and in bends: printed as "[boarders] reach: W2 seat 0
//    to its own spot ... m (straight), ... m (in a bend); to W1's spot ... m, to W3's ... m" (the nearest each neighbour came); BoarderRules.WagonReach
//    must be at least the larger own distance + 0.15 and under TrainRules.Spacing.
//  - The lead's forced blows: every melee attack its class has (the weapon's light, heavy and perfect swing, each melee strike ability), aimed at
//    each hound in turn, refilled before each: W1's and W3's hounds never lose HP or grip and take no hit at all; W2's takes the blows that land (a
//    W2 hound knocked off by its grip is replaced by a fresh one).
//  - A carried blow (fix round 1, review M6): a cleave off W2's hound (Adventurer.DebugCleave), widened past both neighbours' hounds, takes neither.
//  - From the cart: a blade companion seated in the cart strikes at W1's hound three times (Adventurer.DebugStrikeAt): it never lands. Then
//    the same companion, seated in W3, strikes at W2's hound (nearer than WagonReach) three times: it never lands either (the own-car rule).
// =knock (Task 4, Rulings 14-15): restored at stop 1, rolling on ride 2; the lead climbs back into W1 by the grip's press.
//  - Task 3 review M1: at speed, after the move's grace, W1's own rider's prompt is "" (no Get out under Ruling 9); and in the first seated lever
//    window of an armed ride (ride 3's, the flow rides on to it) the lead's move is refused ("a lever window is open") and none is offered.
//  - Pass 1: the lead lands three melee blows on a W1 hound held above 1 HP: grip 1, 2, then knocked off after 3 hits, standing beside the lane
//    alive, not carried, aggro on. Pass 2: a caster companion's spell knocks a fresh W1 hound off with its one hit.
//  - Pass 3 (with 100 m of the ride left): stops 2-3's crew are seated (DebugSeatCrew: W2, W3; stop 1's sit in W1). Ruling 15 as amended in
//    fix round 1: 3a, a hound held above 1 HP on W3 is answered from its seat by a cart companion with a clear shot (the only one let fight):
//    nobody moves, and its shots knock the hound off by the grip. 3b, with every companion that has a shot held down, a fresh W3 hound is
//    answered by the nearest cart blade climbing one move per cooldown (passing the lead's W1); it reaches W3, strikes the hound (its hit
//    counted toward the grip) until it is knocked off or killed, and 5 s after W3 is clear moves back toward the cart; the shooters stand again.
//  - Pass 4: DebugKill of a W1 hound drops it beside the lane.
// Prints [boarders] lines and a RESULT.
public partial class DebugTools
{
    private bool _testBoarders;
    private string _bdVariant = "move";
    private int _bdStep, _bdPhase;
    private float _bdClock, _bdPhaseT;
    private bool _bdListening, _bdLeadUnderBeam, _bdSecondPressed;
    private readonly List<string> _bdLog = new();
    private readonly List<string> _bdSaid = new();
    private readonly BeamHeads _bdHeads = new();
    private int _bdMovesBefore;
    private float _bdHopNearest;
    private Adventurer? _bdMate3, _bdMate2;
    // =reach and =knock (Task 4)
    private readonly Dictionary<Enemies.Enemy, List<(int By, Combat.AttackKind Kind, int Grip, float At)>> _bdHurt = new();
    private Enemies.Enemy? _bdH1, _bdH2, _bdH3;
    private readonly List<Enemies.Enemy> _bdW2Hounds = new();
    private readonly List<(int Index, bool Heavy, bool Perfect, string Name)> _bdBlows = new();
    private int _bdBlow, _bdKnockBase, _bdSwings;
    private float _bdBlowAt, _bdHp1, _bdHp3, _bdClearAt = -1f;
    private float _bdOwnStraight = -1f, _bdOwnBend = -1f, _bdToW1 = float.MaxValue, _bdToW3 = float.MaxValue, _bdMaxBend;
    private float _bdOwnMin = float.MaxValue, _bdBodyMin = float.MaxValue, _bdBodyMax = -1f;
    private Adventurer? _bdMate;
    private Enemies.Enemy? _bdHound;
    private bool _bdLeverChecked, _bdWasHopping;
    private readonly List<(float At, int From, int To)> _bdAnswerHops = new();
    private readonly List<Adventurer> _bdShootersDown = new(); // pass 3b: the seat answerers made to pump, in turn
    private float _bdHandoffAt = -1f;
    /// <summary>A wagon's track curvature (1/m) from which the reach sample counts as "in a bend" (a radius of 25 m or less); under 0.01 is the straight.</summary>
    private const float BdBendK = 0.04f;

    /// <summary>The variants plan 14 names for this flow (Ruling 22), and the ones built so far.</summary>
    private static readonly string[] BoardersVariants = { "move", "reach", "knock", "throw", "lone", "pause", "groups" };
    private static readonly string[] BoardersBuilt = { "move", "reach", "knock" };

    private void RunBoardersTest()
    {
        var v = _bdVariant;
        void Fail(string why) { GD.Print($"[boarders] RESULT FAIL ({v}): {why}"); if (Main.Instance?.Party is { } p) { p.AllowPossession = true; p.ForcedMoveInput = null; } _done = true; GetTree().Quit(1); }
        if (!BoardersBuilt.Contains(v))
        {
            GD.PrintErr($"[boarders] ERROR: --test-boarders={v} is {(BoardersVariants.Contains(v) ? "not built yet" : "not a variant")} (the variants: {string.Join(", ", BoardersVariants)}; built: {string.Join(", ", BoardersBuilt)})");
            Fail($"the variant {v} is {(BoardersVariants.Contains(v) ? "not built yet" : "unknown")}");
            return;
        }
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        var dt = (float)GetProcessDeltaTime();
        _bdClock += dt;
        var party = main.Party;
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_bdClock > 3f) Fail("floor 12 has no line"); return; }
        BoardersListen();
        BdReadGrips(enc);
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive && !enc.IsDebugBoarder(e)) e.Kill(); // the flow owns the plating and the HP
        if (v != "move") foreach (var a in party.Adventurers) if (IsInstanceValid(a) && !a.Parked && !a.Downed) a.State.Hp = a.State.Stats[Characters.Stat.MaxHp]; // the hounds bite: nobody goes down
        enc.RunawayClock = 0f; // no traffic runaway on the party line
        if (party.Local is not { } me) { Fail("no human"); return; }

        switch (_bdStep)
        {
            case 0 when _bdClock >= 1.5f:
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(v == "move" ? 0 : 1);
                GD.Print($"[boarders] floor 12 ({v}): restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Number))}]; lethality {TierRules.Lethality}");
                if (rc.Train.Count != 4) { Fail($"the train has {rc.Train.Count} wagons, not 4"); return; }
                if (v != "move")
                {
                    // stop 1's cage holds the cart while locked (CageHoldRules): opened by the test's hook, its crew seated in W1 at once
                    if (ObjectiveController.Instance is not CrewRescueController ctrl || ctrl.DebugSeatCrew(1) is not { Count: > 0 } crew) { Fail("stop 1's crew could not be seated"); return; }
                    GD.Print($"[boarders] stop 1's cage opened by the test's hook: [{string.Join(", ", crew.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}]");
                }
                {
                    // the server's reach for a use of a wagon (fix round 1, I3): measured to its tub, as a cart's; a point 4.1 m end-on from
                    // W1's middle, at the tub's height, is 3.0 m off its box (inside the 3.5 m reach), where its origin is 4.1 m off (outside)
                    var w1 = rc.WagonAt(1)!;
                    var endOn = w1.GlobalTransform * new Vector3(0f, 1f, 4.1f);
                    var tub = w1.TubDistance(endOn);
                    var origin = w1.GlobalPosition.DistanceTo(endOn);
                    GD.Print($"[boarders] reach: a point 4.1 m end-on from W1's middle reads {tub:0.000} m from its tub (origin {origin:0.000} m; reach {PropPromptRules.InteractReach} m)");
                    if (Mathf.Abs(tub - 3.0f) > 0.01f || tub > PropPromptRules.InteractReach || origin <= PropPromptRules.InteractReach) { Fail($"W1's tub reads {tub:0.000} m for a point 4.1 m end-on, not 3.0"); return; }
                }
                _bdStep++; _bdClock = 0f;
                break;
            case 1 when _bdClock >= 1f:
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _bdStep++; _bdClock = 0f;
                break;
            case 2:
            {
                var heroes = party.Active.Count();
                var seated = party.Active.Count(a => a.Cart == rc.Cart);
                if ((!me.Seated || seated < heroes) && _bdClock < 3f) break;
                if (me.Cart != rc.Cart || seated < 3) { Fail($"the lead seated in the cart {me.Cart == rc.Cart}, {seated} of {heroes} heroes in it (3 needed)"); return; }
                party.AllowPossession = false; // the mid-hop down keeps the lead the human
                GD.Print($"[boarders] boarded at stop {rc.StopIndex}: {seated} of {heroes} heroes in the cart");
                _bdStep++; _bdClock = 0f;
                break;
            }
            case 3:
            {
                var ride = v == "move" ? 1 : 2;
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != ride || rc.State.V < 1f || _bdClock < 1f) { if (_bdClock > 20f) Fail($"ride {ride} did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                GD.Print($"[boarders] rolling on ride {ride} at S={rc.State.S:0.0} (v {rc.State.V:0.00}); moves refused {rc.MovesRefused}");
                _bdStep++; _bdClock = 0f; _bdPhase = 0; _bdPhaseT = 0f;
                break;
            }
            case 4:
                if (v == "reach") BoardersReach(rc, enc, party, me, Fail);
                else if (v == "knock") BoardersKnock(rc, enc, party, me, Fail);
                else BoardersMove(rc, enc, party, me, Fail);
                break;
        }
    }

    /// <summary>The landing and refusal lines, heard once per run.</summary>
    private void BoardersListen()
    {
        if (_bdListening) return;
        _bdListening = true;
        EventBus.Instance.EnemyHurt += (e, _, _, by) =>
        {
            if (_done || !_testBoarders || RailEncounterController.Instance is not { } enc || !enc.IsDebugBoarder(e)) return;
            if (!_bdHurt.TryGetValue(e, out var list)) _bdHurt[e] = list = new();
            list.Add((by, e.LastHitKind, BdGripUnread, _bdClock)); // the grip is read after the controller has counted the hit (BdReadGrips)
        };
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testBoarders || !text.Contains(" climbs ", StringComparison.Ordinal)) return;
            _bdSaid.Add(text);
            GD.Print($"[boarders] logged: \"{text}\"");
        };
    }

    /// <summary>The seat yaw that looks back over the shoulder (120 degrees) and the one that looks forward.</summary>
    private static readonly float BdBack = Mathf.DegToRad(120f), BdForward = 0f;

    /// <summary>=move's script on ride 1 (one phase at a time; each waits a little for the view tick to read a new yaw).</summary>
    private void BoardersMove(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT; // since the phase began
        var tick = 1f / Engine.PhysicsTicksPerSecond;
        TrainWagon W(int k) => rc.WagonAt(k)!;
        string Where(Adventurer a) => $"car {rc.CarOf(a)} seat {a.Seat}";
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        if (rc.State.Mode == CartMode.Wrecked) { Fail("the train was wrecked"); return; }
        if (_bdPhase < 80 && rc.State.Mode == CartMode.Held) { Fail($"the train reached stop {rc.StopIndex} at phase {_bdPhase}, before the script was done"); return; }
        BoardersBeam(rc, enc, me, Fail);
        if (_done) return;

        // a move from the lead's seat, offered on `to` with `prompt`, pressed with the grip: true once pressed (false while waiting, or failed)
        bool Press(ICartBody to, string prompt)
        {
            if (t < 0.15f) return false;
            var offer = rc.MoveOfferFor(me);
            var said = to is TrainWagon w ? w.Prompt : rc.Cart.Prompt;
            if (offer != to || !ReferenceEquals(me.LookingAt, to) || said != prompt)
            {
                if (t > 1.6f) Fail($"from {Where(me)} at yaw {Mathf.RadToDeg(me.SeatYaw):0}: offered {(offer is TrainWagon ow ? $"wagon {ow.Number}" : offer == null ? "nothing" : "the cart")}, looking at {me.LookingAt?.GetType().Name ?? "nothing"}, prompt \"{said}\" (wanted \"{prompt}\")");
                return false;
            }
            _bdMovesBefore = rc.MovesTaken;
            party.VrUse(true);
            party.VrUse(false);
            if (rc.MovesTaken != _bdMovesBefore + 1 || !rc.Hopping(me)) { Fail($"the press on \"{prompt}\" took no move (refused: {rc.LastMoveRefused ?? "-"})"); return false; }
            GD.Print($"[boarders] {me.State.Name} pressed \"{prompt}\" from {Where(me)} at S={rc.State.S:0.0}");
            return true;
        }
        // the hop under way, then its landing in car `car` seat `seat`: true once landed and judged
        bool Landed(int fromCar, int car, string log)
        {
            if (rc.Hopping(me))
            {
                if (rc.CarOf(me) != fromCar) { Fail($"mid-hop the lead is in {Where(me)}, not still in car {fromCar}"); return false; }
                if (t > 2f) Fail("the hop never landed");
                return false;
            }
            var seat = car == 0 ? me.Seat : TrainRules.HeroPlace;
            if (rc.CarOf(me) != car || me.Seat != seat || rc.LastMoveLanded is not { } l || l.Car != car) { Fail($"the hop landed the lead in {Where(me)}, not car {car}"); return false; }
            if (Mathf.Abs((float)l.After - TrainMoveRules.HopSeconds) > tick + 1e-4f) { Fail($"the hop took {l.After:0.000} s, not {TrainMoveRules.HopSeconds} s within one tick ({tick:0.000})"); return false; }
            if (_bdSaid.Count == 0 || _bdSaid[^1] != log) { Fail($"the landing logged \"{(_bdSaid.Count > 0 ? _bdSaid[^1] : "")}\", not \"{log}\""); return false; }
            GD.Print($"[boarders] landed in {Where(me)} after {l.After:0.000} s (a tick {tick:0.000} s): \"{log}\"");
            return true;
        }

        var name = me.State.Name;
        switch (_bdPhase)
        {
            // the first move, cart -> W1
            case 0: me.SeatYaw = BdBack; Next(1); break;
            case 1: if (Press(W(1), "Climb back to wagon 1")) { _bdSecondPressed = false; Next(2); } break;
            case 2:
                if (!_bdSecondPressed && t >= 0.3f)
                {
                    _bdSecondPressed = true; // the cooldown: a second press 0.3 s after the first does nothing
                    var before = rc.MovesTaken;
                    var looking = me.LookingAt;
                    party.VrUse(true);
                    party.VrUse(false);
                    GD.Print($"[boarders] a second press {t:0.00} s after the first: looking at {looking?.GetType().Name ?? "nothing"}, moves taken {rc.MovesTaken - before}, the lead in {Where(me)}");
                    if (looking != null || rc.MovesTaken != before || !me.Seated) { Fail("a second press 0.3 s after the first did something"); return; }
                }
                if (!Landed(0, 1, $"{name} climbs back to wagon 1.")) break;
                if (!_bdSecondPressed) { Fail("the hop landed before the second press at 0.3 s"); return; }
                Next(3);
                break;
            case 3:
            {
                // the cooldown ends: offered again 1 s after the first press (the phase began 0.6 s in, at the landing)
                var since = t + (float)(rc.LastMoveLanded?.After ?? 0);
                if (rc.MoveOfferFor(me) == null) { if (since > 1.3f) Fail($"no move offered {since:0.00} s after the first press"); break; }
                GD.Print($"[boarders] the cooldown: a move offered again {since:0.00} s after the first press, the lead still in {Where(me)}, seated {me.Seated}");
                if (since < TrainMoveRules.MoveCooldown - 0.05f || rc.CarOf(me) != 1) { Fail($"offered {since:0.00} s after the press, under the {TrainMoveRules.MoveCooldown} s cooldown"); return; }
                Next(4); _bdPhaseT -= 1f; // press at once
                break;
            }
            case 4: if (Press(W(2), "Climb back to wagon 2")) Next(5); break;
            case 5: if (Landed(1, 2, $"{name} climbs back to wagon 2.")) Next(6); break;

            // passing: a companion in W3's place, the lead's back move from W2 lands in W4 in one move
            case 6:
            {
                if (t < TrainMoveRules.MoveCooldown) break;
                _bdMate3 = party.Active.FirstOrDefault(a => a != me && a.Cart == rc.Cart && !a.Downed);
                _bdMate2 = party.Active.FirstOrDefault(a => a != me && a != _bdMate3 && a.Cart == rc.Cart && !a.Downed);
                if (_bdMate3 == null || _bdMate2 == null) { Fail("two companions in the cart are needed"); return; }
                if (!rc.DebugSeat(_bdMate3, 3)) { Fail($"{_bdMate3.State.Name} could not be seated in W3"); return; }
                GD.Print($"[boarders] passing: {_bdMate3.State.Name} seated in {Where(_bdMate3)}");
                Next(7);
                break;
            }
            case 7: if (Press(W(4), "Climb back to wagon 4")) { _bdMovesBefore = rc.MovesTaken; Next(8); } break;
            case 8:
                if (!Landed(2, 4, $"{name} climbs back to wagon 4.")) break;
                if (rc.MovesTaken != _bdMovesBefore) { Fail("passing took more than one move"); return; }
                GD.Print($"[boarders] passing: from W2 past {_bdMate3!.State.Name} in W3 to W4 in one move");
                Next(9);
                break;

            // forward: W4 -> W2 (past W3) -> W1 -> the cart
            case 9: me.SeatYaw = BdForward; Next(10); break;
            case 10: if (t >= TrainMoveRules.MoveCooldown - 0.6f && Press(W(2), "Climb forward to wagon 2")) Next(11); break;
            case 11: if (Landed(4, 2, $"{name} climbs forward to wagon 2.")) Next(12); break;
            case 12: if (t >= 0.4f && Press(W(1), "Climb forward to wagon 1")) Next(13); break;
            case 13: if (Landed(2, 1, $"{name} climbs forward to wagon 1.")) Next(14); break;
            case 14: if (t >= 0.4f && Press(rc.Cart, "Climb forward into the cart")) Next(15); break;
            case 15: if (Landed(1, 0, $"{name} climbs forward into the cart.")) { me.SeatYaw = BdBack; Next(16); } break;

            // the beam: back to W2 (W1, then W2), ridden under r1_beam
            case 16: if (t >= 0.4f && Press(W(1), "Climb back to wagon 1")) Next(17); break;
            case 17: if (Landed(0, 1, $"{name} climbs back to wagon 1.")) Next(18); break;
            case 18: if (t >= 0.4f && Press(W(2), "Climb back to wagon 2")) Next(19); break;
            case 19:
                if (!Landed(1, 2, $"{name} climbs back to wagon 2.")) break;
                if (enc.SetPieceNode("r1_beam") is LowBeam beam && rc.Party.FrontS >= beam.S - 1f) { Fail($"the lead reached W2 with the front at S={rc.Party.FrontS:0.0}, already at r1_beam (S={beam.S:0.0})"); return; }
                GD.Print($"[boarders] the beam: the lead rides in {Where(me)} toward r1_beam, the front at S={rc.Party.FrontS:0.0}");
                Next(20);
                break;
            case 20:
                if (!_bdHeads.Done) { if (rc.CarOf(me) != 2) { Fail($"the lead left W2 before the beam ({Where(me)})"); return; } break; }
                if (!_bdLeadUnderBeam) { Fail("the lead was never measured under r1_beam in W2"); return; }
                me.SeatYaw = BdForward;
                Next(21);
                break;

            // the double press: the lead's press forward to W1, then in the same frame a cart companion's press on W1 through the real use
            // (fix round 1, I1: judged as a move and refused out loud, never the wagon's plain use) and its forced move to W1
            case 21:
            {
                if (t < 0.15f) break;
                if (rc.MoveOfferFor(me) != W(1)) { if (t > 1.6f) Fail("from W2 looking forward no move to W1 was offered"); break; }
                var mate = _bdMate2!;
                party.VrUse(true);
                party.VrUse(false);
                var pressed = rc.Hopping(me);
                int refusedBefore = rc.MovesRefusedCount, plainBefore = rc.WagonPlainUses, takenBefore = rc.MovesTaken;
                W(1).Interact(party, mate); // the companion's own press on W1, as a key or an intent reaches it (OnUse)
                var pressRefused = rc.LastMoveRefused;
                GD.Print($"[boarders] the double press: {mate.State.Name}'s press on W1 from {Where(mate)}: refused out loud {rc.MovesRefusedCount - refusedBefore} ({pressRefused ?? "-"}), plain uses {rc.WagonPlainUses - plainBefore}, moves taken {rc.MovesTaken - takenBefore}");
                if (rc.MovesRefusedCount != refusedBefore + 1 || pressRefused != "the place is booked" || rc.WagonPlainUses != plainBefore || rc.MovesTaken != takenBefore || rc.CarOf(mate) != 0)
                { Fail("the double press: the second press for the booked place was not refused out loud as a move (or fell into the wagon's plain use)"); return; }
                var forced = rc.Move(mate, W(1), "test");
                var refused = rc.LastMoveRefused;
                GD.Print($"[boarders] the double press: the lead's press took {pressed}; {mate.State.Name}'s forced move from {Where(mate)} to W1 taken={forced} (refused: {refused ?? "-"})");
                if (!pressed || forced || refused != "the place is booked") { Fail("the double press: the first did not book W1, or the second was not refused (the place is booked)"); return; }
                Next(22);
                break;
            }
            case 22:
                if (!Landed(2, 1, $"{name} climbs forward to wagon 1.")) break;
                if (_bdMate2!.Cart != rc.Cart) { Fail($"{_bdMate2.State.Name} left the cart"); return; }
                me.SeatYaw = BdBack;
                Next(23);
                break;

            // the mid-hop down: back toward W2, downed 0.2 s into the hop
            case 23: if (t >= 0.4f && Press(W(2), "Climb back to wagon 2")) { _bdMovesBefore = rc.MovesDropped; Next(24); } break;
            case 24:
                if (t < 0.2f) { if (!rc.Hopping(me)) { Fail("the hop ended before the lead was downed"); return; } break; }
                me.State.Mangled = true; // held down (calm auto-revive never stands a Mangled member up)
                me.State.Hp = 0;
                party.OnMemberDowned(me.Index);
                GD.Print($"[boarders] the mid-hop down: {name} downed {t:0.00} s into the hop (downed {me.Downed}, in {Where(me)})");
                Next(25);
                break;
            case 25:
            {
                if (rc.Hopping(me)) { if (t > 0.2f) Fail("the downed lead's hop was not dropped"); break; }
                if (t < 0.8f) // past where it would have landed
                {
                    if (rc.CarOf(me) != 1 || me.Seat != TrainRules.HeroPlace || !W(2).HeroPlaceFree) { Fail($"mid-hop downed, the lead is in {Where(me)} and W2's place free {W(2).HeroPlaceFree}"); return; }
                    break;
                }
                var offer = rc.MoveOfferFor(me);
                GD.Print($"[boarders] the mid-hop down: the hop dropped ({rc.MovesDropped - _bdMovesBefore}), the lead slumped in {Where(me)}, W2's place free {W(2).HeroPlaceFree}, offered while downed: {(offer == null ? "nothing" : "a move")}");
                if (rc.MovesDropped != _bdMovesBefore + 1 || offer != null || !me.Downed) { Fail("the mid-hop down: not dropped once, or a move offered while downed"); return; }
                me.State.Mangled = false;
                me.Revive();
                me.State.Hp = Math.Max(me.State.Hp, me.State.Stats[Characters.Stat.MaxHp] * 0.5f);
                party.AllowPossession = true;
                Next(80);
                break;
            }

            // never offered: the arrival at stop 1 (held), on foot
            case 80:
            {
                if (rc.State.Mode != CartMode.Held || rc.Halted || rc.StopIndex != 1) { if (t > 60f) Fail($"no arrival at stop 1 within 60 s (S={rc.State.S:0.0})"); break; }
                Next(81); // held: judged a little after the arrival
                break;
            }
            case 81:
            {
                if (t < 0.3f) break;
                var offer = rc.MoveOfferFor(me);
                var prompt = W(1).Prompt;
                GD.Print($"[boarders] the arrival at stop 1: the lead in {Where(me)} (yaw {Mathf.RadToDeg(me.SeatYaw):0}) offered {(offer == null ? "no move" : "a move")}, W1's prompt \"{prompt}\"; moves refused {rc.MovesRefused}");
                if (offer != null || prompt != "Get out" || rc.CarOf(me) != 1) { Fail("at the arrival a move was offered, or W1 did not offer its rider \"Get out\""); return; }
                // companions hopping in (fix round 1, I2; Ruling 12): with the lead in W1 of the held train, a companion on foot beside W3, out of the
                // cart's own 3 m hop-in reach, hops in by its own brain (BesideTheCart, TryHopIn) before any departure
                var hopper = _bdMate2!;
                hopper.Stance = Characters.Stance.Defensive;
                hopper.Teleport(rc.StandBesideLane(W(3).GlobalPosition) + Vector3.Up * 0.2f, rc.Cart.GlobalPosition);
                _bdHopNearest = float.PositiveInfinity;
                GD.Print($"[boarders] hop-in: {hopper.State.Name} on foot beside W3 at {Dungeon.DungeonBuilder.WorldToCell(hopper.GlobalPosition)}, {hopper.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the cart; the lead in {Where(me)}");
                if (hopper.Seated) { Fail("the companion did not get out for the hop-in check"); return; }
                Next(82);
                break;
            }
            case 82:
            {
                var hopper = _bdMate2!;
                if (!hopper.Seated)
                {
                    _bdHopNearest = Mathf.Min(_bdHopNearest, hopper.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition));
                    if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1) { Fail($"the train left stop 1 before {hopper.State.Name} hopped in"); return; }
                    if (t > 6f) Fail($"{hopper.State.Name} on foot did not hop in within 6 s with the lead in W1 (nearest the cart {_bdHopNearest:0.0} m, action {hopper.CurrentActionName})");
                    break;
                }
                GD.Print($"[boarders] hop-in: {hopper.State.Name} hopped into {Where(hopper)} {t:0.00} s after being set down, never nearer the cart than {_bdHopNearest:0.0} m; the lead still in {Where(me)}");
                if (rc.CarOf(hopper) < 0 || _bdHopNearest <= 3f || rc.CarOf(me) != 1) { Fail("the hop-in came by the cart's own 3 m reach, or not into the train"); return; }
                W(1).Interact(party, me); // the get-out press, as the key would
                if (me.Seated) { Fail("the lead did not get out of W1 at stop 1"); return; }
                var onFoot = rc.MoveOfferFor(me);
                GD.Print($"[boarders] on foot beside the lane at {Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition)}: offered {(onFoot == null ? "no move" : "a move")}");
                if (onFoot != null) { Fail("a move was offered on foot"); return; }
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): moves taken {rc.MovesTaken}, landed {rc.MovesLanded}, dropped {rc.MovesDropped}; the hop {TrainMoveRules.HopSeconds} s within a tick, the cooldown held, W2 to W4 past a companion in one move, forward to the cart, r1_beam cleared by {_bdHeads.Min:0.00} m with the lead in W2, the double press refused (the place is booked) by the real press and the forced move, the mid-hop down dropped; a companion on foot hopped in with the lead in W1; W1 measured to its tub; never offered held, downed or on foot");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }

    /// <summary>r1_beam over every seated head in the train (<see cref="BeamHeads"/>), and whether the lead's in W2 was under it.</summary>
    private void BoardersBeam(RailController rc, RailEncounterController enc, Adventurer me, Action<string> Fail)
    {
        if (_bdHeads.Done || enc.ArmedRide != 1) return;
        if (enc.SetPieceNode("r1_beam") is not LowBeam beam) { Fail("floor 12 lacks r1_beam"); return; }
        if (rc.WagonAt(2) is { } w2 && me.Cart == w2 && Mathf.Abs((beam.GlobalTransform.AffineInverse() * w2.SeatTransform(TrainRules.HeroPlace).Origin).Z) <= 0.25f) _bdLeadUnderBeam = true;
        if (!_bdHeads.Sample(beam, rc)) return;
        _bdHeads.Done = true;
        GD.Print($"[boarders] the beam: r1_beam clears every seated head in the train by {_bdHeads.Min:0.00} m ({_bdHeads.Heads} sample(s) under it at S={beam.S:0.0}; the lead in W2 among them {_bdLeadUnderBeam})");
        if (_bdHeads.Heads == 0 || _bdHeads.Min < 0.05f) Fail($"r1_beam clears a seated head by {_bdHeads.Min:0.00} m, under 0.05 (or measured none)");
    }

    // ---- =reach (Task 4, Ruling 13) -------------------------------------------------------------------------------------------

    private const int BdGripUnread = int.MinValue;

    /// <summary>Each hit heard since the last frame gets the grip its hound has after it (−1 once knocked off): the controller counts it in its own handler.</summary>
    private void BdReadGrips(RailEncounterController enc)
    {
        foreach (var (hound, list) in _bdHurt)
            for (var i = 0; i < list.Count; i++)
                if (list[i].Grip == BdGripUnread) list[i] = list[i] with { Grip = IsInstanceValid(hound) ? enc.GripOf(hound) : -1 };
    }

    /// <summary>Hurts a party member dealt <paramref name="hound"/> (by member index; all party hits when <paramref name="by"/> is null).</summary>
    private int BdHits(Enemies.Enemy? hound, int? by = null)
        => hound != null && _bdHurt.TryGetValue(hound, out var list) ? list.Count(h => h.By >= 0 && (by == null || h.By == by)) : 0;

    /// <summary>The party member index of <paramref name="a"/> (the striker index EnemyHurt reports).</summary>
    private static int BdMember(PartyController party, Adventurer a) => Array.FindIndex(party.Members, x => x == a.State);

    /// <summary>Every melee attack <paramref name="a"/>'s class has: the weapon's light, heavy and perfect swing and each melee strike ability.</summary>
    private static List<(int Index, bool Heavy, bool Perfect, string Name)> BdMeleeAttacks(PartyController party, Adventurer a)
    {
        var list = new List<(int, bool, bool, string)>();
        for (var i = 0; i < a.State.KnownAbilities.Count; i++)
        {
            var ab = party.GetAbilityFor(a.State, a.State.KnownAbilities[i]);
            if (ab.Kind == Combat.AbilityKind.WeaponAttack && !a.State.WeaponProfile.IsRanged)
            {
                list.Add((i, false, false, "a light swing"));
                list.Add((i, true, false, "a heavy swing"));
                list.Add((i, false, true, "a perfect swing"));
            }
            else if (ab.Kind == Combat.AbilityKind.MeleeStrike) list.Add((i, false, false, ab.Name));
        }
        return list;
    }

    /// <summary>A companion that fights with a blade (a warrior or a rogue).</summary>
    private static bool BdBlade(Adventurer a) => a.State.Class.Id is "class.warrior" or "class.rogue";

    /// <summary>The lead's blow <paramref name="d"/> aimed at <paramref name="at"/> (refilled first, as a fresh press each time).</summary>
    private static void BdLeadBlow(PartyController party, Adventurer me, Enemies.Enemy at, (int Index, bool Heavy, bool Perfect, string Name) d)
    {
        AbilitiesRefill(me.State);
        me.State.ActiveAbilityIndex = d.Index;
        BarricadeAim(me, at.GlobalPosition + Vector3.Up * 0.4f);
        party.Attack(Array.IndexOf(party.Adventurers, me), d.Heavy, d.Perfect);
    }

    /// <summary>
    /// W2's seat 0 to each rim spot this frame, read off the wagons' own poses as they stand now (fix round 1, review M5): where a seated body
    /// and a boarder are both posed when a blow is judged in the physics tick. Its own (W2's) on the straight and in bends, the neighbours'
    /// nearest. The bodies themselves are also sampled (the lead's body to its own hound): in a rendered frame one of the two may already be
    /// posed for the frame while the other still stands where the physics tick left it, which is the 0.02 m spread a run measured.
    /// </summary>
    private void BdSampleReach(RailController rc, Adventurer me)
    {
        if (rc.WagonAt(2) is not { } w2 || rc.WagonAt(1) is not { } w1 || rc.WagonAt(3) is not { } w3) return;
        var k = Mathf.Abs(w2.Curvature);
        _bdMaxBend = Mathf.Max(_bdMaxBend, k);
        var seat = w2.SeatTransform(TrainRules.HeroPlace).Origin;
        var own = seat.DistanceTo(w2.BoarderSpotTransform().Origin);
        if (k < 0.01f) _bdOwnStraight = Mathf.Max(_bdOwnStraight, own);
        else if (k >= BdBendK) _bdOwnBend = Mathf.Max(_bdOwnBend, own);
        _bdOwnMin = Mathf.Min(_bdOwnMin, own);
        _bdToW1 = Mathf.Min(_bdToW1, seat.DistanceTo(w1.BoarderSpotTransform().Origin));
        _bdToW3 = Mathf.Min(_bdToW3, seat.DistanceTo(w3.BoarderSpotTransform().Origin));
        if (_bdH2 is { } h2 && IsInstanceValid(h2) && h2.Carrier == w2 && rc.CarOf(me) == 2)
        {
            var bodies = me.GlobalPosition.DistanceTo(h2.GlobalPosition);
            _bdBodyMin = Mathf.Min(_bdBodyMin, bodies);
            _bdBodyMax = Mathf.Max(_bdBodyMax, bodies);
        }
    }

    private void BoardersReach(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT;
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        if (rc.State.Mode == CartMode.Wrecked) { Fail("the train was wrecked"); return; }
        if (rc.State.Mode == CartMode.Held && _bdPhase < 9) { Fail($"the train reached stop {rc.StopIndex} at phase {_bdPhase}, before the script was done"); return; }
        var lead = BdMember(party, me);
        if (_bdPhase >= 2) BdSampleReach(rc, me);
        switch (_bdPhase)
        {
            case 0:
            {
                enc.DebugHoldAnswers = true;
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true;
                if (!rc.DebugSeat(me, 2) || rc.CarOf(me) != 2) { Fail("the lead could not be seated in W2"); return; }
                Next(1);
                break;
            }
            case 1:
            {
                if (t < 0.3f) break;
                _bdH1 = enc.DebugBoard(1, holdHp: true);
                _bdH2 = enc.DebugBoard(2, holdHp: true);
                _bdH3 = enc.DebugBoard(3, holdHp: true);
                if (_bdH1 == null || _bdH2 == null || _bdH3 == null) { Fail("a hound could not be put on W1, W2 or W3"); return; }
                _bdW2Hounds.Add(_bdH2);
                _bdHp1 = _bdH1.Hp;
                _bdHp3 = _bdH3.Hp;
                _bdBlows.Clear();
                var attacks = BdMeleeAttacks(party, me);
                foreach (var h in new[] { 1, 2, 3 }) foreach (var d in attacks) _bdBlows.Add(d); // each attack at each hound in turn: W1's, W2's, W3's
                GD.Print($"[boarders] reach: the lead ({me.State.Class.Name}) in {rc.CarOf(me) switch { var c => $"car {c}" }} seat {me.Seat}; hounds on W1, W2, W3 (held above 1 HP); its melee attacks [{string.Join(", ", attacks.Select(a => a.Name))}], {_bdBlows.Count} blows in all");
                if (attacks.Count == 0) { Fail($"the lead's class {me.State.Class.Name} has no melee attack"); return; }
                _bdBlow = 0;
                _bdBlowAt = -1f;
                Next(2);
                break;
            }
            case 2:
            {
                // a W2 hound knocked off by the lead's blows (its grip) makes way for a fresh one: the blows at W2 keep a target
                if (_bdH2 is not { } h2 || !IsInstanceValid(h2) || h2.Carrier != rc.WagonAt(2))
                {
                    _bdH2 = enc.DebugBoard(2, holdHp: true);
                    if (_bdH2 == null) { Fail("a fresh hound could not be put on W2"); return; }
                    _bdW2Hounds.Add(_bdH2);
                    GD.Print($"[boarders] reach: W2's hound was knocked off by its grip; a fresh one boards ({_bdW2Hounds.Count} so far)");
                }
                if (me.BladeSwinging || _bdClock - _bdBlowAt < 0.35f) break;
                if (_bdBlow >= _bdBlows.Count) { Next(3); break; }
                var per = _bdBlows.Count / 3;
                var target = (_bdBlow / per) switch { 0 => _bdH1!, 1 => _bdH2!, _ => _bdH3! };
                var d = _bdBlows[_bdBlow];
                BdLeadBlow(party, me, target, d);
                GD.Print($"[boarders] reach: blow {_bdBlow + 1}/{_bdBlows.Count}, {d.Name}, aimed at W{_bdBlow / per + 1}'s hound {target.GlobalPosition.DistanceTo(me.GlobalPosition):0.00} m away");
                _bdBlow++;
                _bdBlowAt = _bdClock;
                break;
            }
            case 3:
            {
                if (t < 0.8f || me.BladeSwinging) break;
                var w2Hits = _bdW2Hounds.Sum(h => BdHits(h, lead));
                int w1 = BdHits(_bdH1), w3 = BdHits(_bdH3);
                float hp1 = _bdH1!.Hp, hp3 = _bdH3!.Hp;
                int g1 = enc.GripOf(_bdH1), g3 = enc.GripOf(_bdH3);
                GD.Print($"[boarders] reach: the lead's {_bdBlows.Count} blows landed {w2Hits} time(s) on W2's hound(s) ({_bdW2Hounds.Count} boarded, knock-offs {enc.KnockOffs}); W1's hound hits {w1}, hp {hp1:0}/{_bdHp1:0}, grip {g1}; W3's hits {w3}, hp {hp3:0}/{_bdHp3:0}, grip {g3}");
                if (w2Hits == 0) { Fail("no blow of the lead's in W2 landed on W2's own hound"); return; }
                if (w1 != 0 || w3 != 0 || hp1 < _bdHp1 || hp3 < _bdHp3 || g1 != 0 || g3 != 0) { Fail("a blow from W2 landed on W1's or W3's hound"); return; }
                {
                    // a carried blow (fix round 1, review M6): a cleave off W2's hound, its reach widened past both neighbours' hounds, takes neither
                    var h2 = _bdH2!;
                    float d1 = h2.GlobalPosition.DistanceTo(_bdH1.GlobalPosition), d3 = h2.GlobalPosition.DistanceTo(_bdH3.GlobalPosition);
                    var radius = Mathf.Max(d1, d3) + 0.3f;
                    me.DebugCleave(h2, 10f, radius);
                    GD.Print($"[boarders] reach: a cleave off W2's hound out to {radius:0.00} m (W1's hound {d1:0.00} m from it, W3's {d3:0.00} m): W1's hits {BdHits(_bdH1)}, hp {_bdH1.Hp:0}/{_bdHp1:0}; W3's hits {BdHits(_bdH3)}, hp {_bdH3.Hp:0}/{_bdHp3:0}");
                    if (BdHits(_bdH1) != 0 || BdHits(_bdH3) != 0 || _bdH1.Hp < _bdHp1 || _bdH3.Hp < _bdHp3) { Fail("a cleave off W2's hound carried into W1's or W3's"); return; }
                    // and a chained one (fix round 2): both neighbours frozen and inside the chain's 4 m, a shatter chain off W2's hound takes neither
                    foreach (var n in new[] { _bdH1, _bdH3 }) n.ApplyStatus(new Combat.StatusApplication(Combat.StatusKind.Frost, 5f, 0));
                    var frozen = new[] { _bdH1, _bdH3 }.Count(n => n.Effects.Has(Combat.StatusKind.Frost) && n.GlobalPosition.DistanceTo(h2.GlobalPosition) <= Adventure.Fortune.FortuneRules.ShatterChainRadius);
                    me.DebugShatterChain(h2, 2);
                    GD.Print($"[boarders] reach: a shatter chain off W2's hound ({frozen} frozen neighbour(s) within {Adventure.Fortune.FortuneRules.ShatterChainRadius:0} m): W1's hits {BdHits(_bdH1)}, hp {_bdH1.Hp:0}/{_bdHp1:0}; W3's hits {BdHits(_bdH3)}, hp {_bdH3.Hp:0}/{_bdHp3:0}");
                    if (frozen != 2) { Fail($"the shatter chain's pin needs both neighbours frozen within reach ({frozen})"); return; }
                    if (BdHits(_bdH1) != 0 || BdHits(_bdH3) != 0 || _bdH1.Hp < _bdHp1 || _bdH3.Hp < _bdHp3) { Fail("a shatter chain off W2's hound carried into W1's or W3's"); return; }
                    foreach (var n in new[] { _bdH1, _bdH3 }) n.Effects.Remove(Combat.StatusKind.Frost);
                }
                _bdMate = party.Adventurers.Where(a => IsInstanceValid(a) && a != me && a.Control == ControlKind.Companion && a.Cart == rc.Cart && !a.Downed && BdBlade(a))
                    .OrderBy(a => a.GlobalPosition.DistanceTo(_bdH1.GlobalPosition)).FirstOrDefault();
                if (_bdMate == null) { Fail("no melee companion seated in the cart"); return; }
                _bdSwings = 0;
                _bdBlowAt = -1f;
                Next(4);
                break;
            }
            case 4:
            {
                // from the cart: a melee companion's blows at W1's hound never land
                var mate = _bdMate!;
                if (_bdClock - _bdBlowAt < 0.6f) break;
                if (_bdSwings >= 3) { Next(5); break; }
                AbilitiesRefill(mate.State);
                mate.State.ActiveAbilityIndex = 0;
                mate.DebugStrikeAt(_bdH1!);
                _bdSwings++;
                _bdBlowAt = _bdClock;
                GD.Print($"[boarders] reach: from the cart, {mate.State.Name} (car {rc.CarOf(mate)} seat {mate.Seat}) strikes at W1's hound {mate.GlobalPosition.DistanceTo(_bdH1!.GlobalPosition):0.00} m away (swing {_bdSwings})");
                break;
            }
            case 5:
            {
                if (t < 0.5f) break;
                var mate = _bdMate!;
                var hits = BdHits(_bdH1, BdMember(party, mate));
                GD.Print($"[boarders] reach: from the cart, {mate.State.Name}'s 3 strikes at W1's hound landed {hits}; its hp {_bdH1!.Hp:0}/{_bdHp1:0}, grip {enc.GripOf(_bdH1)}");
                if (hits != 0 || BdHits(_bdH1) != 0 || _bdH1.Hp < _bdHp1 || enc.GripOf(_bdH1) != 0) { Fail("a companion seated in the cart landed a blow on W1's hound"); return; }
                // the same blade seated in W3: W2's hound, nearer than its own reach, is still another car's (the own-car rule, not the distance, keeps it off)
                if (!rc.DebugSeat(mate, 3)) { Fail($"{mate.State.Name} could not be seated in W3"); return; }
                _bdSwings = 0;
                _bdBlowAt = -1f;
                _bdHp1 = _bdH2!.Hp; // W2's hound's HP now (its HP is held above 1)
                Next(7);
                break;
            }
            case 7:
            {
                var mate = _bdMate!;
                if (t < 0.3f || _bdClock - _bdBlowAt < 0.6f) break;
                if (_bdSwings >= 3) { Next(8); break; }
                AbilitiesRefill(mate.State);
                mate.State.ActiveAbilityIndex = 0;
                mate.DebugStrikeAt(_bdH2!);
                _bdSwings++;
                _bdBlowAt = _bdClock;
                GD.Print($"[boarders] reach: from W3, {mate.State.Name} (car {rc.CarOf(mate)} seat {mate.Seat}) strikes at W2's hound {mate.GlobalPosition.DistanceTo(_bdH2!.GlobalPosition):0.00} m away (WagonReach {BoarderRules.WagonReach:0.00}; swing {_bdSwings})");
                break;
            }
            case 8:
            {
                if (t < 0.5f) break;
                var mate = _bdMate!;
                var hits = BdHits(_bdH2, BdMember(party, mate));
                GD.Print($"[boarders] reach: from W3, {mate.State.Name}'s 3 strikes at W2's hound landed {hits}; its hp {_bdH2!.Hp:0}/{_bdHp1:0}, its own hound (W3's) hits {BdHits(_bdH3, BdMember(party, mate))}");
                if (hits != 0 || _bdH2.Hp < _bdHp1) { Fail("a companion seated in W3 landed a blow on W2's hound"); return; }
                Next(6);
                break;
            }
            case 6:
            {
                // the reach: a bend measured (ride on until one is), then the line and the constant's bounds
                if (_bdOwnBend < 0f || _bdOwnStraight < 0f) { if (t > 40f) Fail($"no {(_bdOwnBend < 0f ? "bend" : "straight")} sample of W2's own spot on ride 2 (the sharpest bend {_bdMaxBend:0.000}/m)"); break; }
                GD.Print($"[boarders] reach: W2 seat 0 to its own spot {_bdOwnStraight:0.00} m (straight), {_bdOwnBend:0.00} m (in a bend); to W1's spot {_bdToW1:0.00} m, to W3's {_bdToW3:0.00} m (the sharpest bend {_bdMaxBend:0.000}/m)");
                GD.Print($"[boarders] reach: the own spot read off W2's pose spans {_bdOwnMin:0.000}..{Mathf.Max(_bdOwnStraight, _bdOwnBend):0.000} m; the bodies sampled in the rendered frame (the lead to W2's hound) span {_bdBodyMin:0.000}..{_bdBodyMax:0.000} m");
                var need = Mathf.Max(_bdOwnStraight, _bdOwnBend) + 0.15f;
                GD.Print($"[boarders] reach: BoarderRules.WagonReach {BoarderRules.WagonReach:0.00} m: the own spot + 0.15 is {need:0.000} m, the car spacing {TrainRules.Spacing:0.00} m");
                if (BoarderRules.WagonReach < need - 1e-3f || BoarderRules.WagonReach >= TrainRules.Spacing) { Fail($"WagonReach {BoarderRules.WagonReach:0.00} is not in [{need:0.000}, {TrainRules.Spacing:0.00})"); return; }
                if (_bdBodyMax < 0f || _bdBodyMax > BoarderRules.WagonReach) { Fail($"the lead's body and W2's hound sampled {_bdBodyMax:0.000} m apart, past WagonReach {BoarderRules.WagonReach:0.00} (or never sampled)"); return; }
                foreach (var h in _bdW2Hounds.Append(_bdH1!).Append(_bdH3!)) if (IsInstanceValid(h) && h.IsAlive) enc.DebugKill(h);
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): from W2 the lead's {_bdBlows.Count} blows landed only on W2's hound ({_bdW2Hounds.Sum(h => BdHits(h, lead))} hit(s)); W1's and W3's took none; a cart companion's strikes at W1's never landed; the own spot {Mathf.Max(_bdOwnStraight, _bdOwnBend):0.00} m (the bodies {_bdBodyMin:0.00}..{_bdBodyMax:0.00} m) within WagonReach {BoarderRules.WagonReach:0.00} m; a cleave off W2's hound took neither neighbour");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }

    // ---- =knock (Task 4, Rulings 14-15; Task 3 review M1) ---------------------------------------------------------------------

    /// <summary>The metres to the next stop ahead of the train (its S), or +inf past the last.</summary>
    private static float BdRideLeft(RailController rc)
    {
        foreach (var s in rc.StopS) if (s > rc.State.S + 0.5f) return s - rc.State.S;
        return float.PositiveInfinity;
    }

    /// <summary>Task 3 review M1 (a): in the first seated lever window of an armed ride, the lead's move is refused and none is offered.</summary>
    private void BdLeverWindow(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        if (_bdLeverChecked || rc.State.Mode != CartMode.Rolling || !enc.AnyLeverWindow() || rc.CarOf(me) < 0 || rc.Hopping(me) || me.Downed) return;
        _bdLeverChecked = true;
        var from = rc.CarOf(me);
        ICartBody to = from == 0 ? rc.WagonAt(1)! : rc.Cart;
        var offer = rc.MoveOfferFor(me);
        var before = rc.MovesTaken;
        var refusedBefore = rc.MovesRefusedCount;
        var took = rc.Move(me, to, "test: a lever window");
        var why = rc.LastMoveRefused;
        GD.Print($"[boarders] the lever window (ride {enc.ArmedRide}, front S={rc.Party.FrontS:0.0}): moves refused {rc.MovesRefused}; offered {(offer == null ? "nothing" : "a move")}; the lead's move from car {from} taken={took} (refused: {why ?? "-"})");
        if (offer != null || took || rc.MovesTaken != before || rc.MovesRefusedCount != refusedBefore + 1 || why != "a lever window is open") Fail("in a seated lever window a move was offered or not refused (a lever window is open)");
    }

    private void BoardersKnock(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT;
        var tick = 1f / Engine.PhysicsTicksPerSecond;
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        TrainWagon W(int k) => rc.WagonAt(k)!;
        if (rc.State.Mode == CartMode.Wrecked) { Fail("the train was wrecked"); return; }
        BdLeverWindow(rc, enc, party, me, Fail);
        if (_done) return;
        var lead = BdMember(party, me);
        bool Gone(Enemies.Enemy h, TrainWagon w) => !IsInstanceValid(h) || !h.IsAlive || h.Carrier != w;
        switch (_bdPhase)
        {
            case 0:
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true;
                me.SeatYaw = BdBack;
                Next(1);
                break;
            case 1:
            {
                // into W1 by the grip's press, as =move does
                if (t < 0.15f) break;
                if (rc.MoveOfferFor(me) != W(1) || !ReferenceEquals(me.LookingAt, W(1)) || W(1).Prompt != "Climb back to wagon 1") { if (t > 1.6f) Fail($"no \"Climb back to wagon 1\" offered (prompt \"{W(1).Prompt}\")"); break; }
                party.VrUse(true);
                party.VrUse(false);
                if (!rc.Hopping(me)) { Fail($"the press took no move (refused: {rc.LastMoveRefused ?? "-"})"); return; }
                Next(2);
                break;
            }
            case 2:
            {
                // Task 3 review M1 (b): at speed, after the move's grace, W1's own rider is offered nothing (no "Get out", Ruling 9)
                if (rc.Hopping(me) || t < TrainMoveRules.MoveCooldown + 0.1f) break;
                if (rc.CarOf(me) != 1) { Fail($"the lead landed in car {rc.CarOf(me)}, not W1"); return; }
                if (rc.State.V < TrainMoveRules.GetOutBelow + 0.5f) { if (t > 5f) Fail($"the train is not at speed (v {rc.State.V:0.00})"); break; }
                var prompt = W(1).Prompt;
                GD.Print($"[boarders] at speed (v {rc.State.V:0.00}), {t:0.00} s after the press (in the grace {rc.InMoveGrace(me)}): W1's own rider's prompt \"{prompt}\"");
                if (rc.InMoveGrace(me) || prompt != "") { Fail($"W1's own rider is offered \"{prompt}\" at speed"); return; }
                me.SeatYaw = BdForward; // the seat turns at most 150 degrees either way: W1's rim spot (back right) is reached turning right from forward
                Next(10);
                break;
            }

            // pass 1: three melee blows of the lead's knock a W1 hound off: grip 1, 2, 3
            case 10:
            {
                if (BdRideLeft(rc) < 40f || rc.State.Mode != CartMode.Rolling) break;
                _bdHound = enc.DebugBoard(1, holdHp: true);
                if (_bdHound == null) { Fail("no hound could be put on W1"); return; }
                _bdKnockBase = enc.KnockOffs;
                _bdBlowAt = -1f;
                _bdSwings = 0;
                Next(11);
                break;
            }
            case 11:
            {
                var h = _bdHound!;
                var hurts = _bdHurt.TryGetValue(h, out var l) ? l.Where(x => x.By >= 0).ToList() : new();
                if (hurts.Any(x => x.By != lead)) { Fail("pass 1: someone else hit W1's hound"); return; }
                if (enc.KnockOffs > _bdKnockBase)
                {
                    GD.Print($"[boarders] pass 1: the grip after each landed blow [{string.Join(", ", hurts.Select(x => x.Grip < 0 ? "knocked off" : x.Grip.ToString()))}] in {_bdSwings} swing(s)");
                    if (hurts.Count != 3 || hurts[0].Grip != 1 || hurts[1].Grip != 2 || hurts[2].Grip != -1 || hurts.Any(x => x.Kind != Combat.AttackKind.Melee)) { Fail("pass 1: the grip did not go 1, 2, knocked off on three melee hits"); return; }
                    Next(12);
                    break;
                }
                if (_bdSwings >= 12) { Fail($"pass 1: {_bdSwings} swings landed {hurts.Count} blow(s), the hound still aboard"); return; }
                if (me.BladeSwinging || _bdClock - _bdBlowAt < 0.35f) break;
                BdLeadBlow(party, me, h, (me.State.KnownAbilities.IndexOf(Combat.AbilityProfile.BasicId) is var b and >= 0 ? b : 0, false, false, "a light swing"));
                {
                    var aim = -me.Camera.GlobalTransform.Basis.Z;
                    var to = h.GlobalPosition - me.GlobalPosition;
                    GD.Print($"[boarders] pass 1: swing {_bdSwings + 1} at W1's hound {to.Length():0.00} m away, {Mathf.RadToDeg(new Vector2(aim.X, aim.Z).AngleTo(new Vector2(to.X, to.Z))):0} degrees off the aim (seat yaw {Mathf.RadToDeg(me.SeatYaw):0}), car {rc.CarOf(me)}, the hound's car {rc.CarCarrying(h.Carrier)}");
                }
                _bdSwings++;
                _bdBlowAt = _bdClock;
                break;
            }
            case 12:
            {
                if (t < 0.2f) break;
                var h = _bdHound!;
                var cell = Dungeon.DungeonBuilder.WorldToCell(h.GlobalPosition);
                var rail = Main.Instance.Builder.Layout?.IsRail(cell) == true;
                GD.Print($"[boarders] pass 1: the hound knocked off stands at {cell} (rail {rail}), alive {h.IsAlive}, carried {h.Carried}, aggro {!h.IsUnaware}, {h.GlobalPosition.DistanceTo(W(1).GlobalPosition):0.0} m from W1");
                if (!h.IsAlive || h.Carried || h.IsUnaware || rail) { Fail("pass 1: the knocked-off hound is not standing beside the lane alive with its aggro on"); return; }
                h.Kill(); // on foot now: it would only chase the train
                Next(20);
                break;
            }

            // pass 2: a caster companion's spell knocks a fresh W1 hound off with its one hit
            case 20:
            {
                if (t < 0.3f || BdRideLeft(rc) < 30f || rc.State.Mode != CartMode.Rolling) break;
                _bdMate = party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && a.Cart == rc.Cart && !a.Downed && BdSpell(party, a) >= 0);
                if (_bdMate == null) { Fail("pass 2: no caster companion in the cart"); return; }
                _bdHound = enc.DebugBoard(1, holdHp: true);
                if (_bdHound == null) { Fail("pass 2: no hound could be put on W1"); return; }
                _bdKnockBase = enc.KnockOffs;
                _bdSwings = 0;
                _bdBlowAt = -1f;
                Next(21);
                break;
            }
            case 21:
            {
                var h = _bdHound!;
                var mate = _bdMate!;
                var hurts = _bdHurt.TryGetValue(h, out var l) ? l.Where(x => x.By >= 0).ToList() : new();
                if (enc.KnockOffs > _bdKnockBase)
                {
                    GD.Print($"[boarders] pass 2: {mate.State.Name}'s {party.GetAbilityFor(mate.State, mate.State.ActiveAbilityId).Name} knocked W1's hound off: hits [{string.Join(", ", hurts.Select(x => $"{x.Kind} by {party.Members[x.By].Name}"))}] in {_bdSwings} cast(s)");
                    if (hurts.Count != 1 || hurts[0].Kind != Combat.AttackKind.Spell || hurts[0].By != BdMember(party, mate)) { Fail("pass 2: the hound was not knocked off by the one spell hit"); return; }
                    Next(22);
                    break;
                }
                if (hurts.Count > 0) { Fail($"pass 2: a hit on W1's hound did not knock it off ({hurts[0].Kind})"); return; }
                if (_bdSwings >= 4) { Fail($"pass 2: {_bdSwings} casts never hit W1's hound"); return; }
                if (_bdClock - _bdBlowAt < 1.5f) break;
                AbilitiesRefill(mate.State);
                mate.State.ActiveAbilityIndex = BdSpell(party, mate);
                mate.DebugStrikeAt(h);
                _bdSwings++;
                _bdBlowAt = _bdClock;
                GD.Print($"[boarders] pass 2: {mate.State.Name} (car {rc.CarOf(mate)} seat {mate.Seat}) casts {party.GetAbilityFor(mate.State, mate.State.ActiveAbilityId).Name} at W1's hound {mate.GlobalPosition.DistanceTo(h.GlobalPosition):0.0} m away");
                break;
            }
            case 22:
            {
                if (t < 0.2f) break;
                var h = _bdHound!;
                if (!h.IsAlive || h.Carried) { Fail("pass 2: the knocked-off hound is not standing beside the lane alive"); return; }
                h.Kill();
                Next(30);
                break;
            }

            // pass 3: crew in W3 and a hound on it. 3a: a cart companion with a shot answers from its seat (nobody moves); 3b: with every
            // shooter held down, the nearest blade climbs to it (Ruling 15 as amended in fix round 1)
            case 30:
            {
                if (BdRideLeft(rc) < 100f || rc.State.Mode != CartMode.Rolling || rc.MovesRefused) { if (t > 60f) Fail("pass 3: no ride with 100 m left"); break; }
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                var seated = new List<string>();
                for (var stop = 2; stop <= 3; stop++) // stop 1's sit in W1 since the restore
                    foreach (var c in ctrl.DebugSeatCrew(stop) ?? new()) seated.Add($"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}");
                var inW3 = W(3).Riders.Values.Count(r => r is Npc { Crew: true, Dead: false });
                GD.Print($"[boarders] pass 3: crew seated [{string.Join(", ", seated)}]; {inW3} in W3; the lead in car {rc.CarOf(me)}; the cart's companions [{string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion).Select(a => $"{a.State.Name} car {rc.CarOf(a)}"))}]");
                if (inW3 == 0) { Fail("pass 3: no crew in W3"); return; }
                GD.Print($"[boarders] pass 3a: the cart's companions [{string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion).Select(a => $"{a.State.Name} {(a.Shooter ? "a shooter" : "a blade")}"))}]");
                _bdHound = enc.DebugBoard(3, holdHp: true);
                if (_bdHound == null) { Fail("pass 3a: no hound could be put on W3"); return; }
                _bdKnockBase = enc.KnockOffs;
                _bdMovesBefore = rc.MovesTaken;
                _bdMate = null;
                Next(36);
                break;
            }
            case 36:
            {
                // 3a: answered from a seat with a clear shot; its hits take the grip and nobody moves
                var h = _bdHound!;
                if (_bdMate == null)
                {
                    var answer = enc.AnswersNow.FirstOrDefault(a => a.Wagon == 3);
                    if (answer.Name == null) { if (t > 1f) Fail("pass 3a: nobody answered W3"); break; }
                    _bdMate = party.Adventurers.First(a => IsInstanceValid(a) && a.State.Name == answer.Name);
                    GD.Print($"[boarders] pass 3a: {_bdMate.State.Name} answers W3 {(answer.FromSeat ? "from its seat" : "by moving")} in car {rc.CarOf(_bdMate)}, {_bdMate.GlobalPosition.DistanceTo(h.GlobalPosition):0.0} m from the hound (a shot: {_bdMate.HasShotAt(h)})");
                    if (!answer.FromSeat || !_bdMate.Shooter || rc.CarOf(_bdMate) != 0) { Fail("pass 3a: W3 was not answered from a cart seat by a companion with a shot"); return; }
                    _bdMate.DebugHoldFire = false; // the only one let fight
                    break;
                }
                if (rc.MovesTaken != _bdMovesBefore || rc.CarOf(_bdMate) != 0) { Fail($"pass 3a: a move was taken while W3 was answered from a seat ({_bdMate.State.Name} in car {rc.CarOf(_bdMate)})"); return; }
                if (!Gone(h, W(3))) { if (t > 10f) Fail($"pass 3a: {_bdMate.State.Name}'s shots did not knock W3's hound off within 10 s (its hits {BdHits(h, BdMember(party, _bdMate))}, grip {enc.GripOf(h)})"); break; }
                var hits = _bdHurt.TryGetValue(h, out var l) ? l.Where(x => x.By >= 0).ToList() : new();
                GD.Print($"[boarders] pass 3a: W3's hound knocked off={enc.KnockOffs > _bdKnockBase} by {_bdMate.State.Name}'s shots [{string.Join(", ", hits.Select(x => $"{x.Kind} grip {x.Grip}"))}] {t:0.00} s on; no move taken");
                if (enc.KnockOffs <= _bdKnockBase || hits.Count == 0 || hits.Any(x => x.By != BdMember(party, _bdMate) || x.Kind == Combat.AttackKind.Melee)) { Fail("pass 3a: the hound was not knocked off by the seated answerer's shots"); return; }
                if (IsInstanceValid(h) && h.IsAlive) h.Kill();
                _bdMate.DebugHoldFire = true;
                Next(37);
                break;
            }
            case 37:
            {
                // 3b (fix round 2): each seat answerer is made to pump (DebugPump) in turn: the answer passes on to the next shooter with a
                // shot, and with none left to the cart's blade, which climbs
                if (t < 0.5f) break;
                if (enc.AnswersNow.Any(a => a.Wagon == 3)) { if (t > 2f) Fail("pass 3a: the seat answer did not end with W3 clear"); break; }
                if (BdRideLeft(rc) < 90f || rc.State.Mode != CartMode.Rolling || rc.MovesRefused) { if (t > 60f) Fail("pass 3b: no ride with 90 m left"); break; }
                _bdShootersDown.Clear();
                _bdHound = enc.DebugBoard(3);
                if (_bdHound == null) { Fail("pass 3b: no hound could be put on W3"); return; }
                _bdMate = party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && rc.CarOf(a) == 0 && a.Blade);
                if (_bdMate == null) { Fail("pass 3b: no blade companion in the cart"); return; }
                _bdKnockBase = enc.KnockOffs;
                _bdAnswerHops.Clear();
                _bdWasHopping = false;
                _bdClearAt = -1f;
                _bdHandoffAt = -1f;
                Next(38);
                break;
            }
            case 38:
            {
                var blade = _bdMate!;
                BdTrackHops(rc, blade);
                var answer = enc.AnswersNow.FirstOrDefault(a => a.Wagon == 3);
                if (answer.Name == null) { if (_bdHandoffAt >= 0f ? _bdClock - _bdHandoffAt > 0.25f : t > 1f) Fail($"pass 3b: W3 went unanswered {(_bdHandoffAt >= 0f ? "after its seat answerer began to pump" : "")}"); break; }
                var who = party.Adventurers.First(a => IsInstanceValid(a) && a.State.Name == answer.Name);
                if (answer.FromSeat)
                {
                    if (_bdShootersDown.Contains(who)) { if (_bdClock - _bdHandoffAt > 0.25f) Fail($"pass 3b: {who.State.Name} kept W3's seat answer while it pumped"); break; }
                    if (_bdHandoffAt >= 0f) GD.Print($"[boarders] pass 3b: W3's answer passed to {who.State.Name}'s seat {_bdClock - _bdHandoffAt:0.00} s after {_bdShootersDown[^1].State.Name} began to pump");
                    else GD.Print($"[boarders] pass 3b: {who.State.Name} answers W3 from its seat");
                    if (!who.Shooter || who.Pumping) { Fail($"pass 3b: {who.State.Name} answers from its seat with no shot, or pumping"); return; }
                    who.DebugPump = true; // the cart's pump picks it: it holds its fire, so its seat answer must pass on
                    _bdShootersDown.Add(who);
                    _bdHandoffAt = _bdClock;
                    break;
                }
                var unforced = party.Adventurers.Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && !a.Downed && rc.CarOf(a) >= 0 && a.Shooter && !_bdShootersDown.Contains(a) && a.HasShotAt(_bdHound!)).Select(a => a.State.Name).ToList();
                GD.Print($"[boarders] pass 3b: with [{string.Join(", ", _bdShootersDown.Select(a => a.State.Name))}] pumping, {who.State.Name} answers W3 by climbing {(_bdHandoffAt >= 0f ? $"{_bdClock - _bdHandoffAt:0.00} s after the last pump began" : "")}; shooters with a shot left: [{string.Join(", ", unforced)}]");
                if (who != blade || _bdShootersDown.Count == 0 || unforced.Count > 0) { Fail("pass 3b: the answer did not pass through every shooter to the cart's blade"); return; }
                Next(31);
                break;
            }
            case 31:
            {
                var answer = enc.AnswersNow.FirstOrDefault(a => a.Wagon == 3);
                if (answer.Name == null) { if (t > 1f) Fail("pass 3: nobody answered W3"); break; }
                _bdMate = party.Adventurers.First(a => IsInstanceValid(a) && a.State.Name == answer.Name);
                var nearest = party.Adventurers.Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && !a.Downed && rc.CarOf(a) >= 0).Min(a => Math.Abs(rc.CarOf(a) - 3));
                GD.Print($"[boarders] pass 3: {_bdMate.State.Name} answers W3 from car {rc.CarOf(_bdMate)} ({t:0.00} s after the hound boarded); the nearest companion is {nearest} car(s) away");
                if (_bdMate.Control != ControlKind.Companion || answer.FromSeat || _bdMate.Shooter || Math.Abs(rc.CarOf(_bdMate) - 3) != nearest || rc.CarOf(_bdMate) != 0) { Fail("pass 3b: the answerer is not the nearest cart blade, moving"); return; }
                _bdMate.DebugHoldFire = false; // the only one let fight: the hit that counts is the answerer's
                Next(32);
                break;
            }
            case 32:
            {
                var mate = _bdMate!;
                BdTrackHops(rc, mate);
                var h = _bdHound!;
                if (rc.CarOf(mate) != 3 || rc.Hopping(mate))
                {
                    if (Gone(h, W(3))) { Fail("pass 3: the W3 hound was gone before its answerer got there"); return; }
                    if (t > 8f) Fail($"pass 3: {mate.State.Name} did not reach W3 within 8 s (in car {rc.CarOf(mate)}, hops [{string.Join(", ", _bdAnswerHops.Select(x => $"{x.From}>{x.To}"))}])");
                    break;
                }
                var gaps = _bdAnswerHops.Zip(_bdAnswerHops.Skip(1), (a, b) => b.At - a.At).ToList();
                GD.Print($"[boarders] pass 3: {mate.State.Name} reached W3 in {_bdAnswerHops.Count} move(s) [{string.Join(", ", _bdAnswerHops.Select(x => $"{x.From}>{x.To} at {x.At - _bdPhaseT:0.00} s"))}]; the gaps [{string.Join(", ", gaps.Select(g => $"{g:0.00}"))}] s");
                if (_bdAnswerHops.Count == 0 || gaps.Any(g => g < TrainMoveRules.MoveCooldown - tick - 1e-3f)) { Fail("pass 3: the answerer moved more than once per cooldown"); return; }
                Next(33);
                break;
            }
            case 33:
            {
                var mate = _bdMate!;
                var h = _bdHound!;
                if (!Gone(h, W(3))) { if (t > 15f) Fail($"pass 3: the W3 hound still aboard {t:0.0} s after {mate.State.Name} reached it (its hits {BdHits(h, BdMember(party, mate))}, grip {enc.GripOf(h)})"); break; }
                var hits = _bdHurt.TryGetValue(h, out var l) ? l.Where(x => x.By >= 0).ToList() : new();
                var byMate = hits.Count(x => x.By == BdMember(party, mate));
                var fate = !IsInstanceValid(h) || !h.IsAlive ? "killed" : enc.KnockOffs > _bdKnockBase ? "knocked off" : "gone";
                GD.Print($"[boarders] pass 3: the W3 hound {fate} after {hits.Count} party hit(s), {byMate} by {mate.State.Name} [{string.Join(", ", hits.Select(x => $"{x.Kind} grip {x.Grip}"))}]");
                if (byMate == 0 || hits.Any(x => x.By != BdMember(party, mate)) || fate == "gone") { Fail("pass 3: the answerer's hits did not end the hound"); return; }
                // a hit it lived through took grip (the grip read after it is above 0, or −1 once knocked off); only a first blow that killed it takes none
                if (!(fate == "killed" && hits.Count == 1) && !hits.Any(x => x.Grip != 0)) { Fail("pass 3: the answerer's hits did not count toward the grip"); return; }
                if (IsInstanceValid(h) && h.IsAlive) h.Kill();
                _bdClearAt = _bdClock;
                _bdAnswerHops.Clear();
                Next(34);
                break;
            }
            case 34:
            {
                var mate = _bdMate!;
                BdTrackHops(rc, mate);
                if (_bdAnswerHops.Count == 0 || _bdAnswerHops[0].To < 0) { if (t > TrainMoveRules.AnswerBack + 2f) Fail($"pass 3: {mate.State.Name} did not move back within {TrainMoveRules.AnswerBack + 2f:0} s of W3 going clear (in car {rc.CarOf(mate)})"); break; }
                var hop = _bdAnswerHops[0]; // landed: the car it left and the one it came down in
                var after = hop.At - _bdClearAt;
                GD.Print($"[boarders] pass 3: {mate.State.Name} moves back {hop.From}>{hop.To} {after:0.00} s after W3 went clear (AnswerBack {TrainMoveRules.AnswerBack} s)");
                if (hop.To >= hop.From || after < TrainMoveRules.AnswerBack - 2f * tick || after > TrainMoveRules.AnswerBack + 0.6f) { Fail("pass 3: the move back was not toward the cart 5 s after W3 went clear"); return; }
                mate.DebugHoldFire = true;
                foreach (var a in _bdShootersDown) a.DebugPump = false;
                GD.Print($"[boarders] pass 3b: the shooters' pumps let go [{string.Join(", ", _bdShootersDown.Select(a => a.State.Name))}] (the cart's pump picks its pumpers afresh next tick)");
                Next(40);
                break;
            }

            // pass 4: a W1 hound killed drops beside the lane
            case 40:
            {
                if (t < 0.5f || rc.State.Mode != CartMode.Rolling) break;
                _bdHound = enc.DebugBoard(1);
                if (_bdHound == null) { Fail("pass 4: no hound could be put on W1"); return; }
                Next(41);
                break;
            }
            case 41:
                if (t < 0.2f) break;
                enc.DebugKill(_bdHound!);
                Next(42);
                break;
            case 42:
            {
                var h = _bdHound!;
                if (!IsInstanceValid(h) || h.Carried) { if (t > 1f) Fail("pass 4: the killed hound was not dropped off W1"); break; }
                var cell = Dungeon.DungeonBuilder.WorldToCell(h.GlobalPosition);
                var rail = Main.Instance.Builder.Layout?.IsRail(cell) == true;
                GD.Print($"[boarders] pass 4: the hound killed on W1 lies at {cell} (rail {rail}), alive {h.IsAlive}, {h.GlobalPosition.DistanceTo(W(1).GlobalPosition):0.0} m from W1");
                if (h.IsAlive || rail) { Fail("pass 4: the killed hound does not lie beside the lane"); return; }
                Next(50);
                break;
            }
            case 50:
                if (!_bdLeverChecked) { if (t > 120f) Fail("no seated lever window was met"); break; }
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): three melee blows knocked a W1 hound off (grip 1, 2, 3), alive beside the lane; one spell knocked one off; W3 answered from a seat with a shot (no move), then with the shooters down {_bdMate?.State.Name} climbed to W3 one move per cooldown, ended its hound and moved back 5 s after; a killed hound dropped beside the lane; no Get out at speed; the lever window refused a move; knock-offs {enc.KnockOffs}");
                _done = true;
                GetTree().Quit(0);
                break;
        }
    }

    /// <summary>The index of a spell <paramref name="a"/> knows that strikes one enemy (a projectile of the Spell kind), or −1.</summary>
    private static int BdSpell(PartyController party, Adventurer a)
    {
        for (var i = 0; i < a.State.KnownAbilities.Count; i++)
            if (party.GetAbilityFor(a.State, a.State.KnownAbilities[i]) is { Kind: Combat.AbilityKind.Projectile, Attack.Kind: Combat.AttackKind.Spell, ArrowCost: 0 }) return i;
        return -1;
    }

    /// <summary>The answerer's moves as they are taken (a hop's start), with the car it leaves and the car it lands in once known.</summary>
    private void BdTrackHops(RailController rc, Adventurer mate)
    {
        var hopping = rc.Hopping(mate);
        if (hopping && !_bdWasHopping) _bdAnswerHops.Add((_bdClock, rc.CarOf(mate), -1));
        if (!hopping && _bdWasHopping && _bdAnswerHops.Count > 0) _bdAnswerHops[^1] = (_bdAnswerHops[^1].At, _bdAnswerHops[^1].From, rc.CarOf(mate));
        _bdWasHopping = hopping;
    }
}
