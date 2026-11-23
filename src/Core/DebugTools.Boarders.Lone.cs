using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders=lone (plan 14, Task 5): see DebugTools.Boarders.cs for the flow as a whole ------------------------------------------
// As =throw (nobody answers, the companions hold fire, the hound never drops below 1 HP), but restored at stop 1 with its two crew seated in W4,
// and a companion seated in W2's place. A hound is put on W4 in the frame the lead's first move is pressed; the lead presses back from the cart
// at each cooldown with the grip, cart -> W1 -> W3 (passing W2) -> W4. It lands in W4 within TrainMoveRules.ThrowBound(4) = 5.6 s of the
// hound's landing, the clock resets (not alone, nought counted) and no glow or throw follows (watched to 8 s after the landing). Prints
// "[boarders] lone: in W4 {t} s after the hound landed, {6 - t} s to spare".
public partial class DebugTools
{
    private void BoardersLone(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT;
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        TrainWagon W(int k) => rc.WagonAt(k)!;
        if (rc.State.Mode != CartMode.Rolling) { Fail($"the train stopped ({rc.State.Mode} at stop {rc.StopIndex}) at phase {_bdPhase}, before the script was done"); return; }
        // the lead's back move, offered on wagon k with its prompt and pressed with the grip: true once taken
        bool Press(int k)
        {
            var prompt = TrainMoveRules.Prompt(rc.CarOf(me), k);
            if (rc.Hopping(me) || rc.MoveOfferFor(me) != W(k) || !ReferenceEquals(me.LookingAt, W(k)) || W(k).Prompt != prompt) return false;
            var before = rc.MovesTaken;
            party.VrUse(true);
            party.VrUse(false);
            if (rc.MovesTaken != before + 1 || !rc.Hopping(me)) { Fail($"the press on \"{prompt}\" took no move (refused: {rc.LastMoveRefused ?? "-"})"); return false; }
            _bdAnswerHops.Add((BdSince(Engine.GetPhysicsFrames()), rc.CarOf(me), k));
            GD.Print($"[boarders] lone: {me.State.Name} pressed \"{prompt}\" {BdSince(Engine.GetPhysicsFrames()):0.00} s after the hound landed");
            return true;
        }
        void Watch()
        {
            // the clock of W4 while it counts: the last value before the lead landed
            if (enc.ThrowClockOf(4) is { Alone: true } c) _bdHp1 = c.Running;
        }
        switch (_bdPhase)
        {
            case 0:
                enc.DebugHoldAnswers = true;
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true;
                me.SeatYaw = BdBack;
                Next(1);
                break;
            case 1:
            {
                if (t < 0.5f || rc.MovesRefused) break;
                if (BdRideLeft(rc) < 60f) { Fail($"ride 2 has {BdRideLeft(rc):0} m left"); return; }
                _bdMate = party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && rc.CarOf(a) == 0 && !a.Downed);
                if (_bdMate == null || !rc.DebugSeat(_bdMate, 2)) { Fail("no companion could be seated in W2's place"); return; }
                GD.Print($"[boarders] lone: {_bdMate.State.Name} seated in W2's place; W4 crew [{string.Join(", ", W(4).Riders.Values.OfType<Npc>().Select(n => $"{n.NpcName} seat {n.Seat}"))}]");
                Next(2);
                break;
            }
            case 2:
            {
                // the hound lands in the frame of the first press
                if (t < 0.3f || rc.MovesRefused || rc.MoveOfferFor(me) != W(1) || !ReferenceEquals(me.LookingAt, W(1))) { if (t > 3f) Fail($"no move to W1 offered (prompt \"{W(1).Prompt}\")"); break; }
                _bdLogMark = enc.ThrowLog.Count;
                _bdThrowsBase = enc.Throws;
                _bdAnswerHops.Clear();
                _bdHp1 = -1f;
                _bdHound = enc.DebugBoard(4, holdHp: true);
                if (_bdHound == null) { Fail("no hound could be put on W4"); return; }
                _bdLandFrame = Engine.GetPhysicsFrames();
                GD.Print($"[boarders] lone: a hound lands on W4 at tick {_bdLandFrame} (S={rc.State.S:0.0}, v {rc.State.V:0.00})");
                _bdMovesBefore = rc.MovesTaken;
                if (!Press(1)) { if (!_done) Fail("the first press was not taken"); return; }
                Next(3);
                break;
            }
            case 3: Watch(); if (rc.CarOf(me) == 1 && Press(3)) Next(4); else if (t > 3f) Fail($"no move on from W1 (in car {rc.CarOf(me)}, offered {(rc.MoveOfferFor(me) is TrainWagon o ? $"W{o.Number}" : "nothing")})"); break;
            case 4: Watch(); if (rc.CarOf(me) == 3 && Press(4)) Next(5); else if (t > 3f) Fail($"no move on from W3 (in car {rc.CarOf(me)})"); break;
            case 5:
            {
                Watch();
                if (rc.CarOf(me) != 4 || rc.Hopping(me)) { if (t > 2f) Fail($"the lead never landed in W4 (in car {rc.CarOf(me)})"); break; }
                var at = BdSince(Engine.GetPhysicsFrames());
                var clock = enc.ThrowClockOf(4);
                var log = BdThrowEntries(enc, 4, _bdLogMark);
                GD.Print($"[boarders] lone: in W4 {at:0.00} s after the hound landed, {BoarderRules.ThrowFirst - at:0.00} s to spare (bound {TrainMoveRules.ThrowBound(4):0.0} s); moves [{string.Join(", ", _bdAnswerHops.Select(h => $"{h.From}>{h.To} at {h.At:0.00} s"))}]; W4's clock counted {_bdHp1:0.00} s, now alone {clock?.Alone}, {clock?.Running:0.00} s; its log [{string.Join(", ", log.Select(e => $"{e.Step} at {BdSince(e.Frame):0.00} s by {e.Who}"))}]");
                if (at > TrainMoveRules.ThrowBound(4)) { Fail($"the lead reached W4 {at:0.00} s after the hound landed, past {TrainMoveRules.ThrowBound(4):0.0} s"); return; }
                if (rc.MovesTaken - _bdMovesBefore != 3 || !_bdAnswerHops.Select(h => (h.From, h.To)).SequenceEqual(new[] { (0, 1), (1, 3), (3, 4) })) { Fail("the lead's climb was not cart > W1 > W3 > W4 in three moves"); return; }
                if (clock == null || clock.Alone || clock.Running != 0f || _bdHp1 <= 0f) { Fail("W4's clock did not count, or did not reset when the lead landed"); return; }
                if (log.Count != 1 || log[0].Step != ThrowStep.Call) { Fail("W4's clock did more than its call before the lead landed"); return; }
                Next(6);
                break;
            }
            case 6:
            {
                if (BdSince(Engine.GetPhysicsFrames()) < 8f) { if (enc.Throws != _bdThrowsBase || rc.CarOf(me) != 4) { Fail($"a throw came (or the lead left W4: car {rc.CarOf(me)})"); return; } break; }
                var log = BdThrowEntries(enc, 4, _bdLogMark);
                var crew = W(4).Riders.Values.OfType<Npc>().Count(n => n.Crew && !n.Dead);
                GD.Print($"[boarders] lone: 8 s after the landing: throws {enc.Throws - _bdThrowsBase}, W4's log [{string.Join(", ", log.Select(e => e.Step))}], {crew} crew in W4");
                if (enc.Throws != _bdThrowsBase || log.Count != 1 || crew != 2) { Fail("with the lead in W4 the hound still glowed or threw"); return; }
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): the lead climbed cart > W1 > W3 (past {_bdMate?.State.Name} in W2) > W4 in three moves and landed {_bdAnswerHops[^1].At + TrainMoveRules.HopSeconds:0.00} s after the hound, inside {TrainMoveRules.ThrowBound(4):0.0} s; the clock reset and nobody was thrown");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }
}
