using DungeonCrawler.Combat;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A timber barricade across a line (plan 11). <see cref="Toughness"/> hits cut it: a blade, an arrow, a shove or a thrown
/// weapon knocks off one; any spell clears it at once (<see cref="HitWeight"/>). A cart whose front reaches its face on its own
/// lane while it stands smashes through (<see cref="Smashes"/>): the plating loses <see cref="SmashDent"/>, the cart keeps
/// <see cref="SpeedKept"/> of its speed, and each seated hero who is up takes <see cref="SmashDamage"/>, which never takes them
/// below 1 HP. A seated companion calls it once, <see cref="CallAhead"/> before its face (<see cref="Calls"/>). Every cart on its line
/// smashes one still standing (<see cref="OnItsLane"/>, <see cref="SmashesAlong"/>): a slag cart's riders each take <see cref="SmashHpFraction"/>
/// of their own max HP, unscaled. Engine-free.
/// </summary>
public static class BarricadeRules
{
    /// <summary>Hits that cut it down (a spell's one is worth all of them).</summary>
    public const int Toughness = 3;
    /// <summary>The plating a smash costs.</summary>
    public const float SmashDent = 25f;
    /// <summary>A smash costs each seated hero this fraction of max HP at the default lethality.</summary>
    public const float SmashHpFraction = 0.15f;
    /// <summary>The fraction of its speed a cart keeps through a smash.</summary>
    public const float SpeedKept = 0.8f;
    /// <summary>The call comes this far before the face (about six seconds at cruise).</summary>
    public const float CallAhead = 35f;
    /// <summary>From the barricade's middle to the face a cart meets.</summary>
    public const float HalfDepth = 0.25f;

    /// <summary>How much of <see cref="Toughness"/> one hit takes off: a spell all of it, anything else one.</summary>
    public static int HitWeight(AttackKind kind) => kind == AttackKind.Spell ? Toughness : 1;

    /// <summary>A smash's share of a seated hero's max HP: <see cref="SmashHpFraction"/> at the default lethality, scaled by the party's damage scale.</summary>
    public static float SmashFraction(float partyDamageScale) => SmashHpFraction * partyDamageScale / TierRules.DefaultLethality;

    /// <summary>The HP a smash takes from a hero at <paramref name="hp"/> of <paramref name="maxHp"/>: its fraction, but never below 1 HP.</summary>
    public static float SmashDamage(float maxHp, float hp, float partyDamageScale)
        => Math.Min(maxHp * SmashFraction(partyDamageScale), Math.Max(0f, hp - 1f));

    /// <summary>A cart's front reached the standing barricade's face this step (<paramref name="prevFront"/> short of it, <paramref name="front"/> at or past it), on its lane.</summary>
    public static bool Smashes(bool standing, bool onItsLane, float prevFront, float front, float faceS)
        => standing && onItsLane && prevFront < faceS && faceS <= front;

    /// <summary>A cart's front end at centre <paramref name="s"/> running in <paramref name="dir"/> (+1 toward higher S, −1 toward lower): half a cart ahead in its own way of travel.</summary>
    public static float FrontOf(int dir, float s) => s + (dir >= 0 ? 1f : -1f) * CartRules.HalfLength;

    /// <summary>
    /// <see cref="Smashes"/> for a cart running in <paramref name="dir"/> on the barricade's line: its front this step and the last
    /// (<see cref="FrontOf"/>) and the face it meets (the side of <paramref name="middleS"/> it comes from, <see cref="HalfDepth"/> short)
    /// are read along its own way of travel, so a cart running toward lower S smashes as its front comes down onto the far face.
    /// </summary>
    public static bool SmashesAlong(bool standing, bool onItsLane, int dir, float prevFront, float front, float middleS)
    {
        var d = dir >= 0 ? 1f : -1f;
        return Smashes(standing, onItsLane, d * prevFront, d * front, d * (middleS - d * HalfDepth));
    }

    /// <summary>A cart judges a barricade only on its own line: one across the slag line (<paramref name="slagLine"/>) by a run on the enemy lane, one across the party line by a run on the party's.</summary>
    public static bool OnItsLane(bool slagLine, bool runOnEnemyLane) => slagLine == runOnEnemyLane;

    /// <summary>
    /// A barricade that is down stands again at its ride's next arming (Ruling 5) when that ride was undone (<paramref name="rideUndone"/>:
    /// a wreck or a load), or when it went down while its ride was not armed (traffic or a runaway smashed it). Taken down on its ride, it stays down.
    /// </summary>
    public static bool StandsAgainAtArming(bool downWhileItsRideArmed, bool rideUndone) => rideUndone || !downWhileItsRideArmed;

    /// <summary>The call is due: not yet made this arming, the barricade standing on the cart's lane, the front within <see cref="CallAhead"/> of its face and short of it.</summary>
    public static bool Calls(bool standing, bool onItsLane, bool called, float front, float faceS)
        => standing && onItsLane && !called && front >= faceS - CallAhead && front < faceS;
}
