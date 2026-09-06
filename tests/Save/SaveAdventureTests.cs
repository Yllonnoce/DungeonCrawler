using DungeonCrawler.Adventure;
using DungeonCrawler.Save;
using DungeonCrawler.Tests.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Save;

public class SaveAdventureTests
{
    [Fact]
    public void V5Save_Loads_WithNoAdventure()
    {
        const string json = """{"Version":5,"Label":"old","GameSeed":7,"CurrentFloor":2,"SavedAtUtc":"","PlaySeconds":3,"Victory":false,"Party":{"Members":[],"Bag":[]},"Floors":{}}""";
        var data = SaveFormat.Deserialize(json);
        Assert.Equal(SaveFormat.CurrentVersion, data.Version);
        Assert.Null(data.Adventure);
        Assert.Equal(2, data.CurrentFloor);
    }

    [Fact]
    public void Adventure_RoundTrips_WithEnumsAsText()
    {
        var (layout, _, menu) = TestAdventure.Floor(3, 11);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 11);
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        var data = new SaveData
        {
            GameSeed = 99, CurrentFloor = 3,
            Adventure = new AdventureDto { Mode = AdventureMode.Offline, Campaign = OfflineDesigner.Outline(99, new[] { TestAdventure.Crypt }), FloorPlans = { [3] = plan } },
        };
        var json = SaveFormat.Serialize(data);
        Assert.Contains("\"Mode\": \"Offline\"", json);
        Assert.Contains("\"Kind\": \"" + plan.Collectives[0].Goal.Kind + "\"", json);
        var back = SaveFormat.Deserialize(json);
        Assert.Equal(json, SaveFormat.Serialize(back));
        Assert.Equal(AdventureMode.Offline, back.Adventure!.Mode);
        Assert.Equal(plan.LayoutHash, back.Adventure.FloorPlans[3].LayoutHash);
        Assert.Equal(plan.Collectives.Count, back.Adventure.FloorPlans[3].Collectives.Count);
        Assert.Equal("The Long Descent", back.Adventure.Campaign!.Title);
    }
}
