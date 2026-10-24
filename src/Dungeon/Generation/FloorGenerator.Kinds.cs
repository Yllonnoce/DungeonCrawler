namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The floor shapes other than the ordinary one (see <see cref="LayoutKind"/>): how each lays its rooms and joins them,
/// plus the two hazards, flooding and fire. Everything after room placement and corridors runs unchanged on any shape.
/// </summary>
public static partial class FloorGenerator
{
    // ---- labyrinth ---------------------------------------------------------------------------

    /// <summary>
    /// Small chambers on the odd lattice the maze will run on: odd corners and odd sizes, so every chamber wall lies on
    /// an even row or column that no maze passage can cross, and at least three cells (wall, passage, wall) apart.
    /// </summary>
    private static void PlaceLatticeRooms(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.BossFloor)
        {
            var size = Math.Min(spec.BossArenaSize, Math.Min(spec.Width, spec.Height) - 4) | 1;
            var arena = TryPlaceOdd(spec, rng, layout, size, size, attempts: 200) ?? new Room(1, 1, size, size);
            layout.Rooms.Add(arena);
            layout.BossArena = arena;
        }
        var target = rng.Next(spec.MinRooms, spec.MaxRooms + 1);
        for (var attempt = 0; attempt < 600 && layout.Rooms.Count < target; attempt++)
        {
            var w = rng.Next(spec.MinRoomSize, spec.MaxRoomSize + 1) | 1;
            var h = rng.Next(spec.MinRoomSize, spec.MaxRoomSize + 1) | 1;
            var room = TryPlaceOdd(spec, rng, layout, w, h, attempts: 1);
            if (room != null) layout.Rooms.Add(room);
        }
    }

    private static Room? TryPlaceOdd(FloorSpec spec, Random rng, FloorLayout layout, int w, int h, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            var x = rng.Next(1, Math.Max(2, spec.Width - w - 1)) | 1;
            var y = rng.Next(1, Math.Max(2, spec.Height - h - 1)) | 1;
            var candidate = new Room(x, y, w, h);
            if (candidate.Right > spec.Width - 3 || candidate.Bottom > spec.Height - 3) continue;
            if (layout.Rooms.Any(r => r.Intersects(candidate, spec.RoomMargin))) continue;
            return candidate;
        }
        return null;
    }

    /// <summary>
    /// A perfect maze over every lattice node outside the chambers (recursive backtracker; separate regions are joined
    /// where they touch), each chamber given one or two doors onto it, then most dead ends pruned back and a few walls
    /// opened for loops, so the result is a labyrinth to get lost in rather than a hedge maze with a single answer.
    /// </summary>
    private static void CarveMaze(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var w = layout.Width;
        var h = layout.Height;
        bool IsNode(GridPos p) => p.X >= 1 && p.Y >= 1 && p.X <= w - 3 && p.Y <= h - 3 && (p.X & 1) == 1 && (p.Y & 1) == 1;
        bool Free(GridPos p) => IsNode(p) && layout.RoomAt(p) == null;

        // carve trees region by region
        var tree = new Dictionary<GridPos, int>();
        var trees = 0;
        for (var y = 1; y <= h - 3; y += 2)
            for (var x = 1; x <= w - 3; x += 2)
            {
                var start = new GridPos(x, y);
                if (!Free(start) || tree.ContainsKey(start)) continue;
                var id = trees++;
                var stack = new Stack<GridPos>();
                stack.Push(start);
                tree[start] = id;
                layout[start] = Cell.Floor;
                while (stack.Count > 0)
                {
                    var p = stack.Peek();
                    var options = new List<GridPos>();
                    foreach (var d in Dirs4)
                    {
                        var n = new GridPos(p.X + d.X * 2, p.Y + d.Y * 2);
                        if (Free(n) && !tree.ContainsKey(n)) options.Add(d);
                    }
                    if (options.Count == 0) { stack.Pop(); continue; }
                    var dir = options[rng.Next(options.Count)];
                    var next = new GridPos(p.X + dir.X * 2, p.Y + dir.Y * 2);
                    layout[p + dir] = Cell.Floor;
                    layout[next] = Cell.Floor;
                    tree[next] = id;
                    stack.Push(next);
                }
            }
        // regions a chamber happened to split apart are joined where they meet
        if (trees > 1)
        {
            var parent = Enumerable.Range(0, trees).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            foreach (var (p, id) in tree.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
                foreach (var d in new[] { new GridPos(1, 0), new GridPos(0, 1) })
                {
                    var n = new GridPos(p.X + d.X * 2, p.Y + d.Y * 2);
                    if (!tree.TryGetValue(n, out var other) || Find(id) == Find(other)) continue;
                    layout[p + d] = Cell.Floor;
                    parent[Find(other)] = Find(id);
                }
        }

        // doors: a chamber opens onto a neighbouring node through the wall cell between them
        foreach (var room in layout.Rooms)
        {
            var candidates = new List<(GridPos wall, int side)>();
            foreach (var c in room.Cells())
            {
                if ((c.X & 1) == 0 || (c.Y & 1) == 0 || layout[c] != Cell.Floor) continue;
                for (var s = 0; s < 4; s++)
                {
                    var d = Dirs4[s];
                    var wall = c + d;
                    var node = wall + d;
                    if (room.Contains(wall) || !tree.ContainsKey(node)) continue;
                    candidates.Add((wall, s));
                }
            }
            if (candidates.Count == 0) continue;
            Shuffle(rng, candidates);
            var want = room == layout.BossArena ? 3 : 1 + (rng.NextDouble() < 0.45 ? 1 : 0);
            var used = new List<int>();
            foreach (var (wall, side) in candidates)
            {
                if (used.Count >= want) break;
                if (used.Contains(side)) continue;
                layout[wall] = Cell.Floor;
                used.Add(side);
            }
        }

        // prune: most dead ends are cut back so the maze breathes; chamber doors keep their passages alive
        var rounds = 7 + rng.Next(4);
        for (var round = 0; round < rounds; round++)
        {
            var ends = new List<GridPos>();
            for (var y = 1; y < h - 1; y++)
                for (var x = 1; x < w - 1; x++)
                {
                    var p = new GridPos(x, y);
                    if (layout[p] != Cell.Floor || layout.RoomAt(p) != null) continue;
                    var open = 0;
                    foreach (var d in Dirs4) if (layout[p + d] == Cell.Floor) open++;
                    if (open <= 1) ends.Add(p);
                }
            if (ends.Count == 0) break;
            foreach (var p in ends) layout[p] = Cell.Void;
        }
        // loops: a handful of walls between two live passages come down
        var live = tree.Keys.Where(p => layout[p] == Cell.Floor).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        var loops = Math.Max(3, (int)(live.Count * Math.Max(0.02, spec.ExtraEdgeChance * 0.35)));
        for (var attempt = 0; attempt < loops * 6 && loops > 0 && live.Count > 0; attempt++)
        {
            var p = live[rng.Next(live.Count)];
            var d = Dirs4[rng.Next(4)];
            var between = p + d;
            var n = between + d;
            if (layout[between] != Cell.Void || layout[n] != Cell.Floor || layout.RoomAt(n) != null || !IsNode(n)) continue;
            layout[between] = Cell.Floor;
            loops--;
        }
        // a chamber that lost its passage to pruning cannot happen (its door keeps the passage a through-cell), but a
        // chamber the maze never reached would be sealed: dig it a straight way to the nearest passage
        foreach (var room in layout.Rooms)
            if (Entrances(layout, room).Count == 0) DigToNearestPassage(layout, room);
    }

    private static void DigToNearestPassage(FloorLayout layout, Room room)
    {
        GridPos? best = null;
        var bestD = long.MaxValue;
        for (var y = 1; y < layout.Height - 1; y++)
            for (var x = 1; x < layout.Width - 1; x++)
            {
                var p = new GridPos(x, y);
                if (layout[p] != Cell.Floor || layout.RoomAt(p) != null) continue;
                var d = Dist2(p, room.Center);
                if (d < bestD) { bestD = d; best = p; }
            }
        if (best is { } target) CarveLine(layout, room.Center, new GridPos(target.X, room.Center.Y));
        if (best is { } t2) CarveLine(layout, new GridPos(t2.X, room.Center.Y), t2);
    }

    /// <summary>
    /// Which rooms the corridors join, read off the carved floor: from each room's entrances, walk the passages
    /// (never through another room) and note every room reached. Shapes that carve without an explicit edge list use it.
    /// </summary>
    internal static void DeriveConnections(FloorLayout layout)
    {
        layout.Connections.Clear();
        var rooms = layout.Rooms;
        var seenPairs = new HashSet<(int, int)>();
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            var seen = new HashSet<GridPos>();
            var queue = new Queue<GridPos>();
            foreach (var e in Entrances(layout, room)) if (seen.Add(e)) queue.Enqueue(e);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                var other = layout.RoomAt(p);
                if (other != null && other != room)
                {
                    var j = rooms.IndexOf(other);
                    var key = i < j ? (i, j) : (j, i);
                    if (seenPairs.Add(key)) layout.Connections.Add(key);
                    continue;
                }
                foreach (var d in Dirs4)
                {
                    var n = p + d;
                    if (layout.IsWalkable(n) && !room.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                }
            }
        }
    }

    // ---- ring ----------------------------------------------------------------------------------

    /// <summary>A big rotunda in the middle (the arena on a boss floor) and rooms spaced evenly around it on a circle.</summary>
    private static void PlaceRingRooms(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var cx = spec.Width / 2;
        var cy = spec.Height / 2;
        var hubSize = spec.BossFloor ? Math.Min(spec.BossArenaSize, Math.Min(spec.Width, spec.Height) - 4) : Math.Clamp(spec.MaxRoomSize + 3, 9, 13);
        var hub = new Room(cx - hubSize / 2, cy - hubSize / 2, hubSize, hubSize);
        layout.Rooms.Add(hub);
        if (spec.BossFloor) layout.BossArena = hub;

        var radius = Math.Min(spec.Width, spec.Height) / 2 - spec.MaxRoomSize / 2 - 2;
        var count = Math.Clamp(rng.Next(spec.MinRooms, spec.MaxRooms + 1), 5, 12);
        var a0 = rng.NextDouble() * Math.PI * 2;
        for (var i = 0; i < count; i++)
        {
            var angle = a0 + i * Math.PI * 2 / count;
            var rx = cx + (int)Math.Round(Math.Cos(angle) * radius);
            var ry = cy + (int)Math.Round(Math.Sin(angle) * radius);
            var w = rng.Next(spec.MinRoomSize, spec.MaxRoomSize + 1);
            var h = rng.Next(spec.MinRoomSize, spec.MaxRoomSize + 1);
            Room? placed = null;
            for (var shrink = 0; shrink < 6 && placed == null; shrink++)
            {
                var ww = Math.Max(spec.MinRoomSize, w - shrink);
                var hh = Math.Max(spec.MinRoomSize, h - shrink);
                var room = new Room(Math.Clamp(rx - ww / 2, 1, spec.Width - ww - 2), Math.Clamp(ry - hh / 2, 1, spec.Height - hh - 2), ww, hh);
                if (layout.Rooms.All(r => !r.Intersects(room, spec.RoomMargin))) placed = room;
            }
            if (placed != null) layout.Rooms.Add(placed);
        }
    }

    /// <summary>The ring corridor joins neighbours around the circle; spokes run in to the rotunda from every other room.</summary>
    private static void ConnectRing(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var rooms = layout.Rooms;
        if (rooms.Count < 2) return;
        var edges = new List<(int a, int b)>();
        var n = rooms.Count - 1; // ring rooms are 1..n in order around the circle
        for (var i = 1; i <= n; i++)
        {
            var next = i == n ? 1 : i + 1;
            if (next != i) edges.Add((i, next));
        }
        for (var i = 1; i <= n; i += 2) edges.Add((0, i));
        for (var i = 2; i <= n; i += 2) if (rng.NextDouble() < spec.ExtraEdgeChance * 2) edges.Add((0, i));
        var walk = new Random(layout.Seed ^ 0x0C0D);
        foreach (var (a, b) in edges.Distinct())
        {
            layout.Connections.Add((a, b));
            CarveCorridor(walk, layout, rooms[a].Center, rooms[b].Center);
        }
    }

    // ---- mine ----------------------------------------------------------------------------------

    /// <summary>Galleries two or three cells wide and long, with a few square chambers where they were widened.</summary>
    private static void PlaceGalleries(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.BossFloor)
        {
            var size = Math.Min(spec.BossArenaSize, Math.Min(spec.Width, spec.Height) - 4);
            var arena = TryPlace(spec, rng, layout, size, size, attempts: 200) ?? new Room(spec.Width - size - 2, spec.Height - size - 2, size, size);
            layout.Rooms.Add(arena);
            layout.BossArena = arena;
        }
        var target = rng.Next(spec.MinRooms, spec.MaxRooms + 1);
        for (var attempt = 0; attempt < 2500 && layout.Rooms.Count < target; attempt++)
        {
            int w, h;
            if (rng.NextDouble() < 0.65)
            {
                var length = rng.Next(7, 15);
                var width = rng.Next(2, 4);
                if (rng.Next(2) == 0) { w = length; h = width; } else { w = width; h = length; }
            }
            else
            {
                w = rng.Next(4, 8);
                h = rng.Next(4, 8);
            }
            var room = TryPlace(spec, rng, layout, w, h, attempts: 1);
            if (room != null) layout.Rooms.Add(room);
        }
    }

    /// <summary>
    /// Abandoned drifts: dead-end tunnels dug straight out of a gallery wall into solid rock, sometimes with one bend,
    /// touching nothing else. Traps like them; nothing spawns in them.
    /// </summary>
    private static void DigDrifts(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var want = 5 + rng.Next(4);
        var dug = 0;
        for (var attempt = 0; attempt < 120 && dug < want; attempt++)
        {
            var room = layout.Rooms[rng.Next(layout.Rooms.Count)];
            if (room == layout.BossArena) continue;
            var d = Dirs4[rng.Next(4)];
            var c = d.X != 0
                ? new GridPos(d.X > 0 ? room.Right : room.X, room.Y + rng.Next(room.H))
                : new GridPos(room.X + rng.Next(room.W), d.Y > 0 ? room.Bottom : room.Y);
            if (layout[c] != Cell.Floor) continue;
            var path = new List<GridPos>();
            var p = c;
            var dir = d;
            var length = rng.Next(4, 10);
            var bendAt = rng.NextDouble() < 0.4 ? rng.Next(2, length - 1) : -1;
            var ok = true;
            for (var i = 0; i < length; i++)
            {
                if (i == bendAt) dir = rng.Next(2) == 0 ? dir.Perp : -dir.Perp;
                p += dir;
                if (p.X < 1 || p.Y < 1 || p.X >= layout.Width - 1 || p.Y >= layout.Height - 1 || layout[p] != Cell.Void) { ok = false; break; }
                // the tunnel keeps rock on every side: no cell of it may touch any floor but its own
                for (var oy = -1; oy <= 1 && ok; oy++)
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        var q = new GridPos(p.X + ox, p.Y + oy);
                        if (q == p || path.Contains(q) || q == c) continue;
                        if (layout[q] != Cell.Void || layout.RoomAt(q) != null) { ok = false; break; }
                    }
                if (!ok) break;
                path.Add(p);
            }
            if (!ok || path.Count < 3) continue;
            foreach (var cell in path) layout[cell] = Cell.Floor;
            dug++;
        }
    }

    // ---- flooding ------------------------------------------------------------------------------

    /// <summary>Most eligible rooms go under water entirely (ground level only; never the start, exit, arena or a vault).</summary>
    private static void FloodRooms(FloorSpec spec, Random rng, FloorLayout layout)
    {
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var candidates = layout.Rooms.Where(r => r != upRoom && r != downRoom && r != layout.BossArena && r.W >= 4 && r.H >= 4 && layout.LevelOf(r.Center) == 0
            && layout.Vaults.All(v => v.Room != r) && layout.Ledges.All(l => l.Room != r) && layout.Cellars.All(c => c.Room != r) && layout.StructureAt(r) == null).ToList();
        Shuffle(rng, candidates);
        var flooded = 0;
        foreach (var room in candidates)
        {
            if (flooded >= 2 && rng.NextDouble() > 0.8) continue; // the odd dry room
            var cells = room.Cells().Where(p => layout[p] == Cell.Floor && !Dry(layout, p) && layout.LevelOf(p) == 0).ToList();
            if (cells.Count < 4) continue;
            foreach (var c in cells) layout.SetWater(c, WaterKind.Flood);
            layout.Water.Add(new WaterFeature(WaterKind.Flood, room, cells, default));
            flooded++;
        }
        // a stream or two still cuts across whatever stayed dry
        PlaceWater(spec with { WaterCount = Math.Min(spec.WaterCount, 1) }, rng, layout);
    }

    // ---- fire ----------------------------------------------------------------------------------

    /// <summary>
    /// Fires on room floors, spread out and never beside a doorway or in the start room, so every room can still be
    /// crossed; they are reserved cells (nothing spawns on them) that bodies path around and the party can wade through.
    /// </summary>
    private static void PlaceFires(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.Floor == 10 && layout.Kind == LayoutKind.Burning && spec.BossFloor && layout.BossArena is { } arena)
        {
            // the Hatchery (floor 10, the Broodmother): eight fires in a broken ring two cells out from the arena's centre, open along the axes
            var centre = arena.Center;
            foreach (var (dx, dy) in new[] { (2, 1), (2, -1), (-2, 1), (-2, -1), (1, 2), (-1, 2), (1, -2), (-1, -2) })
            {
                var p = centre + new GridPos(dx, dy);
                if (arena.Contains(p) && !Busy(layout, p) && !layout.IsReserved(p) && !layout.IsWater(p) && !layout.IsUnder(p) && layout.LevelOf(p) == layout.LevelOf(centre)) layout.Fires.Add(p);
            }
        }
        if (spec.FireCount <= 0) return;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var rooms = layout.Rooms.Where(r => r != upRoom && r != layout.BossArena && r.W >= 4 && r.H >= 4 && layout.Vaults.All(v => v.Room != r)).ToList();
        Shuffle(rng, rooms);
        var pool = new List<GridPos>();
        foreach (var room in rooms)
        {
            var level = layout.LevelOf(room.Center);
            foreach (var p in room.Cells())
            {
                if (Busy(layout, p) || layout.IsReserved(p) || layout.IsWater(p) || layout.IsUnder(p) || layout.LevelOf(p) != level) continue;
                if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)) continue;
                if (layout.Secrets.Any(s => s.Facing == p)) continue;
                var nearDoor = false;
                foreach (var d in Dirs4)
                {
                    var n = p + d;
                    if ((!room.Contains(n) && layout.IsWalkable(n)) || layout.Vaults.Any(v => v.Door == n) || layout.IsNestMouth(n)) { nearDoor = true; break; }
                }
                if (!nearDoor) pool.Add(p);
            }
        }
        Shuffle(rng, pool);
        foreach (var p in pool)
        {
            if (layout.Fires.Count >= spec.FireCount) break;
            if (layout.Fires.Any(f => f.ManhattanDistance(p) < 3)) continue;
            layout.Fires.Add(p);
        }
    }
}
