namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// The stop trap's words (plan 16): the siege's log lines and the warning Grisel or a freed crew member calls on a stop's first
/// siege. Subtitles only: every line goes out with no voice (<see cref="Line.VoiceId"/> null) and none reaches the planner.
/// T6 appended the hijack's lines (the crew's bark and three log lines); T7 appends its own. Engine-free.
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

    /// <summary>The crew's bark when a hijacker takes the cart (plan 16 Task 6): said by the nearest up freed crew member, else logged.</summary>
    public const string CrewCartTaken = "They've got the cart!";
    /// <summary>The log line when the driver's glow ends and the train rolls back: {0} is the driver, with its article ("A cinder imp").</summary>
    public const string BrakeOffLog = "{0} lets the brake off! The train rolls back!";
    /// <summary>The log line of a driver knocked off: {0} the hero who struck, {1} the creature's name in lower case.</summary>
    public const string KnockedOffCartLog = "{0} knocks the {1} off the cart!";
    /// <summary>The log line when the braked train turns back for its stop.</summary>
    public const string ReturnLog = "The train rolls back to the platform.";

    /// <summary>A creature's name with its article, capitalised for a line's start: "Cinder Imp" gives "A cinder imp", "Ember Hound" "An ember hound".</summary>
    public static string AName(string displayName)
    {
        var name = Lower(displayName);
        return (name.Length > 0 && "aeiou".Contains(name[0]) ? "An " : "A ") + name;
    }

    /// <summary>A creature's name in lower case, for the middle of a line.</summary>
    public static string Lower(string displayName) => displayName.Trim().ToLowerInvariant();

    /// <summary>The words as a line to say: subtitle only.</summary>
    public static Line Say(string text) => new(text) { VoiceId = null };
}
