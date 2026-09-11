using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// How the gas rises on a Toxic floor. Engine-free and deterministic: given the layout and the seconds since the
/// party arrived, every room and corridor cell has a level from 0 (clear) to 1 (full). The low rooms go first, from
/// the start room outward, so the gas chases the party; the high rooms only after every low room, so the high ground
/// is a refuge for a while; the exit lies low and far, so it gasses late but it does gas.
/// </summary>
public static class GasRules
{
    /// <summary>Seconds before the first room starts to gas.</summary>
    public const float Grace = 45f;
    /// <summary>Seconds the front takes to reach the next room out.</summary>
    public const float Spread = 14f;
    /// <summary>Seconds after the last low room before the high ground goes.</summary>
    public const float HighDelay = 50f;
    /// <summary>Seconds from a room's arrival to full gas.</summary>
    public const float Thicken = 40f;
    public const float Unreachable = float.PositiveInfinity;

    /// <summary>When the gas reaches each room (index = room index), in seconds since arrival.</summary>
    public static float[] Arrivals(FloorLayout layout)
    {
        var rooms = layout.Rooms;
        var hops = FloorGenerator.RoomHops(layout, layout.RoomAt(layout.UpStairs));
        var maxHop = hops.Length == 0 ? 0 : Math.Max(0, hops.Max());
        var lowHops = new int[rooms.Count];
        var maxLow = 0;
        for (var i = 0; i < rooms.Count; i++)
        {
            lowHops[i] = hops[i] < 0 ? maxHop : hops[i];
            if (layout.LevelOf(rooms[i].Center) == 0) maxLow = Math.Max(maxLow, lowHops[i]);
        }
        var arrivals = new float[rooms.Count];
        for (var i = 0; i < rooms.Count; i++)
        {
            var low = layout.LevelOf(rooms[i].Center) == 0;
            arrivals[i] = low ? Grace + Spread * lowHops[i] : Grace + Spread * maxLow + HighDelay + Spread * lowHops[i];
        }
        return arrivals;
    }

    /// <summary>0 (clear) to 1 (full) for a room that the gas reaches at <paramref name="arrival"/>.</summary>
    public static float Level(float arrival, float seconds) => float.IsInfinity(arrival) ? 0f : Math.Clamp((seconds - arrival) / Thicken, 0f, 1f);

    /// <summary>0 clear, 1 thin, 2 thick, 3 lethal.</summary>
    public static int Danger(float level) => level <= 0f ? 0 : level < 0.34f ? 1 : level < 0.67f ? 2 : 3;

    public static string Describe(int danger) => danger switch { 0 => "clear", 1 => "thin", 2 => "thick", _ => "lethal" };

    /// <summary>One second of breathing gas at this danger: a little poison damage now and the poison that follows, scaled like a trap.</summary>
    public static AttackProfile Tick(int floor, int danger)
    {
        var s = TierRules.DamageScale(floor) * TierRules.Lethality;
        var d = Math.Clamp(danger, 0, 3);
        return new AttackProfile
        {
            MinDamage = 1.5f * d * s, MaxDamage = 2.5f * d * s, Type = DamageType.Poison, Kind = AttackKind.Spell,
            OnHit = d == 0 ? Array.Empty<StatusApplication>() : new[] { new StatusApplication(StatusKind.Poison, 6, 1.5f * d * s) },
        };
    }

    /// <summary>Seconds until the whole floor is full: the last arrival plus the thickening.</summary>
    public static float FullAt(float[] arrivals) => arrivals.Length == 0 ? 0f : arrivals.Where(a => !float.IsInfinity(a)).DefaultIfEmpty(0f).Max() + Thicken;
}

/// <summary>Every walkable cell's arrival time: a room's cells take the room's, a corridor cell that of the nearest room by walking.</summary>
public sealed class GasField
{
    private readonly float[] _arrival;
    private readonly int _width, _height;
    public float[] RoomArrivals { get; }

    private GasField(int width, int height, float[] arrival, float[] rooms) { _width = width; _height = height; _arrival = arrival; RoomArrivals = rooms; }

    public static GasField Build(FloorLayout layout)
    {
        var rooms = GasRules.Arrivals(layout);
        var arrival = new float[layout.Width * layout.Height];
        Array.Fill(arrival, GasRules.Unreachable);
        var queue = new Queue<GridPos>();
        for (var i = 0; i < layout.Rooms.Count; i++)
            foreach (var p in layout.Rooms[i].Cells())
            {
                if (!layout.IsWalkable(p)) continue;
                var idx = p.Y * layout.Width + p.X;
                if (rooms[i] < arrival[idx]) { arrival[idx] = rooms[i]; queue.Enqueue(p); }
            }
        // multi-source spread: each corridor cell takes the earliest arrival it can be walked to from
        var dirs = new[] { new GridPos(1, 0), new GridPos(-1, 0), new GridPos(0, 1), new GridPos(0, -1) };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var here = arrival[p.Y * layout.Width + p.X];
            foreach (var d in dirs)
            {
                var n = p + d;
                if (!layout.InBounds(n.X, n.Y) || !layout.IsWalkable(n) || !layout.CanStep(p, n)) continue;
                if (layout.RoomAt(n) != null) continue; // a room's cells keep the room's own time; only the ways between take the spread
                var idx = n.Y * layout.Width + n.X;
                if (here < arrival[idx]) { arrival[idx] = here; queue.Enqueue(n); }
            }
        }
        return new GasField(layout.Width, layout.Height, arrival, rooms);
    }

    public float ArrivalAt(GridPos p) => p.X < 0 || p.Y < 0 || p.X >= _width || p.Y >= _height ? GasRules.Unreachable : _arrival[p.Y * _width + p.X];
    public float LevelAt(GridPos p, float seconds) => GasRules.Level(ArrivalAt(p), seconds);
    public int DangerAt(GridPos p, float seconds) => GasRules.Danger(LevelAt(p, seconds));
}
