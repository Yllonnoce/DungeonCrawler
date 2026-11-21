using System.Numerics;

namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// A 2-cell ramp on a rail lane: <paramref name="Index"/> is the lane index of its first cell in lane order, <paramref name="Low"/>
/// and <paramref name="High"/> its two cells (by <see cref="RampInfo.LowHeight"/>), <paramref name="Dir"/> the way it rises, and
/// <paramref name="Climb"/> holds when the lane runs along <paramref name="Dir"/> (the lane climbs it).
/// </summary>
public sealed record LaneRamp(int Index, GridPos Low, GridPos High, GridPos Dir, bool Climb);

/// <summary>
/// The bed a rail lane lies on: the drawn floor under it, and the ramps along it. Engine-free: no Godot types. Heights are
/// metres above the floor grid (add <c>DungeonBuilder.FloorTop</c> for the walk surface in world Y).
/// </summary>
public static class RailBed
{
    /// <summary>
    /// How far a ramp slab's top stands over its centre plane's line, past a flat slab's: 0.1/cos(atan 0.5) − 0.1. The ramp item
    /// is the 0.2 m slab tilted, so its top is LowHeight + 0.2118 + 0.5·u against a flat cell's level·4 + 0.2.
    /// </summary>
    public const float RampSlabRise = 0.0118f;
    /// <summary>The length (along the ramp) over which the bed turns from a flat onto a ramp and off it again.</summary>
    public const float EaseLength = 1f;
    /// <summary>A ramp cell's rise over run: 2 m over a 4 m cell.</summary>
    public const float RampSlope = 0.5f;

    /// <summary>
    /// The height at signed distance <paramref name="w"/> from where two lines meet (<paramref name="y0"/> at w = 0, slope
    /// <paramref name="s1"/> before and <paramref name="s2"/> after): the lines themselves beyond <paramref name="length"/>/2 either
    /// side, a parabola tangent to both between. A sag rides over its corner and a crest dips under it, by |s2 − s1|·length/8.
    /// </summary>
    public static float Ease(float w, float y0, float s1, float s2, float length = EaseLength)
    {
        var h = length / 2f;
        if (w <= -h) return y0 + s1 * w;
        if (w >= h) return y0 + s2 * w;
        return y0 + s1 * w + (s2 - s1) * (w + h) * (w + h) / (2f * length);
    }

    /// <summary>
    /// The drawn top above the floor grid at <paramref name="xz"/> (world metres), <c>FloorTop</c> excluded: a ramp cell's tilted
    /// slab, LowHeight + <see cref="RampSlabRise"/> + <see cref="RampSlope"/>·u with u the distance along its Dir from the cell's
    /// low edge; any other cell's level·4.
    /// </summary>
    public static float DrawnAt(FloorLayout layout, Vector2 xz, float cell = 4f)
    {
        var g = CellAt(xz, cell);
        return layout.Ramps.TryGetValue(g, out var r) ? RampLine(r, g, xz, cell) : layout.LevelOf(g) * 4f;
    }

    /// <summary>
    /// The bed the track follows at <paramref name="xz"/>: <see cref="DrawnAt"/>, eased with <see cref="Ease"/> within
    /// <see cref="EaseLength"/>/2 (along Dir) of where the lines meet at every ramp end. A ramp end is the edge of a ramp cell whose
    /// neighbour along its Dir (the high end) or against it (the low end) is not a ramp cell; the point may lie in the ramp cell or
    /// in that neighbour. The lines are the neighbour's flat level·4 and the ramp's line, and they meet 0.0236 m outside a low
    /// edge and 0.0236 m inside a high edge.
    /// </summary>
    public static float SurfaceAt(FloorLayout layout, Vector2 xz, float cell = 4f)
    {
        var g = CellAt(xz, cell);
        if (EasedAt(layout, g, g, xz, cell) is { } here) return here;
        foreach (var n in g.Neighbours4())
            if (EasedAt(layout, n, g, xz, cell) is { } near) return near;
        return DrawnAt(layout, xz, cell);
    }

