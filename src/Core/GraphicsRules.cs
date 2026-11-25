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

    /// <summary>Whether this scale is drawn with FSR (any step below full); full scale uses bilinear, which at 1.0 is no scaling at all.</summary>
    public static bool UsesFsr(float scale) => scale < 0.999f;
}
