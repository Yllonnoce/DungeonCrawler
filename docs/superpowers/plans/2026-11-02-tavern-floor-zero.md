# Tavern, floor 0 Implementation Plan (plan D)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new game opens on floor 0, a single hand-dressed tavern room where the merchant delivers his briefing and the party is paid 150 gold once (at the deal, or on descending unpaid), with the down stairs leading to floor 1 exactly as today.

**Architecture:** Floor 0 is an ordinary floor to every system (saves, net, exploration, HUD) built from a new `LayoutKind.Tavern` that the generator turns into one 7×5 room with no spawns, dressed by `DungeonBuilder.DressTavern` in a dedicated `tavern` theme. The merchant is the plan-C merchant NPC with a greet radius; the briefing script comes from the campaign's `Merchant` section (plan C/E) or the generic bank; the payment rule is engine-free (`TavernRules`) and driven by a new `DialogueNode` event plus the descent from floor 0. Automation and the dedicated server keep starting on floor 1 unless `--floor=0` is given.

**Tech Stack:** Godot 4.7.2 .NET, C#, xUnit (tests/), Poly Haven CC0 textures via `scripts/fetch_polyhaven.py`.

**Spec:** `docs/superpowers/specs/2026-11-02-tavern-merchant-safe-rooms-design.md` (section 1 and the "briefing fits the shipped campaign's frame" call). **Contract:** `docs/superpowers/plans/2026-11-02-tavern-shared-interfaces.md` section D; this plan assumes plans A (`PartyController.AddGold`, `Gold`), B and C (`Npc.IsMerchant`, `CampaignPlan.Merchant`, `MerchantPlan.Briefing`, `MerchantLineBank.Generic(title, antagonist)`, `DialogueScreen.OpenBriefing`) are merged.

## Global Constraints

- Engine-free rules in `*/Core` namespaces with xUnit tests in `tests/` (dev machine only); `src/Adventure/Core/**` and `src/Dungeon/Generation/**` are already linked by `tests/DungeonCrawler.Tests.csproj`.
- Build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before any headless run (shared NTFS folder, clock skew).
- `LayoutKindTests.StandardShape_Unchanged` and `LayoutKinds.Deal` for floors 1–20 must not change: `LayoutKinds.All`, `Special`, `BossKinds` and `Deal` are untouched; `Tavern` is never dealt.
- Co-op is server-authoritative: the purse is credited only where `!Net.NetSession.IsClient`; a client reports the deal node with an intent.
- Seed money is **150 gold**, paid **once** per game, at node `deal` or on the first descent from floor 0, whichever comes first.
- No shipped voiced line changes; the briefing text itself is plan E's.
- Windows scripts, if any, are `.bat`. Commit after every task with the attribution line the session requires.

## Review Focus

1. A save made in the tavern, unpaid, loads back into the tavern and still pays exactly once on descent (Task 6, `--test-tavern` saves and loads on floor 0 before descending).
2. `--layout=ring`, a campaign outline naming a layout, or a theme override never changes floor 0's shape or theme (Task 2 headless assertion with `--layout=ring --theme=crypt.brick`).
3. A client joining a server that is still in the tavern builds the same tavern and sees the merchant (Task 5, `scripts/net_test.sh` `START_FLOOR=0` join check).
4. The briefing never opens on top of another screen or while input is locked; it waits and opens once (Task 4 headless: inventory open while approaching, then closed).
5. A campaign with no `Merchant` section (offline designer, legacy mode, a pre-change AI campaign) still gets a briefing with a reachable `deal` node (Task 4 unit test `TavernRules.BriefingFor(null)`).

---

### Task 1: `LayoutKind.Tavern` and `TavernRules` (engine-free)

**Files:**
- Modify: `src/Dungeon/Generation/LayoutKinds.cs` (enum value, `Describe`, `Tune`)
- Modify: `src/Dungeon/Generation/FloorGenerator.cs:17-40` (the shape switch), new private methods `PlaceTavernRoom`, `PlaceTavernStairs`
- Create: `src/Adventure/Core/TavernRules.cs`
- Test: `tests/Dungeon/LayoutKindTests.cs` (append), `tests/Adventure/TavernRulesTests.cs` (new)

