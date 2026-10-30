using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// A player's machine on a server: runs the join handshake, sends the local body's pose, and applies what the
/// server streams back (enemy and NPC poses, other players, vitals, log lines, events). The floor itself is built
/// locally from the seed and plan the Welcome carried.
/// </summary>
public partial class ClientWorld : Node
{
    private INetTransport _t = null!;
    private ClientHandshake _h = null!;
    private readonly Dictionary<int, PoseBuffer> _players = new();
    private Dictionary<int, Enemy> _enemies = new();
    private Dictionary<string, Npc> _npcs = new();
    private float _reindexTimer;
    private Vector3 _lastSentPos;
    private float _lastSentYaw;
    private bool _readySent;
    public Welcome? Welcome => _h.Welcome;
    public ClientHandshake.Phase Phase => _h.State;
    public int MemberIndex => _h.Welcome?.YourMemberIndex ?? -1;
    public int SnapshotsSeen { get; private set; }
    public double Rtt { get; private set; }
    /// <summary>What the scripted client test saw, in order.</summary>
    public List<string> Trace { get; } = new();
    public event Action<NetMessage>? MessageReceived;

    public void Start(INetTransport t, string playerName, string classId)
    {
        _t = t;
        _h = new ClientHandshake(playerName, classId, Protocol.Version.ToString());
        _h.Connecting();
        t.ConnectedToServer += () => { NetSession.Instance.SetStatus("connected, joining..."); _t.Send(NetPeers.Server, NetCodec.Encode(_h.OnConnected())); };
        t.ConnectionFailed += () => { _h.OnConnectionFailed(); NetSession.Instance.SetStatus(_h.Reason ?? "connection failed"); Trace.Add("failed"); Main.Instance.OnClientLeft(_h.Reason); };
        t.ServerDisconnected += () => { _h.OnServerDisconnected(); NetSession.Instance.SetStatus(_h.Reason ?? "server closed the connection"); Trace.Add("dropped"); Main.Instance.OnClientLeft(_h.Reason); };
        t.Received += OnReceived;
    }

