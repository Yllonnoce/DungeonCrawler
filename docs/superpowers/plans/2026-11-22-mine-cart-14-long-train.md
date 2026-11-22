# Mine Cart Chase, Plan 14: the long train, the platform cages, fighting car to car, boarders who throw (floor 12, plan A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12's party cart pulls four open wagons from the landing. The prisoners wait in iron cages on the stop platforms, and when a cage opens they run to the first empty wagon seats. Heroes climb car to car to knock off the hounds that board the wagons. A hound left alone with prisoners throws them off, one at a time, after a warning. New hound groups on rides 1, 3 and 5 carry the fight down the whole line.

**Architecture:**
- **The train is the rail controller's.** A new `TrainWagon` (an open tub the cart's size) replaces the coupled `CageWagon`.
  - `RailController` builds W1..Wn on every role when the crew controller asks (n = one wagon per cage placed, four on floor 12) and couples all of them at the build. "Whenever the cart is restored whole" means `Restore`, a spare's takeover after a fall, and (in plan B) the lift's takeover. Each of these couples them all again (`CoupleFresh`).
  - An uncoupled wagon is a lost wagon: hidden and not solid. Wagons stand only in the train; there are no wagon sidings.
- **Identity by number.** A wagon is `wagon:{k}`, `CartIds.Wagon(k)`, and `k` in `CartStateDto.Train`, the train hash and the RIDE book (`W{k}`). A cage keeps its stop: `cage:{stop}` (new `NetRefs.Cage`), `CartIds.Cage(stop)` for its crew's seats, and `OpenCages` stays a list of stops.
- **The cages.** `PlatformCage` is the old `CageWagon` with the wheels, bed track, coupling and roof spot taken off. It stays an `ICartBody`, so the caged crew stand in its two bench seats as today.
  - An opened cage lets its crew out to follow the party on foot.
  - Whenever the party's train is held at a stop short of the last, every freed crew member who is up, on foot and within 25 m books the first empty wagon seat, walks to it, and sits in within 3 m.
  - The countdown waits for them up to `BoardMax` (8 s). After that, anyone still within 25 m sits in at once.
