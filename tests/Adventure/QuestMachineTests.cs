using DungeonCrawler.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class QuestMachineTests
{
    private static FloorPlan PlanWith(QuestPlan q)
    {
        var plan = new FloorPlan { Floor = 3 };
        plan.Rooms.Add(new RoomPlan { Index = 4, Name = "Sunken Cell" });
        plan.Collectives.Add(new CollectivePlan { Id = "wolves", Name = "Grey Pack", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 3 } } });
        plan.Npcs.Add(new NpcPlan { Id = "hermit", Name = "Old Teodor", Room = 2 });
        plan.Quests.Add(q);
        return plan;
    }

    private static QuestState Kill()
    {
        var q = new QuestPlan { Id = "q", Template = QuestTemplate.KillTarget, GiverNpc = "hermit", Params = new QuestParams { TargetCollective = "wolves" }, Reward = new QuestReward { Xp = 100 } };
        return QuestMachine.Create(q, PlanWith(q), 3, 3);
    }

    [Fact]
    public void Create_FillsNamesAndGoal()
    {
        var q = Kill();
        Assert.Equal(QuestStatus.Offered, q.Status);
        Assert.Equal("Old Teodor", q.GiverName);
        Assert.Equal("Grey Pack", q.TargetName);
        Assert.Equal(3, q.Goal);
        Assert.Contains("Old Teodor", q.ObjectiveText);
    }

    [Fact]
    public void KillTarget_CountsOnlyItsCollective_ThenTurnsIn()
    {
        var q = Kill();
        Assert.False(QuestMachine.Apply(q, new EnemyDiedEvent("wolves", "enemy.ghoul", 3)), "kills before accepting do not count");
        Assert.True(QuestMachine.Apply(q, new QuestAccepted("q")));
        Assert.Equal(QuestStatus.Active, q.Status);
        Assert.False(QuestMachine.Apply(q, new EnemyDiedEvent("bats", "enemy.ghoul", 3)));
        Assert.False(QuestMachine.Apply(q, new EnemyDiedEvent("wolves", "enemy.ghoul", 4)), "other floors do not count");
        Assert.False(QuestMachine.Apply(q, new EnemyDiedEvent(null, "enemy.ghoul", 3)));
        for (var i = 0; i < 3; i++) Assert.True(QuestMachine.Apply(q, new EnemyDiedEvent("wolves", "enemy.ghoul", 3)));
        Assert.Equal(QuestStatus.ReadyToTurnIn, q.Status);
        Assert.Contains("Return to Old Teodor", q.ObjectiveText);
        Assert.False(QuestMachine.Apply(q, new QuestTurnedIn("other")));
        Assert.True(QuestMachine.Apply(q, new QuestTurnedIn("q")));
        Assert.Equal(QuestStatus.Completed, q.Status);
        Assert.False(QuestMachine.Apply(q, new NpcDied("hermit")), "terminal quests ignore events");
    }

    [Fact]
    public void Decline_KeepsItOffered()
    {
        var q = Kill();
        Assert.False(QuestMachine.Apply(q, new QuestDeclined("q")));
        Assert.Equal(QuestStatus.Offered, q.Status);
        Assert.True(QuestMachine.Apply(q, new QuestAccepted("q")));
    }

    [Fact]
    public void Recover_CompletesOnPickup_ThenTurnIn()
    {
        var plan = new QuestPlan { Id = "r", Template = QuestTemplate.Recover, GiverNpc = "hermit", Params = new QuestParams { ItemRoom = 4, ItemName = "Iron Psalter" } };
        var q = QuestMachine.Create(plan, PlanWith(plan), 3, 0);
        Assert.Equal("Sunken Cell", q.PlaceName);
        QuestMachine.Apply(q, new QuestAccepted("r"));
        Assert.Contains("Iron Psalter", q.ObjectiveText);
        Assert.False(QuestMachine.Apply(q, new QuestItemPicked("other")));
        Assert.True(QuestMachine.Apply(q, new QuestItemPicked("r")));
        Assert.Equal(QuestStatus.ReadyToTurnIn, q.Status);
        Assert.True(QuestMachine.Apply(q, new QuestTurnedIn("r")));
        Assert.Equal(QuestStatus.Completed, q.Status);
    }

    [Fact]
    public void Escort_CompletesAtStairs_FailsOnDeathOrLeaving()
    {
        var plan = new QuestPlan { Id = "e", Template = QuestTemplate.Escort, GiverNpc = "hermit", Params = new QuestParams { EscortNpc = "hermit" } };
        var q = QuestMachine.Create(plan, PlanWith(plan), 3, 0);
        QuestMachine.Apply(q, new QuestAccepted("e"));
        Assert.False(QuestMachine.Apply(q, new NpcReachedStairs("someone")));
        Assert.True(QuestMachine.Apply(q, new NpcReachedStairs("hermit")));
        Assert.Equal(QuestStatus.Completed, q.Status);

        var dead = QuestMachine.Create(plan, PlanWith(plan), 3, 0);
        QuestMachine.Apply(dead, new QuestAccepted("e"));
        Assert.True(QuestMachine.Apply(dead, new NpcDied("hermit")));
        Assert.Equal(QuestStatus.Failed, dead.Status);

        var left = QuestMachine.Create(plan, PlanWith(plan), 3, 0);
        QuestMachine.Apply(left, new QuestAccepted("e"));
        Assert.False(QuestMachine.Apply(left, new FloorLeft(2)));
        Assert.True(QuestMachine.Apply(left, new FloorLeft(3)));
        Assert.Equal(QuestStatus.Failed, left.Status);
    }

    [Fact]
    public void HoldOut_CountsWaves_AndFailsIfGiverDies()
    {
        var plan = new QuestPlan { Id = "h", Template = QuestTemplate.HoldOut, GiverNpc = "hermit", Params = new QuestParams { WaveCollective = "wolves", Waves = 2 } };
        var q = QuestMachine.Create(plan, PlanWith(plan), 3, 0);
        Assert.Equal(2, q.Goal);
        QuestMachine.Apply(q, new QuestAccepted("h"));
        Assert.True(QuestMachine.Apply(q, new WaveCleared("h", 1)));
        Assert.Equal(QuestStatus.Active, q.Status);
        Assert.True(QuestMachine.Apply(q, new WaveCleared("h", 2)));
        Assert.Equal(QuestStatus.ReadyToTurnIn, q.Status);

        var dead = QuestMachine.Create(plan, PlanWith(plan), 3, 0);
        QuestMachine.Apply(dead, new QuestAccepted("h"));
        Assert.True(QuestMachine.Apply(dead, new NpcDied("hermit")));
        Assert.Equal(QuestStatus.Failed, dead.Status);
    }

    [Fact]
    public void KillQuest_SurvivesLeavingTheFloor()
    {
        var q = Kill();
        QuestMachine.Apply(q, new QuestAccepted("q"));
        Assert.False(QuestMachine.Apply(q, new FloorLeft(3)));
        Assert.Equal(QuestStatus.Active, q.Status);
    }

    [Fact]
    public void OfferedQuest_FailsIfGiverDies()
    {
        var q = Kill();
        Assert.True(QuestMachine.Apply(q, new NpcDied("hermit")));
        Assert.Equal(QuestStatus.Failed, q.Status);
    }
}
