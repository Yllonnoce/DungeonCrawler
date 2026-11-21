namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 12's dark stretch (Ruling 16): from its start S the rail's pocket lamps are out for <see cref="Length"/> m. A view or a torch is
/// darkened by its own place in the span (<see cref="Factor"/>, ramping in and out over <see cref="Ramp"/>): the ambient light keeps
/// <see cref="AmbientKept"/>, the fog light <see cref="FogKept"/> and a hero's torch <see cref="TorchKept"/> (<see cref="Kept"/>). Cart
/// lamps, sparks and warning lamps are never touched. Engine-free.
/// </summary>
public static class DarkRules
{
    /// <summary>The stretch's length along the party lane, from its start.</summary>
    public const float Length = 85f;
    /// <summary>The darkness ramps in over this much at the start and out over this much at the end.</summary>
    public const float Ramp = 6f;
    /// <summary>A pocket lamp this near (flat) either lane over the span is put out.</summary>
    public const float LampReach = 6f;
    /// <summary>What the dark keeps of the ambient light's energy, the fog light's colour and a hero's torch.</summary>
    public const float AmbientKept = 0.12f, FogKept = 0.25f, TorchKept = 0.1f;

    /// <summary>How dark it is at party S <paramref name="s"/> of a stretch starting at <paramref name="fromS"/>: 0 outside it, 1 inside its ramps, linear between; 0 for NaN.</summary>
    public static float Factor(float s, float fromS)
    {
        if (float.IsNaN(s) || float.IsNaN(fromS)) return 0f;
        var f = System.Math.Min(System.Math.Min(s - fromS, fromS + Length - s) / Ramp, 1f);
        return f > 0f ? f : 0f;
    }

    /// <summary>The share of a light kept at darkness <paramref name="factor"/> when the full dark keeps <paramref name="kept"/>.</summary>
    public static float Kept(float factor, float kept) => 1f - factor * (1f - kept);
}
