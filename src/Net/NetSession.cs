using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Net;

public enum NetRole { Single, Server, Client }

/// <summary>
/// Autoload that owns the transport and the role. Single-player never touches it. A dedicated server is started from
/// the command line (<c>--server</c>); a client connects from the title screen or <c>--join=</c>. Everything that
/// crosses the wire goes through <see cref="ServerWorld"/> or <see cref="ClientWorld"/>.
/// </summary>
public partial class NetSession : Node
{
    public static NetSession Instance { get; private set; } = null!;
    public static bool IsServer => Instance?.Role == NetRole.Server;
    public static bool IsClient => Instance?.Role == NetRole.Client;
    public static bool IsNetworked => Instance != null && Instance.Role != NetRole.Single;

    public NetRole Role { get; private set; } = NetRole.Single;
    public INetTransport? Transport { get; private set; }
    public ServerWorld? Server { get; private set; }
    public ClientWorld? Client { get; private set; }
    public int Tick { get; private set; }
    public double Now => Time.GetTicksMsec() / 1000.0;
    public string Status { get; private set; } = "";
    public event Action<string>? StatusChanged;

    // command-line requests
    public bool ServerRequested { get; private set; }
    public int Port { get; private set; } = Protocol.DefaultPort;
    public string Bind { get; private set; } = "*";
    public string Campaign { get; private set; } = "offline";
    public ulong? Seed { get; private set; }
    public string SaveSlot { get; private set; } = "server";
    public int MaxPlayers { get; private set; } = Protocol.MaxPlayers;
    public bool QuitWhenEmpty { get; private set; }
    public string? StatusFile { get; private set; }
    public float AutosaveSeconds { get; private set; } = 60f;
    public string? JoinUrl { get; private set; }
    public string JoinName { get; private set; } = "";
    public string JoinClass { get; private set; } = "class.warrior";
    public bool TestServer { get; private set; }
    public bool TestClient { get; private set; }
    /// <summary>The short client check: join a resumed server, confirm the character came back, leave.</summary>
    public bool TestRejoinOnly { get; private set; }
    /// <summary>The server resumed a save at start (the scripted check expects a rejoin, not a fresh join).</summary>
    public bool Resumed { get; set; }

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--server") ServerRequested = true;
            else if (arg.StartsWith("--port=")) Port = int.Parse(arg["--port=".Length..]);
            else if (arg.StartsWith("--bind=")) Bind = arg["--bind=".Length..];
            else if (arg.StartsWith("--campaign=")) Campaign = arg["--campaign=".Length..];
            else if (arg.StartsWith("--seed=")) Seed = ulong.Parse(arg["--seed=".Length..]);
            else if (arg.StartsWith("--save=")) SaveSlot = arg["--save=".Length..];
            else if (arg.StartsWith("--max-players=")) MaxPlayers = Math.Clamp(int.Parse(arg["--max-players=".Length..]), 1, 8);
            else if (arg == "--quit-when-empty") QuitWhenEmpty = true;
            else if (arg.StartsWith("--status-file=")) StatusFile = arg["--status-file=".Length..];
            else if (arg.StartsWith("--autosave=")) AutosaveSeconds = float.Parse(arg["--autosave=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--join=")) JoinUrl = arg["--join=".Length..];
            else if (arg.StartsWith("--name=")) JoinName = arg["--name=".Length..];
            else if (arg.StartsWith("--class=")) JoinClass = arg["--class=".Length..];
            else if (arg == "--test-net-server") { TestServer = true; ServerRequested = true; }
            else if (arg == "--test-net-client") TestClient = true;
            else if (arg == "--test-net-rejoin") { TestClient = true; TestRejoinOnly = true; }
        }
        if (ServerRequested || JoinUrl != null) CallDeferred(nameof(AutoStart));
    }

    private void AutoStart()
    {
        if (Main.Instance == null) { CallDeferred(nameof(AutoStart)); return; }
        if (ServerRequested) StartServer();
        else if (JoinUrl != null) Connect(JoinUrl, JoinName == "" ? "Player" : JoinName, JoinClass);
    }

    public void SetStatus(string text)
    {
        Status = text;
        GD.Print($"[net] {text}");
        StatusChanged?.Invoke(text);
    }

    // ---- server ------------------------------------------------------------------------------

    public bool StartServer()
    {
        if (Role != NetRole.Single) return false;
        var t = new WebSocketTransport();
        // the previous instance may still be letting go of the port (a restart, a redeploy): keep trying for a while
        var attempt = 0;
        while (!t.Listen(Port, Bind))
        {
            if (++attempt >= 30) { SetStatus(t.LastError); return false; }
            GD.Print($"[net] {t.LastError}; retrying ({attempt}/30)");
            OS.DelayMsec(1000);
            t = new WebSocketTransport();
        }
        Transport = t;
        Role = NetRole.Server;
        Engine.MaxFps = 60;
        Server = new ServerWorld { Name = "ServerWorld" };
        AddChild(Server);
        Server.Start(t);
        if (TestServer) AddChild(new NetTestFlow { Name = "TestFlow", ServerSide = true });
        SetStatus($"server listening on {Bind}:{Port} ({Campaign}, max {MaxPlayers})");
        Main.Instance.StartServerSession();
        return true;
    }

    // ---- client ------------------------------------------------------------------------------

    public bool Connect(string url, string playerName, string classId)
    {
        if (Role != NetRole.Single) Disconnect();
        var t = new WebSocketTransport();
        if (!t.Dial(url)) { SetStatus(t.LastError); return false; }
        Transport = t;
        Role = NetRole.Client;
        Client = new ClientWorld { Name = "ClientWorld" };
        AddChild(Client);
        Client.Start(t, playerName, classId);
        if (TestClient) AddChild(new NetTestFlow { Name = "TestFlow" });
        SetStatus($"connecting to {url}...");
        return true;
    }

    /// <summary>Leave the server (or stop it) and go back to single-player mode. The caller decides what the screen shows.</summary>
    public void Disconnect(string? reason = null)
    {
        if (Role == NetRole.Single) return;
        Transport?.Close();
        Transport = null;
        if (Server != null) { Server.QueueFree(); Server = null; }
        if (Client != null) { Client.QueueFree(); Client = null; }
        Role = NetRole.Single;
        SetStatus(reason ?? "disconnected");
    }

    public void Send(NetMessage m, NetChannel channel = NetChannel.Reliable) => Transport?.Send(NetPeers.Server, NetCodec.Encode(m), channel);

    public override void _Process(double delta) => Transport?.Poll();

    public override void _PhysicsProcess(double delta)
    {
        if (Role == NetRole.Single) return;
        Tick++;
    }
}
