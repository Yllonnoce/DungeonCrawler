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
    public void Dance_IsTwoOfEachMove_InSomeOrder_ThenAWave_WhenTheFightIsOver()
    {
        var dance = new VictoryDance();
        dance.Start(seed: 1);
        var steps = Run(dance, enemiesRemain: false);
        Assert.Equal(VictoryDance.Repeats * 3 + 1, steps.Count);
        Assert.Equal("Wave", steps.Last().Clip);
        var moves = steps.Take(steps.Count - 1).Select(s => s.Clip).ToList();
        foreach (var move in new[] { "Squat", "HipBuck", "HipRock" }) Assert.Equal(VictoryDance.Repeats, moves.Count(m => m == move));
        // each move is done its two times in a row before the next
        for (var i = 0; i < moves.Count; i += VictoryDance.Repeats) Assert.Equal(moves[i], moves[i + 1]);
        Assert.False(dance.Active);
    }

    [Fact]
    public void TheOrderOfTheMoves_ChangesWithTheSeed()
    {
        var orders = new HashSet<string>();
        for (var seed = 0; seed < 30; seed++)
        {
            var dance = new VictoryDance();
            dance.Start(seed);
            orders.Add(string.Join(",", Run(dance, enemiesRemain: true).Select(s => s.Clip).Distinct()));
        }
        Assert.True(orders.Count >= 4, $"{orders.Count} orders in 30 seeds");
        Assert.All(orders, o => Assert.Equal(3, o.Split(',').Length));
    }

    [Fact]
    public void Dance_SkipsTheWave_WhileEnemiesRemain()
    {
        var dance = new VictoryDance();
        dance.Start(seed: 2);
        var steps = Run(dance, enemiesRemain: true);
        Assert.Equal(VictoryDance.Repeats * 3, steps.Count);
        Assert.DoesNotContain(steps, s => s.Clip == "Wave");
    }

    [Fact]
    public void Repeats_IsTwo_AndTheMovesAreFast_TheWaveIsNot()
    {
        Assert.Equal(2, VictoryDance.Repeats);
        var dance = new VictoryDance();
        dance.Start(seed: 3);
        var steps = Run(dance, enemiesRemain: false);
        Assert.All(steps.Where(s => s.Clip != "Wave"), s => Assert.Equal(VictoryDance.FastSpeed, s.Speed));
        Assert.Equal(4f, VictoryDance.FastSpeed);
        Assert.Equal(1f, steps.Last().Speed);
    }

    [Fact]
    public void TheWaveWaitsForTheFightToEnd_DecidedWhenItsTurnComes()
    {
        var dance = new VictoryDance();
        dance.Start(seed: 4);
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
        dance.Start(seed: 5);
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
        dance.Start(seed: 6);
        var first = dance.Next(enemiesRemain: false)?.Clip;
        dance.Next(enemiesRemain: false); dance.Next(enemiesRemain: false);
        dance.Start(seed: 6);
        Assert.Equal(first, dance.Next(enemiesRemain: false)?.Clip);
    }
}
