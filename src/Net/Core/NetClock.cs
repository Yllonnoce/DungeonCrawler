namespace DungeonCrawler.Net;

/// <summary>
/// A client's estimate of the server's clock (<c>NetSession.Instance.Now</c> on the server), from Ping/Pong. Each pong is a
/// sample: its offset is <c>serverTime − (sentAt + now) / 2</c> (the server answered half way through the round trip) and its
/// round trip <c>now − sentAt</c>. The clock keeps the last <see cref="Window"/> samples and uses the median offset of those
/// whose round trip is at most twice the median, so one slow or lopsided round trip does not move it. The Welcome's server
/// time seeds it (<see cref="Seed"/>) until the first pong. Medians are the lower middle of an even count. Engine-free.
/// </summary>
public sealed class NetClock
{
    public const int Window = 9;

    private readonly double[] _offsets = new double[Window];
    private readonly double[] _rtts = new double[Window];
    private int _count;
    private int _next;
    private double _offset;
    private bool _seeded;

    /// <summary>Whether the clock has anything to go on: a seed or one real sample.</summary>
    public bool Ready => _seeded || _count > 0;

    /// <summary>The median round trip of the real samples (seconds); 0 before the first pong.</summary>
    public double Rtt { get; private set; }

    /// <summary>Server time minus client time.</summary>
    public double Offset => _offset;

    /// <summary>
    /// The starting offset, from a one-way message (the Welcome): server time then, our time on receipt. Kept out of the
    /// round-trip statistics and the sample window; the first real pong replaces it. Ignored once real samples exist.
    /// </summary>
    public void Seed(double serverTime, double clientNow)
    {
        if (_count > 0) return;
        _offset = serverTime - clientNow;
        _seeded = true;
    }

    /// <summary>A pong arrived at <paramref name="clientNow"/> for the ping sent at <paramref name="clientSentAt"/>; the server stamped it <paramref name="serverTime"/>.</summary>
    public void OnPong(double clientSentAt, double clientNow, double serverTime)
    {
        var rtt = clientNow - clientSentAt;
        if (!double.IsFinite(rtt) || rtt < 0 || !double.IsFinite(serverTime)) return;
        _offsets[_next] = serverTime - (clientSentAt + clientNow) / 2;
        _rtts[_next] = rtt;
        _next = (_next + 1) % Window;
        if (_count < Window) _count++;

        Span<double> rtts = stackalloc double[Window];
        Span<double> offsets = stackalloc double[Window];
        for (var i = 0; i < _count; i++) rtts[i] = _rtts[i];
        var medianRtt = LowerMedian(rtts[.._count]);
        var kept = 0;
        for (var i = 0; i < _count; i++)
            if (_rtts[i] <= 2 * medianRtt) offsets[kept++] = _offsets[i];
        Rtt = medianRtt;
        _offset = LowerMedian(offsets[..kept]); // the median itself always passes the filter, so kept >= 1
    }

    /// <summary>The server's clock now, given ours.</summary>
    public double ServerNow(double clientNow) => clientNow + _offset;

    private static double LowerMedian(Span<double> values)
    {
        values.Sort();
        return values[(values.Length - 1) / 2];
    }
}
