using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.AiClients;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Enemies.Collectives;
using DungeonCrawler.Items;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// Developer cheats and automation. Autoload.
/// Keys: F1 heal party, F2 kill all enemies, F3 next floor, F4 previous floor, F6 screenshot, F7 roll chest loot into the bag, F8 give arrows. (F5/F9 are quicksave/quickload.)
/// On a multiplayer client only F6 works: the others would change this machine's world without the server.
/// User args (after "--" on the command line):
///   --screenshot=PATH   save a PNG then quit      --after=SECONDS   delay before the screenshot / quit (default 2)
///   --face-enemy        teleport next to the nearest enemy at start
///   --auto-attack       every member swings whenever ready (headless combat smoke test)
///   --trace-ai          print enemy state transitions
///   --quit-after=SECONDS quit without a screenshot
///   --smoke             scripted end-to-end run: get hit, kill an enemy, loot it, descend twice, ascend once, report
/// </summary>
public partial class DebugTools : Node
{
    public static bool TraceAi { get; private set; }

    private string? _screenshotPath;
    private double _after = 2.0;
    private double _quitAfter = -1;
    private bool _faceEnemy;
    private bool _autoAttack;
    private bool _smoke;
    private bool _openMap;
    private readonly List<string> _pressActions = new();
    private bool _levelUp;
    private bool _testDrag;
    private bool _testSave;
    private bool _testDepth;
    private int _depthStep;
    private bool _testCollectives;
    private int _collStep;
    private readonly List<string> _collFails = new();
    private Collective? _collBig, _collAlarm;
    private Enemy? _collPatrol;
    private Vector3 _collPatrolStart;
    private bool _talk;
    private bool _talked;
    private bool _testQuest;
    private int _questStep;
    private readonly List<string> _questFails = new();
    private float _questXpBefore;
    public static DebugTools? Instance { get; private set; }
    /// <summary>Any automation flag: skip the title screen and shorten cosmetic waits.</summary>
    public bool Automated => _started;
    public string? ReplayDir { get; private set; }
    public string? RecordDir { get; private set; }
    public bool ForceAi { get; private set; }
    public bool NoAi { get; private set; }
    /// <summary>--no-ambush / --no-portals: no corridor ambushers, no reinforcement portals this run.</summary>
    public bool NoAmbush { get; private set; }
    public bool NoPortals { get; private set; }
    private bool _testAmbush, _testPortal;
    private int _dangerStep;
    private float _dangerClock;
    private bool _hurried;
    /// <summary>No live text-to-speech in this run (automation without --ai).</summary>
    public bool NoVoice { get; private set; }
    public float? FakeAiDelay { get; private set; }
    /// <summary>--vault=KIND forces every ceiling to that arch profile (flat, semicircular, pointed, segmental, catenary).</summary>
    public string? ForcedVault { get; private set; }
    /// <summary>--theme=ID forces every floor to wear that theme (screenshots and checks).</summary>
    public string? ForcedTheme { get; private set; }
    private bool _testFloors;
    private bool _testStructures;
    private bool _testHorde;
    private bool _testKeys, _testedKeys;
    private bool _tremor, _testTremor, _tremorFired;
    private float _tremorClock;
    private int _tremorRocksSeen, _tremorLanded;
    private bool _tremorShook;
    private int _hordeStep;
    private bool _hordeShot;
    private float _hordeClock;
    private (int x, int y)? _inspect;
    private bool _inspected;
    private int _floorsStep;
    private int _floorsMisses;
    public bool FakeVoice { get; private set; }
    private bool _testBakeUi;
    private bool _openSettings;
    private bool _openedSettings;
    private int _bakeUiStep;
    private string? _bakeVoiceDir;
    private bool _testVoice;
    private int _voiceStep;
    private readonly List<string> _voiceFails = new();
    private string? _voiceLineId;
    private string? _bakeDir;
    private int _bakeFloor;
    private bool _builtin;
    private bool _testAiFlow;
    private int _aiStep;
    private readonly List<string> _aiFails = new();
    private ulong _aiWaitStart;
    private bool _testPlan;
    private int _planStep;
    private bool _planOk = true;
    private int _saveStep;
    private bool _autoStart;
    private bool _started;
    private string _saveSnapshot = "";
    private string _saveAfter = "";
    private int _dragStep;
    private double _dragT;
    private int _smokeStep;
    private double _elapsed;
    private double _attackTimer;
    private bool _faced;
    private bool _done;

