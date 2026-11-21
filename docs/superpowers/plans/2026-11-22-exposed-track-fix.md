# Exposed Track Fix (the rails never under the floor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No stretch of any mine-cart track lies under the drawn floor or inside a wall. Floor 12's `--track-depth` verdict reads `buried=0 inside=0`, and every placement on floor 12 that depends on S still meets its rule.

**Architecture:**
- **Buckled tiles (case 1):** `PickBuckled` moves into an engine-free `BuckleRules.Pick`. A rail lane cell still makes both rng draws, but its buckle is never stored, so the cell gets the plain flat slab. Every other tile and every hash stay as they are.
- **Ramps (cases 2-4):** the track follows the drawn walk surface (`RailBed.SurfaceAt`, a function of the 2D point, not of the lane index). Each ramp end is eased over 1 m with a tangent parabola, and the 0.0118 m slab rise is included. `DungeonBuilder.Lay` lays lanes with a new `RailCurves.CentrelineOver`. Only the builder changes: no generator code, no `LayoutHash`.
- **Measurement:** a `--track-depth[=CM]` flag on `--print-map` probes every `RailTrack` against the drawn floor (rays and point queries in `_PhysicsProcess`). It also prints the line's ramps, its set pieces' S values and the spared buckles. It is the proof for every task and the gate's record.
- **Case 5 (`r5_spur` into a wall):** the measurement settles it. If confirmed, the spur bends less when rock lies beyond (dressing only).
- **The gate:** floor 12's S-based placements are re-measured, and every floor-12 flow is run. A new seated-view flow, `--test-track-view`, checks a ramp end and a formerly heaved lane cell with rays from the seated eye and windowed screenshots.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `.superpowers/sdd/track-under-floor-investigation.md` (the causes with file:line references, the fix, and the `--track-depth` measurement design: probes, output lines, expected numbers). Read it whole: this plan does not repeat its probe design. The controller's ruling is in the last lines of `.superpowers/sdd/2026-11-22-mine-cart-12-dark-runaway/progress.md`. Line numbers drift, so find code by name. `docs/development.md` (Rails, The five rides, Crevasses) says how floor 12 behaves today.

**Builds on:** plan 12, as closed by the controller. Run this before plan 13.

**Not measured by the planner.** Godot was not run, because a co-op test may be running. Task 1 measures the baseline, and Tasks 2-5 measure against it. The investigation's expectations for today:
- one short run per buckled lane cell (≤ ~10 cm at the rails);
- two runs per rail ramp, ~2 m each and ~26-27 cm at the rails;
- 1-3 rail ramps on floor 12.

**Predicted S drift** (Ruling 5): every S past a ramp grows by ≈0.226 m per ramp on that lane. Set pieces are ride fractions, so a piece on a ride with a party ramp drifts against the geometry:
- by +f·0.226 m if the piece lies before the ramp;
- by −(1−f)·0.226 m if it lies past it.

A piece on a ride with no ramp does not drift. Every drift is ≤ 0.23 m per ramp.

