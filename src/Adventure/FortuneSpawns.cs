using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Fortune's named creatures: the mimic's brute and the bound chest's hunter. The affix work is a seam for the elites plan.
/// </summary>
public static class FortuneSpawns
{
    /// <summary>A base profile made an elite by name and affixes.</summary>
    // TEMPORARY until elites-and-bosses lands: then call EliteRules.Apply (EnemyProfile.Affixes / EliteName) instead of scaling by hand.
    public static EnemyProfile Elite(DungeonBuilder builder, EnemyProfile baseProfile, string name, string[] affixNames)
    {
        var p = baseProfile with { Name = name };
        foreach (var affix in affixNames)
            p = affix switch
            {
                "Hulking" => p with { MaxHp = p.MaxHp * 2.2f, Scale = p.Scale * 1.3f, MinDamage = p.MinDamage * 1.25f, MaxDamage = p.MaxDamage * 1.25f },
                "Swift" => p with { MoveSpeed = p.MoveSpeed * 1.35f },
                _ => p, // Vampiric and the rest: nothing until elites' EliteRules.Apply
            };
        return p;
    }

    /// <summary>Spawns the pool creature <paramref name="id"/> as the named elite at <paramref name="at"/>; awake when <paramref name="aggro"/>. Null when the id is unknown or no floor stands.</summary>
    public static Enemy? Spawn(DungeonBuilder builder, string id, Vector3 at, int floor, string name, string[] affixes, bool aggro, float xpFactor = 1f)
    {
        if (builder.EnemyProfile(id) is not { } baseProfile) return null;
        var profile = Elite(builder, baseProfile, name, affixes) with { Xp = (int)MathF.Round(baseProfile.Xp * xpFactor) };
        var enemy = builder.SpawnEnemy(id, at, floor, profile: profile);
        if (enemy == null) return null;
        enemy.NameOverride = name;
        if (aggro) enemy.ForceAggro();
        return enemy;
    }
}
