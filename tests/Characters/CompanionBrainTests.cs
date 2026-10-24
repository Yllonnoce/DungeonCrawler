using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using Xunit;

namespace DungeonCrawler.Tests.Characters;

public class CompanionBrainTests
{
    private static readonly AbilityProfile Basic = AbilityProfile.Basic;
    private static readonly AbilityProfile Fireball = new() { Id = "ability.fireball", Name = "Fireball", Kind = AbilityKind.Projectile, Attack = new AttackProfile { MinDamage = 8, MaxDamage = 14 } };
    private static readonly AbilityProfile Shield = new() { Id = "ability.arcane_shield", Name = "Arcane Shield", Kind = AbilityKind.PartyBuff, Effects = new[] { new StatusApplication(StatusKind.Shield, 20, 40) } };
    private static readonly AbilityProfile Nova = new() { Id = "ability.frost_nova", Name = "Frost Nova", Kind = AbilityKind.Nova, Radius = 6, Attack = new AttackProfile { MinDamage = 6, MaxDamage = 10 } };
    private static readonly AbilityProfile Cleave = new() { Id = "ability.cleave", Name = "Cleave", Kind = AbilityKind.MeleeStrike, Attack = new AttackProfile { MinDamage = 6, MaxDamage = 10 } };

    private static AbilityView V(int i, AbilityProfile a, float range, bool ready = true) => new(i, a, ready, range);

    [Fact]
    public void Companion_Rescues_WhenCalm_AndSomeoneIsMangled()
    {
        var ctx = new CompanionContext { MangledMemberId = 2, MangledDistance = 6f, NearestEnemyDistance = 30f, Enemies = Array.Empty<EnemyView>() };
        Assert.Equal(ActionKind.Rescue, CompanionBrain.Decide(ctx).Kind);
        Assert.Equal(2, CompanionBrain.Decide(ctx).TargetId);
    }

    [Fact] public void Companion_DoesNotRescue_UnderFire() => Assert.NotEqual(ActionKind.Rescue, CompanionBrain.Decide(new CompanionContext { MangledMemberId = 2, NearestEnemyDistance = 5f }).Kind);
    [Fact] public void Companion_OnHold_DoesNotRescue() => Assert.NotEqual(ActionKind.Rescue, CompanionBrain.Decide(new CompanionContext { MangledMemberId = 2, NearestEnemyDistance = 30f, Stance = Stance.Hold }).Kind);
    [Fact] public void Companion_WithNobodyMangled_DoesNotRescue() => Assert.NotEqual(ActionKind.Rescue, CompanionBrain.Decide(new CompanionContext { NearestEnemyDistance = 30f }).Kind);

    [Fact]
    public void LowHealth_DrinksPotionFirst()
    {
        var ctx = new CompanionContext { HpFraction = 0.2f, HasHealingPotion = true, Enemies = new[] { new EnemyView(1, 1.5f, true, true, 1f) }, Abilities = new[] { V(0, Basic, 1.8f) } };
        Assert.Equal(ActionKind.DrinkPotion, CompanionBrain.Decide(ctx).Kind);
        Assert.NotEqual(ActionKind.DrinkPotion, CompanionBrain.Decide(ctx with { HasHealingPotion = false }).Kind);
    }

    [Fact]
    public void Defensive_IgnoresCalmDistantEnemy_ButFightsHostile()
    {
        var calm = new CompanionContext { Enemies = new[] { new EnemyView(1, 9f, false, true, 1f) }, Abilities = new[] { V(0, Basic, 1.8f) }, DistanceToFormation = 0.5f };
        Assert.Equal(ActionKind.Idle, CompanionBrain.Decide(calm).Kind);
        var hostile = calm with { Enemies = new[] { new EnemyView(1, 9f, true, true, 1f) } };
        var a = CompanionBrain.Decide(hostile);
        Assert.Equal(ActionKind.Approach, a.Kind);
        Assert.Equal(1, a.TargetId);
        // the lead's target is always fair game
        var leadTarget = calm with { LeadTargetId = 1 };
        Assert.Equal(ActionKind.Approach, CompanionBrain.Decide(leadTarget).Kind);
    }

