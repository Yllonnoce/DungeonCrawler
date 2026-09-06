using DungeonCrawler.Adventure;
using DungeonCrawler.Net;
using DungeonCrawler.Save;
using Xunit;

namespace DungeonCrawler.Tests.Net;

/// <summary>A minimal server host and client host wired over the in-memory network, the way NetSession will wire the real ones.</summary>
public class HandshakeTests
{
    private sealed class FakeServer
    {
        public readonly InMemoryNetwork Net = new(seed: 3);
        public readonly PlayerRegistry Registry = new();
        public readonly ServerHandshake Handshake;
        public readonly List<string> Members = new();
        public readonly List<string> Events = new();
        public readonly List<DynamicEnemyDto> Dynamic = new();
        public int Floor = 1;

        public FakeServer()
        {
            Handshake = new ServerHandshake(Registry)
            {
                ClassExists = id => id.StartsWith("class."),
                CreateMember = (name, cls) => { Members.Add($"{name}:{cls}"); return Members.Count - 1; },
                BuildWelcome = (slot, peer) => new Welcome(peer, slot.MemberIndex, 42UL, Floor, 0, false, new AdventureDto { Mode = AdventureMode.Builtin }, PartySnapshot.Expected(Floor),
                    new FloorStateDto { Visited = true }, Dynamic.ToList(), new PartyDto(), Registry.ToDtos(), "Objective", 100),
                PlayerReady = s => Events.Add($"ready:{s.Name}"),
                PlayerLeft = s => Events.Add($"left:{s.Name}"),
                NowUtc = () => "now",
                ParkedPoseOf = s => (new PoseDto { X = s.MemberIndex }, Floor),
            };
            var t = Net.Server;
            t.PeerConnected += p => Handshake.OnPeerConnected(p);
            t.PeerDisconnected += p => Handshake.OnPeerDisconnected(p);
            t.Received += (from, bytes) =>
            {
                NetMessage m;
                try { m = NetCodec.Decode(bytes); }
                catch (NetCodecException) { Events.Add($"garbage:{from}"); return; }
                if (Handshake.InGame(from)) { Events.Add($"game:{from}:{m.Kind}"); return; }
                var reply = Handshake.OnMessage(from, m, out var drop);
                if (reply != null) t.Send(from, NetCodec.Encode(reply));
                if (drop) Events.Add($"drop:{from}");
            };
        }
    }

    private sealed class FakeClient
    {
        public readonly InMemoryTransport T;
        public readonly ClientHandshake H;
        public readonly List<NetMessage> InGame = new();
        public bool AutoReady = true;

        public FakeClient(InMemoryNetwork net, string name, string cls)
        {
            T = net.CreateClient();
            H = new ClientHandshake(name, cls, "test");
            T.ConnectedToServer += () => T.Send(NetPeers.Server, NetCodec.Encode(H.OnConnected()));
            T.ServerDisconnected += H.OnServerDisconnected;
            T.Received += (_, bytes) =>
            {
                var m = NetCodec.Decode(bytes);
                if (H.State == ClientHandshake.Phase.InGame) { InGame.Add(m); return; }
                if (H.OnMessage(m) && H.State == ClientHandshake.Phase.Welcomed && AutoReady) T.Send(NetPeers.Server, NetCodec.Encode(H.OnFloorBuilt()));
            };
            H.Connecting();
            net.Connect(T);
        }
    }

    [Fact]
    public void Join_Welcome_Ready()
    {
        var s = new FakeServer();
        var c = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        Assert.Equal(ClientHandshake.Phase.InGame, c.H.State);
        Assert.Equal(0, c.H.Welcome!.YourMemberIndex);
        Assert.Equal(c.T.LocalId, c.H.Welcome.YourPeerId);
        Assert.Equal(42UL, c.H.Welcome.GameSeed);
        Assert.Equal(new[] { "Eric:class.rogue" }, s.Members);
        Assert.Equal(new[] { "ready:Eric" }, s.Events);
        Assert.True(s.Handshake.InGame(c.T.LocalId));
    }

    [Fact]
    public void WrongProtocol_IsRejectedAndDropped()
    {
        var s = new FakeServer();
        var t = s.Net.CreateClient();
        Reject? reject = null;
        t.Received += (_, b) => reject = NetCodec.Decode(b) as Reject;
        t.ConnectedToServer += () => t.Send(NetPeers.Server, NetCodec.Encode(new Hello(Protocol.Version + 1, "Eric", "class.rogue", "")));
        s.Net.Connect(t);
        s.Net.Settle();
        Assert.NotNull(reject);
        Assert.Contains("version", reject!.Reason);
        Assert.Contains($"drop:{t.LocalId}", s.Events);
        Assert.Empty(s.Members);
    }

