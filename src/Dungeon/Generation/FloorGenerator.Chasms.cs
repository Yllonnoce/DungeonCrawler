namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The crevasses (<see cref="FloorSpec.Crevasses"/>): drops cut into the rock beside a railed line once it is laid. The
/// cavern on the line's reserved span (<see cref="RailLine.Cavern"/>) is the cavern's reach cut to <see cref="Cell.Chasm"/>,
/// with both lanes over the span on a timber deck; edge strips run beside straight stretches of the other rides, every
/// one the last ride's gorge window holds (<see cref="RailLine.Gorge"/>); and plank crossings (<see cref="FloorLayout.Walkways"/>)
/// cut the rock either side of a straight hall. Only rock and listed pockets become chasm, so no walk is ever cut. No rng:
/// the line and the halls alone fix where they go.
/// </summary>
public static partial class FloorGenerator
{
    /// <summary>
    /// Cut the crevasses into a laid line's rock; a no-op unless <see cref="FloorSpec.Crevasses"/> and the floor has a line.
    /// The cavern (when the line kept one): every cell of its reach (<see cref="CavernReach"/>) that is rock or a pocket
    /// becomes a chasm, and both lanes over the span become <see cref="BridgeKind.Deck"/> bridges. Then the strips
    /// (<see cref="CarveStrips"/>) and the planks (<see cref="CarveWalkways"/>). Pockets cut away leave
    /// <see cref="RailLine.Pockets"/>, the rock beside the drops is walled and <see cref="FloorLayout.ChasmCount"/> set.
    /// </summary>
    internal static void CarveChasms(FloorSpec spec, FloorLayout layout)
    {
        if (!spec.Crevasses || layout.Rail is not { } rail) return;
        var sign = LineSign(rail);
        var pockets = rail.Pockets.ToHashSet();
        var dropped = new HashSet<GridPos>();
        if (rail.Cavern is { } span)
        {
            var window = rail.Party.Skip(span.From).Take(span.To - span.From + 1).ToList();
            var side = RailSide(window[1] - window[0], sign);
            foreach (var c in CavernReach(window, sign))
            {
                var pocket = pockets.Contains(c);
                if (!pocket && !Rock(layout, c)) continue;
                layout[c] = Cell.Chasm;
                if (pocket) dropped.Add(c);
            }
            foreach (var c in window)
            {
                layout.Bridges[c] = BridgeKind.Deck;
                layout.Bridges[c + side] = BridgeKind.Deck; // the enemy twin
            }
            pockets.ExceptWith(dropped);
        }
        var (strips, gorge) = CarveStrips(layout, rail, sign, pockets, dropped);
        layout.Rail = rail with { Pockets = rail.Pockets.Where(p => !dropped.Contains(p)).ToList(), Strips = strips, Gorge = gorge };
        CarveWalkways(layout);
        RaiseWalls(layout);
        layout.ChasmCount = layout.Cells.Count(c => c == Cell.Chasm);
    }

