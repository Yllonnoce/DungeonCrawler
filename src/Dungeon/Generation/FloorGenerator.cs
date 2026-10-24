namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Room-and-corridor generator. Deterministic for a given (spec, seed): only System.Random seeded from the
/// argument is used, and every collection is iterated in insertion order.
/// </summary>
public static partial class FloorGenerator
{
    public static FloorLayout Generate(FloorSpec spec, int seed)
    {
        spec = FloorObjectives.Tune(LayoutKinds.Tune(spec));
        var rng = new Random(seed);
        var layout = new FloorLayout(seed, spec.Width, spec.Height) { Kind = spec.Layout };
        if (spec.Objective != FloorObjectiveKind.None) layout.Objective = new ObjectiveLayout { Kind = spec.Objective };

        // the shape: how rooms are laid and joined; everything after works the same on any of them (see FloorGenerator.Kinds.cs)
        switch (spec.Layout)
        {
            case LayoutKind.Labyrinth:
                PlaceLatticeRooms(spec, rng, layout);
                CarveRooms(layout);
                CarveMaze(spec, rng, layout);
                DeriveConnections(layout);
                break;
            case LayoutKind.Ring:
                PlaceRingRooms(spec, rng, layout);
                CarveRooms(layout);
                ConnectRing(spec, rng, layout);
                break;
            case LayoutKind.Mine:
                PlaceGalleries(spec, rng, layout);
                CarveRooms(layout);
                ConnectRooms(spec, rng, layout);
                DigDrifts(spec, new Random(seed ^ 0x0D1F), layout);
                break;
            default:
                PlaceRooms(spec, rng, layout);
                CarveRooms(layout);
                ConnectRooms(spec, rng, layout);
                break;
        }
        RaiseWalls(layout);
        PlaceStairs(layout);
        PlaceVaults(spec, rng, layout);
        Verticality.AssignLevels(spec, rng, layout);
        PlaceObjective(spec, new Random(seed ^ 0x0B1E), layout); // rooms and doors the objective needs; a no-op without one
        Verticality.PlacePits(spec, rng, layout);
        Verticality.PlaceSecrets(spec, rng, layout);
        PlaceStructures(spec, new Random(seed ^ 0x57C7), layout);
        if (spec.Layout == LayoutKind.Flooded) FloodRooms(spec, new Random(seed ^ 0x7A7E), layout);
        else PlaceWater(spec, new Random(seed ^ 0x7A7E), layout);
        PlaceRubble(spec, new Random(seed ^ 0x2B1E), layout);
        PlaceNests(spec, new Random(seed ^ 0x4E57), layout);
        PlaceObjectiveMid(spec, new Random(seed ^ 0x0B1D), layout); // the shrine is confirmed once the breaches exist; a no-op without one
        PlaceFires(spec, new Random(seed ^ 0xF1AE), layout);
        RaiseWalls(layout);
        PlaceSpawns(spec, rng, layout);
        PlaceTraps(spec, rng, layout);
        HazardPlanner.PlaceHazards(spec, new Random(seed ^ 0x4A2D), layout); // the lethal hazards, last and from their own seed: a floor without them is untouched
        PlaceObjectiveLate(spec, new Random(seed ^ 0x0B1F), layout); // keys and props that go among the spawns; a no-op without one

        return layout;
    }

    // ---- rooms -------------------------------------------------------------------------------

    private static void PlaceRooms(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.BossFloor)
        {
            // Arena first so it always fits; it is deliberately far from the map centre to give the floor some length.
            var size = Math.Min(spec.BossArenaSize, Math.Min(spec.Width, spec.Height) - 4);
            var arena = TryPlace(spec, rng, layout, size, size, attempts: 200)
                        ?? new Room(spec.Width - size - 2, spec.Height - size - 2, size, size);
            layout.Rooms.Add(arena);
            layout.BossArena = arena;
        }

