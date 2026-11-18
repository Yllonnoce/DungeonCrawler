# Mine Cart Chase, Plan 3: stops, cage wagons, Grisel's crew and the win rule

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On floor 12 the rescue becomes the spine of the ride:

- Each intermediate stop is held by a rail gate until its mobs are dead.
- A cage wagon at each stop holds two of Grisel's named crew; unlocked, it couples behind the cart.
- Grisel helps on the forgiven branch, or lies dead with the orc's keys on the condemned one.
- A spare cart waits at every stop.
- The stairs open when every cage is open and every living crew member stands in the Cooling Stair.

**Architecture:** The crew rules are engine-free in `src/Adventure/Core/Objectives/CrewRules.cs`:

- crew states;
- the win rule;
- the HUD text;
- bleed-out;
- whether a stop is cleared.

Railed Jailbreak floors get a new `CrewRescueController : ObjectiveController`, which replaces the walk-in `JailbreakController` there. It spawns the cage wagons and the crew, handles unlocking and coupling, and judges the win.

`RailController` gains a stop gate and coupled wagons, which add load and follow the cart along the curve. It also gains spare-cart recovery after a wreck.

`Npc` gains a seated mode for bench and wagon riders, a revive, and a bleed-out death for freed crew. Floor 12's plan data names the crew, places the stop mobs, moves Grisel to the landing, and moves the cage narrations to stop 1. The generator is unchanged, so there is no rehash.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (tests/ is local, not in git).

**Spec:** `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md`. The relevant sections are "A stop", "The cart can be hurt", "Grisel and the story", "When the floor is won", and "Calls made after the builder check".

**Builds on:**

- **Plan 1:**
  - `FloorLayout.Rail` (Stops: Room, Platform);
  - `RailStop{i}` markers;
  - `DungeonBuilder.PartyTrack`.
- **Plan 2:**
  - `RailController`: Cart, State, StopIndex, StopS, Boarding, seats with bench 4–5 for NPC ids 100+, exit cells, run-over, wreck, Capture/Restore, HUD;
  - `MineCart`;
  - `CartInput.CrewWagons`;
  - `--test-ride`.
- **Code map:** `.superpowers/sdd/shared/plan3-code-map.md`.

## Global Constraints

- Nothing changes on floors without rails. Floors 2 and 13, the walk-in jailbreak floors, keep `JailbreakController` exactly as today. `CrewRescueController` is used only when the objective is Jailbreak and the floor has a `RailController`.
- The generator does not change in this plan. `--rehash-plans` must show no data hash moves; floor 12's data changes are plan content, not layout.
- Story flags stay as they are: no new flag appears in data (`StoryRewireTests`). The forgiven branch is `grisel_forgiven`; condemned and unset both mean "not forgiven".
- Floor 12 stays a Jailbreak floor (`TauntTests`), and its per-branch FloorEnter taunts stay.
- Enemy budget: floor 12's `EnemyTotal` stays at or below the hard cap of 16. Stop mobs are plan collectives in the stop rooms.
- Crew who die are lost and do not block the win. A downed crew member bleeds out after 60 s unless revived. The floor completes when every cage wagon is open and every living crew member is in the exit room (`Stops[^1].Room`), counting those in wagons there.
- Server authority: all objective, gate, wagon and crew state changes only when `!NetSession.IsClient`. Plan 5 syncs it.
- Protocol stays 16. Save fields are additive and nullable, so old saves still load.
- Crew names avoid cast names. The stock list contains "Tobit", a cast member, so do not use it.
- New text is plain and short. Voice stays subtitles; plan 6 voices it after the owner's OK.
- `tests/` is not in git. Build before any headless run. No installers.

## Review Focus

