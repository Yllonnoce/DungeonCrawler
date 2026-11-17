# Mine Cart Chase, Plan 2: the cart, seating and run-over (single player)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On floor 12 the party boards a mine cart at Kiln-Mouth Landing, rides the warden line stop to stop, and anything on the rails, enemy or hero, is run over.

**Architecture:** The cart's motion, the run-over check, seating and boarding are engine-free rules in `src/Dungeon/Rails/Core` (unit tested). A per-floor `RailController` node owns one `MineCart` on `DungeonBuilder.PartyTrack`, steps those rules on the server/single-player tick, and poses the cart and its seated riders every rendered frame from an interpolated distance. Seated `Adventurer`s skip `MoveAndSlide`. Run-overs go through the existing `IHazardVictim.ReceiveHazard` path, as the Press's crush does.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (tests/ is local, not in git).

**Spec:** `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md` (sections "Boarding and departing", "Cart controls", "Rolled over", "Feel", "Carts (runtime)", "Testing").

**Builds on plan 1:** `FloorLayout.Rail` (`RailLine`, `RailStop`, `Pockets`), `DungeonBuilder.PartyTrack : RailTrack?` (`Curve : Curve3D`, `Length`), the `RailStop{i}` marker nodes under the floor root, `RailRules.Band`.

## Global Constraints

- Every floor without rails (`DungeonBuilder.PartyTrack == null`) behaves exactly as today: no `RailController` is created, and no hero, enemy or NPC code path changes when nobody is seated.
- Cart physics, the run-over check, the hazard outcomes and boarding run only when `!NetSession.IsClient`. Co-op sync is plan 5; in this plan a client shows the cart parked at the start.
- Run-overs follow the lethality setting, as the Press does: `HazardRules.Outcome(kind, TierRules.Lethality)` for heroes and NPCs (mangled at the default 3; part of max HP below 2.5).
- Heroes seated in a cart are never hit by carts; a downed body is not hit again.
- Engine-free code lives in `src/Dungeon/Rails/Core/` and never references `Godot`; the local `tests/DungeonCrawler.Tests.csproj` gains `<Compile Include="../src/Dungeon/Rails/Core/**/*.cs" Link="Game/Dungeon/Rails/%(RecursiveDir)%(Filename)%(Extension)" />`.
- Protocol stays 16 for the whole branch: nothing has been released since plan 1, so the protocol is bumped once at most, in plan 5, and only if a message shape changes.
- `tests/` is not in git; commit only `src/`, `data/`, `docs/`. Build before any headless run (`dotnet build`; Godot flags go after `--`). No installers / `export_all` / `package.sh`.
- New player-facing text is plain and short; voice lines stay subtitles (no Runware spend).

## Review Focus

1. A hero who gets out of a moving cart is run over by it, at every lethality: mangled at 3, hurt at 1. Getting out of a stopped cart is safe. Covered by `RunOverRules` tests and the `--test-ride` step.
2. The cart never leaves the track or overshoots a stop: S stays within [0, Length], and the cart ends Held exactly on each stop's S. Covered by `CartPhysicsTests`.
3. A companion seated in the cart must not walk, follow the formation, regroup-teleport or go to rescue someone while the cart moves. It still fights from its seat, and the human player's camera stays smooth. Covered by the `--test-ride` "companions stayed seated" check.
4. A save mid-ride followed by a load puts the cart Held at the last stop it reached, with nobody seated. A floor without rails ignores the saved field. Covered by the `--test-ride` save/load step.
5. When the cart takes the human player's hero to a stop, that hero must not end up inside a wall or under the floor. A hero who gets out stands on a walkable cell beside the cart: a platform cell at a stop, or a lane cell mid-line, where the next cart will hit them. Covered by `CartSeats.ExitCell` tests.

---

### Task 1: Cart physics (engine-free)

**Files:**
- Create: `src/Dungeon/Rails/Core/CartPhysics.cs`
- Test: `tests/Dungeon/Rails/CartPhysicsTests.cs` (and the csproj line from Global Constraints)

