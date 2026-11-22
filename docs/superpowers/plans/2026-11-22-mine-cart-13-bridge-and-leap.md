# Mine Cart Chase, Plan 13: the cavern's collapsing bridge and the leap to the enemy cart (ride 4 closes) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Amended 2026-11-22 against d175fb7 per .superpowers/sdd/shared/plan13-drift-check.md (B1–B3, S1–S19, C1–C8).

**Goal:** Ride 4 on floor 12 closes in the crevasse cavern:
- the line enters it on a timber bridge that collapses behind the train, and goes under a train that brakes on it;
- a slag cart runs alongside on the slag line's deck, and a human player can leap across onto it and back;
- after the leap stretch the slag line's deck gives way, and anyone still aboard falls with the slag cart.

**Architecture:**
- **Gone decks.** These are runtime state, not layout. `FloorLayout.GoneBridges` is a set of bridge cells that have given way. It is not hashed and not saved, and `CrevasseRules.Footing` reads it, so companions never step onto a gone cell and a walker on one falls and is judged like any other fall.
  - `ChasmDressing` builds the cavern lanes' deck cells as one look per cell, so each can go. `SetGone` hides the cell's look and disables its footing.
  - The live pieces cut the track bare over their span (`RailTrack.CutMore`) and lay their own rails, one mesh per cell. They lay no curve and add no `RailTrack`: each cell's rails are `RailTrack.LaySpan` of the lane's own track, so they lie on `RailBed.SurfaceAt` as the lane does and `--track-depth` covers them as `PartyTrack` and `EnemyTrack`. If a task finds it needs a curve of its own, it is a `RailTrack` laid by `RailCurves.CentrelineOver` over `RailBed.SurfaceAt`, so the probe lists and measures it; stop and report first.
- **The bridge** is a set piece (`bridge`, `CavernBridge`) built from the layout's cavern span (`RailLine.Cavern`), not from its At.
  - It starts to go a grace after the train's last body is wholly on it.
  - Its front then runs at 0.8 × the train's unpumped cruise. It catches the train only while a player brakes; otherwise it holds 2 m short of the train's rear.
- **The leap** is a seat change: `Adventurer.SitIn` onto a slag cart's deck seat. Plan 2's seat model is reused, with a real seat book on `SlagCart`.
  - It is offered like plan 8's seated window, whatever the aim, as "Leap across" or "Leap back".
  - The server takes the press with extra slack, and `SeatMsg` carries it with the slag cart's `CartId`.
- **The slag line's drop** (piece `leap`, dressing `SlagDrop`) is the slag deck's two cells two short of the cavern's end.
  - With the party's front 25 m short of it, the cells give way, the bell rings and a companion calls it.
  - A slag cart that reaches the drop goes over. Only an able human still aboard falls with it. A downed or AI-run hero is hauled back into the party's cart.
