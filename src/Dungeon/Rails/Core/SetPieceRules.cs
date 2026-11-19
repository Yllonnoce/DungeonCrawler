namespace DungeonCrawler.Dungeon.Rails;

/// <summary>How a slag cart paces itself against the party cart. Engine-free.</summary>
public static class PaceRules
{
    public const float AlongsideRadius = 2.5f;

    /// <summary>The S a slag cart aims for; a positive offset is ahead of the party cart.</summary>
    public static float TargetS(float partyEnemyS, float offset) => partyEnemyS + offset;

    /// <summary>The commanded speed: the party's speed plus a capped correction toward the target, within [0, MaxSpeed].</summary>
    public static float SpeedFor(float s, float v, float targetS, float partyV)
        => Math.Clamp(partyV + Math.Clamp((targetS - s) * 0.8f, -3f, 3f), 0f, CartRules.MaxSpeed);

    public static bool Alongside(float s, float targetS) => Math.Abs(s - targetS) < AlongsideRadius;
}

public enum HammerPhase { Up, Glow, Fall, Down, Rise }

public enum PourPhase { Dry, Warn, Pouring }

/// <summary>Where one ore bucket stands in its ride's round (<see cref="SetPieceRules.BucketStateAt"/>).</summary>
public enum BucketPhase { Hanging, Creaking, Poured }

/// <summary>Timing and numbers for floor 12's set pieces. Engine-free; ticks are 60 Hz server physics ticks.</summary>
public static class SetPieceRules
{
    public const int TicksPerSecond = 60;

    /// <summary>A hammer's or the pour's cues carry at most this far (a walker near it hears its warning).</summary>
    public const float MaxCueRange = 30f;
    /// <summary>The cue range ends at least this far short of stop 3 (a seated head sits up to 0.7 m along the cart from its middle).</summary>
    public const float StopThreeMargin = 2f;

    /// <summary>A hazard's cue range when it stands <paramref name="metresToStopThree"/> from stop 3: 30 m, or less so a cart held there never hears it (never below 0).</summary>
    public static float CueRange(float metresToStopThree) => Math.Clamp(metresToStopThree - StopThreeMargin, 0f, MaxCueRange);

    const float UpS = 1.6f, GlowS = 1.5f, FallS = 0.15f, DownS = 0.5f, RiseS = 0.75f;
    const int UpT = (int)(UpS * TicksPerSecond), GlowT = (int)(GlowS * TicksPerSecond), FallT = (int)(FallS * TicksPerSecond),
        DownT = (int)(DownS * TicksPerSecond), RiseT = (int)(RiseS * TicksPerSecond);
    const int HammerCycleT = UpT + GlowT + FallT + DownT + RiseT;
    public const int HammerIndexOffsetTicks = 40;

    const int DryT = 6 * TicksPerSecond, WarnT = (int)(1.5f * TicksPerSecond), PouringT = 3 * TicksPerSecond;
    const int PourCycleT = DryT + WarnT + PouringT;

    /// <summary>
    /// The lip speed that makes the jump: one pumper with every wagon coupled clears it (6 × 1.2 / 1.15 = 6.26 m/s), an unpumped
    /// cart never does (6.0 m/s empty, less with wagons), so a lone player can always make it by pumping.
    /// </summary>
    public const float JumpSpeed = 6.1f;
    public const float TrestleGrace = 4f;
    public const float RunawayEvery = 40f;
    public const float SlagTrafficEvery = 25f;
    public const float LeapWindup = 0.8f;
    public const float LeapRange = 6f;
    /// <summary>The party cart's rim has this many boarder spots (front-left, back-right).</summary>
    public const int RimSpots = 2;

    /// <summary>
    /// The party cart's rim spots open to a leaping hound: one per awake hero seated in it, at most <see cref="RimSpots"/>, so a
    /// lone rider faces one hound at a time (the next tries a wagon, or leaps and misses beside the lane). A party of two or more is unchanged.
    /// </summary>
    public static int RimSpotsOpen(int awakeHeroesSeated) => Math.Clamp(awakeHeroesSeated, 0, RimSpots);

    static long Mod(long a, int m) { var r = a % m; return r < 0 ? r + m : r; }

