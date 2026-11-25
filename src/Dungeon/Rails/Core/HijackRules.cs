namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Where a hijack of the party's train stands (plan 16, spec section 3): None; Glow (the driver calls and glows, the train still held);
/// RollingBack (the train rolls backwards from its stop, against the way of travel, facing forward); Braking (the brake goes on, still
/// rolling back); Returning (forward to its stop at walking pace); Gone (it got away: the cart hidden, the wagons uncoupled); ReliefIn (a relief
/// train rolls in from the limit to the stop).
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
    /// <summary>The driver's call and glow before the brake comes off (s, call).</summary>
    public const float BrakeGlow = 3f;

    // ---- Task 6: the hijack, won back -------------------------------------------------------------------------------------------

    /// <summary>A runner this close to the cart's (or a wagon's) side, flat, climbs in (m, call).</summary>
    public const float ClimbReach = 2f;
    /// <summary>The climb takes this long (s, call): a hit cancels it.</summary>
    public const float ClimbSeconds = 0.8f;
    /// <summary>A hit runner fights its attacker this long before it runs for the train again (s, Ruling 9).</summary>
    public const float FightBackSeconds = 3f;
    /// <summary>After a hijack ends (won back, or the relief arrived) the next tag waits this long (s, spec Ruling 2).</summary>
    public const float NextTagAfter = 20f;
    /// <summary>The rolling-back train may be boarded on foot up to this speed (m/s, Ruling 12): the roll's 4 m/s with a margin.</summary>
    public const float BoardMaxSpeed = 4.05f;
    /// <summary>The creatures that may be tagged as hijackers (spec section 3: never a priest or a brute).</summary>
    public const string Cinder = "enemy.horde_cinder", Hound = "enemy.ember_hound";

    /// <summary>
    /// A hijack may start (the climb's end hands the cart to its driver): the train Held at a sieged stop (1 to <paramref name="stopCount"/> − 2,
    /// never the landing or the last), not halted on the line nor standing past its stop (the island), with no hero seated anywhere in the train,
    /// up or downed (Ruling 8: one is enough), no chaser or solo relief live (Ruling 30), not in a rise's grace, no hijack live, and room to roll.
    /// </summary>
    public static bool MayStart(int stop, int stopCount, bool held, bool halted, bool standingPast, int heroesSeatedInTrain, bool chaseLive, bool risingGrace, bool hijackLive, bool rollRoom)
        => InRange(stop, stopCount) && held && !halted && !standingPast && heroesSeatedInTrain == 0 && !chaseLive && !risingGrace && !hijackLive && rollRoom;

    /// <summary>
    /// A siege creature of wave <paramref name="wave"/> may be tagged as a hijacker (spec Ruling 2): never in a stop's first wave, only one hijack
    /// live, <see cref="NextTagAfter"/> since the last ended, at most one a wave, a cinder imp or an ember hound, and only where the train has room to roll.
    /// </summary>
    public static bool MayTag(int wave, bool hijackLive, float sinceLastHijackEnded, bool taggedThisWave, string enemyId, bool rollRoom)
        => wave >= 2 && !hijackLive && sinceLastHijackEnded >= NextTagAfter && !taggedThisWave && MayTagKind(enemyId) && rollRoom;

    /// <summary>A creature of this kind may be a hijacker: a cinder imp or an ember hound.</summary>
    public static bool MayTagKind(string enemyId) => enemyId is Cinder or Hound;

    /// <summary>The cart may be boarded on foot in <paramref name="p"/> at <paramref name="v"/> (Ruling 12): rolling back, braking or returning, at walking speeds.</summary>
    public static bool MayBoard(HijackPhase p, float v) => (p is HijackPhase.RollingBack or HijackPhase.Braking or HijackPhase.Returning) && v <= BoardMaxSpeed;

    /// <summary>
    /// A tagged runner runs for the train (Ruling 25): no hero seated in it, not in a rise's grace, at a sieged stop, and either the train held
    /// there (no hijack yet, or its glow) or in the glow or the roll back. At the brake, the return or a got-away it is released.
    /// </summary>
    public static bool MayRun(int stop, int stopCount, HijackPhase phase, bool held, int heroesSeatedInTrain, bool risingGrace)
        => InRange(stop, stopCount) && heroesSeatedInTrain == 0 && !risingGrace
           && (held && (phase is HijackPhase.None or HijackPhase.Glow) || (phase is HijackPhase.Glow or HijackPhase.RollingBack));

    /// <summary>
    /// The brake may come off (<c>RailController.BeginRollBack</c>'s own guard, whatever called it): the train held at a sieged stop, not halted
    /// on the line nor standing past its stop, with room to roll. The seats, a chase and a rise's grace are <see cref="MayStart"/>'s, judged when
    /// the driver takes the cart (a hero who sits during the glow does not stop the roll: Ruling 8).
    /// </summary>
    public static bool MayRollBack(int stop, int stopCount, bool held, bool halted, bool standingPast, bool rollRoom)
        => InRange(stop, stopCount) && held && !halted && !standingPast && rollRoom;

    /// <summary>
    /// A client glows the driver from the BoarderThrow telegraph for <see cref="BoarderRules.ThrowGlow"/> (1.5 s), so the server tells it again
    /// every this many seconds of the <see cref="BrakeGlow"/>: a co-op player sees the whole 3 s warning.
    /// </summary>
    public const float GlowRetell = BoarderRules.ThrowGlow;

    /// <summary>The telegraph's tellings over a glow: the first at its start, then one every <see cref="GlowRetell"/> while the glow lasts.</summary>
    public static int GlowTells => (int)MathF.Ceiling(BrakeGlow / GlowRetell - 1e-4f);

    /// <summary>A sieged stop: 1 to <paramref name="stopCount"/> − 2 (the siege's own range).</summary>
    private static bool InRange(int stop, int stopCount) => stop >= 1 && stop <= stopCount - 2;

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

    // ---- Task 7: the hijack, got away ----------------------------------------------------------------------------------------

    /// <summary>While the train rolls back with a driver, a prisoner is put off it this often (s, call): the first at this many seconds of the roll.</summary>
    public const float ThrowEvery = 3f;
    /// <summary>A train gone at its limit is followed by a relief train this many seconds later (s, call).</summary>
    public const float ReliefAfter = 12f;
    /// <summary>A crew member a hijack put off books a seat from this far (m, spec Ruling 3): the usual <see cref="TrainRules.BoardReach"/> for everyone else.</summary>
    public const float ThrownBoardReach = 60f;
    /// <summary>The departure waits for the crew a hijack put off at most this long after the train is held at its stop again (s, spec Ruling 3).</summary>
    public const float ThrownWaitMax = 30f;

    /// <summary>The next prisoner is put off now: <paramref name="thrown"/> already off after <paramref name="rollingSeconds"/> of the roll back (one every <see cref="ThrowEvery"/>).</summary>
    public static bool ThrowDue(float rollingSeconds, int thrown) => rollingSeconds >= (thrown + 1) * ThrowEvery - 1e-4f;

    /// <summary>
    /// Who is put off next (Ruling 13): <paramref name="train"/> is the coupled wagons in coupling order (nearest the cart first) with their crew seats
    /// held; the last wagon goes first, and within a wagon its rear bench seat 5 then 4 (<see cref="BoarderRules.ThrowSeat"/>). Null: nobody aboard.
    /// </summary>
    public static (int Wagon, int Seat)? NextThrow(IReadOnlyList<(int Wagon, bool Seat4, bool Seat5)> train)
    {
        for (var i = train.Count - 1; i >= 0; i--)
            if (BoarderRules.ThrowSeat(train[i].Seat4, train[i].Seat5) is int seat) return (train[i].Wagon, seat);
        return null;
    }

    /// <summary>The relief train comes now, <paramref name="goneSeconds"/> after the train was gone (<see cref="ReliefAfter"/>).</summary>
    public static bool ReliefDue(float goneSeconds) => goneSeconds >= ReliefAfter - 1e-4f;

    /// <summary>
    /// The departure waits for crew a hijack put off (spec Ruling 3): <paramref name="thrownOnTheWay"/> of them still on the way (up or down, not yet
    /// seated nor dead) and under <see cref="ThrownWaitMax"/> since the train was held at its stop again (+inf: it never was).
    /// </summary>
    public static bool HoldsForThrown(int thrownOnTheWay, float sinceHeldAgain) => thrownOnTheWay > 0 && sinceHeldAgain < ThrownWaitMax;

    /// <summary>At <see cref="ThrownWaitMax"/> a thrown crew member walking to its seat within <see cref="ThrownBoardReach"/> sits in at once; farther ones stay on foot.</summary>
    public static bool SeatsThrownAtOnce(float sinceHeldAgain, float distance)
        => !float.IsPositiveInfinity(sinceHeldAgain) && sinceHeldAgain >= ThrownWaitMax && distance <= ThrownBoardReach; // +inf: never held again at its stop

    /// <summary>
    /// A crew member a hijack put off is still "thrown" (its 60 m reach, the departure's wait, the sit-in at the cap): until the train, held at its
    /// stop again (<paramref name="sinceHeldAgain"/> finite), has waited <see cref="ThrownWaitMax"/>, or until it <paramref name="departed"/>. After
    /// that one still on foot is an ordinary freed crew member (25 m), never seated at once at this stop or a later one.
    /// </summary>
    public static bool StillThrown(float sinceHeldAgain, bool departed)
        => !departed && (float.IsPositiveInfinity(sinceHeldAgain) || sinceHeldAgain < ThrownWaitMax);

    /// <summary>
    /// Floor 12's roll-back limits as measured on the shipped layout with four wagons (Ruling 26): stop → the limit (the cart's middle S) and
    /// whether it leaves room for a hijack. <c>--test-stop-trap=limits</c> fails when the built floor's limit moves off its row by more than 0.1.
    /// Measured 2026-11-25: stops 1-3 at the 40 m cap (stop 3's broken bridge is on the slag line: the party lane behind it has no hole), stop 4
    /// cut by ride 4's whole bridge span (903.1..991.1, standing or gone: Ruling 10) to 14.9 m of roll.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, (float LimitS, bool RollRoom)> ShippedLimits = new Dictionary<int, (float LimitS, bool RollRoom)>
    {
        [1] = (243.187f, true),  // stop 1 at S 283.187: the 40 m cap
        [2] = (448.841f, true),  // stop 2 at S 488.841: the 40 m cap
        [3] = (730.109f, true),  // stop 3 at S 770.109: the 40 m cap
        [4] = (1007.556f, true), // stop 4 at S 1022.470: cut by ride 4's bridge span 903.056..991.056
    };
}
