using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders=knock (plan 14, Task 4): see DebugTools.Boarders.cs for the flow as a whole ----------------------------------
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
//    Through 3b the cart's own pump picks nobody (PumperCap 0; Task 5, the Task 4 re-review's Minor): a shooter pumps only by the flow's
//    DebugPump, so a natural pump can never take a shooter out of the seat answer and redden the hand-off check.
//  - Pass 4: DebugKill of a W1 hound drops it beside the lane.
//  - The final review's I1 (the look asks the ray first): (b) before the first move, at speed in the cart, the lead looks back (the seat's
//    150 degree turn) toward W1's rim with a W1 crew member put down by the test (W1's move is offered all the while): W1 offers "Revive {name}",
//    not the climb, and the grip's press revives them (no move taken, the lead still in the cart). From seat 0 the ray's 3 m never meets W1
//    (it looks 30 degrees off it, printed): W1 is what the look offers as the car its yaw points to, with its revive first. (a) After the first move (and Task 3 review M1's check), stop
//    2's crew seated in W2 by the test's hook, the lead climbs back to W2, one of them is put down, and seated in W2 at speed, looking at them,
//    the lead is offered "Revive {name}" by what its ray found (W2 itself; a move offered by its yaw all the while) and the press revives
//    them; then it climbs forward to W1.
//  - Plan 15 Task 0 (the plan 14 final re-review's N1, N2; a revive that would be refused never masks the climb): N1, after (b), a hound held
//    alive on W1 (answers held: nobody else climbs) and a W1 crew member put down beside it: the revive is refused (the hound within the clear
//    radius), so W1 offers no revive and the cart rider looking back is offered "Climb back to wagon 1"; the press lands the lead in W1 and
//    W1's throw clock, running alone until then, is reset within a tick or two of the landing (no throw). There a press on W1 from its own seat
//    at speed (a "Revive" sent from a screen behind) finds nothing to do: refused out loud ("Not with enemies this close."), no move, no get-out.
//    The hound is killed and the crew member got up. N2, after (a) and the climb forward to W1: the lead climbs forward into the cart, a cart
//    companion is seated in W1's place (answers held), a W2 crew member is put down (no enemy near: the revive would be taken), and the cart
//    rider looking back, beyond the server's reach of W2 (printed), is offered "Climb back to wagon 2" (passing W1), not the revive; the press
//    lands it in W2, where the revive, now within reach, is offered by the ray and taken (as (a)); the companion goes back to the cart and the
//    lead climbs forward to W1 again.
//  - The final review's M8: after the lever window, rolling, the player takes a cart companion (party.Possess); the old lead, left in W1, goes
//    back into the cart's free hero seat by itself within the cooldown, the hop and 1 s.
public partial class DebugTools
{
    private Npc? _bdDowned;
    private Adventurer? _bdOldLead;
    private bool _bdN2Done;
    private ulong _bdPressedAt;

