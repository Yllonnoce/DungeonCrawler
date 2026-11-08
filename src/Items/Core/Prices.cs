namespace DungeonCrawler.Items;

/// <summary>What an item is worth in coins: base by kind, times rarity, times an item-level factor. Engine-free.</summary>
public static class Prices
{
    /// <summary>Buying from the merchant costs this many times the item's value.</summary>
    public const double BuyMultiplier = 2.5;

    /// <summary>One of the item: base by kind, rarity and level factors. An arrow stack is the exception: 0.3 a piece for the whole bundle, nothing else.</summary>
    public static int UnitValue(ItemInstance item)
    {
        if (item.Kind == SlotKind.Ammo) return (int)Math.Floor(0.3 * item.StackCount);
        var raw = Base(item.Kind) * RarityFactor(item.Rarity) * (1 + 0.15 * item.ItemLevel);
        return (int)Math.Floor(raw + 1e-9); // the epsilon keeps exact products like 144.0 from landing at 143.99999
    }

    /// <summary>The whole stack's worth: unit value times count (arrows are already priced as a bundle).</summary>
    public static int Value(ItemInstance item) => item.Kind == SlotKind.Ammo ? UnitValue(item) : UnitValue(item) * item.StackCount;

    /// <summary>What the merchant pays, with an optional bonus fraction (The Alderman's Seal).</summary>
    public static int SellPrice(ItemInstance item, float bonus = 0f) => (int)Math.Floor(Value(item) * (1 + (double)bonus) + 1e-9);

    public static int UnitBuyPrice(ItemInstance item) => (int)Math.Floor(BuyMultiplier * UnitValue(item) + 1e-9);

    /// <summary>What the merchant charges for the whole stack: the unit price times count (arrows are a bundle already).</summary>
    public static int BuyPrice(ItemInstance item) => item.Kind == SlotKind.Ammo ? UnitBuyPrice(item) : UnitBuyPrice(item) * item.StackCount;

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
