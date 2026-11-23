namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Plan 15's two crevasse passes, both behind knobs that default off and both without rng: the pit on a ride's party lane
/// (<see cref="FloorSpec.PitRide"/>, <see cref="RailLine.Pit"/>) and the exit island (<see cref="FloorSpec.ExitIsland"/>,
/// <see cref="RailLine.Island"/>), whose halls are sealed and whose one way in is a rail bridge over lava. They run inside
/// <see cref="CarveChasms"/> after the planks are picked and before they are cut.
/// </summary>
public static partial class FloorGenerator
{
    /// <summary>
    /// The pit on 1-based ride <paramref name="ride"/> (<see cref="FloorSpec.PitRide"/>), and why: "refused" on a ride the line does
    /// not have, the broken bridge's ride (<paramref name="breakRide"/>), the split's ride (<paramref name="splitRide"/>) or the
    /// cavern's ride; "none" when the ride has no fitting run; else "ok". The run is the first (lowest start index) of at least
    /// <see cref="RailRules.PitRunCells"/> consecutive straight party-lane cells lying whole in the ride's window
    /// (<see cref="RideWindow"/> over <see cref="RailRules.PitFrom"/> to <see cref="RailRules.PitTo"/>), each with its enemy twin a
    /// straight enemy cell taken by nothing, both lanes at one level and no ramp on it or within a cell of it. The pit is the run's
    /// cell <see cref="RailRules.PitLeadCells"/> from its start: it becomes <see cref="Cell.Chasm"/>, and so do the rock or pocket
    /// cells inward of it (t = -1, then -2), at the lane's level. The enemy twin stays solid lane with no deck. The pockets cut
    /// away are added to <paramref name="dropped"/>.
    /// </summary>
    internal static (RailPit? Pit, string Why) CarvePit(FloorLayout layout, RailLine rail, int ride, int breakRide, int splitRide, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)
    {
        var k = ride - 1;
        if (k > rail.Stops.Count - 2 || ride == breakRide || ride == splitRide) return (null, "refused"); // no such ride, the break's, the split's
        var w = RideWindow(rail, k, RailRules.PitFrom, RailRules.PitTo);
        if (rail.Cavern is { } cavern && cavern.From > w.After && cavern.From < w.Before) return (null, "refused"); // the cavern's ride
        var enemy = rail.Enemy.ToHashSet();
        var taken = TakenCells(layout);
        int? start = null;
        for (int i = w.From, run = 0; i <= w.To && start == null; i++)
        {
            run = PitRunCell(i) ? run + 1 : 0;
            if (run >= RailRules.PitRunCells) start = i - run + 1; // the first run long enough
        }
        if (start == null) return (null, "none");
        var pit = start.Value + RailRules.PitLeadCells;
        var cell = rail.Party[pit];
        var level = layout.LevelOf(cell);
        var inward = -RailSide(rail.Party[pit + 1] - cell, sign);
        var cut = new List<GridPos> { cell };
        layout[cell] = Cell.Chasm;
        for (var t = 1; t <= 2; t++)
        {
            var q = cell + Times(inward, t);
            var pocket = pockets.Contains(q);
            if ((!pocket && !Rock(layout, q)) || layout.RoomAt(q) != null || taken.Contains(q)) break; // only rock or a pocket, next to the cut
            layout[q] = Cell.Chasm;
            layout.SetLevel(q, level);
            if (pocket) dropped.Add(q);
            cut.Add(q);
        }
        return (new RailPit(ride, pit, cut), "ok");

        bool PitRunCell(int i)
        {
            if (i <= 0 || i >= rail.Party.Count - 1) return false;
            var p = rail.Party[i];
            if (p - rail.Party[i - 1] != rail.Party[i + 1] - p || taken.Contains(p)) return false;
            var twin = SlagTwin(rail, i, sign);
            if (!enemy.Contains(twin) || !StraightEnemy(enemy, twin) || taken.Contains(twin) || layout.LevelOf(twin) != layout.LevelOf(p)) return false;
            return !RampNear(layout, p);
        }
    }

