using DungeonCrawler.Combat;

namespace DungeonCrawler.Items;

/// <summary>Combat numbers of a weapon, copied onto the ItemInstance so affixes (Phase 3) can modify them.</summary>
public sealed record WeaponProfile
{
    public WeaponKind Kind { get; init; } = WeaponKind.Fists;
    public float MinDamage { get; init; } = 1;
    public float MaxDamage { get; init; } = 3;
    public DamageType Type { get; init; } = DamageType.Physical;
    public float Cooldown { get; init; } = 0.8f;
    public float StaminaCost { get; init; } = 8f;
    public float HeavyMultiplier { get; init; } = 1.8f;
    public float Range { get; init; } = 1.8f;
    public StatusApplication[] OnHit { get; init; } = Array.Empty<StatusApplication>();

    public bool IsRanged => Kind == WeaponKind.Bow;

    public static WeaponProfile Fists { get; } = new() { Kind = WeaponKind.Fists, MinDamage = 1, MaxDamage = 3, Cooldown = 0.5f, StaminaCost = 4 };

    public AttackProfile ToAttack() => new()
    {
        MinDamage = MinDamage,
        MaxDamage = MaxDamage,
        Type = Type,
        Kind = IsRanged ? AttackKind.Ranged : AttackKind.Melee,
        OnHit = OnHit,
    };
}
