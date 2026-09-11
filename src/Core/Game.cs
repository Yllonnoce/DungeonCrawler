using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Defs;
using DungeonCrawler.Characters;
using DungeonCrawler.Characters.Defs;
using DungeonCrawler.Items;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>Session-wide state: current game seed, current floor. Autoload.</summary>
public partial class Game : Node
{
    public static Game Instance { get; private set; } = null!;

    public ulong GameSeed { get; private set; }
    public int CurrentFloor { get; private set; }
    public bool InGame { get; private set; }
    public ItemCatalog Catalog { get; private set; } = new();
    public SkillCatalog Skills { get; private set; } = new();

    private readonly Dictionary<int, ExplorationMap> _exploration = new();

    /// <summary>Per-floor deltas for floors the party has left (and, after a load, every saved floor).</summary>
    public Dictionary<int, FloorStateDto> Floors { get; } = new();
    /// <summary>A client's record of the objective the server dealt each floor, so both build the same floor whatever the server was told on its command line.</summary>
    public Dictionary<int, Dungeon.Generation.FloorObjectiveKind> ServerObjectives { get; } = new();
    /// <summary>Hordes already let loose on a floor, from its saved state.</summary>
    public IReadOnlyCollection<string> SpentHordes(int floor) => Floors.TryGetValue(floor, out var s) ? s.SpentHordes : Array.Empty<string>();
    public double PlaySeconds { get; set; }
    public bool Victory { get; set; }
    /// <summary>Who designs this run's floors and what they have decided so far. Saved with the game.</summary>
    public AdventureDto Adventure { get; set; } = new();

    /// <summary>Fog-of-war state for a floor, kept for the whole game so revisited floors stay mapped.</summary>
    public ExplorationMap ExplorationFor(int floorIndex)
    {
        if (!_exploration.TryGetValue(floorIndex, out var map)) _exploration[floorIndex] = map = new ExplorationMap();
        return map;
    }

    public override void _Ready()
    {
        Instance = this;
        Settings.Load();
        Catalog = Items.Catalog.FromRegistry(Registry.Instance);
        foreach (var def in Registry.Instance.All<SkillDef>()) Skills.Skills[def.Id] = def.ToProfile();
        GD.Print($"[Game] ready: {Catalog.Bases.Count} item bases, {Catalog.Affixes.Count} affixes, {Catalog.Legendaries.Count} legendaries, {Catalog.Tables.Count} loot tables, {Skills.Skills.Count} skills");
    }

    /// <summary>Reset session state for a new run. The campaign is filled in by the caller before <see cref="BeginFloor"/>.</summary>
    public void PrepareNewGame(ulong? seed = null)
    {
        GameSeed = seed ?? (ulong)Random.Shared.NextInt64(); // the only place Random.Shared is allowed: picking the game seed
        CurrentFloor = 1;
        InGame = true;
        _exploration.Clear();
        Floors.Clear();
        PlaySeconds = 0;
        Victory = false;
        Adventure = new AdventureDto { Mode = AdventureMode.Offline };
        GD.Print($"[Game] new game seed={GameSeed}");
    }

    public List<TierProfile> TierProfiles() => Registry.Instance.All<TierDef>().OrderBy(t => t.Index).Select(t => t.ToProfile()).ToList();

    /// <summary>Build the current floor: raises FloorChanged, which Main answers by rebuilding.</summary>
    public void BeginFloor() => EventBus.Instance.RaiseFloorChanged(CurrentFloor, +1);

    /// <summary>Offline new game in one step (tests and debug paths).</summary>
    public void NewGame(ulong? seed = null)
    {
        PrepareNewGame(seed);
        Adventure.Campaign = OfflineDesigner.Outline(GameSeed, TierProfiles(), Main.Instance?.Builder.ThemeProfiles());
        BeginFloor();
    }

    /// <summary>Adopt a loaded save's session state. The caller restores the party and rebuilds the floor.</summary>
    public void BeginLoaded(SaveData data)
    {
        GameSeed = data.GameSeed;
        CurrentFloor = data.CurrentFloor;
        InGame = true;
        PlaySeconds = data.PlaySeconds;
        Victory = data.Victory;
        _exploration.Clear();
        Floors.Clear();
        foreach (var (floor, state) in data.Floors) Floors[floor] = state;
        Adventure = data.Adventure ?? new AdventureDto();
        GD.Print($"[Game] loaded seed={GameSeed} floor={CurrentFloor} floors={Floors.Count} adventure={Adventure.Mode} plans={Adventure.FloorPlans.Count}");
    }

    /// <summary>Adopt the state a server sent in its Welcome. The caller builds the floor.</summary>
    public void BeginNetwork(Net.Welcome w)
    {
        GameSeed = w.GameSeed;
        CurrentFloor = w.CurrentFloor;
        InGame = true;
        PlaySeconds = w.PlaySeconds;
        Victory = w.Victory;
        _exploration.Clear();
        Floors.Clear();
        Floors[w.CurrentFloor] = w.FloorState;
        Adventure = w.Adventure ?? new AdventureDto();
        GD.Print($"[Game] joined seed={GameSeed} floor={CurrentFloor} adventure={Adventure.Mode} plans={Adventure.FloorPlans.Count}");
    }

    public void EndSession()
    {
        InGame = false;
        Floors.Clear();
        _exploration.Clear();
        Adventure = new AdventureDto();
    }

    public override void _Process(double delta)
    {
        if (InGame && !GetTree().Paused) PlaySeconds += delta;
    }

    /// <summary>Move to an adjacent floor. Returns false when the move is not allowed (e.g. up from floor 1).</summary>
    public bool ChangeFloor(int delta)
    {
        if (!FloorIsOurs()) return false;
        var target = CurrentFloor + delta;
        if (target < 1)
        {
            EventBus.Instance.Log("The way up is sealed.");
            return false;
        }
        CurrentFloor = target;
        EventBus.Instance.RaiseFloorChanged(CurrentFloor, delta);
        return true;
    }

    public void ReloadFloor() { if (FloorIsOurs()) EventBus.Instance.RaiseFloorChanged(CurrentFloor, 0); }

    /// <summary>
    /// A client never changes floor on its own: the server decides, and <see cref="SetFloorFromServer"/> follows it.
    /// Anything else (a debug key, a script, a stray call) would leave this machine on a floor the server is not
    /// simulating, with enemies that are only scenery.
    /// </summary>
    private static bool FloorIsOurs()
    {
        if (!Net.NetSession.IsClient) return true;
        EventBus.Instance.Log("The server decides when the party moves between floors.");
        GD.PushWarning("[Game] floor change refused on a client");
        return false;
    }

    /// <summary>A client following the server down (or up): the floor is set, not raised, because Main rebuilds on request.</summary>
    public void SetFloorFromServer(int floor) => CurrentFloor = floor;

    /// <summary>Debug / testing: go straight to a floor.</summary>
    public void JumpToFloor(int floor)
    {
        if (!FloorIsOurs()) return;
        var delta = Math.Sign(floor - CurrentFloor);
        CurrentFloor = Math.Max(1, floor);
        EventBus.Instance.RaiseFloorChanged(CurrentFloor, delta == 0 ? 0 : delta);
    }
}
