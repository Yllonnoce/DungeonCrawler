using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The "cart coming" geometry, one rule for the bark (<see cref="RailController"/>'s Warn) and every peer's bell (<see cref="CartSound"/>):
/// a body on the rails ahead of a cart at <c>s</c> (its middle, running toward higher S unless told its way) within the 3 s look-ahead (at
/// <see cref="RunOverRules.WarnMinSpeed"/> at least). No allocation.
/// </summary>
public static class RailWarn
{
    public const float Seconds = 3f;

    /// <summary>
    /// A body at <paramref name="pos"/> stands in the way of a cart at <paramref name="s"/> moving at <paramref name="v"/> on <paramref name="track"/>
    /// in its way of travel (<paramref name="dir"/>), looked for from its leading face, <paramref name="lead"/> past its own front (a train rolling
    /// back: its last wagon's back, plan 16).
    /// </summary>
    public static bool Ahead(TrackSampler track, float s, float v, Vector3 pos, int dir = 1, float lead = 0f)
    {
        if (!track.Valid) return false;
        var look = Mathf.Max(v, RunOverRules.WarnMinSpeed) * Seconds;
        float hint = dir >= 0 && lead == 0f ? s + CartRules.HalfLength + look / 2f : s + dir * (CartRules.HalfLength + lead + look / 2f), window = look / 2f + 2f;
        // a warned body stands within 2 m of the line at most look + HalfLength (+ lead) ahead, and no stretch of line is shorter than the straight way
        var reach = look + CartRules.HalfLength + lead + 3f;
        if (pos.DistanceSquaredTo(track.Point(s)) > reach * reach) return false;
        if (!track.Measure(pos, hint, window, out var b)) return false;
        return RunOverRules.InWarning(s, v, b with { Radius = RunOverSweep.BodyRadius }, Seconds, dir, lead);
    }

    /// <summary>Any of <paramref name="bodies"/> (the heroes this peer sees on foot) stands in the way of the cart (<see cref="Ahead"/>, its way of travel and lead).</summary>
    public static bool AnyoneAhead(TrackSampler track, float s, float v, IReadOnlyList<Node3D> bodies, int dir = 1, float lead = 0f)
    {
        for (var i = 0; i < bodies.Count; i++)
            if (GodotObject.IsInstanceValid(bodies[i]) && Ahead(track, s, v, bodies[i].GlobalPosition, dir, lead)) return true;
        return false;
    }

    /// <summary>The nearest of <paramref name="bodies"/> on the rails within a few metres of <paramref name="s"/>, either side, as a distance along the line (+∞ for none).</summary>
    public static float NearestGap(TrackSampler track, float s, IReadOnlyList<Node3D> bodies)
    {
        var best = float.PositiveInfinity;
        if (!track.Valid) return best;
        var at = track.Point(s);
        const float near = CartRules.HalfLength + 4f;
        for (var i = 0; i < bodies.Count; i++)
        {
            if (!GodotObject.IsInstanceValid(bodies[i])) continue;
            var pos = bodies[i].GlobalPosition;
            if (pos.DistanceSquaredTo(at) > near * near || !track.Measure(pos, s, near, out var b)) continue;
            // on the rails: InWarning from a cart a little behind it (it is past that cart's middle and within its look)
            if (!RunOverRules.InWarning(b.S - 0.01f, 0f, b with { Radius = RunOverSweep.BodyRadius })) continue;
            best = Mathf.Min(best, Mathf.Abs(b.S - s));
        }
        return best;
    }

    /// <summary>Seconds until a cart at <paramref name="s"/> (front a half length on) moving at <paramref name="v"/> reaches a body on the rails ahead at <paramref name="pos"/>, within 6 s; else +∞.</summary>
    public static float SecondsTo(TrackSampler track, float s, float v, Vector3 pos)
    {
        if (!track.Valid || v < 0.1f) return float.PositiveInfinity;
        const float horizon = 6f;
        var look = Mathf.Max(v, RunOverRules.WarnMinSpeed) * horizon;
        var reach = look + CartRules.HalfLength + 3f;
        if (pos.DistanceSquaredTo(track.Point(s)) > reach * reach) return float.PositiveInfinity;
        if (!track.Measure(pos, s + CartRules.HalfLength + look / 2f, look / 2f + 2f, out var b)) return float.PositiveInfinity;
        if (!RunOverRules.InWarning(s, v, b with { Radius = RunOverSweep.BodyRadius }, horizon)) return float.PositiveInfinity;
        return Mathf.Max(0f, b.S - (s + CartRules.HalfLength)) / v;
    }
}
