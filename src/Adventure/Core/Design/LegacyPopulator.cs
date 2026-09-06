using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Reproduces the pre-campaign enemy placement exactly: weighted random picks over the generator's flat spawn lists,
/// ranged enemies on ledges, ambushers in cellars, boss in the arena. Used for saves that predate floor plans and as
/// the last-resort fallback, so its output must never change for a given layout and seed.
/// </summary>
public static class LegacyPopulator
{
    public const string FallbackEnemy = "enemy.ghoul";
    public const string FallbackRanged = "enemy.skeleton_archer";

    public static FloorContents Populate(FloorLayout layout, TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, int floor, int seed)
    {
        var contents = new FloorContents();
        var rng = new Random(seed ^ 0x5EED);
        var index = 0;
        foreach (var cell in layout.EnemySpawns)
            contents.Enemies.Add(new EnemyPlacement(index++, tier?.PickEnemy(rng) ?? FallbackEnemy, cell, Under: false));
        foreach (var cell in layout.LedgeSpawns)
            contents.Enemies.Add(new EnemyPlacement(index++, PickRanged(tier, roster, rng), cell, Under: false));
        foreach (var cell in layout.CellarSpawns)
            contents.Enemies.Add(new EnemyPlacement(index++, tier?.PickEnemy(rng) ?? FallbackEnemy, cell, Under: true));
        if (TierRules.IsBossFloor(floor) && layout.BossArena is { } arena && tier != null && tier.BossId != "")
        {
            contents.BossId = tier.BossId;
            contents.BossCell = arena.Center;
        }
        return contents;
    }

    public static string PickRanged(TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, Random rng)
    {
        var ranged = tier?.EnemyPool.Where(id => !roster.TryGetValue(id, out var p) || p.Kind != EnemyKind.Melee).ToList();
        if (ranged == null || ranged.Count == 0) return tier?.PickEnemy(rng) ?? FallbackRanged;
        return ranged[rng.Next(ranged.Count)];
    }
}
