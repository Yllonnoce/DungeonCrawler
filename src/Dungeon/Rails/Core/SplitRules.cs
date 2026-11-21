namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's split (plan 12, Rulings 3-4): a party-line spur off the layout's points (<c>RailSplit.Points</c>, its cell centre on the party
/// track), <see cref="SpurLength"/> m long. The split's piece must stand within <see cref="AtSlack"/> of the points, and it is live only
/// with the spur's end over the crevasse (<see cref="Live"/>). Engine-free.
/// </summary>
public static class SplitRules
{
    /// <summary>The spur's length from its points: the lane blend's 6 m onto the slag line and 5 m more bending out over the crevasse.</summary>
    public const float SpurLength = 11f;
    /// <summary>The split piece's At must lie within this of the layout's points.</summary>
    public const float AtSlack = 8f;

    /// <summary>The spur's end, <paramref name="pointsS"/> + <see cref="SpurLength"/>, lies over the crevasse, a metre inside each edge. NaN is never live.</summary>
    public static bool Live(float pointsS, float crevasseFromS, float crevasseToS)
    {
        var end = pointsS + SpurLength;
        return end >= crevasseFromS + 1f && end <= crevasseToS - 1f; // NaN compares false
    }
}
