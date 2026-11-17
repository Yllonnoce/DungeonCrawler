# Floor 12: the mine cart chase — design

Date: 2026-11-17. Status: draft for the owner's approval.

## Why

The owner wants floor 12 of the shipped story (*The Gullet of Gorm*, "The Casting Hall") to become a mine cart chase,
"as much fun as you can imagine". Floor 12 is a Jailbreak floor: Grisel Thorn's crew are caged here, nearly turned into
cinders. Today it plays like every other jailbreak (walk the ring, find the jail, unlock prisoners, walk them out). The
cart chase gives the floor its own identity and makes the rescue a ride: you break the crew out stop by stop and haul
them to the stair while the orc's foundry tries to stop you.

## What the owner decided (their words, condensed)

- You **can** walk the halls, but it is not a good idea.
- A mine cart waits. You take it and it carries you along its track to the end; there you **unlock the next track**.
  The floor is a chain of rides.
- **Mobs at each stop must be killed** to unlock the next track.
- Monsters ride their own carts **on a parallel track** and you fight them while both carts roll.
- **Anything on the rails when a cart comes gets rolled over: mobs and heroes.** A hero who gets out of the cart is
  rolled over; "should have been in the carts".
- You **can walk the rails**: there are **small pockets in the tunnel walls to hide in** from passing carts.
- Build approach: **C** (below). Look to the web and use agents for inspiration, effects and physics. "Go crazy."

Agreed in conversation as my proposals (the owner did not object): the cart moves by itself (no steering); the crew are
held at the stops and ride with you once freed; the walking route always stays open as the fallback.

## Goals

1. Floor 12 is a sequence of four rides between stops, each ride short (60–90 s) and each adding one new thing.
2. Fighting from a moving cart uses the full hero kit (melee, ranged, spells). Never a turret section.
3. The rescue is the spine: each stop frees part of the crew, who ride behind you in a wagon you must protect.
4. It works in single player (AI companions), in online co-op (up to 4 players plus hero bots), and in VR.
5. The floor can always be finished on foot; nothing about the carts can soft-lock it.

## Non-goals

- Rails on any other floor now. The generator knob exists so a later floor or the AI designer can use it, but only
  floor 12 turns it on.
- Steering. The player controls speed (brake, pump) and points (switches), not direction.
- Physics-engine carts (RigidBody/VehicleBody), except a cosmetic tumble when a cart is wrecked.
- Changing floor 11 (it keeps its levers) or the rest of the campaign.
- Installers / dist builds (the owner asks for those when a batch is done).

## Anti-goals (it fails even if it "works" when…)

- A hero dies to something they could not see coming. Every cart and hazard warns by light **and** sound first.
- Riding feels like losing control of the game, or a ride drags.
- A VR player feels sick.
- Co-op carts stutter, rubber-band, or desync so one player sees a different ride.
- A stuck cart, a wrecked cart or a dead passenger seals the stairs.

## Approaches considered

- **A. A new rail floor shape in the generator.** Every system that reads the grid keeps working and any seed could
  use it, but grid track is straight runs and right-angle bends: flat for a "go crazy" ride. A new LayoutKind also
  changes `LayoutKinds.Deal()` for every seed, which would move the objectives on shipped floors 11 and 13.
- **B. A hand-built 3D ride for floor 12 only.** The wildest ride, but nothing else knows the track exists: enemy
  pathing, the map, the solvability checker and the co-op world checks would all need special cases. A one-off.
- **C. Both (chosen by the owner).** The generator carves the rail tunnels, refuge pockets and stops into the grid
  behind a new spec knob, so pathing, co-op checks, the map and `FloorSolvability` keep working. `DungeonBuilder`
  then dresses rail cells as real 3D track: smooth curves, banking, dips and climbs, and set pieces.

## The floor as the player sees it

The orc's ore line runs through the Casting Hall: two tracks side by side in timbered, lantern-lit tunnels. **Yours** (the
old warden line) and **his** (the slag line). Grey cinder crews push slag carts along his line all day; today they are
told to stop you.

