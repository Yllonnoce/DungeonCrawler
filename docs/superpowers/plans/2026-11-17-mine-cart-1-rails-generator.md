# Mine Cart Chase, Plan 1 of 6: the rail line in the generator and the track Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12 (and any ring floor whose plan sets `RailCount`) is generated with a twin-track rail line (party lane +
enemy lane) through a band of rock around the ring, running through the start room, up to three stop rooms and the exit
room, with refuge pockets; the builder lays real 3D track along it. Nothing moves yet.

**Architecture:** A new spec knob `RailCount` (0 = off, every other floor unchanged). With it on a Ring floor, the grid
grows by a band of `RailRules.Band` cells on every side and the ring keeps its old radius, so the same rooms come out
shifted inward; a late, salted pass (`FloorGenerator.Rails.cs`) routes the line through the band and records it as
`FloorLayout.Rail`. An engine-free `RailCurves` turns lane cells into a smooth centreline; a Godot `RailTrack` node
extrudes rails and sleepers along a `Curve3D`, which later plans reuse for carts.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md`

**The six plans:** 1 rails in the generator + track (this); 2 the cart, seating and run-over in single player; 3 stops,
gate, cage wagons, crew and Grisel, the win rule; 4 the enemy line, slag-cart traffic, switches, crossing and the set
pieces; 5 co-op sync, VR comfort and the tick-rate fix; 6 lines, taunts, assets, sound and music. Each is written after the
one before it lands, against the names it really produced.

## Global Constraints

- `RailCount` defaults to 0; with 0 every floor generates exactly as today (`LayoutKindTests`, `FloorGeneratorTests`,
  `VerticalityTests`, `FloorSolvabilityTests` all pass unchanged).
- Rails only on `LayoutKind.Ring`; on any other shape `RailCount` is ignored.
- Room indices and order never change: rooms are only ever appended (plans address rooms by index).
- The rail pass draws only from `new Random(seed ^ 0x2A11)`; it never touches the main `rng`.
- Floor 12 must still generate a Jailbreak objective on the shipped seed (`TauntTests` pins floor 12 = Jailbreak); in this
  plan the jail stays as today (cage wagons are plan 3).
- Generator change ⇒ `Protocol.Version` 15 → 16 and floor 12's `LayoutHash` refreshed with `--rehash-plans`.
- Hand-edited JSON keeps the game's escaping (`'` → `'`, non-ASCII → `\uXXXX`).
- `tests/` is not in git: test files are written and run locally, only `src/`, `data/`, `docs/` are committed.
- Build before any headless run: `dotnet build`; flags go after `--`.
- No installers / `export_all` / `package.sh`.

## Review Focus

- **A seed where routing fails** (no room in the band, too few eligible rooms): the floor must still generate and play,
  with `Rail == null` — pinned by `RailLineTests.Sweep_NeverBreaksTheFloor`.
- **A stop room on the upper level** (floor 12 has many `:` rooms): the lanes must stay walkable end to end across the
  height change — pinned by `RailLineTests.Lanes_AreWalkableEndToEnd`.
- **Something else carved into the band** (secret alcoves, the safe room's carved side room, vault side rooms): no lane
  or pocket may touch foreign floor — pinned by `RailLineTests.Lanes_TouchNoForeignFloor`.
- **The halls alone must still finish the floor** (the rails are the dangerous route): pinned by
  `RailLineTests.Floor_SolvableWithRailsBlocked` and the new `FloorSolvability` rule.
- **Old saves / old clients** on floor 12: the hash moves, so the protocol bump must reject old clients — pinned by
  `NetCodecTests.MessageKindCount_MatchesTheEnum_BumpVersionWhenItChanges` (16).

---

### Task 1: The `RailCount` knob

**Files:**
- Modify: `src/Dungeon/Generation/FloorSpec.cs` (add property after `FireCount`)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs:40-78` (`SpecOverrides`)
- Modify: `src/Adventure/Core/Ai/PromptBuilder.cs:61` (the SpecOverrides ranges sentence)
- Test: `tests/Dungeon/RailLineTests.cs` (new), `tests/Adventure/OfflineDesignerTests.cs` (existing `IsEmpty` test still passes)

**Interfaces:**
- Produces: `FloorSpec.RailCount : int` (default 0); `SpecOverrides.RailCount : int?`, clamped 0–3 in `Apply`, part of `IsEmpty`.

- [ ] **Step 1: Write the failing tests** in `tests/Dungeon/RailLineTests.cs`