    /// <summary>
    /// I1's revive from a seat at speed: the lead looks at <see cref="_bdDowned"/> (aimed each frame) in <paramref name="car"/>'s wagon; true
    /// once it was offered "Revive {name}" by what its look found, a move offered by its yaw all the while, and the grip's press revived it.
    /// </summary>
    private bool BdSeatRevive(RailController rc, PartyController party, Adventurer me, TrainWagon car, float t, string where, bool byRay, Action<string> Fail)
    {
        var npc = _bdDowned!;
        // seated crew are not on the ray's layer: the look finds their wagon. From its own seat the lead looks down at them; from another car
        // at the nearest point of the wagon's rim (0.2 m in), which the ray's 3 m reaches where the bench does not
        var aimAt = me.Cart == car ? npc.GlobalPosition + Vector3.Up * 0.3f : BdRimNearest(car, me.Camera.GlobalPosition);
        BarricadeAim(me, aimAt);
        var aimOff = Mathf.RadToDeg((-me.Camera.GlobalTransform.Basis.Z).AngleTo(aimAt - me.Camera.GlobalPosition));
        if (t < 0.3f) return false;
        var want = $"Revive {npc.NpcName}";
        var looking = me.LookingAt;
        var said = looking?.Prompt ?? "";
        var offer = rc.MoveOfferFor(me);
        var dist = me.Camera.GlobalPosition.DistanceTo(npc.GlobalPosition);
        var rim = me.Camera.GlobalPosition.DistanceTo(BdRimNearest(car, me.Camera.GlobalPosition));
        if (rc.State.V < TrainMoveRules.GetOutBelow + 0.5f || rc.State.Mode != CartMode.Rolling) { Fail($"{where}: the train is not at speed (v {rc.State.V:0.00})"); return false; }
        if (!ReferenceEquals(looking, car) || said != want || me.LookingAtMove || byRay && !me.LookingByRay || offer == null)
        {
            if (t > 2f) Fail($"{where}: looking at {(looking as Node)?.Name ?? "nothing"} (the move's {me.LookingAtMove}, by the ray {me.LookingByRay}), offered \"{said}\", not \"{want}\"; a move offered {offer != null}; {npc.NpcName} {dist:0.00} m from the eye, the wagon's rim {rim:0.00} m; the aim {aimOff:0.0} degrees off it at yaw {Mathf.RadToDeg(me.SeatYaw):0}");
            return false;
        }
        var movesBefore = rc.MovesTaken;
        var car0 = rc.CarOf(me);
        GD.Print($"[boarders] I1 {where}: at speed (v {rc.State.V:0.00}, yaw {Mathf.RadToDeg(me.SeatYaw):0}), a move to {(offer is TrainWagon ow ? $"W{ow.Number}" : "the cart")} offered by the yaw, the look found {(looking as Node)?.Name} {(me.LookingByRay ? $"by the ray (looked through {me.LookedThrough})" : "as the car the yaw points to (the ray found nothing)")} (its rim {rim:0.00} m from the eye, {npc.NpcName} {dist:0.00} m): \"{said}\"");
        party.VrUse(true);
        party.VrUse(false);
        GD.Print($"[boarders] I1 {where}: the press: {npc.NpcName} down {npc.Downed}, moves taken {rc.MovesTaken - movesBefore}, the lead in car {rc.CarOf(me)}");
        if (npc.Downed || rc.MovesTaken != movesBefore || rc.CarOf(me) != car0 || rc.Hopping(me)) { Fail($"{where}: the press did not revive {npc.NpcName} (or it moved the lead)"); return false; }
        _bdDowned = null;
        return true;
    }