1. **The floor can always be finished.** A wrecked cart with no spares left, the gate's collective emptied by the horde or by run-overs, Grisel dead, all crew dead: in every case the stairs still open once nothing more can be done. If every crew member is dead, the floor completes when the last cage is open and nobody living is left. Tests: `CrewRulesTests`, plus the `--test-crew` lost-crew step.
2. **Unlocking works without keys.** Lockpicks always work as a fallback, and Grisel or the orc's keys open a cage free. The orc's keys are never consumed.
3. **Save and load mid-floor.** Open cages, coupled wagons, crew home or lost, gates up, spares used and the orc's keys all survive a save and load. Crew in a coupled wagon come back in their wagon, coupled at the cart's stop. Test: `--test-crew` save/load step.
4. **Nobody gets stuck.**
   - A seated NPC (bench or wagon) never walks off. When the cart is wrecked they are set down beside the track. Downed NPCs are not run over again.
   - Crew left on foot follow the lead, as freed prisoners do today.
5. **Walk-in jailbreak floors are unchanged.** Floors 2 and 13 still play the walk-in jailbreak, and `JailbreakTests` pass unchanged.

---

### Task 0: Split the cart runtime so many carts can share it

Plan 2's final review asked for this before plan 3. Spare carts, coupled wagons and plan 4's slag carts all need the same stepping, posing, track sampling and run-over code.

**Files:**
- Create:
  - `src/Dungeon/Rails/TrackSampler.cs`
  - `src/Dungeon/Rails/CartRun.cs`
  - `src/Dungeon/Rails/RunOverSweep.cs`
  - `src/Dungeon/Rails/ICartRider.cs`
- Modify:
  - `src/Dungeon/Rails/RailController.cs` (shrinks to boarding, stops, party inputs, HUD and save)
  - `src/Dungeon/Rails/MineCart.cs` (keeps its riders)
  - `src/Party/Adventurer.cs` (implements `ICartRider`)
  - `src/Dungeon/Rails/Core/RunOverRules.cs` (direction-aware `Hits`)
- Test: `tests/Dungeon/Rails/RunOverRulesTests.cs`, which gains boundary and reverse-direction cases.

**Interfaces:**
- `public sealed class TrackSampler`:
  - `TrackSampler(RailTrack track)`
  - `Transform3D Frame(float s)` in global space
  - `Vector3 Point(float s)`
  - `float Slope(float s)`
  - `float Curvature(float s)`
  - `float Length`
  - `bool Measure(Vector3 pos, float sHint, float window, out TrackBody body)`. It searches only `[sHint − window, sHint + window]` of the baked curve, stepping 0.25 m and refining. That avoids the whole-line `GetClosestOffset` and its wrong-branch pick where the line passes near itself. It returns false when the point is outside the window.
- `public sealed class CartRun`:
  - fields: `TrackSampler Track`, `Node3D Body`, `CartState State`, `float PrevS`, `CartInput LastInput`, `int Dir` (+1 or −1), `float Limit` (the StopAt)
  - `void Step(in CartInput input, float dt)`, which fixed-steps with an accumulator at 1/60 s and keeps `PrevS`
  - `void Pose(float frac)`
  - `float FrontS => State.S + Dir * CartRules.HalfLength`
- `public static class RunOverSweep`:
  - `void Judge(CartRun run, IReadOnlyList<Node3D> bodies, double now, Dictionary<ulong,double> hitAt, List<(Node3D body, TrackBody tb)> hits)`
  - It culls by distance to the cart's front first, then measures within the window, then calls `Hits`. It returns the hits for the owner to apply (plating, shake, derail…).
- `RunOverRules.Hits(float sPrev, float sNow, in TrackBody b, int dir = 1)` is direction-aware: the front is `s + dir·HalfLength`, and the cart must be moving in `dir`.
- `public interface ICartRider`:
  - members: `int RiderId { get; }`, `MineCart? Cart { get; }`, `int Seat { get; }`, `float SeatYaw { get; set; }`
  - methods: `void SitIn(MineCart cart, int seat)`, `void GetOut(Vector3 at, Vector3 velocity)`, `void FollowSeat()`
  - `Adventurer` implements it now, with `RiderId = Index`. `Npc` implements it in Task 3, with `RiderId = 100 + i`.
  - `MineCart` keeps a `Riders` map from id to rider, plus `Release(ICartRider)`. Posing, ejection, restore and the run-over skip all go through `cart.Riders`, not loops over the party.
