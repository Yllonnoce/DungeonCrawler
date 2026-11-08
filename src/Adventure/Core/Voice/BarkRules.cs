namespace DungeonCrawler.Adventure.Voice;

/// <summary>Every rate and size of the party's reactive talk, each with the spec sentence it comes from (design spec, section 6). Engine-free.</summary>
public static class BarkRules
{
    /// <summary>"Rate limits stay: 5 s global gap ... for reactive barks."</summary>
    public const float GlobalGapSeconds = 5f;
    /// <summary>"... and 25 s per member per trigger for reactive barks."</summary>
    public const float PerTriggerCooldownSeconds = 25f;
    /// <summary>A hit worth a Hurt line takes at least this share of max health (the director's existing threshold).</summary>
    public const float HurtFraction = 0.15f;
    /// <summary>"LowHp (first time under 25% on a floor)."</summary>
    public const float LowHpFraction = 0.25f;
    /// <summary>A perfect blow or a parry is remarked on this often, like a kill rolls <see cref="KillBarks.Chance"/>: the fight is full of them.</summary>
    public const float QuickChance = 0.4f;
    /// <summary>"Eight lines per hero per trigger": a set with fewer is topped up from stock.</summary>
    public const int ReactiveMinLines = 8;
    /// <summary>The triggers that roll <see cref="QuickChance"/> before speaking.</summary>
    public static readonly BarkTrigger[] Quick = { BarkTrigger.PerfectBlow, BarkTrigger.Parried };

    /// <summary>How long a bark stays in the subtitle strip: 1.5 s plus a twentieth of a second per character, between 2.5 and 6 s.</summary>
    public static float SubtitleSeconds(string text) => Math.Clamp(1.5f + text.Length * 0.05f, 2.5f, 6f);

    /// <summary>"Hurt, Loot, Downed, Revived, BossSeen and LevelUp ... use ChooseOrOldest": every trigger does, except Possess (a hero swap may go unremarked).</summary>
    public static bool UsesOldest(BarkTrigger t) => t != BarkTrigger.Possess;
}

/// <summary>Remembers which members have already been badly hurt on which floor, so LowHp speaks once per member per floor.</summary>
public sealed class LowHpWatch
{
    private readonly HashSet<(int Member, int Floor)> _noted = new();

    /// <summary>True the first time a living member is under <see cref="BarkRules.LowHpFraction"/> on this floor.</summary>
    public bool Note(int member, float hp, float maxHp, int floor)
    {
        if (hp <= 0 || maxHp <= 0 || hp / maxHp >= BarkRules.LowHpFraction) return false;
        return _noted.Add((member, floor));
    }

    public void Reset() => _noted.Clear();
}
