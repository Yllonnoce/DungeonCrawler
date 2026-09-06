namespace DungeonCrawler.Dungeon;

/// <summary>The cross-section a vaulted ceiling follows (data/themes Vault = "pointed" etc.; empty = the tier's default).</summary>
public enum ArchKind { Flat, Semicircular, Pointed, Segmental, Catenary }

/// <summary>Which ceiling piece a cell gets: a barrel along one axis where it is a corridor, a groin (two barrels crossing) elsewhere.</summary>
public enum VaultPiece { BarrelX, BarrelZ, Groin }

/// <summary>
/// Arch profiles for vaulted ceilings, engine-free. The vault springs from the top of the walls and rises into the
/// space above the corridor; heights are metres above the spring line for a cross position t in -1..1 (t = ±1 at the walls).
/// </summary>
public static class VaultRules
{
    /// <summary>Highest point of the profile above the spring line for a vault of the given half-width.</summary>
    public static float Rise(ArchKind kind, float halfWidth) => kind switch
    {
        ArchKind.Semicircular => halfWidth,
        ArchKind.Pointed => halfWidth * MathF.Sqrt(2f),          // two arcs of radius 1.5 × half-width, centred a quarter-span past the middle
        ArchKind.Segmental => halfWidth * 0.45f,                  // a shallow arc
        ArchKind.Catenary => halfWidth * 1.2f,
        _ => 0f,
    };

    /// <summary>Height above the spring line at cross position t (-1..1); 0 at the walls, <see cref="Rise"/> at the centre.</summary>
    public static float Height(ArchKind kind, float t, float halfWidth)
    {
        t = Math.Clamp(t, -1f, 1f);
        var x = MathF.Abs(t) * halfWidth;
        switch (kind)
        {
            case ArchKind.Semicircular:
                return MathF.Sqrt(MathF.Max(0f, halfWidth * halfWidth - x * x));
            case ArchKind.Pointed:
            {
                var r = 1.5f * halfWidth; // each half is an arc whose centre sits a quarter-span past the middle
                var dx = x + halfWidth / 2f;
                return MathF.Sqrt(MathF.Max(0f, r * r - dx * dx));
            }
            case ArchKind.Segmental:
            {
                var rise = Rise(kind, halfWidth);
                var r = (halfWidth * halfWidth + rise * rise) / (2f * rise);
                return MathF.Max(0f, MathF.Sqrt(MathF.Max(0f, r * r - x * x)) - (r - rise));
            }
            case ArchKind.Catenary:
            {
                var rise = Rise(kind, halfWidth);
                const float a = 0.9f; // steepness of the chain: the vault climbs quickly off the walls and rounds at the top
                var span = MathF.Cosh(a * halfWidth) - 1f;
                return rise * (1f - (MathF.Cosh(a * x) - 1f) / span);
            }
            default:
                return 0f;
        }
    }

    /// <summary>Height of a groin vault at (u, w) in -1..1: two barrels cross, and the ceiling follows whichever is higher.</summary>
    public static float GroinHeight(ArchKind kind, float u, float w, float halfWidth) =>
        MathF.Max(Height(kind, u, halfWidth), Height(kind, w, halfWidth));

    /// <summary>The look of a tier when its themes do not say: round crypts, hanging caves, low forges, soaring abyss.</summary>
    public static ArchKind DefaultFor(int tierIndex) => tierIndex switch
    {
        0 => ArchKind.Semicircular,
        1 => ArchKind.Catenary,
        2 => ArchKind.Segmental,
        3 => ArchKind.Pointed,
        _ => ArchKind.Semicircular,
    };

    /// <summary>Parses a theme's Vault field; empty or unknown falls back to the tier default.</summary>
    public static ArchKind Parse(string? text, int tierIndex)
    {
        if (string.IsNullOrWhiteSpace(text)) return DefaultFor(tierIndex);
        return text.Trim().ToLowerInvariant() switch
        {
            "flat" => ArchKind.Flat,
            "semicircular" or "round" or "barrel" or "romanesque" => ArchKind.Semicircular,
            "pointed" or "gothic" => ArchKind.Pointed,
            "segmental" or "segmented" => ArchKind.Segmental,
            "catenary" => ArchKind.Catenary,
            _ => DefaultFor(tierIndex),
        };
    }

    /// <summary>
    /// The piece for a cell from which of its four sides open onto more floor: a run with both ends open (or a dead end)
    /// is a barrel along that axis; rooms, corners and junctions get a groin bay whose four edges are arches.
    /// </summary>
    public static VaultPiece PieceFor(bool west, bool east, bool north, bool south)
    {
        var alongX = west || east;
        var alongZ = north || south;
        if (alongX && !alongZ) return VaultPiece.BarrelX;
        if (alongZ && !alongX) return VaultPiece.BarrelZ;
        return VaultPiece.Groin;
    }
}
