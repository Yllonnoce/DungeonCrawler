using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>The offline designer plans around an objective's rooms so its plans validate without repairs.</summary>
public class ObjectiveDesignTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in FloorObjectives.All)
            for (var s = 0; s < 8; s++) yield return new object[] { kind, 3 + s % 2, 3000 + s * 7919 + 13 };
    }

    [Theory, MemberData(nameof(Cases))]
    public void Designs_Validate_Without_Repairs(FloorObjectiveKind kind, int floor, int seed)
    {
        var spec = TestAdventure.SpecFor(floor) with { Objective = kind };
        var layout = FloorGenerator.Generate(spec, seed);
        if (layout.Objective == null) return;
        var menu = FloorMenu.Build(layout, spec, TestAdventure.Crypt, TestAdventure.Roster, floor, PartySnapshot.Expected(floor));
        Assert.Equal(FloorObjectives.Id(kind), menu.Objective);
        var (plan, report) = AdventurePlanning.DesignOffline(layout, spec, TestAdventure.Crypt, TestAdventure.Roster, floor, seed, PartySnapshot.Expected(floor));
        Assert.True(report.Ok, report.ToString());
        Assert.Empty(report.Repairs);
        if (kind == FloorObjectiveKind.Jailbreak) Assert.Contains(plan.Rooms, r => r.Role == RoomRole.Jail);
        if (kind == FloorObjectiveKind.Ritual) Assert.Contains(plan.Rooms, r => r.Role == RoomRole.Shrine && menu.Room(r.Index)!.IsShrine);
        foreach (var n in plan.Npcs) { Assert.False(menu.Room(n.Room)!.IsJail); Assert.False(menu.Room(n.Room)!.IsShrine); }
        foreach (var h in plan.Hordes) Assert.False(menu.Room(h.Room)!.IsShrine);
    }
}