**Planner's rulings** (mine; the controller's (a)-(c) are taken as given; each ruling says what it costs if wrong):
1. A spared rail cell is any cell where `layout.IsRail(p)` holds: party or enemy lane, platforms included. It gets `ItemFloor` and its box collision. Pockets and every other cell buckle exactly as today. There is no "humps beside the rails" variant. Cost: ruin-room and rubble-side lane cells look less heaved; the masked-item variant in the investigation is the later polish.
2. The bed is `RailBed.SurfaceAt(layout, xz)`, the walk surface under the 2D point:
   - flat cells: `LevelOf·4`;
   - ramp cells: `LowHeight + RampSlabRise + 0.5·u` along `Dir`.

   It is eased over `EaseLength` = 1 m at every ramp end. A point function, not a lane-index function, so an enemy-lane inside corner arcing onto a ramp is exact. Cost: at each crest the track base sinks ≤ 7 cm (6.25 cm from the ease, plus the slab's 1.2 cm step at the edge) over ~0.7 m. The sleeper tops stay ≥ 5 cm and the rail tops ≥ 0.17 m proud. Each sag floats ≤ 7 cm.
3. `RampSlabRise` = 0.0118 is part of the ramp line, which fixes the gravel z-fight (case 4): the ballast clears the slope by 1.8 cm. Cost: none.
4. The new lane builder is a new name, `RailCurves.CentrelineOver`, not an overload. A second `Centreline` taking `Func<Vector2,float>` would make every `_ => 0f` call ambiguous (CS0121). `Centreline` stays as it is, for its tests and as "today" in the ride-feel test. Cost: none.
5. The S shift is accepted and nothing is retuned. The gate compares every placement rule before and after. A misfit stops the task and is reported to the controller, with no silent At change. Cost: a placement sitting on a rule's edge may flip, and the controller then rules.
6. `Protocol.Version` stays 17 (the unreleased-branch rule). Old and new builds must not be mixed in co-op. Saves keep only the stop index (`RailController.Restore(stopIndex)`), so old saves load unchanged. Cost: none on the branch.
7. Ride feel: no change to `CartPhysics`, `ComfortRules` or `CartFeel`. The planner's simulation of the 2-cell ramp at 60 Hz, cart at cruise 6 m/s, gives:

   | measure | today | eased |
   |---|---|---|
   | climb, min V (rope) | 3.36 m/s | 3.06 m/s |
   | descent, max V | 7.23 m/s | 7.55 m/s |
   | descent, max \|accel\| | 1.79 m/s² | 2.91 m/s² (under `AccelTo` 4) |
   | Strong's drop fade | fires at every crest | fires at every crest |
   | steepest pitch change | 0.49 rad/m | 0.46 rad/m |

   - The rope still pulls on the whole climb: the slope is 0.447, well over `ClimbSlope` 0.05.
   - A headset's room is never pitched (`Adventurer.FollowSeat`). Pitch reaches only the desktop camera, as `CartFeel.PitchShare` 0.4 of the cart's pitch.
   - Task 3's `RampRideTests` pins these numbers.

   Cost if wrong: Recommended narrows more on descents. The remedy is `EaseLength` 2 m, at the price of 12.5 cm crest dips.
8. `--track-depth` defaults to 3 cm and follows the investigation's design. Anything it finds that is not cases 1-5 stops the task and is reported. Cost: none.
9. Case 5, if confirmed (Task 4): `SetPieceRules.SpurBend(roomBeyond) = min(1.8, roomBeyond − 0.8 − 0.1)`, so 1.1 m against rock and 1.8 m otherwise. The stop beam (±0.8 m) and the sleepers then stay inside the lane cell, against the wall face. No rock is carved, because that would change the layout hash. Cost: against rock the spur bends visibly less.
10. `--test-track-view` rides from the seat (`rc.Restore`, `RideBoard`, the lead pumping), with the ride-4 runaway held (plan 12 Ruling 22's pattern).
    - It aims the seated eye at the target and casts layer-1 rays to the rail tops.
    - A negative control (a ray to 0.10 m under the floor must be blocked) keeps the check honest.
    - Windowed with `--screenshot=PATH`, it saves three PNGs for the controller to view.

    Cost: one windowed run of ~1-2 min.

## Global Constraints

- **Hashes do not move.** No generator code changes. `RailLineTests.DefaultKnobs_BuildTodaysFloors` stays green in every task. `--rehash-plans=data/adventures/builtin` rewrites nothing (`git diff --stat data/` is empty). Floor 12 still prints `objective=jailbreak rails=6`, five rides, a cavern span, `break=` on ride 3 and `split=` on ride 4.
- **The rng order is kept.** `BuckleRules.Pick` draws `NextDouble()` and then `Next(Variants)` for exactly the cells and in exactly the order `PickBuckled` does today, from `new Random(layout.Seed ^ 0xB0C1)`. `RailBed` uses no rng.
- **The wire.** `Protocol.Version` stays **17**. No DTO changes.
- **Assets and voice.** No new assets and no voice spend. Never run a Runware or voice bake.
- **Tests.** `tests/` (xUnit) is not in git. Write and run tests locally, never `git add tests/`, and commit only `src/` and `docs/`.
- **Headless runs.**
  - Run `dotnet build DungeonCrawler.csproj` before any `godot --headless --path . -- <flags>` run. Engine flags go before `--`, game flags after it.
  - Every check greps its own flow's `[...] RESULT` line, never the early `[builtin] RESULT ok`.
  - Use `--test-crew --quit-after=570` and `--test-chase --quit-after=750`.
  - Never `pkill -f` a pattern that matches your own command; end runs with `--quit-after=N`.
- **One godot at a time**, unless the controller has shipped `scripts/flow.sh` (parallel solo flows, each with its own `XDG_DATA_HOME`): then use it as it says. Before any run, check with the controller that no co-op test is running.
- **The RIDE pass.** `RIDE=1 scripts/net_test.sh` is run by the controller, alone. `cat /sys/firmware/acpi/platform_profile` must not read `low-power`; a run in low-power is invalid, not a failure. It is flaky at baseline: rerun once a failure in a step this plan did not touch.
- **Full suite:** `dotnet test tests/DungeonCrawler.Tests.csproj`.
- **Commits.** Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Stage by explicit path. No installers or dist builds (`export_all.sh`, `package.sh`, `publish_*.sh`), no push, no merge.
- **Ledger.** Save every `--track-depth` and flow output you quote under `.superpowers/sdd/2026-11-22-exposed-track-fix/` (git-ignored), named `<task>-<what>.txt`.

## Review Focus

- **The enemy lane's inside corner touching a ramp** (`Twin` drops the duplicate, so a 3 m arc runs onto the ramp's first metre). The track must lie on the drawn ramp there too. Pinned by `RailBedTests.EveryLanePointNearARamp_LiesOnTheDrawnFloor`, a sweep over the floor-12-knob seeds, both lanes (Task 3).
- **A commanded cart on the steeper ramp** (the runaway at cruise + 15 %, a slag cart pacing the party, the relief cart). It must be hauled up at the rope's speed and never past its command. Pinned by `RampRideTests`' commanded climb row (Task 3) and by `--test-runaway=tail`, `--test-chase` and `--test-relief` (Task 5).
- **A walker on foot over a spared lane cell or a ramp crest.** The step must be flat, with no hump and no lip. Pinned by `BuckleRulesTests` (Task 2), `--test-relief=rails` and `--test-crew` (Task 5).
- **A ramp cell under a bridge deck.** The track must lie on the ramp's slope, which is the deck. Pinned by the Task 3 sweep (`DrawnAt` uses the ramp line on a deck ramp) and by the probe, which accepts `Chasms/Footings` hits (Task 1).
- **A co-op client on the new build.** Both peers must derive the same longer S. Pinned by `RIDE=1 scripts/net_test.sh` (Task 5, the controller).

---

### Task 1: The `--track-depth` measurement and the baseline

**Files:**
- Create: `src/Dungeon/Rails/Core/TrackDepthRules.cs` (engine-free)
- Create: `src/Dungeon/Generation/RailBed.cs` (only `LaneRamp` and `RampsOn` in this task)
- Create: `src/Core/DebugTools.TrackDepth.cs`
- Modify: `src/Core/DebugTools.cs`:
  - the parse, next to `--print-map`;
  - the `--print-map` block defers the ASCII print and the quit while a probe is due;
  - the `_PhysicsProcess` hook;
  - the help lines.
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`DebugPieces`)
- Test: `tests/Dungeon/Rails/TrackDepthRulesTests.cs`, `tests/Dungeon/RailBedTests.cs` (the `RampsOn` rows)

