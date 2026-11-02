# Merchant and Shop Implementation Plan (plan C)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The travelling merchant: a campaign cast member who stands in every floor's safe room, talks, buys the party's loot and sells a seeded, depleting stock that includes seven new shop-only unique weapons.

**Architecture:** Engine-free rules (`ShopRules`, `MerchantLineBank`, the `Merchant` campaign section, the body rule, the validator rule) live in `*/Core` and are unit tested; the Godot side is an `Npc` with `IsMerchant`, a `ShopScreen` built from the inventory's slot controls, and three net messages so the server owns stock and purse. The stock is rolled from `gameSeed ^ floor`, so every peer and every reload agree and only the bought indices are saved.

**Tech Stack:** Godot 4.7.2 .NET, C#, xUnit (tests/, dev machine only), the existing `LootRoller`, `Inventory`, `DialogueScreen`, net intent/sync pattern.

**Spec:** `docs/superpowers/specs/2026-11-02-tavern-merchant-safe-rooms-design.md` (sections 4, 5, 6 for the generic bank, 7) and the contract `docs/superpowers/plans/2026-11-02-tavern-shared-interfaces.md` section C. Plans A (gold, `Prices`) and B (`FloorLayout.SafeRoom`, `SafeRoomRules.MerchantCell`, `EventBus.Gathered`) are merged before this plan starts.

## Global Constraints

- Engine-free code in `*/Core` namespaces, never `using Godot`; tests in `tests/` (dev machine, not in git); `tests/DungeonCrawler.Tests.csproj` already links `../src/Items/Core/**`, `../src/Adventure/Core/**`, `../src/Party/Core/**`, `../src/Net/Core/**`, `../src/Save/Core/**`.
- Build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before every headless run (clock skew on the shared NTFS folder makes incremental builds skip).
- Co-op is server-authoritative: `Net.NetSession.IsClient` sends intents and applies syncs; `!IsClient` (server or solo) mutates state.
- The merchant is **nobody else's story**: never a Secret, quest or Betray; never a floor NPC; exactly one per campaign.
- New shop-only legendaries never drop in the dungeon; the existing five never enter the shop's unique slot.
- Seven unique floors `{3, 6, 9, 12, 15, 18, 20}`, each of the seven wieldable kinds `Sword, Axe, Mace, Dagger, Polearm, Bow, Staff` exactly once per run.
- Prices come from plan A's `Prices.BuyPrice` / `Prices.SellPrice` / `Prices.Sellable`; the purse is `PartyController.Gold` / `AddGold` / `TrySpendGold`.
- Shipped campaign data (`data/adventures/builtin/*.json`) is not edited here; the merchant text for the shipped campaign is plan E's. Until E lands, the shipped campaign uses `MerchantLineBank.Generic`.
- Contract deviations (real type names): the catalog is `ItemCatalog` (not `Catalog`), legendary data is `LegendaryProfile` (not `LegendaryBase`); the plan uses the real names everywhere.

## Review Focus

1. A full bag: buying with no free slot must refuse with a logged line and leave the purse untouched (Task 5 `TryBuy_RefusesWhenBagFull_AndPurseIsUntouched`).
2. A bought item on reload or on a peer: the index is in `ShopSold`, so the slot shows sold and cannot be bought twice (Task 5 `Buy_SameIndexTwice_RefusesSecondTime`, Task 8 headless step).
3. Selling an unsellable item (iron key, quest relic) must refuse, not vanish the item (Task 5 `TrySell_RefusesKeysAndQuestItems`).
4. A campaign JSON from before this plan (no `Merchant`, no `Merchant: true` cast member) must still load, validate and get a merchant from repair, never crash a floor build (Task 2 `Repair_AddsAGenericMerchant_WhenNoneIsFlagged`, Task 6 null-safety in placement).
5. A player on a floor whose generator found no safe room (`SafeRoom == -1`) must simply have no merchant and no shop, with no error (Task 6 `PlaceMerchant` early return; Task 8 flow asserts a log line names the floor instead of throwing).

---

## File structure

| File | Responsibility |
|---|---|
| `src/Adventure/Core/Plans/CampaignPlan.cs` (modify) | `CastMember.Merchant`, `CampaignPlan.Merchant`, `MerchantPlan`, `MerchantFloorLines`, `CampaignPlan.MerchantCast()` |
| `src/Adventure/Core/Voice/MerchantLineBank.cs` (create) | `MerchantLineBank.Generic(title, antagonist)` |
| `src/Adventure/Core/Validate/CampaignRepair.cs`, `PlanValidator.cs` (modify) | one-merchant rule; merchant never a floor NPC |
| `src/Adventure/Core/NpcBodies.cs` (modify) | `ForCast(CastMember)`: merchant body rule |
| `src/Items/Defs/LegendaryDef.cs`, `src/Items/Core/ItemBase.cs`, `src/Items/Core/LootRoller.cs` (modify) | `ShopOnly` flag, excluded from dungeon rolls |
| `data/legendaries/*.tres` (create ×7) | the shop-only uniques |
| `src/Items/Core/ShopRules.cs` (create) | `UniqueFloors`, `UniqueKindFor`, `Roll`, `ShopStock`, `TryBuy`, `TrySell` |
| `src/Save/Core/SaveDtos.cs`, `src/Save/FloorStateCapture.cs` (modify) | `FloorStateDto.ShopSold` |
| `src/Party/PartyController.cs` (modify) | `ShopBuy`, `ShopSell`, `ShopSoldOnFloor`, `ShopStockFor` |
| `src/Adventure/Npc.cs`, `src/Dungeon/DungeonBuilder.cs` (modify) | `IsMerchant`, `GreetLine`, `PlaceMerchant` |
| `src/UI/Dialogue/DialogueScreen.cs`, `src/UI/Hud/Hud.cs` (modify); `src/UI/Shop/ShopScreen.cs` (create) | merchant dialogue, shop screen |
| `src/Net/Core/Protocol.cs`, `Messages.cs`, `src/Net/ServerWorld.cs`, `ClientWorld.cs` (modify) | `ShopBuyIntent`, `ShopSellIntent`, `ShopSync` |
| `src/Adventure/AdventureDirector.cs` (modify) | `Gathered` → merchant's gathering lines |
| `src/Core/DebugTools.cs`, `src/Net/NetTestFlow.cs`, `docs/development.md` (modify) | headless buy/sell steps |
| `tests/Items/ShopRulesTests.cs`, `tests/Adventure/MerchantPlanTests.cs`, `tests/Adventure/NpcBodiesTests.cs` (create/modify) | the engine-free proofs |

