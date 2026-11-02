# Gold and Prices Implementation Plan (plan A of the tavern project)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the party a shared gold purse that coins from chests, enemies and bosses flow into, with engine-free prices for every item, saved, synced to co-op clients and shown in the inventory.

**Architecture:** Two engine-free rule classes (`Prices`, `CoinRules`) under `src/Items/Core` carry every number; `PartyController` owns the purse and raises `EventBus.GoldChanged`; a `LootDrop` with `Coins > 0` is a coin pile that walks into the purse on pickup; the save gains `PartyDto.Gold` with a v10 migration; the server mirrors the purse with `GoldSync` the way it mirrors the bag.

**Tech Stack:** Godot 4.7.2 .NET / C#, xUnit tests in `tests/` (linked source: `../src/Items/Core/**/*.cs`, `../src/Save/Core/**/*.cs`, `../src/Net/Core/**/*.cs` are already compiled into the test assembly, so nothing in those folders may reference Godot).

**Spec:** `docs/superpowers/specs/2026-11-02-tavern-merchant-safe-rooms-design.md` section 2 ("Money"). **Contract:** `docs/superpowers/plans/2026-11-02-tavern-shared-interfaces.md` section A: every name, signature and number below is copied from it; later plans consume them verbatim.

## Global Constraints

- Build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before any headless run (the shared NTFS folder has clock skew that defeats incremental builds).
- Engine-free code (`src/Items/Core`, `src/Save/Core`, `src/Net/Core`) never references the Godot namespace.
- Co-op is server-authoritative: the purse changes only on the server/solo; clients receive `GoldSync`; every purse mutation is guarded by `Net.NetSession.IsClient`.
- Save format: `SaveFormat.CurrentVersion` goes 9 → 10 with a migration case in `SaveMigrations.Migrate`; old saves load with an empty purse.
- The coin is called "gold" everywhere in text; the log line is `+N gold` / `-N gold`.
- Numbers (verbatim from the contract): seed money is a later plan's; chest coins uniform in [10 + 6·floor, 25 + 10·floor]; enemy coins 0 with probability 3/4 else uniform in [2 + floor, 6 + 2·floor]; boss coins 150·tier; value = floor(base × rarity × (1 + 0.15 × ItemLevel)) with base Weapon 20, Body 18, Head/Hands/Feet 10, Offhand 12, Ring/Amulet 15, Consumable 4, Ammo floor(0.3 × StackCount); rarity Common 1, Magic 2.2, Rare 4.5, Legendary 12; buy = floor(2.5 × value); sell = value.
- Tests live in `tests/` on the development machine only (never `git add tests`).

## Review Focus

1. A coin pile the party cannot carry: there is no bag slot involved, so `LootDrop.Interact` with `Coins > 0` must never log "The bag is full" and must always free itself (Task 4 test `CoinPile_IsPickedUpWithAFullBag` in `--test-gold`).
2. A negative purse: `AddGold(-30)` on a purse of 20 must clamp to 0, not go negative, and `TrySpendGold` must refuse rather than overdraw (Task 3 test `AddGold_NeverGoesBelowZero`).
3. An old save: a v9 file with no `Gold` field must load with `Gold == 0` and save back as v10 (Task 3 test `V9_WithoutGold_MigratesToZeroPurse`).
4. A reconnecting client: a client that joins after the purse already holds gold must see the right number from `Welcome.Party.Gold` without waiting for a change (Task 5 net flow check).
5. Stacks of arrows: `Prices.Value` on a stack of 7 arrows is 2, of 10 is 3, of 1 is 0, and a 0-value item is still `Sellable` (Task 1 test `Arrows_AreWorthThreePerTen`).

---

### Task 1: Prices

**Files:**
- Create: `src/Items/Core/Prices.cs`
- Test: `tests/Items/PricesTests.cs`

**Interfaces:**
- Consumes: `ItemInstance` (`Kind`, `Rarity`, `ItemLevel`, `StackCount`, `DefId`), `SlotKind`, `Rarity` from `src/Items/ItemInstance.cs` and `src/Items/Core/ItemEnums.cs`.
- Produces: `public static class Prices` with `int Value(ItemInstance item)`, `int SellPrice(ItemInstance item)`, `int BuyPrice(ItemInstance item)`, `bool Sellable(ItemInstance item)`.

