namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// The crevasses (<see cref="FloorSpec.Crevasses"/>): drops cut into the rock beside a railed line once it is laid. The
/// cavern on the line's reserved span (<see cref="RailLine.Cavern"/>) is the cavern's reach cut to <see cref="Cell.Chasm"/>,
/// with both lanes over the span on a timber deck. No rng: the line alone fixes where they go.
/// </summary>
public static partial class FloorGenerator
{
    /// <summary>
    /// Cut the crevasses into a laid line's rock; a no-op unless <see cref="FloorSpec.Crevasses"/> and the line kept a
    /// cavern. Every cell of the cavern's reach (<see cref="CavernReach"/>) that is rock or a pocket becomes a chasm (the
    /// pockets leave <see cref="RailLine.Pockets"/>), both lanes over the span become <see cref="BridgeKind.Deck"/> bridges,
    /// the rock beside the drop is walled and <see cref="FloorLayout.ChasmCount"/> set.
    /// </summary>
    internal static void CarveChasms(FloorSpec spec, FloorLayout layout)
    {
        if (!spec.Crevasses || layout.Rail is not { Cavern: { } span } rail) return;
        var sign = LineSign(rail);
        var window = rail.Party.Skip(span.From).Take(span.To - span.From + 1).ToList();
        var side = RailSide(window[1] - window[0], sign);
        var pockets = rail.Pockets.ToHashSet();
        var dropped = new HashSet<GridPos>();
        foreach (var c in CavernReach(window, sign))
        {
            var pocket = pockets.Contains(c);
            if (!pocket && !Rock(layout, c)) continue;
            layout[c] = Cell.Chasm;
            if (pocket) dropped.Add(c);
        }
        if (dropped.Count > 0) layout.Rail = rail with { Pockets = rail.Pockets.Where(p => !dropped.Contains(p)).ToList() };
        foreach (var c in window)
        {
            layout.Bridges[c] = BridgeKind.Deck;
            layout.Bridges[c + side] = BridgeKind.Deck; // the enemy twin
        }
        RaiseWalls(layout);
        layout.ChasmCount = layout.Cells.Count(c => c == Cell.Chasm);
    }

    /// <summary>
    /// The sign the line was routed with: the enemy lane's first cell is the twin of the party lane's first, which
    /// <see cref="Twin"/> lays as a straight cell, one step to <see cref="RailSide"/>.
    /// </summary>
    internal static int LineSign(RailLine rail)
        => rail.Enemy[0] - rail.Party[0] == RailSide(rail.Party[1] - rail.Party[0], 1) ? 1 : -1;
}
