# Mine Cart Chase, Plan 15: the exit island, the pit switch, the parked train (floor 12, plan B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12's exit room becomes an island that only the rails reach. Lava trenches and cave-ins seal its halls, and one rail bridge with no walkway crosses the lava. Ride 5's lava pit gets a deadly switch round it. A parked train before the bridge is smashed by a party that rides, and it carries in anyone left outside.

**Architecture:**
- **The grid says "no walk".** Two `[AiIgnore]` knobs, `PitRide` and `ExitIsland`, add two passes to `CarveChasms`. Both use no rng, and both run after the gorge and before the planks.
  - `CarvePit` cuts the pit: one party-lane cell and the two cells inward of it.
  - `CarveIsland` seals the exit room and the rooms that hang off it. Each hall gets a lava trench at its mouth and a cave-in behind it. The exit bridge's lane cells become chasm with their track kept, and a two-cell moat runs beside them.
  - `RailLine.Pit` and `RailLine.Island` record both. `FloorSolvability` proves an island floor by rail instead of on foot.
- **Every role builds the runtime from the layout**, as the broken bridge does. That covers:
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
  - Crew on foot outside, with nobody outside to fetch them, are stranded and lost.
  - Grisel is brought in, and the win puts a reward chest in room 9.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-22-floor-12-long-train-and-exit-island-design.md` (4e913a9).
- This plan covers part B (B.1–B.10), "Build order", "Left to the builder" and "The rulings".
- Part A is plan 14 (`docs/superpowers/plans/2026-11-22-mine-cart-14-long-train.md`), which this plan builds on: the train, `TrainRules`, the wagon numbers, `AboardCrew`, `RailController.MovesRefused`, `ThrowClock`, `BarkTrigger.BoarderThrow`, `--test-boarders` and the hound groups.
- The spec is an addendum to `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md`, whose anti-goals still bind.

Earlier rulings: `.superpowers/sdd/mine-cart-rulings.md` (the "Long train + exit island" block) and plan 14's ledger, `.superpowers/sdd/2026-11-22-mine-cart-14-long-train/progress.md`.
- The ledger amends plan 14: answering from a seat with a shot comes first, the boarder pick has a range cap, and `IsMoveAsk`. The `BoarderThrow` telegraph reaches clients, `--test-boarders` is split into per-variant partial files, and `WagonReach` is 2.0.
- One ledger ruling binds here: "plan B moves r5_hounds (not just re-measures)".

Facts with file:line are in `.superpowers/sdd/shared/exit-island-recon.md` (A–G). In `docs/development.md`, read:
- "The five rides" (the table, ride 5);
- "Stops, cages and the crew";
- the Crevasses section (`ChasmDressing`, the gorge, the probe);
- the watchdog section;
- "Riding together" (the chaser, Props, the RIDE net test, Known gaps);
- "After any generator change, sweep the shipped floors" (`development.md:879`).

Line numbers drift, so find code by name.

**Plan 14 must be complete before plan 15 starts.** Its Tasks 1–5 are committed (87f08a4).
- Task 6 adds `r1_hounds`, `r3_hounds` and `r5_hounds` (At 0.3705, S 1100.0), along with the hounds-only cart linger, the crossing frees (`SetPieceRules.CrossingFrees`), the golem frees, the boarder range cap and `--test-boarders=groups` (`DebugTools.Boarders.Groups.cs`).
- Task 7 rewrites the RIDE pass for the long train (`RideTrainCheck`, the move, the knock-off, the throw).
- Task 8 adds the docs, the timings re-pinned at 5.22 m/s, and the full sweep.

Where a task below uses a name that plan 14's Tasks 6–8 add, its Interfaces block says so.

**Measured** (the shipped floor at 303ef27, from the recon's `--print-map`/`--track-depth`; plan 14 moves no S). Task 1 Step 1 confirms each value at plan 14's end.
- **Layout.** `LayoutHash` 16995108853750381062.
  - `[map] rail: stops=[1,2,4,6,8,9] … cavern=238..259 chasm=217 strips=8 gorge=[291..292@0.42-0.44,298..300@0.55-0.58,302..306@0.62-0.69,311..313@0.78-0.82] planks=3 break=188@0.81 run=185..191 split=227@0.38 crevasse=228..231`.
  - `[chasm] regions=12 decks=50 planks=3 cavern=44`.
- **The line.** `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`, ride 5 is 209.2 m and the party track 1243.7 m.
  - Stop 4's platform is party cells 265..271 and stop 5's is 319..326 (room 9's row y 57).
  - Ride 5 runs down x 61 on cells 274..286; the straight run is 275..285 (y 49..59). It runs along y 60 on cells 310..316 (x 55→49), with straight cells 311..315, then (49,59), (49,58), then the platform.
- **Ride 5's pieces:**
  - `r5_hammers` 1061.3/1064.3/1067.3 and `r5_pour` 1106.2 (valve 1102.7);
  - `r5_golem` 1125.0 and `r5_hounds` 1100.0 (plan 14 Task 6);
  - `r5_trestle` 1134.7–1146.7 and `r5_jump` 1156.0–1161.0;
  - `r5_spur` points 1200.3 (lever 1198.3), `r5_barricade` 1206.6 and `r5_buffer` 1221.3.
- **The probe.** `nofloorDrop` is PartyTrack 0 and EnemyTrack 40, and the five other fields read 0.
- **The rooms.**
  - Room 1 (30,53) 8x6 is the start.
  - Room 9 (42,50) 8x8 holds the down stairs (46,54) and stop 5.
  - Room 12 (54,50) 4x5 is the safe room, whose door (53,53) is reached by (50..52,53).
  - The west hall is (38..41,54). The north hall is (46,45..49), with the trap (46,49) and the plank (46,47); its fork is at (46,45).
- **The train** is 12.6 m with its cars 2.6 m apart. It cruises at 5.22 m/s unpumped with four wagons.
- **What the spec expects after the cut** (approximate; Tasks 1, 3 and 4 measure each):
  - The pit is cell 282, (61,56), with the lip at about S 1073 and the far edge at about 1077.
  - The exit bridge is party cells 311..314 (about S 1184–1199) and enemy cells (51..53,61).
  - The trenches are (41,54) and (46,49). The cave-ins are (38..40,54) and (46,46..48).
  - The moat adds (51,58), (51,59) and (51..53,62..63).
  - The parked train's rear is at about 1170.4. `nofloorDrop` is 200 / 160.

**What moves in plan B.** Every layout-dependent pin is re-measured in its task's measurement step and re-pinned from the measurement, never loosened.

| Pin or flow | Where | Moves to |
|---|---|---|
| `floor_12.json` `LayoutHash` | Task 3 | the `--rehash-plans` value (only floor 12's may move) |
| `TrackDepthRules.Floor12NoFloorDrop` | Task 3 | the measured pair (expected PartyTrack 200, EnemyTrack 160) |
| `[chasm]` and `[map] rail` counts (`chasm=`, `planks=` 3→2, `regions=`) | Tasks 1, 3 | measured; `pit=282@0.25 island=room 9+12 sealed=2 bridge=311..314` appended |
| ride 5's At values (`r5_hammers`, `r5_hounds`, new `r5_switch_out`, `r5_pit`, `r5_switch_back`, `r5_parked`) | Tasks 4, 5 | computed to 4 decimals from the measured S (Ruling 12) |
| `TrackStationsTests`' (1156.0, 1161.0) | Task 4 | the pit's measured lip and far edge |
| `RailEncounterPlanTests` (ride 5's list, its order, the barricades, 21 → 23 pieces) | Tasks 4, 5 | B.3's order |
| `--test-cavern`'s bare-track check | Tasks 3, 4 | laid over every exit bridge cell (3), bare over the pit (4) |
| `--test-cart-watchdog` step 3, `--test-chase`'s two jumps, `--test-crevasse` B/C/`=trestle`/`=survivor` | Task 4 | the pit switch (B.10) |
| `--test-barricade` (r5), `--test-chase`'s ride-5 smash | Task 5 | the parked train |
| `--test-boarders=groups` (r5_hounds' gate), `--test-crew`, every flow that rides to stop 5 | Task 4 | the pit thrown on the way (Ruling 19) |
| the RIDE pass (the jump's pumping, step 8's chaser, the end-of-line wreck) | Task 8 | B.10 and Ruling 38 |
| `development.md:1539`, `2094-2100`, `2236-2240`, the counts | Task 9 | rewritten |
| `RampRideTests`' ride-5 row (B.10: "four wagons from ride 1") | — | done by plan 14 Task 1; Task 9's sweep confirms it |
| `LeapRulesTests.ShippedFloor12()` | Task 3 | holds by itself: its spec reads the campaign's knobs and its hash the plan's (cavern 238..259 unchanged) |
| `StopS`, the rides, cavern, break, split, every other piece's S | — | must not move (gated in Tasks 3–5) |

**Planner's rulings** (mine; each settles what the spec left open, with its cost if wrong):
1. Ruling: the build order and the interim floor.
   - Tasks 1–2 build the generator and the proof with floor 12 off.
   - Task 3 turns floor 12 on and builds the island's runtime. From Task 3, the pit cell is carved, but its track stays laid and `r5_jump` stays ride 5's hazard until Task 4 cuts the track and judges the pit. Between the two tasks the train rolls over the pit unjudged, while walkers fall into it as into any chasm.
   - Cost: one task with a hole the train ignores.
2. Ruling (a contradiction resolved): `RailRules.BridgeIslandCells` is **4**, not the spec's 3.
   - The rule clips the last straight run to end that many cells before the exit platform. On floor 12, 3 gives 311..315, while the spec's own cells (311..314, S ≈1184–1199), its enemy cells (51..53,61), its moat (51..53,62..63), "The bridge keeps 4 cells to the platform" and the spur's points "on the island" (1200.3) all need 4.
   - Cost: one fewer bridge cell on a line whose last straight runs into the corner.
3. Ruling: the island's rooms and seals.
   - **The island rooms** are the exit room `R`, plus each other room (not the start's) that has no cell reachable from the up stairs with the rails blocked and `R`'s cells walled, but has one with `R` open.
   - **A mouth** is a walkable hall cell (outside every room, not a line or pocket cell) beside an island room's cell.
   - **The walk** starts at a mouth and goes from cell to cell along its unvisited walkable hall neighbours. It stops before a cell with 3 or more walkable 4-neighbours, which is the fork and stays open, or before a room cell.
   - A walk that ends at an island room seals nothing (room 9 to room 12). Any other walk is sealed: the mouth becomes a trench (`Cell.Chasm` at the room's level) and the rest becomes `Cell.Wall`.
   - Cost: a hall that branches inside the sealed stretch leaves its branch open, and then the carve refuses (Ruling 4).
4. Ruling: `RailIsland.Cells` is `Reachable(DownStairs)` after the cut (rails open, secrets shut).
   - The carve records every cell it changes. It restores them all and reports `refused (…)` when:
     - the up stairs reach any island cell: `refused (leaks at (x,y))`;
     - a key (`KeySpawns`, a vault's key, the objective's spare keys), a vault lever, a prisoner cell or a jail-room cell lies in the island: `refused (a key at (x,y) inside)`, and the same for each kind (the objective's other reserved cells do not refuse);
     - the exit room is the start's;
     - a taken cell that is not a trap lies on a sealed hall (B.1).
   - No bridge run found gives `none`.
   - Cost: none (a refused floor is the floor without the island, byte for byte).
5. Ruling: a trap on a sealed cell is removed from `FloorLayout.TrapSpawns`.
   - It is logged through a new `FloorLayout.GenLog` (not hashed), which `DungeonBuilder` prints as `[gen] {line}` after its floor line.
   - Cost: later trap indices shift (floor 12's plan names traps by room, never by index).
6. Ruling: the moat.
   - It takes each party bridge cell's inward cells at t = −1, −2, and the outward cells at t = +2, +3 of each enemy bridge twin (the strips' convention), plus the trenches. Only rock or pocket cells are cut, never a lane, platform, ramp, ramp neighbour or taken cell.
   - An enemy twin is a bridge cell only when it is a straight enemy cell (its two enemy-lane neighbours face each other), so the corner (54,61) stays solid.
   - The lava is the whole `ChasmDressing` region holding any pit or island cut, so the gorge's last span (311..313, inward) is molten too.
   - Cost: that span changes from embers to lava.
7. Ruling: `CarvePit` refuses (`refused`) a ride the line does not have, the break's ride, the split's ride and the cavern's ride. A ride with no fitting run is `none`.
   - The run must lie whole in `RideWindow(rail, k, PitFrom, PitTo)`, the strips' platform-middle fractions with `StripPlatformGap` in from each platform.
   - Cost: none.
8. Ruling: the exit bridge's track is kept whole (never in `Gaps`). `RailTrack.OmitBallast(spans)` takes the gravel off over the span and leaves the rails and sleepers, so the lava shows between the sleepers on the trestle's stringers. Cost: one `RailTrack` method.
9. Ruling: the exit bridge's hold is the second term of `RailEncounterController.HoldsRiders()` (cavern bridge or exit bridge).
   - So "Get out" (cart and wagons), `RailController.MovesRefused` (plan 14 Ruling 11: "Plan B adds the exit bridge here") and the throw clocks' pause (`MovesRefused`) take it with no new wiring.
   - It holds while the train rolls at any speed, so a train braked to a stop on the span holds its riders until it rolls (B.2).
   - Cost: none.
10. Ruling: a plain wreck with the cart's front at or past the bridge's start (`ExitBridgeRules.WreckInside`).
    - No spare takes over and no OnFoot event is raised. The cart is hidden.
    - Every rider of the cart and of each coupled wagon, crew included, is set down: on the island's set-down cells past the far edge when the rider stands at or short of it, and beside the lane where they are otherwise.
    - `StopIndex` becomes the last stop and `LastWreckInside` is set, so a save after it loads the cart Held at stop 5, inside.
    - Cost: a load after such a wreck gives a whole cart at stop 5, which is what "home" (B.9) means.
11. Ruling: the pit switch reuses ride 3's machinery.
    - `Piece.PitDeadly` is a `switch_out` on `PitRide` with `PitRules.Deadly(points, lip)`. Its `TrackSwitch` is `Deadly`, and its seated offer is the plain switch_out's (no "across" condition).
    - The back is a plain `switch_back`, so `BuildSwitch` already makes it `Fixed` and `TakeSwitch` crosses with `backS` set.
    - R-lone is decided in `TakeSwitch` (`PitRules.TakesRound`). A crossing there logs "The cart takes the switch round the pit.", and the late press is `LateFor` as it stands.
    - Cost: none.
12. Ruling: the placement numbers.
    - The pit switch's points are at lip − `PitRules.OutLead` (8.0 m) and the back's at the far edge + `PitRules.BackPast` (1.0 m).
    - The hammers' middle is S 1157.0 (B.3's 1154/1157/1160).
    - The parked train's rear is `ParkedRules.RearS(bridgeFrom, 4)`, so its front is 1.0 m short of the bridge.
    - Each At is `(S − StopS[4]) / (StopS[5] − StopS[4])` to 4 decimals, from the measured S (plan 13's rule).
    - Cost: none.
13. Ruling (the controller's "plan B moves r5_hounds"): its pace point moves to `max(1100.0, home + SpawnBehind − Offset + 2)`, where `home` is the back's points + `SwitchStretch`. That is about S 1104.0 with Offset −8. Its spawn point is then 2 m clear of the crossing, behind a train whose rear is home.
    - `--test-boarders=groups` gates it. One retune is allowed, Offset −8 → −6, and it must be reported.
    - Cost: the hounds have about 4 m less of ride before the golem frees their cart.
14. Ruling: the plan's indices stay. `r5_jump` becomes `r5_pit` in its own entry, and `r5_barricade` becomes `r5_parked` in its own entry. `r5_switch_out` and `r5_switch_back` are appended after `r4_leap`. Cost: none (wire names are plan indices).
15. Ruling: the pit is read and cut from the layout on every role (`ReadPit`, before the pieces), whether or not a `pit` piece names it, as the broken bridge is.
    - The `r5_pit` piece is its plan entry: it gives the dressing and the order. An `r5_pit` more than `PitRules.AtSlack` (2 m) from the lip, or a layout pit with no piece, is an ERROR line. The cut and the guard stand regardless.
    - Cost: none.
16. Ruling: the watchdog's pit steps (`WatchdogPit`) feed `GrindFree`'s existing `BackWindow`, `BackPoints` and `BreakLip` slots, with prints that name the pit. The break and the pit never share a ride, and the armed ride picks one. Cost: three enum names that read "back" for the pit.
17. Ruling: `SetPieceRules.JudgesJump` adds the lane check. A gap whose lip the train meets across (`LaneBlendAt` ≥ 0.5) is passed, never judged, and the watchdog's `GapLip` is given only when the train is on its own lane there. Cost: none.
18. Ruling: the traffic runaway ends `RunawayShort` (3 m) short of the pit's lip when the pit lies between its stop and the next (`SetPieceRules.RunawayEnd`). Cost: none.
19. Ruling: every flow that rides ride 5 to its end throws the pit lever from the seat through one helper, `ThrowPitOnTheWay`, called by `RideOnTo` and by every flow that drives ride 5 itself.
    - The fall tests (`--test-pit=wrong|stall|late`, `--test-crevasse` B and its variants) leave it straight.
    - The RIDE pass is red by construction from Task 3 until Task 8. From Task 3 its end-of-line wreck takes no spare (Ruling 38), and from Task 4 an able remote human leaves the pit lever straight. This is a planned window; plain `net_test.sh` stays green each task, and Task 8's reviewer checks the window closes.
    - Cost: co-op island and pit bugs surface at Task 8.
20. Ruling: the parked train is a `ParkedTrain` node.
    - It is made of a `MineCart.AsChaser()` cart and four `TrainWagon` tubs (Numbers 1..4) with their own wire names (`TrainWagon.WireRef`, `liftwagon:{k}`), never in `RailController.Wagons`.
    - It is solid (layer 1|8) while it stands and has no collision while smashed.
    - Cost: none.
21. Ruling: the smash costs the barricade's, through one helper (`RailEncounterController.SmashCost`) that `r1`/`r2_barricade` and the parked train both call.
    - Riders aboard are thrown clear first, each to its own `SetDownNear` cell on the outer bank (never a drop).
    - The five tubs tumble as the cart's `Tumble` does (layer 0) and go after `ParkedRules.TumbleSeconds` (6 s).
    - Cost: none.
22. Ruling: `ParkedRules.Next` moves the parked train from InTheWay to Smashed to Lift.
    - A lift is due when the party train's rear is past the bridge's far edge, the train is held at the last stop, the party has no live cart (wrecked and hidden), or `LastWreckInside`.
    - It stands at ride 5's arming and at every restore. It is not saved.
    - Cost: none.
23. Ruling: the lift is a `Chase` with `Lift = true`, so one chase runs at a time. The lift offers nothing while another chase is live, until that chaser or relief has ended. Cost: a player at the lift waits a few seconds for a chaser's riders to step out.
24. Ruling: the takeover happens at the first "Board the cart" while the party has no live cart (`ParkedRules.TakesOver`).
    - The lift hides, and the party's cart with four fresh wagons (`CoupleFresh("the lift takes over")`) stands Held at the lift's cart S, with `StopIndex` kept at 4 and the presser seated.
    - Its departure arms ride 5 with every piece and encounter short of that S counted as passed (`RailEncounterController.ArmFrom`).
    - Cost: none.
25. Ruling: at the lift's first board, every freed crew member on foot within 25 m sits in its wagons at once, in `TrainRules.SeatOrder`, and escorts within 25 m take its bench. Nobody walks to it. Cost: no walk to the lift.
26. Ruling: a chaser's and a relief's limit also stays `SpareRules.ChaseGap` (1.5 m) behind a standing lift's rear, and never past the bridge's start − `HalfLength` − 0.5 m (`SpareRules.ChaseLimit`, four-argument). `ChaseDone` at the lift ends the chase outside, and its riders step out beside the lane. Cost: none.
27. Ruling: the lift on the wire.
    - `CartIds.LiftBase` is 2100: the lift is 2100 and its wagons are 2101..2104.
    - Its `CartStateDto` has `Kind` Chaser, `StopIndex` −1, `Train` [1, 2, 3, 4] and `CrewWagons` 4.
    - A client drives its own `ParkedTrain` (built from the layout) from those states. `PropEvt parked:{piece}` says "standing" and "smashed".
    - The RIDE book is `L`.
    - Cost: none.
28. Ruling: a save replaces the pose of a hero or NPC seated in the lift with the lift's set-down spot (`RailController.SavePose`): beside the parked train outside, or on the island's set-down cells once the lift's rear is past the far edge. Cost: a lift ride cut short by a load is ridden again.
29. Ruling: on an island floor, `InIsland` replaces `InExitRoom` (`FloorLayout.InIsland`); "seated in a coupled wagon while held at the last stop" is unchanged. Cost: none.
30. Ruling: once `CrewRules.UnopenedLeftBehind` holds (every up, unparked player is in the island, and the train is held at the last stop or `LastWreckInside`), each unopened cage's stop is added to `RailController.CrewLeftAt`. It prints `[CrewRescue] island: cage at stop {s} left behind (never opened)`. Cost: none.
31. Ruling: "stranded" is judged live (`CrewRules.JudgeOnIsland`). It counts as Lost while it holds and as Freed again once an up player stands outside; once the win fires, the win stands. Cost: none.
32. Ruling: Grisel is brought in (`CrewRules.BringEscortIn`).
    - She is set down on the island's set-down cell nearest the held train's rear, once each time the condition starts.
    - The log reads "Grisel picks her way in over the bridge behind you." (a log line, not a bark).
    - Cost: none.
33. Ruling: the reward chest is a `Chest` with `TableId = ChestRules.VaultTable` (vault 0's table), `SpawnIndex = CrewRules.RewardChestIndex` (9000), not a mimic and not locked.
    - It stands on the exit room's first free cell (the existing `ExitCells()` order, nearest the stairs) that is not a line cell and no prop takes.
    - It is saved in `ObjectiveStateDto.RewardChest` and its opened state in `OpenedChests`, as any chest.
    - On the wire it is `PropEvt(NetRefs.Chest(9000), "stood")`, and the catch-up re-sends it.
    - The log reads "The prisoners point you to a strongbox by the stairs: the Founders' share is yours."
    - Cost: none.
34. Ruling: `col_stairbell`'s goal becomes `{ "Kind": "Guard", "Room": 9, "Objective": null, "Route": [], "AlarmTarget": null, "RallyRoom": 9 }`. Cost: none.
35. Ruling: `BarkTrigger.PitSwitch`, `PitSwitchNow` and `ParkedCart` are appended after `BoarderThrow`, all Shout.
    - Each has subtitle-only stock banks in `RideBarks`: 3 per roster name (Sable's a woman's), 2 per class and 3 generic.
    - The logs with nobody to speak are the spec's: "Lava pit on the line — throw the switch!", "Throw it — now!" and "Cart on the line — brace!".
    - Cost: none.
36. Ruling: the cave-in's look is `Rubble.Mound` and `Rubble.Collapse` on each open face, drawn from a per-cell seeded local `Random`, as `DungeonBuilder.SealMouth` already does.
    - It is look only: no collision, skipped headless, and no floor stream is touched. The face count is printed headless.
    - Cost: if the controller reads "nothing new draws from an rng" as covering looks, a fixed boulder pattern replaces it (the look alone changes).
37. Ruling: the new flows.
    - `--test-pit[=thrown|wrong|lone|stall|late]` lives in `DebugTools.Pit.cs`, default `=thrown`, with `_quitAfter = 200`.
    - `--test-island[=cavein|bridge|wreck|parked|lift|takeover|onfoot|stranded|grisel|chest]` has its dispatcher and shared helpers in `DebugTools.Island.cs` and one partial file per group (`.Cavein`, `.Bridge`, `.Parked`, `.Lift`, `.Home`), plan 14's split pattern. Default `=cavein`, with `_quitAfter = 300` (450 for `=onfoot`).
    - Both use solo floor 12 at lethality 3, every enemy culled as it appears, `[pit]`/`[island]` lines, `RESULT ok ({variant})` or `FAIL (…)`, and plan 13's 10 s stall watchdog.
    - A variant that needs more than its budget stops and is reported.
    - Cost: none.
38. Ruling (a contradiction resolved): the RIDE pass's end-of-line wreck (`net_test.sh`: "the wreck at the end of the line: no spare took over") is at or past the bridge's start, where B.2 forbids a spare.
    - It becomes B.2's check: no spare, and the riders are set down inside.
    - The server then restores the cart at stop 4 through a test hook (`DebugRestoreAt(4)`) and wrecks it there, which carries the old spare checks (spare 3, then spare 2).
    - Cost: one more test hook.
39. Ruling: `r5_spur`'s lever. When `PointsS − LeverLead` falls on an exit bridge cell, it stands `SplitRules.LeverPast` (3 m) past the points on the party lane, and its seated window keeps its start (`WindowStartS = PointsS − LeverLead − WindowAhead`). Cost: none.
40. Ruling: a unit test pins that `SafeRoomRules.GatherCell` is an island cell, so the safe room's gathering spawns inside it, and every island flow runs with gathering on and greps no ERROR. This is the builder's check B.6 asks for. Cost: none.
41. Ruling: in the proof, "every cage" reads as each intermediate stop's platform (the cages are runtime, on sidings beside their platforms). "Every room outside the island" reads as each room outside it that the up stairs reach when the island's cut and cave-in cells are read as open (`Reachable`'s `open`) but not as the floor stands, the `ChasmProblems` pattern (so secret rooms and vaults behind their own locks are judged as today). Cost: a siding walled off from its platform would pass the proof (`PickSiding` takes only cells beside it).
42. Ruling: a solo relief halting behind the lift is pinned engine-free (`SpareRules.ChaseLimit`), and the online chaser's halt is pinned by the RIDE pass. The two share the limit, so no solo flow drives a relief to the lift. Cost: the relief's halt there is not run end to end.

## Global Constraints

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
- Only the controller runs the RIDE pass, alone, under the lock. A failure is investigated from its kept logs, never rerun to green.
- Commits end with "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>". Stage by explicit path. No push, merge, installers or dist builds.
- Implementers run on opus, reviewers on fable.
- `RailController.MovesRefused` gains the exit bridge (plan 14 Ruling 11). Plan B's barks append after `BarkTrigger.BoarderThrow`.
- Also binding:
  - Nothing new draws from an rng.
  - A flow passes only on exit 0, its own `[...] RESULT ok` line (never the early `[builtin] RESULT ok`) and no `ERROR` line.
  - Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N`.
  - Hand-edited JSON keeps the game's escaping (`'`, `&`, `<`, `>` and `+` as `'`, `&`, `<`, `>` and `+`; non-ASCII as `\uXXXX`, upper-case hex) and a two-space indent, and `git diff` shows only the values meant.
  - The full suite is `dotnet test tests/DungeonCrawler.Tests.csproj`. Never `git add tests/`.
  - Floor-12 dev saves from before this plan are not supported (spec, Constraints): a flow that loads one makes a fresh save.

