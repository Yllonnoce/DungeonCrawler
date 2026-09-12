using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>An ambusher's hiding place: it bursts out of <see cref="Cover"/> (a wall or a rubble mound) onto <see cref="Trigger"/> when the party steps near.</summary>
public sealed record AmbushPoint(GridPos Trigger, GridPos Cover, string EnemyId)
{
    public bool FromRubble { get; init; }
}

/// <summary>
/// Extra creatures on every floor beyond the planned ones, hidden along the corridors and springing out when the party
/// passes. Engine-free: the placement is seeded, so a floor always hides the same ambushes.
/// </summary>
public static class AmbushRules
{
    public const int OnFloorOne = 3;
    public const int Deepest = 10;
    /// <summary>Cells the party must be from the arrival stairs before an ambush may sit there.</summary>
    public const int ClearOfStairs = 8;
    /// <summary>Corridor cells between two ambushes, so they do not chain into one brawl.</summary>
    public const int Spacing = 6;

    /// <summary>About three on floor 1 rising to about ten on floor 20; endless floors keep the deepest count.</summary>
    public static int Count(int floor)
    {
        if (floor <= 0) return 0;
        var f = Math.Min(floor, TierRules.FinalFloor) - 1;
        return OnFloorOne + f * (Deepest - OnFloorOne) / (TierRules.FinalFloor - 1);
    }

    /// <summary>
    /// Pick hiding places along the corridors: a walkable corridor cell (not in any room, not a ramp, dry, not reserved)
    /// well away from the arrival stairs, with a rubble mound or a solid wall beside it to burst from. Points keep
    /// <see cref="Spacing"/> apart; fewer than asked come back on a floor with too little corridor.
    /// </summary>
    public static List<AmbushPoint> Place(FloorLayout layout, IReadOnlyList<string> enemyIds, int seed, int count)
    {
        var points = new List<AmbushPoint>();
        if (count <= 0 || enemyIds.Count == 0) return points;
        var rng = new Random(seed ^ 0x5A17B);
        var candidates = new List<(GridPos Trigger, GridPos Cover, bool Rubble)>();
        for (var y = 1; y < layout.Height - 1; y++)
            for (var x = 1; x < layout.Width - 1; x++)
            {
                var p = new GridPos(x, y);
                if (layout[p] != Cell.Floor || layout.RoomAt(p) != null || layout.IsWater(p) || layout.IsReserved(p) || layout.IsUnder(p)) continue;
                if (Math.Abs(p.X - layout.UpStairs.X) + Math.Abs(p.Y - layout.UpStairs.Y) < ClearOfStairs) continue;
                GridPos? wall = null, rubble = null;
                foreach (var n in Neighbours(p))
                {
                    if (layout.Rubble.Contains(n)) rubble ??= n;
                    else if (!layout.IsWalkable(n) && layout[n] != Cell.Secret && layout.LevelOf(n) <= layout.LevelOf(p)) wall ??= n;
                }
                if (rubble is { } r) candidates.Add((p, r, true));
                else if (wall is { } w) candidates.Add((p, w, false));
            }
        // rubble first (the mound is the better hiding place), then a shuffle so the picks spread over the floor
        var ordered = candidates.OrderBy(c => c.Rubble ? 0 : 1).ThenBy(_ => rng.Next()).ToList();
        foreach (var c in ordered)
        {
            if (points.Count >= count) break;
            if (points.Any(q => Math.Abs(q.Trigger.X - c.Trigger.X) + Math.Abs(q.Trigger.Y - c.Trigger.Y) < Spacing)) continue;
            points.Add(new AmbushPoint(c.Trigger, c.Cover, enemyIds[rng.Next(enemyIds.Count)]) { FromRubble = c.Rubble });
        }
        return points;
    }

    private static IEnumerable<GridPos> Neighbours(GridPos p)
    {
        yield return new GridPos(p.X + 1, p.Y);
        yield return new GridPos(p.X - 1, p.Y);
        yield return new GridPos(p.X, p.Y + 1);
        yield return new GridPos(p.X, p.Y - 1);
    }
}