**Interfaces:**
- Produces: `LayoutKind.Tavern = 6`; `LayoutKinds.Tune(FloorSpec)` for `Tavern` returns the spec with `Width = 11, Height = 9, MinRooms = MaxRooms = 1, MinRoomSize = 5, MaxRoomSize = 7, EnemyCount = ChestCount = BreakableCount = TrapCount = VaultCount = LockedChestCount = LedgeCount = PitCount = SecretCount = StructureCount = RubbleCount = NestCount = FireCount = 0, HighRoomChance = 0f, HazardsEnabled = false, Objective = FloorObjectiveKind.None`; `FloorGenerator.Generate` for `Tavern` yields exactly one `Room` of `W == 7, H == 5` centred in the grid, `DownStairs == new GridPos(room.X + room.W - 1, room.Y + room.H - 1)`, `UpStairs == new GridPos(room.X, room.Y)`, and empty `EnemySpawns, LedgeSpawns, CellarSpawns, ChestSpawns, BreakableSpawns, TrapSpawns, Vaults, Ledges, Cellars, Secrets, Structures, Water, Fires, Hazards, Nests, KeySpawns`.
- Produces: `public static class TavernRules { public const int SeedGold = 150; public const string DealNodeId = "deal"; public const float GreetRadius = 4f; public static bool ShouldPaySeed(bool alreadyPaid, string? reachedNodeId, bool descending); public static NpcScript BriefingFor(CampaignPlan? campaign); }` in namespace `DungeonCrawler.Adventure` (`BriefingFor` is added in Task 4; declare the class here).

- [ ] **Step 1: Write the failing generator test** in `tests/Dungeon/LayoutKindTests.cs`:

```csharp
[Fact]
public void Tavern_IsOneSevenByFiveRoom_StairsInOppositeCorners_NothingElse()
{
    var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Tavern }, 42);
    Assert.Equal(LayoutKind.Tavern, layout.Kind);
    var room = Assert.Single(layout.Rooms);
    Assert.Equal((7, 5), (room.W, room.H));
    Assert.Equal(new GridPos(room.X, room.Y), layout.UpStairs);
    Assert.Equal(new GridPos(room.X + room.W - 1, room.Y + room.H - 1), layout.DownStairs);
    Assert.Empty(layout.EnemySpawns); Assert.Empty(layout.LedgeSpawns); Assert.Empty(layout.CellarSpawns);
    Assert.Empty(layout.ChestSpawns); Assert.Empty(layout.BreakableSpawns); Assert.Empty(layout.TrapSpawns);
    Assert.Empty(layout.Vaults); Assert.Empty(layout.Ledges); Assert.Empty(layout.Cellars); Assert.Empty(layout.Secrets);
    Assert.Empty(layout.Structures); Assert.Empty(layout.Water); Assert.Empty(layout.Fires); Assert.Empty(layout.Hazards);
    Assert.Empty(layout.Nests); Assert.Empty(layout.KeySpawns);
    Assert.Equal(layout.ContentHash(), FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Tavern }, 42).ContentHash());
}

[Fact]
public void Tavern_IsNeverDealt_AndStandardIsUnchanged()
{
    foreach (var seed in new ulong[] { 1, 42, 7319 }) Assert.DoesNotContain(LayoutKind.Tavern, LayoutKinds.Deal(seed));
    Assert.DoesNotContain(LayoutKind.Tavern, LayoutKinds.All);
    Assert.Equal(LayoutKind.Standard, LayoutKinds.Pick(42, 0)); // floor 0's shape is the builder's business, not the deal's
}
```

- [ ] **Step 2: Run it** — `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~LayoutKindTests.Tavern"`. Expected: FAIL, `LayoutKind` has no `Tavern`.

- [ ] **Step 3: Implement.** Add `Tavern = 6` with a doc comment ("the tavern above the dungeon: one room, floor 0 only; never dealt"). `Describe`: "the tavern over the dungeon: one warm room with the way down in a corner". `Tune`: the values in Interfaces. In `Generate`, `case LayoutKind.Tavern: PlaceTavernRoom(spec, layout); CarveRooms(layout); break;` where `PlaceTavernRoom` adds one `Room` 7×5 centred (`X = (spec.Width - 7) / 2`, `Y = (spec.Height - 5) / 2`). After `PlaceStairs(layout)` add `if (layout.Kind == LayoutKind.Tavern) PlaceTavernStairs(layout);` which sets `UpStairs`/`DownStairs` to the two corners above. Every later placement step is a no-op with the zeroed spec; verify by the test rather than by special cases.

- [ ] **Step 4: Run** the same filter. Expected: PASS. Then `dotnet test --nologo -v q --filter "FullyQualifiedName~LayoutKindTests"`: all pass, `StandardShape_Unchanged` included.

