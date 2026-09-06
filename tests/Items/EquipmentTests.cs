using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Items;
using DungeonCrawler.Tests.Characters;
using Xunit;

namespace DungeonCrawler.Tests.Items;

public class EquipmentTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();

    [Fact]
    public void SlotKind_MustMatch()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        var sword = _cat.Create("item.longsword");
        Assert.Null(Equipment.WhyCannotEquip(w, sword, EquipSlot.Weapon));
        Assert.Contains("slot", Equipment.WhyCannotEquip(w, sword, EquipSlot.Head)!);
        Assert.NotNull(Equipment.WhyCannotEquip(w, _cat.Create("item.potion"), EquipSlot.Weapon));
    }

    [Fact]
    public void ClassRestrictions_WeaponArmorOffhand()
    {
        var mage = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
        Assert.Contains("cannot wield", Equipment.WhyCannotEquip(mage, _cat.Create("item.longsword"), EquipSlot.Weapon)!);
        Assert.Contains("cannot wear", Equipment.WhyCannotEquip(mage, _cat.Create("item.plate"), EquipSlot.Body)!);
        Assert.Null(Equipment.WhyCannotEquip(mage, _cat.Create("item.robe"), EquipSlot.Body));
        Assert.Contains("cannot use", Equipment.WhyCannotEquip(mage, _cat.Create("item.shield"), EquipSlot.Offhand)!);

        var warrior = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        Assert.Null(Equipment.WhyCannotEquip(warrior, _cat.Create("item.plate"), EquipSlot.Body));
        Assert.Null(Equipment.WhyCannotEquip(warrior, _cat.Create("item.shield"), EquipSlot.Offhand));
    }

    [Fact]
    public void TwoHanded_BlocksOffhandBothWays()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        w.Equipment.Equip(EquipSlot.Offhand, _cat.Create("item.shield"));
        Assert.Contains("unequip the offhand", Equipment.WhyCannotEquip(w, _cat.Create("item.spear"), EquipSlot.Weapon)!);
        w.Equipment.Unequip(EquipSlot.Offhand);
        w.Equipment.Equip(EquipSlot.Weapon, _cat.Create("item.spear"));
        Assert.Contains("Both hands", Equipment.WhyCannotEquip(w, _cat.Create("item.shield"), EquipSlot.Offhand)!);
    }

    [Fact]
    public void Modifiers_FlowIntoStats()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        var baseArmor = w.Stats[Stat.Armor];
        var plate = _cat.Create("item.plate");
        plate.Affixes.Add(new RolledAffix { AffixId = "s", Name = "Sturdy", IsPrefix = true, Tier = 1, Modifiers = { new StatModifier(Stat.Armor, ModOp.Add, 4) } });
        w.Equipment.Equip(EquipSlot.Body, plate);
        w.Recalculate();
        Assert.Equal(baseArmor + 24, w.Stats[Stat.Armor]);
        w.Equipment.Unequip(EquipSlot.Body);
        w.Recalculate();
        Assert.Equal(baseArmor, w.Stats[Stat.Armor]);
    }

    [Fact]
    public void Weapon_AffixOnHitReachesWeaponProfile()
    {
        var sword = _cat.Create("item.longsword");
        Assert.Empty(sword.Weapon!.OnHit);
        sword.Affixes.Add(new RolledAffix { AffixId = "v", Name = "Venomous", IsPrefix = true, Tier = 1, OnHit = new StatusApplication(StatusKind.Poison, 5, 2, 0.3f) });
        Assert.Single(sword.Weapon!.OnHit);
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front, sword);
        Assert.Single(w.WeaponProfile.OnHit);
    }

    [Fact]
    public void PreferredSlot_PicksEmptyRingFirst()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        var ring = _cat.Create("item.ring");
        Assert.Equal(EquipSlot.Ring1, w.Equipment.PreferredSlot(w, ring));
        w.Equipment.Equip(EquipSlot.Ring1, ring);
        Assert.Equal(EquipSlot.Ring2, w.Equipment.PreferredSlot(w, _cat.Create("item.ring")));
        w.Equipment.Equip(EquipSlot.Ring2, _cat.Create("item.ring"));
        Assert.Equal(EquipSlot.Ring1, w.Equipment.PreferredSlot(w, _cat.Create("item.ring")));
        Assert.Null(w.Equipment.PreferredSlot(CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back), _cat.Create("item.longsword")));
    }

    [Fact]
    public void Consumables_HealCureAndBuff()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        w.Hp = 10;
        var text = ConsumableRules.Use(_cat.Base("item.potion").Consumable!, new[] { w });
        Assert.Equal(50, w.Hp);
        Assert.Contains("+40 HP", text);

        w.Effects.Apply(new StatusApplication(StatusKind.Poison, 5, 2));
        ConsumableRules.Use(_cat.Base("item.antidote").Consumable!, new[] { w });
        Assert.False(w.Effects.Has(StatusKind.Poison));

        var m = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
        ConsumableRules.Use(_cat.Base("item.scroll_haste").Consumable!, new[] { w, m });
        Assert.True(w.Effects.Has(StatusKind.Haste));
        Assert.True(m.Effects.Has(StatusKind.Haste));
        Assert.True(m.Stats[Stat.AttackSpeed] > 1f);
    }
}
