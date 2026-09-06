using DungeonCrawler.Characters;
using Xunit;

namespace DungeonCrawler.Tests.Characters;

public class StatsTests
{
    [Fact]
    public void Compute_UsesPrimariesAndLevel()
    {
        var s1 = Stats.Compute(TestClasses.Warrior, 1, null, null);
        var s5 = Stats.Compute(TestClasses.Warrior, 5, null, null);
        Assert.Equal(30 + 15 * 5, s1[Stat.MaxHp]);
        Assert.Equal(s1[Stat.MaxHp] + 4 * TestClasses.Warrior.HpPerLevel, s5[Stat.MaxHp]);
        Assert.Equal(6, s1[Stat.Armor]); // class modifier
    }

    [Fact]
    public void Compute_AppliesAllocatedPointsBeforeDerived()
    {
        var alloc = new Dictionary<Stat, int> { [Stat.Vitality] = 4 };
        var s = Stats.Compute(TestClasses.Warrior, 1, alloc, null);
        Assert.Equal(30 + 19 * 5, s[Stat.MaxHp]);
    }

    [Fact]
    public void Compute_AddThenMul()
    {
        var mods = new[]
        {
            new StatModifier(Stat.MaxHp, ModOp.Add, 20),
            new StatModifier(Stat.MaxHp, ModOp.Mul, 1.5f),
        };
        var s = Stats.Compute(TestClasses.Warrior, 1, null, mods);
        Assert.Equal((105 + 20) * 1.5f, s[Stat.MaxHp], 3);
    }

    [Fact]
    public void Compute_ClampsResistAndCrit()
    {
        var mods = new[] { new StatModifier(Stat.ResistFire, ModOp.Add, 500), new StatModifier(Stat.CritChance, ModOp.Add, 5) };
        var s = Stats.Compute(TestClasses.Mage, 1, null, mods);
        Assert.Equal(75f, s[Stat.ResistFire]);
        Assert.Equal(0.95f, s[Stat.CritChance]);
    }

    [Fact]
    public void CharacterState_CreateRestoresToMax()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front, TestClasses.Item(TestClasses.Sword));
        Assert.Equal(c.Stats[Stat.MaxHp], c.Hp);
        Assert.Equal(c.Stats[Stat.MaxStamina], c.Stamina);
        Assert.True(c.IsAlive);
        Assert.Equal(WeaponKindOf(c), DungeonCrawler.Combat.WeaponKind.Sword);
    }

    private static DungeonCrawler.Combat.WeaponKind WeaponKindOf(CharacterState c) => c.WeaponProfile.Kind;

    [Fact]
    public void AllocateStat_SpendsPointsAndKeepsHpFraction()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        c.StatPoints = 2;
        c.Hp = c.Stats[Stat.MaxHp] / 2;
        Assert.True(c.AllocateStat(Stat.Vitality));
        Assert.Equal(1, c.StatPoints);
        Assert.Equal(1, c.Allocated[Stat.Vitality]);
        Assert.InRange(c.Hp / c.Stats[Stat.MaxHp], 0.49f, 0.51f);
        Assert.False(c.AllocateStat(Stat.MaxHp)); // not a primary
        c.StatPoints = 0;
        Assert.False(c.AllocateStat(Stat.Strength));
    }

    [Fact]
    public void Regenerate_StaminaPausesWhileBlocking()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        c.Stamina = 0; c.Mana = 0;
        c.Regenerate(1f, blocking: true);
        Assert.Equal(0, c.Stamina);
        Assert.True(c.Mana > 0);
        c.Regenerate(1f, blocking: false);
        Assert.Equal(c.Stats[Stat.StaminaRegen], c.Stamina, 3);
    }

    [Fact]
    public void CycleAbility_Wraps()
    {
        var c = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
        Assert.Equal(2, c.KnownAbilities.Count);
        c.CycleAbility(+1); Assert.Equal("ability.fireball", c.ActiveAbilityId);
        c.CycleAbility(+1); Assert.Equal("ability.basic", c.ActiveAbilityId);
        c.CycleAbility(-1); Assert.Equal("ability.fireball", c.ActiveAbilityId);
    }
}
