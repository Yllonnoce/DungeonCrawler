using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using Xunit;

namespace DungeonCrawler.Tests.Combat;

public class StatusEffectsTests
{
    [Fact]
    public void Poison_TicksOncePerSecondAndExpires()
    {
        var c = new StatusEffectController();
        c.Apply(new StatusApplication(StatusKind.Poison, 3f, 2f));
        var total = 0f;
        var ticks = 0;
        for (var i = 0; i < 40; i++)
            foreach (var t in c.Update(0.1f)) { total += t.Amount; ticks++; Assert.Equal(DamageType.Poison, t.Type); Assert.False(t.IsHeal); }
        Assert.Equal(3, ticks);
        Assert.Equal(6f, total, 3);
        Assert.Empty(c.Active);
    }

    [Fact]
    public void Dots_StackToThreeAndRefresh()
    {
        var c = new StatusEffectController();
        for (var i = 0; i < 5; i++) c.Apply(new StatusApplication(StatusKind.Bleed, 4f, 1f));
        var e = c.Get(StatusKind.Bleed)!;
        Assert.Equal(3, e.Stacks);
        var tick = c.Update(1f).Single();
        Assert.Equal(3f, tick.Amount);
    }

    [Fact]
    public void Regen_Heals()
    {
        var c = new StatusEffectController();
        c.Apply(new StatusApplication(StatusKind.Regen, 2f, 5f));
        var tick = c.Update(1f).Single();
        Assert.True(tick.IsHeal);
        Assert.Equal(5f, tick.Amount);
    }

    [Fact]
    public void Frost_SlowsAndStunBlocksActions()
    {
        var c = new StatusEffectController();
        c.Apply(new StatusApplication(StatusKind.Frost, 2f, 1f));
        Assert.Equal(0.6f, c.Multiplier(Stat.MoveSpeed), 3);
        Assert.Equal(0.7f, c.Multiplier(Stat.AttackSpeed), 3);
        c.Apply(new StatusApplication(StatusKind.Haste, 2f, 1f));
        Assert.Equal(0.6f * 1.2f, c.Multiplier(Stat.MoveSpeed), 3);
        Assert.False(c.IsStunned);
        c.Apply(new StatusApplication(StatusKind.Stun, 1f, 0));
        Assert.True(c.IsStunned);
        c.Update(1.01f);
        Assert.False(c.IsStunned);
    }

    [Fact]
    public void Shield_AddsAndCaps()
    {
        var c = new StatusEffectController();
        c.Apply(new StatusApplication(StatusKind.Shield, 10, 150));
        c.Apply(new StatusApplication(StatusKind.Shield, 10, 150));
        Assert.Equal(StatusEffectController.MaxShield, c.ShieldPoints);
        Assert.Equal(50f, c.AbsorbWithShield(250));
        Assert.False(c.Has(StatusKind.Shield));
    }

    [Fact]
    public void Chance_RespectsRng()
    {
        var c = new StatusEffectController();
        var rng = new Random(2);
        var applied = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (c.Apply(new StatusApplication(StatusKind.Burn, 1f, 1f, 0.3f), rng)) applied++;
            c.Clear();
        }
        Assert.InRange(applied, 240, 360);
    }

    [Fact]
    public void Version_BumpsOnChange()
    {
        var c = new StatusEffectController();
        var v = c.Version;
        c.Apply(new StatusApplication(StatusKind.Slow, 0.5f, 1f));
        Assert.True(c.Version > v);
        v = c.Version;
        c.Update(0.1f);
        Assert.Equal(v, c.Version);
        c.Update(1f);
        Assert.True(c.Version > v);
    }
}
