using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>A player who stands about gets razzed by a companion: ten lines per hero, on a timer that knows when the party is busy.</summary>
public class IdleBarksTests
{
    [Theory]
    [InlineData("Brand", "class.warrior")]
    [InlineData("Sable", "class.rogue")]
    [InlineData("Wren", "class.ranger")]
    [InlineData("Ilex", "class.mage")]
    [InlineData("Somebody", "class.warrior")] // a co-op name falls back to the class
    [InlineData("Nobody", "class.unknown")]   // and an unknown class to the generic lines
    public void EveryHero_HasTenDistinctRazzings(string name, string classId)
    {
        var lines = IdleBarks.For(name, classId);
        Assert.True(lines.Count >= 10, $"{lines.Count} lines");
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.All(lines, l => Assert.InRange(l.Length, 8, 160));
    }

    [Fact]
    public void TheHeroes_DoNotShareLines()
    {
        var all = new[] { "Brand", "Sable", "Wren", "Ilex" }.SelectMany(n => IdleBarks.For(n, "")).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void TopUp_AddsIdleLinesOnce_AndLeavesOtherTriggersAlone()
    {
        var set = new BarkSet { Member = "Brand" };
        set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line("Down.") });
        Assert.Equal(10, IdleBarks.TopUp(set, "class.warrior"));
        Assert.Equal(0, IdleBarks.TopUp(set, "class.warrior"));
        Assert.Equal(10, set.Lines.Count(l => l.Trigger == BarkTrigger.Idle));
        Assert.Single(set.Lines, l => l.Trigger == BarkTrigger.Kill);
    }

    [Fact]
    public void TheWatch_FiresAfterTenStillSeconds_ThenEveryTwentyWhileStill()
    {
        var w = new IdleWatch();
        Assert.False(w.Update(0, 0, now: 0, busy: false));
        Assert.False(w.Update(0.1f, 0, now: 9.9, busy: false));
        Assert.True(w.Update(0.1f, 0, now: 10.1, busy: false));
        Assert.False(w.Update(0.1f, 0, now: 20, busy: false));
        Assert.True(w.Update(0.1f, 0, now: 30.2, busy: false));
    }

    [Fact]
    public void TheWatch_ResetsWhenThePlayerMoves_OrIsBusy()
    {
        var w = new IdleWatch();
        w.Update(0, 0, now: 0, busy: false);
        Assert.False(w.Update(3f, 0, now: 9, busy: false));   // walked away: the clock restarts here
        Assert.False(w.Update(3f, 0, now: 18, busy: false));
        Assert.True(w.Update(3f, 0, now: 19.5, busy: false));
        Assert.False(w.Update(3f, 0, now: 25, busy: true));   // fighting or talking: not idle, and the clock restarts
        Assert.False(w.Update(3f, 0, now: 34, busy: false));
        Assert.True(w.Update(3f, 0, now: 35.5, busy: false));
    }

    [Fact]
    public void TheRazzer_IsACompanionNotTheLead_AndTakesTurns()
    {
        var party = new[] { "Brand", "Sable", "Wren", "Ilex" };
        var first = IdleBarks.Razzer(party, lead: "Brand", turn: 0);
        var second = IdleBarks.Razzer(party, lead: "Brand", turn: 1);
        Assert.NotEqual("Brand", first);
        Assert.NotEqual("Brand", second);
        Assert.NotEqual(first, second);
        Assert.Null(IdleBarks.Razzer(new[] { "Brand" }, lead: "Brand", turn: 0)); // alone, nobody to razz
    }
}
