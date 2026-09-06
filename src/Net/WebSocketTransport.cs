using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// <see cref="INetTransport"/> over Godot's <see cref="WebSocketMultiplayerPeer"/>, used directly as a packet peer (no
/// SceneMultiplayer, no RPC): the server listens on a port, a client dials a ws:// or wss:// URL. WebSocket carries
/// one reliable ordered stream, so both channels go the same way.
/// </summary>
public sealed class WebSocketTransport : INetTransport
{
    private readonly WebSocketMultiplayerPeer _peer = new();
    private MultiplayerPeer.ConnectionStatus _last = MultiplayerPeer.ConnectionStatus.Disconnected;
    private bool _wasConnected;

    public int LocalId => _peer.GetUniqueId();
    public bool IsServer { get; private set; }
    public bool IsConnected => IsServer ? _last != MultiplayerPeer.ConnectionStatus.Disconnected : _last == MultiplayerPeer.ConnectionStatus.Connected;
    public string LastError { get; private set; } = "";

    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;
    public event Action? ConnectedToServer;
    public event Action? ConnectionFailed;
    public event Action? ServerDisconnected;
    public event Action<int, byte[]>? Received;

    public WebSocketTransport()
    {
        _peer.OutboundBufferSize = 8 * 1024 * 1024; // a Welcome for an AI campaign can run to a few hundred KB
        _peer.InboundBufferSize = 8 * 1024 * 1024;
        _peer.MaxQueuedPackets = 4096;
        _peer.PeerConnected += id => { if (IsServer) PeerConnected?.Invoke((int)id); };
        _peer.PeerDisconnected += id => { if (IsServer) PeerDisconnected?.Invoke((int)id); };
    }

    public bool Listen(int port, string bind = "*")
    {
        IsServer = true;
        var err = _peer.CreateServer(port, bind);
        if (err != Error.Ok) { LastError = $"listen on {bind}:{port} failed: {err}"; return false; }
        _last = MultiplayerPeer.ConnectionStatus.Connected;
        return true;
    }

    public bool Dial(string url)
    {
        IsServer = false;
        var tls = url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ? TlsOptions.Client() : null;
        var err = _peer.CreateClient(url, tls);
        if (err != Error.Ok) { LastError = $"connect to {url} failed: {err}"; return false; }
        _last = MultiplayerPeer.ConnectionStatus.Connecting;
        return true;
    }

    public void Send(int peer, byte[] bytes, NetChannel channel = NetChannel.Reliable)
    {
        if (!IsConnected) return;
        if (IsServer && peer != NetPeers.All && _peer.GetPeer(peer) is not { } socket) return;
        if (IsServer && peer != NetPeers.All && _peer.GetPeer(peer).GetReadyState() != WebSocketPeer.State.Open) return;
        _peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        _peer.SetTargetPeer(IsServer ? peer : NetPeers.Server);
        var err = _peer.PutPacket(bytes);
        if (err != Error.Ok) LastError = $"send failed: {err}";
    }

    public void Poll()
    {
        _peer.Poll();
        if (!IsServer)
        {
            var status = _peer.GetConnectionStatus();
            if (status != _last)
            {
                if (status == MultiplayerPeer.ConnectionStatus.Connected) { _wasConnected = true; _last = status; ConnectedToServer?.Invoke(); }
                else if (status == MultiplayerPeer.ConnectionStatus.Disconnected)
                {
                    _last = status;
                    if (_wasConnected) ServerDisconnected?.Invoke(); else ConnectionFailed?.Invoke();
                }
                else _last = status;
            }
        }
        while (_peer.GetAvailablePacketCount() > 0)
        {
            var from = _peer.GetPacketPeer();
            var bytes = _peer.GetPacket();
            Received?.Invoke(from, bytes);
        }
    }

    public void Disconnect(int peer)
    {
        if (IsServer) _peer.DisconnectPeer(peer, false);
    }

    public void Close()
    {
        _peer.Poll(); // push anything still queued before the close frame
        _peer.Close();
        _last = MultiplayerPeer.ConnectionStatus.Disconnected;
    }
}
