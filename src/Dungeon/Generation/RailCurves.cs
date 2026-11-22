using System.Numerics;

namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Turns a lane of grid cells into a smooth world-space centreline. Engine-free: no Godot types.</summary>
public static class RailCurves
{
    /// <summary>
    /// World-space points along the lane: straight between cell centres, each 90 degree turn replaced by a quarter circle
    /// (radius clamped so an arc uses at most half the run to a neighbouring corner, the full run to a lane end), heights
    /// interpolated linearly between cell-centre heights, resampled every <paramref name="step"/> metres of path length.
    /// The first point is the first cell's centre and the last point the last cell's centre. The heights are taken only at the
    /// path's vertices (the lane's ends and its arcs), so a rise along a straight is spread over the whole straight; the track
    /// is laid with <see cref="CentrelineOver"/>, which follows the bed point by point.
    /// </summary>
    public static IReadOnlyList<Vector3> Centreline(IReadOnlyList<GridPos> lane, Func<GridPos, float> heightOf,
        float cell = 4f, float cornerRadius = 3f, float step = 0.5f)
    {
        Check(lane, cell, cornerRadius, step);
        if (lane.Count == 0) return Array.Empty<Vector3>();
        if (lane.Count == 1) { var c = Centre(lane[0], cell); return new[] { new Vector3(c.X, heightOf(lane[0]), c.Y) }; }

        int n = lane.Count;
        float Height(float t)
        {
            int i = Math.Clamp((int)MathF.Floor(t), 0, n - 2);
            float f = Math.Clamp(t - i, 0f, 1f);
            return heightOf(lane[i]) + (heightOf(lane[i + 1]) - heightOf(lane[i])) * f;
        }
        return Resample(Path(lane, cell, cornerRadius).Select(q => new Vector3(q.P.X, Height(q.T), q.P.Y)).ToList(), step);
    }

    /// <summary>The longest straight piece <see cref="CentrelineOver"/> takes a height across: finer than the bed's 1 m eases.</summary>
    private const float BedPiece = 0.125f;

    /// <summary>A bed vertex is kept where its pitch turns by more than this (rise over run): the eases turn 0.0625 per 0.125 m piece.</summary>
    private const float BedTurn = 1e-3f;

    /// <summary>A resample point this near a kept bed vertex is left out: closer than the laying's 0.05 m merge, never a sliver chord.</summary>
    private const float KeptGap = BedPiece / 2f;

