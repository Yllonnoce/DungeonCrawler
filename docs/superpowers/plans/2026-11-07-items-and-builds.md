# Items and Builds Implementation Plan (The Depths, pillar 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Loot keeps improving to floor 20 and beyond, a legendary changes how you play, every skill branch ends in a keystone with a price, and the shop and bag tell you what is worth taking.

**Architecture:** Item power becomes a function of item level everywhere it was flat: affixes gain tiers at 12/16/20 in data, base damage and armour scale from `ItemInstance.ItemLevel` (so saves and co-op need nothing new), and six affixes fill the stats nothing rolled. Legendary effects reuse the skill pipeline: `LegendaryProfile` gains `Trigger`/`Upgrades`, an engine-free `LegendaryRules` reads them off a member's `Equipment` through the catalog, and `PartyController.Trigger` sums them with learned skills. Effects whose firing site belongs to another pillar (parry, stagger, Shatter, Lucky Dip, perfect blows, sneak) are exposed as readable `PartyController` hooks in a new partial file `PartyController.Items.cs`; effects whose site exists today (kills, blocks, hits, damage taken) are implemented here. Skill trees gain `AnyOf` prerequisites for duo nodes, tier-6 keystones in data, a Wild Hunt branch, a flatter XP curve. The shop gains comparison tooltips, upgrade arrows, class glyphs, stack pricing and a buyback tab.

**Tech Stack:** Godot 4.7.2 .NET / C#, xUnit (tests/, dev machine only), headless `--test-items-and-builds` flow in `src/Core/DebugTools.cs`, `.tres` data under `data/`.

**Spec:** `docs/superpowers/specs/2026-11-07-the-depths-update-design.md` section 4 ("Builds and loot that keep going"); section 1 for `Thorns` at Bulwark tier 2 and the Parry node; the shared contract `scratchpad/plan-contract.md` (rules 4 to 9).

## Global Constraints

- Engine-free rules live in `src/Items/Core/`, `src/Characters/` and `src/Characters/Skills/` (all already linked into `tests/DungeonCrawler.Tests.csproj` lines 15-16, 21), never reference `Godot`, and are tested with xUnit before any Godot glue. Godot-side definitions (`ItemDef`, `AffixDef`, `LegendaryDef`, `SkillDef`) only convert; tests read the shipped `.tres` through `tests/Items/DataFiles.cs`.
- Numbers, verbatim from the spec: affix tiers at item levels **12, 16, 20**; base weapon damage and armour **+4% per item level above the base's own level**; smart loot **60%**; six new affixes *Cruel* (CritMultiplier), *of Reverie* (ManaRegen), *of Breath* (StaminaRegen), *of Dimming* (ResistArcane), *of the Owl* (Perception), *of Alacrity* (cooldown **×0.9 / 0.85 / 0.8**, suffix, rare weight); XP **25L² + 50L, cap 30**; keystones at **tier 6**; duo nodes at **tier 3**; capstone heals **15**; `Thorns 4` at Warrior Bulwark tier 2; buyback of the **last three** sales at sell price; Lucky Dip **30%** cheaper with Pellet's Receipt; Gerald fires below **30% HP once per floor**; Sister's Thimble parry window **+0.1 s**; Old Ulf's Chin stagger immunity **3 s**, **+20 HP**; Toll Road Boots **+12% move**; Alderman's Seal **+25% gold, sells +20%**; Lantern-Jaw heals **8**; Deacon's Patience shields **15**; Gorm's Molar stuns **1 s**.
- Twelve legendaries, twelve keystones, four duo nodes: every one must be reachable in play and proven by a test (anti-goal: dead nodes).
- Existing saves keep level, XP, learned skills and items: skill **ids and file names stay** (`skill.ranger.beastcraft.*` keeps its id under the branch name "Wild Hunt"); no `SaveFormat` bump from this plan. Save v11 is run-structure's (if it has not landed when a DTO field here needs it, it does not: every field added here has a default that needs no migration).
- Co-op: the server rolls loot and runs every trigger; buyback travels in `ShopSync` and a new `ShopBuybackIntent` (Protocol 6 → 7, see Task 10).
- Build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before any `godot --headless` run; the flow uses `--no-ambush --no-portals --no-gathering --lethality=1 --seed=7`.
- Collision discipline: this plan owns `src/Items/**`, `data/items`, `data/affixes`, `data/legendaries`, `data/skills`, `data/classes`, `src/Characters/Skills/**`, `src/Characters/Leveling.cs`, `src/UI/Inventory/**`, `src/UI/Shop/ShopScreen.cs`, `src/UI/Skills/SkillTreeScreen.cs`. Every member it adds to a file another plan owns is listed in **Shared-file members** at the end; nothing else in those files changes. `Adventurer.cs` is not touched.

## Review Focus

Spec-implied cases no task's main tests cover; each has its pin named in its owning task.

1. A level-1 Common longsword dropped on floor 16 must hit harder than the same sword on floor 1 and must still hit that hard after a save and a load (scaling is derived, never stored). Pin: Task 1 `BaseScale_SurvivesARebuildFromDefIdAndLevel`.
2. A Keen (CritChance) affix at tier 1 currently rounds its 0.02–0.04 roll to one decimal and adds nothing; the new decimals rule must give every rolled affix a visible, non-zero line. Pin: Task 1 `RollAffix_NeverProducesAnEmptyStatLine`.
3. A party whose only living member is the Mage must still receive a wearable drop six times in ten, and a party where nobody can wear anything (all dead) must still get drops. Pin: Task 3 `SmartLoot_FallsBackToTheWholePoolWhenNobodyCanUseAnything`.
4. A legendary a member equips, then unequips, must stop firing: triggers come from `Equipment` now, not from a cached sum. Pin: Task 4 `LegendaryRules_Trigger_FollowsTheEquipmentNotAHistory`.
5. A duo node learned through branch A must stay learned (and keep its effect) after the player respecs and takes branch B instead; a respec must refund its cost like any node. Pin: Task 7 `DuoNode_RespecRefundsItAndItIsLearnableAgainThroughAnotherBranch`.

---

### Task 1: Base scaling by item level, and honest affix rounding

**Files:**
- Create: `src/Items/Core/ItemRules.cs`
- Modify: `src/Items/ItemInstance.cs:18-23,50-61,63-70,77-89`, `src/Items/Core/LootRoller.cs:113-132`, `src/Items/Core/ItemText.cs:19`
- Test: `tests/Items/ItemRulesTests.cs` (new), `tests/Items/LootRollerTests.cs` (add)

**Interfaces:**
- Consumes: `ItemBase.ItemLevelMin`, `WeaponProfile` (record `with`), `StatModifier`, `StatInfo.IsPrimary`.
- Produces: `public static class ItemRules { public const float BaseScalePerLevel = 0.04f; public const float TopTierChance = 0.6f; public const float OnHitTierStep = 0.25f; public const float SmartLootChance = 0.6f; public static float BaseScale(int itemLevel, int baseLevel); public static int Decimals(Stat s); public static float RoundStat(Stat s, float raw); }` — `BaseScale = 1 + BaseScalePerLevel × max(0, itemLevel − baseLevel)`; `Decimals`: 0 for every stat `LootRoller.StatIsWhole` names today (move that list here as `IsWhole(Stat)`), 2 for `CritChance` and `CritMultiplier`, else 1.
  `ItemInstance` gains `public int BaseLevel { get; init; } = 1;` (set from `b.ItemLevelMin` in `FromBase`, copied by `Clone`), `public float BaseScale => ItemRules.BaseScale(ItemLevel, BaseLevel);`, `public IEnumerable<StatModifier> ScaledBaseModifiers` (every `Stat.Armor` `Add` modifier in `BaseModifiers` × `BaseScale`, rounded to a whole number, other modifiers unchanged). `Weapon` returns `BaseWeapon with { MinDamage = ×BaseScale, MaxDamage = ×BaseScale }` (then the affix on-hits as today); `Modifiers` uses `ScaledBaseModifiers`. `LootRoller.RollAffix` rounds with `ItemRules.RoundStat`, picks the top tier with `ItemRules.TopTierChance`, and gives an on-hit affix `OnHit with { Magnitude = Magnitude × (1 + OnHitTierStep × (tierIndex − 1)) }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Items/ItemRulesTests.cs
public class ItemRulesTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();

    [Fact] public void BaseScale_IsFourPercentPerLevelAboveTheBase_NeverBelowOne()
    {
        Assert.Equal(1f, ItemRules.BaseScale(1, 1)); Assert.Equal(1f, ItemRules.BaseScale(3, 5));
        Assert.Equal(1.6f, ItemRules.BaseScale(16, 1), 3); Assert.Equal(1.44f, ItemRules.BaseScale(16, 5), 3);
    }
    [Fact] public void AWeaponAndAnArmour_ScaleWithTheirItemLevel()
    {
        var sword = LootRoller.Generate(_cat, _cat.Base("item.longsword"), Rarity.Common, 16, new Random(1)); // base 6-10 at level 1
        Assert.Equal(9.6f, sword.Weapon!.MinDamage, 2); Assert.Equal(16f, sword.Weapon.MaxDamage, 2);
        var plate = LootRoller.Generate(_cat, _cat.Base("item.plate"), Rarity.Common, 15, new Random(1));    // Armor+20 at level 3 -> x1.48 = 29.6 -> 30
        Assert.Equal(30f, plate.Modifiers.Single(m => m.Stat == Stat.Armor).Value);
        Assert.Equal(20f, plate.BaseModifiers.Single(m => m.Stat == Stat.Armor).Value); // the raw base is untouched
    }
    [Fact] public void BaseScale_SurvivesARebuildFromDefIdAndLevel()
    {   // what SaveMapper.FromDto does: FromBase, then ItemLevel from the dto
        var rebuilt = ItemInstance.FromBase(_cat.Base("item.longsword")); rebuilt.ItemLevel = 16;
        Assert.Equal(16f, rebuilt.Weapon!.MaxDamage, 2); Assert.Equal(1, rebuilt.BaseLevel);
        Assert.Equal(16f, rebuilt.Clone().Weapon!.MaxDamage, 2);
    }
    [Fact] public void Decimals_WholeForWholeStats_TwoForCrit_OneOtherwise()
    {
        Assert.Equal(0, ItemRules.Decimals(Stat.Armor)); Assert.Equal(0, ItemRules.Decimals(Stat.Strength));
        Assert.Equal(2, ItemRules.Decimals(Stat.CritChance)); Assert.Equal(2, ItemRules.Decimals(Stat.CritMultiplier));
        Assert.Equal(1, ItemRules.Decimals(Stat.HpRegen)); Assert.Equal(1, ItemRules.Decimals(Stat.StaminaRegen));
    }
}
// tests/Items/LootRollerTests.cs (add)
[Fact] public void RollAffix_NeverProducesAnEmptyStatLine()
{
    var keen = new AffixProfile { Id = "affix.keen", Name = "Keen", IsPrefix = true, Tags = new[] { "weapon" }, Stat = Stat.CritChance, Tiers = new[] { new AffixTier(2, 0.02f, 0.04f) } };
    var rng = new Random(4);
    for (var i = 0; i < 200; i++) { var r = LootRoller.RollAffix(keen, 3, rng); var m = Assert.Single(r.Modifiers); Assert.InRange(m.Value, 0.02f, 0.04f); }
}
[Fact] public void RollAffix_OnHitMagnitudeGrowsATierAtATime()
{
    var flaming = new AffixProfile { Id = "affix.flaming", Name = "Flaming", IsPrefix = true, Tags = new[] { "weapon" }, Stat = Stat.MeleeDamage,
        Tiers = new[] { new AffixTier(3, 0, 0), new AffixTier(12, 0, 0), new AffixTier(16, 0, 0), new AffixTier(20, 0, 0) }, OnHit = new StatusApplication(StatusKind.Burn, 4, 2, 0.3f) };
    var rng = new Random(5);
    var low = Enumerable.Range(0, 50).Select(_ => LootRoller.RollAffix(flaming, 3, rng)).ToList();
    Assert.All(low, r => { Assert.Equal(1, r.Tier); Assert.Equal(2f, r.OnHit!.Value.Magnitude); Assert.Empty(r.Modifiers); });
    var top = Enumerable.Range(0, 200).Select(_ => LootRoller.RollAffix(flaming, 20, rng)).Where(r => r.Tier == 4).ToList();
    Assert.NotEmpty(top); Assert.All(top, r => Assert.Equal(3.5f, r.OnHit!.Value.Magnitude, 3));
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "ItemRulesTests|LootRollerTests"` → build errors on `ItemRules`, `BaseLevel`.
- [ ] **Step 3: Implement** as the Interfaces block says. `ItemText.Describe` line 19 iterates `item.ScaledBaseModifiers`. Delete `LootRoller.StatIsWhole` (it moves to `ItemRules.IsWhole`).
- [ ] **Step 4: Run to see them pass** — same filter → PASS; then `cd tests && dotnet test --nologo -v q --filter "Items"` → all PASS (`WeaponProgressionTests` and `PricesTests` are untouched by scaling: prices read `ItemLevel`, not damage).
- [ ] **Step 5: Commit** — `git add src/Items/Core/ItemRules.cs src/Items/ItemInstance.cs src/Items/Core/LootRoller.cs src/Items/Core/ItemText.cs && git commit -m "feat(items): base damage and armour scale with item level; affixes round to a visible value"`

### Task 2: Affix tiers at 12/16/20, six new affixes, the Cooldown stat

