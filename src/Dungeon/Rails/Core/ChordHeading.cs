using System.Numerics;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The heading a cart takes at S: along the chord from S − <see cref="HalfSpan"/> to S + <see cref="HalfSpan"/>, not the
/// tangent of the straight piece under S. The centreline is straight 0.5 m pieces (<c>RailCurves.Centreline</c>), laid with
/// no handles, so the tangent jumps ~9.5 degrees at each join of a 3 m bend and holds between; a chord as long as a piece
/// always spans one join at most, and turns evenly through it. The points and every S stay as they are. Engine-free.
/// </summary>
public static class ChordHeading
{
    /// <summary>Half the chord: half the centreline's 0.5 m resample step.</summary>
    public const float HalfSpan = 0.25f;

    /// <summary>
    /// The S range whose chord gives the heading at <paramref name="s"/> on a line <paramref name="length"/> long: ±half about
    /// S, slid inside the line within half of either end (it keeps its length), the whole line when that is shorter.
    /// </summary>
    public static (float From, float To) Window(float s, float length, float half = HalfSpan)
    {
        if (length <= 2f * half) return (0f, Math.Max(0f, length));
        s = Math.Clamp(s, 0f, length);
        if (s - half < 0f) return (0f, 2f * half);
        if (s + half > length) return (length - 2f * half, length);
        return (s - half, s + half);
    }

    /// <summary>
    /// A frame's axes (X right, Y up, Z back: a Godot basis's columns) with −Z along <paramref name="behind"/> →
    /// <paramref name="ahead"/> and Y the nearest to <paramref name="up"/> square to it. False when the chord is too short or
    /// runs along the up: the caller keeps its own basis.
    /// </summary>
    public static bool Axes(Vector3 behind, Vector3 ahead, Vector3 up, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        x = y = z = default;
        var along = ahead - behind;
        if (along.LengthSquared() < 1e-8f) return false;
        var back = -Vector3.Normalize(along);
        var right = Vector3.Cross(up, back);
        if (right.LengthSquared() < 1e-8f) return false;
        x = Vector3.Normalize(right);
        z = back;
        y = Vector3.Cross(z, x);
        return true;
    }

    /// <summary>The flat heading of a forward vector, in radians: 0 facing −Z, positive turned left (toward −X).</summary>
    public static float Yaw(Vector3 forward) => MathF.Atan2(-forward.X, -forward.Z);

    /// <summary>How far ahead <see cref="Curvature"/> looks: the change in heading over the next two metres.</summary>
    public const float CurvatureSpan = 2f;

    /// <summary>The flat heading (<see cref="Yaw"/>) of the chord at <paramref name="s"/> on a line read by <paramref name="point"/>.</summary>
    public static float Heading(Func<float, Vector3> point, float s, float length)
    {
        var (from, to) = Window(s, length);
        return Yaw(point(to) - point(from));
    }

    /// <summary>
    /// The signed change in flat heading over the next <paramref name="span"/> metres toward higher S, per metre (positive bends
    /// left), between the chord headings at either end, as <c>TrackSampler.Frame</c> heads the cart: the lean's target turns
    /// evenly through a bend instead of stepping by a join's ~9.5 degrees as the span takes in four joins or five.
    /// </summary>
    public static float Curvature(Func<float, Vector3> point, float s, float length, float span = CurvatureSpan)
    {
        float a = Math.Clamp(s, 0f, Math.Max(0f, length)), b = Math.Min(length, a + span);
        if (b - a < 0.01f) return 0f;
        return Wrap(Heading(point, b, length) - Heading(point, a, length)) / (b - a);
    }

    private static float Wrap(float angle)
    {
        angle = MathF.IEEERemainder(angle, 2f * MathF.PI);
        return angle;
    }
}
