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
    public void EveryHero_HasFiveDistinctDanceLines(string name, string classId)
    {
        var lines = DanceBarks.For(name, classId);
        Assert.True(lines.Count >= 5, $"{lines.Count} lines");
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
        Assert.Equal(5, added);
        Assert.All(set.Lines, l => Assert.Equal(BarkTrigger.Dance, l.Trigger));
        Assert.Equal(0, DanceBarks.TopUp(set, "class.rogue"));
        Assert.Equal(5, set.Lines.Count);
    }

    [Fact]
    public void DanceLines_AreCrowed() => Assert.Equal(Delivery.Cheerful, Voices.BarkDelivery(BarkTrigger.Dance));
}