- `RailController` keeps one `CartRun Party`. Every state change goes through `void Raise(CartEvent e)`, with `enum CartEventKind { Departed, Arrived, Hit, Wrecked, Seated, Unseated, Restored }`. Plan 5 subscribes to it. The run-over sweep also runs on the arrival tick. The `_hitAt` and `_warnedAt` throttles use physics time, not the wall clock.

- [ ] **Step 1: Direction-aware `Hits`.** Add tests: a body exactly at `sPrev + HalfLength` is excluded and one at `sNow + HalfLength` is included; with dir −1, a cart moving toward lower S hits a body below it. Run them to see them fail, then implement.
- [ ] **Step 2: Extract.** Move code without changing behaviour: `--test-ride` (and `--test-ride-edges` if it exists) must still print RESULT ok with the same numbers. Run the full suite.
- [ ] **Step 3: Commit** `refactor(rails): a cart runtime, a track sampler and a run-over sweep that many carts can share`.

### Task 1: Crew rules (engine-free)

**Files:**
- Create: `src/Adventure/Core/Objectives/CrewRules.cs`
- Test: `tests/Adventure/CrewRulesTests.cs`

**Interfaces:**
- Produces:
  - `public enum CrewState : byte { Caged, Freed, Downed, Home, Lost }`
  - `public static class CrewRules` with:
    - `const float BleedOutSeconds = 60f`
    - `const float ReviveHpFraction = 0.35f`
    - `const int PerWagon = 2`
    - `static bool Completed(IReadOnlyList<CrewState> crew, int cagesClosed)`. True when `cagesClosed == 0` and no crew member is Caged, Freed or Downed. Every crew member is Home or Lost, and at least the cages are open.
    - `static string HudText(IReadOnlyList<CrewState> crew)`. Returns `"Crew freed {freed}/{n}, home {home}"`, then `", {lost} lost"` when lost > 0, then `"."`. Here `freed` counts everyone not Caged.
    - `static bool StopCleared(int aliveInStopCollective)`, which is `aliveInStopCollective <= 0`.
    - `static float LoadFactorWagons(int coupledWagons)`, which returns coupledWagons. This is the `CartInput.CrewWagons` value, and one wagon is one load step.

- [ ] **Step 1: Tests**

```csharp
[Fact] public void NotDone_WhileACageIsClosed()                  // cagesClosed 1, crew all Home -> false
[Fact] public void NotDone_WhileACrewIsOutOnTheFloor()           // cages 0; [Home, Freed] -> false; [Home, Downed] -> false
[Fact] public void Done_WhenEveryLivingCrewIsHome()              // cages 0; [Home, Lost, Home] -> true
[Fact] public void Done_WhenEveryoneIsLost()                     // cages 0; [Lost, Lost] -> true
[Fact] public void Hud()                                         // [Caged, Freed, Home, Lost] -> "Crew freed 3/4, home 1, 1 lost."
[Fact] public void StopCleared_AtZero()
```

