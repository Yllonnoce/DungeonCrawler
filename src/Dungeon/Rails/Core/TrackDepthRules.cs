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
    /// The probe's last line: the totals over every track, the worst depth in whole centimetres. <paramref name="noFloorOff"/>
    /// counts the probes with no drawn floor in the ray's window anywhere but over a drop (a chasm or a pit): a track too deep
    /// or too high for the ray. The pass is buried=0 inside=0 nofloorOff=0.
    /// </summary>
    public static string Verdict(int buried, int inside, int noFloorOff, int runs, float worstM, int buckledLaneCells, int ramps)
        => string.Create(CultureInfo.InvariantCulture,
            $"total buried={buried} inside={inside} nofloorOff={noFloorOff} runs={runs} worst={(int)MathF.Round(worstM * 100f)}cm buckledLaneCells={buckledLaneCells} rampsOnLine={ramps}");

    /// <summary>A probe over a drop (a chasm, crevasse or cavern cell, or a pit) legitimately finds no drawn floor under it.</summary>
    public static bool OverDrop(Cell cell) => cell is Cell.Chasm or Cell.Pit;
}
