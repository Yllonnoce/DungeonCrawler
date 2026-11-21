using DungeonCrawler.Party;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>A companion's call of a live switch back (Ruling 17): none due, the early one, or the last.</summary>
public enum BackCall { None, Early, Last }

/// <summary>
/// The live switch back and the broken bridge past it (plan 11). A ride's <c>switch_back</c> is live (deadly) only when a broken
/// bridge's gap lies <see cref="LeadMin"/> to <see cref="LeadMax"/> metres past its points on that ride (<see cref="LiveBack"/>);
/// otherwise it stays fixed. A train still on the slag line as its front reaches the gap falls (<see cref="Falls"/>). Its red signal
/// lamp stands <see cref="SignalPast"/> past the points.
/// <list type="bullet">
/// <item>With a live back the cart crosses out with no return set (<see cref="BackAtSwitchOut"/>) and takes the lever as it stands at its
/// points (<see cref="BackAtPoints"/>).</item>
/// <item>Only a player moves it (<see cref="MayToggle"/>), and the seat offers it only while the cart is across with no return set
/// (<see cref="Offered"/>). A companion calls it twice (<see cref="Call"/>).</item>
/// <item>A ride whose switch_out could leave a correct choice across at the gap has its fall disarmed (<see cref="FallArmed"/>).</item>
/// </list>
/// Engine-free.
/// </summary>
public static class SwitchBackRules
{
    /// <summary>The early call comes with the front this far before the lever; the last this far before the points (Ruling 17).</summary>
    public const float CallEarly = 30f, CallLast = 6f;
    /// <summary>
    /// A return switch's points at least this far short of the gap bring the cart wholly home before it (the crossover's length, the
    /// game's <c>RailController.SwitchStretch</c>).
    /// </summary>
    public const float HomeStretch = 6f;

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

    /// <summary>The return the cart takes at a switch_out: none yet past a live back (it is decided at that back's points), else the fixed back's points.</summary>
    public static float BackAtSwitchOut(bool live, float backPointsS) => live ? float.NaN : backPointsS;

    /// <summary>At a live back's points the cart takes the lever as it stands then: thrown, it crosses home from them; straight, it stays across (NaN).</summary>
    public static float BackAtPoints(bool thrown, float pointsS) => thrown ? pointsS : float.NaN;

    /// <summary>The seat offers a live back only while the cart is across with no return set (Ruling 16): never from its own lane, never twice.</summary>
    public static bool Offered(bool crossing, bool backSet) => crossing && !backSet;

    /// <summary>A deadly lever moves only for a human's E (Ruling 15): a companion's or a parked body's use does nothing. Any other lever moves for anyone.</summary>
    public static bool MayToggle(bool deadly, ControlKind actor) => !deadly || actor is ControlKind.LocalHuman or ControlKind.Remote;

    /// <summary>
    /// The call due now for a live back (Ruling 17), while the cart is across with no return set and the lever not thrown, the front short of
    /// the points: the last (<see cref="CallLast"/> before the points, once) if due, else the early one (<see cref="CallEarly"/> before the
    /// lever, or at once on crossing nearer than that, once and never after the last).
    /// </summary>
    public static BackCall Call(bool crossing, bool backSet, bool thrown, float front, float leverS, float pointsS, bool earlyDone, bool lastDone)
    {
        if (!crossing || backSet || thrown || lastDone || !(front < pointsS)) return BackCall.None; // NaN compares false
        if (front >= pointsS - CallLast) return BackCall.Last;
        return !earlyDone && front >= leverS - CallEarly ? BackCall.Early : BackCall.None;
    }

    /// <summary>
    /// The broken bridge's fall is armed on its ride (carried guard): every switch_out whose points lie short of the gap (<paramref name="gapS"/>,
    /// NaN: no break) has a first switch back after it whose points lie at least <see cref="HomeStretch"/> short of the gap, so a cart that
    /// switched out and took its return (a fixed back, or a live one thrown) is wholly home before it. A ride where one does not would kill a
    /// correct choice: its fall is disarmed instead. All S are the party line's.
    /// </summary>
    public static bool FallArmed(IReadOnlyList<float> switchOuts, IReadOnlyList<float> switchBacks, float gapS)
    {
        if (float.IsNaN(gapS)) return true;
        foreach (var outS in switchOuts)
        {
            if (!(outS < gapS)) continue; // past the gap: never across there
            var first = float.NaN;
            foreach (var backS in switchBacks)
                if (backS > outS && (float.IsNaN(first) || backS < first)) first = backS;
            if (!(gapS - first >= HomeStretch)) return false; // none, or too near the gap or past it (NaN compares false)
        }
        return true;
    }
}
