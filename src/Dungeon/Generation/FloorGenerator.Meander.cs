namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The meander: on a railed ring with a band wider than <see cref="RailRules.Band"/>, a routed stretch of party lane is
/// lengthened with outward rectangular bulges until its ride reaches <see cref="RailRules.RideMinCells"/>, and the stretch
/// into the last stop before the exit keeps a straight, rock-backed window for the cavern (<see cref="RailLine.Cavern"/>).
/// No rng: the same stretch always bends the same way.
/// </summary>
public static partial class FloorGenerator
{
    private static GridPos Times(GridPos d, int t) => new(d.X * t, d.Y * t);

    /// <summary>
    /// <paramref name="cells"/> with outward bulges (to <see cref="RailSide"/>) added until it is at least
    /// <paramref name="addMin"/> cells longer, never more than <paramref name="addMax"/>, or until none fits. A bulge of depth
    /// D leaves a straight run at index k, goes D cells out, <see cref="RailRules.BulgeSpan"/> along and D back to the run,
    /// adding 2D cells; the deepest that fits is tried first, nearest the middle of the stretch first. Every new cell of
    /// either lane must pass <paramref name="ok"/> and lie outside <paramref name="keep"/>, the twin must hold and the lane
    /// stay clear of itself (<see cref="SelfClear"/>). The first and last three cells never move.
    /// </summary>
    internal static List<GridPos> Meander(IReadOnlyList<GridPos> cells, int sign, Func<GridPos, bool> ok, IReadOnlySet<GridPos> keep, int addMin, int addMax)
    {
        var path = new List<GridPos>(cells);
        var added = 0;
        const int span = RailRules.BulgeSpan;
        while (added < addMin)
        {
            var placed = false;
            for (var depth = RailRules.BulgeMaxDepth; depth >= 2 && !placed; depth--)
            {
                if (added + 2 * depth > addMax) continue;
                var mid = path.Count / 2;
                var starts = new List<int>();
                for (var k = 3; k + span <= path.Count - 4; k++)
                    if (StraightRun(path, k - 2, k + span + 2)) starts.Add(k);
                foreach (var k in starts.OrderBy(k => Math.Abs(k - mid)).ThenBy(k => k))
                {
                    var d = path[k + 1] - path[k];
                    var h = RailSide(d, sign); // outward only
                    var bulge = new List<GridPos>(2 * depth + span - 1);
                    for (var t = 1; t <= depth; t++) bulge.Add(path[k] + Times(h, t));
                    for (var t = 1; t <= span; t++) bulge.Add(path[k] + Times(h, depth) + Times(d, t));
                    for (var t = depth - 1; t >= 1; t--) bulge.Add(path[k + span] + Times(h, t));
                    if (bulge.Any(c => !ok(c) || keep.Contains(c))) continue;
                    var trial = new List<GridPos>(path.Count + 2 * depth);
                    trial.AddRange(path.Take(k + 1));
                    trial.AddRange(bulge);
                    trial.AddRange(path.Skip(k + span));
                    var enemy = Twin(trial, sign, out var source);
                    if (enemy == null) continue;
                    var fresh = true;
                    for (var j = 0; j < enemy.Count && fresh; j++)
                        if (source[j] >= k && source[j] <= k + 2 * depth + span && (!ok(enemy[j]) || keep.Contains(enemy[j]))) fresh = false;
                    if (!fresh || !SelfClear(trial, sign)) continue;
                    path = trial;
                    added += 2 * depth;
                    placed = true;
                    break;
                }
            }
            if (!placed) break;
        }
        return path;
    }

    /// <summary>Whether <paramref name="path"/>[from..to] is one straight run in a single heading.</summary>
    private static bool StraightRun(IReadOnlyList<GridPos> path, int from, int to)
    {
        var d = path[from + 1] - path[from];
        for (var i = from + 1; i < to; i++)
            if (path[i + 1] - path[i] != d) return false;
        return true;
    }