    private void OnReceived(int from, byte[] bytes)
    {
        NetMessage m;
        try { m = NetCodec.Decode(bytes); }
        catch (NetCodecException e) { GD.PushWarning($"[net] bad packet: {e.Message}"); return; }
        if (_h.State != ClientHandshake.Phase.InGame)
        {
            if (!_h.OnMessage(m)) return;
            if (_h.State == ClientHandshake.Phase.Rejected) { NetSession.Instance.SetStatus(_h.Reason ?? "rejected"); Trace.Add("rejected"); Main.Instance.OnClientLeft(_h.Reason); return; }
            if (_h.State == ClientHandshake.Phase.Welcomed && _h.Welcome is { } w)
            {
                NetSession.Instance.SetStatus($"welcome: floor {w.CurrentFloor}, member {w.YourMemberIndex}, {w.Players.Count} known players");
                Trace.Add("welcome");
                Main.Instance.StartClientSession(w);
            }
            return;
        }
        MessageReceived?.Invoke(m);
        switch (m)
        {
            case WorldSnapshot s: ApplySnapshot(s); break;
            case Vitals v: ApplyVitals(v); break;
            case LogMsg log: foreach (var line in log.Lines) EventBus.Instance.Log(line); break;
            case ChatMsg chat: EventBus.Instance.Log($"[{chat.From}] {chat.Text}"); Trace.Add("chat"); break;
            case SfxMsg sfx: EventBus.Instance.PlaySfx(sfx.Name); break;
            case ObjectiveMsg o: EventBus.Instance.RaiseObjectiveChanged(o.Text); break;
            case PlayerJoined pj: OnPlayerJoined(pj.Player); break;
            case PlayerLeft pl: OnPlayerLeft(pl); break;
            case Pong pong: Rtt = NetSession.Instance.Now - pong.SentAt; break;
            case ForcePose fp: ApplyForcePose(fp); break;
            case PartySync ps: Main.Instance.Party.ApplyMemberDto(ps.Index, ps.Member); break;
            case BagSync bs: Main.Instance.Party.ApplyBagDto(bs.Bag); Trace.Add("bag"); break;
            case LootSpawned ls: OnLootSpawned(ls); break;
            case LootRemoved lr: OnLootRemoved(lr); break;
            case PropEvt pe: OnProp(pe); break;
            case TrapPlacedEvt tp: OnTrapPlaced(tp.Trap); break;
            case EnemyHurt eh: if (_enemies.TryGetValue(eh.NetId, out var hurt) && GodotObject.IsInstanceValid(hurt)) hurt.ClientHurt(eh.Amount, eh.Hp); break;
            case EnemyDied ed: if (_enemies.TryGetValue(ed.NetId, out var dead) && GodotObject.IsInstanceValid(dead)) dead.ClientDie(); Trace.Add("kill"); break;
            case ProjectileSpawned pr: OnProjectile(pr); break;
            case MemberDownedEvt md: if (md.Index < Main.Instance.Party.Adventurers.Length) Main.Instance.Party.Adventurers[md.Index].ShowDowned(); EventBus.Instance.RaiseMemberDowned(md.Index); break;
            case MemberRevivedEvt mr: if (mr.Index < Main.Instance.Party.Adventurers.Length) Main.Instance.Party.Adventurers[mr.Index].Revive(); break;
            case LevelUpEvt lu: EventBus.Instance.RaiseLevelUp(lu.Index); break;
            case EmoteEvt em: if (em.Emote == "victory" && em.Member >= 0 && em.Member < Main.Instance.Party.Adventurers.Length) Main.Instance.Party.Adventurers[em.Member].Celebrate(); break;
            case BossDefeatedEvt bd: OnBossDefeated(bd.Floor); break;
            case ObjectiveDoneEvt od: OnObjectiveDone(od); break;
            case ObjectiveStateMsg os: Adventure.Objectives.ObjectiveController.Instance?.ApplyNet(os.State); break;
            case FloorChangeBegin fb: Main.Instance.Overlay.Open("The dungeon stirs...", fb.Status); Main.Instance.Party.InputLocked = true; break;
            case FloorChange fc: OnFloorChange(fc); break;
            case QuestSync qs: AdventureDirector.Instance.Quests[qs.Quest.Id] = qs.Quest; EventBus.Instance.RaiseQuestChanged(qs.Quest); break;
            case NarrationMsg n:
                // the server sends both forms of a villain line; this screen shows the one its own setting asks for
                if (n.CleanText != null && !Settings.VillainCurses) EventBus.Instance.RaiseNarration(n.CleanText, n.CleanVoiceId);
                else EventBus.Instance.RaiseNarration(n.Text, n.VoiceId);
                break;
            case BarkMsg b: AdventureDirector.Instance.PlayBark(new Line { Text = b.Text, VoiceId = b.VoiceId }); break;
            case DialogueAnswer da: Main.Instance.Hud?.Dialogue.OnJoinAnswer(da.NpcId, da.Accepted); Trace.Add(da.Accepted ? "joined" : "refused"); break;
            case EnemySpawned es: OnEnemySpawned(es.Enemy); break;
            case HordeTriggeredEvt ht: Root?.GetChildren().OfType<HordeSpawner>().FirstOrDefault(h => h.Horde.Id == ht.Id)?.Show(); break;
            case HordeSpentEvt hs: Root?.GetChildren().OfType<HordeSpawner>().FirstOrDefault(h => h.Horde.Id == hs.Id)?.ShowSpent(); break;
            case AmbushSprungEvt asp: Root?.GetChildren().OfType<AmbushSpawner>().FirstOrDefault()?.ShowBurst(asp.Index); break;
            case HazardPhaseMsg hp: OnHazardPhase(hp); break;
            case PortalOpenedEvt po: Root?.GetChildren().OfType<PortalSpawner>().FirstOrDefault()?.ShowPortal(new Vector3(po.X, po.Y, po.Z)); break;
        }
    }

    private void ApplyForcePose(ForcePose fp)
    {
        var party = Main.Instance?.Party;
        if (party == null || fp.Member < 0 || fp.Member >= party.Adventurers.Length) return;
        var body = party.Adventurers[fp.Member];
        var at = new Vector3(fp.Pose.X, fp.Pose.Y, fp.Pose.Z);
        body.Teleport(at);
        body.Rotation = new Vector3(0, fp.Pose.Yaw, 0);
        if (fp.Member == MemberIndex) GetNodeOrNull<NetTestFlow>("../TestFlow")?.NotePlaced(at);
    }

