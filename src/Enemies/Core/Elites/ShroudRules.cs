namespace DungeonCrawler.Enemies;

/// <summary>
/// The Shrouded dome, in the horizontal plane: a disc of <see cref="Radius"/> around the elite. A bolt or a nova that starts outside and
/// reaches in fizzles at the edge; whatever starts inside (the elite's own bolts, a party arrow loosed from inside) is never an "entry".
/// </summary>
public static class ShroudRules
{
    public const float Radius = EliteRules.ShroudRadius;

    public static bool Inside(float px, float pz, float cx, float cz, float r)
    {
        var dx = px - cx; var dz = pz - cz;
        return dx * dx + dz * dz < r * r;
    }

    /// <summary>True only when A is outside the disc and the segment AB reaches a point inside it.</summary>
    public static bool SegmentEntersDisc(float ax, float az, float bx, float bz, float cx, float cz, float r)
    {
        if (Inside(ax, az, cx, cz, r)) return false;
        if (Inside(bx, bz, cx, cz, r)) return true;
        var abx = bx - ax; var abz = bz - az;
        var len2 = abx * abx + abz * abz;
        if (len2 <= 1e-12f) return false;
        var t = ((cx - ax) * abx + (cz - az) * abz) / len2;
        if (t <= 0f || t >= 1f) return false; // the closest point is an endpoint, and both are outside
        var px = ax + abx * t; var pz = az + abz * t;
        return Inside(px, pz, cx, cz, r);
    }
}
