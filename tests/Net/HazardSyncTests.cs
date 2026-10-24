using DungeonCrawler.Dungeon;
using DungeonCrawler.Net;
using Xunit;

namespace DungeonCrawler.Tests.Net;

public class HazardSyncTests
{
    [Fact]
    public void OpenEndedPhase_TravelsAsZeroTicks_NotIntMinValue()
    {
        Assert.Equal(0, HazardSync.DurationTicks(HazardRules.PressDuration(PressPhase.Armed)));
        Assert.Equal(0, HazardSync.DurationTicks(HazardRules.SpikeDuration(SpikePhase.Down, HazardVariant.Pressure)));
        Assert.Equal(144, HazardSync.DurationTicks(HazardRules.PressDuration(PressPhase.Closing)));
    }

    [Fact]
    public void SecondsIn_IsClampedToTheDuration_AndNeverNegative()
    {
        Assert.Equal(0.5f, HazardSync.SecondsIn(localTick: 1030, serverTickOffset: 0, serverTick: 1000, durationTicks: 144), 3);
        Assert.Equal(2.4f, HazardSync.SecondsIn(localTick: 2000, serverTickOffset: 0, serverTick: 1000, durationTicks: 144), 3);
        Assert.Equal(0f, HazardSync.SecondsIn(localTick: 900, serverTickOffset: 0, serverTick: 1000, durationTicks: 144));
    }

    [Fact]
    public void SecondsIn_OpenEndedPhase_IsJustTheElapsedTime()
    {
        Assert.Equal(1f, HazardSync.SecondsIn(localTick: 1000, serverTickOffset: 60, serverTick: 1000, durationTicks: 0), 3);
    }
}