    /// <summary>
    /// The exit island (<see cref="FloorSpec.ExitIsland"/>), and why: "ok", "none" when the last ride has no bridge run, or
    /// "refused (…)" with the reason, nothing cut. Every cell it changes is recorded, and all are restored on a refusal.
    /// <list type="bullet">
    /// <item><b>The bridge</b>: the last run of at least <see cref="RailRules.BridgeMinCells"/> straight party-lane cells on the last
    /// ride, ending <see cref="RailRules.BridgeIslandCells"/> cells before the exit platform's first, with no platform, ramp or taken
    /// cell on it or its enemy twins. Its cells become <see cref="Cell.Chasm"/> with their track kept, and so does each twin that is a
    /// straight enemy cell (a corner stays solid).</item>
    /// <item><b>The rooms</b>: the exit room R, and each other room (not the start's) with no cell reachable from the up stairs with
    /// the rails blocked and R's cells walled, but one with R open.</item>
    /// <item><b>The seals</b>: from each mouth (a walkable hall cell, outside every room and off the line, beside an island room)
    /// the walk follows the hall to the cell before a fork (3 or more walkable 4-neighbours, kept open) or a room. A walk ending at
    /// an island room seals nothing; any other walk's mouth becomes a trench (chasm at the room's level) and the rest a cave-in
    /// (<see cref="Cell.Wall"/>). A trap on a sealed cell is removed and logged (<see cref="FloorLayout.GenLog"/>); any other taken
    /// or reserved cell on one refuses.</item>
    /// <item><b>The moat</b>: each party bridge cell's inward cells at t = -1..-<see cref="RailRules.MoatDepth"/> and each enemy
    /// bridge twin's outward cells at t = +2..+1+<see cref="RailRules.MoatDepth"/>, where rock or a pocket outside every room and
    /// not a lane, platform, ramp, ramp neighbour or taken cell, cut at the lane's level (exempt from
    /// <see cref="RailRules.StripPlatformGap"/>).</item>
    /// <item><b>The proof</b>: the island's cells are those walkable from the down stairs once cut (the rails open, the secrets
    /// shut). It refuses when the up stairs reach one ("leaks at"), when a key, a vault lever, a prisoner or a jail cell lies in
    /// it, or when the exit room is the start's.</item>
    /// </list>
    /// The pockets cut away are added to <paramref name="dropped"/> (only when it is cut).
    /// </summary>
    internal static (RailIsland? Island, string Why) CarveIsland(FloorLayout layout, RailLine rail, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)
    {
        var startRoom = rail.Stops[0].Room;
        var exitRoom = rail.Stops[^1].Room;
        if (startRoom == exitRoom) return (null, "refused (the exit room is the start's)");
        var index = new Dictionary<GridPos, int>();
        for (var i = 0; i < rail.Party.Count; i++) index[rail.Party[i]] = i;
        var enemy = rail.Enemy.ToHashSet();
        var platform = rail.Stops.SelectMany(s => s.Platform).ToHashSet();
        var taken = TakenCells(layout);

        // the bridge: the last straight run before the exit platform, clipped to leave the island its lane cells
        var k = rail.Stops.Count - 2;
        var after = index[rail.Stops[k].Platform[^1]];
        var limit = index[rail.Stops[k + 1].Platform[0]] - RailRules.BridgeIslandCells - 1;
        RailSpan? bridge = null;
        int? from = null;
        for (var i = after + 1; i <= limit + 1; i++)
        {
            if (i <= limit && BridgeCell(i)) { from ??= i; continue; }
            if (from is { } f && i - f >= RailRules.BridgeMinCells) bridge = new RailSpan(f, i - 1);
            from = null;
        }
        if (bridge == null) return (null, "none");

        // the rooms: the exit room, and every room only it leads to with the rails blocked
        bool Blocked(GridPos p) => (layout.IsRail(p) || pockets.Contains(p)) && !(layout.IsPartyRail(p) && platform.Contains(p));
        var exit = layout.Rooms[exitRoom];
        var shut = layout.Reachable(layout.UpStairs, p => Blocked(p) || exit.Contains(p));
        var open = layout.Reachable(layout.UpStairs, Blocked);
        var rooms = new List<int>();
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            if (i == exitRoom) { rooms.Add(i); continue; }
            if (i == startRoom) continue;
            var cells = layout.Rooms[i].Cells().ToList();
            if (!cells.Any(shut.Contains) && cells.Any(open.Contains)) rooms.Add(i);
        }