    /// <summary>
    /// The edge strips: per ride, on the party-lane cells between its two platforms (<see cref="RailRules.StripPlatformGap"/>
    /// in from each), the straight runs a strip can lie beside (<see cref="StripCells"/>), inward of the party lane when that
    /// side has one, else outward of the enemy lane. A ride takes its longest run of at least <see cref="RailRules.StripMinCells"/>
    /// (the middle <see cref="RailRules.StripMaxCells"/> of a longer one), but for the cavern's ride, which has the cavern;
    /// the last ride takes every run of two or more cells within <see cref="RailRules.GorgeFrom"/> to
    /// <see cref="RailRules.GorgeTo"/> of it, platform middle to platform middle: the gorge. Returns the strips in line order
    /// and the gorge's; the pockets cut away are added to <paramref name="dropped"/>.
    /// </summary>
    private static (List<RailSpan> Strips, List<RailSpan> Gorge) CarveStrips(FloorLayout layout, RailLine rail, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)
    {
        var strips = new List<RailSpan>();
        var gorge = new List<RailSpan>();
        var index = new Dictionary<GridPos, int>();
        for (var i = 0; i < rail.Party.Count; i++) index[rail.Party[i]] = i;
        var enemy = rail.Enemy.ToHashSet();
        var taken = TakenCells(layout);
        var last = rail.Stops.Count - 2;
        for (var k = 0; k <= last; k++)
        {
            var from = index[rail.Stops[k].Platform[^1]] + RailRules.StripPlatformGap;
            var to = index[rail.Stops[k + 1].Platform[0]] - RailRules.StripPlatformGap;
            if (rail.Cavern is { } cavern && cavern.From >= from && cavern.From <= to) continue; // the cavern's ride
            var minimum = RailRules.StripMinCells;
            if (k == last)
            {
                var a = index[rail.Stops[k].Platform[0]] + rail.Stops[k].Platform.Count / 2;
                var b = index[rail.Stops[k + 1].Platform[0]] + rail.Stops[k + 1].Platform.Count / 2;
                from = Math.Max(from, a + (int)Math.Ceiling(RailRules.GorgeFrom * (b - a)));
                to = Math.Min(to, a + (int)Math.Floor(RailRules.GorgeTo * (b - a)));
                minimum = 2;
            }
            if (from > to) continue;
            // the runs on each side, and the side to cut: inward of the party lane when it has a run long enough
            var inward = Runs(-1);
            var runs = inward.Any(r => r.Count >= minimum) ? inward : Runs(1);
            runs = runs.Where(r => r.Count >= minimum).ToList();
            if (runs.Count == 0) continue;
            if (k != last)
            {
                var longest = runs.OrderByDescending(r => r.Count).First(); // the earliest of the longest
                var cut = Math.Max(0, longest.Count - RailRules.StripMaxCells);
                runs = new List<List<(int I, GridPos[] Cells)>> { longest.Skip(cut / 2).Take(RailRules.StripMaxCells).ToList() };
            }
            foreach (var run in runs)
            {
                foreach (var (_, cells) in run)
                    foreach (var c in cells)
                    {
                        if (pockets.Contains(c)) dropped.Add(c);
                        layout[c] = Cell.Chasm;
                    }
                var strip = new RailSpan(run[0].I, run[^1].I);
                strips.Add(strip);
                if (k == last) gorge.Add(strip);
            }

            // the maximal runs of consecutive cells a strip can lie beside on one side (-1 inward, +1 outward)
            List<List<(int I, GridPos[] Cells)>> Runs(int way)
            {
                var found = new List<List<(int, GridPos[])>>();
                List<(int, GridPos[])>? run = null;
                for (var i = from; i <= to; i++)
                {
                    if (StripCells(layout, rail, enemy, pockets, taken, sign, i, way) is { } cells)
                    {
                        if (run == null) found.Add(run = new());
                        run.Add((i, cells));
                    }
                    else run = null;
                }
                return found;
            }
        }
        return (strips, gorge);
    }

