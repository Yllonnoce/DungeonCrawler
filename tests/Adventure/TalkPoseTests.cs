using DungeonCrawler.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class TalkPoseTests
{
    [Fact] public void Speaking_IsTheTalkClip_AtFullSpeed() => Assert.Equal(("Talk", 1f), TalkPose.Choose(speaking: true));
    [Fact] public void Listening_IsASlowHipRock() { var (clip, speed) = TalkPose.Choose(speaking: false); Assert.Equal("HipRock", clip); Assert.InRange(speed, 0.3f, 0.7f); }

    [Fact]
    public void UnvoicedLine_SpeaksForAboutAsLongAsReadingItAloud()
    {
        var twelveWords = "The toll road runs past the old chapel and down into the dark.";
        var seconds = TalkPose.EstimateSeconds(twelveWords);
        Assert.InRange(seconds, 3.5f, 6f);
        Assert.True(TalkPose.EstimateSeconds("Yes.") >= 1f);
        Assert.True(TalkPose.EstimateSeconds("") >= 1f);
        Assert.True(TalkPose.EstimateSeconds(string.Join(' ', Enumerable.Repeat("word", 400))) <= 15f);
    }
}
