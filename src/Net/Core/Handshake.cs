namespace DungeonCrawler.Net;

/// <summary>
/// The join sequence as pure state machines. Server: a connected peer must send <see cref="Hello"/> first and gets
/// <see cref="Reject"/> or <see cref="Welcome"/>; after <see cref="Ready"/> it is in the game. Client: connect, send
/// Hello, wait for Welcome, build the floor, send Ready. The host supplies the world-facing callbacks.
/// </summary>
public sealed class ServerHandshake
{
    public enum PeerPhase { Connected, Welcomed, InGame }

    private readonly Dictionary<int, PeerPhase> _peers = new();

    public PlayerRegistry Registry { get; }
    public string Build { get; set; } = "";
    /// <summary>Does this class id exist.</summary>
    public Func<string, bool> ClassExists { get; set; } = _ => true;
    /// <summary>Make a party member for a new name; returns its index.</summary>
    public Func<string, string, int> CreateMember { get; set; } = (_, _) => 0;
    /// <summary>Build the welcome for a claimed slot (the host captures the live world).</summary>
    public Func<PlayerSlot, int, Welcome> BuildWelcome { get; set; } = (_, _) => throw new InvalidOperationException("BuildWelcome not set");
    /// <summary>Called once the peer has built the floor and is in the world.</summary>
    public Action<PlayerSlot>? PlayerReady { get; set; }
    /// <summary>Called when an in-game or welcomed peer disconnects (before its slot is parked).</summary>
    public Action<PlayerSlot>? PlayerLeft { get; set; }
    public Func<string> NowUtc { get; set; } = () => DateTime.UtcNow.ToString("o");
    public Func<PlayerSlot, (Save.PoseDto? pose, int floor)> ParkedPoseOf { get; set; } = _ => (null, 0);

    public ServerHandshake(PlayerRegistry registry) => Registry = registry;

    public PeerPhase? PhaseOf(int peer) => _peers.TryGetValue(peer, out var p) ? p : null;
    public bool InGame(int peer) => PhaseOf(peer) == PeerPhase.InGame;
    public IEnumerable<int> InGamePeers => _peers.Where(p => p.Value == PeerPhase.InGame).Select(p => p.Key);

    public void OnPeerConnected(int peer) => _peers[peer] = PeerPhase.Connected;

    /// <summary>A packet from a peer that is not in the game yet. Returns what to send back (null = nothing, keep waiting).</summary>
    public NetMessage? OnMessage(int peer, NetMessage m, out bool dropPeer)
    {
        dropPeer = false;
        if (!_peers.TryGetValue(peer, out var phase)) { dropPeer = true; return null; }
        switch (phase)
        {
            case PeerPhase.Connected when m is Hello hello:
                if (hello.Protocol != Protocol.Version) { dropPeer = true; return new Reject(PlayerRegistry.ReasonText(ClaimResult.BadProtocol)); }
                var result = Registry.Claim(peer, hello.PlayerName, hello.ClassId, ClassExists, CreateMember, out var slot);
                if (result != ClaimResult.Ok || slot == null) { dropPeer = true; return new Reject(PlayerRegistry.ReasonText(result)); }
                _peers[peer] = PeerPhase.Welcomed;
                return BuildWelcome(slot, peer);
            case PeerPhase.Welcomed when m is Ready:
                var s = Registry.ByPeer(peer);
                if (s == null) { dropPeer = true; return null; }
                _peers[peer] = PeerPhase.InGame;
                s.Ready = true;
                PlayerReady?.Invoke(s);
                return null;
            case PeerPhase.Connected:
            case PeerPhase.Welcomed:
                dropPeer = true; // anything else before Ready is a protocol violation
                return null;
            default:
                return null;
        }
    }

    public void OnPeerDisconnected(int peer)
    {
        _peers.Remove(peer);
        var slot = Registry.ByPeer(peer);
        if (slot == null) return;
        PlayerLeft?.Invoke(slot);
        var (pose, floor) = ParkedPoseOf(slot);
        Registry.Leave(peer, pose, floor, NowUtc());
    }
}

public sealed class ClientHandshake
{
    public enum Phase { Idle, Connecting, HelloSent, Welcomed, InGame, Rejected, Failed }

    public Phase State { get; private set; } = Phase.Idle;
    public string PlayerName { get; }
    public string ClassId { get; }
    public string Build { get; }
    public Welcome? Welcome { get; private set; }
    public string? Reason { get; private set; }

    public ClientHandshake(string playerName, string classId, string build = "")
    {
        PlayerName = playerName; ClassId = classId; Build = build;
    }

    public void Connecting() => State = Phase.Connecting;

    /// <summary>Connected: the Hello to send.</summary>
    public Hello OnConnected()
    {
        State = Phase.HelloSent;
        return new Hello(Protocol.Version, PlayerName, ClassId, Build);
    }

    public void OnConnectionFailed() { State = Phase.Failed; Reason = "Could not reach the server."; }
    public void OnServerDisconnected() { if (State != Phase.Rejected) { State = Phase.Failed; Reason ??= "The server closed the connection."; } }

    /// <summary>A packet before we are in the game. True when it advanced the handshake.</summary>
    public bool OnMessage(NetMessage m)
    {
        switch (State)
        {
            case Phase.HelloSent when m is Welcome w:
                Welcome = w; State = Phase.Welcomed; return true;
            case Phase.HelloSent when m is Reject r:
                Reason = r.Reason; State = Phase.Rejected; return true;
            default:
                return false;
        }
    }

    /// <summary>The floor is built: the Ready to send.</summary>
    public Ready OnFloorBuilt()
    {
        if (State != Phase.Welcomed || Welcome == null) throw new InvalidOperationException($"not welcomed ({State})");
        State = Phase.InGame;
        return new Ready(Welcome.CurrentFloor);
    }
}
