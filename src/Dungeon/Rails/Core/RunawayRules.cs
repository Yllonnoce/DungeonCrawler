namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's runaway ore wagon (plan 12, Rulings 10-13): one loose wagon comes down the party line from behind the train, its bell
/// ringing, at the train's own unpumped speed + <see cref="Boost"/> (<see cref="Target"/>), so one pumper holds it off and none lets it
/// close. It breaks loose <see cref="Behind"/> m behind the train's last body (<see cref="StartS"/>), and only with a live split, no
/// chaser or relief cart on the party line and room before the split's window (<see cref="Sends"/>). It rams a rolling train once
/// (<see cref="Rams"/>): the plating loses <see cref="RamDent"/> and each seated hero who is up <see cref="RamDamage"/>, never below
/// 1 HP; then it derails. A held train is never rammed: it halts <see cref="HaltShort"/> behind it (<see cref="HaltS"/>). Engine-free.
/// </summary>
public static class RunawayRules
{
    /// <summary>Its speed over the train's own unpumped target: the spec's cruise + 15%.</summary>
    public const float Boost = 0.15f;
    /// <summary>It breaks loose with its front this far behind the train's last body.</summary>
    public const float Behind = 15f;
    /// <summary>Its front must start at least this far past the ride's stop, or it is not sent.</summary>
    public const float MinRoom = 2f;
    /// <summary>The split's seated window must open at least this far past the trigger, or it is not sent.</summary>
    public const float MinLead = 10f;
    /// <summary>Its front within this much of the last body's back, with the train rolling, is a ram.</summary>
    public const float RamReach = 0.25f;
    /// <summary>The plating a ram takes.</summary>
    public const float RamDent = 40f;
    /// <summary>A seated hero's share of max HP a ram takes at the default lethality.</summary>
    public const float RamHpFraction = 0.20f;
    /// <summary>Behind a held train its front halts this far short of the last body's back.</summary>
    public const float HaltShort = 3f;
    /// <summary>Its bell rings this often while it is within <see cref="BellNear"/> behind the train.</summary>
    public const float BellEvery = 2f;
    public const float BellNear = 40f;

    /// <summary>Its target speed: the train's own with nobody pumping, for its <paramref name="crewWagons"/>, + <see cref="Boost"/>.</summary>
    public static float Target(int crewWagons) => CartPhysics.TargetSpeed(0, crewWagons) * (1 + Boost);

    /// <summary>The S of the train's last body's back: the cart's back, <paramref name="wagons"/> times <paramref name="wagonSpacing"/> further back (<c>CartTrain.WagonSpacing</c>).</summary>
    public static float TrainRearS(float partyBackS, int wagons, float wagonSpacing) => partyBackS - wagons * wagonSpacing;

    /// <summary>Where its front starts: <see cref="Behind"/> short of the train's last body; NaN when that is short of the ride's stop + <see cref="MinRoom"/>.</summary>
    public static float StartS(float trainRearS, float rideStartS)
    {
        var s = trainRearS - Behind;
        return s < rideStartS + MinRoom ? float.NaN : s;
    }

    /// <summary>It is sent: the ride's split is live, no chaser or relief cart rolls on the party line, and the split's window opens <see cref="MinLead"/> or more past the trigger.</summary>
    public static bool Sends(bool splitLive, bool chaserRolling, float triggerS, float windowStartS)
        => splitLive && !chaserRolling && windowStartS - triggerS >= MinLead;

    /// <summary>A ram: the train rolls and its front is within <see cref="RamReach"/> of the last body's back.</summary>
    public static bool Rams(bool trainRolling, float front, float trainRearS) => trainRolling && front >= trainRearS - RamReach;

    /// <summary>Behind a held train it halts here (its middle): its front <see cref="HaltShort"/> short of the last body's back.</summary>
    public static float HaltS(float trainRearS) => trainRearS - HaltShort - CartRules.HalfLength;

    /// <summary>The HP a ram takes from a seated hero at <paramref name="hp"/> of <paramref name="maxHp"/>: <see cref="RamHpFraction"/> scaled by the party's damage, never below 1 HP.</summary>
    public static float RamDamage(float maxHp, float hp, float partyDamageScale)
        => Math.Min(maxHp * RamHpFraction * partyDamageScale / TierRules.DefaultLethality, Math.Max(0f, hp - 1f));

    /// <summary>A client's read of a runaway gone from the party line: its front went within 1.5 m of its own train's last body (the ram).</summary>
    public static bool RammedAt(float goneFrontS, float trainRearS) => Math.Abs(goneFrontS - trainRearS) <= 1.5f;

    /// <summary>Its bell rings (every <see cref="BellEvery"/>): rolling, behind the train's last body and within <see cref="BellNear"/> of it.</summary>
    public static bool Rings(bool rolling, float front, float trainRearS)
    {
        var gap = trainRearS - front;
        return rolling && gap >= -RamReach && gap <= BellNear;
    }
}