- [ ] **Step 2: Run to see them fail.** `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~CrewRulesTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** The tests pass.
- [ ] **Step 5: Commit** with message `feat(crew): crew rules — freed, downed, home, lost; the win`.

### Task 2: Floor 12's plan data — the crew, the stop mobs, Grisel at the landing

**Files:**
- Modify:
  - `src/Adventure/Core/Plans/FloorPlan.cs` adds `CrewPlan` and `FloorPlan.Crew`.
  - `src/Adventure/Core/Validate/PlanValidator.cs` cleans the crew list.
  - `src/Adventure/Core/Validate/PlanResolver.cs` and `FloorContents` pass the crew through.
  - `data/adventures/builtin/floor_12.json`.
- Test:
  - `tests/Adventure/CrewPlanTests.cs`.
  - Existing tests that must pass unchanged: `TauntTests`, `StoryRewireTests` and `GulletCampaignTests`. Update any of them that pins floor 12's NPC room or narration room, and say which.

**Interfaces:**
- Produces:
  - `public sealed record CrewPlan` with `string Id`, `string Name`, `string Sex` ("man" or "woman", for the voice pass) and `string? Body` (an `NpcBodies` key).
  - `FloorPlan.Crew : List<CrewPlan>`, which defaults to empty.
  - `FloorContents.Crew : IReadOnlyList<CrewPlan>`.
  - The validator:
    - drops entries with no name or a duplicate id;
    - drops any entry whose name matches a campaign Cast name;
    - caps the list at `CrewRules.PerWagon × (number of intermediate stops)` when the layout has rails, and at 6 otherwise.
  - `[AiIgnore]` on `Crew` until the AI designer can make railed floors.

- [ ] **Step 1: Find floor 12's stop rooms.** Print `Layout.Rail.Stops` rooms for the shipped floor 12 with a throwaway headless print, or by reading `--print-map`. Record the three intermediate stop rooms (call them S1, S2, S3, in line order) in the report and in a short table in `docs/development.md` under Rails.
- [ ] **Step 2: Tests in `CrewPlanTests`.**
  - A plan with six crew round-trips through the validator.
  - A cast name is dropped.
  - A duplicate id is dropped.
  - Built-in floor 12 has exactly 6 crew whose names are not cast names.
- [ ] **Step 3: Implement the plan types, validator and resolver pass-through.**
- [ ] **Step 4: Floor 12 data.** Use the game's JSON escaping: `'` becomes `'`, and non-ASCII becomes `\uXXXX`.
  - **Crew**, in wagon order:

    | Id | Name | Sex | Body |
    |---|---|---|---|
    | `crew_hob` | Hob Tallow | man | npc_young_man |
    | `crew_merrin` | Merrin Ash | woman | npc_young_woman |
    | `crew_dace` | Dace Wicker | man | npc_prisoner |
    | `crew_pell` | Old Pell | man | npc_old_man |
    | `crew_juno` | Juno Brack | woman | npc_prisoner |
    | `crew_selby` | Selby Crane | woman | npc_old_woman |

  - **Stop collectives**, each with Goal Guard in its stop room and all members from floor 12's roster:
    - `col_stop1`, "The Cage Watch", in S1: `horde_cinder` ×2.
    - `col_stop2`, "The Slag Crew", in S2: `horde_cinder` ×2 and `ember_hound` ×1.
    - `col_stop3`, "The Pour Guard", in S3: `forge_priest` ×1 and `horde_cinder_brute` ×1.
  - **Rebalance to keep `EnemyTotal` ≤ 16:**
    - remove `col_floorpriest`;
    - cut `col_ring` to `forge_priest` ×1 and `ember_hound` ×1;
    - set `EnemyTotal` to the new sum.
  - **Validator rules:** a stop room that is the camp may hold bandits only. If a stop room is room 4 (the camp), put the `col_oddfellows` bandits there as that stop's mobs instead of a new collective, and record it.
  - **Grisel:** `npc_grisel.Room` moves to room 1 (Kiln-Mouth Landing).
  - **Narrations:** both room-11 narrations move to `Room: S1`.
- [ ] **Step 5: Run the checks.**
  - Run the focused tests, then the full suite.
  - Then run `godot --headless --path . -- --builtin --no-ai --floor=12 --quit-after=8 2>&1 | grep -E "repaired|dropped|demoted|adapted"`. It must print nothing.
  - Then run `--rehash-plans=data/adventures/builtin`. `git status data/` must show only `floor_12.json`, from Step 4, with no hash change.
- [ ] **Step 6: Commit** with message `feat(floor12): Grisel's crew by name; mobs at the stops; Grisel waits at the landing`.

### Task 3: NPCs that ride, go down, bleed out and get revived

**Files:**
- Modify: `src/Adventure/Npc.cs`.
- Modify: `src/Dungeon/Rails/RailController.cs`, but only the run-over victim filter that skips seated NPCs.

