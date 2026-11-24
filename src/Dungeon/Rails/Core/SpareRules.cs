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

    /// <summary>A chaser or relief halts this far short of the exit bridge's near edge (its front at <c>bridgeFromS</c> − this), never on it (plan 15 Ruling 29).</summary>
    public const float BridgeClear = 0.5f;

    /// <summary>
    /// The chaser's (and a relief's) limit on a line with a live parked train (plan 15, Ruling 29): the nearest of <see cref="ChaseLimit(float, float)"/>,
    /// <paramref name="parkedBackS"/> − <see cref="ChaseGap"/> (behind the parked train's place, <see cref="ParkedRules.BackS"/>, whether or not it stands)
    /// and <paramref name="bridgeFromS"/> − half a cart − <see cref="BridgeClear"/> (short of the exit bridge). NaN arguments are ignored; every argument
    /// and the result is a centre S in <see cref="TrainBackS"/>'s sense. So no chaser or relief ever runs through a standing lift or onto the bridge.
    /// </summary>
    public static float ChaseLimit(float trainBackS, float nextStopS, float parkedBackS, float bridgeFromS)
    {
        var limit = float.NaN;
        if (!float.IsNaN(nextStopS)) limit = nextStopS;
        if (!float.IsNaN(trainBackS)) limit = Nearest(limit, trainBackS - ChaseGap);
        if (!float.IsNaN(parkedBackS)) limit = Nearest(limit, parkedBackS - ChaseGap);
        if (!float.IsNaN(bridgeFromS)) limit = Nearest(limit, bridgeFromS - CartRules.HalfLength - BridgeClear);
        return limit;
    }

    private static float Nearest(float a, float b) => float.IsNaN(a) ? b : Math.Min(a, b);

    /// <summary>
    /// The log line when a chaser (or a relief, <paramref name="relief"/>) ends its chase: behind the train, or, halted at the parked train's place
    /// (<paramref name="atParked"/>, Ruling 29: the train is across the lava, past the exit bridge), short of the bridge (Task 9).
    /// </summary>
    public static string PullUpLine(bool relief, bool atParked)
        => $"The {(relief ? "relief" : "spare")} cart {(atParked ? "halts short of the exit bridge" : "pulls up behind the train")}.";

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

    /// <summary>
    /// Where a hop sets the chaser down (all centre S): <see cref="S"/>, and whether it stands there (<see cref="Held"/>: up against its
    /// limit); <see cref="NearS"/>, the centre with the front at the first hole's near lip (what this step swept up to the hole is judged
    /// to there); <see cref="SweepFromS"/>, the centre with the front at the last hopped hole's far lip (the landing is judged from there
    /// to <see cref="S"/>, so the pit itself sweeps nothing). <see cref="Landed"/>: it got past; false, it stopped short of the near lip.
    /// </summary>
    public readonly record struct HopLanding(float S, bool Held, float NearS, float SweepFromS)
    {
        public bool Landed => S > NearS;
    }

    /// <summary>
    /// The chaser's front reached a hole this step (<see cref="HopPast"/>): where it comes down, never past <paramref name="limit"/> and
    /// never over a pit. A landing past the limit is pulled back to the limit if the whole body still clears the hole there (its back at
    /// the far lip or beyond); otherwise it stops short, its front at the first hole's near lip (Held). Null when no hole was reached.
    /// No allocation.
    /// </summary>
    public static HopLanding? Hop(float prevFrontS, float frontS, IReadOnlyList<(float From, float To)> holes, float limit)
    {
        if (HopPast(prevFrontS, frontS, holes) is not { } land) return null;
        var first = float.PositiveInfinity;
        for (var i = 0; i < holes.Count; i++)
        {
            var (from, to) = holes[i];
            if (prevFrontS < to && frontS >= from && from < first) first = from;
        }
        var near = first - CartRules.HalfLength;
        var s = land - CartRules.HalfLength;
        var sweepFrom = s - 2f * CartRules.HalfLength - HopClear; // the front at the far lip of the last hole hopped
        if (s > limit) s = limit;
        if (s <= near || OverHole(s, holes)) return new HopLanding(near, true, near, near);
        return new HopLanding(s, limit - s < 0.02f, near, Math.Min(sweepFrom, s));
    }

    /// <summary>A chaser whose centre is at <paramref name="s"/> stands over one of <paramref name="holes"/> (any part of its body). No allocation.</summary>
    public static bool OverHole(float s, IReadOnlyList<(float From, float To)> holes)
    {
        for (var i = 0; i < holes.Count; i++)
        {
            var (from, to) = holes[i];
            if (s - CartRules.HalfLength < to && s + CartRules.HalfLength > from) return true;
        }
        return false;
    }

    /// <summary>
    /// The chaser's limit pulled back short of any hole it would stand over there: its front at that hole's near lip. A limit past a
    /// hole by less than a body cannot be reached by a hop, so the chaser waits at the lip instead of hopping and stopping short each
    /// tick. No allocation.
    /// </summary>
    public static float HoleLimit(float limit, IReadOnlyList<(float From, float To)> holes)
    {
        for (var pass = 0; pass <= holes.Count; pass++)
        {
            var moved = false;
            for (var i = 0; i < holes.Count; i++)
            {
                var (from, to) = holes[i];
                if (limit - CartRules.HalfLength < to && limit + CartRules.HalfLength > from) { limit = from - CartRules.HalfLength; moved = true; }
            }
            if (!moved) break;
        }
        return limit;
    }

    /// <summary>The chaser's limit with a live ore heap's face (<paramref name="faceS"/>, +∞ for none) ahead of its front: it halts one half length short of the face, as the party cart does, and never drives through.</summary>
    public static float HeapLimit(float limit, float frontS, float faceS)
        => faceS >= frontS - RollAgain ? Math.Min(limit, faceS - CartRules.HalfLength) : limit;

    /// <summary>
    /// The chaser's prompt: "Get out" (the board prompt's word) for a hero seated in it, up, while it stands and its countdown is not in
    /// its first <see cref="Boarding.MinWait"/> (<see cref="GetOutOpen"/>), else nothing (nobody boards a chaser; a downed rider stays put).
    /// The server's check and a client's prompt both ask this.
    /// </summary>
    public static string ChaserPrompt(bool seatedHere, bool held, bool downed, float boardingLeft)
        => seatedHere && held && !downed && GetOutOpen(boardingLeft)
            ? RideInputRules.BoardPrompt(seatedHere: true, held: true, lastStop: false, heroSeatFree: false, inReach: true) : "";

    /// <summary>
    /// "Get out" is open: no countdown runs (<paramref name="boardingLeft"/> &lt; 0), or it has run <see cref="Boarding.MinWait"/>. The key
    /// that took the spare, pressed again a beat later, does not set the player straight back down.
    /// </summary>
    public static bool GetOutOpen(float boardingLeft) => boardingLeft < 0f || Boarding.Countdown - boardingLeft >= Boarding.MinWait - 1e-4f;

    /// <summary>
    /// A chase that ends before its chaser ever rolled (its riders got out, or nobody is left in it, during the countdown) gives the spare
    /// back: parked again and offered again. One that rolled keeps it spent.
    /// </summary>
    public static bool GivesSpareBack(bool departed) => !departed;
}
