using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Tests.Characters;
using Xunit;

namespace DungeonCrawler.Tests.Combat;

public class AbilityRulesTests
{
    private static readonly AbilityProfile Fireball = new() { Id = "ability.fireball", Name = "Fireball", Kind = AbilityKind.Projectile, ManaCost = 12, Cooldown = 2f, Attack = new AttackProfile { MinDamage = 8, MaxDamage = 12, Type = DamageType.Fire, Kind = AttackKind.Spell } };

    [Fact]
    public void Rank_IsFormationOnly_AnyoneMayMelee()
    {
        var back = CharacterState.Create("Wren", TestClasses.Warrior, Rank.Back, TestClasses.Item(TestClasses.Sword));
        Assert.Null(AbilityRules.WhyCannotUse(back, AbilityProfile.Basic, 0));
        back.Equipment.Equip(DungeonCrawler.Items.EquipSlot.Weapon, TestClasses.Item(TestClasses.Bow));
        Assert.Equal("No arrows", AbilityRules.WhyCannotUse(back, AbilityProfile.Basic, 0));
        Assert.Null(AbilityRules.WhyCannotUse(back, AbilityProfile.Basic, 5));
        Assert.True(RankRules.CanMelee(Rank.Back, WeaponKind.Dagger));
    }

    [Fact]
    public void Costs_AreCheckedAndPaid()
    {
        var mage = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
        Assert.Null(AbilityRules.WhyCannotUse(mage, Fireball, 0));
        var manaBefore = mage.Mana;
        AbilityRules.PayCosts(mage, Fireball, false);
        Assert.Equal(manaBefore - 12, mage.Mana, 3);
        Assert.True(mage.CooldownRemaining > 0);
        Assert.Equal(2f, mage.AbilityCooldown("ability.fireball"));
        Assert.Equal("Recovering", AbilityRules.WhyCannotUse(mage, Fireball, 0));
        mage.CooldownRemaining = 0;
        Assert.Contains("not ready", AbilityRules.WhyCannotUse(mage, Fireball, 0)!);
        mage.TickCooldowns(2.5f);
        mage.Mana = 5;
        Assert.Contains("lacks mana", AbilityRules.WhyCannotUse(mage, Fireball, 0)!);
    }

    [Fact]
    public void HeavySwing_CostsDoubleStamina()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front, TestClasses.Item(TestClasses.Sword));
        Assert.Equal(10f, AbilityRules.StaminaCost(w, AbilityProfile.Basic, false));
        Assert.Equal(20f, AbilityRules.StaminaCost(w, AbilityProfile.Basic, true));
        w.Stamina = 15;
        Assert.Null(AbilityRules.WhyCannotUse(w, AbilityProfile.Basic, 0, heavy: false));
        Assert.Contains("exhausted", AbilityRules.WhyCannotUse(w, AbilityProfile.Basic, 0, heavy: true)!);
    }

    [Fact]
    public void StunnedOrDead_CannotAct()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        w.Effects.Apply(new StatusApplication(StatusKind.Stun, 1, 0));
        Assert.Contains("stunned", AbilityRules.WhyCannotUse(w, AbilityProfile.Basic, 0)!);
        w.Hp = 0;
        Assert.Contains("down", AbilityRules.WhyCannotUse(w, AbilityProfile.Basic, 0)!);
    }
}
