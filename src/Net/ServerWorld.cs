using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The dedicated server's view of the game: who is connected, their bodies, what to tell them. Runs the handshake,
/// applies player poses and intents to the world, and streams snapshots, vitals and events back.
/// </summary>
public partial class ServerWorld : Node
{
    public PlayerRegistry Registry { get; } = new();
    public ServerHandshake Handshake { get; private set; } = null!;
    private INetTransport _t = null!;
    private readonly List<string> _logBatch = new();
    private readonly Dictionary<int, Vector3> _lastSent = new();
    /// <summary>The flags each enemy was last sent with, by peer (plan 15 Task 8: a change goes out at once, still or not; <see cref="SnapshotRules.SendEnemy"/>).</summary>
    private readonly Dictionary<(int Peer, int NetId), byte> _flagsSent = new();
    private float _fullSnapshotTimer;
    private float _autosaveTimer;
    private float _statusTimer;
    /// <summary>What the scripted server test saw, in order.</summary>
    public List<string> Trace { get; } = new();
    public event Action<PlayerSlot>? PlayerReady;
    public event Action<PlayerSlot>? PlayerLeft;
    public event Action<int, NetMessage>? IntentReceived;

    public void Start(INetTransport t)
    {
        _t = t;
        Registry.MaxOnline = NetSession.Instance.MaxPlayers;
        Handshake = new ServerHandshake(Registry)
        {
            Build = Protocol.Version.ToString(),
            ClassExists = id => Core.Registry.Instance.TryGet<Characters.Defs.ClassDef>(id, out _),
            CreateMember = (name, cls) => Main.Instance.Party.AddMember(name, cls),
            BuildWelcome = BuildWelcome,
            PlayerReady = OnPlayerReady,
            PlayerLeft = OnPlayerLeft,
            ParkedPoseOf = slot => (PoseOf(slot.MemberIndex), Game.Instance.CurrentFloor),
        };
        t.PeerConnected += p => { Handshake.OnPeerConnected(p); GD.Print($"[net-server] peer {p} connected"); };
        t.PeerDisconnected += p => { _joinQueue.Remove(p); _joinQueueOverflowed.Remove(p); _welcomeEpoch.Remove(p); ForgetResyncLimits(p); Handshake.OnPeerDisconnected(p); GD.Print($"[net-server] peer {p} disconnected"); if (NetSession.Instance.QuitWhenEmpty && Registry.OnlineCount == 0) CallDeferred(nameof(QuitEmpty)); };
        t.Received += OnReceived;
        StartBots();
        var bus = EventBus.Instance;
        bus.Message += line => _logBatch.Add(line);
        bus.Sfx += cue => { if (!LocalCues.Contains(cue) && !Quiet) Broadcast(new SfxMsg(cue)); };
        bus.ObjectiveChanged += text => Broadcast(new ObjectiveMsg(text));
        // no MemberChanged hook: it fires on every swing and hit; FlushSync compares each sheet's content instead (MemberSync)
        bus.LootSpawned += drop => { if (!Quiet) Broadcast(new LootSpawned(SaveMapper.ToDto(drop.Item), Vec(drop.GlobalPosition), drop.KeyIndex, drop.QuestId, drop.Coins)); };
        bus.LootRemoved += (id, by) => { if (!Quiet) Broadcast(new LootRemoved(id, by)); };
        bus.PropChanged += (r, what) => { if (!Quiet) Broadcast(new PropEvt(r, what)); };
        bus.TrapPlaced += trap => { if (!Quiet) Broadcast(new TrapPlacedEvt(new PlacedTrapDto { Id = trap.SpawnIndex, Kind = trap.Kind.ToString(), X = trap.GlobalPosition.X, Y = trap.GlobalPosition.Y, Z = trap.GlobalPosition.Z })); };
        bus.HitFx += (kind, pos, dir, enemy) => { if (!Quiet) Broadcast(HitFxMessage(kind, pos, dir, enemy)); };
        bus.EnemyHurt += (e, amount, flavor, by) => { if (!Quiet) Broadcast(new EnemyHurt(e.NetId, amount, (byte)flavor, e.Hp, by)); };
        bus.EnemyTelegraph += (e, state) => { if (!Quiet && !e.IsHorde) Broadcast(new EnemyStateMsg(e.NetId, state)); }; // hordes wind up from the snapshot instead
        bus.EnemyDied += e => { if (!Quiet) { Broadcast(new EnemyDied(e.NetId, -1)); Trace.Add("kill"); } };
        bus.ProjectileFired += p => { if (!Quiet) Broadcast(new ProjectileSpawned(0, Vec(p.GlobalPosition), Vec(p.Velocity), p.IsArrow, p.Color.ToRgba32(), p.SourceMember)); };
        bus.MemberDowned += i => Broadcast(new MemberDownedEvt(i));
        bus.MemberRevived += i => { _mangledSince.Remove(i); _bound.Remove(i); Broadcast(new MemberRevivedEvt(i)); };
        bus.LevelUp += i => Broadcast(new LevelUpEvt(i));
        bus.MemberKill += m => { var idx = Array.IndexOf(Main.Instance.Party.Members, m); if (idx >= 0) Broadcast(new EmoteEvt(idx, "victory")); };
        bus.PartyWiped += () => Broadcast(new PartyWipedEvt());
        bus.BossDefeated += floor => Broadcast(new BossDefeatedEvt(floor));
        bus.ObjectiveCompleted += (floor, kind) => Broadcast(new ObjectiveDoneEvt(floor, Dungeon.Generation.FloorObjectives.Id(kind)));
        bus.ItemPickedUp += _ => _bagDirty = true;
        bus.FloorChanged += (floor, delta) => OnFloorChangeBegin(floor);
        bus.FloorBuilt += OnFloorBuilt;
        bus.QuestChanged += q => Broadcast(new QuestSync(q));
        bus.NarrationEx += (text, voice, clean, cleanVoice) => { var i = _logBatch.LastIndexOf(text); if (i >= 0) _logBatch.RemoveAt(i); Broadcast(new NarrationMsg(text, voice, clean, cleanVoice)); };
        bus.Bark += (member, line) => { var idx = Array.FindIndex(Main.Instance.Party.Members, m => m.Name == member); Broadcast(new BarkMsg(idx, line.Text, line.VoiceId)); };
        bus.EnemySpawned += e => { if (!Quiet) _spawned.Add(e); };
        bus.HordeTriggered += id => Broadcast(new HordeTriggeredEvt(id));
        bus.HordeSpent += id => Broadcast(new HordeSpentEvt(id));
        bus.AmbushSprung += i => Broadcast(new AmbushSprungEvt(i));
        bus.HazardPhase += OnHazardPhase;
        bus.PortalOpened += at => Broadcast(new PortalOpenedEvt(at.X, at.Y, at.Z));
        bus.RemoteShake += OnRemoteShake;
    }

    /// <summary>A remote player's body was shaken here (a hammer, a slam, a landing): only that player's screen is told; it never shakes a headset.</summary>
    private void OnRemoteShake(int member, float amplitude, float seconds)
    {
        if (Quiet) return;
        foreach (var slot in Registry.Online)
            if (slot.MemberIndex == member && slot.BotOwner == null) SendTo(slot.PeerId, new ShakeEvt(amplitude, seconds));
    }

    /// <summary>Each member's round trip as its own client last reckoned it (its pings carry it): a seated thrower's lag allowance.</summary>
    private readonly Dictionary<int, double> _rttOf = new();

    private readonly List<Enemy> _spawned = new();
    private readonly HashSet<int> _plansSent = new();

    /// <summary>The floor is about to change: everyone waits behind their loading screen and their poses stop counting.</summary>
    private void OnFloorChangeBegin(int floor)
    {
        if (Main.Instance?.Party is not { } party) return;
        foreach (var s in Registry.Online)
        {
            s.Ready = false;
            if (s.MemberIndex < party.Adventurers.Length) party.Adventurers[s.MemberIndex].Remote = null;
        }
        _floorChangeBegin = new FloorChangeBegin(floor, floor > Game.Instance.CurrentFloor ? "Descending..." : "The dungeon shifts...");
        Broadcast(_floorChangeBegin);
        _floorChangeStarted = Time.GetTicksMsec() / 1000.0;
        _floorChanging = true;
    }

    private double _floorChangeStarted;
    /// <summary>The change under way, said again to a client whose floor watchdog asks while we still build or wait for a plan.</summary>
    private FloorChangeBegin? _floorChangeBegin;
    /// <summary>Between FloorChangeBegin and the new floor's FloorChange: a joiner's Ready must not count its poses yet.</summary>
    private bool _floorChanging;

    private void OnFloorBuilt(int floor, int delta, bool firstVisit)
    {
        if (NetSession.Instance.TestServer) GD.Print($"[net-server] floor {floor} built {(_floorChangeStarted > 0 ? $"{Time.GetTicksMsec() / 1000.0 - _floorChangeStarted:0.00} s after the change began" : "")}");
        var main = Main.Instance;
        var game = Game.Instance;
        var party = main.Party;
        var poses = party.Adventurers.Select(a => new PoseDto { X = a.GlobalPosition.X, Y = a.GlobalPosition.Y, Z = a.GlobalPosition.Z, Yaw = a.Rotation.Y }).ToList();
        for (var i = 0; i < poses.Count; i++) _spawnSent[i] = new Vector3(poses[i].X, poses[i].Y, poses[i].Z);
        game.Floors.TryGetValue(floor, out var state);
        _floorEpoch++; // a new build, even of the same floor number (a wipe's reload): checks about the old one no longer apply
        _floorDelta = delta;
        _floorBuiltAt = Time.GetTicksMsec() / 1000.0;
        var change = NetCodec.Encode(FloorChangeOf(floor, delta, state, poses, firstVisit, resync: false));
        foreach (var peer in Handshake.InGamePeers) _t.Send(peer, change);
        foreach (var peer in Handshake.WelcomedPeers) HoldForJoiner(peer, change); // a joiner still building the old floor follows on Ready
        _floorChanging = false;
        _lastSent.Clear();
        _flagsSent.Clear();
        _firstPose.Clear();
        Trace.Add($"floor:{floor}");
        WriteStatus();
        CallDeferred(nameof(AutosaveFloor));
    }

