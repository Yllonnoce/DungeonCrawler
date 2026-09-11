using DungeonCrawler.Adventure.Objectives;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class RitualRulesTests
{
    [Fact]
    public void Duration_IsBoundedAndGrowsWithDepth()
    {
        Assert.Equal(RitualRules.MinDuration, RitualRules.Duration(0));
        Assert.True(RitualRules.Duration(10) > RitualRules.Duration(2));
        Assert.Equal(RitualRules.MaxDuration, RitualRules.Duration(40));
        for (var f = 1; f <= 30; f++) Assert.InRange(RitualRules.Duration(f), RitualRules.MinDuration, RitualRules.MaxDuration);
    }

    [Fact]
    public void Waves_ComeFasterAndBiggerAsTheRiteGoesOn()
    {
        var last = float.MaxValue;
        var lastSize = 0;
        for (var t = 0f; t <= 1f; t += 0.1f)
        {
            var interval = RitualRules.WaveInterval(t, allSealed: false);
            Assert.True(interval <= last);
            last = interval;
            var size = RitualRules.WaveSize(t, 5);
            Assert.True(size >= lastSize);
            lastSize = size;
        }
        Assert.True(RitualRules.WaveSize(1f, 20) > RitualRules.WaveSize(1f, 1));
        Assert.True(RitualRules.MaxAlive(20) > RitualRules.MaxAlive(1));
        Assert.True(RitualRules.MaxAlive(99) <= 14);
    }

    [Fact]
    public void SealingEverything_SlowsButNeverStops()
    {
        var open = RitualRules.WaveInterval(0.5f, allSealed: false);
        var sealedAll = RitualRules.WaveInterval(0.5f, allSealed: true);
        Assert.Equal(open * RitualRules.SealedFactor, sealedAll, 3);
        Assert.True(float.IsFinite(sealedAll));
    }

    [Fact]
    public void Advances_OnlyWhileTheRitualistStands()
    {
        Assert.False(RitualRules.Advances(false, false, false));
        Assert.True(RitualRules.Advances(true, false, false));
        Assert.False(RitualRules.Advances(true, true, false));
        Assert.False(RitualRules.Advances(true, false, true));
    }
}
