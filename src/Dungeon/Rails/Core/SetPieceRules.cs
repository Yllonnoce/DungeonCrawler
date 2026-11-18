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

/// <summary>Timing and numbers for floor 12's set pieces. Engine-free; ticks are 60 Hz server physics ticks.</summary>
public static class SetPieceRules
{
    public const int TicksPerSecond = 60;

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

    public static bool JumpMade(float v) => v >= JumpSpeed;

    /// <summary>A parabola over u in [0, gapLength], zero at both ends, apex 0.12 × gapLength × v / JumpSpeed.</summary>
    public static float ArcHeight(float u, float gapLength, float v)
    {
        if (gapLength <= 0f) return 0f;
        var x = Math.Clamp(u / gapLength, 0f, 1f);
        return 4f * x * (1f - x) * 0.12f * gapLength * v / JumpSpeed;
    }
}