## Review Focus

- **A save is taken while someone rides the lift over the exit bridge, or sits in the party's train braked on it, and is then loaded.** Nobody stands on a bridge or moat cell after the load. The lift's rider stands beside the parked train outside (or on the island past it), and the party's riders are seated as saved.
  - Pinned by `ParkedRulesTests.ALiftRidersSavedPose_IsNeverOverTheBridge` (Task 6).
  - Pinned by `--test-island=lift`'s mid-bridge save and load (Task 6).
- **The cart is wrecked with its front just past the bridge's start, the wagons still outside and crew aboard.** Every hero and crew member stands on an island cell past the far edge. None stands on a drop, none falls, and no spare takes over.
  - Pinned by `ExitBridgeRulesTests.AWreckJustPastTheStart_IsInside_AndSetsEveryoneDownPastTheFarEdge` (Task 3).
  - Pinned by `--test-island=wreck`, with the front 0.5 m past the start (Task 3).
- **A player outside parks (leaves) or rejoins while the train is held at stop 5, before the win.** Crew on foot outside are lost while no up player stands outside, and are freed again once one does. Once the win fires, it stands.
  - Pinned by `CrewRulesTests.Stranded_IsLive_UntilTheWin` (Task 7).
  - Pinned by `--test-island=stranded`'s leave-and-return leg (Task 7).
