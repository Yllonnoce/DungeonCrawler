namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// What the cart's HUD line is told: the party cart as this peer shows it (the server's or single player's own state, a client's
/// flags and countdown), and the chaser when the local hero sits in it. <see cref="Boarding"/>: a countdown runs, with
/// <see cref="BoardingLeft"/> seconds left; <see cref="Plating01"/> is the plating's share of full. <see cref="ChaserBoardingLeft"/> is −1
/// when the chaser's countdown does not run. <see cref="CageHeld"/>: the cart stands at a stop whose locked cage holds it (plan 7). <see cref="ChaserRelief"/>: the chaser the
/// local hero rides is a relief cart (single player, plan 7). <see cref="Hijack"/>: the hijack's phase (plan 16), the server's own or, on a
/// client, the one its <c>CartStateDto.Hijack</c> carries.
/// </summary>
public readonly record struct RailHudState(CartMode Mode, bool Hidden, bool Halted, bool LineBlocked, bool GateClear, bool Boarding,
    float BoardingLeft, float Plating01, int StopIndex, int StopCount, bool InChaser = false, CartMode ChaserMode = CartMode.Held,
    float ChaserBoardingLeft = -1f, bool CageHeld = false, bool ChaserRelief = false, HijackPhase Hijack = HijackPhase.None);

/// <summary>The cart's HUD line from <see cref="RailHudState"/>. Engine-free.</summary>
public static class RailHud
{
    /// <summary>The line while an enemy has the brake off: its glow at the stop and the roll back (plan 16 final fix wave).</summary>
    public const string HijackedLine = "Cart — hijacked! rolling back";
    /// <summary>The line once the driver is off: the brake (the train slowing on its roll back) and the return to the stop.</summary>
    public const string BrakingLine = "Cart — braking", ReturningLine = "Cart — returning";
    /// <summary>The line while the train is gone into the dark and its relief comes up the line.</summary>
    public const string TrainLostLine = "Train lost — a relief train is coming";

    /// <summary>The hijack's line for <paramref name="phase"/>, or null when none is live (<see cref="HijackPhase.None"/>).</summary>
    public static string? HijackText(HijackPhase phase) => phase switch
    {
        HijackPhase.Glow or HijackPhase.RollingBack => HijackedLine,
        HijackPhase.Braking => BrakingLine,
        HijackPhase.Returning => ReturningLine,
        HijackPhase.Gone or HijackPhase.ReliefIn => TrainLostLine,
        _ => null,
    };

    /// <summary>
    /// The hijack's line shows however far the local hero is from the party cart (it is gone down the line, or rolling away): the hijack is the
    /// stop's news, on every peer. <c>RailController.RefreshHud</c> asks it before its own range rule.
    /// </summary>
    public static bool ShowsAnywhere(HijackPhase phase) => phase != HijackPhase.None;

    /// <summary>The plating bar stands under the line, but not while the train is lost (gone, or its relief still coming).</summary>
    public static bool ShowsPlating(HijackPhase phase) => phase is not (HijackPhase.Gone or HijackPhase.ReliefIn);

    /// <summary>
    /// The line's words, or null when nothing is shown: the chaser's line while the local hero rides it (however far the party cart is);
    /// else the hijack's (<see cref="HijackText"/>, a hidden train's too); else nothing for a hidden party cart (a wreck no spare replaced),
    /// else the party cart's state.
    /// </summary>
    public static string? Text(in RailHudState s)
    {
        if (s.InChaser)
        {
            var name = s.ChaserRelief ? "Relief cart" : "Spare cart";
            if (s.ChaserBoardingLeft >= 0f) return $"{name} — departs in {Ceil(s.ChaserBoardingLeft)}";
            return s.ChaserMode == CartMode.Rolling ? $"{name} — after the train" : $"{name} — behind the train";
        }
        if (HijackText(s.Hijack) is { } hijack) return hijack;
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