**Interfaces:**
- Produces (engine-free, `namespace DungeonCrawler.Dungeon.Rails`):
  - `public enum DepthCause { Buckled, RampCrest, RampSag, Wall, Plinth, Other }`
  - `public readonly record struct DepthHit(string Track, float S, GridPos Cell, string Probe, float Under, DepthCause Cause)`
  - `public sealed record DepthRun(string Track, float FromS, float ToS, IReadOnlyList<GridPos> Cells, int Samples, float Worst, string Probe, DepthCause Cause)`
  - `public static class TrackDepthRules`:
    - constants: `Step = 0.5f`, `RailHalf = 0.45f`, `RailTop = 0.24f`, `SleeperHalf = 0.7f`, `SleeperTop = 0.12f`, `MidRail = 0.06f`, `RayUp = 1.2f`, `RayDown = 1f`, `MaxCasts = 5`, `DefaultCm = 3`;
    - `public static bool Buried(float under, int thresholdCm)`: `under > thresholdCm / 100f`;
    - `public static DepthCause Cause(bool buckled, bool rampNear, bool floats, string? insideItem, bool insideBelowLane)`. The first rule that applies wins:
      1. an `insideItem` starting with `"Ramp"` → `RampCrest`;
      2. any other `insideItem`: `Plinth` when `insideBelowLane`, else `Wall`;
      3. `buckled` → `Buckled`;
      4. `rampNear` → `floats ? RampSag : RampCrest`;
      5. otherwise `Other`.
    - `public static IReadOnlyList<DepthRun> Runs(IReadOnlyList<DepthHit> hits, float step = Step)`:
      - hits on one track whose distinct S values follow each other within `step + 0.01` merge into one run;
      - `Samples` counts the run's distinct S values;
      - `Worst`, `Probe` and `Cause` come from the hit with the largest `Under`;
      - `Cells` are distinct, in S order;
      - runs are ordered by track, then by S.
    - `public static string Verdict(int buried, int inside, int runs, float worstM, int buckledLaneCells, int ramps)` returns `"total buried={b} inside={i} runs={r} worst={cm}cm buckledLaneCells={n} rampsOnLine={k}"`, with `cm = (int)MathF.Round(worstM * 100f)` and invariant culture.
- Produces (engine-free, `namespace DungeonCrawler.Dungeon.Generation`):
  - `public sealed record LaneRamp(int Index, GridPos Low, GridPos High, GridPos Dir, bool Climb)`:
    - `Index` is the lane index of the ramp's first cell in lane order;
    - `Low` and `High` are its two cells, by `LowHeight`;
    - `Climb` holds when the lane runs along `Dir`.
  - `public static IReadOnlyList<LaneRamp> RailBed.RampsOn(FloorLayout layout, IReadOnlyList<GridPos> lane)`: each pair of consecutive lane cells that are both in `layout.Ramps` with the same `Dir`.
  - `public static Vector2 RailBed.SagEdge(LaneRamp r, float cell = 4f)`: the centre of `Low` − `Dir`·cell/2.
  - `public static Vector2 RailBed.CrestEdge(LaneRamp r, float cell = 4f)`: the centre of `High` + `Dir`·cell/2. Both are in world XZ.
- Produces (Godot):
  - `internal IEnumerable<(string Id, int Ride, string Kind, float S)> RailEncounterController.DebugPieces()`, every built piece in index order.
  - `--track-depth[=CM]` (with an optional `--track-depth-float`):
    - It implies `--print-map` and sets `_quitAfter = Math.Max(_quitAfter, 20)`.
    - The print block sets `_trackDepthDue` and returns before the ASCII print. `_PhysicsProcess` then runs `RunTrackDepth()`, prints the ASCII map and quits.
    - Tracks: every `RailTrack` under `Builder.FloorRoot`, recursively. The label is `PartyTrack` or `EnemyTrack`, else `{parent.Name}/{Name}` (for example `Spur_r5_spur/Spur`).
    - Probes, ray filter, intersections, causes and output lines are as the investigation's "Headless measurement" section specifies. The numbers come from `TrackDepthRules`.
    - It also prints:
      - `[track-depth] ramp {track} ride={k} cells={Low}->{High} {climb|descent} sag S={s:0.0} crest S={c:0.0}`, with the S values from `ClosestS` of the edge points at `FloorTop + LowHeight(Low)` and `FloorTop + HighHeight(High)`;
      - `[track-depth] piece {Id} ride={Ride} kind={Kind} S={S:0.0}`, one per `DebugPieces()` entry;
      - `[track-depth] cavern party S={from:0.0}..{to:0.0}`: the near edge of `Cavern.From` to the far edge of `Cavern.To`;
      - the verdict line, last.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Runs_MergeConsecutiveSamplesOnOneTrack_AndSplitOnAGapOrATrackChange()
{
    var c = new GridPos(22, 51);
    var runs = TrackDepthRules.Runs(new[]
    {
        new DepthHit("PartyTrack", 309.0f, c, "railL", 0.20f, DepthCause.RampCrest),
        new DepthHit("PartyTrack", 309.0f, c, "railR", 0.27f, DepthCause.RampCrest),
        new DepthHit("PartyTrack", 309.5f, c, "mid", 0.10f, DepthCause.RampCrest),
        new DepthHit("PartyTrack", 310.0f, new GridPos(22, 50), "railL", 0.05f, DepthCause.RampCrest),
        new DepthHit("PartyTrack", 311.0f, c, "railL", 0.04f, DepthCause.Buckled),
        new DepthHit("EnemyTrack", 309.5f, c, "railL", 0.26f, DepthCause.RampCrest),
    });
    Assert.Equal(3, runs.Count);
    Assert.Equal(("EnemyTrack", 1), (runs[0].Track, runs[0].Samples));
    Assert.Equal((309.0f, 310.0f, 3, 0.27f, "railR"), (runs[1].FromS, runs[1].ToS, runs[1].Samples, runs[1].Worst, runs[1].Probe));
    Assert.Equal(new[] { c, new GridPos(22, 50) }, runs[1].Cells);
    Assert.Equal((311.0f, DepthCause.Buckled), (runs[2].FromS, runs[2].Cause));
}

