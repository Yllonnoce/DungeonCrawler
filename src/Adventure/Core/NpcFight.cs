using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Numbers for a fighting NPC (<see cref="NpcMode.Fighter"/>): a friendly bruiser who goes for enemies that come near
/// and swings in the same windup/recover rhythm enemies use. Engine-free so the tests can compile it.
/// </summary>
public static class NpcFight
{
    /// <summary>An alive enemy within this (metres) is engaged.</summary>
    public const float SightRange = 9f;
    public const float AttackRange = 2.3f;
    public const float Windup = 0.5f;
    public const float Recover = 1.0f;
    /// <summary>A fighter has this many times a plain NPC's hit points.</summary>
    public const float HpMultiplier = 3f;

    /// <summary>How often a fighter says yes when asked to join the party. Each ask rolls again.</summary>
    public const int JoinChancePercent = 70;
    public static bool Decides(ulong seed, string npcId, int ask) => (int)(Core.Rng.StableHash($"{seed}:{npcId}:join:{ask}") % 100) < JoinChancePercent;

    public static readonly Stats Stats = Stats.FromValues(new Dictionary<Stat, float>
    {
        [Stat.Accuracy] = 72, [Stat.Evasion] = 10, [Stat.Armor] = 6,
        [Stat.CritChance] = 0.05f, [Stat.CritMultiplier] = 1.5f, [Stat.MoveSpeed] = 1, [Stat.AttackSpeed] = 1,
    });

    /// <summary>A heavy melee blow that keeps pace with the floor's enemies.</summary>
    public static AttackProfile AttackFor(int floor) => new() { MinDamage = 8f * TierRules.DamageScale(floor), MaxDamage = 14f * TierRules.DamageScale(floor) };
}
