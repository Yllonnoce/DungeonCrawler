namespace DungeonCrawler.Core;

/// <summary>The Display settings' render scale: the steps the menu offers, and when the upscaler is FSR. Engine-free so the tests can compile it.</summary>
public static class GraphicsRules
{
    /// <summary>The render scales offered, full first; below full the 3D view is drawn smaller and upscaled with FSR.</summary>
    public static readonly float[] RenderScales = { 1f, 0.85f, 0.7f, 0.5f };

    /// <summary>A tie between two steps closer than this goes to the higher one.</summary>
    private const float TieEpsilon = 1e-5f;

    /// <summary>The listed step nearest <paramref name="v"/> (the higher one on a tie); 1.0 for NaN.</summary>
    public static float SnapScale(float v)
    {
        if (float.IsNaN(v)) return 1f;
        v = Math.Clamp(v, 0f, 2f); // an infinity is as far as any big number
        var best = RenderScales[0];
        var bestD = Math.Abs(v - best);
        foreach (var step in RenderScales)
        {
            var d = Math.Abs(v - step);
            if (d < bestD - TieEpsilon || (Math.Abs(d - bestD) <= TieEpsilon && step > best)) { best = step; bestD = d; }
        }
        return best;
    }

    /// <summary>The smallest 3D scale fullscreen draws at, however low the resolution and render scale go (a quarter of the screen's lines).</summary>
    public const float MinScale = 0.25f;

    /// <summary>
    /// What the root viewport is given for the saved <paramref name="setting"/>: its snapped step and whether FSR draws it. In a headset the
    /// setting does nothing (the headset drives its own scale): full scale, no FSR, whatever was saved.
    /// Fullscreen fills the screen whatever resolution is chosen, so a chosen one under the screen's is honoured by drawing the 3D view at
    /// its height (<paramref name="fullscreenHeight"/> of the screen's <paramref name="screenHeight"/> lines) and upscaling with FSR, times
    /// the render scale, never under <see cref="MinScale"/>; menus and HUD stay at the screen's size, so they stay sharp and fit. A
    /// resolution at or above the screen's, windowed (0) or an unknown screen (0) draws at the render scale alone.
    /// </summary>
    public static (float Scale, bool Fsr) ViewportScale(float setting, bool vr, int fullscreenHeight = 0, int screenHeight = 0)
    {
        if (vr) return (1f, false);
        var scale = SnapScale(setting);
        if (fullscreenHeight > 0 && screenHeight > 0 && fullscreenHeight < screenHeight)
            scale = Math.Max(MinScale, scale * fullscreenHeight / screenHeight);
        return (scale, UsesFsr(scale));
    }

    /// <summary>Whether this scale is drawn with FSR (any step below full); full scale uses bilinear, which at 1.0 is no scaling at all.</summary>
    public static bool UsesFsr(float scale) => scale < 0.999f;
}