[Theory]
[InlineData(false, false, false, "Ramp(1, 0)1", false, DepthCause.RampCrest)]
[InlineData(true, true, false, "Wall", true, DepthCause.Plinth)]
[InlineData(true, false, false, "Wall", false, DepthCause.Wall)]
[InlineData(true, true, false, null, false, DepthCause.Buckled)]
[InlineData(false, true, false, null, false, DepthCause.RampCrest)]
[InlineData(false, true, true, null, false, DepthCause.RampSag)]
[InlineData(false, false, false, null, false, DepthCause.Other)]
public void Cause_PrefersAnIntersectionThenABuckleThenARamp(bool buckled, bool rampNear, bool floats, string? inside, bool below, DepthCause want)
    => Assert.Equal(want, TrackDepthRules.Cause(buckled, rampNear, floats, inside, below));

[Fact] public void Buried_IsStrictlyOverTheThreshold()
{
    Assert.False(TrackDepthRules.Buried(0.03f, 3));
    Assert.True(TrackDepthRules.Buried(0.031f, 3));
    Assert.False(TrackDepthRules.Buried(-0.1f, 3));
}

[Fact] public void Verdict_IsInvariant()
{
    var was = System.Globalization.CultureInfo.CurrentCulture;
    System.Globalization.CultureInfo.CurrentCulture = new("de-DE");
    try { Assert.Equal("total buried=29 inside=2 runs=13 worst=27cm buckledLaneCells=17 rampsOnLine=2", TrackDepthRules.Verdict(29, 2, 13, 0.268f, 17, 2)); }
    finally { System.Globalization.CultureInfo.CurrentCulture = was; }
}
```

In `RailBedTests`, the hand-built layout is used here and in Task 3. `internal static FloorLayout Strip()` returns a `FloorLayout(1, 8, 3)` with:
- row y = 1 all `Cell.Floor`;
- `Ramps[(2,1)] = new RampInfo(new GridPos(1,0), 0)` and `Ramps[(3,1)] = new RampInfo(new GridPos(1,0), 2)`, with both cells `Cell.Ramp`;
- `SetLevel` 1 for x = 4..7.

```csharp
[Fact] public void RampsOn_FindsTheRampAndItsWay()
{
    var l = Strip(); var lane = Enumerable.Range(0, 8).Select(x => new GridPos(x, 1)).ToList();
    var r = Assert.Single(RailBed.RampsOn(l, lane));
    Assert.Equal((2, new GridPos(2, 1), new GridPos(3, 1), true), (r.Index, r.Low, r.High, r.Climb));
    Assert.Equal(new Vector2(8f, 6f), RailBed.SagEdge(r));
    Assert.Equal(new Vector2(16f, 6f), RailBed.CrestEdge(r));
    lane.Reverse();
    var d = Assert.Single(RailBed.RampsOn(l, lane));
    Assert.Equal((4, false), (d.Index, d.Climb));
}
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~TrackDepthRulesTests|FullyQualifiedName~RailBedTests"`. Expected: a compile error (`TrackDepthRules`, `RailBed`).
- [ ] **Step 3: Implement** the Interfaces above. Filter the probe's ray by RID: add each rejected collider to `Exclude` and cast again, up to `MaxCasts` in all. Point queries use `CollisionMask = 1`. A GridMap hit names `grid.MeshLibrary.GetItemName(grid.GetCellItem(grid.LocalToMap(grid.ToLocal(p))))`, and `insideBelowLane` is that map cell's Y < `LevelOf(lane cell)`.
- [ ] **Step 4: Run them.** Run the filter; expect PASS. Then run `dotnet build DungeonCrawler.csproj && timeout 120 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth=3 --track-depth-float 2>&1 | grep -E "^\[track-depth\]|^\[map\] rail|^\[rails\]" > .superpowers/sdd/2026-11-22-exposed-track-fix/t1-baseline.txt`.
  - Expected: the verdict line with `buried>0`, `buckled*` lines and `ramp-crest` runs (~26-27 cm), one `ramp` line per party ramp and per enemy ramp, and every piece line.
  - Record in the report:
    - the verdict;
    - every `ramp` line, with its ride;
    - which pieces lie on a ramp's ride, and before or after the ramp;
    - whether any `Spur_r5_spur/Spur` line says `inside` or `buried` (case 5);
    - any line whose cause is not cases 1-5. If there is one, stop and report.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/TrackDepthRules.cs src/Dungeon/Generation/RailBed.cs src/Core/DebugTools.TrackDepth.cs src/Core/DebugTools.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs
git commit -m "test(rails): --track-depth[=CM] probes every track against the drawn floor (rails, sleepers, walls) and lists the line's ramps, pieces and cavern S

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Rail lane cells are never buckled (case 1)

**Files:**
- Create: `src/Dungeon/Generation/BuckleRules.cs`
- Modify: `src/Dungeon/DungeonBuilder.cs`:
  - `PickBuckled` calls `BuckleRules.Pick`;
  - `BuckledVariants` becomes `BuckleRules.Variants`;
  - add `SparedRailBuckles`.
- Modify: `src/Core/DebugTools.TrackDepth.cs` (the `spared` line)
- Test: `tests/Dungeon/BuckleRulesTests.cs`

**Interfaces:**
- Produces:
  - `public static class BuckleRules`:
    - `public const int Variants = 4;`
    - `public const int Salt = 0xB0C1;`
    - `public static (Dictionary<GridPos, int> Buckled, List<GridPos> SparedRail) Pick(FloorLayout layout)`. Today's skips, chances and scan order are moved verbatim. When the roll succeeds, the variant is always drawn; it is stored only when `!layout.IsRail(p)`, else the cell is added to `SparedRail`.
  - `public IReadOnlyList<GridPos> DungeonBuilder.SparedRailBuckles` (empty before a build).
  - `--track-depth` prints `[track-depth] spared {n} rail buckle(s): party {p}, enemy {e}` before the verdict, and the verdict's `buckledLaneCells` counts `BuckledCells.Keys.Count(layout.IsRail)`.

- [ ] **Step 1: Write the failing tests.** `TodaysPick(FloorLayout)` in the test file is today's `PickBuckled` copied verbatim, returning its dictionary. The seeds are `ChasmTests.SplitSeeds()` (floor 12's knobs).

