using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Items;

namespace DungeonCrawler.Tests.Characters;

/// <summary>Hand-built class profiles mirroring data/classes so tests do not need Godot Resources.</summary>
public static class TestClasses
{
    public static ClassProfile Warrior { get; } = new()
    {
        Id = "class.warrior", Name = "Warrior", CanBlock = true, HpPerLevel = 12,
        BasePrimaries = new() { [Stat.Strength] = 16, [Stat.Dexterity] = 9, [Stat.Vitality] = 15, [Stat.Intelligence] = 6, [Stat.Willpower] = 9, [Stat.Luck] = 8 },
        AllowedWeapons = new[] { WeaponKind.Sword, WeaponKind.Axe, WeaponKind.Mace, WeaponKind.Polearm },
        AllowedArmor = new[] { ArmorClass.Cloth, ArmorClass.Leather, ArmorClass.Plate },
        AllowedOffhands = new[] { OffhandKind.Shield },
        BaseModifiers = new[] { new StatModifier(Stat.Armor, ModOp.Add, 6, "class") },
    };

    public static ClassProfile Rogue { get; } = new()
    {
        Id = "class.rogue", Name = "Rogue", SneakAttackMultiplier = 3f,
        BasePrimaries = new() { [Stat.Strength] = 10, [Stat.Dexterity] = 16, [Stat.Vitality] = 11, [Stat.Intelligence] = 8, [Stat.Willpower] = 8, [Stat.Luck] = 12 },
        AllowedWeapons = new[] { WeaponKind.Dagger, WeaponKind.Sword },
        AllowedArmor = new[] { ArmorClass.Cloth, ArmorClass.Leather },
        BaseModifiers = new[] { new StatModifier(Stat.Stealth, ModOp.Add, 30, "class"), new StatModifier(Stat.Perception, ModOp.Add, 25, "class") },
    };

    public static ClassProfile Mage { get; } = new()
    {
        Id = "class.mage", Name = "Mage", ManaPerLevel = 8,
        BasePrimaries = new() { [Stat.Strength] = 6, [Stat.Dexterity] = 9, [Stat.Vitality] = 8, [Stat.Intelligence] = 17, [Stat.Willpower] = 14, [Stat.Luck] = 9 },
        AllowedWeapons = new[] { WeaponKind.Staff },
        AllowedArmor = new[] { ArmorClass.Cloth },
        AllowedOffhands = new[] { OffhandKind.Focus },
        StartingAbilities = new[] { "ability.fireball" },
    };

    public static WeaponProfile Sword { get; } = new() { Kind = WeaponKind.Sword, MinDamage = 6, MaxDamage = 10, Cooldown = 0.8f, StaminaCost = 10 };
    public static WeaponProfile Spear { get; } = new() { Kind = WeaponKind.Polearm, MinDamage = 7, MaxDamage = 12, Cooldown = 1.0f, StaminaCost = 12 };
    public static WeaponProfile Bow { get; } = new() { Kind = WeaponKind.Bow, MinDamage = 5, MaxDamage = 9, Cooldown = 1.0f, StaminaCost = 4 };

    public static ItemInstance Item(WeaponProfile w) => new() { DefId = "item.test_" + w.Kind, BaseName = w.Kind.ToString(), Kind = SlotKind.Weapon, BaseWeapon = w, TwoHanded = w.Kind is WeaponKind.Polearm };

    /// <summary>A defender with no evasion, armor or resistances so damage math is predictable.</summary>
    public static Stats Dummy(float armor = 0, float evasion = 0, float resistAll = 0) => Stats.FromValues(new Dictionary<Stat, float>
    {
        [Stat.Armor] = armor, [Stat.Evasion] = evasion,
        [Stat.ResistPhysical] = resistAll, [Stat.ResistFire] = resistAll, [Stat.ResistFrost] = resistAll, [Stat.ResistPoison] = resistAll, [Stat.ResistArcane] = resistAll,
    });

    public static Stats Attacker(float accuracy = 100, float crit = 0, float critMult = 1.5f, float melee = 0) => Stats.FromValues(new Dictionary<Stat, float>
    {
        [Stat.Accuracy] = accuracy, [Stat.CritChance] = crit, [Stat.CritMultiplier] = critMult, [Stat.MeleeDamage] = melee,
    });
}
