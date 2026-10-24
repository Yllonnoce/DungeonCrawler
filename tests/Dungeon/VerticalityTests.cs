using DungeonCrawler.Dungeon.Generation;
using Xunit;
using Xunit.Abstractions;

namespace DungeonCrawler.Tests.Dungeon;

public class VerticalityTests
{
    private readonly ITestOutputHelper _out;
    public VerticalityTests(ITestOutputHelper o) => _out = o;

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 80).Select(i => new object[] { i * 7919 + 101 });
    private static readonly FloorSpec Spec = FloorSpec.Default with { VaultCount = 1, LedgeCount = 1, PitCount = 1, SecretCount = 1, HighRoomChance = 0.45f };

    [Fact]
    public void LedgeRoom_IsBuiltTall_SoTheGalleryOverlooksIt()
    {
        // a room with a ledge gets its ceiling two layers up, like the ledge and an upper room; everything else keeps one
        FloorLayout? l = null;
        for (var s = 1; s <= 60 && l == null; s++) { var c = FloorGenerator.Generate(Spec, s * 7919 + 101); if (c.Ledges.Count > 0) l = c; }
        Assert.NotNull(l);
        var ledge = l!.Ledges[0];
        Assert.True(l.IsTall(ledge.RampBottom));
        Assert.All(ledge.Room.Cells().Where(c => l[c] is Cell.Floor or Cell.Ramp), c => Assert.Equal(2, l.CeilingLayer(c)));
        Assert.All(ledge.Cells, c => Assert.Equal(2, l.CeilingLayer(c)));
        var plainLow = l.Rooms.First(r => r != ledge.Room && l.LevelOf(r.Center) == 0 && l.Ledges.All(g => g.Room != r));
        Assert.Equal(1, l.CeilingLayer(plainLow.Center));
        Assert.False(l.IsTall(plainLow.Center));
        var corridor = Enumerable.Range(0, l.Height).SelectMany(y => Enumerable.Range(0, l.Width).Select(x => new GridPos(x, y))).First(c => l[c] == Cell.Floor && l.RoomAt(c) == null && l.LevelOf(c) == 0);
        Assert.Equal(1, l.CeilingLayer(corridor));
        if (l.Rooms.FirstOrDefault(r => l.LevelOf(r.Center) == 1) is { } high) Assert.Equal(2, l.CeilingLayer(high.Center));
    }

    [Fact]
    public void Floor5_Ring_HasALedgeOnTheArenaWall()
    {
        // the Gallery: archers shoot down into the Crypt Lord's arena
        var with = 0;
        for (var s = 1; s <= 20; s++)
        {
            var l = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Ring, Floor = 5, BossFloor = true, LedgeCount = 1 }, s);
            if (l.Ledges.Any(g => g.Room == l.BossArena)) with++;
        }
        Assert.True(with >= 10, $"a ledge on the arena wall on {with} of 20 floors");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void EveryWalkableCell_ReachableRespectingHeights(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var reach = l.Reachable(l.UpStairs, null, secretsOpen: true);
        var walkable = 0;
        for (var y = 0; y < l.Height; y++)
            for (var x = 0; x < l.Width; x++)
            {
                var p = new GridPos(x, y);
                if (!l.IsWalkable(p)) continue;
                walkable++;
                Assert.True(reach.Contains(p), $"{p} ({l[p]}, level {l.LevelOf(p)}) unreachable\n{l.ToAscii()}");
            }
        Assert.True(walkable > 0);
        Assert.Contains(l.DownStairs, reach);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Ramps_ComeInPairsWithMatchingEnds(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        foreach (var (cell, ramp) in l.Ramps)
        {
            Assert.Equal(Cell.Ramp, l[cell]);
            var lowEnd = cell - ramp.Dir;
            var highEnd = cell + ramp.Dir;
            var lowH = ramp.LowHeight;
            var highH = ramp.HighHeight;
            var lowNeighbourH = l.Ramps.TryGetValue(lowEnd, out var lr) ? lr.HighHeight : l.LevelOf(lowEnd) * 4;
            var highNeighbourH = l.Ramps.TryGetValue(highEnd, out var hr) ? hr.LowHeight : l.LevelOf(highEnd) * 4;
            Assert.True(l.IsWalkable(lowEnd) && l.IsWalkable(highEnd), $"ramp {cell} has a dead end\n{l.ToAscii()}");
            Assert.Equal(lowH, lowNeighbourH);
            Assert.Equal(highH, highNeighbourH);
            // enemies may only enter along the axis
            Assert.False(l.CanStep(cell, cell + ramp.Dir.Perp));
        }
    }

    [Fact]
    public void Verticality_ActuallyHappens()
    {
        var layouts = Enumerable.Range(0, 60).Select(i => FloorGenerator.Generate(Spec, i * 31 + 3)).ToList();
        var withRamps = layouts.Count(l => l.Ramps.Count > 0);
        Assert.True(withRamps >= 50, $"only {withRamps}/60 floors have ramps");
        Assert.True(layouts.Count(l => l.Ledges.Count > 0) >= 15, $"ledges on {layouts.Count(l => l.Ledges.Count > 0)} floors");
        Assert.True(layouts.Count(l => l.Cellars.Count > 0) >= 10, $"cellars on {layouts.Count(l => l.Cellars.Count > 0)} floors");
        Assert.True(layouts.Count(l => l.Secrets.Count > 0) >= 40, $"secrets on {layouts.Count(l => l.Secrets.Count > 0)} floors");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Ledges_AreRaisedAndConnected(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        foreach (var ledge in l.Ledges)
        {
            foreach (var c in ledge.Cells) Assert.Equal(1, l.LevelOf(c));
            Assert.Equal(Cell.Ramp, l[ledge.RampBottom]);
            Assert.Equal(Cell.Ramp, l[ledge.RampTop]);
            var reach = l.Reachable(l.UpStairs);
            Assert.Contains(ledge.EnemySpawn, reach);
            // the drop from the ledge into the room is not a step
            var below = ledge.EnemySpawn + new GridPos(0, 1);
            Assert.False(l.CanStep(ledge.EnemySpawn, below));
            Assert.Contains(ledge.EnemySpawn, l.LedgeSpawns);
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Cellars_SitUnderHighRoomsAndTunnelOut(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        foreach (var cellar in l.Cellars)
        {
            Assert.Equal(Cell.Pit, l[cellar.Pit]);
            Assert.Equal(1, l.LevelOf(cellar.Pit));
            foreach (var c in cellar.Cells) { Assert.True(l.IsUnder(c)); Assert.Equal(1, l.LevelOf(c)); }
            foreach (var c in cellar.Exit) Assert.True(l.IsUnder(c));
            Assert.Equal(0, l.LevelOf(cellar.ExitTarget));
            Assert.Equal(Cell.Floor, l[cellar.ExitTarget]);
            // a path exists from the cellar (under layer) to the up stairs
            var path = GridPathfinder.FindPath(l, new PathNode(cellar.Chest, true), new PathNode(l.UpStairs, false));
            Assert.NotNull(path);
            Assert.Contains(path!, n => !n.Under && l.LevelOf(n.Cell) == 0); // climbs out onto the low level somewhere
            // enemies on the main layer never path through the pit
            Assert.DoesNotContain(l.EnemySpawns, e => e == cellar.Pit);
            var around = GridPathfinder.FindPath(l, cellar.Pit + new GridPos(-2, 0), cellar.Pit + new GridPos(2, 0));
            Assert.NotNull(around);
            Assert.DoesNotContain(cellar.Pit, around!);
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Secrets_HideAnAlcoveBehindAWall(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        foreach (var s in l.Secrets)
        {
            Assert.Equal(Cell.Secret, l[s.Wall]);
            Assert.Equal(Cell.Floor, l[s.Alcove]);
            Assert.Equal(l.LevelOf(s.Facing), l.LevelOf(s.Alcove));
            Assert.DoesNotContain(s.Alcove, l.Reachable(l.UpStairs));
            Assert.Contains(s.Alcove, l.Reachable(l.UpStairs, null, secretsOpen: true));
            Assert.Contains(s.Alcove, l.ChestSpawns);
            Assert.DoesNotContain(s.Alcove, l.TrapSpawns);
            Assert.NotNull(GridPathfinder.FindPath(l, l.UpStairs, s.Alcove, passable: p => p == s.Wall));
            Assert.Null(GridPathfinder.FindPath(l, l.UpStairs, s.Alcove));
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Spawns_NeverOnPitsRampsOrLedges(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var bad = l.EnemySpawns.Concat(l.ChestSpawns.Except(l.Ledges.Where(x => x.Chest != null).Select(x => x.Chest!.Value))).Concat(l.BreakableSpawns).Concat(l.TrapSpawns).Concat(l.KeySpawns)
            .Where(p => l[p] is Cell.Pit or Cell.Ramp || l.Ledges.Any(x => x.Cells.Contains(p) && p != x.Chest)).ToList();
        Assert.Empty(bad);
        foreach (var s in l.EnemySpawns.Concat(l.ChestSpawns).Concat(l.BreakableSpawns))
            Assert.True(l.IsWalkable(s) || l.Secrets.Any(x => x.Alcove == s), $"{s} is {l[s]}");
    }

    [Fact]
    public void FlatSpec_HasNoVerticality()
    {
        var l = FloorGenerator.Generate(Spec with { HighRoomChance = 0, LedgeCount = 0, PitCount = 0, SecretCount = 0 }, 5);
        Assert.Empty(l.Ramps);
        Assert.Empty(l.Cellars);
        Assert.True(l.Levels.All(b => b == 0));
    }

    [Fact]
    public void Ascii_Preview()
    {
        var l = Enumerable.Range(0, 100).Select(i => FloorGenerator.Generate(Spec, i * 31 + 3)).First(x => x.Cellars.Count > 0 && x.Ledges.Count > 0 && x.Secrets.Count > 0);
        _out.WriteLine($"seed {l.Seed}: ramps={l.Ramps.Count} ledges={l.Ledges.Count} cellars={l.Cellars.Count} secrets={l.Secrets.Count}");
        _out.WriteLine(l.ToAscii());
    }
}
