# Mine Cart Chase, Plan 11: barricades, and a live switch back with a deadly branch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12 gets timber barricades across the line. There is one on ride 1, a defended one on ride 2, one on ride 2's
slag line, and one on ride 5 before the buffer. They can be hacked, shot or spelled down; smashing through one costs 25
plating and 15% HP. Ride 3's `switch_back` becomes a live lever that only a human player can throw, offered from the seat
and called twice by a companion. Staying on the slag line past it ends at a broken bridge over a new crevasse, and the train
falls. Plan 10's three residual minors are fixed first.

**Architecture:**
- **Barricades.** A `Barricade` is a set piece (layer 1|4 + `ICombatTarget`, like `OreHeap`). The engine-free
  `BarricadeRules` hold its numbers. The encounter controller judges it as each cart's front reaches its face on its own lane.
- **The broken bridge.** It is carved by the generator, rng-free, behind a new knob: `FloorSpec.BrokenBridgeRide`, acting
  only with `Crevasses`. On that ride's slag line it lays a deck over a strip of chasm, with one enemy-lane cell left as a
  4 m gap (`RailLine.Break`). Every role builds the same dressing from the layout. The server calls plan 10's
  `RailController.Fall` when the party's cart reaches the gap still on the slag line.
- **The live return.** A `switch_back` whose ride has that gap 12–40 m past its points is live (`TrackSwitch.Deadly`).
  - The cart now crosses out with no return set.
  - At the back's points it takes the lever as it stands then: `Cross(out, points)`.
  - Clients learn this from the `CartStateDto.CrossBack` already on the wire, so no DTO field is added and the protocol
    stays 17.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md`. This plan is build-order item 3. It draws on:
- "Barricades";
- the ride table, rides 1, 2, 3 and 5;
- "Who works the deadly controls";
- the anti-goal "A death the player could not have seen coming";
- "Calls left to the builder" (the barricade numbers).

Code maps are in `.superpowers/sdd/floor12-v2-recon/gameplay.md` and `generation.md`; their line numbers have drifted. The
rulings already made are in `.superpowers/sdd/mine-cart-rulings.md`; plan 10's section is the last one.

**Builds on:** plan 10 as built and closed at `455bbf1`. It provides:
- `Cell.Chasm`, `BridgeKind.Deck`, `RailLine.Strips`/`Gorge`, `FloorGenerator.CarveChasms`/`StripCells`;
- `CrevasseRules` (`IsDrop`, `SetDownOk`, `OverCrevasse`, `LowGroundBesideDrops`) and `CrevasseWatch.Take`;
- `RailController.Fall(string why)`, the arrival snapshot and the wipe rise at the last stop;
- `--test-crevasse` and its variants, and the RIDE pass's walk-off tail.

Line numbers here are hints and drift, so find code by name. `docs/development.md` (Rails, Crevasses, The RIDE net test) says
how it all behaves today.

**Measured before planning** (shipped seed, `--builtin --no-ai --floor=12 --print-map`):
- Rides `[267,206,281,252,209]` m, 1214 m in all, `cavern=238..259`.
- Ride 3 runs from S 488.8 to 769.5. `r3_switch_out`'s points are at S 545.0 and `r3_switch_back`'s at S 643.2 (Fixed).
- An ASCII trace of ride 3 found outward-eligible straight runs (party indices, roughly) at 138–146, 148–155, 163–167,
  173–177 and 185–191. As fractions of the ride these are ≈0.12–0.23, 0.26–0.36, 0.47–0.52, 0.60–0.66 and 0.77–0.85.
- Ride 3's ordinary strip is the inward one beside 185–192.
- Cells are 4 m (`DungeonBuilder.CellSize`).

These are estimates from the map. Task 5 measures for real.

**The later plans** (not this one):
- 12: the dark and the runaway. Its split-track switch reuses this plan's `SwitchBackRules` (deadly lever, two calls,
  human only).
- 13: the cavern's collapsing bridge and spiral.
- 14: the leap.
- 15: the loop-the-loop.

**Planner's rulings** (mine; each decides something the spec left open, with what it costs if wrong):
1. Ruling: barricades are two set-piece kinds, `barricade` (the party lane) and `slag_barricade` (the enemy lane), not a lane
   field on `RailSetPiecePlan` — the record and the validator keep their shape and `RailSetPieces` is `[AiIgnore]` — cost
   if wrong: a third lane would need a third kind.
2. Ruling: a barricade takes `BarricadeRules.Toughness` = 3. Any hit that is not a spell (blade, arrow, shove, thrown
   weapon) knocks off 1, a heavy blow included. Any `AttackKind.Spell` hit clears it at once. "A heavy spell" is read as
   "any spell": weapons are never `Spell`, and only party abilities are — cost if wrong: a mage clears every barricade with
   one frostbolt.
3. Ruling: a smash costs the following.
   - The plating loses 25 (`DentPlating`, which wrecks a cart at 0 like any dent).
   - Each seated hero who is up takes 15% of max HP × `TierRules.PartyDamageScale` / 3: 15% at the default lethality of 3,
     5% at 1. A smash never takes a hero below 1 HP.
   - NPC bench riders and crew in wagons are only jolted (plan 5's cinder ruling: a quest NPC dies at 0 HP).
   - The cart keeps 80% of its speed.

   Cost if wrong: NPC riders never feel a barricade.
4. Ruling: the cart's front judges a barricade on that barricade's own lane, and any cart on that lane smashes one still
   standing: the party's cart, a ride's slag cart, a traffic cart and a runaway wagon.
   - A slag cart's riders each take 15% of their own max HP. Lethality scales damage to the party only. Enemies may die of it.
   - Chasers, the relief cart and spares are not judged, since they follow a train that already took the line.

   Cost if wrong: a co-op chaser passes through a barricade the train went round, which floor 12 never does.
5. Ruling: once cut or smashed, a barricade stays down until its ride is undone. It stands again at its ride's next arming
   after any of these:
   - a wreck;
   - a `Restore` (a wipe rise or a load);
   - being smashed while its ride was not armed (by traffic or a runaway).

   This is the heap's rule. Cost if wrong: a player who hacked one and then wrecked must hack it again.
6. Ruling: companions and hero bots never attack a barricade (plan 4: "breaking shoes is the player's job"). The warning
   comes in three forms:
   - one call (`BarkTrigger.Barricade`, "Timber across the line — cut it!") when the cart's front is 35 m (about 6 s at
     cruise) from the face, on its lane;
   - a lantern that blinks by `SetPieceRules.HeapAlarm`;
   - the heap's bell at the alarm's start.

   Cost if wrong: a melee solo player who does not brake takes the smash.
7. Ruling: ride 5's "barricade before the buffer" (in the spec's ride table) is built here with the other three — cost if
   wrong: one more piece than the brief listed.
8. Ruling: "defended" is a matter of placement. `r2_barricade` stands where `r2_slag2` (a priest thrower and a hound) runs
   alongside, and there is no new guard kind. `r2_slag_barricade` on the slag line ahead of `r2_slag1` shows the slag carts
   smashing timber — cost if wrong: a player who kills `r2_slag2` first hacks unopposed.
9. Ruling: the broken bridge sits behind a new knob, `FloorSpec.BrokenBridgeRide`:
   - an int, default 0, clamped 0..6;
   - `[AiIgnore]` in `SpecOverrides`;
   - acting only with `Crevasses`;
   - floor 12 sets it to 3.

   The generator never reads plans, so it cannot infer the ride — cost if wrong: one more override line.
10. Ruling: where the break goes.
    - It goes on the longest outward strip-eligible straight run (`StripCells` with way +1) of at least `BreakMinCells` = 5
      cells, inside fractions 0.55–0.90 of that ride (platform middle to platform middle). Ties go to the later run, and a
      longer run is clipped to its middle `StripMaxCells` (8).
    - It is refused on the cavern's ride or the last ride (`break=refused`); with no fitting run the report says `break=none`.
    - That ride gets no other strip: the break is its strip, and it also appears in `RailLine.Strips`.

    Cost if wrong: on a seed with no fitting run the `switch_back` stays Fixed (Ruling 13), and ride 3 loses its inward
    strip.
11. Ruling: the break's geometry.
    - Each run cell's outward t = 2, 3 cells become chasm at the lane's level.
    - The run's enemy-lane cells become `Deck` bridges, except its middle one (party index `From + Count / 2`), which becomes
      a `Cell.Chasm`: the 4 m gap.
    - The gap stays in `RailLine.Enemy`. The party lane stays solid floor.

    Cost if wrong: code that assumes every enemy-lane cell is walkable must skip the gap (Task 4 sweeps the tests for it).
12. Ruling: who falls at the gap.
    - The party's cart falls (`Fall("The slag line ends at the broken bridge!")`) when its front reaches the gap's near
      edge with `LaneBlendAt(gapS) ≥ 0.5`.
    - A slag or traffic cart whose front reaches the gap on the enemy track derails into it, and its riders go to
      `CrevasseWatch.Take` and die.

    Cost: none.
13. Ruling: a `switch_back` is live (`Deadly`, not `Fixed`) only when a break's gap lies 12–40 m past its points on its own
    ride (`SwitchBackRules.LeadMin`/`LeadMax`). Otherwise it stays Fixed as today. Every role decides this the same way at
    build — cost if wrong: a seed whose geometry defeats the break keeps today's safe ride 3.
14. Ruling: the late return.
    - With a live back, `switch_out` crosses with the return unset (NaN).
    - At the back's points the cart takes the lever as it stands then: `Cross(CrossOutS, points)` if it is thrown, else it
      stays across.
    - `CartStateDto.CrossBack` already carries the value, so the wire is unchanged and the protocol stays 17.

    Cost: none.
15. Ruling: a deadly lever takes no hits. Its collision layer is 1|8, not 4, so stray friendly fire never decides it. It
    moves only on E from a human (`ControlKind.LocalHuman` or `Remote`); a `Companion` actor's `Interact` does nothing —
    cost if wrong: a player cannot shoot the switch back from afar.
16. Ruling: the seated window offers a live back only while the cart is across with no return set
    (`Crossing && float.IsNaN(CrossBackS)`), on every peer. The server's acceptance (`InSeatedWindow`) uses the same gate.
    Plan 8's grace and guards are unchanged — cost: none.
17. Ruling: the switch-back calls.
    - The early call is "The slag line's out past the bridge — switch back!" (`BarkTrigger.SwitchBack`). It is said once,
      when the front is 30 m before the lever, or at once on crossing if the lever is nearer.
    - The last call is "Switch back — now!" (`BarkTrigger.SwitchBackNow`). It is said when the front is 6 m before the
      points and the lever is not thrown. If both are due, the last call is said.
    - Only deadly levers get these calls. `switch_out` keeps its one "Line's blocked" call, since a wrong `switch_out` costs
      the heap, not a life.

    Cost if wrong: one call fewer on `switch_out`.
18. Ruling: the warning by sight.
    - A live back gets a steady red signal lamp on its own post, on the slag line's outer side, 3 m past its points.
    - The gap gets broken plank ends, rail ends bent down, and a red lamp at its near edge.
    - The enemy track's rails and sleepers are cut over the gap (`RailTrack.Cut`).

    Cost: none.
19. Ruling: on floor 12 (Task 5), `r3_switch_back`'s points sit 16–24 m before the gap (aim 20). `r3_buckets` moves after
    the gap when there is room, else at least 8 m clear of the back's crossover and of the gap. The flows check that the
    broken end can be seen from the lever (a physics ray); if it cannot, the gate stops and reports — cost if wrong: a
    seed-specific re-placement.
20. Ruling: plan 10's residual 1 is fixed by a per-member "returned" guard on the server (`_returnedPending`). It drops
    poses over a drop cell until one arrives that is off a drop. It gives up after `PoseGuardRules.ReturnHoldSeconds` = 3 s,
    so a lost ForcePose cannot freeze a body — cost if wrong: a client lagging more than 3 s may fall again.
21. Ruling: residual 2 pins the diagonal lip at `CrevasseRules.CornerLipSlack` = 1 m, the value observed, instead of
    `FallDepth` (2.5 m) — cost if wrong: a future ramp layout that touches a lip diagonally by more than 1 m fails the sweep
    loudly.
22. Ruling: residual 3. The server prints `[net-server] ride: walk-off: armed` as the last wreck stands. `FELL_AT` is the
    first `[crevasse] Joiner fell` line after that marker, and a missing marker fails the script — cost: none.
23. Ruling: every flow that throws `switch_out` on ride 3 now also throws the live back from the seat: `--test-crew`,
    `--test-chase` (by `PartyController.VrUse`), and the RIDE pass (by the intent). The chase's "a hit throws it" check stays
    on `switch_out` only — cost: none.
24. Ruling: flows that are not about barricades keep their own checks. Where a barricade's dent or HP hit breaks one, the
    flow cuts that ride's barricades at the arming through `RailEncounterController.DebugCutBarricades(int ride)` and logs
    `[<flow>] barricades on ride N cut by the test` — cost if wrong: that flow no longer meets the barricade (the barricade
    flow and the RIDE pass do).

## Global Constraints

- Defaults reproduce today byte for byte. With `BrokenBridgeRide` 0, or `Crevasses` false, every floor's `ContentHash` is
  unchanged. Pinned by `RailLineTests.DefaultKnobs_BuildTodaysFloors` (must stay green in every task) and by
  `--rehash-plans` changing only `floor_12.json`'s `LayoutHash`.
- `CarveChasms` draws from no rng. The rail pass still draws only from `new Random(seed ^ 0x2A11)`.
- Room indices and order never change. Floor 12 must still generate:
  - `objective=jailbreak rails=6`;
  - five rides, each ≥ 160 m, and a total ≥ 1200 m;
  - a cavern span;
  - from Task 5 on, `break=` on ride 3.
- `Protocol.Version` stays **17** (the branch is unreleased). Break cells come from the shared seed. New state that clients
  must see goes in existing messages (`PropEvt`, `RailPropsDto`, `CartStateDto`).
- Lethality never changes a fall: no crevasse code reads `TierRules.Lethality`. Barricade damage to the party reads
  `TierRules.PartyDamageScale` (Ruling 3).
- CC0 assets only (the repository is public). This plan adds no assets: meshes are procedural, reusing the existing
  `weathered_planks` texture and the cue names `crate_break`, `timber_smash`, `cart_bell`, `switch_clunk`.
- No voice spend: new lines are subtitle-only (`"VoiceId": null`). Never run a Runware/voice bake.
- Hand-edited JSON keeps the game's escaping: `'` → `'`, `&` → `&`, `<` → `<`, `>` → `>`,
  `+` → `+`, non-ASCII → `\uXXXX` (upper-case hex), two-space indent. `git diff` must show only the values you meant
  to change.
- `tests/` (xUnit) is not in git. Write and run tests locally, never `git add tests/`. Commit only `src/`, `data/`,
  `docs/`, `scripts/`.
- Run `dotnet build DungeonCrawler.csproj` before any `godot --headless --path . -- <flags>` run. Engine flags such as
  `--headless --path .` go before `--`, and game flags go after it.
- Every headless check greps its own flow's `[...] RESULT` line, never the early `[builtin] RESULT ok`.
- `--test-crew` needs `--quit-after=570` and `--test-chase` needs `--quit-after=750`. The new `--test-barricade` sets
  `_quitAfter = 240`, and `--test-switch-back` (and `=wrong`) sets 300.
- `RIDE=1 scripts/net_test.sh` runs alone, with nothing else on the machine. It is flaky at baseline (about 1 run in 3–4):
  a failure in a step this plan did not touch is rerun once before it is investigated.
- Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N` (or `--quit-when-empty`).
- Full test suite: `dotnet test tests/DungeonCrawler.Tests.csproj`.
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No installers or dist builds (`export_all.sh`, `package.sh`, `publish_*.sh`), no push, no merge. The controller commits
  this plan; implementers commit only their task's files, staged by explicit path.