```csharp
[Theory, MemberData(nameof(ChasmTests.SplitSeeds), MemberType = typeof(ChasmTests))]
public void RailCellsAreNeverBuckled_AndEveryOtherPickIsTodays(int seed, FloorObjectiveKind obj)
{
    var l = ChasmTests.GenSplit(seed, obj);
    var today = TodaysPick(l);
    var (buckled, spared) = BuckleRules.Pick(l);
    Assert.DoesNotContain(buckled.Keys, l.IsRail);
    Assert.Equal(today.Where(kv => !l.IsRail(kv.Key)).OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X), buckled.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X));
    Assert.Equal(today.Keys.Where(l.IsRail).OrderBy(p => p.Y).ThenBy(p => p.X), spared.OrderBy(p => p.Y).ThenBy(p => p.X));
}

[Fact] public void Floor12KnobLines_DoHaveLaneCellsThatWouldHaveBuckled()
    => Assert.True(ChasmTests.SplitSeeds().Take(10).Sum(o => BuckleRules.Pick(ChasmTests.GenSplit((int)o[0], (FloorObjectiveKind)o[1])).SparedRail.Count) >= 10);
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~BuckleRulesTests"`. Expected: a compile error (`BuckleRules`).
- [ ] **Step 3: Implement** the Interfaces above.
- [ ] **Step 4: Run them.** Run the filter, then `--filter "FullyQualifiedName~RailLineTests"`; expect PASS. Then run the Task 1 command into `t2-depth.txt`. Expected:
  - no `cause=buckled*` lines;
  - `buckledLaneCells=0`;
  - the `spared` line's n equals Task 1's `buckledLaneCells`;
  - the ramp runs unchanged;
  - every `piece` and `ramp` S identical to `t1-baseline.txt` (this fix moves no S).
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/BuckleRules.cs src/Dungeon/DungeonBuilder.cs src/Core/DebugTools.TrackDepth.cs
git commit -m "fix(rails): a rail lane cell is never heaved; the track lies on a flat slab (both rng draws kept: every other tile and every hash unchanged)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: The track follows the drawn ramps (cases 2-4) and the ride-feel check

**Files:**
- Modify: `src/Dungeon/Generation/RailBed.cs` (`SurfaceAt`, `DrawnAt`, `Ease`, constants)
- Modify: `src/Dungeon/Generation/RailCurves.cs`:
  - `CentrelineOver`;
  - the 2D path and the resample factored into private helpers that `Centreline` shares unchanged.
- Modify: `src/Dungeon/DungeonBuilder.cs` (`PlaceRails.Lay` uses `CentrelineOver`)
- Modify: `docs/development.md` (Rails: a "The track bed" paragraph; the `--track-depth` flag lines next to `--print-map`)
- Test: `tests/Dungeon/RailBedTests.cs`, `tests/Dungeon/RailCurvesTests.cs`, `tests/Dungeon/Rails/RampRideTests.cs`

**Interfaces:**
- Consumes: `LaneRamp` and `RampsOn` (Task 1).
- Produces:
  - `RailBed` constants:
    - `RampSlabRise = 0.0118f` (0.1/cos(atan 0.5) − 0.1);
    - `EaseLength = 1f`;
    - `RampSlope = 0.5f`.
  - `public static float RailBed.Ease(float w, float y0, float s1, float s2, float length = EaseLength)` gives the height at signed distance w from where two lines meet (y0 at w = 0, slope s1 before and s2 after):
    - for |w| ≥ length/2, the lines themselves;
    - between, `y0 + s1·w + (s2 − s1)·(w + length/2)² / (2·length)`.
  - `public static float RailBed.DrawnAt(FloorLayout layout, Vector2 xz, float cell = 4f)`, the drawn top above the floor grid, `FloorTop` excluded:
    - a ramp cell gives `LowHeight + RampSlabRise + RampSlope·u`, where u is the distance along `Dir` from the cell's low edge (0..cell);
    - any other cell gives `LevelOf·4`.
  - `public static float RailBed.SurfaceAt(FloorLayout layout, Vector2 xz, float cell = 4f)` is `DrawnAt`, eased with `Ease` within `EaseLength/2` (along `Dir`) of the meeting point at every ramp end. A ramp end is the edge of a ramp cell whose neighbour along that ramp's `Dir` (the high end) or against it (the low end) is not a ramp cell. The point may lie in the ramp cell or in that neighbour. At a low end the lines are the flat neighbour's `LevelOf·4` and the ramp line. At a high end they are the ramp line and the flat level. The meeting point is where they cross (0.0236 m outside the low edge, 0.0236 m inside the high edge).
  - `public static IReadOnlyList<Vector3> RailCurves.CentrelineOver(IReadOnlyList<GridPos> lane, Func<Vector2, float> surfaceAt, float cell = 4f, float cornerRadius = 3f, float step = 0.5f)`:
    - the same 2D path as `Centreline` (straights and arcs);
    - straight pieces subdivided to ≤ 0.125 m before the heights are taken;
    - each point's Y is `surfaceAt(P)`;
    - the same 0.5 m resample.
  - `DungeonBuilder.PlaceRails.Lay`: `RailCurves.CentrelineOver(lane, xz => FloorTop + RailBed.SurfaceAt(layout, xz, CellSize), cell: CellSize)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// RailBedTests (Strip() from Task 1: the low edge at world x 8, the high edge at x 16, row centre z 6)
[Fact] public void Ease_IsTangentToBothLines()
{
    Assert.Equal(0f, RailBed.Ease(-0.5f, 0f, 0f, 0.5f), 4);
    Assert.Equal(0.25f, RailBed.Ease(0.5f, 0f, 0f, 0.5f), 4);
    Assert.Equal(0.0625f, RailBed.Ease(0f, 0f, 0f, 0.5f), 4);
    Assert.Equal(-0.0625f, RailBed.Ease(0f, 0f, 0.5f, 0f), 4); // a crest dips under its corner by Δslope·L/8
}

[Fact] public void SurfaceAt_IsTheDrawnFloor_EasedOverAMetreAtEachEnd()
{
    var l = Strip(); float At(float x) => RailBed.SurfaceAt(l, new Vector2(x, 6f));
    Assert.Equal(0f, At(6f), 4); Assert.Equal(0f, At(7.4f), 4);
    Assert.Equal(1.0118f, At(10f), 4); Assert.Equal(3.0118f, At(14f), 4);
    Assert.Equal(4f, At(16.6f), 4); Assert.Equal(4f, At(18f), 4);
    for (var x = 6f; x < 18f; x += 0.01f)
    {
        float a = At(x), b = At(x + 0.01f), c = At(x + 0.02f);
        Assert.InRange(b - a, -1e-5f, 0.5f * 0.01f + 1e-4f);                          // never falls, never steeper than the ramp
        Assert.InRange(MathF.Abs((c - b) - (b - a)), 0f, 0.5f * 0.0001f / 1f + 2e-5f);    // no kink: curvature ≤ slope / EaseLength
        Assert.InRange(MathF.Abs(a - RailBed.DrawnAt(l, new Vector2(x, 6f))), 0f, 0.069f); // the ease's 6.25 cm plus the slab rise's step at the edge
    }
}

[Theory, MemberData(nameof(ChasmTests.SplitSeeds), MemberType = typeof(ChasmTests))]
public void EveryLanePointNearARamp_LiesOnTheDrawnFloor(int seed, FloorObjectiveKind obj)
{
    var l = ChasmTests.GenSplit(seed, obj);
    if (l.Rail is not { } rail) return;
    foreach (var lane in new[] { rail.Party, rail.Enemy })
        foreach (var p in RailCurves.CentrelineOver(lane, xz => RailBed.SurfaceAt(l, xz)))
        {
            var xz = new Vector2(p.X, p.Z); var c = new GridPos((int)MathF.Floor(p.X / 4f), (int)MathF.Floor(p.Z / 4f));
            if (!new[] { c, c + new GridPos(1, 0), c - new GridPos(1, 0), c + new GridPos(0, 1), c - new GridPos(0, 1) }.Any(l.Ramps.ContainsKey)) continue;
            Assert.InRange(p.Y - RailBed.DrawnAt(l, xz), -0.075f, 0.075f);
        }
}
```

