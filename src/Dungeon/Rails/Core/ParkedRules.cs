namespace DungeonCrawler.Dungeon.Rails;

/// <summary>The parked train's state (plan 15 Task 5, spec B.5): in the party's way, gone (smashed, taken over, or its lift away), or standing as a lift.</summary>
public enum ParkedState { InTheWay, Gone, Lift }

/// <summary>
/// The parked train before floor 12's exit bridge (plan 15 Task 5, spec B.5; Rulings 14, 23-25): a cart and four empty wagons standing on the
/// party lane with the cart's front <see cref="FrontShort"/> short of the bridge's near edge, so it is built from the layout's bridge. While it
/// is in the way a seated hero calls it <see cref="CallAt"/> before its rear face (<see cref="CallDue"/>), and the party's train front reaching
/// that face smashes it (<see cref="Smashes"/>) at the barricade's cost (<see cref="SmashDent"/>, <see cref="SpeedKept"/>); its five tubs
/// tumble and go after <see cref="TumbleSeconds"/>. It stands again as a lift once one is due (<see cref="LiftDue"/>) and no chaser or relief is
/// on the line (<see cref="Next"/>). Only a lift offers boarding (<see cref="Prompt"/>). Every S is the party lane's. Engine-free.
/// </summary>
public static class ParkedRules
{
    /// <summary>The cart's front stands this far short of the exit bridge's near edge.</summary>
    public const float FrontShort = 1f;
    /// <summary>A seated hero calls it with the party's front this far short of its rear face.</summary>
    public const float CallAt = 35f;
    /// <summary>A <c>parked</c> piece's At lying more than this from its place (<see cref="RearS"/>) builds a dead marker and an error.</summary>
    public const float AtSlack = 2f;
    /// <summary>Its tumbling tubs go this long after the smash (as the party cart's tumble).</summary>
    public const float TumbleSeconds = 6f;
    /// <summary>The wagons behind its cart.</summary>
    public const int Wagons = 4;
    /// <summary>The smash's cost: the barricade's (<see cref="BarricadeRules"/>).</summary>
    public const float SmashDent = BarricadeRules.SmashDent, SpeedKept = BarricadeRules.SpeedKept;
    /// <summary>Its smash hurts every seated hero who is up in the whole train, the wagons' too (<see cref="BarricadeRules.SmashBooks"/>): the train meets it, not the cart alone.</summary>
    public const bool SmashReachesWholeTrain = true;

    /// <summary>The call's log line, with nobody seated to speak it (subtitle only).</summary>
    public const string CallLog = "Cart on the line — brace!";
    /// <summary>The log line as it stands again as a lift.</summary>
    public const string LiftLog = "Another cart rolls out onto the line by the lava.";
    /// <summary>The smash's log line.</summary>
    public const string SmashLog = "The cart smashes through the parked train!";
    /// <summary>The lift's prompt, the only one it offers.</summary>
    public const string BoardPrompt = "Board the cart";

    /// <summary>Its cart's front face, <see cref="FrontShort"/> short of the bridge's near edge at <paramref name="bridgeFromS"/>.</summary>
    public static float FrontS(float bridgeFromS) => bridgeFromS - FrontShort;

    /// <summary>Its cart's centre.</summary>
    public static float CartS(float bridgeFromS) => FrontS(bridgeFromS) - CartRules.HalfLength;

    /// <summary>Its last body's rear face, the face the party's front meets: the train's whole length (<see cref="TrainRules.Length"/>) behind the front.</summary>
    public static float RearS(float bridgeFromS, int wagons) => FrontS(bridgeFromS) - TrainRules.Length(wagons);

    /// <summary>A body just behind it, as a centre S in <see cref="SpareRules.TrainBackS"/>'s sense: the rear face less half a cart.</summary>
    public static float BackS(float bridgeFromS, int wagons) => RearS(bridgeFromS, wagons) - CartRules.HalfLength;