## Review Focus

- **A companion's stray arrow or blade, or a hero bot, deciding the live switch back.** The deadly lever must never move
  for anyone but a human: hits are ignored, and a Companion's E does nothing.
  - Pinned by `SwitchBackRulesTests.ADeadlyLever_MovesOnlyForAHuman` (Task 7).
  - Pinned by the switch-back flow's check that the lever stays straight, with `LastToggledBy` null, until the human's
    press (Task 7).
- **A lever thrown, then set back, or pre-thrown by a walker before the ride.** At the points the cart must follow the lever
  as it stands then, never as it stood at `switch_out`.
  - Pinned by `SwitchBackRulesTests.AtItsPoints_TheCartTakesTheLeverAsItStands` and `AtSwitchOut_ALiveBackLeavesTheReturnOpen`
    (Task 7).
- **A smash with a hero at low HP, or an NPC on the bench.** It must leave the hero at 1 HP or more, and leave the NPC
  unhurt.
  - Pinned by `BarricadeRulesTests.Smash_NeverDowns` (Task 2).
  - Pinned by the barricade flow's 5% HP companion and Grisel-on-the-bench checks (Task 2).
- **A cart that never switched out, or a co-op rider pressing E while the cart is on the party lane.** The back lever must
  not be offered and must do nothing.
  - Pinned by `SwitchBackRulesTests.ALiveBack_IsOfferedOnlyAcross` (Task 7).
  - Pinned by the RIDE pass's heap ride (it rides ride 3 straight), which must not fall (Task 7).
- **A slag or traffic cart reaching the broken bridge, and a body set down near it.** The cart must go into the drop with
  its riders dead. No hero, crew member or companion is ever set down on the deck or the gap.
  - Pinned by `ChasmTests.TheBreak_IsADeckWithAGap` and the existing `ExitAndFormationCells_AreNeverDrops` on Break rows
    (Task 4).
  - Pinned by the switch-back flow's traffic step and its "nobody fell but the train" check (Task 6).

---

### Task 1: Plan 10's residual minors

**Files:**
- Modify: `src/Net/Core/PoseGuardRules.cs`, `src/Net/ServerWorld.cs` (`ApplyPose`, `OnPlayerLeft`, the new guard)
- Modify: `src/Party/Adventurer.cs` (`ReturnFromCrevasse`)
- Modify: `src/Dungeon/CrevasseRules.cs` (`CornerLipSlack`), `src/Core/DebugTools.Crevasse.cs` (its lip check)
- Modify: `src/Net/NetTestFlow.cs` (the walk-off marker), `scripts/net_test.sh` (`FELL_AT`)
- Test: `tests/Net/PoseGuardRulesTests.cs`, `tests/Dungeon/ChasmTests.cs`, `tests/Dungeon/CrevasseRulesTests.cs`

