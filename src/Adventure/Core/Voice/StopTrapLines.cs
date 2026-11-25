namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// The stop trap's words (plan 16): the siege's log lines and the warning Grisel or a freed crew member calls on a stop's first
/// siege. Subtitles only: every line goes out with no voice (<see cref="Line.VoiceId"/> null) and none reaches the planner.
/// T6 and T7 append the hijack's lines here. Engine-free.
/// </summary>
public static class StopTrapLines
{
    /// <summary>The log line of a siege's first wave.</summary>
    public const string SiegeFirstLog = "The air tears open around the platform!";
    /// <summary>The log line of every later wave (the floor-wide portals' own words).</summary>
    public const string SiegeLaterLog = "A portal tears open behind you.";
    /// <summary>Grisel's warning on a stop's first siege.</summary>
    public const string GriselSiegeWarning = "They're tearing through the air! Hold the platform!";
    /// <summary>A freed crew member's warning on a stop's first siege, when Grisel is absent, dead or downed.</summary>
    public const string CrewSiegeWarning = "Something's coming through! Watch the platform!";

    /// <summary>The words as a line to say: subtitle only.</summary>
    public static Line Say(string text) => new(text) { VoiceId = null };
}
