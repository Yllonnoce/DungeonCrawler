namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Adds the third dimension to a flat layout: rooms on two height levels joined by ramped corridors, ledges,
/// pits with cellars underneath, and secret alcoves. Deterministic for a given rng.
/// </summary>
public static class Verticality
{
    private static readonly GridPos[] Dirs = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    // ---- levels, ledges and ramps ------------------------------------------------------------------

    [ThreadStatic] private static HashSet<GridPos>? _bannedStore;
    private static HashSet<GridPos> _banned => _bannedStore ??= new HashSet<GridPos>();

    public static void AssignLevels(FloorSpec spec, Random rng, FloorLayout layout)
    {
        _banned.Clear();
        var rooms = layout.Rooms;
        if (spec.HighRoomChance <= 0 || rooms.Count < 2) return;

        var roomLevel = new int[rooms.Count];
        for (var i = 0; i < rooms.Count; i++)
            roomLevel[i] = rooms[i] == layout.BossArena ? 0 : rng.NextDouble() < spec.HighRoomChance ? 1 : 0;
        if (roomLevel.All(l => l == 0)) roomLevel[rng.Next(rooms.Count)] = rooms[0] == layout.BossArena && rooms.Count == 1 ? 0 : 1;

        var ledges = PickLedges(spec, rng, layout, roomLevel);
        var ramps = new List<(GridPos low, GridPos high, GridPos dir, int lowLevel, bool ledge)>();
        foreach (var l in ledges)
            ramps.Add((l.RampBottom, l.RampTop, new GridPos(0, -1), 0, true));

        // Rooms that could not be joined by a ramp are merged onto one level; merges only grow, so this converges.
        var group = Enumerable.Range(0, rooms.Count).ToArray();
        int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
        var flatten = false;

        for (var attempt = 0; attempt < rooms.Count * 6 + 60; attempt++)
        {
            ApplyLevels(layout, roomLevel, ledges, ramps);
            var source = ComputeCorridorLevels(layout, ramps);
            var seams = FindSeams(layout);
            if (seams.Count == 0) { Commit(layout, ledges); return; }

            var (p, q) = seams[0];
            if (TryPlaceRamp(layout, ramps, p, q) || TryPlaceRamp(layout, ramps, q, p)) continue;

            var sp = source[p.Y * layout.Width + p.X];
            var sq = source[q.Y * layout.Width + q.X];
            // a side fed by an existing ramp: that ramp is in the wrong place; ban its spot and drop it
            var rampSide = sp <= -2 ? sp : sq <= -2 ? sq : 0;
            if (rampSide != 0)
            {
                var ri = -rampSide - 2;
                if (ri < ramps.Count && !ramps[ri].ledge)
                {
                    _banned.Add(ramps[ri].low);
                    _banned.Add(ramps[ri].high);
                    ramps.RemoveAt(ri);
                    continue;
                }
            }
            if (sp < 0 && sq < 0) { flatten = true; break; }
            var a = sp < 0 ? sq : sp;
            var b = sq < 0 ? sp : sq;
            var ga = Find(a);
            var gb = Find(b);
            if (ga == gb) { flatten = true; break; }
            var areaA = rooms.Where((_, i) => Find(i) == ga).Sum(r => r.Area);
            var areaB = rooms.Where((_, i) => Find(i) == gb).Sum(r => r.Area);
            var level = areaA >= areaB ? roomLevel[a] : roomLevel[b];
            if (rooms.Select((r, i) => (r, i)).Any(t => (Find(t.i) == ga || Find(t.i) == gb) && t.r == layout.BossArena)) level = 0;
            group[gb] = ga;
            for (var i = 0; i < rooms.Count; i++)
                if (Find(i) == ga)
                {
                    roomLevel[i] = level;
                    if (level == 1) ledges.RemoveAll(l => l.Room == rooms[i]);
                }
            ramps.RemoveAll(r => !r.ledge);
            ramps.RemoveAll(r => r.ledge && ledges.All(l => l.RampBottom != r.low));
        }

        if (!flatten)
        {
            // attempts exhausted: keep what we have only if seam-free, else flatten
            ApplyLevels(layout, roomLevel, ledges, ramps);
            ComputeCorridorLevels(layout, ramps);
            if (FindSeams(layout).Count == 0) { Commit(layout, ledges); return; }
        }

        // give up: flat floor
        Array.Fill(roomLevel, 0);
        ledges.Clear();
        ramps.Clear();
        ApplyLevels(layout, roomLevel, ledges, ramps);
        ComputeCorridorLevels(layout, ramps);
        Commit(layout, ledges);
    }

