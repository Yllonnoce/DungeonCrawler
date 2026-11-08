using DungeonCrawler.Core;
using DungeonCrawler.Enemies;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>
/// Mimics: from floor 4 a tenth of the ordinary chests (a fifth from floor 12) are mouths, rolled from the chest's spawn seed.
/// A member whose Perception reaches the chest reads the tell in the prompt. "The tier's brute" is the tier pool's heaviest
/// melee creature (the data's horde brutes are 34-HP swarm fodder), spawned Hulking with twice the XP; the chest yields its
/// roll when it dies. Engine-free.
/// </summary>
public static class MimicRules
{
    public const int FromFloor = 4, DeepFloor = 12;
    public const float Chance = 0.10f, DeepChance = 0.20f;
    public const int Actor = 7000;
    /// <summary>The XP factor for a brute that no affix made an elite; a Hulking mimic is an elite and pays <c>EliteRules.XpMultiplier</c> instead.</summary>
    public const float XpFactor = 2f;
    public const string Tell = "the lock is breathing";
    public const string EliteName = "Mimic";
    public static readonly string[] Affixes = { "Hulking" };

    /// <summary>Test hook: this chest spawn index is a mimic whatever the roll says.</summary>
    public static int? ForcedSpawnIndex { get; set; }

    public static float ChanceFor(int floor) => floor >= DeepFloor ? DeepChance : floor >= FromFloor ? Chance : 0f;

    /// <summary>Ordinary chests only (a vault's prize, a secret's or a ledge's reward is never a mouth), from the chest's own seed.</summary>
    public static bool IsMimic(ulong gameSeed, int floor, int spawnIndex, ChestKind kind)
    {
        if (kind != ChestKind.Ordinary) return false;
        var chance = ChanceFor(floor);
        if (chance <= 0f) return false;
        return new Random(Rng.ActorSeed(gameSeed, floor, Actor + spawnIndex)).NextDouble() < chance;
    }

    /// <summary>The pool's non-boss melee profile with the most HP (ties by id); null when the pool has none.</summary>
    public static string? BruteFor(IReadOnlyList<string> pool, Func<string, EnemyProfile?> lookup)
        => pool.Select(id => (id, p: lookup(id))).Where(t => t.p is { IsBoss: false, Kind: EnemyKind.Melee })
            .OrderByDescending(t => t.p!.MaxHp).ThenBy(t => t.id, StringComparer.Ordinal).Select(t => t.id).FirstOrDefault();
}
