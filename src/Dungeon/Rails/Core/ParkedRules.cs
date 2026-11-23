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
    /// The state after this step (Ruling 25), in this order: taken over (the caller's latch, set from the takeover's press until the arrival at
    /// the last stop or a restore) gives <see cref="ParkedState.Gone"/>; a lift due with no chaser or relief on the line gives
    /// <see cref="ParkedState.Lift"/>; in the way and smashed now gives <see cref="ParkedState.Gone"/>; otherwise it stays.
    /// </summary>
    public static ParkedState Next(ParkedState state, bool smashedNow, bool liftDue, bool takenOver, bool chaseOnLine)
    {
        if (takenOver) return ParkedState.Gone;
        if (liftDue && !chaseOnLine) return ParkedState.Lift;
        if (state == ParkedState.InTheWay && smashedNow) return ParkedState.Gone;
        return state;
    }

    /// <summary>The lift rolls out (pre-flight F5): its place is empty, <see cref="ParkedState.Gone"/> ("gone" on the wire); any other state stays. While it runs it is the chase on the line, so <see cref="Next"/> stands no lift until it is hidden.</summary>
    public static ParkedState LiftDeparts(ParkedState state) => state == ParkedState.Lift ? ParkedState.Gone : state;

    /// <summary>What it offers: "Board the cart" only as a lift, with no chase on the line, to a player on foot within reach; otherwise "".</summary>
    public static string Prompt(ParkedState state, bool chaseOnLine, bool onFootPlayer, bool inReach)
        => state == ParkedState.Lift && !chaseOnLine && onFootPlayer && inReach ? BoardPrompt : "";
}
