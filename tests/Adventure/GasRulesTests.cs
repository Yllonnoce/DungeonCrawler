using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>The gas rises low first, from the start outward, high ground last, the same every time.</summary>
public class GasRulesTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 40).Select(i => new object[] { i * 7919 + 13 });
    private static FloorLayout Toxic(int seed, LayoutKind kind = LayoutKind.Standard) => FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = FloorObjectiveKind.Toxic }, seed);

    [Theory, MemberData(nameof(Seeds))]
    public void Arrivals_LowBeforeHigh_StartFirst_Deterministic(int seed)
    {
        var layout = Toxic(seed);
        var a = GasRules.Arrivals(layout);
        var b = GasRules.Arrivals(layout);
        Assert.Equal(a, b);
        var start = layout.Rooms.IndexOf(layout.RoomAt(layout.UpStairs)!);
        var exit = layout.Rooms.IndexOf(layout.RoomAt(layout.DownStairs)!);
        if (layout.LevelOf(layout.Rooms[start].Center) == 0) Assert.Equal(GasRules.Grace, a[start]);
        var lowMax = a.Where((t, i) => layout.LevelOf(layout.Rooms[i].Center) == 0).Max();
        for (var i = 0; i < a.Length; i++)
        {
            if (layout.LevelOf(layout.Rooms[i].Center) == 0) Assert.True(a[i] >= GasRules.Grace);
            else Assert.True(a[i] > lowMax, $"room {i} is high but gasses before the last low room");
        }
        Assert.True(a[exit] >= a.Where((t, i) => layout.LevelOf(layout.Rooms[i].Center) == 0).Min());
        // everything fills in the end
        var full = GasRules.FullAt(a);
        for (var i = 0; i < a.Length; i++) Assert.Equal(1f, GasRules.Level(a[i], full));
    }

    [Fact]
    public void Level_IsMonotoneInTime_AndDangerBands()
    {
        var last = 0f;
        for (var t = 0f; t < 200f; t += 5f)
        {
            var level = GasRules.Level(60f, t);
            Assert.True(level >= last);
            last = level;
        }
        Assert.Equal(0, GasRules.Danger(0f));
        Assert.Equal(1, GasRules.Danger(0.2f));
        Assert.Equal(2, GasRules.Danger(0.5f));
        Assert.Equal(3, GasRules.Danger(1f));
        Assert.Equal(0f, GasRules.Level(GasRules.Unreachable, 9999f));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Field_CoversEveryWalkableCell(int seed)
    {
        var layout = Toxic(seed);
        var field = GasField.Build(layout);
        var reach = layout.Reachable(layout.UpStairs);
        foreach (var p in reach)
        {
            if (layout.IsUnder(p)) continue;
            Assert.False(float.IsInfinity(field.ArrivalAt(p)), $"cell {p} never gasses");
        }
        Assert.True(float.IsInfinity(field.ArrivalAt(new GridPos(-1, -1))));
        // a room cell carries its room's time
        var start = layout.RoomAt(layout.UpStairs)!;
        Assert.Equal(field.RoomArrivals[layout.Rooms.IndexOf(start)], field.ArrivalAt(start.Center));
    }

    [Fact]
    public void Tick_ScalesWithDangerAndLethality()
    {
        var saved = TierRules.Lethality;
        try
        {
            TierRules.Lethality = 1f;
            Assert.Empty(GasRules.Tick(3, 0).OnHit);
            var thin = GasRules.Tick(3, 1);
            var lethal = GasRules.Tick(3, 3);
            Assert.True(lethal.MaxDamage > thin.MaxDamage);
            Assert.Equal(DungeonCrawler.Combat.StatusKind.Poison, lethal.OnHit[0].Kind);
            TierRules.Lethality = 3f;
            Assert.Equal(thin.MaxDamage * 3f, GasRules.Tick(3, 1).MaxDamage, 3);
        }
        finally { TierRules.Lethality = saved; }
    }
}
