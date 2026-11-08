// TEMPORARY: stubs for the hooks the items-and-builds plan provides in src/Party/PartyController.Items.cs; the orchestrator deletes this file at merge.
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;

namespace DungeonCrawler.Party;

public partial class PartyController
{
    /// <summary>Whether this member can raise a guard (the class, or an item that lends a shield).</summary>
    public bool CanGuard(CharacterState m) => m.Class.CanBlock;
    /// <summary>Seconds added to the parry window.</summary>
    public float ParryWindowBonus(CharacterState m) => 0f;
    /// <summary>A member who cannot be staggered or stunned by a blow.</summary>
    public bool StaggerImmune(CharacterState m) => false;
    /// <summary>A heavy blow also stuns the target this long when &gt; 0.</summary>
    public float HeavyStunSeconds(CharacterState m) => 0f;
    /// <summary>A Shatter also stuns the target this long when &gt; 0.</summary>
    public float ShatterStunSeconds(CharacterState m) => 0f;
    /// <summary>&gt; 0: a perfect blow also hits every enemy within 2.5 m for that fraction of its damage.</summary>
    public float PerfectBlowCleave(CharacterState m) => 0f;
    /// <summary>Multiplier on the sneak-attack multiplier, 1 when none.</summary>
    public float SneakBonus(CharacterState m, ICombatTarget target) => 1f;
    /// <summary>Arrow hits count as heavy for Shatter.</summary>
    public bool ArrowsShatter(CharacterState m) => false;
}
