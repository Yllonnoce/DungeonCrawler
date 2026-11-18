# Mine Cart Chase, Plan 4: the enemy line, traffic, switches and the four rides' set pieces

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each of floor 12's four rides adds one new thing. Ride 1 is the Ore Run, ride 2 is Side by Side, ride 3 is the Switchyard and ride 4 is the Pour. Slag carts ride the enemy line beside you, hounds leap aboard, carts run over whoever is on their rails, and a forge golem chases you to the buffer.

**Architecture:** Plain-number rules (pacing, timing, jump threshold) live in engine-free `src/Dungeon/Rails/Core/SetPieceRules.cs`. Floor 12's encounters and set pieces are plan data: `RailEncounters` and `RailSetPieces`, placed by ride and fraction of the ride. A per-floor `RailEncounterController` reads them, spawns slag carts, scripted riders, runaway wagons and set-piece props, and drives them from `RailController.CartEvents` and the party cart's S.

- **Slag carts** are `ICartBody`s with their own `CartRun` on `EnemyTrack`, paced against the party cart through a party S to enemy S table.
- **Riders** are ordinary `Enemy`s in a new carried mode. A carried enemy keeps its AI's attacks but none of its movement.
- **Switches** don't change the generator. They slide the party cart's pose across to the enemy lane over a stretch, and run-over is judged against the lane it is on.
- **The golem** is an elite, never `IsBoss`, so its death can't unlock the stairs. It runs on the enemy line like a cartless rider.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (tests/ is local, not in git).

**Spec:** `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md`. The sections that apply are "The four rides", "Rolled over", the "Traffic" call, "Enemies are plan data", and the ride-length amendment (rides of 20–30 s).

**Builds on:**
- `RailController`: `Party` CartRun, `StopS`, `StopIndex`, `CartEvents`, gates, train, spares, `RunOver`.
- The rest of the cart code: `CartRun`, `TrackSampler`, `RunOverSweep`, `ICartBody`, `ICartRider`, `CartRules`, `RunOverRules` (direction-aware), `CartPhysics`.
- `DungeonBuilder.EnemyTrack` and `CrewRescueController`.
- Code map: `.superpowers/sdd/shared/plan4-code-map.md`.

**Floor 12's rides:** Floor 12's party line is about 497 m, with stops at S ≈ 16 / 87 / 272 / 414 / 489. That gives ride 1 ≈ 71 m, ride 2 ≈ 185 m, ride 3 ≈ 142 m and ride 4 ≈ 75 m.

## Global Constraints

- **Floors without rails are untouched.** No encounter controller is created unless the floor has a `RailController`, a `RailEncounters` list, or a `RailSetPieces` list.
- **The generator does not change.** No new lanes, junctions or hash changes. Switches, the spur, the trestle and the gap are dressing plus scripted behaviour.
- **No floor-12 enemy may be `IsBoss`.** A boss death on floor 12 would unlock the stairs through `Main.OnBossDefeated` and bypass the crew objective. The golem is an elite.
- **Everything is server-authoritative.** Every encounter, set piece, slag cart and rider state change happens only when `!NetSession.IsClient`. Clients get enemies through the existing snapshots; the cart visuals are plan 5.
- **Everything warns first.** Every cart and hazard warns by light and sound (a placeholder sfx cue is fine) before it can hurt anyone.
  - The minimum warning is 1.5 s for hazards on a timer.
  - A slag cart's bell rings at 3 s of look-ahead, using the existing `InWarning` with the 3 m/s floor.
- **Run-overs follow the lethality setting.** Pockets stay safe from every cart. A hero seated in the party cart is never run over.
- **Protocol stays 16.** Save fields are additive and nullable.
- **Text and sound are placeholders.** New text is plain and short, and voice is subtitles only; plan 6 adds the real sounds and lines. Sfx cues here are placeholder names and are silent until plan 6.
- **Housekeeping.** `tests/` is not in git. Build before any headless run. No installers.

## Review Focus

