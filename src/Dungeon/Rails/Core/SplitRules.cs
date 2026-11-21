namespace DungeonCrawler.Dungeon.Rails;

/// <summary>Where the party's train goes at the split's points (<see cref="SplitRules.PartyRoute"/>).</summary>
public enum SplitRoute { Main, Spur }

/// <summary>A companion's call of the split (Ruling 9): none due, wait for it, not yet (it is the spur), or now.</summary>
public enum SplitCall { None, Wait, NotYet, Now }

/// <summary>
/// Ride 4's split (plan 12, Rulings 3-9): a party-line spur off the layout's points (<c>RailSplit.Points</c>, its cell centre on the party
/// track), <see cref="SpurLength"/> m long. The split's piece must stand within <see cref="AtSlack"/> of the points, and it is live only
/// with the spur's end over the crevasse (<see cref="Live"/>). "Throw it after you pass" is the lever's design: it stands
/// <see cref="LeverPast"/> past the points and the seat offers it, while the ride's runaway is coming, from the window's start until the
/// runaway takes the points (<see cref="Offered"/>). The window opens at the points less the straight run before them, at most
/// <see cref="WindowAhead"/> (ruling R-bend, <see cref="WindowReach"/>), so no corner lies inside it; and a throw anywhere in it short of
/// the points is held to them (<see cref="HoldPress"/>: the hold zone covers the whole window by construction, <see cref="HoldReach"/>), so a
/// press before the points never sends the party down the spur. Engine-free.
/// </summary>
public static class SplitRules
{
    /// <summary>The spur's length from its points: the lane blend's 6 m onto the slag line and 5 m more bending out over the crevasse.</summary>
    public const float SpurLength = 11f;
    /// <summary>The split piece's At must lie within this of the layout's points.</summary>
    public const float AtSlack = 8f;
    /// <summary>The lever stands this far past the points, on the party lane's side away from the slag line (clear of the spur).</summary>
    public const float LeverPast = 3f;
    /// <summary>Past the lane blend the spur bends this far further out from the slag line, over its last 5 m.</summary>
    public const float SpurOut = 3.5f;
    /// <summary>A train that took the spur falls with its front this far past the points: the blend's 6 m onto the slag line and a metre (7 m).</summary>
    public const float FallPast = SwitchBackRules.HomeStretch + 1f;
    /// <summary>A throw is held to the points this far short of them at least (Ruling 7: a co-op client's lead); see <see cref="HoldReach"/>.</summary>
    public const float HoldLead = SeatedSwitchRules.AcceptLead;
    /// <summary>"Now" is called with the front this far past the points.</summary>
    public const float NowPast = 1f;
    /// <summary>The seated window reaches at most this far before the points: the lever's seated window (<see cref="SeatedSwitchRules.WindowAhead"/> before it) from its place past them (12 m).</summary>
    public const float WindowAhead = SeatedSwitchRules.WindowAhead - LeverPast;

    /// <summary>The straight before the points ends where the line turns more than this from the points' heading (degrees) ...</summary>
    public const float StraightTurn = 2f;
    /// <summary>... or strays more than this from the line through the points (metres).</summary>
    public const float StraightStray = 0.1f;

    /// <summary>The spur's end, <paramref name="pointsS"/> + <see cref="SpurLength"/>, lies over the crevasse, a metre inside each edge. NaN is never live.</summary>
    public static bool Live(float pointsS, float crevasseFromS, float crevasseToS)
    {
        var end = pointsS + SpurLength;
        return end >= crevasseFromS + 1f && end <= crevasseToS - 1f; // NaN compares false
    }

    /// <summary>Ruling R-bend: how far before the points the window opens, the straight run before them (<see cref="StraightBefore"/>) up to <see cref="WindowAhead"/>. NaN or less than none: none.</summary>
    public static float WindowReach(float straightBefore) => straightBefore > 0f ? Math.Min(WindowAhead, straightBefore) : 0f; // NaN compares false

    /// <summary>How far short of the points a throw is held to them: <see cref="HoldLead"/>, or the whole window before them when that is longer (the controller's ruling: never less than the window).</summary>
    public static float HoldReach(float windowAhead) => Math.Max(HoldLead, windowAhead);

    /// <summary>
    /// The party's branch, decided as its front reaches the points (Ruling 6): the spur only with the lever thrown and a human aboard able to
    /// throw it (<see cref="SwitchBackRules.AbleToThrow"/>); thrown with nobody able (ruling R-lone), or straight, the main line.
    /// </summary>
    public static SplitRoute PartyRoute(bool thrown, bool ableHumanAboard) => thrown && ableHumanAboard ? SplitRoute.Spur : SplitRoute.Main;

