namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Pure-data result of <see cref="FloorGenerator"/>. The Godot-side DungeonBuilder turns this into a GridMap and
/// instanced scenes. Never saved: it is regenerated from the seed, so it must stay deterministic.
/// </summary>
public sealed class FloorLayout
{
    public int Seed { get; }
    /// <summary>The shape this floor was generated as.</summary>
    public LayoutKind Kind { get; init; }
    public int Width { get; }
    public int Height { get; }
    public Cell[] Cells { get; }               // row-major, index = y * Width + x
    /// <summary>Height level per cell: 0 low, 1 high (4 m up). Meaningful for walkable cells; ramps store their base level.</summary>
    public byte[] Levels { get; }
    /// <summary>Under-layer floor (cellars and their tunnels) at height 0 beneath the main grid.</summary>
    public bool[] Under { get; }
    public Dictionary<GridPos, RampInfo> Ramps { get; } = new();
    public List<Ledge> Ledges { get; } = new();
    public List<Cellar> Cellars { get; } = new();
    public List<SecretAlcove> Secrets { get; } = new();
    /// <summary>Ranged enemies stand here (ledge cells, level 1).</summary>
    public List<GridPos> LedgeSpawns { get; } = new();
    /// <summary>Ambushers in cellars (under layer).</summary>
    public List<GridPos> CellarSpawns { get; } = new();
    public int TotalEnemySpawns => EnemySpawns.Count + LedgeSpawns.Count + CellarSpawns.Count;
    public List<Room> Rooms { get; } = new();
    public GridPos UpStairs { get; internal set; }
    public GridPos DownStairs { get; internal set; }
    public Room? BossArena { get; internal set; }
    public List<GridPos> EnemySpawns { get; } = new();
    public List<GridPos> ChestSpawns { get; } = new();
    public List<GridPos> BreakableSpawns { get; } = new();
    public List<GridPos> TrapSpawns { get; } = new();
    public List<Vault> Vaults { get; } = new();
    /// <summary>Keys for locked chests, parallel to LockedChests.</summary>
    public List<GridPos> KeySpawns { get; } = new();
    /// <summary>Indices into ChestSpawns that start locked.</summary>
    public List<int> LockedChests { get; } = new();
    /// <summary>Room index pairs joined by corridors.</summary>
    public List<(int A, int B)> Connections { get; } = new();
    /// <summary>Gatehouses and bandit camps built inside big rooms.</summary>
    public List<Structure> Structures { get; } = new();
    /// <summary>Ponds and streams; their cells stay walkable floor (see <see cref="WaterAt"/>).</summary>
    public List<WaterFeature> Water { get; } = new();
    private readonly Dictionary<GridPos, WaterKind> _water = new();
    public WaterKind WaterAt(GridPos p) => _water.TryGetValue(p, out var k) ? k : WaterKind.None;
    public bool IsWater(GridPos p) => _water.ContainsKey(p);
    public void SetWater(GridPos p, WaterKind kind) { if (kind == WaterKind.None) _water.Remove(p); else _water[p] = kind; }
    /// <summary>Cell pairs nothing can step between (hut walls). Stored normalised, see <see cref="Edge"/>.</summary>
    public HashSet<(GridPos, GridPos)> BlockedEdges { get; } = new();

