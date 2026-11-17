using System.Numerics;

namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Turns a lane of grid cells into a smooth world-space centreline. Engine-free: no Godot types.</summary>
public static class RailCurves
{
    /// <summary>
    /// World-space points along the lane: straight between cell centres, each 90 degree turn replaced by a quarter circle
    /// (radius clamped so an arc uses at most half the run to a neighbouring corner, the full run to a lane end), heights
    /// interpolated linearly between cell-centre heights, resampled every <paramref name="step"/> metres of path length.
    /// The first point is the first cell's centre and the last point the last cell's centre.
    /// </summary>
    public static IReadOnlyList<Vector3> Centreline(IReadOnlyList<GridPos> lane, Func<GridPos, float> heightOf,
        float cell = 4f, float cornerRadius = 3f, float step = 0.5f)
    {
        if (lane.Count == 0) return Array.Empty<Vector3>();
        Vector2 Centre(GridPos g) => new(g.X * cell + cell / 2f, g.Y * cell + cell / 2f);
        if (lane.Count == 1) return new[] { new Vector3(Centre(lane[0]).X, heightOf(lane[0]), Centre(lane[0]).Y) };

        int n = lane.Count;
        var dir = new Vector2[n - 1];
        for (int i = 0; i < n - 1; i++)
            dir[i] = Vector2.Normalize(Centre(lane[i + 1]) - Centre(lane[i]));
        var corners = new List<int>();
        for (int i = 1; i < n - 1; i++)
            if (Vector2.Dot(dir[i - 1], dir[i]) < 0.99f) corners.Add(i);

        // (x, z, param) where param is the fractional lane index, used to interpolate height.
        var path = new List<(Vector2 P, float T)> { (Centre(lane[0]), 0f) };
        for (int c = 0; c < corners.Count; c++)
        {
            int k = corners[c];
            float before = (k - (c > 0 ? corners[c - 1] : 0)) * cell * (c > 0 ? 0.5f : 1f);
            float after = ((c + 1 < corners.Count ? corners[c + 1] : n - 1) - k) * cell * (c + 1 < corners.Count ? 0.5f : 1f);
            float r = MathF.Min(cornerRadius, MathF.Min(before, after));
            var din = dir[k - 1]; var dout = dir[k]; var p = Centre(lane[k]);
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
        path.Add((Centre(lane[n - 1]), n - 1));

        float Height(float t)
        {
            int i = Math.Clamp((int)MathF.Floor(t), 0, n - 2);
            float f = Math.Clamp(t - i, 0f, 1f);
            return heightOf(lane[i]) + (heightOf(lane[i + 1]) - heightOf(lane[i])) * f;
        }
        var poly = path.Select(q => new Vector3(q.P.X, Height(q.T), q.P.Y)).ToList();

        var result = new List<Vector3> { poly[0] };
        float nextAt = step, walked = 0f;
        for (int i = 1; i < poly.Count; i++)
        {
            float seg = Vector3.Distance(poly[i - 1], poly[i]);
            while (seg > 0f && walked + seg >= nextAt - 1e-4f)
            {
                float f = Math.Clamp((nextAt - walked) / seg, 0f, 1f);
                result.Add(Vector3.Lerp(poly[i - 1], poly[i], f));
                nextAt += step;
            }
            walked += seg;
        }
        if (Vector3.Distance(result[^1], poly[^1]) > 1e-3f) result.Add(poly[^1]);
        else result[^1] = poly[^1];
        return result;
    }
}