- **The lift is boarded while a chaser or relief still runs toward it, or two riders press "Board the cart" on the same tick while the party has no live cart.** Nobody boards until the chase has ended. Exactly one takeover happens, both riders sit in the party's cart, and nobody is seated in the hidden lift.
  - Pinned by `ParkedRulesTests.TheLift_OffersBoarding_OnlyWithNoChaseLive` (Task 6).
  - Pinned by `--test-island=takeover`'s same-tick double press (Task 6).
- **A press on the pit lever comes on the tick the front reaches the points.** It is too late: it is refused out loud, the lever stays straight and the train falls. It never crosses half the train.
  - Pinned by `PitRulesTests.APressOnThePointsOwnTick_IsTooLate` (Task 4).
  - Pinned by `--test-pit=late` (Task 4).

---

### Task 1: The baseline, and the pit and the island in the generator (floor 12 still off)

**Files:**
- Create: `src/Dungeon/Generation/FloorGenerator.Island.cs` (`CarvePit`, `CarveIsland` and their helpers)
- Modify:
  - Generator: `src/Dungeon/Generation/FloorGenerator.Chasms.cs` (the two passes after `CarveStrips`, before `CarveWalkways`).
  - Layout records:
    - `src/Dungeon/Generation/RailLine.cs` (the `RailRules` constants, `RailPit`, `RailIsland` and the four `RailLine` fields);
    - `src/Dungeon/Generation/FloorLayout.cs` (`GenLog`, `InIsland`).
  - Knobs: `src/Dungeon/Generation/FloorSpec.cs`, `src/Adventure/Core/Plans/CampaignPlan.cs` (`SpecOverrides`).
  - Output:
    - `src/Dungeon/Generation/LayoutReport.cs` (` pit=` and ` island=`);
    - `src/Dungeon/DungeonBuilder.cs` (prints `GenLog` as `[gen] …`).
- Test: `tests/Dungeon/ShippedFloors.cs` (new: floor 12 built engine-free with or without the island), `tests/Dungeon/IslandTests.cs` (new)

**Interfaces:**
- Consumes: `FloorGenerator.RideWindow`, `SlagTwin`, `StripCells`' rules, `TakenCells`, `RailSide`, `Times`, `Rock`; `FloorLayout.Reachable`.
- Produces:
  - **The knobs.** `FloorSpec.PitRide` (int, default 0) and `FloorSpec.ExitIsland` (bool, default false).
    - `SpecOverrides.PitRide` (`int?`, `[AiIgnore]`, clamped 0..6) and `SpecOverrides.ExitIsland` (`bool?`, `[AiIgnore]`), both in `IsEmpty` and `Apply`.
    - Their `AiDescription`s: "The ride (1-based) whose party line has a lava pit with a deadly switch round it, 0 for none; needs Crevasses." and "True: the exit room and the rooms off it become an island reached only by a rail bridge over lava; needs Crevasses."
  - **`RailRules`.** `PitRunCells = 11`, `PitLeadCells = 7`, `PitFrom = 0.05f`, `PitTo = 0.60f`, `BridgeMinCells = 4`, `BridgeIslandCells = 4` (Ruling 2), `MoatDepth = 2`, `ParkedCells = 4`.
  - **The records:**
    - `public sealed record RailPit(int Ride, int Cell, IReadOnlyList<GridPos> Cut)`: `Cell` is the party-lane index, and `Cut` is the pit cell, then its inward cells t = −1, −2 that were cut;
    - `public sealed record RailIsland(IReadOnlyList<int> Rooms, RailSpan Bridge, RailSpan Parked, IReadOnlyList<GridPos> EnemyBridge, IReadOnlyList<GridPos> Trenches, IReadOnlyList<GridPos> CaveIns, IReadOnlyList<GridPos> Moat, IReadOnlySet<GridPos> Cells)`, every list in (Y, X) order, with `Moat` the cells it cut. `Parked` = (`Bridge.From − ParkedCells`, `Bridge.From − 1`).
    - `IEnumerable<GridPos> RailIsland.Cut(RailLine rail)` yields the party bridge cells, `EnemyBridge`, `Moat` and `Trenches`.
  - **`RailLine` fields** (none hashed):
    - `RailPit? Pit`; `string? PitWhy` ("ok", "none" or "refused"; null when not asked);
    - `RailIsland? Island`; `string? IslandWhy` ("ok", "none" or "refused (…)"; null when not asked).
  - **`FloorLayout`.** `List<string> GenLog` (not hashed) and `bool InIsland(GridPos p) => Rail?.Island?.Cells.Contains(p) == true`.
  - **The passes** (no rng; in `CarveChasms` after `CarveStrips`, before `CarveWalkways` and `RaiseWalls`):
    - `internal static (RailPit? Pit, string Why) CarvePit(FloorLayout layout, RailLine rail, int ride, int breakRide, int splitRide, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)`. It takes the first run (lowest start index) of at least `PitRunCells` consecutive straight party cells lying whole in `RideWindow(rail, ride − 1, PitFrom, PitTo)`. Each cell's enemy twin must be a straight enemy cell taken by nothing, both lanes must be at one level, and no ramp may lie on the run or within a cell of it. The pit is `run.From + PitLeadCells`. Refusals follow Ruling 7.
    - `internal static (RailIsland? Island, string Why) CarveIsland(FloorLayout layout, RailLine rail, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)` follows Rulings 3–6. Its bridge is the last straight party run of at least `BridgeMinCells` on the last ride, clipped to end `BridgeIslandCells` cells before the exit platform's first cell, with no platform, ramp or taken cell on it or its twins. A trap removed adds `island: trap at {c} sealed` to `GenLog`. The island's cuts keep every `TakenCells` rule and stay off the platforms' cells, but are exempt from `StripPlatformGap` (B.3), as the trench beside room 9 is.
  - **`LayoutReport.Rail`** appends ` pit={Cell}@{f}`, ` pit=none` or ` pit=refused` when asked, then ` island=room {a}+{b} sealed={trenches} bridge={From}..{To}`, ` island=none` or ` island={why}` when asked. `f` is the cell's fraction of its own ride, measured as `break=` is.
  - **The `[gen]` lines.** `DungeonBuilder` prints `[gen] {line}` for each `GenLog` line, after its `[DungeonBuilder] floor …` line.

- [ ] **Step 1: Measure the baseline** (lock absent).
  - Run `dotnet build DungeonCrawler.csproj && XDG_DATA_HOME=$(mktemp -d) timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | tee /tmp/p15-baseline.txt | grep -E "\[map\] (rail|room (1|9|12):)|\[track-depth\] (piece|encounter|PartyTrack |EnemyTrack |total)|\[chasm\]|hash="`.
  - Expected: every value in "Measured" (S ± 0.1), `hash=EBDAC17562E33A06`, and `encounter r5_hounds … S=1100.0`.
  - Then read the ASCII map's rows 43–50 from `/tmp/p15-baseline.txt` and report the glyphs at (45..48,44..46) (the north fork).
  - **Stop and report** without changing any code if a value differs, or if (46,45) does not have three walkable 4-neighbours.
- [ ] **Step 2: Write the failing tests.** `ShippedFloors.Floor12(bool island)` builds the spec as `LeapRulesTests.ShippedFloor12` does, then `spec with { PitRide = island ? 5 : 0, ExitIsland = island }`. It asserts the plan's `LayoutHash` only when `island` matches the shipped campaign's knobs.

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
    Assert.DoesNotContain(new GridPos(46, 47), l.Walkways);
    Assert.Equal((new RailSpan(311, 314), new RailSpan(307, 310)), (i.Bridge, i.Parked));
    for (var k = 311; k <= 314; k++) Assert.True(l.IsChasm(l.Rail.Party[k]) && l.IsPartyRail(l.Rail.Party[k]));
    Assert.Equal(new[] { new GridPos(51, 61), new GridPos(52, 61), new GridPos(53, 61) }, i.EnemyBridge);
    foreach (var (x, y) in new[] { (51, 58), (51, 59), (51, 62), (52, 62), (53, 62), (51, 63), (52, 63), (53, 63) }) Assert.True(l.IsChasm(new GridPos(x, y)), $"({x},{y})");
    Assert.False(l.IsChasm(new GridPos(48, 62)) || l.IsChasm(new GridPos(50, 62)) || l.IsChasm(new GridPos(54, 61)));
    Assert.True(i.Cells.Contains(l.DownStairs) && i.Cells.Contains(new GridPos(55, 52)) && i.Cells.Contains(new GridPos(50, 60)));
    Assert.DoesNotContain(i.Cells, l.Reachable(l.UpStairs).Contains);
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

  - `internal static FloorLayout GenIsland(int seed, FloorObjectiveKind obj, bool island)` is `ChasmTests.GenSplit`'s spec plus `PitRide = 5, ExitIsland = island` (Task 2's tests call it).
  - Add `ThePitAndTheIsland_AreDeterministic`: two builds of floor 12 with the island give equal hashes, `Pit`, `Bridge` and `Cells.Count`.