        var target = rng.Next(spec.MinRooms, spec.MaxRooms + 1);
        var attempts = 0;
        while (layout.Rooms.Count < target && attempts < 2000)
        {
            attempts++;
            var w = rng.Next(spec.MinRoomSize, spec.MaxRoomSize + 1);
            var h = rng.Next(spec.MinRoomSize, spec.MaxRoomSize + 1);
            var room = TryPlace(spec, rng, layout, w, h, attempts: 1);
            if (room != null) layout.Rooms.Add(room);
        }
    }

    internal static Room? TryPlace(FloorSpec spec, Random rng, FloorLayout layout, int w, int h, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            // keep a 1-cell border for walls
            var x = rng.Next(1, spec.Width - w - 1);
            var y = rng.Next(1, spec.Height - h - 1);
            var candidate = new Room(x, y, w, h);
            var clash = false;
            foreach (var r in layout.Rooms)
                if (r.Intersects(candidate, spec.RoomMargin)) { clash = true; break; }
            if (!clash) return candidate;
        }
        return null;
    }

    private static void CarveRooms(FloorLayout layout)
    {
        foreach (var room in layout.Rooms)
            foreach (var p in room.Cells())
                if (!IsChamfered(room, p)) layout[p] = Cell.Floor;
    }

    /// <summary>How many cells each corner of a room loses: big rooms read as octagons instead of boxes.</summary>
    public static int ChamferDepth(Room room) => Math.Min(room.W, room.H) >= 9 ? 2 : Math.Min(room.W, room.H) >= 6 ? 1 : 0;

    /// <summary>A corner cell left as wall; the builder draws it with a 45° piece.</summary>
    public static bool IsChamfered(Room room, GridPos p)
    {
        var depth = ChamferDepth(room);
        if (depth == 0) return false;
        var dx = Math.Min(p.X - room.X, room.Right - p.X);
        var dy = Math.Min(p.Y - room.Y, room.Bottom - p.Y);
        return dx + dy < depth;
    }

    // ---- corridors ---------------------------------------------------------------------------

    private static void ConnectRooms(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var rooms = layout.Rooms;
        var n = rooms.Count;
        if (n < 2) return;

        // Prim's MST on room centres (squared Euclidean).
        var inTree = new bool[n];
        var edges = new List<(int a, int b)>();
        inTree[0] = true;
        for (var added = 1; added < n; added++)
        {
            var best = (a: -1, b: -1, d: long.MaxValue);
            for (var a = 0; a < n; a++)
            {
                if (!inTree[a]) continue;
                for (var b = 0; b < n; b++)
                {
                    if (inTree[b]) continue;
                    var d = Dist2(rooms[a].Center, rooms[b].Center);
                    if (d < best.d) best = (a, b, d);
                }
            }
            inTree[best.b] = true;
            edges.Add((best.a, best.b));
        }

        // Extra loop edges between rooms not already joined.
        var joined = new HashSet<(int, int)>(edges.Select(Norm));
        for (var a = 0; a < n; a++)
            for (var b = a + 1; b < n; b++)
                if (!joined.Contains((a, b)) && rng.NextDouble() < spec.ExtraEdgeChance)
                    edges.Add((a, b));

        // corridors walk with their own stream so the room, vault and spawn decisions above and below stay put
        var walk = new Random(layout.Seed ^ 0x0C0D);
        foreach (var (a, b) in edges)
        {
            layout.Connections.Add((a, b));
            CarveCorridor(walk, layout, rooms[a].Center, rooms[b].Center);
        }
    }

    private static (int, int) Norm((int a, int b) e) => e.a < e.b ? (e.a, e.b) : (e.b, e.a);
    internal static long Dist2(GridPos a, GridPos b) => (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>
    /// A corridor with character: the usual L between the rooms, but every long leg is staggered sideways by one cell
    /// for a stretch in its middle, so hallways jog and weave instead of running dead straight. Each jog adds only two
    /// cells and leaves straight runs at both ends, which is what ramps need.
    /// </summary>
    internal static void CarveCorridor(Random rng, FloorLayout layout, GridPos from, GridPos to)
    {
        var corner = rng.Next(2) == 0 ? new GridPos(to.X, from.Y) : new GridPos(from.X, to.Y);
        CarveLeg(rng, layout, from, corner);
        CarveLeg(rng, layout, corner, to);
    }

    private static void CarveLeg(Random rng, FloorLayout layout, GridPos a, GridPos b)
    {
        var length = a.ManhattanDistance(b);
        var dir = new GridPos(Math.Sign(b.X - a.X), Math.Sign(b.Y - a.Y));
        // the stretch of the leg that runs outside rooms: only there does a jog show, and it must leave three straight
        // cells at either end so a ramp can still sit by the room's door
        int first = -1, last = -1;
        for (var i = 0; i <= length; i++)
        {
            var c = new GridPos(a.X + dir.X * i, a.Y + dir.Y * i);
            if (layout.RoomAt(c) != null) continue;
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0 || last - first < 9 || rng.NextDouble() < 0.2) { CarveLine(layout, a, b); return; }
        var side = dir.Perp;
        if (rng.Next(2) == 0) side = -side;
        var start = rng.Next(first + 3, last - 5);
        var end = Math.Min(last - 3, start + rng.Next(3, 7));
        var s0 = new GridPos(a.X + dir.X * start, a.Y + dir.Y * start);
        var s1 = new GridPos(a.X + dir.X * end, a.Y + dir.Y * end);
        var o0 = s0 + side;
        var o1 = s1 + side;
        // the staggered stretch must not run into or alongside anything already carved
        for (var i = start; i <= end; i++)
        {
            var o = new GridPos(a.X + dir.X * i, a.Y + dir.Y * i) + side;
            var beyond = o + side;
            if (o.X < 1 || o.Y < 1 || o.X >= layout.Width - 1 || o.Y >= layout.Height - 1 || layout.IsWalkable(o) || layout.IsWalkable(beyond)) { CarveLine(layout, a, b); return; }
        }
        CarveLine(layout, a, s0);
        CarveLine(layout, s0, o0);
        CarveLine(layout, o0, o1);
        CarveLine(layout, o1, s1);
        CarveLine(layout, s1, b);
    }

    internal static void CarveLine(FloorLayout layout, GridPos a, GridPos b)
    {
        var dx = Math.Sign(b.X - a.X);
        var dy = Math.Sign(b.Y - a.Y);
        var p = a;
        layout[p] = Cell.Floor;
        while (p != b)
        {
            p = new GridPos(p.X + dx, p.Y + dy);
            layout[p] = Cell.Floor;
        }
    }

    // ---- walls -------------------------------------------------------------------------------

    internal static void RaiseWalls(FloorLayout layout)
    {
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                if (layout[x, y] != Cell.Void) continue;
                for (var oy = -1; oy <= 1 && layout[x, y] == Cell.Void; oy++)
                    for (var ox = -1; ox <= 1; ox++)
                        if ((ox != 0 || oy != 0) && layout[x + ox, y + oy] == Cell.Floor)
                        {
                            layout[x, y] = Cell.Wall;
                            break;
                        }
            }
    }

    // ---- stairs and spawns -------------------------------------------------------------------

    private static void PlaceStairs(FloorLayout layout)
    {
        var rooms = layout.Rooms;
        if (rooms.Count == 0) return;

        var centre = new GridPos(layout.Width / 2, layout.Height / 2);
        var candidates = layout.BossArena is { } arena ? rooms.Where(r => r != arena).ToList() : rooms;
        if (layout.Kind == LayoutKind.Mine)
        {
            // stairs belong in a chamber, not jammed against a gallery wall (a flight in a two-wide gallery half blocks it)
            var chambers = candidates.Where(r => Math.Min(r.W, r.H) >= 4).ToList();
            if (chambers.Count >= 2) candidates = chambers;
        }
        if (candidates.Count == 0) candidates = rooms;

        var upRoom = candidates.OrderByDescending(r => Dist2(r.Center, centre)).ThenBy(rooms.IndexOf).First();
        layout.UpStairs = upRoom.Center;

        var downRoom = layout.BossArena
                       ?? rooms.Where(r => r != upRoom).OrderByDescending(r => Dist2(r.Center, upRoom.Center)).ThenBy(rooms.IndexOf).FirstOrDefault()
                       ?? upRoom;
        var down = downRoom.Center;
        if (down == layout.UpStairs) down = new GridPos(down.X + 1, down.Y); // degenerate single-room floor
        layout.DownStairs = down;
    }

    private static void PlaceSpawns(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var upRoom = layout.RoomAt(layout.UpStairs);
        var pool = new List<GridPos>();
        foreach (var room in layout.Rooms)
        {
            if (room == upRoom || layout.Vaults.Any(v => v.Room == room)) continue;
            foreach (var p in room.Cells())
                if (layout[p] == Cell.Floor && layout.LevelOf(p) == layout.LevelOf(room.Center) && p != layout.UpStairs && p != layout.DownStairs
                    && !layout.IsReserved(p)
                    && layout.Vaults.All(v => v.Key != p && v.Lever != p) && layout.Ledges.All(l => !l.Cells.Contains(p) && l.RampBottom != p && l.RampTop != p)) pool.Add(p);
        }
        Shuffle(rng, pool);

        var take = 0;
        for (var i = 0; i < spec.EnemyCount && take < pool.Count; i++) layout.EnemySpawns.Add(pool[take++]);
        // things that sit on the floor stay out of the water; enemies may stand in it
        var dry = pool.Skip(take).Where(p => !layout.IsWater(p)).ToList();
        take = 0;
        var firstOrdinaryChest = layout.ChestSpawns.Count;
        for (var i = 0; i < spec.ChestCount && take < dry.Count; i++) layout.ChestSpawns.Add(dry[take++]);
        for (var i = 0; i < spec.BreakableCount && take < dry.Count; i++) layout.BreakableSpawns.Add(dry[take++]);
        for (var i = 0; i < spec.LockedChestCount && firstOrdinaryChest + i < layout.ChestSpawns.Count && take < dry.Count; i++)
        {
            layout.LockedChests.Add(firstOrdinaryChest + i);
            layout.KeySpawns.Add(dry[take++]);
        }
    }

    // ---- vaults and traps --------------------------------------------------------------------

    private static void PlaceVaults(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.VaultCount <= 0 || layout.Rooms.Count < 4) return;
        var rooms = layout.Rooms;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);

        // A vault is any room with a single entrance whose sealing leaves the rest of the floor connected.
        var candidates = new List<(Room room, GridPos entrance)>();
        foreach (var room in rooms)
        {
            if (room == upRoom || room == downRoom || room == layout.BossArena) continue;
            var entrances = Entrances(layout, room);
            if (entrances.Count != 1) continue;
            var door = entrances[0];
            var reach = layout.Reachable(layout.UpStairs, p => p == door);
            if (!reach.Contains(layout.DownStairs)) continue;
            if (rooms.Any(r => r != room && !reach.Contains(r.Center))) continue; // another room would be cut off too
            candidates.Add((room, door));
        }
        Shuffle(rng, candidates);

        // Not enough natural dead ends: carve small side rooms off existing rooms.
        for (var attempt = 0; attempt < 40 && candidates.Count < spec.VaultCount; attempt++)
            if (TryCarveSideRoom(rng, layout) is { } side) candidates.Add(side);

        var made = 0;
        foreach (var (room, door) in candidates)
        {
            if (made >= spec.VaultCount) break;
            if (layout.Vaults.Any(v => v.Room == room)) continue;
            var kind = made % 2 == 0 ? VaultKind.KeyDoor : VaultKind.LeverGate;
            var elsewhere = rooms.Where(r => r != room && r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && candidates.All(c => c.room != r || c.room == room)).ToList();
            if (elsewhere.Count == 0) elsewhere = rooms.Where(r => r != room).ToList();
            var host = elsewhere[rng.Next(elsewhere.Count)];
            var spot = RandomCell(rng, host, layout);
            var vault = new Vault(made, room, door, kind, kind == VaultKind.LeverGate ? spot : null, kind == VaultKind.KeyDoor ? spot : null);
            layout[door] = Cell.Door;
            layout.Vaults.Add(vault);
            layout.ChestSpawns.Add(room.Center); // the prize
            made++;
        }
    }

    /// <summary>
    /// Add a 3-5 cell room a short straight corridor away from a host room, so it has exactly one entrance.
    /// Returns the room and its door cell, or null if nothing fit.
    /// </summary>
    private static (Room room, GridPos entrance)? TryCarveSideRoom(Random rng, FloorLayout layout)
    {
        var rooms = layout.Rooms;
        var host = rooms[rng.Next(rooms.Count)];
        if (host == layout.BossArena) return null;
        var dir = rng.Next(4); // 0 right, 1 left, 2 down, 3 up
        var w = rng.Next(3, 6);
        var h = rng.Next(3, 6);
        var gap = rng.Next(2, 5);
        int x, y;
        if (dir <= 1)
        {
            y = Math.Clamp(host.Y + rng.Next(host.H) - rng.Next(h), 1, layout.Height - h - 2);
            x = dir == 0 ? host.Right + 1 + gap : host.X - gap - w;
        }
        else
        {
            x = Math.Clamp(host.X + rng.Next(host.W) - rng.Next(w), 1, layout.Width - w - 2);
            y = dir == 2 ? host.Bottom + 1 + gap : host.Y - gap - h;
        }
        var room = new Room(x, y, w, h);
        if (x < 1 || y < 1 || room.Right >= layout.Width - 1 || room.Bottom >= layout.Height - 1) return null;
        foreach (var r in rooms) if (r.Intersects(room, 1)) return null;
        // the corridor: a straight line from the host edge to the vault edge, along the shared span
        GridPos start, end;
        var depth = ChamferDepth(host); // the corridor must meet the host on a floor cell, not a chamfered corner
        if (dir <= 1)
        {
            var lo = Math.Max(host.Y + depth, room.Y); var hi = Math.Min(host.Bottom - depth, room.Bottom);
            if (lo > hi) return null;
            var cy = lo + rng.Next(hi - lo + 1);
            start = new GridPos(dir == 0 ? host.Right + 1 : host.X - 1, cy);
            end = new GridPos(dir == 0 ? room.X - 1 : room.Right + 1, cy);
        }
        else
        {
            var lo = Math.Max(host.X + depth, room.X); var hi = Math.Min(host.Right - depth, room.Right);
            if (lo > hi) return null;
            var cx = lo + rng.Next(hi - lo + 1);
            start = new GridPos(cx, dir == 2 ? host.Bottom + 1 : host.Y - 1);
            end = new GridPos(cx, dir == 2 ? room.Y - 1 : room.Bottom + 1);
        }
        // every cell the corridor and room will use must currently be void or wall (not another corridor)
        var cells = new List<GridPos>(room.Cells());
        var p = start;
        var step = new GridPos(Math.Sign(end.X - start.X), Math.Sign(end.Y - start.Y));
        while (true) { cells.Add(p); if (p == end) break; p += step; }
        foreach (var c in cells) if (layout[c] == Cell.Floor || layout[c] == Cell.Door) return null;
        // the room must not touch any existing floor cell (would create extra entrances)
        foreach (var c in room.Cells())
            for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++)
            {
                var n = new GridPos(c.X + ox, c.Y + oy);
                if (!room.Contains(n) && n != end && layout.IsWalkable(n)) return null;
            }
        foreach (var c in cells) layout[c] = Cell.Floor;
        rooms.Add(room);
        layout.Connections.Add((rooms.IndexOf(host), rooms.Count - 1));
        RaiseWalls(layout);
        return (room, end);
    }

    /// <summary>Floor cells outside the room that touch it orthogonally: where corridors come in.</summary>
    internal static List<GridPos> Entrances(FloorLayout layout, Room room)
    {
        var result = new List<GridPos>();
        for (var x = room.X; x <= room.Right; x++)
        {
            Check(new GridPos(x, room.Y - 1)); Check(new GridPos(x, room.Bottom + 1));
        }
        for (var y = room.Y; y <= room.Bottom; y++)
        {
            Check(new GridPos(room.X - 1, y)); Check(new GridPos(room.Right + 1, y));
        }
        return result;

        void Check(GridPos p) { if (layout.IsWalkable(p) && !room.Contains(p)) result.Add(p); }
    }

    // ---- structures --------------------------------------------------------------------------

    /// <summary>Gatehouses and bandit camps in big rooms that carry nothing else special.</summary>
    private static void PlaceStructures(FloorSpec spec, Random rng, FloorLayout layout)
    {
        // the Colonnade: the last arena is fought among broken pillars; it does not use up a room's structure
        var colonnade = spec.Floor >= 20 && spec.Floor % 5 == 0 && layout.Kind == LayoutKind.Ring && layout.BossArena is { } arena
            && layout.Ledges.All(l => l.Room != arena) && TryRuin(rng, layout, arena) ? 1 : 0;
        if (spec.StructureCount <= 0) return;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var candidates = layout.Rooms.Where(r => r != upRoom && r != downRoom && r != layout.BossArena && r != layout.Objective?.ShrineRoom && r != layout.Objective?.JailRoom
            && Math.Min(r.W, r.H) >= 6
            && layout.Vaults.All(v => v.Room != r) && layout.Ledges.All(l => l.Room != r) && layout.Cellars.All(c => c.Room != r)).ToList();
        Shuffle(rng, candidates);
        // each floor deals its kinds in a fresh order, so no kind hogs the big rooms
        var kinds = new List<StructureKind> { StructureKind.Gate, StructureKind.Camp, StructureKind.Ruin };
        Shuffle(rng, kinds);
        var next = 0;
        foreach (var room in candidates)
        {
            if (layout.Structures.Count >= spec.StructureCount + colonnade) break;
            for (var k = 0; k < kinds.Count; k++)
            {
                var kind = kinds[(next + k) % kinds.Count];
                var placed = kind switch
                {
                    StructureKind.Gate => Math.Max(room.W, room.H) >= 7 && TryGate(rng, layout, room),
                    StructureKind.Camp => Math.Max(room.W, room.H) >= 7 && TryCamp(rng, layout, room),
                    _ => TryRuin(rng, layout, room),
                };
                if (placed) { next = (next + k + 1) % kinds.Count; break; }
            }
        }
    }

    /// <summary>A cell a structure must leave alone.</summary>
    internal static bool Busy(FloorLayout layout, GridPos p)
        => layout[p] != Cell.Floor || layout.IsUnder(p) || p == layout.UpStairs || p == layout.DownStairs
           || layout.Vaults.Any(v => v.Key == p || v.Lever == p || v.Door == p)
           || layout.Secrets.Any(s => s.Facing == p || s.Alcove == p) || layout.ChestSpawns.Contains(p);

    /// <summary>A wall across the room's long axis with an opening in the middle; corridors that meet the wall keep their cell open.</summary>
    private static bool TryGate(Random rng, FloorLayout layout, Room room)
    {
        var vertical = room.W >= room.H; // the wall runs top to bottom across a wide room
        var line = vertical ? room.X + room.W / 2 : room.Y + room.H / 2;
        var cells = (vertical ? Enumerable.Range(room.Y, room.H).Select(y => new GridPos(line, y)) : Enumerable.Range(room.X, room.W).Select(x => new GridPos(x, line))).ToList();
        if (cells.Any(c => Busy(layout, c))) return false;
        var facing = vertical ? new GridPos(1, 0) : new GridPos(0, 1);
        var side = facing.Perp;
        var opening = vertical ? new GridPos(line, room.Y + room.H / 2) : new GridPos(room.X + room.W / 2, line);
        var openings = new List<GridPos> { opening };
        foreach (var c in cells)
        {
            if (c == opening) continue;
            var before = c - side;
            var after = c + side;
            if ((!room.Contains(before) && layout.IsWalkable(before)) || (!room.Contains(after) && layout.IsWalkable(after))) openings.Add(c);
        }
        var walls = cells.Where(c => !openings.Contains(c)).ToList();
        if (walls.Count < 3) return false;
        foreach (var c in walls) layout[c] = Cell.Wall;
        layout.Structures.Add(new Structure(StructureKind.Gate, room, walls, opening, facing, openings, new List<(GridPos, GridPos)>()));
        return true;
    }

    /// <summary>A two-by-two (or three-by-two) hut a cell in from every wall, with a doorway and a campfire in front of it.</summary>
    private static bool TryCamp(Random rng, FloorLayout layout, Room room)
    {
        var hw = room.W >= 8 && rng.Next(2) == 0 ? 3 : 2;
        var hh = hw == 2 && room.H >= 8 && rng.Next(2) == 0 ? 3 : 2;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var x0 = room.X + 1 + rng.Next(room.W - hw - 1);
            var y0 = room.Y + 1 + rng.Next(room.H - hh - 1);
            var hut = new Room(x0, y0, hw, hh);
            var interior = hut.Cells().ToList();
            if (interior.Any(c => Busy(layout, c))) continue;
            // doorway candidates: an interior cell on one side, the fire one cell out, open floor one cell beyond
            var doors = new List<(GridPos door, GridPos dir)>();
            foreach (var c in interior)
                foreach (var d in Dirs4)
                {
                    var pit = c + d;
                    var beyond = pit + d;
                    if (hut.Contains(pit) || !room.Contains(pit) || !room.Contains(beyond)) continue;
                    if (Busy(layout, pit) || Busy(layout, beyond) || layout.Structures.Any(s => s.Anchor == pit)) continue;
                    doors.Add((c, d));
                }
            if (doors.Count == 0) continue;
            var (door, dir) = doors[rng.Next(doors.Count)];
            var fire = door + dir;
            var edges = new List<(GridPos, GridPos)>();
            foreach (var c in interior)
                foreach (var d in Dirs4)
                {
                    var n = c + d;
                    if (hut.Contains(n) || (c == door && d == dir)) continue;
                    edges.Add(FloorLayout.Edge(c, n));
                }
            foreach (var e in edges) layout.BlockedEdges.Add(e);
            var broken = new List<(GridPos, GridPos)>();
            var breaks = rng.Next(1, 3);
            for (var i = 0; i < breaks && edges.Count > 0; i++) { var e = edges[rng.Next(edges.Count)]; if (!broken.Contains(e)) broken.Add(e); }
            layout.Structures.Add(new Structure(StructureKind.Camp, room, interior, fire, dir, new List<GridPos> { door }, broken));
            return true;
        }
        return false;
    }

    /// <summary>A colonnade along the room's two long sides, one column every other cell, a cell in from the wall.</summary>
    private static bool TryRuin(Random rng, FloorLayout layout, Room room)
    {
        // ground level only: in an upper room a column stub would be a step toward the tops of the walls
        if (layout.LevelOf(room.Center) != 0) return false;
        var wide = room.W >= room.H;
        var columns = new List<GridPos>();
        if (wide)
            foreach (var y in new[] { room.Y + 1, room.Bottom - 1 })
                for (var x = room.X + 1 + rng.Next(2); x <= room.Right - 1; x += 2) columns.Add(new GridPos(x, y));
        else
            foreach (var x in new[] { room.X + 1, room.Right - 1 })
                for (var y = room.Y + 1 + rng.Next(2); y <= room.Bottom - 1; y += 2) columns.Add(new GridPos(x, y));
        columns.RemoveAll(c => Busy(layout, c) || layout.IsWater(c) || c == room.Center);
        if (columns.Count < 4) return false;
        layout.Structures.Add(new Structure(StructureKind.Ruin, room, columns, room.Center, wide ? new GridPos(1, 0) : new GridPos(0, 1), new List<GridPos>(), new List<(GridPos, GridPos)>()));
        return true;
    }

    // ---- rubble ------------------------------------------------------------------------------

    /// <summary>
    /// Mounds of fallen stone on room floors: never on a cell that touches a doorway (bodies path around mounds, and a
    /// mound in a doorway would seal it), never on anything already placed. Ruined rooms get extra.
    /// </summary>
    private static void PlaceRubble(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.RubbleCount <= 0) return;
        var upRoom = layout.RoomAt(layout.UpStairs);
        bool Ok(Room room, GridPos p)
        {
            if (Busy(layout, p) || layout.IsReserved(p) || layout.IsWater(p) || layout.IsUnder(p) || layout.LevelOf(p) != layout.LevelOf(room.Center)) return false;
            if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)) return false;
            if (layout.Rubble.Any(r => r.ManhattanDistance(p) < 3)) return false;
            foreach (var d in Dirs4)
            {
                var n = p + d;
                if (!room.Contains(n) && layout.IsWalkable(n)) return false;      // beside an entrance
                if (layout.Vaults.Any(v => v.Door == n)) return false;
            }
            return true;
        }
        // mines heap spoil in their galleries too; elsewhere a mound needs a roomy room
        bool Roomy(Room r) => layout.Kind == LayoutKind.Mine ? Math.Min(r.W, r.H) >= 2 && r.Area >= 12 : r.W >= 5 && r.H >= 5;
        var rooms = layout.Rooms.Where(r => r != upRoom && r != layout.BossArena && Roomy(r) && layout.Vaults.All(v => v.Room != r)).ToList();
        Shuffle(rng, rooms);
        // ruins first, and generously
        foreach (var ruin in layout.Structures.Where(s => s.Kind == StructureKind.Ruin))
        {
            var cells = ruin.Room.Cells().Where(p => Ok(ruin.Room, p)).ToList();
            Shuffle(rng, cells);
            foreach (var c in cells.Take(2 + rng.Next(2))) if (Ok(ruin.Room, c)) layout.Rubble.Add(c);
        }
        var want = spec.RubbleCount;
        foreach (var room in rooms)
        {
            if (layout.Rubble.Count(r => layout.StructureAt(room)?.Kind != StructureKind.Ruin) >= want) break;
            if (layout.StructureAt(room)?.Kind == StructureKind.Ruin) continue;
            var cells = room.Cells().Where(p => Ok(room, p)).ToList();
            if (cells.Count == 0) continue;
            layout.Rubble.Add(cells[rng.Next(cells.Count)]);
        }
    }

    // ---- nests -------------------------------------------------------------------------------

    /// <summary>
    /// One to three one-cell nooks broken through a room's walls on different sides, with solid rock behind them, out
    /// of which a horde can stream. Never the start, exit or arena room, never a vault. Nest rooms are spread out:
    /// a room joined by a corridor to one that already has a nest is passed over until nothing else is left.
    /// </summary>
    private static void PlaceNests(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.NestCount <= 0) return;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var rooms = layout.Rooms.Where(r => r != upRoom && r != downRoom && r != layout.BossArena && r.W >= 4 && r.H >= 4
            && layout.Vaults.All(v => v.Room != r) && layout.Cellars.All(c => c.Room != r)).ToList();
        Shuffle(rng, rooms);
        bool Adjoins(Room room) => layout.Connections.Any(c =>
            (layout.Rooms[c.A] == room && layout.Nests.Any(n => n.Room == layout.Rooms[c.B])) || (layout.Rooms[c.B] == room && layout.Nests.Any(n => n.Room == layout.Rooms[c.A])));
        var ordered = rooms.Where(r => !Adjoins(r)).Concat(rooms.Where(Adjoins)).ToList();
        // a shrine (Ritual) breaches first and widest, and its neighbours next, so the rite is besieged from every side
        if (layout.Objective?.ShrineRoom is { } shrine)
        {
            var next = layout.Rooms.Where(r => r != shrine && layout.Connections.Any(c => (layout.Rooms[c.A] == shrine && layout.Rooms[c.B] == r) || (layout.Rooms[c.B] == shrine && layout.Rooms[c.A] == r))).ToList();
            ordered = new[] { shrine }.Concat(next.Where(ordered.Contains)).Concat(ordered.Where(r => r != shrine && !next.Contains(r))).ToList();
        }
        foreach (var room in ordered)
        {
            if (layout.Nests.Count >= spec.NestCount) break;
            if (layout.NestAt(room) != null) continue;
            var level = layout.LevelOf(room.Center);
            var mouths = new List<GridPos>();
            var outward = new List<GridPos>();
            var sides = new List<int> { 0, 1, 2, 3 };
            Shuffle(rng, sides);
            var want = room == layout.Objective?.ShrineRoom ? 3 : 2 + rng.Next(2);
            foreach (var side in sides)
            {
                if (mouths.Count >= want) break;
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    GridPos r, n;
                    switch (side)
                    {
                        case 0: r = new GridPos(room.X, room.Y + 1 + rng.Next(Math.Max(1, room.H - 2))); n = new GridPos(-1, 0); break;
                        case 1: r = new GridPos(room.Right, room.Y + 1 + rng.Next(Math.Max(1, room.H - 2))); n = new GridPos(1, 0); break;
                        case 2: r = new GridPos(room.X + 1 + rng.Next(Math.Max(1, room.W - 2)), room.Y); n = new GridPos(0, -1); break;
                        default: r = new GridPos(room.X + 1 + rng.Next(Math.Max(1, room.W - 2)), room.Bottom); n = new GridPos(0, 1); break;
                    }
                    if (layout[r] != Cell.Floor || layout.LevelOf(r) != level || Busy(layout, r) || layout.IsReserved(r) || layout.IsWater(r) || layout.Rubble.Contains(r)) continue;
                    if (layout.Ledges.Any(l => l.Cells.Contains(r) || l.RampBottom == r || l.RampTop == r)) continue;
                    var w = r + n;
                    var a = w + n;
                    if (!layout.InBounds(a.X, a.Y) || w.X < 1 || w.Y < 1 || w.X >= layout.Width - 1 || w.Y >= layout.Height - 1) continue; // rock behind, never the world's edge
                    if (layout[w] != Cell.Wall || layout.IsUnder(w) || layout.IsWalkable(a) || layout.IsUnder(a) || layout[a] == Cell.Secret) continue;
                    if (layout.IsWalkable(w + n.Perp) || layout.IsWalkable(w - n.Perp) || layout.Secrets.Any(s => s.Wall == w || s.Alcove == w)) continue;
                    if (mouths.Any(m => m.ManhattanDistance(w) < 3)) continue;
                    layout[w] = Cell.Floor;
                    layout.SetLevel(w, level);
                    mouths.Add(w);
                    outward.Add(-n);
                    break;
                }
            }
            if (mouths.Count < 1) continue;
            layout.Nests.Add(new Nest(room, mouths, outward));
        }
    }

    internal static readonly GridPos[] Dirs4 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    // ---- water -------------------------------------------------------------------------------

    /// <summary>Ponds pooled in room interiors and streams cut wall to wall across a room. Water stays walkable; it only slows.</summary>
    private static void PlaceWater(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.WaterCount <= 0) return;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        // ground level only: the bed is sunk half a metre, which an upper room's plinth has no room for
        var candidates = layout.Rooms.Where(r => r != upRoom && r != downRoom && r != layout.BossArena && r != layout.Objective?.ShrineRoom && r != layout.Objective?.JailRoom && r.W >= 5 && r.H >= 5 && layout.LevelOf(r.Center) == 0
            && layout.Vaults.All(v => v.Room != r) && layout.Ledges.All(l => l.Room != r) && layout.Cellars.All(c => c.Room != r) && layout.StructureAt(r) == null).ToList();
        Shuffle(rng, candidates);
        foreach (var room in candidates)
        {
            if (layout.Water.Count >= spec.WaterCount) break;
            var streamFirst = rng.Next(2) == 0;
            if (streamFirst ? TryStream(rng, layout, room) || TryPond(rng, layout, room) : TryPond(rng, layout, room) || TryStream(rng, layout, room)) continue;
        }
    }

    internal static bool Dry(FloorLayout layout, GridPos p)
        => Busy(layout, p) || layout.IsReserved(p) || layout.IsWater(p) || layout.Ledges.Any(l => l.RampBottom == p || l.RampTop == p);

    /// <summary>A one-cell channel across the room's short axis, from wall to wall, never through a doorway cell.</summary>
    private static bool TryStream(Random rng, FloorLayout layout, Room room)
    {
        var vertical = room.W >= room.H; // runs top to bottom across a wide room
        var span = vertical ? room.W : room.H;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var line = (vertical ? room.X : room.Y) + 1 + rng.Next(span - 2);
            var cells = (vertical ? Enumerable.Range(room.Y, room.H).Select(y => new GridPos(line, y)) : Enumerable.Range(room.X, room.W).Select(x => new GridPos(x, line))).ToList();
            if (cells.Any(c => Dry(layout, c))) continue;
            // both ends must meet solid wall: a stream that pours out of a corridor mouth reads wrong
            var flow = vertical ? new GridPos(0, 1) : new GridPos(1, 0);
            if (layout.IsWalkable(cells[0] - flow) || layout.IsWalkable(cells[^1] + flow)) continue;
            if (rng.Next(2) == 0) { flow = -flow; }
            foreach (var c in cells) layout.SetWater(c, WaterKind.Stream);
            // the crossing: the cell nearest the room's middle, never an end cell
            var inner = cells.Count > 2 ? cells.Skip(1).Take(cells.Count - 2).ToList() : cells;
            var bridge = inner.MinBy(c => c.ManhattanDistance(room.Center));
            layout.Water.Add(new WaterFeature(WaterKind.Stream, room, cells, flow, bridge));
            return true;
        }
        return false;
    }

    /// <summary>A blob of four to eight cells grown from a seed inside the room, away from its edges.</summary>
    private static bool TryPond(Random rng, FloorLayout layout, Room room)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var seed = new GridPos(room.X + 1 + rng.Next(room.W - 2), room.Y + 1 + rng.Next(room.H - 2));
            if (Dry(layout, seed)) continue;
            var cells = new List<GridPos> { seed };
            var want = rng.Next(4, 9);
            for (var grow = 0; grow < 40 && cells.Count < want; grow++)
            {
                var from = cells[rng.Next(cells.Count)];
                var n = from + Dirs4[rng.Next(4)];
                if (cells.Contains(n) || n.X <= room.X || n.X >= room.Right || n.Y <= room.Y || n.Y >= room.Bottom || Dry(layout, n)) continue;
                cells.Add(n);
            }
            if (cells.Count < 4) continue;
            foreach (var c in cells) layout.SetWater(c, WaterKind.Pond);
            layout.Water.Add(new WaterFeature(WaterKind.Pond, room, cells, default));
            return true;
        }
        return false;
    }

    private static GridPos RandomCell(Random rng, Room room, FloorLayout layout)
    {
        for (var i = 0; i < 20; i++)
        {
            var p = new GridPos(room.X + rng.Next(room.W), room.Y + rng.Next(room.H));
            if (layout[p] == Cell.Floor && p != layout.UpStairs && p != layout.DownStairs) return p;
        }
        return room.Center;
    }

    private static void PlaceTraps(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.TrapCount <= 0) return;
        var taken = new HashSet<GridPos>(layout.EnemySpawns.Concat(layout.ChestSpawns).Concat(layout.BreakableSpawns).Concat(layout.KeySpawns))
        {
            layout.UpStairs, layout.DownStairs,
        };
        foreach (var v in layout.Vaults) { taken.Add(v.Door); if (v.Key is { } k) taken.Add(k); if (v.Lever is { } l) taken.Add(l); }
        // avoid the cells right next to the stairs so spawning on a trap is impossible
        foreach (var s in new[] { layout.UpStairs, layout.DownStairs })
            for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++) taken.Add(new GridPos(s.X + ox, s.Y + oy));

        var corridors = new List<GridPos>();
        var roomCells = new List<GridPos>();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPos(x, y);
                if (layout[p] != Cell.Floor || taken.Contains(p) || layout.IsInVault(p) || layout.IsReserved(p) || layout.IsWater(p) || layout.Secrets.Any(sc => sc.Alcove == p)) continue;
                if (layout.RoomAt(p) == null) corridors.Add(p); else roomCells.Add(p);
            }
        Shuffle(rng, corridors);
        Shuffle(rng, roomCells);
        foreach (var p in corridors.Concat(roomCells))
        {
            if (layout.TrapSpawns.Count >= spec.TrapCount) break;
            if (layout.TrapSpawns.Any(t => t.ManhattanDistance(p) < 3)) continue;
            layout.TrapSpawns.Add(p);
        }
    }

    internal static void Shuffle<T>(Random rng, List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
