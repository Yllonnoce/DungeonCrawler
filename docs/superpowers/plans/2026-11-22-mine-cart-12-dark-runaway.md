# Mine Cart Chase, Plan 12: the dark and the runaway (ride 4 opens) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ride 4 on floor 12 opens with three things:
- a dark stretch where the pocket lamps are out;
- a runaway ore wagon that comes down the party line from behind, bell ringing, and rams the train if it catches it;
- a live split track: thrown after the train passes, it sends the runaway down a spur into a new crevasse. Thrown before, the party takes the spur and falls.

Plan 11's two residuals are fixed first.

**Architecture:**
- **The split's crevasse** is carved by the generator. It is rng-free and sits behind a new knob, `FloorSpec.SplitRide`, which acts only with `Crevasses`. On that ride it cuts an outward strip beside a straight run, short of the cavern, and records it as `RailLine.Split` (the crevasse cells and the points cell).
- **The split** is a party-line spur set piece (`SplitSwitch`):
  - its spur track is numbered like the ride-5 spur (`CartStateDto.Track` 2 + k);
  - its lever is a deadly, human-only `TrackSwitch` that stands past the points;
  - the party's branch is decided when its front reaches the points, and the runaway's when the runaway's front does.
  - The party's wrong branch reuses `RailController.Cross` (onto the slag line, with no return) and then plan 10's `Fall`.
