using System.Numerics;

namespace DungeonCrawler.Dungeon.Generation;

/// <summary>
/// A 2-cell ramp on a rail lane: <paramref name="Index"/> is the lane index of its first cell in lane order, <paramref name="Low"/>
/// and <paramref name="High"/> its two cells (by <see cref="RampInfo.LowHeight"/>), <paramref name="Dir"/> the way it rises, and
/// <paramref name="Climb"/> holds when the lane runs along <paramref name="Dir"/> (the lane climbs it).
/// </summary>
public sealed record LaneRamp(int Index, GridPos Low, GridPos High, GridPos Dir, bool Climb);

/// <summary>The bed a rail lane lies on: the drawn floor under it, and the ramps along it. Engine-free: no Godot types.</summary>
public static class RailBed
{
    /// <summary>Every ramp on <paramref name="lane"/>: each pair of consecutive lane cells that are both ramp cells with the same Dir.</summary>
    public static IReadOnlyList<LaneRamp> RampsOn(FloorLayout layout, IReadOnlyList<GridPos> lane)
    {
        var ramps = new List<LaneRamp>();
        for (var i = 0; i + 1 < lane.Count; i++)
        {
            if (!layout.Ramps.TryGetValue(lane[i], out var a) || !layout.Ramps.TryGetValue(lane[i + 1], out var b) || a.Dir != b.Dir) continue;
            var (low, high) = a.LowHeight <= b.LowHeight ? (lane[i], lane[i + 1]) : (lane[i + 1], lane[i]);
            ramps.Add(new LaneRamp(i, low, high, a.Dir, lane[i + 1] - lane[i] == a.Dir));
            i++; // the pair is one ramp: its second cell starts none
        }
        return ramps;
    }

    /// <summary>The ramp's low end in world XZ: the edge of <see cref="LaneRamp.Low"/> away from the slope (the sag).</summary>
    public static Vector2 SagEdge(LaneRamp r, float cell = 4f) => Centre(r.Low, cell) - Dir(r) * (cell / 2f);

    /// <summary>The ramp's high end in world XZ: the edge of <see cref="LaneRamp.High"/> onto the upper floor (the crest).</summary>
    public static Vector2 CrestEdge(LaneRamp r, float cell = 4f) => Centre(r.High, cell) + Dir(r) * (cell / 2f);

    private static Vector2 Centre(GridPos g, float cell) => new(g.X * cell + cell / 2f, g.Y * cell + cell / 2f);
    private static Vector2 Dir(LaneRamp r) => new(r.Dir.X, r.Dir.Y);
}
