using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Party;

/// <summary>
/// What happens to a party member or ally a lethal hazard caught: they are down and Mangled, which auto-revive never
/// touches. A living body within reach pulls them free once the hazard that took them is armed again; in co-op a body
/// nobody reaches in time is bound further ahead for the party to find. Engine-free.
/// </summary>
public static class MangledRules
{
    public const float RescueSeconds = 3f;
    public const float RescueRange = 2f;
    /// <summary>A rescuer who moves further than this from where they started the rescue interrupts it.</summary>
    public const float RescueInterruptMove = 1f;
    public const float BoundAfterSeconds = 30f;
    public const float ReviveHp = 1f;
    public const float ReviveStress = 25f;

    /// <summary>Calm auto-revive stands a downed member up; a Mangled one waits for hands.</summary>
    public static bool CanAutoRevive(bool mangled, bool hazardArmed) => !mangled;

    /// <summary>The party is wiped only when nobody is standing; a Mangled body counts as down, never as gone.</summary>
    public static bool IsWipe(IReadOnlyList<(bool Downed, bool Mangled)> members)
    {
        if (members.Count == 0) return false;
        foreach (var (downed, _) in members) if (!downed) return false;
        return true;
    }

    /// <summary>Where the safety valve puts a body nobody rescued in <see cref="BoundAfterSeconds"/> (<see cref="IslandBind"/>).</summary>
    public enum BindChoice
    {
        /// <summary>As on every floor: bound ahead on the walk to the down stairs (<see cref="BoundCell"/>), decided once.</summary>
        Ahead,
        /// <summary>It stays where it fell, freeable there, judged again every tick.</summary>
        StayHere,
        /// <summary>Bound on one of the exit island's set-down spots, still crushed, for anyone inside to free.</summary>
        Island,
    }

    /// <summary>
    /// On a floor with an exit island (plan 15) the walk from the up stairs to the down stairs crosses the lava, so a body left outside
    /// could never be bound ahead: while a human is up outside (<paramref name="upHumansOutside"/>) it stays where it fell, where they can free it; with
    /// none (the last of them rode the lift in) it is bound inside the island. A body inside, and every other floor, go on as before.
    /// </summary>
    public static BindChoice IslandBind(bool islandFloor, bool outsideIsland, int upHumansOutside)
        => !islandFloor || !outsideIsland ? BindChoice.Ahead : upHumansOutside > 0 ? BindChoice.StayHere : BindChoice.Island;

    /// <summary>
    /// A crushed player's banner (a client's HUD), <paramref name="sinceMangled"/> seconds on: the countdown to the safety valve, then where the body is
    /// and who can free it. On an island floor a body still outside after the valve is there only while a friend is up outside (<see cref="IslandBind"/>).
    /// </summary>
    public static string Banner(float sinceMangled, bool islandFloor, bool outsideIsland)
    {
        var left = BoundAfterSeconds - sinceMangled;
        if (left > 0f) return $"Crushed. Rescue in {Math.Ceiling(left):0}s, or wait to be found";
        return islandFloor && outsideIsland ? "Crushed. A friend out here can free you" : "Crushed. Bound in the dark ahead; a friend can free you";
    }

    public static bool RescueAllowed(bool hazardArmed, float distance, bool rescuerDowned)
        => hazardArmed && distance <= RescueRange && !rescuerDowned;

    /// <summary>
    /// Where a body nobody rescued is bound: the first critical-path cell inside a room the party has already seen,
    /// beyond the furthest living member along the path from the up stairs to the down stairs. Null when nothing
    /// ahead of them is explored; the body then stays where it fell.
    /// </summary>
    public static GridPos? BoundCell(FloorLayout layout, IReadOnlySet<GridPos> explored, IEnumerable<GridPos> livingCells, HashSet<GridPos> criticalPath)
    {
        var ordered = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs);
        if (ordered == null || ordered.Count == 0) return null;
        var furthest = -1;
        foreach (var living in livingCells)
        {
            // a member off the path counts from the path cell nearest them
            var best = -1; var bestD = int.MaxValue;
            for (var i = 0; i < ordered.Count; i++)
            {
                var d = Math.Abs(ordered[i].X - living.X) + Math.Abs(ordered[i].Y - living.Y);
                if (d < bestD) { bestD = d; best = i; }
            }
            furthest = Math.Max(furthest, best);
        }
        for (var i = furthest + 1; i < ordered.Count; i++)
        {
            var c = ordered[i];
            if (criticalPath.Contains(c) && explored.Contains(c) && layout.RoomAt(c) != null) return c;
        }
        return null;
    }
}
