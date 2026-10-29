using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class KillBarksTests
{
    [Theory]
    [InlineData("Brand", "class.warrior")]
    [InlineData("Sable", "class.rogue")]
    [InlineData("Wren", "class.ranger")]
    [InlineData("Ilex", "class.mage")]
    [InlineData("Somebody", "class.warrior")] // a co-op name falls back to the class
    public void EveryMember_HasAtLeastEightShortDistinctKillLines(string name, string classId)
    {
        var lines = KillBarks.For(name, classId);
        Assert.True(lines.Count >= 8, $"{lines.Count} lines");
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.All(lines, l => Assert.InRange(l.Length, 3, 80));
    }

    [Theory]
    [InlineData("viewer_foehammer")]
    [InlineData("fighter_tamsin")]
    [InlineData("fighter_oskar")]
    [InlineData("fighter_ione")]
    public void EveryFighter_HasKillLines(string id)
    {
        var lines = KillBarks.ForFighter(id);
        Assert.True(lines.Count >= 6);
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Fact]
    public void TopUp_AddsOnlyWhatIsMissing_AndIsIdempotent()
    {
        var set = new BarkSet { Member = "Brand" };
        set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line("Down. Next one, don't dawdle.") });
        set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Hurt, Line = new Line("Agh!") });
        var added = KillBarks.TopUp(set, "class.warrior");
        Assert.True(added >= 7);
        var kills = set.Lines.Where(l => l.Trigger == BarkTrigger.Kill).Select(l => l.Line.Text).ToList();
        Assert.Contains("Down. Next one, don't dawdle.", kills);
        Assert.Equal(kills.Count, kills.Distinct().Count());
        Assert.Single(set.Lines.Where(l => l.Trigger == BarkTrigger.Hurt));
        Assert.Equal(0, KillBarks.TopUp(set, "class.warrior"));
    }

    [Fact]
    public void ChooseOrOldest_NeverFallsSilent()
    {
        var said = new Dictionary<string, int> { ["a"] = 5, ["b"] = 4, ["c"] = 5 };
        var rng = new Random(1);
        // everything was said within the last two floors: the one said longest ago comes back
        Assert.Equal("b", BarkRepeat.ChooseOrOldest(said, new[] { "a", "b", "c" }, 5, rng));
        // a fresh line still wins over a stale one
        Assert.Equal("d", BarkRepeat.ChooseOrOldest(said, new[] { "a", "b", "d" }, 5, rng));
    }

    [Fact]
    public void KillBarkRules_AreQuickerThanOtherBarks()
    {
        Assert.True(KillBarks.CooldownSeconds <= 8f);
        Assert.InRange(KillBarks.Chance, 0.5f, 0.9f);
    }
}
