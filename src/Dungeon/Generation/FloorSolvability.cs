namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// Proves a finished floor can be played through: every lock on it has its opener where the party can walk to it, and
/// the way down opens once the floor's objective is met. Engine-free. Hidden passages count as walls throughout (the
/// party may never notice one), and the only doors taken to open by themselves are the safe room's.
/// <list type="bullet">
/// <item>Treasure vaults: a lever gate's lever, or a key door's key, reachable with the vaults still shut (a key may lie in
/// another vault the party can open first); a hidden vault door still has to be noticed, which is up to the party.</item>
/// <item>Shifting Vault: the lever-and-gate puzzle proven dead-end free and solvable from the start; without a puzzle, the
/// ordinary lever reachable.</item>
/// <item>Jailbreak: the jail door and every prisoner out on the floor reachable, and a spare key somewhere the party can get to.</item>
/// <item>Ritual: the altar and every breach lever reachable.</item>
/// <item>Rising gas: the exit gate's lever reachable without passing that gate.</item>
/// <item>Crevasses: with the rails blocked, nothing the party could walk to were the chasm floor (a room, a chest, a key, an
/// objective cell, the stairs down) is cut off by it, and no chasm lies on a spawn, chest, key, objective or platform cell.</item>
/// <item>The exit island (<see cref="RailLine.Island"/>): the rail proof in place of the on-foot stairs checks
/// (<c>FloorSolvability.Island.cs</c>).</item>
/// </list>
/// </summary>
public static partial class FloorSolvability
{
    /// <summary>Every way the floor cannot be finished, in words; empty when it can.</summary>
    public static List<string> Problems(FloorLayout layout)
    {
        var problems = Problems(layout, null);
        // a railed floor must also be finishable on foot: the lines are the mine cart's, the halls have to do the job alone
        var rails = RailsBlocked(layout);
        if (rails != null)
        {
            var already = problems.ToHashSet();
            foreach (var q in Problems(layout, rails))
                if (!already.Contains(q)) problems.Add("with the rails blocked, " + q);
        }
        if (layout.ChasmCount > 0) problems.AddRange(ChasmProblems(layout, rails));
        return problems;
    }

    /// <summary>The line's lanes and pockets as walls, but the party lane's platform cells (they are the stop rooms' floor); null on a floor with no line.</summary>
    private static Func<GridPos, bool>? RailsBlocked(FloorLayout layout)
    {
        if (layout.Rail is not { } rail) return null;
        var platform = rail.Stops.SelectMany(s => s.Platform).ToHashSet();
        return p => (layout.IsRail(p) || layout.IsRailPocket(p)) && !(layout.IsPartyRail(p) && platform.Contains(p));
    }

    /// <summary>
    /// What the crevasses got wrong: anything reachable on foot (the rails blocked) were the chasm cells floor that is not
    /// reachable as the floor is, and any chasm cell on something that has to be stood on.
    /// </summary>
    private static List<string> ChasmProblems(FloorLayout layout, Func<GridPos, bool>? rails)
    {
        var problems = new List<string>();
        // the exit island is proven by rail (IslandProblems): its rooms, its stairs and what lies in it are no business of the chasms'
        var island = layout.Rail?.Island;
        bool InIsland(GridPos p) => island != null && island.Cells.Contains(p);
        var now = layout.Reachable(layout.UpStairs, rails);
        var open = layout.Reachable(layout.UpStairs, rails, layout.IsChasm);
        bool Cut(GridPos p) => open.Contains(p) && !now.Contains(p) && !InIsland(p);
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            if (island != null && island.Rooms.Contains(i)) continue;
            var cells = layout.Rooms[i].Cells().ToList();
            if (cells.Any(open.Contains) && !cells.Any(now.Contains)) problems.Add($"the chasm cuts off room {i}");
        }
        var objective = ObjectiveCells(layout);
        var keys = layout.KeySpawns.Concat(layout.Vaults.Where(v => v.Key != null).Select(v => v.Key!.Value)).ToList();
        foreach (var c in layout.ChestSpawns) if (Cut(c)) problems.Add($"the chasm cuts off the chest at {c}");
        foreach (var c in keys) if (Cut(c)) problems.Add($"the chasm cuts off the key at {c}");
        foreach (var c in objective) if (Cut(c)) problems.Add($"the chasm cuts off the objective cell at {c}");
        if (island == null && Cut(layout.DownStairs)) problems.Add($"the chasm cuts off the stairs down at {layout.DownStairs}");

