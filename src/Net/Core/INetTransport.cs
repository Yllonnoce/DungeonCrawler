namespace DungeonCrawler.Net;

/// <summary>
/// The thin layer the session talks to: peers by integer id, raw byte packets, events pumped from <see cref="Poll"/>.
/// The server is peer 1 (Godot's convention); clients learn their id when connected. Sending to peer 0 broadcasts.
/// </summary>
public interface INetTransport
{
    int LocalId { get; }
    bool IsServer { get; }
    bool IsConnected { get; }
    event Action<int>? PeerConnected;
    event Action<int>? PeerDisconnected;
    event Action? ConnectedToServer;
    event Action? ConnectionFailed;
    event Action? ServerDisconnected;
    /// <summary>(sender peer id, packet).</summary>
    event Action<int, byte[]>? Received;
    void Send(int peer, byte[] bytes, NetChannel channel = NetChannel.Reliable);
    /// <summary>Deliver queued packets and raise events. Called once per frame.</summary>
    void Poll();
    void Close();
}

public static class NetPeers
{
    public const int Server = 1;
    public const int All = 0;
}
