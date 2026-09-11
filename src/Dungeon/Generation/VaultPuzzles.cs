namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Builds and proves the lever-and-gate puzzle of a Shifting Vault floor. Engine-free and deterministic. A puzzle is
/// only accepted when, from the start with the gates as they first stand, every reachable situation (which levers
/// stand pulled, where the party can walk) can still reach one where the gates match the target and the party can
/// walk up to the vault door: no dead ends, no sealed-in party, ever.
/// </summary>
public static class VaultPuzzles
{
    public const int MinLevers = 3, MaxLevers = 5, MinGates = 3, MaxGates = 5;
    public const int Samples = 200;

    /// <summary>Generate a puzzle for the floor whose stairs sit behind <paramref name="vaultDoor"/>; null only when the floor has nowhere to put a gate or a lever.</summary>
    public static VaultPuzzle? Generate(FloorLayout layout, Random rng, GridPos vaultDoor, Room vaultRoom)
    {
        var gateCandidates = GateCandidates(layout, vaultDoor);
        var leverCandidates = LeverCandidates(layout, vaultRoom);
        if (leverCandidates.Count == 0 || gateCandidates.Count == 0) return null;
        for (var gates = Math.Min(MaxGates, gateCandidates.Count); gates >= 1; gates--)
        {
            var levers = Math.Clamp(gates, Math.Min(MinLevers, leverCandidates.Count), Math.Min(MaxLevers, leverCandidates.Count));
            var samples = gates >= MinGates ? Samples : Samples / 2;
            for (var n = 0; n < samples; n++)
            {
                var puzzle = Sample(rng, gateCandidates, leverCandidates, gates, levers);
                if (puzzle != null && Verify(layout, puzzle, vaultDoor, vaultRoom)) return puzzle;
            }
        }
        return null;
    }

    /// <summary>Corridor cells a gate can stand in: straight runs outside any room, one cell wide, not a ramp, not touching a door, stairs or anything reserved.</summary>
    public static List<GridPos> GateCandidates(FloorLayout layout, GridPos vaultDoor)
    {
        var list = new List<GridPos>();
        for (var y = 1; y < layout.Height - 1; y++)
            for (var x = 1; x < layout.Width - 1; x++)
            {
                var p = new GridPos(x, y);
                if (layout[p] != Cell.Floor || layout.RoomAt(p) != null || layout.IsReserved(p) || layout.IsUnder(p) || layout.IsWater(p)) continue;
                if (layout.Ramps.ContainsKey(p) || p == layout.UpStairs || p == layout.DownStairs) continue;
                if (layout.EnemySpawns.Contains(p) || layout.TrapSpawns.Contains(p) || layout.BreakableSpawns.Contains(p) || layout.KeySpawns.Contains(p) || layout.ChestSpawns.Contains(p)) continue;
                var n = layout.IsWalkable(new GridPos(x, y - 1)) || layout[new GridPos(x, y - 1)] == Cell.Secret;
                var s = layout.IsWalkable(new GridPos(x, y + 1)) || layout[new GridPos(x, y + 1)] == Cell.Secret;
                var w = layout.IsWalkable(new GridPos(x - 1, y)) || layout[new GridPos(x - 1, y)] == Cell.Secret;
                var e = layout.IsWalkable(new GridPos(x + 1, y)) || layout[new GridPos(x + 1, y)] == Cell.Secret;
                var straight = (n && s && !w && !e) || (w && e && !n && !s);
                if (!straight) continue;
                // nothing special right beside it: a door, a ramp, the stairs, the vault's mouth
                var near = false;
                for (var oy = -1; oy <= 1 && !near; oy++)
                    for (var ox = -1; ox <= 1 && !near; ox++)
                    {
                        var q = new GridPos(x + ox, y + oy);
                        if (q == p) continue;
                        if (layout[q] == Cell.Door || layout.Ramps.ContainsKey(q) || q == layout.UpStairs || q == layout.DownStairs || q == vaultDoor || layout.IsNestMouth(q)) near = true;
                        if (layout.RoomAt(q) != null) near = true; // not the first cell out of a room either: two gates on one doorstep is a wall
                    }
                if (near) continue;
                list.Add(p);
            }
        return list;
    }