1. **The floor never stops being winnable.** A golem left alive, a slag cart stuck alongside, a set piece mid-cycle when the party saves, a wreck at the jump, or every spare used: in every case the floor can still be finished, by cart or on foot. Test: the `--test-chase` sweep steps.
2. **Nothing hits without warning.** Each set piece and cart logs its warning at least 1.5 s before its first possible hit. Test: `SetPieceRulesTests` timing asserts, plus the `--test-chase` checks.
3. **Carried enemies never walk off, fall or stick.** If the slag cart under them is destroyed they die "thrown under". A hound that leaps and misses lands on the floor beside the track as a normal enemy.
4. **No per-frame allocation in the encounter tick.** Lists are reused, and props spawn only when their ride begins.
5. **Save and load.** A load mid-ride restores the cart at its last stop with the ride's encounters reset to "not started", since rides restart from the stop. The golem's state persists only as "defeated or not".

---

### Task 0: Split RailController before the enemy line arrives

Plan 3's final review measured RailController at about 950 lines. It carries gates, spares, the train, boarding and the bench, save, run-over effects, warnings, the wreck, inputs, posing and the HUD. This task does behaviour-preserving extractions so that slag carts, the golem runner and lane switching share code instead of growing it further.

**Files:**
- Create `src/Dungeon/Rails/StopGates.cs`, `src/Dungeon/Rails/SpareYard.cs`, `src/Dungeon/Rails/Core/SpareRules.cs`, `src/Dungeon/Rails/CartTrain.cs` and `src/Dungeon/Rails/RunOverEffects.cs`.
- Modify `RailController.cs` and `CartRun.cs`.
- Test `tests/Dungeon/Rails/SpareRulesTests.cs`.

**Interfaces** (produces):

- **`StopGates`**: building gates, the tick, the winch check, holders, plus `GateUp(i)`, `GatesUpList` and `RestoreGates`.
- **`SpareYard`**: building and hiding spare carts, and `Pick` via `SpareRules`.
  - `public static int SpareRules.Pick(IReadOnlyList<float> stopS, IReadOnlySet<int> used, int stopIndex, float s)`.
  - It picks the nearest unused intermediate stop at or below `stopIndex + 1`. If there is none, it returns −1.
- **`CartTrain`**: `Couple`, `PoseTrain`, `SyncTrain`, `FollowTrain` and the siding bookkeeping.
- **`RunOverEffects.Apply(...)`**: applies hits with the per-body-type outcome switch. Heroes, NPCs and enemies are covered, including the big enemy's knock-aside. It returns the plating damage dealt, so the party cart and slag carts share it.
- **`CartRun.FrameAt`**: `public Func<float, Transform3D>? FrameAt` on `CartRun`. When it is set, posing uses it instead of `Track.Frame`. This is how lane blending (Task 7), the jump arc (Task 8) and the golem runner get their frames.

- [ ] **Step 1: SpareRules tests.** Test the nearest-unused pick, the rule that it never picks past `stopIndex + 1`, and −1 when nothing qualifies. Watch them fail, then implement.
- [ ] **Step 2: Extract.** Behaviour must not change. These must still print RESULT ok:
  - `--test-ride`
  - `--test-ride-edges`
  - `--test-crew=condemned`
  - `--test-crew=forgiven`

  Then run the full suite.
- [ ] **Step 3: Commit** `refactor(rails): gates, spares, the train and run-over effects get their own homes`.

### Task 1: Set-piece rules (engine-free)

**Files:** Create `src/Dungeon/Rails/Core/SetPieceRules.cs`; test `tests/Dungeon/Rails/SetPieceRulesTests.cs`.

**Interfaces** (produces):

- `public static class PaceRules`
  - `float TargetS(float partyEnemyS, float offset)`: the S a slag cart aims for. A positive offset means ahead of the party cart.
  - `float SpeedFor(float s, float v, float targetS, float partyV)`: the commanded speed. It is `partyV + clamp((targetS − s) × 0.8, −3, 3)`, clamped to `[0, CartRules.MaxSpeed]`.
  - `bool Alongside(float s, float targetS)`, true when `|s − targetS| < 2.5`.
