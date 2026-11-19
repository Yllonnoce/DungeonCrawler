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
}
