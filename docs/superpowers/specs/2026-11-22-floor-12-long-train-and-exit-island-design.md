# Floor 12: the long train and the exit island (design)

Status: ruled, ready to plan, 2026-11-22. An addendum to `2026-11-19-floor-12-full-circle-design.md`, which still binds everything
this one does not change, its anti-goals above all. Facts and file:line references come from
`.superpowers/sdd/shared/exit-island-recon.md` (sections A–G, at 303ef27), the builder's check
(`.superpowers/sdd/shared/design-builder-check.md`) and a fresh `--print-map` of the shipped floor; the S values are the shipped
layout's and move only where this design moves a piece.

**The owner said "build it", so the controller decided every open question.** Each one is marked "controller's call" so the owner
can change it later.

Every decision below is labelled. **Owner** means the owner decided it and it is binding. **Controller's call** means the controller
or this design decided it, and the owner may change it. The rulings on the owner's three open questions and on the builder's check
are listed at the end, with where each one lands.

## The owner's decisions (binding)

1. "The exit room becomes an island." Room 9 (the down stairs and stop 5) must be unreachable on foot from the rest of the mine:
   "The rails are needed to get to the exit." The owner chose to do it by changing floor 12's layout, and accepts that the
   `LayoutHash` moves, because nothing is shipped. "The walking ways in are blocked by cave-ins of fallen boulders." "Lava lies all
   round the island, crossed by one rail bridge with no walkway." (Where the lava lies is a controller's call, ruling R2.)
2. "A switch before ride 5's lava pit." The pit has no rails. A switch before it routes the train round the pit and back, like ride
   3's broken bridge, and leaving the switch unthrown sends the train into the pit. This is the owner's founding rule: "a contiguous
   track condition if you choose correctly". (Where on ride 5 the pit lies is a controller's call, ruling R1.)
3. "A parked cart at the end." If the party is walking the rails, a cart is sitting at the end before the exit bridge. A party that
   already has a cart smashes through it.
4. "A long train from the start." The train has its wagons from stop 0, so no cage wagons are coupled at stops. Freed prisoners just
   jump into the wagons.
