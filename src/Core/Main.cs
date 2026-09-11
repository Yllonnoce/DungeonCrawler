using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using DungeonCrawler.UI.Hud;
using DungeonCrawler.UI.Menus;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>Root of Main.tscn. Owns the 3D World (dungeon + party) and the UI CanvasLayer.</summary>
public partial class Main : Node
{
    public static Main Instance { get; private set; } = null!;

    public PartyController Party { get; private set; } = null!;
    public DungeonBuilder Builder { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public ExplorationMap? Exploration { get; private set; }

    private GridPos _lastPartyCell = new(int.MinValue, int.MinValue);

    private Node3D _world = null!;
    private CanvasLayer _ui = null!;
    private MainMenu _menu = null!;
    public MainMenu? Menu => _menu;
    private PartyDto? _pendingPartyState;
    public bool InGame => Party != null && IsInstanceValid(Party);

    public override void _Ready()
    {
        Instance = this;
        _world = GetNode<Node3D>("World");
        _ui = GetNode<CanvasLayer>("UiRoot");

        Builder = new DungeonBuilder { Name = "Dungeon" };
        _world.AddChild(Builder);

        _menu = new MainMenu { Name = "MainMenu" };
        _ui.AddChild(_menu);
        Overlay = new LoadingOverlay { Name = "LoadingOverlay" };
        _ui.AddChild(Overlay);
        AddChild(new Items.ItemIcons { Name = "ItemIcons" }); // renders bag icons from the item models

        EventBus.Instance.FloorChanged += OnFloorChanged;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print("[Main] scene ready (menu)");
    }

    public override void _ExitTree() => EventBus.Instance.FloorChanged -= OnFloorChanged;

    public override void _PhysicsProcess(double delta)
    {
        TickWaiting();
        if (!InGame || Builder.Layout is not { } layout || Exploration == null) return;
        if (NetSession.IsServer)
        {
            if (Engine.GetPhysicsFrames() % 10 == 0) foreach (var b in Party.Active) Exploration.Reveal(layout, DungeonBuilder.WorldToCell(b.GlobalPosition));
            return;
        }
        if (!Party.HasBodies) return;
        var cell = DungeonBuilder.WorldToCell(Party.GlobalPosition);
        if (cell == _lastPartyCell) return;
        _lastPartyCell = cell;
        Exploration.Reveal(layout, cell);

        var under = Builder.IsUnderAt(Party.GlobalPosition);
        var level = under ? -1 : layout[cell] == Cell.Ramp ? _lastLevel : layout.LevelOf(cell);
        if (level != _lastLevel)
        {
            if (_lastLevel != int.MinValue)
                EventBus.Instance.Log(level < 0 ? "You are below the dungeon floor." : level > _lastLevel ? "The way climbs to a higher level." : "The way descends to a lower level.");
            _lastLevel = level;
        }
        if (layout[cell] == Cell.Ramp && !_onRamp) EventBus.Instance.Log("The floor slopes here.");
        _onRamp = layout[cell] == Cell.Ramp;
    }

    private void CreateSession(IEnumerable<(string Name, string ClassId, Characters.Rank Rank)>? roster = null, int localIndex = 0, bool hud = true)
    {
        TearDownSession();
        Party = GD.Load<PackedScene>("res://scenes/Party.tscn").Instantiate<PartyController>();
        Party.Configure(roster ?? PartyController.DefaultRoster, localIndex);
        Party.AllowPossession = !NetSession.IsNetworked;
        _world.AddChild(Party);
        Party.SpawnBodies(_world);
        if (hud)
        {
            Hud = GD.Load<PackedScene>("res://scenes/UI/Hud.tscn").Instantiate<Hud>();
            Hud.Bind(Party);
            _ui.AddChild(Hud);
        }
        _menu.Visible = false;
    }

    // ---- multiplayer ---------------------------------------------------------------------------

    /// <summary>A dedicated server: no HUD, an empty roster that players fill as they join, the campaign from the command line.</summary>
    public void StartServerSession()
    {
        var net = NetSession.Instance;
        var game = Game.Instance;
        var director = AdventureDirector.Instance;
        // a server save from an earlier run: the same world, every character parked until its player is back
        if (SaveManager.Read(net.SaveSlot) is { } saved)
        {
            if (!saved.Multiplayer) net.SetStatus($"save slot '{net.SaveSlot}' holds a single-player game; starting a fresh server world instead");
            else
            {
                var roster = saved.Party.Members.Select(m => (m.Name, m.ClassId, Enum.TryParse<Characters.Rank>(m.Rank, out var r) ? r : PartyController.RankFor(m.ClassId))).ToList();
                CreateSession(roster, -1, hud: false);
                game.BeginLoaded(saved);
                director.ConfigurePlanner(offlineOnly: net.Campaign != "ai");
                net.Server!.Registry.Load(saved.Players.Select(PlayerSlot.FromSave));
                _pendingPartyState = saved.Party;
                RebuildFloor(saved.CurrentFloor, 0);
                for (var i = 0; i < Party.Adventurers.Length; i++) Party.Park(i);
                net.Resumed = true;
                net.SetStatus($"resumed save '{net.SaveSlot}': floor {saved.CurrentFloor}, {saved.Players.Count} known players, {saved.Party.Members.Count} members");
                return;
            }
        }
        CreateSession(Array.Empty<(string, string, Characters.Rank)>(), -1, hud: false);
        if (net.Campaign == "builtin" && BuiltinCampaign.Load() is { } baked)
        {
            game.PrepareNewGame(baked.Campaign.GameSeed);
            game.Adventure.Mode = AdventureMode.Builtin;
            game.Adventure.Campaign = baked.Campaign;
            foreach (var (floor, plan) in baked.Floors) game.Adventure.FloorPlans[floor] = plan;
            director.ConfigurePlanner(offlineOnly: true);
            game.BeginFloor();
            return;
        }
        game.PrepareNewGame(net.Seed);
        director.ConfigurePlanner(offlineOnly: net.Campaign != "ai");
        if (!director.AiEnabled)
        {
            game.Adventure.Campaign = OfflineDesigner.Outline(game.GameSeed, game.TierProfiles(), Builder.ThemeProfiles());
            game.BeginFloor();
            return;
        }
        net.SetStatus($"{director.PlannerName} is writing the descent...");
        director.PrepareCampaign(net.SetStatus, (campaign, floor1) =>
        {
            if (!InGame) return;
            game.Adventure.Campaign = campaign;
            game.Adventure.Mode = campaign.Source == PlanSources.Claude ? AdventureMode.Claude : AdventureMode.Offline;
            if (floor1 != null) game.Adventure.FloorPlans[1] = floor1;
            net.SetStatus($"campaign ready: {campaign.Title}");
            game.BeginFloor();
        });
    }

    /// <summary>A client got its Welcome: build the same floor the server has, then say we are ready.</summary>
    public void StartClientSession(Welcome w)
    {
        var roster = w.Party.Members.Select(m => (m.Name, m.ClassId, Enum.TryParse<Characters.Rank>(m.Rank, out var r) ? r : PartyController.RankFor(m.ClassId))).ToList();
        CreateSession(roster, w.YourMemberIndex);
        var online = w.Players.Where(p => p.Online).Select(p => p.MemberIndex).ToHashSet();
        for (var i = 0; i < Party.Adventurers.Length; i++)
        {
            if (i == w.YourMemberIndex) continue;
            if (online.Contains(i)) Party.Unpark(i, ControlKind.Remote); else Party.Park(i);
        }
        Game.Instance.BeginNetwork(w);
        Game.Instance.ServerObjectives[w.CurrentFloor] = Dungeon.Generation.FloorObjectives.TryParse(w.FloorObjective, out var objective) ? objective : Dungeon.Generation.FloorObjectiveKind.None;
        AdventureDirector.Instance.ConfigurePlanner(offlineOnly: true);
        _pendingPartyState = w.Party;
        _forcedSnapshot = w.BuildSnapshot;
        RebuildFloor(w.CurrentFloor, 0);
        _forcedSnapshot = null;
        foreach (var d in w.DynamicEnemies)
        {
            var e = Builder.SpawnEnemy(d.DefId, new Vector3(d.X, d.Y, d.Z), w.CurrentFloor, netId: d.NetId);
            if (e == null) continue;
            e.Rotation = new Vector3(0, d.Yaw, 0);
            if (d.HordeId != null) { e.SetSwarmDetail(); e.HordeId = d.HordeId; }
        }
        if (Builder.Layout is { } lay && Party.Local is { } me && !w.Players.Any(p => p.MemberIndex == w.YourMemberIndex && p.Online))
            me.Teleport(Builder.Surface(DungeonBuilder.WalkableNeighbor(lay, lay.UpStairs)) + Vector3.Up * 0.3f, Builder.Surface(lay.UpStairs));
        var hash = Builder.Layout?.ContentHash() ?? 0;
        LayoutHashMatched = hash == w.LayoutHash;
        GD.Print($"[net] layout hash {(LayoutHashMatched ? "match" : $"MISMATCH (ours {hash}, server {w.LayoutHash})")}");
        if (!LayoutHashMatched) EventBus.Instance.Log("Warning: this floor does not match the server's. Check that both run the same build.");
        NetSession.Instance.Client?.FloorBuilt();
    }

    public bool LayoutHashMatched { get; private set; }
    private Adventure.PartySnapshot? _forcedSnapshot;

    /// <summary>The connection ended (refused, dropped, or we left): back to the title screen with the reason showing.</summary>
    public void OnClientLeft(string? reason)
    {
        Callable.From(() =>
        {
            NetSession.Instance.Disconnect(reason);
            if (InGame) ReturnToMenu();
            else { _menu.Reset(); _menu.Visible = true; }
            if (reason != null) _menu.ShowStatus(reason);
        }).CallDeferred();
    }

    private void TearDownSession()
    {
        GetTree().Paused = false;
        Builder.Clear();
        if (Hud != null && IsInstanceValid(Hud)) Hud.QueueFree();
        if (Party != null && IsInstanceValid(Party)) { Party.RemoveFromGroup(PartyController.GroupName); Party.FreeBodies(); Party.QueueFree(); }
        Hud = null!;
        Party = null!;
        Exploration = null;
    }

    public LoadingOverlay Overlay { get; private set; } = null!;
    private (int floor, int delta, ulong sinceMs)? _waitingFor;

    /// <param name="aiCampaign">True: ask the live planner for a fresh campaign (automation only; the title screen never plans, see <see cref="StartNewGameFromDisk"/>); false: never plan, the offline designer writes it; null: follow the Settings toggle (automation).</param>
    public void StartNewGame(ulong? seed = null, bool? aiCampaign = null)
    {
        CreateSession();
        var game = Game.Instance;
        game.PrepareNewGame(seed);
        var director = AdventureDirector.Instance;
        director.ConfigurePlanner(forceAi: aiCampaign == true, noAi: aiCampaign == false);
        if (!director.AiEnabled)
        {
            game.Adventure.Campaign = OfflineDesigner.Outline(game.GameSeed, game.TierProfiles(), Builder.ThemeProfiles());
            game.Adventure.Campaign.Barks = Party.Members.Select(m => OfflineDesigner.DefaultBarks(m.Name, m.Class.Name)).ToList();
            director.EnqueueCampaignVoice(game.Adventure.Campaign);
            game.BeginFloor();
            return;
        }
        Overlay.Open("The dungeon stirs...", $"{director.PlannerName} is writing your descent.");
        Party.InputLocked = true;
        director.PrepareCampaign(Overlay.SetStatus, (campaign, floor1) =>
        {
            if (!InGame) return;
            game.Adventure.Campaign = campaign;
            game.Adventure.Mode = campaign.Source == PlanSources.Claude ? AdventureMode.Claude : AdventureMode.Offline;
            if (floor1 != null) game.Adventure.FloorPlans[1] = floor1;
            Overlay.ShowCampaign(campaign.Title, campaign.Synopsis);
            Overlay.SetStatus("Descending...");
            var linger = DebugTools.Instance?.Automated == true ? 0.3f : 3.5f;
            GetTree().CreateTimer(linger).Timeout += () =>
            {
                if (!InGame) return;
                Overlay.Close();
                Party.InputLocked = false;
                game.BeginFloor();
            };
        });
    }

    /// <summary>
    /// New Game: the campaign on disk (your rebake, else the shipped one), every floor already written, so nothing is
    /// planned and nothing is spent. Without one, the offline designer writes a run on the spot.
    /// </summary>
    public void StartNewGameFromDisk()
    {
        if (!StartBuiltinCampaign()) StartNewGame(aiCampaign: false);
    }

    /// <summary>New Game on the shipped campaign: fixed seed, every floor plan already written.</summary>
    public bool StartBuiltinCampaign()
    {
        if (BuiltinCampaign.Load() is not { } baked) return false;
        CreateSession();
        var game = Game.Instance;
        game.PrepareNewGame(baked.Campaign.GameSeed);
        game.Adventure.Mode = AdventureMode.Builtin;
        game.Adventure.Campaign = baked.Campaign;
        foreach (var (floor, plan) in baked.Floors) game.Adventure.FloorPlans[floor] = plan;
        AdventureDirector.Instance.ConfigurePlanner(offlineOnly: true);
        if (baked.Campaign.Barks.Count == 0) baked.Campaign.Barks = Party.Members.Select(m => OfflineDesigner.DefaultBarks(m.Name, m.Class.Name)).ToList();
        AdventureDirector.Instance.EnqueueCampaignVoice(baked.Campaign);
        Overlay.Open(baked.Campaign.Title, baked.Campaign.Synopsis);
        Overlay.SetStatus("Descending...");
        Party.InputLocked = true;
        GetTree().CreateTimer(DebugTools.Instance?.Automated == true ? 0.3f : 3.0f).Timeout += () =>
        {
            if (!InGame) return;
            Overlay.Close();
            Party.InputLocked = false;
            game.BeginFloor();
        };
        return true;
    }

    /// <summary>A bake picking up where it stopped: the outline and the floors already written come from disk, the rest from the planner.</summary>
    public void StartResumedBake(CampaignPlan campaign, Dictionary<int, FloorPlan> floors)
    {
        CreateSession();
        var game = Game.Instance;
        game.PrepareNewGame(campaign.GameSeed);
        game.Adventure.Mode = campaign.Source == PlanSources.Claude ? AdventureMode.Claude : AdventureMode.Offline;
        game.Adventure.Campaign = campaign;
        foreach (var (floor, plan) in floors) game.Adventure.FloorPlans[floor] = plan;
        var director = AdventureDirector.Instance;
        director.ConfigurePlanner();
        if (campaign.Barks.Count == 0 && director.AiEnabled) director.RequestBarks(campaign);
        game.BeginFloor();
    }

    public void ReturnToMenu()
    {
        if (NetSession.IsNetworked) NetSession.Instance.Disconnect();
        TearDownSession();
        Game.Instance.EndSession();
        _menu.Reset();
        _menu.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    // ---- save / load ---------------------------------------------------------------------------

    public SaveData BuildSaveData(string label)
    {
        var game = Game.Instance;
        if (Builder.FloorRoot is { } root && Builder.Layout is { } layout && Exploration != null)
            game.Floors[game.CurrentFloor] = FloorStateCapture.Capture(root, layout, Builder.Contents, Exploration);
        var data = new SaveData
        {
            Label = label, GameSeed = game.GameSeed, CurrentFloor = game.CurrentFloor,
            SavedAtUtc = SaveMapper.Now(), PlaySeconds = game.PlaySeconds, Party = Party.CaptureState(), Adventure = game.Adventure,
            Multiplayer = NetSession.IsServer, Players = NetSession.Instance?.Server?.Registry.ToSave() ?? new List<PlayerDto>(),
        };
        foreach (var (floor, state) in game.Floors) data.Floors[floor] = state;
        return data;
    }

    public void SaveGame(string slot)
    {
        if (!InGame) return;
        SaveManager.Write(slot, BuildSaveData(SaveManager.DisplayName(slot)));
        EventBus.Instance.Log($"Game saved ({SaveManager.DisplayName(slot)}).");
    }

    /// <summary>Load a slot, replacing the current session. False if the slot is missing or unreadable.</summary>
    public bool LoadGame(string slot)
    {
        var data = SaveManager.Read(slot);
        if (data == null) return false;
        CreateSession();
        Game.Instance.BeginLoaded(data);
        AdventureDirector.Instance.ConfigurePlanner();
        _pendingPartyState = data.Party;
        RebuildFloor(data.CurrentFloor, 0);
        EventBus.Instance.Log($"Game loaded ({SaveManager.DisplayName(slot)}).");
        return true;
    }

    // Floor changes are triggered from interaction/physics callbacks; rebuilding the tree must wait a frame.
    private void OnFloorChanged(int floor, int delta) => Callable.From(() => BeginRebuild(floor, delta)).CallDeferred();

    /// <summary>Rebuild now, or wait a while for a plan the AI is still writing for this floor.</summary>
    private void BeginRebuild(int floor, int delta)
    {
        if (!InGame) return;
        var director = AdventureDirector.Instance;
        // a floor the prefetch has not reached yet (fast player, debug jump): start it now and wait a little
        if (director != null && director.AiEnabled && !Game.Instance.Adventure.FloorPlans.ContainsKey(floor)) director.Prefetch(floor);
        if (director != null && director.IsPlanning(floor))
        {
            _waitingFor = (floor, delta, Time.GetTicksMsec());
            Overlay.Open("The dungeon stirs...", $"Floor {floor} is still being written (up to {Settings.PlanWaitSeconds:0} s).");
            Party.InputLocked = true;
            return;
        }
        RebuildFloor(floor, delta);
    }

    private void TickWaiting()
    {
        if (_waitingFor is not { } w || !InGame) return;
        var director = AdventureDirector.Instance;
        var waited = (Time.GetTicksMsec() - w.sinceMs) / 1000f;
        if (director.IsPlanning(w.floor) && waited < Settings.PlanWaitSeconds) return;
        if (director.IsPlanning(w.floor)) director.AbandonPlanning(w.floor, "the party arrived first");
        _waitingFor = null;
        Overlay.Close();
        Party.InputLocked = false;
        RebuildFloor(w.floor, w.delta);
    }

    /// <summary>The floor's design: stored in the run, or produced now by the offline designer.</summary>
    private FloorPlan? PlanFor(int floor)
    {
        var adv = Game.Instance.Adventure;
        if (adv.Mode == AdventureMode.Legacy) return null;
        if (adv.FloorPlans.TryGetValue(floor, out var plan)) return plan;
        if (NetSession.IsClient) { GD.PushError($"[net] no plan for floor {floor} in the server's adventure"); return null; }
        var (designed, report) = Builder.DesignOffline(floor, Game.Instance.GameSeed, Party.Snapshot());
        if (report.Repairs.Count > 0) GD.Print($"[Adventure] floor {floor} offline design repaired: {string.Join("; ", report.Repairs)}");
        adv.FloorPlans[floor] = designed;
        return designed;
    }

    /// <summary>True while a floor is being built and its saved state applied: nothing that happens then is news to anyone.</summary>
    public bool Rebuilding { get; private set; }

    private void RebuildFloor(int floor, int delta)
    {
        Rebuilding = true;
        try { RebuildFloorInner(floor, delta); }
        finally { Rebuilding = false; }
    }

    private void RebuildFloorInner(int floor, int delta)
    {
        if (!InGame) return;
        var game = Game.Instance;

        // remember the floor we are leaving so it comes back the same way
        if (Builder.FloorRoot is { } oldRoot && Builder.Layout is { } oldLayout && Exploration != null && _lastBuiltFloor != floor)
            game.Floors[_lastBuiltFloor] = FloorStateCapture.Capture(oldRoot, oldLayout, Builder.Contents, Exploration);

        var snapshot = _forcedSnapshot ?? Party.Snapshot();
        BuildSnapshotFor[floor] = snapshot; // what a client must pass to Build to get the same floor
        var layout = Builder.Build(floor, game.GameSeed, PlanFor(floor), snapshot);
        NoteFloorBuilt();
        ApplyTierLook();
        Hud?.RefreshFloorLabel();
        Exploration = game.ExplorationFor(floor);
        _lastPartyCell = new GridPos(int.MinValue, int.MinValue);
        _lastLevel = int.MinValue;
        _lastBuiltFloor = floor;

        if (game.Floors.TryGetValue(floor, out var saved) && Builder.FloorRoot is { } root)
            FloorStateCapture.Apply(saved, root, Exploration, Party);

        if (_pendingPartyState is { } pending)
        {
            Party.RestoreState(pending, keepControl: NetSession.IsNetworked);
            _pendingPartyState = null;
            Hud?.Refresh();
            if (!NetSession.IsClient) AdventureDirector.Instance?.OnFloorBuilt(Builder.Contents!, floor, firstVisit: false);
            return;
        }

        var stairs = delta >= 0 ? layout.UpStairs : layout.DownStairs;
        var spawnCell = DungeonBuilder.WalkableNeighbor(layout, stairs);
        Party.Teleport(Builder.Surface(spawnCell) + Vector3.Up * 0.3f, Builder.Surface(stairs));
        if (!NetSession.IsClient) EventBus.Instance.Log(delta > 0 ? $"You descend to floor {floor}." : delta < 0 ? $"You climb back to floor {floor}." : $"Floor {floor} reshapes itself.");
        if (!NetSession.IsClient) AdventureDirector.Instance?.OnFloorBuilt(Builder.Contents!, floor, firstVisit: saved == null);
        EventBus.Instance.RaiseFloorBuilt(floor, delta, saved == null);
    }

    /// <summary>A client following the server to another floor: same seed, same plan, same snapshot, then everyone's poses.</summary>
    public void ApplyFloorChange(FloorChange fc)
    {
        if (!InGame) return;
        var game = Game.Instance;
        game.SetFloorFromServer(fc.Floor);
        if (fc.Plan != null) game.Adventure.FloorPlans[fc.Floor] = fc.Plan;
        game.ServerObjectives[fc.Floor] = Dungeon.Generation.FloorObjectives.TryParse(fc.Objective, out var objective) ? objective : Dungeon.Generation.FloorObjectiveKind.None;
        if (fc.State != null) game.Floors[fc.Floor] = fc.State; else game.Floors.Remove(fc.Floor);
        _pendingPartyState = null;
        _forcedSnapshot = fc.BuildSnapshot;
        RebuildFloor(fc.Floor, fc.Delta);
        _forcedSnapshot = null;
        for (var i = 0; i < Party.Adventurers.Length && i < fc.SpawnPoses.Count; i++)
        {
            var p = fc.SpawnPoses[i];
            Party.Adventurers[i].Teleport(new Vector3(p.X, p.Y, p.Z));
            Party.Adventurers[i].Rotation = new Vector3(0, p.Yaw, 0);
        }
        foreach (var d in fc.DynamicEnemies)
        {
            var e = Builder.SpawnEnemy(d.DefId, new Vector3(d.X, d.Y, d.Z), fc.Floor, netId: d.NetId);
            if (e == null) continue;
            e.Rotation = new Vector3(0, d.Yaw, 0);
            if (d.HordeId != null) { e.SetSwarmDetail(); e.HordeId = d.HordeId; }
        }
        if (fc.Plan != null) AdventureDirector.Instance?.EnqueueFloorVoice(fc.Plan);
        LayoutHashMatched = Builder.Layout != null && (fc.LayoutHash == 0 || fc.LayoutHash == Builder.Layout.ContentHash());
        GD.Print($"[net] floor {fc.Floor} layout hash {(LayoutHashMatched ? "match" : "MISMATCH")}");
        Overlay.Close();
        Party.InputLocked = Hud?.Paused ?? false;
        EventBus.Instance.Log(fc.Delta > 0 ? $"You descend to floor {fc.Floor}." : fc.Delta < 0 ? $"You climb back to floor {fc.Floor}." : $"Floor {fc.Floor} reshapes itself.");
    }

    private int _lastBuiltFloor = -1;
    private int _lastLevel = int.MinValue;
    /// <summary>The party snapshot each floor was built with; a joining client rebuilds with the same one.</summary>
    public Dictionary<int, Adventure.PartySnapshot> BuildSnapshotFor { get; } = new();
    private bool _onRamp;

    private void ApplyTierLook()
    {
        var tier = Builder.CurrentTier;
        if (tier == null) return;
        var look = Builder.CurrentTheme ?? Dungeon.Defs.ThemeDef.FromTier(tier);
        var (fogColor, ambientColor, fogScale) = LayoutTint(Builder.Layout?.Kind ?? Dungeon.Generation.LayoutKind.Standard, look);
        _baseFog = fogColor; _baseAmbient = ambientColor; _baseFogDensity = look.FogDensity * fogScale;
        if (_world.GetNodeOrNull<WorldEnvironment>("Env")?.Environment is { } env)
        {
            env.AmbientLightColor = ambientColor;
            env.AmbientLightEnergy = look.AmbientEnergy * (fogScale > 1f ? 1.15f : 1f);
            env.FogLightColor = fogColor;
            env.FogDensity = look.FogDensity * fogScale;
            env.BackgroundColor = fogColor.Darkened(0.5f);
        }
        ApplyHaze();
        foreach (var adv in Party.Adventurers) adv.Torch.LightColor = look.TorchColor;
        Audio.AudioManager.Instance?.SetTierAmbience(tier.Index);
    }

    private Color _baseFog = new(0.05f, 0.05f, 0.06f), _baseAmbient = Colors.Black;
    private float _baseFogDensity = 0.035f;

    /// <summary>The gas on a Toxic floor: the air around the player goes green and thick with its level (0 = the floor's own air).</summary>
    public void ApplyGasTint(float level)
    {
        if (_world.GetNodeOrNull<WorldEnvironment>("Env")?.Environment is not { } env) return;
        var green = new Color(0.22f, 0.42f, 0.10f);
        level = Mathf.Clamp(level, 0f, 1f);
        env.FogLightColor = _baseFog.Lerp(green, 0.75f * level);
        env.AmbientLightColor = _baseAmbient.Lerp(green, 0.45f * level);
        env.FogDensity = _baseFogDensity * (1f + 2.5f * level);
        env.BackgroundColor = env.FogLightColor.Darkened(0.5f);
    }

    /// <summary>The air of a floor shape over the theme's own: flooded floors go cold and green, burning ones hot and thick with smoke.</summary>
    private static (Color fog, Color ambient, float fogScale) LayoutTint(Dungeon.Generation.LayoutKind kind, Dungeon.Defs.ThemeDef look) => kind switch
    {
        Dungeon.Generation.LayoutKind.Flooded => (look.FogColor.Lerp(new Color(0.06f, 0.16f, 0.17f), 0.55f), look.AmbientColor.Lerp(new Color(0.3f, 0.44f, 0.46f), 0.4f), 1.3f),
        Dungeon.Generation.LayoutKind.Burning => (look.FogColor.Lerp(new Color(0.40f, 0.13f, 0.03f), 0.6f), look.AmbientColor.Lerp(new Color(0.62f, 0.32f, 0.12f), 0.45f), 1.6f),
        _ => (look.FogColor, look.AmbientColor, 1f),
    };

    /// <summary>
    /// A slight haze hanging in the air: volumetric fog thin enough to leave rooms readable, thick enough that
    /// torches, braziers and fires throw a glow into it. Scaled by the Haze setting and the theme's own fog.
    /// </summary>
    public void ApplyHaze()
    {
        if (_world.GetNodeOrNull<WorldEnvironment>("Env")?.Environment is not { } env) return;
        var haze = Mathf.Clamp(Settings.Haze, 0f, 1f);
        env.VolumetricFogEnabled = haze > 0.01f;
        if (!env.VolumetricFogEnabled) return;
        var look = Builder.CurrentTheme ?? (Builder.CurrentTier != null ? Dungeon.Defs.ThemeDef.FromTier(Builder.CurrentTier) : null);
        var kind = Builder.Layout?.Kind ?? Dungeon.Generation.LayoutKind.Standard;
        var (tintedFog, _, fogScale) = look == null ? (new Color(0.05f, 0.05f, 0.06f), Colors.Black, 1f) : LayoutTint(kind, look);
        var themeFog = (look?.FogDensity ?? 0.035f) * fogScale;
        env.VolumetricFogDensity = 0.008f + 0.045f * haze * Mathf.Clamp(themeFog / 0.035f, 0.5f, 2f);
        // dust in torchlight: a warm grey that the theme's fog colour tints
        var fogColor = tintedFog;
        env.VolumetricFogAlbedo = new Color(0.72f, 0.68f, 0.62f).Lerp(fogColor.Lightened(0.6f), 0.35f);
        env.VolumetricFogEmission = fogColor;
        env.VolumetricFogEmissionEnergy = 0.02f + 0.03f * haze;
        env.VolumetricFogAnisotropy = 0.4f;
        env.VolumetricFogLength = 56f;
        env.VolumetricFogDetailSpread = 2.5f;
        env.VolumetricFogAmbientInject = 0.15f;
    }

    /// <summary>A tier guardian died: unlock the way down; on the last floor, declare victory.</summary>
    public void OnBossDefeated(int floor)
    {
        if (NetSession.IsClient) return; // the server's BossDefeated event reaches us over the wire
        if (Builder.FloorRoot?.GetNodeOrNull<Stairs>("DownStairs") is { } stairs) stairs.Unlock();
        if (Game.Instance.Floors.TryGetValue(floor, out var state)) state.BossDefeated = true;
        _bossDefeatedThisFloor = true;
        EventBus.Instance.RaiseBossDefeated(floor);
        if (TierRules.IsFinalBossFloor(floor) && !Game.Instance.Victory)
        {
            Game.Instance.Victory = true;
            Hud?.ShowVictory();
        }
    }

    /// <summary>The floor's objective is met: unlock the way down and tell everyone (clients hear it over the wire).</summary>
    public void OnObjectiveCompleted(int floor, Dungeon.Generation.FloorObjectiveKind kind)
    {
        if (NetSession.IsClient) return;
        if (Builder.FloorRoot?.GetNodeOrNull<Stairs>("DownStairs") is { } stairs) stairs.Unlock();
        if (Game.Instance.Floors.TryGetValue(floor, out var state))
        {
            state.Objective ??= new Save.ObjectiveStateDto { Kind = Dungeon.Generation.FloorObjectives.Id(kind) };
            state.Objective.Completed = true;
        }
        EventBus.Instance.Log(Adventure.Objectives.ObjectiveText.Completed(kind));
        EventBus.Instance.RaiseObjectiveCompleted(floor, kind);
    }

    public bool BossDefeatedThisFloor => _bossDefeatedThisFloor;
    private bool _bossDefeatedThisFloor;

    public void NoteFloorBuilt() => _bossDefeatedThisFloor = false;
}
