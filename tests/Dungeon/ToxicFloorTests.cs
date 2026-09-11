using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>A Toxic floor keeps every floor invariant and puts its exit low, far and (where it can) behind a gate.</summary>
public class ToxicFloorTests
{
    public static IEnumerable<object[]> KindsAndSeeds()
    {
        foreach (var kind in new[] { LayoutKind.Standard, LayoutKind.Ring, LayoutKind.Mine, LayoutKind.Burning })
            for (var i = 0; i < 30; i++) yield return new object[] { kind, i * 7919 + 13 };
    }

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void Exit_IsLowFarAndReachable(LayoutKind kind, int seed)
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = FloorObjectiveKind.Toxic }, seed);
        Assert.NotNull(layout.Objective);
        Assert.Equal(FloorObjectiveKind.Toxic, layout.Objective!.Kind);
        Assert.Equal(Cell.Floor, layout[layout.DownStairs]);
        Assert.NotEqual(layout.UpStairs, layout.DownStairs);
        var exit = layout.RoomAt(layout.DownStairs);
        Assert.NotNull(exit);
        var reach = layout.Reachable(layout.UpStairs);
        Assert.Contains(layout.DownStairs, reach);
        Assert.False(layout.IsUnder(layout.DownStairs));
        Assert.DoesNotContain(layout.Ledges, l => l.Cells.Contains(layout.DownStairs) || l.RampBottom == layout.DownStairs || l.RampTop == layout.DownStairs);
        Assert.DoesNotContain(layout.DownStairs, layout.ChestSpawns);
        Assert.DoesNotContain(layout.DownStairs, layout.EnemySpawns);
        Assert.DoesNotContain(layout.DownStairs, layout.TrapSpawns);
        // low when any usable low room exists at all
        var upRoom = layout.RoomAt(layout.UpStairs);
        if (layout.Rooms.Any(r => r != upRoom && layout.LevelOf(r.Center) == 0 && layout.Vaults.All(v => v.Room != r || v.Index == layout.Objective.ExitGateVault)))
            Assert.Equal(0, layout.LevelOf(exit!.Center));
        if (layout.Objective.ExitGateVault >= 0)
        {
            var gate = layout.Vaults[layout.Objective.ExitGateVault];
            Assert.Equal(VaultKind.LeverGate, gate.Kind);
            Assert.True(gate.Revealed);
            Assert.Equal(exit, gate.Room);
            Assert.Equal(Cell.Door, layout[gate.Door]);
            Assert.NotNull(gate.Lever);
            Assert.Contains(gate.Lever!.Value, reach); // the lever can be reached with the gate shut
            var shut = layout.Reachable(layout.UpStairs, p => p == gate.Door);
            Assert.Contains(gate.Lever!.Value, shut);
            Assert.DoesNotContain(layout.DownStairs, shut);
            Assert.False(exit!.Contains(gate.Lever.Value));
        }
        // every spawn and chest still stands on walkable ground the party can reach
        foreach (var s in layout.EnemySpawns) Assert.Contains(s, reach);
        foreach (var c in layout.ChestSpawns) Assert.True(layout.IsWalkable(c));
    }

    [Fact]
    public void Deterministic()
    {
        var a = FloorGenerator.Generate(FloorSpec.Default with { Objective = FloorObjectiveKind.Toxic }, 77);
        var b = FloorGenerator.Generate(FloorSpec.Default with { Objective = FloorObjectiveKind.Toxic }, 77);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.NotEqual(FloorGenerator.Generate(FloorSpec.Default, 77).ContentHash(), a.ContentHash());
    }
}
