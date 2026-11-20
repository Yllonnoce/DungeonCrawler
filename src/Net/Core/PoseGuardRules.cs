namespace DungeonCrawler.Net;

/// <summary>
/// Which of a remote player's poses the server drops after it moved that body itself. Engine-free. The unseat guard (seated poses after a
/// set-down) lives in <c>ServerWorld.ApplyPose</c>; this is the shove's (a hazard pushing a body clear, a test putting it somewhere) and the
/// map's (a pose off the floor the layout knows, <see cref="OffTheMap"/>).
/// </summary>
public static class PoseGuardRules
{
    /// <summary>The guard gives up this long after the shove (a ForcePose that never landed cannot freeze the body).</summary>
    public const double ShoveHoldSeconds = 1.0;

    /// <summary>
    /// A pose that arrives <paramref name="secondsSinceShove"/> after the server shoved the body is dropped while it lies nearer the spot the
    /// body was shoved from than the spot it was shoved to (<paramref name="fromShoved"/>, <paramref name="toShoved"/>: its distances to
    /// each): it was sent before the client heard, and would pull the body back. The first pose from the new spot ends the guard.
    /// </summary>
    public static bool DropAfterShove(float fromShoved, float toShoved, double secondsSinceShove)
        => secondsSinceShove < ShoveHoldSeconds && toShoved > fromShoved;

    /// <summary>The returned-pose guard gives up this long after a crevasse return (a client that never lands must not be filtered forever).</summary>
    public const double ReturnHoldSeconds = 3.0;

    /// <summary>
    /// A pose over a drop that arrives <paramref name="secondsSinceReturn"/> after the server returned the body from a crevasse is dropped:
    /// it was sent from the client's own held body, before the client heard of the return, and would send the body straight back in.
    /// </summary>
    public static bool DropAfterReturn(bool overDrop, double secondsSinceReturn)
        => overDrop && secondsSinceReturn < ReturnHoldSeconds;

    /// <summary>A pose over a drop may sit this far above the drop cell's floor: a body just stepping off the lip.</summary>
    public const float DropPoseSlack = 0.05f;

    /// <summary>
    /// Is a remote pose off the map (dropped)? On a cell nobody walks (<paramref name="walkable"/> false: rock, a wall, a chasm) and not
    /// on the under layer (<paramref name="under"/>), except over a drop (<paramref name="overDrop"/>: a chasm, a crevasse floor's)
    /// at or below that cell's floor <paramref name="dropFloorY"/> (with <see cref="DropPoseSlack"/>): that is a body falling in, which
    /// the server's copy must follow for the crevasse watch to judge it. Above a chasm's floor it is walking on air.
    /// </summary>
    public static bool OffTheMap(bool walkable, bool under, bool overDrop, float y, float dropFloorY)
        => !walkable && !under && !(overDrop && y <= dropFloorY + DropPoseSlack);
}
