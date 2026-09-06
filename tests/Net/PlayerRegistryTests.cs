using DungeonCrawler.Net;
using DungeonCrawler.Save;
using Xunit;

namespace DungeonCrawler.Tests.Net;

public class PlayerRegistryTests
{
    private int _next;
    private readonly List<(string name, string cls)> _created = new();
    private int Create(string name, string cls) { _created.Add((name, cls)); return _next++; }
    private static bool Cls(string id) => id is "class.warrior" or "class.rogue" or "class.ranger" or "class.mage";

    [Fact]
    public void NewName_CreatesMember_ReturningName_KeepsStoredClass()
    {
        var r = new PlayerRegistry();
        Assert.Equal(ClaimResult.Ok, r.Claim(2, " Eric ", "class.rogue", Cls, Create, out var slot));
        Assert.Equal("Eric", slot!.Name);
        Assert.Equal(0, slot.MemberIndex);
        Assert.True(slot.Online);
        r.Leave(2, new PoseDto { X = 5 }, 3, "t1");
        Assert.False(slot.Online);
        Assert.Equal(3, slot.ParkedFloor);
        Assert.Equal(ClaimResult.Ok, r.Claim(7, "eric", "class.mage", Cls, Create, out var again));
        Assert.Same(slot, again);
        Assert.Equal("class.rogue", again!.ClassId);
        Assert.Equal(7, again.PeerId);
        Assert.Single(_created);
        Assert.Equal(5f, again.ParkedPose!.X);
    }

    [Fact]
    public void Rejections()
    {
        var r = new PlayerRegistry { MaxOnline = 2 };
        Assert.Equal(ClaimResult.BadName, r.Claim(2, "", "class.rogue", Cls, Create, out _));
        Assert.Equal(ClaimResult.BadName, r.Claim(2, "a name that is far too long", "class.rogue", Cls, Create, out _));
        Assert.Equal(ClaimResult.BadName, r.Claim(2, "bad<name>", "class.rogue", Cls, Create, out _));
        Assert.Equal(ClaimResult.BadClass, r.Claim(2, "Eric", "class.bard", Cls, Create, out _));
        Assert.Equal(ClaimResult.Ok, r.Claim(2, "Eric", "class.rogue", Cls, Create, out _));
        Assert.Equal(ClaimResult.NameInUse, r.Claim(3, "ERIC", "class.rogue", Cls, Create, out _));
        Assert.Equal(ClaimResult.Ok, r.Claim(3, "Sam", "class.mage", Cls, Create, out _));
        Assert.Equal(ClaimResult.Full, r.Claim(4, "Kim", "class.mage", Cls, Create, out _));
        Assert.Equal(2, r.OnlineCount);
        r.Leave(3, null, 1, "t");
        Assert.Equal(ClaimResult.Ok, r.Claim(4, "Kim", "class.mage", Cls, Create, out _));
        Assert.Equal(3, r.All.Count);
        Assert.NotEmpty(PlayerRegistry.ReasonText(ClaimResult.Full));
    }

    [Fact]
    public void FullServer_RefusesReturningPlayerToo()
    {
        var r = new PlayerRegistry { MaxOnline = 1 };
        r.Claim(2, "Eric", "class.rogue", Cls, Create, out _);
        r.Leave(2, null, 1, "t");
        r.Claim(3, "Sam", "class.mage", Cls, Create, out _);
        Assert.Equal(ClaimResult.Full, r.Claim(4, "Eric", "class.rogue", Cls, Create, out _));
    }

    [Fact]
    public void Load_StartsEveryoneOffline_AndDtosMatch()
    {
        var r = new PlayerRegistry();
        r.Load(new[] { new PlayerSlot { Name = "Eric", ClassId = "class.rogue", MemberIndex = 0, PeerId = 9 }, new PlayerSlot { Name = "Sam", ClassId = "class.mage", MemberIndex = 1 } });
        Assert.Equal(0, r.OnlineCount);
        Assert.Equal(1, r.ByName("sam")!.MemberIndex);
        Assert.Null(r.ByPeer(0));
        var dtos = r.ToDtos();
        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.False(d.Online));
    }
}