        // the seals: each hall out of an island room, from its mouth to the cell before its fork or room
        var trenches = new Dictionary<GridPos, int>(); // the trench, and the level of the room it opens from
        var caveIns = new HashSet<GridPos>();
        bool Hall(GridPos p) => layout.IsWalkable(p) && layout.RoomAt(p) == null && !layout.IsRail(p) && !pockets.Contains(p);
        var mouths = new List<(GridPos Cell, int Room)>();
        foreach (var r in rooms)
            foreach (var c in layout.Rooms[r].Cells())
                foreach (var n in c.Neighbours4())
                    if (Hall(n) && mouths.All(m => m.Cell != n)) mouths.Add((n, r));
        foreach (var (mouth, r) in mouths.OrderBy(m => m.Cell.Y).ThenBy(m => m.Cell.X))
        {
            var seen = new HashSet<GridPos>(layout.Rooms[r].Cells()) { mouth };
            var path = new List<GridPos> { mouth };
            var toIsland = false;
            for (var cur = mouth; ;)
            {
                var steps = cur.Neighbours4().Where(n => layout.IsWalkable(n) && !seen.Contains(n)).ToList();
                if (steps.Select(layout.RoomAt).FirstOrDefault(room => room != null) is { } reached)
                {
                    toIsland = rooms.Contains(layout.Rooms.IndexOf(reached)); // the walk stops before a room
                    break;
                }
                var next = steps.Where(Hall).Select(n => (GridPos?)n).FirstOrDefault();
                if (next is not { } step || step.Neighbours4().Count(layout.IsWalkable) >= 3) break; // a dead end, or the fork kept open
                seen.Add(step);
                path.Add(step);
                cur = step;
            }
            if (toIsland) continue; // a hall between two island rooms
            trenches[mouth] = layout.LevelOf(layout.Rooms[r].Center);
            caveIns.UnionWith(path.Skip(1));
        }
        caveIns.ExceptWith(trenches.Keys);
        var takenButTraps = TakenCells(layout, traps: false);
        foreach (var c in ByYX(trenches.Keys.Concat(caveIns)))
        {
            if (takenButTraps.Contains(c)) return (null, $"refused (a taken cell at {c} on a sealed hall)");
            if (layout.IsReserved(c)) return (null, $"refused (a reserved cell at {c} on a sealed hall)");
        }

        // the cut, every change recorded so a refusal restores the floor as it was
        var before = new Dictionary<GridPos, (Cell Cell, int Level)>();
        var rampsRemoved = new Dictionary<GridPos, RampInfo>();
        var trapsRemoved = new List<(int Index, GridPos Cell)>();
        void Set(GridPos p, Cell cell, int level)
        {
            before.TryAdd(p, (layout[p], layout.LevelOf(p)));
            layout[p] = cell;
            layout.SetLevel(p, level);
        }
        for (var i = layout.TrapSpawns.Count - 1; i >= 0; i--)
            if (trenches.ContainsKey(layout.TrapSpawns[i]) || caveIns.Contains(layout.TrapSpawns[i]))
            {
                trapsRemoved.Add((i, layout.TrapSpawns[i]));
                layout.TrapSpawns.RemoveAt(i);
            }
        foreach (var c in trenches.Keys.Concat(caveIns))
            if (layout.Ramps.Remove(c, out var ramp)) rampsRemoved[c] = ramp;
        foreach (var (c, level) in trenches) Set(c, Cell.Chasm, level);
        foreach (var c in caveIns) Set(c, Cell.Wall, layout.LevelOf(c));
        var enemyBridge = new List<GridPos>();
        var moat = new List<GridPos>();
        var moatDropped = new List<GridPos>();
        for (var i = bridge.From; i <= bridge.To; i++)
        {
            var p = rail.Party[i];
            var level = layout.LevelOf(p);
            var side = RailSide(rail.Party[i + 1] - p, sign);
            Set(p, Cell.Chasm, level); // the track is kept: the cell stays the party lane's
            var twin = p + side;
            var straight = enemy.Contains(twin) && StraightEnemy(enemy, twin);
            if (straight)
            {
                Set(twin, Cell.Chasm, layout.LevelOf(twin));
                enemyBridge.Add(twin);
            }
            for (var t = 1; t <= RailRules.MoatDepth; t++)
            {
                Moat(p - Times(side, t), level);
                if (straight) Moat(p + Times(side, 1 + t), level);
            }
        }
        void Moat(GridPos q, int level)
        {
            var pocket = pockets.Contains(q);
            if ((!pocket && !Rock(layout, q)) || layout.RoomAt(q) != null || layout.IsRail(q) || platform.Contains(q) || taken.Contains(q) || RampNear(layout, q)) return;
            Set(q, Cell.Chasm, level);
            moat.Add(q);
            if (pocket) moatDropped.Add(q);
        }

