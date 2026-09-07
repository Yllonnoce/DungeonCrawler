using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class ReinforcementTests
{
    private static readonly string[] Ids = { "enemy.ghoul", "enemy.skeleton" };

    [Fact]
    public void AmbushCount_ScalesWithDepth()
    {
        Assert.Equal(3, AmbushRules.Count(1));
        Assert.Equal(10, AmbushRules.Count(20));
        Assert.Equal(10, AmbushRules.Count(35));
        Assert.Equal(0, AmbushRules.Count(0));
        for (var f = 1; f < 20; f++) Assert.True(AmbushRules.Count(f) <= AmbushRules.Count(f + 1));
    }

    [Fact]
    public void AmbushPoints_SitInCorridors_BesideCover_SpacedOut_AwayFromStairs_AndRepeat()
    {
        var placed = 0;
        for (var seed = 1; seed <= 6; seed++)
        {
            var layout = FloorGenerator.Generate(FloorSpec.Default, seed);
            var points = AmbushRules.Place(layout, Ids, seed, 8);
            Assert.True(points.Count <= 8);
            placed += points.Count;
            foreach (var p in points)
            {
                Assert.Null(layout.RoomAt(p.Trigger));
                Assert.Equal(Cell.Floor, layout[p.Trigger]);
                Assert.False(layout.IsWater(p.Trigger));
                Assert.True(Math.Abs(p.Trigger.X - p.Cover.X) + Math.Abs(p.Trigger.Y - p.Cover.Y) == 1);
                Assert.True(p.FromRubble ? layout.Rubble.Contains(p.Cover) : !layout.IsWalkable(p.Cover));
                Assert.True(Math.Abs(p.Trigger.X - layout.UpStairs.X) + Math.Abs(p.Trigger.Y - layout.UpStairs.Y) >= AmbushRules.ClearOfStairs);
                Assert.Contains(p.EnemyId, Ids);
                foreach (var q in points) if (q != p) Assert.True(Math.Abs(p.Trigger.X - q.Trigger.X) + Math.Abs(p.Trigger.Y - q.Trigger.Y) >= AmbushRules.Spacing);
            }
            var again = AmbushRules.Place(layout, Ids, seed, 8);
            Assert.Equal(points, again);
        }
        Assert.True(placed >= 24, $"only {placed} points over six floors");
        Assert.Empty(AmbushRules.Place(FloorGenerator.Generate(FloorSpec.Default, 3), Array.Empty<string>(), 3, 5));
    }

    [Fact]
    public void PortalPacing_EscalatesAndScalesWithIntensity()
    {
        Assert.Equal(90f, PortalRules.Grace(1f));
        Assert.Equal(45f, PortalRules.Grace(2f));
        Assert.Equal(60f, PortalRules.Interval(0f, 1f));
        Assert.Equal(20f, PortalRules.Interval(PortalRules.RampSeconds, 1f));
        Assert.Equal(20f, PortalRules.Interval(PortalRules.RampSeconds * 3, 1f));
        Assert.Equal(30f, PortalRules.Interval(0f, 2f));
        for (var s = 0f; s < PortalRules.RampSeconds; s += 30f) Assert.True(PortalRules.Interval(s + 30f, 1f) <= PortalRules.Interval(s, 1f));
        var rng = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(PortalRules.GroupSize(0f, 1f, rng), 1, 2);
            Assert.InRange(PortalRules.GroupSize(PortalRules.RampSeconds, 1f, rng), 3, 4);
            Assert.InRange(PortalRules.GroupSize(0f, 2f, rng), 2, 3);
        }
        Assert.Equal(6, PortalRules.MaxAliveFor(1f));
        Assert.Equal(10, PortalRules.MaxAliveFor(2f));
        Assert.False(PortalRules.Enabled(0f));
        Assert.True(PortalRules.Enabled(0.25f));
        Assert.Equal(5, PortalRules.BossFloorFor(1));
        Assert.Equal(5, PortalRules.BossFloorFor(5));
        Assert.Equal(10, PortalRules.BossFloorFor(6));
        Assert.Equal(20, PortalRules.BossFloorFor(20));
        Assert.Equal(20, PortalRules.BossFloorFor(33));
    }

    [Fact]
    public void PortalSite_IsAnExploredRoomBehindTheParty_OrNothing()
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default, 5);
        var start = layout.RoomAt(layout.UpStairs)!;
        // the party has walked from the start room into the room farthest from the stairs, seeing both
        var far = layout.Rooms.Where(r => r != start && r != layout.BossArena).OrderByDescending(r => Math.Abs(r.Center.X - layout.UpStairs.X) + Math.Abs(r.Center.Y - layout.UpStairs.Y)).First();
        var seen = new HashSet<GridPos>(start.Cells().Concat(far.Cells()));
        var party = new List<GridPos> { far.Center };
        var rng = new Random(2);
        var site = PortalRules.ChooseSite(layout, seen, party, rng);
        Assert.NotNull(site);
        Assert.Equal(start, site!.Value.Room);
        Assert.True(layout.IsWalkable(site.Value.Cell) && start.Contains(site.Value.Cell));
        // nothing explored but the party's own room: no site
        Assert.Null(PortalRules.ChooseSite(layout, new HashSet<GridPos>(far.Cells()), party, rng));
        Assert.Null(PortalRules.ChooseSite(layout, seen, new List<GridPos>(), rng));
    }
}
