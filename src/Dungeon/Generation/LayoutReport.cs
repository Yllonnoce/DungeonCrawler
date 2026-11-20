namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Plain-text lines describing a generated floor's rooms and its rail line, for <c>--print-map</c>.</summary>
public static class LayoutReport
{
    /// <summary>
    /// One line per room: <c>room {i}: ({X},{Y}) {W}x{H} level={L} {hub|ring|side|secret}</c> and then the tags that apply
    /// (<c>up</c>, <c>down</c>, <c>stop={k}</c>, <c>vault={index}</c>, <c>jail</c>, <c>safe</c>, <c>nest</c>, <c>camp</c>, <c>shrine</c>).
    /// </summary>
    public static IEnumerable<string> Rooms(FloorLayout l)
    {
        for (var i = 0; i < l.Rooms.Count; i++)
        {
            var r = l.Rooms[i];
            var role = i == 0 && l.RingRooms > 0 ? "hub" : i >= 1 && i <= l.RingRooms ? "ring" : l.SecretRooms.Any(s => s.Room == r) ? "secret" : "side";
            var line = $"room {i}: ({r.X},{r.Y}) {r.W}x{r.H} level={l.LevelOf(r.Center)} {role}";
            if (r.Contains(l.UpStairs)) line += " up";
            if (r.Contains(l.DownStairs)) line += " down";
            if (l.Rail is { } rail)
                for (var k = 0; k < rail.Stops.Count; k++)
                    if (rail.Stops[k].Room == i) line += $" stop={k}";
            foreach (var v in l.Vaults.Where(v => v.Room == r)) line += $" vault={v.Index}";
            if (l.Objective?.JailRoom == r) line += " jail";
            if (l.SafeRoom == i) line += " safe";
            if (l.NestAt(r) != null) line += " nest";
            if (l.Structures.Any(s => s.Room == r && s.Kind == StructureKind.Camp)) line += " camp";
            if ((l.ShrineCell is { } sc && r.Contains(sc)) || l.Objective?.ShrineRoom == r) line += " shrine";
            yield return line;
        }
    }

    /// <summary>
    /// The line in one row, or null when the floor has none: <c>rail: stops=[rooms] rides~[metres] total~{metres} cavern={From}..{To}|none</c>,
    /// each ride counted in cells of 4 m from platform middle to platform middle.
    /// </summary>
    public static string? Rail(FloorLayout l)
    {
        if (l.Rail is not { } rail) return null;
        var index = new Dictionary<GridPos, int>();
        for (var i = 0; i < rail.Party.Count; i++) index[rail.Party[i]] = i;
        var mids = rail.Stops.Select(s => index[s.Platform[0]] + s.Platform.Count / 2).ToList();
        var rides = mids.Skip(1).Zip(mids, (b, a) => (b - a) * 4).ToList();
        var cavern = rail.Cavern is { } c ? $"{c.From}..{c.To}" : "none";
        return $"rail: stops=[{string.Join(",", rail.Stops.Select(s => s.Room))}] rides~[{string.Join(",", rides)}] total~{rides.Sum()} cavern={cavern}";
    }
}
