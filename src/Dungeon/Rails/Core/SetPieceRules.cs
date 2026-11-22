using System.Numerics;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>How a slag cart paces itself against the party cart. Engine-free.</summary>
public static class PaceRules
{
    public const float AlongsideRadius = 2.5f;

    /// <summary>The S a slag cart aims for; a positive offset is ahead of the party cart.</summary>
    public static float TargetS(float partyEnemyS, float offset) => partyEnemyS + offset;

    /// <summary>A pacing cart's correction toward its pace point is capped at this either way (m/s over the party's speed).</summary>
    public const float CatchUp = 3f;
    /// <summary>
    /// A side-by-side cart (Offset 0: ride 4's leap cart) closes on its pace point from behind at up to this (m/s over the party's
    /// speed): it starts ahead of it out of the seats' sight and a jog's pace-point jump must not leave it lagging into the leap's
    /// stretch (plan 13, the legibility fix round). Its catch-down stays <see cref="CatchUp"/>.
    /// </summary>
    public const float AlongsideCatchUp = 6f;

    /// <summary>
    /// The commanded speed: the party's speed plus a correction toward the target, capped at <see cref="CatchUp"/> back and
    /// <paramref name="catchUp"/> forward, within [0, MaxSpeed].
    /// </summary>
    public static float SpeedFor(float s, float v, float targetS, float partyV, float catchUp = CatchUp)
        => Math.Clamp(partyV + Math.Clamp((targetS - s) * 0.8f, -CatchUp, catchUp), 0f, CartRules.MaxSpeed);

    public static bool Alongside(float s, float targetS) => Math.Abs(s - targetS) < AlongsideRadius;
}

public enum HammerPhase { Up, Glow, Fall, Down, Rise }

public enum PourPhase { Dry, Warn, Pouring }

/// <summary>Where one ore bucket stands in its ride's round (<see cref="SetPieceRules.BucketStateAt"/>).</summary>
public enum BucketPhase { Hanging, Creaking, Poured }

/// <summary>Timing and numbers for floor 12's set pieces. Engine-free; ticks are 60 Hz server physics ticks.</summary>
public static class SetPieceRules
{
    public const int TicksPerSecond = 60;

    /// <summary>A hammer's or the pour's cues carry at most this far (a walker near it hears its warning).</summary>
    public const float MaxCueRange = 30f;

    /// <summary>The kinds that stand on the line from the ride's departure rather than starting as the cart reaches them (a golem arms at its At).</summary>
    public static bool StandsFromDeparture(string kind) => kind is "lone" or "thrower";
    /// <summary>The cue range ends at least this far short of every stop (a seated head sits up to 0.7 m along the cart from its middle).</summary>
    public const float StopMargin = 2f;

    /// <summary>A hazard's cue range when it stands <paramref name="metresToStop"/> from its nearest stop: 30 m, or less so a cart held there never hears it (never below 0).</summary>
    public static float CueRange(float metresToStop) => Math.Clamp(metresToStop - StopMargin, 0f, MaxCueRange);

    /// <summary>
    /// A hazard's cue range against every stop on the line: <see cref="CueRange"/> of the nearest, so a cart held at any stop never
    /// hears it (<see cref="MaxCueRange"/> on a line with no stops).
    /// </summary>
    public static float CueRangeNearest(IEnumerable<float> metresToEachStop)
    {
        var nearest = float.PositiveInfinity;
        foreach (var metres in metresToEachStop) nearest = Math.Min(nearest, metres);
        return float.IsPositiveInfinity(nearest) ? MaxCueRange : CueRange(nearest);
    }

    /// <summary>
    /// A piece with this cue range makes any sound at all: a range of 0 (a piece within the margin of a stop) plays nothing, its one-shots
    /// skipped and its loop kept stopped, since a 3D player reads a MaxDistance of 0 as unlimited.
    /// </summary>
    public static bool CueAudible(float cueRange) => cueRange > 0f;

