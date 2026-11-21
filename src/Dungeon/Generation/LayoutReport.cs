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
    /// The line in one row, or null when the floor has none: <c>rail: stops=[rooms] rides~[metres] total~{metres}
    /// cavern={From}..{To}|none|breached chasm={cells} strips={n} gorge=[{From}..{To}@{fFrom}-{fTo},…] planks={n}</c>, each ride
    /// counted in cells of 4 m from platform middle to platform middle, and each gorge span's ends as fractions of the last ride
    /// measured the same way. When a broken bridge was asked for (<see cref="RailLine.BreakWhy"/>), then
    /// <c> break={Gap}@{f} run={From}..{To}</c>, f the gap's fraction of its own ride measured the same way, or
    /// <c> break=none</c> or <c> break=refused</c>. When a split was asked for (<see cref="RailLine.SplitWhy"/>), then
    /// <c> split={Points}@{f} crevasse={From}..{To}</c>, f the points' fraction of their own ride measured the same way, or
    /// <c> split=none</c> or <c> split=refused</c>.
    /// </summary>
    public static string? Rail(FloorLayout l)
    {
        if (l.Rail is not { } rail) return null;
        var index = new Dictionary<GridPos, int>();
        for (var i = 0; i < rail.Party.Count; i++) index[rail.Party[i]] = i;
        var mids = rail.Stops.Select(s => index[s.Platform[0]] + s.Platform.Count / 2).ToList();
        var rides = mids.Skip(1).Zip(mids, (b, a) => (b - a) * 4).ToList();
        var cavern = rail.Cavern is { } c ? $"{c.From}..{c.To}" : rail.CavernBreached ? "breached" : "none";
        var (from, to) = mids.Count >= 2 ? (mids[^2], mids[^1]) : (0, 1);
        string Fraction(int i, int a, int b) => ((i - a) / (float)(b - a)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var gorge = string.Join(",", rail.Gorge.Select(g => $"{g.From}..{g.To}@{Fraction(g.From, from, to)}-{Fraction(g.To, from, to)}"));
        var line = $"rail: stops=[{string.Join(",", rail.Stops.Select(s => s.Room))}] rides~[{string.Join(",", rides)}] total~{rides.Sum()} cavern={cavern}"
            + $" chasm={l.ChasmCount} strips={rail.Strips.Count} gorge=[{gorge}] planks={l.Walkways.Count}";
        if (rail.BreakWhy != null)
            line += rail.Break is { } b
                ? $" break={b.Gap}@{Fraction(b.Gap, mids[b.Ride - 1], mids[b.Ride])} run={b.Run.From}..{b.Run.To}"
                : $" break={rail.BreakWhy}";
        if (rail.SplitWhy != null)
            line += rail.Split is { } s
                ? $" split={s.Points}@{Fraction(s.Points, mids[s.Ride - 1], mids[s.Ride])} crevasse={s.Crevasse.From}..{s.Crevasse.To}"
                : $" split={rail.SplitWhy}";
        return line;
    }
}
