using DungeonCrawler.Characters;
namespace DungeonCrawler.Items;

/// <summary>Deterministic item generation: table -> base -> rarity -> affixes (or a legendary).</summary>
public static class LootRoller
{
    public const int MaxPrefixes = 2;
    public const int MaxSuffixes = 2;

    public static Rarity RollRarity(IReadOnlyList<float> weights, int itemLevel, Random rng)
    {
        var w = new float[4];
        for (var i = 0; i < 4 && i < weights.Count; i++) w[i] = Math.Max(0, weights[i]);
        var depth = Math.Max(0, itemLevel - 1);
        w[(int)Rarity.Rare] *= 1f + 0.05f * depth;
        w[(int)Rarity.Legendary] *= 1f + 0.15f * depth;
        return (Rarity)WeightedIndex(w, rng);
    }

    public static int AffixCount(Rarity rarity, Random rng) => rarity switch
    {
        Rarity.Magic => rng.Next(1, 3),
        Rarity.Rare => rng.Next(3, 5),
        Rarity.Legendary => 2,
        _ => 0,
    };

    /// <summary>How many of a found stack: a bundle of arrows is five to ten, any other stackable one to three, the rest one.</summary>
    public static int StackSize(ItemBase b, Random rng)
    {
        if (!b.Stackable) return 1;
        return b.Kind == SlotKind.Ammo ? rng.Next(5, 11) : rng.Next(1, 4);
    }

    /// <summary>One roll on a table. Null when the drop chance fails or nothing matches the level.</summary>
    public static ItemInstance? Roll(ItemCatalog cat, LootTableProfile table, int itemLevel, Random rng)
    {
        if (table.Entries.Length == 0) return null;
        if (table.DropChance < 1f && rng.NextDouble() >= table.DropChance) return null;

        var entry = table.Entries[WeightedIndex(table.Entries.Select(e => e.Weight).ToArray(), rng)];
        var candidates = cat.Bases.Values.Where(b => entry.Matches(b) && b.ItemLevelMin <= itemLevel).OrderBy(b => b.Id).ToList();
        if (candidates.Count == 0) return null;
        var b = candidates[rng.Next(candidates.Count)];

        if (!b.IsEquippable)
            return ItemInstance.FromBase(b, StackSize(b, rng));

        var rarity = RollRarity(table.RarityWeights, itemLevel, rng);
        if (rarity == Rarity.Legendary)
        {
            var legs = cat.Legendaries.Values
                .Where(l => l.ItemLevelMin <= itemLevel && cat.Bases.TryGetValue(l.BaseId, out var lb) && entry.Matches(lb))
                .OrderBy(l => l.Id).ToList();
            if (legs.Count > 0)
                return MakeLegendary(cat, legs[WeightedIndex(legs.Select(l => l.Weight).ToArray(), rng)], itemLevel, rng);
            rarity = Rarity.Rare;
        }
        return Generate(cat, b, rarity, itemLevel, rng);
    }

    public static List<ItemInstance> RollMany(ItemCatalog cat, LootTableProfile table, int itemLevel, Random rng)
    {
        var list = new List<ItemInstance>();
        for (var i = 0; i < Math.Max(1, table.Rolls); i++)
            if (Roll(cat, table, itemLevel, rng) is { } item) list.Add(item);
        return list;
    }

    public static ItemInstance Generate(ItemCatalog cat, ItemBase b, Rarity rarity, int itemLevel, Random rng)
    {
        var item = ItemInstance.FromBase(b);
        item.Rarity = rarity;
        item.ItemLevel = Math.Max(itemLevel, b.ItemLevelMin);
        AddAffixes(cat, item, b, AffixCount(rarity, rng), itemLevel, rng);
        return item;
    }

    public static ItemInstance MakeLegendary(ItemCatalog cat, LegendaryProfile l, int itemLevel, Random rng)
    {
        var b = cat.Base(l.BaseId);
        var item = ItemInstance.FromBase(b);
        item.Rarity = Rarity.Legendary;
        item.ItemLevel = Math.Max(itemLevel, l.ItemLevelMin);
        item.LegendaryName = l.Name;
        item.Flavor = l.Flavor;
        item.Affixes.Add(new RolledAffix
        {
            AffixId = l.Id, Name = l.Name, IsPrefix = false, Tier = 0,
            Modifiers = l.Modifiers.Select(m => m with { Source = l.Name }).ToList(), OnHit = l.OnHit,
        });
        AddAffixes(cat, item, b, 2, itemLevel, rng);
        return item;
    }

    private static void AddAffixes(ItemCatalog cat, ItemInstance item, ItemBase b, int count, int itemLevel, Random rng)
    {
        for (var i = 0; i < count; i++)
        {
            var prefixes = item.Affixes.Count(a => a.IsPrefix && a.Tier > 0);
            var suffixes = item.Affixes.Count(a => !a.IsPrefix && a.Tier > 0);
            var pool = cat.Affixes.Values
                .Where(a => a.AppliesTo(b) && a.AvailableAt(itemLevel))
                .Where(a => item.Affixes.All(r => r.AffixId != a.Id))
                .Where(a => a.IsPrefix ? prefixes < MaxPrefixes : suffixes < MaxSuffixes)
                .OrderBy(a => a.Id).ToList();
            if (pool.Count == 0) return;
            var affix = pool[WeightedIndex(pool.Select(a => a.Weight).ToArray(), rng)];
            item.Affixes.Add(RollAffix(affix, itemLevel, rng));
        }
    }

    public static RolledAffix RollAffix(AffixProfile affix, int itemLevel, Random rng)
    {
        var eligible = affix.Tiers.Select((t, i) => (t, i)).Where(x => x.t.ItemLevelMin <= itemLevel).ToList();
        var mods = new List<StatModifier>();
        var tierIndex = 1;
        if (eligible.Count > 0)
        {
            // favour the best tier, sometimes fall back to a lower one
            var pick = rng.NextDouble() < 0.6 ? eligible[^1] : eligible[rng.Next(eligible.Count)];
            tierIndex = pick.i + 1;
            var raw = pick.t.Min + (float)rng.NextDouble() * (pick.t.Max - pick.t.Min);
            var value = affix.Op == ModOp.Mul ? 1f + MathF.Round(raw) / 100f : MathF.Round(raw, StatIsWhole(affix.Stat) ? 0 : 1);
            mods.Add(new StatModifier(affix.Stat, affix.Op, value, affix.Name));
        }
        return new RolledAffix { AffixId = affix.Id, Name = affix.Name, IsPrefix = affix.IsPrefix, Tier = tierIndex, Modifiers = mods, OnHit = affix.OnHit };
    }

    private static bool StatIsWhole(Stat s) => StatInfo.IsPrimary(s) || s is Stat.MaxHp or Stat.MaxStamina or Stat.MaxMana or Stat.Armor or Stat.Accuracy or Stat.Evasion or Stat.Stealth or Stat.Perception
        or Stat.ResistPhysical or Stat.ResistFire or Stat.ResistFrost or Stat.ResistPoison or Stat.ResistArcane;

    public static int WeightedIndex(IReadOnlyList<float> weights, Random rng)
    {
        var total = 0f;
        foreach (var w in weights) total += Math.Max(0, w);
        if (total <= 0) return 0;
        var r = (float)rng.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            r -= Math.Max(0, weights[i]);
            if (r < 0) return i;
        }
        return weights.Count - 1;
    }
}