- **The wire:** protocol stays 17.
  - Seats go in `SeatMsg`.
  - The bridge and the drop go in `PropEvt` (`bridge:{piece}`, `leap:{piece}`) and in two new `RailPropsDto` fields.
  - The slag cart's plunge is read from its gone state, as the broken bridge's is.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-full-circle-design.md`. Read the owner's notes at its top first:
- 2026-11-21: the loop-the-loop is dropped.
- 2026-11-22: the spiral is removed. Plan 13 is the collapsing bridge only, on the floor's two levels, with no free 3D track.

This plan merges build-order items 5 (the bridge only) and 6 (the leap). It draws on:
- ride 4 in "The five rides" (the cavern bullets);
- "The leap to the enemy cart has no miss";
- "Who works the deadly controls" (speed checks are shared);
- "Crevasses and death";
- the anti-goals.

Earlier rulings are in `.superpowers/sdd/mine-cart-rulings.md` (plans 1–12; plan 12's block starts at "## Plan 12") and `.superpowers/sdd/shared/plan12/rulings.md` (every plan 12 ruling; `progress.md` beside it is the ledger). Read the corrected split ruling first: the party's spur fall is test-hook-only on every floor. `docs/development.md` describes how everything behaves today: Rails; The five rides (ride 4); Crevasses; Riding together (Seats and poses, Leaps and boarders, Props); The watchdog; The RIDE net test. Line numbers drift, so find code by name.

**Builds on** plans 9–12 and the exposed-track fix, as on the branch at `d175fb7`:
- `RailLine.Cavern` (a `RailSpan` of party-lane indices; with `Crevasses`, both lanes over it are `BridgeKind.Deck`);
- `ChasmDressing` (one `Footings` body with a `Deck_{x}_{y}` box per bridge cell; the shared `Bridges` mesh);
- `CrevasseRules` (`Footing`, `KeepFooting`, `NearestStandable`, `NearestSetDown`, `IsDrop`);
- `CrevasseWatch.Take` and `RailController.Fall`, which is a no-op while the cart is wrecked;
- plan 10's `TickTrestle` (the trestle's timed fall over a crevasse);
- `RailTrack.Cut`;
- the quiet plunge (`SwitchBackRules.PlungedAt`, `PlungeTold`, `TrafficStartS`, `DropsThrough`);
- `SlagCart`, `CartRun`, `PaceRules`, `SpawnSlagCart`, `Free`;
- `ICartBody`/`ICartRider`/`CartSeats`, `Adventurer.SitIn` (which leaves an old cart first), `RailNet.OnSeat`/`FlushSeats`/`SendRailCatchUp`;
- plan 12's runaway (`RunawayRules.TrainRearS`, `FreeRideRunaway`) and its split;
- `RailCalls.Say`, `RideBarks`, `TimberDebris`;
- both lanes are laid by `RailCurves.CentrelineOver` over `RailBed.SurfaceAt` (`DungeonBuilder.cs`, `Lay`): the track follows the drawn floor, ramps at 26.6° with 1 m eases; a rail lane cell is never buckled (`BuckleRules.Pick`);
- `--track-depth` (`DebugTools.TrackDepth.cs`, `TrackDepthRules`) probes every `RailTrack` under the floor root against the drawn floor; its pass is `buried=0 inside=0 nofloorOff=0 budget=0`;
- `RailEncounterController.DebugPieces()` (the probe's `piece` lines), `SetPieceRules.PickSpurBend` (the kiln spur), `--test-track-view`, and the held luminance gate in `--test-split=after --screenshot`;
- `scripts/flows.sh` and the RIDE lock (see Global Constraints);
- the RIDE fixes 0f46dc0 and 021cabb: `CartFollower` re-snaps after a state too old to catch up; the server arms the shove guard from the FloorChange spawn (`ServerWorld._spawnSent`); the RIDE pass has a wipe-rise watch.

In `docs/development.md` also read "The track bed" (in Rails) and "Running flows side by side".

**Measured** by `--track-depth` at d175fb7 (`.superpowers/sdd/2026-11-22-exposed-track-fix/t5-depth.txt`; re-run at HEAD, identical):
- The cavern is `238..259`, 22 cells, straight south down x=61 from (61,18) to (61,39). The enemy lane is x=62, and the chasm runs outward from it.
- `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`. The cavern's party S is 903.1..991.1 (88.0 m, 4.0 m a cell, ≈16.9 s at 5.2 m/s), and stop 4 stands 31.4 m past its end.
- Ride 4 today: the dark is S 793.6–878.6; the runaway breaks loose at S 813.5; the split's points are at S 863.6, with its crevasse at S 865.6–881.6; `r4_slag` (At 0.7, Offset −3) starts at S 946.8, inside the cavern; `r4_hounds` (At 0.262, Offset −4) still paces the train through the cavern.
- Ride 4 has no ramp on either lane (the line's ramps are on rides 3 and 5).
- This plan changes no generator and no builder code, so Task 2 must reproduce these to 0.1 m. A difference is a stop, not a re-measure.

**Planner's rulings** (mine; each decides something the spec left open, with what it costs if wrong):
1. Ruling: both pieces go in the cavern on ride 4, as the spec's text has it. It is the line's only long straight of twin decks over a drop.
   - The leap stretch runs from the cavern's start + `LeapRules.LeapIn` (10 m) to the drop.
   - The drop is the slag deck's 2 cells (`DropCells`), 2 short of the cavern's end (`DropFromEnd`).
   - On floor 12 that is 62.0 m (≈12 s): from S 913.1 to the drop's near edge at S 975.1.
   - Task 2 stops and reports if the stretch is under `MinStretch` (30 m).

   Cost if wrong: a short fight on the slag cart.
2. Ruling: the bridge is the cavern's party-lane deck. It is built from the layout (the deck cells of `RailLine.Cavern` on the party lane), not from its At.
   - `r4_bridge`'s At must lie within `BridgeRules.AtSlack` (8 m) of the span's start.
   - The span must be `flat`. `flat` holds when every span cell is at one level, none is a ramp, and neither lane's cell just before `Cavern.From` or just after `Cavern.To` is a ramp cell (the bed eases 0.5 m into a flat neighbour: `RailBed.SurfaceAt`, `EaseLength` 1 m).
   - Otherwise it is dead: a plain deck, logged `(dead: …)`.
   - No generator code changes, so every `LayoutHash` stands.

   Cost: none.
3. Ruling: the collapse starts `Grace` (1.5 s) after the train's rear (the last body's back, `RunawayRules.TrainRearS`) passes the bridge's start, with the call at that moment.
   - Its front then runs from the start at `Pace` = `PaceShare` (0.8) × `CartPhysics.TargetSpeed(0, crewWagons)`, fixed when it starts. That is 4.8 m/s empty and 4.17 m/s with three wagons.
   - A cell goes when the front passes its far edge. The front stops at the bridge's end.
   - "The bridge groans under the cart." is logged, with `trestle_creak`, as the cart's front comes onto it.

   Cost: an unpumped train outruns it, so it bites only under Ruling 4.
4. Ruling: "too slow" means braking. The collapse takes the train (`RailController.Fall`) only when its front reaches the train's rear while the train is rolling and a seated player braked within `BrakeMemory` (2 s).
   - Otherwise its front holds `HoldShort` (2 m) behind the rear. That covers a stall, the watchdog, a heavy load and the stop's own braking.
   - "Don't stop — it's going under us!" is called once per arming when a player brakes with the front within `BrakeCallNear` (12 m) of the rear.
   - Companions never brake, so a solo player is never killed by a station they could not know was theirs (the spec's "speed checks are shared").

   Cost if wrong: the owner may want a pump check. Then raise `PaceShare` past 1 and have companions pump, as at the jump.
5. Ruling: what ends the collapse.
   - After an arrival it runs on to the bridge's end.
   - A wreck or a fall stops it where it stands.
   - A `Restore` (a wipe's rise or a load), a spare's takeover, or ride 4's next arming stands the whole bridge again, footings included. (Ruling 18 likewise for the drop.)
   - A wrecked train is never taken: `Fall` is a no-op while wrecked, and a spare's takeover moves the cart off the bridge.

   Cost: a wreck on the bridge is the plain wreck.
6. Ruling: while the party's train is on a live bridge at 1 m/s or more (`BridgeRules.HoldsRiders`), "Get out" is neither offered nor taken, in solo or from a remote seat. E on the bridge is the leap's alone, so nobody throws themselves onto a collapsing deck by habit. Cost: none.
7. Ruling: gone decks are runtime state in `FloorLayout.GoneBridges`, read by `CrevasseRules.Footing`.
   - `ChasmDressing` always builds each cavern deck cell's look (both lanes) as its own `DeckLook_{x}_{y}` mesh, and `SetGone` toggles it with the cell's footing box.
   - The live bridge cuts the party track bare over its span with `RailTrack.CutMore` and lays its own rails and sleepers (`RailTrack.LaySpan`), one mesh per cell. The live drop does the same on the slag line. `RailTrack` keeps every gap it is built or cut with, and `Cut` and `CutMore` both add to them, so neither the jump's gap nor the bridge's is re-laid by a later cut (floor 12 cuts r5_jump's gap after every piece is built: `CutGaps`).

   Cost: about 44 more draw calls in the cavern, distance-culled.
8. Ruling: the ride-4 runaway at the bridge.
   - A runaway whose front reaches the gone span goes into the crevasse quietly and is freed. It is logged "The runaway goes down with the bridge!" only with a hero within `SwitchBackRules.PlungeHeard` (40 m).
   - One right on the train's tail may still ram on the bridge, as plan 12's Ruling 12 has it. A ram that wrecks is the plain wreck (Ruling 5).
   - Plan 12's deferred "ram in the spiral" is void.
   - An unpumped train with the split left straight is rammed about 98 m after the runaway breaks loose, with its rear at S 901.6 (three wagons, `--test-runaway=ram`) to about S 909.4 (none): at the bridge's start and inside its 1.5 s grace, so the ram always comes before the collapse starts. `=ram` must stay green unchanged, its second pass's wreck included (the cart's front is on the bridge by then: Ruling 5's plain wreck).

   Cost: the runaway seldom rams in the cavern.
9. Ruling: the leap is a set piece `leap` (`r4_leap`) whose At is the stretch's start. Its drop comes from the layout: `LeapRules.DropSpan(Cavern)` gives the party indices, and their slag-lane twins are the cells that go.
   - It is dead when there is no cavern, the stretch starts short of the cavern, or the stretch is under 30 m.
   - A dead leap offers nothing and leaves the slag line whole, logged `(dead: …)`.

   Cost: none.
10. Ruling: the leap's slag cart is the ride's own `r4_slag`. It is retuned to Offset 0 (side by side), with its At 20 m before the stretch, so it has caught its pace point by the stretch's start. Any of the ride's slag carts in reach counts, the nearest along first. Cost: two values of one encounter.
11. Ruling: "within reach (about 2.5 m)" is the rim gap, |lateral| − 2 × `CartRules.HalfWidth` ≤ `Reach` (2.6 m), together with |along| ≤ `AlongReach` (1.2 m).
    - Both are measured between the cart centres in the party cart's frame. Twin lanes 4 m apart give 2.5 m.
    - The server takes a press with `AcceptSlack` (1 m) more on both.

    Cost: none.
12. Ruling: who is offered what, and when.
    - **"Leap across"** goes to a hero who meets all of these:
      - human-controlled (`ControlKind.LocalHuman` or `Remote`; never a companion or a bot);
      - up, and seated in the party's rolling cart;
      - the stretch is live and not yet called;
      - a slag cart is in reach with fewer than `MaxLeapers` (2) heroes on its deck.
    - **"Leap back"** goes to a hero who is up on that slag cart, while the party cart is in reach with a hero seat free and the slag cart's front is at least `BackCloses` (3 m) short of the drop.
    - The server takes a leap back until the slag cart's front reaches the drop's edge.

    Cost: a client whose slag copy runs ahead sees the offer close early, never late.
13. Ruling: a leap is `SitIn` on the target cart. It is never `GetOut`: no throw, no run-over.
    - The slag cart keeps a real seat book. `Admit` and `Release` raise `RiderChanged`.
    - It has two deck seats (`SlagCart.DeckSeat`) on its party-side rim at heap height (local x = `InnerSide` × 0.4, y = `StandOn`, z = −0.5 and +0.5).
    - The change is instant, with `cart_jump` and then `cart_land`, and a small shake (none in VR).

    Cost: no flying arc.
14. Ruling: a leaper is not aboard the party's cart. Every party-cart judgement counts `Cart == _rails.Cart`, never `Seated`:
    - the departure's player aboard;
    - pumps and brakes;
    - `AbleToThrow`;
    - the fall's toll;
    - the arrival's set-down;
    - the rim spots.

    Cost: none.
15. Ruling: a slag cart with a hero on its deck.
    - Its brake shoe takes no hits, so it never derails under a friend.
    - Every way it goes other than the drop sets its leapers down quietly on `CrevasseRules.NearestSetDown`, never into a drop. That covers `Free` (a wreck, a fall, a restore, an arrival, the line's end, a stuck pace) and a ram.

    Cost: a shoe shot cannot derail a cart that carries a friend.
16. Ruling: the drop.
    - **The call.** When the party's front reaches the drop's party S − `CallAhead` (25 m, ≈5 s), these happen once per arming:
      - the drop's cells give way (debris and `timber_collapse`);
      - a red lamp on a stub post at its near edge lights, as the broken bridge's does;
      - each live slag cart of the ride rings its bell;
      - a seated companion calls "The slag line's going — leap back!".
    - **After the call** no leap across is offered.
    - **The fall.** A slag cart whose front reaches the drop's near edge goes over. Its creatures die to the crevasse (`CrevasseWatch.Take`), and clients play the quiet plunge (`SwitchBackRules.PlungedAt` on the drop's edge).

    Cost: none.
17. Ruling: only an able human (human-controlled and up) still aboard at the drop falls with it, through `CrevasseWatch.Take`: fallen, and back when the party next reaches a stop.
    - Any other hero aboard (downed, or run by the AI after a possession) is hauled back into a free hero seat of the party's cart. The log says "{companion} hauls {name} back across!", or "{name} is hauled back across." with no companion seated.
    - This is plan 11's R-lone, judged at the drop.

    Cost: a downed leaper is spared.
18. Ruling: the drop stands again at ride 4's next arming, a `Restore` or a spare's takeover (as the bridge does: Ruling 5). While it is down and the party's cart is past it, traffic starts just past it (`LeapRules.TrafficStartS`, on top of the ride-3 break's rule). Cost: none.
19. Ruling: the calls. `BarkTrigger` gains `BridgeGoing, BridgeBrake, SlagDrop`, appended after `SplitNow`. All three are Shout, with subtitle-only stock lines in `RideBarks` (three own lines per roster name, Sable's a woman's, two per class, three generic). The log copy with nobody to speak:
    - "The bridge is going behind us — keep her rolling!";
    - "Don't stop — it's going under us!";
    - "The slag line's going — leap back!".

    There is no leap-across call: the prompt is the offer. Cost: none.
20. Ruling: the wire. Protocol stays 17.
    - **Seats.** A leap is a `SeatMsg` naming the slag cart's `CartId` and the deck seat. An unseat whose rider sits in another cart at the flush is not sent (`LeapRules.SendsUnseat`), because its seat in the new cart moves it.
    - **The client.** It resolves slag ids from its copies. It sets a rider down where it stands when its slag copy is freed first, or when an unseat names a copy already freed.
    - **The presses** are `InteractIntent("slag:{cartId}")` across and `cart:0` back.
    - **The props.** `PropEvt` carries `bridge:{piece}` ("gone {n}", "stands") and `leap:{piece}` ("drops", "stands"). `RailPropsDto` gains `BridgeGone` (int, the party cells gone) and `SlagLineDropped` (bool), and both go into the prop fingerprint.

    Cost: none.
21. Ruling: other flows need no hold, with one exception. `--test-runaway=tail` follows a pumped train through the cavern with the runaway 15–19 m behind and must see it halt behind the train held at stop 4 (plan 12's Ruling 13), so the bridge would take it (Ruling 8). `=tail` holds the bridge with `enc.DebugHoldBridge(4)`; `--test-cavern=runaway` is the flow that proves the plunge. Nobody brakes in the cavern on any other flow and nobody leaps, so the bridge only runs behind them. Every existing flow must stay green in Task 7's sweep. Cost if wrong: a flow that brakes on ride 4 meets the collapse, and gets plan 12's `DebugHold…` pattern.
22. Ruling: the leap's fight is the existing seated combat. The leaper swings from the deck seat as from a cart seat, and the riders fight as carried creatures. Nothing new is added. Cost: the riders may keep throwing at the party's cart.
23. Ruling: legibility is a gate (Task 6), as plan 12's Ruling 23 is.
    - Physics rays run from the seated eye, headless too, each with a control that must be blocked (Task 6).
    - Windowed screenshots are held A/B twins (the party cart pinned at the place until both are saved, as plan 12's gate is since 5fc41d3): one with the gone cells shown, or the slag cart hidden.
    - Plan 12's luminance gate is re-run once, windowed (Task 6).
    - Any failure stops and reports. A fix inside that task is allowed only for dressing (a lamp's energy or range, or a second lamp).

    Cost: one or two short windowed runs.

## Global Constraints

- **No layout moves.** This plan changes no generator code.
  - Every floor's `ContentHash`, and floor 12's `LayoutHash` (`16995108853750381062`), stay unchanged.
  - `RailLineTests.DefaultKnobs_BuildTodaysFloors` stays green in every task.
  - `--rehash-plans=data/adventures/builtin --quit-after=2` changes nothing.
- **No rng.** Nothing new draws from an rng. The rail pass still draws only from `new Random(seed ^ 0x2A11)`.
- **Floor 12's shape.** Room indices and order never change. Floor 12 must still generate:
  - `objective=jailbreak rails=6`;
  - five rides, each ≥ 160 m, and a total ≥ 1200 m;
  - `cavern=238..259`;
  - `break=` on ride 3;
  - `split=` on ride 4.
- **The wire.** `Protocol.Version` stays **17** (the branch is unreleased). New state that clients must see goes in existing messages (`SeatMsg`, `PropEvt`, `RailPropsDto`, `CartStateDto`).
- **Lethality.** No bridge or leap code reads `TierRules.Lethality`: a fall is a fall at every level.
- **Assets.** CC0 assets only (the repository is public). This plan adds no assets: meshes are procedural, and the cues are existing names (`trestle_creak`, `timber_collapse`, `cart_bell`, `cart_jump`, `cart_land`, `cart_wreck`).
- **Voice.** No voice spend: new lines are subtitle-only (`"VoiceId": null`). Never run a Runware/voice bake.
- **Hand-edited JSON** keeps the game's escaping and a two-space indent. `git diff` must show only the values you meant to change. The escapes are:
  - `'` → `'`;
  - `&` → `&`;
  - `<` → `<`;
  - `>` → `>`;
  - `+` → `+`;
  - non-ASCII → `\uXXXX` (upper-case hex).
- **Tests.** `tests/` (xUnit) is not in git. Write and run tests locally, and never `git add tests/`. Commit only `src/`, `data/`, `docs/` and `scripts/`.
- **Headless runs.**
  - Run `dotnet build DungeonCrawler.csproj` before any `godot --headless --path . -- <flags>` run. Engine flags such as `--headless --path .` go before `--`, and game flags go after it.
  - Run two or more non-network flows through `scripts/flows.sh` (longest first, e.g. `--test-chase --quit-after=750`); a single flow may be run directly. Never run a network flow (`scripts/net_test.sh`, `--test-net-*`, `--server`, `--join=`) through it or beside it.
  - A flow passes only on exit 0 **and** its own `[...] RESULT ok` line (never the early `[builtin] RESULT ok`) **and** no `ERROR` line in its log.
  - While `.superpowers/sdd/shared/RIDE.lock` exists, start no `dotnet build`, no suite and no godot or `flows.sh` run: wait (poll every 60 s). Never create or delete the lock.
  - `--test-crew` needs `--quit-after=570` and `--test-chase` needs `--quit-after=750`. The new `--test-cavern` (any variant) sets `_quitAfter = 300`.
  - Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N` (or `--quit-when-empty`).
