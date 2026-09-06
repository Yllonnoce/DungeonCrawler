using DungeonCrawler.Adventure;

namespace DungeonCrawler.Enemies.Collectives;

public enum AlertLevel { Calm, Suspicious, Alarmed }

/// <summary>What one member should be doing. The Godot-side states read this every tick.</summary>
public enum OrderKind { HoldPost, ReturnHome, Chase, FallBack, Patrol, RunAlarm, Regroup, Investigate }

/// <summary>What the group knows about one member this tick. DistToPost is to the member's own post (Defend: the objective).</summary>
public sealed record MemberView(int Ordinal, bool Alive, float HpFraction, float DistToPost, float DistToParty, bool SeesParty, bool Engaged, float Speed);

/// <summary>Snapshot handed to the brain. Built by the Godot layer; plain so decisions are unit-testable.</summary>
public sealed record CollectiveContext
{
    public GoalKind Goal { get; init; } = GoalKind.Guard;
    public AlertLevel Alert { get; init; } = AlertLevel.Calm;
    public IReadOnlyList<MemberView> Members { get; init; } = Array.Empty<MemberView>();
    public int InitialCount { get; init; }
    public float SecondsSinceContact { get; init; }
    public bool AlarmRaised { get; init; }
    /// <summary>Ordinal of the member already running for help, so the choice does not flip between ticks.</summary>
    public int? Runner { get; init; }
    public bool Regrouped { get; init; }
    public bool HasRally { get; init; }
    public bool HasObjective { get; init; }
    /// <summary>Nearest living party member to the objective (Defend); huge when there is none.</summary>
    public float PartyDistToObjective { get; init; } = float.MaxValue;
    public float PartyHpFraction { get; init; } = 1f;
    public float LeashRadius { get; init; } = 20f;
    public float InterceptRadius { get; init; } = 10f;
    public float HoverRadius { get; init; } = 3f;
    public float CalmAfterSeconds { get; init; } = 30f;
}

public sealed record MemberOrder(int Ordinal, OrderKind Kind);

public sealed record CollectiveOrders(AlertLevel Alert, IReadOnlyList<MemberOrder> Members, bool RaiseAlarm, bool Regroup)
{
    public OrderKind For(int ordinal) => Members.FirstOrDefault(m => m.Ordinal == ordinal)?.Kind ?? OrderKind.HoldPost;
}

/// <summary>
/// Group-level decisions for an enemy collective: alert level, whether to send a runner or fall back to the rally
/// point, and one order per living member according to the group's goal.
/// </summary>
public static class CollectiveBrain
{
    public static CollectiveOrders Decide(CollectiveContext ctx)
    {
        var alive = ctx.Members.Where(m => m.Alive).ToList();
        var contact = alive.Any(m => m.SeesParty || m.Engaged);
        var alert = contact ? AlertLevel.Alarmed : ctx.Alert;
        if (!contact && alert != AlertLevel.Calm && ctx.SecondsSinceContact >= ctx.CalmAfterSeconds) alert = AlertLevel.Calm;

        var orders = new List<MemberOrder>();
        if (alert == AlertLevel.Alarmed && !ctx.Regrouped && ctx.HasRally && ctx.InitialCount >= 3 && alive.Count > 0
            && alive.Count * 2 <= ctx.InitialCount && ctx.PartyHpFraction > 0.5f)
        {
            foreach (var m in alive) orders.Add(new MemberOrder(m.Ordinal, OrderKind.Regroup));
            return new CollectiveOrders(alert, orders, false, true);
        }

        int? runner = null;
        if (ctx.Goal == GoalKind.Alarm && alert == AlertLevel.Alarmed && !ctx.AlarmRaised && alive.Count > 0)
            runner = ctx.Runner is { } r && alive.Any(m => m.Ordinal == r) ? r
                : alive.OrderByDescending(m => m.Speed).ThenByDescending(m => m.DistToParty).First().Ordinal;

        foreach (var m in alive)
            orders.Add(new MemberOrder(m.Ordinal, m.Ordinal == runner ? OrderKind.RunAlarm : Order(ctx, alert, m, contact)));
        return new CollectiveOrders(alert, orders, runner != null, false);
    }

    private static OrderKind Order(CollectiveContext ctx, AlertLevel alert, MemberView m, bool contact)
    {
        switch (alert)
        {
            case AlertLevel.Calm:
                if (ctx.Goal == GoalKind.Patrol) return OrderKind.Patrol;
                return m.DistToPost > ctx.HoverRadius ? OrderKind.ReturnHome : OrderKind.HoldPost;
            case AlertLevel.Suspicious:
                if (ctx.Goal == GoalKind.Defend) return m.DistToPost > ctx.HoverRadius ? OrderKind.ReturnHome : OrderKind.HoldPost;
                return OrderKind.Investigate;
            default:
                switch (ctx.Goal)
                {
                    case GoalKind.Defend:
                        if (ctx.PartyDistToObjective <= ctx.InterceptRadius) return OrderKind.Chase;
                        return m.DistToPost > ctx.HoverRadius ? OrderKind.FallBack : OrderKind.HoldPost;
                    case var _ when !contact:
                        // alarmed but nobody has eyes on the party any more: settle back rather than chase blindly
                        return m.DistToPost > ctx.HoverRadius ? OrderKind.ReturnHome : OrderKind.HoldPost;
                    case GoalKind.Patrol:
                        return m.DistToPost > ctx.LeashRadius * 1.5f && !(m.SeesParty && m.DistToParty < 4f) ? OrderKind.ReturnHome : OrderKind.Chase;
                    default: // Guard, Alarm
                        return m.DistToPost > ctx.LeashRadius && !(m.SeesParty && m.DistToParty < 4f) ? OrderKind.ReturnHome : OrderKind.Chase;
                }
        }
    }
}
