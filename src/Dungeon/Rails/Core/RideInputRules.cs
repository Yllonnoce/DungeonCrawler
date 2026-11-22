namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The riders' say over the party's cart, and the board prompt, as one rule for every peer. Engine-free: the server's (or
/// solo) <c>RailController.Inputs</c> and its <c>PromptFor</c> call it, and so does a client's prompt with the synced state.
/// </summary>
public static class RideInputRules
{
    /// <summary>Two pumpers at most from the companions (a second one adds the last boost).</summary>
    public const int MaxPumpers = 2;
    /// <summary>The stick past this forward pumps, past this back brakes.</summary>
    public const float StickThreshold = 0.5f;
    /// <summary>A client resends a held pump or brake this often (seconds of elapsed time), besides on every change.</summary>
    public const double ResendSeconds = 0.25;
    /// <summary>A held pump or brake the server has not heard again for this long is let go (a lost client).</summary>
    public const double HeldTimeoutSeconds = 1.0;

    /// <summary>
    /// The cart's pumpers and brake from its seated riders: a brake by any seated player cancels all pumping; players pump first
    /// (each one who pumps counts); willing companions fill up to <paramref name="most"/> pumpers in all (two in the game, a
    /// test may cap it lower).
    /// </summary>
    public static (int Pumpers, bool Brake) Inputs(int playersPumping, bool anyPlayerBrakes, bool companionsWant, int companionsAvailable, int most = MaxPumpers)
    {
        if (anyPlayerBrakes) return (0, true);
        var pumpers = Math.Max(0, playersPumping);
        if (companionsWant) pumpers += Math.Clamp(most - pumpers, 0, Math.Max(0, companionsAvailable));
        return (pumpers, false);
    }

    /// <summary>A seated player's stick (the move vector's Y; forward is −Y): forward pumps, back brakes.</summary>
    public static (bool Pump, bool Brake) FromMove(float moveY) => (moveY < -StickThreshold, moveY > StickThreshold);

    /// <summary>A hero boards from this close to the tub itself (its nearest point, not its middle): the server's intent check and the prompt agree.</summary>
    public const float BoardReach = 3.5f;

    /// <summary>
    /// "Get out" when seated in this cart, unless <paramref name="holdOn"/> (the train rolls on ride 4's collapsing bridge:
    /// <see cref="BridgeRules.HoldsRiders"/>), when nothing is offered; "Board the cart" when it is Held short of the end of the line with a
    /// hero seat free and the hero within <see cref="BoardReach"/> of the tub; else nothing.
    /// </summary>
    public static string BoardPrompt(bool seatedHere, bool held, bool lastStop, bool heroSeatFree, bool inReach, bool holdOn = false)
        => seatedHere ? (holdOn ? "" : "Get out") : held && !lastStop && heroSeatFree && inReach ? "Board the cart" : "";

    /// <summary>
    /// A rider may get out of the cart it sits in only while up: a downed rider stays slumped in the seat; and not while <paramref name="holdOn"/>
    /// (on the collapsing bridge at speed: <see cref="BridgeRules.HoldsRiders"/>). The server's check of a "Get out" intent.
    /// </summary>
    public static bool MayGetOut(bool seatedHere, bool downed, bool holdOn = false) => seatedHere && !downed && !holdOn;

    /// <summary>
    /// How far a point in the cart's own frame (<paramref name="x"/> across, <paramref name="y"/> up, <paramref name="z"/> along) lies
    /// from the tub's box: |x| ≤ <paramref name="halfWidth"/>, <paramref name="bottom"/> ≤ y ≤ <paramref name="top"/>,
    /// |z| ≤ <paramref name="halfLength"/>; 0 inside.
    /// </summary>
    public static float TubDistance(float x, float y, float z, float halfWidth, float halfLength, float bottom, float top)
    {
        var dx = Math.Max(0f, Math.Abs(x) - halfWidth);
        var dz = Math.Max(0f, Math.Abs(z) - halfLength);
        var dy = y < bottom ? bottom - y : y > top ? y - top : 0f;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>A hero this far from the tub may board it.</summary>
    public static bool InBoardReach(float tubDistance) => tubDistance <= BoardReach;

    /// <summary>A held pump or brake last heard at <paramref name="lastAt"/> is stale at <paramref name="now"/> (a let-go never is).</summary>
    public static bool Stale(bool holding, double lastAt, double now) => holding && now - lastAt > HeldTimeoutSeconds;
}