    /// <summary>
    /// The two cells a strip cuts beside party-lane cell <paramref name="i"/> (inward of the party lane at t = -1, -2 for
    /// <paramref name="way"/> -1; outward of the enemy lane at t = 2, 3 for +1), or null when it cannot: the lane is straight
    /// there with its enemy twin beside it, the lane cells beside the strip are level ground and no ramp, and in the block
    /// round the two cells there is only that lane's own stretch, its pockets and rock, with rock behind them. Each cell is
    /// rock or a pocket of the lane, level 0, outside every room, not under anything, reserved or taken (<see cref="TakenCells"/>).
    /// </summary>
    private static GridPos[]? StripCells(FloorLayout layout, RailLine rail, HashSet<GridPos> enemy, HashSet<GridPos> pockets, HashSet<GridPos> taken, int sign, int i, int way)
    {
        if (i <= 0 || i >= rail.Party.Count - 1) return null;
        var d = rail.Party[i + 1] - rail.Party[i];
        if (rail.Party[i] - rail.Party[i - 1] != d) return null;
        var side = RailSide(d, sign);
        var twin = rail.Party[i] + side;
        if (!enemy.Contains(twin)) return null;
        var lane = way < 0 ? rail.Party[i] : twin; // the lane the strip runs beside
        var u = way < 0 ? -side : side;            // away from that lane
        var own = way < 0 ? (Func<GridPos, bool>)layout.IsPartyRail : enemy.Contains;
        foreach (var c in new[] { rail.Party[i], twin })
            if (layout.LevelOf(c) != 0 || layout[c] == Cell.Ramp || layout.Ramps.ContainsKey(c)) return null;
        // the block round the strip's two cells: the lane's own cells (m = 0), the strip's rows (m = 1, 2), the rock behind (m = 3)
        for (var along = -1; along <= 1; along++)
            for (var m = 0; m <= 3; m++)
            {
                var q = lane + Times(d, along) + Times(u, m);
                if (layout.IsUnder(q)) return null;
                var ok = m switch
                {
                    0 => (own(q) && layout.LevelOf(q) == 0 && layout[q] != Cell.Ramp) || Rock(layout, q),
                    1 => Rock(layout, q) || pockets.Contains(q),
                    _ => Rock(layout, q),
                };
                if (!ok) return null;
            }
        var cells = new[] { lane + u, lane + Times(u, 2) };
        foreach (var c in cells)
        {
            var pocket = pockets.Contains(c);
            if (layout.LevelOf(c) != 0 || layout.RoomAt(c) != null || taken.Contains(c)) return null;
            if (!pocket && layout.IsReserved(c)) return null; // a pocket is the line's, which is what reserves it
        }
        return cells;
    }

    /// <summary>
    /// Up to <see cref="RailRules.WalkwayCount"/> plank crossings: a level hall cell (<see cref="WalkwayCell"/>) whose rock
    /// <see cref="RailRules.WalkwayReach"/> deep either side across the hall is cut to a chasm, so the hall crosses it on a
    /// <see cref="BridgeKind.Plank"/>. Chosen farthest-point (Manhattan) from the up stairs and then from those chosen, at
    /// least <see cref="RailRules.WalkwaySpacing"/> apart, ties by (Y, X).
    /// </summary>
    private static void CarveWalkways(FloorLayout layout)
    {
        var taken = TakenCells(layout);
        var candidates = new List<GridPos>();
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
                if (WalkwayCell(layout, taken, new GridPos(x, y), out _)) candidates.Add(new GridPos(x, y));
        while (layout.Walkways.Count < RailRules.WalkwayCount)
        {
            GridPos? best = null;
            var bestScore = -1;
            foreach (var c in candidates)
            {
                if (layout.Walkways.Any(w => w.ManhattanDistance(c) < RailRules.WalkwaySpacing)) continue;
                var score = layout.Walkways.Select(w => w.ManhattanDistance(c)).Append(c.ManhattanDistance(layout.UpStairs)).Min();
                if (score <= bestScore || !WalkwayCell(layout, taken, c, out _)) continue; // still a crossing after the planks before it
                best = c;
                bestScore = score;
            }
            if (best is not { } plank) break;
            WalkwayCell(layout, taken, plank, out var across);
            for (var t = 1; t <= RailRules.WalkwayReach; t++)
            {
                layout[plank + Times(across, t)] = Cell.Chasm;
                layout[plank - Times(across, t)] = Cell.Chasm;
            }
            layout.Bridges[plank] = BridgeKind.Plank;
            layout.Walkways.Add(plank);
        }
    }

