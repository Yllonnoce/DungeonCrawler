using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>Item power as a function of item level: base scaling, affix rounding and the roller's odds. Engine-free.</summary>
public static class ItemRules
{
    /// <summary>Base weapon damage and the Armor base modifier grow this much per item level above the base's own level.</summary>
    public const float BaseScalePerLevel = 0.04f;
    /// <summary>How often a rolled affix takes its best eligible tier rather than a random one.</summary>
    public const float TopTierChance = 0.6f;
    /// <summary>An on-hit affix's status magnitude grows this fraction per tier above the first.</summary>
    public const float OnHitTierStep = 0.25f;
    /// <summary>How often a roll restricts the base pool to what a living member can use.</summary>
    public const float SmartLootChance = 0.6f;

    public static float BaseScale(int itemLevel, int baseLevel) => 1f + BaseScalePerLevel * Math.Max(0, itemLevel - baseLevel);

    /// <summary>Stats that only make sense as whole numbers.</summary>
    public static bool IsWhole(Stat s) => StatInfo.IsPrimary(s) || s is Stat.MaxHp or Stat.MaxStamina or Stat.MaxMana or Stat.Armor or Stat.Accuracy or Stat.Evasion or Stat.Stealth or Stat.Perception
        or Stat.ResistPhysical or Stat.ResistFire or Stat.ResistFrost or Stat.ResistPoison or Stat.ResistArcane;

    /// <summary>How many decimals a rolled Add value keeps: none for whole stats, two for the crit stats (a 0.02 crit chance must not round away), one otherwise.</summary>
    public static int Decimals(Stat s) => IsWhole(s) ? 0 : s is Stat.CritChance or Stat.CritMultiplier ? 2 : 1;

    public static float RoundStat(Stat s, float raw) => MathF.Round(raw, Decimals(s));
}
