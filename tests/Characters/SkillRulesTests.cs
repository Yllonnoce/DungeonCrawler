using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Save;
using DungeonCrawler.Tests.Items;
using Xunit;

namespace DungeonCrawler.Tests.Characters;

public class SkillRulesTests
{
    private static SkillCatalog Catalog()
    {
        var cat = new SkillCatalog();
        void S(SkillProfile s) => cat.Skills[s.Id] = s;
        S(new SkillProfile { Id = "w.iron", Name = "Iron Skin", ClassId = "class.warrior", Branch = "Bulwark", Tier = 0, Modifiers = new[] { new StatModifier(Stat.Armor, ModOp.Add, 6) } });
        S(new SkillProfile { Id = "w.second", Name = "Second Wind", ClassId = "class.warrior", Branch = "Bulwark", Tier = 1, Prerequisites = new[] { "w.iron" }, Trigger = TriggerKind.SecondWind, TriggerMagnitude = 10 });
        S(new SkillProfile { Id = "w.cleave", Name = "Cleave", ClassId = "class.warrior", Branch = "Berserker", Tier = 0, GrantsAbility = "ability.cleave" });
        S(new SkillProfile { Id = "w.bash2", Name = "Shield Training", ClassId = "class.warrior", Branch = "Bulwark", Tier = 2, Prerequisites = new[] { "w.second" }, Cost = 2,
            Upgrade = new AbilityUpgrade { AbilityId = "ability.shield_bash", DamageMul = 1.5f, CooldownMul = 0.5f, AddStatus = new StatusApplication(StatusKind.Bleed, 4, 1) } });
        S(new SkillProfile { Id = "m.well", Name = "Mana Well", ClassId = "class.mage", Branch = "Warding", Tier = 0, Modifiers = new[] { new StatModifier(Stat.MaxMana, ModOp.Add, 20) } });
        return cat;
    }

    private static readonly AbilityProfile ShieldBash = new() { Id = "ability.shield_bash", Name = "Shield Bash", Kind = AbilityKind.MeleeStrike, StaminaCost = 18, Cooldown = 6, Attack = new AttackProfile { MinDamage = 4, MaxDamage = 6 } };

    [Fact]
    public void Learn_RequiresClassPrereqsAndPoints()
    {
        var cat = Catalog();
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        Assert.Contains("Not enough", SkillRules.WhyCannotLearn(w, cat.Skills["w.iron"])!);
        w.SkillPoints = 3;
        Assert.Contains("earlier skill", SkillRules.WhyCannotLearn(w, cat.Skills["w.second"])!);
        Assert.Contains("another class", SkillRules.WhyCannotLearn(w, cat.Skills["m.well"])!);
        var armor = w.Stats[Stat.Armor];
        Assert.True(SkillRules.Learn(w, cat.Skills["w.iron"], cat));
        Assert.Equal(2, w.SkillPoints);
        Assert.Equal(armor + 6, w.Stats[Stat.Armor]);
        Assert.Contains("already", SkillRules.WhyCannotLearn(w, cat.Skills["w.iron"])!);
        Assert.True(SkillRules.Learn(w, cat.Skills["w.second"], cat));
        Assert.Equal(10, SkillRules.TriggerMagnitude(w, cat, TriggerKind.SecondWind));
        Assert.Equal(0, SkillRules.TriggerMagnitude(w, cat, TriggerKind.Riposte));
        Assert.False(SkillRules.Learn(w, cat.Skills["w.bash2"], cat)); // costs 2, only 1 left
    }

    [Fact]
    public void GrantedAbility_AppearsAndRespecRemovesIt()
    {
        var cat = Catalog();
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        w.SkillPoints = 2;
        SkillRules.Learn(w, cat.Skills["w.cleave"], cat);
        SkillRules.Learn(w, cat.Skills["w.iron"], cat);
        Assert.Contains("ability.cleave", w.KnownAbilities);
        var armor = w.Stats[Stat.Armor];
        var refunded = SkillRules.Respec(w, cat);
        Assert.Equal(2, refunded);
        Assert.Equal(2, w.SkillPoints);
        Assert.Empty(w.LearnedSkills);
        Assert.DoesNotContain("ability.cleave", w.KnownAbilities);
        Assert.Equal(armor - 6, w.Stats[Stat.Armor]);
    }

    [Fact]
    public void Upgrades_ChangeTheAbilityForThatCharacterOnly()
    {
        var cat = Catalog();
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        w.SkillPoints = 4;
        SkillRules.Learn(w, cat.Skills["w.iron"], cat);
        SkillRules.Learn(w, cat.Skills["w.second"], cat);
        SkillRules.Learn(w, cat.Skills["w.bash2"], cat);
        var upgraded = SkillRules.ApplyUpgrades(ShieldBash, w, cat);
        Assert.Equal(6f, upgraded.Attack!.MinDamage);
        Assert.Equal(3f, upgraded.Cooldown);
        Assert.Contains(upgraded.Attack.OnHit, s => s.Kind == StatusKind.Bleed);
        var other = CharacterState.Create("Other", TestClasses.Warrior, Rank.Front);
        Assert.Same(ShieldBash, SkillRules.ApplyUpgrades(ShieldBash, other, cat));
    }

    [Fact]
    public void Save_RoundTripsLearnedSkills()
    {
        var cat = Catalog();
        var items = TestCatalog.Build();
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        w.SkillPoints = 2;
        SkillRules.Learn(w, cat.Skills["w.iron"], cat);
        SkillRules.Learn(w, cat.Skills["w.cleave"], cat);
        var back = SaveMapper.FromDto(SaveMapper.ToDto(w), _ => TestClasses.Warrior, items, cat);
        Assert.Equal(w.LearnedSkills, back.LearnedSkills);
        Assert.Equal(w.Stats[Stat.Armor], back.Stats[Stat.Armor]);
        Assert.Contains("ability.cleave", back.KnownAbilities);
    }
}