```
 Kiln-Mouth Landing (Start)                                    Cooling Stair (Exit)
   [your cart]==ride 1==>(STOP 1)==ride 2==>(STOP 2)==ride 3==>(STOP 3)==ride 4==>[buffer]
   [slag cart]~~~~~~~~~~~>   |   ~~~~~~~~~~~~>   |   ~~~~~~~~~~~~>   |   ~~~~~~~~~~~~~>
     tunnel wall: [pocket]  cage    [pocket]   cage    [pocket]   cage     [pocket]
   halls of the ring (walkable, patrolled, the horde breaches) connect to every stop
```

### Boarding and departing

- The party cart waits at Kiln-Mouth Landing. **E on the cart** sits the hero in a free seat (4 seats).
- When the first hero boards, a short "All aboard!" countdown starts (5 s, shown on the HUD and called out by a
  companion); the cart departs when it ends or when every living hero is seated. AI companions board with the lead.
- Anyone not aboard is left behind and must walk (and dodge carts) or catch up at the next stop.
- **E on the cart while seated** gets out. Getting out on a moving cart throws you onto the rails (see run-over).

### A stop

1. The cart rolls into the stop and is held by a **rail gate** (an iron bar across both tracks, chained to a winch).
2. The stop's mobs attack. The gate lifts when **every enemy assigned to that stop is dead** (new "stop cleared" check).
3. A **cage wagon** stands on a siding at the stop with part of Grisel's crew inside. Unlock it (key or lockpick, as
   jailbreak cages work today) and it **couples to the back of your cart**: the freed crew ride in it. The train grows
   by one wagon per stop.
4. Any hero pulls the departure lever, or the countdown runs as at boarding. The next ride begins.

### The four rides (escalation: one new thing per ride)

1. **The Ore Run — learn the cart.** A calm, lantern-lit start, then the first big dip into the Casting Hall. Teaches,
   each once and safely: a lone cinder standing on your rails (run it over); a **low beam that clears the cart by a hand's
   width** (a near miss, never a hit while seated); a trackside **slag thrower** in a pocket, with a bright wind-up glow
   before each throw.
2. **Side by Side — the enemy line wakes.** Slag carts on the parallel track pull alongside and hold pace, coming in
   waves on alternating sides. Their riders throw fire and slag; **ember hounds crouch, then leap across** onto your cart.
   Each slag cart has a **glowing brake shoe**: break it and that cart derails in a shower of sparks and its riders are
   thrown under the next cart.
3. **The Switchyard — choices.** Track switches by the rails: pull one in passing (E or hit it) to dodge a blocked track,
   or to **cross onto the enemy line and ram a slag cart from behind**. The lines cross once at a junction: whoever
   reaches the crossing first goes through and the other must brake or crash, so the brake matters. **Ore buckets** on an
   overhead line drop cinders onto your cart.
4. **The Pour — the climax.** Stop 3 looks like the end (the crew are all free, the music settles); then a **forge golem
   bursts through the kiln wall onto the slag line** and the chase restarts. The last ride runs under the pour channels:
   **drop hammers that fall on the beat of the music**, molten pours that close the track briefly (break the quench
   valve to stop one), a **collapsing trestle** over the casting floor, and a **jump across a gap** that needs speed: the
   heavier train (more crew) has to be pumped to make it. The golem runs beside you to the end and is beaten by fighting
   or by switching it onto the dead-end spur. The train smashes through the buffer into the Cooling Stair.

### Cart controls (stations)

- **Brake lever** (front): slows the train. For junction timing, sharp bends, and stopping to let a slag cart pass.
- **Pump handle** (middle): a hero working it speeds the train up; two pumping go faster. Pumping means not fighting.
- Everyone else fights. AI companions and hero bots fill stations by need: pump when the train is slow on a climb or
  approaching the jump, brake when a crossing is contested, otherwise fight.
- Each freed crew member makes the train a little heavier (about 5% slower per wagon of crew), so rescue costs speed.

### The cart can be hurt; the floor cannot be lost to it

- The party cart has **iron plating** (cart health, shown on the HUD). Enemy fire, leaping hounds, rammed golems and
  big bodies on the rails damage it.
- At zero the cart **is wrecked spectacularly** (it tumbles into a slag heap; a cosmetic physics copy), seated heroes are
  thrown clear and take a heavy hit but are not mangled, and crew in the cage wagons are knocked down.
