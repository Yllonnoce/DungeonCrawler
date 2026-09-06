using DungeonCrawler.Dungeon.Generation;
using Xunit;
using Xunit.Abstractions;

namespace DungeonCrawler.Tests.Dungeon;

public class FloorGeneratorTests
{
    private readonly ITestOutputHelper _out;
    public FloorGeneratorTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 50).Select(i => new object[] { i * 7919 + 13 });

    [Theory, MemberData(nameof(Seeds))]
    public void SameSeed_SameLayout(int seed)
    {
        var a = FloorGenerator.Generate(FloorSpec.Default, seed);
        var b = FloorGenerator.Generate(FloorSpec.Default, seed);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.Equal(a.Cells, b.Cells);
        Assert.Equal(a.UpStairs, b.UpStairs);
        Assert.Equal(a.DownStairs, b.DownStairs);
    }

    [Fact]
    public void DifferentSeeds_DifferentLayouts()
    {
        var hashes = Enumerable.Range(1, 20).Select(s => FloorGenerator.Generate(FloorSpec.Default, s).ContentHash()).ToHashSet();
        Assert.True(hashes.Count >= 19, "seeds should essentially never collide");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void RoomCount_WithinSpec(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        Assert.InRange(l.Rooms.Count, 2, FloorSpec.Default.MaxRooms + FloorSpec.Default.VaultCount); // carved side rooms may add vaults
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Rooms_DoNotOverlap(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        for (var i = 0; i < l.Rooms.Count; i++)
            for (var j = i + 1; j < l.Rooms.Count; j++)
                Assert.False(l.Rooms[i].Intersects(l.Rooms[j], 0), $"rooms {i} and {j} overlap");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Stairs_AreDistinctWalkableCells(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        Assert.NotEqual(l.UpStairs, l.DownStairs);
        Assert.True(l.IsWalkable(l.UpStairs));
        Assert.True(l.IsWalkable(l.DownStairs));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void EveryWalkableCell_ReachableFromUpStairs(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        var reach = l.Reachable(l.UpStairs, null, secretsOpen: true); // alcoves count once their wall is open
        Assert.Contains(l.DownStairs, reach);
        foreach (var room in l.Rooms)
            if (l.IsWalkable(room.Center)) Assert.True(reach.Contains(room.Center), $"room centre {room.Center} ({l[room.Center]}) unreachable\n{l.ToAscii()}");
        foreach (var s in l.EnemySpawns) Assert.True(reach.Contains(s), $"enemy spawn {s} unreachable");
        foreach (var s in l.ChestSpawns) Assert.True(reach.Contains(s) || l.Cellars.Any(c => c.Chest == s), $"chest {s} ({l[s]}) unreachable\n{l.ToAscii()}");
        foreach (var s in l.BreakableSpawns) Assert.True(reach.Contains(s), $"barrel {s} unreachable");
        var walkable = 0;
        for (var y = 0; y < l.Height; y++) for (var x = 0; x < l.Width; x++) if (l.IsWalkable(new GridPos(x, y), true)) walkable++;
        Assert.Equal(walkable, reach.Count);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void NoFloorCell_TouchesVoid(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        for (var y = 0; y < l.Height; y++)
            for (var x = 0; x < l.Width; x++)
            {
                if (l[x, y] != Cell.Floor) continue;
                for (var oy = -1; oy <= 1; oy++)
                    for (var ox = -1; ox <= 1; ox++)
                        Assert.NotEqual(Cell.Void, l[x + ox, y + oy]);
            }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Spawns_AreUniqueAndNotInStartRoom(int seed)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default, seed);
        var all = l.EnemySpawns.Concat(l.ChestSpawns).Concat(l.BreakableSpawns).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        var startRoom = l.RoomAt(l.UpStairs);
        Assert.NotNull(startRoom);
        foreach (var s in all) Assert.False(startRoom!.Contains(s), $"spawn {s} inside start room");
        Assert.Equal(FloorSpec.Default.EnemyCount, l.EnemySpawns.Count);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void BossFloor_ArenaContainsDownStairs(int seed)
    {
        var spec = FloorSpec.Default with { BossFloor = true };
        var l = FloorGenerator.Generate(spec, seed);
        Assert.NotNull(l.BossArena);
        Assert.True(l.BossArena!.Contains(l.DownStairs));
        Assert.False(l.BossArena.Contains(l.UpStairs));
        Assert.Equal(spec.BossArenaSize, l.BossArena.W);
        Assert.Contains(l.DownStairs, l.Reachable(l.UpStairs));
    }

    [Fact]
    public void Ascii_Preview()
    {
        _out.WriteLine(FloorGenerator.Generate(FloorSpec.Default, 42).ToAscii());
    }
}