- `public static class SetPieceRules`
  - `HammerPhase Phase(long tick, int index, out float secondsInto)`
    - `enum HammerPhase { Up, Glow, Fall, Down, Rise }`.
    - Durations: Up 1.6 s, Glow 1.5 s (the warning), Fall 0.15 s, Down 0.5 s, Rise 0.75 s, so a cycle is 4.5 s at 60 ticks/s.
    - Offset by `index × 40` ticks.
  - `PourPhase Pour(long tick, out float secondsInto)`
    - `enum PourPhase { Dry, Warn, Pouring }`.
    - Dry 6 s, Warn 1.5 s, Pouring 3 s.
  - `bool JumpMade(float v)`, which is `v >= JumpSpeed`, with `const float JumpSpeed = 6.5f`.
  - `float ArcHeight(float u, float gapLength, float v)`: a parabola over u in [0, gapLength] with apex `0.12 × gapLength × v / JumpSpeed`, 0 at both ends.
  - `const float TrestleGrace = 4f`: seconds after the cart enters the trestle before it collapses.
  - `const float RunawayEvery = 40f` and `const float SlagTrafficEvery = 25f`.
  - `const float LeapWindup = 0.8f` and `const float LeapRange = 6f`: a hound leaps only when the party cart is within 6 m.

- [ ] **Step 1: Write the tests.**
  - `SpeedFor` closes a gap and holds pace when alongside.
  - Hammer phases cycle with the right durations, and the Glow before every Fall is ≥ 1.5 s.
  - Pour Warn is ≥ 1.5 s before Pouring.
  - `JumpMade(6.4)` is false and `JumpMade(6.5)` is true.
  - `ArcHeight` is 0 at both ends and peaks at the middle.
- [ ] **Step 2: Watch them fail.** Then implement.
- [ ] **Step 3: Commit** `feat(rails): set-piece rules — pacing, hammer and pour timing, the jump`.

### Task 2: Rail encounters and set pieces as plan data

**Files:**
- Modify `src/Adventure/Core/Plans/FloorPlan.cs`, `src/Adventure/Core/Validate/PlanValidator.cs`, `src/Adventure/Core/Validate/PlanResolver.cs`, `FloorContents` and `data/adventures/builtin/floor_12.json`.
- Test `tests/Adventure/RailEncounterPlanTests.cs`.

**Interfaces** (produces):

- `public sealed record RailEncounterPlan`
  - Fields:
    - `string Id`
    - `int Ride`: 1-based, the stretch from stop Ride−1 to stop Ride
    - `string Kind`: `"slag_cart" | "lone" | "thrower" | "golem"`
    - `List<MemberPlan> Riders`
    - `float At`: the fraction of the ride where it starts or stands
    - `float Offset`: metres ahead (+) or behind (−) the party cart, for a slag cart
    - `string? Line`: a narration or bark line
  - Validator rules:
    - drop unknown kinds and unknown enemy ids;
    - drop any `IsBoss` enemy, with the repair "rail encounter {id}: no bosses";
    - Ride must be in 1..(stops − 1);
    - `At` is clamped to [0.05, 0.95];
    - a slag cart has at most 3 riders;
    - at most 12 encounters.
- `public sealed record RailSetPiecePlan` with `string Id`, `int Ride`, `string Kind` and `float At`.
  - Kind is one of `"beam" | "buckets" | "switch_out" | "switch_back" | "heap" | "hammers" | "pour" | "trestle" | "jump" | "spur_switch" | "buffer"`.
  - Validated the same way, with at most 20.
- `FloorPlan.RailEncounters` and `FloorPlan.RailSetPieces` are `[AiIgnore]` lists, passed through to `FloorContents`.

- [ ] **Step 1: Tests.**
  - A plan round-trips through the validator.
  - A boss rider is dropped.
  - An out-of-range ride is dropped.
  - Built-in floor 12 parses with the authored lists below.
