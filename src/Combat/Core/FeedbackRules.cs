namespace DungeonCrawler.Combat;

/// <summary>What a landed hit reads as: the colour and size of its number, the verb in the log.</summary>
public enum HitFlavor : byte { Hit, Crit, PerfectBlow, Shatter, Blocked, Bleed, Poison, Burn, Frost }

/// <summary>Engine-free rules for how damage is shown (numbers, bars, markers); Task 8 adds the constants and the merger.</summary>
public static class FeedbackRules
{
    /// <summary>Loudest first: Shatter > PerfectBlow > Blocked (a shield ate it) > Crit > Hit. A miss is a plain Hit flavor (nothing is shown for it).</summary>
    public static HitFlavor FlavorOf(DamageResult r, bool perfectBlow)
    {
        if (!r.Hit) return HitFlavor.Hit;
        if (r.Shatter) return HitFlavor.Shatter;
        if (perfectBlow) return HitFlavor.PerfectBlow;
        if (r.Amount <= 0f) return HitFlavor.Blocked;
        if (r.Crit) return HitFlavor.Crit;
        return HitFlavor.Hit;
    }

    public static HitFlavor TickFlavor(StatusKind k) => k switch
    {
        StatusKind.Bleed => HitFlavor.Bleed,
        StatusKind.Poison => HitFlavor.Poison,
        StatusKind.Burn => HitFlavor.Burn,
        _ => HitFlavor.Frost,
    };
}
