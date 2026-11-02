namespace DungeonCrawler.Enemies;

/// <summary>Which grunt clip set an enemy uses when hurt or killed: chosen by size and boss flag unless its definition names one.</summary>
public static class VoiceSets
{
    public const string Small = "small", Medium = "medium", Large = "large", Boss = "boss";
    public static readonly string[] All = { Small, Medium, Large, Boss };

    public static string DefaultFor(float scale, bool isBoss)
        => isBoss ? Boss : scale < 0.85f ? Small : scale >= 1.25f ? Large : Medium;

    public static string Resolve(string voiceSet, float scale, bool isBoss)
        => string.IsNullOrEmpty(voiceSet) ? DefaultFor(scale, isBoss) : voiceSet;

    public static string HurtCue(string set) => $"grunt_hurt_{set}";
    public static string DeathCue(string set) => $"grunt_death_{set}";

    /// <summary>Pitch shift added at playback: small voices rise, big ones sink.</summary>
    public static float PitchOffset(string set) => set switch
    {
        Small => 0.25f, Medium => 0f, Large => -0.25f, Boss => -0.4f, _ => 0f,
    };
}
