namespace DungeonCrawler.Combat;

/// <summary>A status to apply on hit or as a buff. Chance is rolled by the resolver.</summary>
public readonly record struct StatusApplication(StatusKind Kind, float Duration, float Magnitude, float Chance = 1f);

/// <summary>What an attack does before the defender is considered.</summary>
public sealed record AttackProfile
{
    public float MinDamage { get; init; }
    public float MaxDamage { get; init; }
    public DamageType Type { get; init; } = DamageType.Physical;
    public AttackKind Kind { get; init; } = AttackKind.Melee;
    public float CritBonus { get; init; }
    public StatusApplication[] OnHit { get; init; } = Array.Empty<StatusApplication>();

    public AttackProfile WithExtraOnHit(IEnumerable<StatusApplication> more) => this with { OnHit = OnHit.Concat(more).ToArray() };
}

/// <summary>Situational facts about one particular swing or shot.</summary>
public sealed record HitContext
{
    public bool Heavy { get; init; }
    public float HeavyMultiplier { get; init; } = 1f;
    public bool SneakAttack { get; init; }
    public float SneakMultiplier { get; init; } = 1f;
    public bool DefenderBlocking { get; init; }
    public float BlockReduction { get; init; } = 0.75f;
    public bool DefenderStunned { get; init; }

    public static HitContext None { get; } = new();
}

public sealed record DamageResult(bool Hit, bool Crit, bool Blocked, bool Sneak, float Amount, DamageType Type, IReadOnlyList<StatusApplication> Applied)
{
    public static DamageResult Miss(DamageType type) => new(false, false, false, false, 0, type, Array.Empty<StatusApplication>());
}
