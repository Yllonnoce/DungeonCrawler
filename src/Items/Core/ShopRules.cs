using DungeonCrawler.Combat;
using DungeonCrawler.Save;
namespace DungeonCrawler.Items;

/// <summary>The merchant's last three sales, newest first, each at the price he paid: the party may buy them back for that.</summary>
public sealed class BuybackLedger
{
    public const int Capacity = 3;
    private readonly List<(ItemInstance Item, int Price)> _entries = new();

    public IReadOnlyList<(ItemInstance Item, int Price)> Entries => _entries;

    /// <summary>Remembers a sale; the fourth push drops the oldest.</summary>
    public void Push(ItemInstance item, int price)
    {
        _entries.Insert(0, (item, price));
        while (_entries.Count > Capacity) _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>Removes and returns entry <paramref name="index"/>; null when there is none.</summary>
    public (ItemInstance Item, int Price)? Take(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;
        var e = _entries[index];
        _entries.RemoveAt(index);
        return e;
    }

    public List<SoldDto> ToDto() => _entries.Select(e => new SoldDto { Item = SaveMapper.ToDto(e.Item), Price = e.Price }).ToList();

    /// <summary>Replaces the ledger with a saved one; null (an older save, an empty sync) clears it.</summary>
    public void Restore(List<SoldDto>? dtos, ItemCatalog cat)
    {
        _entries.Clear();
        if (dtos == null) return;
        foreach (var d in dtos.Take(Capacity)) _entries.Add((SaveMapper.FromDto(d.Item, cat), d.Price));
    }
}

/// <summary>What the merchant has on one floor: the items in order, and where the unique sits (-1 when the floor has none).</summary>
public sealed class ShopStock
{
    public List<ItemInstance> Items { get; } = new();
    public int UniqueIndex { get; init; } = -1;
    /// <summary>Where the hidden stall's wares start in <see cref="Items"/> (fortune); -1 when the floor has no stall. The regular wares are [0, HiddenFrom).</summary>
    public int HiddenFrom { get; init; } = -1;
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

    /// <summary>The hidden stall's wares: this many Rare pieces of gear after the regular stock (fortune).</summary>
    public const int HiddenStallCount = Adventure.Fortune.SecretRoomRules.HiddenStallCount;

    /// <param name="hiddenStall">The floor carries a hidden merchant's stall: its Rare gear is appended after the regular wares, from its own seed, so the wares are as they were.</param>
    public static ShopStock Roll(ItemCatalog cat, ulong gameSeed, int floor, bool hiddenStall = false)
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

        var hiddenFrom = -1;
        if (hiddenStall)
        {
            hiddenFrom = items.Count;
            var stallRng = new Random(StockSeed(gameSeed, floor) ^ 0x41DD);
            var kinds = new[] { SlotKind.Weapon, SlotKind.Body, SlotKind.Offhand, SlotKind.Ring, SlotKind.Amulet };
            for (var i = 0; i < HiddenStallCount; i++)
            {
                var kind = kinds[i % kinds.Length];
                var c = cat.Bases.Values.Where(b => b.Kind == kind && b.ItemLevelMin <= level).OrderBy(b => b.Id).ToList();
                items.Add(LootRoller.Generate(cat, c[stallRng.Next(c.Count)], Rarity.Rare, level, stallRng));
            }
        }

        var stock = new ShopStock { UniqueIndex = uniqueIndex, HiddenFrom = hiddenFrom };
        stock.Items.AddRange(items);
        return stock;
    }

    /// <summary>Buys stock item <paramref name="index"/> into the bag. Null on success, else the reason it was refused (nothing changes then).</summary>
    public static string? TryBuy(ShopStock stock, ISet<int> sold, int index, Inventory bag, ref int gold)
    {
        if (index < 0 || index >= stock.Items.Count) return "Nothing there.";
        if (sold.Contains(index)) return "Sold out.";
        var item = stock.Items[index];
        var price = Prices.BuyPrice(item);
        if (gold < price) return "Not enough gold.";
        if (!Fits(bag, item)) return "The bag is full.";
        bag.TryAdd(item.Clone());
        sold.Add(index);
        gold -= price;
        return null;
    }

    /// <summary>Sells the bag's item at <paramref name="bagIndex"/>, remembering it in the <paramref name="ledger"/> at the price paid. Null on success, else the reason it was refused.</summary>
    public static string? TrySell(Inventory bag, int bagIndex, ref int gold, BuybackLedger? ledger = null, float sellBonus = 0f)
    {
        var item = bag[bagIndex];
        if (item == null) return "Nothing there.";
        if (!Prices.Sellable(item)) return $"{item.DisplayName} cannot be sold.";
        bag.RemoveAt(bagIndex);
        var price = Prices.SellPrice(item, sellBonus);
        gold += price;
        ledger?.Push(item.Clone(), price);
        return null;
    }

    /// <summary>The question asked before a legendary is sold: the price (with any sell bonus) and what the merchant keeps.</summary>
    public static string SellQuestion(ItemInstance item, float sellBonus = 0f)
        => $"Sell {item.DisplayName} for {Prices.SellPrice(item, sellBonus)} gold? He keeps only his last three purchases for buyback.";

    /// <summary>Buys back ledger entry <paramref name="index"/> for what the merchant paid. Null on success, else the reason (nothing changes then).</summary>
    public static string? TryBuyback(BuybackLedger ledger, int index, Inventory bag, ref int gold)
    {
        if (index < 0 || index >= ledger.Entries.Count) return "Nothing there.";
        var (item, price) = ledger.Entries[index];
        if (gold < price) return "Not enough gold.";
        if (!Fits(bag, item)) return "The bag is full.";
        ledger.Take(index);
        bag.TryAdd(item);
        gold -= price;
        return null;
    }

    /// <summary>Whether the whole item fits, so a refused buy never leaves a half-merged stack behind.</summary>
    private static bool Fits(Inventory bag, ItemInstance item)
    {
        if (bag.FreeSlots > 0) return true;
        if (!item.Stackable) return false;
        var room = 0;
        for (var i = 0; i < bag.Capacity; i++)
            if (bag[i] is { Stackable: true } s && s.DefId == item.DefId) room += Math.Max(0, s.MaxStack - s.StackCount);
        return room >= item.StackCount;
    }
}
