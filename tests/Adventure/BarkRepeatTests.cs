using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class BarkRepeatTests
{
    [Fact]
    public void ALineWaitsTwoFloors_ThenReturns()
    {
        var said = new Dictionary<string, int>();
        var lines = new[] { "Down.", "Stay down.", "One less." };
        var rng = new Random(3);
        var heard = new HashSet<string>();
        for (var i = 0; i < 3; i++)
        {
            var pick = BarkRepeat.Choose(said, lines, 4, rng);
            Assert.NotNull(pick);
            Assert.True(heard.Add(pick!));
            BarkRepeat.Said(said, pick!, 4);
        }
        Assert.Null(BarkRepeat.Choose(said, lines, 4, rng)); // everything said on this floor: silence
        Assert.Null(BarkRepeat.Choose(said, lines, 5, rng)); // still too soon one floor down
        Assert.NotNull(BarkRepeat.Choose(said, lines, 6, rng)); // two floors on it may repeat
        Assert.True(BarkRepeat.CanSay(said, "  DOWN. ", 6)); // keyed on the text, case and spacing aside
        Assert.False(BarkRepeat.CanSay(said, "down.", 5));
        Assert.Null(BarkRepeat.Choose(said, new[] { "", "  " }, 9, rng));
    }
}