    const float UpS = 1.6f, GlowS = 1.5f, FallS = 0.15f, DownS = 0.5f, RiseS = 0.75f;
    const int UpT = (int)(UpS * TicksPerSecond), GlowT = (int)(GlowS * TicksPerSecond), FallT = (int)(FallS * TicksPerSecond),
        DownT = (int)(DownS * TicksPerSecond), RiseT = (int)(RiseS * TicksPerSecond);
    const int HammerCycleT = UpT + GlowT + FallT + DownT + RiseT;
    /// <summary>Hammer i runs i·this many ticks ahead of hammer 0: 0.75 s, two beats of the pour track at 160 BPM, so every hammer falls on a beat.</summary>
    public const int HammerIndexOffsetTicks = 45;

    /// <summary>One hammer's whole cycle in seconds (4.5: twelve beats at 160 BPM).</summary>
    public static double HammerCycleSeconds => HammerCycleT / (double)TicksPerSecond;

    /// <summary>The floor second, within a cycle, at which hammer <paramref name="index"/> begins to fall (hammer 0: 3.1).</summary>
    public static double HammerFallSeconds(int index) => Mod(UpT + GlowT - (long)index * HammerIndexOffsetTicks, HammerCycleT) / (double)TicksPerSecond;

    const int DryT = 6 * TicksPerSecond, WarnT = (int)(1.5f * TicksPerSecond), PouringT = 3 * TicksPerSecond;
    const int PourCycleT = DryT + WarnT + PouringT;

    /// <summary>
    /// The lip speed that makes the jump: one pumper with every wagon coupled clears it (6 × 1.2 / 1.15 = 6.26 m/s), an unpumped
    /// cart never does (6.0 m/s empty, less with wagons), so a lone player can always make it by pumping.
    /// </summary>
    public const float JumpSpeed = 6.1f;
    public const float TrestleGrace = 4f;
    public const float RunawayEvery = 40f;
    public const float SlagTrafficEvery = 25f;
    public const float LeapWindup = 0.8f;
    public const float LeapRange = 6f;
    /// <summary>The party cart's rim has this many boarder spots (front-left, back-right).</summary>
    public const int RimSpots = 2;

    /// <summary>
    /// The party cart's rim spots open to a leaping hound: one per awake hero seated in it, at most <see cref="RimSpots"/>, so a
    /// lone rider faces one hound at a time (the next tries a wagon, or leaps and misses beside the lane). A party of two or more is unchanged.
    /// </summary>
    public static int RimSpotsOpen(int awakeHeroesSeated) => Math.Clamp(awakeHeroesSeated, 0, RimSpots);

    static long Mod(long a, int m) { var r = a % m; return r < 0 ? r + m : r; }

    /// <summary>The hammer's phase at <paramref name="tick"/>; <paramref name="secondsInto"/> counts from that phase's start.</summary>
    public static HammerPhase Phase(long tick, int index, out float secondsInto)
    {
        var t = (int)Mod(tick + (long)index * HammerIndexOffsetTicks, HammerCycleT);
        HammerPhase p;
        int start;
        if (t < UpT) { p = HammerPhase.Up; start = 0; }
        else if (t < UpT + GlowT) { p = HammerPhase.Glow; start = UpT; }
        else if (t < UpT + GlowT + FallT) { p = HammerPhase.Fall; start = UpT + GlowT; }
        else if (t < UpT + GlowT + FallT + DownT) { p = HammerPhase.Down; start = UpT + GlowT + FallT; }
        else { p = HammerPhase.Rise; start = UpT + GlowT + FallT + DownT; }
        secondsInto = (t - start) / (float)TicksPerSecond;
        return p;
    }

