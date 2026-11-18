namespace DungeonCrawler.Dungeon.Rails;

/// <summary>What a seated desktop view does with the ride: lean into bends, share the track's pitch, widen with speed. Engine-free.</summary>
public static class CartFeel
{
    /// <summary>The share of the track's pitch the camera takes.</summary>
    public const float PitchShare = 0.4f;
    public const float MaxLeanDegrees = 7f;
    public const float MaxFovBoost = 10f;

    /// <summary>Roll in degrees for a bend (curvature 1/m, positive turning left) at speed v: positive leans left, into the bend.</summary>
    public static float LeanDegrees(float curvature, float v) => Math.Clamp(curvature * v * v * 1.2f, -MaxLeanDegrees, MaxLeanDegrees);

    /// <summary>Extra field of view in degrees at speed v.</summary>
    public static float FovBoost(float v) => MaxFovBoost * Math.Clamp(v / CartRules.MaxSpeed, 0f, 1f);
}
