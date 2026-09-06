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
}

/// <summary>Changes a skill makes to one ability the character knows.</summary>
public sealed record AbilityUpgrade
{
    public string AbilityId { get; init; } = "";
    public float DamageMul { get; init; } = 1f;
    public float CooldownMul { get; init; } = 1f;
    public float CostMul { get; init; } = 1f;
    public float BuffMul { get; init; } = 1f;
    public float RadiusAdd { get; init; }
    public int ExtraProjectiles { get; init; }
    public StatusApplication? AddStatus { get; init; }
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
    public int Cost { get; init; } = 1;
    public StatModifier[] Modifiers { get; init; } = Array.Empty<StatModifier>();
    public string GrantsAbility { get; init; } = "";
    public AbilityUpgrade? Upgrade { get; init; }
    public TriggerKind Trigger { get; init; } = TriggerKind.None;
    public float TriggerMagnitude { get; init; }
}

public sealed class SkillCatalog
{
    public Dictionary<string, SkillProfile> Skills { get; } = new();

    public SkillProfile? Get(string id) => Skills.TryGetValue(id, out var s) ? s : null;
    public IEnumerable<SkillProfile> ForClass(string classId) => Skills.Values.Where(s => s.ClassId == classId).OrderBy(s => s.Branch).ThenBy(s => s.Tier);
    public IEnumerable<string> Branches(string classId) => ForClass(classId).Select(s => s.Branch).Distinct();
    public IEnumerable<SkillProfile> Learned(CharacterState c) => c.LearnedSkills.Select(Get).Where(s => s != null)!;
}