- [ ] **Step 1: Write the failing tests** (`tests/Items/PricesTests.cs`, namespace `DungeonCrawler.Tests.Items`, build items with `ItemInstance.Simple(...)` and set `Rarity`/`ItemLevel`/`StackCount`):

```csharp
[Fact] public void Value_IsBaseTimesRarityTimesLevelFactor_FlooredDown()
{   // Weapon base 20, Rare 4.5, level 4 -> 20 * 4.5 * 1.6 = 144
    var sword = ItemInstance.Simple("item.longsword", "Longsword", SlotKind.Weapon); sword.Rarity = Rarity.Rare; sword.ItemLevel = 4;
    Assert.Equal(144, Prices.Value(sword));
    var robe = ItemInstance.Simple("item.robe", "Robe", SlotKind.Body); robe.Rarity = Rarity.Magic; robe.ItemLevel = 3; // 18 * 2.2 * 1.45 = 57.42 -> 57
    Assert.Equal(57, Prices.Value(robe));
}
[Fact] public void BaseByKind_MatchesTheContract()   // level 1 Common: Weapon 23, Body 20, Head/Hands/Feet 11, Offhand 13, Ring/Amulet 17, Consumable 4
{   // 20*1.15=23, 18*1.15=20.7->20, 10*1.15=11.5->11, 12*1.15=13.8->13, 15*1.15=17.25->17, 4*1.15=4.6->4
    int V(SlotKind k) => Prices.Value(ItemInstance.Simple("x", "x", k));
    Assert.Equal(23, V(SlotKind.Weapon)); Assert.Equal(20, V(SlotKind.Body)); Assert.Equal(11, V(SlotKind.Head)); Assert.Equal(11, V(SlotKind.Hands));
    Assert.Equal(11, V(SlotKind.Feet)); Assert.Equal(13, V(SlotKind.Offhand)); Assert.Equal(17, V(SlotKind.Ring)); Assert.Equal(17, V(SlotKind.Amulet)); Assert.Equal(4, V(SlotKind.Consumable));
    Assert.Equal(0, V(SlotKind.Junk));
}
[Fact] public void Arrows_AreWorthThreePerTen()   // Review Focus 5
{
    ItemInstance Arrows(int n) => ItemInstance.Simple("item.arrow", "Arrow", SlotKind.Ammo, stackable: true, count: n);
    Assert.Equal(2, Prices.Value(Arrows(7))); Assert.Equal(3, Prices.Value(Arrows(10))); Assert.Equal(0, Prices.Value(Arrows(1)));
    Assert.True(Prices.Sellable(Arrows(1)));
}
[Fact] public void Legendary_IsTwelveTimesCommon() { /* a Legendary level-1 weapon: floor(20*12*1.15) = 276 */ }
[Fact] public void BuyIsTwoAndAHalfTimesValue_SellIsValue() { /* Rare level-4 sword: Sell 144, Buy floor(360) = 360; Magic level-3 robe: Sell 57, Buy floor(142.5) = 142 */ }
[Fact] public void KeysAndQuestRelics_AreNotSellable_TheTomeIs()
{
    Assert.False(Prices.Sellable(ItemInstance.Simple("item.iron_key", "Iron Key")));
    Assert.False(Prices.Sellable(ItemInstance.Simple("item.quest_relic", "Crane's basket")));
    Assert.True(Prices.Sellable(ItemInstance.Simple("item.tome_of_unlearning", "Tome of Unlearning", SlotKind.Consumable)));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~PricesTests"`
Expected: build error `'Prices' does not exist`.

