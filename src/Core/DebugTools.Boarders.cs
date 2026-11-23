using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders[=move|reach|knock|throw|lone|pause|groups] (plan 14, Tasks 3-7) ------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo; every enemy is culled as it appears. The variants are plan 14's (Ruling 22): =move
// (Task 3) is built; reach, knock, throw, lone, pause and groups are later tasks' and, like an unknown name, print an ERROR and fail.
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
    private Adventurer? _bdMate3, _bdMate2;

    /// <summary>The variants plan 14 names for this flow (Ruling 22), and the ones built so far.</summary>
    private static readonly string[] BoardersVariants = { "move", "reach", "knock", "throw", "lone", "pause", "groups" };
    private static readonly string[] BoardersBuilt = { "move" };

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
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill(); // the flow owns the plating and the HP
        enc.RunawayClock = 0f; // no traffic runaway on the party line
        if (party.Local is not { } me) { Fail("no human"); return; }

        switch (_bdStep)
        {
            case 0 when _bdClock >= 1.5f:
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(0);
                GD.Print($"[boarders] floor 12 ({v}): restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Number))}]; lethality {TierRules.Lethality}");
                if (rc.Train.Count != 4) { Fail($"the train has {rc.Train.Count} wagons, not 4"); return; }
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
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 1 || rc.State.V < 1f || _bdClock < 1f) { if (_bdClock > 20f) Fail($"ride 1 did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                GD.Print($"[boarders] rolling on ride 1 at S={rc.State.S:0.0} (v {rc.State.V:0.00}); moves refused {rc.MovesRefused}");
                _bdStep++; _bdClock = 0f; _bdPhase = 0;
                break;
            case 4:
                BoardersMove(rc, enc, party, me, Fail);
                break;
        }
    }

    /// <summary>The landing and refusal lines, heard once per run.</summary>
    private void BoardersListen()
    {
        if (_bdListening) return;
        _bdListening = true;
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

            // the double press: the lead's press forward to W1 and a cart companion's forced move to W1, in one frame
            case 21:
            {
                if (t < 0.15f) break;
                if (rc.MoveOfferFor(me) != W(1)) { if (t > 1.6f) Fail("from W2 looking forward no move to W1 was offered"); break; }
                var mate = _bdMate2!;
                party.VrUse(true);
                party.VrUse(false);
                var pressed = rc.Hopping(me);
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
                W(1).Interact(party, me); // the get-out press, as the key would
                if (me.Seated) { Fail("the lead did not get out of W1 at stop 1"); return; }
                var onFoot = rc.MoveOfferFor(me);
                GD.Print($"[boarders] on foot beside the lane at {Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition)}: offered {(onFoot == null ? "no move" : "a move")}");
                if (onFoot != null) { Fail("a move was offered on foot"); return; }
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): moves taken {rc.MovesTaken}, landed {rc.MovesLanded}, dropped {rc.MovesDropped}; the hop {TrainMoveRules.HopSeconds} s within a tick, the cooldown held, W2 to W4 past a companion in one move, forward to the cart, r1_beam cleared by {_bdHeads.Min:0.00} m with the lead in W2, the double press refused (the place is booked), the mid-hop down dropped; never offered held, downed or on foot");
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
}
