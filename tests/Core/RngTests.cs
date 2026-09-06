using DungeonCrawler.Core;
using Xunit;

namespace DungeonCrawler.Tests.Core;

public class RngTests
{
    [Fact]
    public void FloorSeed_IsDeterministic()
    {
        Assert.Equal(Rng.FloorSeed(12345UL, 3), Rng.FloorSeed(12345UL, 3));
    }

    [Fact]
    public void FloorSeed_DiffersAcrossFloorsAndGames()
    {
        Assert.NotEqual(Rng.FloorSeed(12345UL, 3), Rng.FloorSeed(12345UL, 4));
        Assert.NotEqual(Rng.FloorSeed(12345UL, 3), Rng.FloorSeed(12346UL, 3));
    }

    [Fact]
    public void ForFloor_ProducesSameSequence()
    {
        var a = Rng.ForFloor(99UL, 7);
        var b = Rng.ForFloor(99UL, 7);
        for (var i = 0; i < 100; i++) Assert.Equal(a.Next(), b.Next());
    }
}
