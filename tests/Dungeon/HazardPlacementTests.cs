using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class HazardPlacementTests
{
    private static FloorLayout Gen(int seed, int floor, LayoutKind kind = LayoutKind.Standard, FloorObjectiveKind obj = FloorObjectiveKind.None)
        => FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = obj, Floor = floor, BossFloor = TierRules.IsBossFloor(floor) }, seed);

    [Fact]
    public void Floors1And2_HaveNoHazards()
    {
        for (var s = 1; s <= 40; s++) { Assert.Empty(Gen(s, 1).Hazards); Assert.Empty(Gen(s, 2).Hazards); }
    }

    [Fact]
    public void FloorsWithoutHazards_HashAsBefore()
    {
        for (var s = 1; s <= 20; s++)
            Assert.Equal(FloorGenerator.Generate(FloorSpec.Default with { Floor = 1, HazardsEnabled = false }, s).ContentHash(), Gen(s, 1).ContentHash());
        for (var s = 1; s <= 10; s++)
        {
            var off = FloorGenerator.Generate(FloorSpec.Default with { Floor = 9, HazardsEnabled = false }, s);
            Assert.Empty(off.Hazards);
            // with hazards disabled a deep floor is exactly the floor the generator made before this feature
            Assert.Equal(off.ContentHash(), FloorGenerator.Generate(FloorSpec.Default with { Floor = 9, HazardsEnabled = false }, s).ContentHash());
        }
    }

    [Theory] [InlineData(3)] [InlineData(8)] [InlineData(13)] [InlineData(18)]
    public void Counts_FollowTheTierTable(int floor)
    {
        var (press, _, bedsMax, _) = HazardCounts.For(floor);
        for (var s = 1; s <= 30; s++)
        {
            var l = Gen(s, floor);
            Assert.InRange(l.Hazards.Count(h => h.Kind == HazardKind.Press), 0, press);
            Assert.InRange(l.Hazards.Count(h => h.Kind == HazardKind.SpikeBed), 0, bedsMax);
        }
    }

    [Fact]
    public void Table_MatchesSpec()
    {
        Assert.Equal((0, 0, 0, false), HazardCounts.For(2));
        Assert.Equal((1, 1, 1, false), HazardCounts.For(3));
        Assert.Equal((1, 1, 2, true), HazardCounts.For(8));
        Assert.Equal((1, 2, 2, true), HazardCounts.For(13));
        Assert.Equal((1, 2, 3, true), HazardCounts.For(18));
        Assert.Equal((1, 2, 3, true), HazardCounts.For(27));
    }

    [Fact]
    public void SomeFloors_ActuallyGetHazards()
    {
        var presses = 0; var beds = 0;
        for (var s = 1; s <= 30; s++) { var l = Gen(s, 9); presses += l.Hazards.Count(h => h.Kind == HazardKind.Press); beds += l.Hazards.Count(h => h.Kind == HazardKind.SpikeBed); }
        Assert.True(presses >= 10, $"presses {presses}");
        Assert.True(beds >= 20, $"beds {beds}");
    }

    [Fact]
    public void AtMostOnePress_NeverOnLabyrinth()
    {
        for (var s = 1; s <= 30; s++)
        {
            Assert.True(Gen(s, 12).Hazards.Count(h => h.Kind == HazardKind.Press) <= 1);
            Assert.Empty(Gen(s, 12, LayoutKind.Labyrinth).Hazards.Where(h => h.Kind == HazardKind.Press));
        }
    }

    [Fact]
    public void Press_NeverOnDoorsOrNestMouths()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 9);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.Press))
            {
                Assert.Equal(3, h.Cells.Count);
                Assert.All(h.Cells, c => { Assert.Equal(Cell.Floor, l[c]); Assert.Null(l.RoomAt(c)); Assert.False(l.IsNestMouth(c)); Assert.False(l.IsWater(c)); });
                Assert.True(h.Cells.All(c => c.X == h.Cells[0].X) || h.Cells.All(c => c.Y == h.Cells[0].Y));
                Assert.Equal(h.Cells[1], h.Plate);
                // walls on both long sides so the slabs have something to be
                foreach (var c in h.Cells)
                {
                    var side = h.Horizontal ? new GridPos(0, 1) : new GridPos(1, 0);
                    Assert.Equal(Cell.Wall, l[c + side]);
                    Assert.Equal(Cell.Wall, l[c - side]);
                }
            }
        }
    }

    [Fact]
    public void Press_NeverSevers_StartFromStairs()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 9);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.Press))
                Assert.Contains(l.DownStairs, l.Reachable(l.UpStairs, c => h.Cells.Contains(c)));
        }
    }

    [Fact]
    public void PressureBeds_StayOffTheCriticalPath()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 14); var path = HazardPlanner.CriticalPath(l);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.SpikeBed && h.Variant == HazardVariant.Pressure))
                Assert.DoesNotContain(h.Cells, path.Contains);
        }
    }

    [Fact]
    public void SpikeBeds_NeverTouchRampsPitsOrLedges()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 8);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.SpikeBed)) foreach (var c in h.Cells)
            {
                Assert.Equal(Cell.Floor, l[c]);
                foreach (var n in c.Neighbours4())
                {
                    Assert.False(l.Ramps.ContainsKey(n));
                    Assert.NotEqual(Cell.Pit, l[n]);
                    Assert.NotEqual(Cell.Ramp, l[n]);
                    Assert.False(l.Ledges.Any(g => g.Cells.Contains(n)));
                }
            }
        }
    }

    [Fact]
    public void NoBeds_OnFloodedOrBurning_NoHazards_InTheArena()
    {
        for (var s = 1; s <= 30; s++)
        {
            Assert.Empty(Gen(s, 6, LayoutKind.Flooded).Hazards.Where(h => h.Kind == HazardKind.SpikeBed));
            Assert.Empty(Gen(s, 8, LayoutKind.Burning).Hazards.Where(h => h.Kind == HazardKind.SpikeBed));
            var boss = Gen(s, 10, LayoutKind.Ring);
            Assert.NotNull(boss.BossArena);
            Assert.All(boss.Hazards, h => Assert.All(h.Cells, c => Assert.False(boss.BossArena!.Contains(c))));
        }
    }

    [Fact]
    public void Hazards_AreReserved_AndAwayFromStairsAndSpawns()
    {
        for (var s = 1; s <= 30; s++)
        {
            var l = Gen(s, 15);
            foreach (var h in l.Hazards) foreach (var c in h.Cells)
            {
                Assert.True(l.IsReserved(c));
                Assert.True(c.ManhattanDistance(l.UpStairs) > 2 && c.ManhattanDistance(l.DownStairs) > 2);
                Assert.DoesNotContain(c, l.TrapSpawns);
                Assert.DoesNotContain(c, l.EnemySpawns);
                Assert.DoesNotContain(c, l.ChestSpawns);
                Assert.DoesNotContain(c, l.KeySpawns);
            }
            var cells = l.Hazards.SelectMany(h => h.Cells).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());
        }
    }

    [Fact]
    public void Indices_CountFromZero_PerFloor()
    {
        for (var s = 1; s <= 20; s++)
        {
            var l = Gen(s, 13);
            Assert.Equal(Enumerable.Range(0, l.Hazards.Count), l.Hazards.Select(h => h.Index));
        }
    }

    [Fact]
    public void Deterministic_PerSeed_AndHashed()
    {
        for (var s = 1; s <= 10; s++)
        {
            Assert.Equal(Gen(s, 13).ContentHash(), Gen(s, 13).ContentHash());
            var with = Gen(s, 13); if (with.Hazards.Count == 0) continue;
            Assert.NotEqual(with.ContentHash(), FloorGenerator.Generate(FloorSpec.Default with { Floor = 13, HazardsEnabled = false }, s).ContentHash());
        }
    }

    [Fact]
    public void MostHazards_GuardSomething()
    {
        var guarded = 0; var total = 0;
        for (var s = 1; s <= 60; s++) foreach (var h in Gen(s, 13).Hazards) { total++; if (h.GuardsCell != null) guarded++; }
        Assert.True(total > 0 && guarded >= total * 0.4, $"{guarded}/{total} guarded");
    }
}
