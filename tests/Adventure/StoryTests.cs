using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class StoryQuestTests
{
    private static FloorPlan PlanWith(QuestPlan q, NpcPlan? npc = null)
    {
        var plan = new FloorPlan { Floor = 4 };
        plan.Rooms.Add(new RoomPlan { Index = 2, Name = "Cell" });
        plan.Rooms.Add(new RoomPlan { Index = 3, Name = "Hall" });
        plan.Npcs.Add(npc ?? new NpcPlan { Id = "guide", Name = "Marrow", Room = 2 });
        plan.Quests.Add(q);
        return plan;
    }

    [Fact]
    public void Investigate_CountsCluesThenTurnsIn()
    {
        var plan = new QuestPlan { Id = "q", Template = QuestTemplate.Investigate, GiverNpc = "guide", Params = new QuestParams { ClueRooms = { 2, 3, 5 }, ClueName = "wax seals" } };
        var q = QuestMachine.Create(plan, PlanWith(plan), 4, 0);
        Assert.Equal(3, q.Goal);
        Assert.True(QuestMachine.Apply(q, new QuestAccepted("q")));
        Assert.Contains("wax seals", q.ObjectiveText);
        Assert.True(QuestMachine.Apply(q, new QuestItemPicked("q")));
        Assert.True(QuestMachine.Apply(q, new QuestItemPicked("q")));
        Assert.Equal(QuestStatus.Active, q.Status);
        Assert.True(QuestMachine.Apply(q, new QuestItemPicked("q")));
        Assert.Equal(QuestStatus.ReadyToTurnIn, q.Status);
        Assert.True(QuestMachine.Apply(q, new QuestTurnedIn("q")));
        Assert.Equal(QuestStatus.Completed, q.Status);
    }

    [Fact]
    public void Decide_CompletesOnTheChoice_AndRemembersIt()
    {
        var plan = new QuestPlan { Id = "q", Template = QuestTemplate.Decide, GiverNpc = "guide" };
        var q = QuestMachine.Create(plan, PlanWith(plan), 4, 0);
        Assert.True(QuestMachine.Apply(q, new QuestAccepted("q")));
        Assert.Contains("Decide", q.ObjectiveText);
        Assert.False(QuestMachine.Apply(q, new ChoiceMade("other", "x")));
        Assert.True(QuestMachine.Apply(q, new ChoiceMade("q", "spared_marrow")));
        Assert.Equal(QuestStatus.Completed, q.Status);
        Assert.Equal("spared_marrow", q.ChosenFlag);
        Assert.False(QuestMachine.Apply(q, new ChoiceMade("q", "again")));
    }

    [Fact]
    public void Betrayal_CountsTheTurnedNpcsCreatures_AndIgnoresTheirDeathAsAnNpc()
    {
        var npc = new NpcPlan { Id = "guide", Name = "Marrow", Room = 2, Betrayal = new BetrayalPlan { EnemyId = "enemy.bandit", Count = 2 } };
        var plan = new QuestPlan { Id = "q", Template = QuestTemplate.Betrayal, GiverNpc = "guide" };
        var q = QuestMachine.Create(plan, PlanWith(plan, npc), 4, 0);
        Assert.Equal(2, q.Goal);
        Assert.True(QuestMachine.Apply(q, new BetrayalStarted("q")));
        Assert.Equal(QuestStatus.Active, q.Status);
        Assert.False(QuestMachine.Apply(q, new NpcDied("guide")), "the turned NPC is gone, not dead: the quest must not fail");
        Assert.False(QuestMachine.Apply(q, new EnemyDiedEvent("wolves", "enemy.bandit", 4)));
        Assert.True(QuestMachine.Apply(q, new EnemyDiedEvent(QuestMachine.BetrayalGroup("guide"), "enemy.bandit", 4)));
        Assert.Equal(QuestStatus.Active, q.Status);
        Assert.True(QuestMachine.Apply(q, new EnemyDiedEvent(QuestMachine.BetrayalGroup("guide"), "enemy.bandit", 4)));
        Assert.Equal(QuestStatus.Completed, q.Status);
    }

    [Fact]
    public void Flags_CleanAndGate()
    {
        Assert.Equal("spared_the_warden", FlagRules.Clean("  Spared the Warden! "));
        Assert.Null(FlagRules.Clean("   "));
        Assert.Equal(FlagRules.MaxLength, FlagRules.Clean(new string('a', 60))!.Length);
        var flags = new[] { "a" };
        Assert.True(FlagRules.Allowed(null, null, flags));
        Assert.True(FlagRules.Allowed("a", null, flags));
        Assert.False(FlagRules.Allowed("b", null, flags));
        Assert.False(FlagRules.Allowed(null, "a", flags));
        Assert.Equal("4.q1", QuestKeys.QuestOf(QuestKeys.Item(4, "q1", 2)));
    }

    [Fact]
    public void MemorySummary_ListsFlagsAndChoices()
    {
        var memory = new CampaignMemoryDto { Flags = { "spared_marrow" } };
        var r = memory.For(2); r.Completed = true; r.Kills = 3; r.Seconds = 200; r.Choices.Add("spared_marrow (with Marrow on floor 2)");
        var text = MemorySummary.Build(memory, 3);
        Assert.Contains("spared_marrow", text);
        Assert.Contains("Choices and discoveries", text);
    }
}

