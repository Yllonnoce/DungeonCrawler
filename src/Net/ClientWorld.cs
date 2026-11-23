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
    private PoseFlags _lastSentFlags;
    private float _lastSentSeatYaw;
    private PoseFlags2 _lastSentFlags2;
    private bool _readySent;
    public Welcome? Welcome => _h.Welcome;
    public ClientHandshake.Phase Phase => _h.State;
    public int MemberIndex => _h.Welcome?.YourMemberIndex ?? -1;
    /// <summary>The name this client plays under (the server knows it by this name).</summary>
    public string PlayerName => _h.PlayerName;
    private readonly Dictionary<int, NetPlayerDto> _roster = new();
    /// <summary>Every hero the server knows, by member: players and hero bots, in play or not (the pause menu's party list).</summary>
    public IReadOnlyDictionary<int, NetPlayerDto> Roster => _roster;
    public int SnapshotsSeen { get; private set; }
    /// <summary>The server's clock as this client reckons it (the Welcome seeds it, each pong refines it).</summary>
    public NetClock Clock { get; } = new();
    /// <summary>The floor's clock as the server runs it (fed by the Welcome, each FloorChange and each <see cref="FloorClockMsg"/>).</summary>
    private readonly FloorClock _floorClock = new();
    /// <summary>The median ping round trip, seconds (0 before the first pong).</summary>
    public double Rtt => Clock.Rtt;
    /// <summary>The server's <c>NetSession.Instance.Now</c> as of now.</summary>
    public double ServerNow => Clock.ServerNow(NetSession.Instance.Now);
    /// <summary>The seconds the server's current floor has run, now (<see cref="Dungeon.DungeonBuilder.FloorSeconds"/> reads it once a physics tick).</summary>
    public double ReadFloorSeconds() => _floorClock.Read(ServerNow);
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
                _roster.Clear();
                foreach (var p in w.Players) _roster[p.MemberIndex] = p;
                _epoch = w.FloorEpoch;
                if (w.ServerTime > 0)
                {
                    Clock.Seed(w.ServerTime, NetSession.Instance.Now);
                    _floorClock.Reset();
                    _floorClock.Apply(w.FloorSeconds, w.ServerTime);
                }
                try { Main.Instance.StartClientSession(w); }
                catch (Exception e)
                {
                    // the server waits for a Ready that would never come: leave with the reason rather than sit in a half-built floor
                    GD.PushError($"[net] the Welcome's floor would not build: {e}");
                    Main.Instance.OnClientLeft("The server's floor would not build here; try joining again.");
                }
                finally { AfterBuild(); }
            }
            return;
        }
        MessageReceived?.Invoke(m);
        // one message that throws costs that message only, and says which it was (it used to escape the transport's poll unnamed)
        try { Dispatch(m); }
        catch (Exception e) { GD.PushError($"[net] {m.Kind} failed: {e.GetType().Name}: {e.Message}"); }
    }

    private void Dispatch(NetMessage m)
    {
        switch (m)
        {
            case WorldSnapshot s: ApplySnapshot(s); break;
            case Vitals v: ApplyVitals(v); break;
            case LogMsg log: foreach (var line in log.Lines) EventBus.Instance.Log(line); break;
            case ChatMsg chat: EventBus.Instance.Log($"[{chat.From}] {chat.Text}"); Trace.Add("chat"); break;
            case SfxMsg sfx:
                if (Dungeon.Rails.RideSound.Clips.ContainsKey(sfx.Name)) RailSfxSeen++; // the RIDE pass: a rail cue is never relayed (each peer plays its own)
                EventBus.Instance.PlaySfx(sfx.Name);
                break;
            case ObjectiveMsg o: EventBus.Instance.RaiseObjectiveChanged(o.Text); break;
            case PlayerJoined pj: OnPlayerJoined(pj.Player); break;
            case PlayerLeft pl: OnPlayerLeft(pl); break;
            case Pong pong: Clock.OnPong(pong.SentAt, NetSession.Instance.Now, pong.ServerTime); break;
            case FloorClockMsg fcm: OnFloorClock(fcm); break;
            case CartStateMsg cs: OnCarts(cs.Floor, cs.Epoch, cs.Carts, correction: false); break;
            case CartCorrectionMsg cc: OnCarts(cc.Floor, cc.Epoch, cc.Carts, correction: true); break;
            case SeatMsg seat: if (RailsHere() is { } seatRails) seatRails.OnSeat(seat); break;
            case LeapMsg leap: if (RailsHere() is { } leapRails) leapRails.OnLeap(leap); break;
            case ForcePose fp: ApplyForcePose(fp); break;
            case ShakeEvt sh: OnShake(sh); break;
            case PartySync ps: Main.Instance.Party.ApplyMemberDto(ps.Index, ps.Member); break;
            case BagSync bs: Main.Instance.Party.ApplyBagDto(bs.Bag); Trace.Add("bag"); break;
            case ShopSync ss: Main.Instance.Party.ApplyShopSync(ss.Floor, ss.Sold, ss.Gold, ss.Buyback); Trace.Add("shop"); break;
            case LuckyDipSync ld: Main.Instance.Party.ApplyLuckyDipSync(ld.Floor, ld.Bought, ld.Gold); Trace.Add("dip"); break;
            case ShrineCardMsg sc: Main.Instance.Hud.Shrine.Open(sc); Trace.Add("shrine"); break;
            case ShrineClosedEvt scl: Main.Instance.Hud.Shrine.Close(scl.Outcome); break;
            case FortuneSync fs: Game.Instance.Adventure.Boons = fs.Boons; Game.Instance.Adventure.Curses = fs.Curses; FortuneDirector.Instance?.Refresh(); Trace.Add("fortune"); break;
            case NpcSay ns:
                if (Main.Instance?.Builder.FloorRoot?.GetNodeOrNull<Npc>($"Npc_{ns.NpcId}") is { } npc) npc.Say(ns.Line);
                Trace.Add("npcsay");
                break;
            case GoldSync gs: Main.Instance.Party.SetGold(gs.Gold); Trace.Add("gold"); break;
            case LootSpawned ls: OnLootSpawned(ls); break;
            case LootRemoved lr: OnLootRemoved(lr); break;
            case LootManifest lm: OnLootManifest(lm); break;
            case EnemyCheckMsg ec: OnEnemyCheck(ec); break;
            case StateCheckMsg sc: OnStateCheck(sc); break;
            case PropCheckMsg pc: OnPropCheck(pc); break;
            case PropStateMsg ps2: OnPropState(ps2); break;
            case PropEvt pe: OnProp(pe); break;
            case TrapPlacedEvt tp: OnTrapPlaced(tp.Trap); break;
            case EnemyHurt eh: if (_enemies.TryGetValue(eh.NetId, out var hurt) && GodotObject.IsInstanceValid(hurt)) hurt.ClientHurt(eh.Amount, eh.Hp, (Combat.HitFlavor)eh.Flavor, eh.ByMember); break;
            case EnemyStateMsg es: if (_enemies.TryGetValue(es.NetId, out var tele) && GodotObject.IsInstanceValid(tele)) tele.ClientTelegraph(es.State); break;
            case EnemyDied ed: if (_enemies.TryGetValue(ed.NetId, out var dead) && GodotObject.IsInstanceValid(dead)) dead.ClientDie(); Trace.Add("kill"); break;
            case ProjectileSpawned pr: OnProjectile(pr); break;
            case HitFxEvt fx: OnHitFx(fx); break;
            case DeathCardEvt dc: Main.Instance.Hud?.ShowDeathCard(dc.Card, null); Trace.Add("deathcard"); break; // the card closes on its button; under the Death Clause the server's recap follows
            case FloorCardEvt fc2: Main.Instance.Hud?.ShowFloorCard(fc2.Card); Trace.Add("floorcard"); break;
            case PingEvt pe: UI.Hud.PingMarkers.Instance?.Place(pe.Name, new Vector3(pe.At.X, pe.At.Y, pe.At.Z)); Trace.Add("ping"); break;
            case StairsReadyMsg rm:
                if (rm.Total == 0) Main.Instance.Hud?.HideReadyCheck(); else Main.Instance.Hud?.ShowReadyCheck(rm.Count, rm.Total, rm.SecondsLeft, rm.Direction);
                Trace.Add(rm.Total == 0 ? "ready-done" : "ready-check");
                break;
            case RunRecapEvt rr: Main.Instance.Hud?.ShowRunRecap(rr.Recap, rr.Button, rr.Recap.Victory ? null : () => { Main.Instance.OnClientLeft("The contract is void."); Main.Instance.Menu?.OpenRecords(); }); Trace.Add("recap"); break; // a dead world is nothing to stay in
            case MemberDownedEvt md:
                if (md.Index < Main.Instance.Party.Adventurers.Length)
                {
                    Main.Instance.Party.Members[md.Index].Hp = 0; // the state agrees with the pose at once (the vitals follow), so nothing reads the body as standing in between
                    Main.Instance.Party.Adventurers[md.Index].ShowDowned();
                }
                EventBus.Instance.RaiseMemberDowned(md.Index);
                break;
            case MemberRevivedEvt mr: if (mr.Index < Main.Instance.Party.Adventurers.Length) Main.Instance.Party.Adventurers[mr.Index].Revive(); break;
            case LevelUpEvt lu: EventBus.Instance.RaiseLevelUp(lu.Index); break;
            case SwingEvt sw:
                if (sw.Member != Main.Instance.Party.LocalIndex && sw.Member >= 0 && sw.Member < Main.Instance.Party.Adventurers.Length)
                    Main.Instance.Party.Adventurers[sw.Member].ShowSwing((Combat.AbilityKind)sw.AbilityKind, sw.Ranged);
                if (!Trace.Contains("swing")) Trace.Add("swing");
                break;
            case EmoteEvt em when em.Member >= 0 && em.Member < Main.Instance.Party.Adventurers.Length:
                if (em.Emote == "victory") Main.Instance.Party.Adventurers[em.Member].Celebrate();
                else if (em.Emote == "cheer") Main.Instance.Party.Adventurers[em.Member].PlayCheer();
                break;
            case BossDefeatedEvt bd: OnBossDefeated(bd.Floor); break;
            case ObjectiveDoneEvt od: OnObjectiveDone(od); break;
            case ObjectiveStateMsg os: Adventure.Objectives.ObjectiveController.Instance?.ApplyNet(os.State); break;
            case FloorChangeBegin fb: Main.Instance.Overlay.Open("The dungeon stirs...", fb.Status); Main.Instance.Party.InputLocked = true; _floorWatch.Begin(NetSession.Instance.Now); break;
            case FloorChange when TestDropFloorChanges > 0:
                TestDropFloorChanges--; // the net test loses one on purpose: the watchdog must fetch the floor again
                GD.Print("[net] test: dropped a FloorChange; the floor watchdog must ask for it again");
                Trace.Add("floor-dropped");
                break;
            case FloorChange fc: OnFloorChange(fc); break;
            case QuestSync qs: AdventureDirector.Instance.Quests[qs.Quest.Id] = qs.Quest; EventBus.Instance.RaiseQuestChanged(qs.Quest); break;
            case NarrationMsg n:
                // the server sends both forms of a villain line; this screen shows the one its own setting asks for
                if (n.CleanText != null && !Settings.VillainCurses) EventBus.Instance.RaiseNarration(n.CleanText, n.CleanVoiceId);
                else EventBus.Instance.RaiseNarration(n.Text, n.VoiceId);
                break;
            case BarkMsg b: AdventureDirector.Instance.PlayBark(b.Member, new Line { Text = b.Text, VoiceId = b.VoiceId }); Trace.Add($"bark:{b.Member}"); break;
            case DialogueAnswer da: Main.Instance.Hud?.Dialogue.OnJoinAnswer(da.NpcId, da.Accepted); Trace.Add(da.Accepted ? "joined" : "refused"); break;
            case EnemySpawned es: OnEnemySpawned(es.Enemy); break;
            case HordeTriggeredEvt ht: Root?.GetChildren().OfType<HordeSpawner>().FirstOrDefault(h => h.Horde.Id == ht.Id)?.Show(); break;
            case HordeSpentEvt hs: Root?.GetChildren().OfType<HordeSpawner>().FirstOrDefault(h => h.Horde.Id == hs.Id)?.ShowSpent(); break;
            case AmbushSprungEvt asp: Root?.GetChildren().OfType<AmbushSpawner>().FirstOrDefault()?.ShowBurst(asp.Index); break;
            case HazardPhaseMsg hp: OnHazardPhase(hp); break;
            case PortalOpenedEvt po: Root?.GetChildren().OfType<PortalSpawner>().FirstOrDefault()?.ShowPortal(new Vector3(po.X, po.Y, po.Z)); break;
        }
    }

    /// <summary>Shakes the server sent this screen's own body (a hammer, a slam, a landing): the camera rattles, never in a headset.</summary>
    public int ShakesHeard { get; private set; }

    private void OnShake(ShakeEvt sh)
    {
        ShakesHeard++;
        if (Vr.Active || Main.Instance?.Party?.Local is not { } me || !float.IsFinite(sh.Amplitude) || !float.IsFinite(sh.Seconds)) return;
        me.Shake(Math.Clamp(sh.Amplitude, 0f, 0.3f), Math.Clamp(sh.Seconds, 0f, 3f));
    }

    private void ApplyForcePose(ForcePose fp)
    {
        var party = Main.Instance?.Party;
        if (party == null || fp.Member < 0 || fp.Member >= party.Adventurers.Length) return;
        var body = party.Adventurers[fp.Member];
        var at = new Vector3(fp.Pose.X, fp.Pose.Y, fp.Pose.Z);
        body.Teleport(at);
        body.Rotation = new Vector3(0, fp.Pose.Yaw, 0);
        if (Dungeon.Rails.RailNet.Instance is { } rails && GodotObject.IsInstanceValid(rails)) rails.PlaceHeld(body, at, cartId: -1); // a held leaper (ruling (d)): the server placed it, on footing (re-review N2, R1)
        if (fp.Member == MemberIndex) GetNodeOrNull<NetTestFlow>("../TestFlow")?.NotePlaced(at);
    }

    private static Node3D? Root => Main.Instance?.Builder.FloorRoot;

    private void OnHazardPhase(HazardPhaseMsg hp)
    {
        var secondsIn = HazardSync.SecondsIn(ServerNow, hp.ServerTime, hp.DurationSeconds);
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

    /// <summary>The server's floor clock pair: dropped when it is about another floor or build than the one this client holds.</summary>
    /// <summary>The carts' states (an event's, or the 5 Hz correction's): for this floor's build only, once it stands here.</summary>
    private void OnCarts(int floor, int epoch, List<CartStateDto> carts, bool correction)
    {
        if (floor != Game.Instance.CurrentFloor || !WorldCheck.SameBuild(_epoch, epoch)) return;
        if (Main.Instance is not { InGame: true, Rebuilding: false } main || main.Builder.CurrentFloor != floor) return;
        if (Dungeon.Rails.RailNet.Instance is { } rails && GodotObject.IsInstanceValid(rails)) rails.OnCarts(carts, correction);
    }

    /// <summary>
    /// The rail sender's copy here while a built floor stands (a seat or a leap names no floor: one meant for a floor this client
    /// has not built yet finds no cart or rider here and is dropped; the rail catch-up on its Ready says it again).
    /// </summary>
    private static Dungeon.Rails.RailNet? RailsHere()
    {
        if (Main.Instance is not { InGame: true, Rebuilding: false } main || main.Builder.CurrentFloor != Game.Instance.CurrentFloor) return null;
        return Dungeon.Rails.RailNet.Instance is { } rails && GodotObject.IsInstanceValid(rails) ? rails : null;
    }

    /// <summary>The living (or dying) copy of the server's enemy <paramref name="netId"/> here, or null.</summary>
    public Enemy? EnemyByNetId(int netId)
    {
        if (_enemies.TryGetValue(netId, out var e) && GodotObject.IsInstanceValid(e) && !e.IsQueuedForDeletion()) return e;
        if (Root is not { } root) return null;
        foreach (var child in root.GetChildren())
            if (child is Enemy found && found.NetId == netId && !found.IsQueuedForDeletion()) { _enemies[netId] = found; return found; }
        return null;
    }

    private void OnFloorClock(FloorClockMsg m)
    {
        if (m.Floor != Game.Instance.CurrentFloor || !WorldCheck.SameBuild(_epoch, m.Epoch)) return;
        var report = NetSession.Instance.TestClient && ++_floorPairsSeen == 5; // a few pairs in: the pings have refined the seed
        var before = report && _floorClock.HasPair ? ReadFloorSeconds() : double.NaN;
        _floorClock.Apply(m.FloorSeconds, m.ServerTime);
        if (report)
        {
            // the scripted client's proof that the clocks agree: the pair's floor time carried to now against what this client read just before it
            GD.Print($"[net] floor clock: server floor {m.FloorSeconds + (ServerNow - m.ServerTime):0.000} s, ours {before:0.000} s, rtt {Rtt * 1000:0} ms");
            Trace.Add("floorclock");
        }
    }

    private int _floorPairsSeen;

    /// <summary>
    /// A floor build here has ended (the Welcome's or a FloorChange's, built inside the handler for a second or two): the pongs of pings sent
    /// before now waited through it, and the server's own build, so they are not samples (<see cref="NetClock.IgnoreBefore"/>, review I1); a
    /// fresh ping goes out on the next tick.
    /// </summary>
    private void AfterBuild()
    {
        Clock.IgnoreBefore(NetSession.Instance.Now);
        _pingSoon = true;
    }

    /// <summary>The floor build this client holds (the server's count, from the Welcome and each FloorChange; 0 unknown).</summary>
    private int _epoch;
    /// <summary>Watches a floor change from its FloorChangeBegin to its build (<see cref="FloorWatchdog"/>).</summary>
    private readonly FloorWatchdog _floorWatch = new() { TimeoutSeconds = NetSession.Instance?.TestClient == true ? 4 : 20 };
    /// <summary>The net test drops this many FloorChanges on purpose (the watchdog must recover them).</summary>
    public int TestDropFloorChanges { get; set; }

    /// <summary>The net test (the RIDE pass): ask the server for the floor we stand on again, as the floor watchdog does, so it is rebuilt here mid-ride.</summary>
    public void TestAskFloorAgain(string why)
    {
        _floorWatch.Begin(NetSession.Instance.Now); // a resend of the build we hold is applied, not dropped as one that crossed our ask
        NetSession.Instance.Send(new FloorResyncIntent(Game.Instance.CurrentFloor, _epoch, why));
        Trace.Add("floor-ask");
    }

    private void OnFloorChange(FloorChange fc)
    {
        // our ask crossed the change it was about: the build we hold is already this one
        if (fc.Resync && !_floorWatch.Pending && fc.Epoch != 0 && fc.Epoch == _epoch && fc.Floor == Game.Instance.CurrentFloor) { GD.Print($"[net] floor {fc.Floor} (build {fc.Epoch}) sent again, already built"); return; }
        // a new floor, a new clock: its first pair rides in the change
        _floorClock.Reset();
        if (fc.ServerTime > 0) _floorClock.Apply(fc.FloorSeconds, fc.ServerTime);
        try { Main.Instance.ApplyFloorChange(fc); }
        catch (Exception e)
        {
            // the loading screen stays up and the controls locked: the watchdog asks for the floor again shortly, then rejoins
            GD.PushError($"[net] floor {fc.Floor} would not build: {e}");
            _floorWatch.Failed(NetSession.Instance.Now);
            Trace.Add("floor-failed");
            return;
        }
        finally { AfterBuild(); }
        _floorWatch.Done();
        _epoch = fc.Epoch;
        _enemySpawnedAt.Clear();
        Reindex();
        _players.Clear();
        _t.Send(NetPeers.Server, NetCodec.Encode(new Ready(fc.Floor)));
        if (fc.Resync) { GD.Print($"[net] floor {fc.Floor} (build {fc.Epoch}) rebuilt from the server's resend"); Trace.Add("floor-resync"); }
        Trace.Add($"floor:{fc.Floor}");
    }

    /// <summary>The watchdog's turn: ask the server for the floor again, or rejoin when asking has not helped.</summary>
    private void TickFloorWatch()
    {
        switch (_floorWatch.Tick(NetSession.Instance.Now))
        {
            case FloorWatchdog.Step.Ask:
                var why = _floorWatch.Failures > 0 ? "our build of it failed" : "no floor change arrived";
                GD.Print($"[net] floor watchdog: {why}; asking the server for the floor again (ask {_floorWatch.Asks})");
                NetSession.Instance.Send(new FloorResyncIntent(Game.Instance.CurrentFloor, _epoch, why));
                Trace.Add("floor-ask");
                break;
            case FloorWatchdog.Step.Rejoin:
                Rejoin("the floor change never completed");
                break;
        }
    }

    /// <summary>
    /// The last resort: leave and join again under the same name, which the server treats as a rejoin with a whole new
    /// Welcome (what "reconnecting fixed it" did by hand). With no address to dial, back to the title with the reason.
    /// </summary>
    private void Rejoin(string why)
    {
        Trace.Add("rejoin");
        if (NetSession.Instance.ServerUrl is not { } url) { Main.Instance.OnClientLeft($"Lost the floor ({why}); join again."); return; }
        GD.PushWarning($"[net] {why}: rejoining {url} as {PlayerName}");
        var name = PlayerName;
        var cls = _h.ClassId;
        Callable.From(() => NetSession.Instance.Connect(url, name, cls)).CallDeferred();
    }

    /// <summary>When this client spawned an enemy from a server message (the enemy check spares one that new).</summary>
    private readonly Dictionary<int, double> _enemySpawnedAt = new();

    private void OnEnemySpawned(DynamicEnemyDto d)
    {
        var main = Main.Instance;
        if (main?.Builder.FloorRoot is not { } root) return;
        // a living copy already here (the index can lag the tree, so look at the tree): nothing to do. A corpse under a net id
        // the server says is alive (the enemy check's resend) makes way for the living one
        var have = root.GetChildren().OfType<Enemy>().Where(e => e.NetId == d.NetId && !e.IsQueuedForDeletion()).ToList();
        if (have.FirstOrDefault(e => e.IsAlive) is { } alive) { _enemies[d.NetId] = alive; Dungeon.Rails.RailNet.Instance?.OnEnemySpawned(alive); return; }
        foreach (var corpse in have) corpse.QueueFree();
        var e = main.Builder.SpawnEnemy(d.DefId, new Vector3(d.X, d.Y, d.Z), Game.Instance.CurrentFloor, spawnIndex: d.SpawnIndex >= 0 || d.SpawnIndex == -2 ? d.SpawnIndex : -1, netId: d.NetId,
            elite: d.EliteName != null ? new EliteRoll(EliteRules.Decode(d.EliteAffixes), d.EliteName) : null);
        if (e == null) return;
        e.Rotation = new Vector3(0, d.Yaw, 0);
        if (d.HordeId != null) { e.SetSwarmDetail(); e.HordeId = d.HordeId; }
        _enemies[d.NetId] = e;
        _enemySpawnedAt[d.NetId] = NetSession.Instance.Now;
        Dungeon.Rails.RailNet.Instance?.OnEnemySpawned(e); // a cart's state may have named it as a rider before it came
    }

    /// <summary>
    /// The enemy check (<see cref="EnemyCheckMsg"/>): free a living enemy the server has no living copy of (a ghost: its death
    /// or its corpse's end never reached us, or the floor state brought it back), and ask for the living ones we lack.
    /// </summary>
    private void OnEnemyCheck(EnemyCheckMsg ec)
    {
        if (Root is not { } root || Main.Instance is not { InGame: true } main || main.Rebuilding || ec.Floor != Game.Instance.CurrentFloor || main.Builder.CurrentFloor != ec.Floor || !WorldCheck.SameBuild(_epoch, ec.Epoch)) return;
        var now = NetSession.Instance.Now;
        var local = root.GetChildren().OfType<Enemy>().Where(e => !e.IsQueuedForDeletion()).ToList();
        var (free, missing) = WorldCheck.CompareEnemies(local.Select(e => (e.NetId, e.IsAlive, _enemySpawnedAt.TryGetValue(e.NetId, out var at) ? now - at : double.MaxValue)).ToList(), ec.Alive);
        foreach (var i in free)
        {
            if (NetSession.Instance.TestClient) GD.Print($"[net] enemy check: freeing {local[i].Name} ({local[i].Profile.Id}, net id {local[i].NetId}): the server has no living copy");
            local[i].QueueFree();
        }
        if (free.Count > 0) Reindex();
        if (missing.Count > 0) NetSession.Instance.Send(new EnemyResyncIntent(ec.Floor, ec.Epoch, missing.Take(64).ToList()));
        if (free.Count == 0 && missing.Count == 0) return;
        GD.Print($"[net] enemy check: freed {free.Count} creature(s) the server does not have, asked for {missing.Count} missing");
        Trace.Add("enemy-check");
    }

    /// <summary>
    /// The server's blood or stuck arrow, drawn here: blood under the floor root, an arrow on the enemy it names. An arrow
    /// for an enemy this screen never spawned or has already freed, or blood with no live floor root, is dropped without a
    /// word and leaves no "hitfx" trace.
    /// </summary>
    private void OnHitFx(HitFxEvt fx)
    {
        var pos = new Vector3(fx.Pos.X, fx.Pos.Y, fx.Pos.Z);
        var dir = new Vector3(fx.Dir.X, fx.Dir.Y, fx.Dir.Z);
        switch ((HitFxKind)fx.FxKind)
        {
            case HitFxKind.Blood:
                if (Root is not { } root || !GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion()) return;
                Combat.Fx.HitFx.Blood(root, pos, dir);
                break;
            case HitFxKind.Arrow:
                if (!_enemies.TryGetValue(fx.EnemyNetId, out var enemy) || !GodotObject.IsInstanceValid(enemy)) return;
                // sent in the enemy's frame: back to world space from where this screen draws it
                var xf = enemy.GlobalTransform;
                var before = enemy.StuckArrowCount;
                Combat.Fx.HitFx.StickArrow(enemy, xf * pos, xf.Basis * dir);
                if (enemy.StuckArrowCount == before) return; // no model, or already showing its six
                break;
            default: return; // a kind this build does not know
        }
        Trace.Add("hitfx");
    }

    private void OnLootSpawned(LootSpawned ls)
    {
        if (Root is not { } root) return;
        // idempotent: an item already shown (a resend that crossed the original, an ask repeated on a slow link, the item
        // check's answer to a count that differs) is brought up to date in place instead of shown twice
        var at = new Vector3(ls.Position.X, ls.Position.Y, ls.Position.Z);
        var shown = root.GetChildren().OfType<Items.LootDrop>().Where(d => !d.Taken && d.Item.Id == ls.Item.Id).ToList();
        if (shown.Count > 0)
        {
            var keep = shown[0];
            foreach (var extra in shown.Skip(1)) extra.QueueFree();
            if ((keep.Coins > 0) == (ls.Coins > 0)) // the same kind of thing on the floor: its look stays, its count and place follow the server
            {
                keep.Item = Save.SaveMapper.FromDto(ls.Item, Game.Instance.Catalog);
                keep.Coins = ls.Coins;
                keep.KeyIndex = ls.KeyIndex;
                keep.QuestId = ls.QuestKey;
                keep.GlobalPosition = at;
                Trace.Add("loot-updated");
                return;
            }
            keep.QueueFree(); // a pile became an item or the other way: its look is built once, so make it again
        }
        var drop = GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn").Instantiate<Items.LootDrop>();
        drop.Item = Save.SaveMapper.FromDto(ls.Item, Game.Instance.Catalog);
        drop.Coins = ls.Coins;
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
            if (d.Item.Id == lr.Id && !d.Taken) d.QueueFree(); // every copy: a drop shown twice must not leave a ghost behind
    }

    /// <summary>The state check (<see cref="StateCheckMsg"/>): compare our copies of the bag, the members and the purse with the server's fingerprints and ask again for what differs; take the server's word on the stairs seal.</summary>
    private void OnStateCheck(StateCheckMsg sc)
    {
        if (Root is not { } root || Main.Instance is not { InGame: true } main || main.Rebuilding || sc.Floor != Game.Instance.CurrentFloor || main.Builder.CurrentFloor != sc.Floor) return;
        var party = main.Party;
        var bag = Save.SaveMapper.ToDto(party.Bag);
        var bagDiffers = StateCheck.BagHash(bag) != sc.Bag;
        var members = new List<int>();
        for (var i = 0; i < Math.Min(sc.Members.Count, party.Members.Length); i++)
            if (StateCheck.MemberHash(Save.SaveMapper.ToDto(party.Members[i])) != sc.Members[i]) members.Add(i);
        var goldDiffers = party.Gold != sc.Gold;
        if (root.GetNodeOrNull<Dungeon.Stairs>("DownStairs") is { } stairs && stairs.Locked != sc.StairsLocked)
        {
            GD.Print($"[net] state check: the stairs down are {(sc.StairsLocked ? "sealed" : "open")} on the server; fixing ours");
            stairs.SetLocked(sc.StairsLocked);
            Trace.Add("stairs-check");
        }
        if (!bagDiffers && members.Count == 0 && !goldDiffers) return;
        GD.Print($"[net] state check: {(bagDiffers ? "bag " : "")}{(members.Count > 0 ? $"member(s) {string.Join(",", members)} " : "")}{(goldDiffers ? "purse " : "")}differ from the server; asking again");
        NetSession.Instance.Send(new StateResyncIntent(bagDiffers, members, goldDiffers));
        Trace.Add("state-check");
    }

    /// <summary>The item check (<see cref="LootManifest"/>): drop what the server no longer has, ask for what we lack.</summary>
    private void OnLootManifest(LootManifest lm)
    {
        // another floor, or another build of this floor number (a wipe's reload): the list is not about what we show
        if (Root is not { } root || Main.Instance is not { InGame: true } main || main.Rebuilding || lm.Floor != Game.Instance.CurrentFloor || main.Builder.CurrentFloor != lm.Floor || !WorldCheck.SameBuild(_epoch, lm.Epoch)) return;
        var drops = root.GetChildren().OfType<Items.LootDrop>().Where(d => !d.Taken).ToList();
        var (free, missing) = WorldCheck.CompareItems(drops.Select(d => (d.Item.Id, WorldCheck.ItemCount(d.Coins, d.Item.StackCount))).ToList(), lm.Ids, lm.Counts);
        foreach (var i in free) drops[i].QueueFree(); // not on the server's floor, or a second copy of one that is
        // what we lack, and what we show with another count (a partial pickup's stack): the resend replaces ours in place
        if (missing.Count > 0) NetSession.Instance.Send(new LootResyncIntent(lm.Floor, missing, lm.Epoch));
        if (free.Count > 0 || missing.Count > 0)
        {
            GD.Print($"[net] item check: removed {free.Count} item(s) the server does not have, asked for {missing.Count} missing or miscounted");
            Trace.Add("loot-check");
        }
    }

    /// <summary>Relayed sound cues that were a rail cue (the RIDE pass wants none: every peer plays the ride's own sound).</summary>
    public int RailSfxSeen { get; private set; }

    private void OnProp(PropEvt pe)
    {
        if (Root is not { } root || NetRefResolver.Resolve(root, pe.Ref) is not { } node) return;
        switch (node, pe.What)
        {
            case (Dungeon.Props.Chest c, "opened"): c.MarkOpened(); Trace.Add("chest"); break;
            case (Dungeon.Props.Chest c, "mimic"): c.ShowSprung(); Trace.Add("mimic"); break;
            case (Dungeon.Props.Chest c, "jammed"): c.ShowJammed(); break;
            case (Dungeon.Props.Door d, "opened"): d.Open(silent: true); break;
            case (Dungeon.Props.Door d, "revealed"): d.Reveal(); break;
            case (Dungeon.Props.Door d, "closed"): d.Close(silent: true); break;
            case (Dungeon.Props.Lever l, "pulled"): l.Pull(silent: true); break;
            case (Dungeon.Props.Lever l, "reset"): l.Reset(silent: true); break;
            case (Dungeon.Props.SecretWall s, "revealed"): s.Reveal(); break;
            case (Dungeon.Props.SecretWall s, "opened"): s.Open(silent: true); break;
            case (Dungeon.Props.Shrine sh, "used"): sh.MarkUsed(silent: true); break;
            case (Dungeon.Props.Trap t, "revealed"): t.Reveal(); break;
            case (Dungeon.Props.Press p, "revealed"): p.Plate.Reveal(); break;
            case (Dungeon.Props.Trap t, "disarmed"): t.Disarm(); break;
            case (Dungeon.Props.Trap t, "sprung"): t.ShowSprung(); break;
            case (Dungeon.Props.Breakable b, "broken"): b.Vanish(); break;
            // a railed floor's props (plan 5): the server judged them; our copies show it, then make its sound here (RideAudio.OnProp, after
            // the switch: a quiet change makes none). The quiet tells ("raised", "burst-quiet", "smashed-quiet") are a load's: shown silently
            case (Dungeon.Rails.RailGate g, "up"): g.ShowUp(); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.RailGate g, "raised"): g.ShowUp(animate: false); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.TrackSwitch sw, "thrown"): sw.ShowThrown(true); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.TrackSwitch sw, "straight"): sw.ShowThrown(false); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.TrackSwitch sw, "taken"): sw.ShowTaken(); break;
            case (Dungeon.Rails.TrackSwitch sw, "free"): sw.ShowTaken(false); break;
            case (Dungeon.Rails.TrackSwitch sw, "reset"): sw.ShowReset(); break;
            case (Dungeon.Rails.OreHeap h, "cleared"): h.ShowCleared(); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.OreHeap h, "rebuilt"): h.ShowRebuilt(); break;
            case (Dungeon.Rails.QuenchValve v, "broken"): v.ShowBroken(); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.QuenchValve v, "burst-quiet"): v.ShowBroken(steam: false); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.QuenchValve v, "rebuilt"): v.ShowRebuilt(); break;
            case (Dungeon.Rails.Trestle t, "collapsed"): t.ShowCollapsed(root); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.Trestle t, "rebuilt"): t.ShowRebuilt(); break;
            case (Dungeon.Rails.BufferStop b, "smashed"): b.ShowSmashed(root, Dungeon.Rails.RailController.Instance is { } rc && IsInstanceValid(rc.Cart) ? rc.Cart.Velocity : Vector3.Zero); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.BufferStop b, "smashed-quiet"): b.ShowSmashed(root, Vector3.Zero, debris: false); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.BufferStop b, "restored"): b.ShowRestored(); break;
            case (Dungeon.Rails.Barricade b, "cut"): b.ShowCut(); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.Barricade b, "smashed"): b.ShowSmashed(root); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.Barricade b, "standing"): b.ShowStanding(); break;
            case (Dungeon.Rails.CavernBridge cb, _): if (Dungeon.Rails.RailEncounterController.Instance?.ApplyBridgeTold(cb, pe.What, root) == true) Trace.Add("rail-prop"); break; // "gone {n}", "stands" (plan 13)
            case (Dungeon.Rails.SlagDrop sd, _): if (Dungeon.Rails.RailEncounterController.Instance?.ApplyDropTold(sd, pe.What, root) == true) Trace.Add("rail-prop"); break; // "drops", "stands" (plan 13)
            case (Dungeon.Rails.MineCart spare, "used"): Dungeon.Rails.RailController.Instance?.ShowSpare(spare, used: true); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.MineCart spare, "restored"): Dungeon.Rails.RailController.Instance?.ShowSpare(spare, used: false); break;
            case (Dungeon.Rails.PlatformCage c, "opened"): (Adventure.Objectives.ObjectiveController.Instance as Adventure.Objectives.CrewRescueController)?.ShowCageOpen(c.Stop); Trace.Add("rail-prop"); break;
            case (Dungeon.Rails.TrainWagon, var what) when what.StartsWith("thrown:", StringComparison.Ordinal): // a boarder threw a prisoner off (plan 14 Task 5)
                if ((Adventure.Objectives.ObjectiveController.Instance as Adventure.Objectives.CrewRescueController)?.ShowThrown(what["thrown:".Length..]) == true) Trace.Add("rail-prop");
                break;
            case (Adventure.Objectives.OrcKeyring, "taken"): (Adventure.Objectives.ObjectiveController.Instance as Adventure.Objectives.CrewRescueController)?.ShowKeysTaken(); Trace.Add("rail-prop"); break;
        }
        Dungeon.Rails.RideAudio.OnProp(pe.Ref, pe.What); // a rail prop's sound (RideSound.PropCue; none for any other): the live event only, never a prop state's catch-up
    }

    private void OnTrapPlaced(Save.PlacedTrapDto dto)
    {
        if (Root is not { } root || !Enum.TryParse<Dungeon.TrapKind>(dto.Kind, out var kind)) return;
        // the server's id rides along (v14), so "sprung" reaches this copy when a creature steps on it there
        if (dto.Id >= 0 && root.GetChildren().OfType<Dungeon.Props.Trap>().Any(t => t.PlacedByParty && t.SpawnIndex == dto.Id && !t.IsQueuedForDeletion())) return;
        PropSync.SpawnPlaced(root, dto, kind);
    }

    /// <summary>Prop checks whose hash matched ours (the scripted RIDE check reads it).</summary>
    public int PropChecksMatched { get; private set; }

    /// <summary>The server's prop state we last asked for (and when): an ask the apply could not settle is not repeated every check.</summary>
    private uint? _propAskedFor;
    private double _propAskedAt;

    /// <summary>The prop check (<see cref="PropCheckMsg"/>): hash our props as the server hashes its own and ask for the whole state when they differ.</summary>
    private void OnPropCheck(PropCheckMsg pc)
    {
        if (Root is not { } root || Main.Instance is not { InGame: true } main || main.Rebuilding || pc.Floor != Game.Instance.CurrentFloor || main.Builder.CurrentFloor != pc.Floor) return;
        var mine = PropCheck.Hash(PropSync.Capture(root));
        if (mine == pc.Hash) PropChecksMatched++;
        var now = NetSession.Instance.Now;
        if (!PropCheck.ShouldAsk(mine, pc.Hash, _propAskedFor, _propAskedAt, now)) return;
        _propAskedFor = pc.Hash;
        _propAskedAt = now;
        GD.Print($"[net] prop check: our props differ from the server's ({mine:X8} vs {pc.Hash:X8}); asking again");
        NetSession.Instance.Send(new PropResyncIntent(pc.Floor));
        Trace.Add("prop-check");
    }

    /// <summary>The server's whole prop state (the prop check's answer, or a join's catch-up): move ours to match, silently.</summary>
    private void OnPropState(PropStateMsg ps)
    {
        if (Root is not { } root || Main.Instance is not { InGame: true } main || main.Rebuilding || ps.Floor != Game.Instance.CurrentFloor || main.Builder.CurrentFloor != ps.Floor) return;
        var before = PropSync.Capture(root);
        var changed = PropSync.ApplyLive(ps.State, root);
        if (changed.Count > 0)
        {
            GD.Print($"[net] prop check: {string.Join(", ", changed)}");
            Trace.Add("props-fixed");
        }
        // settled (Capture skips what is queued for freeing): the next difference may ask at once. A difference only the
        // other way can mend (a barrel or chest we lost that the server still has) keeps the ask held back (PropCheck.ShouldAsk)
        var after = PropSync.Capture(root);
        if (PropCheck.Hash(after) == PropCheck.Hash(ps.State)) { if (changed.Count == 0 && _propAskedFor != null) GD.Print("[net] prop check: nothing left to repair (the difference had passed)"); _propAskedFor = null; }
        else GD.Print($"[net] prop check: still differs from the server after the apply ({string.Join("; ", PropCheck.Differences(after, ps.State))}; before: {string.Join("; ", PropCheck.Differences(before, ps.State))})");
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
        if (Dungeon.TierRules.IsFinalBossFloor(floor)) Game.Instance.Victory = true; // the recap comes from the server as a RunRecapEvt
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
        _roster[p.MemberIndex] = p;
        Trace.Add(p.BotOwner != null ? $"bot:{p.Name}" : $"joined:{p.Name}");
    }

    private void OnPlayerLeft(PlayerLeft pl)
    {
        var party = Main.Instance.Party;
        if (pl.MemberIndex < party.Adventurers.Length) party.Park(pl.MemberIndex);
        _players.Remove(pl.MemberIndex);
        if (_roster.TryGetValue(pl.MemberIndex, out var known)) _roster[pl.MemberIndex] = known with { Online = false, PeerId = 0 };
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
            if (child.IsQueuedForDeletion()) continue; // a ghost the checks just freed must not shadow the living copy
            if (child is Enemy e) _enemies[e.NetId] = e;
            else if (child is Npc n) _npcs[n.Id] = n;
        }
    }

    private void ApplySnapshot(WorldSnapshot s)
    {
        SnapshotsSeen++;
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
            if (p.Member == MemberIndex)
            {
                if (!_echoed) { _echoed = true; Trace.Add("echo"); }
                // the server's word on our own pump (its Inputs set it from our RideIntent): our seated hero shows it as other screens do
                ServerSaysPumping = p.Flags2.HasFlag(PoseFlags2.Pumping);
                if (p.Member < party.Adventurers.Length && party.Adventurers[p.Member] is { } own) own.Pumping = own.Seated && ServerSaysPumping;
                continue;
            }
            if (p.Member >= party.Adventurers.Length) continue;
            if (!_players.TryGetValue(p.Member, out var buf)) _players[p.Member] = buf = new PoseBuffer();
            buf.Push(now, p, s.Tick);
            var body = party.Adventurers[p.Member];
            if (body.Parked) party.Unpark(p.Member, ControlKind.Remote);
        }
    }

    private bool _echoed;

    /// <summary>The last snapshot's Pumping flag for this client's own hero (the server's Inputs; the RIDE pass reads it).</summary>
    internal bool ServerSaysPumping { get; private set; }

    private void ApplyVitals(Vitals v)
    {
        var party = Main.Instance?.Party;
        if (party == null) return;
        foreach (var mv in v.Members)
        {
            if (mv.Member < 0 || mv.Member >= party.Members.Length) continue;
            var m = party.Members[mv.Member];
            var wasAlive = m.IsAlive;
            if (m.IsAlive && mv.Hp < m.Hp - 0.5f) EventBus.Instance.RaiseMemberDamaged(mv.Member, m.Hp - mv.Hp); // the vignette, the portrait flash and the shake on a client
            m.Hp = mv.Hp; m.Stamina = mv.Stamina; m.Mana = mv.Mana; m.CooldownRemaining = mv.Cooldown;
            if (wasAlive != m.IsAlive) EventBus.Instance.RaiseMemberChanged(mv.Member);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_h.State != ClientHandshake.Phase.InGame || Main.Instance?.Party is not { } party || !GodotObject.IsInstanceValid(party)) return;
        TickFloorWatch();
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
            body.Remote.SeatYaw = s.SeatYaw;
            body.Remote.Seated = s.Flags2.HasFlag(PoseFlags2.Seated);
            body.Remote.Pumping = s.Flags2.HasFlag(PoseFlags2.Pumping);
        }
        _reindexTimer -= dt;
        if (_reindexTimer <= 0f) { _reindexTimer = 2f; Reindex(); }

        // by elapsed time, not ticks: a 90 Hz headset sends as often as a 60 Hz screen
        var tick = NetSession.Instance.Tick;
        if (_heartbeatCadence.Advance(delta) > 0) _heartbeatDue = true;
        if (_poseCadence.Advance(delta) > 0 && party.Local is { } local)
        {
            var pos = local.GlobalPosition;
            var flags = PoseFlags.None;
            if (local.Crouching) flags |= PoseFlags.Crouch;
            if (local.AutoRunning || local.Intent.Sprint) flags |= PoseFlags.Sprint;
            if (local.Guarding) flags |= PoseFlags.Guard;
            if (local.IsOnFloor()) flags |= PoseFlags.OnFloor;
            if (local.Downed) flags |= PoseFlags.Downed;
            if (local.Defense.Parrying) flags |= PoseFlags.Parry;
            if (local.Defense.Dodging) flags |= PoseFlags.Dodge;
            // seated (the server's SeatMsg sat it here): where it looks from the seat goes too; the server ignores the pump bit (its Inputs decide)
            var flags2 = PoseFlags2.None;
            if (local.Seated) flags2 |= PoseFlags2.Seated;
            if (local.Seated && local.Pumping) flags2 |= PoseFlags2.Pumping;
            var seatYaw = local.Seated ? local.SeatYaw : 0f;
            // coalesce: a body that has not moved sends a pose every half second only; a window that opens or closes while standing still goes at
            // once, and so does a seated head turning (a held cart moves nothing else)
            if (pos.DistanceSquaredTo(_lastSentPos) > 0.0001f || Math.Abs(local.Rotation.Y - _lastSentYaw) > 0.001f || flags != _lastSentFlags
                || Math.Abs(seatYaw - _lastSentSeatYaw) > 0.001f || flags2 != _lastSentFlags2 || _heartbeatDue)
            {
                _lastSentPos = pos; _lastSentYaw = local.Rotation.Y; _lastSentFlags = flags; _lastSentSeatYaw = seatYaw; _lastSentFlags2 = flags2; _heartbeatDue = false;
                _t.Send(NetPeers.Server, NetCodec.Encode(new Pose(tick, local.Index, new NetVec(pos.X, pos.Y, pos.Z), local.Rotation.Y, local.Camera.Rotation.X,
                    new NetVec(local.Velocity.X, local.Velocity.Y, local.Velocity.Z), flags, seatYaw, flags2)), NetChannel.State);
            }
        }
        // every second (it was every two): more samples for the server clock (NetClock) to settle on
        if (_pingCadence.Advance(delta) > 0 || _pingSoon)
        {
            if (_pingSoon) _pingCadence.Reset(); // the next one a second after this
            _pingSoon = false;
            _t.Send(NetPeers.Server, NetCodec.Encode(new Ping(tick, now, Rtt)));
        }
    }

    private Cadence _poseCadence = new(1.0 / Protocol.PoseHz);
    /// <summary>A body that has not moved still sends a pose this often.</summary>
    private Cadence _heartbeatCadence = new(0.5);
    private bool _heartbeatDue;
    private Cadence _pingCadence = new(1.0);
    /// <summary>A ping goes out on the next tick (a build here just ended: <see cref="AfterBuild"/>).</summary>
    private bool _pingSoon;
}
