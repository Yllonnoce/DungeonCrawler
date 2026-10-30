using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>The engine asks the Gullet campaign made: a cast member who leads a floor's rite, and a boss whose last words turn on a flag.</summary>
public class StoryBeatsTests
{
    private static (FloorLayout Layout, FloorMenu Menu, FloorPlan Plan) RitualFloor()
    {
        for (var seed = 3000; seed < 3400; seed += 7)
        {
            var spec = TestAdventure.SpecFor(3) with { Objective = FloorObjectiveKind.Ritual };
            var layout = FloorGenerator.Generate(spec, seed);
            if (layout.Objective?.Kind != FloorObjectiveKind.Ritual) continue;
            var menu = FloorMenu.Build(layout, spec, TestAdventure.Crypt, TestAdventure.Roster, 3, PartySnapshot.Expected(3));
            var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
            return (layout, menu, plan);
        }
        throw new InvalidOperationException("no ritual layout in the seed range");
    }

    private static NpcPlan Grisel(int room) => new() { Id = "npc_grisel", Name = "Grisel Thorn", Room = room, Ritualist = true };

    [Fact]
    public void ARitualistInThePlan_IsKept_EvenInTheShrine_WithNoDialogue()
    {
        var (layout, menu, plan) = RitualFloor();
        var shrine = menu.Rooms.First(r => r.IsShrine).Index;
        plan.Npcs.Add(Grisel(shrine));
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.True(report.Ok, report.ToString());
        Assert.DoesNotContain(report.Repairs, r => r.Contains("npc_grisel"));
        Assert.Contains(plan.Npcs, n => n.Id == "npc_grisel" && n.Ritualist);
    }

    [Fact]
    public void ARitualistOnAFloorWithNoRite_LosesTheFlag_AndThenNeedsDialogue()
    {
        var (layout, _, menu) = TestAdventure.Floor(3, 21);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 21);
        plan.Npcs.Add(Grisel(menu.Rooms.First(r => !r.IsStart && !r.IsExit && !r.IsArena).Index));
        var report = PlanValidator.Validate(layout, plan, menu);
        Assert.Contains(report.Repairs, r => r.Contains("npc_grisel") && r.Contains("ritual"));
        Assert.DoesNotContain(plan.Npcs, n => n.Id == "npc_grisel"); // no rite to lead and nothing to say
    }

    [Fact]
    public void TheRitualist_TakesTheCastMembersNameAndId_AndIsNotPlacedTwice()
    {
        var (layout, menu, plan) = RitualFloor();
        plan.Npcs.Add(Grisel(menu.Rooms.First(r => r.IsShrine).Index));
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        var contents = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 3, 5);
        Assert.NotNull(contents.Objective?.Ritualist);
        Assert.Equal("npc_grisel", contents.Objective!.Ritualist!.Id);
        Assert.Equal("Grisel Thorn", contents.Objective.Ritualist.Name);
        Assert.DoesNotContain(contents.Npcs, n => n.Id == "npc_grisel");
        Assert.DoesNotContain(contents.Dropped, d => d.Contains("npc_grisel"));
    }

    [Fact]
    public void WithoutACastRitualist_TheStockNamesStillServe()
    {
        var (layout, menu, plan) = RitualFloor();
        var contents = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 3, 5);
        Assert.Equal("ritualist", contents.Objective?.Ritualist?.Id);
        Assert.False(string.IsNullOrEmpty(contents.Objective?.Ritualist?.Name));
    }

    [Fact]
    public void ABoss_DiesWithDifferentWords_WhenTheFlagIsSet()
    {
        var boss = new BossPlan { Name = "The Verger", DeathLine = new Line("Hush."), AltDeathFlag = "wake_gorm", AltDeathLine = new Line("He's saying thank you.") };
        Assert.Equal("Hush.", boss.DeathLineFor(Array.Empty<string>()).Text);
        Assert.Equal("He's saying thank you.", boss.DeathLineFor(new[] { "wake_gorm" }).Text);
        Assert.Equal("Hush.", new BossPlan { DeathLine = new Line("Hush."), AltDeathLine = new Line("x") }.DeathLineFor(new[] { "wake_gorm" }).Text); // no flag named: the twin never plays
    }

    [Fact]
    public void TheAltDeathLine_SurvivesTheFileFormat_AndIsNotAskedOfTheDesigner()
    {
        var boss = new BossPlan { Name = "The Verger", AltDeathFlag = "wake_gorm", AltDeathLine = new Line("thanks") { VoiceId = "abcd" } };
        var back = PlanFormat.Deserialize<BossPlan>(PlanFormat.Serialize(boss));
        Assert.Equal("wake_gorm", back.AltDeathFlag);
        Assert.Equal("thanks", back.AltDeathLine?.Text);
        Assert.Equal("abcd", back.AltDeathLine?.VoiceId);
        Assert.Null(PlanFormat.Deserialize<BossPlan>("{\"Name\":\"x\"}").AltDeathLine);
        var schema = PlanSchemas.For(typeof(BossPlan));
        Assert.DoesNotContain("AltDeath", schema);
        Assert.DoesNotContain("Ritualist", PlanSchemas.For(typeof(NpcPlan)));
    }
}