```csharp
// RailCurvesTests
[Theory] [MemberData(nameof(FlatLanes))] // yields new object[] { Lane(...).ToArray() } for the straight, corner and S-bend lanes of the existing tests
public void CentrelineOver_OnFlatGround_IsTodaysCentreline(GridPos[] lane)
{
    var a = RailCurves.Centreline(lane, _ => 0f); var b = RailCurves.CentrelineOver(lane, _ => 0f);
    Assert.Equal(a.Count, b.Count);
    for (var i = 0; i < a.Count; i++) Assert.True(Vector3.Distance(a[i], b[i]) < 1e-3f, $"point {i}: {a[i]} vs {b[i]}");
}

[Fact] public void CentrelineOver_AcrossARamp_IsLongerOnlyByTheSteeperSlope()
{
    var l = RailBedTests.Strip(); var lane = Enumerable.Range(0, 8).Select(x => new GridPos(x, 1)).ToList();
    float Len(IReadOnlyList<Vector3> p) => p.Zip(p.Skip(1), Vector3.Distance).Sum();
    var d = Len(RailCurves.CentrelineOver(lane, xz => RailBed.SurfaceAt(l, xz))) - Len(RailCurves.Centreline(lane, l.HeightOf));
    Assert.InRange(d, 0.18f, 0.25f); // 12.944 - 12.718 over L..C, less the eases' few mm
}
```

```csharp
// RampRideTests: Ride(points, descend, v0 = 6, targetOverride = -1) steps CartPhysics at 60 Hz along the points' arc length:
// Slope = (y(s+0.5) - y(s-0.5)) / span as TrackSampler.Slope; CartInput(slope, false, 0, 0, StopAt: length + 100, targetOverride);
// ComfortSampler.Sample(t, V, 0f, y(s) + 1.2f) each step; records MinV, MaxV, MaxAbsAccel (sampler.Accel), MinVertical
// (sampler.VerticalAccel) after the first 0.2 s, and MaxPitchPerMetre (atan2 over ±0.25 m chords, every 0.1 m).
[Fact] public void TheEasedRamp_RidesNoHarderThanToday()
{
    var l = RailBedTests.Strip(); var lane = Enumerable.Range(0, 8).Select(x => new GridPos(x, 1)).ToList();
    var today = RailCurves.Centreline(lane, l.HeightOf); var eased = RailCurves.CentrelineOver(lane, xz => RailBed.SurfaceAt(l, xz));
    var (upT, upE) = (Ride(today, false), Ride(eased, false));
    var (dnT, dnE) = (Ride(today, true), Ride(eased, true));
    Assert.True(upE.MinV >= 2.9f, $"the rope holds the climb: {upE.MinV}");
    Assert.True(Ride(eased, false, targetOverride: CartPhysics.TargetSpeed(0, 0) * 1.15f).MinV >= 2.9f); // a commanded cart (the runaway) too
    Assert.True(dnE.MaxV <= 8f, $"descent {dnE.MaxV}");
    Assert.True(dnE.MaxAbsAccel < ComfortRules.AccelTo, $"Recommended never closes fully: {dnE.MaxAbsAccel}");
    Assert.Equal(upT.MinVertical < ComfortRules.DropAccel, upE.MinVertical < ComfortRules.DropAccel);   // Strong fades at the crests as today
    Assert.Equal(dnT.MinVertical < ComfortRules.DropAccel, dnE.MinVertical < ComfortRules.DropAccel);
    Assert.True(dnE.MaxPitchPerMetre <= dnT.MaxPitchPerMetre + 0.02f, $"pitch {dnE.MaxPitchPerMetre} vs {dnT.MaxPitchPerMetre}");
}
```
Print both columns (`today` and `eased`) to the test output, and put them in the report next to Ruling 7's table. If a bound fails, stop and report the numbers. Never loosen a bound.

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~RailBedTests|FullyQualifiedName~RailCurvesTests|FullyQualifiedName~RampRideTests"`. Expected: a compile error (`SurfaceAt`, `DrawnAt`, `Ease`, `CentrelineOver`).
- [ ] **Step 3: Implement** the Interfaces above. Then update `docs/development.md`:
  - Rails gets "The track bed": the bed, the eases, the crest dip ≤ 7 cm, the S shift of ≈0.226 m per ramp per lane, and spared buckles.
  - The flag list gets `--track-depth[=CM]` and `--track-depth-float`.