- [ ] **Step 3: Implement `Prices` in `src/Items/Core/Prices.cs`** (namespace `DungeonCrawler.Items`): `Value` = `(int)Math.Floor(Base(item) * RarityFactor(item.Rarity) * (1 + 0.15 * item.ItemLevel))` with `Base` a switch on `item.Kind` (Ammo returns `Math.Floor(0.3 * item.StackCount)` directly, before the rarity/level factor is applied: an arrow stack's value is `floor(0.3 × n)` and nothing else; Junk 0); `SellPrice => Value`; `BuyPrice => (int)Math.Floor(2.5 * Value(item))`; `Sellable` false when `item.DefId` is `"item.iron_key"` or `"item.quest_relic"` (the id `DungeonBuilder.cs:1196` gives Recover-quest items).

- [ ] **Step 4: Run to verify they pass**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~PricesTests"`
Expected: `Passed! - Failed: 0, Passed: 6`.

- [ ] **Step 5: Commit**

```bash
git add src/Items/Core/Prices.cs
git commit -m "feat(items): engine-free prices: value by kind, rarity and level; buy at 2.5x; keys and quest relics unsellable"
```

### Task 2: CoinRules

**Files:**
- Create: `src/Items/Core/CoinRules.cs`
- Test: `tests/Items/CoinRulesTests.cs`

**Interfaces:**
- Produces: `public static class CoinRules` with `int ChestCoins(int floor, Random rng)`, `int EnemyCoins(int floor, Random rng)`, `int BossCoins(int tier)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ChestCoins_StayInsideTheFloorRange_AndReachBothEnds()
{   // floor 3: [28, 55]; floor 12: [82, 145]
    var rng = new Random(1); var seen = new HashSet<int>();
    for (var i = 0; i < 2000; i++) { var c = CoinRules.ChestCoins(3, rng); Assert.InRange(c, 28, 55); seen.Add(c); }
    Assert.Contains(28, seen); Assert.Contains(55, seen);
    for (var i = 0; i < 200; i++) Assert.InRange(CoinRules.ChestCoins(12, rng), 82, 145);
}
[Fact] public void EnemyCoins_AreZeroThreeTimesInFour_OtherwiseInRange()
{   // floor 5: [7, 16]
    var rng = new Random(2); int zeros = 0, n = 4000;
    for (var i = 0; i < n; i++) { var c = CoinRules.EnemyCoins(5, rng); if (c == 0) zeros++; else Assert.InRange(c, 7, 16); }
    Assert.InRange(zeros / (double)n, 0.72, 0.78);
}
[Fact] public void BossCoins_Are150PerTier() { Assert.Equal(150, CoinRules.BossCoins(1)); Assert.Equal(600, CoinRules.BossCoins(4)); }
[Fact] public void SameSeed_SameCoins() { Assert.Equal(CoinRules.ChestCoins(7, new Random(9)), CoinRules.ChestCoins(7, new Random(9))); }
```

- [ ] **Step 2: Run to verify they fail** — Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~CoinRulesTests"`; Expected: build error `'CoinRules' does not exist`.

- [ ] **Step 3: Implement `CoinRules` in `src/Items/Core/CoinRules.cs`** (namespace `DungeonCrawler.Items`): inclusive uniform ranges via `rng.Next(min, max + 1)`; `EnemyCoins` draws `rng.NextDouble() < 0.25` first and returns 0 otherwise.

- [ ] **Step 4: Run to verify they pass** — Expected: `Passed: 4`.

- [ ] **Step 5: Commit** — `git add src/Items/Core/CoinRules.cs && git commit -m "feat(items): coin rules for chests, enemies and bosses"`

### Task 3: The purse, its event, its save field and migration

**Files:**
- Modify: `src/Party/PartyController.cs` (fields near `Bag` at line 40; `CaptureState` / `RestoreState` at lines 760-780)
- Modify: `src/Core/EventBus.cs` (events list around line 74; raisers around line 134)
- Modify: `src/Save/Core/SaveDtos.cs:59-70` (`PartyDto`)
- Modify: `src/Save/Core/SaveFormat.cs` (`CurrentVersion`, `SaveMigrations.Migrate`, new `From9To10`)
- Modify: `src/Core/DebugTools.cs:3157` (`Snapshot()` for `--test-save`)
- Test: `tests/Save/SaveFormatTests.cs` (new facts), `tests/Party/PurseRulesTests.cs` only if the clamp is extracted (see Step 3)

**Interfaces:**
- Produces: `PartyController.Gold { get; private set; }`, `void AddGold(int amount)`, `bool TrySpendGold(int amount)`, `void SetGold(int gold)` (client mirror; no event log, raises `GoldChanged` with delta 0 when unchanged is fine); `EventBus.GoldChanged` (`Action<int, int>`: total, delta) and `RaiseGoldChanged(int total, int delta)`; `PartyDto.Gold` (int, default 0); `SaveFormat.CurrentVersion == 10`.

- [ ] **Step 1: Write the failing save tests** in `tests/Save/SaveFormatTests.cs` (same style as `SaveV7Tests`):

```csharp
[Fact] public void V9_WithoutGold_MigratesToZeroPurse()   // Review Focus 3
{
    var json = "{\"Version\":9,\"GameSeed\":5,\"CurrentFloor\":2,\"Party\":{},\"Floors\":{}}";
    var data = SaveFormat.Deserialize(json);
    Assert.Equal(10, SaveFormat.CurrentVersion); Assert.Equal(SaveFormat.CurrentVersion, data.Version);
    Assert.Equal(0, data.Party.Gold);
}
[Fact] public void Gold_RoundTrips()
{
    var data = new SaveData { Party = new PartyDto { Gold = 237 } };
    Assert.Equal(237, SaveFormat.Deserialize(SaveFormat.Serialize(data)).Party.Gold);
}
```

- [ ] **Step 2: Run to verify they fail** — Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~SaveFormatTests"`; Expected: build error `'PartyDto' does not contain a definition for 'Gold'`.

- [ ] **Step 3: Implement the save side**: `PartyDto.Gold { get; set; }`; `CurrentVersion = 10`; `9 => From9To10(root)` whose body sets `root["Party"]["Gold"] ??= 0` when `Party` is an object (mirror `From1To2`). Then the purse on `PartyController`: `Gold` property; `AddGold(int amount)`: `Gold = Math.Max(0, Gold + amount)`, raise `GoldChanged(Gold, actualDelta)`, `EventBus.Instance.Log(actualDelta >= 0 ? $"+{actualDelta} gold" : $"{actualDelta} gold")`, `PlaySfx("coins")` on a positive delta; `TrySpendGold(int amount)`: false when `amount > Gold`, else `AddGold(-amount)` and true; `SetGold(int gold)` sets and raises without a log line. `CaptureState` writes `Gold = Gold`; `RestoreState` reads `Gold = dto.Gold` (before `RefreshControl`). `Snapshot()` in `DebugTools` appends ` gold={party.Gold}` so `--test-save` compares it.

- [ ] **Step 4: Run the save tests and the full suite** — Run: `cd tests && dotnet test --nologo -v q`; Expected: `Failed: 0`, total = previous total + 2 (+4 from Task 2, +6 from Task 1).

- [ ] **Step 5: Headless save round trip** — Run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && godot --headless --path . -- --test-save 2>&1 | grep "\[save\] RESULT"`; Expected: `RESULT match` (the snapshot now carries `gold=0` on both sides; Task 4 makes it non-zero).

- [ ] **Step 6: Commit**

```bash
git add src/Party/PartyController.cs src/Core/EventBus.cs src/Save/Core/SaveDtos.cs src/Save/Core/SaveFormat.cs src/Core/DebugTools.cs
git commit -m "feat(party): a shared gold purse with its event, saved as v10 (old saves load with none)"
```

### Task 4: Coin piles on the floor: chests, enemies, bosses, pickup by players and companions

**Files:**
- Modify: `src/Items/LootDrop.cs` (`Coins` property, `Prompt`, `_Ready` visual, `Interact`)
- Modify: `src/Party/PartyController.cs:667-676` (`SpawnLoot`; add `SpawnCoins`)
- Modify: `src/Party/Adventurer.cs:716-736` (`ScanLoot`)
- Modify: `src/Dungeon/Props/Chest.cs:64-72` (coin drop beside the items)
- Modify: `src/Enemies/Enemy.cs:789-805` (death loot)
- Modify: `src/Core/DebugTools.cs` (new flow `--test-gold`), `docs/development.md:228-246` (one line in the headless checks list)

**Interfaces:**
- Consumes: `CoinRules` (Task 2), `PartyController.AddGold` (Task 3), `TierRules.TierIndex(floor)` (`src/Dungeon/TierRules.cs:11`, 0-based: boss tier = `TierIndex(floor) + 1`).
- Produces: `LootDrop.Coins` (int, 0 for an item drop); `PartyController.SpawnCoins(int coins, Vector3 at)` returning the `LootDrop?`; the headless flow `--test-gold`.

- [ ] **Step 1: Write the failing headless flow** `RunGoldTest()` in `DebugTools` (flag `--test-gold` sets `_testGold = true; _quitAfter = 14;`, dispatched beside `_testParty` at line 726; steps on `_elapsed` like `RunPartyTest`):
  - step 0 (1.0 s): record `party.Gold` (expect 0); Review Focus 2: `party.AddGold(-10)` must leave `Gold == 0` and `party.TrySpendGold(5)` must return false, printed `[gold] clamp: OK/FAIL`; then open the nearest unlocked `Chest` via `chest.Interact(party, party.Lead)`; count `LootDrop` nodes with `Coins > 0` in the floor root → print `[gold] chest coin piles=N` and the pile's `Coins`, expect N == 1 and `Coins` within `CoinRules` range for `Game.Instance.CurrentFloor`;
  - step 1 (2.0 s): fill the bag completely (`party.Bag.TryAdd(cat.Create("item.bone_shard"))` until `FreeSlots == 0`), then `drop.Interact(party, party.Lead)` on the coin pile → expect `party.Gold == before + pile.Coins` and the pile `IsQueuedForDeletion()` (Review Focus 1, printed as `[gold] full bag pickup: OK/FAIL`);
  - step 2 (3.0 s): spawn a coin pile 1.0 m from a companion (`party.SpawnCoins(25, companion.GlobalPosition + companion.GlobalTransform.Basis.Z * -1f)`) and record `Gold`;
  - step 3 (5.0 s): expect `Gold` grew by 25 → `[gold] companion pickup: OK/FAIL`;
  - step 4 (5.5 s): kill the nearest enemy 40 times? No: spawn and kill 40 regular enemies in a loop (`main.Builder.SpawnEnemy(...)` then `Kill()`), count coin piles → expect between 3 and 20 piles (3/4 drop nothing) → `[gold] enemy coins: piles=N OK/FAIL`; then `--test-save`-style `Snapshot` is not needed; print `[gold] RESULT ok` or `FAIL (n)` and set `_quitAfter = _elapsed + 0.5`.

- [ ] **Step 2: Run to verify it fails** — Run: `dotnet build ... && godot --headless --path . -- --test-gold --no-ambush --no-portals 2>&1 | grep "^\[gold\]"`; Expected: build error (`Coins`, `SpawnCoins` missing).

- [ ] **Step 3: Implement**: `LootDrop.Coins { get; set; }`; in `_Ready`, when `Coins > 0` skip `ItemVisuals.Attach` and give the `Mesh` a small flattened gold-coloured cylinder (`CylinderMesh` radius 0.18, height 0.08) with an emissive gold material, no halo; `Prompt => Coins > 0 ? $"Pick up {Coins} gold" : …`; `Interact`: when `Coins > 0` → `party.AddGold(Coins)`, `RaiseLootRemoved(Item.Id, actor.Index)`, `QueueFree()`, return (never touches the bag). `PartyController.SpawnCoins(int coins, Vector3 at)`: like `SpawnLoot` but sets `drop.Coins = coins` and leaves `Item` at its default (its `Id` is the drop's identity for `LootRemoved`); returns null for `coins <= 0`. `Adventurer.ScanLoot`: a `LootDrop { Coins: > 0, DroppedByParty: false }` within 1.6 m is picked up regardless of `FreeSlots` (`Party.AddGold(drop.Coins)`, log `"{State.Name} picks up {drop.Coins} gold."`, `RaiseLootRemoved`, `QueueFree`), checked before the item branch. `Chest.Interact`: after the items, `party.SpawnCoins(CoinRules.ChestCoins(Game.Instance.CurrentFloor, rng), GlobalPosition + toward * 1.0f + Vector3.Up * 0.4f)` using the same seeded `rng` so peers and reloads agree. `Enemy` death (after the bone-shard line, before the table): `var coins = IsBoss ? CoinRules.BossCoins(TierRules.TierIndex(Floor) + 1) : CoinRules.EnemyCoins(Floor, _rng); if (coins > 0) Party?.SpawnCoins(coins, GlobalPosition + Vector3.Up * 0.4f + new Vector3(0.3f, 0, -0.5f));` (hordes and reinforcements keep their early returns, so they drop no coins).

- [ ] **Step 4: Run to verify it passes** — Run the Step 2 command; Expected: `[gold] RESULT ok`. Then `godot --headless --path . -- --test-save 2>&1 | grep RESULT` → `RESULT match` (the chest in that flow now spills coins; `Snapshot` includes `gold=`).

- [ ] **Step 5: Document** — add to `docs/development.md` headless checks: `godot --headless --path . -- --test-gold --no-ambush --no-portals   # chest spills a coin pile, a full bag still picks it up, a companion picks coins up, enemies drop coins one time in four; prints RESULT` and one sentence in the loot paragraph near line 340 ("pick up loot they pass") that companions pick up coin piles too.

- [ ] **Step 6: Commit**

```bash
git add src/Items/LootDrop.cs src/Party/PartyController.cs src/Party/Adventurer.cs src/Dungeon/Props/Chest.cs src/Enemies/Enemy.cs src/Core/DebugTools.cs docs/development.md
git commit -m "feat(loot): coin piles from chests, enemies and bosses walk into the party purse; --test-gold proves it"
```

### Task 5: Co-op: `GoldSync`, coin piles on clients, `Welcome` carrying the purse

**Files:**
- Modify: `src/Net/Core/Protocol.cs:29-52` (`MsgKind.GoldSync` appended at the current end of `MsgKind` (after whatever earlier plans added; never reorder existing values))
- Modify: `src/Net/Core/Messages.cs:66,85` (`GoldSync` record; `LootSpawned` gains `int Coins = 0`)
- Modify: `src/Net/ServerWorld.cs:53,557-571` (`LootSpawned` broadcast carries `drop.Coins`; `FlushSync` sends `GoldSync` when the purse differs from the last sent value)
- Modify: `src/Net/ClientWorld.cs:76,165-173` (`case GoldSync` → `Main.Instance.Party.SetGold`; `OnLootSpawned` sets `drop.Coins`)
- Modify: `src/Net/NetTestFlow.cs:162-175` (chest step asserts the client's purse rose)
- Test: `tests/Net/` codec round-trip for `GoldSync` and `LootSpawned` with coins (follow the existing message codec tests in that folder)

**Interfaces:**
- Consumes: `PartyController.Gold`/`SetGold` (Task 3), `LootDrop.Coins` (Task 4).
- Produces: `[Msg(MsgKind.GoldSync)] public sealed record GoldSync(int Gold) : NetMessage`; `LootSpawned(ItemDto Item, NetVec Position, int KeyIndex, string? QuestKey, int Coins = 0)`.

- [ ] **Step 1: Write the failing codec tests** in `tests/Net/` (same shape as the existing round-trip tests there): `GoldSync_RoundTrips` (encode `new GoldSync(412)`, decode, `Assert.Equal(412, ...)`) and `LootSpawned_CarriesCoins` (`Coins = 33` survives; a message without the field decodes with `Coins == 0`).

- [ ] **Step 2: Run to verify they fail** — Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~Net"`; Expected: build error `'GoldSync' could not be found`.

- [ ] **Step 3: Implement** the enum member, the record, the `LootSpawned` parameter, the server broadcast (`drop.Coins`), `FlushSync`: keep `_goldSent` (int, start -1) and `Broadcast(new GoldSync(party.Gold))` when `party.Gold != _goldSent`; `ClientWorld`: `case GoldSync gs: Main.Instance.Party.SetGold(gs.Gold); Trace.Add("gold"); break;` and `drop.Coins = ls.Coins` in `OnLootSpawned`. `Welcome` needs no change: `Welcome.Party` is `main.Party.CaptureState()` (`ServerWorld.cs:184`) and the client restores it through `RestoreState`, which Task 3 taught to read `Gold` (find the client's `RestoreState(` call under `src/Net` or `src/Core/Main.cs`; verify it passes the welcome's `Party` and that `SetGold`/`Gold` lands before the HUD refreshes).

- [ ] **Step 4: Run the codec tests** — Expected: `Failed: 0`.

- [ ] **Step 5: Extend the net flow**: in `NetTestFlow.cs` after the chest step confirms the pickup (`_step = 6` region around line 156), record `main.Party.Gold` before the chest opens and require `client.Trace.Contains("gold") && main.Party.Gold > before` within 8 s, printing `[net] gold synced: N`; Review Focus 4: in the rejoin step (line ~282) assert the rejoined client's `Gold` equals the value printed before leaving. Run: `scripts/net_test.sh` (see `docs/development.md`, `SERVER_BIN` unset runs from source); Expected: both `RESULT ok` lines, with the new `[net] gold synced` line.

- [ ] **Step 6: Commit**

```bash
git add src/Net/Core/Protocol.cs src/Net/Core/Messages.cs src/Net/ServerWorld.cs src/Net/ClientWorld.cs src/Net/NetTestFlow.cs
git commit -m "feat(net): the purse reaches clients (GoldSync, Welcome) and coin piles spawn on clients"
```

### Task 6: The inventory shows the purse

**Files:**
- Modify: `src/UI/Inventory/InventoryScreen.cs:109-136` (`Refresh`: `_info.Text` gains a first line `{_party.Gold} gold`) and the constructor (subscribe `EventBus.Instance.GoldChanged += (_, _) => { if (Visible) Refresh(); }`)
- Modify: `docs/development.md` (the Inventory paragraph under Controls, one clause: the purse shows at the top of the info panel)

- [ ] **Step 1: Implement** the label text and the subscription (unsubscribe in `_ExitTree` the way other screens do if they do; otherwise match the file's existing pattern).

- [ ] **Step 2: Verify by screenshot** — Run: `dotnet build ... && godot --path . -- --test-gold --screenshot=/tmp/claude-1000/-mnt-Transfer-5FD1E1415AC80F29-dev-DungeonCrawler/28c37315-82e7-4676-b482-84d83f3fa37d/scratchpad/gold.png --after=6 --quit-after=8` with the inventory opened by the flow at step 3 (`main.Hud.ToggleInventory()` or the equivalent the HUD exposes; check `Hud.cs:33` `InventoryOpen`), then view the PNG: the info panel's first line reads `N gold` with N > 0. (Needs a window; on the headless server it is skipped and the `--test-gold` RESULT stands.)

- [ ] **Step 3: Commit** — `git add src/UI/Inventory/InventoryScreen.cs docs/development.md && git commit -m "feat(ui): the inventory shows the party's gold"`

---

## Self-review

- **Spec coverage (section 2):** shared whole-coin purse (T3); shown in the inventory (T6) and as a log line (T3); coin ranges for chests, enemies and bosses (T2, T4); coins walk into the purse without a bag slot (T4); companions pick coins up (T4); selling/buying formula with the exact bases, rarities and the 2.5 factor (T1); keys and quest items unsellable, the Tome sells (T1); engine-free and unit tested (T1, T2); save and co-op (T3, T5). Seed money and the shop itself belong to plans D and C.
- **Step scan:** every code step names the file, the member and its rule; test steps carry the exact numbers (23/20/11/13/17/4; 144; 57; 276; 360/142; ranges [28,55], [82,145], [7,16]; 150/600).
- **Type consistency:** `Gold`, `AddGold`, `TrySpendGold`, `SetGold`, `GoldChanged(int total, int delta)`, `LootDrop.Coins`, `SpawnCoins`, `GoldSync(int Gold)`, `PartyDto.Gold`, `CurrentVersion 10` match the contract and are used with the same names in every task.
- **Review Focus:** 1 → T4 step 1 (full bag pickup); 2 → T4 step 1 (the clamp check; `PartyController` is a Node, so the rule is proven headless rather than in xUnit); 3 → T3 test; 4 → T5 step 5; 5 → T1 test.
- **Proportion:** six tasks, about a page each of decisions, no function bodies beyond rules the tests do not already pin.

## Deviations from the contract

None. One clarification the contract left open and this plan settles: quest items are recognised by their id `item.quest_relic` (the only id Recover-quest drops use, `DungeonBuilder.cs:1196`); if a later plan introduces other quest items, `Prices.Sellable` is the one place to extend.
