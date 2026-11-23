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
//  - Pass 4: DebugKill of a W1 hound drops it beside the lane.
public partial class DebugTools
{
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
}
