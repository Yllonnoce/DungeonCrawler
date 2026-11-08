using DungeonCrawler.Core;
using DungeonCrawler.Items;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>The three cloth-wrapped bundles a floor: a weapon, a piece of armour, a piece of jewellery.</summary>
public enum DipSlot { Weapon, Armour, Jewellery }

/// <summary>
/// The merchant's Lucky Dip: a fixed price (120 + 30 x floor), rolled once by floor and slot at item level floor + 2 with the
/// chest table's odds under a 10% legendary floor, so a reload cannot reroll it. A Rare or better is a win. Engine-free.
/// </summary>
public static class GambleRules
{
    public const int PriceBase = 120, PricePerFloor = 30, LevelBonus = 2, Actor = 8000;
    public const float LegendaryFloor = 0.10f;
    public static readonly DipSlot[] Slots = { DipSlot.Weapon, DipSlot.Armour, DipSlot.Jewellery };

    /// <param name="discount">A fraction off (Pellet's Receipt); clamped to nine tenths.</param>
    public static int Price(int floor, float discount = 0f) => (int)Math.Round((PriceBase + PricePerFloor * floor) * (1f - Math.Clamp(discount, 0f, 0.9f)));

    private static bool Fits(DipSlot slot, ItemBase b) => slot switch
    {
        DipSlot.Weapon => b.Kind == SlotKind.Weapon,
        DipSlot.Armour => b.Kind is SlotKind.Body or SlotKind.Head or SlotKind.Hands or SlotKind.Feet,
        _ => b.Kind is SlotKind.Ring or SlotKind.Amulet,
    };

    public static ItemInstance Roll(ItemCatalog cat, ulong gameSeed, int floor, DipSlot slot)
    {
        var rng = new Random(Rng.ActorSeed(gameSeed, floor, Actor + (int)slot));
        var level = floor + LevelBonus;
        var bases = cat.Bases.Values.Where(b => Fits(slot, b) && b.ItemLevelMin <= level).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
        if (bases.Count == 0) bases = cat.Bases.Values.Where(b => Fits(slot, b)).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
        var b = bases[rng.Next(bases.Count)];
        var rarity = rng.NextDouble() < LegendaryFloor ? Rarity.Legendary : LootRoller.RollRarity(cat.Table(ChestRules.ChestTable).RarityWeights, level, rng);
        if (rarity == Rarity.Legendary)
        {
            var legs = cat.Legendaries.Values
                .Where(l => !l.ShopOnly && l.ItemLevelMin <= level && cat.Bases.TryGetValue(l.BaseId, out var lb) && lb.ItemLevelMin <= level && Fits(slot, lb))
                .OrderBy(l => l.Id, StringComparer.Ordinal).ToList();
            if (legs.Count > 0) return LootRoller.MakeLegendary(cat, legs[LootRoller.WeightedIndex(legs.Select(l => l.Weight).ToArray(), rng)], level, rng);
            rarity = Rarity.Rare;
        }
        return LootRoller.Generate(cat, b, rarity, level, rng);
    }

    public static List<ItemInstance> RollAll(ItemCatalog cat, ulong gameSeed, int floor) => Slots.Select(s => Roll(cat, gameSeed, floor, s)).ToList();

    public static bool IsWin(ItemInstance it) => it.Rarity >= Rarity.Rare;

    public static string BundleName(DipSlot s) => s switch { DipSlot.Weapon => "Weapon bundle", DipSlot.Armour => "Armour bundle", _ => "Jewellery bundle" };
}