    /// <summary>The party's train smashes it this step: its front reached the rear face (<paramref name="prevFront"/> short of it, <paramref name="front"/> at or past it).</summary>
    public static bool Smashes(float prevFront, float front, float rearFaceS) => prevFront < rearFaceS && rearFaceS <= front;

    /// <summary>The call is due: rolling, not yet called this arming, the front within <see cref="CallAt"/> of the rear face and short of it.</summary>
    public static bool CallDue(bool rolling, float front, float rearFaceS, bool called)
        => rolling && !called && front >= rearFaceS - CallAt && front < rearFaceS;

    /// <summary>
    /// A lift is due: the party train's rear is past the bridge's far edge (<paramref name="bridgeToS"/>), the train is held at the last stop, the
    /// party has no live cart (wrecked with no spare, hidden), or its last wreck was past the bridge's start (<paramref name="wreckedInside"/>). NaN compares false.
    /// </summary>
    public static bool LiftDue(float partyRearS, float bridgeToS, bool heldAtLast, bool liveCart, bool wreckedInside)
        => partyRearS > bridgeToS || heldAtLast || !liveCart || wreckedInside;

    /// <summary>
    /// The state after this step (Ruling 25), in this order: taken over (the caller's latch, <see cref="LatchHolds"/>) gives <see cref="ParkedState.Gone"/>;
    /// in the way and smashed now gives <see cref="ParkedState.Gone"/>, so "smashed" is told before any lift, even when the smash wrecked the cart with no
    /// spare left and a lift is due in the same tick; a lift due with no chaser or relief on the line, and its tubs no longer <paramref name="tumbling"/>
    /// (<see cref="Tumbling"/>), gives <see cref="ParkedState.Lift"/>; otherwise it stays.
    /// </summary>
    public static ParkedState Next(ParkedState state, bool smashedNow, bool liftDue, bool takenOver, bool chaseOnLine, bool tumbling)
    {
        if (takenOver) return ParkedState.Gone;
        if (state == ParkedState.InTheWay && smashedNow) return ParkedState.Gone;
        if (liftDue && !chaseOnLine && !tumbling) return ParkedState.Lift;
        return state;
    }

    /// <summary>
    /// Its tubs still tumble <paramref name="sinceSmash"/> seconds after the smash (under <see cref="TumbleSeconds"/>): its place stays gone, so the tumble
    /// copies never spawn inside a lift standing solid again. Infinity (never smashed): false.
    /// </summary>
    public static bool Tumbling(double sinceSmash) => sinceSmash < TumbleSeconds;

    /// <summary>
    /// The takeover's latch (Ruling 25) after a cart event: set from the takeover's press, it clears on the arrival at the last stop
    /// (<paramref name="atLastStop"/>), on a restore, and on a wreck of the taken-over train (the lift may then stand again for anyone still outside).
    /// </summary>
    public static bool LatchHolds(bool latched, CartEventKind e, bool atLastStop)
        => latched && e switch
        {
            CartEventKind.Wrecked or CartEventKind.Restored => false,
            CartEventKind.Arrived => !atLastStop,
            _ => true,
        };

    /// <summary>
    /// <see cref="LiftLog"/> is logged as the lift newly stands in play: once until the next restore (not again after each lift run), not on the step
    /// right after a restore (a load at the last stop stands it at once), and not with nobody standing (a fall that took everyone: the wipe comes next).
    /// </summary>
    public static bool SaysLiftLog(bool toldAlready, bool justRestored, bool anyoneStanding) => !toldAlready && !justRestored && anyoneStanding;

    /// <summary>The lift rolls out: its place is empty, <see cref="ParkedState.Gone"/> ("gone" on the wire); any other state stays. While it runs it is the chase on the line, so <see cref="Next"/> stands no lift until it is hidden.</summary>
    public static ParkedState LiftDeparts(ParkedState state) => state == ParkedState.Lift ? ParkedState.Gone : state;

    /// <summary>What it offers: "Board the cart" only as a lift, with no chase on the line, to a player on foot within reach; otherwise "".</summary>
    public static string Prompt(ParkedState state, bool chaseOnLine, bool onFootPlayer, bool inReach)
        => state == ParkedState.Lift && !chaseOnLine && onFootPlayer && inReach ? BoardPrompt : "";