- **The RIDE pass.** Only the controller runs `RIDE=1 scripts/net_test.sh`, alone on the machine, under `.superpowers/sdd/shared/RIDE.lock`, on a platform profile other than `low-power` (a low-power run is invalid, not a failure). It is not flaky at baseline any more: a failure is investigated from its kept logs, never rerun to green.
- Full test suite: `dotnet test tests/DungeonCrawler.Tests.csproj`.
- Implementers run on opus, reviewers on fable (ruling of 2026-11-22).
- Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- No installers or dist builds (`export_all.sh`, `package.sh`, `publish_*.sh`), no push and no merge. The controller commits this plan. Implementers commit only their task's files, staged by explicit path.

## Review Focus

- **A co-op client presses "Leap back" on its last offered frame** (its slag copy 3 m short of the drop) while the server's slag cart runs ahead. The server must take it, and the player never falls after seeing the offer.
  - Pinned by `LeapRulesTests.TheServerTakesALeapBack_UntilTheDropsEdge` (Task 4).
  - Pinned by the RIDE joiner's last-frame press (Task 7).
- **A hero on the slag deck who is down, or whose control passed to the AI, at the drop.** They are hauled back and never fall.
  - Pinned by `LeapRulesTests.OnlyAnAbleHuman_FallsWithIt` (Task 5).
  - Pinned by `--test-cavern=lone` (Task 5).
- **A wreck, a restore or a line-end free of a slag cart while a hero rides it,** on the server and on a client. The hero is set down on footing, never fallen and never left seated on a freed cart.
  - Pinned by `--test-cavern=wreck` (Task 4).
  - Pinned by `LeapRulesTests.AnUnseatIntoAnotherCart_IsNotSent` (Task 4).
  - Pinned by the client's freed-copy set-down, checked in the RIDE pass (Task 7).
- **A leaper is counted aboard the party's cart** by some departure, pump, switch, fall-toll or arrival rule. Every party-cart judgement must read `Cart == _rails.Cart`.
  - Pinned by `=leap`'s aboard checks (Task 4).
  - Pinned by `=stay`'s fall toll and arrival (Task 5).
- **A stall, a watchdog free or a heavy load on the bridge with nobody braking.** The collapse never takes the train.
  - Pinned by `BridgeRulesTests.WithNoBrake_TheFrontHoldsShortOfTheTrain` (Task 3).
  - Pinned by `--test-cavern=stall` (Task 3).

---

### Task 1: Gone decks (runtime footing, per-cell cavern looks, `CutMore`)

**Files:**
- Modify: `src/Dungeon/Generation/FloorLayout.cs` (`GoneBridges`)
- Modify: `src/Dungeon/CrevasseRules.cs` (`Footing` reads it; `CavernDecks`)
- Modify: `src/Dungeon/ChasmDressing.cs` (per-cell cavern deck looks, `SetGone`, `Instance`, the `[chasm]` line)
- Modify: `src/Dungeon/Props/RailTrack.cs` (the kept `Gaps`, `CutMore`, `LaySpan`)
- Test: `tests/Dungeon/CrevasseRulesTests.cs`

