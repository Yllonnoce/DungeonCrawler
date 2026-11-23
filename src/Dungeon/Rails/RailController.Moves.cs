using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Climbing car to car (plan 14, Rulings 7-11; <see cref="TrainMoveRules"/>): a seated hero who is up moves along the rolling train by a seat
/// change, never a get-out. The local hero's offer (<see cref="MoveOfferFor"/>, every role from its own copies) follows the seat's yaw: back past
/// <see cref="TrainMoveRules.BackCone"/>, else forward from a wagon; the target is the nearest car that way with its hero place free (the cart's
/// first free hero seat, a wagon's seat 0), passing taken ones. The press (E or the VR grip, through the car's own use) books that place
/// (<see cref="Move"/>, server and solo); the body stays in its old seat for <see cref="TrainMoveRules.HopSeconds"/>, counted aboard the old car,
/// then sits in the new one with its yaw kept, unless it went down or left the train, or the target was uncoupled, meanwhile (the booking is then
/// dropped and it stays where it sits). E does nothing for <see cref="TrainMoveRules.MoveCooldown"/> after a press on the presser's machine
/// (<see cref="InMoveGrace"/>); the server takes a press <see cref="TrainMoveRules.AcceptSlack"/> sooner from the last it took for that hero, and
/// only for the place its own book names (<see cref="MoveAccepts"/>). Nobody moves while <see cref="MovesRefused"/>.
/// </summary>
public partial class RailController
{
    /// <summary>A move under way (server and solo): the hero, the place booked for it, the cars, when it was taken and why.</summary>
    private sealed record Hop(Adventurer Hero, ICartBody Target, int Seat, int From, int To, double At, string Why);

    private readonly List<Hop> _hops = new();
    /// <summary>Each hero's last move press on this machine (physics seconds, <see cref="_now"/>), by party index: the offer's cooldown.</summary>
    private readonly Dictionary<int, double> _movePressedAt = new();
    /// <summary>Each hero's last move taken here (server and solo; physics seconds), by party index: the acceptance's cooldown.</summary>
    private readonly Dictionary<int, double> _moveAcceptedAt = new();
    /// <summary>The places free now, by car (reused: the local hero's offer asks every view tick).</summary>
    private readonly List<bool> _placeFree = new();

    /// <summary>Moves taken, landed and dropped on this floor build (tests read them).</summary>
    internal int MovesTaken { get; private set; }
    internal int MovesLanded { get; private set; }
    internal int MovesDropped { get; private set; }
    /// <summary>Why the last move asked for here was refused (test hook), or null.</summary>
    internal string? LastMoveRefused { get; private set; }
    /// <summary>The last landing (test hook): who, the car and seat it landed in, and how long after the press.</summary>
    internal (string Name, int Car, int Seat, double After)? LastMoveLanded { get; private set; }

    /// <summary>
    /// Moves are refused for everyone (Ruling 11; every role, this peer's own cart): the train not rolling, a seated lever window of the armed ride
    /// open (<see cref="RailEncounterController.AnyLeverWindow"/>), or the train on ride 4's live bridge (<see cref="HoldsRiders"/>).
    /// </summary>
    public bool MovesRefused => TrainMoveRules.Refused(State.Mode == CartMode.Rolling, LeverWindowOpen, HoldsRiders);

    private static bool LeverWindowOpen => RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.AnyLeverWindow();

    /// <summary>The train's car <paramref name="car"/>: 0 the party's cart, k the coupled wagon Wk; null for any other.</summary>
    private ICartBody? CarBody(int car) => car == 0 ? Cart : WagonAt(car) is { Coupled: true } wagon ? wagon : null;

    /// <summary>The car <paramref name="target"/> is: 0 the party's cart, k the coupled wagon Wk, −1 anything else.</summary>
    private int CarNumberOf(object? target)
        => target == Cart ? 0 : target is TrainWagon { Coupled: true } wagon && WagonAt(wagon.Number) == wagon ? wagon.Number : -1;

    /// <summary>The hero places free now, by car: [0] a hero seat of the cart, [k] Wk's seat 0 (a wagon lost reads taken). No allocation.</summary>
    private IReadOnlyList<bool> PlacesFree()
    {
        _placeFree.Clear();
        _placeFree.Add(HeroSeatFree());
        for (var k = 1; k <= _wagons.Count; k++) _placeFree.Add(CarBody(k) is TrainWagon wagon && wagon.HeroPlaceFree);
        return _placeFree;
    }

    private double SincePress(Adventurer hero) => _movePressedAt.TryGetValue(hero.Index, out var at) ? _now - at : double.PositiveInfinity;

