using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class LegacyPopulatorTests
{
    public static readonly IReadOnlyDictionary<string, EnemyProfile> Roster = new Dictionary<string, EnemyProfile>
    {
        ["enemy.ghoul"] = new() { Id = "enemy.ghoul", Kind = EnemyKind.Melee },
        ["enemy.skeleton_archer"] = new() { Id = "enemy.skeleton_archer", Kind = EnemyKind.Ranged },
        ["enemy.wraith"] = new() { Id = "enemy.wraith", Kind = EnemyKind.Caster },
        ["enemy.crypt_lord"] = new() { Id = "enemy.crypt_lord", Kind = EnemyKind.Melee, IsBoss = true },
    };

    public static readonly TierProfile Crypt = new()
    {
        Id = "tier.crypt", DisplayName = "Crypt", Index = 0,
        EnemyPool = new[] { "enemy.ghoul", "enemy.skeleton_archer", "enemy.wraith" },
        EnemyWeights = new[] { 3f, 2f, 1f },
        BossId = "enemy.crypt_lord",
    };

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 50).Select(i => new object[] { i * 7919 + 13 });

    [Theory, MemberData(nameof(Seeds))]
    public void Deterministic_And_IndicesContiguous(int seed)
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default, seed);
        var a = LegacyPopulator.Populate(layout, Crypt, Roster, floor: 3, seed);
        var b = LegacyPopulator.Populate(layout, Crypt, Roster, floor: 3, seed);

        Assert.Equal(layout.TotalEnemySpawns, a.EnemyCount);
        Assert.Equal(a.Enemies, b.Enemies);
        Assert.Equal(Enumerable.Range(0, a.EnemyCount), a.Enemies.Select(e => e.SpawnIndex));
        Assert.Null(a.BossId);
    }

    [Fact]
    public void LedgesGetRanged_CellarsAreUnder()
    {
        var found = 0;
        for (var seed = 1; seed < 40 && found < 2; seed++)
        {
            var layout = FloorGenerator.Generate(FloorSpec.Default with { LedgeCount = 2, PitCount = 2 }, seed);
            if (layout.LedgeSpawns.Count == 0 || layout.CellarSpawns.Count == 0) continue;
            found++;
            var contents = LegacyPopulator.Populate(layout, Crypt, Roster, 2, seed);
            foreach (var cell in layout.LedgeSpawns)
            {
                var e = Assert.Single(contents.Enemies, e => e.Cell == cell && !e.Under);
                Assert.NotEqual(EnemyKind.Melee, Roster[e.EnemyId].Kind);
            }
            foreach (var cell in layout.CellarSpawns)
                Assert.Single(contents.Enemies, e => e.Cell == cell && e.Under);
        }
        Assert.True(found > 0, "no seed produced both a ledge and a cellar");
    }

    [Fact]
    public void BossFloor_PlacesBossInArena()
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default with { BossFloor = true, PitCount = 0 }, 77);
        var contents = LegacyPopulator.Populate(layout, Crypt, Roster, floor: 5, seed: 77);
        Assert.Equal("enemy.crypt_lord", contents.BossId);
        Assert.NotNull(layout.BossArena);
        Assert.True(layout.BossArena!.Contains(contents.BossCell!.Value));
    }

    [Fact]
    public void NoTier_FallsBackToGhoul()
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default, 5);
        var contents = LegacyPopulator.Populate(layout, null, Roster, 1, 5);
        Assert.All(contents.Enemies.Where(e => !layout.LedgeSpawns.Contains(e.Cell)), e => Assert.Equal(LegacyPopulator.FallbackEnemy, e.EnemyId));
    }
}
