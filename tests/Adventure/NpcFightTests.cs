using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Dungeon;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class NpcFightTests
{
    [Fact]
    public void AttackFor_ScalesWithTheFloorLikeEnemies()
    {
        var a1 = NpcFight.AttackFor(1);
        Assert.Equal(8f * TierRules.DamageScale(1), a1.MinDamage, 3);
        Assert.Equal(14f * TierRules.DamageScale(1), a1.MaxDamage, 3);
        var a5 = NpcFight.AttackFor(5);
        Assert.True(a5.MaxDamage > a1.MaxDamage);
    }

    [Fact]
    public void Stats_GiveAFighterAHonestChanceToHit()
    {
        Assert.InRange(NpcFight.Stats[Stat.Accuracy], 60f, 90f);
        Assert.True(NpcFight.AttackRange > 1.5f && NpcFight.AttackRange < 4f);
        Assert.True(NpcFight.Windup > 0f && NpcFight.Recover > 0f);
        Assert.True(NpcFight.HpMultiplier >= 2f);
    }
}

public class NpcJoinTests
{
    [Fact]
    public void Decides_IsStablePerAsk_AndSaysYesAboutSevenTimesInTen()
    {
        Assert.Equal(NpcFight.Decides(42, "viewer_foehammer", 1), NpcFight.Decides(42, "viewer_foehammer", 1));
        var yes = 0;
        for (var ask = 1; ask <= 1000; ask++) if (NpcFight.Decides(7, "viewer_foehammer", ask)) yes++;
        Assert.InRange(yes, 600, 800);
        Assert.Contains(Enumerable.Range(1, 50), ask => !NpcFight.Decides(7, "viewer_foehammer", ask)); // a no is possible
    }
}