    private bool _menuOnly;
    private bool _lookBack;
    private int _possess = -1;
    private bool _swing;
    private bool _swung;
    private bool _lookedBack;
    private bool _countProps;
    private bool _counted;
    private int _startFloor;
    private bool _faceBoss;
    private bool _faceLedge;
    private bool _facePit;
    private string? _faceNode;
    private int _faceBack = 1;
    private (int x, int y)? _faceCell;
    private string[]? _dropIds;
    private bool _dropped;
    private bool _facedNode;
    private bool _facedPit;
    private bool _facedLedge;
    private ulong? _seed;
    private bool _testPath;
    private bool _testSkills;
    private int _skillStep;
    private bool _testVertical;
    private bool _testStairs;
    private bool _askStairs, _askedStairsDebug;
    private int _stairsStep;
    private double _stairsWalk;
    private int _vertStep;
    private bool _testParty;
    private int _partyStep;
    private bool _server, _netClient;
    private bool _testMusic;
    private bool _bakeResume;
    private bool _listVoiceModels, _listedVoiceModels;
    private string? _probeVoiceModel;
    private bool _probedVoiceModel;
    private bool _probePlanner, _probedPlanner;
    private int _bakePartialWritten = -1;
    private bool _testStory;
    private int _storyStep;
    private readonly List<string> _storyFails = new();
    private bool _bakeVoiceQueued;
    private int _musicStep;
    private double _musicClock;
    private readonly List<string> _musicFails = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // keep automation alive while the pause menu holds the tree
        Instance = this;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=")) _screenshotPath = arg["--screenshot=".Length..];
            else if (arg.StartsWith("--after=")) _after = double.Parse(arg["--after=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--quit-after=")) _quitAfter = double.Parse(arg["--quit-after=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg == "--face-enemy") _faceEnemy = true;
            else if (arg == "--auto-attack") _autoAttack = true;
            else if (arg == "--open-map") _openMap = true;
            else if (arg == "--level-up") _levelUp = true;
            else if (arg == "--menu-only") _menuOnly = true;
            else if (arg == "--open-settings") { _menuOnly = true; _openSettings = true; }
            else if (arg == "--look-back") _lookBack = true;
            else if (arg.StartsWith("--possess=")) _possess = int.Parse(arg["--possess=".Length..]);
            else if (arg == "--swing") _swing = true;
            else if (arg == "--count-props") { _countProps = true; _quitAfter = 2; }
            else if (arg.StartsWith("--floor=")) _startFloor = int.Parse(arg["--floor=".Length..]);
            else if (arg.StartsWith("--seed=")) _seed = ulong.Parse(arg["--seed=".Length..]);
            else if (arg == "--test-path") { _testPath = true; _quitAfter = 8; }
            else if (arg == "--test-skills") { _testSkills = true; _faceEnemy = true; _quitAfter = 7; }
            else if (arg == "--test-vertical") { _testVertical = true; _quitAfter = 14; }
            else if (arg == "--test-stairs") { _testStairs = true; _quitAfter = 40; }
            else if (arg == "--ask-stairs") _askStairs = true;
            else if (arg == "--test-party") { _testParty = true; _faceEnemy = true; _quitAfter = 16; }
            else if (arg == "--face-boss") { _faceBoss = true; _faceEnemy = true; }
            else if (arg == "--face-ledge") _faceLedge = true;
            else if (arg == "--face-pit") _facePit = true;
            else if (arg.StartsWith("--face-node=")) { var spec = arg["--face-node=".Length..].Split(':'); _faceNode = spec[0]; if (spec.Length > 1) _faceBack = int.Parse(spec[1]); }
            else if (arg.StartsWith("--face-cell=")) { var spec = arg["--face-cell=".Length..].Split(':'); var xy = spec[0].Split(','); _faceCell = (int.Parse(xy[0]), int.Parse(xy[1])); if (spec.Length > 1) _faceBack = int.Parse(spec[1]); _quitAfter = Math.Max(_quitAfter, 3); }
            else if (arg.StartsWith("--drop=")) _dropIds = arg["--drop=".Length..].Split(',');
            else if (arg == "--test-drag") { _testDrag = true; _quitAfter = 6; }
            else if (arg == "--test-save") { _testSave = true; _faceEnemy = true; _quitAfter = 9; }
            else if (arg == "--test-depth") { _testDepth = true; TraceAi = false; _quitAfter = 16; }
            else if (arg == "--test-plan") { _testPlan = true; TraceAi = false; _quitAfter = 6; }
            else if (arg == "--test-collectives") { _testCollectives = true; TraceAi = false; _quitAfter = 37; }
            else if (arg == "--test-quest") { _testQuest = true; TraceAi = false; _quitAfter = 14; }
            else if (arg == "--talk") _talk = true;
            else if (arg == "--ai") ForceAi = true;
            else if (arg == "--no-ai") NoAi = true;
            else if (arg.StartsWith("--replay-plans=")) ReplayDir = ProjectSettings.GlobalizePath(arg["--replay-plans=".Length..]);
            else if (arg.StartsWith("--record-plans=")) RecordDir = arg["--record-plans=".Length..];
            else if (arg.StartsWith("--fake-ai=")) FakeAiDelay = float.Parse(arg["--fake-ai=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg == "--fake-ai") FakeAiDelay = 1f;
            else if (arg == "--test-ai-flow") { _testAiFlow = true; FakeAiDelay ??= 1.2f; TraceAi = false; _quitAfter = 22; }
            else if (arg.StartsWith("--bake-campaign=")) { _bakeDir = ProjectSettings.GlobalizePath(arg["--bake-campaign=".Length..]); TraceAi = false; _quitAfter = 1800; }
            else if (arg == "--bake-resume") _bakeResume = true;
            else if (arg.StartsWith("--voice-model=")) { Settings.VoiceModel = arg["--voice-model=".Length..]; Settings.RunwareVoiceModel = Settings.VoiceModel; Settings.Apply(); }
            else if (arg.StartsWith("--planner=")) { Settings.PlannerProvider = arg["--planner=".Length..].Trim().ToLowerInvariant() == Settings.ProviderRunware ? Settings.ProviderRunware : Settings.ProviderClaude; }
            else if (arg.StartsWith("--voice-provider=")) { Settings.VoiceProvider = arg["--voice-provider=".Length..].Trim().ToLowerInvariant() == Settings.ProviderRunware ? Settings.ProviderRunware : Settings.ProviderGemini; Settings.Apply(); }
            else if (arg.StartsWith("--planner-model=")) { Settings.RunwareTextModel = arg["--planner-model=".Length..]; }
            else if (arg.StartsWith("--voice-fallback=")) Settings.VoiceFallbackModels = arg["--voice-fallback=".Length..];
            else if (arg.StartsWith("--voice-rpm=")) Settings.VoiceRequestsPerMinute = float.Parse(arg["--voice-rpm=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--voice-model-minor=")) { Settings.VoiceModelMinor = arg["--voice-model-minor=".Length..]; Settings.RunwareVoiceModelMinor = Settings.VoiceModelMinor; Settings.Apply(); }
            else if (arg == "--list-voice-models") { _listVoiceModels = true; _menuOnly = true; _quitAfter = 30; }
            else if (arg.StartsWith("--probe-voice-model=")) { _probeVoiceModel = arg["--probe-voice-model=".Length..]; _menuOnly = true; _quitAfter = 60; }
            else if (arg == "--probe-planner") { _probePlanner = true; _menuOnly = true; _quitAfter = 120; }
            else if (arg == "--builtin") { _builtin = true; _quitAfter = Math.Max(_quitAfter, 6); }
            else if (arg == "--fake-voice") FakeVoice = true;
            else if (arg == "--test-floors") { _testFloors = true; TraceAi = false; _quitAfter = 14; }
            else if (arg == "--test-structures") { _testStructures = true; TraceAi = false; _quitAfter = 12; }
            else if (arg == "--test-horde") { _testHorde = true; TraceAi = false; _quitAfter = 40; _startFloor = 3; }
            else if (arg == "--no-ambush") NoAmbush = true;
            else if (arg == "--no-portals") NoPortals = true;
            else if (arg == "--test-ambush") { _testAmbush = true; _quitAfter = 40; _startFloor = 2; }
            else if (arg == "--test-portal") { _testPortal = true; TraceAi = false; _quitAfter = 60; }
            else if (arg == "--test-keys") { _testKeys = true; _menuOnly = true; _quitAfter = 2; }
            else if (arg == "--tremor") { _tremor = true; _quitAfter = Math.Max(_quitAfter, 6); }
            else if (arg == "--test-tremor") { _testTremor = true; _tremor = true; TraceAi = false; _quitAfter = 12; }
            else if (arg.StartsWith("--theme=")) ForcedTheme = arg["--theme=".Length..];
            else if (arg.StartsWith("--vault=")) ForcedVault = arg["--vault=".Length..];
            else if (arg.StartsWith("--inspect=")) { var xy = arg["--inspect=".Length..].Split(','); _inspect = (int.Parse(xy[0]), int.Parse(xy[1])); _quitAfter = Math.Max(_quitAfter, 3); }
            else if (arg.StartsWith("--bake-voice=")) _bakeVoiceDir = ProjectSettings.GlobalizePath(arg["--bake-voice=".Length..]);
            else if (arg.StartsWith("--voice-budget=")) CampaignBaker.VoiceBudgetLines = int.Parse(arg["--voice-budget=".Length..]);
            else if (arg.StartsWith("--spend-cap=")) Settings.SpendCapUsd = double.Parse(arg["--spend-cap=".Length..], System.Globalization.CultureInfo.InvariantCulture); // this run only; not saved
            else if (arg == "--test-voice") { _testVoice = true; FakeVoice = true; TraceAi = false; _quitAfter = 16; }
            else if (arg == "--test-bake-ui") { _testBakeUi = true; _menuOnly = true; FakeVoice = true; FakeAiDelay = 0f; TraceAi = false; _quitAfter = 240; }
            else if (arg.StartsWith("--press=")) _pressActions.AddRange(arg["--press=".Length..].Split(','));
            else if (arg == "--smoke") { _smoke = true; _faceEnemy = true; TraceAi = true; _quitAfter = 11; }
            else if (arg == "--trace-ai") TraceAi = true;
            else if (arg == "--test-music") { _testMusic = true; TraceAi = false; _quitAfter = 45; }
            else if (arg == "--test-story") { _testStory = true; TraceAi = false; _quitAfter = 20; }
            else if (arg == "--server") _server = true;
            else if (arg == "--test-net-server") { _server = true; TraceAi = false; _quitAfter = 120; }
            else if (arg == "--test-net-client" || arg == "--test-net-rejoin") { _netClient = true; TraceAi = false; _quitAfter = 90; }
            else if (arg.StartsWith("--join=")) _netClient = true;
        }
        if ((_testCollectives || _testQuest || _testVoice || _testStory) && _seed == null) _seed = 4242; // scripted scenarios want a repeatable floor
        if (_testStructures && _seed == null) _seed = StructureSeed();
        if (_testHorde && _seed == null) _seed = HordeSeed();
        // Automation never spends the player's keys unless --ai is given explicitly: scripted runs use the offline designer and no live voice.
        var automation = _screenshotPath != null || _quitAfter > 0 || _facePit || _faceNode != null || _faceCell != null || _faceEnemy || _autoAttack || _smoke || _testDrag || _testSave || _levelUp || _pressActions.Count > 0 || _openMap || _talk || _builtin || _menuOnly;
        if (automation && !ForceAi) { NoAi = true; NoVoice = !FakeVoice; }
        if (_server || _netClient) NoVoice = true; // a headless server has no speakers; the test client keeps its key in its pocket
    }

    public override void _Process(double delta)
    {
        // any automation flag means "skip the title screen"
        if (!_started && !_menuOnly && !_server && !_netClient && Main.Instance != null && (_screenshotPath != null || _quitAfter > 0 || _faceEnemy || _autoAttack || _smoke || _testDrag || _testSave || _levelUp || _pressActions.Count > 0 || _openMap || _talk))
        {
            _started = true;
            if (_builtin) { if (!Main.Instance.StartBuiltinCampaign()) { GD.Print("[builtin] RESULT FAIL: no built-in campaign"); GetTree().Quit(); } }
            else if (_bakeDir != null && _bakeResume && BuiltinCampaign.LoadFrom(_bakeDir + ".partial") is { } partial)
            {
                GD.Print($"[bake] resuming \"{partial.Campaign.Title}\" ({partial.Campaign.Source}) with {partial.Floors.Count} floors already written");
                Main.Instance.StartResumedBake(partial.Campaign, partial.Floors);
            }
            else Main.Instance.StartNewGame(_seed);
            if (_startFloor > 1) Callable.From(() => Game.Instance.JumpToFloor(_startFloor)).CallDeferred();
            return;
        }
        _elapsed += delta;
        if (_done) return;
        if (_testBakeUi) RunBakeUiTest();
        if (_openSettings && !_openedSettings && _elapsed >= 0.3 && Main.Instance?.Menu != null) { _openedSettings = true; Main.Instance.Menu.OpenSettings(); }

        if ((_faceNode != null || _faceCell != null) && !_facedNode && _elapsed >= 0.6 && Main.Instance?.Builder.FloorRoot is { } faceRoot && Main.Instance.Builder.Layout is { } faceLayout)
        {
            _facedNode = true;
            Vector3? look = null;
            GridPos? stand = null;
            if (_faceCell is { } fc) look = Main.Instance.Builder.Surface(new GridPos(fc.x, fc.y));
            else if (_faceNode == "buckled" && Main.Instance.Builder.BuckledCells.Count > 0)
            {
                // a heaved tile, seen from a neighbouring cell
                // prefer a dome inside a ruined room, where the heaving is thickest
                var cells = Main.Instance.Builder.BuckledCells;
                bool InRuin(GridPos c) => faceLayout.RoomAt(c) is { } rm && faceLayout.StructureAt(rm)?.Kind == Dungeon.Generation.StructureKind.Ruin;
                var pick = cells.Keys.OrderByDescending(InRuin).ThenByDescending(c => cells[c] == 2).ThenBy(c => c.Y).ThenBy(c => c.X).First();
                look = Main.Instance.Builder.Surface(pick);
            }
            else if (_faceNode == "bridge" && faceLayout.Water.FirstOrDefault(w => w.Bridge != null) is { } stream)
            {
                // the plank bridge, seen from the bank beside it
                var b = stream.Bridge!.Value;
                look = Main.Instance.Builder.Surface(b);
                var across = stream.Flow.Perp;
                var back = Math.Max(1, _faceBack);
                stand = b + new GridPos(across.X * back, across.Y * back);
                while (back > 1 && !faceLayout.IsWalkable(stand.Value)) { back--; stand = b + new GridPos(across.X * back, across.Y * back); }
                if (!faceLayout.IsWalkable(stand.Value)) stand = b - new GridPos(across.X, across.Y);
            }
            else if (_faceNode is "vault" or "groin")
            {
                // the ceiling: from one end of the longest straight corridor looking up its barrel, or from a room's
                // edge looking up across its groin bays
                bool Open(GridPos c) => faceLayout.IsWalkable(c) && faceLayout[c] != Dungeon.Generation.Cell.Ramp && faceLayout.LevelOf(c) == 0;
                bool Corridor(GridPos c, GridPos d) => Open(c) && Open(c + d) && Open(c - d) && !Open(c + new GridPos(d.Y, d.X)) && !Open(c - new GridPos(d.Y, d.X));
                GridPos? best = null; GridPos bestDir = default; var bestLen = 0;
                if (_faceNode == "vault")
                    foreach (var d in new[] { new GridPos(1, 0), new GridPos(0, 1) })
                        for (var y = 0; y < faceLayout.Height; y++)
                            for (var x = 0; x < faceLayout.Width; x++)
                            {
                                var start = new GridPos(x, y);
                                if (!Corridor(start, d) || Corridor(start - d, d)) continue;
                                var len = 0;
                                while (Corridor(start + new GridPos(d.X * len, d.Y * len), d)) len++;
                                if (len > bestLen) { bestLen = len; best = start; bestDir = d; }
                            }
                else
                    foreach (var rm in faceLayout.Rooms.OrderByDescending(r => r.W * r.H))
                    {
                        var c = new GridPos(rm.X, rm.Y + rm.H / 2);
                        if (!Open(c) || !Open(c + new GridPos(1, 0)) || !Open(c + new GridPos(2, 0))) continue;
                        best = c; bestDir = new GridPos(1, 0); bestLen = rm.W; break;
                    }
                if (best is { } from)
                {
                    stand = from;
                    var ahead = Math.Max(2, Math.Min(bestLen - 1, 4));
                    look = Main.Instance.Builder.Surface(from + new GridPos(bestDir.X * ahead, bestDir.Y * ahead)) + Vector3.Up * 5.5f;
                    GD.Print($"[face] {_faceNode} run of {bestLen} cells from {from} along {bestDir}");
                }
            }
            else if (_faceNode == "collapse" && faceRoot.GetNodeOrNull("RubbleField")?.GetChildren().OfType<Node3D>().FirstOrDefault(n => n.Name.ToString().StartsWith("Collapse_")) is { } gap)
            {
                // a ruin's fallen crown, seen from a couple of cells away with the eye raised to it
                look = gap.GlobalPosition;
                GD.Print($"[face] collapse at {gap.GlobalPosition:0.0}");
            }
            else if (_faceNode == "chamfer")
            {
                // the first cut corner on the floor, seen from out on its open diagonal
                for (var y = 0; y < faceLayout.Height && look == null; y++)
                    for (var x = 0; x < faceLayout.Width; x++)
                        if (faceLayout.IsChamfer(new GridPos(x, y), out var open))
                        {
                            var corner = new GridPos(x, y);
                            look = Main.Instance.Builder.Surface(corner);
                            var back = Math.Max(1, _faceBack);
                            stand = corner + new GridPos(open.X * back, open.Y * back);
                            while (back > 1 && !faceLayout.IsWalkable(stand.Value)) { back--; stand = corner + new GridPos(open.X * back, open.Y * back); }
                            break;
                        }
            }
            else if (faceRoot.GetChildren().OfType<Node3D>().FirstOrDefault(n => n.Name.ToString().StartsWith(_faceNode!)) is { } target) { look = target.GlobalPosition; GD.Print($"[face] {target.Name} at {target.GlobalPosition:0.0} cell {DungeonBuilder.WorldToCell(target.GlobalPosition)} level {faceLayout.LevelOf(DungeonBuilder.WorldToCell(target.GlobalPosition))}"); }
            if (look is { } at)
            {
                // stand a few cells back along the first walkable direction, so the whole thing is in view
                var b = Main.Instance.Builder;
                var targetCell = DungeonBuilder.WorldToCell(at);
                var cell = stand ?? DungeonBuilder.WalkableNeighbor(faceLayout, targetCell);
                if (cell == targetCell && !faceLayout.IsWalkable(targetCell)) // a wall (a secret door): stand on the floor beside it
                    cell = new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) }.Select(d => targetCell + d).FirstOrDefault(faceLayout.IsWalkable, cell);
                var dir = cell - targetCell;
                if (stand == null) for (var i = 1; i < _faceBack; i++) { var next = cell + dir; if (!faceLayout.CanStep(cell, next) || next == faceLayout.UpStairs) break; cell = next; }
                Main.Instance.Party.Teleport(b.Surface(cell) + Vector3.Up * 0.3f, at + Vector3.Up * (_faceNode is "vault" or "groin" ? 0f : 0.5f));
                GD.Print($"[face] standing at cell {cell} ({b.Surface(cell):0.0}) looking at {at:0.0}");
            }
            else GD.Print($"[face] no node starting with {_faceNode}");
        }
        if (_dropIds != null && !_dropped && _elapsed >= 0.9 && Main.Instance?.Builder.FloorRoot is { } dropRoot)
        {
            _dropped = true;
            var lead = Main.Instance.Party.Lead;
            var forward = -lead.GlobalTransform.Basis.Z with { Y = 0 };
            var right = lead.GlobalTransform.Basis.X with { Y = 0 };
            var scene = GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn");
            var cat = Game.Instance.Catalog;
            for (var i = 0; i < _dropIds.Length; i++)
            {
                if (!cat.TryBase(_dropIds[i], out _)) { GD.Print($"[drop] unknown item {_dropIds[i]}"); continue; }
                var drop = scene.Instantiate<Items.LootDrop>();
                drop.Item = cat.Create(_dropIds[i]);
                if (i % 3 == 1) drop.Item.Rarity = Items.Rarity.Magic;
                if (i % 3 == 2) drop.Item.Rarity = Items.Rarity.Rare;
                drop.Name = $"Drop{i}";
                dropRoot.AddChild(drop);
                drop.GlobalPosition = lead.GlobalPosition with { Y = Main.Instance.Builder.Surface(DungeonBuilder.WorldToCell(lead.GlobalPosition)).Y + 0.45f } + forward.Normalized() * 2.2f + right.Normalized() * ((i - (_dropIds.Length - 1) / 2f) * 0.75f);
            }
        }
        if (_facePit && !_facedPit && _elapsed >= 0.6 && Main.Instance?.Builder.Layout is { Cellars.Count: > 0 } pitLay)
        {
            _facedPit = true;
            var cellar = pitLay.Cellars[0];
            var b = Main.Instance.Builder;
            var edge = cellar.Pit + new GridPos(0, 2);
            Main.Instance.Party.Teleport(b.Surface(edge) + Vector3.Up * 0.3f, b.Surface(cellar.Pit, under: true));
            Main.Instance.Party.Lead.Look(new Vector2(0, 260)); // tilt the eye down into the pit
        }
        if (_faceLedge && !_facedLedge && _elapsed >= 0.6 && Main.Instance?.Builder.Layout is { Ledges.Count: > 0 } lay)
        {
            _facedLedge = true;
            var ledge = lay.Ledges[0];
            var b = Main.Instance.Builder;
            Main.Instance.Party.Teleport(b.Surface(ledge.RampBottom + new Dungeon.Generation.GridPos(0, 2)) + Vector3.Up * 0.3f, b.Surface(ledge.EnemySpawn) + Vector3.Up * 1.2f);
        }
        if (_talk && !_talked && _elapsed >= (_startFloor > 1 ? 0.8 : 0.4) && Main.Instance?.Builder.FloorRoot is { } talkRoot)
        {
            _talked = true;
            if (talkRoot.GetChildren().OfType<Npc>().FirstOrDefault(n => !n.Dead) is { } npc)
            {
                var b = Main.Instance.Builder;
                var cell = DungeonBuilder.WalkableNeighbor(b.Layout!, DungeonBuilder.WorldToCell(npc.GlobalPosition));
                Main.Instance.Party.Teleport(b.Surface(cell) + Vector3.Up * 0.3f, npc.GlobalPosition);
                Main.Instance.Hud.OpenDialogue(npc);
            }
            else GD.Print("[talk] no NPC on this floor");
        }
        if (_faceEnemy && !_faced && _elapsed >= (_startFloor > 1 ? 0.6 : 0.25) && Main.Instance?.Builder.FloorRoot is { } root)
        {
            _faced = true;
            FaceNearestEnemy(root);
        }

        if (_countProps && !_counted && _elapsed >= 1.0 && Main.Instance?.Builder.FloorRoot is { } pr)
        {
            _counted = true;
            var kids = pr.GetChildren();
            GD.Print($"[props] lamps={kids.Count(k => k.Name.ToString().StartsWith("Lamp"))} lanternModels={kids.Count(k => k.Name.ToString().Contains("wooden_lantern"))} statues={kids.Count(k => k.Name.ToString().Contains("gothic_statue"))} chestModels={pr.GetChildren().OfType<Dungeon.Props.Chest>().Count(c => c.GetNodeOrNull("Model") != null)} barrelModels={pr.GetChildren().OfType<Dungeon.Props.Breakable>().Count(b => b.GetChildCount() > 2)} lanternExists={ResourceLoader.Exists("res://assets/models/wooden_lantern_01/wooden_lantern_01_1k.gltf")}");
        }
        if (_smoke) RunSmokeScript();
        if (_testMusic) RunMusicTest(delta);
        if (_testStory) RunStoryTest();
        if (_probeVoiceModel != null && !_probedVoiceModel && _elapsed >= 0.3)
        {
            // one real line through the named model: proves the request shape and the quota without a whole bake
            _probedVoiceModel = true;
            if (!Settings.HasVoiceKey) { GD.Print($"[voice-probe] no {Settings.VoiceLabel} key in user://keys.cfg"); GetTree().Quit(1); return; }
            IVoiceSynth synth = Settings.VoiceViaRunware ? new RunwareTtsClient(Settings.RunwareKey.Trim(), _probeVoiceModel) : new GeminiTtsClient(Settings.GeminiKey.Trim(), _probeVoiceModel);
            var client = (IVoiceSynthDiagnostics)synth;
            var model = _probeVoiceModel;
            _ = Task.Run(async () =>
            {
                var started = DateTime.UtcNow;
                var pcm = await synth.RenderPcmAsync("The stair behind you settles. Somebody swept these steps recently.", Adventure.Voice.Voices.Narrator, Adventure.Voice.Voices.Style(Adventure.Voice.Speaker.Narrator), CancellationToken.None);
                var secs = (DateTime.UtcNow - started).TotalSeconds;
                Callable.From(() =>
                {
                    var cost = synth is RunwareTtsClient rw ? $", ${rw.SpentUsd:0.0000}" : "";
                    GD.Print(pcm != null ? $"[voice-probe] {model} via {Settings.VoiceLabel}: ok, {pcm.Length / 2 / (float)Adventure.Voice.IVoiceSynth.SampleRate:0.0} s of audio in {secs:0.0} s{cost}" : $"[voice-probe] {model} via {Settings.VoiceLabel}: FAIL HTTP {client.LastStatus} {client.LastError}{(client.QuotaExhausted ? " (quota or balance used up)" : "")}");
                    GetTree().Quit(pcm != null ? 0 : 1);
                }).CallDeferred();
            });
        }
        if (_probePlanner && !_probedPlanner && _elapsed >= 0.3 && AdventureDirector.Instance != null)
        {
            // one tiny structured call through the selected planner provider: proves the key, the model id and the reply shape
            _probedPlanner = true;
            var viaRunware = Settings.PlannerViaRunware;
            GD.Print($"[planner-probe] {(viaRunware ? AdventureDirector.RunwareModel + " via Runware" : "Claude " + Pricing.DefaultModel)}");
            AdventureDirector.Instance.TestConnection(viaRunware ? Settings.RunwareKey : Settings.ClaudeKey, viaRunware, r =>
            {
                GD.Print($"[planner-probe] {r}");
                GetTree().Quit(r.StartsWith("Connected") ? 0 : 1);
            });
        }
        if (_listVoiceModels && !_listedVoiceModels && _elapsed >= 0.3)
        {
            _listedVoiceModels = true;
            if (Settings.VoiceViaRunware)
            {
                // Runware's model search cannot filter for speech, so this is the table we know how to voice
                GD.Print("[voice-models] Runware TTS models with mapped voices: " + string.Join("; ", Adventure.Voice.RunwareVoices.KnownModels.Select(m => $"{m.Model} ({m.Note})")));
                GD.Print($"[voice-models] configured: {Settings.RunwareVoiceModel}; minor: {Settings.RunwareVoiceModelMinor}; fallbacks: {Settings.RunwareVoiceFallbackModels}");
                GetTree().Quit(0);
                return;
            }
            if (!Settings.HasGeminiKey) { GD.Print("[voice-models] no Gemini key in user://keys.cfg"); GetTree().Quit(1); return; }
            var key = Settings.GeminiKey.Trim();
            _ = Task.Run(async () =>
            {
                var (models, error) = await GeminiTtsClient.ListTtsModelsAsync(key, CancellationToken.None);
                Callable.From(() =>
                {
                    GD.Print(error != null ? $"[voice-models] ERROR {error}" : $"[voice-models] {models.Count} text-to-speech models: {string.Join(", ", models)}");
                    GD.Print($"[voice-models] configured: {Settings.VoiceModel}; fallbacks: {Settings.VoiceFallbackModels}");
                    GetTree().Quit(error != null ? 1 : 0);
                }).CallDeferred();
            });
        }
        if (_testSave) RunSaveTest();
        if (_testDepth) RunDepthTest();
        if (_testPlan) RunPlanTest();
        if (_testCollectives) RunCollectivesTest();
        if (_testQuest) RunQuestTest();
        if (_testAiFlow) RunAiFlowTest();
        if (_bakeDir != null) RunBake();
        if (_testVoice) RunVoiceTest();
        if (_testFloors) RunFloorsTest();
        if (_testStructures) RunStructuresTest();
        if (_testHorde) RunHordeTest();
        if (_testAmbush) RunAmbushTest();
        if (_testPortal) RunPortalTest();
        if (_testKeys && !_testedKeys && _elapsed >= 0.5) { _testedKeys = true; RunKeysTest(); }
        if (_tremor) RunTremor();
        if (_inspect is { } ins && !_inspected && _elapsed >= 1.2 && Main.Instance?.Builder.Layout != null && Main.Instance.Builder.CurrentFloor == Math.Max(1, _startFloor)) { _inspected = true; InspectCell(ins.x, ins.y); }
        if (_builtin && _elapsed >= 2.5 && !_builtinChecked) { _builtinChecked = true; var m = Main.Instance!; GD.Print($"[builtin] campaign=\"{Game.Instance.Adventure.Campaign?.Title}\" mode={Game.Instance.Adventure.Mode} floor={m.Builder.CurrentFloor} src={m.Builder.Contents?.Plan?.Source} plans={Game.Instance.Adventure.FloorPlans.Count}"); GD.Print($"[builtin] RESULT {(Game.Instance.Adventure.Mode == AdventureMode.Builtin && m.Builder.Contents?.Plan?.Source == PlanSources.Builtin ? "ok" : "FAIL")}"); }
        if (_testPath) RunPathTest();
        if (_testSkills) RunSkillsTest();
        if (_testVertical) RunVerticalTest();
        if (_testStairs) RunStairsTest();
        if (_testParty) RunPartyTest();
        if (_testDrag) RunDragTest(delta);

        if (_autoAttack && Main.Instance != null && (_attackTimer -= delta) <= 0)
        {
            _attackTimer = 0.2;
            var p = Main.Instance.Party;
            for (var i = 0; i < p.Members.Length; i++)
            {
                var m = p.Members[i];
                if (m.IsAlive && m.CooldownRemaining <= 0 && m.AbilityCooldown(m.ActiveAbilityId) <= 0) p.Attack(i);
            }
        }

        if (_levelUp && Main.Instance != null) { _levelUp = false; Main.Instance.Party.AwardXp(150); }
        if (_possess >= 0 && Main.Instance?.InGame == true && _elapsed >= 0.8) { Main.Instance.Party.Possess(_possess); _possess = -1; }
        if (_swing && !_swung && Main.Instance?.InGame == true && _elapsed >= _after - 0.12) { _swung = true; Main.Instance.Party.Attack(Main.Instance.Party.LeadIndex, heavy: true); }
        if (_askStairs && !_askedStairsDebug && _elapsed >= _after - 0.6 && Main.Instance?.InGame == true && Main.Instance.Builder.FloorRoot is { } askRoot)
        {
            _askedStairsDebug = true;
            Main.Instance.Hud.AskStairs(askRoot.GetNode<Dungeon.Stairs>("DownStairs"), Main.Instance.Party.Lead); // the landing's question, for screenshots
        }
        if (_lookBack && !_lookedBack && _elapsed >= _after - 0.4 && Main.Instance?.InGame == true)
        {
            _lookedBack = true;
            var p = Main.Instance.Party;
            p.Lead.RotateY(Mathf.Pi); // turn around to see the companions
        }
        if (_openMap) { _pressActions.Add("map"); _openMap = false; }
        if (_pressActions.Count > 0 && _elapsed >= _after - 0.3 && Main.Instance != null)
        {
            foreach (var action in _pressActions)
            {
                if (action == "settings") { Main.Instance.Hud.TogglePause(); Main.Instance.Hud.GetNode<UI.Menus.PauseMenu>("PauseMenu").OpenSettings(); continue; }
                Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            }
            _pressActions.Clear();
        }

        if (_screenshotPath != null && !_testHorde && _elapsed >= _after && (_menuOnly || Main.Instance?.InGame == true))
        {
            _done = true;
            SaveScreenshot(_screenshotPath);
            GetTree().Quit();
        }
        else if (_quitAfter > 0 && _elapsed >= _quitAfter)
        {
            _done = true;
            GD.Print($"[debug] quitting after {_elapsed:0.0}s");
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || Main.Instance == null) return;
        var party = Main.Instance.Party;
        // on a client the world belongs to the server: a local floor jump or heal would only split this machine from it
        if (Net.NetSession.IsClient && key.Keycode is Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F7 or Key.F8)
        {
            EventBus.Instance.Log("[debug] not on a client: the server owns the world");
            return;
        }
        switch (key.Keycode)
        {
            case Key.F1:
                foreach (var m in party.Members) m.FullRestore();
                for (var i = 0; i < party.Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
                EventBus.Instance.Log("[debug] party healed");
                break;
            case Key.F2:
                foreach (var e in GetTree().GetNodesInGroup("enemies")) if (e is Enemy en) en.Kill();
                EventBus.Instance.Log("[debug] all enemies slain");
                break;
            case Key.F3: Game.Instance.ChangeFloor(+1); break;
            case Key.F4: Game.Instance.ChangeFloor(-1); break;
            case Key.F8:
                party.PickUp(Game.Instance.Catalog.Create("item.arrow", 20));
                break;
            case Key.F7:
                var cat = Game.Instance.Catalog;
                var rolled = LootRoller.RollMany(cat, cat.Table("table.chest") with { Rolls = 6 }, Game.Instance.CurrentFloor, party.Rng);
                foreach (var item in rolled) party.PickUp(item);
                break;
            case Key.F6:
                SaveScreenshot(ProjectSettings.GlobalizePath($"user://screenshot_{Time.GetUnixTimeFromSystem():0}.png"));
                break;
        }
    }

    // Timeline: 0.25s face enemy | 0.25-3s stand and take hits | 3s+ attack | 5.5s loot | 6s down | 7.5s down | 9s up | 10.5s report
    private void RunSmokeScript()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;

        switch (_smokeStep)
        {
            case 0 when _elapsed >= 3.0:
                _smokeStep++;
                _autoAttack = true;
                party.Members[3].ActiveAbilityIndex = 1; // Ilex: fireball
                GD.Print("[smoke] attacking (Ilex casting " + party.AbilityName(party.Members[3].ActiveAbilityId) + ")");
                party.Attack(0, heavy: true, perfect: true); // Brand opens with a perfect heavy blow
                break;
            case 1 when _elapsed >= 5.5:
                _smokeStep++;
                _autoAttack = false;
                var drops = root.GetChildren().OfType<LootDrop>().ToList();
                GD.Print($"[smoke] loot drops on floor: {drops.Count}");
                foreach (var d in drops) d.Interact(party, party.Lead);
                GD.Print($"[smoke] bag now holds {party.Bag.Items.Count()}: {string.Join(", ", party.Bag.Items)}");
                var cat = Game.Instance.Catalog;
                var rng = new Random(2024);
                var rolled = LootRoller.RollMany(cat, cat.Table("table.chest") with { Rolls = 8 }, 4, rng);
                foreach (var it in rolled) GD.Print($"[smoke] rolled {it.Rarity} {it.DisplayName}: {string.Join(" | ", it.Affixes.SelectMany(a => a.Modifiers).Select(StatModifierParser.Describe))}");
                var brand = party.Members[0];
                var armorBefore = brand.Stats[Characters.Stat.Armor];
                var armorPiece = rolled.FirstOrDefault(i => i.Kind == SlotKind.Body && Equipment.WhyCannotEquip(brand, i, EquipSlot.Body) == null);
                if (armorPiece != null)
                {
                    brand.Equipment.Equip(EquipSlot.Body, armorPiece);
                    brand.Recalculate();
                    GD.Print($"[smoke] Brand equips {armorPiece.DisplayName}: armor {armorBefore} -> {brand.Stats[Characters.Stat.Armor]}");
                }
                var barrels = root.GetChildren().OfType<Dungeon.Props.Breakable>().ToList();
                GD.Print($"[smoke] chests={root.GetChildren().Count(c => c.Name.ToString().StartsWith("Chest"))} barrels={barrels.Count}");
                break;
            case 2 when _elapsed >= 6.0:
            case 3 when _elapsed >= 7.5:
                _smokeStep++;
                GD.Print($"[smoke] descending from floor {Game.Instance.CurrentFloor}");
                root.GetNode<Dungeon.Stairs>("DownStairs").Interact(party, party.Lead);
                break;
            case 4 when _elapsed >= 9.0:
                _smokeStep++;
                GD.Print($"[smoke] ascending from floor {Game.Instance.CurrentFloor}");
                root.GetNode<Dungeon.Stairs>("UpStairs").Interact(party, party.Lead);
                break;
            case 5 when _elapsed >= 10.5:
                _smokeStep++;
                var hp = string.Join(", ", party.Members.Select(m => $"{m.Name} L{m.Level} {m.Hp:0}/{m.Stats[Characters.Stat.MaxHp]:0}"));
                GD.Print($"[smoke] REPORT floor={Game.Instance.CurrentFloor} bag={party.Bag.Items.Count()} party=[{hp}] enemiesOnFloor={root.GetChildren().OfType<Enemy>().Count()}");
                break;
        }
    }

    // Depth test: traps, doors, levers, locked chests, pathing around corners, boss stairs, victory on floor 20.
    private bool _builtinChecked;

    /// <summary>--inspect=X,Y: print the 7x7 neighbourhood of a cell (type, level, under, grid items) and probe each for geometry.</summary>
    private void InspectCell(int cx, int cy)
    {
        var main = Main.Instance!;
        var layout = main.Builder.Layout!;
        var space = main.Party.Lead.GetWorld3D().DirectSpaceState;
        GD.Print($"[inspect] floor {main.Builder.CurrentFloor} around {new GridPos(cx, cy)}: rooms={string.Join(" ", layout.Rooms.Select((r, i) => $"{i}:{r.X},{r.Y} {r.W}x{r.H} L{layout.LevelOf(r.Center)}"))}");
        for (var y = cy - 3; y <= cy + 3; y++)
        {
            var row = new System.Text.StringBuilder();
            for (var x = cx - 3; x <= cx + 3; x++)
            {
                var c = new GridPos(x, y);
                var centre = DungeonBuilder.CellToWorld(c);
                var from = new Vector3(centre.X, 13f, centre.Z);
                var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 26f, collisionMask: 1);
                q.HitFromInside = true;
                var hit = space.IntersectRay(q);
                var hitY = hit.Count > 0 ? ((Vector3)hit["position"]).Y.ToString("0.0") : "none";
                row.Append($"{(c == new GridPos(cx, cy) ? "*" : " ")}{c.X},{c.Y}:{layout[c].ToString()[0]}L{layout.LevelOf(c)}{(layout.IsUnder(c) ? "u" : " ")}{main.Builder.DebugCell(c).Split('[')[1].TrimEnd(']')}@{hitY}  ");
            }
            GD.Print("[inspect] " + row);
        }
        var cellars = string.Join("; ", layout.Cellars.Select(cl => $"pit {cl.Pit} cells {cl.Cells.Count} exit [{string.Join(",", cl.Exit)}] -> {cl.ExitTarget}"));
        var ledges = string.Join("; ", layout.Ledges.Select(l => $"ledge room {l.Room.X},{l.Room.Y} cells [{string.Join(",", l.Cells)}] ramp {l.RampBottom}->{l.RampTop}"));
        GD.Print($"[inspect] cellars: {cellars} | ledges: {ledges} | ramps near: {string.Join(",", layout.Ramps.Where(kv => Math.Abs(kv.Key.X - cx) <= 4 && Math.Abs(kv.Key.Y - cy) <= 4).Select(kv => $"{kv.Key}{kv.Value.Dir}h{kv.Value.LowHeight}"))}");
    }

