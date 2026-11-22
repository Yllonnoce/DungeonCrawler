namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's collapsing bridge (plan 13, Rulings 2-6): the cavern's party-lane deck, which starts to go <see cref="Grace"/> after the
/// train's last body is wholly on it (<see cref="Running"/>). Its front then runs from the bridge's start at <see cref="Pace"/>, fixed
/// as it starts, and a cell goes as the front passes its far edge (<see cref="GoneCells"/>). It catches the train only while the
/// train rolls and a seated player braked within <see cref="BrakeMemory"/> (<see cref="Catches"/>); otherwise its front holds
/// <see cref="HoldShort"/> behind the train's rear (<see cref="Advance"/>), which covers a stall, the watchdog, a heavy load and the
/// stop's own braking. A brake counts only when it was pressed <see cref="ReactionTime"/> or more after the first call (<see cref="Counts"/>,
/// the controller's ruling on Task 3's review I1): a train already braked onto the bridge before the warning is held short, never taken by it. A runaway behind the train whose front is on the gone span goes down with it (<see cref="TakesRunaway"/>).
/// While the train rolls on it, at any speed, "Get out" is neither offered nor taken (<see cref="HoldsRiders"/>). Engine-free.
/// </summary>
public static class BridgeRules
{
    /// <summary>The collapse starts this long after the train's rear passed the bridge's start (the call comes then).</summary>
    public const float Grace = 1.5f;
    /// <summary>Its front runs at this share of the train's unpumped cruise: an unpumped train outruns it.</summary>
    public const float PaceShare = 0.8f;
    /// <summary>A seated player's brake counts for this long after it was last held (seconds).</summary>
    public const float BrakeMemory = 2f;
    /// <summary>A brake counts toward the take only when pressed this long or more after the first call (the reaction time, seconds).</summary>
    public const float ReactionTime = 1.5f;
    /// <summary>Unbraked, its front holds this far short of the train's rear.</summary>
    public const float HoldShort = 2f;
    /// <summary>A player braking with the front within this of the train's rear is told not to stop.</summary>
    public const float BrakeCallNear = 12f;
    /// <summary>The bridge piece's At must lie within this of the cavern's start, or the bridge is dead.</summary>
    public const float AtSlack = 8f;
    /// <summary>The collapse's cue plays once every this many cells.</summary>
    public const int CueEveryCells = 2;
    /// <summary>
    /// A train the bridge takes is seen to fall for this long (seconds): its look and its riders' drop through the gone cells, each seated
    /// player's eye riding down with them, before the wipe is judged (the crevasse takes the riders at once, as any fall; the legibility fix round).
    /// </summary>
    public const float FallSeen = 1.4f;

    /// <summary>
    /// A client sees the take from its player's seat (<see cref="FallSeen"/>): its hero sits in the cart now, or sat in it when the cart's last
    /// state before this one was applied or on a frame since (<paramref name="inCartSinceLastState"/>), or is told fallen beside the cart. The
    /// server's unseat pose for the taken player is sent at the take, so on the one ordered stream it comes after every state before the take
    /// and before the Wrecked|Fell one, however many frames apart they land; its Fallen sheet comes after both.
    /// </summary>
    public static bool LocalRode(bool inCartNow, bool inCartSinceLastState, bool fallenNearCart)
        => inCartNow || inCartSinceLastState || fallenNearCart;

    /// <summary>
    /// The <see cref="LocalRode"/> window's next value: an applied state sets it to whether our hero sits in the cart then; a posed frame only
    /// opens it (in the cart). So a hero out of the cart on one frame between two states (a get-out a frame after a state) still reads as having
    /// ridden until the next state, at most one correction (0.2 s); the take's pose always follows a state in the cart.
    /// </summary>
    public static bool LocalInCartNext(bool open, bool fromState, bool inCart) => fromState ? inCart : open || inCart;

    /// <summary>The front's speed: <see cref="PaceShare"/> of the train's unpumped cruise with <paramref name="crewWagons"/> coupled.</summary>
    public static float Pace(int crewWagons) => CartPhysics.TargetSpeed(0, crewWagons) * PaceShare;

