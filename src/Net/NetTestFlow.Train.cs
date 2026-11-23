using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using Godot;
using Rails = DungeonCrawler.Dungeon.Rails;

namespace DungeonCrawler.Net;

// ---- the RIDE pass for the long train (plan 14 Task 7, spec A.6) -------------------------------------------------------------------
// The train at the landing on every peer; stop 1's crew in W1 on the server and each client within 1 s; on ride 1 the first client climbs
// back to wagon 1 by its own press (InteractIntent wagon:1), knocks a test hound off it by three blows of its own (holdHp: the grip ends it)
// and climbs forward into the cart (cart:0); at stop 1 a crewman downed in wagon 1 is revived end-on (more than 3.5 m from the wagon's
// origin, within its tub's reach); on ride 2 a test hound alone on wagon 1 throws seat 5's prisoner at 6 s, seen on both clients (the call,
// the glow first, the PropEvt, the copy hidden 1.2 s on, seated nowhere); a joiner's rebuild after it shows no corpse; on ride 4 both
// clients press for wagon 1's place at once and the second is refused (the place is booked); every Ruling 19 free of a hound cart is gone
// on the first client within 0.5 s of the server's print. The pass checks the wire, not the fight: a natural hound alone with crew is freed
// by the test's hand 1 s after it boards (no unscripted throw), and the crew in the train are kept above half their hp (as TopUp keeps the heroes).
public partial class NetTestFlow
{
    /// <summary>The seat yaw a RIDE client turns to for a move back (degrees off the seat's forward: past TrainMoveRules.BackCone).</summary>
    private const float LookBackDegrees = 150f;
    /// <summary>A natural hound on a crewed wagon is freed by the test's hand this long after it boards (before its glow at 4.5 s).</summary>
    private const double GuardAfter = 1.0;
    /// <summary>Ride 4's double press goes this far past the stop ride 4 leaves (both clients' copies, within a frame or two of each other).</summary>
    private const float DoublePressPast = 6f;

    private static string Deg(float radians) => Mathf.RadToDeg(radians).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    // ---- shared: the train at the landing, stop 1's crew ----------------------------------------------------------------------------

    private bool _trainLandingTold;
    /// <summary>Cage 1's crew (stop 1's), by id, as this side first saw them standing in it.</summary>
    private List<string>? _stop1Crew;
    private bool _stop1SeatedTold;
    private string _stop1Seats = "";

    /// <summary>The train at the landing on this side: W1..Wn coupled and shown (n the cages placed), or what it is instead.</summary>
    private static string TrainHereLine(string tag, Rails.RailController rc)
    {
        var numbers = rc.Train.Where(w => IsInstanceValid(w)).Select(w => w.Number).ToList();
        var want = ObjectiveController.Instance is CrewRescueController crew ? crew.Cages.Count : 0;
        var ok = want > 0 && numbers.SequenceEqual(Enumerable.Range(1, want)) && rc.Train.All(w => IsInstanceValid(w) && w.Coupled && w.Visible);
        var train = string.Join(", ", numbers);
        return ok ? $"{tag} ride: the train here at stop 0: W1..W{want} coupled (Train [{train}])"
            : $"{tag} ride: the train here at stop 0 is NOT W1..W{want} coupled and shown (Train [{train}], coupled [{string.Join(", ", rc.Train.Where(w => IsInstanceValid(w)).Select(w => $"W{w.Number} {w.Coupled}/{w.Visible}"))}]) (FAIL)";
    }

    /// <summary>The crew seated in the coupled wagons here (their copies' seats), and the same from the wagons' seat books; and those standing in a cage.</summary>
    private static (int Copies, int Books, int Caged, string Where) CrewInTrain(Rails.RailController rc, CrewRescueController crew)
    {
        var where = new List<string>();
        var copies = 0;
        var caged = 0;
        foreach (var n in crew.Crew)
        {
            if (!IsInstanceValid(n) || n.Dead) continue;
            if (n.Cart is Rails.PlatformCage) caged++;
            if (n.Cart is not Rails.TrainWagon { Coupled: true } w || rc.WagonAt(w.Number) != w) continue;
            copies++;
            where.Add($"W{w.Number} seat {n.Seat}");
        }
        var books = 0;
        foreach (var w in rc.Train)
            if (IsInstanceValid(w))
                foreach (var r in w.Riders.Values)
                    if (r is Npc { Crew: true, Dead: false } npc && IsInstanceValid(npc) && npc.Cart == w) books++;
        where.Sort(string.CompareOrdinal);
        return (copies, books, caged, string.Join(", ", where));
    }