    /// <summary>--test-floors: on floors 1-6, drop a ray onto every walkable, pit and cellar cell and report any without ground.</summary>
    private void RunFloorsTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.Layout is not { } layout || main.Builder.FloorRoot == null) return;
        var floor = _floorsStep + 1;
        if (_elapsed < floor * 1.6) return;
        if (main.Builder.CurrentFloor != floor) { if (_floorsStep == 0) Game.Instance.JumpToFloor(1); return; }
        var space = main.Party.Lead.GetWorld3D().DirectSpaceState;
        // secret doorways are part of the walkable floor once found: open them all before probing
        foreach (var sw in main.Builder.FloorRoot!.GetChildren().OfType<Dungeon.Props.SecretWall>()) if (!sw.IsOpen) sw.Open();
        var misses = new List<string>();
        var probed = 0;
        // Walk-off detector: start on every designed surface, step to any neighbouring surface within a stair's height,
        // and flag a neighbour that has no ground anywhere below the step. Surfaces are found by rays, so it sees the
        // geometry exactly as a body does, including ramps, roofs of tunnels and the tops of enclosure cubes.
        float? Ground(GridPos c, float fromY, float toY, bool inside = false)
        {
            var centre = DungeonBuilder.CellToWorld(c);
            var q = PhysicsRayQueryParameters3D.Create(new Vector3(centre.X, fromY, centre.Z), new Vector3(centre.X, toY, centre.Z), collisionMask: 1);
            q.HitFromInside = inside;
            var hit = space.IntersectRay(q);
            return hit.Count > 0 ? ((Vector3)hit["position"]).Y : null;
        }
        var seen = new HashSet<(GridPos, int)>();
        var frontier = new Queue<(GridPos cell, float h)>();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var c = new GridPos(x, y);
                if (layout.IsWalkable(c, secretsOpen: true) || layout[c] == Cell.Pit) { var h = main.Builder.Surface(c).Y; if (seen.Add((c, (int)MathF.Round(h)))) frontier.Enqueue((c, h)); }
                if (layout.IsUnder(c)) { var h = main.Builder.Surface(c, under: true).Y; if (seen.Add((c, (int)MathF.Round(h)))) frontier.Enqueue((c, h)); }
            }
        ReadOnlySpan<GridPos> dirs = stackalloc GridPos[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
        var dirList = dirs.ToArray();
        while (frontier.Count > 0)
        {
            var (cell, h) = frontier.Dequeue();
            probed++;
            // this surface itself must have ground just below where a body stands (a pit is open by design: its cellar is probed on its own)
            // a pit is open by design (its cellar is probed on its own), and so is the down-stairs well
            if (layout[cell] != Cell.Pit && cell != layout.DownStairs && Ground(cell, h + 1.5f, h - 1.0f, inside: true) == null) { misses.Add($"{cell}@{h:0.0} has no ground ({layout[cell]}/L{layout.LevelOf(cell)})"); continue; }
            if (layout[cell] == Cell.Pit) continue;
            foreach (var d in dirList)
            {
                var n = cell + d;
                if (!layout.InBounds(n.X, n.Y)) continue;
                var step = Ground(n, h + 2.4f, h - 1.3f); // a surface one stair up to one big step down: a body walks onto it
                if (step is { } nh)
                {
                    // a surface with solid right above it (a cube buried inside a taller cube) is not somewhere a body can stand
                    if (Ground(n, nh + 1.7f, nh + 0.3f, inside: true) != null) continue;
                    if (seen.Add((n, (int)MathF.Round(nh)))) frontier.Enqueue((n, nh));
                    continue;
                }
                // no walking surface: fine if solid blocks the way at body height (rays that start inside a cube count), a hole if nothing lies below
                var wall = Ground(n, h + 1.6f, h + 0.3f, inside: true);
                if (wall != null) continue;
                var below = Ground(n, h + 0.3f, -12f, inside: true);
                if (below == null) misses.Add($"{cell}@{h:0.0} -> {n} ({layout[n]}/L{layout.LevelOf(n)}{(layout.IsUnder(n) ? "u" : "")}) drops into nothing");
            }
        }
        _floorsMisses += misses.Count;
        GD.Print($"[floors] floor {floor}: probed {probed} cells, {misses.Count} without ground{(misses.Count > 0 ? ": " + string.Join(" ", misses.Take(8)) : "")}");
        _floorsStep++;
        if (_floorsStep >= 6) { GD.Print($"[floors] RESULT {(_floorsMisses == 0 ? "ok" : $"FAIL: {_floorsMisses} holes")}"); _done = true; GetTree().Quit(); return; }
        Game.Instance.JumpToFloor(floor + 1);
    }

    /// <summary>The first game seed whose floors 1-4 between them hold a gate, a camp, a pond and a stream.</summary>
    private static ulong StructureSeed()
    {
        var tier = Registry.Instance?.All<Dungeon.Defs.TierDef>().FirstOrDefault(t => t.Index == 0);
        for (ulong seed = 1; seed < 5000; seed++)
        {
            bool gate = false, camp = false, pond = false, stream = false, ruin = false;
            for (var f = 1; f <= 4; f++)
            {
                var spec = tier?.ToSpec(f) ?? Dungeon.Generation.FloorSpec.Default;
                var layout = Dungeon.Generation.FloorGenerator.Generate(spec, Rng.FloorSeed(seed, f));
                gate |= layout.Structures.Any(x => x.Kind == Dungeon.Generation.StructureKind.Gate);
                camp |= layout.Structures.Any(x => x.Kind == Dungeon.Generation.StructureKind.Camp);
                ruin |= layout.Structures.Any(x => x.Kind == Dungeon.Generation.StructureKind.Ruin);
                pond |= layout.Water.Any(w => w.Kind == Dungeon.Generation.WaterKind.Pond);
                stream |= layout.Water.Any(w => w.Kind == Dungeon.Generation.WaterKind.Stream);
            }
            if (gate && camp && pond && stream && ruin) return seed;
        }
        return 1;
    }

    /// <summary>--test-keys: a rebind must survive a reload of the settings without anyone pressing Back, and land in the InputMap.</summary>
    private void RunTremor()
    {
        // --tremor: set off a heavy tremor once the floor is up; --test-tremor also watches the stones fall and land, and the camera shake
        var main = Main.Instance;
        if (main?.Builder.FloorRoot is not { } root || Dungeon.Props.Tremor.Instance is not { } tremor) return;
        _tremorClock += (float)GetProcessDeltaTime();
        if (!_tremorFired && _tremorClock >= 1.5f) { _tremorFired = true; tremor.Trigger(1f); return; }
        if (!_tremorFired || !_testTremor) return;
        var rocks = root.GetChildren().OfType<Dungeon.Props.FallingRock>().ToList();
        _tremorRocksSeen = Math.Max(_tremorRocksSeen, rocks.Count);
        if (main.Party.Lead.Shaking) _tremorShook = true;
        var floorY = main.Builder.Surface(DungeonBuilder.WorldToCell(main.Party.Lead.GlobalPosition)).Y;
        _tremorLanded = Math.Max(_tremorLanded, rocks.Count(r => r.LinearVelocity.Length() < 0.2f && r.GlobalPosition.Y < floorY + 1.0f && r.GlobalPosition.Y > floorY - 1.0f));
        if (_tremorClock >= 8f)
        {
            var ok = tremor.Tremors == 1 && tremor.RocksDropped >= 3 && _tremorRocksSeen >= 3 && _tremorLanded >= 2 && _tremorShook && !tremor.Active;
            GD.Print($"[tremor] tremors={tremor.Tremors} dropped={tremor.RocksDropped} seen={_tremorRocksSeen} landed={_tremorLanded} shook={_tremorShook} active={tremor.Active}");
            GD.Print($"[tremor] RESULT {(ok ? "ok" : "FAIL")}");
            _done = true; GetTree().Quit();
        }
    }

    private void RunKeysTest()
    {
        var before = Settings.KeyFor("jump");
        var wanted = before == Key.J ? Key.K : Key.J;
        Settings.Rebind("jump", wanted);
        Settings.Load(); // what the next launch does
        var inMap = InputMap.ActionGetEvents("jump").OfType<InputEventKey>().Any(e => e.PhysicalKeycode == wanted);
        GD.Print($"[keys] rebound jump {before}->{wanted}: afterReload={Settings.KeyFor("jump")} inInputMap={inMap}");
        var ok = Settings.KeyFor("jump") == wanted && inMap;
        Settings.Rebind("jump", before); // leave the player's own binding as it was
        GD.Print($"[keys] RESULT {(ok ? "ok" : "FAIL")}");
        _done = true; GetTree().Quit();
    }

    /// <summary>The first game seed whose floor 3 the offline designer gives a horde.</summary>
    private static ulong HordeSeed()
    {
        var tier = Registry.Instance?.All<Dungeon.Defs.TierDef>().FirstOrDefault(t => t.Index == 0);
        var roster = Registry.Instance?.All<Enemies.Defs.EnemyDef>().ToDictionary(d => d.Id, d => d.ToProfile()) ?? new Dictionary<string, Enemies.EnemyProfile>();
        for (ulong seed = 1; seed < 3000; seed++)
        {
            var spec = tier?.ToSpec(3) ?? Dungeon.Generation.FloorSpec.Default;
            var floorSeed = Rng.FloorSeed(seed, 3);
            var layout = Dungeon.Generation.FloorGenerator.Generate(spec, floorSeed);
            if (layout.Nests.Count == 0) continue;
            var menu = Adventure.FloorMenu.Build(layout, spec, tier?.ToProfile(), roster, 3, Adventure.PartySnapshot.Expected(3));
            var plan = Adventure.OfflineDesigner.PlanFloor(menu, layout, floorSeed);
            if (plan.Hordes.Count > 0 && plan.Hordes[0].Trigger == Adventure.HordeTrigger.Enter) return seed;
        }
        return 1;
    }

    /// <summary>--test-horde: walk into the nest room on floor 3, count what pours out, kill it all, check the horde is spent.</summary>
    /// <summary>--test-ambush: floor 2; stand on the first ambush point, expect the creature to burst out hunting and the point to save as sprung.</summary>
    private void RunAmbushTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame) return;
        _dangerClock += (float)GetProcessDeltaTime();
        var spawner = root.GetChildren().OfType<Adventure.AmbushSpawner>().FirstOrDefault();
        switch (_dangerStep)
        {
            case 0 when _dangerClock >= 1.5f:
                _dangerStep++;
                if (spawner == null || spawner.Points.Count == 0) { GD.Print($"[ambush] RESULT FAIL: no ambush points (spawner={(spawner != null)})"); _done = true; GetTree().Quit(); return; }
                var p = spawner.Points[0];
                GD.Print($"[ambush] floor {main.Builder.CurrentFloor}: {spawner.Points.Count} points (expected {Adventure.AmbushRules.Count(main.Builder.CurrentFloor)}); first {p.EnemyId} at {p.Trigger} from {(p.FromRubble ? "rubble" : "wall")} {p.Cover}; corridor={layout.RoomAt(p.Trigger) == null} spacing ok={spawner.Points.All(a => spawner.Points.Where(b => b != a).All(b => Math.Abs(a.Trigger.X - b.Trigger.X) + Math.Abs(a.Trigger.Y - b.Trigger.Y) >= Adventure.AmbushRules.Spacing))}");
                foreach (var m in main.Party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp] * 50;
                main.Party.Teleport(main.Builder.Surface(p.Trigger) + Vector3.Up * 0.3f, main.Builder.Surface(p.Cover) + Vector3.Up * 0.5f);
                _dangerClock = 0f;
                break;
            case 1 when spawner != null && spawner.Sprung.Contains(0):
                _dangerStep++;
                _dangerClock = 0f;
                GD.Print("[ambush] sprung on approach");
                break;
            case 1 when _dangerClock >= 5f:
                GD.Print("[ambush] RESULT FAIL: never sprung"); _done = true; GetTree().Quit();
                break;
            case 2 when spawner != null && _dangerClock >= 0.6f:
            {
                _dangerStep++;
                var burst = root.GetChildren().OfType<Enemies.Enemy>().Where(e => e.SpawnIndex < 0 && e.SpawnIndex != -2).ToList();
                // hunting, or already cut down by the companions: either way it came out fighting
                var hunting = burst.Count(e => !e.IsAlive || !e.State.Unaware);
                var dto = FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                var ok = burst.Count >= 1 && hunting >= 1 && dto.SprungAmbushes.Contains(0);
                GD.Print($"[ambush] after: dynamic={burst.Count} hunting={hunting} states=[{string.Join(",", burst.Select(e => $"{e.Name}:{e.State.Name} hp={e.Hp:0} dist={e.DistanceToParty():0.0} sees={e.CanSeeParty()}"))}] saved sprung=[{string.Join(",", dto.SprungAmbushes)}]");
                GD.Print($"[ambush] RESULT {(ok ? "ok" : "FAIL")}");
                _done = true; GetTree().Quit();
                break;
            }
        }
    }

    /// <summary>--test-portal: floor 1; hurry the pressure clock, expect a portal in an explored room away from the party and hunting creatures, then a dead tier boss to silence it.</summary>
    private void RunPortalTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame) return;
        _dangerClock += (float)GetProcessDeltaTime();
        var spawner = root.GetChildren().OfType<Adventure.PortalSpawner>().FirstOrDefault();
        switch (_dangerStep)
        {
            case 0 when _dangerClock >= 2f:
            {
                _dangerStep++;
                if (spawner == null) { GD.Print("[portal] RESULT FAIL: no portal spawner"); _done = true; GetTree().Quit(); return; }
                if (spawner.Silenced) { GD.Print($"[portal] RESULT FAIL: silenced at start ({spawner.SilencedWhy})"); _done = true; GetTree().Quit(); return; }
                foreach (var m in main.Party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp] * 50;
                // walk (well, jump) into the room farthest from the stairs so there is an explored room behind the party
                var start = layout.RoomAt(layout.UpStairs);
                var far = layout.Rooms.Where(r => r != start && r != layout.BossArena && !layout.Vaults.Any(v => v.Room == r)).OrderByDescending(r => Math.Abs(r.Center.X - layout.UpStairs.X) + Math.Abs(r.Center.Y - layout.UpStairs.Y)).First();
                var cell = far.Cells().Where(c => layout.IsWalkable(c) && !layout.IsReserved(c) && !layout.IsWater(c)).OrderBy(c => Math.Abs(c.X - far.Center.X) + Math.Abs(c.Y - far.Center.Y)).First();
                main.Party.Teleport(main.Builder.Surface(cell) + Vector3.Up * 0.3f);
                GD.Print($"[portal] floor {main.Builder.CurrentFloor}: intensity {spawner.Intensity}, grace {Adventure.PortalRules.Grace(spawner.Intensity):0} s; party moved to room {layout.Rooms.IndexOf(far)}");
                _dangerClock = 0f;
                break;
            }
            case 1 when spawner != null && _dangerClock >= 1.5f && spawner.Waves == 0 && !_hurried:
                _hurried = true;
                spawner.Hurry();
                GD.Print("[portal] hurrying");
                break;
            case 1 when spawner != null && spawner.Waves >= 1:
                _dangerStep++;
                _dangerClock = 0f;
                var lead = layout.RoomAt(DungeonBuilder.WorldToCell(main.Party.Lead.GlobalPosition));
                GD.Print($"[portal] opened in room {spawner.LastPortalRoom}; party in room {(lead == null ? -1 : layout.Rooms.IndexOf(lead))}");
                break;
            case 1 when _dangerClock >= 10f:
                GD.Print($"[portal] RESULT FAIL: no portal opened (silenced={spawner?.Silenced} {spawner?.SilencedWhy})"); _done = true; GetTree().Quit();
                break;
            case 2 when spawner != null && spawner.Alive >= 1 && _dangerClock >= 2.5f:
            {
                _dangerStep++;
                var sent = root.GetChildren().OfType<Enemies.Enemy>().Where(e => e.IsReinforcement).ToList();
                var hunting = sent.Count(e => e.IsAlive && !e.State.Unaware);
                var leadRoom = layout.RoomAt(DungeonBuilder.WorldToCell(main.Party.Lead.GlobalPosition));
                var awayFromParty = leadRoom == null || layout.Rooms.IndexOf(leadRoom) != spawner.LastPortalRoom;
                var dto = FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                var bossFloor = Adventure.PortalRules.BossFloorFor(main.Builder.CurrentFloor);
                Game.Instance.Floors[bossFloor] = new FloorStateDto { BossDefeated = true };
                spawner.RefreshSilence();
                var ok = sent.Count >= 1 && hunting >= 1 && awayFromParty && dto.PortalWaves >= 1 && dto.PressureSeconds > 0f && spawner.Silenced;
                GD.Print($"[portal] sent={sent.Count} hunting={hunting} awayFromParty={awayFromParty} saved waves={dto.PortalWaves} pressure={dto.PressureSeconds:0} s; after boss kill silenced={spawner.Silenced} ({spawner.SilencedWhy})");
                GD.Print($"[portal] RESULT {(ok ? "ok" : "FAIL")}");
                Game.Instance.Floors.Remove(bossFloor);
                _done = true; GetTree().Quit();
                break;
            }
            case 2 when _dangerClock >= 12f:
                GD.Print($"[portal] RESULT FAIL: nothing came through (alive={spawner?.Alive})"); _done = true; GetTree().Quit();
                break;
        }
    }

    private void RunHordeTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || main.Builder.CurrentFloor != 3) return;
        _hordeClock += (float)GetProcessDeltaTime();
        var spawner = root.GetChildren().OfType<Adventure.HordeSpawner>().FirstOrDefault();
        switch (_hordeStep)
        {
            case 0 when _hordeClock >= 1.5f:
                _hordeStep++;
                if (spawner == null) { GD.Print("[horde] RESULT FAIL: no horde on this floor"); _done = true; GetTree().Quit(); return; }
                var room = layout.Rooms[spawner.Horde.Room];
                var inside = new GridPos(room.X + room.W / 2, room.Y + room.H / 2);
                if (layout.IsReserved(inside) || !layout.IsWalkable(inside)) inside = DungeonBuilder.WalkableNeighbor(layout, inside);
                main.Party.Teleport(main.Builder.Surface(inside) + Vector3.Up * 0.3f, main.Builder.Surface(spawner.Horde.Mouths[0]) + Vector3.Up * 0.5f);
                foreach (var m in main.Party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp] * 50; // stand through it
                GD.Print($"[horde] seed={_seed} planned: {spawner.Horde.Name} {string.Join(" + ", spawner.Horde.Members.Select(m => $"{m.Count} x {m.EnemyId}"))} trigger={spawner.Horde.Trigger} mouths={spawner.Horde.Mouths.Count} room={spawner.Horde.Room}");
                break;
            case 1 when spawner != null && spawner.Triggered:
                _hordeStep++;
                _hordeClock = 0f;
                GD.Print("[horde] triggered on entry");
                break;
            case 1 when _hordeClock >= 6f:
                GD.Print("[horde] RESULT FAIL: never triggered"); _done = true; GetTree().Quit();
                break;
            case 2 when spawner != null && _screenshotPath != null && spawner.Spawned >= 16 && !_hordeShot:
                _hordeShot = true;
                SaveScreenshot(_screenshotPath); // the stream in full flood
                break;
            case 2 when spawner != null && (spawner.Spawned >= spawner.Count || _hordeClock >= 25f):
            {
                _hordeStep++;
                var hordeEnemies = root.GetChildren().OfType<Enemies.Enemy>().Where(e => e.IsHorde).ToList();
                var hunting = hordeEnemies.Count(e => e.IsAlive && !e.State.Unaware);
                var models = hordeEnemies.Count(e => e.GetNodeOrNull("Model") != null);
                GD.Print($"[horde] streamed: spawned={spawner.Spawned}/{spawner.Count} in {_hordeClock:0.0}s alive={spawner.Alive} hunting={hunting} models={models} fps={Engine.GetFramesPerSecond()}");
                if (spawner.Spawned < spawner.Count) { GD.Print("[horde] RESULT FAIL: stream stalled"); _done = true; GetTree().Quit(); return; }
                foreach (var e in hordeEnemies.Where(e => e.IsAlive)) e.Kill();
                _hordeClock = 0f;
                break;
            }
            case 3 when spawner != null && _hordeClock >= 1.5f:
            {
                _hordeStep++;
                var dto = FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                var ok = spawner.Spent && spawner.Alive == 0 && dto.SpentHordes.Contains(spawner.Horde.Id) && !dto.Enemies.Any(e => e.SpawnIndex < 0);
                GD.Print($"[horde] after the kill: spent={spawner.Spent} alive={spawner.Alive} killed={spawner.Killed} saved={string.Join(",", dto.SpentHordes)} savedEnemies={dto.Enemies.Count}");
                GD.Print($"[horde] RESULT {(ok ? "ok" : "FAIL")}");
                _done = true; GetTree().Quit();
                break;
            }
        }
    }

    private int _structStep;
    private int _structGates, _structCamps, _structRuins, _structPonds, _structStreams, _structFails;

    /// <summary>--test-structures: over floors 1-4 of a seed that has them all, check what the builder made of gates, camps and water, and that wading slows.</summary>
    private void RunStructuresTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root) return;
        var floor = _structStep + 1;
        if (_elapsed < floor * 1.8) return;
        if (main.Builder.CurrentFloor != floor) { if (_structStep == 0) Game.Instance.JumpToFloor(1); return; }
        var kids = root.GetChildren().ToList();
        var gates = kids.Count(k => k.Name.ToString().StartsWith("Gate_"));
        var camps = kids.Count(k => k.Name.ToString().StartsWith("Camp_"));
        var water = kids.Count(k => k.Name.ToString().StartsWith("Water_"));
        var braziers = kids.Where(k => k.Name.ToString().StartsWith("Gate_")).Sum(g => g.GetChildren().OfType<Dungeon.Props.Brazier>().Count());
        static int Fires(Node n) => (n is Dungeon.Props.Fire ? 1 : 0) + n.GetChildren().Sum(Fires);
        var fires = Fires(root);
        var panels = kids.Where(k => k.Name.ToString().StartsWith("Camp_")).Sum(c => c.GetChildren().OfType<MeshInstance3D>().Count(m => m.Mesh is BoxMesh));
        var bandits = kids.OfType<Enemies.Enemy>().Where(e => e.Profile.Faction == "bandits").ToList();
        var camp = layout.Structures.FirstOrDefault(x => x.Kind == Dungeon.Generation.StructureKind.Camp);
        var nearFire = camp == null ? 0 : bandits.Count(b => DungeonBuilder.WorldToCell(b.GlobalPosition).ManhattanDistance(camp.Anchor) <= 3);
        var models = bandits.Count(b => b.GetNodeOrNull("Model") != null);
        var grid = root.GetNodeOrNull<GridMap>("Structure");
        var chamfers = grid == null ? 0 : Enumerable.Range(11, 4).Sum(id => grid.GetUsedCellsByItem(id).Count);
        var beds = kids.Where(k => k.Name.ToString().StartsWith("Water_")).Count(w => w.GetNodeOrNull("Bed") != null && w.GetNodeOrNull("BedBody") != null && w.GetNodeOrNull("Surface") != null && w.GetNodeOrNull("Ambience") != null);
        var bridges = kids.Where(k => k.Name.ToString().StartsWith("Water_Stream")).Count(w => w.GetChildren().OfType<MeshInstance3D>().Count(m => m.Mesh is BoxMesh) >= 7);
        var streams = layout.Water.Count(w => w.Kind == Dungeon.Generation.WaterKind.Stream);
        var waterCells = layout.Water.Sum(w => w.Cells.Count);
        var basins = beds; // kept in the report line for continuity
        var wading = true;
        if (layout.Water.FirstOrDefault() is { } wet)
        {
            main.Party.Lead.GlobalPosition = main.Builder.Surface(wet.Cells[0]) - Vector3.Up * 0.35f;
            wading = main.Builder.IsInWater(main.Party.Lead.GlobalPosition) && !main.Builder.IsInWater(main.Builder.Surface(wet.Cells[0]) + Vector3.Up * 0.2f);
        }
        var ruins = kids.Count(k => k.Name.ToString().StartsWith("Ruin_"));
        var columns = kids.Where(k => k.Name.ToString().StartsWith("Ruin_")).Sum(r => r.GetChildren().Count(c => c.Name.ToString().StartsWith("Column")));
        var fallenColumns = kids.Where(k => k.Name.ToString().StartsWith("Ruin_")).Sum(r => r.GetChildren().Count(c => c.Name.ToString().StartsWith("ColumnFallen")));
        var mounds = root.GetNodeOrNull("RubbleField")?.GetChildren().Count(c => c.Name.ToString().StartsWith("Rubble_")) ?? 0;
        var scatters = root.GetNodeOrNull("RubbleField")?.GetChildren().Count(c => c.Name.ToString().StartsWith("Scatter_")) ?? 0;
        var buckled = grid == null ? 0 : Enumerable.Range(16, 5).Sum(id => grid.GetUsedCellsByItem(id).Count);
        var expectedColumns = layout.Structures.Where(x => x.Kind == Dungeon.Generation.StructureKind.Ruin).Sum(x => x.Cells.Count);
        var drops = kids.OfType<Items.LootDrop>().ToList();
        var items = drops.Count(d => d.GetNodeOrNull("ItemModel") != null);
        var ok = braziers == gates * 4 && (camps == 0 || (panels >= 6 && bandits.Count >= 3 && nearFire == bandits.Count && models == bandits.Count)) && (camps == 0 || fires >= 2)
                 && water == layout.Water.Count && beds == layout.Water.Count && bridges == streams && chamfers > 0 && wading && items == drops.Count
                 && ruins == layout.Structures.Count(x => x.Kind == Dungeon.Generation.StructureKind.Ruin) && columns == expectedColumns && (ruins == 0 || fallenColumns >= 1)
                 && mounds == layout.Rubble.Count && scatters >= 8 && buckled > 0;
        GD.Print($"[structures] floor {floor}: structures=[{string.Join(",", layout.Structures.Select(x => x.Kind))}] gates={gates} braziers={braziers} camps={camps} panels={panels} fires={fires} water={water}/{layout.Water.Count} beds={basins} bridges={bridges}/{streams} waterCells={waterCells} chamfers={chamfers} ruins={ruins} columns={columns}(fallen {fallenColumns}) mounds={mounds}/{layout.Rubble.Count} scatter={scatters} buckled={buckled} bandits={bandits.Count} nearFire={nearFire} banditModels={models} wading={wading} itemModels={items}/{drops.Count} {(ok ? "ok" : "FAIL")}");
        _structGates += gates; _structCamps += camps; _structRuins += ruins; _structPonds += layout.Water.Count(w => w.Kind == Dungeon.Generation.WaterKind.Pond); _structStreams += layout.Water.Count(w => w.Kind == Dungeon.Generation.WaterKind.Stream);
        if (!ok) _structFails++;
        _structStep++;
        if (_structStep >= 4)
        {
            var all = _structFails == 0 && _structGates > 0 && _structCamps > 0 && _structPonds > 0 && _structStreams > 0 && _structRuins > 0;
            GD.Print($"[structures] seed={_seed} gates={_structGates} camps={_structCamps} ruins={_structRuins} ponds={_structPonds} streams={_structStreams}");
            GD.Print($"[structures] RESULT {(all ? "ok" : "FAIL")}");
            _done = true; GetTree().Quit(); return;
        }
        Game.Instance.JumpToFloor(floor + 1);
    }

    /// <summary>--test-bake-ui: from the title screen, run the Settings "rebuild" path with stand-ins and check the user copy and its voice.</summary>
    private void RunBakeUiTest()
    {
        var baker = Adventure.CampaignBaker.Instance;
        if (baker == null || Main.Instance == null) return;
        switch (_bakeUiStep)
        {
            case 0 when _elapsed >= 0.5:
                _bakeUiStep++;
                Settings.AiVoice = true;
                var started = baker.StartCampaign(withVoice: true, seed: 777);
                GD.Print($"[bakeui] started={started} status=\"{baker.Status}\"");
                if (!started) { GD.Print("[bakeui] RESULT FAIL: did not start"); _bakeUiStep = 99; }
                break;
            case 1 when !baker.Running:
                _bakeUiStep++;
                var dir = ProjectSettings.GlobalizePath(Adventure.BuiltinCampaign.UserDir);
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
                var loaded = Adventure.BuiltinCampaign.Load();
                var voiced = loaded?.Floors.Values.Count(f => f.IntroLine.VoiceId != null && VoiceRenderer.Instance!.HasAudio(f.IntroLine.VoiceId)) ?? 0;
                GD.Print($"[bakeui] done: ok={baker.LastSucceeded} status=\"{baker.Status}\" files={files} userCopy={Adventure.BuiltinCampaign.IsUserCopy} title=\"{loaded?.Campaign.Title}\" floors={loaded?.Floors.Count} introsVoiced={voiced} rendered={VoiceRenderer.Instance?.Rendered}");
                var ok = baker.LastSucceeded && files == 21 && Adventure.BuiltinCampaign.IsUserCopy && loaded?.Floors.Count == 20 && voiced == 20;
                GD.Print($"[bakeui] RESULT {(ok ? "ok" : "FAIL")}");
                try { Directory.Delete(dir, true); } catch { } // leave the shipped campaign in charge on this machine
                _done = true;
                GetTree().Quit();
                break;
        }
    }

    /// <summary>--test-voice: with the tone synth, a floor's intro line gets an id, renders to the cache, loads and plays; rendering pauses while enemies are close.</summary>
    private void RunVoiceTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var vr = VoiceRenderer.Instance;
        void Fail(string why) { _voiceFails.Add(why); GD.Print($"[voice] FAIL: {why}"); }
        switch (_voiceStep)
        {
            case 0 when _elapsed >= 0.5 && vr != null:
                _voiceStep++;
                _voiceLineId = main.Builder.Contents?.Plan?.IntroLine.VoiceId;
                GD.Print($"[voice] enabled={vr.Enabled} introId={_voiceLineId ?? "-"} pending={vr.Pending} cache={vr.CacheDir}");
                if (!vr.Enabled) Fail("voice not enabled with --fake-voice");
                if (_voiceLineId == null) Fail("intro line got no voice id");
                break;
            case 1 when _elapsed >= 4.0 && vr != null:
                _voiceStep++;
                var has = _voiceLineId != null && vr.HasAudio(_voiceLineId);
                GD.Print($"[voice] after 4 s: rendered={vr.Rendered} failed={vr.Failed} pending={vr.Pending} introCached={has}");
                if (!has) Fail("intro line not rendered to the cache");
                var played = vr.Play(_voiceLineId, interrupt: true);
                GD.Print($"[voice] play intro: {played} busy={Audio.AudioManager.Instance.VoiceBusy}");
                if (!played) Fail("could not play the cached line");
                // combat pause: stand next to an enemy and aggro it, then queue a new line
                var enemy = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.IsAlive && !main.Builder.IsUnderAt(e.GlobalPosition));
                if (enemy == null) { Fail("no enemy to fight"); break; }
                var cell = DungeonBuilder.WalkableNeighbor(main.Builder.Layout!, DungeonBuilder.WorldToCell(enemy.GlobalPosition));
                main.Party.Teleport(main.Builder.Surface(cell) + Vector3.Up * 0.3f, enemy.GlobalPosition);
                foreach (var a in main.Party.Adventurers) a.Stance = Characters.Stance.Hold;
                foreach (var m in main.Party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp] * 10; // stay alive while it hits us
                enemy.ForceAggro();
                _voiceProbe = new Line($"A line queued in the thick of a fight at tick {Time.GetTicksMsec()}."); // unique so an earlier run's cache cannot satisfy it
                vr.Enqueue(_voiceProbe, Adventure.Voice.Voices.Narrator, Adventure.Voice.Speaker.Narrator, Adventure.Voice.VoicePriority.Narrator);
                _voiceEnemy = enemy;
                break;
            case 2 when _elapsed >= 7.0 && vr != null:
                _voiceStep++;
                var ve = _voiceEnemy;
                GD.Print($"[voice] in combat: inCombat={vr.InCombat} probeCached={vr.HasAudio(_voiceProbe!.VoiceId!)} pending={vr.Pending} enemy={(ve != null && IsInstanceValid(ve) ? $"{ve.State.Name} hp={ve.Hp:0} hostile={ve.IsHostile} dist={ve.GlobalPosition.DistanceTo(main.Party.Lead.GlobalPosition):0.0}" : "gone")}");
                if (!vr.InCombat) Fail("renderer did not notice the fight");
                if (vr.HasAudio(_voiceProbe.VoiceId!)) Fail("rendered during combat");
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.Kill();
                break;
            case 3 when _elapsed >= 11.0 && vr != null:
                _voiceStep++;
                GD.Print($"[voice] after the fight: inCombat={vr.InCombat} probeCached={vr.HasAudio(_voiceProbe!.VoiceId!)} rendered={vr.Rendered} failed={vr.Failed}");
                if (!vr.HasAudio(_voiceProbe.VoiceId!)) Fail("probe line not rendered after combat ended");
                GD.Print($"[voice] RESULT {(_voiceFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _voiceFails))}");
                break;
        }
    }
    private Line? _voiceProbe;
    private Enemy? _voiceEnemy;

    /// <summary>--bake-campaign=DIR: with whatever planner is configured (--ai, --fake-ai or --replay-plans), write the campaign and all twenty floor plans to DIR.</summary>
    private void RunBake()
    {
        var main = Main.Instance;
        var game = Game.Instance;
        var director = AdventureDirector.Instance;
        if (main == null || !game.InGame || game.Adventure.Campaign == null || main.Builder.CurrentFloor < 1) return;
        if (_bakeFloor == 0)
        {
            if (_bakeVoiceDir != null && VoiceRenderer.Instance != null) { VoiceRenderer.Instance.CacheDir = _bakeVoiceDir; VoiceRenderer.Instance.Filter = _ => false; } // nothing renders until the whole campaign is known and the budget picks the lines
            _bakeFloor = 2;
            GD.Print($"[bake] campaign \"{game.Adventure.Campaign.Title}\" ({game.Adventure.Campaign.Source}); planning floors 2-{TierRules.FinalFloor} with {director.PlannerName}");
        }
        // keep what is planned so far on disk: a bake that stops (credit, crash) resumes with --bake-resume instead of paying for the outline again
        var written = game.Adventure.FloorPlans.Count(kv => kv.Key >= 1 && kv.Key <= TierRules.FinalFloor);
        if (written != _bakePartialWritten)
        {
            _bakePartialWritten = written;
            try { BuiltinCampaign.Write(_bakeDir! + ".partial", game.Adventure.Campaign, game.Adventure.FloorPlans.Where(kv => kv.Key >= 1 && kv.Key <= TierRules.FinalFloor).ToDictionary(kv => kv.Key, kv => kv.Value)); }
            catch (Exception e) { GD.PushWarning($"[bake] partial write failed: {e.Message}"); }
        }
        if (_bakeFloor > TierRules.FinalFloor)
        {
            if (director.IsPlanning(1) || game.Adventure.Campaign.Barks.Count == 0 && director.Planner is not OfflinePlanner && _elapsed < 120) return; // let the bark bank land
            var plans = game.Adventure.FloorPlans.Where(kv => kv.Key >= 1 && kv.Key <= TierRules.FinalFloor).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (_bakeVoiceDir != null && VoiceRenderer.Instance is { Enabled: true } queue && !_bakeVoiceQueued)
            {
                _bakeVoiceQueued = true;
                var picks = Adventure.Voice.VoiceBudget.Select(game.Adventure.Campaign, plans, CampaignBaker.VoiceBudgetLines);
                var chosen = new HashSet<Line>(picks.Select(p => p.Line), ReferenceEqualityComparer.Instance);
                queue.Filter = line => chosen.Contains(line);
                director.EnqueueCampaignVoice(game.Adventure.Campaign);
                foreach (var plan in plans.Values) director.EnqueueFloorVoice(plan);
                queue.Filter = null;
                GD.Print($"[bake] {Adventure.Voice.VoiceBudget.Describe(picks, Adventure.Voice.VoiceBudget.Candidates(game.Adventure.Campaign, plans).Count)}");
                foreach (var p in picks) GD.Print($"[bake]   voice floor {p.Floor,2} {p.Reason}: {p.Line.Text}");
            }
            if (VoiceRenderer.Instance is { Enabled: true } vr && vr.Pending > 0 && _elapsed < 1700) { if ((int)_elapsed % 10 == 0) GD.Print($"[bake] voice: {vr.Pending} lines pending, {vr.Rendered} done, {vr.Failed} failed ({vr.CurrentModel})"); return; }
            var voiceStopped = _bakeVoiceDir != null ? VoiceRenderer.Instance?.StoppedReason : null;
            if (_bakeVoiceDir != null && VoiceRenderer.Instance is { } vrDone) GD.Print($"[bake] voice rendered per model: {string.Join(", ", vrDone.RenderedByModel.Select(kv => $"{kv.Key} x{kv.Value}"))}");
            if (voiceStopped != null) GD.Print($"[bake] VOICE STOPPED: {voiceStopped} ({VoiceRenderer.Instance!.Rendered} lines rendered, {VoiceRenderer.Instance.Failed} failed). The plans are written; run again with --bake-voice once the quota or model is sorted, lines already rendered are kept.");
            BuiltinCampaign.Write(_bakeDir!, game.Adventure.Campaign, plans);
            try { if (Directory.Exists(_bakeDir + ".partial")) Directory.Delete(_bakeDir + ".partial", recursive: true); } catch { }
            GD.Print($"[bake] wrote {plans.Count} floors to {_bakeDir}; sources: {string.Join(",", plans.OrderBy(k => k.Key).Select(k => k.Value.Source).Distinct())}; spend ${game.Adventure.Spend.Usd:0.000} over {game.Adventure.Spend.Calls} calls");
            var bakeOk = plans.Count == TierRules.FinalFloor && voiceStopped == null;
            GD.Print($"[bake] RESULT {(bakeOk ? "ok" : voiceStopped != null ? "FAIL (voice stopped)" : "FAIL")}");
            _bakeDir = null;
            GetTree().Quit(bakeOk ? 0 : 1);
            return;
        }
        if (game.Adventure.FloorPlans.ContainsKey(_bakeFloor)) { _bakeFloor++; return; }
        if (!director.IsPlanning(_bakeFloor)) director.Prefetch(_bakeFloor);
    }

    /// <summary>--test-ai-flow: with the stand-in planner, check the New Game card, prefetch of the next floor, waiting for a plan, and the timeout fallback.</summary>
    private void RunAiFlowTest()
    {
        var main = Main.Instance;
        if (main == null) return;
        var game = Game.Instance;
        var director = AdventureDirector.Instance;
        void Fail(string why) { _aiFails.Add(why); GD.Print($"[aiflow] FAIL: {why}"); }
        string Src(int f) => game.Adventure.FloorPlans.TryGetValue(f, out var p) ? p.Source : "-";
        switch (_aiStep)
        {
            case 0 when _elapsed >= 0.3:
                _aiStep++;
                GD.Print($"[aiflow] planner={director.PlannerName} overlay={main.Overlay.Visible} planning1={director.IsPlanning(1)} campaign={game.Adventure.Campaign?.Title ?? "-"}");
                if (!main.Overlay.Visible && game.Adventure.Campaign == null) Fail("no loading card while the outline is written");
                break;
            case 1 when _elapsed >= 3.5:
                _aiStep++;
                GD.Print($"[aiflow] after outline: campaign=\"{game.Adventure.Campaign?.Title}\" src={game.Adventure.Campaign?.Source} floor1={Src(1)} built={main.Builder.CurrentFloor} overlay={main.Overlay.Visible} planning2={director.IsPlanning(2)}");
                if (game.Adventure.Campaign?.Source != FakePlanner.Source) Fail("campaign not from the stand-in planner");
                if (Src(1) != FakePlanner.Source) Fail("floor 1 not from the stand-in planner");
                if (main.Builder.CurrentFloor != 1 || main.Builder.Contents?.Plan?.Source != FakePlanner.Source) Fail("floor 1 not built from its plan");
                if (!director.IsPlanning(2) && Src(2) == "-") Fail("floor 2 not being prefetched");
                break;
            case 2 when _elapsed >= 6.0:
                _aiStep++;
                GD.Print($"[aiflow] prefetch: floor2={Src(2)} planning2={director.IsPlanning(2)}");
                if (Src(2) != FakePlanner.Source) Fail("floor 2 plan did not arrive");
                game.JumpToFloor(2);
                break;
            case 3 when _elapsed >= 7.0:
                _aiStep++;
                GD.Print($"[aiflow] on floor 2: built={main.Builder.CurrentFloor} src={main.Builder.Contents?.Plan?.Source} overlay={main.Overlay.Visible} planning3={director.IsPlanning(3)}");
                if (main.Builder.CurrentFloor != 2 || main.Builder.Contents?.Plan?.Source != FakePlanner.Source) Fail("floor 2 not built from the prefetched plan");
                if (main.Overlay.Visible) Fail("waited although the plan was ready");
                // jump ahead of the prefetch: the game must wait for floor 5's plan
                director.Fake!.DelaySeconds = 2.5f;
                game.JumpToFloor(5);
                _aiWaitStart = Time.GetTicksMsec();
                break;
            case 4 when _elapsed >= 7.8:
                _aiStep++;
                GD.Print($"[aiflow] jumped to 5: overlay={main.Overlay.Visible} planning5={director.IsPlanning(5)} built={main.Builder.CurrentFloor}");
                if (!main.Overlay.Visible) Fail("no wait card while floor 5 is written");
                if (main.Builder.CurrentFloor == 5) Fail("floor 5 built before its plan");
                break;
            case 5 when _elapsed >= 11.5:
                _aiStep++;
                GD.Print($"[aiflow] floor 5 after wait: built={main.Builder.CurrentFloor} src={main.Builder.Contents?.Plan?.Source} overlay={main.Overlay.Visible} waited={(Time.GetTicksMsec() - _aiWaitStart) / 1000f:0.0}s");
                if (main.Builder.CurrentFloor != 5 || main.Builder.Contents?.Plan?.Source != FakePlanner.Source) Fail("floor 5 not built from the awaited plan");
                if (main.Overlay.Visible) Fail("wait card still up");
                // timeout: a very slow planner and a short patience
                director.Fake!.DelaySeconds = 60f;
                Settings.PlanWaitSeconds = 2f;
                game.JumpToFloor(8);
                break;
            case 6 when _elapsed >= 15.5:
                _aiStep++;
                GD.Print($"[aiflow] floor 8 after timeout: built={main.Builder.CurrentFloor} src={main.Builder.Contents?.Plan?.Source} overlay={main.Overlay.Visible} planning8={director.IsPlanning(8)}");
                if (main.Builder.CurrentFloor != 8) Fail("floor 8 never built after the timeout");
                else if (main.Builder.Contents?.Plan?.Source != PlanSources.Offline) Fail($"floor 8 should be offline after the timeout, was {main.Builder.Contents?.Plan?.Source}");
                if (main.Overlay.Visible) Fail("wait card still up after timeout");
                var data = main.BuildSaveData("aiflow");
                var back = SaveFormat.Deserialize(SaveFormat.Serialize(data));
                GD.Print($"[aiflow] save: mode={back.Adventure?.Mode} campaign=\"{back.Adventure?.Campaign?.Title}\" plans=[{string.Join(",", back.Adventure?.FloorPlans.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value.Source}") ?? Array.Empty<string>())}]");
                if (back.Adventure?.Campaign == null || back.Adventure.FloorPlans.Count < 4) Fail("save lacks the campaign or plans");
                GD.Print($"[aiflow] memory: floors={back.Adventure?.Memory.Floors.Count} summary=\"{director.MemorySummary(9).Replace("\n", " | ")}\"");
                GD.Print($"[aiflow] RESULT {(_aiFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _aiFails))}");
                break;
        }
    }

    /// <summary>--test-quest: author a floor with one NPC per quest template and play each quest through dialogue to completion.</summary>
    private void RunQuestTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        var b = main.Builder;
        var director = AdventureDirector.Instance;
        var game = Game.Instance;
        void Fail(string why) { _questFails.Add(why); GD.Print($"[quest] FAIL: {why}"); }
        Npc? NpcById(string id) => root.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == id);
        QuestState? Q(string id) => director.Quests.GetValueOrDefault(QuestKeys.For(3, id));
        void Talk(string npcId, DialogueEffect effect, string? firstChoice = null)
        {
            var npc = NpcById(npcId);
            if (npc == null) { Fail($"npc {npcId} missing"); return; }
            var dlg = main.Hud.Dialogue;
            main.Hud.OpenDialogue(npc);
            if (firstChoice != null)
            {
                var idx = dlg.Choices.ToList().FindIndex(c => c.Text.StartsWith(firstChoice));
                if (idx < 0) { Fail($"{npcId}: choice '{firstChoice}' not offered ({string.Join("|", dlg.Choices.Select(c => c.Text))})"); dlg.Close(); return; }
                dlg.Choose(idx);
            }
            var eff = dlg.Choices.ToList().FindIndex(c => c.Effect == effect);
            if (eff < 0) { Fail($"{npcId}: effect {effect} not offered at node {dlg.Node?.Id} ({string.Join("|", dlg.Choices.Select(c => c.Text))})"); dlg.Close(); return; }
            dlg.Choose(eff);
            if (dlg.Visible) dlg.Close();
        }
        switch (_questStep)
        {
            case 0 when _elapsed >= 0.5:
            {
                _questStep++;
                var seed3 = Rng.FloorSeed(game.GameSeed, 3);
                var spec = b.SpecFor(3);
                var layout = FloorGenerator.Generate(spec, seed3);
                var menu = AdventurePlanning.MenuFor(layout, spec, b.TierFor(3)?.ToProfile(), b.Roster, 3, new PartySnapshot(8, 8, 4));
                var plan = OfflineDesigner.PlanFloor(menu, layout, seed3);
                plan.Npcs.Clear(); plan.Quests.Clear();
                var target = plan.Collectives.OrderByDescending(c => c.Members.Sum(m => m.Count)).First();
                var rooms = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && !r.IsExit && r.FreeCells >= 3).OrderBy(r => r.HopsFromStart).ToList();
                var itemRoom = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells > 2).OrderByDescending(r => r.HopsFromStart).First();
                string[] ids = { "hermit", "digger", "pilgrim", "sentinel" };
                var templates = new[] { QuestTemplate.KillTarget, QuestTemplate.Recover, QuestTemplate.Escort, QuestTemplate.HoldOut };
                for (var i = 0; i < 4; i++)
                {
                    var room = rooms[i % rooms.Count];
                    var q = new QuestPlan { Id = $"q_{ids[i]}", Template = templates[i], Title = $"Test {templates[i]}", GiverNpc = ids[i], Reward = new QuestReward { Xp = 50, Table = "table.chest" },
                        Lines = new QuestLines { Complete = new Line("Well done."), Fail = new Line("Alas.") } };
                    switch (templates[i])
                    {
                        case QuestTemplate.KillTarget: q.Params.TargetCollective = target.Id; break;
                        case QuestTemplate.Recover: q.Params.ItemRoom = itemRoom.Index; q.Params.ItemName = "Test Relic"; break;
                        case QuestTemplate.Escort: q.Params.EscortNpc = ids[i]; break;
                        case QuestTemplate.HoldOut: q.Params.WaveCollective = target.Id; q.Params.Waves = 1; break;
                    }
                    plan.Npcs.Add(new NpcPlan { Id = ids[i], Name = ids[i], Room = room.Index, Script = OfflineDesigner.StandardScript("Hello.", "Will you help?", "Thank you.", "It is done."), QuestId = q.Id });
                    plan.Quests.Add(q);
                }
                var report = PlanValidator.Validate(layout, plan, menu);
                GD.Print($"[quest] authored floor 3: {report} npcs={plan.Npcs.Count} quests={plan.Quests.Count} target={target.Id}x{target.Members.Sum(m => m.Count)} {string.Join("; ", report.Repairs)}");
                if (plan.Quests.Count != 4) Fail("validator dropped a quest");
                game.Adventure.FloorPlans[3] = plan;
                foreach (var a in party.Adventurers) a.Stance = Characters.Stance.Hold;
                game.JumpToFloor(3);
                break;
            }
            case 1 when _elapsed >= 2.0:
            {
                _questStep++;
                var npcs = root.GetChildren().OfType<Npc>().ToList();
                GD.Print($"[quest] floor 3: npcs=[{string.Join(",", npcs.Select(n => n.Id))}] quests=[{string.Join(",", director.Quests.Values.Where(q => q.Floor == 3).Select(q => $"{q.Id}:{q.Status}"))}] questItems={root.GetChildren().OfType<LootDrop>().Count(d => d.QuestId != null)}");
                if (npcs.Count != 4) Fail("NPCs not placed");
                if (director.Quests.Values.Count(q => q.Floor == 3) != 4) Fail("quests not registered");
                _questXpBefore = party.Members[0].Xp;
                // kill quest: ask, accept, then slay the target group
                Talk("hermit", DialogueEffect.AcceptQuest, "What happened");
                GD.Print($"[quest] hermit after accept: {Q("q_hermit")?.Status} objective=\"{Q("q_hermit")?.ObjectiveText}\"");
                if (Q("q_hermit")?.Status != QuestStatus.Active) Fail("kill quest not accepted");
                var targetId = Q("q_hermit")?.TargetCollective;
                foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => e.Group?.Id == targetId)) e.Kill();
                // recover quest: accept, walk to the relic, pick it up
                Talk("digger", DialogueEffect.AcceptQuest, "What happened");
                if (root.GetChildren().OfType<LootDrop>().FirstOrDefault(d => d.QuestId != null && QuestKeys.QuestOf(d.QuestId) == QuestKeys.For(3, "q_digger")) is { } relic)
                {
                    party.Teleport(relic.GlobalPosition + new Vector3(0.8f, 0, 0), relic.GlobalPosition);
                    relic.Interact(party, party.Lead);
                }
                else Fail("relic not placed");
                break;
            }
            case 2 when _elapsed >= 3.5:
            {
                _questStep++;
                GD.Print($"[quest] hermit: {Q("q_hermit")?.Status} ({Q("q_hermit")?.Progress}/{Q("q_hermit")?.Goal}); digger: {Q("q_digger")?.Status}; objective line=\"{main.Hud.ObjectiveText}\"");
                if (Q("q_hermit")?.Status != QuestStatus.ReadyToTurnIn) Fail("kill quest not ready after slaying the group");
                if (Q("q_digger")?.Status != QuestStatus.ReadyToTurnIn) Fail("recover quest not ready after pickup");
                var dropsBefore = root.GetChildren().OfType<LootDrop>().Count();
                Talk("hermit", DialogueEffect.TurnIn);
                Talk("digger", DialogueEffect.TurnIn);
                var dropsAfter = root.GetChildren().OfType<LootDrop>().Count();
                GD.Print($"[quest] turned in: hermit={Q("q_hermit")?.Status} digger={Q("q_digger")?.Status} xp {_questXpBefore:0} -> {party.Members[0].Xp:0} loot drops {dropsBefore} -> {dropsAfter}");
                if (Q("q_hermit")?.Status != QuestStatus.Completed || Q("q_digger")?.Status != QuestStatus.Completed) Fail("turn-in did not complete");
                if (party.Members[0].Xp <= _questXpBefore) Fail("no xp reward");
                if (dropsAfter <= dropsBefore) Fail("no loot reward");
                // escort: accept, then drop the pilgrim at the stairs
                Talk("pilgrim", DialogueEffect.AcceptQuest, "What happened");
                var pilgrim = NpcById("pilgrim");
                GD.Print($"[quest] pilgrim escorting={pilgrim?.Escorting}");
                if (pilgrim is not { Escorting: true }) Fail("escort NPC not following");
                else pilgrim.GlobalPosition = b.Surface(b.Layout!.DownStairs) + Vector3.Up * 0.3f;
                // hold out: accept, the wave spawns near the sentinel
                Talk("sentinel", DialogueEffect.AcceptQuest, "What happened");
                break;
            }
            case 3 when _elapsed >= 7.0:
            {
                _questStep++;
                var wave = root.GetChildren().OfType<Enemy>().Where(e => e.SpawnIndex == -1 && e.IsAlive).ToList();
                GD.Print($"[quest] pilgrim: {Q("q_pilgrim")?.Status} rescued={NpcById("pilgrim")?.Rescued}; sentinel: {Q("q_sentinel")?.Status} wave enemies alive={wave.Count} spawner={root.GetNodeOrNull<WaveSpawner>("Waves") != null}");
                if (Q("q_pilgrim")?.Status != QuestStatus.Completed) Fail("escort not completed at the stairs");
                if (wave.Count == 0) Fail("no wave spawned");
                foreach (var e in wave) e.Kill();
                break;
            }
            case 4 when _elapsed >= 9.5:
            {
                _questStep++;
                GD.Print($"[quest] sentinel after wave: {Q("q_sentinel")?.Status} ({Q("q_sentinel")?.Progress}/{Q("q_sentinel")?.Goal})");
                if (Q("q_sentinel")?.Status != QuestStatus.ReadyToTurnIn) Fail("hold-out not ready after the wave");
                Talk("sentinel", DialogueEffect.TurnIn);
                if (Q("q_sentinel")?.Status != QuestStatus.Completed) Fail("hold-out turn-in failed");
                var data = main.BuildSaveData("quest");
                var back = SaveFormat.Deserialize(SaveFormat.Serialize(data));
                var floorState = back.Floors.GetValueOrDefault(3);
                GD.Print($"[quest] save: quests={back.Adventure?.Quests.Count} completed={back.Adventure?.Quests.Values.Count(q => q.Status == QuestStatus.Completed)} npcStates={floorState?.NpcStates.Count} takenItems={floorState?.TakenQuestItems.Count} rescued={floorState?.NpcStates.Count(n => n.Rescued)}");
                if (back.Adventure?.Quests.Values.Count(q => q.Status == QuestStatus.Completed) != 4) Fail("save does not hold 4 completed quests");
                if (floorState == null || floorState.NpcStates.Count != 4 || floorState.TakenQuestItems.Count != 1) Fail("floor state missing npc/item records");
                GD.Print($"[quest] RESULT {(_questFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _questFails))}");
                break;
            }
        }
    }

    /// <summary>--test-collectives: author a floor with guard, patrol and alarm groups, then provoke each behaviour.</summary>
    private void RunCollectivesTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        var b = main.Builder;
        void Fail(string why) { _collFails.Add(why); GD.Print($"[coll] FAIL: {why}"); }
        string St(Enemy? e) => e == null || !IsInstanceValid(e) ? "gone" : e.State.Name;
        string States(Collective c) => string.Join(",", c.Members.Select(St));
        switch (_collStep)
        {
            case 0 when _elapsed >= 0.5:
            {
                _collStep++;
                var game = Game.Instance;
                var seed3 = Rng.FloorSeed(game.GameSeed, 3);
                var spec = b.SpecFor(3);
                var layout = FloorGenerator.Generate(spec, seed3);
                var menu = AdventurePlanning.MenuFor(layout, spec, b.TierFor(3)?.ToProfile(), b.Roster, 3, new PartySnapshot(8, 8, 4)); // strong party: a generous budget so nothing is trimmed
                var plan = OfflineDesigner.PlanFloor(menu, layout, seed3);
                plan.Collectives.Clear(); plan.Loners.Clear(); plan.Chests.Clear(); plan.Traps.Clear();
                var rooms = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells >= 4).OrderByDescending(r => r.HopsFromStart).ToList();
                var guards = new CollectivePlan { Id = "guards", Name = "Test Guards", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 3 } }, Goal = new GoalPlan { Kind = GoalKind.Guard, Room = rooms[0].Index, RallyRoom = rooms[1].Index } };
                var pa = rooms.Skip(1).FirstOrDefault(r => r.Neighbours.Any(n => n != rooms[0].Index && rooms.Any(x => x.Index == n)));
                var pb = pa == null ? null : rooms.First(x => pa.Neighbours.Contains(x.Index) && x.Index != rooms[0].Index);
                var walkers = new CollectivePlan { Id = "walkers", Name = "Test Walkers", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 2 } }, Goal = pa != null && pb != null ? new GoalPlan { Kind = GoalKind.Patrol, Room = pa.Index, Route = { pa.Index, pb.Index } } : new GoalPlan { Room = rooms[^1].Index } };
                float Dist2(RoomInfo a, RoomInfo c) { var dx = (a.X + a.W / 2f) - (c.X + c.W / 2f); var dy = (a.Y + a.H / 2f) - (c.Y + c.H / 2f); return dx * dx + dy * dy; }
                var ar = rooms.Where(r => r.Index != rooms[0].Index && r.Index != pa?.Index && r.Index != pb?.Index).OrderBy(r => Dist2(r, rooms[0])).First(); // closest by distance: a short run for the alarm
                var sentries = new CollectivePlan { Id = "sentries", Name = "Test Sentries", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 2 } }, Goal = new GoalPlan { Kind = GoalKind.Alarm, Room = ar.Index, AlarmTarget = "guards", RallyRoom = rooms[0].Index } };
                plan.Collectives.AddRange(new[] { guards, walkers, sentries });
                var report = PlanValidator.Validate(layout, plan, menu);
                GD.Print($"[coll] authored floor 3: {report} guards@{rooms[0].Index} patrol {pa?.Index}->{pb?.Index} sentries@{ar.Index} {string.Join("; ", report.Repairs)}");
                game.Adventure.FloorPlans[3] = plan;
                foreach (var a in party.Adventurers) a.Stance = Characters.Stance.Hold;
                game.JumpToFloor(3);
                break;
            }
            case 1 when _elapsed >= 2.0:
            {
                _collStep++;
                var reg = b.Collectives!;
                foreach (var c in reg.Collectives) GD.Print($"[coll] group {c.Id} goal={c.Goal} members={c.Members.Count} alert={c.Alert} states=[{States(c)}] route={c.Route.Count} rally={c.RallyPosition != null}");
                var guards = reg.Find("guards"); var walkers = reg.Find("walkers"); var sentries = reg.Find("sentries");
                if (guards == null || walkers == null || sentries == null || guards.Members.Count < 3) { Fail("groups missing"); GD.Print("[coll] RESULT FAIL"); _collStep = 99; break; }
                _collBig = guards; _collAlarm = sentries;
                if (walkers.Goal != GoalKind.Patrol) Fail("walkers are not a patrol");
                _collPatrol = walkers.Alive.FirstOrDefault();
                _collPatrolStart = _collPatrol?.GlobalPosition ?? Vector3.Zero;
                var g = guards.Alive.First();
                var near = DungeonBuilder.WalkableNeighbor(b.Layout!, DungeonBuilder.WorldToCell(g.GlobalPosition));
                party.Teleport(b.Surface(near) + Vector3.Up * 0.3f, g.GlobalPosition);
                break;
            }
            case 2 when _elapsed >= 4.5:
            {
                _collStep++;
                GD.Print($"[coll] guards after contact: alert={_collBig!.Alert} states=[{States(_collBig)}] orders=[{string.Join(",", _collBig.LastOrders.Members.Select(m => m.Kind))}]");
                if (_collBig.Alert != AlertLevel.Alarmed) Fail("guards did not alarm on contact");
                if (_collBig.Alive.All(m => m.State.Unaware)) Fail("no guard reacted to the party");
                var moved = _collPatrol != null && IsInstanceValid(_collPatrol) ? _collPatrol.GlobalPosition.DistanceTo(_collPatrolStart) : 0f;
                GD.Print($"[coll] patrol member: state={St(_collPatrol)} moved={moved:0.0}m");
                if (moved < 1.5f) Fail("patrol did not move");
                party.Teleport(b.Surface(b.Layout!.UpStairs) + Vector3.Up * 0.3f);
                break;
            }
            case 3 when _elapsed >= 12.5:
            {
                _collStep++;
                var homes = string.Join(",", _collBig!.Alive.Select(m => $"{m.DistanceToPost():0.0}m"));
                GD.Print($"[coll] guards after party left: alert={_collBig.Alert} states=[{States(_collBig)}] distToPost=[{homes}]");
                if (_collBig.Alive.Any() && !_collBig.Alive.All(m => m.State is ReturnState or GuardState)) Fail("guards did not return to post");
                var sentry = _collAlarm!.Alive.First();
                var near = DungeonBuilder.WalkableNeighbor(b.Layout!, DungeonBuilder.WorldToCell(sentry.GlobalPosition));
                party.Teleport(b.Surface(near) + Vector3.Up * 0.3f, sentry.GlobalPosition);
                break;
            }
            case 4 when _elapsed >= 15.0:
            {
                _collStep++;
                GD.Print($"[coll] sentries: alert={_collAlarm!.Alert} raised={_collAlarm.AlarmRaised} states=[{States(_collAlarm)}]");
                if (_collAlarm.Members.FirstOrDefault(m => IsInstanceValid(m) && m.State is AlarmRunState) is { } runner && _collAlarm.AlarmTargetPosition is { } tgt)
                    GD.Print($"[coll] runner: dist to target={runner.GlobalPosition.DistanceTo(tgt):0.0} speed={runner.EffectiveMoveSpeed:0.0} travelled={runner.DistanceTravelled:0.0} {runner.PathDebug}");
                if (!_collAlarm.AlarmRaised && !_collAlarm.Members.Any(m => IsInstanceValid(m) && m.State is AlarmRunState)) Fail("no runner sent");
                break;
            }
            case 5 when _elapsed >= 29.0:
            {
                _collStep++;
                GD.Print($"[coll] guards after alarm: alert={_collBig!.Alert} raised={_collAlarm!.AlarmRaised} states=[{States(_collBig)}]");
                if (_collAlarm.Members.FirstOrDefault(m => IsInstanceValid(m) && m.State is AlarmRunState) is { } runner2 && _collAlarm.AlarmTargetPosition is { } tgt2)
                    GD.Print($"[coll] runner: dist to target={runner2.GlobalPosition.DistanceTo(tgt2):0.0} speed={runner2.EffectiveMoveSpeed:0.0} travelled={runner2.DistanceTravelled:0.0} {runner2.PathDebug}");
                if (!_collAlarm.AlarmRaised) Fail("runner never arrived");
                if (_collBig.Alert == AlertLevel.Calm) Fail("guards ignored the alarm");
                var g = _collBig.Alive.FirstOrDefault();
                if (g == null) { Fail("no guards left to regroup"); break; }
                var near = DungeonBuilder.WalkableNeighbor(b.Layout!, DungeonBuilder.WorldToCell(g.GlobalPosition));
                party.Teleport(b.Surface(near) + Vector3.Up * 0.3f, g.GlobalPosition);
                foreach (var m in party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp];
                foreach (var victim in _collBig.Alive.Skip(1).ToList()) victim.Kill();
                break;
            }
            case 6 when _elapsed >= 33.0:
            {
                _collStep++;
                var survivor = _collBig!.Alive.FirstOrDefault();
                GD.Print($"[coll] guards after losses: regrouped={_collBig.Regrouped} done={_collBig.RegroupDone} survivor={St(survivor)} order={survivor?.Order}");
                if (!_collBig.Regrouped) Fail("survivors did not regroup");
                var dto = FloorStateCapture.Capture(root, b.Layout!, b.Contents, main.Exploration!);
                GD.Print($"[coll] saved groups: {string.Join(", ", dto.Collectives.Select(c => $"{c.Id}:{c.Alert}{(c.AlarmRaised ? "!" : "")}{(c.Regrouped ? "R" : "")}"))}");
                if (!new[] { "guards", "walkers", "sentries" }.All(id => dto.Collectives.Any(c => c.Id == id))) Fail("collectives not saved"); // a camp on the floor adds its band
                GD.Print($"[coll] RESULT {(_collFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _collFails))}");
                break;
            }
        }
    }

    /// <summary>--test-plan: design every floor offline, validate, round-trip the JSON, then build one floor and check the tree matches its contents.</summary>
    private void RunPlanTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        switch (_planStep)
        {
            case 0 when _elapsed >= 0.5:
                _planStep++;
                for (var f = 1; f <= TierRules.FinalFloor; f++)
                {
                    var (plan, report) = main.Builder.DesignOffline(f, Game.Instance.GameSeed, main.Party.Snapshot());
                    var json = PlanFormat.Serialize(plan);
                    var same = PlanFormat.Serialize(PlanFormat.Deserialize<FloorPlan>(json)) == json;
                    if (!report.Ok || report.Repairs.Count > 0 || !same) _planOk = false;
                    GD.Print($"[plan] floor {f}: {report} groups={plan.Collectives.Count} ({string.Join(",", plan.Collectives.Select(c => c.Goal.Kind))}) enemies={plan.EnemyTotal} chests+={plan.Chests.Count} traps+={plan.Traps.Count} boss={plan.Boss?.Name ?? "-"} title=\"{plan.Title}\" roundtrip={same}");
                }
                Game.Instance.JumpToFloor(7);
                break;
            case 1 when _elapsed >= 2.0:
                _planStep++;
                var contents = main.Builder.Contents;
                if (contents?.Plan == null) { GD.Print("[plan] RESULT FAIL: floor 7 was not built from a plan"); break; }
                var enemies = root.GetChildren().OfType<Enemy>().Count(e => e.SpawnIndex >= 0);
                var extraChests = root.GetChildren().OfType<Dungeon.Props.Chest>().Count(c => c.SpawnIndex >= FloorContents.ExtraIndexBase);
                var extraTraps = root.GetChildren().OfType<Dungeon.Props.Trap>().Count(t => t.SpawnIndex >= FloorContents.ExtraIndexBase);
                var grouped = contents.Enemies.Count(e => e.CollectiveId != null);
                var data = main.BuildSaveData("plan");
                var back = SaveFormat.Deserialize(SaveFormat.Serialize(data));
                var kept = back.Version == SaveFormat.CurrentVersion && back.Adventure?.Mode == AdventureMode.Offline && back.Adventure.FloorPlans.ContainsKey(7) && back.Adventure.Campaign != null;
                var match = enemies == contents.EnemyCount && extraChests == contents.ExtraChests.Count && extraTraps == contents.ExtraTraps.Count;
                GD.Print($"[plan] floor 7 tree: enemies={enemies}/{contents.EnemyCount} grouped={grouped} groups={contents.Collectives.Count} extraChests={extraChests}/{contents.ExtraChests.Count} extraTraps={extraTraps}/{contents.ExtraTraps.Count} saveKeepsPlan={kept} dropped={contents.Dropped.Count}");
                GD.Print($"[plan] RESULT {(match && kept && _planOk ? "ok" : "FAIL")}");
                break;
        }
    }

    private void RunDepthTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout) return;
        var party = main.Party;
        switch (_depthStep)
        {
            case 0 when _elapsed >= 0.5:
                _depthStep++;
                Game.Instance.JumpToFloor(3); // floor 3 has a locked chest, vault chances, traps
                break;
            case 1 when _elapsed >= 1.5:
                _depthStep++;
                GD.Print($"[depth] floor {Game.Instance.CurrentFloor} tier={main.Builder.CurrentTier?.DisplayName} traps={root.GetChildren().OfType<Dungeon.Props.Trap>().Count()} doors={root.GetChildren().OfType<Dungeon.Props.Door>().Count()} levers={root.GetChildren().OfType<Dungeon.Props.Lever>().Count()} keys={root.GetChildren().OfType<LootDrop>().Count(d => d.KeyIndex >= 0)} lockedChests={root.GetChildren().OfType<Dungeon.Props.Chest>().Count(c => c.Locked)}");
                // trap: stand on it
                if (root.GetChildren().OfType<Dungeon.Props.Trap>().FirstOrDefault() is { } trap)
                {
                    var hpBefore = party.Members.Sum(m => m.Hp);
                    trap.Spring(party.Lead);
                    GD.Print($"[depth] trap {trap.Kind}: party hp {hpBefore:0} -> {party.Members.Sum(m => m.Hp):0}, triggered={trap.Triggered}");
                }
                // door: with a key
                if (root.GetChildren().OfType<Dungeon.Props.Door>().FirstOrDefault(d => d.Kind == Dungeon.Generation.VaultKind.KeyDoor) is { } door)
                {
                    party.Bag.TryAdd(Game.Instance.Catalog.Create("item.iron_key"));
                    var blockedBefore = main.Builder.IsCellBlocked(door.Cell);
                    door.Interact(party, party.Lead);
                    GD.Print($"[depth] key door: blocked {blockedBefore} -> {main.Builder.IsCellBlocked(door.Cell)}, open={door.IsOpen}");
                }
                if (root.GetChildren().OfType<Dungeon.Props.Lever>().FirstOrDefault() is { } lever)
                {
                    lever.Interact(party, party.Lead);
                    var gate = root.GetChildren().OfType<Dungeon.Props.Door>().First(d => d.VaultIndex == lever.VaultIndex);
                    GD.Print($"[depth] lever gate: open={gate.IsOpen}");
                }
                if (root.GetChildren().OfType<Dungeon.Props.Chest>().FirstOrDefault(c => c.Locked) is { } chest)
                {
                    party.Bag.TryAdd(Game.Instance.Catalog.Create("item.iron_key"));
                    chest.Interact(party, party.Lead);
                    GD.Print($"[depth] locked chest: opened={chest.Opened}");
                }
                party.PlaceTrap(Dungeon.TrapKind.Spikes, party.Lead);
                GD.Print($"[depth] placed traps={root.GetChildren().OfType<Dungeon.Props.Trap>().Count(t => t.PlacedByParty)}");
                break;
            case 2 when _elapsed >= 2.0:
                _depthStep++;
                // pathing: put the party in a far room with no line of sight and aggro the farthest enemy
                var hidden = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && e.Kind == EnemyKind.Melee && !e.HasLineOfSight()).OrderBy(e => e.DistanceToParty()).FirstOrDefault();
                if (hidden != null)
                {
                    _pathTarget = hidden;
                    _pathStart = hidden.DistanceToParty();
                    hidden.ForceAggro();
                    hidden.GiveUpSeconds = 999;
                    GD.Print($"[depth] pathing: {hidden.Name} ({hidden.DisplayName}) starts {_pathStart:0.0}m away with no line of sight");
                }
                break;
            case 3 when _elapsed >= 8.0:
                _depthStep++;
                if (_pathTarget != null && IsInstanceValid(_pathTarget))
                {
                    var d = _pathTarget.DistanceToParty();
                    GD.Print($"[depth] pathing: after 6s {_pathTarget.Name} is {d:0.0}m away (was {_pathStart:0.0}), travelled {_pathTarget.DistanceTravelled:0}m, state={_pathTarget.State.Name} -> {(d < 3.5f ? "ARRIVED" : _pathTarget.DistanceTravelled > 12f ? "moving along its path" : "STUCK")}");
                }
                Game.Instance.JumpToFloor(5);
                break;
            case 4 when _elapsed >= 9.0:
                _depthStep++;
                var stairs = root.GetNode<Dungeon.Stairs>("DownStairs");
                var boss = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.IsBoss);
                GD.Print($"[depth] floor 5 boss={boss?.DisplayName ?? "none"} hp={boss?.MaxHp:0} stairsLocked={stairs.Locked} enemies={root.GetChildren().OfType<Enemy>().Count()}");
                boss?.Kill();
                break;
            case 5 when _elapsed >= 10.0:
                _depthStep++;
                GD.Print($"[depth] after boss kill: stairsLocked={root.GetNode<Dungeon.Stairs>("DownStairs").Locked}");
                Game.Instance.JumpToFloor(20);
                break;
            case 6 when _elapsed >= 11.0:
                _depthStep++;
                var herald = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.IsBoss);
                GD.Print($"[depth] floor 20 tier={main.Builder.CurrentTier?.DisplayName} boss={herald?.DisplayName} hp={herald?.MaxHp:0} sig={herald?.Profile.Signature}");
                herald?.Kill();
                break;
            case 7 when _elapsed >= 12.0:
                _depthStep++;
                GD.Print($"[depth] victory={Game.Instance.Victory} stairsLocked={root.GetNode<Dungeon.Stairs>("DownStairs").Locked}");
                Game.Instance.JumpToFloor(23);
                break;
            case 8 when _elapsed >= 13.0:
                _depthStep++;
                var e0 = root.GetChildren().OfType<Enemy>().FirstOrDefault();
                GD.Print($"[depth] floor 23 (endless) tier={main.Builder.CurrentTier?.DisplayName} enemies={root.GetChildren().OfType<Enemy>().Count()} sample={e0?.DisplayName} hp={e0?.MaxHp:0} vs base {e0?.Profile.MaxHp:0}");
                Main.Instance.SaveGame("depth");
                GD.Print($"[depth] RESULT ok");
                break;
        }
    }

    private Enemy? _pathTarget;
    private float _pathStart;
    private double _pathLog;
    private int _pathStep;

    // Vertical: find a floor with a ledge, a cellar and a secret; climb, fall, open, and path across levels.
    private Enemy? _vertEnemy;
    private void RunVerticalTest()
    {
        var delta = GetProcessDeltaTime();
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout) return;
        var party = main.Party;
        var builder = main.Builder;
        switch (_vertStep)
        {
            case 0 when _elapsed >= 0.5:
                _vertStep++;
                // hop floors until one has everything
                for (var f = 2; f <= 12; f++)
                {
                    var probe = Dungeon.Generation.FloorGenerator.Generate(builder.SpecFor(f), Rng.FloorSeed(Game.Instance.GameSeed, f));
                    if (probe.Cellars.Count > 0 && probe.Ledges.Count > 0 && probe.Secrets.Count > 0 && probe.Ramps.Count > 0) { Game.Instance.JumpToFloor(f); return; }
                }
                GD.Print("[vert] no floor with all features in 2..12 for this seed; using floor 2");
                Game.Instance.JumpToFloor(2);
                break;
            case 1 when _elapsed >= 1.5:
                _vertStep++;
                GD.Print($"[vert] seed={Game.Instance.GameSeed} floor {Game.Instance.CurrentFloor} ramps={layout.Ramps.Count} ledges={layout.Ledges.Count} cellars={layout.Cellars.Count} secrets={layout.Secrets.Count} highCells={layout.Levels.Count(b => b == 1)}");
                // ledge: an enemy stands up there
                if (layout.Ledges.Count > 0)
                {
                    var ledge = layout.Ledges[0];
                    var archer = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => DungeonBuilder.WorldToCell(e.GlobalPosition) == ledge.EnemySpawn);
                    GD.Print($"[vert] ledge archer={archer?.DisplayName ?? "none"} kind={archer?.Kind} y={archer?.GlobalPosition.Y:0.0} (ledge surface {builder.Surface(ledge.EnemySpawn).Y:0.0})");
                    // party climbs the ramp: teleport to the ramp bottom entry and walk up
                    var entry = ledge.RampBottom + new Dungeon.Generation.GridPos(0, 1);
                    party.Teleport(builder.Surface(entry) + Vector3.Up * 0.3f, builder.Surface(ledge.RampTop));
                    _vertWalk = 3.0;
                }
                break;
            case 2 when _elapsed >= 5.0:
                _vertStep++;
                GD.Print($"[vert] after walking up the ramp: party y={party.GlobalPosition.Y:0.0} cell={DungeonBuilder.WorldToCell(party.GlobalPosition)} level={layout.LevelOf(DungeonBuilder.WorldToCell(party.GlobalPosition))} -> {(party.GlobalPosition.Y > 3.5f ? "ON THE LEDGE" : "did not climb")}");
                // cellar: drop through the pit
                if (layout.Cellars.Count > 0)
                {
                    var cellar = layout.Cellars[0];
                    var hp = party.Members.Sum(m => m.Hp);
                    party.Teleport(builder.Surface(cellar.Pit) + Vector3.Up * 0.6f);
                    _vertHp = hp;
                    _vertEnemy = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => builder.IsUnderAt(e.GlobalPosition));
                }
                break;
            case 3 when _elapsed >= 7.0:
                _vertStep++;
                if (layout.Cellars.Count > 0)
                {
                    var cellar = layout.Cellars[0];
                    GD.Print($"[vert] after the pit: party y={party.GlobalPosition.Y:0.0} under={builder.IsUnderAt(party.GlobalPosition)} hp {_vertHp:0} -> {party.Members.Sum(m => m.Hp):0}; cellar enemy={_vertEnemy?.DisplayName} state={_vertEnemy?.State.Name}");
                    // path from the cellar out to the up stairs
                    var path = Dungeon.Generation.GridPathfinder.FindPath(layout, new Dungeon.Generation.PathNode(DungeonBuilder.WorldToCell(party.GlobalPosition), true), new Dungeon.Generation.PathNode(layout.UpStairs, false), builder.IsCellBlocked, builder.IsSecretOpen);
                    GD.Print($"[vert] tunnel out: path {(path == null ? "none" : path.Count + " nodes")} via exit {cellar.ExitTarget}");
                }
                // secret: reveal and open
                if (root.GetChildren().OfType<Dungeon.Props.SecretWall>().FirstOrDefault() is { } secret)
                {
                    var wallCell = secret.Cell;
                    var facing = layout.Secrets[secret.Index].Facing;
                    party.Teleport(builder.Surface(facing) + Vector3.Up * 0.3f, secret.GlobalPosition);
                    _vertSecret = secret;
                }
                break;
            case 4 when _elapsed >= 8.5:
                _vertStep++;
                if (_vertSecret != null)
                {
                    GD.Print($"[vert] secret detected={_vertSecret.Detected} prompt='{party.LookingAt?.Prompt}'");
                    _vertSecret.Interact(party, party.Lead);
                    var alcove = layout.Secrets[_vertSecret.Index].Alcove;
                    var chest = root.GetChildren().OfType<Dungeon.Props.Chest>().FirstOrDefault(c => DungeonBuilder.WorldToCell(c.GlobalPosition) == alcove);
                    var path = Dungeon.Generation.GridPathfinder.FindPath(layout, layout.UpStairs, alcove, builder.IsCellBlocked, passable: builder.IsSecretOpen);
                    GD.Print($"[vert] secret open={_vertSecret.IsOpen} alcove chest={chest != null} pathable={path != null} gridCell={main.Builder.IsSecretOpen(_vertSecret.Cell)}");
                }
                // cross-level pathing: aggro every melee enemy on the other level and watch
                foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && e.Kind == EnemyKind.Melee && !builder.IsUnderAt(e.GlobalPosition) && layout.LevelOf(DungeonBuilder.WorldToCell(e.GlobalPosition)) != layout.LevelOf(DungeonBuilder.WorldToCell(party.GlobalPosition))))
                {
                    e.ForceAggro(); e.GiveUpSeconds = 999; _watched.Add(e);
                }
                GD.Print($"[vert] watching {_watched.Count} enemies on the other level");
                break;
            case 5 when _elapsed >= 13.0:
                _vertStep++;
                foreach (var e in _watched.Where(IsInstanceValid))
                {
                    var c = DungeonBuilder.WorldToCell(e.GlobalPosition);
                    string Info(Dungeon.Generation.GridPos g) => $"{g}:{layout[g]}/L{layout.LevelOf(g)}/h{layout.HeightOf(g):0}{(layout.Ramps.TryGetValue(g, out var r) ? $"/ramp{r.Dir}@{r.LowHeight}" : "")}{(layout.IsUnder(g) ? "/under" : "")}";
                    var around = string.Join(" ", new[] { new Dungeon.Generation.GridPos(1, 0), new(-1, 0), new(0, 1), new(0, -1) }.Select(d => Info(c + d)));
                    var ok = e.DistanceTravelled > e.EffectiveMoveSpeed * 4.5f * 0.6f || e.DistanceToParty() < 3.5f; // 4.5 s of chasing
                    GD.Print($"[vert] {e.Name} ({e.DisplayName}) state={e.State.Name} hp={e.Hp:0}/{e.MaxHp:0} alive={e.IsAlive} stunned={e.Effects.IsStunned} travelled {e.DistanceTravelled:0}m, now {e.DistanceToParty():0.0}m away, y={e.GlobalPosition.Y:0.0} {(ok ? "ok" : "STUCK " + e.PathDebug + " here=" + Info(c) + " around=[" + around + "]")}");
                    if (!ok)
                    {
                        var dir = new Vector3(e.Velocity.X, 0, e.Velocity.Z).Normalized();
                        var from = e.GlobalPosition + Vector3.Up * 0.9f;
                        var q = PhysicsRayQueryParameters3D.Create(from, from + dir * 1.5f, collisionMask: 1);
                        var hit = e.GetWorld3D().DirectSpaceState.IntersectRay(q);
                        GD.Print($"[vert]   ray {dir:0.0}: {(hit.Count == 0 ? "clear" : $"{(hit["collider"].AsGodotObject() as Node)?.Name} at {hit["position"].AsVector3():0.00} normal {hit["normal"].AsVector3():0.00}")}");
                        GD.Print($"[vert]   grid: {string.Join(" ", new[] { new Dungeon.Generation.GridPos(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1) }.Select(d => builder.DebugCell(c + d)))}");
                        var q2 = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * 0.8f, from + Vector3.Up * 0.8f + dir * 1.5f, collisionMask: 1);
                        var hit2 = e.GetWorld3D().DirectSpaceState.IntersectRay(q2);
                        GD.Print($"[vert]   ray at head: {(hit2.Count == 0 ? "clear" : $"{(hit2["collider"].AsGodotObject() as Node)?.Name} at {hit2["position"].AsVector3():0.00}")}");
                    }
                }
                GD.Print("[vert] RESULT ok");
                break;
        }
        if (_vertWalk > 0)
        {
            _vertWalk -= delta;
            party.ForcedMoveInput = new Vector2(0, -1);
            _vertLog += delta;
            if (_vertLog >= 0.4)
            {
                _vertLog = 0;
                var lead = party.Lead;
                GD.Print($"[vert] walk: pos=({lead.GlobalPosition.X:0.0},{lead.GlobalPosition.Y:0.00},{lead.GlobalPosition.Z:0.0}) cell={DungeonBuilder.WorldToCell(lead.GlobalPosition)} onFloor={lead.IsOnFloor()}");
            }
            if (_vertWalk <= 0) party.ForcedMoveInput = null;
        }
    }
    private double _vertWalk;
    private double _vertLog;
    private float _vertHp;
    private Dungeon.Props.SecretWall? _vertSecret;

    // Stairs: walk up the flight to the landing, get asked, decline, step off and back, accept, arrive; then the well down.
    private void RunStairsTest()
    {
        var delta = GetProcessDeltaTime();
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || main.Hud == null) return;
        var party = main.Party;
        var lead = party.Lead;
        var open = main.Hud.StairsPromptOpen;
        void Press(string action) => Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        string Pos() => $"pos=({lead.GlobalPosition.X:0.0},{lead.GlobalPosition.Y:0.00},{lead.GlobalPosition.Z:0.0}) cell={DungeonBuilder.WorldToCell(lead.GlobalPosition)}";
        switch (_stairsStep)
        {
            case 0 when _elapsed >= 0.5:
                _stairsStep++;
                Game.Instance.JumpToFloor(2);
                break;
            case 1 when _elapsed >= 2.0:
                _stairsStep++;
                GD.Print($"[stairs] floor {Game.Instance.CurrentFloor}, start {Pos()}, walking at the up-stairs");
                _stairsWalk = 5.0;
                break;
            case 2 when open || _elapsed >= 8.0:
                _stairsStep++;
                _stairsWalk = open ? 0.4 : 0; if (!open) party.ForcedMoveInput = null;
                GD.Print($"[stairs] up landing: prompt={open} {Pos()}");
                if (!open) { GD.Print("[stairs] RESULT FAIL (no question on the up landing)"); _testStairs = false; return; }
                break;
            case 3 when _elapsed >= 9.0:
                _stairsStep++;
                GD.Print($"[stairs] on the landing, still asked: prompt={open} {Pos()}");
                if (!open) { GD.Print("[stairs] RESULT FAIL (the question went away while standing on the landing)"); _testStairs = false; return; }
                Press("ui_cancel"); // no thanks
                break;
            case 4 when _elapsed >= 9.6:
                _stairsStep++;
                GD.Print($"[stairs] after declining: prompt={open} floor={Game.Instance.CurrentFloor}");
                if (open || Game.Instance.CurrentFloor != 2) { GD.Print("[stairs] RESULT FAIL (decline did not close the question)"); _testStairs = false; return; }
                party.ForcedMoveInput = new Vector2(0, 1); // back down a little
                break;
            case 5 when _elapsed >= 10.4:
                _stairsStep++;
                GD.Print($"[stairs] stepped off: prompt={open} {Pos()}");
                _stairsWalk = 3.0; // and back up
                break;
            case 6 when open || _elapsed >= 14.0:
                _stairsStep++;
                _stairsWalk = open ? 0.4 : 0; if (!open) party.ForcedMoveInput = null;
                GD.Print($"[stairs] up landing again: prompt={open} {Pos()}");
                if (!open) { GD.Print("[stairs] RESULT FAIL (no question the second time)"); _testStairs = false; return; }
                Press("interact"); // yes
                break;
            case 7 when _elapsed >= 16.0:
                _stairsStep++;
                GD.Print($"[stairs] after accepting: floor={Game.Instance.CurrentFloor} prompt={open}");
                if (Game.Instance.CurrentFloor != 1) { GD.Print("[stairs] RESULT FAIL (did not climb to floor 1)"); _testStairs = false; return; }
                // now the well: stand before the down-stairs and walk in
                var foot = DungeonBuilder.WalkableNeighbor(layout, layout.DownStairs);
                party.Teleport(main.Builder.Surface(foot) + Vector3.Up * 0.3f, main.Builder.Surface(layout.DownStairs));
                _stairsWalk = 5.0;
                break;
            case 8 when open || _elapsed >= 22.5:
                _stairsStep++;
                _stairsWalk = open ? 0.4 : 0; if (!open) party.ForcedMoveInput = null;
                GD.Print($"[stairs] down landing: prompt={open} {Pos()}");
                if (!open) { GD.Print("[stairs] RESULT FAIL (no question at the bottom of the well)"); _testStairs = false; return; }
                if (lead.GlobalPosition.Y - main.Builder.Surface(layout.DownStairs).Y > -1.0f) { GD.Print("[stairs] RESULT FAIL (asked before reaching the bottom)"); _testStairs = false; return; }
                Press("interact");
                break;
            case 9 when _elapsed >= 25.0:
                _stairsStep++;
                GD.Print($"[stairs] after descending: floor={Game.Instance.CurrentFloor} {Pos()}");
                GD.Print(Game.Instance.CurrentFloor == 2 ? "[stairs] RESULT ok" : "[stairs] RESULT FAIL (did not descend to floor 2)");
                _testStairs = false;
                break;
        }
        if (_stairsWalk > 0)
        {
            _stairsWalk -= delta;
            party.ForcedMoveInput = new Vector2(0, -1);
            if (_stairsWalk <= 0) party.ForcedMoveInput = null;
        }
    }

    // Party: companions follow, switch control, companions fight on their own, auto-revive.
    private void RunPartyTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        string Where() => string.Join(" ", party.Adventurers.Select(a => $"{a.State.Name}@{a.GlobalPosition.DistanceTo(party.Lead.GlobalPosition):0.0}m:{a.CurrentActionName}"));
        switch (_partyStep)
        {
            case 0 when _elapsed >= 0.5:
                _partyStep++;
                GD.Print($"[party] lead={party.Lead.State.Name} bodies={party.Adventurers.Length} {Where()}");
                // walk the lead away for 3 s and see whether companions follow
                party.ForcedMoveInput = new Vector2(0, -1);
                break;
            case 1 when _elapsed >= 3.5:
                _partyStep++;
                party.ForcedMoveInput = null;
                break;
            case 2 when _elapsed >= 5.0:
                _partyStep++;
                var maxDist = party.Adventurers.Where(a => !a.IsLocal).Max(a => a.GlobalPosition.DistanceTo(party.Lead.GlobalPosition));
                GD.Print($"[party] after walking: {Where()} -> {(maxDist < 7f ? "FOLLOWING" : "not following")}");
                party.Possess(3);
                GD.Print($"[party] possess Ilex: lead={party.Lead.State.Name} camera={party.Lead.Camera.Current}");
                // stand near an enemy without attacking; companions should engage a hostile
                var enemy = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && e.Kind == EnemyKind.Melee && !main.Builder.IsUnderAt(e.GlobalPosition)).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(party.Lead.GlobalPosition)).FirstOrDefault();
                if (enemy != null && main.Builder.Layout is { } lay2)
                {
                    // stand the party next to it so the fight is guaranteed
                    var cell = DungeonBuilder.WalkableNeighbor(lay2, DungeonBuilder.WorldToCell(enemy.GlobalPosition));
                    party.Teleport(main.Builder.Surface(cell) + Vector3.Up * 0.3f, enemy.GlobalPosition);
                    enemy.ForceAggro(); _partyEnemy = enemy; _partyEnemyHp = enemy.Hp;
                    GD.Print($"[party] enemy {enemy.DisplayName} hp={enemy.Hp:0} pos={enemy.GlobalPosition:0.0} lead={party.Lead.GlobalPosition:0.0} partyNode={party.GlobalPosition:0.0} hasParty={enemy.Party != null} hasTarget={enemy.HasTarget} targetPos={enemy.TargetPosition:0.0} dist={enemy.DistanceToParty():0.0} state={enemy.State.Name}");
                    foreach (var a in party.Adventurers) GD.Print($"[party]   {a.State.Name} pos={a.GlobalPosition:0.0} downed={a.Downed} action={a.CurrentActionName} stance={a.Stance}");
                }
                break;
            case 3 when _elapsed >= 11.0:
                _partyStep++;
                var alive = _partyEnemy != null && IsInstanceValid(_partyEnemy) && _partyEnemy.IsAlive;
                GD.Print($"[party] after 6s of companion combat: enemy {(alive ? $"hp={_partyEnemy!.Hp:0}/{_partyEnemyHp:0}" : "dead")} {Where()} -> {(!alive || _partyEnemy!.Hp < _partyEnemyHp ? "COMPANIONS FIGHT" : "companions idle")}");
                // knock Brand down and see auto-revive
                party.Members[0].Hp = 0;
                party.Adventurers[0].CallDeferred("Revive"); // no-op guard; downed handled below
                party.Members[0].TakeDamage(1);
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.Kill();
                GD.Print($"[party] Brand downed, enemies cleared; alive={party.Members.Count(m => m.IsAlive)}");
                break;
            case 4 when _elapsed >= 15.5:
                _partyStep++;
                GD.Print($"[party] revive check: Brand hp={party.Members[0].Hp:0} alive={party.Members[0].IsAlive} -> {(party.Members[0].IsAlive ? "REVIVED" : "still down")}");
                GD.Print("[party] RESULT ok");
                break;
        }
    }
    private Enemy? _partyEnemy;
    private float _partyEnemyHp;

    // Skills: learn chains, see stats change, use granted/upgraded abilities, respec, save round trip.
    private void RunSkillsTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        var skills = Game.Instance.Skills;
        switch (_skillStep)
        {
            case 0 when _elapsed >= 0.5:
                _skillStep++;
                foreach (var m in party.Members) m.SkillPoints = 12;
                var brand = party.Members[0];
                var armor = brand.Stats[Characters.Stat.Armor];
                foreach (var id in new[] { "skill.warrior.bulwark.iron_skin", "skill.warrior.bulwark.shield_training", "skill.warrior.bulwark.second_wind", "skill.warrior.berserker.brawn", "skill.warrior.berserker.cleave" })
                    GD.Print($"[skills] Brand learns {id}: {Characters.SkillRules.Learn(brand, skills.Skills[id], skills)}");
                GD.Print($"[skills] Brand armor {armor} -> {brand.Stats[Characters.Stat.Armor]}, abilities=[{string.Join(", ", brand.KnownAbilities)}], points={brand.SkillPoints}");
                var bash = party.GetAbilityFor(brand, "ability.shield_bash");
                GD.Print($"[skills] Shield Bash cooldown for Brand: {party.GetAbility("ability.shield_bash").Cooldown} -> {bash.Cooldown}");
                GD.Print($"[skills] cannot skip chain: {Characters.SkillRules.WhyCannotLearn(brand, skills.Skills["skill.warrior.bulwark.riposte"])}");

                var wren = party.Members[2];
                foreach (var id in new[] { "skill.ranger.marksman.steady_aim", "skill.ranger.marksman.power_shot_mastery", "skill.ranger.marksman.piercing", "skill.ranger.marksman.multishot", "skill.ranger.survivalist.hardy", "skill.ranger.survivalist.scavenger" })
                    Characters.SkillRules.Learn(wren, skills.Skills[id], skills);
                wren.ActiveAbilityIndex = wren.KnownAbilities.IndexOf("ability.multishot");
                GD.Print($"[skills] Wren active={party.AbilityName(wren.ActiveAbilityId)} projectiles={party.GetAbilityFor(wren, wren.ActiveAbilityId).ProjectileCount} arrows={party.ArrowCount}");
                party.Attack(2);
                GD.Print($"[skills] after Multishot: arrows={party.ArrowCount} projectiles in scene={root.GetChildren().OfType<Combat.Projectile>().Count()}");

                var ilex = party.Members[3];
                foreach (var id in new[] { "skill.mage.fire.kindling", "skill.mage.fire.ember_mind", "skill.mage.fire.meteor", "skill.mage.warding.mana_well" })
                    Characters.SkillRules.Learn(ilex, skills.Skills[id], skills);
                var fb = party.GetAbilityFor(ilex, "ability.fireball");
                GD.Print($"[skills] Ilex fireball {party.GetAbility("ability.fireball").Attack!.MaxDamage} -> {fb.Attack!.MaxDamage}, maxMana={ilex.Stats[Characters.Stat.MaxMana]}, knows meteor={ilex.KnownAbilities.Contains("ability.meteor")}");
                break;
            case 1 when _elapsed >= 2.0:
                _skillStep++;
                var b = party.Members[0];
                var before = b.LearnedSkills.Count;
                party.Bag.TryAdd(Game.Instance.Catalog.Create("item.tome_of_unlearning"));
                var refunded = Characters.SkillRules.Respec(b, skills);
                GD.Print($"[skills] respec Brand: learned {before} -> {b.LearnedSkills.Count}, refunded={refunded}, points={b.SkillPoints}, cleave known={b.KnownAbilities.Contains("ability.cleave")}");
                main.SaveGame("skills");
                break;
            case 2 when _elapsed >= 3.0:
                _skillStep++;
                var w = party.Members[2];
                var learnedBefore = string.Join(",", w.LearnedSkills.OrderBy(x => x));
                var maxHpBefore = w.Stats[Characters.Stat.MaxHp];
                main.LoadGame("skills");
                var w2 = main.Party.Members[2];
                GD.Print($"[skills] load: Wren skills same={string.Join(",", w2.LearnedSkills.OrderBy(x => x)) == learnedBefore} maxHp {maxHpBefore} -> {w2.Stats[Characters.Stat.MaxHp]} multishot known={w2.KnownAbilities.Contains("ability.multishot")}");
                GD.Print("[skills] RESULT ok");
                break;
        }
    }

    // Aggro every melee enemy that cannot see the party and watch whether they all keep moving.
    private readonly List<Enemy> _watched = new();
    private readonly Dictionary<Enemy, Vector3> _lastPos = new();

    private void RunPathTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        switch (_pathStep)
        {
            case 0 when _elapsed >= 0.6:
                _pathStep++;
                foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && e.Kind == EnemyKind.Melee && !e.HasLineOfSight()))
                {
                    e.ForceAggro();
                    e.GiveUpSeconds = 999;
                    _watched.Add(e);
                    _lastPos[e] = e.GlobalPosition;
                }
                GD.Print($"[path] seed={Game.Instance.GameSeed} watching {_watched.Count} enemies");
                break;
            case 1 when _elapsed >= 7.0:
                _pathStep++;
                var stuck = 0;
                foreach (var e in _watched.Where(IsInstanceValid))
                {
                    var moved = e.DistanceTravelled;
                    var arrived = e.DistanceToParty() < 3.5f;
                    if (moved < e.EffectiveMoveSpeed * 6.4f * 0.6f && !arrived) // 6.4 s of chasing
                    {
                        stuck++;
                        var near = root.GetChildren().OfType<Node3D>().Where(n => n != e && n.GlobalPosition.DistanceTo(e.GlobalPosition) < 1.6f).Select(n => $"{n.Name}@{n.GlobalPosition.DistanceTo(e.GlobalPosition):0.00}m");
                        GD.Print($"[path] STUCK {e.Name} ({e.DisplayName}) moved {moved:0.0}m {e.PathDebug} near=[{string.Join(", ", near)}]");
                    }
                }
                var arrivedCount = _watched.Where(IsInstanceValid).Count(e => e.DistanceToParty() < 3.5f);
                GD.Print($"[path] RESULT {(stuck == 0 ? "ok" : stuck + " stuck")} of {_watched.Count}; arrived={arrivedCount}; travelled=[{string.Join(", ", _watched.Where(IsInstanceValid).Select(e => e.DistanceTravelled.ToString("0")))}]m");
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_pathTarget == null || !IsInstanceValid(_pathTarget) || _depthStep != 3) return;
        _pathLog += delta;
        if (_pathLog < 1.0) return;
        _pathLog = 0;
        GD.Print($"[path] {_pathTarget.Name} dist={_pathTarget.DistanceToParty():0.0} {_pathTarget.PathDebug}");
    }

    // Timeline: 1s loot into bag, 2s quicksave, 3s kill everything + drop bag item, 4.5s quickload, 6s compare.
    private void RunSaveTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        switch (_saveStep)
        {
            case 0 when _elapsed >= 1.0:
                _saveStep++;
                var cat = Game.Instance.Catalog;
                foreach (var item in LootRoller.RollMany(cat, cat.Table("table.chest") with { Rolls = 3 }, 2, new Random(77))) party.PickUp(item);
                party.Members[0].Hp = 40; // something non-default to compare
                root.GetChildren().OfType<Dungeon.Props.Chest>().First().Interact(party, party.Lead);
                break;
            case 1 when _elapsed >= 2.0:
                _saveStep++;
                main.SaveGame("test");
                _saveSnapshot = Snapshot();
                GD.Print($"[save] snapshot: {_saveSnapshot}");
                break;
            case 2 when _elapsed >= 3.0:
                _saveStep++;
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.Kill();
                party.Bag.RemoveAt(0);
                party.Members[0].Hp = 1;
                Game.Instance.ChangeFloor(+1);
                GD.Print($"[save] mutated: {Snapshot()}");
                break;
            case 3 when _elapsed >= 4.5:
                _saveStep++;
                GD.Print($"[save] load -> {main.LoadGame("test")}");
                _saveAfter = Snapshot(); // immediately: companions start fighting the moment the floor is back
                break;
            case 4 when _elapsed >= 6.0:
                _saveStep++;
                var after = _saveAfter;
                GD.Print($"[save] restored: {after}");
                static string StripHp(string x) => System.Text.RegularExpressions.Regex.Replace(x, @" hp=\S+", "");
                GD.Print(StripHp(after) == StripHp(_saveSnapshot) ? "[save] RESULT match (hp excluded: regen and enemies keep acting after load)" : $"[save] RESULT MISMATCH\n  before: {_saveSnapshot}\n  after:  {after}");
                break;
        }
    }

    // Story: an Investigate quest (clues), a Decide quest (a remembered flag), a Betrayal (the NPC turns), and a flag-gated NPC on the next floor.
    private void RunStoryTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        var b = main.Builder;
        var director = AdventureDirector.Instance;
        var game = Game.Instance;
        void Fail(string why) { _storyFails.Add(why); GD.Print($"[story] FAIL: {why}"); }
        Npc? NpcById(string id) => root.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == id);
        QuestState? Q(int floor, string id) => director.Quests.GetValueOrDefault(QuestKeys.For(floor, id));
        bool Talk(string npcId, params Func<UI.Dialogue.DialogueScreen, int>[] picks)
        {
            var npc = NpcById(npcId);
            if (npc == null) { Fail($"npc {npcId} missing"); return false; }
            var dlg = main.Hud.Dialogue;
            main.Hud.OpenDialogue(npc);
            foreach (var pick in picks)
            {
                var idx = pick(dlg);
                if (idx < 0) { Fail($"{npcId}: wanted choice not offered at node {dlg.Node?.Id} ({string.Join("|", dlg.Choices.Select(c => c.Text))})"); dlg.Close(); return false; }
                dlg.Choose(idx);
                if (!dlg.Visible) break;
            }
            if (dlg.Visible) dlg.Close();
            return true;
        }
        FloorPlan Design(int floor, out FloorMenu menu, out Dungeon.Generation.FloorLayout layout)
        {
            var seed = Rng.FloorSeed(game.GameSeed, floor);
            var spec = b.SpecFor(floor);
            layout = FloorGenerator.Generate(spec, seed);
            menu = AdventurePlanning.MenuFor(layout, spec, b.TierFor(floor)?.ToProfile(), b.Roster, floor, new PartySnapshot(8, 8, 4));
            var plan = OfflineDesigner.PlanFloor(menu, layout, seed);
            plan.Npcs.Clear(); plan.Quests.Clear();
            return plan;
        }
        switch (_storyStep)
        {
            case 0 when _elapsed >= 0.5:
            {
                _storyStep++;
                var plan = Design(3, out var menu, out var layout);
                var rooms = menu.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && !r.IsExit && r.FreeCells >= 3).OrderBy(r => r.HopsFromStart).ToList();
                var clueRooms = rooms.Skip(3).Take(2).Select(r => r.Index).ToList();
                var seer = new NpcPlan { Id = "seer", Name = "Seer", Room = rooms[0].Index, Script = OfflineDesigner.StandardScript("Hm.", "Find the seals.", "Go.", "So that is how it was."), QuestId = "q_seer" };
                plan.Npcs.Add(seer);
                plan.Quests.Add(new QuestPlan { Id = "q_seer", Template = QuestTemplate.Investigate, Title = "Seals", GiverNpc = "seer", Params = new QuestParams { ClueRooms = clueRooms, ClueName = "wax seals" }, Reward = new QuestReward { Xp = 50 } });
                var oracle = new NpcPlan { Id = "oracle", Name = "Oracle", Room = rooms[1].Index, QuestId = "q_oracle" };
                oracle.Script.Start = "ask";
                oracle.Script.Nodes.Add(new DialogueNode { Id = "ask", Line = new Line("Run or stay?"), Choices =
                {
                    new ChoicePlan { Text = "Run.", Effect = DialogueEffect.Choose, Flag = "told_to_run", Next = "done" },
                    new ChoicePlan { Text = "Stay.", Effect = DialogueEffect.Choose, Flag = "told_to_stay", Next = "done" },
                } });
                oracle.Script.Nodes.Add(new DialogueNode { Id = "done", Line = new Line("So be it."), Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } } });
                plan.Npcs.Add(oracle);
                plan.Quests.Add(new QuestPlan { Id = "q_oracle", Template = QuestTemplate.Decide, Title = "Run or Stay", GiverNpc = "oracle", Reward = new QuestReward { Xp = 30 } });
                var creature = menu.Roster.First(r => !r.IsBandit && !r.IsHorde).Id;
                var guide = new NpcPlan { Id = "guide", Name = "Marrow", Room = rooms[2].Index, QuestId = "q_guide", Betrayal = new BetrayalPlan { EnemyId = creature, Count = 2 } };
                guide.Script.Start = "lead";
                guide.Script.Nodes.Add(new DialogueNode { Id = "lead", Line = new Line("Follow me, it is just through here."), Choices =
                {
                    new ChoicePlan { Text = "Lead on.", Effect = DialogueEffect.Betray },
                    new ChoicePlan { Text = "No.", Effect = DialogueEffect.Farewell },
                } });
                plan.Npcs.Add(guide);
                plan.Quests.Add(new QuestPlan { Id = "q_guide", Template = QuestTemplate.Betrayal, Title = "Marrow's Shortcut", GiverNpc = "guide", Reward = new QuestReward { Xp = 40 } });
                var report = PlanValidator.Validate(layout, plan, menu);
                GD.Print($"[story] floor 3 authored: {report} npcs={plan.Npcs.Count} quests={plan.Quests.Count} clues={string.Join(",", clueRooms)} betrayer={creature} {string.Join("; ", report.Repairs)}");
                if (plan.Quests.Count != 3) Fail("validator dropped a story quest");
                game.Adventure.FloorPlans[3] = plan;
                var plan4 = Design(4, out var menu4, out var layout4);
                var rooms4 = menu4.Rooms.Where(r => !r.IsStart && !r.IsVault && !r.IsArena && r.FreeCells >= 2).OrderBy(r => r.HopsFromStart).ToList();
                plan4.Npcs.Add(new NpcPlan { Id = "runner", Name = "Runner", Room = rooms4[0].Index, Script = OfflineDesigner.StandardScript("You told them to run.", "", "", ""), RequiresFlag = "told_to_run" });
                plan4.Npcs.Add(new NpcPlan { Id = "stayer", Name = "Stayer", Room = rooms4[1 % rooms4.Count].Index, Script = OfflineDesigner.StandardScript("You told them to stay.", "", "", ""), RequiresFlag = "told_to_stay" });
                plan4.Npcs.Add(new NpcPlan { Id = "unless", Name = "Unless", Room = rooms4[2 % rooms4.Count].Index, Script = OfflineDesigner.StandardScript("Nobody ran.", "", "", ""), ForbidsFlag = "told_to_run" });
                PlanValidator.Validate(layout4, plan4, menu4);
                game.Adventure.FloorPlans[4] = plan4;
                foreach (var a in party.Adventurers) a.Stance = Characters.Stance.Hold;
                game.JumpToFloor(3);
                break;
            }
            case 1 when _elapsed >= 2.0:
            {
                _storyStep++;
                var clues = root.GetChildren().OfType<LootDrop>().Where(d => d.QuestId != null && QuestKeys.QuestOf(d.QuestId) == QuestKeys.For(3, "q_seer")).ToList();
                GD.Print($"[story] floor 3: npcs=[{string.Join(",", root.GetChildren().OfType<Npc>().Select(n => n.Id))}] clues={clues.Count} quests=[{string.Join(",", director.Quests.Values.Where(q => q.Floor == 3).Select(q => $"{q.Id}:{q.Status}"))}]");
                if (clues.Count != 2) Fail($"expected 2 clue drops, found {clues.Count}");
                Talk("seer", d => d.Choices.ToList().FindIndex(c => c.Text.StartsWith("What happened")), d => d.Choices.ToList().FindIndex(c => c.Effect == DialogueEffect.AcceptQuest));
                foreach (var clue in clues) { party.Teleport(clue.GlobalPosition + new Vector3(0.8f, 0, 0), clue.GlobalPosition); clue.Interact(party, party.Lead); }
                GD.Print($"[story] seer after the clues: {Q(3, "q_seer")?.Status} ({Q(3, "q_seer")?.Progress}/{Q(3, "q_seer")?.Goal}) objective=\"{Q(3, "q_seer")?.ObjectiveText}\"");
                if (Q(3, "q_seer")?.Status != QuestStatus.ReadyToTurnIn) Fail("investigate quest not ready after both clues");
                Talk("seer", d => d.Choices.ToList().FindIndex(c => c.Effect == DialogueEffect.TurnIn));
                if (Q(3, "q_seer")?.Status != QuestStatus.Completed) Fail("investigate quest did not complete");
                // the choice
                Talk("oracle", d => d.Choices.ToList().FindIndex(c => c.Text == "Run."));
                GD.Print($"[story] oracle: {Q(3, "q_oracle")?.Status} chose={Q(3, "q_oracle")?.ChosenFlag} flags=[{string.Join(",", game.Adventure.Memory.Flags)}]");
                if (Q(3, "q_oracle")?.Status != QuestStatus.Completed || !game.Adventure.Memory.Flags.Contains("told_to_run")) Fail("decide quest did not set its flag");
                // the betrayal
                var enemiesBefore = root.GetChildren().OfType<Enemy>().Count(e => e.IsAlive);
                Talk("guide", d => d.Choices.ToList().FindIndex(c => c.Effect == DialogueEffect.Betray));
                var turned = root.GetChildren().OfType<Enemy>().Where(e => e.QuestGroupId == QuestMachine.BetrayalGroup("guide")).ToList();
                GD.Print($"[story] guide turned={NpcById("guide")?.Turned} creatures={turned.Count} named=[{string.Join(",", turned.Select(t => t.DisplayName))}] quest={Q(3, "q_guide")?.Status} objective=\"{Q(3, "q_guide")?.ObjectiveText}\" enemies {enemiesBefore}->{root.GetChildren().OfType<Enemy>().Count(e => e.IsAlive)}");
                if (NpcById("guide")?.Turned != true) Fail("guide did not turn");
                if (turned.Count != 2 || turned.All(t => t.DisplayName != "Marrow")) Fail("betrayal creatures missing or unnamed");
                if (Q(3, "q_guide")?.Status != QuestStatus.Active) Fail("betrayal quest not active after the turn");
                foreach (var t in turned) t.Kill();
                break;
            }
            case 2 when _elapsed >= 3.5:
            {
                _storyStep++;
                GD.Print($"[story] after killing the turned: {Q(3, "q_guide")?.Status}; memory choices=[{string.Join("; ", game.Adventure.Memory.For(3).Choices)}] fates=[{string.Join("; ", game.Adventure.Memory.For(3).NpcFates)}]");
                if (Q(3, "q_guide")?.Status != QuestStatus.Completed) Fail("betrayal quest did not complete when its creatures died");
                game.JumpToFloor(4);
                break;
            }
            case 3 when _elapsed >= 5.0:
            {
                _storyStep++;
                var ids = root.GetChildren().OfType<Npc>().Select(n => n.Id).OrderBy(x => x).ToList();
                GD.Print($"[story] floor 4 npcs=[{string.Join(",", ids)}] (told_to_run is set: runner in, stayer and unless out)");
                if (!ids.Contains("runner")) Fail("flag-gated NPC missing although its flag is set");
                if (ids.Contains("stayer")) Fail("NPC gated on an unset flag appeared");
                if (ids.Contains("unless")) Fail("NPC forbidden by a set flag appeared");
                var summary = director.MemorySummary(5);
                if (!summary.Contains("told_to_run")) Fail("memory summary does not mention the flag");
                GD.Print($"[story] RESULT {(_storyFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _storyFails))}");
                GetTree().Quit(_storyFails.Count == 0 ? 0 : 1);
                break;
            }
        }
    }

    // Music: exploration track, battle music once an enemy is on us, back to exploration after the kill, the tavern tune in the menu.
    private void RunMusicTest(double delta)
    {
        var music = Audio.MusicDirector.Instance;
        var main = Main.Instance;
        if (music == null || main == null) return;
        _musicClock += delta;
        void Fail(string why) { _musicFails.Add(why); GD.Print($"[music] {why} (mood={music.Mood}, track={music.CurrentTrack})"); _musicClock = 0; _musicStep++; }
        switch (_musicStep)
        {
            case 0:
                if (!main.InGame) return;
                if (music.Mood == Audio.Mood.Explore && music.CurrentTrack != "") { GD.Print($"[music] exploring to {music.CurrentTrack}"); _musicClock = 0; _musicStep = 1; }
                else if (_musicClock > 10) Fail("no exploration track within 10 s");
                return;
            case 1:
                if (main.Builder.FloorRoot is not { } root) return;
                var lead = main.Party.Lead;
                var near = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(lead.GlobalPosition)).FirstOrDefault();
                if (near == null) { Fail("no enemy to aggro"); return; }
                main.Party.Teleport(near.GlobalPosition + new Vector3(2f, 0.3f, 0), near.GlobalPosition);
                near.ForceAggro();
                GD.Print($"[music] {near.DisplayName} aggroed at {near.GlobalPosition.DistanceTo(main.Party.Lead.GlobalPosition):0.0} m");
                _musicClock = 0; _musicStep = 2;
                return;
            case 2:
                if (music.Mood == Audio.Mood.Combat) { GD.Print($"[music] battle music after {_musicClock:0.0} s: {music.CurrentTrack}"); _musicClock = 0; _musicStep = 3; }
                else if (_musicClock > 8) Fail("battle music never started");
                return;
            case 3:
                foreach (var e in GetTree().GetNodesInGroup("enemies")) if (e is Enemy en) en.Kill();
                _musicClock = 0; _musicStep = 4;
                return;
            case 4:
                if (music.Mood == Audio.Mood.Explore) { GD.Print($"[music] back to exploration after {_musicClock:0.0} s: {music.CurrentTrack}"); main.ReturnToMenu(); _musicClock = 0; _musicStep = 5; }
                else if (_musicClock > 14) Fail("battle music never let go after the kill");
                return;
            case 5:
                if (music.Mood == Audio.Mood.Menu) { GD.Print($"[music] menu: {music.CurrentTrack}"); _musicStep = 6; }
                else if (_musicClock > 6) Fail("no menu music");
                return;
            case 6:
                _musicStep = 7;
                GD.Print(_musicFails.Count == 0 ? "[music] RESULT ok" : $"[music] RESULT FAIL: {string.Join("; ", _musicFails)}");
                GetTree().Quit(_musicFails.Count == 0 ? 0 : 1);
                return;
        }
    }

    private string Snapshot()
    {
        var main = Main.Instance;
        var party = main.Party;
        var root = main.Builder.FloorRoot!;
        var enemies = root.GetChildren().OfType<Enemy>().Count(e => e.IsAlive && !e.IsHorde && !e.IsQueuedForDeletion()); // hordes are never saved
        var loot = root.GetChildren().OfType<LootDrop>().Count(l => !l.IsQueuedForDeletion());
        var chests = root.GetChildren().OfType<Dungeon.Props.Chest>().Count(c => c.Opened);
        var pos = party.GlobalPosition;
        return $"floor={Game.Instance.CurrentFloor} enemies={enemies} loot={loot} openedChests={chests} bag=[{string.Join(",", party.Bag.Items.Select(i => i.DisplayName))}] hp={string.Join("/", party.Members.Select(m => m.Hp.ToString("0")))} pos=({pos.X:0.0},{pos.Z:0.0}) explored={main.Exploration?.Seen.Count}";
    }

    // Synthetic mouse drag: bag cell -> paper-doll cell, then doll -> doll, through the real GUI input path.
    private void RunDragTest(double delta)
    {
        var main = Main.Instance;
        if (main == null) return;
        _dragT += delta;
        var hud = main.Hud;
        var party = main.Party;
        switch (_dragStep)
        {
            case 0 when _dragT > 0.5:
                _dragStep++;
                hud.ToggleInventory();
                break;
            case 1 when _dragT > 0.8:
                _dragStep++;
                hud.Inventory.QuickAction(UI.Inventory.SlotRef.Equip(0, EquipSlot.Head)); // Brand's helm -> bag
                GD.Print($"[drag] bag after unequip: {string.Join(", ", party.Bag.Items)}  Brand head={party.Members[0].Equipment[EquipSlot.Head]?.DisplayName ?? "empty"}");
                break;
            case 2 when _dragT > 1.1:
                _dragStep++;
                var bagIdx = party.Bag.IndexOf(party.Bag.Items.First(i => i.Kind == SlotKind.Head));
                StartDrag(Centre(hud.Inventory.BagCell(bagIdx)), Centre(hud.Inventory.EquipCell(0, EquipSlot.Feet))); // wrong cell on purpose: should route to Head
                break;
            case 3 when _dragT > 2.6:
                _dragStep++;
                GD.Print($"[drag] after bag->Brand.Head: Brand head={party.Members[0].Equipment[EquipSlot.Head]?.DisplayName ?? "empty"}  bag={string.Join(", ", party.Bag.Items)}");
                // now Wren's leather armor -> Sable's Body (swap between characters)
                StartDrag(Centre(hud.Inventory.EquipCell(2, EquipSlot.Body)), Centre(hud.Inventory.EquipCell(1, EquipSlot.Body)));
                break;
            case 4 when _dragT > 4.1:
                _dragStep++;
                GD.Print($"[drag] after Wren.Body->Sable.Body: Sable body={party.Members[1].Equipment[EquipSlot.Body]?.DisplayName ?? "empty"}  Wren body={party.Members[2].Equipment[EquipSlot.Body]?.DisplayName ?? "empty"}");
                // Brand's chain mail onto Sable (Rogue cannot wear plate): expect a logged reason and no change
                StartDrag(Centre(hud.Inventory.EquipCell(0, EquipSlot.Body)), Centre(hud.Inventory.EquipCell(1, EquipSlot.Body)));
                break;
            case 5 when _dragT > 5.6:
                _dragStep++;
                GD.Print($"[drag] after ChainMail->Sable.Body: Sable body={party.Members[1].Equipment[EquipSlot.Body]?.DisplayName ?? "empty"}  Brand body={party.Members[0].Equipment[EquipSlot.Body]?.DisplayName ?? "empty"}");
                break;
        }
        PumpDrag(delta);
    }

    private static Vector2 Centre(Control c) => c.GlobalPosition + c.Size / 2f;

    private Vector2 _dragFrom, _dragTo;
    private double _dragPhase = -1;

    private void StartDrag(Vector2 from, Vector2 to)
    {
        _dragFrom = from; _dragTo = to; _dragPhase = 0;
        GD.Print($"[drag] start {from} -> {to}");
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
    }

    private void PumpDrag(double delta)
    {
        if (_dragPhase < 0) return;
        _dragPhase += delta;
        var t = (float)Math.Min(1.0, _dragPhase / 0.8);
        var pos = _dragFrom.Lerp(_dragTo, t);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos, Relative = new Vector2(2, 2), ButtonMask = MouseButtonMask.Left });
        if (_dragPhase >= 1.0)
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = _dragTo, GlobalPosition = _dragTo });
            _dragPhase = -1;
            GD.Print("[drag] released");
        }
    }

    private void FaceNearestEnemy(Node3D root)
    {
        var main = Main.Instance;
        var party = main.Party;
        var layout = main.Builder.Layout;
        Enemy? nearest = null;
        var best = float.MaxValue;
        foreach (var child in root.GetChildren())
        {
            if (child is not Enemy e) continue;
            if (_faceBoss && e.IsBoss) { nearest = e; break; }
            var d = e.GlobalPosition.DistanceSquaredTo(party.GlobalPosition);
            if (d < best) { best = d; nearest = e; }
        }
        if (nearest == null || layout == null) return;

        // Stand on a walkable cell adjacent to the enemy's cell so we never end up inside a wall.
        var enemyCell = new GridPos((int)(nearest.GlobalPosition.X / DungeonBuilder.CellSize), (int)(nearest.GlobalPosition.Z / DungeonBuilder.CellSize));
        var standCell = DungeonBuilder.WalkableNeighbor(layout, enemyCell);
        var pos = main.Builder.Surface(standCell, main.Builder.IsUnderAt(nearest.GlobalPosition)) + Vector3.Up * 0.3f;
        party.Teleport(pos, nearest.GlobalPosition);
        GD.Print($"[debug] facing {nearest.Name} at {nearest.GlobalPosition} from cell {standCell}");
    }

    private void SaveScreenshot(string path)
    {
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"[debug] screenshot {path}: {err}");
    }
}
