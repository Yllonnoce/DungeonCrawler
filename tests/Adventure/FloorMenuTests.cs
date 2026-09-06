using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class FloorMenuTests
{
    [Fact]
    public void Rooms_DescribeLayout()
    {
        var (layout, spec, menu) = TestAdventure.Floor(3, 42);
        Assert.Equal(layout.Rooms.Count, menu.Rooms.Count);
        var start = Assert.Single(menu.Rooms, r => r.IsStart);
        Assert.Equal(0, start.HopsFromStart);
        Assert.Equal(RoomRoleOf(layout, layout.UpStairs), start.Index);
        Assert.Single(menu.Rooms, r => r.IsExit);
        Assert.All(menu.Rooms, r => Assert.True(r.HopsFromStart >= 0, "every room is reachable in the room graph"));
        Assert.All(menu.Rooms, r => Assert.Equal(r.Neighbours.Count, r.Degree));
        Assert.Equal(layout.Vaults.Count, menu.Rooms.Count(r => r.IsVault));
        Assert.Equal(spec.EnemyCount, menu.TargetEnemies);
        Assert.Contains("<", menu.Ascii);
        Assert.Equal(layout.Structures.Any(s => s.Kind == DungeonCrawler.Dungeon.Generation.StructureKind.Camp) ? 6 : 3, menu.Roster.Count); // bandits join the menu where there is a camp
        Assert.True(menu.Budget > 0);
    }

    [Fact]
    public void FreeCells_ExcludeStairsKeysAndProps()
    {
        var (layout, _, _) = TestAdventure.Floor(3, 42);
        foreach (var room in layout.Rooms)
        {
            var free = RoomCells.Free(layout, room);
            Assert.DoesNotContain(layout.UpStairs, free);
            Assert.DoesNotContain(layout.DownStairs, free);
            foreach (var c in layout.ChestSpawns) Assert.DoesNotContain(c, free);
            foreach (var k in layout.KeySpawns) Assert.DoesNotContain(k, free);
            foreach (var v in layout.Vaults) if (v.Key is { } k) Assert.DoesNotContain(k, free);
            Assert.All(free, p => Assert.Equal(Cell.Floor, layout[p]));
        }
    }

    [Fact]
    public void PartyPower_ScalesBudget()
    {
        Assert.Equal(1f, FloorMenu.PartyPower(PartySnapshot.Expected(5), 5));
        Assert.True(FloorMenu.PartyPower(new PartySnapshot(9, 8, 4), 5) > 1f);
        Assert.True(FloorMenu.PartyPower(new PartySnapshot(2, 1, 4), 5) < 1f);
        Assert.InRange(FloorMenu.PartyPower(new PartySnapshot(40, 40, 4), 1), 0.7f, 1.5f);
    }

    private static int RoomRoleOf(FloorLayout layout, GridPos p) => layout.Rooms.IndexOf(layout.RoomAt(p)!);
}
