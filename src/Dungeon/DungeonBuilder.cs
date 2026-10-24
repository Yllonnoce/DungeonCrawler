using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Defs;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Enemies.Defs;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Dungeon;

/// <summary>
/// Turns a <see cref="FloorLayout"/> into scene nodes: one GridMap for structure and instanced scenes for stairs,
/// enemies and props. Only one floor exists at a time; <see cref="Build"/> frees the previous one.
/// </summary>
public partial class DungeonBuilder : Node3D
{
    public const float CellSize = 4f;
    public const float FloorTop = 0.2f;

    private const int ItemFloor = 0;
    private const int ItemWall = 1;
    private const int ItemCeiling = 2;
    private const int ItemRampBase = 3; // 3..10: 4 directions x {low half, high half}
    private const int ItemChamferBase = 11; // 11..14: a 45° wall piece per open diagonal
    // 15 is free: water cells get no floor item at all; WaterSurface builds their sunken bed
    private const int ItemBuckledBase = 16; // 16..19: floor slabs heaved into ridges and domes
    private const int ItemVaultBase = 20; // 20..22: vaulted ceiling pieces, see VaultPiece (barrel along X, barrel along Z, groin)
    private const int ItemVaultBrokenBase = 23; // 23..25: the same pieces with the crown fallen in (a ruin's collapse)
    private const int BuckledVariants = 4;
    /// <summary>Floor cells whose slab is tilted or heaved this floor; picked from the seed before the grid is built.</summary>
    private readonly Dictionary<GridPos, int> _buckled = new();
    private int _dynamicSerial;
    /// <summary>Game seed of the floor being built (for per-actor seeds placed by the builder).</summary>
    public ulong GameSeed { get; private set; }
    public IReadOnlyDictionary<GridPos, int> BuckledCells => _buckled;
    public const float LevelHeight = 4f;
    private static readonly GridPos[] RampDirs = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private readonly Dictionary<string, MeshLibrary> _libraries = new();
    private readonly List<ThemeDef> _themes = new();
    private readonly Dictionary<string, EnemyProfile> _enemies = new();
    private readonly List<TierDef> _tiers = new();
    private readonly HashSet<GridPos> _closedDoors = new();
    private readonly HashSet<GridPos> _openSecrets = new();
    private GridMap? _grid;

    private Node3D? _floorRoot;
    private PackedScene _enemyScene = null!, _stairsScene = null!, _chestScene = null!, _barrelScene = null!, _trapScene = null!, _doorScene = null!, _leverScene = null!, _lootScene = null!, _secretScene = null!, _npcScene = null!;

    public FloorLayout? Layout { get; private set; }
    public IReadOnlyDictionary<string, EnemyProfile> Roster => _enemies;
    /// <summary>What populated the current layout (enemy placements with their save indices).</summary>
    public FloorContents? Contents { get; private set; }
    public Node3D? FloorRoot => _floorRoot;
    public TierDef? CurrentTier { get; private set; }
    /// <summary>The look the current floor wears (textures and lighting).</summary>
    public ThemeDef? CurrentTheme { get; private set; }
    public IReadOnlyList<ThemeDef> Themes => _themes;
    public List<ThemeProfile> ThemeProfiles() => _themes.Select(t => t.ToProfile()).ToList();
    public int CurrentFloor { get; private set; }

    public override void _Ready()
    {
        _enemyScene = GD.Load<PackedScene>("res://scenes/Enemies/Enemy.tscn");
        _stairsScene = GD.Load<PackedScene>("res://scenes/Props/Stairs.tscn");
        _chestScene = GD.Load<PackedScene>("res://scenes/Props/Chest.tscn");
        _barrelScene = GD.Load<PackedScene>("res://scenes/Props/Barrel.tscn");
        _trapScene = GD.Load<PackedScene>("res://scenes/Props/Trap.tscn");
        _doorScene = GD.Load<PackedScene>("res://scenes/Props/Door.tscn");
        _leverScene = GD.Load<PackedScene>("res://scenes/Props/Lever.tscn");
        _lootScene = GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn");
        _secretScene = GD.Load<PackedScene>("res://scenes/Props/SecretWall.tscn");
        _npcScene = GD.Load<PackedScene>("res://scenes/Props/Npc.tscn");
        foreach (var def in Registry.Instance.All<EnemyDef>()) _enemies[def.Id] = def.ToProfile();
        _tiers.AddRange(Registry.Instance.All<TierDef>().OrderBy(t => t.Index));
        _themes.AddRange(Registry.Instance.All<ThemeDef>().OrderBy(t => t.TierIndex).ThenBy(t => t.Id));
        foreach (var tier in _tiers) if (_themes.All(t => t.TierIndex != tier.Index)) _themes.Add(ThemeDef.FromTier(tier)); // every tier has at least its own look
        GD.Print($"[DungeonBuilder] {_tiers.Count} tiers, {_themes.Count} themes, {_enemies.Count} enemy types");
    }

    public static GridPos WorldToCell(Vector3 world) => new((int)Mathf.Floor(world.X / CellSize), (int)Mathf.Floor(world.Z / CellSize));
    /// <summary>Cell centre at ground level (no height). Prefer <see cref="Surface"/> for placing things.</summary>
    public static Vector3 CellToWorld(GridPos p) => new(p.X * CellSize + CellSize / 2f, FloorTop, p.Y * CellSize + CellSize / 2f);

    /// <summary>Cell centre on its walking surface, on the main or under layer.</summary>
    public Vector3 Surface(GridPos p, bool under = false)
    {
        var c = CellToWorld(p);
        if (under || Layout == null) return c;
        c.Y = FloorTop + Layout.HeightOf(p);
        return c;
    }

    /// <summary>Is a position on the under layer (a cellar or its tunnel)?</summary>
    public bool IsUnderAt(Vector3 world)
    {
        if (Layout == null) return false;
        var cell = WorldToCell(world);
        return Layout.IsUnder(cell) && world.Y < LevelHeight * 0.5f;
    }

    public bool IsSecretOpen(GridPos cell) => _openSecrets.Contains(cell);

    /// <summary>Debug: which library items occupy a cell at y indices 0..2.</summary>
    public string DebugCell(GridPos c)
    {
        if (_grid == null) return "?";
        string Item(int y) { var i = _grid.GetCellItem(new Vector3I(c.X, y, c.Y)); return i == GridMap.InvalidCellItem ? "-" : _grid.MeshLibrary!.GetItemName(i); }
        return $"{c}[{Item(0)}|{Item(1)}|{Item(2)}]";
    }

    /// <summary>Turn a secret wall into a doorway: the cube goes, a floor slab takes its place, and the gap above is roofed.</summary>
    public void OpenSecret(GridPos cell, int level)
    {
        _openSecrets.Add(cell);
        if (_grid == null) return;
        _grid.SetCellItem(new Vector3I(cell.X, level, cell.Y), ItemFloor);
        var above = new Vector3I(cell.X, level + 1, cell.Y);
        if (_grid.GetCellItem(above) == GridMap.InvalidCellItem) _grid.SetCellItem(above, Layout != null ? CeilingFor(Layout, cell, level, asOpen: true) : ItemCeiling);
    }

    public TierDef? TierFor(int floor) => _tiers.Count == 0 ? null : _tiers[Math.Min(_tiers.Count - 1, TierRules.TierIndex(floor))];

    /// <summary>
    /// Generator knobs for a floor: the tier's defaults, adjusted by the campaign's per-floor overrides, in the shape the
    /// floor takes (see <see cref="LayoutFor"/>). <paramref name="gameSeed"/> defaults to the running game's seed.
    /// </summary>
    public FloorSpec SpecFor(int floor, ulong? gameSeed = null)
    {
        var spec = TierFor(floor)?.ToSpec(floor) ?? new FloorSpec
        {
            EnemyCount = Math.Min(4 + floor, 14), ChestCount = 2, BossFloor = TierRules.IsBossFloor(floor), LockedChestCount = floor >= 2 ? 1 : 0, Floor = floor,
        };
        var seed = gameSeed ?? Game.Instance?.GameSeed ?? GameSeed;
        var layout = LayoutFor(floor, seed);
        spec = spec with { Layout = layout, Objective = ObjectiveFor(floor, seed, layout) };
        if (DebugTools.Instance?.ForcedHazard is { } fh) spec = spec with { ForceHazard = fh.StartsWith("spike") ? HazardKind.SpikeBed : HazardKind.Press };
        var overrides = Game.Instance?.Adventure?.Campaign?.OutlineFor(floor)?.SpecOverrides;
        return overrides is { IsEmpty: false } ? overrides.Apply(spec) : spec;
    }

    /// <summary>
    /// The floor's objective: what the server dealt (a client), else an --objective override, else the campaign's
    /// choice, else the seed's deal; never on a boss floor, never one the shape cannot hold, never one not yet built.
    /// </summary>
    public FloorObjectiveKind ObjectiveFor(int floor, ulong gameSeed, LayoutKind layout)
    {
        FloorObjectiveKind kind;
        if (Net.NetSession.IsClient && Game.Instance?.ServerObjectives.TryGetValue(floor, out var served) == true) kind = served;
        else if (FloorObjectives.TryParse(DebugTools.Instance?.ForcedObjective, out var forced)) kind = forced;
        else if (FloorObjectives.TryParse(Game.Instance?.Adventure?.Campaign?.OutlineFor(floor)?.Objective, out var wanted) && floor >= 2) kind = wanted;
        else kind = FloorObjectives.Pick(gameSeed, floor, layout);
        if (floor < 2 || TierRules.IsBossFloor(floor) || !FloorObjectives.Compatible(kind, layout) || !FloorObjectives.Implemented.Contains(kind)) return FloorObjectiveKind.None; // floor 1 teaches
        return kind;
    }

    /// <summary>The floor's shape: a --layout override, else the campaign's choice when it names one, else the seed's deal.</summary>
    public LayoutKind LayoutFor(int floor, ulong gameSeed)
    {
        if (LayoutKinds.TryParse(DebugTools.Instance?.ForcedLayout, out var forced)) return forced;
        var wanted = Game.Instance?.Adventure?.Campaign?.OutlineFor(floor)?.Layout;
        if (LayoutKinds.TryParse(wanted, out var chosen) && (!TierRules.IsBossFloor(floor) || LayoutKinds.BossKinds.Contains(chosen))) return chosen;
        return LayoutKinds.Pick(gameSeed, floor);
    }

    private readonly HashSet<GridPos> _propCells = new();
    /// <summary>Closed doors and solid props (chests) that enemies must path around.</summary>
    public bool IsCellBlocked(GridPos cell) => _closedDoors.Contains(cell) || _propCells.Contains(cell);
    public void SetDoorOpen(GridPos cell) => _closedDoors.Remove(cell);
    public void SetDoorClosed(GridPos cell) => _closedDoors.Add(cell);
    public void BlockCell(GridPos cell) => _propCells.Add(cell);

    public const float DangerWeightCompanion = 50f;
    public const float DangerWeightSmart = 12f;
    public const float DangerWeightMindless = 0f;
    private readonly Dictionary<GridPos, float> _danger = new();
    /// <summary>How dangerous a cell is right now, 0..1: a lethal hazard arming or closing over it. Pathfinders add it to their step cost.</summary>
    public float DangerAt(GridPos cell) => _danger.TryGetValue(cell, out var d) ? d : 0f;
    public void SetDanger(GridPos cell, float value) { if (value <= 0f) _danger.Remove(cell); else _danger[cell] = Math.Min(1f, value); }
    public void ClearDanger(IEnumerable<GridPos> cells) { foreach (var c in cells) _danger.Remove(c); }
    /// <summary>Any cell between two points (sampled every half cell) carrying danger.</summary>
    public bool DangerAlong(Vector3 from, Vector3 to)
    {
        if (_danger.Count == 0) return false;
        var steps = Math.Max(1, (int)(from.DistanceTo(to) / (CellSize * 0.5f)));
        for (var i = 0; i <= steps; i++) if (DangerAt(WorldToCell(from.Lerp(to, i / (float)steps))) > 0f) return true;
        return false;
    }

