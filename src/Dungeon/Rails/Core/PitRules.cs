namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 5's lava pit and the deadly switch round it (plan 15 Task 4, spec B.4). The layout cuts one party-lane cell to chasm
/// (<see cref="Generation.RailLine.Pit"/>); every role cuts the party track bare over it, from its lip to its far edge. A
/// <c>switch_out</c> on the pit's ride whose points stand <see cref="LeadMin"/> to <see cref="LeadMax"/> short of the lip is deadly
/// (<see cref="Deadly"/>): a human's lever in the seated window, called twice by a companion (<see cref="Call"/>), taken at its points as
/// it stands, and taken round the pit anyway when no human aboard could throw it (<see cref="TakesRound"/>, ruling R-lone). Its fixed back
/// brings the train home past the far edge. A train whose front meets the lip on its own lane falls into the lava (<see cref="Falls"/>);
/// a ride where a correct throw could still meet the lip has its fall disarmed (<see cref="FallArmed"/>). Engine-free.
/// </summary>
public static class PitRules
{
    /// <summary>The nearest and farthest the pit's lip may lie past a switch_out's points for the switch to be deadly.</summary>
    public const float LeadMin = 7f, LeadMax = 40f;
    /// <summary>The pit's switch_out's points stand this far short of the lip (<see cref="OutPoints"/>).</summary>
    public const float OutLead = 8f;
    /// <summary>The pit's switch_back's points stand this far past its far edge (<see cref="BackPoints"/>).</summary>
    public const float BackPast = 1f;
    /// <summary>A <c>pit</c> piece's At lying more than this from the layout's lip is an error (the layout's pit stands regardless).</summary>
    public const float AtSlack = 2f;
    /// <summary>The early call comes with the front this far before the lever; the last this far before the points (as ride 3's live back).</summary>
    public const float CallEarly = SwitchBackRules.CallEarly, CallLast = SwitchBackRules.CallLast;
    /// <summary>A switch_out's points at least this far short of the lip bring the cart wholly across before it: the crossover and a metre.</summary>
    public const float ArmLead = SwitchBackRules.HomeStretch + 1f;

    /// <summary>The fall's log line (subtitle only).</summary>
    public const string FallLog = "The cart runs into the lava pit!";
    /// <summary>The crossing's log line, as the cart takes the switch round the pit.</summary>
    public const string RoundLog = "The cart takes the switch round the pit.";
    /// <summary>The early call's log line, with nobody seated to speak it.</summary>
    public const string EarlyLog = "Lava pit on the line — throw the switch!";
    /// <summary>The last call's log line, with nobody seated to speak it.</summary>
    public const string LastLog = "Throw it — now!";

    /// <summary>A switch_out at party S <paramref name="pointsS"/> is deadly: the pit's lip (<paramref name="lipS"/>, NaN for none) lies within [<see cref="LeadMin"/>, <see cref="LeadMax"/>] past it.</summary>
    public static bool Deadly(float pointsS, float lipS)
    {
        var lead = lipS - pointsS;
        return lead >= LeadMin && lead <= LeadMax; // NaN compares false
    }

    /// <summary>The pit's switch_out's points for a lip at <paramref name="lipS"/>.</summary>
    public static float OutPoints(float lipS) => lipS - OutLead;

    /// <summary>The pit's switch_back's points for a far edge at <paramref name="farS"/>.</summary>
    public static float BackPoints(float farS) => farS + BackPast;

    /// <summary>
    /// The train falls this step: its front crossed the lip (<paramref name="prevFront"/> short of <paramref name="lipS"/>, <paramref name="front"/>
    /// at or past it) with the cart on its own lane there (<paramref name="blendAtLip"/> under 0.5). Once: a front already past the lip never falls again.
    /// </summary>
    public static bool Falls(float prevFront, float front, float lipS, float blendAtLip)
        => prevFront < lipS && lipS <= front && blendAtLip < 0.5f;

    /// <summary>
    /// The pit's fall is armed on its ride (spec B.4's guard): some switch_out stands short of the lip, every one short of it stands at least
    /// <see cref="ArmLead"/> short of it (the cart is wholly across at the lip), and the switch back that takes it home (the next one past its
    /// points) stands at or past the far edge (<paramref name="farS"/>), so a train taken round comes home only past the pit. Otherwise a correct
    /// throw could fall: disarmed.
    /// </summary>
    public static bool FallArmed(IReadOnlyList<float> outPoints, IReadOnlyList<float> backPoints, float lipS, float farS)
    {
        if (float.IsNaN(lipS) || float.IsNaN(farS)) return false;
        var any = false;
        foreach (var outS in outPoints)
        {
            if (!(outS < lipS)) continue; // past the lip: never across there
            if (!(lipS - outS >= ArmLead)) return false; // too near: still half across at the lip
            var next = float.NaN;
            foreach (var backS in backPoints)
                if (backS > outS && (float.IsNaN(next) || backS < next)) next = backS;
            if (!(next >= farS)) return false; // no back, or one home before the pit's far edge (NaN compares false)
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The call due now for the pit's deadly switch_out, while its lever is straight and untaken with the front short of its points: the last
    /// (<see cref="CallLast"/> before the points, once) if due, else the early one (<see cref="CallEarly"/> before the lever, once and never after the last).
    /// </summary>
    public static BackCall Call(bool thrown, bool taken, float front, float leverS, float pointsS, bool earlyDone, bool lastDone)
    {
        if (thrown || taken || lastDone || !(front < pointsS)) return BackCall.None; // NaN compares false
        if (front >= pointsS - CallLast) return BackCall.Last;
        return !earlyDone && front >= leverS - CallEarly ? BackCall.Early : BackCall.None;
    }

    /// <summary>At the deadly switch_out's points the cart takes the switch round the pit: thrown, or straight with no human aboard able to throw it (R-lone).</summary>
    public static bool TakesRound(bool thrown, bool ableHumanAboard) => thrown || !ableHumanAboard;
}
