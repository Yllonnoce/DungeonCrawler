using DungeonCrawler.Characters;

namespace DungeonCrawler.Combat;

/// <summary>Anything the party can attack. Implemented by Enemy; bosses and breakables later.</summary>
public interface ICombatTarget
{
    string DisplayName { get; }
    bool IsAlive { get; }
    /// <summary>True while the target has not noticed the party (enables sneak attacks).</summary>
    bool IsUnaware { get; }
    /// <summary>False for props (crates, barrels): breaking one is not a kill, so no bark, no bloodlust, no scavenged arrows.</summary>
    bool IsCreature => true;
    DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName);
    void ApplyStatus(StatusApplication status);
}
