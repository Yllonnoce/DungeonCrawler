using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;

namespace DungeonCrawler.Enemies;

public enum EnemyKind { Melee, Ranged, Caster }
public enum Signature { None, Slam, Volley, Charge, Summon }

/// <summary>Plain-data view of an EnemyDef, before floor scaling.</summary>
public sealed record EnemyProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "Enemy";
    public EnemyKind Kind { get; init; } = EnemyKind.Melee;
    public float MaxHp { get; init; } = 30;
    public float MinDamage { get; init; } = 6;
    public float MaxDamage { get; init; } = 10;
    public DamageType DamageType { get; init; } = DamageType.Physical;
    public float MoveSpeed { get; init; } = 3f;
    public float DetectRange { get; init; } = 14f;
    public float AttackRange { get; init; } = 1.9f;
    /// <summary>Ranged/caster: how far away they like to stand.</summary>
    public float PreferredRange { get; init; } = 7f;
    public float WindupSeconds { get; init; } = 0.5f;
    public float RecoverSeconds { get; init; } = 0.9f;
    public float ProjectileSpeed { get; init; } = 16f;
    public int Xp { get; init; } = 25;
    public float Accuracy { get; init; } = 62;
    public float Evasion { get; init; } = 8;
    public float Armor { get; init; } = 5;
    public float ResistFire { get; init; }
    public float ResistFrost { get; init; }
    public float ResistPoison { get; init; }
    public float ResistArcane { get; init; }
    public StatusApplication? OnHit { get; init; }
    public string Color { get; init; } = "#bf2620";
    public float Scale { get; init; } = 1f;
    public bool IsBoss { get; init; }
    public Signature Signature { get; init; } = Signature.None;
    public float SignatureCooldown { get; init; } = 8f;
    public string SummonId { get; init; } = "";
    /// <summary>Aggroing one alerts idle kin nearby.</summary>
    public string Model { get; init; } = "";
    /// <summary>"bandits" for the outlaws who squat in camps; empty for a tier's own creatures.</summary>
    public string Faction { get; init; } = "";
    /// <summary>For horde creatures: the tier they belong to (-1 any).</summary>
    public int TierIndex { get; init; } = -1;
    public bool PackHunter { get; init; }
    /// <summary>Below this HP fraction the enemy runs for a while (0 = never).</summary>
    public float FleeHpFraction { get; init; }

    public Stats BuildStats() => Stats.FromValues(new Dictionary<Stat, float>
    {
        [Stat.Accuracy] = Accuracy, [Stat.Evasion] = Evasion, [Stat.Armor] = Armor,
        [Stat.CritChance] = 0.05f, [Stat.CritMultiplier] = 1.5f, [Stat.MoveSpeed] = 1, [Stat.AttackSpeed] = 1,
        [Stat.ResistFire] = ResistFire, [Stat.ResistFrost] = ResistFrost, [Stat.ResistPoison] = ResistPoison, [Stat.ResistArcane] = ResistArcane,
    });

    /// <summary>The attack this creature makes on a floor: the base numbers scaled by depth and by the dungeon's <see cref="TierRules.Lethality"/>.</summary>
    public AttackProfile BuildAttack(int floor)
    {
        var s = TierRules.DamageScale(floor) * TierRules.Lethality;
        return new AttackProfile
        {
            MinDamage = MinDamage * s, MaxDamage = MaxDamage * s, Type = DamageType,
            Kind = Kind == EnemyKind.Melee ? AttackKind.Melee : Kind == EnemyKind.Ranged ? AttackKind.Ranged : AttackKind.Spell,
            OnHit = OnHit is { } o ? new[] { o } : Array.Empty<StatusApplication>(),
        };
    }

    public float ScaledHp(int floor) => MathF.Round(MaxHp * TierRules.HpScale(floor));
    public int ScaledXp(int floor) => (int)MathF.Round(Xp * TierRules.XpScale(floor));
}
