using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>A hero crowing over the kill they are dancing on: five lines per hero, topped into the campaign's bark set like the rest.</summary>
public class DanceBarksTests
{
    [Theory]
    [InlineData("Brand", "class.warrior")]
    [InlineData("Sable", "class.rogue")]
    [InlineData("Wren", "class.ranger")]
    [InlineData("Ilex", "class.mage")]
    [InlineData("Somebody", "class.warrior")] // a co-op name falls back to the class
    [InlineData("Nobody", "class.unknown")]   // and an unknown class to the generic lines
    public void EveryHero_HasTwentyDistinctDanceLines(string name, string classId)
    {
        var lines = DanceBarks.For(name, classId);
        var want = name is "Brand" or "Sable" or "Wren" or "Ilex" ? 20 : 5;
        Assert.True(lines.Count >= want, $"{lines.Count} lines");
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.All(lines, l => Assert.InRange(l.Length, 8, 120));
    }

    [Fact]
    public void TheHeroes_DoNotShareLines()
    {
        var all = new[] { "Brand", "Sable", "Wren", "Ilex" }.SelectMany(n => DanceBarks.For(n, "")).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void TopUp_AddsTheDanceLinesOnce_UnderTheDanceTrigger()
    {
        var set = new BarkSet { Member = "Sable" };
        var added = DanceBarks.TopUp(set, "class.rogue");
        Assert.Equal(20, added);
        Assert.All(set.Lines, l => Assert.Equal(BarkTrigger.Dance, l.Trigger));
        Assert.Equal(0, DanceBarks.TopUp(set, "class.rogue"));
        Assert.Equal(20, set.Lines.Count);
    }

    [Fact]
    public void Sable_SpeaksAsAWoman_AndHasFifteenDanceKillTaunts()
    {
        var dance = DanceBarks.For("Sable", "class.rogue");
        Assert.Contains(dance, l => l.Contains("girl") || l.Contains("lady"));
        var kills = KillBarks.For("Sable", "class.rogue");
        Assert.True(kills.Count >= 25, $"{kills.Count} kill lines"); // the ten first voiced plus fifteen on the dance theme
        Assert.Equal(kills.Count, kills.Distinct().Count());
        Assert.True(kills.Skip(10).Count(l => l.Contains("danc") || l.Contains("beat") || l.Contains("music") || l.Contains("hips") || l.Contains("shimmy") || l.Contains("squat") || l.Contains("rhythm") || l.Contains("footwork") || l.Contains("step")) >= 12);
    }

    [Fact]
    public void DanceLines_AreCrowed() => Assert.Equal(Delivery.Cheerful, Voices.BarkDelivery(BarkTrigger.Dance));
}