**Files:**
- Modify: all 26 files in `data/affixes/` (`TierMinLevels`/`TierMins`/`TierMaxes` only), `src/Characters/Stat.cs:4-17`, `src/Characters/Stats.cs:35-71`, `src/Characters/Skills/SkillRules.cs:52-78`
- Create: `data/affixes/cruel.tres`, `of_reverie.tres`, `of_breath.tres`, `of_dimming.tres`, `of_the_owl.tres`, `of_alacrity.tres`
- Test: `tests/Items/AffixDataTests.cs` (new), `tests/Characters/StatsTests.cs` (add), `tests/Characters/SkillRulesTests.cs` (add)

**Interfaces:**
- Consumes: `DataFiles.Catalog()` (tests), `AffixProfile.Tiers`.
- Produces: `Stat.Cooldown` appended last in the enum (a multiplier stat: base `1f` in `Stats.Compute`, clamped to `[0.5, 2]` after modifiers; `StatInfo.IsPercent` false); `SkillRules.ApplyUpgrades` multiplies the returned `Cooldown` by `c.Stats[Stat.Cooldown]` whenever it is not 1, even when no upgrade matches (the early `return a` only when there is nothing to change). `StatModifierParser.Describe` already prints a Mul as "-10% Cooldown".

Data table (append three tiers; the first tiers stay as they are; Mul tiers are percent):

| Affix | New TierMinLevels | New TierMins | New TierMaxes |
|---|---|---|---|
| sharp, deadly | +12, 16, 20 | +9, 12, 16 | +12, 16, 20 |
| arcane | +12, 16, 20 | +10, 14, 18 | +14, 18, 23 |
| sturdy | +12, 16, 20 | +14, 19, 25 | +19, 25, 32 |
| vigorous | +12, 16, 20 | +40, 55, 72 | +55, 72, 90 |
| keen | +12, 16, 20 | +0.08, 0.11, 0.14 | +0.11, 0.14, 0.18 |
| swift | +12, 16, 20 | +18, 24, 30 | +24, 30, 36 |
| flaming, freezing, venomous | +12, 16, 20 | +0, 0, 0 | +0, 0, 0 (on-hit magnitude grows per tier, Task 1) |
| of_dexterity, of_strength, of_intellect, of_vitality, of_will, of_fortune | +12, 16, 20 | +5, 7, 9 | +7, 9, 12 |
| of_fire_warding, of_frost_warding, of_venom_warding, of_shadows | +12, 16, 20 | +30, 40, 50 | +40, 50, 60 |
| of_haste | +12, 16, 20 | +15, 20, 25 | +20, 25, 30 |
| of_precision | +12, 16, 20 | +15, 22, 30 | +22, 30, 38 |
| of_regeneration | +12, 16, 20 | +1.2, 1.8, 2.5 | +1.8, 2.5, 3.2 |
| of_stamina, of_the_mind | +12, 16, 20 | +30, 42, 55 | +42, 55, 70 |
| of_the_fox | +12, 16, 20 | +12, 17, 22 | +17, 22, 28 |

New affixes (copy `data/affixes/of_fortune.tres` as the template; `OnHit*` fields zero):

| File | Id / DisplayName | IsPrefix | Tags | Weight | Stat / Op | TierMinLevels | TierMins | TierMaxes |
|---|---|---|---|---|---|---|---|---|
| cruel.tres | affix.cruel / Cruel | true | weapon | 0.6 | CritMultiplier / Add | 2, 6, 12, 16, 20 | 0.10, 0.16, 0.26, 0.36, 0.46 | 0.15, 0.25, 0.35, 0.45, 0.55 |
| of_reverie.tres | affix.of_reverie / of Reverie | false | staff, focus, jewelry, armor | 0.7 | ManaRegen / Add | 2, 6, 12, 16, 20 | 0.3, 0.7, 1.1, 1.6, 2.1 | 0.6, 1.0, 1.5, 2.0, 2.6 |
| of_breath.tres | affix.of_breath / of Breath | false | armor, jewelry | 0.7 | StaminaRegen / Add | 2, 6, 12, 16, 20 | 1, 2.5, 4.5, 6.5, 8.5 | 2, 4, 6, 8, 10 |
| of_dimming.tres | affix.of_dimming / of Dimming | false | armor, jewelry | 0.8 | ResistArcane / Add | 2, 6, 12, 16, 20 | 8, 16, 31, 41, 51 | 15, 30, 40, 50, 60 |
| of_the_owl.tres | affix.of_the_owl / of the Owl | false | armor, jewelry | 0.7 | Perception / Add | 1, 5, 12, 16, 20 | 5, 11, 19, 27, 35 | 10, 18, 26, 34, 42 |
| of_alacrity.tres | affix.of_alacrity / of Alacrity | false | weapon, jewelry | 0.3 | Cooldown / Mul | 12, 16, 20 | -10, -15, -20 | -10, -15, -20 |

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Items/AffixDataTests.cs
public class AffixDataTests
{
    private readonly ItemCatalog _cat = DataFiles.Catalog();
    public static readonly int[] DeepTiers = { 12, 16, 20 };

    [Fact] public void EveryAffix_HasATierAtTwelveSixteenAndTwenty_AndValuesNeverFall()
    {
        Assert.Equal(32, _cat.Affixes.Count);
        foreach (var a in _cat.Affixes.Values)
        {
            foreach (var lvl in DeepTiers) Assert.Contains(a.Tiers, t => t.ItemLevelMin == lvl);
            for (var i = 1; i < a.Tiers.Length; i++) { Assert.True(a.Tiers[i].ItemLevelMin > a.Tiers[i - 1].ItemLevelMin, a.Id); Assert.True(Math.Abs(a.Tiers[i].Min) >= Math.Abs(a.Tiers[i - 1].Max) - 0.001f || a.OnHit != null, $"{a.Id} tier {i} overlaps the one below"); }
        }
    }
    [Fact] public void TheSixNewAffixes_RollTheirStats()
    {
        var want = new Dictionary<string, (Stat, bool prefix)> { ["affix.cruel"] = (Stat.CritMultiplier, true), ["affix.of_reverie"] = (Stat.ManaRegen, false), ["affix.of_breath"] = (Stat.StaminaRegen, false), ["affix.of_dimming"] = (Stat.ResistArcane, false), ["affix.of_the_owl"] = (Stat.Perception, false), ["affix.of_alacrity"] = (Stat.Cooldown, false) };
        foreach (var (id, (stat, prefix)) in want) { var a = _cat.Affixes[id]; Assert.Equal(stat, a.Stat); Assert.Equal(prefix, a.IsPrefix); }
        var alacrity = _cat.Affixes["affix.of_alacrity"];
        Assert.Equal(ModOp.Mul, alacrity.Op); Assert.Equal(new[] { 12, 16, 20 }, alacrity.Tiers.Select(t => t.ItemLevelMin)); Assert.True(alacrity.Weight <= 0.3f);
        for (var i = 0; i < 30; i++) { var r = LootRoller.RollAffix(alacrity, 20, new Random(i)); Assert.InRange(r.Tier, 1, 3); Assert.Equal(1f - 0.05f * (r.Tier + 1), r.Modifiers.Single().Value, 3); } // tiers 1..3 are exactly x0.9, x0.85, x0.8
        Assert.False(alacrity.AvailableAt(11)); Assert.True(alacrity.AvailableAt(12));
    }
    [Fact] public void AFloorSixteenRoll_BeatsAFloorEightRoll_OnAverage()
    {
        var table = _cat.Table("table.chest") with { Rolls = 1 };
        double Avg(int level) { var rng = new Random(16); var s = 0.0; var n = 0; for (var i = 0; i < 1500; i++) { var it = LootRoller.Roll(_cat, table, level, rng); if (it is { IsEquippable: true }) { s += ItemText.Score(it); n++; } } return s / n; }
        Assert.True(Avg(16) > Avg(8) * 1.15, $"{Avg(16):0.0} vs {Avg(8):0.0}");
        Assert.True(Avg(20) > Avg(16) * 1.05);
    }
}
// tests/Characters/StatsTests.cs (add)
[Fact] public void Cooldown_IsAMultiplierStat_BaseOne_ClampedToHalf()
{
    var s = Stats.Compute(TestClasses.Mage, 1, null, null); Assert.Equal(1f, s[Stat.Cooldown]);
    var fast = Stats.Compute(TestClasses.Mage, 1, null, new[] { new StatModifier(Stat.Cooldown, ModOp.Mul, 0.8f), new StatModifier(Stat.Cooldown, ModOp.Mul, 0.5f) });
    Assert.Equal(0.5f, fast[Stat.Cooldown]);
}
// tests/Characters/SkillRulesTests.cs (add)
[Fact] public void ApplyUpgrades_ScalesCooldownByTheCooldownStat_EvenWithoutAnUpgrade()
{
    var m = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
    var ring = ItemInstance.Simple("item.ring", "Ring", SlotKind.Ring);
    ring.Affixes.Add(new RolledAffix { AffixId = "affix.of_alacrity", Name = "of Alacrity", Tier = 1, Modifiers = { new StatModifier(Stat.Cooldown, ModOp.Mul, 0.8f) } });
    m.Equipment.Equip(EquipSlot.Ring1, ring); m.Recalculate();
    Assert.Equal(4.8f, SkillRules.ApplyUpgrades(ShieldBash, m, Catalog()).Cooldown, 3); // 6 s x 0.8, with no upgrade learned
}
```

(`ItemText.Score` is written in Task 11; until then the third test does not compile: write it now, leave it `[Fact(Skip = "Task 11")]`, and un-skip it in Task 11's Step 4.)

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "AffixDataTests|StatsTests|SkillRulesTests"` → `Stat.Cooldown` missing; 26 affixes, no deep tiers.
- [ ] **Step 3: Implement** — edit the 32 data files per the tables; `Stat.cs` appends `Cooldown` with a doc comment ("Multiplies ability cooldowns; 1 is normal, of Alacrity lowers it"); `Stats.Compute` sets `v[Stat.Cooldown] = 1f` with the other bases and clamps `Math.Clamp(v[Stat.Cooldown], 0.5f, 2f)` beside the resist clamp; `SkillRules.ApplyUpgrades` as in Interfaces.
- [ ] **Step 4: Run to see them pass** — same filter → PASS (the skipped one excepted); `cd tests && dotnet test --nologo -v q` → all PASS (`WeaponProgressionTests.Affixes_FitTheWeaponKind` now sees Cruel and of Alacrity on weapons; adjust its expected pairs if it pins an exact set, and say so in the commit).
- [ ] **Step 5: Commit** — `git add data/affixes src/Characters/Stat.cs src/Characters/Stats.cs src/Characters/Skills/SkillRules.cs && git commit -m "feat(items): affix tiers at item levels 12, 16 and 20, six new affixes, and a Cooldown stat"`

### Task 3: Smart loot

**Files:**
- Create: `src/Items/Core/SmartLoot.cs`
- Modify: `src/Items/Core/ItemBase.cs:122-133` (`ItemCatalog`), `src/Items/Core/LootRoller.cs:36-60` (`Roll`)
- Test: `tests/Items/SmartLootTests.cs` (new)

**Interfaces:**
- Consumes: `ClassProfile.CanWield/AllowedArmor/AllowedOffhands`, `ItemRules.SmartLootChance`.
- Produces: `public static class SmartLoot { public static bool Usable(ItemBase b, ClassProfile cls); public static bool UsableByAny(ItemBase b, IEnumerable<ClassProfile> classes); }` — a weapon needs `cls.CanWield(b.Weapon.Kind)`, an offhand needs `AllowedOffhands.Contains(b.Offhand)`, armour with an `ArmorClass` needs `AllowedArmor.Contains`, rings and amulets are usable by everyone, non-equippables return true. `ItemCatalog` gains `public Func<IEnumerable<ClassProfile>>? SmartLootClasses { get; set; }` (null: no smart loot, as in tests). `LootRoller.Roll`: after `candidates` is built (line 42) and before the pick, `if (cat.SmartLootClasses?.Invoke().ToList() is { Count: > 0 } living && rng.NextDouble() < ItemRules.SmartLootChance) { var fit = candidates.Where(b => SmartLoot.UsableByAny(b, living)).ToList(); if (fit.Count > 0) candidates = fit; }` — one `rng` draw, always taken when a provider is set so the sequence stays deterministic for a seed.

- [ ] **Step 1: Write the failing tests**

