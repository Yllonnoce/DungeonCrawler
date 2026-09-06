namespace DungeonCrawler.Dungeon.Generation;

/// <summary>A cell plus which layer it is on: the main grid, or the under layer beneath high rooms.</summary>
public readonly record struct PathNode(GridPos Cell, bool Under);

/// <summary>A* over the floor grid (4-neighbour) honouring heights, ramps, pits and the under layer. Deterministic and engine-free.</summary>
public static class GridPathfinder
{
    private static readonly GridPos[] Steps = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>Main-layer path. <paramref name="blocked"/> marks closed doors; <paramref name="passable"/> marks opened secret walls.</summary>
    public static List<GridPos>? FindPath(FloorLayout layout, GridPos from, GridPos to, Func<GridPos, bool>? blocked = null, int maxExpansions = 6000, Func<GridPos, bool>? passable = null)
        => FindPath(layout, new PathNode(from, false), new PathNode(to, false), blocked, passable, maxExpansions)?.Select(n => n.Cell).ToList();

    public static List<PathNode>? FindPath(FloorLayout layout, PathNode from, PathNode to, Func<GridPos, bool>? blocked = null, Func<GridPos, bool>? passable = null, int maxExpansions = 6000)
    {
        if (!Exists(layout, from, passable) || !Exists(layout, to, passable)) return null;
        if (from == to) return new List<PathNode> { from };

        var open = new PriorityQueue<PathNode, int>();
        var cameFrom = new Dictionary<PathNode, PathNode>();
        var gScore = new Dictionary<PathNode, int> { [from] = 0 };
        open.Enqueue(from, from.Cell.ManhattanDistance(to.Cell));
        var expansions = 0;

        while (open.Count > 0 && expansions++ < maxExpansions)
        {
            var current = open.Dequeue();
            if (current == to) return Reconstruct(cameFrom, current);
            var g = gScore[current];
            foreach (var n in Neighbours(layout, current, passable))
            {
                if (n != to && blocked != null && !n.Under && blocked(n.Cell)) continue;
                var ng = g + 1;
                if (gScore.TryGetValue(n, out var old) && old <= ng) continue;
                gScore[n] = ng;
                cameFrom[n] = current;
                open.Enqueue(n, ng + n.Cell.ManhattanDistance(to.Cell));
            }
        }
        return null;
    }

    private static bool Exists(FloorLayout layout, PathNode n, Func<GridPos, bool>? passable)
        => n.Under ? layout.IsUnder(n.Cell) : layout.IsWalkable(n.Cell, passable?.Invoke(n.Cell) == true);

    public static IEnumerable<PathNode> Neighbours(FloorLayout layout, PathNode node, Func<GridPos, bool>? passable = null)
    {
        foreach (var d in Steps)
        {
            var n = node.Cell + d;
            if (node.Under)
            {
                if (layout.IsUnder(n)) yield return new PathNode(n, true);
                else if (layout[n] is Cell.Floor or Cell.Door && layout.LevelOf(n) == 0) yield return new PathNode(n, false);
            }
            else
            {
                // an opened secret wall is walkable from either side
                var open = passable != null && (passable(n) || passable(node.Cell));
                if (layout[n] != Cell.Pit && layout.CanStep(node.Cell, n, open))
                    yield return new PathNode(n, false);
                if (layout.IsUnder(n) && layout.LevelOf(node.Cell) == 0 && layout[node.Cell] is Cell.Floor or Cell.Door)
                    yield return new PathNode(n, true);
            }
        }
    }

    private static List<PathNode> Reconstruct(Dictionary<PathNode, PathNode> cameFrom, PathNode end)
    {
        var path = new List<PathNode> { end };
        while (cameFrom.TryGetValue(end, out var prev)) { path.Add(prev); end = prev; }
        path.Reverse();
        return path;
    }
}
