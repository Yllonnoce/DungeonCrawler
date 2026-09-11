namespace DungeonCrawler.Adventure.Objectives;

public enum PrisonerState { Wandering, Captured, Jailed, Freed }

/// <summary>A room as a prisoner sees it: how many creatures stand in it, how far it is from them and from the nearest creature.</summary>
public sealed record RoomView(int Index, int Enemies, int HopsFromMe, int HopsFromNearestThreat, bool IsJail);

public sealed record PrisonerContext(PrisonerState State, int CurrentRoom, float NearestThreat, IReadOnlyList<RoomView> Rooms, float TimeInRoom);

public abstract record PrisonerDecision;
public sealed record Flee(int Room) : PrisonerDecision;
public sealed record WanderTo(int Room) : PrisonerDecision;
public sealed record Wait : PrisonerDecision;
public sealed record FollowJailer : PrisonerDecision;

/// <summary>
/// What a villager does on a Jailbreak floor: keep away from the creatures, wander when it is quiet, follow the jailer
/// once caught, and stay put behind bars. Engine-free; the controller turns a decision into a walk.
/// </summary>
public static class PrisonerBrain
{
    /// <summary>A creature this close sends the prisoner running.</summary>
    public const float FleeRange = 9f;
    /// <summary>Seconds in one room before a quiet prisoner drifts to another.</summary>
    public const float WanderEvery = 8f;

    public static PrisonerDecision Decide(PrisonerContext ctx, Random rng)
    {
        switch (ctx.State)
        {
            case PrisonerState.Captured: return new FollowJailer();
            case PrisonerState.Jailed:
            case PrisonerState.Freed: return new Wait();
        }
        if (ctx.NearestThreat < FleeRange)
        {
            // the emptiest room farthest from the nearest creature, never the jail, never where we stand
            var refuge = ctx.Rooms.Where(r => r.Index != ctx.CurrentRoom && !r.IsJail && r.Enemies == 0 && r.HopsFromMe >= 0)
                .OrderByDescending(r => r.HopsFromNearestThreat).ThenBy(r => r.HopsFromMe).ThenBy(r => r.Index).FirstOrDefault();
            refuge ??= ctx.Rooms.Where(r => r.Index != ctx.CurrentRoom && !r.IsJail && r.HopsFromMe >= 0)
                .OrderBy(r => r.Enemies).ThenByDescending(r => r.HopsFromNearestThreat).ThenBy(r => r.Index).FirstOrDefault();
            return refuge == null ? new Wait() : new Flee(refuge.Index);
        }
        if (ctx.TimeInRoom >= WanderEvery)
        {
            var near = ctx.Rooms.Where(r => r.Index != ctx.CurrentRoom && !r.IsJail && r.Enemies == 0 && r.HopsFromMe is >= 1 and <= 3).ToList();
            if (near.Count > 0) return new WanderTo(near[rng.Next(near.Count)].Index);
        }
        return new Wait();
    }
}

public abstract record PrisonerEvent;
public sealed record PrisonerCaught : PrisonerEvent;
public sealed record PrisonerDelivered : PrisonerEvent;
public sealed record JailerLost : PrisonerEvent;
public sealed record PrisonerFreed : PrisonerEvent;

/// <summary>The prisoner's state machine and the objective's arithmetic. Engine-free.</summary>
public static class JailbreakRules
{
    public const int MinPrisoners = 3, MaxPrisoners = 5;
    /// <summary>A creature this close to a wandering prisoner has caught them.</summary>
    public const float CaptureRange = 1.6f;
    /// <summary>The jailer has delivered its captive when this close to the jail's middle.</summary>
    public const float DeliverRange = 1.8f;

    public static PrisonerState Apply(PrisonerState state, PrisonerEvent e) => (state, e) switch
    {
        (PrisonerState.Wandering, PrisonerCaught) => PrisonerState.Captured,
        (PrisonerState.Captured, PrisonerDelivered) => PrisonerState.Jailed,
        (PrisonerState.Captured, JailerLost) => PrisonerState.Wandering,
        (PrisonerState.Jailed, PrisonerFreed) => PrisonerState.Freed,
        (PrisonerState.Wandering, PrisonerFreed) => PrisonerState.Freed,
        _ => state, // Freed is for keeps; anything else is a no-op
    };

    public static bool Completed(IEnumerable<PrisonerState> states) => states.Any() && states.All(s => s == PrisonerState.Freed);

    public static string HudText(IReadOnlyList<PrisonerState> states)
    {
        var freed = states.Count(s => s == PrisonerState.Freed);
        var jailed = states.Count(s => s == PrisonerState.Jailed);
        var taken = states.Count(s => s == PrisonerState.Captured);
        var parts = new List<string> { $"Prisoners freed {freed}/{states.Count}" };
        if (jailed > 0) parts.Add($"{jailed} in the jail");
        if (taken > 0) parts.Add($"{taken} being taken");
        return string.Join("; ", parts) + ".";
    }

    public static int JailedAtStart(int prisoners, Random rng) => Math.Min(prisoners - 1, 1 + rng.Next(2));
    public static int XpForFreeing(int floor) => 20 + 4 * floor;
}
