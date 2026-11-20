# Mine Cart Chase, Plan 9: the full circle Implementation Plan

> **Amended 2026-11-19: five rides, not six.** After measurement the plan was amended to five rides (spec commit d2a809b):
> `RailCount` 4, not 5; the stops are rooms [1,2,4,6,8,9]; the rides are 267/206/281/252/209 m, 1214 m in all; the cavern is on
> ride 4; there are eight crew in four cages; the kiln ids are `r5_*`; `LastRide` 5, `LastRideStop` 4, `Intermediate` 4.
> The task text below keeps the original numbers.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12's line runs the whole way round the ring to a Cooling Stair beside Kiln-Mouth Landing, through five
intermediate stops (six rides of about 220–330 m, about 1.5–2 km in all), with every 4-ride assumption in the code, the
data, the debug flows and the RIDE pass generalised to any stop count. It plays with today's set-piece kinds only.

**Architecture:** Two new spec knobs, both off by default: `RailLoop` (the down stairs go in a ring neighbour of the
start, so the only way round is the long one) and `RailBand` (the band of rock outside the ring, today's 6 cells by
default). A wider band translates the whole floor by the extra cells, so room indices hold. When the band is wider than
6 cells, `RouteSegment` lengthens each routed stretch with outward rectangular bulges (the meander) until the ride
reaches its target length, and it reserves a straight, rock-backed span on the second-to-last ride for plan 10's cavern.
An engine-free `LineShape` gives the debug flows and the music the line's ride count. The shipped `floor_12.json` is then
re-homed onto the new rooms by identity.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md` (an addendum to
`2026-11-17-floor-12-mine-cart-chase-design.md` and the two 2026-11-19 addenda). Code maps:
`.superpowers/sdd/floor12-v2-recon/generation.md` and `gameplay.md`.

**The later plans:** 9 the full circle (this one). 10 crevasses (`Cell.Chasm`, the cavern carved on `RailLine.Cavern`),
death and the wipe rise. 11 barricades and live switches with deadly branches. 12 the dark and the runaway. 13 the
cavern's collapsing bridge and spiral (free 3D track). 14 the leap to the enemy cart. 15 the loop-the-loop. Each one is
written after the plan before it lands, against the names that plan actually produced.

**Rulings** (mine; each one decides something the spec left open or that cannot work as written):
- Ruling: the down stairs go in the ring neighbour of the up room whose centre is **nearer** to the up room's centre
  (ties go to the lower index) — the spec says "beside Kiln-Mouth Landing", and on the shipped seed this likely keeps
  the line leaving east as it does today, so stop 1 stays room 9 and less content moves — cost if wrong: the line runs
  the other way round. The re-home in Task 6 works either way, and flipping it is a one-word change to "farther".
- Ruling: the meander is a post-route pass of outward rectangular bulges inside `RouteSegment`. It has no rng. It runs
  only when `RailBand > RailRules.Band`, and when a bulged stretch fails any existing check, the plain route is used
  instead — A* always returns the shortest path, so a straightness cost cannot lengthen a ride. Forced waypoints would
  fight the search's heading and turn state and its lack of self-avoidance. A bulge is local and is checked by the same
  lane, twin and pocket rules plus one self-clearance rule — cost if wrong: some rides stay short. The tuning knobs are
  floor 12's `RailBand` (12, then 14, then 16) and `RailRules.BulgeMaxDepth`, never a hand-picked seed.
- Ruling: the meander is keyed to the wider band, not to `RailLoop` — the spec ties the weave to the band ("within the
  wider band the route weaves") — cost if wrong: a loop floor at band 6 does not weave, which is what that knob then means.
- Ruling: a `"golem"` arms when the cart reaches its `At`, as a slag cart does, not from the ride's departure. The last
  ride's departure now gets the RideStart taunt as the other rides do — the kiln ride is now 3 to 4 times as long, and a
  golem riding beside the train from the departure would slam about 12 times (144 plating) and wreck every train —
  cost if wrong: an old plan's golem at `At 0.05` still bursts out at the start, which is today's behaviour.
- Ruling: the crew wagons' weight counts at most three wagons (`CartRules.MaxLoadWagons = 3`) — `SetPieceRules.JumpSpeed`
  promises that one pumper makes the jump with every wagon coupled, and five wagons would break that (5.76 m/s < 6.1) —
  cost if wrong: a five-wagon train feels no heavier than a three-wagon one.
- Ruling: the validator caps scale with the ride count: `Math.Max(12, 3 × rides)` encounters and `Math.Max(20, 5 × rides)`
  set pieces. `MaxSlagRiders` stays 3 — a 4-ride line keeps today's caps — cost if wrong: an AI plan could put up to 18
  encounters on a 6-ride floor.
- Ruling: the set-piece ids move with their ride: `r4_*` becomes `r6_*`, and the new pieces are `r4_*` and `r5_*` — an id
  that names the wrong ride misleads every later plan and debug flow — cost: the debug flows and two data tests are
  edited (there are no released saves of this floor).
- Ruling: floor 12 keeps 16 collective enemies (`FloorMenu.MaxEnemies`), and every intermediate stop gets a guard group.
  `col_ring` loses its hound and `col_stop2` loses a cinder. `col_stop4` is one cinder and `col_stop5` is one ember hound,
  so the cost is unchanged — a 17th enemy would be trimmed by the validator — cost if wrong: stops 4 and 5 are thinly
  guarded until the later plans add their pieces.
- Ruling: plan 10 finds its cavern through `RailLine.Cavern`: a span of party-lane indices on the second-to-last ride,
  backed by solid rock outward. Plan 10 carves the cavern inside `LayRails` right after `CommitRails`, so no later pass
  can take that rock — the spec says to carve the cavern "on ride 5's segment" but does not say how to keep the room for
  it — cost if wrong: on seeds where no straight, rock-backed run exists, `Cavern` is null and plan 10 has to widen its
  search.
- Ruling: the floor 12 sweep also greps `forced|cannot be|trimmed|clamped|kept only` — a room-role repair prints
  "role forced to" or "cannot be", which the documented `repaired|dropped|demoted|adapted` grep misses — cost: none.

## Global Constraints

- Defaults reproduce today byte for byte: with `RailLoop` false and `RailBand` 6 (`RailRules.Band`), every floor's
  `ContentHash` is unchanged. This is pinned by `RailLineTests.DefaultKnobs_BuildTodaysFloors` (Task 1) and by
  `--rehash-plans` changing only `floor_12.json`'s `LayoutHash`.
- Rails only on `LayoutKind.Ring`. `RailLoop` and `RailBand` do nothing unless `RailCount > 0` on a ring.
- Room indices and order never change. A wider band only translates the floor. Rooms are only ever appended.
- The rail pass draws only from `new Random(seed ^ 0x2A11)`. The meander draws from no rng at all.
- Floor 12 must still generate a Jailbreak objective on the shipped seed (`objective=jailbreak` in the floor log).
- `Protocol.Version` stays **17** (the branch is unreleased).
- CC0 assets only (the repository is public). This plan adds no assets.
- No voice spend: new and rewritten lines are subtitle-only (`"VoiceId": null`). Never run a Runware/voice bake.
- Hand-edited JSON keeps the game's escaping: `'` → `'`, `&` → `&`, `<` → `<`, `>` → `>`,
  `+` → `+`, non-ASCII → `\uXXXX` (upper-case hex), two-space indent. `git diff` must show only the values you meant
  to change.
- `tests/` (xUnit) is not in git. Write and run tests locally, never `git add tests/`. Commit only `src/`, `data/`,
  `docs/`, `scripts/`.
- Run `dotnet build DungeonCrawler.csproj` before any `godot --headless --path . -- <flags>` run. Engine flags such as
  `--headless --path .` go before `--`, and game flags go after it.
- `RIDE=1 scripts/net_test.sh` runs alone, with nothing else on the machine.
- Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N` (or `--quit-when-empty`).
- Full test suite: `dotnet test tests/DungeonCrawler.Tests.csproj`.
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No installers or dist builds (`export_all.sh`, `package.sh`, `publish_*.sh`), no push, no merge.

## Review Focus