    /// <summary>
    /// The lane keeps a wall between its own legs: every two 8-adjacent line cells (a party cell by its index, an enemy
    /// cell by the party index <see cref="Twin"/> gives it) are at most <see cref="RailRules.SelfClearGap"/> party indices
    /// apart. False when the twin fails.
    /// </summary>
    internal static bool SelfClear(IReadOnlyList<GridPos> party, int sign)
    {
        var enemy = Twin(party, sign, out var source);
        if (enemy == null) return false;
        var at = new Dictionary<GridPos, List<int>>(party.Count + enemy.Count);
        void Put(GridPos c, int i)
        {
            if (!at.TryGetValue(c, out var list)) at[c] = list = new List<int>(2);
            list.Add(i);
        }
        for (var i = 0; i < party.Count; i++) Put(party[i], i);
        for (var j = 0; j < enemy.Count; j++) Put(enemy[j], source[j]);
        foreach (var (c, mine) in at)
            for (var oy = -1; oy <= 1; oy++)
                for (var ox = -1; ox <= 1; ox++)
                {
                    if (!at.TryGetValue(new GridPos(c.X + ox, c.Y + oy), out var theirs)) continue;
                    foreach (var a in mine)
                        foreach (var b in theirs)
                            if (Math.Abs(a - b) > RailRules.SelfClearGap) return false;
                }
        return true;
    }

    /// <summary>
    /// The straight window of <paramref name="cells"/> kept for the cavern, or null: of the maximal straight runs at least
    /// three cells from either end, each trimmed by two cells at both ends and at least <see cref="RailRules.CavernCells"/>
    /// long, the one with the deepest <paramref name="rock"/> outward past the enemy lane and its pockets (from three steps out, counted to
    /// <see cref="RailRules.CavernDepth"/> + 3 at most, the least over its cells), then the longest, then the one nearest the
    /// middle. Null when the best is shallower than <see cref="RailRules.CavernDepth"/>.
    /// </summary>
    internal static List<GridPos>? CavernWindow(IReadOnlyList<GridPos> cells, int sign, Func<GridPos, bool> rock)
    {
        int lo = 3, hi = cells.Count - 4; // the cells at least three from either end
        (int from, int to, int depth, double mid)? best = null;
        for (var i = lo; i < hi;)
        {
            var d = cells[i + 1] - cells[i];
            var j = i + 1;
            while (j < hi && cells[j + 1] - cells[j] == d) j++;
            var (from, to) = (i + 2, j - 2);
            if (to - from + 1 >= RailRules.CavernCells)
            {
                var side = RailSide(d, sign);
                var depth = int.MaxValue;
                for (var k = from; k <= to && depth > 0; k++)
                {
                    var n = 0;
                    while (n < RailRules.CavernDepth + 3 && rock(cells[k] + Times(side, n + 3))) n++;
                    depth = Math.Min(depth, n);
                }
                var mid = Math.Abs((from + to) / 2.0 - (cells.Count - 1) / 2.0);
                if (best is not { } b || depth > b.depth || (depth == b.depth && (to - from > b.to - b.from || (to - from == b.to - b.from && mid < b.mid))))
                    best = (from, to, depth, mid);
            }
            i = j;
        }
        if (best is not { } w || w.depth < RailRules.CavernDepth) return null;
        return cells.Skip(w.from).Take(w.to - w.from + 1).ToList();
    }

    /// <summary>
    /// The cells the meander keeps off round a cavern window: the window, the enemy lane and its pocket row beside it, the
    /// rock <see cref="RailRules.CavernDepth"/> deep past them, and a cell's margin round the lot.
    /// </summary>
    private static IEnumerable<GridPos> CavernKeep(List<GridPos> window, int sign)
    {
        var d = window[1] - window[0];
        var side = RailSide(d, sign);
        for (var i = -1; i <= window.Count; i++)
            for (var t = -1; t <= RailRules.CavernDepth + 3; t++)
                yield return window[0] + Times(d, i) + Times(side, t);
    }
}
