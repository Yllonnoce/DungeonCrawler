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
        t.PeerDisconnected += p => { Handshake.OnPeerDisconnected(p); GD.Print($"[net-server] peer {p} disconnected"); if (NetSession.Instance.QuitWhenEmpty && Registry.OnlineCount == 0) CallDeferred(nameof(QuitEmpty)); };
        t.Received += OnReceived;
        var bus = EventBus.Instance;
        bus.Message += line => _logBatch.Add(line);
        bus.Sfx += cue => { if (!LocalCues.Contains(cue) && !Quiet) Broadcast(new SfxMsg(cue)); };
        bus.ObjectiveChanged += text => Broadcast(new ObjectiveMsg(text));
        bus.MemberChanged += i => _dirtyMembers.Add(i);
        bus.LootSpawned += drop => { if (!Quiet) Broadcast(new LootSpawned(SaveMapper.ToDto(drop.Item), Vec(drop.GlobalPosition), drop.KeyIndex, drop.QuestId)); };
        bus.LootRemoved += (id, by) => { if (!Quiet) Broadcast(new LootRemoved(id, by)); };
        bus.PropChanged += (r, what) => { if (!Quiet) Broadcast(new PropEvt(r, what)); };
        bus.TrapPlaced += trap => { if (!Quiet) Broadcast(new TrapPlacedEvt(new PlacedTrapDto { Kind = trap.Kind.ToString(), X = trap.GlobalPosition.X, Y = trap.GlobalPosition.Y, Z = trap.GlobalPosition.Z })); };
        bus.EnemyHurt += (e, amount, crit) => { if (!Quiet) Broadcast(new EnemyHurt(e.NetId, amount, crit, e.Hp)); };
        bus.EnemyDied += e => { if (!Quiet) { Broadcast(new EnemyDied(e.NetId, -1)); Trace.Add("kill"); } };
        bus.ProjectileFired += p => { if (!Quiet) Broadcast(new ProjectileSpawned(0, Vec(p.GlobalPosition), Vec(p.Velocity), p.IsArrow, p.Color.ToRgba32(), p.SourceMember)); };
        bus.MemberDowned += i => Broadcast(new MemberDownedEvt(i));
        bus.MemberRevived += i => { _mangledSince.Remove(i); _bound.Remove(i); Broadcast(new MemberRevivedEvt(i)); };
        bus.LevelUp += i => Broadcast(new LevelUpEvt(i));
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
    }

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
        Broadcast(new FloorChangeBegin(floor, floor > Game.Instance.CurrentFloor ? "Descending..." : "The dungeon shifts..."));
    }

    private void OnFloorBuilt(int floor, int delta, bool firstVisit)
    {
        var main = Main.Instance;
        var game = Game.Instance;
        var party = main.Party;
        var poses = party.Adventurers.Select(a => new PoseDto { X = a.GlobalPosition.X, Y = a.GlobalPosition.Y, Z = a.GlobalPosition.Z, Yaw = a.Rotation.Y }).ToList();
        game.Adventure.FloorPlans.TryGetValue(floor, out var plan);
        game.Floors.TryGetValue(floor, out var state);
        var snapshot = main.BuildSnapshotFor.TryGetValue(floor, out var s) ? s : party.Snapshot();
        foreach (var peer in Handshake.InGamePeers)
            _t.Send(peer, NetCodec.Encode(new FloorChange(floor, delta, plan, snapshot, state, poses, DynamicEnemies(main.Builder.FloorRoot), firstVisit, main.Builder.Layout?.ContentHash() ?? 0, FloorObjectiveId(main))));
        _lastSent.Clear();
        _firstPose.Clear();
        Trace.Add($"floor:{floor}");
        WriteStatus();
        CallDeferred(nameof(AutosaveFloor));
    }

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
        }
        SendSnapshotTo(slot.PeerId, full: true);
        foreach (var q in AdventureDirector.Instance.Quests.Values) SendTo(slot.PeerId, new QuestSync(q));
        SendTo(slot.PeerId, new ObjectiveMsg(AdventureDirector.Instance.CurrentObjective));
        if (Adventure.Objectives.ObjectiveController.Instance is { } objective) SendTo(slot.PeerId, new ObjectiveStateMsg(objective.Capture()));
    }

    private static string FloorObjectiveId(Main main) => Dungeon.Generation.FloorObjectives.Id(main.Builder.Layout?.Objective?.Kind ?? Dungeon.Generation.FloorObjectiveKind.None);

    /// <summary>Cues the acting client already played for itself, and UI noise that is nobody else's business.</summary>
    private static readonly HashSet<string> LocalCues = new() { "swing", "swing_heavy", "bow", "cast", "ui_click", "ui_open", "footstep", "miss" };
    private static bool Quiet => Main.Instance?.Rebuilding == true;
    private static NetVec Vec(Vector3 v) => new(v.X, v.Y, v.Z);
    private int _attackLogs;
    private readonly HashSet<int> _dirtyMembers = new();
    private bool _bagDirty;
    private string _bagSignature = "";

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
        };
        var snapshot = main.BuildSnapshotFor.TryGetValue(floor, out var s) ? s : main.Party.Snapshot();
        var objective = AdventureDirector.Instance?.CurrentObjective ?? "";
        Trace.Add(slot.ParkedPose != null || slot.LastSeenUtc != "" ? $"rejoin:{slot.Name}" : $"join:{slot.Name}");
        GD.Print($"[net-server] {slot.Name} {(slot.LastSeenUtc != "" ? "rejoins" : "joins")} as member {slot.MemberIndex} ({slot.ClassId}) on floor {floor}");
        return new Welcome(peer, slot.MemberIndex, game.GameSeed, floor, game.PlaySeconds, game.Victory, adventure, snapshot, state,
            DynamicEnemies(root), main.Party.CaptureState(), Registry.ToDtos(), objective, NetSession.Instance.Tick, layout?.ContentHash() ?? 0, FloorObjectiveId(main));
    }

    public static List<DynamicEnemyDto> DynamicEnemies(Node3D? root)
    {
        var list = new List<DynamicEnemyDto>();
        if (root == null) return list;
        foreach (var e in root.GetChildren().OfType<Enemy>())
            if (e.NetId >= Enemy.DynamicNetIdBase && e.IsAlive)
                list.Add(new DynamicEnemyDto(e.NetId, e.Profile.Id, e.GlobalPosition.X, e.GlobalPosition.Y, e.GlobalPosition.Z, e.Rotation.Y, e.Hp, e.HordeId));
        return list;
    }

    private void OnPlayerReady(PlayerSlot slot)
    {
        var main = Main.Instance;
        var party = main.Party;
        var index = slot.MemberIndex;
        Vector3? at = null;
        Vector3? look = null;
        if (slot.ParkedPose is { } p && slot.ParkedFloor == Game.Instance.CurrentFloor) at = new Vector3(p.X, p.Y, p.Z);
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
        SendTo(slot.PeerId, new ForcePose(index, new PoseDto { X = body.GlobalPosition.X, Y = body.GlobalPosition.Y, Z = body.GlobalPosition.Z, Yaw = body.Rotation.Y }));
        EventBus.Instance.Log($"{slot.Name} joins the party.");
        Broadcast(new PlayerJoined(slot.ToDto()), except: slot.PeerId);
        SendSnapshotTo(slot.PeerId, full: true);
        Trace.Add($"ready:{slot.Name}");
        PlayerReady?.Invoke(slot);
        WriteStatus();
    }

    private void OnPlayerLeft(PlayerSlot slot)
    {
        var party = Main.Instance?.Party;
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
            SaveManager.Write(NetSession.Instance.SaveSlot, main.BuildSaveData("Server"));
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
            var reply = Handshake.OnMessage(from, m, out var drop);
            if (reply != null) _t.Send(from, NetCodec.Encode(reply));
            if (drop) (_t as WebSocketTransport)?.Disconnect(from);
            return;
        }
        var slot = Registry.ByPeer(from);
        if (slot == null) return;
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
            case Ping ping: _t.Send(from, NetCodec.Encode(new Pong(ping.Tick, ping.SentAt, NetSession.Instance.Now))); break;
            default:
                HandleIntent(slot, m);
                IntentReceived?.Invoke(from, m);
                break;
        }
    }

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
            case AttackIntent a:
            {
                // aim exactly where the player aimed; the body stays where its poses put it
                body.Rotation = new Vector3(0, a.Yaw, 0);
                body.Camera.Rotation = new Vector3(Mathf.Clamp(a.Pitch, Mathf.DegToRad(-Adventurer.MaxPitchDegrees), Mathf.DegToRad(Adventurer.MaxPitchDegrees)), 0, 0);
                var wanted = new Vector3(a.Position.X, a.Position.Y, a.Position.Z);
                if (wanted.DistanceTo(body.Camera.GlobalPosition) < 1.5f && body.Remote != null) { body.Remote.Position = body.GlobalPosition + (wanted - body.Camera.GlobalPosition); body.GlobalPosition = body.Remote.Position; }
                body.Attack(a.Heavy, a.Perfect);
                _dirtyMembers.Add(slot.MemberIndex);
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
                var resolved = NetRefResolver.Resolve(root, i.Ref);
                if (resolved is not { } node || node is not IInteractable target)
                {
                    if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: not found");
                    break;
                }
                var reach = node is Items.LootDrop ? 3f : 3.5f;
                var dist = Math.Min(node.GlobalPosition.DistanceTo(body.GlobalPosition), node.GlobalPosition.DistanceTo(body.Camera.GlobalPosition));
                if (NetSession.Instance.TestServer) GD.Print($"[net-server] interact {i.Ref} from {slot.Name}: {dist:0.0} m (reach {reach}) body={body.GlobalPosition:0.0} node={node.GlobalPosition:0.0}");
                if (dist > reach) break;
                target.Interact(party, body);
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
            case ShoutIntent:
                if (!body.Downed) break;
                EventBus.Instance.Log($"{slot.Name} needs help!");
                Trace.Add("shout");
                break;
            case BagMoveIntent bm:
                if (!OwnsSlots(slot, bm.From, bm.To)) break;
                party.BagMove(bm.From, bm.To, body);
                _bagDirty = true;
                break;
            case QuickActionIntent q:
                if (!OwnsSlots(slot, q.Slot, q.Slot)) break;
                party.BagQuick(q.Slot, slot.MemberIndex, body);
                _bagDirty = true;
                break;
            case CycleAbilityIntent c:
                party.CycleAbility(slot.MemberIndex, Math.Sign(c.Dir));
                break;
            case AllocateStatIntent st:
                if (Enum.TryParse<Characters.Stat>(st.Stat, out var stat)) party.AllocateStat(slot.MemberIndex, stat);
                break;
            case LearnSkillIntent ls:
                if (Game.Instance.Skills.Skills.TryGetValue(ls.SkillId, out var skill)) party.LearnSkill(slot.MemberIndex, skill);
                break;
            case DialogueChoiceIntent d:
                EventBus.Instance.RaiseDialogueEffect(d.NpcId, d.Effect, d.Flag);
                break;
        }
    }

    /// <summary>A player may rearrange the shared bag and their own gear, nobody else's.</summary>
    private static bool OwnsSlots(PlayerSlot slot, BagSlot a, BagSlot b)
        => (!a.IsEquip || a.Member == slot.MemberIndex) && (!b.IsEquip || b.Member == slot.MemberIndex);

    private void ApplyPose(PlayerSlot slot, Pose pose)
    {
        var party = Main.Instance?.Party;
        if (party == null || slot.MemberIndex >= party.Adventurers.Length) return;
        var body = party.Adventurers[slot.MemberIndex];
        if (body.Control != ControlKind.Remote) return;
        var at = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
        // a body cannot be steered off the map: keep it on floor the layout knows
        if (Main.Instance!.Builder.Layout is { } layout && !layout.IsWalkable(DungeonBuilder.WorldToCell(at)) && !Main.Instance.Builder.IsUnderAt(at)) { RejectedPoses++; if (NetSession.Instance.TestServer && RejectedPoses <= 12) GD.Print($"[net-server] pose from {slot.Name} rejected at {at:0.0} cell {DungeonBuilder.WorldToCell(at)} ({layout[DungeonBuilder.WorldToCell(at)]})"); return; }
        // nor into a Press that is holding shut: there is no room for a body between the slabs
        if (Main.Instance.Builder.Hazards.OfType<Dungeon.Props.Press>().Any(p => p.Phase == PressPhase.Hold && p.Placement.Cells.Contains(DungeonBuilder.WorldToCell(at)))) { RejectedPoses++; return; }
        body.Remote ??= new RemotePose();
        body.Remote.Position = at;
        body.Remote.Yaw = pose.Yaw;
        body.Remote.Pitch = pose.Pitch;
        body.Remote.Velocity = new Vector3(pose.Velocity.X, pose.Velocity.Y, pose.Velocity.Z);
        body.Remote.Crouch = pose.Flags.HasFlag(PoseFlags.Crouch);
        body.Remote.Guard = pose.Flags.HasFlag(PoseFlags.Guard);
        body.Remote.OnFloor = pose.Flags.HasFlag(PoseFlags.OnFloor);
        if (!_moved.Contains(slot.MemberIndex) && _firstPose.TryGetValue(slot.MemberIndex, out var first) && first.DistanceTo(at) > 1f) { _moved.Add(slot.MemberIndex); Trace.Add($"moved:{slot.Name}"); }
        _firstPose.TryAdd(slot.MemberIndex, at);
    }

    /// <summary>A hazard entered a phase: tell every client which, when and how far along, so they animate in step.</summary>
    private void OnHazardPhase(int index, byte phase)
    {
        var tick = NetSession.Instance.Tick;
        foreach (var node in Main.Instance?.Builder.Hazards ?? Array.Empty<Node3D>())
        {
            switch (node)
            {
                case Dungeon.Props.Press p when p.Index == index:
                    Broadcast(new HazardPhaseMsg(index, (byte)HazardKind.Press, phase, tick, (int)(HazardRules.PressDuration(p.Phase) * 60), p.Jammed, p.Spent));
                    Trace.Add($"hazard:{p.Phase}");
                    return;
                case Dungeon.Props.SpikeBed b when b.Index == index:
                    Broadcast(new HazardPhaseMsg(index, (byte)HazardKind.SpikeBed, phase, tick, (int)(HazardRules.SpikeDuration(b.Phase, b.Variant) * 60), false, b.Spent));
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
            b.Teleport(main.Builder.Surface(at) + Vector3.Up * 0.3f);
            if (b.Remote != null) b.Remote.Position = b.GlobalPosition;
            Broadcast(new ForcePose(b.Index, new PoseDto { X = b.GlobalPosition.X, Y = b.GlobalPosition.Y, Z = b.GlobalPosition.Z, Yaw = b.Rotation.Y }));
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
    }

    public void SendTo(int peer, NetMessage m) => _t.Send(peer, NetCodec.Encode(m));

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var tick = NetSession.Instance.Tick;
        if (_logBatch.Count > 0) { Broadcast(new LogMsg(_logBatch.ToList())); _logBatch.Clear(); }
        _fullSnapshotTimer -= dt;
        var full = _fullSnapshotTimer <= 0f;
        if (full) _fullSnapshotTimer = Protocol.FullSnapshotSeconds;
        if (tick % (60 / Protocol.SnapshotHz) == 0) foreach (var peer in Handshake.InGamePeers) SendSnapshotTo(peer, full);
        if (tick % (60 / Protocol.VitalsHz) == 0 && Handshake.InGamePeers.Any()) Broadcast(BuildVitals());
        if (tick % 6 == 0 && Handshake.InGamePeers.Any()) FlushSync();
        if (tick % 120 == 0 && Handshake.InGamePeers.Any() && Adventure.Objectives.ObjectiveController.Instance is { } objective) Broadcast(new ObjectiveStateMsg(objective.Capture()));
        if (_spawned.Count > 0)
        {
            foreach (var e in _spawned)
                if (GodotObject.IsInstanceValid(e)) Broadcast(new EnemySpawned(new DynamicEnemyDto(e.NetId, e.Profile.Id, e.GlobalPosition.X, e.GlobalPosition.Y, e.GlobalPosition.Z, e.Rotation.Y, e.Hp, e.HordeId)));
            _spawned.Clear();
        }
        TickBound(dt);
        _statusTimer -= dt;
        if (_statusTimer <= 0f) { _statusTimer = 10f; WriteStatus(); }
        _autosaveTimer += dt;
        if (_autosaveTimer >= NetSession.Instance.AutosaveSeconds && Registry.OnlineCount > 0) { _autosaveTimer = 0f; Autosave("timer"); }
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
            if (!full && !moved && !(near && !e.IsUnaware)) continue;
            _lastSent[e.NetId] = pos;
            byte flags = 0;
            if (e.IsAlive) flags |= EntityState.FlagAlive;
            if (e.IsHostile) flags |= EntityState.FlagHostile;
            if (!e.IsUnaware) flags |= EntityState.FlagAware;
            if (e.IsBoss) flags |= EntityState.FlagBoss;
            if (e.InWater) flags |= EntityState.FlagInWater;
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
            return new PlayerPoseState(b.Index, new NetVec(b.GlobalPosition.X, b.GlobalPosition.Y, b.GlobalPosition.Z), b.Rotation.Y, b.Camera.Rotation.X,
                new NetVec(b.Velocity.X, b.Velocity.Y, b.Velocity.Z), flags);
        }).ToArray();
        _t.Send(peer, NetCodec.Encode(new WorldSnapshot(NetSession.Instance.Tick, NetSession.Instance.Now, enemies.ToArray(), npcs, players)), NetChannel.State);
    }

    /// <summary>Members whose sheet changed and a bag that changed go out whole, a few times a second at most.</summary>
    private void FlushSync()
    {
        var party = Main.Instance?.Party;
        if (party == null) return;
        foreach (var i in _dirtyMembers)
            if (i >= 0 && i < party.Members.Length) Broadcast(new PartySync(i, SaveMapper.ToDto(party.Members[i])));
        _dirtyMembers.Clear();
        var sig = string.Join("|", party.Bag.Items.Select(it => $"{party.Bag.IndexOf(it)}:{it.Id:N}:{it.StackCount}"));
        if (_bagDirty || sig != _bagSignature)
        {
            _bagSignature = sig;
            _bagDirty = false;
            Broadcast(new BagSync(SaveMapper.ToDto(party.Bag)));
        }
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
        var json = $"{{\"floor\":{game.CurrentFloor},\"online\":{Registry.OnlineCount},\"players\":[{players}],\"known\":{Registry.All.Count},\"uptime\":{(int)(Time.GetTicksMsec() / 1000)},\"layoutHash\":\"{Main.Instance?.Builder.Layout?.ContentHash() ?? 0}\",\"tick\":{NetSession.Instance.Tick}}}";
        try { File.WriteAllText(ProjectSettings.GlobalizePath(path), json); } catch (Exception e) { GD.PushWarning($"[net-server] status file: {e.Message}"); }
    }
}