**Interfaces:**
- Produces:
  - Two constants: `public const double PoseGuardRules.ReturnHoldSeconds = 3.0` and
    `public const float CrevasseRules.CornerLipSlack = 1f`.
  - `public static bool PoseGuardRules.DropAfterReturn(bool overDrop, double secondsSinceReturn)`. It is
    `overDrop && secondsSinceReturn < ReturnHoldSeconds`.
  - `public void ServerWorld.NoteReturned(int member)`. It records the time in `_returnedPending : Dictionary<int, double>`.
    `public int ServerWorld.ReturnedPosesDropped { get; private set; }` is for tests.
  - `ApplyPose`, after the shove guard and before the `Fallen` skip: a member in `_returnedPending` has a pose over a drop
    (`CrevasseRules.IsDrop` of the pose's cell) dropped while `DropAfterReturn` holds. The first pose off a drop, or the
    give-up, removes the entry. `OnPlayerLeft` removes it too.
  - `Adventurer.ReturnFromCrevasse`: on a server, a Remote body also calls `NetSession.Instance.Server?.NoteReturned(Index)`,
    beside its `Shove(at)`.
  - The sweep and the flow pin corners at `CornerLipSlack`: `LowGroundBesideDrops(l, diagonals: true, below: CrevasseRules.CornerLipSlack)`.
  - The RIDE server prints `[net-server] ride: walk-off: armed (the last wreck stands)` where `_lastWreckDone` becomes true.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Net/PoseGuardRulesTests.cs
[Fact] public void AfterAReturn_APoseOverADrop_IsDropped_UntilTheGuardGivesUp()
{
    Assert.True(PoseGuardRules.DropAfterReturn(overDrop: true, secondsSinceReturn: 0.2));
    Assert.False(PoseGuardRules.DropAfterReturn(overDrop: false, secondsSinceReturn: 0.2));
    Assert.False(PoseGuardRules.DropAfterReturn(overDrop: true, secondsSinceReturn: PoseGuardRules.ReturnHoldSeconds + 0.01));
    Assert.Equal(3.0, PoseGuardRules.ReturnHoldSeconds);
}
// tests/Dungeon/CrevasseRulesTests.cs
[Fact] public void CornerLipSlack_IsTheObservedMetre() => Assert.Equal(1f, CrevasseRules.CornerLipSlack);
// tests/Dungeon/ChasmTests.cs NoGround_BesideAChasm_IsBelowItsLip: the corner line becomes
//   var corner = CrevasseRules.LowGroundBesideDrops(l, diagonals: true, below: CrevasseRules.CornerLipSlack).ToList();
```

- [ ] **Step 2: Run them and see them fail.** Run
  `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~PoseGuardRulesTests|FullyQualifiedName~CrevasseRulesTests|FullyQualifiedName~ChasmTests.NoGround"`.
  Expected: compile errors (`DropAfterReturn`, `ReturnHoldSeconds`, `CornerLipSlack`).

- [ ] **Step 3: Implement** the Interfaces above. In `net_test.sh`, replace the `FELL_AT` line with these:
  `MARK=$(grep -n "\[net-server\] ride: walk-off: armed" "$TMP/server.log" | head -1 | cut -d: -f1)`
  and
  `FELL_AT=$(awk -v m="${MARK:-999999999}" 'NR>m && /\[crevasse\] Joiner fell at .*: Dead/ {print NR; exit}' "$TMP/server.log")`.
  Then add `NET TEST: FAIL (walk-off: no armed marker on the server)` when `MARK` is empty, before the existing `FELL_AT`
  check.

- [ ] **Step 4: Run them.** Run the filter. Expected: PASS, and the 120-row corner sweep is green at 1 m. If any row fails,
  report its cells and do not raise the slack. Then:
  - `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-crevasse 2>&1 | grep -E "\[crevasse\] (floor 12 lips|RESULT)"`.
    Expected: `floor 12 lips: 0` and `RESULT ok`.
  - Alone: `RIDE=1 scripts/net_test.sh`. Expected: the `walk-off: armed` line, `NET TEST: ok`.

- [ ] **Step 5: Commit**

```bash
git add src/Net/Core/PoseGuardRules.cs src/Net/ServerWorld.cs src/Party/Adventurer.cs src/Dungeon/CrevasseRules.cs src/Core/DebugTools.Crevasse.cs src/Net/NetTestFlow.cs scripts/net_test.sh
git commit -m "fix(crevasse): a returned remote hero's late poses over the drop are dropped; corners pinned at the observed 1 m; the walk-off's fall is the one after its marker (plan 10 residuals)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Barricades on the party line (solo and on the wire)

**Files:**
- Create: `src/Dungeon/Rails/Core/BarricadeRules.cs`, `src/Dungeon/Rails/SetPieces/Barricade.cs`,
  `src/Dungeon/Rails/SetPieces/TimberDebris.cs` (extracted from `BufferStop`), `src/Core/DebugTools.Barricade.cs`
- Modify: `src/Dungeon/Rails/SetPieces/Buffer.cs` (use `TimberDebris`)
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`Piece.Barricade`, build, tick, `NetNode`,
  `CaptureProps`/`ApplyProps`)
- Create: `src/Dungeon/Rails/RailEncounterController.Barricades.cs` (build, tick, smash, arm/undo, `DebugCutBarricades`)
- Modify: `src/Dungeon/Rails/RailController.cs` (`Smash`)
- Modify: `src/Adventure/Core/Validate/PlanValidator.cs` (`SetPieceKinds` + `"barricade"`), `src/Adventure/Core/Plans/FloorPlan.cs` (doc)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`BarkTrigger.Barricade` appended), `src/Adventure/Core/Voice/RideBarks.cs`
- Modify: `src/Net/Core/NetRefs.cs`, `src/Net/Core/PropCheck.cs` (`RailPropsDto.DownBarricades`), `src/Net/ClientWorld.cs`
  (the PropEvt cases), `src/Dungeon/Rails/Core/RideSound.cs` (`PropCue`)
- Modify: `src/Core/DebugTools.cs` (the flag)
- Modify: `data/adventures/builtin/floor_12.json` (`r1_barricade`, `r2_barricade`, `r5_barricade`)
- Test: `tests/Dungeon/Rails/BarricadeRulesTests.cs` (new), `tests/Dungeon/Rails/RideSoundTests.cs`,
  `tests/Adventure/PlanValidatorTests.cs` (or wherever rail set pieces are pinned), `tests/Adventure/RideBarksTests.cs`,
  `tests/Net/PropCheckTests.cs`, `tests/Adventure/RailEncounterPlanTests.cs`

**Interfaces:**
- Produces:
  - `public static class BarricadeRules` (engine-free):
    - constants `Toughness = 3`, `SmashDent = 25f`, `SmashHpFraction = 0.15f`, `SpeedKept = 0.8f`, `CallAhead = 35f`,
      `HalfDepth = 0.25f`;
    - `int HitWeight(AttackKind kind)` (Spell → `Toughness`, else 1);
    - `float SmashFraction(float partyDamageScale) => SmashHpFraction * partyDamageScale / TierRules.DefaultLethality`;
    - `float SmashDamage(float maxHp, float hp, float partyDamageScale)` = min(maxHp × fraction, max(0, hp − 1));
    - `bool Smashes(bool standing, bool onItsLane, float prevFront, float front, float faceS)` (prevFront < face ≤ front);
    - `bool Calls(bool standing, bool onItsLane, bool called, float front, float faceS)`
      (`!called && front ≥ faceS − CallAhead && front < faceS`).
  - `public partial class Barricade : StaticBody3D, ICombatTarget, ISoundPoint`. It is two posts and three crossed planks,
    1.8 m wide by 1.2 m tall, with a lantern on a stake on the cart's side (+Z), as on `OreHeap`. Layers 1|4, mask 0.
    - init `S` (the party S of its middle), `SlagLine` (bool), `NetRef`;
    - `FaceS => S − BarricadeRules.HalfDepth` (in its own lane's S: party, or enemy for the slag line);
    - `Left`, `IsStanding => Left > 0`, `Alarm`;
    - `event Action<Barricade, string>? Down` ("cut" or "smashed"; server only);
    - `ReceiveAttack` (server: `Left -= HitWeight(attack.Kind)`; at 0 a deferred `Cut(attackerName)`; client: `Miss`);
    - `Smash(Node3D root, Vector3 from, bool debris)` (`TimberDebris.Burst`, collision off);
    - `Restand()`;
    - client shows: `ShowCut()`, `ShowSmashed(Node3D root, bool debris)`, `ShowStanding()`.
    - It tells "cut", "smashed" and "standing" (`RaisePropChanged`).
  - `public static class TimberDebris { public static void Burst(Node3D root, Transform3D at, float width, Material wood) }`.
    This is `BufferStop`'s splinters and tumbling timbers moved, unchanged.
  - `internal void RailController.Smash(float dent, float speedKept)`. It does `DentPlating(dent)`, scales V by `speedKept`,
    shakes every seated hero (0.1, 0.5) and calls `Props.Tremor.ShakeOnly(0.04f, 0.4f)`. Server only.
  - The controller:
    - builds `"barricade"` on the party track at `RideS(ride, At)`, facing the cart;
    - each tick of the armed ride, for the party cart (`onItsLane = !_rails.OnEnemyLane(face)`), sets the lantern by
      `SetPieceRules.HeapAlarm` and rings `cart_bell` at the alarm's start;
    - makes the call by `Calls` through `RailCalls.Say(party, cart, BarkTrigger.Barricade, "Timber across the line — cut it!")`;
    - on `Smashes` calls `_rails.Smash(SmashDent, SpeedKept)` and gives each seated `Adventurer { Downed: false }`
      `ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, SmashDamage(..)/maxHp, 0f), "The barricade", -1)`;
    - logs "The cart smashes through the barricade!" and prints
      `[rails] barricade {id} smashed at S={S:0.0}: plating {a:0} -> {b:0}, {n} hero(es) hit`.
    - A cut logs "{who} cuts the barricade down." and prints `[rails] barricade {id} cut by {who} at S={front:0.0}`. The
      build prints `[rails] barricade {id} on the {party|slag} line at S={S:0.0} (ride {r})`.
  - Arming and undoing follow Ruling 5 (a `Rebuild` flag per piece, as the heap's). Plus
    `internal void DebugCutBarricades(int ride)` and `internal void DebugRestandBarricades(int ride)`.
  - The wire:
    - `NetRefs.BarricadeKind = "barricade"`, `NetRefs.Barricade(int piece)`;
    - `RailPropsDto.DownBarricades : List<int>` (in the prop hash);
    - `NetNode` → `p.Barricade`;
    - `ClientWorld`'s PropEvt cases `(Barricade, "cut"|"smashed"|"standing")`;
    - `RideSound.PropCue("barricade","cut") = "crate_break"`, `("barricade","smashed") = "timber_smash"`.
  - `BarkTrigger.Barricade` is appended after `Crevasse` and stored by name. `RideBarks.Triggers` gets it, with a
    subtitle-only bank: three lines for each roster name (Sable's a woman's), two per class, three generic, in the bank's
    style. The generic set includes "Timber across the line — cut it!".
  - `floor_12.json` gains `r1_barricade` (ride 1, `At` 0.25), `r2_barricade` (ride 2, 0.6) and `r5_barricade` (ride 5,
    0.9), appended after their rides' pieces.
  - `--test-barricade` (`_builtin`, floor 12, lethality 3, no ambush/portals/gathering, `_quitAfter = 240`), printing
    `[barricade]` lines:
    - **A, a spell.** At stop 0, on foot, `r1_barricade` takes one `AttackKind.Spell` hit and is cut.
      `DebugRestandBarricades(1)` stands it up again.
    - **B, ride 1's cut.**
      - The party boards (`RideBoard`) and departs.
      - The `Barricade` call is said with the front 30–35 m from the face.
      - From 12 m out the human brakes until V < 0.3.
      - The seated lead hits with its weapon profile (`WeaponProfile.ToAttack()`, melee). The barricade still stands after
        hits 1 and 2, and is cut after hit 3.
      - The brake is let go. The plating is unchanged across the barricade, and no smash line prints.
    - **C, ride 2's smash.**
      - Before stop 1's departure, one companion is set to 5% of max HP, and Grisel (forgiven, as `--test-crew=forgiven`
        sets her) sits on the bench. If that path is not cheap, use any NPC rider the crew flow seats.
      - Nobody brakes or hits, and the cart smashes `r2_barricade`.
      - The plating drops by exactly 25.
      - Each other seated hero loses 15% (±0.5) of max HP. The 5% companion is up with HP ≥ 1. The NPC's HP is unchanged.
      - V after the smash is 0.8 × before (±0.2).
    - **D, standing again.** After arriving at stop 2, `rc.Restore(1)` stands `r2_barricade` again.
    - **Sight.** For each barricade, a ray (layer 1) from the seated lead's eye to the lantern, at 15 m, is clear.
      The result at 35 m is printed.
    - Then `[barricade] RESULT ok`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Dungeon/Rails/BarricadeRulesTests.cs
[Fact] public void ThreeBladeOrArrowHits_OneSpell()
{
    Assert.Equal(3, BarricadeRules.Toughness);
    Assert.Equal(1, BarricadeRules.HitWeight(AttackKind.Melee));
    Assert.Equal(1, BarricadeRules.HitWeight(AttackKind.Ranged));
    Assert.Equal(3, BarricadeRules.HitWeight(AttackKind.Spell));
}

[Theory, InlineData(3f, 0.15f), InlineData(2f, 0.10f), InlineData(1f, 0.05f)]
public void SmashFraction_Is15PercentAtTheDefaultLethality(float scale, float fraction)
    => Assert.Equal(fraction, BarricadeRules.SmashFraction(scale), 4);

[Fact] public void Smash_NeverDowns()
{
    Assert.Equal(15f, BarricadeRules.SmashDamage(maxHp: 100f, hp: 80f, partyDamageScale: 3f), 3);
    Assert.Equal(9f, BarricadeRules.SmashDamage(100f, 10f, 3f), 3);
    Assert.Equal(0f, BarricadeRules.SmashDamage(100f, 1f, 3f), 3);
}

[Fact] public void Smashes_OnlyAsTheFrontReachesTheFace_OnItsLane()
{
    Assert.True(BarricadeRules.Smashes(standing: true, onItsLane: true, prevFront: 49.9f, front: 50.1f, faceS: 50f));
    Assert.False(BarricadeRules.Smashes(false, true, 49.9f, 50.1f, 50f));
    Assert.False(BarricadeRules.Smashes(true, false, 49.9f, 50.1f, 50f));
    Assert.False(BarricadeRules.Smashes(true, true, 50.1f, 50.5f, 50f));
}

[Fact] public void TheCall_ComesOnce_35MetresOut()
{
    Assert.True(BarricadeRules.Calls(standing: true, onItsLane: true, called: false, front: 15.1f, faceS: 50f));
    Assert.False(BarricadeRules.Calls(true, true, false, 14.9f, 50f));
    Assert.False(BarricadeRules.Calls(true, true, true, 30f, 50f));
    Assert.False(BarricadeRules.Calls(true, false, false, 30f, 50f));
}

[Fact] public void TheSmash_Dents25_AndKeeps80PercentOfTheSpeed()
{ Assert.Equal(25f, BarricadeRules.SmashDent); Assert.Equal(0.8f, BarricadeRules.SpeedKept); }

// RideSoundTests
[Fact] public void ABarricade_CracksWhenCut_AndSmashesWhenSmashed()
{ Assert.Equal("crate_break", RideSound.PropCue("barricade", "cut")); Assert.Equal("timber_smash", RideSound.PropCue("barricade", "smashed")); Assert.Null(RideSound.PropCue("barricade", "standing")); }
// PlanValidatorTests: a "barricade" set piece survives ValidateRailPlans with no repair line
// RideBarksTests: EveryClass_HasABarricadeLine (TopUp fills BarkTrigger.Barricade for every class, VoiceId null)
// PropCheckTests: the rail part's hash differs when only DownBarricades differs
// RailEncounterPlanTests.BuiltinFloor12_HasTheFiveRides: r1_barricade, r2_barricade, r5_barricade present; r5's At between r5_spur's and r5_buffer's
```