    /// <summary>The slag pour's phase: dry, a warning, then pouring.</summary>
    public static PourPhase Pour(long tick, out float secondsInto)
    {
        var t = (int)Mod(tick, PourCycleT);
        if (t < DryT) { secondsInto = t / (float)TicksPerSecond; return PourPhase.Dry; }
        if (t < DryT + WarnT) { secondsInto = (t - DryT) / (float)TicksPerSecond; return PourPhase.Warn; }
        secondsInto = (t - DryT - WarnT) / (float)TicksPerSecond;
        return PourPhase.Pouring;
    }

    // ---- the ore buckets: the server's schedule, which a client runs from the ride's arm time too --------------

    /// <summary>One bucket tips every this many seconds of an armed ride, in turn.</summary>
    public const float BucketEvery = 3f;
    /// <summary>A bucket creaks and glows this long before it pours.</summary>
    public const float BucketCreak = 1.5f;
    /// <summary>A poured cinder patch burns this long.</summary>
    public const float BucketPatchSeconds = 4f;

    /// <summary>
    /// Bucket <paramref name="n"/> of a ride (0 the first to tip) <paramref name="secondsSinceArmed"/> after the ride armed: it hangs until
    /// n·<see cref="BucketEvery"/>, creaks for <see cref="BucketCreak"/>, then has poured. <c>SecondsIn</c> counts from the phase's start
    /// (hanging: from the arming); before the arming every bucket hangs, 0 s in. The server's buckets and a client's copies both ask this.
    /// </summary>
    public static (BucketPhase Phase, double SecondsIn) BucketStateAt(double secondsSinceArmed, int n)
    {
        if (secondsSinceArmed < 0) return (BucketPhase.Hanging, 0.0);
        var creakAt = n * (double)BucketEvery;
        if (secondsSinceArmed < creakAt) return (BucketPhase.Hanging, secondsSinceArmed);
        var pourAt = creakAt + BucketCreak;
        if (secondsSinceArmed < pourAt) return (BucketPhase.Creaking, secondsSinceArmed - creakAt);
        return (BucketPhase.Poured, secondsSinceArmed - pourAt);
    }

    // ---- the heap's lantern and the beam: one rule on the server and on a client's copy ---------------------

    /// <summary>The heap's lantern blinks while a cart bears down within this many seconds of its speed.</summary>
    public const float HeapWarnSeconds = 3f;

    /// <summary>How far ahead the heap is called: <see cref="HeapWarnSeconds"/> at the cart's speed, never less than at the warn speed.</summary>
    public static float HeapLook(float v) => Math.Max(v, RunOverRules.WarnMinSpeed) * HeapWarnSeconds;

    /// <summary>A rolling cart heads for the heap: its face is not behind the front, on the party lane, and no thrown switch before it sends the cart round.</summary>
    public static bool HeapHeaded(bool rolling, float faceS, float front, bool faceOnEnemyLane, bool switchSetBefore)
        => rolling && faceS >= front - 0.5f && !faceOnEnemyLane && !switchSetBefore;

    /// <summary>The lantern blinks: headed for it, and the face within <see cref="HeapLook"/> of the front.</summary>
    public static bool HeapAlarm(bool headed, float faceS, float front, float v) => headed && faceS - front <= HeapLook(v);

    /// <summary>The beam is passed (the seated desktop cameras duck once): the rolling cart's front reached it, not yet this arming.</summary>
    public static bool BeamPasses(bool rolling, bool fired, float front, float beamS) => rolling && !fired && front >= beamS;

    public static bool JumpMade(float v) => v >= JumpSpeed;

    /// <summary>A parabola over u in [0, gapLength], zero at both ends, apex 0.12 × gapLength × v / JumpSpeed.</summary>
    public static float ArcHeight(float u, float gapLength, float v)
    {
        if (gapLength <= 0f) return 0f;
        var x = Math.Clamp(u / gapLength, 0f, 1f);
        return 4f * x * (1f - x) * 0.12f * gapLength * v / JumpSpeed;
    }

