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

    /// <summary>Traffic started with the party's cart past the gap starts this far past the gap's far edge on the enemy track (its back end clear of the drop).</summary>
    public const float TrafficPastGap = 2f;
    /// <summary>A cart gone with its front this far past the gap's near edge at most went through the gap (its last step's reach), not off the rails.</summary>
    public const float PlungeReach = 1.5f;
    /// <summary>A plunge is told in the log only with a hero this close to the gap.</summary>
    public const float PlungeHeard = 40f;

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

    /// <summary>
    /// Where a traffic cart starts on the enemy track: just past the gap's far edge (<paramref name="gapFarEnemyS"/> + <see cref="TrafficPastGap"/>)
    /// once the party's cart, held at a stop, is past the gap (<paramref name="partyS"/> beyond <paramref name="gapPartyS"/>), so the stops
    /// past the broken bridge keep their traffic; else (or with no break: NaN) the line's start, 0.
    /// </summary>
    public static float TrafficStartS(float partyS, float gapPartyS, float gapFarEnemyS)
        => partyS > gapPartyS && !float.IsNaN(gapFarEnemyS) ? gapFarEnemyS + TrafficPastGap : 0f; // NaN compares false

    /// <summary>
    /// A slag or traffic cart gone off the line at <paramref name="cartS"/> (its middle, on the enemy track) went through the broken bridge's gap
    /// (near edge <paramref name="gapEnemyS"/>): its front had just reached it. Every peer reads a derail this way (a client knows only where the cart was).
    /// </summary>
    public static bool PlungedAt(float cartS, float gapEnemyS)
    {
        var past = cartS + CartRules.HalfLength - gapEnemyS;
        return past >= -0.25f && past <= PlungeReach; // NaN compares false
    }

    /// <summary>The plunge is told in the log: the nearest hero stands within <see cref="PlungeHeard"/> of the gap.</summary>
    public static bool PlungeTold(float nearestHeroDistance) => nearestHeroDistance <= PlungeHeard;
}
