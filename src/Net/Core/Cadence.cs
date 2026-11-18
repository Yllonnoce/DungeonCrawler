namespace DungeonCrawler.Net;

/// <summary>
/// Something done every <see cref="Period"/> seconds of elapsed time, whatever the physics rate: feed it each step's delta and
/// it says how many periods passed, keeping the remainder. At 60 Hz a period of n/60 s fires on the same ticks as the old
/// <c>tick % n == 0</c> (counting from the cadence's first step), at 90 Hz as often over the same wall time. Engine-free; a
/// mutable struct, so keep it in a field and call it there.
/// </summary>
public struct Cadence
{
    /// <summary>A step sum this close to a period counts as reaching it (sums of 1/60 land a hair short).</summary>
    public const double Tolerance = 1e-6;

    public double Period { get; }
    private double _elapsed;

    public Cadence(double period)
    {
        Period = period;
        _elapsed = 0;
    }

    /// <summary>Adds <paramref name="dt"/> seconds; returns how many whole periods have now elapsed (usually 0 or 1).</summary>
    public int Advance(double dt)
    {
        _elapsed += dt;
        if (Period <= 0 || _elapsed + Tolerance < Period) return 0;
        var n = (int)Math.Floor((_elapsed + Tolerance) / Period);
        _elapsed -= n * Period; // may go a hair negative: the tolerance is paid back, so nothing drifts
        return n;
    }

    /// <summary>Starts the count again from nothing.</summary>
    public void Reset() => _elapsed = 0;
}
