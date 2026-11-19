namespace DungeonCrawler.Net;

/// <summary>
/// Which of a remote player's poses the server drops after it moved that body itself. Engine-free. The unseat guard (seated poses after a
/// set-down) lives in <c>ServerWorld.ApplyPose</c>; this is the shove's: a hazard pushing a body clear, a test putting it somewhere.
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
}
