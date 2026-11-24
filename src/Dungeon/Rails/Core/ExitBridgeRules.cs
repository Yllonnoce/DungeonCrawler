using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The exit island's bridge at runtime (plan 15, spec B.2; Rulings 10-12): the last straight party-lane run before the exit platform,
/// cut to chasm with its track kept (rails and sleepers, no ballast, no deck, no footing). Carts roll on it; a body on foot falls.
/// <list type="bullet">
/// <item>While the party's train rolls with any car over it, at any speed, nobody gets out and nobody moves car to car (<see cref="Holds"/>):
/// a train braked to a stop on it holds its riders until it rolls.</item>
/// <item>A plain wreck with the cart's front at or past its start (<see cref="WreckInside"/>) spends no spare: every rider is set down
/// inside the island, past the far edge (<see cref="SetDownPastFarEdge"/>) or where it already is.</item>
/// <item>No set-down ever lands on a drop (<see cref="SafeExitCell"/>), on any floor.</item>
/// </list>
/// Engine-free.
/// </summary>
public static class ExitBridgeRules
{
    /// <summary>The lava's molten surface lies this far under the lip (the island's lava regions, the exit bridge's posts).</summary>
    public const float LavaDepth = 3f;

    /// <summary>The log line of a wreck at or past the bridge's start (Ruling 11).</summary>
    public const string WreckLog = "The cart is wrecked on the bridge. You scramble across onto the island.";

    /// <summary>
    /// The bridge holds the party's riders: the train is Rolling (<paramref name="rolling"/>: <c>CartMode.Rolling</c>, at any speed), its
    /// front past the bridge's near edge and its rear short of its far edge. NaN edges (no island): never.
    /// </summary>
    public static bool Holds(bool rolling, float frontS, float rearS, float fromS, float toS)
        => rolling && frontS > fromS && rearS < toS;

    /// <summary>A wreck with the cart's front at <paramref name="frontS"/> is at or past the bridge's start (<paramref name="fromS"/>; NaN: no island).</summary>
    public static bool WreckInside(float frontS, float fromS) => frontS >= fromS;

    /// <summary>
    /// A rider of an inside wreck standing at <paramref name="bodyS"/> (party S) is set down on the island past the far edge
    /// (<paramref name="toS"/>) when it is at or short of that edge; one already past it is set down where it is.
    /// </summary>
    public static bool SetDownPastFarEdge(float bodyS, float toS) => bodyS <= toS;

    /// <summary>
    /// Where a body steps out by party-lane index <paramref name="laneIndex"/> (Ruling 12, as fix round 1 ruled): today's
    /// <see cref="CartSeats.ExitCell"/> when that cell may take a body (<see cref="CrevasseRules.SetDownOk"/>); otherwise the at-stop exit cell
    /// of the nearest party index, searched outward i+1, i−1, i+2, i−2, … (ties to the higher index), whose result holds a body. None
    /// anywhere: today's cell. On an exit-island floor the body keeps its side (<paramref name="inside"/>: true inside the island, false
    /// outside, null for no side): every cell it is offered, in the exit cell's own window and in the search, is on that side, so no set-down
    /// ever carries a body across the lava.
    /// </summary>
    public static GridPos SafeExitCell(FloorLayout l, IReadOnlyList<GridPos> lane, int laneIndex, Func<GridPos, bool> free, bool atStop, bool? inside = null)
    {
        if (lane.Count == 0) return default;
        var sided = inside is { } side && l.Rail?.Island != null;
        bool OnSide(GridPos c) => !sided || l.InIsland(c) == inside;
        bool Holds(GridPos c) => CrevasseRules.SetDownOk(l, c) && OnSide(c);
        Func<GridPos, bool> offered = sided ? c => free(c) && OnSide(c) : free;
        var cell = CartSeats.ExitCell(lane, laneIndex, offered, atStop);
        if (Holds(cell)) return cell;
        var at = Math.Clamp(laneIndex, 0, lane.Count - 1);
        for (var d = 1; d < lane.Count; d++)
            foreach (var j in new[] { at + d, at - d })
            {
                if (j < 0 || j >= lane.Count) continue;
                var c = CartSeats.ExitCell(lane, j, offered, atStop: true);
                if (Holds(c)) return c;
            }
        return cell;
    }

