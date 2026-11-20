# Mine Cart Chase, Plan 10: crevasses, death and the wipe rise Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12 gets real crevasses: a new `Cell.Chasm` (a hole with no floor) carved as the cavern on ride 4's
`RailLine.Cavern` span (the track rides a timber deck over the drop), as strips beside the line on the other rides (a gorge
under ride 5's trestle and jump), and as plank crossings in the halls. A fall kills: a hero is dead until the party reaches
a stop, crew are lost, enemies die, Grisel catches the chains at the lip, a train over a crevasse that misses its jump or
loses its trestle falls with everything coupled to it, and a wipe rises at the last stop reached beside a fresh cart with
the train as it arrived there, in single player and co-op.

**Architecture:** One knob, `FloorSpec.Crevasses` (off by default), turns on a rng-free generator pass,
`FloorGenerator.CarveChasms`, that runs right after `LayRails`. It only ever turns rock (and refuge pockets) into
`Cell.Chasm`; the cells a walker or the cart needs to cross a drop stay `Cell.Floor` and are listed as bridges (a deck
on the cavern's lanes, a narrow plank in a hall). The engine-free `CrevasseRules` say what a fall does to whom and
whether a lane cell is "over a crevasse". A server/solo `CrevasseWatch` node applies them to bodies that drop below a
chasm's lip, and `RailController.Fall` applies them to a whole train. The rail controller snapshots the floor (deferred
one frame) at every stop arrival; a wipe merges the train's part of that snapshot into the wipe's capture and the party
is set down beside the cart instead of at the stairs.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md` (build-order item 2; sections "The line" →
"Crevasse cavern" and "Crevasse edges elsewhere", "Crevasses and death", "After a wipe", and the calls "What a fall
costs" and "Grisel never dies in a fall"). Code maps: `.superpowers/sdd/floor12-v2-recon/gameplay.md` and
`generation.md`. Rulings already made: `.superpowers/sdd/mine-cart-rulings.md`.

**Builds on:** plan 9 as built (commits `5175e91..bdc7317`): `RailLoop`, `RailBand` 14, five rides on floor 12, the
`RailLine.Cavern` span on ride 4 (measured `cavern=238..259`), `LineShape`, the per-ride rules. Line numbers in this plan
are hints and drift; find code by name. `docs/development.md`, `src/Core/DebugTools.cs` and `src/Core/DebugTools.Crew.cs`
are being edited by a concurrent fix wave: read their current text before editing them.

**The later plans** (not this one): 11 barricades and live switches with deadly branches (the wrong branch ends at
`RailController.Fall`), 12 the dark and the runaway (the runaway's spur ends in a crevasse), 13 the cavern's collapsing
bridge and spiral (replaces this plan's plain deck), 14 the leap, 15 the loop-the-loop. Each calls this plan's
`RailController.Fall(string why)` and reads `CrevasseRules`; none is planned here.

**Planner's rulings** (mine; each decides something the spec left open, with what it costs if wrong):
1. Ruling: one new knob, `FloorSpec.Crevasses` (bool, `[AiIgnore]` in `SpecOverrides`), not keyed on `RailBand` — the
   hall crossings are not the band's business, and a knob keeps every other floor byte-identical by construction — cost
   if wrong: one more line in floor 12's overrides.
2. Ruling: the cavern is the box outward of the cavern span at t = 2 .. 2 + `CavernDepth` (the enemy lane's pocket row and
   plan 9's reserved rock), and both lanes over the span become a timber **deck** (walkable bridge cells, no stone floor
   under them). "The track crosses it" is read as "rides a plain trestle deck over the drop"; plan 13 replaces the deck
   with the collapsing bridge and the descending track — cost if wrong: until plan 13 the cavern reads as a drop beside a
   trestle, not a span from wall to wall.
3. Ruling: plan 9's deferred review item — the cavern box is enforced, not only tested. Stretches routed after the cavern
   segment treat the box as foreign; no refuge pocket is placed in the cavern's reach (t = 2 .. 2 + `CavernDepth`), and the
   window's enemy-lane cells need no pocket cover; `CommitRails` drops the span (`Cavern = null`, reported
   `cavern=breached`) if any line or pocket cell still lands in the box, rather than losing the line — cost if wrong: a
   seed with a breach has no cavern (the floor 12 gate fails loudly on it).
4. Ruling: edge strips are 2 cells deep, on straight party-lane runs of 4–8 cells at least 4 cells from either platform:
   inward of the party lane (t = −1, −2) when that rock is free, else outward of the enemy lane (t = 2, 3), always with a
   rock wall behind. One per ride (the longest run) except the cavern ride; on the **last ride every eligible run inside
   fractions 0.50–0.85 of the ride** is carved (the gorge under the trestle and the jump). Pockets inside a strip are
   dropped, and a lane cell with a chasm within 2 cells needs no pocket cover — cost if wrong: a ride on some seeds gets
   no strip, and a walker beside a strip has to cross to the other lane's pockets.
5. Ruling: hall crossings are planks only, never detours: up to 3, each in a straight one-cell-wide corridor at least 3
   cells from any room, the chasm 2 cells either side, chosen farthest-point from the up stairs. Only rock becomes chasm,
   so no walk is ever cut — cost if wrong: no detour variant; a body stepping off the 1.6 m plank's side falls.
6. Ruling: chasms are carved only at level 0, never beside a ramp, a cellar/tunnel (`IsUnder`) or the under layer — cost:
   fewer strip candidates on climbing stretches.
7. Ruling: "over a crevasse" is geometry, not data: a party-lane cell that is a bridge or has a chasm within 2 cells
   (Chebyshev). A missed jump or a trestle collapse over such cells is a **fall**; anywhere else it is today's wreck.
   Floor 12's `r5_trestle` and `r5_jump` `At` are moved into the measured gorge by the gate task — cost if wrong: an AI
   plan's jump away from a chasm still wrecks (correct: there is no drop there).
8. Ruling: a fallen hero (`CharacterState.Fallen`) comes back when the party cart rolls into a stop (`Arrive`, not a spare's
   takeover), or when a living hero on foot steps onto any stop platform after the fall, or on any `RailController.Restore`
   (a load, a wipe rise), or on a floor change; back at 25 % of max HP with effects cleared, beside the cart or that hero —
   cost if wrong: balance (the spec says only "back when the party next reaches a stop").
9. Ruling: `Fallen` is saved (`CharacterDto.Fallen`) and reaches clients in the `PartySync` sheet (its JSON signature covers
   a new field). A fallen body is hidden, not solid and frozen 6 m under the lip; `Adventurer.FellOut` over a chasm or
   bridge cell holds the body still instead of putting it back on its last ground (a client waits for the server's word) —
   cost if wrong: none seen.
10. Ruling: falls are judged on the server and in single player only, by `CrevasseWatch`: a body over a chasm or bridge cell
    and 2.5 m below that cell's floor. It covers every hero (a remote player's by the server's copy of its body), every
    enemy not carried by a cart, every NPC not seated — cost if wrong: a body that clips through a deck edge on a client
    falls only when the server sees it.
11. Ruling: any escorting NPC that is not crew (Grisel, an ally, a quest escort) is caught at the lip like Grisel; other
    NPCs are lost (they die); bosses die like enemies — keeps every escort quest alive, and floor 12 has no other NPC on
    the line — cost if wrong: an escort can never be lost to a crevasse.
12. Ruling: the lip cell is the nearest walkable cell to the fall point that is not a lane cell (pockets count) within 3
    cells, else the nearest walkable cell within 6; the escort is laid there downed but not Mangled, so the existing
    "Revive {name}" prompt serves — cost if wrong: Grisel may lie on a pocket the party has to look for.
13. Ruling: `RailController.Fall` is a wreck with no tumble: riders, the hounds aboard, every coupled wagon and its crew go
    (wagons hidden and marked lost, `ObjectiveStateDto.LostCages`); a spare takes over only if a hero still stands, so a
    fall that takes everyone spends no spare before the wipe. On the wire it is today's Wrecked state plus a new
    `CartStateDto.Fell` bool (`CartFlags` has no free bit); a client plays the plunge, not the tumble, on that rising edge
    — cost if wrong: one more DTO field.
14. Ruling: the arrival snapshot is a whole `FloorStateDto` captured one frame after every `Arrived` and `Restored` event
    (server/solo). The wipe keeps its own live capture and takes from the snapshot only the train: `RailStop`, the crew's
    and Grisel's `NpcDto`s, and the objective's `OpenCages`, `CoupledCages`, `HomeCrew`, `LostCrew`, `FreedNpcs` and
    `LostCages`; spares, gates, the orc's keys, enemies and chests stay as they are at the wipe — cost if wrong: a cage
    opened after the arrival is shut again with its crew inside.
15. Ruling: every wipe on a railed floor rises at the cart (any cause, not only a fall), and remote heroes are placed by the
    server before the clients are ready, so the existing `OnFloorReady` ForcePose lands them; the RIDE pass gets one tail
    step to prove it — cost if wrong: a wipe on foot far from the line also rises at the cart (the spec wants this).
16. Ruling: the walking warning is a new stock bark trigger, `BarkTrigger.Crevasse`, appended to the enum (stored by name)
    with subtitle-only stock lines in `RideBarks`, said once per chasm region per floor visit when a hero on foot first
    comes within 2 cells; the jump keeps its `JumpCall` — cost if wrong: a few unvoiced lines.
17. Ruling: plan 9's deferred cue item — `CueRangeAt` clamps against the nearest of **every** stop, not `StopS[^2]` alone.
    Plan 10 adds no new ranged cue (no audio assets; the plunge reuses `cart_wreck`, a walker's fall the existing `fall`
    sfx) — cost if wrong: none; a hammer near an earlier stop is quieter there.
18. Ruling: `HazardDamage.Fall` is appended (byte 3, verb "swallows") for the log of an enemy killed by a crevasse —
    cost: none.
19. Ruling: the floor 12 sweep greps `forced|cannot be|trimmed|clamped|kept only` as well (plan 9's ruling, kept) — cost:
    none.

## Global Constraints

- Defaults reproduce today byte for byte: with `Crevasses` false every floor's `ContentHash` is unchanged. Pinned by
  `RailLineTests.DefaultKnobs_BuildTodaysFloors` (must stay green in every task) and by `--rehash-plans` changing only
  `floor_12.json`'s `LayoutHash`.
- `CarveChasms` draws from no rng. The rail pass still draws only from `new Random(seed ^ 0x2A11)`.
- Room indices and order never change. Floor 12 must still generate `objective=jailbreak rails=6`, five rides each
  ≥ 160 m and a total ≥ 1200 m (plan 9 measured 267/206/281/252/209 m), and a cavern span.
- `Protocol.Version` stays **17** (the branch is unreleased). Chasm cells and bridges come from the shared seed; new
  state clients must see goes in existing messages (`CharacterDto`, `CartStateDto`, `ObjectiveStateDto`).
- Lethality never changes a fall: no crevasse code reads `TierRules.Lethality`.
- CC0 assets only (the repository is public). This plan adds no assets: meshes and materials are procedural, reusing the
  existing `weathered_planks` texture.
- No voice spend: new lines are subtitle-only (`"VoiceId": null`). Never run a Runware/voice bake.
- Hand-edited JSON keeps the game's escaping: `'` → `'`, `&` → `&`, `<` → `<`, `>` → `>`,
  `+` → `+`, non-ASCII → `\uXXXX` (upper-case hex), two-space indent. `git diff` must show only the values you meant
  to change.
- `tests/` (xUnit) is not in git. Write and run tests locally, never `git add tests/`. Commit only `src/`, `data/`,
  `docs/`, `scripts/`.
- Run `dotnet build DungeonCrawler.csproj` before any `godot --headless --path . -- <flags>` run. Engine flags such as
  `--headless --path .` go before `--`, and game flags go after it.
- `RIDE=1 scripts/net_test.sh` runs alone, with nothing else on the machine. It is flaky at baseline (about 1 run in 3–4):
  a failure in a step this plan did not touch is rerun once before it is investigated.
- Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N` (or `--quit-when-empty`).
- Full test suite: `dotnet test tests/DungeonCrawler.Tests.csproj`.
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No installers or dist builds (`export_all.sh`, `package.sh`, `publish_*.sh`), no push, no merge. The controller commits
  this plan; implementers commit only their task's files.

## Review Focus

- **A companion walking a plank or a lane beside a strip** (formation slots, a dodge, a set-aside from the cart): it must
  not step into the drop on its own. Expect bodies set down by code (`ExitCell`, set-aside, `Party.Teleport` formation)
  never to land on a chasm cell — pinned by `CrevasseRulesTests.ExitAndFormationCells_AreNeverDrops` (Task 6) and the
  crevasse flow's "nobody fell but the test's" check after every set-down (Tasks 6–8).
- **A fall that takes the last standing hero while a spare is left** (solo, the whole party seated): no spare may be spent
  and the wipe must rise at the stop the train left, not at the spare's — pinned by
  `CrevasseRulesTests.SpareAfterFall_OnlyWithAHeroStanding` (Task 7) and the flow's `spares used` check (Task 8).
- **Grisel in the falling train, then a wipe**: she must come back escorting, standing, as she was on arrival, and her
  quest must not fail — pinned by `RailArrivalTests.Merge_TakesTheTrainFromTheArrival` (Grisel's `NpcDto`, Task 8) and the
  flow's Grisel check (Task 8).
- **A remote player falling on their own screen** (their client's physics drops the body below the lip before the
  server's word arrives): the client must hold the body, not climb it back out — pinned by
  `CrevasseRulesTests.FellOut_OverADrop_Holds` (Task 6) and the RIDE tail step's client trace (Task 9).
- **A save or a co-op resync after a non-wipe fall** (wagons lost, crew lost, a hero fallen): nothing lost may reappear —
  pinned by `SaveRunFieldsTests.LostCages_RoundTrip` (Task 7) and the `=survivor` flow's re-capture check (Task 7).

---

### Task 1: `Cell.Chasm`, bridges and the `Crevasses` knob

**Files:**
- Modify: `src/Dungeon/Generation/Cell.cs` (append `Chasm = 7`)
- Modify: `src/Dungeon/Generation/FloorLayout.cs` (bridges, `IsDrop` helpers, `ToAscii`)
- Modify: `src/Dungeon/Generation/FloorSpec.cs` (after `RailBand`)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`SpecOverrides`)
- Modify: `src/Dungeon/Generation/FloorGenerator.cs` (`RaiseWalls`, `TryCarveSecretRoom`)
- Test: `tests/Dungeon/ChasmTests.cs` (new)

**Interfaces:**
- Produces:
  - `Cell.Chasm = 7` — not walkable (`IsWalkable` already lists only Floor/Door/Ramp), not rock.
  - `public enum BridgeKind : byte { Deck, Plank }` (in `Cell.cs`).
  - `FloorLayout.Bridges : Dictionary<GridPos, BridgeKind>` (public get), `public bool IsBridge(GridPos p)`,
    `public bool IsChasm(GridPos p) => this[p] == Cell.Chasm`, `public int ChasmCount { get; internal set; }`.
    Bridges are `Cell.Floor` cells over a drop. Neither is part of the content hash (the chasm cells are, as cells).
  - `FloorSpec.Crevasses : bool` (default false). `SpecOverrides.Crevasses : bool?` (`[AiIgnore]`, in `IsEmpty`,
    `Apply`: `Crevasses = Crevasses ?? spec.Crevasses`).
  - `RaiseWalls` walls a void cell 8-adjacent to a `Floor` **or `Chasm`** cell.
  - `TryCarveSecretRoom` refuses a room with a chasm cell in its 8-neighbourhood.
  - `ToAscii` draws `Chasm` as `~` and a bridge cell as `=`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Dungeon/ChasmTests.cs
[Fact] public void Crevasses_DefaultsOff() => Assert.False(FloorSpec.Default.Crevasses);

[Fact] public void SpecOverrides_CarryCrevasses()
{
    Assert.True(new SpecOverrides { Crevasses = true }.Apply(FloorSpec.Default).Crevasses);
    Assert.False(new SpecOverrides { Crevasses = false }.IsEmpty);
}

[Fact] public void AChasm_IsNotWalkable_AndIsWalledFromTheRock()
{
    var l = new FloorLayout(1, 7, 7);
    l[3, 3] = Cell.Chasm;
    FloorGenerator.RaiseWalls(l);
    Assert.False(l.IsWalkable(new GridPos(3, 3)));
    Assert.True(l.IsChasm(new GridPos(3, 3)));
    Assert.Equal(Cell.Wall, l[2, 2]);                     // every rock neighbour is walled, diagonals too
    Assert.Equal(Cell.Void, l[0, 0]);
}

[Fact] public void ToAscii_DrawsChasmAndBridges()
{
    var l = new FloorLayout(1, 3, 1);
    l[0, 0] = Cell.Chasm; l[1, 0] = Cell.Floor; l.Bridges[new GridPos(1, 0)] = BridgeKind.Plank;
    Assert.StartsWith("~=", l.ToAscii());
}
```

- [ ] **Step 2: Run to see them fail.** `dotnet test tests/DungeonCrawler.Tests.csproj --filter FullyQualifiedName~ChasmTests`.
  Expected: compile errors (`Crevasses`, `Cell.Chasm`, `Bridges`, `IsChasm` not defined).

- [ ] **Step 3: Implement** the Interfaces above. `AiDescription` for the override: "True: crevasses cut the floor:
  chasms beside the mine-cart line and plank crossings in the halls." `ToAscii`: a bridge cell's `=` wins over its `.`.

- [ ] **Step 4: Run** `--filter FullyQualifiedName~ChasmTests|FullyQualifiedName~RailLineTests|FullyQualifiedName~SecretRoomTests|FullyQualifiedName~OfflineDesignerTests|FullyQualifiedName~AiPlannerTests`.
  Expected: PASS (`DefaultKnobs_BuildTodaysFloors` included; the AI schema still hides the field).

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/Cell.cs src/Dungeon/Generation/FloorLayout.cs src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Dungeon/Generation/FloorGenerator.cs
git commit -m "feat(chasm): Cell.Chasm, bridge cells and the Crevasses knob (off by default); rock beside a chasm is walled

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: The cavern carved, and its rock box enforced

**Files:**
- Create: `src/Dungeon/Generation/FloorGenerator.Chasms.cs`
- Modify: `src/Dungeon/Generation/FloorGenerator.cs` (`Generate`: call `CarveChasms` on the line after `LayRails`)
- Modify: `src/Dungeon/Generation/FloorGenerator.Rails.cs` (`Foreign`, `PlaceRailPockets`, `CommitRails`)
- Modify: `src/Dungeon/Generation/FloorGenerator.Meander.cs` (`CavernBox`, `CavernReach` beside `CavernKeep`)
- Modify: `src/Dungeon/Generation/RailLine.cs` (doc of `Cavern`; `RailLine.CavernBreached`)
- Test: `tests/Dungeon/ChasmTests.cs`, `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Consumes: `Cell.Chasm`, `FloorLayout.Bridges`, `FloorSpec.Crevasses` (Task 1); `RailLine.Cavern`, `CavernWindow`,
  `RailSide` (plan 9).
- Produces:
  - `internal static IEnumerable<GridPos> CavernBox(IReadOnlyList<GridPos> window, int sign)` — for each window cell `c`
    and t in 3 .. 2 + `CavernDepth`: `c + RailSide(d, sign) * t`. `CavernReach` is the same for t in 2 .. 2 + `CavernDepth`.
  - Routing: `Foreign(...)` also holds `CavernBox` of every prior segment's `Cavern` window.
  - Pockets: `PlaceRailPockets` places no pocket on a `CavernReach` cell, and the window's own lane cells (party and
    enemy) count as covered (like a stop's cells).
  - `CommitRails`: when any party, enemy or pocket cell lies in the window's `CavernBox`, `Cavern = null` and
    `RailLine.CavernBreached = true` (an init bool, not hashed).
  - `internal static void CarveChasms(FloorSpec spec, FloorLayout layout)` — a no-op unless `spec.Crevasses`; this task
    carves the cavern: every `CavernReach` cell of `Rail.Cavern` that is rock or a pocket becomes `Cell.Chasm` (pockets
    there leave `Rail.Pockets`: `layout.Rail = layout.Rail with { Pockets = … }`, which keeps `Cavern`), every party cell of
    the span and its enemy twin (`party[i] + RailSide(d, sign)`) becomes a `BridgeKind.Deck` bridge, then `RaiseWalls`
    and `ChasmCount` set.
  - `internal static int LineSign(RailLine rail)` — the `sign` the line was routed with, recovered from its first straight
    party cell and its enemy twin (`RailSide(d, sign) == twin - cell`).

- [ ] **Step 1: Write the failing tests.** `ChasmTests` keeps its own `Gen(seed, obj, crevasses = true)` (plan 9's Long
  spec `with { Crevasses = crevasses }`, cached like `RailLineTests.Gen`) and `CrevasseSeeds()` (the first 60 sweep seeds
  × Jailbreak and None).

```csharp
// RailLineTests: a Crevasse row kind: Long + Crevasses
//   LineKind.Crevasse => SpecOf(LineKind.Long, obj) with { Crevasses = true }
[Theory, MemberData(nameof(LongSeeds))]
public void CavernBox_HoldsNoLineOrPocketCell(int seed, FloorObjectiveKind obj, LineKind kind)
{
    var l = Gen(seed, obj, kind);
    if (l.Rail is not { Cavern: { } c } rail) return;
    var window = rail.Party.Skip(c.From).Take(c.To - c.From + 1).ToList();
    var line = rail.Party.Concat(rail.Enemy).Concat(rail.Pockets).ToHashSet();
    var sign = FloorGenerator.LineSign(rail);
    Assert.DoesNotContain(FloorGenerator.CavernBox(window, sign), line.Contains);
}

[Fact] public void MostLongLines_HaveACavernSpan()   // plan 9's test, unchanged: still >= 80 % with the box enforced

// ChasmTests (Crevasse rows: the first 60 sweep seeds, Jailbreak and None)
[Theory, MemberData(nameof(CrevasseSeeds))]
public void TheCavern_IsCarved_AndItsLanesAreDecks(int seed, FloorObjectiveKind obj)
{
    var l = Gen(seed, obj);                                  // SpecOf(Long) with { Crevasses = true }
    if (l.Rail is not { Cavern: { } c } rail) return;
    for (var i = c.From; i <= c.To; i++)
    {
        Assert.Equal(BridgeKind.Deck, l.Bridges[rail.Party[i]]);
        Assert.True(l.IsWalkable(rail.Party[i]));
        var side = FloorGenerator.RailSide(rail.Party[c.From + 1] - rail.Party[c.From], FloorGenerator.LineSign(rail));
        Assert.True(l.IsBridge(rail.Party[i] + side));        // the enemy twin
        for (var t = 2; t <= 2 + RailRules.CavernDepth; t++) Assert.True(l.IsChasm(rail.Party[i] + side * t));
    }
    Assert.DoesNotContain(rail.Pockets, l.IsChasm);
}

[Fact] public void WithoutCrevasses_ALongLineHasNoChasm()
{
    foreach (var seed in SweepSeeds().Take(20))
        Assert.Equal(0, Gen(seed, FloorObjectiveKind.Jailbreak, crevasses: false).Cells.Count(c => c == Cell.Chasm));
}
```

  Amend `RailLineTests.EveryLaneCell_IsNearAPocket`: a lane cell inside the cavern window (either lane) is exempt, as a
  stop's cells are.

- [ ] **Step 2: Run to see them fail** (`--filter FullyQualifiedName~ChasmTests|FullyQualifiedName~CavernBox`).
  Expected: compile errors, then (once `CavernBox` exists) `CavernBox_HoldsNoLineOrPocketCell` fails on any seed whose
  last stretch or pockets cross the box — if none fails, the box was clear by luck; keep the enforcement anyway.

- [ ] **Step 3: Implement** the Interfaces above, in that order: `CavernBox`/`CavernReach`; `Foreign`; the pocket rule;
  the commit check; `CarveChasms` (cavern only). Never let a pocket rule make `PlaceRailPockets` return null for a line it
  accepted before: the window exemption is what keeps the enemy lane coverable.

- [ ] **Step 4: Run** `RailLineTests`, `RailMeanderTests`, `ChasmTests`, `FloorGeneratorTests`, `FloorSolvabilityTests`.
  Expected: PASS, `DefaultKnobs_BuildTodaysFloors` included.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorGenerator.Chasms.cs src/Dungeon/Generation/FloorGenerator.cs src/Dungeon/Generation/FloorGenerator.Rails.cs src/Dungeon/Generation/FloorGenerator.Meander.cs src/Dungeon/Generation/RailLine.cs
git commit -m "feat(chasm): the cavern is carved on Rail.Cavern with its lanes on a deck; its rock box is kept by the routing and the pockets and checked at commit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Strips beside the line, the gorge, and plank crossings in the halls

**Files:**
- Modify: `src/Dungeon/Generation/FloorGenerator.Chasms.cs`
- Modify: `src/Dungeon/Generation/RailLine.cs` (rules; `RailLine.Gorge`)
- Modify: `src/Dungeon/Generation/FloorLayout.cs` (`Walkways`)
- Modify: `src/Dungeon/Generation/FloorSolvability.cs`
- Modify: `src/Dungeon/Generation/LayoutReport.cs` (`Rail` line)
- Test: `tests/Dungeon/ChasmTests.cs`, `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Consumes: Task 2's `CarveChasms`, `CavernReach`.
- Produces:
  - `RailRules.StripMinCells = 4`, `StripMaxCells = 8`, `StripDepth = 2`, `StripPlatformGap = 4`,
    `GorgeFrom = 0.50f`, `GorgeTo = 0.85f`, `WalkwayCount = 3`, `WalkwayReach = 2`, `WalkwayRoomGap = 3`,
    `WalkwaySpacing = 8`.
  - `RailLine.Gorge : IReadOnlyList<RailSpan>` (init, default empty, not hashed): the party-index spans of the last ride's
    strips.
  - `FloorLayout.Walkways : List<GridPos>` — the plank cells, in choice order (also in `Bridges` as `Plank`).
  - A strip cell is eligible only if it is rock or a pocket of its own lane, level 0, outside every room, not
    `IsUnder`, not reserved, not a spawn/chest/key/trap/breakable/hazard cell, with only its own lane, its pockets, other
    strip cells or rock in its 8-neighbourhood; the lane cells beside it are level 0 and not ramps; and the cell one past
    the strip (t = −3 or t = 4) is rock (a wall behind).
  - Walkway plank `c`: a level-0 `Floor` cell outside rooms, `WalkwayRoomGap` cells from any room, not rail, door, ramp,
    water, reserved, a spawn of any kind, a hazard cell or a vault door; its walkable 4-neighbours are exactly the two
    opposite ones `a`, `b`, and `a`, `b` are straight too; the `WalkwayReach` cells each side across the corridor are rock
    with nothing walkable 8-adjacent but `c`. Chosen farthest-point (Manhattan) from `UpStairs`, then from those chosen,
    at least `WalkwaySpacing` apart, ties by (Y, X).
  - `FloorSolvability.Problems` gains, when `ChasmCount > 0`: with the rails blocked, every room (any cell), every chest,
    key and objective cell, and the down stairs that are reachable with the chasm cells read as floor must be reachable as
    the floor is ("the chasm cuts off room {i}" / "… chest at {c}"); and "a chasm on {what} at {c}" for any chasm cell that
    is a spawn, chest, key, objective-reserved or platform cell.
  - `LayoutReport.Rail` appends ` chasm={ChasmCount} strips={n} gorge=[{from}..{to}@{fFrom:0.00}-{fTo:0.00},…] planks={Walkways.Count}`
    (fractions of the last ride, platform middle to platform middle, as `rides~` counts) and `cavern=breached` when
    `CavernBreached`.

- [ ] **Step 1: Write the failing tests** (ChasmTests, Crevasse rows)

```csharp
[Theory, MemberData(nameof(CrevasseSeeds))]
public void Chasms_CutNothingOff(int seed, FloorObjectiveKind obj)
    => Assert.Empty(FloorSolvability.Problems(Gen(seed, obj)));

[Theory, MemberData(nameof(CrevasseSeeds))]
public void NoChasm_OnASpawnChestKeyObjectiveOrPlatformCell(int seed, FloorObjectiveKind obj)
{
    var l = Gen(seed, obj);
    var taken = l.EnemySpawns.Concat(l.ChestSpawns).Concat(l.KeySpawns).Concat(l.TrapSpawns).Concat(l.BreakableSpawns)
        .Concat(l.Objective?.Reserved ?? Enumerable.Empty<GridPos>()).Concat(l.Rail?.Stops.SelectMany(s => s.Platform) ?? Enumerable.Empty<GridPos>());
    Assert.DoesNotContain(taken, l.IsChasm);
    Assert.DoesNotContain(new[] { l.UpStairs, l.DownStairs }, l.IsChasm);
}

[Theory, MemberData(nameof(CrevasseSeeds))]
public void Strips_LieBesideTheLine_AndAreWalledBehind(int seed, FloorObjectiveKind obj)
// every chasm cell outside the cavern box is within StripDepth of a party-lane or enemy-lane cell of a straight run, or
// within WalkwayReach of a plank; each strip run is StripMinCells..StripMaxCells long (gorge runs >= 2); every cell 4-adjacent
// to a chasm is a chasm, a bridge, a lane, a pocket, a plank's corridor cell or a Wall

[Fact] public void MostCrevasseFloors_HaveAGorgeOnTheLastRide()
// of Crevasse lines with 6+ stops, >= 80 % have Rail.Gorge non-empty, every span inside GorgeFrom..GorgeTo of the last ride

[Theory, MemberData(nameof(CrevasseSeeds))]
public void Walkways_CrossOnAPlank(int seed, FloorObjectiveKind obj)
{
    var l = Gen(seed, obj);
    Assert.InRange(l.Walkways.Count, 0, RailRules.WalkwayCount);
    foreach (var p in l.Walkways)
    {
        Assert.Equal(BridgeKind.Plank, l.Bridges[p]);
        Assert.Equal(Cell.Floor, l[p]);
        var across = /* the corridor's perpendicular */;
        for (var t = 1; t <= RailRules.WalkwayReach; t++) { Assert.True(l.IsChasm(p + across * t)); Assert.True(l.IsChasm(p - across * t)); }
    }
}

[Fact] public void Problems_ReportsAChasmThatCutsOffARoom()   // a hand-built floor: two rooms joined by one corridor cell turned Chasm
    => Assert.Contains(FloorSolvability.Problems(l), p => p.Contains("chasm cuts off room"));

[Fact] public void Chasms_AreDeterministic()   // the same seed twice: equal ContentHash, equal Walkways, equal Gorge
[Fact] public void MostCrevasseFloors_HaveAPlank()   // >= 60 % of Crevasse floors have at least one plank
```

  Amend `RailLineTests.EveryLaneCell_IsNearAPocket` again: a lane cell with a chasm within 2 cells is exempt.

- [ ] **Step 2: Run to see them fail.** Expected: compile errors (`Gorge`, `Walkways`, the rules), then assertion
  failures on the strip and walkway tests.

- [ ] **Step 3: Implement** in `CarveChasms` after the cavern: per ride (party-index range between the two platforms,
  `StripPlatformGap` cells in from each), the eligible straight runs on the preferred side; carve as the ruling says;
  record the last ride's spans in `Gorge`; then the walkways. One `RaiseWalls` at the end; `ChasmCount` from the cells.
  The solvability check reads chasm cells as floor through a `Reachable` overload that takes an `open` predicate
  (`Func<GridPos, bool>? open`, cells it accepts count as walkable); add that overload rather than editing the cells.

- [ ] **Step 4: Run** `ChasmTests`, `RailLineTests`, `FloorSolvabilityTests`, `FloorGeneratorTests`, `LayoutKindTests`.
  Expected: PASS. If `MostCrevasseFloors_HaveAGorgeOnTheLastRide` fails, widen nothing silently: report the rate.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorGenerator.Chasms.cs src/Dungeon/Generation/RailLine.cs src/Dungeon/Generation/FloorLayout.cs src/Dungeon/Generation/FloorSolvability.cs src/Dungeon/Generation/LayoutReport.cs
git commit -m "feat(chasm): strips beside the line, a gorge on the last ride and plank crossings in the halls; FloorSolvability proves the chasms cut nothing off

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Floor 12 gets its crevasses (the gate)

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (floor 12 `SpecOverrides`)
- Modify: `data/adventures/builtin/floor_12.json` (`LayoutHash`; `r5_trestle` and `r5_jump` `At` only if Step 3 says so)
- Test: `tests/Adventure/RailEncounterPlanTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3; `--print-map`'s `[map] rail` line (Task 3).
- Produces: floor 12 on the shipped seed with the cavern carved, a gorge under `r5_trestle` and `r5_jump`, and its new
  `LayoutHash`. Tasks 5–9 run their headless checks on it.

- [ ] **Step 1: Turn it on.** In floor 12's `SpecOverrides` add `"Crevasses": true` after `"RailBand": 14`.

- [ ] **Step 2: Measure.** `dotnet build DungeonCrawler.csproj && godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --quit-after=6 2>&1 | grep -E "^\[map\] rail|\[DungeonBuilder\] floor 12|RailFailure"`.
  Expected, and pinned in the commit message:
  - `objective=jailbreak rails=6`;
  - `rides=` five rides each ≥ 160 m, total ≥ 1200 m (the line may move a little from plan 9's 267/206/281/252/209 m
    because the box is now enforced);
  - `cavern=` a span of ≥ 10 cells (not `none`, not `breached`);
  - `chasm=` > 0, `strips=` ≥ 3, `gorge=` at least one span, `planks=` ≥ 1.
  If the cavern is gone or breached, or a ride falls under 160 m, stop and report the `[map]` lines. Do not pick another
  seed. Save the ASCII map to the scratchpad.

- [ ] **Step 3: Put the trestle and the jump over the gorge.** From the `gorge=` fractions: the trestle spans
  `At ± 6 m / ride length` and the jump `At ± 2.5 m / ride length`; each must lie inside one gorge span with 0.02 of
  margin at both ends (the cell fractions and the track's metres differ by corner arcs). If today's `At 0.6` and
  `At 0.75` already fit, leave them. Else move each to the nearest fitting fraction, keeping the trestle before the jump
  and both before `r5_spur` (0.85) and after `r5_pour` (0.4). If no span fits one of them, stop and report.

- [ ] **Step 4: Rehash and prove the other floors unchanged.**
  `godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`, then `git diff --stat data/`.
  Expected: only `campaign.json` and `floor_12.json`, and in `floor_12.json` only `LayoutHash` (and the two `At` values if
  moved).

- [ ] **Step 5: Sweep floor 12, then every floor.**
  `timeout 90 godot --headless --path . -- --builtin --floor=12 --quit-after=6 --no-ambush --no-portals --no-gathering 2>&1 | grep -iE "repaired|dropped|demoted|adapted|forced|cannot be|trimmed|clamped|kept only"`
  (expected: no output but the known `could not place: npc npc_grisel: waits on flag grisel_forgiven`), then the docs'
  20-floor loop and `timeout 120 godot --headless --path . -- --builtin --quit-after=4 2>&1 | grep -c adapted` (expected `0`).
  Then `timeout 300 godot --headless --path . -- --test-vault-puzzle 2>&1 | grep -E "\[vault\] (PROBLEM|RESULT)"`:
  expected no `PROBLEM` line and `RESULT ok` (FloorSolvability over every story floor, floor 12 with its chasms).

- [ ] **Step 6: Pin the data.** In `RailEncounterPlanTests`, `BuiltinFloor12_HasTheFiveRides` (plan 9's) keeps its
  assertions; add `Assert.True(trestle.At < jump.At && pour.At < trestle.At && jump.At < spur.At)` for the ride-5 pieces.
  Run `--filter FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~CrewPlanTests|FullyQualifiedName~GulletCampaignTests|FullyQualifiedName~StoryRewireTests`.
  Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add data/adventures/builtin/campaign.json data/adventures/builtin/floor_12.json
git commit -m "feat(floor12): crevasses on: the cavern on ride 4, strips beside the line, a gorge under the trestle and the jump, plank crossings

(measured: rides …, cavern …, chasm … strips … gorge … planks …)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: The look of a crevasse, and every cue range clamped at every stop

**Files:**
- Create: `src/Dungeon/ChasmDressing.cs`
- Modify: `src/Dungeon/DungeonBuilder.cs` (`BuildGridMap`: the `Chasm` case and bridge cells; `Build`: add the dressing)
- Modify: `src/Dungeon/Rails/Core/SetPieceRules.cs` (`CueRangeNearest`)
- Modify: `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (`CueRangeAt`)
- Test: `tests/Dungeon/Rails/RideSoundTests.cs`, `tests/Dungeon/ChasmTests.cs`

**Interfaces:**
- Consumes: `FloorLayout.IsChasm`, `Bridges`, `ChasmCount` (Task 1); floor 12 with chasms (Task 4).
- Produces:
  - `ChasmDressing : Node3D` with `public static ChasmDressing Build(FloorLayout layout, Func<GridPos, Vector3> surface, float cellSize, Material plank)`
    and `public int Regions { get; }`: for each 4-connected region of chasm-or-bridge cells, one `ArrayMesh` of shaft faces
    (every edge between a region cell and a non-region cell, from that neighbour's floor down to
    `ChasmDressing.DrawDepth` = 30 m below, a near-black rock material), an emissive ember floor at the bottom
    (unshaded orange noise) and one `OmniLight3D` (range 18 m, energy 1.5, no shadow) 3 m above it, and two mist layers
    (unshaded, alpha 0.35 and 0.2, a `NoiseTexture2D` whose UV offset drifts in `_Process`) at 4 m and 10 m below the lip;
    a deck (`BridgeKind.Deck`) is a full-cell plank slab 0.2 m thick with a `StaticBody3D` box (layer 1) whose top is the
    cell's floor, with a trestle bent under every deck cell down to 12 m; a plank (`BridgeKind.Plank`) is a 1.6 m wide
    slab along its corridor with the same collision rule.
  - `BuildGridMap`: a `Chasm` cell gets its ceiling only (as a `Pit`); a bridge cell gets no `ItemFloor`.
  - `SetPieceRules.CueRangeNearest(IEnumerable<float> metresToEachStop)` = `CueRange(min)` (`MaxCueRange` when empty).
  - `CueRangeAt(at)` passes the distance from `at` to every stop's point (`Party.Track.Point(StopS[k])` for all k).
  - `public const float ChasmDressing.DrawDepth = 30f` (how deep the drop is drawn; a body is judged fallen long before).

- [ ] **Step 1: Write the failing tests**

```csharp
// RideSoundTests
[Fact] public void CueRange_ClampsAgainstTheNearestStop()
{
    Assert.Equal(SetPieceRules.CueRange(5f), SetPieceRules.CueRangeNearest(new[] { 80f, 5f, 40f }), 4);
    Assert.Equal(SetPieceRules.MaxCueRange, SetPieceRules.CueRangeNearest(Array.Empty<float>()), 4);
    Assert.Equal(0f, SetPieceRules.CueRangeNearest(new[] { 1f, 200f }), 4);   // inside the margin of any stop: silent
}
```

- [ ] **Step 2: Run to see it fail** (`--filter FullyQualifiedName~RideSoundTests`). Expected: compile error.

- [ ] **Step 3: Implement** the Interfaces above. `ChasmDressing` is built only when `layout.ChasmCount > 0` and is
  skipped under `--headless` meshes as the rest of the dressing is (follow how `WaterSurface` is added). The plank
  material is the builder's existing `Material("weathered_planks", …)` as the water's planks use. The dressing prints
  `[chasm] regions={Regions} decks={n} planks={n}` once on build.

- [ ] **Step 4: Run** the test filter, then `dotnet build DungeonCrawler.csproj && timeout 90 godot --headless --path . -- --builtin --floor=12 --quit-after=6 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "\[chasm\]|ERROR|Unhandled"`.
  Expected: PASS; one `[chasm] regions=` line with regions ≥ 3, decks ≥ 20, planks ≥ 1; no `ERROR` or `Unhandled`.
  Then `--test-ride` and `--test-chase` (both must still print their `RESULT ok`: the hammers' and the pour's cue ranges
  changed).

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/ChasmDressing.cs src/Dungeon/DungeonBuilder.cs src/Dungeon/Rails/Core/SetPieceRules.cs src/Dungeon/Rails/RailEncounterController.Kiln.cs
git commit -m "feat(chasm): a dark drop with mist and an ember glow far below, a deck over the cavern and planks in the halls; a hazard's cue range ends short of every stop

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: A body that falls in

**Files:**
- Create: `src/Dungeon/CrevasseRules.cs`, `src/Dungeon/CrevasseWatch.cs`, `src/Core/DebugTools.Crevasse.cs`
- Modify: `src/Characters/CharacterState.cs` (`Fallen`; `FullRestore` clears it), `src/Save/Core/SaveDtos.cs`
  (`CharacterDto.Fallen`), `SaveMapper` (where `Mangled` is mapped)
- Modify: `src/Party/Adventurer.cs` (`FallIntoCrevasse`, `ReturnFromCrevasse`, the physics guard, `FellOut`)
- Modify: `src/Party/PartyController.cs` (`AutoRevive` skips the fallen; `Teleport`'s formation check reads `SetDownOk`)
- Modify: `src/Dungeon/Rails/RailController.cs` (the exit-cell predicate in `ExitCellNear` reads `SetDownOk`)
- Modify: `src/Dungeon/HazardRules.cs` (`HazardDamage.Fall`, its verb)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`BarkTrigger.Crevasse`), `src/Adventure/Core/Voice/RideBarks.cs`,
  `src/Adventure/Core/Voice/VoiceLines.cs` (its delivery)
- Modify: `src/Characters/PortraitText.cs`: "Fallen" instead of "Down"
- Modify: `src/Dungeon/DungeonBuilder.cs` (add `CrevasseWatch` beside the dressing, server/solo; `WalkableNeighbor` reads `SetDownOk`)
- Modify: `src/Core/DebugTools.cs` (the `--test-crevasse` flag: read its current text first)
- Test: `tests/Dungeon/CrevasseRulesTests.cs` (new), `tests/Save/SaveRunFieldsTests.cs`, `tests/Net/TrafficTests.cs`
  (where `MemberSync` is pinned), `tests/Party/MangledRulesTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Interfaces:**
- Consumes: `FloorLayout.IsChasm`, `IsBridge`, `IsRail`, `IsRailPocket`.
- Produces:
  - `public enum FallBody { Hero, Crew, Escort, OtherNpc, Enemy }`, `public enum FallFate { Dead, Lost, CaughtAtLip, Dies }`.
  - `public static class CrevasseRules` (engine-free): `FallDepth = 2.5f`, `HoldDepth = 6f` (above `Adventurer.FellOutY`, so a held body never trips it),
    `ReturnHpFraction = 0.25f`, `WarnCells = 2`, `LipReach = 3`, `LipSearch = 6`, `OverReach = 2`;
    `bool IsDrop(FloorLayout l, GridPos c)` (chasm or bridge); `bool Fell(FloorLayout l, GridPos c, float y, float floorY)`
    (`IsDrop && y < floorY - FallDepth`); `FallFate FateOf(FallBody b)` (Hero → Dead, Crew → Lost, Escort → CaughtAtLip,
    OtherNpc → Lost, Enemy → Dies); `GridPos? LipCell(FloorLayout l, GridPos near)` (Ruling 12);
    `bool OverCrevasse(FloorLayout l, GridPos laneCell)` (Ruling 7); `bool SpareAfterFall(int standingHeroes) => standingHeroes > 0`;
    `bool SetDownOk(FloorLayout l, GridPos c) => l.IsWalkable(c) && !IsDrop(l, c)` — read by every place code sets a body
    down: `DungeonBuilder.WalkableNeighbor`, `PartyController.Teleport`'s formation check, and `RailController`'s exit-cell
    predicate (a plank is walkable but never a place to put someone).
  - `CharacterState.Fallen : bool`; `CharacterDto.Fallen : bool`, mapped both ways.
  - `Adventurer.FallIntoCrevasse(Vector3 hold)`: leaves any seat (a remote's is told, as `Teleport` does), `Fallen = true`,
    `Hp = 0`, `Mangled = false`, effects cleared, `LastHarm = new HarmSource(HarmCauses.Generic, "The crevasse", …)`, logs
    "{name} falls into the crevasse!", plays `fall`, raises `MemberChanged` and the downed path (`OnDowned`), then hides
    the body (not visible, collision layer 0) at `hold`. `_PhysicsProcess` returns at once while `State.Fallen` (every
    role). `FellOut()` over a drop cell (`CrevasseRules.IsDrop`) zeroes the velocity and returns instead of teleporting.
  - `Adventurer.ReturnFromCrevasse(Vector3 at)`: `Fallen = false`, `Hp = MaxHp × ReturnHpFraction`, effects cleared,
    `Revive()`, visible, then `Teleport(at)`; on a server a Remote body also gets the ForcePose (`Shove(at)` semantics).
    Logs "{name} climbs back out of the dark."
  - `AutoRevive` and `TryRescue` never act on a `Fallen` member. `MangledRules.IsWipe` is unchanged (a fallen hero is
    downed).
  - `HazardDamage.Fall = 3`; `HazardRules.Verb(Fall) = "swallows"`.
  - `CrevasseWatch : Node` (server and solo; `Instance`): each physics tick judges every non-parked hero, every enemy
    `{ IsAlive, Carried: false }` and every NPC `{ Dead: false, Seated: false }` on the floor root with
    `CrevasseRules.Fell`, and calls `public void Take(Node3D body, Vector3 at)`, which applies `FateOf`: a hero
    `FallIntoCrevasse(hold)` (hold = the fall cell's surface − `HoldDepth`); crew `Die()`; an escort placed on `LipCell`
    and `ReceiveHazard(HazardDamage.Fall, new HazardOutcome(false, 1f, 0f), "The crevasse", -1)` (downed, revivable,
    still escorting); another NPC `Die()`; an enemy `KillByEnvironment("The crevasse", HazardDamage.Fall)`. The same
    `Take` serves the train's fall (Task 7). Prints `[crevasse] {who} fell at {cell}: {fate}`.
  - The warning: a hero on foot within `WarnCells` of a chasm region not yet warned this floor visit makes the nearest
    companion within 12 m (else that hero) say `BarkTrigger.Crevasse` through `AdventureDirector.CallOut`, logging
    "Mind the edge — that drop has no bottom." when nobody speaks. `RideBarks` gets a `Crevasse` bank (subtitle only).
  - `--test-crevasse` (with `=trestle`, `=survivor`; `_quitAfter = 300`, floor 12, `--builtin`, lethality 3 as the ride
    flows, no ambush/portals/gathering): this task writes steps A1–A3 (below) and `RESULT ok` after them; Tasks 7 and 8 add
    steps after A3.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Dungeon/CrevasseRulesTests.cs
[Theory]
[InlineData(FallBody.Hero, FallFate.Dead)] [InlineData(FallBody.Crew, FallFate.Lost)] [InlineData(FallBody.Escort, FallFate.CaughtAtLip)]
[InlineData(FallBody.OtherNpc, FallFate.Lost)] [InlineData(FallBody.Enemy, FallFate.Dies)]
public void FateOf_IsTheSameAtEveryLethality(FallBody body, FallFate fate) => Assert.Equal(fate, CrevasseRules.FateOf(body));

[Fact] public void Fell_OnlyBelowTheLipOfADrop()
{
    var l = Strip();   // 9x5: a floor row y=2, a chasm row y=3, a plank at (4,2) over a chasm column
    Assert.False(CrevasseRules.Fell(l, new GridPos(2, 3), -2.4f, 0f));
    Assert.True(CrevasseRules.Fell(l, new GridPos(2, 3), -2.6f, 0f));
    Assert.True(CrevasseRules.Fell(l, new GridPos(4, 2), -2.6f, 0f));     // off the plank's side
    Assert.False(CrevasseRules.Fell(l, new GridPos(2, 2), -9f, 0f));      // solid floor: FellOut's business
}

[Fact] public void LipCell_PrefersAPocketOrFloorOffTheLane()
[Fact] public void OverCrevasse_IsABridgeOrAChasmWithinTwo()
[Fact] public void SpareAfterFall_OnlyWithAHeroStanding() { Assert.False(CrevasseRules.SpareAfterFall(0)); Assert.True(CrevasseRules.SpareAfterFall(1)); }
[Fact] public void ExitAndFormationCells_AreNeverDrops()   // CartSeats.ExitCell with CrevasseRules.SetDownOk over Strip(): never the plank, never a chasm
[Fact] public void FellOut_OverADrop_Holds()               // the rule the guard reads: CrevasseRules.IsDrop on the chasm and the plank, false on floor

// tests/Party/MangledRulesTests.cs
[Fact] public void FullRestore_ClearsFallen() { var c = /* a member */; c.Fallen = true; c.FullRestore(); Assert.False(c.Fallen); }
// tests/Save/SaveRunFieldsTests.cs
[Fact] public void Fallen_RoundTrips()                 // SaveMapper: CharacterState.Fallen -> CharacterDto.Fallen -> back
// tests/Net/TrafficTests.cs
[Fact] public void Signature_DiffersOnFallen()         // MemberSync.Signature of two sheets that differ only in Fallen
// tests/Adventure/RideBarksTests.cs
[Fact] public void EveryClass_HasACrevasseLine()          // TopUp fills BarkTrigger.Crevasse for every class, VoiceId null
```

- [ ] **Step 2: Run to see them fail** (`--filter FullyQualifiedName~CrevasseRulesTests|FullyQualifiedName~MangledRulesTests|FullyQualifiedName~SaveRunFieldsTests|FullyQualifiedName~TrafficTests|FullyQualifiedName~RideBarks`).
  Expected: compile errors.

- [ ] **Step 3: Implement** the Interfaces above. Then the flow's steps (`src/Core/DebugTools.Crevasse.cs`, printing
  `[crevasse]` lines; `Fail` as the watchdog flow does):
  - **A1 hero:** 1.5 s in, a companion is put over the nearest chasm cell beside a walkable cell, 0.5 m above its lip.
    Within 3 s: `State.Fallen`, `Downed`, not visible. After 8 s with no enemy near: still `Fallen` (no auto-revive).
  - **A2 enemy:** before the enemies are culled, a living non-carried enemy is put over a chasm cell: within 3 s
    `!IsAlive`, and the log has "The crevasse swallows".
  - **A3 crew and Grisel:** one crew member (taken out of their wagon with `GetOut`) and Grisel are each put over a chasm
    cell: within 3 s the crew member is `Dead` (the crew HUD counts them lost) and Grisel is `Downed`, not `Dead`, still
    `Escorting`, on a walkable cell within `LipReach` of a chasm; `TryRevive()` stands her up.
  - After every step: no other hero is `Fallen` ("nobody fell but the test's").

- [ ] **Step 4: Run** the test filter, then
  `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --test-crevasse 2>&1 | grep -E "\[crevasse\]|ERROR|Unhandled"`.
  Expected: PASS; A1–A3 lines and `[crevasse] RESULT ok`; no `ERROR`. Then `--test-ride` and `--test-crew`:
  `RESULT ok` (nothing on the line ever lands in a drop).

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/RailController.cs src/Dungeon/CrevasseRules.cs src/Dungeon/CrevasseWatch.cs src/Core/DebugTools.Crevasse.cs src/Core/DebugTools.cs src/Characters/CharacterState.cs src/Save src/Party/Adventurer.cs src/Party/PartyController.cs src/Dungeon/HazardRules.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Dungeon/DungeonBuilder.cs
git commit -m "feat(crevasse): a fall kills at any lethality: a hero is dead until a stop, crew are lost, enemies die, an escort is caught at the lip; a companion warns at the edge

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(and `git add src/Characters/PortraitText.cs`.)

### Task 7: The train falls, and the fallen come back at a stop

**Files:**
- Create: `src/Dungeon/Rails/Core/FallenReturn.cs`
- Modify: `src/Dungeon/Rails/RailController.cs` (`Fall`, `Wreck(bool fall)`, `Plunge`, `Arrive`, the tick, `Restore`)
- Modify: `src/Dungeon/Rails/RailController.Jump.cs` (`OverCrevasse`; the jump's drop-in)
- Modify: `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (`TickTrestle`)
- Modify: `src/Dungeon/Rails/RailEncounterController.Leaps.cs` (boarders on a fall die)
- Modify: `src/Dungeon/Rails/CageWagon.cs` (`Lost`, `SetLost`), `src/Adventure/Objectives/CrewRescueController.cs`
  (lost wagons in `Capture`/`Restore`/`ApplyNet`; `Couple` clears `Lost`)
- Modify: `src/Save/Core/SaveDtos.cs` (`ObjectiveStateDto.LostCages`), `src/Net/Core/CartNet.cs` (`CartStateDto.Fell`),
  `src/Dungeon/Rails/RailController.Net.cs` (send `Fell`; the client's plunge)
- Modify: `src/Core/DebugTools.Crevasse.cs`
- Test: `tests/Dungeon/Rails/FallenReturnTests.cs` (new), `tests/Save/SaveRunFieldsTests.cs`, `tests/Net/CartNetTests.cs`

**Interfaces:**
- Consumes: `CrevasseWatch.Take`, `CrevasseRules.OverCrevasse`, `SpareAfterFall`, `Adventurer.ReturnFromCrevasse` (Task 6).
- Produces:
  - `public void RailController.Fall(string why)` — server only; a no-op while `Wrecked`. Logs `why`, `Falls++`,
    `FellAt = Cart.GlobalPosition`, then `Wreck(fall: true)`: as `Wreck`, but `Plunge()` instead of `Tumble()`; every
    rider, every coupled wagon's riders and every hound aboard the cart or a wagon's roof go to `CrevasseWatch.Take` (at
    the cart's or wagon's place); each coupled wagon `SetLost(true)` and the train is uncoupled; then
    `SpareTakesOver()` only when `SpareAfterFall(standing heroes)`, else `HideWreck()` and the log "The train is gone into
    the dark." Prints `[rails] the train falls at S={S:0.0} ({why}): {n} rider(s), {w} wagon(s)`.
  - `public bool RailController.LastWreckFell { get; }` (true from a fall until the next takeover or `Restore`);
    `public int Falls { get; }`; `public Vector3? FellAt { get; }`.
  - `public bool RailController.OverCrevasse(float from, float to)` — any party-lane cell under S in [from, to]
    (sampled every 1 m through `Party.Track.Point` → `WorldToCell`) is `CrevasseRules.OverCrevasse`.
  - The tick: a jump's drop-in over a crevasse (`OverCrevasse(GapFrom, GapTo)`) calls `Fall("The cart drops into the
    crevasse!")` instead of letting the plating wreck it; `TickTrestle`'s catch calls
    `Fall("The trestle gives way into the crevasse!")` when `OverCrevasse(trestle.From, trestle.To)`, else `Catch` as
    today. The ride's build prints `[rails] {piece} over a crevasse: {bool}` for every jump and trestle.
  - `Plunge()`: the cart's and each coupled wagon's body copied onto cosmetic rigid bodies (layer 0, mask 0: nothing
    holds them), thrown toward the nearest chasm cell and down, freed after 4 s; `cart_wreck` once at the lip.
  - `CartStateDto.Fell : bool` — the party cart's `LastWreckFell`. A client, on the Wrecked rising edge with `Fell`, plays
    `Plunge()` for the cart and the wagons of the last `Train` it was told, and hides those wagons; a first state (a
    catch-up) only hides them.
  - `CageWagon.Lost : bool`, `SetLost(bool)` (hidden, collision 0; `false` shows it again). `ObjectiveStateDto.LostCages :
    List<int>?` (stops); `Capture`, `Restore` and `ApplyNet` carry it; a lost wagon never offers a prompt.
  - `public sealed class FallenReturn` (engine-free):
    `void Baseline(IReadOnlyList<int> platformStopOfEach)` (each hero's platform stop at the fall, −1 off any);
    `int Tick(IReadOnlyList<(bool Living, bool Seated, int PlatformStop)> heroes, int arrivedStop)` — returns
    `arrivedStop` when ≥ 0, else the platform stop of the first living, unseated hero whose stop changed to ≥ 0 since its
    last tick, else −1.
  - `RailController` runs it each tick while any hero is `Fallen` (`arrivedStop` set by `Arrive()` only, not by a
    spare's takeover); a return puts each fallen hero on its own `ExitCellNear` beside the cart (cart arrival) or beside
    that hero (platform), with `ReturnFromCrevasse`. `Restore(...)` returns every fallen hero beside the cart.
  - The flow (`--test-crevasse`):
    - **A4 return:** after A3, the party boards at stop 0 with the A1 companion still fallen; on ride 1's arrival at stop
      1, within 1 s, that companion is up, not `Fallen`, HP ≈ 25 % (± 1), within 6 m of the cart.
    - **B the fall** (default): every cage waived (`RideWaiveCages`), one wagon coupled (open stop 1's cage and couple it
      through the crew controller's existing path or a `DebugCouple(int stop)` hook added beside it), then
      `rc.Restore(LastRideStop)`; board everyone with Grisel aboard; `PumperCap = 0`; the cart reaches the jump too slow
      and `[rails] the train falls` prints; within 1 s every rider is `Fallen`, the coupled wagon is `Lost`, its crew
      `Dead`, Grisel downed on a lip cell; `SparesUsed` unchanged.
    - **`=trestle`:** as B, but the human brakes from the trestle's `From` until it falls with the cart on it.
    - **`=survivor`:** as B, but the human gets out at the stop as the cart departs (the relief flow's way) and stays
      on foot; after the fall nobody wipes; `A spare cart waits at stop N` (N ≥ 1) or the relief applies; the crew who fell
      stay `Dead`; the human is put on stop N's platform and the fallen companions come back beside it; a fresh
      `FloorStateCapture.Capture` names the lost wagon in `LostCages` and the fallen crew in `LostCrew`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Dungeon/Rails/FallenReturnTests.cs
[Fact] public void AnArrival_ReturnsThemThere()
    => Assert.Equal(3, new FallenReturn().Tick(new[] { (true, true, -1) }, arrivedStop: 3));

[Fact] public void SteppingOntoAPlatform_ReturnsThemThere()
{
    var r = new FallenReturn();
    r.Baseline(new[] { -1, -1 });
    Assert.Equal(-1, r.Tick(new[] { (true, false, -1), (false, false, -1) }, -1));
    Assert.Equal(2, r.Tick(new[] { (true, false, 2), (false, false, -1) }, -1));
}

[Fact] public void StandingOnAPlatformAtTheFall_DoesNotCount()
{
    var r = new FallenReturn();
    r.Baseline(new[] { 2 });
    Assert.Equal(-1, r.Tick(new[] { (true, false, 2) }, -1));       // already there: step off and back
    r.Tick(new[] { (true, false, -1) }, -1);
    Assert.Equal(2, r.Tick(new[] { (true, false, 2) }, -1));
}

[Fact] public void ASeatedOrDeadHero_DoesNotCount()
    => Assert.Equal(-1, new FallenReturn().Tick(new[] { (true, true, 1), (false, false, 1) }, -1));

// tests/Save/SaveRunFieldsTests.cs
[Fact] public void LostCages_RoundTrip()   // ObjectiveStateDto { LostCages = [2] } through the save JSON and back: [2]
// tests/Net/CartNetTests.cs
[Fact] public void CartStateDto_CarriesFell()   // through NetCodec: Fell true survives; default false
```

- [ ] **Step 2: Run to see them fail** (`--filter FullyQualifiedName~FallenReturnTests|FullyQualifiedName~SaveRunFieldsTests|FullyQualifiedName~CartNetTests`).
  Expected: compile errors.

- [ ] **Step 3: Implement** the Interfaces above, then the flow's steps A4, B and the two variants.

- [ ] **Step 4: Run** the filter, then each of
  `timeout 300 godot --headless --path . -- --test-crevasse`, `--test-crevasse=trestle`, `--test-crevasse=survivor`
  `2>&1 | grep -E "\[crevasse\]|\[rails\] (the train falls|.* over a crevasse)|ERROR|Unhandled"`.
  Expected: A1–A4 and B lines; `[rails] r5_jump over a crevasse: True` and `[rails] r5_trestle over a crevasse: True`;
  `[rails] the train falls`; `RESULT ok` for all three (the default and `=trestle` stop at the wipe: Task 8 asserts what
  follows, so this task's `RESULT ok` prints right after B). Then `--test-chase`, `--test-cart-watchdog`, `--test-relief`:
  `RESULT ok` (a jump made over the gorge is still a jump; one missed off a crevasse still wrecks).

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails src/Adventure/Objectives/CrewRescueController.cs src/Save/Core/SaveDtos.cs src/Net/Core/CartNet.cs src/Core/DebugTools.Crevasse.cs
git commit -m "feat(rails): a train over a crevasse that misses its jump or loses its trestle falls with every rider and wagon (RailController.Fall); the fallen come back at the next stop

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: A wipe rises at the last stop, with the train as it arrived

**Files:**
- Create: `src/Save/Core/RailArrival.cs`
- Modify: `src/Dungeon/Rails/RailController.cs` (`ArrivalSnapshot`, `RiseBesideCart`)
- Modify: `src/Core/Main.cs` (`OnPartyWiped`, `RebuildFloorInner`)
- Modify: `src/Adventure/Objectives/CrewRescueController.cs` (`TrainNpcIds`)
- Modify: `src/Core/DebugTools.Crevasse.cs`
- Test: `tests/Save/RailArrivalTests.cs` (new)

**Interfaces:**
- Consumes: `RailController.Fall`, `LostCages` (Task 7); `FloorStateCapture.Capture/Apply`, `WipeRules`.
- Produces:
  - `public static FloorStateDto RailArrival.Merge(FloorStateDto live, FloorStateDto arrival, IReadOnlySet<string> trainNpcIds)`
    — a new instance (a JSON round-trip copy of `live`; neither input changes) with Ruling 14's fields taken from
    `arrival`: `RailStop`; `RailRiders = null`; the `NpcStates` entries whose `Id` is in `trainNpcIds` (added if live
    lacks them); the objective's `OpenCages`, `CoupledCages`, `HomeCrew`, `LostCrew`, `FreedNpcs`, `LostCages`.
    Everything else is `live`'s, `SparesUsed` included.
  - `public FloorStateDto? RailController.ArrivalSnapshot { get; }` — server/solo: captured with
    `FloorStateCapture.Capture` one frame after each flushed `Arrived` or `Restored` event (`CallDeferred`); prints
    `[rails] arrival snapshot at stop {StopIndex}: train [{stops}] npcs alive {n}`.
  - `public IReadOnlySet<string> CrewRescueController.TrainNpcIds` — every crew id and `GriselId`.
  - `Main.OnPartyWiped`: after `CaptureCurrentFloor()`, when the floor's `RailController` has an `ArrivalSnapshot` and the
    party rises, `game.Floors[floor] = RailArrival.Merge(...)` (an empty id set without a crew controller) and
    `_riseAtCart = true`. `RebuildFloorInner`: after the stairs `Party.Teleport`, when `_riseAtCart`, it is cleared and
    `RailController.Instance?.RiseBesideCart()` is called deferred (so it lands after `Restore`'s deferred `ClearRiders`).
  - `public int RailController.RiseBesideCart()` — every non-parked hero, **remote ones too**, and every escorting NPC on
    its own `ExitCellNear(Cart, atStop: true, useTaken: true)` cell, facing the cart; returns the heroes placed and
    prints `[rails] wipe-rise: {n} hero(es) beside the cart at stop {StopIndex}`. No spare is spent (it does not touch
    `_spares`).
  - The flow: after B (default and `=trestle`), **C the rise** — within 10 s of `The party has fallen`: the floor rebuilt;
    `rc.StopIndex == LastRideStop`; the cart `Held`, plating `MaxPlating`, visible; `rc.Train` stops equal the snapshot's
    `CoupledCages`; no crew member who was alive at the snapshot is `Dead`; Grisel standing and `Escorting`; every
    non-parked hero not `Fallen`, standing within 6 m of the cart; `SparesUsed` as before the ride; the party's gold is
    `WipeRules.GoldAfterWipe(before)`; the Scar is on. Then `RESULT ok`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Save/RailArrivalTests.cs
[Fact] public void Merge_TakesTheTrainFromTheArrival()
{
    var arrival = Dto(stop: 4, coupled: new[] { 1, 2 }, lost: new string[0], npcs: new[] { Npc("crew_hob", dead: false), Npc("npc_grisel", dead: false, escorting: true) });
    var live = Dto(stop: 4, coupled: new int[0], lost: new[] { "crew_hob" }, npcs: new[] { Npc("crew_hob", dead: true), Npc("npc_grisel", dead: false, escorting: true, downed: true) });
    live.DeadEnemies.Add(7); live.SparesUsed = new() { 2 }; arrival.Objective!.LostCages = new(); live.Objective!.LostCages = new() { 1, 2 };
    var m = RailArrival.Merge(live, arrival, new HashSet<string> { "crew_hob", "npc_grisel" });
    Assert.Equal(new[] { 1, 2 }, m.Objective!.CoupledCages);
    Assert.Empty(m.Objective.LostCrew!);
    Assert.Empty(m.Objective.LostCages!);
    Assert.False(m.NpcStates.Single(n => n.Id == "crew_hob").Dead);
    Assert.False(m.NpcStates.Single(n => n.Id == "npc_grisel").Downed);
    Assert.Equal(new[] { 7 }, m.DeadEnemies);            // the dead stay dead
    Assert.Equal(new[] { 2 }, m.SparesUsed);             // no spare given back, none spent
    Assert.Null(m.RailRiders);
    Assert.True(live.NpcStates.Single(n => n.Id == "crew_hob").Dead);   // inputs untouched
}

[Fact] public void Merge_KeepsLiveNpcsOffTheTrain()   // an NpcDto not in trainNpcIds keeps live's values
[Fact] public void Merge_WithNoObjective_TakesOnlyTheStop()
```

- [ ] **Step 2: Run to see them fail** (`--filter FullyQualifiedName~RailArrivalTests`). Expected: compile error.

- [ ] **Step 3: Implement** the Interfaces above, then step C of the flow.

- [ ] **Step 4: Run** the filter, then `--test-crevasse` and `--test-crevasse=trestle` (as in Task 7).
  Expected: the C lines, `[rails] arrival snapshot at stop 4`, `[rails] wipe-rise: … at stop 4` (or the measured last
  ride's stop), `RESULT ok` for both; `=survivor` still `RESULT ok`. Then `--test-ride`, `--test-crew`, `--test-chase`:
  `RESULT ok`. Then `--test-gas-wipe` (an ordinary floor's wipe still rises at the stairs): the same `[gaswipe]` result as on the commit before this task.

- [ ] **Step 5: Commit**

```bash
git add src/Save/Core/RailArrival.cs src/Dungeon/Rails/RailController.cs src/Core/Main.cs src/Adventure/Objectives/CrewRescueController.cs src/Core/DebugTools.Crevasse.cs
git commit -m "feat(rails): a wipe on the line rises at the last stop reached beside a fresh cart, the train as it arrived there; remote heroes are set down too, no spare spent

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 9: Co-op proof, docs and the full sweep

**Files:**
- Modify: `src/Net/NetTestFlow.cs` (the ride pass: one tail step), `scripts/net_test.sh` (the RIDE block's greps)
- Modify: `docs/development.md` (the Rails section, the generator knobs, the debug flags: read the current text first)
- Modify: `src/Core/DebugTools.cs` (the header list of flags: `--test-crevasse` and its variants)

**Interfaces:**
- Consumes: `CrevasseWatch.Take`, `RailController.RiseBesideCart` (Tasks 6, 8).
- Produces:
  - The RIDE pass's last step (server, after its last existing step, before its `RESULT`): every non-parked hero is
    given to `CrevasseWatch.Take` at its place; the server prints
    `[net-server] ride: wipe-rise: {n} hero(es) beside the cart at stop {k}`. The client, after the floor is rebuilt and
    its ForcePose lands, prints `[net] ride: rose beside the cart at stop {k} (d={metres:0.0} m)` from its own hero's
    place and the party cart's.
  - `net_test.sh` (RIDE block, after its last check): fail with "NET TEST: FAIL (the wipe did not set the remote player
    down beside the cart)" unless the server line is present and the client's `d=` is ≤ 6.0.

- [ ] **Step 1: Write the step and the greps** as above (no xUnit test: this is the co-op proof).

- [ ] **Step 2: Run the RIDE pass alone.** `dotnet build DungeonCrawler.csproj && RIDE=1 scripts/net_test.sh`.
  Expected: `[net-server] RESULT ok`, `[net] RESULT ok`, the two wipe-rise lines, no `NET TEST: FAIL`. A failure in a
  step before the tail is rerun once (the baseline flake); a tail failure is investigated. Then plain `scripts/net_test.sh`:
  `RESULT ok`.

- [ ] **Step 3: Write the docs.** In `docs/development.md`:
  - the Rails section: crevasses (where the generator carves them, `Cell.Chasm`, decks and planks), what a fall does to
    whom (Rulings 8–13), `RailController.Fall` as the entry the later plans call, the arrival snapshot and the wipe rise;
  - the generator knobs: `Crevasses`;
  - the debug flags: `--test-crevasse`, `=trestle`, `=survivor`, and what each proves;
  - the RIDE pass's new tail step.

- [ ] **Step 4: Run the whole suite and the sweep.** `dotnet test tests/DungeonCrawler.Tests.csproj`, the 20-floor loop
  and the `grep -c adapted` line from Task 4 Step 5. Expected: all tests pass, no repair line, `0`. Then
  `git diff --stat` shows no file outside this plan's lists.

- [ ] **Step 5: Commit**

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh docs/development.md src/Core/DebugTools.cs
git commit -m "test(rails): the RIDE pass ends in a wipe that rises beside the cart with the remote player set down there; docs: crevasses, falls and the wipe rise

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
