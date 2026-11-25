namespace DungeonCrawler.Dungeon.Rails;

/// <summary>A body near the track: S is its closest offset on the curve, Lateral the horizontal distance to that point, Height its feet y minus the track y there.</summary>
public readonly record struct TrackBody(float S, float Lateral, float Height, float Radius);

/// <summary>When a cart runs a body over, and what that does. Engine-free.</summary>
public static class RunOverRules
{
    /// <summary>The hazard source each cart names when it runs a body over (the log line, the death cause, the orc's gloat).</summary>
    public const string PartyCartSource = "the warden cart", SlagCartSource = "the slag cart", RunawaySource = "a runaway wagon", ChaserSource = "the spare cart",
        TrafficSource = "a slag cart";

    /// <summary>A run-over by one of the carts (the party's, an armed slag cart, the slag line's traffic, a runaway, a left-behind player's chaser): the orc says RolledOver, not the Press's Crushed.</summary>
    public static bool IsCartSource(string? source) => source is PartyCartSource or SlagCartSource or TrafficSource or RunawaySource or ChaserSource;

    public const float BigBodyPlatingDamage = 15f;
    /// <summary>A hero who gets out at or above this speed is run over by their own cart.</summary>
    public const float ThrownAtSpeed = 1f;
    private const float BigScale = 1.05f;
    private const float FeetBelow = -0.5f;

    /// <summary>The body stands on the rails: within the cart's half width (plus its radius) of the line, its feet between just below the track and the cart's clearance.</summary>
    public static bool OnRails(in TrackBody b) =>
        b.Lateral <= CartRules.HalfWidth + b.Radius && b.Height >= FeetBelow && b.Height <= CartRules.Clearance;

    /// <summary>
    /// The cart's leading face swept the body this step. A cart running toward higher S (<paramref name="dir"/> +1) has its front
    /// at S + HalfLength, one running toward lower S (−1) at S − HalfLength; it must be moving that way. <paramref name="lead"/> moves the
    /// face on by that much in the way of travel: a train rolling back leads with its last wagon's back (plan 16, <c>wagons × Spacing</c>).
    /// The face's place before the step is excluded and its place after it included, so a body is swept once.
    /// </summary>
    public static bool Hits(float sPrev, float sNow, in TrackBody b, int dir = 1, float lead = 0f)
    {
        if (dir >= 0 ? sNow <= sPrev : sNow >= sPrev) return false;
        float reach = CartRules.HalfLength + lead;
        float frontPrev = sPrev + dir * reach, frontNow = sNow + dir * reach;
        bool swept = dir >= 0 ? frontPrev < b.S && b.S <= frontNow : frontNow <= b.S && b.S < frontPrev;
        return swept && OnRails(b);
    }

    /// <summary>A crawling cart still warns this far ahead: the look-ahead reckons with at least this speed.</summary>
    public const float WarnMinSpeed = 3f;

    /// <summary>
    /// A body on the rails ahead in the way of travel (<paramref name="dir"/>), within the distance the cart covers in the given seconds (at
    /// <see cref="WarnMinSpeed"/> at least) of its leading face, which <paramref name="lead"/> moves on (a train rolling back: its last wagon's
    /// back). Measured from <paramref name="lead"/> past the cart's middle: a body under the train behind the face is not warned.
    /// </summary>
    public static bool InWarning(float sNow, float v, in TrackBody b, float seconds = 3f, int dir = 1, float lead = 0f)
    {
        var ahead = (dir >= 0 ? b.S - sNow : sNow - b.S) - lead;
        return ahead >= 0f && ahead <= Math.Max(v, WarnMinSpeed) * seconds + CartRules.HalfLength && OnRails(b);
    }

    public static bool IsBig(float profileScale, bool isBoss) => isBoss || profileScale >= BigScale;

    public static HazardOutcome Outcome(VictimKind kind, bool big, float lethality) =>
        kind == VictimKind.Enemy && big ? new HazardOutcome(false, 0.35f, 1.5f) : HazardRules.Outcome(kind, lethality);
}
