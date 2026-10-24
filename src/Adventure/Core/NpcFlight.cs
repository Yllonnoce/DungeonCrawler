using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Where a frightened NPC runs. Engine-free: the floor is a walkability predicate, so the tests can drive it and
/// <see cref="Npc"/> feeds it the real layout.
/// </summary>
public static class NpcFlight
{
    /// <summary>How many cells away a flight goal is picked.</summary>
    public const int FleeSteps = 4;
    /// <summary>An alive enemy closer than this (metres) sends a standing NPC running.</summary>
    public const float FleeRange = 9f;
    /// <summary>Once scared, the NPC stays wary of any enemy within this (metres): it keeps its distance and will not walk home while one is this close to home.</summary>
    public const float SafeRange = 16f;
    /// <summary>Seconds without an enemy within <see cref="SafeRange"/> before the NPC calms down and walks back to its spot.</summary>
    public const float CalmSeconds = 3f;
    /// <summary>Seconds between re-picking the goal while the threat keeps moving.</summary>
    public const float RepickSeconds = 0.8f;

    /// <summary>
    /// The open cell that gains the most distance from <paramref name="threat"/>, looking out to <paramref name="steps"/> cells
    /// around <paramref name="npc"/> in sixteen directions (the farthest open cell in each). Null when no open cell is farther
    /// from the threat than the NPC already is. Cells for which <paramref name="taken"/> is true (another NPC's spot or goal) are skipped,
    /// so frightened neighbours scatter instead of piling onto one cell.
    /// </summary>
    /// <param name="danger">A cell a lethal hazard is arming over is no refuge.</param>
    public static GridPos? PickFleeCell(GridPos npc, GridPos threat, Func<GridPos, bool> open, int steps, Func<GridPos, bool>? taken = null, Func<GridPos, float>? danger = null)
    {
        GridPos? best = null;
        var bestScore = double.NegativeInfinity;
        var now = Distance(npc, threat);
        for (var dir = 0; dir < 16; dir++)
        {
            var angle = dir * Math.PI * 2 / 16;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);
            for (var s = steps; s >= 1; s--)
            {
                var cell = new GridPos(npc.X + (int)Math.Round(dx * s), npc.Y + (int)Math.Round(dy * s));
                if (cell == npc || !open(cell) || (taken != null && taken(cell)) || (danger != null && danger(cell) > 0f)) continue;   // a cell another NPC is heading for is not a refuge
                var gain = Distance(cell, threat) - now;
                if (gain <= 0) break;                 // this way leads toward the threat
                var score = gain + s * 0.25;          // farther is better, a longer run slightly more so
                if (score > bestScore) { bestScore = score; best = cell; }
                break;                                // the farthest open cell in this direction is enough
            }
        }
        return best;
    }

    private static double Distance(GridPos a, GridPos b) => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));
}