- A **spare cart waits at every stop**. After a wreck the party walks the rails (pockets!) to the nearest stop and takes
  the spare; the cage wagons they had are re-coupled there (they roll to the nearest stop on their own).
- **Halls stay walkable**: every stop room connects to the ring. On foot the halls are worse than the rails: the ring's
  patrols, the Oddfellows' camp, and the Cinder Breaches horde. But they always lead to every cage and to the stair.

### Rolled over

- A **cart** (any cart: yours, a spare, a slag cart, a runaway wagon) hits whatever is on its rails when it reaches it.
- **Heroes on foot on the rails are mangled** (the existing crush outcome at full lethality) and must be dragged free by
  a friend (the existing 3 s rescue). Heroes seated in a cart are never hit by carts.
- **Small enemies** (cinders, hounds, priests) die. **Big ones** (cinder brutes, forge golems) take a heavy hit and are
  knocked aside; the cart takes damage and jolts.
- **Enemies step onto your rails on purpose** to block you; shoot them first or ram them.
- **Freed crew** are never hit while in a wagon. On foot they can be hit and go down; they can be **revived** (new).
- **Refuge pockets**: one-cell niches in the tunnel wall, lit by a lantern, at most about 3 cells (12 m) apart. Standing
  in a pocket is safe from every cart. AI companions and crew use them.
- **Warnings**: every cart carries a lamp whose light reaches round the bend ahead of it; the rails "sing" and a horn or
  bell sounds 2–3 s before it arrives; sparks on the rail. Hazards are telegraphed by glow and sound.

### Grisel and the story