- [ ] **Step 2: Run them and see them fail.** Run
  `--filter "FullyQualifiedName~BarricadeRulesTests|FullyQualifiedName~RideSoundTests|FullyQualifiedName~PlanValidator|FullyQualifiedName~RideBarks|FullyQualifiedName~PropCheck|FullyQualifiedName~RailEncounterPlanTests"`.
  Expected: compile errors.

- [ ] **Step 3: Implement** the Interfaces above. The client builds the barricade as dressing, as every set piece; only the
  server judges it. Check that a seated hero's blade reaches a barricade across the track from the front seat. The cut in
  flow B proves it; if `CutAlong` cannot reach the barricade, report rather than widen the collider beyond 1.9 m × 1.3 m.

- [ ] **Step 4: Run them.** Run the filter, then the following in turn:
  - `dotnet build DungeonCrawler.csproj && timeout 260 godot --headless --path . -- --test-barricade 2>&1 | grep -E "\[barricade\]|\[rails\] barricade|ERROR|Unhandled"`.
    Expected: steps A–D, three `on the party line` build lines (r1, r2 and r5), sight `clear` at 15 m for each,
    `[barricade] RESULT ok`, and no `ERROR`.
  - Then, each greping its own `RESULT`: `--test-ride`, `--test-crew --quit-after=570`, `--test-chase --quit-after=750`,
    `--test-relief`, `--test-cart-watchdog`, `--test-ride-edges` and `--test-crevasse`. Expected: `RESULT ok` from each. A
    flow broken only by a barricade's dent or HP gets Ruling 24's hook; report every such use.
  - Then, alone: `RIDE=1 scripts/net_test.sh`. Expected: `NET TEST: ok`, with the props hash still matching. Ride 1 and
    ride 2 now smash their barricades.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/BarricadeRules.cs src/Dungeon/Rails/SetPieces/Barricade.cs src/Dungeon/Rails/SetPieces/TimberDebris.cs src/Dungeon/Rails/SetPieces/Buffer.cs src/Dungeon/Rails/RailEncounterController*.cs src/Dungeon/Rails/RailController.cs src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Net/Core/NetRefs.cs src/Net/Core/PropCheck.cs src/Net/ClientWorld.cs src/Dungeon/Rails/Core/RideSound.cs src/Core/DebugTools.Barricade.cs src/Core/DebugTools.cs data/adventures/builtin/floor_12.json
git commit -m "feat(rails): timber barricades across the party line: three hits or one spell cut them, a smash costs 25 plating and 15% HP (never a down); called at 35 m; told to clients

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Stage any flow file that took Ruling 24's hook by its own path.)

### Task 3: Barricades on the slag line, and every cart that smashes them

**Files:**
- Modify: `src/Dungeon/Rails/RailEncounterController.Barricades.cs`, and `RailEncounterController.cs` and
  `RailEncounterController.Traffic.cs` (the per-cart smash)
- Modify: `src/Dungeon/Rails/Core/BarricadeRules.cs`, `src/Adventure/Core/Validate/PlanValidator.cs` (+ `"slag_barricade"`)
- Modify: `src/Core/DebugTools.Barricade.cs` (step E), `src/Core/DebugTools.Chase.cs` (the ledger)
- Modify: `data/adventures/builtin/floor_12.json` (`r2_slag_barricade`)
- Test: `tests/Dungeon/Rails/BarricadeRulesTests.cs`, the validator test, `RailEncounterPlanTests`