```csharp
public class SmartLootTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();
    private static LootTableProfile Gear() => new() { Id = "t", Entries = new[] { new LootEntry("tag:weapon", 1), new LootEntry("tag:armor", 1), new LootEntry("tag:offhand", 1) }, RarityWeights = new float[] { 1, 0, 0, 0 } };

    [Fact] public void Usable_FollowsClassRules()
    {
        Assert.True(SmartLoot.Usable(_cat.Base("item.longsword"), TestClasses.Warrior)); Assert.False(SmartLoot.Usable(_cat.Base("item.longsword"), TestClasses.Mage));
        Assert.False(SmartLoot.Usable(_cat.Base("item.plate"), TestClasses.Rogue)); Assert.True(SmartLoot.Usable(_cat.Base("item.shield"), TestClasses.Warrior)); Assert.False(SmartLoot.Usable(_cat.Base("item.shield"), TestClasses.Mage));
        Assert.True(SmartLoot.Usable(_cat.Base("item.ring"), TestClasses.Mage)); Assert.True(SmartLoot.Usable(_cat.Base("item.potion"), TestClasses.Mage));
    }
    [Fact] public void SixInTen_OfRolledBases_FitALivingMember()
    {   // tag:armor holds plate, robe and shield; the mage can wear only the robe: unaided 1/3, aided 0.6 + 0.4 x 1/3 = 0.733
        var armour = new LootTableProfile { Id = "a", Entries = new[] { new LootEntry("tag:armor", 1) }, RarityWeights = new float[] { 1, 0, 0, 0 } };
        double Rate(Func<IEnumerable<ClassProfile>>? provider) { _cat.SmartLootClasses = provider; var rng = new Random(3); var fit = 0; const int n = 3000; for (var i = 0; i < n; i++) if (LootRoller.Roll(_cat, armour, 5, rng) is { } it && SmartLoot.Usable(_cat.Base(it.DefId), TestClasses.Mage)) fit++; return fit / (double)n; }
        Assert.InRange(Rate(null), 0.28, 0.39);
        Assert.InRange(Rate(() => new[] { TestClasses.Mage }), 0.69, 0.78);
    }
    [Fact] public void SmartLoot_FallsBackToTheWholePoolWhenNobodyCanUseAnything()
    {
        _cat.SmartLootClasses = () => Array.Empty<ClassProfile>();
        var rng = new Random(3);
        Assert.All(Enumerable.Range(0, 200).Select(_ => LootRoller.Roll(_cat, Gear(), 5, rng)), it => Assert.NotNull(it));
        _cat.SmartLootClasses = () => new[] { TestClasses.Mage };
        var onlyShields = new LootTableProfile { Id = "s", Entries = new[] { new LootEntry("id:item.shield", 1) }, RarityWeights = new float[] { 1, 0, 0, 0 } };
        Assert.All(Enumerable.Range(0, 50).Select(_ => LootRoller.Roll(_cat, onlyShields, 5, rng)), it => Assert.Equal("item.shield", it!.DefId)); // nothing fits: the pool is used as it is
    }
    [Fact] public void Roll_StaysDeterministic_WithAProvider()
    {
        _cat.SmartLootClasses = () => new[] { TestClasses.Warrior };
        var a = Enumerable.Range(0, 40).Select(_ => LootRoller.Roll(_cat, Gear(), 6, new Random(5))?.DisplayName).ToList();
        var b = Enumerable.Range(0, 40).Select(_ => LootRoller.Roll(_cat, Gear(), 6, new Random(5))?.DisplayName).ToList();
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter SmartLootTests` → build errors.
- [ ] **Step 3: Implement** per Interfaces. The Godot side sets the provider in Task 5 (`HookItemTriggers`).
- [ ] **Step 4: Run to see them pass** — same filter → PASS; `--filter Items` → PASS (`Roll_IsDeterministicForSeed` unchanged: TestCatalog has no provider).
- [ ] **Step 5: Commit** — `git add src/Items/Core/SmartLoot.cs src/Items/Core/ItemBase.cs src/Items/Core/LootRoller.cs && git commit -m "feat(items): six rolled bases in ten fit a living member"`

### Task 4: Legendary effect infrastructure: triggers and upgrades from gear

**Files:**
- Modify: `src/Characters/Skills/SkillProfile.cs:6-25` (TriggerKind), `:28-38` (AbilityUpgrade), `:41-56` (SkillProfile), `src/Characters/Skills/SkillRules.cs:52-81`, `src/Items/Core/ItemBase.cs:76-88` (LegendaryProfile), `src/Items/Defs/LegendaryDef.cs:10-33`, `src/Characters/Defs/SkillDef.cs:37-57`
- Create: `src/Characters/Skills/UpgradeParser.cs`, `src/Items/Core/LegendaryRules.cs`
- Test: `tests/Characters/UpgradeParserTests.cs`, `tests/Items/LegendaryRulesTests.cs`, `tests/Characters/SkillRulesTests.cs` (add)

