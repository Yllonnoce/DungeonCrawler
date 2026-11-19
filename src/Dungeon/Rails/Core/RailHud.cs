namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// What the cart's HUD line is told: the party cart as this peer shows it (the server's or single player's own state, a client's
/// flags and countdown), and the chaser when the local hero sits in it. <see cref="Boarding"/>: a countdown runs, with
/// <see cref="BoardingLeft"/> seconds left; <see cref="Plating01"/> is the plating's share of full. <see cref="ChaserBoardingLeft"/> is −1
/// when the chaser's countdown does not run. <see cref="CageHeld"/>: the cart stands at a stop whose locked cage holds it (plan 7).
/// </summary>
public readonly record struct RailHudState(CartMode Mode, bool Hidden, bool Halted, bool LineBlocked, bool GateClear, bool Boarding,
    float BoardingLeft, float Plating01, int StopIndex, int StopCount, bool InChaser = false, CartMode ChaserMode = CartMode.Held,
    float ChaserBoardingLeft = -1f, bool CageHeld = false);

/// <summary>The cart's HUD line from <see cref="RailHudState"/>. Engine-free.</summary>
public static class RailHud
{
    /// <summary>
    /// The line's words, or null when nothing is shown: the chaser's line while the local hero rides it (however far the party cart is);
    /// else nothing for a hidden party cart (a wreck no spare replaced), else the party cart's state.
    /// </summary>
    public static string? Text(in RailHudState s)
    {
        if (s.InChaser)
        {
            if (s.ChaserBoardingLeft >= 0f) return $"Spare cart — departs in {Ceil(s.ChaserBoardingLeft)}";
            return s.ChaserMode == CartMode.Rolling ? "Spare cart — after the train" : "Spare cart — behind the train";
        }
        if (s.Hidden) return null;
        if (s.Mode == CartMode.Wrecked) return "Cart — wrecked";
        if (s.Halted && s.LineBlocked) return "Cart — line blocked";
        if (s.Mode == CartMode.Held && !s.GateClear) return "Cart — gate down";
        if (s.Mode == CartMode.Held && s.CageHeld && !s.Boarding) return "Cart — cage locked";
        if (s.Boarding) return $"Cart — {(int)MathF.Round(s.Plating01 * 100f)}% — departs in {Ceil(Math.Max(0f, s.BoardingLeft))}";
        if (s.Mode == CartMode.Rolling) return "Cart — rolling";
        if (s.Halted) return "Cart — on the line";
        if (s.StopIndex == 0) return "Cart — at the landing";
        return s.StopIndex >= s.StopCount - 1 ? "Cart — end of the line" : $"Cart — at stop {s.StopIndex}";
    }

    /// <summary>Godot's Mathf.CeilToInt, engine-free.</summary>
    private static int Ceil(float x) => (int)MathF.Ceiling(x);
}
