using DungeonCrawler.Combat;
namespace DungeonCrawler.Items;

/// <summary>What the merchant has on one floor: the items in order, and where the unique sits (-1 when the floor has none).</summary>
public sealed class ShopStock
{
    public List<ItemInstance> Items { get; } = new();
    public int UniqueIndex { get; init; } = -1;
}

/// <summary>Engine-free shop rules: which floors carry a unique weapon, of what kind, and the seeded stock per floor.</summary>
public static class ShopRules
{
    public static readonly int[] UniqueFloors = { 3, 6, 9, 12, 15, 18, 20 };
    public static readonly WeaponKind[] UniqueKinds =
        { WeaponKind.Sword, WeaponKind.Axe, WeaponKind.Mace, WeaponKind.Dagger, WeaponKind.Polearm, WeaponKind.Bow, WeaponKind.Staff };

    public static bool IsUniqueFloor(int floor) => Array.IndexOf(UniqueFloors, floor) >= 0;

    /// <summary>The weapon kind of the floor's unique: a seeded shuffle of the seven kinds, one per unique floor.</summary>
    public static WeaponKind UniqueKindFor(ulong gameSeed, int floor)
    {
        var index = Array.IndexOf(UniqueFloors, floor);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(floor), floor, "not a unique floor");
        var deck = (WeaponKind[])UniqueKinds.Clone();
        var rng = new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ 0x0B1A_DE5UL)));
        for (var i = deck.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck[index];
    }

    public static int StockSeed(ulong gameSeed, int floor) => unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0x5106_5106UL) ^ 0x5A0BUL));

    public static ShopStock Roll(ItemCatalog cat, ulong gameSeed, int floor)
    {
        var rng = new Random(StockSeed(gameSeed, floor));
        var level = floor + 2;
        var items = new List<ItemInstance>();

        ItemBase Pick(Func<ItemBase, bool> where)
        {
            var c = cat.Bases.Values.Where(b => where(b) && b.ItemLevelMin <= level).OrderBy(b => b.Id).ToList();
            return c[rng.Next(c.Count)];
        }
        ItemInstance Gear(ItemBase b) => LootRoller.Generate(cat, b, rng.NextDouble() < 0.7 ? Rarity.Magic : Rarity.Rare, level, rng);

        items.Add(Gear(Pick(b => b.Kind == SlotKind.Weapon)));
        items.Add(Gear(Pick(b => b.Kind == SlotKind.Weapon)));
        items.Add(Gear(Pick(b => b.Kind == SlotKind.Body)));
        items.Add(Gear(Pick(b => b.Kind is SlotKind.Offhand or SlotKind.Ring or SlotKind.Amulet)));

        items.Add(cat.Create("item.health_potion", 5));
        items.Add(cat.Create("item.mana_potion", 3));
        items.Add(cat.Create("item.antidote", 2));
        items.Add(cat.Create("item.arrow", 20));
        items.Add(cat.Create("item.arrow", 20));

        var scrolls = cat.Bases.Keys.Where(k => k.StartsWith("item.scroll_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        items.Add(cat.Create(scrolls[rng.Next(scrolls.Count)]));
        var kits = cat.Bases.Keys.Where(k => k.StartsWith("item.", StringComparison.Ordinal) && k.EndsWith("_trap_kit", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        items.Add(cat.Create(kits[rng.Next(kits.Count)]));

        var uniqueIndex = -1;
        if (IsUniqueFloor(floor))
        {
            var kind = UniqueKindFor(gameSeed, floor);
            var leg = cat.Legendaries.Values
                .Where(l => l.ShopOnly && cat.Base(l.BaseId).Weapon?.Kind == kind)
                .OrderBy(l => l.Id, StringComparer.Ordinal).First();
            uniqueIndex = items.Count;
            items.Add(LootRoller.MakeLegendary(cat, leg, level, rng));
        }

        var stock = new ShopStock { UniqueIndex = uniqueIndex };
        stock.Items.AddRange(items);
        return stock;
    }
}