- **Forgiven** (`grisel_forgiven`): Grisel is alive (room 7 today). She rides in your cart, calls out which stop holds
  which of her crew ("they'll know my voice before they know my face"), and opens the cage wagons without keys. Her
  escort quest still completes when she reaches the stair alive. While seated she cannot be hurt; thrown, she can be
  revived (fixes today's gap where a downed escort NPC breaks the quest).
- **Condemned** (`ForbidsFlag: grisel_forgiven`): she lies dead by the first stop's cage wagon with the orc's keys on her
  belt (the narration already says this). Her keys open every cage wagon; without them, lockpicks.
- The **orc taunts** the cart moments (new taunt triggers: ride start, a hero rolled over, the cart wrecked, a slag cart
  derailed, the golem's entrance, the buffer smash). The existing FloorEnter / Freed / AllFreed taunts and the room-11
  narrations stay, moved to fit (the cages are now at the stops).
- **Voice**: new lines are written in the established voices (the orc Morg Ashtusk, Grisel, the heroes' barks) and play
  as subtitles until the owner OKs a Runware voice pass (pennies, as the story rewire was).

## Feel

- **Desktop**: the camera leans into bends (about 6–8°, a damped spring), takes 30–50% of the track's pitch, gains up to
  +10° FOV at top speed, and shakes with the existing offset shake; rail-joint clacks at a rate set by speed; a thump on
  landing; no head bob while seated. Turn on physics interpolation (or interpolate cart and camera) so carts do not
  judder on high-refresh monitors.
- **Speed**: cruise about 8–10 m/s, gravity-driven on dips and climbs, clamped to about 14 m/s; pump adds about 30%.
- **VR** (from Meta's comfort guidance): never shake, roll or force pitch on the headset camera; the rig follows the
  cart's position and yaw only while the cart model banks beneath a level view; the cart rim and front board are a fixed
  frame in view; speed changes are eased and rare; a **comfort vignette** that tightens with acceleration and turning, with
  presets Off / Recommended / Strong (Strong adds a fade on big drops). Snap turn still works while seated.
- **Sound** carries the ride: rolling loop pitched by speed, screech on tight bends, clacks, brake squeal, switch clunk,
  creaking timber, a foundry rumble bed, molten hiss. A chase music track for the rides.

## How it is built (decided parts)

### Generator (`src/Dungeon/Generation`)

- New `FloorSpec.RailCount` (default 0; every other floor unchanged) and `SpecOverrides.RailCount` with a clamp in
  `Apply`. Floor 12's `SpecOverrides` in `campaign.json` sets it. Not a new LayoutKind (that would reshuffle `Deal()`).
- A salted rail pass (`new Random(seed ^ CONST)`) carves the twin-track tunnels, stops, sidings, pockets and spur, and
  records them in new `FloorLayout` lists (rail paths, stops, pockets); rail cells join `IsReserved`; the lists fold into
  `ContentHash`. Rails that join rooms are added to `Connections`.
- Existing room indices and roles hold (the plan addresses rooms by index). The jail objective must still generate on
  the shipped seed (`TauntTests` pins floor 12 = Jailbreak); in rail mode the jail becomes the stops' cage wagons.
- `FloorSolvability`: with rail cells blocked, the stairs, every cage and every key must still be reachable on foot.
- Protocol bump to 16; refresh floor 12's `LayoutHash` with `--rehash-plans`; re-home any plan contents whose room
  changed role (sweep `--builtin --floor=12` for "repaired|dropped|demoted|adapted").

### Building the track (`DungeonBuilder`)

- Each track segment between stops/junctions is a `Curve3D` with up vectors and tilt for banking, generated from the rail
  cells and smoothed; dips may sink below a cell's floor and climbs use the existing level-0/level-1 ramps.
- The track mesh is extruded along the sampled frames (CSG path mode cannot follow tilt). Tunnel timbering, lanterns,
  pockets and set-piece props follow the `PlaceMineTimbers` pattern.

### Carts (runtime)

- A cart's state is plain numbers: segment, distance along it, speed, mode (rolling / held / ballistic / wrecked). The
  server integrates it each tick (slope gravity, rolling drag, brake, pump, load) and samples the curve
  (`SampleBakedWithRotation`) to pose an `AnimatableBody3D`, as the press slabs move today. The track is a graph of
  segments; switches choose the successor and are lever props.
- **Riders are seated, not carried by platform physics** (Godot's moving-platform carry does not handle rotation and
  riders slide off). A seated `Adventurer` skips `MoveAndSlide` and takes its transform from its seat each tick; mouselook
  turns a seat-relative yaw. Companions skip formation movement while seated and fight normally. Cart moves before riders
  (physics priority). Getting out or being thrown restores normal movement with the cart's velocity.
- Enemy riders sit in enemy carts the same way.
- **Run-over is judged on the server as track logic**: anything whose closest point on the curve lies between last
  tick's and this tick's distance, within the cart's half-width and height. A look-ahead query drives warnings and lets AI
  step into pockets. Damage goes through the existing `IHazardVictim.ReceiveHazard` / `HazardRules.Outcome` path (the
  press's crush is the template), with a new big-enemy outcome (heavy hit, knocked aside) and `Shove` toward the nearest
  pocket.
- **Stop cleared**: a stop's enemies are tracked by id; `EventBus.EnemyDied` drops them; empty means the gate lifts.

### Co-op and saving

- Carts sync as events, the way hazard phases already do: a cart-state message on start, stop, junction, jump, hit,
  wreck, plus a correction a few times a second; clients run the same integrator on the same seed-built track and close
  drift by playing slightly fast or slow, snapping only when far off. Carts do not ride `WorldSnapshot.Enemies` (drawn
  100 ms in the past).
- Rider poses become seat-relative (cart id, seat, seat yaw) for players and enemies; the server's walkable-cell check on
  poses accepts seated riders.
- **Fix the VR tick-rate drift** this work depends on: timed sync assumes 60 ticks/s, but a VR client runs physics at the
  headset rate, so hazard timing already drifts on VR clients. Carts are timed in seconds against the server clock
  (ping/RTT offset), and hazard sync gets the same fix.
- Saving: carts, coupled wagons and the reached stop go in the objective state. A save or reload mid-ride puts the train
  back at the last stop it reached.
- **Watchdog**: a cart that makes no progress for 10 s is moved on to its next stop.

### Assets (licences checked by the research agent; the repo is public, so raw files must be redistributable)

- Prefer **CC0**: loafbrr *Mine Object collection* and *Mines and cave set* (tracks, carts, tunnel kit), rubberduck *free
  mine assets pack* (timbering), loafbrr *Wood Kit 1* (trestle), Poly Haven props and textures, ambientCG metal / timber
  / gravel / lava textures, freesound CC0 sounds (rolling, clacks, screech, brake, impacts, switch, creak, collapse,
  rumble, hiss), Kenney particle and smoke packs, godotshaders CC0 lava / glow / tunnel speed lines.
- **CC BY 4.0** where nothing CC0 fits (e.g. janchristian's *Rail Section (2-way split)*, the only free switch found;
  guguxa's *Mine cart Rusted*): allowed with a new `CREDITS.md` listing each.
- Not used: Quaternius direct downloads (new licence forbids redistributing files), Sonniss / ZapSplat / Pixabay raw
  files (no redistribution), anything NC / SA / AGPL, and the flagged-unsafe items in the research.
- Music: cynicmusic *Battle Theme A* (CC0) as default; the owner may prefer to supply a track, as with DungeonWalking.
- The owner's own 3dObject viewer models are welcome for the carts if they want to make them.

### Testing

- Engine-free unit tests for the cart integrator, run-over judgement, stop-cleared tracking and seating rules.
- Generator: a rail sweep (seeds × objectives) through `FloorSolvability` with rails blocked; existing generator tests
  unchanged with `RailCount` 0.
- Headless: a scripted ride of floor 12 on the shipped seed (`--builtin`), a run-over check, and a step in
  `scripts/net_test.sh` for a two-player ride.
- `--rehash-plans` shows only floor 12's hash moved.

## When the floor is won (the owner's choice, 2026-11-17)

- **The stairs open when the freed crew reach the Cooling Stair**, by train or on foot through the halls, not when the
  last cage is opened. Ride 4 is the main way out and the rescue counts only once the crew are home.
- Crew who die on the way are **lost, not blocking**: the floor completes when every cage is open and every living crew
  member is at the stair. Grisel's escort quest completes as today when she arrives alive.
- The HUD counts both: crew freed and crew home.

## Calls made after the builder check (mine; the owner may overrule)

- **The crew start caged at the stops.** No wanderers and no capture-and-march in rail mode; freed crew are never
  recaptured (as today). One cage wagon per stop, two crew per wagon (six in all on three stops).
- **The crew are Grisel's named people**, written into `floor_12.json`, not stock villager names (the stock list includes
  "Tobit", who is a cast member).
- **Room 11 "The Kiln Cages" becomes stop 1**: its narrations (crew caged; Grisel dead by the cage door) play there.
  Stops 2 and 3 are other existing ring rooms; indices and roles otherwise hold.
- **Grisel (forgiven) waits at Kiln-Mouth Landing** (moved from room 7 in the plan) and rides from ride 1. The party
  cart has **four hero seats plus a rear bench for two** (Grisel and the fighter ally who joined on floor 11).
- **Traffic**: slag carts run the slag line on a schedule all the time, not only beside the train, so walking it is
  dangerous. Runaway ore wagons come down the warden line now and then while nobody rides it. Gates stay up once
  lifted. A co-op player left behind may take a stop's spare cart; it runs to the stop where the train waits and halts
  behind it (no second train past that).
- **Run-overs follow the lethality setting**, as the press does: mangled at the default (3), part of max HP below 2.5.
- **Enemies are plan data**: stop mobs are collectives placed in the stop rooms; slag-cart riders and the golem are a new
  horde-like list of rail encounters in the plan (budgeted outside `EnemyCount`, as hordes are today), so a later floor
  or the AI designer can use them.

## Left to the builder

- Exact stop count (three stops plus the exit is the target) and where the set pieces go, by what the geometry allows.
- Tuning numbers: speeds, damage, cart health, countdowns, pocket spacing.
- How the rail pass fits around the ring (late salted pass vs before levels) as long as room indices hold and the jail
  still generates.
- Asset choice within the licence rules above; HUD layout for cart health and countdowns.
- Implementation order. Suggested: (1) generator + track dressing; (2) cart, seating and run-over in single player;
  (3) stops, gate, cage wagons, Grisel; (4) enemy line, switches, set pieces; (5) co-op + VR comfort; (6) lines and
  taunts.
