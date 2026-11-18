using DungeonCrawler.Net;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What the server's cart sender and a client's applier decide from the flags. Engine-free.</summary>
public static class CartSendRules
{
    /// <summary>
    /// A wreck and a spare's takeover in the same tick leave the cart Held at the spare's stop with Wrecked cleared, so a client
    /// would never see the wreck. Then one extra state goes first with Wrecked set (<paramref name="firstFlags"/> =
    /// <paramref name="flagsNow"/> | Wrecked); otherwise false and <paramref name="firstFlags"/> = <paramref name="flagsNow"/>.
    /// </summary>
    public static bool NeedsWreckFrame(bool wreckedThisTick, bool tookOverThisTick, byte flagsNow, out byte firstFlags)
    {
        firstFlags = flagsNow;
        if (!wreckedThisTick || !tookOverThisTick) return false;
        firstFlags = (byte)(flagsNow | (byte)CartFlags.Wrecked);
        return true;
    }

    /// <summary>The party's cart shows (Hidden: a wreck with no spare left). Its coupled wagons stay shown, as the server's HideWreck leaves them.</summary>
    public static bool Shown(byte flags) => (flags & (byte)CartFlags.Hidden) == 0;

    /// <summary>Wrecked was clear and is set now: the client plays the tumble (once).</summary>
    public static bool WreckedRose(byte before, byte now) => (before & (byte)CartFlags.Wrecked) == 0 && (now & (byte)CartFlags.Wrecked) != 0;
}
