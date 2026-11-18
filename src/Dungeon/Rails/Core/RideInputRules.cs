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

    /// <summary>"Get out" when seated in this cart; "Board the cart" when it is Held short of the end of the line with a hero seat free; else nothing.</summary>
    public static string BoardPrompt(bool seatedHere, bool held, bool lastStop, bool heroSeatFree)
        => seatedHere ? "Get out" : held && !lastStop && heroSeatFree ? "Board the cart" : "";

    /// <summary>A held pump or brake last heard at <paramref name="lastAt"/> is stale at <paramref name="now"/> (a let-go never is).</summary>
    public static bool Stale(bool holding, double lastAt, double now) => holding && now - lastAt > HeldTimeoutSeconds;
}