public class VoiceBudgetTests
{
    private static (CampaignPlan, Dictionary<int, FloorPlan>) Campaign()
    {
        var c = new CampaignPlan();
        for (var t = 0; t < 4; t++) c.Tiers.Add(new TierPlan { Index = t, EnterLine = new Line($"tier {t}") });
        for (var f = 1; f <= 20; f++) c.Floors.Add(new FloorOutline { Floor = f, Story = f % 5 == 2 ? "beat" : "" });
        c.Barks.Add(new BarkSet { Member = "Brand", Lines = { new BarkLine { Line = new Line("Down.") }, new BarkLine { Line = new Line("Agh!") } } });
        var floors = new Dictionary<int, FloorPlan>();
        for (var f = 1; f <= 20; f++)
        {
            var p = new FloorPlan { Floor = f, IntroLine = new Line($"intro {f}") };
            if (f % 5 == 0) p.Boss = new BossPlan { IntroLine = new Line($"boss {f}"), DeathLine = new Line($"dies {f}") };
            if (f % 5 == 2)
            {
                var npc = new NpcPlan { Id = "n", Name = $"Cast{f}" };
                npc.Script.Nodes.Add(new DialogueNode { Id = "a", Line = new Line($"hello {f}") });
                npc.Script.Nodes.Add(new DialogueNode { Id = "b", Line = new Line($"more {f}") });
                p.Npcs.Add(npc);
                p.Quests.Add(new QuestPlan { Id = "q", Lines = new QuestLines { Offer = new Line($"offer {f}"), Complete = new Line($"done {f}"), Accept = new Line("ok"), Decline = new Line("no"), Fail = new Line("bad") } });
            }
            p.Hordes.Add(new HordePlan { Id = "h", Line = new Line($"horde {f}") });
            floors[f] = p;
        }
        return (c, floors);
    }

    [Fact]
    public void Select_PrefersTiersBossesAndStoryFloors_AndStopsAtTheCap()
    {
        var (c, floors) = Campaign();
        var picks = VoiceBudget.Select(c, floors, 30);
        Assert.Equal(30, picks.Count);
        Assert.Equal(4, picks.Count(p => p.Reason == "tier entrance"));
        Assert.Equal(8, picks.Count(p => p.Reason is "boss intro" or "boss death"));
        Assert.Equal(4, picks.Count(p => p.Reason == "story intro"));
        Assert.True(picks.Count(p => p.Reason.StartsWith("Cast")) == 8, "every story NPC line comes before any quiet floor");
        Assert.DoesNotContain(picks, p => p.Reason.StartsWith("bark"));
        Assert.DoesNotContain(picks, p => p.Reason == "horde");
        var all = VoiceBudget.Candidates(c, floors);
        Assert.True(all.Count > 60);
        Assert.Contains("30 of", VoiceBudget.Describe(picks, all.Count));
    }

    [Fact]
    public void Select_SkipsEmptyAndDuplicateLines()
    {
        var c = new CampaignPlan();
        var shared = new Line("same");
        c.Tiers.Add(new TierPlan { Index = 0, EnterLine = shared });
        var floors = new Dictionary<int, FloorPlan> { [1] = new FloorPlan { Floor = 1, IntroLine = new Line("") }, [2] = new FloorPlan { Floor = 2, IntroLine = shared } };
        var picks = VoiceBudget.Select(c, floors, 10);
        Assert.Single(picks);
    }
}
