using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon;

/// <summary>
/// Fog-of-war state for one floor: which cells the party has seen. Engine-free so it is testable and can be
/// saved as part of FloorState. Reveal is a bounded flood fill through walkable cells, so walls block sight.
/// </summary>
public sealed class ExplorationMap
{
    private readonly HashSet<GridPos> _seen = new();

    /// <summary>How many walkable steps away from the party a cell can be and still be revealed.</summary>
    public int RevealDepth { get; init; } = 9;

    public IReadOnlySet<GridPos> Seen => _seen;
    public bool IsSeen(GridPos p) => _seen.Contains(p);

    /// <summary>Reveal cells around <paramref name="from"/>. Returns how many cells were newly revealed.</summary>
    public int Reveal(FloorLayout layout, GridPos from)
    {
        if (!layout.IsWalkable(from)) return 0;

        var before = _seen.Count;
        var frontier = new Queue<(GridPos p, int d)>();
        var visited = new HashSet<GridPos> { from };
        frontier.Enqueue((from, 0));

        ReadOnlySpan<GridPos> steps = stackalloc GridPos[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        while (frontier.Count > 0)
        {
            var (p, d) = frontier.Dequeue();
            MarkWithWalls(layout, p);
            if (d >= RevealDepth) continue;
            foreach (var s in steps)
            {
                var n = p + s;
                if (layout.IsWalkable(n) && visited.Add(n)) frontier.Enqueue((n, d + 1));
            }
        }
        return _seen.Count - before;
    }

    /// <summary>Mark a walkable cell and the wall cells around it, so room outlines appear on the map.</summary>
    private void MarkWithWalls(FloorLayout layout, GridPos p)
    {
        _seen.Add(p);
        for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var n = new GridPos(p.X + ox, p.Y + oy);
                if (layout[n] == Cell.Wall) _seen.Add(n);
            }
    }

    /// <summary>Restore from a saved list of cells.</summary>
    public void Load(IEnumerable<GridPos> cells)
    {
        _seen.Clear();
        foreach (var c in cells) _seen.Add(c);
    }
}