- [ ] **Step 2: Implement.** Then author floor 12. Data uses the game's JSON escaping.
  - **Ride 1:**
    - `lone` horde_cinder ×1 at 0.45, with the line "One of them's stood on the rails."
    - `beam` at 0.6.
    - `thrower` forge_priest ×1 at 0.8.
  - **Ride 2:**
    - three `slag_cart`s at 0.15, 0.45 and 0.75:
      - riders `horde_cinder ×2`, offset −6;
      - riders `forge_priest ×1, ember_hound ×1`, offset −4;
      - riders `ember_hound ×2, horde_cinder ×1`, offset −3.
    - The first has the line "Carts on the slag line!"
  - **Ride 3:**
    - `switch_out` at 0.2;
    - `heap` at 0.35, the blocked party line;
    - `switch_back` at 0.55;
    - one `slag_cart` with `horde_cinder_brute ×1` at 0.3 and offset +8, so it runs ahead on the enemy line to be rammed after the switch;
    - `buckets` at 0.75.
  - **Ride 4:**
    - `golem` forge_golem ×1 at 0.05, with the line "The kiln wall bursts — something huge is on the slag line!";
    - `hammers` at 0.25;
    - `pour` at 0.45;
    - `trestle` at 0.6;
    - `jump` at 0.75;
    - `spur_switch` at 0.85;
    - `buffer` at 0.98.
- [ ] **Step 3: Check.** Run the full suite, and confirm the headless repair grep for floor 12 prints nothing.
- [ ] **Step 4: Commit** `feat(floor12): rail encounters and set pieces as plan data`.

### Task 3: Carried enemies and the slag cart

**Files:**
- Modify `src/Enemies/Enemy.cs` to add carried mode.
- Create `src/Dungeon/Rails/SlagCart.cs`, `src/Dungeon/Rails/LaneMap.cs` and `src/Dungeon/Rails/RailEncounterController.cs`.
- Modify `src/Dungeon/DungeonBuilder.cs` to create the controller after `PlaceObjective` when the floor has a `RailController` and contents lists.

**Interfaces** (produces):

- **`Enemy` carried mode.**
  - Members: `public Node3D? Carrier`, `public bool Carried => Carrier != null`, `public void Carry(Node3D carrier, Func<Transform3D> spot)`, `public void Drop(Vector3 at)` and `public void FollowCarrier()`.
  - While carried, `_PhysicsProcess` skips movement, gravity, separation, Unstick and `MoveToward`.
  - Retarget, attack wind-ups and `Fire()` still run. Melee strikes happen only when the target is within `AttackRange × 1.15` of the enemy's current position, as today.
  - Collision layer stays 4, so heroes can hit carried enemies; mask goes to 0.
- **`public sealed class LaneMap`**
  - `LaneMap(TrackSampler party, TrackSampler enemy)` builds a table every 1 m.
  - It provides `float EnemyS(float partyS)` and `float PartyS(float enemyS)`.
- **`public partial class SlagCart : AnimatableBody3D, ICartBody`**
  - A procedural tub, rust-red with a slag heap and glowing coals.
  - A `BrakeShoe` child, which is a small `AnimatableBody3D : ICombatTarget` on layer 4, glowing orange. Breaking it calls `Derail()`.
  - `CartRun Run` on `EnemyTrack`.
  - Riders are posed at three spots on the tub.
  - `Derail()`:
    - sparks via a GpuParticles burst;
    - a cosmetic tumble: hide the body and spawn a short-lived `RigidBody3D` copy;
    - riders die, using `KillByEnvironment("the slag cart", HazardDamage.RunOver)` with the log "{name} is thrown under the cart.";
    - log "The slag cart derails!".
- **`RailEncounterController`**
  - Reads `FloorContents.RailEncounters` and `RailSetPieces`.
  - On `CartEvent.Departed` from stop k−1 (ride k), it arms ride k's items.
  - A slag cart spawns at `EnemyS(partyS)` plus its offset when the party cart reaches the ride fraction `At`, with its riders carried. It is paced each tick with `PaceRules`.
  - It despawns at the ride's end stop: it rolls on past and is freed off the line's end, or freed at the next stop.
  - A slag cart runs over bodies on the enemy lane using `RunOverSweep` on its own `CartRun`, applying `RunOverRules.Outcome`. Heroes on the enemy lane are mangled at lethality 3.
  - On `CartEvent.Wrecked`, `Restored` or `Arrived` at a stop, the ride's live items are cleared. A ride re-arms on the next departure.

