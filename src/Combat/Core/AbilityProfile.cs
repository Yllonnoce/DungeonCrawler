using DungeonCrawler.Characters;

namespace DungeonCrawler.Combat;

public enum AbilityKind
{
    /// <summary>Swing or shoot the equipped weapon.</summary>
    WeaponAttack,
    /// <summary>A projectile spawned from the party; arrows if ArrowCost is set.</summary>
    Projectile,
    /// <summary>Weapon swing with bonus damage and effects (uses the melee cast).</summary>
    MeleeStrike,
    SelfBuff,
    PartyBuff,
    /// <summary>Hits every enemy within Radius of the party.</summary>
    Nova,
}

/// <summary>Plain-data view of an AbilityDef Resource.</summary>
public sealed record AbilityProfile
{
    public const string BasicId = "ability.basic";

    public string Id { get; init; } = BasicId;
    public string Name { get; init; } = "Attack";
    public string Description { get; init; } = "";
    public AbilityKind Kind { get; init; } = AbilityKind.WeaponAttack;
    public float ManaCost { get; init; }
    public float StaminaCost { get; init; }
    public int ArrowCost { get; init; }
    public float Cooldown { get; init; }
    public AttackProfile? Attack { get; init; }
    public StatusApplication[] Effects { get; init; } = Array.Empty<StatusApplication>();
    public float Radius { get; init; } = 6f;
    public float ProjectileSpeed { get; init; } = 18f;
    public int ProjectileCount { get; init; } = 1;
    public string Color { get; init; } = "#ffffff";

    public bool UsesWeapon => Kind is AbilityKind.WeaponAttack or AbilityKind.MeleeStrike;

    public static AbilityProfile Basic { get; } = new();
}

public static class AbilityRules
{
    /// <summary>Stamina a use will cost, given the equipped weapon and whether it is a heavy swing.</summary>
    public static float StaminaCost(CharacterState c, AbilityProfile a, bool heavy)
        => a.Kind == AbilityKind.WeaponAttack ? c.WeaponProfile.StaminaCost * (heavy ? 2f : 1f) : a.StaminaCost;

    public static int ArrowCost(CharacterState c, AbilityProfile a)
        => a.Kind == AbilityKind.WeaponAttack ? (c.WeaponProfile.IsRanged ? 1 : 0) : a.ArrowCost;

    /// <summary>Null when the ability can be used now; otherwise a short reason for the message log.</summary>
    public static string? WhyCannotUse(CharacterState c, AbilityProfile a, int arrowsInBag, bool heavy = false)
    {
        if (!c.IsAlive) return $"{c.Name} is down";
        if (c.IsStunned) return $"{c.Name} is stunned";
        if (c.CooldownRemaining > 0) return "Recovering";
        if (a.Cooldown > 0 && c.AbilityCooldown(a.Id) > 0) return $"{a.Name} is not ready";

        var w = c.WeaponProfile;
        if (a.Kind == AbilityKind.MeleeStrike && w.IsRanged)
            return $"{a.Name} needs a melee weapon";
        if (ArrowCost(c, a) > arrowsInBag) return "No arrows";
        if (StaminaCost(c, a, heavy) > c.Stamina) return $"{c.Name} is exhausted";
        if (a.ManaCost > c.Mana) return $"{c.Name} lacks mana";
        return null;
    }

    /// <summary>Deduct stamina, mana and start cooldowns. Arrows are consumed by the caller (they live in the bag).</summary>
    public static void PayCosts(CharacterState c, AbilityProfile a, bool heavy)
    {
        c.Stamina = Math.Max(0, c.Stamina - StaminaCost(c, a, heavy));
        c.Mana = Math.Max(0, c.Mana - a.ManaCost);
        var speed = Math.Max(0.2f, c.Stats[Stat.AttackSpeed]);
        var swing = a.Kind == AbilityKind.WeaponAttack ? c.WeaponProfile.Cooldown * (heavy ? 1.5f : 1f) : 0.5f;
        c.CooldownRemaining = swing / speed;
        if (a.Cooldown > 0) c.AbilityCooldowns[a.Id] = a.Cooldown;
    }
}