**Interfaces:**
- Consumes: Task 2's `Barricade`, `BarricadeRules`, `RailController.Smash`.
- Produces:
  - `public static bool BarricadeRules.OnItsLane(bool slagLine, bool runOnEnemyLane) => slagLine == runOnEnemyLane`.
  - `public static bool BarricadeRules.StandsAgainAtArming(bool downWhileItsRideArmed, bool rideUndone) => rideUndone || !downWhileItsRideArmed`.
  - `"slag_barricade"`: built on the enemy track at `Lanes.EnemyS(RideS(ride, At))`, facing up the enemy line. Its
    `FaceS` is in enemy-lane S.
  - The party's cart judges it with `onItsLane = _rails.FrontOnEnemyLane`, comparing `Lanes.EnemyS(front)` with its face.
  - Each slag cart (`_carts`), the traffic cart and a runaway wagon, after stepping, judges every standing barricade on its
    own track (slag and traffic: the enemy track; a runaway: the party track) with `Smashes`. On a smash:
    - the barricade is smashed;
    - each living slag rider takes `ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = d, MaxDamage = d }, new HitContext { DefenderStunned = true }, rng, "The barricade")`,
      with d = 0.15 × its max HP;
    - the run's V is scaled by `SpeedKept`;
    - the server prints `[rails] {cart name} smashes the barricade {id} at S={s:0.0}: {n} rider(s) hit`.
  - Ruling 5's re-stand for a barricade smashed while its ride is not armed.
  - The `--test-chase` ledger: `"barricade"` joins `ChaseHazards`. The warning is the `BarkTrigger.Barricade` call, the hit
    is the log "The cart smashes through the barricade!", one warning for one hit, under the ledger's existing lead rule.
  - `floor_12.json` gains `r2_slag_barricade` (ride 2, `At` 0.3), so `r2_slag1` (0.15, −6 m) reaches it.
  - Flow step **E**: on ride 2, `r2_slag1` smashes `r2_slag_barricade`, and its two cinders are each down 15% (±1) of max HP
    or dead. The party cart, on its own lane, does not smash it: the plating is unchanged at that S.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ABarricade_IsJudgedOnlyOnItsOwnLane()
{
    Assert.True(BarricadeRules.OnItsLane(slagLine: true, runOnEnemyLane: true));
    Assert.True(BarricadeRules.OnItsLane(false, false));
    Assert.False(BarricadeRules.OnItsLane(true, false));
    Assert.False(BarricadeRules.OnItsLane(false, true));
}

[Fact] public void ASmashOffItsRide_StandsAgainAtTheArming()
{
    Assert.True(BarricadeRules.StandsAgainAtArming(downWhileItsRideArmed: false, rideUndone: false));  // traffic smashed it
    Assert.False(BarricadeRules.StandsAgainAtArming(true, false));                                     // the party took it down on its ride
    Assert.True(BarricadeRules.StandsAgainAtArming(true, true));                                       // a wreck or a load
}
// validator: "slag_barricade" kept; RailEncounterPlanTests: r2_slag_barricade on ride 2 with At > r2_slag1's
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~BarricadeRulesTests|FullyQualifiedName~PlanValidator|FullyQualifiedName~RailEncounterPlanTests"`.
  Expected: compile errors.

- [ ] **Step 3: Implement** the Interfaces above. A runaway smashing a barricade on its party line logs
  "The runaway smashes through the barricade!".

- [ ] **Step 4: Run them.** Run the filter, then `--test-barricade` (steps A–E, `RESULT ok`), then
  `--test-chase --quit-after=750` (a `barricade` warn/hit pair for each of `r1`, `r2` and `r5` that the chase does not cut,
  and `RESULT ok`). Then `--test-ride` and `--test-crew --quit-after=570`: `RESULT ok`.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/RailEncounterController*.cs src/Dungeon/Rails/Core/BarricadeRules.cs src/Adventure/Core/Validate/PlanValidator.cs src/Core/DebugTools.Barricade.cs src/Core/DebugTools.Chase.cs data/adventures/builtin/floor_12.json
git commit -m "feat(rails): barricades on the slag line; slag, traffic and runaway carts smash those on their own line; the chase ledger pairs every barricade smash with its call

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: The broken bridge in the generator

**Files:**
- Modify: `src/Dungeon/Generation/FloorSpec.cs` (`BrokenBridgeRide`), `src/Adventure/Core/Plans/CampaignPlan.cs` (`SpecOverrides`)
- Modify: `src/Dungeon/Generation/RailLine.cs` (`RailRules.BreakFrom/BreakTo/BreakMinCells`, `RailBreak`, `RailLine.Break`,
  `RailLine.BreakWhy`)