- [ ] **Step 1: Carried mode.** Unit tests aren't practical, since this is the engine. Keep the change minimal and guarded by `Carried` so normal enemies are untouched.
- [ ] **Step 2: LaneMap, SlagCart and the controller.**
- [ ] **Step 3: Throwaway headless smoke (not committed).** On floor 12, ride from stop 1. The first slag cart appears behind, pulls alongside within 6 s, and holds alongside (`Alongside` true over 3 s of samples). Its priest rider shoots seated heroes. Breaking the brake shoe derails it, and its riders die. Remove the smoke afterwards.
- [ ] **Step 4: Check.** Run the full suite, `--test-ride` and `--test-ride-edges`. A wreck must not leave slag carts orphaned.
- [ ] **Step 5: Commit** `feat(rails): slag carts ride the enemy line alongside you, with riders`.

### Task 4: Hounds leap aboard

**Files:** Modify `src/Dungeon/Rails/RailEncounterController.cs`, `src/Enemies/Enemy.cs` and `src/Dungeon/Rails/MineCart.cs` (boarder spots).

**Interfaces** (produces):

- A carried `ember_hound` on a slag cart that is alongside, and within `SetPieceRules.LeapRange` of the party cart, crouches. That means a `TelegraphFx.HandGlow`-style glow plus the bark "Hound!" from a seated companion, held for `LeapWindup`.
- It then leaps on a 0.6 s arc onto a boarder spot on the party cart's rim. There are 2 spots, front and back. It becomes carried by the party cart (`MineCart.BoarderSpot(i)`).
- A boarder bites:
  - the nearest seated hero through its normal melee;
  - the cart's plating, for −3 every 2 s.
- It falls off when killed. If no boarder spot is free, it leaps and misses: it lands on the floor beside the track and becomes a normal enemy (`Drop`).
- **Wagons are threatened too.** The plan's goal is "a wagon you must protect". When the train has cage wagons, a hound picks the nearest of cart and wagons. A wagon has one boarder spot on its roof, and a boarder there bites the seated crew in that wagon through its normal melee. Crew are `Npc`s, so they take damage and can go down, and a downed crew member in a wagon can be revived from the cart as today.

- [ ] **Step 1: Implement.** Write a throwaway smoke and do not commit it: a hound leaps, bites and dies, and when both spots are taken the next hound misses and lands.
- [ ] **Step 2: Check.** Run the full suite and both ride tests.
- [ ] **Step 3: Commit** `feat(rails): ember hounds leap across onto your cart`.

### Task 5: Traffic — slag carts on a schedule, runaway wagons ahead

**Files:** Modify `src/Dungeon/Rails/RailEncounterController.cs`.

**Interfaces** (produces):

- **Slag traffic.**
  - Every `SlagTrafficEvery` s while no ride encounter is live, a riderless slag cart runs the whole enemy line at cruise from S 0 to the end, then despawns.
  - It rings its bell (`PlaySfx("cart_bell")` placeholder) and lights its lamp. The existing warning bark goes to heroes on foot on the enemy lane within 3 s of look-ahead.
  - It runs over whoever is on the enemy lane, but never anyone in a pocket.
- **Runaway wagons.**
  - Every `RunawayEvery` s while the party cart is Held and nobody is aboard, a runaway ore wagon runs the party line from just past the party cart's stop to the next stop, then halts and despawns.
  - It warns the same way. It runs over whoever stands on the party lane, and it stops at a down gate.
- **Throttling.** Both are throttled. No more than one of each exists at a time.
- **Pockets.** The spec says "AI companions and crew use them". When a cart's look-ahead warning covers an AI-controlled body on foot standing on that cart's lane, the body walks to the nearest pocket of that lane within 12 m and holds there until the cart has passed. Such bodies include a companion, crew let out of a wagon, or an escorting NPC. If no pocket is within 12 m, the body steps off the lane to the nearest walkable non-rail cell. Use a reusable list, and only scan bodies within 30 m of the cart.