    /// <summary>The collapse runs: the train's rear came onto the bridge at <paramref name="rearOnAt"/> (NaN: not yet) and the grace has passed.</summary>
    public static bool Running(double rearOnAt, double now) => !double.IsNaN(rearOnAt) && now - rearOnAt >= Grace;

    /// <summary>
    /// The front after <paramref name="dt"/>: on at <paramref name="pace"/>, never past <paramref name="toS"/>; unbraked, never closer than
    /// <see cref="HoldShort"/> to the train's rear. It never moves back.
    /// </summary>
    public static float Advance(float frontS, float toS, float pace, float dt, float trainRearS, bool braked)
    {
        var on = frontS + pace * dt;
        if (braked) return Math.Max(frontS, Math.Min(toS, on));
        return Math.Max(frontS, Math.Min(toS, Math.Min(on, Math.Max(frontS, trainRearS - HoldShort))));
    }

    /// <summary>A seated player braked within <see cref="BrakeMemory"/> (last at <paramref name="lastBrakeAt"/>, NaN: never).</summary>
    public static bool Braked(double lastBrakeAt, double now) => !double.IsNaN(lastBrakeAt) && now - lastBrakeAt <= BrakeMemory;

    /// <summary>
    /// The brake counts toward the take (Rulings 3-4 as amended): pressed at <paramref name="pressedAt"/>, at or after the first call
    /// (<paramref name="calledAt"/>) + <see cref="ReactionTime"/>, and last held within <see cref="BrakeMemory"/> (<see cref="Braked"/>). A brake
    /// pressed earlier counts not at all, however long it is held: the memory never carries it across the line. NaN (no call, no press): no.
    /// </summary>
    public static bool Counts(double pressedAt, double lastBrakeAt, double calledAt, double now)
        => !double.IsNaN(calledAt) && !double.IsNaN(pressedAt) && pressedAt >= calledAt + ReactionTime && Braked(lastBrakeAt, now);

    /// <summary>
    /// A braking tick starts a new press when the last rolling tick did not brake. A brake held on is one press however long it is held,
    /// through a pause or a frame hitch alike (no clock gap re-stamps it); the braking flag is cleared whenever the cart leaves Rolling, so a
    /// brake held into a stop is a fresh press on the next ride's first rolling tick.
    /// </summary>
    public static bool NewPress(bool wasBraking) => !wasBraking;

    /// <summary>The bridge takes the train: rolling, braked, and its front at the train's rear.</summary>
    public static bool Catches(bool rolling, bool braked, float frontS, float trainRearS) => rolling && braked && frontS >= trainRearS;

    /// <summary>The cells gone (in line order): those whose far edge (<paramref name="cellFarS"/>) is at or behind the front.</summary>
    public static int GoneCells(float frontS, IReadOnlyList<float> cellFarS)
    {
        var n = 0;
        while (n < cellFarS.Count && cellFarS[n] <= frontS) n++; // NaN compares false: none
        return n;
    }

    /// <summary>A runaway whose front is on the gone span (from <paramref name="fromS"/> to the front) goes down with it; none while the bridge stands (NaN front).</summary>
    public static bool TakesRunaway(float runawayFrontS, float fromS, float frontS)
        => !float.IsNaN(frontS) && fromS <= runawayFrontS && runawayFrontS <= frontS;

    /// <summary>
    /// Nobody gets out: the train is on a live bridge (the caller requires it Rolling), at any speed (the final review's I2, amending
    /// Ruling 6: a train braked onto the bridge is held short alive, and a "Get out" there would strand its riders in the pockets the collapse
    /// then islands). A wreck, a halt and a held cart are not Rolling, so they are untouched; a stalled train is still freed by the watchdog.
    /// </summary>
    public static bool HoldsRiders(bool trainOnBridge) => trainOnBridge;

    /// <summary>A player brakes with the front within <see cref="BrakeCallNear"/> of the train's rear: "Don't stop".</summary>
    public static bool CallsBrake(bool braking, float frontS, float trainRearS) => braking && trainRearS - frontS <= BrakeCallNear;
}