    private static Node3D? Root => Main.Instance?.Builder.FloorRoot;

    /// <summary>Server tick minus ours, from the latest snapshot: turns a phase's start tick into seconds already elapsed here.</summary>
    private int _serverTickOffset;

    private void OnHazardPhase(HazardPhaseMsg hp)
    {
        var secondsIn = HazardSync.SecondsIn(NetSession.Instance.Tick, _serverTickOffset, hp.ServerTick, hp.DurationTicks);
        foreach (var node in Main.Instance?.Builder.Hazards ?? Array.Empty<Node3D>())
        {
            switch (node)
            {
                case Dungeon.Props.Press p when hp.Hazard == (byte)Dungeon.HazardKind.Press && p.Index == hp.Index:
                    p.ApplyPhase((Dungeon.PressPhase)hp.Phase, secondsIn, hp.Jammed, hp.Spent);
                    Trace.Add($"hazard:{p.Phase}");
                    return;
                case Dungeon.Props.SpikeBed b when hp.Hazard == (byte)Dungeon.HazardKind.SpikeBed && b.Index == hp.Index:
                    b.ApplyPhase((Dungeon.SpikePhase)hp.Phase, secondsIn, hp.Spent);
                    Trace.Add($"hazard:{b.Phase}");
                    return;
            }
        }
    }

    private void OnFloorChange(FloorChange fc)
    {
        Main.Instance.ApplyFloorChange(fc);
        Reindex();
        _players.Clear();
        _t.Send(NetPeers.Server, NetCodec.Encode(new Ready(fc.Floor)));
        Trace.Add($"floor:{fc.Floor}");
    }

    private void OnEnemySpawned(DynamicEnemyDto d)
    {
        var main = Main.Instance;
        if (main?.Builder.FloorRoot == null || _enemies.ContainsKey(d.NetId)) return;
        var e = main.Builder.SpawnEnemy(d.DefId, new Vector3(d.X, d.Y, d.Z), Game.Instance.CurrentFloor, netId: d.NetId);
        if (e == null) return;
        e.Rotation = new Vector3(0, d.Yaw, 0);
        if (d.HordeId != null) { e.SetSwarmDetail(); e.HordeId = d.HordeId; }
        _enemies[d.NetId] = e;
    }

    private void OnLootSpawned(LootSpawned ls)
    {
        if (Root is not { } root) return;
        var drop = GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn").Instantiate<Items.LootDrop>();
        drop.Item = Save.SaveMapper.FromDto(ls.Item, Game.Instance.Catalog);
        drop.KeyIndex = ls.KeyIndex;
        drop.QuestId = ls.QuestKey;
        root.AddChild(drop);
        drop.GlobalPosition = new Vector3(ls.Position.X, ls.Position.Y, ls.Position.Z);
        Trace.Add("loot");
    }

    private void OnLootRemoved(LootRemoved lr)
    {
        if (Root is not { } root) return;
        foreach (var d in root.GetChildren().OfType<Items.LootDrop>())
            if (d.Item.Id == lr.Id) { d.QueueFree(); break; }
    }

    private void OnProp(PropEvt pe)
    {
        if (Root is not { } root || NetRefResolver.Resolve(root, pe.Ref) is not { } node) return;
        switch (node, pe.What)
        {
            case (Dungeon.Props.Chest c, "opened"): c.MarkOpened(); Trace.Add("chest"); break;
            case (Dungeon.Props.Door d, "opened"): d.Open(silent: true); break;
            case (Dungeon.Props.Door d, "revealed"): d.Reveal(); break;
            case (Dungeon.Props.Door d, "closed"): d.Close(silent: true); break;
            case (Dungeon.Props.Lever l, "pulled"): l.Pull(silent: true); break;
            case (Dungeon.Props.Lever l, "reset"): l.Reset(silent: true); break;
            case (Dungeon.Props.SecretWall s, "revealed"): s.Reveal(); break;
            case (Dungeon.Props.SecretWall s, "opened"): s.Open(silent: true); break;
            case (Dungeon.Props.Trap t, "revealed"): t.Reveal(); break;
            case (Dungeon.Props.Press p, "revealed"): p.Plate.Reveal(); break;
            case (Dungeon.Props.Trap t, "disarmed"): t.Disarm(); break;
            case (Dungeon.Props.Trap t, "sprung"): t.ShowSprung(); break;
            case (Dungeon.Props.Breakable b, "broken"): b.Vanish(); break;
        }
    }