    /// <summary>The server's count of floor builds (every <see cref="OnFloorBuilt"/>): carried by the Welcome, FloorChange and the checks so a client never applies a check about one build to another.</summary>
    private int _floorEpoch = 1;
    public int FloorEpoch => _floorEpoch;
    private int _floorDelta;
    private double _floorBuiltAt;
    /// <summary>The floor build each joining peer's Welcome described (until its Ready): a rebuild in between makes it follow like a floor change.</summary>
    private readonly Dictionary<int, int> _welcomeEpoch = new();
    /// <summary>
    /// Where each member's client was told it stands on the floor it builds (the FloorChange's poses, taken as the floor is built), until
    /// its Ready: a body the server moved since (a wipe's rise beside the cart) is put there by the floor-ready ForcePose, and the poses its
    /// client sent from the spot it was told, before that ForcePose landed, are dropped (the shove guard), not applied as a step back there.
    /// </summary>
    private readonly Dictionary<int, Vector3> _spawnSent = new();

    /// <summary>
    /// The FloorChange for the floor on the server now: the one every peer gets when it is built, or (resync) the answer to
    /// one client's <see cref="FloorResyncIntent"/>, carrying the floor as it stands (a live capture, the bodies where they are).
    /// </summary>
    private FloorChange FloorChangeOf(int floor, int delta, FloorStateDto? state, List<PoseDto> poses, bool firstVisit, bool resync)
    {
        var main = Main.Instance;
        var game = Game.Instance;
        game.Adventure.FloorPlans.TryGetValue(floor, out var plan);
        var snapshot = main.BuildSnapshotFor.TryGetValue(floor, out var s) ? s : main.Party.Snapshot();
        var elites = main.Builder.FloorRoot?.GetChildren().OfType<Enemy>().Where(e => e.SpawnIndex >= 0 && e.IsElite).OrderBy(e => e.SpawnIndex).ToList() ?? new List<Enemy>();
        if (NetSession.Instance.TestServer) GD.Print($"[net-server] elites on floor {floor}: [{string.Join(", ", elites.Select(e => $"{e.SpawnIndex}:{e.DisplayName}"))}]");
        return new FloorChange(floor, delta, plan, snapshot, state, poses, DynamicEnemies(main.Builder.FloorRoot), firstVisit, main.Builder.Layout?.ContentHash() ?? 0, FloorObjectiveId(main),
            elites.Select(e => e.SpawnIndex).ToList(), _floorEpoch, resync, main.Builder.FloorStampNow, main.Builder.FloorSeconds); // the pair as the builder stamped it (FloorClockNow)
    }

    /// <summary>
    /// The floor clock's pair now: the floor's seconds (the sum of its physics steps) at the server's time the builder stamped with them.
    /// This node ticks before the builder (an autoload's child), so <c>NetSession.Instance.Now</c> here would pair last step's floor time
    /// with this step's clock and put every client's floor a step behind.
    /// </summary>
    private FloorClockMsg FloorClockNow() => new(Game.Instance.CurrentFloor, _floorEpoch, Main.Instance.Builder.FloorSeconds, Main.Instance.Builder.FloorStampNow);

    /// <summary>A peer finished building the floor everyone moved to: its poses count again and it gets a full picture.</summary>
    private void OnFloorReady(PlayerSlot slot, int floor)
    {
        if (floor != Game.Instance.CurrentFloor) return;
        slot.Ready = true;
        var party = Main.Instance.Party;
        if (slot.MemberIndex < party.Adventurers.Length)
        {
            var body = party.Adventurers[slot.MemberIndex];
            SendTo(slot.PeerId, new ForcePose(slot.MemberIndex, new PoseDto { X = body.GlobalPosition.X, Y = body.GlobalPosition.Y, Z = body.GlobalPosition.Z, Yaw = body.Rotation.Y }));
            if (_spawnSent.Remove(slot.MemberIndex, out var told) && body.Control == ControlKind.Remote && told.DistanceTo(body.GlobalPosition) > 0.5f && !body.Seated)
                NoteShoved(slot.MemberIndex, told, body.GlobalPosition); // its poses in flight from where the FloorChange put it would pull the body back there
        }
        SendSnapshotTo(slot.PeerId, full: true);
        foreach (var q in AdventureDirector.Instance.Quests.Values) SendTo(slot.PeerId, new QuestSync(q));
        SendTo(slot.PeerId, new ObjectiveMsg(AdventureDirector.Instance.CurrentObjective));
        SendShopSync(slot.PeerId); // a floor change: the floor is built on both ends now (a join is sent in OnPlayerReady)
        if (Adventure.Objectives.ObjectiveController.Instance is { } objective) SendTo(slot.PeerId, new ObjectiveStateMsg(objective.Capture()));
        SendTo(slot.PeerId, FloorClockNow()); // the floor ran on while it built: the pair in its FloorChange is old, this one is not
        SendPropState(slot.PeerId); // the props as they stand now (a revisit's cages, keys and gates at once, not at the next prop check)
        Dungeon.Rails.RailNet.Instance?.SendRailCatchUp(slot.PeerId); // the carts as they roll, the seats taken while it built (the crew in their wagons), the leaps in the air
        SendWorldChecks(slot.PeerId); // what changed while it built (an item taken, a creature spawned) is put right now, not at the next check
    }

    private static string FloorObjectiveId(Main main) => Dungeon.Generation.FloorObjectives.Id(main.Builder.Layout?.Objective?.Kind ?? Dungeon.Generation.FloorObjectiveKind.None);

    /// <summary>
    /// Cues the acting client already played for itself, and UI noise that is nobody else's business. Death cues too:
    /// every client plays its enemy's death cue in <c>Enemy.ClientDie</c> on the EnemyDied message.
    /// </summary>
    private static readonly HashSet<string> LocalCues = new(new[] { "swing", "swing_heavy", "bow", "cast", "ui_click", "ui_open", "footstep", "miss", "enemy_die" }
        .Concat(VoiceSets.All.Select(VoiceSets.DeathCue)));
    private static bool Quiet => Main.Instance?.Rebuilding == true;
    private static NetVec Vec(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// The wire form of a hit effect. Blood goes in world space; an arrow goes in the enemy's own frame, because a client
    /// draws its copy of the enemy a little in the past: rebuilt from that copy's transform, the arrow lands in the body
    /// it shows instead of beside it.
    /// </summary>
    private static HitFxEvt HitFxMessage(HitFxKind kind, Vector3 pos, Vector3 dir, Enemies.Enemy enemy)
    {
        if (kind == HitFxKind.Arrow)
        {
            var xf = enemy.GlobalTransform;
            pos = xf.AffineInverse() * pos;
            dir = xf.Basis.Inverse() * dir;
        }
        return new HitFxEvt((byte)kind, Vec(pos), Vec(dir), enemy.NetId, 0u);
    }
    private int _attackLogs;
    private readonly HashSet<int> _dirtyMembers = new();
    private bool _bagDirty;
    private string _bagSignature = "";
    private int _goldSent = -1;

    private void QuitEmpty()
    {
        GD.Print("[net-server] empty, quitting");
        Trace.Add("empty");
        if (!NetSession.Instance.TestServer) GetTree().Quit();
    }

    private static PoseDto? PoseOf(int member)
    {
        var party = Main.Instance?.Party;
        if (party == null || member < 0 || member >= party.Adventurers.Length) return null;
        var a = party.Adventurers[member];
        return new PoseDto { X = a.GlobalPosition.X, Y = a.GlobalPosition.Y, Z = a.GlobalPosition.Z, Yaw = a.Rotation.Y };
    }

    // ---- handshake callbacks -----------------------------------------------------------------

    private Welcome BuildWelcome(PlayerSlot slot, int peer)
    {
        var main = Main.Instance;
        var game = Game.Instance;
        var floor = game.CurrentFloor;
        var root = main.Builder.FloorRoot;
        var layout = main.Builder.Layout;
        var state = root != null && layout != null && main.Exploration != null
            ? FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration)
            : new FloorStateDto();
        var adventure = new AdventureDto
        {
            Mode = game.Adventure.Mode, Campaign = game.Adventure.Campaign, FloorPlans = game.Adventure.FloorPlans,
            Quests = game.Adventure.Quests, AiStoppedReason = game.Adventure.AiStoppedReason,
            Clauses = game.Adventure.Clauses, Run = game.Adventure.Run, DailyDate = game.Adventure.DailyDate, UnlockedMerchantLines = game.Adventure.UnlockedMerchantLines, // run-structure: the contract and the stall's lines ride in the Welcome
        };
        var snapshot = main.BuildSnapshotFor.TryGetValue(floor, out var s) ? s : main.Party.Snapshot();
        var objective = AdventureDirector.Instance?.CurrentObjective ?? "";
        Trace.Add(slot.ParkedPose != null || slot.LastSeenUtc != "" ? $"rejoin:{slot.Name}" : $"join:{slot.Name}");
        GD.Print($"[net-server] {slot.Name} {(slot.LastSeenUtc != "" ? "rejoins" : "joins")} as member {slot.MemberIndex} ({slot.ClassId}) on floor {floor}");
        _welcomeEpoch[peer] = _floorEpoch;
        return new Welcome(peer, slot.MemberIndex, game.GameSeed, floor, game.PlaySeconds, game.Victory, adventure, snapshot, state,
            DynamicEnemies(root), main.Party.CaptureState(), Registry.ToDtos(), objective, NetSession.Instance.Tick, layout?.ContentHash() ?? 0, FloorObjectiveId(main), _floorEpoch,
            NetSession.Instance.Now, WelcomeFloorSeconds(main.Builder));
    }

