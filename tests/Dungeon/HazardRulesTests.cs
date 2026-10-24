using DungeonCrawler.Dungeon;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class HazardRulesTests
{
    [Theory] [InlineData(3f, true)] [InlineData(2.5f, true)] [InlineData(2.49f, false)] [InlineData(1f, false)]
    public void Lethal_FromTwoAndAHalf(float lethality, bool lethal) => Assert.Equal(lethal, HazardRules.IsLethal(lethality));

    [Theory] [InlineData(1f, 0.5f)] [InlineData(2f, 0.75f)] [InlineData(2.49f, 0.8725f)]
    public void DamageFraction_Interpolates(float lethality, float expected) => Assert.Equal(expected, HazardRules.DamageFraction(lethality), 3);

    [Theory] [InlineData(1f)] [InlineData(2f)] [InlineData(3f)]
    public void Enemies_Die_AtEveryLethality(float lethality) => Assert.True(HazardRules.Outcome(VictimKind.Enemy, lethality).Lethal);

    [Fact]
    public void Boss_IsNeverKilled()
    {
        var o = HazardRules.Outcome(VictimKind.Boss, 3f);
        Assert.False(o.Lethal);
        Assert.Equal(0.35f, o.HpFraction);
        Assert.Equal(2f, o.StunSeconds);
    }

    [Fact]
    public void QuestNpc_IsDownedNotKilled()
    {
        var o = HazardRules.Outcome(VictimKind.QuestNpc, 3f);
        Assert.False(o.Lethal);
        Assert.Equal(1f, o.HpFraction);
    }

    [Fact] public void Member_AtDefaultLethality_IsLethal() => Assert.True(HazardRules.Outcome(VictimKind.Member, 3f).Lethal);

    [Fact] public void Ally_BelowLethal_TakesAFraction() { var o = HazardRules.Outcome(VictimKind.Ally, 2f); Assert.False(o.Lethal); Assert.Equal(0.75f, o.HpFraction, 3); }

    [Fact]
    public void Press_Timeline_MatchesSpec()
    {
        Assert.Equal(1.2f, HazardRules.PressDuration(PressPhase.Warning));
        Assert.Equal(2.4f, HazardRules.PressDuration(PressPhase.Closing));
        Assert.Equal(1.5f, HazardRules.PressDuration(PressPhase.Hold));
        Assert.Equal(4.0f, HazardRules.PressDuration(PressPhase.Retract));
        Assert.Equal(6.0f, HazardRules.PressDuration(PressPhase.Cooldown));
        Assert.True(float.IsPositiveInfinity(HazardRules.PressDuration(PressPhase.Armed)));
        Assert.Equal(0f, HazardRules.PressDuration(PressPhase.Crush));
        Assert.Equal(3.6f, HazardRules.PressDuration(PressPhase.Warning) + HazardRules.PressDuration(PressPhase.Closing), 3);
        Assert.Equal(PressPhase.Warning, HazardRules.NextPress(PressPhase.Armed));
        Assert.Equal(PressPhase.Crush, HazardRules.NextPress(PressPhase.Closing));
        Assert.Equal(PressPhase.Hold, HazardRules.NextPress(PressPhase.Crush));
        Assert.Equal(PressPhase.Armed, HazardRules.NextPress(PressPhase.Cooldown));
    }

    [Fact]
    public void PressGap_EasesIn()
    {
        Assert.Equal(3.5f, HazardRules.PressGap(0f));
        Assert.Equal(0.8f, HazardRules.PressGap(1f), 3);
        Assert.True(HazardRules.PressGap(0.5f) > (3.5f + 0.8f) / 2f);
        Assert.Equal(1.0f, HazardRules.JamGap);
    }

    [Fact]
    public void Spikes_PressureTimeline()
    {
        Assert.Equal(0.5f, HazardRules.SpikeDuration(SpikePhase.Click, HazardVariant.Pressure));
        Assert.Equal(1.0f, HazardRules.SpikeDuration(SpikePhase.Up, HazardVariant.Pressure));
        Assert.Equal(1.5f, HazardRules.SpikeDuration(SpikePhase.Retract, HazardVariant.Pressure));
        Assert.Equal(4.0f, HazardRules.SpikeDuration(SpikePhase.Rearm, HazardVariant.Pressure));
        Assert.True(float.IsPositiveInfinity(HazardRules.SpikeDuration(SpikePhase.Down, HazardVariant.Pressure)));
        Assert.Equal(3.0f, HazardRules.SpikeDuration(SpikePhase.Down, HazardVariant.Periodic));
        Assert.Equal(0f, HazardRules.SpikeDuration(SpikePhase.Rearm, HazardVariant.Periodic));
        Assert.Equal(SpikePhase.Click, HazardRules.NextSpike(SpikePhase.Down));
        Assert.Equal(SpikePhase.Up, HazardRules.NextSpike(SpikePhase.Click));
        Assert.Equal(SpikePhase.Down, HazardRules.NextSpike(SpikePhase.Rearm));
    }

    [Fact]
    public void Periodic_TwoBeds_AreOutOfStep()
    {
        var a = HazardRules.PeriodicPhaseAt(0, 0, out var secondsA);
        Assert.Equal(SpikePhase.Down, a);
        Assert.Equal(0f, secondsA);
        // when bed 0 fires, bed 3 (offset 111 ticks) is still down: nobody can be caught by two beds at once
        Assert.Equal(SpikePhase.Up, HazardRules.PeriodicPhaseAt(210, 0, out _));
        Assert.NotEqual(SpikePhase.Up, HazardRules.PeriodicPhaseAt(210, 3, out _));
        Assert.Equal(HazardRules.PeriodicPhaseAt(0, 0, out _), HazardRules.PeriodicPhaseAt(HazardRules.PeriodicPeriodTicks, 0, out _));
        Assert.Equal(SpikePhase.Click, HazardRules.PeriodicPhaseAt(180, 0, out _));
        Assert.Equal(SpikePhase.Up, HazardRules.PeriodicPhaseAt(210, 0, out var intoUp));
        Assert.Equal(0f, intoUp);
        Assert.Equal(270, HazardRules.PeriodicPeriodTicks);
    }

    [Fact]
    public void Airborne_DodgesSpikes()
    {
        Assert.True(HazardRules.CanDodgeSpikes(onFloor: false));
        Assert.False(HazardRules.CanDodgeSpikes(onFloor: true));
        Assert.Equal(0.10f, HazardRules.SpikePreRaiseMetres);
    }
}