---

### Task 1: The `Merchant` campaign section and the generic line bank

**Files:**
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs:77-89` (CastMember), `:161-184` (CampaignPlan)
- Create: `src/Adventure/Core/Voice/MerchantLineBank.cs`
- Test: `tests/Adventure/MerchantPlanTests.cs`

**Interfaces:**
- Consumes: `Line`, `NpcScript`, `DialogueNode`, `ChoicePlan` from `FloorPlan.cs`; `DialogueEffect.Farewell`.
- Produces:
  `CastMember.Merchant : bool` (`[AiDescription("True for the one cast member who sells to the party from the safe rooms; never a story NPC, never a Secret, quest or Betray.")]`, default false).
  `public sealed class MerchantFloorLines { public int Floor { get; set; } public Line Greeting { get; set; } = new(); public Line Progress { get; set; } = new(); public Line Trade { get; set; } = new(); }`
  `public sealed class MerchantPlan { public NpcScript Briefing { get; set; } = new(); public List<MerchantFloorLines> FloorLines { get; set; } = new(); public List<Line> Gathering { get; set; } = new(); public MerchantFloorLines? For(int floor) }` (`For` returns the entry with that floor or null).
  `CampaignPlan.Merchant : MerchantPlan?` (`[AiNotNull]`, null when the campaign has none) and `public CastMember? MerchantCast() => Cast.FirstOrDefault(c => c.Merchant)`.
  `public static class MerchantLineBank { public static MerchantPlan Generic(string campaignTitle, string antagonistName); public static CastMember GenericCast(); }` — `Generic` returns a `Briefing` with node ids `hire`, `quakes`, `deal`, `sendoff` (two choices on `hire`, `quakes`, `deal`, all paths reaching `deal` then `sendoff`; `sendoff` has one Farewell choice), `FloorLines` for floors 1..20 (each `Greeting`, `Progress`, `Trade` non-empty, at most 220 characters, campaign title and antagonist substituted where the text names them), `Gathering` of exactly 3 lines. `GenericCast()` returns `{ Id = "cast_merchant", Name = "The Merchant", Merchant = true, Role = "a nervous trader who follows the party down to buy and sell", Secret = "", Persona = new Persona { VoiceStyle = "a nervous, over-polite trader counting coins under his breath", DefaultDelivery = "calm", Voice = "Achird" } }` (check the exact `Persona` property names in `src/Adventure/Core/Voice/Persona.cs` before writing).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Generic_HasBriefingThatReachesDeal_TwentyFloorLines_ThreeGatheringLines()
{
    var m = MerchantLineBank.Generic("The Gullet of Gorm", "Absalom Quill");
    Assert.Equal("hire", m.Briefing.Start);
    var ids = m.Briefing.Nodes.Select(n => n.Id).ToList();
    Assert.Equal(new[] { "hire", "quakes", "deal", "sendoff" }, ids);
    Assert.All(m.Briefing.Nodes.Take(3), n => Assert.Equal(2, n.Choices.Count));
    Assert.All(m.Briefing.Nodes.Take(3).SelectMany(n => n.Choices), c => Assert.NotNull(c.Next));
    Assert.True(ReachesDeal(m.Briefing)); // every path from "hire" visits "deal" (write a small graph walk helper in the test)
    Assert.Equal(Enumerable.Range(1, 20), m.FloorLines.Select(f => f.Floor));
    Assert.All(m.FloorLines, f => { Assert.InRange(f.Greeting.Text.Length, 1, 220); Assert.InRange(f.Progress.Text.Length, 1, 220); Assert.InRange(f.Trade.Text.Length, 1, 220); });
    Assert.Equal(3, m.Gathering.Count);
    Assert.Contains(m.FloorLines, f => f.Progress.Text.Contains("Gullet") || f.Progress.Text.Contains("Quill"));
}
[Fact] public void For_ReturnsTheFloorsLines_OrNull()
{
    var m = MerchantLineBank.Generic("T", "A");
    Assert.Equal(7, m.For(7)!.Floor);
    Assert.Null(m.For(0)); Assert.Null(m.For(21));
}
[Fact] public void MerchantCast_IsTheFlaggedMember()
{
    var c = new CampaignPlan { Cast = { new CastMember { Id = "a" }, new CastMember { Id = "b", Merchant = true } } };
    Assert.Equal("b", c.MerchantCast()!.Id);
    Assert.Null(new CampaignPlan().MerchantCast());
}
[Fact] public void GenericCast_IsAMerchantWithTheAchirdVoice()
{
    var c = MerchantLineBank.GenericCast();
    Assert.True(c.Merchant); Assert.Equal("", c.Secret); Assert.Equal("Achird", c.Persona!.Voice);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~MerchantPlanTests"`
Expected: compile errors `'CastMember' does not contain a definition for 'Merchant'`, `MerchantLineBank` not found.

- [ ] **Step 3: Add the types to `CampaignPlan.cs` and write `MerchantLineBank.cs`**

