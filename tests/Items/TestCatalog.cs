using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Items;

namespace DungeonCrawler.Tests.Items;

public static class TestCatalog
{
    public static ItemCatalog Build()
    {
        var cat = new ItemCatalog();
        void B(ItemBase b) => cat.Bases[b.Id] = b;
        B(new ItemBase { Id = "item.longsword", Name = "Longsword", Kind = SlotKind.Weapon, Tags = new[] { "weapon", "melee", "sword" }, Weapon = new WeaponProfile { Kind = WeaponKind.Sword, MinDamage = 6, MaxDamage = 10 } });
        B(new ItemBase { Id = "item.spear", Name = "Spear", Kind = SlotKind.Weapon, Tags = new[] { "weapon", "melee", "polearm" }, TwoHanded = true, Weapon = new WeaponProfile { Kind = WeaponKind.Polearm, MinDamage = 7, MaxDamage = 12 } });
        B(new ItemBase { Id = "item.shortbow", Name = "Shortbow", Kind = SlotKind.Weapon, Tags = new[] { "weapon", "ranged", "bow" }, TwoHanded = true, Weapon = new WeaponProfile { Kind = WeaponKind.Bow, MinDamage = 5, MaxDamage = 9 } });
        B(new ItemBase { Id = "item.plate", Name = "Plate Armor", Kind = SlotKind.Body, Tags = new[] { "armor", "body" }, ArmorClass = ArmorClass.Plate, ItemLevelMin = 3, BaseModifiers = new[] { new StatModifier(Stat.Armor, ModOp.Add, 20) } });
        B(new ItemBase { Id = "item.robe", Name = "Robe", Kind = SlotKind.Body, Tags = new[] { "armor", "body" }, ArmorClass = ArmorClass.Cloth, BaseModifiers = new[] { new StatModifier(Stat.Armor, ModOp.Add, 3) } });
        B(new ItemBase { Id = "item.shield", Name = "Shield", Kind = SlotKind.Offhand, Tags = new[] { "offhand", "shield", "armor" }, Offhand = OffhandKind.Shield, BaseModifiers = new[] { new StatModifier(Stat.Armor, ModOp.Add, 8) } });
        B(new ItemBase { Id = "item.ring", Name = "Ring", Kind = SlotKind.Ring, Tags = new[] { "jewelry", "ring" } });
        B(new ItemBase { Id = "item.potion", Name = "Health Potion", Kind = SlotKind.Consumable, Tags = new[] { "consumable" }, Stackable = true, MaxStack = 10, Consumable = new ConsumableProfile { HealHp = 40 } });
        B(new ItemBase { Id = "item.antidote", Name = "Antidote", Kind = SlotKind.Consumable, Tags = new[] { "consumable" }, Stackable = true, Consumable = new ConsumableProfile { Cures = new[] { StatusKind.Poison } } });
        B(new ItemBase { Id = "item.scroll_haste", Name = "Scroll of Haste", Kind = SlotKind.Consumable, Tags = new[] { "consumable" }, Stackable = true, Consumable = new ConsumableProfile { Buff = new StatusApplication(StatusKind.Haste, 10, 1), TargetsParty = true } });

        void A(AffixProfile a) => cat.Affixes[a.Id] = a;
        A(new AffixProfile { Id = "affix.sharp", Name = "Sharp", IsPrefix = true, Tags = new[] { "melee" }, Stat = Stat.MeleeDamage, Tiers = new[] { new AffixTier(1, 1, 3), new AffixTier(5, 4, 7) } });
        A(new AffixProfile { Id = "affix.deadly", Name = "Deadly", IsPrefix = true, Tags = new[] { "ranged" }, Stat = Stat.RangedDamage, Tiers = new[] { new AffixTier(1, 1, 3) } });
        A(new AffixProfile { Id = "affix.sturdy", Name = "Sturdy", IsPrefix = true, Tags = new[] { "armor" }, Stat = Stat.Armor, Tiers = new[] { new AffixTier(1, 2, 5) } });
        A(new AffixProfile { Id = "affix.swift", Name = "Swift", IsPrefix = true, Tags = new[] { "weapon" }, Stat = Stat.AttackSpeed, Op = ModOp.Mul, Tiers = new[] { new AffixTier(1, 5, 10) } });
        A(new AffixProfile { Id = "affix.venomous", Name = "Venomous", IsPrefix = true, Tags = new[] { "weapon" }, Stat = Stat.MeleeDamage, Tiers = new[] { new AffixTier(2, 0, 0) }, OnHit = new StatusApplication(StatusKind.Poison, 5, 2, 0.3f) });
        A(new AffixProfile { Id = "affix.bear", Name = "of the Bear", IsPrefix = false, Tags = new[] { "armor", "jewelry", "weapon" }, Stat = Stat.MaxHp, Tiers = new[] { new AffixTier(1, 5, 15), new AffixTier(4, 16, 30) } });
        A(new AffixProfile { Id = "affix.strength", Name = "of Strength", IsPrefix = false, Tags = new[] { "armor", "jewelry", "weapon" }, Stat = Stat.Strength, Tiers = new[] { new AffixTier(1, 1, 3) } });
        A(new AffixProfile { Id = "affix.fox", Name = "of the Fox", IsPrefix = false, Tags = new[] { "armor", "jewelry" }, Stat = Stat.Evasion, Tiers = new[] { new AffixTier(1, 3, 8) } });
        A(new AffixProfile { Id = "affix.warding", Name = "of Warding", IsPrefix = false, Tags = new[] { "armor", "jewelry" }, Stat = Stat.ResistFire, Tiers = new[] { new AffixTier(3, 5, 15) } });

        cat.Legendaries["leg.ember"] = new LegendaryProfile { Id = "leg.ember", Name = "Ember Fang", BaseId = "item.longsword", Flavor = "It hums.", Modifiers = new[] { new StatModifier(Stat.CritChance, ModOp.Add, 0.1f) }, OnHit = new StatusApplication(StatusKind.Burn, 4, 3) };
        cat.Legendaries["leg.bulwark"] = new LegendaryProfile { Id = "leg.bulwark", Name = "Bulwark", BaseId = "item.shield", ItemLevelMin = 5, Modifiers = new[] { new StatModifier(Stat.Armor, ModOp.Add, 15) } };

        cat.Tables["table.all"] = new LootTableProfile { Id = "table.all", Entries = new[] { new LootEntry("tag:weapon", 3), new LootEntry("tag:armor", 3), new LootEntry("kind:Ring", 1), new LootEntry("kind:Consumable", 2) }, RarityWeights = new float[] { 60, 28, 10, 2 } };
        cat.Tables["table.weapons"] = new LootTableProfile { Id = "table.weapons", Entries = new[] { new LootEntry("tag:weapon", 1) }, RarityWeights = new float[] { 0, 0, 0, 1 } };
        cat.Tables["table.rare"] = new LootTableProfile { Id = "table.rare", Entries = new[] { new LootEntry("any", 1) }, RarityWeights = new float[] { 0, 0, 1, 0 } };
        return cat;
    }
}
