namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The cage hold (plan 7): the party's cart does not count down at a stop whose cage wagon is still locked with living crew, until
/// a seated player holds interact to leave the prisoners behind. Engine-free; the server (or single player) decides it, a client
/// learns it from <c>CartFlags.CageHeld</c>.
/// </summary>
public static class CageHoldRules
{
    /// <summary>The seated prompt while the cage holds the cart: a tap still gets out, a held press leaves the prisoners behind.</summary>
    public const string SeatedPrompt = "Get out (hold to leave the prisoners behind)";

    /// <summary>A stop's cage wagon holds the cart: it is locked and someone in it still lives (a downed crew member counts; the dead do not).</summary>
    public static bool Holds(bool cageOpen, int livingCrew) => !cageOpen && livingCrew > 0;

    /// <summary>
    /// The cart is held by its stop's cage: it stands Held at a stop (not halted on the line) short of the end of the line, nobody
    /// has left that stop's prisoners behind (<paramref name="waived"/>), and the stop's cage holds (<see cref="Holds"/>). Stop 0
    /// never has a wagon, so its <paramref name="cageHolds"/> is false. A cart standing past its stop as the lift's takeover (<paramref name="standingPast"/>,
    /// plan 15 Ruling 27) is held by no cage.
    /// </summary>
    public static bool Held(CartMode mode, bool halted, int stopIndex, int stopCount, bool waived, bool cageHolds, bool standingPast = false)
        => mode == CartMode.Held && !halted && !standingPast && stopIndex < stopCount - 1 && !waived && cageHolds;

    /// <summary>
    /// The stops whose prisoners count as left behind after a load (nothing about them is saved): every cage stop before the one the
    /// cart is restored at (stop 0 has no wagon), since a cart restored past a stop left it. The cage at the restored stop holds again.
    /// An open cage ignores the waiver (its crew are freed), so in effect only the passed stops whose cage is still locked count as left.
    /// </summary>
    public static IEnumerable<int> LeftOnLoad(int stopIndex)
    {
        for (var s = 1; s < stopIndex; s++) yield return s;
    }

    /// <summary>A hero may leave the prisoners behind: seated in the held cart and on their feet.</summary>
    public static bool MayLeaveBehind(bool seatedHere, bool downed, bool cageHeld) => seatedHere && !downed && cageHeld;
}
