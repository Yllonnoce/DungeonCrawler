using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>The objective deal keeps its promises on every seed, and a floor without one generates exactly as before.</summary>
public class FloorObjectiveTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 60).Select(i => new object[] { (ulong)(i * 7919 + 13) });

    [Theory, MemberData(nameof(Seeds))]
    public void Deal_KeepsItsRules(ulong seed)
    {
        var layouts = LayoutKinds.Deal(seed);
        var deal = FloorObjectives.Deal(seed, layouts);
        Assert.Equal(TierRules.FinalFloor, deal.Length);
        Assert.Equal(FloorObjectiveKind.None, deal[0]);
        Assert.Equal(FloorObjectives.PerRun, deal.Count(k => k != FloorObjectiveKind.None));
        for (var i = 0; i < deal.Length; i++)
        {
            if (deal[i] == FloorObjectiveKind.None) continue;
            Assert.False(TierRules.IsBossFloor(i + 1), $"floor {i + 1} is a boss floor");
            if (i + 1 < deal.Length) Assert.Equal(FloorObjectiveKind.None, deal[i + 1]);
            Assert.True(FloorObjectives.Compatible(deal[i], layouts[i]), $"floor {i + 1}: {deal[i]} on {layouts[i]}");
        }
        for (var t = 0; t < TierRules.TierCount; t++)
            Assert.Equal(FloorObjectives.PerTier, deal.Skip(t * TierRules.FloorsPerTier).Take(TierRules.FloorsPerTier).Count(k => k != FloorObjectiveKind.None));
        foreach (var kind in FloorObjectives.All) Assert.Contains(kind, deal);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Deal_IsDeterministic_AndPickAgrees(ulong seed)
    {
        var layouts = LayoutKinds.Deal(seed);
        var a = FloorObjectives.Deal(seed, layouts);
        var b = FloorObjectives.Deal(seed, layouts);
        Assert.Equal(a, b);
        for (var f = 1; f <= TierRules.FinalFloor; f++) Assert.Equal(a[f - 1], FloorObjectives.Pick(seed, f));
    }

    [Fact]
    public void Deal_DoesNotDisturbTheLayoutDeal()
    {
        var before = LayoutKinds.Deal(99);
        _ = FloorObjectives.Deal(99, before);
        Assert.Equal(before, LayoutKinds.Deal(99));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Endless_RollsFairlyAndNeverTwiceInARow(ulong seed)
    {
        var withOne = 0;
        for (var f = TierRules.FinalFloor + 1; f <= TierRules.FinalFloor + 40; f++)
        {
            var kind = FloorObjectives.Pick(seed, f);
            if (kind == FloorObjectiveKind.None) continue;
            withOne++;
            Assert.Equal(FloorObjectiveKind.None, FloorObjectives.Pick(seed, f + 1));
            Assert.True(FloorObjectives.Compatible(kind, LayoutKinds.Pick(seed, f)));
        }
        Assert.InRange(withOne, 2, 25);
    }

    [Fact]
    public void Pick_RespectsAnOverriddenShape_ButKeepsTheSlot()
    {
        var deal = FloorObjectives.Deal(4242, LayoutKinds.Deal(4242));
        for (var f = 2; f <= 19; f++)
        {
            Assert.NotEqual(FloorObjectiveKind.Toxic, FloorObjectives.Pick(4242, f, LayoutKind.Labyrinth));
            Assert.NotEqual(FloorObjectiveKind.ShiftingVault, FloorObjectives.Pick(4242, f, LayoutKind.Labyrinth));
            Assert.NotEqual(FloorObjectiveKind.Toxic, FloorObjectives.Pick(4242, f, LayoutKind.Flooded));
            Assert.NotEqual(FloorObjectiveKind.Ritual, FloorObjectives.Pick(4242, f, LayoutKind.Flooded));
            // a dealt slot stays an objective floor whatever shape a campaign gives it
            Assert.Equal(deal[f - 1] != FloorObjectiveKind.None, FloorObjectives.Pick(4242, f, LayoutKind.Labyrinth) != FloorObjectiveKind.None);
            Assert.Equal(FloorObjectives.Pick(4242, f, LayoutKind.Flooded), FloorObjectives.Pick(4242, f, LayoutKind.Flooded));
        }
        Assert.Equal(FloorObjectiveKind.None, FloorObjectives.Pick(4242, 1));
        Assert.Equal(FloorObjectiveKind.None, FloorObjectives.Pick(4242, 5));
        Assert.Equal(FloorObjectiveKind.None, FloorObjectives.Pick(4242, 20));
    }

    [Fact]
    public void Ids_RoundTrip()
    {
        foreach (var kind in FloorObjectives.All)
        {
            Assert.True(FloorObjectives.TryParse(FloorObjectives.Id(kind), out var back));
            Assert.Equal(kind, back);
            Assert.NotEqual("", FloorObjectives.Describe(kind));
            Assert.NotEqual("", FloorObjectives.Title(kind));
        }
        Assert.True(FloorObjectives.TryParse("Shifting_Vault", out var sv));
        Assert.Equal(FloorObjectiveKind.ShiftingVault, sv);
        Assert.True(FloorObjectives.TryParse("none", out var none));
        Assert.Equal(FloorObjectiveKind.None, none);
        Assert.False(FloorObjectives.TryParse("garbage", out _));
        Assert.False(FloorObjectives.TryParse(null, out _));
    }

    [Fact]
    public void Gates_AllButToxic()
    {
        Assert.True(FloorObjectives.LocksStairs(FloorObjectiveKind.Jailbreak));
        Assert.True(FloorObjectives.LocksStairs(FloorObjectiveKind.Ritual));
        Assert.True(FloorObjectives.LocksStairs(FloorObjectiveKind.ShiftingVault));
        Assert.False(FloorObjectives.LocksStairs(FloorObjectiveKind.Toxic));
        Assert.False(FloorObjectives.LocksStairs(FloorObjectiveKind.None));
    }

    [Fact]
    public void NoObjective_GeneratesExactlyAsBefore()
    {
        foreach (var kind in LayoutKinds.All)
            for (var seed = 1; seed < 20; seed++)
            {
                var plain = FloorGenerator.Generate(FloorSpec.Default with { Layout = kind }, seed);
                var explicitNone = FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = FloorObjectiveKind.None }, seed);
                Assert.Equal(plain.ContentHash(), explicitNone.ContentHash());
                Assert.Null(plain.Objective);
            }
    }

    [Fact]
    public void Tune_OnlyRaisesWhatTheObjectiveNeeds()
    {
        var spec = FloorSpec.Default with { HighRoomChance = 0.1f, NestCount = 1, VaultCount = 0 };
        Assert.Equal(spec, FloorObjectives.Tune(spec));
        Assert.True(FloorObjectives.Tune(spec with { Objective = FloorObjectiveKind.Toxic }).HighRoomChance >= 0.45f);
        Assert.True(FloorObjectives.Tune(spec with { Objective = FloorObjectiveKind.Ritual }).NestCount >= 3);
        Assert.True(FloorObjectives.Tune(spec with { Objective = FloorObjectiveKind.Jailbreak }).VaultCount >= 1);
        Assert.Equal(0.7f, FloorObjectives.Tune(spec with { Objective = FloorObjectiveKind.Toxic, HighRoomChance = 0.7f }).HighRoomChance);
    }
}
