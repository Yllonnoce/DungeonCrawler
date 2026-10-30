using DungeonCrawler.Party;
using Xunit;

namespace DungeonCrawler.Tests.Party;

public class VictoryDanceTests
{
    private static List<(string Clip, float Speed)> Run(VictoryDance dance, bool enemiesRemain)
    {
        var steps = new List<(string, float)>();
        while (dance.Next(enemiesRemain) is { } step) steps.Add(step);
        return steps;
    }

    [Fact]
    public void Dance_IsFiveSquats_FiveBucks_FiveRocks_ThenAWave_WhenTheFightIsOver()
    {
        var dance = new VictoryDance();
        dance.Start();
        var steps = Run(dance, enemiesRemain: false);
        var five = (string clip) => Enumerable.Repeat(clip, VictoryDance.Repeats);
        Assert.Equal(five("Squat").Concat(five("HipBuck")).Concat(five("HipRock")).Append("Wave"), steps.Select(s => s.Clip));
        Assert.False(dance.Active);
    }

    [Fact]
    public void Dance_SkipsTheWave_WhileEnemiesRemain()
    {
        var dance = new VictoryDance();
        dance.Start();
        var steps = Run(dance, enemiesRemain: true);
        Assert.Equal(VictoryDance.Repeats * 3, steps.Count);
        Assert.DoesNotContain(steps, s => s.Clip == "Wave");
    }

    [Fact]
    public void Repeats_IsFive_AndTheyAreFast_TheWaveIsNot()
    {
        Assert.Equal(5, VictoryDance.Repeats);
        var dance = new VictoryDance();
        dance.Start();
        var steps = Run(dance, enemiesRemain: false);
        Assert.All(steps.Where(s => s.Clip != "Wave"), s => Assert.Equal(VictoryDance.FastSpeed, s.Speed));
        Assert.True(VictoryDance.FastSpeed >= 2f);
        Assert.Equal(1f, steps.Last().Speed);
    }

    [Fact]
    public void TheWaveWaitsForTheFightToEnd_DecidedWhenItsTurnComes()
    {
        var dance = new VictoryDance();
        dance.Start();
        for (var i = 0; i < VictoryDance.Repeats * 3; i++) Assert.NotNull(dance.Next(enemiesRemain: true));
        Assert.Equal("Wave", dance.Next(enemiesRemain: false)?.Clip);
        Assert.Null(dance.Next(enemiesRemain: false));
    }

    [Fact]
    public void Idle_DanceYieldsNothing_AndCancelStopsIt()
    {
        var dance = new VictoryDance();
        Assert.False(dance.Active);
        Assert.Null(dance.Next(enemiesRemain: false));
        dance.Start();
        Assert.True(dance.Active);
        Assert.NotNull(dance.Next(enemiesRemain: false));
        dance.Cancel();
        Assert.False(dance.Active);
        Assert.Null(dance.Next(enemiesRemain: false));
    }

    [Fact]
    public void StartingAgainMidDance_StartsOver()
    {
        var dance = new VictoryDance();
        dance.Start();
        for (var i = 0; i < VictoryDance.Repeats + 2; i++) dance.Next(enemiesRemain: false);
        dance.Start();
        Assert.Equal("Squat", dance.Next(enemiesRemain: false)?.Clip);
    }
}