    private static void Commit(FloorLayout layout, List<Ledge> ledges)
    {
        layout.Ledges.Clear();
        layout.Ledges.AddRange(ledges);
        foreach (var l in ledges)
        {
            layout.LedgeSpawns.Add(l.EnemySpawn);
            if (l.Chest is { } c) layout.ChestSpawns.Add(c);
        }
    }

    private static List<Ledge> PickLedges(FloorSpec spec, Random rng, FloorLayout layout, int[] roomLevel)
    {
        var result = new List<Ledge>();
        if (spec.LedgeCount <= 0) return result;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var candidates = new List<int>();
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            var room = layout.Rooms[i];
            if (roomLevel[i] != 0 || room.H < 5 || room.W < 4 || FloorGenerator.ChamferDepth(room) > 1) continue;
            if (layout.StructureAt(room) != null) continue;
            if (room == upRoom || room == downRoom || room == layout.BossArena || layout.Vaults.Any(v => v.Room == room)) continue;
            // corridors must not enter along the top row or beside the ramp column
            if (FloorGenerator.Entrances(layout, room).Any(e => e.Y == room.Y - 1 || (e.X == room.X - 1 && e.Y <= room.Y + 2) || (e.X == room.X && e.Y == room.Y - 1))) continue;
            candidates.Add(i);
        }
        FloorGenerator.Shuffle(rng, candidates);
        foreach (var i in candidates.Take(spec.LedgeCount))
        {
            var room = layout.Rooms[i];
            // the strip needs its corners: a chamfered room gets its top corners back (walls are raised again afterwards)
            layout[room.X, room.Y] = Cell.Floor;
            layout[room.Right, room.Y] = Cell.Floor;
            var cells = new List<GridPos> { new(room.X, room.Y) };
            for (var x = room.X + 1; x <= room.Right; x++) cells.Add(new GridPos(x, room.Y));
            var bottom = new GridPos(room.X, room.Y + 2);
            var top = new GridPos(room.X, room.Y + 1);
            GridPos? chest = room.W >= 4 && rng.NextDouble() < 0.5 ? new GridPos(room.X + 2, room.Y) : null;
            result.Add(new Ledge(room, cells, bottom, top, new GridPos(room.Right, room.Y), chest));
        }
        return result;
    }

    private static void ApplyLevels(FloorLayout layout, int[] roomLevel, List<Ledge> ledges, List<(GridPos low, GridPos high, GridPos dir, int lowLevel, bool ledge)> ramps)
    {
        Array.Fill(layout.Levels, (byte)0);
        layout.Ramps.Clear();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
                if (layout[x, y] == Cell.Ramp) layout[x, y] = Cell.Floor;

        for (var i = 0; i < layout.Rooms.Count; i++)
            foreach (var c in layout.Rooms[i].Cells()) layout.SetLevel(c, roomLevel[i]);
        foreach (var l in ledges)
            foreach (var c in l.Cells) layout.SetLevel(c, 1);
        foreach (var (low, high, dir, lowLevel, _) in ramps)
        {
            layout[low] = Cell.Ramp; layout[high] = Cell.Ramp;
            layout.SetLevel(low, lowLevel); layout.SetLevel(high, lowLevel);
            layout.Ramps[low] = new RampInfo(dir, lowLevel * 4);
            layout.Ramps[high] = new RampInfo(dir, lowLevel * 4 + 2);
        }
    }

    /// <summary>BFS from room cells (and ramp ends) through corridor cells, giving each the level of the cell it was reached from. Returns the source room per cell (-1 for none).</summary>
    private static int[] ComputeCorridorLevels(FloorLayout layout, List<(GridPos low, GridPos high, GridPos dir, int lowLevel, bool ledge)> ramps)
    {
        var source = new int[layout.Width * layout.Height];
        Array.Fill(source, -1);
        var visited = new bool[layout.Width * layout.Height];
        var queue = new Queue<GridPos>();
        void Seed(GridPos c, int level, int room)
        {
            if (!layout.IsWalkable(c) || layout[c] == Cell.Ramp) return;
            var i = c.Y * layout.Width + c.X;
            if (visited[i]) return;
            visited[i] = true;
            source[i] = room;
            layout.SetLevel(c, level);
            queue.Enqueue(c);
        }
        for (var r = 0; r < layout.Rooms.Count; r++)
            foreach (var c in layout.Rooms[r].Cells()) Seed(c, layout.LevelOf(c), r);
        for (var ri = 0; ri < ramps.Count; ri++)
        {
            var (low, high, dir, lowLevel, _) = ramps[ri];
            Seed(low - dir, lowLevel, -(ri + 2));
            Seed(high + dir, lowLevel + 1, -(ri + 2));
        }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            var i = c.Y * layout.Width + c.X;
            foreach (var d in Dirs)
            {
                var n = c + d;
                if (!layout.IsWalkable(n) || layout[n] == Cell.Ramp) continue;
                var ni = n.Y * layout.Width + n.X;
                if (visited[ni] || layout.RoomAt(n) != null) continue;
                visited[ni] = true;
                source[ni] = source[i];
                layout.SetLevel(n, layout.LevelOf(c));
                queue.Enqueue(n);
            }
        }
        return source;
    }

    private static List<(GridPos, GridPos)> FindSeams(FloorLayout layout)
    {
        var seams = new List<(GridPos, GridPos)>();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var p = new GridPos(x, y);
                if (!layout.IsWalkable(p)) continue;
                foreach (var d in new[] { new GridPos(1, 0), new GridPos(0, 1) })
                {
                    var q = p + d;
                    if (!layout.IsWalkable(q)) continue;
                    if (!layout.CanStep(p, q) && !(layout.Ramps.ContainsKey(p) || layout.Ramps.ContainsKey(q)) && Math.Abs(layout.HeightOf(p) - layout.HeightOf(q)) > 2.01f)
                    {
                        // a ledge edge inside a room is a deliberate drop, not a seam
                        if (layout.RoomAt(p) is { } rp && layout.RoomAt(q) == rp) continue;
                        seams.Add((p, q));
                    }
                    else if ((layout.Ramps.ContainsKey(p) || layout.Ramps.ContainsKey(q)) && !layout.CanStep(p, q) && d.IsParallelTo(layout.Ramps.TryGetValue(p, out var rp2) ? rp2.Dir : layout.Ramps[q].Dir))
                    {
                        seams.Add((p, q)); // a ramp whose end no longer matches its neighbour's level
                    }
                }
            }
        return seams;
    }

    /// <summary>
    /// Put a two-cell ramp on q's side of the seam. If the corridor bends right there, follow it (up to 8 cells) to the
    /// first straight stretch with solid sides; the cells before the ramp simply take p's level.
    /// </summary>
    private static bool TryPlaceRamp(FloorLayout layout, List<(GridPos low, GridPos high, GridPos dir, int lowLevel, bool ledge)> ramps, GridPos p, GridPos q)
    {
        if (layout[p] == Cell.Ramp || layout[q] == Cell.Ramp) return false;
        var lp = layout.LevelOf(p);
        var lq = layout.LevelOf(q);
        if (lp == lq) return false;
        var low = Math.Min(lp, lq);

        // breadth-first along corridor cells from q (never through rooms), trying to fit a ramp at each step
        var start = (cell: q, dir: q - p);
        var seen = new HashSet<GridPos> { p, q };
        var queue = new Queue<(GridPos cell, GridPos dir, int depth)>();
        queue.Enqueue((start.cell, start.dir, 0));
        while (queue.Count > 0)
        {
            var (cur, d, depth) = queue.Dequeue();
            if (RampFits(layout, cur, d))
            {
                var c1 = cur;
                var c2 = cur + d;
                if (lp < lq) ramps.Add((c1, c2, d, low, false));   // rising away from p
                else ramps.Add((c2, c1, -d, low, false));           // descending away from p
                return true;
            }
            if (depth >= 8 || layout.Vaults.Any(v => v.Door == cur)) continue;
            foreach (var dd in Dirs)
            {
                var n = cur + dd;
                if (seen.Contains(n) || !layout.IsWalkable(n) || layout[n] == Cell.Ramp || layout.RoomAt(n) != null) continue;
                seen.Add(n);
                queue.Enqueue((n, dd, depth + 1));
            }
        }
        return false;
    }

    private static bool RampFits(FloorLayout layout, GridPos c1, GridPos d)
    {
        var c2 = c1 + d;
        var beyond = c2 + d;
        foreach (var c in new[] { c1, c2 })
        {
            if (_banned.Contains(c)) return false;
            if (layout[c] != Cell.Floor || layout.RoomAt(c) != null || layout.Vaults.Any(v => v.Door == c) || layout.IsUnder(c)) return false;
            if (layout.IsWalkable(c + d.Perp) || layout.IsWalkable(c - d.Perp)) return false;
        }
        if (!layout.IsWalkable(beyond) || layout[beyond] == Cell.Ramp) return false;
        if (layout.Secrets.Any(sc => sc.Alcove == beyond)) return false;
        return true;
    }

    // ---- pits and cellars --------------------------------------------------------------------------

    public static void PlacePits(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.PitCount <= 0) return;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var candidates = layout.Rooms.Where(r => r.W >= 5 && r.H >= 5 && layout.LevelOf(r.Center) == 1 && r != layout.Objective?.ShrineRoom && r != layout.Objective?.JailRoom
            && r != upRoom && r != downRoom && r != layout.BossArena
            && layout.Vaults.All(v => v.Room != r) && layout.Ledges.All(l => l.Room != r)
            && r.Cells().All(c => layout[c] == Cell.Floor || FloorGenerator.IsChamfered(r, c))).ToList();
        FloorGenerator.Shuffle(rng, candidates);
        foreach (var room in candidates)
        {
            if (layout.Cellars.Count >= spec.PitCount) break;
            var pit = room.Center;
            var cells = new List<GridPos>();
            for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++) cells.Add(new GridPos(pit.X + ox, pit.Y + oy));
            if (cells.Any(c => layout.IsUnder(c) || c == layout.UpStairs || c == layout.DownStairs || layout.ChestSpawns.Contains(c))) continue;

            var exit = FindTunnel(layout, cells, out var target);
            if (exit == null) continue;

            layout[pit] = Cell.Pit;
            foreach (var c in cells) layout.SetUnder(c, true);
            foreach (var c in exit) layout.SetUnder(c, true);
            var chest = new GridPos(pit.X + 1, pit.Y + 1);
            var enemies = new List<GridPos> { new(pit.X - 1, pit.Y - 1), new(pit.X + 1, pit.Y - 1) };
            layout.Cellars.Add(new Cellar(room, pit, cells, exit, target, chest, enemies));
            layout.CellarSpawns.AddRange(enemies);
        }
    }

    /// <summary>BFS from the cellar through cells that are free at ground level, until a low main-layer floor cell is adjacent.</summary>
    private static List<GridPos>? FindTunnel(FloorLayout layout, List<GridPos> cellar, out GridPos target)
    {
        target = default;
        var cellarSet = cellar.ToHashSet();
        var cameFrom = new Dictionary<GridPos, GridPos>();
        var queue = new Queue<GridPos>();
        var depth = new Dictionary<GridPos, int>();
        foreach (var c in cellar) { queue.Enqueue(c); depth[c] = 0; }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (depth[c] > 16) continue;
            foreach (var d in Dirs)
            {
                var n = c + d;
                if (!layout.InBounds(n.X, n.Y) || cellarSet.Contains(n) || depth.ContainsKey(n)) continue;
                if (IsLowExitTarget(layout, n) && !cellarSet.Contains(c))
                {
                    target = n;
                    var path = new List<GridPos>();
                    for (var cur = c; !cellarSet.Contains(cur); cur = cameFrom[cur]) path.Add(cur);
                    path.Reverse();
                    return path;
                }
                if (!IsTunnelable(layout, n)) continue;
                depth[n] = depth[c] + 1;
                cameFrom[n] = c;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    private static bool IsTunnelable(FloorLayout layout, GridPos c)
    {
        if (layout.IsUnder(c) || c == layout.UpStairs || c == layout.DownStairs) return false;
        return layout[c] switch
        {
            Cell.Wall or Cell.Void => true,
            Cell.Floor or Cell.Door or Cell.Pit => layout.LevelOf(c) == 1,
            Cell.Ramp => layout.Ramps[c].LowHeight >= 4,
            _ => false,
        };
    }

    private static bool IsLowExitTarget(FloorLayout layout, GridPos c)
        => layout[c] is Cell.Floor && layout.LevelOf(c) == 0 && !layout.IsInVault(c) && !layout.Secrets.Any(s => s.Alcove == c)
           && (layout.RoomAt(c) == null || layout.Ledges.All(l => l.Room != layout.RoomAt(c)));

    // ---- secret alcoves ----------------------------------------------------------------------------

    public static void PlaceSecrets(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (spec.SecretCount <= 0) return;
        var rooms = layout.Rooms.Where(r => layout.Vaults.All(v => v.Room != r) && r != layout.BossArena).ToList();
        if (rooms.Count == 0) return;
        for (var attempt = 0; attempt < 40 && layout.Secrets.Count < spec.SecretCount; attempt++)
        {
            var room = rooms[rng.Next(rooms.Count)];
            var side = rng.Next(4);
            GridPos r, n;
            switch (side)
            {
                case 0: r = new GridPos(room.X, room.Y + rng.Next(room.H)); n = new GridPos(-1, 0); break;
                case 1: r = new GridPos(room.Right, room.Y + rng.Next(room.H)); n = new GridPos(1, 0); break;
                case 2: r = new GridPos(room.X + rng.Next(room.W), room.Y); n = new GridPos(0, -1); break;
                default: r = new GridPos(room.X + rng.Next(room.W), room.Bottom); n = new GridPos(0, 1); break;
            }
            if (layout[r] != Cell.Floor || layout.LevelOf(r) != layout.LevelOf(room.Center)) continue;
            var w = r + n;
            var a = w + n;
            if (layout[w] != Cell.Wall || layout[a] != Cell.Void || layout.IsUnder(w) || layout.IsUnder(a)) continue;
            var ok = true;
            for (var oy = -1; oy <= 1 && ok; oy++)
                for (var ox = -1; ox <= 1; ox++)
                {
                    var nb = new GridPos(a.X + ox, a.Y + oy);
                    if (nb == a || nb == w) continue;
                    if (!layout.InBounds(nb.X, nb.Y) || layout[nb] is not (Cell.Void or Cell.Wall) || layout.IsUnder(nb)) { ok = false; break; }
                }
            if (!ok) continue;
            layout[a] = Cell.Floor;
            layout.SetLevel(a, layout.LevelOf(r));
            layout[w] = Cell.Secret;
            layout.SetLevel(w, layout.LevelOf(r));
            layout.Secrets.Add(new SecretAlcove(layout.Secrets.Count, w, a, r));
            layout.ChestSpawns.Add(a);
        }
    }
}
