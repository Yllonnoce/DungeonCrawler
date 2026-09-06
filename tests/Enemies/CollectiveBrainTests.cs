using DungeonCrawler.Adventure;
using DungeonCrawler.Enemies.Collectives;
using Xunit;

namespace DungeonCrawler.Tests.Enemies;

public class CollectiveBrainTests
{
    private static MemberView M(int ordinal, float distToPost = 0f, float distToParty = 30f, bool sees = false, bool engaged = false, float speed = 3f, bool alive = true)
        => new(ordinal, alive, 1f, distToPost, distToParty, sees, engaged, speed);

    private static CollectiveContext Ctx(GoalKind goal, AlertLevel alert = AlertLevel.Calm, params MemberView[] members)
        => new() { Goal = goal, Alert = alert, Members = members, InitialCount = members.Length };

    [Fact]
    public void Calm_Guards_HoldPost_And_Patrols_Walk()
    {
        var guard = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Calm, M(0), M(1, distToPost: 6f)));
        Assert.Equal(AlertLevel.Calm, guard.Alert);
        Assert.Equal(OrderKind.HoldPost, guard.For(0));
        Assert.Equal(OrderKind.ReturnHome, guard.For(1));
        var patrol = CollectiveBrain.Decide(Ctx(GoalKind.Patrol, AlertLevel.Calm, M(0, distToPost: 9f)));
        Assert.Equal(OrderKind.Patrol, patrol.For(0));
    }

    [Fact]
    public void Contact_Alarms_TheWholeGroup()
    {
        var orders = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Calm, M(0, sees: true, distToParty: 8f), M(1)));
        Assert.Equal(AlertLevel.Alarmed, orders.Alert);
        Assert.Equal(OrderKind.Chase, orders.For(0));
        Assert.Equal(OrderKind.Chase, orders.For(1));
        Assert.False(orders.RaiseAlarm);
    }

    [Fact]
    public void Guard_BeyondLeash_ReturnsHome_UnlessInMelee()
    {
        var far = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Alarmed, M(0, distToPost: 26f, distToParty: 9f, sees: true)));
        Assert.Equal(OrderKind.ReturnHome, far.For(0));
        var melee = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Alarmed, M(0, distToPost: 26f, distToParty: 2f, sees: true)));
        Assert.Equal(OrderKind.Chase, melee.For(0));
    }

    [Fact]
    public void Alarm_CoolsDown_AfterQuiet()
    {
        var still = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Alarmed, M(0)) with { SecondsSinceContact = 5f });
        Assert.Equal(AlertLevel.Alarmed, still.Alert);
        var calm = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Alarmed, M(0)) with { SecondsSinceContact = 35f });
        Assert.Equal(AlertLevel.Calm, calm.Alert);
        Assert.Equal(OrderKind.HoldPost, calm.For(0));
    }

    [Fact]
    public void Defend_Intercepts_NearObjective_ElseFallsBack()
    {
        var near = CollectiveBrain.Decide(Ctx(GoalKind.Defend, AlertLevel.Alarmed, M(0, distToPost: 1f, sees: true)) with { HasObjective = true, PartyDistToObjective = 6f });
        Assert.Equal(OrderKind.Chase, near.For(0));
        var pulled = CollectiveBrain.Decide(Ctx(GoalKind.Defend, AlertLevel.Alarmed, M(0, distToPost: 15f, sees: true), M(1, distToPost: 1f)) with { HasObjective = true, PartyDistToObjective = 25f });
        Assert.Equal(OrderKind.FallBack, pulled.For(0));
        Assert.Equal(OrderKind.HoldPost, pulled.For(1));
    }

    [Fact]
    public void Alarm_SendsFastestRunner_Once()
    {
        var ctx = Ctx(GoalKind.Alarm, AlertLevel.Calm, M(0, sees: true, speed: 3f), M(1, speed: 5f), M(2, speed: 5f, distToParty: 40f));
        var orders = CollectiveBrain.Decide(ctx);
        Assert.True(orders.RaiseAlarm);
        Assert.Equal(OrderKind.RunAlarm, orders.For(2)); // fastest, and farther from the party than the other fast one
        Assert.Equal(OrderKind.Chase, orders.For(0));
        Assert.Equal(OrderKind.Chase, orders.For(1));
        var again = CollectiveBrain.Decide(ctx with { AlarmRaised = true });
        Assert.False(again.RaiseAlarm);
        Assert.DoesNotContain(again.Members, m => m.Kind == OrderKind.RunAlarm);
    }

    [Fact]
    public void Suspicious_Investigates_ButDefendersStay()
    {
        var guard = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Suspicious, M(0)) with { SecondsSinceContact = 1f });
        Assert.Equal(AlertLevel.Suspicious, guard.Alert);
        Assert.Equal(OrderKind.Investigate, guard.For(0));
        var defend = CollectiveBrain.Decide(Ctx(GoalKind.Defend, AlertLevel.Suspicious, M(0, distToPost: 0.5f)) with { SecondsSinceContact = 1f, HasObjective = true });
        Assert.Equal(OrderKind.HoldPost, defend.For(0));
    }

    [Fact]
    public void HalfDead_Group_Regroups_Once_WhenPartyHealthy()
    {
        var ctx = Ctx(GoalKind.Guard, AlertLevel.Alarmed, M(0, sees: true), M(1, alive: false), M(2, alive: false), M(3, alive: false)) with { HasRally = true, PartyHpFraction = 0.9f };
        var orders = CollectiveBrain.Decide(ctx);
        Assert.True(orders.Regroup);
        Assert.Equal(OrderKind.Regroup, orders.For(0));
        Assert.Single(orders.Members);
        Assert.False(CollectiveBrain.Decide(ctx with { Regrouped = true }).Regroup);
        Assert.False(CollectiveBrain.Decide(ctx with { PartyHpFraction = 0.3f }).Regroup);
        Assert.False(CollectiveBrain.Decide(ctx with { HasRally = false }).Regroup);
        Assert.False(CollectiveBrain.Decide(ctx with { Members = new[] { M(0, sees: true), M(1, sees: true), M(2, alive: false), M(3) }, InitialCount = 4 }).Regroup);
    }

    [Fact]
    public void Alarmed_WithoutEyesOnParty_SettlesBack()
    {
        var orders = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Alarmed, M(0, distToPost: 8f), M(1)) with { SecondsSinceContact = 3f });
        Assert.Equal(AlertLevel.Alarmed, orders.Alert);
        Assert.Equal(OrderKind.ReturnHome, orders.For(0));
        Assert.Equal(OrderKind.HoldPost, orders.For(1));
    }

    [Fact]
    public void DeadMembers_GetNoOrders()
    {
        var orders = CollectiveBrain.Decide(Ctx(GoalKind.Guard, AlertLevel.Calm, M(0, alive: false), M(1)));
        Assert.Single(orders.Members);
        Assert.Equal(1, orders.Members[0].Ordinal);
    }
}