**Interfaces:**
- Produces, on `Npc`:
  - `public Node3D? Ride { get; private set; }`
  - `public bool Seated => Ride != null`
  - `public void SitIn(Node3D ride, Func<Transform3D> seat)`
  - `public void GetOut(Vector3 at)`
  - `public void FollowSeat()`, which sets an upright transform at the seat with its yaw, and is called every rendered frame by whoever owns the ride.
  - `Npc` also implements `ICartRider` from Task 0 for bench seats in a `MineCart`, with `RiderId = 100 + its index` among the floor root's NPCs. That route delegates to the generic `SitIn`. Cage wagons use the generic `SitIn` directly.
  - `public float DownedSeconds { get; }`
  - `public bool Crew { get; set; }`, set on crew NPCs.
  - A Crew NPC that is Downed for `CrewRules.BleedOutSeconds` calls `Die()`. Today only Quest mode reaches `Die()`; Crew must too.
  - "Revive {Name}" is offered for a Downed Crew NPC, and for a Downed Quest NPC that is Escorting (Grisel). It goes through the existing `TryRevive()` with `CrewRules.ReviveHpFraction`.

- [ ] **Step 1: Seated mode.**
  - While seated, `_PhysicsProcess` skips every movement branch and `MoveAndSlide`.
  - Collision drops to layer 0 so the cart's bodies don't fight.
  - A seated NPC is not caught by the escort `Hazard(dt)` wounding.
  - A seated NPC still counts for `Npc.CheckStairs` (Grisel arriving by cart).
  - `GetOut` restores the collision layer and following.
- [ ] **Step 2: Bleed-out and revive.**
  - Downed time runs on the server.
  - At 60 s a Crew NPC dies: `Die()` raises `NpcEventKind.Died`. Log "{Name} is gone."
  - Revive resets the timer.
  - The label shows "(down {s}s)" while bleeding.
- [ ] **Step 3: Run-over filter.** `RailController` skips seated NPCs.
- [ ] **Step 4: Build, then run the full suite.** Write a throwaway headless smoke and do not commit it:
  - spawn a Crew NPC;
  - `GoDown`, then advance 61 s with a debug time scale or a direct timer poke, and see it die;
  - revive another one.
- [ ] **Step 5: Commit** with message `feat(crew): NPCs can ride; downed crew bleed out unless revived`.

### Task 4: Cage wagons and the crew rescue controller

**Files:**
- Create: `src/Dungeon/Rails/CageWagon.cs` and `src/Adventure/Objectives/CrewRescueController.cs`.
- Modify:
  - `src/Dungeon/DungeonBuilder.cs`: `PlaceObjective` chooses `CrewRescueController` for a Jailbreak objective when a `RailController` exists.
  - `src/Adventure/Npc.cs`: Prompt and Interact route through a small `IPrisonerHost` interface implemented by both jail controllers, not by casting to `JailbreakController`.
  - `src/Dungeon/Rails/RailController.cs`: coupled wagons and the `CrewWagons` input.
  - `src/Save/Core/SaveDtos.cs`: `ObjectiveStateDto` gains `OpenCages : List<int>?`, `CoupledCages : List<int>?`, `HomeCrew : List<string>?`, `LostCrew : List<string>?` and `OrcKeys : bool?`.