    [Fact]
    public void DuplicateName_Rejected_SecondJoinerGetsNextMember()
    {
        var s = new FakeServer();
        var a = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        var dup = new FakeClient(s.Net, "eric", "class.mage");
        s.Net.Settle();
        Assert.Equal(ClientHandshake.Phase.Rejected, dup.H.State);
        Assert.Contains("already playing", dup.H.Reason);
        var b = new FakeClient(s.Net, "Sam", "class.mage");
        s.Net.Settle();
        Assert.Equal(1, b.H.Welcome!.YourMemberIndex);
        Assert.Equal(2, b.H.Welcome.Players.Count);
        Assert.Equal(2, s.Registry.OnlineCount);
        Assert.Equal(ClientHandshake.Phase.InGame, a.H.State);
    }

    [Fact]
    public void MessageBeforeHello_DropsPeer()
    {
        var s = new FakeServer();
        var t = s.Net.CreateClient();
        t.ConnectedToServer += () => t.Send(NetPeers.Server, NetCodec.Encode(new ChatIntent("hi")));
        s.Net.Connect(t);
        s.Net.Settle();
        Assert.Contains($"drop:{t.LocalId}", s.Events);
        Assert.Null(s.Registry.ByPeer(t.LocalId));
    }

    [Fact]
    public void Garbage_IsReportedNotFatal()
    {
        var s = new FakeServer();
        var c = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        c.T.Send(NetPeers.Server, new byte[] { 250, 0 });
        s.Net.Settle();
        Assert.Contains($"garbage:{c.T.LocalId}", s.Events);
        Assert.True(s.Handshake.InGame(c.T.LocalId));
    }

    [Fact]
    public void Leave_ParksAndRejoinGetsSameMember()
    {
        var s = new FakeServer();
        var c = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        s.Floor = 2;
        c.T.Close();
        s.Net.Settle();
        Assert.Contains("left:Eric", s.Events);
        var slot = s.Registry.ByName("Eric")!;
        Assert.False(slot.Online);
        Assert.Equal(2, slot.ParkedFloor);
        Assert.Equal(0f, slot.ParkedPose!.X);
        Assert.False(s.Handshake.InGame(c.T.LocalId));

        var again = new FakeClient(s.Net, "ERIC", "class.warrior");
        s.Net.Settle();
        Assert.Equal(ClientHandshake.Phase.InGame, again.H.State);
        Assert.Equal(0, again.H.Welcome!.YourMemberIndex);
        Assert.Single(s.Members);
        Assert.Equal("class.rogue", s.Registry.ByName("eric")!.ClassId);
    }

    [Fact]
    public void MidHandshakeDisconnect_LeavesNoGhost()
    {
        var s = new FakeServer();
        var c = new FakeClient(s.Net, "Eric", "class.rogue") { AutoReady = false };
        s.Net.Settle();
        Assert.Equal(ClientHandshake.Phase.Welcomed, c.H.State);
        Assert.Equal(ServerHandshake.PeerPhase.Welcomed, s.Handshake.PhaseOf(c.T.LocalId));
        c.T.Close();
        s.Net.Settle();
        Assert.Null(s.Handshake.PhaseOf(c.T.LocalId));
        Assert.False(s.Registry.ByName("Eric")!.Online);
        Assert.DoesNotContain("ready:Eric", s.Events);
        Assert.Contains("left:Eric", s.Events);
    }

    [Fact]
    public void JoinInProgress_WelcomeCarriesDynamicEnemies_AndInGameTrafficRoutes()
    {
        var s = new FakeServer();
        s.Dynamic.Add(new DynamicEnemyDto(200003, "enemy.horde_husk", 1, 0, 1, 0, 5, "h1"));
        var c = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        Assert.Single(c.H.Welcome!.DynamicEnemies);
        c.T.Send(NetPeers.Server, NetCodec.Encode(new Pose(1, 0, NetVec.Zero, 0, 0, NetVec.Zero, PoseFlags.OnFloor)), NetChannel.State);
        s.Net.Settle();
        Assert.Contains($"game:{c.T.LocalId}:Pose", s.Events);
        s.Net.Server.Send(NetPeers.All, NetCodec.Encode(new ChatMsg("Sam", "hello")));
        s.Net.Settle();
        Assert.Contains(c.InGame, m => m is ChatMsg { Text: "hello" });
    }

    [Fact]
    public void ServerKick_IsReportedToClient()
    {
        var s = new FakeServer();
        var c = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        s.Net.Disconnect(c.T, serverInitiated: true);
        s.Net.Settle();
        Assert.Equal(ClientHandshake.Phase.Failed, c.H.State);
        Assert.NotNull(c.H.Reason);
    }

    [Fact]
    public void StateChannel_CanDropAndReorder()
    {
        var s = new FakeServer { Net = { StateDropChance = 0.5f } };
        var c = new FakeClient(s.Net, "Eric", "class.rogue");
        s.Net.Settle();
        for (var i = 0; i < 40; i++) c.T.Send(NetPeers.Server, NetCodec.Encode(new Pose(i, 0, NetVec.Zero, 0, 0, NetVec.Zero, 0)), NetChannel.State);
        s.Net.Settle();
        var poses = s.Events.Count(e => e.EndsWith(":Pose"));
        Assert.InRange(poses, 5, 35);
        Assert.Equal(ClientHandshake.Phase.InGame, c.H.State);
    }
}