    [Fact]
    public void Aggressive_EngagesAnythingVisible_HoldOnlyAdjacent()
    {
        var ctx = new CompanionContext { Stance = Stance.Aggressive, Enemies = new[] { new EnemyView(1, 9f, false, true, 1f) }, Abilities = new[] { V(0, Basic, 1.8f) } };
        Assert.Equal(ActionKind.Approach, CompanionBrain.Decide(ctx).Kind);
        var hold = ctx with { Stance = Stance.Hold };
        Assert.Equal(ActionKind.Hold, CompanionBrain.Decide(hold).Kind);
        var holdAdjacent = hold with { Enemies = new[] { new EnemyView(1, 1.5f, false, true, 1f) } };
        Assert.Equal(ActionKind.Attack, CompanionBrain.Decide(holdAdjacent).Kind);
    }

    [Fact]
    public void Melee_AttacksInRange_HeavyWhenRested()
    {
        var ctx = new CompanionContext { Enemies = new[] { new EnemyView(1, 1.5f, true, true, 1f) }, Abilities = new[] { V(0, Basic, 1.8f) }, StaminaFraction = 0.9f };
        var a = CompanionBrain.Decide(ctx);
        Assert.Equal(ActionKind.Attack, a.Kind);
        Assert.True(a.Heavy);
        Assert.False(CompanionBrain.Decide(ctx with { StaminaFraction = 0.3f }).Heavy);
        Assert.Equal(ActionKind.Idle, CompanionBrain.Decide(ctx with { Abilities = new[] { V(0, Basic, 1.8f, ready: false) } }).Kind);
    }

    [Fact]
    public void Ranged_KeepsDistance()
    {
        var ctx = new CompanionContext { PrefersRange = true, Enemies = new[] { new EnemyView(1, 2f, true, true, 1f) }, Abilities = new[] { V(0, Basic, 14f), V(1, Fireball, 14f) } };
        Assert.Equal(ActionKind.Retreat, CompanionBrain.Decide(ctx).Kind);
        var far = ctx with { Enemies = new[] { new EnemyView(1, 9f, true, true, 1f) } };
        var a = CompanionBrain.Decide(far);
        Assert.Equal(ActionKind.Attack, a.Kind);
        Assert.Equal(1, a.AbilityIndex); // fireball outdamages the staff
        Assert.False(a.Heavy);
        var blind = far with { Enemies = new[] { new EnemyView(1, 9f, true, false, 1f) } };
        Assert.Equal(ActionKind.Approach, CompanionBrain.Decide(blind).Kind);
    }

    [Fact]
    public void Buffs_AndNovas_ChosenByRules()
    {
        var abilities = new[] { V(0, Basic, 1.8f), V(1, Shield, 999f), V(2, Nova, 6f), V(3, Cleave, 1.8f) };
        var oneEnemy = new CompanionContext { Enemies = new[] { new EnemyView(1, 1.5f, true, true, 1f) }, Abilities = abilities, PartyMissingShield = true };
        Assert.Equal(1, CompanionBrain.Decide(oneEnemy).AbilityIndex); // shield the party first
        var shielded = oneEnemy with { PartyMissingShield = false };
        Assert.Equal(3, CompanionBrain.Decide(shielded).AbilityIndex); // cleave beats a basic swing
        var crowd = shielded with { Enemies = new[] { new EnemyView(1, 1.5f, true, true, 1f), new EnemyView(2, 4f, true, true, 1f) } };
        Assert.Equal(2, CompanionBrain.Decide(crowd).AbilityIndex); // nova with two nearby
    }

    [Fact]
    public void NoEnemies_FollowsOrRegroups()
    {
        var ctx = new CompanionContext { DistanceToFormation = 6f, Abilities = new[] { V(0, Basic, 1.8f) } };
        Assert.Equal(ActionKind.Follow, CompanionBrain.Decide(ctx).Kind);
        Assert.Equal(ActionKind.Regroup, CompanionBrain.Decide(ctx with { FormationReachable = false }).Kind);
        Assert.Equal(ActionKind.Idle, CompanionBrain.Decide(ctx with { DistanceToFormation = 0.5f }).Kind);
    }

    [Fact]
    public void Panic_RetreatsWhenNearlyDeadAndNoPotion()
    {
        var ctx = new CompanionContext { HpFraction = 0.1f, Enemies = new[] { new EnemyView(1, 1.5f, true, true, 1f) }, Abilities = new[] { V(0, Basic, 1.8f) } };
        Assert.Equal(ActionKind.Retreat, CompanionBrain.Decide(ctx).Kind);
    }
}
