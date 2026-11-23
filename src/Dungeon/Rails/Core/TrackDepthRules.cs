using System.Globalization;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>Why a stretch of track lies under the drawn floor or inside it (the --track-depth probe's cause).</summary>
public enum DepthCause { Buckled, RampCrest, RampSag, Wall, Plinth, Other }

/// <summary>
/// One probe's finding at S on a track: <paramref name="Under"/> metres of drawn floor over the probe (a float's height over
/// the floor for <see cref="DepthCause.RampSag"/>; 0 for an intersection), on <paramref name="Cell"/>.
/// </summary>
public readonly record struct DepthHit(string Track, float S, GridPos Cell, string Probe, float Under, DepthCause Cause);

/// <summary>
/// Consecutive findings on one track merged: S from and to, the distinct cells in S order, the distinct S values
/// (<paramref name="Samples"/>), and the worst finding's depth, probe and cause.
/// </summary>
public sealed record DepthRun(string Track, float FromS, float ToS, IReadOnlyList<GridPos> Cells, int Samples, float Worst, string Probe, DepthCause Cause);

/// <summary>
/// The numbers of the --track-depth probe (DebugTools.TrackDepth): where it samples a track and its probes, when a probe
/// counts as buried, which cause a finding gets, and how findings merge into runs. Engine-free.
/// </summary>
public static class TrackDepthRules
{
    /// <summary>
    /// Floor 12's pinned <c>nofloorDrop</c> counts (plan 13's probe gate, re-measured on the island floor by plan 15 Task 3; was PartyTrack 0 /
    /// EnemyTrack 40). Each 4 m cell of drop under a track reads 40 (8 samples by 5 probes). PartyTrack 201: the pit (61,56) 40, the exit
    /// bridge (51..54,60) 160, and 1 more from the S=1184.0 sample on the (55,60)|(54,60) join, whose left sleeper end lies over (54,60).
    /// EnemyTrack 161: ride 3's broken-bridge gap (40,12) 40, the enemy bridge (51..53,61) 120, and 1 from the S=1220.0 sample on the
    /// (54,61)|(53,61) join. The verdict does not judge nofloorDrop (a probe over a drop is legitimate), so these expected values are what
    /// catches a cavern deck built without its footing (a deck that gives way at runtime must still stand at the build).
    /// <c>--test-track-view</c> holds the probe to them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Floor12NoFloorDrop = new Dictionary<string, int> { ["PartyTrack"] = 201, ["EnemyTrack"] = 161 };

    /// <summary>Null when every pinned track's measured nofloorDrop is its pin; else why not (a missing deck footing at the build, or the track not probed).</summary>
    public static string? NoFloorDropPin(IReadOnlyDictionary<string, int> pins, IReadOnlyDictionary<string, int> measured)
    {
        var bad = new List<string>();
        foreach (var (track, want) in pins)
        {
            if (!measured.TryGetValue(track, out var got)) bad.Add($"{track} not probed");
            else if (got != want) bad.Add($"{track} nofloorDrop={got} (pinned {want})");
        }
        return bad.Count == 0 ? null : string.Join("; ", bad) + ": a deck footing missing at the build?";
    }

    /// <summary>The probe samples every track every this many metres of S.</summary>
    public const float Step = 0.5f;
    /// <summary>A rail's centre lies this far either side of the centreline; its top this far above the curve.</summary>
    public const float RailHalf = 0.45f, RailTop = 0.24f;
    /// <summary>A sleeper's end lies this far either side of the centreline; its top this far above the curve.</summary>
    public const float SleeperHalf = 0.7f, SleeperTop = 0.12f;
    /// <summary>The intersection points sit this far under a rail top: the rail's middle.</summary>
    public const float MidRail = 0.06f;
    /// <summary>The floor ray starts this far above the curve and ends this far under it: wide enough for a track 2.5 m inside an upper level or 2 m over a lower one (the pre-fix ramps).</summary>
    public const float RayUp = 3f, RayDown = 3f;
    /// <summary>The floor ray is cast at most this many times, each cast excluding the colliders that are not the drawn floor.</summary>
    public const int MaxCasts = 5;
    /// <summary>The default threshold, in centimetres.</summary>
    public const int DefaultCm = 3;
    /// <summary>
    /// A sample whose curve stands more than this over the highest floor under its probes is floating, a failure: the eased bed's
    /// worst float is about 8.5 cm (a 1 m ease's 6.25 cm and the slab rise, laid on straight pieces).
    /// </summary>
    public const float FloatBound = 0.09f;

    /// <summary>A probe with <paramref name="under"/> metres of floor over it is buried past a threshold of <paramref name="thresholdCm"/> cm.</summary>
    public static bool Buried(float under, int thresholdCm) => under > thresholdCm / 100f;

    /// <summary>
    /// A finding's cause; the first rule that applies wins: an intersection with a ramp item is a ramp crest, with any other
    /// item a plinth (the item lies below the lane's level) or a wall; then a buckled tile; then a ramp next to it (a sag for
    /// a float, else a crest); else other.
    /// </summary>
    public static DepthCause Cause(bool buckled, bool rampNear, bool floats, string? insideItem, bool insideBelowLane)
    {
        if (insideItem != null && insideItem.StartsWith("Ramp", StringComparison.Ordinal)) return DepthCause.RampCrest;
        if (insideItem != null) return insideBelowLane ? DepthCause.Plinth : DepthCause.Wall;
        if (buckled) return DepthCause.Buckled;
        if (rampNear) return floats ? DepthCause.RampSag : DepthCause.RampCrest;
        return DepthCause.Other;
    }

    /// <summary>
    /// Merge <paramref name="hits"/> into runs: on one track, distinct S values that follow each other within
    /// <paramref name="step"/> + 0.01 are one run. Runs are ordered by track (ordinal), then by S.
    /// </summary>
    public static IReadOnlyList<DepthRun> Runs(IReadOnlyList<DepthHit> hits, float step = Step)
    {
        var runs = new List<DepthRun>();
        foreach (var track in hits.Select(h => h.Track).Distinct().OrderBy(t => t, StringComparer.Ordinal))
        {
            var mine = hits.Where(h => h.Track == track).OrderBy(h => h.S).ToList();
            var start = 0;
            for (var i = 1; i <= mine.Count; i++)
            {
                if (i < mine.Count && mine[i].S - mine[i - 1].S <= step + 0.01f) continue;
                runs.Add(RunOf(track, mine.GetRange(start, i - start)));
                start = i;
            }
        }
        return runs;
    }

    private static DepthRun RunOf(string track, List<DepthHit> hits)
    {
        var worst = hits[0];
        foreach (var h in hits) if (h.Under > worst.Under) worst = h;
        var cells = new List<GridPos>();
        foreach (var h in hits) if (!cells.Contains(h.Cell)) cells.Add(h.Cell);
        return new DepthRun(track, hits[0].S, hits[^1].S, cells, hits.Select(h => h.S).Distinct().Count(), worst.Under, worst.Probe, worst.Cause);
    }

    /// <summary>
    /// The probe's last line: the totals over every track, the worst depth and the worst float in whole centimetres.
    /// <paramref name="noFloorOff"/> counts the probes with no drawn floor in the ray's window anywhere but over a drop (a chasm or
    /// a pit): a track too deep or too high for the ray. <paramref name="budget"/> counts the probes whose ray spent every cast on
    /// other colliders (no answer at all). <paramref name="floating"/> counts the samples standing more than <see cref="FloatBound"/>
    /// over the floor. <paramref name="buckledLaneCells"/> counts the lane cells the GridMap draws with a heaved slab. The pass is
    /// buried=0 inside=0 nofloorOff=0 budget=0 floating=0.
    /// </summary>
    public static string Verdict(int buried, int inside, int noFloorOff, int budget, int floating, int runs, float worstM, float worstFloatM, int buckledLaneCells, int ramps)
        => string.Create(CultureInfo.InvariantCulture,
            $"total buried={buried} inside={inside} nofloorOff={noFloorOff} budget={budget} floating={floating} runs={runs} worst={(int)MathF.Round(worstM * 100f)}cm float={(int)MathF.Round(worstFloatM * 100f)}cm buckledLaneCells={buckledLaneCells} rampsOnLine={ramps}");

    /// <summary>A probe over a drop (a chasm, crevasse or cavern cell, or a pit) legitimately finds no drawn floor under it.</summary>
    public static bool OverDrop(Cell cell) => cell is Cell.Chasm or Cell.Pit;
}