    /// <summary>Room cells a lever can stand on: plain floor in an ordinary room (not the vault, not a treasure vault), off the stairs and everything else.</summary>
    public static List<(int Room, GridPos Cell)> LeverCandidates(FloorLayout layout, Room vaultRoom)
    {
        var list = new List<(int, GridPos)>();
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            var room = layout.Rooms[i];
            if (room == vaultRoom || room == layout.BossArena || layout.Vaults.Any(v => v.Room == room)) continue;
            var level = layout.LevelOf(room.Center);
            foreach (var p in room.Cells())
            {
                if (layout[p] != Cell.Floor || layout.LevelOf(p) != level || layout.IsUnder(p) || layout.IsReserved(p) || layout.IsWater(p)) continue;
                if (p == layout.UpStairs || p == layout.DownStairs || layout.ChestSpawns.Contains(p)) continue;
                if (layout.EnemySpawns.Contains(p) || layout.TrapSpawns.Contains(p) || layout.BreakableSpawns.Contains(p) || layout.KeySpawns.Contains(p)) continue;
                if (layout.Ledges.Any(l => l.Cells.Contains(p) || l.RampBottom == p || l.RampTop == p)) continue;
                if (layout.Vaults.Any(v => v.Key == p || v.Lever == p || v.Door == p)) continue;
                if (layout.Structures.Any(s => s.Cells.Contains(p) || s.Openings.Contains(p) || s.Anchor == p)) continue;
                // not on the doorstep of the room, so the levers never block the way through
                if (p.X == room.X || p.X == room.Right || p.Y == room.Y || p.Y == room.Bottom) continue;
                list.Add((i, p));
            }
        }
        return list;
    }

    private static VaultPuzzle? Sample(Random rng, List<GridPos> gateCandidates, List<(int Room, GridPos Cell)> leverCandidates, int gates, int levers)
    {
        // gates: distinct cells at least three apart, so two never crowd one corridor
        var gateCells = new List<GridPos>();
        var pool = gateCandidates.OrderBy(_ => rng.Next()).ToList();
        foreach (var c in pool)
        {
            if (gateCells.Any(g => Math.Abs(g.X - c.X) + Math.Abs(g.Y - c.Y) < 3)) continue;
            gateCells.Add(c);
            if (gateCells.Count == gates) break;
        }
        if (gateCells.Count < gates) return null;
        // levers: distinct rooms
        var leverCells = new List<GridPos>();
        var rooms = new HashSet<int>();
        foreach (var (room, cell) in leverCandidates.OrderBy(_ => rng.Next()))
        {
            if (!rooms.Add(room)) continue;
            leverCells.Add(cell);
            if (leverCells.Count == levers) break;
        }
        if (leverCells.Count < levers) return null;
        // the wiring: every lever moves something, every gate answers to something
        var toggles = new int[levers];
        var all = (1 << gates) - 1;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var covered = 0;
            for (var i = 0; i < levers; i++)
            {
                toggles[i] = rng.Next(1, all + 1);
                if (gates >= 3 && rng.Next(2) == 0) { var mask = rng.Next(1, all + 1); if ((toggles[i] & mask) != 0) toggles[i] &= mask; } // some levers move fewer gates
                covered |= toggles[i];
            }
            if (covered == all) break;
            if (attempt == 19) return null;
        }
        var initial = rng.Next(0, all + 1);
        // the secret setting: some non-empty pull set that actually moves the gates
        var target = initial;
        for (var attempt = 0; attempt < 20 && target == initial; attempt++)
        {
            var solution = rng.Next(1, 1 << levers);
            target = initial;
            for (var i = 0; i < levers; i++) if ((solution & (1 << i)) != 0) target ^= toggles[i];
        }
        if (target == initial) return null;
        return new VaultPuzzle(leverCells, gateCells, toggles, initial, target);
    }

    /// <summary>
    /// The proof. States are (which levers stand pulled, which cell the party is at); from a state the party may walk
    /// anywhere the open gates allow and pull any lever it can reach. Every state reachable from the start must be able
    /// to reach a state where the gates match the target and the vault's doorstep is in reach.
    /// </summary>
    public static bool Verify(FloorLayout layout, VaultPuzzle puzzle, GridPos vaultDoor, Room vaultRoom)
    {
        var gateIndex = new Dictionary<GridPos, int>();
        for (var g = 0; g < puzzle.GateCount; g++) gateIndex[puzzle.Gates[g]] = g;
        var doorstep = Doorstep(layout, vaultDoor, vaultRoom);
        if (doorstep == null) return false;
        // components per gate setting (not per lever parity: many parities share a setting), reused for every cell inside them
        var components = new Dictionary<int, List<HashSet<GridPos>>>();
        HashSet<GridPos> Reach(int parity, GridPos from)
        {
            var state = puzzle.StateFor(parity);
            if (!components.TryGetValue(state, out var list)) components[state] = list = new List<HashSet<GridPos>>();
            foreach (var c in list) if (c.Contains(from)) return c;
            var result = layout.Reachable(from, p => p == vaultDoor || (gateIndex.TryGetValue(p, out var g) && !puzzle.IsOpen(state, g)), secretsOpen: true);
            list.Add(result);
            return result;
        }
        var start = (Parity: 0, Cell: layout.UpStairs);
        var seen = new HashSet<(int, GridPos)> { start };
        var queue = new Queue<(int, GridPos)>();
        queue.Enqueue(start);
        var edges = new Dictionary<(int, GridPos), List<(int, GridPos)>>();
        var goals = new HashSet<(int, GridPos)>();
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            var reach = Reach(s.Item1, s.Item2);
            var next = new List<(int, GridPos)>();
            if (puzzle.StateFor(s.Item1) == puzzle.Target && reach.Contains(doorstep.Value)) goals.Add(s);
            for (var i = 0; i < puzzle.LeverCount; i++)
            {
                if (!reach.Contains(puzzle.Levers[i])) continue;
                var t = (s.Item1 ^ (1 << i), puzzle.Levers[i]);
                next.Add(t);
                if (seen.Add(t)) queue.Enqueue(t);
            }
            edges[s] = next;
        }
        if (goals.Count == 0) return false;
        // every reachable state must reach a goal: walk the edges backwards from the goals
        var reverse = new Dictionary<(int, GridPos), List<(int, GridPos)>>();
        foreach (var (from, tos) in edges)
            foreach (var to in tos)
            {
                if (!reverse.TryGetValue(to, out var list)) reverse[to] = list = new List<(int, GridPos)>();
                list.Add(from);
            }
        var canFinish = new HashSet<(int, GridPos)>(goals);
        var back = new Queue<(int, GridPos)>(goals);
        while (back.Count > 0)
        {
            var s = back.Dequeue();
            if (!reverse.TryGetValue(s, out var froms)) continue;
            foreach (var f in froms) if (canFinish.Add(f)) back.Enqueue(f);
        }
        return seen.All(canFinish.Contains);
    }

    /// <summary>The walkable cell just outside the vault door.</summary>
    public static GridPos? Doorstep(FloorLayout layout, GridPos vaultDoor, Room vaultRoom)
    {
        foreach (var d in new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) })
        {
            var q = vaultDoor + d;
            if (layout.IsWalkable(q) && !vaultRoom.Contains(q)) return q;
        }
        return null;
    }

    // ---- words ------------------------------------------------------------------------------

    public static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

    public static string GateName(int gate) => $"Gate {Numerals[Math.Clamp(gate, 0, Numerals.Length - 1)]}";

    /// <summary>Which way a gate lies from the middle of the floor, for the plaques.</summary>
    public static string Direction(FloorLayout layout, GridPos p)
    {
        var dx = p.X - layout.Width / 2;
        var dy = p.Y - layout.Height / 2;
        var ns = dy < 0 ? "north" : "south";
        var ew = dx < 0 ? "west" : "east";
        if (Math.Abs(dx) < layout.Width / 8) return ns;
        if (Math.Abs(dy) < layout.Height / 8) return ew;
        return $"{ns}-{ew}";
    }

    /// <summary>What the plaque beside lever i says.</summary>
    public static string LeverClue(VaultPuzzle puzzle, int lever)
    {
        var gates = Enumerable.Range(0, puzzle.GateCount).Where(g => (puzzle.Toggles[lever] & (1 << g)) != 0).Select(GateName).ToList();
        return $"This lever turns {Join(gates)}: what stands open shuts, what stands shut opens.";
    }

    /// <summary>What the vault's own plaque says: the setting that opens it.</summary>
    public static string VaultClue(VaultPuzzle puzzle, FloorLayout layout)
    {
        var open = Enumerable.Range(0, puzzle.GateCount).Where(g => puzzle.IsOpen(puzzle.Target, g)).Select(g => $"{GateName(g)} ({Direction(layout, puzzle.Gates[g])})").ToList();
        var shut = Enumerable.Range(0, puzzle.GateCount).Where(g => !puzzle.IsOpen(puzzle.Target, g)).Select(g => $"{GateName(g)} ({Direction(layout, puzzle.Gates[g])})").ToList();
        var parts = new List<string>();
        if (open.Count > 0) parts.Add($"{Join(open)} {(open.Count == 1 ? "stands" : "stand")} open");
        if (shut.Count > 0) parts.Add($"{Join(shut)} {(shut.Count == 1 ? "stands" : "stand")} shut");
        return $"The vault yields when {string.Join(" and ", parts)}. The levers know the gates; the gates know the way down.";
    }

    private static string Join(List<string> items) => items.Count switch
    {
        0 => "nothing",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
