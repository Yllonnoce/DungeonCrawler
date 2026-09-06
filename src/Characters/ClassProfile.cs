using DungeonCrawler.Combat;
using DungeonCrawler.Items;

namespace DungeonCrawler.Characters;

/// <summary>Plain-data view of a ClassDef Resource so the character model stays engine-free.</summary>
public sealed class ClassProfile
{
    public string Id { get; init; } = "class.none";
    public string Name { get; init; } = "Adventurer";
    public string Description { get; init; } = "";
    public Dictionary<Stat, int> BasePrimaries { get; init; } = StatInfo.Primaries.ToDictionary(p => p, _ => 10);
    public float HpPerLevel { get; init; } = 8;
    public float StaminaPerLevel { get; init; } = 5;
    public float ManaPerLevel { get; init; } = 3;
    public bool CanBlock { get; init; }
    public WeaponKind[] AllowedWeapons { get; init; } = Array.Empty<WeaponKind>();
    public ArmorClass[] AllowedArmor { get; init; } = { ArmorClass.Cloth };
    public OffhandKind[] AllowedOffhands { get; init; } = Array.Empty<OffhandKind>();
    /// <summary>Equipped at character creation (base ids). Slot is inferred from the item kind.</summary>
    public string[] StartingItems { get; init; } = Array.Empty<string>();
    public string[] StartingAbilities { get; init; } = Array.Empty<string>();
    public string StartingWeaponId { get; init; } = "";
    public float SneakAttackMultiplier { get; init; } = 1.5f;
    /// <summary>Flat class bonuses (e.g. rogue stealth, warrior armor) applied on every recalculation.</summary>
    public StatModifier[] BaseModifiers { get; init; } = Array.Empty<StatModifier>();

    public static ClassProfile Empty { get; } = new();

    public bool CanWield(WeaponKind kind) => kind == WeaponKind.Fists || AllowedWeapons.Length == 0 || AllowedWeapons.Contains(kind);
}
