using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The floor's safe room: one dead end near the stairs-to-stairs route, sealed by a door no creature passes, where the
/// party rests, heals faster and trades with the merchant. Engine-free: which room, its door, where the merchant
/// stands and where the shut-out creatures gather are all decided here, so the generator and the tests agree.
/// </summary>
public static class SafeRoomRules
{
    public const float GraceSeconds = 20f;
    public const float IntervalSeconds = 40f;
    public const float EmptyResetSeconds = 30f;
    public const float RegenMultiplier = 6f;
    public const float DoorOpenRadius = 2.5f;
    public const float DoorCloseRadius = 3f;
    public const float DoorCloseDelay = 2f;
    public const int CapAlive = 8;
    /// <summary>How far (Manhattan cells) the safe room's door may be from the stairs-to-stairs route.</summary>
    public const int RouteDistanceCells = 10;

    private const int GatherMin = 4;
    private const int GatherMax = 8;

    /// <summary>
    /// The room to seal: not a stairs room or the arena, not already a vault, with exactly one entrance whose sealing
    /// keeps the down stairs reachable and cuts no other room off, and that entrance within <see cref="RouteDistanceCells"/>
    /// of the route. Of those, the one whose entrance is nearest the route's midpoint (ties: lower room index); -1 when none.
    /// </summary>
    public static int Choose(FloorLayout layout)
    {
        var rooms = layout.Rooms;
        if (rooms.Count == 0) return -1;
        var route = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, maxExpansions: 20000);
        if (route == null || route.Count == 0) return -1;
        var mid = route[route.Count / 2];
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);

        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == upRoom || room == downRoom || room == layout.BossArena || layout.Vaults.Any(v => v.Room == room)) continue;
            var entrances = FloorGenerator.Entrances(layout, room);
            if (entrances.Count != 1) continue;
            var door = entrances[0];
            if (route.Min(c => c.ManhattanDistance(door)) > RouteDistanceCells) continue;
            var distance = door.ManhattanDistance(mid);
            if (distance >= bestDistance) continue; // ties keep the lower index
            var reach = layout.Reachable(layout.UpStairs, p => p == door);
            if (!reach.Contains(layout.DownStairs)) continue;
            if (rooms.Any(r => r != room && !reach.Contains(r.Center))) continue; // another room would be cut off too
            best = i;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>The room's single entrance: the cell the safe door stands in.</summary>
    public static GridPos DoorCell(FloorLayout layout, int room) => FloorGenerator.Entrances(layout, layout.Rooms[room])[0];

    public static IEnumerable<GridPos> Cells(FloorLayout layout) => layout.Rooms[layout.SafeRoom].Cells();

    /// <summary>Where the merchant stands: the room cell farthest from the door (ties: lower Y, then lower X).</summary>
    public static GridPos MerchantCell(FloorLayout layout)
    {
        var door = layout.SafeRoomDoor;
        return Cells(layout).OrderByDescending(c => c.ManhattanDistance(door)).ThenBy(c => c.Y).ThenBy(c => c.X).First();
    }

    /// <summary>
    /// Where the creatures shut out of the safe room gather: a walkable cell outside it at walking distance 4..8 from
    /// the door, preferring one off the door's row and column (out of a straight line of sight), nearest first. When the
    /// corridor offers no cell that far, the nearest walkable cell outside the room. Null when the door leads nowhere.
    /// </summary>
    public static GridPos? GatherCell(FloorLayout layout)
    {
        if (layout.SafeRoom < 0) return null;
        var room = layout.Rooms[layout.SafeRoom];
        var door = layout.SafeRoomDoor;
        var distance = new Dictionary<GridPos, int> { [door] = 0 };
        var order = new List<GridPos>();
        var queue = new Queue<GridPos>();
        queue.Enqueue(door);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var d = distance[p];
            if (d >= GatherMax) continue;
            foreach (var n in p.Neighbours4())
            {
                if (room.Contains(n) || distance.ContainsKey(n) || !layout.CanStep(p, n)) continue;
                distance[n] = d + 1;
                order.Add(n);
                queue.Enqueue(n);
            }
        }
        if (order.Count == 0) return null;
        var inRange = order.Where(c => distance[c] >= GatherMin).ToList();
        if (inRange.Count == 0) return order[0];
        foreach (var c in inRange) if (c.X != door.X && c.Y != door.Y) return c;
        return inRange[0];
    }
}
