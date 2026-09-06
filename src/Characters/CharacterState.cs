using DungeonCrawler.Combat;
using DungeonCrawler.Items;

namespace DungeonCrawler.Characters;

/// <summary>Runtime state of one party member. Plain C# so it saves as JSON and tests without the engine.</summary>
public sealed class CharacterState
{
    public string Name { get; init; } = "Adventurer";
    public ClassProfile Class { get; init; } = ClassProfile.Empty;
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int StatPoints { get; set; }
    public int SkillPoints { get; set; }
    public Dictionary<Stat, int> Allocated { get; } = new();

    public float Hp { get; set; }
    public float Stamina { get; set; }
    public float Mana { get; set; }
    public Rank Rank { get; set; }

    public Equipment Equipment { get; } = new();
    public ItemInstance? Weapon => Equipment[EquipSlot.Weapon];
    public List<string> KnownAbilities { get; } = new() { AbilityProfile.BasicId };
    public int ActiveAbilityIndex { get; set; }
    public Dictionary<string, float> AbilityCooldowns { get; } = new();
    public HashSet<string> LearnedSkills { get; } = new();
    /// <summary>Derived from LearnedSkills by SkillRules.RebuildSkillEffects; kept so Recalculate stays catalog-free.</summary>
    public List<StatModifier> SkillModifiers { get; } = new();

    public StatusEffectController Effects { get; } = new();
    public Stats Stats { get; private set; } = Stats.Empty;

    /// <summary>Seconds until the next weapon swing or cast is allowed.</summary>
    public float CooldownRemaining { get; set; }

    public bool IsAlive => Hp > 0;
    public bool IsStunned => Effects.IsStunned;
    public WeaponProfile WeaponProfile => Weapon?.Weapon ?? WeaponProfile.Fists;

    public string ActiveAbilityId => KnownAbilities.Count == 0
        ? AbilityProfile.BasicId
        : KnownAbilities[Math.Clamp(ActiveAbilityIndex, 0, KnownAbilities.Count - 1)];

    public static CharacterState Create(string name, ClassProfile cls, Rank rank, ItemInstance? weapon = null)
    {
        var c = new CharacterState { Name = name, Class = cls, Rank = rank };
        if (weapon != null) c.Equipment.Equip(EquipSlot.Weapon, weapon);
        foreach (var a in cls.StartingAbilities) if (!c.KnownAbilities.Contains(a)) c.KnownAbilities.Add(a);
        c.Recalculate();
        c.FullRestore();
        return c;
    }

    /// <summary>Rebuild <see cref="Stats"/> from class, level, points, weapon and status effects.</summary>
    public void Recalculate()
    {
        Stats = Stats.Compute(Class, Level, Allocated, Effects.Modifiers().Concat(Equipment.Modifiers()).Concat(SkillModifiers));
        ClampResources();
    }

    public void ClampResources()
    {
        Hp = Math.Clamp(Hp, 0, Stats[Stat.MaxHp]);
        Stamina = Math.Clamp(Stamina, 0, Stats[Stat.MaxStamina]);
        Mana = Math.Clamp(Mana, 0, Stats[Stat.MaxMana]);
    }

    public void FullRestore()
    {
        Hp = Stats[Stat.MaxHp];
        Stamina = Stats[Stat.MaxStamina];
        Mana = Stats[Stat.MaxMana];
        Effects.Clear();
        AbilityCooldowns.Clear();
        CooldownRemaining = 0;
    }

    public bool AllocateStat(Stat stat)
    {
        if (StatPoints <= 0 || !StatInfo.IsPrimary(stat)) return false;
        Allocated[stat] = Allocated.GetValueOrDefault(stat) + 1;
        StatPoints--;
        var hpFrac = Hp / Math.Max(1, Stats[Stat.MaxHp]);
        Recalculate();
        Hp = Math.Max(Hp, hpFrac * Stats[Stat.MaxHp]);
        return true;
    }

    public void CycleAbility(int direction)
    {
        if (KnownAbilities.Count == 0) return;
        ActiveAbilityIndex = ((ActiveAbilityIndex + direction) % KnownAbilities.Count + KnownAbilities.Count) % KnownAbilities.Count;
    }

    public float AbilityCooldown(string id) => AbilityCooldowns.GetValueOrDefault(id);

    public void TickCooldowns(float dt)
    {
        if (CooldownRemaining > 0) CooldownRemaining = Math.Max(0, CooldownRemaining - dt);
        if (AbilityCooldowns.Count == 0) return;
        foreach (var key in AbilityCooldowns.Keys.ToList())
        {
            var v = AbilityCooldowns[key] - dt;
            if (v <= 0) AbilityCooldowns.Remove(key); else AbilityCooldowns[key] = v;
        }
    }

    /// <summary>Natural regeneration. Stamina does not return while the character is holding a block.</summary>
    public void Regenerate(float dt, bool blocking)
    {
        if (!IsAlive) return;
        Hp = Math.Min(Stats[Stat.MaxHp], Hp + Stats[Stat.HpRegen] * dt);
        if (!blocking) Stamina = Math.Min(Stats[Stat.MaxStamina], Stamina + Stats[Stat.StaminaRegen] * dt);
        Mana = Math.Min(Stats[Stat.MaxMana], Mana + Stats[Stat.ManaRegen] * dt);
    }

    public void TakeDamage(float amount) => Hp = Math.Max(0, Hp - amount);
    public void Heal(float amount) => Hp = Math.Min(Stats[Stat.MaxHp], Hp + amount);
}