**Interfaces:**
- Produces:
  - `public enum CartMode : byte { Held, Rolling, Wrecked }`
  - `public readonly record struct CartState(float S, float V, CartMode Mode, float Plating)`
  - `public readonly record struct CartInput(float Slope, bool Brake, int Pumpers, int CrewWagons, float StopAt)`. `Slope` is dy/ds of the track at S, and `StopAt` is the S of the next stop (the cart halts there).
  - `public static class CartRules` holding these constants:
    - speeds: `Cruise = 6f`, `MaxSpeed = 11f` (the spec's ride-length amendment: rides of about 20–30 s on a ~500 m line)
    - accelerations: `DriveAccel = 2.5f`, `BrakeDecel = 6f`, `StopDecel = 3f`, `Gravity = 9.81f`, `Drag = 0.02f`
    - pump: `PumpBoost1 = 0.2f`, `PumpBoost2 = 0.3f`
    - load: `LoadPerWagon = 0.05f`
    - plating: `MaxPlating = 100f`
    - geometry: `HalfLength = 1.1f`, `HalfWidth = 0.75f`, `Clearance = 1.6f`
  - `public static CartState CartPhysics.Step(CartState c, in CartInput i, float dt)`
  - `public static float CartPhysics.TargetSpeed(int pumpers, int crewWagons)`, which returns `Cruise * (1 + boost) / (1 + LoadPerWagon * crewWagons)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Flat_ReachesCruiseAndHoldsIt()        // Rolling from V 0, Slope 0, StopAt 1000: after 10 s of 1/60 steps V within 0.1 of 6
[Fact] public void Downhill_NeverPassesMaxSpeed()        // Slope -0.3 for 30 s: V <= 11 every step
[Fact] public void Uphill_SlowsBelowCruise()             // Slope +0.15, no pump: settles below 6
[Fact] public void Pump_RaisesTheSpeed()                 // TargetSpeed(1,0) == 6*1.2, TargetSpeed(2,0) == 6*1.3; flat run with 2 pumpers settles near 7.8
[Fact] public void Crew_MakeTheTrainSlower()             // TargetSpeed(0,3) == 6/1.15
[Fact] public void Brake_Slows()                         // V 10, Brake: after 1 s V <= 4.1
[Fact] public void StopsExactlyOnTheStop()               // V 6 at S 0, StopAt 40: ends Held with S == 40 (± 0.01), V == 0, never S > 40
[Fact] public void Held_DoesNotMove()                    // Held with any input: S, V unchanged
[Fact] public void Wrecked_DoesNotMove()
[Fact] public void Deterministic()                       // same inputs twice -> identical states
```

- [ ] **Step 2: Run to see them fail.** `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~CartPhysicsTests"`.
- [ ] **Step 3: Implement `Step`.** Only a Rolling cart moves. Its acceleration is the sum of:
  - **drive:** `+DriveAccel` when V is below `TargetSpeed`, `−DriveAccel` above it, both scaled by the gap and capped at 1 m/s² per m/s of gap;
  - **gravity:** `−Gravity * Slope`;
  - **drag:** `−Drag * V²`;
  - **brake:** `−BrakeDecel` while braking.

  The stop approach: when `StopAt − S <= V² / (2 * StopDecel) + 0.05`, decelerate at `max(StopDecel, V²/(2*(StopAt−S)))`. Snap to `StopAt` and switch to Held when `StopAt − S < 0.02` or the cart would pass it. Clamp V to [0, MaxSpeed] and S to [0, StopAt].
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(cart): cart physics — cruise, slope, pump, brake, load, stopping on the mark` (src only).

### Task 2: Run-over, seats and boarding rules (engine-free)

**Files:**
- Create: `src/Dungeon/Rails/Core/RunOverRules.cs`, `src/Dungeon/Rails/Core/CartSeats.cs`
- Modify: `src/Dungeon/HazardRules.cs` (`HazardDamage` gains `RunOver`)
- Test: `tests/Dungeon/Rails/RunOverRulesTests.cs`, `tests/Dungeon/Rails/CartSeatsTests.cs`

**Interfaces:**
- Consumes: `HazardRules.Outcome`, `VictimKind`, `HazardOutcome`, `CartRules` (Task 1).
- Produces:
  - `HazardDamage.RunOver` is appended after `Impale`.
  - `public readonly record struct TrackBody(float S, float Lateral, float Height, float Radius)`. `S` is the body's closest offset on the curve; `Lateral` is the horizontal distance to that point; `Height` is the body's feet y minus the track y at that point.
  - `public static bool RunOverRules.Hits(float sPrev, float sNow, in TrackBody b)`. It returns true when the cart moved forward (`sNow > sPrev`), the cart's front swept the body (`sPrev + HalfLength < b.S <= sNow + HalfLength`), `b.Lateral <= HalfWidth + b.Radius`, and `-0.5f <= b.Height <= Clearance`.
  - `public static bool RunOverRules.InWarning(float sNow, float v, in TrackBody b, float seconds = 3f)`. It is true for a body on the rails ahead within `v*seconds + HalfLength`.
  - `public static bool RunOverRules.IsBig(float profileScale, bool isBoss)`, defined as `isBoss || profileScale >= 1.05f`.
  - `public static HazardOutcome RunOverRules.Outcome(VictimKind kind, bool big, float lethality)`:
    - a big Enemy gets `new(false, 0.35f, 1.5f)`;
    - every other kind gets `HazardRules.Outcome(kind, lethality)`.
  - `public const float RunOverRules.BigBodyPlatingDamage = 15f`.
  - `public const float RunOverRules.ThrownAtSpeed = 1f`. A hero who gets out at or above this speed is run over by their own cart.
  - `public sealed class CartSeats` has:
    - constants `HeroSeats = 4` and `BenchSeats = 2`;
    - `int? Take(int occupant, bool isHero)`, where heroes take seats 0–3 and NPCs take the bench seats 4–5;
    - `bool Leave(int occupant)`, `int? SeatOf(int occupant)`, `int Count`, and `IReadOnlyList<int> Occupants`.
  - `public sealed class Boarding` has `const float Countdown = 5f`, `void Start()`, `bool Running`, and `float Left`. Its `bool Tick(float dt, int seatedHeroes, int livingHeroes)` returns true (depart) when `Left` reaches 0, or as soon as `seatedHeroes == livingHeroes` (with at least 1 seated).
  - `public static (int X, int Y) CartSeats.ExitCell(...)`. It takes `IReadOnlyList<(int X,int Y)> lane`, `int laneIndex`, `Func<(int X,int Y), bool> walkable` and `bool atStop`. At a stop it returns the nearest walkable non-lane cell 4-adjacent to the lane cells within 2 of `laneIndex`. Otherwise it returns the lane cell at `laneIndex`.

- [ ] **Step 1: Write the failing tests**

```csharp
// RunOverRulesTests
[Fact] public void SweptBodyAhead_IsHit()                 // sPrev 10, sNow 10.2, body S 11.25 lateral 0.2 height 0 radius 0.4 -> true
[Fact] public void BodyBehindOrBeside_IsNotHit()          // body S 9 -> false; lateral 2.0 (a pocket) -> false; height 3 (a ledge above) -> false
[Fact] public void StandingCart_HitsNothing()             // sNow == sPrev -> false
[Fact] public void FastCart_DoesNotTunnel()               // sPrev 0, sNow 0.25 (15 m/s at 60 Hz), body at 1.3 -> true
[Fact] public void Warning_SeesAheadByTime()              // v 6, body 15 m ahead -> InWarning true at 3 s, false at 2 s
[Fact] public void Outcomes()                             // Member @3 -> Lethal; Member @1 -> !Lethal, HpFraction == HazardRules.DamageFraction(1); small Enemy -> Lethal; big Enemy -> (false,0.35,1.5); Boss -> HazardRules.Outcome(Boss,…)
[Fact] public void Bigness()                              // IsBig(0.66,false) false; IsBig(1.1,false) true; IsBig(0.8,true) true
// CartSeatsTests
[Fact] public void HeroesTakeFrontSeats_NpcsTheBench()     // 4 heroes -> 0..3, 5th hero -> null; npc -> 4, 5, then null
[Fact] public void LeaveFreesTheSeat()
[Fact] public void Boarding_DepartsWhenAllAboardOrTimeUp() // Start; Tick(1,1,4) false; Tick(1,4,4) true; restarted: 5 x Tick(1,1,4) -> true on the 5th
[Fact] public void ExitCell_AtStopIsBesideTheLane_MidLineIsTheLane()
```

- [ ] **Step 2: Run to see them fail.**
- [ ] **Step 3: Implement.** Add `RunOver` to `HazardDamage`. Wherever a `switch` or a ternary over `HazardDamage` names the verb ("crushes"/"impales"), `RunOver` reads "runs over". `Adventurer.ReceiveHazard`'s `HarmSource` maps `RunOver` to `HarmCauses.Generic` with the source name. Find the call sites with `grep -rn "HazardDamage\." src`.
- [ ] **Step 4: Run** the two test classes, then the full suite once → PASS.
- [ ] **Step 5: Commit** `feat(cart): run-over, seats and boarding rules`

### Task 3: The cart and a seated hero

**Files:**
- Create: `src/Dungeon/Rails/MineCart.cs`
- Modify: `src/Party/Adventurer.cs` (seated mode), `src/Party/PartyController.cs` (mouselook while seated), `src/Characters/Companions/CompanionBrain.cs` or the `Think`/`CompanionVelocity` path in Adventurer (seated companions)

**Interfaces:**
- Consumes: `CartSeats` (Task 2), `RailTrack.Curve`, `IInteractable { string Prompt; void Interact(PartyController party, Adventurer actor); }`.
- Produces:
  - `public partial class MineCart : AnimatableBody3D, IInteractable`, with:
    - `SyncToPhysics = false` and `CollisionLayer = 1`, so the interact ray finds it;
    - `public CartSeats Seats { get; }`;
    - `public Transform3D SeatTransform(int seat)` in world space. Seats 0–3 are two rows of two, facing forward. Seats 4–5 are a rear bench.
    - `public void Pose(Transform3D trackFrame)`, which sets the cart's transform from a curve sample with the cart's forward along −Z.
    - `public Func<Adventurer, string>? PromptFor` and `public Action<Adventurer>? OnUse`, both set by the RailController (Task 4) so the cart stays a dumb prop.
    - the body is a box-built tub on four wheels with the lamp from Task 6; `Box` primitives are fine (plan 6 swaps in an asset).
  - `Adventurer`:
    - `public MineCart? Cart { get; private set; }`, `public int Seat { get; private set; } = -1`, and `public bool Seated => Cart != null`;
    - `public void SitIn(MineCart cart, int seat)`;
    - `public void GetOut(Vector3 at, Vector3 velocity)`;
    - `public void FollowSeat()`, which sets the body's global transform to `Cart.SeatTransform(Seat)` rotated by the seat-relative yaw `_seatYaw`, and keeps the camera pitch.

- [ ] **Step 1: Seated mode in `Adventurer._PhysicsProcess`.** When `Seated`, skip the whole movement block, including gravity, the `wish`/`Velocity` ramp and `MoveAndSlide`, and also skip the VR `BeforeMove`/`AfterMove` hooks. Do not call `FollowSeat` here; Task 4 calls it every rendered frame. Attacks, guard, abilities and the interact ray keep working. In `Look` (mouselook), a seated hero adds the yaw to `_seatYaw` instead of calling `RotateY`.
- [ ] **Step 2: Seated companions.** While seated, the companion's chosen `ActionKind` among Follow, Hold, Regroup, Approach, Retreat, Rescue and Dodge becomes Hold, with zero velocity. Attack still runs: the brain picks targets as now, and the companion attacks when the target is in its reach (ranged heroes shoot). It faces its target by turning `_seatYaw`.
- [ ] **Step 3: `MineCart` prop.** Build the mesh: a tub about 2.2 × 1.4 × 0.9 m on four wheels, with an iron rim, a front board, and seat markers. Add the collision shape, the Prompt from `PromptFor?.Invoke(actor) ?? ""`, and Interact → `OnUse`.
- [ ] **Step 4: Build** with `dotnet build`, then run the full test suite once (no engine tests here; it must still compile and pass).
- [ ] **Step 5: Commit** `feat(cart): the mine cart prop; heroes can sit in it`

### Task 4: The ride — RailController

**Files:**
- Create: `src/Dungeon/Rails/RailController.cs`
- Modify: `src/Dungeon/DungeonBuilder.cs` (create it after `PlaceRails` when `PartyTrack != null`, as a child of the floor root, so `Clear()` frees it), `src/UI/Hud/Hud.cs` (cart line)

**Interfaces:**
- Consumes: Tasks 1–3, the `RailStop{i}` markers, `PartyController.Active`, `NetSession.IsClient`.
- Produces:
  - `public partial class RailController : Node` with:
    - `public static RailController? Instance` (cleared in `_ExitTree`);
    - `public MineCart Cart`, `public CartState State`, and `public int StopIndex` (the stop the cart is at or last left);
    - `public float[] StopS`: each marker's `Curve.GetClosestOffset(PartyTrack.ToLocal(marker.GlobalPosition))`, ascending;
    - `public const int HazardIndex = 1000` (a mangled hero's `MangledBy`; `HazardArmed` returns true for it, so rescue works whenever the cart is not on top of them);
    - `public string? HudText`.
  - `Hud.SetCart(float? plating01, string? line)`: a small bar and label under the objective line, hidden when `plating01` is null.

- [ ] **Step 1: Placement and posing.** The cart starts Held at `StopS[0]`. Every rendered frame (`_Process`), compute `s = Mathf.Lerp(prevS, State.S, Engine.GetPhysicsInterpolationFraction())`. Pose the cart from `PartyTrack.Curve.SampleBakedWithRotation(s, true, true)` transformed by `PartyTrack.GlobalTransform`. Then call `FollowSeat()` on every seated hero. Set `ProcessPriority` so this runs before the heroes' own `_Process` (camera). The ride is posed every rendered frame, not every physics tick, so the camera does not judder (physics interpolation stays off project-wide).
- [ ] **Step 2: Boarding.** `OnUse(actor)`:
  - **Not seated:** if `State.Mode == Held`, take a seat.
    - If the actor is the human-controlled hero, every Companion-controlled hero who is alive, not downed and within 25 m also takes a seat (they hop in; walking to the cart is polish for later).
    - Start `Boarding` if it is not running.
  - **Seated:** get out at the `ExitCell` (Task 2) with the cart's velocity.
    - If `State.V >= RunOverRules.ThrownAtSpeed`, the cart runs the hero over at once: `ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(Member, false, TierRules.Lethality), "the warden cart", HazardIndex)`.
    - Log "{name} jumps from the moving cart!" before it.

  `PromptFor`:
  - "Board the cart" when it is Held and the hero is not seated;
  - "Get out" when the hero is seated;
  - "" otherwise.
- [ ] **Step 3: The tick** (`_PhysicsProcess`, only when `!NetSession.IsClient`):
  1. **Boarding:** step `Boarding`. When it says depart, switch to Rolling toward `StopS[StopIndex+1]`.
  2. **Inputs:**
     - `Slope` = (y(s+0.5) − y(s−0.5)) / 1.0 from the curve;
     - `Brake` = the seated human holds "move back";
     - `Pumpers` = the seated human holds "move forward" (1), plus 1 for each seated companion while the slope is greater than 0.05 or the speed is below 0.7×Cruise, at most 2 in all;
     - `CrewWagons` = 0 (plan 3).

     A pumping hero does not attack.
  3. **Arriving at a stop:** when the cart reaches it (Held), set `StopIndex++` and log "The cart rolls into stop {n}." If anyone is seated and the stop is not the last, start `Boarding` again. Plan 3 adds the rail gate and the departure lever; until then the cart waits out the countdown.
  4. **Holding the previous S:** keep `prevS` for interpolation and run-over.
- [ ] **Step 4: HUD.** The line is "Cart — {plating}% — departs in {n}" while boarding, "Cart — rolling" while moving, and "Cart — at stop {n}" while held. It is shown only while a hero is seated or within 12 m of the cart.
- [ ] **Step 5: Look at it.** Rails are on for floor 12 since plan 1.
  - Run: `dotnet build && xvfb-run -a godot --path . -- --builtin --no-ai --clear-enemies --floor=12 --face-node=RailStop0:4 --screenshot=<scratchpad>/cart.png --after=8 --quit-after=12`.
  - Expected: the cart sits on the track at the landing.
- [ ] **Step 6: Commit** `feat(cart): board the cart and ride it stop to stop`

### Task 5: Run-over in the world

**Files:**
- Modify: `src/Dungeon/Rails/RailController.cs`, `src/Adventure/Npc.cs` and `src/Enemies/Enemy.cs` only where the verb switch needs it (Task 2 did the rest)

**Interfaces:**
- Consumes: `RunOverRules` (Task 2), `IHazardVictim`, `Enemy.Profile.Scale`, `Enemy.IsBoss`.

- [ ] **Step 1: The check.** Each server tick while Rolling, for every `IHazardVictim` body, compute a `TrackBody` from `Curve.GetClosestOffset` and the sampled point. The bodies are the party's Active heroes that are not seated and not downed, plus the floor root's living `Enemy` and `Npc` children. Each body judged a hit (`Hits(prevS, S, b)`) gets `ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(kind, big, TierRules.Lethality), "the warden cart", HazardIndex)`.
  - **Big enemy:** also knock it aside. Move it to the nearest walkable cell beside the lane that is not a lane cell, and take `BigBodyPlatingDamage` off the cart's plating. Apply a 0.4 s shake (`Shake(0.06f, 0.4f)`) to seated heroes.
  - **Hero or NPC:** they stay where they fell (on the rails) for the rescue.

  Cost: only bodies within 6 m of the cart's front are judged (cull by distance first).
- [ ] **Step 2: Warnings.** While Rolling, a hero on foot for whom `InWarning(...)` is true gets "Cart coming!" as a bark from a seated companion (once per hero per 4 s) and a "bell" sfx cue on the cart (`EventBus.Instance.PlaySfx("bell")`, a placeholder cue; plan 6 adds the sound). Pocket-seeking AI comes with traffic in plan 4.
- [ ] **Step 3: Wreck.** When plating reaches 0, set Mode to Wrecked. Seated heroes get out beside the track and take 25% of max HP each. Log "The cart is wrecked!" The spectacle and the spare carts are plan 3; in this plan the line is then walked.
- [ ] **Step 4: Build;** full suite once.
- [ ] **Step 5: Commit** `feat(cart): carts run over what stands on the rails`

### Task 6: Feel (desktop) and the cart lamp

**Files:**
- Create: `src/Dungeon/Rails/Core/CartFeel.cs` + `tests/Dungeon/Rails/CartFeelTests.cs`
- Modify: `src/Party/Adventurer.cs` (seated camera), `src/Dungeon/Rails/MineCart.cs` (lamp)

**Interfaces:**
- Produces:
  - `public static float CartFeel.LeanDegrees(float curvature, float v)`. It returns `clamp(curvature * v * v * 1.2f, -7f, 7f)`; `curvature` is 1/m, signed, positive turning left.
  - `public static float CartFeel.FovBoost(float v)`. It returns `10f * clamp(v / CartRules.MaxSpeed, 0, 1)`.
  - `public const float CartFeel.PitchShare = 0.4f`.

- [ ] **Step 1: Tests** check that `LeanDegrees` is 0 on a straight, clamps at ±7, and has the same sign as the curvature. They also check `FovBoost(0) == 0` and `FovBoost(11) == 10`.
- [ ] **Step 2: Seated desktop camera** (skipped when `Vr.Active`, which is plan 5).
  - **Roll:** a damped spring (stiffness 30, damping 10) toward `LeanDegrees` puts the lean on the camera's roll. Take the curvature from the change in yaw between S and S+2 over 2 m.
  - **Pitch:** `PitchShare` × the track pitch is added to the camera pitch.
  - **FOV:** `Settings.Fov + FovBoost(v)`, eased.
  - **Reset:** all three return to normal when the hero gets out.
- [ ] **Step 3: Lamp.** A SpotLight3D on the front board, aimed down the track (range 18 m, angle 35°, energy 2, warm), plus a small OmniLight3D glow. Shadows are off.
- [ ] **Step 4: Screenshot** a seated ride mid-bend. Use `--test-ride` from Task 7 with `--screenshot` at a time when the cart is rolling, or describe the frames.
- [ ] **Step 5: Commit** `feat(cart): the ride leans into bends; the cart carries a lamp`

### Task 7: Save, the scripted ride, docs

**Files:**
- Modify: `src/Save/Core/SaveDtos.cs` (`FloorStateDto.RailStop : int?`), `src/Save/FloorStateCapture.cs` (capture/apply via `RailController.Instance`), `src/Core/DebugTools.cs` (+ a `DebugTools.Ride.cs` partial), `docs/development.md` (the "Rails" section gains "Riding")

**Interfaces:**
- Produces:
  - `RailController.Capture() : int` (StopIndex) and `RailController.Restore(int stopIndex)`. Restore puts the cart Held at `StopS[stopIndex]`, nobody seated.
  - The headless flag `--test-ride`. It forces floor 12 on the built-in campaign, `--no-ai`, lethality 3, and `_quitAfter = 120`.

- [ ] **Step 1: Save.** Capture writes `RailStop` when the floor has a RailController. Apply restores it when the floor has one and ignores it otherwise.
- [ ] **Step 2: `--test-ride`**, modelled on `RunHazardTest` (DebugTools.cs ~:1319). Each step prints `[ride] …`, and the run ends with `[ride] RESULT ok` or `[ride] RESULT FAIL: why` and exit code 1.
  0. Assert floor 12 has `Layout.Rail` with 5 stops and a RailController (else `RESULT FAIL: floor 12 has no rail line` with `Layout.RailFailure`): this is the guard that a later generator change has not quietly removed the chase.
  1. Clear the floor's enemies, teleport the party beside the cart, and have the lead use the cart. Assert that all four heroes are seated within 1 s.
  2. Wait for departure and assert Rolling within 6 s.
  3. Spawn a small enemy (`enemy.horde_cinder`) on a lane cell 30 m ahead. Assert that it dies by `EnemyKilledByHazard` before the cart reaches stop 1.
  4. Assert the cart becomes Held at `StopS[1]` (± 0.05) within 60 s, and that no seated companion's position ever left its seat by more than 0.1 m.
  5. Save, load and assert Held at stop 1 with nobody seated.
  6. Re-board, depart, and at 2 s into the ride have one hero get out. Assert that hero is Mangled (lethality 3) and that the cart rolls on.
- [ ] **Step 3: Run** `dotnet build && godot --headless --path . -- --test-ride` until it prints RESULT ok. Then run the full suite and `scripts/net_test.sh`: co-op must still connect, and clients just see a parked cart.
- [ ] **Step 4: Docs.** Add a "Riding" paragraph to the Rails section of `docs/development.md` covering: board with E, the countdown, forward = pump, back = brake, getting out of a moving cart, run-over outcomes, the plating, `--test-ride`.
- [ ] **Step 5: Commit** `feat(cart): save the stop reached; a scripted test ride`
