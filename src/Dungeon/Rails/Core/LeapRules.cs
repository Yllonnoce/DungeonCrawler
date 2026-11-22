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
    /// <see cref="DropFromEnd"/> short of <paramref name="cavern"/>'s end; null with no cavern.
    /// </summary>
    public static RailSpan? DropSpan(RailSpan? cavern)
        => cavern is { } c ? new RailSpan(c.To - DropFromEnd - DropCells + 1, c.To - DropFromEnd) : null;
}
