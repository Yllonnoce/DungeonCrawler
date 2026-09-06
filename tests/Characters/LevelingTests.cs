using DungeonCrawler.Characters;
using Xunit;

namespace DungeonCrawler.Tests.Characters;

public class LevelingTests
{
    [Fact]
    public void XpToNext_Increases()
    {
        Assert.Equal(100, Leveling.XpToNext(1));
        for (var l = 1; l < 20; l++) Assert.True(Leveling.XpToNext(l + 1) > Leveling.XpToNext(l));
    }

    [Fact]
    public void AddXp_LevelsUpAndGrantsPoints()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        var hpBefore = c.Stats[Stat.MaxHp];
        Assert.Equal(0, Leveling.AddXp(c, 99));
        Assert.Equal(1, c.Level);
        Assert.Equal(1, Leveling.AddXp(c, 1));
        Assert.Equal(2, c.Level);
        Assert.Equal(0, c.Xp);
        Assert.Equal(Leveling.StatPointsPerLevel, c.StatPoints);
        Assert.Equal(Leveling.SkillPointsPerLevel, c.SkillPoints);
        Assert.True(c.Stats[Stat.MaxHp] > hpBefore);
    }

    [Fact]
    public void AddXp_CanGainSeveralLevels()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        var gained = Leveling.AddXp(c, Leveling.XpToNext(1) + Leveling.XpToNext(2) + Leveling.XpToNext(3) + 5);
        Assert.Equal(3, gained);
        Assert.Equal(4, c.Level);
        Assert.Equal(5, c.Xp);
    }

    [Fact]
    public void AddXp_IgnoresDead()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        c.Hp = 0;
        Assert.Equal(0, Leveling.AddXp(c, 1000));
        Assert.Equal(1, c.Level);
    }
}
