using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders=throw (plan 14, Task 5; the default): see DebugTools.Boarders.cs for the flow as a whole ----------------------------
// Restored at stop 1; its cage is opened and its two crew walk out of it to their seats in W3 as in play (fix round 1, I1: they stand on the
// ground first, CrewRescueController.DebugBoardWagon only picks the wagon), rolling on ride 2 with every hero in the cart, the lead included. Nobody answers
// (DebugHoldAnswers), the companions hold fire (DebugHoldFire) and the hounds never drop below 1 HP (DebugBoard holdHp), so the throw clock alone
// is measured, in physics frames from the hound's landing.
//  - Part 1: a hound is put on W3. The call comes within 0.1 s of the landing (the nearest seated hero's line, or the log's "It's at the
//    prisoners — wagon 3!"); the glows at 4.5 ± 0.1 s and 8.5 ± 0.1 s; the throws at 6.0 ± 0.1 s (seat 5's crew member) and 10.0 ± 0.1 s (seat
//    4's), none of it paused, each glow told as the BoarderThrow telegraph in its tick (fix round 1, I2). Each thrown crew member flies from over
//    the rim, rises above its seat and lies down more than 1 m out on the enemy lane's side (fix round 1, I1 and M1), is dead, unseated, hidden
//    1.2 s (72 ticks, ± 2) after its throw and Fled from then, lost
//    (LostCrew), logged "{name} is thrown from the wagon!", and sent as PropEvt wagon:3 "thrown:{id}". W3 with no crew left: its clock is back at 0.
//  - Part 2 (Task 4 review M2): stop 2's crew are seated in W2 (both bench seats), a shooter companion is seated in W2's place and downed
//    (Mangled), the other shooters pump (DebugPump: no seat answer) and the cart's own pump is held (PumperCap 0); a hound is put on W2 and the
//    answers run. The cart's blade answers by climbing: it waits one car short (both bench seats crew, the place a downed hero's) until the 6 s
//    throw empties seat 5, then lands on W2 seat 5 (within a cooldown and a hop); no second throw comes. Fix round 1: from that bench seat its
//    blows end W2's hound (its fire let go, the hound's HP no longer held), printed with the bench seat's distance to the rim spot.
//  - Part 3: a save and a load: every thrown crew member is still dead, fled, hidden and unseated (no body), and lost.
public partial class DebugTools
{
    private const string BdSlot = "boarderstest";

    /// <summary>Physics ticks per second (the clock's step).</summary>
    private static float BdTps => Engine.PhysicsTicksPerSecond;

    /// <summary>Seconds from the hound's landing (<see cref="_bdLandFrame"/>) to physics frame <paramref name="frame"/>.</summary>
    private float BdSince(ulong frame) => (frame - _bdLandFrame) / BdTps;

    /// <summary>The throw log's entries for wagon <paramref name="k"/> from index <paramref name="from"/> on.</summary>
    private static List<(int Wagon, ThrowStep Step, ulong Frame, float Counted, float Paused, string Who)> BdThrowEntries(RailEncounterController enc, int k, int from)
        => enc.ThrowLog.Skip(from).Where(e => e.Wagon == k).ToList();

