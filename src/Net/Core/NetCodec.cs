using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

public sealed class NetCodecException : Exception
{
    public NetCodecException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Bytes on the wire: one kind byte, then the payload. Pose, WorldSnapshot and Vitals are binary; everything else is
/// JSON with the save layer's options (so the same DTOs and converters apply). Bit 0x80 of the kind byte marks a
/// gzipped JSON payload, used above <see cref="Protocol.CompressThreshold"/>.
/// </summary>
public static class NetCodec
{
    private const byte CompressedBit = 0x80;

    /// <summary>Compact copy of the save options: no indentation on the wire.</summary>
    public static readonly JsonSerializerOptions Json = new(SaveFormat.Options) { WriteIndented = false };

    public static byte[] Encode(NetMessage m)
    {
        var kind = m.Kind;
        switch (m)
        {
            case Pose p: return Binary(kind, w => BinaryLayout.Write(w, p));
            case WorldSnapshot s: return Binary(kind, w => BinaryLayout.Write(w, s));
            case Vitals v: return Binary(kind, w => BinaryLayout.Write(w, v));
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(m, m.GetType(), Json);
        if (json.Length <= Protocol.CompressThreshold)
        {
            var plain = new byte[json.Length + 1];
            plain[0] = (byte)kind;
            json.CopyTo(plain, 1);
            return plain;
        }
        using var ms = new MemoryStream();
        ms.WriteByte((byte)((byte)kind | CompressedBit));
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(json);
        return ms.ToArray();
    }

    /// <summary>The kind of a packet without decoding it (dispatch, stats). Null for an empty or unknown packet.</summary>
    public static MsgKind? PeekKind(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        var kind = (MsgKind)(bytes[0] & ~CompressedBit);
        return MsgRegistry.TypeOf(kind) == null ? null : kind;
    }

    /// <summary>Decode a packet; throws <see cref="NetCodecException"/> for anything malformed so the caller can drop the peer.</summary>
    public static NetMessage Decode(byte[] bytes)
    {
        if (bytes.Length == 0) throw new NetCodecException("empty packet");
        var compressed = (bytes[0] & CompressedBit) != 0;
        var kind = (MsgKind)(bytes[0] & ~CompressedBit);
        var type = MsgRegistry.TypeOf(kind) ?? throw new NetCodecException($"unknown message kind {(int)kind}");
        try
        {
            if (type == typeof(Pose) || type == typeof(WorldSnapshot) || type == typeof(Vitals))
            {
                using var r = new BinaryReader(new MemoryStream(bytes, 1, bytes.Length - 1), Encoding.UTF8);
                if (type == typeof(Pose)) return BinaryLayout.ReadPose(r);
                if (type == typeof(WorldSnapshot)) return BinaryLayout.ReadSnapshot(r);
                return BinaryLayout.ReadVitals(r);
            }
            ReadOnlySpan<byte> payload;
            if (compressed)
            {
                using var gz = new GZipStream(new MemoryStream(bytes, 1, bytes.Length - 1), CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                gz.CopyTo(outMs);
                payload = outMs.ToArray();
            }
            else payload = new ReadOnlySpan<byte>(bytes, 1, bytes.Length - 1);
            return (NetMessage?)JsonSerializer.Deserialize(payload, type, Json) ?? throw new NetCodecException($"null {type.Name}");
        }
        catch (NetCodecException) { throw; }
        catch (Exception e) { throw new NetCodecException($"bad {type.Name} packet: {e.Message}", e); }
    }

    public static T Decode<T>(byte[] bytes) where T : NetMessage
        => Decode(bytes) as T ?? throw new NetCodecException($"expected {typeof(T).Name}");

    private static byte[] Binary(MsgKind kind, Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)kind);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) write(w);
        return ms.ToArray();
    }
}