```csharp
[Fact] public void RailCount_DefaultsToZero() => Assert.Equal(0, FloorSpec.Default.RailCount);

[Fact]
public void SpecOverrides_ClampRailCount()
{
    Assert.Equal(3, new SpecOverrides { RailCount = 9 }.Apply(FloorSpec.Default).RailCount);
    Assert.Equal(0, new SpecOverrides { RailCount = -2 }.Apply(FloorSpec.Default).RailCount);
    Assert.False(new SpecOverrides { RailCount = 2 }.IsEmpty);
    Assert.True(new SpecOverrides().IsEmpty);
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter FullyQualifiedName~RailLineTests`
Expected: compile error, `RailCount` not defined.

- [ ] **Step 3: Add the knob**

`FloorSpec.RailCount { get; init; }` doc: "Stops on a mine-cart line round a ring floor's outside (0 = no rails; ring
only)". `SpecOverrides.RailCount` with `[AiNotNull]` and
`[AiDescription("Stops on a mine-cart line round the outside of a ring floor, 0 to 3; 0 means no rails.")]`, added to
`IsEmpty`, `Apply` clamps `Math.Clamp(RailCount ?? spec.RailCount, 0, 3)`. In PromptBuilder line 61 add
`RailCount 0-3 (ring floors only)` to the SpecOverrides ranges.

- [ ] **Step 4: Run to see them pass**, plus `--filter FullyQualifiedName~AiPlannerTests|FullyQualifiedName~OfflineDesignerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Ai/PromptBuilder.cs
git commit -m "feat(rails): RailCount spec knob (0 = no rails)"
```

### Task 2: The rail line's data and the inset ring

**Files:**
- Create: `src/Dungeon/Generation/RailLine.cs`
- Modify: `src/Dungeon/Generation/FloorLayout.cs` (`Rail`, `IsRail`, `IsReserved`, `ContentHash`, `ToAscii`)
- Modify: `src/Dungeon/Generation/FloorGenerator.cs:9-14` (grow the grid), `src/Dungeon/Generation/FloorGenerator.Kinds.cs:225-254` (`PlaceRingRooms` radius)
- Test: `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Produces:
  - `public static class RailRules { public const int Band = 6; public const int PocketEvery = 3; }`
  - `public sealed record RailStop(int Room, IReadOnlyList<GridPos> Platform);` — `Platform` is the party-lane cells inside the room, in line order.
  - `public sealed record RailLine(IReadOnlyList<GridPos> Party, IReadOnlyList<GridPos> Enemy, IReadOnlyList<RailStop> Stops, IReadOnlyList<GridPos> Pockets);` — `Party`/`Enemy` are ordered, 4-connected cell lists from the start room to the exit room; `Stops[0]` is the up-stairs room, `Stops[^1]` the down-stairs room, between them up to `RailCount` stops.
  - `FloorLayout.Rail : RailLine?` (internal set), `FloorLayout.IsRail(GridPos) : bool` (lane cell of either lane).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void RailFloor_KeepsTheSameRooms_InsetByTheBand()
{
    var spec = FloorSpec.Default with { Layout = LayoutKind.Ring };
    var plain = FloorGenerator.Generate(spec, 4242);
    var railed = FloorGenerator.Generate(spec with { RailCount = 3 }, 4242);
    Assert.Equal(plain.Width + 2 * RailRules.Band, railed.Width);
    Assert.Equal(plain.Rooms.Count, railed.Rooms.Count);   // no room lost or reordered (secret rooms are appended after)
    for (var i = 0; i < plain.Rooms.Count; i++)
        Assert.Equal((plain.Rooms[i].W, plain.Rooms[i].H), (railed.Rooms[i].W, railed.Rooms[i].H));
}
```

- [ ] **Step 2: Run to see it fail** (`RailRules` not defined).

- [ ] **Step 3: Implement**

In `Generate`, after the Tune line: `if (spec.RailCount > 0 && spec.Layout == LayoutKind.Ring) spec = spec with { Width = spec.Width + 2 * RailRules.Band, Height = spec.Height + 2 * RailRules.Band };`.
In `PlaceRingRooms`, subtract `RailRules.Band` from `radius` when `spec.RailCount > 0`. Add `RailLine.cs` with the records
above. In `FloorLayout`: `Rail` property; `IsRail`; `IsReserved` also true for lane and pocket cells; `ContentHash` folds
every `Party`, `Enemy` and `Pockets` cell and each stop's room index (multiplier 37) when `Rail != null`; `ToAscii` draws
party lane `=`, enemy lane `%`, pocket `)` (before the cell switch, after stairs).