- **Moves are seat changes** (plan 13's leap pattern).
  - The offer is the target car itself, which is what the seated hero looks at (`LookingAt`), whatever the aim. Its prompt names the move.
  - The press books the target's hero place, and 0.6 s later `SitIn` moves the body there, keeping the seat yaw.
  - A remote press is `InteractIntent(<target car's ref>)`, where pressing your own car's ref is "Get out". It goes over the wire as a `SeatMsg` with the wagon's id.
- **Fighting along the train.**
  - A seated hero's melee lands only on a boarder its own car carries.
  - A blow that met nothing still finds the own car's boarder within `BoarderRules.WagonReach` (the re-keyed `CompanionBrain.BoarderReach`).
  - A wagon boarder has a grip of 3, counted from `EventBus.EnemyHurt` with `BarricadeRules.HitWeight`. At 3 it drops beside the lane alive.
  - Companions answer boarded crewed wagons one move at a time.
- **The throw clock** is engine-free (`ThrowClock`). The encounter controller runs one per coupled wagon: a call, a glow 1.5 s before each throw, the first throw at 6 s, then one every 4 s.
  - It pauses while moves are refused for everyone (`RailController.MovesRefused`: a seated lever window, or the live bridge).
  - It resets when the wagon stops being alone or the train is held.
  - A thrown crew member is gone, not a corpse (`NpcDto.Fled`'s meaning, Ruling 17). Clients see it through `PropEvt wagon:{k} "thrown:{id}"`.
- **No generator change.** Floor 12's `floor_12.json` gains three encounters (Task 6), and the `LayoutHash` stays.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-22-floor-12-long-train-and-exit-island-design.md` (4e913a9), part A only (A.1–A.6). It is an addendum to `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md`, whose anti-goals still bind. Plan B (the exit island, the pit switch, the parked train) is a later plan: nothing here may make it harder. In particular, the train's length, `TrainRules.Length`, the wagon identities, `AboardCrew` and `RailController.MovesRefused` are what B reads.

Earlier rulings: `.superpowers/sdd/mine-cart-rulings.md` (the "Long train + exit island" block last) and `.superpowers/sdd/shared/plan13/rulings.md`. Facts with file:line: `.superpowers/sdd/shared/exit-island-recon.md` (D, F, G) and `.superpowers/sdd/shared/design-builder-check.md`. In `docs/development.md` read: Rails (the ramp table and Riding), "Stops, cages and the crew", "The five rides" (the table, ride 1's beam, slag carts and hound leaps), "Riding together" (Seats and poses, Leaps and boarders, Props, Crew, the RIDE net test, Known gaps) and "Running flows side by side". Line numbers drift: find code by name.

**Measured at 4e913a9** (the spec's numbers, from the recon's `--print-map`/`--track-depth` at 303ef27; nothing since moved the layout):
- `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`; rides 267.2 / 205.6 / 281.3 / 252.4 / 209.2 m.
- Ride 1: `r1_barricade` S 82.8, `r1_lone` 136.2, `r1_beam` 176.3, `r1_thrower` 229.8.
- Ride 3: `r3_switch_out`'s points S 545.1 (lever 540.1, seated window from 525.1), `r3_heap` 587.3, `r3_switch_back`'s points 694.4, the slag-line gap 714.7, `r3_buckets` 722.8, the climb 621.7–630.6.
- Ride 5: `r5_hammers` 1061.3/1064.3/1067.3, `r5_pour` 1106.2, `r5_golem` 1125.0, `r5_trestle` 1134.7–1146.7, `r5_jump` 1156.0–1161.0. Plan B's pit pattern uses x 61, about S 1045–1089.
- Train: car spacing 2.6 m, train 12.6 m; held at stop 0 its rear is at S 4.5.
- Cruise unpumped: 6.0 m/s empty, 6/1.15 = 5.22 m/s with three or more wagons (`CartPhysics.MaxLoadWagons` 3). The ride-3 climb runs at 2.61 m/s with four wagons.
- 9 encounters, 21 set pieces; the caps on five rides are 15 and 25.

**What moves with 5.22 m/s on rides 1–3** (the S values do not; the timings do). Every flow now rides with four wagons from the landing, so each of these is re-run in Task 8. A measured window that moves is re-pinned from the measurement and written in the docs, never loosened silently.

| Flow | What changes |
|---|---|
| `--test-ride`, `--test-ride-edges` | Ride 1 is slower; the downed companion's catch-up distance. |
| `--test-barricade` | The 35 m call is about 6.7 s ahead at 5.22 (5.8 s at 6.0). |
| `--test-switch-back` (all) | The 15 m window is about 2.9 s (2.75 s at 5.45); the 30 m early call. |
| `--test-crew`, `--test-chase` | Rides 1–3 slower; ride 2's slag carts and leaps; crew walking at each stop (Task 2); its two jumps, which already ran with four wagons, are unchanged. |
| `--test-relief` (all) | A relief aims at the train's rear, now 10.4 m behind the cart on every ride. |
| `--test-track-view` | It rides ride 3's climb at 2.61 m/s. |
| `--test-cavern` (all) | It ran ride 4 with an empty train. Four wagons put the rear 10.4 m back and cut `BridgeRules.Pace` to 4.17. "Every cell gone within 6 s after the arrival" and `=stall`'s bound are re-measured. The drop's 25 m call is about 4.8 s (4.16 s). |
| `--test-runaway=ram`, `=tail`, `--test-split` (all) | Four wagons, not three: the ram's rear S (901.6) moves about 2.6 m back, and `=ram`'s `Train.Count != 3` becomes 4. |
| `--test-crevasse` (all), `--test-cart-watchdog` | B's unpumped miss and step 3's lip are unchanged; the wagon checks change (Task 1). |
| The RIDE pass | Ride 1's barricade creep, the joiner's Ready mid-ride 2, ride 3's window and the joiner's throw 2 s in, the chaser's halt behind a 12.6 m train. |

**Planner's rulings** (mine; each decides what the spec left open, with its cost if wrong):
1. Ruling: the wagon count is one per cage placed (`TrainRules.WagonsFor(cages)`): four on floor 12, so the spec's W1..W4. A railed floor with no crew controller, or no cage placed, has no train, as today. The crew controller calls `RailController.BuildTrain(n)` from its `Build` on every role (the rail controller is set up first). Cost: none.
2. Ruling: an uncoupled `TrainWagon` is lost: hidden, layer 0, empty. There is no separate `Lost` flag, and a client hides every wagon that is not in the state's `Train`. Loss is all-or-nothing, since only a fall uncouples (`TakeTheTrain`), so the train is always W1..Wn in order or nothing. Cost: none.
3. Ruling (a contradiction resolved): `LostWagons` is captured (the uncoupled wagons' numbers, null when none) and merged by `RailArrival.Merge`, but no load honours it.
   - Every load runs `RailController.Restore`, which always restores the cart whole (Held, full plating, shown).
   - Under A.1's "any restore of the cart whole couples four fresh wagons and clears it", a load therefore always couples fresh wagons.
   - Restore prints `[rails] train: the save had wagons [..] lost; the cart is restored whole, W1..W4 couple fresh`. `--test-crevasse` reads the field.

   Cost: a field that only records, which is what the builder's check proposed.
4. Ruling: a wagon's hero place is seat 0 at the cart's seat-0 spot, its crew seats are the bench 4 and 5 at the cart's bench spots, and its one rim boarder spot is the cart's rim spot 1 (back right, `MineCart.BoarderLift` above the rim). The tub is the cart's box tub, built by a shared static helper on `MineCart`. Cost: none.
5. Ruling: crew boarding.
   - An opened cage's crew always get out at its door and start following on foot (`StartFollowing`). This also covers a cage opened on foot when the train is elsewhere, and replaces "Let them out".
   - `TickBoarding` (server and solo) seats every freed crew member who is up, alive, on foot and within `BoardReach` (25 m) of the train while it is held at a stop short of the last (not halted on the line). Each one books the first empty seat in `TrainRules.SeatOrder`, walks to the lane side beside its wagon (`MoveTarget`, `MoveOverride`), and sits in at `HopReach` (3 m).
   - A booking is dropped when the train leaves, wrecks or halts, when the crew member goes down, or when it is over 25 m away.
   - Grisel (an escort) never takes a wagon seat.
   - `Npc.SeatedLeadCatchUp` (set down beside the cart when 30 m behind a seated lead) is unchanged, so crew on foot are beside the cart again at the next stop.

   Cost: crew walk at the NPC walk speed (up to about 12 m).
6. Ruling: the countdown waits through `Boarding.Tick(dt, seated, living, hold)`. When the departure would come and `hold` is set, it stays running at 0 s left. `hold` is `TrainRules.HoldsDeparture(onTheWay, Boarding.Elapsed)`: crew on the way and under `BoardMax` (8 s) since the countdown started. At `BoardMax`, everyone on the way within 25 m sits in at once. Cost: a departure can wait 3 s more than today.
7. Ruling: the move.
   - The press books the target's hero place in its seat book. The body stays in its old seat for `HopSeconds` (0.6 s) and counts as aboard the old car; then `SitIn(target, seat, keepYaw: true)`.
   - `cart_jump` plays at the press and `cart_land` at the landing, with a small shake (none in VR).
   - A hop always lands unless the hero went down, left the train or the target was uncoupled meanwhile. In that case the booking is dropped and the hero stays where it sits. A hop already under way is not cancelled by a refusal that starts during it.

   Cost: no arc.
8. Ruling: the direction is read from the camera's flat heading against its seat's forward. That is the desktop's seat yaw and a headset's head yaw alike.
   - More than `BackCone` (100°) off forward is "back".
   - Otherwise, from a wagon it is "forward", and from the cart there is no move.
   - The target is `TrainMoveRules.Target`: the nearest car in that direction with a free hero place, passing taken ones (Q6).
   - Desktop E and the VR grip both reach it through `PartyController`'s use, as the leap's offer does.

   Cost: none.
9. Ruling: "Get out" from a wagon is offered (and taken) only while the train is held, or rolls under 1 m/s, where the rider steps out beside the lane. At speed a wagon offers only moves. E does nothing for `MoveCooldown` (1 s) after a move's press. Cost: a wagon rider cannot throw themselves off at speed.
10. Ruling: the server takes a remote move whose target is still `Target` on its own book. Its cooldown is `MoveCooldown − AcceptSlack` (0.25 s) from the last press it took for that hero. A second press for a place the first booked is refused and printed. Cost: a press 0.75 s after the last, on a laggy link, does nothing.
11. Ruling: moves are refused for everyone while `RailController.MovesRefused`: the train is not rolling, any seated lever window of the armed ride is open (`RailEncounterController.AnyLeverWindow`, the same window that offers a lever to a seated cart rider), or the train rolls on the live cavern bridge (`HoldsRiders`). Plan B adds the exit bridge here. Cost: none.
12. Ruling: who counts as aboard (the audit, Task 3). A hero seated in a coupled wagon counts as aboard for:
    - the departure's player aboard and the seated count (`PlayerAboard`, `CountHeroes`);
    - companions hopping in;
    - an arrival's "no player aboard" set-down;
    - the relief's left-behind test;
    - `CaptureRiders` and a `Restore`'s set-down.

    It does not count for pumps and brakes (`Inputs`, `ServerWorld.RideInputOf`), `AbleToThrow`, the lever window, the leap (from the cart only), the cart's rim spots or `GetOutWithParty`. The fall already takes wagon riders. A wreck sets wagon heroes down beside the lane with the wreck's 25% toll, as cart riders. Cost: none.
13. Ruling: a seated hero's melee (the shapecast, the blade's contact, and the "met nothing" fallback) lands on a train boarder (a hound carried by the cart or a coupled wagon) only when the boarder's car is the hero's own (`BoarderRules.Strikes`). A hero on foot is unaffected, and so are arrows and spells.
    - `WagonReach` replaces `CompanionBrain.BoarderReach`. It is proposed at 2.4 m and measured in Task 4: at least the measured own-spot distance + 0.15, and under `TrainRules.Spacing`.
    - By my estimate, the car ahead's rim spot is about 2.0 m from a wagon's seat 0, so the distance alone cannot keep a blow off a neighbouring car. The own-car rule is what does.

    Cost: an archer in the cart can still shoot W4's hound. That is consistent with the accepted "ranged hits count" ruling.
14. Ruling: the grip.
    - A wagon boarder's grip counts each `EventBus.EnemyHurt` with `byMember ≥ 0` (a party member: a player, a companion, a remote player), weighted by `BarricadeRules.HitWeight(enemy.LastHitKind)`. `Enemy.LastHitKind` is a new getter for the kind its last `ReceiveAttack` took.
    - At 3 the hound drops beside the lane alive (`Drop`, `ForceAggro`). It is logged "{name} knocks the hound off wagon {k}!" and printed `[rails] t=… {hound} knocked off wagon {k} by {name} after {n} hit(s)`.
    - Damage over time and thorns (byMember −1) do not count. The cart's own rim boarders have no grip: they still fall off only when they die.

    Cost: none.
15. Ruling: companions answer (A.3), server and solo, in `TickAnswers`.
    - A wagon needs an answer when it has a boarder aboard, crew seated in it and no up hero.
    - Its answerer is the nearest (by cars) seated, up companion or hero bot not already answering. It moves one car at a time (`TrainMoveRules.AnswerStep`), and only when its cooldown has run and moves are not refused.
    - Once its wagon has had no boarder for `AnswerBack` (5 s), it moves back toward the cart, while the cart has a free hero seat.
    - `DebugHoldAnswers` (a test hook) stops all of this.

    Cost: one companion per boarded wagon.
16. Ruling: the throw.
    - **Alone** is A.4's rule, counting only an up hero, so a downed hero in the wagon does not stop it.
    - **The call.** On each start of an alone stretch, the seated up hero of the train nearest the wagon calls `BarkTrigger.BoarderThrow`, through a new `RailCalls.SayNearest`. With nobody to speak, the log reads "It's at the prisoners — wagon {k}!".
    - **The throws.** The hound glows `ThrowGlow` (1.5 s) before each throw (`TelegraphFx.HandGlow`). It throws seat 5's crew member first, then seat 4's.
    - **The thrown crew member** is unseated beside the lane on the enemy lane's side (`RailController.StandBesideLane`), thrown outward and up, and dies. It is gone 1.2 s later (`ThrownHide`): hidden, `Fled` set (gone, not a corpse), so a save, a load and a client all keep it gone.
    - It is lost (`LostCrew`, since `Dead`), logged "{name} is thrown from the wagon!", and sent as `PropEvt wagon:{k} "thrown:{crewId}"`. Its unseat is the usual `SeatMsg`.

    Cost: no ragdoll.
17. Ruling: `Npc.Throw(Vector3 at, Vector3 velocity)` is the new death. It sets `Dead`, `Fled` and `Escorting` false, unseats, logs nothing (the controller logs), and hides after `ThrownHide`. `NpcDto.Fled`'s doc gains "or thrown from a wagon". Cost: none.
18. Ruling: the added hound groups (A.5).
    - They are `slag_cart` encounters with ember hounds only: `r1_hounds` (1 hound), `r3_hounds` (1) and `r5_hounds` (2), appended after `r5_golem`.
    - The proposed places, which Task 6 measures and gates:
      - `r1_hounds`: pace point S 105.0 (At 0.3331), Offset −8, between the barricade and the lone cinder.
      - `r3_hounds`: At 0.05 (S 502.9, the validator's floor), Offset −5 (beside W2: stop 2's crew), before the switchyard.
      - `r5_hounds`: pace point S 1100.0 (At 0.3705), Offset −8, after x 61 and before the golem (S 1125.0). The golem paces beside the cart at offset 0, 8 m clear of the hound cart.
    - Plan B must re-measure `r5_hounds`. Its train is crossed on the slag line round the pit until its rear is home at about S 1097, so the hound cart's roll-up from 10 m behind its pace point needs about S 1115 there.

    Cost: three more fights.
19. Ruling: a boarding group's emptied cart goes.
    - **The linger.** A slag cart whose riders at spawn were all ember hounds is freed quietly `SetPieceRules.HoundCartLinger` (3 s) after its last rider left it (leapt or died), with no leap of its still in the air and no hero on its deck. `r4_hounds` is included: its empty cart no longer paces the train through the cavern; the leap uses `r4_slag`.
    - **The crossing.** Every live slag cart of the ride whose party-equivalent S is behind the party's front, with no hero on its deck, is freed quietly when the front comes within `CrossFreeAhead` (5 m) of a thrown `switch_out`'s points. So a hound cart never sits where the train crosses (ride 3 now, plan B's pit switch later).
    - **The golem.** `r5_golem`'s burst frees any other live slag cart of its ride.
    - Each is printed `[rails] slag cart {id} ({enc}) freed: {why}`.

    Cost: an empty cart vanishes in view at the crossing.
20. Ruling: `BarkTrigger.BoarderThrow` is appended after `SlagDrop`, a Shout, with subtitle-only stock lines in `RideBarks`: three per roster name (Sable's in a woman's voice and words), two per class, three generic. They name no wagon. Plan B appends its triggers after it. Cost: none.
21. Ruling: the red window. Between Task 1 and Task 2 the crew cannot reach the train (the cages stand still and nobody walks yet), so `--test-crew` is not run in Task 1. Task 1 seats crew through `DebugSeatCrew` in the flows that need them. `--test-chase`'s ride-2 "a companion struck the wagon boarder within 6 s" check is suspended in Task 1 and restored in Task 4 with the answering rule. Every other flow stays green in every task. Cost: none.
22. Ruling: `--test-boarders` (`DebugTools.Boarders.cs`) is one flow with variants `=move|reach|knock|throw|lone|pause|groups`.
    - Default `=throw`. Built-in campaign floor 12, solo, lethality 3, `_quitAfter = 300`.
    - Every enemy is culled as it appears except the test's own hounds (`enc.DebugBoard(k)`); in `=groups` the ride's real hounds are kept.
    - Every check prints its own `[boarders]` line, and the end is `[boarders] RESULT ok ({variant})` or `FAIL (…)`, with plan 13's 10 s timeout watchdog.

    Cost: none.

## Global Constraints

- Protocol.Version stays 17. New state goes in existing messages.
- LayoutHash does not move in plan A (no generator change). `RailLineTests.DefaultKnobs_BuildTodaysFloors` stays green. If plan A adds pieces to floor 12's data, `--rehash-plans=data/adventures/builtin --quit-after=2` must leave the LayoutHash alone.
- No voice spend. New lines are subtitles with VoiceId null, and the speakers' sexes match .superpowers memory (not needed if there is no voice).
- CC0 assets only. The repo is public.
- tests/ (xUnit) is not in git.
- Run `dotnet build DungeonCrawler.csproj` before headless godot. Engine flags go before `--`, game flags after it.
- Every check greps its own flow's RESULT line. Run non-network flows side by side through `scripts/flows.sh`, longest first. While .superpowers/sdd/shared/RIDE.lock exists, nothing builds or runs.
- `--test-crew --quit-after=570` and `--test-chase --quit-after=750`.
- The probe's pass is `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`, with nofloorDrop pinned at PartyTrack 0 / EnemyTrack 40 (`--test-track-view`).
- Only the controller runs the RIDE pass, alone, under the lock. A failure is investigated from its kept logs, never rerun to green.
- Commits end with "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>". Stage by explicit path. No push, merge, installers or dist builds.
- Implementers run on opus, reviewers on fable.
- Also binding:
  - Floor 12's `LayoutHash` is `16995108853750381062`.
  - Nothing new draws from an rng.
  - A flow passes only on exit 0, its own `[...] RESULT ok` line (never the early `[builtin] RESULT ok`) and no `ERROR` line.
  - Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N`.
  - Hand-edited JSON keeps the game's escaping (`'`, `&`, `<`, `>` and `+` as `\u0027`, `\u0026`, `\u003C`, `\u003E` and `\u002B`; non-ASCII as `\uXXXX`, upper-case hex) and a two-space indent, and `git diff` shows only the values meant.
  - The full suite is `dotnet test tests/DungeonCrawler.Tests.csproj`. Never `git add tests/`.
  - Floor-12 dev saves from before this plan are not supported (spec, Constraints): a flow that loads one makes a fresh save.

## Review Focus

- **Two players press for the same wagon place in one tick, or a companion's answer and a player's press meet.** Only the first is booked; the second is refused with its own line. Nobody is ever booked or seated in two places, and two heroes never share one hero place.
  - Pinned by `TrainMoveRulesTests.ASecondPressForABookedPlace_IsRefused` (Task 3).
  - Pinned by `--test-boarders=move`'s double press (Task 3).
- **A hero goes down, is thrown, or the train halts, wrecks or falls while a hop is under way.** The booking is dropped or the hop lands. No body is left half-seated or posed in a car it does not sit in.
  - Pinned by `TrainMoveRulesTests.AHopLands_UnlessTheHeroOrTheTargetIsGone` (Task 3).
  - Pinned by `--test-boarders=move`'s mid-hop down (Task 3).
- **The tick a hero lands in the wagon (or the hound is knocked off) is the tick its throw comes due.** The reset wins: no crew member is thrown once a hero sits in the wagon.
  - Pinned by `ThrowClockTests.TheResetWins_OnTheThrowsOwnTick` (Task 5).
- **A load or a wipe's rise whose `AboardCrew` names a seat now taken, a wagon number missing, or the same seat twice.** The crew member sits in the first empty seat instead and is never dropped. A crew member the arrival had seated stays in the train.
  - Pinned by `TrainRulesTests.AReseat_FallsBackToTheFirstEmptySeat` (Task 2).
  - Pinned by `--test-crew`'s load (Task 2).
- **A crew member still walking to its seat when the train leaves after `BoardMax`, more than 25 m away, or when the cart wrecks or halts.** Its booking is freed, it keeps following on foot, and it boards at the next held stop. It never walks after a moving train, and no booked seat is left empty for good.
  - Pinned by `TrainRulesTests.ABooking_EndsWhenTheTrainLeavesOrTheWalkerFallsBehind` (Task 2).
  - Pinned by `--test-crew`'s straggler step (Task 2).

---

### Task 1: The train from the landing (TrainWagon, identity, the wire, loss and fresh wagons)

**Files:**
- Create: `src/Dungeon/Rails/Core/TrainRules.cs`, `src/Dungeon/Rails/TrainWagon.cs`
- Modify:
  - Rails:
    - `src/Dungeon/Rails/CartTrain.cs` (`TrainWagon`; `WagonSpacing = TrainRules.Spacing`).
    - `src/Dungeon/Rails/MineCart.cs`: the shared box-tub helper and its static spot readers.
    - `src/Dungeon/Rails/RailController.cs`:
      - `Wagons`, `Train`, `WagonAt`, `BuildTrain`, `CoupleFresh`;
      - `Restore` and `SpareTakesOver` couple fresh;
      - the wagons' prompt and use;
      - the arrival snapshot's print.
    - `src/Dungeon/Rails/RailController.Fall.cs`: `TakeTheTrain`, `Plunge` and `SeenFall` on `TrainWagon`.
    - `src/Dungeon/Rails/RailController.Net.cs`: numbers in `Train`, the signature, the boarders and `ApplyTrain`.
    - `src/Dungeon/Rails/RailNet.Seats.cs`: hear and send the wagons by number, and the cages under `CartIds.Cage`.
    - `src/Dungeon/Rails/RailNet.cs`: the book's `W{k}` and `train=`.
    - `src/Dungeon/Rails/RailNet.Leaps.cs`, `src/Dungeon/Rails/RailEncounterController.Leaps.cs`: any coupled wagon, its rim spot, `WireId` by number.
  - Crew controller: `src/Adventure/Objectives/CrewRescueController.cs`:
    - `BuildTrain`; no coupling at departures;
    - `Judge` reads `TrainWagon`;
    - `Capture`/`Restore` without `CoupledCages`/`LostCages`, and `LostWagons` from the rails;
    - `DebugCouple` becomes `DebugSeatCrew`;
    - the cage's ref.
  - Cages: `src/Dungeon/Rails/CageWagon.cs` (its `NetRef` is `cage:{stop}`; no `Coupled`/`Lost`/roof spot).
  - Net:
    - `src/Net/Core/NetRefs.cs` (`CageKind`, `Cage`), `src/Net/Core/CartNet.cs` (`CartIds.Cage`, the docs);
    - `src/Net/NetRefResolver.cs`, `src/Net/ClientWorld.cs` (the cage's "opened"), `src/Net/ServerWorld.cs` (`RailPropState`), `src/Net/PropSync.cs`.
  - Save:
    - `src/Save/Core/SaveDtos.cs` (`LostWagons`; `CoupledCages`/`LostCages` marked retired);
    - `src/Save/Core/RailArrival.cs`;
    - `src/Core/Main.cs` (the rise's print).
  - Party: `src/Party/Adventurer.cs` (`OnWagonRoof` reads a coupled `TrainWagon`; the reach itself is Task 4's).
  - Flows:
    - `src/Core/DebugTools.Chase.cs`, `src/Core/DebugTools.Crevasse.cs`, `src/Core/DebugTools.Runaway.cs`, `src/Core/DebugTools.Cavern.cs`, `src/Core/DebugTools.Crew.cs` (compile only);
    - `src/Net/NetTestFlow.cs` (compile only: numbers for stops; Task 7 rewrites its checks).
- Test: `tests/Dungeon/Rails/TrainRulesTests.cs`, `tests/Save/SaveRunFieldsTests.cs`, `tests/Save/RailArrivalTests.cs`, `tests/Net/NetRefsTests.cs`, `tests/Dungeon/Rails/RampRideTests.cs` (the row's label)

**Interfaces:**
- Produces:
  - `TrainRules` (engine-free, `DungeonCrawler.Dungeon.Rails`):
    - `const float Spacing = 2f * CartRules.HalfLength + 0.4f`;
    - `const int HeroPlace = 0`;
    - `static readonly int[] CrewSeats = { 4, 5 }`;
    - `int WagonsFor(int cages) => Math.Max(0, cages)`;
    - `float Length(int wagons) => 2f * CartRules.HalfLength + wagons * Spacing` (12.6 with four);
    - `IReadOnlyList<(int Wagon, int Seat)> SeatOrder(int wagons)`: W1 seat 4, W1 seat 5, W2 seat 4, and so on;
    - `List<int>? Lost(IReadOnlyList<bool> coupled)`: the 1-based numbers of the uncoupled, or null when none.
  - `TrainWagon : AnimatableBody3D, IInteractable, ICartBody` (Ruling 4):
    - `int Number { get; init; }`, `bool Coupled`, `void SetCoupled(bool)`: coupled, shown and layer 8; uncoupled, hidden, layer 0, `Speed` 0;
    - `CartSeats Seats`, `Riders`, `RiderChanged`, `Speed`, `Curvature`, `Velocity`, `SeatTransform(int)`;
    - `bool HeroPlaceFree`;
    - `Admit`/`Release` raise `RiderChanged`; a crew member's label lift is as `CageWagon`'s;
    - `Enemy?[] Boarders` (one), `BoarderSpotTransform(int)`, `ReserveBoarderSpot(Enemy)`, `FreeBoarderSpot(Enemy)`, `Pose(Transform3D)`;
    - `Func<Adventurer,string>? PromptFor`, `Action<Adventurer>? OnUse`, `string NetRef => NetRefs.Wagon(Number)`.
  - `RailController`:
    - `IReadOnlyList<TrainWagon> Wagons` (every built one, by number) and `IReadOnlyList<TrainWagon> Train` (coupled, nearest first);
    - `TrainWagon? WagonAt(int number)`;
    - `void BuildTrain(int count)`, every role, once per build: it makes `Wagon{k}` under the floor root and couples all;
    - `void CoupleFresh(string why)`, server and solo: every uncoupled wagon is emptied (any rider left is set down quietly), shown and coupled in number order. It prints `[rails] train: W1..W{n} coupled at stop {s} ({why})` and is called by `BuildTrain` ("the build"), `Restore` ("restored whole") and `SpareTakesOver` when `Train.Count < Wagons.Count` ("a spare takes over");
    - `int CarOf(ICartRider r)`: 0 the cart, k a coupled wagon, −1 none;
    - each wagon's `PromptFor`/`OnUse` are the rail controller's. This task offers only `RailController.WagonCrewPrompt`/`WagonCrewUse`, delegates the crew controller sets (Task 2: "Revive {name}").
  - `CartStateDto.Train` and `CrewWagons` hold wagon numbers and their count. A client's `ApplyTrain(List<int>? numbers, double)` couples the listed wagons in order and uncouples (hides) every other. A fall's state plunges the last told train, as today.
  - `NetRefs.CageKind = "cage"`, `NetRefs.Cage(int stop)`; `NetRefs.Wagon(int number)`; `CartIds.Wagon(int number)`; `CartIds.CageBase = 1500`, `CartIds.Cage(int stop)`.
    - `NetRefResolver` resolves `wagon:{k}` through `RailController.WagonAt` and `cage:{stop}` through the crew controller's cages.
    - `RailNet` hears each wagon under `CartIds.Wagon(k)` and each cage under `CartIds.Cage(stop)`. `CartById` resolves both, and the catch-up sends both.
  - `ObjectiveStateDto.LostWagons` (`List<int>?`). `CoupledCages` and `LostCages` stay on the DTO, documented "retired (plan 14): never written, ignored on load". `RailArrival.Merge` copies `LostWagons` from the arrival and no longer copies the retired two.
  - Leaps:
    - `Candidate(i)` is the cart or any coupled wagon, crewed or not;
    - the wagon's spot is its rim spot;
    - `WireId` is `CartIds.Wagon(Number)`;
    - a wagon boarder bites the crew inside (`NearestCrew`), else nobody in particular.
  - `CrewRescueController.DebugSeatCrew(int stop)` (a test hook, server and solo): opens that stop's cage if locked and seats its living crew in the first empty seats at once, printing `[CrewRescue] test: stop {s}'s crew seated in [W{k} seat {n}, …]`.
- Consumes: nothing new.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheTrain_IsOneWagonPerCage_TwelvePointSixMetresWithFour()
{
    Assert.Equal(4, TrainRules.WagonsFor(cages: 4));
    Assert.Equal(0, TrainRules.WagonsFor(0));
    Assert.Equal(2.6f, TrainRules.Spacing, 3);
    Assert.Equal(12.6f, TrainRules.Length(4), 3);
    Assert.Equal(new[] { (1, 4), (1, 5), (2, 4), (2, 5) }, TrainRules.SeatOrder(2));
}

[Fact] public void LostWagons_AreTheUncoupledNumbers_OrNull()
{
    Assert.Null(TrainRules.Lost(new[] { true, true, true, true }));
    Assert.Equal(new[] { 1, 2, 3, 4 }, TrainRules.Lost(new[] { false, false, false, false }));
}
```

  Then update the other tests:
  - In `SaveRunFieldsTests`, `LostCages_RoundTrip` becomes `LostWagons_RoundTrip`, with the same three asserts on `LostWagons`, plus `Assert.Null(old.LostWagons)` on a state from before the field.
  - In `RailArrivalTests`, the merge test takes `LostWagons` from the arrival (`new() { 1, 2 }` against the live's null) and no longer asserts `CoupledCages`.
  - `NetRefsTests` gains `(NetRefs.Cage(2), "cage", "2")` and `CageKind` in the kind list.
  - In `RampRideTests`, the row's label "two pumpers hauling three crew wagons (ride 5 in the story)" becomes "(every ride: the long train)". Its numbers are unchanged.
- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~TrainRulesTests|FullyQualifiedName~SaveRunFieldsTests|FullyQualifiedName~RailArrivalTests|FullyQualifiedName~NetRefsTests"`. Expected: a compile error (`TrainRules`, `LostWagons`, `NetRefs.Cage`).
- [ ] **Step 3: Implement** the Interfaces. The crew still stand in their cages, which no longer couple (Ruling 21). Rewrite the flows' wagon reads:
  - **`--test-chase`.** "Every wagon coupled" becomes "W1..W4 coupled from stop 0" (it fails at its floor check otherwise). Its per-departure coupling check becomes `ctrl.DebugSeatCrew(stop)` after the cage opens, then "stop {s}'s crew sit in the first empty seats". Its ride-2 boarder-answer failure is suspended: print `[chase] 2 … (the answer check waits for Task 4)` in its place. The `CageWagon` bite reads become `TrainWagon`.
  - **`--test-crevasse` B.** `DebugCouple(stop)` becomes `DebugSeatCrew(stop)`. "Wagon {s} lost and hidden, train 0" becomes "W1..W4 uncoupled and hidden, `Train.Count` 0, the seated crew dead". The capture names `LostWagons` [1,2,3,4], and `=survivor`'s capture after the spare's takeover names none (four fresh wagons).
  - **`=survivor` after the load.** "Wagon {s} came back after the load" becomes "W1..W4 coupled, empty, `LostWagons` printed but not honoured" (Ruling 3).
  - **`--test-runaway=ram`.** It drops its couplings, and `Train.Count != 3` becomes `!= 4`.
  - **`--test-split`** (its flow lives in `DebugTools.Runaway.cs`). It gains `[split] beam: r4_beam clears every seated head in the train by {d:0.00} m` (fails under 0.05 m). Each head's top is its eye + `LowBeam.HeadAboveEye`.
  - **`--test-cavern`.** It prints numbers.
  - **`--test-crew` and `NetTestFlow`.** They only compile here (Task 2 and Task 7).
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS, with `DefaultKnobs_BuildTodaysFloors` green. Then, through `scripts/flows.sh`:
  - `--test-chase --quit-after=750`;
  - `--test-crevasse`, `=trestle`, `=survivor`;
  - `--test-runaway=ram`, `=tail`;
  - `--test-split`, `=early`, `=hold`, `=stall`, `=lone`;
  - `--test-cavern`, `=brake`, `=stall`, `=runaway`, `=leap`;
  - `--test-ride`, `--test-ride-edges`, `--test-relief`, `--test-switch-back`, `--test-barricade`, `--test-cart-watchdog`, `--test-track-view`.

  Each must PASS. Each flow's log shows `[rails] train: W1..W4 coupled at stop 0 (the build)`. A window or time that moved with the slower cruise is re-pinned from the measurement, with its old and new values in the task report (never loosened silently). `--test-crew` is not run (Ruling 21).
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/TrainRules.cs src/Dungeon/Rails/TrainWagon.cs src/Dungeon/Rails/CartTrain.cs src/Dungeon/Rails/MineCart.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailController.Fall.cs src/Dungeon/Rails/RailController.Net.cs src/Dungeon/Rails/RailNet.Seats.cs src/Dungeon/Rails/RailNet.cs src/Dungeon/Rails/RailNet.Leaps.cs src/Dungeon/Rails/RailEncounterController.Leaps.cs src/Dungeon/Rails/CageWagon.cs src/Adventure/Objectives/CrewRescueController.cs src/Net/Core/NetRefs.cs src/Net/Core/CartNet.cs src/Net/NetRefResolver.cs src/Net/ClientWorld.cs src/Net/ServerWorld.cs src/Net/PropSync.cs src/Save/Core/SaveDtos.cs src/Save/Core/RailArrival.cs src/Core/Main.cs src/Party/Adventurer.cs src/Core/DebugTools.Chase.cs src/Core/DebugTools.Crevasse.cs src/Core/DebugTools.Runaway.cs src/Core/DebugTools.Cavern.cs src/Core/DebugTools.Crew.cs src/Net/NetTestFlow.cs
git commit -m "feat(rails): the party's cart pulls four open wagons from the landing (TrainWagon, the rail controller's; one per cage); wagons go by number on the wire (wagon:{k}, CartIds.Wagon, the train hash), cages by stop (cage:{stop}); a fall loses them all and any restore of the cart whole couples four fresh; LostWagons saved, CoupledCages/LostCages retired; hounds board any coupled wagon's rim

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: The platform cages, and the crew running to the train

**Files:**
- Create: `src/Dungeon/Rails/PlatformCage.cs` (from `CageWagon.cs`, which is deleted: `git rm src/Dungeon/Rails/CageWagon.cs src/Dungeon/Rails/CageWagon.cs.uid`)
- Modify: `src/Dungeon/Rails/Core/TrainRules.cs` (boarding), `src/Dungeon/Rails/Core/CartSeats.cs` (`Boarding.Tick`'s hold, `Elapsed`), `src/Dungeon/Rails/Core/PropPromptRules.cs` (`CagePrompt` without "Let them out")
- Modify: `src/Adventure/Objectives/CrewRescueController.cs`:
  - `PlatformCage`, `UseCage` and `TickBoarding`;
  - the countdown's hold;
  - `AboardCrew`;
  - the OnFoot let-out;
  - the wagon's revive prompt.
- Modify: `src/Dungeon/Rails/RailController.cs` (`CrewBoardingHolds`, `BoardingElapsed`), `src/Adventure/Core/Objectives/CrewRules.cs` (the docs: two per cage), `src/Adventure/Core/Validate/PlanValidator.cs` (its comment)
- Modify: `src/Save/Core/SaveDtos.cs` (`AboardCrew`, `CrewSeatDto`), `src/Save/Core/RailArrival.cs`
- Modify:
  - Net: `src/Net/NetRefResolver.cs`, `src/Net/ClientWorld.cs`, `src/Net/ServerWorld.cs`, `src/Net/PropSync.cs`, `src/Dungeon/Rails/RailNet.Seats.cs`, `src/Dungeon/Rails/RailNet.cs` (the cage type).
  - Flows: `src/Core/DebugTools.Crew.cs` (rewritten), `src/Core/DebugTools.Chase.cs` and `src/Core/DebugTools.Crevasse.cs` (the crew walk for real where they opened a cage).
- Test: `tests/Dungeon/Rails/TrainRulesTests.cs`, `tests/Dungeon/Rails/CartSeatsTests.cs` (`Boarding`), `tests/Save/SaveRunFieldsTests.cs`, `tests/Save/RailArrivalTests.cs`, `tests/Dungeon/Rails/PropPromptRulesTests.cs` (`CagePrompt`)

**Interfaces:**
- Consumes: Task 1's `TrainWagon`, `Wagons`, `WagonAt`, `SeatOrder`, `DebugSeatCrew`, `CartIds.Cage`.
- Produces:
  - `TrainRules`:
    - `BoardMax = 8f`, `BoardReach = 25f`, `HopReach = 3f`;
    - `bool BoardsHere(bool held, bool halted, bool lastStop) => held && !halted && !lastStop`;
    - `bool HoldsDeparture(int onTheWay, float boardingElapsed) => onTheWay > 0 && boardingElapsed < BoardMax`;
    - `bool SeatsAtOnce(float boardingElapsed, float distance) => boardingElapsed >= BoardMax && distance <= BoardReach`;
    - `bool KeepsBooking(bool boardsHere, bool up, float distance) => boardsHere && up && distance <= BoardReach`;
    - `(int Wagon, int Seat)? Reseat((int Wagon, int Seat) saved, Func<int, int, bool> free, int wagons)`: the saved seat when it exists and is free, else the first free in `SeatOrder`, else null.
  - `Boarding`:
    - `float Elapsed`: seconds since `Start`, hold included;
    - `bool Tick(float dt, int seatedHeroes, int livingHeroes, bool hold = false)`: as today, but a departure due with `hold` set keeps it running at `Left` 0 and returns false.
  - `PlatformCage : AnimatableBody3D, IInteractable, ICartBody`:
    - `CageWagon`'s cage, door, padlock and crew spots on the siding cell, on a plank floor, with no wheels, bed, siding track or roof spot. `Stop`, `PlatformSide`, `Cell`, `Open`, `SetOpen`, `Seats`, `Riders`, `PromptFor`, `OnUse`;
    - `NetRef => NetRefs.Cage(Stop)`; `IReadOnlyList<PlatformCage> CrewRescueController.Cages`.
  - `PropPromptRules.CagePrompt(bool open, bool orcKeys, bool griselOpens, string? downedAboard)`: "Let them out" is gone.
  - `CrewRescueController`:
    - `UseCage` opens a cage the same four ways. An opened cage's living, up crew get out at its door and `StartFollowing` (Ruling 5), and `TickBoarding` takes them from there. A downed one stays in the cage, offered "Revive {name}", and follows once up;
    - `TickBoarding(float dt)`, server and solo, every tick (Ruling 5). It prints:
      - `[CrewRescue] {name} books W{k} seat {n} at stop {s} ({d:0.0} m)`;
      - `[CrewRescue] {name} sits in W{k} seat {n} after {t:0.0} s`;
      - `[CrewRescue] {name} sat in at once at {BoardMax} s ({d:0.0} m)`;
      - `[CrewRescue] {name}'s booking of W{k} seat {n} ends ({why})`;
    - `RailController.BoardingElapsed` is `Boarding.Elapsed` (0 when no countdown runs), and `RailController.CrewBoardingHolds` (`Func<int, bool>?`) is set to `TrainRules.HoldsDeparture(onTheWay at that stop, rails.BoardingElapsed)`. The rail controller passes it as `Boarding.Tick`'s `hold`;
    - the OnFoot event (no carts left): every crew member seated in a wagon gets out beside it and follows;
    - `RailController.WagonCrewPrompt`/`WagonCrewUse` offer "Revive {name}" for a downed crew member seated in that wagon;
    - `Judge`: home is on foot in the exit room, or seated in a coupled `TrainWagon` while the cart is held at the last stop (unchanged in form).
  - `internal bool CrewRescueController.DebugOpen(int stop)` (a test hook, server and solo): opens that stop's cage as a key would (its crew then board by `TickBoarding`), false when there is none or it is open.
  - `CrewSeatDto { string Id; int Wagon; int Seat; }`, `ObjectiveStateDto.AboardCrew` (`List<CrewSeatDto>?`, by wagon then seat, null when none).
    - `Capture` writes it.
    - `Restore` (after the rails' `Restore` has coupled fresh) seats each listed crew member at `TrainRules.Reseat`'s seat, printing `[CrewRescue] load: {name} back in W{k} seat {n}` (and `… (seat {m} taken: first empty)` when it fell back).
    - `RailArrival.Merge` copies `AboardCrew` from the arrival.
  - `--test-crew` (A.6), rewritten:
    - **The floor check.** "a cage at each intermediate stop (4), two caged crew per cage (8), W1..W4 coupled and empty at stop 0, and a gate down at …", every count from `LineShape`.
    - **At each opened cage.** Every one of its living, up crew must sit in the first empty seats in `SeatOrder` before the departure: `[crew] stop {s}: crew [{name} W{k} seat {n}, …] seated {t:0.0} s after the cage opened, before the departure`.
    - **The countdown's wait.** The departure must come after the last of them sits in, and at most `BoardMax` + 0.5 s after the countdown started.
    - **The save and the load** between stops 2 and 3 must bring every `AboardCrew` entry back seat for seat (`[crew] load: AboardCrew [..] back seat for seat`).
    - **The straggler** (Review Focus 5). At stop 3, one freed crew member is held 30 m off (a test `MoveTarget`) while the countdown runs. Its booking must end at the departure, it must follow on foot, and it must board at stop 4 (set down beside the cart there by `SeatedLeadCatchUp`).
    - **Ride 3's seated throw, the downed crew member and the end of the line**, as today. The downed crew member is taken out of W2.
    - **The rebuilt floor.** Stop 1 is left with its cage locked; it must leave W1 empty. The thin-plating brute and the spare follow, then the load at stop 2. Then the cage at stop 2 opens (`ctrl.DebugOpen(2)`, a test hook that opens a cage as a key would), and its two crew must take W1 seats 4 and 5 (`[crew] 10 stop 2's crew took W1 (stop 1's were left)`).
    - The RESULT and timeout lines are unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheCountdown_WaitsForCrewOnTheWay_UpToBoardMax()
{
    Assert.True(TrainRules.HoldsDeparture(onTheWay: 1, boardingElapsed: 7.9f));
    Assert.False(TrainRules.HoldsDeparture(1, 8f));
    Assert.False(TrainRules.HoldsDeparture(0, 1f));
    Assert.True(TrainRules.SeatsAtOnce(boardingElapsed: 8f, distance: 25f));
    Assert.False(TrainRules.SeatsAtOnce(8f, 25.1f));
    var b = new Boarding(); b.Start();
    Assert.False(b.Tick(5f, 1, 1, hold: true));
    Assert.True(b.Running); Assert.Equal(0f, b.Left);
    Assert.True(b.Tick(0.1f, 1, 1, hold: false));
    Assert.Equal(5.1f, b.Elapsed, 3);
}

[Fact] public void ABooking_EndsWhenTheTrainLeavesOrTheWalkerFallsBehind()
{
    Assert.True(TrainRules.KeepsBooking(boardsHere: true, up: true, distance: 24f));
    Assert.False(TrainRules.KeepsBooking(false, true, 2f));
    Assert.False(TrainRules.KeepsBooking(true, up: false, 2f));
    Assert.False(TrainRules.KeepsBooking(true, true, 25.5f));
    Assert.False(TrainRules.BoardsHere(held: true, halted: false, lastStop: true));
    Assert.False(TrainRules.BoardsHere(true, halted: true, false));
}

[Fact] public void AReseat_FallsBackToTheFirstEmptySeat()
{
    var taken = new HashSet<(int, int)> { (1, 4) };
    bool Free(int w, int s) => !taken.Contains((w, s));
    Assert.Equal((2, 5), TrainRules.Reseat((2, 5), Free, wagons: 4));
    Assert.Equal((1, 5), TrainRules.Reseat((1, 4), Free, 4));
    Assert.Equal((1, 5), TrainRules.Reseat((9, 4), Free, 4)); // no wagon 9
}
```

  Then update the other tests:
  - `SaveRunFieldsTests.AboardCrew_RoundTrip`: null on a new state and on one from before the field; `{ Id "npc.crew_1", Wagon 1, Seat 5 }` survives a save.
  - `RailArrivalTests`: the merge takes `AboardCrew` from the arrival.
  - The `CagePrompt` tests: the open-cage case returns "" with nobody down, or "Revive {name}"; "Let them out" is gone.
- [ ] **Step 2: Run them and see them fail.** Run the filter on `TrainRulesTests|SaveRunFieldsTests|RailArrivalTests|PropPromptRules`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces. Then:
  - `--test-chase` and `--test-crevasse` drop Task 1's `DebugSeatCrew` where they open a cage for real: the crew walk.
  - `--test-crevasse` B keeps the hook (it restores at the kiln's stop).
  - `--test-chase`'s per-stop check becomes "stop {s}'s crew sit in the first empty seats before the departure". It also gains `[chase] 1 beam: r1_beam clears every seated head in the train by {d:0.00} m` (fails under 0.05 m).
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then, through `scripts/flows.sh` (longest first): `--test-crew --quit-after=570`, `--test-crew=forgiven --quit-after=570`, `--test-chase --quit-after=750`, `--test-crevasse`, `=trestle`, `=survivor`, `--test-ride`, `--test-relief`. Each must PASS.
  - Report each stop's walk time and countdown wait from `--test-crew`.
  - If `--test-crew` needs more than its 570 s, stop and report the run's length (the budget is a Global Constraint).
- [ ] **Step 5: Commit**

```bash
git rm src/Dungeon/Rails/CageWagon.cs src/Dungeon/Rails/CageWagon.cs.uid
git add src/Dungeon/Rails/PlatformCage.cs src/Dungeon/Rails/Core/TrainRules.cs src/Dungeon/Rails/Core/CartSeats.cs src/Dungeon/Rails/Core/PropPromptRules.cs src/Adventure/Objectives/CrewRescueController.cs src/Dungeon/Rails/RailController.cs src/Adventure/Core/Objectives/CrewRules.cs src/Adventure/Core/Validate/PlanValidator.cs src/Save/Core/SaveDtos.cs src/Save/Core/RailArrival.cs src/Net/NetRefResolver.cs src/Net/ClientWorld.cs src/Net/ServerWorld.cs src/Net/PropSync.cs src/Dungeon/Rails/RailNet.Seats.cs src/Dungeon/Rails/RailNet.cs src/Core/DebugTools.Crew.cs src/Core/DebugTools.Chase.cs src/Core/DebugTools.Crevasse.cs
git commit -m "feat(crew): the prisoners wait in iron cages on the platforms; an opened cage's crew run to the first empty wagon seats and the countdown waits for them up to 8 s; crew on foot board the held train again within 25 m; Let them out goes; AboardCrew saved seat for seat; --test-crew rewritten for the train from the landing

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: Climbing car to car (the seat move)

**Files:**
- Create: `src/Dungeon/Rails/Core/TrainMoveRules.cs`, `src/Dungeon/Rails/RailController.Moves.cs`, `src/Core/DebugTools.Boarders.cs`
- Modify:
  - Rails:
    - `src/Dungeon/Rails/RailController.cs`:
      - the cart's prompt offers "Climb forward into the cart";
      - the wagons' prompt and use;
      - `MovesRefused`;
      - the aboard audit (Ruling 12);
      - a wagon's "Get out" (Ruling 9).
    - `src/Dungeon/Rails/RailController.Relief.cs` and `src/Dungeon/Rails/RailController.Chase.cs` (the audit).
    - `src/Dungeon/Rails/RailEncounterController.Switchyard.cs` (`AnyLeverWindow`).
    - `src/Dungeon/Rails/RailNet.Seats.cs` (a move's cue on a client; `keepYaw`).
  - Party:
    - `src/Party/Adventurer.cs`: `SitIn(…, keepYaw)`; `TickView` sets `LookingAt` to the move's target after the leap and the lever, before the ray.
    - `src/Party/PartyController.cs`, only if the VR grip does not already reach `LookingAt.Interact`.
  - Net:
    - `src/Net/ServerWorld.cs`: a move's target passes the reach check when `MoveAccepts`.
    - `src/Net/ServerWorld.Rails.cs`: the audit.
  - Flows: `src/Core/DebugTools.cs` (the flag and its help line), `src/Core/DebugTools.Cavern.cs` (`=bridge`: no move on the live bridge).
- Test: `tests/Dungeon/Rails/TrainMoveRulesTests.cs`

**Interfaces:**
- Consumes: Task 1's `CarOf`, `Wagons`, `Train`, `TrainWagon.HeroPlaceFree`, `PromptFor`/`OnUse`; plan 13's `HoldsRiders`; `SeatedSwitchRules`.
- Produces:
  - `TrainMoveRules` (engine-free):
    - `HopSeconds = 0.6f`, `MoveCooldown = 1f`, `AcceptSlack = 0.25f`, `BackCone = 100f`, `AnswerBack = 5f`;
    - `int Direction(bool inCart, float lookYawDegrees)`: |yaw| > `BackCone` gives +1 (back); else −1 from a wagon and 0 from the cart;
    - `int? Target(int fromCar, int dir, IReadOnlyList<bool> placeFree)`: `placeFree[0]` is the cart's free hero seat and `placeFree[k]` is Wk's seat 0. It returns the nearest car past `fromCar` in `dir` whose place is free, passing taken ones, or null (and null for `dir` 0);
    - `bool Refused(bool rolling, bool leverWindow, bool bridgeHolds) => !rolling || leverWindow || bridgeHolds`;
    - `bool Offered(bool up, bool inTrain, bool refused, double sincePress) => up && inTrain && !refused && sincePress >= MoveCooldown`;
    - `bool Accepted(bool up, bool inTrain, bool refused, double sinceAccepted, int target, int? serverTarget) => up && inTrain && !refused && sinceAccepted >= MoveCooldown - AcceptSlack && serverTarget == target`;
    - `bool Lands(bool heroUpAndInTrain, bool targetCoupled) => heroUpAndInTrain && targetCoupled`;
    - `string Prompt(int fromCar, int toCar)`: "Climb back to wagon {to}", "Climb forward to wagon {to}" or "Climb forward into the cart";
    - `string Log(string name, int fromCar, int toCar)`: "{name} climbs back to wagon {to}.", "{name} climbs forward to wagon {to}." or "{name} climbs forward into the cart.";
    - `float ThrowBound(int moves) => (moves - 1) * MoveCooldown + HopSeconds + 2f`;
    - `int? AnswerStep(int fromCar, int toCar, IReadOnlyList<bool> placeFree) => Target(fromCar, Math.Sign(toCar - fromCar), placeFree)`.
  - `RailController` (`RailController.Moves.cs`):
    - `bool MovesRefused` (Ruling 11; every role, the peer's own cart);
    - `ICartBody? MoveOfferFor(Adventurer hero)`: every role. It is the local hero's offer: up, seated in the train, human-controlled (companions move by `TickAnswers`, Task 4), `Offered`, and the target is the cart or a coupled wagon;
    - `bool MoveAccepts(Adventurer body, Node target)` (server);
    - `bool Move(Adventurer hero, ICartBody target, string why)` (server and solo). It books the place, starts the hop, plays `cart_jump`, and prints `[rails] move: {name} {back|forward} from car {a} to car {b} ({why}) at S=…`. The landing logs `TrainMoveRules.Log` and prints `[rails] move: {name} lands in car {b} seat {n} after {t:0.00} s`;
    - each hero's last press (local: its own clock; server: its acceptance time);
    - `internal bool DebugSeat(Adventurer hero, int car)` (a test hook, server and solo): seats the hero in that car's hero place at once, as a landed move.
  - The cart's `PromptFor` returns the move's prompt when `MoveOfferFor(hero) == Cart`, and a wagon's `PromptFor` when it is that wagon. Their `OnUse` call `Move` when the actor rides another car of the train. A wagon's own rider is offered "Get out" only under Ruling 9.
  - `RailEncounterController.AnyLeverWindow()`: an armed-ride lever whose seated window holds the party's front (`SeatedSwitchRules.Nearest` with seated and up true).
  - `Adventurer.SitIn(ICartBody cart, int seat, bool keepYaw = false)`.
  - A client's `OnSeat` plays the move's `cart_jump`/`cart_land` where the body left and landed, as the leap does, and keeps the yaw, when both cars are train bodies.
  - The server's reach check lets a `TrainWagon` or the party's `MineCart` past when `MoveAccepts`, and prints `[net-server] interact {ref} from {name}: move {accepted|refused ({why})}`.
  - The aboard audit (Ruling 12):
    - Run `grep -rn "Cart == Cart\b\|Cart != Cart\b\|Cart == _rails.Cart\|Cart != _rails.Cart\|\.Seated\b" src --include=*.cs | grep -v "src/Core/DebugTools\|src/Net/NetTestFlow"`.
    - Each hit that Ruling 12 counts reads `InTrain` (`CarOf(r) >= 0`), and each it does not keep is left with a one-word comment.
    - The task report lists every hit and its group.
  - `--test-boarders=move`. Restored at stop 0; everyone boards; on ride 1, every enemy is culled. The lead's view is turned with `SeatYaw`.
    - **The first move.** Looking back 120°, the prompt is "Climb back to wagon 1". The press (`VrUse`) leaves the lead in the cart for 0.6 s ± one tick, then in W1 seat 0, and the log says "… climbs back to wagon 1.".
    - **The cooldown.** A second press 0.3 s after the first does nothing (still in W1, not on foot). The lead then moves on at the cooldown.
    - **Passing.** A companion is seated in W3's place (`rc.DebugSeat(mate, 3)`, a test hook), so the lead's next back move from W2 lands in W4, one move.
    - **Forward.** From W4, looking forward, the prompt is "Climb forward to wagon 2"; then "… wagon 1"; then "Climb forward into the cart".
    - **The beam.** Under `r1_beam`, the lead rides in W2 and every seated head clears it by ≥ 0.05 m.
    - **The double press** (Review Focus 1). In one tick the lead's press and a companion's forced `rc.Move(mate, W1, "test")` both ask for W1's free place. The first books it and lands; the second is refused (`move refused (the place is booked)`).
    - **The mid-hop down** (Review Focus 2). The lead is downed (`Mangled`) mid-hop. The booking is dropped, the lead stays slumped in its old seat, and W1's place is free again.
    - **Never offered:** at the arrival at stop 1 (held, the prompt "Get out"); while downed; on foot.
  - `--test-cavern=bridge` gains: while `HoldsRiders`, `MoveOfferFor` is null for every hero in the train, and a forced `Move` press is refused.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AMove_GoesToTheNearestFreePlace_PassingTakenOnes()
{
    var free = new[] { false, true, false, false, true }; // cart full, W1 free, W2 and W3 taken, W4 free
    Assert.Equal(1, TrainMoveRules.Target(fromCar: 0, dir: +1, free));
    Assert.Equal(4, TrainMoveRules.Target(1, +1, free));
    Assert.Equal(1, TrainMoveRules.Target(4, -1, free));
    Assert.Null(TrainMoveRules.Target(1, -1, free)); // the cart is full
    Assert.Null(TrainMoveRules.Target(0, 0, free));
}

[Fact] public void TheLook_PicksTheWay()
{
    Assert.Equal(+1, TrainMoveRules.Direction(inCart: true, lookYawDegrees: 101f));
    Assert.Equal(0, TrainMoveRules.Direction(true, 99f));
    Assert.Equal(-1, TrainMoveRules.Direction(false, -40f));
    Assert.Equal(+1, TrainMoveRules.Direction(false, -150f));
}

[Fact] public void Moves_AreRefusedHeldInAWindowOrOnTheBridge_AndWaitTheCooldown()
{
    Assert.True(TrainMoveRules.Refused(rolling: false, leverWindow: false, bridgeHolds: false));
    Assert.True(TrainMoveRules.Refused(true, leverWindow: true, false));
    Assert.True(TrainMoveRules.Refused(true, false, bridgeHolds: true));
    Assert.False(TrainMoveRules.Refused(true, false, false));
    Assert.False(TrainMoveRules.Offered(up: true, inTrain: true, refused: false, sincePress: 0.99));
    Assert.True(TrainMoveRules.Offered(true, true, false, 1.0));
    Assert.False(TrainMoveRules.Offered(up: false, true, false, 5.0));
}

[Fact] public void ASecondPressForABookedPlace_IsRefused()
{
    var free = new[] { false, true, true, true, true };
    Assert.True(TrainMoveRules.Accepted(true, true, false, sinceAccepted: 0.8, target: 1, serverTarget: TrainMoveRules.Target(0, +1, free)));
    free[1] = false; // the first press booked W1
    Assert.False(TrainMoveRules.Accepted(true, true, false, 5.0, target: 1, serverTarget: TrainMoveRules.Target(0, +1, free)));
    Assert.False(TrainMoveRules.Accepted(true, true, false, sinceAccepted: 0.7, 2, 2));
}

[Fact] public void AHopLands_UnlessTheHeroOrTheTargetIsGone()
{
    Assert.True(TrainMoveRules.Lands(heroUpAndInTrain: true, targetCoupled: true));
    Assert.False(TrainMoveRules.Lands(false, true));
    Assert.False(TrainMoveRules.Lands(true, targetCoupled: false));
}

[Fact] public void TheCopy_AndTheThrowBound()
{
    Assert.Equal("Climb back to wagon 3", TrainMoveRules.Prompt(fromCar: 1, toCar: 3));
    Assert.Equal("Climb forward to wagon 1", TrainMoveRules.Prompt(2, 1));
    Assert.Equal("Climb forward into the cart", TrainMoveRules.Prompt(1, 0));
    Assert.Equal("Brand climbs back to wagon 2.", TrainMoveRules.Log("Brand", 1, 2));
    Assert.Equal("Brand climbs forward into the cart.", TrainMoveRules.Log("Brand", 1, 0));
    Assert.Equal(5.6f, TrainMoveRules.ThrowBound(moves: 4), 3);
    Assert.Equal(3, TrainMoveRules.AnswerStep(fromCar: 0, toCar: 3, new[] { false, false, false, true, true }));
}
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~TrainMoveRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces, the audit included.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then:
  - Through `scripts/flows.sh`: `--test-boarders=move`, `--test-cavern`, `--test-crew --quit-after=570`, `--test-ride`, `--test-ride-edges`, `--test-relief`, `--test-switch-back`, `--test-split`. Each must PASS.
  - Then plain `scripts/net_test.sh`, alone, with the lock absent: `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/TrainMoveRules.cs src/Dungeon/Rails/RailController.Moves.cs src/Core/DebugTools.Boarders.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailController.Relief.cs src/Dungeon/Rails/RailController.Chase.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Dungeon/Rails/RailNet.Seats.cs src/Party/Adventurer.cs src/Net/ServerWorld.cs src/Net/ServerWorld.Rails.cs src/Core/DebugTools.cs src/Core/DebugTools.Cavern.cs
git commit -m "feat(rails): a seated hero climbs car to car while the train rolls: a seat change to the nearest free hero place that way, passing taken ones, a 0.6 s hop, 1 s between presses; looked for by the seat's yaw (back past 100 degrees); refused held, in a lever window or on the live bridge; a wagon hero counts as aboard; --test-boarders=move

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `src/Party/PartyController.cs` only if the VR grip needed it.)

### Task 4: Fighting from the wagons (own car only, the grip, companions answering)

**Files:**
- Create: `src/Dungeon/Rails/Core/BoarderRules.cs`, `src/Dungeon/Rails/RailEncounterController.Answers.cs`
- Modify:
  - `src/Party/Adventurer.cs`:
    - `MeleeSwing`, the blade's contact path and its "met nothing" fallback read `BoarderRules.Strikes`;
    - `RoofBoarderInSwing` becomes `CarBoarderInSwing`;
    - the companion's view.
  - Companions: `src/Characters/Companions/CompanionBrain.cs` (`BoarderReach => BoarderRules.WagonReach`; `EnemyView.CarBoarder`, `OtherCarBoarder`).
  - Enemies: `src/Enemies/Enemy.cs` (`LastHitKind`).
  - Encounters:
    - `src/Dungeon/Rails/RailEncounterController.Leaps.cs`: the grip; the knock-off; `DebugBoard`.
    - `src/Dungeon/Rails/RailEncounterController.cs`: the `EnemyHurt` hook; `TickAnswers` in the tick.
  - Flows:
    - `src/Core/DebugTools.Boarders.cs` (`=reach`, `=knock`);
    - `src/Core/DebugTools.Chase.cs`: ride 2's answer check restored under the new rule; `ChaseReachReport` re-keyed.
- Test: `tests/Dungeon/Rails/BoarderRulesTests.cs`, `tests/Characters/CompanionBoarderTests.cs` (rewritten for the rim)

**Interfaces:**
- Consumes: Task 1's `CarOf`, the wagons' rim spot; Task 3's `Move`, `AnswerStep`, `MovesRefused`, `AnswerBack`.
- Produces:
  - `BoarderRules` (engine-free):
    - `WagonReach` (proposed 2.4f; Step 3 sets the measured value);
    - `Grip = BarricadeRules.Toughness`;
    - `int GripTaken(AttackKind kind) => BarricadeRules.HitWeight(kind)`;
    - `bool KnockedOff(int taken) => taken >= Grip`;
    - `bool Strikes(bool strikerSeatedInTrain, int strikerCar, int boarderCar) => !strikerSeatedInTrain || boarderCar < 0 || strikerCar == boarderCar`;
    - `bool Counts(int byMember) => byMember >= 0`;
    - `bool NeedsAnswer(bool boarderAboard, int crewAboard, int upHeroesAboard) => boarderAboard && crewAboard > 0 && upHeroesAboard == 0`.
  - `CompanionBrain.BoarderReach` is `BoarderRules.WagonReach`.
    - `EnemyView.RoofBoarder` becomes `CarBoarder`: a boarder its own car carries, struck up to `BoarderReach`.
    - The new `OtherCarBoarder` flag marks a boarder another car carries. A seated companion's melee never picks it; ranged may.
  - `Enemy.LastHitKind` (`AttackKind`): the kind its last `ReceiveAttack` took.
  - `RailEncounterController`:
    - a wagon `Leap` gains `GripTaken`;
    - the `EnemyHurt` hook (server and solo) adds `GripTaken(LastHitKind)` when `Counts(byMember)`, and at `KnockedOff` does Ruling 14;
    - death still drops it as today;
    - `internal Enemy? DebugBoard(int wagon, bool holdHp = false)` (a test hook) spawns an ember hound landed on Wk's spot (with `holdHp` its HP never drops below 1, so only the grip ends it), prints `[rails] test: a hound boards wagon {k}`, and the culls in the test flows spare it;
    - `internal void DebugKill(Enemy hound)` (a test hook) kills it as a blow would;
    - `TickAnswers(float dt)` (Ruling 15) prints `[rails] answer: {name} answers wagon {k} (from car {c})`, `… reaches wagon {k}`, `… goes back to the cart`;
    - `internal bool DebugHoldAnswers`.
  - `--test-boarders=reach`. Restored at stop 1; on ride 2 the lead moves to W2, and `DebugBoard(k, holdHp: true)` puts hounds on W1, W2 and W3.
    - **The reach.** It prints `[boarders] reach: W2 seat 0 to its own spot {own:0.00} m (straight), {ownBend:0.00} m (in a bend); to W1's spot {a:0.00} m, to W3's {b:0.00} m`.
    - **The lead's 12 forced blows** (every melee attack its class has, aimed at each hound in turn) land on W2's hound only: W1's and W3's never lose HP or grip.
    - **From the cart.** A companion seated in the cart swinging at W1's hound never lands.
  - `--test-boarders=knock`. Restored at stop 1, rolling:
    - **Pass 1.** The lead, in W1, lands three melee blows on W1's hound: grip 1, 2, 3. The hound stands beside the lane alive, not carried, aggro on.
    - **Pass 2.** A spell (a caster companion's, through its ability, or the lead's when it is a caster) knocks off a fresh W1 hound with one hit.
    - **Pass 3.** Crew are seated in W3 (`DebugSeatCrew`) and a hound is put on W3. The nearest cart companion answers, one move per cooldown. It reaches W3 and strikes it (its hit counts toward the grip), the hound is knocked off or killed, and 5 s after W3 is clear the companion moves back toward the cart.
    - **Pass 4.** `DebugKill` of a W1 hound drops it beside the lane.
  - `--test-chase`'s ride-2 check becomes: a hound that boards a crewed wagon with no up hero is answered within `ChaseBoarderAnswer` + 3 s by a companion. Its line prints the answerer, its moves and its first strike.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ASeatedBlow_LandsOnItsOwnCarsBoarderOnly()
{
    Assert.True(BoarderRules.Strikes(strikerSeatedInTrain: true, strikerCar: 2, boarderCar: 2));
    Assert.False(BoarderRules.Strikes(true, 2, boarderCar: 1));
    Assert.False(BoarderRules.Strikes(true, strikerCar: 0, boarderCar: 1)); // from the cart, wagon 1's hound
    Assert.True(BoarderRules.Strikes(strikerSeatedInTrain: false, -1, 3)); // on foot
    Assert.True(BoarderRules.Strikes(true, 2, boarderCar: -1)); // not a train boarder
    Assert.True(BoarderRules.WagonReach < TrainRules.Spacing);
    Assert.Equal(BoarderRules.WagonReach, CompanionBrain.BoarderReach);
}

[Fact] public void TheGrip_IsThreeHitsOrOneSpell_CountedForTheParty()
{
    Assert.Equal(1, BoarderRules.GripTaken(AttackKind.Melee));
    Assert.Equal(1, BoarderRules.GripTaken(AttackKind.Ranged));
    Assert.Equal(3, BoarderRules.GripTaken(AttackKind.Spell));
    Assert.False(BoarderRules.KnockedOff(2));
    Assert.True(BoarderRules.KnockedOff(3));
    Assert.True(BoarderRules.Counts(byMember: 0));
    Assert.False(BoarderRules.Counts(-1)); // thorns, a burn's tick
}

[Fact] public void AWagonNeedsAnAnswer_WithABoarderCrewAndNoHeroUp()
{
    Assert.True(BoarderRules.NeedsAnswer(boarderAboard: true, crewAboard: 1, upHeroesAboard: 0));
    Assert.False(BoarderRules.NeedsAnswer(true, crewAboard: 0, 0));
    Assert.False(BoarderRules.NeedsAnswer(true, 2, upHeroesAboard: 1));
}
```

  `CompanionBoarderTests` is rewritten on `CarBoarder` and `OtherCarBoarder`:
  - `Seated_Melee_StrikesItsOwnCarsBoarder_InBoarderReach`;
  - `Seated_Melee_NeverPicks_AnotherCarsBoarder`;
  - `Seated_Ranged_MayShoot_AnotherCarsBoarder`;
  - `InBoarderSwing` at `BoarderReach` and `BoarderReach + 0.01`.

  The old roof tests (3.83 m, 6.4 m) are retired.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~BoarderRulesTests|FullyQualifiedName~CompanionBoarderTests"`. Expected: a compile error.
- [ ] **Step 3: Measure, then implement.** Implement with `WagonReach` 2.4, run `--test-boarders=reach`, and read the reach line.
  - Set `WagonReach` to max(own, ownBend) + 0.15, rounded up to 0.05, and write the measured numbers in its doc comment as `CompanionBrain.BoarderReach`'s are today.
  - **Stop and report without retuning** if that value is 2.6 or more, or if a bend puts the own spot past it.
  - Then run the full `=reach` and `=knock`.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then, through `scripts/flows.sh`: `--test-chase --quit-after=750` (its ride-2 answer check back on), `--test-boarders=reach`, `=knock`, `=move`, `--test-cavern=leap`, `--test-split`. Each must PASS.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/BoarderRules.cs src/Dungeon/Rails/RailEncounterController.Answers.cs src/Party/Adventurer.cs src/Characters/Companions/CompanionBrain.cs src/Enemies/Enemy.cs src/Dungeon/Rails/RailEncounterController.Leaps.cs src/Dungeon/Rails/RailEncounterController.cs src/Core/DebugTools.Boarders.cs src/Core/DebugTools.Chase.cs
git commit -m "feat(rails): a seated hero's blow lands only on its own car's boarder (the reach re-keyed to the wagon rim, measured under the 2.6 m spacing); a wagon boarder's grip is three hits or one spell (companions' count), and it drops beside the lane alive; companions answer a boarded crewed wagon one car at a time and come back after 5 s clear

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: A boarder alone with the prisoners throws them off

**Files:**
- Create: `src/Dungeon/Rails/Core/ThrowClock.cs`, `src/Dungeon/Rails/RailEncounterController.Throws.cs`
- Modify:
  - Rules: `src/Dungeon/Rails/Core/BoarderRules.cs` (the alone and throw values).
  - Encounters: `src/Dungeon/Rails/RailEncounterController.cs` (`TickThrows` after `TickAnswers`; the clocks cleared with the ride).
  - Calls and barks:
    - `src/Dungeon/Rails/RailCalls.cs` (`SayNearest`);
    - `src/Adventure/Core/Plans/CampaignPlan.cs` (`BarkTrigger.BoarderThrow`);
    - `src/Adventure/Core/Voice/RideBarks.cs`, `src/Adventure/Core/Voice/VoiceLines.cs` (Shout).
  - Crew:
    - `src/Adventure/Npc.cs` (`Throw`);
    - `src/Adventure/Objectives/CrewRescueController.cs` (`ThrowCrew`, `ShowThrown`);
    - `src/Save/Core/SaveDtos.cs` (`NpcDto.Fled`'s doc).
  - Client: `src/Net/ClientWorld.cs` (`(TrainWagon, "thrown:{id}")`).
  - Flows: `src/Core/DebugTools.Boarders.cs` (`=throw`, `=lone`, `=pause`).
- Test: `tests/Dungeon/Rails/ThrowClockTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Interfaces:**
- Consumes: Task 3's `MovesRefused`, `ThrowBound`, `Move`; Task 4's `DebugBoard`, `DebugHoldAnswers`, `NeedsAnswer`; Task 2's crew seats.
- Produces:
  - `BoarderRules`:
    - `ThrowFirst = 6f`, `ThrowEvery = 4f`, `ThrowGlow = 1.5f`, `ThrownHide = 1.2f`;
    - `bool Alone(bool boarderAboard, int crewAboard, int upHeroesAboard, bool rolling) => boarderAboard && crewAboard > 0 && upHeroesAboard == 0 && rolling`;
    - `int? ThrowSeat(bool seat4Taken, bool seat5Taken)`: 5 first, then 4, else null.
  - `enum ThrowStep { None, Call, Glow, Throw }`.
  - `ThrowClock` (engine-free, one per wagon):
    - `float Running` (seconds counted), `int Thrown`, `bool Alone`;
    - `ThrowStep Step(float dt, bool alone, bool paused)`:
      - not alone: reset to zero and `None`;
      - alone after not: `Call` (counting starts from 0; the Call step itself counts no time);
      - paused: nothing changes, `None`;
      - otherwise it counts `dt`; crossing `ThrowFirst + Thrown·ThrowEvery − ThrowGlow` gives `Glow`, and crossing `ThrowFirst + Thrown·ThrowEvery` gives `Throw` (`Thrown++`).
  - `RailCalls.SayNearest(PartyController party, Node3D near, BarkTrigger trigger, string logIfSilent)`: the train's seated up hero nearest `near` speaks, else the log.
  - `TickThrows(float dt)` (server and solo, while a ride is armed):
    - for each coupled wagon, `Step(dt, Alone(…), paused: rails rolling && MovesRefused)`;
    - **on `Call`:** `SayNearest(…, BarkTrigger.BoarderThrow, $"It's at the prisoners — wagon {k}!")`, printed `[rails] t=… wagon {k}: {hound} alone with {n} crew; called by {who}`;
    - **on `Glow`:** `TelegraphFx.HandGlow(hound, ThrowGlow)` and its crouch;
    - **on `Throw`:** `CrewRescueController.ThrowCrew(npc, wagon)` (Rulings 16, 17), printed `[rails] t=… {hound} throws {name} off wagon {k} (seat {s}) after {t:0.0} s alone ({paused:0.0} s paused)`;
    - `internal int Throws`.
  - `CrewRescueController.ThrowCrew(Npc, TrainWagon)` raises `PropEvt(NetRefs.Wagon(k), "thrown:{id}")`. A client's `ShowThrown(string id)` hides that crew member's copy after `ThrownHide`, and the RIDE trace adds `rail-prop`.
  - `BarkTrigger.BoarderThrow`, appended after `SlagDrop`. `RideBarks.Triggers` gains it, with the banks of Ruling 20, and `Voices.BarkDelivery` gives Shout.
  - `--test-boarders=throw` (the default). Restored at stop 1, rolling on ride 2, with `DebugHoldAnswers`. Crew are seated in W3 (two) and a hound is put on W3; every hero is in the cart, the lead included.
    - **The call** is within 0.1 s of the landing.
    - **The glows** are at 4.5 ± 0.1 s and 8.5 ± 0.1 s.
    - **The throws** are at 6.0 ± 0.1 s (seat 5's) and 10.0 ± 0.1 s (seat 4's). Each thrown crew member is dead, `Fled`, hidden 1.2 s later, in `LostCrew`, and logged "… is thrown from the wagon!".
    - **The save and the load.** A save and a load afterwards keep them gone (hidden, no body).
  - `--test-boarders=lone`. As `=throw`, but crew are seated in W4 and a companion is seated in W2's place. The lead presses back from the cart at the cooldown: cart → W1 → W3 (passing W2) → W4.
    - It lands within `ThrowBound(4)` = 5.6 s of the hound's landing. The clock resets and there is no throw.
    - The flow prints `[boarders] lone: in W4 {t:0.00} s after the hound landed, {6 - t:0.00} s to spare`.
  - `--test-boarders=pause`. Restored at stop 2, on ride 3. Crew are seated in W1, and a hound is put on W1 so that 3.0 s of the alone stretch pass before `r3_switch_out`'s seated window opens (front at lever − 15 m).
    - **Inside the window:** `MovesRefused` holds, `MoveOfferFor` is null for every hero, and the clock does not move.
    - **After the window**, the throw comes at 6.0 ± 0.1 s of counted time. It is printed with its paused seconds, which must be the window's length within 0.1 s.
    - **The reset** (Review Focus 3). A second pass presses the lead's move to W1 `HopSeconds` before the throw is due, so it lands on the throw's own tick (the flow prints both ticks, which must be equal). No crew member is thrown.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheClock_CallsGlowsAndThrows_At6sThenEvery4s()
{
    var c = new ThrowClock();
    Assert.Equal(ThrowStep.Call, c.Step(0.5f, alone: true, paused: false));
    var steps = new List<(float, ThrowStep)>();
    for (var i = 0; i < 20; i++) { var s = c.Step(0.5f, true, false); if (s != ThrowStep.None) steps.Add((c.Running, s)); } // the Call step counts no time
    Assert.Equal(new[] { (4.5f, ThrowStep.Glow), (6.0f, ThrowStep.Throw), (8.5f, ThrowStep.Glow), (10.0f, ThrowStep.Throw) }, steps);
}

[Fact] public void ThePause_KeepsTheCount_AndLeavingAloneResets()
{
    var c = new ThrowClock();
    c.Step(0f, true, false);
    for (var i = 0; i < 30; i++) c.Step(0.1f, true, false);
    for (var i = 0; i < 50; i++) Assert.Equal(ThrowStep.None, c.Step(0.1f, true, paused: true));
    Assert.Equal(3.0f, c.Running, 2);
    Assert.Equal(ThrowStep.None, c.Step(0.1f, alone: false, false));
    Assert.Equal(0f, c.Running);
    Assert.Equal(ThrowStep.Call, c.Step(0.1f, true, false));
}

[Fact] public void TheResetWins_OnTheThrowsOwnTick()
{
    var c = new ThrowClock();
    c.Step(0f, true, false);
    for (var i = 0; i < 11; i++) c.Step(0.5f, true, false); // 5.5 s counted
    Assert.Equal(ThrowStep.None, c.Step(0.5f, alone: false, false)); // the step that would reach 6.0 s: a hero landed as it came due
    Assert.Equal(0, c.Thrown);
}

[Fact] public void Alone_CountsOnlyAnUpHero_AndTheFirstThrowCoversFourMoves()
{
    Assert.True(BoarderRules.Alone(boarderAboard: true, crewAboard: 2, upHeroesAboard: 0, rolling: true));
    Assert.False(BoarderRules.Alone(true, 2, upHeroesAboard: 1, true));
    Assert.False(BoarderRules.Alone(true, 2, 0, rolling: false));
    Assert.False(BoarderRules.Alone(true, crewAboard: 0, 0, true));
    Assert.Equal(5, BoarderRules.ThrowSeat(seat4Taken: true, seat5Taken: true));
    Assert.Equal(4, BoarderRules.ThrowSeat(true, false));
    Assert.Null(BoarderRules.ThrowSeat(false, false));
    Assert.True(BoarderRules.ThrowFirst >= TrainMoveRules.ThrowBound(moves: 4)); // A.4: 6 s against 5.6 s
}
```

  In `RideBarksTests`, the triggers list gains `BarkTrigger.BoarderThrow` at its end, and the Shout list gains it. Each name has 3 lines, each class 2, and the generic bank 3.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~ThrowClockTests|FullyQualifiedName~RideBarksTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces. Each stock line names no wagon. Sable's lines are a woman's, and Brand's, Wren's and Ilex's are men's (`.superpowers` memory: character sexes). Every new line has `VoiceId` null.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then, through `scripts/flows.sh`: `--test-boarders`, `=lone`, `=pause`, `=knock`, `=move`, `--test-chase --quit-after=750`, `--test-crew --quit-after=570`, `--test-switch-back`. Each must PASS.
  - The chase's ride 2 must print no throw while the answer rule runs, or, if a throw comes, each must be preceded by its call and glow.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/ThrowClock.cs src/Dungeon/Rails/RailEncounterController.Throws.cs src/Dungeon/Rails/Core/BoarderRules.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailCalls.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Adventure/Npc.cs src/Adventure/Objectives/CrewRescueController.cs src/Save/Core/SaveDtos.cs src/Net/ClientWorld.cs src/Core/DebugTools.Boarders.cs
git commit -m "feat(rails): a hound alone on a wagon with prisoners is called out, glows 1.5 s and throws one off at 6 s and one every 4 s after; the clock pauses while moves are refused and resets when a hero lands, the hound goes or the train is held; a thrown prisoner is lost and gone (PropEvt wagon:{k} thrown); BarkTrigger.BoarderThrow

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: More boarders on rides 1, 3 and 5 (placed, measured, their carts cleared)

**Files:**
- Modify: `data/adventures/builtin/floor_12.json` (`r1_hounds`, `r3_hounds`, `r5_hounds` appended to `RailEncounters`, after `r5_golem`)
- Modify:
  - Rules: `src/Dungeon/Rails/Core/SetPieceRules.cs` (`HoundCartLinger`, `HoundCartGoes`, `CrossFreeAhead`, `CrossingFrees`).
  - Encounters:
    - `src/Dungeon/Rails/RailEncounterController.cs`: the hound cart's linger; a spawn's "hounds only" mark.
    - `src/Dungeon/Rails/RailEncounterController.Switchyard.cs`: the crossing frees.
    - `src/Dungeon/Rails/RailEncounterController.Kiln.cs`: the golem frees.
  - Flows: `src/Core/DebugTools.Boarders.cs` (`=groups`).
- Test: `tests/Adventure/RailEncounterPlanTests.cs`, `tests/Dungeon/Rails/SetPieceRulesTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5 (the wagons, rim spots, moves, grip, answers, throws).
- Produces:
  - `SetPieceRules`:
    - `HoundCartLinger = 3f`, `CrossFreeAhead = 5f`;
    - `bool HoundCartGoes(bool houndsOnly, int livingRiders, int leapsInAir, int heroesOnDeck, float sinceEmpty) => houndsOnly && livingRiders == 0 && leapsInAir == 0 && heroesOnDeck == 0 && sinceEmpty >= HoundCartLinger`;
    - `bool CrossingFrees(bool leverThrown, float frontS, float pointsS, float cartPartyS, int heroesOnDeck) => leverThrown && frontS >= pointsS - CrossFreeAhead && frontS < pointsS && cartPartyS < frontS && heroesOnDeck == 0`.
  - Ruling 19's three frees, each printed `[rails] slag cart {id} ({enc}) freed: {its hounds are gone | the train crosses onto its lane | the golem bursts out}`.
  - Floor 12's encounters, appended in this order (Ruling 18):

    | Id | Ride | Kind | Riders | At | Offset |
    |---|---|---|---|---|---|
    | `r1_hounds` | 1 | `slag_cart` | 1 × `enemy.ember_hound` | f(105.0) = 0.3331 | −8 |
    | `r3_hounds` | 3 | `slag_cart` | 1 × `enemy.ember_hound` | 0.05 (S 502.9) | −5 |
    | `r5_hounds` | 5 | `slag_cart` | 2 × `enemy.ember_hound` | f(1100.0) = 0.3705 | −8 |

    f(S) = (S − StopS[r−1]) / (StopS[r] − StopS[r−1]), to 4 decimals, from Step 3's measured StopS. `Line` is null for all three.
  - `--test-boarders=groups`. Floor 12 solo at lethality 3, ridden from the landing to stop 5 with nobody pumping and every enemy culled but the three groups' hounds. A test hook seats crew in W1..W3, keeping each stop's cage shut so the cage holds are waived through `RideWaiveCages`. Companions answer as in play.
    - For each group it prints `[boarders] group {id}: spawned at party S={s:0.0}, alongside at S={a:0.0}, {hound} leapt at S={l:0.0} onto {W{k}|the cart|a miss}, its cart freed at S={f:0.0} ({why})`.
    - It fails unless:
      - every hound leapt;
      - `r1_hounds` leapt at S ≥ 82.8 + 5 (past the barricade) and ≤ 176.3 (`r1_beam`);
      - `r3_hounds`' cart was freed with the party's front at or short of `r3_switch_out`'s points − 2 m (S 543.1), by either rule;
      - `r5_hounds`' cart was freed before the front reached `r5_golem`'s S (1125.0), or by the golem's burst, and never stood within 3 m of the golem's runner;
      - a throw, if one came, was preceded by its call and glow.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AnEmptiedHoundCart_GoesAfterItsLinger()
{
    Assert.True(SetPieceRules.HoundCartGoes(houndsOnly: true, livingRiders: 0, leapsInAir: 0, heroesOnDeck: 0, sinceEmpty: 3f));
    Assert.False(SetPieceRules.HoundCartGoes(true, 0, 0, 0, 2.9f));
    Assert.False(SetPieceRules.HoundCartGoes(houndsOnly: false, 0, 0, 0, 9f)); // r2_slag2 keeps pacing with its priest dead
    Assert.False(SetPieceRules.HoundCartGoes(true, 0, leapsInAir: 1, 0, 9f));
    Assert.False(SetPieceRules.HoundCartGoes(true, 0, 0, heroesOnDeck: 1, 9f));
}

[Fact] public void TheCrossing_FreesACartBesideTheTrain()
{
    Assert.True(SetPieceRules.CrossingFrees(leverThrown: true, frontS: 540.1f, pointsS: 545.1f, cartPartyS: 535f, heroesOnDeck: 0));
    Assert.False(SetPieceRules.CrossingFrees(true, 540.0f, 545.1f, 535f, 0));
    Assert.False(SetPieceRules.CrossingFrees(leverThrown: false, 541f, 545.1f, 535f, 0));
    Assert.False(SetPieceRules.CrossingFrees(true, 541f, 545.1f, cartPartyS: 549f, 0)); // r3_slag ahead: the ram's rule
}

[Fact] public void Floor12_HasAHoundGroupOnRidesOneThreeAndFive()
{
    var f = ShippedStory.Floor(12);
    var e = f.RailEncounters.ToDictionary(x => x.Id);
    Assert.Equal(new[] { "r1_hounds", "r3_hounds", "r5_hounds" }, f.RailEncounters.TakeLast(3).Select(x => x.Id));
    foreach (var (id, ride, n, offset) in new[] { ("r1_hounds", 1, 1, -8f), ("r3_hounds", 3, 1, -5f), ("r5_hounds", 5, 2, -8f) })
        Assert.Equal((ride, "slag_cart", "enemy.ember_hound", n, offset, (Line?)null), (e[id].Ride, e[id].Kind, e[id].Riders.Single().EnemyId, e[id].Riders.Single().Count, e[id].Offset, e[id].Line));
    var p = f.RailSetPieces.ToDictionary(x => x.Id);
    Assert.True(p["r1_barricade"].At < e["r1_hounds"].At && e["r1_hounds"].At < e["r1_lone"].At);
    Assert.True(e["r3_hounds"].At < p["r3_switch_out"].At);
    Assert.True(p["r5_hammers"].At < e["r5_hounds"].At && e["r5_hounds"].At < e["r5_golem"].At);
}
```

  In `BuiltinFloor12_HasTheFiveRides`, the encounter id list gains the three at its end, and the count becomes 12.
- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~SetPieceRules"`. Expected: FAIL (compile, then the missing ids).
- [ ] **Step 3: Measure and place.**
  - **The static gate.** Run `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | grep -E "\[track-depth\] (piece|encounter) |\[map\] rail StopS"`. It must read `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]` and the pieces at the S values in "Measured" (± 0.1).
  - Compute the three At values from it, and append them with the rules.
  - Re-run: the three `encounter` lines must read S 105.0, 502.9 and 1100.0 (± 0.1).
  - **The dynamic gate.** Run `--test-boarders=groups`.
  - **Stop and report** without retuning if:
    - any `=groups` gate fails;
    - a measured piece S is off by more than 0.1;
    - the `[map] rail` line's `rides`, `cavern=`, `break=` or `split=` moved.

    One retune is allowed, `r3_hounds`' Offset between −5 and −3 (the cart sits nearer its pace point); report it.
- [ ] **Step 4: Run them.** Run the filter and the full suite: PASS. Then:
  - `timeout 60 godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`, then:
    - `git diff --stat data/` shows only `floor_12.json`;
    - `git diff data/` shows only the three appended encounters, with no `LayoutHash` change;
    - `--print-map … | grep -E "repaired|dropped|demoted|adapted"` prints nothing.
  - Through `scripts/flows.sh`: `--test-chase --quit-after=750`, `--test-crew --quit-after=570`, `--test-boarders=groups`, `--test-ride`, `--test-ride-edges`, `--test-barricade`, `--test-switch-back` and its four variants, `--test-split`, `--test-cavern=leap`, `--test-crevasse`. Each must PASS.
- [ ] **Step 5: Commit**

```bash
git add data/adventures/builtin/floor_12.json src/Dungeon/Rails/Core/SetPieceRules.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Dungeon/Rails/RailEncounterController.Kiln.cs src/Core/DebugTools.Boarders.cs
git commit -m "data(floor 12): an ember-hound boarding group on rides 1, 3 and 5, measured and placed (r1_hounds, r3_hounds, r5_hounds); a hounds-only slag cart goes 3 s after its hounds have left it, a cart beside the train goes as it crosses at a switch, and the golem's burst clears the slag line; --test-boarders=groups

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 7: The RIDE pass for the long train (the builder writes it; the controller runs it)

**Files:**
- Modify: `src/Net/NetTestFlow.cs`, `scripts/net_test.sh` (the checks and the header)

**Interfaces:**
- Consumes: every task above; the hooks `DebugBoard`, `DebugHoldAnswers`, `DebugKill`, `DebugSeatCrew`.
- Produces, each with its own `NET TEST: FAIL (…)` line:
  - **`RideTrainCheck`, rewritten (A.6).**
    - **The train at the landing.** At stop 0, before boarding, each client prints `[net] ride: the train here at stop 0: W1..W4 coupled (Train [1, 2, 3, 4])`, and the server prints the same from its own.
    - **The book.** Every compared point has `train=1,2,3,4` on both sides.
    - **Stop 1's crew in W1.** The server prints `[net-server] ride: stop 1's crew seated in W1 (seats 4, 5) at t={t:0.000}` (server time), and each client prints `[net] ride: stop 1's crew seated in W1 here at t={t:0.000}` (its `ServerNow`). The script fails unless each client's is within 1.0 s of the server's.
    - **Dropped and renamed.** The old "the cage wagon coupled here … after the cart left stop 1" print and grep are removed. The trace list's `prop:wagon:1:open` becomes `prop:cage:1:open`.
    - **The crew counts.** "crew seated in wagons: N" reads `rc.Wagons`, and the clients' count reads `TrainWagon` seats.
  - **A remote move and back.**
    - On ride 1, after the barricade is cut and the cart has rolled 10 m, the first client turns its seat yaw to 150° and presses its offer on the first frame it reads "Climb back to wagon 1", through `InteractIntent(wagon:1)`.
    - The server prints `interact wagon:1 from Rider: move accepted` and `move: Rider lands in car 1 seat 0`. Both clients print `[net] ride: Rider rides wagon 1 seat 0 here` from the `SeatMsg`.
    - Later it turns forward and presses "Climb forward into the cart" (`cart:0`). It lands in a cart hero seat on all three, and the client's camera yaw was kept across the first move.
  - **A remote knock-off.**
    - While the first client sits in W1, the server boards a test hound on W1 (`DebugBoard(1)`, once ride 1's own `r1_hounds` has leapt or missed).
    - `FightBoarders` aims at its own car's boarder only (`BoarderRules.Strikes`) and swings through `AttackIntent`.
    - The server prints `knocked off wagon 1 by Rider after {n} hit(s)` with n = 3 at lethality 1 (`DebugBoard(1, holdHp: true)`, so the grip, not the kill, ends it). Both clients show the hound off the wagon (not in the cart state's `WagonBoarders`) within 1 s.
  - **A throw seen on a client.**
    - On ride 2, after the joiner's `joined-mid-ride ok`, the server sets `DebugHoldAnswers` and boards a test hound on W1 (stop 1's crew seated there, both clients in the cart).
    - The server prints the call and `throws {name} off wagon 1 (seat 5) after 6.0 s alone`, within 0.3 s of 6.0.
    - Each client prints `[net] ride: {name} thrown from wagon 1 here (PropEvt), hidden {t:0.0} s later, seated nowhere`. The call is heard as a `BarkMsg` or the log's line.
    - The server then frees the hound (`DebugKill`) before 10 s, and clears `DebugHoldAnswers`.
  - **Every later check that counted stop 1's two crew** now counts one, which is what the thrown one leaves. The wipe's rise brings back the train as it arrived.
  - **Plan 13's cavern checks** read four wagons. Its rear and the chaser's `chase-done` "3 m behind the train's last body" use `TrainRules.Length`.

- [ ] **Step 1: Add the checks** to `NetTestFlow` and `net_test.sh`. The move and the knock-off go after ride 1's barricade checks, and the throw after `joined-mid-ride ok`. Update the header's comment and its timings.
- [ ] **Step 2: Run plain `scripts/net_test.sh`** (alone, lock absent). Expected: `NET TEST: ok`.
- [ ] **Step 3: Hand the RIDE pass to the controller.** Stop and report that the RIDE checks are in, with the expected new lines. The controller runs `RIDE=1 scripts/net_test.sh` alone, under `.superpowers/sdd/shared/RIDE.lock`, on a profile other than `low-power`. Expected: `NET TEST: ok`, with every line above and `wipe-rise watch: ok`. A failure is investigated from its kept logs (`KEEP_TMP=1`), never rerun to green.
- [ ] **Step 4: Commit** (after the controller's pass)

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh
git commit -m "test(net): the RIDE pass for the long train: W1..W4 coupled at the landing on every peer with the train books equal, stop 1's crew in W1 on server and client within 1 s, a remote seat climbing to wagon 1 and back, a remote knock-off by three blows, and a throw seen on both clients (prop:cage:1:open)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: Docs, the re-pinned timings and the full sweep

**Files:**
- Modify: `docs/development.md`
- Modify only to re-pin a measured window (with the measurement in the commit message): any `src/Core/DebugTools.*.cs` whose check moved with the slower cruise.

**Interfaces:**
- Produces, in `docs/development.md`:
  - **Rails.**
    - Riding: drag from ride 1 (`CartInput.CrewWagons` counts the four coupled wagons: 5.22 m/s unpumped from the landing).
    - The ramp table: the "3 crew wagons" row applies from ride 1.
    - The `--test-crew` paragraph, rewritten.
  - **"Stops, cages and the crew"**, rewritten:
    - the train from the landing, the platform cages and opening them;
    - the crew running to the first empty seats; the countdown's wait and `BoardMax`; crew on foot boarding again;
    - "Let them out" gone;
    - a spare's or a restore's fresh wagons;
    - the save (`AboardCrew`, `LostWagons` and why no load honours it, the two retired fields).
  - **"The five rides".**
    - The table's encounters for rides 1, 3 and 5.
    - The encounter count: 12 of 15.
    - Ride 1's beam: "a beam clears every seated head in the train: the cage roof is gone".
    - The slag carts and leaps paragraph: any coupled wagon, the rim spot.
    - New paragraphs on the move, the grip and the throw, with every constant and line.
    - Ruling 19's frees.
  - **"Riding together".**
    - Seats and poses: wagons by number, cages by stop and `CartIds.Cage`.
    - Leaps and boarders: the rim spots, the own-car reach and its measured value, the grip.
    - Remote riding: the move's intent and acceptance.
    - Props: `cage:{stop}` "opened", `wagon:{k}` "thrown:{id}".
    - The RIDE net test: the new steps and their measured numbers from the controller's pass.
    - Known gaps: what the review leaves.
  - Every measured number from Tasks 1–7 (the walk times, the reach, the group S values, the re-pinned windows) is written with where it came from.
  - **The S paragraph under the ride table** says no S moved in plan 14.

- [ ] **Step 1: Write the docs** (Interfaces), checking each moved number against its flow's log.
- [ ] **Step 2: Run the full sweep.**
  - The full suite.
  - Through `scripts/flows.sh`, longest first:
    - `--test-chase --quit-after=750`; `--test-crew --quit-after=570`; `--test-crew=forgiven --quit-after=570`;
    - `--test-boarders` and its six other variants;
    - `--test-cavern` and its seven other variants;
    - `--test-runaway=tail`, `=ram`;
    - `--test-split`, `=early`, `=hold`, `=stall`, `=lone`;
    - `--test-switch-back` and its four other variants;
    - `--test-crevasse`, `=trestle`, `=survivor`;
    - `--test-relief`, `=rails`, `=refuse`, `=ahead`;
    - `--test-barricade`, `--test-ride`, `--test-ride-edges`, `--test-cart-watchdog`, `--test-track-view`.
  - Then the `--track-depth` check, with the lock absent: `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | grep -E "\[track-depth\] (PartyTrack |EnemyTrack |total)"`. Expected: PartyTrack `nofloorDrop=0`, EnemyTrack `nofloorDrop=40`, and the total reading `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`.
  - Then plain `scripts/net_test.sh`, alone.
  - Every one must PASS with `errors=0` (or `NET TEST: ok`).
  - A window that moved is re-pinned only from its measurement, named in the commit with its old and new values.
- [ ] **Step 3: Commit**

```bash
git add docs/development.md
git commit -m "docs: the long train from the landing, the platform cages and the crew running to their seats, climbing car to car, the grip and the throw, the three new hound groups; the re-pinned timings at 5.22 m/s and the RIDE pass's new steps

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add each re-pinned `src/Core/DebugTools.*.cs` to the `git add` by its path.)