**Interfaces:**
- Consumes: Tasks 1–3, `FloorContents.Crew`, `RailController`, `PartyController.TryUnlock(what, objectiveLock: true)`.
- Produces:
  - `public partial class CageWagon : AnimatableBody3D, IInteractable`, with:
    - `int Stop`, the 1-based intermediate stop;
    - `bool Open`, `bool Coupled`;
    - `Transform3D SeatTransform(int i)` for i in 0..1;
    - `void Pose(Transform3D trackFrame)`;
    - `Func<Adventurer, string>? PromptFor` and `Action<Adventurer>? OnUse`.
    - The body is procedural, like the cart: a wagon bed on four wheels with an iron-bar cage (vertical bars from thin boxes or cylinders), a padlock box, and seats for two.
  - `public partial class CrewRescueController : ObjectiveController, IPrisonerHost`, with:
    - `IReadOnlyList<CageWagon> Wagons`;
    - `bool OrcKeys`;
    - `CrewState StateOf(Npc)`;
    - `Capture()` and `Restore(ObjectiveStateDto)`, extending the base class;
    - `HudText = CrewRules.HudText(...)`.
  - `RailController`:
    - `public void Couple(CageWagon w)`;
    - `public IReadOnlyList<CageWagon> Train`.
    - Each coupled wagon is posed every frame at `S − (2·CartRules.HalfLength + 0.4) × k`, where k is the wagon's 1-based place in the train, clamped at 0, on the same curve as the cart.
    - `CrewWagons = Train.Count`.

- [ ] **Step 1: Placement.** One wagon per intermediate stop, standing on a platform cell of that stop's room. Pick a cell that is not a lane cell, nearest the platform's middle; it represents the siding. Fill each wagon with the next two crew from `FloorContents.Crew`, in order. Crew are spawned with `SpawnObjectiveNpc(..., NpcMode.Prisoner)` and their `Body`, with `Crew = true`, seated in the wagon (`SitIn`). Their state is Caged.
- [ ] **Step 2: Unlocking.** E on a closed wagon. The prompt reads "Unlock the cage", or "Open the cage (Grisel)" or "Open the cage (the orc's keys)" when free. The checks run in this order:
  1. If `OrcKeys` is set, open free.
  2. Else if Grisel is alive, Escorting and within 8 m (or seated in the cart at this stop), open free, and log her line: "Grisel works the lock. 'They'll know my voice before they know my face.'"
  3. Else call `party.TryUnlock("the cage", objectiveLock: true)`.

  On open, for both crew members:
  - set Freed;
  - award XP as `JailbreakRules.XpForFreeing`;
  - `SetFlag("freed:{id}")`, the same flag the walk-in jailbreak sets;
  - log "{A} and {B} are free.";
  - `Raise(ObjectiveTrigger.Freed, "{A} and {B}")`.

  When every wagon is open, `Raise(AllFreed)`.
- [ ] **Step 3: Coupling.** An open wagon at the stop where the cart is Held couples to the train:
  - It happens automatically when the cart departs from that stop, and the log reads "The cage wagon couples on behind."
  - The crew stay seated in it.
  - An open wagon that is not coupled lets its crew out on E ("Let them out"). They follow the lead on foot, as freed prisoners do.
- [ ] **Step 4: Home and win.** Each server tick, a crew member counts as Home when:
  - they are alive and not downed, and inside the exit room, either on foot or seated in a coupled wagon while the cart is at the last stop;
  - or they have died, in which case they are Lost.

  When `CrewRules.Completed`:
  - set `SetFlag("jailbreak_freed_all", "the prisoners")`;
  - open the reward vault, as `JailbreakController.AllFree` does;
  - log "The crew are home. The stairs grind open.";
  - call `Complete()`.
- [ ] **Step 5: Save.**
  - `Capture` writes OpenCages, CoupledCages, HomeCrew, LostCrew and OrcKeys.
  - `Restore` re-opens cages and re-couples wagons to the train at the cart's restored stop, with the crew seated in them. Lost crew stay dead; Home crew are placed in the exit room.
  - `ApplyNet` reads the same fields; plan 5 adds the rest.
- [ ] **Step 6: Run the checks.** Build, then run the full suite. Then write a throwaway headless smoke on floor 12 and do not commit it: unlock wagon 1 with a forced lockpick (`LockRules.ForcedChance = 1`), depart, and check the wagon follows and `CrewWagons` is 1.
- [ ] **Step 7: Commit** with message `feat(crew): cage wagons hold Grisel's crew at the stops and couple behind the cart`.

### Task 5: The rail gate — stops hold until their mobs are dead

