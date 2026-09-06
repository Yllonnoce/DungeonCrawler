namespace DungeonCrawler.Dungeon;

/// <summary>Plain-data view of a TierDef's enemy roster, usable by engine-free designers and tests.</summary>
public sealed record TierProfile
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int Index { get; init; }
    public IReadOnlyList<string> EnemyPool { get; init; } = Array.Empty<string>();
    public IReadOnlyList<float> EnemyWeights { get; init; } = Array.Empty<float>();
    public int BaseEnemyCount { get; init; } = 4;
    public string BossId { get; init; } = "";

    /// <summary>Weighted pick from the pool; empty string when the pool is empty.</summary>
    public string PickEnemy(Random rng)
    {
        if (EnemyPool.Count == 0) return "";
        var weights = EnemyPool.Select((_, i) => i < EnemyWeights.Count ? EnemyWeights[i] : 1f).ToArray();
        return EnemyPool[Items.LootRoller.WeightedIndex(weights, rng)];
    }
}

/// <summary>Plain view of a ThemeDef: enough for the designer and the planner to pick a look per floor.</summary>
public sealed record ThemeProfile(string Id, string DisplayName, int TierIndex, float Weight, string Description = "")
{
    /// <summary>Themes a floor of this tier may wear.</summary>
    public static IReadOnlyList<ThemeProfile> ForTier(IEnumerable<ThemeProfile> all, int tierIndex)
        => all.Where(t => t.TierIndex == tierIndex || t.TierIndex < 0).ToList();

    /// <summary>The seed's own pick for a floor: weighted, stable for a given run and floor.</summary>
    public static ThemeProfile? Pick(IEnumerable<ThemeProfile> all, int tierIndex, ulong gameSeed, int floor)
    {
        var options = ForTier(all, tierIndex);
        if (options.Count == 0) return null;
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0x9E3779B97F4A7C15UL))));
        return options[Items.LootRoller.WeightedIndex(options.Select(o => o.Weight).ToArray(), rng)];
    }
}
