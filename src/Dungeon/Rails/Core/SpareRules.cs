namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Which stop's spare cart takes over from a wrecked one, and the left-behind spare of a co-op player (plan 5 task 9): when it may
/// be taken, its prompt, and how its chaser runs up behind the train. Engine-free; the server (or solo) and a client's prompt call it.
/// </summary>
public static class SpareRules
{
    /// <summary>
    /// The intermediate stop (index 1 .. stops − 2) with an unused spare nearest to <paramref name="s"/> along the line,
    /// among the stop last left (<paramref name="stopIndex"/>), earlier ones and the very next one (never past a gate not
    /// yet crossed); a tie goes to the earlier stop. −1 when none qualifies. No allocation.
    /// </summary>
    public static int Pick(IReadOnlyList<float> stopS, IReadOnlySet<int> used, int stopIndex, float s)
    {
        var pick = -1;
        var bestD = float.MaxValue;
        var reach = Math.Min(stopS.Count - 2, stopIndex + 1);
        for (var i = 1; i <= reach; i++)
        {
            if (used.Contains(i)) continue;
            var d = Math.Abs(stopS[i] - s);
            if (d < bestD) { bestD = d; pick = i; }
        }
        return pick;
    }
    // ---- the left-behind spare (co-op) ------------------------------------------------------------

    /// <summary>The chaser's body halts this far behind the train's last body.</summary>
    public const float ChaseGap = 1.5f;
    /// <summary>The spare's prompt shows to a player this close to its tub (its nearest point).</summary>
    public const float SpareReach = 3f;
    /// <summary>A chaser that has run up behind the train is hidden this long after its riders are set down.</summary>
    public const float HideSeconds = 2f;
    /// <summary>The chaser counts as up against its limit within this much.</summary>
    const float AtLimit = 0.1f;
    /// <summary>The chaser rolls again only when its limit is this far ahead (CartPhysics holds a cart within 0.02 m of its StopAt).</summary>
    const float RollAgain = 0.05f;

    /// <summary>
    /// The spare at <paramref name="spareStop"/> may be taken after the train: unused, the train gone from that stop (it last arrived at a
    /// later one, or it rolls out of this one or is halted on the line past it: <paramref name="trainStop"/> is the party cart's StopIndex,
    /// which stays k while it leaves k), and no chase live (one at a time: no second train).
    /// </summary>
    public static bool MayChase(int spareStop, bool spareUsed, int trainStop, bool trainHeldAtSpare, bool chaseLive)
        => !spareUsed && !chaseLive && (trainStop > spareStop || trainStop == spareStop && !trainHeldAtSpare);

    /// <summary>A player's hero (played here, or another player's), up and on foot, within <see cref="SpareReach"/> of the spare's tub.</summary>
    public static bool OnFootPlayer(bool player, bool seated, bool downed, float tubDistance)
        => player && !seated && !downed && tubDistance <= SpareReach;

    /// <summary>"Take the spare cart" for an on-foot player when <see cref="MayChase"/> holds; else nothing. The server's check and a client's prompt both ask this.</summary>
    public static string Prompt(bool onFootPlayer, bool mayChase) => onFootPlayer && mayChase ? "Take the spare cart" : "";

    /// <summary>
    /// The S the chaser's centre may not pass before the gap: one half length behind the last body's back
    /// (<paramref name="partyBackS"/> − <paramref name="wagons"/>·<paramref name="wagonSpacing"/> − HalfLength; wagon k's centre is S − k·spacing).
    /// </summary>
    public static float TrainBackS(float partyBackS, int wagons, float wagonSpacing) => partyBackS - wagons * wagonSpacing - CartRules.HalfLength;

    /// <summary>The chaser's Limit (its StopAt), recomputed every tick: <see cref="ChaseGap"/> short of <paramref name="trainBackS"/>, and never past <paramref name="nextStopS"/>.</summary>
    public static float ChaseLimit(float trainBackS, float nextStopS) => Math.Min(nextStopS, trainBackS - ChaseGap);

    /// <summary>Where the chaser is placed: at its stop, or back at its limit while the train's tail still stands there; never before the line's start.</summary>
    public static float ChaserStart(float stopS, float limit) => Math.Max(0f, Math.Min(stopS, limit));

    /// <summary>A boarded chaser rolls while its limit lies ahead of it (it halts behind a slow train and goes on as the train does).</summary>
    public static bool ChaseRolls(bool boarded, float s, float limit) => boarded && limit - s > RollAgain;

    /// <summary>The chase is over: the chaser, boarded and held, stands up against its limit behind a held train (at a stop or halted).</summary>
    public static bool ChaseDone(bool boarded, bool chaserHeld, bool trainHeld, float s, float limit)
        => boarded && chaserHeld && trainHeld && limit - s <= AtLimit;
    // ---- the line ahead of the chaser ---------------------------------------------------------------

    /// <summary>A hop lands the chaser's back end this far past the hole.</summary>
    public const float HopClear = 0.05f;

    /// <summary>
    /// The chaser judges no set piece, but it never floats over a pit: when its front reaches a hole in the party line this step
    /// (<paramref name="holes"/>: a jump's gap, a fallen trestle, From..To in party S, any order), the new front S that lands its whole
    /// body just past the first such hole (and past any hole that landing falls in), else null. No allocation.
    /// </summary>
    public static float? HopPast(float prevFrontS, float frontS, IReadOnlyList<(float From, float To)> holes)
    {
        const float tub = 2f * CartRules.HalfLength;
        float? landing = null;
        var first = float.PositiveInfinity;
        for (var i = 0; i < holes.Count; i++)
        {
            var (from, to) = holes[i];
            if (prevFrontS < to && frontS >= from && from < first) { first = from; landing = to + tub + HopClear; }
        }
        if (landing is not { } land) return null;
        for (var pass = 0; pass <= holes.Count; pass++) // a landing in the next hole hops that one too
        {
            var moved = false;
            for (var i = 0; i < holes.Count; i++)
            {
                var (from, to) = holes[i];
                if (land - tub < to && land > from && to + tub + HopClear > land) { land = to + tub + HopClear; moved = true; }
            }
            if (!moved) break;
        }
        return land;
    }

    /// <summary>The chaser's limit with a live ore heap's face (<paramref name="faceS"/>, +∞ for none) ahead of its front: it halts one half length short of the face, as the party cart does, and never drives through.</summary>
    public static float HeapLimit(float limit, float frontS, float faceS)
        => faceS >= frontS - RollAgain ? Math.Min(limit, faceS - CartRules.HalfLength) : limit;

    /// <summary>The chaser's prompt: "Get out" (the board prompt's word) for a hero seated in it while it stands, else nothing (nobody boards a chaser).</summary>
    public static string ChaserPrompt(bool seatedHere, bool held)
        => seatedHere && held ? RideInputRules.BoardPrompt(seatedHere: true, held: true, lastStop: false, heroSeatFree: false, inReach: true) : "";
}
