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
    /// <summary>The server refuses another rider's set-back from inside their window (in reach or not) this soon after a toggle.</summary>
    public const double SetBackGuardSeconds = 1.0;
    /// <summary>A second window press on the same lever is ignored on the presser's own machine this long, unless the lever changed meanwhile.</summary>
    public const double RepressGuardSeconds = 0.5;

    /// <summary>
    /// The rider is offered <paramref name="lever"/> from the seat: seated in the party's cart, up, the cart rolling, the lever throwable
    /// (not fixed, not taken), and the front at most <see cref="WindowAhead"/> before the lever and short of the points. A NaN front never is.
    /// </summary>
    public static bool InWindow(bool seatedInPartyCart, bool riderUp, bool rolling, float frontS, SeatedLever lever)
        => seatedInPartyCart && riderUp && rolling && !lever.Fixed && !lever.Taken
           && frontS >= lever.LeverS - WindowAhead && frontS < lever.PointsS;

    /// <summary>
    /// The server accepts a seated press this far before its own window opens (Task 7 fix round): a co-op client's cart can run a few metres
    /// ahead of the server's (it extrapolates; a loaded server steps late), so a press on the client's first window frame can reach the
    /// server short of its window. The offer itself (<see cref="InWindow"/>) still opens at <see cref="WindowAhead"/>. 5 m (plan 11 Task 8):
    /// the measured client leads were 1.5–2.8 m, and the points and Taken guards keep a larger lead safe.
    /// </summary>
    public const float AcceptLead = 5f;

    /// <summary>
    /// The server's acceptance of a seated press on <paramref name="lever"/>: as <see cref="InWindow"/>, the window's start moved
    /// <see cref="AcceptLead"/> earlier; never at or past the points (the cart has taken the lever as it stood).
    /// </summary>
    public static bool Accepts(bool seatedInPartyCart, bool riderUp, bool rolling, float frontS, SeatedLever lever)
        => seatedInPartyCart && riderUp && rolling && !lever.Fixed && !lever.Taken
           && frontS >= lever.LeverS - WindowAhead - AcceptLead && frontS < lever.PointsS;

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
    /// The server refuses <paramref name="actor"/>'s in-window use of a <paramref name="thrown"/> lever (a set-back), in reach or not, within
    /// <see cref="SetBackGuardSeconds"/> of another rider's toggle. A throw is never refused, nor a rider's own second press.
    /// </summary>
    public static bool RefuseSetBack(bool thrown, double now, double lastToggledAt, string? lastToggledBy, string actor)
        => thrown && lastToggledBy != null && !string.Equals(lastToggledBy, actor, StringComparison.Ordinal) && now - lastToggledAt < SetBackGuardSeconds;

    /// <summary>
    /// A seated-window press on <paramref name="sameLever"/> the last one went to is ignored while the lever still stands as that press
    /// left it on this screen (<paramref name="thrownNow"/> == <paramref name="thrownAfterPress"/>) and less than
    /// <see cref="RepressGuardSeconds"/> has passed: a client's double tap inside one round trip, or a solo mash, would otherwise set
    /// back what the first press threw. A client's lever changes when the server's answer arrives, and then its prompt is the new one.
    /// </summary>
    public static bool IgnoreRepress(bool sameLever, bool thrownNow, bool thrownAfterPress, double now, double pressedAt)
        => sameLever && thrownNow == thrownAfterPress && now - pressedAt < RepressGuardSeconds;
}

/// <summary>
/// One played body's seated window over time (M1): the 1 s grace follows only a window that closed while the body was played and up.
/// A body downed, or swapped out of the player's hands, mid-window owes no grace later. Times are this machine's, in seconds. Engine-free.
/// </summary>
public sealed class SeatedWindowClock
{
    private bool _open;
    private double _closedAt = double.NegativeInfinity;

    /// <summary>The window offers a lever this view tick.</summary>
    public void Offered() => _open = true;

    /// <summary>The body is not played and up this tick (downed, or another member is played): a window open now is forgotten, not closed.</summary>
    public void NotPlayed() => _open = false;

    /// <summary>Played and up, the window offers nothing this tick: true while in the grace after the last close (<see cref="SeatedSwitchRules.InGrace"/>).</summary>
    public bool NotOffered(bool seated, double now)
    {
        if (_open)
        {
            _closedAt = now;
            _open = false;
        }
        return SeatedSwitchRules.InGrace(seated, now, _closedAt);
    }
}

/// <summary>The re-press guard's memory of the last seated-window press (<see cref="SeatedSwitchRules.IgnoreRepress"/>), on the presser's machine. Engine-free.</summary>
public sealed class SeatedPressGuard
{
    private object? _lever;
    private bool _thrownAfter;
    private double _at = double.NegativeInfinity;

    /// <summary>A window press on <paramref name="lever"/>, which now stands <paramref name="thrownNow"/> here, is to be ignored.</summary>
    public bool Ignore(object lever, bool thrownNow, double now)
        => SeatedSwitchRules.IgnoreRepress(ReferenceEquals(lever, _lever), thrownNow, _thrownAfter, now, _at);

    /// <summary>A window press went to <paramref name="lever"/> at <paramref name="now"/>, leaving it <paramref name="thrownAfter"/> on this screen.</summary>
    public void Pressed(object lever, bool thrownAfter, double now)
    {
        _lever = lever;
        _thrownAfter = thrownAfter;
        _at = now;
    }
}
