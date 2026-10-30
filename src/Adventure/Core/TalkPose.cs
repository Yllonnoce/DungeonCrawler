namespace DungeonCrawler.Adventure;

/// <summary>
/// How an NPC holds itself while a conversation is open: the Talk clip while its line is being spoken, a slow HipRock
/// while it waits for the player to answer. A line with no audio is "spoken" for about as long as reading it aloud.
/// </summary>
public static class TalkPose
{
    public const float ListenSpeed = 0.5f;
    private const float WordsPerSecond = 2.6f, MinSeconds = 1.2f, MaxSeconds = 15f;

    public static (string Clip, float Speed) Choose(bool speaking) => speaking ? ("Talk", 1f) : ("HipRock", ListenSpeed);

    public static float EstimateSeconds(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp(words / WordsPerSecond + 0.4f, MinSeconds, MaxSeconds);
    }
}
