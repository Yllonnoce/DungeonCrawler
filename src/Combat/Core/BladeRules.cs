using DungeonCrawler.Items;

namespace DungeonCrawler.Combat;

/// <summary>
/// Where the blade is at one moment of a swing, as the fight sees it. <see cref="Bearing"/> is where it points across the
/// wielder's view (degrees, + to the left, 0 straight ahead); <see cref="Elevation"/> how far it is raised (90 upright,
/// 0 level); <see cref="Length"/> how far it reaches from the chest, in metres. <see cref="Live"/> is true only while
/// it can cut: inside a cutting stroke and lowered toward the enemy.
/// </summary>
public readonly record struct BladeReach(float Bearing, float Elevation, float Length, bool Live);

/// <summary>
/// Engine-free rules for a blade that hits what it touches. A swing's <see cref="WeaponMove"/> is the same one the
/// first-person view plays, so the blow lands when and where the blade is seen to arrive: a sweep cuts what lies along
/// its arc, one body after another; a chop or a thrust cuts only what stands ahead. The view's moves were drawn to look
/// right on a screen, so only their timing, their bearing and their reach are taken literally; the height of a cut
/// follows where the wielder looks.
/// </summary>
public static class BladeRules
{
    /// <summary>How far the view's blade is raised at rest (the elevation of FirstPersonWeapon's BladeDir).</summary>
    public const float RestElevation = 53f;
    /// <summary>The view's blade rests leaning across the screen; this much of a move's Left is that lean, not aim.</summary>
    public const float AimLean = 12f;
    /// <summary>A blade raised above this is still coming round and cuts nothing.</summary>
    public const float LiveBelowElevation = 45f;
    /// <summary>Half the width of the path a blade cuts, in metres (the old aim cast was 0.7).</summary>
    public const float Thickness = 0.3f;
    /// <summary>The blade is walked along its path in steps no larger than this many degrees, so nothing slips between two.</summary>
    public const float MaxStepDegrees = 8f;
    /// <summary>A hand drawn back shortens the reach by this much of the range per metre, down to <see cref="ShortestReach"/> of it.</summary>
    public const float DrawBackPerMetre = 1.5f;
    public const float ShortestReach = 0.4f;
    /// <summary>A touch whose hit roll failed still lands, for this much of the damage.</summary>
    public const float GlancingMultiplier = 0.5f;
    /// <summary>Each body a blade has already cut in a swing leaves this much of the blow for the next.</summary>
    public const float CarryPerBody = 0.75f;
    /// <summary>The swing hangs this long on the first body it bites, light and heavy.</summary>
    public const float HitStopSeconds = 0.03f, HeavyHitStopSeconds = 0.06f;

    /// <summary>The cutting strokes of a move: each runs from the pose before a blow to the blow itself. <c>ImpactZ</c> is how far forward the hand ends.</summary>
    public static IEnumerable<(float From, float To, float ImpactZ)> Cuts(WeaponMove move)
    {
        var at = 0f;
        foreach (var k in move.Keys)
        {
            if (k.Impact) yield return (at, at + k.Seconds, k.Z);
            at += k.Seconds;
        }
    }

    /// <summary>When the last cutting stroke of a move ends (0 for a move with none).</summary>
    public static float Ends(WeaponMove move)
    {
        var end = 0f;
        foreach (var c in Cuts(move)) end = c.To;
        return end;
    }

    /// <summary>The blade <paramref name="t"/> seconds into a move, for a weapon of this <paramref name="range"/>.</summary>
    public static BladeReach At(WeaponMove move, float t, float range)
    {
        var p = move.Sample(t);
        var elevation = RestElevation + p.Up;
        var bearing = p.Left + AimLean;
        foreach (var c in Cuts(move))
        {
            if (t < c.From || t > c.To) continue;
            var length = range * Math.Clamp(1f - DrawBackPerMetre * (p.Z - c.ImpactZ), ShortestReach, 1f);
            return new BladeReach(bearing, elevation, length, elevation <= LiveBelowElevation);
        }
        return new BladeReach(bearing, elevation, range, false);
    }

    /// <summary>
    /// The moments to look at the blade between <paramref name="from"/> (already looked at) and <paramref name="to"/>:
    /// close enough together that it turns about <see cref="MaxStepDegrees"/> between two, and always the start and the
    /// end of a cutting stroke that fall inside, so neither the near end of a sweep nor the blow itself is stepped over.
    /// </summary>
    public static List<float> Steps(WeaponMove move, float from, float to, float range)
    {
        var times = new List<float>();
        if (to <= from) return times;
        var a = At(move, from, range);
        var b = At(move, to, range);
        var turn = Math.Max(Math.Abs(b.Bearing - a.Bearing), Math.Abs(b.Elevation - a.Elevation));
        var n = Math.Clamp((int)MathF.Ceiling(turn / MaxStepDegrees), 1, 16);
        for (var i = 1; i <= n; i++) times.Add(from + (to - from) * i / n);
        foreach (var c in Cuts(move))
        {
            if (c.From > from && c.From < to) times.Add(c.From);
            if (c.To > from && c.To < to) times.Add(c.To);
        }
        times.Sort();
        return times;
    }

    /// <summary>How much of a blow is left for a body when the blade has already cut <paramref name="bodiesAlreadyCut"/> others this swing.</summary>
    public static float Carry(int bodiesAlreadyCut) => MathF.Pow(CarryPerBody, Math.Max(0, bodiesAlreadyCut));

    public static float HitStop(bool heavy) => heavy ? HeavyHitStopSeconds : HitStopSeconds;
}