    private bool HopUnderWay(Adventurer hero)
    {
        for (var i = 0; i < _hops.Count; i++) if (_hops[i].Hero == hero) return true;
        return false;
    }

    /// <summary>A move of <paramref name="hero"/>'s is under way here (server and solo; test hook).</summary>
    internal bool Hopping(Adventurer hero) => HopUnderWay(hero);

    /// <summary>
    /// The move offered to <paramref name="hero"/> now, every role from this peer's copies: the car it would climb to (the party's cart or a
    /// coupled wagon), or null. Only the hero this machine plays (companions move by their own brain, Task 4), up, seated in the train, moves
    /// not refused, <see cref="TrainMoveRules.MoveCooldown"/> after its last press here (<see cref="TrainMoveRules.Offered"/>), no hop of its own
    /// under way; the way read from its seat yaw (<see cref="TrainMoveRules.Direction"/>) and the target by <see cref="TrainMoveRules.Target"/>.
    /// </summary>
    public ICartBody? MoveOfferFor(Adventurer hero)
    {
        if (!Current || !IsInstanceValid(hero) || hero.Control != ControlKind.LocalHuman || hero.Downed || hero.Parked) return null;
        var from = CarOf(hero);
        if (from < 0 || !TrainMoveRules.Offered(up: true, inTrain: true, MovesRefused, SincePress(hero)) || HopUnderWay(hero)) return null;
        var dir = TrainMoveRules.Direction(inCart: from == 0, Mathf.RadToDeg(hero.SeatYaw));
        return TrainMoveRules.Target(from, dir, PlacesFree()) is int to ? CarBody(to) : null;
    }

    /// <summary>The move's prompt on <paramref name="car"/> for <paramref name="hero"/>, or null when no move to it is offered.</summary>
    private string? MovePromptFor(ICartBody car, Adventurer hero)
        => MoveOfferFor(hero) == car ? TrainMoveRules.Prompt(CarOf(hero), CarNumberOf(car)) : null;

    /// <summary>The hero this machine plays pressed a move (its own clock): no move is offered and E does nothing for <see cref="TrainMoveRules.MoveCooldown"/>.</summary>
    public void MovePressed(Adventurer hero)
    {
        if (IsInstanceValid(hero)) _movePressedAt[hero.Index] = _now;
    }

    /// <summary>E does nothing for <paramref name="hero"/> now: seated, within <see cref="TrainMoveRules.MoveCooldown"/> of a move's press on this machine (Ruling 9).</summary>
    public bool InMoveGrace(Adventurer hero) => IsInstanceValid(hero) && hero.Seated && SincePress(hero) < TrainMoveRules.MoveCooldown;

    /// <summary>
    /// A use of <paramref name="target"/> by <paramref name="actor"/> is a move press: the actor rides another car of the train, moves are not
    /// refused, and <paramref name="target"/> is the place a move that way goes to on this peer's book. Anything else is the car's plain use
    /// (a revive from beside the wagon, a get-out).
    /// </summary>
    public bool IsMovePress(Adventurer actor, object target)
    {
        if (!Current || !IsInstanceValid(actor)) return false;
        var from = CarOf(actor);
        var to = CarNumberOf(target);
        if (from < 0 || to < 0 || from == to || MovesRefused) return false;
        return TrainMoveRules.Target(from, Math.Sign(to - from), PlacesFree()) == to;
    }

    /// <summary>The server (and single player) takes <paramref name="body"/>'s move to <paramref name="target"/> (Ruling 10). False on a client.</summary>
    public bool MoveAccepts(Adventurer body, Node target) => WhyMoveRefused(body, target) == null;

    /// <summary>Why the server (or single player) would refuse <paramref name="body"/>'s move to <paramref name="target"/> now, or null when it takes it.</summary>
    public string? WhyMoveRefused(Adventurer body, object target)
    {
        if (NetSession.IsClient) return "a client judges no move";
        if (!Current || !IsInstanceValid(body)) return "no line";
        var to = CarNumberOf(target);
        if (to < 0) return "not a car of the train";
        var from = CarOf(body);
        var up = !body.Downed && !body.Parked;
        var inTrain = from >= 0;
        var refused = MovesRefused;
        var since = _moveAcceptedAt.TryGetValue(body.Index, out var at) ? _now - at : double.PositiveInfinity;
        int? serverTarget = inTrain && from != to ? TrainMoveRules.Target(from, Math.Sign(to - from), PlacesFree()) : null;
        if (TrainMoveRules.Accepted(up, inTrain, refused, since, to, serverTarget) && !HopUnderWay(body)) return null;
        if (!up) return "down";
        if (!inTrain) return "not in the train";
        if (from == to) return "already in that car";
        if (refused) return State.Mode != CartMode.Rolling ? "the train is not rolling" : HoldsRiders ? "the train is on the live bridge" : "a lever window is open";
        if (HopUnderWay(body)) return "a hop under way";
        if (since < TrainMoveRules.MoveCooldown - TrainMoveRules.AcceptSlack) return $"{since:0.00} s after the last";
        return PlacesFree()[to] ? $"the nearest free place that way is car {serverTarget?.ToString() ?? "none"}" : "the place is booked";
    }