    /// <summary>
    /// The Welcome's floor seconds for its ServerTime (<c>Now</c>, which also seeds the joiner's NetClock, so it stays exact): the builder's
    /// stamped pair (<see cref="DungeonBuilder.FloorStampNow"/>) carried on to now, so the pair is the one FloorClockNow would send, never a step stale.
    /// </summary>
    private static double WelcomeFloorSeconds(DungeonBuilder builder)
        => builder.FloorSeconds + Math.Max(0.0, NetSession.Instance.Now - builder.FloorStampNow);

    /// <summary>An enemy on the wire: its elite name and affixes ride along so a client dresses and names it, and its spawn index so a planned one sent again (the enemy check) keeps its identity.</summary>
    public static DynamicEnemyDto EnemyDto(Enemy e)
        => new(e.NetId, e.Profile.Id, e.GlobalPosition.X, e.GlobalPosition.Y, e.GlobalPosition.Z, e.Rotation.Y, e.Hp, e.HordeId,
            e.IsElite ? e.Profile.EliteName : null, e.IsElite ? EliteRules.Encode(e.Profile.Affixes) : null, e.SpawnIndex);

    public static List<DynamicEnemyDto> DynamicEnemies(Node3D? root)
    {
        var list = new List<DynamicEnemyDto>();
        if (root == null) return list;
        foreach (var e in root.GetChildren().OfType<Enemy>())
            if (e.NetId >= Enemy.DynamicNetIdBase && e.IsAlive)
                list.Add(EnemyDto(e));
        return list;
    }

    /// <summary>A peer may trade only from the safe room or within reach of the merchant, alive (the refusal is logged to the party).</summary>
    public bool CanTrade(PlayerSlot slot)
    {
        var party = Main.Instance.Party;
        return PartyController.CanTradeAt(slot.MemberIndex < party.Adventurers.Length ? party.Adventurers[slot.MemberIndex] : null);
    }

    /// <summary>The floor's sold stock slots and the purse, to everyone (or just one peer); the Lucky Dip's bought slots ride along.</summary>
    private void SendShopSync(int peer = -1)
    {
        var party = Main.Instance.Party;
        var msg = new ShopSync(Game.Instance.CurrentFloor, party.ShopSoldOnFloor.OrderBy(i => i).ToList(), party.Gold, party.Buyback.ToDto());
        if (peer < 0) Broadcast(msg); else SendTo(peer, msg);
        SendLuckyDipSync(peer);
    }

    /// <summary>The floor's bought Lucky Dip bundles and the purse (fortune), to everyone (or just one peer).</summary>
    private void SendLuckyDipSync(int peer = -1)
    {
        var party = Main.Instance.Party;
        var msg = new LuckyDipSync(Game.Instance.CurrentFloor, party.LuckyDipBoughtOnFloor.OrderBy(i => i).ToList(), party.Gold);
        if (peer < 0) Broadcast(msg); else SendTo(peer, msg);
    }

    /// <summary>
    /// The parked pose is somewhere to rejoin (<see cref="CrevasseRules.RejoinAtParkedPose"/>): not for a hero who fell (its pose is
    /// the hold in the dark; it comes back beside the party as it unparks), nor below a drop's lip. Else the stairs.
    /// </summary>
    private static bool ParkedPoseHolds(Main main, int index, Vector3 pose)
    {
        if (main.Builder.Layout is not { } layout || index < 0 || index >= main.Party.Members.Length) return true;
        var cell = DungeonBuilder.WorldToCell(pose);
        var holds = CrevasseRules.RejoinAtParkedPose(layout, cell, pose.Y, main.Builder.Surface(cell).Y, main.Party.Members[index].Fallen);
        if (!holds) GD.Print($"[net-server] member {index}'s parked pose at {cell} (y {pose.Y:0.0}) is in the dark or fallen: rejoins at the stairs");
        return holds;
    }

    private void OnPlayerReady(PlayerSlot slot)
    {
        var main = Main.Instance;
        var party = main.Party;
        var index = slot.MemberIndex;
        _spawnSent.Remove(index); // a join places its body itself (below): a floor's told spot is not its
        FlushJoinQueue(slot.PeerId);
        // the floor changed while this player built the old one (the held FloorChange is on its way to it now): its poses
        // must not count until it has built the new floor and said so (OnFloorReady sets Ready and sends the full picture)
        // (or the same floor number was built again: a wipe's reload during the join, which the floor number alone missed)
        var staleBuild = _welcomeEpoch.Remove(slot.PeerId, out var welcomed) && welcomed != _floorEpoch;
        var behind = _floorChanging || slot.ReadyFloor != Game.Instance.CurrentFloor || staleBuild;
        if (behind) { slot.Ready = false; GD.Print($"[net-server] {slot.Name} joined on floor {slot.ReadyFloor} while the party went to {Game.Instance.CurrentFloor}: it follows"); }
        Vector3? at = null;
        Vector3? look = null;
        if (slot.ParkedPose is { } p && slot.ParkedFloor == Game.Instance.CurrentFloor && ParkedPoseHolds(main, index, new Vector3(p.X, p.Y, p.Z))) at = new Vector3(p.X, p.Y, p.Z);
        else if (main.Builder.Layout is { } layout)
        {
            var stairs = layout.UpStairs;
            var cell = DungeonBuilder.WalkableNeighbor(layout, stairs);
            at = main.Builder.Surface(cell) + Vector3.Up * 0.3f + new Vector3(0.3f * index, 0, 0.3f * index);
            look = main.Builder.Surface(stairs);
        }
        party.Unpark(index, ControlKind.Remote, at, look);
        if (slot.ParkedPose is { } pp && at != null && look == null) party.Adventurers[index].Rotation = new Vector3(0, pp.Yaw, 0);
        var body = party.Adventurers[index];
        if (!behind) SendTo(slot.PeerId, new ForcePose(index, new PoseDto { X = body.GlobalPosition.X, Y = body.GlobalPosition.Y, Z = body.GlobalPosition.Z, Yaw = body.Rotation.Y }));
        EventBus.Instance.Log($"{slot.Name} joins the party.");
        Broadcast(new PlayerJoined(slot.ToDto()), except: slot.PeerId);
        // the others built a default member from PlayerJoined; the sheet sent at Hello came before they had the place for it
        Broadcast(new PartySync(index, SaveMapper.ToDto(party.Members[index])), except: slot.PeerId);
        if (!behind)
        {
            SendSnapshotTo(slot.PeerId, full: true);
            SendShopSync(slot.PeerId); // a join: the joiner is in game now, so it will not discard this (a floor change sends it in OnFloorReady)
        }
        SendCatchUp(slot.PeerId);
        if (!behind) Dungeon.Rails.RailNet.Instance?.SendRailCatchUp(slot.PeerId); // the carts as they roll, who sits where, the leaps in the air (after the join queue's own SeatMsgs)
        if (!behind) SendWorldChecks(slot.PeerId); // closes the join window at once: items and creatures that changed since the Welcome
        Trace.Add($"ready:{slot.Name}");
        ResumeBotsOf(slot);
        PlayerReady?.Invoke(slot);
        WriteStatus();
    }

    /// <summary>The shared state a joiner must hold whatever it missed: the bag, every member, the purse, an altar card waiting for a pick.</summary>
    private void SendCatchUp(int peer)
    {
        var party = Main.Instance.Party;
        SendTo(peer, new BagSync(SaveMapper.ToDto(party.Bag)));
        for (var i = 0; i < party.Members.Length; i++) SendTo(peer, new PartySync(i, SaveMapper.ToDto(party.Members[i])));
        SendTo(peer, new GoldSync(party.Gold));
        if (FortuneDirector.Instance?.PendingShrine is { } card) SendTo(peer, card);
        SendTo(peer, FloorClockNow()); // the floor clock as it stands (a joiner still on an older floor drops it by its epoch)
        SendPropState(peer); // the props as they stand now (a joiner still on an older floor ignores it; the prop check covers it later)
    }

    private void OnPlayerLeft(PlayerSlot slot)
    {
        var party = Main.Instance?.Party;
        UnseatLeaving(slot); // a seat is never left booked to a body that is gone: it steps out beside the lane first
        _rttOf.Remove(slot.MemberIndex);
        _shovedPending.Remove(slot.MemberIndex);
        _returnedPending.Remove(slot.MemberIndex);
        if (party != null && GodotObject.IsInstanceValid(party)) party.Park(slot.MemberIndex);
        EventBus.Instance.Log($"{slot.Name} leaves the party.");
        Broadcast(new PlayerLeft(slot.MemberIndex, slot.Name));
        Trace.Add($"left:{slot.Name}");
        PlayerLeft?.Invoke(slot);
        WriteStatus();
        CallDeferred(nameof(AutosaveDeferred)); // once the registry has parked the slot
    }

    private void AutosaveDeferred() => Autosave("leave");
    private void AutosaveFloor() => Autosave("floor change");

