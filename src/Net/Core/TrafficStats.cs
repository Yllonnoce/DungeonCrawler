using System.Globalization;
using System.Text;

namespace DungeonCrawler.Net;

/// <summary>
/// Per-kind traffic counters for one transport: messages and payload bytes, sent and received. Cheap enough to leave on
/// (four array increments per packet, the kind read from the first byte without decoding), so a live server can say
/// what its bandwidth goes to (<c>--net-stats</c>, and the status file's <c>net</c> block). Bytes are the packet as the
/// game hands it to the socket: the kind byte plus the payload, gzipped when <see cref="NetCodec"/> gzips it. WebSocket
/// framing (2-4 bytes a message, 4 more from a client for the mask) and TCP/IP headers come on top.
/// </summary>
public sealed class TrafficStats
{
    /// <summary>The kind byte's low seven bits (the top bit is NetCodec's gzip flag).</summary>
    public const int Kinds = 128;
    private readonly long[] _sentMsgs = new long[Kinds], _sentBytes = new long[Kinds], _recvMsgs = new long[Kinds], _recvBytes = new long[Kinds];
    private long _sentGzipped, _recvGzipped;
    private double _startedAt = -1, _secondStart = -1;
    private long _secondSent, _secondRecv, _secondSentMsgs;

    public long SentMessages => _sentMsgs.Sum();
    public long SentBytes => _sentBytes.Sum();
    public long ReceivedMessages => _recvMsgs.Sum();
    public long ReceivedBytes => _recvBytes.Sum();
    /// <summary>The busiest whole second seen so far, out and in (bytes), and messages out in that second.</summary>
    public long PeakSentBytesPerSecond { get; private set; }
    public long PeakReceivedBytesPerSecond { get; private set; }
    public long PeakSentMessagesPerSecond { get; private set; }

    public static int KindOf(byte[] packet) => packet.Length == 0 ? 0 : packet[0] & 0x7F;

    /// <summary>One packet handed to the socket (a broadcast counts once per peer it reaches).</summary>
    public void Sent(byte[] packet)
    {
        var k = KindOf(packet);
        _sentMsgs[k]++;
        _sentBytes[k] += packet.Length;
        if (packet.Length > 0 && (packet[0] & 0x80) != 0) _sentGzipped++;
        _secondSent += packet.Length;
        _secondSentMsgs++;
    }

    public void Received(byte[] packet)
    {
        var k = KindOf(packet);
        _recvMsgs[k]++;
        _recvBytes[k] += packet.Length;
        if (packet.Length > 0 && (packet[0] & 0x80) != 0) _recvGzipped++;
        _secondRecv += packet.Length;
    }

    /// <summary>Called every frame with the clock in seconds: closes each whole second and keeps the peaks.</summary>
    public void Sample(double now)
    {
        if (_startedAt < 0) { _startedAt = now; _secondStart = now; return; }
        if (now - _secondStart < 1.0) return;
        var span = now - _secondStart;
        PeakSentBytesPerSecond = Math.Max(PeakSentBytesPerSecond, (long)(_secondSent / span));
        PeakReceivedBytesPerSecond = Math.Max(PeakReceivedBytesPerSecond, (long)(_secondRecv / span));
        PeakSentMessagesPerSecond = Math.Max(PeakSentMessagesPerSecond, (long)(_secondSentMsgs / span));
        _secondSent = _secondRecv = _secondSentMsgs = 0;
        _secondStart = now;
    }

    public double Seconds(double now) => _startedAt < 0 ? 0 : Math.Max(0.001, now - _startedAt);

    /// <summary>One row per kind that moved: (kind, sent messages, sent bytes, received messages, received bytes).</summary>
    public IEnumerable<(int Kind, long SentMsgs, long SentBytes, long RecvMsgs, long RecvBytes)> Rows()
    {
        for (var k = 0; k < Kinds; k++)
            if (_sentMsgs[k] > 0 || _recvMsgs[k] > 0) yield return (k, _sentMsgs[k], _sentBytes[k], _recvMsgs[k], _recvBytes[k]);
    }

