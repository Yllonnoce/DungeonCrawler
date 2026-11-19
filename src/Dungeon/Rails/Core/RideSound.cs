namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The rail ride's sound table: which clips each cue plays, which cues loop, and which sound a prop's synced change
/// makes. The clips are CC0 (Freesound, credited in assets/LICENSES.md) cut into <c>assets/audio/rails/</c>, plus
/// the Kenney impact packs the game already carries. Engine-free; <c>AudioManager</c> merges <see cref="Clips"/>
/// into its cue table.
/// </summary>
public static class RideSound
{
    public const string Dir = "res://assets/audio/rails/";
    private const string Impact = "res://assets/audio/kenney/kenney_impact-sounds/Audio/";

    private static string[] Rails(params string[] names) => names.Select(n => Dir + n + ".ogg").ToArray();

    private static string[] Kenney(string stem, int from = 0, int to = 4)
        => Enumerable.Range(from, to - from + 1).Select(i => $"{Impact}{stem}_{i:000}.ogg").ToArray();

    /// <summary>Cue to clip paths (res://), played round-robin.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Clips = new Dictionary<string, string[]>
    {
        ["cart_roll"] = Rails("cart_roll"),
        ["rail_clack"] = Rails("clack_1", "clack_2", "clack_3"),
        ["rail_screech"] = Rails("screech_loop"),
        ["brake_squeal"] = Rails("brake_squeal"),
        ["rail_sing"] = Rails("rail_sing"),
        ["switch_clunk"] = Rails("lever_1", "lever_2"),
        ["gate_lift"] = Rails("gate_chain"),
        ["hammer_wind"] = Rails("gate_chain"),
        ["cart_bell"] = Rails("bell"),
        ["cart_crash"] = Rails("metal_crash"),
        ["cart_wreck"] = Rails("metal_crash", "beams_collapse"),
        ["timber_smash"] = Rails("beams_collapse"),
        ["timber_collapse"] = Rails("timber_collapse"),
        ["trestle_creak"] = Rails("creak_1"),
        ["bucket_creak"] = Rails("creak_1"),
        ["golem_slam"] = Rails("anvil_1", "anvil_2"),
        ["hammer_fall"] = Rails("anvil_1", "anvil_2"),
        ["golem_windup"] = Rails("windup"),
        ["pour_hiss"] = Rails("lava_hiss"),
        ["molten_loop"] = Rails("lava_loop"),
        ["steam_burst"] = Rails("steam"),
        ["foundry_rumble"] = Rails("rumble_bed"),
        ["cinder_pour"] = Rails("cinders"),
        // the Kenney impact packs the game already carries
        ["crate_break"] = Kenney("impactWood_heavy").Concat(Kenney("impactMining")).ToArray(),
        ["cart_jump"] = Kenney("impactPlate_light"),
        ["cart_land"] = Kenney("impactPlate_heavy").Concat(Kenney("impactMetal_heavy")).ToArray(),
        ["cart_hit"] = Kenney("impactPlate_heavy").Concat(Kenney("impactMetal_heavy")).ToArray(),
    };

    /// <summary>Cues whose clip loops: a positional one-shot of these would never free itself.</summary>
    public static readonly IReadOnlySet<string> Loops = new HashSet<string>
    {
        "cart_roll", "rail_screech", "rail_sing", "molten_loop", "foundry_rumble",
    };

    /// <summary>
    /// The cue a prop's synced change makes (<c>PropEvt</c> kind and what), or null for a quiet one. Only a lever
    /// thrown or set back by hand sounds; a reset, the points taken or freed, and the quiet tells make none.
    /// </summary>
    public static string? PropCue(string kind, string what) => (kind, what) switch
    {
        ("gate", "up") => "gate_lift",
        ("switch" or "spur", "thrown" or "straight") => "switch_clunk",
        ("heap", "cleared") => "crate_break",
        ("valve", "broken") => "steam_burst",
        ("trestle", "collapsed") => "timber_collapse",
        ("buffer", "smashed") => "timber_smash",
        _ => null,
    };
}