- [ ] **Step 4: Run them.** Run the filter, then `--filter "FullyQualifiedName~RailLineTests|FullyQualifiedName~ChasmTests"`; expect PASS. Then run the Task 1 command into `t3-depth.txt`. Expected:
  - no `ramp-crest` line;
  - the worst `float` ≤ 7 cm;
  - each `ramp` line's span grown by 0.20-0.25 m;
  - every later S shifted by the predicted drift. Tabulate it in the report against `t2-depth.txt`.

  The verdict reads `buried=0`, apart from the case-5 lines Task 4 owns.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/RailBed.cs src/Dungeon/Generation/RailCurves.cs src/Dungeon/DungeonBuilder.cs docs/development.md
git commit -m "fix(rails): the track follows the drawn ramp (its slab's top), eased over 1 m at each end: no crest buries the rails, no sag floats them, no gravel z-fights; builder only, no layout hash moves

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: Case 5 (`r5_spur` into a wall), and the verdict `buried=0 inside=0`

Read `t3-depth.txt` first.
- **No `Spur_r5_spur/Spur` line:** case 5 is ruled out. Write that, with the spur's sample count, into the report and into `docs/development.md` next to the spur, and skip Steps 1-3. The verdict must already read `buried=0 inside=0`.
- **Spur lines present:** do Steps 1-5.
- **Any other track still has lines:** stop and report.

**Files:**
- Modify: `src/Dungeon/Rails/Core/SetPieceRules.cs` (`SpurBend` and its constants)
- Modify: `src/Dungeon/Rails/SetPieces/SpurSwitch.cs` (`Build` takes `Func<Vector3, bool> rockAt` and bends by `SpurBend`)
- Modify: `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (the one `spur.Build` call passes `rockAt`)
- Test: `tests/Dungeon/Rails/SetPieceRulesTests.cs`

**Interfaces:**
- Produces:
  - `SetPieceRules.SpurBendMax = 1.8f`, `SpurStopHalf = 0.8f`, `SpurWallMargin = 0.1f`.
  - `public static float SetPieceRules.SpurBend(float roomBeyond) => MathF.Min(SpurBendMax, roomBeyond - SpurStopHalf - SpurWallMargin)`, where `roomBeyond` is the open distance outward from the enemy lane's centre.
  - `SpurSwitch.Build(TrackSampler party, TrackSampler enemy, LaneMap lanes, Material steel, Material sleeper, Func<Vector3, bool> rockAt)`:
    - It probes `enemy.Point(EnemyPointsS + SpurLength) + outward·(cell/2 + 0.5)`.
    - Rock gives a bend of `SpurBend(cell/2)` (1.1 m); open ground gives `SpurBend(cell·1.5)` (1.8 m). It replaces `SpurOut`, which is removed.
    - The `[rails] spur switch:` line gains ` bend {b:0.0} m{(rock ? " (rock beyond)" : "")}`.
  - The caller's `rockAt` is `w => _builder.Layout[DungeonBuilder.WorldToCell(w)] == Cell.Wall`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void TheSpurBend_KeepsItsStopInsideTheLaneCellAgainstRock()
{
    Assert.Equal(1.1f, SetPieceRules.SpurBend(2f), 4);
    Assert.Equal(1.8f, SetPieceRules.SpurBend(6f), 4);
    Assert.True(SetPieceRules.SpurBend(2f) + SetPieceRules.SpurStopHalf <= 2f - SetPieceRules.SpurWallMargin + 1e-4f);
    Assert.True(SetPieceRules.SpurBend(2f) + 0.7f < 2f); // the sleepers too
}
```
- [ ] **Step 2: Run it and see it fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~SetPieceRulesTests"`. Expected: a compile error (`SpurBend`).
- [ ] **Step 3: Implement** the Interfaces above, and note the bend in `docs/development.md`'s spur paragraph.
- [ ] **Step 4: Run it.** Run the filter; expect PASS. Then:
  - Run the Task 1 command into `t4-depth.txt`. Expected: the verdict line `total buried=0 inside=0 …`, with `buckledLaneCells=0`.
  - Run `timeout 800 godot --headless --path . -- --test-chase --quit-after=750 2>&1 | grep -E "\[chase\] RESULT|spur switch"`. Expected: `bend 1.1 m (rock beyond)` and `[chase] RESULT ok` (the golem still meets the stop).
- [ ] **Step 5: Commit** (only if Steps 1-3 ran)