The bank's text is the implementer's, in a nervous trader's voice, floors 1-6 greedy and cheerful, 7-13 uneasy, 14-19 frightened, 20 a send-off, never stating any truth about the dungeon; substitute `campaignTitle` and `antagonistName` into at least three lines. Round-trip safety: `MerchantPlan` must serialise with `PlanFormat.Options` (plain properties, no dictionaries) because `PlanSchemas` reflects over `CampaignPlan` for the AI schema; `[AiNotNull]` on `Merchant` and `[AiDescription]` on the new classes like the neighbours.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~MerchantPlanTests"`
Expected: PASS (4 tests). Also `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~AiPlanner"` still passes (schema generation handles the new section).

- [ ] **Step 5: Commit**

```bash
git add src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/MerchantLineBank.cs
git commit -m "feat(merchant): the Merchant campaign section, the merchant cast flag and a generic line bank"
```

---

### Task 2: One merchant per campaign, never a floor NPC; the merchant body rule

**Files:**
- Modify: `src/Adventure/Core/Validate/CampaignRepair.cs:15-40`, `src/Adventure/Core/Validate/PlanValidator.cs:305-320`, `src/Adventure/Core/NpcBodies.cs`
- Test: `tests/Adventure/MerchantPlanTests.cs` (add), `tests/Adventure/NpcBodiesTests.cs` (add)

**Interfaces:**
- Consumes: Task 1's `CastMember.Merchant`, `CampaignPlan.Merchant`, `MerchantLineBank.GenericCast()/Generic()`.
- Produces:
  `CampaignRepair.Apply(campaign)`: when no cast member is flagged, appends `MerchantLineBank.GenericCast()` and records `"generic merchant added"`; when more than one is flagged, clears the flag on all but the first and records `"extra merchant flags cleared"`; when `campaign.Merchant == null`, sets `MerchantLineBank.Generic(campaign.Title, campaign.Antagonist.Name)` and records `"generic merchant lines added"`; a flagged member with a non-empty `Secret` has it cleared (`"merchant secret cleared"`).
  `PlanValidator.ValidateNpcsAndQuests`: an NPC whose `CastId` is the merchant's is dropped with `"dropped NPC {id}: the merchant is never a floor NPC"`.
  `NpcBodies.ForCast(CastMember cast)`: `"npc_merchant"` when `cast.Merchant`; otherwise `For(cast.Id, cast.Name, false, cast.Role, cast.Persona?.VoiceStyle)` except that a result of `"npc_merchant"` becomes `"npc_old_man"`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Repair_AddsAGenericMerchant_WhenNoneIsFlagged()
{
    var c = new CampaignPlan { Title = "T", Cast = { new CastMember { Id = "cast_x", Name = "X" } } };
    var repairs = CampaignRepair.Apply(c);
    Assert.Single(c.Cast, m => m.Merchant);
    Assert.NotNull(c.Merchant);
    Assert.Contains(repairs, r => r.Contains("generic merchant"));
}
[Fact] public void Repair_KeepsOnlyTheFirstMerchant_AndClearsItsSecret()
{
    var c = new CampaignPlan { Cast = { new CastMember { Id = "a", Merchant = true, Secret = "hides a knife" }, new CastMember { Id = "b", Merchant = true } } };
    CampaignRepair.Apply(c);
    Assert.True(c.Cast[0].Merchant); Assert.False(c.Cast[1].Merchant); Assert.Equal("", c.Cast[0].Secret);
}
[Fact] public void Validator_DropsAFloorNpcWearingTheMerchantsCastId()
{
    // build the smallest layout/plan/menu the existing validator tests use (copy their helper), one NPC with CastId "cast_m" in a plain room
    var campaign = new CampaignPlan { Cast = { new CastMember { Id = "cast_m", Merchant = true } } };
    var report = PlanValidator.Validate(layout, plan, menu, enforceBudget: false, campaign);
    Assert.Empty(plan.Npcs);
    Assert.Contains(report.Repairs, r => r.Contains("never a floor NPC"));
}
// NpcBodiesTests.cs
[Fact] public void ForCast_MerchantAlwaysWearsTheMerchantBody()
    => Assert.Equal("npc_merchant", NpcBodies.ForCast(new CastMember { Id = "m", Name = "Grim Harl", Merchant = true, Role = "a tall soldier" }));
[Fact] public void ForCast_AnAldermanWhoIsNotTheMerchant_WearsTheOldMan()
    => Assert.Equal("npc_old_man", NpcBodies.ForCast(new CastMember { Id = "c", Name = "Alderman Hesketh Crane", Role = "The employer, a plump alderman" }));
[Fact] public void ForCast_OthersKeepTheirGuess()
    => Assert.Equal("npc_priest", NpcBodies.ForCast(new CastMember { Id = "t", Name = "Deacon Tobit Hale", Role = "an eager young deacon" }));
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~MerchantPlanTests|FullyQualifiedName~NpcBodiesTests"`
Expected: FAIL (`ForCast` missing; repair assertions false; validator keeps the NPC).

- [ ] **Step 3: Implement the repair, validator and body rules**

`DungeonBuilder.cs:1189` builds a plan NPC's body from `NpcBodies.For(plan.Id, plan.Name, false, plan.Persona?.VoiceStyle, cast.Role, cast.Style)`; change that call to `cast != null ? NpcBodies.ForCast(castMember) : NpcBodies.For(...)` where `castMember` is `campaign.CastFor(plan.CastId)` (look at `CastWords` to find how the campaign is reached there).

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~MerchantPlanTests|FullyQualifiedName~NpcBodiesTests|FullyQualifiedName~GulletCampaign"`
Expected: PASS. If `GulletCampaignTests` asserts Crane's body is `npc_merchant`, update that assertion to `npc_old_man` and say so in the commit (the shipped campaign gets its real merchant in plan E; until then repair adds the generic one at load).

- [ ] **Step 5: Commit**

```bash
git add src/Adventure/Core/Validate/CampaignRepair.cs src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/NpcBodies.cs src/Dungeon/DungeonBuilder.cs
git commit -m "feat(merchant): one merchant per campaign, never a floor NPC; the merchant body is his alone"
```

