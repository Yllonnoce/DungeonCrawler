using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Run;

/// <summary>No Rest for the Hired: no safe room, the merchant in the open by the arrival stairs. Engine-free.</summary>
public static class NoRestRules
{
    /// <summary>At least this far (Manhattan cells) from the up stairs, so the party does not arrive on top of him.</summary>
    public const int MinDistance = 2;

    /// <summary>Where the merchant stands: a free cell of the start room, the nearest to the up stairs beyond <see cref="MinDistance"/>; failing that any free floor cell nearest the stairs.</summary>
    public static GridPos MerchantCell(FloorLayout layout)
    {
        var up = layout.UpStairs;
        int Dist(GridPos c) => Math.Abs(c.X - up.X) + Math.Abs(c.Y - up.Y);
        var room = layout.RoomAt(up);
        var candidates = room != null ? RoomCells.Free(layout, room) : new List<GridPos>();
        var pick = candidates.Where(c => Dist(c) >= MinDistance).OrderBy(Dist).ThenBy(c => c.Y).ThenBy(c => c.X).Cast<GridPos?>().FirstOrDefault();
        if (pick is { } p) return p;
        var any = new List<GridPos>();
        for (var y = 0; y < layout.Height; y++) for (var x = 0; x < layout.Width; x++) { var c = new GridPos(x, y); if (layout.IsWalkable(c) && c != up && Dist(c) >= MinDistance) any.Add(c); }
        return any.OrderBy(Dist).ThenBy(c => c.Y).ThenBy(c => c.X).FirstOrDefault(up);
    }
}