        // the proof: the island is what the down stairs reach, and the up stairs reach none of it
        var island = layout.Reachable(layout.DownStairs);
        var reach = layout.Reachable(layout.UpStairs);
        if (ByYX(island).FirstOrDefault(reach.Contains, Nowhere) is var leak && leak != Nowhere) return Refuse($"leaks at {leak}");
        var keys = layout.KeySpawns.Concat(layout.Vaults.Where(v => v.Key != null).Select(v => v.Key!.Value)).Concat(layout.Objective?.ExtraKeyCells ?? new());
        var inside = new (string What, IEnumerable<GridPos> Cells)[]
        {
            ("a key", keys),
            ("a lever", layout.Vaults.Where(v => v.Lever != null).Select(v => v.Lever!.Value)),
            ("a prisoner", layout.Objective?.PrisonerCells ?? new()),
            ("a jail cell", layout.Objective?.JailRoom?.Cells() ?? Enumerable.Empty<GridPos>()),
        };
        foreach (var (what, cells) in inside)
            if (ByYX(cells.Where(island.Contains)).FirstOrDefault(Nowhere) is var c && c != Nowhere) return Refuse($"{what} at {c} inside");

        dropped.UnionWith(moatDropped);
        foreach (var (_, c) in trapsRemoved.OrderBy(t => t.Cell.Y).ThenBy(t => t.Cell.X)) layout.GenLog.Add($"island: trap at {c} sealed");
        return (new RailIsland(rooms, bridge, new RailSpan(bridge.From - RailRules.ParkedCells, bridge.From - 1), ByYX(enemyBridge).ToList(),
            ByYX(trenches.Keys).ToList(), ByYX(caveIns).ToList(), ByYX(moat.Distinct()).ToList(), island), "ok");

        (RailIsland?, string) Refuse(string why)
        {
            foreach (var (p, (cell, level)) in before) { layout[p] = cell; layout.SetLevel(p, level); }
            foreach (var (p, ramp) in rampsRemoved) layout.Ramps[p] = ramp;
            foreach (var (i, c) in trapsRemoved.OrderBy(t => t.Index)) layout.TrapSpawns.Insert(i, c);
            return (null, $"refused ({why})");
        }

        bool BridgeCell(int i)
        {
            if (i <= 0 || i >= rail.Party.Count - 1) return false;
            var p = rail.Party[i];
            if (p - rail.Party[i - 1] != rail.Party[i + 1] - p) return false;
            var twin = SlagTwin(rail, i, sign);
            return !platform.Contains(p) && !taken.Contains(p) && !IsRamp(layout, p) && !platform.Contains(twin) && !taken.Contains(twin) && !IsRamp(layout, twin);
        }
    }

    /// <summary>A cell no pass may pick: outside every grid.</summary>
    private static readonly GridPos Nowhere = new(-1, -1);

    /// <summary>The cells in (Y, X) order.</summary>
    private static IEnumerable<GridPos> ByYX(IEnumerable<GridPos> cells) => cells.OrderBy(c => c.Y).ThenBy(c => c.X);

    /// <summary>An enemy-lane cell whose two enemy-lane 4-neighbours face each other across it (not a corner).</summary>
    private static bool StraightEnemy(HashSet<GridPos> enemy, GridPos c)
    {
        var ns = c.Neighbours4().Where(enemy.Contains).ToList();
        return ns.Count == 2 && ns[0] - c == c - ns[1];
    }

    private static bool IsRamp(FloorLayout layout, GridPos p) => layout[p] == Cell.Ramp || layout.Ramps.ContainsKey(p);

    /// <summary>A ramp on <paramref name="p"/> or any of its 8-neighbours.</summary>
    private static bool RampNear(FloorLayout layout, GridPos p) => IsRamp(layout, p) || Around8(p).Any(q => IsRamp(layout, q));
}