**Interfaces:**
- Consumes: `Equipment.Items`, `ItemCatalog.Legendaries`, `RolledAffix.AffixId` (a legendary's fixed affix carries `AffixId = l.Id`, `Tier = 0`: `LootRoller.MakeLegendary` line 89).
- Produces:
  - `TriggerKind` appended after `Momentum`, in this order: `HeavyStun` (heavy blows stun Magnitude s; Shatter stuns the same), `GoldFind` (+Magnitude fraction of coins picked up), `SellBonus` (+Magnitude fraction on sales), `LastGasp` (below 30% HP, once per floor: Frost Nova), `LuckyDip` (Lucky Dip costs ×(1−Magnitude)), `PerfectCleave` (a perfect blow also strikes every other enemy in the arc for Magnitude × weapon damage), `FrostSneak` (sneak attacks on a Frosted target ×(1+Magnitude)), `QuietKill` (kills never wake kin), `ParryWindow` (+Magnitude s), `IronChin` (stagger immunity Magnitude s after getting up), `BurnSpread` (Burn you apply spreads to Magnitude neighbours within 3 m), `NoGuard` (cannot guard), `ArrowShatter` (arrows Shatter a Frosted target on any hit).
  - `AbilityUpgrade` gains `public float OnHitDurationMul { get; init; } = 1f;` (scales `Attack.OnHit` durations), `public StatusApplication? AddEffect { get; init; }` (appended to `Effects`), and `AbilityId` may be a comma list: `public bool Targets(string abilityId)` splits on ',' and trims.
  - `SkillProfile` gains `public string[] AnyOf { get; init; } = Array.Empty<string>();` (Task 7 uses it), `public TriggerKind Trigger2 { get; init; } = TriggerKind.None; public float TriggerMagnitude2 { get; init; }`, `public AbilityUpgrade[] Upgrades { get; init; } = Array.Empty<AbilityUpgrade>();`, `public IEnumerable<AbilityUpgrade> AllUpgrades => Upgrade is null ? Upgrades : Upgrades.Prepend(Upgrade);`, `public bool IsDuo => AnyOf.Length > 0;`, `public bool IsKeystone => Tier == 6;`.
  - `public static class UpgradeParser { public static AbilityUpgrade Parse(string text); public static AbilityUpgrade[] ParseAll(IEnumerable<string> lines); }` — syntax `"<abilityId>[,<abilityId>...]: Key=Value Key=Value"`, keys `DamageMul CooldownMul CostMul BuffMul RadiusAdd ExtraProjectiles OnHitDurationMul AddStatus AddEffect`; `AddStatus=Kind,Duration,Magnitude[,Chance]`, `AddEffect=Kind,Duration,Magnitude`; invariant culture; `FormatException` on an unknown key or a missing colon.
  - `LegendaryProfile` gains `TriggerKind Trigger`, `float TriggerMagnitude`, `TriggerKind Trigger2`, `float TriggerMagnitude2`, `AbilityUpgrade[] Upgrades`, and `IEnumerable<(TriggerKind Kind, float Magnitude)> Triggers` (the non-None of the two). `LegendaryDef` exports `string Trigger = "None"`, `float TriggerMagnitude`, `string Trigger2 = "None"`, `float TriggerMagnitude2`, `string[] Upgrades` (parsed with `UpgradeParser.ParseAll`). `SkillDef` exports `string[] AnyOf`, `string Trigger2 = "None"`, `float TriggerMagnitude2`, `string[] MoreUpgrades` (→ `Upgrades`).
  - `public static class LegendaryRules { public static LegendaryProfile? Of(ItemInstance item, ItemCatalog cat); public static float Trigger(Equipment eq, ItemCatalog cat, TriggerKind kind); public static IEnumerable<AbilityUpgrade> Upgrades(Equipment eq, ItemCatalog cat); public static float GoldFound(int coins, float bonus) => (int)Math.Round(coins * (1 + bonus)); }` — `Of` finds the `Tier == 0` affix whose `AffixId` is a key of `cat.Legendaries` (null for non-legendaries); `Trigger` sums `Triggers` of every equipped legendary with that kind.
  - `SkillRules.TriggerMagnitude(c, cat, kind)` sums `Trigger` and `Trigger2`; `SkillRules.ApplyUpgrades(AbilityProfile a, CharacterState c, SkillCatalog cat, IEnumerable<AbilityUpgrade>? extra = null)` uses `s.AllUpgrades` from learned skills plus `extra`, matches with `u.Targets(a.Id)`, applies `OnHitDurationMul` to every `Attack.OnHit` duration and appends `AddEffect` to `Effects`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Characters/UpgradeParserTests.cs
public class UpgradeParserTests
{
    [Fact] public void Parse_ReadsEveryKey()
    {
        var u = UpgradeParser.Parse("ability.mend: BuffMul=2 CooldownMul=0.8 AddEffect=Shield,10,15");
        Assert.True(u.Targets("ability.mend")); Assert.Equal(2f, u.BuffMul); Assert.Equal(0.8f, u.CooldownMul);
        Assert.Equal(new StatusApplication(StatusKind.Shield, 10, 15), u.AddEffect);
        var v = UpgradeParser.Parse("ability.frostbolt,ability.frost_nova: OnHitDurationMul=2 AddStatus=Stun,1,0,0.5");
        Assert.True(v.Targets("ability.frost_nova")); Assert.False(v.Targets("ability.fireball")); Assert.Equal(2f, v.OnHitDurationMul); Assert.Equal(0.5f, v.AddStatus!.Value.Chance);
    }
    [Fact] public void Parse_RejectsGarbage() { Assert.Throws<FormatException>(() => UpgradeParser.Parse("ability.mend BuffMul=2")); Assert.Throws<FormatException>(() => UpgradeParser.Parse("ability.mend: Sparkle=2")); }
}
// tests/Characters/SkillRulesTests.cs (add)
[Fact] public void ApplyUpgrades_UsesExtraUpgrades_OnHitDuration_AndAddEffect()
{
    var m = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
    var mend = new AbilityProfile { Id = "ability.mend", Name = "Mend", Kind = AbilityKind.PartyBuff, Cooldown = 15, Effects = new[] { new StatusApplication(StatusKind.Regen, 8, 3) } };
    var up = SkillRules.ApplyUpgrades(mend, m, Catalog(), new[] { UpgradeParser.Parse("ability.mend: AddEffect=Shield,10,15") });
    Assert.Equal(2, up.Effects.Length); Assert.Contains(up.Effects, e => e.Kind == StatusKind.Shield && e.Magnitude == 15);
    var bolt = new AbilityProfile { Id = "ability.frostbolt", Attack = new AttackProfile { MinDamage = 6, MaxDamage = 10, OnHit = new[] { new StatusApplication(StatusKind.Frost, 4, 0) } } };
    var cold = SkillRules.ApplyUpgrades(bolt, m, Catalog(), new[] { UpgradeParser.Parse("ability.frostbolt,ability.frost_nova: OnHitDurationMul=2") });
    Assert.Equal(8f, cold.Attack!.OnHit.Single().Duration);
}
[Fact] public void TriggerMagnitude_SumsTheSecondTrigger()
{
    var cat = Catalog(); cat.Skills["w.thorny"] = new SkillProfile { Id = "w.thorny", ClassId = "class.warrior", Branch = "Bulwark", Tier = 0, Trigger = TriggerKind.SecondWind, TriggerMagnitude = 10, Trigger2 = TriggerKind.Thorns, TriggerMagnitude2 = 4 };
    var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); w.SkillPoints = 1; SkillRules.Learn(w, cat.Skills["w.thorny"], cat);
    Assert.Equal(4f, SkillRules.TriggerMagnitude(w, cat, TriggerKind.Thorns)); Assert.Equal(10f, SkillRules.TriggerMagnitude(w, cat, TriggerKind.SecondWind));
}
// tests/Items/LegendaryRulesTests.cs
public class LegendaryRulesTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();
    private ItemInstance Lantern()
    {
        _cat.Legendaries["leg.lantern"] = new LegendaryProfile { Id = "leg.lantern", Name = "Lantern-Jaw", BaseId = "item.longsword", Trigger = TriggerKind.Bloodlust, TriggerMagnitude = 8, Trigger2 = TriggerKind.GoldFind, TriggerMagnitude2 = 0.25f, Upgrades = new[] { UpgradeParser.Parse("ability.cleave: DamageMul=1.2") } };
        return LootRoller.MakeLegendary(_cat, _cat.Legendaries["leg.lantern"], 8, new Random(1));
    }
    [Fact] public void Of_FindsTheLegendaryByItsFixedAffix_AndNothingForAnOrdinaryItem()
    {
        Assert.Equal("Lantern-Jaw", LegendaryRules.Of(Lantern(), _cat)!.Name);
        Assert.Null(LegendaryRules.Of(LootRoller.Generate(_cat, _cat.Base("item.longsword"), Rarity.Rare, 8, new Random(2)), _cat));
    }
    [Fact] public void LegendaryRules_Trigger_FollowsTheEquipmentNotAHistory()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        Assert.Equal(0f, LegendaryRules.Trigger(w.Equipment, _cat, TriggerKind.Bloodlust));
        w.Equipment.Equip(EquipSlot.Weapon, Lantern());
        Assert.Equal(8f, LegendaryRules.Trigger(w.Equipment, _cat, TriggerKind.Bloodlust)); Assert.Equal(0.25f, LegendaryRules.Trigger(w.Equipment, _cat, TriggerKind.GoldFind));
        Assert.Single(LegendaryRules.Upgrades(w.Equipment, _cat));
        w.Equipment.Unequip(EquipSlot.Weapon);
        Assert.Equal(0f, LegendaryRules.Trigger(w.Equipment, _cat, TriggerKind.Bloodlust)); Assert.Empty(LegendaryRules.Upgrades(w.Equipment, _cat));
    }
    [Fact] public void Trigger_SurvivesASaveRoundTrip()
    {
        var dto = SaveMapper.ToDto(Lantern()); var back = SaveMapper.FromDto(dto, _cat);
        Assert.Equal("Lantern-Jaw", LegendaryRules.Of(back, _cat)!.Name);
    }
    [Fact] public void GoldFound_RoundsTheBonus() { Assert.Equal(25, (int)LegendaryRules.GoldFound(20, 0.25f)); Assert.Equal(13, (int)LegendaryRules.GoldFound(10, 0.25f)); Assert.Equal(7, (int)LegendaryRules.GoldFound(7, 0f)); }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "UpgradeParserTests|LegendaryRulesTests|SkillRulesTests"` → build errors.
- [ ] **Step 3: Implement** per Interfaces; `tests/Items/DataFiles.cs` `ToLegendary` gains the trigger fields and `UpgradeParser.ParseAll(A(d, "Upgrades"))`, and a new `ToSkill(Dictionary)` + `Skills()` loader mirroring `SkillDef.ToProfile` (used from Task 7).
- [ ] **Step 4: Run to see them pass** — same filter → PASS; whole suite PASS.
- [ ] **Step 5: Commit** — `git add src/Characters/Skills src/Characters/Defs/SkillDef.cs src/Items/Core/ItemBase.cs src/Items/Core/LegendaryRules.cs src/Items/Defs/LegendaryDef.cs && git commit -m "feat(items): legendaries carry triggers and ability upgrades; the skill pipeline reads them"`

### Task 5: The twelve legendaries and the party's item hooks

**Files:**
- Create: `data/legendaries/gorms_molar.tres`, `aldermans_seal.tres`, `gerald.tres`, `pellets_receipt.tres`, `lantern_jaw.tres`, `levy_steel.tres`, `deacons_patience.tres`, `hush.tres`, `sisters_thimble.tres`, `old_ulfs_chin.tres`, `toll_road_boots.tres`, `candle_of_the_congregation.tres`; `src/Party/PartyController.Items.cs`
- Modify: `src/Party/PartyController.cs:97,99,101-109,519-520` (four lines, listed in Shared-file members), `src/Items/LootDrop.cs:81-88`
- Test: `tests/Items/LegendaryDataTests.cs` (new)

**Interfaces:**
- Consumes: `LegendaryRules`, `EventBus.MemberDamaged/MemberRevived/FloorChanged` (`src/Core/EventBus.cs:49,68,16`), `Enemy` (`FloorRoot` children; `Effects` is its status controller), `GetAbilityFor(m, "ability.frost_nova")`, `LogResult`, `OnHitLanded`.
- Produces (all in `PartyController.Items.cs`, `public partial class PartyController`):
  - `private void HookItemTriggers()` — called once from `_Ready`: sets `Game.Instance.Catalog.SmartLootClasses = () => Members.Where(m => m.IsAlive).Select(m => m.Class)`, subscribes `EventBus.MemberDamaged += OnItemMemberDamaged`, `MemberRevived += i => _gotUpAt[i] = Time.GetTicksMsec()`, `FloorChanged += (_, _) => _lastGaspUsed.Clear()`; unsubscribes in `_ExitTree` is not possible from a partial, so the handlers early-return when `!IsInsideTree()`.
  - `private void OnItemHit(CharacterState m, ICombatTarget target, DamageResult r)` — called from `OnHitLanded` right after `RaiseHitLanded`: if `Trigger(m, TriggerKind.BurnSpread) is > 0 and var n && r.Applied.Any(s => s.Kind == StatusKind.Burn) && target is Enemy e`, apply that Burn (`Applied` entry) to the `(int)n` nearest other living `Enemy` children of `Main.Instance.Builder.FloorRoot` within 3 m of `e`, and log `"{m.Name}'s flame leaps to {other.DisplayName}."`.
  - `private void OnItemMemberDamaged(int index, float amount)` — server/solo only (`Net.NetSession.IsClient` returns); if `Trigger(m, TriggerKind.LastGasp) > 0 && m.IsAlive && m.Hp < 0.3f * m.Stats[Stat.MaxHp] && _lastGaspUsed.Add(index)`: run the Frost Nova loop of `Adventurer.Nova` (enemies within `a.Radius` of `Adventurers[index].GlobalPosition`, `ReceiveAttack`, `LogResult`, `OnHitLanded`) with `a = GetAbilityFor(m, "ability.frost_nova")`, log `"Gerald remembers being bigger: frost bursts from {m.Name}."`.
  - Readable hooks (other plans read these; each is one line over `Trigger`): `public bool CanGuard(CharacterState m) => m.Class.CanBlock && Trigger(m, TriggerKind.NoGuard) <= 0;` `public float ParryWindowBonus(CharacterState m) => Trigger(m, TriggerKind.ParryWindow);` `public bool StaggerImmune(CharacterState m)` (true within `Trigger(m, IronChin)` seconds of `_gotUpAt[index]`); `public float HeavyStunSeconds(CharacterState m) => Trigger(m, TriggerKind.HeavyStun);` `public float ShatterStunSeconds(CharacterState m) => HeavyStunSeconds(m);` `public float PerfectBlowCleave(CharacterState m) => Trigger(m, TriggerKind.PerfectCleave);` `public float SneakBonus(CharacterState m, ICombatTarget target)` (1 + `Trigger(m, FrostSneak)` when `target is Enemy { } e && e.Effects.Has(StatusKind.Frost)`, else 1); `public bool QuietKills(CharacterState m) => Trigger(m, TriggerKind.QuietKill) > 0;` `public bool ArrowsShatter(CharacterState m) => Trigger(m, TriggerKind.ArrowShatter) > 0;` `public float LuckyDipDiscount() => Members.Where(m => m.IsAlive).Select(m => Trigger(m, TriggerKind.LuckyDip)).DefaultIfEmpty(0).Max();` `public float GoldFindBonus() => Members.Where(m => m.IsAlive).Select(m => Trigger(m, TriggerKind.GoldFind)).DefaultIfEmpty(0).Max();` `public float SellBonus()` (same over `SellBonus`).
  - `LootDrop.Interact` (line 85): `party.AddGold((int)LegendaryRules.GoldFound(Coins, party.GoldFindBonus()));`.

Data (copy `data/legendaries/ember_fang.tres`; `ShopOnly = false`, `Weight = 1.0`, `OnHit*` zero unless given):

| File / Id | DisplayName | BaseId | ItemLevelMin | Modifiers | Trigger / Magnitude | Trigger2 / Magnitude2 | Upgrades | Flavor |
|---|---|---|---|---|---|---|---|---|
| gorms_molar / leg.gorms_molar | Gorm's Molar | item.mace | 6 | MeleeDamage+5, MaxHp+15 | HeavyStun / 1 | | | Pulled from a jaw that had already stopped arguing. |
| aldermans_seal / leg.aldermans_seal | The Alderman's Seal | item.gold_ring | 5 | Luck+2 | GoldFind / 0.25 | SellBonus / 0.2 | | The wax is still warm. The alderman is not. |
| gerald / leg.gerald | Gerald | item.bone_amulet | 7 | ResistFrost+15, Willpower+2 | LastGasp / 1 | | | A puddle, bottled. It remembers being bigger. |
| pellets_receipt / leg.pellets_receipt | Pellet's Receipt | item.iron_ring | 4 | Luck+2 | LuckyDip / 0.3 | | | Paid in full, it says. It does not say for what. |
| lantern_jaw / leg.lantern_jaw | Lantern-Jaw | item.hand_axe | 5 | MeleeDamage+4, Accuracy+6 | Bloodlust / 8 | | | Named for the face it left on the last man who argued. |
| levy_steel / leg.levy_steel | Levy Steel | item.longsword | 6 | MeleeDamage+4, MaxStamina+15 | PerfectCleave / 0.6 | | | Tax, in the old sense: taken whether or not you agree. |
| deacons_patience / leg.deacons_patience | Deacon's Patience | item.staff | 6 | SpellPower+6, ManaRegen+1 | | | ability.mend: AddEffect=Shield,10,15 | He waited out three bishops and a flood. |
| hush / leg.hush | Hush | item.dagger | 5 | MeleeDamage+3, Stealth+15, CritChance+0.05 | FrostSneak / 0.5 | QuietKill / 1 | | It has never been heard. Not even by its owners. |
| sisters_thimble / leg.sisters_thimble | Sister's Thimble | item.leather_gloves | 7 | Dexterity+3, Evasion+6 | ParryWindow / 0.1 | | | She mended sails. Then she mended men. Then she stopped. |
| old_ulfs_chin / leg.old_ulfs_chin | Old Ulf's Chin | item.iron_helm | 8 | MaxHp+20, Armor+6 | IronChin / 3 | | | He was hit by everything down here and complained about none of it. |
| toll_road_boots / leg.toll_road_boots | Toll Road Boots | item.leather_boots | 4 | MoveSpeed*1.12, Armor+3 | Momentum / 1 | | | Every step paid for. Some of them twice. |
| candle_of_the_congregation / leg.candle_of_the_congregation | Candle of the Congregation | item.crystal_focus | 8 | SpellPower+6, ResistFire+15 | BurnSpread / 1 | | | Lit once, for everyone. It has not gone out since. |

- [ ] **Step 1: Write the failing tests**

```csharp
public class LegendaryDataTests
{
    private readonly ItemCatalog _cat = DataFiles.Catalog();
    public static readonly string[] Twelve = { "leg.gorms_molar", "leg.aldermans_seal", "leg.gerald", "leg.pellets_receipt", "leg.lantern_jaw", "leg.levy_steel", "leg.deacons_patience", "leg.hush", "leg.sisters_thimble", "leg.old_ulfs_chin", "leg.toll_road_boots", "leg.candle_of_the_congregation" };

