using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>A Jailbreak floor: a sealed jail with a keyless key door, prisoners in and out of it, spare keys, a reward vault.</summary>
public class JailFloorTests
{
    public static IEnumerable<object[]> KindsAndSeeds()
    {
        foreach (var kind in LayoutKinds.All)
            for (var i = 0; i < 25; i++) yield return new object[] { kind, i * 7919 + 13 };
    }

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void Jail_PrisonersAndKeys(LayoutKind kind, int seed)
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = FloorObjectiveKind.Jailbreak }, seed);
        if (layout.Objective == null) return;
        var o = layout.Objective;
        Assert.NotNull(o.JailRoom);
        Assert.True(o.JailVault >= 0);
        var jail = layout.Vaults[o.JailVault];
        Assert.Equal(VaultKind.KeyDoor, jail.Kind);
        Assert.Null(jail.Key);
        Assert.True(jail.Revealed);
        Assert.Equal(o.JailRoom, jail.Room);
        Assert.Equal(Cell.Door, layout[jail.Door]);
        Assert.False(jail.Room.Contains(layout.UpStairs));
        Assert.False(jail.Room.Contains(layout.DownStairs));
        var reach = layout.Reachable(layout.UpStairs);
        Assert.Contains(jail.Room.Center, reach);
        Assert.DoesNotContain(jail.Room.Center, layout.Reachable(layout.UpStairs, p => p == jail.Door));
        Assert.DoesNotContain(layout.EnemySpawns, s => jail.Room.Contains(s));
        // prisoners: some inside, the rest out in different rooms
        Assert.InRange(o.PrisonerCells.Count, JailbreakRules.MinPrisoners, JailbreakRules.MaxPrisoners);
        Assert.InRange(o.JailedAtStart, 1, 2);
        Assert.True(o.PrisonerCells.Count > o.JailedAtStart);
        for (var i = 0; i < o.PrisonerCells.Count; i++)
        {
            var cell = o.PrisonerCells[i];
            Assert.Equal(i < o.JailedAtStart, jail.Room.Contains(cell));
            Assert.Equal(Cell.Floor, layout[cell]);
            Assert.True(layout.IsReserved(cell));
            Assert.Contains(cell, reach);
            Assert.DoesNotContain(cell, layout.EnemySpawns);
        }
        var outside = o.PrisonerCells.Skip(o.JailedAtStart).Select(c => layout.RoomAt(c)).ToList();
        Assert.All(outside, r => Assert.NotNull(r));
        Assert.Equal(outside.Count, outside.Distinct().Count());
        Assert.DoesNotContain(outside, r => r == layout.RoomAt(layout.UpStairs));
        // spare keys lie somewhere reachable and are counted among the floor's keys
        Assert.InRange(o.ExtraKeyCells.Count, 1, 2);
        foreach (var k in o.ExtraKeyCells) { Assert.Contains(k, layout.KeySpawns); Assert.Contains(k, reach); Assert.False(jail.Room.Contains(k)); }
        if (o.RewardVault >= 0) Assert.NotEqual(o.JailVault, o.RewardVault);
        // the people
        var contents = ObjectivePlanner.Resolve(layout, 3, seed)!;
        Assert.Equal(o.PrisonerCells.Count, contents.Prisoners.Count);
        Assert.Equal(o.JailedAtStart, contents.Prisoners.Count(p => p.Jailed));
        Assert.Equal(contents.Prisoners.Count, contents.Prisoners.Select(p => p.Id).Distinct().Count());
        Assert.Equal(contents.Prisoners.Count, contents.Prisoners.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void MostFloors_GetAJail()
    {
        var with = 0;
        for (var seed = 1; seed <= 40; seed++) if (FloorGenerator.Generate(FloorSpec.Default with { Objective = FloorObjectiveKind.Jailbreak }, seed).Objective != null) with++;
        Assert.True(with >= 36, $"only {with}/40 floors got a jail");
    }
}
