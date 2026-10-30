using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>Heroes who have walked a long way without a fight grumble about it: twenty lines each, on a watch that counts quiet miles.</summary>
public class TravelBarksTests
{
    [Theory]
    [InlineData("Brand", "class.warrior")]
    [InlineData("Sable", "class.rogue")]
    [InlineData("Wren", "class.ranger")]
    [InlineData("Ilex", "class.mage")]
    [InlineData("Somebody", "class.warrior")] // a co-op name falls back to the class
    [InlineData("Nobody", "class.unknown")]   // and an unknown class to the generic lines
    public void EveryHero_HasTwentyDistinctTravelLines(string name, string classId)
    {
        var lines = TravelBarks.For(name, classId);
        var want = name is "Brand" or "Sable" or "Wren" or "Ilex" ? 20 : 5;
        Assert.True(lines.Count >= want, $"{lines.Count} lines");
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.All(lines, l => Assert.InRange(l.Length, 8, 140));
    }

    [Fact]
    public void TheHeroes_DoNotShareLines_AndTheOwnersExamplesAreIn()
    {
        var all = new[] { "Brand", "Sable", "Wren", "Ilex" }.SelectMany(n => TravelBarks.For(n, "")).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Contains(all, l => l.Contains("paid by the mile"));
        Assert.Contains(all, l => l.Contains("pass gas"));
        Assert.Contains(all, l => l.Contains("Fighting for a living is overrated"));
    }

    [Fact]
    public void TopUp_AddsTheTravelLinesOnce_UnderTheTravelTrigger()
    {
        var set = new BarkSet { Member = "Wren" };
        Assert.Equal(20, TravelBarks.TopUp(set, "class.ranger"));
        Assert.All(set.Lines, l => Assert.Equal(BarkTrigger.Travel, l.Trigger));
        Assert.Equal(0, TravelBarks.TopUp(set, "class.ranger"));
    }

    [Fact] public void TravelLines_AreSaidWearily() => Assert.Equal(Delivery.Weary, Voices.BarkDelivery(BarkTrigger.Travel));

    [Fact]
    public void Speaker_RotatesThroughTheWholeParty_LeadIncluded()
    {
        var party = new[] { "Brand", "Sable", "Wren", "Ilex" };
        var picks = Enumerable.Range(0, 8).Select(t => TravelBarks.Speaker(party, t)).ToList();
        Assert.Equal(party, picks.Take(4));
        Assert.Equal(party, picks.Skip(4));
        Assert.Null(TravelBarks.Speaker(Array.Empty<string>(), 0));
    }

    [Fact]
    public void Watch_FiresAfterAQuietStretchOfWalking_ThenNotAgainUntilMoreMiles()
    {
        var w = new TravelWatch();
        var quiet = TravelWatch.QuietSeconds; var dist = TravelWatch.MinDistance;
        Assert.False(w.Update(now: 0, distance: 0, action: true));                 // a fight just ended
        Assert.False(w.Update(now: quiet - 1, distance: dist * 2, action: false));  // far enough, but not quiet long enough
        Assert.True(w.Update(now: quiet + 1, distance: dist * 2, action: false));   // quiet and travelled: speak
        Assert.False(w.Update(now: quiet + 2, distance: dist * 2 + 1, action: false)); // just spoke
        var later = quiet + 1 + TravelWatch.RepeatSeconds + 1;
        Assert.False(w.Update(now: later, distance: dist * 2 + 1, action: false));  // time passed but the party stood about
        Assert.True(w.Update(now: later + 1, distance: dist * 3 + 2, action: false)); // walked on: speak again
    }

    [Fact]
    public void Watch_AFight_OrAMenu_ResetsTheQuietClock()
    {
        var w = new TravelWatch();
        w.Update(now: 0, distance: 0, action: false);
        Assert.False(w.Update(now: TravelWatch.QuietSeconds - 5, distance: 100, action: true));
        Assert.False(w.Update(now: TravelWatch.QuietSeconds + 5, distance: 200, action: false)); // only ten quiet seconds since the fight
        Assert.True(w.Update(now: TravelWatch.QuietSeconds * 2, distance: 300, action: false));
    }

    [Fact]
    public void Watch_SurvivesANewFloor_WhereTheBodysDistanceStartsOver()
    {
        var w = new TravelWatch();
        w.Update(now: 0, distance: 500, action: false);
        Assert.True(w.Update(now: TravelWatch.QuietSeconds + 1, distance: 500 + TravelWatch.MinDistance + 1, action: false));
        var t = TravelWatch.QuietSeconds + 1 + TravelWatch.RepeatSeconds + 1;
        Assert.False(w.Update(now: t, distance: 2, action: false)); // new body: distance fell back to nearly nothing
        Assert.True(w.Update(now: t + 1, distance: 2 + TravelWatch.MinDistance + 1, action: false));
    }
}