- [ ] **Step 1: Implement.** Write a throwaway smoke and do not commit it: a hero standing on the enemy lane is warned at ≥ 3 s, then mangled; a hero in a pocket is untouched.
- [ ] **Step 2: Check.** Run the full suite and both ride tests. Those tests ride with heroes aboard and clear enemies; traffic must not break them. If needed, disable traffic in those tests through a debug flag that is off by default, and say so.
- [ ] **Step 3: Commit** `feat(rails): slag traffic on the enemy line and runaway wagons on the warden line`.

### Task 6: Ride 1 — the lone cinder, the low beam, the slag thrower

**Files:**
- Modify `src/Dungeon/Rails/RailEncounterController.cs`.
- Create `src/Dungeon/Rails/SetPieces/LowBeam.cs`.

**Interfaces** (produces):

- **`lone`.** The enemy is spawned standing on a party-lane cell at the ride fraction when the ride starts. It holds there (`HoldAt`) and faces the cart, so you run it over.
- **`beam`.** A timber beam across the party line at about 1.25 m above the rails, built from boxes, just clearing the seated riders. When the cart passes under it:
  - a 0.15 s duck shake (desktop camera only; VR gets no shake);
  - the bark "Heads!" from a seated companion;
  - never a hit while seated.

  A hero standing on the rails there is unaffected; the beam is dressing.
- **`thrower`.** The ranged enemy stands in the nearest pocket to the ride fraction (`HoldAt`). It throws with `TelegraphFx.HandGlow` on its wind-up, which its Shoot state already has, so verify the glow lasts ≥ 1.5 s. Raise its wind-up for this spawn through `profile with { WindupSeconds = max(1.5, …) }`.

- [ ] **Step 1: Implement.** Take a screenshot of the beam with the cart under it.
- [ ] **Step 2: Check.** Run the full suite and both ride tests.
- [ ] **Step 3: Commit** `feat(rails): ride 1 — a cinder on the rails, a low beam, a slag thrower`.

### Task 7: Ride 3 — switches, a blocked line, ramming, ore buckets

**Files:**
- Modify `src/Dungeon/Rails/RailController.cs` to add the lane offset for posing and run-over.
- Create `src/Dungeon/Rails/SetPieces/TrackSwitch.cs`, `OreHeap.cs` and `OreBuckets.cs`.
- Modify `src/Dungeon/Rails/RailEncounterController.cs`.

**Interfaces** (produces):

- **Lane switching** (in `RailController`):
  - `public float LaneBlend` runs 0..1, where 0 is the party lane and 1 is the enemy lane.
  - Posing lerps the cart's frame between `PartyTrack.Frame(s)` and `EnemyTrack.Frame(LaneMap.EnemyS(s))` by `LaneBlend`, eased over a 6 m switch stretch.
  - Run-over judges against the lane the cart is on: the party lane below 0.5, the enemy lane at 0.5 and above, with `RunOverSweep` run on the corresponding sampler.
- **`TrackSwitch : StaticBody3D, IInteractable, ICombatTarget`**
  - A lever on a post beside the line, with a lamp: green for straight, red for switched.
  - Pull it with E or hit it, in passing or ahead.
  - `switch_out` sets the cart to cross to the enemy lane when it reaches the switch's S, if the lever is thrown.
  - `switch_back` crosses back. It is always thrown, so the cart always returns before ride 3 ends.
  - A rammed slag cart ahead on the enemy lane is a big hit: it derails, and the plating loses 10.
- **`OreHeap`** sits on the party lane at its S. A cart that reaches it on the party lane crashes: plating −40, a heavy shake, and the cart stops Held there, so a wreck follows if the plating reaches 0.
  - The heap warns with a lantern and the bark "Line's blocked — switch!" at 3 s of look-ahead.
  - Walkers can clear it in 3 hits.
- **`OreBuckets`** are overhead buckets on a cable over the party line. Every 3 s one tips, with a 1.5 s creak and a glow as warning, and drops a `FloorFire`-style cinder patch, 2 m wide, onto the line for 4 s.
  - A cart passing through a live patch takes plating −5, and seated riders take `TrapRules.FloorFire` once.

