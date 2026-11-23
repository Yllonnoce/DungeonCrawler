# Mine Cart Chase, Plan 15: the exit island, the pit switch, the parked train (floor 12, plan B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12's exit room becomes an island that only the rails reach. Lava trenches and cave-ins seal its halls, and one rail bridge with no walkway crosses the lava. Ride 5's lava pit gets a deadly switch round it. A parked train before the bridge is smashed by a party that rides, and it carries in anyone left outside.

**Architecture:**
- **The grid says "no walk".** Two `[AiIgnore]` knobs, `PitRide` and `ExitIsland`, add two passes to `CarveChasms`. Neither uses an rng, and both run after the gorge and before the planks are cut.
  - `CarvePit` cuts the pit: one party-lane cell and the two cells inward of it.
  - `CarveIsland` seals the exit room and the rooms that hang off it. Each hall gets a lava trench at its mouth and a cave-in behind it. The exit bridge's lane cells become chasm with their track kept, and a two-cell moat runs beside them.
  - `RailLine.Pit` and `RailLine.Island` record both. `FloorSolvability` proves an island floor by rail instead of on foot.
- **Every role builds the runtime from the layout**, as the broken bridge does:
  - the lava (`ChasmDressing`) and the cave-ins' boulders;
  - the exit bridge's open trestle, and its hold (no Get out, no move);
  - the pit's bare track, its lamps and its bent rail stubs.
- **The pit switch is ride 3's live back, moved to the out lever.**
  - A `switch_out` with the pit 7–40 m past its points is a deadly, human-only lever in plan 8's seated window.
  - It is called twice and is R-lone safe, and a late press is refused.
  - A fixed back brings the train home past the pit. A train that meets the lip on the party lane falls, and the watchdog steps a stuck train down as plan 11's does.
- **The parked train is a set piece built from the bridge**: a cart and four wagons, standing 1 m short of the bridge.
  - While it is in the way, the party's train smashes it at a barricade's cost.
  - Once the train is past, or the party has no cart, it stands again as a lift. The lift runs like a chaser into the island for anyone left outside. With no party cart left, it takes over as the party's train.
- **Home is the island.**
  - On an island floor, a crew member anywhere in the island is home.
  - Once every up player is inside, unopened cages count as left behind.
  - Crew on foot outside, with nobody outside to fetch them, are stranded and lost, because a follower's catch-up never crosses the island's edge.
  - Grisel is brought in, and the win puts a reward chest in room 9.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-22-floor-12-long-train-and-exit-island-design.md` (4e913a9).
- This plan covers part B (B.1–B.10), "Build order", "Left to the builder" and "The rulings".
- Part A is plan 14 (`docs/superpowers/plans/2026-11-22-mine-cart-14-long-train.md`), which this plan builds on: the train, `TrainRules`, the wagon numbers, `AboardCrew`, `RailController.MovesRefused`, `ThrowClock`, `BarkTrigger.BoarderThrow`, `--test-boarders` and the hound groups.
- The spec is an addendum to `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md`, whose anti-goals still bind.

Earlier rulings: `.superpowers/sdd/mine-cart-rulings.md` (the "Long train + exit island" block) and plan 14's ledger, `.superpowers/sdd/2026-11-22-mine-cart-14-long-train/progress.md`. Facts with file:line are in `.superpowers/sdd/shared/exit-island-recon.md` (A–G), and this plan's review is `.superpowers/sdd/shared/plan15-review.md`.

In `docs/development.md`, read:
- "The five rides" (the table, ride 5);
- "Stops, cages and the crew";
- the Crevasses section (`ChasmDressing`, the gorge, the probe);
- the watchdog section;
- "Riding together" (the chaser, Props, the RIDE net test, Known gaps);
- "After any generator change, sweep the shipped floors" (`development.md:879`).

Line numbers drift, so find code by name.

**Plan 14 must be complete before plan 15 starts.**
- Tasks 1–6 are committed: Task 6 is c9ae5b8, with r1, r3 and r5_hounds, the hounds-only linger, the crossing frees, the golem frees, the boarder range cap and `--test-boarders=groups`. Its fix round committed `r5_hounds` at At 0.2749 (S 1080.0), Offset −8 (28f6ebb).
- Task 7 (the RIDE pass for the long train) and Task 8 (docs, the re-pinned 5.22 m/s timings, the sweep) are still to come.
- Where a task below uses a name that plan 14's later work adds, its Interfaces block says so.

**Measured** (the shipped floor at 303ef27, from the recon's `--print-map`/`--track-depth`; the review re-read the grid cell by cell at c9ae5b8). Task 1 Step 1 confirms each value at plan 14's end.
- **Layout.** `LayoutHash` 16995108853750381062.
  - `[map] rail: stops=[1,2,4,6,8,9] … cavern=238..259 chasm=217 strips=8 gorge=[291..292@0.42-0.44,298..300@0.55-0.58,302..306@0.62-0.69,311..313@0.78-0.82] planks=3 break=188@0.81 run=185..191 split=227@0.38 crevasse=228..231`.
  - `[chasm] regions=12 decks=50 planks=3 cavern=44`.
- **The line.** `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`, ride 5 is 209.2 m and the party track 1243.7 m.
  - Stop 4's platform is party cells 265..271 and stop 5's is 319..326.
  - Ride 5's straight run down x 61 is cells 275..285, and its straight run along y 60 is 311..315.
- **Ride 5's pieces:**
  - `r5_hammers` 1061.3/1064.3/1067.3 and `r5_pour` 1106.2 (valve 1102.7);
  - `r5_golem` 1125.0 (its burst at 1125.1, ledger);
  - `r5_hounds`: At 0.2749 (S 1080.0), Offset −8 (plan 14, 28f6ebb);
  - `r5_trestle` 1134.7–1146.7 and `r5_jump` 1156.0–1161.0;
  - `r5_spur` points 1200.3 (lever 1198.3), `r5_barricade` 1206.6 and `r5_buffer` 1221.3.
- **The probe.** `nofloorDrop` is PartyTrack 0 and EnemyTrack 40, and the five other fields read 0.
- **The rooms.**
  - Room 1 (30,53) 8x6 is the start.
  - Room 9 (42,50) 8x8 holds the down stairs (46,54) and stop 5.
  - Room 12 (54,50) 4x5 is the safe room, its door (53,53).
  - The west hall is (38..41,54). The north hall is (46,45..49), with the trap (46,49) and the plank (46,47); its fork is (46,45), with three walkable neighbours.
- **The train** is 12.6 m with its cars 2.6 m apart, and cruises at 5.22 m/s unpumped with four wagons.
- **What the spec expects after the cut** (approximate; Tasks 1, 3 and 4 measure each):
  - The pit is cell 282, (61,56), with the lip at about S 1073 and the far edge at about 1077.
  - The exit bridge is party cells 311..314 (about S 1184–1199) and enemy cells (51..53,61).
  - The trenches are (41,54) and (46,49). The cave-ins are (38..40,54) and (46,46..48).
  - The moat adds (51,58), (51,59) and (51..53,62..63).
  - The parked train's rear is at about 1170.3. `nofloorDrop` is 200 / 160.

**What moves in plan B.** Every layout-dependent pin is re-measured in its task's measurement step and re-pinned from the measurement, never loosened.

| Pin or flow | Where | Moves to |
|---|---|---|
| `floor_12.json` `LayoutHash` | Task 3 | the `--rehash-plans` value (only floor 12's may move) |
| `TrackDepthRules.Floor12NoFloorDrop` | Task 3 | the measured pair (expected PartyTrack 200, EnemyTrack 160); Task 4 checks that it holds |
| `[chasm]` and `[map] rail` counts (`chasm=`, `planks=` 3→2, `regions=`) | Tasks 1, 3 | measured; `pit=282@0.25 island=room 9+12 sealed=2 bridge=311..314` appended |
| ride 5's At values (`r5_hammers`, new `r5_switch_out`, `r5_pit`, `r5_switch_back`, `r5_parked`) | Tasks 4, 5 | computed to 4 decimals from the measured S |
| `r5_hounds` | Task 4 | from plan 14's At 0.2749 (S 1080.0) to the first S in 1096.0..1105.0 clear of the crossing, At recomputed; STOP if gate (b) fails there |
| `TrackStationsTests`' (1156.0, 1161.0) | Task 4 | the pit's measured lip and far edge |
| `RailEncounterPlanTests` (ride 5's list, its order, the barricades, 21 → 23 pieces) | Tasks 4, 5 | B.3's order |
| `--test-cavern`'s bare-track check | Tasks 3, 4 | laid over every exit bridge cell (3), bare over the pit (4) |
| `--test-cart-watchdog` step 3, `--test-chase`'s two jumps, `--test-crevasse` B/C/`=survivor`/`=trestle` | Task 4 | the pit switch (B.10) |
| `--test-island=bridge`, `=wreck` | Task 4 | the pit thrown on the way |
| `--test-barricade` (r5), `--test-chase`'s ride-5 smash | Task 5 | the parked train |
| `--test-boarders=groups`, `--test-crew` and every flow that rides to stop 5 | Task 4 | the pit thrown on the way |
| the RIDE pass | Tasks 4, 6, 8 | Task 4 repairs it (the remote pit throw, the end-of-line wreck, the "hopped" wording); Task 6 rewrites step 8 (the chaser behind the parked place, the lift); Task 8 adds the no-Get-out and the smash |
| `RampRideTests`' ride-5 row (B.10: "four wagons from ride 1") | — | done by plan 14 Task 1; Task 9's sweep confirms it |
| `LeapRulesTests.ShippedFloor12()` | Task 3 | holds by itself: its spec reads the campaign's knobs and its hash the plan's (cavern 238..259 unchanged) |
| `development.md:1539`, `2094-2100`, `2236-2240`, the counts | Task 9 | rewritten |
| `StopS`, the rides, cavern, break, split, every other piece's S | — | must not move (gated in Tasks 3–5) |

**Planner's rulings (an index).** Each task carries the full text of every ruling it relies on, in its "Rulings this task needs" block. The text below names each ruling and the tasks that carry it.
1. Build order and the interim floor (Tasks 3, 4)
2. `BridgeIslandCells` is 4, not the spec's 3 (Task 1)
3. Island rooms, mouths, the hall walk, the fork (Task 1)
4. `RailIsland.Cells` and the refusals (Tasks 1, 2)
5. A sealed trap is removed and logged (Task 1)
6. The moat and the lava regions (Tasks 1, 3)
7. The pit's refused rides and its window (Task 1)
8. The plank pass: picked before the cuts, cut after them (Task 1)
9. The exit bridge keeps its track and loses its gravel (Task 3)
10. The exit bridge's hold, and the refusal's words (Task 3)
11. A wreck at or past the bridge's start (Tasks 3, 5, 6, 7)
12. A set-down never lands on a drop (Tasks 3, 6, 7)
13. The pit switch reuses ride 3's machinery (Task 4)
14. The placement numbers (Tasks 4, 5)
15. `r5_hounds` moves from plan 14's S 1080.0 to the first S in [1095.5, 1105.0] (1 m steps up from 1096) clear of the crossing, then must land 1.5 s before the burst in the measured flows, else STOP (Task 4)
16. The plan's indices stay (Tasks 4, 5)
17. The pit is read and cut from the layout (Task 4)
18. The watchdog's pit steps (Task 4)
19. The jump's lane check (Task 4)
20. The traffic runaway stops short of the pit (Task 4)
21. Flows throw the pit on the way; the fall tests do not (Task 4)
22. The RIDE pass's red window is Tasks 3–4 (Tasks 3, 4, 6, 8)
23. The parked train's bodies, and guards on the party's `TrainWagon` (Tasks 5, 6)
24. The smash (Task 5)
25. The parked train's states (Tasks 5, 6)
26. The lift is a chase (Task 6)
27. The takeover and its standing state (Tasks 6, 7)
28. Boarding the lift (Task 6)
29. A chaser's and a relief's limit on ride 5 (Task 6)
30. The lift on the wire (Tasks 5, 6)
31. A lift rider's saved pose (Task 6)
32. Home is the island (Task 7)
33. Unopened cages left behind (Task 7)
34. Stranded, judged live (Task 7)
35. A follower's catch-up never crosses the island's edge (Tasks 6, 7)
36. Grisel is brought in (Task 7)
37. The reward chest (Task 7)
38. `col_stairbell` guards room 9 (Task 3)
39. The new barks (Tasks 4, 5)
40. The cave-ins' look (Task 3)
41. The new flows (Tasks 3–7)
42. The RIDE pass's end-of-line wreck and `DebugRestoreAt` (Task 4)
43. `r5_spur`'s lever (Task 3)
44. The safe room's gathering (Tasks 3, 7)
45. The proof's cages and rooms (Task 2)
46. A relief at the lift is pinned engine-free (Task 6)

## Global Constraints

- **Rulings travel with their tasks.** Each task carries the full text of every planner's ruling it relies on, in its "Rulings this task needs" block, and every command it runs. The header's ruling list is only an index.
- Protocol.Version stays 17. New state goes in existing messages (`SeatMsg`, `CartStateDto`, `PropEvt`, `LogMsg`, snapshots).
- LayoutHash moves in plan B, in Task 3 only. Until Task 3 commits, floor 12's `LayoutHash` is `16995108853750381062`; Task 3 sets it with `--rehash-plans`, and every later task holds that value.
  - Only floor 12's hash may move. Every new knob defaults off, and `RailLineTests.DefaultKnobs_BuildTodaysFloors` stays green.
  - Every pin that depends on the layout is re-measured in a measurement step and re-pinned from the measurement, never loosened.
- No voice spend. New lines are subtitles with VoiceId null, and the speakers' sexes match .superpowers memory (Brand, Wren and Ilex are men; Sable is a woman).
- CC0 assets only. The repo is public. New looks are procedural boxes on textures already in `assets/` (`weathered_planks`, the rail gap's molten material, `Rubble`) or the loafbrr MinesPack.
- tests/ (xUnit) is not in git.
- Run `dotnet build DungeonCrawler.csproj` before headless godot. Engine flags go before `--`, game flags after it.
- Every check greps its own flow's RESULT line. Run non-network flows side by side through `scripts/flows.sh`, longest first. While .superpowers/sdd/shared/RIDE.lock exists, nothing builds or runs.
- `--test-crew --quit-after=570` and `--test-chase --quit-after=750`.
- The probe's pass is `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`, with nofloorDrop pinned at PartyTrack 0 / EnemyTrack 40 (`--test-track-view`) until Task 3 re-pins it from its measurement (the spec expects 200 / 160).
- Only the controller runs the RIDE pass, alone, under the lock. A failure is investigated from its kept logs, never rerun to green. Plain `scripts/net_test.sh` stays green in every task.
- Commits end with "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>". Stage by explicit path. No push, merge, installers or dist builds.
- Implementers run on opus, reviewers on fable.
- `RailController.MovesRefused` gains the exit bridge (plan 14 Ruling 11). Plan B's barks append after `BarkTrigger.BoarderThrow`.
- Also binding:
  - Nothing new draws from a gameplay rng stream; a look-only per-cell seeded rng (as `DungeonBuilder.SealMouth`'s) is allowed.
  - A flow passes only on exit 0, its own `[...] RESULT ok` line (never the early `[builtin] RESULT ok`) and no `ERROR` line.
  - Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N`.
  - Hand-edited JSON keeps the game's escaping (`'`, `&`, `<`, `>` and `+` as `'`, `&`, `<`, `>` and `+`; non-ASCII as `\uXXXX`, upper-case hex) and a two-space indent, and `git diff` shows only the values meant.
  - The full suite is `dotnet test tests/DungeonCrawler.Tests.csproj`. Never `git add tests/`.
  - Floor-12 dev saves from before this plan are not supported (spec, Constraints): a flow that loads one makes a fresh save.
- **The measurement command**, named in several tasks:

  ```bash
  dotnet build DungeonCrawler.csproj && XDG_DATA_HOME=$(mktemp -d) timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | tee /tmp/p15-map.txt | grep -E "\[map\] rail|\[track-depth\] (piece|encounter|PartyTrack |EnemyTrack |total)|\[chasm\]|\[gen\]|\[rails\] (exit bridge|pit|parked)|hash="
  ```

  Run it with the lock absent.

## Review Focus

- **The last spare goes before ride 5, and the party takes the lift over with stop 4's holders alive and its cage locked.** The countdown starts as soon as a player sits. Neither stop 4's gate nor its cage holds a cart that stands 160 m past it, and the unopened cages resolve at the end.
  - Pinned by `TrainRulesTests.StandingPastItsStop_NoGateOrCageHoldsTheCart` (Task 6).
  - Pinned by `--test-island=takeover` (Task 6) and `=onfoot` (Task 7).
- **A freed crew member or Grisel is following on foot outside while the lead rides in.** The catch-up teleport never carries them across the island's edge. "Stranded" and "brought in" are decided by their rules, never by a race against the teleport.
  - Pinned by `CrewRulesTests.TheCatchUp_NeverCrossesTheIslandEdge` (Task 7).
  - Pinned by `--test-island=stranded`'s 12 s hold and `=grisel` (Task 7).
- **The cart is wrecked with its front just past the bridge's start, the wagons still outside and crew aboard.** Every hero and crew member stands on an island cell, none on a drop and none fallen. No spare takes over, no wagon is left standing, and the crew are home.
  - Pinned by `ExitBridgeRulesTests.AWreckJustPastTheStart_IsInside_AndSetsEveryoneDownPastTheFarEdge` (Task 3).
  - Pinned by `--test-island=wreck` (Tasks 3, 5, 6, 7).
- **A save is taken while someone rides the lift over the exit bridge, or while the party's train is braked on it, and is then loaded.** Nobody stands on a bridge or moat cell after the load. The lift's rider stands beside the parked train, and the party's riders stand beside the cart at its stop.
  - Pinned by `ParkedRulesTests.ALiftRidersSavedPose_IsNeverOverTheBridge` (Task 6).
  - Pinned by `--test-island=lift`'s mid-bridge save (Task 6) and `=bridge`'s braked save (Task 3).
- **The lift is boarded while a chaser or relief is still on the line, or two riders press "Board the cart" on the same tick while the party has no live cart.** Nothing boards until the chaser is gone, a remote's intent included. Exactly one takeover happens, and nobody is left seated in the hidden lift.
  - Pinned by `ParkedRulesTests.TheLift_OffersBoarding_OnlyWithNoChaseOnTheLine` (Task 6).
  - Pinned by `--test-island=takeover`'s double press and `DebugBoardLift` refused while a chaser stands (Task 6).

---

### Task 1: The baseline, and the pit and the island in the generator (floor 12 still off)

**Files:**
- Create: `src/Dungeon/Generation/FloorGenerator.Island.cs` (`CarvePit`, `CarveIsland` and their helpers)
- Modify:
  - Generator: `src/Dungeon/Generation/FloorGenerator.Chasms.cs` (`PickWalkways`/`CutWalkways` split from `CarveWalkways`; the two passes between them).
  - Layout records:
    - `src/Dungeon/Generation/RailLine.cs` (the `RailRules` constants, `RailPit`, `RailIsland` and the four `RailLine` fields);
    - `src/Dungeon/Generation/FloorLayout.cs` (`GenLog`, `InIsland`).
  - Knobs: `src/Dungeon/Generation/FloorSpec.cs`, `src/Adventure/Core/Plans/CampaignPlan.cs` (`SpecOverrides`).
  - Output:
    - `src/Dungeon/Generation/LayoutReport.cs` (` pit=` and ` island=`);
    - `src/Dungeon/DungeonBuilder.cs` (prints `GenLog` as `[gen] …`).
