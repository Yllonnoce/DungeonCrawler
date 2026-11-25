namespace DungeonCrawler.Core;

/// <summary>
/// Frame times over a window (the perf log line's 10 s, a --test-perf phase): every frame kept, so the median, the 1% low and the
/// worst frame are exact. Seconds are counted on the frames' own clock (a frame belongs to the second it ends in), and only complete
/// seconds count toward the worst second. Allocation-free once the lists have grown to a window's size. Engine-free so the tests can
/// compile it.
/// </summary>
public sealed class FrameStats
{
    /// <summary>A second's boundary is crossed this close (float frame times summed in double).</summary>
    private const double BoundaryEpsilon = 1e-4;

    private readonly List<float> _frames = new();
    private readonly List<float> _sorted = new();
    private bool _sortedValid;
    private double _elapsed;
    private float _worstFrame;
    private int _inSecond;
    private double _secondEnd = 1.0;
    private int _worstSecond = -1;

    public int Frames => _frames.Count;
    /// <summary>The window's length on the frames' clock, in seconds.</summary>
    public double Elapsed => _elapsed;

    public void Add(float frameSeconds)
    {
        if (!(frameSeconds >= 0f)) frameSeconds = 0f; // NaN or negative: a frame that took no time
        _frames.Add(frameSeconds);
        _sortedValid = false;
        _elapsed += frameSeconds;
        if (frameSeconds > _worstFrame) _worstFrame = frameSeconds;
        // the seconds this frame passes whole close before it lands: a frame longer than a second leaves the seconds it spans with none
        while (_elapsed > _secondEnd + BoundaryEpsilon) CloseSecond();
        _inSecond++; // the frame counts in the second it ends in (one ending on a boundary, in the second it closes)
        if (_elapsed >= _secondEnd - BoundaryEpsilon) CloseSecond();
    }

    private void CloseSecond()
    {
        if (_worstSecond < 0 || _inSecond < _worstSecond) _worstSecond = _inSecond;
        _inSecond = 0;
        _secondEnd += 1.0;
    }

    /// <summary>Frames over elapsed time (the perf line's avg); 0 for an empty window.</summary>
    public float MeanFps => _elapsed > 0 ? (float)(_frames.Count / _elapsed) : 0f;

    /// <summary>The fps of the median frame time; 0 for an empty window.</summary>
    public float MedianFps
    {
        get
        {
            if (_frames.Count == 0) return 0f;
            var s = Sorted();
            var n = s.Count;
            var median = n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2f;
            return median > 0f ? 1f / median : 0f;
        }
    }

    /// <summary>The fps of the slowest <paramref name="fraction"/> of frames (at least one): their mean frame time, inverted. 0 for an empty window.</summary>
    public float LowFps(float fraction = 0.01f)
    {
        if (_frames.Count == 0) return 0f;
        var s = Sorted();
        var n = Math.Clamp((int)Math.Ceiling(s.Count * (double)fraction), 1, s.Count);
        double sum = 0;
        for (var i = s.Count - n; i < s.Count; i++) sum += s[i];
        return sum > 0 ? (float)(n / sum) : 0f;
    }

    /// <summary>The lowest frame count over the window's complete seconds; the mean fps while no second is complete yet.</summary>
    public float WorstSecondFps => _worstSecond >= 0 ? _worstSecond : MeanFps;

    /// <summary>The longest frame, in ms.</summary>
    public float WorstFrameMs => _worstFrame * 1000f;

    public void Reset()
    {
        _frames.Clear();
        _sorted.Clear();
        _sortedValid = false;
        _elapsed = 0;
        _worstFrame = 0f;
        _inSecond = 0;
        _secondEnd = 1.0;
        _worstSecond = -1;
    }

    private List<float> Sorted()
    {
        if (_sortedValid) return _sorted;
        _sorted.Clear();
        _sorted.AddRange(_frames);
        _sorted.Sort();
        _sortedValid = true;
        return _sorted;
    }
}

/// <summary>The perf log's cadence and its slow-frame threshold.</summary>
public static class PerfRules
{
    /// <summary>The perf line every this many seconds in game.</summary>
    public const float LineEvery = 10f;
    /// <summary>A frame longer than this (ms) is slow and earns its own line.</summary>
    public const float SlowFrameMs = 50f;
    /// <summary>At most one slow-frame line this often (seconds); the slow frames between are counted in the next.</summary>
    public const float SlowLineGap = 1f;

    /// <summary>After a floor is built, this many seconds of frames are not counted (the load's settling: first draws, shader and model warm-up).</summary>
    public const float BuildGrace = 2f;

    public static bool Slow(float frameMs) => frameMs > SlowFrameMs;
}