- **A seed whose long way cannot take bulges** (narrow rock, pockets that will not fit, a ramp's straight run): the
  meander must never cost the floor its line. Expect the plain route as the fallback, and a wider band never yields
  fewer stops than band 6 on the same seed — pinned by `RailLineTests.AWiderBand_NeverLosesALine` (Task 4).
- **A full train at the jump** (all five cages opened, five wagons coupled, one pumper): it must make ride 6's jump —
  pinned by `SetPieceRulesTests.Jump_OnePumperMakesIt_NoneNever(1, 5, true)` (Task 5) and by the chase flow's one-pumper
  jump with every wagon coupled (Task 8).
- **The golem on a ride four times longer**: it must not ride beside the train from the departure and wreck it before
  the spur — pinned by `SetPieceRulesTests.Golem_ArmsAtItsAt` (Task 5), `RailEncounterPlanTests.BuiltinFloor12_HasTheSixRides`
  (golem `At` 0.55, Task 7), and the chase flow's "golem bursts out" S check (Task 8).
- **Grisel naming the wrong cage** now that ten crew fill five wagons and her "Last cage" line moves from ride 3 to ride 5:
  each `ride{N}` call must name stop N's two crew — pinned by `CrewPlanTests.BuiltinFloor12_GriselNamesEachCageOnItsRide` (Task 7).
- **RailLoop on a floor whose objective moves the stairs** (ShiftingVault) or on a boss floor (the arena holds the stairs):
  the floor must still generate and finish, with or without a line — pinned by the Loop/Long sweeps including
  `ShiftingVault` in `Sweep_NeverBreaksTheFloor`, and by `RailLoop_OnABossFloor_KeepsTheArenaStairs` (Task 3).

---

### Task 1: Pin today's floors, and the knobs

**Files:**
- Modify: `src/Dungeon/Generation/FloorSpec.cs` (after `RailCount`)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs:61-84` (`SpecOverrides`)
- Test: `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Produces: `FloorSpec.RailLoop : bool` (default false), `FloorSpec.RailBand : int` (default `RailRules.Band`);
  `SpecOverrides.RailLoop : bool?`, `SpecOverrides.RailBand : int?` (both `[AiIgnore]`, both part of `IsEmpty`);
  `Apply` clamps `RailCount` 0..6 and `RailBand` `RailRules.Band`..16.

- [ ] **Step 1: Write the pinning test, before any source change**

```csharp
// tests/Dungeon/RailLineTests.cs — values recorded from the code as it is before this plan (Step 2 fills them in)
public static IEnumerable<object[]> TodaysFloors() => new[]
{
    // (layout, railCount, objective, seed, hash)
    new object[] { LayoutKind.Ring, 3, FloorObjectiveKind.Jailbreak, 13, 0x0UL },
    // … the first 5 sweep seeds (i * 7919 + 13) for each of: Ring/3/Jailbreak, Ring/3/None, Ring/0/None, Standard/0/None
};
[Theory, MemberData(nameof(TodaysFloors))]
public void DefaultKnobs_BuildTodaysFloors(LayoutKind kind, int rails, FloorObjectiveKind obj, int seed, ulong hash)
    => Assert.Equal(hash, FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, RailCount = rails, Objective = obj }, seed).ContentHash());
```

- [ ] **Step 2: Record the hashes.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter FullyQualifiedName~DefaultKnobs_BuildTodaysFloors`.
  Expected: 20 failures, each printing the actual hash. Paste the actual values into the table and run again.
  Expected: PASS. From here on this test must stay green through every task.

- [ ] **Step 3: Write the failing knob tests**

```csharp
[Fact] public void RailLoopAndBand_DefaultToToday()
{ Assert.False(FloorSpec.Default.RailLoop); Assert.Equal(RailRules.Band, FloorSpec.Default.RailBand); }

[Fact] public void SpecOverrides_ClampRailCount()            // replaces the old test: 9 now clamps to 6
{
    Assert.Equal(6, new SpecOverrides { RailCount = 9 }.Apply(FloorSpec.Default).RailCount);
    Assert.Equal(0, new SpecOverrides { RailCount = -2 }.Apply(FloorSpec.Default).RailCount);
    Assert.False(new SpecOverrides { RailCount = 2 }.IsEmpty);
    Assert.True(new SpecOverrides().IsEmpty);
}

[Fact] public void SpecOverrides_CarryRailLoopAndClampRailBand()
{
    Assert.True(new SpecOverrides { RailLoop = true }.Apply(FloorSpec.Default).RailLoop);
    Assert.Equal(16, new SpecOverrides { RailBand = 40 }.Apply(FloorSpec.Default).RailBand);
    Assert.Equal(RailRules.Band, new SpecOverrides { RailBand = 2 }.Apply(FloorSpec.Default).RailBand);
    Assert.False(new SpecOverrides { RailLoop = false }.IsEmpty);
    Assert.False(new SpecOverrides { RailBand = 12 }.IsEmpty);
}
```

- [ ] **Step 4: Run to see them fail.** Same command with `--filter FullyQualifiedName~RailLineTests`. Expected: they
  fail to compile, because `RailLoop` and `RailBand` are not defined.

- [ ] **Step 5: Add the knobs.**
  - `FloorSpec.RailLoop { get; init; }`, documented as "The mine-cart line runs the whole ring: the down stairs go in
    the start's ring neighbour (railed rings only)".
  - `FloorSpec.RailBand { get; init; } = RailRules.Band`, documented as "Cells of rock between a railed ring's rooms and
    the grid's edge; wider than RailRules.Band, the line meanders".
  - In `SpecOverrides`: `[AiDescription("True: the mine-cart line runs the whole way round the ring, and the stairs down are beside the start.")] [AiIgnore] public bool? RailLoop`
    and `[AiDescription("Cells of rock outside a railed ring's rooms for the line to weave in, 6 to 16.")] [AiIgnore] public int? RailBand`.
  - Change the `RailCount` description to "0 to 6". Add both new fields to `IsEmpty`. In `Apply`:
    `RailCount = Math.Clamp(..., 0, 6)`, `RailLoop = RailLoop ?? spec.RailLoop`,
    `RailBand = Math.Clamp(RailBand ?? spec.RailBand, RailRules.Band, 16)`.

- [ ] **Step 6: Run** `--filter FullyQualifiedName~RailLineTests|FullyQualifiedName~OfflineDesignerTests|FullyQualifiedName~AiPlannerTests`.
  Expected: PASS (the AI schema still hides all three rail fields).

- [ ] **Step 7: Commit**

```bash
git add src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs
git commit -m "feat(rails): RailLoop and RailBand spec knobs (off by default), RailCount 0..6

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: `RailBand` sizes the grid

**Files:**
- Modify: `src/Dungeon/Generation/FloorGenerator.cs:13,16` (grow by `spec.RailBand`; `layout.RailBand = spec.RailBand + 2`)
- Modify: `src/Dungeon/Generation/FloorGenerator.Kinds.cs:234` (radius subtracts `spec.RailBand`)
- Test: `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Consumes: `FloorSpec.RailBand` (Task 1).
- Produces: a railed ring of band B is `48 + 2B` wide. Every room placed before `LayRails` is the band-6 room shifted by
  `(B − 6, B − 6)`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void AWiderBand_TranslatesTheFloorBeforeTheLine()
{
    foreach (var seed in SweepSeeds().Take(20))
    {
        var spec = FloorSpec.Default with { Layout = LayoutKind.Ring, RailCount = 3, Objective = FloorObjectiveKind.Jailbreak };
        var a = FloorGenerator.Generate(spec, seed);
        var b = FloorGenerator.Generate(spec with { RailBand = 12 }, seed);
        var d = new GridPos(6, 6);
        Assert.Equal((a.Width + 12, a.Height + 12), (b.Width, b.Height));
        Assert.Equal(a.RingRooms, b.RingRooms);
        for (var i = 0; i <= a.RingRooms; i++)                       // the hub and every ring room
            Assert.Equal((a.Rooms[i].X + 6, a.Rooms[i].Y + 6, a.Rooms[i].W, a.Rooms[i].H), (b.Rooms[i].X, b.Rooms[i].Y, b.Rooms[i].W, b.Rooms[i].H));
        Assert.Equal(a.UpStairs + d, b.UpStairs);
        Assert.Equal(a.DownStairs + d, b.DownStairs);
        Assert.Equal(a.Vaults.Select(v => v.Door + d), b.Vaults.Select(v => v.Door));
        Assert.Equal(a.Objective?.JailRoom is { } j ? a.Rooms.IndexOf(j) : -1, b.Objective?.JailRoom is { } k ? b.Rooms.IndexOf(k) : -1);
    }
}
```

- [ ] **Step 2: Run to see it fail** (`--filter FullyQualifiedName~AWiderBand`). Expected: FAIL on the width, because
  the grid still grows by 6.

- [ ] **Step 3: Implement.** Replace `RailRules.Band` with `spec.RailBand` in the three places named under Files.
  `TryCarveSideRoom`'s band check and `KeepRailEdges` already read `layout.RailBand` and shift with the floor, so leave
  them alone. If a pass is found that does not shift with the floor, stop and report it; do not paper over it.

- [ ] **Step 4: Run** the whole `RailLineTests` class (including `DefaultKnobs_BuildTodaysFloors`), `LayoutKindTests` and
  `FloorGeneratorTests`. Expected: PASS.

- [ ] **Step 5: Commit** `feat(rails): RailBand sizes a railed ring's band; a wider band translates the floor`
  (with the Co-Authored-By line).

### Task 3: `RailLoop` — the down stairs beside the start, the line the long way round

**Files:**
- Modify: `src/Dungeon/Generation/FloorGenerator.cs` (`PlaceStairs(FloorLayout layout, bool loop)` at :400; its call site at :50)
- Test: `tests/Dungeon/RailLineTests.cs` (sweep configs; new tests)

**Interfaces:**
- Consumes: `FloorSpec.RailLoop`, `layout.RingRooms`, `FloorGenerator.Dist2`.
- Produces: with `loop` set, `DownStairs` is the centre of the ring neighbour (index u−1 or u+1 round 1..n) of the up room
  u whose centre is nearer the up room's centre, with ties going to the lower index. `BossArena` keeps its precedence. If the
  up room is not a ring room, or `RingRooms < 3`, the rule falls back to today's. `loop` is
  `spec.RailLoop && spec.RailCount > 0 && spec.Layout == LayoutKind.Ring`. `PlanRails` is unchanged: the short way has
  no eligible stop, so the long way is the only line.

- [ ] **Step 1: Turn the sweep into configs** (the existing tests' bodies are unchanged; they gain the config parameter)

```csharp
public enum LineKind { Today, Loop, Long }
private static FloorSpec SpecOf(LineKind k, FloorObjectiveKind obj) => k switch
{
    LineKind.Today => FloorSpec.Default with { Layout = LayoutKind.Ring, RailCount = 3, Objective = obj },
    LineKind.Loop  => FloorSpec.Default with { Layout = LayoutKind.Ring, RailCount = 5, RailLoop = true, Objective = obj },
    _              => FloorSpec.Default with { Layout = LayoutKind.Ring, RailCount = 5, RailLoop = true, RailBand = 12, Objective = obj },
};
// Seeds(): Today = the 200 sweep seeds x {None, Jailbreak} (as now); Loop and Long = the first 100 x {None, Jailbreak, ShiftingVault}.
// LoopSeeds(): the Loop and Long rows with None or Jailbreak (no stairs-moving objective). LongSeeds(): the Long rows of Seeds().
// Gen(seed, obj, kind) caches on all three. Every [Theory, MemberData(nameof(Seeds))] test takes (int seed, FloorObjectiveKind obj, LineKind kind).
// The Long rows exist from this task on; they behave like Loop until Task 4 adds the meander.
```

- [ ] **Step 2: Write the failing tests**

```csharp
[Theory, MemberData(nameof(LoopSeeds))]   // Loop and Long rows of Seeds(), objective None or Jailbreak
public void RailLoop_PutsTheDownStairsInTheNearerNeighbourOfTheStart(int seed, FloorObjectiveKind obj, LineKind kind)
{
    var l = Gen(seed, obj, kind);
    int n = l.RingRooms, u = RingIndex(l, l.UpStairs), down = RingIndex(l, l.DownStairs);
    int a = u == 1 ? n : u - 1, b = u == n ? 1 : u + 1;
    long da = Dist2(l.Rooms[a].Center, l.Rooms[u].Center), db = Dist2(l.Rooms[b].Center, l.Rooms[u].Center);
    Assert.Equal(da < db ? a : db < da ? b : Math.Min(a, b), down);
}

[Theory, MemberData(nameof(LoopSeeds))]
public void RailLoop_RunsTheLongWayRound(int seed, FloorObjectiveKind obj, LineKind kind)
{
    var l = Gen(seed, obj, kind);
    if (l.Rail is not { } rail) return;
    // walking the ring from Stops[0] the way Stops[1] lies, every stop comes in order and the exit is the last ring room reached
    var way = Way(l, rail.Stops[0].Room, rail.Stops[1].Room);       // +1 or -1 round 1..n, the way with no ring room skipped backwards
    var order = Walk(l, rail.Stops[0].Room, way).ToList();            // ring indices from the start, n of them
    Assert.Equal(rail.Stops.Select(s => s.Room), rail.Stops.Select(s => s.Room).OrderBy(order.IndexOf));
    Assert.Equal(order[^1], rail.Stops[^1].Room);
}

[Theory] [InlineData(LineKind.Loop)] [InlineData(LineKind.Long)]
public void MostLoopSeeds_GetFiveStops(LineKind kind)
{
    var full = SweepSeeds().Take(100).SelectMany(s => new[] { FloorObjectiveKind.None, FloorObjectiveKind.Jailbreak }.Select(o => Gen(s, o, kind)))
        .Count(l => l.Rail is { Stops.Count: 7 });
    Assert.True(full >= 160, $"{full} of 200 {kind} floors got five stops");
}

[Fact]
public void RailLoop_OnABossFloor_KeepsTheArenaStairs()
{
    var l = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Ring, BossFloor = true, RailCount = 5, RailLoop = true }, 13);
    Assert.True(l.BossArena!.Contains(l.DownStairs));
    Assert.Empty(FloorSolvability.Problems(l));
}
```
(`RingIndex`, `Way`, `Walk`, `Dist2` are small private helpers in the test class.)

- [ ] **Step 3: Run to see them fail** (`--filter FullyQualifiedName~RailLineTests`). Expected: the RailLoop tests fail
  (the down stairs are still opposite the start). The Today rows still pass.

- [ ] **Step 4: Implement `PlaceStairs(layout, loop)`** as described under Interfaces.

- [ ] **Step 5: Run** `--filter FullyQualifiedName~Tests.Dungeon`. Expected: PASS, including every invariant test
  (twin, walkable, foreign floor, pockets, usable stops, solvable with the rails blocked) on the Loop and Long rows, and
  `Sweep_NeverBreaksTheFloor` on the ShiftingVault rows. If `MostLoopSeeds_GetFiveStops` misses 160, report the
  measured count and the commonest `RailFailure`. Do not lower the threshold yourself.

- [ ] **Step 6: Commit** `feat(rails): RailLoop puts the down stairs beside the start, so the line runs the whole ring`
  (with the Co-Authored-By line).

### Task 4: The meander and the cavern span

**Files:**
- Create: `src/Dungeon/Generation/FloorGenerator.Meander.cs` (partial: `Meander`, `SelfClear`, `CavernWindow`)
- Create: `src/Dungeon/Generation/LayoutReport.cs`
- Modify: `src/Dungeon/Generation/RailLine.cs` (rules, `RailSpan`, `RailLine.Cavern`)
- Modify: `src/Dungeon/Generation/FloorGenerator.Rails.cs` (`LayRails`, `PlanRails`, `RouteLine`, `RouteSegment`,
  `PlaceRailRamp`, `RailSegment`, `CommitRails`; `Twin` and `RailSide` go from private to internal)
- Modify: `src/Core/DebugTools.cs:893` (`--print-map` prints `LayoutReport` and the cart's `StopS`)
- Modify: `docs/development.md` (Rails: the knobs paragraph at :1096-1101)
- Test: `tests/Dungeon/RailMeanderTests.cs` (new), `tests/Dungeon/RailLineTests.cs`

**Interfaces:**
- Consumes: `FloorSpec.RailBand`, `RailLoop` (Tasks 1–3); `RouteSegment`'s `Ok` lane predicate and `shadow` set.
- Produces:
  - `RailRules`: `RideMinCells = 55` (220 m), `RideMaxCells = 82` (330 m), `BulgeSpan = 4`, `BulgeMaxDepth = 8`,
    `CavernCells = 10`, `CavernDepth = 5`.
  - `public sealed record RailSpan(int From, int To);` — inclusive party-lane indices.
  - `RailLine.Cavern : RailSpan?` (an init property, null unless the line meandered). It lies on the second-to-last
    ride, and for each cell of the span the cells 2..1+`CavernDepth` steps outward (`RailSide`) were solid rock outside
    every room when the line was planned. Plan 10 carves the cavern from it in `LayRails`, right after `CommitRails`.
    It is not folded into `ContentHash`.
  - `internal static List<GridPos> FloorGenerator.Meander(IReadOnlyList<GridPos> cells, int sign, Func<GridPos, bool> ok, IReadOnlySet<GridPos> keep, int addMin, int addMax)`
  - `internal static bool FloorGenerator.SelfClear(IReadOnlyList<GridPos> party, int sign)` — every two 8-adjacent line
    cells (party, or enemy via `Twin`'s source) are at most 3 party indices apart.
  - `internal static List<GridPos>? FloorGenerator.CavernWindow(IReadOnlyList<GridPos> cells, int sign, Func<GridPos, bool> rock)`
  - `public static IEnumerable<string> LayoutReport.Rooms(FloorLayout l)`: one line per room,
    `room {i}: ({X},{Y}) {W}x{H} level={L} {hub|ring|side|secret}` followed by the tags that apply: ` up`, ` down`,
    ` stop={k}`, ` vault={index}`, ` jail`, ` safe`, ` nest`, ` camp`, ` shrine`.
    `public static string? LayoutReport.Rail(FloorLayout l)`:
    `rail: stops=[rooms] rides~[cells x 4 m, platform middle to platform middle] total~{m} cavern={From}..{To}|none`.

- [ ] **Step 1: Write the failing meander unit tests** (`tests/Dungeon/RailMeanderTests.cs`; a straight run heading +x,
  `sign` +1, so outward is +y)

```csharp
static readonly List<GridPos> Run = Enumerable.Range(5, 41).Select(x => new GridPos(x, 20)).ToList();
static bool Open(GridPos p) => p.X >= 1 && p.X <= 58 && p.Y >= 19 && p.Y <= 40;
static readonly HashSet<GridPos> None = new();

[Fact] public void AStraightRun_InOpenRock_GrowsByAtLeastAddMin_AndAnEvenCount()
{ var m = FloorGenerator.Meander(Run, 1, Open, None, 20, 30); Assert.InRange(m.Count - Run.Count, 20, 30); Assert.Equal(0, (m.Count - Run.Count) % 2); }

[Fact] public void TheResult_IsAValidLane()
// m[0] == Run[0], m[^1] == Run[^1]; the first and last 3 cells are unchanged; 4-connected; no cell twice;
// turns at least 2 cells apart; FloorGenerator.Twin(m, 1, out _) != null; FloorGenerator.SelfClear(m, 1)

[Fact] public void Bulges_GoOutwardOnly()          // every cell of m has Y >= 20
[Fact] public void KeepCells_AreNeverTouched()     // keep = cells with X in 20..30, Y in 19..40: m and its twin hold no cell there but Run's own
[Fact] public void NoRoom_ReturnsTheInput()        // ok = p => p.Y is 20 or 21 (the run and its twin): m equals Run
[Fact] public void NeverPastAddMax()               // addMin 100, addMax 12: m.Count - Run.Count <= 12
[Fact] public void SelfClear_FailsForLegsTooClose() // a hand-built U whose legs stand 2 cells apart: false
[Fact] public void CavernWindow_PicksTheStraightRunWithDeepRock()
// Run, rock = Open: a window of >= CavernCells cells, all on Run, not within 3 cells of either end
```

- [ ] **Step 2: Write the failing sweep tests** (`RailLineTests`, Long rows)

```csharp
[Fact] public void Meander_LengthensTheLine()
// over seeds where both Loop and Long (None) have 7 stops: mean Long Party.Count >= 1.4 x mean Loop Party.Count

[Fact] public void AWiderBand_NeverLosesALine()
// for every Loop/Long seed pair (None, Jailbreak): Loop.Rail != null => Long.Rail != null && Long.Stops.Count >= Loop.Stops.Count

[Theory, MemberData(nameof(LongSeeds))] public void LongLines_KeepAWallBetweenLegs(int seed, FloorObjectiveKind obj, LineKind kind)
// Rail != null => FloorGenerator.SelfClear(Rail.Party, sign) where sign is recovered as in LayRails (enemy on RailSide)

[Fact] public void MostLongLines_HaveACavernSpan()
// of Long lines with 7 stops, >= 80% have Cavern != null; each Cavern: To - From + 1 >= CavernCells, all cells straight in one heading,
// between the platforms of Stops[^3] and Stops[^2] in Party order, and for every cell c and t in 2..1+CavernDepth the cell
// c + RailSide*t is Wall or Void, in no room, not a line or pocket cell

[Fact] public void TodaysLines_HaveNoCavern()      // Today and Loop rows: Cavern == null
```

- [ ] **Step 3: Run to see them fail.** Expected: compile errors (`Meander`, `SelfClear`, `CavernWindow`, `Cavern` not
  defined).

- [ ] **Step 4: Implement `Meander`** in `FloorGenerator.Meander.cs`. The signature and the tests do not decide the
  search order, so follow this algorithm:

```text
path = copy(cells); added = 0; L = RailRules.BulgeSpan
while added < addMin:
  placed = false
  for D = BulgeMaxDepth down to 2, while !placed:
    if added + 2D > addMax: continue
    for each k with 3 <= k and k + L <= path.Count - 4, where path[k-2 .. k+L+2] is one straight run of heading d,
        taken in order of |k - path.Count/2|, then k:
      h = RailSide(d, sign)                                   // outward only
      trial = path[0..k] ++ [path[k] + h*t for t = 1..D] ++ [path[k] + h*D + d*t for t = 1..L]
                         ++ [path[k+L] + h*t for t = D-1 down to 1] ++ path[k+L .. end]
      new = the cells of trial at indices k+1 .. k+2D+L-1, plus Twin(trial)'s cells whose source index lies in k .. k+2D+L
      if Twin(trial) != null and every cell of new passes ok and none is in keep and SelfClear(trial, sign):
        path = trial; added += 2D; placed = true; break
  if !placed: break
return path
```

- [ ] **Step 5: Implement `SelfClear` and `CavernWindow`.** `CavernWindow`: take the maximal straight runs of `cells`
  that keep at least 3 cells from either end. Trim 2 cells off each end of each run, and keep those of length
  ≥ `CavernCells`. For each, take the depth: the minimum over the run's cells of the count of `rock(c + RailSide(d, sign) * t)`
  for t = 2, 3, … (capped at `CavernDepth + 3`). Pick the deepest, then the longest, then the one nearest the middle.
  Return null when the best depth is below `CavernDepth`.

- [ ] **Step 6: Wire it into the routing.** `LayRails` passes `meander = spec.RailBand > RailRules.Band` to
  `PlanRails`. In `RouteSegment(…, bool meander, bool cavern)`, after A* returns the plain `cells`:
  1. `keep` = `shadow`, plus (when `a.Level != b.Level`) the six cells round the ramp run that `PlaceRailRamp` would pick
     on the plain cells. Extract that search as `RampIndex(List<GridPos>)`, a pure refactor that must pick the same
     cell as today.
  2. When `cavern` is set, `window = CavernWindow(cells, sign, Rock-and-not-foreign-and-in-no-room)`. When it is not null,
     add the window, its depth box and a one-cell margin to `keep`.
  3. `rideCells = a.Platform.Count / 2 + cells.Count + (b.Platform.Count + 1) / 2`. When
     `rideCells < RailRules.RideMinCells`, set `cells = Meander(cells, sign, Ok, keep, RideMinCells - rideCells, RideMaxCells - rideCells)`.
  4. Continue as today (levels, ramp, `LineCells`, `ValidateLine`, `LaneHeightsOk`). If any of these fails on the
     meandered cells, redo them once on the plain cells, with no window.
  5. `RailSegment` gains `List<GridPos>? Cavern` (the window cells).
  `RouteLine` passes `cavern = meander && next == queue.Count - 2`, which marks the stretch into the last stop before the
  exit. In `PlanRails`, when a way's meandered line fails `PlaceRailPockets` or `ValidateLine`, re-route that way with
  `meander = false` before trying the next way. `CommitRails` sets
  `Cavern = segs[^2].Cavern is { } w ? new RailSpan(party.IndexOf(w[0]), party.IndexOf(w[^1])) : null` (when there are
  at least 2 segments). With `meander` false, no new code runs, so `DefaultKnobs_BuildTodaysFloors` stays green.

- [ ] **Step 7: Implement `LayoutReport`** and print it from `--print-map` (DebugTools.cs:893): one `[map] ` line per
  `Rooms(...)` line, then `[map] ` + `Rail(...)`. Then add a line with the real metres from the cart:
  `[map] rail StopS=[…] rides=[…] m total={…} m` from `RailController.Instance.StopS` (when there is one).
  Unit-test `Rooms` in `RailLineTests`: there is one line per room, and the line for `Stops[k].Room` contains `stop={k}`.

- [ ] **Step 8: Run** `--filter FullyQualifiedName~RailMeanderTests|FullyQualifiedName~Tests.Dungeon`. Expected: PASS.
  `DefaultKnobs_BuildTodaysFloors` must still pass. If `Meander_LengthensTheLine` or `MostLongLines_HaveACavernSpan`
  miss, report the measured numbers. Do not lower the thresholds.

- [ ] **Step 9: Docs.** In docs/development.md, Rails, rewrite the knobs paragraph to cover:
  - `RailCount` 0..6;
  - `RailLoop` (the down stairs in the nearer ring neighbour of the start; an objective that moves the stairs wins);
  - `RailBand` (default 6, the grid grows by it and the floor translates);
  - the meander (outward bulges to 220–330 m a ride, the plain route as the fallback, no rng);
  - `RailLine.Cavern` (what it guarantees, and that plan 10 carves it in `LayRails`);
  - the new `--print-map` room and rail lines.

- [ ] **Step 10: Commit**

```bash
git add src/Dungeon/Generation/FloorGenerator.Meander.cs src/Dungeon/Generation/LayoutReport.cs src/Dungeon/Generation/RailLine.cs src/Dungeon/Generation/FloorGenerator.Rails.cs src/Core/DebugTools.cs docs/development.md
git commit -m "feat(rails): a wider band meanders the line to 220-330 m a ride and keeps a rock-backed span for the cavern

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: The rules for any number of rides

**Files:**
- Create: `src/Dungeon/Rails/Core/LineShape.cs`
- Modify: `src/Dungeon/Rails/Core/RideMusic.cs:41-57` (`TrackFor`), `src/Audio/MusicDirector.cs:244-245`
- Modify: `src/Dungeon/Rails/Core/CartPhysics.cs` (`CartRules.MaxLoadWagons`, `Scaled`)
- Modify: `src/Dungeon/Rails/Core/SetPieceRules.cs` (`StandsFromDeparture`), `src/Dungeon/Rails/RailEncounterController.SetPieces.cs:75`
- Modify: `src/Dungeon/Rails/Core/RideLines.cs:48-58` (`TauntFor`)
- Modify: `src/Dungeon/Rails/RailEncounterController.Kiln.cs:14-20,165-166` (comments; `CueRangeAt` reads `StopS[^2]`)
- Modify: `src/Adventure/Core/Validate/PlanValidator.cs:349-409` (per-ride caps)
- Test: `tests/Dungeon/Rails/LineShapeTests.cs` (new), `RideMusicTests.cs`, `RailWarmupTests.cs:50`, `SetPieceRulesTests.cs`,
  `CartPhysicsTests.cs`, `RideLinesTests.cs:40-53`, `tests/Adventure/RailEncounterPlanTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct LineShape(int Stops) { public int Rides => Math.Max(0, Stops - 1); public int Intermediate => Math.Max(0, Stops - 2); public int LastRide => Rides; public int LastRideStop => Math.Max(0, Stops - 2); public bool IsLastRide(int ride) => ride >= 1 && ride == Rides; }`
  - `RideMusic.TrackFor(int ride, int rides) : string` (Pour on the last ride, Chase otherwise; the one-argument form is removed)
  - `CartRules.MaxLoadWagons = 3`; `CartPhysics.Scaled` uses `Math.Min(crewWagons, MaxLoadWagons)`
  - `SetPieceRules.StandsFromDeparture(string kind) : bool` (`"lone"` or `"thrower"`)
  - `RideLines.TauntFor`: RideStart on every departure, i.e. `stopLeft >= 0 && stopLeft < stopCount - 1`
  - `PlanValidator.RailEncounterCap(int rides) => Math.Max(MaxRailEncounters, 3 * rides)`,
    `PlanValidator.RailSetPieceCap(int rides) => Math.Max(MaxRailSetPieces, 5 * rides)`

- [ ] **Step 1: Write the failing tests**

```csharp
// LineShapeTests
[Fact] public void SevenStops_AreSixRides_FiveCages_TheKilnFromStopFive()
{ var s = new LineShape(7); Assert.Equal((6, 5, 6, 5), (s.Rides, s.Intermediate, s.LastRide, s.LastRideStop)); Assert.True(s.IsLastRide(6)); Assert.False(s.IsLastRide(4)); }
[Fact] public void FiveStops_AreTodaysLine() { var s = new LineShape(5); Assert.Equal((4, 3, 3), (s.Rides, s.Intermediate, s.LastRideStop)); }

// RideMusicTests (replaces TrackFor_RideFourIsThePour_TheRestTheChase)
[Fact] public void TrackFor_TheLastRideIsThePour_TheRestTheChase()
{
    Assert.Equal(RideMusic.PourTrack, RideMusic.TrackFor(4, 4));
    Assert.Equal(RideMusic.PourTrack, RideMusic.TrackFor(6, 6));
    foreach (var ride in new[] { 1, 2, 3, 4, 5 }) Assert.Equal(RideMusic.ChaseTrack, RideMusic.TrackFor(ride, 6));
}
// RailWarmupTests:50 -> TrackFor(ride, 6)

// SetPieceRulesTests: add [InlineData(1, 5, true)] and [InlineData(0, 5, false)] to Jump_OnePumperMakesIt_NoneNever, and
[Fact] public void Golem_ArmsAtItsAt()
{ Assert.False(SetPieceRules.StandsFromDeparture("golem")); Assert.True(SetPieceRules.StandsFromDeparture("lone")); Assert.True(SetPieceRules.StandsFromDeparture("thrower")); Assert.False(SetPieceRules.StandsFromDeparture("slag_cart")); }

// CartPhysicsTests
[Fact] public void Load_CountsAtMostThreeWagons() => Assert.Equal(CartPhysics.TargetSpeed(1, 3), CartPhysics.TargetSpeed(1, 5), 4);

// RideLinesTests (replaces the stop-count cases at :43-46)
[Fact] public void EveryDeparture_StartsARide()
{
    foreach (var stop in new[] { 0, 2, 3 }) Assert.Equal(TauntTrigger.RideStart, RideLines.TauntFor(CartEventKind.Departed, stop, 5));
    foreach (var stop in new[] { 0, 4, 5 }) Assert.Equal(TauntTrigger.RideStart, RideLines.TauntFor(CartEventKind.Departed, stop, 7));
    Assert.Null(RideLines.TauntFor(CartEventKind.Departed, 6, 7));     // the exit never departs
    Assert.Equal("ride6", RideLines.GriselCallFor(CartEventKind.Departed, 5));
}

// RailEncounterPlanTests: a 7-stop layout (TestAdventure.SpecFor(12) with Ring, RailCount 5, RailLoop, Jailbreak;
// the first sweep seed whose Rail has 7 stops)
[Fact] public void ASixRideLine_KeepsEighteenEncounters_AndThirtySetPieces()
// 20 encounters (ride 1..6, kind "lone") and 32 set pieces ("beam"): report says "dropped 2 over the cap of 18" and "dropped 2 over the cap of 30"
[Fact] public void AFourRideLine_KeepsTodaysCaps()   // the existing 25-beam test on the 5-stop layout still drops 5 over the cap of 20
[Fact] public void ASixRideLine_KeepsTenCrew()       // CrewPlanTests-style: 12 crew on the 7-stop layout -> "crew: dropped 2 over the cap of 10"
```

- [ ] **Step 2: Run to see them fail** (`--filter FullyQualifiedName~Tests.Dungeon.Rails|FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~CrewPlanTests`).
  Expected: compile errors (`LineShape`, `StandsFromDeparture`, `TrackFor(int,int)`), then assertion failures (the load
  cap, the taunt rule, the caps).

- [ ] **Step 3: Implement** the Produces list.
  - `MusicDirector` passes `rc.StopS.Length - 1` as `rides`.
  - `RailEncounterController.SetPieces.cs:75` calls `SetPieceRules.StandsFromDeparture`, so `"golem"` now starts in
    the as-the-cart-reaches-it path. `SpawnGolem` is unchanged.
  - `CueRangeAt` uses `_rails.StopS.Length >= 2 ? …StopS[^2]… : MaxCueRange`, which is the same stop as today on a 5-stop line.
  - The Kiln header comment becomes "The last ride, the kiln run", with the golem bursting out "as the cart reaches its At".
  - Update `RideLines`' doc comment.
  - `ValidateRailPlans` uses the two cap functions with `rides` (the cap numbers appear in the repair text).
  - No change is needed in `CrewRescueController` (one wagon per intermediate stop, two crew a wagon), in
    `DungeonBuilder.cs:1092` (`Stops.Count >= 3`), in `PlanValidator.cs:104` (collectives) or `:345` (the crew cap):
    these already read the stop count. Task 8's flows prove it with five wagons.

- [ ] **Step 4: Build and run** `dotnet build DungeonCrawler.csproj` and the Step 2 filter. Expected: build clean, tests PASS.

- [ ] **Step 5: Commit** `feat(rails): any number of rides: the last ride's music, the per-ride caps, every departure a RideStart, a golem at its At, the load counts three wagons`
  (with the Co-Authored-By line; add the files listed under Files except `tests/`).

### Task 6: Floor 12 rides the full circle (layout and rooms)

**Files:**
- Modify: `data/adventures/builtin/campaign.json:519-535` (floor 12 `SpecOverrides`)
- Modify: `data/adventures/builtin/floor_12.json` (`LayoutHash`, `Rooms`, `Collectives`, `Hordes`, `Chests`, `Traps`, `Narration`)
- Modify: `docs/development.md` (the "Floor 12's stops" table at :1155-1163)
- Test: `tests/Adventure/CrewPlanTests.cs:109-114`

**Interfaces:**
- Consumes: `--print-map`'s room and rail lines (Task 4); the per-ride rules (Task 5).
- Produces: floor 12 on the shipped seed with 7 stops, `objective=jailbreak`, and room content re-homed by identity.
  The new room indices are `S0..S6` (= `Rail.Stops[k].Room`), `NEST`, `CAMP`, `VAULT` (the reward vault's room) and
  `JAIL`; Task 7 and the debug flows read them from the plan.

- [ ] **Step 1: Turn it on.** In floor 12's `SpecOverrides`, change `"RailCount": 3` to `"RailCount": 5` and add
  `"RailLoop": true, "RailBand": 12` right after it.

- [ ] **Step 2: Measure.** Run `dotnet build DungeonCrawler.csproj && godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --quit-after=6 2>&1 | grep -E "^\[map\]|\[DungeonBuilder\] floor 12|RailFailure|rails\]"`.
  Expected:
  - `objective=jailbreak rails=7`;
  - the up stairs still in room 1;
  - the down stairs in a ring neighbour of room 1;
  - six rides in `[map] rail … rides=` of 160–360 m each, with a total of at least 1500 m;
  - a cavern span reported.

  If the rides fall short, raise `RailBand` to 14 and then to 16 and measure again (the ruling's ladder). If there are
  fewer than 7 stops, no jailbreak, or still short at 16, stop and report the `[map]` output. Do not pick another seed.
  Save the full `[map]` room list in the scratchpad: Step 4 works from it.

- [ ] **Step 3: Rehash and prove the other floors unchanged.** Run
  `godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`, then `git diff --stat data/`.
  Expected: only `campaign.json` and `floor_12.json` changed, and in `floor_12.json` only `LayoutHash`. Any other floor
  in the diff means a default changed. In that case revert it and find out which default changed (Task 1's pinning test
  should have caught it).

- [ ] **Step 4: Re-home the rooms by identity.** Build the old→new room map from the room list, apply it to every room
  reference in `floor_12.json` (`Rooms[].Index`, `Collectives[].Goal.Room/RallyRoom/Route`, `Hordes[].Room`,
  `Chests[].Room`, `Traps[].Room`, `Npcs[].Room`, `Narration[].Room`), and keep the escaping rule. Script it in the
  scratchpad (Python `json` with `ensure_ascii=True`, then the `'`-style replacements and upper-case hex) and check
  the `git diff` by eye.

| Identity (old room) | What goes with it | New home |
|---|---|---|
| Kiln-Mouth Landing (1, Start) | `npc_grisel` (Room 1) | `S0` (still room 1) |
| The Cooling Stair (5, Exit) | `col_stairbell` (Alarm) | `S6`, the down-stairs room |
| The Ladle Ledge (9, Passage) | `col_stop1`, the Spikes trap, both `RoomEnter` narrations (the Kiln Cages lines) | `S1` |
| Tenders' Quarters (7, Barracks) | `col_stop2` | `S2` |
| The Cinder Breaches (6, Lair) | `horde_breach`, chest 0 (unlocked) | `NEST` (the room of `layout.Nests[0]`) |
| Oddfellows' Fire (4, Camp) | `col_oddfellows`; every `RallyRoom` 4 | `CAMP` (the Camp structure's room) |
| The Founders' Strongbox (10, Treasury) | `col_vault` (Defend) | `VAULT` (the room of `Vaults[Objective.RewardVault]`) |
| The Kiln Cages (11, Jail) | — | `JAIL` (`Objective.JailRoom`) |
| The Casting Floor (0, Hall) | the Fire trap, the patrol's first room | hub 0 |
| The Slag Walk (2), The Pouring Gallery (3), Shelf of Blanks (8) | the locked chest (2); the Fire trap (3) | the ring rooms still unnamed, walked in line order from `S0`, taking the identities in old index order (2, 3, 8); a ring room left over is "The Quench Run" (Passage), then "The Dross Gallery" (Hall) |

Then:
  - `col_stop3` (The Pour Guard) guards `S3`, wherever the Breaches landed.
  - `col_ring`'s route is `[0, S1, the ring rooms strictly between S1 and S2 along the line, S2]`, and its members are
    one `enemy.forge_priest`.
  - `col_stop2`'s members are one `enemy.horde_cinder` and one `enemy.ember_hound`.
  - Append `col_stop4` "The Clinker Watch" (`fac_bellows`, one `enemy.horde_cinder`, Guard and Rally `S4`) and
    `col_stop5` "The Last Lamp" (`fac_bellows`, one `enemy.ember_hound`, Guard and Rally `S5`) after `col_stop3`.
  - `EnemyTotal` stays 16.
  - If `CAMP` is a stop, no `col_stopN` goes there, and the Oddfellows hold that stop's gate.

- [ ] **Step 5: Sweep floor 12 until it is silent.** Run
  `godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2` (the room edits do not change the
  hash, but run it anyway), then
  `timeout 90 godot --headless --path . -- --builtin --floor=12 --quit-after=6 --no-ambush --no-portals --no-gathering 2>&1 | grep -iE "repaired|dropped|demoted|adapted|forced|cannot be|trimmed|clamped|kept only"`.
  Expected: no output, except the known
  `could not place: npc npc_grisel: waits on flag grisel_forgiven` (it does not match the grep). Fix every line it
  prints by moving the identity, never by re-pointing story text.

- [ ] **Step 6: Sweep every floor.** Run the docs loop (`for N in $(seq 1 20) …`, docs/development.md:846) and
  `timeout 120 godot --headless --path . -- --builtin --quit-after=4 2>&1 | grep -c adapted`. Expected: no repair line
  on any floor, and `0`.

- [ ] **Step 7: Update the data test** `BuiltinFloor12_PostsAGroupAtEachIntermediateStop_OnTopOfTheUsualFive`:
  expect `col_stop1` … `col_stop5`. Add `Assert.Equal(16, plan.EnemyTotal)`. Run
  `--filter FullyQualifiedName~CrewPlanTests|FullyQualifiedName~GulletCampaignTests|FullyQualifiedName~StoryRewireTests|FullyQualifiedName~TauntTests`.
  Expected: PASS.

- [ ] **Step 8: Docs.** Rewrite the "Floor 12's stops" table with seven rows (stop, room, name, mobs), from the new
  layout.

- [ ] **Step 9: Commit** `feat(floor12): the line runs the whole ring to the Cooling Stair beside the landing; the plan re-homed onto the new rooms`
  (`git add data/adventures/builtin/campaign.json data/adventures/builtin/floor_12.json docs/development.md`, with the
  Co-Authored-By line).

### Task 7: Floor 12's ten crew, Grisel's six calls, and the six rides

**Files:**
- Modify: `data/adventures/builtin/floor_12.json` (`Crew`, `Npcs[npc_grisel].Calls`, `RailEncounters`, `RailSetPieces`)
- Modify: `docs/development.md` (the crew paragraph at :1165; a new "The six rides" table under Rails)
- Test: `tests/Adventure/CrewPlanTests.cs:97-106`, `tests/Adventure/RailEncounterPlanTests.cs:131-152`

**Interfaces:**
- Consumes: the re-homed floor (Task 6); `PlanValidator` caps (Task 5).
- Produces: the ride ids that the Task 8 flows use: `r1_lone`, `r1_thrower`, `r1_beam`, `r2_slag1..3`, `r3_switch_out`,
  `r3_heap`, `r3_switch_back`, `r3_buckets`, `r3_slag`, `r4_hounds`, `r4_beam`, `r4_buckets`, `r5_lone`, `r5_slag`,
  `r5_beam`, `r6_golem`, `r6_hammers`, `r6_pour`, `r6_trestle`, `r6_jump`, `r6_spur`, `r6_buffer`.

- [ ] **Step 1: Write the failing data tests**

```csharp
[Fact] public void BuiltinFloor12_HasTenCrew_WhoseNamesAreNotCast()   // replaces ..._HasSixCrew_...: 10, distinct ids, no cast names, sex man|woman, a Body
[Fact] public void BuiltinFloor12_GriselNamesEachCageOnItsRide()
{
    var f = ShippedStory.Floor(12);
    var grisel = f.Npcs.Single(n => n.Id == "npc_grisel");
    for (var n = 1; n <= 5; n++)
    {
        var call = grisel.CallFor($"ride{n}")!.Text;
        Assert.Contains(f.Crew[2 * n - 2].Name, call);
        Assert.Contains(f.Crew[2 * n - 1].Name, call);
    }
    Assert.StartsWith("Last cage.", grisel.CallFor("ride5")!.Text);
    Assert.DoesNotContain("Last cage", grisel.CallFor("ride3")!.Text);
    Assert.All(f.Crew, c => Assert.DoesNotContain(c.Name, grisel.CallFor("ride6")!.Text));
    Assert.All(grisel.Calls, c => Assert.Null(c.Line.VoiceId));
}
[Fact] public void BuiltinFloor12_HasTheSixRides()   // replaces BuiltinFloor12_HasTheAuthoredRides
// 10 encounters, 15 set pieces; every ride 1..6 has at least one; ids and (Ride, Kind, At, Offset) exactly as the table below;
// r1_lone, r2_slag1 and r6_golem keep today's lines and are the only 3 with a Line; every slag cart <= 3 riders;
// r6 set pieces in order: hammers, pour, trestle, jump, spur, buffer
```

- [ ] **Step 2: Run to see them fail** (`--filter FullyQualifiedName~CrewPlanTests|FullyQualifiedName~RailEncounterPlanTests`).

- [ ] **Step 3: Write the crew and the calls.**
  - Keep the six crew in their order and append `crew_hal` "Hal Sumpter" (man, `npc_young_man`), `crew_bet` "Bet Wynn"
    (woman, `npc_young_woman`), `crew_ivo` "Ivo Rill" (man, `npc_prisoner`) and `crew_nan` "Nan Gully" (woman,
    `npc_old_woman`).
  - Checked against the cast, `OfflineDesigner`'s name lists, `ObjectivePlanner`'s prisoner and ritualist names and
    every shipped plan: none of the four clashes.
  - All of Grisel's calls have `"VoiceId": null`. Use this exact copy (the escaping applies):

| Key | Text |
|---|---|
| ride1 | unchanged |
| ride2 | unchanged |
| ride3 | `Juno Brack and Selby Crane next. Selby said we shouldn't go down unpaid. She was right.` |
| ride4 | `Hal Sumpter and Bet Wynn. Hal carried my lamp the day I ran. I let him think I'd come back.` |
| ride5 | `Last cage. Ivo Rill and Nan Gully. Nan wanted to turn back at the first grate. I called her old.` |
| ride6 | `That's the last cage behind us. Get us to the stair before he thinks of something worse.` (today's ride4) |
| cage | unchanged |

- [ ] **Step 4: Write the six-ride table.** Rides 1–3 keep their pieces and `At`s. Ride 4's kiln pieces move to ride 6
  under `r6_` ids. The new rides use today's kinds only, and later plans fill the slots marked below.

| Ride | Encounters (kind, At, Offset, riders) | Set pieces (kind, At) | Slots left for later plans |
|---|---|---|---|
| 1 The Ore Run | `r1_lone` lone 0.45 (cinder, line kept); `r1_thrower` thrower 0.8 (priest) | `r1_beam` beam 0.6 | plan 11: `r1_barricade` ~0.25 |
| 2 Side by Side | `r2_slag1` 0.15 −6 (2 cinders, line kept); `r2_slag2` 0.45 −4 (priest, hound); `r2_slag3` 0.75 −3 (hound, 2 cinders) | — | plan 11: `r2_barricade` ~0.6, defended |
| 3 The Switchyard | `r3_slag` 0.3 +8 (brute) | `r3_switch_out` 0.2; `r3_heap` 0.35; `r3_switch_back` 0.55; `r3_buckets` 0.75 | plan 11: `switch_back` live and later, the broken bridge past the last return |
| 4 The Dark | `r4_hounds` slag_cart 0.35 −4 (2 ember hounds) | `r4_beam` beam 0.65; `r4_buckets` buckets 0.8 | plan 12: the dark stretch, the runaway from ~0.2, the split ~0.5 |
| 5 The Throat | `r5_lone` lone 0.2 (cinder); `r5_slag` slag_cart 0.55 −3 (cinder, priest) | `r5_beam` beam 0.8 | plans 10/13–15: the cavern on `Rail.Cavern`, its bridge and spiral, the leap beside `r5_slag`, the loop |
| 6 The Pour | `r6_golem` golem **0.55** (forge golem, line kept) | `r6_hammers` 0.2; `r6_pour` 0.4; `r6_trestle` 0.6; `r6_jump` 0.75; `r6_spur` spur_switch 0.85; `r6_buffer` 0.95 | plan 10: the trestle and jump over a crevasse; plan 11: `r6_barricade` ~0.9 |

  Encounter rider ids are `enemy.horde_cinder`, `enemy.forge_priest`, `enemy.ember_hound`, `enemy.horde_cinder_brute`
  and `enemy.forge_golem`, as today.

- [ ] **Step 5: Run** the Step 2 filter, then the floor 12 sweep from Task 6 Step 5. Expected: PASS and no output.

- [ ] **Step 6: Docs.**
  - The crew paragraph: "The ten named crew … two per wagon across five stops".
  - Add a "The six rides" table (the one above, slots included) under Rails, next to the stops table.
  - Update the golem's paragraph (:1466): it bursts out as the cart reaches its `At` on the last ride.
  - Update the RideStart line (:1555): every departure.
  - Update the music line (:257): the last ride plays the pour.

- [ ] **Step 7: Commit** `feat(floor12): ten crew in five cages, Grisel's six calls, the six rides from today's pieces with slots for the new ones`
  (with the Co-Authored-By line).

### Task 8: The debug flows and the RIDE pass for any stop count

**Files:**
- Create: `src/Core/DebugTools.RideOn.cs` (the ride-on helper)
- Modify: `src/Core/DebugTools.Chase.cs`, `DebugTools.Crew.cs`, `DebugTools.Ride.cs`, `DebugTools.RideEdges.cs`,
  `DebugTools.CartWatchdog.cs`, `DebugTools.Relief.cs`, `src/Core/DebugTools.cs:302-320` (`_quitAfter`s),
  `src/Net/NetTestFlow.cs`, `src/Dungeon/Rails/RailNet.cs:314-321`, `scripts/net_test.sh:52-200`
- Modify: `docs/development.md` (:354-356, :1286-1296, :1508-1535, :1951, :2027-2075)

**Interfaces:**
- Consumes: `LineShape` (Task 5); the `r1..r6` ids (Task 7); `rc.Restore(int)`, `rc.DebugKillHolders(int)`, `RideBoard`,
  `RideWaiveCages`, `HoldsAStop` (existing).
- Produces: `private bool RideOnTo(Main main, RailController rc, int stop, string tag, Action<string> fail)`. It is called
  every tick and returns true once the cart is held at `stop`. On the way, at each held intermediate stop, it kills the
  stop's holders (`rc.DebugKillHolders`), lets the gate rise, leaves the cage as the flow's cage handling does
  (`RideWaiveCages`), and boards and departs the way each flow already does at stop 3.

The rule for every literal, and the grep sites to apply it to (from
`grep -nE "ArmedRide ?(==|>=|!=) ?[0-9]|StopIndex ?(==|!=|>=|<=|<|>) ?[0-9]|StopS\[[0-9]\]|StopS\.Length ?[!=<>]=? ?[0-9]|SpareAt\([0-9]\)|GateUp\([0-9]\)|HammerIndexOf\([0-9]\)|\"r4_" src/Core/DebugTools.*.cs src/Net/NetTestFlow.cs src/Dungeon/Rails/RailNet.cs`):
- With `var shape = new LineShape(rc.StopS.Length)`: a literal that names **the kiln** becomes `shape.LastRide` (was
  ride 4) or `shape.LastRideStop` (was stop 3 as the kiln's start), and **the end of the line** becomes
  `shape.Rides` (was stop 4). Counts of **wagons, cages, gates or spares** become `shape.Intermediate` (was 3), and
  **crew** become `2 * shape.Intermediate` (was 6). `"r4_*"` becomes `"r6_*"`.
- A literal that names **rides 1–3 or stops 1–3 for what they are** stays: the landing, the lone cinder, the slag carts
  of ride 2, the switch of ride 3, "arriving at stop 3 sets ride 3's lever back", and a save between stops 1 and 2.
- The expectation `rc.StopS.Length != 5` becomes `!= 7`, and the wagons expectation `{ 1, 2, 3 }` becomes
  `1..shape.Intermediate`. The Crew flow's gates-down count is the number of intermediate stops whose room holds a
  Guard or Defend collective in `main.Builder.Contents` (5 on the shipped floor).
- `DebugTools.Ride.cs`'s `Stop3Check` becomes `StopsCheck`: every hammer and the pour, against **every** stop, must end
  its cue range at least 2 m short of a cart held there.
- Flows that ride past stop 3 (`--test-chase`, `--test-crew`, the RIDE pass) ride rides 4 and 5 for real with `RideOnTo`.
  `--test-cart-watchdog` keeps its shortcut, as `rc.Restore(shape.LastRideStop)`.
- The chase flow's one-pumper jump sets up **every** wagon coupled (`rc.Train.Count == shape.Intermediate`, 5) and must
  still make the jump with one pumper. The golem check gains:
  the `(party S=…)` printed in `[rails] golem r6_golem … bursts onto the enemy line` must be at or past
  `rc.StopS[5] + 0.55 × (rc.StopS[6] − rc.StopS[5]) − 1`.
- The RIDE pass: the joiner's stop-3 sequence moves to `shape.LastRideStop`, and both clients ride rides 4 and 5 seated,
  every departure a real one. That sequence is: on foot beside the spare while the train is held, the train leaving
  without it, `spare:N`, the chaser hopping the gap and the trestle, `chase-done`. `RailNet`'s hammer probe reads
  `HammerIndexOf(shape.LastRide)`. In `net_test.sh`, the strings that name the kiln's stop or ride change to match
  (`stop 3's spare` → `stop 5's spare`, `spare:3` → `spare:5`, `departs stop 3 with 1 player` → `departs stop 5 …`,
  `ride 4's gap` → `ride 6's gap`). Strings about ride 3's switch and heap stay.

- [ ] **Step 1: Implement `RideOnTo`** and apply the rule to each file listed under Files.

- [ ] **Step 2: Build and run each solo flow.** Run `dotnet build DungeonCrawler.csproj` once, then each of these on its
  own, waiting for each to finish:
  - `godot --headless --path . -- --test-ride`
  - `godot --headless --path . -- --test-ride-edges`
  - `godot --headless --path . -- --test-cart-watchdog`
  - `godot --headless --path . -- --test-relief` (and `=rails`, `=refuse`, `=ahead`)
  - `godot --headless --path . -- --test-crew` (and `--test-crew=forgiven`)
  - `godot --headless --path . -- --test-chase`

  Expected: each prints its `RESULT ok` line. Where a flow ran out of time because the line is longer, set its
  `_quitAfter` (DebugTools.cs:302-320) to the measured run time × 1.5, rounded up to 30 s, and run it again. Do not
  raise a per-step timeout to hide a stuck cart. Look at the step that stalled first.

- [ ] **Step 3: Run the RIDE pass alone.** Run `RIDE=1 scripts/net_test.sh`. Expected: `[net-server] RESULT ok`,
  `[net] RESULT ok` for the client and the joiner, and no `NET TEST: FAIL`. If the server, client or joiner quits early,
  set its `--quit-after` in the script to the measured time × 1.5. Note that seed 4242's floor 12 is not the shipped
  seed's. If it gets fewer than 5 rides, stop and report its `[map] rail` line. Then run plain `scripts/net_test.sh`
  (both of its RESULT lines must pass).

- [ ] **Step 4: Run the whole suite and the sweep.** Run `dotnet test tests/DungeonCrawler.Tests.csproj` and the
  20-floor sweep from Task 6 Step 6. Expected: all tests pass, there are no repair lines, and `adapted` counts 0.

- [ ] **Step 5: Docs.** In docs/development.md:
  - correct the flag lines at :354-356 (ride 6's gap; the RIDE pass's measured minutes);
  - correct the `--test-chase` paragraph (:1508: "rides the six rides", the full-train jump, the golem at its `At`);
  - correct the watchdog paragraph (:1951);
  - in the "Riding together" RIDE-pass section (:2027-2075), replace "stop 3" and "ride 4" with the kiln's stop and ride
    wherever they name the kiln, and give the new time.

- [ ] **Step 6: Commit**

```bash
git add src/Core/DebugTools.RideOn.cs src/Core/DebugTools.Chase.cs src/Core/DebugTools.Crew.cs src/Core/DebugTools.Ride.cs src/Core/DebugTools.RideEdges.cs src/Core/DebugTools.CartWatchdog.cs src/Core/DebugTools.Relief.cs src/Core/DebugTools.cs src/Net/NetTestFlow.cs src/Dungeon/Rails/RailNet.cs scripts/net_test.sh docs/development.md
git commit -m "test(rails): the ride, crew, chase, watchdog and relief flows and the RIDE pass read the line's own stop count: six rides on floor 12

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
