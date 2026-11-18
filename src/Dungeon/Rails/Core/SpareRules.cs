namespace DungeonCrawler.Dungeon.Rails;

/// <summary>Which stop's spare cart takes over from a wrecked one. Engine-free.</summary>
public static class SpareRules
{
    /// <summary>
    /// The intermediate stop (index 1 .. stops − 2) with an unused spare nearest to <paramref name="s"/> along the line,
    /// among the stop last left (<paramref name="stopIndex"/>), earlier ones and the very next one (never past a gate not
    /// yet crossed); a tie goes to the earlier stop. −1 when none qualifies. No allocation.
    /// </summary>
    public static int Pick(IReadOnlyList<float> stopS, IReadOnlySet<int> used, int stopIndex, float s)
    {
        var pick = -1;
        var bestD = float.MaxValue;
        var reach = Math.Min(stopS.Count - 2, stopIndex + 1);
        for (var i = 1; i <= reach; i++)
        {
            if (used.Contains(i)) continue;
            var d = Math.Abs(stopS[i] - s);
            if (d < bestD) { bestD = d; pick = i; }
        }
        return pick;
    }
}