- [ ] **Step 5: Write the failing `TavernRules` test** in `tests/Adventure/TavernRulesTests.cs`:

```csharp
[Theory]
[InlineData(false, "deal", false, true)]
[InlineData(false, null, true, true)]
[InlineData(false, "hire", false, false)]
[InlineData(false, null, false, false)]
[InlineData(true, "deal", false, false)]
[InlineData(true, null, true, false)]
public void ShouldPaySeed_OnceAtTheDealOrOnDescent(bool paid, string? node, bool descending, bool expected)
    => Assert.Equal(expected, TavernRules.ShouldPaySeed(paid, node, descending));

[Fact] public void SeedGold_Is150_AndTheDealNodeIsNamedDeal() { Assert.Equal(150, TavernRules.SeedGold); Assert.Equal("deal", TavernRules.DealNodeId); }
```

- [ ] **Step 6: Run** `--filter "FullyQualifiedName~TavernRulesTests"`. Expected: FAIL, type missing.
- [ ] **Step 7: Implement `TavernRules`** in `src/Adventure/Core/TavernRules.cs` (`ShouldPaySeed` = `!alreadyPaid && (reachedNodeId == DealNodeId || descending)`).
- [ ] **Step 8: Run** the filter. Expected: PASS.
- [ ] **Step 9: Commit** — `git add src/Dungeon/Generation/LayoutKinds.cs src/Dungeon/Generation/FloorGenerator.cs src/Adventure/Core/TavernRules.cs && git commit -m "feat(tavern): LayoutKind.Tavern, a one-room floor, and the seed-money rule"`.

### Task 2: Floor 0 exists (Game, Main, DungeonBuilder, HUD, stairs, debug start floor)

**Files:**
- Modify: `src/Core/Game.cs` (`PrepareNewGame`, `JumpToFloor`), `src/Core/DebugTools.cs` (start floor), `src/Core/Main.cs:390-399` (`PlanFor`), `src/Core/Main.cs:548-553` (`LayoutTint`), the descent log line in `RebuildFloorInner`
- Modify: `src/Dungeon/DungeonBuilder.cs` (`LayoutFor`, `ThemeFor`, `PlaceStairs` skip the up flight on floor 0), `src/Dungeon/Stairs.cs:35` (`Question`), `src/UI/Hud/Hud.cs:358-364` (floor label)
- Modify: `src/Adventure/AdventureDirector.cs:477` (`OnFloorBuilt` tolerates floor 0 with no plan)
- Test: headless (Task 6 proves it); unit: none beyond Task 1

**Interfaces:**
- Produces: `Game.PrepareNewGame(ulong? seed = null)` sets `CurrentFloor = DebugTools.Instance?.StartFloor ?? 0`; `DebugTools.StartFloor` (`int?`): `null` for an ordinary title-screen run, `Math.Min(_startFloor, 1)` when `Automated || _server` (so every existing `--test-*` and the server still open on floor 1), and `0` when `--floor=0` was given explicitly. `Game.JumpToFloor` clamps to `Math.Max(0, floor)`. `ChangeFloor` is unchanged: `target < 1` is refused, so floor 1 still cannot go up and floor 0 → 1 works.
- Produces: `DungeonBuilder.LayoutFor(0, seed) == LayoutKind.Tavern` regardless of `--layout` or campaign outline; `DungeonBuilder.ThemeFor(0, seed)` returns the theme with `Id == "tavern"` (Task 3 adds it; until then `null` and the tier default) regardless of `--theme` or outline; `Main.PlanFor(0)` returns `null` (floor 0 is never designed); `Hud` floor label reads `"The Tavern"` on floor 0; `Stairs.Question` on floor 0 reads `"Go down into the tunnels?"`; `Main.LayoutTint(Tavern, look)` = fog `look.FogColor.Lerp(new Color(0.26f, 0.18f, 0.09f), 0.55f)`, ambient `look.AmbientColor.Lerp(new Color(0.62f, 0.48f, 0.32f), 0.5f)`, scale `0.7f`; the floor-built log on floor 0 is `"The tavern. The way down is in the corner."`.