    /// <summary>The eased bed at <paramref name="xz"/> (in cell <paramref name="at"/>) by an end of the ramp cell <paramref name="g"/>, or null when no end of it is that near.</summary>
    private static float? EasedAt(FloorLayout layout, GridPos g, GridPos at, Vector2 xz, float cell)
    {
        if (!layout.Ramps.TryGetValue(g, out var r)) return null;
        var u = Along(r, g, xz, cell);
        var low = g - r.Dir;
        if ((at == g || at == low) && !layout.Ramps.ContainsKey(low))
        {
            var flat = layout.LevelOf(low) * 4f;
            var w = u - MeetAt(r, flat);
            if (MathF.Abs(w) < EaseLength / 2f) return Ease(w, flat, 0f, RampSlope);
        }
        var high = g + r.Dir;
        if ((at == g || at == high) && !layout.Ramps.ContainsKey(high))
        {
            var flat = layout.LevelOf(high) * 4f;
            var w = u - MeetAt(r, flat);
            if (MathF.Abs(w) < EaseLength / 2f) return Ease(w, flat, RampSlope, 0f);
        }
        return null;
    }

    /// <summary>The u (along Dir, from the ramp cell's low edge) where its line crosses the flat at <paramref name="flat"/>.</summary>
    private static float MeetAt(RampInfo r, float flat) => (flat - r.LowHeight - RampSlabRise) / RampSlope;

    /// <summary>The ramp cell's line at <paramref name="xz"/> (any distance along it, past its edges too).</summary>
    private static float RampLine(RampInfo r, GridPos g, Vector2 xz, float cell) => r.LowHeight + RampSlabRise + RampSlope * Along(r, g, xz, cell);

    /// <summary>The distance along the ramp's Dir from <paramref name="g"/>'s low edge to <paramref name="xz"/>.</summary>
    private static float Along(RampInfo r, GridPos g, Vector2 xz, float cell) => Vector2.Dot(xz - Centre(g, cell), new Vector2(r.Dir.X, r.Dir.Y)) + cell / 2f;

    private static GridPos CellAt(Vector2 xz, float cell) => new((int)MathF.Floor(xz.X / cell), (int)MathF.Floor(xz.Y / cell));

    /// <summary>Every ramp on <paramref name="lane"/>: each pair of consecutive lane cells that are both ramp cells with the same Dir.</summary>
    public static IReadOnlyList<LaneRamp> RampsOn(FloorLayout layout, IReadOnlyList<GridPos> lane)
    {
        var ramps = new List<LaneRamp>();
        for (var i = 0; i + 1 < lane.Count; i++)
        {
            if (!layout.Ramps.TryGetValue(lane[i], out var a) || !layout.Ramps.TryGetValue(lane[i + 1], out var b) || a.Dir != b.Dir) continue;
            var (low, high) = a.LowHeight <= b.LowHeight ? (lane[i], lane[i + 1]) : (lane[i + 1], lane[i]);
            ramps.Add(new LaneRamp(i, low, high, a.Dir, lane[i + 1] - lane[i] == a.Dir));
            i++; // the pair is one ramp: its second cell starts none
        }
        return ramps;
    }

    /// <summary>The ramp's low end in world XZ: the edge of <see cref="LaneRamp.Low"/> away from the slope (the sag).</summary>
    public static Vector2 SagEdge(LaneRamp r, float cell = 4f) => Centre(r.Low, cell) - Dir(r) * (cell / 2f);

    /// <summary>The ramp's high end in world XZ: the edge of <see cref="LaneRamp.High"/> onto the upper floor (the crest).</summary>
    public static Vector2 CrestEdge(LaneRamp r, float cell = 4f) => Centre(r.High, cell) + Dir(r) * (cell / 2f);

    private static Vector2 Centre(GridPos g, float cell) => new(g.X * cell + cell / 2f, g.Y * cell + cell / 2f);
    private static Vector2 Dir(LaneRamp r) => new(r.Dir.X, r.Dir.Y);
}
