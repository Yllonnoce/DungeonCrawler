using System.Numerics;

namespace DungeonCrawler.Enemies;

/// <summary>
/// Lead aim (plan 16 Task 8): where to aim a bolt of <c>projectileSpeed</c> so it meets a target moving at a constant velocity: the earliest
/// intercept, solved from |d + v·t| = speed·t (d the target's offset). Engine-free (System.Numerics).
/// </summary>
public static class LeadAim
{
    /// <summary>The target's place at the earliest intercept from <paramref name="from"/>, or <paramref name="target"/> when no bolt can meet it.</summary>
    public static Vector3 Point(Vector3 from, Vector3 target, Vector3 targetVelocity, float projectileSpeed)
    {
        if (!(projectileSpeed > 0f)) return target;
        var d = target - from;
        float a = Vector3.Dot(targetVelocity, targetVelocity) - projectileSpeed * projectileSpeed;
        float b = 2f * Vector3.Dot(d, targetVelocity);
        float c = Vector3.Dot(d, d);
        float t;
        if (MathF.Abs(a) < 1e-6f)
        {
            if (b >= 0f) return target; // as fast as the bolt and moving away (or still at the muzzle): no meeting ahead
            t = -c / b;
        }
        else
        {
            var disc = b * b - 4f * a * c;
            if (disc < 0f) return target;
            var root = MathF.Sqrt(disc);
            float t1 = (-b - root) / (2f * a), t2 = (-b + root) / (2f * a);
            if (t1 > t2) (t1, t2) = (t2, t1);
            t = t1 > 0f ? t1 : t2;
        }
        if (!(t > 0f) || float.IsInfinity(t)) return target;
        return target + targetVelocity * t;
    }
}