    /// <summary>
    /// A hall cell a plank can cross at: level-0 plain floor outside every room and <see cref="RailRules.WalkwayRoomGap"/>
    /// cells (Chebyshev) from any, not the line, water, reserved, taken (<see cref="TakenCells"/>) or under anything, in a
    /// straight one-cell corridor: its walkable 4-neighbours are exactly two opposite ones, each plain level floor whose own
    /// walkable 4-neighbours are the plank and the cell beyond. The <see cref="RailRules.WalkwayReach"/> cells each side
    /// across the corridor (<paramref name="across"/>) are rock with nothing round them but rock and the corridor's three cells.
    /// </summary>
    private static bool WalkwayCell(FloorLayout layout, HashSet<GridPos> taken, GridPos c, out GridPos across)
    {
        across = default;
        if (!Plain(c) || layout.IsWater(c) || layout.IsReserved(c) || taken.Contains(c) || layout.IsBridge(c)) return false;
        var open = c.Neighbours4().Where(layout.IsWalkable).ToList();
        if (open.Count != 2 || open[0] - c != c - open[1]) return false;
        var e = open[0] - c;
        foreach (var a in open)
        {
            var beyond = a + (a - c);
            if (!Plain(a) || a.Neighbours4().Count(layout.IsWalkable) != 2 || !layout.IsWalkable(beyond)) return false;
        }
        for (var oy = -(RailRules.WalkwayRoomGap - 1); oy <= RailRules.WalkwayRoomGap - 1; oy++)
            for (var ox = -(RailRules.WalkwayRoomGap - 1); ox <= RailRules.WalkwayRoomGap - 1; ox++)
                if (layout.RoomAt(new GridPos(c.X + ox, c.Y + oy)) != null) return false;
        var p = e.Perp;
        var corridor = new HashSet<GridPos> { c, open[0], open[1] };
        var cut = new HashSet<GridPos>();
        for (var t = 1; t <= RailRules.WalkwayReach; t++) { cut.Add(c + Times(p, t)); cut.Add(c - Times(p, t)); }
        foreach (var q in cut)
        {
            if (!Rock(layout, q) || !InRailRange(layout, q) || layout.IsReserved(q) || taken.Contains(q)) return false;
            foreach (var n in Around8(q))
                if (!corridor.Contains(n) && !cut.Contains(n) && !Rock(layout, n)) return false;
        }
        across = p;
        return true;

        bool Plain(GridPos q) => layout[q] == Cell.Floor && layout.LevelOf(q) == 0 && !layout.IsUnder(q) && !layout.IsRail(q)
                                 && !layout.Ramps.ContainsKey(q) && layout.RoomAt(q) == null;
    }

    /// <summary>The cells a crevasse may not cut or cross: every spawn, chest, key, trap and breakable, the hazards, the vaults' doors, levers and keys, the objective's cells, the stairs and the platforms.</summary>
    private static HashSet<GridPos> TakenCells(FloorLayout layout)
    {
        var taken = new HashSet<GridPos>(layout.EnemySpawns);
        taken.UnionWith(layout.LedgeSpawns);
        taken.UnionWith(layout.CellarSpawns);
        taken.UnionWith(layout.ChestSpawns);
        taken.UnionWith(layout.KeySpawns);
        taken.UnionWith(layout.TrapSpawns);
        taken.UnionWith(layout.BreakableSpawns);
        foreach (var h in layout.Hazards) taken.UnionWith(h.Cells);
        foreach (var v in layout.Vaults)
        {
            taken.Add(v.Door);
            if (v.Lever is { } lever) taken.Add(lever);
            if (v.Key is { } key) taken.Add(key);
        }
        if (layout.Objective is { } o)
        {
            taken.UnionWith(o.Reserved);
            taken.UnionWith(o.PrisonerCells);
            taken.UnionWith(o.ExtraKeyCells);
            if (o.Altar is { } altar) taken.Add(altar);
            foreach (var (lever, _) in o.BreachLevers) taken.Add(lever);
            foreach (var (cell, _) in o.Plaques) taken.Add(cell);
        }
        taken.Add(layout.UpStairs);
        taken.Add(layout.DownStairs);
        if (layout.Rail is { } rail) taken.UnionWith(rail.Stops.SelectMany(s => s.Platform));
        return taken;
    }

    /// <summary>
    /// The sign the line was routed with: the enemy lane's first cell is the twin of the party lane's first, which
    /// <see cref="Twin"/> lays as a straight cell, one step to <see cref="RailSide"/>.
    /// </summary>
    internal static int LineSign(RailLine rail)
        => rail.Enemy[0] - rail.Party[0] == RailSide(rail.Party[1] - rail.Party[0], 1) ? 1 : -1;
}