    /// <summary>
    /// Follows every crew member thrown since the throw log's index <paramref name="from"/>: when it was thrown and the first frame it is seen hidden.
    /// Returns false (after failing) when a thrown one is not dead, still seated, or the log is not the controller's.
    /// </summary>
    private bool BdFollowThrown(RailEncounterController enc, CrewRescueController ctrl, int k, int from, Action<string> Fail)
    {
        foreach (var e in BdThrowEntries(enc, k, from).Where(e => e.Step == ThrowStep.Throw))
        {
            if (_bdThrownCrew.Any(c => c.Frame == e.Frame && c.Name == e.Who)) continue;
            var npc = ctrl.Crew.FirstOrDefault(c => IsInstanceValid(c) && c.NpcName == e.Who);
            if (npc == null) { Fail($"the thrown {e.Who} is no crew member"); return false; }
            _bdThrownCrew.Add((npc.Id, npc.NpcName, k, e.Frame, 0));
        }
        for (var i = 0; i < _bdThrownCrew.Count; i++)
        {
            var c = _bdThrownCrew[i];
            if (c.HiddenFrame != 0) continue;
            var npc = ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == c.Id);
            if (npc == null) { Fail($"the thrown {c.Name} is gone from the crew list"); return false; }
            if (!npc.Dead || npc.Seated) { Fail($"the thrown {c.Name} is dead {npc.Dead}, seated {npc.Seated}"); return false; }
            if (!npc.Visible) _bdThrownCrew[i] = c with { HiddenFrame = Engine.GetPhysicsFrames() };
            else if (npc.Fled) { Fail($"the thrown {c.Name} is Fled while still shown"); return false; }
            else
            {
                // its flight while shown (fix round 1, I1): the highest it rose, and where it first lay on the ground
                var f = _bdFlight.TryGetValue(c.Id, out var seen) ? seen : (MaxY: float.MinValue, Land: (Vector3?)null, Air: false);
                var p = npc.GlobalPosition;
                var onFloor = npc.IsOnFloor(); // read only once it has been seen in the air: its first frames still hold the floor state from before it sat
                _bdFlight[c.Id] = (Mathf.Max(f.MaxY, p.Y), f.Land ?? (f.Air && onFloor ? p : null), f.Air || !onFloor);
            }
        }
        return true;
    }

    /// <summary>Each thrown crew member's flight as the flow saw it while shown: the highest it rose and where it first lay on the ground.</summary>
    private readonly Dictionary<string, (float MaxY, Vector3? Land, bool Air)> _bdFlight = new();

    /// <summary>
    /// The judgement of each thrown crew member once hidden: dead, fled, unseated, hidden 1.2 s after its throw, lost, logged and sent; and (fix
    /// round 1, I1 and M1) its flight: it rose above its seat and above the rim it left from, and lay down more than 1 m out from the wagon, on the
    /// enemy lane's side (nearer the enemy lane than the party lane, by the cells it landed on).
    /// </summary>
    private bool BdThrownOk(CrewRescueController ctrl, Action<string> Fail)
    {
        var lost = ctrl.Capture().LostCrew ?? new List<string>();
        foreach (var c in _bdThrownCrew)
        {
            if (!ctrl.ThrowLaunches.TryGetValue(c.Id, out var launch) || !_bdFlight.TryGetValue(c.Id, out var flight) || flight.Land is not { } land) { Fail($"the thrown {c.Name}'s flight was not seen to land"); return false; }
            var wagon = launch.Wagon;
            var lateral = Mathf.Abs((land - wagon.Origin).Dot(wagon.Basis.X.Normalized()));
            var flat = new Vector2(land.X - wagon.Origin.X, land.Z - wagon.Origin.Z).Length();
            var layout = Main.Instance.Builder.Layout!;
            var cell = DungeonBuilder.WorldToCell(land);
            static int Near(IReadOnlyList<Dungeon.Generation.GridPos> lane, Dungeon.Generation.GridPos at) { var best = int.MaxValue; foreach (var l in lane) best = Math.Min(best, Math.Abs(l.X - at.X) + Math.Abs(l.Y - at.Y)); return best; }
            var toEnemy = Near(layout.Rail!.Enemy, cell);
            var toParty = Near(layout.Rail!.Party, cell);
            GD.Print($"[boarders] thrown: {c.Name}'s flight: seat y {launch.Seat.Y:0.00}, off the rim at y {launch.From.Y:0.00} at {launch.Velocity.Length():0.0} m/s (up {launch.Velocity.Y:0.0}), rose to y {flight.MaxY:0.00} ({flight.MaxY - launch.Seat.Y:+0.00;-0.00} m over its seat), lay down at {cell} {lateral:0.0} m out from the wagon's line ({flat:0.0} m from it), {toEnemy} cell(s) from the enemy lane and {toParty} from the party lane");
            if (flight.MaxY <= launch.From.Y + 0.05f || flight.MaxY <= launch.Seat.Y) { Fail($"the thrown {c.Name} never rose (top y {flight.MaxY:0.00}, launched at {launch.From.Y:0.00}, seat {launch.Seat.Y:0.00})"); return false; }
            if (lateral < 1f) { Fail($"the thrown {c.Name} lay down {lateral:0.0} m out from the wagon, not more than 1 m"); return false; }
            if (toEnemy > toParty) { Fail($"the thrown {c.Name} lay down on the party lane's side"); return false; }
        }
        foreach (var c in _bdThrownCrew)
        {
            var npc = ctrl.Crew.First(n => IsInstanceValid(n) && n.Id == c.Id);
            var hid = (c.HiddenFrame - c.Frame) / BdTps;
            var logged = _bdThrowSaid.Contains($"{c.Name} is thrown from the wagon!");
            var sent = _bdThrowProps.Contains($"{Net.NetRefs.Wagon(c.Wagon)} thrown:{c.Id}");
            GD.Print($"[boarders] thrown: {c.Name} ({c.Id}) off W{c.Wagon}: dead {npc.Dead}, fled {npc.Fled}, seated {npc.Seated}, hidden {hid:0.00} s after the throw, lost {lost.Contains(c.Id)} ({ctrl.StateOf(npc)}), logged {logged}, sent {sent}");
            if (!npc.Dead || !npc.Fled || npc.Seated || npc.Visible || !lost.Contains(c.Id) || ctrl.StateOf(npc) != CrewState.Lost || !logged || !sent) { Fail($"the thrown {c.Name} is not dead, fled, unseated, hidden, lost, logged and sent"); return false; }
            if (Mathf.Abs(hid - BoarderRules.ThrownHide) > 2f / BdTps + 1e-4f) { Fail($"{c.Name} was hidden {hid:0.000} s after its throw, not {BoarderRules.ThrownHide} s (within two ticks)"); return false; }
        }
        return true;
    }

    private void BoardersThrow(Main main, RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT;
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        TrainWagon W(int k) => rc.WagonAt(k)!;
        if (rc.State.Mode == CartMode.Wrecked) { Fail("the train was wrecked"); return; }
        if (_bdPhase < 20 && rc.State.Mode != CartMode.Rolling) { Fail($"the train stopped ({rc.State.Mode} at stop {rc.StopIndex}) at phase {_bdPhase}, before the script was done"); return; }
        if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
        Adventurer? Named(string? n) => party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.State.Name == n);
        switch (_bdPhase)
        {
            case 0:
            {
                var walked = W(3).Riders.Values.OfType<Npc>().Where(n => n.Crew && !n.Dead).ToList();
                GD.Print($"[boarders] throw: W3's crew walked to their seats from the cage: [{string.Join(", ", walked.Select(n => $"{n.NpcName} seat {n.Seat} {(_bdWalked.Contains(n.Id) ? "seen on the ground" : "never seen on foot")}"))}]");
                if (walked.Count != 2 || walked.Any(n => !_bdWalked.Contains(n.Id))) { Fail("W3's two crew did not walk to their seats on the ground"); return; }
                ctrl.DebugBoardWagon = 0;
                enc.DebugHoldAnswers = true;
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true;
                GD.Print($"[boarders] throw: stops at S=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}]; ride 2 has {BdRideLeft(rc):0} m left; W3 crew [{string.Join(", ", W(3).Riders.Values.OfType<Npc>().Select(n => $"{n.NpcName} seat {n.Seat}"))}]");
                Next(1);
                break;
            }

            // part 1: the call, the glows and the throws, measured from the landing
            case 1:
            {
                if (t < 0.5f || rc.MovesRefused) break;
                if (BdRideLeft(rc) < 80f) { Fail($"ride 2 has {BdRideLeft(rc):0} m left for part 1"); return; }
                if (!party.Active.All(a => rc.CarOf(a) == 0)) { Fail("not every hero rides in the cart"); return; }
                _bdLogMark = enc.ThrowLog.Count;
                _bdThrowsBase = enc.Throws;
                _bdThrownCrew.Clear();
                _bdSeatOf.Clear();
                foreach (var n in W(3).Riders.Values.OfType<Npc>()) if (n.Crew && !n.Dead) _bdSeatOf[n.Seat] = n.NpcName;
                _bdHound = enc.DebugBoard(3, holdHp: true);
                if (_bdHound == null) { Fail("no hound could be put on W3"); return; }
                _bdLandFrame = Engine.GetPhysicsFrames();
                GD.Print($"[boarders] throw: a hound lands on W3 at tick {_bdLandFrame} (S={rc.State.S:0.0}, v {rc.State.V:0.00})");
                Next(2);
                break;
            }
            case 2:
            {
                var from = _bdLogMark;
                if (!BdFollowThrown(enc, ctrl, 3, from, Fail)) return;
                var log = BdThrowEntries(enc, 3, from);
                if (log.Count(e => e.Step == ThrowStep.Throw) < 2 || _bdThrownCrew.Any(c => c.HiddenFrame == 0)) { if (t > 14f) Fail($"part 1: {log.Count(e => e.Step == ThrowStep.Throw)} throw(s) in 14 s"); break; }
                GD.Print($"[boarders] throw: W3's clock [{string.Join(", ", log.Select(e => $"{e.Step} at {BdSince(e.Frame):0.000} s ({e.Counted:0.000} counted, {e.Paused:0.00} paused, {e.Who})"))}]");
                var steps = log.Select(e => e.Step).ToArray();
                if (!steps.SequenceEqual(new[] { ThrowStep.Call, ThrowStep.Glow, ThrowStep.Throw, ThrowStep.Glow, ThrowStep.Throw })) { Fail($"W3's clock went [{string.Join(", ", steps)}], not call, glow, throw, glow, throw"); return; }
                var want = new[] { 0f, 4.5f, 6.0f, 8.5f, 10.0f };
                for (var i = 0; i < want.Length; i++)
                {
                    var at = BdSince(log[i].Frame);
                    if (i == 0 ? at > 0.1f : Mathf.Abs(at - want[i]) > 0.1f) { Fail($"the {log[i].Step} came {at:0.000} s after the landing, not {want[i]:0.0} ± 0.1"); return; }
                    if (log[i].Paused > 0f) { Fail($"part 1's clock was paused {log[i].Paused:0.00} s"); return; }
                }
                var seats = _bdThrownCrew.Select(c => c.Name).ToList();
                if (_bdSeatOf.Count != 2 || seats.Count != 2 || seats[0] != _bdSeatOf[5] || seats[1] != _bdSeatOf[4]) { Fail($"thrown [{string.Join(", ", seats)}], not seat 5's ({_bdSeatOf.GetValueOrDefault(5)}) then seat 4's ({_bdSeatOf.GetValueOrDefault(4)})"); return; }
                var caller = log[0].Who;
                var called = caller == "the log" ? _bdThrowSaid.Contains("It's at the prisoners — wagon 3!") : Named(caller) is { } spoke && rc.CarOf(spoke) >= 0;
                GD.Print($"[boarders] throw: called by {caller} ({(caller == "the log" ? "\"It's at the prisoners — wagon 3!\" logged" : "a seated hero of the train")} {called}); thrown seat 5's {seats[0]} then seat 4's {seats[1]}; throws {enc.Throws - _bdThrowsBase}");
                if (!called || enc.Throws - _bdThrowsBase != 2) { Fail("the call was not made by a seated hero or the log, or the throws were not two"); return; }
                if (!BdThrownOk(ctrl, Fail)) return;
                // fix round 1 (I2): each glow is told as the BoarderThrow telegraph in its own tick (clients glow it from the EnemyStateMsg)
                var told = _bdTelegraphs.Where(x => x.Hound == _bdHound).Select(x => x.Frame).ToList();
                var glows = log.Where(e => e.Step == ThrowStep.Glow).Select(e => e.Frame).ToList();
                GD.Print($"[boarders] throw: the glows' telegraph \"{Enemies.TelegraphRules.BoarderThrow}\" told at ticks [{string.Join(", ", told)}], the glows at [{string.Join(", ", glows)}]");
                if (!told.SequenceEqual(glows)) { Fail("the throw's glow was not told as its telegraph at each glow"); return; }
                var clock = enc.ThrowClockOf(3);
                if (clock == null || clock.Alone || clock.Running != 0f) { Fail($"W3 with no crew left is still counted (alone {clock?.Alone}, {clock?.Running:0.00} s)"); return; }
                enc.DebugKill(_bdHound!);
                Next(10);
                break;
            }

            // part 2 (Task 4 review M2): the answerer lands on a crew seat a throw freed when a downed hero holds the place
            case 10:
            {
                if (t < 0.5f || rc.MovesRefused) break;
                if (BdRideLeft(rc) < 70f) { Fail($"ride 2 has {BdRideLeft(rc):0} m left for part 2"); return; }
                if (ctrl.DebugSeatCrew(2, 2) is not { Count: 2 } crew) { Fail("stop 2's two crew could not be seated in W2"); return; }
                _bdSeatOf.Clear();
                foreach (var c in crew) _bdSeatOf[c.Seat] = c.Npc.NpcName;
                var companions = party.Adventurers.Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && rc.CarOf(a) == 0).ToList();
                _bdMate2 = companions.FirstOrDefault(a => a.Shooter);
                _bdMate = companions.FirstOrDefault(a => a.Blade);
                if (_bdMate2 == null || _bdMate == null) { Fail("a shooter and a blade companion in the cart are needed"); return; }
                if (!rc.DebugSeat(_bdMate2, 2)) { Fail($"{_bdMate2.State.Name} could not be seated in W2"); return; }
                _bdMate2.State.Mangled = true; // held down
                _bdMate2.State.Hp = 0;
                party.OnMemberDowned(_bdMate2.Index);
                _bdShootersDown.Clear();
                foreach (var a in companions.Where(a => a != _bdMate2 && a.Shooter)) { a.DebugPump = true; _bdShootersDown.Add(a); } // no seat answer
                PumperCap = 0; // the cart's own pump picks nobody: only the hook marks a pumper
                _bdHound = enc.DebugBoard(2, holdHp: true);
                if (_bdHound == null) { Fail("no hound could be put on W2"); return; }
                _bdLandFrame = Engine.GetPhysicsFrames();
                _bdLogMark = enc.ThrowLog.Count;
                _bdThrowsBase = enc.Throws;
                _bdAnswerHops.Clear();
                _bdWasHopping = false;
                _bdMarkFrame = 0;
                enc.DebugHoldAnswers = false;
                GD.Print($"[boarders] the downed place: W2 crew [{string.Join(", ", crew.Select(c => $"{c.Npc.NpcName} seat {c.Seat}"))}], {_bdMate2.State.Name} down in W2 seat {_bdMate2.Seat} (downed {_bdMate2.Downed}); pumping [{string.Join(", ", _bdShootersDown.Select(a => a.State.Name))}]; a hound lands on W2 at tick {_bdLandFrame}; the answers run");
                Next(11);
                break;
            }
            case 11:
            {
                var blade = _bdMate!;
                BdTrackHops(rc, blade);
                if (!BdFollowThrown(enc, ctrl, 2, _bdLogMark, Fail)) return;
                var answer = enc.AnswersNow.FirstOrDefault(a => a.Wagon == 2);
                if (answer.Name != null && answer.Name != blade.State.Name) { Fail($"W2 was answered by {answer.Name}, not the cart's blade {blade.State.Name}"); return; }
                if (answer.Name != null && answer.FromSeat) { Fail($"{answer.Name} answered W2 from its seat"); return; }
                var thrown = BdThrowEntries(enc, 2, _bdLogMark).Where(e => e.Step == ThrowStep.Throw).ToList();
                var car = rc.CarOf(blade);
                if (thrown.Count == 0)
                {
                    if (car == 2) { Fail($"{blade.State.Name} got into W2 (seat {blade.Seat}) with both its bench seats crew"); return; }
                    if (t > 8f) Fail("W2's first throw did not come");
                    break;
                }
                if (_bdMarkFrame == 0)
                {
                    _bdMarkFrame = thrown[0].Frame;
                    if (_bdThrownCrew.Count == 0 || _bdThrownCrew[^1].Name != _bdSeatOf[5]) { Fail($"W2's first throw took {thrown[0].Who}, not seat 5's {_bdSeatOf[5]}"); return; }
                    GD.Print($"[boarders] the downed place: {thrown[0].Who} thrown off W2 at {BdSince(thrown[0].Frame):0.000} s with {blade.State.Name} waiting in car {car} (moves [{string.Join(", ", _bdAnswerHops.Select(x => $"{x.From}>{x.To}"))}])");
                    if (car != 1) { Fail($"{blade.State.Name} waited in car {car}, not one car short (W1)"); return; }
                }
                if (car != 2 || rc.Hopping(blade)) { if ((Engine.GetPhysicsFrames() - _bdMarkFrame) / BdTps > TrainMoveRules.MoveCooldown + TrainMoveRules.HopSeconds + 0.2f) Fail($"{blade.State.Name} did not land in W2 within a cooldown and a hop of the throw (in car {car})"); break; }
                var after = (Engine.GetPhysicsFrames() - _bdMarkFrame) / BdTps;
                GD.Print($"[boarders] the downed place: {blade.State.Name} lands in W2 seat {blade.Seat} {after:0.00} s after the throw freed it (the place still {_bdMate2!.State.Name}'s, downed {_bdMate2.Downed}); W2 alone {enc.ThrowClockOf(2)?.Alone}");
                if (blade.Seat != TrainRules.CrewSeats[1] || W(2).Seats.OccupantOf(TrainRules.HeroPlace) != _bdMate2.RiderId) { Fail($"{blade.State.Name} sits in W2 seat {blade.Seat}, not the freed seat 5 beside the downed {_bdMate2.State.Name}"); return; }
                // fix round 1: from the bench its blows reach its own wagon's boarder. Its fire is let go and the hound's HP no longer held; the others
                // still hold theirs (the downed one cannot, the shooter pumps), so every hit is the bench blade's
                blade.DebugHoldFire = false;
                _bdHound!.DebugHoldHp = false;
                _bdKnockBase = enc.KnockOffs;
                Next(13);
                break;
            }
            case 13:
            {
                var blade = _bdMate!;
                var h = _bdHound!;
                var seat = W(2).SeatTransform(blade.Seat).Origin;
                var spot = W(2).BoarderSpotTransform().Origin;
                if (IsInstanceValid(h) && h.IsAlive && h.Carrier == W(2))
                {
                    _bdOwnMin = Mathf.Min(_bdOwnMin, seat.DistanceTo(spot));
                    _bdBodyMin = Mathf.Min(_bdBodyMin, blade.GlobalPosition.DistanceTo(h.GlobalPosition));
                    _bdBodyMax = Mathf.Max(_bdBodyMax, blade.GlobalPosition.DistanceTo(h.GlobalPosition));
                }
                var hits = _bdHurt.TryGetValue(h, out var l) ? l.Where(x => x.By >= 0).ToList() : new();
                var gone = !IsInstanceValid(h) || !h.IsAlive || h.Carrier != W(2);
                if (!gone && t < 8f) break;
                var by = BdMember(party, blade);
                var fate = !IsInstanceValid(h) || !h.IsAlive ? "killed" : enc.KnockOffs > _bdKnockBase ? "knocked off" : h.Carrier == W(2) ? "still aboard" : "gone";
                GD.Print($"[boarders] bench reach: {blade.State.Name} in W2 seat {blade.Seat}: the seat to W2's rim spot {_bdOwnMin:0.00} m (seat 4's {W(2).SeatTransform(TrainRules.CrewSeats[0]).Origin.DistanceTo(spot):0.00} m), the bodies {_bdBodyMin:0.00}–{_bdBodyMax:0.00} m (WagonReach {BoarderRules.WagonReach:0.0} m); its hits on W2's hound {hits.Count(x => x.By == by)} [{string.Join(", ", hits.Select(x => $"{x.Kind} by {party.Members[x.By].Name} grip {x.Grip}"))}]; the hound {fate} {t:0.00} s on");
                if (hits.Count == 0 || hits.Any(x => x.By != by) || fate is "still aboard" or "gone") { Fail($"from W2's bench seat {blade.Seat} {blade.State.Name} did not end its own wagon's hound by its blows (seat to spot {_bdOwnMin:0.00} m, WagonReach {BoarderRules.WagonReach} m)"); return; }
                blade.DebugHoldFire = true;
                Next(12);
                break;
            }
            case 12:
            {
                // with the blade up in W2, no second throw comes (it would have at 10 s)
                if (BdSince(Engine.GetPhysicsFrames()) < 11f) { if (enc.ThrowClockOf(2) is { Alone: true }) { Fail("W2 is still alone with the blade in it"); return; } break; }
                var throws = enc.Throws - _bdThrowsBase;
                GD.Print($"[boarders] the downed place: {throws} throw(s) off W2 in {BdSince(Engine.GetPhysicsFrames()):0.0} s; {W(2).Riders.Values.OfType<Npc>().Count(n => n.Crew && !n.Dead)} crew still in it");
                if (throws != 1) { Fail($"{throws} throws off W2, not the one before the blade landed"); return; }
                enc.DebugHoldAnswers = true;
                foreach (var a in _bdShootersDown) a.DebugPump = false;
                PumperCap = null;
                _bdMate2!.State.Mangled = false;
                _bdMate2.Revive();
                enc.DebugKill(_bdHound!);
                Next(20);
                break;
            }

            // part 3: a save and a load keep the thrown gone
            case 20:
            {
                if (t < 0.5f) break;
                GD.Print($"[boarders] throw: saved at S={rc.State.S:0.0} with [{string.Join(", ", _bdThrownCrew.Select(c => c.Name))}] thrown");
                main.SaveGame(BdSlot);
                if (!main.LoadGame(BdSlot)) { Fail("the save would not load"); return; }
                Next(21);
                break;
            }
            case 21:
            {
                if (t < 1.5f) break;
                var lost = ctrl.Capture().LostCrew ?? new List<string>();
                foreach (var c in _bdThrownCrew)
                {
                    var npc = ctrl.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == c.Id);
                    GD.Print($"[boarders] after the load: {c.Name} dead {npc?.Dead}, fled {npc?.Fled}, shown {npc?.Visible}, seated {npc?.Seated}, lost {lost.Contains(c.Id)}");
                    if (npc == null || !npc.Dead || !npc.Fled || npc.Visible || npc.Seated || !lost.Contains(c.Id)) { Fail($"after the load the thrown {c.Name} is back (or a body)"); return; }
                }
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): W3's hound called out at its landing, glowed at 4.5 and 8.5 s, threw seat 5's then seat 4's crew off at 6.0 and 10.0 s (dead, fled, hidden 1.2 s later, lost, logged, sent); the blade answering W2 past a downed hero in its place waited in W1 and landed on the seat the throw freed (one throw); a save and a load kept [{string.Join(", ", _bdThrownCrew.Select(c => c.Name))}] gone");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }
}
