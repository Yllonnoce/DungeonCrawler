namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// One lethal hazard dealt onto a floor. A Press owns three straight corridor cells with its plate in the middle;
/// a Spike Bed owns a patch of room floor (or one corridor cell) with its plate on the first cell.
/// </summary>
/// <param name="GuardsCell">A chest, key or vault door the hazard stands between the party and, when it has one.</param>
/// <param name="Horizontal">A Press run laid along X (true) or along Y.</param>
public sealed record HazardPlacement(int Index, HazardKind Kind, IReadOnlyList<GridPos> Cells, HazardVariant Variant, GridPos Plate, GridPos? GuardsCell, bool Horizontal);

/// <summary>How many hazards a floor may carry, by tier. Floors 1 and 2 have none.</summary>
public static class HazardCounts
{
    public static (int Press, int BedsMin, int BedsMax, bool PeriodicAllowed) For(int floor)
    {
        if (floor < 3) return (0, 0, 0, false);
        if (floor <= 5) return (1, 1, 1, false);
        if (floor <= 10) return (1, 1, 2, true);
        if (floor <= 15) return (1, 2, 2, true);
        return (1, 2, 3, true);
    }
}

/// <summary>
/// Deals the Press and the Spike Beds after every other placer, from its own seed, so a floor with no hazards is
/// exactly the floor the generator made before. Every rule here has a test in HazardPlacementTests.
/// </summary>
public static class HazardPlanner
{
    private const int StairsClearance = 2;
    private const int TrapClearance = 2;
    private const int GuardSearchSteps = 6;
    private const float CryptPressChance = 0.7f;

    public static void PlaceHazards(FloorSpec spec, Random rng, FloorLayout layout)
    {
        if (!spec.HazardsEnabled) return;
        var (pressMax, bedsMin, bedsMax, periodicAllowed) = HazardCounts.For(spec.Floor);
        if (pressMax == 0 && bedsMax == 0) return;

        var critical = CriticalPath(layout);
        var depth = PathDepths(layout);
        var taken = Taken(layout);
        var index = 0;

        if (pressMax > 0 && layout.Kind != LayoutKind.Labyrinth && (spec.Floor > 5 || rng.NextDouble() < CryptPressChance))
        {
            var runs = PressCandidates(layout, taken, depth, rng);
            if (runs.Count > 0)
            {
                var (cells, horizontal, guards) = runs[0];
                layout.Hazards.Add(new HazardPlacement(index++, HazardKind.Press, cells, HazardVariant.Pressure, cells[1], guards, horizontal));
            }
        }

        if (bedsMax > 0 && layout.Kind is not (LayoutKind.Flooded or LayoutKind.Burning))
        {
            var want = rng.Next(bedsMin, bedsMax + 1);
            var patches = BedCandidates(layout, taken, rng);
            foreach (var cells in patches)
            {
                if (layout.Hazards.Count(h => h.Kind == HazardKind.SpikeBed) >= want) break;
                if (cells.Any(c => layout.Hazards.Any(h => h.Cells.Any(hc => hc.ManhattanDistance(c) <= 1)))) continue;
                var onPath = cells.Any(critical.Contains);
                HazardVariant variant;
                if (onPath) { if (!periodicAllowed) continue; variant = HazardVariant.Periodic; }
                else variant = periodicAllowed && !IsDeep(cells[0], depth) && rng.NextDouble() < 0.4 ? HazardVariant.Periodic : HazardVariant.Pressure;
                var guards = GuardedCell(layout, cells, GuardSearchSteps);
                layout.Hazards.Add(new HazardPlacement(index++, HazardKind.SpikeBed, cells, variant, cells[0], guards, true));
            }
        }
    }

