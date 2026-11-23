using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Companions answer a boarded wagon (plan 14, Ruling 15; spec A.3), server and solo. A coupled wagon needs an answer while it carries a
/// boarder, has crew seated in it and no hero up in it (<see cref="BoarderRules.NeedsAnswer"/>). Its answerer is the nearest (by cars)
/// seated companion who is up and not already answering (at most one per wagon). It climbs toward the wagon one move at a time
/// (<see cref="TrainMoveRules.AnswerStep"/> through <see cref="RailController.Move"/>), at most one move per
/// <see cref="TrainMoveRules.MoveCooldown"/>, never while a hop of its own is under way or while <see cref="RailController.MovesRefused"/>
/// (a seated lever window, the live bridge, the train not rolling). There it fights from the seat by its own brain (its own car's boarder is
/// in its blade's reach). Once its wagon has had no boarder for <see cref="TrainMoveRules.AnswerBack"/> it climbs back toward the cart, while
/// the cart has a free hero seat (with none it stays where it sits). <see cref="DebugHoldAnswers"/> stops all of it.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>One companion's answer to one wagon: where it goes, its moves so far (cars, in order) and the clock of its wagon going clear.</summary>
    private sealed class Answer
    {
        public Adventurer Hero = null!;
        public int Wagon;
        public double LastMove = double.NegativeInfinity;
        /// <summary>When its wagon was last seen without a boarder (the server's physics clock), or −1 while it has one.</summary>
        public double ClearSince = -1;
        public bool Reached, Returning;
        public readonly List<int> Route = new();
    }

    private readonly List<Answer> _answers = new();

    /// <summary>Test hook: no wagon is answered and no answerer moves while it is set.</summary>
    internal bool DebugHoldAnswers { get; set; }

    /// <summary>Answers under way now (tests read them): who answers which wagon, the cars it moved through, whether it got there, and whether it is going back.</summary>
    internal IEnumerable<(string Name, int Wagon, IReadOnlyList<int> Route, bool Reached, bool Returning)> AnswersNow
    {
        get { foreach (var a in _answers) yield return (IsInstanceValid(a.Hero) ? a.Hero.State.Name : "?", a.Wagon, a.Route, a.Reached, a.Returning); }
    }

    /// <summary>Every answer the floor has started (tests read them; the routes kept after the answer ends).</summary>
    internal readonly List<(string Name, int Wagon, int From, List<int> Route)> AnswersStarted = new();

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

    private bool CartHeroSeatFree()
    {
        for (var i = 0; i < CartSeats.HeroSeats; i++) if (_rails.Cart.Seats.OccupantOf(i) == null) return true;
        return false;
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

    /// <summary>Each physics tick (server and solo): boarded, crewed wagons with no hero up get an answerer, and every answerer takes its next move.</summary>
    private void TickAnswers(float dt)
    {
        if (DebugHoldAnswers || !IsInstanceValid(_rails.Cart)) return;
        var party = Main.Instance.Party;
        var train = _rails.Train;
        for (var i = 0; i < train.Count; i++)
        {
            var wagon = train[i];
            if (!IsInstanceValid(wagon) || !wagon.Coupled || Answered(wagon.Number)) continue;
            if (!BoarderRules.NeedsAnswer(BoarderOn(wagon) != null, CrewIn(wagon), UpHeroesIn(wagon))) continue;
            if (Answerer(party, wagon.Number) is not { } hero) continue;
            var from = _rails.CarOf(hero);
            var a = new Answer { Hero = hero, Wagon = wagon.Number };
            _answers.Add(a);
            AnswersStarted.Add((hero.State.Name, wagon.Number, from, a.Route));
            GD.Print($"[rails] answer: {hero.State.Name} answers wagon {wagon.Number} (from car {from})");
        }
        for (var i = _answers.Count - 1; i >= 0; i--)
            if (!TickAnswer(_answers[i])) _answers.RemoveAt(i);
    }

    /// <summary>The nearest (by cars) seated companion who is up and not answering, the nearer the cart on a tie, then by party order; null when none is.</summary>
    private Adventurer? Answerer(PartyController party, int wagon)
    {
        Adventurer? best = null;
        int bestBy = int.MaxValue, bestCar = int.MaxValue;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Control != ControlKind.Companion || hero.Downed || hero.Parked || Answering(hero) || _rails.Hopping(hero)) continue;
            var car = _rails.CarOf(hero);
            if (car < 0) continue;
            var by = Math.Abs(car - wagon);
            if (by < bestBy || by == bestBy && car < bestCar) { best = hero; bestBy = by; bestCar = car; }
        }
        return best;
    }

    /// <summary>One answer's tick: false once it is over (back in the cart, staying for want of a seat there, or its answerer gone from the train or down).</summary>
    private bool TickAnswer(Answer a)
    {
        var hero = a.Hero;
        var name = IsInstanceValid(hero) ? hero.State.Name : "?";
        var car = IsInstanceValid(hero) ? _rails.CarOf(hero) : -1;
        if (car < 0 || hero.Downed || hero.Parked || _rails.WagonAt(a.Wagon) is not { Coupled: true } wagon)
        {
            GD.Print($"[rails] answer: {name} stops answering wagon {a.Wagon} ({(car < 0 ? "not in the train" : !IsInstanceValid(hero) ? "gone" : hero.Downed ? "down" : hero.Parked ? "parked" : "the wagon is lost")})");
            return false;
        }
        if (!a.Reached && !a.Returning && car == a.Wagon)
        {
            a.Reached = true;
            GD.Print($"[rails] answer: {name} reaches wagon {a.Wagon} after {a.Route.Count} move(s) [{string.Join(" > ", a.Route)}] at S={_rails.State.S:0.0}");
        }
        if (BoarderOn(wagon) != null)
        {
            a.ClearSince = -1;
            if (a.Returning) { a.Returning = false; a.Reached = car == a.Wagon; GD.Print($"[rails] answer: {name} turns back for wagon {a.Wagon} (boarded again)"); }
        }
        else if (a.ClearSince < 0) a.ClearSince = _now;
        if (_rails.Hopping(hero)) return true; // a hop under way decides nothing (its booking holds the place it lands in)
        if (!a.Returning && a.ClearSince >= 0 && _now - a.ClearSince >= TrainMoveRules.AnswerBack)
        {
            if (car == 0) return false; // the boarder went before it left the cart
            if (!CartHeroSeatFree()) { GD.Print($"[rails] answer: {name} stays in car {car}: the cart has no free hero seat"); return false; }
            a.Returning = true;
            GD.Print($"[rails] answer: {name} goes back to the cart (wagon {a.Wagon} clear {_now - a.ClearSince:0.0} s)");
        }
        if (a.Returning && car == 0)
        {
            GD.Print($"[rails] answer: {name} is back in the cart after {a.Route.Count} move(s) [{string.Join(" > ", a.Route)}]");
            return false;
        }
        if (a.Returning && !CartHeroSeatFree()) { GD.Print($"[rails] answer: {name} stays in car {car}: the cart has no free hero seat"); return false; }
        var toward = a.Returning ? 0 : a.Wagon;
        if (car == toward || _rails.MovesRefused || _now - a.LastMove < TrainMoveRules.MoveCooldown) return true;
        if (_rails.AnswerStepFor(hero, toward) is not { } step) return true; // every place that way short of where it goes is taken: it waits
        if (_rails.Move(hero, step, "answer"))
        {
            a.LastMove = _now;
            a.Route.Add(_rails.CarCarrying(step as Node));
        }
        return true;
    }

    /// <summary>The answers end with the floor's train undone (a wreck, a restore): whoever answered sits where the restore put it.</summary>
    private void ClearAnswers() => _answers.Clear();
}
