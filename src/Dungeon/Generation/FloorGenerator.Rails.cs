namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The mine-cart line round a railed ring floor (see <see cref="RailLine"/>): a twin-track tunnel through the rock band
/// outside the ring rooms, from the start room through up to <see cref="FloorSpec.RailCount"/> stops to the exit room.
/// Planned in full first and carved only when the whole line holds, so a floor that cannot take one is left untouched.
/// </summary>
public static partial class FloorGenerator
{
    /// <summary>Extra cost per turn in the tunnel search: long straight runs ride better and leave room for ramps.</summary>
    private const int RailTurnCost = 4;
    /// <summary>Extra cost per lane for a tunnel cell with no room beside it for a pocket: the line keeps a cell off walls where it can.</summary>
    private const int RailNoPocketCost = 3;

    /// <summary>Rock needed outside a platform: its wall (the enemy lane), a pocket and the pocket's wall.</summary>
    private const int RailPlatformDepth = 3;
    /// <summary>Rock needed outside a platform's end where the lanes dip through the wall: the wall, two cells of leg, and the row they turn in.</summary>
    private const int RailDipDepth = 4;

    /// <summary>
    /// A room on the line: the party lane runs inside along its outer edge (the platform), the enemy lane in the wall outside
    /// it. At each end the lanes either run straight on out through the side wall (where the platform reaches the room's
    /// corner) or dip through the outer wall at the platform's end cell; the start room has only an exit, the exit room only an entry.
    /// </summary>
    private sealed class RailRoom
    {
        public required int Index { get; init; }
        public required Room Room { get; init; }
        /// <summary>Outward: away from the floor's centre, and the side the enemy lane runs on.</summary>
        public required GridPos Out { get; init; }
        /// <summary>The way the line travels along the platform.</summary>
        public required GridPos Along { get; init; }
        public required List<GridPos> Platform { get; init; }
        public required int Level { get; init; }
        /// <summary>Whether the lanes come in (go out) through the outer wall rather than straight along the edge through the side wall.</summary>
        public required bool EntryDip { get; init; }
        public required bool ExitDip { get; init; }
        /// <summary>The party lane's mouth cells, just outside the room, and the way the lane crosses them.</summary>
        public GridPos Entry => EntryDip ? Platform[0] + Out : Platform[0] - Along;
        public GridPos Exit => ExitDip ? Platform[^1] + Out : Platform[^1] + Along;
        public GridPos EntryHeading => EntryDip ? -Out : Along;
        public GridPos ExitHeading => ExitDip ? Out : Along;
        /// <summary>Cells outside the room the room's own stretch of line takes: the wall over the platform and the mouths.</summary>
        public HashSet<GridPos> Owned { get; } = new();
    }

    /// <summary>A routed stretch of party lane between two rooms' mouths, with the level (and ramp) of each cell.</summary>
    private sealed record RailSegment(RailRoom From, RailRoom To, List<GridPos> Cells, List<int> Levels, List<RampInfo?> Ramps);

    /// <summary>
    /// Lay the line, or leave the floor untouched with <see cref="FloorLayout.Rail"/> null when it will not fit. The stops
    /// are ring rooms between the start and the exit, spread evenly along the side of the circle with more of them; a stop
    /// the tunnel cannot reach is dropped. Adds a connection for each consecutive pair of line rooms.
    /// </summary>
    internal static void LayRails(FloorSpec spec, Random rng, FloorLayout layout)
    {
        // what was kept for the line is let go: the line takes what it needs below, everything after may use the rest
        layout.RailKeep.Clear();
        layout.RailRock.Clear();
        layout.RailFailure = null;
        if (spec.RailCount <= 0 || layout.Kind != LayoutKind.Ring || layout.Rail != null) return;
        List<RailSegment>? plan;
        List<GridPos>? pockets = null;
        int sign = 0;
        try
        {
            plan = PlanRails(spec.RailCount, rng, layout, out sign, out pockets);
        }
        catch (Exception e)
        {
            plan = null; // planning only reads the floor: a fault in it costs the line, never the floor
            layout.RailFailure = $"{e.GetType().Name}: {e.Message}";
            return;
        }
        if (plan == null || pockets == null) { layout.RailFailure = "no line fits (no eligible stop rooms, or no route to the exit)"; return; }
        CommitRails(layout, plan, sign, pockets);
    }

    // ---- planning ----------------------------------------------------------------------------------