    /// <summary>
    /// The lane's centreline laid on a bed: the same 2D path as <see cref="Centreline"/> (straights between cell centres, a quarter
    /// circle at each turn), the straight pieces cut to at most <see cref="BedPiece"/>, each point's height
    /// <paramref name="surfaceAt"/> of its world XZ, then the same resample every <paramref name="step"/> metres of path length,
    /// except where the bed turns: every bed vertex whose pitch changes (inside a ramp's eases) is kept as it is, and the
    /// resample points between two kept vertices, or within <see cref="KeptGap"/> of one, are left out. So an ease is laid as its
    /// 0.125 m pieces, not two or three 0.5 m chords, and the seat's vertical speed turns in small steps; on flat ground and along a
    /// ramp's even slope nothing turns, and the line is the plain resample. A point function, so a ramp's rise stays on the ramp,
    /// and an arc onto one is exact.
    /// </summary>
    public static IReadOnlyList<Vector3> CentrelineOver(IReadOnlyList<GridPos> lane, Func<Vector2, float> surfaceAt,
        float cell = 4f, float cornerRadius = 3f, float step = 0.5f)
    {
        Check(lane, cell, cornerRadius, step);
        if (lane.Count == 0) return Array.Empty<Vector3>();
        Vector3 On(Vector2 p) => new(p.X, surfaceAt(p), p.Y);
        if (lane.Count == 1) return new[] { On(Centre(lane[0], cell)) };

        var path = Path(lane, cell, cornerRadius);
        var poly = new List<Vector3> { On(path[0].P) };
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1].P, b = path[i].P;
            if (Vector2.Distance(a, b) < 1e-6f) continue; // two arcs that meet: no piece between them, nor a turn read off one
            int pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / BedPiece));
            for (int k = 1; k <= pieces; k++) poly.Add(On(k == pieces ? b : Vector2.Lerp(a, b, k / (float)pieces)));
        }
        return Resample(poly, step, BedTurns(poly));
    }

    /// <summary>Which of the bed's vertices to keep: the inner ones where the pitch (rise over flat run) turns by more than <see cref="BedTurn"/>.</summary>
    private static bool[] BedTurns(List<Vector3> poly)
    {
        var keep = new bool[poly.Count];
        static float Pitch(Vector3 a, Vector3 b)
        {
            var run = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            return run < 1e-6f ? 0f : (b.Y - a.Y) / run;
        }
        for (int i = 1; i + 1 < poly.Count; i++)
            keep[i] = MathF.Abs(Pitch(poly[i], poly[i + 1]) - Pitch(poly[i - 1], poly[i])) > BedTurn;
        return keep;
    }

    private static void Check(IReadOnlyList<GridPos> lane, float cell, float cornerRadius, float step)
    {
        if (step <= 0f) throw new ArgumentOutOfRangeException(nameof(step), "step must be positive");
        if (cell <= 0f) throw new ArgumentOutOfRangeException(nameof(cell), "cell must be positive");
        if (cornerRadius < 0f) throw new ArgumentOutOfRangeException(nameof(cornerRadius), "cornerRadius must not be negative");
        for (int i = 1; i < lane.Count; i++)
            if (Math.Abs(lane[i].X - lane[i - 1].X) + Math.Abs(lane[i].Y - lane[i - 1].Y) != 1)
                throw new ArgumentException($"lane cells {lane[i - 1]} and {lane[i]} are not 4-adjacent", nameof(lane));
    }

    private static Vector2 Centre(GridPos g, float cell) => new(g.X * cell + cell / 2f, g.Y * cell + cell / 2f);

    /// <summary>
    /// The lane's 2D path (two cells or more): its first cell's centre, each corner's arc (radius clamped so an arc uses at most
    /// half the run to a neighbouring corner, the full run to a lane end) and its last cell's centre, with each point's
    /// fractional lane index T.
    /// </summary>
    private static List<(Vector2 P, float T)> Path(IReadOnlyList<GridPos> lane, float cell, float cornerRadius)
    {
        int n = lane.Count;
        var dir = new Vector2[n - 1];
        for (int i = 0; i < n - 1; i++)
            dir[i] = Vector2.Normalize(Centre(lane[i + 1], cell) - Centre(lane[i], cell));
        var corners = new List<int>();
        for (int i = 1; i < n - 1; i++)
            if (Vector2.Dot(dir[i - 1], dir[i]) < 0.99f) corners.Add(i);

        var path = new List<(Vector2 P, float T)> { (Centre(lane[0], cell), 0f) };
        for (int c = 0; c < corners.Count; c++)
        {
            int k = corners[c];
            float before = (k - (c > 0 ? corners[c - 1] : 0)) * cell * (c > 0 ? 0.5f : 1f);
            float after = ((c + 1 < corners.Count ? corners[c + 1] : n - 1) - k) * cell * (c + 1 < corners.Count ? 0.5f : 1f);
            float r = MathF.Min(cornerRadius, MathF.Min(before, after));
            var din = dir[k - 1]; var dout = dir[k]; var p = Centre(lane[k], cell);
            var a = p - din * r; var b = p + dout * r;
            var centre = a + dout * r; // arc centre: perpendicular offset from the entry tangent point
            float tA = k - r / cell, tB = k + r / cell;
            int segs = Math.Max(2, (int)MathF.Ceiling(r * MathF.PI / 2f / 0.1f));
            float a0 = MathF.Atan2(a.Y - centre.Y, a.X - centre.X), a1 = MathF.Atan2(b.Y - centre.Y, b.X - centre.X);
            float da = a1 - a0;
            while (da > MathF.PI) da -= 2 * MathF.PI;
            while (da < -MathF.PI) da += 2 * MathF.PI;
            for (int s = 0; s <= segs; s++)
            {
                float u = s / (float)segs, ang = a0 + da * u;
                var pt = s == 0 ? a : s == segs ? b : centre + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r;
                path.Add((pt, tA + (tB - tA) * u));
            }
        }
        path.Add((Centre(lane[n - 1], cell), n - 1));
        return path;
    }

    /// <summary>
    /// Points every <paramref name="step"/> metres along the 3D polyline from its first point, and its last point. With
    /// <paramref name="keep"/>, each polyline vertex it marks is a point too, and the step's points on a piece between two marked
    /// vertices, or within <see cref="KeptGap"/> of one, are left out (the step's count along the path is unchanged).
    /// </summary>
    private static IReadOnlyList<Vector3> Resample(List<Vector3> poly, float step, bool[]? keep = null)
    {
        var result = new List<Vector3> { poly[0] };
        float nextAt = step, walked = 0f;
        for (int i = 1; i < poly.Count; i++)
        {
            float seg = Vector3.Distance(poly[i - 1], poly[i]);
            bool keepA = keep?[i - 1] == true, keepB = keep?[i] == true;
            while (seg > 0f && walked + seg >= nextAt - 1e-4f)
            {
                float f = Math.Clamp((nextAt - walked) / seg, 0f, 1f);
                float d = f * seg;
                if (!(keepA && keepB) && !(keepA && d < KeptGap) && !(keepB && seg - d < KeptGap))
                    result.Add(Vector3.Lerp(poly[i - 1], poly[i], f));
                nextAt += step;
            }
            walked += seg;
            if (keepB) result.Add(poly[i]);
        }
        if (Vector3.Distance(result[^1], poly[^1]) > 1e-3f) result.Add(poly[^1]);
        else result[^1] = poly[^1];
        return result;
    }
}
