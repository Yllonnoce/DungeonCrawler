using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>
/// Secrets worth finding: every other secret alcove (the even indices) tries to open into a carved 3-5 cell room behind its
/// wall, holding a secret-table chest, a plaque, and on one seeded non-boss floor per tier a merchant's stall. Engine-free.
/// </summary>
public static class SecretRoomRules
{
    /// <summary>Every Nth alcove (by index) wants a room: 2 means the even ones.</summary>
    public const int EveryNth = 2;
    public const int MinSide = 3, MaxSide = 5, HiddenStallCount = 6;
    /// <summary>The room may sit up to this many cells of passage behind the alcove when nothing fits flush against it.</summary>
    public const int MaxGap = 3;

    public static bool WantsRoom(int secretIndex) => secretIndex % EveryNth == 0;

    /// <summary>How many of a floor's secrets should be rooms: half, rounded up.</summary>
    public static int RoomTarget(int secretCount) => (secretCount + 1) / 2;

    /// <summary>The tier's hidden-stall floor: the second, third or fourth floor of the tier (never its first, never the boss floor), by game seed.</summary>
    public static int HiddenStallFloor(int tierIndex, ulong gameSeed)
        => 5 * tierIndex + 2 + new Random(unchecked((int)Rng.Mix(gameSeed ^ 0x51DEUL ^ (ulong)tierIndex))).Next(3);

    /// <summary>A room-edge cell farthest from the doorway that is not the chest.</summary>
    public static GridPos PlaqueCell(Room room, GridPos doorway, GridPos chest)
        => room.Cells().Where(c => c != chest && (c.X == room.X || c.X == room.Right || c.Y == room.Y || c.Y == room.Bottom))
            .OrderByDescending(c => c.ManhattanDistance(doorway)).ThenBy(c => c.Y).ThenBy(c => c.X).First();

    /// <summary>The free cell farthest from the doorway (not the chest, not the plaque).</summary>
    public static GridPos MerchantCell(Room room, GridPos doorway, GridPos chest, GridPos plaque)
        => room.Cells().Where(c => c != chest && c != plaque)
            .OrderByDescending(c => c.ManhattanDistance(doorway)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
}
