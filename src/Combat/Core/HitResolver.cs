using DungeonCrawler.Characters;

namespace DungeonCrawler.Combat;

/// <summary>The Depths' combat numbers that are not a class's or a weapon's: statuses that change damage taken or healing, and the data clamp on resists.</summary>
public static class CombatRules
{
    public const float VulnerableMultiplier = 1.25f;
    public const float VulnerableSeconds = 6f;
    public const float BleedHealingMultiplier = 0.5f;
    /// <summary>Resists above this are clamped here and in data (the files say what happens).</summary>
    public const float MaxResist = 75f;
}

/// <summary>Mitigation math shared by direct hits and damage-over-time ticks.</summary>
public static class DamagePipeline
{
    /// <summary>Armor (physical only), then resist, then the defender's damage-taken statuses (Vulnerable).</summary>
    public static float Mitigate(float raw, DamageType type, Stats defender, StatusEffectController? defenderEffects = null)
    {
        if (raw <= 0) return 0;
        var dmg = raw;
        if (type == DamageType.Physical)
        {
            var armor = Math.Max(0, defender[Stat.Armor]);
            dmg *= 100f / (100f + armor);
        }
        var resist = Math.Clamp(defender[type switch
        {
            DamageType.Physical => Stat.ResistPhysical,
            DamageType.Fire => Stat.ResistFire,
            DamageType.Frost => Stat.ResistFrost,
            DamageType.Poison => Stat.ResistPoison,
            _ => Stat.ResistArcane,
        }], 0f, CombatRules.MaxResist);
        dmg *= 1f - resist / 100f;
        if (defenderEffects != null) dmg *= defenderEffects.DamageTakenMultiplier;
        return dmg;
    }

    public static float RollBase(Stats attacker, AttackProfile attack, Random rng)
    {
        var min = Math.Min(attack.MinDamage, attack.MaxDamage);
        var max = Math.Max(attack.MinDamage, attack.MaxDamage);
        var roll = min + (float)rng.NextDouble() * (max - min);
        var flat = attack.Kind switch
        {
            AttackKind.Melee => attacker[Stat.MeleeDamage],
            AttackKind.Ranged => attacker[Stat.RangedDamage],
            _ => attacker[Stat.SpellPower],
        };
        return roll + flat;
    }

    public static float HitChance(Stats attacker, Stats defender)
        => Math.Clamp(0.72f + (attacker[Stat.Accuracy] - defender[Stat.Evasion]) * 0.006f, 0.35f, 0.95f);
}

/// <summary>Turns an attack against a defender into a DamageResult, applying on-hit statuses to the defender.</summary>
public static class HitResolver
{
    public static DamageResult Resolve(Stats attacker, Stats defender, StatusEffectController? defenderEffects,
                                       AttackProfile attack, HitContext ctx, Random rng)
    {
        var autoHit = ctx.SneakAttack || ctx.DefenderStunned;
        if (!autoHit && rng.NextDouble() >= DamagePipeline.HitChance(attacker, defender))
            return DamageResult.Miss(attack.Type);

        var crit = ctx.SneakAttack || rng.NextDouble() < attacker[Stat.CritChance] + attack.CritBonus;
        var dmg = DamagePipeline.RollBase(attacker, attack, rng);
        if (ctx.Heavy) dmg *= ctx.HeavyMultiplier;
        if (crit) dmg *= Math.Max(1f, attacker[Stat.CritMultiplier]);
        if (ctx.SneakAttack) dmg *= Math.Max(1f, ctx.SneakMultiplier);

        dmg = DamagePipeline.Mitigate(dmg, attack.Type, defender, defenderEffects);

        var blocked = ctx.DefenderBlocking && attack.Kind != AttackKind.Spell;
        if (blocked) dmg *= 1f - ctx.BlockReduction;

        if (defenderEffects != null) dmg = defenderEffects.AbsorbWithShield(dmg);

        var applied = new List<StatusApplication>();
        foreach (var s in attack.OnHit)
        {
            var chance = blocked ? s.Chance * 0.5f : s.Chance;
            if (rng.NextDouble() >= chance) continue;
            defenderEffects?.Apply(s with { Chance = 1f });
            applied.Add(s);
        }

        return new DamageResult(true, crit, blocked, ctx.SneakAttack, Math.Max(0f, dmg), attack.Type, applied);
    }
}