    /// <summary>The hammer's phase at <paramref name="tick"/>; <paramref name="secondsInto"/> counts from that phase's start.</summary>
    public static HammerPhase Phase(long tick, int index, out float secondsInto)
    {
        var t = (int)Mod(tick + (long)index * HammerIndexOffsetTicks, HammerCycleT);
        HammerPhase p;
        int start;
        if (t < UpT) { p = HammerPhase.Up; start = 0; }
        else if (t < UpT + GlowT) { p = HammerPhase.Glow; start = UpT; }
        else if (t < UpT + GlowT + FallT) { p = HammerPhase.Fall; start = UpT + GlowT; }
        else if (t < UpT + GlowT + FallT + DownT) { p = HammerPhase.Down; start = UpT + GlowT + FallT; }
        else { p = HammerPhase.Rise; start = UpT + GlowT + FallT + DownT; }
        secondsInto = (t - start) / (float)TicksPerSecond;
        return p;
    }

    /// <summary>The slag pour's phase: dry, a warning, then pouring.</summary>
    public static PourPhase Pour(long tick, out float secondsInto)
    {
        var t = (int)Mod(tick, PourCycleT);
        if (t < DryT) { secondsInto = t / (float)TicksPerSecond; return PourPhase.Dry; }
        if (t < DryT + WarnT) { secondsInto = (t - DryT) / (float)TicksPerSecond; return PourPhase.Warn; }
        secondsInto = (t - DryT - WarnT) / (float)TicksPerSecond;
        return PourPhase.Pouring;
    }

    // ---- the ore buckets: the server's schedule, which a client runs from the ride's arm time too --------------

    /// <summary>One bucket tips every this many seconds of an armed ride, in turn.</summary>
    public const float BucketEvery = 3f;
    /// <summary>A bucket creaks and glows this long before it pours.</summary>
    public const float BucketCreak = 1.5f;
    /// <summary>A poured cinder patch burns this long.</summary>
    public const float BucketPatchSeconds = 4f;

    /// <summary>
    /// Bucket <paramref name="n"/> of a ride (0 the first to tip) <paramref name="secondsSinceArmed"/> after the ride armed: it hangs until
    /// n·<see cref="BucketEvery"/>, creaks for <see cref="BucketCreak"/>, then has poured. <c>SecondsIn</c> counts from the phase's start
    /// (hanging: from the arming); before the arming every bucket hangs, 0 s in. The server's buckets and a client's copies both ask this.
    /// </summary>
    public static (BucketPhase Phase, double SecondsIn) BucketStateAt(double secondsSinceArmed, int n)
    {
        if (secondsSinceArmed < 0) return (BucketPhase.Hanging, 0.0);
        var creakAt = n * (double)BucketEvery;
        if (secondsSinceArmed < creakAt) return (BucketPhase.Hanging, secondsSinceArmed);
        var pourAt = creakAt + BucketCreak;
        if (secondsSinceArmed < pourAt) return (BucketPhase.Creaking, secondsSinceArmed - creakAt);
        return (BucketPhase.Poured, secondsSinceArmed - pourAt);
    }

    // ---- the heap's lantern and the beam: one rule on the server and on a client's copy ---------------------

    /// <summary>The heap's lantern blinks while a cart bears down within this many seconds of its speed.</summary>
    public const float HeapWarnSeconds = 3f;

    /// <summary>How far ahead the heap is called: <see cref="HeapWarnSeconds"/> at the cart's speed, never less than at the warn speed.</summary>
    public static float HeapLook(float v) => Math.Max(v, RunOverRules.WarnMinSpeed) * HeapWarnSeconds;

    /// <summary>A rolling cart heads for the heap: its face is not behind the front, on the party lane, and no thrown switch before it sends the cart round.</summary>
    public static bool HeapHeaded(bool rolling, float faceS, float front, bool faceOnEnemyLane, bool switchSetBefore)
        => rolling && faceS >= front - 0.5f && !faceOnEnemyLane && !switchSetBefore;

    /// <summary>The lantern blinks: headed for it, and the face within <see cref="HeapLook"/> of the front.</summary>
    public static bool HeapAlarm(bool headed, float faceS, float front, float v) => headed && faceS - front <= HeapLook(v);

    /// <summary>The beam is passed (the seated desktop cameras duck once): the rolling cart's front reached it, not yet this arming.</summary>
    public static bool BeamPasses(bool rolling, bool fired, float front, float beamS) => rolling && !fired && front >= beamS;

    public static bool JumpMade(float v) => v >= JumpSpeed;

    /// <summary>A parabola over u in [0, gapLength], zero at both ends, apex 0.12 × gapLength × v / JumpSpeed.</summary>
    public static float ArcHeight(float u, float gapLength, float v)
    {
        if (gapLength <= 0f) return 0f;
        var x = Math.Clamp(u / gapLength, 0f, 1f);
        return 4f * x * (1f - x) * 0.12f * gapLength * v / JumpSpeed;
    }
}
