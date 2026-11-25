namespace DungeonCrawler.Enemies;

/// <summary>
/// The enemy tick diet (plan 16, Ruling 20): a still, unaware or holding creature beyond every hero and beyond its own
/// sight runs its AI state at a quarter rate, staggered by its net id, with the time it banked meanwhile. Everything else
/// (water, unstick, animation, effects, tint, dying) still runs every tick, and a sleeper is too far to see anyone, so
/// no detection comes late.
/// </summary>
public static class TickDietRules
{
    /// <summary>Never asleep nearer than this to a hero, whatever its sight.</summary>
    public const float FarMetres = 40f;
    /// <summary>A sleeper's state runs one tick in this many.</summary>
    public const int Every = 4;
    /// <summary>Horizontal speed (m/s) under which a creature counts as standing still.</summary>
    public const float StillSpeed = 0.1f;
    /// <summary>Seconds after a hurt during which a creature stays awake.</summary>
    public const float HurtAwake = 1f;

    /// <summary>
    /// Whether the creature sleeps this tick: unaware or holding, still, not carried or leaping, not hurt within
    /// <see cref="HurtAwake"/>, and farther from the nearest hero than the larger of <see cref="FarMetres"/> and
    /// <paramref name="sightReach"/> (its detect range times aggression times a wading hero's 1.3).
    /// </summary>
    public static bool Sleeps(bool unawareOrHolding, float horizontalSpeed, bool carried, bool leaping, float sinceHurt, float nearestHeroMetres, float sightReach)
        => unawareOrHolding && horizontalSpeed < StillSpeed && !carried && !leaping && sinceHurt > HurtAwake
           && nearestHeroMetres > Math.Max(FarMetres, sightReach);

    /// <summary>A sleeper's state runs on the ticks whose frame matches its net id's low bits: one sleeper in four each tick.</summary>
    public static bool RunsThisTick(int netId, ulong physicsFrame) => (ulong)(netId & (Every - 1)) == (physicsFrame & (Every - 1));
}
