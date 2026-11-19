namespace DungeonCrawler.Party;

/// <summary>
/// The right stick's snap turn (VrRig.Read, every physics tick, seated or on foot): pushed past <see cref="Push"/> it turns the
/// room <see cref="Degrees"/> once; it must come back inside <see cref="Release"/> before it turns again. Engine-free.
/// </summary>
public static class SnapTurn
{
    /// <summary>A flick turns the view this far at once (a smooth turn is what makes people ill).</summary>
    public const float Degrees = 30f;
    /// <summary>How far the stick must be pushed to turn, and how far it must come back before it turns again.</summary>
    public const float Push = 0.6f, Release = 0.3f;

    /// <summary>The turn this tick in radians (positive turns left, the body's measure of yaw; a stick pushed right turns right), or 0.</summary>
    public static float Read(ref bool armed, float stickX)
    {
        if (MathF.Abs(stickX) < Release) { armed = true; return 0f; }
        if (!armed || MathF.Abs(stickX) <= Push) return 0f;
        armed = false;
        return -MathF.Sign(stickX) * Degrees * (MathF.PI / 180f);
    }
}
