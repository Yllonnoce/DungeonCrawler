using DungeonCrawler.Core;

namespace DungeonCrawler.Adventure.Run;

/// <summary>The Daily Descent: everyone gets today's dungeon (the seed is the UTC date) and two fixed rank-1 clauses, never the Death Clause. Engine-free.</summary>
public static class DailyRules
{
    public const int ClauseCount = 2;

    /// <summary>The profile's key for a day, and the save's DailyDate.</summary>
    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The same on every machine and in every time zone: a function of the UTC date alone.</summary>
    public static ulong SeedFor(DateOnly d) => Rng.Mix(Rng.StableHash(Key(d)) ^ 0xDA11DA11UL);

    /// <summary>Two distinct rank-1 clauses from the contract (never the Death Clause), chosen from the day's seed and normalized.</summary>
    public static List<string> ClausesFor(DateOnly d)
    {
        var rng = new Random(unchecked((int)SeedFor(d)));
        var pool = ClauseRules.All.Where(c => c.Id != Clause.DeathClause).ToList();
        var picked = new List<string>();
        while (picked.Count < ClauseCount && pool.Count > 0)
        {
            var i = rng.Next(pool.Count);
            picked.Add(pool[i].Key);
            pool.RemoveAt(i);
        }
        return ClauseRules.Normalize(picked);
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
