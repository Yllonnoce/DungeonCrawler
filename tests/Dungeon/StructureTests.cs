using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class StructureTests
{
    private static readonly FloorSpec Spec = FloorSpec.Default;
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 40).Select(i => new object[] { i * 977 + 5 });

    [Fact]
    public void BigRooms_LoseTheirCorners()
    {
        var layouts = Enumerable.Range(0, 30).Select(i => FloorGenerator.Generate(Spec, i * 41 + 7)).ToList();
        var chamfered = 0;
        foreach (var l in layouts)
            foreach (var room in l.Rooms)
            {
                var depth = FloorGenerator.ChamferDepth(room);
                if (depth == 0) { Assert.Equal(Cell.Floor, l[room.X, room.Y]); continue; }
                // a corner is wall unless a corridor or ledge claimed it; the cell just inside is always floor
                if (l[room.X, room.Y] == Cell.Wall) chamfered++;
                Assert.NotEqual(Cell.Void, l[room.X, room.Y]);
                Assert.Equal(Cell.Floor, l[room.X + depth, room.Y + depth]);
            }
        Assert.True(chamfered >= 30, $"only {chamfered} chamfered corners across 30 floors");
        // the builder's 45° rule fires on real corners
        var pieces = layouts.Sum(l => Enumerable.Range(0, l.Width * l.Height).Count(i => l.IsChamfer(new GridPos(i % l.Width, i / l.Width), out _)));
        Assert.True(pieces >= 60, $"only {pieces} chamfer pieces");
    }

    [Fact]
    public void Corridors_Jog()
    {
        // a staggered leg leaves corridor cells whose neighbours form a Z: floor on one side and the diagonal, nothing straight through
        var layouts = Enumerable.Range(0, 30).Select(i => FloorGenerator.Generate(Spec, i * 53 + 11)).ToList();
        var bends = 0;
        foreach (var l in layouts)
            for (var y = 1; y < l.Height - 1; y++)
                for (var x = 1; x < l.Width - 1; x++)
                {
                    var p = new GridPos(x, y);
                    if (l[p] != Cell.Floor || l.RoomAt(p) != null) continue;
                    var walk = new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) }.Where(d => l.IsWalkable(p + d)).ToList();
                    if (walk.Count == 2 && walk[0].X != -walk[1].X) bends++;
                }
        Assert.True(bends >= 30 * 4, $"only {bends} corridor bends on 30 floors: corridors run too straight");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Structures_AreSoundAndReachable(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var reach = l.Reachable(l.UpStairs);
        foreach (var s in l.Structures)
        {
            Assert.DoesNotContain(s.Room, new[] { l.RoomAt(l.UpStairs), l.RoomAt(l.DownStairs), l.BossArena });
            if (s.Kind == StructureKind.Gate)
            {
                Assert.All(s.Cells, c => Assert.Equal(Cell.Wall, l[c]));
                Assert.Equal(Cell.Floor, l[s.Anchor]);
                Assert.Contains(s.Anchor, s.Openings);
                // both sides of the wall are reachable
                Assert.Contains(s.Anchor + s.Facing, reach);
                Assert.Contains(s.Anchor - s.Facing, reach);
                Assert.True(l.IsReserved(s.Anchor));
            }
            else if (s.Kind == StructureKind.Ruin) continue; // covered by Ruins_And_Rubble_LeaveTheFloorPassable
            else
            {
                Assert.All(s.Cells, c => { Assert.Equal(Cell.Floor, l[c]); Assert.Contains(c, reach); });
                var door = s.Openings[0];
                Assert.True(l.CanStep(door, s.Anchor), "the doorway opens onto the campfire");
                Assert.Contains(s.Anchor, reach);
                Assert.Equal(Cell.Floor, l[s.Anchor]);
                // every other edge out of the hut is walled
                foreach (var c in s.Cells)
                    foreach (var d in new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) })
                    {
                        var n = c + d;
                        if (s.Cells.Contains(n)) continue;
                        if (c == door && n == s.Anchor) continue;
                        Assert.False(l.CanStep(c, n), $"hut wall {c}->{n} is open");
                    }
                Assert.NotEmpty(s.Broken);
                Assert.DoesNotContain(l.ChestSpawns, c => c == s.Anchor);
            }
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Ruins_And_Rubble_LeaveTheFloorPassable(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var reach = l.Reachable(l.UpStairs);
        foreach (var ruin in l.Structures.Where(s => s.Kind == StructureKind.Ruin))
        {
            Assert.True(ruin.Cells.Count >= 4);
            Assert.All(ruin.Cells, c => { Assert.Equal(Cell.Floor, l[c]); Assert.True(l.IsReserved(c)); Assert.True(ruin.Room.Contains(c)); Assert.NotEqual(l.UpStairs, c); Assert.NotEqual(l.DownStairs, c); });
            // columns sit a cell in from the walls, so the ring around them stays open
            Assert.All(ruin.Cells, c => Assert.True(c.X > ruin.Room.X && c.X < ruin.Room.Right && c.Y > ruin.Room.Y && c.Y < ruin.Room.Bottom));
        }
        foreach (var r in l.Rubble)
        {
            Assert.Equal(Cell.Floor, l[r]);
            Assert.True(l.IsReserved(r));
            Assert.Contains(r, reach);
            var room = l.RoomAt(r);
            Assert.NotNull(room);
            foreach (var d in new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) })
            {
                var n = r + d;
                Assert.False(!room!.Contains(n) && l.IsWalkable(n), $"mound {r} sits beside the entrance {n}");
            }
            Assert.DoesNotContain(l.EnemySpawns.Concat(l.ChestSpawns).Concat(l.BreakableSpawns).Concat(l.KeySpawns).Concat(l.TrapSpawns), s => s == r);
            Assert.False(l.IsWater(r));
        }
        // with mounds and columns treated as blocked (as enemies treat them), every room centre is still reachable from the stairs
        var columns = l.Structures.Where(s => s.Kind == StructureKind.Ruin).SelectMany(s => s.Cells).ToHashSet();
        var blocked = l.Reachable(l.UpStairs, p => (l.Rubble.Contains(p) || columns.Contains(p)) && p != l.UpStairs);
        foreach (var room in l.Rooms) if (l.IsWalkable(room.Center) && !l.Rubble.Contains(room.Center) && !columns.Contains(room.Center)) Assert.Contains(room.Center, blocked);
    }

    [Fact]
    public void Structures_And_Water_ActuallyHappen()
    {
        var layouts = Enumerable.Range(0, 40).Select(i => FloorGenerator.Generate(Spec, i * 977 + 5)).ToList();
        Assert.True(layouts.Count(l => l.Structures.Count > 0) >= 32, $"structures on {layouts.Count(l => l.Structures.Count > 0)} floors");
        Assert.True(layouts.Count(l => l.Structures.Any(s => s.Kind == StructureKind.Gate)) >= 8);
        Assert.True(layouts.Count(l => l.Structures.Any(s => s.Kind == StructureKind.Camp)) >= 8);
        Assert.True(layouts.Count(l => l.Structures.Any(s => s.Kind == StructureKind.Ruin)) >= 8, $"ruins on {layouts.Count(l => l.Structures.Any(s => s.Kind == StructureKind.Ruin))} floors");
        Assert.True(layouts.Count(l => l.Rubble.Count > 0) >= 30, $"rubble on {layouts.Count(l => l.Rubble.Count > 0)} floors");
        Assert.Empty(FloorGenerator.Generate(Spec with { RubbleCount = 0, StructureCount = 0 }, 5).Rubble);
        Assert.True(layouts.Count(l => l.Water.Count > 0) >= 30, $"water on {layouts.Count(l => l.Water.Count > 0)} floors");
        Assert.True(layouts.Count(l => l.Water.Any(w => w.Kind == WaterKind.Pond)) >= 8);
        Assert.True(layouts.Count(l => l.Water.Any(w => w.Kind == WaterKind.Stream)) >= 8);
        Assert.Empty(FloorGenerator.Generate(Spec with { StructureCount = 0, WaterCount = 0 }, 5).Structures);
        Assert.Empty(FloorGenerator.Generate(Spec with { StructureCount = 0, WaterCount = 0 }, 5).Water);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Water_StaysWalkable_AndStreamsMeetWalls(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var reach = l.Reachable(l.UpStairs);
        foreach (var w in l.Water)
        {
            Assert.All(w.Cells, c => { Assert.Equal(Cell.Floor, l[c]); Assert.Equal(w.Kind, l.WaterAt(c)); Assert.Contains(c, reach); });
            Assert.DoesNotContain(w.Cells, c => c == l.UpStairs || c == l.DownStairs || l.ChestSpawns.Contains(c) || l.IsReserved(c));
            if (w.Kind == WaterKind.Stream)
            {
                Assert.NotEqual(default, w.Flow);
                Assert.NotNull(w.Bridge);
                Assert.Contains(w.Bridge!.Value, w.Cells);
                if (w.Cells.Count > 2) Assert.DoesNotContain(w.Bridge.Value, new[] { w.Cells[0], w.Cells[^1] });
                Assert.Equal(0, l.LevelOf(w.Cells[0]));
                Assert.False(l.IsWalkable(w.Cells[0] - (w.Flow.X != 0 ? new GridPos(Math.Abs(w.Flow.X), 0) : new GridPos(0, Math.Abs(w.Flow.Y)))));
            }
            else Assert.InRange(w.Cells.Count, 4, 8);
        }
    }

    [Fact]
    public void Generation_IsStillDeterministic()
    {
        var a = FloorGenerator.Generate(Spec, 4242);
        var b = FloorGenerator.Generate(Spec, 4242);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.Equal(a.Structures.Count, b.Structures.Count);
        Assert.Equal(a.BlockedEdges.OrderBy(e => e.Item1.X).ThenBy(e => e.Item1.Y), b.BlockedEdges.OrderBy(e => e.Item1.X).ThenBy(e => e.Item1.Y));
    }
}
