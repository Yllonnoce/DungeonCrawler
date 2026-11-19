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
    /// never has a wagon, so its <paramref name="cageHolds"/> is false.
    /// </summary>
    public static bool Held(CartMode mode, bool halted, int stopIndex, int stopCount, bool waived, bool cageHolds)
        => mode == CartMode.Held && !halted && stopIndex < stopCount - 1 && !waived && cageHolds;

    /// <summary>A hero may leave the prisoners behind: seated in the held cart and on their feet.</summary>
    public static bool MayLeaveBehind(bool seatedHere, bool downed, bool cageHeld) => seatedHere && !downed && cageHeld;
}