    private readonly List<Node3D> _hazards = new();
    /// <summary>The lethal hazard nodes on this floor (Press, SpikeBed), by placement index.</summary>
    public IReadOnlyList<Node3D> Hazards => _hazards;
    /// <summary>Whether the lethal hazard with this placement index is back at rest (a Mangled body may be pulled free). True when no such hazard exists.</summary>
    public bool HazardArmed(int index)
    {
        foreach (var h in _hazards)
        {
            if (!IsInstanceValid(h)) continue;
            if (h is Props.Press p && p.Index == index) return p.Armed;
        }
        return true;
    }

    public EnemyProfile? EnemyProfile(string id) => _enemies.TryGetValue(id, out var p) ? p : null;

    /// <summary>Build a floor. With a plan, enemies and extras follow it (falling back to legacy population if it no longer fits the layout).</summary>
    public FloorLayout Build(int floorIndex, ulong gameSeed, FloorPlan? plan = null, PartySnapshot? party = null)
    {
        Clear();
        GameSeed = gameSeed;
        var seed = Rng.FloorSeed(gameSeed, floorIndex);
        var tier = TierFor(floorIndex);
        var spec = SpecFor(floorIndex, gameSeed);
        var layout = FloorGenerator.Generate(spec, seed);
        Layout = layout;
        CurrentTier = tier;
        CurrentFloor = floorIndex;
        CurrentTheme = ThemeFor(floorIndex, gameSeed);
        FloorContents? contents = null;
        if (plan != null)
        {
            contents = AdventurePlanning.ResolveStored(layout, spec, plan, tier?.ToProfile(), _enemies, floorIndex, seed, party ?? PartySnapshot.Expected(floorIndex), out var report, Game.Instance?.Adventure.Memory.Flags);
            if (contents == null) GD.PushWarning($"[DungeonBuilder] floor {floorIndex}: stored plan rejected ({report}), using legacy population");
            else if (report.Repairs.Count > 0) GD.Print($"[DungeonBuilder] floor {floorIndex}: plan repaired: {string.Join("; ", report.Repairs)}");
            if (contents is { Dropped.Count: > 0 }) GD.Print($"[DungeonBuilder] floor {floorIndex}: could not place: {string.Join("; ", contents.Dropped)}");
        }
        contents ??= LegacyPopulator.Populate(layout, tier?.ToProfile(), _enemies, floorIndex, seed);
        Contents = contents;

        _floorRoot = new Node3D { Name = $"Floor{floorIndex}" };
        AddChild(_floorRoot);

        PickBuckled(layout);
        _dynamicSerial = 0;
        GameSeed = gameSeed;
        _grid = BuildGridMap(layout, CurrentTheme);
        _floorRoot.AddChild(_grid);
        PlaceStairs(layout, _floorRoot, floorIndex);
        PlaceGreeter(layout, _floorRoot, floorIndex);
        PlaceViewerCast(layout, _floorRoot, floorIndex);
        PlaceEnemies(contents, floorIndex);
        PlaceProps(layout, contents, _floorRoot, floorIndex, seed);
        PlaceFires(layout, _floorRoot, floorIndex);
        PlaceStructures(layout, _floorRoot, seed);
        PlaceNestMouths(layout, _floorRoot, seed);
        PlaceHordes(layout, contents, floorIndex, Game.Instance?.SpentHordes(floorIndex) ?? Array.Empty<string>());
        var floorState = Game.Instance != null && Game.Instance.Floors.TryGetValue(floorIndex, out var fs) ? fs : null;
        PlaceAmbushes(layout, floorIndex, seed, floorState);
        PlacePortals(layout, floorIndex, seed, floorState);
        PlaceObjective(layout, contents, _floorRoot, floorIndex, seed);
        if (layout.Water.Count > 0)
        {
            var tile = CurrentTheme?.MetersPerTile ?? 2f;
            var bedMat = Material(CurrentTheme?.FloorTexture ?? "", (CurrentTheme?.FloorColor ?? new Color(0.32f, 0.30f, 0.28f)).Darkened(0.2f).Lerp(new Color(0.2f, 0.34f, 0.36f), 0.4f), tile);
            var plank = Material("weathered_planks", new Color(0.5f, 0.38f, 0.24f), 1.6f);
            foreach (var water in layout.Water) _floorRoot.AddChild(WaterSurface.Build(layout, water, cell => Surface(cell), CellSize, bedMat, plank));
        }
        PlaceLightsAndDecor(layout, _floorRoot, tier, seed);
        var tremor = new Tremor { Name = "Tremor" };
        tremor.SetStone(Material(CurrentTheme?.WallTexture ?? "", CurrentTheme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), CurrentTheme?.MetersPerTile ?? 2f));
        _floorRoot.AddChild(tremor);