    /// <summary>
    /// Stop 1's crew seated in W1 on this side, once: their seats (sorted) when both of cage 1's crew sit in W1, else null. Cage 1's crew are
    /// taken the first time two of them stand in it.
    /// </summary>
    private string? Stop1CrewSeats(Rails.RailController rc, CrewRescueController crew)
    {
        if (_stop1Crew == null && crew.Cages.FirstOrDefault(c => IsInstanceValid(c) && c.Stop == 1) is { } cage1)
        {
            var inCage = crew.Crew.Where(n => IsInstanceValid(n) && !n.Dead && n.Cart == cage1).Select(n => n.Id).ToList();
            if (inCage.Count == 2) _stop1Crew = inCage;
        }
        if (_stop1Crew == null || rc.WagonAt(1) is not { } w1) return null;
        var seats = new List<int>();
        foreach (var id in _stop1Crew)
        {
            if (crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == id) is not { Dead: false } n || n.Cart != w1) return null;
            seats.Add(n.Seat);
        }
        seats.Sort();
        return string.Join(", ", seats);
    }

    // ---- the server ---------------------------------------------------------------------------------------------------------------

    /// <summary>The server's train checks, every tick of the riding: the landing, stop 1's crew, the knock-off, the throw, the guard, the frees.</summary>
    private void RideTrainServer(ServerWorld world, Rails.RailController rc, Party.Adventurer hero, Party.PartyController party)
    {
        if (!_trainLandingTold && rc.StopIndex == 0 && rc.State.Mode == Rails.CartMode.Held && !hero.Seated)
        {
            _trainLandingTold = true;
            GD.Print(TrainHereLine("[net-server]", rc));
        }
        if (ObjectiveController.Instance is CrewRescueController crew && !_stop1SeatedTold && Stop1CrewSeats(rc, crew) is { } seats)
        {
            _stop1SeatedTold = true;
            _stop1Seats = seats;
            GD.Print(FormattableString.Invariant($"[net-server] ride: stop 1's crew seated in W1 (seats {seats}) at t={NetSession.Instance.Now:0.000}"));
            var (_, books, caged, where) = CrewInTrain(rc, crew);
            GD.Print($"[net-server] ride: crew seated in wagons: {books} in {rc.Wagons.Count} wagon(s) [{where}]; {caged} caged in {crew.Cages.Count(c => !c.Open)} locked cage(s)");
        }
        if (Rails.RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc)) return;
        RideKnockOffServer(rc, hero, party, enc);
        RideThrowServer(rc, party, enc);
        RideCrewGuard(rc, enc);
    }

    /// <summary>The hero bots' (companions') fire held, or let go: the knock-off and the throw are the client's blows and the clock's alone.</summary>
    private static void HoldBotsFire(Party.PartyController party, bool on)
    {
        foreach (var a in party.Adventurers)
            if (IsInstanceValid(a) && a.Control == Party.ControlKind.Companion) a.DebugHoldFire = on;
    }

    // the knock-off (ride 1): once the first client sits in W1 by its own move and ride 1's own r1_hounds has leapt or missed (or its cart is
    // freed, or 25 s have gone), with W1's rim free, a test hound boards W1 holding its hp (the grip, not a kill, ends it); the client's blows
    // knock it off; a second later it is freed by the test's hand where it lies
    private int _koStep;
    private Enemies.Enemy? _koHound;
    private double _koSince, _koAt;
    private string _koR1 = "";
    private bool _moveBackTold;

    private void RideKnockOffServer(Rails.RailController rc, Party.Adventurer hero, Party.PartyController party, Rails.RailEncounterController enc)
    {
        if (_koStep >= 4)
        {
            if (!_moveBackTold && rc.StopIndex == 0 && rc.CarOf(hero) == 0)
            {
                _moveBackTold = true;
                GD.Print($"[net-server] ride: move: {hero.State.Name} back in the party's cart seat {hero.Seat} here (ride 1, from wagon 1)");
            }
            return;
        }
        var w1 = rc.WagonAt(1);
        if (w1 == null || rc.StopIndex != 0)
        {
            if (_koStep > 0 && rc.StopIndex != 0)
            {
                GD.Print($"[net-server] ride: knock-off: ride 1 ended at its step {_koStep} (FAIL)");
                if (_koHound != null && IsInstanceValid(_koHound) && _koHound.IsAlive) enc.DebugKill(_koHound);
                Release();
                _koStep = 4;
            }
            return;
        }
        switch (_koStep)
        {
            case 0:
                if (rc.State.Mode != Rails.CartMode.Rolling || rc.CarOf(hero) != 1 || rc.Hopping(hero)) return;
                _koStep = 1;
                _koSince = _elapsed;
                GD.Print(FormattableString.Invariant($"[net-server] ride: move: {hero.State.Name} sits in W1 seat {hero.Seat} here (ride 1, S={rc.State.S:0.0})"));
                return;
            case 1:
            {
                if (rc.CarOf(hero) != 1) { GD.Print($"[net-server] ride: knock-off: {hero.State.Name} left wagon 1 before the test hound boarded (in car {rc.CarOf(hero)}) (FAIL)"); _koStep = 4; return; }
                var r1 = enc.SlagLog.LastOrDefault(e => e.Encounter == "r1_hounds" && (e.What.StartsWith("leapt", StringComparison.Ordinal) || e.What.StartsWith("freed: ", StringComparison.Ordinal)));
                if (r1.Encounter == null && _elapsed - _koSince < 25) return;
                _koR1 = r1.Encounter != null ? FormattableString.Invariant($"r1_hounds {r1.What} at party S={r1.PartyS:0.0}") : "r1_hounds never leapt in 25 s";
                if (w1.Boarders[0] is { } held && IsInstanceValid(held) && held.IsAlive && held.Carrier == w1) return; // ride 1's own hound on W1: our client's blows first
                enc.DebugHoldAnswers = true;
                HoldBotsFire(party, true);
                _koHound = enc.DebugBoard(1, holdHp: true);
                if (_koHound == null) { GD.Print("[net-server] ride: knock-off: the test hound could not board wagon 1 (FAIL)"); Release(); _koStep = 4; return; }
                _koStep = 2;
                _koAt = _elapsed;
                GD.Print(FormattableString.Invariant($"[net-server] ride: knock-off: a test hound {_koHound.Name} (net id {_koHound.NetId}) boards wagon 1 with {hero.State.Name} seated in it at t={NetSession.Instance.Now:0.000} (S={rc.State.S:0.0}; {_koR1}); answers and the hero bots' fire held"));
                return;
            }
            case 2:
                if (_koHound == null || !IsInstanceValid(_koHound) || !_koHound.IsAlive)
                {
                    GD.Print("[net-server] ride: knock-off: the test hound died on wagon 1 (its hp is held: only the grip may end it) (FAIL)");
                    Release();
                    _koStep = 4;
                    return;
                }
                if (_koHound.Carrier == w1)
                {
                    if (_elapsed - _koAt <= 30) return;
                    GD.Print(FormattableString.Invariant($"[net-server] ride: knock-off: the test hound still rides wagon 1 30 s after it boarded (grip {enc.GripOf(_koHound)}) (FAIL)"));
                    enc.DebugKill(_koHound);
                    Release();
                    _koStep = 4;
                    return;
                }
                GD.Print(FormattableString.Invariant($"[net-server] ride: knock-off: test hound {_koHound.Name} (net id {_koHound.NetId}) knocked off wagon 1 at t={NetSession.Instance.Now:0.000}, {_elapsed - _koAt:0.0} s after it boarded, alive ({_koHound.Hp:0}/{_koHound.MaxHp:0} hp), {hero.State.Name} in car {rc.CarOf(hero)}"));
                Release();
                _koStep = 3;
                _koAt = _elapsed;
                return;
            case 3:
                if (_elapsed - _koAt < 1.0) return;
                if (_koHound != null && IsInstanceValid(_koHound) && _koHound.IsAlive) enc.DebugKill(_koHound); // it lies beside the lane, its hp held: gone by the test's hand
                _koStep = 4;
                return;
        }

        void Release()
        {
            enc.DebugHoldAnswers = false;
            HoldBotsFire(party, false);
        }
    }

    // the throw (ride 2): 2 s after the joiner is ready mid-ride 2 (after its joined-mid-ride check, 1 s after its Ready), with stop 1's crew in
    // W1 and no hero there, answers, crouches and the hero bots' fire are held and a test hound boards W1; alone, it is called, glows at 4.5 s
    // and throws seat 5's prisoner at 6.0 s; 1.5 s later (before the second throw at 10 s) the test frees it and lets everything go
    private int _twStep;
    private Enemies.Enemy? _twHound;
    private double _twAt, _twThrowElapsed, _twCallNow = double.NaN, _twGlowNow = double.NaN, _twThrowNow = double.NaN;
    private ulong _twCallFrame, _twThrowFrame;
    private int _twLog;
    private string _twWho = "", _twThrown = "", _twCrew = "";
    private float _twCounted;
    private readonly Dictionary<string, int> _twSeats = new();
    private double _joinReadyElapsed = -1;

    private void RideThrowServer(Rails.RailController rc, Party.PartyController party, Rails.RailEncounterController enc)
    {
        if (_twStep >= 9) return;
        var w1 = rc.WagonAt(1);
        switch (_twStep)
        {
            case 0:
            {
                if (_joinReadyElapsed < 0 || _elapsed - _joinReadyElapsed < 2.0 || w1 == null) return;
                if (rc.StopIndex >= 2) { GD.Print($"[net-server] ride: throw: never staged on ride 2 (the train at stop {rc.StopIndex}) (FAIL)"); ReleaseThrow(); _twStep = 9; return; }
                if (rc.StopIndex != 1 || rc.State.Mode != Rails.CartMode.Rolling) return;
                var crewIn = w1.Riders.Values.OfType<Npc>().Where(n => IsInstanceValid(n) && n.Crew && !n.Dead && n.Cart == w1).ToList();
                if (crewIn.Count == 0 || w1.Riders.Values.Any(r => r is Party.Adventurer)) return;
                enc.DebugHoldAnswers = true;
                enc.DebugHoldCrouches = true;
                HoldBotsFire(party, true);
                if (w1.Boarders[0] is { } natural && IsInstanceValid(natural) && natural.IsAlive && natural.Carrier == w1 && !enc.IsDebugBoarder(natural)) { enc.DebugKill(natural); return; } // its rim spot free first
                _twHound = enc.DebugBoard(1, holdHp: true);
                if (_twHound == null) { GD.Print("[net-server] ride: throw: the test hound could not board wagon 1 (FAIL)"); ReleaseThrow(); _twStep = 9; return; }
                _twLog = enc.ThrowLog.Count;
                _twAt = _elapsed;
                _twSeats.Clear();
                foreach (var n in crewIn) _twSeats[n.NpcName] = n.Seat;
                _twCrew = string.Join(", ", crewIn.OrderBy(n => n.Seat).Select(n => $"{n.NpcName} seat {n.Seat}"));
                var where = string.Join(", ", party.Adventurers.Where(a => IsInstanceValid(a) && !a.Parked).Select(a => $"{a.State.Name} {(rc.CarOf(a) is var car and >= 0 ? $"in car {car}" : "on foot")}"));
                GD.Print(FormattableString.Invariant($"[net-server] ride: throw: a test hound {_twHound.Name} (net id {_twHound.NetId}) boards wagon 1 at t={NetSession.Instance.Now:0.000} (S={rc.State.S:0.0}) with {crewIn.Count} crew in it ({_twCrew}); {where}; answers, crouches and the hero bots' fire held"));
                _twStep = 1;
                return;
            }
            case 1:
                for (; _twLog < enc.ThrowLog.Count; _twLog++)
                {
                    var e = enc.ThrowLog[_twLog];
                    if (e.Wagon != 1) continue;
                    if (e.Step == Rails.ThrowStep.Call && double.IsNaN(_twCallNow)) { _twCallNow = NetSession.Instance.Now; _twCallFrame = e.Frame; _twWho = e.Who; }
                    else if (e.Step == Rails.ThrowStep.Glow && double.IsNaN(_twGlowNow)) _twGlowNow = NetSession.Instance.Now;
                    else if (e.Step == Rails.ThrowStep.Throw)
                    {
                        _twThrowNow = NetSession.Instance.Now;
                        _twThrowFrame = e.Frame;
                        _twThrown = e.Who;
                        _twCounted = e.Counted;
                        _twThrowElapsed = _elapsed;
                        _twStep = 2;
                        GD.Print(FormattableString.Invariant($"[net-server] ride: throw: {_twThrown} thrown off wagon 1 at t={_twThrowNow:0.000}"));
                        break;
                    }
                }
                if (_twStep == 1 && _elapsed - _twAt > 12)
                {
                    GD.Print(FormattableString.Invariant($"[net-server] ride: throw: no throw 12 s after the test hound boarded (called {Yes(!double.IsNaN(_twCallNow))}, glowed {Yes(!double.IsNaN(_twGlowNow))}) (FAIL)"));
                    if (_twHound != null && IsInstanceValid(_twHound)) enc.DebugKill(_twHound);
                    ReleaseThrow();
                    _twStep = 9;
                }
                return;
            case 2:
            {
                if (_elapsed - _twThrowElapsed < 1.5) return;
                var second = enc.ThrowLog.Skip(_twLog + 1).Count(e => e.Wagon == 1 && e.Step == Rails.ThrowStep.Throw);
                if (_twHound != null && IsInstanceValid(_twHound)) enc.DebugKill(_twHound);
                var freedAfter = _elapsed - _twAt;
                ReleaseThrow();
                _twStep = 9;
                var tick = 1.0 / Engine.PhysicsTicksPerSecond;
                var sinceCall = (_twThrowFrame - _twCallFrame) * tick;
                var seat = _twSeats.TryGetValue(_twThrown, out var s) ? s : -1;
                var ofStop1 = _stop1Crew != null && ObjectiveController.Instance is CrewRescueController crew && crew.Crew.Any(n => IsInstanceValid(n) && n.NpcName == _twThrown && _stop1Crew.Contains(n.Id));
                var ok = !double.IsNaN(_twCallNow) && !double.IsNaN(_twGlowNow) && _twGlowNow <= _twThrowNow && Math.Abs(sinceCall - Rails.BoarderRules.ThrowFirst) <= 0.3
                    && Math.Abs(_twCounted - Rails.BoarderRules.ThrowFirst) <= 0.3 && seat == Rails.TrainRules.CrewSeats[1] && ofStop1 && second == 0 && freedAfter < Rails.BoarderRules.ThrowFirst + Rails.BoarderRules.ThrowEvery;
                GD.Print(FormattableString.Invariant($"[net-server] ride: throw: the test hound alone on wagon 1 ({_twCrew}) was called by {_twWho} at t={_twCallNow:0.000}, glowed at t={_twGlowNow:0.000} and threw {_twThrown} (seat {seat}, one of stop 1's crew {Yes(ofStop1)}) at t={_twThrowNow:0.000}, {sinceCall:0.00} s after the call (its clock {_twCounted:0.0} s alone); freed by the test's hand {freedAfter:0.0} s after it boarded, {second} more throw(s) ({(ok ? "ok" : "FAIL")})"));
                return;
            }
        }

        void ReleaseThrow()
        {
            enc.DebugHoldAnswers = false;
            enc.DebugHoldCrouches = false;
            HoldBotsFire(party, false);
        }
    }

    // the guard: a natural hound on a crewed wagon is freed by the test's hand 1 s after it boards (its call may come; its glow at 4.5 s and its
    // throw at 6 s never do), so only the scripted throw takes a prisoner; the crew seated in the rolling train are kept above half their hp
    private readonly Dictionary<Enemies.Enemy, double> _guardSeen = new();
    private int _guardKills, _crewTopUps;
    private readonly List<Npc> _guardCrew = new();
    private readonly Dictionary<Npc, double> _topUpTriedAt = new();

    private void RideCrewGuard(Rails.RailController rc, Rails.RailEncounterController enc)
    {
        if (rc.State.Mode != Rails.CartMode.Rolling) { _guardSeen.Clear(); return; }
        foreach (var wagon in rc.Train)
        {
            if (!IsInstanceValid(wagon) || !wagon.Coupled) continue;
            _guardCrew.Clear();
            foreach (var r in wagon.Riders.Values)
                if (r is Npc { Crew: true, Dead: false } npc && IsInstanceValid(npc) && npc.Cart == wagon) _guardCrew.Add(npc);
            foreach (var npc in _guardCrew)
            {
                if (npc.Downed)
                {
                    if (_topUpTriedAt.TryGetValue(npc, out var tried) && _elapsed - tried < 1.0) continue; // a refused revive (a hostile too close) logs: once a second
                    _topUpTriedAt[npc] = _elapsed;
                    if (!npc.TryRevive()) continue;
                    npc.RestoreHp(1f);
                }
                else if (npc.Hp < npc.MaxHp * 0.5f) npc.RestoreHp(1f);
                else continue;
                _crewTopUps++;
                GD.Print(FormattableString.Invariant($"[net-server] ride: crew top-up: {npc.NpcName} in wagon {wagon.Number} seat {npc.Seat} (stop {rc.StopIndex}, S={rc.State.S:0.0})"));
            }
            var b = wagon.Boarders[0];
            if (_guardCrew.Count == 0 || b == null || !IsInstanceValid(b) || !b.IsAlive || b.Carrier != wagon || enc.IsDebugBoarder(b)) continue;
            if (!_guardSeen.TryGetValue(b, out var since)) { _guardSeen[b] = _elapsed; continue; }
            if (_elapsed - since < GuardAfter) continue;
            _guardSeen.Remove(b);
            _guardKills++;
            GD.Print(FormattableString.Invariant($"[net-server] ride: guard: {b.Name} on wagon {wagon.Number} with {_guardCrew.Count} crew freed by the test's hand {GuardAfter:0.0} s after it boarded (stop {rc.StopIndex}, S={rc.State.S:0.0}; {_guardKills} so far)"));
            enc.DebugKill(b);
        }
    }

    // the frees (Ruling 19): every "freed:" in the encounter controller's slag log, printed at the server's clock (the first client pairs each
    // with its copy's going)
    private Rails.RailEncounterController? _freeEnc;
    private int _freeLog;

    private void RideFreesServer()
    {
        if (Rails.RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc)) return;
        if (enc != _freeEnc) { _freeEnc = enc; _freeLog = 0; }
        for (; _freeLog < enc.SlagLog.Count; _freeLog++)
        {
            var e = enc.SlagLog[_freeLog];
            if (!e.What.StartsWith("freed: ", StringComparison.Ordinal)) continue;
            GD.Print(FormattableString.Invariant($"[net-server] ride: slag cart {e.Who} ({e.Encounter}) {e.What} at t={NetSession.Instance.Now:0.000}"));
        }
    }

    // the end-on revive (stop 1, Task 3 carry): once stop 1's crew sit in W1, seat 4's goes down by the test's hand and the hero is put end-on
    // by W1, more than 3.5 m from its origin and within 3 m of its tub; the client revives him through wagon:1 from there
    private Npc? _eoNpc;
    private bool _eoDone;
    private double _eoAt;

    /// <summary>The server's end-on step at stop 1, between the cage's revive and the second boarding. True while it is not done (RideProps waits).</summary>
    private bool RideEndOnServer(Main main, Rails.RailController rc, CrewRescueController crew, Party.Adventurer hero)
    {
        if (_eoDone) return false;
        if (!_stop1SeatedTold || rc.WagonAt(1) is not { } w1) return true;
        if (_eoNpc == null)
        {
            _eoNpc = crew.Crew.Where(n => IsInstanceValid(n) && !n.Dead && !n.Downed && n.Cart == w1).OrderBy(n => n.Seat).FirstOrDefault();
            if (_eoNpc == null) { GD.Print("[net-server] ride: end-on: nobody up in wagon 1 to go down (FAIL)"); _eoDone = true; return false; }
            _eoNpc.GoDown();
            var eye = hero.Camera.GlobalPosition - hero.GlobalPosition;
            var spot = EndOnSpot(main, rc, w1, eye, out var origin, out var tub);
            hero.Shove(spot);
            _eoAt = _elapsed;
            var ok = origin > Rails.PropPromptRules.InteractReach && tub <= 3f;
            GD.Print(FormattableString.Invariant($"[net-server] ride: end-on: {_eoNpc.NpcName} goes down in wagon 1 seat {_eoNpc.Seat} by the test's hand; {hero.State.Name} is put end-on by wagon 1 at ({spot.X:0.00}, {spot.Y:0.00}, {spot.Z:0.00}), {origin:0.00} m from its origin and {tub:0.00} m from its tub ({(ok ? "ok" : "FAIL")}: over the {Rails.PropPromptRules.InteractReach:0.0} m origin reach, within the 3.0 m look ray's)"));
            return true;
        }
        if (_eoNpc.Downed && IsInstanceValid(_eoNpc))
        {
            if (_elapsed - _eoAt < 40) return true;
            GD.Print($"[net-server] ride: end-on: {_eoNpc.NpcName} still down in wagon 1 40 s on; revived by the test's hand (FAIL)");
            _eoNpc.TryRevive();
        }
        else GD.Print($"[net-server] ride: end-on: {_eoNpc.NpcName} is up again in wagon 1 seat {_eoNpc.Seat} (revived through wagon:1)");
        _eoDone = true;
        return false;
    }

    /// <summary>
    /// A walkable spot off the rails by <paramref name="w1"/>'s back end, toward the side its riders step out: its distances (the body's or the
    /// eye's, the nearer) to the wagon's origin over the origin reach and to its tub within the look ray's 3 m, the first that has both (else the
    /// farthest from the origin within the tub's reach).
    /// </summary>
    private static Vector3 EndOnSpot(Main main, Rails.RailController rc, Rails.TrainWagon w1, Vector3 eye, out float origin, out float tub)
    {
        var basis = w1.GlobalBasis.Orthonormalized();
        var back = basis.Z;
        var side = basis.X;
        var beside = rc.StandBesideCart();
        var sign = (beside - rc.Cart.GlobalPosition).Dot(side) >= 0f ? 1f : -1f;
        var layout = main.Builder.Layout;
        Vector3? best = null;
        float bestOrigin = -1f, bestTub = float.MaxValue;
        foreach (var s in new[] { sign, -sign })
            foreach (var along in new[] { 3.0f, 3.3f, 2.8f, 3.6f })
                foreach (var off in new[] { 2.2f, 2.0f, 2.4f, 1.8f, 2.6f })
                {
                    var at = OnFloor(main, w1.GlobalPosition + back * along + side * (s * off));
                    var cell = Dungeon.DungeonBuilder.WorldToCell(at);
                    if (layout == null || !layout.IsWalkable(cell) || layout.IsRail(cell)) continue;
                    var o = Mathf.Min(at.DistanceTo(w1.GlobalPosition), (at + eye).DistanceTo(w1.GlobalPosition));
                    var t = Mathf.Min(w1.TubDistance(at), w1.TubDistance(at + eye));
                    if (o > Rails.PropPromptRules.InteractReach + 0.1f && t <= 2.9f) { origin = o; tub = t; return at; }
                    if (t <= 3f && o > bestOrigin) { best = at; bestOrigin = o; bestTub = t; }
                }
        if (best is { } b) { origin = bestOrigin; tub = bestTub; return b; }
        var fallback = OnFloor(main, w1.GlobalPosition + back * 3.0f + side * (sign * 2.2f));
        origin = Mathf.Min(fallback.DistanceTo(w1.GlobalPosition), (fallback + eye).DistanceTo(w1.GlobalPosition));
        tub = Mathf.Min(w1.TubDistance(fallback), w1.TubDistance(fallback + eye));
        return fallback;
    }

    /// <summary>The server's guard and frees told once the pass is over (a note line).</summary>
    private void RideTrainServerSummary() => GD.Print($"[net-server] ride: guard: {_guardKills} natural hound(s) freed off crewed wagons by the test's hand; the crew topped up {_crewTopUps} time(s)");

    // ---- a client -----------------------------------------------------------------------------------------------------------------

    /// <summary>A RIDE client's train rear: the party cart's front less the train's length (TrainRules.Length).</summary>
    private static float TrainRearS(Rails.RailController rc) => rc.Party.FrontS - Rails.TrainRules.Length(rc.Train.Count);

    /// <summary>Hounds this client has seen riding a slag cart (net ids): a hound first seen on a wagon's rim otherwise is a test's.</summary>
    private readonly HashSet<int> _slagHounds = new();
    /// <summary>The slag carts' copies seen here (by cart id) and their encounter, until each goes.</summary>
    private readonly Dictionary<int, (string Enc, Rails.SlagCart Cart)> _slagSeen = new();

    /// <summary>
    /// Each RIDE client, every tick: the train at the landing, stop 1's crew in W1, the hounds on slag carts, ride 1's W1 boarders while we
    /// sit in it, every slag copy's going, and the throw's showing. Returns a failure, or null.
    /// </summary>
    private string? RideTrainCheck(ClientWorld client, Main main, Rails.RailController rc)
    {
        if (main.Rebuilding || main.Party?.Local is not { } me) return null;
        string? fail = null;
        if (!_wireJoiner)
        {
            if (!_trainLandingTold && rc.Following && rc.StopIndex == 0 && rc.State.Mode == Rails.CartMode.Held)
            {
                if (me.Seated) fail = "our hero sat in the cart at the landing before the train was seen here";
                else
                {
                    _trainLandingTold = true;
                    GD.Print(TrainHereLine("[net]", rc));
                }
            }
            if (!_stop1SeatedTold && ObjectiveController.Instance is CrewRescueController crew)
            {
                if (Stop1CrewSeats(rc, crew) is { } seats)
                {
                    _stop1SeatedTold = true;
                    _stop1Seats = seats;
                    GD.Print(FormattableString.Invariant($"[net] ride: stop 1's crew seated in W1 here at t={client.ServerNow:0.000} (seats {seats})"));
                    var (copies, books, caged, where) = CrewInTrain(rc, crew);
                    if (copies == books && copies > 0)
                    {
                        _rideCrewOk = true;
                        _rideCrewSeated = copies;
                        GD.Print($"[net] ride: crew seated in their wagons here: {copies} in {rc.Wagons.Count} wagon(s) [{where}]; {caged} caged in {crew.Cages.Count(c => !c.Open)} locked cage(s)");
                    }
                    else fail = $"stop 1's crew sit in W1 here, but the crew's copies seat {copies} in the train and the wagons' books {books} [{where}]";
                }
                else if (rc.StopIndex >= 2) fail = $"the train left stop 1 without stop 1's crew seated in W1 here (cage 1's crew {(_stop1Crew == null ? "never seen" : string.Join(", ", _stop1Crew))})";
            }
        }
        foreach (var s in Rails.SlagCart.Live)
        {
            if (!IsInstanceValid(s) || s.IsQueuedForDeletion()) continue;
            _slagSeen.TryAdd(s.CartId, (s.EncounterId, s));
            for (var i = 0; i < Rails.SlagCart.MaxRiders; i++)
                if (s.RiderAt(i) is { } r && IsInstanceValid(r) && r.Profile.Id == Rails.RailEncounterController.HoundId) _slagHounds.Add(r.NetId);
        }
        RideSlagGoneWatch(client);
        RideKnockWatch(client, rc, me);
        RideThrowWatch(client, rc);
        return fail;
    }

    private readonly List<int> _slagGone = new();

    /// <summary>A slag copy that went here (freed, derailed away, or gone with the ride), told once at the server's clock as this client has it.</summary>
    private void RideSlagGoneWatch(ClientWorld client)
    {
        _slagGone.Clear();
        foreach (var (id, seen) in _slagSeen)
            if (!IsInstanceValid(seen.Cart) || seen.Cart.IsQueuedForDeletion() || !seen.Cart.IsInsideTree()) _slagGone.Add(id);
        foreach (var id in _slagGone)
        {
            var enc = _slagSeen[id].Enc;
            _slagSeen.Remove(id);
            GD.Print(FormattableString.Invariant($"[net] ride: slag cart {id} ({enc}) gone here at t={client.ServerNow:0.000}"));
        }
    }

    // ride 1's wagon-1 boarders while we sit in it: which leave, when (the server's clock), and whether it was the test's hound (never on a slag cart here)
    private int _w1Boarder = -1;
    private bool _w1BoarderTest;
    private double _koGoneAt = -1;

    private void RideKnockWatch(ClientWorld client, Rails.RailController rc, Party.Adventurer me)
    {
        if (_wireJoiner || rc.StopIndex != 0 || rc.WagonAt(1) is not { } w1) return;
        var b = w1.Boarders[0];
        var on = b != null && IsInstanceValid(b) && b.Carrier == w1 && b.IsAlive ? b.NetId : -1;
        if (on == _w1Boarder) return;
        if (_w1Boarder >= 0)
        {
            GD.Print(FormattableString.Invariant($"[net] ride: knock-off: hound {_w1Boarder} ({(_w1BoarderTest ? "the test's" : "ride 1's own")}) off wagon 1 here at t={client.ServerNow:0.000} (we sit in car {rc.CarOf(me)})"));
            if (_w1BoarderTest && _koGoneAt < 0) _koGoneAt = client.ServerNow;
        }
        _w1Boarder = on;
        _w1BoarderTest = on >= 0 && !_slagHounds.Contains(on);
        if (on >= 0) GD.Print(FormattableString.Invariant($"[net] ride: knock-off: hound {on} ({(_w1BoarderTest ? "the test's: never on a slag cart here" : "ride 1's own")}) rides wagon 1 here at t={client.ServerNow:0.000} (we sit in car {rc.CarOf(me)})"));
    }

    // the throw seen here (both clients): the call (a BarkMsg or the log's line), the BoarderThrow glow (EnemyStateMsg), the PropEvt
    // wagon:{k} "thrown:{id}" (the server's clock as this client has it), then the copy hidden and seated nowhere
    private readonly List<(double At, string How)> _throwCalls = new();
    private double _glowAt = double.NaN;
    private int _glowHound = -1;
    private readonly List<(string Id, string Ref, double At, double GlowAt, int GlowHound)> _thrownHere = new();
    private readonly HashSet<string> _thrownTold = new();
    /// <summary>The ids of the crew thrown that this client heard of (its PropEvt), for a later rebuild's check.</summary>
    private readonly HashSet<string> _thrownIds = new();

    /// <summary>From the wire (OnRideWire, before each message is applied): the throw's call, glow and PropEvt.</summary>
    private void NoteThrowWire(NetMessage m)
    {
        if (_wireClient is not { } client) return;
        switch (m)
        {
            case EnemyStateMsg es when es.State == Enemies.TelegraphRules.BoarderThrow:
                _glowAt = client.ServerNow;
                _glowHound = es.NetId;
                break;
            case PropEvt pe when pe.What.StartsWith("thrown:", StringComparison.Ordinal) && pe.Ref.StartsWith(NetRefs.WagonKind + ":", StringComparison.Ordinal):
            {
                var id = pe.What["thrown:".Length..];
                _thrownIds.Add(id);
                _thrownHere.Add((id, pe.Ref, client.ServerNow, _glowAt, _glowHound));
                break;
            }
            case BarkMsg bark when IsThrowCall(bark.Text):
                _throwCalls.Add((client.ServerNow, "heard (BarkMsg)"));
                break;
        }
    }

    /// <summary>A line in the log here: the throw's call in the log's words when nobody seated was there to say it.</summary>
    private void NoteThrowLog(string text)
    {
        if (_wireClient is { } client && IsThrowCall(text)) _throwCalls.Add((client.ServerNow, "logged"));
    }

    private static bool IsThrowCall(string text)
    {
        for (var k = 1; k <= 4; k++) if (text == $"It's at the prisoners — wagon {k}!") return true;
        return IsRideCall(BarkTrigger.BoarderThrow, "It's at the prisoners — wagon 1!", text);
    }

    private void RideThrowWatch(ClientWorld client, Rails.RailController rc)
    {
        if (_thrownHere.Count == 0 || ObjectiveController.Instance is not CrewRescueController crew) return;
        foreach (var (id, wagonRef, at, glowAt, glowHound) in _thrownHere)
        {
            if (_thrownTold.Contains(id)) continue;
            var npc = crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == id);
            var hidden = npc == null || !npc.Visible;
            if (!hidden && client.ServerNow - at < 3.0) continue;
            _thrownTold.Add(id);
            var name = npc?.NpcName ?? id;
            var seatedIn = npc == null ? null : npc.Cart is { } cart ? (cart as Node)?.Name.ToString() ?? "a cart" : rc.Train.FirstOrDefault(w => IsInstanceValid(w) && w.Riders.ContainsKey(npc.RiderId)) is { } book ? $"W{book.Number}'s book" : null;
            var call = _throwCalls.LastOrDefault(c => c.At <= at + 0.05 && c.At >= at - 8.0);
            var k = wagonRef[(NetRefs.WagonKind.Length + 1)..];
            var after = client.ServerNow - at;
            var why = new List<string>();
            if (!hidden) why.Add("still shown 3 s after the PropEvt");
            if (seatedIn != null) why.Add($"seated in {seatedIn}");
            if (call.How == null) why.Add("no call heard before it");
            if (double.IsNaN(glowAt) || glowAt > at || at - glowAt > 3.0) why.Add("no BoarderThrow glow seen before it");
            GD.Print(FormattableString.Invariant($"[net] ride: {name} thrown from wagon {k} here (PropEvt), hidden {after:0.0} s later, {(seatedIn == null ? "seated nowhere" : $"seated in {seatedIn}")} (the call {(call.How != null ? $"{call.How} at t={call.At:0.000}" : "NOT heard")}; the glow on hound {glowHound} at t={glowAt:0.000}, the throw's PropEvt at t={at:0.000}, hidden at t={client.ServerNow:0.000}) ({(why.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", why))})"));
        }
    }

    /// <summary>After a rebuild (the joiner's resync): each thrown crew member this client heard of is absent or hidden here, never a corpse. Null when so, else why.</summary>
    private string? ThrownGoneHere(Main main)
    {
        if (_thrownIds.Count == 0 || ObjectiveController.Instance is not CrewRescueController crew) return null;
        var shown = new List<string>();
        foreach (var id in _thrownIds)
            if (crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == id) is { Visible: true } npc) shown.Add($"{npc.NpcName} shown ({(npc.Dead ? "a corpse" : "up")}{(npc.Cart != null ? ", seated" : "")})");
        GD.Print(shown.Count == 0
            ? $"[net] ride: after the rebuild the thrown {string.Join(", ", _thrownIds)} {(_thrownIds.Count == 1 ? "is" : "are")} gone here: no corpse (ok)"
            : $"[net] ride: after the rebuild {string.Join(", ", shown)} here (FAIL)");
        return shown.Count == 0 ? null : $"the thrown crew shown after the rebuild: {string.Join(", ", shown)}";
    }

    /// <summary>
    /// The joiner at its join and after its resync: the train W1..W4 coupled here, stop 1's crew as many as the throws it heard of leave seated in
    /// W1's bench seats (their copies and the wagons' books agree), cage 1 open and empty and every locked cage holding its two. The reasons it is not, or none.
    /// </summary>
    private List<string> JoinerTrainWhy(Rails.RailController rc, out string line)
    {
        var why = new List<string>();
        line = "no crew controller";
        if (ObjectiveController.Instance is not CrewRescueController crew) { why.Add("no crew controller here"); return why; }
        var numbers = rc.Train.Where(w => IsInstanceValid(w) && w.Coupled).Select(w => w.Number).ToList();
        if (!numbers.SequenceEqual(Enumerable.Range(1, crew.Cages.Count))) why.Add($"the train here is [{string.Join(", ", numbers)}] coupled (want W1..W{crew.Cages.Count})");
        var (copies, books, caged, where) = CrewInTrain(rc, crew);
        var want = 2 - _thrownIds.Count;
        var locked = crew.Cages.Count(c => IsInstanceValid(c) && !c.Open);
        var cage1Open = crew.Cages.FirstOrDefault(c => IsInstanceValid(c) && c.Stop == 1) is { Open: true };
        if (copies != want || books != want) why.Add($"{copies} crew seated in the train here, {books} in the wagons' books (want {want}: stop 1's two less {_thrownIds.Count} thrown)");
        if (copies > 0 && !where.Split(", ").All(p => p is "W1 seat 4" or "W1 seat 5")) why.Add($"the crew sit at [{where}] here (want W1's bench)");
        if (!cage1Open) why.Add("cage 1 is not open here");
        if (caged != 2 * locked) why.Add($"{caged} crew stand in the {locked} locked cage(s) here (want two in each)");
        line = $"{copies} crew seated in the train [{where}] (books {books}), {caged} caged in {locked} locked cage(s), cage 1 {(cage1Open ? "open" : "SHUT")}";
        return why;
    }

    // ---- ride 1: the remote move to wagon 1 and back (the first client) ------------------------------------------------------------

    private int _mvStep;
    private double _mvAt;
    private float _mvCutS = float.NaN, _mvYawBefore;

    private void MvNext(int step) { _mvStep = step; _mvAt = _clock; }

    /// <summary>The first client's own blows hold while it turns for a move or climbs (an aim would turn its seat yaw off the offer).</summary>
    private bool HoldSwings => _mvStep is 1 or 2 or 5 or 6 || _dpStep is 1 or 2 or 4 or 5;

    /// <summary>
    /// Ride 1 after r1_barricade's cut, each tick: 10 m on, the seat yaw turned back; "Climb back to wagon 1" pressed on its first frame (the
    /// grip, as E: InteractIntent wagon:1); seated in W1 seat 0 by the SeatMsg with the yaw kept; the test hound knocked off W1 by our blows
    /// (FightBoarders); a second on, turned forward, "Climb forward into the cart" pressed (cart:0) and seated in a cart hero seat. True when it failed.
    /// </summary>
    private bool RideTrainMove(NetSession net, Main main, Rails.RailController rc, Party.Adventurer me)
    {
        if (_mvStep >= 7) return false;
        var party = main.Party;
        if (rc.WagonAt(1) is not { } w1) return RideFail(net, "no wagon 1 here");
        if (rc.StopIndex != 0) return RideFail(net, $"ride 1 ended (stop {rc.StopIndex}, {rc.State.Mode}) with the move to wagon 1 and back at its step {_mvStep}");
        var t = _clock - _mvAt;
        switch (_mvStep)
        {
            case 0:
                if (float.IsNaN(_mvCutS) || rc.State.Mode != Rails.CartMode.Rolling || rc.State.S < _mvCutS + 10f) return false;
                me.SeatYaw = Mathf.DegToRad(LookBackDegrees);
                _mvYawBefore = me.SeatYaw;
                GD.Print(FormattableString.Invariant($"[net] ride: move: the cart has rolled {rc.State.S - _mvCutS:0.0} m past r1_barricade's cut (S={rc.State.S:0.0}, V={rc.State.V:0.00}); our seat yaw turned to {Deg(me.SeatYaw)} degrees"));
                MvNext(1);
                return false;
            case 1:
                if (w1.Prompt == "Climb back to wagon 1" && ReferenceEquals(party.LookingAt, w1))
                {
                    party.VrUse(true); // the dispatch E uses: our TryInteract sends the wagon's intent
                    party.VrUse(false);
                    GD.Print(FormattableString.Invariant($"[net] ride: move: \"Climb back to wagon 1\" offered here {t:0.00} s after the turn (S={rc.State.S:0.0}); pressed on its first frame through the intent ({w1.NetRef})"));
                    MvNext(2);
                    return false;
                }
                return t > 3 ? RideFail(net, $"no \"Climb back to wagon 1\" offered here 3 s after turning back (wagon 1 offers \"{w1.Prompt}\", looking at {(party.LookingAt as Node)?.Name.ToString() ?? "nothing"}, moves refused {rc.MovesRefused})") : false;
            case 2:
                if (me.Cart == w1 && me.Seat == Rails.TrainRules.HeroPlace)
                {
                    var kept = Mathf.Abs(Mathf.RadToDeg(me.SeatYaw - _mvYawBefore)) <= 1f;
                    GD.Print(FormattableString.Invariant($"[net] ride: {me.State.Name} rides wagon 1 seat 0 here (its SeatMsg, {t:0.00} s after the press); our seat yaw {Deg(me.SeatYaw)} degrees, {Deg(_mvYawBefore)} at the press ({(kept ? "kept" : "NOT kept")})"));
                    MvNext(3);
                    return false;
                }
                return t > 3 ? RideFail(net, $"3 s after the press our hero sits in {(me.Cart as Node)?.Name.ToString() ?? "nothing"} seat {me.Seat} here (want wagon 1 seat 0)") : false;
            case 3: // the server boards its test hound on W1 once ride 1's own has leapt or missed; our blows knock it off (FightBoarders, own car only)
                if (me.Cart != w1) return RideFail(net, $"our hero left wagon 1 before the test hound was knocked off it (it sits in {(me.Cart as Node)?.Name.ToString() ?? "nothing"})");
                if (_koGoneAt >= 0) { MvNext(4); return false; }
                return t > 60 ? RideFail(net, $"no test hound was knocked off wagon 1 here 60 s after we sat in it (its rim holds {(w1.Boarders[0] is { } on && IsInstanceValid(on) ? $"hound {on.NetId}" : "nothing")})") : false;
            case 4:
                if (t < 1.0) return false;
                me.SeatYaw = 0f;
                GD.Print(FormattableString.Invariant($"[net] ride: move: turned forward in wagon 1 (S={rc.State.S:0.0})"));
                MvNext(5);
                return false;
            case 5:
                if (rc.Cart.Prompt == "Climb forward into the cart" && ReferenceEquals(party.LookingAt, rc.Cart))
                {
                    party.VrUse(true);
                    party.VrUse(false);
                    GD.Print(FormattableString.Invariant($"[net] ride: move: \"Climb forward into the cart\" offered here {t:0.00} s after the turn (S={rc.State.S:0.0}); pressed on its first frame through the intent ({NetRefResolver.RefOf(rc.Cart)})"));
                    MvNext(6);
                    return false;
                }
                return t > 3 ? RideFail(net, $"no \"Climb forward into the cart\" offered here 3 s after turning forward in wagon 1 (the cart offers \"{rc.Cart.Prompt}\", moves refused {rc.MovesRefused})") : false;
            case 6:
                if (me.Cart == rc.Cart && me.Seat is >= 0 and < Rails.CartSeats.HeroSeats)
                {
                    GD.Print(FormattableString.Invariant($"[net] ride: {me.State.Name} rides the party's cart seat {me.Seat} here (back from wagon 1, its SeatMsg, {t:0.00} s after the press)"));
                    MvNext(7);
                    return false;
                }
                return t > 3 ? RideFail(net, $"3 s after pressing forward our hero sits in {(me.Cart as Node)?.Name.ToString() ?? "nothing"} seat {me.Seat} here (want a hero seat of the party's cart)") : false;
        }
        return false;
    }

    // ---- ride 4: both clients press for wagon 1's place at once (Task 3 carry: a second press for a booked place is refused) ----------

    private int _dpStep;
    private double _dpAt;

    private void DpNext(int step) { _dpStep = step; _dpAt = _clock; }

    /// <summary>
    /// Each RIDE client on ride 4 (the cavern's) once both sit in the cart: 6 m out of stop 3 each turns back and presses "Climb back to wagon 1"
    /// on its first frame (or, with W1 already taken here, sends the press in flight); the server takes one and refuses the other (the place is
    /// booked). The winner climbs forward into the cart again a second on. True when it failed.
    /// </summary>
    private bool RideDoublePress(NetSession net, Main main, Rails.RailController rc, Party.Adventurer me, string who)
    {
        if (_dpStep >= 9 || Rails.RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc) || CavernStop(rc, enc) is not (>= 0 and var stop) || rc.WagonAt(1) is not { } w1) return false;
        if (rc.StopIndex != stop) return _dpStep > 0 ? RideFail(net, $"ride {stop + 1} ended with {who}'s double press at its step {_dpStep}") : false;
        var party = main.Party;
        var t = _clock - _dpAt;
        Party.Adventurer? Other() => w1.Riders.Values.OfType<Party.Adventurer>().FirstOrDefault(a => IsInstanceValid(a) && a != me && a.Cart == w1);
        switch (_dpStep)
        {
            case 0:
                if (rc.State.Mode != Rails.CartMode.Rolling || me.Cart != rc.Cart || rc.State.S < rc.StopS[stop] + DoublePressPast || rc.MovesRefused) return false;
                if (main.Party.Adventurers.Count(a => IsInstanceValid(a) && !a.Parked && a.Cart == rc.Cart) < 2) { GD.Print($"[net] ride: double press: {who} is alone in the cart here on ride {stop + 1}: not pressed"); _dpStep = 9; return false; }
                me.SeatYaw = Mathf.DegToRad(LookBackDegrees);
                GD.Print(FormattableString.Invariant($"[net] ride: double press: {who} turns back in the cart seat {me.Seat} at S={rc.State.S:0.0} on ride {stop + 1}"));
                DpNext(1);
                return false;
            case 1:
                if (w1.Prompt == "Climb back to wagon 1" && ReferenceEquals(party.LookingAt, w1))
                {
                    party.VrUse(true);
                    party.VrUse(false);
                    GD.Print(FormattableString.Invariant($"[net] ride: double press: {who} presses \"Climb back to wagon 1\" on its first frame offered it here (S={rc.State.S:0.0}, {w1.NetRef})"));
                    DpNext(2);
                    return false;
                }
                if (Other() != null || !w1.HeroPlaceFree)
                {
                    net.Send(new InteractIntent(w1.NetRef)); // the other's took the place on our screen first: our press was in flight
                    GD.Print(FormattableString.Invariant($"[net] ride: double press: {who} presses for wagon 1 as a press in flight ({(Other()?.State.Name ?? "someone")} took its place here first; S={rc.State.S:0.0}, {w1.NetRef})"));
                    DpNext(2);
                    return false;
                }
                return t > 3 ? RideFail(net, $"{who}: no \"Climb back to wagon 1\" offered here 3 s after turning back on ride {stop + 1} (wagon 1 offers \"{w1.Prompt}\", moves refused {rc.MovesRefused})") : false;
            case 2:
                if (me.Cart == w1)
                {
                    GD.Print($"[net] ride: double press: {who} won: rides wagon 1 seat {me.Seat} here");
                    DpNext(3);
                    return false;
                }
                if (Other() is { } winner && t >= 0.8)
                {
                    me.SeatYaw = 0f;
                    GD.Print($"[net] ride: double press: {who} lost: {winner.State.Name} rides wagon 1 here; we stay in the cart seat {me.Seat}");
                    DpNext(9);
                    return false;
                }
                return t > 3 ? RideFail(net, $"{who}: 3 s after the double press nobody rides wagon 1 here (we sit in {(me.Cart as Node)?.Name.ToString() ?? "nothing"})") : false;
            case 3:
                if (t < 1.0) return false;
                me.SeatYaw = 0f;
                DpNext(4);
                return false;
            case 4:
                if (rc.Cart.Prompt == "Climb forward into the cart" && ReferenceEquals(party.LookingAt, rc.Cart))
                {
                    party.VrUse(true);
                    party.VrUse(false);
                    GD.Print(FormattableString.Invariant($"[net] ride: double press: {who} presses \"Climb forward into the cart\" from wagon 1 (S={rc.State.S:0.0})"));
                    DpNext(5);
                    return false;
                }
                return t > 3 ? RideFail(net, $"{who}: no \"Climb forward into the cart\" offered in wagon 1 on ride {stop + 1} (the cart offers \"{rc.Cart.Prompt}\", moves refused {rc.MovesRefused})") : false;
            case 5:
                if (me.Cart == rc.Cart)
                {
                    GD.Print($"[net] ride: double press: {who} back in the party's cart seat {me.Seat} here");
                    DpNext(9);
                    return false;
                }
                return t > 3 ? RideFail(net, $"{who}: 3 s after pressing forward from wagon 1 we sit in {(me.Cart as Node)?.Name.ToString() ?? "nothing"} here") : false;
        }
        return false;
    }

    // ---- stop 1: the end-on revive (the first client) ------------------------------------------------------------------------------

    private int _eoStep;
    private Npc? _eoHere;
    private double _eoHereAt, _eoWaitFrom = -1;

    /// <summary>
    /// At stop 1 after cage 1's revive: a crewman down in wagon 1 here; once the server has put our hero end-on by it (more than 3.5 m from its
    /// origin, or 2 s on), its "Revive {name}" used through wagon:1 every 2 s until he is up. True once done; <paramref name="failed"/> when it failed.
    /// </summary>
    private bool RideEndOnRevive(NetSession net, Rails.RailController rc, Party.Adventurer me, CrewRescueController crew, out bool failed)
    {
        failed = false;
        if (rc.WagonAt(1) is not { } w1) { failed = RideFail(net, "no wagon 1 here at stop 1"); return false; }
        switch (_eoStep)
        {
            case 0:
                _eoHere = crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && n.Downed && !n.Dead && n.Cart == w1);
                if (_eoWaitFrom < 0) _eoWaitFrom = _clock;
                if (_eoHere == null) { if (_clock - _eoWaitFrom > 40) failed = RideFail(net, "no crewman went down in wagon 1 here at stop 1 40 s after cage 1's revive (the end-on revive)"); return false; }
                _eoHereAt = _clock;
                _rideUsedAt = -1;
                GD.Print($"[net] ride: end-on: {_eoHere.NpcName} is down in wagon 1 seat {_eoHere.Seat} here; wagon 1 offers \"{w1.Prompt}\"");
                _eoStep = 1;
                return false;
            default:
            {
                var npc = _eoHere!;
                if (!IsInstanceValid(npc) || !npc.Downed)
                {
                    GD.Print($"[net] ride: end-on: revived {npc.NpcName} in wagon 1 through the intent");
                    return true;
                }
                var origin = Mathf.Min(me.GlobalPosition.DistanceTo(w1.GlobalPosition), me.Camera.GlobalPosition.DistanceTo(w1.GlobalPosition));
                var tub = w1.TubDistance(me);
                if (_clock - _eoHereAt > 30) { failed = RideFail(net, FormattableString.Invariant($"{npc.NpcName} still down in wagon 1 30 s on (we stand {origin:0.00} m from its origin, {tub:0.00} m from its tub; it offers \"{w1.Prompt}\")")); return false; }
                var placed = origin > Rails.PropPromptRules.InteractReach || _clock - _eoHereAt > 2.0;
                if (w1.Prompt != $"Revive {npc.NpcName}" || !placed || (_rideUsedAt >= 0 && _clock - _rideUsedAt < 2)) return false;
                _rideUsedAt = _clock;
                net.Send(new InteractIntent(w1.NetRef));
                GD.Print(FormattableString.Invariant($"[net] ride: end-on: wagon 1 offers \"{w1.Prompt}\" here {origin:0.00} m from its origin and {tub:0.00} m from its tub; used through the intent ({w1.NetRef})"));
                return false;
            }
        }
    }
}