**Files:**
- Create: `src/Dungeon/Rails/RailGate.cs`.
- Modify: `src/Dungeon/Rails/RailController.cs`.

**Interfaces:**
- Produces:
  - `public partial class RailGate : Node3D`, with `int Stop`, `bool Up` and `void Lift()`.
    - An iron bar across both tracks, chained to a winch post.
    - Placed `S = StopS[i] + CartRules.HalfLength + 1.5` along the party curve, spanning both lanes.
    - `Lift()` animates the bar up over 1.5 s and logs "The gate lifts."
  - `RailController`:
    - `public bool GateUp(int stopIndex)`;
    - `StopGate` refers to the gate's collective, found in `Builder.Collectives` by `Goal.Room == stop room`.

- [ ] **Step 1: Gate logic.**
  - At an intermediate stop, `Boarding` does not start, and the HUD reads "Cart — gate down", until that stop's gate is Up.
  - The gate lifts when `CrewRules.StopCleared(collective.Alive.Count)` holds. Check it each 0.25 s on the server.
  - A stop with no collective starts Up.
  - Gates stay up once lifted.
  - `EventBus.Log` "The gate is down. Clear the stop." is logged once, when the cart arrives at a down gate.
- [ ] **Step 2: Save.** Gates that are up are saved in the same `ObjectiveStateDto`, as `GatesUp : List<int>?`. Store them on the rescue controller, or on `FloorStateDto` next to `RailStop`; pick one and say which. Restore lifts them without the animation.
- [ ] **Step 3: Build and run the full suite.** Run the `--test-ride` regression too. It clears enemies at step 1, so gates start up; if the test needs it, have its step 1 lift every gate.
- [ ] **Step 4: Commit** with message `feat(rails): a rail gate holds each stop until its mobs are dead`.

### Task 6: Grisel's two branches and the bench riders

**Files:**
- Modify: `src/Adventure/Objectives/CrewRescueController.cs` and `src/Dungeon/Rails/RailController.cs`.

**Interfaces:**
- Produces:
  - **Forgiven branch.** Escorting NPCs board with the human onto bench seats 4–5. This covers Grisel on an active escort quest, and a Fighter ally such as Oskar Vell. Their occupant ids are `100 + their index` in the floor root's NPC order. They get out with the human below throwing speed, and are set down beside the track on a wreck.
  - **Condemned branch** (not `grisel_forgiven`).
    - The controller spawns a dead Grisel by wagon 1: cast body `npc_prisoner`, name "Grisel Thorn", `Npc.Restore(dead: true, …)`, with no dialogue.
    - A prompt on her body reads "Take the orc's keys". It sets `OrcKeys = true` and logs "You take the orc's keys from Grisel's belt."
    - It also places a `kaykit_dungeon/keyring.gltf.glb` prop on her belt, which is hidden once taken.

- [ ] **Step 1: Bench boarding.**
  - In `RailController`'s boarding, after the companions hop in, every living, not-downed `Npc` that is Escorting and within 25 m takes a bench seat (`Seats.Take(100 + i, isHero: false)`) and is posed in `_Process` via `FollowSeat`.
  - The cart's `Count`-based checks count heroes only.
- [ ] **Step 2: Grisel arrives.** Grisel seated in the cart at the last stop gets out with the human, and her `CheckStairs` completes the escort quest when she is within 4.5 m of the stairs. If the exit platform is further than 4.5 m from the stairs, she walks there, following the lead.
- [ ] **Step 3: Condemned corpse and keys.** Implement as described above, and save `OrcKeys`.
- [ ] **Step 4: Build and run the full suite.** Write a throwaway headless check of both branches and do not commit it. Set or clear the flag through the test's `AdventureDirector` flag API.
- [ ] **Step 5: Commit** with message `feat(crew): Grisel rides and opens the cages, or lies dead with the orc's keys`.

### Task 7: Spare carts

**Files:**
- Modify: `src/Dungeon/Rails/RailController.cs`.