        GD.Print($"[DungeonBuilder] floor {floorIndex} ({tier?.DisplayName ?? "default"}{(CurrentTheme != null ? "/" + CurrentTheme.Id : "")}{(TierRules.IsBossFloor(floorIndex) ? ", boss" : "")}, {LayoutKinds.Id(layout.Kind)}) seed={seed} rooms={layout.Rooms.Count} fires={layout.Fires.Count} enemies={contents.EnemyCount} plan={contents.Plan?.Source ?? "legacy"} groups={contents.Collectives.Count} vaults={layout.Vaults.Count} traps={layout.TrapSpawns.Count} ramps={layout.Ramps.Count} ledges={layout.Ledges.Count} cellars={layout.Cellars.Count} secrets={layout.Secrets.Count} structures=[{string.Join(",", layout.Structures.Select(s => s.Kind))}] water=[{string.Join(",", layout.Water.Select(w => w.Kind))}] nests={layout.Nests.Count} objective={FloorObjectives.Id(layout.Objective?.Kind ?? FloorObjectiveKind.None)} hordes={contents.Hordes.Count} ambushes={AmbushSpawner.Instance?.Points.Count ?? 0} hash={layout.ContentHash():X16}");
        return layout;
    }

    public void Clear()
    {
        _closedDoors.Clear();
        _openSecrets.Clear();
        _propCells.Clear();
        _danger.Clear();
        _hazards.Clear();
        _grid = null;
        if (_floorRoot == null) return;
        _floorRoot.QueueFree();
        _floorRoot = null;
        Layout = null;
    }

    /// <summary>The character rigged in the 3dObject viewer (assets/models/viewer/dwarf_warrior.glb, exported with "Export for Godot").</summary>
    public const string GreeterModel = "res://assets/models/viewer/dwarf_warrior.glb";
    /// <summary>The KayKit item hung from the greeter's right-hand grip bone.</summary>
    public const string GreeterWeapon = "sword_1handed";
    /// <summary>More characters from the viewer, set out near floor 1's up stairs to be looked at and tried: a goddess to talk to and an orc to fight.</summary>
    public const string GoddessModel = "res://assets/models/viewer/goddess.glb";
    public const string FoehammerModel = "res://assets/models/viewer/foehammer.glb";
    public const string ViewerOrcId = "enemy.viewer_orc";

    /// <summary>
    /// Floor 1 only: the viewer-made dwarf stands a few steps from the entrance stairs, facing the cell the party
    /// arrives on (the stairs' walkable neighbour, where Main teleports it), with a word of welcome.
    /// </summary>
    private void PlaceGreeter(FloorLayout layout, Node3D root, int floorIndex)
    {
        if (floorIndex != 1 || !ResourceLoader.Exists(GreeterModel)) return;
        var arrival = WalkableNeighbor(layout, layout.UpStairs);
        if (arrival == layout.UpStairs) return;
        var cell = StepsFrom(layout, arrival, 3) ?? StepsFrom(layout, arrival, 2);
        if (cell == null) return;
        var npc = SpawnObjectiveNpc(root, "greeter_dwarf", "Brannock", cell.Value, NpcMode.Quest, GreeterModel);
        var armed = npc.HoldWeapon(GreeterWeapon, 0.75f);
        GD.Print($"[DungeonBuilder] greeter {(armed ? "holds " + GreeterWeapon : "has nothing to hold (no grip bones in the model)")}");
        npc.ShowMoves = true; // his dialogue lists every clip the viewer export carries, to try them in place
        npc.Script = new NpcScript
        {
            Start = "hello",
            Nodes =
            {
                new DialogueNode
                {
                    Id = "hello",
                    Line = new Line("Welcome, travellers. The dark starts past these stairs. Mind your footing, and your friends. Ask, and I'll show you how I move."),
                    Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } },
                },
            },
        };
        npc.LookAt(Surface(arrival) + Vector3.Up * 0.1f, Vector3.Up);
        GD.Print($"[DungeonBuilder] floor {floorIndex}: greeter {npc.NpcName} at {cell} facing the arrival cell {arrival} by the up stairs {layout.UpStairs} (model {(npc.HasModel ? "loaded" : "missing")})");
    }

    private void PlaceViewerCast(FloorLayout layout, Node3D root, int floorIndex)
    {
        if (floorIndex != 1) return;
        var arrival = WalkableNeighbor(layout, layout.UpStairs);
        if (arrival == layout.UpStairs) return;
        if (ResourceLoader.Exists(GoddessModel) && (StepsFrom(layout, arrival, 5) ?? StepsFrom(layout, arrival, 4)) is { } gCell)
        {
            var npc = SpawnObjectiveNpc(root, "viewer_goddess", "Goddess", gCell, NpcMode.Quest, GoddessModel);
            npc.ShowMoves = true;
            npc.Script = new NpcScript
            {
                Start = "hello",
                Nodes =
                {
                    new DialogueNode
                    {
                        Id = "hello",
                        Line = new Line("You have come far for so little light. Ask, and I will show you how I move."),
                        Choices = { new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell } },
                    },
                },
            };
            npc.LookAt(Surface(arrival) + Vector3.Up * 0.1f, Vector3.Up);
            GD.Print($"[DungeonBuilder] floor {floorIndex}: goddess at {gCell} (model {(npc.HasModel ? "loaded" : "missing")})");
        }
        if (ResourceLoader.Exists(FoehammerModel) && Main.Instance?.Companions.Contains("viewer_foehammer") != true && (StepsFrom(layout, arrival, 7) ?? StepsFrom(layout, arrival, 6)) is { } fCell)
        {
            var npc = SpawnObjectiveNpc(root, "viewer_foehammer", "Foehammer", fCell, NpcMode.Fighter, FoehammerModel);
            npc.ShowMoves = true;
            npc.Script = new NpcScript
            {
                Start = "hello",
                Nodes =
                {
                    new DialogueNode
                    {
                        Id = "hello",
                        Line = new Line("Stand behind me when it comes to blows. Ask, and I'll show you how I move."),
                        Choices =
                        {
                            new ChoicePlan { Text = "Will you fight alongside us?", Effect = DialogueEffect.Join, Next = "yes", NextIfRefused = "no" },
                            new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell },
                        },
                    },
                    new DialogueNode { Id = "yes", Line = new Line("Then I'm with you. Keep up, and keep out of the way of my hammer."), Choices = { new ChoicePlan { Text = "Welcome aboard.", Effect = DialogueEffect.Farewell } } },
                    new DialogueNode { Id = "no", Line = new Line("Not today. Show me you can hold your own, then ask again."), Choices = { new ChoicePlan { Text = "Fair enough.", Effect = DialogueEffect.Farewell } } },
                },
            };
            npc.LookAt(Surface(arrival) + Vector3.Up * 0.1f, Vector3.Up);
            GD.Print($"[DungeonBuilder] floor {floorIndex}: fighter Foehammer at {fCell} (model {(npc.HasModel ? "loaded" : "missing")})");
        }
        if (_enemies.ContainsKey(ViewerOrcId) && (StepsFrom(layout, arrival, 8) ?? StepsFrom(layout, arrival, 7) ?? StepsFrom(layout, arrival, 6)) is { } oCell)
        {
            var orc = SpawnEnemy(ViewerOrcId, Surface(oCell) + Vector3.Up * 0.1f, floorIndex);
            GD.Print($"[DungeonBuilder] floor {floorIndex}: viewer orc at {oCell} ({(orc != null ? "spawned" : "not spawned")})");
        }
    }

    /// <summary>A plain floor cell exactly <paramref name="steps"/> walkable steps from <paramref name="from"/>, or null when there is none.</summary>
    private static GridPos? StepsFrom(FloorLayout layout, GridPos from, int steps)
    {
        ReadOnlySpan<GridPos> dirs = stackalloc GridPos[] { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };
        var seen = new HashSet<GridPos> { from };
        var ring = new List<GridPos> { from };
        for (var d = 0; d < steps && ring.Count > 0; d++)
        {
            var next = new List<GridPos>();
            foreach (var c in ring)
                foreach (var dir in dirs)
                {
                    var n = c + dir;
                    if (!layout.IsWalkable(n) || !layout.CanStep(c, n) || !seen.Add(n)) continue;
                    next.Add(n);
                }
            ring = next;
        }
        foreach (var c in ring)
            if (layout[c] is Cell.Floor && c != layout.UpStairs && c != layout.DownStairs && !layout.IsWater(c) && !layout.IsUnder(c) && !layout.IsReserved(c)) return c;
        return null;
    }

    public static GridPos WalkableNeighbor(FloorLayout layout, GridPos cell)
    {
        ReadOnlySpan<GridPos> dirs = stackalloc GridPos[] { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };
        foreach (var d in dirs)
        {
            var n = cell + d;
            if (layout.IsWalkable(n) && n != layout.UpStairs && n != layout.DownStairs && layout[n] is Cell.Floor && layout.CanStep(cell, n)) return n;
        }
        return cell;
    }

    // ---- structure ---------------------------------------------------------------------------

    /// <summary>The floor's look: the campaign's choice when it names a valid theme of this tier, else the seed's weighted pick.</summary>
    public ThemeDef? ThemeFor(int floor, ulong gameSeed)
    {
        var tierIndex = TierRules.TierIndex(floor);
        if (DebugTools.Instance?.ForcedTheme is { } forced && _themes.FirstOrDefault(t => t.Id == forced) is { } forcedTheme) return forcedTheme;
        var wanted = Game.Instance?.Adventure?.Campaign?.OutlineFor(floor)?.Theme;
        if (wanted != null && _themes.FirstOrDefault(t => t.Id == wanted && (t.TierIndex == tierIndex || t.TierIndex < 0)) is { } chosen) return chosen;
        var pick = ThemeProfile.Pick(ThemeProfiles(), tierIndex, gameSeed, floor);
        return pick == null ? null : _themes.FirstOrDefault(t => t.Id == pick.Id);
    }

    private GridMap BuildGridMap(FloorLayout layout, ThemeDef? theme)
    {
        _vaultKind = VaultRules.Parse(DebugTools.Instance?.ForcedVault ?? theme?.Vault, theme?.TierIndex ?? 0);
        var key = (theme?.Id ?? "default") + ":" + _vaultKind;
        if (!_libraries.TryGetValue(key, out var lib))
        {
            lib = BuildLibrary(theme);
            _libraries[key] = lib;
        }
        var grid = new GridMap
        {
            Name = "Structure", MeshLibrary = lib, CellSize = new Vector3(CellSize, CellSize, CellSize), CollisionLayer = 1, CollisionMask = 0,
        };
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPos(x, y);
                var level = layout.LevelOf(p);
                var under = layout.IsUnder(p);
                switch (layout[p])
                {
                    case Cell.Floor or Cell.Door:
                        // the down-stairs cell is a well the stairs prop floors itself
                        if (!layout.IsWater(p) && p != layout.DownStairs) grid.SetCellItem(new Vector3I(x, level, y), _buckled.TryGetValue(p, out var variant) ? ItemBuckledBase + variant : ItemFloor);
                        // a low cell beside an upper floor gets a wall cube above it (its ceiling doubles as the upper room's side);
                        // ledges are the exception, they are meant to overlook the room below
                        var besideUpper = level == 0 && Neighbours4(p).Any(n => layout[n] is Cell.Floor or Cell.Door && layout.LevelOf(n) == 1 && !layout.Ledges.Any(l => l.Cells.Contains(n)));
                        grid.SetCellItem(new Vector3I(x, level + 1, y), besideUpper ? ItemWall : CeilingFor(layout, p, level));
                        // an upper floor stands on a solid plinth unless a cellar or tunnel runs beneath it
                        if (level == 1 && !under && p != layout.DownStairs) grid.SetCellItem(new Vector3I(x, 0, y), ItemWall);
                        break;
                    case Cell.Pit:
                        grid.SetCellItem(new Vector3I(x, level + 1, y), CeilingFor(layout, p, level));
                        break;
                    case Cell.Ramp:
                        var ramp = layout.Ramps[p];
                        var dirIndex = Array.IndexOf(RampDirs, ramp.Dir);
                        grid.SetCellItem(new Vector3I(x, level, y), ItemRampBase + dirIndex * 2 + (ramp.LowHeight % 4 == 2 ? 1 : 0));
                        grid.SetCellItem(new Vector3I(x, level + 2, y), CeilingFor(layout, p, level));
                        break;
                    case Cell.Wall when layout.IsChamfer(p, out var open):
                        // a corner cut at 45°: the solid half faces away from the floors, the open half gets its own floor
                        grid.SetCellItem(new Vector3I(x, 0, y), ItemChamferBase + ChamferIndex(open));
                        grid.SetCellItem(new Vector3I(x, 1, y), CeilingFor(layout, p, 0));
                        break;
                    case Cell.Wall or Cell.Secret:
                        if (!under) grid.SetCellItem(new Vector3I(x, 0, y), ItemWall);
                        grid.SetCellItem(new Vector3I(x, 1, y), ItemWall);
                        break;
                }
                if (under)
                {
                    grid.SetCellItem(new Vector3I(x, 0, y), ItemFloor);
                    // a tunnel is roofed by a full cube (nothing can walk on it); a cellar under an upper room is roofed by that room's floor
                    if (layout[p] is not (Cell.Floor or Cell.Door or Cell.Pit)) grid.SetCellItem(new Vector3I(x, 1, y), ItemWall);
                    else if (level != 1) grid.SetCellItem(new Vector3I(x, 1, y), CeilingFor(layout, p, 0));
                }
            }
        // enclose the under layer: cells beside a cellar/tunnel that are open at ground level get a low wall cube
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPos(x, y);
                if (layout.IsUnder(p)) continue;
                if (layout[p] is Cell.Wall or Cell.Secret) continue;
                if (layout[p] is Cell.Floor or Cell.Door && layout.LevelOf(p) == 0) continue;
                if (layout[p] == Cell.Ramp && layout.Ramps[p].LowHeight < 4) continue;
                var touches = false;
                for (var oy = -1; oy <= 1 && !touches; oy++)
                    for (var ox = -1; ox <= 1; ox++)
                        if ((ox != 0 || oy != 0) && layout.IsUnder(new GridPos(x + ox, y + oy))) { touches = true; break; }
                if (!touches) continue;
                if (grid.GetCellItem(new Vector3I(x, 0, y)) == GridMap.InvalidCellItem) grid.SetCellItem(new Vector3I(x, 0, y), ItemWall);
                // full height on empty cells, so the cube's top is never a step from anything walkable
                if (layout[p] == Cell.Void && grid.GetCellItem(new Vector3I(x, 1, y)) == GridMap.InvalidCellItem) grid.SetCellItem(new Vector3I(x, 1, y), ItemWall);
            }
        return grid;
    }

    private static int ChamferIndex(GridPos open) => (open.X > 0 ? 0 : 1) + (open.Y > 0 ? 0 : 2);

    /// <summary>The yaw that lays a box's X axis along <paramref name="along"/> (RotateY sends +X to (cos t, 0, -sin t)).</summary>
    public static float Yaw(Vector3 along) => Mathf.Atan2(-along.Z, along.X);

    /// <summary>
    /// Which floor slabs are buckled: nearly half of a ruined room, most cells beside a rubble mound, and one in
    /// twenty elsewhere. Never stairs, doors, ramps, ledges, water or the under layer.
    /// </summary>
    private void PickBuckled(FloorLayout layout)
    {
        _buckled.Clear();
        var rng = new Random(layout.Seed ^ 0xB0C1);
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPos(x, y);
                if (layout[p] != Cell.Floor || layout.IsWater(p) || layout.IsUnder(p) || p == layout.UpStairs || p == layout.DownStairs) continue;
                if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)) continue;
                if (layout.Vaults.Any(v => v.Door == p || v.Key == p || v.Lever == p)) continue;
                if (layout.Structures.Any(s => s.Kind == StructureKind.Camp && (s.Cells.Contains(p) || s.Anchor == p))) continue;
                var room = layout.RoomAt(p);
                var chance = room != null && layout.StructureAt(room)?.Kind == StructureKind.Ruin ? 0.42
                    : layout.Rubble.Any(r => r.ManhattanDistance(p) == 1) ? 0.55
                    : layout.Rubble.Contains(p) ? 0.0 : 0.05;
                if (rng.NextDouble() < chance) _buckled[p] = rng.Next(BuckledVariants);
            }
    }

    /// <summary>Height of the water in a pond or stream cell (the floor top, less the drop).</summary>
    public float WaterLevel(GridPos cell) => Surface(cell).Y - WaterSurface.SurfaceDrop;

    /// <summary>Is this position (a body's feet) down in a pond or stream, rather than on a bridge or bank above it?</summary>
    public bool IsInWater(Vector3 world)
    {
        if (Layout == null) return false;
        var cell = WorldToCell(world);
        return Layout.IsWater(cell) && world.Y < WaterLevel(cell) + 0.12f;
    }

    /// <summary>
    /// The ceiling piece over a cell: the flat slab where the theme wants none, else a barrel vault along a corridor's
    /// run and a groin bay over rooms, corners and junctions. A side is open when the next cell is floor at the same
    /// level (ramps always count, so the vault runs on up a climb).
    /// </summary>
    private int CeilingFor(FloorLayout layout, GridPos p, int level, bool asOpen = false)
    {
        if (_vaultKind == ArchKind.Flat) return ItemCeiling;
        bool Open(GridPos n)
        {
            if (!layout.InBounds(n.X, n.Y)) return false;
            var c = layout[n];
            if (c == Cell.Ramp || layout[p] == Cell.Ramp) return c is Cell.Floor or Cell.Door or Cell.Pit or Cell.Ramp;
            if (c == Cell.Secret && _openSecrets.Contains(n)) return true;
            if (c is not (Cell.Floor or Cell.Door or Cell.Pit)) return false;
            return asOpen || layout.LevelOf(n) == layout.LevelOf(p);
        }
        var piece = VaultRules.PieceFor(Open(new GridPos(p.X - 1, p.Y)), Open(new GridPos(p.X + 1, p.Y)), Open(new GridPos(p.X, p.Y - 1)), Open(new GridPos(p.X, p.Y + 1)));
        return ItemVaultBase + (int)piece;
    }

    private ArchKind _vaultKind = ArchKind.Semicircular;

    /// <summary>How far above the wall tops the ceiling sits at a cell's centre: a vault's apex, or just under a flat slab.</summary>
    public float CeilingRise => _vaultKind == ArchKind.Flat ? -0.2f : VaultRules.Rise(_vaultKind, CellSize / 2f);
    /// <summary>Height of the ceiling above a cell's floor surface, at the cell's centre.</summary>
    public float CeilingAbove => LevelHeight + CeilingRise;

    private static IEnumerable<GridPos> Neighbours4(GridPos p)
    {
        yield return new GridPos(p.X + 1, p.Y);
        yield return new GridPos(p.X - 1, p.Y);
        yield return new GridPos(p.X, p.Y + 1);
        yield return new GridPos(p.X, p.Y - 1);
    }

    /// <summary>PBR material from a Poly Haven texture folder, triplanar so it tiles across box geometry; falls back to a flat color.</summary>
    public static StandardMaterial3D Material(string textureName, Color tint, float metersPerTile = 2f)
    {
        var mat = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.9f };
        if (textureName == "") return mat;
        Texture2D? Load(string suffix)
        {
            var path = $"res://assets/textures/{textureName}/{textureName}_{suffix}_1k.jpg";
            return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }
        var diff = Load("diff");
        if (diff == null) { GD.PushWarning($"[DungeonBuilder] texture {textureName} not imported; using flat color"); return mat; }
        mat.AlbedoTexture = diff;
        mat.AlbedoColor = Colors.White.Lerp(tint, 0.35f);
        if (Load("nor") is { } nor) { mat.NormalEnabled = true; mat.NormalTexture = nor; mat.NormalScale = 1.0f; }
        if (Load("rough") is { } rough) { mat.RoughnessTexture = rough; mat.Roughness = 1f; }
        mat.Uv1Triplanar = true;
        mat.Uv1Scale = Vector3.One / metersPerTile;
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        return mat;
    }

    private static MeshLibrary BuildLibrary(ThemeDef? tier)
    {
        var lib = new MeshLibrary();
        var slabOffset = new Vector3(0, -CellSize / 2f + 0.1f, 0);
        var tile = tier?.MetersPerTile ?? 2f;
        var floorMat = Material(tier?.FloorTexture ?? "", tier?.FloorColor ?? new Color(0.32f, 0.30f, 0.28f), tile);
        var wallMat = Material(tier?.WallTexture ?? "", tier?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), tile);
        var ceilMat = Material(tier?.CeilingTexture ?? "", tier?.CeilingColor ?? new Color(0.18f, 0.17f, 0.17f), tile);
        AddBoxItem(lib, ItemFloor, "Floor", new Vector3(CellSize, 0.2f, CellSize), slabOffset, floorMat);
        AddBoxItem(lib, ItemWall, "Wall", new Vector3(CellSize, CellSize, CellSize), Vector3.Zero, wallMat);
        AddBoxItem(lib, ItemCeiling, "Ceiling", new Vector3(CellSize, 0.2f, CellSize), slabOffset, ceilMat);
        AddVaultItems(lib, VaultRules.Parse(DebugTools.Instance?.ForcedVault ?? tier?.Vault, tier?.TierIndex ?? 0), ceilMat);

        // ramps: a slab rising 2 m over one cell, in four directions, for the lower and upper half of a climb
        var rampMat = Material(tier?.FloorTexture ?? "", (tier?.FloorColor ?? new Color(0.32f, 0.30f, 0.28f)).Lerp(new Color(0.9f, 0.7f, 0.4f), 0.5f), tile); // warmer tint marks a ramp
        var angle = Mathf.Atan2(2f, CellSize);
        var length = Mathf.Sqrt(CellSize * CellSize + 4f);
        for (var d = 0; d < RampDirs.Length; d++)
            for (var half = 0; half < 2; half++)
            {
                var dir = RampDirs[d];
                var alongX = dir.X != 0;
                var size = alongX ? new Vector3(length, 0.2f, CellSize) : new Vector3(CellSize, 0.2f, length);
                // rotate so the +dir end is higher
                var basis = alongX ? new Basis(Vector3.Back, dir.X > 0 ? angle : -angle) : new Basis(Vector3.Right, dir.Y > 0 ? -angle : angle);
                var lowHeight = half * 2f;
                var offset = new Vector3(0, lowHeight + 1f - CellSize / 2f + 0.1f, 0);
                var id = ItemRampBase + d * 2 + half;
                var xf = new Transform3D(basis, offset);
                // the slope plus a floor slab underneath: the space below a ramp must never be a hole (tunnels can run beside it)
                var slope = new BoxMesh { Size = size };
                var slab = new BoxMesh { Size = new Vector3(CellSize, 0.2f, CellSize) };
                var slabXf = new Transform3D(Basis.Identity, slabOffset);
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                st.AppendFrom(slope, 0, xf);
                st.AppendFrom(slab, 0, slabXf);
                var mesh = st.Commit();
                mesh.SurfaceSetMaterial(0, rampMat);
                lib.CreateItem(id);
                lib.SetItemName(id, $"Ramp{dir}{half}");
                lib.SetItemMesh(id, mesh);
                lib.SetItemMeshTransform(id, Transform3D.Identity);
                lib.SetItemShapes(id, new Godot.Collections.Array { new BoxShape3D { Size = size }, xf, new BoxShape3D { Size = slab.Size }, slabXf });
            }

        // 45° wall pieces: a triangular prism filling the half of the cell away from the open diagonal, over a full floor slab
        var hc = CellSize / 2f;
        for (var i = 0; i < 4; i++)
        {
            var dx = i % 2 == 0 ? 1 : -1;
            var dy = i < 2 ? 1 : -1;
            var corner = new Vector3(-dx * hc, 0, -dy * hc);
            var alongX = new Vector3(dx * hc, 0, -dy * hc);
            var alongZ = new Vector3(-dx * hc, 0, dy * hc);
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            var lo = -hc;
            var hi = hc;
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = (b - a).Cross(c - a).Normalized();
                var centre = (a + b + c) / 3f;
                var outward = centre - new Vector3(corner.X * 0.5f, 0, corner.Z * 0.5f); // the prism's centroid sits a quarter cell in from the corner
                // Godot's front faces wind clockwise seen from outside, so the right-hand normal of a front face points inward
                if (n.Dot(outward) > 0) { (b, c) = (c, b); n = -n; }
                n = -n;
                st.SetNormal(n); st.AddVertex(a); st.SetNormal(n); st.AddVertex(b); st.SetNormal(n); st.AddVertex(c);
            }
            Vector3 At(Vector3 p, float y) => new(p.X, y, p.Z);
            // top and bottom
            Tri(At(corner, hi), At(alongX, hi), At(alongZ, hi));
            Tri(At(corner, lo), At(alongX, lo), At(alongZ, lo));
            // the cut face
            Tri(At(alongX, lo), At(alongZ, lo), At(alongZ, hi));
            Tri(At(alongX, lo), At(alongZ, hi), At(alongX, hi));
            // the two sides along the cell boundary
            Tri(At(corner, lo), At(alongX, lo), At(alongX, hi));
            Tri(At(corner, lo), At(alongX, hi), At(corner, hi));
            Tri(At(corner, lo), At(alongZ, lo), At(alongZ, hi));
            Tri(At(corner, lo), At(alongZ, hi), At(corner, hi));
            var prism = st.Commit();
            prism.SurfaceSetMaterial(0, wallMat);
            var slabTool = new SurfaceTool();
            slabTool.Begin(Mesh.PrimitiveType.Triangles);
            slabTool.AppendFrom(new BoxMesh { Size = new Vector3(CellSize, 0.2f, CellSize) }, 0, new Transform3D(Basis.Identity, slabOffset));
            var mesh = slabTool.Commit(prism);
            mesh.SurfaceSetMaterial(1, floorMat);
            var id = ItemChamferBase + i;
            lib.CreateItem(id);
            lib.SetItemName(id, $"Chamfer{dx},{dy}");
            lib.SetItemMesh(id, mesh);
            lib.SetItemMeshTransform(id, Transform3D.Identity);
            var points = new[] { At(corner, lo), At(alongX, lo), At(alongZ, lo), At(corner, hi), At(alongX, hi), At(alongZ, hi) };
            lib.SetItemShapes(id, new Godot.Collections.Array { new ConvexPolygonShape3D { Points = points }, Transform3D.Identity, new BoxShape3D { Size = new Vector3(CellSize, 0.2f, CellSize) }, new Transform3D(Basis.Identity, slabOffset) });
        }

        // buckled floors: the slab's top heaved into a ridge or a dome that settles back to floor level at the cell's
        // edge, so it is a bump underfoot and never a step (a step taller than a hand blocks a capsule)
        for (var v = 0; v < BuckledVariants; v++)
        {
            var id = ItemBuckledBase + v;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            const int n = 6;
            var half = CellSize / 2f;
            var top = slabOffset.Y + 0.1f; // the plain slab's top
            float Bump(float u, float w) // u, w in -1..1 across the cell
            {
                var edge = Mathf.Clamp(1f - Math.Max(Math.Abs(u), Math.Abs(w)), 0f, 1f);
                edge = edge * edge * (3f - 2f * edge);
                var shape = v switch
                {
                    0 => 1f - Math.Abs(w),                                   // ridge along X
                    1 => 1f - Math.Abs(u),                                   // ridge along Z
                    2 => 1f - Mathf.Clamp(MathF.Sqrt(u * u + w * w), 0f, 1f), // dome
                    _ => 1f - Math.Abs(u + w) / 1.4f,                        // diagonal ridge
                };
                return top + Mathf.Clamp(shape, 0f, 1f) * edge * (0.3f + v * 0.03f);
            }
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    float u0 = -1f + 2f * i / n, u1 = -1f + 2f * (i + 1) / n, w0 = -1f + 2f * j / n, w1 = -1f + 2f * (j + 1) / n;
                    var a = new Vector3(u0 * half, Bump(u0, w0), w0 * half);
                    var b = new Vector3(u1 * half, Bump(u1, w0), w0 * half);
                    var c = new Vector3(u1 * half, Bump(u1, w1), w1 * half);
                    var d = new Vector3(u0 * half, Bump(u0, w1), w1 * half);
                    st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); // clockwise from above: front faces
                    st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
                }
            st.GenerateNormals();
            var bump = st.Commit();
            var slabTool = new SurfaceTool();
            slabTool.Begin(Mesh.PrimitiveType.Triangles);
            slabTool.AppendFrom(new BoxMesh { Size = new Vector3(CellSize, 0.2f, CellSize) }, 0, new Transform3D(Basis.Identity, slabOffset));
            var mesh = slabTool.Commit(bump);
            mesh.SurfaceSetMaterial(0, floorMat);
            mesh.SurfaceSetMaterial(1, floorMat);
            lib.CreateItem(id);
            lib.SetItemName(id, $"Buckled{v}");
            lib.SetItemMesh(id, mesh);
            lib.SetItemMeshTransform(id, Transform3D.Identity);
            lib.SetItemShapes(id, new Godot.Collections.Array { bump.CreateTrimeshShape(), Transform3D.Identity, new BoxShape3D { Size = new Vector3(CellSize, 0.2f, CellSize) }, new Transform3D(Basis.Identity, slabOffset) });
        }

        return lib;
    }

    /// <summary>
    /// Vaulted ceiling pieces: the vault springs from the top of the walls (the bottom of the ceiling layer) and rises
    /// into the layer, where the taller wall cubes beside a corridor close its sides. A barrel keeps one profile along
    /// its run; a groin bay is two barrels crossing, the ceiling following whichever is higher, so each of its four
    /// edges is an arch that meets the next bay's (or a wall cube) without a seam. Faces wind clockwise seen from below.
    /// </summary>
    private static void AddVaultItems(MeshLibrary lib, ArchKind kind, Material material)
    {
        const int n = 12;
        var half = CellSize / 2f;
        var spring = -CellSize / 2f;
        foreach (var (piece, broken) in new[] { VaultPiece.BarrelX, VaultPiece.BarrelZ, VaultPiece.Groin }.SelectMany(p => new[] { (p, false), (p, true) }))
        {
            var id = (broken ? ItemVaultBrokenBase : ItemVaultBase) + (int)piece;
            const float holeRadius = 1.25f; // a broken piece has its crown missing: a ruin's collapse hangs its throat over the gap
            float Height(float u, float w) => kind == ArchKind.Flat ? 0.1f : piece switch
            {
                VaultPiece.BarrelX => VaultRules.Height(kind, w, half),
                VaultPiece.BarrelZ => VaultRules.Height(kind, u, half),
                _ => VaultRules.GroinHeight(kind, u, w, half),
            };
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    float u0 = -1f + 2f * i / n, u1 = -1f + 2f * (i + 1) / n, w0 = -1f + 2f * j / n, w1 = -1f + 2f * (j + 1) / n;
                    // each web of a groin is its own smoothing group so the crossing shows a crisp edge
                    var uc = (u0 + u1) / 2f; var wc = (w0 + w1) / 2f;
                    if (broken && (uc * uc + wc * wc) * half * half < holeRadius * holeRadius) continue;
                    st.SetSmoothGroup(piece == VaultPiece.Groin && VaultRules.Height(kind, uc, half) < VaultRules.Height(kind, wc, half) ? 2u : 1u);
                    var a = new Vector3(u0 * half, spring + Height(u0, w0), w0 * half);
                    var b = new Vector3(u1 * half, spring + Height(u1, w0), w0 * half);
                    var c = new Vector3(u1 * half, spring + Height(u1, w1), w1 * half);
                    var d = new Vector3(u0 * half, spring + Height(u0, w1), w1 * half);
                    st.AddVertex(a); st.AddVertex(c); st.AddVertex(b); // clockwise from below: front faces
                    st.AddVertex(a); st.AddVertex(d); st.AddVertex(c);
                }
            st.GenerateNormals();
            var mesh = st.Commit();
            mesh.SurfaceSetMaterial(0, material);
            lib.CreateItem(id);
            lib.SetItemName(id, broken ? $"Vault{piece}Broken" : $"Vault{piece}");
            lib.SetItemMesh(id, mesh);
            lib.SetItemMeshTransform(id, Transform3D.Identity);
            lib.SetItemShapes(id, new Godot.Collections.Array { mesh.CreateTrimeshShape(), Transform3D.Identity });
        }
    }

    private static void AddBoxItem(MeshLibrary lib, int id, string name, Vector3 size, Vector3 offset, Material material)
    {
        var mesh = new BoxMesh { Size = size, Material = material };
        var xf = new Transform3D(Basis.Identity, offset);
        lib.CreateItem(id);
        lib.SetItemName(id, name);
        lib.SetItemMesh(id, mesh);
        lib.SetItemMeshTransform(id, xf);
        lib.SetItemShapes(id, new Godot.Collections.Array { new BoxShape3D { Size = size }, xf });
    }

    // ---- contents ----------------------------------------------------------------------------

    private void PlaceStairs(FloorLayout layout, Node3D root, int floor)
    {
        var up = _stairsScene.Instantiate<Stairs>();
        up.Direction = -1;
        up.Name = "UpStairs";
        root.AddChild(up);
        up.GlobalTransform = StairsTransform(layout, layout.UpStairs);

        var down = _stairsScene.Instantiate<Stairs>();
        down.Direction = 1;
        down.Name = "DownStairs";
        var objective = layout.Objective?.Kind ?? FloorObjectiveKind.None;
        down.Locked = TierRules.IsBossFloor(floor) || FloorObjectives.LocksStairs(objective);
        if (FloorObjectives.LocksStairs(objective)) down.LockReason = Adventure.Objectives.ObjectiveText.LockReason(objective, Game.Instance?.Adventure?.Campaign?.Antagonist?.Name);
        root.AddChild(down);
        down.GlobalTransform = StairsTransform(layout, layout.DownStairs);
        // the flights are walled: nothing paths through them
        _propCells.Add(layout.UpStairs);
        _propCells.Add(layout.DownStairs);
    }

    /// <summary>The floor objective's controller and its props, once every other prop stands (it may lean on the portals and hordes).</summary>
    private void PlaceObjective(FloorLayout layout, FloorContents contents, Node3D root, int floor, int seed)
    {
        if (layout.Objective is not { } objective || contents.Objective is not { } oc) return;
        Adventure.Objectives.ObjectiveController? controller = objective.Kind switch
        {
            FloorObjectiveKind.Toxic => new Adventure.Objectives.ToxicController(),
            FloorObjectiveKind.ShiftingVault => new Adventure.Objectives.ShiftingVaultController(),
            FloorObjectiveKind.Ritual => new Adventure.Objectives.RitualController(),
            FloorObjectiveKind.Jailbreak => new Adventure.Objectives.JailbreakController(),
            _ => null,
        };
        if (controller == null) return;
        controller.Setup(layout, oc, floor, seed);
        root.AddChild(controller);
        controller.Build(this, root);
    }

    /// <summary>A puzzle gate: a lever-gate door that shows from the start, named, owned by the objective (index from <see cref="FloorContents.ExtraIndexBase"/>).</summary>
    public Door SpawnGate(Node3D root, GridPos cell, int index, string label)
    {
        var door = _doorScene.Instantiate<Door>();
        door.Kind = VaultKind.LeverGate;
        door.VaultIndex = index;
        door.Cell = cell;
        door.GateLabel = label;
        door.Name = $"Door{index}";
        root.AddChild(door);
        door.GlobalPosition = Surface(cell) with { Y = Layout!.LevelOf(cell) * LevelHeight };
        _closedDoors.Add(cell);
        door.Reveal();
        return door;
    }

    public Lever SpawnLever(Node3D root, GridPos cell, int index, bool toggle, string? label = null)
    {
        var l = _leverScene.Instantiate<Lever>();
        l.VaultIndex = index;
        l.Toggle = toggle;
        l.Label = label;
        l.Name = $"Lever{index}";
        root.AddChild(l);
        l.GlobalPosition = Surface(cell);
        return l;
    }

    /// <summary>One of the objective's people: no script, no quest, a mode instead.</summary>
    public Npc SpawnObjectiveNpc(Node3D root, string id, string name, GridPos cell, NpcMode mode, string? modelPath = null)
    {
        var npc = _npcScene.Instantiate<Npc>();
        npc.Id = id;
        npc.NpcName = name;
        npc.Mode = mode;
        npc.ModelPath = modelPath;
        npc.Floor = CurrentFloor;
        npc.Name = $"Npc_{id}";
        root.AddChild(npc);
        npc.GlobalPosition = Surface(cell) + Vector3.Up * 0.1f;
        return npc;
    }

    /// <summary>The shrine's altar: a stone block with candles, solid, in the middle of the room.</summary>
    public void SpawnAltar(Node3D root, GridPos cell)
    {
        var altar = new StaticBody3D { Name = "Altar", CollisionLayer = 1, CollisionMask = 0 };
        var stone = Material(CurrentTheme?.WallTexture ?? "", (CurrentTheme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f)).Darkened(0.15f), 1f);
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.08f, 0.1f), Roughness = 0.95f };
        altar.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.8f, 1.0f, 1.0f) }, MaterialOverride = stone, Position = new Vector3(0, 0.5f, 0) });
        altar.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.9f, 0.06f, 1.1f) }, MaterialOverride = cloth, Position = new Vector3(0, 1.03f, 0) });
        altar.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 1.05f, 1.0f) }, Position = new Vector3(0, 0.525f, 0) });
        var candle = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.85f, 0.7f), EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.3f), EmissionEnergyMultiplier = 1.5f };
        foreach (var x in new[] { -0.6f, 0.6f })
            altar.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 0.3f }, MaterialOverride = candle, Position = new Vector3(x, 1.2f, 0.25f) });
        altar.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.4f), LightEnergy = 1.6f, OmniRange = 7f, ShadowEnabled = false, Position = new Vector3(0, 1.8f, 0) });
        root.AddChild(altar);
        altar.GlobalPosition = Surface(cell);
        _propCells.Add(cell);
    }

    /// <summary>A breach mouth falls shut: rubble to the ceiling, the cell blocked for paths.</summary>
    public void SealMouth(Node3D root, GridPos mouth, bool silent)
    {
        if (Layout is not { } layout) return;
        var wallMat = Material(CurrentTheme?.WallTexture ?? "", CurrentTheme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), CurrentTheme?.MetersPerTile ?? 2f);
        var rng = new Random(mouth.X * 73856093 ^ mouth.Y * 19349663 ^ CurrentFloor);
        var node = new StaticBody3D { Name = $"Sealed_{mouth.X}_{mouth.Y}", CollisionLayer = 1, CollisionMask = 0 };
        var height = LevelHeight - 0.2f;
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(CellSize * 0.98f, height, CellSize * 0.98f) }, MaterialOverride = wallMat, Position = new Vector3(0, height / 2f, 0) });
        node.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(CellSize * 0.98f, height, CellSize * 0.98f) }, Position = new Vector3(0, height / 2f, 0) });
        root.AddChild(node);
        node.GlobalPosition = Surface(mouth) with { Y = layout.LevelOf(mouth) * LevelHeight };
        Rubble.Collapse(root, Surface(mouth), height, wallMat, rng);
        _propCells.Add(mouth);
        if (!silent) EventBus.Instance.PlaySfx("door");
    }

    public Plaque SpawnPlaque(Node3D root, GridPos cell, int index, string text)
    {
        var plaque = new Plaque { Index = index, Text = text };
        root.AddChild(plaque);
        plaque.GlobalPosition = Surface(cell);
        // face the middle of its room
        if (Layout?.RoomAt(cell) is { } room) { var centre = Surface(room.Center); if (centre.DistanceSquaredTo(plaque.GlobalPosition) > 0.01f) plaque.LookAt(new Vector3(centre.X, plaque.GlobalPosition.Y, centre.Z), Vector3.Up); }
        _propCells.Add(cell);
        return plaque;
    }

    /// <summary>Stairs open toward the cell the party spawns on (the first walkable neighbour), their back to the far side.</summary>
    private Transform3D StairsTransform(FloorLayout layout, GridPos cell)
    {
        var front = WalkableNeighbor(layout, cell);
        var back = front == cell ? new Vector3(0, 0, -1) : (CellToWorld(cell) - CellToWorld(front)) with { Y = 0 };
        return new Transform3D(Basis.LookingAt(back.Normalized(), Vector3.Up), Surface(cell));
    }

    public CollectiveRegistry? Collectives { get; private set; }

    /// <summary>Hordes waiting in the walls; spent ones (from a save) stay quiet.</summary>
    /// <summary>The tier's ordinary creatures (its enemy pool, no boss): what ambushes and portals draw on.</summary>
    public List<string> RegularEnemies(int floor)
    {
        var pool = TierFor(floor)?.ToProfile().EnemyPool ?? Array.Empty<string>();
        var ids = pool.Where(id => _enemies.TryGetValue(id, out var p) && !p.IsBoss).Distinct().ToList();
        if (ids.Count == 0) ids = _enemies.Values.Where(p => !p.IsBoss && p.Faction == "").Select(p => p.Id).OrderBy(id => id).ToList();
        return ids;
    }

    /// <summary>Seeded ambushers along the corridors (see <see cref="AmbushRules"/>); none when the setting or a debug flag says so.</summary>
    public void PlaceAmbushes(FloorLayout layout, int floor, int seed, Save.FloorStateDto? state)
    {
        var count = Settings.Ambushes && DebugTools.Instance?.NoAmbush != true ? AmbushRules.Count(floor) : 0;
        var points = AmbushRules.Place(layout, RegularEnemies(floor), seed, count);
        var spawner = new AmbushSpawner();
        _floorRoot!.AddChild(spawner);
        spawner.Setup(layout, floor, points, state?.SprungAmbushes);
    }

    /// <summary>The boss's reinforcement portals for this floor (see <see cref="PortalRules"/>), with the saved pressure clock.</summary>
    public void PlacePortals(FloorLayout layout, int floor, int seed, Save.FloorStateDto? state)
    {
        var spawner = new PortalSpawner();
        _floorRoot!.AddChild(spawner);
        spawner.Setup(layout, floor, RegularEnemies(floor), seed, state?.PressureSeconds ?? 0f, state?.PortalWaves ?? 0);
    }

    public void PlaceHordes(FloorLayout layout, FloorContents contents, int floor, IReadOnlyCollection<string> spent)
    {
        foreach (var h in contents.Hordes)
        {
            var spawner = new HordeSpawner();
            _floorRoot!.AddChild(spawner);
            spawner.Setup(h, layout, floor, spent.Contains(h.Id));
        }
    }

    private void PlaceEnemies(FloorContents contents, int floor)
    {
        var registry = new CollectiveRegistry { Name = "Collectives" };
        _floorRoot!.AddChild(registry);
        Collectives = registry;
        foreach (var cc in contents.Collectives) registry.Collectives.Add(new Collective(cc, cell => Surface(cell), registry));
        foreach (var e in contents.Enemies)
        {
            var group = e.CollectiveId == null ? null : registry.Find(e.CollectiveId);
            SpawnEnemy(e.EnemyId, Surface(e.Cell, e.Under) + Vector3.Up * 0.1f, floor, e.SpawnIndex, group, e.Ordinal);
        }
        if (contents.BossId is { } bossId && contents.BossCell is { } bossCell)
        {
            var boss = SpawnEnemy(bossId, Surface(bossCell) + new Vector3(0, 0.1f, 2f), floor, -2);
            if (boss != null && contents.BossSignature is { } sig && Enum.TryParse<Signature>(sig, true, out var parsed)) boss.Profile = boss.Profile with { Signature = parsed };
        }
    }

    /// <summary>Design a floor with the offline designer against the layout its seed will produce.</summary>
    public (FloorPlan Plan, ValidationReport Report) DesignOffline(int floor, ulong gameSeed, PartySnapshot party, float difficulty = 1f)
    {
        var seed = Rng.FloorSeed(gameSeed, floor);
        var spec = SpecFor(floor);
        var layout = FloorGenerator.Generate(spec, seed);
        return AdventurePlanning.DesignOffline(layout, spec, TierFor(floor)?.ToProfile(), _enemies, floor, seed, party, difficulty);
    }

    /// <summary>Instantiate an enemy by id. SpawnIndex -1 = summoned, -2 = boss.</summary>
    public Enemy? SpawnEnemy(string id, Vector3 position, int floor, int spawnIndex = -1, Collective? group = null, int ordinal = 0, int netId = -1)
    {
        if (_floorRoot == null) return null;
        var profile = EnemyProfile(id) ?? EnemyProfile("enemy.ghoul") ?? new EnemyProfile();
        var enemy = _enemyScene.Instantiate<Enemy>();
        enemy.Profile = profile;
        enemy.Floor = floor;
        enemy.SpawnIndex = spawnIndex;
        enemy.Group = group;
        enemy.GroupOrdinal = ordinal;
        enemy.HomePosition = position;
        // a network id every peer agrees on: planned enemies by spawn index, the boss fixed, the rest in spawn order
        enemy.NetId = netId >= 0 ? netId : spawnIndex == -2 ? Enemy.BossNetId : spawnIndex >= 0 ? spawnIndex : Enemy.DynamicNetIdBase + _dynamicSerial++;
        enemy.Name = spawnIndex == -2 ? "Boss" : spawnIndex >= 0 ? $"Enemy{spawnIndex}" : $"Dyn{enemy.NetId - Enemy.DynamicNetIdBase}";
        group?.Add(enemy);
        _floorRoot.AddChild(enemy);
        enemy.GlobalPosition = position;
        if (spawnIndex < 0 && spawnIndex != -2) EventBus.Instance.RaiseEnemySpawned(enemy); // dynamic: a server tells its clients
        return enemy;
    }

    private void PlaceProps(FloorLayout layout, FloorContents contents, Node3D root, int floor, int seed)
    {
        var cat = Game.Instance.Catalog;
        for (var i = 0; i < layout.ChestSpawns.Count; i++)
        {
            var chest = _chestScene.Instantiate<Chest>();
            chest.SpawnIndex = i;
            chest.Locked = layout.LockedChests.Contains(i);
            chest.Name = $"Chest{i}";
            root.AddChild(chest);
            chest.GlobalPosition = Surface(layout.ChestSpawns[i]);
            _propCells.Add(layout.ChestSpawns[i]);
            chest.RotateY((float)(Rng.Mix((ulong)seed ^ (ulong)(i + 1)) % 4) * Mathf.Pi / 2f);
        }
        foreach (var extra in contents.ExtraChests)
        {
            var chest = _chestScene.Instantiate<Chest>();
            chest.SpawnIndex = extra.SpawnIndex;
            chest.Locked = extra.Locked;
            chest.Name = $"Chest{extra.SpawnIndex}";
            root.AddChild(chest);
            chest.GlobalPosition = Surface(extra.Cell);
            _propCells.Add(extra.Cell);
            chest.RotateY((float)(Rng.Mix((ulong)seed ^ (ulong)(extra.SpawnIndex + 1)) % 4) * Mathf.Pi / 2f);
        }
        for (var i = 0; i < layout.BreakableSpawns.Count; i++)
        {
            var barrel = _barrelScene.Instantiate<Breakable>();
            barrel.SpawnIndex = i;
            barrel.Name = $"Barrel{i}";
            root.AddChild(barrel);
            var jitter = new Vector3(((int)(Rng.Mix((ulong)seed ^ (ulong)(i * 31 + 7)) % 100) - 50) / 100f, 0, ((int)(Rng.Mix((ulong)seed ^ (ulong)(i * 17 + 3)) % 100) - 50) / 100f);
            barrel.GlobalPosition = Surface(layout.BreakableSpawns[i]) + jitter;
        }
        for (var i = 0; i < layout.TrapSpawns.Count; i++)
        {
            var trap = _trapScene.Instantiate<Trap>();
            trap.SpawnIndex = i;
            trap.Floor = floor;
            trap.Kind = (TrapKind)(Rng.Mix((ulong)seed ^ (ulong)(i * 101 + 11)) % 3);
            trap.Name = $"Trap{i}";
            root.AddChild(trap);
            trap.GlobalPosition = Surface(layout.TrapSpawns[i]);
        }
        var stone = Material(CurrentTheme?.WallTexture ?? "", CurrentTheme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), CurrentTheme?.MetersPerTile ?? 2f);
        foreach (var h in layout.Hazards)
        {
            if (h.Kind != HazardKind.Press) continue; // spike beds arrive with their own node
            var press = new Props.Press { Name = $"Press{h.Index}" };
            press.Configure(h, floor, stone, this);
            root.AddChild(press);
            _hazards.Add(press);
        }
        foreach (var n in contents.Npcs)
        {
            var plan = contents.Plan?.Npcs.FirstOrDefault(x => x.Id == n.Id);
            if (plan == null) continue;
            var npc = _npcScene.Instantiate<Npc>();
            npc.Id = plan.Id;
            npc.NpcName = plan.Name;
            npc.Script = plan.Script;
            npc.QuestKey = plan.QuestId == null ? null : QuestKeys.For(floor, plan.QuestId);
            npc.Floor = floor;
            npc.Name = $"Npc_{plan.Id}";
            root.AddChild(npc);
            npc.GlobalPosition = Surface(n.Cell) + Vector3.Up * 0.1f;
            npc.RotateY((float)(Rng.Mix((ulong)seed ^ (Rng.StableHash(n.Id) & 0xFFFF)) % 8) * Mathf.Pi / 4f);
        }
        foreach (var q in contents.QuestItems)
        {
            var drop = _lootScene.Instantiate<LootDrop>();
            drop.Item = ItemInstance.Simple("item.quest_relic", q.ItemName, id: Rng.StableGuid((ulong)seed ^ Rng.StableHash($"{q.QuestId}#{q.Ordinal}")));
            drop.QuestId = QuestKeys.Item(floor, q.QuestId, q.Ordinal);
            drop.Name = $"QuestItem_{q.QuestId}_{q.Ordinal}";
            root.AddChild(drop);
            drop.GlobalPosition = Surface(q.Cell) + Vector3.Up * 0.4f;
        }
        foreach (var extra in contents.ExtraTraps)
        {
            var trap = _trapScene.Instantiate<Trap>();
            trap.SpawnIndex = extra.SpawnIndex;
            trap.Floor = floor;
            trap.Kind = Enum.TryParse<TrapKind>(extra.Kind, true, out var kind) ? kind : TrapKind.Spikes;
            trap.Name = $"Trap{extra.SpawnIndex}";
            root.AddChild(trap);
            trap.GlobalPosition = Surface(extra.Cell);
        }

        var keyIndex = 0;
        foreach (var v in layout.Vaults)
        {
            var door = _doorScene.Instantiate<Door>();
            door.Kind = v.Kind;
            door.VaultIndex = v.Index;
            door.Cell = v.Door;
            door.Name = $"Door{v.Index}";
            root.AddChild(door);
            door.GlobalPosition = Surface(v.Door) with { Y = layout.LevelOf(v.Door) * LevelHeight }; // a full cube: no orientation to worry about
            _closedDoors.Add(v.Door);
            if (v.Revealed) door.Reveal();

            if (v.Lever is { } lever)
            {
                var l = _leverScene.Instantiate<Lever>();
                l.VaultIndex = v.Index;
                l.Name = $"Lever{v.Index}";
                root.AddChild(l);
                l.GlobalPosition = Surface(lever);
            }
            if (v.Key is { } key) PlaceKey(root, cat, key, keyIndex++);
        }
        foreach (var key in layout.KeySpawns) PlaceKey(root, cat, key, keyIndex++);

        foreach (var cellar in layout.Cellars)
        {
            var chest = _chestScene.Instantiate<Chest>();
            chest.SpawnIndex = layout.ChestSpawns.Count + cellar.Cells.IndexOf(cellar.Chest);
            chest.Name = $"CellarChest{chest.SpawnIndex}";
            root.AddChild(chest);
            chest.GlobalPosition = Surface(cellar.Chest, under: true);
        }
        foreach (var secret in layout.Secrets)
        {
            var wall = _secretScene.Instantiate<SecretWall>();
            wall.Index = secret.Index;
            wall.Cell = secret.Wall;
            wall.Level = layout.LevelOf(secret.Wall);
            wall.Name = $"Secret{secret.Index}";
            root.AddChild(wall);
            wall.GlobalPosition = CellToWorld(secret.Wall) with { Y = layout.LevelOf(secret.Wall) * LevelHeight };
            // face the seam toward the room
            var toRoom = secret.Facing - secret.Wall;
            wall.RotateY(toRoom.X > 0 ? -Mathf.Pi / 2f : toRoom.X < 0 ? Mathf.Pi / 2f : toRoom.Y > 0 ? 0f : Mathf.Pi);
        }
    }

    /// <summary>Wall lanterns in rooms (no shadows: many small lights), plus a statue in vaults and the arena.</summary>
    private void PlaceLightsAndDecor(FloorLayout layout, Node3D root, TierDef? tier, int seed)
    {
        var rng = new Random(seed ^ 0x1A17);
        var lantern = ResourceLoader.Exists("res://assets/models/wooden_lantern_01/wooden_lantern_01_1k.gltf") ? GD.Load<PackedScene>("res://assets/models/wooden_lantern_01/wooden_lantern_01_1k.gltf") : null;
        var statue = ResourceLoader.Exists("res://assets/models/gothic_statue/gothic_statue_1k.gltf") ? GD.Load<PackedScene>("res://assets/models/gothic_statue/gothic_statue_1k.gltf") : null;
        var perRoom = CurrentTheme?.WallLightsPerRoom ?? tier?.WallLightsPerRoom ?? 2;
        var torchColor = CurrentTheme?.TorchColor ?? tier?.TorchColor ?? new Color(1f, 0.85f, 0.65f);
        var count = 0;
        // cellars glow faintly so a pit reads as a drop with a floor, not a void; the tunnel mouth gets a spark too
        foreach (var cellar in layout.Cellars)
        {
            var glow = new OmniLight3D { LightColor = torchColor.Lerp(new Color(0.55f, 0.7f, 0.9f), 0.5f), LightEnergy = 1.1f, OmniRange = 11f, OmniAttenuation = 1.2f, ShadowEnabled = false, Name = $"CellarGlow{count++}" };
            root.AddChild(glow);
            glow.GlobalPosition = Surface(cellar.Pit, under: true) + Vector3.Up * 2.6f;
            if (cellar.Exit.Count > 0)
            {
                var mouth = new OmniLight3D { LightColor = torchColor, LightEnergy = 0.8f, OmniRange = 7f, OmniAttenuation = 1.3f, ShadowEnabled = false, Name = $"TunnelGlow{count++}" };
                root.AddChild(mouth);
                mouth.GlobalPosition = Surface(cellar.Exit[^1], under: true) + Vector3.Up * 2.4f;
            }
        }
        foreach (var room in layout.Rooms)
        {
            // candidate wall cells: walls orthogonally adjacent to the room's floor cells, on the room's own level
            var spots = new List<(GridPos wall, GridPos floor)>();
            foreach (var c in room.Cells())
            {
                if (layout[c] != Cell.Floor) continue;
                foreach (var d in RampDirs)
                {
                    var w = c + d;
                    if (layout[w] == Cell.Wall && !layout.IsUnder(w) && !layout.IsChamfer(w, out _)) spots.Add((w, c));
                }
            }
            var wanted = perRoom + (layout.StructureAt(room)?.Kind == StructureKind.Ruin ? 2 : 0); // a ruin shows its bones
            for (var i = 0; i < wanted && spots.Count > 0; i++)
            {
                var (wall, floor) = spots[rng.Next(spots.Count)];
                spots.RemoveAll(sp => sp.wall.ManhattanDistance(wall) < 3);
                var d = wall - floor;
                var wallFace = Surface(floor) + new Vector3(d.X, 0, d.Y) * (CellSize / 2f - 0.25f);
                var pos = new Vector3(wallFace.X, layout.LevelOf(floor) * LevelHeight + 2.0f, wallFace.Z);
                var light = new OmniLight3D { LightColor = torchColor, LightEnergy = 1.6f, OmniRange = 9f, OmniAttenuation = 1.3f, ShadowEnabled = false, Name = $"Lamp{count++}" };
                root.AddChild(light);
                light.GlobalPosition = pos + new Vector3(-d.X, 0, -d.Y) * 0.4f;
                if (lantern != null)
                {
                    var model = lantern.Instantiate<Node3D>();
                    root.AddChild(model);
                    model.GlobalPosition = pos - Vector3.Up * 0.35f;
                    model.RotateY(Mathf.Atan2(-d.X, -d.Y));
                }
            }
        }
        if (statue != null)
        {
            var spots = layout.Vaults.Select(v => v.Room).ToList();
            if (layout.BossArena is { } arena) spots.Add(arena);
            foreach (var room in spots.Where(r => r.W >= 4 && r.H >= 4))
            {
                var cell = new GridPos(room.X + 1, room.Y + 1);
                if (layout[cell] != Cell.Floor || layout.ChestSpawns.Contains(cell) || layout.EnemySpawns.Contains(cell)) continue;
                var model = statue.Instantiate<Node3D>();
                root.AddChild(model);
                model.GlobalPosition = Surface(cell);
                model.RotateY(rng.Next(4) * Mathf.Pi / 2f);
                _propCells.Add(cell);
                var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.5f, Height = 2.4f }, Position = new Vector3(0, 1.2f, 0) });
                model.AddChild(body);
            }
        }
    }

    // ---- structures: gatehouses and bandit camps ----------------------------------------------

    private void PlaceStructures(FloorLayout layout, Node3D root, int seed)
    {
        var rng = new Random(seed ^ 0x5A5A);
        var theme = CurrentTheme;
        var tile = theme?.MetersPerTile ?? 2f;
        var wallMat = Material(theme?.WallTexture ?? "", theme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), tile);
        var plankMat = Material("weathered_planks", new Color(0.55f, 0.42f, 0.28f), 1.6f);
        var darkWood = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.15f, 0.09f), Roughness = 0.9f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.13f, 0.12f), Metallic = 0.75f, Roughness = 0.5f };
        var index = 0;
        foreach (var s in layout.Structures)
        {
            var node = new Node3D { Name = $"{s.Kind}{index++}" };
            root.AddChild(node);
            node.GlobalPosition = Surface(s.Anchor); // children are placed in world space and made relative to this
            if (s.Kind == StructureKind.Gate) BuildGate(layout, node, s, wallMat, iron);
            else if (s.Kind == StructureKind.Camp) BuildCamp(layout, node, s, rng, plankMat, darkWood);
            else BuildRuin(layout, node, s, rng, wallMat);
        }
        if (layout.Kind == LayoutKind.Mine) PlaceMineTimbers(layout, root, plankMat, darkWood);
        // rubble: the layout's mounds (bodies path around them), then loose scatter along walls and in corridors
        var rubbleRoot = new Node3D { Name = "RubbleField" };
        root.AddChild(rubbleRoot);
        foreach (var cell in layout.Rubble)
        {
            var ruin = layout.RoomAt(cell) is { } r && layout.StructureAt(r)?.Kind == StructureKind.Ruin;
            Rubble.Mound(rubbleRoot, Surface(cell), wallMat, rng, ruin ? 1.15f : 1f);
            _propCells.Add(cell);
            if (ruin && rng.Next(2) == 0)
            {
                // under a vault the crown really falls in: the cell's ceiling piece becomes the holed variant and the
                // prop hangs a dark throat over the gap; under a flat slab the old black disc stands in for the hole
                var vaulted = false;
                if (_grid != null)
                {
                    var above = new Vector3I(cell.X, layout.LevelOf(cell) + 1, cell.Y);
                    var item = _grid.GetCellItem(above);
                    if (item >= ItemVaultBase && item < ItemVaultBase + 3) { _grid.SetCellItem(above, item - ItemVaultBase + ItemVaultBrokenBase); vaulted = true; }
                }
                Rubble.Collapse(rubbleRoot, Surface(cell), vaulted ? CeilingAbove : LevelHeight - 0.2f, wallMat, rng, vaulted);
            }
        }
        var scatterWant = 8 + rng.Next(6);
        for (var attempt = 0; attempt < 200 && scatterWant > 0; attempt++)
        {
            var p = new GridPos(rng.Next(layout.Width), rng.Next(layout.Height));
            if (layout[p] != Cell.Floor || layout.IsWater(p) || layout.IsUnder(p) || layout.IsReserved(p) || p == layout.UpStairs || p == layout.DownStairs) continue;
            if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p) || layout.Structures.Any(s => s.Kind == StructureKind.Camp && s.Cells.Contains(p))) continue;
            // tucked against a wall, out of the walking line
            var walls = RampDirs.Where(d => layout[p + d] is Cell.Wall).ToList();
            if (walls.Count == 0) continue;
            var d = walls[rng.Next(walls.Count)];
            Rubble.Scatter(rubbleRoot, Surface(p) + new Vector3(d.X, 0, d.Y) * 1.25f, wallMat, rng);
            scatterWant--;
        }
    }

    /// <summary>Each breach: a black throat in the back wall, jagged stone around the mouth, a sick glow inside.</summary>
    private void PlaceNestMouths(FloorLayout layout, Node3D root, int seed)
    {
        if (layout.Nests.Count == 0) return;
        var rng = new Random(seed ^ 0x4E57);
        var theme = CurrentTheme;
        var stone = Material(theme?.WallTexture ?? "", theme?.WallColor ?? new Color(0.45f, 0.42f, 0.40f), theme?.MetersPerTile ?? 2f);
        var throat = new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.01f, 0.01f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var index = 0;
        foreach (var nest in layout.Nests)
            for (var i = 0; i < nest.Mouths.Count; i++)
            {
                var node = new Node3D { Name = $"Nest_{index++}" };
                root.AddChild(node);
                var centre = Surface(nest.Mouths[i]);
                node.GlobalPosition = centre;
                var outward = new Vector3(nest.Outward[i].X, 0, nest.Outward[i].Y);
                var side = new Vector3(-outward.Z, 0, outward.X);
                var yaw = Yaw(side);
                // the throat: a black slab just in front of the back wall, and a ragged ring of stone around it
                Box(node, new Vector3(2.8f, 2.6f, 0.06f), centre - outward * (CellSize / 2f - 0.05f) + Vector3.Up * 1.35f, throat, collide: false, yaw: yaw);
                for (var k = 0; k < 7; k++)
                {
                    var a = k * Mathf.Tau / 7f + (float)rng.NextDouble() * 0.5f;
                    var ring = centre - outward * (CellSize / 2f - 0.25f) + side * Mathf.Cos(a) * 1.55f + Vector3.Up * (1.35f + Mathf.Sin(a) * 1.4f);
                    Box(node, new Vector3(0.35f + (float)rng.NextDouble() * 0.4f, 0.3f + (float)rng.NextDouble() * 0.4f, 0.5f), ring, stone, collide: false, yaw: yaw + ((float)rng.NextDouble() - 0.5f) * 0.8f);
                }
                Rubble.Scatter(node, centre + outward * 1.6f + side * ((float)rng.NextDouble() - 0.5f) * 1.5f, stone, rng);
                var glow = new OmniLight3D { LightColor = new Color(0.55f, 0.75f, 0.35f), LightEnergy = 0.9f, OmniRange = 5f, OmniAttenuation = 1.6f, ShadowEnabled = false };
                node.AddChild(glow);
                glow.GlobalPosition = centre - outward * 1.2f + Vector3.Up * 1.0f;
            }
    }

    /// <summary>A colonnade with a third of its columns down, extra rubble beside them, and the floor heaved (see PickBuckled).</summary>
    private void BuildRuin(FloorLayout layout, Node3D node, Structure ruin, Random rng, Material stone)
    {
        var along = new Vector3(ruin.Facing.X, 0, ruin.Facing.Y);
        var ceiling = CeilingAbove - 0.05f; // capitals meet the crown of the vault above each cell
        var fallenCount = Math.Max(1, ruin.Cells.Count / 3);
        var fallen = ruin.Cells.OrderBy(_ => rng.Next()).Take(fallenCount).ToHashSet();
        foreach (var cell in ruin.Cells)
        {
            var lie = along * (rng.Next(2) == 0 ? 1f : -1f);
            Rubble.Column(node, Surface(cell), ceiling, stone, rng, fallen.Contains(cell), lie);
            _propCells.Add(cell);
            if (fallen.Contains(cell)) Rubble.Scatter(node, Surface(cell) + new Vector3(-lie.Z, 0, lie.X) * 1.4f, stone, rng);
        }
        node.Name = $"Ruin_{ruin.Anchor.X}_{ruin.Anchor.Y}";
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 position, Material material, bool collide = true, float yaw = 0f, float roll = 0f)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position - parent.GlobalPosition };
        mi.RotateY(yaw);
        if (roll != 0f) mi.RotateZ(roll);
        parent.AddChild(mi);
        if (collide)
        {
            var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            mi.AddChild(body);
        }
        return mi;
    }

    /// <summary>A glTF model scaled so its tallest side is <paramref name="size"/> metres, standing on its base at <paramref name="position"/>.</summary>
    /// <summary>Burning floor patches (Burning floors): a hazard prop on each fire cell, which paths treat as solid.</summary>
    private void PlaceFires(FloorLayout layout, Node3D root, int floor)
    {
        if (layout.Fires.Count == 0) return;
        var node = new Node3D { Name = "Fires" };
        root.AddChild(node);
        var i = 0;
        foreach (var cell in layout.Fires.OrderBy(f => f.Y).ThenBy(f => f.X))
        {
            var fire = new FloorFire { Floor = floor, Name = $"FloorFire{i++}" };
            node.AddChild(fire);
            fire.GlobalPosition = Surface(cell);
            _propCells.Add(cell);
        }
    }

    /// <summary>
    /// Timber sets across a mine's galleries: two posts against the long walls and a cap beam over them every third
    /// cell, decorative only (no collision, so nothing snags on them), never where a passage comes in.
    /// </summary>
    private void PlaceMineTimbers(FloorLayout layout, Node3D root, Material plank, Material darkWood)
    {
        var node = new Node3D { Name = "Timbers" };
        root.AddChild(node);
        var post = new BoxMesh { Size = new Vector3(0.32f, 3.7f, 0.32f) };
        var sets = 0;
        foreach (var room in layout.Rooms)
        {
            var width = Math.Min(room.W, room.H);
            var length = Math.Max(room.W, room.H);
            // ground level only: a beam in an upper gallery would be a step onto the tops of the walls
            if (width > 3 || length < 6 || room == layout.BossArena || layout.LevelOf(room.Center) != 0) continue;
            var along = room.W >= room.H; // the long axis runs in x
            var span = width * CellSize;
            var beam = new BoxMesh { Size = new Vector3(along ? 0.34f : span, 0.34f, along ? span : 0.34f) };
            for (var k = 1; k < length; k += 3)
            {
                // the cells the set stands in, and the wall cells beyond each post: an opening there means a doorway
                var a = along ? new GridPos(room.X + k, room.Y) : new GridPos(room.X, room.Y + k);
                var b = along ? new GridPos(room.X + k, room.Bottom) : new GridPos(room.Right, room.Y + k);
                var wallA = along ? new GridPos(a.X, a.Y - 1) : new GridPos(a.X - 1, a.Y);
                var wallB = along ? new GridPos(b.X, b.Y + 1) : new GridPos(b.X + 1, b.Y);
                if (layout[a] != Cell.Floor || layout[b] != Cell.Floor || layout.IsWalkable(wallA) || layout.IsWalkable(wallB)) continue;
                if (layout.IsWater(a) || layout.IsWater(b) || layout.IsUnder(a) || layout.LevelOf(a) != layout.LevelOf(b)) continue;
                if (layout.Ramps.ContainsKey(a) || layout.Ramps.ContainsKey(b)) continue;
                var floorA = Surface(a);
                var floorB = Surface(b);
                var inset = CellSize / 2f - 0.2f;
                var pa = floorA + (along ? new Vector3(0, 0, -inset) : new Vector3(-inset, 0, 0));
                var pb = floorB + (along ? new Vector3(0, 0, inset) : new Vector3(inset, 0, 0));
                foreach (var p in new[] { pa, pb })
                {
                    var m = new MeshInstance3D { Mesh = post, MaterialOverride = darkWood };
                    node.AddChild(m);
                    m.GlobalPosition = p + Vector3.Up * 1.85f;
                }
                var cap = new MeshInstance3D { Mesh = beam, MaterialOverride = plank };
                node.AddChild(cap);
                cap.GlobalPosition = (pa + pb) / 2f + Vector3.Up * 3.55f;
                sets++;
            }
        }
        if (sets == 0) node.QueueFree();
    }

    private static Node3D? Decor(Node3D parent, string path, Vector3 position, float size, float yaw, float collisionRadius = 0f)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        parent.AddChild(model);
        var aabb = new Aabb();
        var first = true;
        foreach (var mi in Core.AnimatedModel.Meshes(model)) { var box = mi.GetAabb().Abs(); aabb = first ? box : aabb.Merge(box); first = false; }
        var longest = Math.Max(0.05f, Math.Max(aabb.Size.X, Math.Max(aabb.Size.Y, aabb.Size.Z)));
        var scale = size / longest;
        model.Scale = Vector3.One * scale;
        model.Position = position - parent.GlobalPosition + new Vector3(-aabb.GetCenter().X * scale, -aabb.Position.Y * scale, -aabb.GetCenter().Z * scale);
        model.RotateY(yaw);
        if (collisionRadius > 0f)
        {
            var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = collisionRadius, Height = aabb.Size.Y * scale }, Position = new Vector3(0, aabb.Size.Y * scale / 2f, 0) });
            body.Position = position - parent.GlobalPosition;
            parent.AddChild(body);
        }
        return model;
    }

    /// <summary>The wall itself is grid cubes; this adds the arch, a raised portcullis and four braziers at the opening.</summary>
    private void BuildGate(FloorLayout layout, Node3D node, Structure gate, Material wallMat, Material iron)
    {
        var centre = Surface(gate.Anchor);
        var facing = new Vector3(gate.Facing.X, 0, gate.Facing.Y);  // through the opening
        var side = new Vector3(-facing.Z, 0, facing.X);            // along the wall
        var yaw = Yaw(side);                      // BoxMesh X axis onto the wall line
        const float thickness = 1.2f;
        // jambs narrow the opening to three metres; the lintel closes the top
        Box(node, new Vector3(0.5f, 3.1f, thickness), centre + side * (CellSize / 2f - 0.25f) + Vector3.Up * 1.55f, wallMat, yaw: yaw);
        Box(node, new Vector3(0.5f, 3.1f, thickness), centre - side * (CellSize / 2f - 0.25f) + Vector3.Up * 1.55f, wallMat, yaw: yaw);
        Box(node, new Vector3(CellSize + 0.2f, 0.9f, thickness), centre + Vector3.Up * 3.55f, wallMat, yaw: yaw);
        Box(node, new Vector3(1.0f, 0.5f, thickness + 0.3f), centre + Vector3.Up * 3.35f, wallMat, collide: false, yaw: yaw); // keystone
        // a portcullis, winched up: bars hang from the lintel to head height
        for (var i = -3; i <= 3; i++)
        {
            var bar = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1.5f }, MaterialOverride = iron };
            node.AddChild(bar);
            bar.GlobalPosition = centre + side * (i * 0.42f) + Vector3.Up * 3.25f;
        }
        for (var j = 0; j < 2; j++)
            Box(node, new Vector3(3.0f, 0.08f, 0.08f), centre + Vector3.Up * (2.65f + j * 0.7f), iron, collide: false, yaw: yaw);
        foreach (var f in new[] { 1f, -1f })
            foreach (var sd in new[] { 1f, -1f })
            {
                var brazier = new Brazier { Name = $"Brazier{(f > 0 ? "F" : "B")}{(sd > 0 ? "L" : "R")}" };
                node.AddChild(brazier);
                brazier.GlobalPosition = centre + facing * (f * 2.6f) + side * (sd * 1.35f);
            }
        node.Name = $"Gate_{gate.Anchor.X}_{gate.Anchor.Y}";
    }

    /// <summary>A dilapidated plank hut on the hut's blocked edges, its roof, a campfire, seats, a torch and bedding inside.</summary>
    private void BuildCamp(FloorLayout layout, Node3D node, Structure camp, Random rng, Material plankMat, Material darkWood)
    {
        var door = camp.Openings[0];
        var xs = camp.Cells.Select(c => c.X).ToList();
        var ys = camp.Cells.Select(c => c.Y).ToList();
        var hut = new Room(xs.Min(), ys.Min(), xs.Max() - xs.Min() + 1, ys.Max() - ys.Min() + 1);
        var level = layout.LevelOf(camp.Cells[0]) * LevelHeight + FloorTop;
        // walls: one panel per blocked edge around the hut, leaning and uneven; broken ones are knee-high with stubs
        foreach (var c in camp.Cells)
            foreach (var d in RampDirs)
            {
                var n = c + d;
                if (camp.Cells.Contains(n) || !layout.IsEdgeBlocked(c, n)) continue;
                var mid = (Surface(c) + Surface(n)) / 2f;
                var broken = camp.Broken.Contains(FloorLayout.Edge(c, n));
                var along = d.X != 0 ? Mathf.Pi / 2f : 0f;
                var height = broken ? 1.0f + (float)rng.NextDouble() * 0.4f : 2.5f + (float)rng.NextDouble() * 0.7f;
                var lean = ((float)rng.NextDouble() - 0.5f) * 0.09f;
                Box(node, new Vector3(CellSize - 0.1f, height, 0.16f), mid with { Y = level + height / 2f }, plankMat, yaw: along, roll: lean);
                if (broken)
                    for (var k = 0; k < 2; k++)
                    {
                        var offset = (k == 0 ? -1f : 1f) * (CellSize / 2f - 0.35f);
                        var stub = mid + (d.X != 0 ? new Vector3(0, 0, offset) : new Vector3(offset, 0, 0));
                        Box(node, new Vector3(0.25f, 1.4f + (float)rng.NextDouble() * 0.8f, 0.12f), stub with { Y = level + 0.9f }, plankMat, collide: false, yaw: along, roll: ((float)rng.NextDouble() - 0.5f) * 0.5f);
                    }
            }
        // corner posts and the door lintel
        foreach (var (cx, cy) in new[] { (hut.X, hut.Y), (hut.Right + 1, hut.Y), (hut.X, hut.Bottom + 1), (hut.Right + 1, hut.Bottom + 1) })
        {
            var corner = CellToWorld(new GridPos(cx, cy)) - new Vector3(CellSize / 2f, 0, CellSize / 2f);
            var post = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.13f, Height = 3.3f }, MaterialOverride = darkWood };
            node.AddChild(post);
            post.GlobalPosition = corner with { Y = level + 1.65f };
        }
        var doorMid = (Surface(door) + Surface(camp.Anchor)) / 2f;
        var doorAlong = camp.Facing.X != 0 ? Mathf.Pi / 2f : 0f;
        Box(node, new Vector3(CellSize - 0.1f, 0.25f, 0.18f), doorMid with { Y = level + 2.75f }, plankMat, collide: false, yaw: doorAlong);
        // roof: two slabs, one corner missing, both sagging a little
        var roofCentre = new Vector3((hut.X + hut.W / 2f) * CellSize, level + 3.15f, (hut.Y + hut.H / 2f) * CellSize);
        var w = hut.W * CellSize + 0.5f;
        var h = hut.H * CellSize + 0.5f;
        Box(node, new Vector3(w * 0.62f, 0.14f, h), roofCentre + new Vector3(-w * 0.19f, 0, 0), plankMat, collide: false, roll: 0.03f);
        Box(node, new Vector3(w * 0.38f, 0.14f, h * 0.55f), roofCentre + new Vector3(w * 0.31f, -0.05f, -h * 0.225f), plankMat, collide: false, roll: -0.04f);
        // inside: bedding and stores; outside: the fire, two log seats and a torch by the door
        var interior = camp.Cells.Where(c => c != door).ToList();
        if (interior.Count == 0) interior.Add(door);
        var bedCell = interior[rng.Next(interior.Count)];
        Decor(node, "res://assets/models/kaykit_dungeon/bed_floor.gltf.glb", Surface(bedCell) + new Vector3(1.1f, 0.02f, -1.0f), 2.0f, rng.Next(4) * Mathf.Pi / 2f);
        if (interior.Count > 1)
            Decor(node, "res://assets/models/kaykit_dungeon/bed_floor.gltf.glb", Surface(interior[(interior.IndexOf(bedCell) + 1) % interior.Count]) + new Vector3(-1.1f, 0.02f, 1.0f), 2.0f, rng.Next(4) * Mathf.Pi / 2f);
        Decor(node, "res://assets/models/wooden_crate_01/wooden_crate_01_1k.gltf", Surface(door) + new Vector3(-1.3f, 0, 1.2f), 0.8f, (float)rng.NextDouble(), 0.5f);
        Decor(node, "res://assets/models/wine_barrel_01/wine_barrel_01_1k.gltf", Surface(door) + new Vector3(1.3f, 0, 1.25f), 1.0f, (float)rng.NextDouble(), 0.45f);
        var lantern = Decor(node, "res://assets/models/wooden_lantern_01/wooden_lantern_01_1k.gltf", roofCentre + new Vector3(0, -0.9f, 0), 0.45f, 0f);
        if (lantern != null)
        {
            var glow = new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.55f), LightEnergy = 1.2f, OmniRange = 7f, ShadowEnabled = false };
            node.AddChild(glow);
            glow.GlobalPosition = roofCentre + new Vector3(0, -1.0f, 0);
        }

        var fireAt = Surface(camp.Anchor);
        var pit = Decor(node, "res://assets/models/stone_fire_pit/stone_fire_pit_1k.gltf", fireAt, 1.6f, (float)rng.NextDouble() * Mathf.Tau);
        var fire = Fire.Create(0.45f, 1.1f, 2.6f, 11f);
        fire.Name = "Campfire";
        node.AddChild(fire);
        fire.GlobalPosition = fireAt + Vector3.Up * (pit != null ? 0.32f : 0.1f);
        var ring = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        ring.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.75f, Height = 1.0f }, Position = new Vector3(0, 0.5f, 0) });
        node.AddChild(ring);
        ring.GlobalPosition = fireAt;
        _propCells.Add(camp.Anchor);
        var facing = new Vector3(camp.Facing.X, 0, camp.Facing.Y);
        var sideways = new Vector3(-facing.Z, 0, facing.X);
        foreach (var sd in new[] { 1f, -1f })
        {
            var log = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.22f, Height = 1.3f }, MaterialOverride = darkWood };
            node.AddChild(log);
            log.GlobalPosition = fireAt + sideways * (sd * 1.35f) + Vector3.Up * 0.22f;
            log.RotateX(Mathf.Pi / 2f);
            log.RotateY(Mathf.Atan2(facing.X, facing.Z));
        }
        var torchAt = Surface(door) + sideways * 1.6f + facing * 1.4f;
        var pole = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 1.7f }, MaterialOverride = darkWood };
        node.AddChild(pole);
        pole.GlobalPosition = torchAt + Vector3.Up * 0.85f;
        var torch = Fire.Create(0.14f, 0.5f, 1.3f, 6f);
        torch.Name = "Torch";
        node.AddChild(torch);
        torch.GlobalPosition = torchAt + Vector3.Up * 1.75f;
        node.Name = $"Camp_{camp.Anchor.X}_{camp.Anchor.Y}";
    }

    private void PlaceKey(Node3D root, ItemCatalog cat, GridPos cell, int keyIndex)
    {
        if (!cat.TryBase("item.iron_key", out _)) return;
        var drop = _lootScene.Instantiate<LootDrop>();
        drop.Item = cat.Create("item.iron_key", 1, Rng.StableGuid(Rng.Mix((ulong)Rng.FloorSeed(GameSeed, CurrentFloor)) ^ (ulong)(keyIndex + 1) * 0x9E37UL));
        drop.KeyIndex = keyIndex;
        drop.Name = $"Key{keyIndex}";
        root.AddChild(drop);
        drop.GlobalPosition = Surface(cell) + Vector3.Up * 0.3f;
    }
}
