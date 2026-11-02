namespace DungeonCrawler.Items;

/// <summary>What an item is worth in coins: base by kind, times rarity, times an item-level factor. Engine-free.</summary>
public static class Prices
{
    /// <summary>Buying from the merchant costs this many times the item's value.</summary>
    public const double BuyMultiplier = 2.5;

    public static int Value(ItemInstance item)
    {
        // An arrow stack is worth 0.3 a piece and nothing else: no rarity or level factor.
        if (item.Kind == SlotKind.Ammo) return (int)Math.Floor(0.3 * item.StackCount);
        var raw = Base(item.Kind) * RarityFactor(item.Rarity) * (1 + 0.15 * item.ItemLevel);
        return (int)Math.Floor(raw + 1e-9); // the epsilon keeps exact products like 144.0 from landing at 143.99999
    }

    public static int SellPrice(ItemInstance item) => Value(item);

    public static int BuyPrice(ItemInstance item) => (int)Math.Floor(BuyMultiplier * Value(item) + 1e-9);

    /// <summary>Keys and Recover-quest relics cannot be sold.</summary>
    public static bool Sellable(ItemInstance item) => item.DefId is not ("item.iron_key" or "item.quest_relic");

    private static double Base(SlotKind kind) => kind switch
    {
        SlotKind.Weapon => 20,
        SlotKind.Body => 18,
        SlotKind.Head or SlotKind.Hands or SlotKind.Feet => 10,
        SlotKind.Offhand => 12,
        SlotKind.Ring or SlotKind.Amulet => 15,
        SlotKind.Consumable => 4,
        _ => 0,
    };

    private static double RarityFactor(Rarity r) => r switch
    {
        Rarity.Magic => 2.2,
        Rarity.Rare => 4.5,
        Rarity.Legendary => 12,
        _ => 1,
    };
}
