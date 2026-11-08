using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>Reads legendary effects off a member's equipment through the catalog: nothing is cached, so unequipping stops the effect.</summary>
public static class LegendaryRules
{
    /// <summary>The legendary an item is, found by its fixed (tier 0) affix; null for an ordinary item.</summary>
    public static LegendaryProfile? Of(ItemInstance item, ItemCatalog cat)
    {
        foreach (var a in item.Affixes)
            if (a.Tier == 0 && cat.Legendaries.TryGetValue(a.AffixId, out var l)) return l;
        return null;
    }

    private static IEnumerable<LegendaryProfile> Worn(Equipment eq, ItemCatalog cat) => eq.Items.Select(t => Of(t.Item, cat)).Where(l => l != null)!;

    /// <summary>The summed magnitude of that trigger over every equipped legendary.</summary>
    public static float Trigger(Equipment eq, ItemCatalog cat, TriggerKind kind)
        => Worn(eq, cat).SelectMany(l => l.Triggers).Where(t => t.Kind == kind).Sum(t => t.Magnitude);

    public static IEnumerable<AbilityUpgrade> Upgrades(Equipment eq, ItemCatalog cat) => Worn(eq, cat).SelectMany(l => l.Upgrades);

    /// <summary>Coins picked up with a gold-find bonus, rounded to the nearest coin.</summary>
    public static float GoldFound(int coins, float bonus) => (int)Math.Round(coins * (1 + bonus), MidpointRounding.AwayFromZero);
}