    private static List<RailSegment>? PlanRails(int railCount, Random rng, FloorLayout layout, out int sign, out List<GridPos>? pockets)
    {
        sign = 0;
        pockets = null;
        var n = layout.RingRooms;
        if (n < 2 || n >= layout.Rooms.Count) return null;
        var start = RingIndexOf(layout, layout.UpStairs, n);
        var exit = RingIndexOf(layout, layout.DownStairs, n);
        if (start < 0 || exit < 0 || start == exit || !RailRoleOk(layout, layout.Rooms[start]) || !RailRoleOk(layout, layout.Rooms[exit])) return null;

        // both ways round the circle; the one with more eligible rooms first (on a tie, a coin from the rail's own rng)
        var ways = new List<(int way, List<int> eligible)>();
        foreach (var way in new[] { 1, -1 })
        {
            var eligible = new List<int>();
            for (var i = Step(start, way, n); i != exit; i = Step(i, way, n))
                if (RailRoleOk(layout, layout.Rooms[i]) && MakeRailRoom(layout, i, way) != null) eligible.Add(i);
            ways.Add((way, eligible));
        }
        var coin = rng.Next(2);
        ways = ways.OrderByDescending(w => w.eligible.Count).ThenBy(w => w.way == 1 ? coin : 1 - coin).ToList();

        (List<RailSegment> segs, int s, List<GridPos> pk)? best = null;
        foreach (var (way, eligible) in ways)
        {
            var from = MakeRailRoom(layout, start, way, RailEnds.Exit);
            var to = MakeRailRoom(layout, exit, way, RailEnds.Entry);
            if (from == null || to == null) continue;
            var count = Math.Min(railCount, eligible.Count);
            if (count < 1) continue; // a line needs at least one stop between the start and the exit
            var chosen = new List<RailRoom>();
            for (var k = 0; k < count; k++) chosen.Add(MakeRailRoom(layout, eligible[(int)((k + 0.5) * eligible.Count / count)], way)!);
            var s = -way; // increasing index runs the way Perp turns, so the outward side is the opposite hand
            var segs = RouteLine(layout, from, chosen, to, s);
            if (segs == null || segs.Count < 2) continue; // every stop dropped: no line this way
            var line = LineCells(segs, s, out var levels, out var ramps);
            if (line == null) continue;
            if (!LaneHeightsOk(line.Value.party, line.Value.enemy, levels, ramps, s)) continue;
            var pk = PlaceRailPockets(layout, segs, line.Value.party, line.Value.enemy, ramps, s);
            if (pk == null || !ValidateLine(layout, segs, line.Value.party, line.Value.enemy, pk, null)) continue;
            if (best == null || segs.Count > best.Value.segs.Count) best = (segs, s, pk);
            if (segs.Count == railCount + 1) break;
        }
        if (best == null) return null;
        sign = best.Value.s;
        pockets = best.Value.pk;
        return best.Value.segs;
    }

    private static int Step(int i, int way, int n) => way > 0 ? (i == n ? 1 : i + 1) : (i == 1 ? n : i - 1);

    private static int RingIndexOf(FloorLayout layout, GridPos p, int n)
    {
        for (var i = 1; i <= n; i++) if (layout.Rooms[i].Contains(p)) return i;
        return -1;
    }

    /// <summary>A room the cart may stop in: never the arena, a vault (the safe room and the jail are vaults), the jail or the shrine room.</summary>
    private static bool RailRoleOk(FloorLayout layout, Room r)
        => r != layout.BossArena && layout.Vaults.All(v => v.Room != r) && r != layout.Objective?.JailRoom && r != layout.Objective?.ShrineRoom
           && (layout.SafeRoom < 0 || layout.Rooms[layout.SafeRoom] != r);

    /// <summary>The side of travel the enemy lane runs on: outward, for a line going round the circle in <paramref name="sign"/>'s sense.</summary>
    private static GridPos RailSide(GridPos d, int sign) => sign > 0 ? d.Perp : -d.Perp;

    private static bool Rock(FloorLayout layout, GridPos p) => layout[p] is Cell.Void or Cell.Wall && !layout.IsUnder(p);

    /// <summary>A cell the line may carve or stand next to: inside the grid with a cell to spare for its walls.</summary>
    private static bool InRailRange(FloorLayout layout, GridPos p) => p.X >= 1 && p.Y >= 1 && p.X <= layout.Width - 2 && p.Y <= layout.Height - 2;

    private static IEnumerable<GridPos> Around8(GridPos p)
    {
        for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
                if (ox != 0 || oy != 0) yield return new GridPos(p.X + ox, p.Y + oy);
    }

    /// <summary>Which ends of a room's platform the line uses: a stop both, the start room only its exit, the exit room only its entry.</summary>
    [Flags] private enum RailEnds { Entry = 1, Exit = 2, Both = 3 }

    /// <summary>
    /// The room's stretch of line when travelled <paramref name="way"/> round the circle, or null when none fits: on the side
    /// facing away from the floor's centre (or, failing that, its other outward side), the longest run of edge cells that are
    /// plain floor at the room's level with nothing on them and solid rock outside, with a way in and out at the ends the line uses.
    /// </summary>
    private static RailRoom? MakeRailRoom(FloorLayout layout, int index, int way, RailEnds ends = RailEnds.Both)
    {
        var room = layout.Rooms[index];
        var d = new GridPos(room.Center.X - layout.Width / 2, room.Center.Y - layout.Height / 2);
        var major = RailOutward(layout, room);
        var minor = major.X != 0 ? new GridPos(0, Math.Sign(d.Y)) : new GridPos(Math.Sign(d.X), 0);
        return MakeRailRoomOn(layout, index, way, major, ends) ?? (minor != default ? MakeRailRoomOn(layout, index, way, minor, ends) : null);
    }

