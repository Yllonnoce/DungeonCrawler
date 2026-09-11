using DungeonCrawler.Adventure.Objectives;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class ObjectiveLineTests
{
    [Fact]
    public void Compose_OrdersAndSkipsBlanks()
    {
        Assert.Equal("", ObjectiveLine.Compose(null, "", "  ", null));
        Assert.Equal("Quest: do it", ObjectiveLine.Compose(null, null, "Quest: do it", null));
        Assert.Equal("Gate first   Horde: 3 remain   Quest: do it   Reinforcements: 2 hunting you",
            ObjectiveLine.Compose("Gate first", "Horde: 3 remain", "Quest: do it", "   Reinforcements: 2 hunting you"));
        Assert.Equal("Horde: 3 remain   Reinforcements: 2 hunting you", ObjectiveLine.Compose("", "Horde: 3 remain", "", "Reinforcements: 2 hunting you"));
    }

    [Fact]
    public void Compose_ClipsLongLines()
    {
        var line = ObjectiveLine.Compose(new string('a', 100), new string('b', 100), null, null, 60);
        Assert.Equal(60, line.Length);
        Assert.EndsWith("…", line);
    }
}