- [ ] **Step 1: Implement.**
  - Write a throwaway smoke and do not commit it:
    - Throw the switch: the cart crosses, rams the slag cart ahead (it derails), and crosses back.
    - Don't throw it: the cart crashes into the heap.
  - Take a screenshot of the cart mid-switch.
- [ ] **Step 2: Check.** Run the full suite and both ride tests.
- [ ] **Step 3: Commit** `feat(rails): ride 3 — switches, a blocked line, ramming, ore buckets`.

### Task 8: Ride 4 — the golem, hammers, the pour, the trestle, the jump, the spur, the buffer

**Files:**
- Create `src/Dungeon/Rails/SetPieces/DropHammer.cs`, `MoltenPour.cs`, `Trestle.cs`, `RailGap.cs`, `Buffer.cs` and `SpurSwitch.cs`.
- Modify `src/Dungeon/Rails/RailEncounterController.cs` and `src/Dungeon/Rails/RailController.cs` for the ballistic pose and the jump outcome.
- Modify `src/Dungeon/Props/RailTrack.cs` so that `Build` accepts S gaps with no rails or sleepers.

**Interfaces** (produces):

- **Golem.** It spawns when ride 4 begins, as an elite `forge_golem` (`SpawnEnemy(..., elite:)`, never `IsBoss`), on the enemy lane at the party cart's S.
  - It enters with the burst recipe: thud, `Tremor.ShakeOnly(0.07, 1.0)` and dust.
  - It runs alongside at pace: carried by an invisible runner with a `CartRun` on `EnemyTrack`, paced like a slag cart.
  - Every 4 s it slams the cart: a 1.5 s wind-up glow, then plating −12 and a rider shake.
  - It can be fought at range.
  - Killing it ends the slams. On death it logs "The golem falls between the rails."
- **`SpurSwitch`.** A `TrackSwitch` on the enemy line near the end. If it is thrown when the golem's runner reaches it, the golem is diverted: it leaves the line along a dressing spur into the wall, takes heavy damage (`ReceiveHazard` big run-over outcome ×3), and stops chasing. Log "The golem goes off the spur!"
- **`DropHammer`.** Three hammers over the party line, using `SetPieceRules.Phase` on `DungeonBuilder.FloorTick`.
  - The Glow phase is the 1.5 s warning: a red light plus `PlaySfx("hammer_wind")`.
  - A cart under a hammer at the Fall instant takes plating −15 and every seated rider is shaken. The hammer never mangles seated riders.
  - A walker under it takes `ReceiveHazard(Crush, …)`.
- **`MoltenPour`.** A pour channel over the line, using `SetPieceRules.Pour`.
  - The Warn phase is a 1.5 s glow plus a hiss.
  - While Pouring, the cart passing through takes plating −20, riders are burned (`TrapRules.FloorFire` ×2), and a walker inside is burned each tick.
  - A `QuenchValve` (an `ICombatTarget` on a pipe beside the line) stops the pour for good when broken: log "The quench valve bursts — the pour gutters out."
- **`Trestle`.** A timber bridge dressing over a stretch of about 12 m. When the cart enters it, it collapses after `TrestleGrace` s behind the cart (cosmetic falling timbers). If the cart is still on it at collapse, the cart is caught: plating to 0, then a wreck.
- **`RailGap`.** A gap in the rails, about 5 m: no rails or sleepers, plus a glowing pour channel underneath.
  - At the lip, if `JumpMade(V)`, the cart flies. S advances at V, and the pose adds `ArcHeight`.
  - Otherwise the cart drops in: a wreck, with the spare cart at stop 3 taking over.
  - At 3 s of look-ahead a companion barks "Pump it — we have to jump!"
- **`Buffer`.** A timber-and-iron buffer at the last stop. The cart smashes through it on arrival, with splinters and a thud, then stops Held as today.