    /// <summary>The kiln spur's end stands out at most this far from the enemy lane's centre, away from the party's lane.</summary>
    public const float SpurBendMax = 1.8f;
    /// <summary>Half the width of the spur's stop beam (1.6 m across the spur's end).</summary>
    public const float SpurStopHalf = 0.8f;
    /// <summary>The stop beam's end stays this far short of the rock beyond the spur.</summary>
    public const float SpurWallMargin = 0.1f;

    /// <summary>
    /// How far the spur's end bends out, given <paramref name="roomBeyond"/>, the open distance outward from the enemy lane's centre:
    /// <see cref="SpurBendMax"/>, or less so its stop beam ends <see cref="SpurWallMargin"/> short of the rock (1.1 m against a lane
    /// cell's wall face, 2 m out).
    /// </summary>
    public static float SpurBend(float roomBeyond) => MathF.Min(SpurBendMax, roomBeyond - SpurStopHalf - SpurWallMargin);

    /// <summary>The kiln spur's bend is tried from <see cref="SpurBendMax"/> down in steps of this.</summary>
    public const float SpurBendStep = 0.05f;
    /// <summary>The stop beam's inner end is cut back to clear the main line's sleeper ends (and so its rails) by this.</summary>
    public const float SpurMainClear = 0.05f;

    /// <summary>
    /// The kiln spur's bend: the largest from <see cref="SpurBendMax"/> down to <see cref="SpurBend"/>(<paramref name="roomAgainstRock"/>)
    /// (1.1 m: the lane cell's wall face, half a cell out), in steps of <see cref="SpurBendStep"/>, whose layout (1) keeps its whole
    /// footprint (<see cref="SpurFootprint"/>) <see cref="SpurWallMargin"/> clear of rock, (2) has left the main line at its end
    /// (<see cref="SpurSleeperGap"/> at least 0), and (3) keeps a stop beam that, cut back clear of the main line
    /// (<see cref="SpurStopOf"/>), still spans both of its own rails. <paramref name="layAt"/> lays the spur's path (X, Z) for a bend;
    /// <paramref name="main"/> is the main line's path beside it. No bend fits: <c>NoFit</c> (an error the builder logs), and the spur takes
    /// the least bend with its full beam.
    /// </summary>
    public static SpurBendPick PickSpurBend(Func<float, IReadOnlyList<Vector2>> layAt, IReadOnlyList<Vector2> main,
        Func<Vector2, bool> rockAt, float roomAgainstRock, SpurStopShape shape)
    {
        var least = SpurBend(roomAgainstRock);
        for (var i = 0; SpurBendMax - i * SpurBendStep >= least - 1e-4f; i++)
        {
            var bend = MathF.Max(least, SpurBendMax - i * SpurBendStep);
            var path = layAt(bend);
            var stop = SpurStopOf(path, main, shape);
            var gap = SpurSleeperGap(path, main, shape);
            if (stop.Inner >= TrackDepthRules.RailHalf && gap >= 0f && SpurFootprint(path, stop, shape).All(p => ClearOfRock(p, rockAt)))
                return new SpurBendPick(bend, i > 0, false, stop, gap);
        }
        var fallback = layAt(least);
        return new SpurBendPick(least, true, true, SpurStopOf(fallback, main, shape) with { Inner = SpurStopHalf }, SpurSleeperGap(fallback, main, shape));
    }

