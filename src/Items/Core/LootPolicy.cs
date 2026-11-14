namespace DungeonCrawler.Items;

/// <summary>
/// Whether a companion picks an item up as it walks past. In solo the companions are the player's own hands and take
/// what fits. In co-op the bag is shared with people: a bot leaves them room and never snatches what a player is
/// walking up to.
/// </summary>
public static class LootPolicy
{
    /// <summary>Free bag cells a co-op bot leaves for the players: one row of the bag.</summary>
    public const int CoopReserveCells = 8;
    /// <summary>A co-op bot leaves an item this close to a human player for that player.</summary>
    public const float CoopHumanClaimMetres = 3f;

    /// <param name="coop">The party has human players besides this companion (a server's bots).</param>
    /// <param name="essential">Keys and quest items: the floor needs them taken, so anyone takes them.</param>
    /// <param name="mergesFully">The item joins stacks already in the bag without a new cell (arrows, potions carried).</param>
    /// <param name="hasRoom">The bag can take it at all (<see cref="Inventory.HasRoomFor"/>).</param>
    /// <param name="freeCells">Empty bag cells now.</param>
    /// <param name="nearestHumanMetres">Distance from the item to the nearest standing human player (infinity when none).</param>
    public static bool ShouldTake(bool coop, bool essential, bool mergesFully, bool hasRoom, int freeCells, float nearestHumanMetres)
    {
        if (!hasRoom) return false;
        if (!coop) return true;
        if (essential) return true;
        if (nearestHumanMetres < CoopHumanClaimMetres) return false;
        return mergesFully || freeCells > CoopReserveCells;
    }

    /// <summary>Whether <paramref name="item"/> fits into stacks already in <paramref name="bag"/> without taking a new cell.</summary>
    public static bool MergesFully(Inventory bag, ItemInstance item)
    {
        if (!item.Stackable) return false;
        var room = bag.Items.Where(s => s.Stackable && s.DefId == item.DefId).Sum(s => Math.Max(0, s.MaxStack - s.StackCount));
        return room >= item.StackCount;
    }
}
