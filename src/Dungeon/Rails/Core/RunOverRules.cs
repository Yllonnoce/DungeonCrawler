namespace DungeonCrawler.Dungeon.Rails;

/// <summary>A body near the track: S is its closest offset on the curve, Lateral the horizontal distance to that point, Height its feet y minus the track y there.</summary>
public readonly record struct TrackBody(float S, float Lateral, float Height, float Radius);

/// <summary>When a cart runs a body over, and what that does. Engine-free.</summary>
public static class RunOverRules
{
    public const float BigBodyPlatingDamage = 15f;
    /// <summary>A hero who gets out at or above this speed is run over by their own cart.</summary>
    public const float ThrownAtSpeed = 1f;
    private const float BigScale = 1.05f;
    private const float FeetBelow = -0.5f;

    private static bool OnTheRails(in TrackBody b) =>
        b.Lateral <= CartRules.HalfWidth + b.Radius && b.Height >= FeetBelow && b.Height <= CartRules.Clearance;

    /// <summary>The cart's front swept the body this step.</summary>
    public static bool Hits(float sPrev, float sNow, in TrackBody b) =>
        sNow > sPrev
        && sPrev + CartRules.HalfLength < b.S && b.S <= sNow + CartRules.HalfLength
        && OnTheRails(b);

    /// <summary>A crawling cart still warns this far ahead: the look-ahead reckons with at least this speed.</summary>
    public const float WarnMinSpeed = 3f;

    /// <summary>A body on the rails ahead, within the distance the cart covers in the given seconds (at <see cref="WarnMinSpeed"/> at least).</summary>
    public static bool InWarning(float sNow, float v, in TrackBody b, float seconds = 3f) =>
        b.S >= sNow && b.S - sNow <= Math.Max(v, WarnMinSpeed) * seconds + CartRules.HalfLength && OnTheRails(b);

    public static bool IsBig(float profileScale, bool isBoss) => isBoss || profileScale >= BigScale;

    public static HazardOutcome Outcome(VictimKind kind, bool big, float lethality) =>
        kind == VictimKind.Enemy && big ? new HazardOutcome(false, 0.35f, 1.5f) : HazardRules.Outcome(kind, lethality);
}
