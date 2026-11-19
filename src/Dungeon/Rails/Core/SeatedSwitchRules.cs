namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What the seated window needs of a lever: the party line's S at the lever and at its points, and whether it can be thrown at all.</summary>
public readonly record struct SeatedLever(float LeverS, float PointsS, bool Fixed, bool Taken);

/// <summary>
/// Throwing a switch from the seat (plan 8, the seated-switch addendum to the floor-12 spec): while a throwable lever of the armed ride
/// comes up, a seated, up rider of the party's rolling cart is offered it from <see cref="WindowAhead"/> metres before the lever until
/// the cart's front reaches its points (or the lever is taken). E or the VR grip then toggles it with no aiming, and never gets them out.
/// For <see cref="GraceSeconds"/> after the window closes E does nothing, so a press still mashed past the points cannot throw the rider
/// out at speed. The server accepts a rider's use in their window as well as by reach, but refuses another rider's set-back within
/// <see cref="SetBackGuardSeconds"/> of a toggle, so two riders cued by the same call do not undo each other into the heap. Engine-free.
/// </summary>
public static class SeatedSwitchRules
{
    /// <summary>The window opens with the cart's front this far before the lever (about 2.5 s at the 6 m/s cruise).</summary>
    public const float WindowAhead = 15f;
    /// <summary>After the window closes, a seated rider's E does nothing for this long (timed on their own machine).</summary>
    public const double GraceSeconds = 1.0;
    /// <summary>The server refuses another rider's out-of-reach set-back this soon after a toggle.</summary>
    public const double SetBackGuardSeconds = 1.0;

    /// <summary>
    /// The rider is offered <paramref name="lever"/> from the seat: seated in the party's cart, up, the cart rolling, the lever throwable
    /// (not fixed, not taken), and the front at most <see cref="WindowAhead"/> before the lever and short of the points. A NaN front never is.
    /// </summary>
    public static bool InWindow(bool seatedInPartyCart, bool riderUp, bool rolling, float frontS, SeatedLever lever)
        => seatedInPartyCart && riderUp && rolling && !lever.Fixed && !lever.Taken
           && frontS >= lever.LeverS - WindowAhead && frontS < lever.PointsS;

    /// <summary>The in-window lever nearest the front by S (the first on a tie), or −1. Runs every physics frame: it does not allocate.</summary>
    public static int Nearest(bool seatedInPartyCart, bool riderUp, bool rolling, float frontS, IReadOnlyList<SeatedLever> levers)
    {
        if (!seatedInPartyCart || !riderUp || !rolling) return -1;
        var best = -1;
        var bestGap = float.PositiveInfinity;
        for (var i = 0; i < levers.Count; i++)
        {
            var lever = levers[i];
            if (!InWindow(true, true, true, frontS, lever)) continue;
            var gap = Math.Abs(lever.LeverS - frontS);
            if (gap < bestGap) { best = i; bestGap = gap; }
        }
        return best;
    }

    /// <summary>
    /// A seated rider is in the grace after a window: <see cref="GraceSeconds"/> from <paramref name="windowClosedAt"/> (their own clock;
    /// <see cref="double.NegativeInfinity"/>: never closed), E does nothing.
    /// </summary>
    public static bool InGrace(bool seated, double now, double windowClosedAt)
        => seated && now >= windowClosedAt && now - windowClosedAt < GraceSeconds;

    /// <summary>
    /// The server refuses <paramref name="actor"/>'s window-only use of a <paramref name="thrown"/> lever (a set-back) within
    /// <see cref="SetBackGuardSeconds"/> of another rider's toggle. A throw is never refused, nor a rider's own second press.
    /// </summary>
    public static bool RefuseSetBack(bool thrown, double now, double lastToggledAt, string? lastToggledBy, string actor)
        => thrown && lastToggledBy != null && !string.Equals(lastToggledBy, actor, StringComparison.Ordinal) && now - lastToggledAt < SetBackGuardSeconds;
}
