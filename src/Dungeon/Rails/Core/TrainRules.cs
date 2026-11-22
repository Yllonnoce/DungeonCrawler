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
}