    private void OnTrapPlaced(Save.PlacedTrapDto dto)
    {
        if (Root is not { } root || !Enum.TryParse<Dungeon.TrapKind>(dto.Kind, out var kind)) return;
        var trap = GD.Load<PackedScene>("res://scenes/Props/Trap.tscn").Instantiate<Dungeon.Props.Trap>();
        trap.Kind = kind;
        trap.Floor = Game.Instance.CurrentFloor;
        trap.PlacedByParty = true;
        root.AddChild(trap);
        trap.GlobalPosition = new Vector3(dto.X, dto.Y, dto.Z);
    }

    private void OnProjectile(ProjectileSpawned pr)
    {
        if (Root is not { } root) return;
        if (pr.SourceMember == MemberIndex && !pr.Arrow) { /* our own spell: the swing already showed; still show the bolt */ }
        var p = GD.Load<PackedScene>("res://scenes/Props/Projectile.tscn").Instantiate<Combat.Projectile>();
        root.AddChild(p);
        p.GlobalPosition = new Vector3(pr.Position.X, pr.Position.Y, pr.Position.Z);
        p.SetupCosmetic(new Vector3(pr.Velocity.X, pr.Velocity.Y, pr.Velocity.Z), pr.Arrow, new Color(pr.Color));
    }

    private void OnBossDefeated(int floor)
    {
        if (Root?.GetNodeOrNull<Dungeon.Stairs>("DownStairs") is { } stairs) stairs.Unlock();
        EventBus.Instance.RaiseBossDefeated(floor);
        if (Dungeon.TierRules.IsFinalBossFloor(floor)) { Game.Instance.Victory = true; Main.Instance.Hud?.ShowVictory(); }
    }

    private void OnObjectiveDone(ObjectiveDoneEvt od)
    {
        if (Root?.GetNodeOrNull<Dungeon.Stairs>("DownStairs") is { } stairs) stairs.Unlock();
        Adventure.Objectives.ObjectiveController.Instance?.MarkCompletedFromServer();
        if (Dungeon.Generation.FloorObjectives.TryParse(od.ObjectiveKind, out var kind)) EventBus.Instance.RaiseObjectiveCompleted(od.Floor, kind);
        Trace.Add("objective");
    }

    /// <summary>Main built the floor from the Welcome: tell the server we are in.</summary>
    public void FloorBuilt()
    {
        if (_readySent || _h.State != ClientHandshake.Phase.Welcomed) return;
        _readySent = true;
        _t.Send(NetPeers.Server, NetCodec.Encode(_h.OnFloorBuilt()));
        NetSession.Instance.SetStatus("in game");
        Trace.Add("ready");
        Reindex();
    }

    private void OnPlayerJoined(NetPlayerDto p)
    {
        var party = Main.Instance.Party;
        if (p.MemberIndex >= party.Members.Length) party.AddMember(p.Name, p.ClassId);
        party.Unpark(p.MemberIndex, ControlKind.Remote);
        _players[p.MemberIndex] = new PoseBuffer();
        Trace.Add($"joined:{p.Name}");
    }

    private void OnPlayerLeft(PlayerLeft pl)
    {
        var party = Main.Instance.Party;
        if (pl.MemberIndex < party.Adventurers.Length) party.Park(pl.MemberIndex);
        _players.Remove(pl.MemberIndex);
        Trace.Add($"left:{pl.Name}");
    }

    // ---- apply -------------------------------------------------------------------------------

    public void Reindex()
    {
        _enemies = new Dictionary<int, Enemy>();
        _npcs = new Dictionary<string, Npc>();
        if (Main.Instance?.Builder.FloorRoot is not { } root) return;
        foreach (var child in root.GetChildren())
        {
            if (child is Enemy e) _enemies[e.NetId] = e;
            else if (child is Npc n) _npcs[n.Id] = n;
        }
    }

