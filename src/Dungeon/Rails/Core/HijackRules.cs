namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Where a hijack of the party's train stands (plan 16, spec section 3): None; Glow (the driver calls and glows, the train still held);
/// RollingBack (the train rolls backwards from its stop, against the way of travel, facing forward); Braking (the brake goes on, still
/// rolling back); Returning (forward to its stop at walking pace); Gone (it got away: T7); ReliefIn (a relief train rolls in: T7).
/// On the wire as <c>CartStateDto.Hijack</c> (a byte).
/// </summary>
public enum HijackPhase : byte { None, Glow, RollingBack, Braking, Returning, Gone, ReliefIn }

/// <summary>
/// The reverse roll's numbers and its input per phase (plan 16 Task 5). The party's run rolls back with <c>Dir</c> −1 (its way of travel)
/// and <c>Reversing</c> (its facing stays +1, the wagons on the lower-S side and leading); its leading face is then the last wagon's back,
/// <c>wagons × TrainRules.Spacing</c> beyond the cart's own half length. Engine-free.
/// </summary>
public static class HijackRules
{
    /// <summary>The roll back gains speed this slowly (m/s², call): below a hero's run, so it can be caught on foot.</summary>
    public const float RollBackAccel = 0.5f;
    /// <summary>The roll back's top speed (m/s, call).</summary>
    public const float RollBackMaxSpeed = 4f;
    /// <summary>The brake's deceleration when it goes on (m/s²): the cart's own.</summary>
    public const float BrakeDecel = CartRules.BrakeDecel;
    /// <summary>The walk back to the stop (m/s, call).</summary>
    public const float ReturnSpeed = 2f;
    /// <summary>The farthest the train rolls back from its stop (m, call).</summary>
    public const float RollBackReach = 40f;
    /// <summary>The leading face stops at least this far short of a hole or a live hazard behind the stop (m, call).</summary>
    public const float HoleClear = 5f;
    /// <summary>A stop whose limit leaves less roll than this allows no hijack (m).</summary>
    public const float MinRoll = 5f;
    /// <summary>The driver's call and glow before the brake comes off (s, call): T6's.</summary>
    public const float BrakeGlow = 3f;

    /// <summary>The run rolls against its facing in <paramref name="p"/>: the roll back and its brake.</summary>
    public static bool Reversing(HijackPhase p) => p is HijackPhase.RollingBack or HijackPhase.Braking;

    /// <summary>The drive's acceleration in <paramref name="p"/> (<see cref="CartInput.DriveAccel"/>): the roll back's slow ramp, else −1 (the cart's own).</summary>
    public static float DriveAccelFor(HijackPhase p) => p == HijackPhase.RollingBack ? RollBackAccel : -1f;

    /// <summary>
    /// The input the party's run steps on in <paramref name="p"/>, toward <paramref name="limit"/>, with <paramref name="slope"/> in its way of
    /// travel: the roll back drives 4 m/s on the 0.5 m/s² ramp, the brake brakes, the return drives 2 m/s and a relief train 4 m/s.
    /// A commanded speed counts no pumpers and no load (<see cref="CartPhysics.Step"/>). Any other phase stands (no command, no brake).
    /// </summary>
    public static CartInput InputFor(HijackPhase p, float slope, int wagons, float limit) => p switch
    {
        HijackPhase.RollingBack => new CartInput(slope, false, 0, wagons, limit, RollBackMaxSpeed, DriveAccelFor(p)),
        HijackPhase.Braking => new CartInput(slope, true, 0, wagons, limit),
        HijackPhase.Returning => new CartInput(slope, false, 0, wagons, limit, ReturnSpeed),
        HijackPhase.ReliefIn => new CartInput(slope, false, 0, wagons, limit, RollBackMaxSpeed),
        _ => new CartInput(slope, false, 0, wagons, limit),
    };

    /// <summary>
    /// How far back the train held at <paramref name="stopS"/> may roll (its middle's S): <see cref="RollBackReach"/> back, or short of the
    /// nearest of <paramref name="hazards"/> behind the stop (From below the stop) so that its leading face, the last of <paramref name="wagons"/>'s
    /// back, stands <see cref="HoleClear"/> past that hazard's far end (To). A hazard ahead of the stop is ignored.
    /// </summary>
    public static float LimitS(float stopS, int wagons, IReadOnlyList<(float From, float To)> hazards)
    {
        var limit = stopS - RollBackReach;
        var lead = Math.Max(0, wagons) * TrainRules.Spacing + CartRules.HalfLength;
        for (var i = 0; i < hazards.Count; i++)
        {
            var (from, to) = hazards[i];
            if (!(from < stopS)) continue;
            var clear = to + HoleClear + lead;
            if (clear > limit) limit = clear;
        }
        return limit;
    }

    /// <summary>The limit leaves room for a hijack: at least <see cref="MinRoll"/> of roll behind the stop.</summary>
    public static bool RollRoom(float stopS, float limitS) => stopS - limitS >= MinRoll;
}
