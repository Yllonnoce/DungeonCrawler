using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Run;

/// <summary>
/// Full Occupancy: a quarter more creatures per rank, added to the resolved floor contents (the shipped plans are
/// pre-written, so a spec multiplier alone would never touch the Story). Copies of existing non-boss, surface
/// placements, chosen round-robin from a seeded shuffle, each on a free cell of the same room, appended as loners.
/// Rolled from the floor seed so peers agree. Engine-free.
/// </summary>
public static class OccupancyRules
{
    public const int SeedSalt = 0x0CC0;

    /// <summary>How many to add for <paramref name="count"/> creatures at a rank: round(n × 0.25 × rank), halves to even.</summary>
    public static int Extra(int count, int rank) => rank <= 0 ? 0 : (int)Math.Round(count * ClauseRules.FullOccupancyPerRank * (double)rank);

    /// <summary>Adds the extra placements; returns how many were added (0 at rank 0 or when no free cell is left).</summary>
    public static int Apply(FloorContents contents, FloorLayout layout, int rank, int seed)
    {
        var templates = contents.Enemies.Where(e => !e.Under && e.SpawnIndex >= 0).ToList(); // the copies stand on the surface; the count is of every creature
        var wanted = Extra(contents.Enemies.Count, rank);
        if (wanted <= 0 || templates.Count == 0) return 0;
        var rng = new Random(seed ^ SeedSalt);
        var order = templates.OrderBy(_ => rng.Next()).ToList();
        var used = new HashSet<GridPos>(contents.Enemies.Select(e => e.Cell));
        var added = 0;
        for (var i = 0; i < wanted * 4 && added < wanted; i++) // a few passes: a crowded room yields its template's turn to the next
        {
            var t = order[i % order.Count];
            var room = layout.RoomAt(t.Cell);
            if (room == null) continue;
            var free = RoomCells.Free(layout, room).Where(c => !used.Contains(c)).OrderBy(c => Math.Abs(c.X - t.Cell.X) + Math.Abs(c.Y - t.Cell.Y)).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
            if (free.Count == 0) continue;
            var cell = free[rng.Next(Math.Min(free.Count, 4))];
            used.Add(cell);
            contents.Enemies.Add(new EnemyPlacement(contents.Enemies.Count, t.EnemyId, cell, false));
            added++;
        }
        return added;
    }
}
