using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Save;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>
/// How the party's boons and curses become numbers: every factor is 1 (or 0) with nothing held and the spec's constant
/// when the id is in <see cref="AdventureDto.Boons"/> / <see cref="AdventureDto.Curses"/>. Engine-free; the
/// <c>FortuneDirector</c> pushes the static factors (prices, hazards, potions) and the stat modifiers into play.
/// </summary>
public static class FortuneRules
{
    public const float GoldFoundFactor = 1.30f;
    public const float DetectFactor_Loud = 1.3f;
    public const float PriceFactor_Miser = 1.4f;
    public const float HazardFactor_Brittle = 1.5f;
    public const float HealFactor_Thirst = 0.5f;
    public const float GasThickness_StoneLungs = 0.5f;
    public const float IgniteNovaRadius = 4f;
    public const int ShatterChainExtra = 1;
    public const float ShatterChainRadius = 4f;
    public const float BlockHealAmount = 10f;
    public const float LockBonus = 0.25f;
    public const int ExtraElites_Marked = 1;
    public const float KindledBladesChance = 0.20f;
    public const float KindledBurnSeconds = 4f, KindledBurnMagnitude = 3f;
    public const float RimeFrostSeconds = 2f;
    public const float EmberDueExtraSeconds = 3f;
    // the bound chest and the hunt
    public const int BoundFromFloor = 5;
    public const float HuntedSeconds = 60f;
    public const float VulnerableMagnitude = 0.25f;
    /// <summary>The hunter's affixes by name (the contract's list has no "Marked"; the Marked set is this). Elites' EliteRules.Apply reads them at merge.</summary>
    public static readonly string[] HunterAffixes = { "Swift", "Vampiric" };
    public const string HunterName = "the Hunter";
    public const string BoundPrompt = "Bound chest: open and be Hunted?";
    public const int HunterActor = 7999;

    /// <summary>A boon or a curse the party holds.</summary>
    public static bool Has(AdventureDto? adv, string id) => adv != null && (adv.Boons.Contains(id) || adv.Curses.Contains(id));

    public static IEnumerable<StatModifier> StatModifiers(AdventureDto adv) => Boons.Modifiers(adv.Boons).Concat(Curses.Modifiers(adv.Curses));

    public static int GoldFound(int coins, AdventureDto? adv) => (int)Math.Round(coins * (Has(adv, "toll_road") ? GoldFoundFactor : 1f));
    public static float DetectFactor(AdventureDto? adv) => Has(adv, "loud") ? DetectFactor_Loud : 1f;
    /// <summary>Miser's Mark times whatever clause factor run-structure hands in.</summary>
    public static float PriceFactor(AdventureDto? adv, float clauseFactor = 1f) => (Has(adv, "misers_mark") ? PriceFactor_Miser : 1f) * clauseFactor;
    public static float HazardFactor(AdventureDto? adv) => Has(adv, "brittle") ? HazardFactor_Brittle : 1f;
    public static float HealFactor(AdventureDto? adv) => Has(adv, "thirst") ? HealFactor_Thirst : 1f;
    public static float GasThickness(AdventureDto? adv) => Has(adv, "stone_lungs") ? GasThickness_StoneLungs : 1f;
    public static float IgniteRadius(AdventureDto? adv, float baseRadius) => Has(adv, "ember_crown") ? IgniteNovaRadius : baseRadius;
    public static int ShatterChain(AdventureDto? adv) => Has(adv, "cold_crown") ? ShatterChainExtra : 0;
    public static int ExtraElites(AdventureDto? adv) => Has(adv, "marked") ? ExtraElites_Marked : 0;

    /// <summary>How long a status on a party member lasts: Cold Comfort halves Frost, Slow Blood halves Bleed and Poison.</summary>
    public static float DurationScale(AdventureDto? adv, StatusKind kind) => kind switch
    {
        StatusKind.Frost when Has(adv, "cold_comfort") => 0.5f,
        StatusKind.Bleed or StatusKind.Poison when Has(adv, "slow_blood") => 0.5f,
        _ => 1f,
    };

    /// <summary>Adds a boon; false when already held. Throws on an unknown id.</summary>
    public static bool Take(AdventureDto adv, string boonId)
    {
        Boons.Get(boonId);
        if (adv.Boons.Contains(boonId)) return false;
        adv.Boons.Add(boonId);
        return true;
    }

    /// <summary>Lays a curse; false when already carried. Throws on an unknown id.</summary>
    public static bool Curse(AdventureDto adv, string curseId)
    {
        Curses.Get(curseId);
        if (adv.Curses.Contains(curseId)) return false;
        adv.Curses.Add(curseId);
        return true;
    }

    /// <summary>Lifts a curse; false when it was not carried.</summary>
    public static bool Lift(AdventureDto adv, string curseId) => adv.Curses.Remove(curseId);

    /// <summary>One of the eight the party does not carry yet; null when all eight are on them. Scar is never dealt.</summary>
    public static string? RandomCurse(AdventureDto adv, Random rng)
    {
        var pool = Curses.Offerable.Where(c => !adv.Curses.Contains(c.Id)).Select(c => c.Id).ToList();
        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }
}