---

### Task 3: Shop-only legendaries

**Files:**
- Modify: `src/Items/Defs/LegendaryDef.cs:10-33`, `src/Items/Core/ItemBase.cs:76-86`, `src/Items/Core/LootRoller.cs:50-57`
- Create: `data/legendaries/{shop_sword,shop_axe,shop_mace,shop_polearm,shop_shortbow,shop_dagger,shop_staff}.tres` (file names are the implementer's; ids below are fixed)
- Test: `tests/Items/ShopRulesTests.cs` (first tests), `tests/Items/DataFiles.cs` (extend the legendary reader with `ShopOnly`)

**Interfaces:**
- Produces: `LegendaryDef.ShopOnly` (`[Export] bool`, default false) → `LegendaryProfile.ShopOnly` (init, default false). `LootRoller.Roll` skips `ShopOnly` legendaries. Seven legendaries with ids `leg.shop_sword` (BaseId `item.longsword`), `leg.shop_axe` (`item.hand_axe`), `leg.shop_mace` (`item.mace`), `leg.shop_polearm` (`item.spear`), `leg.shop_bow` (`item.shortbow`), `leg.shop_dagger` (`item.dagger`), `leg.shop_staff` (`item.staff`), each `ShopOnly = true`, `ItemLevelMin = 3`, `Weight = 1.0`, 2 or 3 `Modifiers` and (for all but at most two) an `OnHitStatus` with `OnHitChance <= 0.5`, a `DisplayName` and a one-line `Flavor` (implementer's words).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SevenShopOnlyLegendaries_OneForEachWeaponKind_AtLevelThree()
{
    var legs = DataFiles.Legendaries().Where(l => l.ShopOnly).ToList();   // DataFiles reads data/legendaries/*.tres (extend it to parse ShopOnly)
    Assert.Equal(7, legs.Count);
    var kinds = legs.Select(l => DataFiles.Items()[l.BaseId].WeaponKind).OrderBy(k => k).ToList();
    Assert.Equal(new[] { WeaponKind.Sword, WeaponKind.Axe, WeaponKind.Mace, WeaponKind.Dagger, WeaponKind.Polearm, WeaponKind.Bow, WeaponKind.Staff }.OrderBy(k => k), kinds);
    Assert.All(legs, l => { Assert.Equal(3, l.ItemLevelMin); Assert.InRange(l.Modifiers.Length, 2, 3); Assert.True(DataFiles.Items()[l.BaseId].ItemLevelMin <= 3); });
}
[Fact] public void DungeonRolls_NeverProduceAShopOnlyLegendary()
{
    var cat = DataFiles.Catalog();                       // the ItemCatalog the weapon progression tests already build
    var table = cat.Table("table.chest") with { RarityWeights = LegendaryOnly };   // or whatever forces Legendary in RollRarity; read LootRoller.RollRarity and force it
    var rng = new Random(3);
    for (var i = 0; i < 3000; i++)
        if (LootRoller.Roll(cat, table, 12, rng) is { Rarity: Rarity.Legendary } item)
            Assert.DoesNotContain(cat.Legendaries.Values.Where(l => l.ShopOnly).Select(l => l.Name), n => n == item.LegendaryName);
}
[Fact] public void TheOldFiveStillDrop()
{   // same loop, assert each of Ember Fang, Whisperwind, Stormcaller, Bulwark of the Deep, Heart of Oak appears at least once at level 12
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopRulesTests"`
Expected: FAIL (`ShopOnly` not a member; zero shop-only legendaries).

- [ ] **Step 3: Add the flag, the exclusion and the seven data files**

`LootRoller.cs:52` filter gains `&& !l.ShopOnly`. The seven `.tres` follow `data/legendaries/whisperwind.tres` with the extra line `ShopOnly = true`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopRulesTests|FullyQualifiedName~WeaponProgression"`
Expected: PASS (the weapon progression test that counts legendaries must still see five dropping and may need its "all legendaries roll" assertion limited to `!ShopOnly`).

- [ ] **Step 5: Commit**

```bash
git add src/Items/Defs/LegendaryDef.cs src/Items/Core/ItemBase.cs src/Items/Core/LootRoller.cs data/legendaries/
git commit -m "feat(items): seven shop-only unique weapons that the dungeon never drops"
```

---

### Task 4: `ShopRules.Roll` and `UniqueKindFor`

**Files:**
- Create: `src/Items/Core/ShopRules.cs`
- Test: `tests/Items/ShopRulesTests.cs` (add)

**Interfaces:**
- Consumes: `ItemCatalog`, `LootRoller.Generate(cat, b, rarity, itemLevel, rng)`, `LootRoller.MakeLegendary(cat, l, itemLevel, rng)`, `ItemCatalog.Create(baseId, count)`, `Core.Rng.Mix`.
- Produces:
  `public static class ShopRules { public static readonly int[] UniqueFloors = { 3, 6, 9, 12, 15, 18, 20 }; public static readonly WeaponKind[] UniqueKinds = { WeaponKind.Sword, WeaponKind.Axe, WeaponKind.Mace, WeaponKind.Dagger, WeaponKind.Polearm, WeaponKind.Bow, WeaponKind.Staff }; public static bool IsUniqueFloor(int floor); public static WeaponKind UniqueKindFor(ulong gameSeed, int floor); public static int StockSeed(ulong gameSeed, int floor) => unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0x5106_5106UL) ^ 0x5A0BUL)); public static ShopStock Roll(ItemCatalog cat, ulong gameSeed, int floor); }`
  `public sealed class ShopStock { public List<ItemInstance> Items { get; } = new(); public int UniqueIndex { get; init; } = -1; }`
  `UniqueKindFor`: a Fisher-Yates shuffle of `UniqueKinds` with `new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ 0x0B1A_DE5UL)))`, indexed by `Array.IndexOf(UniqueFloors, floor)`; throws `ArgumentOutOfRangeException` for a non-unique floor.
  `Roll` order (seeded by `new Random(StockSeed(gameSeed, floor))`, item level `floor + 2`): 2 weapons (`Generate` on a random weapon base allowed at that level, rarity `Magic` or `Rare` by a 70/30 roll), 1 body armour (`SlotKind.Body`), 1 offhand-or-jewellery (random among offhands, rings, amulets), `health_potion ×5` as one stack of 5, `mana_potion ×3`, `antidote ×2`, `arrow ×20` twice, 1 scroll (seeded among `item.scroll_*`), 1 trap kit (seeded among `item.*_trap_kit`), then on a unique floor `MakeLegendary` of the `ShopOnly` legendary whose base `WeaponKind == UniqueKindFor(...)`, with `UniqueIndex` its position.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void UniqueKindFor_CoversEveryKindExactlyOnce_PerRun()
{
    foreach (var seed in Enumerable.Range(1, 200).Select(i => (ulong)i * 7919UL))
    {
        var kinds = ShopRules.UniqueFloors.Select(f => ShopRules.UniqueKindFor(seed, f)).ToList();
        Assert.Equal(ShopRules.UniqueKinds.OrderBy(k => k), kinds.OrderBy(k => k));
    }
    Assert.Throws<ArgumentOutOfRangeException>(() => ShopRules.UniqueKindFor(1, 4));
}
[Fact] public void UniqueKindFor_DiffersBetweenSeeds()
{
    var orders = Enumerable.Range(1, 50).Select(i => string.Join(",", ShopRules.UniqueFloors.Select(f => ShopRules.UniqueKindFor((ulong)i * 31UL, f)))).Distinct().Count();
    Assert.True(orders >= 20, $"{orders} distinct orders in 50 seeds");
}
[Fact] public void Roll_IsDeterministic_AndHasTheSpecifiedShape()
{
    var cat = DataFiles.Catalog();
    var a = ShopRules.Roll(cat, 7319, 5); var b = ShopRules.Roll(cat, 7319, 5);
    Assert.Equal(a.Items.Select(i => i.DisplayName + i.StackCount), b.Items.Select(i => i.DisplayName + i.StackCount));
    Assert.Equal(2, a.Items.Count(i => i.Kind == SlotKind.Weapon && i.Rarity != Rarity.Legendary));
    Assert.Single(a.Items, i => i.Kind == SlotKind.Body);
    Assert.Equal(5, a.Items.First(i => i.DefId == "item.health_potion").StackCount);
    Assert.Equal(2, a.Items.Count(i => i.DefId == "item.arrow" && i.StackCount == 20));
    Assert.All(a.Items.Where(i => i.IsEquippable), i => Assert.True(i.Rarity >= Rarity.Magic));
    Assert.All(a.Items.Where(i => i.IsEquippable), i => Assert.Equal(7, i.ItemLevel)); // floor + 2
    Assert.Equal(-1, a.UniqueIndex);
}
[Fact] public void Roll_OnAUniqueFloor_EndsWithTheShopOnlyLegendaryOfTheDealtKind()
{
    var cat = DataFiles.Catalog();
    var s = ShopRules.Roll(cat, 7319, 9);
    var unique = s.Items[s.UniqueIndex];
    Assert.Equal(Rarity.Legendary, unique.Rarity);
    Assert.Equal(ShopRules.UniqueKindFor(7319, 9), unique.BaseWeapon!.Kind);
    Assert.True(cat.Legendaries.Values.Single(l => l.Name == unique.LegendaryName).ShopOnly);
}
[Fact] public void Roll_ChangesWithFloorAndSeed()
{
    var cat = DataFiles.Catalog();
    Assert.NotEqual(Names(ShopRules.Roll(cat, 1, 4)), Names(ShopRules.Roll(cat, 1, 5)));
    Assert.NotEqual(Names(ShopRules.Roll(cat, 1, 4)), Names(ShopRules.Roll(cat, 2, 4)));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopRulesTests"`
Expected: FAIL, `ShopRules` not found.

- [ ] **Step 3: Implement `ShopRules.cs` as specified in Produces**

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopRulesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Items/Core/ShopRules.cs
git commit -m "feat(shop): seeded, depleting stock per floor with a unique weapon of a dealt kind on seven floors"
```

---

### Task 5: `TryBuy` / `TrySell`, the party's shop API and the save field

**Files:**
- Modify: `src/Items/Core/ShopRules.cs`, `src/Party/PartyController.cs`, `src/Save/Core/SaveDtos.cs:148-178`, `src/Save/FloorStateCapture.cs:44-60,94-106`
- Test: `tests/Items/ShopRulesTests.cs` (add)

**Interfaces:**
- Consumes: `Prices.BuyPrice/SellPrice/Sellable` (plan A), `Inventory` (`TryAdd`, `RemoveAt`, `this[int]`, `FreeSlots`).
- Produces:
  `public sealed class Purse { public int Gold; }` (engine-free stand-in for the test; the party passes its own gold in and out), or simpler: pure functions over values:
  `public static string? TryBuy(ShopStock stock, ISet<int> sold, int index, Inventory bag, ref int gold)` → null on success (item added to `bag`, `index` added to `sold`, `gold -= BuyPrice`), else one of `"Sold out."`, `"Not enough gold."`, `"The bag is full."`, `"Nothing there."`.
  `public static string? TrySell(Inventory bag, int bagIndex, ref int gold)` → null on success (item removed, `gold += SellPrice`), else `"Nothing there."` or `"{name} cannot be sold."`.
  `PartyController`: `public HashSet<int> ShopSoldOnFloor { get; }` (current floor's sold indices; loaded from `FloorStateDto.ShopSold`, cleared on floor change), `public ShopStock ShopStockFor(int floor)` (cached per floor from `ShopRules.Roll(Game.Instance.Catalog, Game.Instance.GameSeed, floor)`), `public string? ShopBuy(int stockIndex)` and `public string? ShopSell(BagSlot slot)` (server/solo: call the pure functions with `Gold` via `AddGold`/`TrySpendGold`, log the refusal, raise `EventBus.RaiseBagChanged`-equivalent the bag already uses and return the string; client: send the intent and return null).
  `FloorStateDto.ShopSold : List<int>` (default empty) captured from `ShopSoldOnFloor` and restored into it in `FloorStateCapture` next to `PressureSeconds`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TryBuy_MovesTheItemAndTheGold_AndMarksTheSlotSold()
{
    var cat = DataFiles.Catalog(); var stock = ShopRules.Roll(cat, 7319, 3); var bag = new Inventory(); var sold = new HashSet<int>(); var gold = 10_000;
    var price = Prices.BuyPrice(stock.Items[0]);
    Assert.Null(ShopRules.TryBuy(stock, sold, 0, bag, ref gold));
    Assert.Equal(10_000 - price, gold); Assert.Contains(0, sold); Assert.Same(stock.Items[0], bag.Items.Single());
}
[Fact] public void Buy_SameIndexTwice_RefusesSecondTime()
{ ... Assert.Equal("Sold out.", ShopRules.TryBuy(stock, sold, 0, bag, ref gold)); gold unchanged }
[Fact] public void TryBuy_RefusesWhenPoor_AndWhenBagFull_AndPurseIsUntouched()
{ gold = 0 → "Not enough gold."; a bag filled to Capacity → "The bag is full." with gold unchanged and sold empty }
[Fact] public void TrySell_PaysTheSellPrice_AndRemovesTheItem()
{ bag with a rolled Magic sword at index 0, gold 0 → null, gold == Prices.SellPrice(sword), bag empty }
[Fact] public void TrySell_RefusesKeysAndQuestItems()
{ bag with cat.Create("item.iron_key") → "Iron Key cannot be sold." (use the item's DisplayName), item still in the bag, gold unchanged }
[Fact] public void TryBuy_OutOfRange_IsNothingThere() { index 99 → "Nothing there." }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopRulesTests"`
Expected: FAIL (`TryBuy`/`TrySell` missing).

- [ ] **Step 3: Implement the pure functions, the party API and the save field**

`ShopBuy`/`ShopSell` in `PartyController` follow `BagMove`'s shape (`:686`): guard `IsClient` → `Net.NetSession.Instance.Send(new Net.ShopBuyIntent(stockIndex))` (message defined in Task 7; until then leave a `// Task 7` comment and compile without the send, or do Tasks 5 and 7 in one commit), else mutate and `EventBus.Instance.Log(refusal)`.

- [ ] **Step 4: Run tests and the game build**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopRulesTests"` then `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly`
Expected: PASS; 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Items/Core/ShopRules.cs src/Party/PartyController.cs src/Save/Core/SaveDtos.cs src/Save/FloorStateCapture.cs
git commit -m "feat(shop): buying and selling rules, the party's shop API, sold slots saved per floor"
```

---

### Task 6: The merchant stands in the safe room and talks

**Files:**
- Modify: `src/Adventure/Npc.cs:17-75,110-130`, `src/Dungeon/DungeonBuilder.cs` (next to `PlaceViewerCast`, called from the same place at `:262`), `src/UI/Dialogue/DialogueScreen.cs:67-80`, `src/UI/Hud/Hud.cs:165-171`, `src/Adventure/AdventureDirector.cs`

**Interfaces:**
- Consumes: `FloorLayout.SafeRoom`, `SafeRoomRules.MerchantCell(layout)`, `SafeRoomRules.DoorCell(layout, room)` (plan B); `EventBus.Gathered` (plan B, `event Action<int>`); `CampaignPlan.MerchantCast()`, `CampaignPlan.Merchant ?? MerchantLineBank.Generic(...)` (Task 1); `NpcBodies.ForCast` (Task 2); `VoiceRenderer.Instance.PlayLine(Line, bool interrupt)`.
- Produces:
  `Npc.IsMerchant : bool`, `Npc.MerchantLines : MerchantFloorLines?`, `Npc.GreetRadius : float = 8f`; `Npc.Say(Line line)` (plays the voice if any, logs `"{NpcName}: \"{text}\""`, `Converse(true)` for `TalkPose.EstimateSeconds` then `Converse(null)`); the greeting fires once per floor when the local adventurer first comes within `GreetRadius` (checked in `_PhysicsProcess`, local only, no net).
  `Npc.Prompt` for a merchant: `$"Talk to {NpcName}"`; `Npc.Interact` for a merchant: `hud.OpenMerchant(this)`.
  `DungeonBuilder.PlaceMerchant(FloorLayout layout, Node3D root, int floor)`: returns early when `layout.SafeRoom < 0` or the campaign has no merchant cast (logs `"[DungeonBuilder] floor {floor}: no safe room, no merchant"`); else instantiates `_npcScene`, `Id = "merchant"`, `NpcName = cast.Name`, `IsMerchant = true`, `MerchantLines = (campaign.Merchant ?? MerchantLineBank.Generic(campaign.Title, campaign.Antagonist.Name)).For(floor)`, `ModelPath = BodyPath(NpcBodies.ForCast(cast))`, `Script` = a one-node script whose line is `MerchantLines.Trade` (used only as the dialogue's opening text), position `Surface(SafeRoomRules.MerchantCell(layout)) + Vector3.Up * 0.1f`, facing the door cell, `Name = "Npc_merchant"`.
  `Hud.OpenMerchant(Npc npc)` → `DialogueScreen.OpenMerchant(Npc npc)`: shows `npc.MerchantLines.Trade` as the line and exactly three choices in this order: `"Trade"` (opens `Hud.Shop` for `Game.Instance.CurrentFloor`, closes the dialogue), `"How is it going?"` (shows `Progress` as the line, choices unchanged), `"Leave"` (Farewell). The merchant's voice persona for the lines comes from the cast member like other cast NPCs.
  `AdventureDirector`: subscribes `EventBus.Gathered`; at spawnedTotal 2, 4, 6 the merchant NPC on the floor says `Merchant.Gathering[0], [1], [2]` (via `Npc.Say`), each once per floor.

- [ ] **Step 1: Write the failing headless check (the flow is finished in Task 8; here only the placement step)**

In `DebugTools`, `--test-safe-room` (plan B) already teleports the party to the safe room; add a step `[safe-room] merchant: {present|missing} at {cell}` that asserts an `Npc` with `IsMerchant` exists inside the safe room cells when `layout.SafeRoom >= 0`, and that `--floor=N` for a floor whose layout has `SafeRoom == -1` logs the "no safe room" line without an exception. Run it before implementing and watch it report `missing`.

Run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && timeout 120 godot --headless --path . -- --test-safe-room --no-gathering 2>&1 | grep -E "^\[safe-room\]"`
Expected: `merchant: missing`.

- [ ] **Step 2: Implement the NPC fields, placement, dialogue and gathering lines**

Placement call sits beside `PlaceViewerCast(layout, _floorRoot, floorIndex)` at `DungeonBuilder.cs:262`. Clients also build the merchant (the floor build is shared code) so he exists on every machine; his state needs no `NpcDto` beyond what every NPC gets.

- [ ] **Step 3: Run the check**

Run: same as Step 1.
Expected: `merchant: present at (x,y)`; with `--floor=N` on a no-safe-room floor: the log line and no exception.

- [ ] **Step 4: Commit**

```bash
git add src/Adventure/Npc.cs src/Dungeon/DungeonBuilder.cs src/UI/Dialogue/DialogueScreen.cs src/UI/Hud/Hud.cs src/Adventure/AdventureDirector.cs src/Core/DebugTools.cs
git commit -m "feat(merchant): the merchant stands in the safe room, greets, talks and remarks on the gathering"
```

---

### Task 7: Net messages: `ShopBuyIntent`, `ShopSellIntent`, `ShopSync`

**Files:**
- Modify: `src/Net/Core/Protocol.cs:29-60` (append after `DialogueAnswer`), `src/Net/Core/Messages.cs:60-80`, `src/Net/ServerWorld.cs:361-370`, `src/Net/ClientWorld.cs:76-100`, `src/Party/PartyController.cs` (the `IsClient` branches of `ShopBuy`/`ShopSell`)
- Test: `tests/Net/` codec round-trip (follow the existing message codec tests, if any; otherwise `tests/Net/ShopMessagesTests.cs` encoding and decoding each new record through the JSON codec)

**Interfaces:**
- Produces: `MsgKind.ShopBuyIntent, ShopSellIntent, ShopSync` (appended in that order at the current end of `MsgKind`, after whatever earlier plans added, never reordering existing values, with a comment `// the merchant's shop`); `[Msg(MsgKind.ShopBuyIntent)] record ShopBuyIntent(int StockIndex)`, `[Msg(MsgKind.ShopSellIntent)] record ShopSellIntent(BagSlot Slot)`, `[Msg(MsgKind.ShopSync)] record ShopSync(int Floor, List<int> Sold, int Gold)`.
  Server: `case ShopBuyIntent b: party.ShopBuy(b.StockIndex); SendShopSync(); _bagDirty = true;` and `case ShopSellIntent s: if (!OwnsSlots(slot, s.Slot, s.Slot)) break; party.ShopSell(s.Slot); SendShopSync(); _bagDirty = true;` where `SendShopSync()` broadcasts `new ShopSync(floor, party.ShopSoldOnFloor.ToList(), party.Gold)`; the server also sends `ShopSync` in the floor-change path after `FloorChange` and to a joining player after `Welcome`.
  Client: `case ShopSync ss: party.ApplyShopSync(ss.Floor, ss.Sold, ss.Gold); Trace.Add("shop");` where `PartyController.ApplyShopSync` replaces `ShopSoldOnFloor` (when `Floor == CurrentFloor`), calls `SetGold(ss.Gold)` (plan A) and refreshes an open `ShopScreen`.

- [ ] **Step 1: Write the failing codec test**

```csharp
[Fact] public void ShopMessages_RoundTrip()
{
    foreach (NetMessage m in new NetMessage[] { new ShopBuyIntent(4), new ShopSellIntent(BagSlot.Bag(7)), new ShopSync(5, new List<int> { 1, 3 }, 240) })
        Assert.Equal(Describe(m), Describe(Codec.Decode(Codec.Encode(m))));   // use the project's codec entry points; copy the pattern from any existing message test
}
```

- [ ] **Step 2: Run to verify it fails** — `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~ShopMessages"` → FAIL, types missing.

- [ ] **Step 3: Implement the messages and both ends**

- [ ] **Step 4: Run the test and the game build** — PASS; `dotnet build ... --no-incremental` 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Net/Core/Protocol.cs src/Net/Core/Messages.cs src/Net/ServerWorld.cs src/Net/ClientWorld.cs src/Party/PartyController.cs
git commit -m "feat(net): the shop's buy and sell intents and the sold-slots sync"
```

---

### Task 8: The shop screen, the headless flow and the co-op step

**Files:**
- Create: `src/UI/Shop/ShopScreen.cs`
- Modify: `src/UI/Hud/Hud.cs` (`Shop` property, `ShopOpen`, modal state, `UpdateModalState`), `src/UI/Inventory/ItemSlotControl.cs` (a `ShopSide` flag or a second `Screen`-like owner so a stock cell can be clicked/dragged), `src/Core/DebugTools.cs` (`--test-safe-room` buy/sell steps), `src/Net/NetTestFlow.cs` (a buy and a sell between the chest step and the stairs step), `scripts/net_test.sh` (expect `"shop"` in the client trace), `docs/development.md` (flow line)

**Interfaces:**
- Consumes: `PartyController.ShopStockFor`, `ShopSoldOnFloor`, `ShopBuy`, `ShopSell`, `Gold`; `Prices.BuyPrice/SellPrice/Sellable`; `ItemSlotControl` (`Item`, `Caption`, `Highlight`, drag data via `SlotRef`).
- Produces: `public partial class ShopScreen : PanelContainer { public void Bind(PartyController party); public void Open(int floor); public void Close(); public void Refresh(); }`; left grid: one `ItemSlotControl` per stock item with `Caption = $"{Prices.BuyPrice(item)}g"`, greyed (`Modulate` 0.4) when sold; right grid: the bag's 40 cells with `Caption = Prices.Sellable(item) ? $"{Prices.SellPrice(item)}g" : ""`; a centre label `"{Gold} gold"` bound to `EventBus.GoldChanged`; left click on a stock cell → `party.ShopBuy(index)` (a `ConfirmationDialog` first when `Rarity == Legendary`), left click on a bag cell → `party.ShopSell(BagSlot.Bag(index))`; drag from stock to the bag side buys, from bag to stock side sells; Esc or the Leave button closes. `Hud.Shop`, `Hud.ShopOpen => _shop.Visible`; opening the shop closes the dialogue and other screens like `OpenDialogue` does; `Hud.UpdateModalState` treats the shop as modal (mouse released, movement locked).

- [ ] **Step 1: Write the failing headless steps**

Extend `--test-safe-room` (after the merchant step) with: (a) `party.ShopStockFor(floor)` has the Task 4 shape, logged as `[safe-room] stock: N items, unique={yes|no}`; (b) give the party 10_000 gold, call `party.ShopBuy(0)`, assert bag grew by one, gold fell by the buy price, `ShopSoldOnFloor` contains 0, logged `[safe-room] buy: OK`; (c) `party.ShopBuy(0)` again → refusal logged, nothing changes, `[safe-room] buy twice: refused OK`; (d) `party.ShopSell(BagSlot.Bag(indexOfThatItem))` → gold rises by the sell price, bag shrinks, `[safe-room] sell: OK`; (e) `main.SaveGame("shop")`, `main.LoadGame("shop")`, assert `ShopSoldOnFloor` still contains 0, `[safe-room] sold slots survive a reload: OK`; (f) `hud.Shop.Open(floor)` then `Close()` without exception (headless display server), `[safe-room] shop screen opens: OK`. Final line `[safe-room] RESULT ok` only when every step passed.
  In `NetTestFlow` client side, after the chest step and before `_step = 12`: send `ShopBuyIntent(0)` after the server granted gold (the server-side flow sets `party.AddGold(10_000)` when the client trace shows `"chest"`), wait for `"shop"` in the client trace and the bag to grow; then `ShopSellIntent(BagSlot.Bag(newIndex))`, wait for `"shop"` again and the bag to shrink; log `[net] bought and sold at the merchant`. Server `want` list gains `"shop"`.

Run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && timeout 180 godot --headless --path . -- --test-safe-room --no-gathering 2>&1 | grep -E "^\[safe-room\]"`
Expected before implementing: the stock/buy/sell lines pass already (Task 5), `shop screen opens` fails (no `Hud.Shop`).

- [ ] **Step 2: Implement `ShopScreen`, the HUD wiring and the two test flows**

- [ ] **Step 3: Run the flows**

Run: the headless command above → `[safe-room] RESULT ok`. Then `scripts/net_test.sh` → both `RESULT ok` lines with `shop` in the client trace. Then `cd tests && dotnet test --nologo -v q` → all pass.

- [ ] **Step 4: Document**

`docs/development.md` test table: `godot --headless --path . -- --test-safe-room --no-gathering   # door, rest, gathering (plan B) then the merchant: stock, buy, refuse, sell, reload, shop screen`; the Multiplayer section's `net_test.sh` sentence gains "a purchase and a sale at the merchant".

- [ ] **Step 5: Commit**

```bash
git add src/UI/Shop/ShopScreen.cs src/UI/Hud/Hud.cs src/UI/Inventory/ItemSlotControl.cs src/Core/DebugTools.cs src/Net/NetTestFlow.cs scripts/net_test.sh docs/development.md
git commit -m "feat(shop): the shop screen; headless and co-op proofs of buying and selling at the merchant"
```

---

## Self-review

- **Spec coverage:** §4 merchant identity, flag, validator, body, dialogue with three choices, greeting at 8 m, gathering remarks → Tasks 1, 2, 6. §5 shop screen, stock composition, seeding, depletion, unique floors and kinds, shop-only legendaries, buy/sell rules, refusals logged, Legendary confirmation → Tasks 3, 4, 5, 8. §6 generic bank and offline/AI fallback → Task 1 (bank), Task 2 (repair adds it); the planner prompt wording and the shipped lines are plan E's by contract. §7 save (`ShopSold`), net (three messages, syncs on floor change and join), tests (engine-free list and both flows) → Tasks 5, 7, 8. Calls list items 3 (seven slots, seven kinds, shop-only) → Tasks 3, 4; item 1 (Crane untouched, merchant separate) → Task 2's body rule and validator; nothing in §1-3 is this plan's.
- **Step scan:** each code step names the file, the signature and the fixed values; bodies are left to the implementer except the three refusal strings and the seed mixing constants, which the tests pin.
- **Type consistency:** `ItemCatalog`, `LegendaryProfile`, `ShopStock`, `MerchantFloorLines`, `HashSet<int> ShopSoldOnFloor`, `BagSlot` are used with the same names in Tasks 4-8; `Gathered` is plan B's `event Action<int>`.
- **Review Focus:** all five lines name their task and test.
- **Proportion:** eight tasks for a spec section of about two pages; code blocks are tests and signatures only.