    /// <summary>The side of the exit island a body at <paramref name="cell"/> stands on (true inside), or null on a floor without one.</summary>
    public static bool? SideOf(FloorLayout l, GridPos cell) => l.Rail?.Island == null ? null : l.InIsland(cell);

    /// <summary>
    /// The side of the exit island a body at <paramref name="here"/> stands on as a set-down reads it (true inside; null without an island): its cell's,
    /// and on the exit bridge's span (party-lane cells <c>Bridge.From..Bridge.To</c>, which <see cref="SideOf"/> counts as outside) the nearer end's, ties
    /// outside: so on floor 12 cells 311 and 312 are outside and 313 and 314 inside, and a rider set down from a car over the bridge never lands across it.
    /// </summary>
    public static bool? SideOfBody(FloorLayout l, IReadOnlyList<GridPos> lane, GridPos here)
    {
        if (l.Rail?.Island is not { } island) return null;
        for (var k = island.Bridge.From; k <= island.Bridge.To && k < lane.Count; k++)
            if (lane[k] == here) return k - island.Bridge.From > island.Bridge.To - k;
        return l.InIsland(here);
    }

    /// <summary>How far (cells, each way) a hold's step off the rails may look (<see cref="HoldStepOff"/>).</summary>
    public const int HoldStepReach = 4;

    /// <summary>
    /// Where a follower whose catch-up hold starts at <paramref name="here"/> steps first (Ruling 35 as plan 15 Task 8 ruled; fix round 1, M2 and M3):
    /// on a rail cell, the nearest cell (straight distance, ties by (Y, X)) off the rails, on its own side of the island (<paramref name="inside"/>), that
    /// may take a body (<see cref="CrevasseRules.SetDownOk"/>), holds no barrel (<see cref="FloorLayout.BreakableSpawns"/>), is not <paramref name="blocked"/>
    /// (a closed door or a solid prop) and not <paramref name="taken"/> (another body's), within <see cref="HoldStepReach"/> cells and reached from
    /// <paramref name="here"/> by 4-neighbour steps over such cells (rail cells may be walked over, never answered), all within that reach: so a held follower
    /// never stands where the lift stands again or a cart runs, and never steps across a drop, through a door or a prop, or past a barrel or a body (Task 9,
    /// the Task 8 re-review's N1). Null off the rails (it holds where it is) or with no such cell near.
    /// </summary>
    public static GridPos? HoldStepOff(FloorLayout l, GridPos here, bool inside, Func<GridPos, bool>? blocked = null, ISet<GridPos>? taken = null)
    {
        if (!l.IsRail(here)) return null;
        bool InReach(GridPos c) => Math.Abs(c.X - here.X) <= HoldStepReach && Math.Abs(c.Y - here.Y) <= HoldStepReach;
        var seen = new HashSet<GridPos> { here };
        var queue = new Queue<GridPos>();
        queue.Enqueue(here);
        GridPos? best = null;
        var bestD = int.MaxValue;
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var n in new[] { new GridPos(at.X + 1, at.Y), new GridPos(at.X - 1, at.Y), new GridPos(at.X, at.Y + 1), new GridPos(at.X, at.Y - 1) })
            {
                if (!InReach(n) || !seen.Add(n) || !CrevasseRules.SetDownOk(l, n)) continue; // walk-connected: only over cells that take a body,
                if (l.BreakableSpawns.Contains(n) || blocked?.Invoke(n) == true || taken?.Contains(n) == true) continue; // with nothing standing in them
                queue.Enqueue(n);
                if (l.IsRail(n) || l.InIsland(n) != inside) continue;
                var d = (n.X - here.X) * (n.X - here.X) + (n.Y - here.Y) * (n.Y - here.Y);
                if (d < bestD || d == bestD && best is { } b && (n.Y < b.Y || n.Y == b.Y && n.X < b.X)) { bestD = d; best = n; }
            }
        }
        return best;
    }
}
