using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>The dungeon's lethality multiplier: what it scales, and what it deliberately leaves alone.</summary>
[Collection("Lethality")]
public class LethalityTests : IDisposable
{
    private readonly float _saved = TierRules.Lethality;
    public void Dispose() => TierRules.Lethality = _saved;

    private static readonly EnemyProfile Ghoul = new() { Id = "enemy.ghoul", Name = "Ghoul", MinDamage = 6, MaxDamage = 10 };

    [Fact]
    public void Default_IsBrutal()
    {
        Assert.Equal(3f, TierRules.DefaultLethality);
        Assert.InRange(TierRules.DefaultLethality, TierRules.MinLethality, TierRules.MaxLethality);
    }

    [Fact]
    public void EnemyAttack_ScalesWithLethality()
    {
        TierRules.Lethality = 1f;
        var normal = Ghoul.BuildAttack(1);
        TierRules.Lethality = 3f;
        var brutal = Ghoul.BuildAttack(1);
        Assert.Equal(6f, normal.MinDamage, 3);
        Assert.Equal(10f, normal.MaxDamage, 3);
        Assert.Equal(18f, brutal.MinDamage, 3);
        Assert.Equal(30f, brutal.MaxDamage, 3);
        Assert.Equal(normal.Type, brutal.Type);
        Assert.Equal(normal.Kind, brutal.Kind);
    }

    [Fact]
    public void EnemyAttack_StillScalesWithDepth()
    {
        TierRules.Lethality = 3f;
        Assert.True(Ghoul.BuildAttack(10).MaxDamage > Ghoul.BuildAttack(1).MaxDamage);
    }

    [Theory]
    [InlineData(TrapKind.Spikes)]
    [InlineData(TrapKind.Fire)]
    [InlineData(TrapKind.Gas)]
    public void TrapOnParty_ScalesDamageAndStatus(TrapKind kind)
    {
        TierRules.Lethality = 1f;
        var normal = TrapRules.Attack(kind, 3);
        TierRules.Lethality = 3f;
        var brutal = TrapRules.Attack(kind, 3);
        Assert.Equal(normal.MinDamage * 3f, brutal.MinDamage, 3);
        Assert.Equal(normal.MaxDamage * 3f, brutal.MaxDamage, 3);
        Assert.Equal(normal.OnHit[0].Kind, brutal.OnHit[0].Kind);
        Assert.Equal(normal.OnHit[0].Duration, brutal.OnHit[0].Duration, 3);
        Assert.Equal(normal.OnHit[0].Magnitude * 3f, brutal.OnHit[0].Magnitude, 3);
    }

    [Fact]
    public void FloorFire_Scales()
    {
        TierRules.Lethality = 1f;
        var normal = TrapRules.FloorFire(2);
        TierRules.Lethality = 2f;
        var hard = TrapRules.FloorFire(2);
        Assert.Equal(normal.MaxDamage * 2f, hard.MaxDamage, 3);
        Assert.Equal(normal.OnHit[0].Magnitude * 2f, hard.OnHit[0].Magnitude, 3);
    }

    [Theory]
    [InlineData(TrapKind.Spikes)]
    [InlineData(TrapKind.Gas)]
    public void PartyTrapOnEnemy_IsNotScaled(TrapKind kind)
    {
        TierRules.Lethality = 1f;
        var normal = TrapRules.Attack(kind, 4, lethal: false);
        TierRules.Lethality = 3f;
        var placed = TrapRules.PlacedAttack(kind, 4);
        Assert.Equal(normal.MinDamage, placed.MinDamage, 3);
        Assert.Equal(normal.MaxDamage, placed.MaxDamage, 3);
        Assert.Equal(normal.OnHit[0].Magnitude, placed.OnHit[0].Magnitude, 3);
        // and it is the same as what the party would suffer at normal lethality
        TierRules.Lethality = 1f;
        Assert.Equal(TrapRules.Attack(kind, 4).MaxDamage, placed.MaxDamage, 3);
    }

    [Fact]
    public void NpcHp_CarriesLethality()
    {
        TierRules.Lethality = 1f;
        var normal = TierRules.NpcHp(5);
        TierRules.Lethality = 3f;
        var brutal = TierRules.NpcHp(5);
        Assert.Equal(100f, normal, 3);
        Assert.Equal(300f, brutal, 3);
    }

    [Fact]
    public void EnemyCost_IgnoresLethality()
    {
        TierRules.Lethality = 1f;
        var normal = DungeonCrawler.Adventure.FloorMenu.EnemyCost(Ghoul, 3);
        TierRules.Lethality = 3f;
        var brutal = DungeonCrawler.Adventure.FloorMenu.EnemyCost(Ghoul, 3);
        Assert.Equal(normal, brutal, 5);
    }
}