- [ ] **Step 4: Run to see it pass**, and the whole `LayoutKindTests` class (unchanged with `RailCount` 0).

- [ ] **Step 5: Commit** `feat(rails): rail line data; a railed ring is inset by the band`

### Task 3: Routing the line

**Files:**
- Create: `src/Dungeon/Generation/FloorGenerator.Rails.cs`
- Modify: `src/Dungeon/Generation/FloorGenerator.cs` (call after `PlaceObjectiveLate`, before `PlaceSecretRooms`)
- Test: `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Consumes: `RailLine`, `RailStop`, `RailRules`, `FloorLayout.Rail` (Task 2).
- Produces: `internal static void LayRails(FloorSpec spec, Random rng, FloorLayout layout)` — sets `layout.Rail` or leaves it null; adds `(a, b)` to `layout.Connections` for each consecutive pair of line rooms.

- [ ] **Step 1: Write the failing tests** (seed sweep: `for i in 0..199, seed = i * 7919 + 13`, spec `FloorSpec.Default with { Layout = Ring, RailCount = 3 }`, objectives None and Jailbreak)

```csharp
[Theory, MemberData(nameof(Seeds))] public void Sweep_NeverBreaksTheFloor(int seed, FloorObjectiveKind obj)
{   var l = Gen(seed, obj); Assert.Empty(FloorSolvability.Problems(l)); Assert.Contains(l.DownStairs, l.Reachable(l.UpStairs, null, secretsOpen: true)); }

[Fact] public void MostSeeds_GetALineWithThreeStops()   // at least 90% of the 200 seeds: Rail != null and Stops.Count == 5
[Theory, MemberData(nameof(Seeds))] public void Lanes_AreTwinAndConnected(int seed, FloorObjectiveKind obj)
// when Rail != null: Party.Count >= 8; each lane 4-connected in order; lanes disjoint; every Party cell has an Enemy cell among its 8 neighbours and vice versa
[Theory, MemberData(nameof(Seeds))] public void Lanes_AreWalkableEndToEnd(int seed, FloorObjectiveKind obj)
// for each lane: CanStep(lane[i], lane[i+1]) for every i
[Theory, MemberData(nameof(Seeds))] public void Lanes_TouchNoForeignFloor(int seed, FloorObjectiveKind obj)
// every 8-neighbour of a lane or pocket cell outside the line's own rooms is a lane cell, a pocket, Wall or Void
[Theory, MemberData(nameof(Seeds))] public void EveryLaneCell_IsNearAPocket(int seed, FloorObjectiveKind obj)
// every lane cell outside a stop room has a pocket 4-adjacent to some cell of the same lane within RailRules.PocketEvery steps along it
[Theory, MemberData(nameof(Seeds))] public void Stops_AreUsableRooms(int seed, FloorObjectiveKind obj)
// Stops[0] holds UpStairs, Stops[^1] holds DownStairs; no stop is the BossArena, a vault room, the jail room or the safe room; stops are distinct; each Platform has >= 2 cells, all inside its room
```

- [ ] **Step 2: Run to see them fail** (`Rail` is always null).

- [ ] **Step 3: Implement `LayRails`**

If `spec.RailCount == 0 || layout.Kind != LayoutKind.Ring` return. Then:
1. **Line rooms**: ring rooms are `1..n` in circle order. Start = room holding `UpStairs`, exit = room holding `DownStairs`.
   Walk the circle both ways from start to exit; eligible = not the arena, not a vault room, not the jail room, not the safe
   room, not the shrine's room. Take the way with more eligible rooms; choose `min(RailCount, eligible)` of them spread
   evenly along it. Fewer than 1 → return (no rails).
2. **Mouths**: for each line room, the lanes cross it along its outer edge row/column (the side facing away from the floor
   centre). Entry and exit mouths are wall cells on that side; the party lane runs inside the room along the edge between
   them (the `Platform`), the enemy lane alongside it one cell further out — carve that outer cell to Floor if it is wall.
3. **Segments**: between consecutive line rooms, A* the party lane over cells that are `Void`/`Wall`, not under, with both
   the cell and its outward twin (perpendicular to travel, away from the floor centre) rock whose 8-neighbours are rock,
   this route, or the two rooms' own edge cells. Step cost 1, +4 per turn. The enemy lane is the twin path (at an outer
   corner add the corner cell so it stays 4-connected; at an inner corner drop the duplicate). No route → drop that stop
   and route to the next; no route at all → return with `Rail` null and nothing carved.
4. **Levels**: lane cells take the level of the room the segment leaves; if the next room differs, the middle straight run
   of the segment gets a 2-cell ramp on both lanes (`Cell.Ramp`, `RampInfo(dir, lowLevel * 4)` with the `Verticality`
   conventions) and the rest of the segment takes the other level. No straight run of 2 → treat as no route.
5. **Pockets**: walking each lane outside rooms, every `RailRules.PocketEvery` cells carve a one-cell niche on the lane's
   free side (party lane: toward the ring; enemy lane: outward) when that cell is rock with rock all round but for its lane
   cell; if not possible there, try the next cell.
6. Carve lane and pocket cells to `Floor`/`Ramp`, set `layout.Rail`, add the `Connections`, then `RaiseWalls(layout)`.

- [ ] **Step 4: Run the new tests and the full generator suite**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter FullyQualifiedName~Tests.Dungeon`
Expected: PASS (the old tests are untouched because `RailCount` is 0 there).

