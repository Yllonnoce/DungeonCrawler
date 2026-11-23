namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Climbing car to car (plan 14, Rulings 7-11): a seated hero who is up moves along the rolling train by a seat change, never a get-out.
/// Cars are numbered as <see cref="RailController.CarOf"/> numbers them: 0 the party's cart, k the coupled wagon Wk; back is +1 (toward the
/// train's rear), forward −1. Looking more than <see cref="BackCone"/> off the seat's forward is "back"; otherwise "forward" from a wagon and
/// no move from the cart. The target is the nearest car that way whose hero place is free, passing taken ones (one move, one hop). The press
/// books it; the body hops for <see cref="HopSeconds"/> and lands; E does nothing for <see cref="MoveCooldown"/> after a press, and the
/// server takes a press <see cref="AcceptSlack"/> sooner. Engine-free.
/// </summary>
public static class TrainMoveRules
{
    /// <summary>The hop: the press's body stays in its old seat this long, then lands (Ruling 7).</summary>
    public const float HopSeconds = 0.6f;
    /// <summary>After a move's press, no move is offered and E does nothing for this long on the presser's machine (Ruling 9).</summary>
    public const float MoveCooldown = 1f;
    /// <summary>The server takes a hero's press this much sooner than <see cref="MoveCooldown"/> after the last it took (Ruling 10).</summary>
    public const float AcceptSlack = 0.25f;
    /// <summary>Looking more than this many degrees off the seat's forward is looking back (Ruling 8).</summary>
    public const float BackCone = 100f;
    /// <summary>A companion answering a boarded wagon goes back to the cart once its wagon has been clear this long (Task 4).</summary>
    public const float AnswerBack = 5f;
    /// <summary>A wagon's rider is offered "Get out" only held, or rolling under this speed (m/s; Ruling 9).</summary>
    public const float GetOutBelow = 1f;
    /// <summary>
    /// After a wagon last offered a revive, its own rider is neither offered nor given "Get out" for this long (seconds, on each machine's
    /// clock): a "Revive" pressed on a screen behind the server lands within a round trip of the server's revive ending, and is refused out
    /// loud, never taken as a get-out (plan 15 Task 0, fix round 1, M1). A get-out is pressed when it was offered.
    /// </summary>
    public const float ReviveGetOutGrace = 1f;

    /// <summary>The way a look goes: +1 back past <see cref="BackCone"/>; else −1 (forward) from a wagon, 0 (no move) from the cart.</summary>
    public static int Direction(bool inCart, float lookYawDegrees) => Math.Abs(lookYawDegrees) > BackCone ? +1 : inCart ? 0 : -1;

    /// <summary>
    /// The nearest car past <paramref name="fromCar"/> in <paramref name="dir"/> whose hero place is free (<paramref name="placeFree"/>: [0] the
    /// cart has a free hero seat, [k] Wk's seat 0), passing taken ones; null when none is (or <paramref name="dir"/> is 0).
    /// </summary>
    public static int? Target(int fromCar, int dir, IReadOnlyList<bool> placeFree)
    {
        if (dir == 0) return null;
        dir = Math.Sign(dir);
        for (var car = fromCar + dir; car >= 0 && car < placeFree.Count; car += dir)
            if (placeFree[car]) return car;
        return null;
    }

    /// <summary>Moves are refused for everyone (Ruling 11): the train not rolling, a seated lever window open, or the train on the live bridge.</summary>
    public static bool Refused(bool rolling, bool leverWindow, bool bridgeHolds) => !rolling || leverWindow || bridgeHolds;

    /// <summary>A move is offered to a hero up and seated in the train, moves not refused, <see cref="MoveCooldown"/> after its last press (its own clock).</summary>
    public static bool Offered(bool up, bool inTrain, bool refused, double sincePress) => up && inTrain && !refused && sincePress >= MoveCooldown;

    /// <summary>
    /// The server (or single player) takes a press for <paramref name="target"/>: as <see cref="Offered"/>, its cooldown <see cref="AcceptSlack"/>
    /// shorter from the last press it took, and the target still the nearest free place that way on its own book (Ruling 10).
    /// </summary>
    public static bool Accepted(bool up, bool inTrain, bool refused, double sinceAccepted, int target, int? serverTarget)
        => up && inTrain && !refused && sinceAccepted >= MoveCooldown - AcceptSlack && serverTarget == target;

    /// <summary>A hop under way lands unless the hero went down or left the train, or the target was uncoupled meanwhile (Ruling 7).</summary>
    public static bool Lands(bool heroUpAndInTrain, bool targetCoupled) => heroUpAndInTrain && targetCoupled;

    /// <summary>A wagon's own rider may get out (offered and taken): held, or rolling under <see cref="GetOutBelow"/> (Ruling 9).</summary>
    public static bool WagonGetOut(bool rolling, float speed) => !rolling || speed < GetOutBelow;

    /// <summary>"Get out" may be offered and taken <paramref name="sinceRevive"/> seconds after the wagon last offered a revive (+∞: never did): <see cref="ReviveGetOutGrace"/> or more.</summary>
    public static bool GetOutAfterRevive(double sinceRevive) => !(sinceRevive < ReviveGetOutGrace);

    /// <summary>The move's prompt: "Climb back to wagon {to}", "Climb forward to wagon {to}" or "Climb forward into the cart".</summary>
    public static string Prompt(int fromCar, int toCar)
        => toCar > fromCar ? $"Climb back to wagon {toCar}" : toCar == 0 ? "Climb forward into the cart" : $"Climb forward to wagon {toCar}";

    /// <summary>The landing's log line: "{name} climbs back to wagon {to}.", "… forward to wagon {to}." or "… forward into the cart.".</summary>
    public static string Log(string name, int fromCar, int toCar)
        => toCar > fromCar ? $"{name} climbs back to wagon {toCar}." : toCar == 0 ? $"{name} climbs forward into the cart." : $"{name} climbs forward to wagon {toCar}.";

    /// <summary>The longest a hero needs to reach a wagon <paramref name="moves"/> moves away: the cooldowns between presses, the last hop and 2 s to act.</summary>
    public static float ThrowBound(int moves) => (moves - 1) * MoveCooldown + HopSeconds + 2f;

    /// <summary>
    /// The seat a companion answering a boarded wagon lands in there (Task 5, Task 4 review M2): its hero place when free; when a downed hero holds
    /// it, the first free crew bench seat (4, then 5), so a hero down in seat 0 never keeps the answer out; else none (it waits one car short). A
    /// player's move is unchanged: it lands only in a free hero place.
    /// </summary>
    public static int? AnswerSeat(bool heroPlaceFree, bool heroPlaceDowned, bool seat4Free, bool seat5Free)
        => heroPlaceFree ? TrainRules.HeroPlace : !heroPlaceDowned ? null : seat4Free ? TrainRules.CrewSeats[0] : seat5Free ? TrainRules.CrewSeats[1] : null;

    /// <summary>
    /// A companion's next move toward <paramref name="toCar"/> (Task 4): the target that way from <paramref name="fromCar"/>, never past
    /// <paramref name="toCar"/> (null while every free place short of it is taken: it waits rather than overshoot its wagon).
    /// </summary>
    public static int? AnswerStep(int fromCar, int toCar, IReadOnlyList<bool> placeFree)
    {
        var dir = Math.Sign(toCar - fromCar);
        return Target(fromCar, dir, placeFree) is int to && (to - toCar) * dir <= 0 ? to : null;
    }
}
