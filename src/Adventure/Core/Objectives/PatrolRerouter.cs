namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// Keeps a patrol route walkable when gates shift: the route is trimmed to the rooms the patrol can still reach and,
/// if fewer than two are left, grown along reachable neighbours so the patrol keeps walking. Engine-free.
/// </summary>
public static class PatrolRerouter
{
    /// <param name="route">Room indices the patrol walks, in order.</param>
    /// <param name="reachable">Rooms the patrol can walk to now.</param>
    /// <param name="neighbours">Room adjacency (index → neighbour indices).</param>
    /// <param name="home">The room the patrol stands in, always kept.</param>
    public static List<int> Reroute(IReadOnlyList<int> route, ISet<int> reachable, IReadOnlyList<IReadOnlyList<int>> neighbours, int home)
    {
        var kept = route.Where(reachable.Contains).ToList();
        if (kept.Count == 0 && reachable.Contains(home)) kept.Add(home);
        // grow: walk reachable neighbours of what we keep until two rooms stand on the route
        var guard = 0;
        while (kept.Count < 2 && guard++ < 64)
        {
            var added = false;
            foreach (var r in kept.ToList())
            {
                if (r < 0 || r >= neighbours.Count) continue;
                foreach (var n in neighbours[r])
                    if (reachable.Contains(n) && !kept.Contains(n)) { kept.Add(n); added = true; break; }
                if (added) break;
            }
            if (!added) break;
        }
        return kept;
    }

    /// <summary>True when the route as walked has changed in any way.</summary>
    public static bool Changed(IReadOnlyList<int> before, IReadOnlyList<int> after) => !before.SequenceEqual(after);
}