    /// <summary>
    /// The spur's stop beam on its <paramref name="path"/>: centred <see cref="SpurStopShape.Back"/> short of the end, square to it, its outer
    /// end <see cref="SpurStopHalf"/> out, its inner end cut back (never past <see cref="SpurStopHalf"/>) so its inner corners stand
    /// <see cref="TrackDepthRules.SleeperHalf"/> + <see cref="SpurMainClear"/> from the <paramref name="main"/> line, measured across it.
    /// </summary>
    public static SpurStop SpurStopOf(IReadOnlyList<Vector2> path, IReadOnlyList<Vector2> main, SpurStopShape shape)
    {
        var (centre, along) = AlongPath(path, MathF.Max(0f, PathLength(path) - shape.Back));
        var side = SideOf(main, path[^1]);
        var (d, o) = AcrossMain(main, centre, side);
        var across = Across(along);
        if (Vector2.Dot(across, o) < 0f) across = -across;
        var reach = Vector2.Dot(across, o);
        var need = TrackDepthRules.SleeperHalf + SpurMainClear + shape.Depth / 2f * MathF.Abs(Vector2.Dot(along, o));
        if (reach <= 1e-3f) return new SpurStop(centre, across, along, SpurStopHalf, SpurStopHalf);
        var inner = MathF.Min(SpurStopHalf, (d - need) / reach);
        // the cut is taken square to the main line at the beam's middle; where the main line curves, cut on by what its corners still lack
        for (var pass = 0; pass < 4; pass++)
        {
            var nearest = float.PositiveInfinity;
            foreach (var z in new[] { -shape.Depth / 2f, shape.Depth / 2f })
                nearest = MathF.Min(nearest, AcrossMain(main, centre - across * inner + along * z, side).D);
            var lack = TrackDepthRules.SleeperHalf + SpurMainClear - nearest;
            if (lack <= 1e-4f) break;
            inner -= lack / reach;
        }
        return new SpurStop(centre, across, along, MathF.Min(SpurStopHalf, inner), SpurStopHalf);
    }

    /// <summary>
    /// How far the spur's inner sleeper ends stand clear of the <paramref name="main"/> line's at its stop beam's station and at its last
    /// point (the lesser, across the main line; below 0 they overlap). Only its end must have left the main line: nearer the points the
    /// two share sleepers, as at any turnout (an 8 m spur is only 0.56 of its bend out 2 m short of its end).
    /// </summary>
    public static float SpurSleeperGap(IReadOnlyList<Vector2> path, IReadOnlyList<Vector2> main, SpurStopShape shape)
    {
        var side = SideOf(main, path[^1]);
        float Gap(Vector2 at, Vector2 along)
        {
            var across = Across(along);
            return MathF.Min(AcrossMain(main, at - across * TrackDepthRules.SleeperHalf, side).D,
                AcrossMain(main, at + across * TrackDepthRules.SleeperHalf, side).D) - TrackDepthRules.SleeperHalf;
        }
        var (stopAt, stopAlong) = AlongPath(path, MathF.Max(0f, PathLength(path) - shape.Back));
        return MathF.Min(Gap(stopAt, stopAlong), Gap(path[^1], path[^1] - path[^2]));
    }