    [Fact] public void TwentyFourLegendaries_TwelveNewOnesEachWithAnEffectAndALine()
    {
        Assert.Equal(24, _cat.Legendaries.Count);
        foreach (var id in Twelve)
        {
            var l = _cat.Legendaries[id];
            Assert.False(l.ShopOnly); Assert.True(_cat.TryBase(l.BaseId, out _), id); Assert.NotEqual("", l.Flavor);
            Assert.True(l.Triggers.Any() || l.Upgrades.Length > 0, $"{id} is only a stat block");
        }
        Assert.Equal(TriggerKind.Bloodlust, _cat.Legendaries["leg.lantern_jaw"].Trigger); Assert.Equal(8f, _cat.Legendaries["leg.lantern_jaw"].TriggerMagnitude);
        Assert.Equal(0.3f, _cat.Legendaries["leg.pellets_receipt"].TriggerMagnitude); Assert.Equal(0.1f, _cat.Legendaries["leg.sisters_thimble"].TriggerMagnitude);
        Assert.Contains(_cat.Legendaries["leg.aldermans_seal"].Triggers, t => t.Kind == TriggerKind.SellBonus && t.Magnitude == 0.2f);
        Assert.True(_cat.Legendaries["leg.deacons_patience"].Upgrades.Single().Targets("ability.mend"));
    }
    [Fact] public void TheWarriorGapIsClosed_ASwordAnAxeAndAMaceDrop()
    {
        var kinds = _cat.Legendaries.Values.Where(l => !l.ShopOnly).Select(l => _cat.Base(l.BaseId).Weapon?.Kind).Where(k => k != null).ToHashSet();
        Assert.Contains(WeaponKind.Sword, kinds); Assert.Contains(WeaponKind.Axe, kinds); Assert.Contains(WeaponKind.Mace, kinds);
    }
    [Fact] public void EveryNewLegendary_DropsFromTheChestTableByLevelTwelve()
    {
        var table = _cat.Table("table.chest") with { Rolls = 1, RarityWeights = new float[] { 0, 0, 0, 1 } };
        var rng = new Random(12); var seen = new HashSet<string>();
        for (var i = 0; i < 6000; i++) if (LootRoller.Roll(_cat, table, 12, rng) is { LegendaryName: { } n }) seen.Add(n);
        foreach (var id in Twelve) Assert.Contains(_cat.Legendaries[id].Name, seen);
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter LegendaryDataTests` → 12 legendaries, none new.
- [ ] **Step 3: Implement** — the twelve `.tres` files; `PartyController.Items.cs`; the four one-line changes in `PartyController.cs` (Shared-file members); `LootDrop.Interact`.
- [ ] **Step 4: Run** — tests PASS (`ShopRulesTests.SevenShopOnlyLegendaries...` and `TheOldFiveStillDrop` still PASS); `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors; `timeout 60 godot --headless --path . -- --seed=7 --quit-after=4 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "\[Game\] ready|ERROR"` → "24 legendaries", no errors.
- [ ] **Step 5: Commit** — `git add data/legendaries src/Party/PartyController.Items.cs src/Party/PartyController.cs src/Items/LootDrop.cs && git commit -m "feat(items): twelve legendaries with effects, and the party's item hooks"`

### Task 6: The XP curve

**Files:**
- Modify: `src/Characters/Leveling.cs:5-10`
- Test: `tests/Characters/LevelingTests.cs`

**Interfaces:**
- Consumes: `PartyController.AwardXp` (`:556-569`) and `UI/CharacterSheet.cs:61` read `XpToNext`/`MaxLevel`; both unchanged.
- Produces: `public const int MaxLevel = 30; public static int XpToNext(int level) => 25 * level * level + 50 * level;` and `public static int TotalXpTo(int level)` (sum of `XpToNext(1..level−1)`), for the test and the docs.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void XpToNext_IsTwentyFiveLSquaredPlusFiftyL() { Assert.Equal(75, Leveling.XpToNext(1)); Assert.Equal(1200, Leveling.XpToNext(6)); Assert.Equal(9000, Leveling.XpToNext(18)); }
[Fact] public void MaxLevel_IsThirty_AndTheCapHolds()
{
    var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); c.Level = 30;
    Assert.Equal(0, Leveling.AddXp(c, 1_000_000)); Assert.Equal(30, c.Level);
}
[Fact] public void TheCurve_ReachesEighteenAtFiftyTwoThousand_TwentyAtSeventyOne()
{   // sum of 25L^2+50L for L = 1..17 and 1..19 (the old curve needed 80,580 for level 18)
    Assert.Equal(52_275, Leveling.TotalXpTo(18)); Assert.Equal(71_250, Leveling.TotalXpTo(20));
    var c = CharacterState.Create("Wren", TestClasses.Rogue, Rank.Front); Leveling.AddXp(c, 60_000); Assert.Equal(18, c.Level);
}
[Fact] public void AnOldSave_KeepsItsLevelAndXp_AndJustLevelsOnTheNextGain()
{   // a v10 member at level 7 with 2,000 banked XP (the old curve needed 2,380): the next 1 XP is enough to level under the new one
    var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); c.Level = 7; c.Xp = 2000;
    Assert.Equal(1, Leveling.AddXp(c, 1)); Assert.Equal(8, c.Level); Assert.Equal(2001 - Leveling.XpToNext(7), c.Xp);
}
```

(Update `XpToNext_Increases` from `100` to `75`.)

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter LevelingTests` → 100 ≠ 75.
- [ ] **Step 3: Implement** the two constants and `TotalXpTo`.
- [ ] **Step 4: Run to see them pass**; build the game → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Characters/Leveling.cs && git commit -m "feat(characters): XP curve 25L^2+50L, level cap 30"`

### Task 7: Skill trees: duo nodes, keystones, Wild Hunt, Thorns, Parry

**Files:**
- Modify: `src/Characters/Skills/SkillRules.cs:7-15` (`WhyCannotLearn`), `src/Characters/Skills/SkillProfile.cs:58-67` (`SkillCatalog`), 12 tier-6 files + `ranger_beastcraft_*.tres` (7) + `ranger_beastcraft_5_alpha.tres` + `rogue_duelist_3_parry.tres` + `warrior_bulwark_2_second_wind.tres` + `mage_fire_6_phoenix.tres` in `data/skills/`
- Create: `data/skills/warrior_duo_3_grim_resolve.tres`, `rogue_duo_3_opportunist.tres`, `ranger_duo_3_quick_draw.tres`, `mage_duo_3_attunement.tres`
- Test: `tests/Characters/SkillRulesTests.cs` (add), `tests/Characters/SkillDataTests.cs` (new, reads `DataFiles.Skills()`)

**Interfaces:**
- Consumes: `SkillProfile.AnyOf/IsDuo/IsKeystone/Trigger2/Upgrades` (Task 4), `UpgradeParser` syntax for `MoreUpgrades`.
- Produces: `SkillRules.WhyCannotLearn` adds, after the `Prerequisites` loop: `if (s.AnyOf.Length > 0 && !s.AnyOf.Any(c.LearnedSkills.Contains)) return $"{s.Name} needs a skill from one of its branches first";`. `SkillCatalog.Branches(classId)` excludes duo nodes (`Where(s => !s.IsDuo)`); `SkillCatalog.Duo(classId) => ForClass(classId).FirstOrDefault(s => s.IsDuo)`.

Data. Duo nodes (`Branch = "Duo"`, `Tier = 3`, `Cost = 1`, `Prerequisites` empty, copy any node file and add `AnyOf`, `Trigger2`, `TriggerMagnitude2`, `MoreUpgrades` lines, which every skill file gains with defaults `PackedStringArray()`, `"None"`, `0`, `PackedStringArray()`):

| Id | DisplayName | ClassId | AnyOf | Modifiers | Trigger / Magnitude | Description |
|---|---|---|---|---|---|---|
| skill.warrior.duo.grim_resolve | Grim Resolve | class.warrior | skill.warrior.berserker.heavy_hands, skill.warrior.bulwark.second_wind, skill.warrior.commander.tactician | MeleeDamage+2 | SecondWind / 6 | Blocking restores 6 stamina; +2 melee damage. Needs a tier-2 skill of any branch. |
| skill.rogue.duo.opportunist | Opportunist | class.rogue | skill.rogue.assassin.venom_crit, skill.rogue.duelist.venom_mastery, skill.rogue.trickster.smoke_bomb | CritChance+0.04 | VenomCrit / 1 | Crits poison; +4% crit chance. Needs a tier-2 skill of any branch. |
| skill.ranger.duo.quick_draw | Quick Draw | class.ranger | skill.ranger.marksman.piercing, skill.ranger.beastcraft.tracker, skill.ranger.survivalist.poison_arrow | AttackSpeed*1.1 | Scavenger / 1 | +10% attack speed; bow kills recover an arrow. Needs a tier-2 skill of any branch. |
| skill.mage.duo.attunement | Attunement | class.mage | skill.mage.fire.meteor, skill.mage.frost.chain_frost, skill.mage.warding.arcane_barrier | SpellPower+3 | SoulSiphon / 4 | Kills restore 4 mana; +3 spell power. Needs a tier-2 skill of any branch. |

Keystones (tier 6; ids, files, `Cost = 2` and `Prerequisites` unchanged; every one's description ends with its price):

| File | DisplayName | Modifiers | Trigger / Magnitude | Upgrade group (existing fields) | MoreUpgrades | Description |
|---|---|---|---|---|---|---|
| warrior_berserker_6_rampage | Red Mist | MeleeDamage*1.3 | NoGuard / 1 | | | +30% melee damage. You cannot guard. |
| warrior_bulwark_6_unbreakable | Unbreakable | Armor+12, MoveSpeed*0.9 | (GrantsAbility ability.iron_skin stays) | | | Learn Iron Skin: a 60-point shield on yourself. +12 armor; −10% move speed. |
| warrior_commander_6_warlord | Warlord | MaxStamina-20 | | UpgradeAbility ability.rally, BuffMul 1.5 | ability.war_cry: CooldownMul=0.8 | Rally heals half again as much and War Cry recovers 20% faster. −20 max stamina. |
| rogue_assassin_6_death_mark | Death Mark | CritMultiplier+0.6, Accuracy-15 | | | | +60% crit damage. −15 accuracy. |
| rogue_duelist_6_perfect_form | Perfect Form | AttackSpeed*1.2, Evasion+8, MaxHp*0.8 | | | | +20% attack speed, +8 evasion. −20% max HP. |
| rogue_trickster_6_master_thief | Master Thief | Luck+5, Stealth+20, Armor-10 | | | | +5 Luck, +20 stealth. −10 armor. |
| ranger_marksman_6_barrage | Barrage | Accuracy-10 | | (keep its ExtraProjectiles and CooldownMul) | | Multishot fires two more arrows and recovers faster. −10 accuracy. |
| ranger_beastcraft_6_apex_hunter | Apex Hunter | RangedDamage+8, CritMultiplier+0.4, Evasion-10 | | | | +8 ranged damage, +40% crit damage. −10 evasion. |
| ranger_survivalist_6_survivor | Survivor | MaxHp+40, HpRegen+1, AttackSpeed*0.9 | | | | +40 max HP, +1 HP regen. −10% attack speed. |
| mage_fire_6_phoenix | Phoenix | | Bloodlust / 15 | UpgradeAbility ability.fireball, DamageMul 1.3, CostMul 1.5 | | Kills heal you for 15; Fireball deals 30% more and costs 50% more mana. |
| mage_frost_6_absolute_zero | Absolute Zero | | | UpgradeAbility "ability.frostbolt,ability.frost_nova,ability.chain_frost", OnHitDurationMul 2 (new `[Export] float OnHitDurationMul = 1f` in the SkillDef Upgrade group) | ability.fireball: CostMul=2 | Frost you apply lasts twice as long. Fireball costs double. |
| mage_warding_6_archmage | Archmage | MaxHp*0.85 | | UpgradeAbility "ability.mend,ability.arcane_shield", BuffMul 2 | | Mend and Arcane Shield are twice as strong. −15% max HP. |

Other edits: the seven `ranger_beastcraft_*` files set `Branch = "Wild Hunt"` (ids and file names stay); `ranger_beastcraft_5_alpha`: `Trigger = "ArrowShatter"`, `TriggerMagnitude = 1`, Description "Your arrows Shatter a Frosted target on any hit, not only a crit or a heavy."; `rogue_duelist_3_parry`: Description "After a parry, counter for 50% weapon damage." (trigger unchanged: combat-feel fires Riposte on a parry); `warrior_bulwark_2_second_wind`: `Trigger2 = "Thorns"`, `TriggerMagnitude2 = 4`, Description "Blocking a hit restores 10 stamina. Attackers take 4 damage back."

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Characters/SkillRulesTests.cs (add)
[Fact] public void DuoNode_IsLearnableThroughEitherBranch_AndNotWithout()
{
    var cat = Catalog();
    cat.Skills["w.duo"] = new SkillProfile { Id = "w.duo", Name = "Grim Resolve", ClassId = "class.warrior", Branch = "Duo", Tier = 3, AnyOf = new[] { "w.second", "w.cleave" }, Trigger = TriggerKind.SecondWind, TriggerMagnitude = 6 };
    var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); w.SkillPoints = 9;
    Assert.Contains("one of its branches", SkillRules.WhyCannotLearn(w, cat.Skills["w.duo"])!);
    SkillRules.Learn(w, cat.Skills["w.cleave"], cat);
    Assert.True(SkillRules.Learn(w, cat.Skills["w.duo"], cat));
    var other = CharacterState.Create("Ash", TestClasses.Warrior, Rank.Front); other.SkillPoints = 9;
    SkillRules.Learn(other, cat.Skills["w.iron"], cat); SkillRules.Learn(other, cat.Skills["w.second"], cat);
    Assert.True(SkillRules.Learn(other, cat.Skills["w.duo"], cat));
    Assert.DoesNotContain("Duo", cat.Branches("class.warrior")); Assert.Equal("w.duo", cat.Duo("class.warrior")!.Id);
}
[Fact] public void DuoNode_RespecRefundsItAndItIsLearnableAgainThroughAnotherBranch()
{
    var cat = Catalog(); cat.Skills["w.duo"] = new SkillProfile { Id = "w.duo", ClassId = "class.warrior", Branch = "Duo", Tier = 3, AnyOf = new[] { "w.second", "w.cleave" }, Trigger = TriggerKind.SecondWind, TriggerMagnitude = 6 };
    var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); w.SkillPoints = 9;
    SkillRules.Learn(w, cat.Skills["w.cleave"], cat); SkillRules.Learn(w, cat.Skills["w.duo"], cat);
    Assert.Equal(6f, SkillRules.TriggerMagnitude(w, cat, TriggerKind.SecondWind));
    Assert.Equal(2, SkillRules.Respec(w, cat)); Assert.Equal(9, w.SkillPoints); Assert.Equal(0f, SkillRules.TriggerMagnitude(w, cat, TriggerKind.SecondWind));
    SkillRules.Learn(w, cat.Skills["w.iron"], cat); SkillRules.Learn(w, cat.Skills["w.second"], cat);
    Assert.True(SkillRules.Learn(w, cat.Skills["w.duo"], cat)); Assert.Equal(16f, SkillRules.TriggerMagnitude(w, cat, TriggerKind.SecondWind));
}
// tests/Characters/SkillDataTests.cs
public class SkillDataTests
{
    private readonly SkillCatalog _cat = DataFiles.Skills();
    private static readonly string[] Classes = { "class.warrior", "class.rogue", "class.ranger", "class.mage" };

    [Fact] public void EightyEightNodes_FourDuos_TwelveKeystones_WildHunt()
    {
        Assert.Equal(88, _cat.Skills.Count);
        foreach (var c in Classes) { Assert.Equal(3, _cat.Branches(c).Count()); var duo = _cat.Duo(c); Assert.NotNull(duo); Assert.Equal(3, duo!.Tier); Assert.Equal(3, duo.AnyOf.Length); Assert.All(duo.AnyOf, p => Assert.Equal(2, _cat.Skills[p].Tier)); Assert.Equal(3, duo.AnyOf.Select(p => _cat.Skills[p].Branch).Distinct().Count()); }
        Assert.Contains("Wild Hunt", _cat.Branches("class.ranger")); Assert.DoesNotContain("Beastcraft", _cat.Branches("class.ranger"));
        Assert.Equal("Wild Hunt", _cat.Skills["skill.ranger.beastcraft.alpha"].Branch); // the id stays for old saves
    }
    [Fact] public void EveryKeystone_TradesSomething()
    {
        var keystones = _cat.Skills.Values.Where(s => s.IsKeystone).ToList(); Assert.Equal(12, keystones.Count);
        foreach (var k in keystones)
        {
            var costs = k.Modifiers.Any(m => m.Op == ModOp.Add ? m.Value < 0 : m.Value < 1) || k.Trigger == TriggerKind.NoGuard || k.AllUpgrades.Any(u => u.CostMul > 1);
            Assert.True(costs, $"{k.Name} has no price"); Assert.Equal(2, k.Cost);
        }
        var redMist = _cat.Skills["skill.warrior.berserker.rampage"]; Assert.Equal("Red Mist", redMist.Name); Assert.Contains(redMist.Modifiers, m => m.Stat == Stat.MeleeDamage && m.Op == ModOp.Mul && Math.Abs(m.Value - 1.3f) < 0.001f); Assert.Equal(TriggerKind.NoGuard, redMist.Trigger);
        var zero = _cat.Skills["skill.mage.frost.absolute_zero"]; Assert.Equal(2f, zero.Upgrade!.OnHitDurationMul); Assert.True(zero.Upgrade.Targets("ability.chain_frost")); Assert.Equal(2f, zero.Upgrades.Single().CostMul); Assert.True(zero.Upgrades.Single().Targets("ability.fireball"));
        Assert.Equal(15f, _cat.Skills["skill.mage.fire.phoenix"].TriggerMagnitude);
    }
    [Fact] public void Thorns_AlphaAndParry_AreLive()
    {
        var sw = _cat.Skills["skill.warrior.bulwark.second_wind"]; Assert.Equal(TriggerKind.Thorns, sw.Trigger2); Assert.Equal(4f, sw.TriggerMagnitude2);
        Assert.Equal(TriggerKind.ArrowShatter, _cat.Skills["skill.ranger.beastcraft.alpha"].Trigger);
        Assert.Equal(TriggerKind.Riposte, _cat.Skills["skill.rogue.duelist.parry"].Trigger); Assert.Contains("parry", _cat.Skills["skill.rogue.duelist.parry"].Description);
        Assert.Equal(0, _cat.Skills.Values.Count(s => s.Trigger == TriggerKind.Momentum || s.Trigger2 == TriggerKind.Momentum)); // Rampage became Red Mist and Alpha shatters; Toll Road Boots carry Momentum now
    }
    [Fact] public void EveryPrerequisite_Exists_AndChainsStayStrictInsideABranch()
    {
        foreach (var s in _cat.Skills.Values) { foreach (var p in s.Prerequisites.Concat(s.AnyOf)) Assert.True(_cat.Skills.ContainsKey(p), $"{s.Id} -> {p}"); if (!s.IsDuo && s.Tier > 0) Assert.Single(s.Prerequisites); }
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "SkillRulesTests|SkillDataTests"` → `AnyOf` refused, 84 nodes.
- [ ] **Step 3: Implement** — rules, catalog, `SkillDef` export `OnHitDurationMul`, the 4 new files, the 12 keystone edits, the Wild Hunt/Alpha/Parry/Second Wind/Phoenix edits. Every skill file gains the four new export lines (a `sed` over `data/skills/*.tres` after `TriggerMagnitude = ...` is fine).
- [ ] **Step 4: Run to see them pass**; build the game; `timeout 60 godot --headless --path . -- --seed=7 --quit-after=4 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "\[Game\] ready|ERROR"` → "88 skills".
- [ ] **Step 5: Commit** — `git add data/skills src/Characters/Skills src/Characters/Defs/SkillDef.cs && git commit -m "feat(skills): duo nodes with AnyOf prerequisites, twelve keystones, Wild Hunt, Thorns at Bulwark 2, Parry text"`