    /// <summary>The whole world plus who owns which member, to the server's slot. Clients never save.</summary>
    public void Autosave(string reason)
    {
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Rebuilding) return;
        try
        {
            var started = Time.GetTicksMsec();
            SaveManager.Write(NetSession.Instance.SaveSlot, main.BuildSaveData("Server"));
            if (NetSession.Instance.TestServer) GD.Print($"[net-server] autosave took {Time.GetTicksMsec() - started} ms");
            GD.Print($"[net-server] autosaved ({reason})");
        }
        catch (Exception e) { GD.PushError($"[net-server] autosave failed: {e.Message}"); }
    }

    public override void _ExitTree()
    {
        // on quit the world may already be torn down; the leave and floor autosaves cover that case
        var main = Main.Instance;
        if (main != null && main.InGame && main.Party.IsInsideTree() && main.Party.Adventurers.All(a => GodotObject.IsInstanceValid(a) && a.IsInsideTree())) Autosave("quit");
    }

    // ---- traffic -----------------------------------------------------------------------------

    private void OnReceived(int from, byte[] bytes)
    {
        NetMessage m;
        try { m = NetCodec.Decode(bytes); }
        catch (NetCodecException e)
        {
            GD.PushWarning($"[net-server] bad packet from {from}: {e.Message}");
            (_t as WebSocketTransport)?.Disconnect(from);
            return;
        }
        if (!Handshake.InGame(from))
        {
            // a join that throws (building the Welcome, readying the player) must not leave the peer Welcomed for good, its name
            // taken and broadcasts piling up for it: drop it, and its client can join again
            try
            {
                var reply = Handshake.OnMessage(from, m, out var drop);
                if (reply != null) _t.Send(from, NetCodec.Encode(reply));
                if (drop) (_t as WebSocketTransport)?.Disconnect(from);
            }
            catch (Exception e)
            {
                GD.PushError($"[net-server] {m.Kind} from joining peer {from} failed: {e.GetType().Name}: {e.Message}; dropping it");
                (_t as WebSocketTransport)?.Disconnect(from);
            }
            return;
        }
        var slot = Registry.ByPeer(from);
        if (slot == null) return;
        // one message that throws (a stale index, a node freed under it) must cost that message only: uncaught, it escaped the
        // transport's poll and the rest of the frame's packets waited, and its log line never named what failed
        try { Dispatch(from, slot, m); }
        catch (Exception e) { GD.PushError($"[net-server] {m.Kind} from {slot.Name} failed: {e.GetType().Name}: {e.Message}"); }
    }

    private void Dispatch(int from, PlayerSlot slot, NetMessage m)
    {
        switch (m)
        {
            case Pose pose: if (slot.Ready) ApplyPose(slot, pose); break;
            case Ready ready: OnFloorReady(slot, ready.Floor); break;
            case ChatIntent chat:
                var text = chat.Text.Trim();
                if (text.Length == 0) break;
                if (text.Length > Protocol.MaxChatLength) text = text[..Protocol.MaxChatLength];
                Broadcast(new ChatMsg(slot.Name, text));
                GD.Print($"[chat] {slot.Name}: {text}");
                Trace.Add("chat");
                break;
            case Ping ping:
                _t.Send(from, NetCodec.Encode(new Pong(ping.Tick, ping.SentAt, NetSession.Instance.Now)));
                if (double.IsFinite(ping.Rtt) && ping.Rtt > 0) _rttOf[slot.MemberIndex] = Math.Min(ping.Rtt, 2.0);
                break;
            default:
                HandleIntent(slot, m);
                IntentReceived?.Invoke(from, m);
                break;
        }
    }

    /// <summary>A railed floor's prop after a player's use, for a test server's trace ("prop:{ref}:{state}"): a lever thrown or straight, a gate up or down, a cage open or locked, the keys taken or left; null for anything else.</summary>
    private static string? RailPropState(Node3D node) => node switch
    {
        Dungeon.Rails.TrackSwitch sw => sw.Thrown ? "thrown" : "straight",
        Dungeon.Rails.RailGate.WinchPost w => w.Gate.Up ? "up" : "down",
        Dungeon.Rails.PlatformCage c => c.Open ? "open" : "locked",
        Adventure.Objectives.OrcKeyring => Adventure.Objectives.ObjectiveController.Instance is Adventure.Objectives.CrewRescueController { OrcKeys: true } ? "taken" : "left",
        _ => null,
    };

    /// <summary>A dialogue node id or NPC id from a client longer than this is ignored: real ids are short words.</summary>
    private const int MaxDialogueNodeIdLength = 64;

    /// <summary>A player wants to do something: the server's body for them does it, with the same code single-player runs.</summary>
    private void HandleIntent(PlayerSlot slot, NetMessage m)
    {
        var main = Main.Instance;
        var party = main?.Party;
        if (main == null || party == null || slot.MemberIndex >= party.Adventurers.Length) return;
        var body = party.Adventurers[slot.MemberIndex];
        if (body.Parked) return;
        switch (m)
        {
            case AddBotIntent ab: OnAddBot(slot, ab.Hero); break;
            case DismissBotIntent db: OnDismissBot(slot, db.Member); break;
            case LootResyncIntent lr: OnLootResync(slot, lr); break;
            case StateResyncIntent sr: OnStateResync(slot, sr); break;
            case PropResyncIntent pr: OnPropResync(slot, pr); break;
            case EnemyResyncIntent er: OnEnemyResync(slot, er); break;
            case FloorResyncIntent fr: OnFloorResync(slot, fr); break;
            case RideIntent ri: OnRideIntent(slot, body, ri); break;
            case AttackIntent a:
            {
                // aim exactly where the player aimed; the body stays where its poses put it. A seated body (the server's seat, never
                // the client's word) turns its head in the seat instead: no body rotation, no nudge (the seat places it)
                var wanted = new Vector3(a.Position.X, a.Position.Y, a.Position.Z);
                if (body.Seated) body.AimSeated(a.Yaw);
                else body.Rotation = new Vector3(0, a.Yaw, 0);
                body.Camera.Rotation = new Vector3(Mathf.Clamp(a.Pitch, Mathf.DegToRad(-Adventurer.MaxPitchDegrees), Mathf.DegToRad(Adventurer.MaxPitchDegrees)), 0, 0);
                if (!body.Seated && wanted.DistanceTo(body.Camera.GlobalPosition) < 1.5f && body.Remote != null) { body.Remote.Position = body.GlobalPosition + (wanted - body.Camera.GlobalPosition); body.GlobalPosition = body.Remote.Position; }
                body.Attack(a.Heavy, a.Perfect); // stamina and cooldown reach every screen in the Vitals; the sheet itself did not change
                if (!Trace.Contains("attack")) Trace.Add("attack");
                if (NetSession.Instance.TestServer && _attackLogs++ < 6)
                {
                    var nearest = main.Builder.FloorRoot?.GetChildren().OfType<Enemy>().Where(e => e.IsAlive).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(body.GlobalPosition)).FirstOrDefault();
                    GD.Print($"[net-server] attack from {slot.Name}: body={body.GlobalPosition:0.0} cam={body.Camera.GlobalPosition:0.0} wanted={wanted:0.0} yaw={a.Yaw:0.00} nearest={(nearest?.DisplayName ?? "-")}@{(nearest?.GlobalPosition.DistanceTo(body.GlobalPosition) ?? 0):0.0}m stamina={body.State.Stamina:0} cd={body.State.CooldownRemaining:0.00} rejectedPoses={RejectedPoses}");
                }
                break;
            }
            case InteractIntent i:
            {
                if (main.Builder.FloorRoot is not { } root) break;
                // a held interact (plan 7: leave the prisoners behind) on the wrapped ref, in reach as a plain use is
                if (NetRefs.TryParse(i.Ref, out var holdKind, out var inner) && holdKind == NetRefs.HoldKind)
                {
                    if (NetRefResolver.Resolve(root, inner) is not { } held || held is not IHoldInteractable holdTarget)
                    {
                        if (NetSession.Instance.TestServer) GD.Print($"[net-server] hold {inner} from {slot.Name}: not found");
                        break;
                    }
                    var holdDist = body.HoldDistance(held);
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] hold {inner} from {slot.Name}: {holdDist:0.0} m");
                    if (holdDist > Dungeon.Rails.PropPromptRules.InteractReach) break;
                    holdTarget.HoldInteract(party, body);
                    break;
                }
                var resolved = NetRefResolver.Resolve(root, i.Ref);
                if (resolved is not { } node || node is not IInteractable target)
                {
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: not found");
                    // recovery: an item this client still shows that is gone here (whatever let it slip) is taken off its screen now
                    if (NetRefs.TryParse(i.Ref, out var kind, out var id) && kind == NetRefs.LootKind && Guid.TryParseExact(id, "N", out var gone))
                    {
                        SendTo(slot.PeerId, new LootRemoved(gone, -1));
                        Trace.Add("loot-resync");
                        GD.Print($"[net-server] {slot.Name} reached for loot {id} that is not on the floor: told them to drop it");
                    }
                    break;
                }
                // the look ray reaches 3 m to the item's (or prop's) box; its centre can sit half a metre further. A lever (a switch, a spur's)
                // thrown from the seat of a rolling cart was in reach a round trip ago: the cart's speed times that member's round trip is allowed too
                var reach = node is Dungeon.Rails.TrackSwitch ? Dungeon.Rails.PropPromptRules.LeverReach(body.Seated, SeatSpeed(body), _rttOf.GetValueOrDefault(slot.MemberIndex)) : Dungeon.Rails.PropPromptRules.InteractReach;
                // a cart, or a wagon of the party's train (plan 14: the same tub), is measured to its tub (2.2 m long), as the look ray reaches its box and
                // the cart's prompt measures it (RideInputRules.BoardReach): its end is in reach, not only its middle. The lift's five bodies (plan 15
                // Task 6: "parked:{piece}" its cart, "liftwagon:{k}" its tubs) are the same tubs, measured the same way as its prompt measures them
                var dist = node is Dungeon.Rails.MineCart tub ? tub.TubDistance(body)
                    : node is Dungeon.Rails.TrainWagon wagonTub ? wagonTub.TubDistance(body)
                    : Math.Min(node.GlobalPosition.DistanceTo(body.GlobalPosition), node.GlobalPosition.DistanceTo(body.Camera.GlobalPosition));
                // a lever in the sender's seated window (seated in the party's rolling cart, the lever coming up on the armed ride) is taken from
                // any distance, as their screen offered it (SeatedSwitchRules); its taking at the points ends that. Another rider's set-back from
                // inside their window right after a toggle is refused, in reach or not (no window press is aimed), so two riders cued by the same
                // call do not undo each other into the heap
                // ride 4's leap (plan 13, Ruling 20): a slag cart's "Leap across" (slag:{id}), or the party's cart's "Leap back" (cart:0) from a slag
                // cart's deck, is taken from wherever the sender sits when the leap's rules take it (LeapAccepts: the offer with AcceptSlack); else refused
                var leapPress = node is Dungeon.Rails.SlagCart || (node is Dungeon.Rails.MineCart && body.Cart is Dungeon.Rails.SlagCart);
                var leapTaken = false;
                if (leapPress)
                {
                    leapTaken = Dungeon.Rails.RailEncounterController.Instance is { } leapEnc && GodotObject.IsInstanceValid(leapEnc) && leapEnc.LeapAccepts(body, node);
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: leap {(leapTaken ? "accepted" : "refused")}"); // a mashing client on a live server writes nothing (T4 Minor 10)
                    if (!leapTaken) break;
                }
                // climbing car to car (plan 14, Ruling 10): a press on a wagon of the train, or on the party's cart from a wagon, that the move's rules
                // read as a move (RailController.IsMovePress), or that asks for one from another car of the rolling train on a car whose plain use offers
                // the sender nothing (IsMoveAttempt: a place another press booked, a lever window, the live bridge), is judged as a move: taken from
                // wherever the sender sits, or refused out loud (WhyMoveRefused) and never passed on to the car's plain use
                var moveRails = Dungeon.Rails.RailController.Instance is { } mrc && GodotObject.IsInstanceValid(mrc) ? mrc : null;
                var movePress = moveRails != null && (node is Dungeon.Rails.TrainWagon || node == moveRails.Cart) && moveRails.IsMoveAsk(body, node);
                var moveTaken = false;
                if (movePress)
                {
                    var no = moveRails!.WhyMoveRefused(body, node);
                    moveTaken = no == null;
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: move {(moveTaken ? "accepted" : $"refused ({no})")}"); // a mashing client on a live server writes nothing, as the leap's
                    if (!moveTaken) break;
                }
                var lever = node as Dungeon.Rails.TrackSwitch;
                var window = lever != null && Dungeon.Rails.RailEncounterController.Instance is { } enc && GodotObject.IsInstanceValid(enc) && enc.InSeatedWindow(body, lever);
                if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: {dist:0.0} m (reach {reach}) body={body.GlobalPosition:0.0} node={node.GlobalPosition:0.0}{(lever != null ? $" seated window {(window ? "yes" : "no")}" : "")}");
                // ride 4's split: a set-back short of its points is never refused (plan 12, Ruling 7; the guard applies past them)
                if (window && Dungeon.Rails.RailEncounterController.Instance?.GuardsSetBack(lever!) != false && Dungeon.Rails.SeatedSwitchRules.RefuseSetBack(lever!.Thrown, NetSession.Instance.Now, lever.LastToggledAt, lever.LastToggledBy, body.State.Name))
                {
                    GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: seated window: set back {NetSession.Instance.Now - lever.LastToggledAt:0.00} s after {lever.LastToggledBy} threw it, ignored");
                    break;
                }
                // a press on a deadly lever (a live switch back) after the cart took it as it stood, or at its points: refused out loud while it is
                // straight, so the presser does not believe it thrown (final review M4; the client's cart may run behind the server's near the
                // points); thrown, it is ignored quietly (Ruling 21): never "Too late" to the rider who threw it, and never un-thrown
                if (lever is { Deadly: true } && Dungeon.Rails.RailEncounterController.Instance is { } lateEnc && GodotObject.IsInstanceValid(lateEnc)
                    && lateEnc.LateFor(lever) is var late && late != Dungeon.Rails.LatePress.None)
                {
                    if (late == Dungeon.Rails.LatePress.TooLate)
                    {
                        Tell(slot, Dungeon.Rails.SwitchBackRules.TooLateLine);
                        GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: too late to throw the live switch back (taken {lever.Taken}): told them");
                    }
                    else GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: the deadly lever was thrown (taken {lever.Taken}): ignored quietly");
                    break;
                }
                if (dist > reach && !window && !leapTaken && !moveTaken) { if (node is Items.LootDrop) Tell(slot, "Too far away to reach."); break; }
                // recovery: the altar is paid and its card still waits for a pick, but this player's screen lost it: hand it back
                if (node is Dungeon.Props.Shrine && FortuneDirector.Instance?.PendingShrine is { } pendingCard) { SendTo(slot.PeerId, pendingCard); Trace.Add("shrine-resent"); break; }
                // the bag filled here (a bot's pickup) before this player's copy showed it: tell them, not the whole party, and
                // send the bag so their prompt says "full" too
                if (node is Items.LootDrop { Coins: 0 } full && !party.Bag.HasRoomFor(full.Item))
                {
                    Tell(slot, Items.LootDrop.BagFullLine);
                    SendTo(slot.PeerId, new BagSync(SaveMapper.ToDto(party.Bag)));
                    break;
                }
                var seatedBefore = body.Seated;
                target.Interact(party, body);
                if (node is Dungeon.Rails.MineCart && !seatedBefore && body.Seated) Trace.Add($"aboard:{slot.Name}");
                if (node.GetParent() is Dungeon.Rails.ParkedTrain && !seatedBefore && body.Seated) // a press on any of the lift's bodies (plan 15 Ruling 28)
                {
                    Trace.Add($"lift:{slot.Name}");
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: boarded {(body.Cart is Dungeon.Rails.MineCart { Kind: CartKind.Chaser } ? "the lift" : "the party's cart that took its place")} ({dist:0.0} m)");
                }
                if (moveTaken) Trace.Add($"move:{slot.Name}");
                if (NetSession.Instance.TestServer && node is Dungeon.Rails.MineCart && seatedBefore && body.Seated && !leapTaken && Dungeon.Rails.RailEncounterController.Instance?.HoldReason() is { } holdReason)
                    GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: get out refused ({holdReason})"); // the refusal is RailController.UseCart's
                if (NetSession.Instance.TestServer && RailPropState(node) is { } propState) { Trace.Add($"prop:{i.Ref}:{propState}"); GD.Print($"[net-server] {slot.Name} used {i.Ref}: now {propState}"); } // test servers only: a lever can be used without end
                if (node is Items.LootDrop) { _bagDirty = true; Trace.Add("pickup"); }
                if (node is Dungeon.Props.Chest) Trace.Add("chest");
                break;
            }
            case RescueIntent r:
            {
                if (r.Member < 0 || r.Member >= party.Adventurers.Length) break;
                var target = party.Adventurers[r.Member];
                if (!target.State.Mangled || !target.Downed) break;
                var started = party.TryRescue(target, body, () => _bound.Contains(target.Index) || main.Builder.HazardArmed(target.State.MangledBy)); // a bound body is clear of its hazard
                if (NetSession.Instance.TestServer) GD.Print($"[net-server] rescue of {target.State.Name} by {slot.Name}: {(started ? "started" : "refused")}");
                if (started) Trace.Add("rescue");
                break;
            }
            case StairsReadyIntent sr:
            {
                // a Yes needs the voter within reach of the stairs (as InteractIntent) and runs the gate through Stairs.Interact; a No needs no reach
                if (main.Builder.FloorRoot is not { } root || NetRefResolver.Resolve(root, sr.Ref) is not Dungeon.Stairs stairs) break;
                if (!sr.Ready) { StairsVote(stairs, slot.MemberIndex, false); break; }
                var dist = Math.Min(stairs.GlobalPosition.DistanceTo(body.GlobalPosition), stairs.GlobalPosition.DistanceTo(body.Camera.GlobalPosition));
                if (NetSession.Instance.TestServer) GD.Print($"[net-server] stairs vote {sr.Ref} from {slot.Name}: {dist:0.0} m");
                if (dist > 3.5f) break;
                stairs.Interact(party, body);
                break;
            }
            case PingIntent pi:
                HandlePing(slot, body, pi);
                break;
            case ShoutIntent:
                if (!body.Downed) break;
                EventBus.Instance.Log($"{slot.Name} needs help!");
                Trace.Add("shout");
                break;
            case CheerIntent:
                EventBus.Instance.RaiseCheer(slot.MemberIndex); // the director picks the lines (relayed as barks) and the bodies' emote
                Trace.Add("cheer");
                break;
            case BagMoveIntent bm:
                if (!MayTouch(slot, bm.From, bm.To) || !StillHolds(slot, bm.From, bm.Expect)) break; // the guard answers a refusal (ServerWorld.BagGuard.cs)
                party.BagMove(bm.From, bm.To, body);
                _bagDirty = true;
                break;
            case ShopBuyIntent sb:
            {
                if (!slot.Ready || !CanTrade(slot)) break;
                var reach = PartyController.TradeReachFor(slot.MemberIndex < party.Adventurers.Length ? party.Adventurers[slot.MemberIndex] : null); // which keeper: each sells only its own segment
                if (party.ShopBuy(sb.StockIndex, reach) == null) Trace.Add("shop");
                SendShopSync();
                _bagDirty = true;
                break;
            }
            case LuckyDipIntent ld:
            {
                if (!slot.Ready || !CanTrade(slot)) break;
                var reach = PartyController.TradeReachFor(slot.MemberIndex < party.Adventurers.Length ? party.Adventurers[slot.MemberIndex] : null);
                if (party.LuckyDipBuy(ld.Slot, reach, slot.MemberIndex) == null) Trace.Add("dip"); // the Gambled bark names the buyer, not the server's focused member
                SendLuckyDipSync();
                _bagDirty = true;
                break;
            }
            case ShopSellIntent ss:
                if (!slot.Ready || !CanTrade(slot)) break;
                if (!MayTouch(slot, ss.Slot, ss.Slot) || !StillHolds(slot, ss.Slot, ss.Expect)) break; // only plain sales were unguarded: a legendary's screen asks by id
                if (party.ShopSell(ss.Slot) == null) Trace.Add("shop");
                SendShopSync();
                _bagDirty = true;
                break;
            case ShrineChoiceIntent sc:
                if (!slot.Ready || FortuneDirector.Instance is not { } fortune) break;
                // the first pick wins and the director closes the card everywhere; a refused pick (a lift the purse cannot pay) hands the asker its card back so its rows come alive again
                if (!fortune.ResolveShrine(slot.MemberIndex, sc.Choice)) { if (fortune.PendingShrine is { } pending) SendTo(slot.PeerId, pending); else SendTo(slot.PeerId, new ShrineClosedEvt("gone")); }
                break;
            case ShopBuybackIntent bb:
                if (!slot.Ready || !CanTrade(slot)) break;
                if (party.ShopBuyback(bb.Index) == null) Trace.Add("shop");
                SendShopSync();
                _bagDirty = true;
                break;
            case DialogueNodeIntent dn:
                // any ready player may reach the deal; AdventureDirector filters (floor 0, the merchant) and pays the shared purse once
                if (!slot.Ready || string.IsNullOrEmpty(dn.NpcId) || string.IsNullOrEmpty(dn.NodeId) || dn.NpcId.Length > MaxDialogueNodeIdLength || dn.NodeId.Length > MaxDialogueNodeIdLength) break;
                EventBus.Instance.RaiseDialogueNode(dn.NpcId, dn.NodeId);
                if (!NetSession.Instance.TestServer) break;
                if (dn.NodeId == TavernRules.DealNodeId && !Trace.Contains($"node:{dn.NodeId}")) Trace.Add($"node:{dn.NodeId}"); // the test's mark; never a client-sized list
                GD.Print($"[net-server] {slot.Name} reached dialogue node {dn.NpcId}/{dn.NodeId} on floor {Game.Instance.CurrentFloor}");
                break;
            case QuickActionIntent q:
            {
                // the hero the screen acts for (equip on, drink for): the sender's own, or a bot they own; a click on an equip
                // slot takes that piece off its wearer whoever the screen shows (MayTouch checks the wearer is ours)
                var who = q.Slot.IsEquip ? q.Slot.Member : q.Member < 0 ? slot.MemberIndex : q.Member;
                if (!MayActFor(slot, who) || !MayTouch(slot, q.Slot, q.Slot) || !StillHolds(slot, q.Slot, q.Expect)) break;
                party.BagQuick(q.Slot, who, body);
                _bagDirty = true;
                break;
            }
            case CycleAbilityIntent c:
                party.CycleAbility(slot.MemberIndex, Math.Sign(c.Dir));
                break;
            case AllocateStatIntent st:
            {
                var who = st.Member < 0 ? slot.MemberIndex : st.Member; // the sheet's hero: our own or a bot we own (their points were never spendable before v14)
                if (!MayActFor(slot, who)) break;
                if (Enum.TryParse<Characters.Stat>(st.Stat, out var stat)) party.AllocateStat(who, stat);
                break;
            }
            case LearnSkillIntent ls:
            {
                var who = ls.Member < 0 ? slot.MemberIndex : ls.Member;
                if (!MayActFor(slot, who)) break;
                if (Game.Instance.Skills.Skills.TryGetValue(ls.SkillId, out var skill)) party.LearnSkill(who, skill);
                break;
            }
            case DialogueChoiceIntent d:
                if (d.Effect == DialogueEffect.Join)
                {
                    // the fighter decides here and the asker hears the answer, so their screen shows the yes or the no line
                    var fighter = main.Builder.FloorRoot?.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == d.NpcId);
                    var yes = fighter?.AskToJoin() ?? false;
                    SendTo(slot.PeerId, new DialogueAnswer(d.NpcId, yes));
                    Trace.Add(yes ? $"escort:{fighter!.NpcName}" : $"refused:{d.NpcId}");
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] {slot.Name} asked {d.NpcId} to join: {(yes ? "yes" : "no")}");
                    break;
                }
                EventBus.Instance.RaiseDialogueEffect(d.NpcId, d.Effect, d.Flag);
                break;
        }
    }

    /// <summary>The speed of the cart <paramref name="body"/> sits in (the party's), 0 on foot or in anything else.</summary>
    private static float SeatSpeed(Adventurer body)
        => body.Seated && Dungeon.Rails.RailController.Instance is { } rc && GodotObject.IsInstanceValid(rc) && body.Cart == rc.Cart /* cart-only */ && rc.State.Mode == Dungeon.Rails.CartMode.Rolling ? rc.State.V : 0f;

    /// <summary>Members the server set down from a seat whose poses still in flight say seated: dropped until one says otherwise.</summary>
    private readonly HashSet<int> _unseatedPending = new();

    /// <summary>Members the server moved itself, with where from, where to and when, until a pose from the new spot arrives (<see cref="PoseGuardRules.DropAfterShove"/>): a shove (<c>Adventurer.Shove</c>), a return from a crevasse, or a floor-ready ForcePose that puts a body somewhere other than the spot its FloorChange told it (<c>OnFloorReady</c>, from <see cref="_spawnSent"/>).</summary>
    private readonly Dictionary<int, (Vector3 From, Vector3 To, double At)> _shovedPending = new();

    /// <summary>Members the server returned from a crevasse (<c>Adventurer.ReturnFromCrevasse</c>) and when, until a pose off a drop arrives or the guard gives up (<see cref="PoseGuardRules.DropAfterReturn"/>).</summary>
    private readonly Dictionary<int, double> _returnedPending = new();
    /// <summary>Poses dropped by the returned-pose guard (for tests).</summary>
    public int ReturnedPosesDropped { get; private set; }

    /// <summary>The server returned remote member <paramref name="member"/> from a crevasse: its poses over a drop still in flight are dropped.</summary>
    public void NoteReturned(int member) => _returnedPending[member] = NetSession.Instance.Now;

    /// <summary>The server moved remote member <paramref name="member"/>'s body from <paramref name="from"/> to <paramref name="to"/> (its ForcePose is on its way): its poses still in flight are dropped. Callers: <c>Adventurer.Shove</c> (a hazard, a test, a crevasse return) and <c>OnFloorReady</c> (from the spot the FloorChange told the client, to where the body stands).</summary>
    public void NoteShoved(int member, Vector3 from, Vector3 to) => _shovedPending[member] = (from, to, NetSession.Instance.Now);

    /// <summary>Poses the shove guard dropped, after a shove and after a floor-ready ForcePose alike (tests read it; the RIDE pass's rise watch prints it).</summary>
    public int ShovedPosesDropped { get; private set; }

    /// <summary>
    /// The server set remote member <paramref name="member"/> down from a seat at <paramref name="at"/> (a stop, a set-down, a wreck,
    /// getting out, a throw, a quiet unseat, a teleport; all through <c>Adventurer.UnseatedOnServer</c>): its ride intent is let go, its own screen is put there at once (with the SeatMsg the rail sender sends), and its
    /// poses still flagged seated are dropped until it has heard (they would put the body back on the seat's old place).
    /// </summary>
    public void ForceUnseatedPose(int member, Vector3 at, float yaw)
    {
        _rides.Remove(member); // its pump and brake go with the seat
        _unseatedPending.Add(member);
        var pose = new ForcePose(member, new PoseDto { X = at.X, Y = at.Y, Z = at.Z, Yaw = yaw });
        foreach (var slot in Registry.Online)
            if (slot.MemberIndex == member && slot.BotOwner == null) SendTo(slot.PeerId, pose);
    }

    private void ApplyPose(PlayerSlot slot, Pose pose)
    {
        var party = Main.Instance?.Party;
        if (party == null || slot.MemberIndex >= party.Adventurers.Length) return;
        var body = party.Adventurers[slot.MemberIndex];
        if (body.Control != ControlKind.Remote) return;
        var seatedThere = pose.Flags2.HasFlag(PoseFlags2.Seated);
        if (body.Seated)
        {
            // the server's seat places the body (never the client's flag): only where it looks from the seat, the pitch and the
            // flags count; no walkable or press check, the position is ignored. Pumping is the server's own (Inputs), not the client's
            _unseatedPending.Remove(slot.MemberIndex);
            body.Remote ??= new RemotePose();
            var seatYaw = float.IsFinite(pose.SeatYaw) ? Mathf.Wrap(pose.SeatYaw, -Mathf.Pi, Mathf.Pi) : 0f;
            body.Remote.SeatYaw = Mathf.Clamp(seatYaw, Mathf.DegToRad(-Adventurer.MaxSeatYawDegrees), Mathf.DegToRad(Adventurer.MaxSeatYawDegrees));
            body.Remote.Seated = true;
            body.Remote.Pitch = pose.Pitch;
            ApplyPoseFlags(body.Remote, pose.Flags);
            return;
        }
        if (_unseatedPending.Contains(slot.MemberIndex))
        {
            if (seatedThere) return; // sent before its screen heard of the set-down
            _unseatedPending.Remove(slot.MemberIndex);
        }
        var at = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
        if (_shovedPending.TryGetValue(slot.MemberIndex, out var shove))
        {
            if (PoseGuardRules.DropAfterShove(at.DistanceTo(shove.From), at.DistanceTo(shove.To), NetSession.Instance.Now - shove.At)) { ShovedPosesDropped++; return; } // sent before its screen heard of the shove
            _shovedPending.Remove(slot.MemberIndex);
        }
        // just returned from a crevasse: poses over a drop still in flight from its client's held body would send it straight back in
        if (_returnedPending.TryGetValue(slot.MemberIndex, out var returnedAt))
        {
            var rlayout = Main.Instance?.Builder.Layout;
            var overDrop = rlayout != null && CrevasseRules.IsDrop(rlayout, DungeonBuilder.WorldToCell(at));
            if (PoseGuardRules.DropAfterReturn(overDrop, NetSession.Instance.Now - returnedAt)) { ReturnedPosesDropped++; return; }
            _returnedPending.Remove(slot.MemberIndex);
        }
        // fallen into a crevasse: the server holds the body under the lip (CrevasseWatch.Take) until a return; its client's held body says nothing
        if (body.State.Fallen) return;
        // a body cannot be steered off the map: keep it on floor the layout knows, or falling into a drop at or below its floor (the
        // server's copy must follow it in for the crevasse watch to judge the fall; above a chasm's floor it would walk on air)
        var builder = Main.Instance!.Builder;
        var cell = DungeonBuilder.WorldToCell(at);
        if (builder.Layout is { } layout && PoseGuardRules.OffTheMap(layout.IsWalkable(cell), builder.IsUnderAt(at), CrevasseRules.IsDrop(layout, cell), at.Y, builder.Surface(cell).Y)) { RejectedPoses++; if (NetSession.Instance.TestServer && RejectedPoses <= 12) GD.Print($"[net-server] pose from {slot.Name} rejected at {at:0.0} cell {cell} ({layout[cell]})"); return; }
        // nor into a Press that is holding shut: there is no room for a body between the slabs
        if (builder.Hazards.OfType<Dungeon.Props.Press>().Any(p => p.Phase == PressPhase.Hold && p.Placement.Cells.Contains(cell))) { RejectedPoses++; return; }
        body.Remote ??= new RemotePose();
        body.Remote.Position = at;
        body.Remote.Yaw = pose.Yaw;
        body.Remote.Pitch = pose.Pitch;
        body.Remote.Velocity = new Vector3(pose.Velocity.X, pose.Velocity.Y, pose.Velocity.Z);
        body.Remote.Seated = false;
        ApplyPoseFlags(body.Remote, pose.Flags);
        if (!_moved.Contains(slot.MemberIndex) && _firstPose.TryGetValue(slot.MemberIndex, out var first) && first.DistanceTo(at) > 1f) { _moved.Add(slot.MemberIndex); Trace.Add($"moved:{slot.Name}"); }
        _firstPose.TryAdd(slot.MemberIndex, at);
    }

    private static void ApplyPoseFlags(RemotePose remote, PoseFlags flags)
    {
        remote.Crouch = flags.HasFlag(PoseFlags.Crouch);
        remote.Guard = flags.HasFlag(PoseFlags.Guard);
        remote.Parry = flags.HasFlag(PoseFlags.Parry);
        remote.Dodge = flags.HasFlag(PoseFlags.Dodge);
        remote.OnFloor = flags.HasFlag(PoseFlags.OnFloor);
    }

    /// <summary>A hazard entered a phase: tell every client which, when and how far along, so they animate in step.</summary>
    private void OnHazardPhase(int index, byte phase)
    {
        var now = NetSession.Instance.Now;
        foreach (var node in Main.Instance?.Builder.Hazards ?? Array.Empty<Node3D>())
        {
            switch (node)
            {
                case Dungeon.Props.Press p when p.Index == index:
                    Broadcast(new HazardPhaseMsg(index, (byte)HazardKind.Press, phase, now, HazardSync.DurationSeconds(HazardRules.PressDuration(p.Phase)), p.Jammed, p.Spent));
                    Trace.Add($"hazard:{p.Phase}");
                    return;
                case Dungeon.Props.SpikeBed b when b.Index == index:
                    Broadcast(new HazardPhaseMsg(index, (byte)HazardKind.SpikeBed, phase, now, HazardSync.DurationSeconds(HazardRules.SpikeDuration(b.Phase, b.Variant)), false, b.Spent));
                    Trace.Add($"hazard:{b.Phase}");
                    return;
            }
        }
    }

    // ---- bound respawn: a crushed body nobody reached is carried ahead for the party to find --------------------

    private readonly Dictionary<int, float> _mangledSince = new();
    private readonly HashSet<int> _bound = new();
    /// <summary>Members the server has bound ahead (tests and the HUD read it).</summary>
    public IReadOnlySet<int> Bound => _bound;

    private void TickBound(float dt)
    {
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Builder.Layout is not { } layout || main.Exploration is not { } exploration) return;
        var party = main.Party;
        foreach (var b in party.Active)
        {
            if (!b.Downed || !b.State.Mangled) { _mangledSince.Remove(b.Index); _bound.Remove(b.Index); continue; }
            if (_bound.Contains(b.Index)) continue;
            var since = _mangledSince.GetValueOrDefault(b.Index) + dt;
            _mangledSince[b.Index] = since;
            if (since < MangledRules.BoundAfterSeconds) continue;
            var living = party.Active.Where(a => !a.Downed).Select(a => DungeonBuilder.WorldToCell(a.GlobalPosition)).ToList();
            var cell = MangledRules.BoundCell(layout, exploration.Seen, living, Dungeon.Generation.HazardPlanner.CriticalPath(layout));
            _bound.Add(b.Index); // decided once: a body with nowhere to go stays where it fell
            if (cell is not { } at) continue;
            b.Shove(main.Builder.Surface(at) + Vector3.Up * 0.3f);
            EventBus.Instance.Log($"{b.State.Name} is bound in the dark ahead.");
            Trace.Add($"bound:{b.State.Name}");
        }
    }

    public int RejectedPoses { get; private set; }
    private readonly Dictionary<int, Vector3> _firstPose = new();
    private readonly HashSet<int> _moved = new();

    public void Broadcast(NetMessage m, int except = -1)
    {
        var bytes = NetCodec.Encode(m);
        foreach (var peer in Handshake.InGamePeers) if (peer != except) _t.Send(peer, bytes);
        foreach (var peer in Handshake.WelcomedPeers) if (peer != except) HoldForJoiner(peer, bytes);
    }

    /// <summary>
    /// Everything that happens while a player is between its Welcome and its Ready (building the floor): a boss falling,
    /// a door opening, a bot's pickup, a floor change. It used to be lost (only in-game peers hear broadcasts) and stayed
    /// wrong until a reconnect; it is held here and delivered in order the moment the player is in (<see cref="OnPlayerReady"/>).
    /// </summary>
    private readonly Dictionary<int, List<byte[]>> _joinQueue = new();
    private readonly HashSet<int> _joinQueueOverflowed = new();
    /// <summary>Well under the socket's queued-packet limit, so the flush and the catch-up behind it always fit in one frame.</summary>
    private const int JoinQueueCap = 600;

    /// <summary>Moments a joiner has no use for once they are past: the next Vitals, snapshot or swing replaces them.</summary>
    private static bool Transient(byte kindByte) => (MsgKind)(kindByte & 0x7F) is MsgKind.Vitals or MsgKind.Sfx or MsgKind.EnemyHurt
        or MsgKind.HitFxEvt or MsgKind.SwingEvt or MsgKind.Emote or MsgKind.ProjectileSpawned or MsgKind.ProjectileEnded or MsgKind.Bark
        or MsgKind.FloorClock or MsgKind.CartCorrection or MsgKind.Leap; // a stale leap must not fly twice: the rail catch-up resends the ones in the air

    private static bool FloorKind(byte kindByte) => (MsgKind)(kindByte & 0x7F) is MsgKind.FloorChangeBegin or MsgKind.FloorChange;

    private void HoldForJoiner(int peer, byte[] bytes)
    {
        if (bytes.Length == 0 || Transient(bytes[0])) return;
        if (!_joinQueue.TryGetValue(peer, out var q)) _joinQueue[peer] = q = new List<byte[]>();
        if (q.Count >= JoinQueueCap)
        {
            // a long join through a busy fight: keep only the floor changes (the one thing the catch-up cannot redo), and let
            // the catch-up, the prop state and the world checks sent on Ready repair the rest
            if (_joinQueueOverflowed.Add(peer)) GD.Print($"[net-server] peer {peer}'s join queue is full: keeping only floor changes");
            q.RemoveAll(b => !FloorKind(b[0]));
            if (q.Count >= JoinQueueCap) q.RemoveRange(0, q.Count - JoinQueueCap / 2);
        }
        if (!_joinQueueOverflowed.Contains(peer) || FloorKind(bytes[0])) q.Add(bytes);
    }

    private void FlushJoinQueue(int peer)
    {
        var overflowed = _joinQueueOverflowed.Remove(peer);
        if (!_joinQueue.Remove(peer, out var q)) return;
        foreach (var bytes in q) _t.Send(peer, bytes);
        if (q.Count > 0) GD.Print($"[net-server] delivered {q.Count} message(s) held while peer {peer} was joining{(overflowed ? " (overflowed: the catch-up repairs the rest)" : "")}");
    }

    public void SendTo(int peer, NetMessage m) => _t.Send(peer, NetCodec.Encode(m));

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        if (_logBatch.Count > 0) { Broadcast(new LogMsg(_logBatch.ToList())); _logBatch.Clear(); }
        _fullSnapshotTimer -= dt;
        var full = _fullSnapshotTimer <= 0f;
        if (full) _fullSnapshotTimer = Protocol.FullSnapshotSeconds;
        // by elapsed time, not ticks (each Advance runs every step, before the test that may skip the send)
        if (_snapshotCadence.Advance(delta) > 0) foreach (var peer in Handshake.InGamePeers) SendSnapshotTo(peer, full);
        if (_vitalsCadence.Advance(delta) > 0 && Handshake.InGamePeers.Any()) SendVitals();
        if (_syncCadence.Advance(delta) > 0 && Handshake.InGamePeers.Any()) FlushSync();
        if (_objectiveCadence.Advance(delta) > 0 && Handshake.InGamePeers.Any() && Adventure.Objectives.ObjectiveController.Instance is { } objective) Broadcast(new ObjectiveStateMsg(objective.Capture()));
        TickFloorClock(delta);
        if (_spawned.Count > 0)
        {
            foreach (var e in _spawned)
                if (GodotObject.IsInstanceValid(e)) Broadcast(new EnemySpawned(EnemyDto(e)));
            _spawned.Clear();
        }
        TickBound(dt);
        TickLootCheck(dt);
        TickReadyCheck(delta);
        _statusTimer -= dt;
        if (_statusTimer <= 0f) { _statusTimer = 10f; WriteStatus(); }
        _autosaveTimer += dt;
        if (_autosaveTimer >= NetSession.Instance.AutosaveSeconds && Registry.OnlineCount > 0) { _autosaveTimer = 0f; Autosave("timer"); }
    }

    private Cadence _snapshotCadence = new(1.0 / Protocol.SnapshotHz);
    private Cadence _vitalsCadence = new(1.0 / Protocol.VitalsHz);
    private Cadence _syncCadence = new(0.1);
    private Cadence _objectiveCadence = new(2.0);
    private Cadence _floorClockCadence = new(FloorClockSeconds(railed: false));

    /// <summary>How often the floor clock's pair goes out: every second on a railed floor (the hammers and the pour must agree), every two elsewhere.</summary>
    private static double FloorClockSeconds(bool railed) => railed ? 1.0 : 2.0;

    /// <summary>The floor clock's pair to everyone in game, on its cadence (<see cref="FloorClockMsg"/>; transient, so not held for joiners).</summary>
    private void TickFloorClock(double delta)
    {
        var main = Main.Instance;
        var period = FloorClockSeconds(main?.Builder.Layout?.Rail != null);
        if (_floorClockCadence.Period != period) _floorClockCadence = new Cadence(period);
        if (_floorClockCadence.Advance(delta) == 0 || main == null || !main.InGame || main.Rebuilding || _floorChanging || !Handshake.InGamePeers.Any()) return;
        Broadcast(FloorClockNow());
    }

    private void SendSnapshotTo(int peer, bool full)
    {
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        var bodies = party.Active.ToList();
        var enemies = new List<EntityState>();
        foreach (var e in root.GetChildren().OfType<Enemy>())
        {
            var pos = e.GlobalPosition;
            var moved = !_lastSent.TryGetValue(e.NetId, out var last) || last.DistanceSquaredTo(pos) > 0.0004f;
            var near = bodies.Any(b => b.GlobalPosition.DistanceSquaredTo(pos) < Protocol.SnapshotInterestRadius * Protocol.SnapshotInterestRadius);
            byte flags = 0;
            if (e.IsAlive) flags |= EntityState.FlagAlive;
            if (e.IsHostile) flags |= EntityState.FlagHostile;
            if (!e.IsUnaware) flags |= EntityState.FlagAware;
            if (e.IsBoss) flags |= EntityState.FlagBoss;
            if (e.InWater) flags |= EntityState.FlagInWater;
            var flagsChanged = !_flagsSent.TryGetValue((peer, e.NetId), out var sentFlags) || sentFlags != flags; // an enemy turned unaware standing still goes out now
            if (!SnapshotRules.SendEnemy(full, moved, near && !e.IsUnaware, flagsChanged)) continue;
            _lastSent[e.NetId] = pos;
            _flagsSent[(peer, e.NetId)] = flags;
            enemies.Add(new EntityState(e.NetId, new NetVec(pos.X, pos.Y, pos.Z), e.Rotation.Y, e.Hp, EnemyStateCodes.Encode(e.State.Name), flags));
        }
        var npcs = root.GetChildren().OfType<Npc>().Select(n =>
        {
            byte f = 0;
            if (n.Dead) f |= NpcState.FlagDead;
            if (n.Escorting) f |= NpcState.FlagEscorting;
            if (n.Rescued) f |= NpcState.FlagRescued;
            if (n.Downed) f |= NpcState.FlagDowned;
            if (n.Captured) f |= NpcState.FlagCaptured;
            if (n.Jailed) f |= NpcState.FlagJailed;
            if (n.Fled) f |= NpcState.FlagFled;
            if (n.Seated) f |= NpcState.FlagSeated;
            return new NpcState(n.Id, new NetVec(n.GlobalPosition.X, n.GlobalPosition.Y, n.GlobalPosition.Z), n.Rotation.Y, f);
        }).ToArray();
        var players = bodies.Select(b =>
        {
            var flags = PoseFlags.None;
            if (b.Crouching) flags |= PoseFlags.Crouch;
            if (b.Guarding) flags |= PoseFlags.Guard;
            if (b.IsOnFloor() || b.Remote?.OnFloor == true) flags |= PoseFlags.OnFloor;
            if (b.Downed) flags |= PoseFlags.Downed;
            if (_bound.Contains(b.Index)) flags |= PoseFlags.Bound;
            var flags2 = PoseFlags2.None;
            if (b.Seated) flags2 |= PoseFlags2.Seated;
            if (b.Seated && b.Pumping) flags2 |= PoseFlags2.Pumping;
            return new PlayerPoseState(b.Index, new NetVec(b.GlobalPosition.X, b.GlobalPosition.Y, b.GlobalPosition.Z), b.Rotation.Y, b.Camera.Rotation.X,
                new NetVec(b.Velocity.X, b.Velocity.Y, b.Velocity.Z), flags, b.Seated ? b.SeatYaw : 0f, flags2);
        }).ToArray();
        _t.Send(peer, NetCodec.Encode(new WorldSnapshot(NetSession.Instance.Tick, NetSession.Instance.Now, enemies.ToArray(), npcs, players)), NetChannel.State);
    }

    /// <summary>What each member's last broadcast PartySync held (<see cref="MemberSync"/>): its signature, its effects and when.</summary>
    private readonly Dictionary<int, (string Signature, List<Save.StatusDto> Effects, double At)> _memberSent = new();

    /// <summary>
    /// Members whose sheet changed and a bag that changed go out whole, a few times a second at most. A member goes out
    /// when its content differs from the last send (gear, level, xp, points, skills, an effect starting, ending or
    /// stacking), when an effect was refreshed, or when <see cref="_dirtyMembers"/> forces it (a bot just called in); never
    /// for a swing or a hit alone, whose hp, stamina, mana and cooldown are the Vitals' job.
    /// </summary>
    private void FlushSync()
    {
        var party = Main.Instance?.Party;
        if (party == null) return;
        var now = Time.GetTicksMsec() / 1000.0;
        for (var i = 0; i < party.Members.Length; i++)
        {
            var dto = SaveMapper.ToDto(party.Members[i]);
            var signature = MemberSync.Signature(dto);
            if (!_dirtyMembers.Contains(i) && _memberSent.TryGetValue(i, out var sent) && sent.Signature == signature && !MemberSync.TimersDrifted(sent.Effects, sent.At, dto.Effects, now)) continue;
            _memberSent[i] = (signature, dto.Effects, now);
            Broadcast(new PartySync(i, dto));
        }
        _dirtyMembers.Clear();
        var sig = string.Join("|", party.Bag.Items.Select(it => $"{party.Bag.IndexOf(it)}:{it.Id:N}:{it.StackCount}"));
        if (_bagDirty || sig != _bagSignature)
        {
            _bagSignature = sig;
            _bagDirty = false;
            Broadcast(new BagSync(SaveMapper.ToDto(party.Bag)));
        }
        if (party.Gold != _goldSent)
        {
            _goldSent = party.Gold;
            Broadcast(new GoldSync(party.Gold));
        }
    }

    private byte[]? _vitalsSent;
    private double _vitalsSentAt;

    /// <summary>
    /// The Vitals go out at <see cref="Protocol.VitalsHz"/> while anything in them changes (a fight, regeneration, a
    /// cooldown running down), and once every <see cref="Protocol.VitalsHeartbeatSeconds"/> while the whole party stands
    /// still, which is all a joiner or a screen that missed one needs. An idle party no longer streams identical copies.
    /// </summary>
    private void SendVitals()
    {
        var vitals = BuildVitals();
        var bytes = NetCodec.Encode(vitals);
        var now = Time.GetTicksMsec() / 1000.0;
        if (_vitalsSent != null && now - _vitalsSentAt < Protocol.VitalsHeartbeatSeconds && bytes.AsSpan().SequenceEqual(_vitalsSent)) return;
        _vitalsSent = bytes;
        _vitalsSentAt = now;
        Broadcast(vitals);
    }

    private Vitals BuildVitals()
    {
        var members = Main.Instance.Party.Members;
        var v = new MemberVitals[members.Length];
        for (var i = 0; i < members.Length; i++) v[i] = new MemberVitals(i, members[i].Hp, members[i].Stamina, members[i].Mana, members[i].CooldownRemaining, members[i].IsAlive);
        return new Vitals(v);
    }

    /// <summary>A small JSON file the Render front door serves as the status page.</summary>
    private void WriteStatus()
    {
        var path = NetSession.Instance.StatusFile;
        if (path == null) return;
        var game = Game.Instance;
        var players = string.Join(",", Registry.Online.Select(s => $"\"{s.Name.Replace("\"", "")}\""));
        var bots = string.Join(",", Registry.ActiveBots.Select(s => $"\"{s.Name.Replace("\"", "")}\""));
        var json = $"{{\"floor\":{game.CurrentFloor},\"online\":{Registry.OnlineCount},\"players\":[{players}],\"bots\":[{bots}],\"known\":{Registry.All.Count},\"uptime\":{(int)(Time.GetTicksMsec() / 1000)},\"layoutHash\":\"{Main.Instance?.Builder.Layout?.ContentHash() ?? 0}\",\"tick\":{NetSession.Instance.Tick},\"net\":{NetSession.Instance.Traffic?.ToJson(NetSession.Instance.Now) ?? "null"}}}";
        try { File.WriteAllText(ProjectSettings.GlobalizePath(path), json); } catch (Exception e) { GD.PushWarning($"[net-server] status file: {e.Message}"); }
    }
}
