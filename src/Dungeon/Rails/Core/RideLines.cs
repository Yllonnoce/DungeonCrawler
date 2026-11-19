using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Which line a cart moment calls for: the keys of Grisel's <c>NpcPlan.Calls</c> on the railed jailbreak floor. The
/// words live in the floor's plan, where the voice pipeline reads them; this only names the moment. Engine-free.
/// </summary>
public static class RideLines
{
    /// <summary>Her word at a cage wagon she opens.</summary>
    public const string CageCall = "cage";

    /// <summary>Her call as the cart leaves stop <paramref name="stopLeft"/> (0-based): "ride1" for the first ride, and so on. Null for any other moment.</summary>
    public static string? GriselCallFor(CartEventKind kind, int stopLeft) => kind == CartEventKind.Departed ? $"ride{stopLeft + 1}" : null;

    /// <summary>
    /// Grisel's call for the ride from <paramref name="stopLeft"/>, still waiting to be said, still names the right cage: the cart is still
    /// at or riding from the stop it left (<paramref name="stopIndex"/> is that stop, halted on the line or not), or it stands held
    /// (<paramref name="held"/>, not <paramref name="halted"/> past it) at the next stop it rode to. Gone on, or set back by a spare: stale.
    /// </summary>
    public static bool CallStillDue(int stopIndex, int stopLeft, bool held, bool halted)
        => stopIndex == stopLeft || (stopIndex == stopLeft + 1 && held && !halted);

    /// <summary>
    /// Who calls a moment of the ride from a cart, by party order: the first hero seated in it and up whom no player controls
    /// (a companion), else the first player's hero seated in it and up; -1 when nobody in it can. Each entry is one party member.
    /// </summary>
    public static int CallerIndex(IReadOnlyList<(bool InCart, bool Downed, bool Companion)> heroes)
    {
        var player = -1;
        for (var i = 0; i < heroes.Count; i++)
        {
            var (inCart, downed, companion) = heroes[i];
            if (!inCart || downed) continue;
            if (companion) return i;
            if (player < 0) player = i;
        }
        return player;
    }

    /// <summary>The hero's call for a cart moment, or null: the boarding's countdown starting is "all aboard". The warnings are called where they are judged.</summary>
    public static BarkTrigger? BarkFor(CartEventKind kind) => kind == CartEventKind.Boarding ? BarkTrigger.AllAboard : null;

    /// <summary>
    /// The orc's taunt for a cart moment, or null: a departure from one of the first stops (<paramref name="stopLeft"/> at most
    /// <paramref name="stopCount"/> − 3: rides 1–3 of five stops; the last ride's opening belongs to the golem) starts the ride,
    /// and a wreck is gloated over. The run-overs, the derails, the golem and the buffer are told where they happen.
    /// </summary>
    public static TauntTrigger? TauntFor(CartEventKind kind, int stopLeft, int stopCount) => kind switch
    {
        CartEventKind.Departed when stopLeft >= 0 && stopLeft <= stopCount - 3 => TauntTrigger.RideStart,
        CartEventKind.Wrecked => TauntTrigger.CartWrecked,
        _ => null,
    };
}
