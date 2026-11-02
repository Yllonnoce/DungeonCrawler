# Tavern, merchant and safe rooms: shared interfaces for the plans

Spec: `docs/superpowers/specs/2026-11-02-tavern-merchant-safe-rooms-design.md`. Six plans implement it; this file is
the contract between them, so a later plan consumes exactly what an earlier one produces. Order of execution:

| # | Plan | Depends on |
|---|---|---|
| A | `2026-11-02-gold-and-prices.md` | nothing |
| B | `2026-11-02-safe-room.md` | nothing (A for nothing but the HUD line placement) |
| C | `2026-11-02-merchant-and-shop.md` | A (gold), B (safe room cells) |
| D | `2026-11-02-tavern-floor-zero.md` | A (gold), C (merchant NPC, briefing script) |
| E | `2026-11-02-merchant-lines-and-voice.md` | C (the `Merchant` campaign section) |
| F | `2026-11-02-hit-effects.md` | nothing (independent; owner's requests from the same day: blood, arrows that stick, spell impacts, enemy grunts on hit and death) |

Conventions every plan inherits: Godot 4.7.2 .NET / C#; engine-free rules live in `*/Core` namespaces and are tested
with xUnit in `tests/` (dev machine only; link new source files in `tests/DungeonCrawler.Tests.csproj` the way the
existing Core folders are linked); Godot-side behaviour is proven by a headless `--test-*` flow in `src/Core/DebugTools.cs`
documented in `docs/development.md`; co-op is server-authoritative (intents in, syncs out, `Net.NetSession.IsClient`
guards); saves bump `SaveFormat.CurrentVersion` (9 today) with a migration; build with
`dotnet build DungeonCrawler.csproj --no-incremental` before any headless run (the shared NTFS folder has clock skew).

## A. Gold and prices (`src/Items/Core/Prices.cs`, `src/Items/Core/CoinRules.cs`, `PartyController`)

- `public static class Prices` (engine-free):
  `int Value(ItemInstance item)` = floor(base × rarity × (1 + 0.15 × item.ItemLevel)); base by `item.Kind`: Weapon 20,
  Body 18, Head/Hands/Feet 10, Offhand 12, Ring/Amulet 15, Consumable 4, arrows 3 per 10 (a stack of n arrows is
  worth floor(0.3 × n)); rarity Common 1, Magic 2.2, Rare 4.5, Legendary 12.
  `int SellPrice(ItemInstance item) => Value(item)`; `int BuyPrice(ItemInstance item) => (int)Math.Floor(2.5 * Value(item))`;
  `bool Sellable(ItemInstance item)`: false for `item.DefId == "item.iron_key"` and for quest items (whatever marks them
  today; the plan names it), true otherwise (the Tome of Unlearning sells).
- `public static class CoinRules` (engine-free): `int ChestCoins(int floor, Random rng)` uniform in
  [10 + 6 × floor, 25 + 10 × floor]; `int EnemyCoins(int floor, Random rng)` = 0 with probability 3/4, else uniform in
  [2 + floor, 6 + 2 × floor]; `int BossCoins(int tier) => 150 * tier`.
- `PartyController`: `public int Gold { get; private set; }`, `public void AddGold(int amount)` (negative allowed;
  raises `EventBus.RaiseGoldChanged(Gold, amount)` and logs `"+14 gold"` / `"-30 gold"`), `public bool TrySpendGold(int amount)`.
- `EventBus`: `event Action<int, int>? GoldChanged` (total, delta), `RaiseGoldChanged(int total, int delta)`.
- Coins on the floor: `LootDrop.Coins` (int, 0 for an item drop); a coin drop has no `ItemInstance`; picking it up calls
  `PartyController.AddGold`. Chests add a coin drop beside their items; `Enemy` death rolls `EnemyCoins`
  (boss: `BossCoins`); companions pick coins up like loot.
- Save: `PartyDto.Gold` (int, default 0); `SaveFormat.CurrentVersion` 9 → 10, migration sets 0.
- Net: `[Msg(MsgKind.GoldSync)] record GoldSync(int Gold)`; `Welcome.Party` (a `PartyDto`) carries `Gold`; the server
  sends `GoldSync` after every change; `ClientWorld` sets the purse from it (client-side `PartyController.SetGold(int)`).
- HUD: the inventory screen shows `"{Gold} gold"` beside the bag; the message log carries the `+N gold` line.

## B. Safe room (`src/Adventure/Core/SafeRoomRules.cs`, `src/Adventure/SafeRoomController.cs`, generation, `Door`)

- `FloorLayout.SafeRoom` (int, room index, -1 when none) and `FloorLayout.SafeRoomDoor` (GridPos);
  `bool FloorLayout.IsInSafeRoom(GridPos p)`.
- `VaultKind.SafeDoor` added to the enum; the safe room is appended to `layout.Vaults` as
  `new Vault(index, room, door, VaultKind.SafeDoor, Lever: null, Key: null, Revealed: true)` so the existing vault/door
  placement builds its `Door`; `Door` with `Kind == SafeDoor` has `Prompt == ""`, opens itself (`Open()`) when a living
  party member is within `SafeRoomRules.DoorOpenRadius` and closes (`Close()`) `DoorCloseDelay` seconds after the last
  member is farther than `DoorCloseRadius`; enemies never open it.
- `public static class SafeRoomRules` (engine-free): `const float GraceSeconds = 20, IntervalSeconds = 40,
  EmptyResetSeconds = 30, RegenMultiplier = 6, DoorOpenRadius = 2.5, DoorCloseRadius = 3, DoorCloseDelay = 2;
  const int CapAlive = 8, RouteDistanceCells = 10;`
  `int Choose(FloorLayout layout)` → room index or -1 (dead-end room, not UpStairs/DownStairs room, not a vault, not a
  nest, door cell within `RouteDistanceCells` of the shortest route between `UpStairs` and `DownStairs`, nearest the
  route's midpoint wins); `GridPos DoorCell(FloorLayout layout, int room)`; `GridPos MerchantCell(FloorLayout layout)`
  (the room cell farthest from the door); `IEnumerable<GridPos> Cells(FloorLayout layout)`.
- `public sealed class GatheringClock` (engine-free): `void Update(float dt, bool occupied, int aliveFromGathering)`;
  `bool TakeDue()` (true once per interval after grace while occupied and alive < cap); `int Spawned { get; }`;
  `float Seconds { get; }`; resets to grace after `EmptyResetSeconds` unoccupied.
- `SafeRoomController : Node` (server/solo, one per floor with a safe room): owns the door rule, `bool Occupied`
  (any living party member inside), sets `PartyController.RegenMultiplier = RegenMultiplier` while occupied (1 otherwise;
  `CharacterState.Tick` multiplies HP, stamina and mana regen by it), runs the `GatheringClock`, spawns a group of 1 or 2
  floor enemies at the walkable corridor cell nearest the door that is not visible from it, awake and holding position
  (`Enemy.HoldAt(GridPos)`), raises `EventBus.RaiseGathered(int spawnedTotal)` at each spawn, `EventBus.RaiseSafeRoomChanged(bool inside)`
  on change; honours `DebugTools --no-gathering`; ignores `Settings.Reinforcements`.
- Exclusions: generation never puts enemy, chest, trap, breakable, ambush, horde, nest or portal in safe-room cells;
  `DungeonBuilder` calls `BlockCell` for every safe-room cell and the door cell; `GasField.Build` gives safe-room cells
  `GasRules.Unreachable`; the HUD air indicator reads clean inside.
- HUD: `Hud.SafeRoom` (bool) shows the text "Safe room" in the objective line area while true.
- Save: `FloorStateDto.Gathered` (int, default 0), restored into the clock's `Spawned`.
- Net: door state travels as today via `PropChanged(NetRefs.Door(index), "opened"/"closed")`; `SafeRoomChanged` and
  regen are server-side, visible to clients through `Vitals`/`PartySync`; a `SafeRoomSync(bool Inside)` message tells
  clients to show the HUD text.

## C. Merchant and shop (`CampaignPlan`, `NpcBodies`, `LegendaryDef`, `src/Items/Core/ShopRules.cs`, `src/UI/Shop/ShopScreen.cs`)

- `CastMember.Merchant` (bool, `[AiDescription]`: exactly one cast member; sells to the party; never a story NPC, never
  a Secret, quest or Betray). `PlanValidator` rejects a floor plan that places the merchant cast member as an NPC and a
  campaign with zero or more than one merchant (`CampaignRepair` adds a generic one when missing).
- `CampaignPlan.Merchant : MerchantPlan?`; `public sealed class MerchantPlan { NpcScript Briefing; List<MerchantFloorLines> FloorLines; List<Line> Gathering; }`;
  `public sealed class MerchantFloorLines { int Floor; Line Greeting; Line Progress; Line Trade; }`.
  `public static class MerchantLineBank` (engine-free, `src/Adventure/Core/Voice/MerchantLineBank.cs`):
  `MerchantPlan Generic(string campaignTitle, string antagonistName)` used when a campaign has no `Merchant` section.
- `NpcBodies`: a cast member with `Merchant == true` always gets `npc_merchant`; any other cast member whose role would
  match `npc_merchant` gets `npc_old_man`.
- Merchant NPC: `DungeonBuilder.PlaceMerchant(FloorLayout layout, CampaignPlan campaign)` when `layout.SafeRoom >= 0`,
  an `Npc` at `SafeRoomRules.MerchantCell(layout)` with `Npc.IsMerchant = true`, name/voice/persona from the cast
  member, net ref `NetRefs.Npc("merchant")`. `Npc.Interact` on a merchant opens `DialogueScreen.OpenMerchant(Npc npc, MerchantFloorLines lines)`
  with three fixed choices: "Trade" → `Hud.Shop.Open(floor)`, "How is it going?" → `lines.Progress`, "Leave" → close.
  First approach within 8 m speaks `lines.Greeting` as a bark (`Npc.Converse`). `EventBus.Gathered` with
  spawnedTotal 2, 4, 6 speaks `Merchant.Gathering[0..2]`.
- `LegendaryDef.ShopOnly` (bool, default false) → `LegendaryBase.ShopOnly`; `LootRoller` excludes `ShopOnly` legendaries
  from every dungeon roll. Seven new `data/legendaries/*.tres` with `ShopOnly = true`: sword, axe, mace, polearm,
  shortbow, dagger, staff.
- `public static class ShopRules` (engine-free): `static readonly int[] UniqueFloors = { 3, 6, 9, 12, 15, 18, 20 }`;
  `WeaponKind UniqueKindFor(ulong gameSeed, int floor)` (a seeded permutation of the seven kinds over the seven floors;
  throws for other floors); `ShopStock Roll(Catalog cat, ulong gameSeed, int floor)`; `public sealed class ShopStock { List<ItemInstance> Items; }`
  with, in order: 2 weapons, 1 body armour, 1 offhand-or-jewellery (all `LootRoller` at item level floor + 2, rarity
  Magic or better), 5 health potions, 3 mana potions, 2 antidotes, 2 arrow stacks of 20, 1 scroll (seeded choice), 1 trap
  kit (seeded choice), then on a unique floor the `ShopOnly` legendary of `UniqueKindFor`. Rolled from
  `new Random(unchecked((int)Core.Rng.Mix(gameSeed ^ ((ulong)floor * 0x5106_5106UL) ^ 0x5A0BUL)))`.
- `ShopState` per floor: `FloorStateDto.ShopSold : List<int>` (indices into the rolled stock that were bought).
- `Hud.Shop : ShopScreen` (`src/UI/Shop/ShopScreen.cs`): `Open(int floor)`, `Close()`, `Visible`; left pane his stock
  (sold indices greyed out), right pane the bag (reuses `ItemSlotControl`), purse between; click or drag to buy/sell;
  Legendary purchases ask once. Solo/server: `PartyController.ShopBuy(int stockIndex)` / `ShopSell(BagSlot slot)`
  (return a refusal string or null; log refusals); client: send intents.
- Net: `[Msg(MsgKind.ShopBuyIntent)] record ShopBuyIntent(int StockIndex)`, `[Msg(MsgKind.ShopSellIntent)] record ShopSellIntent(BagSlot Slot)`,
  `[Msg(MsgKind.ShopSync)] record ShopSync(int Floor, List<int> Sold, int Gold)`; clients roll the same stock from the
  seed and apply `Sold`.

## D. Tavern, floor 0 (`Game`, `LayoutKinds`, `FloorGenerator`, theme `tavern`, `DungeonBuilder`, `DialogueScreen`)

- `Game.CurrentFloor` may be 0; `Game.NewGame` and the title screen's New Game start on floor 0; `Game.JumpToFloor(0)`
  allowed; `ChangeFloor(+1)` from 0 goes to 1; `ChangeFloor(-1)` from 1 stays refused. `--floor=N` debug default unchanged
  (1); `--floor=0` lands in the tavern.
- `LayoutKind.Tavern` (next free byte); `FloorGenerator.Generate` for `Tavern`: one room 7 × 5 cells, `DownStairs` in a
  corner cell of that room, `UpStairs` = the party spawn cell (opposite corner), no spawns of any kind, `SafeRoom = -1`.
  `TierRules`/theme: floor 0 uses `data/themes/tavern.tres` (wood plank floor, timber-and-plaster wall, beam ceiling;
  textures fetched with `scripts/fetch_polyhaven.py` into `assets/textures/tavern/`, listed in `assets/LICENSES.md`).
- `DungeonBuilder.DressTavern(FloorLayout layout)`: tables and benches (box meshes in wood material) and the existing
  barrel/crate models as non-breakable decor, a `Fire` hearth against the wall farthest from the stairs, lanterns, the
  merchant at a table between spawn and stairs (an `Npc` with `IsMerchant = true`, `GreetRadius = 4`, auto-converse once:
  `DialogueScreen.OpenBriefing(Npc npc, NpcScript script)` with the campaign's `Merchant.Briefing`), `Stairs.Prompt`
  "Go down into the tunnels?" on floor 0, `MusicDirector` plays the title tune on floor 0, `Main.LayoutTint` warm.
- `public static class TavernRules` (engine-free): `const int SeedGold = 150`; `const string DealNodeId = "deal"`;
  `bool ShouldPaySeed(bool alreadyPaid, string? reachedNodeId, bool descending)`.
- `AdventureDto.SeedMoneyPaid` (bool). `DialogueScreen` raises `EventBus.RaiseDialogueNode(string npcId, string nodeId)`
  on every node shown; `Game` (or `AdventureDirector`) pays `SeedGold` once when `DealNodeId` is reached on floor 0 or
  when the party descends from floor 0 unpaid.
- Co-op: the server starts on floor 0 like solo; the briefing is lead-only (the interacting player); seed money goes to
  the shared purse once.
- Save: nothing new beyond `SeedMoneyPaid` (floor 0 saves like any floor).

## E. Merchant lines and voice (data + bake)

- `data/adventures/builtin/campaign.json`: cast entry `{ Id: "cast_pellet", Name: "Master Oswin Pellet", Merchant: true,
  Role: "...", Secret: "", Persona: { VoiceStyle: "...", DefaultDelivery: "calm", Voice: "Achird" } }` and the
  `Merchant` section: `Briefing` (node ids `hire`, `quakes`, `deal`, `sendoff`, two choices each, every path reaches
  `deal`), `FloorLines` for floors 1..20, `Gathering` three lines. Voice ids rendered by the existing bake
  (`--bake-cast-voice` extended to the `Merchant` section); `--check-voice-bank` must pass.
- `PromptBuilder` gains the `Merchant` section and the one-merchant rule; `OfflineDesigner` emits
  `MerchantLineBank.Generic(...)` and a generic merchant cast member.

## F. Hit effects (`src/Combat/Fx/HitFx.cs`, `Enemy`, `Projectile`, net)

- `public static class HitFx`: `void Blood(Node parent, Vector3 at, Vector3 towardAttacker)` (one-shot red
  `GpuParticles3D`, 0.6 s, frees itself), `void SpellBurst(Node parent, Vector3 at, Color color)` (one-shot burst in the
  bolt's colour plus a brief `OmniLight3D` flash), `void StickArrow(Enemy enemy, Vector3 worldAt, Vector3 direction)`
  (a thin arrow mesh parented to the enemy's model, kept until the enemy node frees; `Enemy.EmbeddedArrows` drives how
  many are re-created on load, scattered on the torso).
- Hooks: `Enemy.ReceiveAttack` with a hit of `DamageType.Physical` and `AttackKind.Melee` → `Blood`; `Projectile` on a
  hit → arrow: `StickArrow`, spell: `SpellBurst` (and `SpellBurst` on a wall hit for spells).
- Net: `[Msg(MsgKind.HitFxEvt)] record HitFxEvt(byte Kind, NetVec Pos, NetVec Dir, int EnemyNetId, uint Color)`
  broadcast by the server; clients play the same effect. Cosmetic client bolts already stop on contact; they now burst.
- Enemy voices: `EnemyProfile.VoiceSet` (string, default by enemy size: "small", "medium", "large", "boss"); CC0 grunt
  and death clips fetched by `scripts/fetch_grunts.py` into `assets/audio/grunts/<set>/hurt_N.ogg` and `death_N.ogg`
  (source and licence listed in `assets/LICENSES.md`; Kenney's CC0 voice packs or equivalent), registered in
  `AudioManager` as `grunt_hurt_<set>` / `grunt_death_<set>` with the existing random variant pick and pitch jitter;
  `Enemy.ReceiveAttack` plays `grunt_hurt_<set>` instead of the generic `enemy_hurt` (keeping it as the fallback when a
  set has no clips) and the death path plays `grunt_death_<set>`; larger sets pitched lower. Clients hear them through
  the existing `PlaySfx` relay the way `enemy_hurt` reaches them today.
