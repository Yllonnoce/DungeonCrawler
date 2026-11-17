namespace DungeonCrawler.Dungeon.Generation;

/// <summary>Sizes shared by the generator and the rail routing.</summary>
public static class RailRules
{
    /// <summary>Cells between the grid edge and the ring's room circle on a railed floor: the line runs outside the rooms.</summary>
    public const int Band = 6;
    /// <summary>One side pocket every this many line cells.</summary>
    public const int PocketEvery = 3;
}

/// <summary>A stop on the line: a room the cart halts in, and the party-lane cells inside it, in line order.</summary>
public sealed record RailStop(int Room, IReadOnlyList<GridPos> Platform);

/// <summary>
/// The mine-cart line round a ring floor: two parallel ordered, 4-connected lanes from the start room to the exit room.
/// Stops[0] is the up-stairs room, Stops[^1] the down-stairs room.
/// </summary>
public sealed record RailLine(IReadOnlyList<GridPos> Party, IReadOnlyList<GridPos> Enemy, IReadOnlyList<RailStop> Stops, IReadOnlyList<GridPos> Pockets);
