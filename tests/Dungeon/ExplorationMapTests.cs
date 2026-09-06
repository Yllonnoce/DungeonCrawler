using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class ExplorationMapTests
{
    private static FloorLayout Layout(int seed = 42) => FloorGenerator.Generate(FloorSpec.Default, seed);

    [Fact]
    public void Reveal_MarksStartRoomAndItsWalls()
    {
        var l = Layout();
        var map = new ExplorationMap();
        var revealed = map.Reveal(l, l.UpStairs);
        Assert.True(revealed > 0);

        var room = l.RoomAt(l.UpStairs)!;
        foreach (var c in room.Cells()) Assert.True(map.IsSeen(c), $"room cell {c} should be seen");
        // the wall ring around the room
        for (var x = room.X - 1; x <= room.Right + 1; x++)
        {
            Assert.True(map.IsSeen(new GridPos(x, room.Y - 1)));
            Assert.True(map.IsSeen(new GridPos(x, room.Bottom + 1)));
        }
    }

    [Fact]
    public void Reveal_IsBoundedByDepth()
    {
        var l = Layout();
        var map = new ExplorationMap { RevealDepth = 3 };
        map.Reveal(l, l.UpStairs);
        // walkable cells are within 3 steps; the wall ring adds up to 2 (diagonal neighbours)
        foreach (var c in map.Seen)
            Assert.True(c.ManhattanDistance(l.UpStairs) <= 3 + 2, $"{c} is beyond depth");
    }

    [Fact]
    public void Reveal_NeverMarksVoid()
    {
        var l = Layout();
        var map = new ExplorationMap();
        map.Reveal(l, l.UpStairs);
        map.Reveal(l, l.DownStairs);
        foreach (var c in map.Seen) Assert.NotEqual(Cell.Void, l[c]);
    }

    [Fact]
    public void Reveal_IsIdempotent()
    {
        var l = Layout();
        var map = new ExplorationMap();
        var first = map.Reveal(l, l.UpStairs);
        var second = map.Reveal(l, l.UpStairs);
        Assert.True(first > 0);
        Assert.Equal(0, second);
    }

    [Fact]
    public void Reveal_FromWallCell_DoesNothing()
    {
        var l = Layout();
        var map = new ExplorationMap();
        var wall = Enumerable.Range(0, l.Width * l.Height).Select(i => new GridPos(i % l.Width, i / l.Width)).First(p => l[p] == Cell.Wall);
        Assert.Equal(0, map.Reveal(l, wall));
        Assert.Empty(map.Seen);
    }

    [Fact]
    public void Load_RoundTrips()
    {
        var l = Layout();
        var map = new ExplorationMap();
        map.Reveal(l, l.UpStairs);
        var copy = new ExplorationMap();
        copy.Load(map.Seen);
        Assert.Equal(map.Seen.Count, copy.Seen.Count);
        Assert.All(map.Seen, c => Assert.True(copy.IsSeen(c)));
    }
}
