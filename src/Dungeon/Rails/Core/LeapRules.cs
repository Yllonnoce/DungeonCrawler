using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's leap to the slag cart (plan 13, Rulings 1 and 9), its geometry: the stretch runs from the cavern's start
/// (<see cref="RailLine.Cavern"/>'s near edge on the party track) + <see cref="LeapIn"/> to the drop's near edge, and is dead under
/// <see cref="MinStretch"/>; the drop (<see cref="DropSpan"/>) is the slag deck's <see cref="DropCells"/> cells, <see cref="DropFromEnd"/>
/// short of the cavern's end. Engine-free.
/// </summary>
public static class LeapRules
{
    /// <summary>The leap stretch starts this far past the cavern's start (the near edge of its first party cell).</summary>
    public const float LeapIn = 10f;
    /// <summary>A stretch shorter than this (its start to the drop's near edge) is dead: the leap offers nothing.</summary>
    public const float MinStretch = 30f;
    /// <summary>The slag deck cells that give way at the drop.</summary>
    public const int DropCells = 2;
    /// <summary>The drop's last cell stands this many cells short of the cavern's last.</summary>
    public const int DropFromEnd = 2;

    /// <summary>
    /// The drop as party-lane indices (their slag-lane twins are the cells that go): the <see cref="DropCells"/> cells ending
    /// <see cref="DropFromEnd"/> short of <paramref name="cavern"/>'s end; null with no cavern, or one too short to hold it
    /// (under <see cref="DropFromEnd"/> + <see cref="DropCells"/> cells: the generator's shortest is <see cref="RailRules.CavernCells"/>).
    /// </summary>
    public static RailSpan? DropSpan(RailSpan? cavern)
        => cavern is { } c && c.To - c.From + 1 >= DropFromEnd + DropCells ? new RailSpan(c.To - DropFromEnd - DropCells + 1, c.To - DropFromEnd) : null;

    /// <summary>
    /// The step from a cavern's party-lane cell to its slag twin: the enemy lane's 4-neighbour of the span's first cell (the span is
    /// straight, so every span cell's twin lies the same step aside); null with no such neighbour.
    /// </summary>
    public static GridPos? SlagSide(FloorLayout layout, RailLine rail, RailSpan cavern)
    {
        var at = rail.Party[cavern.From];
        foreach (var n in at.Neighbours4())
            if (layout.IsRail(n) && !layout.IsPartyRail(n)) return n - at;
        return null;
    }

    /// <summary>
    /// Is the cavern flat (plan 13, Ruling 2; a cavern that is not gets neither a live bridge nor a live leap)? Every span cell of both
    /// lanes at one level and none a ramp, and neither lane's cell just before <c>From</c> or just after <c>To</c> a ramp cell (the bed
    /// eases 0.5 m into a flat neighbour: <c>RailBed.SurfaceAt</c>). False with no cavern, no line, or no slag twin.
    /// </summary>
    public static bool Flat(FloorLayout layout, RailSpan? cavern)
    {
        if (cavern is not { } c || layout.Rail is not { } rail || c.From < 0 || c.From > c.To || c.To >= rail.Party.Count) return false;
        if (SlagSide(layout, rail, c) is not { } side) return false;
        bool Ramp(GridPos p) => layout.Ramps.ContainsKey(p) || layout[p] == Cell.Ramp;
        var level = layout.LevelOf(rail.Party[c.From]);
        for (var i = c.From; i <= c.To; i++)
            foreach (var p in new[] { rail.Party[i], rail.Party[i] + side })
                if (Ramp(p) || layout.LevelOf(p) != level) return false;
        foreach (var i in new[] { c.From - 1, c.To + 1 })
            if (i >= 0 && i < rail.Party.Count && (Ramp(rail.Party[i]) || Ramp(rail.Party[i] + side))) return false;
        return true;
    }
}
