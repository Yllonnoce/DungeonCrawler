using DungeonCrawler.Adventure.Objectives;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class PatrolRerouterTests
{
    private static readonly IReadOnlyList<IReadOnlyList<int>> Graph = new List<IReadOnlyList<int>>
    {
        new[] { 1 }, new[] { 0, 2, 3 }, new[] { 1 }, new[] { 1, 4 }, new[] { 3 },
    };

    [Fact]
    public void KeepsReachableRoomsInOrder()
    {
        var route = PatrolRerouter.Reroute(new[] { 0, 1, 2, 3 }, new HashSet<int> { 0, 1, 3 }, Graph, 1);
        Assert.Equal(new[] { 0, 1, 3 }, route);
        Assert.True(PatrolRerouter.Changed(new[] { 0, 1, 2, 3 }, route));
    }

    [Fact]
    public void GrowsToTwoRoomsWhenCutDown()
    {
        var route = PatrolRerouter.Reroute(new[] { 2, 4 }, new HashSet<int> { 1, 3, 4 }, Graph, 4);
        Assert.Equal(2, route.Count);
        Assert.Contains(4, route);
        Assert.Contains(3, route);
    }

    [Fact]
    public void HomeAlone_WhenNothingElseReachable()
    {
        var route = PatrolRerouter.Reroute(new[] { 0, 2 }, new HashSet<int> { 4 }, Graph, 4);
        Assert.Equal(new[] { 4 }, route);
    }

    [Fact]
    public void Unchanged_WhenEverythingStillReachable()
    {
        var route = PatrolRerouter.Reroute(new[] { 1, 3, 4 }, new HashSet<int> { 0, 1, 2, 3, 4 }, Graph, 1);
        Assert.False(PatrolRerouter.Changed(new[] { 1, 3, 4 }, route));
    }
}
