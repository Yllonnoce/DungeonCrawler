namespace DungeonCrawler.Core;

/// <summary>
/// How fast the ground should pass under a character exported from the 3dObject viewer for its Walk and Run clips
/// to look planted. The viewer's gaits swing the thigh sinusoidally (walk ±25° over a 1.2 s cycle, run ±45° over
/// 0.7 s) on a leg that is 0.45 of the character's height, two steps per cycle; a clip played at normal speed
/// therefore implies a ground speed proportional to height. Playing the clip at (actual speed ÷ implied speed)
/// keeps the feet from sliding or spinning in place. Engine-free so the tests can compile it.
/// </summary>
public static class ViewerGait
{
    public const float MinScale = 0.5f, MaxScale = 2.5f;
    private const float LegFraction = 0.45f;

    /// <summary>Metres per second a 1.0-speed clip implies for a character of <paramref name="height"/> metres; 0 for anything that is not a gait.</summary>
    public static float ImpliedSpeed(string clip, float height)
    {
        var (swingDeg, cycle) = clip switch { "Walk" => (25f, 1.2f), "Run" => (45f, 0.7f), _ => (0f, 0f) };
        if (swingDeg == 0f || height <= 0f) return 0f;
        var step = 2f * LegFraction * height * MathF.Sin(swingDeg * MathF.PI / 180f);
        return 2f * step / cycle;
    }

    /// <summary>Walk or Run, whichever cycle's natural pace is nearer to <paramref name="groundSpeed"/> (the switch sits at the geometric mean of the two), or Idle when barely moving. A fast walker runs rather than playing a frantic walk.</summary>
    public static string GaitFor(float height, float groundSpeed)
    {
        if (groundSpeed <= 0.5f) return "Idle";
        var walk = ImpliedSpeed("Walk", height);
        var run = ImpliedSpeed("Run", height);
        if (walk <= 0f || run <= 0f) return "Walk";
        return groundSpeed < MathF.Sqrt(walk * run) ? "Walk" : "Run";
    }

    /// <summary>AnimationPlayer speed scale that makes <paramref name="clip"/> cover the ground at <paramref name="groundSpeed"/>; 1 when unknown.</summary>
    public static float SpeedScaleFor(string clip, float height, float groundSpeed)
    {
        var implied = ImpliedSpeed(clip, height);
        if (implied <= 0f) return 1f;
        return Math.Clamp(groundSpeed / implied, MinScale, MaxScale);
    }
}
