using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Items;
using Xunit;

namespace DungeonCrawler.Tests.Items;

public class LootRollerTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();

    [Fact]
    public void StackSize_ArrowBundles_HoldFiveToTen_OtherStacksOneToThree()
    {
        var rng = new Random(7);
        var arrows = new ItemBase { Id = "item.arrow", Name = "Arrow", Kind = SlotKind.Ammo, Tags = new[] { "ammo" }, Stackable = true, MaxStack = 50 };
        var potion = new ItemBase { Id = "item.potion", Name = "Health Potion", Kind = SlotKind.Consumable, Tags = new[] { "consumable" }, Stackable = true, MaxStack = 10 };
        var arrowRolls = Enumerable.Range(0, 300).Select(_ => LootRoller.StackSize(arrows, rng)).ToList();
        var potionRolls = Enumerable.Range(0, 300).Select(_ => LootRoller.StackSize(potion, rng)).ToList();
        Assert.All(arrowRolls, n => Assert.InRange(n, 5, 10));
        Assert.Contains(5, arrowRolls); Assert.Contains(10, arrowRolls);
        Assert.All(potionRolls, n => Assert.InRange(n, 1, 3));
        Assert.Equal(1, LootRoller.StackSize(new ItemBase { Id = "item.ring", Name = "Ring", Kind = SlotKind.Ring }, rng));
    }

    [Fact]
    public void Rarity_DistributionFollowsWeights()
    {
        var rng = new Random(1);
        var counts = new int[4];
        for (var i = 0; i < 20000; i++) counts[(int)LootRoller.RollRarity(new float[] { 60, 28, 10, 2 }, 1, rng)]++;
        Assert.InRange(counts[0] / 20000.0, 0.57, 0.63);
        Assert.InRange(counts[1] / 20000.0, 0.25, 0.31);
        Assert.InRange(counts[2] / 20000.0, 0.08, 0.12);
        Assert.InRange(counts[3] / 20000.0, 0.012, 0.028);
    }

    [Fact]
    public void Rarity_LegendaryMoreLikelyDeeper()
    {
        var rng = new Random(2);
        int Count(int level) { var n = 0; for (var i = 0; i < 20000; i++) if (LootRoller.RollRarity(new float[] { 60, 28, 10, 2 }, level, rng) == Rarity.Legendary) n++; return n; }
        Assert.True(Count(10) > Count(1) * 1.8);
    }

    [Fact]
    public void Roll_IsDeterministicForSeed()
    {
        var a = Enumerable.Range(0, 30).Select(_ => LootRoller.Roll(_cat, _cat.Table("table.all"), 4, new Random(99))?.DisplayName).ToList();
        var b = Enumerable.Range(0, 30).Select(_ => LootRoller.Roll(_cat, _cat.Table("table.all"), 4, new Random(99))?.DisplayName).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Roll_AffixCountMatchesRarityAndNoDuplicates()
    {
        var rng = new Random(3);
        for (var i = 0; i < 3000; i++)
        {
            var item = LootRoller.Roll(_cat, _cat.Table("table.all"), 6, rng);
            if (item == null || !item.IsEquippable) continue;
            var rolled = item.Affixes.Count(a => a.Tier > 0);
            switch (item.Rarity)
            {
                case Rarity.Common: Assert.Equal(0, rolled); break;
                case Rarity.Magic: Assert.InRange(rolled, 1, 2); break;
                case Rarity.Rare: Assert.InRange(rolled, 2, 4); break; // may be capped by the eligible pool
                case Rarity.Legendary: Assert.NotNull(item.LegendaryName); Assert.InRange(rolled, 0, 2); break;
            }
            Assert.Equal(item.Affixes.Count, item.Affixes.Select(a => a.AffixId).Distinct().Count());
            Assert.True(item.Affixes.Count(a => a.IsPrefix) <= LootRoller.MaxPrefixes);
            Assert.True(item.Affixes.Count(a => !a.IsPrefix && a.Tier > 0) <= LootRoller.MaxSuffixes);
            foreach (var a in item.Affixes.Where(a => a.Tier > 0))
                Assert.True(_cat.Affixes[a.AffixId].Tags.Any(t => item.Tags.Contains(t)), $"{a.Name} on {item.DisplayName}");
        }
    }

    [Fact]
    public void Roll_RespectsItemLevelForBasesAndTiers()
    {
        var rng = new Random(4);
        for (var i = 0; i < 2000; i++)
        {
            var item = LootRoller.Roll(_cat, _cat.Table("table.all"), 1, rng);
            if (item == null) continue;
            Assert.NotEqual("item.plate", item.DefId);
            foreach (var a in item.Affixes.Where(a => a.Tier > 0))
            {
                Assert.NotEqual("affix.warding", a.AffixId); // needs level 3
                Assert.Equal(1, a.Tier);
            }
        }
        var high = Enumerable.Range(0, 2000).Select(_ => LootRoller.Roll(_cat, _cat.Table("table.rare"), 6, rng)).Where(x => x != null).ToList();
        Assert.Contains(high, x => x!.DefId == "item.plate");
        Assert.Contains(high, x => x!.Affixes.Any(a => a.Tier == 2));
    }

    [Fact]
    public void Legendary_HasFixedPowersAndTwoRolledAffixes()
    {
        var rng = new Random(5);
        var item = LootRoller.Roll(_cat, _cat.Table("table.weapons"), 3, rng)!;
        Assert.Equal(Rarity.Legendary, item.Rarity);
        Assert.Equal("Ember Fang", item.DisplayName);
        Assert.Equal("Ember Fang", item.Affixes[0].Name);
        Assert.Contains(item.Modifiers, m => m.Stat == Stat.CritChance);
        Assert.Contains(item.Weapon!.OnHit, o => o.Kind == StatusKind.Burn);
        Assert.Equal(2, item.Affixes.Count(a => a.Tier > 0));
    }

    [Fact]
    public void Legendary_FallsBackToRareWhenNoneEligible()
    {
        var rng = new Random(6);
        // shield legendary needs level 5; weapons table at level 1 only has Ember Fang (level 1) - use a table with only armor
        _cat.Tables["table.armor_leg"] = new LootTableProfile { Id = "x", Entries = new[] { new LootEntry("kind:Body", 1) }, RarityWeights = new float[] { 0, 0, 0, 1 } };
        var item = LootRoller.Roll(_cat, _cat.Table("table.armor_leg"), 2, rng)!;
        Assert.Equal(Rarity.Rare, item.Rarity);
        Assert.Null(item.LegendaryName);
    }

    [Fact]
    public void DisplayName_UsesFirstPrefixAndSuffix()
    {
        var item = _cat.Create("item.longsword");
        item.Affixes.Add(new RolledAffix { AffixId = "a", Name = "Sharp", IsPrefix = true, Tier = 1 });
        item.Affixes.Add(new RolledAffix { AffixId = "b", Name = "of the Bear", IsPrefix = false, Tier = 1 });
        item.Affixes.Add(new RolledAffix { AffixId = "c", Name = "Swift", IsPrefix = true, Tier = 1 });
        Assert.Equal("Sharp Longsword of the Bear", item.DisplayName);
    }

    [Fact]
    public void MulAffix_RollsAsPercentFactor()
    {
        var rng = new Random(7);
        var affix = _cat.Affixes["affix.swift"];
        for (var i = 0; i < 50; i++)
        {
            var r = LootRoller.RollAffix(affix, 1, rng);
            var m = r.Modifiers.Single();
            Assert.Equal(ModOp.Mul, m.Op);
            Assert.InRange(m.Value, 1.05f, 1.10f);
        }
    }

    [Fact]
    public void DropChance_ProducesNulls()
    {
        var rng = new Random(8);
        var table = _cat.Table("table.all") with { DropChance = 0.25f };
        var hits = Enumerable.Range(0, 4000).Count(_ => LootRoller.Roll(_cat, table, 1, rng) != null);
        Assert.InRange(hits, 850, 1150);
    }

    [Fact]
    public void ModifierParser_ParsesAndDescribes()
    {
        var add = StatModifierParser.Parse("Armor+12");
        Assert.Equal((Stat.Armor, ModOp.Add, 12f), (add.Stat, add.Op, add.Value));
        var mul = StatModifierParser.Parse("AttackSpeed*1.15");
        Assert.Equal((Stat.AttackSpeed, ModOp.Mul, 1.15f), (mul.Stat, mul.Op, mul.Value));
        var neg = StatModifierParser.Parse("Evasion-4");
        Assert.Equal(-4f, neg.Value);
        Assert.Equal("+12 Armor", StatModifierParser.Describe(add));
        Assert.Equal("+15% Attack speed", StatModifierParser.Describe(mul));
        Assert.Equal("+10% Crit chance", StatModifierParser.Describe(new StatModifier(Stat.CritChance, ModOp.Add, 0.1f)));
        Assert.Throws<FormatException>(() => StatModifierParser.Parse("Bogus+1"));
    }

    [Fact]
    public void ItemText_ShowsDeltasAgainstEquipped()
    {
        var a = _cat.Create("item.longsword");
        a.Affixes.Add(new RolledAffix { AffixId = "x", Name = "Sharp", IsPrefix = true, Tier = 1, Modifiers = { new StatModifier(Stat.MeleeDamage, ModOp.Add, 3) } });
        var b = _cat.Create("item.spear");
        var text = ItemText.Describe(a, b);
        Assert.Contains("Equipped: Spear", text);
        Assert.Contains("-1.5 average damage", text);
        Assert.Contains("+3 Melee damage", text);
    }
}