        var spawns = layout.EnemySpawns.Concat(layout.LedgeSpawns).Concat(layout.CellarSpawns).Concat(layout.TrapSpawns).Concat(layout.BreakableSpawns).ToHashSet();
        var chests = layout.ChestSpawns.ToHashSet();
        var keySet = keys.ToHashSet();
        var platform = layout.Rail?.Stops.SelectMany(s => s.Platform).ToHashSet() ?? new HashSet<GridPos>();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var c = new GridPos(x, y);
                if (!layout.IsChasm(c)) continue;
                if (spawns.Contains(c)) problems.Add($"a chasm on a spawn at {c}");
                if (chests.Contains(c)) problems.Add($"a chasm on a chest at {c}");
                if (keySet.Contains(c)) problems.Add($"a chasm on a key at {c}");
                if (objective.Contains(c)) problems.Add($"a chasm on an objective cell at {c}");
                if (platform.Contains(c)) problems.Add($"a chasm on a platform at {c}");
            }
        return problems;
    }

    /// <summary>The cells the floor's objective lays things on or keeps: its reserved cells, the prisoners, the spare keys, the altar and the breach levers.</summary>
    private static HashSet<GridPos> ObjectiveCells(FloorLayout layout)
    {
        var cells = new HashSet<GridPos>();
        if (layout.Objective is not { } o) return cells;
        cells.UnionWith(o.Reserved);
        cells.UnionWith(o.PrisonerCells);
        cells.UnionWith(o.ExtraKeyCells);
        if (o.Altar is { } altar) cells.Add(altar);
        foreach (var (lever, _) in o.BreachLevers) cells.Add(lever);
        return cells;
    }

    private static List<string> Problems(FloorLayout layout, Func<GridPos, bool>? extraBlocked)
    {
        var problems = new List<string>();
        if (!layout.IsWalkable(layout.UpStairs)) { problems.Add($"the stairs up at {layout.UpStairs} stand on no floor"); return problems; }
        var objective = layout.Objective;
        var puzzle = objective?.Puzzle;
        var gateCells = puzzle?.Gates.ToHashSet() ?? new HashSet<GridPos>();

        // the vault doors that open as their locks are worked, and the gate settings the party can stand in
        var opened = new HashSet<int>();
        foreach (var v in layout.Vaults) if (v.Kind == VaultKind.SafeDoor) opened.Add(v.Index);
        bool ShutDoor(GridPos p) => (extraBlocked != null && extraBlocked(p)) || layout.Vaults.Any(v => v.Door == p && !opened.Contains(v.Index));
        HashSet<GridPos> ReachWith(int? gateState)
            => layout.Reachable(layout.UpStairs, p => ShutDoor(p) || (gateCells.Contains(p) && (gateState is not { } s || !puzzle!.IsOpen(s, IndexOf(puzzle!, p)))));
        // a lock counts as worked when its opener is in reach in some setting of the puzzle's gates the party can bring about
        HashSet<GridPos> Reach() => puzzle == null ? ReachWith(null) : PuzzleReach(layout, puzzle, ShutDoor);

        var stairsVault = objective?.StairsVault ?? -1;
        var exitGate = objective?.ExitGateVault ?? -1;
        var jailVault = objective?.JailVault ?? -1;
        var rewardVault = objective?.RewardVault ?? -1;

        // Shifting Vault first: its door opens by the puzzle, not by anything lying about
        if (objective is { Kind: FloorObjectiveKind.ShiftingVault } && stairsVault >= 0)
        {
            var vault = layout.Vaults[stairsVault];
            if (puzzle != null)
            {
                if (!VaultPuzzles.Verify(layout, puzzle, vault.Door, vault.Room, secretsOpen: false)) problems.Add("the vault puzzle has a dead end or no way to the vault door");
                else if (VaultPuzzles.Solve(layout, puzzle, vault.Door, vault.Room, 0, layout.UpStairs, secretsOpen: false) is not { } order || puzzle.StateFor(order.Aggregate(0, (acc, i) => acc ^ (1 << i))) != puzzle.Target)
                    problems.Add("the vault puzzle has no lever order that opens the vault");
                var leverClues = objective.Plaques.Count(p => p.Clue >= 0);
                if (leverClues < puzzle.LeverCount) problems.Add($"only {leverClues} of {puzzle.LeverCount} levers have a plaque");
                if (!objective.Plaques.Any(p => p.Clue < 0)) problems.Add("the vault has no notice saying which gates it wants");
            }
            else if (vault.Lever == null) problems.Add("the stairs vault has neither a puzzle nor a lever");
        }

        // open what can be opened, round after round, until nothing more gives (a key may lie behind another lock)
        for (var changed = true; changed;)
        {
            changed = false;
            var reach = Reach();
            foreach (var v in layout.Vaults)
            {
                if (opened.Contains(v.Index) || v.Index == rewardVault) continue; // the reward opens when the prisoners are free
                bool open;
                if (v.Index == stairsVault && puzzle != null) open = true; // proven above; the puzzle's gates are its business
                else if (v.Index == jailVault) open = reach.Contains(Doorstep(layout, v) ?? v.Door); // a pick, a spare key or a jailer's key
                else if (v.Kind == VaultKind.LeverGate) open = v.Lever is { } lever && reach.Contains(lever);
                else open = v.Key is { } key ? reach.Contains(key) : reach.Contains(Doorstep(layout, v) ?? v.Door); // a keyless key door takes a pick
                if (!open) continue;
                opened.Add(v.Index);
                changed = true;
            }
        }
        foreach (var v in layout.Vaults)
        {
            if (opened.Contains(v.Index) || v.Index == rewardVault) continue;
            var what = v.Index == stairsVault ? "the stairs vault" : v.Index == exitGate ? "the exit gate" : v.Index == jailVault ? "the jail" : $"treasure vault {v.Index}";
            problems.Add(v.Kind == VaultKind.LeverGate ? $"{what}: its lever{(v.Lever is { } l ? $" at {l}" : "")} is out of reach" : $"{what}: its key{(v.Key is { } k ? $" at {k}" : "")} is out of reach");
        }
        var final = Reach();
        if (rewardVault >= 0) opened.Add(rewardVault);
        // the way down: with a puzzle, from the vault's doorstep once the gates stand right (the proof puts the party there); else from the start
        var stairsDoorstep = puzzle != null && stairsVault >= 0 ? Doorstep(layout, layout.Vaults[stairsVault]) : null;
        var solved = stairsDoorstep is { } step
            ? layout.Reachable(step, p => ShutDoor(p) || (gateCells.Contains(p) && !puzzle!.IsOpen(puzzle.Target, IndexOf(puzzle, p))))
            : layout.Reachable(layout.UpStairs, ShutDoor);
        if (layout.Rail?.Island != null) problems.AddRange(IslandProblems(layout, ShutDoor, railsBlocked: extraBlocked != null)); // the rail proof, in place of the stairs
        else if (!solved.Contains(layout.DownStairs)) problems.Add($"the stairs down at {layout.DownStairs} are out of reach with every lock worked");

        switch (objective?.Kind)
        {
            case FloorObjectiveKind.Jailbreak:
                if (objective.JailRoom == null || jailVault < 0) { problems.Add("the jailbreak has no jail"); break; }
                var outside = objective.PrisonerCells.Skip(objective.JailedAtStart).ToList();
                foreach (var p in outside) if (!final.Contains(p)) problems.Add($"a prisoner at {p} is out of reach");
                if (outside.Count == 0) problems.Add("no prisoner waits out on the floor");
                foreach (var p in objective.PrisonerCells.Take(objective.JailedAtStart)) if (!objective.JailRoom.Contains(p)) problems.Add($"a jailed prisoner at {p} stands outside the jail");
                if (objective.ExtraKeyCells.Count == 0) problems.Add("the jail has no spare key on the floor");
                else if (!objective.ExtraKeyCells.Any(final.Contains)) problems.Add("every spare jail key is out of reach");
                break;
            case FloorObjectiveKind.Ritual:
                if (objective.Altar is not { } altar) { problems.Add("the ritual has no altar"); break; }
                if (!final.Contains(altar) && !Neighbours(altar).Any(final.Contains)) problems.Add($"the altar at {altar} is out of reach");
                foreach (var (lever, _) in objective.BreachLevers) if (!final.Contains(lever)) problems.Add($"a breach lever at {lever} is out of reach");
                break;
        }
        return problems;
    }

    /// <summary>Every cell the party can stand on in some situation the puzzle lets it reach from the start (levers pulled in any order it can walk), with <paramref name="shut"/> cells as walls.</summary>
    private static HashSet<GridPos> PuzzleReach(FloorLayout layout, VaultPuzzle puzzle, Func<GridPos, bool> shut)
    {
        var gateIndex = new Dictionary<GridPos, int>();
        for (var g = 0; g < puzzle.GateCount; g++) gateIndex[puzzle.Gates[g]] = g;
        HashSet<GridPos> From(int parity, GridPos at)
        {
            var state = puzzle.StateFor(parity);
            return layout.Reachable(at, p => shut(p) || (gateIndex.TryGetValue(p, out var g) && !puzzle.IsOpen(state, g)));
        }
        var all = new HashSet<GridPos>();
        var seen = new HashSet<(int, GridPos)> { (0, layout.UpStairs) };
        var queue = new Queue<(int Parity, GridPos Cell)>();
        queue.Enqueue((0, layout.UpStairs));
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            var reach = From(s.Parity, s.Cell);
            all.UnionWith(reach);
            for (var i = 0; i < puzzle.LeverCount; i++)
                if (reach.Contains(puzzle.Levers[i]) && seen.Add((s.Parity ^ (1 << i), puzzle.Levers[i]))) queue.Enqueue((s.Parity ^ (1 << i), puzzle.Levers[i]));
        }
        return all;
    }

    private static int IndexOf(VaultPuzzle puzzle, GridPos gate)
    {
        for (var g = 0; g < puzzle.GateCount; g++) if (puzzle.Gates[g] == gate) return g;
        return -1;
    }

    private static GridPos? Doorstep(FloorLayout layout, Vault v) => VaultPuzzles.Doorstep(layout, v.Door, v.Room);

    private static IEnumerable<GridPos> Neighbours(GridPos p)
    {
        yield return p + new GridPos(1, 0);
        yield return p + new GridPos(-1, 0);
        yield return p + new GridPos(0, 1);
        yield return p + new GridPos(0, -1);
    }
}
