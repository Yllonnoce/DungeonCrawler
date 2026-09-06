namespace DungeonCrawler.Net;

/// <summary>
/// A pretend network for tests: one server endpoint, any number of client endpoints, packets delivered on
/// <see cref="INetTransport.Poll"/>. The State channel can drop or reorder packets with a seeded rng.
/// </summary>
public sealed class InMemoryNetwork
{
    private readonly Dictionary<int, InMemoryTransport> _peers = new();
    private int _nextId = NetPeers.Server + 1;
    private readonly Random _rng;

    public float StateDropChance { get; set; }
    public float StateReorderChance { get; set; }
    public InMemoryTransport Server { get; }

    public InMemoryNetwork(int seed = 1)
    {
        _rng = new Random(seed);
        Server = new InMemoryTransport(this, NetPeers.Server, isServer: true);
        _peers[NetPeers.Server] = Server;
    }

    public InMemoryTransport CreateClient()
    {
        var t = new InMemoryTransport(this, _nextId++, isServer: false);
        _peers[t.LocalId] = t;
        return t;
    }

    /// <summary>A client dials in: both sides learn of each other on their next Poll.</summary>
    public void Connect(InMemoryTransport client)
    {
        if (client.IsServer || client.IsConnected) return;
        client.MarkConnected();
        Server.QueueEvent(() => Server.RaisePeerConnected(client.LocalId));
        client.QueueEvent(client.RaiseConnectedToServer);
    }

    public void Disconnect(InMemoryTransport client, bool serverInitiated = false)
    {
        if (client.IsServer || !client.IsConnected) return;
        client.MarkDisconnected();
        Server.QueueEvent(() => Server.RaisePeerDisconnected(client.LocalId));
        if (!serverInitiated) return;
        client.QueueEvent(client.RaiseServerDisconnected);
    }

    internal void Route(int from, int to, byte[] bytes, NetChannel channel)
    {
        if (channel == NetChannel.State && StateDropChance > 0 && _rng.NextDouble() < StateDropChance) return;
        var reorder = channel == NetChannel.State && StateReorderChance > 0 && _rng.NextDouble() < StateReorderChance;
        if (to == NetPeers.All)
        {
            foreach (var p in _peers.Values) if (p.LocalId != from && p.IsConnected) p.QueuePacket(from, bytes, reorder);
            return;
        }
        if (_peers.TryGetValue(to, out var peer) && (peer.IsServer || peer.IsConnected)) peer.QueuePacket(from, bytes, reorder);
    }

    /// <summary>Poll everyone until no packets are in flight (bounded so a ping-pong loop cannot spin forever).</summary>
    public void Settle(int maxRounds = 20)
    {
        for (var i = 0; i < maxRounds; i++)
        {
            var any = false;
            foreach (var p in _peers.Values.ToList()) any |= p.Poll(out _);
            if (!any) return;
        }
    }
}

public sealed class InMemoryTransport : INetTransport
{
    private readonly InMemoryNetwork _net;
    private readonly List<(int from, byte[] bytes)> _inbox = new();
    private readonly List<Action> _events = new();

    public int LocalId { get; }
    public bool IsServer { get; }
    public bool IsConnected { get; private set; }
    public int SentPackets { get; private set; }
    public long SentBytes { get; private set; }

    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;
    public event Action? ConnectedToServer;
    public event Action? ConnectionFailed;
    public event Action? ServerDisconnected;
    public event Action<int, byte[]>? Received;

    internal InMemoryTransport(InMemoryNetwork net, int id, bool isServer)
    {
        _net = net;
        LocalId = id;
        IsServer = isServer;
        IsConnected = isServer;
    }

    public void Send(int peer, byte[] bytes, NetChannel channel = NetChannel.Reliable)
    {
        if (!IsServer && !IsConnected) return;
        SentPackets++;
        SentBytes += bytes.Length;
        _net.Route(LocalId, IsServer ? peer : NetPeers.Server, bytes, channel);
    }

    public void Poll() => Poll(out _);

    internal bool Poll(out int delivered)
    {
        var events = _events.ToList(); _events.Clear();
        foreach (var e in events) e();
        var packets = _inbox.ToList(); _inbox.Clear();
        foreach (var (from, bytes) in packets) Received?.Invoke(from, bytes);
        delivered = packets.Count;
        return events.Count > 0 || packets.Count > 0;
    }

    public void Close()
    {
        if (IsServer) return;
        _net.Disconnect(this);
    }

    internal void QueuePacket(int from, byte[] bytes, bool reorder)
    {
        if (reorder && _inbox.Count > 0) _inbox.Insert(_inbox.Count - 1, (from, bytes));
        else _inbox.Add((from, bytes));
    }

    internal void QueueEvent(Action a) => _events.Add(a);
    internal void MarkConnected() => IsConnected = true;
    internal void MarkDisconnected() => IsConnected = false;
    internal void RaisePeerConnected(int id) => PeerConnected?.Invoke(id);
    internal void RaisePeerDisconnected(int id) => PeerDisconnected?.Invoke(id);
    internal void RaiseConnectedToServer() => ConnectedToServer?.Invoke();
    internal void RaiseServerDisconnected() => ServerDisconnected?.Invoke();
    internal void RaiseConnectionFailed() => ConnectionFailed?.Invoke();
}
