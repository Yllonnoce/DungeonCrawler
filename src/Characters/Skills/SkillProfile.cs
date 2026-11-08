using DungeonCrawler.Combat;

namespace DungeonCrawler.Characters;

/// <summary>Passive effects that fire on combat events. Magnitude meaning depends on the kind.</summary>
public enum TriggerKind
{
    None,
    /// <summary>After blocking: counter-attack the attacker for Magnitude x weapon damage.</summary>
    Riposte,
    /// <summary>On a critical hit: apply Poison (Magnitude per tick, 6 s).</summary>
    VenomCrit,
    /// <summary>On a kill with a bow: recover Magnitude arrows.</summary>
    Scavenger,
    /// <summary>On a kill: restore Magnitude mana.</summary>
    SoulSiphon,
    /// <summary>On a kill: heal Magnitude HP.</summary>
    Bloodlust,
    /// <summary>When hit: the attacker takes Magnitude physical damage.</summary>
    Thorns,
    /// <summary>After blocking: restore Magnitude stamina.</summary>
    SecondWind,
    /// <summary>On a kill: Haste for Magnitude x 3 seconds.</summary>
    Momentum,
    /// <summary>Heavy blows stun for Magnitude seconds; Shatter stuns the same.</summary>
    HeavyStun,
    /// <summary>Coins picked up grow by a Magnitude fraction.</summary>
    GoldFind,
    /// <summary>Sales pay a Magnitude fraction more.</summary>
    SellBonus,
    /// <summary>Below 30% HP, once per floor: a Frost Nova bursts from you.</summary>
    LastGasp,
    /// <summary>The Lucky Dip costs x(1 - Magnitude).</summary>
    LuckyDip,
    /// <summary>A perfect blow also strikes every other enemy in the arc for Magnitude x weapon damage.</summary>
    PerfectCleave,
    /// <summary>Sneak attacks on a Frosted target deal x(1 + Magnitude).</summary>
    FrostSneak,
    /// <summary>Your kills never wake kin.</summary>
    QuietKill,
    /// <summary>The parry window grows by Magnitude seconds.</summary>
    ParryWindow,
    /// <summary>Stagger immunity for Magnitude seconds after getting up.</summary>
    IronChin,
    /// <summary>Burn you apply spreads to Magnitude neighbours within 3 m.</summary>
    BurnSpread,
    /// <summary>You cannot guard.</summary>
    NoGuard,
    /// <summary>Arrows Shatter a Frosted target on any hit.</summary>
    ArrowShatter,
}

/// <summary>Changes a skill (or a legendary) makes to one ability the character knows; <see cref="AbilityId"/> may list several, comma-separated.</summary>
public sealed record AbilityUpgrade
{
    public string AbilityId { get; init; } = "";
    /// <summary>True when this upgrade applies to the ability with that id.</summary>
    public bool Targets(string abilityId) => AbilityId.Split(',').Any(id => id.Trim() == abilityId);
    public float DamageMul { get; init; } = 1f;
    public float CooldownMul { get; init; } = 1f;
    public float CostMul { get; init; } = 1f;
    public float BuffMul { get; init; } = 1f;
    public float RadiusAdd { get; init; }
    public int ExtraProjectiles { get; init; }
    public StatusApplication? AddStatus { get; init; }
    /// <summary>Scales the duration of every status the ability's attack applies on hit.</summary>
    public float OnHitDurationMul { get; init; } = 1f;
    /// <summary>A status appended to the ability's Effects (a buff that also shields, say).</summary>
    public StatusApplication? AddEffect { get; init; }
}

/// <summary>One node in a class's skill tree. Chains inside a branch: each tier needs the previous one.</summary>
public sealed record SkillProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string ClassId { get; init; } = "";
    public string Branch { get; init; } = "";
    public int Tier { get; init; }
    public string[] Prerequisites { get; init; } = Array.Empty<string>();
    /// <summary>A duo node: learnable once any one of these is learned (an OR beside the Prerequisites AND).</summary>
    public string[] AnyOf { get; init; } = Array.Empty<string>();
    public int Cost { get; init; } = 1;
    public StatModifier[] Modifiers { get; init; } = Array.Empty<StatModifier>();
    public string GrantsAbility { get; init; } = "";
    public AbilityUpgrade? Upgrade { get; init; }
    public TriggerKind Trigger { get; init; } = TriggerKind.None;
    public float TriggerMagnitude { get; init; }
    public TriggerKind Trigger2 { get; init; } = TriggerKind.None;
    public float TriggerMagnitude2 { get; init; }
    /// <summary>Further upgrades beside <see cref="Upgrade"/>.</summary>
    public AbilityUpgrade[] Upgrades { get; init; } = Array.Empty<AbilityUpgrade>();
    public IEnumerable<AbilityUpgrade> AllUpgrades => Upgrade is null ? Upgrades : Upgrades.Prepend(Upgrade);
    public bool IsDuo => AnyOf.Length > 0;
    /// <summary>Tier 6: the branch's end, which trades something.</summary>
    public bool IsKeystone => Tier == 6;
}

public sealed class SkillCatalog
{
    public Dictionary<string, SkillProfile> Skills { get; } = new();

    public SkillProfile? Get(string id) => Skills.TryGetValue(id, out var s) ? s : null;
    public IEnumerable<SkillProfile> ForClass(string classId) => Skills.Values.Where(s => s.ClassId == classId).OrderBy(s => s.Branch).ThenBy(s => s.Tier);
    /// <summary>The class's branch names, the duo node's pseudo-branch excluded.</summary>
    public IEnumerable<string> Branches(string classId) => ForClass(classId).Where(s => !s.IsDuo).Select(s => s.Branch).Distinct();
    /// <summary>The class's duo node (tier 3, opens from any branch's second tier), if it has one.</summary>
    public SkillProfile? Duo(string classId) => ForClass(classId).FirstOrDefault(s => s.IsDuo);
    public IEnumerable<SkillProfile> Learned(CharacterState c) => c.LearnedSkills.Select(Get).Where(s => s != null)!;
}