- [ ] **Step 1: Start floor.** In `DebugTools` add `public int? StartFloor` computed as above (note `_startFloor` is initialised to 1 today; `--floor=0` must parse to 0 and be distinguishable: keep a `bool _floorGiven` set by the parser). In `Game.PrepareNewGame` set `CurrentFloor = DebugTools.Instance?.StartFloor ?? 0;`. In `JumpToFloor` use `Math.Max(0, floor)`. Leave the existing `_startFloor > 1` jump as it is.
- [ ] **Step 2: Builder.** `LayoutFor`: first line `if (floor == 0) return LayoutKind.Tavern;`. `ThemeFor`: first line `if (floor == 0) return _themes.FirstOrDefault(t => t.Id == "tavern");`. `PlaceStairs(layout, root, floorIndex)`: do not instantiate the up flight when `floorIndex == 0` (the party still spawns at `WalkableNeighbor(layout, UpStairs)`, which Main already does).
- [ ] **Step 3: Main.** `PlanFor`: `if (floor == 0) return null;` as the first line. `LayoutTint`: the `Tavern` case. The descent log: when `floor == 0` print the tavern line instead of "You descend to floor 0.".
- [ ] **Step 4: Director, HUD, stairs.** `AdventureDirector.OnFloorBuilt`: with `floor == 0` and `contents.Plan == null` it must not narrate, taunt or enqueue voice; confirm by reading the method and add `if (floor == 0) { _currentFloor = 0; return; }` after `OpenRecord(floor)` if anything below would misfire. `Hud.OnFloorChanged`: `_floorText = floor == 0 ? "The Tavern" : <existing>`. `Stairs.Question`: `Direction > 0 && (Game.Instance?.CurrentFloor ?? 1) == 0 ? "Go down into the tunnels?" : <existing>`.
- [ ] **Step 5: Build and smoke** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` (0 errors), then `timeout 60 godot --headless --path . -- --floor=0 --quit-after=6 --layout=ring 2>&1 | grep -E "\[DungeonBuilder\] floor 0|tavern"`. Expected: a `[DungeonBuilder] floor 0 (...) ... tavern)` line with `rooms=1 enemies=0` despite `--layout=ring`. Then `timeout 60 godot --headless --path . -- --test-skills 2>&1 | grep RESULT` still prints `[skills] RESULT ok` (automation still starts on floor 1).
- [ ] **Step 6: Commit** — `git commit -am "feat(tavern): floor 0 is a real floor: the builder, HUD, stairs and start-floor rules know it"`.

### Task 3: The `tavern` theme and the dressing

**Files:**
- Create: `data/themes/tavern.tres`, `assets/textures/<id>/...` (fetched), entries in `assets/LICENSES.md`
- Modify: `src/Dungeon/DungeonBuilder.cs` (`Build` calls `DressTavern` when `layout.Kind == LayoutKind.Tavern`; new `DressTavern(FloorLayout layout, Node3D root)`), `src/Audio/MusicDirector.cs` (`Mood.Tavern`)
- Test: `tests/Dungeon/ThemeTests.cs` (append); headless check in Task 6

**Interfaces:**
- Produces: theme `Id = "tavern"`, `TierIndex = 99` (so `ThemeProfile.ForTier` never offers it to floors 1+), `Weight = 1`, `FloorTexture` a wood plank set, `WallTexture` a plaster or timber set, `CeilingTexture` a rough plank set, `FloorColor (0.42,0.30,0.18)`, `WallColor (0.70,0.62,0.50)`, `CeilingColor (0.30,0.22,0.14)`, `MetersPerTile = 1.5`, `Vault = ""` (flat ceiling), `AmbientColor (0.55,0.42,0.28)`, `AmbientEnergy 0.5`, `FogColor (0.18,0.12,0.06)`, `FogDensity 0.02`, `TorchColor (1,0.78,0.5)`, `WallLightsPerRoom 4`.
- Produces: `MusicDirector.Mood.Tavern` plays `MenuTracks[0]` (the title tune) looped while `Game.Instance.CurrentFloor == 0 && InGame`; the existing mood logic chooses `Explore`/`Combat` otherwise.
- Produces: `DressTavern` adds under `root`: three tables (`Box` meshes `1.6 × 0.08 × 0.8` at height 0.78 on four legs `0.08 × 0.78 × 0.08`, plank material `Material("weathered_planks", new Color(0.5f,0.38f,0.24f), 1.6f)`) with a bench on each long side (`1.6 × 0.06 × 0.35` at 0.45), a candle `OmniLight3D` (`LightColor (1,0.8,0.5)`, energy 0.9, range 3) on each table; four barrels and two crates along the wall through the existing `Decor(...)` helper using the paths `PlaceLightsAndDecor` already uses for barrels/crates (with `collisionRadius 0.45`); a `Fire` node (`Radius 0.5, Height 1.2`) as the hearth against the wall farthest from `DownStairs`, on a stone plinth `Box 1.6 × 0.3 × 0.9`; everything in `_propCells` via `BlockCell` so companions path around it. Textures: run `scripts/fetch_polyhaven.py` with, in order of preference, floor `wood_floor_deck` then `wood_planks_dirt`; wall `plaster_brick_pattern` then `beige_wall_001`; ceiling `wooden_rough_planks` then `wood_planks_grey`; use the first id of each that fetches, then `godot --headless --import --path .`.

- [ ] **Step 1: Write the failing theme test** in `tests/Dungeon/ThemeTests.cs` (follow its existing data-file reading; if it loads `data/themes/*.tres` textually, parse `Id` and `TierIndex`):

```csharp
[Fact]
public void TavernTheme_ExistsAndIsNeverOfferedToADungeonFloor()
{
    var themes = LoadThemeProfiles(); // the helper this test file already uses, or a small .tres reader
    var tavern = Assert.Single(themes, t => t.Id == "tavern");
    for (var tier = 0; tier < TierRules.TierCount; tier++) Assert.DoesNotContain(ThemeProfile.ForTier(themes, tier), t => t.Id == "tavern");
}
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ThemeTests.TavernTheme"`. Expected: FAIL (no tavern theme).
- [ ] **Step 3: Fetch textures and write `data/themes/tavern.tres`** (copy `crypt_brick.tres`'s structure); add the three Poly Haven entries to `assets/LICENSES.md`; import.
- [ ] **Step 4: Run** the filter. Expected: PASS.
- [ ] **Step 5: Implement `DressTavern` and `Mood.Tavern`**, call `DressTavern` from `Build` after `PlaceLightsAndDecor`. `MusicDirector._Process`: `wanted = Mood.Tavern` when in game on floor 0; `case Mood.Tavern: Play(MenuTracks[0], loop: true, fadeSeconds: 2f)`.
- [ ] **Step 6: Build and look** — build, then `timeout 60 godot --headless --path . -- --floor=0 --quit-after=6 --count-props 2>&1 | grep -E "floor 0|Fire|Table|prop"`; expected: the floor 0 line names theme `tavern`, and the root holds one `Fire` child and three `Table*` nodes (name them `Table0..2`). With a display, `godot --path . -- --floor=0 --screenshot=/tmp/tavern.png --quit-after=4` and look at it.
- [ ] **Step 7: Commit** — `git add data/themes/tavern.tres assets/textures assets/LICENSES.md src/Dungeon/DungeonBuilder.cs src/Audio/MusicDirector.cs && git commit -m "feat(tavern): the tavern theme, its furniture, hearth and tune"`.

### Task 4: The merchant in the tavern: greeting, briefing, seed money

**Files:**
- Modify: `src/Adventure/Core/TavernRules.cs` (`BriefingFor`), `src/Adventure/Npc.cs` (`GreetRadius`, `Greeted`, the approach check), `src/UI/Dialogue/DialogueScreen.cs:80-95` (`Show` raises the node event; `OpenBriefing`), `src/UI/Hud/Hud.cs` (`OpenBriefing(Npc)`), `src/Core/EventBus.cs` (`DialogueNode` event), `src/Save/Core/SaveDtos.cs:40` (`AdventureDto.SeedMoneyPaid`), `src/Adventure/AdventureDirector.cs` (pay at the deal; `OnLeavingTavern`), `src/Core/Game.cs:123` (`ChangeFloor` calls `OnLeavingTavern` when leaving 0), `src/Dungeon/DungeonBuilder.cs` (`PlaceTavernMerchant`)
- Test: `tests/Adventure/TavernRulesTests.cs` (append)

**Interfaces:**
- Consumes: `Npc.IsMerchant` (C), `CampaignPlan.Merchant?.Briefing` (C), `MerchantLineBank.Generic(string title, string antagonist)` (C), `DialogueScreen.OpenBriefing(Npc, NpcScript)` (declared by C; if C has not added it, add it here as `npc.Script = script; Open(npc);`), `PartyController.AddGold(int)` (A), `Game.Instance.Adventure.Campaign.CastFor(...)` / `campaign.Cast.FirstOrDefault(c => c.Merchant)`.
- Produces: `TavernRules.BriefingFor(CampaignPlan? campaign)` = `campaign?.Merchant?.Briefing` when it has a node with `Id == DealNodeId`, else `MerchantLineBank.Generic(campaign?.Title ?? "the tunnels", campaign?.Antagonist.Name ?? "what waits below").Briefing`; `Npc.GreetRadius` (float, 0 off) and `Npc.Greeted`; `EventBus.DialogueNode` (`Action<string npcId, string nodeId>`), `RaiseDialogueNode`; `Hud.OpenBriefing(Npc npc)` → `Dialogue.OpenBriefing(npc, TavernRules.BriefingFor(Game.Instance.Adventure.Campaign))`; `AdventureDto.SeedMoneyPaid` (bool); `AdventureDirector.OnLeavingTavern()`; `DungeonBuilder.PlaceTavernMerchant(FloorLayout layout, Node3D root)` spawning `Npc` id `"merchant"`, mode `NpcMode.Quest`, `IsMerchant = true`, `GreetRadius = TavernRules.GreetRadius`, at `layout.Rooms[0].Center`, named after the campaign's merchant cast member (fallback name "The Merchant"), facing `WalkableNeighbor(layout, layout.UpStairs)`.

- [ ] **Step 1: Write the failing tests**:

```csharp
[Fact]
public void BriefingFor_NoCampaign_StillReachesTheDeal()
{
    var script = TavernRules.BriefingFor(null);
    Assert.Contains(script.Nodes, n => n.Id == TavernRules.DealNodeId);
    Assert.Equal(script.Start, script.Nodes[0].Id);
    // every path from the start reaches the deal: walk every choice
    var reach = new HashSet<string>(); var queue = new Queue<string>(new[] { script.Start });
    while (queue.Count > 0) { var id = queue.Dequeue(); if (!reach.Add(id)) continue; foreach (var c in script.Nodes.First(n => n.Id == id).Choices) if (c.Next != null) queue.Enqueue(c.Next); }
    Assert.Contains(TavernRules.DealNodeId, reach);
}

[Fact]
public void BriefingFor_CampaignWithoutADealNode_FallsBackToTheBank()
{
    var campaign = new CampaignPlan { Title = "The Gullet of Gorm", Merchant = new MerchantPlan { Briefing = new NpcScript { Start = "x", Nodes = { new DialogueNode { Id = "x" } } } } };
    Assert.Contains(TavernRules.BriefingFor(campaign).Nodes, n => n.Id == "deal");
}
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~TavernRulesTests.BriefingFor"`. Expected: FAIL (`BriefingFor` missing).
- [ ] **Step 3: Implement `BriefingFor`.** Run the filter: PASS.
- [ ] **Step 4: Greeting and briefing.** `Npc`: on the server/solo and on every client, in `_PhysicsProcess`, when `GreetRadius > 0 && !Greeted && Main.Instance is { Hud: { } hud, Party: { Local: { } local } } && !hud.DialogueOpen && !hud.InventoryOpen && !hud.Paused && !hud.StairsPromptOpen && !Main.Instance.Party.InputLocked && local.GlobalPosition.DistanceTo(GlobalPosition) <= GreetRadius` → `Greeted = true; hud.OpenBriefing(this);` (local only; the briefing never goes over the wire). `DialogueScreen.Show` raises `EventBus.RaiseDialogueNode(Npc.Id, node.Id)` after setting `Node`; on a client it also sends `new Net.DialogueNodeIntent(Npc.Id, node.Id)` (Task 5 adds the message; until then guard with `#if` nothing: just the event here).
- [ ] **Step 5: Payment.** `AdventureDirector` subscribes to `EventBus.DialogueNode` in its `_Ready`: when `!Net.NetSession.IsClient && Game.Instance.CurrentFloor == 0 && npcId == "merchant" && TavernRules.ShouldPaySeed(Game.Instance.Adventure.SeedMoneyPaid, nodeId, descending: false)` → `Pay()` = `Main.Instance.Party.AddGold(TavernRules.SeedGold); Game.Instance.Adventure.SeedMoneyPaid = true; EventBus.Instance.Log("The seed money: 150 gold, counted twice.")`. `OnLeavingTavern()`: `if (TavernRules.ShouldPaySeed(paid, null, descending: true)) Pay();`. `Game.ChangeFloor`: before `CurrentFloor = target`, `if (CurrentFloor == 0 && delta > 0) AdventureDirector.Instance?.OnLeavingTavern();`.
- [ ] **Step 6: Place him.** `DungeonBuilder.Build`: after `DressTavern`, `if (layout.Kind == LayoutKind.Tavern) PlaceTavernMerchant(layout, _floorRoot);` using `SpawnObjectiveNpc` with the merchant cast member's name and `BodyPath(Adventure.NpcBodies...)` as C defines for merchants; set `npc.Script = TavernRules.BriefingFor(campaign)` so E on him repeats the briefing.
- [ ] **Step 7: Build and smoke** — build; `timeout 60 godot --headless --path . -- --floor=0 --quit-after=6 2>&1 | grep -E "Npc_merchant|merchant"`: the merchant is spawned on floor 0. (The full behaviour is proven in Task 6.)
- [ ] **Step 8: Commit** — `git commit -am "feat(tavern): the merchant greets the party, briefs them and pays the seed money once"`.

### Task 5: Co-op: the deal node reaches the server; the server's start floor

**Files:**
- Modify: `src/Net/Core/Protocol.cs` (`MsgKind.DialogueNodeIntent` appended at the current end of `MsgKind` (after whatever earlier plans added; never reorder existing values)), `src/Net/Core/Messages.cs` (`[Msg(MsgKind.DialogueNodeIntent)] public sealed record DialogueNodeIntent(string NpcId, string NodeId) : NetMessage;`), `src/Net/ServerWorld.cs` (handler → `EventBus.RaiseDialogueNode(npcId, nodeId)` on the server), `src/UI/Dialogue/DialogueScreen.cs` (send it when `IsClient`), `scripts/net_test.sh` (server line gains `--floor=1`; optional `START_FLOOR=0` run), `src/Net/NetTestFlow.cs` (join asserts the client's floor equals `Welcome.CurrentFloor` and, when 0, `Layout.Kind == Tavern` and the merchant NPC exists)
- Test: `scripts/net_test.sh` (both runs)

**Interfaces:**
- Consumes: `DebugTools.StartFloor` (Task 2) so `--server --floor=1` opens the server on floor 1 and a bare `--server` on floor 0.
- Produces: `DialogueNodeIntent`; `NetTestFlow` step "join floor" ; `scripts/net_test.sh` runs the server with `--floor=1` by default and with `--floor=0` when `START_FLOOR=0` is set, in which case the client flow stops after the join checks (`--net-test-join-only`).

- [ ] **Step 1: Message and handler.** Add the enum value, the record, the server handler (only `Game.Instance.CurrentFloor == 0` matters today, but relay every node: `AdventureDirector` filters), and the client send in `DialogueScreen.Show`.
- [ ] **Step 2: net_test.** Edit `scripts/net_test.sh`: `--floor=${START_FLOOR:-1}` on the server command; when `START_FLOOR=0`, pass `--net-test-join-only` to the client. In `NetTestFlow`, after the Welcome: assert `Game.Instance.CurrentFloor == welcome.CurrentFloor`; if 0, assert `Main.Instance.Builder.Layout.Kind == LayoutKind.Tavern` and `FloorRoot` has `Npc_merchant`; print `[net-client] tavern join OK`; with `--net-test-join-only` print `RESULT ok` and quit.
- [ ] **Step 3: Run** `dotnet build ... --no-incremental`, then `scripts/net_test.sh` (both processes print `RESULT ok`, as before) and `START_FLOOR=0 scripts/net_test.sh` (client prints `tavern join OK` and `RESULT ok`). Run `dotnet build-server shutdown` first if rtt looks slow (see memory: headless test gotchas).
- [ ] **Step 4: Commit** — `git commit -am "feat(tavern): a client's deal node reaches the server; the server starts in the tavern unless told a floor"`.

### Task 6: `--test-tavern` and the manual

**Files:**
- Modify: `src/Core/DebugTools.cs` (flags `--test-tavern`, `--test-tavern-skip`; `RunTavernTest()` dispatched beside `_testSkills`), `docs/development.md` (the `--test-*` list; a "Floor 0: the tavern" paragraph under the floor shapes section; the themes note)
- Test: the flow itself

**Interfaces:**
- Consumes: everything above; `PartyController.Gold`.
- Produces: `[tavern] RESULT ok` / `RESULT FAIL (...)`.

- [ ] **Step 1: The flow.** Both flags set `StartFloor` to 0 (they are automation but want the tavern: set `_startFloor = 0; _floorGiven = true`), `_quitAfter = 20`. Steps by `_elapsed`:
  1. `≥ 1.0`: assert `CurrentFloor == 0`, `Layout.Kind == Tavern`, `Rooms.Count == 1`, no `Enemy` under the floor root, `Npc_merchant` present, `Party.Gold == 0`, HUD floor text `"The Tavern"`, `MusicDirector.Mood == Tavern` (when music is enabled headless; otherwise skip the mood check and say so). Open the inventory (`Hud.ToggleInventory()`), teleport the party to `Surface(room.Center) + (0,0,2)` facing the merchant.
  2. `≥ 2.0`: assert `!Hud.DialogueOpen` (the inventory blocked the greeting); close the inventory.
  3. `≥ 3.0`: assert `Hud.DialogueOpen` and `Dialogue.Node.Id == "hire"` (or the bank's start id); in `--test-tavern`: `Main.SaveGame("tavern")` then `Main.LoadGame("tavern")` — after the load assert `CurrentFloor == 0`, `Gold == 0`, `SeedMoneyPaid == false`, re-teleport and wait for the greeting again (the merchant on the rebuilt floor greets afresh: `Greeted` is not saved; assert the dialogue reopens by step 4 without crediting gold yet).
  4. `≥ 5.5` (`--test-tavern` only): `Dialogue.Choose(0)` until `Node.Id == "deal"` (at most 6 choices), assert `Party.Gold == 150` right after the deal node shows; keep choosing until closed; assert `Gold == 150` still.
  5. `≥ 8.0`: teleport onto the down-stairs landing cell (`WalkableNeighbor(layout, DownStairs)` then the landing: reuse the approach `--test-stairs` uses) and `stairs.Confirm()`; `≥ 10.0`: assert `CurrentFloor == 1`, `Gold == 150` (paid once), `Layout.Kind != Tavern`; print `RESULT ok`.
  `--test-tavern-skip`: skip step 4 (never talk; close the briefing with `Dialogue.Close()` if it opened); expect `Gold == 0` until the descent, then `150` on floor 1.
- [ ] **Step 2: Run** both: `timeout 90 godot --headless --path . -- --test-tavern 2>&1 | grep -E "^\[tavern\]"` and the `-skip` variant; both end in `RESULT ok`. Also `--test-tavern --layout=ring --theme=crypt.brick` still passes (Review Focus 2).
- [ ] **Step 3: Docs.** Add both flags to the `--test-*` list in `docs/development.md`, a paragraph "Floor 0: the tavern" (one room, `LayoutKind.Tavern`, theme `tavern`, the merchant's briefing, seed money rule, `--floor=0`, automation and the server start on floor 1 unless `--floor=0`), and `tavern` to the themes note.
- [ ] **Step 4: Full suite** — `cd tests && dotnet test --nologo -v q` (all pass) and `scripts/net_test.sh` once more.
- [ ] **Step 5: Commit** — `git add src/Core/DebugTools.cs docs/development.md && git commit -m "test(tavern): --test-tavern proves the opening, the briefing, the seed money and the way down"`.

---

## Self-review notes

- Spec coverage: floor 0 as a real floor (Task 2), one dressed room with theme, hearth, tune, warm tint (Task 3), merchant at a table with auto-briefing at 4 m, purse credited at the deal or on descent, stairs prompt text (Task 4, Task 2), co-op server start and lead-only briefing with the deal reaching the server (Task 5), saves on floor 0 (Task 6 round trip), `--test-tavern` (Task 6). The briefing text and the shipped `Merchant` section are plan E; the generic bank is plan C.
- Type consistency: `TavernRules.{SeedGold, DealNodeId, GreetRadius, ShouldPaySeed, BriefingFor}`, `Npc.{GreetRadius, Greeted, IsMerchant}`, `Hud.OpenBriefing(Npc)`, `DialogueScreen.OpenBriefing(Npc, NpcScript)`, `EventBus.{DialogueNode, RaiseDialogueNode}`, `AdventureDto.SeedMoneyPaid`, `AdventureDirector.OnLeavingTavern`, `DebugTools.StartFloor`, `DialogueNodeIntent(NpcId, NodeId)` are used with the same names throughout.
- Contract check against the code: `Game.PrepareNewGame` sets `CurrentFloor = 1` today and `JumpToFloor` clamps to 1 (Task 2 changes both); `ChangeFloor` already refuses `target < 1`, which is exactly the spec's rule once floor 0 exists; `LayoutKinds.Pick(seed, 0)` returns `Standard` and is left alone (the builder's `LayoutFor` owns floor 0); `ThemeFor` would otherwise pick a Crypt theme for tier 0 (floor 0 clamps to tier 0), hence the explicit `tavern` lookup; `Main.PlanFor(0)` would otherwise call the offline designer for floor 0, hence the guard; every `--test-*` and `scripts/net_test.sh` assume floor 1 at start, hence `DebugTools.StartFloor` and `--floor=1` on the server line.
