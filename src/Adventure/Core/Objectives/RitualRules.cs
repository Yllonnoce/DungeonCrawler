using DungeonCrawler.Dungeon;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The pacing of a Ritual floor: how long the rite takes, how often and how many creatures the breaches send, and
/// what sealing every breach buys (a slower trickle through the shrine's doorways, not silence). Engine-free.
/// </summary>
public static class RitualRules
{
    public const float MinDuration = 90f, MaxDuration = 150f;
    /// <summary>Seconds before the first wave once the rite begins.</summary>
    public const float FirstWave = 6f;
    /// <summary>Creatures let through when every mouth is sealed come this much slower.</summary>
    public const float SealedFactor = 2f;
    /// <summary>Reviving the ritualist needs no enemy this close.</summary>
    public const float ReviveClearRadius = 6f;
    public const float ReviveHpFraction = 0.35f;

    /// <summary>
    /// A revive is taken (any downed NPC's: the ritualist, a fighter, an escort, a crew member): it is down and alive, and no living hostile
    /// enemy stands nearer than <see cref="ReviveClearRadius"/> (<paramref name="nearestHostile"/>, in metres; +∞ or NaN for none). The one
    /// rule <c>Npc.TryRevive</c> refuses by and a wagon's "Revive {name}" is offered by, so the prompt and the refusal never disagree, and a refused revive never masks a move to the car (plan 15
    /// Task 0).
    /// </summary>
    public static bool ReviveTaken(bool downed, bool dead, float nearestHostile) => downed && !dead && !(nearestHostile < ReviveClearRadius);

    /// <summary>
    /// An enemy counts against a revive (its distance is what <see cref="ReviveTaken"/>'s nearest reads): alive and aware of the party. Only
    /// those two, never the AI state behind them, so a client's enemy copy, which runs no AI and reads its awareness from the server's snapshot,
    /// counts exactly as the server's (plan 15 Task 0).
    /// </summary>
    public static bool CountsAgainstRevive(bool alive, bool aware) => alive && aware;

    /// <summary>
    /// An enemy counts against a downed crew member's revive (plan 16 final fix wave, the controller's ruling on Fable I1): as
    /// <see cref="CountsAgainstRevive(bool, bool)"/>, except a boarder riding the crew member's own wagon (<paramref name="onTheirWagon"/>: carried by
    /// the wagon they are seated in) while the train is at its stop or hijacked (<paramref name="trainAtStop"/>, <see cref="TrainAtStop"/>). That
    /// boarder is a hijacker spared on the wagon's rim (plan 16 Task 6): it sits inside the clear radius of every seat, and no fight with the
    /// siege clears it. A siege creature and a boarder on another wagon still count; so does a ride's boarder on the wagon itself, which the
    /// party knocks off with its blows. The 60 s bleed-out is unchanged.
    /// </summary>
    public static bool CountsAgainstRevive(bool alive, bool aware, bool onTheirWagon, bool trainAtStop)
        => CountsAgainstRevive(alive, aware) && !(onTheirWagon && trainAtStop);

    /// <summary>
    /// The train is at its stop (held there, not halted on the line short of the next one) or hijacked, on every peer from the cart's state a
    /// client is sent (<c>CartStateDto</c>'s mode, halted flag and hijack phase). Then every boarder on a wagon is a spared hijacker: the arrival
    /// clears the ride's boarders, and the departure, which makes the train roll, turns the spared ones into the next ride's.
    /// </summary>
    public static bool TrainAtStop(bool held, bool halted, bool hijackLive) => hijackLive || (held && !halted);

    public static float Duration(int floor) => Math.Clamp(90f + 3f * floor, MinDuration, MaxDuration);

    /// <summary>Seconds between waves at this point of the rite (0..1): from 16 down to 8.</summary>
    public static float WaveInterval(float progress, bool allSealed)
    {
        var t = Math.Clamp(progress, 0f, 1f);
        var interval = 16f - 8f * t;
        return allSealed ? interval * SealedFactor : interval;
    }

    /// <summary>Creatures in a wave at this point of the rite.</summary>
    public static int WaveSize(float progress, int floor)
    {
        var t = Math.Clamp(progress, 0f, 1f);
        return 2 + floor / 5 + (int)MathF.Floor(3f * t);
    }

    /// <summary>The most wave creatures alive at once; new waves wait until there is room.</summary>
    public static int MaxAlive(int floor) => Math.Min(14, 6 + floor / 3);

    /// <summary>Whether the rite advances: begun, the ritualist up, and (optionally) the party not wiped.</summary>
    public static bool Advances(bool started, bool ritualistDowned, bool ritualistDead) => started && !ritualistDowned && !ritualistDead;

    /// <summary>How much of a wave is fodder from the breaches (the rest are the floor's own creatures).</summary>
    public const float FodderShare = 0.7f;
}