### Task 8: The skill tree screen draws duos between the columns and marks keystones

**Files:**
- Modify: `src/UI/Skills/SkillTreeScreen.cs:44` (hint), `:56-88` (`Refresh`), `:90-122` (`Style`, `Describe`)

**Interfaces:**
- Consumes: `SkillCatalog.Branches/Duo`, `SkillProfile.IsDuo/IsKeystone/AnyOf/Trigger2/AllUpgrades`.
- Produces: `Refresh` builds the branch columns as today, and after the first column inserts a duo column (`VBoxContainer`, `CustomMinimumSize (170, 0)`) holding: a title label "Duo" (font 16), then three invisible rows (`Control { CustomMinimumSize = (170, 40) }` each preceded, after the first, by a `"│"` label with `Modulate.A = 0`) so the duo button lands level with the tier-3 row, then the duo button (`CustomMinimumSize (170, 40)`), then two labels `"◄  ►"` (font 12, `LockedColor`) under it pointing at the neighbours. A keystone's button text is prefixed `"★ "` after the learned/available glyph, and its tooltip has a line `"Keystone: a price comes with it."`. `Describe` adds `Passive: {Trigger2}` when set, prints every `AllUpgrades` entry (`OnHitDurationMul` as `status duration x{..}`, `AddEffect` as `also {Kind} {Magnitude:0}`), and for a duo lists `"Needs one of: {names of AnyOf}"`. The hint reads `"   Click a skill to learn it. Branches are chains; the Duo node opens from any branch's second tier. K: close"`.

- [ ] **Step 1: Implement** (Godot UI; the headless flow in Task 13 pins the learnability, a screenshot pins the layout).
- [ ] **Step 2: Build** → 0 errors.
- [ ] **Step 3: Look at it** — `godot --path . -- --seed=7 --no-ambush --no-portals --no-gathering --press=skills --screenshot=/tmp/skills.png --after=2` (if `--press=skills` is not a known action, open with the K action name found in `Settings.Rebindable`; the existing `--press=quest_log` shows the pattern at `DebugTools.cs` `_pressActions`): the duo button sits level with the tier-3 row between columns one and two; tier-6 buttons carry ★.
- [ ] **Step 4: Commit** — `git add src/UI/Skills/SkillTreeScreen.cs && git commit -m "feat(ui): the skill tree shows the duo node between the branches and marks keystones"`

### Task 9: Stack pricing and the buyback ledger

**Files:**
- Modify: `src/Items/Core/Prices.cs:9-19`, `src/Items/Core/ShopRules.cs:97-106` (`TrySell`) and new members, `src/Save/Core/SaveDtos.cs:61-74` (`PartyDto.Buyback`, `SoldDto`), `src/Party/PartyController.cs:824-838,886-899,902-908` (three one-line additions, see Shared-file members), `src/Party/PartyController.Items.cs` (`Buyback`, `ShopBuyback`)
- Test: `tests/Items/PricesTests.cs` (add), `tests/Items/ShopRulesTests.cs` (add)

