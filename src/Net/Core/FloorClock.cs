namespace DungeonCrawler.Net;

/// <summary>
/// A client's copy of the floor clock: the seconds the server's floor has run (<c>DungeonBuilder.FloorSeconds</c>, the sum of
/// its physics steps). The server publishes pairs (FloorSeconds, its Now) in the Welcome, the FloorChange and a
/// <see cref="FloorClockMsg"/> every second or two; between pairs the floor runs at the server clock's rate. The read slews
/// toward the latest pair at 0.9 to 1.1 times real time, so it never jumps and never runs backwards, and snaps only when it is
/// more than <see cref="SnapSeconds"/> off (a server stall, a new floor). For the first <see cref="SettleSeconds"/> after the first
/// read of a floor (a new clock or a <see cref="Reset"/>) it settles: a read more than a metronome tick behind its target snaps
/// forward, and one more than a tick (but under <see cref="SnapSeconds"/>) ahead holds until the target catches it (review I1:
/// the first pairs of a floor can disagree with the first reads by more than the slew closes in seconds). Before the first pair
/// it reads 0. Engine-free.
/// </summary>
public sealed class FloorClock
{
    public const double SnapSeconds = 0.25;
    public const double MinRate = 0.9;
    public const double MaxRate = 1.1;
    /// <summary>How long after a floor's first read an error over one tick snaps (behind) or holds (ahead) instead of slewing.</summary>
    public const double SettleSeconds = 5;
    /// <summary>The floor's metronome runs in 60ths of a second, whatever the physics rate.</summary>
    public const int TicksPerSecond = 60;
    private const double Tick = 1.0 / TicksPerSecond;

    private bool _hasPair;
    private double _pairFloorSeconds;
    private double _pairServerTime;
    private bool _started;
    private double _read;
    private double _lastServerNow;
    private double _settleUntil;

    public bool HasPair => _hasPair;

    /// <summary>A pair from the server: its floor had run <paramref name="floorSeconds"/> at its time <paramref name="serverTime"/>.</summary>
    public void Apply(double floorSeconds, double serverTime)
    {
        if (!double.IsFinite(floorSeconds) || !double.IsFinite(serverTime)) return;
        _hasPair = true;
        _pairFloorSeconds = floorSeconds;
        _pairServerTime = serverTime;
    }

    /// <summary>Forget the floor (a new one is coming): reads 0 until the next pair.</summary>
    public void Reset()
    {
        _hasPair = false;
        _started = false;
        _read = 0;
    }

    /// <summary>The floor's seconds at server time <paramref name="serverNow"/>. Calling it twice at the same time gives the same value.</summary>
    public double Read(double serverNow)
    {
        if (!_hasPair) return 0;
        var target = _pairFloorSeconds + (serverNow - _pairServerTime);
        if (!_started)
        {
            _started = true;
            _read = target;
            _lastServerNow = serverNow;
            _settleUntil = serverNow + SettleSeconds;
            return _read;
        }
        var dt = Math.Max(0, serverNow - _lastServerNow); // the server clock estimate can step back a hair: hold, never run backwards
        var error = target - (_read + dt);
        if (Math.Abs(error) > SnapSeconds || (error > Tick && serverNow < _settleUntil))
        {
            _read = target;
            _lastServerNow = serverNow; // the snap's own time: kept at a later one, the next reads would see dt = 0 and stand still (review I1)
            return _read;
        }
        if (error < -Tick && serverNow < _settleUntil)
        {
            _lastServerNow += dt; // settling and ahead: hold until the target catches the read, never backwards
            return _read;
        }
        _read += Math.Clamp(dt + error, dt * MinRate, dt * MaxRate);
        _lastServerNow += dt;
        return _read;
    }

    /// <summary>The 60 Hz metronome tick of a floor time (<c>DungeonBuilder.FloorTick</c>): the same tick count at any physics rate.</summary>
    public static int TickOf(double seconds) => (int)Math.Floor(seconds * TicksPerSecond + 1e-6);
}

/// <summary>
/// A running sum of step lengths that stays exact over hours (compensated summation): a plain double sum of 1/60 falls a tick
/// short of <see cref="FloorClock.TickOf"/> after about two hours of a floor. Engine-free; a mutable struct, keep it in a field.
/// </summary>
public struct ElapsedSeconds
{
    private double _sum;
    private double _carry;

    public double Value => _sum;

    public void Add(double dt)
    {
        var y = dt - _carry;
        var t = _sum + y;
        _carry = (t - _sum) - y;
        _sum = t;
    }

    public void Reset()
    {
        _sum = 0;
        _carry = 0;
    }
}
