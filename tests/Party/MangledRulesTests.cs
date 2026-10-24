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
