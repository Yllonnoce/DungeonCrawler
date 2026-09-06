using DungeonCrawler.Net;
using Xunit;

namespace DungeonCrawler.Tests.Net;

public class NetRefsTests
{
    [Fact]
    public void Builds_And_Parses()
    {
        Assert.Equal("chest:3", NetRefs.Chest(3));
        Assert.True(NetRefs.TryParse(NetRefs.Door(1), out var kind, out var id));
        Assert.Equal("door", kind); Assert.Equal("1", id);
        Assert.True(NetRefs.TryIndex(id, out var index)); Assert.Equal(1, index);
        Assert.True(NetRefs.TryParse(NetRefs.StairsDown, out kind, out id));
        Assert.Equal("stairs", kind); Assert.Equal("down", id);
        var g = Guid.NewGuid();
        Assert.True(NetRefs.TryParse(NetRefs.Loot(g), out kind, out id));
        Assert.Equal(g, Guid.ParseExact(id, "N"));
        Assert.True(NetRefs.TryParse(NetRefs.Npc("npc.smith:2"), out kind, out id));
        Assert.Equal("npc", kind); Assert.Equal("npc.smith:2", id);
    }

    [Fact]
    public void Malformed_IsRefused()
    {
        Assert.False(NetRefs.TryParse(null, out _, out _));
        Assert.False(NetRefs.TryParse("", out _, out _));
        Assert.False(NetRefs.TryParse("chest", out _, out _));
        Assert.False(NetRefs.TryParse(":3", out _, out _));
        Assert.False(NetRefs.TryParse("chest:", out _, out _));
        Assert.False(NetRefs.TryParse("ch3st:1", out _, out _));
        Assert.False(NetRefs.TryParse(new string('a', 81) + ":1", out _, out _));
        Assert.False(NetRefs.TryIndex("-1", out _));
        Assert.False(NetRefs.TryIndex("x", out _));
    }
}
