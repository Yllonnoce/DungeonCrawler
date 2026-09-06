using DungeonCrawler.Combat;

namespace DungeonCrawler.Characters;

public static class SkillRules
{
    public static string? WhyCannotLearn(CharacterState c, SkillProfile s)
    {
        if (s.ClassId != "" && s.ClassId != c.Class.Id) return $"{s.Name} belongs to another class";
        if (c.LearnedSkills.Contains(s.Id)) return $"{s.Name} is already learned";
        foreach (var p in s.Prerequisites)
            if (!c.LearnedSkills.Contains(p)) return $"{s.Name} needs an earlier skill first";
        if (c.SkillPoints < s.Cost) return $"Not enough skill points ({s.Cost} needed)";
        return null;
    }

    public static bool Learn(CharacterState c, SkillProfile s, SkillCatalog cat)
    {
        if (WhyCannotLearn(c, s) != null) return false;
        c.LearnedSkills.Add(s.Id);
        c.SkillPoints -= s.Cost;
        RebuildSkillEffects(c, cat);
        return true;
    }

    /// <summary>Forget everything and refund the points. Returns points refunded.</summary>
    public static int Respec(CharacterState c, SkillCatalog cat)
    {
        var refund = cat.Learned(c).Sum(s => s.Cost);
        foreach (var s in cat.Learned(c).ToList())
            if (s.GrantsAbility != "" && !c.Class.StartingAbilities.Contains(s.GrantsAbility)) c.KnownAbilities.Remove(s.GrantsAbility);
        c.LearnedSkills.Clear();
        c.SkillPoints += refund;
        c.ActiveAbilityIndex = Math.Clamp(c.ActiveAbilityIndex, 0, Math.Max(0, c.KnownAbilities.Count - 1));
        RebuildSkillEffects(c, cat);
        return refund;
    }

    /// <summary>Recompute the stat modifiers from learned skills and make sure granted abilities are on the hotbar.</summary>
    public static void RebuildSkillEffects(CharacterState c, SkillCatalog cat)
    {
        c.SkillModifiers.Clear();
        foreach (var s in cat.Learned(c))
        {
            foreach (var m in s.Modifiers) c.SkillModifiers.Add(m with { Source = s.Name });
            if (s.GrantsAbility != "" && !c.KnownAbilities.Contains(s.GrantsAbility)) c.KnownAbilities.Add(s.GrantsAbility);
        }
        c.Recalculate();
    }

    /// <summary>The ability as this character casts it, with every learned upgrade applied.</summary>
    public static AbilityProfile ApplyUpgrades(AbilityProfile a, CharacterState c, SkillCatalog cat)
    {
        var upgrades = cat.Learned(c).Where(s => s.Upgrade?.AbilityId == a.Id).Select(s => s.Upgrade!).ToList();
        if (upgrades.Count == 0) return a;
        float dmg = 1, cd = 1, cost = 1, buff = 1, radius = 0; var extra = 0;
        var statuses = new List<StatusApplication>();
        foreach (var u in upgrades)
        {
            dmg *= u.DamageMul; cd *= u.CooldownMul; cost *= u.CostMul; buff *= u.BuffMul; radius += u.RadiusAdd; extra += u.ExtraProjectiles;
            if (u.AddStatus is { } st) statuses.Add(st);
        }
        var attack = a.Attack == null ? null : a.Attack with
        {
            MinDamage = a.Attack.MinDamage * dmg, MaxDamage = a.Attack.MaxDamage * dmg,
            OnHit = a.Attack.OnHit.Concat(statuses).ToArray(),
        };
        return a with
        {
            Attack = attack,
            Cooldown = a.Cooldown * cd,
            ManaCost = a.ManaCost * cost,
            StaminaCost = a.StaminaCost * cost,
            Radius = a.Radius + radius,
            ProjectileCount = a.ProjectileCount + extra,
            Effects = a.Effects.Select(e => e with { Magnitude = e.Magnitude * buff, Duration = e.Duration * (buff > 1 ? 1 + (buff - 1) * 0.5f : 1) }).ToArray(),
        };
    }

    public static float TriggerMagnitude(CharacterState c, SkillCatalog cat, TriggerKind kind)
        => cat.Learned(c).Where(s => s.Trigger == kind).Sum(s => s.TriggerMagnitude);
}