**Interfaces:**
- Produces: `Prices.UnitValue(ItemInstance)` (today's `Value` formula, the Ammo rule included), `Prices.Value(item) = item.Kind == Ammo ? UnitValue : UnitValue × StackCount`, `Prices.UnitBuyPrice(item) = floor(2.5 × UnitValue)`, `Prices.BuyPrice(item) = UnitBuyPrice × StackCount`, `Prices.SellPrice(item, float bonus = 0f) = floor(Value × (1 + bonus))`.
  `public sealed class BuybackLedger { public const int Capacity = 3; public IReadOnlyList<(ItemInstance Item, int Price)> Entries; public void Push(ItemInstance item, int price); public (ItemInstance, int)? Take(int index); public List<SoldDto> ToDto(); public void Restore(List<SoldDto>? dtos, ItemCatalog cat); }` — newest first, the fourth push drops the oldest. `ShopRules.TrySell(Inventory bag, int bagIndex, ref int gold, BuybackLedger? ledger = null, float sellBonus = 0f)` pushes a clone and the price paid. `ShopRules.TryBuyback(BuybackLedger ledger, int index, Inventory bag, ref int gold)` → null on success ("Nothing there." / "Not enough gold." / "The bag is full." otherwise), charges `Price`, adds the item, removes the entry. `SoldDto { ItemDto Item; int Price; }` in `SaveDtos.cs`; `PartyDto.Buyback` (`List<SoldDto>`, default empty; **no version bump**: a missing list loads empty).
  `PartyController`: `public BuybackLedger Buyback { get; } = new();` (partial file), `public string? ShopBuyback(int index)` mirroring `ShopBuy` (client: `Send(new Net.ShopBuybackIntent(index))`, server/solo: `ShopRules.TryBuyback(Buyback, index, Bag, ref gold)`, then `AddGold(gold - Gold)`, log `"Bought back {item} for {price} gold."`, `EventBus.RaiseGoldChanged` happens in `AddGold`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Items/PricesTests.cs (add)
[Fact] public void AStack_IsPricedByCount_OnBothSides()
{
    ItemInstance Potions(int n) { var p = ItemInstance.Simple("item.health_potion", "Health Potion", SlotKind.Consumable, stackable: true, count: n); return p; }
    Assert.Equal(4, Prices.UnitValue(Potions(5))); Assert.Equal(20, Prices.Value(Potions(5))); Assert.Equal(20, Prices.SellPrice(Potions(5)));
    Assert.Equal(10, Prices.UnitBuyPrice(Potions(1))); Assert.Equal(50, Prices.BuyPrice(Potions(5))); Assert.Equal(5 * Prices.BuyPrice(Potions(1)), Prices.BuyPrice(Potions(5)));
    Assert.Equal(3, Prices.Value(ItemInstance.Simple("item.arrow", "Arrow", SlotKind.Ammo, stackable: true, count: 10))); // arrows keep their own rule
}
[Fact] public void SellPrice_TakesABonus() { var s = ItemInstance.Simple("item.longsword", "Longsword", SlotKind.Weapon); s.Rarity = Rarity.Rare; s.ItemLevel = 4; Assert.Equal(144, Prices.SellPrice(s)); Assert.Equal(172, Prices.SellPrice(s, 0.2f)); }
// tests/Items/ShopRulesTests.cs (add)
[Fact] public void Buyback_KeepsTheLastThreeSales_AtTheirPrice()
{
    var cat = TestCatalog.Build(); var bag = new Inventory(); var ledger = new BuybackLedger(); var gold = 0;
    var sold = new[] { "item.longsword", "item.robe", "item.ring", "item.shield" }.Select(id => { var it = cat.Create(id); it.Rarity = Rarity.Magic; bag.TryAdd(it); return it; }).ToList();
    for (var i = 0; i < 4; i++) Assert.Null(ShopRules.TrySell(bag, bag.IndexOf(sold[i]), ref gold, ledger));
    Assert.Equal(3, ledger.Entries.Count); Assert.Equal("Shield", ledger.Entries[0].Item.BaseName); Assert.DoesNotContain(ledger.Entries, e => e.Item.BaseName == "Longsword");
    var price = ledger.Entries[1].Price; Assert.Equal(Prices.SellPrice(sold[2]), price);
    var before = gold; Assert.Null(ShopRules.TryBuyback(ledger, 1, bag, ref gold));
    Assert.Equal(before - price, gold); Assert.Equal(2, ledger.Entries.Count); Assert.Contains(bag.Items, i => i.BaseName == "Ring");
    gold = 0; Assert.Equal("Not enough gold.", ShopRules.TryBuyback(ledger, 0, bag, ref gold)); Assert.Equal("Nothing there.", ShopRules.TryBuyback(ledger, 5, bag, ref gold));
}
[Fact] public void Buyback_SellBonusRaisesTheSaleButTheBuybackCostsWhatWasPaid()
{
    var cat = TestCatalog.Build(); var bag = new Inventory(); var ledger = new BuybackLedger(); var gold = 0;
    var sword = cat.Create("item.longsword"); sword.Rarity = Rarity.Rare; sword.ItemLevel = 4; bag.TryAdd(sword);
    ShopRules.TrySell(bag, 0, ref gold, ledger, 0.2f); Assert.Equal(172, gold); Assert.Equal(172, ledger.Entries[0].Price);
}
[Fact] public void Buyback_RoundTripsThroughItsDto()
{
    var cat = TestCatalog.Build(); var ledger = new BuybackLedger(); var it = cat.Create("item.robe"); it.Rarity = Rarity.Rare; ledger.Push(it, 99);
    var back = new BuybackLedger(); back.Restore(ledger.ToDto(), cat);
    Assert.Equal(99, back.Entries[0].Price); Assert.Equal(Rarity.Rare, back.Entries[0].Item.Rarity); back.Restore(null, cat); Assert.Empty(back.Entries);
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "PricesTests|ShopRulesTests"` → build errors / 57 ≠ 20 for stacks.
- [ ] **Step 3: Implement** per Interfaces; `PartyController.ShopSell` passes `Buyback, SellBonus()`; `CaptureState` adds `Buyback = Buyback.ToDto(),`; `RestoreState` adds `Buyback.Restore(dto.Buyback, cat);` after the bag.
- [ ] **Step 4: Run to see them pass**; whole suite PASS (`SaveFormatTests` pin `CurrentVersion == 10`: untouched); build → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Items/Core/Prices.cs src/Items/Core/ShopRules.cs src/Save/Core/SaveDtos.cs src/Party/PartyController.cs src/Party/PartyController.Items.cs && git commit -m "feat(shop): stacks are priced by count; the merchant keeps the last three sales for buyback"`

### Task 10: Co-op: buyback over the wire

**Files:**
- Modify: `src/Net/Core/Protocol.cs:6-7` (Version 7, MessageKindCount 81), `:57` (append `ShopBuybackIntent` to `MsgKind` at the end of the enum, never in the middle), `src/Net/Core/Messages.cs:68-70`, `src/Net/ServerWorld.cs:227-232,408-418`, `src/Net/ClientWorld.cs:77`, `src/Party/PartyController.cs:840-848` (`ApplyShopSync` gains a parameter)
- Test: `tests/Net/NetCodecTests.cs` (add)

**Interfaces:**
- Produces: `[Msg(MsgKind.ShopBuybackIntent)] public sealed record ShopBuybackIntent(int Index) : NetMessage;` `ShopSync(int Floor, List<int> Sold, int Gold, List<SoldDto>? Buyback = null)`; server `SendShopSync` passes `party.Buyback.ToDto()`; server intent handler `case ShopBuybackIntent bb: if (!slot.Ready || !CanTrade(slot)) break; if (party.ShopBuyback(bb.Index) == null) Trace.Add("shop"); SendShopSync(); break;` (mirror `ShopBuyIntent`'s lines exactly, including whatever follows a successful buy there); client `ApplyShopSync(ss.Floor, ss.Sold, ss.Gold, ss.Buyback)`; `PartyController.ApplyShopSync(int floor, List<int> sold, int gold, List<SoldDto>? buyback = null)` restores the ledger (`Buyback.Restore(buyback, Game.Instance.Catalog)`) when `buyback != null`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void BuybackMessages_RoundTrip()
{
    Assert.Equal(2, RoundTrip(new ShopBuybackIntent(2)).Index);
    var sold = new SoldDto { Item = new ItemDto { DefId = "item.robe", Rarity = "Rare", ItemLevel = 6 }, Price = 99 };
    var sync = RoundTrip(new ShopSync(5, new List<int> { 1 }, 240, new List<SoldDto> { sold }));
    Assert.Equal(99, sync.Buyback!.Single().Price); Assert.Equal("item.robe", sync.Buyback.Single().Item.DefId);
    Assert.Null(RoundTrip(new ShopSync(5, new List<int>(), 0)).Buyback);
}
```

- [ ] **Step 2: Run to see it fail** — `cd tests && dotnet test --nologo -v q --filter NetCodecTests` → missing record; the existing kind-count test fails until `MessageKindCount` is 81.
- [ ] **Step 3: Implement** the six modifications.
- [ ] **Step 4: Run to see it pass**; build → 0 errors; `scripts/net_test.sh` → both RESULT lines ok (its merchant step buys and sells; the sale now also fills the ledger on the server and reaches the client in the next `ShopSync`).
- [ ] **Step 5: Commit** — `git add src/Net src/Party/PartyController.cs && git commit -m "feat(net): buyback intent and ledger in ShopSync (protocol 7)"`

### Task 11: What the cell can say: scores, upgrades, who can use it

**Files:**
- Modify: `src/Items/Core/ItemText.cs` (add), `src/Items/Core/Equipment.cs:52-58` (add `Worn`)
- Test: `tests/Items/ItemTextTests.cs` (new or add)

**Interfaces:**
- Produces: `Equipment.Worn(CharacterState c, ItemInstance item) => PreferredSlot(c, item) is { } s ? this[s] : null;`
  `ItemText.Score(ItemInstance item)`: `avg weapon damage × 2` + Σ over `item.Modifiers` of `Weight(stat) × value` for Add (`(value − 1) × 100 × 0.6` for Mul), with `Weight`: primaries 3; MeleeDamage/RangedDamage/SpellPower 2; Armor 1; MaxHp 0.3; MaxStamina/MaxMana 0.15; Accuracy/Evasion 0.5; CritChance 100; CritMultiplier 30; HpRegen 10; StaminaRegen 1; ManaRegen 5; Stealth/Perception 0.2; resists 0.2; AttackSpeed/MoveSpeed/Cooldown handled as Mul (Cooldown inverted: `(1 − value) × 100 × 0.6`); a legendary adds 5 for each trigger or upgrade it carries.
  `ItemText.IsUpgrade(ItemInstance item, CharacterState c)`: `item.IsEquippable && c.Equipment.PreferredSlot(c, item) != null && (c.Equipment.Worn(c, item) is not { } worn || Score(item) > Score(worn))`.
  `ItemText.UsableBy(ItemInstance item, IEnumerable<CharacterState> members) → IReadOnlyList<(string Glyph, bool Usable)>` with `Glyph(ClassProfile cls) = cls.Name[..Math.Min(2, cls.Name.Length)]` ("Wa", "Ro", "Ra", "Ma"); `Usable` = `SlotRules.SlotsFor(item.Kind).Any(s => Equipment.WhyCannotEquip(m, item, s) == null)` ignoring the two-handed/offhand occupancy reasons (check against a fresh `Equipment`? no: pass the member; only the `"Two-handed"`/`"Both hands"` strings count as usable).

- [ ] **Step 1: Write the failing tests**

```csharp
public class ItemTextTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();
    [Fact] public void Score_PrefersMoreDamageAndMoreStats()
    {
        var common = LootRoller.Generate(_cat, _cat.Base("item.longsword"), Rarity.Common, 1, new Random(1));
        var rare = LootRoller.Generate(_cat, _cat.Base("item.longsword"), Rarity.Rare, 8, new Random(1));
        Assert.True(ItemText.Score(rare) > ItemText.Score(common));
        var plate = _cat.Create("item.plate"); var robe = _cat.Create("item.robe"); Assert.True(ItemText.Score(plate) > ItemText.Score(robe));
    }
    [Fact] public void IsUpgrade_NeedsAUsableSlot_AndABetterScore_OrAnEmptySlot()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); var mage = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back);
        var plate = _cat.Create("item.plate"); Assert.True(ItemText.IsUpgrade(plate, w)); Assert.False(ItemText.IsUpgrade(plate, mage));
        w.Equipment.Equip(EquipSlot.Body, plate); Assert.False(ItemText.IsUpgrade(_cat.Create("item.robe"), w));
        var better = LootRoller.Generate(_cat, _cat.Base("item.plate"), Rarity.Rare, 10, new Random(3)); Assert.True(ItemText.IsUpgrade(better, w));
        Assert.False(ItemText.IsUpgrade(_cat.Create("item.potion"), w));
    }
    [Fact] public void UsableBy_GlyphsPerMember()
    {
        var members = new[] { CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front), CharacterState.Create("Sable", TestClasses.Rogue, Rank.Front), CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back) };
        var row = ItemText.UsableBy(_cat.Create("item.plate"), members);
        Assert.Equal(new[] { ("Wa", true), ("Ro", false), ("Ma", false) }, row);
        Assert.All(ItemText.UsableBy(_cat.Create("item.ring"), members), g => Assert.True(g.Usable));
        members[0].Equipment.Equip(EquipSlot.Weapon, _cat.Create("item.spear")); // two-handed: a shield is still "usable" by the class
        Assert.True(ItemText.UsableBy(_cat.Create("item.shield"), members)[0].Usable);
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter ItemTextTests` → build errors.
- [ ] **Step 3: Implement**; un-skip `AffixDataTests.AFloorSixteenRoll_BeatsAFloorEightRoll_OnAverage` (Task 2).
- [ ] **Step 4: Run to see them pass** — `--filter "ItemTextTests|AffixDataTests"` → PASS (if the 1.15 margin in the affix test fails on the shipped data, print both averages and lower the margin to 1.10 with a comment; do not touch the data).
- [ ] **Step 5: Commit** — `git add src/Items/Core/ItemText.cs src/Items/Core/Equipment.cs && git commit -m "feat(items): item scores, upgrade checks and class glyphs for the cells"`

### Task 12: Shop and bag UI: comparison, ▲, glyphs, buyback tab

**Files:**
- Modify: `src/UI/Inventory/ItemSlotControl.cs:16-19,31-68,77-101,103-113`, `src/UI/Inventory/InventoryScreen.cs:134-143`, `src/UI/Shop/ShopScreen.cs:16-26,72-79,160-255`

**Interfaces:**
- Consumes: `ItemText.Describe(item, compare)`, `ItemText.IsUpgrade`, `ItemText.UsableBy`, `Equipment.Worn`, `Prices.*`, `PartyController.Buyback/ShopBuyback/ShopSoldOnFloor/ShopStockFor/FocusedIndex`.
- Produces: `public enum ShopSection { Bag, Wares, Buyback }` (in `ShopScreen.cs`). `ItemSlotControl` gains `public ShopSection Section { get; set; }` (replaces `StockSide`; keep `public bool StockSide => Section != ShopSection.Bag;` for the headless flows that read it), `public bool Upgrade { get; set; }` (draws a green `▲` at the top-right, font 12, when true), `public IReadOnlyList<(string Glyph, bool Usable)> Glyphs { get; set; } = Array.Empty<...>()` (drawn as a row at the top-left, font 9, usable white at alpha 0.9, unusable grey at alpha 0.45, 2 px apart). Drag data `["shop"] = (int)Section`; `_CanDropData` accepts when the sections differ; `_DropData` calls `Shop.Dropped((ShopSection)(int)d["shop"], (int)d["i"])`; `_GuiInput` calls `Shop.Clicked(Section, Ref.BagIndex)`.
  `ShopScreen`: the left column becomes a `TabContainer` (`_tabs`) with tab 0 "His wares" (the existing `_stockGrid`) and tab 1 "Buyback" (`_buybackGrid`, `GridContainer { Columns = 3 }` holding exactly three cells with `Section = Buyback`, captions `"{price}g"`, empty cells captioned "—"); fortune appends its tab after these. `Clicked(ShopSection from, int index)`: Wares → `TryBuy`, Bag → `TrySell`, Buyback → `_party.ShopBuyback(index); Refresh();`. `Dropped(from, index) => Clicked(from, index)`. `Refresh` sets for every stock, buyback and bag cell: `Upgrade = item != null && ItemText.IsUpgrade(item, focused)`, `Glyphs = ItemText.UsableBy(item, _party.Members)`, and tooltips `ItemText.Describe(item, focused.Equipment.Worn(focused, item))` followed by the price line as today (`"Sold."`, `"Price: N gold"`, `"Sells for N gold"`, `"Buy back for N gold"`). The gold label gains a second line `"Focused: {focused.Name}"`. Public for the flow: `public int BuybackCount => _party.Buyback.Entries.Count; public ItemSlotControl BuybackCell(int i);`.
  `InventoryScreen.Refresh` (lines 134-143): `compare = focused.Equipment.Worn(focused, it)`, `cell.Upgrade = ItemText.IsUpgrade(it, focused)`, `cell.Glyphs = ItemText.UsableBy(it, _party.Members)` (equip cells: `Glyphs` empty, `Upgrade` false).

- [ ] **Step 1: Implement** (Godot UI; the headless flow pins buyback and the comparison text; the drag test `--test-drag` must still pass).
- [ ] **Step 2: Build** → 0 errors; `godot --path . -- --test-drag 2>&1 | grep -E "RESULT|ERROR"` → its RESULT unchanged.
- [ ] **Step 3: Look at it** — `godot --path . -- --seed=7 --floor=3 --no-ambush --no-portals --no-gathering --drop=item.plate_armor,item.gold_ring,item.staff --screenshot=/tmp/bag.png --after=3 --press=inventory` (the action name as `Settings.Rebindable` lists it): ▲ on the plate for Brand, glyph rows on each cell.
- [ ] **Step 4: Commit** — `git add src/UI/Inventory src/UI/Shop && git commit -m "feat(ui): shop tooltips compare, cells show an upgrade arrow and who can use them, a Buyback tab"`

### Task 13: `--test-items-and-builds`, docs, full verification

**Files:**
- Modify: `src/Core/DebugTools.cs` (flag beside line 327, defaults beside lines 366-371, dispatch beside line 738, flow `RunItemsAndBuildsTest` modelled on `RunSafeRoomTest` lines 2685-2700), `docs/development.md` (test list after the `--test-safe-room` lines; a new section `## Items, legendaries and skill trees` before `## Layout`)

**Interfaces:**
- Consumes: everything above; `Main.Instance.Builder.SpawnEnemy(id, position, floor)`, `Enemy.ReceiveAttack`, `party.OnHitLanded`, `party.Teleport`, `Main.SaveGame/LoadGame`, `Game.Instance.Catalog/Skills`, `DebugTools.NoGathering`.
- Produces: `--test-items-and-builds` (sets `NoAmbush = NoPortals = NoGathering = true; TraceAi = false; _quitAfter = 45; _seed ??= 7; ForcedLethality ??= 1f`), printing `[items] ...` lines and `[items] RESULT ok` or `[items] RESULT FAIL (why; why)`.