    /// <summary>The point of <paramref name="wagon"/>'s rim (its tub's top, 0.2 m in) nearest <paramref name="eye"/>.</summary>
    private static Vector3 BdRimNearest(TrainWagon wagon, Vector3 eye)
    {
        var local = wagon.GlobalTransform.AffineInverse() * eye;
        var x = Mathf.Clamp(local.X, -MineCart.TubWidth / 2f + 0.2f, MineCart.TubWidth / 2f - 0.2f);
        var z = Mathf.Clamp(local.Z, -MineCart.TubLength / 2f + 0.2f, MineCart.TubLength / 2f - 0.2f);
        return wagon.GlobalTransform * new Vector3(x, MineCart.TubBottom + MineCart.TubHeight, z);
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
                Next(3);
                break;
            case 3:
            {
                // I1 (b): from the cart, a W1 crew member down, the lead looking back at them
                if (t < 0.3f) break;
                if (rc.CarOf(me) != 0) { Fail($"the lead is in car {rc.CarOf(me)}, not the cart"); return; }
                _bdDowned = W(1).Riders.Values.OfType<Npc>().Where(n => n.Crew && !n.Dead).OrderBy(n => n.Seat).FirstOrDefault();
                if (_bdDowned == null) { Fail("no crew in W1"); return; }
                _bdDowned.GoDown();
                GD.Print($"[boarders] I1 (b): {_bdDowned.NpcName} (W1 seat {_bdDowned.Seat}) put down by the test; the lead in the cart seat {me.Seat}");
                Next(4);
                break;
            }
            case 4:
                if (!BdSeatRevive(rc, party, me, W(1), t, "(b) from the cart", byRay: false, Fail)) break;
                me.SeatYaw = BdBack;
                Next(60);
                break;

            // plan 15 Task 0, N1: a hound held alive on W1 beside a downed crew member; the refused revive never masks the climb
            case 60:
            {
                if (t < 0.3f) break;
                if (rc.MovesRefused || rc.State.Mode != CartMode.Rolling || BdRideLeft(rc) < 30f) { if (t > 60f) Fail("N1: no rolling stretch with moves allowed"); break; }
                enc.DebugHoldAnswers = true; // nobody else climbs to W1's place
                _bdHound = enc.DebugBoard(1, holdHp: true);
                if (_bdHound == null) { Fail("N1: no hound could be put on W1"); return; }
                _bdDowned = W(1).Riders.Values.OfType<Npc>().Where(n => n.Crew && !n.Dead).OrderBy(n => n.Seat).FirstOrDefault();
                if (_bdDowned == null) { Fail("N1: no crew in W1"); return; }
                if (!_bdDowned.Downed) _bdDowned.GoDown();
                _bdThrowsBase = enc.Throws;
                me.SeatYaw = BdBack;
                GD.Print($"[boarders] N1: a hound held on W1, {_bdDowned.NpcName} (W1 seat {_bdDowned.Seat}) put down by the test {_bdDowned.GlobalPosition.DistanceTo(_bdHound.GlobalPosition):0.00} m from it (the clear radius {RitualRules.ReviveClearRadius} m); the lead in the cart seat {me.Seat}");
                Next(61);
                break;
            }
            case 61:
            {
                var npc = _bdDowned!;
                var h = _bdHound!;
                if (!IsInstanceValid(h) || !h.IsAlive || h.Carrier != W(1)) { Fail("N1: W1's hound is gone before the look"); return; }
                if (!npc.Downed || npc.Dead) { Fail($"N1: {npc.NpcName} is no longer down (dead {npc.Dead})"); return; }
                if (t < 0.3f) break;
                var clock = enc.ThrowClockOf(1);
                var offer = rc.MoveOfferFor(me);
                var said = me.LookingAt?.Prompt ?? "";
                var plain = rc.PlainPromptOf(W(1), me);
                if (npc.ReviveTaken || offer != W(1) || !ReferenceEquals(me.LookingAt, W(1)) || !me.LookingAtMove || said != TrainMoveRules.Prompt(0, 1) || plain != "" || clock is not { Alone: true } || !(clock.Running > 0f))
                {
                    if (t > 1.6f) Fail($"N1: the revive taken {npc.ReviveTaken}, W1's plain prompt \"{plain}\"; looking at {(me.LookingAt as Node)?.Name ?? "nothing"} (the move's {me.LookingAtMove}) offered \"{said}\", not \"{TrainMoveRules.Prompt(0, 1)}\"; a move to W1 offered {offer == W(1)}; W1's clock alone {clock?.Alone} at {clock?.Running:0.00} s");
                    break;
                }
                GD.Print($"[boarders] N1: from the cart (v {rc.State.V:0.00}, yaw {Mathf.RadToDeg(me.SeatYaw):0}), {npc.NpcName} down beside a living hound {npc.GlobalPosition.DistanceTo(h.GlobalPosition):0.00} m off: the revive taken {npc.ReviveTaken}, W1's plain prompt \"{plain}\"; the look offers \"{said}\" (the move's {me.LookingAtMove}); W1's clock alone at {clock.Running:0.00} s");
                party.VrUse(true);
                party.VrUse(false);
                if (!rc.Hopping(me)) { Fail($"N1: the press took no move to W1 (refused: {rc.LastMoveRefused ?? "-"})"); return; }
                _bdPressedAt = Engine.GetPhysicsFrames();
                Next(62);
                break;
            }
            case 62:
            {
                var npc = _bdDowned!;
                var clock = enc.ThrowClockOf(1);
                if (rc.Hopping(me))
                {
                    if (t > TrainMoveRules.HopSeconds + 0.5f) Fail("N1: the hop to W1 never landed");
                    break;
                }
                if (rc.CarOf(me) != 1) { Fail($"N1: the lead landed in car {rc.CarOf(me)}, not W1"); return; }
                if (_bdClimbFrame <= _bdPressedAt) { if (t > TrainMoveRules.HopSeconds + 0.5f) Fail("N1: the landing's line was not logged"); break; }
                var since = (long)Engine.GetPhysicsFrames() - (long)_bdClimbFrame; // ticks since the landing's own tick
                if (clock is null or { Alone: true }) { if (since > 2) Fail($"N1: W1's clock is still counting alone ({clock?.Running:0.00} s) {since} ticks after the lead landed"); break; }
                GD.Print($"[boarders] N1: the lead landed in W1 seat {me.Seat} (hop {(_bdClimbFrame - _bdPressedAt) / BdTps:0.00} s); {since} tick(s) later W1's clock alone {clock.Alone} at {clock.Running:0.00} s; throws {enc.Throws - _bdThrowsBase}");
                if (clock.Running != 0f || enc.Throws != _bdThrowsBase || !npc.Downed || npc.Dead) { Fail("N1: W1's clock was not reset on the landing (or a throw came)"); return; }
                // the lag race: a "Revive" pressed on a screen behind lands now, from W1's own seat at speed, the revive still refused (the hound
                // beside them): refused out loud, no move, no get-out, never dropped
                int refusedBefore = rc.WagonUsesRefused, plainBefore = rc.WagonPlainUses, movesBefore = rc.MovesTaken, saidBefore = _bdSaid.Count(x => x.StartsWith("Not with enemies", StringComparison.Ordinal));
                W(1).Interact(party, me);
                var saidNow = _bdSaid.Count(x => x.StartsWith("Not with enemies", StringComparison.Ordinal));
                GD.Print($"[boarders] N1: a stale press on W1 from its own seat at speed: refused out loud {rc.WagonUsesRefused - refusedBefore} (\"Not with enemies this close.\" said {saidNow - saidBefore}), plain uses {rc.WagonPlainUses - plainBefore}, moves taken {rc.MovesTaken - movesBefore}; the lead in car {rc.CarOf(me)}, {npc.NpcName} down {npc.Downed}");
                if (rc.WagonUsesRefused != refusedBefore + 1 || saidNow != saidBefore + 1 || rc.WagonPlainUses != plainBefore || rc.MovesTaken != movesBefore || rc.CarOf(me) != 1 || rc.Hopping(me) || !npc.Downed) { Fail("N1: the stale press was not refused out loud (or it was taken as something)"); return; }
                enc.DebugKill(_bdHound!);
                Next(63);
                break;
            }
            case 63:
            {
                var npc = _bdDowned!;
                if (IsInstanceValid(_bdHound) && _bdHound!.IsAlive) { if (t > 1f) Fail("N1: W1's hound was not killed"); break; }
                if (npc.Downed && !npc.TryRevive()) { if (t > 1f) Fail($"N1: {npc.NpcName} could not be got up with the hound dead"); break; }
                GD.Print($"[boarders] N1: W1's hound killed and {npc.NpcName} got up by the test {t:0.00} s on; the answers run again");
                _bdDowned = null;
                enc.DebugHoldAnswers = false;
                Next(2);
                break;
            }
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
                Next(5);
                break;
            }
            case 5:
            {
                // I1 (a): stop 2's crew in W2, the lead climbs back to it
                if (ObjectiveController.Instance is not CrewRescueController ctrl || ctrl.DebugSeatCrew(2, 2) is not { Count: 2 } crew || crew.Any(c => c.Wagon != 2)) { Fail("stop 2's crew are not the two seated in W2"); return; }
                GD.Print($"[boarders] I1 (a): stop 2's cage opened by the test's hook: [{string.Join(", ", crew.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}]");
                me.SeatYaw = BdBack;
                Next(6);
                break;
            }
            case 6:
            {
                if (t < 0.15f) break;
                if (rc.MoveOfferFor(me) != W(2) || !ReferenceEquals(me.LookingAt, W(2)) || W(2).Prompt != "Climb back to wagon 2") { if (t > 1.6f) Fail($"no \"Climb back to wagon 2\" offered from W1 (prompt \"{W(2).Prompt}\")"); break; }
                party.VrUse(true);
                party.VrUse(false);
                if (!rc.Hopping(me)) { Fail($"the press took no move to W2 (refused: {rc.LastMoveRefused ?? "-"})"); return; }
                Next(7);
                break;
            }
            case 7:
            {
                if (rc.Hopping(me) || t < TrainMoveRules.MoveCooldown + 0.1f) break;
                if (rc.CarOf(me) != 2) { Fail($"the lead landed in car {rc.CarOf(me)}, not W2"); return; }
                _bdDowned = W(2).Riders.Values.OfType<Npc>().Where(n => n.Crew && !n.Dead).OrderBy(n => n.Seat).FirstOrDefault();
                if (_bdDowned == null) { Fail("no crew in W2"); return; }
                _bdDowned.GoDown();
                GD.Print($"[boarders] I1 (a): {_bdDowned.NpcName} (W2 seat {_bdDowned.Seat}) put down by the test; the lead in W2 seat {me.Seat}");
                Next(8);
                break;
            }
            case 8:
                if (!BdSeatRevive(rc, party, me, W(2), t, "(a) seated in W2", byRay: true, Fail)) break;
                me.SeatYaw = BdForward;
                Next(9);
                break;
            case 9:
            {
                if (t < 0.15f) break;
                if (rc.MoveOfferFor(me) != W(1) || !ReferenceEquals(me.LookingAt, W(1)) || W(1).Prompt != "Climb forward to wagon 1") { if (t > 1.6f) Fail($"no \"Climb forward to wagon 1\" offered from W2 (prompt \"{W(1).Prompt}\")"); break; }
                party.VrUse(true);
                party.VrUse(false);
                if (!rc.Hopping(me)) { Fail($"the press took no move to W1 (refused: {rc.LastMoveRefused ?? "-"})"); return; }
                Next(19);
                break;
            }
            case 19:
                if (rc.Hopping(me)) break;
                if (rc.CarOf(me) != 1) { Fail($"the lead landed in car {rc.CarOf(me)}, not W1"); return; }
                me.SeatYaw = BdForward; // the seat turns at most 150 degrees either way: W1's rim spot (back right) is reached turning right from forward
                Next(_bdN2Done ? 10 : 90);
                break;