- [ ] **Step 5: Commit** `feat(rails): route a twin-track line round a ring floor through its stops`

### Task 4: The halls must finish the floor alone

**Files:**
- Modify: `src/Dungeon/Generation/FloorSolvability.cs` (end of `Problems`)
- Test: `tests/Dungeon/RailLineTests.cs`, `tests/Dungeon/FloorSolvabilityTests.cs` (unchanged, must pass)

**Interfaces:**
- Consumes: `FloorLayout.Rail`, `FloorLayout.IsRail`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Problems_ReportsAFloorThatNeedsTheRails()
{
    var l = FirstRailedSeed();                      // helper: first seed from the sweep with Rail != null
    // wall off every non-rail way into the exit room, leaving only the line: the checker must complain
    // DoorwaysOfExitRoomOffTheLine: every walkable cell outside the exit room, 4-adjacent to it, that is not a lane cell
    foreach (var c in DoorwaysOfExitRoomOffTheLine(l)) l[c] = Cell.Wall;
    Assert.Contains(FloorSolvability.Problems(l), p => p.Contains("with the rails blocked"));
}
[Theory, MemberData(nameof(Seeds))] public void Floor_SolvableWithRailsBlocked(int seed, FloorObjectiveKind obj)
// Problems(l) has no "with the rails blocked" entry
```

- [ ] **Step 2: Run to see it fail.**

- [ ] **Step 3: Implement**: when `layout.Rail != null`, recompute reach from `UpStairs` with shut doors and `IsRail` cells
  (outside stop platforms) blocked; report `"with the rails blocked, the stairs down are out of reach"`, and the same for
  each outside prisoner and when every spare jail key is out of reach.

- [ ] **Step 4: Run** `--filter FullyQualifiedName~FloorSolvabilityTests|FullyQualifiedName~RailLineTests` → PASS.

- [ ] **Step 5: Commit** `feat(rails): the floor must finish on foot with the rails blocked`

### Task 5: Smooth centrelines (engine-free)

**Files:**
- Create: `src/Dungeon/Generation/RailCurves.cs`
- Test: `tests/Dungeon/RailCurvesTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlyList<System.Numerics.Vector3> RailCurves.Centreline(IReadOnlyList<GridPos> lane, Func<GridPos, float> heightOf, float cell = 4f, float cornerRadius = 3f, float step = 0.5f)` — world-space points (x = cell.X * cell + cell/2, z likewise, y = surface height), straight between cell centres, each 90° turn replaced by a quarter-circle of `cornerRadius`, heights eased linearly across ramp cells; sampled every `step`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Straight_PointsAreEvenlySpaced()      // lane (0,0)..(5,0), flat: every gap within step ± 0.05, first point at (2,0,2), last at (22,0,2)
[Fact] public void Corner_IsRounded()                     // lane (0,0)->(3,0)->(3,3): no point within 0.5 m of the sharp corner (14,·,2); every gap <= step + 0.05
[Fact] public void Ramp_ClimbsSmoothly()                  // heightOf 0 for x<2, 2 at x==2, 4 at x>=3 on lane (0,0)..(5,0): y never drops along the line, ends at 4
```

- [ ] **Step 2: Run to see them fail.**
- [ ] **Step 3: Implement `Centreline`.**
- [ ] **Step 4: Run** `--filter FullyQualifiedName~RailCurvesTests` → PASS.
- [ ] **Step 5: Commit** `feat(rails): smooth rail centrelines from lane cells`

### Task 6: The track in the world