    /// <summary>
    /// <paramref name="hero"/> climbs to <paramref name="target"/> (server and solo; taken by <see cref="WhyMoveRefused"/>): the place is booked in
    /// its seat book (the cart's first free hero seat, a wagon's seat 0), the hop starts (it lands in <see cref="TickMoves"/>) and the jump's cue
    /// plays where the body sits. <paramref name="why"/> says who asked (a press, a companion's answer, a test). True when it was taken.
    /// </summary>
    public bool Move(Adventurer hero, ICartBody target, string why)
    {
        if (NetSession.IsClient) return false;
        var name = IsInstanceValid(hero) ? hero.State.Name : "?";
        var from = IsInstanceValid(hero) ? CarOf(hero) : -1;
        var to = CarNumberOf(target);
        if (WhyMoveRefused(hero, target) is { } no)
        {
            LastMoveRefused = no;
            GD.Print($"[rails] move refused ({no}): {name} from car {from} to car {to} ({why}) at S={State.S:0.0}");
            return false;
        }
        int seat;
        if (to == 0)
        {
            if (Cart.Seats.Take(hero.RiderId, isHero: true) is not int s) return false;
            seat = s;
        }
        else
        {
            if (!target.Seats.Put(hero.RiderId, TrainRules.HeroPlace)) return false;
            seat = TrainRules.HeroPlace;
        }
        _moveAcceptedAt[hero.Index] = _now;
        _hops.Add(new Hop(hero, target, seat, from, to, _now, why));
        MovesTaken++;
        LastMoveRefused = null;
        RideAudio.At("cart_jump", hero.GlobalPosition);
        GD.Print($"[rails] move: {name} {(to > from ? "back" : "forward")} from car {from} to car {to} ({why}) at S={State.S:0.0}");
        return true;
    }

    /// <summary>
    /// Each physics tick (server and solo): every hop under way lands once <see cref="TrainMoveRules.HopSeconds"/> have passed since its press
    /// (the body sits in the booked place with its yaw kept, the landing's cue and a small shake, none in a headset here; the log line), or is
    /// dropped first when <see cref="TrainMoveRules.Lands"/> says no or the booking is gone (a restore emptied the book): the booking is let go
    /// and the body stays where it sits.
    /// </summary>
    private void TickMoves()
    {
        for (var i = _hops.Count - 1; i >= 0; i--)
        {
            var hop = _hops[i];
            var hero = hop.Hero;
            var heroOk = IsInstanceValid(hero) && !hero.Downed && !hero.Parked && CarOf(hero) == hop.From;
            var targetOk = CarBody(hop.To) == hop.Target;
            var booked = hop.Target.Seats.OccupantOf(hop.Seat) == hero.RiderId;
            if (!TrainMoveRules.Lands(heroOk, targetOk) || !booked)
            {
                _hops.RemoveAt(i);
                if (booked) hop.Target.Seats.Leave(hero.RiderId);
                MovesDropped++;
                var why = !booked ? "the booking is gone" : !targetOk ? $"car {hop.To} is gone" : !IsInstanceValid(hero) ? "gone" : hero.Downed ? "down" : "left the train";
                GD.Print($"[rails] move: {(IsInstanceValid(hero) ? hero.State.Name : "?")}'s hop to car {hop.To} dropped ({why}); it stays in car {(IsInstanceValid(hero) ? CarOf(hero) : -1)}");
                continue;
            }
            var after = _now - hop.At;
            if (after < TrainMoveRules.HopSeconds - 1e-6) continue;
            _hops.RemoveAt(i);
            hero.SitIn(hop.Target, hop.Seat, keepYaw: true); // leaves the old car's book: a seat change, never a get-out
            MovesLanded++;
            LastMoveLanded = (hero.State.Name, hop.To, hop.Seat, after);
            EventBus.Instance.Log(TrainMoveRules.Log(hero.State.Name, hop.From, hop.To));
            RideAudio.At("cart_land", hero.GlobalPosition);
            if (hero.Control == ControlKind.Remote || !Vr.Active) hero.Shake(0.05f, 0.3f);
            GD.Print($"[rails] move: {hero.State.Name} lands in car {hop.To} seat {hop.Seat} after {after:0.00} s");
        }
    }