- [ ] **Step 3: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~IslandTests"`. Expected: a compile error (`PitRide`, `RailPit`, `GenLog`).
- [ ] **Step 4: Implement** the Interfaces.
- [ ] **Step 5: Run them.** Run the filter, then the full suite. Expected: PASS, with `DefaultKnobs_BuildTodaysFloors` and every `ChasmTests` row green.
  - **Stop and report** without tuning any rule if the floor-12 tests fail on a cell list, the bridge, `planks=2` (the plank pass picked a different plank in (46,47)'s place) or the fraction. Report the cells the rules produced.
- [ ] **Step 6: Commit**

```bash
git add src/Dungeon/Generation/FloorGenerator.Island.cs src/Dungeon/Generation/FloorGenerator.Chasms.cs src/Dungeon/Generation/RailLine.cs src/Dungeon/Generation/FloorLayout.cs src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Dungeon/Generation/LayoutReport.cs src/Dungeon/DungeonBuilder.cs
git commit -m "feat(gen): PitRide and ExitIsland (AiIgnore, off by default, needing Crevasses): a no-rng pass cuts a ride's lava pit (the first 11-cell straight run's seventh cell and two inward) and another seals the exit room and the rooms off it (a lava trench at each hall's mouth, a cave-in to its fork), cuts the last straight run before the exit platform to a rail-only bridge with a two-cell moat, and refuses whole (every cell put back) when it would leak or hold a key; RailLine.Pit/Island, [gen] lines, pit= and island= on the map line

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: The rail proof (FloorSolvability on an island floor)

**Files:**
- Modify: `src/Dungeon/Generation/FloorSolvability.cs` (made `static partial`)
- Create: `src/Dungeon/Generation/FloorSolvability.Island.cs`
- Test: `tests/Dungeon/IslandSolvabilityTests.cs`

**Interfaces:**
- Consumes: Task 1's `RailIsland`, `RailPit` and `FloorLayout.InIsland`.
- Produces: on a floor with `Rail.Island` (and on no other), `FloorSolvability.Problems`:
  - **Skips the stairs.** It drops "the stairs down … are out of reach with every lock worked" in both runs.
  - **Adds, rails open** (the every-lock-worked reach, with the reward vault open as the stairs check opens it):
    - **On foot.**
      - Each intermediate stop's first platform cell: `on foot, stop {k}'s platform at {c} is out of reach`.
      - Each party cell of `Island.Parked`: `on foot, the parked train's place at {c} is out of reach`.
      - Each room that the island's cuts cut off (Ruling 41): `on foot, room {i} is out of reach`.
    - **Sealed.** The first island cell (Y, X) reachable from the up stairs with every door open: `the exit island leaks at {c}`.
    - **By rail.** Every cell from `Parked.From` to the cell before the last stop's platform must be a party-lane cell, walkable or a bridge cell that is a chasm: `the line into the island breaks at {c}`.
    - **Inside.** From the last stop's first platform cell, the stairs and a cell of each island room: `inside the island, room {i} is out of reach from stop {k}'s platform` and `inside the island, the stairs down at {c} are out of reach from stop {k}'s platform`.
    - **The pit** (with `Rail.Pit`): the party cells either side of it, and the enemy twins of it and of them, must be walkable: `the pit cuts the line at {c}`.
  - **Adds, rails blocked:** each intermediate stop's platform, with today's `with the rails blocked, ` prefix.
  - **`ChasmProblems`** skips the island's rooms and the stairs, and skips its cells in the chest, key and objective checks.

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

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~IslandSolvabilityTests"`. Expected: FAIL ("the stairs down … are out of reach" on the island floor).
- [ ] **Step 3: Implement** the Interfaces.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS, with `RailLineTests.Sweep_NeverBreaksTheFloor`, `Floor_SolvableWithRailsBlocked` and `ChasmTests.Chasms_CutNothingOff` unchanged and green.
  - **Stop and report** if a row of `EveryIslandRow…` fails. The fix is a carve refusal (Ruling 4) or a proof rule, and the controller rules which.
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
    - `data/adventures/builtin/floor_12.json`: the `LayoutHash` by rehash, and `col_stairbell`'s goal (Ruling 34).
  - Rails:
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (calls `BuildExitBridge` after `BuildBreak`);
    - `src/Dungeon/Rails/RailEncounterController.Bridge.cs` (`HoldsRiders` ORs the exit bridge);
    - `src/Dungeon/Rails/RailController.cs` (`Wreck`'s inside rule, `WreckInside`);
    - `src/Dungeon/Rails/RailController.Fall.cs` (`LastWreckInside` beside `LastWreckFell`);
    - `src/Dungeon/Rails/RailController.Moves.cs` (the refusal's reason);
    - `src/Dungeon/Rails/SetPieces/SpurSwitch.cs`, `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (Ruling 39);
    - `src/Dungeon/Rails/Core/TrackDepthRules.cs` (the pins).
  - Looks:
    - `src/Dungeon/Props/RailTrack.cs` (`OmitBallast`);
    - `src/Dungeon/ChasmDressing.cs` (lava);
    - `src/Dungeon/DungeonBuilder.cs` (the cave-ins' look);
    - `src/Dungeon/Rails/SetPieces/RailGap.cs` (its molten material and light, shared as a static).
  - Flows:
    - `src/Core/DebugTools.cs` (the `[map] rail pit:` and `[map] rail island:` lines, the flag and its help);
    - `src/Core/DebugTools.Cavern.cs` (the laid check);
    - `src/Core/DebugTools.TrackView.cs`, only if it names 0/40 outside `TrackDepthRules`.
- Test: `tests/Dungeon/Rails/ExitBridgeRulesTests.cs`, `tests/Dungeon/Rails/TrackDepthRulesTests.cs`, `tests/Dungeon/IslandTests.cs` (the shipped floor is now the island one), `tests/Dungeon/SafeRoomTests.cs` (Ruling 40)

**Interfaces:**
- Consumes: Tasks 1–2; plan 13's `HoldsRiders` and `BridgeRules`; plan 14's `MovesRefused`, `TrainRules.Length`, `RunawayRules.TrainRearS`, `SetDownWagonHeroes`, `CoupleFresh` and `Train`.
- Produces:
  - **`ExitBridgeRules`** (engine-free, `DungeonCrawler.Dungeon.Rails`):
    - `const float LavaDepth = 3f`;
    - `bool Holds(bool rolling, float frontS, float rearS, float fromS, float toS) => rolling && frontS > fromS && rearS < toS` (NaN false);
    - `bool WreckInside(float frontS, float fromS) => frontS >= fromS` (NaN false);
    - `bool SetDownPastFarEdge(float bodyS, float toS) => !(bodyS > toS)`;
    - `const string WreckLog = "The cart is wrecked on the bridge. You scramble across onto the island."`.
  - **`RailEncounterController`** (`.Island.cs`, every role):
    - `float ExitFromS, ExitToS` (party S of the bridge's near and far edges), `float ExitEnemyFromS, ExitEnemyToS`, all NaN without an island.
    - `void BuildExitBridge(Node3D root)`. It reads the layout's bridge, calls `OmitBallast` on both tracks over their spans, builds the `ExitBridge` look and prints `[rails] exit bridge: party S={from:0.0}..{to:0.0} ({n} cells), enemy S={a:0.0}..{b:0.0} ({m} cells); rails kept, no deck, no footing`.
    - `bool ExitBridgeHolds()`: `ExitBridgeRules.Holds` for this peer's own train, rear from `RunawayRules.TrainRearS`.
    - `HoldsRiders()` returns the cavern's hold OR `ExitBridgeHolds()`.
    - `internal List<Vector3> IslandSetDown(int count)`: count set-down spots, from `CrevasseRules.SetDownCells` round `rail.Party[Bridge.To + 1]`, island cells only, never a drop.
  - **`RailController`:**
    - `bool LastWreckInside { get; private set; }`, cleared where `LastWreckFell` is.
    - `Wreck` does Ruling 10 when `!fall && enc.ExitFromS` is set and `ExitBridgeRules.WreckInside(Party.FrontS, enc.ExitFromS)`. It prints `[rails] wreck past the exit bridge's start (front S={f:0.0}): no spare takes over; {h} hero(es) and {c} crew set down inside; the stop is taken as {last}`, and logs `WreckLog`.
    - The `MovesRefused` reason over the exit bridge reads "the train is on a bridge that holds its riders".
  - **`RailTrack.OmitBallast(IReadOnlyList<(float from, float to)> spans)`** rebuilds the gravel without those spans. The rails, sleepers, curve, S and `Gaps` are unchanged.
  - **`ExitBridge : Node3D`** (look only, skipped headless):
    - `Build(TrackSampler party, TrackSampler enemy, (float From, float To) partySpan, (float From, float To) enemySpan, float lavaY)`;
    - two bents per cell, posts down to `lavaY`, cross-braces and a stringer under each rail, as boxes on `weathered_planks`.
  - **`ChasmDressing`.** A region holding any `Rail.Pit.Cut` or `Island.Cut` cell is lava. Its shaft runs down `ExitBridgeRules.LavaDepth`, with a molten surface and light (`RailGap`'s) and no ember floor or mist. The print becomes `[chasm] regions={r} decks={d} planks={p} cavern={c} lava={n}`.
  - **The cave-ins.** `DungeonBuilder.DressCaveIns(root)` dresses each open face of each cave-in cell (Ruling 36) and prints `[DungeonBuilder] floor 12: {n} cave-in face(s)` on every role.
  - **`--print-map`** adds:
    - `[map] rail pit: ride {r} cell {i} {pos}, lip at party S={lip:0.0}, far edge {far:0.0}`;
    - `[map] rail island: rooms [{..}], {n} hall(s) sealed, the exit bridge party S={from:0.0}..{to:0.0} (cells {a}..{b}), enemy S={ef:0.0}..{et:0.0}`.
  - **`TrackDepthRules.Floor12NoFloorDrop`** holds the measured pair.
  - **`SpurSwitch`** puts its lever `SplitRules.LeverPast` past its points when `PointsS − LeverLead` lies on an exit bridge cell, and keeps its window's start (Ruling 39). `BuildSpur` passes the bridge test.
  - **`--test-island=cavein`.** At stop 0, on foot, every enemy culled. It fails unless:
    - **The build.** `[island] floor 12: rooms 9+12, trenches [(46,49), (41,54)], 6 cave-in cells (faces {n}), exit bridge S=…..… (4 cells), lava regions {n}`; a ray down at each trench, moat and bridge cell finds no footing, and one at the pit's enemy twin finds the floor.
    - **The west hall.** The lead, walked into it from room 1 toward (46,54) (`ForcedMoveInput`), never passes world x 38 × 4 + 4 (the cave-in's face) in 8 s. A companion sent toward the down stairs finds no path and stays out of room 9.
    - **The north hall.** The same from the fork (46,45) toward (46,50).
    - **Into the lava.** The lead, teleported into room 9 and walked west, falls into the trench (`CrevasseWatch`, fallen within 2 s). A companion walked along the lane from cell 310 onto 311 falls within 2 s. Both come back at the next arrival, as plan 10's crevasse return.
  - **`--test-island=bridge`.** `rc.Restore(4)`, everyone boards, ride 5 is ridden with the jump made by pumping. It fails unless:
    - while any car is over the span: the lead's prompt is "", a forced Get out does nothing, `MoveOfferFor` is null for every hero and a forced `Move` is refused;
    - braked to a stop with the cart's front at `ExitFromS + 6` for 3 s, nobody can get out;
    - let go, the train arrives at stop 5.
  - **`--test-island=wreck`** (Review Focus 2). `rc.Restore(4)`, with crew in W1 and W2 (`DebugSeatCrew(1)`, `(2)`) and spares left. The test wrecks the cart (`rc.DebugWreck()`) as its front passes `ExitFromS + 0.5`. It fails unless, within 1 s:
    - no spare took over, `StopIndex` is the last stop, `LastWreckInside` is set and the cart is hidden;
    - every hero and every crew member stands on an island cell (`InIsland`), none on a drop, none fallen 3 s later.
    - Then a save and a load must give the cart Held at stop 5.
  - **`--test-cavern`'s build** adds `[cavern] exit bridge laid: party S={from:0.0}..{to:0.0} not bare, no gravel`. It fails if any party or enemy gap overlaps the exit bridge.

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
```

  - In `TrackDepthRulesTests`, the pin becomes the measured pair (Step 3).
  - `IslandTests`' shipped helper now asserts the hash with `island: true`.
  - `SafeRoomTests.OnTheIslandFloor_TheGatheringStandsInside`: `ShippedFloors.Floor12(true).InIsland(SafeRoomRules.GatherCell(l)!.Value)`.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~ExitBridgeRulesTests|FullyQualifiedName~SafeRoomTests"`. Expected: a compile error.
- [ ] **Step 3: Switch floor 12 on and measure** (lock absent).
  - Edit `campaign.json` (Ruling 34's goal too). Then run `timeout 60 godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2` and the sweep of `development.md:879` (20 floors, then `grep -c adapted` = 0). Expected: no repair line, and `git diff --stat data/` shows only `campaign.json` and `floor_12.json`.
  - Implement the Interfaces. Then run `dotnet build DungeonCrawler.csproj && XDG_DATA_HOME=$(mktemp -d) timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | tee /tmp/p15-island.txt | grep -E "\[map\] rail|\[track-depth\] (piece|encounter|PartyTrack |EnemyTrack |total)|\[chasm\]|\[gen\]|\[rails\] exit bridge|hash="`.
  - **The static gate.** `StopS`, `rides`, `cavern=`, `break=`, `split=` and every piece and encounter S must equal the baseline ± 0.1. **Stop and report** if any moved.
  - **Report** the pit's lip and far edge, the bridge's four S, the new hash, `chasm=`, `[chasm]` and the measured `nofloorDrop` pair. Task 4 computes its At values from them.
  - **Pin `nofloorDrop`** to the measured pair. If it is not PartyTrack 200 / EnemyTrack 160, the commit explains the difference cell by cell (B.2): 40 samples per 4 m of drop under the track.
  - Re-run: the total reads `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`, longest first:
  - `--test-chase --quit-after=750`, `--test-crew --quit-after=570`;
  - `--test-island=cavein`, `=bridge`, `=wreck`;
  - `--test-boarders=groups`;
  - `--test-crevasse`, `=trestle`, `=survivor`;
  - `--test-cavern`, `=bridge`, `=leap`;
  - `--test-split`, `--test-switch-back`, `--test-barricade`, `--test-ride`, `--test-ride-edges`, `--test-relief`, `--test-cart-watchdog`, `--test-track-view`, `--test-vault-puzzle`.

  Each must PASS. Then run plain `scripts/net_test.sh`, alone: `NET TEST: ok`.
- [ ] **Step 5: Commit** (the message names the old and new hash and the measured pins)

```bash
git add data/adventures/builtin/campaign.json data/adventures/builtin/floor_12.json src/Dungeon/Rails/Core/ExitBridgeRules.cs src/Dungeon/Rails/RailEncounterController.Island.cs src/Dungeon/Rails/SetPieces/ExitBridge.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Bridge.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailController.Fall.cs src/Dungeon/Rails/RailController.Moves.cs src/Dungeon/Rails/SetPieces/SpurSwitch.cs src/Dungeon/Rails/RailEncounterController.Kiln.cs src/Dungeon/Rails/Core/TrackDepthRules.cs src/Dungeon/Props/RailTrack.cs src/Dungeon/ChasmDressing.cs src/Dungeon/DungeonBuilder.cs src/Dungeon/Rails/SetPieces/RailGap.cs src/Core/DebugTools.cs src/Core/DebugTools.Cavern.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Cavein.cs src/Core/DebugTools.Island.Bridge.cs
git commit -m "feat(rails): floor 12's exit is an island (PitRide 5, ExitIsland, rehashed): lava in the trenches, the moat and the pit, boulders against the cave-ins, an open trestle under the exit bridge's kept rails; no Get out and no move car to car while any car is over the bridge; a wreck past its start sets everyone down inside and takes no spare; col_stairbell guards room 9; nofloorDrop re-pinned from the measurement; --test-island=cavein|bridge|wreck

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `src/Core/DebugTools.TrackView.cs` only if it changed.)

### Task 4: The pit switch (ride 5 re-laid round the pit)

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/PitRules.cs`
  - `src/Dungeon/Rails/RailEncounterController.Pit.cs`
  - `src/Dungeon/Rails/SetPieces/LavaPit.cs`
  - `src/Core/DebugTools.Pit.cs`
- Modify:
  - Data and plan: `data/adventures/builtin/floor_12.json` (Ruling 14: `r5_jump` → `r5_pit`, `r5_hammers`' At, `r5_hounds`' At, `r5_switch_out` and `r5_switch_back` appended), `src/Adventure/Core/Validate/PlanValidator.cs` (the kind `pit`), `src/Adventure/Core/Plans/FloorPlan.cs` (the kind's doc).
  - Encounters:
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs`: `ReadPit` before the loop, the `pit` case, `GuardPit` after `GuardBreak`, and the pit's calls in the tick;
    - `src/Dungeon/Rails/RailEncounterController.Switchyard.cs`: `PitDeadly`, R-lone and the log in `TakeSwitch`, and the deadly lever's lamp;
    - `src/Dungeon/Rails/RailEncounterController.cs`: `TickPit` beside `TickBreak`; `CollectPartyLaneHoles` gains the pit;
    - `src/Dungeon/Rails/RailEncounterController.Traffic.cs`: `RunawayEnd`.
  - Rails:
    - `src/Dungeon/Rails/RailController.Watchdog.cs` (the pit's step-down);
    - `src/Dungeon/Rails/RailController.Jump.cs` (the lane check);
    - `src/Dungeon/Rails/Core/SetPieceRules.cs` (`JudgesJump`, `RunawayEnd`).
  - Barks: `src/Adventure/Core/Plans/CampaignPlan.cs` (`PitSwitch`, `PitSwitchNow`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Adventure/Core/Voice/VoiceLines.cs`.
  - Flows:
    - `src/Core/DebugTools.cs` (the flag and its help; the cavern and watchdog help lines);
    - `src/Core/DebugTools.RideOn.cs` (`ThrowPitOnTheWay`);
    - `src/Core/DebugTools.Chase.cs`, `src/Core/DebugTools.Crevasse.cs`, `src/Core/DebugTools.CartWatchdog.cs`, `src/Core/DebugTools.Cavern.cs`;
    - `src/Core/DebugTools.Boarders.Groups.cs` (plan 14 Task 6's file);
    - `src/Core/DebugTools.Crew.cs` (only if it drives ride 5 itself).
- Test: `tests/Dungeon/Rails/PitRulesTests.cs`, `tests/Dungeon/Rails/SetPieceRulesTests.cs`, `tests/Adventure/RailEncounterPlanTests.cs`, `tests/Dungeon/Rails/TrackStationsTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Interfaces:**
- Consumes:
  - Task 3's measured lip and far edge, `HoldsRiders` and `ExitFromS`;
  - plan 11's `SwitchBackRules` (`HomeStretch`, `GrindBackTo`, `Late`, `SafeLineLog`, `TooLateLine`, `BackCall`), `SeatedSwitchRules`, `TrackSwitch.Deadly` and `BuildSignal`'s lamp;
  - plan 14's `AnyLeverWindow`, `MovesRefused`, `RailCalls.Say` and `BarkTrigger.BoarderThrow`;
  - plan 14 Task 6's `SetPieceRules.CrossingFrees` and `--test-boarders=groups`.
- Produces:
  - **`PitRules`** (engine-free):
    - the numbers: `LeadMin = 7f`, `LeadMax = 40f`, `OutLead = 8f`, `BackPast = 1f`, `AtSlack = 2f`, `CallEarly = SwitchBackRules.CallEarly`, `CallLast = SwitchBackRules.CallLast`, `ArmLead = SwitchBackRules.HomeStretch + 1f`;
    - `bool Deadly(float pointsS, float lipS)`: the lip lies `LeadMin`..`LeadMax` past the points (NaN false);
    - `bool Falls(float prevFront, float front, float lipS, float blendAtLip) => prevFront < lipS && lipS <= front && blendAtLip < 0.5f`;
    - `bool FallArmed(IReadOnlyList<float> outPoints, IReadOnlyList<float> backPoints, float lipS, float farS)`: at least one out, every out at least `ArmLead` short of the lip, and a back at or past the far edge;
    - `BackCall Call(bool thrown, bool taken, float front, float leverS, float pointsS, bool earlyDone, bool lastDone)`: Last at points − `CallLast` while straight and short of the points; Early from lever − `CallEarly`, once and never after the last;
    - `bool TakesRound(bool thrown, bool ableHumanAboard) => thrown || !ableHumanAboard`;
    - `float OutPoints(float lipS) => lipS - OutLead` and `float BackPoints(float farS) => farS + BackPast`;
    - the copy: `FallLog = "The cart runs into the lava pit!"`, `RoundLog = "The cart takes the switch round the pit."`, `EarlyLog = "Lava pit on the line — throw the switch!"`, `LastLog = "Throw it — now!"`.
  - **`SetPieceRules`:**
    - `bool JudgesJump(bool gapSet, bool judged, float front, float gapFrom, float blendAtLip) => gapSet && !judged && front >= gapFrom && blendAtLip < 0.5f`;
    - `float RunawayEnd(float nextStopS, float pitLipS)`: the nearer of next − `RunawayShort` and lip − `RunawayShort` (NaN ignored). `RunawayShort` moves to `SetPieceRules` with its value, and the controller's constant reads it.
  - **`RailEncounterController`** (`.Pit.cs`, every role unless marked):
    - `float PitS`, `PitFarS` (party S of the lip and the far edge, NaN without), `int PitRide = -1`, `bool PitArmed`.
    - `ReadPit(Node3D root)` reads `Rail.Pit`, cuts the party track bare over [lip, far] and builds `LavaPit` (bent rail stubs, red lamps at both edges). It prints `[rails] pit on ride {r}: cell {i} {pos}, lip at party S={lip:0.0}, far edge {far:0.0}; the party track bare over it`.
    - The `pit` piece (`BuildPitPiece`) checks `AtSlack` (Ruling 15) and prints `[rails] pit {id}: At S={s:0.0} ({d:0.0} m from the lip)`.
    - `Piece.PitDeadly` is set in `BuildSwitch`, and the switch's print reads `(deadly: the pit {lead:0.0} m past)`. A deadly out lever's post carries a steady red lamp.
    - `GuardPit()`: `PitArmed = PitRules.FallArmed(…)` over the ride's switches. It prints `[rails] pit on ride {r}: its fall armed (switch_out at [..], switch_back at [..])`, or an ERROR line, and disarms.
    - `TickPit()` (server): `PitRules.Falls` → `_rails.Fall(PitRules.FallLog)` and `[rails] the cart runs into the pit at S={front:0.0}`.
    - `TickPitCalls(Piece, PartyController)` (server): `RailCalls.Say` with `BarkTrigger.PitSwitch`/`PitSwitchNow` and `EarlyLog`/`LastLog`. It prints `[rails] switch {id} called {early|last} at front S={f:0.0} ({d:0.0} m to the lever|points); bark={who}`.
    - `TakeSwitch` with `PitDeadly` and a straight lever applies `TakesRound(false, AbleHumanAboard())` and crosses, logging `SafeLineLog`. A thrown one logs `RoundLog`.
    - `internal (float Window, float Points, float Lip) WatchdogPit()` (server): front S, from `SwitchBackRules.GrindBackTo` over the out lever. It returns the lip only while the train is on the party lane past the points, and NaN otherwise.
    - `CollectPartyLaneHoles` adds (`PitS`, `PitFarS`).
    - `internal int PitFalls` (tests).
  - **`RailController.GrindFree`** takes `WatchdogPit()` when `WatchdogBreak()` gives nothing (Ruling 16). Its prints name the pit:
    - `… short of the pit switch's window (its lever straight); set with its front at the window's start`;
    - `… at the last call's mark`;
    - `… at the pit switch's points, taken as its lever stands`;
    - `… past the points on the party lane; ground on into the pit: the train falls` (`FallNow(PitRules.FallLog)`).
  - **`JudgeJump`** and the watchdog's lip read `SetPieceRules.JudgesJump` (Ruling 17).
  - **`BarkTrigger.PitSwitch` and `PitSwitchNow`** are appended after `BoarderThrow`, Shout (Ruling 35).
  - **`DebugTools.ThrowPitOnTheWay(Main main, RailController rc, string tag)`.** While the pit's ride is armed and a deadly out lever's seated window holds the front, it presses the lever for the seated lead, once, through the dispatch `--test-switch-back`'s thrown branch uses, and prints `[{tag}] pit: thrown from the seat at front S={f:0.0} ({d:0.0} m before the lever)`.
    - `RideOnTo` calls it while rolling. `DebugTools.LeavePitStraight` (bool) stops it for the fall flows.
  - **Floor 12's ride 5**, with At computed per Ruling 12 from Task 3's measurement:

    | Id | Kind | Placed at (party S) |
    |---|---|---|
    | `r5_switch_out` | `switch_out` | `PitRules.OutPoints(lip)` (lever 5 m before) |
    | `r5_pit` (was `r5_jump`) | `pit` | the lip |
    | `r5_switch_back` | `switch_back` | `PitRules.BackPoints(far)` |
    | `r5_hammers` | `hammers` | middle at 1157.0 |
    | `r5_hounds` (encounter) | `slag_cart` | Ruling 13 (≈1104.0), Offset −8 |

  - **`--test-pit`** (Ruling 37). `rc.Restore(4)`, everyone boards (Grisel too, if escorting), and ride 5 departs.
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
      - the train is home (blend ≤ 0.01) by the back's points + 8.1 m, with no fall, and arrives at stop 5.
    - **`=wrong`.** Nobody presses. It fails unless:
      - the last call comes 5–6.5 m before the points;
      - the train passes the points straight and falls with its front within 0.6 m of the lip, logging `FallLog`;
      - every rider is fallen within 1 s;
      - the wipe rises at stop 4, with every hero up within 10 m of a whole cart.
    - **`=lone`.**
      - Pass 1: the lead, the only human, is downed in the seat before the window (held down until the points, possession off). At the points the train takes the safe line with the lever straight, logging `SafeLineLog`. It comes home and arrives at stop 5 with no fall.
      - Pass 2 (restored at stop 4): the lead is seated and up in W2 (`rc.DebugSeat(lead, 2)`). That is not able (`AbleToThrow` reads the cart), so it takes the same safe line.
    - **`=stall`.** As `=wrong`, but `DebugStall` catches the train four times:
      - 25 m before the lever: set at the window's start, still rolling;
      - 8 m before the lever: set at the last call's mark, and the last call is heard after it;
      - 3 m short of the points: set at the points and taken straight;
      - 5 m past the points: ground into the pit, where it falls.

      It is never carried past the pit.
    - **`=late`** (Review Focus 5). The lead's press arrives on the tick the front reaches the points, and again 1 m past them. Each gets "Too late to throw it.", the lever stays straight and the train falls.
  - **The re-pointed flows** (B.10):
    - **`--test-cart-watchdog` step 3.** The train, caught with its front 25 m before the pit lever, is set at the window's start still rolling. The lead throws from the seat, and the train comes home past the far edge and arrives at stop 5. The RESULT line names the pit.
    - **`--test-chase`.** Ride 5's jump check becomes:
      - `[chase] 4 … the pit switch thrown at front S={f}, across from {points}, home by {home}, no fall`, with `RoundLog` logged;
      - the hazard stamp `pit` comes from `BarkTrigger.PitSwitch` (it was `jump` from `JumpCall`);
      - section 2's one-pumper jump becomes `PumperCap` 1 with W1..W4 and the pit thrown: no fall, arrival at stop 5;
      - "The cart leaps the gap!" is no longer expected.
    - **`--test-crevasse` B, C, `=trestle`, `=survivor`.** `LeavePitStraight`, and nobody presses with the lead able. The train runs into the pit and falls (`FallLog`), with W1's crew seated (`DebugSeatCrew(1)`). Every other check is as today.
    - **`--test-cavern`.** The party track is bare over `r5_pit`'s span (`PitS`..`PitFarS`), in place of `r5_jump`'s gap.
    - **`--test-boarders=groups`.** It rides through `RideOnTo`, which throws the pit. `r5_hounds`' gate becomes:
      - its cart spawned with the train's rear past the back's points + `SwitchStretch`, its enemy S at least 2 m past that;
      - it leapt before the front reached `r5_golem`'s S − 2;
      - the plan-14 gates still hold.

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

[Fact] public void APressOnThePointsOwnTick_IsTooLate()
{
    Assert.Equal(LatePress.TooLate, SwitchBackRules.Late(taken: false, thrown: false, front: 1065f, pointsS: 1065f));
    Assert.Equal(LatePress.None, SwitchBackRules.Late(false, false, 1064.99f, 1065f));
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
  - **`RailEncounterPlanTests`.** The set-piece id list reads `r5_pit` in `r5_jump`'s place and ends with `"r5_switch_out", "r5_switch_back"`, and the count is 23. The ride-5 At order is `switch_out < pit < switch_back < pour < trestle < hammers < spur < barricade < buffer`, with `r5_switch_back.At < r5_hounds.At < r5_golem.At`. `r5_switch_out` is `PitRules.Deadly` against `r5_pit` by At.
  - **`TrackStationsTests`.** `Floor12`'s second gap is the measured (lip, far). The lattice test keeps (1156.0, 1161.0) as a synthetic on-lattice gap.
  - **`RideBarksTests`.** `PitSwitch` and `PitSwitchNow` are appended to the triggers and to Shout: 3 per name, 2 per class, 3 generic.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~PitRulesTests|FullyQualifiedName~SetPieceRules|FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~TrackStationsTests|FullyQualifiedName~RideBarksTests"`. Expected: a compile error.
- [ ] **Step 3: Implement and place.**
  - Implement the Interfaces, then write the data from Task 3's lip and far edge.
  - Run Task 3's `--print-map --track-depth` command. The pieces must read their placed S ± 0.1: `r5_switch_out` lip − 8.0, `r5_pit` the lip, `r5_switch_back` far + 1.0, `r5_hammers` 1154.0/1157.0/1160.0 and `r5_hounds` Ruling 13's. The build must also print `fall armed`.
  - Then run `--test-pit` and `--test-boarders=groups`.
  - **Stop and report** without retuning if:
    - a placed S is off by more than 0.1;
    - the early call falls outside 25–30 m;
    - the sight ray is blocked;
    - the guard disarms;
    - a `=groups` gate fails. One retune is allowed here, Ruling 13's Offset.
  - Every flow that rides ride 5 to its end must print its `pit: thrown` line or be a fall test. List them in the report: `grep -ln "RideOnTo\|LastRide\|shape.Rides" src/Core/DebugTools*.cs`.
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
- [ ] **Step 5: Commit**

```bash
git add data/adventures/builtin/floor_12.json src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs src/Dungeon/Rails/Core/PitRules.cs src/Dungeon/Rails/RailEncounterController.Pit.cs src/Dungeon/Rails/SetPieces/LavaPit.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.Traffic.cs src/Dungeon/Rails/RailController.Watchdog.cs src/Dungeon/Rails/RailController.Jump.cs src/Dungeon/Rails/Core/SetPieceRules.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Core/DebugTools.cs src/Core/DebugTools.RideOn.cs src/Core/DebugTools.Pit.cs src/Core/DebugTools.Chase.cs src/Core/DebugTools.Crevasse.cs src/Core/DebugTools.CartWatchdog.cs src/Core/DebugTools.Cavern.cs src/Core/DebugTools.Boarders.Groups.cs
git commit -m "feat(rails): ride 5's lava pit has a deadly switch round it: a human's lever in the seated window, called 30 m before it and 6 m before the points, the safe line with no able human aboard, too late past the points; a fixed back brings the train home past the far edge, a train meeting the lip on its own lane falls into the lava, and the watchdog steps a stuck train down without carrying it past; r5_jump becomes r5_pit, the hammers move to the old jump straight and r5_hounds clears the crossing; --test-pit

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
    - `data/adventures/builtin/floor_12.json`: `r5_barricade` → `{ "Id": "r5_parked", "Ride": 5, "Kind": "parked", "At": f(rear) }`;
    - `src/Adventure/Core/Validate/PlanValidator.cs` (the kind `parked`), `src/Adventure/Core/Plans/FloorPlan.cs` (doc).
  - Encounters:
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (the `parked` case and its tick);
    - `src/Dungeon/Rails/RailEncounterController.Barricades.cs` (`SmashCost`, shared).
  - Rails: `src/Dungeon/Rails/TrainWagon.cs` (`WireRef`), `src/Dungeon/Rails/RailController.cs` (a Restore stands it).
  - Net: `src/Net/Core/NetRefs.cs` (`ParkedKind`, `Parked(piece)`, `LiftWagon(k)`), `src/Net/Core/CartNet.cs` (`CartIds.LiftBase`, `Lift`, `LiftWagon`), `src/Net/NetRefResolver.cs`, `src/Net/ClientWorld.cs`, `src/Net/PropSync.cs`.
  - Barks: `src/Adventure/Core/Plans/CampaignPlan.cs` (`ParkedCart`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Adventure/Core/Voice/VoiceLines.cs`.
  - Flows: `src/Core/DebugTools.Island.cs` (the variant), `src/Core/DebugTools.Barricade.cs` (r5 dropped), `src/Core/DebugTools.Chase.cs` (ride 5's smash), `src/Core/DebugTools.cs` (`[map] rail island:` adds the parked place).
- Test: `tests/Dungeon/Rails/ParkedRulesTests.cs`, `tests/Adventure/RailEncounterPlanTests.cs`, `tests/Adventure/RideBarksTests.cs`, `tests/Net/NetRefsTests.cs`

**Interfaces:**
- Consumes: Task 3's `ExitFromS`/`ExitToS`, `LastWreckInside` and `IslandSetDown`; `BarricadeRules`; plan 14's `TrainRules.Length` and `TrainWagon`; plan 11's heap alarm (`SetPieceRules.HeapAlarm`) and its bell.
- Produces:
  - **`ParkedRules`** (engine-free):
    - `enum ParkedState { InTheWay, Smashed, Lift }`;
    - the numbers: `FrontShort = 1f`, `CallAt = 35f`, `AtSlack = 2f`, `TumbleSeconds = 6f`, `SmashDent = BarricadeRules.SmashDent`, `SpeedKept = BarricadeRules.SpeedKept`;
    - `float FrontS(float bridgeFromS) => bridgeFromS - FrontShort`;
    - `float CartS(float bridgeFromS) => FrontS(bridgeFromS) - CartRules.HalfLength`;
    - `float RearS(float bridgeFromS, int wagons) => FrontS(bridgeFromS) - TrainRules.Length(wagons)`;
    - `bool Smashes(float prevFront, float front, float rearFaceS) => prevFront < rearFaceS && rearFaceS <= front`;
    - `bool CallDue(bool rolling, float front, float rearFaceS, bool called) => rolling && !called && front >= rearFaceS - CallAt && front < rearFaceS`;
    - `bool LiftDue(float partyRearS, float bridgeToS, bool heldAtLast, bool liveCart, bool wreckedInside) => partyRearS > bridgeToS || heldAtLast || !liveCart || wreckedInside`;
    - `ParkedState Next(ParkedState state, bool smashed, bool liftDue)`: Lift when due; else Smashed once smashed (until due); else InTheWay;
    - `string Prompt(ParkedState state, bool chaseLive, bool onFootPlayer, bool inReach)`: "Board the cart" only in Lift with no chase live, for an on-foot player in reach; else "".
  - **The ids** (Ruling 27). `CartIds.LiftBase = 2100`, `CartIds.Lift = 2100` and `CartIds.LiftWagon(int k) => LiftBase + k`; `NetRefs.LiftWagon(int k) => "liftwagon:{k}"`.
  - **`ParkedTrain : Node3D`** (Ruling 20; every role):
    - `MineCart Cart` (`AsChaser()`, `CartId = CartIds.Lift`) and `IReadOnlyList<TrainWagon> Wagons` (Numbers 1..4, `WireRef = NetRefs.LiftWagon(k)`);
    - `Pose(float cartS, TrackSampler party)`: the wagons at cart S − 2.6·k;
    - `Stand()`: shown, solid layer 1|8, lantern lit;
    - `Smash(Vector3 velocity)`: five tumbles (layer 0, freed after `TumbleSeconds`) and the train hidden with no collision;
    - `bool Standing`, the `RearLantern` node, `Alarm` (blink, as `OreHeap.Alarm`).
  - **`RailEncounterController`** (`.Parked.cs`):
    - `BuildParked(Node3D root, Piece piece)` (kind `parked`): from the layout's bridge. It is live when `|piece.S − RearS| ≤ AtSlack`; otherwise it is a dead marker with an ERROR line.
    - It prints `[rails] parked {id}: rear S={rear:0.0}, cart S={cart:0.0}, front S={front:0.0} (1.0 m short of the exit bridge at {from:0.0}), a cart and 4 wagons`.
    - `ParkedState ParkedNow` (every role; a client's from `PropEvt`).
    - The server's tick, while ride 5 is armed and the parked train is `InTheWay`:
      - `CallDue` → `RailCalls.Say(…, BarkTrigger.ParkedCart, "Cart on the line — brace!")`, printed `[rails] parked {id} called at front S={f:0.0} ({d:0.0} m to its rear)`;
      - its alarm blinks by `HeapAlarm`, with `cart_bell` at its start;
      - `Smashes` → Ruling 21, printed `[rails] the cart smashes the parked train at S={f:0.0}: plating {a:0} -> {b:0}, {n} hero(es) hurt, V {v0:0.0} -> {v1:0.0}`, then `PropEvt(NetRefs.Parked(i), "smashed")`.
    - Every tick: `Next(…)`. On a change to Lift it logs "Another cart rolls out onto the line by the lava." and sends `PropEvt "standing"`.
    - It stands (InTheWay) at ride 5's arming and on `Restored`.
    - `internal int ParkedSmashes` and `internal void DebugSeatParked(Adventurer)` (a test hook).
  - **`RailEncounterController.SmashCost(string what, bool partyCart)`**: `r1_barricade`, `r2_barricade` and the parked train all smash through it (Ruling 21).
  - **The client** applies `PropEvt parked:{i}` ("smashed" plays the tumbles; "standing" stands it). The catch-up sends its state.
  - **`BarkTrigger.ParkedCart`** is appended after `PitSwitchNow`, Shout.
  - **`--test-island=parked`.** `rc.Restore(4)`, everyone boards, the pit is thrown on the way, and every enemy is culled. It fails unless:
    - **The build.** The rear lies at `RearS(ExitFromS, 4)` ± 0.1, and a layer-1 ray from the seated lead's eye at rear − 15 m to its lantern is clear.
    - **In the way.** At stop 4, held, the lead teleported beside it is offered "" and no "Board the cart".
    - **The call.** The call (bark or log) comes with the front 33–35 m from the rear, the alarm turns on, and the bell rings.
    - **The race.** A companion seated in its cart by `DebugSeatParked` before the smash is thrown clear at the smash onto a walkable non-drop cell, HP ≥ 1, never fallen.
    - **The smash.** The plating drops by 25, each other seated up hero loses 15% of max HP (one set to 5% beforehand ends with ≥ 1 HP), the crew's HP is unchanged, and V is × 0.8 ± 0.05. Five tumbles show, and `ParkedNow` is Smashed.
    - **Standing again.** Within 1 s of the train's rear passing `ExitToS`, `ParkedNow` is Lift and "Another cart rolls out…" is logged.
    - **A restore.** `rc.Restore(4)` stands it InTheWay again.
  - **`--test-barricade`** drops `r5_barricade` (r1, r2 and the slag one remain). **`--test-chase`**'s ride-5 smash check reads the parked train (`ParkedSmashes == 1`, the call stamped `parked`).
  - **`--print-map`'s island line** adds `, the parked train rear S={r:0.0} .. front S={f:0.0}`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheParkedTrain_StandsAMetreShortOfTheBridge_TwelvePointSixLong()
{
    Assert.Equal(1182.9f, ParkedRules.FrontS(bridgeFromS: 1183.9f), 3);
    Assert.Equal(1181.8f, ParkedRules.CartS(1183.9f), 3);
    Assert.Equal(1170.3f, ParkedRules.RearS(1183.9f, wagons: 4), 3);
}

[Fact] public void ItIsInTheWay_ThenSmashed_ThenALift()
{
    Assert.Equal(ParkedState.InTheWay, ParkedRules.Next(ParkedState.InTheWay, smashed: false, liftDue: false));
    Assert.Equal(ParkedState.Smashed, ParkedRules.Next(ParkedState.InTheWay, smashed: true, liftDue: false));
    Assert.Equal(ParkedState.Smashed, ParkedRules.Next(ParkedState.Smashed, false, false));
    Assert.Equal(ParkedState.Lift, ParkedRules.Next(ParkedState.Smashed, false, liftDue: true));
    Assert.Equal(ParkedState.Lift, ParkedRules.Next(ParkedState.InTheWay, false, liftDue: true));
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
    Assert.Equal("", ParkedRules.Prompt(ParkedState.InTheWay, chaseLive: false, onFootPlayer: true, inReach: true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Smashed, false, true, true));
}
```

  Then update the other tests:
  - **`RailEncounterPlanTests`.** `r5_parked` stands in `r5_barricade`'s place, and the count stays 23. The ride-5 order is final: `switch_out < pit < switch_back < pour < trestle < hammers < parked < spur < buffer`, with the encounters `switch_back < r5_hounds < r5_golem`. The barricades are r1 and r2 only.
  - **`RideBarksTests`.** `ParkedCart` is appended.
  - **`NetRefsTests`.** It gains `(NetRefs.Parked(15), "parked", "15")` and `(NetRefs.LiftWagon(2), "liftwagon", "2")`.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~ParkedRulesTests|FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~RideBarksTests|FullyQualifiedName~NetRefsTests"`. Expected: a compile error.
- [ ] **Step 3: Implement and place.**
  - Implement the Interfaces. Write `r5_parked`'s At from `RearS(ExitFromS, 4)` (Ruling 12).
  - Run Task 3's `--print-map --track-depth` command. The parked piece's S must be the rear ± 0.1, and every other piece S unchanged.
  - **Stop and report** if not, or if the 15 m sight ray is blocked.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`:
  - `--test-chase --quit-after=750`;
  - `--test-island=parked`, `=bridge`, `=wreck`;
  - `--test-barricade`, `--test-pit`, `--test-crew --quit-after=570`, `--test-boarders=groups`.

  Each must PASS. Then run plain `scripts/net_test.sh`: `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add data/adventures/builtin/floor_12.json src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs src/Dungeon/Rails/Core/ParkedRules.cs src/Dungeon/Rails/SetPieces/ParkedTrain.cs src/Dungeon/Rails/RailEncounterController.Parked.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Barricades.cs src/Dungeon/Rails/TrainWagon.cs src/Dungeon/Rails/RailController.cs src/Net/Core/NetRefs.cs src/Net/Core/CartNet.cs src/Net/NetRefResolver.cs src/Net/ClientWorld.cs src/Net/PropSync.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Core/DebugTools.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Parked.cs src/Core/DebugTools.Barricade.cs src/Core/DebugTools.Chase.cs
git commit -m "feat(rails): a parked train stands a metre short of the exit bridge (r5_parked in r5_barricade's place): called at 35 m with its lantern and bell, smashed at a barricade's cost (riders thrown clear onto the bank, five tubs tumbling), and standing again as a lift once the train is past the bridge, at the end, or without a cart; PropEvt parked standing/smashed

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: The lift (in for anyone left outside; the takeover with no cart left)

**Files:**
- Create: `src/Dungeon/Rails/RailController.Lift.cs`, `src/Core/DebugTools.Island.Lift.cs`
- Modify:
  - Rails:
    - `src/Dungeon/Rails/Core/ParkedRules.cs` (the lift's limit, takeover and save pose);
    - `src/Dungeon/Rails/Core/SpareRules.cs` (the four-argument `ChaseLimit`);
    - `src/Dungeon/Rails/RailController.Chase.cs` (`Chase.Lift`; the limit and `ChaseDone` at the lift; one chase at a time);
    - `src/Dungeon/Rails/RailController.Relief.cs` (the limit);
    - `src/Dungeon/Rails/RailController.cs` (`TakeOverAt`; the lift's riders held over the bridge);
    - `src/Dungeon/Rails/SetPieces/ParkedTrain.cs` (the lift's prompt and use);
    - `src/Dungeon/Rails/RailEncounterController.Parked.cs` (`ArmFrom`; the lift's run's hand-off);
    - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (a departure armed from S).
  - Net:
    - `src/Dungeon/Rails/RailNet.Carts.cs` (the client drives its `ParkedTrain`);
    - `src/Dungeon/Rails/RailNet.Seats.cs` (the lift and its wagons heard and resolved, ahead of the chaser range);
    - `src/Dungeon/Rails/RailNet.cs` (the `L` book; the lift's `CartStateDto`);
    - `src/Net/ServerWorld.cs` (the lift's use passes the reach check).
  - Saves and crew:
    - `src/Party/PartyController.cs`, `src/Save/FloorStateCapture.cs` (`SavePose`);
    - `src/Adventure/Objectives/CrewRescueController.cs` (crew on foot seated in the lift's wagons at its first board).
  - Flows: `src/Core/DebugTools.Island.cs` (the variants).
- Test: `tests/Dungeon/Rails/ParkedRulesTests.cs`, `tests/Dungeon/Rails/SpareRulesTests.cs`

**Interfaces:**
- Consumes: Task 5's `ParkedTrain`, `ParkedNow` and `ParkedRules`; Task 3's `IslandSetDown`, `ExitBridgeHolds`'s rule and `LastWreckInside`; the chaser (`Chase`, `CartRun`, `Boarding`, `SpareRules.HideSeconds`, `ChaserPrompt`); plan 14's `CoupleFresh`, `TrainRules.SeatOrder` and `BoardReach`.
- Produces:
  - **`ParkedRules`:**
    - `float LiftLimit(float heldRearS, float bufferFaceS, float lastStopS)`: the nearest of held rear − `SpareRules.ChaseGap`, buffer face − `CartRules.HalfLength` and the last stop (NaN ignored);
    - `bool TakesOver(bool partyLiveCart) => !partyLiveCart`;
    - `float SavePoseS(float liftCartS, float liftRearS, float bridgeToS, float parkedCartS) => liftRearS > bridgeToS ? liftCartS : parkedCartS`: the save keeps a lift rider where the lift was only once all of it is past the far edge, and otherwise beside the parked place (Ruling 28).
  - **`SpareRules.ChaseLimit(float trainBackS, float nextStopS, float liftRearS, float bridgeFromS)`** gives the nearest of the two-argument one, lift rear − `ChaseGap` and bridge from − `CartRules.HalfLength` − `BridgeClear` (`BridgeClear = 0.5f`), with NaN ignored.
  - **`RailController`** (`.Lift.cs`):
    - `MineCart? Lift` (the lift's cart while it runs, server and solo);
    - `bool BoardLift(Adventurer player)`: Ruling 23–25. It takes over when `TakesOver`; otherwise it seats the player, their own companions within 25 m, escorts on the bench, and crew on foot within 25 m in its wagons. It prints:
      - `[rails] lift: {name} boards ({n} companion(s), {c} crew, {e} escort(s))`;
      - `[rails] lift: the party has no live cart: it takes over at S={s:0.0}`;
    - the run: as the chaser's, with no pumping and no set piece judged, its limit `LiftLimit`. At the halt it prints `[rails] lift: halted at S={s:0.0}, {d:0.0} m behind the held train's rear; {n} rider(s) set down inside`, sets its riders down on `IslandSetDown` spots, and is hidden `SpareRules.HideSeconds` later. Then `ParkedNow` re-evaluates and it stands again.
    - while any of the lift's bodies is over the span (`ExitBridgeRules.Holds`), its riders get no Get out;
    - `void TakeOverAt(float cartS, Adventurer presser)`: the party's cart stands Held at `cartS` with full plating, four fresh wagons (`CoupleFresh("the lift takes over")`), `StopIndex` 4 and the presser seated. `Arrived` is not raised; the HUD reads held. It prints `[rails] lift: the party's cart stands at S={s:0.0} (stop {k} kept)`.
    - `Vector3 SavePose(ICartRider r, Vector3 at)`: `SavePoseS`'s spot for a lift rider (`StandBesideLane` there, or an `IslandSetDown` spot), else `at`;
    - `internal bool DebugBoardLift(Adventurer a)` (a test hook: a press as a remote's would be) and `internal void DebugSpendSpares()` (a test hook: every spare marked used, as a save with them spent loads).
  - **`RailEncounterController.ArmFrom(int ride, float s)`.** A departure from a takeover's S arms the ride with every piece of S < s `Fired` and every encounter of start S < s started. It prints `[rails] ride {r} armed from S={s:0.0}: {p} piece(s) and {e} encounter(s) short of it passed`.
  - **The chaser and the relief** read the four-argument `ChaseLimit` with the standing lift's rear and `ExitFromS`. A chaser that comes up against the lift ends its chase (`ChaseDone` at the lift), and its server line reads `chase-done: the chaser halted {d:0.0} m behind the lift's rear at S={s:0.0}, outside the island; it hopped {n} hole(s) in the line`.
  - **On the wire** (Task 5's ids).
    - The lift's state is `Kind` Chaser, `StopIndex` −1, `Train` [1, 2, 3, 4] and `CrewWagons` 4.
    - Its seats and its wagons' are `SeatMsg`s under those ids.
    - A client resolves `CartIds.Lift..LiftWagon(4)` before the chaser range and follows the lift with its own `ParkedTrain`.
    - The RIDE book gains `L` (the lift and its wagons).
  - **`--test-island=lift`.** The party rides to stop 5 (the pit thrown on the way). The lead and one companion are teleported beside the parked train, outside, and one freed crew member is put on foot within 20 m of it. It fails unless:
    - **The prompt.** "Board the cart" is offered to the lead.
    - **The boarding.** The press seats the lead, hops the companion in within 25 m, and seats the crew member in W1 seat 4 at once.
    - **The countdown.** It runs 5 s, with Get out closed for 2 s.
    - **The bridge.** While the lift is over the span, the prompt is "" and a forced Get out does nothing.
    - **The save** (Review Focus 1). A save at mid-bridge and a load leave the lead beside the parked train outside, up and not fallen, and the lift standing; the lead boards again.
    - **The halt.** The lift halts 1.5 ± 1.5 m behind the held train's rear inside, every rider is set down on island cells, and it is hidden 2 s later and standing again 1 s after that.
  - **`--test-island=takeover`.** `rc.Restore(4)`, then `rc.DebugSpendSpares()`. The cart is wrecked at front S ≈ 1100 ("No carts left. The rest is on foot."), and the party is teleported beside the parked train. It fails unless:
    - **The double press** (Review Focus 4). The lead's press and `DebugBoardLift(companion)` land in one tick. Exactly one takeover is printed, both sit in the party's cart and nobody is seated in the lift.
    - **The new cart.** The party's cart stands Held at the lift's cart S with W1..W4 coupled fresh and `StopIndex` 4.
    - **The departure.** It prints `armed from S=…`, with the golem, the hounds and the pit counted as passed.
    - **The ride.** The train crosses the bridge and arrives at stop 5 as the party's train.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheLift_OffersBoarding_OnlyWithNoChaseLive()
{
    Assert.Equal("Board the cart", ParkedRules.Prompt(ParkedState.Lift, chaseLive: false, onFootPlayer: true, inReach: true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Lift, chaseLive: true, true, true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Lift, false, onFootPlayer: false, true));
    Assert.Equal("", ParkedRules.Prompt(ParkedState.Lift, false, true, inReach: false));
}

[Fact] public void TheLift_HaltsBehindTheHeldTrain_OrShortOfTheBuffer_OrAtTheLastStop()
{
    Assert.Equal(1218.7f, ParkedRules.LiftLimit(heldRearS: 1220.2f, bufferFaceS: float.NaN, lastStopS: 1231.7f), 3);
    Assert.Equal(1220.2f, ParkedRules.LiftLimit(float.NaN, bufferFaceS: 1221.3f, 1231.7f), 3);
    Assert.Equal(1231.7f, ParkedRules.LiftLimit(float.NaN, float.NaN, 1231.7f), 3);
    Assert.True(ParkedRules.TakesOver(partyLiveCart: false));
    Assert.False(ParkedRules.TakesOver(true));
}

[Fact] public void ALiftRidersSavedPose_IsNeverOverTheBridge()
{
    Assert.Equal(1181.8f, ParkedRules.SavePoseS(liftCartS: 1195f, liftRearS: 1183.5f, bridgeToS: 1199.9f, parkedCartS: 1181.8f), 3);
    Assert.Equal(1214f, ParkedRules.SavePoseS(1214f, liftRearS: 1202.5f, 1199.9f, 1181.8f), 3);
}

[Fact] public void AChaser_HaltsBehindTheLift_AndNeverOnTheBridge()
{
    Assert.Equal(1168.8f, SpareRules.ChaseLimit(trainBackS: 1220.2f, nextStopS: 1231.7f, liftRearS: 1170.3f, bridgeFromS: 1183.9f), 3);
    Assert.Equal(1182.3f, SpareRules.ChaseLimit(1220.2f, 1231.7f, liftRearS: float.NaN, 1183.9f), 3);
    Assert.Equal(1098.5f, SpareRules.ChaseLimit(1100f, 1231.7f, float.NaN, 1183.9f), 3);
    Assert.Equal(SpareRules.ChaseLimit(900f, 1022.5f), SpareRules.ChaseLimit(900f, 1022.5f, float.NaN, float.NaN));
}
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~ParkedRulesTests|FullyQualifiedName~SpareRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`:
  - `--test-chase --quit-after=750`, `--test-crew --quit-after=570`;
  - `--test-island=lift`, `=takeover`, `=parked`, `=wreck`;
  - `--test-relief`, `=rails`, `=refuse`, `=ahead`;
  - `--test-crevasse=survivor`.

  Each must PASS. Then run plain `scripts/net_test.sh`: `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/RailController.Lift.cs src/Dungeon/Rails/Core/ParkedRules.cs src/Dungeon/Rails/Core/SpareRules.cs src/Dungeon/Rails/RailController.Chase.cs src/Dungeon/Rails/RailController.Relief.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/SetPieces/ParkedTrain.cs src/Dungeon/Rails/RailEncounterController.Parked.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailNet.Carts.cs src/Dungeon/Rails/RailNet.Seats.cs src/Dungeon/Rails/RailNet.cs src/Net/ServerWorld.cs src/Party/PartyController.cs src/Save/FloorStateCapture.cs src/Adventure/Objectives/CrewRescueController.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Lift.cs
git commit -m "feat(rails): the parked train is a lift: Board the cart seats a player left outside with their companions, escorts and crew on foot, and it runs like a chaser over the bridge (no Get out on it) to halt 1.5 m behind the held train inside, set everyone down and go; with no live cart left it takes over as the party's train, armed past what it skipped; chasers and reliefs halt behind it, never on the bridge; a save never leaves a rider over the lava

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: Home and lost on an island floor (and the win's reward chest)

**Files:**
- Create: `src/Adventure/Objectives/CrewRescueController.Island.cs`, `src/Core/DebugTools.Island.Home.cs`
- Modify:
  - Crew: `src/Adventure/Core/Objectives/CrewRules.cs`, `src/Adventure/Objectives/CrewRescueController.cs` (`Judge`, `Evaluate`, `Win`).
  - Save: `src/Save/Core/SaveDtos.cs` (`ObjectiveStateDto.RewardChest`), `src/Save/Core/RailArrival.cs`.
  - Net: `src/Net/ClientWorld.cs` (the chest's "stood"), `src/Net/PropSync.cs` (its catch-up).
  - Flows: `src/Core/DebugTools.Island.cs` (the variants).
- Test: `tests/Adventure/CrewRulesTests.cs`, `tests/Save/SaveRunFieldsTests.cs`, `tests/Save/RailArrivalTests.cs`

**Interfaces:**
- Consumes: Task 3's `FloorLayout.InIsland` and `LastWreckInside`; Task 6's lift set-down; plan 7's `RailController.CrewLeftAt`; `ChestRules.VaultTable`; the chest scene and `OpenedChests`.
- Produces:
  - **`CrewRules`:**
    - `const int RewardChestIndex = 9000`;
    - `bool Stranded(bool onFoot, bool inIsland, bool trainAtEndOrNone, int upPlayersOutside) => onFoot && !inIsland && trainAtEndOrNone && upPlayersOutside == 0`;
    - `bool UnopenedLeftBehind(int upPlayers, int upPlayersInside, bool trainAtEndOrWreckedInside) => upPlayers > 0 && upPlayersInside == upPlayers && trainAtEndOrWreckedInside`;
    - `bool BringEscortIn(bool escortOutside, int upPlayersOutside, bool trainAtEnd) => escortOutside && upPlayersOutside == 0 && trainAtEnd`;
    - `CrewState JudgeOnIsland(bool homeAlready, bool downed, bool inIsland, bool seatedAtEnd, bool stranded)`, judged in this order: Home when already home and (not downed or in the island); else Downed when downed; else Home when in the island or seated at the end; else Lost when stranded; else Freed. (The caged and the dead are judged before it, as today.)
  - **Test hooks** (server and solo): `internal void DebugPutOnFoot(string crewId, Vector3 at)` (unseated, set down there and following) and `internal void DebugDown(string crewId)` (downed where it is).
  - **`CrewRescueController`** (`.Island.cs`, server and solo, only on a floor with `Rail.Island`):
    - **`Judge`.** It reads `JudgeOnIsland`, with `InIsland` for `InExitRoom` (Ruling 29). "No train" means wrecked and hidden.
    - **`ResolveUnopened()`** does Ruling 30.
    - **`BringEscortsIn()`.** Ruling 32, printed `[CrewRescue] island: {name} brought in beside the held train at {cell}`.
    - **`Win`.** On an island floor it does the following:
      - stands the reward chest (Ruling 33), printed `[CrewRescue] the reward chest stands in room {r} at {cell} (table {t})`;
      - sends `PropEvt(NetRefs.Chest(RewardChestIndex), "stood")` and logs the chest's line in place of "Somewhere on this floor, a vault door swings wide for you.";
      - still reveals and opens room 10's door.
    - **The save.** `Capture` writes `RewardChest` (true once stood, else null), and `Restore` stands the chest again.
  - **`ObjectiveStateDto.RewardChest`** (`bool?`). `RailArrival.Merge` takes it from the live state only with the live state's `Completed`, as the win's other fields are.
  - **A client** builds the chest on "stood" (and at the catch-up), from the same cell rule.
  - **`--test-island=onfoot`** (B.10's on-foot win). `rc.Restore(2)`, with stops 1–2's cages opened and their crew seated (`DebugSeatCrew(1)`, `(2)`) and every spare spent. The cart is wrecked on ride 3: on foot, and the crew are let out to follow. The party and the crew are teleported beside the parked train. It fails unless:
    - **The ride in.** The lift takes over and rides in, with the crew seated in W1, W2 (its wagons), and arrives at stop 5.
    - **The cages.** Once every up player is inside, `[CrewRescue] island: cage at stop 3 left behind` and `… stop 4 …` print, and their crew are lost.
    - **The win.** Stops 1–2's crew are home. The win fires: `jailbreak_freed_all`, the stairs unsealed and the chest stood.
  - **`--test-island=stranded`** (Review Focus 3). The party rides to stop 5 with one crew member put on foot outside (`DebugPutOnFoot`, beside the parked train) and one crew member downed in the island (`DebugDown`, which holds off the win). It fails unless:
    - **Stranded.** The outsider is Lost within 1 s.
    - **Live.** With the lead teleported outside it is Freed within 1 s, and with the lead back inside it is Lost again.
    - **The win stands.** Reviving the downed one fires the win, and teleporting the lead outside afterwards leaves the win standing (`Completed`).
  - **`--test-island=grisel`.** Grisel is forgiven and escorting; she is left outside (teleported to ride 5's lane at S 1150), and the party rides to stop 5. It fails unless:
    - **Brought in.** Within 1 s of every up player being inside with the train held at stop 5, she stands on an island cell beside the train's rear, and the line is logged.
    - **Her quest.** She walks to the down stairs, and `q_grisel_out` completes.
  - **`--test-island=chest`.** Every cage is opened and its crew seated by hooks, and the party rides to stop 5. It fails unless:
    - **The chest.** Within 1 s of the win the reward chest stands in room 9 with `TableId` `table.vault`; the lead opens it and an item drops; the line is logged.
    - **Room 10.** Its door is revealed and open.
    - **The save.** A save and a load keep the chest, opened.
    - **The gathering.** With gathering on and the party in the safe room 40 s, a group gathers inside the island and no ERROR line prints.

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
    Assert.Equal(CrewState.Downed, CrewRules.JudgeOnIsland(false, downed: true, false, false, false));
    Assert.True(CrewRules.Completed(new[] { CrewState.Home, CrewState.Lost }, cagesClosed: 0)); // the win, once given, is not judged again
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
    Assert.True(CrewRules.BringEscortIn(escortOutside: true, upPlayersOutside: 0, trainAtEnd: true));
    Assert.False(CrewRules.BringEscortIn(true, 1, true));
    Assert.False(CrewRules.BringEscortIn(true, 0, trainAtEnd: false));
}
```

  Then update the other tests:
  - **`SaveRunFieldsTests.RewardChest_RoundTrip`.** It is null on a new state and on one from before the field, and true survives a save.
  - **`RailArrivalTests`.** A wipe merged with the live `Completed` false drops a live `RewardChest`.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~CrewRulesTests|FullyQualifiedName~SaveRunFieldsTests|FullyQualifiedName~RailArrivalTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces.
- [ ] **Step 4: Run them.** Run the filter, then the full suite: PASS. Then through `scripts/flows.sh`:
  - `--test-crew --quit-after=570`, `--test-crew=forgiven --quit-after=570`, `--test-chase --quit-after=750`;
  - `--test-island=onfoot`, `=stranded`, `=grisel`, `=chest`, `=lift`, `=takeover`, `=wreck`.

  Each must PASS. Then run plain `scripts/net_test.sh`: `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Adventure/Core/Objectives/CrewRules.cs src/Adventure/Objectives/CrewRescueController.cs src/Adventure/Objectives/CrewRescueController.Island.cs src/Save/Core/SaveDtos.cs src/Save/Core/RailArrival.cs src/Net/ClientWorld.cs src/Net/PropSync.cs src/Core/DebugTools.Island.cs src/Core/DebugTools.Island.Home.cs
git commit -m "feat(crew): on an island floor the prisoners are home anywhere in the island; once every player is inside with the train at the end, unopened cages are left behind; crew on foot outside with nobody outside to fetch them are lost while that lasts; Grisel is brought in; the win stands a strongbox of the vault's table by the stairs (saved, sent as a PropEvt)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: The RIDE pass for the island (the builder writes it; the controller runs it)

**Files:**
- Modify: `src/Net/NetTestFlow.cs`, `scripts/net_test.sh` (the checks and the header)

**Interfaces:**
- Consumes: Tasks 3–7; plan 14 Task 7's rewritten pass (`RideTrainCheck`, the move, the knock-off, the throw); the hooks `DebugWreck`, `DebugRestoreAt` (new, server: `rc.Restore(stop)` as a load would, with the spares used kept) and `DebugBoardLift`.
- Produces, each with its own `NET TEST: FAIL (…)` line:
  - **A remote seat throws the pit switch.** On ride 5, the first client presses on the first frame its seat offers "Throw the switch" for `r5_switch_out`.
    - The server prints `interact switch:{i} from Rider: accepted in the seated window` and `switch r5_switch_out taken … across from …`.
    - Both clients print `[net] ride: across round the pit here (CrossOut {a:0.0}, CrossBack {b:0.0})`, with the server's values within 0.1.
    - No `runs into the pit` prints anywhere.
    - The old jump checks (two pumpers, `leaps the gap`) go.
  - **No remote Get out over the bridge.** While the first client's train has a car over the span, it sends `InteractIntent(cart:0)`.
    - The server prints `interact cart:0 from Rider: refused (the bridge holds its riders)`.
    - The client's prompt read "" on every frame over the span, and it stays seated.
  - **The parked train's smash, seen on a client.** The server prints the smash. Each client prints `[net] ride: parked: smashed here (PropEvt) {t:0.000}, plating {p:0}` within 1 s of the server's line, with the plating equal to the server's.
  - **Step 8 becomes two rides** (B.10).
    - **The chaser.** The joiner takes `spare:4` as today. Its chaser hops at least one hole (the pit, or the fallen trestle) and halts within 3 m of the lift's rear, outside: `chase-done: … behind the lift's rear … outside the island … hopped [1-9] hole(s)`. Its riders step out.
    - **The lift.** The joiner presses "Board the cart" through `InteractIntent(parked:{i})`. The lift halts within 3 m of the held train's rear, inside: `[net-server] ride: lift halted … behind the held train's rear; 1 rider(s) set down inside`. The joiner prints `[net] ride: the lift set us down inside the island at {cell}`.
    - "the chaser never hopped ride 5's gap" now reads "… ride 5's pit or the fallen trestle".
  - **The end-of-line wreck** (Ruling 38).
    - **At stop 5.** `DebugWreck` at stop 5 prints `ride: wreck at the end of the line: past the exit bridge's start, no spare took over; {h} hero(es) set down inside`. The joiner sees Wrecked and no takeover.
    - **At stop 4.** Then `DebugRestoreAt(4)` and `DebugWreck` print `ride: wreck: … took over` (spare 3), and the old `wreck-spare:` and stop-2 heap steps run from there.
    - **The sound.** The joiner's ride-sound `wreck=1` holds: the stop-4 wreck lies beyond its 30 m.
  - **The props and books.** The trace list gains `prop:parked:{i}:smashed` and `prop:parked:{i}:standing`. The seat books gain `L`, and the prop hash includes the parked train's state.

- [ ] **Step 1: Add the checks** to `NetTestFlow` and `net_test.sh`, in ride order, and update the header's comment and its timings.
- [ ] **Step 2: Run plain `scripts/net_test.sh`** (alone, lock absent). Expected: `NET TEST: ok`.
- [ ] **Step 3: Hand the RIDE pass to the controller.** Stop and report that the RIDE checks are in, with the expected new lines.
  - The controller runs `RIDE=1 scripts/net_test.sh` alone, under `.superpowers/sdd/shared/RIDE.lock`, on a profile other than `low-power`.
  - Expected: `NET TEST: ok`, with every line above, plan 14's lines and `wipe-rise watch: ok`.
  - A failure is investigated from its kept logs (`KEEP_TMP=1`), never rerun to green.
- [ ] **Step 4: Commit** (after the controller's pass)

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh
git commit -m "test(net): the RIDE pass for the exit island: a remote seat throws the pit switch and the train comes round with no fall on every peer, no remote Get out over the exit bridge, the parked train's smash seen on both clients, the joiner's chaser halting behind the lift outside and the lift carrying it in, and the end-of-line wreck taking no spare before the spare checks rerun from stop 4

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 9: Docs and the full sweep

**Files:**
- Modify: `docs/development.md`
- Modify, only to re-pin a measured value (with its measurement in the commit message): any `src/Core/DebugTools.*.cs` whose check moved.

**Interfaces:**
- Produces, in `docs/development.md`:
  - **Generation.**
    - The two knobs and the passes (pit, island, refusals, `[gen]` lines).
    - The rail proof replacing the on-foot stairs checks.
    - The sweep note at `:879`, which names the island floor.
  - **Crevasses.**
    - Lava regions, the trench, the moat and the cave-ins' look.
    - The exit bridge (kept track, no gravel, no footing, the trestle).
    - The new `[map] rail`, `[map] rail pit:`, `[map] rail island:` and `[chasm]` lines with the measured numbers.
    - The probe's new `nofloorDrop` pins and why (40 samples per 4 m of drop).
  - **"The five rides."**
    - Ride 5's row with every piece and its measured S and At.
    - The S paragraph: plan 15 moved no line S, and re-placed ride 5's pieces from the measurement.
    - `:2094-2100` rewritten for the pit switch (deadly out lever, calls, R-lone, late press, guard, fixed back, the fall), and the jump kept for other floors with its lane check.
    - `:2236-2240` rewritten for the pit and the trestle in the gorge.
    - The parked train and the lift.
  - **The watchdog.** The pit's four steps.
  - **"Stops, cages and the crew."** Home on an island floor, unopened cages, stranded crew, Grisel brought in and the reward chest.
  - **"Riding together."**
    - The exit bridge's hold, the lift on the wire (ids, kind, books) and the parked train's props.
    - The RIDE net test's new steps with the controller's measured numbers.
    - Known gaps: the relief at the lift (Ruling 42) and what the reviews leave.
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
  - **The `--track-depth` check** (lock absent): `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | grep -E "\[track-depth\] (PartyTrack |EnemyTrack |total)"`. Expected: Task 3's pins, and the total reading `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`.
  - **The generator sweep** of `development.md:879`: no repair line on any floor, and `adapted` 0.
  - Then plain `scripts/net_test.sh`, alone.
  - Every one must PASS with `errors=0` (or `NET TEST: ok`). A value that moved is re-pinned only from its measurement, named in the commit with its old and new values.
- [ ] **Step 3: Commit**

```bash
git add docs/development.md
git commit -m "docs: floor 12's exit island (the knobs, the rail proof, lava, cave-ins and the exit bridge), ride 5's pit switch, the parked train and the lift, home and lost on an island floor, the reward chest, the new pins and the RIDE pass's island steps

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add each re-pinned `src/Core/DebugTools.*.cs` to the `git add` by its path.)