    /// <summary>
    /// A throw (<paramref name="throwing"/>: straight to thrown) pressed with the front short of the points by at most <see cref="HoldReach"/>
    /// of the window (<paramref name="windowAhead"/>) is held, and applied the instant after the party's branch is decided straight (Ruling 7).
    /// A set-back is never held. A NaN front never is.
    /// </summary>
    public static bool HoldPress(bool throwing, float front, float pointsS, float windowAhead)
        => throwing && front < pointsS && front >= pointsS - HoldReach(windowAhead);

    /// <summary>Plan 8's set-back guard (another rider's set-back within 1 s of a toggle is refused) applies to the split only past its points (Ruling 7).</summary>
    public static bool GuardsSetBack(float front, float pointsS) => front >= pointsS;

    /// <summary>
    /// The seat (and the look ray) offers the split's lever (Ruling 5): its ride armed with the split live (<paramref name="armed"/>), the ride's
    /// runaway coming, the lever not taken (the runaway has not reached the points), and the front at most <paramref name="windowAhead"/> before
    /// the points (<see cref="WindowReach"/>). A NaN front never is.
    /// </summary>
    public static bool Offered(bool armed, bool runawayComing, float front, float pointsS, bool taken, float windowAhead)
        => armed && runawayComing && !taken && front >= pointsS - windowAhead;

    /// <summary>
    /// The call due now (Ruling 9), each once an arming, only while the runaway is coming:
    /// <list type="bullet">
    /// <item>Not yet: the lever thrown with the front short of the hold zone (<see cref="HoldReach"/>) and within <see cref="WindowAhead"/> of the
    /// points. A throw from the window is always held, so only a lever thrown some other way meets it.</item>
    /// <item>Wait: the lever straight, the front in the window (<paramref name="windowAhead"/> before the points) and short of the points.</item>
    /// <item>Now: the lever straight, the front <see cref="NowPast"/> or more past the points.</item>
    /// </list>
    /// </summary>
    public static SplitCall Call(bool runawayComing, bool thrown, float front, float pointsS, bool waitDone, bool notYetDone, bool nowDone, float windowAhead)
    {
        if (!runawayComing || float.IsNaN(front)) return SplitCall.None;
        if (thrown)
            return !notYetDone && front >= pointsS - WindowAhead && front < pointsS - HoldReach(windowAhead) ? SplitCall.NotYet : SplitCall.None;
        if (!waitDone && front >= pointsS - windowAhead && front < pointsS) return SplitCall.Wait;
        return !nowDone && front >= pointsS + NowPast ? SplitCall.Now : SplitCall.None;
    }

    /// <summary>The lever's prompt (Ruling 5): which way a throw sends, short of the points and past them; thrown, its set-back.</summary>
    public static string Prompt(bool past, bool thrown)
        => thrown ? "Set the split back" : past ? "Throw the split behind us" : "Throw the split — we take the spur!";

    /// <summary>A runaway gone from the spur at spur S <paramref name="s"/> (its middle) went off its end into the crevasse (its last step's reach): every peer reads it so.</summary>
    public static bool PlungedOffSpur(float s, float spurLength) => s >= spurLength - SwitchBackRules.PlungeReach; // NaN compares false

    /// <summary>The log line when the split stands thrown at the points with no human aboard able to set it back (ruling R-lone).</summary>
    public const string SafeLineLog = "No one could set the split back — the cart keeps the main line.";

    /// <summary>
    /// The straight run before the points, measured on the track (ruling R-bend): from <paramref name="pointsS"/> back in <paramref name="step"/>
    /// steps, up to <paramref name="reach"/>, until a step turns more than <see cref="StraightTurn"/> from the points' heading or a point strays
    /// more than <see cref="StraightStray"/> off the line through them. <paramref name="point"/> is the track's ground-plane place at S (X, Z).
    /// </summary>
    public static float StraightBefore(Func<float, (float X, float Z)> point, float pointsS, float reach, float step = 0.25f)
    {
        var p0 = point(pointsS);
        var back = point(pointsS - step);
        float dx = p0.X - back.X, dz = p0.Z - back.Z;
        var len = MathF.Sqrt(dx * dx + dz * dz);
        if (!(len > step * 0.5f)) return 0f; // no line behind the points
        dx /= len;
        dz /= len;
        var cosTurn = MathF.Cos(StraightTurn * MathF.PI / 180f);
        var good = 0f;
        var prev = p0;
        for (var d = step; d <= reach + 1e-4f; d += step)
        {
            var q = point(pointsS - d);
            float sx = prev.X - q.X, sz = prev.Z - q.Z;
            var seg = MathF.Sqrt(sx * sx + sz * sz);
            if (!(seg > step * 0.5f)) break; // the line's start
            if ((sx * dx + sz * dz) / seg < cosTurn) break; // a turn
            if (MathF.Abs(dx * (q.Z - p0.Z) - dz * (q.X - p0.X)) > StraightStray) break; // a drift off the line
            good = Math.Min(d, reach);
            prev = q;
        }
        return good;
    }
}
