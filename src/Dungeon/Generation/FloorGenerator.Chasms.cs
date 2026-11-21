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
    /// becomes a chasm, and both lanes over the span become <see cref="BridgeKind.Deck"/> bridges. Then the broken bridge
    /// when <see cref="FloorSpec.BrokenBridgeRide"/> asks for one (<see cref="CarveBreak"/>), the split's crevasse when
    /// <see cref="FloorSpec.SplitRide"/> asks for one (<see cref="CarveSplit"/>), the strips (<see cref="CarveStrips"/>, none on
    /// the break's or the split's ride once it is cut) and the planks (<see cref="CarveWalkways"/>). Pockets cut away leave
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
                for (var t = 2; t <= 2 + RailRules.CavernDepth; t++) // the drop's lip is the lane's floor beside it
                    if (layout.IsChasm(c + Times(side, t))) layout.SetLevel(c + Times(side, t), layout.LevelOf(c));
                layout.Bridges[c] = BridgeKind.Deck;
                layout.Bridges[c + side] = BridgeKind.Deck; // the enemy twin
            }
            pockets.ExceptWith(dropped);
        }
        RailBreak? broken = null;
        string? why = null;
        if (spec.BrokenBridgeRide > 0)
        {
            (broken, why) = CarveBreak(layout, rail, spec.BrokenBridgeRide, sign, pockets, dropped);
            pockets.ExceptWith(dropped);
        }
        RailSplit? split = null;
        string? splitWhy = null;
        if (spec.SplitRide > 0)
        {
            (split, splitWhy) = CarveSplit(layout, rail, spec.SplitRide, spec.BrokenBridgeRide, sign, pockets, dropped);
            pockets.ExceptWith(dropped);
        }
        var (strips, gorge) = CarveStrips(layout, rail, sign, pockets, dropped, broken?.Ride ?? 0, split?.Ride ?? 0);
        if (broken != null) strips = strips.Append(broken.Run).OrderBy(s => s.From).ToList(); // the break is its ride's strip
        if (split != null) strips = strips.Append(split.Crevasse).OrderBy(s => s.From).ToList(); // and the split's crevasse its ride's
        layout.Rail = rail with
        {
            Pockets = rail.Pockets.Where(p => !dropped.Contains(p)).ToList(), Strips = strips, Gorge = gorge, Break = broken, BreakWhy = why,
            Split = split, SplitWhy = splitWhy,
        };
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
    /// <see cref="RailRules.GorgeTo"/> of it, platform middle to platform middle, each run on its own side (inward where that
    /// is free, else outward): the gorge. Returns the strips in line order
    /// and the gorge's; the pockets cut away are added to <paramref name="dropped"/>. Rides <paramref name="skipRide"/> and
    /// <paramref name="skipSplit"/> (1-based; 0 for none) are left alone: the broken bridge, and the split's crevasse, is its strip.
    /// </summary>
    private static (List<RailSpan> Strips, List<RailSpan> Gorge) CarveStrips(FloorLayout layout, RailLine rail, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped, int skipRide, int skipSplit)
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
            if (k + 1 == skipRide || k + 1 == skipSplit) continue; // the broken bridge's ride, the split's
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
            if (k == last) { CutGorge(); continue; }
            // the side to cut: inward of the party lane when it has a run long enough
            var way = Runs(-1).Any(r => r.Count >= minimum) ? -1 : 1;
            var runs = Runs(way).Where(r => r.Count >= minimum).ToList();
            if (runs.Count == 0) continue;
            var longest = runs.OrderByDescending(r => r.Count).First(); // the earliest of the longest
            var cut = Math.Max(0, longest.Count - RailRules.StripMaxCells);
            Carve(longest.Skip(cut / 2).Take(RailRules.StripMaxCells).ToList(), false);

            // the maximal runs of consecutive cells a strip can lie beside on one side (-1 inward, +1 outward)
            List<List<(int I, GridPos[] Cells)>> Runs(int side)
            {
                var found = new List<List<(int, GridPos[])>>();
                List<(int, GridPos[])>? run = null;
                for (var i = from; i <= to; i++)
                {
                    if (StripCells(layout, rail, enemy, pockets, taken, sign, i, side) is { } cells)
                    {
                        if (run == null) found.Add(run = new());
                        run.Add((i, cells));
                    }
                    else run = null;
                }
                return found;
            }

            // the gorge: each cell inward of the party lane when that side is free there, else outward of the enemy lane; a
            // run keeps one side and ends where the side changes, and each run of at least the minimum is cut as soon as it
            // ends, so a later one (the far leg of a bulge, say) sees it as the drop it is and keeps its wall
            void CutGorge()
            {
                List<(int I, GridPos[] Cells)>? run = null;
                var runSide = 0;
                for (var i = from; i <= to + 1; i++)
                {
                    var side = -1;
                    var cells = i <= to ? StripCells(layout, rail, enemy, pockets, taken, sign, i, side) : null;
                    if (cells == null && i <= to) cells = StripCells(layout, rail, enemy, pockets, taken, sign, i, side = 1);
                    if (cells != null && (run == null || side == runSide))
                    {
                        run ??= new();
                        runSide = side;
                        run.Add((i, cells));
                        continue;
                    }
                    if (run != null && run.Count >= minimum) Carve(run, true);
                    run = null;
                    // the side changed here: a run starts on the new one, judged again now the run before it is cut
                    if (cells != null && StripCells(layout, rail, enemy, pockets, taken, sign, i, side) is { } again)
                    {
                        run = new() { (i, again) };
                        runSide = side;
                    }
                }
            }
        }
        return (strips, gorge);

        void Carve(List<(int I, GridPos[] Cells)> run, bool inGorge)
        {
            foreach (var (i, cells) in run)
                foreach (var c in cells)
                {
                    if (pockets.Contains(c)) dropped.Add(c);
                    layout[c] = Cell.Chasm;
                    layout.SetLevel(c, layout.LevelOf(rail.Party[i])); // the drop's lip is the lane's floor beside it
                }
            var strip = new RailSpan(run[0].I, run[^1].I);
            strips.Add(strip);
            if (inGorge) gorge.Add(strip);
        }
    }

    /// <summary>
    /// The broken bridge on 1-based ride <paramref name="ride"/> (<see cref="FloorSpec.BrokenBridgeRide"/>), and why: "refused"
    /// on the cavern's ride, the last ride or a ride the line does not have; "none" when the ride has no fitting run; else "ok".
    /// The run is the longest outward straight run a strip can lie beside (<see cref="StripCells"/> with way +1, its enemy
    /// twins taken by nothing) of at least <see cref="RailRules.BreakMinCells"/> cells, on the ride's party-lane cells
    /// <see cref="RailRules.StripPlatformGap"/> in from each platform and within <see cref="RailRules.BreakFrom"/> to
    /// <see cref="RailRules.BreakTo"/> of it, platform middle to platform middle; ties go to the later run, and a longer one
    /// is clipped to its middle <see cref="RailRules.StripMaxCells"/>. Each run cell's outward cells at t = 2, 3 become chasm
    /// at the lane's level; its enemy twin becomes a <see cref="BridgeKind.Deck"/>, but for the middle one (party index
    /// From + Count / 2), which becomes a chasm at the lane's level: the gap, still in <see cref="RailLine.Enemy"/>. The party
    /// lane stays solid. The pockets cut away are added to <paramref name="dropped"/>.
    /// </summary>
    private static (RailBreak? Break, string Why) CarveBreak(FloorLayout layout, RailLine rail, int ride, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)
    {
        var k = ride - 1;
        if (k >= rail.Stops.Count - 2) return (null, "refused"); // the last ride, or past it
        var w = RideWindow(rail, k, RailRules.BreakFrom, RailRules.BreakTo);
        if (rail.Cavern is { } cavern && cavern.From > w.After && cavern.From < w.Before) return (null, "refused"); // the cavern's ride
        if (LongestOutwardRun(layout, rail, sign, pockets, w.From, w.To, RailRules.BreakMinCells) is not { } best) return (null, "none");
        var cut = Math.Max(0, best.Count - RailRules.StripMaxCells);
        best = best.Skip(cut / 2).Take(RailRules.StripMaxCells).ToList();
        var span = new RailSpan(best[0].I, best[^1].I);
        var gap = span.From + best.Count / 2;
        CutOutward(layout, rail, best, pockets, dropped);
        foreach (var (i, _) in best)
        {
            var twin = SlagTwin(rail, i, sign);
            if (i != gap) { layout.Bridges[twin] = BridgeKind.Deck; continue; }
            layout[twin] = Cell.Chasm; // the gap: the slag line's 4 m with no deck
            layout.SetLevel(twin, layout.LevelOf(rail.Party[i]));
        }
        return (new RailBreak(ride, span, gap), "ok");
    }

    /// <summary>
    /// The split's crevasse on 1-based ride <paramref name="ride"/> (<see cref="FloorSpec.SplitRide"/>), and why: "refused" on
    /// the broken bridge's ride (<paramref name="breakRide"/>, the one asked for), the last ride or a ride the line does not have;
    /// "none" when the ride has no fitting run; else "ok". The run is the longest outward straight run a strip can lie beside
    /// (<see cref="StripCells"/> with way +1, its enemy twins taken by nothing) of at least <see cref="RailRules.SplitMinCells"/>
    /// cells, on the ride's party-lane cells <see cref="RailRules.StripPlatformGap"/> in from each platform and within
    /// <see cref="RailRules.SplitFrom"/> to <see cref="RailRules.SplitTo"/> of it, platform middle to platform middle, and on the
    /// cavern's ride ending <see cref="RailRules.SplitCavernGap"/> cells or more before the cavern span; ties go to the later run.
    /// The run's last <see cref="RailRules.SplitCells"/> cells have their outward cells at t = 2, 3 cut to chasm at the lane's
    /// level: the crevasse. Both lanes stay solid floor with no deck, and the points are the run cell before the cut. The pockets
    /// cut away are added to <paramref name="dropped"/>.
    /// </summary>
    private static (RailSplit? Split, string Why) CarveSplit(FloorLayout layout, RailLine rail, int ride, int breakRide, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)
    {
        var k = ride - 1;
        if (ride == breakRide || k >= rail.Stops.Count - 2) return (null, "refused"); // the broken bridge's ride, the last ride, or past it
        var (from, to) = SplitWindow(rail, k);
        if (LongestOutwardRun(layout, rail, sign, pockets, from, to, RailRules.SplitMinCells) is not { } best) return (null, "none");
        var cut = best.Skip(best.Count - RailRules.SplitCells).ToList();
        CutOutward(layout, rail, cut, pockets, dropped);
        var crevasse = new RailSpan(cut[0].I, cut[^1].I);
        return (new RailSplit(ride, crevasse, crevasse.From - 1), "ok");
    }

    /// <summary>
    /// The party-lane index window on 0-based ride <paramref name="k"/> (from stop k to stop k + 1): <c>After</c> and
    /// <c>Before</c> the last and first platform cells either side, and <c>From</c>..<c>To</c> the cells
    /// <see cref="RailRules.StripPlatformGap"/> in from each platform and within <paramref name="fromFraction"/> to
    /// <paramref name="toFraction"/> of the ride, platform middle to platform middle. Empty when From &gt; To.
    /// </summary>
    internal static (int After, int Before, int From, int To) RideWindow(RailLine rail, int k, float fromFraction, float toFraction)
    {
        var index = new Dictionary<GridPos, int>();
        for (var i = 0; i < rail.Party.Count; i++) index[rail.Party[i]] = i;
        var after = index[rail.Stops[k].Platform[^1]];
        var before = index[rail.Stops[k + 1].Platform[0]];
        var a = index[rail.Stops[k].Platform[0]] + rail.Stops[k].Platform.Count / 2;
        var b = before + rail.Stops[k + 1].Platform.Count / 2;
        var from = Math.Max(after + RailRules.StripPlatformGap, a + (int)Math.Ceiling(fromFraction * (b - a)));
        var to = Math.Min(before - RailRules.StripPlatformGap, a + (int)Math.Floor(toFraction * (b - a)));
        return (after, before, from, to);
    }

    /// <summary>
    /// The split's window on 0-based ride <paramref name="k"/>: <see cref="RideWindow"/> over <see cref="RailRules.SplitFrom"/>
    /// to <see cref="RailRules.SplitTo"/>, its end clamped on the cavern's ride to <see cref="RailRules.SplitCavernGap"/> cells
    /// before the cavern's first cell. Empty when From &gt; To.
    /// </summary>
    internal static (int From, int To) SplitWindow(RailLine rail, int k)
    {
        var w = RideWindow(rail, k, RailRules.SplitFrom, RailRules.SplitTo);
        var to = w.To;
        if (rail.Cavern is { } cavern && cavern.From > w.After && cavern.From < w.Before) to = Math.Min(to, cavern.From - RailRules.SplitCavernGap); // short of the cavern
        return (w.From, to);
    }

    /// <summary>
    /// The longest run of consecutive party-lane cells in <paramref name="from"/>..<paramref name="to"/> an outward strip can lie
    /// beside (<see cref="StripCells"/> with way +1) whose enemy twins nothing takes (<see cref="TakenCells"/>), of at least
    /// <paramref name="minCells"/>, with each cell's two outward cells; ties go to the later run. Null when there is none.
    /// </summary>
    private static List<(int I, GridPos[] Cells)>? LongestOutwardRun(FloorLayout layout, RailLine rail, int sign, HashSet<GridPos> pockets, int from, int to, int minCells)
    {
        var enemy = rail.Enemy.ToHashSet();
        var taken = TakenCells(layout);
        List<(int I, GridPos[] Cells)>? best = null, run = null;
        for (var i = from; i <= to + 1; i++)
        {
            var cells = i <= to ? StripCells(layout, rail, enemy, pockets, taken, sign, i, 1) : null;
            if (cells != null && !taken.Contains(SlagTwin(rail, i, sign)))
            {
                (run ??= new()).Add((i, cells));
                continue;
            }
            if (run != null && run.Count >= minCells && (best == null || run.Count >= best.Count)) best = run; // ties: the later
            run = null;
        }
        return best;
    }

    /// <summary>
    /// Cut each run cell's outward cells (t = 2, 3, from <see cref="LongestOutwardRun"/>) to chasm at the party lane's level,
    /// so the drop's lip is the lane's floor beside it; the pockets cut away are added to <paramref name="dropped"/>.
    /// </summary>
    private static void CutOutward(FloorLayout layout, RailLine rail, IEnumerable<(int I, GridPos[] Cells)> run, HashSet<GridPos> pockets, HashSet<GridPos> dropped)
    {
        foreach (var (i, cells) in run)
        {
            var level = layout.LevelOf(rail.Party[i]);
            foreach (var c in cells)
            {
                if (pockets.Contains(c)) dropped.Add(c);
                layout[c] = Cell.Chasm;
                layout.SetLevel(c, level);
            }
        }
    }

    /// <summary>The enemy (slag) lane's twin of party-lane cell <paramref name="i"/>: one step to <see cref="RailSide"/> of its heading.</summary>
    private static GridPos SlagTwin(RailLine rail, int i, int sign) => rail.Party[i] + RailSide(rail.Party[i + 1] - rail.Party[i], sign);

    /// <summary>
    /// The two cells a strip cuts beside party-lane cell <paramref name="i"/> (inward of the party lane at t = -1, -2 for
    /// <paramref name="way"/> -1; outward of the enemy lane at t = 2, 3 for +1), or null when it cannot. The lane is straight
    /// there with its enemy twin beside it, and the stretch is flat at either level (0 or 1): both lane cells at one level and
    /// neither a ramp, no ramp within a cell of the strip, and nothing walkable at another level round it. In the
    /// block round the two cells there is only that lane's own stretch, its pockets and rock, with rock behind them, and
    /// nothing under anything (a cellar, a tunnel, the under layer). Each cell is rock or a pocket of the lane, outside every
    /// room, not reserved or taken (<see cref="TakenCells"/>); once cut it takes the lane's level, so the drop's lip is the lane's floor.
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
        var level = layout.LevelOf(rail.Party[i]);
        if (layout.LevelOf(twin) != level) return null;
        var cells = new[] { lane + u, lane + Times(u, 2) };
        // no ramp in the strip, beside it or within a cell of it, and neither lane cell beside the strip a ramp
        if (layout.Ramps.ContainsKey(rail.Party[i]) || layout.Ramps.ContainsKey(twin)) return null;
        foreach (var c in cells)
            for (var oy = -1; oy <= 1; oy++)
                for (var ox = -1; ox <= 1; ox++)
                {
                    var q = new GridPos(c.X + ox, c.Y + oy);
                    if (layout[q] == Cell.Ramp || layout.Ramps.ContainsKey(q)) return null;
                }
        // the block round the strip's two cells: the lane's own cells (m = 0), the strip's rows (m = 1, 2), the rock behind (m = 3)
        for (var along = -1; along <= 1; along++)
            for (var m = 0; m <= 3; m++)
            {
                var q = lane + Times(d, along) + Times(u, m);
                if (layout.IsUnder(q)) return null;
                if (layout.IsWalkable(q) && layout.LevelOf(q) != level) return null; // nothing walkable at another level round it
                var ok = m switch
                {
                    0 => own(q) || Rock(layout, q),
                    1 => Rock(layout, q) || pockets.Contains(q),
                    _ => Rock(layout, q),
                };
                if (!ok) return null;
            }
        foreach (var c in cells)
        {
            var pocket = pockets.Contains(c);
            if (layout.RoomAt(c) != null || taken.Contains(c)) return null;
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
                foreach (var q in new[] { plank + Times(across, t), plank - Times(across, t) })
                {
                    layout[q] = Cell.Chasm;
                    layout.SetLevel(q, 0); // the drop's lip is the hall's floor
                }
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
