using DungeonCrawler.Net;
using Xunit;

namespace DungeonCrawler.Tests.Net;

public class PoseBufferTests
{
    private static PoseBuffer Filled()
    {
        var b = new PoseBuffer();
        b.Push(1.0, 10, new NetVec(0, 0, 0), 0f, 0f, new NetVec(10, 0, 0), PoseFlags.OnFloor);
        b.Push(1.1, 11, new NetVec(1, 0, 0), 1f, 0f, new NetVec(10, 0, 0), PoseFlags.OnFloor);
        b.Push(1.2, 12, new NetVec(2, 0, 0), 2f, 0f, new NetVec(10, 0, 0), PoseFlags.OnFloor | PoseFlags.Crouch);
        return b;
    }

    [Fact]
    public void Empty_HasNoSample()
    {
        Assert.Null(new PoseBuffer().At(5));
    }

    [Fact]
    public void Interpolates_BetweenSamples()
    {
        var s = Filled().At(1.05)!.Value;
        Assert.Equal(0.5f, s.Position.X, 3);
        Assert.Equal(0.5f, s.Yaw, 3);
        Assert.Equal(PoseFlags.OnFloor, s.Flags);
        var late = Filled().At(1.16)!.Value;
        Assert.Equal(1.6f, late.Position.X, 3);
        Assert.True(late.Flags.HasFlag(PoseFlags.Crouch), "flags come from the nearer sample");
    }

    [Fact]
    public void Extrapolates_ButOnlySoFar()
    {
        var b = Filled();
        Assert.Equal(2.5f, b.At(1.25)!.Value.Position.X, 3);
        var far = b.At(9.0)!.Value;
        Assert.Equal(2f + 10f * (Protocol.MaxExtrapolationMs / 1000f), far.Position.X, 3);
    }

    [Fact]
    public void BeforeFirst_ClampsToFirst()
    {
        Assert.Equal(0f, Filled().At(0.5)!.Value.Position.X);
    }

    [Fact]
    public void OutOfOrder_IsDropped_AndCapacityHolds()
    {
        var b = Filled();
        Assert.False(b.Push(1.3, 11, new NetVec(99, 0, 0), 0, 0, NetVec.Zero, 0));
        Assert.Equal(2f, b.Newest!.Value.Position.X);
        var small = new PoseBuffer(4);
        for (var i = 0; i < 10; i++) small.Push(i, i, new NetVec(i, 0, 0), 0, 0, NetVec.Zero, 0);
        Assert.Equal(4, small.Count);
        Assert.Equal(9f, small.Newest!.Value.Position.X);
    }

    [Fact]
    public void ClockGoingBackwards_IsClampedNotDropped()
    {
        var b = Filled();
        Assert.True(b.Push(0.9, 13, new NetVec(3, 0, 0), 0, 0, NetVec.Zero, 0));
        Assert.Equal(1.2, b.Newest!.Value.Time);
    }

    [Fact]
    public void YawWraps()
    {
        var v = PoseBuffer.LerpAngle(MathF.PI - 0.1f, -MathF.PI + 0.1f, 0.5f);
        Assert.Equal(MathF.PI, MathF.Abs(v), 3); // halfway across the seam is the seam itself, not the long way round
        Assert.InRange(PoseBuffer.LerpAngle(3.0f, -3.0f, 0.5f), 3.1f, 3.2f);
    }

    [Fact]
    public void Trim_KeepsNewest()
    {
        var b = Filled();
        b.Trim(0.05);
        Assert.Equal(1, b.Count);
        Assert.Equal(12, b.Newest!.Value.Tick);
    }
}