- [ ] **Step 1: Implement.**
  - Write a throwaway smoke and do not commit it. Ride 4:
    - The golem bursts in and slams once.
    - A hammer hits the cart once.
    - Break the valve.
    - Cross the trestle in time.
    - Make the jump with 2 pumpers.
    - Throw the spur switch: the golem is diverted.
    - The buffer smashes.
    - Then a second run without pumping: the jump fails and the spare takes over at stop 3.
  - Take screenshots: golem alongside, hammer glow, mid-jump.
- [ ] **Step 2: Check.** Run the full suite and both ride tests.
- [ ] **Step 3: Commit** `feat(rails): ride 4 — the golem, the hammers, the pour, the trestle, the jump, the spur, the buffer`.

### Task 9: The wreck tumble, and saves mid-ride

**Files:**
- Modify `src/Dungeon/Rails/RailController.cs`.
- Modify `src/Dungeon/Rails/RailEncounterController.cs` for Capture and Restore.
- Modify `src/Save/Core/SaveDtos.cs` to add `FloorStateDto.GolemDefeated : bool?` and `FloorStateDto.ValveBroken : bool?`.

**Interfaces** (produces):

- **The tumble.** A wrecked party cart spawns a cosmetic `RigidBody3D` copy of its tub, with an impulse along its velocity and up. It despawns after 6 s.
- **Hiding the wreck.** Plan 3 does not hide the cart. With a spare, the same node is moved to the spare's stop. With none left, the wreck stays visible on the line. This task must hide the real cart's body at the wreck when no spare takes over, and keep the node for a later restore.
- **Encounter save.** Capture writes `GolemDefeated` and `ValveBroken`. On Restore, live rides are reset: no slag carts and no boarders. The golem re-spawns at ride 4's start unless defeated, and the valve stays broken.

- [ ] **Step 1: Implement.** Then test the save: save mid-ride 4 and load. The cart is at stop 3 with no slag carts, the golem is back unless it was killed, and the valve keeps its state.
- [ ] **Step 2: Check.** Run the full suite and both ride tests.
- [ ] **Step 3: Commit** `feat(rails): a wrecked cart tumbles; rides restart cleanly after a load`.

### Task 10: The scripted chase, docs

**Files:**
- Create `src/Core/DebugTools.Chase.cs`.
- Modify `src/Core/DebugTools.cs` (flag).
- Modify `docs/development.md` (a "The four rides" section under Rails).

**Interfaces** (produces): `--test-chase`, on built-in floor 12 at lethality 3 with no AI and a 300 s quit timeout. Gates and cages are auto-cleared through debug hooks so it is a ride test.

- [ ] **Step 1: The test, tag `[chase]`.** Use one line per check, ending with RESULT ok or FAIL.
  1. Ride 1: the lone cinder dies by run-over and the beam passes with no damage to seated riders.
  2. Ride 2: all three slag carts appear and pull alongside. One brake shoe is broken by a debug hit, and that cart derails. At least one hound leaps.
  3. Ride 3: the switch is thrown, the cart rams the slag cart ahead, and the cart crosses back.
  4. Ride 4: the golem appears and is diverted at the spur by a debug-thrown switch. The jump is made with pumpers forced to 2. The cart arrives at the last stop through the buffer.
  5. Throughout: no enemy on the whole floor is `IsBoss`, plan collectives included (sweep every `Enemy` child of the floor root at each ride's start). The stairs are not unlocked by any enemy death. Every hazard's warning preceded its first hit by ≥ 1.5 s; log timestamps to prove the warning timing.
  6. Traffic: a hero placed on the enemy lane during ride 2's gap is warned, then mangled. A hero in a pocket is untouched.
- [ ] **Step 2: Run the regressions.**
  - `godot --headless --path . -- --test-chase` must print RESULT ok.
  - Then run the full suite, `--test-ride`, `--test-ride-edges`, `--test-crew=condemned`, `--test-crew=forgiven` (from plan 3), `scripts/net_test.sh` and `--test-hazard=press`.
- [ ] **Step 3: Docs.** Add the four rides, encounter and set-piece data, traffic, switches, the golem (elite, not boss), the jump, and `--test-chase`.
- [ ] **Step 4: Commit** `feat(rails): a scripted chase run; docs`.
