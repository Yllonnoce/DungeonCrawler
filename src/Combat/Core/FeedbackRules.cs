namespace DungeonCrawler.Combat;

/// <summary>What a landed hit reads as: the colour and size of its number, the verb in the log.</summary>
public enum HitFlavor : byte { Hit, Crit, PerfectBlow, Shatter, Blocked, Bleed, Poison, Burn, Frost }

/// <summary>Engine-free rules for how damage is shown: numbers, bars, the hit marker, the boss bar, the vignette and the banners.</summary>
public static class FeedbackRules
{
    public const float NumberSeconds = 0.9f, MergeSeconds = 0.15f, CritScale = 1.6f, TickScale = 0.7f;
    public const float BarLingerSeconds = 4f, BarHeight = 1.2f, HitMarkerSeconds = 0.12f;
    public const float HeavyHitFraction = 0.15f, ShakeAmplitude = 0.06f, ShakeSeconds = 0.3f, VignetteSeconds = 0.5f;
    public const float LevelUpPulseSeconds = 2f, BannerSeconds = 2.5f, ToastSeconds = 3f;
    public const int BarPoolSize = 12, NumberPoolSize = 24;

    public static string ColorHex(HitFlavor f) => f switch
    {
        HitFlavor.Crit or HitFlavor.PerfectBlow or HitFlavor.Shatter => "#ffd24a",
        HitFlavor.Blocked => "#9a9a9a",
        HitFlavor.Bleed => "#e03030",
        HitFlavor.Poison => "#50d050",
        HitFlavor.Burn => "#ff8a20",
        HitFlavor.Frost => "#60a8ff",
        _ => "#ffffff",
    };

    public static bool IsTick(HitFlavor f) => f is HitFlavor.Bleed or HitFlavor.Poison or HitFlavor.Burn or HitFlavor.Frost;
    public static float Scale(HitFlavor f) => f is HitFlavor.Crit or HitFlavor.PerfectBlow or HitFlavor.Shatter ? CritScale : IsTick(f) ? TickScale : 1f;
    public static string Text(HitFlavor f, float amount) => f == HitFlavor.Blocked ? "Blocked" : f == HitFlavor.Shatter ? $"Shatter {amount:0}" : $"{amount:0}";

    /// <summary>Louder flavors win a merge: Shatter > PerfectBlow > Crit > Hit (ticks keep the newer tick's colour).</summary>
    public static int Loudness(HitFlavor f) => f switch { HitFlavor.Shatter => 3, HitFlavor.PerfectBlow => 2, HitFlavor.Crit => 1, _ => 0 };

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

/// <summary>Numbers within <see cref="FeedbackRules.MergeSeconds"/> on one target become one number; ticks merge with ticks only; a "Blocked" never merges.</summary>
public sealed class DamageNumberMerger
{
    public sealed class Entry
    {
        public int Target;
        public float Amount;
        public HitFlavor Flavor;
        public float Born;
        public float Age;
        /// <summary>Set when a merge changed the entry since the view last drew it; the view clears it.</summary>
        public bool Dirty;
    }

    private readonly List<Entry> _entries = new();
    public IReadOnlyList<Entry> Entries => _entries;

    public Entry Add(int target, float amount, HitFlavor flavor, float now)
    {
        if (flavor != HitFlavor.Blocked)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Target != target) continue;
                if (now - e.Born <= FeedbackRules.MergeSeconds && e.Flavor != HitFlavor.Blocked && FeedbackRules.IsTick(flavor) == FeedbackRules.IsTick(e.Flavor))
                {
                    e.Amount += amount;
                    if (FeedbackRules.Loudness(flavor) > FeedbackRules.Loudness(e.Flavor)) e.Flavor = flavor;
                    e.Dirty = true;
                    return e;
                }
                break; // only the newest entry of a target can take a merge
            }
        }
        var fresh = new Entry { Target = target, Amount = amount, Flavor = flavor, Born = now, Dirty = true };
        _entries.Add(fresh);
        return fresh;
    }

    public void Update(float dt)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            _entries[i].Age += dt;
            if (_entries[i].Age >= FeedbackRules.NumberSeconds) _entries.RemoveAt(i);
        }
    }
}