    /// <summary>The cells on the shortest walk from the up stairs to the down stairs.</summary>
    public static HashSet<GridPos> CriticalPath(FloorLayout layout)
    {
        var path = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, maxExpansions: 20000);
        return path == null ? new HashSet<GridPos>() : new HashSet<GridPos>(path);
    }

    /// <summary>Removing this cell would cut the down stairs off from the up stairs.</summary>
    public static bool IsBridgeCell(FloorLayout layout, GridPos cell) => !layout.Reachable(layout.UpStairs, c => c == cell).Contains(layout.DownStairs);

    private static bool Severs(FloorLayout layout, IReadOnlyList<GridPos> cells) => !layout.Reachable(layout.UpStairs, cells.Contains).Contains(layout.DownStairs);

    private static HashSet<GridPos> Taken(FloorLayout layout)
    {
        var taken = new HashSet<GridPos>(layout.EnemySpawns.Concat(layout.ChestSpawns).Concat(layout.BreakableSpawns).Concat(layout.KeySpawns).Concat(layout.TrapSpawns).Concat(layout.LedgeSpawns).Concat(layout.CellarSpawns));
        foreach (var v in layout.Vaults) { taken.Add(v.Door); if (v.Key is { } k) taken.Add(k); if (v.Lever is { } l) taken.Add(l); }
        foreach (var s in layout.Secrets) taken.Add(s.Alcove);
        foreach (var s in new[] { layout.UpStairs, layout.DownStairs })
            for (var oy = -StairsClearance; oy <= StairsClearance; oy++) for (var ox = -StairsClearance; ox <= StairsClearance; ox++) taken.Add(new GridPos(s.X + ox, s.Y + oy));
        return taken;
    }

    /// <summary>BFS walking distance from the up stairs, for "the deeper half of the floor".</summary>
    private static Dictionary<GridPos, int> PathDepths(FloorLayout layout)
    {
        var dist = new Dictionary<GridPos, int> { [layout.UpStairs] = 0 };
        var queue = new Queue<GridPos>(); queue.Enqueue(layout.UpStairs);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var n in p.Neighbours4())
                if (layout.Contains(n) && layout.IsWalkable(n) && !dist.ContainsKey(n)) { dist[n] = dist[p] + 1; queue.Enqueue(n); }
        }
        return dist;
    }

    private static bool IsDeep(GridPos cell, Dictionary<GridPos, int> depth)
    {
        if (depth.Count == 0 || !depth.TryGetValue(cell, out var d)) return false;
        return d > depth.Values.Max() / 2;
    }

    private static bool Clear(FloorLayout layout, HashSet<GridPos> taken, GridPos c)
    {
        if (!layout.Contains(c) || layout[c] != Cell.Floor || taken.Contains(c) || layout.IsReserved(c) || layout.IsWater(c) || layout.IsInVault(c)) return false;
        if (layout.BossArena is { } arena && arena.Contains(c)) return false;
        if (layout.TrapSpawns.Any(t => t.ManhattanDistance(c) <= TrapClearance)) return false;
        foreach (var n in c.Neighbours4())
        {
            if (!layout.Contains(n)) return false;
            if (layout.Ramps.ContainsKey(n) || layout[n] is Cell.Pit or Cell.Ramp or Cell.Door) return false;
            if (layout.Ledges.Any(g => g.Cells.Contains(n) || g.RampBottom == n || g.RampTop == n)) return false;
        }
        return true;
    }

    private static List<(IReadOnlyList<GridPos> Cells, bool Horizontal, GridPos? Guards)> PressCandidates(FloorLayout layout, HashSet<GridPos> taken, Dictionary<GridPos, int> depth, Random rng)
    {
        var found = new List<(IReadOnlyList<GridPos>, bool, GridPos?)>();
        foreach (var horizontal in new[] { true, false })
        {
            var along = horizontal ? new GridPos(1, 0) : new GridPos(0, 1);
            var side = horizontal ? new GridPos(0, 1) : new GridPos(1, 0);
            for (var y = 1; y < layout.Height - 1; y++)
                for (var x = 1; x < layout.Width - 1; x++)
                {
                    var c0 = new GridPos(x, y);
                    var cells = new List<GridPos> { c0, c0 + along, c0 + along + along };
                    if (!cells.All(c => Clear(layout, taken, c) && layout.RoomAt(c) == null && !layout.IsNestMouth(c))) continue;
                    if (!cells.All(c => layout.Contains(c + side) && layout.Contains(c - side) && layout[c + side] == Cell.Wall && layout[c - side] == Cell.Wall)) continue;
                    // the cells before and after the run must be open floor, or the run is a dead end with nothing to bait through it
                    var before = c0 - along; var after = cells[2] + along;
                    if (!layout.Contains(before) || !layout.Contains(after) || !layout.IsWalkable(before) || !layout.IsWalkable(after)) continue;
                    if (Severs(layout, cells)) continue;
                    found.Add((cells, horizontal, GuardedCell(layout, cells, GuardSearchSteps)));
                }
        }
        Shuffle(rng, found);
        // a run that guards something beats one that does not; among those, the deeper half of the floor
        return found.OrderBy(f => f.Item3 == null ? 1 : 0).ThenBy(f => IsDeep(f.Item1[1], depth) ? 0 : 1).ToList();
    }

    private static List<IReadOnlyList<GridPos>> BedCandidates(FloorLayout layout, HashSet<GridPos> taken, Random rng)
    {
        var patches = new List<IReadOnlyList<GridPos>>();
        foreach (var room in layout.Rooms)
        {
            if (room.W < 5 || room.H < 5) continue;
            if (layout.BossArena == room || layout.Vaults.Any(v => v.Room == room) || layout.Cellars.Any(c => c.Room == room)) continue;
            if (layout.Objective is { } obj && (obj.JailRoom == room || obj.ShrineRoom == room)) continue;
            var size = room.W >= 7 && room.H >= 7 && rng.NextDouble() < 0.4 ? 3 : 2;
            for (var y = room.Y + 1; y + size - 1 <= room.Bottom - 1; y++)
                for (var x = room.X + 1; x + size - 1 <= room.Right - 1; x++)
                {
                    var cells = new List<GridPos>();
                    for (var dy = 0; dy < size; dy++) for (var dx = 0; dx < size; dx++) cells.Add(new GridPos(x + dx, y + dy));
                    if (cells.All(c => Clear(layout, taken, c) && !layout.Under[c.Y * layout.Width + c.X] && layout.RoomAt(c) == room)) patches.Add(cells);
                }
        }
        // single corridor cells, after the rooms
        var corridors = new List<IReadOnlyList<GridPos>>();
        for (var y = 1; y < layout.Height - 1; y++)
            for (var x = 1; x < layout.Width - 1; x++)
            {
                var c = new GridPos(x, y);
                if (layout.RoomAt(c) == null && !layout.IsNestMouth(c) && Clear(layout, taken, c)) corridors.Add(new[] { c });
            }
        Shuffle(rng, patches);
        Shuffle(rng, corridors);
        patches.AddRange(corridors);
        return patches;
    }

    /// <summary>A chest, key or vault door within a few steps of the hazard's far side, reached without crossing the hazard.</summary>
    private static GridPos? GuardedCell(FloorLayout layout, IReadOnlyList<GridPos> cells, int steps)
    {
        var targets = new HashSet<GridPos>(layout.ChestSpawns.Concat(layout.KeySpawns).Concat(layout.Vaults.Select(v => v.Door)));
        var blocked = new HashSet<GridPos>(cells);
        var seen = new HashSet<GridPos>(cells);
        var frontier = new List<(GridPos Cell, int Steps)>();
        foreach (var c in cells) foreach (var n in c.Neighbours4()) if (layout.Contains(n) && layout.IsWalkable(n) && !blocked.Contains(n) && seen.Add(n)) frontier.Add((n, 1));
        while (frontier.Count > 0)
        {
            var (p, d) = frontier[0]; frontier.RemoveAt(0);
            if (targets.Contains(p)) return p;
            if (d >= steps) continue;
            foreach (var n in p.Neighbours4())
                if (layout.Contains(n) && layout.IsWalkable(n) && !blocked.Contains(n) && seen.Add(n)) frontier.Add((n, d + 1));
        }
        return null;
    }

    private static void Shuffle<T>(Random rng, List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }
}