**Interfaces:**
- Produces:
  - `public HashSet<GridPos> FloorLayout.GoneBridges { get; } = new();`: bridge cells that gave way on this floor build. It is not part of any hash, not saved, and empty at generation.
  - `CrevasseRules.Footing` returns false on a cell in `l.GoneBridges`, as on a chasm. `IsDrop` already holds for a bridge cell.
  - `public static List<GridPos> CrevasseRules.CavernDecks(FloorLayout l)`:
    - the `BridgeKind.Deck` party-lane cells of `l.Rail.Cavern` in line order, then their deck enemy twins (the 4-neighbour on the enemy lane) in the same order;
    - empty with no cavern.
  - `ChasmDressing`:
    - `public static ChasmDressing? Instance` (set in `_EnterTree`, cleared in `_ExitTree`);
    - the look of each `CavernDecks` cell (its slab and bent) is its own `MeshInstance3D` named `DeckLook_{x}_{y}`, not part of the shared `Bridges` mesh, and is not built headless;
    - `public void SetGone(FloorLayout layout, GridPos cell, bool gone)` sets the cell's `Deck_{x}_{y}` shape's `Disabled` to `gone`, hides or shows its look, and adds or removes the cell in `layout.GoneBridges`. It runs on every role. A cell not in `CavernDecks` logs `[chasm] ERROR: SetGone on {cell}, not a cavern deck` and does nothing;
    - the build line becomes `[chasm] regions={r} decks={d} planks={p} cavern={n}`, with n the `CavernDecks` count.
  - `RailTrack` keeps the gaps it is bare over. `Build` stores its `gaps` (and clears what a former build held); `Cut` and `CutMore` both **add** theirs and re-lay, so a gap once cut stays bare whatever the order of the calls (floor 12 cuts r5_jump's gap after every piece is built: `CutGaps`). `public IReadOnlyList<(float from, float to)> Gaps` reads them. `CutMore` is `Cut` by another name and may be dropped.
  - `public void RailTrack.LaySpan(Node3D into, float from, float to, string name)` meshes the rails and sleepers of S `[from, to]` from this track's own baked frames (`Curve.SampleBakedWithRotation`), with its own `Gauge`, rail and sleeper sizes and sleeper phase (`SleeperEvery/2 + k·SleeperEvery` along the whole track), as one rail mesh and one sleeper mesh under `into`. Not called headless. `CavernBridge` and `SlagDrop` use it per cell and define no rail size of their own.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AGoneDeck_GivesNoFooting()
{
    var l = Strip();
    var deck = new GridPos(6, 2);
    l.Bridges[deck] = BridgeKind.Deck;
    var at = new Vector2(23.5f, 10f); // in (5,2), half a metre short of the deck
    Assert.True(CrevasseRules.Footing(l, Centre(deck), Cell));
    Assert.Equal(new Vector2(3f, 0f), CrevasseRules.KeepFooting(l, Cell, at, new Vector2(3f, 0f), recover: true));
    l.GoneBridges.Add(deck);
    Assert.False(CrevasseRules.Footing(l, Centre(deck), Cell));
    Assert.Equal(Vector2.Zero, CrevasseRules.KeepFooting(l, Cell, at, new Vector2(3f, 0f), recover: true));
    Assert.NotEqual(deck, CrevasseRules.CellOf(CrevasseRules.NearestStandable(l, Centre(deck), Cell)!.Value, Cell));
    Assert.True(CrevasseRules.IsDrop(l, deck));
}

[Theory, MemberData(nameof(ChasmTests.SplitSeeds), MemberType = typeof(ChasmTests))]
public void CavernDecks_AreBothLanesDecksOverTheSpan(int seed, FloorObjectiveKind obj)
{
    var l = ChasmTests.GenSplit(seed, obj);
    var decks = CrevasseRules.CavernDecks(l);
    if (l.Rail is not { Cavern: { } c } rail) { Assert.Empty(decks); return; }
    var n = c.To - c.From + 1;
    Assert.Equal(2 * n, decks.Count);
    Assert.Equal(rail.Party.Skip(c.From).Take(n), decks.Take(n));
    Assert.All(decks, d => Assert.Equal(BridgeKind.Deck, l.Bridges[d]));
    Assert.Empty(l.GoneBridges);
}
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~CrevasseRulesTests"`. Expected: a compile error (`GoneBridges`, `CavernDecks`).
- [ ] **Step 3: Implement** the Interfaces above. The per-cell look reuses the slab and bent boxes the shared mesh draws today, in one `SurfaceTool` per cell.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS, with `DefaultKnobs_BuildTodaysFloors` green. Then:
  - `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map 2>&1 | grep "\[chasm\]"`. Expected: `regions=12 decks=50 planks=3 cavern=44`. If n is not 44, report the number.
  - `timeout 300 godot --headless --path . -- --test-crevasse 2>&1 | grep "\[crevasse\] RESULT"`. Expected: `ok` (A0 still walks the cavern deck end to end).
  - **The `--track-depth` check** (Tasks 3, 5 and 7 run it again). With the RIDE lock absent:
    `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth 2>&1 | grep -E "\[track-depth\] (threshold|PartyTrack |EnemyTrack |piece r4_|cavern|total)|\[map\] rail StopS"`
    Expected, as in `.superpowers/sdd/2026-11-22-exposed-track-fix/t5-depth.txt`:
    - `tracks=PartyTrack,EnemyTrack,Rails/Crossover_r3_switch_out,Rails/Crossover_r3_switch_back,Spur_r5_spur/Spur,Split_r4_split/Spur` (this plan adds no `RailTrack`);
    - `PartyTrack length=1243.7 samples=2489 buried=0 inside=0 … nofloorDrop=0 nofloorOff=0 budget=0`;
    - `EnemyTrack length=1275.7 samples=2553 buried=0 inside=0 … nofloorDrop=40 nofloorOff=0 budget=0` (the 40 are ride 3's gap);
    - `cavern party S=903.1..991.1` and `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`;
    - `total buried=0 inside=0 nofloorOff=0 budget=0 … buckledLaneCells=0 rampsOnLine=2`;
    - from Task 3 `piece r4_bridge ride=4 kind=bridge S=903.1`, from Task 5 `piece r4_leap ride=4 kind=leap S=913.1` (each ± 0.1).

    Any other `nofloorDrop` on `PartyTrack` or `EnemyTrack` means a deck footing is missing at the build: stop and report. The verdict does not catch it.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Generation/FloorLayout.cs src/Dungeon/CrevasseRules.cs src/Dungeon/ChasmDressing.cs src/Dungeon/Props/RailTrack.cs
git commit -m "feat(crevasse): a bridge cell can give way at runtime (FloorLayout.GoneBridges, no footing there); the cavern's lane decks are drawn one look per cell (ChasmDressing.SetGone); RailTrack keeps every gap it is cut over (Cut and CutMore add) and lays a span's rails from its own frames (LaySpan)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Floor 12's cavern measured; the bridge and the leap placed (the gate)

**Files:**
- Create: `src/Dungeon/Rails/Core/LeapRules.cs` (its geometry: `LeapIn`, `MinStretch`, `DropCells`, `DropFromEnd`, `DropSpan`)
- Modify: `src/Core/DebugTools.cs` (`--print-map`'s `[map] rail cavern:` line)
- Modify: `data/adventures/builtin/floor_12.json` (`r4_bridge` and `r4_leap` appended; `r4_slag`'s At and Offset)
- Modify: `src/Adventure/Core/Validate/PlanValidator.cs` (`SetPieceKinds` + `"bridge"`, `"leap"`), `src/Adventure/Core/Plans/FloorPlan.cs` (the kinds' doc)
- Test: `tests/Dungeon/Rails/LeapRulesTests.cs`, `tests/Adventure/RailEncounterPlanTests.cs`

**Interfaces:**
- Produces:
  - `LeapRules`:
    - `LeapIn = 10f`, `MinStretch = 30f`, `DropCells = 2`, `DropFromEnd = 2`;
    - `public static RailSpan? DropSpan(RailSpan? cavern)` returns `new(To − DropFromEnd − DropCells + 1, To − DropFromEnd)` (party indices; their slag-lane twins are the cells that go), or null.
  - `--print-map` prints:

    `[map] rail cavern: ride {r} party S={from:0.0}..{to:0.0} ({n} cells, flat {bool}), drop {d0}..{d1} at party S={dp:0.0} (enemy S={e0:0.0}..{e1:0.0}), stretch {len:0.0} m, stop {r} at S={stop:0.0} ({gap:0.0} m past)`

    - `from` is the near edge of party cell `Cavern.From` (`ClosestS` of its centre − 2), and `to` is the far edge of `Cavern.To` (+ 2).
    - `flat` holds when every span cell is at one level, none is a ramp, and neither lane's cell just before `Cavern.From` or just after `Cavern.To` is a ramp cell (the bed eases 0.5 m into a flat neighbour: `RailBed.SurfaceAt`, `EaseLength` 1 m).
    - `dp` is the near edge of party cell `DropSpan.From`, and `e0..e1` are the enemy track's S over the twin cells' outer edges.
    - `len = dp − (from + LeapIn)`, and `gap = StopS[r] − to`.
  - Floor 12's pieces, appended in this order:

    | Piece | Ride | Kind | At |
    |---|---|---|---|
    | `r4_bridge` | 4 | `bridge` | f(from) |
    | `r4_leap` | 4 | `leap` | f(from + LeapIn) |

    - `r4_slag` keeps its riders; its At becomes f(from + LeapIn − 20) and its `Offset` 0.
    - `f(S) = (S − StopS[3]) / (StopS[4] − StopS[3])`, to 3 decimals.
  - Until Tasks 3 and 5 the controller has no `case` for the two kinds (`node == null`), so the floor plays as today with `r4_slag` alongside.
  - The task report gives `from`, `to`, `dp`, `e0..e1`, `len`, `gap`, the three At values, and `r4_slag`'s pace point. Task 4 re-checks them.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheDrop_IsTwoSlagDeckCellsTwoShortOfTheCavernsEnd()
{
    Assert.Equal(new RailSpan(256, 257), LeapRules.DropSpan(new RailSpan(238, 259)));
    Assert.Equal(new RailSpan(105, 106), LeapRules.DropSpan(new RailSpan(98, 108)));
    Assert.Null(LeapRules.DropSpan(null));
    Assert.Equal(10f, LeapRules.LeapIn);
    Assert.Equal(30f, LeapRules.MinStretch);
}

[Fact] public void Floor12_Ride4_TheCavernHoldsTheBridgeAndTheLeap()
{
    var f = ShippedStory.Floor(12); // data/adventures/builtin/floor_12.json
    var p = f.RailSetPieces.Where(x => x.Ride == 4).ToDictionary(x => x.Id);
    Assert.Equal("bridge", p["r4_bridge"].Kind);
    Assert.Equal("leap", p["r4_leap"].Kind);
    Assert.True(p["r4_split"].At < p["r4_bridge"].At && p["r4_bridge"].At < p["r4_leap"].At);
    Assert.Equal(new[] { "r4_bridge", "r4_leap" }, f.RailSetPieces.TakeLast(2).Select(x => x.Id)); // appended
    var slag = f.RailEncounters.Single(e => e.Id == "r4_slag");
    Assert.Equal(0f, slag.Offset);
    Assert.True(slag.At > p["r4_split"].At && slag.At < p["r4_leap"].At);
}
```

  The loader is `ShippedStory.Floor(12)`. In the same file update `BuiltinFloor12_HasTheFiveRides`: the id list gains `"r4_bridge", "r4_leap"` at its end, the count becomes 21, and `r4_slag` becomes `(0.487f, 0f)`.

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~LeapRulesTests|FullyQualifiedName~RailEncounterPlanTests"`. Expected: a compile error (`LeapRules`).
- [ ] **Step 3: Measure.** Add `LeapRules`' geometry and the `[map] rail cavern:` line, then run `dotnet build DungeonCrawler.csproj && timeout 200 godot --headless --path . -- --builtin --no-ai --floor=12 --print-map 2>&1 | grep -E "\[map\] rail"`.

  Expected on floor 12: `[map] rail cavern: ride 4 party S=903.1..991.1 (22 cells, flat True), drop 256..257 at party S=975.1 (enemy S=…), stretch 62.0 m, stop 4 at S=1022.5 (31.4 m past)`. `from..to` must equal `--track-depth`'s `cavern party S=` line. The At values are then `r4_bridge` 0.527, `r4_leap` 0.567 and `r4_slag` 0.487 (S 893.1).

  **Stop and report, without retuning, if any of these fails:**
  - the cavern is not on ride 4;
  - `flat` is False;
  - `len` < 30;
  - `gap` < 15;
  - f(from + LeapIn − 20) is short of f(far + 4), with `far` the split crevasse's far end from the `[map] rail split:` line (881.6 on floor 12, so the bound is S 885.6);
  - the `[map] rail` line's `rides`, `cavern=`, `break=` or `split=` moved, or `objective=jailbreak rails=6` did;
  - any of the expected values above is off by more than 0.1 m or 0.001.
- [ ] **Step 4: Implement.** Add the kinds and the doc, append the pieces and retune `r4_slag` at the measured fractions. Then:
  - `timeout 60 godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2`, then `git diff --stat data/` shows only `floor_12.json`, and `git diff data/` shows only the piece edits, with no `LayoutHash` change;
  - `--print-map … | grep -E "repaired|dropped|demoted|adapted"` prints nothing;
  - the filter, the builtin hash tests and the full suite pass;
  - `timeout 300 godot --headless --path . -- --test-ride 2>&1 | grep "\[ride\] RESULT"` reads `ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/LeapRules.cs src/Core/DebugTools.cs data/adventures/builtin/floor_12.json src/Adventure/Core/Validate/PlanValidator.cs src/Adventure/Core/Plans/FloorPlan.cs
git commit -m "data(floor 12): the cavern measured ([map] rail cavern:); ride 4's bridge and leap pieces at its measured places, r4_slag retuned to run alongside into the leap stretch

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 3: The collapsing bridge

**Files:**
- Create: `src/Dungeon/Rails/Core/BridgeRules.cs`, `src/Dungeon/Rails/SetPieces/CavernBridge.cs`, `src/Dungeon/Rails/RailEncounterController.Bridge.cs`, `src/Core/DebugTools.Cavern.cs`
- Modify: `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (`Piece.Bridge`, `case "bridge"` in build/tick, the prop state), `RailEncounterController.Runaway.cs` (the runaway down with the bridge), `RailEncounterController.cs` (`CollectPartyLaneHoles` adds the gone span), `RailEncounterController.Net.cs` (the client's `bridge:` props)
- Modify: `src/Dungeon/Rails/RailController.cs` (`LastBrakeAt`; "Get out" withheld on the bridge), `src/Dungeon/Rails/Core/RideInputRules.cs` (`holdOn`), `src/Net/ServerWorld.cs` (only the test server's print of a refused get-out; the refusal itself is `RailController.UseCart`'s), `src/Net/Core/PropCheck.cs` (`RailPropsDto.BridgeGone`, the fingerprint)
- Modify: `src/Core/DebugTools.Runaway.cs` (`=tail` holds the bridge)
- Modify: `src/Adventure/Core/Plans/CampaignPlan.cs` (`BarkTrigger.BridgeGoing, BridgeBrake, SlagDrop`), `src/Adventure/Core/Voice/RideBarks.cs`, `src/Adventure/Core/Voice/VoiceLines.cs` (Shout), `src/Core/DebugTools.cs` (the flag and help line)
- Test: `tests/Dungeon/Rails/BridgeRulesTests.cs`, `tests/Adventure/RideBarksTests.cs` (the trigger count)

**Interfaces:**
- Consumes: Task 1's `SetGone`, `CavernDecks`, `CutMore` and `LaySpan`; `RunawayRules.TrainRearS`; `FreeRideRunaway`.
- Produces:
  - `BridgeRules` (engine-free):
    - constants `Grace = 1.5f`, `PaceShare = 0.8f`, `BrakeMemory = 2f`, `HoldShort = 2f`, `BrakeCallNear = 12f`, `AtSlack = 8f`, `CueEveryCells = 2`;
    - `float Pace(int crewWagons) => CartPhysics.TargetSpeed(0, crewWagons) * PaceShare`;
    - `bool Running(double rearOnAt, double now)`: NaN false; otherwise `now − rearOnAt ≥ Grace`;
    - `float Advance(float frontS, float toS, float pace, float dt, float trainRearS, bool braked)`: braked, `min(toS, frontS + pace·dt)`; else `min(toS, min(frontS + pace·dt, max(frontS, trainRearS − HoldShort)))`. The front never moves back;
    - `bool Braked(double lastBrakeAt, double now)`: NaN false; otherwise `now − lastBrakeAt ≤ BrakeMemory`;
    - `bool Catches(bool rolling, bool braked, float frontS, float trainRearS) => rolling && braked && frontS >= trainRearS`;
    - `int GoneCells(float frontS, IReadOnlyList<float> cellFarS)` counts the cells (in line order) whose far edge is ≤ the front;
    - `bool TakesRunaway(float runawayFrontS, float fromS, float frontS)`: NaN front false; otherwise `fromS ≤ runawayFrontS ≤ frontS`;
    - `bool HoldsRiders(bool trainOnBridge, float v) => trainOnBridge && v >= 1f`;
    - `bool CallsBrake(bool braking, float frontS, float trainRearS) => braking && trainRearS − frontS <= BrakeCallNear`.
  - `CavernBridge : Node3D, ISoundPoint`:
    - `Build(TrackSampler track, IReadOnlyList<(GridPos Cell, float FromS, float ToS)> cells)` lays rails and sleepers as one mesh per cell, each with `RailTrack.LaySpan` of the party lane's own track; it builds no mesh headless, but keeps the cells;
    - `int Gone`;
    - `void GiveWay(int upTo, Node3D? debrisRoot)` hides the meshes of cells `[Gone, upTo)`, calls `ChasmDressing.Instance.SetGone(…, true)` for each, tumbles a few timbers per cell (`TimberDebris.Tumble`) and plays `timber_collapse` every `CueEveryCells` cells;
    - `void Stand()` shows every cell again and calls `SetGone(…, false)`;
    - `NetRef = "bridge:{piece}"`. It tells "gone {Gone}" when a server's `GiveWay` moves it and "stands" when a server's `Stand` follows a gone state.
  - `RailEncounterController`:
    - `public float BridgeFromS, BridgeToS` (NaN with no live bridge), `public float CollapseS` (NaN while standing), `public int BridgeCells`, `public int BridgeGone`, `internal int BridgeTakes`;
    - `private Node3D? BuildBridge(Node3D root, Piece piece)`:
      - its cells are the party half of `CavernDecks`;
      - it is dead (Ruling 2) unless At lies within `AtSlack` of `from` and the span is flat;
      - live, it calls `_rails.Party.Track.Track.CutMore([(from, to)])` and builds a `CavernBridge`;
      - it prints `[rails] bridge {id}: party S={from:0.0}..{to:0.0}, {n} deck cell(s), over the crevasse {bool} (live | dead: …)`;
    - `private void TickBridge(Piece piece, PartyController party, float dt)`, server and solo, while its ride is armed:
      - it logs "The bridge groans under the cart." with `trestle_creak` as the front comes onto it;
      - it stamps `rearOnAt` when `TrainRearS ≥ from`;
      - at `Running` it sets `CollapseS = from` and the pace, calls `RailCalls.Say(party, _rails.Cart, BarkTrigger.BridgeGoing, "The bridge is going behind us — keep her rolling!")` and prints `[rails] t=… bridge {id} goes from S=… at {pace:0.00} m/s ({w} wagon(s)); called by {who}`;
      - each tick it `Advance`s, gives way to `GoneCells` and prints `[rails] t=… bridge {id}: {n}/{N} gone, front S=…, rear S=…` every 4 cells;
      - the first tick the hold clamps prints `[rails] t=… bridge {id} held by the stall guard at S=… (rear S=…)`;
      - `CallsBrake` triggers `BarkTrigger.BridgeBrake` ("Don't stop — it's going under us!") once per arming;
      - `Catches` prints `[rails] t=… bridge {id} takes the train at S=… (braked {s:0.0} s ago)`, adds to `BridgeTakes` and calls `_rails.Fall("The bridge goes out from under the train!")`;
      - a wreck or a fall stops it, and an arrival does not (Ruling 5);
    - `ArmBridge(ride)`, the restore path and a spare's takeover call `Stand()` and set `CollapseS = NaN` (Ruling 5);
    - `internal void DebugHoldBridge(int ride)`: a server test hook in `DebugHoldRunaway`'s pattern. Ride `ride`'s bridge never starts to go on this floor build (one going now stands again), printed once as `[rails] bridge {id}: held by the test`;
    - `public bool HoldsRiders()` is true when the party's train (its front past `from` and its rear short of `to`) rolls at ≥ 1 m/s on a live bridge, on every role, reading the peer's own cart;
    - the ride runaway: each tick with `TakesRunaway`, it is freed plainly, `FreeRideRunaway("it went down with the bridge")`, with neither `Rammed` nor `Plunged` set, so a client's copy just goes (on the party line the Derailed flag is read as a ram: `RunawayRules.ShowsRam`). The server plays the thud and `cart_crash` at the place and prints `[rails] t=… runaway {id} goes down with the bridge at S=…`; the log line follows Ruling 8. `--test-cavern=runaway` also checks that the body's `Rammed` and `Plunged` were both false as it went;
    - `CollectPartyLaneHoles` adds `(from, from + Gone cells' length)` while any cell is gone, so a chaser hops it and a relief's seat target never lies in it;
    - the client applies `bridge:{piece}` "gone {n}" (`GiveWay(n, root)`) and "stands" (`Stand()`), and `RailPropsDto.BridgeGone` on the catch-up (`GiveWay(n, null)`).
  - `RailController`:
    - `public double LastBrakeAt { get; private set; } = double.NaN` is set to `NetSession.Instance.Now` on each server or solo tick whose inputs brake (any seated player's brake in the party's cart);
    - its seated prompt and get-out pass `holdOn: RailEncounterController.Instance?.HoldsRiders() == true`.
  - `RideInputRules`:
    - `BoardPrompt(bool seatedHere, bool held, bool lastStop, bool heroSeatFree, bool inReach, bool holdOn = false)`: seated with `holdOn`, `""`;
    - `MayGetOut(bool seatedHere, bool downed, bool holdOn = false) => seatedHere && !downed && !holdOn`.

    A remote "Get out" is an `InteractIntent` on `cart:0` that reaches `RailController.UseCart` through `MineCart.Interact`, so the one refusal there covers solo and remote at once; `ServerWorld` only prints a refused one on the test server.
  - `RailPropsDto.BridgeGone` (int, default 0) is in the prop fingerprint.
  - `--test-cavern[=bridge|brake|stall|runaway]` (`DebugTools.Cavern.cs`):
    - setup: the built-in campaign's floor 12 at lethality 3, solo; `rc.Restore(3)`, everyone boards, the cart departs on ride 4; every enemy is culled as it appears; `enc.DebugHoldRunaway(4)` except in `=runaway`;
    - it prints `[cavern]` lines, including `[cavern] timing: bridge call t=…, earliest take t=…`, and `[cavern] RESULT ok ({variant})` or `FAIL (…)`;
    - **every variant:** nobody falls but the train's riders, and there is no `ERROR`. After the build `rails.Party.Track.Track.Gaps` holds both the bridge's span and r5_jump's gap (printed `[cavern] party track bare over: 903.1..991.1, 1156.0..1161.0`); from Task 5, the enemy track's holds the broken bridge's gap and the drop.
    - **=bridge** (the default):
      - nobody presses anything;
      - the call comes within 0.1 s of the collapse's start, ≥ 1.5 s after the rear passed `from`;
      - the front never comes within 2 m of the rear;
      - while `HoldsRiders`, the seated lead's prompt is `""` and a `MayGetOut` press is refused;
      - every party cell is gone within 6 s after the arrival at stop 4 (`GoneBridges` holds them all, and their `Deck_` shapes are disabled); the flow prints how long before or after the arrival the last cell went;
      - a following `rc.Restore(3)` stands it whole (`GoneBridges` empty, shapes enabled).
    - **=brake:**
      - the lead brakes (`ForcedMoveInput = (0, 1)`) from 1 s after the collapse starts;
      - `BridgeBrake` is heard before the take;
      - the take comes ≥ 1.5 s after the collapse's call;
      - `rc.Falls` rises by 1, with every rider fallen within 1 s;
      - the wipe rises at stop 3 with the bridge whole;
      - the next departure, unbraked, reaches stop 4.
    - **=stall:**
      - `rc.DebugStall = true` with the rear 6 m on the bridge after the collapse starts, nobody braking;
      - the hold line appears, there is no take, the watchdog frees the cart, and it reaches stop 4.
    - **=runaway:**
      - the runaway is real, the split left straight, and the lead pumps;
      - the runaway goes down with the bridge with its front within `[from, from + 8]`, its body's `Rammed` and `Plunged` both false as it went;
      - `RunawayRams` is 0.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheCollapse_StartsAGraceAfterTheTrainIsWhollyOn_AtFourFifthsOfItsCruise()
{
    Assert.False(BridgeRules.Running(rearOnAt: double.NaN, now: 50));
    Assert.False(BridgeRules.Running(10.0, 11.49));
    Assert.True(BridgeRules.Running(10.0, 11.5));
    Assert.Equal(4.8f, BridgeRules.Pace(0), 3);
    Assert.Equal(4.174f, BridgeRules.Pace(3), 3);
}

[Fact] public void Braked_TheFrontRunsOnToTheTrain()
{
    Assert.Equal(905.1f, BridgeRules.Advance(frontS: 903.1f, toS: 991.1f, pace: 4f, dt: 0.5f, trainRearS: 908.1f, braked: true), 3);
    Assert.Equal(991.1f, BridgeRules.Advance(990.1f, 991.1f, 4f, 1f, 1001.1f, true), 3);
    Assert.True(BridgeRules.Catches(rolling: true, braked: true, frontS: 908.1f, trainRearS: 908f));
    Assert.True(BridgeRules.Braked(lastBrakeAt: 10.0, now: 12.0));
    Assert.False(BridgeRules.Braked(10.0, 12.1));
    Assert.False(BridgeRules.Braked(double.NaN, 5.0));
}

[Fact] public void WithNoBrake_TheFrontHoldsShortOfTheTrain()
{
    Assert.Equal(905.1f, BridgeRules.Advance(903.1f, 991.1f, 4f, 0.5f, trainRearS: 918.1f, braked: false), 3);
    Assert.Equal(906.1f, BridgeRules.Advance(905.1f, 991.1f, 4f, 0.5f, trainRearS: 908.1f, braked: false), 3); // held 2 m short
    Assert.Equal(906.6f, BridgeRules.Advance(906.6f, 991.1f, 4f, 0.5f, trainRearS: 908.1f, braked: false), 3); // never back
    Assert.False(BridgeRules.Catches(true, braked: false, frontS: 909.1f, trainRearS: 908.1f));
    Assert.False(BridgeRules.Catches(rolling: false, braked: true, frontS: 909.1f, trainRearS: 908.1f));
}

[Fact] public void CellsGo_AsTheFrontPassesTheirFarEdge_AndTheRunawayBehindGoesDown()
{
    var far = new[] { 907.1f, 911.1f, 915.1f };
    Assert.Equal(1, BridgeRules.GoneCells(911.0f, far));
    Assert.Equal(2, BridgeRules.GoneCells(911.1f, far));
    Assert.True(BridgeRules.TakesRunaway(runawayFrontS: 904.1f, fromS: 903.1f, frontS: 907.1f));
    Assert.False(BridgeRules.TakesRunaway(903.0f, 903.1f, 907.1f));
    Assert.False(BridgeRules.TakesRunaway(908.1f, 903.1f, 907.1f));
    Assert.False(BridgeRules.TakesRunaway(904.1f, 903.1f, float.NaN));
}

[Fact] public void OnTheBridge_GetOutIsHeld_AndTheBrakeIsCalled()
{
    Assert.True(BridgeRules.HoldsRiders(trainOnBridge: true, v: 1f));
    Assert.False(BridgeRules.HoldsRiders(true, 0.9f));
    Assert.False(RideInputRules.MayGetOut(seatedHere: true, downed: false, holdOn: true));
    Assert.Equal("", RideInputRules.BoardPrompt(true, false, false, true, true, holdOn: true));
    Assert.Equal("Get out", RideInputRules.BoardPrompt(true, false, false, true, true));
    Assert.True(BridgeRules.CallsBrake(braking: true, frontS: 900f, trainRearS: 911.9f));
    Assert.False(BridgeRules.CallsBrake(true, 900f, 912.1f));
}
```

  In `RideBarksTests`, the triggers list gains the three new ones, and each has its banks (3 per roster name, 2 per class, 3 generic).

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~BridgeRulesTests|FullyQualifiedName~RideBarksTests"`. Expected: a compile error (`BridgeRules`, the new triggers).
- [ ] **Step 3: Implement** the Interfaces above.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then, through `scripts/flows.sh`: `--test-cavern`, `=brake`, `=stall`, `=runaway`; `--test-runaway=tail` and `=ram`; `--test-split` and `=early`, `=hold`, `=stall`, `=lone`; `--test-track-view`. Each must PASS; the bridge's build line reads `[rails] bridge r4_bridge: party S=903.1..991.1, 22 deck cell(s), over the crevasse True (live)`. Then the `--track-depth` check (Task 1 Step 4).
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/BridgeRules.cs src/Dungeon/Rails/SetPieces/CavernBridge.cs src/Dungeon/Rails/RailEncounterController.Bridge.cs src/Core/DebugTools.Cavern.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Runaway.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.Net.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/Core/RideInputRules.cs src/Net/ServerWorld.cs src/Net/Core/PropCheck.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/Core/Voice/RideBarks.cs src/Adventure/Core/Voice/VoiceLines.cs src/Core/DebugTools.cs src/Core/DebugTools.Runaway.cs
git commit -m "feat(rails): the cavern's bridge collapses behind the train from a grace after it is wholly on, at 0.8 of its cruise; it goes under a train only while a player brakes, else holds 2 m short; called, no Get out on it at speed; the runaway goes down with it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 4: The leap across and back (a seat change)

**Files:**
- Modify: `src/Dungeon/Rails/Core/LeapRules.cs` (the offer rules)
- Modify: `src/Dungeon/Rails/SlagCart.cs` (the seat book, `DeckSeat`, `IInteractable`, `Live`, the shoe guard)
- Create: `src/Dungeon/Rails/RailEncounterController.HeroLeap.cs`
- Modify: `src/Dungeon/Rails/RailEncounterController.cs` (`Free` and the ram set leapers down; `SpawnSlagCart` tells `RailNet`), `RailEncounterController.SetPieces.cs` (`case "leap"` build: the stretch and the drop's S)
- Modify: `src/Dungeon/Rails/RailController.cs` (the party cart's `PromptFor`/`OnUse` offer "Leap back" first; the aboard audit), `src/Party/Adventurer.cs` (`TickView`: the leap offer before the lever window)
- Modify: `src/Dungeon/Rails/RailNet.Seats.cs` (hear slag carts, `SendsUnseat`, slag ids in `CartById`, the catch-up, the freed copy), `src/Dungeon/Rails/RailNet.Carts.cs` (`Free` sets a seated rider down)
- Modify: `src/Net/Core/NetRefs.cs` (`SlagKind`, `Slag(int)`), `src/Net/NetRefResolver.cs`, `src/Net/ServerWorld.cs` (the leap's reach), `src/Net/ServerWorld.Rails.cs` (the aboard audit: `RideInputOf`)
- Modify: `src/Core/DebugTools.Cavern.cs` (`=leap`, `=wreck`)
- Test: `tests/Dungeon/Rails/LeapRulesTests.cs`

**Interfaces:**
- Consumes: Task 2's `LeapIn`, `MinStretch` and `DropSpan`; Task 3's `HoldsRiders` and flow scaffold.
- Produces:
  - `LeapRules` (engine-free):
    - constants `Reach = 2.6f`, `AlongReach = 1.2f`, `AcceptSlack = 1f`, `BackCloses = 3f`, `MaxLeapers = 2`, `AcrossPrompt = "Leap across"`, `BackPrompt = "Leap back"`;
    - `bool InReach(float lateral, float along, float slack = 0f) => MathF.Abs(lateral) - 2f * CartRules.HalfWidth <= Reach + slack && MathF.Abs(along) <= AlongReach + slack`;
    - `bool AcrossOffered(bool human, bool up, bool seatedInParty, bool rolling, bool stretchLive, bool called, bool inReach, int leapersAboard)`: all true and not `called`, and `leapersAboard < MaxLeapers`;
    - `bool BackOffered(bool up, bool onThatSlag, bool inReach, bool seatFree, float slagFrontS, float dropS)`: all, and `slagFrontS < dropS − BackCloses`;
    - `bool BackAccepted(bool up, bool onThatSlag, bool inReachWithSlack, bool seatFree, float slagFrontS, float dropS)`: all, and `slagFrontS < dropS`;
    - `string? Dead(float stretchFromS, float dropS, float cavernFromS)`: "the stretch starts short of the cavern" when `stretchFromS < cavernFromS`; "the stretch is shorter than 30 m" when `dropS − stretchFromS < MinStretch`; else null;
    - `bool SendsUnseat(bool seated, bool seatedElsewhereNow) => seated || !seatedElsewhereNow`.
  - `SlagCart`:
    - `public static IReadOnlyList<SlagCart> Live` (every role; joined in `_EnterTree`, left in `_ExitTree`);
    - `public Transform3D DeckSeat(int seat)` per Ruling 13, and `SeatTransform(seat) => DeckSeat(seat)`;
    - `Admit` adds to `Riders` and raises; `Release` removes the rider from `Riders` and `Seats` and raises;
    - `public int Leapers` counts the `Adventurer`s seated;
    - `IInteractable`: `Prompt` is `AcrossPrompt` when `RailEncounterController.Instance.LeapOfferFor(local) == this`, else `""`; `Interact(party, actor) => controller.LeapAcross(actor, this)`;
    - `NetRef => NetRefs.Slag(CartId)`;
    - its `BrakeShoe` ignores every hit while `Leapers > 0`.
  - `RailEncounterController`:
    - `public float LeapFromS, LeapDropS` (party S of the drop's near edge), `internal float LeapDropEnemyS, LeapDropFarEnemyS` (NaN with no live leap), `public bool LeapCalled` (false until Task 5);
    - `internal int Leaps, LeapsBack`;
    - the build line: `[rails] leap {id}: stretch party S={from:0.0}..{drop:0.0}, the slag line's drop at enemy S={near:0.0}..{far:0.0} over the crevasse {bool} (live | dead: …)`;
    - `public IInteractable? LeapOfferFor(Adventurer hero)` (every role; the client reads its copies, through `SlagCart.Live`, and its own cart). It returns the nearest-along `SlagCart` offered across, the party's `MineCart` offered back, or null;
    - `public bool LeapAccepts(Adventurer body, Node target)`, server only, with `AcceptSlack` and `BackAccepted`;
    - `public bool LeapAcross(Adventurer hero, SlagCart cart)`:
      - it books the first free deck seat and calls `hero.SitIn(cart, seat)`;
      - it logs "{name} leaps onto the slag cart!" and prints `[rails] leap: {name} across onto slag cart {cartId} ({enc}) seat {k} at party S=… (gap {g:0.00} m, along {a:0.00} m)`;
    - `public bool LeapBack(Adventurer hero, string? hauledBy = null)`:
      - it books a free hero seat with `CartSeats.Take(…, isHero: true)` and calls `SitIn(_rails.Cart, seat)`;
      - it logs "{name} leaps back into the cart!" and prints `[rails] leap: {name} back into the cart seat {k} at …`;
    - `private void SetDownLeapers(SlagCart cart, string why)` sets each down at `CrevasseRules.NearestSetDown`'s cell (a quiet unseat, as `RailController.UnseatQuietly`) and prints `[rails] leap: {name} set down off slag cart {id} ({why}) at cell …`. `Free` and the ram call it first.
  - The party's cart: `PromptFor` returns `BackPrompt` and `OnUse` calls `LeapBack` when `LeapOfferFor(hero)` is that cart. `Adventurer.TickView` sets `LookingAt = LeapOfferFor(this)` before the lever window, whatever the aim.
  - **The aboard audit** (Ruling 14). `grep -rn "\.Seated\b" src --include=*.cs | grep -v "src/Core/DebugTools"`. Each hit that judges the party's cart reads `Cart == _rails.Cart` (or that cart); each that means "in any seat" is left and listed as such. `ServerWorld.Rails.cs`'s `RideInputOf` is in the first group; say which group `ServerWorld.cs`'s `LeverReach(body.Seated, …)` call is in. The report lists each hit and what it was given.
  - The wire (Ruling 20):
    - `NetRefs.SlagKind = "slag"` and `NetRefs.Slag(int cartId)`; `NetRefResolver` resolves it through `SlagCart.Live` by `CartId`;
    - `ServerWorld` lets `node is SlagCart` or the party's `MineCart` past the reach check when `LeapAccepts(body, node)`, and prints `[net-server] interact {ref} from {name}: leap {accepted|refused}`;
    - `RailNet` hears each slag cart's `RiderChanged` under its `CartId` (`HearSlagCart`, called from `SpawnSlagCart`). `FlushSeats` drops an unseat when `!LeapRules.SendsUnseat(false, rider.Cart != null)`;
    - `CartById` returns `_carts[id].Slag`; `SendRailCatchUp` sends the seated riders of every live slag cart after the cart state;
    - a client's `Free` of a slag copy sets down, where it stands, any rider seated on it. `OnSeat`'s unseat sets a rider down at the landing when the cart it names is gone and the rider still rides a freed cart.
  - `--test-cavern=leap|wreck`. The runaway is held, and every enemy is culled except `r4_slag`'s riders, which the flow makes harmless and keeps targetable.
    - **=leap:**
      - "Leap across" is offered within 2 s of the stretch's start, with `r4_slag`'s along offset ≤ 1.2 m (printed);
      - every companion's `LeapOfferFor` is null for the whole stretch;
      - the lead presses it (`LookingAt.Interact`): `me.Cart` is the slag cart, `slag.Seats.OccupantOf(0) == me.Index`, `rc.Aboard()` excludes the lead, and the train's inputs read no pump from it;
      - a seated blow from the deck lowers a rider's HP;
      - 3 s after the leap across, "Leap back" is pressed and the lead sits in a party hero seat;
      - stop 4 is reached with `Leaps == LeapsBack == 1`.
    - **=wreck:**
      - the lead leaps across, then `rc.DebugWreck()`;
      - the lead is up, not fallen, not seated, on a cell where `SetDownOk` holds;
      - `CollapseS` has not moved 1 s later.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Reach_IsTheRimGapAndTheDecksOverlapping()
{
    Assert.True(LeapRules.InReach(lateral: 4f, along: 0.5f));   // twin lanes 4 m apart: a 2.5 m gap
    Assert.False(LeapRules.InReach(4.2f, 0f));
    Assert.False(LeapRules.InReach(4f, 1.3f));
    Assert.True(LeapRules.InReach(4f, 2.1f, slack: LeapRules.AcceptSlack));
    Assert.False(LeapRules.InReach(4f, 2.3f, LeapRules.AcceptSlack));
}

[Fact] public void OnlyAHumanUpInTheRollingCart_IsOfferedTheLeap_TillTheCall()
{
    Assert.True(LeapRules.AcrossOffered(human: true, up: true, seatedInParty: true, rolling: true, stretchLive: true, called: false, inReach: true, leapersAboard: 0));
    Assert.False(LeapRules.AcrossOffered(human: false, true, true, true, true, false, true, 0));
    Assert.False(LeapRules.AcrossOffered(true, true, true, true, true, called: true, true, 0));
    Assert.False(LeapRules.AcrossOffered(true, true, true, true, true, false, true, leapersAboard: 2));
    Assert.False(LeapRules.AcrossOffered(true, up: false, true, true, true, false, true, 0));
}

[Fact] public void TheServerTakesALeapBack_UntilTheDropsEdge()
{
    Assert.True(LeapRules.BackOffered(up: true, onThatSlag: true, inReach: true, seatFree: true, slagFrontS: 972.0f, dropS: 975.1f));
    Assert.False(LeapRules.BackOffered(true, true, true, true, 972.2f, 975.1f));
    Assert.True(LeapRules.BackAccepted(true, true, true, true, slagFrontS: 975.0f, dropS: 975.1f));
    Assert.False(LeapRules.BackAccepted(true, true, true, true, 975.1f, 975.1f));
    Assert.False(LeapRules.BackAccepted(true, true, true, seatFree: false, 970f, 975.1f));
}

[Fact] public void AShortOrMisplacedStretch_IsDead()
{
    Assert.Null(LeapRules.Dead(stretchFromS: 913.1f, dropS: 975.1f, cavernFromS: 903.1f));
    Assert.Equal("the stretch starts short of the cavern", LeapRules.Dead(900f, 975.1f, 903.1f));
    Assert.Equal("the stretch is shorter than 30 m", LeapRules.Dead(950f, 975.1f, 903.1f));
}

[Fact] public void AnUnseatIntoAnotherCart_IsNotSent()
{
    Assert.False(LeapRules.SendsUnseat(seated: false, seatedElsewhereNow: true));
    Assert.True(LeapRules.SendsUnseat(false, false));
    Assert.True(LeapRules.SendsUnseat(true, true));
}
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~LeapRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces above, the aboard audit included.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then, one at a time:
  - `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-cavern=leap 2>&1 | grep -E "\[rails\] leap|\[cavern\] RESULT"`. Expected: the leap's build line `(live)`, one `across` and one `back`, and `RESULT ok (leap)`.
  - `=wreck` prints `RESULT ok (wreck)`, and `=bridge` still prints `ok`.
  - `--test-chase --quit-after=750` prints `[chase] RESULT ok`: ride 2's slag carts are unchanged.
  - Plain `scripts/net_test.sh` (alone, lock absent) prints `NET TEST: ok`.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/LeapRules.cs src/Dungeon/Rails/SlagCart.cs src/Dungeon/Rails/RailEncounterController.HeroLeap.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailController.cs src/Party/Adventurer.cs src/Dungeon/Rails/RailNet.Seats.cs src/Dungeon/Rails/RailNet.Carts.cs src/Net/Core/NetRefs.cs src/Net/NetRefResolver.cs src/Net/ServerWorld.cs src/Net/ServerWorld.Rails.cs src/Core/DebugTools.Cavern.cs
git commit -m "feat(rails): in the cavern a human seated in the rolling cart can leap across onto a slag cart alongside (a seat change onto its deck) and back; a leaper is not aboard the party's cart; a slag cart going any other way sets its leapers down; the seat change goes as a SeatMsg with the slag cart's id

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 5: The slag line drops into the crevasse

**Files:**
- Modify: `src/Dungeon/Rails/Core/LeapRules.cs` (`CallAhead`, `Calls`, `FallsWithIt`, `TrafficStartS`)
- Create: `src/Dungeon/Rails/SetPieces/SlagDrop.cs`
- Modify: `src/Dungeon/Rails/RailEncounterController.HeroLeap.cs` (`TickHeroLeap`, `DropsOverTheLeap`), `RailEncounterController.cs` (the per-cart drop check after `DropsThrough`), `RailEncounterController.SetPieces.cs` (`case "leap"` tick; the drop stands at arming and on restore), `RailEncounterController.Traffic.cs` (the start S), `RailEncounterController.Net.cs` (`leap:` props; the client's plunge read)
- Modify: `src/Dungeon/Rails/RailNet.Carts.cs` (a copy gone at the drop plays the quiet plunge), `src/Net/Core/PropCheck.cs` (`RailPropsDto.SlagLineDropped`, the fingerprint)
- Modify: `src/Core/DebugTools.Cavern.cs` (`=leap` completed, `=stay`, `=lone`)
- Test: `tests/Dungeon/Rails/LeapRulesTests.cs`

**Interfaces:**
- Consumes: Task 4's leap piece, `LeapBack(hero, hauledBy)` and `SetDownLeapers`; Task 1's `SetGone`, `CutMore` and `LaySpan`; Task 3's `BarkTrigger.SlagDrop`.
- Produces:
  - `LeapRules`:
    - `CallAhead = 25f`;
    - `bool Calls(float partyFrontS, float dropS) => partyFrontS >= dropS - CallAhead`;
    - `bool FallsWithIt(bool human, bool up) => human && up`;
    - `float TrafficStartS(float baseStartS, float partyS, float dropS, float dropFarEnemyS, bool dropped)`: `dropped && partyS > dropS` gives `max(baseStartS, dropFarEnemyS + SwitchBackRules.TrafficPastGap)`; else `baseStartS`.
  - `SlagDrop : Node3D, ISoundPoint`:
    - `Build(TrackSampler enemy, float nearS, float farS, int innerSide)` lays the drop cells' rails, one mesh per cell, each with `RailTrack.LaySpan` of the enemy lane's own track, and a stub post with a red `OmniLight3D` (unlit until it drops, the broken bridge's energy and range);
    - `void GiveWay(Node3D? root)` hides the cells' rails, calls `SetGone(twin, true)` on both cells, tumbles timbers, lights the lamp and plays `timber_collapse`;
    - `void Stand()`;
    - `bool Dropped`;
    - `NetRef = "leap:{piece}"`. It tells "drops" and "stands".
  - The live leap cuts the slag track bare over the drop with `_enemy.Track.CutMore([(near, far)])`, keeping the ride-3 gap.
  - `TickHeroLeap`, server and solo, while ride 4 is armed and not yet `LeapCalled`. At `Calls` it does these once per arming:
    - `GiveWay`;
    - `LeapCalled = true`;
    - each live slag cart of the ride rings its bell (`cart_bell`);
    - `RailCalls.Say(party, _rails.Cart, BarkTrigger.SlagDrop, "The slag line's going — leap back!")`;
    - it prints `[rails] t=… leap {id}: the slag line drops at enemy S={near:0.0}..{far:0.0}; called by {who}; {n} hero(es) on a slag deck`.
  - `private bool DropsOverTheLeap(SlagCart cart)`, with `LeapCalled` and the cart's front ≥ `LeapDropEnemyS`:
    - each seated hero is checked with `FallsWithIt(human-controlled, !Downed)`. One who passes goes to `CrevasseWatch.Take`; any other gets `LeapBack(hero, hauledBy: the first seated companion's name or null)`, logged per Ruling 17;
    - each creature aboard goes to `CrevasseWatch.Take`;
    - the cart is freed quietly, sent gone with `Derailed` on the wire;
    - the log says "The slag cart goes over into the crevasse!" (Ruling 16), and the server prints `[rails] t=… slag cart {id} goes over the drop at enemy S=…: {r} rider(s), {f} hero(es) fell, {h} hauled back`.
  - On a client, a slag copy gone `Derailed` with `SwitchBackRules.PlungedAt(goneS, LeapDropEnemyS)` plays the quiet plunge and prints `[net] ride: slag cart {id} went over the drop here`.
  - The drop stands again (`Stand()`, `LeapCalled = false`) at ride 4's arming, on a `Restore` and at a spare's takeover (Rulings 5 and 18). Traffic's start S goes through `LeapRules.TrafficStartS`.
  - `RailPropsDto.SlagLineDropped` (bool) is in the prop fingerprint. The catch-up applies it with `GiveWay(null)`.
  - `--test-cavern`:
    - **=leap** (completed): the lead leaps back on its first frame after the call (not on Task 4's 3 s timer). Then:
      - `[cavern] timing: drop call t=…, drop t=…` holds a gap of ≥ 1.5 s;
      - the slag cart goes over with `0 hero(es) fell`, its riders dead;
      - the drop's `Deck_` shapes are disabled and `GoneBridges` holds both twins;
      - stop 4 is reached.
    - **=stay:**
      - the lead leaps across and stays;
      - the call is heard ≥ 1.5 s before the drop;
      - the lead falls with it (`Fallen`) and nothing of the train falls (`rc.Falls` unchanged);
      - the party's cart reaches stop 4 and the lead is back there within 1 s at 25 % HP (`FallenReturn`).
    - **=lone:**
      - possession is off; the lead leaps across and is downed on the deck (`Mangled`, HP 0) before the call;
      - at the drop the lead is hauled back into a party hero seat, not fallen, and the hauler's line is logged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheDrop_IsCalledTwentyFiveMetresShort()
{
    Assert.True(LeapRules.Calls(partyFrontS: 950.1f, dropS: 975.1f));
    Assert.False(LeapRules.Calls(950.0f, 975.1f));
    Assert.Equal(25f, LeapRules.CallAhead);
}

[Fact] public void OnlyAnAbleHuman_FallsWithIt()
{
    Assert.True(LeapRules.FallsWithIt(human: true, up: true));
    Assert.False(LeapRules.FallsWithIt(true, up: false));
    Assert.False(LeapRules.FallsWithIt(human: false, true));
}

[Fact] public void Traffic_StartsPastADroppedSlagLine()
{
    Assert.Equal(980f + SwitchBackRules.TrafficPastGap, LeapRules.TrafficStartS(baseStartS: 720f, partyS: 1022.5f, dropS: 975.1f, dropFarEnemyS: 980f, dropped: true));
    Assert.Equal(720f, LeapRules.TrafficStartS(720f, 1022.5f, 975.1f, 980f, dropped: false));
    Assert.Equal(720f, LeapRules.TrafficStartS(720f, partyS: 960f, 975.1f, 980f, true));
}
```

- [ ] **Step 2: Run them and see them fail.** Run `--filter "FullyQualifiedName~LeapRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the Interfaces above.
- [ ] **Step 4: Run them.** Run the filter, then the full suite. Expected: PASS. Then, one at a time:
  - `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-cavern=leap 2>&1 | grep -E "\[rails\] (leap|t=.*slag cart)|\[cavern\] (timing|RESULT)"`. Expected: the drop line, `0 hero(es) fell`, and `RESULT ok (leap)`.
  - `=stay`, `=lone`, `=wreck`, `=bridge` and `=brake` each print `ok`.
  - `--test-switch-back` prints `[switchback] RESULT ok (thrown)`: ride 3's traffic and break are unchanged.
  - The `--track-depth` check (Task 1 Step 4), now with the `piece r4_leap ride=4 kind=leap S=913.1` line too.
- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/Rails/Core/LeapRules.cs src/Dungeon/Rails/SetPieces/SlagDrop.cs src/Dungeon/Rails/RailEncounterController.HeroLeap.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.SetPieces.cs src/Dungeon/Rails/RailEncounterController.Traffic.cs src/Dungeon/Rails/RailEncounterController.Net.cs src/Dungeon/Rails/RailNet.Carts.cs src/Net/Core/PropCheck.cs src/Core/DebugTools.Cavern.cs
git commit -m "feat(rails): after the leap stretch the slag line's deck gives way, called 25 m short with the bell and a red lamp; the slag cart goes over with its riders and any able human still aboard; a downed or AI-run leaper is hauled back

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 6: Seen from the seat (the legibility gate)

**Files:**
- Modify: `src/Core/DebugTools.Cavern.cs` (sight rays in every variant; `--screenshot=PATH` A/B shots and pixel checks)
- Modify, only within Ruling 23's allowance: `src/Dungeon/Rails/SetPieces/CavernBridge.cs`, `src/Dungeon/Rails/SetPieces/SlagDrop.cs`

**Interfaces:**
- Produces, in every `--test-cavern` run, headless included. These are layer-1 rays from the seated eye, and a `blocked` result fails the flow with its own line:
  - 1 s after the collapse starts, from the lead's eye to the collapse front's point on the party track + 0.5 m: `[cavern] sight: bridge front {clear|blocked by X} at {d:0.0} m`;
  - on the first frame "Leap across" is offered, from the lead's eye to `DeckSeat(0)`: `[cavern] sight: slag cart {clear|blocked by X} at {d:0.0} m`;
  - at the drop's call, from the leaper's eye (`=leap`, `=stay`) to the drop's near edge + 0.3 m: `[cavern] sight: drop {clear|blocked by X} at {d:0.0} m`.
  - Each ray has a control that must be blocked near its end (the same eye to a point 0.3 m under the deck at the target), printed `[cavern] sight: {name} control blocked by X at {d} m`. A variant whose sight never ran fails with its own line.
- With `--screenshot=PATH` (windowed, `=leap` only), the HUD is hidden and the shots are A/B pairs. Each pair is taken held, as plan 12's gate is since 5fc41d3: the party cart pinned (S and V, each tick) from the place until both twins are saved, the twins within 0.05 m of S and adjacent in the queue, 30 frames drawn before each is saved. The places: `behind` with the front at `from + 30` (the collapse front then holds 2 m behind the rear); `side` at the S of the first offered frame; `drop` at the S of the call (the slag cart paces the pinned cart, so it holds too). A hold lasts about a second, well inside the watchdog's 10 s.
  - `PATH_behind`: the lead's view turned 180°, held at `behind`. `PATH_behind_standing` is its held twin with the gone cells' looks shown again.
  - `PATH_side`: the lead's view turned toward the slag cart, held at `side`. `PATH_side_nocart` is its held twin with the slag cart and its riders hidden.
  - `PATH_drop`: the leaper's view forward, held at `drop`. `PATH_drop_standing` is its held twin with the drop's looks shown again and its lamp off.
- It prints `[cavern] shots: behind diff {a} px; side diff {b} px; drop diff {c} px, drop ember {e} px`:
  - a diff counts the pixels whose luminance differs by more than 0.1 over the scene area (x 20–80 %, y 16–80 %);
  - ember counts orange pixels in `PATH_drop`'s scene area (r > 0.7, 0.3 < g < 0.65, b < 0.35);
  - it passes with a, b and c ≥ 400 and e ≥ 20.

- [ ] **Step 1: Run the gate headless.** `dotnet build DungeonCrawler.csproj && timeout 300 godot --headless --path . -- --test-cavern=leap 2>&1 | grep -E "\[cavern\] (sight|RESULT)"`. Expected: every ray clear, and `RESULT ok (leap)`. Also run `=bridge` and `=stay` and check their rays.
- [ ] **Step 2: Run it windowed, once.** `timeout 300 godot --path . -- --test-cavern=leap --screenshot=/tmp/claude-1000/cavern.png 2>&1 | grep -E "\[cavern\] (shots|RESULT)"`. Expected: the shots line passes. Open the six PNGs (the Read tool shows them) and judge four things:
  - the gone deck reads behind the train;
  - the slag cart reads alongside, within a leap;
  - the drop and its red lamp read ahead from the slag deck;
  - in `PATH_behind_standing` the rails run unbroken across both ends of the bridge's span, with no step and no doubled rail (the probe reads the curve, never the meshes).
- [ ] **Step 3: Run plan 12's luminance gate once, windowed:** `timeout 400 godot --path . -- --test-split=after --screenshot=/tmp/claude-1000/split.png 2>&1 | grep -E "\[split\] (shots|RESULT)"`. Expected: `RESULT ok (after)` with the lamp-hidden ratio ≤ 0.6 (0.42 at d175fb7). `--test-track-view` runs headless in Task 7's sweep.
- [ ] **Step 4: On any failure, stop and report** the numbers and the PNG paths. A dressing fix allowed by Ruling 23 may be tried once, followed by one more windowed run. Otherwise make no change.
- [ ] **Step 5: Commit** (the flow's checks, plus any allowed dressing)

```bash
git add src/Core/DebugTools.Cavern.cs
git commit -m "test(rails): the cavern's legibility gate: sight rays from the seat to the bridge's falling edge, the slag cart alongside and the drop; windowed A/B shots of the gone deck behind, the cart beside and the drop ahead

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(Add `CavernBridge.cs` or `SlagDrop.cs` to the `git add` only if Ruling 23 allowed a change to it.)

### Task 7: Co-op proof, docs and the full sweep

**Files:**
- Modify: `src/Net/NetTestFlow.cs` (ride 4's cavern on both clients), `scripts/net_test.sh` (the new checks; its header)
- Modify: `docs/development.md`, in these sections:
  - The five rides: ride 4's row and its cavern paragraph;
  - Crevasses: gone decks;
  - Riding together: Seats and poses (slag ids), Leaps and boarders (the player's leap), Props (`bridge:`, `leap:`, the two `RailPropsDto` fields);
  - The watchdog: the stall guard;
  - The RIDE net test;
  - Known gaps.

**Interfaces:**
- Produces, in the RIDE pass on ride 4, after the split:
  - **The bridge.** The server prints `bridge r4_bridge goes`. Each client prints `[net] ride: the bridge behind us gone {n}/{N} cells here` when its count reaches N, within 10 s of its arrival at stop 4, with N matching the server's build line. No `the train falls` appears.
  - **The first client's leap across.** On its first frame offered "Leap across" (its own prompt), it sends `InteractIntent(NetRefs.Slag(id))`. The server prints `leap: {name} across onto slag cart {id}`, and both clients print `[net] ride: {name} rides slag cart {id} seat {k} here` (from the `SeatMsg`).
  - **The joiner's leap across.** The joiner leaps across too (the second deck seat).
  - **The joiner's leap back.** It presses "Leap back" (`cart:0`) on its last offered frame, with its slag copy within 0.5 m of `LeapDropS − BackCloses`. The server's `leap … accepted` and `back into the cart` follow, never a fall.
  - **The first client's leap back.** It presses on its first frame after the drop's call.
  - **The drop.** The server prints `slag cart {id} goes over the drop … 0 hero(es) fell, 0 hauled back`. Both clients print `slag cart {id} went over the drop here`, and none still shows a rider seated on a freed copy.
  - **The arrival.** The train arrives at stop 4 with both players' heroes seated in the party's cart (`me.Cart == rc.Cart` on each).
  - **After the wipe's rise.** The server prints the bridge and the drop standing again; `[net-server] ride: wipe-rise watch:` still reads ok; the joiner's props hash as the server's after its rebuild (`BridgeGone` 0, `SlagLineDropped` false).
  - **The flow's own seat reads.** Each `.Seated` in `NetTestFlow` that means the party's cart reads `Cart == rc.Cart` (`FightBoarders` first: a client on the slag deck must not aim at the party cart's rim).
  - **No false ram.** No `[net] ride: the runaway rammed the train here` on either client (plan 12's check, kept).
  - Each check has its own `NET TEST: FAIL (…)` line.

- [ ] **Step 1: Add the checks** (Interfaces) to `NetTestFlow` and `net_test.sh`. The cavern's checks go after the split's and before the clock checks.
- [ ] **Step 2: Hand the RIDE pass to the controller.** Build, run plain `scripts/net_test.sh` (alone, lock absent) and expect `NET TEST: ok`; then stop and report that the RIDE checks are in. The controller runs the RIDE pass twice, alone, under the lock. Expected both times: `NET TEST: ok` with the new lines and `wipe-rise watch: ok`.
- [ ] **Step 3: Write the docs.** Say:
  - what each rule is and its number;
  - the flows and their RESULT lines;
  - the `[map] rail cavern:` format;
  - the calls' copy and the prompts;
  - that the RIDE gate needs the performance profile.

  Record Task 2's and Task 6's measurements. In ride 4's row of the ride table (`docs/development.md`, "The five rides"), replace the last cell ("plan 13: the collapsing bridge and the leap (beside `r4_slag`; …)") with "—", add the two pieces to the set-piece cell and give `r4_slag` its new At and Offset. Also fix: "Crevasses", the cavern bullet ("until plan 13 replaces it"); "The five rides", "caps the lists at 12 encounters and 20 set pieces" (the cap is `max(20, 5 × rides)`, 25 on floor 12, and the floor now has 21); the `--track-depth` line in the command list (the bridge's and the drop's rails are not `RailTrack`s: they ride the main lanes' probe, and the gate pins `nofloorDrop`); the paragraph under the ride table that says where the S values come from.
- [ ] **Step 4: Run the full sweep.** Run the full suite, then through `scripts/flows.sh` (longest first): `--test-chase --quit-after=750`; `--test-crew --quit-after=570`; `--test-cavern` and its seven other variants; `--test-runaway=tail`, `=ram`; `--test-split`, `=early`, `=hold`, `=stall`, `=lone`; `--test-switch-back` and its four other variants; `--test-crevasse`, `=trestle`, `=survivor`; `--test-relief`, `=rails`, `=refuse`, `=ahead`; `--test-barricade`; `--test-ride`; `--test-ride-edges`; `--test-cart-watchdog`; `--test-track-view` (34 flows). Then the `--track-depth` check (Task 1 Step 4), then plain `scripts/net_test.sh` alone. Every one must PASS with `errors=0` (or `NET TEST: ok`).
- [ ] **Step 5: Commit**

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh docs/development.md
git commit -m "test(net): the RIDE pass leaps two remote seats onto ride 4's slag cart and back (one on its last offered frame), sees the slag cart go over and the bridge fall behind on both screens; docs: the collapsing bridge, gone decks and the leap

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
