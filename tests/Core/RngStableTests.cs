using DungeonCrawler.Core;
using Xunit;

namespace DungeonCrawler.Tests.Core;

public class RngStableTests
{
    [Fact]
    public void StableHash_IsFixed_AndDistinguishesStrings()
    {
        Assert.Equal(Rng.StableHash("npc1"), Rng.StableHash("npc1"));
        Assert.NotEqual(Rng.StableHash("npc1"), Rng.StableHash("npc2"));
        Assert.Equal(14695981039346656037UL, Rng.StableHash(""));
        Assert.Equal(0xAF63DC4C8601EC8CUL, Rng.StableHash("a")); // the FNV-1a reference value for "a"
    }

    [Fact]
    public void StableGuid_And_ActorSeed_AreDeterministic()
    {
        Assert.Equal(Rng.StableGuid(42), Rng.StableGuid(42));
        Assert.NotEqual(Rng.StableGuid(42), Rng.StableGuid(43));
        Assert.NotEqual(Guid.Empty, Rng.StableGuid(0));
        Assert.Equal(Rng.ActorSeed(7, 3, 12), Rng.ActorSeed(7, 3, 12));
        Assert.NotEqual(Rng.ActorSeed(7, 3, 12), Rng.ActorSeed(7, 3, 13));
        Assert.NotEqual(Rng.ActorSeed(7, 3, 12), Rng.ActorSeed(7, 4, 12));
        Assert.NotEqual(Rng.ActorSeed(7, 3, 12), Rng.ActorSeed(8, 3, 12));
        var a = new Random(Rng.ActorSeed(7, 3, 12)).Next();
        Assert.Equal(a, new Random(Rng.ActorSeed(7, 3, 12)).Next());
    }
}
