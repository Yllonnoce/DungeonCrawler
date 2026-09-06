using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class PlanResolverTests
{
    public static IEnumerable<object[]> Cases()
    {
        for (var floor = 1; floor <= 20; floor += 3)
            for (var s = 0; s < 4; s++)
                yield return new object[] { floor, floor * 131 + s * 7919 + 5 };
    }

    [Theory, MemberData(nameof(Cases))]
    public void Deterministic_Contiguous_And_InRoom(int floor, int seed)
    {
        var (layout, _, menu) = TestAdventure.Floor(floor, seed);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        var a = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, floor, seed);
        var b = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, floor, seed);

        Assert.Equal(a.Enemies, b.Enemies);
        Assert.Empty(a.Dropped);
        Assert.Equal(Enumerable.Range(0, a.EnemyCount), a.Enemies.Select(e => e.SpawnIndex));
        Assert.Equal(plan.EnemyTotal + layout.LedgeSpawns.Count + layout.CellarSpawns.Count, a.EnemyCount);
        Assert.Equal(a.Enemies.Select(e => e.Cell).Count(), a.Enemies.Select(e => e.Cell).Distinct().Count());

        foreach (var c in plan.Collectives)
        {
            var members = a.Enemies.Where(e => e.CollectiveId == c.Id).ToList();
            Assert.Equal(c.Members.Sum(m => m.Count), members.Count);
            Assert.All(members, m => Assert.True(layout.Rooms[c.Goal.Room].Contains(m.Cell)));
            Assert.Equal(Enumerable.Range(0, members.Count), members.Select(m => m.Ordinal));
            var cc = Assert.Single(a.Collectives, x => x.Id == c.Id);
            Assert.Equal(members.Select(m => m.SpawnIndex), cc.MemberSpawnIndices);
            if (c.Goal.Kind == GoalKind.Defend) Assert.NotNull(cc.ObjectiveCell);
            if (c.Goal.Kind == GoalKind.Patrol) Assert.Equal(c.Goal.Route.Count, cc.RouteCells.Count);
            if (c.Goal.RallyRoom != null) Assert.NotNull(cc.RallyCell);
        }
        foreach (var e in a.Enemies) Assert.True(layout.IsWalkable(e.Cell) || e.Under, $"{e.EnemyId} at {e.Cell} is not walkable");
        Assert.Equal(plan.Chests.Count, a.ExtraChests.Count);
        Assert.Equal(plan.Traps.Count, a.ExtraTraps.Count);
        Assert.All(a.ExtraChests, ch => Assert.True(ch.SpawnIndex >= FloorContents.ExtraIndexBase));
        Assert.Equal(layout.Rooms.Count, a.Rooms.Count);
        Assert.Equal(plan.Boss != null, a.BossId != null);
    }

    [Fact]
    public void LedgeAndCellarSpawns_MatchLegacy()
    {
        for (var seed = 1; seed < 40; seed++)
        {
            var (layout, _, menu) = TestAdventure.Floor(2, seed);
            if (layout.LedgeSpawns.Count + layout.CellarSpawns.Count == 0) continue;
            var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
            var resolved = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 2, seed);
            var legacy = LegacyPopulator.Populate(layout, TestAdventure.Crypt, TestAdventure.Roster, 2, seed);
            var fixedCells = layout.LedgeSpawns.Concat(layout.CellarSpawns).ToHashSet();
            var r = resolved.Enemies.Where(e => fixedCells.Contains(e.Cell)).Select(e => (e.Cell, e.EnemyId, e.Under)).ToList();
            var l = legacy.Enemies.Where(e => fixedCells.Contains(e.Cell)).Select(e => (e.Cell, e.EnemyId, e.Under)).ToList();
            Assert.Equal(l, r);
            return;
        }
    }
}