5. "Fighting along the train." The heroes jump down the carts to knock off the baddies who board the wagons.
6. "A boarder left alone on a wagon throws the prisoners off", one at a time, with a warning call first so there is time to react.
   (The warning call was the controller's proposal, and the owner adopted it.)

## Intent and why

The chase works, but the end of it leaks. The exit room is 14 cells' walk from the start (recon A.3), so a party can stroll to the
stairs, and the line's climax does not have to be ridden. The train is short for most of the ride, because cage wagons join one
stop at a time, so the boarders have little to attack and the prisoners are only cargo. This change makes the rails the only way
out. It also makes the prisoners something the party defends, car by car, all the way to the end.

## Goals

- The exit is reachable only by rail. Every walk ends at fallen boulders, and the line ends at lava crossed by one rail bridge.
- Ride 5's pit is a deadly switch with a right branch and a wrong branch, with ride 3's warnings and its R-lone safety.
- Nobody is ever stranded. A party on foot, a player left behind (solo or online) and crew on foot each have a lift into the
  island, or a fair rule that ends the floor.
- A four-wagon train runs from stop 0. Freed prisoners run from a cage on the platform to the first empty seats. Heroes climb car to
  car to fight boarders on every ride. A boarder left alone with prisoners throws them off, after a warning.

## Non-goals

- Rails or islands on any other floor. Every new knob defaults off, and every other floor hashes as before.
- Reverse running, a train leaving the island, new key bindings, steering.
- Voice spend, dist builds, merging or pushing.
- A walking route into the island of any kind, secret ones included.

## Anti-goals (it fails even if it "works" when…)

- A death the player could not have seen coming. The pit switch warns by sight and by two calls. A throw comes only after a call
  and a glow, with time to reach the wagon. A parked-cart smash never kills anyone.
- Anyone stranded: a hero, crew member or escort outside the island with no way in and no rule that settles them. This includes
  any party, solo or online, unable to finish the floor.
- A body set down on the exit bridge, or a walker path drawn across it.
- The island leaking: any walk from outside to the down stairs.
- Shipped content lost: the vaults, the Oddfellows' camp, the Kiln Cages narrations, Grisel's two branches, and the golem with its
  spur.

## Constraints

- `Protocol.Version` stays 17. New state rides in existing messages (`SeatMsg`, `CartStateDto`, `PropEvt`, `LogMsg`, snapshots), as
  plans 10–13 did.
- No voice spend. Every new line is a subtitle-only stock line in `RideBarks` (or a log line), with `VoiceId` null.
- CC0 assets only. The new looks are procedural boxes on textures already in `assets/` (the trestle's `weathered_planks`, the
  rail gap's molten material, `Rubble.Collapse` for boulders) or the loafbrr MinesPack (CC0), as the cart is today.
- `--track-depth`'s five fields stay `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`. `nofloorDrop` gets new pins (B.2), and
  `--test-track-view` holds the probe to them.
- The RIDE co-op pass is the controller's to run (`.superpowers/sdd/shared/RIDE.lock`). The builder may change its flow, but does
  not run it.
- `tests/` is not in git. The builder updates the engine-free tests on disk, and the controller's review reads them there.
- Floor-12 dev saves made before this change are not supported: they embed the old campaign and plan (recon E, Saves). Any flow
  that loads one gets a fresh save.

## Approaches

**How the island is made.**
- **A. Change the layout (owner's choice).** The generator seals the halls and cuts the lava, so the grid itself says "no walk".
  Pathing, `FloorSolvability`, `CrevasseWatch`, the footing guard and every peer's build all agree, because they all read the
  grid. Cost: the hash moves, floor 12 is re-homed, and solvability needs a new proof.
- **B. Block at runtime.** Boulder props and kill volumes go over an unchanged layout. The hash is kept, but the grid still says
  the halls are open. Enemies and companions path into props, solvability proves a walk that no longer exists, and every peer
  must agree on runtime state. Rejected.

**Where in the generator (controller's call).** Skipping the ring edges at `ConnectRing` gives clean halls, but the exit room is not
known yet. It also reshuffles the safe room, traps, spawns and chests, which would mean re-homing all of `floor_12.json`. Instead,
a post-pass runs after the gorge and before the walkways, from no rng. Everything outside the sealed cells stays byte-identical,
the safe room stays where it is (room 12, off room 9), the reward vault stays vault 0 in room 10 (R3), and the line, every `StopS`
and every S stay unchanged.

**Where the pit goes (controller's call, ruling R1).** The pattern needs about 28 m of straight before the lip (the seated window
15 m, the lever's lead 5 m, a full blend across 8 m) and about 12 m after it for the fixed switch back's blend home. Today's jump
sits 6–8 m past a corner on a 30 m straight (x 55), and the 12.6 m parked train needs that same straight before the bridge. Ride
5's first straight (x 61, about S 1045–1089, 11 cells) holds the whole pattern. So the pit moves there and the hammers move to the
jump's old place. Ride 5 is still the last section, and the lava pit is still the one with no rails.

---

## Plan A: the long train, cages, fighting along the train, boarders

### A.1 Wagons from stop 0 (owner 4; details controller's call)

- **The train.** It is four wagons, W1..W4 (nearest first), coupled behind the party's cart at build and whenever the cart is
  restored whole (a restore, a spare's takeover, the lift's takeover). They are open tubs the cart's size, with no cage, so there
  is no roof. Each wagon has two crew seats (the bench seats 4–5, as today), one hero place (seat 0) and one boarder spot on its
  rim, at the cart's rim height. The train is 12.6 m long, its cars 2.6 m apart (recon G.4). Held at stop 0, its rear is at S 4.5,
  on room 1's platform.
- **Identity.** A wagon's number replaces its stop everywhere: `NetRefs.Wagon(k)` is `wagon:{k}`, `CartIds.WagonBase + k`,
  `CartStateDto.Train` holds wagon numbers, and so do the train hash and the RIDE books (`W{k}`). The cages keep the stop as their
  identity: `cage:{stop}` (`NetRefs.Cage`), and `OpenCages` stays a list of stops.
- **The save** (`ObjectiveStateDto`, every field nullable). `AboardCrew` is new: crew ids with their wagon and seat, in seat order,
  so a load seats each one where it sat. `LostWagons` is new and holds only between a loss and the next restore (a fall that takes
  the wagons with no spare left leaves the party without a cart, and a load in that stretch honours it); any restore of the cart
  whole couples four fresh wagons and clears it. `CoupledCages` and `LostCages` are retired and ignored on load.
  `RailArrival.Merge` takes the new fields from the arrival snapshot, so a wipe's rise brings the train back as it arrived.
- **Speed.** `CartInput.CrewWagons` counts coupled wagons, so drag sits at its cap of 3 from ride 1. The cruise is 6/1.15 = 5.22
  m/s unpumped, which the controller accepted. Every ride flow is re-run, and any measured window or time that moves is
  re-pinned from the measurement and written in the docs, never loosened silently. The ride-3 climb runs at about 2.61 m/s
  (recon G.5). `RampRideTests`' "3 crew wagons" row applies from ride 1. Ride 4's bridge `Pace` is 4.17 m/s as today, and its rear
  sits 2.6 m further back.
- **Boarder spots.** Hounds leap onto any coupled wagon within 6 m alongside, not only crewed ones. Each wagon's rim spot is at rim
  height, like the cart's, so the low beams clear wagon boarders exactly as they clear rim boarders today; the only thing that
  ever clipped a beam was the 2.57 m cage roof, and it is gone. `--test-chase` and `--test-split` check that `r1_beam` and
  `r4_beam` clear every seated head in the train.
- **After a fall that loses the wagons** (`TakeTheTrain`), a spare that takes over brings four fresh empty wagons, so later
  prisoners have seats whenever the party has a cart. The crew who fell are lost as today. After a plain wreck, the spare keeps
  the wagons and their crew, as today (but see B.2: never past the exit bridge's start).
- **Ride 4's runaway, the chaser and the relief** already aim at the train's rear (recon G.3). The trestle keeps judging the cart
  only (`Party.BackS`, `Kiln.cs:615`), so a wagon over a fallen trestle rides on the solid lane floor under it. That is the
  controller's call; judging the train's rear would make an unpumped four-wagon train fall there.

### A.2 Cages on the platforms (controller's call)

- **The cage.** Each intermediate stop's `CageWagon` becomes a stationary `PlatformCage`, an iron cage (`CageHeight` 1.95 m) on the
  same siding cell (`PickSiding`, `BlockCell`, `ReserveSiding`), with no wheels or track. Two crew stand in it.
- **Opening it.** It opens the same four ways (`UseWagon` becomes `UseCage`): the orc's keys, Grisel within 8 m or on the bench, an
  iron key, or a pick. The gates, the cage hold (`CageHoldRules`), "leave the prisoners behind" and condemned Grisel's body (beside
  cage 1) are unchanged.
- **Boarding.** Freed crew run to the train and climb into the first empty crew seats, W1 seat 4 first, then W1 seat 5, W2 seat 4,
  and so on. So stop 2's crew sit in W1 if stop 1's were left. The countdown does not end while a freed crew member of this stop
  is on the way to a seat. After `BoardMax` (8 s, a new constant in `CartSeats` beside `Boarding.Countdown` 5 s and `MinWait`
  2 s, `CartSeats.cs:76`, `:78`), anyone still within 25 m is seated at once, as companions hop in; anyone farther stays on foot.
- **Crew on foot.** This reverses "crew on foot never board again". Crew on foot who follow the party board the party's train the
  same way whenever it is held at a stop that is not the last, and they are within 25 m. They also board the parked train (B.5).
  "Let them out" goes, because an opened cage always lets its crew out.
- `PlanValidator.ValidateCrew` caps two per cage. `--test-crew` and the docs say "cage" where they meant the wagon at a stop.

### A.3 Fighting along the train (owner 5; details controller's call)

- **The move.** A seated hero who is up may move forward or back along the train while it rolls. It is a seat change like plan
  13's leap (`Adventurer.SitIn`, never a get-out): a 0.6 s hop, then `cart_jump`/`cart_land`, a small shake, and the log line
  "{name} climbs back to wagon 2." / "… forward into the cart."
  - The target is the nearest free hero place in that direction (a wagon's seat 0, or the cart's first free hero seat), passing
    any taken one, so a companion in W2 never walls off W3 and W4. One move is one hop, however many cars it passes.
  - The prompt follows the seat yaw. Looking back (more than 100° from forward) offers "Climb back to wagon N", N the target.
    Looking forward from a wagon offers "Climb forward to …". In VR the head's yaw and the grip do the same.
  - E does nothing for `MoveCooldown` (1 s) from the press, so a mashed key never becomes a get-out at speed.
- **Never offered:** while held or halted; while downed; inside a seated switch window, to anyone in the train (the window owns
  E); with any car of the train on the collapsing bridge's live span (`HoldsRiders`) or the exit bridge (B.2).
- **Seated in a wagon.**
  - A hero there fights with the seated combat that exists today, and reaches the boarder on its own wagon only.
  - **`BoarderReach` is re-keyed.** Today it is a cage-roof rule: 3.9 m and 45° to a hound on a coupled cage wagon's roof
    (`Adventurer.cs:1495`, `:1510`; `CompanionBrain.cs:82-87`, tuned so cart seats reach wagon 1's roof). The roof goes, so the
    rule moves to rim boarders, with a reach under the 2.6 m car spacing so it never touches a neighbouring car's boarder. The
    builder measures the value; a unit test pins it below the spacing.
  - It does not pump or brake (only the cart's riders do, `ServerWorld.RideInputOf`) and cannot leap to a slag cart (leaps go from
    the cart only). It counts as aboard for boarding, leaving and relief rules.
  - It is not "able" for a deadly switch (`AbleToThrow` reads the cart's seats). So a lone human fighting in a wagon at the pit
    gets R-lone's safe line, never a death.
- **Knocking a boarder off (no new binding).** A boarder on a wagon has a grip, as a barricade has its toughness: three hits or one
  spell knock it off alive, counted as `BarricadeRules.HitWeight` counts (a spell all of it, any other hit one). Companions' hits
  count. Its death also knocks it off, as today. A boarder knocked off drops beside the lane as a creature on foot, left behind;
  a miss lands it there too (today's rule). Every class can do it with the attacks it has, and a remote hero's hits ride the
  existing attack intents under protocol 17.
- **Companions and hero bots under AI.** The nearest seated one that is not already answering moves toward a wagon with a boarder,
  crew and no hero, one move at a time at the cooldown; at most one answers each boarded wagon. Once its wagon has been clear for
  5 s, it moves back to the cart if a hero seat is free. Companions never move while moves are refused (above), the human's switch
  window included.

### A.4 A boarder alone throws prisoners off (owner 6; values controller's call)

- **Alone** means a boarder is on wagon k's spot, at least one crew member sits in wagon k, no hero is in wagon k, and the train is
  rolling.
- **The warning.** When a wagon becomes alone, the nearest seated hero or companion of the train calls it through `RailCalls.Say`
  (`BarkTrigger.BoarderThrow`; the log line with nobody to speak is "It's at the prisoners — wagon N!"). The boarder crouches over
  the crew with a 1.5 s glow before each throw, the existing hazard warning.
- **The throws.** After `ThrowFirst` (6 s) alone, it throws one crew member off, the rear seat first, and one more after each
  `ThrowEvery` (4 s).
  - **The clock runs only while a hero could move to that wagon.** It pauses, keeping its count, while moves are refused for
    everyone (a seated switch window, a car on ride 4's live bridge or the exit bridge, A.3). So ride 4's hounds, which board at
    about S 836, never throw during the split's window (from S 858.6) or the cavern bridge's hold (S 903.1–991.1,
    `RailEncounterController.Bridge.cs:288-292`), and ride 4 needs no re-timing.
  - It resets to the full 6 s whenever the wagon stops being alone: a hero enters, the boarder dies or is knocked off, or the train
    is held.
  - 6 s covers a hero going from the cart to W4: four moves, the cooldown running from each press, take 3 × 1 s + a 0.6 s hop =
    3.6 s, leaving 2.4 s to react. Any later change to the wagon count or the cooldown must keep `ThrowFirst` ≥ (moves − 1) ×
    cooldown + hop + 2 s (5.6 s for four moves). A unit test pins this.
- **A thrown prisoner** is lost at once (`LostCrew`; "{name} is thrown from the wagon!"). It tumbles off the outer side as the
  derail's riders do, and is hidden after the tumble: no corpse, no revive, no loot. This is the cheapest path and reuses the
  lost-NPC state that saves and the wipe snapshot already handle. Escorts (Grisel) never ride in wagons and are never thrown.
  Bites on crew inside go on as today.
- **On the wire.** The unseat is a `SeatMsg`, the death comes in the snapshots, the tumble is `PropEvt wagon:{k}` "thrown:{crewId}",
  and the call goes the existing bark path.

### A.5 More boarders (controller's call)

Only ember hounds leap (`RailEncounterController.Leaps.cs:145`), and floor 12 has four: one each on `r2_slag2` and `r2_slag3`, two
on `r4_hounds`. So that fighting along the train runs the length of the line, plan A adds an ember-hound boarding group, the
existing leap kind, on rides 1, 3 and 5 (`r1_hounds`, `r3_hounds`, `r5_hounds`). This is new `floor_12.json` content: the builder
places each under the leap kind's placement rules and flow checks, and `RailEncounterPlanTests` pins the new orders. Ride 5's
group stands clear of plan B's pit pattern (x 61) and of the parked train's straight before the bridge, so plan B only re-measures
it.

### A.6 Plan A's flows and tests

- **`--test-crew`** is rewritten for a train present from the start. The floor check reads "a cage at each intermediate stop (4),
  two caged crew per cage (8), W1..W4 coupled and empty at stop 0". Each opened cage's crew must sit in the first empty seats
  before the departure; stop 1 left with its cage locked must leave W1 empty, and stop 2's crew must then take W1; the save and
  load must bring back `AboardCrew` seat for seat.
- **New `--test-boarders`** (solo, lethality 3; variants `=throw|reach|knock|lone|pause`): a hound alone with crew is called,
  glows and throws at 6 s and again at 10 s; the human climbs from the cart to W4 inside the 6 s and stops it, passing a wagon
  whose hero place a companion holds; a wagon hero's blows reach its own boarder and never the next car's; three hits or one spell
  knock it off alive; a companion answers a boarded wagon; the clock pauses inside a seated switch window and resumes after it.
- **The RIDE pass** (the controller's to run). `RideTrainCheck` becomes: W1..W4 coupled at stop 0 on every peer, the train hash
  equal, and stop 1's crew seated in W1 on the server and the client within 1 s of each other. The script's "the cage wagon coupled
  here … after the cart left stop 1" grep goes, and `prop:wagon:1:open` becomes `prop:cage:1:open`. New steps: a remote seat move
  to a wagon and back, a remote knock-off of a boarder, and a throw seen on a client.
- **Engine-free tests** for the new rules (moves and passing, `BoarderReach` under the spacing, the grip, alone, the throw clock and
  its pause, the `ThrowFirst` bound, seat order, `LostWagons`, the save merge). Every test that names `CoupledCages`/`LostCages` or
  a wagon by stop is changed.

---

## Plan B: the exit island, the pit switch, the parked train

### B.1 The layout (owner 1 and 2; method controller's call)

- **Two new `[AiIgnore]` knobs** in `SpecOverrides` and `FloorSpec`, modelled on `BrokenBridgeRide`: `PitRide` (int, 0..6) and
  `ExitIsland` (bool). Each extends `IsEmpty` and `Apply`, acts only with `Crevasses` on a laid line, and is hash-neutral when off.
  Pinned by `ChasmTests` and `RailLineTests.DefaultKnobs_BuildTodaysFloors`. Floor 12 sets `PitRide` 5 and `ExitIsland` true in
  `campaign.json`.
- **The pit** (`CarvePit`, after the gorge).
  - On ride `PitRide`, it takes the first straight party-lane run of at least `PitRunCells` (11) with plain enemy twins taken by
    nothing, no ramp in it or beside it, and fractions between 0.05 and 0.60 of the ride.
  - The pit is the run's cell `PitLeadCells` (7) from its start, so 28 m of straight precede the lip and 3 cells follow it. That
    cell, and the rock or pocket cells inward of it (t = −1, −2), become `Cell.Chasm` at the lane's level. The enemy twin stays
    solid lane with no deck, so walkers step round on it.
  - It is recorded as `RailLine.Pit` (ride, cell index) and printed as ` pit={idx}@{f}` (or `none`/`refused`) and `[map] rail pit:
    ride 5 at party S=…`. On floor 12 that should be cell 282, (61,56), with the lip near S 1073.
- **The island** (`CarveIsland`, after `CarvePit` and before the walkways, so no plank lands on a sealed hall).
  - **Island rooms** are the exit room plus every side room whose only walk (rails blocked) is through it. On floor 12 that is
    room 9 and room 12, the safe room. The island is those rooms, their halls to each other, and the line's cells from the
    bridge's far end to the platform, with their pockets.
  - **Sealing.** Every hall leaving an island room toward any other room is sealed from the room's wall out to its first fork or
    room: the hall cell at the island's mouth becomes a **lava trench** (chasm), and the rest becomes a **cave-in** (`Cell.Wall`,
    dressed on each open face with `Rubble.Collapse` boulders to the ceiling). From inside the island every way out is lava. From
    outside every hall ends at fallen boulders.
  - On floor 12 that gives: the west hall, trench (41,54) and cave-in (38..40,54); and the north hall, trench (46,49) and cave-in
    (46,46..48), including the plank (46,47), which leaves `Walkways`.
  - **What a sealed cell may hold.** A trap on a sealed cell is removed and logged (`[gen] island: trap at (46,49) sealed`). Any
    other taken cell on a sealed hall (`TakenCells`: a spawn, chest, key, objective, stair or vault door) refuses the island:
    ` island=refused (…)`, and no cut is made.
  - **The exit bridge.** It is the last straight party-lane run before the exit platform of at least `BridgeMinCells` (4), ending
    `BridgeIslandCells` (3) or more lane cells short of the platform, so the held train and any lift stand on solid island lane.
    Its cells, and the enemy cells beside them (the strips' t = +1 twins, corner cells excluded), become `Cell.Chasm` with the
    track **kept**: grid-unwalkable, judged by `CrevasseWatch`, with no footing. On floor 12 these are party (51..54,60), cells
    311..314, about S 1184–1199; enemy (51..53,61); and the island lane after them, (50,60), (49,58..60) and (48,58..61).
  - **The moat (controller's call, ruling R2).** Every rock or pocket cell at perpendicular offsets 1–2 on either side of each
    bridge cell becomes lava, and so do the trench cells. The island's other sides stay solid rock. Lanes, platforms, ramps'
    neighbours and taken cells are never cut. On floor 12 that is (51..54,58..59), which is the gorge span 311..313 already, plus
    (51..53,62..63). It leaves (48,62), into which `r5_spur` bends.
  - **The look.** An island region is dressed as **lava**: `ChasmDressing`'s shaft with a molten surface (the rail gap's material
    and light) 3 m below the lip. A fall there is a crevasse fall, with the same rules and the same instant death.
- **The `--print-map` line** gains ` island=room 9+12 sealed=2 bridge={From}..{To}` (or `refused (…)`). The docs' counts move:
  `chasm`, `planks` (3 becomes 2) and `[chasm] regions/decks/planks`.
- **Re-home.** The generator never reads plans. Floor 12's rooms and vaults keep their indices, so only `floor_12.json`'s ride-5
  pieces (B.3), its `LayoutHash` (via `--rehash-plans`) and the sweep for "repaired|dropped|demoted|adapted" move. Only floor 12's
  hash may move.

### B.2 The exit bridge at runtime (controller's call)

- **The look.** `ExitBridge` is built by every role from the layout. It is an open trestle over the lava: two bents per cell with
  posts into the molten surface, cross-braces, and a stringer under each rail. Sleepers sit on the stringers with lava showing
  between them. There is no deck, no planking and no handrail. It is procedural boxes on `weathered_planks`.
- **Carts roll on it; people on foot fall** (`CrevasseRules.IsDrop`; no `Deck_` box). No stop stands on it.
- **No "Get out" on it**, at any speed, for any rider of any cart with any part of its body over the span. This is a
  `HoldsRiders`-style hold on the party's train, the chaser, the relief and the parked train. Seat moves are refused too (A.3).
- **A wreck at or past the bridge's start** (a golem slam, the last plating) never makes a spare take over (ruling Q2). Today's
  `Wreck` empties the cart and `SpareTakesOver` would stand the train Held at stop 4 or earlier, outside
  (`RailController.cs:1004`, `:1050-1069`). Here, instead, the heroes and the crew aboard climb out on the island's side at
  `CrevasseRules.NearestSetDown` past the far edge (or where they are, if already past it) and are home (B.9). Nobody is ever set
  down on the bridge.
- **Carts held on the bridge.** A train braked to a stop on the bridge holds its riders until it rolls. No cart's limit is ever set
  on the bridge: the lift halts behind a train held at stop 5, at about S 1218.7, inside the island (recon F), and a chaser or
  relief halts behind the lift, outside (B.5).
- **Carried bodies.** Bodies carried at track height (riders, the golem on its runner, crew in wagons) never meet `Fell`'s 2.5 m,
  so nothing new is needed for them.
- **The pins.** Each 4 m of track over a drop reads 40 `nofloorDrop` samples. `TrackDepthRules.Floor12NoFloorDrop` becomes
  PartyTrack 40 × (pit 1 + bridge 4) = **200** and EnemyTrack 40 × (ride 3's gap 1 + bridge 3) = **160**. The builder pins the
  measured pair, explains any difference cell by cell in the commit, and updates `--test-track-view`.

### B.3 Ride 5 re-laid (controller's call)

The line's S do not change. The approximate S are the shipped floor's, and the builder measures each and re-pins the At values.

| Order | Piece | Where (party S) | Change and why |
|---|---|---|---|
| 1 | `r5_switch_out` | lever ≈1060, points ≈1065, x 61 | **New.** Deadly (B.4). The early call comes at about 1030, as the train leaves stop 4. |
| 2 | `r5_pit` (new kind `pit`) | lip ≈1073, far edge ≈1077 | **Replaces `r5_jump`** (R1). It is built from `RailLine.Pit`, as the split is built from its points. |
| 3 | `r5_switch_back` | points ≈1078, home by ≈1084 | **New.** Fixed, after ride 3's precedent (B.4). |
| 4 | `r5_pour`, `r5_golem`, `r5_trestle` | 1106, 1125, 1134.7–1146.7 | Unchanged. The golem's runner now starts after the crossing, so no clash. |
| 5 | `r5_hammers` | ≈1154/1157/1160, x 55 | **Moved** from x 61 (1061–1067), which the pit pattern needs, to the jump's old straight. |
| 6 | `r5_parked` (new kind `parked`) | rear ≈1170.5, front ≈1183 | **Replaces `r5_barricade`** (B.5). It has the same smash, so ride 5 keeps one smash, not two in 11 m. |
| 7 | the exit bridge | ≈1184–1199 | New layout (B.1, B.2). |
| 8 | `r5_spur` | points 1200.3, on the island | Kept. If its lever falls on a bridge cell, it stands `LeverPast` (3 m) past the points on solid lane, as the split's does. It is thrown from the seat only, because walkers cannot reach the island. |
| 9 | `r5_buffer` | 1221.3 | Unchanged, inside the island. |

Plan A's `r5_hounds` (A.5) keeps its place in this order and is re-measured.

- **`StripPlatformGap`.** The bridge keeps 4 cells to the platform. The island cuts keep every `TakenCells` rule and stay off the
  platform's cells, but they are exempt from `StripPlatformGap`, like the trench beside room 9.
- **`RailEncounterPlanTests`** pins the new ride-5 order: switch_out < pit < switch_back < pour < golem < trestle < hammers < parked
  < spur < buffer, with `r5_hounds` where plan A placed it.
- **The traffic runaway** on ride 5 (stop 4 held with nobody aboard) ends 3 m short of the pit's near edge, so it never reaches the
  parked train.

### B.4 The pit switch (owner 2; rules controller's call)

- **Deadly.** A `switch_out` is deadly when a pit on its own ride lies `PitRules.LeadMin` (7 m) to `LeadMax` (40 m) past its points
  on the party lane. Every role decides it at build: `[rails] switch {id} points … (deadly: the pit {lead} m past)`. Ride 3's
  switch_out stays plain (it has the heap).
- **Ride 3's rules apply**, moved to the out lever:
  - **Human only** (layers 1|8 and `MayToggle`), in **plan 8's seated window**: 15 m, the 1 s grace, the 0.5 s guard, the 5 m
    `AcceptLead`. No one in the train moves a car inside it (A.3), and boarders' throw clocks pause (A.4).
  - **Called twice** (`BarkTrigger.PitSwitch` and `PitSwitchNow`): early, "Lava pit on the line — throw the switch!", 30 m before
    the lever; last, "Throw it — now!", 6 m before the points while still straight.
  - **R-lone.** If the front reaches the points with the lever straight and no able human in the cart, it crosses as if thrown:
    "No one could reach the switch — the cart takes the safe line."
  - **Late press.** A late press says "Too late to throw it." A press on a lever already thrown is ignored quietly.
- **The back is fixed** (controller's call, ride 3's precedent: a back is live only when a deadly gap lies after it on the slag
  line; here the slag line is the safe one). `TakeSwitch` crosses with `backS` set (always thrown, untouchable), so the train comes
  home 1 m past the pit's far edge.
- **The fall.** A train whose front reaches the pit's near edge with `LaneBlendAt(lip)` < 0.5 falls: `Fall("The cart runs into the
  lava pit!")` and `[rails] the cart runs into the pit at S=…`. It is a crevasse fall, so it takes the whole train and its wagons.
  - The blend is read per S, so if the front is across, every wagon is across.
  - **The guard.** The fall is armed only if the points stand ≥ `SwitchStretch` + 1 m short of the lip and a back stands at or past
    the far edge. Otherwise an ERROR line says so and the fall is disarmed (`FallArmed`'s twin).
- **The sight.** A steady red lamp stands on the lever's post. The pit is dressed as the broken bridge's gap: rail stubs bent down,
  red lamps at the near and far edges, and the lava's glow and haze. A layer-1 ray from the seated lead's eye at the lever's S to
  the near lamp must be clear.
- **The watchdog** (`WatchdogPit`, plan 11's steps, never carrying a train past what its player was shown). A train stuck on the
  party lane short of the pit with the lever straight is set (1) at the window's start, still rolling; then (2), inside the window
  and short of the last call, at the last call's mark; then (3) at the points, where the lever is taken as it stands (R-lone
  applies); and (4) if stuck past the points on the party lane, it is ground on into the pit (`FallNow`). Stuck across, it is
  freed to the next stop as ever.
- **The jump goes from floor 12.** The `jump` kind, `RailGap`, `SetGap`, `PumpForJump` and "Pump it — we have to jump!" stay for
  other floors. They are harmless, since no shipped plan uses them, and `JudgeJump` and `FreeTo.GapLip` gain a lane check: a gap
  whose near edge the train meets across (`LaneBlendAt` ≥ 0.5) is passed, never judged. A unit test pins it. `GapLip` is never
  used for a pit, because a pit cannot be leapt.
- **Chasers and reliefs** hop the pit as a hole (`SpareRules.HopPast`), as they hopped the jump.

### B.5 The parked train (owner 3; details controller's call)

- **What it is.** A full train standing on the party lane before the exit bridge: a cart and four empty wagons, with chocks and a
  rear lantern. Its front stands 1 m short of the bridge's near edge, so it is built from the layout's bridge, as the split is from
  its points. It stands where a walking party reaches it along ride 5's lanes. On the wire it is one more live cart:
  `CartStateDto` with the chaser's kind and its own id, its seats and its wagons' seats as `SeatMsg`, and `PropEvt parked:{piece}`
  "smashed" and "standing".
- **Its states:**
  - **In the way.** The party's live train is on ride 5 short of it, or held at stop 4. It offers no boarding. A seated hero calls
    it at 35 m (`BarkTrigger.ParkedCart`, "Cart on the line — brace!"), its lantern blinks by the heap's rule, and `cart_bell`
    rings.
  - **Smashed.** When the party's train front reaches its rear face, it smashes at the barricade's cost (`BarricadeRules`):
    `SmashDent` 25 plating, 80% of the speed kept, 15% × lethality/3 of max HP to each seated hero who is up anywhere in the train
    (never below 1 HP); crew and escorts are jolted only. Its five tubs tumble off the outer side and are freed after 6 s.
  - **A lift.** It stands whole again, with the log line "Another cart rolls out onto the line by the lava.", whenever none stands
    or rolls and either the party's train is past the bridge's far edge or held at stop 5, or the party has no live cart (wrecked
    with no spare, hidden). A train wrecked past the bridge's start (B.2) counts as past it.
- **Boarding a lift.** "Board the cart" seats the player, their companions within 25 m, escorts on its bench, and crew on foot
  within 25 m in its wagons. It counts down 5 s, with "Get out" closed for its first 2 s, and rolls only with a player aboard.
  - **The party's train is held at stop 5, or past the bridge or wrecked past its start.** The lift runs as the chaser does: no
    pumping, no set piece judged. It halts 1.5 m behind the held train's rear (or at stop 5's limit if there is none), inside the
    island, sets its riders down on island cells and is gone 2 s later.
  - **The party has no live cart, outside.** The lift **takes over**: the party's cart and four fresh wagons stand Held at its
    place, as `SpareTakesOver` does at a stop. It departs as a ride-5 departure would (the pieces short of its place count as
    passed) and arrives at stop 5 as the party's train.
- **Riders when it is smashed (never a death).** Boarding is refused while it is in the way, so riders can only be aboard through a
  race. At the smash, each one is thrown clear onto its own set-down cell on the outer bank (`SetDownNear`, never a drop) and
  takes at most the smash's floored share. They board the lift that stands again once the train is past.
- **A chaser or relief halts behind the lift, outside (ruling Q5).** It treats a standing parked train as the train ahead: its
  limit is 1.5 m behind the lift's rear, short of the bridge. Its riders step out there, board the lift, and the lift carries them
  in. A chaser always runs behind the party's train, so by the time it reaches ride 5's end the lift has already stood again. No
  chaser or relief ever crosses the exit bridge.
- **Not saved.** It stands at its ride's arming and at every restore, as barricades do. A takeover keeps `RailStop` at 4, so a load
  puts the cart Held at stop 4.

### B.6 Inside the island, and outside it (controller's call)

- **Inside:** room 9, the down stairs, stop 5, the win's reward chest (B.9, ruling R3), and the safe room 12 with its merchant
  (reachable only at the end now). Also `col_stairbell`, which becomes a **Guard** of room 9: it keeps its id, members and lines,
  but has no alarm and no rally. Its target (room 10) and rally room (room 4) cannot be reached from the island, and a Guard at
  the last stop gets no gate.
- **Outside:** everything else, including the shrine (hub) and the Founders' Strongbox (vault 0, room 10 at (20,16), hidden door
  (24,19)). The Strongbox is unchanged: a key-only vault with its key at stop 3 ((45,25)) and `col_vault` guarding it, opened on
  foot with that key before the party rides on.
- **The builder checks** that safe-room gathering skips any group that cannot reach the door, with no ERROR lines.

### B.7 Solvability: the rail proof (controller's call)

On an `ExitIsland` floor, `FloorSolvability.Problems` replaces the on-foot stairs checks with these.

1. **On foot, rails open.** From the up stairs, every cage, key and room outside the island is reachable, and so are the lane cells
   beside the parked train's place.
2. **Rails blocked.** Every cage and key is still reachable, as today. The stairs are left out.
3. **Sealed.** On foot, rails open, the down stairs and every island cell are **unreachable**: "the exit island leaks at (x,y)".
4. **By rail.** The party track from the parked train's place to stop 5's platform is continuous. Its cells are lane or bridge
   cells, with no cut but the bridge's kept track.
5. **Inside.** From stop 5's platform the down stairs and every island room (the reward chest's room 9 among them) are reachable
   on foot.
6. **The pit.** Its enemy twins and the run around it are solid lane. Otherwise "the pit cuts the line".

The reward vault stays as `FloorSolvability.cs:143`, `:156` and `:161` treat it today, a vault that opens at the win; room 10 lies
outside and check 1 reaches it. The reward chest is runtime content and needs no proof beyond check 5. `ChasmProblems` skips the
island's own cells and still guards every other room. `--test-vault-puzzle`'s sweep, `ChasmTests` and `RailLineTests` take the new
proof. On a floor with `ExitIsland` off, nothing changes.

### B.8 Left behind and soft-locks (controller's call)

| Case (recon F, builder's check Q2 and Q5) | Resolution |
|---|---|
| It is a one-way trip. | Nothing outside is needed to finish. A cage left behind is lost, as today. A cage opened later on foot frees crew on foot (B.9). |
| Cages never reached (a party on foot after the last spare, or one that left early). | Once every up player is inside the island with the train at stop 5, every unopened cage resolves as left behind and its crew are lost, so the win always resolves (B.9). |
| Crew on foot (`OnFoot`, an opened cage, stragglers). | They board a held party train (A.2) or a lift (B.5). Otherwise they are stranded (B.9). |
| A solo player left behind with the train at stop 5 (`LineLive` false, no relief). | They walk ride 5's lanes and take the lift. A wipe's rise at stop 5 also brings them in. |
| A co-op player left behind, `spare:4` spent. | The lift. |
| A chaser or relief behind the held train. | It halts 1.5 m behind the lift, outside; its riders board the lift, which carries them in (B.5). |
| A wreck after the last spare, short of the bridge. | They walk to the parked train; the lift takes over. If it was smashed, it stands again, because the party has no live cart. |
| A wreck at or past the exit bridge's start, spare or not. | No spare takes over. The heroes and the crew aboard are set down inside, past the far edge, and are home (B.2). |
| A braked stop on the exit bridge. | No get-out and no seat move while over it; it holds its riders until it rolls (B.2). |
| A wipe. | It rises at the last stop reached (stop 4 outside, stop 5 inside), as today. |
| A fall into the moat, the pit or the bridge. | The fallen come back at the next arrival, at a hero's step onto a platform (stop 5's serves), at a restore or at the floor change, as today. |
| The stairs vote with a player elsewhere. | The floor change takes everyone: `Party.Teleport` puts every hero at the arrival (`Main.cs:700-702`). But a Yes is given only on the landing (`ServerReadyCheck`), and a lone Yes of two is no majority, so a 2-player server waits for the outsider, who always has the lift. With 3 or more, a majority goes at 15 s and the outsider comes along. Downed and parked players are not waited for. |
| A rejoin at a parked pose outside. | The lift. |
| An escort (Grisel) outside with no up player outside and the train at stop 5. | She is set down beside the held train in the island, with a log line, never stranded, because her quest and floor 17 hang on her. |
| Outside enemies. | They cannot path onto bridge or moat cells (grid chasm), so they never follow and never fall chasing. |

### B.9 How the jailbreak is counted (controller's call)

- **Home** (`InExitRoom` becomes `InIsland` on an island floor) means standing anywhere in the island, seated in the party's train
  while it is held at stop 5, or set down there by a lift or by a wreck at or past the bridge's start (B.2).
- **Lost** means dead, thrown from a wagon, or in a cage left behind (including every unopened cage once every up player is inside
  the island with the train at stop 5, or its wreck inside, B.2). Or:
  - **stranded**: on foot outside the island while the party's train is held at stop 5 (or the party has no train), and no up,
    unparked player stands outside the island. Stranding is judged live, so a player who rejoins at a parked pose outside can
    still bring them in; once the floor is won, the win stands.
- **The win** is unchanged in form. Every cage must be open or left behind, and every crew member home or lost. The win then
  unseals the stairs and sets `jailbreak_freed_all`, as today.
- **The reward (ruling R3, Q1).** At the win a reward chest appears in room 9, holding what vault 0 (the Founders' Strongbox)
  holds. It is new runtime content: a loot table copied from vault 0, a save field and a wire path (a `PropEvt`), with no
  generator change for the vault. The win still reveals and opens room 10's door (`CrewRescueController.cs:410`), which is
  harmless, since nobody can reach it after. The log line "Somewhere on this floor, a vault door swings wide for you." gives way on
  an island floor to one that points at the chest (wording left to the builder). The HUD line is unchanged.

### B.10 Plan B's flows and tests (recon C.4: each jump dependent)

| Dependent | Becomes |
|---|---|
| `--test-cart-watchdog` step 3 | Restored at stop 4 and caught short of the pit's window, the train is set at the window's start still rolling. The lead throws from the seat, and the train comes home past the pit and arrives at stop 5. |
| `--test-chase`, the two-pumper jump and the one-pumper jump with four wagons | The lead throws the pit switch from the seat, and the train crosses and comes home with no fall. Section 2 does it with `PumperCap` 1. "The cart leaps the gap!" becomes "The cart takes the switch round the pit." |
| `--test-crevasse` B (and C, `=trestle`, `=survivor`) | Nobody presses with an able human aboard. The train runs into the pit and falls, with W1's crew seated. |
| `--test-cavern`'s bare-track check | Bare over the pit cell, and **laid** over every exit bridge cell. |
| The RIDE pass's chaser "hopped [1-9] hole(s)" | It holds: the pit and the fallen trestle are holes. Step 8 becomes two rides: the chaser halts within 3 m of the lift's rear, outside; then the lift halts within 3 m of the held train, inside the island. |
| `TrackStationsTests` (1156.0, 1161.0) | The pit's measured From/To. |
| `RailEncounterPlanTests` ride-5 order | B.3's order. |
| `RampRideTests`' ride-5 row | Four wagons from ride 1 (A.1). |
| `LeapRulesTests.ShippedFloor12()` hash | The new hash. Cavern 238..259 is unchanged. |
| Docs (`development.md:1539`, `2094-2100`, `2236-2240`) | Rewritten for the pit, the parked train, the island and the reward chest. |

- **New `--test-pit`** (`=thrown|wrong|lone|stall|late`) mirrors `--test-switch-back`: the early call 25–30 m before the lever;
  the companions never touch the lever; thrown: no fall and arrival at stop 5; wrong: a fall and the wipe at stop 4; lone: the
  safe line; stall: the watchdog's four steps; late: "Too late".
- **New `--test-island`** (every check reads `LineShape`): every sealed hall ends at a cave-in, and a walker at its face never gets
  through; a walker stepping onto the bridge falls; no Get out and no seat move over the bridge at any speed; the parked train is
  in the way, smashed (−25 plating, 15% HP), and stands again once the train is past; a player left outside rides the lift in; a
  party on foot after the last spare takes over and wins, with stops 3 and 4's cages unvisited and resolved as left behind; a
  wreck on the bridge with a spare left sets the heroes and crew down inside, home, and no spare takes over; stranded crew are
  counted lost; Grisel is brought in; the win's chest appears in room 9.
- **The RIDE pass** (the controller's to run): a remote seat throws the pit switch; no remote Get out over the bridge; the parked
  train's smash is seen on a client; the joiner, left outside after the arrival, rides the lift in.

---

## Build order: two plans (controller's call)

- **Plan A first:** A.1–A.6, built on today's layout with the jump still in place (it already carries four wagons at one pumper).
- **Plan B second:** B.1–B.10. The parked train's place and size, the lift's halt behind the held train, the exit bridge's hold on
  "every car over the span", and the stranded rule all read the full-length train and the `AboardCrew`/wagon identities that plan A
  builds. B also re-measures ride 5 once, against A's 5.22 m/s.

## Left to the builder

- **The exact cells and S.** The generator rules decide them. The S and At values in B.3 are approximate and are measured with
  `--print-map` and `--track-depth`, and every placement rule must still hold.
- **The placement of the new hound groups** (A.5) under the leap kind's rules, and the measured `BoarderReach` below 2.6 m.
- **The dressing:** the cave-in's boulder count, the lava surface's depth and light, the trestle's bent spacing, the parked
  train's chocks and lantern, and the reward chest's look.
- **The wording** of the new stock lines (subtitle only), the reward chest's log line and any HUD bar text not given here.
- **How the move prompt reads the yaw** within the cones given, and how companions pick which wagon to answer.
- **The new flows' names, steps and timeouts**, following the existing flows' RESULT and watchdog conventions.
- **Which docs sections are rewritten**, and the measured pins and numbers each one quotes.

## The rulings (controller's calls; the owner said "build it")

**The owner's three open questions.** If the owner disagrees, each has a stated fallback.

- **R1. The pit moves** to ride 5's first straight (lip ≈ S 1073), with the hammers at the jump's old straight. Keeping it at the
  jump's place (x 55, S ≈1156) leaves no room for both the switch pattern and the 12.6 m parked train before the bridge. It is
  still "the last section" (ride 5). Fallback: keep the pit at the end of ride 5, open the seated window round the y 69 corner,
  and cut the parked train to two wagons. Lands in: Approaches, B.1, B.3.
- **R2. Lava at the bridge and in a trench at each sealed hall's mouth**; the island's other sides are rock with no lava to see.
  Fallback: a visible lava ring, which needs a larger re-layout of the rock between room 9 and rooms 1 and 8. Lands in: B.1.
- **R3. The crew win's reward moves into the island**, since room 10 is cut off after the win. Lands in: B.6, B.7, B.9 (with Q1).

**The builder's check** (`.superpowers/sdd/shared/design-builder-check.md`).

- **Q1. The reward:** a chest in room 9 at the win, holding what vault 0 holds; room 10's door still opens on the win; no generator
  change for the vault. Cost: the chest is new content, not a vault. Lands in: B.1 (re-home), B.6, B.7, B.9.
- **Q2. Left outside:** a wreck at or past the exit bridge's start never makes a spare take over, and the crew aboard are set down
  inside with the heroes (home); once every up player is inside with the train at stop 5, every unopened cage resolves as left
  behind. Lands in: A.1, B.2, B.5, B.8, B.9, B.10.
- **Q3. The knock-off:** no new binding; a boarder's grip, like a barricade's toughness: three hits or one spell (companions'
  hits count), or its death. Lands in: A.3, A.6.
- **Q4. The throw clock** runs only while a hero could move to that wagon, and pauses while moves are refused for everyone.
  Lands in: A.3, A.4, B.4.
- **Q5. A chaser halts behind the lift**, outside; its riders board the lift, which carries them in. Lands in: B.2, B.5, B.8,
  B.10.
- **Q6. A move goes to the nearest free hero place** in its direction, passing any taken one. Lands in: A.3, A.6.
- **Q7. More boarders:** an ember-hound boarding group on rides 1, 3 and 5. Lands in: A.5, B.3.
- **The contradictions fixed:** B.6 and B.9 follow Q1; `BoarderReach` is re-keyed to rim boarders under the 2.6 m spacing (A.3);
  `BoardMax` is a new constant (A.2); the throw bound is (moves − 1) × cooldown + hop + 2 s = 5.6 s ≤ `ThrowFirst` 6, the cooldown
  from the press (A.3, A.4); `LostWagons` holds only between a loss and the next restore (A.1); B.8 follows Q2 and Q5.