    /// <summary>The way a ring room faces away from the floor's centre, along its stronger axis.</summary>
    internal static GridPos RailOutward(FloorLayout layout, Room room)
    {
        var d = new GridPos(room.Center.X - layout.Width / 2, room.Center.Y - layout.Height / 2);
        return Math.Abs(d.X) > Math.Abs(d.Y) ? new GridPos(Math.Sign(d.X), 0) : new GridPos(0, d.Y >= 0 ? 1 : -1);
    }

    /// <summary>
    /// Keep each ring room's outer edge for the line until it is laid (see <see cref="FloorLayout.RailKeep"/>): the edge cells
    /// and the two cells inside each of its ends are reserved, so nothing is placed on them and no breach or alcove opens beside
    /// them; the rock outside, as deep as a dip needs and a cell past either end, is kept solid.
    /// </summary>
    internal static void KeepRailEdges(FloorLayout layout)
    {
        for (var i = 1; i <= layout.RingRooms && i < layout.Rooms.Count; i++)
        {
            var room = layout.Rooms[i];
            var n = RailOutward(layout, room);
            var t = n.Perp;
            var edge = n.X > 0 ? room.Right : n.X < 0 ? room.X : n.Y > 0 ? room.Bottom : room.Y;
            var cells = (n.X != 0 ? Enumerable.Range(room.Y, room.H).Select(y => new GridPos(edge, y)) : Enumerable.Range(room.X, room.W).Select(x => new GridPos(x, edge))).ToList();
            layout.RailKeep.UnionWith(cells);
            var first = cells.MinBy(c => c.X * t.X + c.Y * t.Y);
            var last = cells.MaxBy(c => c.X * t.X + c.Y * t.Y);
            for (var k = 1; k <= 2; k++) { layout.RailKeep.Add(first - new GridPos(n.X * k, n.Y * k)); layout.RailKeep.Add(last - new GridPos(n.X * k, n.Y * k)); }
            foreach (var c in cells)
                for (var k = 1; k <= RailDipDepth + 1; k++)
                    for (var j = -1; j <= 1; j++)
                        layout.RailRock.Add(c + new GridPos(n.X * k + t.X * j, n.Y * k + t.Y * j));
        }
    }

    private static RailRoom? MakeRailRoomOn(FloorLayout layout, int index, int way, GridPos outward, RailEnds ends)
    {
        var room = layout.Rooms[index];
        var along = way > 0 ? outward.Perp : -outward.Perp;
        var edge = outward.X > 0 ? room.Right : outward.X < 0 ? room.X : outward.Y > 0 ? room.Bottom : room.Y;
        var cells = (outward.X != 0
                ? Enumerable.Range(room.Y, room.H).Select(y => new GridPos(edge, y))
                : Enumerable.Range(room.X, room.W).Select(x => new GridPos(x, edge)))
            .OrderBy(c => c.X * along.X + c.Y * along.Y).ToList();
        var level = layout.LevelOf(room.Center);
        bool Solid(GridPos q) => InRailRange(layout, q) && Rock(layout, q) && !layout.Rooms.Any(r => r.Contains(q));
        GridPos At(GridPos c, int outwards, int forwards) => c + new GridPos(outward.X * outwards + along.X * forwards, outward.Y * outwards + along.Y * forwards);
        // solid rock outside a cell, this many deep, from <paramref name="back"/> cells behind it to <paramref name="ahead"/> ahead
        bool Deep(GridPos c, int depth, int back = 1, int ahead = 1)
        {
            for (var k = 1; k <= depth; k++)
                for (var j = -back; j <= ahead; j++)
                    if (!Solid(At(c, k, j))) return false;
            return true;
        }
        // a dip's leg takes a pocket on its outer side: two cells of rock there
        bool Dips(GridPos c, bool entry) => Deep(c, RailDipDepth, entry ? 2 : 1, entry ? 1 : 2);
        // straight out through the side wall: the mouth and the cells past it solid, from two rows inside the edge (a
        // pocket and its wall) to two out
        bool RunsOut(GridPos end, int forwards)
        {
            for (var k = 1; k <= 3; k++)
                for (var j = -2; j <= 2; j++)
                    if (!Solid(At(end, j, forwards * k))) return false;
            return true;
        }
        var ok = cells.Select(c => PlatformClear(layout, room, c, level) && Deep(c, RailPlatformDepth) && Around8(c).All(q => room.Contains(q) || Solid(q))).ToList();
        // each run of usable cells, trimmed at either end to where the lanes can get in or out; the longest wins, then the
        // one nearest the middle of the edge, then the first
        RailRoom? best = null;
        double bestMid = double.MaxValue;
        for (var i = 0; i < cells.Count;)
        {
            if (!ok[i]) { i++; continue; }
            var j = i;
            while (j + 1 < cells.Count && ok[j + 1] && !layout.IsEdgeBlocked(cells[j], cells[j + 1])) j++;
            // each end dips through the outer wall (the tunnel comes to it from the open rock outside: tried first) or runs
            // straight out through the side wall where the run reaches the room's corner; a dip may be trimmed inward
            var needEntry = (ends & RailEnds.Entry) != 0;
            var needExit = (ends & RailEnds.Exit) != 0;
            foreach (var (entryDip, exitDip) in new[] { (true, true), (true, false), (false, true), (false, false) })
            {
                if ((!needEntry && !entryDip) || (!needExit && !exitDip)) continue; // an end the line does not use counts as a dip, trimmed by nothing
                int from = i, to = j;
                if (needEntry && entryDip) while (from <= to && !Dips(cells[from], true)) from++;
                if (needEntry && !entryDip && !(i == 0 && RunsOut(cells[0], -1))) continue;
                if (needExit && exitDip) while (to >= from && !Dips(cells[to], false)) to--;
                if (needExit && !exitDip && !(j == cells.Count - 1 && RunsOut(cells[^1], 1))) continue;
                // two dips need the enemy lane's two turns a cell apart; one needs a cell beside its turn
                var dips = (needEntry && entryDip ? 1 : 0) + (needExit && exitDip ? 1 : 0);
                var need = dips == 2 ? 5 : dips == 1 ? 3 : 2;
                var len = to - from + 1;
                if (len < need) continue;
                var mid = Math.Abs((from + to) / 2.0 - (cells.Count - 1) / 2.0);
                if (best == null || len > best.Platform.Count || (len == best.Platform.Count && mid < bestMid))
                {
                    best = new RailRoom { Index = index, Room = room, Out = outward, Along = along, Platform = cells.GetRange(from, len), Level = level, EntryDip = needEntry && entryDip, ExitDip = needExit && exitDip };
                    bestMid = mid;
                }
                break; // the most-dipping way that fits this run
            }
            i = j + 1;
        }
        if (best == null) return null;
        foreach (var c in best.Platform) best.Owned.Add(c + outward);
        if ((ends & RailEnds.Entry) != 0 && !best.EntryDip) { best.Owned.Add(best.Entry); best.Owned.Add(best.Entry + outward); }
        if ((ends & RailEnds.Exit) != 0 && !best.ExitDip) { best.Owned.Add(best.Exit); best.Owned.Add(best.Exit + outward); }
        foreach (var c in best.Owned)
            foreach (var q in Around8(c))
                if (!room.Contains(q) && !best.Owned.Contains(q) && !Solid(q)) return null;
        return best;
    }