**Interfaces:**
- Produces:
  - Spare cart props: a `MineCart` with no seats in use, parked on a platform cell (not a lane cell) beside each intermediate stop.
  - `public int SparesLeft`.
  - Saving `SparesUsed : List<int>?` on `FloorStateDto`.

- [ ] **Step 1: Recovery.** When the cart is Wrecked:
  - Its riders are set down beside the track; heroes take 25% HP and NPCs 25% too, as in plan 2. Seated wagon crew stay in their wagons.
  - The wrecked cart is hidden. A cosmetic tumble is plan 4.
  - The nearest intermediate stop by S that still has a spare (ahead or behind) provides the new cart: the spare prop hides, and the controller's cart becomes Held at that stop's S with full plating.
  - Coupled wagons are posed behind it at that stop, since they "roll to the nearest stop on their own".
  - Log "A spare cart waits at stop {n}."
  - With no spares left, log "No carts left. The rest is on foot." The crew in coupled wagons get out and follow the lead.
- [ ] **Step 2: Save and restore.** Save and restore `SparesUsed`.
- [ ] **Step 3: Build and run the full suite.** Run `--test-ride`: its wreck step must now see a spare take over, so update that step's assertions and say how.
- [ ] **Step 4: Commit** with message `feat(rails): a spare cart waits at every stop`.

### Task 8: The scripted rescue, docs

**Files:**
- Create: `src/Core/DebugTools.Crew.cs`.
- Modify: `src/Core/DebugTools.cs` for the flag.
- Modify: `docs/development.md` adds a "Stops, cages and the crew" section under Rails.

**Interfaces:**
- Produces: `--test-crew[=forgiven|condemned]`, defaulting to condemned. It runs on the built-in floor 12 at lethality 3 with no AI. The quit timeout is 240 s.

- [ ] **Step 1: The test, tag `[crew]`.** Each step prints a line with its numbers; the run ends with RESULT ok or FAIL and exit code 1.
  1. **Guard.** The floor has a `CrewRescueController`, 3 wagons, 6 crew Caged and 3 gates down.
  2. **Ride to stop 1.** Board, ride to stop 1, and assert the gate is down and the cart Held. Kill `col_stop1` through a debug kill, and assert the gate lifts.
  3. **Cage 1.**
     - **Condemned:** take the orc's keys from Grisel, then open cage 1 free, with no key consumed.
     - **Forgiven:** Grisel is on the bench; open cage 1 free.
  4. **Couple and ride on.** Depart and assert wagon 1 is coupled and `CrewWagons == 1`. Ride to stop 2, kill `col_stop2`, open cage 2, and depart.
  5. **Save and load mid-ride** between stops 2 and 3. Assert both wagons are coupled at stop 2, the crew are in them, OrcKeys is unchanged and the gates are up.
  6. **Lost crew.** Down one crew member who is out of the wagon (let them out at stop 2 first), and advance the bleed-out. Assert the floor still completes later without them.
  7. **Finish.** Ride to stop 3, kill, open cage 3, and ride to the end. Assert every living crew member is Home and the DownStairs are unlocked. In the forgiven branch, also assert `q_grisel_out` completes.
  8. **Wreck and spare.** In a fresh run section (`--test-crew` restarts the floor), wreck the cart between stops 1 and 2 and assert the spare at stop 1 or 2 takes over.
- [ ] **Step 2: Run the checks.**
  - `godot --headless --path . -- --test-crew=condemned` and `--test-crew=forgiven` must both print RESULT ok.
  - Then run the full suite, `--test-ride`, `scripts/net_test.sh`, `--test-hazard=press`, and a jailbreak regression on floor 2 (`--builtin --no-ai --floor=2 --quit-after=8`) with the walk-in jail and no errors.
- [ ] **Step 3: Docs.** Write the section: gates, cage wagons and coupling, the crew and bleed-out, Grisel's branches, spare carts, the win rule, and `--test-crew`.
- [ ] **Step 4: Commit** with message `feat(crew): a scripted rescue run; docs`.