- Test: `tests/Dungeon/ShippedFloors.cs` (new), `tests/Dungeon/IslandTests.cs` (new)

**Rulings this task needs:**
- **2.** `RailRules.BridgeIslandCells` is **4**, not the spec's 3.
  - The bridge is the last straight run clipped to end that many cells before the exit platform. On floor 12, 3 gives 311..315.
  - The spec's own cells (311..314, S ≈1184–1199), its enemy cells (51..53,61), its moat (51..53,62..63), "the bridge keeps 4 cells to the platform" and the spur's points "on the island" (1200.3) all need 4.
  - Cost: one fewer bridge cell on a line whose last straight runs into the corner.
- **3. The island's rooms and seals.**
  - The island rooms are the exit room `R`, plus each other room (not the start's) that has no cell reachable from the up stairs with the rails blocked and `R`'s cells walled, but has one with `R` open.
  - A mouth is a walkable hall cell (outside every room, not a line or pocket cell) beside an island room's cell.
  - The walk goes from the mouth along unvisited walkable hall neighbours. It stops before a cell with 3 or more walkable 4-neighbours (the fork, kept open) or before a room cell.
  - A walk that ends at an island room seals nothing (room 9 to room 12). Any other walk is sealed: the mouth becomes a trench (`Cell.Chasm` at the room's level) and the rest `Cell.Wall`.
  - Cost: a hall that branches inside the sealed stretch leaves its branch open, and then the carve refuses (Ruling 4).
- **4.** `RailIsland.Cells` is `Reachable(DownStairs)` after the cut (rails open, secrets shut).
  - The carve records every cell it changes and restores them all, reporting `refused (…)`, when:
    - the up stairs reach an island cell: `refused (leaks at (x,y))`;
    - a key (`KeySpawns`, a vault's key, the objective's spare keys), a vault lever, a prisoner cell or a jail-room cell lies in the island: `refused (a key at (x,y) inside)` and the same for each kind (the objective's other reserved cells do not refuse);
    - the exit room is the start's;
    - a taken cell that is not a trap lies on a sealed hall.
  - No bridge run gives `none`.
  - Cost: none (a refused floor is the floor without the island, byte for byte).
- **5.** A trap on a sealed cell is removed from `FloorLayout.TrapSpawns` and logged through a new `FloorLayout.GenLog` (not hashed), which `DungeonBuilder` prints as `[gen] {line}` after its floor line. Cost: later trap indices shift (floor 12's plan names traps by room, never by index).
- **6. The moat and the lava.**
  - The moat takes each party bridge cell's inward cells at t = −1, −2 and the outward cells at t = +2, +3 of each enemy bridge twin (the strips' convention), plus the trenches. Only rock or pocket cells are cut, never a lane, platform, ramp, ramp neighbour or taken cell.
  - An enemy twin is a bridge cell only when it is a straight enemy cell (its two enemy-lane neighbours face each other), so the corner (54,61) stays solid.
  - The lava is the whole `ChasmDressing` region holding any pit or island cut cell.
  - Cost: the gorge's last span (311..313, inward) and the strip (52..53,64..68), which the moat's row y 63 touches, turn molten. They lie beside the hammers' new place and the parked train.
- **7.** `CarvePit` refuses (`refused`) a ride the line does not have, the break's ride, the split's ride and the cavern's ride, and is `none` with no fitting run. The run must lie whole in `RideWindow(rail, k, PitFrom, PitTo)`. Cost: none.
- **8. The plank pass is split** so the island cannot change today's picks.
  - `PickWalkways(layout)` runs today's farthest-point choice exactly, before `CarvePit`. Each earlier pick's plank and cut cells are read as cut through an overlay passed to `WalkwayCell`, never written, which is all that carving did to the later picks.
  - `CutWalkways(layout, picks)` runs after `CarveIsland`. It carves each pick that is not on a sealed hall and still passes `WalkwayCell`; a pick dropped for the second reason adds `island: plank at {c} dropped` to `GenLog`.
  - With both knobs off the pair gives today's floor byte for byte. On floor 12 the two other planks are today's, in today's order.
  - The STOP in Step 5 stays as a backstop.
  - Cost: a refactor of `CarveWalkways`.

**Interfaces:**
- Consumes: `FloorGenerator.RideWindow`, `SlagTwin`, `StripCells`' rules, `TakenCells`, `RailSide`, `Times`, `Rock`, `WalkwayCell`; `FloorLayout.Reachable`.
- Produces:
  - **The knobs.** `FloorSpec.PitRide` (int, default 0) and `FloorSpec.ExitIsland` (bool, default false).
    - `SpecOverrides.PitRide` (`int?`, `[AiIgnore]`, clamped 0..6) and `SpecOverrides.ExitIsland` (`bool?`, `[AiIgnore]`), both in `IsEmpty` and `Apply`.
    - Their `AiDescription`s: "The ride (1-based) whose party line has a lava pit with a deadly switch round it, 0 for none; needs Crevasses." and "True: the exit room and the rooms off it become an island reached only by a rail bridge over lava; needs Crevasses."
  - **`RailRules`.** `PitRunCells = 11`, `PitLeadCells = 7`, `PitFrom = 0.05f`, `PitTo = 0.60f`, `BridgeMinCells = 4`, `BridgeIslandCells = 4`, `MoatDepth = 2`, `ParkedCells = 4`.
  - **The records:**
    - `public sealed record RailPit(int Ride, int Cell, IReadOnlyList<GridPos> Cut)`: `Cell` is the party-lane index, and `Cut` the pit cell, then its inward cells that were cut;
    - `public sealed record RailIsland(IReadOnlyList<int> Rooms, RailSpan Bridge, RailSpan Parked, IReadOnlyList<GridPos> EnemyBridge, IReadOnlyList<GridPos> Trenches, IReadOnlyList<GridPos> CaveIns, IReadOnlyList<GridPos> Moat, IReadOnlySet<GridPos> Cells)`, every list in (Y, X) order, with `Moat` the cells it cut and `Parked` = (`Bridge.From − ParkedCells`, `Bridge.From − 1`);
    - `IEnumerable<GridPos> RailIsland.Cut(RailLine rail)` yields the party bridge cells, `EnemyBridge`, `Moat` and `Trenches`.
  - **`RailLine` fields** (none hashed): `RailPit? Pit`, `string? PitWhy` ("ok", "none" or "refused"; null when not asked), `RailIsland? Island`, `string? IslandWhy` ("ok", "none" or "refused (…)"; null when not asked).
  - **`FloorLayout`.** `List<string> GenLog` (not hashed) and `bool InIsland(GridPos p)`.
  - **The passes** (no rng), in this order inside `CarveChasms`: after `CarveStrips`, `PickWalkways`, `CarvePit`, `CarveIsland`, `CutWalkways`, then `RaiseWalls` as today.
    - `internal static List<GridPos> PickWalkways(FloorLayout layout)` and `internal static void CutWalkways(FloorLayout layout, IReadOnlyList<GridPos> picks)` (Ruling 8).
    - `internal static (RailPit? Pit, string Why) CarvePit(FloorLayout layout, RailLine rail, int ride, int breakRide, int splitRide, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)`. It takes the first run (lowest start index) of at least `PitRunCells` consecutive straight party cells lying whole in the ride window. Each cell's enemy twin must be a straight enemy cell taken by nothing, both lanes must be at one level, and no ramp may lie on the run or within a cell of it. The pit is `run.From + PitLeadCells`.
    - `internal static (RailIsland? Island, string Why) CarveIsland(FloorLayout layout, RailLine rail, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)` follows Rulings 3–6.
      - Its bridge is the last straight party run of at least `BridgeMinCells` on the last ride, clipped to end `BridgeIslandCells` cells before the exit platform's first cell, with no platform, ramp or taken cell on it or its twins.
      - Its cuts keep every `TakenCells` rule and stay off the platforms' cells, but are exempt from `StripPlatformGap` (B.3).
  - **`LayoutReport.Rail`** appends ` pit={Cell}@{f}`, ` pit=none` or ` pit=refused` when asked, then ` island=room {a}+{b} sealed={trenches} bridge={From}..{To}`, ` island=none` or ` island={why}` when asked. `f` is the cell's fraction of its own ride, measured as `break=` is.
  - **`DungeonBuilder`** prints `[gen] {line}` for each `GenLog` line, after its `[DungeonBuilder] floor …` line.

- [ ] **Step 1: Measure the baseline.**
  - Run the measurement command (Global Constraints) and `grep -E "\[map\] room (1|9|12):" /tmp/p15-map.txt`.
  - Expected: every value in "Measured" (S ± 0.1), `hash=EBDAC17562E33A06`, and `r5_hounds`' encounter line at plan 14's committed At.
  - **Stop and report** without changing any code if a value differs.
- [ ] **Step 2: Write the failing tests.**
  - `ShippedFloors.Floor12(bool island)` builds the spec as `LeapRulesTests.ShippedFloor12` does, then `spec with { PitRide = island ? 5 : 0, ExitIsland = island }`. It asserts the plan's `LayoutHash` only when `island` matches the shipped campaign's knobs.
  - `internal static FloorLayout IslandTests.GenIsland(int seed, FloorObjectiveKind obj, bool island)` is `ChasmTests.GenSplit`'s spec plus `PitRide = 5, ExitIsland = island` (Task 2's tests call it).

```csharp
[Fact] public void TheKnobs_DefaultOff_CarryAndClamp()
{
    Assert.Equal((0, false), (FloorSpec.Default.PitRide, FloorSpec.Default.ExitIsland));
    Assert.Equal(5, new SpecOverrides { PitRide = 5 }.Apply(FloorSpec.Default).PitRide);
    Assert.Equal(6, new SpecOverrides { PitRide = 9 }.Apply(FloorSpec.Default).PitRide);
    Assert.Equal(0, new SpecOverrides { PitRide = -1 }.Apply(FloorSpec.Default).PitRide);
    Assert.True(new SpecOverrides { ExitIsland = true }.Apply(FloorSpec.Default).ExitIsland);
    Assert.False(new SpecOverrides { PitRide = 5 }.IsEmpty);
    Assert.False(new SpecOverrides { ExitIsland = false }.IsEmpty);
}

[Fact] public void Floor12_ThePit_IsCell282_SevenIntoXSixtyOnesStraight()
{
    var l = ShippedFloors.Floor12(island: true);
    var rail = l.Rail!;
    Assert.Equal(("ok", 5, 282), (rail.PitWhy, rail.Pit!.Ride, rail.Pit.Cell));
    Assert.Equal(new[] { new GridPos(61, 56), new GridPos(60, 56), new GridPos(59, 56) }, rail.Pit.Cut);
    foreach (var c in rail.Pit.Cut) Assert.True(l.IsChasm(c) && l.LevelOf(c) == l.LevelOf(rail.Party[281]));
    Assert.True(l.IsWalkable(new GridPos(62, 56)) && !l.IsBridge(new GridPos(62, 56))); // the enemy twin: solid lane, no deck
    Assert.DoesNotContain(new GridPos(60, 56), rail.Pockets);
}

[Fact] public void Floor12_TheIsland_IsRooms9And12_SealedAtTwoHalls_CrossedAt311To314()
{
    var l = ShippedFloors.Floor12(island: true);
    var i = l.Rail!.Island!;
    Assert.Equal("ok", l.Rail.IslandWhy);
    Assert.Equal(new[] { 9, 12 }, i.Rooms);
    Assert.Equal(new[] { new GridPos(46, 49), new GridPos(41, 54) }, i.Trenches);
    Assert.Equal(new[] { new GridPos(46, 46), new GridPos(46, 47), new GridPos(46, 48), new GridPos(38, 54), new GridPos(39, 54), new GridPos(40, 54) }, i.CaveIns);
    Assert.All(i.Trenches, c => Assert.True(l.IsChasm(c)));
    Assert.All(i.CaveIns, c => Assert.Equal(Cell.Wall, l[c]));
    Assert.True(l.IsWalkable(new GridPos(46, 45)));                                   // the fork stays open
    Assert.DoesNotContain(new GridPos(46, 49), l.TrapSpawns);
    Assert.Contains("island: trap at (46,49) sealed", l.GenLog);
    Assert.Equal((new RailSpan(311, 314), new RailSpan(307, 310)), (i.Bridge, i.Parked));
    for (var k = 311; k <= 314; k++) Assert.True(l.IsChasm(l.Rail.Party[k]) && l.IsPartyRail(l.Rail.Party[k]));
    Assert.Equal(new[] { new GridPos(51, 61), new GridPos(52, 61), new GridPos(53, 61) }, i.EnemyBridge);
    foreach (var (x, y) in new[] { (51, 58), (51, 59), (51, 62), (52, 62), (53, 62), (51, 63), (52, 63), (53, 63) }) Assert.True(l.IsChasm(new GridPos(x, y)), $"({x},{y})");
    Assert.False(l.IsChasm(new GridPos(48, 62)) || l.IsChasm(new GridPos(50, 62)) || l.IsChasm(new GridPos(54, 61)));
    Assert.True(i.Cells.Contains(l.DownStairs) && i.Cells.Contains(new GridPos(55, 52)) && i.Cells.Contains(new GridPos(50, 60)));
    Assert.DoesNotContain(i.Cells, l.Reachable(l.UpStairs).Contains);
}

[Fact] public void Floor12_KeepsTodaysOtherPlanks_AndEverythingOutsideTheCuts()
{
    var on = ShippedFloors.Floor12(island: true);
    var off = ShippedFloors.Floor12(island: false);
    Assert.Contains(new GridPos(46, 47), off.Walkways);
    Assert.Equal(off.Walkways.Where(w => w != new GridPos(46, 47)), on.Walkways);
    Assert.Equal((off.ShrineCell, off.BoundChest, off.SecretRooms.Count, off.Caches.Count), (on.ShrineCell, on.BoundChest, on.SecretRooms.Count, on.Caches.Count));
    Assert.Equal(off.EnemySpawns, on.EnemySpawns);
    Assert.Equal(off.ChestSpawns, on.ChestSpawns);
    Assert.Equal(off.KeySpawns, on.KeySpawns);
    Assert.Equal(off.BreakableSpawns, on.BreakableSpawns);
    Assert.Equal(off.LedgeSpawns, on.LedgeSpawns);
    Assert.Equal(off.CellarSpawns, on.CellarSpawns);
    Assert.Equal(off.TrapSpawns.Where(t => t != new GridPos(46, 49)), on.TrapSpawns);
}

[Fact] public void LayoutReport_NamesThePitAndTheIsland()
{
    var line = LayoutReport.Rail(ShippedFloors.Floor12(island: true))!;
    Assert.Contains(" planks=2 ", line);
    Assert.EndsWith(" split=227@0.38 crevasse=228..231 pit=282@0.25 island=room 9+12 sealed=2 bridge=311..314", line);
    Assert.DoesNotContain(" pit=", LayoutReport.Rail(ShippedFloors.Floor12(island: false))!);
}

[Theory, MemberData(nameof(ChasmTests.SplitSeeds), MemberType = typeof(ChasmTests))]
public void AnIsland_IsCutWholeOrNotAtAll_AndNeverLeaks(int seed, FloorObjectiveKind obj)
{
    var l = GenIsland(seed, obj, island: true);
    var noIsland = GenIsland(seed, obj, island: false);
    if (l.Rail is not { } rail) return;
    Assert.True(rail.IslandWhy is "ok" or "none" || rail.IslandWhy!.StartsWith("refused ("), rail.IslandWhy);
    if (rail.Island is { } i) Assert.DoesNotContain(i.Cells, l.Reachable(l.UpStairs).Contains);
    else Assert.Equal(noIsland.ContentHash(), l.ContentHash());
}

[Theory, MemberData(nameof(ChasmTests.SplitSeeds), MemberType = typeof(ChasmTests))]
public void ACutThatIsNotMade_MovesNothing(int seed, FloorObjectiveKind obj)
{
    var on = GenIsland(seed, obj, island: true);
    if (on.Rail?.PitWhy != "ok" && on.Rail?.IslandWhy != "ok") Assert.Equal(ChasmTests.GenSplit(seed, obj).ContentHash(), on.ContentHash());
}

[Fact] public void WithoutCrevasses_TheKnobsMoveNothing()
{
    var spec = FloorSpec.Default with { Layout = LayoutKind.Ring, RailCount = 4, RailLoop = true, RailBand = 14, Objective = FloorObjectiveKind.Jailbreak, BrokenBridgeRide = 3, SplitRide = 4 };
    foreach (var seed in ChasmTests.SplitSeeds().Select(o => (int)o[0]).Distinct().Take(10))
    {
        var dry = FloorGenerator.Generate(spec with { PitRide = 5, ExitIsland = true }, seed);
        Assert.Equal(FloorGenerator.Generate(spec, seed).ContentHash(), dry.ContentHash());
        Assert.Null(dry.Rail?.PitWhy);
        Assert.Null(dry.Rail?.IslandWhy);
    }
}
```

  - Add `ThePitAndTheIsland_AreDeterministic`: two builds of floor 12 with the island give equal hashes, `Pit`, `Bridge`, `Walkways` and `Cells.Count`.
- [ ] **Step 3: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~IslandTests"`. Expected: a compile error (`PitRide`, `RailPit`, `GenLog`).
- [ ] **Step 4: Implement** the Interfaces.
- [ ] **Step 5: Run them.** Run the filter, then the full suite. Expected: PASS, with `DefaultKnobs_BuildTodaysFloors` and every `ChasmTests` row green (the plank tests and the hash rows prove the split changes nothing with the knobs off).
  - **Stop and report** without tuning any rule if a floor-12 test fails on a cell list, the bridge, the planks or the fraction. Report the cells the rules produced.
- [ ] **Step 6: Commit**

```bash
git add src/Dungeon/Generation/FloorGenerator.Island.cs src/Dungeon/Generation/FloorGenerator.Chasms.cs src/Dungeon/Generation/RailLine.cs src/Dungeon/Generation/FloorLayout.cs src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Dungeon/Generation/LayoutReport.cs src/Dungeon/DungeonBuilder.cs
git commit -m "feat(gen): PitRide and ExitIsland (AiIgnore, off by default, needing Crevasses): a no-rng pass cuts a ride's lava pit and another seals the exit room and the rooms off it (a lava trench at each hall's mouth, a cave-in to its fork), cuts the last straight run before the exit platform to a rail-only bridge with a two-cell moat, and refuses whole when it would leak or hold a key; the planks are picked before the cuts and cut after them, so today's picks stand; RailLine.Pit/Island, [gen] lines, pit= and island= on the map line

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: The rail proof (FloorSolvability on an island floor)

**Files:**
- Modify: `src/Dungeon/Generation/FloorSolvability.cs` (made `static partial`)
- Create: `src/Dungeon/Generation/FloorSolvability.Island.cs`
- Test: `tests/Dungeon/IslandSolvabilityTests.cs`

**Rulings this task needs:**
- **4.** `RailIsland.Cells` is `Reachable(DownStairs)` after the cut (rails open, secrets shut). The carve refuses whole when the island would leak or hold a key, vault lever, prisoner cell or jail cell, so a refused floor is the floor without the island. Cost: none.
- **45.** In the proof:
  - "Every cage" reads as each intermediate stop's first platform cell (the cages are runtime, on sidings beside their platforms).
  - "Every room outside the island" reads as each room outside it that the up stairs reach when the island's cut and cave-in cells are read as open (`Reachable`'s `open`), but not as the floor stands. This is the `ChasmProblems` pattern, so secret rooms and vaults behind their own locks are judged as today.
  - Cost: a siding walled off from its platform would pass the proof (`PickSiding` takes only cells beside it).

**Interfaces:**
- Consumes: Task 1's `RailIsland`, `RailPit` and `FloorLayout.InIsland`.
- Produces: on a floor with `Rail.Island` (and on no other), `FloorSolvability.Problems`:
  - **Skips the stairs.** It drops "the stairs down … are out of reach with every lock worked" in both runs.
  - **Adds, rails open** (the every-lock-worked reach, with the reward vault open as the stairs check opens it):
    - **On foot.**
      - Each intermediate stop's first platform cell: `on foot, stop {k}'s platform at {c} is out of reach`.
      - Each party cell of `Island.Parked`: `on foot, the parked train's place at {c} is out of reach`.
      - Each room Ruling 45 names: `on foot, room {i} is out of reach`.
    - **Sealed.** The first island cell (Y, X) reachable from the up stairs with every door open: `the exit island leaks at {c}`.
    - **By rail.** Every cell from `Parked.From` to the cell before the last stop's platform must be a party-lane cell that is walkable, or a bridge cell that is a chasm: `the line into the island breaks at {c}`.
    - **Inside.** From the last stop's first platform cell, the stairs and a cell of each island room must be reachable: `inside the island, room {i} is out of reach from stop {k}'s platform` and `inside the island, the stairs down at {c} are out of reach from stop {k}'s platform`.
    - **The pit** (with `Rail.Pit`): the party cells either side of it, and the enemy twins of it and of them, must be walkable: `the pit cuts the line at {c}`.
  - **Adds, rails blocked:** each intermediate stop's platform, with today's `with the rails blocked, ` prefix.
  - **`ChasmProblems`** skips the island's rooms and the stairs, and its cells in the chest, key and objective checks.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Floor12WithTheIsland_IsProvenByRail()
    => Assert.Empty(FloorSolvability.Problems(ShippedFloors.Floor12(island: true)));

[Fact] public void AWalkIn_IsALeak()
{
    var l = ShippedFloors.Floor12(island: true);
    foreach (var x in new[] { 38, 39, 40, 41 }) l[new GridPos(x, 54)] = Cell.Floor; // the west hall reopened
    Assert.Contains(FloorSolvability.Problems(l), p => p.StartsWith("the exit island leaks at "));
}

[Fact] public void ABrokenLineIn_AndAPitThatCutsTheLine_AreReported()
{
    var a = ShippedFloors.Floor12(island: true);
    a[new GridPos(50, 60)] = Cell.Wall;
    Assert.Contains("the line into the island breaks at (50,60)", FloorSolvability.Problems(a));
    var b = ShippedFloors.Floor12(island: true);
    b[new GridPos(62, 56)] = Cell.Chasm;
    Assert.Contains("the pit cuts the line at (62,56)", FloorSolvability.Problems(b));
}

[Fact] public void InsideTheIsland_EveryRoomIsReachedFromTheLastPlatform()
{
    var l = ShippedFloors.Floor12(island: true);
    l[new GridPos(53, 53)] = Cell.Wall; // the safe room's door
    Assert.Contains("inside the island, room 12 is out of reach from stop 5's platform", FloorSolvability.Problems(l));
}

[Fact] public void WithoutTheIsland_NothingChanges() => Assert.Empty(FloorSolvability.Problems(ShippedFloors.Floor12(island: false)));

[Theory, MemberData(nameof(ChasmTests.SplitSeeds), MemberType = typeof(ChasmTests))]
public void EveryIslandRow_IsProvenOrIsTheFloorWithoutIt(int seed, FloorObjectiveKind obj)
    => Assert.Empty(FloorSolvability.Problems(IslandTests.GenIsland(seed, obj, island: true)));
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~IslandSolvabilityTests"`. Expected: FAIL ("the stairs down … are out of reach" on the island floor).
- [ ] **Step 3: Implement** the Interfaces.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS, with `RailLineTests.Sweep_NeverBreaksTheFloor`, `Floor_SolvableWithRailsBlocked` and `ChasmTests.Chasms_CutNothingOff` unchanged and green.
  - **Stop and report** if a row of `EveryIslandRow…` fails. The fix is a carve refusal or a proof rule, and the controller rules which.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorSolvability.cs src/Dungeon/Generation/FloorSolvability.Island.cs
git commit -m "feat(gen): an island floor is proven by rail: on foot every stop, room and the parked train's place outside are reached, the island never is, the party lane from the parked place to the exit platform runs unbroken but for the bridge's kept track, every island room and the stairs are reached from the last platform, and the pit leaves its enemy twins whole; the on-foot stairs checks are dropped there and nowhere else

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Floor 12 becomes an island (the knobs on, re-homed, its lava, cave-ins and exit bridge at runtime)

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/ExitBridgeRules.cs`
  - `src/Dungeon/Rails/RailEncounterController.Island.cs`
  - `src/Dungeon/Rails/SetPieces/ExitBridge.cs`
  - `src/Core/DebugTools.Island.cs`, `src/Core/DebugTools.Island.Cavein.cs`, `src/Core/DebugTools.Island.Bridge.cs`
- Modify:
  - Data:
    - `data/adventures/builtin/campaign.json`: floor 12's `SpecOverrides` gains `"PitRide": 5, "ExitIsland": true` after `"SplitRide": 4`;
    - `data/adventures/builtin/floor_12.json`: the `LayoutHash` by rehash, and `col_stairbell`'s goal (Ruling 38).
  - Rails:
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`BuildExitBridge` after `BuildBreak`);
    - `src/Dungeon/Rails/RailEncounterController.Bridge.cs` (`HoldsRiders` ORs the exit bridge; `HoldReason`);
    - `src/Dungeon/Rails/RailController.cs` (`Wreck`'s inside rule; `ExitCellNear`'s guard);
    - `src/Dungeon/Rails/RailController.Fall.cs` (`LastWreckInside` beside `LastWreckFell`);
    - `src/Dungeon/Rails/RailController.Moves.cs` (the refusal's reason);
    - `src/Dungeon/Rails/SetPieces/SpurSwitch.cs`, `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (Ruling 43);
    - `src/Dungeon/Rails/Core/TrackDepthRules.cs` (the pins).
  - Looks:
    - `src/Dungeon/Props/RailTrack.cs` (`OmitBallast`);
    - `src/Dungeon/ChasmDressing.cs` (lava);
    - `src/Dungeon/DungeonBuilder.cs` (the cave-ins' look);
    - `src/Dungeon/Rails/SetPieces/RailGap.cs` (its molten material and light, shared as a static).
  - Net: `src/Net/ServerWorld.cs` (the get-out refusal's print names the bridge).
  - Crew: `src/Adventure/Objectives/CrewRescueController.cs` (the test hook `DebugPutOnFoot`).
  - Flows:
    - `src/Core/DebugTools.cs` (`[map] rail pit:`, `[map] rail island:`, the flag and its help);
    - `src/Core/DebugTools.Cavern.cs` (the laid check);
    - `src/Core/DebugTools.TrackView.cs`, only if it names 0/40 outside `TrackDepthRules`.
- Test: `tests/Dungeon/Rails/ExitBridgeRulesTests.cs`, `tests/Dungeon/Rails/TrackDepthRulesTests.cs`, `tests/Dungeon/IslandTests.cs`, `tests/Dungeon/SafeRoomTests.cs`

**Rulings this task needs:**
- **1. Build order and the interim floor.**
  - Tasks 1–2 built the generator and the proof with floor 12 off. This task turns floor 12 on.
  - From here the pit cell is carved, but its track stays laid and `r5_jump` stays ride 5's hazard until Task 4 cuts the track and judges the pit. Between the two tasks the train rolls over the pit unjudged, while walkers fall into it as into any chasm.
  - Cost: one task with a hole the train ignores.
- **6. The lava.** A `ChasmDressing` region holding any `Rail.Pit.Cut` or `Island.Cut` cell is lava, which covers the gorge's last span and the strip (52..53,64..68) beside the moat. Cost: those spans look molten.
- **9. The exit bridge's track.** It is kept whole (never in `Gaps`). `RailTrack.OmitBallast(spans)` takes the gravel off over the span and leaves the rails and sleepers, so the lava shows between sleepers on the trestle's stringers. Cost: one `RailTrack` method.
- **10. The hold.** The exit bridge's hold is the second term of `RailEncounterController.HoldsRiders()` (cavern bridge or exit bridge).
  - So "Get out" (cart and wagons), `MovesRefused` (plan 14 Ruling 11: "Plan B adds the exit bridge here") and the throw clocks' pause take it with no new wiring.
  - It holds while the train rolls at any speed, so a train braked to a stop on the span holds its riders until it rolls (B.2).
  - `RailEncounterController.HoldReason()` names the bridge holding the riders: "the train rolls on the collapsing bridge" or "the train is on the exit bridge". `ServerWorld`'s refusal print, `get out refused ({HoldReason})`, and `MovesRefused`'s reason read it.
  - Cost: none.
- **11. A wreck at or past the bridge's start.** A plain wreck with the cart's front at or past the bridge's start (`ExitBridgeRules.WreckInside`):
  - No spare takes over, and no OnFoot event is raised.
  - Every rider of the cart and of each coupled wagon, crew included, is set down: on the island's set-down spots (`IslandSetDown`) when the rider stands at or short of the far edge, and through `ExitCellNear` where they are otherwise.
  - Then the train is uncoupled (`_train.UncoupleAll()`: hidden and not solid, `LostWagons` names them, and any restore couples fresh) and the cart hidden.
  - `StopIndex` becomes the last stop and `LastWreckInside` is set, cleared where `LastWreckFell` is.
  - Cost: a load after it puts the cart whole at stop 5, which is what "home" (B.9) means.
- **12. A set-down never lands on a drop.**
  - Every get-out and set-down that goes through `RailController.ExitCellNear` passes its result through `ExitBridgeRules.SafeExitCell`. That covers `StandBesideCart`, `StandBesideLane`, a wreck's and `EndChase`'s set-downs, and an NPC's seated-lead catch-up.
  - A result where `CrevasseRules.SetDownOk` fails is replaced by the exit cell (at-stop rule) of the nearest party index, searched outward i+1, i−1, i+2, … with ties to the higher index, whose result holds `SetDownOk`.
  - Cost: a body set down on the bridge's span stands at its nearer end, not beside the tub.
- **38.** `col_stairbell`'s goal in `floor_12.json` becomes `{ "Kind": "Guard", "Room": 9, "Objective": null, "Route": [], "AlarmTarget": null, "RallyRoom": 9 }`. Cost: none.
- **40. The cave-ins' look.**
  - Each face of a cave-in cell toward a walkable or chasm cell (4 on floor 12) gets `Rubble.Mound` and `Rubble.Collapse`, from a local `Random` seeded `cell.X * 73856093 ^ cell.Y * 19349663 ^ CurrentFloor`, as `DungeonBuilder.SealMouth` does.
  - It is look only: no collision, skipped headless, no gameplay stream touched. The face count is printed on every role.
  - Cost: none.
- **41. The new flows (this task's part).** `--test-island[=cavein|bridge|wreck|…]`.
  - Its dispatcher and shared helpers live in `DebugTools.Island.cs`, with one partial file per group (`.Cavein`, `.Bridge`; later tasks add `.Parked`, `.Lift` and `.Home`), plan 14's split pattern.
  - It uses solo floor 12 at lethality 3, every enemy culled as it appears, `[island]` lines, `RESULT ok ({variant})` or `FAIL (…)`, and plan 13's 10 s stall watchdog. The default is `=cavein`, with `_quitAfter = 300`.
  - Every stop, ride and piece id it names reads `Shape(rc)` (`LastRideStop`, `Rides`, `Kiln(rc, …)`), never a literal (B.10: every check reads `LineShape`).
  - A variant that needs more than its budget stops and is reported.
  - Cost: none.
- **43. `r5_spur`'s lever.** When `PointsS − SpurSwitch.LeverLead` lies on an exit bridge cell, the lever stands `SplitRules.LeverPast` (3 m) past the points on the party lane, and its seated window keeps its start (`WindowStartS = PointsS − LeverLead − SeatedSwitchRules.WindowAhead`). Cost: none.
- **44. The safe room's gathering.** A unit test pins that `SafeRoomRules.GatherCell` is an island cell, so the safe room's gathering spawns inside the island. Cost: none.

**Interfaces:**
- Consumes: Tasks 1–2; plan 13's `HoldsRiders` and `BridgeRules`; plan 14's `MovesRefused`, `TrainRules.Length`, `RunawayRules.TrainRearS`, `SetDownWagonHeroes`, `CoupleFresh`, `Train`, `_train.UncoupleAll()` and `CrewRescueController.DebugSeatCrew`.
- Produces:
  - **`ExitBridgeRules`** (engine-free, `DungeonCrawler.Dungeon.Rails`):
    - `const float LavaDepth = 3f`;
    - `bool Holds(bool rolling, float frontS, float rearS, float fromS, float toS)`;
    - `bool WreckInside(float frontS, float fromS)`;
    - `bool SetDownPastFarEdge(float bodyS, float toS)`;
    - `GridPos SafeExitCell(FloorLayout l, IReadOnlyList<GridPos> lane, int laneIndex, Func<GridPos, bool> free, bool atStop)` (Ruling 12);
    - `const string WreckLog = "The cart is wrecked on the bridge. You scramble across onto the island."`.
  - **`RailEncounterController`** (`.Island.cs`, every role):
    - `float ExitFromS, ExitToS` (party S of the bridge's near and far edges) and `float ExitEnemyFromS, ExitEnemyToS`, all NaN without an island.
    - `BuildExitBridge(Node3D root)` omits the ballast on both tracks over their spans, builds the `ExitBridge` look, and prints `[rails] exit bridge: party S={from:0.0}..{to:0.0} ({n} cells), enemy S={a:0.0}..{b:0.0} ({m} cells); rails kept, no deck, no footing`.
    - `bool ExitBridgeHolds()`, `string? HoldReason()`, and `HoldsRiders()` (cavern or exit).
    - `internal List<Vector3> IslandSetDown(int count)`: spots from `CrevasseRules.SetDownCells` round `rail.Party[Bridge.To + 1]`, island cells only, never a drop.
  - **`RailController`:** `bool LastWreckInside { get; private set; }`. `Wreck` does Ruling 11 and prints `[rails] wreck past the exit bridge's start (front S={f:0.0}): no spare takes over; {h} hero(es) and {c} crew set down inside; the train uncoupled; the stop is taken as {last}`.
  - **`RailTrack.OmitBallast(IReadOnlyList<(float from, float to)> spans)`.** The rails, sleepers, curve, S and `Gaps` are unchanged.
  - **`ExitBridge : Node3D`** (look only, skipped headless). `Build(TrackSampler party, TrackSampler enemy, (float From, float To) partySpan, (float From, float To) enemySpan, float lavaY)` lays two bents per cell, posts down to `lavaY`, cross-braces and a stringer under each rail, as boxes on `weathered_planks`.
  - **`ChasmDressing` lava regions.**
    - Each one gets a shaft down `LavaDepth` with a molten surface and light (`RailGap`'s) and its upper mist layer only (the lava's haze, B.4), with no ember floor.
    - The print becomes `[chasm] regions={r} decks={d} planks={p} cavern={c} lava={n}`.
  - **The cave-ins.** `DungeonBuilder.DressCaveIns(root)` (Ruling 40) prints `[DungeonBuilder] floor 12: {n} cave-in face(s)` (4 expected).
  - **`--print-map`** adds:
    - `[map] rail pit: ride {r} cell {i} {pos}, lip at party S={lip:0.0}, far edge {far:0.0}`;
    - `[map] rail island: rooms [{..}], {n} hall(s) sealed, the exit bridge party S={from:0.0}..{to:0.0} (cells {a}..{b}), enemy S={ef:0.0}..{et:0.0}`.
  - **`TrackDepthRules.Floor12NoFloorDrop`** holds the measured pair.
  - **`internal void CrewRescueController.DebugPutOnFoot(string crewId, Vector3 at)`** (a test hook, server and solo): unseated, set down at `at`, following.
  - **`--test-island=cavein`.** At stop 0, on foot. It fails unless:
    - **The build.** `[island] floor 12: rooms 9+12, trenches [(46,49), (41,54)], 6 cave-in cells (faces 4), exit bridge S=…..… (4 cells), lava regions {n}`. A ray down at each trench, moat and bridge cell finds no footing, and one at the pit's enemy twin finds the floor.
    - **The west hall.** The lead, walked from room 1 toward (46,54) (`ForcedMoveInput`), never passes world x 152.0 (the cave-in's face) in 8 s. A companion sent toward the down stairs finds no path and stays out of room 9.
    - **The north hall.** The same from the fork (46,45) toward (46,50).
    - **Into the lava.** The lead, teleported into room 9 and walked west, falls into the trench (fallen within 2 s). A companion walked along the lane from cell 310 onto 311 falls within 2 s. Both come back when the lead next steps onto stop 0's platform (plan 10's return), which the flow walks it onto.
  - **`--test-island=bridge`.** `rc.Restore(Shape(rc).LastRideStop)`, everyone boards, and ride 5 is ridden (with the jump made by pumping, until Task 4). A crew member is put on foot 40 m behind the cart (`ctrl.DebugPutOnFoot`). It fails unless:
    - **The hold.** While any car is over the span, the lead's prompt is "", a forced Get out does nothing, `MoveOfferFor` is null for every hero, and a forced `Move` is refused.
    - **Braked on the span.** Braked to a stop with the cart's front at `ExitFromS + 6`, nobody can get out for 8 s, and the crew member on foot is never on a drop cell (its catch-up lands by Ruling 12).
    - **The save** (Review Focus 4). A save there and a load give the cart Held at stop 4 and every hero and crew member on a `SetDownOk` cell.
    - **Let go.** Ridden again, the train arrives at stop 5.
  - **`--test-island=wreck`** (Review Focus 3). `rc.Restore(Shape(rc).LastRideStop)`, with crew in W1 and W2 (`ctrl.DebugSeatCrew` for stops 1 and 2) and spares left. The test wrecks the cart (`rc.DebugWreck()`) as its front passes `ExitFromS + 0.5`. It fails unless, within 1 s:
    - no spare took over, `StopIndex` is the last stop, `LastWreckInside` is set, and the cart and every party wagon are hidden and not solid;
    - every hero and crew member stands on an island cell, none on a drop, none fallen 3 s later.
    - Then a save and a load must give the cart Held at stop 5.
  - **`--test-cavern`'s build** adds `[cavern] exit bridge laid: party S={from:0.0}..{to:0.0} not bare, no gravel`. It fails if any gap overlaps the exit bridge.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheBridgeHoldsItsRiders_WhileAnyCarIsOverIt()
{
    Assert.True(ExitBridgeRules.Holds(rolling: true, frontS: 1184.0f, rearS: 1171.4f, fromS: 1183.9f, toS: 1199.9f));
    Assert.True(ExitBridgeRules.Holds(true, 1212.4f, rearS: 1199.8f, 1183.9f, 1199.9f));
    Assert.False(ExitBridgeRules.Holds(true, 1183.9f, 1171.3f, 1183.9f, 1199.9f));
    Assert.False(ExitBridgeRules.Holds(true, 1212.6f, 1199.9f, 1183.9f, 1199.9f));
    Assert.False(ExitBridgeRules.Holds(rolling: false, 1190f, 1177f, 1183.9f, 1199.9f));
    Assert.False(ExitBridgeRules.Holds(true, 1190f, 1177f, float.NaN, float.NaN));
}

[Fact] public void AWreckJustPastTheStart_IsInside_AndSetsEveryoneDownPastTheFarEdge()
{
    Assert.True(ExitBridgeRules.WreckInside(frontS: 1184.4f, fromS: 1183.9f));
    Assert.False(ExitBridgeRules.WreckInside(1183.8f, 1183.9f));
    Assert.False(ExitBridgeRules.WreckInside(1190f, float.NaN));
    Assert.True(ExitBridgeRules.SetDownPastFarEdge(bodyS: 1171.8f, toS: 1199.9f)); // W4, still outside: inside past the far edge
    Assert.False(ExitBridgeRules.SetDownPastFarEdge(1205f, 1199.9f));               // already past it: where it is
}

[Fact] public void EveryExitCell_OnTheIslandFloor_IsASetDownCell()
{
    var l = ShippedFloors.Floor12(island: true);
    var lane = l.Rail!.Party;
    bool Free(GridPos c) => CrevasseRules.SetDownOk(l, c) && !l.IsRail(c);
    for (var i = 0; i < lane.Count; i++)
        foreach (var atStop in new[] { true, false })
        {
            var c = ExitBridgeRules.SafeExitCell(l, lane, i, Free, atStop);
            Assert.True(CrevasseRules.SetDownOk(l, c), $"party index {i}, atStop {atStop}: {c}");
        }
}
```

  Then update the other tests:
  - **`TrackDepthRulesTests`.** The pin becomes the measured pair (Step 3).
  - **`IslandTests`.** The shipped helper now asserts the hash with `island: true`.
  - **`SafeRoomTests.OnTheIslandFloor_TheGatheringStandsInside`:** `ShippedFloors.Floor12(true).InIsland(SafeRoomRules.GatherCell(l)!.Value)`.
- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~ExitBridgeRulesTests|FullyQualifiedName~SafeRoomTests"`. Expected: a compile error.
- [ ] **Step 3: Switch floor 12 on and measure** (lock absent).
  - Edit `campaign.json` (the knobs) and `floor_12.json` (Ruling 38's goal).
  - Run `dotnet build DungeonCrawler.csproj && timeout 60 godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`, then the sweep:

    ```bash
    for N in $(seq 1 20); do echo "== floor $N"; timeout 60 godot --headless --path . -- --builtin --floor=$N --quit-after=4 --no-ambush --no-portals --no-gathering 2>&1 | grep -iE "repaired|dropped|demoted|adapted"; done
    timeout 120 godot --headless --path . -- --builtin --quit-after=4 2>&1 | grep -c adapted   # 0
    ```

    Expected: no repair line, and `git diff --stat data/` shows only `campaign.json` and `floor_12.json`.
  - Implement the Interfaces, then run the measurement command (Global Constraints).
  - **The static gate.** `StopS`, `rides`, `cavern=`, `break=`, `split=` and every piece and encounter S must equal the baseline ± 0.1. **Stop and report** if any moved.
  - **Report** the `[map] rail pit:` and `[map] rail island:` lines, the new hash, `chasm=`, `[chasm]` and the measured `nofloorDrop` pair.
  - **Pin `nofloorDrop`** to the measured pair. If it is not PartyTrack 200 / EnemyTrack 160, the commit explains the difference cell by cell (B.2): 40 samples per 4 m of drop under the track.
  - Re-run: the total reads `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`, longest first:
  - `--test-chase --quit-after=750`, `--test-crew --quit-after=570`;
  - `--test-island=cavein`, `=bridge`, `=wreck`;
  - `--test-boarders=groups`;
  - `--test-crevasse`, `=trestle`, `=survivor`;
  - `--test-cavern`, `=bridge`, `=leap`;
  - `--test-split`, `--test-switch-back`, `--test-barricade`, `--test-ride`, `--test-ride-edges`, `--test-relief`, `--test-cart-watchdog`, `--test-track-view`, `--test-vault-puzzle`.

  Each must PASS with gathering on and no ERROR line. Then run plain `scripts/net_test.sh`, alone: `NET TEST: ok`.

  The RIDE pass is red by construction from here until Task 4: its end-of-line wreck now takes no spare. Do not run it.
- [ ] **Step 5: Commit** (the message names the old and new hash and the measured pins)

```bash
git add data/adventures/builtin/campaign.json data/adventures/builtin/floor_12.json src/Dungeon/Rails/Core/ExitBridgeRules.cs src/Dungeon/Rails/RailEncounterController.Island.cs src/Dungeon/Rails/SetPieces/ExitBridge.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Bridge.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailController.Fall.cs src/Dungeon/Rails/RailController.Moves.cs src/Dungeon/Rails/SetPieces/SpurSwitch.cs src/Dungeon/Rails/RailEncounterController.Kiln.cs src/Dungeon/Rails/Core/TrackDepthRules.cs src/Dungeon/Props/RailTrack.cs src/Dungeon/ChasmDressing.cs src/Dungeon/DungeonBuilder.cs src/Dungeon/Rails/SetPieces/RailGap.cs src/Net/ServerWorld.cs src/Adventure/Objectives/CrewRescueController.cs src/Core/DebugTools.cs src/Core/DebugTools.Cavern.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Cavein.cs src/Core/DebugTools.Island.Bridge.cs
git commit -m "feat(rails): floor 12's exit is an island (PitRide 5, ExitIsland, rehashed): lava in the trenches, the moat and the pit, boulders against the cave-ins, an open trestle under the exit bridge's kept rails; no Get out and no move car to car while any car is over the bridge; a wreck past its start sets everyone down inside, uncouples the train and takes no spare; no set-down ever lands on a drop; col_stairbell guards room 9; nofloorDrop re-pinned from the measurement; --test-island=cavein|bridge|wreck

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `src/Core/DebugTools.TrackView.cs` only if it changed.)

### Task 4: The pit switch (ride 5 re-laid round the pit, and the RIDE pass repaired)

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/PitRules.cs`
  - `src/Dungeon/Rails/RailEncounterController.Pit.cs`
  - `src/Dungeon/Rails/SetPieces/LavaPit.cs`
  - `src/Core/DebugTools.Pit.cs`
- Modify:
  - Data and plan: `data/adventures/builtin/floor_12.json` (`r5_jump` → `r5_pit`, `r5_hammers`' At, `r5_hounds`' At by Ruling 15, `r5_switch_out` and `r5_switch_back` appended), `src/Adventure/Core/Validate/PlanValidator.cs` (the kind `pit`), `src/Adventure/Core/Plans/FloorPlan.cs` (the kind's doc).
  - Encounters:
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs`: `ReadPit` before the loop, the `pit` case, `GuardPit` after `GuardBreak`, and the pit's calls in the tick;
    - `src/Dungeon/Rails/RailEncounterController.Switchyard.cs`: `PitDeadly`, R-lone and the log in `TakeSwitch`, and the deadly lever's lamp;
    - `src/Dungeon/Rails/RailEncounterController.cs`: `TickPit` beside `TickBreak`; `CollectPartyLaneHoles` gains the pit;
    - `src/Dungeon/Rails/RailEncounterController.Traffic.cs`: `RunawayEnd`.
  - Rails:
    - `src/Dungeon/Rails/RailController.Watchdog.cs` (the pit's step-down);
    - `src/Dungeon/Rails/RailController.Jump.cs` (the lane check);
    - `src/Dungeon/Rails/Core/SetPieceRules.cs` (`JudgesJump`, `RunawayEnd`);
    - `src/Dungeon/Rails/RailController.Net.cs` (`DebugRestoreAt`, beside `DebugWreck`).
  - Barks: `src/Adventure/Core/Plans/CampaignPlan.cs` (`PitSwitch`, `PitSwitchNow`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Adventure/Core/Voice/VoiceLines.cs`.
  - Flows:
    - `src/Core/DebugTools.cs` (the flag and its help; the cavern and watchdog help lines);
    - `src/Core/DebugTools.RideOn.cs` (`ThrowPitOnTheWay`);
    - `src/Core/DebugTools.Chase.cs`, `src/Core/DebugTools.Crevasse.cs`, `src/Core/DebugTools.CartWatchdog.cs`, `src/Core/DebugTools.Cavern.cs`;
    - `src/Core/DebugTools.Boarders.Groups.cs` (plan 14 Task 6's file);
    - `src/Core/DebugTools.Island.cs`, `src/Core/DebugTools.Island.Bridge.cs` (`=bridge` and `=wreck` throw the pit on the way);
    - `src/Core/DebugTools.Crew.cs` (only if it drives ride 5 itself).
  - RIDE: `src/Net/NetTestFlow.cs`, `scripts/net_test.sh` (Ruling 22's repairs).
- Test: `tests/Dungeon/Rails/PitRulesTests.cs`, `tests/Dungeon/Rails/SetPieceRulesTests.cs`, `tests/Adventure/RailEncounterPlanTests.cs`, `tests/Dungeon/Rails/TrackStationsTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Rulings this task needs:**
- **1.** From Task 3 the pit cell was carved but its track stayed laid and `r5_jump` stayed ride 5's hazard. This task cuts the track, judges the pit and removes the jump from floor 12. Cost: none here.
- **13. The pit switch reuses ride 3's machinery.**
  - `Piece.PitDeadly` is a `switch_out` on `PitRide` with `PitRules.Deadly(points, lip)`. Its `TrackSwitch` is `Deadly` (human only, layers 1|8), and its seated offer is the plain switch_out's (no "across" condition).
  - The back is a plain `switch_back`, so `BuildSwitch` makes it `Fixed` and `TakeSwitch` crosses with `backS` set.
  - R-lone is decided in `TakeSwitch` (`PitRules.TakesRound`). The crossing logs "The cart takes the switch round the pit.", and the late press is `LateFor` as it stands.
  - Cost: none.
- **14. Placement.**
  - `r5_switch_out`'s points are at lip − `PitRules.OutLead` (8.0 m), and `r5_switch_back`'s at the far edge + `PitRules.BackPast` (1.0 m).
  - The hammers' middle is at S 1157.0 (B.3's 1154/1157/1160).
  - Each At is `(S − StopS[4]) / (StopS[5] − StopS[4])` to 4 decimals, from the measured S (plan 13's rule).
  - Cost: none.
- **15. `r5_hounds`** (the controller's ruling).
  - **The start.** Plan 14's committed value (28f6ebb): At 0.2749 (S 1080.0), Offset −8. That pace point lies inside the re-laid crossing, so this task moves it. The Offset stays −8.
  - **The gates:**
    - **gate (a):** on the hound cart's spawn tick, the train's rear is home (rear S ≥ `r5_switch_back`'s points + `SwitchStretch`), and the cart is never rammed nor freed by the crossing;
    - **gate (b):** both hounds land at least 1.5 s before the golem's burst in the measured flows, `--test-boarders=groups` and `--test-chase`, as they ride (no flow pumps before the golem).
    - **Printed, not gated:** a pumped approach's margin as an estimate (the measured margin minus the extra distance the train covers at the pumped speed), printed beside each flow's margin.
  - **The search.** Move the pace point into S [1095.5, 1105.0], trying S 1096.0, 1097.0, … 1105.0 in order (upward, 1 m steps). Take the first S that passes gate (a). For each S tried:
    - compute At = `(S − StopS[4]) / (StopS[5] − StopS[4])` to 4 decimals, on the re-laid ride 5's measured `StopS`;
    - write it into `floor_12.json`;
    - re-run the measurement command, whose static-gate encounter line must read `r5_hounds` at that S ± 0.1;
    - then run both flows.
  - **At the first S passing gate (a),** check gate (b). If it fails, **stop and report** both flows' margins (and the printed estimate). Do not search further: (b) only gets worse upward.
  - **Stop and report** too if no S in the window passes gate (a). That report gives, for each S tried, the spawn S, the rear S on the spawn tick, and any ram or crossing free.
  - No other retune is allowed (no Offset, no other piece). Keep any mark plan 14's fix round put on the boarding groups' entries.
  - Cost: a STOP in Task 4 waits for the controller's ruling; the pumped approach is unproven (an estimate only).
- **16. The plan's indices stay.** `r5_jump` becomes `r5_pit` in its own entry, and `r5_switch_out` and `r5_switch_back` are appended after `r4_leap`. Cost: none (wire names are plan indices).
- **17. The pit is read and cut from the layout** on every role (`ReadPit`, before the pieces), whether or not a `pit` piece names it.
  - The `r5_pit` piece is its plan entry: it gives the dressing and the order.
  - An `r5_pit` more than `PitRules.AtSlack` (2 m) from the lip, or a layout pit with no piece, is an ERROR line. The cut and the guard stand regardless.
  - Cost: none.
- **18. The watchdog's pit steps.** `WatchdogPit` feeds `GrindFree`'s existing `BackWindow`, `BackPoints` and `BreakLip` slots, with prints that name the pit. The break and the pit never share a ride, and the armed ride picks one. Cost: three enum names that read "back" for the pit.
- **19. The jump's lane check.** A gap whose lip the train meets across (`LaneBlendAt` ≥ 0.5) is passed, never judged, and the watchdog's `GapLip` is given only when the train is on its own lane there. Cost: none.
- **20. The traffic runaway** ends `RunawayShort` (3 m) short of the pit's lip when the pit lies between its stop and the next. Cost: none.
- **21. Throwing the pit on the way.**
  - Every flow that rides ride 5 past the pit throws the lever from the seat through one helper, `ThrowPitOnTheWay`, called by `RideOnTo` and by every flow that drives ride 5 itself.
  - The fall tests leave it straight (`DebugTools.LeavePitStraight`): `--test-pit=wrong|stall|late` and `--test-crevasse` B, C and `=survivor`.
  - `--test-crevasse=trestle` throws it and falls at the trestle as today (its point is a train falling with the trestle).
  - Cost: none.
- **22. The RIDE pass's window.**
  - It is red by construction only from Task 3, whose end-of-line wreck takes no spare, to this task, which repairs it:
    - the remote pit throw replaces the jump's pumping;
    - the wreck is re-pointed (Ruling 42);
    - the chaser's "hopped" check names the pit or the fallen trestle.
  - This task then hands a RIDE run to the controller. Task 6 rewrites step 8 in the task that changes the chaser, and Task 8 adds the new checks and the last run.
  - Plain `net_test.sh` is green in every task.
  - Cost: one more controller run.
- **39 (this task's part).** `BarkTrigger.PitSwitch` and `PitSwitchNow` are appended after `BoarderThrow`, both Shout.
  - Each has subtitle-only stock banks in `RideBarks`: 3 per roster name (Sable's a woman's), 2 per class and 3 generic. None names the switch's place.
  - The logs with nobody to speak are "Lava pit on the line — throw the switch!" and "Throw it — now!".
  - Cost: none.
- **41 (this task's part).** `--test-pit[=thrown|wrong|lone|stall|late]` lives in `DebugTools.Pit.cs`, default `=thrown`, with `_quitAfter = 200`. It uses solo floor 12 at lethality 3, every enemy culled, `[pit]` lines, `RESULT ok ({variant})` or `FAIL (…)`, plan 13's stall watchdog, and `Shape(rc)` for every stop and id. Cost: none.
- **42. The end-of-line wreck.**
  - The RIDE pass's wreck at stop 5 is past the bridge's start, where B.2 forbids a spare. It now checks B.2: no spare, and the riders set down inside.
  - The server then calls `rc.DebugRestoreAt(Shape(rc).LastRideStop)` and wrecks the cart there, which carries the old spare checks (spare 3, then spare 2).
  - `DebugRestoreAt(int stop)` calls `Restore(stop, null, <the spares used so far>)`, so the spares stay spent, and prints `[net-server] ride: test restore at stop {s} (not a rise)`, which the wipe-rise watch does not read.
  - Cost: one more test hook.

**Interfaces:**
- Consumes:
  - Task 3's `[map] rail pit:` line (the lip and the far edge), `HoldsRiders`, `ExitFromS`, `--test-island=bridge|wreck` and `ExitBridgeRules`;
  - plan 11's `SwitchBackRules` (`HomeStretch`, `GrindBackTo`, `Late`, `SafeLineLog`, `TooLateLine`, `BackCall`), `SeatedSwitchRules`, `TrackSwitch.Deadly` and `BuildSignal`'s lamp;
  - plan 14's `AnyLeverWindow`, `MovesRefused`, `RailCalls.Say` and `BarkTrigger.BoarderThrow`;
  - plan 14 Task 6's `SetPieceRules.CrossingFrees`, `--test-boarders=groups` and the final `r5_hounds`;
  - plan 14 Task 7's RIDE pass.
- Produces:
  - **`PitRules`** (engine-free):
    - the numbers: `LeadMin = 7f`, `LeadMax = 40f`, `OutLead = 8f`, `BackPast = 1f`, `AtSlack = 2f`, `CallEarly`/`CallLast` (= `SwitchBackRules`'), `ArmLead = SwitchBackRules.HomeStretch + 1f`;
    - `bool Deadly(float pointsS, float lipS)`, `bool Falls(float prevFront, float front, float lipS, float blendAtLip)`, `bool FallArmed(IReadOnlyList<float> outPoints, IReadOnlyList<float> backPoints, float lipS, float farS)`;
    - `BackCall Call(bool thrown, bool taken, float front, float leverS, float pointsS, bool earlyDone, bool lastDone)`, `bool TakesRound(bool thrown, bool ableHumanAboard)`, `float OutPoints(float lipS)`, `float BackPoints(float farS)`;
    - the copy: `FallLog = "The cart runs into the lava pit!"`, `RoundLog = "The cart takes the switch round the pit."`, `EarlyLog = "Lava pit on the line — throw the switch!"`, `LastLog = "Throw it — now!"`.
  - **`SetPieceRules`:** `bool JudgesJump(bool gapSet, bool judged, float front, float gapFrom, float blendAtLip)` and `float RunawayEnd(float nextStopS, float pitLipS)`. `RunawayShort` (3 m) moves here, and the controller's constant reads it.
  - **`RailEncounterController`** (`.Pit.cs`, every role unless marked):
    - `float PitS`, `PitFarS` (NaN without), `int PitRide = -1`, `bool PitArmed`.
    - `ReadPit(Node3D root)` cuts the party track bare over [lip, far] and builds `LavaPit` (bent rail stubs, red lamps at both edges, the lava's glow and haze). It prints `[rails] pit on ride {r}: cell {i} {pos}, lip at party S={lip:0.0}, far edge {far:0.0}; the party track bare over it`.
    - The `pit` piece prints `[rails] pit {id}: At S={s:0.0} ({d:0.0} m from the lip)`.
    - `Piece.PitDeadly`. The switch's build print reads `(deadly: the pit {lead:0.0} m past)`, and the deadly out lever's post carries a steady red lamp.
    - `GuardPit()` prints `[rails] pit on ride {r}: its fall armed (switch_out at [..], switch_back at [..])`, or an ERROR line, and disarms.
    - `TickPit()` (server): `_rails.Fall(PitRules.FallLog)` and `[rails] the cart runs into the pit at S={front:0.0}`.
    - `TickPitCalls(Piece, PartyController)` (server) prints `[rails] switch {id} called {early|last} at front S={f:0.0} ({d:0.0} m to the lever|points); bark={who}`.
    - `internal (float Window, float Points, float Lip) WatchdogPit()` (server): from `GrindBackTo` over the out lever, with the lip given only while the train is on the party lane past the points.
    - `CollectPartyLaneHoles` adds (`PitS`, `PitFarS`). `internal int PitFalls` is for tests.
  - **`RailController.GrindFree`** takes `WatchdogPit()` when `WatchdogBreak()` gives nothing. Its prints name the pit:
    - `… short of the pit switch's window (its lever straight); set with its front at the window's start`;
    - `… at the last call's mark`;
    - `… at the pit switch's points, taken as its lever stands`;
    - `… past the points on the party lane; ground on into the pit: the train falls`.
  - **`JudgeJump`** and the watchdog's lip read `SetPieceRules.JudgesJump`.
  - **`DebugTools.ThrowPitOnTheWay(Main main, RailController rc, string tag)`.** It presses the deadly out lever once for the seated lead, through the dispatch `--test-switch-back`'s thrown branch uses, when its seated window holds the front. It prints `[{tag}] pit: thrown from the seat at front S={f:0.0} ({d:0.0} m before the lever)`. `RideOnTo` calls it while rolling.
  - **`internal void RailController.DebugRestoreAt(int stop)`** (Ruling 42).
  - **Floor 12's ride 5:**

    | Id | Kind | Placed at (party S) |
    |---|---|---|
    | `r5_switch_out` | `switch_out` | `PitRules.OutPoints(lip)` (lever 5 m before) |
    | `r5_pit` (was `r5_jump`) | `pit` | the lip |
    | `r5_switch_back` | `switch_back` | `PitRules.BackPoints(far)` |
    | `r5_hammers` | `hammers` | middle at 1157.0 |
    | `r5_hounds` (encounter) | `slag_cart` | the first S in 1096.0..1105.0 (1 m steps) passing Ruling 15's gate (a), with gate (b) holding there; Offset −8 |

  - **`--test-pit`.** `rc.Restore(Shape(rc).LastRideStop)`, everyone boards, and ride 5 departs.
    - **The build** prints `[pit] floor 12: …` with the lip, far, out points, lever, back points, `(deadly …)`, `(fixed)` and `fall armed`. It fails unless:
      - the party track is bare over exactly [lip, far] and nowhere else on ride 5 but the trestle's own;
      - the pit cell and its two inward cells are chasm, and the enemy twin is walkable;
      - a layer-1 ray from the seated lead's eye at the lever's S to the near lamp is clear.
    - **`=thrown`** (the default). It fails unless:
      - the early call comes with the front 25–30 m before the lever;
      - until the press, the lever stays straight with no companion's hand on it;
      - in the window, `MovesRefused` holds and `MoveOfferFor` is null for every hero;
      - the lead presses on the window's first frame;
      - every car passes the lip with `LaneBlendAt` ≥ 0.99 there;
      - the train is home (blend ≤ 0.01) by the back's points + 8.1 m, as `--test-switch-back`'s bound (the 6 m blend read at the cart's centre, the front 1.1 m ahead, 1 m of slack), with no fall, and arrives at stop 5.
    - **`=wrong`.** Nobody presses. It fails unless:
      - the last call comes 5–6.5 m before the points;
      - the train passes the points straight and falls with its front within 0.6 m of the lip, logging `FallLog`;
      - every rider is fallen within 1 s;
      - the wipe rises at stop 4, with every hero up within 10 m of a whole cart.
    - **`=lone`.**
      - Pass 1: the lead, the only human, is downed in the seat before the window (held down until the points, possession off). At the points the train takes the safe line with the lever straight, logging `SafeLineLog`. It comes home and arrives at stop 5 with no fall.
      - Pass 2: restored again, the lead is seated and up in W2 (`rc.DebugSeat(lead, 2)`). That is not able (`AbleToThrow` reads the cart), so it takes the same safe line.
    - **`=stall`.** As `=wrong`, but `DebugStall` catches the train four times:
      - 25 m before the lever: set at the window's start, still rolling;
      - 8 m before the lever: set at the last call's mark, and the last call is heard after it;
      - 3 m short of the points: set at the points and taken straight;
      - 5 m past the points: ground into the pit, where it falls.

      It is never carried past the pit.
    - **`=late`.** The lead's press arrives on the tick the front reaches the points, and again 1 m past them. Each gets "Too late to throw it.", the lever stays straight and the train falls.
  - **The re-pointed flows** (B.10):
    - **`--test-cart-watchdog` step 3.** The train, caught with its front 25 m before the pit lever, is set at the window's start still rolling. The lead throws from the seat, and the train comes home past the far edge and arrives at stop 5. The RESULT line names the pit.
    - **`--test-chase`.** Ride 5's jump check becomes `[chase] 4 … the pit switch thrown at front S={f}, across from {points}, home by {home}, no fall`, with `RoundLog` logged and the hazard stamp `pit` from `PitSwitch`. Section 2's one-pumper jump becomes `PumperCap` 1 with W1..W4 and the pit thrown: no fall, arrival at stop 5.
    - **`--test-crevasse`.** B, C and `=survivor` leave the lever straight with the lead able. The train runs into the pit and falls (`FallLog`), with W1's crew seated (`ctrl.DebugSeatCrew` for stop 1). `=trestle` throws the pit and falls at the trestle as today.
    - **`--test-cavern`.** The party track is bare over `PitS`..`PitFarS`, in place of `r5_jump`'s gap.
    - **`--test-boarders=groups`.** It rides through `RideOnTo` (the pit thrown) and gates `r5_hounds` by Ruling 15. `--test-chase` prints its landing margin as it rides, and both print the pumped estimate beside it.
    - **`--test-island=bridge`, `=wreck`.** They throw the pit on the way, in place of the jump's pumping.
  - **The RIDE pass** (Ruling 22). On ride 5 the first client presses on the first frame its seat offers "Throw the switch" for `r5_switch_out`.
    - The server prints its `interact switch:{i} from Rider: … seated window yes` line and `switch r5_switch_out taken … across from …`.
    - Both clients print `[net] ride: across round the pit here (CrossOut {a:0.0}, CrossBack {b:0.0})`, with the server's values within 0.1.
    - No `runs into the pit` prints anywhere.
    - The jump's checks (two pumpers, `leaps the gap`) go.
    - The end-of-line wreck follows Ruling 42. `DebugWreck` at stop 5 prints `ride: wreck at the end of the line: past the exit bridge's start, no spare took over; {h} hero(es) set down inside`, and the joiner sees Wrecked and no takeover. Then `DebugRestoreAt` and a wreck at stop 4 print `ride: wreck: … took over` (spare 3), and the old `wreck-spare:` and stop-2 heap steps run from there. The joiner's `wreck=1` holds: the stop-4 wreck lies beyond its 30 m.
    - "the chaser never hopped ride 5's gap" reads "… ride 5's pit or the fallen trestle".

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ASwitchOut_IsDeadly_WithThePit7To40mPastItsPoints()
{
    Assert.True(PitRules.Deadly(pointsS: 1065f, lipS: 1073f));
    Assert.True(PitRules.Deadly(1033f, 1073f));
    Assert.False(PitRules.Deadly(1066.1f, 1073f)); // 6.9 m
    Assert.False(PitRules.Deadly(1032.9f, 1073f)); // 40.1 m
    Assert.False(PitRules.Deadly(1065f, float.NaN));
}

[Fact] public void TheTrainFalls_MeetingTheLipOnItsOwnLane_Once()
{
    Assert.True(PitRules.Falls(prevFront: 1072.9f, front: 1073.0f, lipS: 1073f, blendAtLip: 0.49f));
    Assert.False(PitRules.Falls(1072.9f, 1073.0f, 1073f, blendAtLip: 0.5f));
    Assert.False(PitRules.Falls(1073.0f, 1073.1f, 1073f, 0f));
}

[Fact] public void TheFall_IsArmedOnlyWithRoomToCross_AndABackPastTheFarEdge()
{
    Assert.Equal(7f, PitRules.ArmLead);
    Assert.True(PitRules.FallArmed(new[] { 1065f }, new[] { 1078f }, lipS: 1073f, farS: 1077f));
    Assert.False(PitRules.FallArmed(new[] { 1066.1f }, new[] { 1078f }, 1073f, 1077f));
    Assert.False(PitRules.FallArmed(new[] { 1065f }, new[] { 1076.9f }, 1073f, 1077f));
    Assert.False(PitRules.FallArmed(Array.Empty<float>(), new[] { 1078f }, 1073f, 1077f));
    Assert.True(PitRules.FallArmed(new[] { PitRules.OutPoints(1073f) }, new[] { PitRules.BackPoints(1077f) }, 1073f, 1077f));
    Assert.True(PitRules.Deadly(PitRules.OutPoints(1073f), 1073f));
}

[Fact] public void TheCalls_EarlyThirtyBeforeTheLever_LastSixBeforeThePoints()
{
    Assert.Equal(BackCall.Early, PitRules.Call(thrown: false, taken: false, front: 1030f, leverS: 1060f, pointsS: 1065f, earlyDone: false, lastDone: false));
    Assert.Equal(BackCall.None, PitRules.Call(false, false, 1029.9f, 1060f, 1065f, false, false));
    Assert.Equal(BackCall.Last, PitRules.Call(false, false, 1059f, 1060f, 1065f, earlyDone: true, false));
    Assert.Equal(BackCall.None, PitRules.Call(thrown: true, false, 1059f, 1060f, 1065f, true, false));
    Assert.Equal(BackCall.None, PitRules.Call(false, taken: true, 1059f, 1060f, 1065f, true, false));
}

[Fact] public void AtThePoints_AStraightLeverWithNoAbleHuman_TakesTheSafeLine()
{
    Assert.True(PitRules.TakesRound(thrown: true, ableHumanAboard: true));
    Assert.True(PitRules.TakesRound(false, ableHumanAboard: false));
    Assert.False(PitRules.TakesRound(false, true));
}

[Fact] public void AJump_MetAcross_IsPassed_AndTheRunawayStopsShortOfThePit()
{
    Assert.True(SetPieceRules.JudgesJump(gapSet: true, judged: false, front: 1156f, gapFrom: 1156f, blendAtLip: 0.49f));
    Assert.False(SetPieceRules.JudgesJump(true, false, 1156f, 1156f, blendAtLip: 0.5f));
    Assert.Equal(1070f, SetPieceRules.RunawayEnd(nextStopS: 1231.7f, pitLipS: 1073f), 3);
    Assert.Equal(1228.7f, SetPieceRules.RunawayEnd(1231.7f, float.NaN), 3);
}
```

  Then update the other tests:
  - **`RailEncounterPlanTests`.** The set-piece id list reads `r5_pit` in `r5_jump`'s place and ends with `"r5_switch_out", "r5_switch_back"`, and the count is 23. The ride-5 At order is `switch_out < pit < switch_back < pour < trestle < hammers < spur < barricade < buffer`, with the encounters `r5_switch_back.At < r5_hounds.At < r5_golem.At`. `r5_switch_out` is `PitRules.Deadly` against `r5_pit` by At.
  - **`TrackStationsTests`.** `Floor12`'s second gap is the measured (lip, far). The lattice test keeps (1156.0, 1161.0) as a synthetic on-lattice gap.
  - **`RideBarksTests`.** `PitSwitch` and `PitSwitchNow` are appended to the triggers and to Shout: 3 per name, 2 per class, 3 generic.
- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~PitRulesTests|FullyQualifiedName~SetPieceRules|FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~TrackStationsTests|FullyQualifiedName~RideBarksTests"`. Expected: a compile error.
- [ ] **Step 3: Implement and place.**
  - Implement the Interfaces.
  - Run the measurement command (Global Constraints) and read the lip and the far edge from its `[map] rail pit:` line. Write the data from them (Ruling 14).
  - Run the measurement command again. The pieces must read their placed S ± 0.1: `r5_switch_out` lip − 8.0, `r5_pit` the lip, `r5_switch_back` far + 1.0, `r5_hammers` 1154.0/1157.0/1160.0. The build must print `fall armed`.
  - The `nofloorDrop` pair must still read the pins in `TrackDepthRules.Floor12NoFloorDrop` with the pit's track cut, and the total `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`.
  - Then run `--test-pit`, and Ruling 15's search for `r5_hounds` (`--test-boarders=groups` and `--test-chase --quit-after=750` at each S, the measurement command's encounter line after each edit).
  - **Stop and report** without retuning if:
    - a placed S is off by more than 0.1;
    - the `nofloorDrop` pair moved;
    - the early call falls outside 25–30 m;
    - the sight ray is blocked;
    - the guard disarms;
    - Ruling 15's search stops (no S passes gate (a), or gate (b) fails at the first that does), with its report.
  - Every flow that rides ride 5 past the pit must print its `pit: thrown` line or be a fall test. List them in the report, from `grep -ln "RideOnTo\|LastRide\|LastRideStop\|shape.Rides" src/Core/DebugTools*.cs`.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`, longest first:
  - `--test-chase --quit-after=750`, `--test-crew --quit-after=570`;
  - `--test-pit`, `=wrong`, `=lone`, `=stall`, `=late`;
  - `--test-cart-watchdog`;
  - `--test-crevasse`, `=trestle`, `=survivor`;
  - `--test-cavern`, `=bridge`;
  - `--test-boarders=groups`, `=pause`;
  - `--test-island=cavein`, `=bridge`, `=wreck`;
  - `--test-switch-back`, `--test-split`, `--test-relief`, `--test-track-view`, `--test-vault-puzzle`.

  Each must PASS. Then run plain `scripts/net_test.sh`, alone: `NET TEST: ok`.
- [ ] **Step 5: Hand the RIDE pass to the controller.** Stop and report that the repairs are in, with the expected lines above.
  - The controller runs `RIDE=1 scripts/net_test.sh` alone, under `.superpowers/sdd/shared/RIDE.lock`, on a profile other than `low-power`.
  - Expected: `NET TEST: ok`, with plan 14's lines, this task's lines and `wipe-rise watch: ok`.
  - A failure is investigated from its kept logs (`KEEP_TMP=1`), never rerun to green.
- [ ] **Step 6: Commit** (after the controller's pass)

```bash
git add data/adventures/builtin/floor_12.json src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs src/Dungeon/Rails/Core/PitRules.cs src/Dungeon/Rails/RailEncounterController.Pit.cs src/Dungeon/Rails/SetPieces/LavaPit.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.Traffic.cs src/Dungeon/Rails/RailController.Watchdog.cs src/Dungeon/Rails/RailController.Jump.cs src/Dungeon/Rails/RailController.Net.cs src/Dungeon/Rails/Core/SetPieceRules.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Core/DebugTools.cs src/Core/DebugTools.RideOn.cs src/Core/DebugTools.Pit.cs src/Core/DebugTools.Chase.cs src/Core/DebugTools.Crevasse.cs src/Core/DebugTools.CartWatchdog.cs src/Core/DebugTools.Cavern.cs src/Core/DebugTools.Boarders.Groups.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Bridge.cs src/Net/NetTestFlow.cs scripts/net_test.sh
git commit -m "feat(rails): ride 5's lava pit has a deadly switch round it: a human's lever in the seated window, called 30 m before it and 6 m before the points, the safe line with no able human aboard, too late past the points; a fixed back brings the train home past the far edge, a train meeting the lip on its own lane falls into the lava, and the watchdog steps a stuck train down without carrying it past; r5_jump becomes r5_pit, the hammers move to the old jump straight and r5_hounds moves clear of the crossing; the RIDE pass throws the pit from a remote seat and checks the end-of-line wreck by B.2; --test-pit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `src/Core/DebugTools.Crew.cs` and any other flow the Step 3 grep listed, by path.)

### Task 5: The parked train (in the way, smashed, standing again)

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/ParkedRules.cs`
  - `src/Dungeon/Rails/SetPieces/ParkedTrain.cs`
  - `src/Dungeon/Rails/RailEncounterController.Parked.cs`
  - `src/Core/DebugTools.Island.Parked.cs`
- Modify:
  - Data and plan:
    - `data/adventures/builtin/floor_12.json`: `r5_barricade` → `{ "Id": "r5_parked", "Ride": 5, "Kind": "parked", "At": … }` (Ruling 14);
    - `src/Adventure/Core/Validate/PlanValidator.cs` (the kind `parked`), `src/Adventure/Core/Plans/FloorPlan.cs` (doc).
  - Encounters:
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (the `parked` case and its tick);
    - `src/Dungeon/Rails/RailEncounterController.Barricades.cs` (`SmashCost`, shared).
  - Rails: `src/Dungeon/Rails/TrainWagon.cs` (`OfLift`, `WireRef`), and every file the Step 3 grep finds reading a `TrainWagon` as the party's.
  - Net:
    - `src/Net/Core/NetRefs.cs` (`ParkedKind`, `Parked(piece)`, `LiftWagon(k)`), `src/Net/Core/CartNet.cs` (`CartIds.LiftBase`, `Lift`, `LiftWagon`);
    - `src/Net/NetRefResolver.cs`, `src/Net/ClientWorld.cs`, `src/Net/PropSync.cs`, `src/Net/Core/PropCheck.cs` (the parked state in the prop hash);
    - `src/Dungeon/Rails/RailNet.Seats.cs` (`TrainBody` guard).
  - Crew: `src/Adventure/Objectives/CrewRescueController.cs` (`Judge`'s guard).
  - Barks: `src/Adventure/Core/Plans/CampaignPlan.cs` (`ParkedCart`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Adventure/Core/Voice/VoiceLines.cs`.
  - Flows: `src/Core/DebugTools.Island.cs` (the variant), `src/Core/DebugTools.Island.Bridge.cs` (`=wreck`'s parked check), `src/Core/DebugTools.Barricade.cs` (r5 dropped), `src/Core/DebugTools.Chase.cs` (ride 5's smash), `src/Core/DebugTools.cs` (the island line).
- Test: `tests/Dungeon/Rails/ParkedRulesTests.cs`, `tests/Adventure/RailEncounterPlanTests.cs`, `tests/Adventure/RideBarksTests.cs`, `tests/Net/NetRefsTests.cs`

**Rulings this task needs:**
- **11 (what it leaves).** A wreck past the bridge's start uncouples the party's train (hidden, not solid), hides the cart and sets `LastWreckInside`. Nothing of the party's train stands in the parked place. Cost: none.
- **14 (this task's part).** `r5_parked`'s rear is `ParkedRules.RearS(ExitFromS, 4)`, so its front is 1.0 m short of the bridge. Its At is `(S − StopS[4]) / (StopS[5] − StopS[4])` to 4 decimals, from the measured rear. Cost: none.
- **16.** `r5_barricade` becomes `r5_parked` in its own entry, at plan index 15. Cost: none.
- **23. The parked train's bodies.**
  - The parked train is a `ParkedTrain` node: a `MineCart.AsChaser()` cart (`CartId = CartIds.Lift`) and four `TrainWagon` tubs (Numbers 1..4, `OfLift = true`, `WireRef = NetRefs.LiftWagon(k)`, `SetCoupled(true)` so they show). They are never in `RailController.Wagons`.
  - Every reading of a `TrainWagon` that means the party's train is guarded with `RailController.WagonAt(w.Number) == w`, as `CrewRescueController.cs:842` and `RailController.cs:205` already do. That covers `CrewRescueController.Judge` (`npc.Ride is TrainWagon { Coupled: true }`), `RailNet.Seats.TrainBody`, the leap targets, and whatever `grep -rn "TrainWagon" src` finds.
  - It is solid (layer 1|8) while it stands and has no collision while gone.
  - Cost: none.
- **24. The smash.**
  - It costs the barricade's, through one helper (`RailEncounterController.SmashCost`) that `r1_barricade`, `r2_barricade` and the parked train all call: `SmashDent` 25 plating, 80% of the speed kept, and 15% × lethality/3 of max HP to each seated hero who is up anywhere in the train (never below 1 HP). Crew and escorts are jolted only.
  - Riders aboard are thrown clear first, each to its own `CrevasseRules.SetDownNear` cell on the outer bank (never a drop).
  - The five tubs tumble as the cart's `Tumble` does (layer 0) and go after `ParkedRules.TumbleSeconds` (6 s).
  - Cost: none.
- **25. The parked train's states.**
  - The states are `enum ParkedState { InTheWay, Gone, Lift }`, stepped by `ParkedRules.Next(state, smashedNow, liftDue, takenOver, chaseOnLine)`, in this order:
    1. `takenOver` → Gone. `takenOver` is a latch: it sets at the takeover's press and holds until the party's train arrives at the last stop, or until a `Restore`. While it is set, `Next` returns Gone, overriding rule 2 (the lift standing once the train's rear passes the far edge);
    2. `liftDue && !chaseOnLine` → Lift;
    3. InTheWay and `smashedNow` → Gone;
    4. otherwise the state stays.
  - Ride 5's arming and every `Restored` event set InTheWay.
  - **A lift is due** when the party train's rear is past the bridge's far edge, the train is held at the last stop, the party has no live cart (wrecked and hidden), or `LastWreckInside`.
  - **`chaseOnLine`** is a chaser or relief body standing on the line (`RailController.Chaser != null`, until it is hidden).
  - The parked train is not saved.
  - Cost: a lift may stand a few seconds late behind a chaser.
- **30 (this task's part).** `CartIds.LiftBase = 2100`, `CartIds.Lift = 2100`, `CartIds.LiftWagon(k) = 2100 + k`, and `NetRefs.LiftWagon(k)` = `liftwagon:{k}`. `PropEvt parked:{piece}` says "standing", "smashed" and "gone". The prop hash includes the parked state. Cost: none.
- **39 (this task's part).** `BarkTrigger.ParkedCart` is appended after `PitSwitchNow`, Shout, with subtitle-only banks (3 per roster name, Sable's a woman's; 2 per class; 3 generic). The log with nobody to speak is "Cart on the line — brace!". Cost: none.
- **41 (this task's part).** `--test-island=parked` lives in `DebugTools.Island.Parked.cs`, under `--test-island`'s conventions (solo, lethality 3, every enemy culled, `[island]` lines, `RESULT ok ({variant})`, `Shape(rc)` for every stop and id, `_quitAfter = 300`). Cost: none.

**Interfaces:**
- Consumes: Task 3's `ExitFromS`/`ExitToS`, `LastWreckInside`, `IslandSetDown` and `--test-island=wreck`; Task 4's `ThrowPitOnTheWay`; `BarricadeRules`; plan 14's `TrainRules.Length`, `TrainWagon.SetCoupled` and `RailController.WagonAt`; plan 11's heap alarm (`SetPieceRules.HeapAlarm`) and its bell.
- Produces:
  - **`ParkedRules`** (engine-free):
    - the numbers: `FrontShort = 1f`, `CallAt = 35f`, `AtSlack = 2f`, `TumbleSeconds = 6f`, `SmashDent`, `SpeedKept` (= `BarricadeRules`');
    - `float FrontS(float bridgeFromS)`, `float CartS(float bridgeFromS)`, `float RearS(float bridgeFromS, int wagons)` (rear face);
    - `float BackS(float bridgeFromS, int wagons)`: the rear face − `CartRules.HalfLength`, a centre S in `SpareRules.TrainBackS`'s sense;
    - `bool Smashes(float prevFront, float front, float rearFaceS)`, `bool CallDue(bool rolling, float front, float rearFaceS, bool called)`;
    - `bool LiftDue(float partyRearS, float bridgeToS, bool heldAtLast, bool liveCart, bool wreckedInside)`;
    - `ParkedState Next(ParkedState state, bool smashedNow, bool liftDue, bool takenOver, bool chaseOnLine)` (Ruling 25; `takenOver` is the caller's latch, and Gone wins while it is set);
    - `string Prompt(ParkedState state, bool chaseOnLine, bool onFootPlayer, bool inReach)`: "Board the cart" only in Lift with no chase on the line, for an on-foot player in reach; else "".
  - **`ParkedTrain : Node3D`** (Ruling 23; every role):
    - `MineCart Cart` and `IReadOnlyList<TrainWagon> Wagons`;
    - `Pose(float cartS, TrackSampler party)`: the wagons at cart S − 2.6·k;
    - `Stand()`, `Smash(Vector3 velocity)` (five tumbles, then hidden with no collision) and `Hide()` (no tumble);
    - `bool Standing`, `RearLantern`, `Alarm` (blink, as `OreHeap.Alarm`).
  - **`TrainWagon`:** `bool OfLift { get; init; }`, `string? WireRef { get; init; }` (`NetRef` reads it when set).
  - **`RailEncounterController`** (`.Parked.cs`):
    - `BuildParked(Node3D root, Piece piece)`. It is live when `|piece.S − RearS| ≤ AtSlack`, and otherwise a dead marker with an ERROR line. It prints `[rails] parked {id}: rear S={rear:0.0}, cart S={cart:0.0}, front S={front:0.0} (1.0 m short of the exit bridge at {from:0.0}), a cart and 4 wagons`.
    - `ParkedState ParkedNow` (every role; a client's from `PropEvt`). This task passes `takenOver: false`; Task 6 supplies it.
    - The server's tick, while ride 5 is armed and the parked train is InTheWay:
      - `CallDue` → `RailCalls.Say(…, BarkTrigger.ParkedCart, "Cart on the line — brace!")`, printed `[rails] parked {id} called at front S={f:0.0} ({d:0.0} m to its rear)`;
      - its alarm blinks by `HeapAlarm`, with `cart_bell` at its start;
      - `Smashes` → Ruling 24, printed `[rails] the cart smashes the parked train at S={f:0.0}: plating {a:0} -> {b:0}, {n} hero(es) hurt, V {v0:0.0} -> {v1:0.0}`, then `PropEvt(NetRefs.Parked(i), "smashed")`.
    - Every tick it applies `Next`. Entering Lift logs "Another cart rolls out onto the line by the lava." and sends "standing".
    - `internal int ParkedSmashes`; `internal void DebugSeatParked(Adventurer a)` (a test hook).
  - **`RailEncounterController.SmashCost(string what, bool partyCart)`** (Ruling 24).
  - **The client** applies `PropEvt parked:{i}`. The catch-up sends its state, and `PropCheck` hashes it.
  - **`--test-island=parked`.** `rc.Restore(Shape(rc).LastRideStop)`, everyone boards, and the pit is thrown on the way. It fails unless:
    - **The build.** The rear lies at `RearS(ExitFromS, 4)` ± 0.1, and a layer-1 ray from the seated lead's eye at rear − 15 m to its lantern is clear.
    - **In the way.** At stop 4, held, the lead teleported beside it is offered "" and never "Board the cart".
    - **The call.** The call (bark or log) comes with the front 33–35 m from the rear, the alarm turns on, and the bell rings.
    - **The race.** A companion seated in its cart by `DebugSeatParked` before the smash is thrown clear onto a walkable non-drop cell, HP ≥ 1, never fallen.
    - **The smash.** The plating drops by 25, and each other seated up hero loses 15% of max HP (one set to 5% beforehand ends with ≥ 1 HP). The crew's HP is unchanged, V is × 0.8 ± 0.05, five tumbles show, and `ParkedNow` is Gone.
    - **Standing again.** Within 1 s of the train's rear passing `ExitToS`, `ParkedNow` is Lift and "Another cart rolls out…" is logged.
    - **A restore.** `rc.Restore(Shape(rc).LastRideStop)` stands it InTheWay again.
  - **`--test-island=wreck`** adds: 1 s after the wreck `ParkedNow` is Lift, and no party body (cart or wagon) is shown within its span.
  - **The other flows.** `--test-barricade` drops `r5_barricade` (r1, r2 and the slag one remain). `--test-chase`'s ride-5 smash check reads the parked train (`ParkedSmashes == 1`, the call stamped `parked`). `--print-map`'s island line adds `, the parked train rear S={r:0.0} .. front S={f:0.0}`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheParkedTrain_StandsAMetreShortOfTheBridge_TwelvePointSixLong()
{
    Assert.Equal(1182.9f, ParkedRules.FrontS(bridgeFromS: 1183.9f), 3);
    Assert.Equal(1181.8f, ParkedRules.CartS(1183.9f), 3);
    Assert.Equal(1170.3f, ParkedRules.RearS(1183.9f, wagons: 4), 3);
    Assert.Equal(1169.2f, ParkedRules.BackS(1183.9f, wagons: 4), 3);
}

[Fact] public void ItIsInTheWay_ThenGone_ThenALift()
{
    Assert.Equal(ParkedState.InTheWay, ParkedRules.Next(ParkedState.InTheWay, smashedNow: false, liftDue: false, takenOver: false, chaseOnLine: false));
    Assert.Equal(ParkedState.Gone, ParkedRules.Next(ParkedState.InTheWay, smashedNow: true, false, false, false));
    Assert.Equal(ParkedState.Gone, ParkedRules.Next(ParkedState.Gone, false, liftDue: false, false, false));
    Assert.Equal(ParkedState.Lift, ParkedRules.Next(ParkedState.Gone, false, liftDue: true, false, false));
    Assert.Equal(ParkedState.Gone, ParkedRules.Next(ParkedState.Gone, false, liftDue: true, false, chaseOnLine: true)); // a chaser on the line: not yet
    Assert.Equal(ParkedState.Gone, ParkedRules.Next(ParkedState.Lift, false, liftDue: false, takenOver: true, false));  // taken over: never over the party's new train
    Assert.Equal(ParkedState.Gone, ParkedRules.Next(ParkedState.Gone, false, liftDue: true, takenOver: true, false));   // latched: the rear past the far edge still gives Gone
    Assert.Equal(ParkedState.Lift, ParkedRules.Next(ParkedState.InTheWay, false, liftDue: true, false, false));
}

[Fact] public void ALiftIsDue_PastTheBridge_HeldAtTheEnd_WithNoLiveCart_OrWreckedInside()
{
    Assert.True(ParkedRules.LiftDue(partyRearS: 1200.0f, bridgeToS: 1199.9f, heldAtLast: false, liveCart: true, wreckedInside: false));
    Assert.False(ParkedRules.LiftDue(1199.9f, 1199.9f, false, true, false));
    Assert.True(ParkedRules.LiftDue(1100f, 1199.9f, heldAtLast: true, true, false));
    Assert.True(ParkedRules.LiftDue(1100f, 1199.9f, false, liveCart: false, false));
    Assert.True(ParkedRules.LiftDue(1100f, 1199.9f, false, true, wreckedInside: true));
}

[Fact] public void TheSmash_IsTheBarricadesCost_AsTheFrontReachesTheRear()
{
    Assert.True(ParkedRules.Smashes(prevFront: 1170.2f, front: 1170.4f, rearFaceS: 1170.3f));
    Assert.False(ParkedRules.Smashes(1170.3f, 1170.5f, 1170.3f));
    Assert.Equal((BarricadeRules.SmashDent, BarricadeRules.SpeedKept), (ParkedRules.SmashDent, ParkedRules.SpeedKept));
}

[Fact] public void ItIsCalledAt35m_AndOffersNoBoardingUnlessALift()
{
    Assert.True(ParkedRules.CallDue(rolling: true, front: 1135.3f, rearFaceS: 1170.3f, called: false));
    Assert.False(ParkedRules.CallDue(true, 1135.2f, 1170.3f, false));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.InTheWay, chaseOnLine: false, onFootPlayer: true, inReach: true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Gone, false, true, true));
}
```

  Then update the other tests:
  - **`RailEncounterPlanTests`.** `r5_parked` stands in `r5_barricade`'s place, and the count stays 23. The ride-5 order is final: `switch_out < pit < switch_back < pour < trestle < hammers < parked < spur < buffer`, with the encounters `switch_back < r5_hounds < r5_golem`. The barricades are r1 and r2 only.
  - **`RideBarksTests`.** `ParkedCart` is appended.
  - **`NetRefsTests`.** It gains `(NetRefs.Parked(15), "parked", "15")` and `(NetRefs.LiftWagon(2), "liftwagon", "2")`.
- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~ParkedRulesTests|FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~RideBarksTests|FullyQualifiedName~NetRefsTests"`. Expected: a compile error.
- [ ] **Step 3: Implement, guard and place.**
  - Implement the Interfaces.
  - Run `grep -rn "TrainWagon" src --include=*.cs`, and guard every hit that means the party's train (Ruling 23). The task report lists each hit with "guarded" or why not.
  - Run the measurement command (Global Constraints). Read the bridge's start from its `[map] rail island:` line and write `r5_parked`'s At from `RearS`.
  - Run the measurement command again. The parked piece's S must be the rear ± 0.1, and every other piece S unchanged.
  - **Stop and report** if not, or if the 15 m sight ray is blocked.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`:
  - `--test-chase --quit-after=750`;
  - `--test-island=parked`, `=bridge`, `=wreck`;
  - `--test-barricade`, `--test-pit`, `--test-crew --quit-after=570`, `--test-boarders=groups`.

  Each must PASS. Then run plain `scripts/net_test.sh`: `NET TEST: ok`.

  The RIDE pass's checks still hold: its chaser still runs to the train in this task, and Task 6 halts it behind the parked place.
- [ ] **Step 5: Commit**

```bash
git add data/adventures/builtin/floor_12.json src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs src/Dungeon/Rails/Core/ParkedRules.cs src/Dungeon/Rails/SetPieces/ParkedTrain.cs src/Dungeon/Rails/RailEncounterController.Parked.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Barricades.cs src/Dungeon/Rails/TrainWagon.cs src/Dungeon/Rails/RailNet.Seats.cs src/Adventure/Objectives/CrewRescueController.cs src/Net/Core/NetRefs.cs src/Net/Core/CartNet.cs src/Net/NetRefResolver.cs src/Net/ClientWorld.cs src/Net/PropSync.cs src/Net/Core/PropCheck.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Core/DebugTools.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Parked.cs src/Core/DebugTools.Island.Bridge.cs src/Core/DebugTools.Barricade.cs src/Core/DebugTools.Chase.cs
git commit -m "feat(rails): a parked train stands a metre short of the exit bridge (r5_parked in r5_barricade's place): called at 35 m with its lantern and bell, smashed at a barricade's cost (riders thrown clear onto the bank, five tubs tumbling), and standing again as a lift once the train is past the bridge, at the end, or without a cart, never while a chaser is on the line; its tubs are never taken for the party's wagons; PropEvt parked standing/smashed/gone

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add each other file the guard step changed, by path.)

### Task 6: The lift (in for anyone left outside; the takeover with no cart left)

**Files:**
- Create: `src/Dungeon/Rails/RailController.Lift.cs`, `src/Core/DebugTools.Island.Lift.cs`
- Modify:
  - Rails:
    - `src/Dungeon/Rails/Core/ParkedRules.cs` (the lift's limit, takeover and save pose);
    - `src/Dungeon/Rails/Core/SpareRules.cs` (the four-argument `ChaseLimit`);
    - `src/Dungeon/Rails/Core/TrainRules.cs` (`GateClear`), `src/Dungeon/Rails/Core/CageHoldRules.cs` (`Held`'s `standingPast`);
    - `src/Dungeon/Rails/RailController.Chase.cs` (`Chase.Lift`; the limit and `ChaseDone` at the parked place);
    - `src/Dungeon/Rails/RailController.Relief.cs` (the limit);
    - `src/Dungeon/Rails/RailController.cs` (`TakeOverAt`, `StandingPast`; the lift's riders held over the bridge; `CageHeld`);
    - `src/Dungeon/Rails/RailController.Lanes.cs` (`GateClear`);
    - `src/Dungeon/Rails/SetPieces/ParkedTrain.cs` (the lift's prompt and use on all five bodies);
    - `src/Dungeon/Rails/RailEncounterController.Parked.cs` (the real `takenOver`; `ArmFrom`);
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (a departure armed from S).
  - Net:
    - `src/Dungeon/Rails/RailNet.Carts.cs` (the client drives its `ParkedTrain`);
    - `src/Dungeon/Rails/RailNet.Seats.cs` (the lift and its wagons heard and resolved, ahead of the chaser range);
    - `src/Dungeon/Rails/RailNet.cs` (the `L` book; the lift's `CartStateDto`);
    - `src/Net/ServerWorld.cs` (the lift's use passes the reach check).
  - Saves and crew: `src/Party/PartyController.cs`, `src/Save/FloorStateCapture.cs` (`SavePose`), `src/Adventure/Objectives/CrewRescueController.cs` (crew on foot seated in the lift's wagons).
  - Flows: `src/Core/DebugTools.Island.cs` (the variants), `src/Core/DebugTools.Island.Bridge.cs` (`=wreck`'s lift leg).
  - RIDE: `src/Net/NetTestFlow.cs`, `scripts/net_test.sh` (step 8 rewritten).
- Test: `tests/Dungeon/Rails/ParkedRulesTests.cs`, `tests/Dungeon/Rails/SpareRulesTests.cs`, `tests/Dungeon/Rails/TrainRulesTests.cs`, `tests/Dungeon/Rails/CageHoldRulesTests.cs`

**Rulings this task needs:**
- **11.** A wreck past the bridge's start sets `LastWreckInside` and `StopIndex` to the last stop, and leaves no party body standing. Cost: none here.
- **12.** Every set-down through `ExitCellNear` (the lift's and the chaser's included) passes `ExitBridgeRules.SafeExitCell`, so nothing lands on a drop. Cost: none here.
- **22 (this task's part).** This task changes the chaser's halt, so it rewrites the RIDE pass's step 8 in the same task: no check is left that the code contradicts. The controller runs RIDE at Task 8. Cost: none.
- **23.** The lift's tubs are `TrainWagon`s with `OfLift`, and every party-train reading is guarded (Task 5). Cost: none.
- **25.** `ParkedRules.Next(state, smashedNow, liftDue, takenOver, chaseOnLine)`, with `takenOver` → Gone, and the lift never standing while a chaser or relief body is on the line.
  - `takenOver` is a latch: it sets at the takeover's press and holds until the party's train arrives at the last stop, or until a `Restore`. While it is set, `Next` returns Gone, overriding the lift standing once the train's rear passes the far edge.
  - This task supplies the real latch (`RailEncounterController.TakenOver`, set by `TakeOverAt`, cleared on the last stop's `Arrived` and on `Restored`).
  - Cost: none.
- **26. The lift is a chase.** It is a `Chase` with `Lift = true`, so one chase runs at a time. Boarding (the prompt, `BoardLift` and a remote's intent) is refused while a chaser or relief body is on the line, and `BoardLift` returns false and seats nobody then. Cost: a player at the lift waits for a chaser's riders to step out.
- **27. The takeover.**
  - It happens at the first board while `ParkedRules.TakesOver(partyLiveCart, wreckedInside)` (no live cart, and no wreck inside).
  - The lift hides (Gone, no tumble, `PropEvt "gone"`). The party's cart with four fresh wagons (`CoupleFresh("the lift takes over")`) stands Held at the lift's cart S with full plating, and the presser is seated.
  - `StopIndex` becomes `LineShape.LastRideStop`, whatever stop the wreck left, and `StandingPast` is set. While `StandingPast`:
    - `GateClear` is true and `CageHeld` is false, so there is no hold press and no stop is added to `CrewLeftAt` there;
    - `TrainRules.BoardsHere` is true, so crew on foot board as at a held stop.
  - `StandingPast` is cleared at the departure and by `Restore`.
  - The departure arms `LastRide` from that S, with every piece and encounter short of it counted as passed (`ArmFrom`).
  - A save keeps `RailStop` at `LastRideStop`, so a load stands the cart at stop 4. The unopened cages resolve at the end (Task 7).
  - Cost: none.
- **28. Boarding the lift.**
  - Every one of the lift's five bodies offers "Board the cart" by `ParkedRules.Prompt`, and a press on any of them (`cart` or `liftwagon:{k}`) is the same board.
  - It seats the player, their own companions within 25 m, escorts within 25 m on its bench, and freed crew on foot within 25 m at once in its wagons (`TrainRules.SeatOrder`). Nobody walks to it.
  - It counts down 5 s, with Get out closed for its first 2 s, and rolls only with a player aboard.
  - Cost: no walk to the lift.
- **29. A chaser's and a relief's limit on ride 5.**
  - With a live `r5_parked`, `SpareRules.ChaseLimit(trainBackS, nextStopS, parkedBackS, bridgeFromS)` is the nearest of:
    - today's two-argument limit;
    - `parkedBackS − ChaseGap`;
    - `bridgeFromS − HalfLength − BridgeClear` (0.5 m).

    NaN arguments are ignored. Every argument and the result is a centre S in `TrainBackS`'s sense (the last body's back face − `HalfLength`).
  - `parkedBackS` is `ParkedRules.BackS(ExitFromS, 4)`, whether or not the parked train stands. So a chaser taken as the train leaves stop 4 waits behind the parked place, outside.
  - `ChaseDone` also fires there once the party's train is held at the last stop (or `LastWreckInside`): its riders step out beside the lane, the chaser hides 2 s later, the lift stands, and they board it.
  - Cost: a chaser never reaches the island; its riders take the lift.
- **30. The lift on the wire.**
  - The ids are Task 5's: the lift is 2100 and its wagons 2101..2104.
  - Its `CartStateDto` has `Kind` Chaser, `StopIndex` −1, `Train` [1, 2, 3, 4] and `CrewWagons` 4, and its seats are `SeatMsg`s under those ids.
  - A client resolves `CartIds.Lift..LiftWagon(4)` before the chaser range and follows the lift with its own `ParkedTrain`.
  - The RIDE book gains `L`.
  - Cost: none.
- **31. A lift rider's saved pose.** A save replaces the pose of a hero or NPC seated in the lift with `RailController.SavePose`'s spot: beside the parked train outside, or on the island's set-down spots once the lift's rear is past the far edge. Cost: a lift ride cut short by a load is ridden again.
- **35 (this task's part).** Companion heroes keep the Regroup teleport unchanged (`Adventurer.cs`'s `ActionKind.Regroup`). It is the "nobody stranded" rule for AI heroes and is pinned in `=lift`. Cost: a companion left outside appears beside its lead.
- **41 (this task's part).** `--test-island=lift` and `=takeover` live in `DebugTools.Island.Lift.cs`, under `--test-island`'s conventions (`Shape(rc)` for every stop and id, `_quitAfter = 300`). Cost: none.
- **46.** No solo flow drives a relief up to the parked place, since a relief is sent only while the line is live. Its halt is pinned engine-free (`ChaseLimit`), and the online chaser's by the RIDE pass. Cost: the relief's halt there is not run end to end.

**Interfaces:**
- Consumes:
  - Task 5's `ParkedTrain`, `ParkedNow`, `ParkedRules`, the ids and the `OfLift` guards;
  - Task 3's `IslandSetDown`, `ExitBridgeRules.Holds`, `SafeExitCell` and `LastWreckInside`;
  - the chaser (`Chase`, `CartRun`, `Boarding`, `SpareRules.HideSeconds`, `ChaserPrompt`);
  - plan 14's `CoupleFresh`, `TrainRules.SeatOrder`/`BoardReach`/`BoardsHere` and `CageHoldRules.Held`.
- Produces:
  - **`ParkedRules`:**
    - `float LiftLimit(float trainBackS, float bufferFaceS, float lastStopS)`: a centre S, the nearest of the party train's `TrainBackS` − `ChaseGap` (whenever the party has a live cart ahead, held or not; NaN otherwise), the standing buffer's face − `HalfLength`, and the last stop;
    - `bool TakesOver(bool partyLiveCart, bool wreckedInside)`;
    - `float SavePoseS(float liftCartS, float liftRearS, float bridgeToS, float parkedCartS)`.
  - **`SpareRules.ChaseLimit(float trainBackS, float nextStopS, float parkedBackS, float bridgeFromS)`** (Ruling 29), with `BridgeClear = 0.5f`.
  - **`TrainRules.GateClear(bool halted, bool standingPast, bool gateUp)`**, and **`CageHoldRules.Held(…, bool standingPast)`**: the existing six arguments plus `standingPast`, false while it holds. `RailController.GateClear` and `CageHeld` read them.
  - **`RailController`** (`.Lift.cs`):
    - `MineCart? Lift` (the lift's cart while it runs, server and solo); `bool StandingPast { get; private set; }`.
    - `bool BoardLift(Adventurer player)` (Rulings 26–28). It prints:
      - `[rails] lift: {name} boards ({n} companion(s), {c} crew, {e} escort(s))`;
      - `[rails] lift: the party has no live cart: it takes over at S={s:0.0}`;
      - `[rails] lift: refused ({why})`.
    - **The run.** As the chaser's, with no pumping and no set piece judged, and its limit `LiftLimit`.
      - At the halt it prints `[rails] lift: halted at S={s:0.0}, {d:0.0} m behind the train ahead; {n} rider(s) set down inside`, sets its riders down on `IslandSetDown` spots, and is hidden `SpareRules.HideSeconds` later.
      - While any of its bodies is over the span (`ExitBridgeRules.Holds`), its riders get no Get out.
    - `void TakeOverAt(float cartS, Adventurer presser)` (Ruling 27) prints `[rails] lift: the party's cart stands at S={s:0.0}, past stop {k}; no gate or cage holds it`.
    - `Vector3 SavePose(ICartRider r, Vector3 at)` (Ruling 31). `PartyController`'s and `FloorStateCapture`'s pose captures read it.
    - test hooks (server and solo): `internal bool DebugBoardLift(Adventurer a)` (a press as a remote's would be); `internal void DebugSpendSpares()` (every spare marked used, as a save with them spent loads); `internal void DebugPlaceChaser(int stop)` (that stop's chaser placed empty on the line as a take would, solo too) and `internal void DebugEndChase()` (`EndChase(atOnce: true)`, then hidden at once).
  - **`bool RailEncounterController.TakenOver { get; private set; }`**: the latch of Ruling 25 (set by `TakeOverAt`, cleared on the last stop's `Arrived` and on `Restored`), passed to `ParkedRules.Next`.
  - **`RailEncounterController.ArmFrom(int ride, float s)`** prints `[rails] ride {r} armed from S={s:0.0}: {p} piece(s) and {e} encounter(s) short of it passed`. A passed `parked` piece does not stand.
  - **The chaser and the relief** read the four-argument `ChaseLimit`. The server's line at the parked place reads `chase-done: the chaser halted {d:0.0} m behind the parked train's place at S={s:0.0}, outside the island; it hopped {n} hole(s) in the line`.
  - **`--test-island=lift`.** The party rides to stop 5 (the pit thrown on the way). The lead and one companion are teleported beside the parked train, a second companion 40 m short of it, and one freed crew member is put on foot within 20 m of it (`ctrl.DebugPutOnFoot`). It fails unless:
    - **The prompt.** "Board the cart" is offered by the lift's W4 and its cart alike.
    - **The boarding.** The press seats the lead, hops the near companion in, and seats the crew member in W1 seat 4 at once. That crew member is not Home while it sits in the lift.
    - **The countdown.** It runs 5 s, with Get out closed for 2 s.
    - **The bridge.** While the lift is over the span, the prompt is "" and a forced Get out does nothing.
    - **The save** (Review Focus 4). A save at mid-bridge and a load leave the lead beside the parked train outside, up and not fallen, and the lift standing. The lead boards again.
    - **The halt.** The lift halts 1.5 ± 1.5 m behind the held train's last body, inside, and every rider is set down on island cells. It is hidden 2 s later and standing again 1 s after that.
    - **Regroup.** The far companion stands on an island cell where `SetDownOk` holds within 10 s of the lead's set-down.
  - **`--test-island=takeover`** (Review Focus 1).
    - Setup: `rc.Restore(2)`, then `rc.DebugSpendSpares()`, and the train departs. The cart is wrecked on ride 3 (`rc.DebugWreck()`: "No carts left. The rest is on foot."). The party is teleported beside the parked train. The flow's cull spares stop 4's holders (`col_stop4`), and its cage stays locked.
    - It fails unless:
      - **Refused while a chaser stands.** With `rc.DebugPlaceChaser(Shape(rc).LastRideStop)`, `DebugBoardLift(companion)` returns false, prints `lift: refused (a chaser is on the line)` and seats nobody; then `rc.DebugEndChase()`.
      - **The double press** (Review Focus 5). The lead's press and `DebugBoardLift(companion)` land in one tick. Exactly one takeover is printed, both sit in the party's cart, and nobody is seated in the lift.
      - **The new cart.** The party's cart stands Held at the lift's cart S with W1..W4 coupled fresh, `StopIndex` = `LastRideStop` and `StandingPast` set. `ParkedNow` is Gone from the press to the arrival at stop 5, through the train's rear passing the far edge (the latch holds), and `TakenOver` clears on the arrival.
      - **The countdown.** It starts within 1 s of the lead sitting, though stop 4's gate is down and its cage locked, and no stop is added to `CrewLeftAt`.
      - **The departure.** It prints `armed from S=…`, with the golem, the hounds and the pit passed.
      - **The arrival.** The train crosses the bridge and arrives at stop 5 as the party's train.
  - **`--test-island=wreck`** adds the wreck-inside leg (Ruling 27's `wreckedInside`): the lead, teleported outside after the wreck, boards the lift. There is no takeover; it rides in and is set down inside.
  - **The RIDE pass's step 8** (Ruling 22).
    - **The chaser.** The joiner takes `spare:4` as today. Its chaser hops at least one hole (the pit, or the fallen trestle) and halts within 3 m of the parked train's place, outside: `chase-done: … behind the parked train's place … outside the island … hopped [1-9] hole(s)`. Its riders step out.
    - **The lift.** The joiner presses "Board the cart" through `InteractIntent(parked:{i})`. The lift halts within 3 m of the held train's last body, inside: `[net-server] ride: lift halted …; 1 rider(s) set down inside`. The joiner prints `[net] ride: the lift set us down inside the island at {cell}`.
    - "our chaser halted … behind the train's last body here" becomes "… behind the parked train's place here".

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheLift_OffersBoarding_OnlyWithNoChaseOnTheLine()
{
    Assert.Equal("Board the cart", ParkedRules.Prompt(ParkedState.Lift, chaseOnLine: false, onFootPlayer: true, inReach: true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Lift, chaseOnLine: true, true, true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Lift, false, onFootPlayer: false, true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Lift, false, true, inReach: false));
}

[Fact] public void TheLift_HaltsBehindTheTrainAhead_OrShortOfTheBuffer_OrAtTheLastStop()
{
    Assert.Equal(1217.6f, ParkedRules.LiftLimit(trainBackS: 1219.1f, bufferFaceS: float.NaN, lastStopS: 1231.7f), 3); // a train held at stop 5: its rear face at 1220.2
    Assert.Equal(1220.2f, ParkedRules.LiftLimit(float.NaN, bufferFaceS: 1221.3f, 1231.7f), 3);
    Assert.Equal(1231.7f, ParkedRules.LiftLimit(float.NaN, float.NaN, 1231.7f), 3);
}

[Fact] public void TheLift_TakesOver_OnlyWithNoLiveCart_AndNoWreckInside()
{
    Assert.True(ParkedRules.TakesOver(partyLiveCart: false, wreckedInside: false));
    Assert.False(ParkedRules.TakesOver(true, false));
    Assert.False(ParkedRules.TakesOver(false, wreckedInside: true));
}

[Fact] public void StandingPastItsStop_NoGateOrCageHoldsTheCart()
{
    Assert.True(TrainRules.GateClear(halted: false, standingPast: true, gateUp: false));
    Assert.False(TrainRules.GateClear(false, standingPast: false, gateUp: false));
    Assert.True(TrainRules.GateClear(false, false, gateUp: true));
    Assert.True(TrainRules.BoardsHere(held: true, halted: false, lastStop: false));
}

[Fact] public void ALiftRidersSavedPose_IsNeverOverTheBridge()
{
    Assert.Equal(1181.8f, ParkedRules.SavePoseS(liftCartS: 1195f, liftRearS: 1183.5f, bridgeToS: 1199.9f, parkedCartS: 1181.8f), 3);
    Assert.Equal(1214f, ParkedRules.SavePoseS(1214f, liftRearS: 1202.5f, 1199.9f, 1181.8f), 3);
}

[Fact] public void AChaser_HaltsBehindTheParkedPlace_AndNeverOnTheBridge()
{
    Assert.Equal(1167.7f, SpareRules.ChaseLimit(trainBackS: 1219.1f, nextStopS: 1231.7f, parkedBackS: 1169.2f, bridgeFromS: 1183.9f), 3);
    Assert.Equal(1182.3f, SpareRules.ChaseLimit(1219.1f, 1231.7f, parkedBackS: float.NaN, 1183.9f), 3);
    Assert.Equal(1098.5f, SpareRules.ChaseLimit(1100f, 1231.7f, 1169.2f, 1183.9f), 3);
    Assert.Equal(SpareRules.ChaseLimit(900f, 1022.5f), SpareRules.ChaseLimit(900f, 1022.5f, float.NaN, float.NaN));
}
```

  In `CageHoldRulesTests`, add `StandingPast_HoldsNothing`: `CageHoldRules.Held(CartMode.Held, false, 4, 6, false, true, standingPast: true)` is false, and the same with `standingPast: false` is true.
- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~ParkedRulesTests|FullyQualifiedName~SpareRulesTests|FullyQualifiedName~TrainRulesTests|FullyQualifiedName~CageHoldRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`:
  - `--test-chase --quit-after=750`, `--test-crew --quit-after=570`;
  - `--test-island=lift`, `=takeover`, `=parked`, `=wreck`, `=bridge`;
  - `--test-relief`, `=rails`, `=refuse`, `=ahead`;
  - `--test-crevasse=survivor`.

  Each must PASS. Then run plain `scripts/net_test.sh`: `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/RailController.Lift.cs src/Dungeon/Rails/Core/ParkedRules.cs src/Dungeon/Rails/Core/SpareRules.cs src/Dungeon/Rails/Core/TrainRules.cs src/Dungeon/Rails/Core/CageHoldRules.cs src/Dungeon/Rails/RailController.Chase.cs src/Dungeon/Rails/RailController.Relief.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailController.Lanes.cs src/Dungeon/Rails/SetPieces/ParkedTrain.cs src/Dungeon/Rails/RailEncounterController.Parked.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailNet.Carts.cs src/Dungeon/Rails/RailNet.Seats.cs src/Dungeon/Rails/RailNet.cs src/Net/ServerWorld.cs src/Party/PartyController.cs src/Save/FloorStateCapture.cs src/Adventure/Objectives/CrewRescueController.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Lift.cs src/Core/DebugTools.Island.Bridge.cs src/Net/NetTestFlow.cs scripts/net_test.sh
git commit -m "feat(rails): the parked train is a lift: Board the cart on any of its bodies seats a player left outside with their companions, escorts and crew on foot, and it runs like a chaser over the bridge (no Get out on it) to halt behind the train inside, set everyone down and go; with no live cart and no wreck inside it takes over as the party's train, standing past its stop with no gate or cage holding it; chasers and reliefs halt behind the parked place, never on the bridge; a save never leaves a rider over the lava; the RIDE pass's step 8 rides the chaser and then the lift

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: Home and lost on an island floor (and the win's reward chest)

**Files:**
- Create: `src/Adventure/Objectives/CrewRescueController.Island.cs`, `src/Core/DebugTools.Island.Home.cs`
- Modify:
  - Crew: `src/Adventure/Core/Objectives/CrewRules.cs`, `src/Adventure/Objectives/CrewRescueController.cs` (`Judge`, `Evaluate`, `Win`; `DebugDown`), `src/Adventure/Npc.cs` (the catch-up's side rule).
  - Save: `src/Save/Core/SaveDtos.cs` (`ObjectiveStateDto.RewardChest`), `src/Save/Core/RailArrival.cs`.
  - Net: `src/Net/ClientWorld.cs` (the chest's "stood"), `src/Net/PropSync.cs` (its catch-up).
  - Flows: `src/Core/DebugTools.Island.cs` (the variants), `src/Core/DebugTools.Island.Bridge.cs` (`=wreck`'s home leg).
- Test: `tests/Adventure/CrewRulesTests.cs`, `tests/Save/SaveRunFieldsTests.cs`, `tests/Save/RailArrivalTests.cs`

**Rulings this task needs:**
- **11.** A wreck past the bridge's start sets everyone down inside, sets `LastWreckInside` and takes `StopIndex` as the last stop. For the crew it counts as "the train at stop 5". Cost: none here.
- **12.** Every set-down through `ExitCellNear` (an NPC's seated-lead catch-up included) passes `SafeExitCell`, so nothing lands on a drop. Cost: none here.
- **27.** After a takeover the cart stands past its stop with `StandingPast`, and no stop is added to `CrewLeftAt` there. The unopened cages are resolved here, at the end. Cost: none here.
- **32. Home is the island.** On an island floor, `InIsland` replaces `InExitRoom`. "Seated in a coupled party wagon while held at the last stop" is unchanged, and a lift's tub is never a party wagon (Task 5's guard). Cost: none.
- **33. Unopened cages.** Once `CrewRules.UnopenedLeftBehind` holds (every up, unparked player is in the island, and the train is held at the last stop or `LastWreckInside`), each unopened cage's stop is added to `RailController.CrewLeftAt`, printed `[CrewRescue] island: cage at stop {s} left behind (never opened)`. Cost: none.
- **34. Stranded, judged live.** "Stranded" is judged by `CrewRules.JudgeOnIsland`: it counts as Lost while it holds and as Freed again once an up player stands outside. Once the win fires, the win stands. Cost: none.
- **35. The follower's catch-up.**
  - On a floor with `Rail.Island`, an `Escorting` NPC's catch-up runs only when `CrewRules.CatchUpAllowed(islandFloor, selfInIsland, leadInIsland, destInIsland)` holds: the follower, its lead and the destination all on one side of the island's edge. A body on a bridge cell counts as outside.
  - The catch-up is `Npc.cs`'s two set-downs once `PathMover.Unreachable` passes 5 s: behind a lead on foot, and `StandBesideCart` for a seated lead 30 m off.
  - Otherwise the follower holds where it is.
  - Companion heroes keep the Regroup teleport (Task 6 pins it).
  - Cost: a crew member who fell behind outside waits for the lift instead of appearing beside the party.
- **36. Grisel brought in.**
  - When `CrewRules.BringEscortIn(escortOutside, upPlayersOutside, trainAtEndOrWreckedInside)` holds, she is set down on the island's set-down spot nearest the held train's last body (or the stop's platform after a wreck inside), once each time the condition starts.
  - The log reads "Grisel picks her way in over the bridge behind you." (a log line, not a bark).
  - Cost: none.
- **37. The reward chest.**
  - It is a `Chest` with `TableId = ChestRules.VaultTable` (vault 0's table), `SpawnIndex = CrewRules.RewardChestIndex` (9000), not a mimic and not locked.
  - It stands on the exit room's first free cell in `ExitCells()` order (nearest the stairs) that is not a line cell and that no prop takes.
  - It is saved in `ObjectiveStateDto.RewardChest`, and its opened state in `OpenedChests` as any chest's.
  - On the wire it is `PropEvt(NetRefs.Chest(9000), "stood")`, and the catch-up re-sends it.
  - The log reads "The prisoners point you to a strongbox by the stairs: the Founders' share is yours."
  - Cost: none.
- **41 (this task's part).** `--test-island=onfoot|stranded|grisel|chest` live in `DebugTools.Island.Home.cs`, under `--test-island`'s conventions (`Shape(rc)` for every stop and id, gathering on). `_quitAfter` is 450 for `=onfoot` and 300 for the rest. Cost: none.
- **44.** The safe room's gathering spawns inside the island (Task 3's test). `=chest` runs it live and greps no ERROR. Cost: none.

**Interfaces:**
- Consumes: Task 3's `FloorLayout.InIsland`, `LastWreckInside` and `DebugPutOnFoot`; Task 6's lift set-down, `StandingPast` and `=takeover`'s setup; plan 7's `RailController.CrewLeftAt`; `ChestRules.VaultTable`; the chest scene and `OpenedChests`.
- Produces:
  - **`CrewRules`:**
    - `const int RewardChestIndex = 9000`;
    - `bool Stranded(bool onFoot, bool inIsland, bool trainAtEndOrNone, int upPlayersOutside)`;
    - `bool UnopenedLeftBehind(int upPlayers, int upPlayersInside, bool trainAtEndOrWreckedInside)`;
    - `bool BringEscortIn(bool escortOutside, int upPlayersOutside, bool trainAtEndOrWreckedInside)`;
    - `bool CatchUpAllowed(bool islandFloor, bool selfInIsland, bool leadInIsland, bool destInIsland)`;
    - `CrewState JudgeOnIsland(bool homeAlready, bool downed, bool inIsland, bool seatedAtEnd, bool stranded)`, judged in this order:
      1. Home when already home and (not downed, or in the island);
      2. else Downed when downed;
      3. else Home when in the island or seated at the end;
      4. else Lost when stranded;
      5. else Freed.

      The caged and the dead are judged before it, as today.
  - **`Npc`'s follow block** reads `CatchUpAllowed` before either set-down (Ruling 35). It prints `[npc] {name}: catch-up held (the lead is across the island's edge)` once each time it holds.
  - **`CrewRescueController`** (`.Island.cs`, server and solo, only on a floor with `Rail.Island`):
    - `Judge` reads `JudgeOnIsland`, with `InIsland` for `InExitRoom`; "no train" means wrecked and hidden.
    - `ResolveUnopened()` (Ruling 33) and `BringEscortsIn()` (Ruling 36, printed `[CrewRescue] island: {name} brought in beside the held train at {cell}`).
    - `Win`, on an island floor:
      - stands the reward chest (Ruling 37), printed `[CrewRescue] the reward chest stands in room {r} at {cell} (table {t})`;
      - sends `PropEvt(NetRefs.Chest(RewardChestIndex), "stood")` and logs its line in place of "Somewhere on this floor, a vault door swings wide for you.";
      - still reveals and opens room 10's door.
    - `Capture` writes `RewardChest` (true once stood, else null), and `Restore` stands the chest again.
    - `internal void DebugDown(string crewId)` (a test hook: downed where it sits or stands).
  - **`ObjectiveStateDto.RewardChest`** (`bool?`). `RailArrival.Merge` takes it from the live state only with the live state's `Completed`.
  - **A client** builds the chest on "stood" (and at the catch-up), from the same cell rule.
  - **`--test-island=onfoot`** (Review Focus 1, B.10's on-foot win). Setup: `rc.Restore(2)`; stops 1–2's cages opened and their crew seated (`ctrl.DebugSeatCrew` for stops 1 and 2); `rc.DebugSpendSpares()`; the train departs and is wrecked on ride 3 (`rc.DebugWreck()`: "No carts left. The rest is on foot.", the crew let out to follow); the party and those crew teleported beside the parked train; the flow's cull spares stop 4's holders, and stop 4's cage stays locked. The lead then presses "Board the cart". It fails unless:
    - **The ride in.** The takeover's countdown starts within 1 s of the lead sitting, with stop 4's holders alive and its cage locked. Its crew sit in W1 and W2, and the train arrives at stop 5.
    - **The cages.** Once every up player is inside, `[CrewRescue] island: cage at stop 3 left behind (never opened)` and `… stop 4 …` print, and their crew are lost.
    - **The win.** Stops 1–2's crew are home. The win fires: `jailbreak_freed_all`, the stairs unsealed, the chest stood.
  - **`--test-island=stranded`** (Review Focus 2).
    - Setup: on ride 5, before the arrival, one crew member is downed in its wagon (`ctrl.DebugDown`), which holds off the win, and another is put on foot outside beside the parked train (`ctrl.DebugPutOnFoot`). The train arrives at stop 5 with every player inside.
    - It fails unless:
      - **Stranded.** The outsider is Lost within 1 s and stays outside and Lost for 12 s, printing its `catch-up held` line and never teleported in.
      - **Live.** With the lead teleported outside it is Freed within 1 s, and with the lead back inside it is Lost again.
      - **The win stands.** Reviving the downed one fires the win, and teleporting the lead outside afterwards leaves the win standing (`Completed`).
  - **`--test-island=grisel`** (Review Focus 2).
    - Setup: Grisel is forgiven and escorting. She is left outside (teleported to ride 5's lane at S 1150), and the party rides to stop 5.
    - It fails unless:
      - **She is held.** She stays outside, never teleported, until the line logs.
      - **Brought in.** Within 1 s of every up player being inside with the train held at stop 5, she stands on an island cell beside the train's last body, and the line is logged.
      - **Her quest.** She walks to the down stairs, and `q_grisel_out` completes.
  - **`--test-island=chest`.** Every cage is opened and its crew seated by hooks, and the party rides to stop 5. It fails unless:
    - **The chest.** Within 1 s of the win, the reward chest stands in room 9 with `TableId` `table.vault`. The lead opens it, an item drops, and the line is logged.
    - **Room 10.** Its door is revealed and open.
    - **The save.** A save and a load keep the chest, opened.
    - **The gathering.** With the party in the safe room for 40 s, a group gathers inside the island and no ERROR line prints.
  - **`--test-island=wreck`** adds (Review Focus 3): the set-down crew are Home within 1 s, the unopened cages resolve through `LastWreckInside`, and the win fires.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Stranded_IsOnFootOutside_WithTheTrainAtTheEnd_AndNoPlayerOutside()
{
    Assert.True(CrewRules.Stranded(onFoot: true, inIsland: false, trainAtEndOrNone: true, upPlayersOutside: 0));
    Assert.False(CrewRules.Stranded(true, false, true, upPlayersOutside: 1));
    Assert.False(CrewRules.Stranded(true, inIsland: true, true, 0));
    Assert.False(CrewRules.Stranded(true, false, trainAtEndOrNone: false, 0));
    Assert.False(CrewRules.Stranded(onFoot: false, false, true, 0));
}

[Fact] public void Stranded_IsLive_UntilTheWin()
{
    Assert.Equal(CrewState.Lost, CrewRules.JudgeOnIsland(homeAlready: false, downed: false, inIsland: false, seatedAtEnd: false, stranded: true));
    Assert.Equal(CrewState.Freed, CrewRules.JudgeOnIsland(false, false, false, false, stranded: false)); // a player stands outside again
    Assert.Equal(CrewState.Home, CrewRules.JudgeOnIsland(false, false, inIsland: true, false, false));
    Assert.Equal(CrewState.Home, CrewRules.JudgeOnIsland(false, false, false, seatedAtEnd: true, false));
    Assert.Equal(CrewState.Downed, CrewRules.JudgeOnIsland(false, downed: true, false, seatedAtEnd: true, false)); // downed in its wagon before the arrival: not home
    Assert.Equal(CrewState.Home, CrewRules.JudgeOnIsland(homeAlready: true, downed: true, inIsland: true, false, false)); // home, then downed inside: still home
    Assert.True(CrewRules.Completed(new[] { CrewState.Home, CrewState.Lost }, cagesClosed: 0));
}

[Fact] public void TheCatchUp_NeverCrossesTheIslandEdge()
{
    Assert.True(CrewRules.CatchUpAllowed(islandFloor: false, selfInIsland: false, leadInIsland: true, destInIsland: true));
    Assert.True(CrewRules.CatchUpAllowed(true, selfInIsland: false, leadInIsland: false, destInIsland: false));
    Assert.True(CrewRules.CatchUpAllowed(true, true, true, true));
    Assert.False(CrewRules.CatchUpAllowed(true, selfInIsland: false, leadInIsland: true, destInIsland: true));
    Assert.False(CrewRules.CatchUpAllowed(true, true, leadInIsland: false, destInIsland: false));
    Assert.False(CrewRules.CatchUpAllowed(true, false, false, destInIsland: true)); // a lead on the bridge, set down past its end
}

[Fact] public void UnopenedCages_AreLeftBehind_OnceEveryUpPlayerIsInside()
{
    Assert.True(CrewRules.UnopenedLeftBehind(upPlayers: 2, upPlayersInside: 2, trainAtEndOrWreckedInside: true));
    Assert.False(CrewRules.UnopenedLeftBehind(2, 1, true));
    Assert.False(CrewRules.UnopenedLeftBehind(2, 2, false));
    Assert.False(CrewRules.UnopenedLeftBehind(0, 0, true));
}

[Fact] public void AnEscortOutside_IsBroughtIn_WithNobodyLeftOutside()
{
    Assert.True(CrewRules.BringEscortIn(escortOutside: true, upPlayersOutside: 0, trainAtEndOrWreckedInside: true));
    Assert.False(CrewRules.BringEscortIn(true, 1, true));
    Assert.False(CrewRules.BringEscortIn(true, 0, trainAtEndOrWreckedInside: false));
}
```

  Then update the other tests:
  - **`SaveRunFieldsTests.RewardChest_RoundTrip`.** It is null on a new state and on one from before the field, and true survives a save.
  - **`RailArrivalTests`.** A wipe merged with the live `Completed` false drops a live `RewardChest`.
- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~CrewRulesTests|FullyQualifiedName~SaveRunFieldsTests|FullyQualifiedName~RailArrivalTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`:
  - `--test-crew --quit-after=570`, `--test-crew=forgiven --quit-after=570`, `--test-chase --quit-after=750`;
  - `--test-island=onfoot`, `=stranded`, `=grisel`, `=chest`, `=lift`, `=takeover`, `=wreck`.

  Each must PASS. Then run plain `scripts/net_test.sh`: `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Adventure/Core/Objectives/CrewRules.cs src/Adventure/Objectives/CrewRescueController.cs src/Adventure/Objectives/CrewRescueController.Island.cs src/Adventure/Npc.cs src/Save/Core/SaveDtos.cs src/Save/Core/RailArrival.cs src/Net/ClientWorld.cs src/Net/PropSync.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Home.cs src/Core/DebugTools.Island.Bridge.cs
git commit -m "feat(crew): on an island floor the prisoners are home anywhere in the island; a follower's catch-up never crosses the island's edge; once every player is inside with the train at the end (or wrecked inside), unopened cages are left behind; crew on foot outside with nobody outside to fetch them are lost while that lasts; Grisel is brought in; the win stands a strongbox of the vault's table by the stairs (saved, sent as a PropEvt)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: The RIDE pass's island checks (the builder writes them; the controller runs them)

**Files:**
- Modify: `src/Net/NetTestFlow.cs`, `scripts/net_test.sh` (the checks and the header)

**Rulings this task needs:**
- **22 (this task's part).** Task 4 repaired the RIDE pass and Task 6 rewrote step 8. This task adds the co-op checks that remain (B.10): no remote Get out over the bridge, and the parked train's smash seen on a client. Then the controller runs the last RIDE pass of the plan. Plain `net_test.sh` stays green. Cost: none.

**Interfaces:**
- Consumes:
  - Task 3's refusal print, `get out refused (the train is on the exit bridge)` (from `ServerWorld.cs`);
  - Task 5's `PropEvt parked:{i}` and the prop hash's parked state;
  - Task 6's lift and step 8;
  - plan 14 Task 7's pass.
- Produces, each with its own `NET TEST: FAIL (…)` line:
  - **No remote Get out over the bridge.** While the first client's train has a car over the span, it sends `InteractIntent(cart:0)`. The server prints `interact cart:0 from Rider: get out refused (the train is on the exit bridge)`. The client's prompt read "" on every frame over the span, and it stays seated.
  - **The parked train's smash, seen on a client.** The server prints the smash. Each client prints `[net] ride: parked: smashed here (PropEvt) {t:0.000}, plating {p:0}` within 1 s of the server's line, with the plating equal to the server's.
  - **The props and books.** The trace list gains `prop:parked:{i}:smashed`, `…:standing` and `…:gone`. The seat books include `L`, and `[net] ride: prop hash matches the server's` holds with the parked state hashed.
- [ ] **Step 1: Add the checks** to `NetTestFlow` and `net_test.sh`, in ride order, and update the header's comment and its timings.
- [ ] **Step 2: Run plain `scripts/net_test.sh`** (alone, lock absent). Expected: `NET TEST: ok`.
- [ ] **Step 3: Hand the RIDE pass to the controller.** Stop and report that the checks are in, with the expected new lines.
  - The controller runs `RIDE=1 scripts/net_test.sh` alone, under `.superpowers/sdd/shared/RIDE.lock`, on a profile other than `low-power`.
  - Expected: `NET TEST: ok`, with every line of Tasks 4, 6 and 8, plan 14's lines and `wipe-rise watch: ok`.
  - A failure is investigated from its kept logs (`KEEP_TMP=1`), never rerun to green.
- [ ] **Step 4: Commit** (after the controller's pass)

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh
git commit -m "test(net): the RIDE pass's island checks: no remote Get out over the exit bridge, the parked train's smash seen on both clients, the parked train's props traced and hashed

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 9: Docs and the full sweep

**Files:**
- Modify: `docs/development.md`
- Modify, only to re-pin a measured value (with its measurement in the commit message): any `src/Core/DebugTools.*.cs` whose check moved.

**Rulings this task needs:** none beyond the Global Constraints. The docs state the plan's rulings as the code now has them.

**Interfaces:**
- Produces, in `docs/development.md`:
  - **Generation.**
    - The two knobs and the passes (pit, island, refusals, `[gen]` lines).
    - The plank pass picked before the cuts.
    - The rail proof replacing the on-foot stairs checks.
    - The sweep note at `:879`, which names the island floor.
    - `layout.Connections` still listing the sealed room pairs (9–1, 0–9, 8–9). This is harmless: the baked plan never reads them for a walk.
  - **Crevasses.**
    - Lava regions (the gorge's last span and the strip beside the moat included), the trench, the moat and the cave-ins' look with its per-cell seed.
    - The exit bridge (kept track, no gravel, no footing, the trestle).
    - The set-down guard.
    - The new `[map] rail`, `[map] rail pit:`, `[map] rail island:` and `[chasm]` lines with the measured numbers.
    - The probe's new `nofloorDrop` pins and why (40 samples per 4 m of drop).
  - **"The five rides."**
    - Ride 5's row with every piece and its measured S and At.
    - The S paragraph: plan 15 moved no line S, and re-placed ride 5's pieces from the measurement.
    - `:2094-2100` rewritten for the pit switch (the deadly out lever, calls, R-lone, the late press, the guard, the fixed back, the fall), and the jump kept for other floors with its lane check.
    - `:2236-2240` rewritten for the pit and the trestle in the gorge.
    - The parked train and the lift (states, smash, boarding, the takeover's standing past its stop).
  - **The watchdog.** The pit's four steps.
  - **"Stops, cages and the crew."**
    - Home on an island floor, the follower's catch-up never crossing the edge, unopened cages, stranded crew, Grisel brought in and the reward chest.
    - On an island floor `MangledRules.BoundCell` finds no stairs route, so an unrescued body stays where it fell.
  - **"Riding together."**
    - The exit bridge's hold and its refusal line.
    - The lift on the wire (ids, kind, books) and the parked train's props.
    - The chaser's halt behind the parked place.
    - The RIDE net test's new steps with the controller's measured numbers.
    - Known gaps: the relief at the lift (Ruling 46) and what the reviews leave.
  - **The flows.** The `--test-pit` and `--test-island` paragraphs.
  - Every measured number from Tasks 1–8 is written with where it came from.

- [ ] **Step 1: Write the docs** (Interfaces), checking each number against its flow's log.
- [ ] **Step 2: Run the full sweep.**
  - The full suite.
  - Through `scripts/flows.sh`, longest first:
    - `--test-chase --quit-after=750`; `--test-crew --quit-after=570`; `--test-crew=forgiven --quit-after=570`;
    - `--test-island` and its nine other variants; `--test-pit` and its four other variants;
    - `--test-boarders` and its six other variants;
    - `--test-cavern` and its seven other variants;
    - `--test-runaway=tail`, `=ram`;
    - `--test-split`, `=early`, `=hold`, `=stall`, `=lone`;
    - `--test-switch-back` and its four other variants;
    - `--test-crevasse`, `=trestle`, `=survivor`;
    - `--test-relief`, `=rails`, `=refuse`, `=ahead`;
    - `--test-barricade`, `--test-ride`, `--test-ride-edges`, `--test-cart-watchdog`, `--test-track-view`, `--test-vault-puzzle`.
  - **The probe** (lock absent): the measurement command (Global Constraints). Expected: the pins in `TrackDepthRules.Floor12NoFloorDrop`, and the total reading `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`.
  - **The generator sweep:**

    ```bash
    for N in $(seq 1 20); do echo "== floor $N"; timeout 60 godot --headless --path . -- --builtin --floor=$N --quit-after=4 --no-ambush --no-portals --no-gathering 2>&1 | grep -iE "repaired|dropped|demoted|adapted"; done
    timeout 120 godot --headless --path . -- --builtin --quit-after=4 2>&1 | grep -c adapted   # 0
    ```

    Expected: no repair line on any floor, and `adapted` 0.
  - Then plain `scripts/net_test.sh`, alone.
  - Every one must PASS with `errors=0` (or `NET TEST: ok`). A value that moved is re-pinned only from its measurement, named in the commit with its old and new values.
- [ ] **Step 3: Commit**

```bash
git add docs/development.md
git commit -m "docs: floor 12's exit island (the knobs, the rail proof, lava, cave-ins and the exit bridge), ride 5's pit switch, the parked train and the lift, home and lost on an island floor, the reward chest, the new pins and the RIDE pass's island steps

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add each re-pinned `src/Core/DebugTools.*.cs` to the `git add` by its path.)