    /// <summary>Every hop under way is dropped with its booking (a restore: nobody rides on).</summary>
    private void DropHops()
    {
        foreach (var hop in _hops)
            if (IsInstanceValid(hop.Hero) && hop.Target.Seats.OccupantOf(hop.Seat) == hop.Hero.RiderId) hop.Target.Seats.Leave(hop.Hero.RiderId);
        _hops.Clear();
        _moveAcceptedAt.Clear();
    }

    /// <summary>Test hook (server and solo): <paramref name="hero"/> sits in car <paramref name="car"/>'s hero place at once, as a landed move. True when it did.</summary>
    internal bool DebugSeat(Adventurer hero, int car)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(hero) || CarBody(car) is not { } body) return false;
        int seat;
        if (car == 0)
        {
            if (hero.Cart != Cart && !HeroSeatFree() || Cart.Seats.Take(hero.RiderId, isHero: true) is not int s) return false;
            seat = s;
        }
        else
        {
            if (!body.Seats.Put(hero.RiderId, TrainRules.HeroPlace)) return false;
            seat = TrainRules.HeroPlace;
        }
        hero.SitIn(body, seat, keepYaw: true);
        GD.Print($"[rails] move: {hero.State.Name} seated in car {car} seat {seat} (a test's hook)");
        RefreshHud();
        return true;
    }

    // ---- a wagon's own rider (Ruling 9) ----------------------------------------------------------

    /// <summary>A wagon's own rider may get out now: up, held or rolling under <see cref="TrainMoveRules.GetOutBelow"/>, and not on the live bridge.</summary>
    private bool WagonGetOutOk(Adventurer hero)
        => !hero.Downed && !hero.Parked && !HoldsRiders && TrainMoveRules.WagonGetOut(State.Mode == CartMode.Rolling, State.V);

    /// <summary>A wagon's own rider steps out beside the lane where the wagon stands (server and solo): only the actor, never its companions (Ruling 12).</summary>
    private void GetOutOfWagon(Adventurer actor, TrainWagon wagon)
    {
        _exitTaken.Clear();
        SetDownFromWagon(actor, wagon);
        GD.Print($"[rails] {actor.State.Name} gets out of wagon {wagon.Number} at S={State.S:0.0}");
        RefreshHud();
    }

    /// <summary>A rider of <paramref name="wagon"/> steps out beside the lane by it, on a cell nobody else just took.</summary>
    private void SetDownFromWagon(ICartRider rider, TrainWagon wagon)
    {
        var cell = ExitCellNear(wagon.GlobalPosition, atStop: true, useTaken: true);
        _exitTaken.Add(cell);
        rider.GetOut(_builder.Surface(cell), Vector3.Zero);
    }

    /// <summary>
    /// Every hero seated in a coupled wagon steps out beside the lane by it (crew keep their seats): nobody rides on without a player, and a wreck
    /// sets them down as it sets down the cart's riders, with its toll (<paramref name="wreckToll"/>; Ruling 12). No allocation.
    /// </summary>
    private void SetDownWagonHeroes(bool wreckToll)
    {
        foreach (var wagon in _train.Wagons)
        {
            if (!IsInstanceValid(wagon) || wagon.Riders.Count == 0) continue;
            _fallRiders.Clear();
            foreach (var rider in wagon.Riders.Values)
                if (rider is Adventurer hero && IsInstanceValid(hero)) _fallRiders.Add(hero);
            foreach (var rider in _fallRiders)
            {
                var downed = rider is Adventurer { Downed: true };
                SetDownFromWagon(rider, wagon);
                if (wreckToll && !downed && rider is IHazardVictim victim)
                    victim.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, WreckHpFraction, 0f), SourceName, HazardIndex);
            }
        }
        _fallRiders.Clear();
    }

    /// <summary>A hero sits in a coupled wagon of the train. No allocation.</summary>
    private bool WagonHeroAboard()
    {
        foreach (var wagon in _train.Wagons)
            if (IsInstanceValid(wagon))
                foreach (var rider in wagon.Riders.Values)
                    if (rider is Adventurer hero && IsInstanceValid(hero)) return true;
        return false;
    }
}
