namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What one cart's sound is fed each rendered frame (<c>CartSound.Feed</c>): its place, speed and mode, its plating, the track it
/// runs on (the wire's numbering), and the party cart's air, halt and lane crossing.</summary>
public readonly record struct CartSoundInput(float S, float V, CartMode Mode, float Plating, byte Track, bool Airborne, bool Halted, bool Crossing);

/// <summary>The one-shot edges a cart's sound plays (<see cref="RideSound.Edges"/>), one per feed (<see cref="RideSound.Loudest"/>).</summary>
[Flags]
public enum CartCue
{
    None = 0,
    Crash = 1,
    Jump = 2,
    Land = 4,
    Hit = 8,
    Arrive = 16,
    TrackChange = 32,
    Cross = 64,
}

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
        ("barricade", "cut") => "crate_break",
        ("barricade", "smashed") => "timber_smash",
        _ => null,
    };

    // ---- the rules a cart's sound follows (CartSound, RideSoundscape) --------------------------------------------

    /// <summary>A rail joint every this many metres: a clack each.</summary>
    public const float JointSpacing = 2.4f;
    /// <summary>A rolling cart rings its bell at most this often (the bark's own WarnEvery).</summary>
    public const float BellEvery = 4f;
    /// <summary>A cart farther than this from the active camera makes no sound.</summary>
    public const float Audible = 45f;

    private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

    /// <summary>The roll loop's pitch: 0.7 standing, 1.3 at <see cref="CartRules.MaxSpeed"/>.</summary>
    public static float RollPitch(float v) => 0.7f + 0.6f * Clamp01(v / CartRules.MaxSpeed);

    /// <summary>The roll loop's volume: silent below a crawl, louder with speed and with each wagon coupled behind.</summary>
    public static float RollDb(float v, int wagons) => v < 0.3f ? -80f : -20f + 14f * Clamp01(v / CartRules.MaxSpeed) + wagons;

    /// <summary>Seconds between joint clacks at <paramref name="v"/>: none below 0.5 m/s, never faster than 0.09 s.</summary>
    public static float ClackInterval(float v) => v < 0.5f ? float.PositiveInfinity : Math.Max(0.09f, JointSpacing / v);

    /// <summary>The flange screech on a bend: by the sideways pull |curvature|·v², silent below 3 m/s², full at 8.</summary>
    public static float ScreechDb(float curvature, float v)
    {
        var a = Math.Abs(curvature) * v * v;
        return a < 3f ? -80f : -22f + 16f * Clamp01((a - 3f) / 5f);
    }

    /// <summary>The wheels squeal under hard braking (dV/dt at or under −1.5 m/s², still moving).</summary>
    public static bool Squeals(float dvdt, float v) => dvdt <= -1.5f && v >= 1f;

    /// <summary>The rails singing under the hero's feet: silent beyond 6 s from the nearest rolling cart, louder as it comes.</summary>
    public static float SingDb(float secondsAway) => !(secondsAway <= 6f) ? -80f : -26f + 18f * (1f - Math.Max(0f, secondsAway) / 6f);

    /// <summary>
    /// The one-shot edges from one feed to the next: a halt (Crash), leaving or meeting the rails (Jump, Land), plating lost (Hit), a real
    /// arrival (Arrive: Rolling to Held with no halt and no jump of a metre or more along the line, which a spare's takeover makes; an
    /// arrival mends the plating in the same tick, so a rise says nothing), a track change and a thrown switch taken (Cross).
    /// </summary>
    public static CartCue Edges(in CartSoundInput prev, in CartSoundInput now)
    {
        var e = CartCue.None;
        var haltRose = now.Halted && !prev.Halted;
        if (haltRose) e |= CartCue.Crash;
        if (now.Airborne && !prev.Airborne) e |= CartCue.Jump;
        if (!now.Airborne && prev.Airborne) e |= CartCue.Land;
        if (prev.Plating - now.Plating >= 1f) e |= CartCue.Hit;
        if (prev.Mode == CartMode.Rolling && now.Mode == CartMode.Held && !haltRose && Math.Abs(now.S - prev.S) < 1f)
            e |= CartCue.Arrive;
        if (now.Track != prev.Track) e |= CartCue.TrackChange;
        if (now.Crossing && !prev.Crossing) e |= CartCue.Cross;
        return e;
    }

    /// <summary>One cue a feed: Crash, Land, Jump, Hit, TrackChange, Cross, then Arrive (only when nothing else fired).</summary>
    public static CartCue Loudest(CartCue edges)
    {
        if ((edges & CartCue.Crash) != 0) return CartCue.Crash;
        if ((edges & CartCue.Land) != 0) return CartCue.Land;
        if ((edges & CartCue.Jump) != 0) return CartCue.Jump;
        if ((edges & CartCue.Hit) != 0) return CartCue.Hit;
        if ((edges & CartCue.TrackChange) != 0) return CartCue.TrackChange;
        if ((edges & CartCue.Cross) != 0) return CartCue.Cross;
        return edges == CartCue.Arrive ? CartCue.Arrive : CartCue.None;
    }

    /// <summary>The bell rings for a body ahead while the cart rolls at 0.5 m/s or more, at most every <see cref="BellEvery"/> s. A Held cart never rings by it.</summary>
    public static bool BellDue(bool rolling, float v, bool anyoneAhead, double now, double lastRing)
        => rolling && v >= 0.5f && anyoneAhead && now - lastRing >= BellEvery;

    /// <summary>A chaser placed with a body this close to it (either side, along the line) rings once as it is placed: the one Held ring.</summary>
    public static bool SpawnBell(float gapMetres) => gapMetres <= CartRules.HalfLength + 1f;

    /// <summary>The cue an enemy telegraph makes on a client (the kiln golem's slam wind-up), or null.</summary>
    public static string? TelegraphCue(string state) => state == DungeonCrawler.Enemies.TelegraphRules.RailSlam ? "golem_windup" : null;

    /// <summary><see cref="PropCue"/> by a wire name ("kind:id") and its change.</summary>
    public static string? PropCueOf(string netRef, string what)
    {
        var colon = netRef.IndexOf(':');
        return colon <= 0 ? null : PropCue(netRef[..colon], what);
    }
}
