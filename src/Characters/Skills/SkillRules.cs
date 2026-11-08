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

    /// <summary>
    /// The ability as this character casts it: every learned upgrade plus <paramref name="extra"/> ones (equipped legendaries) applied,
    /// and the Cooldown stat (of Alacrity) on its cooldown.
    /// </summary>
    public static AbilityProfile ApplyUpgrades(AbilityProfile a, CharacterState c, SkillCatalog cat, IEnumerable<AbilityUpgrade>? extra = null)
    {
        var upgrades = cat.Learned(c).SelectMany(s => s.AllUpgrades).Concat(extra ?? Enumerable.Empty<AbilityUpgrade>()).Where(u => u.Targets(a.Id)).ToList();
        var cooldownStat = c.Stats[Stat.Cooldown] is var cs && cs > 0f ? cs : 1f;
        if (upgrades.Count == 0) return cooldownStat == 1f ? a : a with { Cooldown = a.Cooldown * cooldownStat };
        float dmg = 1, cd = cooldownStat, cost = 1, buff = 1, radius = 0, onHitDuration = 1; var extraProjectiles = 0;
        var statuses = new List<StatusApplication>();
        var effects = new List<StatusApplication>();
        foreach (var u in upgrades)
        {
            dmg *= u.DamageMul; cd *= u.CooldownMul; cost *= u.CostMul; buff *= u.BuffMul; radius += u.RadiusAdd; extraProjectiles += u.ExtraProjectiles; onHitDuration *= u.OnHitDurationMul;
            if (u.AddStatus is { } st) statuses.Add(st);
            if (u.AddEffect is { } ef) effects.Add(ef);
        }
        var attack = a.Attack == null ? null : a.Attack with
        {
            MinDamage = a.Attack.MinDamage * dmg, MaxDamage = a.Attack.MaxDamage * dmg,
            OnHit = a.Attack.OnHit.Select(s => onHitDuration == 1f ? s : s with { Duration = s.Duration * onHitDuration }).Concat(statuses).ToArray(),
        };
        return a with
        {
            Attack = attack,
            Cooldown = a.Cooldown * cd,
            ManaCost = a.ManaCost * cost,
            StaminaCost = a.StaminaCost * cost,
            Radius = a.Radius + radius,
            ProjectileCount = a.ProjectileCount + extraProjectiles,
            Effects = a.Effects.Select(e => e with { Magnitude = e.Magnitude * buff, Duration = e.Duration * (buff > 1 ? 1 + (buff - 1) * 0.5f : 1) }).Concat(effects).ToArray(),
        };
    }

    /// <summary>The summed magnitude of every learned skill's first and second trigger of that kind.</summary>
    public static float TriggerMagnitude(CharacterState c, SkillCatalog cat, TriggerKind kind)
        => cat.Learned(c).Sum(s => (s.Trigger == kind ? s.TriggerMagnitude : 0f) + (s.Trigger2 == kind ? s.TriggerMagnitude2 : 0f));
}
