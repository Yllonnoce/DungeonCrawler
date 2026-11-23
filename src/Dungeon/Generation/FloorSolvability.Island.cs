namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The rail proof (plan 15, spec B.7): on a floor with an exit island (<see cref="RailLine.Island"/>) the way down is by rail
/// only, so the on-foot stairs checks give way to these.
/// </summary>
public static partial class FloorSolvability
{
    /// <summary>
    /// What an exit-island floor gets wrong, with <paramref name="shut"/> the every-lock-worked walls (the reward vault open, and
    /// the line's lanes too when <paramref name="railsBlocked"/>, which checks only the first item: a rails-blocked floor has no
    /// ride to the island).
    /// <list type="bullet">
    /// <item><b>On foot</b>: every intermediate stop's first platform cell (its cage's siding lies beside it) is reached from the up
    /// stairs; with the rails open, so is each party cell of the parked train's place, every key outside the island (the loose keys,
    /// the vaults' keys and the objective's spare keys), and every room outside the island that the up stairs would reach with the
    /// island's cut and cave-in cells read as open (Ruling 45).</item>
    /// <item><b>Sealed</b>: no island cell is reached from the up stairs with every door and every secret open.</item>
    /// <item><b>By rail</b>: from the parked train's place to the cell before the exit platform, each party-lane cell is walkable,
    /// but the bridge's, which are chasm with their track kept. A parked place or an exit platform not on the party lane before it is
    /// itself a problem, never a walk that checks nothing.</item>
    /// <item><b>Inside</b>: from the exit platform's first cell, a cell of every island room and the stairs down are reached.</item>
    /// <item><b>The pit</b> (<see cref="RailLine.Pit"/>): the party cells either side of it, and the enemy twins of it and of them,
    /// are walkable.</item>
    /// </list>
    /// </summary>
    private static List<string> IslandProblems(FloorLayout layout, Func<GridPos, bool> shut, bool railsBlocked)
    {
        var problems = new List<string>();
        var rail = layout.Rail!;
        var island = rail.Island!;
        var reach = layout.Reachable(layout.UpStairs, shut);
        for (var k = 1; k < rail.Stops.Count - 1; k++)
            if (rail.Stops[k].Platform[0] is var stop && !reach.Contains(stop)) problems.Add($"on foot, stop {k}'s platform at {stop} is out of reach");
        if (railsBlocked) return problems;

        var last = rail.Stops.Count - 1;
        var platform = rail.Stops[last].Platform[0];
        var end = rail.Party.ToList().IndexOf(platform);
        var parkedOnLane = island.Parked.From >= 0 && island.Parked.From <= island.Parked.To && island.Parked.To < rail.Party.Count;
        if (end < 0) problems.Add($"the exit platform's cell {platform} is not on the party lane");
        if (!parkedOnLane || end >= 0 && island.Parked.From >= end)
            problems.Add($"the parked train's place {island.Parked.From}..{island.Parked.To} does not lie on the party lane before the exit platform");
        if (parkedOnLane)
            for (var i = island.Parked.From; i <= island.Parked.To; i++)
                if (rail.Party[i] is var c && !reach.Contains(c)) problems.Add($"on foot, the parked train's place at {c} is out of reach");
        var keys = layout.KeySpawns.Concat(layout.Vaults.Where(v => v.Key != null).Select(v => v.Key!.Value)).Concat(layout.Objective?.ExtraKeyCells ?? new());
        foreach (var k in keys)
            if (!island.Cells.Contains(k) && !reach.Contains(k)) problems.Add($"on foot, the key at {k} is out of reach");
        var cuts = island.Cut(rail).Concat(island.CaveIns).ToHashSet();
        var uncut = layout.Reachable(layout.UpStairs, shut, cuts.Contains);
        for (var i = 0; i < layout.Rooms.Count; i++)
        {
            if (island.Rooms.Contains(i)) continue;
            var cells = layout.Rooms[i].Cells().ToList();
            if (cells.Any(uncut.Contains) && !cells.Any(reach.Contains)) problems.Add($"on foot, room {i} is out of reach");
        }

        var everyDoor = layout.Reachable(layout.UpStairs, null, secretsOpen: true);
        if (island.Cells.Where(everyDoor.Contains).OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => (GridPos?)c).FirstOrDefault() is { } leak)
            problems.Add($"the exit island leaks at {leak}");

        for (var i = parkedOnLane ? island.Parked.From : end; i < end; i++)
        {
            var c = rail.Party[i];
            var onBridge = i >= island.Bridge.From && i <= island.Bridge.To;
            if (onBridge ? !layout.IsChasm(c) : !layout.IsWalkable(c)) problems.Add($"the line into the island breaks at {c}");
        }

        var inside = layout.Reachable(platform, shut);
        foreach (var r in island.Rooms)
            if (!layout.Rooms[r].Cells().Any(inside.Contains)) problems.Add($"inside the island, room {r} is out of reach from stop {last}'s platform");
        if (!inside.Contains(layout.DownStairs)) problems.Add($"inside the island, the stairs down at {layout.DownStairs} are out of reach from stop {last}'s platform");

        if (rail.Pit is { } pit)
        {
            var sign = FloorGenerator.LineSign(rail);
            GridPos Twin(int i) => rail.Party[i] + FloorGenerator.RailSide(rail.Party[i + 1] - rail.Party[i], sign);
            var whole = new[] { rail.Party[pit.Cell - 1], rail.Party[pit.Cell + 1], Twin(pit.Cell - 1), Twin(pit.Cell), Twin(pit.Cell + 1) };
            foreach (var c in whole)
                if (!layout.IsWalkable(c)) problems.Add($"the pit cuts the line at {c}");
        }
        return problems;
    }
}
