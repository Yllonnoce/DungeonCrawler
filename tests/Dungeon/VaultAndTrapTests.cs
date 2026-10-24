using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class VaultAndTrapTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 60).Select(i => new object[] { i * 104729 + 7 });
    private static readonly FloorSpec Spec = FloorSpec.Default with { VaultCount = 2, TrapCount = 5, LockedChestCount = 1 };

    [Theory, MemberData(nameof(Seeds))]
    public void Vaults_AreDeadEndsWithOneDoorAndReachableOpener(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        foreach (var v in l.Vaults)
        {
            Assert.Equal(Cell.Door, l[v.Door]);
            Assert.False(v.Room.Contains(l.UpStairs));
            Assert.False(v.Room.Contains(l.DownStairs));
            // with the door closed, the vault interior is unreachable from the stairs...
            var reach = ReachableWithBlocked(l, l.UpStairs, p => p == v.Door);
            Assert.DoesNotContain(v.Room.Center, reach);
            // ...but its key or lever is reachable
            var opener = v.Kind == VaultKind.KeyDoor ? v.Key!.Value : v.Lever!.Value;
            Assert.Contains(opener, reach);
            Assert.False(v.Room.Contains(opener));
            Assert.Contains(v.Room.Center, l.ChestSpawns); // the prize
            Assert.DoesNotContain(l.EnemySpawns, e => v.Room.Contains(e));
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void DownStairs_ReachableWithAllDoorsClosed(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var doors = l.Vaults.Select(v => v.Door).ToHashSet();
        var reach = ReachableWithBlocked(l, l.UpStairs, doors.Contains);
        Assert.Contains(l.DownStairs, reach);
        foreach (var k in l.KeySpawns) Assert.Contains(k, reach);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Traps_OnWalkableCellsAwayFromStairsAndEachOther(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        Assert.InRange(l.TrapSpawns.Count, 1, Spec.TrapCount);
        foreach (var t in l.TrapSpawns)
        {
            Assert.Equal(Cell.Floor, l[t]);
            Assert.True(t.ManhattanDistance(l.UpStairs) >= 2 && t.ManhattanDistance(l.DownStairs) >= 2);
            Assert.False(l.IsInVault(t));
            Assert.DoesNotContain(t, l.EnemySpawns);
        }
        for (var i = 0; i < l.TrapSpawns.Count; i++)
            for (var j = i + 1; j < l.TrapSpawns.Count; j++)
                Assert.True(l.TrapSpawns[i].ManhattanDistance(l.TrapSpawns[j]) >= 3);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void LockedChests_HaveKeys(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        Assert.Equal(l.LockedChests.Count, l.KeySpawns.Count);
        foreach (var idx in l.LockedChests) Assert.InRange(idx, 0, l.ChestSpawns.Count - 1);
    }

    [Fact]
    public void Vaults_AppearOnMostFloors()
    {
        var with = Enumerable.Range(0, 100).Count(i => FloorGenerator.Generate(Spec, i * 31 + 5).Vaults.Count > 0);
        Assert.True(with >= 60, $"only {with}% of floors got a vault");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Pathfinder_FindsRouteBetweenStairs(int seed)
    {
        var l = FloorGenerator.Generate(Spec, seed);
        var path = GridPathfinder.FindPath(l, l.UpStairs, l.DownStairs)!;
        Assert.NotNull(path);
        Assert.Equal(l.UpStairs, path[0]);
        Assert.Equal(l.DownStairs, path[^1]);
        for (var i = 1; i < path.Count; i++)
        {
            Assert.Equal(1, path[i].ManhattanDistance(path[i - 1]));
            Assert.True(l.IsWalkable(path[i]));
        }
        Assert.True(path.Count - 1 >= l.UpStairs.ManhattanDistance(l.DownStairs));
    }

    [Fact]
    public void Pathfinder_RespectsBlockedDoorsAndFailsCleanly()
    {
        var l = Enumerable.Range(0, 200).Select(i => FloorGenerator.Generate(Spec, i)).First(x => x.Vaults.Count > 0);
        var v = l.Vaults[0];
        Assert.Null(GridPathfinder.FindPath(l, l.UpStairs, v.Room.Center, p => p == v.Door));
        Assert.NotNull(GridPathfinder.FindPath(l, l.UpStairs, v.Room.Center));
        Assert.Null(GridPathfinder.FindPath(l, l.UpStairs, new GridPos(0, 0)));
        Assert.Single(GridPathfinder.FindPath(l, l.UpStairs, l.UpStairs)!);
    }

    [Fact]
    public void TierRules_Boundaries()
    {
        Assert.Equal(0, TierRules.TierIndex(1)); Assert.Equal(0, TierRules.TierIndex(5));
        Assert.Equal(1, TierRules.TierIndex(6)); Assert.Equal(3, TierRules.TierIndex(20)); Assert.Equal(3, TierRules.TierIndex(37));
        Assert.True(TierRules.IsBossFloor(5)); Assert.True(TierRules.IsBossFloor(20)); Assert.False(TierRules.IsBossFloor(25)); Assert.False(TierRules.IsBossFloor(4));
        Assert.True(TierRules.IsFinalBossFloor(20));
        Assert.True(TierRules.HpScale(10) > TierRules.HpScale(1));
        Assert.True(TierRules.HpScale(25) > TierRules.HpScale(20) + 0.5f);
    }

    [Fact]
    public void TrapRules_ScaleAndClamp()
    {
        Assert.InRange(TrapRules.DetectRadiusCells(0), 0.6f, 0.8f); // a warrior notices a plate a step short of it
        Assert.InRange(TrapRules.DetectRadiusCells(48), 3.5f, 4f); // a ranger from across the room
        Assert.Equal(0.95f, TrapRules.LockpickChance(100));
        Assert.Equal(0.05f, TrapRules.LockpickChance(0));
        Assert.True(TrapRules.Attack(TrapKind.Spikes, 10).MaxDamage > TrapRules.Attack(TrapKind.Spikes, 1).MaxDamage);
        Assert.False(TrapRules.HitsWholeParty(TrapKind.Spikes));
        Assert.True(TrapRules.HitsWholeParty(TrapKind.Gas));
    }

    private static HashSet<GridPos> ReachableWithBlocked(FloorLayout l, GridPos start, Func<GridPos, bool> blocked)
    {
        var seen = new HashSet<GridPos> { start };
        var stack = new Stack<GridPos>();
        stack.Push(start);
        var dirs = new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) };
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in dirs)
            {
                var n = p + d;
                if (l.IsWalkable(n) && !blocked(n) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen;
    }

    [Fact]
    public void Pathfinder_AvoidsDangerWhenWeighted_IgnoresItAtZero()
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Standard, HazardsEnabled = false }, 7);
        var path = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs)!;
        var mid = path[path.Count / 2];
        float Danger(GridPos p) => p == mid ? 1f : 0f;
        var weighted = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, danger: Danger, dangerWeight: 50f)!;
        var mindless = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, danger: Danger, dangerWeight: 0f)!;
        Assert.Equal(path, mindless);
        if (!layout.Reachable(layout.UpStairs, c => c == mid).Contains(layout.DownStairs)) return; // mid is a bridge; nothing to detour through
        Assert.DoesNotContain(mid, weighted);
    }

    [Fact]
    public void Pathfinder_DangerCost_IsProportional()
    {
        // a 1x5 corridor with a dangerous middle: with weight 0 the path is 5 long, with weight it still passes (no alternative) but costs more
        var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Standard, HazardsEnabled = false }, 3);
        var path = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs)!;
        var all = new HashSet<GridPos>(path);
        var weighted = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, danger: p => all.Contains(p) ? 1f : 0f, dangerWeight: 12f)!;
        Assert.NotNull(weighted);
        Assert.Equal(layout.DownStairs, weighted[^1]);
    }
}
