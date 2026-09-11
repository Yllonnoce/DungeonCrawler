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
