using DungeonCrawler.Core;
using Xunit;

namespace DungeonCrawler.Tests.Core;

public class ViewerGaitTests
{
    [Fact]
    public void ImpliedSpeed_FollowsTheViewersWalkAndRunCycles()
    {
        // 1.8 m character: walk ≈ 1.14 m/s (±25° swings, 1.2 s cycle), run ≈ 3.27 m/s (±45°, 0.7 s)
        Assert.InRange(ViewerGait.ImpliedSpeed("Walk", 1.8f), 1.05f, 1.25f);
        Assert.InRange(ViewerGait.ImpliedSpeed("Run", 1.8f), 3.1f, 3.45f);
        Assert.Equal(ViewerGait.ImpliedSpeed("Walk", 1.8f) * 2f, ViewerGait.ImpliedSpeed("Walk", 3.6f), 4); // taller legs, longer stride
        Assert.Equal(0f, ViewerGait.ImpliedSpeed("Idle", 1.8f));
    }

    [Fact]
    public void SpeedScaleFor_MatchesTheClipToTheGroundSpeed_WithinLimits()
    {
        var walk = ViewerGait.ImpliedSpeed("Walk", 1.8f);
        Assert.Equal(1f, ViewerGait.SpeedScaleFor("Walk", 1.8f, walk), 4);
        Assert.Equal(1.5f, ViewerGait.SpeedScaleFor("Walk", 1.8f, walk * 1.5f), 4);
        Assert.Equal(ViewerGait.MaxScale, ViewerGait.SpeedScaleFor("Walk", 1.8f, walk * 10f));
        Assert.Equal(ViewerGait.MinScale, ViewerGait.SpeedScaleFor("Run", 1.8f, 0.1f));
        Assert.Equal(1f, ViewerGait.SpeedScaleFor("Idle", 1.8f, 2f));   // only gaits are matched
        Assert.Equal(1f, ViewerGait.SpeedScaleFor("Walk", 0f, 2f));     // no height known: leave it alone
    }
}

public class ViewerGaitChoiceTests
{
    [Fact]
    public void GaitFor_PicksTheCycleWhoseNaturalPaceIsNearest()
    {
        // 1.8 m: walk ≈ 1.14 m/s, run ≈ 3.27 m/s; the switch sits between them, so a 4.2 m/s NPC runs instead of playing a frantic walk
        Assert.Equal("Walk", ViewerGait.GaitFor(1.8f, 1.0f));
        Assert.Equal("Walk", ViewerGait.GaitFor(1.8f, 1.6f));
        Assert.Equal("Run", ViewerGait.GaitFor(1.8f, 2.5f));
        Assert.Equal("Run", ViewerGait.GaitFor(1.8f, 4.2f));
        Assert.Equal("Idle", ViewerGait.GaitFor(1.8f, 0.2f));
        Assert.Equal("Walk", ViewerGait.GaitFor(2.07f, 1.6f));   // the taller orc at his walking pace
    }
}

public class ViewerGaitHysteresisTests
{
    [Fact]
    public void GaitFor_KeepsTheCurrentGaitNearTheSwitch()
    {
        var walk = ViewerGait.ImpliedSpeed("Walk", 1.8f);
        var run = ViewerGait.ImpliedSpeed("Run", 1.8f);
        var edge = MathF.Sqrt(walk * run);
        Assert.Equal("Walk", ViewerGait.GaitFor(1.8f, edge * 1.05f, "Walk"));   // just over: a walker keeps walking
        Assert.Equal("Run", ViewerGait.GaitFor(1.8f, edge * 0.95f, "Run"));     // just under: a runner keeps running
        Assert.Equal("Run", ViewerGait.GaitFor(1.8f, edge * 1.3f, "Walk"));     // well over: switch
        Assert.Equal("Walk", ViewerGait.GaitFor(1.8f, edge * 0.7f, "Run"));     // well under: switch
        Assert.Equal("Idle", ViewerGait.GaitFor(1.8f, 0.2f, "Run"));
    }
}