    /// <summary>
    /// The spur's footprint in the plane (X, Z) of its polyline <paramref name="path"/>: both sleeper ends
    /// (<see cref="TrackDepthRules.SleeperHalf"/> either side) at every point of it, the four corners of its <paramref name="stop"/> beam
    /// (<see cref="SpurStopShape.Depth"/> deep), and the feet of the stop's two braces (<see cref="SpurStopShape.BraceReach"/> on from the
    /// beam, under its rails).
    /// </summary>
    public static IEnumerable<Vector2> SpurFootprint(IReadOnlyList<Vector2> path, SpurStop stop, SpurStopShape shape)
    {
        if (path.Count < 2) yield break;
        for (var i = 0; i < path.Count; i++)
        {
            var across = Across(path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]);
            yield return path[i] - across * TrackDepthRules.SleeperHalf;
            yield return path[i] + across * TrackDepthRules.SleeperHalf;
        }
        foreach (var a in new[] { -stop.Inner, stop.Outer })
            foreach (var z in new[] { -shape.Depth / 2f, shape.Depth / 2f })
                yield return stop.Centre + stop.Across * a + stop.Along * z;
        foreach (var a in new[] { -TrackDepthRules.RailHalf, TrackDepthRules.RailHalf })
            yield return stop.Centre + stop.Across * a + stop.Along * shape.BraceReach;
    }

    /// <summary>The point and every point <see cref="SpurWallMargin"/> around it (eight ways) lie out of rock.</summary>
    public static bool ClearOfRock(Vector2 p, Func<Vector2, bool> rockAt)
    {
        if (rockAt(p)) return false;
        for (var k = 0; k < 8; k++)
            if (rockAt(p + new Vector2(MathF.Cos(k * MathF.PI / 4f), MathF.Sin(k * MathF.PI / 4f)) * SpurWallMargin)) return false;
        return true;
    }

    /// <summary>
    /// <paramref name="p"/>'s distance across the <paramref name="main"/> polyline from its nearest point, positive on
    /// <paramref name="side"/> (<see cref="SideOf"/>), and the main line's level unit normal there, pointing to that side.
    /// </summary>
    static (float D, Vector2 Normal) AcrossMain(IReadOnlyList<Vector2> main, Vector2 p, float side)
    {
        var best = float.PositiveInfinity;
        var result = (D: 0f, Normal: Vector2.UnitY);
        for (var i = 1; i < main.Count; i++)
        {
            var seg = main[i] - main[i - 1];
            var len2 = seg.LengthSquared();
            if (len2 < 1e-12f) continue;
            var u = Math.Clamp(Vector2.Dot(p - main[i - 1], seg) / len2, 0f, 1f);
            var at = main[i - 1] + seg * u;
            var dist = Vector2.Distance(p, at);
            if (dist >= best) continue;
            best = dist;
            var n = Vector2.Normalize(new Vector2(-seg.Y, seg.X)) * side;
            result = (Vector2.Dot(p - at, n), n);
        }
        return result;
    }

    /// <summary>Which side of the <paramref name="main"/> polyline <paramref name="p"/> lies on, against its left normal: +1 or -1.</summary>
    static float SideOf(IReadOnlyList<Vector2> main, Vector2 p) => AcrossMain(main, p, 1f).D >= 0f ? 1f : -1f;

    static float PathLength(IReadOnlyList<Vector2> path)
    {
        var length = 0f;
        for (var i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
        return length;
    }

    /// <summary>The unit vector square to <paramref name="along"/> in the plane.</summary>
    static Vector2 Across(Vector2 along)
    {
        var n = new Vector2(-along.Y, along.X);
        return n.LengthSquared() > 1e-12f ? Vector2.Normalize(n) : Vector2.UnitX;
    }

    /// <summary>The point <paramref name="s"/> along the polyline and its unit heading there.</summary>
    static (Vector2 At, Vector2 Along) AlongPath(IReadOnlyList<Vector2> path, float s)
    {
        for (var i = 1; i < path.Count; i++)
        {
            var seg = path[i] - path[i - 1];
            var len = seg.Length();
            if (len < 1e-6f) continue;
            if (s <= len || i == path.Count - 1) return (path[i - 1] + seg * (MathF.Min(s, len) / len), seg / len);
            s -= len;
        }
        return (path[^1], Vector2.UnitY);
    }
}

/// <summary>The kiln spur's stop as the builder draws it: its beam's middle <see cref="Back"/> short of the spur's end and <see cref="Depth"/> deep
/// along it; its braces' feet <see cref="BraceReach"/> on from the beam's middle.</summary>
public readonly record struct SpurStopShape(float Back, float Depth, float BraceReach);

/// <summary>The kiln spur's stop beam in the plane (X, Z): its middle, its unit vectors across (away from the main line) and along the spur, and
/// how far it reaches across each way (<see cref="Inner"/> toward the main line, <see cref="Outer"/> away).</summary>
public readonly record struct SpurStop(Vector2 Centre, Vector2 Across, Vector2 Along, float Inner, float Outer);

/// <summary><see cref="SetPieceRules.PickSpurBend"/>'s answer: the bend, whether it is less than the full one, whether nothing fitted (an
/// error), the stop beam and the spur's last stretch's sleeper gap from the main line's.</summary>
public readonly record struct SpurBendPick(float Bend, bool BentIn, bool NoFit, SpurStop Stop, float SleeperGap);
