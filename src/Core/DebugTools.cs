using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.AiClients;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
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
///   --clear-enemies     every enemy dies as soon as its floor is built (quiet screenshots of the cast)
///   --bake-kill-barks=DIR  with --builtin --ai: render every member's and fighter's kill line into DIR (assets/voice/builtin for the shipped bank), then quit
///   --bake-cast-voice=DIR  with --builtin --ai: render every line of the recurring cast and every boss line into DIR, write the plans with their ids to DIR/cast-plans, then quit
///   --face-ramp[=bottom|top|ledge|gallery]  stand a cell short of a corridor ramp looking up it (bottom), at its top looking down, on a ledge's ramp looking into the room, or on the ledge looking down
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
    private bool _openShop, _openedShop; // --open-shop: the merchant's shop open at --after (slot 0 shown sold), for screenshots
    private readonly List<string> _pressActions = new();
    private bool _clearEnemies;
    private bool _levelUp;
    private bool _testDrag;
    private bool _testSave;
    private bool _testDepth;
    private int _depthStep;
    private bool _testCollectives;
    private int _collStep;
    private readonly List<string> _collFails = new();
    private Collective? _collBig, _collAlarm;
    private Enemy? _collPatrol, _collRunner;
    private Vector3 _collPatrolStart;
    private double _collAlarmAt; // when the alarm arrived (or the wait for it ended): the regroup check follows it
    private bool _talk;
    private bool _dance, _danced; // --dance: every companion starts the victory dance a second before --after
    private double _wander, _wanderCheck; private Vector3 _wanderLast; // --wander=SECONDS: the lead walks ahead, turning when it bumps into something
    private bool _talked;
    private string? _showcase;
    private bool _testQuest;
    private int _questStep;
    private readonly List<string> _questFails = new();
    private float _questXpBefore;
    public static DebugTools? Instance { get; private set; }
    /// <summary>Any automation flag: skip the title screen and shorten cosmetic waits.</summary>
    public bool Automated => _started;
    /// <summary>
    /// The floor a new game opens on: null for an ordinary run and a plain --server (the game's own rule, the tavern,
    /// where co-op players gather until the first descent), floor 1 for every automation and --test-net-server, and
    /// the tavern too when --floor=0 is given (a deeper --floor= opens on 1 and jumps).
    /// </summary>
    public int? StartFloor => _floorGiven ? Math.Clamp(_startFloor, 0, 1) : (Automated || _testNetServer ? 1 : null);
    /// <summary>The floor the scripted checks face and inspect: the --floor= asked for (never below the tavern, 0), else a scenario's own floor, at least 1.</summary>
    private int TargetFloor => _floorGiven ? Math.Max(0, _startFloor) : Math.Max(1, _startFloor); // a scenario that picks its own floor (--test-horde, an objective) still counts
    public string? ReplayDir { get; private set; }
    public string? RecordDir { get; private set; }
    /// <summary>--story-brief=FILE: a campaign bible (markdown) the planner must follow; it rides along every planning call as a cached system block.</summary>
    public string? StoryBriefPath { get; private set; }
    /// <summary>--hazard=press|spikes: force one hazard of that kind near the up stairs on every floor.</summary>
    public string? ForcedHazard { get; private set; }
    /// <summary>--hazard=press@2: the forced hazard only on that floor; null means every floor.</summary>
    public int? ForcedHazardFloor { get; private set; }
    private string? _testHazard;
    private int _hazardStep;
    private float _hazardClock;
    /// <summary>--trace-voice: log every voice job the renderer starts (id, model, voice, style, text).</summary>
    public bool TraceVoice { get; private set; }
    public bool ForceAi { get; private set; }
    public bool NoAi { get; private set; }
    /// <summary>--no-ambush / --no-portals: no corridor ambushers, no reinforcement portals this run.</summary>
    public bool NoAmbush { get; private set; }
    public bool NoPortals { get; private set; }
    /// <summary>--no-gathering: the safe room's clock never sends creatures to its door.</summary>
    public bool NoGathering { get; private set; }
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
    /// <summary>--layout=KIND forces every floor to that shape (standard, labyrinth, ring, mine, flooded, burning).</summary>
    public string? ForcedLayout { get; private set; }
    /// <summary>--objective=KIND: every eligible floor carries this objective (boss floors and incompatible shapes still refuse).</summary>
    public string? ForcedObjective { get; private set; }
    /// <summary>--lethality=N: pin the dungeon's damage multiplier for this run (headless flows that walk through traps use 1).</summary>
    public float? ForcedLethality { get; private set; }
    private bool _testFloors;
    private bool _testStructures;
    private bool _testHorde;
    private bool _testKeys, _testedKeys;
    private bool _checkVoiceBank, _checkedVoiceBank;
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
    private string? _bakeKillBarksDir;
    private string? _bakeCastVoiceDir;
    private int _bakeCastVoiceQueued = -1;
    private int _bakeKillBarksQueued = -1;
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
    private bool _floorGiven; // --floor= was on the command line: even --floor=0 is asked for, not defaulted
    private bool _faceBoss;
    private bool _faceLedge, _facedRamp;
    private string? _faceRamp;
    private bool _facePit;
    private string? _faceNode;
    private int _faceBack = 1;
    private (int x, int y)? _faceCell;
    private string[]? _dropIds;
    private string? _equipId;
    private bool _equipped;
    // --pose-sheet=DIR: each weapon (or --pose-items=ids) held at every pose of its moves (and of --pose-skills=ids), one PNG per pose
    private string? _poseSheetDir;
    // --call-bot=Sable: a co-op client calls that story hero in as a bot three seconds into the game (screenshots of bots)
    private string? _callBot;
    private string[]? _poseItems;
    private string[] _poseSkills = Array.Empty<string>();
    private readonly Queue<(string file, Items.MoveKey key)> _poseShots = new();
    private string? _posePending;
    private int _poseIndex = -1, _poseFrames;
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
    /// <summary>--test-objective=KIND: jump to floor 2 with that objective, drive it to completion, check the stairs.</summary>
    private string? _testObjective;
    /// <summary>--print-map: once the floor stands, print its map and where the objective's pieces are, then quit.</summary>
    private bool _printMap, _printedMap;
    private int _objectiveStep;
    private float _objectiveHp;
    private bool _objectiveEventSeen;
    /// <summary>--test-gas-wipe: floor 2 toxic, the gas full and the floor remembered the way the server's autosave does, the party gassed to a wipe; the floor must come back with the gas at its grace and nobody dying again.</summary>
    private bool _testGasWipe;
    private int _gasWipeStep, _gasWipes;
    private double _gasWipeAt;
    private int _villainLines;
    private bool _askStairs, _askedStairsDebug;
    private int _stairsStep;
    private double _stairsWalk;
    private int _vertStep;
    private bool _testParty, _testGold;
    private int _goldStep, _goldFails, _goldBefore;
    private LootDrop? _goldPile;
    private int _partyStep;
    private bool _server, _netClient, _testNetServer;
    private bool _testMusic;
    private bool _bakeResume;
    /// <summary>--keep-old-voice: a bake leaves the bank's unreferenced files alone (by default it sweeps them).</summary>
    private bool _keepOldVoice;
    /// <summary>--purge-voice: sweep the shipped bank against the shipped campaign and the cache against every save, then quit.</summary>
    private bool _purgeVoice, _purgedVoice;
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
            else if (arg == "--open-shop") _openShop = true;
            else if (arg == "--level-up") _levelUp = true;
            else if (arg == "--menu-only") _menuOnly = true;
            else if (arg == "--open-settings") { _menuOnly = true; _openSettings = true; }
            else if (arg == "--look-back") _lookBack = true;
            else if (arg.StartsWith("--possess=")) _possess = int.Parse(arg["--possess=".Length..]);
            else if (arg == "--swing") _swing = true;
            else if (arg == "--count-props") { _countProps = true; _quitAfter = 2; }
            else if (arg.StartsWith("--floor=")) { _startFloor = int.Parse(arg["--floor=".Length..]); _floorGiven = true; }
            else if (arg.StartsWith("--seed=")) _seed = ulong.Parse(arg["--seed=".Length..]);
            else if (arg == "--test-path") { _testPath = true; _quitAfter = 8; }
            else if (arg == "--test-tavern" || arg == "--test-tavern-skip") { _testTavern = true; _tavernSkip = arg == "--test-tavern-skip"; _startFloor = 0; _floorGiven = true; _quitAfter = 20; }
            else if (arg == "--test-skills") { _testSkills = true; _faceEnemy = true; _quitAfter = 7; }
            else if (arg == "--test-abilities") { _testAbilities = true; _quitAfter = 150; }
            else if (arg == "--test-hit-fx") { _testHitFx = true; NoAmbush = true; NoPortals = true; TraceAi = false; _quitAfter = 40; }
            else if (arg == "--test-vertical") { _testVertical = true; _quitAfter = 14; }
            else if (arg == "--test-stairs") { _testStairs = true; _quitAfter = 40; }
            else if (arg == "--test-gas-wipe") { _testGasWipe = true; ForcedObjective = "toxic"; ForcedLayout ??= "standard"; ForcedLethality ??= 3f; _quitAfter = 75; }
            else if (arg.StartsWith("--test-objective=")) { _testObjective = arg["--test-objective=".Length..].Trim().ToLowerInvariant(); ForcedObjective = _testObjective; ForcedLayout ??= "standard"; ForcedLethality ??= 1f; _quitAfter = 30; }
            else if (arg == "--ask-stairs") _askStairs = true;
            else if (arg == "--test-gold") { _testGold = true; _quitAfter = 14; }
            else if (arg == "--test-party") { _testParty = true; _faceEnemy = true; _quitAfter = 16; }
            else if (arg == "--face-boss") { _faceBoss = true; _faceEnemy = true; }
            else if (arg == "--face-ledge") _faceLedge = true;
            else if (arg.StartsWith("--face-ramp")) { _faceRamp = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : "bottom"; _quitAfter = Math.Max(_quitAfter, 3); }
            else if (arg == "--face-pit") _facePit = true;
            else if (arg.StartsWith("--face-node=")) { var spec = arg["--face-node=".Length..].Split(':'); _faceNode = spec[0]; if (spec.Length > 1) _faceBack = int.Parse(spec[1]); }
            else if (arg.StartsWith("--face-cell=")) { var spec = arg["--face-cell=".Length..].Split(':'); var xy = spec[0].Split(','); _faceCell = (int.Parse(xy[0]), int.Parse(xy[1])); if (spec.Length > 1) _faceBack = int.Parse(spec[1]); _quitAfter = Math.Max(_quitAfter, 3); }
            else if (arg.StartsWith("--drop=")) _dropIds = arg["--drop=".Length..].Split(',');
            else if (arg.StartsWith("--equip=")) _equipId = arg["--equip=".Length..]; // a weapon in the lead's hands (first-person view checks)
            else if (arg.StartsWith("--call-bot=")) _callBot = arg["--call-bot=".Length..];
            else if (arg == "--probe-pickups") _probePickups = true;
            else if (arg == "--probe-near") _probeNear = true;
            else if (arg == "--probe-shrine") _probeShrine = true;
            else if (arg.StartsWith("--pose-sheet=")) { _poseSheetDir = arg["--pose-sheet=".Length..]; _quitAfter = Math.Max(_quitAfter, 900); }
            else if (arg.StartsWith("--pose-items=")) _poseItems = arg["--pose-items=".Length..].Split(',');
            else if (arg.StartsWith("--pose-skills=")) _poseSkills = arg["--pose-skills=".Length..].Split(',');
            else if (arg == "--test-drag") { _testDrag = true; _quitAfter = 6; }
            else if (arg == "--test-save") { _testSave = true; _faceEnemy = true; _quitAfter = 9; }
            else if (arg == "--test-vault-puzzle") { _testVault = true; _builtin = true; NoAmbush = true; NoPortals = true; TraceAi = false; _quitAfter = 900; _vaultAt = 1.0; }
            else if (arg == "--test-depth") { _testDepth = true; TraceAi = false; _quitAfter = 16; }
            else if (arg == "--test-plan") { _testPlan = true; TraceAi = false; _quitAfter = 6; }
            else if (arg == "--test-collectives") { _testCollectives = true; TraceAi = false; _quitAfter = 45; }
            else if (arg == "--test-quest") { _testQuest = true; TraceAi = false; _quitAfter = 14; }
            else if (arg == "--talk") _talk = true;
            else if (arg == "--dance") _dance = true;
            else if (arg.StartsWith("--wander=")) _wander = double.Parse(arg["--wander=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--showcase=")) _showcase = arg["--showcase=".Length..];
            else if (arg == "--ai") ForceAi = true;
            else if (arg == "--no-ai") NoAi = true;
            else if (arg.StartsWith("--replay-plans=")) ReplayDir = ProjectSettings.GlobalizePath(arg["--replay-plans=".Length..]);
            else if (arg.StartsWith("--record-plans=")) RecordDir = arg["--record-plans=".Length..];
            else if (arg == "--trace-voice") TraceVoice = true;
            else if (arg.StartsWith("--hazard="))
            {
                var spec = arg["--hazard=".Length..].Trim().ToLowerInvariant();
                var at = spec.IndexOf('@');
                if (at > 0 && int.TryParse(spec[(at + 1)..], out var onFloor)) { ForcedHazardFloor = onFloor; spec = spec[..at]; }
                ForcedHazard = spec;
            }
            else if (arg.StartsWith("--test-hazard=")) { _testHazard = arg["--test-hazard=".Length..].Trim().ToLowerInvariant(); ForcedHazard = _testHazard; ForcedLayout ??= "standard"; ForcedLethality ??= 3f; _seed ??= 4242; _quitAfter = 60; }
            else if (arg.StartsWith("--story-brief=")) StoryBriefPath = ProjectSettings.GlobalizePath(arg["--story-brief=".Length..]);
            else if (arg.StartsWith("--fake-ai=")) FakeAiDelay = float.Parse(arg["--fake-ai=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg == "--fake-ai") FakeAiDelay = 1f;
            else if (arg == "--test-ai-flow") { _testAiFlow = true; FakeAiDelay ??= 1.2f; TraceAi = false; _quitAfter = 22; }
            else if (arg.StartsWith("--bake-campaign=")) { _bakeDir = ProjectSettings.GlobalizePath(arg["--bake-campaign=".Length..]); TraceAi = false; _quitAfter = 1800; }
            else if (arg == "--bake-resume") _bakeResume = true;
            else if (arg == "--keep-old-voice") _keepOldVoice = true;
            else if (arg == "--purge-voice") { _purgeVoice = true; _menuOnly = true; _quitAfter = 10; }
            else if (arg.StartsWith("--voice-model=")) { Settings.VoiceModel = arg["--voice-model=".Length..]; Settings.RunwareVoiceModel = Settings.VoiceModel; Settings.Apply(); }
            else if (arg.StartsWith("--planner=")) { Settings.PlannerProvider = arg["--planner=".Length..].Trim().ToLowerInvariant() == Settings.ProviderRunware ? Settings.ProviderRunware : Settings.ProviderClaude; }
            else if (arg.StartsWith("--voice-provider=")) { Settings.VoiceProvider = arg["--voice-provider=".Length..].Trim().ToLowerInvariant() == Settings.ProviderRunware ? Settings.ProviderRunware : Settings.ProviderGemini; Settings.Apply(); }
            else if (arg.StartsWith("--planner-model=")) { Settings.RunwareTextModel = arg["--planner-model=".Length..]; }
            else if (arg.StartsWith("--voice-fallback=")) Settings.VoiceFallbackModels = arg["--voice-fallback=".Length..];
            else if (arg.StartsWith("--voice-rpm=")) Settings.VoiceRequestsPerMinute = float.Parse(arg["--voice-rpm=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--voice-model-minor=")) { Settings.VoiceModelMinor = arg["--voice-model-minor=".Length..]; Settings.RunwareVoiceModelMinor = Settings.VoiceModelMinor; Settings.Apply(); }
            else if (arg == "--list-voice-models") { _listVoiceModels = true; _menuOnly = true; _quitAfter = 30; }
            else if (arg == "--check-voice-bank") { _checkVoiceBank = true; _menuOnly = true; _quitAfter = 10; }
            else if (arg.StartsWith("--probe-voice-model=")) { _probeVoiceModel = arg["--probe-voice-model=".Length..]; _menuOnly = true; _quitAfter = 60; }
            else if (arg == "--probe-planner") { _probePlanner = true; _menuOnly = true; _quitAfter = 120; }
            else if (arg == "--builtin") { _builtin = true; _quitAfter = Math.Max(_quitAfter, 6); }
            else if (arg == "--fake-voice") FakeVoice = true;
            else if (arg == "--test-floors") { _testFloors = true; TraceAi = false; _quitAfter = 14; }
            else if (arg == "--test-structures") { _testStructures = true; TraceAi = false; _quitAfter = 12; }
            else if (arg == "--test-horde") { _testHorde = true; TraceAi = false; _quitAfter = 40; _startFloor = 3; }
            else if (arg == "--no-ambush") NoAmbush = true;
            else if (arg == "--no-portals") NoPortals = true;
            else if (arg == "--no-gathering") NoGathering = true;
            else if (arg == "--test-safe-room") { _testSafeRoom = true; NoAmbush = true; NoPortals = true; TraceAi = false; _quitAfter = 120; }
            else if (arg == "--test-elites-and-bosses") { _testElites = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 240; _startFloor = 8; _floorGiven = true; }
            else if (arg == "--test-writing") { _testWriting = true; _builtin = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; ForcedLethality ??= 1f; _quitAfter = 130; }
            else if (arg == "--test-narration") { _testNarration = true; _builtin = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; ForcedLethality ??= 1f; _quitAfter = 170; }
            else if (arg == "--test-items-and-builds") { _testItems = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 45; }
            else if (arg == "--test-fortune") { _testFortune = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 200; _startFloor = 2; _floorGiven = true; }
            else if (arg == "--test-run-structure") { _testRunStructure = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 300; }
            else if (arg == "--test-combat-feel") { _testCombatFeel = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 75; }
            else if (arg == "--test-co-op") { _testCoOp = true; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 40; }
            else if (arg.StartsWith("--rehash-plans=")) { _rehashDir = ProjectSettings.GlobalizePath(arg["--rehash-plans=".Length..]); _menuOnly = true; _quitAfter = Math.Max(_quitAfter, 30); }
            else if (arg == "--test-ambush") { _testAmbush = true; _quitAfter = 40; _startFloor = 2; }
            else if (arg == "--test-portal") { _testPortal = true; TraceAi = false; _quitAfter = 60; }
            else if (arg == "--test-keys") { _testKeys = true; _menuOnly = true; _quitAfter = 2; }
            else if (arg == "--tremor") { _tremor = true; _quitAfter = Math.Max(_quitAfter, 6); }
            else if (arg == "--test-tremor") { _testTremor = true; _tremor = true; TraceAi = false; _quitAfter = 12; }
            else if (arg.StartsWith("--theme=")) ForcedTheme = arg["--theme=".Length..];
            else if (arg.StartsWith("--layout=")) ForcedLayout = arg["--layout=".Length..];
            else if (arg.StartsWith("--objective=")) ForcedObjective = arg["--objective=".Length..];
            else if (arg == "--print-map") { _printMap = true; if (_quitAfter <= 0) _quitAfter = 4; }
            else if (arg.StartsWith("--lethality=")) ForcedLethality = float.Parse(arg["--lethality=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--vault=")) ForcedVault = arg["--vault=".Length..];
            else if (arg.StartsWith("--inspect=")) { var xy = arg["--inspect=".Length..].Split(','); _inspect = (int.Parse(xy[0]), int.Parse(xy[1])); _quitAfter = Math.Max(_quitAfter, 3); }
            else if (arg.StartsWith("--bake-voice=")) _bakeVoiceDir = ProjectSettings.GlobalizePath(arg["--bake-voice=".Length..]);
            else if (arg.StartsWith("--bake-kill-barks=")) { _bakeKillBarksDir = ProjectSettings.GlobalizePath(arg["--bake-kill-barks=".Length..]); _quitAfter = Math.Max(_quitAfter, 900); }
            else if (arg.StartsWith("--bake-cast-voice=")) { _bakeCastVoiceDir = ProjectSettings.GlobalizePath(arg["--bake-cast-voice=".Length..]); _quitAfter = Math.Max(_quitAfter, 900); }
            else if (arg.StartsWith("--bake-all-voice=")) { _bakeAllVoiceDir = ProjectSettings.GlobalizePath(arg["--bake-all-voice=".Length..]); _quitAfter = Math.Max(_quitAfter, 3600); }
            else if (arg == "--test-jailer") { _testJailer = true; _quitAfter = Math.Max(_quitAfter, 160); }
            else if (arg.StartsWith("--dump-enemies=")) { foreach (var t in arg["--dump-enemies=".Length..].Split(',')) _dumpAt.Add(float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)); _dumpAt.Sort(); }
            else if (arg.StartsWith("--voice-concurrent=")) VoiceRenderer.MaxConcurrent = Math.Clamp(int.Parse(arg["--voice-concurrent=".Length..]), 1, 8);
            else if (arg.StartsWith("--voice-budget=")) CampaignBaker.VoiceBudgetLines = int.Parse(arg["--voice-budget=".Length..]);
            else if (arg.StartsWith("--spend-cap=")) Settings.SpendCapUsd = double.Parse(arg["--spend-cap=".Length..], System.Globalization.CultureInfo.InvariantCulture); // this run only; not saved
            else if (arg == "--test-voice") { _testVoice = true; FakeVoice = true; TraceAi = false; _quitAfter = 16; }
            else if (arg == "--test-bake-ui") { _testBakeUi = true; _menuOnly = true; FakeVoice = true; FakeAiDelay = 0f; TraceAi = false; _quitAfter = 240; }
            else if (arg.StartsWith("--press=")) _pressActions.AddRange(arg["--press=".Length..].Split(','));
            else if (arg == "--clear-enemies") _clearEnemies = true; // every enemy on the floor dies as soon as it is built: screenshots of the cast without a brawl
            else if (arg == "--smoke") { _smoke = true; _faceEnemy = true; TraceAi = true; _quitAfter = 11; }
            else if (arg == "--trace-ai") TraceAi = true;
            else if (arg == "--test-music") { _testMusic = true; TraceAi = false; _quitAfter = 45; }
            else if (arg == "--test-story") { _testStory = true; TraceAi = false; _quitAfter = 20; }
            else if (arg == "--server") _server = true;
            else if (arg == "--test-net-server") { _server = true; _testNetServer = true; TraceAi = false; _quitAfter = 120; }
            else if (arg == "--test-net-client" || arg == "--test-net-rejoin") { _netClient = true; TraceAi = false; _quitAfter = 90; }
            else if (arg.StartsWith("--join=")) _netClient = true;
        }
        if (ForcedLethality is { } lethality) Dungeon.TierRules.Lethality = lethality; // Settings loaded before the flags were read
        if (_bakeDir != null && ReplayDir != null) Adventure.Ai.ClaudePlanner.StrictReplay = true; // a bake from recordings stops at the first missing one rather than filling in
        if ((_testCollectives || _testQuest || _testVoice || _testStory) && _seed == null) _seed = 4242; // scripted scenarios want a repeatable floor
        if (_testStructures && _seed == null) _seed = StructureSeed();
        if (_testHorde && _seed == null) _seed = HordeSeed();
        if (_testAbilities && _seed == null) _seed = 7; // its fights are flaky on random floors
        if (_testHitFx && _seed == null) _seed = 7; // the abilities flow's floor: a repeatable first regular enemy
        if (_testWriting) Dungeon.TierRules.Lethality = ForcedLethality!.Value; // the shipped campaign's floor 1: Crane with his aside; no seed needed
        if (_testItems)
        {
            _seed ??= 7;
            ForcedLethality ??= 1f;
        }
        if (_testCombatFeel) { _seed ??= 7; ForcedLethality ??= 1f; Dungeon.TierRules.Lethality = ForcedLethality.Value; }
        if (_testElites) { _seed ??= 8; ForcedLethality ??= 1f; Dungeon.TierRules.Lethality = ForcedLethality.Value; }
        if (_testFortune) { _seed ??= 7; ForcedLethality ??= 1f; Dungeon.TierRules.Lethality = ForcedLethality.Value; }
        if (_testRunStructure) { _seed ??= 7; ForcedLethality ??= 1f; Dungeon.TierRules.Lethality = ForcedLethality.Value; }
        if (_testCoOp) { _seed ??= 7; ForcedLethality ??= 1f; Dungeon.TierRules.Lethality = ForcedLethality.Value; }
        if (_testSafeRoom)
        {
            _seed ??= 7;
            ForcedLethality ??= 1f;
            Dungeon.TierRules.Lethality = ForcedLethality.Value;
            if (Dungeon.Generation.FloorObjectives.TryParse(ForcedObjective, out var srObjective) && srObjective != Dungeon.Generation.FloorObjectiveKind.None && _startFloor < 2) { _startFloor = 2; ForcedLayout ??= "standard"; } // floor 1 never carries an objective, and not every shape takes every objective
        }
        // Automation never spends the player's keys unless --ai is given explicitly: scripted runs use the offline designer and no live voice.
        var automation = _screenshotPath != null || _quitAfter > 0 || _facePit || _faceNode != null || _faceCell != null || _faceEnemy || _autoAttack || _smoke || _testDrag || _testSave || _levelUp || _pressActions.Count > 0 || _openMap || _talk || _dance || _wander > 0 || _builtin || _menuOnly;
        if (automation && !ForceAi) { NoAi = true; NoVoice = !FakeVoice; }
        // scripted scenarios were written against the plain floors of their seeds: no objective floor unless one is asked for by name
        // (--test-floors and --builtin keep the deal, so the ground under objective floors is checked and the shipped campaign plays as shipped)
        if (automation && !_testFloors && !_builtin && _bakeDir == null && _rehashDir == null && _testObjective == null && ForcedObjective == null) ForcedObjective = "none";
        if (_server || _netClient) NoVoice = true; // a headless server has no speakers; the test client keeps its key in its pocket
        if (_testNarration && EventBus.Instance != null) { EventBus.Instance.Narration += (text, voice) => { _nrHeard.Add(text); _nrHeardAt.Add(_elapsed); _nrVoiceOf[text] = voice; }; _nrListening = true; } // from the first floor build: the tier line opens floor 1
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

        if ((_faceNode != null || _faceCell != null) && !_facedNode && _possess < 0 && _elapsed >= (_startFloor > 1 ? 1.2 : 0.6) && Game.Instance.CurrentFloor == TargetFloor && Main.Instance?.Builder.FloorRoot is { } faceRoot && Main.Instance.Builder.Layout is { } faceLayout)
        {
            _facedNode = true;
            Vector3? look = null;
            GridPos? stand = null;
            if (_faceNode != null && Main.Instance.Party.Adventurers.FirstOrDefault(a => a.State.Name == _faceNode) is { } member && member != Main.Instance.Party.Local)
            {
                // a party member by name (with --possess=N to lead as someone else): stood in front of the camera, facing it
                var lead = Main.Instance.Party.Local!;
                member.Stance = Characters.Stance.Hold;
                member.Teleport(lead.GlobalPosition - lead.GlobalTransform.Basis.Z * Math.Max(2, _faceBack), lead.GlobalPosition);
                // the others wait behind the camera so nobody's shoulder fills the frame
                var k = 0;
                foreach (var other in Main.Instance.Party.Active.Where(a => a != member && a != lead))
                {
                    other.Stance = Characters.Stance.Hold;
                    other.Teleport(lead.GlobalPosition + lead.GlobalTransform.Basis.Z * 3f + lead.GlobalTransform.Basis.X * (k++ - 0.5f) * 1.5f, lead.GlobalPosition);
                }
                GD.Print($"[face-node] {member.State.Name} placed {Math.Max(2, _faceBack)} m in front of {lead.State.Name}; member {member.ModelDebug}; lead {lead.ModelDebug}");
                return;
            }
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
            else if (_faceNode == "camp" && faceLayout.Structures.FirstOrDefault(s => s.Kind == StructureKind.Camp) is { } camp)
            {
                // the floor's bandit camp, seen from a few cells off its tents
                look = Main.Instance.Builder.Surface(camp.Anchor) + Vector3.Up * 0.5f;
                GD.Print($"[face] camp at {camp.Anchor}");
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
        if (_equipId != null && !_equipped && _elapsed >= 0.5 && Main.Instance?.Party?.Lead is { } equipLead)
        {
            _equipped = true;
            var cat = Game.Instance.Catalog;
            var lead = equipLead.State;
            // a legendary's id (leg.*) equips that legendary on its base, glow and streak included
            var legendary = cat.Legendaries.TryGetValue(_equipId, out var leg) ? leg : null;
            if (legendary == null && !cat.TryBase(_equipId, out _)) GD.Print($"[equip] unknown item {_equipId}");
            else
            {
                var item = legendary != null ? Items.LootRoller.MakeLegendary(cat, legendary, Math.Max(1, legendary.ItemLevelMin), new Random(1)) : cat.Create(_equipId);
                lead.Equipment.Equip(Items.EquipSlot.Weapon, item);
                lead.Recalculate();
                GD.Print($"[equip] {lead.Name} now holds {item.DisplayName} ({lead.WeaponProfile.Kind}{(item.TwoHanded ? ", two-handed" : "")})");
            }
        }
        if (_callBot != null && _elapsed >= 3 && Net.NetSession.IsClient && Net.NetSession.Instance.Client?.Phase == Net.ClientHandshake.Phase.InGame)
        {
            Net.NetSession.Instance.Send(new Net.AddBotIntent(_callBot));
            GD.Print($"[bot] asked the server for {_callBot}");
            _callBot = null;
        }
        TickPickupProbe(delta);
        TickProbeNear(delta);
        TickShrineProbe(delta);
        if (_poseSheetDir != null && _elapsed >= 1.5 && Main.Instance?.Party?.Lead is { } poser) PoseSheetStep(poser);
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
        if (_faceRamp != null && !_facedRamp && _elapsed >= 0.6 && Main.Instance?.Builder.Layout is { Ramps.Count: > 0 } rampLay)
        {
            // --face-ramp[=bottom|top]: stand a cell short of a corridor ramp (not a ledge's) and look along it
            _facedRamp = true;
            var b = Main.Instance.Builder;
            var lows = rampLay.Ramps.Where(kv => kv.Value.LowHeight % 4 == 0 && rampLay.Ledges.All(l => l.RampBottom != kv.Key)).OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList();
            if (_faceRamp == "gallery" && rampLay.Ledges.Count > 0)
            {
                // on the ledge itself, looking down into the room
                var ledge = rampLay.Ledges[0];
                Main.Instance.Party.Teleport(b.Surface(ledge.EnemySpawn) + Vector3.Up * 0.3f, b.Surface(ledge.Room.Center) + Vector3.Up * 0.5f);
                GD.Print($"[face-ramp] on the gallery at {ledge.EnemySpawn}, looking at the room centre {ledge.Room.Center}; cells: {b.DebugCell(ledge.EnemySpawn)} {b.DebugCell(ledge.Room.Center)}");
            }
            else if (_faceRamp == "ledge" && rampLay.Ledges.Count > 0)
            {
                // on a ledge's ramp, high half, looking sideways into the room it climbs out of
                var ledge = rampLay.Ledges[0];
                var top = ledge.RampTop;
                var dir = top - ledge.RampBottom;
                var side = new Dungeon.Generation.GridPos(dir.Y, -dir.X);
                if (!rampLay.IsWalkable(top + side)) side = new Dungeon.Generation.GridPos(-dir.Y, dir.X);
                Main.Instance.Party.Teleport(b.Surface(top) + Vector3.Up * 0.3f, b.Surface(top + side + side) + Vector3.Up * 5f);
                GD.Print($"[face-ramp] ledge ramp {ledge.RampBottom}->{top}; standing on {top}, looking at {top + side + side}; cells: {b.DebugCell(top)} {b.DebugCell(top + side)} {b.DebugCell(top + side + side)}");
            }
            else if (lows.Count > 0)
            {
                var (low, info) = (lows[0].Key, lows[0].Value);
                var high = low + info.Dir;
                var below = low - info.Dir;
                var above = high + info.Dir;
                var stand = _faceRamp == "top" ? above : below;
                var look = _faceRamp == "top" ? below : above;
                Main.Instance.Party.Teleport(b.Surface(stand) + Vector3.Up * 0.3f, b.Surface(look) + Vector3.Up * 1.6f);
                GD.Print($"[face-ramp] ramp {low}->{high} dir {info.Dir}; standing at {stand} ({rampLay[stand]} L{rampLay.LevelOf(stand)}) looking at {look} ({rampLay[look]} L{rampLay.LevelOf(look)}); cells: {b.DebugCell(below)} {b.DebugCell(low)} {b.DebugCell(high)} {b.DebugCell(above)}");
            }
        }
        if (_faceLedge && !_facedLedge && _elapsed >= 0.6 && Main.Instance?.Builder.Layout is { Ledges.Count: > 0 } lay)
        {
            _facedLedge = true;
            var ledge = lay.Ledges[0];
            var b = Main.Instance.Builder;
            Main.Instance.Party.Teleport(b.Surface(ledge.RampBottom + new Dungeon.Generation.GridPos(0, 2)) + Vector3.Up * 0.3f, b.Surface(ledge.EnemySpawn) + Vector3.Up * 1.2f);
        }
        if (_wander > 0 && Main.Instance?.InGame == true && Main.Instance.Party is { HasBodies: true, Local: { } walker } wanderParty)
        {
            _wander -= delta;
            wanderParty.ForcedMoveInput = _wander > 0 ? new Vector2(0, -1) : null;
            _wanderCheck += delta;
            if (_wanderCheck >= 1.2)
            {
                if (walker.GlobalPosition.DistanceTo(_wanderLast) < 0.6f) walker.RotateY(Mathf.DegToRad(90f + (float)GD.RandRange(-30, 30))); // stuck on something: turn
                _wanderLast = walker.GlobalPosition; _wanderCheck = 0;
            }
        }
        if (_dance && !_danced && Main.Instance?.InGame == true && _elapsed >= _after - 1.0)
        {
            _danced = true;
            foreach (var a in Main.Instance.Party.Adventurers) if (a != Main.Instance.Party.Local && !a.Downed) a.Celebrate();
            GD.Print("[dance] companions start the victory dance");
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
                if (_showcase != null) Main.Instance.Hud.Dialogue.ShowMove(_showcase);
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
        if (_purgeVoice && !_purgedVoice && _elapsed >= 0.3 && VoiceRenderer.Instance is { } purger)
        {
            _purgedVoice = true;
            var shippedDir = ProjectSettings.GlobalizePath(BuiltinCampaign.ShippedDir);
            var shipped = BuiltinCampaign.LoadFrom(shippedDir);
            var shippedIds = shipped is { } s ? Adventure.Voice.VoiceRefs.Ids(s.Campaign, s.Floors) : new HashSet<string>();
            var bankDir = ProjectSettings.GlobalizePath(VoiceRenderer.BuiltinDir);
            GD.Print($"[purge-voice] shipped campaign \"{shipped?.Campaign.Title}\" refers to {shippedIds.Count} lines; {VoiceRenderer.Sweep(bankDir, shippedIds).Describe(bankDir)}");
            GD.Print($"[purge-voice] {purger.SweepCache().Describe(purger.CacheDir)}");
            GD.Print("[purge-voice] RESULT ok");
            GetTree().Quit(0);
        }
        if (_checkVoiceBank && !_checkedVoiceBank && _elapsed >= 0.3 && VoiceRenderer.Instance != null)
        {
            // --check-voice-bank: every voice id the campaign on disk refers to, and whether the shipped bank (or the cache) has it and it loads.
            // Run it on an exported build to prove the bank was packed and is found there.
            _checkedVoiceBank = true;
            var loaded = Adventure.BuiltinCampaign.Load();
            var ids = loaded is { } bank ? Adventure.Voice.VoiceRefs.Ids(bank.Campaign, bank.Floors) : new HashSet<string>(); // the same set the sweep keeps: candidates, taunts and their clean twins, the banter
            var vr = VoiceRenderer.Instance;
            var builtin = ids.Count(VoiceRenderer.HasBuiltin);
            var has = ids.Count(vr.HasAudio);
            var loads = ids.Count(id => vr.Load(id) != null);
            GD.Print($"[voice-bank] campaign \"{loaded?.Campaign.Title}\" ({(Adventure.BuiltinCampaign.IsUserCopy ? "user copy" : "shipped")}, {loaded?.Floors.Count ?? 0} floors) refers to {ids.Count} voice lines: {builtin} in the shipped bank, {has} with audio anywhere, {loads} load");
            GD.Print($"[voice-bank] RESULT {(ids.Count > 0 && loads == ids.Count ? "ok" : loads == 0 ? "FAIL: nothing loads" : "PARTIAL")}");
            GetTree().Quit(ids.Count > 0 && loads > 0 ? 0 : 1);
        }
        if (_listVoiceModels && !_listedVoiceModels && _elapsed >= 0.3)
        {
            _listedVoiceModels = true;
            if (Settings.VoiceViaRunware)
            {
                // Runware's model search cannot filter for speech, so this is the table we know how to voice
                GD.Print("[voice-models] Runware TTS models with mapped voices: " + string.Join("; ", Adventure.Voice.RunwareVoices.KnownModels.Select(m => $"{m.Model} ({m.Label})")));
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
        if (_testVault) RunVaultPuzzleTest();
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
        if (_testHazard != null) RunHazardTest();
        if (_testPortal) RunPortalTest();
        if (_testKeys && !_testedKeys && _elapsed >= 0.5) { _testedKeys = true; RunKeysTest(); }
        if (_rehashDir != null && !_rehashed && _elapsed >= 0.3 && Main.Instance != null) RunRehashPlans();
        if (_testSafeRoom) RunSafeRoomTest();
        if (_testElites) RunElitesAndBossesTest();
        if (_testWriting) RunWritingTest();
        if (_testNarration) RunNarrationTest();
        if (_testItems) RunItemsAndBuildsTest();
        if (_testFortune) RunFortuneTest();
        if (_testRunStructure) RunRunStructureTest();
        if (_testCoOp) RunCoOpTest();
        if (_tremor) RunTremor();
        if (_inspect is { } ins && !_inspected && _elapsed >= 1.2 && Main.Instance?.Builder.Layout != null && Main.Instance.Builder.CurrentFloor == TargetFloor) { _inspected = true; InspectCell(ins.x, ins.y); }
        if (_builtin && _elapsed >= 2.5 && !_builtinChecked) { _builtinChecked = true; var m = Main.Instance!; GD.Print($"[builtin] campaign=\"{Game.Instance.Adventure.Campaign?.Title}\" mode={Game.Instance.Adventure.Mode} floor={m.Builder.CurrentFloor} src={m.Builder.Contents?.Plan?.Source} plans={Game.Instance.Adventure.FloorPlans.Count}"); GD.Print($"[builtin] RESULT {(Game.Instance.Adventure.Mode == AdventureMode.Builtin && m.Builder.Contents?.Plan?.Source == PlanSources.Builtin ? "ok" : "FAIL")}"); }
        if (_testPath) RunPathTest();
        if (_testSkills) RunSkillsTest();
        if (_testTavern) RunTavernTest();
        if (_testAbilities) RunAbilitiesTest();
        if (_testHitFx) RunHitFxTest();
        if (_testCombatFeel) RunCombatFeelTest();
        if (_testVertical) RunVerticalTest();
        if (_testStairs) RunStairsTest();
        if (_testObjective != null) RunObjectiveTest();
        if (_testGasWipe) RunGasWipeTest();
        if (_printMap && !_printedMap && _elapsed >= 1.5 && Main.Instance?.Builder.Layout is { } mapLayout && Main.Instance.Builder.CurrentFloor == TargetFloor)
        {
            _printedMap = true;
            var o = mapLayout.Objective;
            GD.Print($"[map] floor {Main.Instance.Builder.CurrentFloor} {Dungeon.Generation.LayoutKinds.Id(mapLayout.Kind)} objective={Dungeon.Generation.FloorObjectives.Id(o?.Kind ?? Dungeon.Generation.FloorObjectiveKind.None)} up={mapLayout.UpStairs} down={mapLayout.DownStairs} downRoom={mapLayout.Rooms.IndexOf(mapLayout.RoomAt(mapLayout.DownStairs)!)}");
            foreach (var v in mapLayout.Vaults) GD.Print($"[map] vault {v.Index}: {v.Kind} door={v.Door} lever={v.Lever} key={v.Key} revealed={v.Revealed} room={mapLayout.Rooms.IndexOf(v.Room)} holdsStairs={v.Room.Contains(mapLayout.DownStairs)}");
            if (o?.Puzzle is { } pz)
            {
                GD.Print($"[map] puzzle: levers={string.Join(" ", pz.Levers)} gates={string.Join(" ", pz.Gates)} toggles={string.Join(",", pz.Toggles.Select(t => Convert.ToString(t, 2)))} initial={Convert.ToString(pz.Initial, 2)} target={Convert.ToString(pz.Target, 2)}");
                GD.Print($"[map] notice: {Dungeon.Generation.VaultPuzzles.VaultClue(pz, mapLayout)}");
                for (var i = 0; i < pz.LeverCount; i++) GD.Print($"[map] lever {i} at {pz.Levers[i]} (room {mapLayout.Rooms.IndexOf(mapLayout.RoomAt(pz.Levers[i])!)}): {Dungeon.Generation.VaultPuzzles.LeverClue(pz, i)}");
                foreach (var (cell, clue) in o.Plaques) GD.Print($"[map] plaque at {cell} clue={clue}");
            }
            GD.Print(mapLayout.ToAscii());
            GetTree().Quit();
        }
        if (_testParty) RunPartyTest();
        if (_testGold) RunGoldTest();
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
        if (_openShop && !_openedShop && _elapsed >= _after - 0.6 && Main.Instance?.InGame == true)
        {
            _openedShop = true;
            var shopParty = Main.Instance.Party;
            shopParty.AddGold(500);
            shopParty.ShopSoldOnFloor.Add(0);
            Main.Instance.Hud.Shop.Open(Game.Instance.CurrentFloor);
        }
        if (_clearEnemies && Main.Instance?.InGame == true && Main.Instance.Builder.FloorRoot is { } clearRoot)
            foreach (var e in clearRoot.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill();
        if (_pressActions.Count > 0 && _elapsed >= _after - 0.3 && Main.Instance != null)
        {
            foreach (var action in _pressActions)
            {
                if (action == "settings") { Main.Instance.Hud.TogglePause(); Main.Instance.Hud.GetNode<UI.Menus.PauseMenu>("PauseMenu").OpenSettings(); continue; }
                if (action == "inventory-probe")
                {
                    var hud = Main.Instance.Hud;
                    try { hud.ToggleInventory(); GD.Print($"[inv] open={hud.InventoryOpen} members={Main.Instance.Party.Members.Length} bag={Main.Instance.Party.Bag.Items.Count()} line=\"{hud.Inventory.PurseLine}\""); }
                    catch (Exception e) { GD.Print($"[inv] THREW {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
                    continue;
                }
                Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            }
            _pressActions.Clear();
        }

        if (_bakeCastVoiceDir != null && Main.Instance?.InGame == true && Game.Instance.Adventure.Campaign is { } castCampaign && _elapsed >= 2.0)
        {
            // --bake-cast-voice=DIR (with --builtin --ai and the voice flags): every line of the recurring cast, the merchant and every boss line gets
            // audio in DIR, outside the 80-line budget; the plans with their new ids are written to DIR/cast-plans for merging by hand
            var vr = VoiceRenderer.Instance;
            var castPlans = Game.Instance.Adventure.FloorPlans.Where(kv => kv.Key >= 1 && kv.Key <= TierRules.FinalFloor).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (_bakeCastVoiceQueued < 0)
            {
                if (vr == null) { GD.Print("[cast-voice] RESULT FAIL: no voice renderer"); _done = true; GetTree().Quit(1); return; }
                var wanted = new HashSet<Line>(ReferenceEqualityComparer.Instance);
                foreach (var plan in castPlans.Values)
                {
                    foreach (var npc in plan.Npcs.Where(n => n.CastId != null))
                    {
                        foreach (var node in npc.Script.Nodes) wanted.Add(node.Line);
                        foreach (var q in plan.Quests.Where(q => q.GiverNpc == npc.Id)) foreach (var l in new[] { q.Lines.Offer, q.Lines.Accept, q.Lines.Decline, q.Lines.Complete, q.Lines.Fail }) wanted.Add(l);
                    }
                    if (plan.Boss != null) foreach (var l in plan.Boss.AllLines()) wanted.Add(l);
                }
                foreach (var l in castCampaign.Merchant?.AllLines() ?? Enumerable.Empty<Line>()) wanted.Add(l);
                vr.CacheDir = _bakeCastVoiceDir;
                vr.Filter = line => wanted.Contains(line);
                foreach (var plan in castPlans.Values) AdventureDirector.Instance?.EnqueueFloorVoice(plan);
                AdventureDirector.Instance?.EnqueueMerchantVoice(castCampaign);
                vr.Filter = null;
                _bakeCastVoiceQueued = wanted.Count;
                GD.Print($"[cast-voice] {wanted.Count} cast, merchant and boss lines across {castPlans.Count} floors; {vr.Pending} to render into {vr.CacheDir} with {vr.CurrentModel}{(vr.Enabled ? "" : " (voice is off: nothing renders)")}");
            }
            else if (vr != null && (vr.Pending == 0 || vr.StoppedReason != null))
            {
                var ok = vr.Failed == 0 && vr.StoppedReason == null;
                GD.Print($"[cast-voice] rendered {vr.Rendered}, failed {vr.Failed}{(vr.StoppedReason != null ? ", stopped: " + vr.StoppedReason : "")}; per model: {string.Join(", ", vr.RenderedByModel.Select(kv => $"{kv.Key} x{kv.Value}"))}");
                var outDir = Path.Combine(_bakeCastVoiceDir, "cast-plans");
                if (ok) { BuiltinCampaign.Write(outDir, castCampaign, castPlans); GD.Print($"[cast-voice] wrote the plans with their voice ids to {outDir}"); }
                GD.Print($"[cast-voice] RESULT {(ok ? "ok" : "FAIL")}");
                _done = true; GetTree().Quit(ok ? 0 : 1); return;
            }
            else if (vr != null && (int)_elapsed % 15 == 0 && _elapsed - Math.Floor(_elapsed) < 0.02) GD.Print($"[cast-voice] {vr.Pending} pending, {vr.Rendered} done, {vr.Failed} failed");
        }
        TickBakeAllVoice();
        TickDumpEnemies();
        TickJailerTest();
        if (_bakeKillBarksDir != null && Main.Instance?.InGame == true && Game.Instance.Adventure.Campaign is { } killCampaign && _elapsed >= 2.0)
        {
            // --bake-kill-barks=DIR (with --builtin --ai and the voice flags): render every kill line into DIR, then quit
            var vr = VoiceRenderer.Instance;
            if (_bakeKillBarksQueued < 0)
            {
                if (vr == null) { GD.Print("[kill-barks] RESULT FAIL: no voice renderer"); _done = true; GetTree().Quit(1); return; }
                vr.CacheDir = _bakeKillBarksDir; vr.Filter = null;
                _bakeKillBarksQueued = AdventureDirector.Instance.EnqueueKillBarkVoice(killCampaign);
                GD.Print($"[kill-barks] {_bakeKillBarksQueued} bark lines carry ids for {vr.CurrentModel}; {vr.Pending} to render into {vr.CacheDir}{(vr.Enabled ? "" : " (voice is off: nothing renders, ids come from the bank as it stands)")}");
            }
            else if (vr != null && (vr.Pending == 0 || vr.StoppedReason != null))
            {
                var ok = vr.Failed == 0 && vr.StoppedReason == null;
                GD.Print($"[kill-barks] rendered {vr.Rendered}, failed {vr.Failed}{(vr.StoppedReason != null ? ", stopped: " + vr.StoppedReason : "")}; per model: {string.Join(", ", vr.RenderedByModel.Select(kv => $"{kv.Key} x{kv.Value}"))}");
                // the ids ship inside the campaign, so a player's own voice settings never change which file a bark looks for
                var plans = Game.Instance.Adventure.FloorPlans.Where(kv => kv.Key >= 1 && kv.Key <= TierRules.FinalFloor).ToDictionary(kv => kv.Key, kv => kv.Value);
                var dir = ProjectSettings.GlobalizePath(BuiltinCampaign.Dir);
                var missing = killCampaign.Barks.Sum(set => set.Lines.Count(l => l.Line.VoiceId == null || !vr.HasAudio(l.Line.VoiceId)));
                if (ok) { BuiltinCampaign.Write(dir, killCampaign, plans); GD.Print($"[kill-barks] wrote {dir}/campaign.json with {killCampaign.Barks.Count} bark sets; {missing} bark lines still without audio"); }
                GD.Print($"[kill-barks] RESULT {(ok ? "ok" : "FAIL")}");
                _done = true; GetTree().Quit(ok ? 0 : 1); return;
            }
            else if (vr != null && (int)_elapsed % 15 == 0 && _elapsed - Math.Floor(_elapsed) < 0.02) GD.Print($"[kill-barks] {vr.Pending} pending, {vr.Rendered} done, {vr.Failed} failed");
        }
        if (_screenshotPath != null && !_testHorde && _elapsed >= _after && (_menuOnly || Main.Instance?.InGame == true))
        {
            if (_faceNode != null && Main.Instance?.Party?.Adventurers.FirstOrDefault(a => a.State.Name == _faceNode) is { } shot) GD.Print($"[face-node] at the screenshot: {shot.State.Name} {shot.ModelDebug}; lead at {Main.Instance.Party.Local?.GlobalPosition:0.0} facing {-Main.Instance.Party.Local?.GlobalTransform.Basis.Z:0.00}");
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
        var parent = new Dictionary<(GridPos, int), (GridPos, float)>(); // how the probe reached a surface, for reading a hole's trail
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
                    if (seen.Add((n, (int)MathF.Round(nh)))) { parent[(n, (int)MathF.Round(nh))] = (cell, h); frontier.Enqueue((n, nh)); }
                    continue;
                }
                // no walking surface: fine if solid blocks the way at body height (rays that start inside a cube count), a hole if nothing lies below
                var wall = Ground(n, h + 1.6f, h + 0.3f, inside: true);
                if (wall != null) continue;
                var below = Ground(n, h + 0.3f, -12f, inside: true);
                if (below == null)
                {
                    var trail = ""; var key = (cell, (int)MathF.Round(h));
                    for (var k = 0; k < 6 && parent.TryGetValue(key, out var from); k++) { trail += $" <- {from.Item1}@{from.Item2:0.0}{main.Builder.DebugCell(from.Item1)}"; key = (from.Item1, (int)MathF.Round(from.Item2)); }
                    misses.Add($"{cell}@{h:0.0} -> {n} ({layout[n]}/L{layout.LevelOf(n)}{(layout.IsUnder(n) ? "u" : "")}) drops into nothing [{main.Builder.DebugCell(cell)} -> {main.Builder.DebugCell(n)}]{trail}");
                }
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
        var wanted = before == Key.Y ? Key.U : Key.Y; // neither is a default binding, so no other action gets cleared
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

    private bool _hazardMemberKilled, _hazardEnemyKilled;
    private Enemies.Enemy? _hazardEnemy;
    private Vector3 _hazardBody;
    private int _hazardLead = -1;
    private int _hazardSavedSpent;
    private bool _hazardTauntSeen;

    /// <summary>--test-hazard=press|spikes: floor 1 with one forced hazard by the stairs; trigger it, crush an enemy and the lead, rescue, re-arm, jam, save.</summary>
    private void RunHazardTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame) return;
        _hazardClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[hazard] RESULT FAIL: {why}"); _done = true; GetTree().Quit(1); }
        var b = main.Builder;
        // the test owns the only enemy on the floor: ambushes, ledge archers and portals are culled as they appear
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e != _hazardEnemy && e.IsAlive) e.Kill();
        if (_testHazard == "press")
        {
            var press = root.GetChildren().OfType<Dungeon.Props.Press>().FirstOrDefault();
            switch (_hazardStep)
            {
                case 0 when _hazardClock >= 1.0f:
                    _hazardStep++;
                    if (press == null) { Fail($"no Press on the floor (hazards in layout: {layout.Hazards.Count})"); return; }
                    EventBus.Instance.MemberKilledByHazard += (_, _) => _hazardMemberKilled = true;
                    EventBus.Instance.EnemyKilledByHazard += (_, _) => _hazardEnemyKilled = true;
                    {
                        var crushedLines = Adventure.Objectives.TauntBank.All().Where(t => t.Trigger == nameof(Adventure.Objectives.TauntTrigger.Crushed)).SelectMany(t => new[] { t.Line.Text, t.CleanLine?.Text ?? "" }).Where(l => l.Length > 0).ToList();
                        EventBus.Instance.Narration += (text, _) => { if (crushedLines.Any(text.Contains)) _hazardTauntSeen = true; };
                    }
                    foreach (var a in main.Party.Adventurers) a.Stance = Characters.Stance.Hold;
                    foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) e.Kill(); // an empty floor: the only enemy in this test is the one we place in the run
                    foreach (var m in main.Party.Members) m.Effects.Clear();
                    _hazardLead = main.Party.Anchor.Index;
                    GD.Print($"[hazard] press {press.Index} cells=[{string.Join(",", press.Placement.Cells)}] plate={press.Placement.Plate} guards={press.Placement.GuardsCell} phase={press.Phase}");
                    main.Party.Teleport(b.Surface(press.Placement.Plate) + Vector3.Up * 0.3f, b.Surface(press.Placement.Cells[2]) + Vector3.Up * 0.5f);
                    {
                        // the companions wait two cells short of the run; only the lead stands on the plate
                        var dir = press.Placement.Horizontal ? new Dungeon.Generation.GridPos(1, 0) : new Dungeon.Generation.GridPos(0, 1);
                        var safe = press.Placement.Cells[0] - dir - dir;
                        var k = 0;
                        foreach (var a in main.Party.Active.Where(a => a.Index != _hazardLead)) a.GlobalPosition = b.Surface(safe) + Vector3.Up * 0.3f + new Vector3(0.5f * (k++ - 1), 0, 0);
                    }
                    _hazardClock = 0f;
                    break;
                case 1 when press != null && press.Phase == Dungeon.PressPhase.Warning:
                    _hazardStep++;
                    _hazardClock = 0f;
                    _hazardEnemy = b.SpawnEnemy(b.RegularEnemies(b.CurrentFloor)[0], b.Surface(press.Placement.Cells[0]) + Vector3.Up * 0.3f, b.CurrentFloor);
                    GD.Print($"[hazard] warning began; enemy {_hazardEnemy?.Name} placed in the run; lead {main.Party.Members[_hazardLead].Name} on the plate");
                    break;
                case 1 when _hazardClock >= 3f:
                    Fail($"the plate did not trigger (phase {press?.Phase})"); break;
                case 2 when press != null && press.Phase is Dungeon.PressPhase.Closing:
                    // keep the lead in the run: the companions on Hold stay where the teleport put them
                    main.Party.Adventurers[_hazardLead].GlobalPosition = b.Surface(press.Placement.Plate) + Vector3.Up * 0.3f;
                    break;
                case 2 when press != null && press.Phase is Dungeon.PressPhase.Hold:
                {
                    _hazardStep++;
                    _hazardClock = 0f;
                    var lead = main.Party.Adventurers[_hazardLead];
                    var enemyDead = _hazardEnemy != null && !_hazardEnemy.IsAlive;
                    GD.Print($"[hazard] crush: enemy dead={enemyDead} enemyKilledEvent={_hazardEnemyKilled} lead downed={lead.Downed} mangled={lead.State.Mangled} hp={lead.State.Hp} memberKilledEvent={_hazardMemberKilled} spent={press.Spent} body at {DungeonBuilder.WorldToCell(lead.GlobalPosition)} (run {string.Join(",", press.Placement.Cells)})");
                    if (!enemyDead || !_hazardEnemyKilled) { Fail("the enemy in the run was not killed"); return; }
                    if (!lead.Downed || !lead.State.Mangled || !_hazardMemberKilled) { Fail("the lead was not mangled"); return; }
                    if (press.Placement.Cells.Contains(DungeonBuilder.WorldToCell(lead.GlobalPosition))) { Fail("the body was left inside the run"); return; }
                    if (!press.Plate.Detected) { Fail("nobody spotted the plate (perception never reveals a Press plate)"); return; }
                    _hazardBody = lead.GlobalPosition;
                    break;
                }
                case 2 when _hazardClock >= 6f:
                    Fail($"never reached Hold (phase {press?.Phase})"); break;
                case 3 when _hazardClock >= 5f:
                {
                    _hazardStep++;
                    var lead = main.Party.Adventurers[_hazardLead];
                    if (!lead.Downed) { Fail("a mangled member auto-revived"); return; }
                    GD.Print($"[hazard] 5 s later still down (phase {press!.Phase}); waiting for Armed");
                    break;
                }
                case 4 when press != null && press.Phase == Dungeon.PressPhase.Armed:
                {
                    _hazardStep++;
                    _hazardClock = 0f;
                    var lead = main.Party.Adventurers[_hazardLead];
                    // the companions come off Hold; with no enemy near, one of them should walk over and pull
                    foreach (var a in main.Party.Active.Where(a => a.Index != _hazardLead)) a.Stance = Characters.Stance.Defensive;
                    GD.Print($"[hazard] armed again after {_hazardClock:0.0} s; companions set to Defensive, {main.Party.Active.Count(a => !a.Downed)} standing, lead body at {lead.GlobalPosition:0.0}");
                    break;
                }
                case 4 when _hazardClock >= 20f:
                    Fail($"press never re-armed (phase {press?.Phase})"); break;
                case 5 when main.Party.RescueProgress(_hazardLead) > 0f:
                {
                    _hazardStep++;
                    _hazardClock = 0f;
                    var rescuer = main.Party.Active.FirstOrDefault(a => a.RescueTarget?.Index == _hazardLead);
                    GD.Print($"[hazard] {rescuer?.State.Name ?? "a companion"} began the rescue {_hazardClock:0.0} s after the press re-armed");
                    break;
                }
                case 5 when _hazardClock >= 12f:
                    Fail($"no companion came to pull the lead free (actions: {string.Join(", ", main.Party.Active.Where(a => a.Index != _hazardLead).Select(a => $"{a.State.Name}:{a.Stance}:{(a.RescueTarget?.State.Name ?? "-")}"))})"); break;
                case 6 when _hazardClock >= Party.MangledRules.RescueSeconds + 0.5f:
                {
                    _hazardStep++;
                    _hazardClock = 0f;
                    var lead = main.Party.Adventurers[_hazardLead];
                    GD.Print($"[hazard] after rescue: downed={lead.Downed} mangled={lead.State.Mangled} hp={lead.State.Hp} progress={main.Party.RescueProgress(_hazardLead):0.00} crushedTaunt={_hazardTauntSeen}");
                    if (lead.Downed || lead.State.Mangled || lead.State.Hp < 1f) { Fail("rescue did not stand the member up"); return; }
                    if (!_hazardTauntSeen) { Fail("the villain never gloated over the crushing (no Crushed taunt in the narration)"); return; }
                    // jam: a barrel in the run, then trigger again
                    var barrel = GD.Load<PackedScene>("res://scenes/Props/Barrel.tscn").Instantiate<Dungeon.Props.Breakable>();
                    barrel.Name = "JamBarrel";
                    root.AddChild(barrel);
                    barrel.GlobalPosition = b.Surface(press!.Placement.Plate);
                    main.Party.Teleport(b.Surface(press.Placement.Cells[0] + (press.Placement.Horizontal ? new Dungeon.Generation.GridPos(-2, 0) : new Dungeon.Generation.GridPos(0, -2))) + Vector3.Up * 0.3f, b.Surface(press.Placement.Plate));
                    _hazardMemberKilled = false;
                    press.Trigger();
                    break;
                }
                case 7 when press != null && press.Phase is Dungeon.PressPhase.Hold or Dungeon.PressPhase.Retract:
                {
                    _hazardStep++;
                    GD.Print($"[hazard] jam run: jammed={press.Jammed} spent={press.Spent} memberKilled={_hazardMemberKilled}");
                    if (!press.Jammed || _hazardMemberKilled) { Fail("the barrel did not jam the press"); return; }
                    main.SaveGame("hazardtest");
                    _hazardSavedSpent = press.Spent;
                    main.LoadGame("hazardtest");
                    _hazardClock = 0f;
                    break;
                }
                case 7 when _hazardClock >= 8f:
                    Fail($"jam run never closed (phase {press?.Phase})"); break;
                case 8 when _hazardClock >= 1.5f:
                {
                    _hazardStep++;
                    if (press == null) { Fail("no Press after loading the save"); return; }
                    GD.Print($"[hazard] after load: phase={press.Phase} time={press.PhaseTime:0.0} spent={press.Spent} jammed={press.Jammed}");
                    if (press.Spent != _hazardSavedSpent) { Fail($"spent did not survive the save ({press.Spent} vs {_hazardSavedSpent})"); return; }
                    if (!press.Jammed) { Fail("the jam did not survive the save"); return; }
                    if (press.Phase == Dungeon.PressPhase.Armed) { Fail("the press re-armed instead of resuming its phase after the load"); return; }
                    GD.Print("[hazard] RESULT ok");
                    _done = true; GetTree().Quit();
                    break;
                }
            }
        }
        else if (_testHazard == "spikes")
        {
            var bed = root.GetChildren().OfType<Dungeon.Props.SpikeBed>().FirstOrDefault();
            switch (_hazardStep)
            {
                case 0 when _hazardClock >= 1.0f:
                    _hazardStep++;
                    if (bed == null) { Fail($"no SpikeBed on the floor (hazards in layout: {layout.Hazards.Count})"); return; }
                    EventBus.Instance.MemberKilledByHazard += (_, _) => _hazardMemberKilled = true;
                    EventBus.Instance.EnemyKilledByHazard += (_, _) => _hazardEnemyKilled = true;
                    foreach (var a in main.Party.Adventurers) a.Stance = Characters.Stance.Hold;
                    foreach (var m in main.Party.Members) m.Effects.Clear();
                    _hazardLead = main.Party.Anchor.Index;
                    GD.Print($"[hazard] bed {bed.Index} variant={bed.Variant} cells=[{string.Join(",", bed.Cells)}] phase={bed.Phase}");
                    {
                        // companions wait three cells away; the lead steps onto the bed
                        var away = bed.Cells[0] + new Dungeon.Generation.GridPos(0, -3);
                        if (!layout.IsWalkable(away)) away = layout.Rooms.First(r => r.Contains(bed.Cells[0])).Center;
                        main.Party.Teleport(b.Surface(away) + Vector3.Up * 0.3f, b.Surface(bed.Cells[0]));
                        main.Party.Adventurers[_hazardLead].GlobalPosition = b.Surface(bed.Cells[0]) + Vector3.Up * 0.3f;
                    }
                    _hazardClock = 0f;
                    break;
                case 1 when bed != null && bed.Phase == Dungeon.SpikePhase.Click:
                    _hazardStep++; _hazardClock = 0f;
                    GD.Print("[hazard] click");
                    break;
                case 1 when _hazardClock >= 3f:
                    Fail($"the bed did not fire (phase {bed?.Phase})"); break;
                case 2 when bed != null && bed.Phase is Dungeon.SpikePhase.Up or Dungeon.SpikePhase.Retract:
                {
                    _hazardStep++; _hazardClock = 0f;
                    var lead = main.Party.Adventurers[_hazardLead];
                    GD.Print($"[hazard] up after {_hazardClock:0.00}: lead downed={lead.Downed} mangled={lead.State.Mangled} killedEvent={_hazardMemberKilled} body at {DungeonBuilder.WorldToCell(lead.GlobalPosition)} bed [{string.Join(",", bed.Cells)}]");
                    if (!lead.Downed || !lead.State.Mangled || !_hazardMemberKilled) { Fail("the lead was not impaled"); return; }
                    if (bed.Cells.Contains(DungeonBuilder.WorldToCell(lead.GlobalPosition))) { Fail("the body was left on the bed"); return; }
                    break;
                }
                case 2 when _hazardClock >= 3f:
                    Fail($"never rose (phase {bed?.Phase})"); break;
                case 3 when bed != null && bed.Phase == Dungeon.SpikePhase.Down && bed.Armed:
                {
                    _hazardStep++; _hazardClock = 0f;
                    var lead = main.Party.Adventurers[_hazardLead];
                    var rescuer = main.Party.Active.First(a => !a.Downed && a.Index != _hazardLead);
                    rescuer.GlobalPosition = lead.GlobalPosition + new Vector3(1.2f, 0, 0);
                    var started = main.Party.TryRescue(lead, rescuer, () => b.HazardArmed(bed.Index));
                    GD.Print($"[hazard] re-armed; rescue started={started}");
                    if (!started) { Fail("rescue did not start"); return; }
                    break;
                }
                case 3 when _hazardClock >= 10f:
                    Fail($"bed never re-armed (phase {bed?.Phase})"); break;
                case 4 when _hazardClock >= Party.MangledRules.RescueSeconds + 0.5f:
                {
                    _hazardStep++; _hazardClock = 0f;
                    var lead = main.Party.Adventurers[_hazardLead];
                    if (lead.Downed || lead.State.Mangled) { Fail("rescue did not stand the member up"); return; }
                    // an enemy walks onto the bed and dies
                    _hazardEnemy = b.SpawnEnemy(b.RegularEnemies(b.CurrentFloor)[0], b.Surface(bed!.Cells[0]) + Vector3.Up * 0.3f, b.CurrentFloor);
                    bed.Trigger();
                    GD.Print($"[hazard] rescued; enemy {_hazardEnemy?.Name} placed on the bed and the bed triggered");
                    break;
                }
                case 5 when bed != null && bed.Phase is Dungeon.SpikePhase.Retract or Dungeon.SpikePhase.Rearm:
                {
                    _hazardStep++; _hazardClock = 0f;
                    var dead = _hazardEnemy != null && !_hazardEnemy.IsAlive;
                    GD.Print($"[hazard] enemy dead={dead} event={_hazardEnemyKilled}");
                    if (!dead || !_hazardEnemyKilled) { Fail("the enemy on the bed was not killed"); return; }
                    // periodic: the phase follows the server tick
                    bed.ForceVariant(Dungeon.HazardVariant.Periodic);
                    break;
                }
                case 5 when _hazardClock >= 4f:
                    Fail($"second cycle never retracted (phase {bed?.Phase})"); break;
                case 6 when _hazardClock >= 1f:
                {
                    _hazardStep++; _hazardClock = 0f;
                    var expected = Dungeon.HazardRules.PeriodicPhaseAt(b.FloorTick, bed!.Index, out _);
                    GD.Print($"[hazard] periodic: phase={bed.Phase} expected={expected} tick={b.FloorTick}");
                    if (bed.Phase != expected) { Fail("periodic phase does not follow the server tick"); return; }
                    // the dodge: the lead is in the air over the bed when it fires
                    _hazardMemberKilled = false;
                    var lead = main.Party.Adventurers[_hazardLead];
                    lead.GlobalPosition = b.Surface(bed.Cells[0]) + Vector3.Up * 0.3f;
                    break;
                }
                case 7 when bed != null && bed.Phase == Dungeon.SpikePhase.Click:
                {
                    // leave the ground just before the spikes come up and stay up through the rise
                    var lead = main.Party.Adventurers[_hazardLead];
                    lead.GlobalPosition = b.Surface(bed.Cells[0]) + Vector3.Up * 1.4f;
                    lead.Velocity = new Vector3(0, 5f, 0);
                    break;
                }
                case 7 when bed != null && bed.Phase == Dungeon.SpikePhase.Up:
                    _hazardStep++; _hazardClock = 0f;
                    break;
                case 7 when _hazardClock >= 8f:
                    Fail($"periodic cycle never fired (phase {bed?.Phase})"); break;
                case 8 when _hazardClock >= 1.2f:
                {
                    var lead = main.Party.Adventurers[_hazardLead];
                    GD.Print($"[hazard] dodge: killed={_hazardMemberKilled} downed={lead.Downed}");
                    if (_hazardMemberKilled) { Fail("a jumping member was impaled"); return; }
                    main.SaveGame("hazardtest");
                    GD.Print("[hazard] RESULT ok");
                    _done = true; GetTree().Quit();
                    break;
                }
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
                // the seed search predicts an Enter trigger from the tier's default spec, but the live floor may take another layout kind and party, so the
                // plan it gets can say Chest or Alarm instead: pull that trigger the way play would (a chest in the room, the alarm) and test the stream
                if (spawner.Horde.Trigger == Adventure.HordeTrigger.Chest)
                {
                    var chest = root.GetChildren().OfType<Chest>().FirstOrDefault(c => room.Contains(DungeonBuilder.WorldToCell(c.GlobalPosition)) && !c.Opened);
                    if (chest != null)
                    {
                        chest.Interact(main.Party, main.Party.Lead);
                        if (!chest.Opened) EventBus.Instance.RaiseChestOpened(chest.GlobalPosition); // a locked chest and a failed pick: the event is what the horde listens for
                        GD.Print($"[horde] {(chest.Opened ? "opened" : "failed to pick, so raised the chest event at")} the room's chest (locked={chest.Locked}) to pull the trigger");
                    }
                    else { GD.Print("[horde] no chest in the room: raising the chest event at the room's centre"); EventBus.Instance.RaiseChestOpened(main.Builder.Surface(inside)); }
                }
                else if (spawner.Horde.Trigger == Adventure.HordeTrigger.Alarm) { GD.Print("[horde] raising the alarm to pull the trigger"); EventBus.Instance.RaiseAlarmRaised(""); }
                break;
            case 1 when spawner != null && spawner.Triggered:
                _hordeStep++;
                _hordeClock = 0f;
                GD.Print($"[horde] triggered ({spawner.Horde.Trigger})");
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
                GD.Print($"[bake] voice cache dir: {queue.CacheDir}; builtin dir: {ProjectSettings.GlobalizePath(VoiceRenderer.BuiltinDir)}");
                foreach (var p in picks) GD.Print($"[bake]   voice floor {p.Floor,2} {p.Reason} [{p.Line.VoiceId ?? "-"} {(p.Line.VoiceId != null && queue.HasAudio(p.Line.VoiceId) ? "cached" : "render")}]: {p.Line.Text}");
            }
            if (VoiceRenderer.Instance is { Enabled: true } vr && vr.Pending > 0 && _elapsed < 1700) { if ((int)_elapsed % 10 == 0) GD.Print($"[bake] voice: {vr.Pending} lines pending, {vr.Rendered} done, {vr.Failed} failed ({vr.CurrentModel})"); return; }
            var voiceStopped = _bakeVoiceDir != null ? VoiceRenderer.Instance?.StoppedReason : null;
            if (_bakeVoiceDir != null && VoiceRenderer.Instance is { } vrDone) GD.Print($"[bake] voice rendered per model: {string.Join(", ", vrDone.RenderedByModel.Select(kv => $"{kv.Key} x{kv.Value}"))}");
            if (voiceStopped != null) GD.Print($"[bake] VOICE STOPPED: {voiceStopped} ({VoiceRenderer.Instance!.Rendered} lines rendered, {VoiceRenderer.Instance.Failed} failed). The plans are written; run again with --bake-voice once the quota or model is sorted, lines already rendered are kept.");
            BuiltinCampaign.Write(_bakeDir!, game.Adventure.Campaign, plans);
            try { if (Directory.Exists(_bakeDir + ".partial")) Directory.Delete(_bakeDir + ".partial", recursive: true); } catch { }
            // the bank this campaign lives in keeps only the lines it refers to: a rebake must not leave the last story's audio behind
            if (!_keepOldVoice)
            {
                var bankDir = _bakeVoiceDir ?? (Path.GetFullPath(_bakeDir!).TrimEnd('/') == Path.GetFullPath(ProjectSettings.GlobalizePath(BuiltinCampaign.ShippedDir)).TrimEnd('/') ? ProjectSettings.GlobalizePath(VoiceRenderer.BuiltinDir) : null);
                if (bankDir != null) GD.Print($"[bake] {VoiceRenderer.Sweep(bankDir, Adventure.Voice.VoiceRefs.Ids(game.Adventure.Campaign, plans)).Describe(bankDir)}");
                if (VoiceRenderer.Instance is { } cache) GD.Print($"[bake] cache: {cache.SweepCache().Describe(cache.CacheDir)}");
            }
            GD.Print($"[bake] wrote {plans.Count} floors to {_bakeDir}; sources: {string.Join(",", plans.OrderBy(k => k.Key).Select(k => k.Value.Source).Distinct())}; spend ${game.Adventure.Spend.Usd:0.000} over {game.Adventure.Spend.Calls} calls");
            var bakeOk = plans.Count == TierRules.FinalFloor && voiceStopped == null;
            GD.Print($"[bake] RESULT {(bakeOk ? "ok" : voiceStopped != null ? "FAIL (voice stopped)" : "FAIL")}");
            _bakeDir = null;
            GetTree().Quit(bakeOk ? 0 : 1);
            return;
        }
        if (game.Adventure.FloorPlans.ContainsKey(_bakeFloor)) { _bakeFloor++; return; }
        if (Adventure.Ai.ClaudePlanner.StrictReplay && Adventure.Ai.ReplayClaudeClient.Pending is { } pending && !director.IsPlanning(_bakeFloor))
        {
            GD.Print($"[bake] REPLAY NEEDS {pending}: its request is in {ReplayDir}; write {pending}.response.json and run again with --bake-resume");
            _bakeDir = null;
            GetTree().Quit(3);
            return;
        }
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
                var rooms = menu.Rooms.Where(r => Authorable(r) && !r.IsExit && r.FreeCells >= 3).OrderBy(r => r.HopsFromStart).ToList(); // the validator drops an NPC in the safe room, the shrine or the jail
                var itemRoom = menu.Rooms.Where(r => Authorable(r) && r.FreeCells > 2).OrderByDescending(r => r.HopsFromStart).First();
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
                var npcs = root.GetChildren().OfType<Npc>().Where(n => !n.IsMerchant).ToList(); // the safe room's merchant (and his hidden stall) are the builder's, not the plan's
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
                var planNpcStates = floorState?.NpcStates.Where(n => !n.Id.StartsWith("merchant")).ToList() ?? new List<NpcDto>(); // the merchant is saved with the floor too
                GD.Print($"[quest] save: quests={back.Adventure?.Quests.Count} completed={back.Adventure?.Quests.Values.Count(q => q.Status == QuestStatus.Completed)} npcStates={planNpcStates.Count} (+{(floorState?.NpcStates.Count ?? 0) - planNpcStates.Count} merchant) takenItems={floorState?.TakenQuestItems.Count} rescued={planNpcStates.Count(n => n.Rescued)}");
                if (back.Adventure?.Quests.Values.Count(q => q.Status == QuestStatus.Completed) != 4) Fail("save does not hold 4 completed quests");
                if (floorState == null || planNpcStates.Count != 4 || floorState.TakenQuestItems.Count != 1) Fail("floor state missing npc/item records");
                GD.Print($"[quest] RESULT {(_questFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _questFails))}");
                break;
            }
        }
    }

    /// <summary>
    /// A room a hand-authored test plan may post creatures, NPCs, quest items or clues in. The validator drops anything
    /// placed in the start room, a vault, the arena, the jail, the shrine or the safe room, so the scripted flows pick
    /// their rooms from the live layout with the same exclusions the designer uses.
    /// </summary>
    private static bool Authorable(RoomInfo r) => !r.IsStart && !r.IsVault && !r.IsArena && !r.IsJail && !r.IsShrine && !r.IsSafeRoom;

    /// <summary>
    /// Where the party stands to be noticed by <paramref name="e"/> without a fight: a free cell of its room two or three cells
    /// away (in sight, detect range 14 m, out of the opening melee: the companions kill a ghoul in seconds, and a poisoned
    /// runner dies on the way) and off its route to <paramref name="toward"/> (a body on the runner's first cell stops it dead).
    /// </summary>
    private static GridPos StandOff(Enemy e, Collective group, Vector3? toward)
    {
        var layout = Main.Instance.Builder.Layout!;
        var cell = DungeonBuilder.WorldToCell(e.GlobalPosition);
        var posts = group.Alive.Select(m => DungeonBuilder.WorldToCell(m.GlobalPosition)).ToHashSet();
        var route = toward is { } t ? GridPathfinder.FindPath(layout, cell, DungeonBuilder.WorldToCell(t))?.ToHashSet() : null;
        var stand = layout.RoomAt(cell) is { } room
            ? RoomCells.Free(layout, room).Where(c => !posts.Contains(c) && route?.Contains(c) != true && c.ManhattanDistance(cell) is >= 2 and <= 3).OrderBy(c => c.ManhattanDistance(cell)).Cast<GridPos?>().FirstOrDefault()
            : null;
        return stand ?? DungeonBuilder.WalkableNeighbor(layout, cell);
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
                GD.Print($"[coll] rooms: {string.Join(" ", menu.Rooms.Select(r => $"{r.Index}{(r.IsStart ? "S" : "")}{(r.IsExit ? "E" : "")}{(r.IsVault ? "V" : "")}{(r.IsArena ? "A" : "")}{(r.IsJail ? "J" : "")}{(r.IsShrine ? "R" : "")}{(r.IsSafeRoom ? "H" : "")}(free {r.FreeCells}, hops {r.HopsFromStart}, to {string.Join("/", r.Neighbours)})"))}");
                var rooms = menu.Rooms.Where(r => Authorable(r) && r.FreeCells >= 4).OrderByDescending(r => r.HopsFromStart).ToList();
                // the guards and their sentries are the pair of rooms with the shortest walk between them (the deeper one guarded): the runner
                // gives up after 20 s, 30 cells at a sprint, and a straight-line neighbour can be a long way round a sealed room
                int Walk(RoomInfo a, RoomInfo c) => GridPathfinder.FindPath(layout, layout.Rooms[a.Index].Center, layout.Rooms[c.Index].Center)?.Count ?? int.MaxValue;
                // ...leaving a patrol: a room with free cells for its two members and a connected room to walk to (a flooded room with no
                // free cell will do for the walk; on seed 4242 floor 3 only four rooms have any)
                var walkable = menu.Rooms.Where(r => Authorable(r) && r.FreeCells >= 2).OrderByDescending(r => r.HopsFromStart).ToList();
                var anyRoom = menu.Rooms.Where(Authorable).ToList();
                var pairs = (from a in rooms from c in rooms where a.Index != c.Index && a.HopsFromStart >= c.HopsFromStart select (a, c)).OrderBy(p => Walk(p.a, p.c)).ThenByDescending(p => p.a.HopsFromStart).ToList();
                RoomInfo? pa = null, pb = null;
                var (gr, ar) = pairs.First();
                foreach (var pair in pairs)
                {
                    bool Taken(int i) => i == pair.a.Index || i == pair.c.Index;
                    var first = walkable.FirstOrDefault(r => !Taken(r.Index) && r.Neighbours.Any(n => !Taken(n) && anyRoom.Any(x => x.Index == n)));
                    if (first == null) continue;
                    (gr, ar, pa) = (pair.a, pair.c, first);
                    pb = anyRoom.First(x => !Taken(x.Index) && first.Neighbours.Contains(x.Index));
                    break;
                }
                var rest = rooms.Where(r => r.Index != gr.Index && r.Index != ar.Index).ToList();
                var guards = new CollectivePlan { Id = "guards", Name = "Test Guards", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 3 } }, Goal = new GoalPlan { Kind = GoalKind.Guard, Room = gr.Index, RallyRoom = (rest.FirstOrDefault() ?? ar).Index } };
                var walkers = new CollectivePlan { Id = "walkers", Name = "Test Walkers", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 2 } }, Goal = pa != null && pb != null ? new GoalPlan { Kind = GoalKind.Patrol, Room = pa.Index, Route = { pa.Index, pb.Index } } : new GoalPlan { Room = (rest.LastOrDefault() ?? ar).Index } };
                var sentries = new CollectivePlan { Id = "sentries", Name = "Test Sentries", Members = { new MemberPlan { EnemyId = "enemy.ghoul", Count = 2 } }, Goal = new GoalPlan { Kind = GoalKind.Alarm, Room = ar.Index, AlarmTarget = "guards", RallyRoom = gr.Index } };
                plan.Collectives.AddRange(new[] { guards, walkers, sentries });
                var report = PlanValidator.Validate(layout, plan, menu);
                GD.Print($"[coll] authored floor 3: {report} guards@{gr.Index} patrol {pa?.Index}->{pb?.Index} sentries@{ar.Index} (walk {Walk(ar, gr)} cells) {string.Join("; ", report.Repairs)}");
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
                party.Teleport(b.Surface(StandOff(g, guards, null)) + Vector3.Up * 0.3f, g.GlobalPosition);
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
                party.Teleport(b.Surface(StandOff(sentry, _collAlarm, _collAlarm.AlarmTargetPosition)) + Vector3.Up * 0.3f, sentry.GlobalPosition);
                break;
            }
            case 4 when _elapsed >= 15.0:
            {
                _collStep++;
                GD.Print($"[coll] sentries: alert={_collAlarm!.Alert} raised={_collAlarm.AlarmRaised} states=[{States(_collAlarm)}]");
                _collRunner = _collAlarm.Members.FirstOrDefault(m => IsInstanceValid(m) && m.State is AlarmRunState);
                if (_collRunner is { } runner && _collAlarm.AlarmTargetPosition is { } tgt)
                    GD.Print($"[coll] runner: dist to target={runner.GlobalPosition.DistanceTo(tgt):0.0} speed={runner.EffectiveMoveSpeed:0.0} travelled={runner.DistanceTravelled:0.0} {runner.PathDebug}");
                if (!_collAlarm.AlarmRaised && _collRunner == null) Fail("no runner sent");
                break;
            }
            case 5 when _collAlarm!.AlarmRaised || _elapsed >= 36.0: // the runner reports back, or gives up at 20 s and reports anyway
            {
                _collStep++;
                _collAlarmAt = _elapsed;
                var runnerDist = _collRunner != null && IsInstanceValid(_collRunner) && _collAlarm.AlarmTargetPosition is { } tgt2 ? _collRunner.GlobalPosition.DistanceTo(new Vector3(tgt2.X, _collRunner.GlobalPosition.Y, tgt2.Z)) : float.NaN;
                GD.Print($"[coll] guards after alarm (t={_elapsed:0.0}s): alert={_collBig!.Alert} raised={_collAlarm.AlarmRaised} states=[{States(_collBig)}] runner={St(_collRunner)} at {runnerDist:0.0}m from the guards' post");
                if (!_collAlarm.AlarmRaised) Fail("runner never arrived");
                else if (!(runnerDist < 6f)) Fail("runner gave up before reaching the guards"); // a real arrival ends within 2.5 m of the post
                if (_collBig.Alert == AlertLevel.Calm) Fail("guards ignored the alarm");
                var g = _collBig.Alive.FirstOrDefault();
                if (g == null) { Fail("no guards left to regroup"); break; }
                var near = DungeonBuilder.WalkableNeighbor(b.Layout!, DungeonBuilder.WorldToCell(g.GlobalPosition));
                party.Teleport(b.Surface(near) + Vector3.Up * 0.3f, g.GlobalPosition);
                foreach (var m in party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp];
                foreach (var victim in _collBig.Alive.Skip(1).ToList()) victim.Kill();
                break;
            }
            case 6 when _elapsed >= _collAlarmAt + 4.0:
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
                var enemies = root.GetChildren().OfType<Enemy>().Count(e => e.SpawnIndex >= 0 && e.SpawnIndex < DungeonBuilder.ViewerOrcSpawnIndex); // the plan's creatures: not the viewer orc, not fortune's drift-cache ambushers (91000+)
                var extraChests = root.GetChildren().OfType<Dungeon.Props.Chest>().Count(c => c.SpawnIndex >= FloorContents.ExtraIndexBase);
                var extraTraps = root.GetChildren().OfType<Dungeon.Props.Trap>().Count(t => t.SpawnIndex >= FloorContents.ExtraIndexBase && !t.PlacedByParty);
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

    // Objectives: floor 2 with the asked-for objective; the stairs sealed (all but the gas); the objective driven to its end; the stairs open.
    private void RunObjectiveTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout) return;
        void Fail(string why) { GD.Print($"[objective] RESULT FAIL ({why})"); _testObjective = null; }
        var controller = Adventure.Objectives.ObjectiveController.Instance;
        var stairs = root.GetNodeOrNull<Dungeon.Stairs>("DownStairs");
        switch (_objectiveStep)
        {
            case 0 when _elapsed >= 0.5:
                _objectiveStep++;
                EventBus.Instance.ObjectiveEvent += (kind, trigger, arg) => { _objectiveEventSeen = true; GD.Print($"[objective] event {kind} {trigger} {arg}"); };
                EventBus.Instance.NarrationEx += (text, voice, clean, cleanVoice) => { var villain = Game.Instance.Adventure.Campaign?.Antagonist.Name ?? ""; if (villain != "" && text.StartsWith(villain + ":")) { _villainLines++; GD.Print($"[objective] villain says: {text} (clean twin: {(clean == null ? "none" : "yes")})"); } };
                Game.Instance.JumpToFloor(2);
                break;
            case 1 when _elapsed >= 2.0:
            {
                _objectiveStep++;
                var kind = layout.Objective?.Kind ?? Dungeon.Generation.FloorObjectiveKind.None;
                GD.Print($"[objective] floor {Game.Instance.CurrentFloor}: objective={Dungeon.Generation.FloorObjectives.Id(kind)} controller={controller?.GetType().Name ?? "none"} stairsLocked={stairs?.Locked} hud=\"{AdventureDirector.Instance?.CurrentObjective}\"");
                if (!Dungeon.Generation.FloorObjectives.TryParse(_testObjective, out var wanted) || kind != wanted) { Fail($"dealt {kind}, wanted {_testObjective}"); return; }
                if (controller == null || stairs == null) { Fail("no controller or no stairs"); return; }
                if (stairs.Locked != Dungeon.Generation.FloorObjectives.LocksStairs(kind)) { Fail($"stairs locked={stairs.Locked}"); return; }
                if (string.IsNullOrWhiteSpace(AdventureDirector.Instance?.CurrentObjective)) { Fail("the HUD objective line is empty"); return; }
                // what each kind must have built
                switch (controller)
                {
                    case Adventure.Objectives.ToxicController toxic:
                        if (layout.Objective!.ExitGateVault >= 0 && root.GetChildren().OfType<Dungeon.Props.Door>().FirstOrDefault(d => d.VaultIndex == layout.Objective.ExitGateVault) is not { Detected: true }) { Fail("the exit gate is not shown"); return; }
                        _objectiveHp = main.Party.Members.Sum(m => m.Hp);
                        toxic.Hurry();
                        break;
                    case Adventure.Objectives.ShiftingVaultController vault:
                        GD.Print($"[objective] puzzle: {vault.Puzzle?.LeverCount} levers, {vault.Puzzle?.GateCount} gates, plaques={root.GetChildren().OfType<Dungeon.Props.Plaque>().Count()}, gates built={vault.Gates.Count}");
                        if (vault.Puzzle != null && (vault.Gates.Count != vault.Puzzle.GateCount || vault.Levers.Count != vault.Puzzle.LeverCount)) { Fail("gates or levers missing"); return; }
                        if (root.GetChildren().OfType<Dungeon.Props.Plaque>().Count() < 1) { Fail("no plaques"); return; }
                        vault.Hurry();
                        break;
                    case Adventure.Objectives.RitualController ritual:
                        GD.Print($"[objective] ritual: ritualist={ritual.Ritualist?.NpcName} levers={layout.Objective!.BreachLevers.Count} duration={ritual.Duration}");
                        if (ritual.Ritualist == null) { Fail("no ritualist"); return; }
                        if (root.GetChildren().OfType<Dungeon.Props.Lever>().Count(l => l.IsObjectiveLever) != layout.Objective!.BreachLevers.Count) { Fail("breach levers missing"); return; }
                        if (layout.Objective!.BreachLevers.Count > 0) ritual.OnLever(0, true); // seal one breach the honest way
                        ritual.Hurry();
                        break;
                    case Adventure.Objectives.JailbreakController jail:
                        GD.Print($"[objective] jail: prisoners={jail.Prisoners.Count} jailed={jail.Prisoners.Count(p => p.Jailed)} keys={root.GetChildren().OfType<Items.LootDrop>().Count(d => d.KeyIndex >= 0)}");
                        if (jail.Prisoners.Count < 3 || jail.Prisoners.Count(p => p.Jailed) < 1) { Fail("prisoners missing"); return; }
                        jail.Hurry();
                        break;
                }
                break;
            }
            case 2 when _elapsed >= 5.0:
            {
                _objectiveStep++;
                var kind = layout.Objective?.Kind ?? Dungeon.Generation.FloorObjectiveKind.None;
                GD.Print($"[objective] after: completed={controller?.Completed} stairsLocked={stairs?.Locked} hud=\"{AdventureDirector.Instance?.CurrentObjective}\" events={_objectiveEventSeen}");
                if (controller is Adventure.Objectives.ToxicController toxic)
                {
                    var hp = main.Party.Members.Sum(m => m.Hp);
                    GD.Print($"[objective] gas: seconds={toxic.Seconds:0} party hp {_objectiveHp:0} -> {hp:0}");
                    if (hp >= _objectiveHp) { Fail("the gas did no harm"); return; }
                    if (stairs is { Locked: true }) { Fail("the gas floor sealed its stairs"); return; }
                }
                else
                {
                    if (controller is not { Completed: true }) { Fail("not completed"); return; }
                    if (stairs is not { Locked: false }) { Fail("stairs still sealed"); return; }
                    if (controller is Adventure.Objectives.JailbreakController jail && jail.Prisoners.Any(p => !p.Freed || !p.Escorting)) { Fail("a prisoner did not follow"); return; }
                    if (controller is Adventure.Objectives.ShiftingVaultController sv && sv.Puzzle != null && sv.State != sv.Puzzle.Target) { Fail("gates not at the target"); return; }
                }
                if (!_objectiveEventSeen) { Fail("no objective event was raised"); return; }
                if (_villainLines < 1) { Fail("the villain never spoke"); return; }
                GD.Print($"[objective] villain lines: {_villainLines}");
                // a save round trip keeps it
                var dto = Save.FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                GD.Print($"[objective] saved: kind={dto.Objective?.Kind} completed={dto.Objective?.Completed} seconds={dto.Objective?.Seconds:0} parity={dto.Objective?.LeverParity} freed={dto.Objective?.FreedNpcs.Count} sealed={dto.Objective?.SealedMouths.Count}");
                if (dto.Objective == null || dto.Objective.Kind != Dungeon.Generation.FloorObjectives.Id(kind)) { Fail("the objective did not save"); return; }
                GD.Print("[objective] RESULT ok");
                _testObjective = null;
                break;
            }
        }
    }

    // Gas wipe: a dedicated server autosaves the floor every minute, gas clock included, and a wipe brings the floor back from that
    // remembered state with the party at the up stairs. The gas must come back at its grace there, not lethal in the start room.
    private void RunGasWipeTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout) return;
        void Fail(string why) { GD.Print($"[gaswipe] RESULT FAIL ({why})"); _testGasWipe = false; }
        var game = Game.Instance;
        switch (_gasWipeStep)
        {
            case 0 when _elapsed >= 0.5:
                _gasWipeStep++;
                EventBus.Instance.PartyWiped += () => { _gasWipes++; if (_gasWipes == 1) _gasWipeAt = _elapsed; GD.Print($"[gaswipe] wipe {_gasWipes} at {_elapsed:0.0} s"); };
                game.JumpToFloor(2);
                break;
            case 1 when _elapsed >= 2.0:
            {
                _gasWipeStep++;
                if (game.CurrentFloor != 2 || Adventure.Objectives.ObjectiveController.Instance is not Adventure.Objectives.ToxicController toxic) { Fail($"floor {game.CurrentFloor} is not a toxic floor"); return; }
                toxic.Hurry();
                // what the server's autosave does every minute: the floor remembered mid-gas
                game.Floors[2] = Save.FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                GD.Print($"[gaswipe] gas full at {toxic.Seconds:0} s, remembered seconds={game.Floors[2].Objective?.Seconds:0}, party hp {main.Party.Members.Sum(m => m.Hp):0}");
                break;
            }
            case 2 when _gasWipes == 0 && _elapsed >= 50:
                Fail("the gas never wiped the party");
                break;
            case 2 or 3 when _gasWipes >= 2:
                Fail($"wiped again {_elapsed - _gasWipeAt:0.0} s after the reshape: the gas came back lethal");
                break;
            case 2 when _gasWipes >= 1 && _elapsed >= _gasWipeAt + 4.0:
            {
                _gasWipeStep++;
                if (game.CurrentFloor != 2 || Adventure.Objectives.ObjectiveController.Instance is not Adventure.Objectives.ToxicController toxic) { Fail("the floor did not come back toxic"); return; }
                var danger = toxic.DangerAt(DungeonBuilder.WorldToCell(main.Party.Lead.GlobalPosition));
                var down = main.Party.Active.Count(a => a.Downed);
                game.Floors.TryGetValue(2, out var remembered);
                GD.Print($"[gaswipe] after the wipe: gas seconds={toxic.Seconds:0} danger at the stairs={danger} down={down} remembered seconds={remembered?.Objective?.Seconds:0}");
                if (toxic.Seconds >= Adventure.Objectives.GasRules.Grace) { Fail($"the gas clock came back at {toxic.Seconds:0} s"); return; }
                if (danger != 0) { Fail($"the air at the stairs is {Adventure.Objectives.GasRules.Describe(danger)}"); return; }
                if (down > 0) { Fail($"{down} still down after the reshape"); return; }
                break;
            }
            case 3 when _elapsed >= _gasWipeAt + 20.0:
                _gasWipeStep++;
                GD.Print("[gaswipe] RESULT ok");
                _testGasWipe = false;
                break;
        }
    }

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

    // ---- --test-items-and-builds --------------------------------------------------------------------------
    // Loot curve 8 vs 16, stack prices, buyback, a keystone's price, the duo node from two branches, a legendary trigger,
    // Burn spread, Gerald, save round trip. Prints [items] lines and [items] RESULT ok / FAIL (why; why).

    private bool _testItems;
    private int _ibStep;
    private double _ibAt;
    private readonly List<string> _ibFails = new();
    private readonly List<Enemy> _ibSpawned = new();
    private int _ibGoldBefore;
    private int _ibPotionIndex = -1;

    private void RunItemsAndBuildsTest()
    {
        if (_ibStep >= 99) return;
        var main = Main.Instance;
        void Fail(string why) { _ibFails.Add(why); GD.Print($"[items] FAIL: {why}"); }
        void Finish()
        {
            foreach (var e in _ibSpawned) if (IsInstanceValid(e)) e.QueueFree();
            GD.Print(_ibFails.Count == 0 ? "[items] RESULT ok" : $"[items] RESULT FAIL ({string.Join("; ", _ibFails)})");
            _ibStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        if (_elapsed > 40) { Fail($"timed out at step {_ibStep}"); Finish(); return; }
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        var party = main.Party;
        var b = main.Builder;
        var cat = Game.Instance.Catalog;
        var skills = Game.Instance.Skills;
        void Next(double wait = 0.2) { _ibStep++; _ibAt = _elapsed + wait; }
        if (_elapsed < _ibAt) return;
        static bool Near(float a, float b, float tol) => Math.Abs(a - b) <= tol;
        Characters.CharacterState M(string name) => party.Members.First(m => m.Name == name);
        int Index(string name) => Array.FindIndex(party.Members, m => m.Name == name);
        Enemy? Spawn(Vector3 at)
        {
            var e = b.SpawnEnemy(b.RegularEnemies(b.CurrentFloor)[0], at, b.CurrentFloor);
            if (e == null) { Fail("could not spawn an enemy"); return null; }
            e.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Stun, 30f, 0f)); // it stands still for the test
            _ibSpawned.Add(e);
            return e;
        }
        void SureHit(Characters.CharacterState m) { m.SkillModifiers.Add(new Characters.StatModifier(Characters.Stat.Accuracy, Characters.ModOp.Add, 1000f, "test")); m.Recalculate(); }
        bool Learn(Characters.CharacterState m, string branchNode)
        {
            var s = skills.Get($"skill.{m.Class.Id["class.".Length..]}.{branchNode}");
            if (s == null) { Fail($"no skill {branchNode}"); return false; }
            if (Characters.SkillRules.Learn(m, s, skills)) return true;
            Fail($"{m.Name} could not learn {branchNode}: {Characters.SkillRules.WhyCannotLearn(m, s)}");
            return false;
        }
        switch (_ibStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree(); // a quiet floor
                // 1. loot curve: a chest roll at item level 16 scores well above one at 8
                var table = cat.Table("table.chest") with { Rolls = 1 };
                double Avg(int level) { var rng = new Random(16); var s = 0.0; var n = 0; for (var i = 0; i < 600; i++) if (LootRoller.Roll(cat, table, level, rng) is { IsEquippable: true } it) { s += ItemText.Score(it, cat); n++; } return n == 0 ? 0 : s / n; }
                var avg8 = Avg(8); var avg16 = Avg(16);
                GD.Print($"[items] loot curve: avg score level 8 = {avg8:0.0}, level 16 = {avg16:0.0}");
                if (!(avg16 > avg8 * 1.1)) Fail($"level 16 loot ({avg16:0.0}) is not 10% better than level 8 ({avg8:0.0})");
                // 2. stack prices
                var five = cat.Create("item.mana_potion", 5); var one = cat.Create("item.mana_potion");
                GD.Print($"[items] stack price: 5 potions buy {Prices.BuyPrice(five)} (one {Prices.BuyPrice(one)}), sell {Prices.SellPrice(five)} (unit value {Prices.UnitValue(five)})");
                if (Prices.BuyPrice(five) != 5 * Prices.BuyPrice(one)) Fail("a 5-stack does not cost five singles");
                if (Prices.SellPrice(five) != 5 * Prices.UnitValue(five)) Fail("a 5-stack does not sell for five unit values");
                // 3. buyback: stand at the merchant, sell the stack, buy it back
                if (root.GetNodeOrNull<Adventure.Npc>("Npc_merchant") is not { } merchant) { Fail("no merchant on the floor"); Finish(); return; }
                party.Teleport(merchant.GlobalPosition + new Vector3(1.2f, 0f, 0f), merchant.GlobalPosition);
                party.AddGold(1000);
                if (!party.Bag.TryAdd(five)) { Fail("the bag refused the potions"); Finish(); return; }
                _ibPotionIndex = party.Bag.IndexOf(five);
                _ibGoldBefore = party.Gold;
                Next();
                break;
            }
            case 1:
            {
                var five = party.Bag[_ibPotionIndex];
                var worth = five == null ? 0 : Prices.SellPrice(five);
                var refusal = party.ShopSell(DungeonCrawler.Party.BagSlot.Bag(_ibPotionIndex));
                GD.Print($"[items] sell: {refusal ?? "ok"}, gold {_ibGoldBefore} -> {party.Gold} (worth {worth}), ledger {party.Buyback.Entries.Count}");
                if (refusal != null) Fail($"sale refused: {refusal}");
                if (party.Buyback.Entries.Count != 1) Fail($"ledger holds {party.Buyback.Entries.Count} after one sale");
                if (party.Gold != _ibGoldBefore + worth) Fail("the sale did not pay the sell price");
                var back = party.ShopBuyback(0);
                GD.Print($"[items] buyback: {back ?? "ok"}, gold {party.Gold}, mana potions in the bag {party.Bag.Count("item.mana_potion")}, ledger {party.Buyback.Entries.Count}");
                if (back != null) Fail($"buyback refused: {back}");
                if (party.Gold != _ibGoldBefore) Fail($"buyback left {party.Gold} gold, not {_ibGoldBefore}");
                if (party.Bag.Count("item.mana_potion") != 5) Fail("the bag does not hold the 5-stack again");
                if (party.Buyback.Entries.Count != 0) Fail("the ledger still holds the bought-back stack");
                // the shop screen: no buyback cells filled, comparisons in the wares, the arrow on a bag plate
                var plate = cat.Create("item.plate_armor");
                party.Bag.TryAdd(plate);
                var plateIndex = party.Bag.IndexOf(plate);
                var shop = main.Hud.Shop;
                shop.Open(b.CurrentFloor);
                var stock = party.ShopStockFor(b.CurrentFloor);
                var brand = M("Brand");
                var comparable = Enumerable.Range(0, shop.StockCount).Where(i => i < stock.Items.Count && stock.Items[i].IsWeapon && SmartLoot.Usable(cat.Base(stock.Items[i].DefId), brand.Class)).ToList();
                var compared = comparable.Count(i => shop.StockCell(i).TooltipText.Contains("Equipped:"));
                var plateCell = plateIndex >= 0 ? shop.BagCell(plateIndex) : null;
                GD.Print($"[items] shop: buyback cells {shop.BuybackCount}, wares Brand could wield {comparable.Count}, of them compared {compared}; plate upgrade={plateCell?.Upgrade} glyphs={(plateCell == null ? "" : string.Join(" ", plateCell.Glyphs.Select(g => g.Glyph + (g.Usable ? "+" : "-"))))}; gold label \"{shop.GoldText.Replace("\n", " / ")}\"");
                if (shop.BuybackCount != 0) Fail("the shop shows a buyback entry after the buyback");
                if (brand.Equipment[EquipSlot.Weapon] == null) Fail("Brand wears no weapon to compare against");
                else if (comparable.Count > 0 && compared != comparable.Count) Fail("a wielded-kind stock weapon's tooltip has no Equipped: comparison");
                if (plateCell is not { Upgrade: true }) Fail("the plate in the bag is not marked as an upgrade for Brand");
                if (plateCell != null && !(plateCell.Glyphs.Any(g => g.Glyph == "Wa" && g.Usable) && plateCell.Glyphs.Any(g => g.Glyph == "Ma" && !g.Usable))) Fail("the plate's glyph row does not read Wa usable, Ma not");
                if (!shop.GoldText.Contains("Focused:")) Fail("the gold label has no Focused line");
                shop.Close();
                if (plateIndex >= 0) party.Bag.RemoveAt(plateIndex);
                Next();
                break;
            }
            case 2:
            {
                // 4. a keystone's price: Perfect Form trades 20% max HP for attack speed; Red Mist trades the guard for damage
                var sable = M("Sable"); sable.SkillPoints = 20;
                foreach (var n in new[] { "duelist.footwork", "duelist.flurry", "duelist.venom_mastery", "duelist.parry", "duelist.cutthroat", "duelist.dance_of_blades" }) Learn(sable, n);
                var hpBefore = sable.Stats[Characters.Stat.MaxHp]; var asBefore = sable.Stats[Characters.Stat.AttackSpeed];
                Learn(sable, "duelist.perfect_form");
                var hpAfter = sable.Stats[Characters.Stat.MaxHp]; var asAfter = sable.Stats[Characters.Stat.AttackSpeed];
                GD.Print($"[items] Perfect Form: max HP {hpBefore:0} -> {hpAfter:0} (want x0.8), attack speed {asBefore:0.00} -> {asAfter:0.00} (want x1.2)");
                if (!Near(hpAfter, hpBefore * 0.8f, 1f)) Fail("Perfect Form did not take 20% max HP");
                if (!Near(asAfter, asBefore * 1.2f, 0.01f)) Fail("Perfect Form did not give 20% attack speed");
                var brand = M("Brand"); brand.SkillPoints = 20;
                foreach (var n in new[] { "berserker.brawn", "berserker.cleave", "berserker.heavy_hands", "berserker.bloodlust", "berserker.frenzy", "berserker.executioner" }) Learn(brand, n);
                var guardBefore = party.CanGuard(brand); var dmgBefore = brand.Stats[Characters.Stat.MeleeDamage];
                Learn(brand, "berserker.rampage");
                var dmgAfter = brand.Stats[Characters.Stat.MeleeDamage];
                GD.Print($"[items] Red Mist: can guard {guardBefore} -> {party.CanGuard(brand)}, melee damage {dmgBefore:0.0} -> {dmgAfter:0.0} (want x1.3)");
                if (!guardBefore || party.CanGuard(brand)) Fail("Red Mist did not take the guard away");
                if (!Near(dmgAfter, dmgBefore * 1.3f, 0.05f)) Fail("Red Mist did not give 30% melee damage");
                // 5. the duo node, from either branch
                var ilex = M("Ilex"); ilex.SkillPoints = 20;
                var attunement = skills.Get("skill.mage.duo.attunement");
                if (attunement == null) { Fail("no Attunement"); Finish(); return; }
                var why = Characters.SkillRules.WhyCannotLearn(ilex, attunement);
                foreach (var n in new[] { "fire.kindling", "fire.ember_mind", "fire.meteor" }) Learn(ilex, n);
                var viaFire = Characters.SkillRules.Learn(ilex, attunement, skills);
                var siphon = party.Trigger(ilex, Characters.TriggerKind.SoulSiphon);
                var refund = Characters.SkillRules.Respec(ilex, skills);
                foreach (var n in new[] { "warding.mana_well", "warding.focus", "warding.arcane_barrier" }) Learn(ilex, n);
                var viaWarding = Characters.SkillRules.Learn(ilex, attunement, skills);
                GD.Print($"[items] Attunement: before any branch \"{why}\"; via Fire {viaFire} (SoulSiphon {siphon}); respec refunded {refund}; via Warding {viaWarding}; Ilex learned {string.Join(", ", ilex.LearnedSkills.Select(s => s.Split('.').Last()))}");
                if (why == null || !why.Contains("one of its branches")) Fail($"Attunement was refused with \"{why}\" instead of the branch rule");
                if (!viaFire || siphon != 4f) Fail("Attunement through Fire did not grant SoulSiphon 4");
                if (refund != 4) Fail($"respec refunded {refund}, not 4");
                if (!viaWarding) Fail("Attunement could not be learned through Warding after a respec");
                Next();
                break;
            }
            case 3:
            {
                // 6. legendary triggers fire: Lantern-Jaw heals on a kill
                var brand = M("Brand"); var brandIdx = Index("Brand"); SureHit(brand);
                var axe = LootRoller.MakeLegendary(cat, cat.Legendaries["leg.lantern_jaw"], 8, new Random(1));
                brand.Equipment.Equip(EquipSlot.Weapon, axe); brand.Recalculate();
                var fromSkills = Characters.SkillRules.TriggerMagnitude(brand, skills, Characters.TriggerKind.Bloodlust); // the Berserker chain's Bloodlust node (8) adds to the axe's 8
                var bloodlust = party.Trigger(brand, Characters.TriggerKind.Bloodlust);
                GD.Print($"[items] Lantern-Jaw: Bloodlust {bloodlust} (want {fromSkills} from skills + 8 from the axe), max damage {axe.Weapon!.MaxDamage:0.00} (want {12 * 1.24f:0.00}: a level-2 axe at item level 8)");
                if (bloodlust != fromSkills + 8f) Fail($"Bloodlust reads {bloodlust}, not {fromSkills + 8f}");
                if (!Near(axe.Weapon.MaxDamage, 12 * 1.24f, 0.05f)) Fail("the axe's damage does not scale with its item level");
                var body = party.Adventurers[brandIdx];
                var fwd = (-body.GlobalTransform.Basis.Z) with { Y = 0 }; fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
                var enemy = Spawn(body.GlobalPosition - Vector3.Up * 0.2f + fwd * 2f);
                if (enemy == null) { Finish(); return; }
                brand.Hp = brand.Stats[Characters.Stat.MaxHp] - 30;
                var hpBefore = brand.Hp;
                var r = enemy.ReceiveAttack(brand.Stats, new Combat.AttackProfile { MinDamage = 9999, MaxDamage = 9999 }, new Combat.HitContext { DefenderStunned = true }, party.Rng, brand.Name);
                party.OnHitLanded(brand, enemy, r);
                GD.Print($"[items] kill: hit={r.Hit} enemy alive={enemy.IsAlive}, Brand HP {hpBefore:0.0} -> {brand.Hp:0.0} (want +{bloodlust})");
                if (!r.Hit || enemy.IsAlive) Fail("the test blow did not kill the enemy");
                if (!Near(brand.Hp, hpBefore + bloodlust, 0.5f)) Fail($"the kill did not heal {bloodlust} (skills + Lantern-Jaw)");
                // Burn spread: the Candle on Ilex, two enemies a metre apart
                var ilex = M("Ilex"); var ilexIdx = Index("Ilex"); SureHit(ilex);
                var candle = LootRoller.MakeLegendary(cat, cat.Legendaries["leg.candle_of_the_congregation"], 8, new Random(2));
                ilex.Equipment.Equip(EquipSlot.Offhand, candle); ilex.Recalculate();
                var ilexBody = party.Adventurers[ilexIdx];
                var ifwd = (-ilexBody.GlobalTransform.Basis.Z) with { Y = 0 }; ifwd = ifwd.LengthSquared() > 1e-4f ? ifwd.Normalized() : Vector3.Forward;
                var side = new Vector3(-ifwd.Z, 0, ifwd.X);
                var e1 = Spawn(ilexBody.GlobalPosition - Vector3.Up * 0.2f + ifwd * 3f);
                var e2 = Spawn(ilexBody.GlobalPosition - Vector3.Up * 0.2f + ifwd * 3f + side * 1f);
                if (e1 == null || e2 == null) { Finish(); return; }
                var burn = new Combat.AttackProfile { MinDamage = 1, MaxDamage = 1, OnHit = new[] { new Combat.StatusApplication(Combat.StatusKind.Burn, 4, 2, 1f) } };
                var r1 = e1.ReceiveAttack(ilex.Stats, burn, new Combat.HitContext { DefenderStunned = true }, party.Rng, ilex.Name);
                party.OnHitLanded(ilex, e1, r1);
                GD.Print($"[items] Candle: BurnSpread {party.Trigger(ilex, Characters.TriggerKind.BurnSpread)}, hit={r1.Hit} applied=[{string.Join(",", r1.Applied.Select(a => a.Kind))}], first burning={e1.Effects.Has(Combat.StatusKind.Burn)}, neighbour burning={e2.Effects.Has(Combat.StatusKind.Burn)} at {e1.GlobalPosition.DistanceTo(e2.GlobalPosition):0.0} m");
                if (!e2.Effects.Has(Combat.StatusKind.Burn)) Fail("the Candle did not spread the Burn to the neighbour");
                // Gerald: below 30% HP, once per floor, frost bursts from Wren
                var wren = M("Wren"); var wrenIdx = Index("Wren"); SureHit(wren);
                var gerald = LootRoller.MakeLegendary(cat, cat.Legendaries["leg.gerald"], 8, new Random(3));
                wren.Equipment.Equip(EquipSlot.Amulet, gerald); wren.Recalculate();
                var wrenBody = party.Adventurers[wrenIdx];
                var wfwd = (-wrenBody.GlobalTransform.Basis.Z) with { Y = 0 }; wfwd = wfwd.LengthSquared() > 1e-4f ? wfwd.Normalized() : Vector3.Forward;
                var e3 = Spawn(wrenBody.GlobalPosition - Vector3.Up * 0.2f + wfwd * 2f);
                if (e3 == null) { Finish(); return; }
                wren.Hp = 0.2f * wren.Stats[Characters.Stat.MaxHp];
                EventBus.Instance.RaiseMemberDamaged(wrenIdx, 1f);
                var frosted = e3.Effects.Has(Combat.StatusKind.Frost);
                e3.Effects.Remove(Combat.StatusKind.Frost);
                EventBus.Instance.RaiseMemberDamaged(wrenIdx, 1f);
                var again = e3.Effects.Has(Combat.StatusKind.Frost);
                GD.Print($"[items] Gerald: LastGasp {party.Trigger(wren, Characters.TriggerKind.LastGasp)}, Wren at {wren.Hp:0}/{wren.Stats[Characters.Stat.MaxHp]:0} HP: frost on the enemy {frosted}, after a second drop {again} (want once per floor)");
                if (!frosted) Fail("Gerald did not burst frost below 30% HP");
                if (again) Fail("Gerald fired twice on one floor");
                wren.Hp = wren.Stats[Characters.Stat.MaxHp];
                foreach (var e in _ibSpawned) if (IsInstanceValid(e)) e.QueueFree();
                _ibSpawned.Clear();
                // 7. save and load
                main.SaveGame("items");
                if (!main.LoadGame("items")) { Fail("the save did not load"); Finish(); return; }
                Next(1.5);
                break;
            }
            case 4:
            {
                var loaded = main.Party;
                if (loaded.Members.Length < 4) { Fail("the loaded party is short"); Finish(); return; }
                var brand = loaded.Members[0]; var sable = loaded.Members[1];
                var bloodlust = loaded.Trigger(brand, Characters.TriggerKind.Bloodlust);
                var fromSkills = Characters.SkillRules.TriggerMagnitude(brand, skills, Characters.TriggerKind.Bloodlust);
                var fromAxe = LegendaryRules.Trigger(brand.Equipment, cat, Characters.TriggerKind.Bloodlust);
                GD.Print($"[items] after load: Brand wields {brand.Equipment[EquipSlot.Weapon]?.DisplayName ?? "nothing"} (Bloodlust {bloodlust} = {fromSkills} skills + {fromAxe} axe, max damage {brand.WeaponProfile.MaxDamage:0.00}); Sable knows {sable.LearnedSkills.Count} skills (Perfect Form {sable.LearnedSkills.Contains("skill.rogue.duelist.perfect_form")}); ledger {loaded.Buyback.Entries.Count}");
                if (fromAxe != 8f || bloodlust != fromSkills + 8f) Fail($"Bloodlust after the load reads {bloodlust} ({fromAxe} from the axe), not {fromSkills + 8f}");
                if (!Near(brand.WeaponProfile.MaxDamage, 14.88f, 0.05f)) Fail("the axe lost its item-level scaling in the save");
                if (!sable.LearnedSkills.Contains("skill.rogue.duelist.perfect_form")) Fail("Sable lost Perfect Form in the save");
                if (loaded.Buyback.Entries.Count != 0) Fail("the ledger came back with entries");
                Finish();
                break;
            }
        }
    }

    // ---- --test-safe-room ---------------------------------------------------------------------------------
    // The floor's safe room end to end: the room and its door, the merchant standing in it and his three-choice talk, the door opening for the party and shut against an
    // enemy, x6 regeneration inside, the HUD line, the gathering (or none with --no-gathering), the gatherers going for
    // the party when it steps out and back to their post when it is gone again, the open door holding a creature out
    // while a member stands in it, the gathered count through a save, and (with --objective=toxic) clean air inside.
    private bool _testSafeRoom;
    private int _srStep;
    private double _srAt;
    private readonly List<string> _srFails = new();
    private Door? _srDoor;
    private Enemy? _srEnemy;
    private int _srMember;
    private float _srHp;
    private double _srOccupiedAt;
    private int _srSavedGathered;
    private readonly List<Enemy> _srGatherers = new();
    private readonly List<Party.Adventurer> _srFrozen = new();
    private bool _srBreached;

    /// <summary>Walking distances (cells) from the safe room's door to the cells outside it, up to <paramref name="max"/>.</summary>
    private static Dictionary<GridPos, int> SafeRoomOutside(FloorLayout layout, int max)
    {
        var room = layout.Rooms[layout.SafeRoom];
        var door = layout.SafeRoomDoor;
        var distance = new Dictionary<GridPos, int> { [door] = 0 };
        var queue = new Queue<GridPos>();
        queue.Enqueue(door);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (distance[p] >= max) continue;
            foreach (var n in p.Neighbours4())
            {
                if (room.Contains(n) || distance.ContainsKey(n) || !layout.CanStep(p, n)) continue;
                distance[n] = distance[p] + 1;
                queue.Enqueue(n);
            }
        }
        return distance;
    }

    private void RunSafeRoomTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        if (_startFloor > 1 && main.Builder.CurrentFloor != _startFloor) return;
        var party = main.Party;
        var b = main.Builder;
        var since = _elapsed - _srAt;
        void Next() { _srStep++; _srAt = _elapsed; }
        void Go(int step) { _srStep = step; _srAt = _elapsed; }
        void Fail(string why) { _srFails.Add(why); GD.Print($"[saferoom] FAIL: {why}"); }
        void Finish()
        {
            GD.Print(_srFails.Count == 0 ? "[saferoom] RESULT ok" : $"[saferoom] RESULT FAIL ({string.Join("; ", _srFails)})");
            _srStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        Vector3 Stand(GridPos cell) => b.Surface(cell) + Vector3.Up * 0.3f;
        GridPos? Outside(int atLeast) => layout.SafeRoom < 0 ? null : SafeRoomOutside(layout, 12).Where(kv => kv.Value >= atLeast).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).Select(kv => (GridPos?)kv.Key).FirstOrDefault();
        var controller = SafeRoomController.Instance;
        switch (_srStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                // 1. the room and its door
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree(); // a quiet floor: only the test's creatures move
                if (layout.SafeRoom < 0) { Fail($"floor {b.CurrentFloor} has no safe room"); Finish(); return; }
                var gather = SafeRoomRules.GatherCell(layout);
                _srDoor = root.GetChildren().OfType<Door>().FirstOrDefault(d => d.Kind == VaultKind.SafeDoor);
                GD.Print($"[saferoom] floor {b.CurrentFloor} seed {Game.Instance.GameSeed}: room {layout.SafeRoom} ({layout.Rooms[layout.SafeRoom].Cells().Count()} cells) door {layout.SafeRoomDoor} merchant {SafeRoomRules.MerchantCell(layout)} gather {gather?.ToString() ?? "none"}");
                // the merchant stands on a safe-room cell
                var merchantNpc = root.GetChildren().OfType<Adventure.Npc>().FirstOrDefault(n => n.IsMerchant);
                var merchantAt = merchantNpc == null ? (GridPos?)null : DungeonBuilder.WorldToCell(merchantNpc.GlobalPosition);
                var merchantIn = merchantAt is { } mc && layout.IsInSafeRoom(mc);
                if (merchantIn) GD.Print($"[saferoom] merchant: present at {merchantAt} (\"{merchantNpc!.NpcName}\", prompt \"{merchantNpc.Prompt}\", lines {(merchantNpc.MerchantLines != null ? "yes" : "none")})");
                else { GD.Print($"[saferoom] merchant: missing at {SafeRoomRules.MerchantCell(layout)}{(merchantAt is { } wrong ? $" (one stands at {wrong}, outside the room)" : "")}"); Fail("no merchant in the safe room"); }
                if (merchantIn && merchantNpc!.MerchantLines == null) Fail("the merchant has no lines for this floor");
                if (merchantIn && merchantNpc!.MerchantLines is { } ml)
                {
                    // his talk: the trade line, exactly Trade / How is it going? / Leave; the second shows his progress line, Trade closes the talk
                    var talk = main.Hud.Dialogue;
                    main.Hud.OpenMerchant(merchantNpc);
                    var offered = string.Join(" / ", talk.Choices.Select(c => c.Text));
                    var opening = talk.Node?.Line.Text;
                    talk.Choose(1);
                    var progress = talk.Node?.Line.Text;
                    var again = string.Join(" / ", talk.Choices.Select(c => c.Text));
                    // the dialogue closing on Trade must not release the input (the mouse recaptured, the cursor warping) before the shop shows
                    bool? lockedAsTalkClosed = null;
                    void OnTalkClosed() => lockedAsTalkClosed = main.Party.InputLocked;
                    talk.Closed += OnTalkClosed;
                    talk.Choose(0);
                    talk.Closed -= OnTalkClosed;
                    var shopOpened = main.Hud.ShopOpen;
                    var keeper = main.Hud.Shop.Keeper == merchantNpc;
                    main.Hud.Shop.Close();
                    GD.Print($"[saferoom] merchant talk: \"{opening}\" [{offered}]; how is it going: \"{progress}\" [{again}]; after Trade open={talk.Visible}, shop open={shopOpened}, input locked as the talk closed={lockedAsTalkClosed}, shop keeper={keeper}");
                    if (!shopOpened) Fail("Trade did not open the shop");
                    if (lockedAsTalkClosed != true) Fail("Trade released the input between the talk and the shop (the cursor warps)");
                    if (!keeper) Fail("the shop does not know the merchant it was opened from (he would stop talking)");
                    if (offered != "Trade / How is it going? / Leave" || again != offered) Fail($"merchant choices are [{offered}] then [{again}]");
                    if (opening != ml.Trade.Text || progress != ml.Progress.Text) Fail("merchant lines are not his trade and progress lines");
                    if (talk.Visible) { Fail("Trade left the dialogue open"); talk.Close(); }
                }
                if (_srDoor == null) { Fail("no SafeDoor on the floor"); Finish(); return; }
                GD.Print($"[saferoom] door at {_srDoor.Cell} open={_srDoor.IsOpen} prompt=\"{_srDoor.Prompt}\"");
                if (_srDoor.Cell != layout.SafeRoomDoor) Fail($"the door stands at {_srDoor.Cell}, the layout says {layout.SafeRoomDoor}");
                if (_srDoor.Prompt != "") Fail("the safe door has a prompt");
                if (_srDoor.IsOpen) Fail("the safe door starts open");
                if (gather == null) { Fail("no gather cell"); Finish(); return; }
                if (controller == null) Fail("no SafeRoomController");
                // 2. walk up to the door: stand 2 m from it on the corridor side
                var near = Outside(1)!.Value;
                var toward = (b.Surface(near) - _srDoor.GlobalPosition) with { Y = 0 };
                party.Teleport(_srDoor.GlobalPosition with { Y = Stand(near).Y } + toward.Normalized() * 2f, _srDoor.GlobalPosition);
                Next();
                break;
            }
            case 1:
                if (_srDoor!.IsOpen) { GD.Print($"[saferoom] door opened for the party after {since:0.00} s"); var away = Outside(2)!.Value; party.Teleport(Stand(away), _srDoor.GlobalPosition); GD.Print($"[saferoom] party moved {b.Surface(away).DistanceTo(_srDoor.GlobalPosition):0.0} m off"); Next(); }
                else if (since > 0.5) { Fail("the door did not open for the party within 0.5 s"); _srStep = 2; _srAt = _elapsed; }
                break;
            case 2 when since >= SafeRoomRules.DoorCloseDelay + 0.5:
            {
                GD.Print($"[saferoom] {SafeRoomRules.DoorCloseDelay + 0.5:0.0} s after the party left: door open={_srDoor!.IsOpen}");
                if (_srDoor.IsOpen) Fail("the door did not close behind the party");
                // 3. a creature at the door, awake and set on the party, which rests deep in the room beyond the door's reach
                // (out in a corridor the companions cut it down before it could try the door)
                var outside = Outside(1)!.Value;
                var merchant = SafeRoomRules.MerchantCell(layout);
                party.Teleport(Stand(merchant), _srDoor.GlobalPosition);
                _srOccupiedAt = _elapsed; // the gathering clock starts with this step inside
                var regulars = b.RegularEnemies(b.CurrentFloor);
                var meleeId = regulars.FirstOrDefault(id => b.EnemyProfile(id)?.Kind == EnemyKind.Melee) ?? regulars[0]; // a melee one walks right up to the door
                _srEnemy = b.SpawnEnemy(meleeId, Stand(outside), b.CurrentFloor);
                if (_srEnemy == null) { Fail("could not spawn the test enemy"); Finish(); return; }
                GD.Print($"[saferoom] {_srEnemy.DisplayName} spawned at {outside}; party at {merchant}, {b.Surface(merchant).DistanceTo(_srDoor.GlobalPosition):0.0} m behind the door");
                Next();
                break;
            }
            case 3 when since >= 0.1:
                _srEnemy!.ForceAggro(); // after a physics frame
                Next();
                break;
            case 4 when since >= 3.0:
            {
                var cell = DungeonBuilder.WorldToCell(_srEnemy!.GlobalPosition);
                GD.Print($"[saferoom] after 3 s the enemy ({_srEnemy.State.Name}, alive={_srEnemy.IsAlive}) stands at {cell}, {_srEnemy.GlobalPosition.DistanceTo(_srDoor!.GlobalPosition):0.0} m from the door, in room={layout.IsInSafeRoom(cell)}; door open={_srDoor.IsOpen}");
                if (!_srEnemy.IsAlive) Fail("the test enemy died before it could try the door");
                // the door must be shut because it held, not because the enemy never came: it is hunting and pressed against it
                var atDoor = _srEnemy.GlobalPosition.DistanceTo(_srDoor.GlobalPosition);
                if (_srEnemy.State.Name is not ("Chase" or "Attack" or "Shoot")) Fail($"the test enemy is in {_srEnemy.State.Name}, not hunting the party");
                if (atDoor > SafeRoomRules.DoorOpenRadius + 0.5f) Fail($"the test enemy stands {atDoor:0.0} m from the door, never came at it (want <= {SafeRoomRules.DoorOpenRadius + 0.5f:0.0})");
                else GD.Print($"[saferoom] the enemy is at the door ({atDoor:0.0} m <= {SafeRoomRules.DoorOpenRadius + 0.5f:0.0}) and hunting ({_srEnemy.State.Name}); the door held");
                if (_srDoor.IsOpen) Fail("the door opened for an enemy");
                if (layout.IsInSafeRoom(cell) || cell == layout.SafeRoomDoor) Fail("the enemy got into the safe room");
                if (!SafeRoomRules.Cells(layout).Append(layout.SafeRoomDoor).All(b.IsCellBlockedForEnemies)) Fail("some safe room cell is open to enemies");
                _srEnemy.QueueFree();
                // 4. on the merchant's cell (where the party already rests)
                party.Teleport(Stand(SafeRoomRules.MerchantCell(layout)), _srDoor.GlobalPosition);
                Next();
                break;
            }
            case 5:
            {
                var hud = main.Hud;
                if (controller is { Occupied: true } && hud.SafeRoom && hud.ObjectiveText.EndsWith("Safe room"))
                {
                    GD.Print($"[saferoom] inside after {since:0.00} s: occupied, HUD \"{hud.ObjectiveText}\"");
                    _srMember = party.Anchor.Index;
                    party.Members[_srMember].Hp = 10f;
                    _srHp = 10f;
                    Next();
                }
                else if (since > 1.0) { Fail($"inside: occupied={controller?.Occupied} hud.SafeRoom={hud.SafeRoom} line=\"{hud.ObjectiveText}\""); Finish(); }
                break;
            }
            case 6 when since >= 3.0:
            {
                var m = party.Members[_srMember];
                var gain = m.Hp - _srHp;
                var rate = m.Stats[Characters.Stat.HpRegen];
                var want = 5f * rate * 3f;
                GD.Print($"[saferoom] {m.Name} regained {gain:0.00} hp in 3 s inside (base {rate:0.000}/s, x{SafeRoomRules.RegenMultiplier} = {SafeRoomRules.RegenMultiplier * rate * 3f:0.00}, need >= {want:0.00}); inside={controller?.IsInside(_srMember)}");
                if (gain < want) Fail($"regeneration inside is {gain / (rate * 3f):0.0}x, want about {SafeRoomRules.RegenMultiplier}x");
                Next();
                break;
            }
            case 7 when _elapsed >= _srOccupiedAt + SafeRoomRules.GraceSeconds + 1:
            {
                // 5. the creatures shut out gather after the grace
                var gather = SafeRoomRules.GatherCell(layout)!.Value;
                _srGatherers.Clear();
                _srGatherers.AddRange(root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsQueuedForDeletion() && DungeonBuilder.WorldToCell(e.GlobalPosition).ManhattanDistance(gather) <= 2));
                var gathered = controller?.Gathered ?? -1;
                GD.Print($"[saferoom] after the grace ({SafeRoomRules.GraceSeconds:0} s + 1): Gathered={gathered}, by the gather cell: [{string.Join(", ", _srGatherers.Select(e => $"{e.DisplayName}:{e.State.Name}"))}]");
                if (NoGathering)
                {
                    if (gathered != 0 || _srGatherers.Count > 0) Fail("--no-gathering still gathered");
                }
                else
                {
                    if (gathered != 1) Fail($"Gathered={gathered} after the grace, want 1");
                    if (!_srGatherers.Any(e => e.State.Name == "Hold")) Fail("no creature holds by the gather cell");
                }
                if (root.GetChildren().OfType<Enemy>().Any(e => e.IsAlive && layout.IsInSafeRoom(DungeonBuilder.WorldToCell(e.GlobalPosition)))) Fail("a creature is inside the safe room");
                Next();
                break;
            }
            case 8 when _elapsed >= _srOccupiedAt + SafeRoomRules.GraceSeconds + SafeRoomRules.IntervalSeconds + 1:
            {
                var gathered = controller?.Gathered ?? -1;
                var gather = SafeRoomRules.GatherCell(layout)!.Value;
                _srGatherers.Clear();
                _srGatherers.AddRange(root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsQueuedForDeletion() && DungeonBuilder.WorldToCell(e.GlobalPosition).ManhattanDistance(gather) <= 3));
                GD.Print($"[saferoom] after another {SafeRoomRules.IntervalSeconds:0} s: Gathered={gathered}, {_srGatherers.Count} creature(s) outside: [{string.Join(", ", _srGatherers.Select(e => $"{e.DisplayName}:{e.State.Name}"))}]");
                if (gathered != (NoGathering ? 0 : 2)) Fail($"Gathered={gathered}, want {(NoGathering ? 0 : 2)}");
                if (root.GetChildren().OfType<Enemy>().Any(e => e.IsAlive && layout.IsInSafeRoom(DungeonBuilder.WorldToCell(e.GlobalPosition)))) Fail("a creature is inside the safe room");
                // 7. step out past them: they come for the party (before the save, which keeps the count but not the creatures)
                if (_srGatherers.Count > 0)
                {
                    var outside = SafeRoomOutside(layout, 12);
                    var beyond = gather.Neighbours4().Where(n => outside.TryGetValue(n, out var d) && d > outside[gather]).Select(n => (GridPos?)n).FirstOrDefault() ?? gather;
                    party.Teleport(Stand(beyond), b.Surface(gather) + Vector3.Up);
                    GD.Print($"[saferoom] party steps out to {beyond}");
                }
                Next();
                break;
            }
            case 9:
            {
                if (_srGatherers.Count == 0) { GD.Print("[saferoom] no gatherers to step out to (none gathered)"); Go(30); break; }
                var hunting = _srGatherers.Where(e => IsInstanceValid(e) && e.State.Name is "Chase" or "Attack").ToList();
                if (hunting.Count > 0 || _srGatherers.All(e => !IsInstanceValid(e) || !e.IsAlive))
                {
                    GD.Print($"[saferoom] {since:0.0} s after stepping out: [{string.Join(", ", _srGatherers.Where(IsInstanceValid).Select(e => $"{e.DisplayName}:{e.State.Name}"))}]");
                    if (hunting.Count == 0) Fail("the gatherers died before they were seen to attack");
                    // 8. back inside behind the shut door: a gatherer that loses the party goes back to its post, awake
                    party.Teleport(Stand(SafeRoomRules.MerchantCell(layout)), b.Surface(layout.SafeRoomDoor));
                    Go(20);
                }
                else if (since > 4.0) { Fail($"no gatherer attacked within 4 s: [{string.Join(", ", _srGatherers.Where(IsInstanceValid).Select(e => e.State.Name))}]"); Go(30); }
                break;
            }
            case 20:
            {
                if (_srDoor!.IsOpen && since >= 0.1) _srDoor.Close(); // the party is deep inside: shut it now rather than after the delay
                var left = _srGatherers.Where(e => IsInstanceValid(e) && e.IsAlive).ToList();
                if (left.Count == 0) { GD.Print("[saferoom] no gatherer left alive to go back to its post"); Go(30); break; }
                float FromPost(Enemy e) => new Vector2(e.GlobalPosition.X - e.HomePosition.X, e.GlobalPosition.Z - e.HomePosition.Z).Length();
                var holding = left.FirstOrDefault(e => e.State.Name == "Hold" && !e.IsUnaware && e.HoldsPost);
                if (holding != null && since >= 0.5)
                {
                    GD.Print($"[saferoom] {since:0.0} s after the party went back inside: {holding.DisplayName} holds again ({holding.State.Name}, unaware={holding.IsUnaware}), {FromPost(holding):0.0} m from its post");
                    _srEnemy = holding;
                    Go(21);
                }
                else if (since > left[0].GiveUpSeconds + 2.0)
                {
                    Fail($"no gatherer went back to holding within {left[0].GiveUpSeconds + 2.0:0} s: [{string.Join(", ", left.Select(e => $"{e.DisplayName}:{e.State.Name}:unaware={e.IsUnaware}"))}]");
                    Go(30);
                }
                break;
            }
            case 21:
            {
                // and it walks back to the post it holds, still awake
                var e = _srEnemy!;
                if (!IsInstanceValid(e) || !e.IsAlive) { GD.Print("[saferoom] the holder died on its way back"); Go(30); break; }
                var away = new Vector2(e.GlobalPosition.X - e.HomePosition.X, e.GlobalPosition.Z - e.HomePosition.Z).Length();
                if (away <= HoldState.StrayDistance && e.State.Name == "Hold" && !e.IsUnaware)
                {
                    GD.Print($"[saferoom] {e.DisplayName} back at its post ({away:0.0} m) after {since:0.0} s, holding awake: OK");
                    Go(30);
                }
                else if (since > 12.0) { Fail($"{e.DisplayName} did not walk back to its post: {away:0.0} m off, {e.State.Name}, unaware={e.IsUnaware}"); Go(30); }
                break;
            }
            case 30:
            {
                // 9. the open door holds a creature out: a member stands in the doorway (the door open for them), an aggroed
                // melee creature starts 3 m outside; the companions wait frozen deep inside so nothing kills it first
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                var merchant = SafeRoomRules.MerchantCell(layout);
                party.Teleport(Stand(merchant), b.Surface(layout.SafeRoomDoor));
                var anchor = party.Anchor;
                _srFrozen.Clear();
                foreach (var a in party.Active.Where(a => a != anchor)) { a.ProcessMode = ProcessModeEnum.Disabled; _srFrozen.Add(a); }
                anchor.Teleport(Stand(layout.SafeRoomDoor), b.Surface(merchant));
                _srMember = anchor.Index;
                // 3 m out from the door's centre, toward the corridor cell beside it
                var beside = Outside(1)!.Value;
                var outward = (b.Surface(beside) - _srDoor!.GlobalPosition) with { Y = 0 };
                var start = _srDoor.GlobalPosition with { Y = Stand(beside).Y } + outward.Normalized() * 3f;
                var regulars = b.RegularEnemies(b.CurrentFloor);
                var meleeId = regulars.FirstOrDefault(id => b.EnemyProfile(id)?.Kind == EnemyKind.Melee) ?? regulars[0];
                _srEnemy = b.SpawnEnemy(meleeId, start, b.CurrentFloor);
                if (_srEnemy == null) { Fail("could not spawn the doorway test enemy"); Go(33); break; }
                _srBreached = false;
                GD.Print($"[saferoom] {party.Members[anchor.Index].Name} stands in the doorway {layout.SafeRoomDoor}; {_srEnemy.DisplayName} spawned at {DungeonBuilder.WorldToCell(start)}, {(start - _srDoor.GlobalPosition with { Y = start.Y }).Length():0.0} m outside");
                Next();
                break;
            }
            case 31 when since >= 0.1:
                _srEnemy!.ForceAggro();
                Next();
                break;
            case 32:
            {
                // the member in the doorway is kept standing (the door opens for a living member only)
                var m = party.Members[_srMember];
                m.Hp = m.Stats[Characters.Stat.MaxHp];
                if (IsInstanceValid(_srEnemy) && _srEnemy!.IsAlive && layout.IsInSafeRoom(DungeonBuilder.WorldToCell(_srEnemy.GlobalPosition))) _srBreached = true;
                if (since < 3.0) break;
                var alive = IsInstanceValid(_srEnemy) && _srEnemy!.IsAlive;
                var cell = alive ? DungeonBuilder.WorldToCell(_srEnemy!.GlobalPosition) : default;
                var atDoor = alive ? _srEnemy!.GlobalPosition.DistanceTo(_srDoor!.GlobalPosition) : -1f;
                GD.Print($"[saferoom] after 3 s with the door open={_srDoor!.IsOpen}: the enemy (alive={alive}, {(alive ? _srEnemy!.State.Name : "-")}) at {cell}, {atDoor:0.0} m from the door, ever inside={_srBreached}");
                if (!_srDoor.IsOpen) Fail("the safe door was not open for the member in the doorway");
                if (!alive) Fail("the doorway test enemy died");
                else if (_srEnemy!.State.Name is not ("Chase" or "Attack")) Fail($"the doorway test enemy is in {_srEnemy.State.Name}, not after the member");
                if (_srBreached || (alive && layout.IsInSafeRoom(cell))) Fail("the enemy followed the party through the open safe door");
                // held at the threshold: without the barrier it walks into the door cell and strikes from there (1.9 m measured)
                if (alive && cell == layout.SafeRoomDoor) Fail("the enemy stepped into the open doorway");
                if (alive && atDoor > SafeRoomRules.DoorOpenRadius + 0.5f) Fail($"the doorway test enemy stands {atDoor:0.0} m off, never came at the doorway");
                if (_srDoor.IsOpen && alive && !_srBreached && !layout.IsInSafeRoom(cell) && cell != layout.SafeRoomDoor && atDoor <= SafeRoomRules.DoorOpenRadius + 0.5f) GD.Print("[saferoom] open door held the enemy out: OK");
                Go(33);
                break;
            }
            case 33:
            {
                foreach (var a in _srFrozen.Where(IsInstanceValid)) a.ProcessMode = ProcessModeEnum.Inherit;
                _srFrozen.Clear();
                if (IsInstanceValid(_srEnemy)) _srEnemy!.QueueFree();
                Go(10);
                break;
            }
            case 10:
            {
                // 6. the count survives a save (the creatures themselves do not: the clock is what is kept)
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                party.Teleport(Stand(SafeRoomRules.MerchantCell(layout)), b.Surface(layout.SafeRoomDoor));
                _srSavedGathered = controller?.Gathered ?? -1;
                main.SaveGame("saferoom");
                if (!main.LoadGame("saferoom")) { Fail("the save would not load"); Finish(); return; }
                Next();
                break;
            }
            case 11 when since >= 2.0:
            {
                var back = SafeRoomController.Instance?.Gathered ?? -1;
                GD.Print($"[saferoom] save/load: Gathered {_srSavedGathered} -> {back}");
                if (back != _srSavedGathered) Fail($"Gathered came back as {back}, saved {_srSavedGathered}");
                // the gas run: the room's air stays clean while the rest of the floor fills
                if (Adventure.Objectives.ObjectiveController.Instance is Adventure.Objectives.ToxicController gas)
                {
                    gas.Seconds = 200f;
                    var merchant = SafeRoomRules.MerchantCell(layout);
                    var dangerInside = SafeRoomRules.Cells(layout).Where(layout.IsWalkable).Max(gas.DangerAt);
                    var gassedOutside = layout.Rooms.Where((r, i) => i != layout.SafeRoom).Any(r => gas.DangerAt(r.Center) > 0);
                    GD.Print($"[saferoom] gas at {gas.Seconds:0} s: merchant cell danger {gas.DangerAt(merchant)}, worst inside {dangerInside}, gas elsewhere {gassedOutside}");
                    if (gas.DangerAt(merchant) != 0 || dangerInside != 0) Fail("the gas gets into the safe room");
                    else GD.Print("[saferoom] air inside: clean");
                    if (!gassedOutside) Fail("no gas anywhere at 200 s: the check proves nothing");
                }
                Go(40);
                break;
            }
            case 40:
            {
                // 10. the merchant's shop, from the merchant's cell: the stock, a buy, the same slot refused, a sale, the sold slots through a save
                party.Teleport(Stand(SafeRoomRules.MerchantCell(layout)), b.Surface(layout.SafeRoomDoor));
                var floor = Game.Instance.CurrentFloor;
                var stock = party.ShopStockFor(floor);
                var wantUnique = Items.ShopRules.IsUniqueFloor(floor);
                GD.Print($"[saferoom] stock: {stock.Items.Count} items, unique={(stock.UniqueIndex >= 0 ? "yes" : "no")}");
                if (stock.Items.Count != (wantUnique ? 12 : 11) || (stock.UniqueIndex >= 0) != wantUnique || (wantUnique && stock.UniqueIndex != 11))
                    Fail($"stock has {stock.Items.Count} items, unique at {stock.UniqueIndex} (floor {floor} {(wantUnique ? "carries" : "carries no")} unique)");
                if (stock.Items.Count == 0) { Finish(); return; }
                // (b) a buy
                party.AddGold(10_000);
                var goldBefore = party.Gold;
                var bagBefore = party.Bag.Items.Count();
                var held = Enumerable.Range(0, party.Bag.Capacity).Where(i => party.Bag[i] != null).ToHashSet();
                var price = Items.Prices.BuyPrice(stock.Items[0]);
                var refusal = party.ShopBuy(0);
                var newIndex = Enumerable.Range(0, party.Bag.Capacity).FirstOrDefault(i => party.Bag[i] != null && !held.Contains(i), -1);
                if (refusal == null && party.Bag.Items.Count() == bagBefore + 1 && party.Gold == goldBefore - price && party.ShopSoldOnFloor.Contains(0) && newIndex >= 0)
                    GD.Print($"[saferoom] buy: OK ({party.Bag[newIndex]} for {price} gold into bag cell {newIndex})");
                else Fail($"buy: refusal \"{refusal}\", bag {bagBefore} -> {party.Bag.Items.Count()}, gold {goldBefore} -> {party.Gold} (price {price}), sold [{string.Join(",", party.ShopSoldOnFloor)}]");
                // (c) the same slot again
                var goldMid = party.Gold;
                var bagMid = party.Bag.Items.Count();
                var again = party.ShopBuy(0);
                if (again != null && party.Gold == goldMid && party.Bag.Items.Count() == bagMid) GD.Print($"[saferoom] buy twice: refused OK (\"{again}\")");
                else Fail($"buy twice: refusal \"{again}\", gold {goldMid} -> {party.Gold}, bag {bagMid} -> {party.Bag.Items.Count()}");
                // (d) a sale of what was bought
                if (newIndex >= 0)
                {
                    var sellPrice = Items.Prices.SellPrice(party.Bag[newIndex]!);
                    var goldPre = party.Gold;
                    var bagPre = party.Bag.Items.Count();
                    var sold = party.ShopSell(Party.BagSlot.Bag(newIndex));
                    if (sold == null && party.Gold == goldPre + sellPrice && party.Bag.Items.Count() == bagPre - 1) GD.Print($"[saferoom] sell: OK ({sellPrice} gold)");
                    else Fail($"sell: refusal \"{sold}\", gold {goldPre} -> {party.Gold} (price {sellPrice}), bag {bagPre} -> {party.Bag.Items.Count()}");
                }
                // (e) the sold slot through a save
                main.SaveGame("shop");
                if (!main.LoadGame("shop")) { Fail("the shop save would not load"); Finish(); return; }
                Next();
                break;
            }
            case 41 when since >= 2.0:
            {
                if (party.ShopSoldOnFloor.Contains(0)) GD.Print($"[saferoom] sold slots survive a reload: OK ([{string.Join(",", party.ShopSoldOnFloor.OrderBy(i => i))}])");
                else Fail($"sold slots after the reload: [{string.Join(",", party.ShopSoldOnFloor)}], want 0 among them");
                // (f) the shop screen opens and closes
                try
                {
                    var hud = main.Hud;
                    var shop = hud.Shop;
                    var floor = Game.Instance.CurrentFloor;
                    shop.Open(floor);
                    var stockCells = shop.StockCount;
                    var soldDim = shop.StockCell(0).Modulate.A;
                    var price = shop.StockCell(1).Caption;
                    var wantPrice = $"{Items.Prices.BuyPrice(party.ShopStockFor(floor).Items[1])}g";
                    var purse = shop.GoldText.Split('\n')[0]; // the label's second line names the focused member
                    var open = hud.ShopOpen;
                    var locked = party.InputLocked;
                    shop.Close();
                    GD.Print($"[saferoom] shop screen: open={open}, {stockCells} wares, slot 0 alpha {soldDim:0.0}, slot 1 \"{price}\", \"{purse}\", input locked={locked}; closed open={hud.ShopOpen} locked={party.InputLocked}");
                    if (!open || stockCells != party.ShopStockFor(floor).Items.Count || Math.Abs(soldDim - 0.4f) > 0.01f || price != wantPrice || purse != $"{party.Gold} gold" || !locked || hud.ShopOpen || party.InputLocked)
                        Fail("the shop screen did not show the stock, the sold slot, the prices and the purse, or did not lock and release the input");
                    else GD.Print("[saferoom] shop screen opens: OK");
                    // (g) a legendary from the bag waits for the question: still there after the click, gone once confirmed
                    var cat = Game.Instance.Catalog;
                    var leg = cat.Legendaries.Values.Where(l => l.ShopOnly).OrderBy(l => l.Id, StringComparer.Ordinal).First();
                    var legendary = Items.LootRoller.MakeLegendary(cat, leg, 10, new Random(1));
                    if (!party.Bag.TryAdd(legendary)) { Fail("no bag room for the legendary"); Finish(); break; }
                    var cellIndex = Enumerable.Range(0, party.Bag.Capacity).First(i => ReferenceEquals(party.Bag[i], legendary));
                    var legPrice = Items.Prices.SellPrice(legendary);
                    var goldPre = party.Gold;
                    shop.Open(floor);
                    shop.Clicked(UI.Shop.ShopSection.Bag, cellIndex);
                    var waited = ReferenceEquals(party.Bag[cellIndex], legendary) && party.Gold == goldPre;
                    var asking = shop.Confirming;
                    shop.Confirm();
                    var gone = party.Bag[cellIndex] == null && party.Gold == goldPre + legPrice;
                    shop.Close();
                    GD.Print($"[saferoom] legendary sell ({legendary.DisplayName}, {legPrice} gold): after the click in bag={waited} asking={asking}; after confirming gone={gone}, gold {goldPre} -> {party.Gold}");
                    if (waited && asking && gone) GD.Print("[saferoom] legendary sell waits for the confirm: OK");
                    else Fail("a legendary sold without asking, or the confirm did not sell it");
                }
                catch (Exception e) { Fail($"shop screen: {e.GetType().Name}: {e.Message}"); }
                Finish();
                break;
            }
        }
    }


    // ---- --test-narration -------------------------------------------------------------------------------
    // A floor's RoomEnter narration on the shipped campaign's floor 1: the party stands in the entry's room and the entry
    // is raised exactly once; after a save and a load, standing there again raises nothing. Floor 1 opens with the Skin's
    // tier line, then the intro once it has been heard; floor 2 (no tier line) opens after floor 1's parting taunt; on
    // floor 20 the victory recap waits until the closing narration has been raised and heard.
    private bool _testNarration;
    private int _nrStep;
    private double _nrAt;
    private readonly List<string> _nrFails = new();
    private readonly List<string> _nrHeard = new();
    private readonly List<double> _nrHeardAt = new();
    private bool _nrListening;
    private NarrationPlan? _nrEntry;
    private TauntPlan? _nrDescend;
    private int _nrBossDeadQueued;
    private string _nrTierText = "";
    private readonly Dictionary<string, string?> _nrVoiceOf = new(); // each heard line's voice id: its recording's real length is what the next line must wait
    /// <summary>A heard line's recording length (null without one) and what the director waits for it.</summary>
    private double? NrReal(string text) => VoiceRenderer.Instance?.LengthOf(_nrVoiceOf.GetValueOrDefault(text));
    private double NrNeed(string text) => Adventure.Voice.NarrationRules.HeardSeconds(text, NrReal(text));
    private double _nrTierAt;
    private double _nrKilledAt;

    private void RunNarrationTest()
    {
        if (_nrStep >= 99) return;
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot == null || main.Builder.Layout is not { } layout || !Game.Instance.InGame || (main.Builder.CurrentFloor != 1 && _nrStep < 4) || !main.Party.HasBodies) return;
        var since = _elapsed - _nrAt;
        void Next() { _nrStep++; _nrAt = _elapsed; }
        void Fail(string why) { _nrFails.Add(why); GD.Print($"[narration] FAIL: {why}"); }
        void Finish()
        {
            GD.Print(_nrFails.Count == 0 ? "[narration] RESULT ok" : $"[narration] RESULT FAIL ({string.Join("; ", _nrFails)})");
            _nrStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        int Heard() => _nrHeard.Count(t => t == _nrEntry!.Line.Text);
        void StandInRoom()
        {
            var room = layout.Rooms[_nrEntry!.Room!.Value];
            foreach (var body in main.Party.Active) body.GlobalPosition = main.Builder.Surface(room.Center);
        }
        if (_elapsed > 160) { Fail($"timed out at step {_nrStep}"); Finish(); return; }
        switch (_nrStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                if (!_nrListening) { EventBus.Instance.Narration += (text, voice) => { _nrHeard.Add(text); _nrHeardAt.Add(_elapsed); _nrVoiceOf[text] = voice; }; _nrListening = true; }
                _nrEntry = main.Builder.Contents?.Plan?.Narration.FirstOrDefault(n => n.Trigger == NarrationTrigger.RoomEnter && n.Room is { } r && r >= 0 && r < layout.Rooms.Count);
                if (_nrEntry == null) { Fail("floor 1 has no RoomEnter narration"); Finish(); return; }
                // the opening: the tier line first, raised once and remembered
                var tier = Game.Instance.Adventure.Campaign is { } tc ? Adventure.Voice.NarrationRules.TierEntry(tc, 1) : null;
                if (tier == null) Fail("the shipped campaign has no tier line for floor 1");
                else
                {
                    var heardTier = _nrHeard.Count(t => t == tier.EnterLine.Text);
                    GD.Print($"[narration] floor 1 opened with: {string.Join(" | ", _nrHeard.Select(t => t.Length > 50 ? t[..50] + "..." : t))}");
                    if (heardTier != 1 || _nrHeard.FirstOrDefault() != tier.EnterLine.Text) Fail($"the tier line was not the first thing heard on floor 1 (heard it {heardTier} times)");
                    if (!Game.Instance.Adventure.BarksSaid.ContainsKey(Adventure.Voice.NarrationRules.TierKey(tier))) Fail("the tier line was not remembered");
                    var intro = main.Builder.Contents?.Plan?.IntroLine.Text ?? "";
                    if (_nrHeard.Contains(intro)) Fail("the floor 1 intro came at once, over the tier line");
                    _nrTierText = tier.EnterLine.Text;
                }
                GD.Print($"[narration] entry for room {_nrEntry.Room}: \"{_nrEntry.Line.Text}\"");
                Game.Instance.Adventure.BarksSaid.Remove(Adventure.Voice.NarrationRules.Key(1, _nrEntry)); // the spawn may already have been in that room
                _nrTierAt = _nrHeard.Count > 0 ? _nrHeardAt[0] : 0;
                _nrHeard.Clear(); _nrHeardAt.Clear();
                _nrHeard.Add(_nrTierText); _nrHeardAt.Add(_nrTierAt); // keep the tier line for step 1's order check
                StandInRoom();
                Next();
                break;
            }
            case 1 when (Heard() > 0 && since >= 8.0) || since >= 45.0:
            {
                // the room line waits its turn behind the opening (the tier line, then the intro): one voice at a time
                var intro = main.Builder.Contents?.Plan?.IntroLine.Text ?? "";
                var ti = _nrTierAt; var ii = _nrHeard.IndexOf(intro); var ei = _nrHeard.IndexOf(_nrEntry!.Line.Text);
                GD.Print($"[narration] tier line at {ti:0.0}s, intro at {(ii >= 0 ? _nrHeardAt[ii] : -1):0.0}s, room line at {(ei >= 0 ? _nrHeardAt[ei] : -1):0.0}s");
                if (ii < 0) Fail("the floor 1 intro never played after the tier line");
                else if (_nrHeardAt[ii] - ti < (NrReal(_nrTierText) ?? NrNeed(_nrTierText)) - 0.05) Fail($"the intro came {_nrHeardAt[ii] - ti:0.0}s after the tier line, whose recording runs {NrReal(_nrTierText):0.0}s");
                if (ii >= 0 && ei >= 0 && _nrHeardAt[ei] - _nrHeardAt[ii] < (NrReal(intro) ?? NrNeed(intro)) - 0.05) Fail("the room line cut the intro off");
                if (Heard() != 1) Fail($"entering the room raised the entry {Heard()} times, wanted 1");
                if (!Game.Instance.Adventure.BarksSaid.ContainsKey(Adventure.Voice.NarrationRules.Key(1, _nrEntry!))) Fail("the played entry was not remembered");
                main.SaveGame("narration-test");
                GD.Print($"[narration] load -> {main.LoadGame("narration-test")}");
                _nrHeard.Clear(); _nrHeardAt.Clear();
                Next();
                break;
            }
            case 2 when since >= 3.0:
            {
                if (!Game.Instance.Adventure.BarksSaid.ContainsKey(Adventure.Voice.NarrationRules.Key(1, _nrEntry!))) Fail("the played entry did not survive the save");
                StandInRoom();
                Next();
                break;
            }
            case 3 when since >= 6.0:
            {
                if (Heard() != 0) Fail($"after a load the entry played again ({Heard()} times)");
                // leaving by the stairs: floor 1's own Descend taunt is heard, and the room line does not replay on the way
                _nrDescend = Game.Instance.Adventure.FloorPlans[1].Taunts.FirstOrDefault(t => t.Trigger == "Descend");
                if (_nrDescend == null) { Fail("floor 1 has no Descend taunt"); Finish(); return; }
                _nrHeard.Clear(); _nrHeardAt.Clear();
                Game.Instance.ChangeFloor(+1);
                Next();
                break;
            }
            case 4 when since >= 22.0:
            {
                var taunts = _nrHeard.Count(t => t.Contains(_nrDescend!.Line.Text));
                if (taunts != 1) Fail($"floor 1's Descend taunt was heard {taunts} times, wanted 1");
                if (Heard() != 0) Fail($"the room line replayed across the floor change ({Heard()} times)");
                // the parting taunt, then the new floor's opening, far enough apart for the first to be heard
                var at = _nrHeard.FindIndex(t => t.Contains(_nrDescend!.Line.Text));
                if (at >= 0 && at + 1 < _nrHeard.Count)
                {
                    var gap = _nrHeardAt[at + 1] - _nrHeardAt[at];
                    var real = NrReal(_nrHeard[at]);
                    GD.Print($"[narration] parting taunt (recording {(real is { } r ? $"{r:0.00}s" : "none")}), then \"{_nrHeard[at + 1]}\" {gap:0.00}s later");
                    if (real == null) Fail("floor 1's parting taunt has no recording to measure");
                    else if (gap < real.Value - 0.05) Fail($"the next floor's opening came {gap:0.00}s after the parting taunt, whose recording runs {real:0.00}s");
                }
                else Fail("nothing opened floor 2 after the parting taunt");
                // floor 2 is no tier's first floor: no tier line there
                if (Game.Instance.Adventure.Campaign is { } c2 && c2.Tiers.Any(t => _nrHeard.Contains(t.EnterLine.Text))) Fail("a tier line played on floor 2");
                Game.Instance.JumpToFloor(20);
                Next();
                break;
            }
            case 5 when main.Builder.CurrentFloor == 20 && since >= 3.0:
            {
                var boss = main.Builder.FloorRoot.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.IsBoss);
                if (boss == null) { Fail("floor 20 has no boss"); Finish(); return; }
                _nrHeard.Clear(); _nrHeardAt.Clear();
                _nrKilledAt = _elapsed;
                boss.Kill();
                Next();
                break;
            }
            case 6 when since >= 0.5:
            {
                var plan = main.Builder.Contents?.Plan;
                var closing = plan?.Narration.Where(n => n.Trigger == NarrationTrigger.BossDead && FlagRules.Allowed(n.RequiresFlag, n.ForbidsFlag, Game.Instance.Adventure.Memory.Flags)).Select(n => n.Line.Text).ToList() ?? new List<string>();
                _nrBossDeadQueued = closing.Count;
                GD.Print($"[narration] floor 20 boss dead: victory={Game.Instance.Victory}, recap shown={main.LastRecap != null}, held={AdventureDirector.Instance.RecapHeld}, closing lines {closing.Count}");
                if (!Game.Instance.Victory) Fail("killing the last boss did not declare victory");
                if (closing.Count == 0) Fail("floor 20 has no closing narration to wait for");
                if (main.LastRecap != null) Fail("the victory recap was shown before the closing narration was raised");
                Next();
                break;
            }
            case 7 when main.LastRecap != null || since >= 75.0:
            {
                var plan = main.Builder.Contents?.Plan;
                var closing = plan?.Narration.Where(n => n.Trigger == NarrationTrigger.BossDead && FlagRules.Allowed(n.RequiresFlag, n.ForbidsFlag, Game.Instance.Adventure.Memory.Flags)).Select(n => n.Line.Text).ToList() ?? new List<string>();
                var recapAt = _elapsed;
                var last = closing.Count > 0 ? _nrHeard.LastIndexOf(closing[^1]) : -1;
                GD.Print($"[narration] victory recap after {recapAt - _nrKilledAt:0.0}s: \"{main.LastRecap?.Headline}\"; closing lines heard {closing.Count(_nrHeard.Contains)}/{closing.Count}{(last >= 0 ? $", the last at {_nrHeardAt[last] - _nrKilledAt:0.0}s" : "")}");
                if (main.LastRecap == null) Fail("the victory recap never came");
                if (closing.Any(t => !_nrHeard.Contains(t))) Fail("the recap came before every closing line was raised");
                if (last >= 0) GD.Print($"[narration] the last closing line's recording runs {NrReal(closing[^1]):0.00}s; the recap came {recapAt - _nrHeardAt[last]:0.00}s after it started");
                if (last >= 0 && recapAt - _nrHeardAt[last] < (NrReal(closing[^1]) ?? NrNeed(closing[^1])) - 0.05) Fail("the recap came over the last closing line");
                for (var i = 0; i + 1 < _nrHeard.Count; i++) // the death line and the closing lines one after another, none cut
                    if (NrReal(_nrHeard[i]) is { } len && _nrHeardAt[i + 1] - _nrHeardAt[i] < len - 0.05) Fail($"\"{_nrHeard[i][..Math.Min(40, _nrHeard[i].Length)]}...\" ({len:0.0}s) was cut after {_nrHeardAt[i + 1] - _nrHeardAt[i]:0.0}s");
                if (main.LastRecap is { } r && Game.Instance.Adventure.Campaign is { } c && r.Headline.Split(c.Antagonist.Name).Length > 2) Fail($"the headline says {c.Antagonist.Name} twice");
                Finish();
                break;
            }
        }
    }

    // ---- --test-writing ---------------------------------------------------------------------------------
    // A party that talks, end to end on the shipped campaign's floor 1: a banter exchange in order and 1.4 s apart, one
    // aborted by a fight, a TrapSprung bark from the lazy stock top-up with the speaker on the subtitle, narration
    // beating a bark on the subtitle, the cheer and its answers (and the cooldown), the press epitaph on a wipe, floor 3's
    // press taunt on Crushed only, and Crane's aside.
    private bool _testWriting;
    private int _wrStep;
    private double _wrAt;
    private readonly List<string> _wrFails = new();
    private readonly List<(string Who, string Text)> _wrHeard = new();
    private int _wrHeardBefore;
    private bool _wrListening;
    private Enemy? _wrEnemy;

    private void RunWritingTest()
    {
        if (_wrStep >= 99) return;
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || !Game.Instance.InGame || main.Builder.CurrentFloor != 1) return;
        var party = main.Party;
        if (!party.HasBodies) return;
        var director = AdventureDirector.Instance!;
        var since = _elapsed - _wrAt;
        void Next() { _wrStep++; _wrAt = _elapsed; }
        void Fail(string why) { _wrFails.Add(why); GD.Print($"[writing] FAIL: {why}"); }
        void Finish()
        {
            GD.Print(_wrFails.Count == 0 ? "[writing] RESULT ok" : $"[writing] RESULT FAIL ({string.Join("; ", _wrFails)})");
            _wrStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        if (_elapsed > 120) { Fail($"timed out at step {_wrStep}"); Finish(); return; }
        IEnumerable<(string Who, string Text)> Since() => _wrHeard.Skip(_wrHeardBefore);
        string Subtitle() => main.Hud.GetNode<Label>("Subtitle").Text;
        var lead = party.Lead.State;
        switch (_wrStep)
        {
            case 0 when _elapsed >= 1.0 && (AdventureDirector.Instance.NarrationIdle || _elapsed >= 40.0): // floor 1 opens with the tier line, then the intro: the subtitle checks below start after them
            {
                if (!_wrListening) { EventBus.Instance.Bark += (who, line) => _wrHeard.Add((who, line.Text)); _wrListening = true; }
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree(); // a quiet floor: only the test's creature moves
                foreach (var m in party.Members) m.Hp = m.Stats[Characters.Stat.MaxHp] * 10; // nobody goes down to the test's own creature
                GD.Print($"[writing] floor {main.Builder.CurrentFloor} campaign \"{Game.Instance.Adventure.Campaign?.Title}\" lead {lead.Name}");
                _wrHeardBefore = _wrHeard.Count;
                if (!director.StartBanter("bs_corporal") || !director.BanterPlaying) { Fail("StartBanter(bs_corporal) did not start"); Finish(); return; }
                Next();
                break;
            }
            case 1:
            {
                // the four lines in order, then the exchange is remembered by id
                if (director.BanterPlaying) { if (since > 7) { Fail("banter never finished"); Finish(); } return; }
                var want = new[] { ("Sable", "You were a sergeant."), ("Brand", "Corporal. Twice."), ("Sable", "Twice?"), ("Brand", "They kept taking it back.") };
                var got = Since().ToList();
                GD.Print($"[writing] banter heard: {string.Join(" | ", got.Select(g => $"{g.Who}: {g.Text}"))}");
                if (!got.SequenceEqual(want)) Fail("banter lines out of order or missing");
                if (!Game.Instance.Adventure.BarksSaid.ContainsKey("banter:bs_corporal")) Fail("the exchange was not marked said by id");
                _wrHeardBefore = _wrHeard.Count;
                if (!director.StartBanter("grp_alive")) { Fail("StartBanter(grp_alive) did not start"); Finish(); return; }
                Next();
                break;
            }
            case 2 when since >= 0.2:
            {
                // a creature joins the conversation: the rest of the exchange is dropped
                var at = party.Lead.GlobalPosition + party.Lead.GlobalTransform.Basis.Z * -3f;
                _wrEnemy = main.Builder.SpawnEnemy(main.Builder.RegularEnemies(main.Builder.CurrentFloor)[0], at with { Y = party.Lead.GlobalPosition.Y - 0.2f }, main.Builder.CurrentFloor);
                if (_wrEnemy == null) { Fail("could not spawn a creature"); Finish(); return; }
                _wrEnemy.ForceAggro();
                Next();
                break;
            }
            case 3 when since >= 2.5:
            {
                var lines = Since().Count();
                if (!director.BanterPlaying && lines < 4) GD.Print($"[writing] banter aborted by the fight after {lines} lines");
                else Fail($"banter did not stop for the fight ({lines} lines, playing {director.BanterPlaying})");
                if (_wrEnemy != null && IsInstanceValid(_wrEnemy)) _wrEnemy.QueueFree();
                _wrEnemy = null;
                Next();
                break;
            }
            case 4 when since >= 6.0:
            {
                // a new trigger the shipped campaign has no lines for: the lazy top-up supplies one in the hero's voice
                _wrHeardBefore = _wrHeard.Count;
                EventBus.Instance.RaiseTrapSprung(0);
                Next();
                break;
            }
            case 5 when since >= 0.5:
            {
                var m = party.Members[0];
                var stock = Adventure.Voice.ReactiveBarks.For(BarkTrigger.TrapSprung, m.Name, m.Class.Id);
                var bark = Since().FirstOrDefault(h => h.Who == m.Name);
                GD.Print($"[writing] trap bark: {(bark.Text == null ? "none" : $"{bark.Who}: {bark.Text}")}; subtitle \"{Subtitle()}\"");
                if (bark.Text == null || !stock.Contains(bark.Text)) Fail("no TrapSprung bark from the stock bank");
                if (!Subtitle().StartsWith($"{m.Name}: ")) Fail("the subtitle does not carry the speaker's name");
                Next();
                break;
            }
            case 6 when since >= 5.5:
            {
                // narration wins the subtitle; the bark is still heard and logged
                _wrHeardBefore = _wrHeard.Count;
                EventBus.Instance.RaiseNarration("The narrator speaks.", null);
                EventBus.Instance.RaiseTrapSprung(1);
                var m = party.Members[1];
                var heard = Since().Any(h => h.Who == m.Name);
                GD.Print($"[writing] narration then bark: bark heard={heard}, subtitle \"{Subtitle()}\"");
                if (!heard) Fail("no bark for member 1 beside the narration");
                if (Subtitle() != "The narrator speaks.") Fail("a bark overwrote the narration on the subtitle");
                Next();
                break;
            }
            case 7 when since >= 5.5:
            {
                _wrHeardBefore = _wrHeard.Count;
                EventBus.Instance.RaiseCheer(party.LocalIndex);
                Next();
                break;
            }
            case 8 when since >= 0.5:
            {
                var cheers = Adventure.Voice.CheerBarks.Cheers(lead.Name, lead.Class.Id);
                var first = Since().FirstOrDefault();
                GD.Print($"[writing] cheer: {(first.Text == null ? "none" : $"{first.Who}: {first.Text}")}");
                if (first.Text == null || first.Who != lead.Name || !cheers.Contains(first.Text)) Fail("the cheer line did not come from the lead's Cheer bank");
                EventBus.Instance.RaiseCheer(party.LocalIndex); // inside the cooldown: nothing more
                Next();
                break;
            }
            case 9 when since >= Adventure.Voice.CheerRules.AnswerDelay(2) + 0.6:
            {
                var others = party.Active.Where(a => !a.Downed && a.State.Name != lead.Name).Select(a => a.State).ToList();
                var answers = Since().Skip(1).ToList();
                GD.Print($"[writing] answers: {string.Join(" | ", answers.Select(a => $"{a.Who}: {a.Text}"))}");
                if (answers.Count != others.Count) Fail($"{answers.Count} answers for {others.Count} other living heroes");
                for (var i = 0; i < Math.Min(answers.Count, others.Count); i++)
                {
                    if (answers[i].Who != others[i].Name) Fail($"answer {i} came from {answers[i].Who}, expected {others[i].Name}");
                    if (!Adventure.Voice.CheerBarks.Answers(others[i].Name, others[i].Class.Id).Contains(answers[i].Text)) Fail($"{answers[i].Who}'s answer is not from the CheerAnswer bank");
                }
                var cheers = Adventure.Voice.CheerBarks.Cheers(lead.Name, lead.Class.Id);
                if (answers.Any(a => a.Who == lead.Name && cheers.Contains(a.Text))) Fail("a second cheer spoke inside the cooldown");
                Next();
                break;
            }
            case 10:
            {
                // the epitaph: the press was the last thing that killed anyone, so the wipe is the press's
                EventBus.Instance.RaiseMemberKilledByHazard(0, "The Press");
                EventBus.Instance.RaisePartyWiped();
                var wipe = director.LastWipe;
                GD.Print($"[writing] wipe: {(wipe == null ? "none" : $"floor {wipe.Floor} cause {wipe.Cause} count {wipe.Count} \"{wipe.Epitaph}\"")}");
                if (wipe is not { Cause: "press", Floor: 1, Count: 1 }) Fail("LastWipe is not the press on floor 1, first time");
                if (wipe != null && wipe.Epitaph != Adventure.Voice.Epitaphs.Pick("press", 1, 1)) Fail("the epitaph is not the press's first line");
                if (!Game.Instance.Adventure.Memory.Flags.Contains("wiped_once")) Fail("wiped_once was not set");
                if (!Adventure.Voice.Epitaphs.Pick("press", 4, 9).StartsWith("Fourth time on floor 9.")) Fail("the fourth press epitaph does not escalate");
                Next();
                break;
            }
            case 11:
            {
                // floor 3's press line fires on a crushing, not a downing
                var loaded = BuiltinCampaign.LoadFrom(ProjectSettings.GlobalizePath("res://data/adventures/builtin"));
                if (loaded == null) { Fail("the builtin campaign did not load"); Next(); break; }
                var taunts = loaded.Value.Floors[3].Taunts;
                var td = new Adventure.Objectives.TauntDirector();
                var downed = td.Pick(taunts, Adventure.Objectives.TauntTrigger.MemberDowned, FloorObjectiveKind.None, new Dictionary<string, int>(), new List<string>(), 3, 0, new Random(1), urgent: true);
                var crushed = td.Pick(taunts, Adventure.Objectives.TauntTrigger.Crushed, FloorObjectiveKind.None, new Dictionary<string, int>(), new List<string>(), 3, 0, new Random(1), urgent: true);
                GD.Print($"[writing] floor 3: MemberDowned -> {(downed == null ? "nothing" : downed.Line.Text)}; Crushed -> {(crushed == null ? "nothing" : crushed.Line.Text)}");
                if (downed != null) Fail("floor 3 still has a MemberDowned taunt");
                if (crushed == null) Fail("floor 3 has no Crushed taunt");
                Next();
                break;
            }
            case 12:
            {
                // Crane's aside: Sable speaks once he has had his say
                var crane = root.GetChildren().OfType<Adventure.Npc>().FirstOrDefault(n => n.Id == "npc_crane");
                if (crane == null) { Fail("npc_crane is not on floor 1"); Finish(); return; }
                var at = crane.GlobalPosition + crane.GlobalTransform.Basis.Z * 1.5f;
                party.Teleport(at with { Y = crane.GlobalPosition.Y + 0.3f }, crane.GlobalPosition);
                main.Hud.OpenDialogue(crane);
                var node = main.Hud.Dialogue.Node;
                GD.Print($"[writing] Crane: node {node?.Id} aside {node?.Aside?.Member}");
                if (node?.Id != "gate") Fail($"Crane opened on {node?.Id}, not gate");
                if (node?.Aside?.Member != "Sable") Fail("the gate node has no aside for Sable");
                Next();
                break;
            }
            case 13:
            {
                if (main.Hud.Dialogue.Speaking) { if (since > 20) { Fail("Crane never stopped speaking"); Finish(); } return; }
                if (since < 0.3) return; // the aside lands the tick after the line ends
                _wrAt = _elapsed; _wrStep = 14; // one more tick for the aside's own frame
                break;
            }
            case 14 when since >= 0.3:
            {
                var label = main.Hud.Dialogue.FindChild("Aside", recursive: true, owned: false) as Label;
                GD.Print($"[writing] aside label: \"{label?.Text}\"");
                if (label?.Text != "Sable: He means us.") Fail("the aside label does not read \"Sable: He means us.\"");
                main.Hud.Dialogue.Close();
                Finish();
                break;
            }
        }
    }
    // ---- --rehash-plans=DIR --------------------------------------------------------------------------------
    private string? _rehashDir;
    private bool _rehashed;

    /// <summary>
    /// Regenerate each floor_NN.json's layout in <see cref="_rehashDir"/> from its campaign (seed and per-floor shape, as
    /// <see cref="DungeonBuilder.Build"/> does) and write back the plan's <c>LayoutHash</c>, so a build whose generator has
    /// moved on (a safe room displacing a spawn) loads the shipped plans without "adapted". Only the hash's digits change
    /// in each file: the rest of it stays byte for byte as written.
    /// </summary>
    private void RunRehashPlans()
    {
        _rehashed = true;
        var dir = _rehashDir!;
        if (BuiltinCampaign.LoadFrom(dir) is not { } baked) { GD.Print($"[rehash] RESULT FAIL: no campaign.json in {dir}"); GetTree().Quit(); return; }
        var builder = Main.Instance!.Builder;
        var adventure = Game.Instance.Adventure;
        var keep = adventure.Campaign;
        adventure.Campaign = baked.Campaign; // the floors' shapes and objectives come from its outline
        var seed = baked.Campaign.GameSeed;
        var changed = 0;
        try
        {
            foreach (var (floor, plan) in baked.Floors.OrderBy(kv => kv.Key))
            {
                var spec = builder.SpecFor(floor, seed);
                var hash = FloorGenerator.Generate(spec, Rng.FloorSeed(seed, floor)).ContentHash();
                var path = System.IO.Path.Combine(dir, $"floor_{floor:00}.json");
                if (plan.LayoutHash == hash) { GD.Print($"[rehash] floor {floor:00}: {hash} (unchanged)"); continue; }
                var text = System.IO.File.ReadAllText(path);
                var field = new System.Text.RegularExpressions.Regex("(\"LayoutHash\"\\s*:\\s*)\\d+");
                if (!field.IsMatch(text)) { GD.Print($"[rehash] floor {floor:00}: no LayoutHash field; left alone"); continue; }
                System.IO.File.WriteAllText(path, field.Replace(text, m => m.Groups[1].Value + hash.ToString(System.Globalization.CultureInfo.InvariantCulture), 1));
                GD.Print($"[rehash] floor {floor:00}: {plan.LayoutHash} -> {hash}");
                changed++;
            }
        }
        finally { adventure.Campaign = keep; }
        GD.Print($"[rehash] RESULT ok: {changed} of {baked.Floors.Count} floor plans rehashed in {dir}");
        GetTree().Quit();
    }

    // Party: companions follow, switch control, companions fight on their own, auto-revive.
    // --test-gold: a chest spills one coin pile; a full bag still takes it; a companion walks into a pile; enemies drop coins
    // about one time in four. Prints RESULT.
    private void RunGoldTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root) return;
        var party = main.Party;
        void Check(string label, bool ok) { GD.Print($"[gold] {label}: {(ok ? "OK" : "FAIL")}"); if (!ok) _goldFails++; }
        switch (_goldStep)
        {
            case 0 when _elapsed >= 1.0:
                _goldStep++;
                GD.Print($"[gold] start gold={party.Gold}");
                Check("starts empty", party.Gold == 0);
                party.AddGold(-10);
                var spent = party.TrySpendGold(5);
                Check("clamp", party.Gold == 0 && !spent);
                Check("negative spend refused", !party.TrySpendGold(-5) && party.Gold == 0);
                var chest = root.GetChildren().OfType<Chest>().Where(c => !c.Opened && !c.Locked).OrderBy(c => c.GlobalPosition.DistanceSquaredTo(party.Lead.GlobalPosition)).FirstOrDefault();
                if (chest == null) { Check("chest found", false); break; }
                chest.Interact(party, party.Lead);
                var piles = root.GetChildren().OfType<LootDrop>().Where(d => d.Coins > 0).ToList();
                var floor = Game.Instance.CurrentFloor;
                _goldPile = piles.FirstOrDefault();
                GD.Print($"[gold] chest coin piles={piles.Count} coins={_goldPile?.Coins ?? 0}");
                Check("chest pile", piles.Count == 1 && _goldPile!.Coins >= 10 + 6 * floor && _goldPile.Coins <= 25 + 10 * floor);
                break;
            case 1 when _elapsed >= 2.0:
                _goldStep++;
                if (_goldPile == null) break;
                for (var i = 0; party.Bag.FreeSlots > 0 && i < 200; i++) party.Bag.TryAdd(ItemInstance.Simple($"item.junk{i}", "Junk")); // one of each: nothing stacks
                _goldBefore = party.Gold;
                _goldPile.Interact(party, party.Lead);
                _goldPile.Interact(party, party.Lead); // a second pickup in the same frame must not pay again
                Check("full bag pickup", party.Bag.FreeSlots == 0 && party.Gold == _goldBefore + _goldPile.Coins && _goldPile.IsQueuedForDeletion());
                Check("double pickup", party.Gold == _goldBefore + _goldPile.Coins);
                break;
            case 2 when _elapsed >= 3.0:
                _goldStep++;
                var companion = party.Adventurers.First(a => !a.IsLocal);
                _goldBefore = party.Gold;
                party.SpawnCoins(25, companion.GlobalPosition + companion.GlobalTransform.Basis.Z * -1f);
                break;
            case 3 when _elapsed >= 5.0:
                _goldStep++;
                Check("companion pickup", party.Gold == _goldBefore + 25);
                // a pile left on the floor must survive save and load
                party.SpawnCoins(17, party.Lead.GlobalPosition + new Vector3(0, 0, -40f));
                main.SaveGame("gold");
                main.LoadGame("gold");
                var back = Main.Instance!.Builder.FloorRoot!.GetChildren().OfType<LootDrop>().ToList();
                Check("save round trip", back.Any(d => d.Coins == 17) && !back.Any(d => d.Coins == 0 && d.Item.DefId == "item.unknown"));
                break;
            case 4 when _elapsed >= 5.5:
                _goldStep++;
                if (_screenshotPath != null) Main.Instance!.Hud.ToggleInventory(); // the screenshot shows the purse
                else { Main.Instance!.Hud.ToggleInventory(); var line = Main.Instance.Hud.Inventory.PurseLine; Main.Instance.Hud.ToggleInventory(); GD.Print($"[gold] inventory shows: \"{line}\""); Check("inventory purse line", System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+ gold")); }
                var b = main.Builder;
                var def = b.RegularEnemies(b.CurrentFloor)[0];
                var at = party.Lead.GlobalPosition + new Vector3(0, 0, -40f); // far from every companion's reach
                _goldBefore = root.GetChildren().OfType<LootDrop>().Count(d => d.Coins > 0 && !d.IsQueuedForDeletion());
                for (var i = 0; i < 40; i++) b.SpawnEnemy(def, at, b.CurrentFloor).Kill();
                break;
            case 5 when _elapsed >= 6.5: // the deaths are handled on the next frames
                _goldStep++;
                var made = root.GetChildren().OfType<LootDrop>().Count(d => d.Coins > 0 && !d.IsQueuedForDeletion()) - _goldBefore;
                GD.Print($"[gold] enemy coins: piles={made} of 40");
                Check("enemy coins", made >= 3 && made <= 20);
                GD.Print(_goldFails == 0 ? "[gold] RESULT ok" : $"[gold] RESULT FAIL ({_goldFails})");
                _quitAfter = _elapsed + 0.5;
                break;
        }
    }

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

    // ---- --test-tavern ------------------------------------------------------------------------------------
    // The opening end to end: a new game stands in the tavern (one room, no enemies, the merchant, an empty purse, "The
    // Tavern", the tavern tune); an open inventory holds the merchant's greeting back and closing it lets him brief the
    // party; the floor survives a save with the purse still empty and the rebuilt merchant greets afresh; the briefing
    // walked to the deal pays the seed money once; the down flight takes the party to floor 1 without paying again.
    // --test-tavern-skip never talks: the purse stays empty until the descent stakes it.
    private bool _testTavern, _tavernSkip;
    private int _tvStep;
    private int _tvGreetings; // briefings opened with the floor's Npc_merchant: rising edges of the HUD's dialogue (a new HUD after a load counts afresh)
    private bool _tvTalking;
    private UI.Hud.Hud? _tvHud;
    private readonly List<string> _tvFails = new();
    private Node3D? _tvSavedRoot;

    private void RunTavernTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || main.Hud is not { } hud || !Game.Instance.InGame) return;
        var party = main.Party;
        var b = main.Builder;
        var game = Game.Instance;
        var talk = hud.Dialogue;
        void Next() => _tvStep++;
        void Fail(string why) { _tvFails.Add(why); GD.Print($"[tavern] FAIL: {why}"); }
        void Finish()
        {
            GD.Print(_tvFails.Count == 0 ? "[tavern] RESULT ok" : $"[tavern] RESULT FAIL ({string.Join("; ", _tvFails)})");
            _tvStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        Npc? Merchant() => root.GetNodeOrNull<Npc>("Npc_merchant");
        var talkingToMerchant = hud.DialogueOpen && talk.Npc != null && talk.Npc == Merchant();
        if (talkingToMerchant && (!_tvTalking || hud != _tvHud)) { _tvGreetings++; GD.Print($"[tavern] the merchant's briefing opened (#{_tvGreetings}) on \"{talk.Node?.Id}\""); }
        _tvTalking = talkingToMerchant;
        _tvHud = hud;
        void StandBeforeMerchant(Npc merchant)
        {
            // two metres in front of the room's centre, where he stands: inside his greeting radius
            var at = b.Surface(layout.Rooms[0].Center) + new Vector3(0, 0.3f, 2f);
            party.Teleport(at, merchant.GlobalPosition);
            var reach = new Vector2(party.Lead.GlobalPosition.X - merchant.GlobalPosition.X, party.Lead.GlobalPosition.Z - merchant.GlobalPosition.Z).Length();
            GD.Print($"[tavern] party before the merchant: {reach:0.0} m from him (greets within {merchant.GreetRadius:0.0} m)");
            if (reach > merchant.GreetRadius) Fail($"the party stands {reach:0.0} m from the merchant, outside his {merchant.GreetRadius:0.0} m");
        }
        string State() => $"floor={game.CurrentFloor} gold={party.Gold} paid={game.Adventure.SeedMoneyPaid} dialogue={hud.DialogueOpen} node={talk.Node?.Id ?? "-"}";
        switch (_tvStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                // 1. the tavern as a new game opens it
                var merchant = Merchant();
                var enemies = root.GetChildren().OfType<Enemy>().Count();
                var floorText = hud.GetNodeOrNull<Label>("%FloorLabel")?.Text;
                GD.Print($"[tavern] floor {game.CurrentFloor} kind={LayoutKinds.Id(layout.Kind)} rooms={layout.Rooms.Count} up={layout.UpStairs} down={layout.DownStairs} enemies={enemies} merchant={(merchant != null ? $"\"{merchant.NpcName}\" at {DungeonBuilder.WorldToCell(merchant.GlobalPosition)}" : "missing")} gold={party.Gold} hud=\"{floorText}\" theme={b.CurrentTheme?.Id}");
                if (game.CurrentFloor != 0) Fail($"a new game opens on floor {game.CurrentFloor}, not the tavern");
                if (layout.Kind != LayoutKind.Tavern) Fail($"floor 0 is a {LayoutKinds.Id(layout.Kind)} layout, not the tavern");
                if (b.CurrentTheme?.Id != "tavern") Fail($"floor 0 wears the {b.CurrentTheme?.Id ?? "default"} theme, not the tavern");
                if (game.Adventure.SeedMoneyPaid) Fail("a new game starts with the seed money paid");
                if (layout.Rooms.Count != 1) Fail($"the tavern has {layout.Rooms.Count} rooms");
                if (enemies != 0) Fail($"{enemies} enemies in the tavern");
                var portals = root.GetChildren().OfType<PortalSpawner>().Count();
                var ambushers = root.GetChildren().OfType<AmbushSpawner>().Count();
                GD.Print($"[tavern] spawners: portals={portals} ambushes={ambushers}");
                if (portals + ambushers != 0) Fail($"the tavern builds {portals} portal and {ambushers} ambush spawner(s)");
                if (party.Gold != 0) Fail($"the purse starts with {party.Gold} gold");
                if (floorText != "The Tavern") Fail($"the HUD says \"{floorText}\"");
                if (Audio.MusicDirector.Instance is { Enabled: true } music)
                {
                    GD.Print($"[tavern] music: mood={music.Mood} track={music.CurrentTrack}");
                    if (music.Mood != Audio.Mood.Tavern) Fail($"the music mood is {music.Mood}, not Tavern");
                }
                else GD.Print("[tavern] music: disabled in this run, mood check skipped");
                if (merchant == null) { Fail("no Npc_merchant in the tavern"); Finish(); return; }
                if (merchant.Greeted) Fail("the merchant greeted before the party came near");
                // 2. an open screen holds the greeting back
                hud.ToggleInventory();
                StandBeforeMerchant(merchant);
                Next();
                break;
            }
            case 1 when _elapsed >= 2.0:
            {
                var merchant = Merchant();
                GD.Print($"[tavern] with the inventory open beside him: inventory={hud.InventoryOpen} greeted={merchant?.Greeted} {State()}");
                if (!hud.InventoryOpen) Fail("the inventory did not open");
                if (hud.DialogueOpen || merchant?.Greeted == true) Fail("the merchant greeted over the open inventory");
                if (hud.InventoryOpen) hud.ToggleInventory();
                Next();
                break;
            }
            case 2 when _elapsed >= 3.0:
            {
                // 3. the inventory closed, he briefs the party
                var merchant = Merchant();
                var start = merchant?.Script.Start;
                GD.Print($"[tavern] inventory closed: inventory={hud.InventoryOpen} greeted={merchant?.Greeted} talking to={talk.Npc?.Id ?? "-"} {State()}");
                if (!hud.DialogueOpen) { Fail("the merchant did not brief the party once the inventory closed"); Finish(); return; }
                if (talk.Npc != merchant) Fail($"the open dialogue is {talk.Npc?.Id}'s, not the merchant's");
                if (start != "hire" || talk.Node?.Id != start) Fail($"the briefing opens on \"{talk.Node?.Id}\" (start \"{start}\", expected \"hire\")");
                if (_tvGreetings != 1) Fail($"{_tvGreetings} briefings opened before the save, expected 1");
                if (party.Gold != 0 || game.Adventure.SeedMoneyPaid) Fail($"the greeting paid before the deal: {State()}");
                if (_tavernSkip)
                {
                    talk.Close(); // never talks
                    GD.Print($"[tavern] skip: briefing closed unread, {State()}");
                    if (hud.DialogueOpen) Fail("the briefing did not close");
                    if (party.Gold != 0 || game.Adventure.SeedMoneyPaid) Fail($"closing the briefing paid: {State()}");
                    _tvStep = 5;
                    break;
                }
                // a save in the tavern, mid-briefing, and back
                _tvSavedRoot = root;
                main.SaveGame("tavern");
                if (!main.LoadGame("tavern")) { Fail("the tavern save would not load"); Finish(); return; }
                Next();
                break;
            }
            case 3 when root != _tvSavedRoot && Merchant() != null:
            {
                var merchant = Merchant()!;
                // the session is new (a fresh HUD with no dialogue open); the party comes back where it was saved, beside him,
                // so the rebuilt merchant (Greeted is not saved) may already have briefed it again on this first tick
                var loadedAt = new Vector2(party.Lead.GlobalPosition.X - merchant.GlobalPosition.X, party.Lead.GlobalPosition.Z - merchant.GlobalPosition.Z).Length();
                GD.Print($"[tavern] after the load: kind={LayoutKinds.Id(layout.Kind)} party {loadedAt:0.0} m from the merchant, greeted={merchant.Greeted} {State()}");
                if (game.CurrentFloor != 0 || layout.Kind != LayoutKind.Tavern) Fail($"the save came back on floor {game.CurrentFloor} ({LayoutKinds.Id(layout.Kind)})");
                if (party.Gold != 0) Fail($"the purse came back with {party.Gold} gold");
                if (game.Adventure.SeedMoneyPaid) Fail("the save came back with the seed money paid");
                if (!hud.DialogueOpen) StandBeforeMerchant(merchant);
                Next();
                break;
            }
            case 3 when _elapsed >= 5.0:
                Fail($"the floor was not rebuilt after the load ({State()})");
                Finish();
                return;
            case 4 when _elapsed >= 5.5:
            {
                // 4. the rebuilt merchant has greeted afresh; the briefing walked to the deal pays the seed money, once
                var merchant = Merchant();
                GD.Print($"[tavern] greeted again after the load: greeted={merchant?.Greeted} {State()}");
                if (!hud.DialogueOpen || talk.Npc != merchant) { Fail("the rebuilt merchant did not brief the party again"); Finish(); return; }
                if (talk.Node?.Id != merchant!.Script.Start || talk.Node?.Id != "hire") Fail($"the briefing after the load opens on \"{talk.Node?.Id}\", not \"hire\"");
                if (_tvGreetings != 2) Fail($"{_tvGreetings} briefings opened by the reload greeting, expected 2");
                if (party.Gold != 0 || game.Adventure.SeedMoneyPaid) Fail($"the greeting paid before the deal: {State()}");
                var path = new List<string> { talk.Node?.Id ?? "-" };
                for (var i = 0; i < 6 && hud.DialogueOpen && talk.Node?.Id != TavernRules.DealNodeId; i++)
                {
                    talk.Choose(0);
                    path.Add(talk.Node?.Id ?? "closed");
                }
                GD.Print($"[tavern] briefing walked: {string.Join(" -> ", path)}; at the deal {State()}");
                if (talk.Node?.Id != TavernRules.DealNodeId) { Fail("the briefing never reached the deal"); Finish(); return; }
                if (party.Gold != TavernRules.SeedGold || !game.Adventure.SeedMoneyPaid) Fail($"the deal paid {party.Gold} gold (paid={game.Adventure.SeedMoneyPaid}), not {TavernRules.SeedGold}");
                for (var i = 0; i < 6 && hud.DialogueOpen; i++)
                {
                    talk.Choose(0);
                    path.Add(talk.Node?.Id ?? "closed");
                }
                GD.Print($"[tavern] briefing to the end: {string.Join(" -> ", path)}; {State()}");
                if (hud.DialogueOpen) Fail("the briefing did not close after the sendoff");
                if (party.Gold != TavernRules.SeedGold) Fail($"the purse holds {party.Gold} after the briefing, not {TavernRules.SeedGold}");
                Next();
                break;
            }
            case 5 when _elapsed >= 8.0:
            {
                // 5. the way down: onto the landing at the bottom of the well
                var stairs = root.GetNodeOrNull<Stairs>("DownStairs");
                if (stairs == null) { Fail("no DownStairs in the tavern"); Finish(); return; }
                if (root.GetNodeOrNull<Stairs>("UpStairs") != null) Fail("the tavern has a way up");
                var foot = DungeonBuilder.WalkableNeighbor(layout, layout.DownStairs);
                party.Teleport(stairs.ToGlobal(new Vector3(0, -Stairs.Rise + 0.3f, -1.2f)), b.Surface(foot));
                GD.Print($"[tavern] on the down landing at {layout.DownStairs} (foot {foot}): {State()}");
                var expected = _tavernSkip ? 0 : TavernRules.SeedGold;
                if (party.Gold != expected || game.Adventure.SeedMoneyPaid != !_tavernSkip) Fail($"before the descent {State()}, expected gold {expected}");
                Next();
                break;
            }
            case 6 when hud.StairsPromptOpen || _elapsed >= 9.0:
            {
                var stairs = root.GetNodeOrNull<Stairs>("DownStairs");
                GD.Print($"[tavern] landing prompt={hud.StairsPromptOpen} question=\"{stairs?.Question}\"");
                if (!hud.StairsPromptOpen) { Fail("no question on the tavern's down landing"); Finish(); return; }
                Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = true }); // the prompt's Yes: Stairs.Confirm
                Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = false });
                Next();
                break;
            }
            case 7 when _elapsed >= 10.0:
                // 6. floor 1, staked exactly once
                GD.Print($"[tavern] after the descent: kind={LayoutKinds.Id(layout.Kind)} {State()}");
                if (game.CurrentFloor != 1) Fail($"the descent left the party on floor {game.CurrentFloor}");
                if (layout.Kind == LayoutKind.Tavern) Fail("floor 1 is still the tavern layout");
                if (party.Gold != TavernRules.SeedGold) Fail($"floor 1 opens with {party.Gold} gold, not {TavernRules.SeedGold} (paid once)");
                if (!game.Adventure.SeedMoneyPaid) Fail("the seed money is not marked paid on floor 1");
                var tavernRecord = game.Adventure.Memory.Floors.FirstOrDefault(f => f.Floor == 0);
                GD.Print($"[tavern] floor 0 record: {(tavernRecord == null ? "missing" : $"completed={tavernRecord.Completed} seconds={tavernRecord.Seconds:0.0}")}");
                if (tavernRecord is not { Completed: true }) Fail("leaving the tavern did not close its floor-0 record");
                Finish();
                break;
        }
    }

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

    // ---- --test-hit-fx ------------------------------------------------------------------------------------
    // What a blow looks and sounds like. Staged like --test-abilities: the party in the largest room, a stunned enemy
    // 1.5 m ahead of Brand, the others parked. A: Brand hits it and its hurt grunt plays; B: it is killed and its death
    // grunt plays; C: an enemy whose definition names a nonsense voice set speaks with the size rule's set, and a set with
    // no clips falls back to enemy_hurt / enemy_die. Cues are recorded from EventBus.Sfx. Then what a blow looks like:
    // D: a sword blow leaves one blood OneShotFx under the floor root that is gone 1.2 s later; E: Ilex's fireball bursts
    // (an OneShotFx with a light) on the enemy and again on a wall face, and no bolt is left; G: three of Wren's arrows stay
    // stuck in the enemy (StuckArrowCount 3, EmbeddedArrows 3), and an enemy restored with 40 draws six; H: the corpse keeps
    // its three and drops two arrows as before; F: a fireball and fresh
    // bursts are in the air when the party jumps to the next floor, which must load cleanly (the flow ends on floor 2).
    // Prints [hitfx] lines and RESULT.
    private bool _testHitFx;
    private int _hfxStep;
    private double _hfxNext;
    private int _hfxTries;
    private Enemy? _hfxEnemy;
    private Vector3 _hfxStand;
    private readonly List<string> _hfxCues = new();
    private readonly List<string> _hfxFails = new();
    private string _hfxHurt = "", _hfxDeath = "";
    private bool _hfxListening;
    private double _hfxSwungAt;
    private int _hfxBlood, _hfxFromFloor, _hfxShieldedBlood = -1;
    private Vector3 _hfxWallHit;
    private Node? _hfxOldRoot;
    private int _hfxShots, _hfxArrowDrops;
    private Enemy? _hfxRestored;

    private void HitFxCue(string cue) => _hfxCues.Add(cue);

    private void RunHitFxTest()
    {
        if (_hfxStep >= 99) return;
        if (_elapsed > 35) { Fail($"timed out at step {_hfxStep}"); Finish(); return; } // before the quit at 40 s, so a stall still prints RESULT
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        if (_elapsed < _hfxNext) return;
        var party = main.Party;
        var b = main.Builder;
        var brand = Math.Max(0, Array.FindIndex(party.Members, m => m.Name == "Brand"));
        void Fail(string why) { _hfxFails.Add(why); GD.Print($"[hitfx] FAIL: {why}"); }
        void Finish()
        {
            if (_hfxListening) { EventBus.Instance.Sfx -= HitFxCue; _hfxListening = false; }
            GD.Print(_hfxFails.Count == 0 ? "[hitfx] RESULT ok" : $"[hitfx] RESULT FAIL ({string.Join("; ", _hfxFails)})");
            _hfxStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        var ilex = Math.Max(0, Array.FindIndex(party.Members, m => m.Name == "Ilex"));
        var wren = Math.Max(0, Array.FindIndex(party.Members, m => m.Name == "Wren"));
        IEnumerable<LootDrop> ArrowDrops() => root.GetChildren().OfType<LootDrop>().Where(d => !d.IsQueuedForDeletion() && d.Item.DefId == DungeonCrawler.Party.PartyController.ArrowDefId);
        List<Combat.Fx.OneShotFx> Effects() => root.GetChildren().OfType<Combat.Fx.OneShotFx>().Where(f => !f.IsQueuedForDeletion()).ToList();
        static bool IsBurst(Combat.Fx.OneShotFx f) => f.GetChildren().OfType<OmniLight3D>().Any();
        Enemy? Spawn(string? voiceSet, int hero = -1)
        {
            foreach (var stray in root.GetChildren().OfType<Enemy>()) stray.QueueFree();
            party.Adventurers[hero < 0 ? brand : hero].Teleport(_hfxStand, _hfxStand + new Vector3(0, 0, -1.5f));
            var e = b.SpawnEnemy(b.RegularEnemies(b.CurrentFloor)[0], _hfxStand - Vector3.Up * 0.2f + new Vector3(0, 0, -1.5f), b.CurrentFloor);
            if (e == null) return null;
            if (voiceSet != null) e.OverrideVoiceSet(voiceSet);
            e.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Stun, 30f, 0f));
            e.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Shield, 60f, Combat.StatusEffectController.MaxShield)); // the hit lands but the body survives it
            return e;
        }
        bool HitLanded() => _hfxEnemy != null && IsInstanceValid(_hfxEnemy) && _hfxCues.Contains(_hfxEnemy.HurtCue);
        void Swing() { _hfxCues.Clear(); AbilitiesRefill(party.Members[brand]); party.Attack(brand); }
        switch (_hfxStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                var m = party.Members[brand];
                m.SkillModifiers.Add(new Characters.StatModifier(Characters.Stat.Accuracy, Characters.ModOp.Add, 1000f, "test"));
                m.Recalculate();
                AbilitiesTakeControl(brand);
                var room = layout.Rooms.OrderByDescending(r => r.W * r.H).First();
                _hfxStand = b.Surface(room.Center) + Vector3.Up * 0.3f;
                party.Teleport(_hfxStand, _hfxStand + new Vector3(0, 0, -1.5f));
                EventBus.Instance.Sfx += HitFxCue; _hfxListening = true;
                _hfxEnemy = Spawn(null);
                if (_hfxEnemy == null) { Fail("could not spawn an enemy"); Finish(); return; }
                GD.Print($"[hitfx] floor {b.CurrentFloor} seed {Game.Instance.GameSeed}: {m.Name} vs {_hfxEnemy.Profile.Id} (scale {_hfxEnemy.Profile.Scale:0.00}, voice \"{_hfxEnemy.Profile.VoiceSet}\" -> {_hfxEnemy.VoiceSet}) at {room.Center}");
                _hfxStep = 1; _hfxNext = _elapsed + 0.35; // the new body joins the physics world next step
                break;
            }
            case 1: // A: one blow
                Swing();
                _hfxStep = 2; _hfxNext = _elapsed + 0.6;
                break;
            case 2:
            {
                var e = _hfxEnemy!;
                if (!HitLanded() && _hfxTries++ < 2) { GD.Print($"[hitfx] no hurt cue yet (cues [{string.Join(", ", _hfxCues)}]), swinging again"); _hfxStep = 1; break; }
                _hfxHurt = e.HurtCue;
                var expected = VoiceSets.Resolve(e.Profile.VoiceSet, e.Profile.Scale, e.IsBoss);
                GD.Print($"[hitfx] A hurt: cue {_hfxHurt}, heard [{string.Join(", ", _hfxCues)}]");
                if (!_hfxCues.Contains(_hfxHurt)) Fail($"the hurt cue {_hfxHurt} did not play");
                if (e.VoiceSet != expected) Fail($"voice set {e.VoiceSet}, expected {expected}");
                if (Audio.AudioManager.Instance?.Has(VoiceSets.HurtCue(e.VoiceSet)) == true && !_hfxHurt.StartsWith("grunt_hurt_")) Fail($"the {e.VoiceSet} set has hurt clips but the cue is {_hfxHurt}");
                // B: the death
                _hfxCues.Clear();
                e.Kill();
                _hfxStep = 3; _hfxNext = _elapsed + 0.6;
                break;
            }
            case 3:
            {
                var e = _hfxEnemy!;
                _hfxDeath = e.DeathCue;
                GD.Print($"[hitfx] B death: cue {_hfxDeath}, alive={e.IsAlive}, heard [{string.Join(", ", _hfxCues)}]");
                if (!_hfxCues.Contains(_hfxDeath)) Fail($"the death cue {_hfxDeath} did not play");
                if (_hfxCues.Contains("boss") != e.IsBoss) Fail("the boss bell rang for a regular, or not for a boss");
                if (Audio.AudioManager.Instance?.Has(VoiceSets.DeathCue(e.VoiceSet)) == true && !_hfxDeath.StartsWith("grunt_death_")) Fail($"the {e.VoiceSet} set has death clips but the cue is {_hfxDeath}");
                // C: a definition naming a voice set that does not exist
                _hfxEnemy = Spawn("nonsense");
                if (_hfxEnemy == null) { Fail("could not spawn the second enemy"); Finish(); return; }
                _hfxTries = 0;
                _hfxStep = 4; _hfxNext = _elapsed + 0.35;
                break;
            }
            case 4:
                Swing();
                _hfxStep = 5; _hfxNext = _elapsed + 0.6;
                break;
            case 5:
            {
                var e = _hfxEnemy!;
                if (!HitLanded() && _hfxTries++ < 2) { GD.Print("[hitfx] no hurt cue yet on the second enemy, swinging again"); _hfxStep = 4; break; }
                var sizeRule = VoiceSets.DefaultFor(e.Profile.Scale, e.IsBoss);
                GD.Print($"[hitfx] C voice \"nonsense\" -> {e.VoiceSet} (size rule {sizeRule}), cue {e.HurtCue}, heard [{string.Join(", ", _hfxCues)}]");
                if (e.VoiceSet != sizeRule) Fail($"a nonsense voice set resolved to {e.VoiceSet}, not the size rule's {sizeRule}");
                if (!_hfxCues.Contains(e.HurtCue)) Fail($"the second enemy's hurt cue {e.HurtCue} did not play");
                // a set whose folder has no clips: what HurtCue / DeathCue hand back when AudioManager.Has is false
                var fallbackHurt = Enemy.CueOrFallback(VoiceSets.HurtCue(e.VoiceSet), "enemy_hurt", _ => false);
                var fallbackDeath = Enemy.CueOrFallback(VoiceSets.DeathCue(e.VoiceSet), "enemy_die", _ => false);
                var noAudio = Enemy.CueOrFallback(VoiceSets.HurtCue(e.VoiceSet), "enemy_hurt", null);
                if (fallbackHurt != "enemy_hurt" || fallbackDeath != "enemy_die" || noAudio != "enemy_hurt") Fail($"with no clips the cues are {fallbackHurt}/{fallbackDeath} (no audio: {noAudio})");
                GD.Print($"[hitfx] grunts: hurt={_hfxHurt} death={_hfxDeath} fallback={fallbackHurt}");
                // a set the definition names wins over the size rule
                e.OverrideVoiceSet(VoiceSets.Large);
                _hfxTries = 0;
                _hfxStep = 6;
                break;
            }
            case 6:
                Swing();
                _hfxStep = 7; _hfxNext = _elapsed + 0.6;
                break;
            case 7:
            {
                var e = _hfxEnemy!;
                if (!HitLanded() && _hfxTries++ < 2) { GD.Print("[hitfx] no large hurt cue yet, swinging again"); _hfxStep = 6; break; }
                var want = Audio.AudioManager.Instance?.Has(VoiceSets.HurtCue(VoiceSets.Large)) == true ? VoiceSets.HurtCue(VoiceSets.Large) : "enemy_hurt";
                var heard = _hfxCues.FirstOrDefault(c => c.StartsWith("grunt_hurt_") || c == "enemy_hurt") ?? "none";
                GD.Print($"[hitfx] D voice \"large\" -> {e.VoiceSet}, cue {e.HurtCue}, first hurt cue heard {heard}");
                if (e.VoiceSet != VoiceSets.Large) Fail($"a named set \"large\" resolved to {e.VoiceSet}");
                if (heard != want) Fail($"after naming the large set the hurt cue heard was {heard}, expected {want}");
                _hfxStep = 8; _hfxNext = _elapsed + 1.2; // the blood of the blows above has run its course
                break;
            }
            case 8: // D: blood
                _hfxEnemy = Spawn(null);
                if (_hfxEnemy == null) { Fail("could not spawn the enemy for blood"); Finish(); return; }
                _hfxTries = 0;
                _hfxStep = 9; _hfxNext = _elapsed + 0.35;
                break;
            case 9:
                Swing(); _hfxSwungAt = _elapsed;
                _hfxStep = 10;
                break;
            case 10: // every frame until the blow lands (hurt cue, or the death cue on a killing blow)
            {
                var e = _hfxEnemy!;
                var landed = IsInstanceValid(e) && (_hfxCues.Contains(e.HurtCue) || _hfxCues.Contains(e.DeathCue));
                if (!landed)
                {
                    if (_elapsed - _hfxSwungAt < 0.8) break;
                    if (_hfxTries++ < 2) { GD.Print("[hitfx] the blood blow has not landed yet, swinging again"); _hfxStep = 9; break; }
                    Fail("the blood blow never landed");
                }
                if (_hfxShieldedBlood < 0)
                {
                    // the first blow lands on the shield whole (0 damage): no blood; then the shield goes and the next one bleeds
                    _hfxShieldedBlood = Effects().Count;
                    if (IsInstanceValid(e)) e.Effects.Remove(Combat.StatusKind.Shield);
                    _hfxTries = 0;
                    _hfxStep = 9; _hfxNext = _elapsed + 1.0; // the shielded blow's effects (if any) run out first
                    break;
                }
                _hfxBlood = Effects().Count;
                _hfxStep = 11; _hfxNext = _elapsed + 1.2;
                break;
            }
            case 11:
            {
                var gone = Effects().Count == 0;
                GD.Print($"[hitfx] blood: shielded={_hfxShieldedBlood} spawned={_hfxBlood} gone={gone.ToString().ToLowerInvariant()} (blow at {_hfxSwungAt:0.00} s)");
                if (_hfxShieldedBlood != 0) Fail($"a blow the shield took whole left {_hfxShieldedBlood} effects, expected none");
                if (_hfxBlood != 1) Fail($"a sword blow left {_hfxBlood} effects, expected 1 (blood)");
                if (!gone) Fail("the blood was still there 1.2 s after the blow");
                // E: Ilex's fireball on the enemy
                AbilitiesTakeControl(ilex);
                party.Adventurers[brand].Teleport(_hfxStand + new Vector3(0, 0, 2.5f), _hfxStand + new Vector3(0, 0, 1f)); // out of Ilex's spot
                _hfxEnemy = Spawn(null, ilex);
                if (_hfxEnemy == null) { Fail("could not spawn the enemy for the fireball"); Finish(); return; }
                _hfxTries = 0;
                _hfxStep = 12; _hfxNext = _elapsed + 0.35;
                break;
            }
            case 12:
            {
                var m = party.Members[ilex];
                AbilitiesRefill(m);
                m.ActiveAbilityIndex = Math.Max(0, m.KnownAbilities.IndexOf("ability.fireball"));
                _hfxCues.Clear();
                party.Attack(ilex);
                _hfxStep = 13; _hfxNext = _elapsed + 0.4;
                break;
            }
            case 13:
            {
                var bursts = Effects().Count(IsBurst);
                var hurt = HitLanded();
                if ((!hurt || bursts == 0) && _hfxTries++ < 2) { GD.Print($"[hitfx] fireball: bursts={bursts} hurt={hurt}, casting again"); _hfxStep = 12; _hfxNext = _elapsed + 0.3; break; }
                GD.Print($"[hitfx] E fireball on the enemy: bursts={bursts} hurt={hurt.ToString().ToLowerInvariant()} (ability {party.Members[ilex].KnownAbilities.ElementAtOrDefault(party.Members[ilex].ActiveAbilityIndex)})");
                if (bursts < 1) Fail("the fireball left no burst on the enemy");
                if (!hurt) Fail("the fireball did not hurt the enemy");
                // and on a wall: the abilities flow's wall, a room cell with rock to the north
                GridPos? found = null;
                for (var y = 1; y < layout.Height && found == null; y++)
                    for (var x = 0; x < layout.Width && found == null; x++)
                    {
                        var c = new GridPos(x, y); var wall = new GridPos(x, y - 1);
                        if (layout[c] == Cell.Floor && layout[wall] == Cell.Wall && !layout.IsUnder(c) && !layout.IsUnder(wall) && layout.RoomAt(c) != null) found = c;
                    }
                if (found == null) { Fail("no room cell with a wall to the north for the wall burst"); Finish(); return; }
                if (_hfxEnemy != null && IsInstanceValid(_hfxEnemy)) _hfxEnemy.QueueFree();
                var m = party.Members[ilex];
                AbilitiesRefill(m);
                var stand = b.Surface(found.Value) + Vector3.Up * 0.3f;
                party.Adventurers[ilex].Teleport(stand, stand + new Vector3(0, 0, -2f));
                var cam = party.Adventurers[ilex].Camera;
                var rh = cam.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, cam.GlobalPosition - cam.GlobalTransform.Basis.Z * 6f, collisionMask: 1));
                if (rh.Count == 0) { Fail($"no wall within 6 m of the eye at cell {found.Value}"); Finish(); return; }
                _hfxWallHit = (Vector3)rh["position"];
                GD.Print($"[hitfx] wall from cell {found.Value}: the face is {_hfxWallHit.DistanceTo(cam.GlobalPosition):0.00} m from the eye");
                party.Attack(ilex);
                _hfxStep = 14; _hfxNext = _elapsed + 0.6;
                break;
            }
            case 14:
            {
                var bursts = Effects().Where(IsBurst).ToList();
                var closest = bursts.Select(f => f.GlobalPosition.DistanceTo(_hfxWallHit)).DefaultIfEmpty(float.PositiveInfinity).Min();
                var bolts = root.GetChildren().OfType<Combat.Projectile>().Count(p => !p.IsQueuedForDeletion());
                GD.Print($"[hitfx] E fireball on the wall: bursts={bursts.Count} closest={closest:0.00} m from the face, bolts left={bolts}");
                if (closest > 0.6f) Fail($"no burst within 0.6 m of the wall face (closest {closest:0.00} m)");
                if (bolts != 0) Fail($"{bolts} bolts still in the scene after hitting the wall");
                // G: Wren's arrows stay in the enemy they hit
                var w = party.Members[wren];
                w.SkillModifiers.Add(new Characters.StatModifier(Characters.Stat.Accuracy, Characters.ModOp.Add, 1000f, "test"));
                w.Recalculate();
                if (party.ArrowCount < 10) party.Bag.TryAdd(Game.Instance.Catalog.Create(DungeonCrawler.Party.PartyController.ArrowDefId, 20));
                AbilitiesTakeControl(wren);
                party.Adventurers[ilex].Teleport(_hfxStand + new Vector3(0, 0, 2.5f), _hfxStand + new Vector3(0, 0, 1f));
                party.Adventurers[brand].Teleport(_hfxStand + new Vector3(1.5f, 0, 2.5f), _hfxStand + new Vector3(1.5f, 0, 1f));
                _hfxEnemy = Spawn(null, wren);
                if (_hfxEnemy == null) { Fail("could not spawn the enemy for the arrows"); Finish(); return; }
                _hfxShots = 0;
                _hfxStep = 20; _hfxNext = _elapsed + 1.5; // the room's surface stands above the floor the bodies land on: both fall first, so the shots fly level
                break;
            }
            case 20: // G: three basic bow shots, 0.5 s apart
            {
                var m = party.Members[wren];
                AbilitiesRefill(m);
                m.ActiveAbilityIndex = Math.Max(0, m.KnownAbilities.IndexOf(Combat.AbilityProfile.BasicId));
                party.Attack(wren);
                _hfxShots++;
                if (_hfxShots < 3) { _hfxNext = _elapsed + 0.5; break; }
                _hfxStep = 21; _hfxNext = _elapsed + 0.6;
                break;
            }
            case 21:
            {
                var e = _hfxEnemy!;
                GD.Print($"[hitfx] G arrows: stuck={e.StuckArrowCount} embedded={e.EmbeddedArrows} model={(e.Model == null ? "none" : e.Model.Name)} alive={e.IsAlive}");
                if (e.Model == null) Fail($"{e.Profile.Id} has no model to stick arrows in");
                if (e.StuckArrowCount != 3) Fail($"three arrows hit but {e.StuckArrowCount} are drawn stuck");
                if (e.EmbeddedArrows != 3) Fail($"three arrows hit but EmbeddedArrows is {e.EmbeddedArrows}");
                if (e.Model != null) GD.Print($"[hitfx] G arrow heights above the feet: {string.Join(", ", e.Model.GetChildren().OfType<Node3D>().Where(c => c.Name.ToString().Contains(Combat.Fx.HitFx.StuckArrowName)).Select(a => $"{a.GlobalPosition.Y - e.GlobalPosition.Y:0.00}"))} m; eye {party.Adventurers[wren].Camera.GlobalPosition.Y - e.GlobalPosition.Y:0.00} m");
                if (e.Model != null)
                    foreach (var a in e.Model.GetChildren().OfType<Node3D>().Where(c => c.Name.ToString().Contains(Combat.Fx.HitFx.StuckArrowName)))
                    {
                        var len = a.GlobalTransform.Basis.Z.Length();
                        if (Math.Abs(len - 1f) > 0.01f) Fail($"a stuck arrow is scaled {len:0.00} in the world, expected 1");
                        if (a.GlobalPosition.DistanceTo(e.GlobalPosition + Vector3.Up * 0.9f * e.Profile.Scale) > 1.5f * e.Profile.Scale) Fail($"a stuck arrow is {a.GlobalPosition.DistanceTo(e.GlobalPosition):0.00} m from the body");
                    }
                // a pincushion from a save: 40 arrows in, six drawn
                var at = _hfxStand - Vector3.Up * 0.2f + new Vector3(2.5f, 0, -1.5f);
                _hfxRestored = b.SpawnEnemy(b.RegularEnemies(b.CurrentFloor)[0], at, b.CurrentFloor);
                if (_hfxRestored == null) { Fail("could not spawn the enemy to restore"); Finish(); return; }
                _hfxRestored.RestoreFrom(new Save.EnemyDto { SpawnIndex = _hfxRestored.SpawnIndex, Hp = _hfxRestored.MaxHp, EmbeddedArrows = 40, X = at.X, Y = at.Y, Z = at.Z });
                _hfxStep = 22; _hfxNext = _elapsed + 0.05; // a frame passes
                break;
            }
            case 22:
            {
                var r = _hfxRestored!;
                var drawn = r.Model?.GetChildren().Count(c => c.Name.ToString().Contains(Combat.Fx.HitFx.StuckArrowName)) ?? 0; // queued ones too: none may linger
                GD.Print($"[hitfx] G restored with 40 arrows: stuck={r.StuckArrowCount} children={drawn} embedded={r.EmbeddedArrows}");
                if (r.StuckArrowCount != Enemy.MaxStuckArrows) Fail($"an enemy restored with 40 arrows draws {r.StuckArrowCount}, expected {Enemy.MaxStuckArrows}");
                if (drawn > Enemy.MaxStuckArrows) Fail($"an enemy restored with 40 arrows holds {drawn} arrow nodes");
                if (r.EmbeddedArrows != 40) Fail($"the restored enemy's EmbeddedArrows is {r.EmbeddedArrows}, expected 40");
                r.QueueFree();
                _hfxRestored = null;
                // H: the corpse keeps its arrows, and half of them (rounded up) drop as before
                _hfxArrowDrops = ArrowDrops().Sum(d => d.Item.StackCount);
                _hfxEnemy!.Kill();
                _hfxStep = 23; _hfxNext = _elapsed + 0.6;
                break;
            }
            case 23:
            {
                var e = _hfxEnemy!;
                var stuck = IsInstanceValid(e) ? e.StuckArrowCount : -1;
                var dropped = ArrowDrops().Sum(d => d.Item.StackCount) - _hfxArrowDrops;
                var want = (3 + 1) / 2;
                GD.Print($"[hitfx] H corpse: alive={(IsInstanceValid(e) && e.IsAlive).ToString().ToLowerInvariant()} stuck={stuck} arrows dropped={dropped} (expected {want})");
                if (IsInstanceValid(e) && e.IsAlive) Fail("the arrow enemy did not die");
                if (stuck != 3) Fail($"the corpse holds {stuck} arrows, expected 3");
                if (dropped != want) Fail($"{dropped} arrows dropped, expected {want}");
                _hfxStep = 24;
                break;
            }
            case 24:
            {
                // F: a bolt and fresh effects in the air when the floor changes under them
                AbilitiesTakeControl(ilex);
                AbilitiesRefill(party.Members[ilex]);
                party.Attack(ilex);
                var eye = party.Adventurers[ilex].Camera.GlobalPosition;
                Combat.Fx.HitFx.SpellBurst(root, eye + new Vector3(0, 0, -1f), new Color(1f, 0.5f, 0.1f));
                Combat.Fx.HitFx.Blood(root, eye + new Vector3(0.3f, 0, -1f), Vector3.Back);
                _hfxOldRoot = root;
                _hfxFromFloor = b.CurrentFloor;
                var inAir = Effects().Count;
                var flying = root.GetChildren().OfType<Combat.Projectile>().Count();
                GD.Print($"[hitfx] F jumping from floor {_hfxFromFloor} with {inAir} effects and {flying} bolts in the air");
                Game.Instance.JumpToFloor(_hfxFromFloor + 1);
                _hfxSwungAt = _elapsed;
                _hfxStep = 15; _hfxNext = _elapsed + 1.0;
                break;
            }
            case 15:
            {
                if (b.CurrentFloor != _hfxFromFloor + 1 || root == _hfxOldRoot) { if (_elapsed - _hfxSwungAt < 10) break; Fail("the next floor never loaded"); Finish(); return; }
                var oldGone = _hfxOldRoot == null || !IsInstanceValid(_hfxOldRoot);
                GD.Print($"[hitfx] F floor {_hfxFromFloor} -> {b.CurrentFloor} loaded, old floor freed={oldGone.ToString().ToLowerInvariant()}, effects on the new floor={Effects().Count}");
                if (!oldGone) Fail("the old floor root was not freed");
                _hfxStep = 16; _hfxNext = _elapsed + 4.0; // the music's crossfade into the new floor settles, or the quit leaves both streams held
                break;
            }
            case 16:
                Finish();
                break;
        }
    }

    // --test-abilities: every hero learns every skill of their class, then uses every ability they know (and a heavy swing
    // for melee weapons) on a fresh enemy standing 1.5 m ahead. Costs are checked the moment the ability fires; a second
    // later the enemy's health and statuses, or the party's buffs, are checked. The three heroes not under test are parked
    // so nobody else hits the enemy. Ends with a fireball and an arrow shot straight into a wall. Prints RESULT.
    private bool _testAbilities;
    private int _abilStep;
    private double _abilNext;
    private readonly List<(int Hero, string Ability, bool Heavy)> _abilPlan = new();
    private int _abilIndex, _abilTries, _abilFails;
    private Enemy? _abilEnemy;
    private float _abilEnemyHp, _abilEnemyShield;
    private string _abilCostNote = "";
    private Vector3 _abilStand, _abilWallStand;
    private Vector3 _abilCastFrom;

    private void AbilitiesTakeControl(int hero)
    {
        var party = Main.Instance!.Party;
        var adv = party.Adventurers[hero];
        if (adv.Parked) adv.SetControl(DungeonCrawler.Party.ControlKind.Companion);
        party.Possess(hero);
        for (var i = 0; i < party.Adventurers.Length; i++) if (i != hero) party.Adventurers[i].SetControl(DungeonCrawler.Party.ControlKind.Parked);
    }

    private static void AbilitiesRefill(Characters.CharacterState m)
    {
        m.Effects.Clear();
        m.Recalculate();
        m.Hp = m.Stats[Characters.Stat.MaxHp]; m.Mana = m.Stats[Characters.Stat.MaxMana]; m.Stamina = m.Stats[Characters.Stat.MaxStamina];
        m.AbilityCooldowns.Clear(); m.CooldownRemaining = 0;
    }

    /// <summary>The statuses this use is expected to leave on the enemy (chance 1 only): the weapon's for swings, the ability's otherwise.</summary>
    private static List<Combat.StatusApplication> AbilitiesExpectedOnHit(Characters.CharacterState m, Combat.AbilityProfile a)
    {
        var list = new List<Combat.StatusApplication>();
        if (a.Kind is Combat.AbilityKind.WeaponAttack or Combat.AbilityKind.MeleeStrike) list.AddRange(m.WeaponProfile.OnHit);
        if (a.Attack != null && a.Kind != Combat.AbilityKind.WeaponAttack) list.AddRange(a.Attack.OnHit);
        return list.Where(x => x.Chance >= 1f && x.Duration > 0).ToList();
    }

    private void RunAbilitiesTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        if (_elapsed < _abilNext) return;
        var party = main.Party;
        var b = main.Builder;
        var skills = Game.Instance.Skills;
        switch (_abilStep)
        {
            case 0 when _elapsed >= 1.0:
                _abilStep = 1; _abilNext = _elapsed + 1.0;
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                for (var i = 0; i < party.Members.Length; i++)
                {
                    var m = party.Members[i];
                    m.SkillPoints = 99;
                    var learned = true;
                    while (learned)
                    {
                        learned = false;
                        foreach (var sk in skills.ForClass(m.Class.Id).ToList())
                            if (Characters.SkillRules.WhyCannotLearn(m, sk) == null && Characters.SkillRules.Learn(m, sk, skills)) learned = true;
                    }
                    m.SkillModifiers.Add(new Characters.StatModifier(Characters.Stat.Accuracy, Characters.ModOp.Add, 1000f, "test"));
                    m.Recalculate();
                    foreach (var id in m.KnownAbilities) _abilPlan.Add((i, id, false));
                    if (!m.WeaponProfile.IsRanged) _abilPlan.Add((i, Combat.AbilityProfile.BasicId, true));
                    GD.Print($"[abilities] {m.Name} ({m.Class.Name}, {m.WeaponProfile.Kind}): skills={m.LearnedSkills.Count} abilities=[{string.Join(", ", m.KnownAbilities.Select(party.AbilityName))}]");
                }
                var room = layout.Rooms.OrderByDescending(r => r.W * r.H).First();
                _abilStand = b.Surface(room.Center) + Vector3.Up * 0.3f;
                party.Teleport(_abilStand, _abilStand + new Vector3(0, 0, -1.5f));
                GD.Print($"[abilities] {_abilPlan.Count} uses planned; standing at {room.Center} {_abilStand:0.0}");
                break;
            case 1: // arm: a fresh enemy, a full hero, one use
            {
                if (_abilIndex >= _abilPlan.Count) { _abilStep = 4; _abilNext = _elapsed + 0.5; break; }
                var (hero, id, heavy) = _abilPlan[_abilIndex];
                var m = party.Members[hero];
                AbilitiesTakeControl(hero);
                foreach (var other in party.Members) AbilitiesRefill(other);
                party.Adventurers[hero].Teleport(_abilStand, _abilStand + new Vector3(0, 0, -1.5f));
                if (party.ArrowCount < 10) party.Bag.TryAdd(Game.Instance.Catalog.Create(DungeonCrawler.Party.PartyController.ArrowDefId, 20));
                foreach (var stray in root.GetChildren().OfType<Enemy>()) stray.QueueFree(); // the test enemy from last time and anything that wandered in
                var a = party.GetAbilityFor(m, id);
                var expect = AbilitiesExpectedOnHit(m, a);
                _abilEnemy = b.SpawnEnemy(b.RegularEnemies(b.CurrentFloor)[0], _abilStand - Vector3.Up * 0.2f + new Vector3(0, 0, -1.5f), b.CurrentFloor);
                if (_abilEnemy == null) { GD.Print("[abilities] FAIL could not spawn an enemy"); _abilFails++; _abilIndex++; break; }
                if (expect.All(x => x.Kind != Combat.StatusKind.Stun)) _abilEnemy.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Stun, 30f, 0f));
                _abilEnemy.Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Shield, 60f, Combat.StatusEffectController.MaxShield)); // soaks the blow so the body survives and its statuses can be read
                _abilEnemyHp = _abilEnemy.Hp;
                // the new body joins the physics world next step: fire only then, or a swing finds nothing
                _abilStep = 2; _abilNext = _elapsed + 0.35;
                break;
            }
            case 2: // fire: one use, costs checked the moment it goes off
            {
                var (hero, id, heavy) = _abilPlan[_abilIndex];
                var m = party.Members[hero];
                var a = party.GetAbilityFor(m, id);
                if (_abilEnemy == null || !IsInstanceValid(_abilEnemy)) { _abilStep = 1; break; }
                _abilEnemyHp = _abilEnemy.Hp; _abilEnemyShield = _abilEnemy.Effects.ShieldPoints;
                var mana0 = m.Mana; var stam0 = m.Stamina; var arrows0 = party.ArrowCount;
                m.ActiveAbilityIndex = Math.Max(0, m.KnownAbilities.IndexOf(id));
                party.Attack(hero, heavy);
                // costs are paid the moment the ability fires, before any regen
                var manaCost = a.ManaCost; var stamCost = Combat.AbilityRules.StaminaCost(m, a, heavy); var arrowCost = Combat.AbilityRules.ArrowCost(m, a);
                var costs = new List<string>();
                var killedNow = !_abilEnemy.IsAlive;
                var siphon = killedNow ? party.Trigger(m, Characters.TriggerKind.SoulSiphon) : 0f; // a kill hands mana back through Soul Siphon
                if (manaCost > 0) { var paid = mana0 - m.Mana; var ok = Math.Abs(paid - manaCost) < 0.01f || (siphon > 0 && Math.Abs(paid + siphon - manaCost) < 0.01f); costs.Add($"mana {manaCost:0} {(ok ? (paid < manaCost ? $"OK (siphon +{siphon:0})" : "OK") : $"FAIL(paid {paid:0.#})")}"); if (!ok) _abilFails++; }
                if (stamCost > 0) { var ok = Math.Abs((stam0 - m.Stamina) - stamCost) < 0.01f; costs.Add($"stamina {stamCost:0} {(ok ? "OK" : $"FAIL(paid {stam0 - m.Stamina:0.#})")}"); if (!ok) _abilFails++; }
                if (arrowCost > 0) { var ok = arrows0 - party.ArrowCount == arrowCost; costs.Add($"arrows {arrowCost} {(ok ? "OK" : $"FAIL(used {arrows0 - party.ArrowCount})")}"); if (!ok) _abilFails++; }
                var cd = a.Cooldown > 0 ? (m.AbilityCooldown(a.Id) > 0 ? "cooldown OK" : "cooldown FAIL(not started)") : "";
                if (cd.Contains("FAIL")) _abilFails++;
                if (cd != "") costs.Add(cd);
                _abilCostNote = string.Join(", ", costs);
                _abilStep = 3; _abilNext = _elapsed + 1.0;
                break;
            }
            case 3: // check: what the use did
            {
                var (hero, id, heavy) = _abilPlan[_abilIndex];
                var m = party.Members[hero];
                var a = party.GetAbilityFor(m, id);
                var label = $"{m.Name} {a.Name}{(heavy ? " (heavy)" : "")} [{a.Kind}{(a.UsesWeapon ? $", {m.WeaponProfile.Kind}" : "")}]";
                var notes = new List<string>();
                if (_abilCostNote != "") notes.Add(_abilCostNote);
                var fails = 0;
                switch (a.Kind)
                {
                    case Combat.AbilityKind.SelfBuff:
                        foreach (var ef in a.Effects) { var ok = m.Effects.Has(ef.Kind); notes.Add($"self {ef.Kind} {(ok ? "OK" : "FAIL")}"); if (!ok) fails++; }
                        break;
                    case Combat.AbilityKind.PartyBuff:
                        foreach (var ef in a.Effects)
                        {
                            var missing = party.Members.Where(x => x.IsAlive && !x.Effects.Has(ef.Kind)).Select(x => x.Name).ToList();
                            notes.Add($"party {ef.Kind} {(missing.Count == 0 ? "OK" : $"FAIL(missing {string.Join(",", missing)})")}"); if (missing.Count > 0) fails++;
                        }
                        break;
                    default:
                    {
                        var alive = _abilEnemy != null && IsInstanceValid(_abilEnemy) && _abilEnemy.IsAlive;
                        var hp = alive ? _abilEnemy!.Hp : 0f;
                        var shield = alive ? _abilEnemy!.Effects.ShieldPoints : 0f;
                        var hit = !alive || hp < _abilEnemyHp - 0.01f || shield < _abilEnemyShield - 0.01f;
                        if (!hit && _abilTries < 2)
                        {
                            _abilTries++;
                            GD.Print($"[abilities] {label}: no hit registered, trying again ({_abilTries})");
                            _abilStep = 1; _abilNext = _elapsed + 0.2;
                            return;
                        }
                        notes.Add(alive ? $"enemy hp {_abilEnemyHp:0}->{hp:0} shield {_abilEnemyShield:0}->{shield:0} {(hit ? "HIT" : "FAIL(no damage)")}" : $"enemy {_abilEnemyHp:0}->dead HIT");
                        if (!hit) fails++;
                        if (alive)
                            foreach (var st in AbilitiesExpectedOnHit(m, a)) { var ok = _abilEnemy!.Effects.Has(st.Kind); notes.Add($"{st.Kind} {(ok ? "OK" : "FAIL(not applied)")}"); if (!ok) fails++; }
                        else if (AbilitiesExpectedOnHit(m, a).Count > 0) notes.Add("statuses n/a (dead)");
                        break;
                    }
                }
                _abilFails += fails;
                GD.Print($"[abilities] {label}: {string.Join(" | ", notes)}");
                _abilIndex++; _abilTries = 0;
                _abilStep = 1; _abilNext = _elapsed + 0.2;
                break;
            }
            case 4: // the wall: a fireball from Ilex straight into solid rock from 2 m
            case 6: // and an arrow from Wren
            {
                var hero = _abilStep == 4 ? 3 : 2;
                if (_abilStep == 4)
                {
                    GridPos? found = null;
                    for (var y = 1; y < layout.Height && found == null; y++)
                        for (var x = 0; x < layout.Width && found == null; x++)
                        {
                            var c = new GridPos(x, y); var wall = new GridPos(x, y - 1);
                            if (layout[c] == Cell.Floor && layout[wall] == Cell.Wall && !layout.IsUnder(c) && !layout.IsUnder(wall) && layout.RoomAt(c) != null) found = c;
                        }
                    if (found == null) { GD.Print("[abilities] wall test: no room cell with a wall to the north; skipped"); _abilStep = 8; break; }
                    _abilWallStand = b.Surface(found.Value) + Vector3.Up * 0.3f;
                    GD.Print($"[abilities] wall test from cell {found.Value}: the wall face is 2 m ahead");
                }
                if (_abilEnemy != null && IsInstanceValid(_abilEnemy)) _abilEnemy.QueueFree();
                var m = party.Members[hero];
                AbilitiesTakeControl(hero);
                AbilitiesRefill(m);
                party.Adventurers[hero].Teleport(_abilWallStand, _abilWallStand + new Vector3(0, 0, -2f));
                m.ActiveAbilityIndex = Math.Max(0, m.KnownAbilities.IndexOf(hero == 3 ? "ability.fireball" : Combat.AbilityProfile.BasicId));
                _abilCastFrom = party.Adventurers[hero].Camera.GlobalPosition;
                var camNode = party.Adventurers[hero].Camera;
                var rq = PhysicsRayQueryParameters3D.Create(camNode.GlobalPosition, camNode.GlobalPosition - camNode.GlobalTransform.Basis.Z * 6f, collisionMask: 1);
                var rh = camNode.GetWorld3D().DirectSpaceState.IntersectRay(rq);
                GD.Print($"[abilities] wall ray from the eye: {(rh.Count > 0 ? $"solid at {((Vector3)rh["position"]).DistanceTo(camNode.GlobalPosition):0.00} m ({((GodotObject)rh["collider"]).GetType().Name})" : "nothing within 6 m")}");
                party.Attack(hero);
                _abilStep++; _abilNext = _elapsed + 0.6;
                break;
            }
            case 5:
            case 7:
            {
                var what = _abilStep == 5 ? "fireball" : "arrow";
                var flying = root.GetChildren().OfType<Combat.Projectile>().ToList();
                var far = flying.Select(p => p.GlobalPosition.DistanceTo(_abilCastFrom)).DefaultIfEmpty(0f).Max();
                var penetrated = flying.Count > 0 && far > 3f;
                GD.Print($"[abilities] {what} vs wall: {(penetrated ? $"PENETRATED ({flying.Count} in flight, {far:0.0} m past the caster)" : "STOPPED")}");
                if (penetrated) _abilFails++;
                foreach (var p in flying) p.QueueFree();
                _abilStep++; _abilNext = _elapsed + 0.3;
                break;
            }
            case 8:
                _abilStep = 9;
                GD.Print($"[abilities] RESULT {(_abilFails == 0 ? "ok" : $"FAIL ({_abilFails} problems)")}");
                _quitAfter = _elapsed + 0.5;
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

    // Story: an Investigate quest (clues), a Decide quest (a remembered flag), a Betrayal (the NPC turns), an escort Betrayal (the NPC flees to the creatures), and a flag-gated NPC on the next floor.
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
                var rooms = menu.Rooms.Where(r => Authorable(r) && !r.IsExit && r.FreeCells >= 3).OrderBy(r => r.HopsFromStart).ToList(); // the validator drops an NPC or a clue in the safe room, the shrine or the jail
                // clues may lie in any room with a free cell, the exit room included; rooms without an NPC first (a flooded floor has few with any)
                var npcRooms = rooms.Take(3).Select(r => r.Index).ToHashSet();
                var clueRooms = menu.Rooms.Where(r => Authorable(r) && r.FreeCells > 0).OrderBy(r => npcRooms.Contains(r.Index) ? 1 : 0).ThenByDescending(r => r.FreeCells).Take(2).Select(r => r.Index).ToList();
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
                var runaway = new NpcPlan { Id = "runaway", Name = "Ash", Room = rooms[Math.Min(3, rooms.Count - 1)].Index, QuestId = "q_runaway", Betrayal = new BetrayalPlan { EnemyId = creature, Count = 2, Escort = true } };
                runaway.Script.Start = "go";
                runaway.Script.Nodes.Add(new DialogueNode { Id = "go", Line = new Line("They have come for me. I am going."), Choices =
                {
                    new ChoicePlan { Text = "Stay.", Effect = DialogueEffect.Betray },
                    new ChoicePlan { Text = "Go, then.", Effect = DialogueEffect.Farewell },
                } });
                plan.Npcs.Add(runaway);
                plan.Quests.Add(new QuestPlan { Id = "q_runaway", Template = QuestTemplate.Betrayal, Title = "Ash's Escort", GiverNpc = "runaway", Reward = new QuestReward { Xp = 40 } });
                var report = PlanValidator.Validate(layout, plan, menu);
                GD.Print($"[story] floor 3 authored: {report} npcs={plan.Npcs.Count} quests={plan.Quests.Count} clues={string.Join(",", clueRooms)} betrayer={creature} {string.Join("; ", report.Repairs)}");
                if (plan.Quests.Count != 4) Fail("validator dropped a story quest");
                game.Adventure.FloorPlans[3] = plan;
                var plan4 = Design(4, out var menu4, out var layout4);
                var rooms4 = menu4.Rooms.Where(r => Authorable(r) && r.FreeCells >= 2).OrderBy(r => r.HopsFromStart).ToList();
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
                // the escort betrayal: the NPC flees, its creatures keep their own names, the log says so
                var logged = new List<string>();
                void Note(string text) => logged.Add(text);
                EventBus.Instance.Message += Note;
                Talk("runaway", d => d.Choices.ToList().FindIndex(c => c.Effect == DialogueEffect.Betray));
                EventBus.Instance.Message -= Note;
                var escort = root.GetChildren().OfType<Enemy>().Where(e => e.QuestGroupId == QuestMachine.BetrayalGroup("runaway")).ToList();
                var ash = NpcById("runaway");
                GD.Print($"[story] runaway fled={ash?.Fled} visible={ash?.Visible} creatures={escort.Count} named=[{string.Join(",", escort.Select(t => t.DisplayName))}] quest={Q(3, "q_runaway")?.Status} objective=\"{Q(3, "q_runaway")?.ObjectiveText}\" log=[{string.Join(" | ", logged)}]");
                if (ash?.Fled != true || ash.Visible) Fail("escort betrayer did not flee");
                if (escort.Count != 2) Fail("escort creatures missing");
                if (escort.Any(t => t.DisplayName.Contains("Ash"))) Fail("an escort creature wears the fled NPC's name");
                if (!logged.Contains("Ash runs, and the enemy comes for you.")) Fail("the fled wording was not logged");
                if (logged.Any(l => l.Contains("drops the act") || l.Contains("never on your side"))) Fail("the turncoat wording was logged for an escort");
                if (Q(3, "q_runaway")?.ObjectiveText.Contains("has fled") != true) Fail("escort quest objective does not say fled");
                foreach (var t in escort) t.Kill();
                break;
            }
            case 2 when _elapsed >= 3.5:
            {
                _storyStep++;
                GD.Print($"[story] after killing the turned: {Q(3, "q_guide")?.Status}; memory choices=[{string.Join("; ", game.Adventure.Memory.For(3).Choices)}] fates=[{string.Join("; ", game.Adventure.Memory.For(3).NpcFates)}]");
                if (Q(3, "q_guide")?.Status != QuestStatus.Completed) Fail("betrayal quest did not complete when its creatures died");
                if (Q(3, "q_runaway")?.Status != QuestStatus.Completed) Fail("escort betrayal quest did not complete when its creatures died");
                var fates = game.Adventure.Memory.For(3).NpcFates;
                if (!fates.Contains("Ash fled to the enemy") || fates.Any(f => f.StartsWith("Ash") && (f.EndsWith("died") || f.Contains("turned")))) Fail("the escort NPC's fate is not a flight");
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
        return $"floor={Game.Instance.CurrentFloor} enemies={enemies} loot={loot} openedChests={chests} bag=[{string.Join(",", party.Bag.Items.Select(i => i.DisplayName))}] hp={string.Join("/", party.Members.Select(m => m.Hp.ToString("0")))} pos=({pos.X:0.0},{pos.Z:0.0}) explored={main.Exploration?.Seen.Count} gold={party.Gold}";
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

    /// <summary>
    /// One step of --pose-sheet: save the pose shown two frames ago, show the next one, or equip the next weapon and queue
    /// every pose of its light, backhand and heavy swings (and the --pose-skills moves) as DIR/NN_item_move_K.png.
    /// </summary>
    private void PoseSheetStep(Party.Adventurer lead)
    {
        if (_poseFrames-- > 0) return;
        if (_posePending != null) { SaveScreenshot(_posePending); _posePending = null; }
        if (_poseShots.Count > 0)
        {
            var (file, key) = _poseShots.Dequeue();
            lead.FirstPerson.HoldPose(key);
            _posePending = file;
            _poseFrames = 2;
            return;
        }
        var cat = Game.Instance.Catalog;
        _poseItems ??= cat.Bases.Values.Where(b => b.Weapon != null).Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (++_poseIndex >= _poseItems.Length) { GD.Print($"[pose-sheet] done: {_poseItems.Length} weapons in {_poseSheetDir}"); _done = true; GetTree().Quit(); return; }
        var id = _poseItems[_poseIndex];
        if (!cat.TryBase(id, out _)) { GD.Print($"[pose-sheet] unknown item {id}"); return; }
        var item = cat.Create(id);
        lead.State.Equipment.Equip(Items.EquipSlot.Weapon, item);
        lead.State.Recalculate();
        var style = Items.WeaponStyles.Of(item);
        var moves = new List<Items.WeaponMove?> { Items.WeaponMoves.Attack(style, false, 0), Items.WeaponMoves.Attack(style, false, 1), Items.WeaponMoves.Attack(style, true) };
        var abilities = Registry.Instance.All<Characters.Defs.AbilityDef>().Select(d => d.ToProfile()).ToDictionary(a => a.Id);
        foreach (var skill in _poseSkills) if (abilities.TryGetValue(skill, out var a)) moves.Add(Items.WeaponMoves.Skill(a.Id, a.Kind, style));
        var shortId = id.Replace("item.", "");
        Directory.CreateDirectory(_poseSheetDir!);
        _poseShots.Enqueue((Path.Combine(_poseSheetDir!, $"{_poseIndex:00}_{shortId}_rest_0.png"), new Items.MoveKey(0, 0, 0, 0, 0, 0, 0)));
        foreach (var move in moves.OfType<Items.WeaponMove>())
            for (var k = 0; k < move.Keys.Length - 1; k++)
                _poseShots.Enqueue((Path.Combine(_poseSheetDir!, $"{_poseIndex:00}_{shortId}_{move.Name.Replace(' ', '-')}_{k}.png"), move.Keys[k]));
        GD.Print($"[pose-sheet] {id}: {style}, {_poseShots.Count} poses");
        _poseFrames = 3; // let the view swap to the new model first
    }

    private void SaveScreenshot(string path)
    {
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"[debug] screenshot {path}: {err}");
    }

    // ---- --test-elites-and-bosses -----------------------------------------------------------------------
    // Floor 8, seed 8: the two rolled elites by name, the same two after a save/load with a curse's extras pending, Warded absorbing and regrowing,
    // Volatile bursting (also under the Press), Thorned paying melee back, Hexed slowing, the Shrouded dome stopping a bolt and an outside nova,
    // x2.5 XP and a table.elite drop, a kiting archer, a shielding shaman, a bomber; floor 5: the Crypt Lord roaring at half, phase two speed,
    // Slam and Summon alternating, the braziers out, phase surviving a load; floor 23 no boss; floor 25 the First Herald; floor 27 silent portals.
    private bool _testElites;
    private int _elStep;
    private double _elAt;
    private readonly List<string> _elFails = new();
    private readonly List<(int Index, string Name)> _elRoster = new();
    private readonly HashSet<string> _elStates = new();
    private readonly List<LootDrop> _elDrops = new();
    private Enemy? _elEnemy, _elEnemy2;
    private float _elLeadHp, _elWard;
    private int _elSlain, _elRoars, _elLevel, _elXp;
    private bool _elShroudHurt, _elWardLog, _elSpinesLog, _elSubscribed;
    private Combat.Projectile? _elBolt;
    private Vector3 _elAxis = Vector3.Right, _elHome;
    private float _elLeadDamage;
    private readonly List<Party.Adventurer> _elFrozen = new();

    private void RunElitesAndBossesTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        var party = main.Party;
        var b = main.Builder;
        var since = _elapsed - _elAt;
        void Next() { _elStep++; _elAt = _elapsed; }
        void Fail(string why) { _elFails.Add(why); GD.Print($"[elites] FAIL: {why}"); }
        void Thaw() { foreach (var a in _elFrozen.Where(IsInstanceValid)) a.ProcessMode = ProcessModeEnum.Inherit; _elFrozen.Clear(); }
        void Freeze() { Thaw(); foreach (var a in party.Active.Where(a => a != party.Lead)) { a.ProcessMode = ProcessModeEnum.Disabled; _elFrozen.Add(a); } } // only the test's own blows land
        void Finish()
        {
            Thaw();
            GD.Print(_elFails.Count == 0 ? "[elites] RESULT ok" : $"[elites] RESULT FAIL ({string.Join("; ", _elFails)})");
            _elStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        if (_elStep < 99 && since > 30) { Fail($"step {_elStep} stuck for {since:0}s"); Finish(); return; }
        var lead = party.Lead;
        float MaxHpOf(Party.Adventurer a) => a.State.Stats[Characters.Stat.MaxHp];
        void Heal(Party.Adventurer a) { a.State.Hp = MaxHpOf(a); a.State.Effects.Clear(); }
        Vector3 Along(Vector3 from, float metres) { var at = from + _elAxis * metres; while (metres > 2f && !layout.IsWalkable(DungeonBuilder.WorldToCell(at))) { metres -= 1f; at = from + _elAxis * metres; } return at; }
        Enemy? Spawn(string id, Vector3 at, EliteRoll? elite = null) { var e = b.SpawnEnemy(id, at, b.CurrentFloor, elite: elite); if (e == null) Fail($"could not spawn {id}"); return e; }
        DamageResult Hit(Enemy e, float dmg) => e.ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = dmg, MaxDamage = dmg }, new HitContext { DefenderStunned = true }, party.Rng, "the test");
        int XpGained(Party.Adventurer a) { var gained = a.State.Xp - _elXp; for (var l = _elLevel; l < a.State.Level; l++) gained += Characters.Leveling.XpToNext(l); return gained; }
        List<Enemy> Elites() => root.GetChildren().OfType<Enemy>().Where(e => e.IsElite && e.SpawnIndex >= 0).OrderBy(e => e.SpawnIndex).ToList();
        Enemy? Boss() => root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.IsBoss && e.IsAlive);
        Stairs? Down() => root.GetNodeOrNull<Stairs>("DownStairs");
        List<Brazier> Braziers() => root.GetChildren().OfType<Brazier>().Where(x => x.Name.ToString().StartsWith("ArenaBrazier")).ToList();
        bool Lit(Brazier br) => br.GetChildren().OfType<Fire>().Any(f => f.Visible);
        if (!_elSubscribed)
        {
            _elSubscribed = true;
            var bus = EventBus.Instance;
            bus.EnemyHurt += (e, amount, flavor, by) => { if (e == _elEnemy && _elStep is 12 or 13) { _elShroudHurt = true; GD.Print($"[elites] Shrouded was hurt: {amount:0.0} {flavor} by member {by}, {e.State.Name}, {e.GlobalPosition.DistanceTo(Main.Instance!.Party.Lead.GlobalPosition):0.0} m from the lead; bolt alive={_elBolt != null && IsInstanceValid(_elBolt)} at {( _elBolt != null && IsInstanceValid(_elBolt) ? _elBolt.GlobalPosition.DistanceTo(e.GlobalPosition) : -1f):0.0} m from it"); } };
            bus.EliteSlain += _ => _elSlain++;
            bus.LootSpawned += d => _elDrops.Add(d);
            bus.Message += m => { if (m.Contains("ward shatters")) _elWardLog = true; if (m.Contains("spines")) _elSpinesLog = true; if (m.Contains("roars!")) _elRoars++; };
            bus.MemberDamaged += (i, amount) => { if (Main.Instance?.Party.Lead is { } l && i == l.Index) _elLeadDamage += amount; };
        }
        switch (_elStep)
        {
            case 0 when _elapsed >= 1.0 && b.CurrentFloor == 8:
            {
                var elites = Elites();
                var seed = Rng.FloorSeed(Game.Instance.GameSeed, 8);
                GD.Print($"[elites] floor 8 seed {Game.Instance.GameSeed}: {elites.Count} elites: {string.Join("; ", elites.Select(e => $"#{e.SpawnIndex} {e.DisplayName} ({EliteRules.Describe(e.Profile.Affixes)})"))}");
                if (elites.Count != EliteRules.CountFor(8, 0)) Fail($"floor 8 has {elites.Count} elites, the schedule says {EliteRules.CountFor(8, 0)}");
                foreach (var e in elites)
                {
                    var roll = EliteRules.Roll(seed, 8, e.SpawnIndex, b.EnemyProfile(e.Profile.Id)?.Faction ?? "");
                    if (e.Profile.EliteName != roll.Name || !e.Profile.Affixes.SequenceEqual(roll.Affixes)) Fail($"#{e.SpawnIndex} is {e.Profile.EliteName}, the roll says {roll.Name}");
                    _elRoster.Add((e.SpawnIndex, e.DisplayName));
                }
                if (Game.Instance.Floors.TryGetValue(8, out var early) && early.EliteSpawns.Count > 0) Fail("the floor state already lists elite spawns before any capture");
                Next();
                break;
            }
            case 1:
                main.SaveGame("elites");
                DungeonBuilder.ExtraElitesPerFloor = 2; // as if Marked had been taken after the save
                main.LoadGame("elites");
                Next();
                break;
            case 2 when since >= 1.5 && b.CurrentFloor == 8:
            {
                DungeonBuilder.ExtraElitesPerFloor = 0;
                var elites = Elites();
                var now = elites.Select(e => (e.SpawnIndex, e.DisplayName)).ToList();
                GD.Print($"[elites] after save/load: {string.Join("; ", now.Select(n => $"#{n.SpawnIndex} {n.DisplayName}"))}");
                if (!now.SequenceEqual(_elRoster)) Fail("the loaded floor does not seat the same elites at the same spawns");
                var saved = Game.Instance.Floors.TryGetValue(8, out var st) ? st.EliteSpawns.OrderBy(x => x).ToList() : new List<int>();
                if (!saved.SequenceEqual(_elRoster.Select(r => r.Index))) Fail($"FloorStateDto.EliteSpawns is [{string.Join(",", saved)}], expected [{string.Join(",", _elRoster.Select(r => r.Index))}]");
                Next();
                break;
            }
            case 3:
            {
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree(); // a quiet floor
                var big = layout.Rooms.Select((r, i) => (r, i)).OrderByDescending(t => t.r.Area).First();
                _elAxis = big.r.W >= big.r.H ? Vector3.Right : Vector3.Back;
                var cell = PlanResolver.Anchor(layout, big.i) ?? big.r.Center;
                _elHome = b.Surface(cell) + Vector3.Up * 0.3f;
                party.Teleport(_elHome, _elHome + _elAxis);
                Freeze();
                Next();
                break;
            }
            case 4 when since >= 0.5:
                foreach (var a in party.Active) Heal(a);
                _elEnemy = Spawn("enemy.ghoul", Along(lead.GlobalPosition, 4f), new EliteRoll(new[] { EliteAffix.Warded }, "Test, Warded"));
                Next();
                break;
            case 5 when since >= 0.2:
            {
                if (_elEnemy == null) { Next(); break; }
                var e = _elEnemy;
                _elWard = e.WardPoints;
                var expected = Math.Min(e.MaxHp * EliteRules.WardFraction, StatusEffectController.MaxShield);
                GD.Print($"[elites] Warded: ward {_elWard:0.0} of max HP {e.MaxHp:0} (expected {expected:0.0})");
                if (_elWard <= 0f || Math.Abs(_elWard - expected) > 0.5f) Fail($"ward is {_elWard:0.0}, expected {expected:0.0}");
                Hit(e, 5f);
                GD.Print($"[elites] Warded: after a 5 hit: hp {e.Hp:0}/{e.MaxHp:0}, ward {e.WardPoints:0.0}");
                if (e.Hp < e.MaxHp) Fail("a 5 hit got through the ward");
                if (!(e.WardPoints < _elWard && e.WardPoints >= _elWard - 5.5f)) Fail($"the ward took {_elWard - e.WardPoints:0.0} from a 5 hit");
                var heavy = e.WardPoints + e.MaxHp * 0.4f;
                Hit(e, heavy);
                GD.Print($"[elites] Warded: after a {heavy:0} hit: hp {e.Hp:0}/{e.MaxHp:0}, ward {e.WardPoints:0.0}");
                if (e.WardPoints > 0f) Fail($"a {heavy:0} hit left the ward standing");
                if (e.Hp >= e.MaxHp || !e.IsAlive) Fail($"a {heavy:0} hit on the ward should wound but not kill");
                Next();
                break;
            }
            case 6 when since >= 0.3:
                if (!_elWardLog) Fail("no 'ward shatters' line");
                Next();
                break;
            case 7 when since >= EliteRules.WardRegrowSeconds + 0.5:
                GD.Print($"[elites] Warded: ward after {since:0.0}s: {_elEnemy?.WardPoints:0.0}");
                if (_elEnemy == null || _elEnemy.WardPoints <= 0f) Fail("the ward did not regrow");
                _elEnemy?.Kill();
                Next();
                break;
            case 8 when since >= 0.5:
                Heal(lead);
                _elLeadDamage = 0f;
                _elEnemy = Spawn("enemy.ghoul", Along(lead.GlobalPosition, 1.5f), new EliteRoll(new[] { EliteAffix.Volatile }, "Test, Volatile"));
                _elEnemy?.Kill();
                Next();
                break;
            case 9 when since >= 0.3:
                GD.Print($"[elites] Volatile: lead took {_elLeadDamage:0.0}, burn={lead.State.Effects.Has(StatusKind.Burn)}");
                if (_elLeadDamage <= 0f) Fail("the Volatile burst did not hurt the lead");
                if (!lead.State.Effects.Has(StatusKind.Burn)) Fail("the Volatile burst did not burn the lead");
                Heal(lead);
                _elLeadDamage = 0f;
                _elEnemy = Spawn("enemy.ghoul", Along(lead.GlobalPosition, 1.5f), new EliteRoll(new[] { EliteAffix.Volatile }, "Test, Volatile"));
                _elEnemy?.KillByEnvironment("the test press", HazardDamage.Crush);
                Next();
                break;
            case 10 when since >= 0.3:
                GD.Print($"[elites] Volatile under the press: lead took {_elLeadDamage:0.0}");
                if (_elLeadDamage <= 0f) Fail("a hazard kill did not detonate the Volatile elite");
                Next();
                break;
            case 11:
            {
                Heal(lead);
                var thorned = Spawn("enemy.ghoul", Along(lead.GlobalPosition, 1.2f), new EliteRoll(new[] { EliteAffix.Thorned }, "Test, Thorned"));
                if (thorned != null)
                {
                    _elLeadDamage = 0f;
                    var r = thorned.ReceiveAttack(lead.State.Stats, lead.State.WeaponProfile.ToAttack(), new HitContext { DefenderStunned = true }, party.Rng, lead.State.Name);
                    party.OnHitLanded(lead.State, thorned, r);
                    GD.Print($"[elites] Thorned: {lead.State.Name} hit for {r.Amount:0.0}, spines paid back {_elLeadDamage:0.0}, spines line={_elSpinesLog}");
                    if (!r.Hit) Fail("the melee test hit missed");
                    else if (_elLeadDamage < r.Amount * EliteRules.ThornedFraction * 0.5f) Fail($"spines paid back {_elLeadDamage:0.0} of a {r.Amount:0.0} hit");
                    if (!_elSpinesLog) Fail("no 'spines' line");
                    thorned.Kill();
                }
                Heal(lead);
                var hexed = Spawn("enemy.ghoul", Along(lead.GlobalPosition, 1.2f), new EliteRoll(new[] { EliteAffix.Hexed }, "Test, Hexed"));
                if (hexed != null)
                {
                    var r = hexed.ReceiveAttack(lead.State.Stats, lead.State.WeaponProfile.ToAttack(), new HitContext { DefenderStunned = true }, party.Rng, lead.State.Name);
                    party.OnHitLanded(lead.State, hexed, r);
                    GD.Print($"[elites] Hexed: hit={r.Hit}, lead slowed={lead.State.Effects.Has(StatusKind.Slow)}");
                    if (!lead.State.Effects.Has(StatusKind.Slow)) Fail("a melee hit on the Hexed elite did not slow the striker");
                    hexed.Kill();
                }
                Heal(lead);
                Next();
                break;
            }
            case 12 when since >= 0.5:
            {
                var at = Along(lead.GlobalPosition, 8f);
                if (at.DistanceTo(lead.GlobalPosition) < 6f) { Fail("no room for the dome test"); Next(); break; }
                _elEnemy = Spawn("enemy.ghoul", at, new EliteRoll(new[] { EliteAffix.Shrouded }, "Test, Shrouded"));
                _elShroudHurt = false;
                if (_elEnemy != null)
                {
                    var from = lead.GlobalPosition + Vector3.Up * 1.2f;
                    var dir = (_elEnemy.GlobalPosition + Vector3.Up * 0.9f - from).Normalized();
                    GD.Print($"[elites] Shrouded: {_elEnemy.DisplayName} [{EliteRules.Describe(_elEnemy.Profile.Affixes)}] at {from.DistanceTo(_elEnemy.GlobalPosition):0.0} m, shrouds registered {Enemy.Shrouds.Count}, a straight bolt blocked={Enemy.ShroudBlocks(from, _elEnemy.GlobalPosition, null)}");
                    _elBolt = GD.Load<PackedScene>("res://scenes/Props/Projectile.tscn").Instantiate<Combat.Projectile>();
                    root.AddChild(_elBolt);
                    _elBolt.GlobalPosition = from + dir * 0.6f;
                    _elBolt.Setup(party, lead.State, lead.State.WeaponProfile.ToAttack() with { Kind = AttackKind.Ranged }, dir * 20f, true, Colors.White, lead);
                }
                Next();
                break;
            }
            case 13 when since >= 1.0:
            {
                var freed = _elBolt == null || !IsInstanceValid(_elBolt);
                GD.Print($"[elites] Shrouded: bolt freed={freed}, shrouded hurt={_elShroudHurt}");
                if (!freed) { Fail("the arrow is still flying after a second"); _elBolt?.QueueFree(); }
                if (_elShroudHurt) Fail("an arrow from outside the dome hit the Shrouded elite");
                if (_elEnemy != null)
                {
                    var s = _elEnemy.GlobalPosition;
                    var inside = s - _elAxis * 2f; var outside = s - _elAxis * 7f;
                    var sepIn = Enemy.ShroudSeparates(inside, s); var sepOut = Enemy.ShroudSeparates(outside, s);
                    GD.Print($"[elites] Shrouded: nova from 2 m separated={sepIn}, from 7 m separated={sepOut}");
                    if (sepIn) Fail("a nova cast inside the dome is blocked");
                    if (!sepOut) Fail("a nova cast from outside the dome reaches in");
                    _elEnemy.Kill();
                }
                Next();
                break;
            }
            case 14 when since >= 0.5:
            {
                party.Teleport(_elHome, _elHome + _elAxis);
                _elEnemy = Spawn("enemy.ghoul", Along(lead.GlobalPosition, 3f), new EliteRoll(new[] { EliteAffix.Swift }, "Test the Swift"));
                _elLevel = lead.State.Level; _elXp = lead.State.Xp; _elSlain = 0; _elDrops.Clear();
                _elEnemy?.Kill();
                Next();
                break;
            }
            case 15 when since >= 0.5:
            {
                if (_elEnemy == null) { Next(); break; }
                var expected = (int)MathF.Round(_elEnemy.Profile.ScaledXp(8) * EliteRules.XpMultiplier);
                var gained = XpGained(lead);
                var items = _elDrops.Where(d => IsInstanceValid(d) && d.Coins == 0).ToList();
                var coins = _elDrops.Any(d => IsInstanceValid(d) && d.Coins > 0);
                GD.Print($"[elites] reward: xp +{gained} (expected {expected}), drops [{string.Join(", ", items.Select(d => $"{d.Item} ({d.Item.Rarity})"))}], coins={coins}, EliteSlain={_elSlain}, table.elite loaded={Game.Instance.Catalog.Tables.ContainsKey("table.elite")}");
                if (gained != expected) Fail($"an elite kill gave {gained} XP, expected {expected}");
                if (!items.Any(d => d.Item.Rarity != Rarity.Common)) Fail("no non-Common drop from the elite");
                if (!coins) Fail("no coins from the elite");
                if (_elSlain != 1) Fail($"EliteSlain fired {_elSlain} times");
                Next();
                break;
            }
            case 16 when since >= 0.5:
                _elEnemy = Spawn("enemy.skeleton_archer", Along(lead.GlobalPosition, 2f));
                _elEnemy?.ForceAggro();
                _elStates.Clear();
                Next();
                break;
            case 17:
            {
                if (_elEnemy == null || !IsInstanceValid(_elEnemy)) { Fail("the archer vanished"); Next(); break; }
                _elStates.Add(_elEnemy.State.Name);
                var dist = _elEnemy.GlobalPosition.DistanceTo(lead.GlobalPosition);
                if (_elStates.Contains("Kite") && dist > 3.5f) { GD.Print($"[elites] kite: archer kited to {dist:0.0} m after {since:0.0}s (states {string.Join(",", _elStates)})"); _elEnemy.Kill(); Next(); }
                else if (since >= 3.0) { Fail($"the archer never kited (states {string.Join(",", _elStates)}, dist {dist:0.0})"); _elEnemy.Kill(); Next(); }
                break;
            }
            case 18 when since >= 0.5:
            {
                var far = Along(lead.GlobalPosition, 14f);
                _elEnemy = Spawn("enemy.cave_shaman", far);
                _elEnemy2 = Spawn("enemy.ghoul", far - _elAxis * 3f);
                if (_elEnemy2 != null) for (var i = 0; i < 12 && _elEnemy2.Hp > _elEnemy2.MaxHp * 0.4f; i++) Hit(_elEnemy2, _elEnemy2.Hp - _elEnemy2.MaxHp * 0.4f + 1f);
                _elEnemy?.ForceAggro(); _elEnemy2?.ForceAggro();
                _elStates.Clear();
                Next();
                break;
            }
            case 19:
            {
                if (_elEnemy == null || _elEnemy2 == null || !IsInstanceValid(_elEnemy) || !IsInstanceValid(_elEnemy2)) { Fail("the shaman or its ally vanished"); Next(); break; }
                _elStates.Add(_elEnemy.State.Name);
                if (_elEnemy2.Effects.Has(StatusKind.Shield) && _elStates.Contains("Support")) { GD.Print($"[elites] support: the ghoul is shielded after {since:0.0}s (shaman states {string.Join(",", _elStates)})"); _elEnemy.Kill(); _elEnemy2.Kill(); Next(); }
                else if (since >= 10.0) { Fail($"the shaman never shielded its ally (states {string.Join(",", _elStates)}, ally hp {_elEnemy2.Hp:0}/{_elEnemy2.MaxHp:0})"); _elEnemy.Kill(); _elEnemy2.Kill(); Next(); }
                break;
            }
            case 20 when since >= 0.5:
                Heal(lead);
                _elLeadDamage = 0f;
                _elDrops.Clear();
                _elEnemy = Spawn("enemy.horde_husk_bomber", Along(lead.GlobalPosition, 3f));
                _elEnemy?.ForceAggro();
                Next();
                break;
            case 21:
            {
                var gone = _elEnemy == null || !IsInstanceValid(_elEnemy);
                if (gone) { GD.Print($"[elites] bomber: gone after {since:0.0}s, lead took {_elLeadDamage:0.0}, drops {_elDrops.Count}"); if (_elLeadDamage <= 0f) Fail("the bomber's blast did not hurt the lead"); if (_elDrops.Count > 0) Fail("the bomber left loot"); Next(); }
                else if (since >= 4.0) { Fail($"the bomber never went off (state {_elEnemy!.State.Name}, dist {_elEnemy.GlobalPosition.DistanceTo(lead.GlobalPosition):0.0})"); _elEnemy.QueueFree(); Next(); }
                break;
            }
            case 22:
                Thaw();
                Game.Instance.JumpToFloor(5);
                Next();
                break;
            case 23 when since >= 1.5 && b.CurrentFloor == 5:
            {
                var boss = Boss();
                if (boss == null || layout.BossArena is not { } arena) { Fail("no boss on floor 5"); Finish(); return; }
                var braziers = Braziers();
                GD.Print($"[elites] boss {boss.DisplayName}: sig {boss.Profile.Signature}/{boss.Profile.SecondSignature}, arena {boss.Profile.ArenaChange}, braziers {braziers.Count} lit {braziers.Count(Lit)}");
                if (boss.Profile.SecondSignature != Signature.Summon) Fail($"the Crypt Lord's second signature is {boss.Profile.SecondSignature}");
                if (boss.Profile.ArenaChange != ArenaChange.Darken) Fail($"the Crypt Lord's arena change is {boss.Profile.ArenaChange}");
                if (braziers.Count != 4 || braziers.Count(Lit) != 4) Fail($"{braziers.Count} arena braziers, {braziers.Count(Lit)} lit");
                foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => !e.IsBoss)) e.QueueFree();
                foreach (var a in party.Active) { a.State.Hp = MaxHpOf(a) * 50; } // stand through the fight
                var spot = b.Surface(arena.Center) + new Vector3(0, 0.3f, -4f);
                party.Teleport(spot, boss.GlobalPosition);
                Freeze();
                for (var i = 0; i < _elFrozen.Count; i++) _elFrozen[i].GlobalPosition = spot + new Vector3(0.9f * (i - 1), 0, -1.2f); // a frozen body stays where it is put: beside the lead, in the boss's reach
                for (var i = 0; i < 20 && boss.Hp > boss.MaxHp * 0.45f && !boss.Untargetable; i++) Hit(boss, boss.Hp - boss.MaxHp * 0.45f);
                var r = Hit(boss, 1f);
                GD.Print($"[elites] boss at {boss.Hp / boss.MaxHp:P0}: state {boss.State.Name}, untargetable={boss.Untargetable}, further hit={r.Hit}");
                if (boss.State.Name != "Roar") Fail($"the boss is in {boss.State.Name}, not Roar");
                if (!boss.Untargetable) Fail("the roaring boss can be targeted");
                if (r.Hit) Fail("a hit landed during the roar");
                Next();
                break;
            }
            case 24 when since >= 1.5:
            {
                var boss = Boss();
                if (boss == null) { Fail("the boss vanished"); Finish(); return; }
                var braziers = Braziers();
                var expectedSpeed = boss.Profile.MoveSpeed * boss.Effects.Multiplier(Characters.Stat.MoveSpeed) * (boss.InWater ? Enemy.WaterSlow : 1f) * BossRules.PhaseTwoSpeed;
                GD.Print($"[elites] after the roar: phase {boss.Phase}, speed {boss.EffectiveMoveSpeed:0.00} (expected {expectedSpeed:0.00}, base {boss.Profile.MoveSpeed:0.00}), lit braziers {braziers.Count(Lit)}, roars {_elRoars}");
                if (boss.Phase != 2) Fail("the boss is not in phase two");
                if (Math.Abs(boss.EffectiveMoveSpeed - expectedSpeed) > boss.Profile.MoveSpeed * 0.05f) Fail($"phase two speed is {boss.EffectiveMoveSpeed:0.00}, expected {expectedSpeed:0.00}");
                if (braziers.Count(Lit) != 0) Fail("the braziers are still lit in phase two");
                if (_elRoars != 1) Fail($"{_elRoars} roar lines");
                _elStates.Clear();
                boss.ForceSignature();
                Next();
                break;
            }
            case 25:
            {
                var boss = Boss();
                if (boss == null) { Fail("the boss died in the signature test"); Finish(); return; }
                _elStates.Add(boss.State.Name);
                if (boss.State.Name == "Chase" && boss.GlobalPosition.DistanceTo(lead.GlobalPosition) <= 9f) boss.ForceSignature();
                if ((int)(since * 2) != (int)((since - GetProcessDeltaTime()) * 2) && (int)(since * 2) % 4 == 0)
                    GD.Print($"[elites] boss watch {since:0.0}s: {boss.State.Name}, {boss.GlobalPosition.DistanceTo(lead.GlobalPosition):0.0} m from the lead, target={boss.HasTarget}, los={boss.HasLineOfSight()}, dist-to-party={boss.DistanceToParty():0.0}");
                var husks = root.GetChildren().OfType<Enemy>().Count(e => e.Profile.Id == "enemy.horde_husk");
                if (_elStates.Contains("Signature:Slam") && _elStates.Contains("Signature:Summon") && husks > 0)
                {
                    GD.Print($"[elites] signatures after {since:0.0}s: {string.Join(",", _elStates)}; husks summoned {husks}");
                    foreach (var e in root.GetChildren().OfType<Enemy>().Where(e => !e.IsBoss)) e.QueueFree();
                    _elRoars = 0;
                    Thaw();
                    main.SaveGame("boss");
                    main.LoadGame("boss");
                    Next();
                }
                else if (since >= 20.0) { Fail($"phase two signatures: {string.Join(",", _elStates)}, husks {husks}"); Finish(); return; }
                break;
            }
            case 26 when since >= 1.5 && b.CurrentFloor == 5:
            {
                var boss = Boss();
                if (boss == null) { Fail("no boss after the load"); Finish(); return; }
                Freeze();
                GD.Print($"[elites] loaded boss at {boss.Hp / boss.MaxHp:P0}: phase {boss.Phase}, lit braziers {Braziers().Count(Lit)}, roars since load {_elRoars}");
                if (boss.Phase != 2) Fail("a boss loaded below half is not in phase two");
                if (Braziers().Count(Lit) != 0) Fail("the braziers relit on load");
                if (_elRoars != 0) Fail("the boss roared again on load");
                boss.Kill();
                Next();
                break;
            }
            case 27 when since >= 0.5:
                GD.Print($"[elites] boss dead: stairs locked={Down()?.Locked}, victory={Game.Instance.Victory}");
                if (Down()?.Locked != false) Fail("the stairs stayed locked after the boss died");
                if (Game.Instance.Victory) Fail("victory declared on floor 5");
                Next();
                break;
            case 28:
                Thaw();
                Game.Instance.JumpToFloor(23);
                Next();
                break;
            case 29 when since >= 1.5 && b.CurrentFloor == 23:
                GD.Print($"[elites] floor 23: boss={Boss()?.DisplayName ?? "none"}, stairs locked={Down()?.Locked}");
                if (Boss() != null) Fail("floor 23 has a boss");
                if (Down()?.Locked != false) Fail("floor 23's stairs are locked");
                Game.Instance.JumpToFloor(25);
                Next();
                break;
            case 30 when since >= 1.5 && b.CurrentFloor == 25:
            {
                var boss = Boss();
                GD.Print($"[elites] floor 25: boss={boss?.DisplayName ?? "none"} sig {boss?.Profile.Signature}/{boss?.Profile.SecondSignature} arena {boss?.Profile.ArenaChange}, stairs locked={Down()?.Locked}, BossFloorFor(25)={PortalRules.BossFloorFor(25)}");
                if (boss == null) { Fail("floor 25 has no boss"); Finish(); return; }
                if (boss.DisplayName != "the First Herald") Fail($"floor 25's boss is {boss.DisplayName}");
                if (boss.Profile.SecondSignature == Signature.None) Fail("the Herald has no second signature");
                if (boss.Profile.ArenaChange != ArenaChange.None) Fail("the Herald changes the arena");
                if (Down()?.Locked != true) Fail("floor 25's stairs are not sealed");
                if (PortalRules.BossFloorFor(25) != 25) Fail("floor 25 is not its own boss floor for the portals");
                if (Game.Instance.Floors.TryGetValue(25, out var st) && st.BossDefeated) Fail("floor 25 counts its boss dead already");
                boss.Kill();
                Next();
                break;
            }
            case 31 when since >= 0.5:
                GD.Print($"[elites] Herald dead: stairs locked={Down()?.Locked}, victory={Game.Instance.Victory}, defeated this floor={main.BossDefeatedThisFloor}");
                if (!main.BossDefeatedThisFloor) Fail("the Herald's death was not counted");
                if (Down()?.Locked != false) Fail("the stairs stayed locked after the Herald died");
                if (Game.Instance.Victory) Fail("victory declared on floor 25");
                Game.Instance.JumpToFloor(27);
                Next();
                break;
            case 32 when since >= 1.5 && b.CurrentFloor == 27:
            {
                var defeated = Game.Instance.Floors.TryGetValue(25, out var st) && st.BossDefeated;
                var portals = root.GetChildren().OfType<PortalSpawner>().FirstOrDefault();
                string why = "no spawner";
                if (portals != null) { NoPortals = false; portals.RefreshSilence(); why = portals.SilencedWhy; NoPortals = true; portals.RefreshSilence(); }
                GD.Print($"[elites] floor 27: floor 25 BossDefeated={defeated}, portals silenced because \"{why}\"");
                if (!defeated) Fail("floor 25's state does not record the Herald's death");
                if (!why.Contains("floor 25")) Fail($"portals on 27 are not silenced by the Herald of 25 (\"{why}\")");
                Finish();
                break;
            }
        }
    }

    // ---- --test-co-op --------------------------------------------------------------------------------------
    // Friends, the solo-provable half: the ping key is bound to X and the markers node exists; a ping places a mark and a
    // Label3D with the lead's name that the cooldown refuses to repeat and that expires after 2 s; the ready-check's rule
    // table (pure); the HUD line; the chat line in solo writing to the log; and solo stairs still changing floor at once.
    // The wire side (a ping relayed, one player's stairs at once) is scripts/net_test.sh; the multi-player vote is the
    // engine-free ReadyCheckTests.
    private bool _testCoOp;
    private int _coStep;
    private double _coAt, _coPingAt;
    private readonly List<string> _coFails = new();

    private void RunCoOpTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame || main.Hud is not { } hud) return;
        var party = main.Party;
        var b = main.Builder;
        var since = _elapsed - _coAt;
        void Next() { _coStep++; _coAt = _elapsed; }
        void Fail(string why) { _coFails.Add(why); GD.Print($"[co-op] FAIL: {why}"); }
        void Finish()
        {
            GD.Print(_coFails.Count == 0 ? "[co-op] RESULT ok" : $"[co-op] RESULT FAIL ({string.Join("; ", _coFails)})");
            _coStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        var pings = UI.Hud.PingMarkers.Instance;
        bool LabelFor(string name) => root.GetChildren().OfType<Label3D>().Any(l => l.Text == name && !l.IsQueuedForDeletion());
        switch (_coStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                // 1. the key and the node
                var bound = Settings.Rebindable.Any(r => r.action == "ping");
                var inMap = InputMap.HasAction("ping");
                var key = Settings.KeyFor("ping");
                GD.Print($"[co-op] ping key: rebindable={bound} inInputMap={inMap} key={key} markers={(pings != null)}");
                if (!bound) Fail("\"ping\" is not in Settings.Rebindable");
                if (!inMap) Fail("no \"ping\" input action");
                if (key != Key.X) Fail($"ping is bound to {key}, not X");
                if (pings == null) { Fail("no PingMarkers node under the HUD"); Finish(); return; }
                // 2. a ping at a creature lands on the creature, not on the wall behind it: stand 2 m in front of the nearest one, looking at it
                var target = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsBoss).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(party.Local!.GlobalPosition)).FirstOrDefault();
                if (target == null) Fail("no creature on the floor to ping at");
                else
                {
                    var toward = (party.Local!.GlobalPosition - target.GlobalPosition) with { Y = 0 };
                    var stand = target.GlobalPosition + (toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward) * 2f;
                    party.Teleport(stand with { Y = target.GlobalPosition.Y + 0.3f }, target.GlobalPosition + Vector3.Up * 1.0f);
                    var hit = hud.Ping();
                    var at = pings.Board.Active.FirstOrDefault(m => m.Name == party.Local!.State.Name);
                    var gap = (new Vector3(at.X, 0, at.Z) - target.GlobalPosition with { Y = 0 }).Length(); // the hit is on the body's surface, above its feet: judge the footprint
                    GD.Print($"[co-op] ping at a creature: {target.DisplayName} {target.GlobalPosition:0.0}, mark {at.X:0.0},{at.Y:0.0},{at.Z:0.0}, {gap:0.0} m from its footprint (returned {hit})");
                    if (!hit) Fail("Hud.Ping() returned false in front of a creature");
                    else if (gap > 1.5f) Fail($"the ping at a creature landed {gap:0.0} m from it (the wall behind it?)");
                    pings.Board.Clear();
                }
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree(); // a quiet floor from here
                Next();
                break;
            }
            case 1 when since >= Net.PingRules.CooldownSeconds + 0.2:
            {
                // 3. a ping from beside the up stairs, facing them (the flight's parapets are within the aim ray)
                var me = party.Local!;
                party.Teleport(b.Surface(DungeonBuilder.WalkableNeighbor(layout, layout.UpStairs)) + Vector3.Up * 0.3f, b.Surface(layout.UpStairs));
                var pinged = hud.Ping();
                _coPingAt = _elapsed;
                var mark = pings.Board.Active.FirstOrDefault(m => m.Name == me.State.Name);
                var label = LabelFor(me.State.Name);
                GD.Print($"[co-op] ping: \"{me.State.Name}\" at {mark.X:0.0},{mark.Y:0.0},{mark.Z:0.0} (returned {pinged}, marks {pings.Board.Active.Count}, label {label})");
                if (!pinged) Fail("Hud.Ping() returned false beside the up stairs");
                if (pings.Board.Active.Count != 1 || mark.Name != me.State.Name) Fail("the board does not hold one mark with the lead's name");
                if (!label) Fail("no Label3D with the lead's name under the floor root");
                Next();
                break;
            }
            case 2 when since >= 0.3:
            {
                var again = hud.Ping();
                GD.Print($"[co-op] ping again at +{_elapsed - _coPingAt:0.0} s: {again} (cooldown {Net.PingRules.CooldownSeconds} s)");
                if (again) Fail("a second ping inside the 1 s cooldown was not refused");
                Next();
                break;
            }
            case 3 when _elapsed - _coPingAt >= Net.PingRules.LifetimeSeconds + 0.3:
            {
                var name = party.Local!.State.Name;
                var label = LabelFor(name);
                GD.Print($"[co-op] ping expired after {Net.PingRules.LifetimeSeconds} s: marks {pings!.Board.Active.Count}, label {label}");
                if (pings.Board.Active.Count != 0) Fail("the mark did not expire after 2 s");
                if (label) Fail("the Label3D did not go with the mark");
                // 3. the ready-check's rule table (pure)
                int[] three = { 0, 1, 2 }, two = { 0, 1 };
                var rows = new List<(string Case, Net.ReadyVerdict Got, Net.ReadyVerdict Want)>();
                var c = new Net.ReadyCheck(); c.Vote(0, true, "stairs:down", 1, three, 0); c.Vote(1, true, "stairs:down", 1, three, 1);
                rows.Add(("3 living all Yes", c.Vote(2, true, "stairs:down", 1, three, 2), Net.ReadyVerdict.Go));
                c = new Net.ReadyCheck(); c.Vote(0, true, "stairs:down", 1, three, 0);
                rows.Add(("1 of 3 at 15 s", c.Tick(three, 15), Net.ReadyVerdict.Cancelled));
                c = new Net.ReadyCheck(); c.Vote(0, true, "stairs:down", 1, three, 0); c.Vote(1, true, "stairs:down", 1, three, 1);
                rows.Add(("2 of 3 at 15 s", c.Tick(three, 15), Net.ReadyVerdict.Go));
                c = new Net.ReadyCheck(); c.Vote(0, true, "stairs:down", 1, three, 0);
                rows.Add(("Yes then a No", c.Vote(1, false, "stairs:down", 1, three, 1), Net.ReadyVerdict.Cancelled));
                c = new Net.ReadyCheck(); c.Vote(0, true, "stairs:down", 1, two, 0);
                rows.Add(("1 of 2 at 15 s", c.Tick(two, 15), Net.ReadyVerdict.Cancelled));
                c = new Net.ReadyCheck(); c.Vote(0, true, "stairs:down", 1, three, 0);
                rows.Add(("a downed player's No", c.Vote(5, false, "stairs:down", 1, three, 1), Net.ReadyVerdict.Pending));
                foreach (var row in rows)
                {
                    GD.Print($"[co-op] rule: {row.Case} -> {row.Got}");
                    if (row.Got != row.Want) Fail($"rule \"{row.Case}\" gave {row.Got}, wanted {row.Want}");
                }
                GD.Print($"[co-op] rule: Needed(1)={Net.ReadyCheckRules.Needed(1)} Needed(2)={Net.ReadyCheckRules.Needed(2)}");
                if (Net.ReadyCheckRules.Needed(1) || !Net.ReadyCheckRules.Needed(2)) Fail("Needed is wrong for 1 or 2 players");
                // 4. the HUD line
                hud.ShowReadyCheck(1, 3, 12, +1);
                var line = hud.ReadyCheckText;
                hud.HideReadyCheck();
                GD.Print($"[co-op] hud line: \"{line}\" then \"{hud.ReadyCheckText}\"");
                if (line != "Descend? 1/3 ready · 12 s") Fail($"the HUD line reads \"{line}\"");
                if (hud.ReadyCheckText != "") Fail("the HUD line did not hide");
                // 5. chat in solo
                hud.OpenChat();
                var opened = party.InputLocked;
                hud.SubmitChat("hello from solo");
                GD.Print($"[co-op] chat: opened (input locked {opened}), log \"{hud.LastLogLine}\", input locked after {party.InputLocked}");
                if (!opened) Fail("the chat line did not open in solo");
                if (hud.LastLogLine != $"[{name}] hello from solo") Fail($"the chat line went to the log as \"{hud.LastLogLine}\"");
                if (party.InputLocked) Fail("the chat line stayed open after Enter");
                // 6. the chat key while the stairs question is up: the question keeps the keys (Esc must answer it, not the chat line)
                if (root.GetNodeOrNull<Stairs>("DownStairs") is not { } stairs) { Fail("no DownStairs node"); Finish(); return; }
                hud.AskStairs(stairs, party.Local!);
                Input.ParseInputEvent(new InputEventAction { Action = "chat", Pressed = true });
                Next();
                break;
            }
            case 4 when since >= 0.3:
            {
                GD.Print($"[co-op] chat key over the stairs question: question open {hud.StairsPromptOpen}, chat open {hud.ChatOpen}");
                if (!hud.StairsPromptOpen) Fail("the stairs question closed when T was pressed");
                if (hud.ChatOpen) Fail("T opened the chat line over the stairs question (Esc would then send a No)");
                if (root.GetNodeOrNull<Stairs>("DownStairs") is { } asked) hud.DismissStairs(asked); // stepping off, not a No
                // 7. solo stairs: a Yes changes the floor at once (no ready-check without a server)
                if (root.GetNodeOrNull<Stairs>("DownStairs") is not { } stairs) { Fail("no DownStairs node"); Finish(); return; }
                party.Teleport(b.Surface(DungeonBuilder.WalkableNeighbor(layout, layout.DownStairs)) + Vector3.Up * 0.3f, b.Surface(layout.DownStairs));
                stairs.Confirm(party, party.Local!);
                Next();
                break;
            }
            case 5:
                if (Game.Instance.CurrentFloor == 2) { GD.Print($"[co-op] solo stairs: floor 2 at once ({since:0.0} s)"); Finish(); }
                else if (since > 4) { Fail($"solo stairs did not change floor within 4 s (floor {Game.Instance.CurrentFloor})"); Finish(); }
                break;
        }
    }

    // ---- --test-run-structure ------------------------------------------------------------------------------
    // The Depths' run structure end to end: Story vs New Descent seeds, a stable Daily with its two clauses, clauses signed,
    // priced and saved, a wipe that keeps the floor as it was, costs a fifth of the gold, scars and shows a death card, the
    // Death Clause's memorial and recap, the autosave and the floor card on a descent, the recap model, the profile and a
    // deed, and a victory whose recap names the campaign's boss and whose Victory flag survives a save.
    private bool _testRunStructure;
    private int _rsStep;
    private double _rsAt;
    private int _rsBuilds;
    private Node3D? _rsRootBefore, _rsRootMark;
    private readonly List<string> _rsFails = new();
    private ulong _rsStorySeed;
    private int _rsGoldBefore, _rsSpawnIndex = -1;
    private float _rsMaxHpBefore;
    private string _rsKiller = "";
    private double _rsCardSeenAt;
    private string? _rsAutoBackup; // the developer's own auto slot, put back when the flow ends

    private void RunRunStructureTest()
    {
        var main = Main.Instance;
        if (main == null) return;
        var game = Game.Instance;
        var since = _elapsed - _rsAt;
        void Next() { _rsStep++; _rsAt = _elapsed; _rsBuilds = 0; _rsRootBefore = _rsRootMark ?? main.Builder.FloorRoot; _rsRootMark = null; }
        void Mark() => _rsRootMark = main.Builder.FloorRoot; // before an action that rebuilds synchronously (a load), so Next() waits for the root it made
        void Fail(string why) { _rsFails.Add(why); GD.Print($"[runstructure] FAIL: {why}"); }
        void Finish()
        {
            Save.Profile.ProfileStore.Delete();
            foreach (var slot in new[] { "rs" }) if (SaveManager.Exists(slot)) System.IO.File.Delete(SaveManager.PathFor(slot));
            if (_rsAutoBackup != null) System.IO.File.WriteAllText(SaveManager.PathFor(SaveManager.AutoSlot), _rsAutoBackup);
            else if (SaveManager.Exists(SaveManager.AutoSlot)) System.IO.File.Delete(SaveManager.PathFor(SaveManager.AutoSlot));
            GD.Print(_rsFails.Count == 0 ? "[runstructure] RESULT ok" : $"[runstructure] RESULT FAIL ({string.Join("; ", _rsFails)})");
            _rsStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        // a new floor root stands (a build, a load or a wipe's reload all make one), on the floor asked for
        bool Built(int floor) => game.InGame && !main.Rebuilding && main.Builder.FloorRoot is { } root && root != _rsRootBefore && GodotObject.IsInstanceValid(root) && main.Builder.CurrentFloor == floor && game.CurrentFloor == floor;
        void WipeTheParty()
        {
            var party = main.Party;
            var enemy = main.Builder.FloorRoot!.GetChildren().OfType<Enemy>().FirstOrDefault(e => !e.IsBoss && e.IsAlive);
            if (enemy == null) { Fail("no creature to wipe the party with"); return; }
            _rsKiller = enemy.DisplayName;
            foreach (var adv in party.Adventurers)
                for (var i = 0; i < 12 && !adv.Downed; i++) adv.ReceiveAttack(enemy.Stats, new Combat.AttackProfile { MinDamage = 9999, MaxDamage = 9999 }, enemy.DisplayName, enemy);
            GD.Print($"[runstructure] {_rsKiller} strikes every member down: {party.Adventurers.Count(a => a.Downed)} down");
        }
        switch (_rsStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                // a profile of its own, so the player's is never touched
                Save.Profile.ProfileStore.FileName = "profile-test.json";
                Save.Profile.ProfileStore.Delete();
                Save.Profile.ProfileStore.Load();
                Save.Profile.ProfileStore.ApplyUnlocks(game.Catalog);
                if (SaveManager.Exists(SaveManager.AutoSlot)) _rsAutoBackup = System.IO.File.ReadAllText(SaveManager.PathFor(SaveManager.AutoSlot));
                EventBus.Instance.FloorBuilt += (_, _, _) => _rsBuilds++;
                // 1. the Story: the shipped seed
                if (!main.StartBuiltinCampaign()) { Fail("no built-in campaign"); Finish(); return; }
                Next();
                break;
            }
            case 1 when Built(game.CurrentFloor):
            {
                _rsStorySeed = game.GameSeed;
                GD.Print($"[runstructure] story: seed {_rsStorySeed} run={game.Adventure.Run} floor={game.CurrentFloor} clauses=[{string.Join(",", game.Adventure.Clauses)}]");
                if (game.Adventure.Run != RunKind.Story) Fail($"the Story runs as {game.Adventure.Run}");
                if (game.Adventure.Clauses.Count != 0) Fail("the Story opened with clauses");
                // 2. New Descent: another seed
                main.StartDescent();
                Next();
                break;
            }
            case 2 when Built(game.CurrentFloor):
            {
                GD.Print($"[runstructure] descent: seed {game.GameSeed} run={game.Adventure.Run}");
                if (game.GameSeed == _rsStorySeed) Fail("New Descent rolled the Story's seed");
                if (game.Adventure.Run != RunKind.Descent) Fail($"New Descent runs as {game.Adventure.Run}");
                // 3. the Daily: the date's seed and clauses, recorded as attempted
                main.StartDaily(new DateOnly(2026, 11, 7));
                Next();
                break;
            }
            case 3 when Built(game.CurrentFloor):
            {
                var date = new DateOnly(2026, 11, 7);
                GD.Print($"[runstructure] daily: seed {game.GameSeed} (expected {Adventure.Run.DailyRules.SeedFor(date)}) clauses=[{string.Join(",", game.Adventure.Clauses)}] date={game.Adventure.DailyDate} done={Save.Profile.ProfileRules.DailyDone(Save.Profile.ProfileStore.Current, "2026-11-07")}");
                if (game.GameSeed != Adventure.Run.DailyRules.SeedFor(date)) Fail("the Daily's seed is not the date's");
                if (!game.Adventure.Clauses.SequenceEqual(Adventure.Run.DailyRules.ClausesFor(date))) Fail("the Daily's clauses are not the date's");
                if (game.Adventure.Run != RunKind.Daily || game.Adventure.DailyDate != "2026-11-07") Fail("the Daily is not marked as one");
                if (!Save.Profile.ProfileRules.DailyDone(Save.Profile.ProfileStore.Current, "2026-11-07")) Fail("the Daily was not recorded as attempted");
                if (Adventure.Run.ClauseRules.Active.Heat == 0) Fail("the Daily's clauses are not active");
                var seed = game.GameSeed;
                main.StartDaily(date);
                if (game.GameSeed != seed) Fail("the Daily's seed changed between two starts");
                // 4. clauses and prices on a plain floor 1
                main.StartNewGame(7, false);
                Next();
                break;
            }
            case 4 when Built(1):
            {
                if (!main.SignClauses(new[] { "conveniencefee" }, force: true)) Fail("the clause could not be signed");
                var stock = Items.ShopRules.Roll(game.Catalog, game.GameSeed, 1);
                var item = stock.Items[0];
                var expected = (int)Math.Floor(2.5 * 1.4 * Items.Prices.Value(item) + 1e-9);
                GD.Print($"[runstructure] clauses: {Adventure.Run.ClauseRules.Active.Summary}; buy factor {Items.Prices.BuyFactor}; {item.DisplayName} costs {Items.Prices.BuyPrice(item)} (expected {expected})");
                if (Math.Abs(Items.Prices.BuyFactor - 1.4f) > 0.001f) Fail($"the Convenience Fee left the buy factor at {Items.Prices.BuyFactor}");
                if (Items.Prices.BuyPrice(item) != expected) Fail("the Convenience Fee did not price the stock");
                main.SaveGame("rs");
                Adventure.Run.ClauseRules.Reset();
                Mark();
                if (!main.LoadGame("rs")) { Fail("the clause save would not load"); Finish(); return; }
                Next();
                break;
            }
            case 5 when Built(1):
            {
                GD.Print($"[runstructure] after the load: clauses=[{string.Join(",", game.Adventure.Clauses)}] buy factor {Items.Prices.BuyFactor}");
                if (!game.Adventure.Clauses.SequenceEqual(new[] { "conveniencefee" })) Fail("the clauses did not survive the save");
                if (Math.Abs(Items.Prices.BuyFactor - 1.4f) > 0.001f) Fail("the loaded clause did not reprice the shop");
                // 5. the wipe: a creature killed first stays dead; the gold, the Scar, the card
                var party = main.Party;
                var root = main.Builder.FloorRoot!;
                var first = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.SpawnIndex >= 0 && !e.IsBoss);
                if (first == null) { Fail("no creature on floor 1"); Finish(); return; }
                _rsSpawnIndex = first.SpawnIndex;
                first.ReceiveAttack(party.Members[0].Stats, new Combat.AttackProfile { MinDamage = 9999, MaxDamage = 9999 }, new Combat.HitContext { DefenderStunned = true }, party.Rng, "the test");
                party.AddGold(100 - party.Gold);
                _rsGoldBefore = party.Gold;
                _rsMaxHpBefore = party.Members[0].Stats[Characters.Stat.MaxHp];
                GD.Print($"[runstructure] wipe setup: killed spawn {_rsSpawnIndex} (alive={first.IsAlive}), gold {_rsGoldBefore}, max hp {_rsMaxHpBefore:0.0}");
                WipeTheParty();
                Next();
                break;
            }
            case 6 when Built(1):
            {
                var party = main.Party;
                var adv = game.Adventure;
                var card = main.Hud.DeathCard.Model;
                var memory = adv.Memory.For(1);
                var remembered = game.Floors.TryGetValue(1, out var fs) ? fs : null;
                var stillThere = main.Builder.FloorRoot!.GetChildren().OfType<Enemy>().Any(e => e.SpawnIndex == _rsSpawnIndex);
                GD.Print($"[runstructure] after the wipe: gold {party.Gold} (expected {Adventure.Objectives.WipeRules.GoldAfterWipe(_rsGoldBefore)}), curses=[{string.Join(",", adv.Curses)}] scar until {adv.ScarUntilFloor}, max hp {party.Members[0].Stats[Characters.Stat.MaxHp]:0.0} (was {_rsMaxHpBefore:0.0}), card up={main.Hud.CardUp} epitaph=\"{card?.Epitaph}\" cause={card?.Cause} killer={card?.Killer} button={card?.Button}, wipes on floor {memory.Wipes}, dead remembered={remembered?.DeadEnemies.Contains(_rsSpawnIndex)}, spawn {_rsSpawnIndex} present={stillThere}, worst=\"{Adventure.Run.LedgerRules.WorstMoment(memory)}\"");
                if (party.Gold != Adventure.Objectives.WipeRules.GoldAfterWipe(_rsGoldBefore)) Fail("the wipe did not take a fifth of the gold");
                if (!adv.Curses.Contains("scar") || adv.ScarUntilFloor != 2) Fail("the Scar was not laid until floor 2");
                if (Math.Abs(party.Members[0].Stats[Characters.Stat.MaxHp] - _rsMaxHpBefore * 0.9f) > 1f) Fail("the Scar did not take a tenth of the max HP");
                if (!main.Hud.CardUp || card == null) Fail("no death card");
                if (card != null && (card.Epitaph == "" || card.Cause != "generic" || card.Killer != _rsKiller || card.Floor != 1 || card.WipesOnFloor != 1 || card.Button != "Rise")) Fail($"the death card reads wrong: {card}");
                if (memory.Wipes != 1 || memory.WipeCauses.Count != 1) Fail($"the ledger counts {memory.Wipes} wipes and {memory.WipeCauses.Count} causes");
                if (remembered == null || !remembered.DeadEnemies.Contains(_rsSpawnIndex)) Fail("the floor was not captured before the reload");
                if (stillThere) Fail("the creature killed before the wipe came back");
                if (party.Adventurers.Any(a => a.Downed)) Fail("the party did not rise");
                main.Hud.CloseCard();
                if (main.Hud.CardUp) Fail("the death card did not close on its button");
                // 6. the Death Clause: the run ends with a memorial
                main.SignClauses(new[] { "deathclause" }, force: true);
                if (!Adventure.Run.ClauseRules.Active.Hardcore) Fail("the Death Clause was not signed");
                if (SaveManager.Exists(SaveManager.AutoSlot)) System.IO.File.Delete(SaveManager.PathFor(SaveManager.AutoSlot));
                WipeTheParty();
                Next();
                break;
            }
            case 7 when since >= 0.5:
            {
                var adv = game.Adventure;
                var card = main.Hud.DeathCard.Model;
                var info = SaveManager.Info(SaveManager.AutoSlot);
                GD.Print($"[runstructure] death clause: fallen={adv.Fallen} run over={main.RunOver} auto fallen={info.Fallen} \"{info.Epitaph}\" summary=\"{info.Summary}\" button={card?.Button} down={main.Party.Adventurers.Count(a => a.Downed)} profile runs={Save.Profile.ProfileStore.Current.Records.Runs}");
                if (!adv.Fallen || !main.RunOver) Fail("the Death Clause did not end the run");
                if (!info.Exists || !info.Fallen || info.Epitaph == "") Fail("the memorial was not written to the auto slot");
                if (card?.Button != "To the Records") Fail("the card's button is not To the Records");
                if (main.Party.Adventurers.Any(a => !a.Downed)) Fail("the bodies rose under the Death Clause");
                main.Hud.CloseCard();
                var recap = main.Hud.Recap.Model;
                GD.Print($"[runstructure] after the card: recap up={main.Hud.Recap.Visible} victory={recap?.Victory} headline=\"{recap?.Headline}\" button={main.Hud.Recap.ButtonText}");
                if (recap == null || recap.Victory) Fail("the Death Clause's recap did not follow the card");
                if (recap != null && !recap.Headline.StartsWith("The contract is void.")) Fail("the recap's headline is not the voided contract");
                var loadedFallen = main.LoadGame(SaveManager.AutoSlot);
                if (loadedFallen) Fail("the memorial loaded");
                if (!main.InGame) { Fail("refusing the memorial tore the session down"); Finish(); return; }
                main.Hud.CloseCard();
                GD.Print($"[runstructure] after the recap: in game={main.InGame} menu={main.Menu?.Visible} records={main.Menu?.Records.Visible} deed rows={main.Menu?.Records.DeedRows}");
                if (main.InGame || main.Menu?.Visible != true || main.Menu.Records.Visible != true) Fail("the recap did not lead to the menu and the Records");
                if (main.Menu?.Records.DeedRows != 24) Fail($"the Records list {main.Menu?.Records.DeedRows} deeds");
                // 7. autosave and the floor card on a descent
                main.StartNewGame(7, false);
                Next();
                break;
            }
            case 8 when Built(1):
            {
                if (SaveManager.Exists(SaveManager.AutoSlot)) System.IO.File.Delete(SaveManager.PathFor(SaveManager.AutoSlot));
                if (!game.ChangeFloor(+1)) Fail("could not descend");
                Next();
                break;
            }
            case 9 when Built(2):
            {
                var card = main.Hud.LastFloorCard;
                GD.Print($"[runstructure] floor 2: autosave={SaveManager.Exists(SaveManager.AutoSlot)} floor card up={main.Hud.FloorCardVisible} {card}");
                if (!SaveManager.Exists(SaveManager.AutoSlot)) Fail("no autosave on the floor change");
                if (!main.Hud.FloorCardVisible || card == null) Fail("no floor card on the descent");
                if (card != null && (!System.Text.RegularExpressions.Regex.IsMatch(card.Secrets, "^\\d+/\\d+$") || card.Elites < 0 || card.Title == "" || card.Time == "")) Fail($"the floor card reads wrong: {card}");
                if (main.Party.InputLocked) Fail("the floor card locked the input");
                _rsCardSeenAt = _elapsed;
                Next();
                break;
            }
            case 10 when _elapsed >= _rsCardSeenAt + 4.5:
            {
                if (main.Hud.FloorCardVisible) Fail("the floor card did not go after four seconds");
                // 8. the recap model
                var recap = Adventure.Run.RecapBuilder.Build(game.Adventure.Memory, main.Party.Members.Select(m => m.Name).ToList(), game.Adventure.Clauses, Array.Empty<string>(), false, "h", 0);
                if (recap.Heroes.Count != 4) Fail($"the recap has {recap.Heroes.Count} heroes");
                // 9. the profile and a deed
                var profile = Save.Profile.ProfileStore.Current;
                GD.Print($"[runstructure] profile: best depth {profile.Records.BestDepth}, runs {profile.Records.Runs}, deeds {profile.Deeds.Count}");
                if (profile.Records.BestDepth < 2) Fail($"the profile's best depth is {profile.Records.BestDepth}");
                for (var i = 0; i < 4; i++) EventBus.Instance.RaisePartyWipedBy(2, "generic");
                GD.Print($"[runstructure] deeds: [{string.Join(",", profile.Deeds.Keys)}] unlocks [{string.Join(",", profile.Unlocks)}] toast={main.Hud.DeedToastVisible}");
                if (!profile.Deeds.ContainsKey("bang_on_schedule")) Fail("four wipes on one floor did not earn Bang on Schedule");
                if (!profile.Unlocks.Contains("banter:wipes") || !Adventure.Voice.BanterRules.UnlockedSets.Contains("wipes")) Fail("the deed did not unlock its banter set");
                if (!main.Hud.DeedToastVisible) Fail("no deed toast");
                var onDisk = Save.Profile.ProfileFormat.Deserialize(System.IO.File.ReadAllText(Save.Profile.ProfileStore.Path));
                if (onDisk == null || !onDisk.Deeds.ContainsKey("bang_on_schedule")) Fail("the deed was not saved to the profile file");
                // 10. victory on floor 20
                game.JumpToFloor(20);
                Next();
                break;
            }
            case 11 when Built(20):
            {
                main.OnBossDefeated(20);
                var recap = main.Hud.Recap.Model;
                var campaign = game.Adventure.Campaign;
                var boss = main.Builder.Contents?.Plan?.Boss?.Name is { Length: > 0 } b ? b : campaign?.Tiers.LastOrDefault()?.BossName ?? "";
                GD.Print($"[runstructure] victory: {game.Victory} card up={main.Hud.CardUp} headline=\"{recap?.Headline}\" boss=\"{boss}\" antagonist=\"{campaign?.Antagonist.Name}\" score {recap?.Score}");
                if (!game.Victory || !main.Hud.CardUp || recap == null) Fail("no victory recap");
                if (recap != null && boss != "" && !recap.Headline.Contains(boss)) Fail("the recap does not name the boss");
                if (recap != null && recap.Headline == Adventure.Run.RecapBuilder.VictoryHeadline(null, null)) Fail("the recap fell back to the stock headline");
                if (recap != null && SaveManager.Exists(SaveManager.QuickSlot)) { var other = SaveManager.Info(SaveManager.QuickSlot); if (other.Fallen && other.Run != "Daily") Fail("the Death Clause marked a save of another run"); }
                if (recap != null && campaign?.Antagonist.Name is { Length: > 0 } ant && !recap.Headline.Contains(ant)) Fail("the recap does not name the antagonist");
                if (recap is { Victory: false }) Fail("the victory recap is not a victory");
                main.Hud.CloseCard();
                if (main.Hud.CardUp) Fail("the recap did not close");
                main.SaveGame("rs");
                Mark();
                if (!main.LoadGame("rs")) { Fail("the victory save would not load"); Finish(); return; }
                Next();
                break;
            }
            case 12 when Built(20):
            {
                GD.Print($"[runstructure] after the load: victory={game.Victory}");
                if (!game.Victory) Fail("Victory did not survive the save");
                Finish();
                break;
            }
            case < 99 when since > 40:
                Fail($"step {_rsStep} timed out (floor {game.CurrentFloor}, builds {_rsBuilds}, in game {game.InGame})");
                Finish();
                break;
        }
    }
}
