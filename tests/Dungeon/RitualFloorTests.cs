using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>A Ritual floor has a shrine with breaches, a lever for every breach inside it, and a ritualist beside the altar.</summary>
public class RitualFloorTests
{
    public static IEnumerable<object[]> KindsAndSeeds()
    {
        foreach (var kind in LayoutKinds.All.Where(k => FloorObjectives.Compatible(FloorObjectiveKind.Ritual, k)))
            for (var i = 0; i < 25; i++) yield return new object[] { kind, i * 7919 + 13 };
    }

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void Shrine_HasBreachesLeversAndAnAltar(LayoutKind kind, int seed)
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = FloorObjectiveKind.Ritual }, seed);
        if (layout.Objective == null) return; // no room fit a shrine: the floor gave the objective up honestly
        var o = layout.Objective;
        Assert.Equal(FloorObjectiveKind.Ritual, o.Kind);
        Assert.NotNull(o.ShrineRoom);
        Assert.NotNull(o.Altar);
        var shrine = o.ShrineRoom!;
        Assert.NotEqual(shrine, layout.RoomAt(layout.UpStairs));
        Assert.NotEqual(shrine, layout.RoomAt(layout.DownStairs));
        Assert.DoesNotContain(layout.Vaults, v => v.Room == shrine);
        Assert.True(layout.IsReserved(o.Altar!.Value));
        Assert.Contains(o.Altar.Value, layout.Reachable(layout.UpStairs));
        Assert.Null(layout.StructureAt(shrine));
        var nest = layout.NestAt(shrine);
        Assert.NotNull(nest);
        Assert.True(nest!.Mouths.Count >= 1);
        Assert.Equal(nest.Mouths.Count, o.BreachLevers.Count);
        foreach (var (lever, mouth) in o.BreachLevers)
        {
            Assert.Contains(mouth, nest.Mouths);
            Assert.True(shrine.Contains(lever));
            Assert.Equal(Cell.Floor, layout[lever]);
            Assert.True(layout.IsReserved(lever));
            Assert.DoesNotContain(lever, layout.EnemySpawns);
            Assert.NotEqual(o.Altar.Value, lever);
        }
        Assert.Equal(o.BreachLevers.Count, o.BreachLevers.Select(b => b.Lever).Distinct().Count());
        // the objective's people
        var contents = ObjectivePlanner.Resolve(layout, 4, seed)!;
        Assert.NotNull(contents.Ritualist);
        Assert.True(shrine.Contains(contents.Ritualist!.Cell));
        Assert.Equal(1, contents.Ritualist.Cell.ManhattanDistance(o.Altar.Value));
        Assert.Equal(contents.Ritualist.Name, ObjectivePlanner.Resolve(layout, 4, seed)!.Ritualist!.Name);
    }

    [Fact]
    public void MostFloors_GetAShrine()
    {
        var withShrine = 0;
        for (var seed = 1; seed <= 40; seed++) if (FloorGenerator.Generate(FloorSpec.Default with { Objective = FloorObjectiveKind.Ritual }, seed).Objective != null) withShrine++;
        Assert.True(withShrine >= 38, $"only {withShrine}/40 floors got a shrine");
    }
}
