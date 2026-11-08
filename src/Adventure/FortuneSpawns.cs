using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Fortune's named creatures: the mimic's brute and the bound chest's hunter. They are elites in every sense (EliteRules.Apply stamps the
/// affixes and the title, Enemy gives them the tells, the x2.5 XP, table.elite and EliteSeen/EliteSlain, the DTO carries them to clients).
/// </summary>
public static class FortuneSpawns
{
    /// <summary>A base profile made an elite by name and affix names (unknown names are dropped).</summary>
    public static EnemyProfile Elite(DungeonBuilder builder, EnemyProfile baseProfile, string name, string[] affixNames)
        => EliteRules.Apply(baseProfile with { Name = name }, new EliteRoll(EliteRules.Decode(string.Join(",", affixNames)), name));

    /// <summary>Spawns the pool creature <paramref name="id"/> as the named elite at <paramref name="at"/>; awake when <paramref name="aggro"/>. Null when the id is unknown or no floor stands. <paramref name="xpFactor"/> applies only when the affixes made no elite: an elite already pays <see cref="EliteRules.XpMultiplier"/>.</summary>
    public static Enemy? Spawn(DungeonBuilder builder, string id, Vector3 at, int floor, string name, string[] affixes, bool aggro, float xpFactor = 1f)
    {
        if (builder.EnemyProfile(id) is not { } baseProfile) return null;
        var profile = Elite(builder, baseProfile, name, affixes);
        if (!profile.IsElite) profile = profile with { Xp = (int)MathF.Round(baseProfile.Xp * xpFactor) };
        var enemy = builder.SpawnEnemy(id, at, floor, profile: profile);
        if (enemy == null) return null;
        enemy.NameOverride = name;
        if (aggro) enemy.ForceAggro();
        return enemy;
    }
}
