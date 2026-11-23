using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Companions answer a boarded wagon (plan 14, Ruling 15 as amended in Task 4's fix round; spec A.3), server and solo. A coupled wagon needs
/// an answer while it carries a boarder, has crew seated in it and no hero up in it (<see cref="BoarderRules.NeedsAnswer"/>). At most one
/// companion answers each wagon, chosen by <see cref="BoarderRules.PickAnswerer"/> among the seated companions (and hero bots) who are up and
/// not answering:
/// <list type="bullet">
/// <item>One with a clear shot at the boarder (<see cref="Adventurer.HasShotAt"/>) answers from its seat: its brain targets that boarder
/// (<see cref="Adventurer.AnswerTarget"/>) and it never moves. The answer ends with the wagon clear, or when it loses the shot.</item>
/// <item>Only with nobody having a shot does an answerer move: the nearest blade or hero bot climbs toward the wagon one car at a time
/// (<see cref="TrainMoveRules.AnswerStep"/> through <see cref="RailController.Move"/>), at most one move per <see cref="TrainMoveRules.MoveCooldown"/>,
/// never while a hop of its own is under way or while <see cref="RailController.MovesRefused"/>, and only while its wagon still has a boarder.
/// There it fights from the seat by its own brain; when a downed hero holds that wagon's hero place it lands on a free crew bench seat of it
/// (<see cref="RailController.AnswerSeatIn"/>; Task 5, Task 4 review M2), and with both bench seats held by crew it waits one car short. Once
/// its wagon has had no boarder for <see cref="TrainMoveRules.AnswerBack"/> it climbs back toward the cart, while the cart has a free hero seat (with none it stays where it sits).</item>
/// <item>A companion left in a wagon (the final review's M8: an answer that ended with it there, the cart having no free hero seat or the
/// answerer going down; or a lead the player possessed away from in a wagon) goes back toward the cart by itself once a hero seat of the cart is
/// free, by the answer's rules: one car per <see cref="TrainMoveRules.MoveCooldown"/>, none while <see cref="RailController.MovesRefused"/> or a
/// hop of its own is under way, and never while its own wagon carries a boarder (it fights from there). Answering a wagon again ends it.</item>
/// </list>
/// <see cref="DebugHoldAnswers"/> stops all of it.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>One companion's answer to one wagon: from its seat or by moving, its moves so far (cars, in order) and the clock of its wagon going clear.</summary>
    private sealed class Answer
    {
        public Adventurer Hero = null!;
        public int Wagon;
        public bool FromSeat;
        /// <summary>A seat answer: when its answerer was last seen losing its shot (the server's physics clock), or −1 while it has one.</summary>
        public double ShotLostAt = -1;
        public double LastMove = double.NegativeInfinity;
        /// <summary>When its wagon was last seen without a boarder (the server's physics clock), or −1 while it has one.</summary>
        public double ClearSince = -1;
        public bool Reached, Returning;
        public readonly List<int> Route = new();
        /// <summary>Its entry in <see cref="AnswersStarted"/>.</summary>
        public int Record;
    }

    private readonly List<Answer> _answers = new();

    /// <summary>A companion left in a wagon going back to the cart by itself (M8), and when it last moved (the server's physics clock).</summary>
    private sealed class Stray
    {
        public Adventurer Hero = null!;
        public double LastMove = double.NegativeInfinity;
    }

    private readonly List<Stray> _strays = new();
    /// <summary>Each party member was played by a player (this machine's or a remote one) at the last tick, by index: a possession away from one in a wagon leaves it a stray.</summary>
    private readonly bool[] _wasPlayed = new bool[8];

    /// <summary>Companions left in a wagon going back to the cart by themselves now (tests read them).</summary>
    internal IEnumerable<string> StraysNow { get { foreach (var s in _strays) yield return IsInstanceValid(s.Hero) ? s.Hero.State.Name : "?"; } }
    private readonly List<BoarderRules.AnswerCandidate> _candidates = new();
    private readonly List<Adventurer> _candidateHeroes = new();

    /// <summary>Test hook: no wagon is answered and no answerer moves while it is set.</summary>
    internal bool DebugHoldAnswers { get; set; }

    /// <summary>Answers under way now (tests read them): who answers which wagon, from its seat or not, the cars it moved through, whether it got there, and whether it is going back.</summary>
    internal IEnumerable<(string Name, int Wagon, bool FromSeat, IReadOnlyList<int> Route, bool Reached, bool Returning)> AnswersNow
    {
        get { foreach (var a in _answers) yield return (IsInstanceValid(a.Hero) ? a.Hero.State.Name : "?", a.Wagon, a.FromSeat, a.Route, a.Reached, a.Returning); }
    }

    /// <summary>
    /// Every answer the floor has started (tests read them): who, which wagon, from which car, from its seat or not, its route (kept after the
    /// answer ends), and the physics frames it started and ended at (<see cref="ulong.MaxValue"/> while it runs).
    /// </summary>
    internal readonly List<(string Name, int Wagon, int From, bool FromSeat, List<int> Route, ulong Started, ulong Ended)> AnswersStarted = new();

    /// <summary>The living boarder riding <paramref name="wagon"/>'s rim now, or null.</summary>
    private Enemies.Enemy? BoarderOn(TrainWagon wagon)
    {
        for (var i = 0; i < _leaps.Count; i++)
        {
            var l = _leaps[i];
            if (l.Phase == LeapPhase.Aboard && l.Dest == wagon && IsInstanceValid(l.Hound) && l.Hound.IsAlive && l.Hound.Carrier == wagon) return l.Hound;
        }
        return null;
    }

    /// <summary>The heroes up and seated in <paramref name="wagon"/>.</summary>
    private static int UpHeroesIn(TrainWagon wagon)
    {
        var n = 0;
        foreach (var rider in wagon.Riders.Values)
            if (rider is Adventurer { Downed: false, Parked: false } hero && IsInstanceValid(hero)) n++;
        return n;
    }

    private bool Answering(Adventurer hero)
    {
        for (var i = 0; i < _answers.Count; i++) if (_answers[i].Hero == hero) return true;
        return false;
    }

    private bool Answered(int wagon)
    {
        for (var i = 0; i < _answers.Count; i++) if (_answers[i].Wagon == wagon) return true;
        return false;
    }

    /// <summary>Each physics tick (server and solo): boarded, crewed wagons with no hero up get an answerer, and every answer goes on.</summary>
    private void TickAnswers()
    {
        if (DebugHoldAnswers || !IsInstanceValid(_rails.Cart)) return;
        var party = Main.Instance.Party;
        var train = _rails.Train;
        // the answers under way first: one that ends this tick (its answerer pumps, goes down, loses its shot past the grace, is taken over)
        // leaves its wagon to be answered afresh below, in the same tick
        for (var i = _answers.Count - 1; i >= 0; i--)
        {
            if (TickAnswer(_answers[i])) continue;
            EndAnswer(_answers[i]);
            LeftInAWagon(_answers[i].Hero, "its answer ended there");
            _answers.RemoveAt(i);
        }
        var heroes = party.Adventurers;
        for (var i = 0; i < heroes.Length && i < _wasPlayed.Length; i++)
        {
            var hero = heroes[i];
            var played = IsInstanceValid(hero) && hero.Control is ControlKind.LocalHuman or ControlKind.Remote;
            if (_wasPlayed[i] && !played && IsInstanceValid(hero)) LeftInAWagon(hero, "the player took another hero");
            _wasPlayed[i] = played;
        }
        TickStrays();

        for (var i = 0; i < train.Count; i++)
        {
            var wagon = train[i];
            if (!IsInstanceValid(wagon) || !wagon.Coupled || Answered(wagon.Number)) continue;
            if (BoarderOn(wagon) is not { } boarder || !BoarderRules.NeedsAnswer(true, CrewIn(wagon), UpHeroesIn(wagon))) continue;
            if (Answerer(party, wagon.Number, boarder) is not var (hero, fromSeat)) continue;
            var from = _rails.CarOf(hero);
            var a = new Answer { Hero = hero, Wagon = wagon.Number, FromSeat = fromSeat, Record = AnswersStarted.Count };
            _answers.Add(a);
            AnswersStarted.Add((hero.State.Name, wagon.Number, from, fromSeat, a.Route, Engine.GetPhysicsFrames(), ulong.MaxValue));
            if (fromSeat) hero.AnswerTarget = boarder;
            GD.Print($"[rails] answer: {hero.State.Name} answers wagon {wagon.Number} {(fromSeat ? $"from its seat in car {from} ({hero.GlobalPosition.DistanceTo(boarder.GlobalPosition):0.0} m, a clear shot)" : $"(from car {from})")}");
        }
    }

    /// <summary>The answer is over: its seat target let go, its record closed.</summary>
    private void EndAnswer(Answer a)
    {
        if (IsInstanceValid(a.Hero)) a.Hero.AnswerTarget = null;
        if (a.Record >= 0 && a.Record < AnswersStarted.Count) AnswersStarted[a.Record] = AnswersStarted[a.Record] with { Ended = Engine.GetPhysicsFrames() };
    }

    /// <summary>
    /// The answerer for wagon <paramref name="wagon"/> and its <paramref name="boarder"/> (<see cref="BoarderRules.PickAnswerer"/>) among the
    /// seated companions (hero bots on a server among them) who are up, not answering and not hopping; null when nobody may.
    /// </summary>
    private (Adventurer Hero, bool FromSeat)? Answerer(PartyController party, int wagon, Enemies.Enemy boarder)
    {
        _candidates.Clear();
        _candidateHeroes.Clear();
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Control != ControlKind.Companion || hero.Downed || hero.Parked || Answering(hero) || _rails.Hopping(hero)) continue;
            var car = _rails.CarOf(hero);
            if (car < 0) continue;
            _candidates.Add(new BoarderRules.AnswerCandidate(_candidateHeroes.Count, car, Blade: hero.Blade, Bot: NetSession.IsServer && hero.Leader != null, HasShot: hero.HasShotAt(boarder), Pumping: hero.Pumping));
            _candidateHeroes.Add(hero);
        }
        return BoarderRules.PickAnswerer(wagon, _candidates) is { } pick ? (_candidateHeroes[pick.Order], pick.FromSeat) : null;
    }

    /// <summary>One answer's tick: false once it is over.</summary>
    private bool TickAnswer(Answer a)
    {
        var hero = a.Hero;
        var name = IsInstanceValid(hero) ? hero.State.Name : "?";
        var car = IsInstanceValid(hero) ? _rails.CarOf(hero) : -1;
        if (car < 0 || hero.Downed || hero.Parked || hero.Control != ControlKind.Companion || _rails.WagonAt(a.Wagon) is not { Coupled: true } wagon)
        {
            GD.Print($"[rails] answer: {name} stops answering wagon {a.Wagon} ({(car < 0 ? "not in the train" : hero.Downed ? "down" : hero.Parked ? "parked" : hero.Control != ControlKind.Companion ? "a player took it over" : "the wagon is lost")})");
            return false;
        }
        var boarder = BoarderOn(wagon);
        if (a.FromSeat)
        {
            if (boarder == null) { GD.Print($"[rails] answer: {name}'s answer to wagon {a.Wagon} from its seat ends: the wagon is clear"); return false; }
            if (hero.HasShotAt(boarder)) a.ShotLostAt = -1;
            else if (a.ShotLostAt < 0) a.ShotLostAt = _now;
            var lostFor = a.ShotLostAt < 0 ? 0 : _now - a.ShotLostAt;
            if (!BoarderRules.SeatAnswerHolds(up: true, companion: true, hero.Pumping, lostFor))
            {
                GD.Print($"[rails] answer: {name}'s answer to wagon {a.Wagon} from its seat ends: {(hero.Pumping ? "it pumps" : $"no shot for {lostFor:0.00} s")}; the wagon is open again");
                return false;
            }
            hero.AnswerTarget = boarder; // a fresh boarder on the same rim is the same answer
            return true;
        }
        if (!a.Reached && !a.Returning && car == a.Wagon)
        {
            a.Reached = true;
            GD.Print($"[rails] answer: {name} reaches wagon {a.Wagon} after {a.Route.Count} move(s) [{string.Join(" > ", a.Route)}] at S={_rails.State.S:0.0}");
        }
        if (boarder != null)
        {
            a.ClearSince = -1;
            if (a.Returning) { a.Returning = false; a.Reached = car == a.Wagon; GD.Print($"[rails] answer: {name} turns back for wagon {a.Wagon} (boarded again)"); }
        }
        else if (a.ClearSince < 0)
        {
            a.ClearSince = _now;
            if (!a.Reached) GD.Print($"[rails] answer: {name} holds in car {car}: wagon {a.Wagon} is clear before it got there");
        }
        if (_rails.Hopping(hero)) return true; // a hop under way decides nothing (its booking holds the place it lands in)
        if (!a.Returning && a.ClearSince >= 0 && _now - a.ClearSince >= TrainMoveRules.AnswerBack)
        {
            if (car == 0) return false; // the boarder went before it left the cart
            if (!_rails.HeroSeatFree()) { GD.Print($"[rails] answer: {name} stays in car {car}: the cart has no free hero seat"); return false; }
            a.Returning = true;
            GD.Print($"[rails] answer: {name} goes back to the cart (wagon {a.Wagon} clear {_now - a.ClearSince:0.0} s)");
        }
        if (a.Returning && car == 0)
        {
            GD.Print($"[rails] answer: {name} is back in the cart after {a.Route.Count} move(s) [{string.Join(" > ", a.Route)}]");
            return false;
        }
        if (a.Returning && !_rails.HeroSeatFree()) { GD.Print($"[rails] answer: {name} stays in car {car}: the cart has no free hero seat"); return false; }
        if (!a.Returning && boarder == null) return true; // the wagon is clear: it advances no further (fix round 1, review M1)
        var toward = a.Returning ? 0 : a.Wagon;
        if (car == toward || _rails.MovesRefused || _now - a.LastMove < TrainMoveRules.MoveCooldown) return true;
        if (_rails.AnswerStepFor(hero, toward) is not int step || _rails.CarAt(step) is not { } to) return true; // every place that way short of where it goes is taken: it waits
        if (_rails.Move(hero, to, "answer", answerTo: a.Returning ? -1 : a.Wagon)) // its own wagon's downed hero never keeps it out (Task 5, Task 4 review M2)
        {
            a.LastMove = _now;
            a.Route.Add(step);
        }
        return true;
    }

    /// <summary>The answers end with the floor's train undone (a wreck, a restore, the party on foot): whoever answered sits where it was put.</summary>
    private void ClearAnswers()
    {
        foreach (var a in _answers) EndAnswer(a);
        _answers.Clear();
        _strays.Clear();
    }

    /// <summary><paramref name="hero"/>, a companion up or down in a coupled wagon, is left there: it goes back to the cart by itself (<see cref="TickStrays"/>).</summary>
    private void LeftInAWagon(Adventurer hero, string why)
    {
        if (!IsInstanceValid(hero) || hero.Control != ControlKind.Companion || hero.Parked || _rails.CarOf(hero) < 1) return;
        for (var i = 0; i < _strays.Count; i++) if (_strays[i].Hero == hero) return;
        _strays.Add(new Stray { Hero = hero });
        GD.Print($"[rails] answer: {hero.State.Name} is left in car {_rails.CarOf(hero)} ({why}): it goes back to the cart once a hero seat there is free");
    }

    /// <summary>
    /// Each tick (server and solo): every companion left in a wagon goes back toward the cart while a hero seat of it is free (M8), one car per
    /// cooldown, none while moves are refused, a hop of its own is under way or its own wagon carries a boarder; home, gone from the train, taken
    /// over by a player or answering a wagon again, it is no longer a stray. No allocation.
    /// </summary>
    private void TickStrays()
    {
        for (var i = _strays.Count - 1; i >= 0; i--)
        {
            var s = _strays[i];
            var hero = s.Hero;
            var car = IsInstanceValid(hero) ? _rails.CarOf(hero) : -1;
            if (car < 1 || hero.Control != ControlKind.Companion || Answering(hero))
            {
                if (car == 0) GD.Print($"[rails] answer: {hero.State.Name} is back in the cart by itself");
                _strays.RemoveAt(i);
                continue;
            }
            if (hero.Downed || hero.Parked || _rails.Hopping(hero) || _rails.MovesRefused || _now - s.LastMove < TrainMoveRules.MoveCooldown || !_rails.HeroSeatFree()) continue;
            if (_rails.WagonAt(car) is { } own && BoarderOn(own) != null) continue; // it fights the boarder of its own wagon from there
            if (_rails.AnswerStepFor(hero, 0) is not int step || _rails.CarAt(step) is not { } to) continue;
            if (_rails.Move(hero, to, "back to the cart")) s.LastMove = _now;
        }
    }
}
