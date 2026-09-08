using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;
using Xunit.Abstractions;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>Every floor shape keeps the invariants the ordinary one has, and each has the character it is named for.</summary>
public class LayoutKindTests
{
    private readonly ITestOutputHelper _out;
    public LayoutKindTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> KindsAndSeeds()
    {
        foreach (var kind in LayoutKinds.All)
            for (var i = 0; i < 30; i++) yield return new object[] { kind, i * 7919 + 13 };
    }

    private static FloorLayout Gen(LayoutKind kind, int seed, bool boss = false) => FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, BossFloor = boss }, seed);

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void Deterministic(LayoutKind kind, int seed)
    {
        var a = Gen(kind, seed);
        var b = Gen(kind, seed);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.Equal(kind, a.Kind);
    }

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void EverythingReachable_NothingTouchesVoid(LayoutKind kind, int seed)
    {
        var l = Gen(kind, seed);
        Assert.NotEqual(l.UpStairs, l.DownStairs);
        Assert.True(l.IsWalkable(l.UpStairs));
        Assert.True(l.IsWalkable(l.DownStairs));
        var reach = l.Reachable(l.UpStairs, null, secretsOpen: true);
        Assert.Contains(l.DownStairs, reach);
        foreach (var room in l.Rooms)
            if (l.IsWalkable(room.Center)) Assert.True(reach.Contains(room.Center), $"{kind} room centre {room.Center} unreachable\n{l.ToAscii()}");
        foreach (var s in l.EnemySpawns) Assert.True(reach.Contains(s), $"{kind} enemy spawn {s} unreachable");
        foreach (var s in l.ChestSpawns) Assert.True(reach.Contains(s) || l.Cellars.Any(c => c.Chest == s), $"{kind} chest {s} unreachable\n{l.ToAscii()}");
        var walkable = 0;
        for (var y = 0; y < l.Height; y++) for (var x = 0; x < l.Width; x++) if (l.IsWalkable(new GridPos(x, y), true)) walkable++;
        Assert.Equal(walkable, reach.Count);
        for (var y = 0; y < l.Height; y++)
            for (var x = 0; x < l.Width; x++)
            {
                if (l[x, y] != Cell.Floor) continue;
                for (var oy = -1; oy <= 1; oy++)
                    for (var ox = -1; ox <= 1; ox++)
                        Assert.NotEqual(Cell.Void, l[x + ox, y + oy]);
            }
        for (var i = 0; i < l.Rooms.Count; i++)
            for (var j = i + 1; j < l.Rooms.Count; j++)
                Assert.False(l.Rooms[i].Intersects(l.Rooms[j], 0), $"{kind} rooms {i} and {j} overlap");
        var all = l.EnemySpawns.Concat(l.ChestSpawns).Concat(l.BreakableSpawns).Concat(l.TrapSpawns).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        foreach (var s in all) Assert.False(l.Fires.Contains(s) || l.Rubble.Contains(s), $"{kind} spawn {s} on a hazard");
        Assert.Equal(FloorSpec.Default.EnemyCount, l.EnemySpawns.Count);
    }

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void BossFloor_ArenaHoldsTheExit(LayoutKind kind, int seed)
    {
        var l = Gen(kind, seed, boss: true);
        Assert.NotNull(l.BossArena);
        Assert.True(l.BossArena!.Contains(l.DownStairs));
        Assert.False(l.BossArena.Contains(l.UpStairs));
        Assert.Contains(l.DownStairs, l.Reachable(l.UpStairs));
        Assert.DoesNotContain(l.Fires, f => l.BossArena.Contains(f));
        Assert.DoesNotContain(l.Water, w => w.Room == l.BossArena);
    }

    [Fact]
    public void Labyrinth_IsMostlyPassage_AndFlat()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var l = Gen(LayoutKind.Labyrinth, seed);
            int corridor = 0, room = 0;
            for (var y = 0; y < l.Height; y++) for (var x = 0; x < l.Width; x++)
            {
                var p = new GridPos(x, y);
                if (l[p] != Cell.Floor) continue;
                if (l.RoomAt(p) == null) corridor++; else room++;
            }
            Assert.True(corridor > room, $"seed {seed}: a labyrinth is mostly passage ({corridor} passage vs {room} room cells)");
            Assert.Empty(l.Ramps);
            Assert.All(l.Rooms, r => Assert.True(r.W <= 5 && r.H <= 5 || r == l.BossArena));
            Assert.NotEmpty(l.Connections);
        }
    }

    [Fact]
    public void Ring_HasARotundaInTheMiddle_OnTheRing()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var l = Gen(LayoutKind.Ring, seed);
            var centre = new GridPos(l.Width / 2, l.Height / 2);
            var hub = l.Rooms[0];
            Assert.True(hub.Contains(centre), $"seed {seed}: the rotunda sits on the centre");
            Assert.True(hub.W >= 9);
            Assert.True(l.Rooms.Count >= 5);
            // every ring room joins its neighbour, and the rotunda has spokes
            Assert.True(l.Connections.Count(c => c.A == 0 || c.B == 0) >= 2, $"seed {seed}: spokes");
            Assert.True(l.Connections.Count(c => c.A != 0 && c.B != 0) >= l.Rooms.Count - 2, $"seed {seed}: ring corridor");
            var boss = Gen(LayoutKind.Ring, seed, boss: true);
            Assert.Same(boss.Rooms[0], boss.BossArena);
        }
    }

    [Fact]
    public void Mine_HasGalleries_AndDeadEndDrifts()
    {
        var driftsSeen = 0;
        for (var seed = 1; seed <= 20; seed++)
        {
            var l = Gen(LayoutKind.Mine, seed);
            Assert.True(l.Rooms.Count(r => Math.Min(r.W, r.H) <= 3 && Math.Max(r.W, r.H) >= 7) >= 3, $"seed {seed}: galleries");
            // a drift: a corridor cell with a single walkable neighbour
            for (var y = 1; y < l.Height - 1; y++) for (var x = 1; x < l.Width - 1; x++)
            {
                var p = new GridPos(x, y);
                if (l[p] != Cell.Floor || l.RoomAt(p) != null) continue;
                var open = 0;
                foreach (var d in new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) }) if (l.IsWalkable(p + d)) open++;
                if (open == 1) driftsSeen++;
            }
            Assert.True(l.Rubble.Count >= 3, $"seed {seed}: spoil heaps ({l.Rubble.Count})");
        }
        Assert.True(driftsSeen >= 40, $"drifts across twenty mines: {driftsSeen}");
    }

    [Fact]
    public void Flooded_PutsMostRoomsUnderWater()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var l = Gen(LayoutKind.Flooded, seed);
            var floods = l.Water.Count(w => w.Kind == WaterKind.Flood);
            Assert.True(floods >= 3, $"seed {seed}: only {floods} flooded rooms\n{l.ToAscii()}");
            foreach (var w in l.Water.Where(w => w.Kind == WaterKind.Flood))
            {
                Assert.All(w.Cells, c => Assert.Equal(WaterKind.Flood, l.WaterAt(c)));
                Assert.NotEqual(l.RoomAt(l.UpStairs), w.Room);
                Assert.NotEqual(l.RoomAt(l.DownStairs), w.Room);
            }
            foreach (var c in l.ChestSpawns.Concat(l.BreakableSpawns).Concat(l.KeySpawns)) Assert.False(l.IsWater(c) && !l.Vaults.Any(v => v.Room.Contains(c)), $"seed {seed}: floor item in water at {c}");
        }
    }

    [Fact]
    public void Burning_HasSpreadOutFires_NoWater_RoomsStillCrossable()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var l = Gen(LayoutKind.Burning, seed);
            Assert.True(l.Fires.Count >= 8, $"seed {seed}: only {l.Fires.Count} fires");
            Assert.Empty(l.Water);
            var fires = l.Fires.ToList();
            for (var i = 0; i < fires.Count; i++) for (var j = i + 1; j < fires.Count; j++) Assert.True(fires[i].ManhattanDistance(fires[j]) >= 3);
            Assert.All(fires, f => Assert.NotNull(l.RoomAt(f)));
            Assert.DoesNotContain(fires, f => l.RoomAt(l.UpStairs)!.Contains(f));
            // walking around the fires: every room centre and the exit still reachable with fire cells treated as walls
            var reach = l.Reachable(l.UpStairs, p => l.Fires.Contains(p), secretsOpen: true);
            Assert.Contains(l.DownStairs, reach);
            foreach (var s in l.EnemySpawns) Assert.True(reach.Contains(s), $"seed {seed}: enemy at {s} boxed in by fire\n{l.ToAscii()}");
        }
    }

    [Fact]
    public void Deal_CoversEveryKind_NoRepeats_Floor1Ordinary()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var deal = LayoutKinds.Deal(seed * 7919);
            Assert.Equal(TierRules.FinalFloor, deal.Length);
            Assert.Equal(LayoutKind.Standard, deal[0]);
            foreach (var kind in LayoutKinds.All) Assert.Contains(kind, deal);
            for (var i = 1; i < deal.Length; i++) Assert.NotEqual(deal[i - 1], deal[i]);
            for (var f = 1; f <= TierRules.FinalFloor; f++)
            {
                Assert.Equal(deal[f - 1], LayoutKinds.Pick(seed * 7919, f));
                if (TierRules.IsBossFloor(f)) Assert.Contains(deal[f - 1], LayoutKinds.BossKinds);
            }
            Assert.Contains(LayoutKinds.Pick(seed * 7919, 25), LayoutKinds.All);
        }
        Assert.Equal(LayoutKinds.Deal(42), LayoutKinds.Deal(42));
    }

    [Fact]
    public void Ids_RoundTrip()
    {
        foreach (var kind in LayoutKinds.All)
        {
            Assert.True(LayoutKinds.TryParse(LayoutKinds.Id(kind), out var back));
            Assert.Equal(kind, back);
        }
        Assert.False(LayoutKinds.TryParse("castle", out _));
        Assert.False(LayoutKinds.TryParse(null, out _));
    }

    [Fact]
    public void StandardShape_Unchanged()
    {
        // the ordinary floor must hash exactly as before the shapes were added, so shipped plans still fit
        var a = FloorGenerator.Generate(FloorSpec.Default, 42);
        var b = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Standard }, 42);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.Empty(a.Fires);
    }

    [Fact]
    public void Ascii_Previews()
    {
        foreach (var kind in LayoutKinds.All)
        {
            _out.WriteLine($"==== {kind}");
            _out.WriteLine(Gen(kind, 42).ToAscii());
        }
    }
}