    private void ApplySnapshot(WorldSnapshot s)
    {
        SnapshotsSeen++;
        _serverTickOffset = s.Tick - NetSession.Instance.Tick;
        var now = NetSession.Instance.Now;
        var party = Main.Instance?.Party;
        if (party == null) return;
        foreach (var e in s.Enemies)
        {
            if (!_enemies.TryGetValue(e.NetId, out var enemy) || !GodotObject.IsInstanceValid(enemy)) { _reindexTimer = 0f; continue; }
            enemy.ApplyNetState(e, s.Tick, now);
        }
        foreach (var n in s.Npcs)
            if (_npcs.TryGetValue(n.Id, out var npc) && GodotObject.IsInstanceValid(npc)) npc.ApplyNetState(n, s.Tick, now);
        foreach (var p in s.Players)
        {
            if (p.Member == MemberIndex) { if (!_echoed) { _echoed = true; Trace.Add("echo"); } continue; }
            if (p.Member >= party.Adventurers.Length) continue;
            if (!_players.TryGetValue(p.Member, out var buf)) _players[p.Member] = buf = new PoseBuffer();
            buf.Push(now, p, s.Tick);
            var body = party.Adventurers[p.Member];
            if (body.Parked) party.Unpark(p.Member, ControlKind.Remote);
        }
    }

    private bool _echoed;

    private void ApplyVitals(Vitals v)
    {
        var party = Main.Instance?.Party;
        if (party == null) return;
        foreach (var mv in v.Members)
        {
            if (mv.Member < 0 || mv.Member >= party.Members.Length) continue;
            var m = party.Members[mv.Member];
            var wasAlive = m.IsAlive;
            m.Hp = mv.Hp; m.Stamina = mv.Stamina; m.Mana = mv.Mana; m.CooldownRemaining = mv.Cooldown;
            if (wasAlive != m.IsAlive) EventBus.Instance.RaiseMemberChanged(mv.Member);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_h.State != ClientHandshake.Phase.InGame || Main.Instance?.Party is not { } party || !GodotObject.IsInstanceValid(party)) return;
        var dt = (float)delta;
        var now = NetSession.Instance.Now;
        var renderTime = now - Protocol.InterpolationMs / 1000.0;
        foreach (var (member, buf) in _players)
        {
            if (member >= party.Adventurers.Length || buf.At(renderTime) is not { } s) continue;
            var body = party.Adventurers[member];
            if (body.Control != ControlKind.Remote) continue;
            body.Remote ??= new RemotePose();
            body.Remote.Position = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
            body.Remote.Yaw = s.Yaw; body.Remote.Pitch = s.Pitch;
            body.Remote.Velocity = new Vector3(s.Velocity.X, s.Velocity.Y, s.Velocity.Z);
            body.Remote.Crouch = s.Flags.HasFlag(PoseFlags.Crouch);
            body.Remote.Guard = s.Flags.HasFlag(PoseFlags.Guard);
            body.Remote.OnFloor = s.Flags.HasFlag(PoseFlags.OnFloor);
        }
        _reindexTimer -= dt;
        if (_reindexTimer <= 0f) { _reindexTimer = 2f; Reindex(); }

        var tick = NetSession.Instance.Tick;
        if (tick % (60 / Protocol.PoseHz) == 0 && party.Local is { } local)
        {
            var pos = local.GlobalPosition;
            var flags = PoseFlags.None;
            if (local.Crouching) flags |= PoseFlags.Crouch;
            if (local.AutoRunning || local.Intent.Sprint) flags |= PoseFlags.Sprint;
            if (local.Guarding) flags |= PoseFlags.Guard;
            if (local.IsOnFloor()) flags |= PoseFlags.OnFloor;
            if (local.Downed) flags |= PoseFlags.Downed;
            // coalesce: a body that has not moved sends a pose every half second only
            if (pos.DistanceSquaredTo(_lastSentPos) > 0.0001f || Math.Abs(local.Rotation.Y - _lastSentYaw) > 0.001f || tick % 30 == 0)
            {
                _lastSentPos = pos; _lastSentYaw = local.Rotation.Y;
                _t.Send(NetPeers.Server, NetCodec.Encode(new Pose(tick, local.Index, new NetVec(pos.X, pos.Y, pos.Z), local.Rotation.Y, local.Camera.Rotation.X,
                    new NetVec(local.Velocity.X, local.Velocity.Y, local.Velocity.Z), flags)), NetChannel.State);
            }
        }
        if (tick % 120 == 0) _t.Send(NetPeers.Server, NetCodec.Encode(new Ping(tick, now)));
    }
}