**Files:**
- Create: `src/Dungeon/Props/RailTrack.cs`
- Modify: `src/Dungeon/DungeonBuilder.cs` (new `PlaceRails(FloorLayout layout, Node3D root)` called right after `PlaceStructures` at :287)

**Interfaces:**
- Consumes: `RailCurves.Centreline`, `FloorLayout.Rail`, `DungeonBuilder.Surface`, `Box` (:1841).
- Produces (plans 2–4 use these):
  - `public partial class RailTrack : Node3D { public Curve3D Curve { get; } public float Length { get; } public void Build(IReadOnlyList<Vector3> points, Material steel, Material sleeper); }`
  - `DungeonBuilder.PartyTrack : RailTrack?`, `DungeonBuilder.EnemyTrack : RailTrack?`
  - Marker nodes `RailStop0`…`RailStopN` (Node3D at the middle of each stop's `Platform`), for screenshots and later plans.

- [ ] **Step 1: Implement `RailTrack.Build`**: a `Path3D` child with a `Curve3D` (`BakeInterval = 0.2f`, `UpVectorEnabled = true`) through the points; an `ArrayMesh` extruded along `Curve.SampleBakedWithRotation(s, cubic: true, applyTilt: true)` every 0.25 m: two rails (0.07 × 0.12 m boxes, gauge 0.9 m), sleepers every 0.6 m (1.4 × 0.12 × 0.22 m). No collision on the rails (bodies walk the cell floor).
- [ ] **Step 2: Implement `PlaceRails`**: return when `layout.Rail` is null; build `PartyTrack`/`EnemyTrack` from each lane's `Centreline` (heights from `layout.HeightOf`), steel from the forge theme's metal, sleepers from its dark wood; a timber frame (two posts and a cap via `Box`, no collision) every 2 lane cells outside rooms; a small lantern light in each pocket (same light settings `PlaceLightsAndDecor` uses for torches); the `RailStop{i}` markers.
- [ ] **Step 3: Build and look at it**

Run: `dotnet build && xvfb-run -a godot --path . -- --builtin --no-ai --clear-enemies --floor=12 --face-node=RailStop1:3 --screenshot=/tmp/claude-1000/-mnt-Transfer-5FD1E1415AC80F29-dev-DungeonCrawler/7ccf0941-144a-4c82-9601-42f1ccaa2c3f/scratchpad/rails.png --after=10 --quit-after=16`
Expected: the screenshot shows two tracks with sleepers running through a timbered tunnel (only once Task 7 turns rails on for floor 12; before that, verify with a scratch campaign copy that sets `RailCount`).
- [ ] **Step 4: Commit** `feat(rails): lay rails, sleepers, timbering and pocket lanterns along the line`

### Task 7: Floor 12 gets its line

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (floor 12 `SpecOverrides`: `"RailCount": 3`)
- Modify: `data/adventures/builtin/floor_12.json` (`LayoutHash`; any room-role repairs)
- Modify: `src/Net/Core/Protocol.cs:6` (16), `tests/Net/NetCodecTests.cs:251` (16, history comment "v16: rails on ring floors that ask for them")
- Modify: `docs/development.md` (a "Rails" section: the knob, the band, the pass, `--print-map` symbols, tests)

- [ ] **Step 1: Turn it on and check the map**

Run: `dotnet build && godot --headless --path . -- --builtin --no-ai --floor=12 --print-map`
Expected: `[map] floor 12 ring objective=jailbreak …` and the ASCII shows `=`/`%` lanes and `)` pockets through 5 rooms. If
the shipped seed gets fewer than 3 stops, report it (do not tune by hand-picking a seed: the campaign seed is fixed).

- [ ] **Step 2: Rehash** — `godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`; `git diff --stat data/` must show only `floor_12.json` changed.
- [ ] **Step 3: Sweep for repairs** — `godot --headless --path . -- --builtin --no-ai --floor=12 --quit-after=8 2>&1 | grep -E "repaired|dropped|demoted|adapted"`; hand-fix `floor_12.json` (room roles, collective rooms, NPC rooms) until it prints nothing, keeping the escaping rule.
- [ ] **Step 4: Bump the protocol** and run `dotnet test tests/DungeonCrawler.Tests.csproj` (whole suite) → PASS; `scripts/net_test.sh` → both RESULT lines pass.
- [ ] **Step 5: Screenshot** as in Task 6 Step 3 and look at it.
- [ ] **Step 6: Commit** `feat(floor12): the Casting Hall gets its rail line — protocol 16`
