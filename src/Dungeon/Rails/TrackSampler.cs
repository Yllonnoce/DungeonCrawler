using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Reads a <see cref="RailTrack"/> by distance along it, in global space: the frame a cart sits on, a point, the slope
/// and the bend at S, and where a body stands relative to the line (<see cref="Measure"/>). Shared by every cart on the
/// track. No allocation.
/// </summary>
public sealed class TrackSampler
{
    const float CurvatureSpan = 2f;
    const float SearchStep = 0.25f;
    /// <summary>A body whose nearest point is at the edge of the searched window and farther than this is outside it.</summary>
    const float OutsideBeyond = 2f;
    const int RefinePasses = 14; // golden-section iterations over ±SearchStep: the bracket ends under a millimetre wide

    public RailTrack Track { get; }

    public TrackSampler(RailTrack track) => Track = track;

    public float Length => Track.Length;

    /// <summary>The track node still exists (it dies with the floor).</summary>
    public bool Valid => GodotObject.IsInstanceValid(Track);

    private float Clamp(float s) => Mathf.Clamp(s, 0f, Track.Length);

    /// <summary>The curve's frame at S in global space: −Z along the line toward higher S, Y up from the track.</summary>
    public Transform3D Frame(float s) => Track.GlobalTransform * Track.Curve.SampleBakedWithRotation(Clamp(s), true, true);

    public Vector3 Point(float s) => Track.ToGlobal(Track.Curve.SampleBaked(Clamp(s), true));

    /// <summary>The S of the whole line's closest point to <paramref name="pos"/> (the baked curve's closest offset: it may pick another stretch passing near).</summary>
    public float ClosestS(Vector3 pos) => Track.Curve.GetClosestOffset(Track.ToLocal(pos));

    /// <summary>Rise over run at S (toward higher S): the track's height half a metre either side.</summary>
    public float Slope(float s)
    {
        float a = Mathf.Max(0f, s - 0.5f), b = Mathf.Min(Track.Length, s + 0.5f);
        return b - a < 0.01f ? 0f : (Point(b).Y - Point(a).Y) / (b - a);
    }

    /// <summary>The signed change in flat heading over the next two metres toward higher S, per metre (positive bends left).</summary>
    public float Curvature(float s)
    {
        float a = Clamp(s), b = Mathf.Min(Track.Length, a + CurvatureSpan);
        if (b - a < 0.01f) return 0f;
        return Mathf.Wrap(Heading(b) - Heading(a), -Mathf.Pi, Mathf.Pi) / (b - a);
    }

    private float Heading(float s)
    {
        var f = -(Track.GlobalBasis * Track.Curve.SampleBakedWithRotation(s, true, true).Basis).Z;
        return Mathf.Atan2(-f.X, -f.Z);
    }

    /// <summary>
    /// Where a body at <paramref name="pos"/> stands relative to the line, looking only at
    /// [<paramref name="sHint"/> − <paramref name="window"/>, <paramref name="sHint"/> + <paramref name="window"/>] of the
    /// baked curve: sampled every 0.25 m, the nearest (3D) refined by a golden-section search over ±0.25 m. Unlike the
    /// whole-line closest offset it never picks another stretch of the line that passes near this one. False when the
    /// nearest point is at the window's edge and more than 2 m away: the body is outside the window. Radius is left 0.
    /// </summary>
    public bool Measure(Vector3 pos, float sHint, float window, out TrackBody body)
    {
        var curve = Track.Curve;
        var local = Track.ToLocal(pos); // the track is never scaled: distances in its space are the world's
        float lo = Clamp(sHint - window), hi = Clamp(sHint + window);
        float best = lo, bestD = float.MaxValue;
        var steps = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / SearchStep));
        for (var i = 0; i <= steps; i++)
        {
            var s = i == steps ? hi : lo + i * SearchStep;
            var d = curve.SampleBaked(s, true).DistanceSquaredTo(local);
            if (d < bestD) { bestD = d; best = s; }
        }
        var atEdge = best <= lo || best >= hi;

        // golden-section refinement around the best sample
        float a = Mathf.Max(lo, best - SearchStep), b = Mathf.Min(hi, best + SearchStep);
        const float invPhi = 0.618034f;
        float c = b - (b - a) * invPhi, e = a + (b - a) * invPhi;
        float fc = curve.SampleBaked(c, true).DistanceSquaredTo(local), fe = curve.SampleBaked(e, true).DistanceSquaredTo(local);
        for (var i = 0; i < RefinePasses; i++)
        {
            if (fc < fe) { b = e; e = c; fe = fc; c = b - (b - a) * invPhi; fc = curve.SampleBaked(c, true).DistanceSquaredTo(local); }
            else { a = c; c = e; fc = fe; e = a + (b - a) * invPhi; fe = curve.SampleBaked(e, true).DistanceSquaredTo(local); }
        }
        var refined = (a + b) / 2f;
        var refinedD = curve.SampleBaked(refined, true).DistanceSquaredTo(local);
        if (refinedD < bestD) { best = refined; bestD = refinedD; }

        var p = Point(best);
        float dx = pos.X - p.X, dz = pos.Z - p.Z;
        body = new TrackBody(best, Mathf.Sqrt(dx * dx + dz * dz), pos.Y - p.Y, 0f);
        return !(atEdge && bestD > OutsideBeyond * OutsideBeyond);
    }
}
