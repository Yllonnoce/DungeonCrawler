using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The stop-wall ambushers (plan 16 Task 4, spec section 2): on the train's arrival at cage stops 1-4, 2 / 3 / 3 / 4 creatures burst out
/// of the stop room's walls and rubble <see cref="SpringAfterStepOff"/> s after the first hero steps off, or <see cref="SpringAfterArrival"/> s
/// after the arrival when nobody does; each point once ever (saved with the corridor ambushes' <c>SprungAmbushes</c>). The placement is seeded
/// (<c>seed ^ </c><see cref="Salt"/>) and built identically on every peer, a list apart from <see cref="AmbushRules.Place"/>'s corridor points
/// (appended after them). Every number is a tunable pinned by a unit test. Engine-free.
/// </summary>
public static class StopAmbushRules
{
    /// <summary>The ambushers at each stop, by stop index (the landing none; past stop 4 none).</summary>
    public static readonly int[] Counts = { 0, 2, 3, 3, 4 };
    /// <summary>They burst this long after the first hero steps off at the stop...</summary>
    public const float SpringAfterStepOff = 1.5f;
    /// <summary>...or this long after the arrival, when nobody has stepped off.</summary>
    public const float SpringAfterArrival = 6f;
    /// <summary>A trigger stands at least this far (metres, cell centre to cell centre) from the stop's cage.</summary>
    public const float CageClear = 4f;
    /// <summary>Triggers stand at least this many cells apart (Manhattan, as the corridor points' spacing).</summary>
    public const int Apart = 3;
    /// <summary>The placement's salt on the floor seed.</summary>
    public const int Salt = 0x5709A;
    /// <summary>Metres per cell (cell centre to cell centre).</summary>
    private const float CellMetres = 4f;

    /// <summary>Stop <paramref name="stop"/>'s ambushers (0 outside 1..4).</summary>
    public static int Count(int stop) => stop >= 1 && stop < Counts.Length ? Counts[stop] : 0;

    /// <summary>
    /// The stop's points burst now: <see cref="SpringAfterStepOff"/> after the first step-off once a hero has stepped off, else
    /// <see cref="SpringAfterArrival"/> after the arrival. <paramref name="sinceFirstStepOff"/> is NaN while nobody has stepped off.
    /// </summary>
    public static bool Due(float sinceArrival, float sinceFirstStepOff)
        => float.IsNaN(sinceFirstStepOff) ? sinceArrival >= SpringAfterArrival : sinceFirstStepOff >= SpringAfterStepOff;

    /// <summary>
    /// The stop-wall points of every stop in <paramref name="stops"/> (index = stop number), in stop order, each with its
    /// <see cref="AmbushPoint.Stop"/> set: up to <see cref="Count"/> a stop, fewer when its room has too few candidates. A trigger is a
    /// walkable <see cref="Cell.Floor"/> cell inside the stop's room, reachable on foot from the platform (<see cref="StopSiegeRules.Reachable"/>:
    /// never across the line, a bridge or a drop, never a vault or the boss arena), not reserved (the line, its pockets, rubble), not water,
    /// not a platform cell and not under a ledge, with a cover by <see cref="AmbushRules.CoverFor"/>, at least <see cref="CageClear"/> m from
    /// the stop's cage (<paramref name="cages"/>, by stop) and <see cref="Apart"/> cells from every point already placed. Rubble candidates go
    /// first, each kind shuffled; the creature is <see cref="StopSiegeRules.Pick"/>'s (one cinder brute a stop at most), from the same rng.
    /// </summary>
    public static List<AmbushPoint> Place(FloorLayout layout, IReadOnlyList<RailStop> stops, IReadOnlyDictionary<int, GridPos> cages, int seed)
    {
        var points = new List<AmbushPoint>();
        var rng = new Random(seed ^ Salt);
        var candidates = new List<(GridPos Trigger, GridPos Cover, bool Rubble, int Key)>();
        for (var k = 0; k < stops.Count; k++)
        {
            var count = Count(k);
            var stop = stops[k];
            if (count <= 0 || stop.Room < 0 || stop.Room >= layout.Rooms.Count) continue;
            var room = layout.Rooms[stop.Room];
            var reach = StopSiegeRules.Reachable(layout, room, stop, null);
            GridPos? cage = cages.TryGetValue(k, out var c) ? c : null;
            candidates.Clear();
            for (var y = room.Y; y <= room.Bottom; y++)
                for (var x = room.X; x <= room.Right; x++)
                {
                    var p = new GridPos(x, y);
                    if (!Candidate(layout, p, stop, reach)) continue;
                    if (cage is { } at && Metres(p, at) < CageClear) continue;
                    if (AmbushRules.CoverFor(layout, p) is not { } cover) continue;
                    candidates.Add((p, cover.Cover, cover.Rubble, rng.Next()));
                }
            // rubble first (the mound is the better hiding place), then the shuffle
            candidates.Sort((a, b) => a.Rubble != b.Rubble ? (a.Rubble ? -1 : 1) : a.Key.CompareTo(b.Key));
            var placed = 0;
            var bruteTaken = false;
            foreach (var cand in candidates)
            {
                if (placed >= count) break;
                var apart = true;
                foreach (var q in points) if (Math.Abs(q.Trigger.X - cand.Trigger.X) + Math.Abs(q.Trigger.Y - cand.Trigger.Y) < Apart) { apart = false; break; }
                if (!apart) continue;
                var id = StopSiegeRules.Pick(k, bruteTaken, rng.NextDouble());
                if (id == StopSiegeRules.Brute) bruteTaken = true;
                points.Add(new AmbushPoint(cand.Trigger, cand.Cover, id) { FromRubble = cand.Rubble, Stop = k });
                placed++;
            }
        }
        return points;
    }

    private static bool Candidate(FloorLayout layout, GridPos p, RailStop stop, HashSet<GridPos> reach)
    {
        if (!layout.Contains(p) || layout[p] != Cell.Floor || !layout.IsWalkable(p) || !reach.Contains(p)) return false;
        if (layout.IsReserved(p) || layout.IsWater(p) || layout.IsUnder(p)) return false;
        foreach (var q in stop.Platform) if (q == p) return false;
        return true;
    }

    /// <summary>Cell centre to cell centre, in metres.</summary>
    private static float Metres(GridPos a, GridPos b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy) * CellMetres;
    }
}