    private static bool PlatformClear(FloorLayout layout, Room room, GridPos p, int level)
    {
        var cell = layout[p];
        if (!(cell == Cell.Floor || (cell == Cell.Wall && IsChamfered(room, p)))) return false;
        if (layout.IsUnder(p) || layout.LevelOf(p) != level || layout.Ramps.ContainsKey(p) || layout.IsWater(p) || layout.IsReserved(p)) return false;
        if (p == layout.UpStairs || p == layout.DownStairs) return false;
        if (layout.EnemySpawns.Contains(p) || layout.ChestSpawns.Contains(p) || layout.BreakableSpawns.Contains(p) || layout.TrapSpawns.Contains(p)) return false;
        if (layout.KeySpawns.Contains(p) || layout.LedgeSpawns.Contains(p) || layout.CellarSpawns.Contains(p)) return false;
        if (layout.Vaults.Any(v => v.Key == p || v.Lever == p || v.Door == p)) return false;
        if (layout.Secrets.Any(s => s.Facing == p || s.Alcove == p || s.Wall == p)) return false;
        if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p || l.Chest == p)) return false;
        if (layout.Cellars.Any(c => c.Pit == p || c.ExitTarget == p || c.Exit.Contains(p))) return false;
        if (layout.Nests.Any(n => n.Mouths.Contains(p))) return false;
        if (layout.Objective is { } o)
        {
            if (o.PrisonerCells.Contains(p) || o.ExtraKeyCells.Contains(p) || o.Altar == p || o.BreachLevers.Any(b => b.Lever == p) || o.Plaques.Any(q => q.Cell == p)) return false;
            if (o.Puzzle is { } pz && (pz.Levers.Contains(p) || pz.Gates.Contains(p))) return false;
        }
        return true;
    }

    /// <summary>
    /// Route the tunnel room to room. A stop that cannot be reached is dropped and the next one tried; when the exit cannot
    /// be reached the last stop is given up and the exit tried from the one before. Null when the exit cannot be reached at all.
    /// </summary>
    private static List<RailSegment>? RouteLine(FloorLayout layout, RailRoom start, List<RailRoom> stops, RailRoom exit, int sign)
    {
        var queue = new List<RailRoom>(stops) { exit };
        var segs = new List<RailSegment>();
        var at = start;
        var next = 0;
        while (next < queue.Count)
        {
            var target = queue[next];
            var ahead = RoomCells(queue.Skip(next + 1));
            var behind = RoomCells(new[] { start }.Concat(segs.Select(s => s.From)).Where(r => r != at));
            var seg = RouteSegment(layout, at, target, sign, Foreign(ahead, behind, segs, sign, at), ahead, segs);
            if (seg != null)
            {
                segs.Add(seg);
                at = target;
                next++;
                continue;
            }
            if (target != exit) { queue.RemoveAt(next); continue; }
            if (segs.Count == 0) return null;
            // the exit is out of reach from the last stop: give that stop up and try again from the one before
            var last = segs[^1];
            segs.RemoveAt(segs.Count - 1);
            queue.Remove(last.To);
            next--;
            at = last.From;
        }
        return segs;
    }

    /// <summary>The rooms' cells on the line: their platforms and their walls.</summary>
    private static HashSet<GridPos> RoomCells(IEnumerable<RailRoom> rooms)
    {
        var cells = new HashSet<GridPos>();
        foreach (var r in rooms) { cells.UnionWith(r.Owned); cells.UnionWith(r.Platform); }
        return cells;
    }

    /// <summary>Everything the stretch being routed keeps off: the rooms still ahead and those behind, and the stretches already routed (both lanes).</summary>
    private static HashSet<GridPos> Foreign(HashSet<GridPos> ahead, HashSet<GridPos> behind, List<RailSegment> segs, int sign, RailRoom a)
    {
        var foreign = new HashSet<GridPos>(ahead);
        foreign.UnionWith(behind);
        if (segs.Count > 0 && LineCells(segs, sign, out _, out _) is { } line) { foreign.UnionWith(line.party); foreign.UnionWith(line.enemy); }
        foreign.ExceptWith(a.Owned);
        foreign.ExceptWith(a.Platform);
        return foreign;
    }

    /// <summary>
    /// A* for the party lane from <paramref name="a"/>'s exit mouth out through the rock to <paramref name="b"/>'s entry mouth,
    /// the enemy lane its twin one cell outward; each cell's sides must be rock, the line itself or the two rooms' own stretches
    /// of line. Step cost 1, +<see cref="RailTurnCost"/> per turn, +<see cref="RailNoPocketCost"/> for each lane that could not
    /// take a pocket there; turns at least two cells apart, and the legs at both mouths straight for two cells. The stretch,
    /// its ramp and the line so far must then hold (<see cref="ValidateLine"/>, <see cref="LaneHeightsOk"/>), or it is no route.
    /// </summary>
    private static RailSegment? RouteSegment(FloorLayout layout, RailRoom a, RailRoom b, int sign, HashSet<GridPos> foreign, HashSet<GridPos> ahead, List<RailSegment> prior)
    {
        int w = layout.Width, h = layout.Height;
        var laneOk = new bool[w * h];
        var pocketOk = new bool[w * h];
        var own = new HashSet<GridPos>(a.Owned);
        own.UnionWith(b.Owned);
        // the party lane keeps out from over a platform but for its two legs: the enemy lane turns in there
        var shadow = new HashSet<GridPos>();
        foreach (var r in new[] { a, b })
            for (var i = r.EntryDip ? 1 : 0; i < r.Platform.Count - (r.ExitDip ? 1 : 0); i++)
                for (var k = 2; k <= 4; k++) shadow.Add(r.Platform[i] + new GridPos(r.Out.X * k, r.Out.Y * k));
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = new GridPos(x, y);
                if (!InRailRange(layout, c) || !Rock(layout, c) || foreign.Contains(c) || layout.Rooms.Any(r => r.Contains(c))) continue;
                // the rooms' own wall cells were proven when the rooms were picked
                if (a.Owned.Contains(c) || b.Owned.Contains(c)) { laneOk[y * w + x] = true; continue; }
                var lane = true;
                var free = true;
                foreach (var q in Around8(c))
                {
                    var rock = Rock(layout, q) && !foreign.Contains(q);
                    if (!rock) free = false;
                    if (!rock && !own.Contains(q)) { lane = false; break; }
                }
                laneOk[y * w + x] = lane;
                pocketOk[y * w + x] = lane && free;
            }
        bool Ok(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h && laneOk[p.Y * w + p.X];
        bool Free(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h && pocketOk[p.Y * w + p.X];
        if (!Ok(a.Exit) || !Ok(a.Exit + RailSide(a.ExitHeading, sign)) || !Ok(b.Entry)) return null;

        // state: cell, heading (0..3), and cells since the last turn (0 at the exit mouth, 1 just past a corner, 2 or more):
        // a turn needs 2, so corners stand at least two cells apart and each mouth's leg runs straight for two
        var dirs = Dirs4;
        int DirIndex(GridPos d) => Array.IndexOf(dirs, d);
        int State(GridPos p, int di, int run) => ((p.Y * w + p.X) * 4 + di) * 3 + run;
        var total = w * h * 12;
        var cost = new int[total];
        Array.Fill(cost, int.MaxValue);
        var parent = new int[total];
        var open = new PriorityQueue<int, (int f, long order)>();
        long order = 0;
        var s0 = State(a.Exit, DirIndex(a.ExitHeading), 0);
        cost[s0] = 0;
        parent[s0] = -1;
        open.Enqueue(s0, (a.Exit.ManhattanDistance(b.Entry), order++));
        var goal = -1;
        while (open.Count > 0)
        {
            open.TryDequeue(out var s, out var pri);
            var run = s % 3;
            var di = s / 3 % 4;
            var cellIndex = s / 12;
            var p = new GridPos(cellIndex % w, cellIndex / w);
            var g = cost[s];
            if (pri.f > g + p.ManhattanDistance(b.Entry)) continue; // stale
            var d = dirs[di];
            if (p == b.Entry)
            {
                if (d == b.EntryHeading && run == 2) { goal = s; break; }
                continue;
            }
            // straight on
            if (!shadow.Contains(p + d)) Try(p + d, di, Math.Min(run + 1, 2), g + 1 + Penalty(p + d, d));
            // or turn here
            if (run == 2)
                foreach (var nd in new[] { d.Perp, -d.Perp })
                    if (CornerOk(p, d, nd) && !shadow.Contains(p + nd)) Try(p + nd, DirIndex(nd), 1, g + 1 + RailTurnCost + Penalty(p + nd, nd));

            void Try(GridPos q, int ndi, int nrun, int ng)
            {
                var nd = dirs[ndi];
                if (!Ok(q) || !Ok(q + RailSide(nd, sign))) return;
                var ns = State(q, ndi, nrun);
                if (ng >= cost[ns]) return;
                cost[ns] = ng;
                parent[ns] = s;
                open.Enqueue(ns, (ng + q.ManhattanDistance(b.Entry), order++));
            }
        }
        if (goal < 0) return null;
        var cells = new List<GridPos>();
        for (var s = goal; s >= 0; s = parent[s]) { var ci = s / 12; cells.Add(new GridPos(ci % w, ci / w)); }
        cells.Reverse();
        if (cells.Distinct().Count() != cells.Count) return null;

        var levels = cells.Select(_ => a.Level).ToList();
        var ramps = cells.Select(_ => (RampInfo?)null).ToList();
        if (a.Level != b.Level && !PlaceRailRamp(cells, levels, ramps, a.Level, b.Level)) return null;
        var seg = new RailSegment(a, b, cells, levels, ramps);
        // the line so far with this stretch on the end must hold before the next is routed
        var local = new List<RailSegment>(prior) { seg };
        var line = LineCells(local, sign, out var lv, out var rp);
        if (line == null || !ValidateLine(layout, local, line.Value.party, line.Value.enemy, new List<GridPos>(), ahead)) return null;
        if (!LaneHeightsOk(line.Value.party, line.Value.enemy, lv, rp, sign)) return null;
        return seg;

        int Penalty(GridPos q, GridPos nd) => (Free(q - RailSide(nd, sign)) ? 0 : RailNoPocketCost) + (Free(q + RailSide(nd, sign) + RailSide(nd, sign)) ? 0 : RailNoPocketCost);

        // a turn at p from din to dout: on the outside of the turn the enemy lane takes the corner and the cell past it
        bool CornerOk(GridPos p, GridPos din, GridPos dout)
        {
            if (dout == RailSide(din, sign)) return true; // the enemy lane is on the inside: it just cuts the corner
            return Ok(p + RailSide(din, sign) + RailSide(dout, sign)) && Ok(p + RailSide(dout, sign));
        }
    }

    /// <summary>
    /// A two-cell ramp on the straight run nearest the middle of the stretch (four cells straight either way of its middle,
    /// so both lanes climb along its axis), cells before it at the level left, after it at the level arrived at.
    /// </summary>
    private static bool PlaceRailRamp(List<GridPos> cells, List<int> levels, List<RampInfo?> ramps, int fromLevel, int toLevel)
    {
        var best = -1;
        var mid = (cells.Count - 2) / 2.0;
        for (var k = 2; k + 3 < cells.Count; k++)
        {
            var d = cells[k + 1] - cells[k];
            var straight = true;
            for (var i = k - 2; i < k + 3 && straight; i++) straight = cells[i + 1] - cells[i] == d;
            if (!straight) continue;
            if (best < 0 || Math.Abs(k - mid) < Math.Abs(best - mid)) best = k;
        }
        if (best < 0) return false;
        var dir = cells[best + 1] - cells[best];
        var low = Math.Min(fromLevel, toLevel);
        var rise = toLevel > fromLevel ? dir : -dir;
        for (var i = 0; i < cells.Count; i++) levels[i] = i < best ? fromLevel : i <= best + 1 ? low : toLevel;
        var upFirst = toLevel > fromLevel;
        ramps[best] = new RampInfo(rise, low * 4 + (upFirst ? 0 : 2));
        ramps[best + 1] = new RampInfo(rise, low * 4 + (upFirst ? 2 : 0));
        return true;
    }

    /// <summary>
    /// The enemy lane for an ordered party lane: each cell's twin one step to the outer side; on the outside of a turn the
    /// corner cell joins them, on the inside the twins meet and the duplicate is dropped. <paramref name="source"/> gives the
    /// party index each enemy cell stands beside. Null for a U-turn.
    /// </summary>
    private static List<GridPos>? Twin(IReadOnlyList<GridPos> party, int sign, out List<int> source)
    {
        var enemy = new List<GridPos>();
        var from = new List<int>();
        source = from;
        if (party.Count < 2) return null;
        for (var i = 0; i < party.Count; i++)
        {
            var din = i > 0 ? party[i] - party[i - 1] : party[1] - party[0];
            var dout = i < party.Count - 1 ? party[i + 1] - party[i] : din;
            var p = party[i];
            if (din == dout) Add(p + RailSide(din, sign), i);
            else if (dout == RailSide(din, sign)) { } // inside of the turn: the twins before and after meet
            else if (dout == -RailSide(din, sign))
            {
                Add(p + RailSide(din, sign), i);
                Add(p + RailSide(din, sign) + RailSide(dout, sign), i);
                Add(p + RailSide(dout, sign), i);
            }
            else return null;
        }
        return enemy;

        void Add(GridPos c, int i)
        {
            if (enemy.Count > 0 && enemy[^1] == c) return;
            enemy.Add(c);
            from.Add(i);
        }
    }

    /// <summary>The whole line as cells: each room's platform, then the stretch to the next room, and so on; with each party cell's level and ramp.</summary>
    private static (List<GridPos> party, List<GridPos> enemy)? LineCells(List<RailSegment> segs, int sign, out List<int> levels, out List<RampInfo?> ramps)
    {
        var party = new List<GridPos>();
        var lv = new List<int>();
        var rp = new List<RampInfo?>();
        levels = lv;
        ramps = rp;
        for (var i = 0; i < segs.Count; i++)
        {
            if (i == 0) Platform(segs[0].From);
            party.AddRange(segs[i].Cells);
            lv.AddRange(segs[i].Levels);
            rp.AddRange(segs[i].Ramps);
            Platform(segs[i].To);
        }
        var enemy = Twin(party, sign, out _);
        return enemy == null ? null : (party, enemy);

        void Platform(RailRoom r)
        {
            party.AddRange(r.Platform);
            foreach (var _ in r.Platform) { lv.Add(r.Level); rp.Add(null); }
        }
    }

    /// <summary>The level and ramp of every enemy cell: those of the party cell it stands beside.</summary>
    private static (List<int> levels, List<RampInfo?> ramps) EnemyHeights(List<GridPos> party, List<int> levels, List<RampInfo?> ramps, int sign)
    {
        Twin(party, sign, out var source);
        return (source.Select(i => levels[i]).ToList(), source.Select(i => ramps[i]).ToList());
    }

    private static float RailHeight(int level, RampInfo? ramp) => ramp?.MidHeight ?? level * 4f;

    /// <summary>Both lanes walkable end to end as <see cref="FloorLayout.CanStep"/> judges it once carved.</summary>
    private static bool LaneHeightsOk(List<GridPos> party, List<GridPos> enemy, List<int> levels, List<RampInfo?> ramps, int sign)
    {
        var (el, er) = EnemyHeights(party, levels, ramps, sign);
        return el.Count == enemy.Count && Walk(party, levels, ramps) && Walk(enemy, el, er);

        static bool Walk(List<GridPos> lane, List<int> lv, List<RampInfo?> rp)
        {
            for (var i = 1; i < lane.Count; i++)
            {
                var d = lane[i] - lane[i - 1];
                if (rp[i - 1] is { } ra && !d.IsParallelTo(ra.Dir)) return false;
                if (rp[i] is { } rb && !d.IsParallelTo(rb.Dir)) return false;
                if (Math.Abs(RailHeight(lv[i - 1], rp[i - 1]) - RailHeight(lv[i], rp[i])) > 2.01f) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The line holds: both lanes ordered, 4-connected, apart and side by side; every carved cell solid rock now and clear
    /// of <paramref name="foreign"/>; and nothing beside a lane or pocket outside the line's rooms but the line or rock.
    /// </summary>
    private static bool ValidateLine(FloorLayout layout, List<RailSegment> segs, List<GridPos> party, List<GridPos> enemy, List<GridPos> pockets, HashSet<GridPos>? foreign)
    {
        var rooms = new List<RailRoom> { segs[0].From };
        rooms.AddRange(segs.Select(s => s.To));
        var platform = rooms.SelectMany(r => r.Platform).ToHashSet();
        bool InLineRoom(GridPos p) => rooms.Any(r => r.Room.Contains(p));
        var partySet = party.ToHashSet();
        var enemySet = enemy.ToHashSet();
        if (partySet.Count != party.Count || enemySet.Count != enemy.Count || partySet.Overlaps(enemySet)) return false;
        for (var i = 1; i < party.Count; i++) if (party[i - 1].ManhattanDistance(party[i]) != 1) return false;
        for (var i = 1; i < enemy.Count; i++) if (enemy[i - 1].ManhattanDistance(enemy[i]) != 1) return false;
        if (party.Any(p => !Around8(p).Any(enemySet.Contains)) || enemy.Any(e => !Around8(e).Any(partySet.Contains))) return false;
        var all = new HashSet<GridPos>(partySet);
        all.UnionWith(enemySet);
        all.UnionWith(pockets);
        if (all.Count != party.Count + enemy.Count + pockets.Count) return false;
        foreach (var c in all)
        {
            if (platform.Contains(c)) continue;
            if (!InRailRange(layout, c) || !Rock(layout, c) || layout.Rooms.Any(r => r.Contains(c)) || foreign?.Contains(c) == true) return false;
        }
        foreach (var c in all)
            foreach (var q in Around8(c))
                if (!InLineRoom(q) && !all.Contains(q) && (!Rock(layout, q) || foreign?.Contains(q) == true)) return false;
        return true;
    }

    /// <summary>
    /// Refuge pockets: one-cell niches on each lane's free side (the party lane's toward the ring, the enemy lane's outward),
    /// beside a straight lane cell outside the stops, in rock with nothing round them but rock, their own lane and its other
    /// pockets. First a cover, so every lane cell outside a stop has one within <see cref="RailRules.PocketEvery"/> cells
    /// along the lane (for the first cell without, the farthest cell that still reaches it); then one wherever none is within
    /// a cell, so they come about every PocketEvery cells. Null when a cell cannot be covered.
    /// </summary>
    private static List<GridPos>? PlaceRailPockets(FloorLayout layout, List<RailSegment> segs, List<GridPos> party, List<GridPos> enemy, List<RampInfo?> ramps, int sign)
    {
        var rooms = new List<RailRoom> { segs[0].From };
        rooms.AddRange(segs.Select(s => s.To));
        bool InStop(GridPos p) => rooms.Any(r => r.Room.Contains(p));
        var taken = new HashSet<GridPos>(party);
        taken.UnionWith(enemy);
        var pockets = new List<GridPos>();
        Twin(party, sign, out var source);
        var enemyRamps = source.Select(i => ramps[i]).ToList();
        // the free side: the party lane's toward the ring, the enemy lane's outward
        if (!Fill(party, ramps, -1) || !Fill(enemy, enemyRamps, 1)) return null;
        return pockets;

        bool Fill(List<GridPos> lane, List<RampInfo?> rp, int freeHand)
        {
            var own = lane.ToHashSet();
            var mine = new HashSet<GridPos>(); // this lane's pockets may touch (round a bend); the other lane's may not
            var has = new bool[lane.Count];
            // first a cover: for the first cell with no pocket in reach, the farthest cell that can take one and still reach it
            for (var i = 0; i < lane.Count; i++)
            {
                if (InStop(lane[i]) || Covered(i)) continue;
                var placed = false;
                for (var j = i + RailRules.PocketEvery; j >= i - RailRules.PocketEvery && !placed; j--) placed = TryAt(j);
                if (!placed) return false;
            }
            // then as many more as fit, so a refuge is about every PocketEvery cells
            for (var i = 0; i < lane.Count; i++)
                if (!InStop(lane[i]) && !Near(i, 1)) TryAt(i);
            return true;

            bool Near(int i, int reach)
            {
                for (var j = Math.Max(0, i - reach); j <= Math.Min(lane.Count - 1, i + reach); j++) if (has[j]) return true;
                return false;
            }

            bool Covered(int i)
            {
                for (var j = Math.Max(0, i - RailRules.PocketEvery); j <= Math.Min(lane.Count - 1, i + RailRules.PocketEvery); j++) if (has[j]) return true;
                return false;
            }

            bool TryAt(int i)
            {
                if (i <= 0 || i >= lane.Count - 1 || has[i] || InStop(lane[i]) || rp[i] != null) return false;
                var d = lane[i] - lane[i - 1];
                if (lane[i + 1] - lane[i] != d) return false; // straight cells only: a corner has no one free side
                var q = lane[i] + (freeHand > 0 ? RailSide(d, sign) : -RailSide(d, sign));
                if (!InRailRange(layout, q) || !Rock(layout, q) || taken.Contains(q) || layout.Rooms.Any(r => r.Contains(q))) return false;
                foreach (var n in Around8(q))
                    if (!own.Contains(n) && !mine.Contains(n) && (!Rock(layout, n) || taken.Contains(n))) return false;
                pockets.Add(q);
                mine.Add(q);
                taken.Add(q);
                has[i] = true;
                return true;
            }
        }
    }

    // ---- carving -----------------------------------------------------------------------------------

    private static void CommitRails(FloorLayout layout, List<RailSegment> segs, int sign, List<GridPos> pockets)
    {
        var line = LineCells(segs, sign, out var levels, out var ramps)!.Value;
        var (enemyLevels, enemyRamps) = EnemyHeights(line.party, levels, ramps, sign);
        void Carve(GridPos c, int level, RampInfo? ramp)
        {
            layout[c] = ramp != null ? Cell.Ramp : Cell.Floor;
            layout.SetLevel(c, level);
            if (ramp != null) layout.Ramps[c] = ramp;
        }
        for (var i = 0; i < line.party.Count; i++) Carve(line.party[i], levels[i], ramps[i]);
        for (var i = 0; i < line.enemy.Count; i++) Carve(line.enemy[i], enemyLevels[i], enemyRamps[i]);
        var partyIndex = new Dictionary<GridPos, int>();
        for (var i = 0; i < line.party.Count; i++) partyIndex[line.party[i]] = i;
        var enemyIndex = new Dictionary<GridPos, int>();
        for (var i = 0; i < line.enemy.Count; i++) enemyIndex[line.enemy[i]] = i;
        foreach (var p in pockets)
        {
            // level with the lane cell it opens off
            var level = 0;
            foreach (var n in p.Neighbours4())
            {
                if (partyIndex.TryGetValue(n, out var pi)) { level = levels[pi]; break; }
                if (enemyIndex.TryGetValue(n, out var ei)) { level = enemyLevels[ei]; break; }
            }
            Carve(p, level, null);
        }
        var rooms = new List<RailRoom> { segs[0].From };
        rooms.AddRange(segs.Select(s => s.To));
        layout.Rail = new RailLine(line.party, line.enemy, rooms.Select(r => new RailStop(r.Index, r.Platform)).ToList(), pockets);
        for (var i = 1; i < rooms.Count; i++) layout.Connections.Add((rooms[i - 1].Index, rooms[i].Index));
        RaiseWalls(layout);
    }
}