    public static (GridPos, GridPos) Edge(GridPos a, GridPos b) => a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a);
    public bool IsEdgeBlocked(GridPos a, GridPos b) => BlockedEdges.Count > 0 && BlockedEdges.Contains(Edge(a, b));
    public Structure? StructureAt(Room room) => Structures.FirstOrDefault(s => s.Room == room);
    /// <summary>Rubble mounds: floor cells heaped with fallen stone that bodies path around.</summary>
    public HashSet<GridPos> Rubble { get; } = new();
    /// <summary>Rooms with breaches in their walls for hordes.</summary>
    public List<Nest> Nests { get; } = new();
    public bool IsNestMouth(GridPos p) => Nests.Any(n => n.Mouths.Contains(p));
    public Nest? NestAt(Room room) => Nests.FirstOrDefault(n => n.Room == room);
    /// <summary>Floor cells on fire (Burning floors): walkable, but it hurts, and bodies path around them.</summary>
    public HashSet<GridPos> Fires { get; } = new();
    /// <summary>Cells nothing may be placed on: a campfire, a gateway, a ruin's columns, a rubble mound, a nest mouth, a fire.</summary>
    public bool IsReserved(GridPos p) => Rubble.Contains(p) || Fires.Contains(p) || IsNestMouth(p) || Structures.Any(s => s.Anchor == p || s.Openings.Contains(p) || (s.Kind == StructureKind.Ruin && s.Cells.Contains(p)));

    /// <summary>
    /// A wall cell the builder may draw as a 45° piece: it touches plain floor on two perpendicular sides at ground level with
    /// floor between them, its other sides are solid, and nothing high or underground is near. <paramref name="open"/> is the
    /// diagonal the cut faces.
    /// </summary>
    public bool IsChamfer(GridPos p, out GridPos open)
    {
        open = default;
        if (this[p] != Cell.Wall || IsUnder(p)) return false;
        bool Plain(GridPos q) => this[q] == Cell.Floor && LevelOf(q) == 0 && !IsUnder(q) && !IsNestMouth(q); // a breach keeps square jambs
        var e = Plain(new GridPos(p.X + 1, p.Y));
        var w = Plain(new GridPos(p.X - 1, p.Y));
        var s = Plain(new GridPos(p.X, p.Y + 1));
        var n = Plain(new GridPos(p.X, p.Y - 1));
        if (e == w || s == n) return false;
        var dx = e ? 1 : -1;
        var dy = s ? 1 : -1;
        if (!Plain(new GridPos(p.X + dx, p.Y + dy))) return false;
        if (this[p.X - dx, p.Y] is not (Cell.Wall or Cell.Void) || this[p.X, p.Y - dy] is not (Cell.Wall or Cell.Void)) return false;
        for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var q = new GridPos(p.X + ox, p.Y + oy);
                if (IsUnder(q) || this[q] is Cell.Ramp or Cell.Pit or Cell.Door) return false;
                if (this[q] == Cell.Floor && LevelOf(q) != 0) return false;
                if (Ledges.Any(l => l.Cells.Contains(q))) return false;
            }
        open = new GridPos(dx, dy);
        return true;
    }

    public bool IsInVault(GridPos p) => Vaults.Any(v => v.Room.Contains(p));

    public FloorLayout(int seed, int width, int height)
    {
        Seed = seed;
        Width = width;
        Height = height;
        Cells = new Cell[width * height];
        Levels = new byte[width * height];
        Under = new bool[width * height];
    }

    public int LevelOf(GridPos p) => InBounds(p.X, p.Y) ? Levels[p.Y * Width + p.X] : 0;
    public void SetLevel(GridPos p, int level) { if (InBounds(p.X, p.Y)) Levels[p.Y * Width + p.X] = (byte)level; }
    public bool IsUnder(GridPos p) => InBounds(p.X, p.Y) && Under[p.Y * Width + p.X];
    public void SetUnder(GridPos p, bool value) { if (InBounds(p.X, p.Y)) Under[p.Y * Width + p.X] = value; }

    /// <summary>Walking-surface height in metres for a main-layer cell (ramps report their midpoint).</summary>
    public float HeightOf(GridPos p) => Ramps.TryGetValue(p, out var r) ? r.MidHeight : LevelOf(p) * 4f;

    /// <summary>Can something walking on the main layer step from a to b? Height difference at most a half ramp; ramps only along their axis.</summary>
    public bool CanStep(GridPos a, GridPos b, bool secretsOpen = false)
    {
        if (!IsWalkable(a, secretsOpen) || !IsWalkable(b, secretsOpen)) return false;
        var d = b - a;
        if (Math.Abs(d.X) + Math.Abs(d.Y) != 1) return false;
        if (IsEdgeBlocked(a, b)) return false;
        if (Ramps.TryGetValue(a, out var ra) && !d.IsParallelTo(ra.Dir)) return false;
        if (Ramps.TryGetValue(b, out var rb) && !d.IsParallelTo(rb.Dir)) return false;
        return Math.Abs(HeightOf(a) - HeightOf(b)) <= 2.01f;
    }

    public bool IsWalkable(GridPos p, bool secretsOpen) => IsWalkable(p) || (secretsOpen && this[p] == Cell.Secret);

    public Cell this[int x, int y]
    {
        get => InBounds(x, y) ? Cells[y * Width + x] : Cell.Void;
        set { if (InBounds(x, y)) Cells[y * Width + x] = value; }
    }

    public Cell this[GridPos p]
    {
        get => this[p.X, p.Y];
        set => this[p.X, p.Y] = value;
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public bool IsWalkable(GridPos p) => this[p] is Cell.Floor or Cell.Door or Cell.Ramp;

    public Room? RoomAt(GridPos p) => Rooms.FirstOrDefault(r => r.Contains(p));

    /// <summary>Stable content hash for determinism tests and save-file sanity checks.</summary>
    public ulong ContentHash()
    {
        var h = 14695981039346656037UL;
        foreach (var c in Cells) { h ^= (byte)c; h *= 1099511628211UL; }
        h ^= (ulong)UpStairs.GetHashCode(); h *= 1099511628211UL;
        h ^= (ulong)DownStairs.GetHashCode(); h *= 1099511628211UL;
        foreach (var s in EnemySpawns) { h ^= (ulong)s.GetHashCode(); h *= 1099511628211UL; }
        foreach (var w in _water.Keys.OrderBy(k => k.Y).ThenBy(k => k.X)) { h ^= (ulong)w.GetHashCode() * 31; h *= 1099511628211UL; }
        foreach (var r in Rubble.OrderBy(k => k.Y).ThenBy(k => k.X)) { h ^= (ulong)r.GetHashCode() * 17; h *= 1099511628211UL; }
        foreach (var m in Nests.SelectMany(n => n.Mouths)) { h ^= (ulong)m.GetHashCode() * 13; h *= 1099511628211UL; }
        foreach (var f in Fires.OrderBy(k => k.Y).ThenBy(k => k.X)) { h ^= (ulong)f.GetHashCode() * 23; h *= 1099511628211UL; }
        foreach (var (a, b) in BlockedEdges.OrderBy(e => e.Item1.Y).ThenBy(e => e.Item1.X).ThenBy(e => e.Item2.Y).ThenBy(e => e.Item2.X))
        { h ^= (ulong)a.GetHashCode(); h *= 1099511628211UL; h ^= (ulong)b.GetHashCode(); h *= 1099511628211UL; }
        return h;
    }

    /// <summary>Flood fill from <paramref name="start"/> over walkable cells. Used by tests and by spawn validation.</summary>
    public HashSet<GridPos> Reachable(GridPos start) => Reachable(start, null);

    /// <summary>Flood fill on the main layer honouring heights and ramps; cells where <paramref name="blocked"/> is true count as walls (closed doors).</summary>
    public HashSet<GridPos> Reachable(GridPos start, Func<GridPos, bool>? blocked, bool secretsOpen = false)
    {
        var seen = new HashSet<GridPos>();
        if (!IsWalkable(start, secretsOpen)) return seen;
        var stack = new Stack<GridPos>();
        stack.Push(start);
        seen.Add(start);
        Span<GridPos> dirs = stackalloc GridPos[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in dirs)
            {
                var n = p + d;
                if (CanStep(p, n, secretsOpen) && (blocked == null || !blocked(n)) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen;
    }

    public string ToAscii()
    {
        var sb = new System.Text.StringBuilder(Width * (Height + 1));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var p = new GridPos(x, y);
                sb.Append(p == UpStairs ? '<' : p == DownStairs ? '>' :
                          EnemySpawns.Contains(p) ? 'e' : ChestSpawns.Contains(p) ? '$' : BreakableSpawns.Contains(p) ? 'b' :
                          TrapSpawns.Contains(p) ? '^' : KeySpawns.Contains(p) || Vaults.Any(v => v.Key == p) ? 'k' : Vaults.Any(v => v.Lever == p) ? 'L' :
                          IsWater(p) ? '~' : Fires.Contains(p) ? '*' :
                          this[x, y] switch { Cell.Floor => LevelOf(p) == 1 ? ':' : '.', Cell.Wall => IsUnder(p) ? '_' : '#', Cell.Door => '+', Cell.Ramp => '/', Cell.Pit => 'O', Cell.Secret => 'S', _ => IsUnder(p) ? '_' : ' ' });
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
