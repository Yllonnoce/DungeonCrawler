namespace DungeonCrawler.Characters;

/// <summary>Every number that gear, skills and status effects can touch. The first six are primaries.</summary>
public enum Stat
{
    Strength, Dexterity, Vitality, Intelligence, Willpower, Luck,
    MaxHp, MaxStamina, MaxMana,
    MeleeDamage, RangedDamage, SpellPower,
    Accuracy, Evasion, Armor,
    CritChance, CritMultiplier,
    AttackSpeed, MoveSpeed,
    HpRegen, StaminaRegen, ManaRegen,
    Stealth,
    ResistPhysical, ResistFire, ResistFrost, ResistPoison, ResistArcane,
    /// <summary>Noticing hidden traps and secret seams; the class adds most of it.</summary>
    Perception,
}

public enum ModOp { Add, Mul }

/// <summary>One modifier from any source (skill, affix, status). Mul values are factors: 1.1 = +10%.</summary>
public readonly record struct StatModifier(Stat Stat, ModOp Op, float Value, string Source = "");

public static class StatInfo
{
    public static readonly Stat[] Primaries = { Stat.Strength, Stat.Dexterity, Stat.Vitality, Stat.Intelligence, Stat.Willpower, Stat.Luck };
    public static readonly Stat[] All = Enum.GetValues<Stat>();

    public static bool IsPrimary(Stat s) => s <= Stat.Luck;

    public static string ShortName(Stat s) => s switch
    {
        Stat.Strength => "STR", Stat.Dexterity => "DEX", Stat.Vitality => "VIT",
        Stat.Intelligence => "INT", Stat.Willpower => "WIL", Stat.Luck => "LCK",
        _ => s.ToString(),
    };

    public static bool IsPercent(Stat s) => s is Stat.CritChance or Stat.ResistPhysical or Stat.ResistFire or Stat.ResistFrost or Stat.ResistPoison or Stat.ResistArcane;
}