- Modify: `src/Dungeon/Generation/FloorGenerator.Chasms.cs` (`CarveBreak`, called from `CarveChasms`; `CarveStrips` skips
  the break's ride)
- Modify: `src/Dungeon/Generation/LayoutReport.cs` (the `Rail` line)
- Test: `tests/Dungeon/ChasmTests.cs`, `tests/Dungeon/RailLineTests.cs`, `tests/Dungeon/CrevasseRulesTests.cs`

**Interfaces:**
- Consumes: plan 10's `CarveChasms`, `StripCells` (way +1), `LineSign`, `RailSide`, `Times`, `TakenCells`.
- Produces:
  - `FloorSpec.BrokenBridgeRide : int` (default 0). `SpecOverrides.BrokenBridgeRide : int?` is `[AiIgnore]`. It joins
    `IsEmpty`, and `Apply` clamps it to 0..6. Its `AiDescription` is "The ride (1-based) whose slag line ends at a broken
    bridge over a crevasse, 0 for none; needs Crevasses."
  - Rules: `RailRules.BreakFrom = 0.55f`, `BreakTo = 0.90f`, `BreakMinCells = 5`.
  - `public sealed record RailBreak(int Ride, RailSpan Run, int Gap)`. `Gap` is the party index whose enemy twin is the
    gap cell.
  - `RailLine.Break : RailBreak?` and `RailLine.BreakWhy : string?` are init-only and not hashed. `BreakWhy` is null when
    the knob is 0; otherwise "ok", "none" or "refused".
  - `CarveChasms` order: the cavern, then `CarveBreak` (when `spec.BrokenBridgeRide > 0`), then the strips (skipping that
    ride when a break was carved), then the walkways, then one `RaiseWalls`.
  - `CarveBreak` follows Rulings 10 and 11. The carved run is also added to `Strips`, and the pockets it cuts leave
    `Rail.Pockets`.
  - `LayoutReport.Rail` appends ` break={Gap}@{f:0.00} run={From}..{To}` (f is the gap's fraction of its ride, measured as
    the gorge's is), or ` break=none`, or ` break=refused`. Nothing is appended when `BreakWhy` is null.

- [ ] **Step 1: Write the failing tests.** `ChasmTests` gains `BreakSeeds()`: the first 60 sweep seeds × Jailbreak and None,
  on lines with at least 5 rides, generated with `Crevasses = true, BrokenBridgeRide = 3`, cached as `Gen` is.

```csharp
[Fact] public void BrokenBridgeRide_DefaultsOff() => Assert.Equal(0, FloorSpec.Default.BrokenBridgeRide);
[Fact] public void SpecOverrides_CarryAndClampBrokenBridgeRide()
{
    Assert.Equal(3, new SpecOverrides { BrokenBridgeRide = 3 }.Apply(FloorSpec.Default).BrokenBridgeRide);
    Assert.Equal(6, new SpecOverrides { BrokenBridgeRide = 9 }.Apply(FloorSpec.Default).BrokenBridgeRide);
    Assert.False(new SpecOverrides { BrokenBridgeRide = 0 }.IsEmpty);
}

[Theory, MemberData(nameof(BreakSeeds))]
public void TheBreak_IsADeckWithAGap(int seed, FloorObjectiveKind obj)
{
    var l = GenBreak(seed, obj);
    if (l.Rail is not { Break: { } b } rail) return;
    var side = FloorGenerator.RailSide(rail.Party[b.Run.From + 1] - rail.Party[b.Run.From], FloorGenerator.LineSign(rail));
    Assert.InRange(b.Run.To - b.Run.From + 1, RailRules.BreakMinCells, RailRules.StripMaxCells);
    Assert.Equal(b.Run.From + (b.Run.To - b.Run.From + 1) / 2, b.Gap);
    for (var i = b.Run.From; i <= b.Run.To; i++)
    {
        var twin = rail.Party[i] + side;
        Assert.True(rail.Enemy.Contains(twin));
        if (i == b.Gap) { Assert.True(l.IsChasm(twin)); Assert.Equal(l.LevelOf(rail.Party[i]), l.LevelOf(twin)); }
        else Assert.Equal(BridgeKind.Deck, l.Bridges[twin]);
        Assert.True(l.IsChasm(rail.Party[i] + side * 2)); Assert.True(l.IsChasm(rail.Party[i] + side * 3));
        Assert.False(CrevasseRules.IsDrop(l, rail.Party[i]));        // the party lane stays solid
    }
    Assert.Contains(rail.Strips, s => s == b.Run);
    Assert.Single(rail.Strips, s => RideOf(rail, s.From) == b.Ride);   // the break is its ride's only strip
}
// RideOf(rail, i): a test helper, the 1-based ride whose two platforms bracket party index i


[Fact] public void MostBreakLines_HaveABreak()          // >= 60 % of BreakSeeds rows have Break != null, every one on ride 3, its gap within BreakFrom..BreakTo
[Fact] public void TheBreak_IsRefusedOnTheCavernsAndTheLastRide()   // BrokenBridgeRide = the cavern's ride, then the last: Break null, BreakWhy "refused"
[Fact] public void WithoutTheKnob_TheFloorIsPlanTensFloor()          // BrokenBridgeRide 0: ContentHash equal to Crevasses-only for 20 seeds, Break null, BreakWhy null
[Fact] public void TheBreak_IsDeterministic()                         // the same seed twice: equal ContentHash and equal Break
[Fact] public void LayoutReport_NamesTheBreak()                      // a "break=" token with "@" and "run=", or break=none / break=refused
```

  Add `BreakSeeds` rows to the existing theories `Chasms_CutNothingOff`, `NoChasm_OnASpawnChestKeyObjectiveOrPlatformCell`,
  `NoGround_BesideAChasm_IsBelowItsLip`, `Strips_LieBesideTheLine_AndAreWalledBehind`, `NoDropOrBridge_TouchesTheUnderLayer`,
  and to `CrevasseRulesTests.ExitAndFormationCells_AreNeverDrops` if that is a theory over floors. Any `RailLineTests` or
  `ChasmTests` invariant that reads every enemy-lane cell as walkable exempts `Rail.Break`'s gap twin; name each in the report.

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~ChasmTests|FullyQualifiedName~RailLineTests"`.
  Expected: compile errors (`BrokenBridgeRide`, `RailBreak`, `Break`).

- [ ] **Step 3: Implement** the Interfaces above. The run search reuses `StripCells(layout, rail, enemy, pockets, taken, sign, i, +1)`
  over the ride's window, clipped by `StripPlatformGap` as the strips are. Do not touch the rng or any pass before
  `CarveChasms`.

- [ ] **Step 4: Run them.** Run the filter, then `FloorSolvabilityTests`, `FloorGeneratorTests` and `LayoutKindTests`.
  Expected: PASS, with `DefaultKnobs_BuildTodaysFloors` green. If `MostBreakLines_HaveABreak` misses 60%, report the rate
  and the commonest reason; widen nothing silently.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorSpec.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Dungeon/Generation/RailLine.cs src/Dungeon/Generation/FloorGenerator.Chasms.cs src/Dungeon/Generation/LayoutReport.cs
git commit -m "feat(chasm): BrokenBridgeRide (off by default): a ride's slag line crosses a strip of chasm on a deck with a 4 m gap

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: Floor 12 gets its broken bridge (the gate)

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (floor 12 `SpecOverrides`)
- Modify: `data/adventures/builtin/floor_12.json` (`LayoutHash`; `r3_switch_back` and `r3_buckets` `At`)
- Modify: `src/Core/DebugTools.cs` (one `--print-map` line: Step 3)
- Test: `tests/Adventure/RailEncounterPlanTests.cs`

**Interfaces:**
- Consumes: Task 4; the `[map] rail` line.
- Produces: floor 12 on the shipped seed with `break=` on ride 3, the back's points 16–24 m before the gap, and its new
  `LayoutHash`. Tasks 6–8 run their checks on it.

- [ ] **Step 1: Turn it on.** In floor 12's `SpecOverrides`, add `"BrokenBridgeRide": 3` after `"Crevasses": true`.

- [ ] **Step 2: Measure.** Run
  `dotnet build DungeonCrawler.csproj && godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --quit-after=6 2>&1 | grep -E "^\[map\] rail|\[DungeonBuilder\] floor 12|\[rails\] switch|RailFailure"`.
  Expected, and pinned in the commit message:
  - `objective=jailbreak rails=6`;
  - five rides, each ≥ 160 m, total ≥ 1200 m;
  - `cavern=` a span (not `none`, not `breached`);
  - `gorge=` still has spans at the trestle's middle and the jump's gap;
  - `break=` a gap on ride 3, with a run of ≥ 5 cells (the estimate is 185..191, gap 188, f ≈ 0.80).

  If `break=none` or `refused`, or any of the above fails, stop and report the lines. Do not pick another seed and do not
  retune. Save the ASCII map to the scratchpad.

- [ ] **Step 3: Place ride 3.** First, beside the existing `[map] rail StopS=` print in `src/Core/DebugTools.cs`, add
  `[map] rail break: ride {r} gap at party S={S:0.0}` when `Rail.Break` is set. Compute it as
  `cart.Party.Track.ClosestS(DungeonBuilder.CellToWorld(rail.Party[b.Gap])) − DungeonBuilder.CellSize / 2`, the near edge
  on the party lane beside the gap. This is good to about 1 m; Task 7's build line prints the exact lead. Then:
  - Set `r3_switch_back`'s `At` so its points lie 20 m (16–24) before the gap's near edge.
  - Keep `r3_switch_out` (0.2) < `r3_heap` (0.35) < `r3_switch_back`.
  - Move `r3_buckets` to the first fraction ≥ gap + 8 m that is ≥ 15 m short of stop 3. If there is no room, keep it
    ≥ 8 m clear of [back, back + `SwitchStretch` + `CartRules.HalfLength`] and of the gap.

  Use three decimals at most. If the back cannot fit 16–24 m, stop and report.

- [ ] **Step 4: Rehash, and prove the other floors unchanged.** Run
  `godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`, then `git diff --stat data/`.
  Expected: only `campaign.json` and `floor_12.json` change, and `floor_12.json` only in `LayoutHash` and the two `At`
  values.

- [ ] **Step 5: Sweep floor 12, then every floor.** Use plan 10's Task 4 Step 5 commands verbatim:
  - the floor 12 grep for `repaired|dropped|demoted|adapted|forced|cannot be|trimmed|clamped|kept only` (only the known
    Grisel line may appear);
  - the 20-floor loop;
  - `grep -c adapted` (expected `0`);
  - `--test-vault-puzzle` (no `PROBLEM`, `RESULT ok`).

- [ ] **Step 6: Pin the data, and rerun what the layout touches.** In `RailEncounterPlanTests`, assert ride 3's order:
  `switch_out.At < heap.At && heap.At < back.At`. Also assert that `back.At` is not more than 0.12 before the buckets, or
  that the buckets lie after it. Run
  `--filter "FullyQualifiedName~RailEncounterPlanTests|FullyQualifiedName~CrewPlanTests|FullyQualifiedName~GulletCampaignTests|FullyQualifiedName~StoryRewireTests"`.
  Then run `--test-ride`, `--test-crew --quit-after=570`, `--test-chase --quit-after=750` and `--test-crevasse`. Each
  should print `RESULT ok`: the back is still Fixed here, so the crossing comes home.

- [ ] **Step 7: Commit**

```bash
git add data/adventures/builtin/campaign.json data/adventures/builtin/floor_12.json src/Core/DebugTools.cs
git commit -m "feat(floor12): the slag line on ride 3 ends at a broken bridge; the switch back stands just before it

(measured: rides …, cavern …, break … run …, back points … m before the gap)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: The broken bridge on the line, and the fall at its gap

**Files:**
- Create: `src/Dungeon/Rails/Core/SwitchBackRules.cs`, `src/Dungeon/Rails/SetPieces/BrokenBridge.cs`,
  `src/Dungeon/Rails/RailEncounterController.Break.cs`, `src/Core/DebugTools.SwitchBack.cs`
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (build the break before the pieces),
  `RailEncounterController.cs` and `RailEncounterController.Traffic.cs` (an enemy cart at the gap),
  `src/Dungeon/Rails/RailController.Lanes.cs` (`CrossOutS`, `CrossBackS`, `DebugStayAcross`)
- Modify: `src/Core/DebugTools.cs` (the `--test-switch-back` flag)
- Test: `tests/Dungeon/Rails/SwitchBackRulesTests.cs` (new)

**Interfaces:**
- Consumes: `RailLine.Break` (Task 4); `RailController.Fall`, `CrevasseWatch.Take` (plan 10); `RailTrack.Cut`.
- Produces:
  - `public static class SwitchBackRules` (engine-free):
    - constants `LeadMin = 12f`, `LeadMax = 40f`, `SignalPast = 3f`;
    - `bool LiveBack(float backPointsS, float gapS)`: gap − points within [LeadMin, LeadMax]; NaN gives false;
    - `bool Falls(float prevFront, float front, float gapS, float blendAtGap)`: prevFront < gap ≤ front and blend ≥ 0.5.
  - `public float RailController.CrossOutS => _crossOut; public float CrossBackS => _crossBack;` (every role).
    `internal void DebugStayAcross()` calls `Cross(_crossOut, float.NaN)` while `Crossing` (server only).
  - `RailEncounterController` (every role builds; the server judges):
    - `public float BreakGapS { get; }` (party S of the gap's near edge, NaN without a break);
    - `internal float BreakGapEnemyS`;
    - `public int BreakRide { get; }` (−1 without a break);
    - the enemy track is cut over [gap near edge, far edge] (`_enemy.Track.Cut`);
    - the build prints `[rails] broken bridge on ride {r}: gap at party S={S:0.0} (enemy S={E:0.0}), deck {n} cells`.
  - `BrokenBridge : Node3D`. It holds splintered plank ends angled down at both gap edges, two rail stubs bent 30° down at
    the near edge, and a red emissive lamp with an `OmniLight3D` (range 6 m, energy 2, red, no shadow) on a stub post at the
    near edge, steady. It uses `weathered_planks`.
  - Server tick: when the party cart rolls and `Falls(Party.PrevFrontS, Party.FrontS, BreakGapS, _rails.LaneBlendAt(BreakGapS))`
    holds, `_rails.Fall("The slag line ends at the broken bridge!")` is called and
    `[rails] the cart runs off the broken bridge at S={front:0.0}` is printed.
    - Use the front's previous S as the run keeps it; if there is none, add `CartRun.PrevFrontS`.
    - Each slag cart, and the traffic cart, whose run's front passes `BreakGapEnemyS` on the enemy track: its riders go to
      `CrevasseWatch.Instance.Take(rider, rider.GlobalPosition)`, then `Derail()`, then it is freed. This prints
      `[rails] {name} drops through the broken bridge at enemy S={s:0.0}: {n} rider(s) lost`.
    - `internal void DebugSpawnTrafficAt(float enemyS)` starts a traffic cart there (test hook).
  - `--test-switch-back=wrong` (the default flag is Task 7's). It uses `_builtin`, floor 12, lethality 3, no
    ambush/portals/gathering and `_quitAfter = 300`, and prints `[switchback]` lines:
    - **A, traffic.** With the cart held at stop 2 and nobody aboard (`rc.Restore(2)`),
      `DebugSpawnTrafficAt(BreakGapEnemyS − 30)` is called. Within 8 s the traffic cart drops through and is gone. No hero
      is `Fallen`.
    - **B, the wrong branch (for now by hook).**
      - `RideWaiveCages`, then everyone boards with `RideBoard`, and the cart departs.
      - The seated lead throws `switch_out` from the seat (`PartyController.VrUse` in its window).
      - When the switch is taken, `rc.DebugStayAcross()` is called.
      - The train falls with its front within 0.6 m of `BreakGapS`, and the log has "The slag line ends at the broken
        bridge!".
      - Within 1 s every rider is `Fallen`.
      - The wipe follows, and the rise is at stop 2: `[rails] wipe-rise: … at stop 2`.
      - The heroes stand within 10 m of the cart, with plating `MaxPlating`.
    - After every step: "nobody fell but the train's".
    - Then `[switchback] RESULT ok (wrong)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Dungeon/Rails/SwitchBackRulesTests.cs
[Theory]
[InlineData(100f, 112f, true)] [InlineData(100f, 140f, true)]
[InlineData(100f, 111.9f, false)] [InlineData(100f, 140.1f, false)] [InlineData(100f, float.NaN, false)]
public void ABack_IsLive_OnlyWithTheGap12To40MetresPastItsPoints(float points, float gap, bool live)
    => Assert.Equal(live, SwitchBackRules.LiveBack(points, gap));

[Fact] public void TheTrainFalls_OnlyOnTheSlagLine_AsItsFrontReachesTheGap()
{
    Assert.True(SwitchBackRules.Falls(prevFront: 199.9f, front: 200.1f, gapS: 200f, blendAtGap: 1f));
    Assert.True(SwitchBackRules.Falls(199.9f, 200.1f, 200f, 0.5f));
    Assert.False(SwitchBackRules.Falls(199.9f, 200.1f, 200f, 0.49f));
    Assert.False(SwitchBackRules.Falls(200.1f, 200.4f, 200f, 1f));
    Assert.False(SwitchBackRules.Falls(199.9f, 200.1f, float.NaN, 1f));
}
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter FullyQualifiedName~SwitchBackRulesTests`. Expected: a compile
  error.

- [ ] **Step 3: Implement** the Interfaces above. While the back is still Fixed, `TakeSwitch` is unchanged, so every
  existing flow comes home.

- [ ] **Step 4: Run them.** Run the filter, then
  `dotnet build DungeonCrawler.csproj && timeout 320 godot --headless --path . -- --test-switch-back=wrong 2>&1 | grep -E "\[switchback\]|\[rails\] (broken bridge|the cart runs off|the train falls|wipe-rise|.*drops through)|ERROR|Unhandled"`.
  Expected: the build line on ride 3, A's drop line, the fall and the rise at stop 2, `[switchback] RESULT ok (wrong)`, and
  no `ERROR`. Then `--test-ride` and `--test-chase --quit-after=750`: `RESULT ok`.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/SwitchBackRules.cs src/Dungeon/Rails/SetPieces/BrokenBridge.cs src/Dungeon/Rails/RailEncounterController*.cs src/Dungeon/Rails/RailController.Lanes.cs src/Core/DebugTools.SwitchBack.cs src/Core/DebugTools.cs
git commit -m "feat(rails): the broken bridge: its gap is cut, lit and dressed; a train still on the slag line there falls, an enemy cart drops through with its riders

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Stage `src/Dungeon/Rails/CartRun.cs` too if `PrevFrontS` was added.)

### Task 7: The live switch back: the player's, called twice, offered from the seat

**Files:**
- Modify: `src/Dungeon/Rails/Core/SwitchBackRules.cs`, `src/Dungeon/Rails/SetPieces/TrackSwitch.cs` (`Deadly`)
- Modify: `src/Dungeon/Rails/RailEncounterController.Switchyard.cs` (`BuildSwitch`, `TakeSwitch`, a new `TakeBack`,
  `SeatedLeverFor`, `InSeatedWindow`, the calls), `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (the
  `switch_back` tick case, `Piece.Live`)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`BarkTrigger.SwitchBack`, `SwitchBackNow`, appended after
  `Barricade`), `src/Adventure/Core/Voice/RideBarks.cs`
- Modify: `src/Net/Core/PropCheck.cs` (the doc of `ThrownSwitches`)
- Modify: `src/Core/DebugTools.SwitchBack.cs` (the default flag; `=wrong` without the hook), `src/Core/DebugTools.Crew.cs`,
  `src/Core/DebugTools.Chase.cs`, `src/Net/NetTestFlow.cs` (the first client's seated throw on ride 3)
- Test: `tests/Dungeon/Rails/SwitchBackRulesTests.cs`, `tests/Adventure/RideBarksTests.cs`

**Interfaces:**
- Consumes: Task 6's `LiveBack`, `BreakGapS`, `CrossOutS`/`CrossBackS`; plan 8's `SeatedSwitchRules`.
- Produces:
  - `SwitchBackRules`:
    - `CallEarly = 30f`, `CallLast = 6f`;
    - `float BackAtSwitchOut(bool live, float backPointsS)` (live → NaN, else the points);
    - `float BackAtPoints(bool thrown, float pointsS)` (thrown → points, else NaN);
    - `bool Offered(bool crossing, bool backSet) => crossing && !backSet`;
    - `bool MayToggle(bool deadly, ControlKind actor)` (`!deadly || actor is LocalHuman or Remote`);
    - `public enum BackCall { None, Early, Last }`;
    - `BackCall Call(bool crossing, bool backSet, bool thrown, float front, float leverS, float pointsS, bool earlyDone, bool lastDone)`
      by Ruling 17: Last when `crossing && !backSet && !thrown && !lastDone && front ≥ points − CallLast && front < points`;
      else Early under the same conditions with `!earlyDone && front ≥ lever − CallEarly`; else None.
  - `TrackSwitch.Deadly : bool` (init):
    - its layer is 1|8;
    - `ReceiveAttack` returns `Miss`;
    - `Interact` returns at once unless `SwitchBackRules.MayToggle(Deadly, actor.Control)`;
    - `Fixed` and `Deadly` are never both true.
  - `BuildSwitch` for a `switch_back`:
    - `Live = SwitchBackRules.LiveBack(piece.S, BreakGapS)` on the same ride (`BreakRide`);
    - live gives `Fixed = false, Deadly = true`, and a red signal-lamp post `SignalPast` past the points on the slag line's
      outer side (steady red emissive with an `OmniLight3D`: range 5 m, energy 1.6);
    - the build line says `(live: the broken bridge {gap − S:0.0} m past)` or `(fixed, thrown)`.
  - `TakeSwitch` (`switch_out`): `backS = SwitchBackRules.BackAtSwitchOut(back.Live, back.S)` for the ride's next back.
  - Tick case `"switch_back"`: when `rolling && !Fired && FrontS ≥ S`, `TakeBack(piece)` runs:
    - it sets Fired and Taken;
    - if live and `_rails.Crossing && float.IsNaN(_rails.CrossBackS)`: `back = BackAtPoints(Thrown, S)`. If it is not NaN,
      it calls `_rails.Cross(_rails.CrossOutS, back)` and logs "The cart takes the switch back onto the party line.";
    - it prints `[rails] switch {id} taken at S=…: back from {S:0.0}` or `… passed straight: the cart stays on the slag line`.
  - `SeatedLeverFor` and `InSeatedWindow` skip a live back unless `SwitchBackRules.Offered(_rails.Crossing, !float.IsNaN(_rails.CrossBackS))`.
  - `TickBackCalls(piece, party)` (server): it says `BarkTrigger.SwitchBack` / `SwitchBackNow` by `Call` through
    `RailCalls.Say`, with the logs "The slag line's out past the bridge — switch back!" / "Switch back — now!". It prints
    `[rails] switch {id} called {early|last} at front S={f:0.0} ({points − f:0.0} m to the points); bark={by}`. The flags
    are reset at the ride's arming.
  - `RideBarks`: banks for the two triggers, built as Task 2's bank. The generic sets include the two spec lines.
  - The flows:
    - **`--test-switch-back`** (default):
      - It runs A as in Task 6, then B up to the cross, with no hook.
      - The early call is heard (`RailCalls.Called`, `SwitchBack`) with the front 25–30 m before the lever.
      - Until the lead's press, the lever is straight and `LastToggledBy` is null (no companion touched it).
      - The seated window offers it ("Throw the switch") and the lead presses through `VrUse`.
      - The take line says `back from`.
      - `rc.LaneBlend` is ≤ 0.01 by points + 6 + 1.1 + 1 m, there is no fall, and the cart arrives at stop 3.
      - Sight: a layer-1 ray from the seated lead's eye at the lever's S to the bridge's lamp is `clear`.
      - Then `[switchback] RESULT ok (thrown)`.
    - **`=wrong`**:
      - Nobody presses, and the hook is gone.
      - The early and last calls are heard, the last with the front 5–6.5 m before the points.
      - The lever is never thrown, and the take line says `passed straight`.
      - From the fall on, it is as in Task 6.
    - **`--test-crew`**: after its seated `switch_out` throw, the seated lead throws the back from the seat in its window
      (`[crew] ride 3: the seated lead throws the switch_back lever from the seat …`).
    - **`--test-chase`**: after step 3's hits on `switch_out`, the seated lead throws the back through `VrUse` before
      stop 3. It also checks that a hit on the back (`ReceiveAttack`) is a `Miss` that leaves it straight.
    - **RIDE pass**: after its ride 3 cross (client phase 8), the first client waits for the seated window to offer the
      back, presses it through the intent, and prints
      `[net] ride: ride 3's switch_back offers "Throw the switch" in the seated window {ahead:0.0} m before the lever; thrown through the intent from the seat`.
      The server traces `prop:switch:{back}:thrown` and prints its take line with `back from`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AtSwitchOut_ALiveBackLeavesTheReturnOpen()
{
    Assert.True(float.IsNaN(SwitchBackRules.BackAtSwitchOut(live: true, backPointsS: 640f)));
    Assert.Equal(640f, SwitchBackRules.BackAtSwitchOut(false, 640f));
}

[Fact] public void AtItsPoints_TheCartTakesTheLeverAsItStands()
{
    Assert.Equal(640f, SwitchBackRules.BackAtPoints(thrown: true, pointsS: 640f));
    Assert.True(float.IsNaN(SwitchBackRules.BackAtPoints(false, 640f)));
}

[Fact] public void ALiveBack_IsOfferedOnlyAcross()
{
    Assert.True(SwitchBackRules.Offered(crossing: true, backSet: false));
    Assert.False(SwitchBackRules.Offered(false, false));
    Assert.False(SwitchBackRules.Offered(true, true));
}

[Theory]
[InlineData(true, ControlKind.LocalHuman, true)] [InlineData(true, ControlKind.Remote, true)]
[InlineData(true, ControlKind.Companion, false)] [InlineData(true, ControlKind.Parked, false)]
[InlineData(false, ControlKind.Companion, true)]
public void ADeadlyLever_MovesOnlyForAHuman(bool deadly, ControlKind actor, bool may)
    => Assert.Equal(may, SwitchBackRules.MayToggle(deadly, actor));

[Fact] public void TheCalls_Early30BeforeTheLever_Last6BeforeThePoints()
{
    BackCall C(float front, bool thrown = false, bool early = false, bool last = false, bool crossing = true, bool backSet = false)
        => SwitchBackRules.Call(crossing, backSet, thrown, front, leverS: 635f, pointsS: 640f, earlyDone: early, lastDone: last);
    Assert.Equal(BackCall.None, C(604.9f));
    Assert.Equal(BackCall.Early, C(605f));
    Assert.Equal(BackCall.None, C(620f, early: true));
    Assert.Equal(BackCall.Last, C(634.1f, early: true));
    Assert.Equal(BackCall.Last, C(634.5f));                 // both due: the last one
    Assert.Equal(BackCall.None, C(636f, early: true, last: true));
    Assert.Equal(BackCall.None, C(636f, thrown: true));
    Assert.Equal(BackCall.None, C(620f, crossing: false));
    Assert.Equal(BackCall.None, C(620f, backSet: true));
    Assert.Equal(BackCall.None, C(640f));                   // at the points: too late to call
}
// RideBarksTests: EveryClass_HasSwitchBackLines (SwitchBack and SwitchBackNow, VoiceId null)
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~SwitchBackRulesTests|FullyQualifiedName~RideBarks|FullyQualifiedName~SeatedSwitchRules"`.
  Expected: compile errors.

- [ ] **Step 3: Implement** the Interfaces above, then the flows.

- [ ] **Step 4: Run them.** Run the filter. Then each of these, alone, greping its own `RESULT`:
  - `--test-switch-back`: `RESULT ok (thrown)`, sight `clear`;
  - `--test-switch-back=wrong`: `RESULT ok (wrong)`, both calls;
  - `--test-crew --quit-after=570`;
  - `--test-chase --quit-after=750`;
  - `--test-ride`.

  Expected: `RESULT ok` from every one, and `[rails] switch r3_switch_back points … (live: the broken bridge … m past)`.
  Then, alone: `RIDE=1 scripts/net_test.sh`. Expected: `NET TEST: ok`, the client's switch_back throw line, and the server's
  `back from`. The heap ride (item 11) still halts at the heap and never falls.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/SwitchBackRules.cs src/Dungeon/Rails/SetPieces/TrackSwitch.cs src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Net/Core/PropCheck.cs src/Core/DebugTools.SwitchBack.cs src/Core/DebugTools.Crew.cs src/Core/DebugTools.Chase.cs src/Net/NetTestFlow.cs
git commit -m "feat(rails): ride 3's switch back is live: only a player throws it, from the seat while the cart is across; called early and at the last moment; left straight, the train runs off the broken bridge

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 8: Co-op proof, docs and the full sweep

**Files:**
- Modify: `src/Net/NetTestFlow.cs` (ride 1's barricade, cut over the wire; ride 2's smash shown; the late `CrossBack` on
  both clients), `scripts/net_test.sh` (the RIDE block's greps)
- Modify: `docs/development.md` (read its current text first), `src/Core/DebugTools.cs` (the header list of flags)

**Interfaces:**
- Consumes: Tasks 2, 6 and 7.
- Produces, in the RIDE pass:
  - **Ride 1, cut over the wire.**
    - The first client hears the barricade call: the server's `[rails] … barricade` call line, or its BarkMsg.
    - From 12 m out it sends the brake intent until its cart state shows V < 0.5.
    - It aims at `r1_barricade` (`Adventurer.AimAt`, as `FightBoarders` does) and swings every 0.5 s until the PropEvt
      "cut" arrives, then lets the brake go. It prints
      `[net] ride: barricade r1_barricade cut here by our blade from the seat ({n} swings, V={v:0.0})`.
    - The server prints its `[rails] barricade r1_barricade cut by Rider` line.
    - The prop hash still matches.
  - **Ride 2, the smash shown.** The client prints `[net] ride: barricade r2_barricade smashed here (PropEvt), plating {a:0} -> {b:0}`,
    from the cart states just before and after the PropEvt.
  - **The late return on both screens.** Each client prints
    `[net] ride: crossed back here: CrossBack {cb:0.0} told at front S={f:0.0} (the back's points {p:0.0}), blend {b:0.00} at S={s:0.0}`.
    This prints the first time its cart state's `CrossBack` turns from NaN to a number on ride 3, with the blend read
    `SwitchStretch` + 2 m later. The joiner prints it from its own screen.
  - **`net_test.sh`**, RIDE block, after its existing checks, fails with a reason unless all of these hold:
    - both ride 1 lines are present, and no `smashes through the barricade` precedes ride 1's cut;
    - ride 2's plating drop `a − b` is 24.5–25.5;
    - the client's switch_back throw line and the server's `back from` are present;
    - both clients' `crossed back here` lines show `|cb − p| ≤ 0.5` and blend ≤ 0.01;
    - no `[rails] the cart runs off the broken bridge` appears anywhere in the server log.

- [ ] **Step 1: Write the steps and the greps** as above. There is no xUnit test: this task is the co-op proof.

- [ ] **Step 2: Run the RIDE pass alone.** Run `dotnet build DungeonCrawler.csproj && RIDE=1 scripts/net_test.sh`.
  Expected: `[net-server] RESULT ok`, `[net] RESULT ok`, the new lines, and `NET TEST: ok`. A failure in a step this plan did
  not touch is rerun once (the baseline flake). Then run plain `scripts/net_test.sh`: `NET TEST: ok`.

- [ ] **Step 3: Write the docs.** In `docs/development.md`:
  - **Rails.** The `BrokenBridgeRide` knob in the knob list. The ride table, with the plan 11 slots now built and the
    measured `At` values. A "Barricades" subsection covering Rulings 1–8, `BarricadeRules`, the props on the wire and the
    test hooks.
  - **Switches and the lane blend.** The live switch back: Rulings 13–18, the late `Cross`, and `TrackSwitch.Deadly`.
    Replace "always thrown, untouchable" where it is no longer true.
  - **Crevasses.** The broken bridge in the generator (Rulings 10–11, the `break=` report token) and the fall at the gap
    (Ruling 12).
  - **The debug flags.** `--test-barricade` (steps A–E) and `--test-switch-back` (`=wrong`), and what each proves.
  - **The RIDE net test.** Items 2, 4 and 5 gain the barricade cut and smash and the seated switch-back throw. Item 13
    gains the walk-off's armed marker (Task 1).
  - **Known gaps.** Anything the reviewers deferred.

- [ ] **Step 4: Run the whole suite and the sweep.** Run `dotnet test tests/DungeonCrawler.Tests.csproj`, then Task 5
  Step 5's sweep. Expected: all tests pass, no repair line, `0` adapted, and `--test-vault-puzzle` `RESULT ok`. Then
  `git diff --stat` shows no file outside this plan's lists.

- [ ] **Step 5: Commit**

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh docs/development.md src/Core/DebugTools.cs
git commit -m "test(rails): the RIDE pass cuts ride 1's barricade from a remote seat, sees ride 2's smash and the late switch back on both screens; docs: barricades, the live switch back and the broken bridge

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
