using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Party;
using Xunit;

namespace DungeonCrawler.Tests.Party;

public class MangledRulesTests
{
    [Fact] public void Mangled_NeverAutoRevives() => Assert.False(MangledRules.CanAutoRevive(mangled: true, hazardArmed: true));
    [Fact] public void Downed_AutoRevives() => Assert.True(MangledRules.CanAutoRevive(mangled: false, hazardArmed: false));
    [Fact] public void PartyWipe_IgnoresMangled_WhileAnyoneStands() => Assert.False(MangledRules.IsWipe(new[] { (Downed: true, Mangled: true), (Downed: false, Mangled: false) }));
    [Fact] public void PartyWipe_WhenAllDown_OrMangled() => Assert.True(MangledRules.IsWipe(new[] { (Downed: true, Mangled: true), (Downed: true, Mangled: false) }));
    [Fact] public void PartyWipe_NeedsSomebody() => Assert.False(MangledRules.IsWipe(System.Array.Empty<(bool, bool)>()));

    [Fact]
    public void BoundCell_IsAheadOfTheParty_AndExplored()
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default, 11);
        var path = HazardPlanner.CriticalPath(layout);
        var ordered = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs)!;
        var explored = new HashSet<GridPos>(ordered.Take(ordered.Count / 2).SelectMany(c => layout.RoomAt(c)?.Cells() ?? new[] { c }));
        var living = new[] { ordered[ordered.Count / 4] };
        var cell = MangledRules.BoundCell(layout, explored, living, path);
        Assert.NotNull(cell);
        Assert.Contains(cell!.Value, explored);
        Assert.True(ordered.IndexOf(cell.Value) > ordered.IndexOf(living[0]));
        Assert.NotNull(layout.RoomAt(cell.Value));
    }

    [Fact]
    public void BoundCell_IsNull_WhenNothingAheadIsExplored()
    {
        var layout = FloorGenerator.Generate(FloorSpec.Default, 11);
        var path = HazardPlanner.CriticalPath(layout);
        var ordered = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs)!;
        var explored = new HashSet<GridPos>(ordered.Take(3));
        Assert.Null(MangledRules.BoundCell(layout, explored, new[] { ordered[ordered.Count - 1] }, path));
    }

    [Fact]
    public void Rescue_NeedsArmedHazard_AndRange()
    {
        Assert.True(MangledRules.RescueAllowed(hazardArmed: true, distance: 1.5f, rescuerDowned: false));
        Assert.False(MangledRules.RescueAllowed(hazardArmed: false, distance: 1.5f, rescuerDowned: false));
        Assert.False(MangledRules.RescueAllowed(hazardArmed: true, distance: 2.5f, rescuerDowned: false));
        Assert.False(MangledRules.RescueAllowed(hazardArmed: true, distance: 1f, rescuerDowned: true));
    }

    [Fact]
    public void Constants_MatchSpec()
    {
        Assert.Equal(3f, MangledRules.RescueSeconds);
        Assert.Equal(2f, MangledRules.RescueRange);
        Assert.Equal(1f, MangledRules.RescueInterruptMove);
        Assert.Equal(30f, MangledRules.BoundAfterSeconds);
        Assert.Equal(1f, MangledRules.ReviveHp);
        Assert.Equal(25f, MangledRules.ReviveStress);
    }
}
