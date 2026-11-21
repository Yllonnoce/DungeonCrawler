using DungeonCrawler.Party;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>A companion's call of a live switch back (Ruling 17): none due, the early one, or the last.</summary>
public enum BackCall { None, Early, Last }

/// <summary>A press on a deadly lever after its moment (<see cref="SwitchBackRules.Late"/>): not late, too late (straight), or already thrown.</summary>
public enum LatePress { None, TooLate, AlreadyThrown }

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
    /// A return switch's points at least this far short of the gap bring the cart wholly home before it: the crossover's length (the game's
    /// <c>RailController.SwitchStretch</c> is defined as this one).
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

    /// <summary>
    /// At a switch back's points, with the cart across and its return still open, the cart goes home from them (final review I1, ruling R-lone):
    /// any back whose lever is thrown (a fixed one always is), or a live one left straight with no human aboard able to throw it
    /// (<see cref="AbleToThrow"/>: nobody could have prevented the fall, so the cart takes the safe line). Otherwise it stays across.
    /// </summary>
    public static bool TakesHome(bool thrown, bool deadly, bool ableHumanAboard) => thrown || deadly && !ableHumanAboard;

    /// <summary>The log line when a live back is taken home because no human aboard could throw it (subtitle only).</summary>
    public const string SafeLineLog = "No one could reach the switch — the cart takes the safe line.";

    /// <summary>A rider who could throw a deadly lever from the seat: a human (<see cref="MayToggle"/>: not a companion, a bot or a parked body), up (not downed or fallen) and seated in the party's cart.</summary>
    public static bool AbleToThrow(ControlKind actor, bool downed, bool seatedInPartyCart) => MayToggle(true, actor) && !downed && seatedInPartyCart;

    /// <summary>
    /// A press on a deadly lever after its moment (Ruling 21): none while it is untaken and the front short of its points (a NaN front never is
    /// past them); otherwise, the lever straight, too late (the server refuses it and says so); thrown, already thrown (ignored quietly: never
    /// "Too late" to the rider who threw it, and never un-thrown).
    /// </summary>
    public static LatePress Late(bool taken, bool thrown, float front, float pointsS)
        => !(taken || front >= pointsS) ? LatePress.None : thrown ? LatePress.AlreadyThrown : LatePress.TooLate; // NaN compares false

    /// <summary>The line the server sends the presser of a deadly lever it refused as too late.</summary>
    public const string TooLateLine = "Too late to throw it.";

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
    /// Where the watchdog frees a train stuck across on the slag line short of a live back it has not taken (front S): with the lever straight
    /// and the front short of the back's seated window, the window's start (<paramref name="leverS"/> − <see cref="SeatedSwitchRules.WindowAhead"/>),
    /// so the calls and the offer run as they would; inside the window and short of the last call, the last call's mark (<paramref name="pointsS"/>
    /// − <see cref="CallLast"/>), so "Switch back — now!" still comes and the offer stays up; past that, or thrown, the points (taken there as it
    /// stands). Never past a window the player was not shown, nor past the last call unheard.
    /// </summary>
    public static float GrindBackTo(float front, float leverS, float pointsS, bool thrown)
    {
        if (thrown) return pointsS;
        var window = leverS - SeatedSwitchRules.WindowAhead;
        if (front < window) return window;
        var last = pointsS - CallLast;
        return front < last ? last : pointsS;
    }

    /// <summary>
    /// The broken bridge's fall is armed on its ride (carried guard, final review I1): every switch_out whose points lie short of the gap
    /// (<paramref name="gapS"/>, NaN: no break) has a switch back after it short of the gap, and every switch back between it and the gap has its
    /// points at least <see cref="HomeStretch"/> short of the gap. A cart across with its return open is taken home by any thrown back it reaches
    /// (a fixed back, or a live one thrown: <see cref="TakesHome"/>), so each of them must bring it wholly home before the gap. A ride where one
    /// does not would kill a correct choice, or show a thrown return the cart cannot finish: its fall is disarmed instead. All S are the party line's.
    /// </summary>
    public static bool FallArmed(IReadOnlyList<float> switchOuts, IReadOnlyList<float> switchBacks, float gapS)
    {
        if (float.IsNaN(gapS)) return true;
        foreach (var outS in switchOuts)
        {
            if (!(outS < gapS)) continue; // past the gap: never across there
            var any = false;
            foreach (var backS in switchBacks)
            {
                if (!(backS > outS && backS < gapS)) continue; // behind the switch_out, or past the gap: never reached across short of it
                if (!(gapS - backS >= HomeStretch)) return false; // too near the gap: still half across there
                any = true;
            }
            if (!any) return false; // no back before the gap: across to it whatever the player does
        }
        return true;
    }
}