```bash
git add src/Dungeon/Rails/Core/SetPieceRules.cs src/Dungeon/Rails/SetPieces/SpurSwitch.cs src/Dungeon/Rails/RailEncounterController.Kiln.cs docs/development.md
git commit -m "fix(rails): the kiln spur bends no further than its lane cell where rock lies beyond: its rails and stop never run into the wall

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: The gate: floor 12's placements, every flow, and the view from the seat

**Files:**
- Create: `src/Core/DebugTools.TrackView.cs`
- Modify: `src/Core/DebugTools.cs` (the `--test-track-view` parse, dispatch and help line)
- Modify: `docs/development.md`:
  - the re-measured floor-12 S values wherever they are quoted (the five-rides table, the switch-back, split, dark and runaway paragraphs, the `break=`/`split=` lines);
  - the flow's line.
- Test: the headless and windowed runs below. This task adds no xUnit test; the flow is the test.

**Interfaces:**
- Consumes:
  - `RailBed.RampsOn`, `SagEdge`, `CrestEdge` (Task 1);
  - `DungeonBuilder.SparedRailBuckles` (Task 2);
  - `RideBoard`, `RideWaiveCages`, `rc.RestoreGates`, `rc.Restore`, `enc.DebugHoldRunaway(4)`;
  - the `QueueShot`/`Ahead`/`Bare`/`HudBack` pattern of `DebugTools.Runaway.cs`.
- Produces: `--test-track-view`, with tag `[trackview]` and the settings
  `_testTrackView = true; _builtin = true; _startFloor = 12; _floorGiven = true; ForcedLethality ??= 3f; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 240;`.
  1. **Targets.**
     - The first party ramp on the line (`RampsOn(layout, rail.Party)[0]`), its ride k, and its crest and sag S.
     - A spared party-lane cell on ride k, else the first spared party-lane cell on any ride k2, which needs a second pass.
     - It fails `no rail ramp on floor 12` or `no spared lane cell` when one is missing.
  2. **Pass setup.** Per pass: kill the enemies, `RestoreGates`, `DebugHoldRunaway(4)` (logging `the runaway on ride 4 held by the test`), `rc.Restore(k − 1)`, board, depart, and pump with the lead.
  3. **Rays.**
     - When the front reaches the target S − 6 m in travel order, aim the seated eye (`me.AimAt`) at the target's rail mid point.
     - Cast a layer-1 ray from `me.Camera.GlobalPosition` to each rail top (`± RailHalf`, `+ RailTop` on the track frame), stopping 0.05 m short.
     - Targets: the crest edge, crest ± 1 m, the sag edge, and the spared cell's centre.
     - Each must be `clear`. Print `[trackview] sight {name} S={s:0.0} railL {…} railR {…}`.
     - The control is a ray to 0.10 m under the drawn floor at the crest edge (`DrawnAt`). It must be `blocked`, else fail `the sight check is vacuous`.
  4. **Shots.** Windowed with `--screenshot=PATH`, it saves the HUD-hidden views `PATH_crest.png`, `PATH_sag.png` and `PATH_heave.png` at those same moments.
  5. **Result.** `[trackview] RESULT ok: crest S={…} sag S={…} ({climb|descent}, ride {k}), heave cell {cell} S={…} (ride {k2}), {n} rays clear, control blocked[, shots saved]`. Otherwise `[trackview] RESULT FAIL: …`.

- [ ] **Step 1: Write the flow.** Then run it headless: `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-track-view 2>&1 | grep -E "^\[trackview\]"`. Expected: every `sight` line clear, `control blocked`, and `[trackview] RESULT ok`.
  - Prove the check bites: with `Lay` temporarily on `Centreline` (today's bed), the crest's sight must fail. Revert that line before going on, and say so in the report.
- [ ] **Step 2: The windowed shots.** The controller confirms the display is free. Run `timeout 300 godot --path . -- --test-track-view --screenshot=.superpowers/sdd/2026-11-22-exposed-track-fix/shots/track 2>&1 | grep -E "^\[trackview\]"`. Expected: `RESULT ok … shots saved`.
  - Open the three PNGs with the Read tool and describe them in the report: rails and sleepers visible over the crest and the sag, and the spared cell flat under them.
  - The controller views them too.
- [ ] **Step 3: Floor 12's placements.** Run the Task 1 command into `t5-depth.txt`. Tabulate, before (`t1-baseline.txt`) and after, each rule below, with its verdict:
  - `r3_switch_back`'s lead (the gap S − its points S) within 16-24 m, still `Live` (12-40 m);
  - `r4_split`: its At within `SplitRules.AtSlack` 8 m of the layout's points, the split `live`, the straight before the points ≥ 5.0 m, and the window start and hold as logged;
  - `r4_runaway`: ≈50 m before the points, starting ≥ 2 m past stop 3, with the window opening ≥ 10 m past the trigger;
  - `r4_dark`: starting ≈70 m before the points and running 85 m (so ≈15 m past them);
  - `r4_hounds`: inside the dark and ≥ 15 m short of the window;
  - the `cavern` span inside ride 4;
  - `r5_trestle` and `r5_jump`: `over a crevasse: True`;
  - `r5_spur`'s lever past the jump's far end;
  - `r5_buffer` short of stop 5;
  - each barricade's S.

  Every drift must match the prediction (header) within 0.05 m. A rule that fails, or a drift that does not match, stops the task: report it, and change no At.
- [ ] **Step 4: Every flow.** Run each with its own grep, sequentially unless `scripts/flow.sh` exists. Expected: each RESULT ok.
  - `--test-ride`: `[ride] RESULT`
  - `--test-ride-edges`: `[ride-edges] RESULT`
  - `--test-crew --quit-after=570`: `[crew] RESULT`
  - `--test-chase --quit-after=750`: `[chase] RESULT`
  - `--test-barricade`: `[barricade] RESULT` (the sight-checked At values)
  - `--test-crevasse`, `=trestle` and `=survivor`: `[crevasse] RESULT`
  - `--test-switch-back`, `=wrong`, `=stall`, `=lone` and `=fixedafter`: `[switchback] RESULT`
  - `--test-split`, `=early`, `=hold`, `=stall` and `=lone`: `[split] RESULT`
  - `--test-runaway=tail` and `=ram`: `[runaway] RESULT`
  - `--test-cart-watchdog`: `[watchdog] RESULT`
  - `--test-relief`, `=rails`, `=refuse` and `=ahead`: `[relief] RESULT`
  - `--test-track-view`: `[trackview] RESULT`
  - `--rehash-plans=data/adventures/builtin --quit-after=2`: `[rehash] RESULT ok: 0 of …`, and then `git diff --stat data/` must be empty.
  - Then run the full suite: `dotnet test tests/DungeonCrawler.Tests.csproj`, expecting all green.
  - Last, tell the controller the branch is ready for `RIDE=1 scripts/net_test.sh` (run alone): `NET TEST: ok`.
- [ ] **Step 5: Update the docs and commit.** Write the re-measured values into `docs/development.md` (the old ones are quoted in the Files list), with one line on the S shift.

```bash
git add src/Core/DebugTools.TrackView.cs src/Core/DebugTools.cs docs/development.md
git commit -m "test(rails): --test-track-view rides floor 12's first ramp and a spared lane cell, sighting the rails from the seat (with a control) and, windowed, shooting them; floor 12's re-measured S values in the manual

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
