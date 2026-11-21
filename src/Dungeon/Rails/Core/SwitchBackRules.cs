namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The live switch back and the broken bridge past it (plan 11). A ride's <c>switch_back</c> is live (deadly) only when a broken
/// bridge's gap lies <see cref="LeadMin"/> to <see cref="LeadMax"/> metres past its points on that ride (<see cref="LiveBack"/>);
/// otherwise it stays fixed. A train still on the slag line as its front reaches the gap falls (<see cref="Falls"/>). Its red signal
/// lamp stands <see cref="SignalPast"/> past the points. Engine-free.
/// </summary>
public static class SwitchBackRules
{
    /// <summary>The nearest and farthest the gap may lie past the back's points for the back to be live.</summary>
    public const float LeadMin = 12f, LeadMax = 40f;
    /// <summary>A live back's red signal lamp stands this far past its points, on the slag line's outer side.</summary>
    public const float SignalPast = 3f;

    /// <summary>A switch back at party S <paramref name="backPointsS"/> is live: the gap (<paramref name="gapS"/>, NaN for none) lies within [<see cref="LeadMin"/>, <see cref="LeadMax"/>] past it.</summary>
    public static bool LiveBack(float backPointsS, float gapS)
    {
        var lead = gapS - backPointsS;
        return lead >= LeadMin && lead <= LeadMax; // NaN compares false
    }

    /// <summary>
    /// The train falls this step: its front crossed the gap's near edge (<paramref name="prevFront"/> short of <paramref name="gapS"/>,
    /// <paramref name="front"/> at or past it) with the cart across on the slag line there (<paramref name="blendAtGap"/> ≥ 0.5).
    /// </summary>
    public static bool Falls(float prevFront, float front, float gapS, float blendAtGap)
        => prevFront < gapS && gapS <= front && blendAtGap >= 0.5f;
}
