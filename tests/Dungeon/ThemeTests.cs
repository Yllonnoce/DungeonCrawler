using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Tests.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class ThemeTests
{
    private static readonly List<ThemeProfile> Themes = new()
    {
        new("crypt.brick", "Red Brick Crypt", 0, 1.2f), new("crypt.ossuary", "White Ossuary", 0, 1f), new("crypt.mossy", "Mossy Catacomb", 0, 1f),
        new("caves.rock", "Dark Caves", 1, 1f), new("caves.sandstone", "Sandstone Warren", 1, 1f),
        new("any.void", "Nowhere", -1, 0.5f),
    };

    [Fact]
    public void ForTier_IncludesTierAndAnyThemes()
    {
        var crypt = ThemeProfile.ForTier(Themes, 0).Select(t => t.Id).ToList();
        Assert.Equal(new[] { "crypt.brick", "crypt.ossuary", "crypt.mossy", "any.void" }, crypt);
        Assert.Equal(new[] { "caves.rock", "caves.sandstone", "any.void" }, ThemeProfile.ForTier(Themes, 1).Select(t => t.Id));
        Assert.Equal(new[] { "any.void" }, ThemeProfile.ForTier(Themes, 3).Select(t => t.Id));
    }

    [Fact]
    public void Pick_IsStable_AndVariesAcrossFloors()
    {
        var a = ThemeProfile.Pick(Themes, 0, 12345, 3)!;
        var b = ThemeProfile.Pick(Themes, 0, 12345, 3)!;
        Assert.Equal(a.Id, b.Id);
        Assert.Contains(a.Id, ThemeProfile.ForTier(Themes, 0).Select(t => t.Id));
        var picks = Enumerable.Range(1, 40).Select(f => ThemeProfile.Pick(Themes, 0, 777, f)!.Id).Distinct().Count();
        Assert.True(picks >= 3, "forty floors of one tier should wear several looks");
        Assert.Null(ThemeProfile.Pick(new List<ThemeProfile>(), 0, 1, 1));
    }

    [Fact]
    public void OfflineOutline_AssignsTierThemes()
    {
        var outline = OfflineDesigner.Outline(99, new[] { TestAdventure.Crypt }, Themes);
        Assert.All(outline.Floors, f => Assert.NotNull(f.Theme));
        Assert.All(outline.Floors, f => Assert.Contains(f.Theme!, ThemeProfile.ForTier(Themes, TierRules.TierIndex(f.Floor)).Select(t => t.Id)));
        Assert.All(OfflineDesigner.Outline(99, new[] { TestAdventure.Crypt }).Floors, f => Assert.Null(f.Theme));
    }

    [Fact]
    public async Task ClaudeOutline_KeepsValidThemes_DropsUnknown()
    {
        var (layout, spec, menu) = TestAdventure.Floor(1, 5);
        var floor1 = OfflineDesigner.PlanFloor(menu, layout, 5);
        var campaign = new CampaignPlan
        {
            Title = "Themed", Floors =
            {
                new FloorOutline { Floor = 1, Theme = "crypt.mossy" },
                new FloorOutline { Floor = 2, Theme = "caves.rock" },   // wrong tier for floor 2
                new FloorOutline { Floor = 6, Theme = "made.up" },
            },
        };
        var fake = new FakeClaudeClient(_ => new ClaudeResponse(PlanFormat.Serialize(new OutlineWithFirstFloor { Campaign = campaign, Floor1 = floor1 }), "end_turn", ClaudeUsage.Zero));
        var planner = new ClaudePlanner(fake, new CostMeter(new SpendDto(), 0));
        var req = new OutlineRequest(5, new[] { TestAdventure.Crypt }, TestAdventure.Roster, new[] { new PartyMemberInfo("Brand", "Warrior") }, layout, spec, 5, PartySnapshot.Expected(1), Themes);
        var result = await planner.OutlineAsync(req, CancellationToken.None);
        Assert.Equal("crypt.mossy", result.Campaign.OutlineFor(1)!.Theme);
        Assert.Null(result.Campaign.OutlineFor(2)!.Theme);
        Assert.Null(result.Campaign.OutlineFor(6)!.Theme);
        var prompt = PromptBuilder.Outline(PromptTemplates.Default, "m", 5, new[] { TestAdventure.Crypt }, TestAdventure.Roster, Array.Empty<PartyMemberInfo>(), menu, Themes);
        Assert.Contains("crypt.ossuary = White Ossuary", prompt.User);
    }
}
