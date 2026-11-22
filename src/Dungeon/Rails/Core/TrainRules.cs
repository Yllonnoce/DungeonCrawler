namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party's train on a railed jailbreak floor (plan 14): the open wagons W1..Wn coupled behind the party's cart from the landing,
/// one per cage placed (four on floor 12), numbered nearest the cart first. Each has one hero place (seat 0) and two crew seats (the
/// bench, 4 and 5). Loss is all or nothing (only a fall uncouples). Engine-free.
/// </summary>
public static class TrainRules
{
    /// <summary>From one body's middle to the next along the train: a cart's length and a coupling's gap (2.6 m).</summary>
    public const float Spacing = 2f * CartRules.HalfLength + 0.4f;
    /// <summary>A wagon's hero place: its seat 0, at the cart's seat-0 spot.</summary>
    public const int HeroPlace = 0;
    /// <summary>A wagon's crew seats, in the order they fill: the bench's 4 then 5.</summary>
    public static readonly int[] CrewSeats = { 4, 5 };

    /// <summary>The wagons the train is built with: one per cage placed, none without a cage.</summary>
    public static int WagonsFor(int cages) => Math.Max(0, cages);

    /// <summary>The whole train's length, the cart's front to the last wagon's back: 12.6 m with four wagons.</summary>
    public static float Length(int wagons) => 2f * CartRules.HalfLength + wagons * Spacing;

    /// <summary>The crew seats of a train of <paramref name="wagons"/> in the order they fill: W1 seat 4, W1 seat 5, W2 seat 4, and so on.</summary>
    public static IReadOnlyList<(int Wagon, int Seat)> SeatOrder(int wagons)
    {
        var order = new List<(int Wagon, int Seat)>(Math.Max(0, wagons) * CrewSeats.Length);
        for (var k = 1; k <= wagons; k++)
            foreach (var seat in CrewSeats) order.Add((k, seat));
        return order;
    }

    /// <summary>The 1-based numbers of the wagons not coupled (<paramref name="coupled"/> by number from W1), or null when every one is.</summary>
    public static List<int>? Lost(IReadOnlyList<bool> coupled)
    {
        List<int>? lost = null;
        for (var i = 0; i < coupled.Count; i++)
            if (!coupled[i]) (lost ??= new List<int>()).Add(i + 1);
        return lost;
    }

    // ---- the crew running to the train (Task 2, Ruling 5 and 6) -----------------------------------------------------

    /// <summary>The countdown waits this long at most (from its start) for crew on the way to their seats (8 s).</summary>
    public const float BoardMax = 8f;
    /// <summary>A freed crew member this near the train (its seat's spot beside the lane) books a seat; one farther stays on foot.</summary>
    public const float BoardReach = 25f;
    /// <summary>A walker this near its seat's spot sits in (the hop in, as a companion's).</summary>
    public const float HopReach = 3f;

    /// <summary>Crew board while the train is held at a stop short of the last, not halted on the line.</summary>
    public static bool BoardsHere(bool held, bool halted, bool lastStop) => held && !halted && !lastStop;

    /// <summary>The countdown's departure waits: crew are on the way and it has not run <see cref="BoardMax"/> yet.</summary>
    public static bool HoldsDeparture(int onTheWay, float boardingElapsed) => onTheWay > 0 && boardingElapsed < BoardMax;

    /// <summary>At <see cref="BoardMax"/> a walker still within <see cref="BoardReach"/> sits in at once, as a companion hops in.</summary>
    public static bool SeatsAtOnce(float boardingElapsed, float distance) => boardingElapsed >= BoardMax && distance <= BoardReach;

    /// <summary>A booking stands while the train boards here, the walker is up, and it is within <see cref="BoardReach"/>.</summary>
    public static bool KeepsBooking(bool boardsHere, bool up, float distance) => boardsHere && up && distance <= BoardReach;

    /// <summary>
    /// Where a crew member saved in <paramref name="saved"/> sits again (a load, a wipe's rise): that seat when the train has it and it is
    /// <paramref name="free"/>, else the first free one in <see cref="SeatOrder"/>, else null (the train is full, or there is none).
    /// </summary>
    public static (int Wagon, int Seat)? Reseat((int Wagon, int Seat) saved, Func<int, int, bool> free, int wagons)
    {
        var order = SeatOrder(wagons);
        foreach (var p in order)
            if (p == saved && free(p.Wagon, p.Seat)) return p;
        foreach (var p in order)
            if (free(p.Wagon, p.Seat)) return p;
        return null;
    }

    /// <summary>
    /// The order a boarding hound tries the train's bodies in (Task 1 review, I1): <paramref name="bodies"/> by index, 0 the party's cart (its
    /// crew count ignored) and k the coupled wagon Wk, each with the crew seated in it and its distance. Crewed wagons first, the most crew
    /// first and the nearer on a tie; then the cart; then the empty wagons, nearest first.
    /// </summary>
    public static List<int> BoardingOrder(IReadOnlyList<(int Crew, float Distance)> bodies)
    {
        var order = new List<int>(bodies.Count);
        for (var i = 1; i < bodies.Count; i++) if (bodies[i].Crew > 0) order.Add(i);
        order.Sort((a, b) => bodies[a].Crew != bodies[b].Crew ? bodies[b].Crew.CompareTo(bodies[a].Crew) : bodies[a].Distance.CompareTo(bodies[b].Distance));
        if (bodies.Count > 0) order.Add(0);
        var empty = new List<int>();
        for (var i = 1; i < bodies.Count; i++) if (bodies[i].Crew <= 0) empty.Add(i);
        empty.Sort((a, b) => bodies[a].Distance.CompareTo(bodies[b].Distance));
        order.AddRange(empty);
        return order;
    }
}