            // plan 15 Task 0, N2: from the cart, W1's place taken, a downed crew member in W2 beyond the server's reach: the climb to W2 is
            // offered, not the revive
            case 90:
            {
                // the lead in W1 climbs forward into the cart
                if (t < TrainMoveRules.MoveCooldown + 0.1f) break;
                if (rc.MovesRefused || rc.State.Mode != CartMode.Rolling || BdRideLeft(rc) < 30f) { if (t > 60f) Fail("N2: no rolling stretch with moves allowed"); break; }
                if (rc.MoveOfferFor(me) != rc.Cart || !ReferenceEquals(me.LookingAt, rc.Cart) || rc.Cart.Prompt != TrainMoveRules.Prompt(1, 0)) { if (t > 3f) Fail($"N2: no \"{TrainMoveRules.Prompt(1, 0)}\" offered from W1 (prompt \"{rc.Cart.Prompt}\")"); break; }
                party.VrUse(true);
                party.VrUse(false);
                if (!rc.Hopping(me)) { Fail($"N2: the press took no move into the cart (refused: {rc.LastMoveRefused ?? "-"})"); return; }
                Next(91);
                break;
            }
            case 91:
            {
                if (rc.Hopping(me) || t < TrainMoveRules.MoveCooldown + 0.1f) break;
                if (rc.CarOf(me) != 0) { Fail($"N2: the lead landed in car {rc.CarOf(me)}, not the cart"); return; }
                if (rc.MovesRefused || rc.State.Mode != CartMode.Rolling || BdRideLeft(rc) < 30f) { if (t > 60f) Fail("N2: no rolling stretch with moves allowed"); break; }
                enc.DebugHoldAnswers = true; // W1's place-holder stays there (no stray goes back to the cart)
                _bdMate = party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && rc.CarOf(a) == 0 && !a.Downed && !rc.Hopping(a));
                if (_bdMate == null || !rc.DebugSeat(_bdMate, 1)) { Fail($"N2: no cart companion could be seated in W1's place ({_bdMate?.State.Name ?? "none"})"); return; }
                _bdDowned = W(2).Riders.Values.OfType<Npc>().Where(n => n.Crew && !n.Dead).OrderBy(n => n.Seat).FirstOrDefault();
                if (_bdDowned == null) { Fail("N2: no crew in W2"); return; }
                _bdDowned.GoDown();
                me.SeatYaw = BdBack;
                GD.Print($"[boarders] N2: {_bdMate.State.Name} holds W1's place; {_bdDowned.NpcName} (W2 seat {_bdDowned.Seat}) put down by the test; the lead in the cart seat {me.Seat}");
                Next(92);
                break;
            }
            case 92:
            {
                if (t < 0.3f) break;
                var npc = _bdDowned!;
                var want = TrainMoveRules.Prompt(0, 2);
                var tub = W(2).TubDistance(me);
                var plain = rc.PlainPromptOf(W(2), me);
                var offer = rc.MoveOfferFor(me);
                var said = me.LookingAt?.Prompt ?? "";
                if (!npc.ReviveTaken || plain != $"Revive {npc.NpcName}" || offer != W(2) || !ReferenceEquals(me.LookingAt, W(2)) || !me.LookingAtMove || said != want || W(2).Prompt != want || tub <= PropPromptRules.InteractReach)
                {
                    if (t > 1.6f) Fail($"N2: the revive taken {npc.ReviveTaken}, W2's plain prompt \"{plain}\", W2 {tub:0.00} m from the lead (reach {PropPromptRules.InteractReach}); looking at {(me.LookingAt as Node)?.Name ?? "nothing"} (the move's {me.LookingAtMove}) offered \"{said}\", not \"{want}\"; the move offered to {(offer as Node)?.Name ?? "nothing"}");
                    break;
                }
                GD.Print($"[boarders] N2: from the cart seat {me.Seat} (v {rc.State.V:0.00}, yaw {Mathf.RadToDeg(me.SeatYaw):0}), W1's place {_bdMate!.State.Name}'s: W2's plain prompt \"{plain}\" stands {W(2).TubDistance(me):0.00} m off (beyond the reach {PropPromptRules.InteractReach} m), so the look offers \"{said}\" (the move's {me.LookingAtMove})");
                party.VrUse(true);
                party.VrUse(false);
                if (!rc.Hopping(me) || !npc.Downed) { Fail($"N2: the press took no move to W2 (refused: {rc.LastMoveRefused ?? "-"}), or it revived {npc.NpcName}"); return; }
                Next(93);
                break;
            }
            case 93:
            {
                if (rc.Hopping(me)) break;
                if (rc.CarOf(me) != 2) { Fail($"N2: the lead landed in car {rc.CarOf(me)}, not W2"); return; }
                if (!rc.DebugSeat(_bdMate!, 0)) { Fail($"N2: {_bdMate!.State.Name} could not go back to the cart"); return; }
                enc.DebugHoldAnswers = false;
                Next(94);
                break;
            }
            case 94:
                // seated in W2 the revive is within reach: offered by what the ray found, and taken (as (a))
                if (!BdSeatRevive(rc, party, me, W(2), t, "N2 seated in W2", byRay: true, Fail)) break;
                _bdN2Done = true;
                me.SeatYaw = BdForward;
                Next(9);
                break;

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
                PumperCap = 0; // the cart's own pump picks nobody (Task 4 re-review 2): only DebugPump makes a shooter pump, so 3b measures the hand-off alone
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
                PumperCap = null;
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
            {
                if (!_bdLeverChecked) { if (t > 120f) Fail("no seated lever window was met"); break; }
                // M8: possessed away from in W1, the old lead goes back to the cart by itself (a hero seat of it is free: the lead's)
                if (rc.State.Mode != CartMode.Rolling || rc.MovesRefused || rc.Hopping(me)) { if (t > 120f) Fail("no rolling stretch with moves allowed after the lever window"); break; }
                if (rc.CarOf(me) != 1) { Fail($"the lead is in car {rc.CarOf(me)}, not W1, before the possession check"); return; }
                var next = party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && rc.CarOf(a) == 0 && !a.Downed);
                if (next == null || !rc.HeroSeatFree()) { Fail($"no cart companion to take over, or no free hero seat in the cart (free {rc.HeroSeatFree()})"); return; }
                _bdOldLead = me;
                party.AllowPossession = true;
                party.Possess(Array.IndexOf(party.Adventurers, next));
                party.AllowPossession = false;
                GD.Print($"[boarders] M8: the player takes {next.State.Name} (car {rc.CarOf(next)}); {me.State.Name} is left in W1 as a companion ({me.Control}); strays [{string.Join(", ", enc.StraysNow)}]");
                if (party.Local != next || me.Control != ControlKind.Companion) { Fail("the possession did not take"); return; }
                Next(51);
                break;
            }
            case 51:
            {
                var old = _bdOldLead!;
                if (rc.CarOf(old) != 0 || rc.Hopping(old))
                {
                    if (t > TrainMoveRules.MoveCooldown + TrainMoveRules.HopSeconds + 1f) Fail($"{old.State.Name}, left in W1, did not go back to the cart by itself within {TrainMoveRules.MoveCooldown + TrainMoveRules.HopSeconds + 1f:0.0} s (in car {rc.CarOf(old)}, strays [{string.Join(", ", enc.StraysNow)}], moves refused {rc.MovesRefused})");
                    break;
                }
                GD.Print($"[boarders] M8: {old.State.Name} is back in the cart seat {old.Seat} by itself {t:0.00} s after the player took another hero");
                Next(52);
                break;
            }
            case 52:
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): three melee blows knocked a W1 hound off (grip 1, 2, 3), alive beside the lane; one spell knocked one off; W3 answered from a seat with a shot (no move), then with the shooters down {_bdMate?.State.Name} climbed to W3 one move per cooldown, ended its hound and moved back 5 s after; a killed hound dropped beside the lane; no Get out at speed; the lever window refused a move; at speed a downed crew member was offered and revived from the cart (W1) and from W2's own seat with a move offered all the while (the final review's I1); a revive refused (a hound beside them on W1) never masked the climb, whose landing reset W1's throw clock, and a stale press there was refused out loud (N1); with W1's place taken the cart rider was offered the climb to W2, not its revive beyond the reach (N2); the lead possessed away from in W1 went back to the cart by itself (M8); knock-offs {enc.KnockOffs}");
                _done = true;
                GetTree().Quit(0);
                break;
        }
    }
}