    public static string KindName(int kind) => Enum.IsDefined(typeof(MsgKind), (byte)kind) ? ((MsgKind)kind).ToString() : $"kind{kind}";

    private static string Rate(double bytesPerSecond) => bytesPerSecond >= 1024 ? $"{bytesPerSecond / 1024:0.0} KB/s" : $"{bytesPerSecond:0} B/s";

    /// <summary>One line for the server's minute log: totals, peaks and the top senders.</summary>
    public string Summary(double now, int top = 6)
    {
        var s = Seconds(now);
        var inv = CultureInfo.InvariantCulture;
        var tops = Rows().Where(r => r.SentBytes > 0).OrderByDescending(r => r.SentBytes).Take(top)
            .Select(r => $"{KindName(r.Kind)} {Rate(r.SentBytes / s)} ({(r.SentMsgs / s).ToString("0.0", inv)}/s)");
        return $"{s:0} s: out {Rate(SentBytes / s)} {(SentMessages / s).ToString("0", inv)} msg/s (peak {Rate(PeakSentBytesPerSecond)}, {PeakSentMessagesPerSecond} msg/s), " +
               $"in {Rate(ReceivedBytes / s)} {(ReceivedMessages / s).ToString("0", inv)} msg/s (peak {Rate(PeakReceivedBytesPerSecond)}); top out: {string.Join(", ", tops)}";
    }

    /// <summary>A table for a client's quit (or a measurement): every kind that moved, busiest first.</summary>
    public string Report(double now)
    {
        var s = Seconds(now);
        var sb = new StringBuilder();
        sb.AppendLine(Summary(now));
        sb.AppendLine($"{"kind",-20} {"out msgs",9} {"out B/s",9} {"out avg B",9} {"in msgs",9} {"in B/s",9} {"in avg B",9}");
        foreach (var r in Rows().OrderByDescending(r => r.SentBytes + r.RecvBytes))
            sb.AppendLine($"{KindName(r.Kind),-20} {r.SentMsgs,9} {r.SentBytes / s,9:0} {(r.SentMsgs > 0 ? r.SentBytes / r.SentMsgs : 0),9} {r.RecvMsgs,9} {r.RecvBytes / s,9:0} {(r.RecvMsgs > 0 ? r.RecvBytes / r.RecvMsgs : 0),9}");
        sb.Append($"gzipped packets: out {_sentGzipped}, in {_recvGzipped}");
        return sb.ToString();
    }

    /// <summary>The status file's <c>net</c> object (hand-built JSON, as the rest of that file is).</summary>
    public string ToJson(double now)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = Seconds(now);
        var kinds = string.Join(",", Rows().OrderByDescending(r => r.SentBytes + r.RecvBytes)
            .Select(r => $"\"{KindName(r.Kind)}\":{{\"outMsgs\":{r.SentMsgs},\"outBytes\":{r.SentBytes},\"inMsgs\":{r.RecvMsgs},\"inBytes\":{r.RecvBytes}}}"));
        return $"{{\"seconds\":{s.ToString("0", inv)},\"outBytes\":{SentBytes},\"outMsgs\":{SentMessages},\"inBytes\":{ReceivedBytes},\"inMsgs\":{ReceivedMessages}," +
               $"\"outBytesPerSec\":{(SentBytes / s).ToString("0", inv)},\"inBytesPerSec\":{(ReceivedBytes / s).ToString("0", inv)}," +
               $"\"peakOutBytesPerSec\":{PeakSentBytesPerSecond},\"peakInBytesPerSec\":{PeakReceivedBytesPerSecond},\"peakOutMsgsPerSec\":{PeakSentMessagesPerSecond},\"kinds\":{{{kinds}}}}}";
    }
}