/// <summary>
/// The boss's reinforcements: after a grace period on a floor, portals open in explored rooms behind the party and
/// let a few creatures through, more often the longer the party lingers. Reaching the stairs ends it; a slain tier
/// boss silences the tier. Engine-free pacing and site choice so the pressure curve is unit tested.
/// </summary>
public static class PortalRules
{
    public const float GraceSeconds = 90f;
    public const float FirstInterval = 60f;
    public const float LastInterval = 20f;
    /// <summary>Seconds of pressure over which the interval shrinks from first to last.</summary>
    public const float RampSeconds = 360f;
    public const int MaxAlive = 6;
    public const int MaxAliveHeavy = 10;
    /// <summary>The distance (in cells) from the party a portal likes to open at.</summary>
    public const int PreferredDistance = 12;

    /// <summary>Intensity 1 is normal; 2 halves every wait; 0 (or less) switches reinforcements off.</summary>
    public static bool Enabled(float intensity) => intensity > 0.01f;
    public static float Grace(float intensity) => GraceSeconds / Math.Max(0.25f, intensity);

    public static float Interval(float secondsUnderPressure, float intensity)
    {
        var t = Math.Clamp(secondsUnderPressure / RampSeconds, 0f, 1f);
        return (FirstInterval + (LastInterval - FirstInterval) * t) / Math.Max(0.25f, intensity);
    }

    /// <summary>One creature at first, up to three as the pressure builds, one more on heavy; never more than four.</summary>
    public static int GroupSize(float secondsUnderPressure, float intensity, Random rng)
    {
        var t = Math.Clamp(secondsUnderPressure / RampSeconds, 0f, 1f);
        var size = 1 + (int)Math.Floor(t * 2.999f);
        if (intensity >= 1.5f) size++;
        if (rng.NextDouble() < 0.25) size++;
        return Math.Clamp(size, 1, 4);
    }

    public static int MaxAliveFor(float intensity) => intensity >= 1.5f ? MaxAliveHeavy : MaxAlive;

    /// <summary>The floor whose boss commands this tier's reinforcements.</summary>
    public static int BossFloorFor(int floor) => Math.Min(TierRules.FinalFloor, (TierRules.TierIndex(floor) + 1) * TierRules.FloorsPerTier);

    /// <summary>
    /// Where the next portal opens: an explored room (a third of it seen) holding no adventurer, not the boss arena or a
    /// vault, about <see cref="PreferredDistance"/> cells from the nearest adventurer and for preference behind the
    /// party (closer to the arrival stairs than the lead is). Null when nothing qualifies yet.
    /// </summary>
    public static (Room Room, GridPos Cell)? ChooseSite(FloorLayout layout, IReadOnlySet<GridPos> seen, IReadOnlyList<GridPos> party, Random rng)
    {
        if (party.Count == 0) return null;
        var lead = party[0];
        var leadFromUp = Dist(lead, layout.UpStairs);
        (Room Room, GridPos Cell)? best = null;
        var bestScore = float.MaxValue;
        foreach (var room in layout.Rooms)
        {
            if (room == layout.BossArena || layout.Vaults.Any(v => v.Room == room)) continue;
            if (party.Any(room.Contains)) continue;
            var cells = room.Cells().ToList();
            if (cells.Count == 0 || cells.Count(seen.Contains) * 3 < cells.Count) continue;
            // never on the stairs (the start and exit rooms keep theirs in the middle) nor on the cell at their foot, nor on a chest or a ledge
            var cell = cells.Where(c => layout.IsWalkable(c) && !layout.IsWater(c) && !layout.IsReserved(c) && layout[c] == Cell.Floor
                    && Dist(c, layout.UpStairs) >= 2 && Dist(c, layout.DownStairs) >= 2 && !layout.ChestSpawns.Contains(c)
                    && !layout.Ledges.Any(l => l.Cells.Contains(c) || l.RampBottom == c || l.RampTop == c))
                .OrderBy(c => Dist(c, room.Center)).Cast<GridPos?>().FirstOrDefault();
            if (cell == null) continue;
            var nearest = party.Min(p => Dist(room.Center, p));
            if (nearest < 4) continue;
            var behind = Dist(room.Center, layout.UpStairs) < leadFromUp;
            var score = Math.Abs(nearest - PreferredDistance) + (behind ? 0f : 10f) + (float)rng.NextDouble();
            if (score < bestScore) { bestScore = score; best = (room, cell.Value); }
        }
        return best;
    }

    private static int Dist(GridPos a, GridPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