- [ ] **Step 1: Write the flow** (steps gated on `_elapsed`, each printing what it measured; every assertion goes through `Fail(why)`):
  1. (t ≥ 1.0) **Loot curve**: with `cat.Table("table.chest") with { Rolls = 1 }` and `new Random(16)`, average `ItemText.Score` of 600 equippable rolls at item level 8 and at 16: `avg16 > avg8 × 1.1`. Print both.
  2. **Stack price**: `var five = cat.Create("item.health_potion", 5)`: `Prices.BuyPrice(five) == 5 × Prices.BuyPrice(cat.Create("item.health_potion"))` and `Prices.SellPrice(five) == 5 × Prices.UnitValue(five)`.
  3. **Buyback**: `party.AddGold(1000)`; add `five` to the bag; `goldBefore = party.Gold`; `party.ShopSell(BagSlot.Bag(idx))` → `party.Buyback.Entries.Count == 1`, gold rose by `Prices.SellPrice(five)`; `party.ShopBuyback(0) == null` → `party.Gold == goldBefore`, the bag holds a 5-stack again, `Buyback.Entries.Count == 0`. Then open the shop (`Main.Instance.Hud.Shop.Open(floor)` as the saferoom flow does) and assert `Shop.BuybackCount == 0` and a stock cell's tooltip contains `"Equipped:"` when Brand wears a weapon and the cell holds one (comparison in the shop); close it.
  4. **Keystone trade-off**: Sable (`Members[1]`): `SkillPoints = 20`; learn `skill.rogue.duelist.{footwork,flurry,venom_mastery,parry,cutthroat,dance_of_blades,perfect_form}` in order; `MaxHp` after ≈ `before × 0.8` (±1) and `AttackSpeed` after ≈ `before × 1.2`. Brand: `SkillPoints = 20`; learn the berserker chain to `rampage` (Red Mist): `party.CanGuard(brand) == false`, `MeleeDamage` ≈ ×1.3. Print all four numbers.
  5. **Duo via either branch**: Ilex: `SkillPoints = 20`; `WhyCannotLearn(ilex, attunement)` contains `"one of its branches"`; learn `fire.{kindling,ember_mind,meteor}` → `Learn(attunement)` true and `party.Trigger(ilex, SoulSiphon) == 4`; `SkillRules.Respec(ilex, skills)`; learn `warding.{mana_well,focus,arcane_barrier}` → `Learn(attunement)` true again.
  6. **Legendary trigger fires**: `var axe = LootRoller.MakeLegendary(cat, cat.Legendaries["leg.lantern_jaw"], 8, new Random(1))`; `brand.Equipment.Equip(EquipSlot.Weapon, axe); brand.Recalculate()`; `party.Trigger(brand, Bloodlust) == 8`; `axe.Weapon.MaxDamage ≈ 12 × 1.24` (hand axe is level 2); spawn one regular enemy of the floor 2 m ahead of Brand (`b.SpawnEnemy(id, pos, floor)`, the id as `--test-hit-fx` picks it), `brand.Hp = brand.Stats[MaxHp] − 30`, `var r = enemy.ReceiveAttack(brand.Stats, new AttackProfile { MinDamage = 9999, MaxDamage = 9999 }, new HitContext { DefenderStunned = true }, party.Rng, brand.Name); party.OnHitLanded(brand, enemy, r);` → `brand.Hp` rose by 8 (±0.5). **Burn spread**: equip the Candle on Ilex (`EquipSlot.Offhand`), spawn two enemies 1 m apart 3 m from Ilex, hit the first with `new AttackProfile { MinDamage = 1, MaxDamage = 1, OnHit = new[] { new StatusApplication(StatusKind.Burn, 4, 2, 1f) } }` and call `party.OnHitLanded(ilex, e1, r)` → `e2.Effects.Has(StatusKind.Burn)`. **Gerald**: equip on Wren (`EquipSlot.Amulet`), `wren.Hp = 0.2 × MaxHp`, an enemy 2 m from Wren with no Frost; `EventBus.Instance.RaiseMemberDamaged(2, 1f)` → it has Frost; remove the Frost, raise again → it has no Frost (once per floor). Kill the spawned enemies.
  7. **Save round trip**: `main.SaveGame("items")`, `main.LoadGame("items")`; after 1 s: `party.Trigger(Members[0], Bloodlust) == 8`, `Members[0].WeaponProfile.MaxDamage ≈ 14.9`, `Members[1].LearnedSkills` holds `perfect_form`, `party.Buyback.Entries.Count == 0`.
  8. `Finish()` → RESULT.
- [ ] **Step 2: Run** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && timeout 90 godot --headless --path . -- --test-items-and-builds 2>&1 | grep -E "^\[items\]|RESULT|ERROR"` → `[items] RESULT ok`.
- [ ] **Step 3: Every other flow still passes** — `--test-skills` (prints its lines; Brand's chain still learns), `--test-abilities --no-ambush --no-portals` → RESULT ok, `--test-save` → RESULT match, `--test-safe-room` → RESULT ok (its shop step: 12/11 wares unchanged), `--test-gold --no-ambush --no-portals` → RESULT ok (coins ×1.0 without the Seal), `scripts/net_test.sh` → both ok; `cd tests && dotnet test --nologo -v q` → all PASS.
- [ ] **Step 4: Docs** — `docs/development.md`: add `godot --headless --path . -- --test-items-and-builds   # loot curve 8 vs 16, stack prices, buyback, a keystone's price, the duo node from two branches, a legendary trigger, Burn spread, Gerald, save round trip; prints [items] RESULT` to the test list, and the section:

  > ## Items, legendaries and skill trees
  > Item level is the floor (`TierRules.ItemLevel`). Base damage and the Armor base modifier scale by +4% per item level above the base's own `ItemLevelMin` (`ItemRules.BaseScale`, derived from `ItemInstance.ItemLevel`, so saves store nothing new). Affixes have tiers at item levels 1–8 and 12/16/20 (data/affixes); on-hit affixes grow their status magnitude +25% per tier; rolled values round to whole numbers, 2 decimals for crit stats, 1 otherwise. Six affixes added: Cruel, of Reverie, of Breath, of Dimming, of the Owl, of Alacrity (the `Cooldown` stat, applied in `SkillRules.ApplyUpgrades`). Smart loot: with `ItemCatalog.SmartLootClasses` set (the party does it), 60% of rolls restrict the base pool to what a living member can use. Legendaries (`LegendaryDef`: `Trigger`, `TriggerMagnitude`, `Trigger2`, `TriggerMagnitude2`, `Upgrades` in the `UpgradeParser` syntax `"ability.x,ability.y: Key=Value ..."`) are read off equipment by `LegendaryRules`; `PartyController.Trigger` sums them with skills, `GetAbilityFor` applies their upgrades. Firing sites: kills/blocks/hits as before, `OnItemHit` (Burn spread), `OnItemMemberDamaged` (Gerald), coins in `LootDrop.Interact`. Hooks other systems read: `CanGuard`, `ParryWindowBonus`, `StaggerImmune`, `HeavyStunSeconds`/`ShatterStunSeconds`, `PerfectBlowCleave`, `SneakBonus`, `QuietKills`, `ArrowsShatter`, `LuckyDipDiscount`, `SellBonus`, `GoldFindBonus` (`PartyController.Items.cs`). Skills: 88 nodes; each class has a Duo node at tier 3 with `AnyOf` prerequisites (any branch's tier 2); tier 6 is a keystone with a price (data/skills, `IsKeystone`); the Ranger's third branch is Wild Hunt (ids keep `beastcraft`). XP: `25L²+50L`, cap 30. Shop: stacks are priced by count, the last three sales sit in the Buyback tab (`BuybackLedger`, saved in `PartyDto.Buyback`, synced in `ShopSync`, protocol 7), cells show ▲ for an upgrade over what the focused member wears and a class glyph row.

- [ ] **Step 5: Commit** — `git add src/Core/DebugTools.cs docs/development.md && git commit -m "test(items): the --test-items-and-builds flow and the docs"`

---

## Shared-file members (the exact additions to files other plans own)

`src/Party/PartyController.cs` (combat-feel owns the rest):
- line 97: `public AbilityProfile GetAbilityFor(CharacterState m, string id) => SkillRules.ApplyUpgrades(GetAbility(id), m, Game.Instance.Skills, LegendaryRules.Upgrades(m.Equipment, Game.Instance.Catalog));`
- line 99: `public float Trigger(CharacterState m, TriggerKind kind) => SkillRules.TriggerMagnitude(m, Game.Instance.Skills, kind) + LegendaryRules.Trigger(m.Equipment, Game.Instance.Catalog, kind);`
- `_Ready` (after the `foreach (var def in Registry.Instance.All<AbilityDef>())` line): `HookItemTriggers();`
- `OnHitLanded` (after `EventBus.Instance.RaiseHitLanded(m, r);`): `OnItemHit(m, target, r);`
- `ShopSell`: `ShopRules.TrySell(Bag, slot.BagIndex, ref gold)` → `ShopRules.TrySell(Bag, slot.BagIndex, ref gold, Buyback, SellBonus())`
- `CaptureState`: `Buyback = Buyback.ToDto(),` after `Gold = Gold,`; `RestoreState`: `Buyback.Restore(dto.Buyback, cat);` after `Save.SaveMapper.Restore(Bag, dto.Bag, cat);`
- `ApplyShopSync(int floor, List<int> sold, int gold)` → `ApplyShopSync(int floor, List<int> sold, int gold, List<Save.SoldDto>? buyback = null)` with `if (buyback != null) Buyback.Restore(buyback, Game.Instance.Catalog);`
- New file `src/Party/PartyController.Items.cs` (`public partial class PartyController`): `HookItemTriggers`, `OnItemHit`, `OnItemMemberDamaged`, `Buyback`, `ShopBuyback`, `CanGuard`, `ParryWindowBonus`, `StaggerImmune`, `HeavyStunSeconds`, `ShatterStunSeconds`, `PerfectBlowCleave`, `SneakBonus`, `QuietKills`, `ArrowsShatter`, `LuckyDipDiscount`, `GoldFindBonus`, `SellBonus`.

`src/Save/Core/SaveDtos.cs` (run-structure): `public sealed class SoldDto { public ItemDto Item { get; set; } = new(); public int Price { get; set; } }` and `PartyDto.Buyback` (`List<SoldDto>`, default `new()`). No version bump.

`src/Net/Core/Protocol.cs`, `Messages.cs`, `src/Net/ServerWorld.cs`, `src/Net/ClientWorld.cs` (co-op): `MsgKind.ShopBuybackIntent` appended; `Protocol.Version = 7`, `MessageKindCount = 81`; `ShopBuybackIntent(int Index)`; `ShopSync` gains `List<SoldDto>? Buyback = null`; `SendShopSync` fills it; the `ShopBuybackIntent` case; `ApplyShopSync` passes `ss.Buyback`.

`src/Characters/Stat.cs`, `Stats.cs` (nobody else's this round): `Stat.Cooldown` appended, base 1, clamp [0.5, 2].

`src/Characters/Skills/SkillProfile.cs`: `TriggerKind` values appended in this order: `HeavyStun, GoldFind, SellBonus, LastGasp, LuckyDip, PerfectCleave, FrostSneak, QuietKill, ParryWindow, IronChin, BurnSpread, NoGuard, ArrowShatter`.

Hooks another plan must read (this plan only exposes them): combat-feel reads `CanGuard(m)` where `Guarding` is set (`Adventurer.cs:347,462`, add `&& Party.CanGuard(State)`), `ParryWindowBonus(m)` in its parry window, `StaggerImmune(m)` in its stagger, `HeavyStunSeconds(m)` on a heavy hit in `MeleeSwing` and `ShatterStunSeconds(m)` in Shatter, `PerfectBlowCleave(m)` on a perfect blow in `MeleeSwing` (strike the other colliders of `_meleeCast` for that fraction), `SneakBonus(m, target)` as a factor on `HitContext.SneakMultiplier` (`Adventurer.cs:859,886`), `ArrowsShatter(m)` in Shatter; elites-and-bosses reads `QuietKills(m)` where a death wakes kin; fortune reads `LuckyDipDiscount()` in the Lucky Dip price.

## Self-review notes

- Spec coverage: tiers 12/16/20 (T2), base scaling (T1), six affixes (T2), smart loot 60% (T3), `LegendaryDef.Trigger/TriggerMagnitude/Upgrades` + `PartyController.Trigger` (T4, T5), twelve legendaries with effects and lines, Warrior sword/axe/mace in the drop pool (T5), keystones with trade-offs (T7), duo nodes with OR prerequisites drawn between columns (T7, T8), Wild Hunt and Alpha (T7), Parry text (T7; the firing is combat-feel's), capstone heal 15 (T7), Thorns 4 at Bulwark 2 (T7), XP curve and cap with old saves kept (T6), shop comparison, ▲, glyphs, stack pricing, buyback (T9–T12), headless flow (T13). Every item `.tres` edit is data-only; code paths that changed shape are `LegendaryDef`, `SkillDef`, `AffixDef` unchanged.
- Type consistency: `LegendaryRules.Trigger(Equipment, ItemCatalog, TriggerKind)` is the signature used in T5's `PartyController.Trigger` and in T4's tests; `ShopRules.TrySell(bag, index, ref gold, ledger, bonus)` in T9 and T13; `SoldDto` lives in `SaveDtos.cs` and is used by T9's ledger and T10's message; `ItemText.Score/IsUpgrade/UsableBy` (T11) are the names T2's deferred test, T12 and T13 use; `SkillProfile.AnyOf/IsDuo/IsKeystone/AllUpgrades` (T4) are used in T7 and T8.
- Spec points resolved here (report them): (1) "Cooldown ×0.9/0.85/0.8" has no stat today: `Stat.Cooldown` is added as a multiplier applied to ability cooldowns in `ApplyUpgrades`. (2) Keen's tier 1 (0.02–0.04) rounds to 0 today and adds nothing; `ItemRules.Decimals` fixes it (two decimals for crit stats). (3) Two effects on one legendary (the Seal, Hush) need two triggers: `Trigger2/TriggerMagnitude2` on `LegendaryDef` and `SkillDef` (the latter also carries Second Wind + Thorns). (4) Two upgrades on one keystone (Absolute Zero, Warlord) need `MoreUpgrades` and comma-listed ability ids. (5) `DamageResult` has no heavy/perfect flag and `Adventurer.cs` is combat-feel's, so Gorm's Molar, Levy Steel, Sister's Thimble, Old Ulf's Chin (the immunity half), Hush, Alpha and Red Mist's "cannot guard" are readable hooks; every other effect fires from a site this plan owns or an existing event. (6) "Capstone heals rise to 15": only Phoenix heals at tier 6; it goes to 15, Bloodlust (tier 3) stays 8. (7) "Companions auto-pick everything" is fortune's (junk 2 g); untouched.
