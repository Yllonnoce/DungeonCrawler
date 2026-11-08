namespace DungeonCrawler.Enemies;

/// <summary>What a Support caster puts on a hurt ally.</summary>
public enum SupportKind { Shield, Regen }

/// <summary>Bombers: one horde creature in eight is a bomber from floor 6; it rushes and detonates (3 m, its damage ×3, no corpse).</summary>
public static class BomberRules
{
    public const int FromFloor = 6, OneIn = 8;
    public const float Radius = 3f, DamageMultiplier = 3f, FuseSeconds = 0.35f;

    /// <summary>The ordinal in a stream that comes out a bomber (the eighth, sixteenth, …) from <see cref="FromFloor"/>.</summary>
    public static bool IsBomberSlot(int floor, int ordinal) => floor >= FromFloor && ordinal % OneIn == OneIn - 1;

    /// <summary>A stream's ordinary fodder for a tier: the tier's horde creatures that are not bombers (bombers come only through <see cref="IsBomberSlot"/>).</summary>
    public static bool IsPlainFodder(EnemyProfile p, int tierIndex) => p.Faction == "horde" && !p.IsBoss && !p.Bomber && (p.TierIndex < 0 || p.TierIndex == tierIndex);

    /// <summary>The tier's bomber on the roster, or null when it has none.</summary>
    public static string? BomberFor(IReadOnlyDictionary<string, EnemyProfile> roster, int tierIndex)
        => roster.Where(kv => kv.Value.Bomber && kv.Value.Faction == "horde" && kv.Value.TierIndex == tierIndex).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
}

/// <summary>Support casters: an ally within 8 m below 70% HP gets a 30-point Shield or 3/s Regen, every 8 s, instead of a bolt.</summary>
public static class SupportRules
{
    public const float Range = 8f, AllyHpFraction = 0.7f, Cooldown = 8f, ShieldPoints = 30f, ShieldSeconds = 12f, RegenPerSecond = 3f, RegenSeconds = 6f;
}

/// <summary>Kiting shooters back off when the party is within half their preferred range, for at most a couple of seconds.</summary>
public static class KiteRules
{
    public const float RetreatFraction = 0.5f, MaxSeconds = 2.5f;
    /// <summary>After a retreat that ran out of time (a wall, a faster party) the shooter stands and fights this long before trying again.</summary>
    public const float CooldownSeconds = 4f;
    public static bool ShouldKite(float dist, float preferred) => dist < preferred * RetreatFraction;
    public static bool MayKite(float dist, float preferred, float cooldownLeft) => cooldownLeft <= 0f && ShouldKite(dist, preferred);
}