- **The runaway** reuses `RunawayWagon` (the traffic runaway's body). It is a ride piece the server runs on the party track behind the train. It rams the train's last body, halts behind a held train, and is freed by any wreck, fall, restore or chase.
- **The dark** is a set piece that puts out the pocket lamps over its span. Each peer darkens its own view by where its camera is in that span.
- **The wire:** nothing new goes on it. `PropEvt`/`ThrownSwitches` carry the lever, `CartStateDto` carries the runaway (and its spur track), and `CrossOut` plus `Fell` carry the party's fall. Protocol stays 17.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md`. This plan is build-order item 4. It draws on:
- ride 4 in "The five rides" (its first three bullets);
- "Who works the deadly controls";
- "Crevasses and death";
- the anti-goals;
- "Calls left to the builder" (the runaway at cruise + 15%).

The owner dropped the loop-the-loop. The cavern's collapsing bridge and spiral are plan 13's. Rulings so far are in `.superpowers/sdd/mine-cart-rulings.md` (plan 11's section is the last). Line numbers drift, so find code by name. `docs/development.md` (Rails, The five rides, Crevasses, The RIDE net test) says how it all behaves today.

**Builds on:** plan 11, as built and closed at `beddd85`:
- `SwitchBackRules` (`MayToggle`, `AbleToThrow`, `TakesHome`, `TooLate`, `GrindBackTo`, `FallArmed`, `TrafficStartS`, `PlungedAt`);
- `SeatedSwitchRules` (`WindowAhead` 15 m, `AcceptLead` 5 m, `RefuseSetBack`);
- `TrackSwitch.Deadly` / `OfferedNow`;
- `RailCalls.Say`;
- `RailController.Cross`/`Fall`/`Smash`/`DebugStall`;
- `CrevasseWatch.Take`;
- the quiet plunge;
- `CartWatchdog.PartyFreeTo`;
- plan 10's `CarveChasms`/`StripCells`.

It also builds on the ride-5 spur's pattern: `SpurSwitch`, `SpurTrack(k)`/`SpurTrackNumber`, and the golem's `OnSpur` run.

**Measured before planning** (shipped seed, `--builtin --no-ai --floor=12 --print-map` at `beddd85`, read from the ASCII map; S values are interpolated at about 3.79 m a party index):
- `StopS=[16.0,283.2,488.8,769.5,1021.8,1230.5]`, `rides=[267,206,281,252,209]`, `cavern=238..259`, `strips=7`.
- Ride 4 is party indices ≈201.5 (stop 3, room 6, platform 198..205) to ≈268 (stop 4, room 8, platform 265..271). It runs:

| Party indices | Route | S |
|---|---|---|
| 205–207 | north out of room 6 | |
| 208–211 | east along y=16 | |
| 212–220 | north up x=55 ("leg A") | ≈809–840 |
| 221–224 | east along y=7 | |
| 225–233 | south down x=59 ("leg B") | ≈859–889 |
| 234–235 | east along y=16 | |
| 236–261 | south down x=61, the long straight with the cavern on 238..259 | S ≈908–986 |
| 262–264 | west into room 8 | |

- **Leg B, outward:** the enemy lane is at x=60, enemy pockets at x=61 (t=2) and rock at x=62 (t=3). It looks strip-eligible about 226..232, ride fraction ≈0.37–0.46.
- **Leg B, inward:** x=58 holds leg B's pockets, x=57 is a one-cell rock core, and x=56 holds leg A's pockets. Leg A is the same mirrored, so no inward drop fits between the legs.
- **Estimated placement:**
  - split points at cell 228 (S ≈870), crevasse 229..232 (S ≈872–887), the seated window opening ≈858 (the corner at 225);
  - the runaway breaks loose with the front at ≈820 (leg A) and starts ≈795;
  - the dark runs from ≈800 to ≈885.
  - Today's `r4_beam` 0.15 (≈807), `r4_hounds` 0.3 (≈845) and `r4_slag` 0.7 (≈946, in the cavern) all sit inside or past it.
- These are estimates. Task 3 re-measures, and the plan's At values follow the measurement.

**The later plans** (not this one):
- 13: the cavern's collapsing bridge and spiral. It must rule on what a runaway ram does in the spiral (Ruling 13).
- 14: the leap.

**Planner's rulings** (mine; each decides something the spec left open, with what it costs if wrong):
1. Ruling: the split's crevasse comes from the generator behind a new knob, `FloorSpec.SplitRide`:
   - an int, default 0, clamped 0..6;
   - `[AiIgnore]` in `SpecOverrides`;
   - acting only with `Crevasses`;
   - floor 12 sets it to 4.

   The generator never reads plans — cost if wrong: one override line.
2. Ruling: where the crevasse goes (`CarveSplit`).
   - It goes on the longest outward strip-eligible straight run (`StripCells` with way +1, its twins free) of at least `SplitMinCells` = 6. The run lies inside ride fractions `SplitFrom` 0.25 to `SplitTo` 0.55, and when the cavern is on that ride it ends at least `SplitCavernGap` = 3 cells before the cavern span. Ties go to the later run.
   - The run's last `SplitCells` = 4 cells have t = 2, 3 cut to chasm at the lane's level. The enemy lane stays solid floor, with no deck.
   - `RailSplit.Points` is the cell before the cut (`Crevasse.From − 1`).
   - It is refused on the last ride and on the broken bridge's ride (`split=refused`); with no run, `split=none`. It appears in `Strips`.
   - Outward, not inward: inward on ride 4 the rock between the meander's legs is one cell, so a drop would open onto the other leg's pockets.

   Cost if wrong: the spur's rails cross the slag line (dressing only).
3. Ruling: the split's points are the layout's (`RailSplit.Points`, its cell centre measured on the party track), not the plan's At.
   - The `split` piece's At must lie within `SplitRules.AtSlack` = 8 m of those points.
   - The spur's end must fall inside the crevasse (`SplitRules.Live`).
   - Otherwise the split is dead: no spur, no lever, and no runaway (Ruling 10), logged `(dead: …)`.

   Cost if wrong: a seed whose geometry defeats it loses the runaway.
4. Ruling: the spur's shape.
   - From the points it follows exactly the path `Cross` blends along (smoothstep over `RailController.SwitchStretch` = 6 m onto the slag line).
   - It then bends `SpurOut` = 3.5 m further out over the next 5 m (`SpurLength` = 11 m), ending over the crevasse with its rail ends bent down.
   - It is a spur track (2 + k). k counts the pieces with a `Spur` or a `Split` in piece order, so the ride-5 spur stays track 2 once `r4_split` is appended after it.

   Cost: the dressing crosses the slag line's rails.
5. Ruling: "throw it after you pass" is the lever's design.
   - The deadly lever (`Deadly`: human only, no hits, layer 1|8) stands `LeverPast` = 3 m past the points, on the party lane's side away from the slag line (clear of the spur).
   - The seat offers it from 12 m before the points (the lever − `SeatedSwitchRules.WindowAhead`) until it is `Taken`, which happens when the runaway's front reaches the points. It is offered only while the ride's runaway is coming (`SplitRules.Offered`), and never on foot (`Withheld`).
   - The prompt copy is `SplitRules.Prompt`:
     - "Throw the split — we take the spur!" short of the points;
     - "Throw the split behind us" past them;
     - "Set the split back" when thrown.

   Cost if wrong: ride 3 taught "throw when offered". The copy and the call are what counter that habit.
6. Ruling: the party's branch is decided when its front reaches the points (`SplitRules.PartyRoute`).
   - **Thrown, with an able human aboard** (`SwitchBackRules.AbleToThrow`): the spur. The cart calls `Cross(points, NaN)`, then falls (`Fall("The cart runs off the spur into the crevasse!")`) when its front reaches the points + `FallPast` = 7 m, wholly on the slag line beside the drop.
   - **Straight:** the main line. The whole train (wagons too) follows the cart.
   - **Thrown, with no able human aboard:** the main line, logged "No one could set the split back — the cart keeps the main line.". The lever stays thrown for the runaway (plan 11's R-lone).

   Cost: a player downed after an early throw is spared.
7. Ruling: client lead.
   - A throw (straight to thrown) pressed while the server's (or solo) front is within `HoldLead` = `SeatedSwitchRules.AcceptLead` (5 m) short of the points is held. It is applied the instant after the party's branch is decided straight, logged `held to the points`.
   - A set-back is never held and never refused before the points. Plan 8's 1 s set-back guard applies only past them (`SplitRules.GuardsSetBack`).

   Cost: a deliberate throw 1–5 m early is forgiven.
8. Ruling: the runaway takes the lever as it stands when its own front reaches the points.
   - **Thrown:** it takes the spur (an `OnSpur` run on the split's sampler, like the golem's) and goes off its end. It is freed with a quiet plunge, logged "The runaway goes off the spur into the crevasse!" when a hero is within `SwitchBackRules.PlungeHeard` (40 m).
   - **Straight:** it stays on the party line behind the train, and the lever is `Taken`.

   Cost: none.
9. Ruling: the calls. Each is said once per arming through `RailCalls.Say`, and `BarkTrigger` gains `Runaway, SplitWait, SplitNotYet, SplitNow`, appended after `SwitchBackNow`:
   - "Runaway on the line behind us — pump!" as it breaks loose;
   - "Let us pass the split, then throw it!" at the window's start (lever straight);
   - "Not yet — set it back, that's the spur!" at once when it is thrown with the front short of the hold zone;
   - "Now — throw the split!" with the front 1 m past the points, the lever straight and the runaway coming.

   Companions and bots never throw it. Cost: none.
10. Ruling: the runaway, a ride piece `runaway`.
    - Its At is where the party's front is when it breaks loose.
    - It is one `RunawayWagon` (an orange lamp and the bell). It starts `Behind` = 15 m behind the train's last body (the cart plus its coupled wagons), rolling at its target speed.
    - It is not sent (logged `none: …`) when:
      - the ride has no live split;
      - a chaser or the relief cart rolls on the party line;
      - its start would lie under 2 m past the ride's stop;
      - the split's window opens under 10 m past the trigger.
    - Its target speed is `CartPhysics.TargetSpeed(0, crewWagons) × 1.15`, the spec's cruise + 15% of the train's own load, so one pumper holds it off and no pumper lets it close.
    - It rings its bell every 2 s within 40 m of the train.

    Cost if wrong: the contest shifts with the crew's load.
11. Ruling: companions do not pump for the runaway. "Pump to stay ahead" is the players' job: a ram maims and never kills, and the shared speed checks are the jump's and the loop's — cost if wrong: a solo player who never pumps is rammed once.
12. Ruling: the ram.
    - It comes when the runaway's front is within 0.25 m of the last body, with the train rolling.
    - The plating loses `RamDent` = 40 (`RailController.Smash(40, speedKept: 1)`, which wrecks at 0 like any dent).
    - Each seated hero who is up takes 20% of max HP × `PartyDamageScale` / 3, and is never taken below 1 HP. NPC bench riders and crew are only jolted.
    - "Riders are thrown" means thrown about in their seats (shaken). Nobody leaves the cart, because a rider thrown out at speed lands on the line in front of the runaway.
    - There is one ram per runaway: it derails behind the train and is freed.

    Cost if wrong: the owner wanted riders ejected.
13. Ruling: the runaway never rams a held train. With the party's cart held at the next stop, it brakes to halt `HaltShort` = 3 m behind the last body and is freed after 1 s (the traffic runaway's rule). A ram while the train is still braking into the stop stands.

    Cost: a party that out-pumps it to stop 4 pays nothing in plan 12. "On your tail" bites in plan 13's spiral, where riders must brake, and plan 13 rules what a ram there throws.
14. Ruling: a wreck, a fall, a `Restore` (a wipe rise or a load), a chaser or relief cart starting, or the party leaving the floor frees the runaway at once. An arrival lets it halt first (Ruling 13). Spares and the relief cart never meet it, and a re-armed ride sends a new one — cost: a late joiner's chase skips the runaway.
15. Ruling: the watchdog.
    - A stall with the front short of the split's points and the lever thrown sets the lever straight first, logged "watchdog: the split set back", and then frees the cart as today. The watchdog never carries a train onto the spur.
    - The runaway's own stall frees it (`OwnProgressStuck`).

    Cost: none.
16. Ruling: the dark stretch is a set piece `dark`. Its At is its start and it runs `DarkRules.Length` = 85 m.
    - **The lamps:** every role puts out the pocket lamps whose pocket lies within 6 m of either lane over that span (light off, lantern emission off: dead lanterns). The count comes from the layout, so it is the same headless.
    - **The view:** each peer darkens by its camera's place in the span, with 6 m ramps at each end: ambient × 0.12, fog light × 0.25, volumetric emission × 0.
    - **The torches:** each hero's torch dims to × 0.1 by its own place in the span.
    - **What stays lit:** cart lamps, slag carts' lamps, wheel sparks, set pieces' warning lamps (the split's two) and the chasm's ember glow. No new spark light is added.

    Cost if wrong: the owner may want the torches wholly out.
17. Ruling: floor 12's placement (Task 3).
    - `r4_split` sits at the layout's points.
    - `r4_runaway` sits 50 m before them (≈8 s at cruise).
    - `r4_dark` starts 70 m before them, so with `Length` 85 it ends 15 m past.
    - `r4_hounds` stays at 0.3 when that lies in the dark and ≥ 15 m short of the window, else it moves to the dark's middle. `r4_beam` stays.
    - New pieces are appended at the end of `RailSetPieces`, so existing indices and wire names do not move.

    Cost: seed-specific numbers.
18. Ruling: ride 4 gets no barricade (the spec lists none); a runaway smashes timber on its own line as today (`SmashAhead`) — cost: none.
19. Ruling: the wire. Protocol stays 17 and no DTO field is added.
    - The split lever's thrown and taken states go by the existing `PropEvt`/`ThrownSwitches`.
    - The runaway goes by `CartStateDto` (kind `Runaway`, its track 2 + k on the spur).
    - A client reads the runaway's plunge (`SplitRules.PlungedOffSpur`) and the ram (`RunawayRules.RammedAt`) from the gone cart's last S, as plan 11's plunges.
    - The party's wrong branch goes by `CrossOut` and `Fell`, and the dark comes from the plan.
    - The held press is server-only.

    Cost: none.
20. Ruling: residual 1 is proven through the controller by `--test-switch-back=fixedafter`.
    - A test hook (`RailEncounterController.DebugFixedBackBeforeGap` = 8) adds a fixed switch back 8 m before the gap, built after `BuildBreak` with the next free piece index.
    - The live back is left straight. A stall past it must grind to the fixed back's points (`WatchdogBreak`), where `TakeBack` takes the train home: no fall, arriving at stop 3.

    Cost: none (test).
21. Ruling: residual 2. `SwitchBackRules.Late(taken, thrown, front, pointsS)` replaces `TooLate` and returns `None | TooLate | AlreadyThrown`.
    - The server tells "Too late to throw it." only for `TooLate` (the lever straight).
    - A press on a thrown deadly lever at or past its points, or after it is taken, is ignored quietly and never un-throws it.
    - The RIDE pass proves it with a second, mashed press.

    Cost: none.
22. Ruling: flows that are not about the runaway (`--test-crew`, `--test-chase`, `--test-ride`, `--test-switch-back`, `--test-barricade`, `--test-crevasse`) hold it with `RailEncounterController.DebugHoldRunaway(4)` and log `[<flow>] the runaway on ride 4 held by the test`. The RIDE pass keeps it real — cost: those flows no longer meet it.
23. Ruling: legibility is a gate (Task 7). It uses windowed screenshots from the seat and physics rays from the seated eye, and any failure stops and reports. A fix inside that task is allowed only for dressing (a lamp's energy or range, or a second lamp); anything else is reported. The checks:
    - the dark's mean luminance is ≤ 0.6 of a lit shot on ride 4;
    - the split's red lamp and the runaway's orange lamp show (pixel counts);
    - the rays to them are clear.

    Cost: one or two short windowed runs.

## Global Constraints

- **Defaults reproduce today byte for byte.**
  - With `SplitRide` 0, or `Crevasses` false, every floor's `ContentHash` is unchanged.
  - This is pinned by `RailLineTests.DefaultKnobs_BuildTodaysFloors`, which must stay green in every task, and by `--rehash-plans` changing only `floor_12.json`'s `LayoutHash`.
- **No rng.** `CarveChasms` draws from no rng. The rail pass still draws only from `new Random(seed ^ 0x2A11)`.
- **Floor 12's shape.** Room indices and order never change. Floor 12 must still generate:
  - `objective=jailbreak rails=6`;
  - five rides, each ≥ 160 m, and a total ≥ 1200 m;
  - a cavern span;
  - `break=` on ride 3;
  - from Task 3 on, `split=` on ride 4.
- **The wire.** `Protocol.Version` stays **17** (the branch is unreleased). Split cells come from the shared seed. New state that clients must see goes in existing messages (`PropEvt`, `RailPropsDto`, `CartStateDto`).
- **Lethality.** Lethality never changes a fall: no crevasse or split code reads `TierRules.Lethality`. The ram's HP hit reads `TierRules.PartyDamageScale` (Ruling 12).
- **Assets.** CC0 assets only (the repository is public). This plan adds no assets: meshes are procedural, and the cues are existing names (`cart_bell`, `switch_clunk`, `timber_smash`, `crate_break`, and the wreck's and plunge's own).
- **Voice.** No voice spend: new lines are subtitle-only (`"VoiceId": null`). Never run a Runware/voice bake.
- **Hand-edited JSON** keeps the game's escaping: `'` → `'`, `&` → `&`, `<` → `<`, `>` → `>`, `+` → `+`, non-ASCII → `\uXXXX` (upper-case hex), and a two-space indent. `git diff` must show only the values you meant to change.
- **Tests.** `tests/` (xUnit) is not in git. Write and run tests locally, and never `git add tests/`. Commit only `src/`, `data/`, `docs/` and `scripts/`.
- **Headless runs.**
  - Run `dotnet build DungeonCrawler.csproj` before any `godot --headless --path . -- <flags>` run. Engine flags such as `--headless --path .` go before `--`, and game flags go after it.
  - Every headless check greps its own flow's `[...] RESULT` line, never the early `[builtin] RESULT ok`.
  - `--test-crew` needs `--quit-after=570` and `--test-chase` needs `--quit-after=750`. `--test-switch-back` (any variant) sets 300, and the new `--test-runaway` (any variant) sets `_quitAfter = 300`.
  - Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N` (or `--quit-when-empty`).
- **The RIDE pass.**
  - `RIDE=1 scripts/net_test.sh` runs alone, with nothing else on the machine.
  - It needs the laptop out of the low-power platform profile: `cat /sys/firmware/acpi/platform_profile` must not read `low-power`, and the script warns when it does. A run in low-power is invalid, not a failure: stop and tell the controller.
  - It is flaky at baseline (about 1 run in 3–4): a failure in a step this plan did not touch is rerun once before it is investigated.
- Full test suite: `dotnet test tests/DungeonCrawler.Tests.csproj`.
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No installers or dist builds (`export_all.sh`, `package.sh`, `publish_*.sh`), no push and no merge. The controller commits this plan; implementers commit only their task's files, staged by explicit path.

## Review Focus

- **A co-op client whose cart runs ahead presses the split on its first frame past the points**, while the server's front is still short. The party must never go down the spur.
  - Pinned by `SplitRulesTests.AThrowJustShortOfThePoints_IsHeld_ASetBackNever` (Task 6).
  - Pinned by `--test-runaway=hold` (Task 6).
  - Pinned by the RIDE pass's ride-4 press (Task 8).
- **An early throw corrected by a set-back**, from the same rider or a second rider within 1 s. The set-back must be taken at once and the train must stay on the main line.
  - Pinned by `SplitRulesTests.ASetBack_IsGuardedOnlyPastThePoints` (Task 6).
  - Pinned by `=hold`'s throw, set-back and held throw (Task 6).
- **The runaway reaching stop 4, where the party is getting out on the platform.** It must halt short of the held train and run nobody over.
  - Pinned by `RunawayRulesTests.AHeldTrain_IsNeverRammed` (Task 5).
  - Pinned by `--test-runaway=tail`'s arrival check (Task 5).
- **A wreck, fall or wipe rise with the runaway behind.** The runaway must be gone at once, and the re-armed ride must send a fresh one.
  - Pinned by `--test-runaway=early` (fall, rise, re-arm) (Task 6).
  - Pinned by `=ram`'s low-plating wreck step (Task 5).
- **A walker or a companion near the spur's crevasse, or following in the dark.** Nobody but the train's riders may ever fall.
  - Pinned by the Split rows of the chasm sweeps (`ExitAndFormationCells_AreNeverDrops` and the rest) (Task 2).
  - Pinned by every `--test-runaway` variant's "nobody fell but the train" check (Tasks 5–6).

---

### Task 1: Plan 11's residuals

**Files:**
- Modify: `src/Dungeon/Rails/Core/SwitchBackRules.cs` (`Late`, `LatePress`; `TooLate` removed), `src/Dungeon/Rails/RailEncounterController.Switchyard.cs` (`TooLateToThrow` becomes `LateFor`), `src/Net/ServerWorld.cs` (the deadly-lever branch)
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`DebugFixedBackBeforeGap`, built after `BuildBreak`), `src/Core/DebugTools.SwitchBack.cs` (`=fixedafter`)
- Test: `tests/Dungeon/Rails/SwitchBackRulesTests.cs`

**Interfaces:**
- Produces:
  - `public enum LatePress { None, TooLate, AlreadyThrown }`.
  - `public static LatePress SwitchBackRules.Late(bool taken, bool thrown, float front, float pointsS)`: `None` unless `taken || front >= pointsS` (a NaN front never is); otherwise `thrown ? AlreadyThrown : TooLate`.
  - `public LatePress RailEncounterController.LateFor(TrackSwitch lever)`: `None` unless `Current && lever.Deadly`, and the lever is a piece of `ArmedRide`. It uses `lever.SeatedView`'s window end as the points (Task 6 widens it for the split).
  - `ServerWorld`: `TooLate` → `Tell(slot, TooLateLine)` and prints the existing line. `AlreadyThrown` → prints `[net-server] interact {ref} from {name}: the deadly lever was thrown (taken {taken}): ignored quietly` and `break`s.
  - `internal static float RailEncounterController.DebugFixedBackBeforeGap = float.NaN` (solo flows only). When it is set and a break exists, `BuildSetPieces` adds a `switch_back` piece `r3_switch_back2` on `BreakRide` at `BreakGapS − value`, index `_setPieces.Count`. It is Fixed by `LiveBack` (a lead under 12 m), and `GuardBreak`'s line lists both backs.
  - `--test-switch-back=fixedafter`:
    - it sets the hook to 8;
    - as B: the seated lead throws `r3_switch_out`, and nobody presses the live back (`passed straight`);
    - with the front 3 m past the live back's points, `rc.DebugStall`; the watchdog sets the front at `r3_switch_back2`'s points;
    - its take line says `back from`;
    - `rc.LaneBlend ≤ 0.01` with the front 1 m short of the gap, there is no fall, and the train arrives at stop 3;
    - `[switchback] RESULT ok (fixedafter)`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void TooLate_IsNeverToldToARiderWhoseLeverWasThrownAndTaken()
{
    Assert.Equal(LatePress.AlreadyThrown, SwitchBackRules.Late(taken: true, thrown: true, front: 700f, pointsS: 694f));
    Assert.Equal(LatePress.AlreadyThrown, SwitchBackRules.Late(false, true, 694f, 694f)); // at its points, not yet taken: never un-thrown
    Assert.Equal(LatePress.TooLate, SwitchBackRules.Late(true, false, 700f, 694f));
    Assert.Equal(LatePress.TooLate, SwitchBackRules.Late(false, false, 694f, 694f));
    Assert.Equal(LatePress.None, SwitchBackRules.Late(false, true, 690f, 694f));
    Assert.Equal(LatePress.None, SwitchBackRules.Late(false, false, float.NaN, 694f));
}
```
Rewrite the existing `TooLate` rows as `Late` rows with `thrown: false`.

- [ ] **Step 2: Run it and see it fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~SwitchBackRulesTests"`. Expected: a compile error (`Late`, `LatePress`).
- [ ] **Step 3: Implement** the Interfaces above.
- [ ] **Step 4: Run it.** Run the filter; expect PASS. Then:
  - `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-switch-back=fixedafter 2>&1 | grep -E "\[switchback\] RESULT|r3_switch_back2|watchdog"`. Expected: `r3_switch_back2 … (fixed, thrown)`, the watchdog's `switch back's points` line, `back from`, and `RESULT ok (fixedafter)`.
  - `--test-switch-back` and `=wrong`: `RESULT ok (thrown)` and `ok (wrong)`.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/SwitchBackRules.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Net/ServerWorld.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Core/DebugTools.SwitchBack.cs
git commit -m "fix(rails): a late press on a thrown deadly lever is ignored quietly, never told 'Too late'; --test-switch-back=fixedafter takes a fixed back after a live one through the watchdog and the controller (plan 11 residuals)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: The split's crevasse in the generator

**Files:**
- Modify: `src/Dungeon/Generation/FloorSpec.cs` (`SplitRide`), `src/Adventure/Core/Plans/CampaignPlan.cs` (`SpecOverrides.SplitRide`, `[AiIgnore]`, `IsEmpty`, `Apply` clamp 0..6)
- Modify: `src/Dungeon/Generation/RailLine.cs` (`RailRules.SplitFrom/SplitTo/SplitMinCells/SplitCells/SplitCavernGap`, `record RailSplit`, `RailLine.Split`, `RailLine.SplitWhy`)
- Modify: `src/Dungeon/Generation/FloorGenerator.Chasms.cs` (`CarveSplit`), `src/Dungeon/Generation/LayoutReport.cs`, `src/Core/DebugTools.cs` (`[map] rail split:`)
- Test: `tests/Dungeon/ChasmTests.cs`, `tests/Dungeon/CrevasseRulesTests.cs` (the Split rows)

**Interfaces:**
- Produces:
  - `public int FloorSpec.SplitRide { get; init; }` (default 0).
  - `RailRules`: `SplitFrom = 0.25f`, `SplitTo = 0.55f`, `SplitMinCells = 6`, `SplitCells = 4`, `SplitCavernGap = 3`.
  - `public sealed record RailSplit(int Ride, RailSpan Crevasse, int Points)`.
  - `RailLine.Split : RailSplit?` and `RailLine.SplitWhy : string?` (`null`, `"ok"`, `"none"` or `"refused"`). Neither is part of the content hash.
  - `private static (RailSplit? Split, string Why) CarveSplit(FloorLayout layout, RailLine rail, int ride, int sign, HashSet<GridPos> pockets, HashSet<GridPos> dropped)`, per Ruling 2.
    - It is called in `CarveChasms` after `CarveBreak`.
    - It is refused when `ride` is the break's ride or `k >= rail.Stops.Count − 2`.
    - Its crevasse is appended to `Strips` in line order.
  - `LayoutReport`: ` split={Points}@{f} crevasse={From}..{To}` (f is the points' fraction of their ride, measured as the break's is), or ` split={SplitWhy}`, appended after the break.
  - `--print-map` prints `[map] rail split: ride {r} points at party S={s:0.0}, crevasse S={from:0.0}..{to:0.0}`. The points are the cell's `ClosestS`; the crevasse runs from its first cell's near edge to its last cell's far edge.
  - Tests: `internal static FloorLayout ChasmTests.GenSplit(int seed, FloorObjectiveKind obj)` (floor 12's knobs: Ring, `RailCount = 4`, `RailLoop`, `RailBand = 14`, `Crevasses`, `BrokenBridgeRide = 3`, `SplitRide = 4`) and `SplitSeeds()` (the Crevasse seeds whose line has at least five rides). `At(seed, obj, 4)` returns `GenSplit`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SplitRide_DefaultsOff() => Assert.Equal(0, FloorSpec.Default.SplitRide);

[Fact] public void SpecOverrides_CarryAndClampSplitRide()
{
    Assert.Equal(6, new SpecOverrides { SplitRide = 9 }.Apply(FloorSpec.Default).SplitRide);
    Assert.Equal(0, new SpecOverrides { SplitRide = -1 }.Apply(FloorSpec.Default).SplitRide);
    Assert.False(new SpecOverrides { SplitRide = 4 }.IsEmpty);
}

[Theory, MemberData(nameof(SplitSeeds))]
public void TheSplit_IsAnOutwardDropBesideTheSlagLine(int seed, FloorObjectiveKind obj)
{
    var l = GenSplit(seed, obj);
    if (l.Rail is not { Split: { } s } rail) return;
    Assert.Equal(RailRules.SplitCells, s.Crevasse.To - s.Crevasse.From + 1);
    Assert.Equal(s.Crevasse.From - 1, s.Points);
    Assert.Equal(4, RideOf(rail, s.Points));
    Assert.Contains(s.Crevasse, rail.Strips);
    if (rail.Cavern is { } c && RideOf(rail, c.From) == 4) Assert.True(s.Crevasse.To <= c.From - RailRules.SplitCavernGap);
    var sign = FloorGenerator.LineSign(rail);
    for (var i = s.Crevasse.From; i <= s.Crevasse.To; i++)
    {
        var side = FloorGenerator.RailSide(rail.Party[i + 1] - rail.Party[i], sign);
        Assert.True(l.IsWalkable(rail.Party[i]) && l.IsWalkable(rail.Party[i] + side)); // both lanes solid floor
        Assert.False(l.Bridges.ContainsKey(rail.Party[i] + side));                    // no deck on the slag line
        Assert.True(l.IsChasm(rail.Party[i] + side * 2) && l.IsChasm(rail.Party[i] + side * 3));
    }
}

[Fact] public void MostSplitLines_HaveASplit()
    => Assert.True(SplitSeeds().Count(o => GenSplit((int)o[0], (FloorObjectiveKind)o[1]).Rail?.Split != null) * 2 >= SplitSeeds().Count());

[Fact] public void TheSplit_IsRefusedOnTheLastAndTheBreaksRide()
{
    // seed 1, Jailbreak, floor 12's knobs with SplitRide = 5 (the last), then = 3 (the break's)
    Assert.Equal("refused", GenWithSplit(1, FloorObjectiveKind.Jailbreak, 5).Rail!.SplitWhy);
    Assert.Equal("refused", GenWithSplit(1, FloorObjectiveKind.Jailbreak, 3).Rail!.SplitWhy);
}

[Fact] public void LayoutReport_NamesTheSplit()
    => Assert.Matches(@" split=(\d+@0\.\d\d crevasse=\d+\.\.\d+|none)", LayoutReport.Line(GenSplit(1, FloorObjectiveKind.Jailbreak)));
```

Add the Split rows (`(seed, obj, 4)`) to every sweep that takes `CrevasseAndBreakSeeds`:
- `Chasms_CutNothingOff`;
- `NoChasm_OnASpawnChestKeyObjectiveOrPlatformCell`;
- `NoGround_BesideAChasm_IsBelowItsLip`;
- `Strips_LieBesideTheLine_AndAreWalledBehind`, which allows the split's crevasse on the cavern's ride;
- `NoDropOrBridge_TouchesTheUnderLayer`;
- `CrevasseRulesTests.ExitAndFormationCells_AreNeverDrops`.

Use the test file's real helper names where they differ from these (`GenWithSplit` is `GenSplit` with the ride given).

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~ChasmTests|FullyQualifiedName~CrevasseRulesTests"`. Expected: compile errors (`SplitRide`, `Split`, `RailSplit`).
- [ ] **Step 3: Implement** the Interfaces above. Model `CarveSplit` on `CarveBreak` (the same index map, ride window maths, `StripCells(..., way 1)`, `TakenCells` and pocket bookkeeping), but cut no deck and no gap.
- [ ] **Step 4: Run them.** Run the filter, then `RailLineTests` and the full suite. Expected: all PASS, and `DefaultKnobs_BuildTodaysFloors` stays green. If `MostSplitLines_HaveASplit` misses 50%, report the count and do not loosen the cell rules.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Dungeon/Generation/RailLine.cs src/Dungeon/Generation/FloorGenerator.Chasms.cs src/Dungeon/Generation/LayoutReport.cs src/Core/DebugTools.cs
git commit -m "feat(crevasse): FloorSpec.SplitRide carves the split's crevasse outward of the slag line on a straight run short of the cavern; RailLine.Split and the split= report

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Floor 12 gets its split, and ride 4's pieces (the gate)

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (floor 12 `"SplitRide": 4` after `BrokenBridgeRide`)
- Modify: `data/adventures/builtin/floor_12.json` (`LayoutHash`; `r4_dark`, `r4_runaway`, `r4_split` appended; `r4_hounds` only under Ruling 17)
- Modify: `src/Adventure/Core/Validate/PlanValidator.cs` (`SetPieceKinds` + `"dark"`, `"runaway"`, `"split"`), `src/Adventure/Core/Plans/FloorPlan.cs` (the kinds' doc)
- Test: `tests/Adventure/RailEncounterPlanTests.cs`

**Interfaces:**
- Consumes: Task 2's `split=` and `[map] rail split:` lines.
- Produces:
  - Floor 12's three new pieces:

    | Piece | Ride | Kind | At |
    |---|---|---|---|
    | `r4_dark` | 4 | `dark` | f(points − 70) |
    | `r4_runaway` | 4 | `runaway` | f(points − 50) |
    | `r4_split` | 4 | `split` | f(points) |

    Here `f(S) = (S − StopS[3]) / (StopS[4] − StopS[3])`, to 3 decimals, and `points` is the `[map] rail split:` points S. All three are appended in that order.
  - Until Tasks 4–6 the controller skips the three kinds (no `case`, so `node == null`), so the floor plays as today.
  - The measurements go in the task report: ride 4's points and crevasse S, the window's start S and which cell it is on, the dark's span, and the runaway's start S with three crew wagons. Task 7 re-checks them.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void Floor12_Ride4_OpensDarkWithTheRunawayAndTheSplit()
{
    var f = BuiltinFloor(12); // the test file's loader for data/adventures/builtin/floor_12.json
    var p = f.RailSetPieces.Where(x => x.Ride == 4).ToDictionary(x => x.Id);
    Assert.Equal("dark", p["r4_dark"].Kind);
    Assert.Equal("runaway", p["r4_runaway"].Kind);
    Assert.Equal("split", p["r4_split"].Kind);
    Assert.True(p["r4_dark"].At < p["r4_runaway"].At && p["r4_runaway"].At < p["r4_split"].At);
    Assert.Equal(new[] { "r4_dark", "r4_runaway", "r4_split" }, f.RailSetPieces.TakeLast(3).Select(x => x.Id)); // appended
}
```

- [ ] **Step 2: Run it and see it fail.** Run `--filter "FullyQualifiedName~RailEncounterPlanTests"`. Expected: FAIL (`KeyNotFoundException`).
- [ ] **Step 3: Measure.** Set `SplitRide` 4, then:
  - `dotnet build DungeonCrawler.csproj && godot --headless --path . -- --rehash-plans`;
  - `git diff --stat data/` shows `floor_12.json` only, and its `LayoutHash` line only;
  - `timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map`.

  **Stop and report, without retuning, if any of these fails:**
  - `split=` is `none` or `refused`;
  - the crevasse is not on ride 4, short of `cavern=`;
  - `rides`, `cavern=`, `break=` or `objective=jailbreak rails=6` moved;
  - `strips=` is not 8;
  - the window's start (points − 12) is not on the same straight as the points, or the straight is shorter than 12 m before them.
- [ ] **Step 4: Implement.** Add the kinds and the doc, and append the pieces at the measured fractions. Check `r4_hounds` against Ruling 17. Then:
  - `--print-map … | grep -E "repaired|dropped|demoted|adapted"` prints nothing;
  - the filter, the builtin hash tests and the full suite pass.
- [ ] **Step 5: Commit**

```bash
git add data/adventures/builtin/campaign.json data/adventures/builtin/floor_12.json src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs
git commit -m "data(floor 12): ride 4's split crevasse (SplitRide 4) and its dark, runaway and split pieces at the measured places; LayoutHash refreshed

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: The dark stretch

**Files:**
- Create: `src/Dungeon/Rails/Core/DarkRules.cs`, `src/Dungeon/Rails/RailEncounterController.Dark.cs`, `src/Dungeon/Rails/RideDarkness.cs`
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`case "dark"`), `src/Core/Main.cs` (`ApplyDarkness`), `src/Dungeon/DungeonBuilder.cs` (only if the pocket lamps need a lookup by pocket index)
- Test: `tests/Dungeon/Rails/DarkRulesTests.cs`

**Interfaces:**
- Produces:
  - `DarkRules`:
    - constants `Length = 85f`, `Ramp = 6f`, `LampReach = 6f`, `AmbientKept = 0.12f`, `FogKept = 0.25f`, `TorchKept = 0.1f`;
    - `float Factor(float s, float fromS)`: 0 outside `[fromS, fromS + Length]`, a linear ramp over `Ramp` at each end, 1 between, and 0 for NaN;
    - `float Kept(float factor, float kept) => 1 − factor·(1 − kept)`.
  - `RailEncounterController`:
    - `public float DarkFromS { get; private set; } = float.NaN`;
    - `public float DarknessAt(Vector3 at)`: `Factor` of the party track's `Measure` near `at` (within 8 m of either lane), else 0;
    - `private Node3D? BuildDark(Node3D root, Piece piece)`. It puts out the pocket lamps (`RailLamp{i}` lights hidden, `RailLantern{i}` emission off) whose pocket lies within `LampReach` of either lane over the span, and prints `[rails] dark {id} from party S={from:0.0} to {to:0.0}: {n} pocket lamp(s) out`. `n` is counted from the layout's pockets (every other one, as `DungeonBuilder` lights them), so every role prints the same line.
  - `RideDarkness : Node`, added by the controller on a non-headless peer. Each frame it calls `Main.Instance.ApplyDarkness(DarknessAt(camera))` and dims every hero's torch by `Kept(DarknessAt(hero), TorchKept)` from its base energy.
  - `public void Main.ApplyDarkness(float factor)`. From the base values it sets:
    - ambient energy × `Kept(f, AmbientKept)`;
    - fog light colour × `Kept(f, FogKept)`;
    - volumetric fog emission energy × `Kept(f, 0)`.

    It is a no-op when the factor has not changed by 0.01.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheDark_RampsInAndOut()
{
    Assert.Equal(0f, DarkRules.Factor(s: 799f, fromS: 800f));
    Assert.Equal(0.5f, DarkRules.Factor(803f, 800f), 3);
    Assert.Equal(1f, DarkRules.Factor(806f, 800f));
    Assert.Equal(1f, DarkRules.Factor(879f, 800f));
    Assert.Equal(0.5f, DarkRules.Factor(882f, 800f), 3);
    Assert.Equal(0f, DarkRules.Factor(885.1f, 800f));
    Assert.Equal(0f, DarkRules.Factor(float.NaN, 800f));
    Assert.Equal(85f, DarkRules.Length);
}

[Fact] public void InTheDark_OnlyAGlimmerIsKept()
{
    Assert.Equal(1f, DarkRules.Kept(0f, DarkRules.AmbientKept));
    Assert.Equal(0.12f, DarkRules.Kept(1f, DarkRules.AmbientKept), 3);
    Assert.Equal(0.56f, DarkRules.Kept(0.5f, 0.12f), 3);
}
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~DarkRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces above.
- [ ] **Step 4: Run them.** Run the filter; expect PASS. Then `timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map 2>&1 | grep "\[rails\] dark"`. Expected: `dark r4_dark from party S=… to …: N pocket lamp(s) out` with N ≥ 3, and the span matching Task 3's report. The look is proven in Task 7.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/DarkRules.cs src/Dungeon/Rails/RailEncounterController.Dark.cs src/Dungeon/Rails/RideDarkness.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Core/Main.cs
git commit -m "feat(rails): ride 4's dark stretch: its pocket lamps out, and each view and torch darkened by where it is in it; cart lamps, sparks and warning lamps stay lit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `src/Dungeon/DungeonBuilder.cs` to the `git add` only if you changed it.)

### Task 5: The runaway on the party line (on your tail, the ram, the stop)

**Files:**
- Create: `src/Dungeon/Rails/Core/RunawayRules.cs`, `src/Dungeon/Rails/Core/SplitRules.cs` (`Live`, `SpurLength`, `AtSlack` only; Task 6 adds the rest), `src/Dungeon/Rails/RailEncounterController.Runaway.cs`, `src/Core/DebugTools.Runaway.cs`
- Test: also `tests/Dungeon/Rails/SplitRulesTests.cs` (`TheSpur_IsLiveOnlyWithItsEndOverTheCrevasse`)
- Modify: `src/Dungeon/Rails/RailEncounterController.cs` (tick, `OnCartEvent` frees), `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`case "runaway"`: a marker node; `case "split"`: a marker reading `SplitRules.Live` from the layout, until Task 6), `src/Dungeon/Rails/RailNet.Carts.cs` (the client's ram read)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`BarkTrigger.Runaway`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Core/DebugTools.cs` (the flag)
- Modify (the hold, Ruling 22): `src/Core/DebugTools.Crew.cs`, `src/Core/DebugTools.Chase.cs`, `src/Core/DebugTools.Ride.cs`, and any other flow that rides ride 4
- Test: `tests/Dungeon/Rails/RunawayRulesTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Interfaces:**
- Produces:
  - `RunawayRules`:
    - constants `Boost = 0.15f`, `Behind = 15f`, `MinRoom = 2f`, `MinLead = 10f`, `RamReach = 0.25f`, `RamDent = 40f`, `RamHpFraction = 0.20f`, `HaltShort = 3f`, `BellEvery = 2f`, `BellNear = 40f`;
    - `float Target(int crewWagons) => CartPhysics.TargetSpeed(0, crewWagons) * (1 + Boost)`;
    - `float StartS(float trainRearS, float rideStartS)`: `trainRearS − Behind`, or NaN when that is short of `rideStartS + MinRoom`;
    - `bool Sends(bool splitLive, bool chaserRolling, float triggerS, float windowStartS)`: `splitLive && !chaserRolling && windowStartS − triggerS >= MinLead`;
    - `bool Rams(bool trainRolling, float front, float trainRearS)`: `trainRolling && front >= trainRearS − RamReach`;
    - `float HaltS(float trainRearS) => trainRearS − HaltShort − CartRules.HalfLength` (its middle);
    - `float RamDamage(float maxHp, float hp, float partyDamageScale)`: `min(maxHp · RamHpFraction · scale / TierRules.DefaultLethality, max(0, hp − 1))`;
    - `bool RammedAt(float goneFrontS, float trainRearS)`: within 1.5 m.
  - `SplitRules.Live(float pointsS, float crevasseFromS, float crevasseToS)` (Task 6's class, created here with only this, `SpurLength = 11f` and `AtSlack = 8f`): the spur's end, `pointsS + SpurLength`, lies in `[from + 1, to − 1]`. NaN is never live.
  - The controller:
    - `public RunawayWagon? RideRunawayCart`;
    - `public bool RunawayComing` (it runs, is not spent, and its front is short of the split's points);
    - `public int RunawayRams { get; private set; }`;
    - `internal void DebugHoldRunaway(int ride)`;
    - `private void ArmRunaway(int ride)`, `private void TickRideRunaway(float dt, PartyController party)`, `private void FreeRideRunaway(string why)`.
  - `ArmRunaway` breaks the runaway loose when the front first reaches the piece's S. It does this only if `Sends(...)` holds and `StartS` is not NaN; otherwise it prints `[rails] runaway {id}: none ({why})`. When it is sent:
    - `NextCartId()`, a `CartRun` on the party track at `StartS` and `Target`;
    - `AnnounceOnStart`;
    - log "A runaway ore wagon breaks loose behind us!";
    - `RailCalls.Say(…, BarkTrigger.Runaway, "Runaway on the line behind us — pump!")`;
    - `[rails] t=… runaway {id} breaks loose at party S={start:0.0}, {gap:0.0} m behind the train ({wagons} wagon(s)), target {v:0.0} m/s`.
  - `TickRideRunaway` steps it at `Target` and does the following:
    - It rings `cart_bell` every `BellEvery` within `BellNear`.
    - `SmashAhead` and `RunOver` run as for the traffic runaway.
    - **A ram** runs `Smash(RamDent, 1f)`, then the `RamDamage` hits (as `SmashBarricade`'s HP pass) and a derail. It is freed afterwards and prints `[rails] runaway rams the train at S={s:0.0}: plating {p0:0}->{p1:0}; heroes hit {n}`.
    - **At a held stop** it halts at `HaltS` and is freed after `RunawayLinger`.
    - **A stall** is handled by `OwnProgressStuck`.
  - `FreeRideRunaway` runs on a wreck, a fall, a `Restored` event, a chaser or relief start, the halt's linger after an arrival, and the controller leaving.
  - On the client, `RailNet.Carts`: a runaway cart gone with `RammedAt(goneFront, ownTrainRear)` plays the jolt and `timber_smash` once, and prints `[net] ride: the runaway rammed the train here`.
  - `--test-runaway` and its variants are solo, at lethality 3, with enemies culled. The cart is `rc.Restore(3)`, then everyone boards and departs:
    - **`=tail`:**
      - nobody presses, and the lead pumps throughout;
      - the runaway breaks loose 14–16 m behind the last body, and its call is heard;
      - the gap never shrinks below 3 m before stop 4;
      - the train arrives, and the runaway halts 2.5–4 m behind the last body and is gone within 2 s;
      - nobody is run over;
      - `[runaway] RESULT ok (tail)`.
    - **`=ram`:**
      - `PumperCap = 0`, nobody pumps, and the train carries three wagons (restored with the crew of stops 1–3 coupled);
      - one ram: plating −40 ± 0.5, each seated hero −20% ± 1 of max HP and none below 1 HP, the bench NPCs unhurt, nobody unseated;
      - `RunawayRams == 1`, and no second ram;
      - then the step with plating set to 30 before the trigger: the ram wrecks the cart, the runaway is gone in the same tick, and the spare/relief rules run as today;
      - `RESULT ok (ram)`.
    - Every variant checks that nobody fell, and greps its log for `ERROR`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ItRunsAtTheTrainsOwnCruisePlusFifteen()
{
    Assert.Equal(CartRules.Cruise * 1.15f, RunawayRules.Target(crewWagons: 0), 3);
    Assert.Equal(CartPhysics.TargetSpeed(0, 3) * 1.15f, RunawayRules.Target(3), 3);
    Assert.True(CartPhysics.TargetSpeed(1, 3) > RunawayRules.Target(3)); // one pumper holds it off
    Assert.True(CartPhysics.TargetSpeed(0, 3) < RunawayRules.Target(3)); // none lets it close
}

[Fact] public void ItStartsFifteenBehindTheLastBody_OrNotAtAll()
{
    Assert.Equal(795f, RunawayRules.StartS(trainRearS: 810f, rideStartS: 769.5f), 3);
    Assert.True(float.IsNaN(RunawayRules.StartS(786f, 769.5f)));
}

[Fact] public void ItIsSentOnlyWithALiveSplitAndNoChase()
{
    Assert.True(RunawayRules.Sends(splitLive: true, chaserRolling: false, triggerS: 820f, windowStartS: 858f));
    Assert.False(RunawayRules.Sends(false, false, 820f, 858f));
    Assert.False(RunawayRules.Sends(true, true, 820f, 858f));
    Assert.False(RunawayRules.Sends(true, false, 850f, 858f));
}

[Fact] public void AHeldTrain_IsNeverRammed()
{
    Assert.True(RunawayRules.Rams(trainRolling: true, front: 809.8f, trainRearS: 810f));
    Assert.False(RunawayRules.Rams(true, 809.7f, 810f));
    Assert.False(RunawayRules.Rams(false, 812f, 810f));
    Assert.Equal(810f - 3f - CartRules.HalfLength, RunawayRules.HaltS(810f), 3);
}

[Fact] public void TheRam_IsBig_AndNeverDowns()
{
    Assert.Equal(40f, RunawayRules.RamDent);
    Assert.Equal(20f, RunawayRules.RamDamage(maxHp: 100f, hp: 100f, partyDamageScale: 3f), 3);
    Assert.Equal(9f, RunawayRules.RamDamage(100f, 10f, 3f), 3);
    Assert.Equal(0f, RunawayRules.RamDamage(100f, 1f, 3f));
    Assert.True(RunawayRules.RammedAt(goneFrontS: 811f, trainRearS: 810f));
    Assert.False(RunawayRules.RammedAt(800f, 810f));
}

[Fact] public void TheSpur_IsLiveOnlyWithItsEndOverTheCrevasse()
{
    Assert.True(SplitRules.Live(pointsS: 869f, crevasseFromS: 871f, crevasseToS: 887f));
    Assert.False(SplitRules.Live(869f, 879.5f, 895f));
    Assert.False(SplitRules.Live(869f, 860f, 880.5f));
    Assert.False(SplitRules.Live(869f, float.NaN, float.NaN));
}
// RideBarksTests: EveryClass_HasRunawayLines (BarkTrigger.Runaway, VoiceId null)
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~RunawayRulesTests|FullyQualifiedName~RideBarks"`. Expected: compile errors.
- [ ] **Step 3: Implement** the Interfaces above. Keep the traffic runaway (`_runaway`) as it is; the ride's runaway is a separate field.
- [ ] **Step 4: Run them.** Run the filter; expect PASS. Then each of these alone, greping its own RESULT:
  - `--test-runaway=tail` and `=ram`: `RESULT ok (tail)`, `ok (ram)`;
  - `--test-crew --quit-after=570`;
  - `--test-chase --quit-after=750`;
  - `--test-ride`.

  Each expects `RESULT ok`, and the flows other than the runaway's log their hold line.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/RunawayRules.cs src/Dungeon/Rails/Core/SplitRules.cs src/Dungeon/Rails/RailEncounterController.Runaway.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailNet.Carts.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Core/DebugTools.Runaway.cs src/Core/DebugTools.cs src/Core/DebugTools.Crew.cs src/Core/DebugTools.Chase.cs src/Core/DebugTools.Ride.cs
git commit -m "feat(rails): ride 4's runaway ore wagon comes down the party line behind the train at cruise + 15%, bell ringing; it rams a rolling train once (40 plating, 20% HP in the seat) and halts behind a held one

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: The split: throw it after you pass

**Files:**
- Create: `src/Dungeon/Rails/SetPieces/SplitSwitch.cs`, `src/Dungeon/Rails/RailEncounterController.Split.cs`
- Modify: `src/Dungeon/Rails/Core/SplitRules.cs` (the rest), `src/Dungeon/Rails/SetPieces/TrackSwitch.cs` (`WindowEndS`, `HoldThrow`, `ThrowHeld`, the split's prompt), `src/Dungeon/Rails/Core/PropPromptRules.cs`
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`case "split"`: `BuildSplit`; `Piece.Split`), `src/Dungeon/Rails/RailEncounterController.Switchyard.cs` (`SeatedLeverFor`, `InSeatedWindow` and `LateFor` take the split's gate), `src/Dungeon/Rails/RailEncounterController.Net.cs` (`SpurTrack`/`SpurTrackNumber` count `Split`), `src/Dungeon/Rails/RailEncounterController.Runaway.cs` (the spur route), `src/Dungeon/Rails/RailController.Watchdog.cs` (`WatchdogSplit` before the free), `src/Net/ServerWorld.cs` (`GuardsSetBack`), `src/Dungeon/Rails/RailNet.Carts.cs` (the client's plunge off the spur)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`SplitWait, SplitNotYet, SplitNow`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Core/DebugTools.Runaway.cs`
- Test: `tests/Dungeon/Rails/SplitRulesTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Interfaces:**
- Consumes: Task 2's `RailLine.Split`; Task 5's runaway, `SplitRules.Live`.
- Produces:
  - `SplitRules`:
    - constants `LeverPast = 3f`, `SpurOut = 3.5f`, `FallPast = RailController.SwitchStretch + 1f` (7 m), `HoldLead = SeatedSwitchRules.AcceptLead`, `NowPast = 1f`, `WindowAhead = SeatedSwitchRules.WindowAhead − LeverPast` (12 m before the points);
    - `public enum SplitRoute { Main, Spur }` and `SplitRoute PartyRoute(bool thrown, bool ableHumanAboard)`;
    - `bool HoldPress(bool throwing, float front, float pointsS)`: `throwing && front < pointsS && front >= pointsS − HoldLead`;
    - `bool GuardsSetBack(float front, float pointsS) => front >= pointsS`;
    - `bool Offered(bool armed, bool runawayComing, float front, float pointsS, bool taken)`: `armed && runawayComing && !taken && front >= pointsS − WindowAhead`;
    - `public enum SplitCall { None, Wait, NotYet, Now }` and `SplitCall Call(bool runawayComing, bool thrown, float front, float pointsS, bool waitDone, bool notYetDone, bool nowDone)`, per Ruling 9:
      - NotYet: coming, thrown, not done, front in `[points − 12, points − HoldLead)`;
      - Wait: coming, straight, not done, front in `[points − 12, points)`;
      - Now: coming, straight, not done, front ≥ points + `NowPast`;
    - `string Prompt(bool past, bool thrown)`, with the copy of Ruling 5;
    - `bool PlungedOffSpur(float s, float spurLength) => s >= spurLength − SwitchBackRules.PlungeReach`;
    - `const string SafeLineLog = "No one could set the split back — the cart keeps the main line."`.
  - `SplitSwitch : Node3D`:
    - init properties `PointsS`, `LeverRef`;
    - `TrackSwitch Lever`, `TrackSampler Sampler`;
    - `List<Vector3> Build(TrackSampler party, TrackSampler enemy, LaneMap lanes, Material steel, Material sleeper)`, which lays the spur by Ruling 4 and stands the lever by Ruling 5;
    - a red lamp on a post at the spur's end, at the lip beyond the slag line, and a steady amber lamp on the lever's post. Both are `OmniLight3D`s at `SignalRange`/`SignalEnergy` with no shadow;
    - bent rail ends;
    - it prints `[rails] split {id}: points at party S={p:0.0}, lever at S={p+3:0.0}, spur {len:0.0} m, its end over the crevasse S={from:0.0}..{to:0.0} (live)` or `(dead: {why})`.
  - `TrackSwitch`:
    - `public float WindowEndS { get; init; } = float.NaN` (the `SeatedView`'s `PointsS` is `WindowEndS` when it is set). The split sets `float.PositiveInfinity`, so its window ends only by `Taken`.
    - `public Func<Adventurer, bool>? HoldThrow { get; init; }`. In `Interact`, a throw (straight to thrown) with `HoldThrow(actor)` true is recorded and returns.
    - `public void ThrowHeld(string by)` applies it as a normal toggle by `by`.
    - The split's `Prompt` comes from `SplitRules.Prompt`.
  - The controller:
    - `BuildSplit` takes the points from `RailLine.Split`, checks `AtSlack` and `Live`, and sets `p.Switch = split.Lever` (deadly, `OfferedNow = () => SplitRules.Offered(...)`, `HoldThrow`, `WindowEndS = +∞`);
    - `TickSplit` does the following:
      - **the party's points:** `PartyRoute`. The spur calls `Cross(points, NaN)` and logs "The cart takes the spur!"; the fall comes at the front ≥ points + `FallPast`. The main line prints `[rails] split {id}: the train passes straight at S=…`. A held throw is applied after the straight, logged `held to the points ({lead:0.0} m short)`. The R-lone case logs `SafeLineLog`.
      - **the calls:** `SplitRules.Call`, through `RailCalls.Say`, printing `[rails] split {id} called {wait|not yet|now} at front S=…; bark=…`.
      - **the runaway's points:** thrown sends it onto the spur run; at the spur's end it is freed with a quiet plunge. Straight leaves it on the line. Either way `Lever.Taken = true`, printing `[rails] split {id}: the runaway takes the spur / passes straight at S=…`.
    - `internal bool WatchdogSplit()` (Ruling 15) and `internal bool GuardsSetBack(TrackSwitch lever)` (true unless the lever is the split's and the front is short of its points).
    - `LateFor` uses the split's `Taken` as its "points".
  - `ServerWorld`: the seated-window `RefuseSetBack` branch is skipped when `!enc.GuardsSetBack(lever)`.
  - On the client, `RailNet.Carts`: a runaway gone on the split's spur track with `PlungedOffSpur` plays the quiet plunge, printing `[net] ride: the runaway took the spur here and plunged`.
  - The flow's variants:
    - **Default (`=after`):** the lead pumps; at the window the Wait call comes; the lead presses through `VrUse` with the front 1–3 m past the points; there is no `Cross`; the runaway takes the spur and plunges within 3 s; no ram; arrival at stop 4. `RESULT ok (after)`.
    - **`=early`:** the lead presses with the front 9 m short; NotYet is heard; the train crosses and falls with its front within 1 m of points + 7, with "The cart runs off the spur into the crevasse!"; every rider is fallen within 1 s; the wipe rises at stop 3, every hero up within 10 m of a whole cart. After boarding and departing again the runaway breaks loose again. `RESULT ok (early)`.
    - **`=hold`:**
      - the lead throws at 9 m short (NotYet), then sets back at 6 m short (accepted, no refusal line), then throws at 3 m short (`held to the points`);
      - the train passes straight, the lever is thrown just after, and the runaway takes the spur;
      - `RESULT ok (hold)`.
      - Solo has no server refusal path, so the set-back guard past the points (`GuardsSetBack`) is pinned by its unit test and the server branch.
    - **`=stall`:** a throw at 9 m short, then `DebugStall` at 7 m short; the watchdog's "split set back"; the grind on; no fall. `RESULT ok (stall)`.
    - **`=lone`:** a throw at 9 m short, then the lead (the only human, with possession off) is downed in the seat; at the points `SafeLineLog`, the main line, the lever still thrown, and the runaway takes the spur. `RESULT ok (lone)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ThePartysBranch_IsTheSpurOnlyThrownWithAnAbleHuman()
{
    Assert.Equal(SplitRoute.Spur, SplitRules.PartyRoute(thrown: true, ableHumanAboard: true));
    Assert.Equal(SplitRoute.Main, SplitRules.PartyRoute(true, false));
    Assert.Equal(SplitRoute.Main, SplitRules.PartyRoute(false, true));
}

[Fact] public void AThrowJustShortOfThePoints_IsHeld_ASetBackNever()
{
    Assert.True(SplitRules.HoldPress(throwing: true, front: 865.1f, pointsS: 870f));
    Assert.True(SplitRules.HoldPress(true, 869.9f, 870f));
    Assert.False(SplitRules.HoldPress(true, 864.9f, 870f));        // 5.1 m short: thrown now, onto the spur
    Assert.False(SplitRules.HoldPress(true, 870f, 870f));          // past: thrown now, behind us
    Assert.False(SplitRules.HoldPress(throwing: false, 868f, 870f)); // a set-back is never held
    Assert.Equal(SeatedSwitchRules.AcceptLead, SplitRules.HoldLead);
}

[Fact] public void ASetBack_IsGuardedOnlyPastThePoints()
{
    Assert.False(SplitRules.GuardsSetBack(front: 866f, pointsS: 870f));
    Assert.True(SplitRules.GuardsSetBack(870f, 870f));
}

[Fact] public void TheWindow_OpensTwelveShort_AndEndsWhenTheRunawayTakesThePoints()
{
    bool O(float front, bool coming = true, bool taken = false) => SplitRules.Offered(armed: true, runawayComing: coming, front, pointsS: 870f, taken);
    Assert.False(O(857.9f)); Assert.True(O(858f)); Assert.True(O(900f));
    Assert.False(O(880f, taken: true)); Assert.False(O(866f, coming: false));
    Assert.False(SplitRules.Offered(false, true, 866f, 870f, false));
    Assert.Equal(12f, SplitRules.WindowAhead);
}

[Fact] public void TheCalls_WaitNotYetNow()
{
    SplitCall C(float front, bool thrown = false, bool coming = true, bool wait = false, bool notYet = false, bool now = false)
        => SplitRules.Call(coming, thrown, front, pointsS: 870f, wait, notYet, now);
    Assert.Equal(SplitCall.None, C(857.9f));
    Assert.Equal(SplitCall.Wait, C(858f));
    Assert.Equal(SplitCall.None, C(860f, wait: true));
    Assert.Equal(SplitCall.NotYet, C(860f, thrown: true, wait: true));
    Assert.Equal(SplitCall.None, C(866f, thrown: true, wait: true));   // in the hold zone: it will be held
    Assert.Equal(SplitCall.None, C(860f, thrown: true, notYet: true));
    Assert.Equal(SplitCall.None, C(870.9f, wait: true));
    Assert.Equal(SplitCall.Now, C(871f, wait: true));
    Assert.Equal(SplitCall.None, C(875f, thrown: true, wait: true));
    Assert.Equal(SplitCall.None, C(875f, coming: false));
}

[Fact] public void ThePrompt_SaysWhichWayItSends()
{
    Assert.Equal("Throw the split — we take the spur!", SplitRules.Prompt(past: false, thrown: false));
    Assert.Equal("Throw the split behind us", SplitRules.Prompt(true, false));
    Assert.Equal("Set the split back", SplitRules.Prompt(false, true));
    Assert.Equal("Set the split back", SplitRules.Prompt(true, true));
}

[Fact] public void APlungeOffTheSpur_IsReadFromWhereTheRunawayWent()
{
    Assert.True(SplitRules.PlungedOffSpur(s: 10f, spurLength: 11f));
    Assert.False(SplitRules.PlungedOffSpur(5f, 11f));
}
// RideBarksTests: EveryClass_HasSplitLines (SplitWait, SplitNotYet, SplitNow; VoiceId null)
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~SplitRulesTests|FullyQualifiedName~RideBarks|FullyQualifiedName~SeatedSwitchRules|FullyQualifiedName~SwitchBackRules"`. Expected: compile errors.
- [ ] **Step 3: Implement** the Interfaces above, then the flow's variants.
- [ ] **Step 4: Run them.** Run the filter. Then each of these alone, greping its own RESULT:
  - `--test-runaway` (after), `=early`, `=hold`, `=stall`, `=lone`, `=tail`, `=ram`;
  - `--test-switch-back` and `=fixedafter`;
  - `--test-crew --quit-after=570`;
  - `--test-chase --quit-after=750`;
  - `--test-ride`.

  Expected: `RESULT ok` from every one, and `[rails] split r4_split: points at … (live)` in the runaway runs. Then the full suite.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/SetPieces/SplitSwitch.cs src/Dungeon/Rails/RailEncounterController.Split.cs src/Dungeon/Rails/Core/SplitRules.cs src/Dungeon/Rails/SetPieces/TrackSwitch.cs src/Dungeon/Rails/Core/PropPromptRules.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Dungeon/Rails/RailEncounterController.Net.cs src/Dungeon/Rails/RailEncounterController.Runaway.cs src/Dungeon/Rails/RailController.Watchdog.cs src/Net/ServerWorld.cs src/Dungeon/Rails/RailNet.Carts.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Core/DebugTools.Runaway.cs
git commit -m "feat(rails): ride 4's split is live: thrown after the train passes it sends the runaway down the spur into the crevasse; thrown before, the party takes the spur and falls; a throw within 5 m of the points is held to them; called three times, the player's alone

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: Seen in the dark (the legibility gate)

**Files:**
- Modify: `src/Core/DebugTools.Runaway.cs` (the sight rays in every run; `--screenshot=PATH` shots and pixel checks)
- Modify, only within Ruling 23's allowance: `src/Dungeon/Rails/SetPieces/SplitSwitch.cs`, `src/Dungeon/Rails/RailEncounterController.Dark.cs`

**Interfaces:**
- Produces, in every `--test-runaway` run, headless included (layer-1 rays from the seated lead's eye):
  - with the front 8 m short of the split's points, to the lever's amber lamp and to the spur's red lamp: `[runaway] sight: lever {clear|blocked by X}, spur end {clear|blocked by X}`;
  - 3 s after the runaway breaks loose, to its lamp: `[runaway] sight: runaway lamp {clear|blocked by X} at {d:0.0} m`.

  A `blocked` result fails the flow with its own line.
- With `--screenshot=PATH` (windowed, `=after` only), the seated view is saved:
  - `PATH_lit`: the front 10 m before the dark;
  - `PATH_dark`: the dark's middle;
  - `PATH_split`: the front 8 m short of the points;
  - `PATH_behind`: the seated camera turned 180° by the flow, 3 s after the runaway breaks loose.
- Printed from the images: `[runaway] shots: dark/lit luminance {r:0.00}; split red px {n}; behind orange px {m}`.
  - red is r > 0.6, g < 0.3, b < 0.3; orange is r > 0.7, 0.3 < g < 0.65, b < 0.35;
  - the pass is `r ≤ 0.6`, `n ≥ 20`, `m ≥ 20`.

- [ ] **Step 1: Run the gate headless.** `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-runaway 2>&1 | grep -E "\[runaway\] (sight|RESULT)"`. Expected: every ray clear, and `RESULT ok (after)`.
- [ ] **Step 2: Run it windowed, once.** `timeout 300 godot --path . -- --test-runaway --screenshot=/tmp/claude-1000/runaway.png 2>&1 | grep -E "\[runaway\] (shots|RESULT)"`. Expected: the shots line passes. Open the four PNGs (the Read tool shows them) and judge three things:
  - the dark reads as dark;
  - the split's red lamp and the drop's ember glow read at the spur;
  - the runaway's lamp reads behind.
- [ ] **Step 3: On any failure, stop and report** the numbers and the PNG paths. A dressing fix allowed by Ruling 23 may be tried once, followed by one more windowed run; otherwise make no change.
- [ ] **Step 4: Commit** (the flow's checks, plus any allowed dressing)

```bash
git add src/Core/DebugTools.Runaway.cs
git commit -m "test(rails): the dark's legibility gate: sight rays to the split's lamps and the runaway's from the seat; windowed shots of the dark, the split and the runaway behind with luminance and lamp-pixel checks

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `SplitSwitch.cs` or `RailEncounterController.Dark.cs` to the `git add` only if Ruling 23 allowed a change to it.)

### Task 8: Co-op proof, docs and the full sweep

**Files:**
- Modify: `src/Net/NetTestFlow.cs` (ride 3's mashed press; ride 4's split and the runaway on both clients), `scripts/net_test.sh` (the new checks; its header)
- Modify: `docs/development.md` (The five rides, ride 4; Crevasses, the split's crevasse; Riding together, Props; The watchdog; The RIDE net test; Known gaps)

**Interfaces:**
- Produces, in the RIDE pass:
  - **Ride 3:** after the server's `back from`, the first client presses the back once more from the seat. The server prints `ignored quietly`, and `Too late to throw it.` never appears in `client.log`.
  - **Ride 4, both clients aboard:**
    - the server prints `runaway r4_runaway breaks loose`, and both clients see its cart (`[net] ride: the runaway runs behind the train here`);
    - the first client presses the split on its own first frame with the front past the points, and prints `[net] ride: ride 4's split offers "Throw the split behind us" … thrown through the intent from the seat ({past:0.0} m past the points here)`;
    - the server shows `the train passes straight`, applied at once or `held to the points`, and never `The cart takes the spur`;
    - the server then shows `the runaway takes the spur`, and both clients print `the runaway took the spur here and plunged`;
    - there is no `runaway rams`;
    - `[net] ride: dark stretch here from S=… lamps out N` on both clients matches the server's `dark` line.
  - The server keeps the plating up from ride 4 as today.

- [ ] **Step 1: Add the checks** (Interfaces) to `NetTestFlow` and `net_test.sh`, each with its own `NET TEST: FAIL (…)` line.
- [ ] **Step 2: Run the RIDE pass.** First `cat /sys/firmware/acpi/platform_profile`: if it reads `low-power`, stop and tell the controller that the gate needs a non-low-power profile. Otherwise run, alone, `RIDE=1 scripts/net_test.sh`. Expected: `NET TEST: ok` with the new lines. A failure in an untouched step is rerun once.
- [ ] **Step 3: Write the docs.** Say:
  - what each rule is and its number;
  - the flows and their RESULT lines;
  - the `split=` format;
  - the calls' copy;
  - that the RIDE gate needs a non-low-power profile.

  Record the Task 3 and Task 7 measurements.
- [ ] **Step 4: Run the full sweep.** Run the full suite, then each flow alone:
  - `--test-runaway` (all seven variants);
  - `--test-switch-back` (all five);
  - `--test-barricade`;
  - `--test-crevasse`;
  - `--test-crew --quit-after=570`;
  - `--test-chase --quit-after=750`;
  - `--test-ride`.

  Then plain `scripts/net_test.sh`. Every one must show `RESULT ok` (or `NET TEST: ok`), and each flow's log must have no `ERROR`.
- [ ] **Step 5: Commit**

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh docs/development.md
git commit -m "test(net): the RIDE pass throws ride 4's split from a remote seat past its points and sees the runaway take the spur on both screens; a mashed press on a taken live back is ignored quietly; docs: the dark, the runaway and the split

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
