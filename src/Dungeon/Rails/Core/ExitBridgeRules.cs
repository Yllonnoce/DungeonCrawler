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
    /// Where a body steps out by party-lane index <paramref name="laneIndex"/> (Ruling 12): today's <see cref="CartSeats.ExitCell"/>
    /// when that cell may take a body (<see cref="CrevasseRules.SetDownOk"/>); otherwise the at-stop exit cell of the nearest party index,
    /// searched outward i+1, i−1, i+2, i−2, … (ties to the higher index), whose result holds a body. None anywhere: today's cell.
    /// </summary>
    public static GridPos SafeExitCell(FloorLayout l, IReadOnlyList<GridPos> lane, int laneIndex, Func<GridPos, bool> free, bool atStop)
    {
        var cell = CartSeats.ExitCell(lane, laneIndex, free, atStop);
        if (CrevasseRules.SetDownOk(l, cell) || lane.Count == 0) return cell;
        var at = Math.Clamp(laneIndex, 0, lane.Count - 1);
        for (var d = 1; d < lane.Count; d++)
            foreach (var j in new[] { at + d, at - d })
            {
                if (j < 0 || j >= lane.Count) continue;
                var c = CartSeats.ExitCell(lane, j, free, atStop: true);
                if (CrevasseRules.SetDownOk(l, c)) return c;
            }
        return cell;
    }
}
