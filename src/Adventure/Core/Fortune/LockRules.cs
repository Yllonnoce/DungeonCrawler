using DungeonCrawler.Dungeon;
using DungeonCrawler.Save;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>Locks that cost: a failed pick is a noise that wakes the unaware within <see cref="NoiseRadius"/> m; the third failure jams the lock (a key still opens it).</summary>
public static class LockRules
{
    public const float NoiseRadius = 10f;
    public const int JamAfterFails = 3;
    public const float RetrySeconds = 1.2f;

    /// <summary>Test hook: every pick succeeds with this chance when set.</summary>
    public static float? ForcedChance { get; set; }

    /// <summary>The pick chance: the trap rule by Dexterity, plus Light Fingers; nothing at all under Butterfingers, except on a lock the floor's objective needs (<paramref name="objectiveLock"/>), which a curse must not seal for good.</summary>
    public static float Chance(float dexterity, AdventureDto? adv, bool objectiveLock = false)
    {
        if (FortuneRules.Has(adv, "butterfingers") && !objectiveLock) return 0f;
        var bonus = Boons.Has(adv, "light_fingers") ? FortuneRules.LockBonus : 0f;
        return Math.Clamp(TrapRules.LockpickChance(dexterity) + bonus, 0.05f, 0.95f);
    }
}