    // ---- the lift (plan 15 Task 6, Rulings 26-31) --------------------------------------------------------------------

    /// <summary>
    /// The lift's limit (its StopAt), a centre S: the nearest of the party train's <paramref name="trainBackS"/> (<see cref="SpareRules.TrainBackS"/>)
    /// less <see cref="SpareRules.ChaseGap"/> (NaN with no live cart ahead), the standing buffer's face <paramref name="bufferFaceS"/> less half a cart
    /// (NaN for none), and the last stop <paramref name="lastStopS"/>. NaN arguments are ignored.
    /// </summary>
    public static float LiftLimit(float trainBackS, float bufferFaceS, float lastStopS)
    {
        var limit = lastStopS;
        if (!float.IsNaN(trainBackS)) limit = Nearest(limit, trainBackS - SpareRules.ChaseGap);
        if (!float.IsNaN(bufferFaceS)) limit = Nearest(limit, bufferFaceS - CartRules.HalfLength);
        return limit;
    }

    private static float Nearest(float a, float b) => float.IsNaN(a) ? b : Math.Min(a, b);

    /// <summary>
    /// Each of <paramref name="riders"/>'s spot index for a set-down taken once for the whole list (Task 9: read once, not as each leaves): its place in
    /// <paramref name="book"/> (the lift's riders before anyone leaves), one not in it after the book's last, in turn. <paramref name="count"/> is how
    /// many spots to take.
    /// </summary>
    public static int[] SetDownOrder<T>(IReadOnlyList<T> book, IReadOnlyList<T> riders, out int count)
    {
        var order = new int[riders.Count];
        count = book.Count;
        for (var i = 0; i < riders.Count; i++)
        {
            var k = -1;
            for (var j = 0; j < book.Count; j++) if (EqualityComparer<T>.Default.Equals(book[j], riders[i])) { k = j; break; }
            order[i] = k >= 0 ? k : count++;
        }
        return order;
    }

    /// <summary>
    /// What the lift's halt line measures its gap to, as <c>LiftHalts</c> measures it (Task 9): the party train's last body
    /// while it has a live cart ahead (<paramref name="trainAhead"/>), else the standing buffer (<paramref name="bufferAhead"/>), else the last stop.
    /// </summary>
    public static string LiftHaltWhat(bool trainAhead, bool bufferAhead)
        => trainAhead ? "behind the train ahead" : bufferAhead ? "short of the standing buffer" : "short of the last stop";

    /// <summary>A board takes the lift over as the party's train (Ruling 27): the party has no live cart, and its last wreck was not past the bridge's start.</summary>
    public static bool TakesOver(bool partyLiveCart, bool wreckedInside) => !partyLiveCart && !wreckedInside;

    /// <summary>
    /// Where a lift rider's saved pose stands (Ruling 31), as a party-lane S: the lift's own cart S once its rear (<paramref name="liftRearS"/>) is past the
    /// bridge's far edge (<paramref name="bridgeToS"/>: the rider is set down inside), else the parked train's place (<paramref name="parkedCartS"/>: beside
    /// it, outside). Never over the bridge.
    /// </summary>
    public static float SavePoseS(float liftCartS, float liftRearS, float bridgeToS, float parkedCartS) => liftRearS > bridgeToS ? liftCartS : parkedCartS;

    /// <summary>
    /// A train with its front at <paramref name="trainFrontS"/> and its rear at <paramref name="trainRearS"/> stands within the parked train's span
    /// (<paramref name="rearFaceS"/> .. <paramref name="frontFaceS"/>): a body of it overlaps the lift's place (<c>ClearTheLiftsSpan</c>). NaN: never.
    /// </summary>
    public static bool InItsSpan(float trainFrontS, float trainRearS, float rearFaceS, float frontFaceS)
        => trainFrontS > rearFaceS && trainRearS < frontFaceS;
}
