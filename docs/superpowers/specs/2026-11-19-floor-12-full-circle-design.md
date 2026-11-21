# Floor 12: full circle — the longer, deadlier ride (design)

> **Owner decision, 2026-11-21: the loop-the-loop is dropped.** "A loop that completes a loop" meant a contiguous
> track when you choose correctly: the full ring (plan 9) and switches whose right choice keeps the line whole and
> whose wrong choice kills (plans 11 and 12). Plan 15 is not built. Every loop-the-loop item below is withdrawn; the
> cavern plan (13) keeps the collapsing bridge and the spiral.
>
> **Owner decision, 2026-11-22: the spiral is removed for now.** A spiral down into the cavern would have to come
> back up to stop 4 at floor height (a chain lift or a second helix), and the owner chose to drop it. Plan 13 is the
> cavern's collapsing bridge only; the track stays on the floor's two levels, with no free 3D track. Every spiral item
> below is withdrawn (it may return in a later plan).

Approved by the owner on 2026-11-19. After their solo play test they asked for:
- barricades;
- a dead switch made live;
- "much longer";
- fun tricks;
- a new cart after everyone dies;
- instant-death crevasses.

I proposed a design for each and the owner answered "do it". Their follow-ups are part of this design:
- "You can make one track that is a loop that completes a loop."
- "Wrong choices are going to make you die or get really hurt."
- "Many parts of the dungeon are not getting used."

This is an addendum to `2026-11-17-floor-12-mine-cart-chase-design.md` and the two 2026-11-19 addenda (relief cart and
seated switch). Those still bind everything this one does not change.

## Why

The owner found the ride "so fun it needs to be much longer". On the shipped seed it is about 500 m in four rides of
12–24 s. It covers only the east half of the ring: the whole west half, the hub, and rooms 2, 3, 4 and 8 never see
it. Today's hazards also hurt too little:
- The wooden frames along the track are pure scenery.
- The return switch looks live but cannot be touched.
- A missed jump only wrecks the cart.
- A wipe sends the party back to the floor's entrance stairs, not to the train.

## Goals

1. **Full circle.** The line runs all the way round the ring, through the west half, and ends at a new Cooling Stair
   beside Kiln-Mouth Landing. It is one track that completes a loop.
2. **Much longer.** Five rides of about 200–280 m each (about 35–50 s at cruise, longer with fights and braking),
   with a line of about 1.2 km, two and a half times today's. Each ride adds one new thing. This was six rides and
   about 1.5–2 km in the first draft. On the shipped floor, three pairs of ring rooms sit side by side, so five intermediate stops
   always leave one ride of about 75 m. Five stops give five full rides.
3. **Wrong choices kill or maim.** Every live switch has a right branch and a wrong branch. The wrong branch is
   telegraphed and is deadly or badly hurts.
4. **Barricades that bite.** Timber barricades across the track must be hacked or shot down before the cart hits them.
   Smashing through hurts the cart and every rider.
5. **Crevasses are instant death.** Deep crevasses lie beside the track and across walkways. Falling in kills, and a
   train that falls takes everyone in it.
6. **A wipe puts you back on the train.** The party rises at the last stop reached, with a fresh cart waiting, in single
   player and co-op.
7. **Tricks:**
   - a dark tunnel;
   - a runaway ore train from behind;
   - a split track whose branches carry their own dangers;
   - a collapsing bridge over a crevasse;
   - a downhill spiral;
   - a loop-the-loop;
   - leaping from your cart onto an enemy cart.

## Non-goals

- Rails on any other floor. The new generator knobs default off, and every other floor generates byte-identical.
- Steering, physics-engine carts, new key bindings.
- Voice spend. New lines are subtitles until the owner OKs a Runware pass.
- Installers or dist builds, merging, pushing.
- Hardcore rules. Crevasse deaths follow the normal down-and-rise rules below, and the Death Clause works as today.

## Anti-goals (it fails even if it "works" when…)

- A death the player could not have seen coming. Every wrong branch, crevasse edge and barricade warns by sight and by a
  companion's call first. A coin-toss switch is a failure.
- A ride that drags. Forty to sixty seconds must be full, not stretched.
- A VR player feels sick, especially in the loop and the spiral.
- A wipe or a fall that strands the party or seals the stairs.
- Shipped content lost: the Oddfellows' camp, the vaults, the breaches, the Kiln Cages narrations, Grisel's two
  branches.

## Approaches

- **A. Full ring loop with a wider meandering band (chosen).** The line goes the whole way round in the rock band
  outside the ring rooms. On floor 12 only, the band is wider and the route weaves within it. The down stairs move
  beside the start. This uses the half of the map the line skips today, and it is the owner's "loop that completes a
  loop". Cost: room roles move on floor 12, so its plan is re-homed.
- **B. Keep the half ring, zig-zag harder.** Smaller, but it leaves the west half unused, which is the owner's
  complaint.
- **C. Run the line through the interior rooms and hub.** It covers the middle, but it cuts the halls that make walking
  the floor an option, and it fights every room-placement rule. Kept as a later option for one hub crossing.

**The loop-the-loop.** I read "a loop that completes a loop" two ways, and this design does both. The whole line is a
loop round the dungeon, and one ride has a roller-coaster loop-the-loop. Grid tunnels cannot hold a loop, so it is built
in a crevasse cavern, a tall open space carved for it. If the owner meant only the ring, the loop-the-loop plan is
dropped without touching the rest.

## The design

### The line (generator)

- **New spec knobs, defaults off:**
  - `RailLoop` (bool). The line runs the whole ring.
  - `RailBand` (int cells, default 6, today's band). Floor 12 sets a wider band.
  - The `RailCount` clamp rises from 3 to 6.
  - Floor 12's `SpecOverrides` sets all three.
- **Stairs:**
  - With `RailLoop`, the up stairs stay where they are.
  - The down stairs go in the ring room next to the start, on the far side of the loop. The line leaves the start one
    way, goes all the way round, and arrives at the Cooling Stair beside Kiln-Mouth Landing.
  - The two rooms stay separate. There is no walking shortcut between them unless the halls already join them.
- **Stops:** four intermediate stops, evenly round the ring, from eligible rooms under the existing `RailRoleOk` and
  platform rules. That makes five rides. No stop room holds the shrine.
- **Meander:** within the wider band the route weaves (switchbacks between ring rooms) so each ride is about 220–330 m.
  The rules for pockets, turns and twin lanes stay. The builder picks the method: forced waypoints, a cost on
  straightness, or another.
- **Crevasse cavern:** the generator carves one deep crevasse cavern on ride 4's segment, the one before the
  last. It is a large open chasm region with the track crossing and descending it. Its cells are a new `Cell.Chasm`
  kind: not walkable, a hole with no floor. Ride 5 also crosses a crevasse at the jump and the trestle.
- **Crevasse edges elsewhere:** chasm strips beside parts of the track, and cutting across some walkways in the halls.
  Every room, cage, key and the stair must still be reachable on foot (`FloorSolvability` with the rails and chasms
  blocked), and no chasm sits on a spawn, chest, key, objective or platform cell.
- **Map size:** the grid grows only by the band, so the ring's radius and room order hold. Because the stairs move, the
  room roles on floor 12 change:
  - Re-home `floor_12.json` to the new rooms: Start, Exit, the stop rooms and the narrations.
  - Refresh `LayoutHash` with `--rehash-plans`.
  - Sweep floor 12 for "repaired|dropped|demoted|adapted".
  - Only floor 12's hash may move.
- **Protocol** stays 17, because the branch is unreleased.

### The five rides (one new thing each)

1. **The Ore Run: learn the cart.** The lone cinder, the low beam ("Heads!") and the slag thrower, as today. Plus the
   **first barricade**, called out early ("Timber across the line — cut it!") and easy to hack.
2. **Side by Side: the enemy line wakes.** Slag carts, ember hounds and brake shoes, as today. Plus a barricade the slag
   riders defend.
3. **The Switchyard: choices that kill.**
   - `switch_out` (dodge the heap, cross to the enemy line, ram the slag cart), as today.
   - The enemy-line stretch is now long. **`switch_back` is live:** it is a real lever, offered seated, and the cart
     takes it only if it is thrown.
   - **The wrong branch:** stay on the slag line past the last return and it ends at a broken bridge over a crevasse.
     The train falls. Telegraph: a dead red signal lamp, a companion's "The slag line's out past the bridge — switch
     back!", and the bridge's broken end visible ahead.
   - The ore buckets, as today.
4. **The Dark and the Throat: outrun it into the cavern.** (Revised 2026-11-19 after measuring the shipped floor: five
   rides, not six. See the calls below. The dark and the runaway open this ride, and the cavern closes it.)
   - A stretch with every lamp out. Only cart lamps and sparks light it, and hounds leap out of the dark.
   - A **runaway ore train** comes down the party line from behind, bell ringing. If it catches you it rams the train
     (a big plating hit, and riders are thrown). Pump to stay ahead.
   - A **split track**: a live switch where one branch sends the runaway down a spur into a crevasse, and the other
     keeps it on your tail. Throw it after you pass, or ride the wrong branch yourself and pay for it.
   - Then the line breaks out into **the crevasse cavern**:
   - The line enters a huge chasm on a timber bridge that **collapses behind you**. Too slow and it goes under you.
   - A **downhill spiral** round the cavern walls. Brake in the bends or the cart tips and is thrown into the crevasse.
   - A **loop-the-loop.** Enter it fast enough or the cart falls off at the top. The entry speed is called out and
     a speed lamp shows green when you are fast enough.
   - A **leap to the enemy cart:** a slag cart runs beside you on the twin line. A seated player can jump across to
     fight its riders on it, and jump back before the slag line drops into the crevasse (see the calls below).
5. **The Pour: the climax.** The golem, hammers, pours, spur and buffer, as today. Plus:
   - the trestle and the jump are now **over a crevasse**;
   - a missed jump drops the whole train into it;
   - a barricade before the buffer.

### Barricades

- These are timber barricades nailed across a track. They are a new set piece placed by ride and `At`, on either lane.
- **Hack, shoot or spell them down.** They take a few hits and are hittable from the cart like the brake shoe. A
  companion calls each one early.
- **Smashing through:**
  - the cart loses plating (about 25);
  - every rider takes a hit (about 15% of max HP, scaled by the lethality setting);
  - splinters fly and the cart jolts.
- The rail-bound slag carts smash through barricades on their own line the same way.
- Low beams stay a near miss for seated riders.

### Crevasses and death

- **Falling into a crevasse kills.**
  - **Heroes:** a hero is **dead**. They are down with no auto-revive and no drag-free. They return when the party
    next reaches a stop, or on a wipe-rise.
  - **Crew and Grisel:** they are lost (the existing lost rules: lost crew do not block the stairs, and Grisel's escort
    quest fails).
  - **Enemies:** they die.
  - **Lethality:** none of this depends on the setting. It is instant death at every level.
- **A train that falls** (the wrong branch, a missed jump, a collapse, tipping in the spiral, stalling in the loop)
  takes everything coupled to it: riders, crew wagons and Grisel. It is a fall, not a wreck.
  - If a hero survives (for example on foot elsewhere), the existing spare-cart takeover or the relief cart applies.
  - If nobody is left standing, it is a wipe.
- **Walking:** chasm edges are visibly deep (a dark drop, mist, an ember glow far below), and walkway crossings get a
  narrow plank path or a detour. The halls stay passable with care.

### After a wipe

- In single player and co-op, the party rises **at the last stop reached**, beside a fresh cart (full plating, wagons
  re-coupled as saved), not at the entrance stairs.
- Remote players' heroes are placed too. Today `ClearRiders` skips them.
- The gold cost and the Scar curse stay as today.
- No spare is spent.

## Calls made after the builder check (mine; the owner may overrule)

- **What a fall costs.**
  - A wipe-rise restores the train as it was when it **last arrived at a stop**: crew alive, wagons coupled.
    The snapshot is taken on arrival, not at the wipe. Since a train that falls usually takes every hero, a wrong
    branch normally costs a restart from the last stop.
  - If a hero survives the fall (for example on foot), there is no wipe. The crew who fell are lost for good, under the
    existing lost rules.
- **Grisel never dies in a fall.** She is a story character with floor 17 content gated on her being forgiven and alive.
  - When her cart falls she catches the chains at the edge and is left down at the lip.
  - She can be revived there, as a thrown escort can today.
  - Her escort quest goes on.
- **Who works the deadly controls.**
  - **Switches are the player's.** Every live switch is offered in the seated window (plan 8). A companion calls it
    early and again at the last moment ("Switch back — now!"). Companions and hero bots never throw a deadly switch.
  - **Speed checks are shared.** Companions and bots pump for the jump and the loop entry, as they pump for the jump
    today. They brake in the spiral's bends, so a solo player is never killed by a station they could not have known
    was theirs. The player can still out-pump or over-brake them.
- **The leap to the enemy cart has no miss.**
  - "E: leap across" is offered only while a slag cart runs alongside within reach (about 2.5 m), so a leap always
    lands. It is a seat change onto the slag cart's deck, using the existing seat model.
  - **Who can leap:** human players only, not companions or bots.
  - "E: leap back" is offered while the slag cart is in reach again.
  - After the leap stretch the slag line drops into the crevasse, with a companion call and the slag cart's bell first.
    Anyone still aboard falls with it.
  - In co-op the leap syncs as a seat change, with the cart id and seat.
- **The loop-the-loop is its own plan, built last.** The owner may drop it before then. The cavern and spiral plan
  builds the free 3D track it needs (a helix is already off the grid), so nothing is wasted either way.

- **Five rides, not six (measured 2026-11-19).** On the shipped seed:
  - With five intermediate stops, the rides came out at 267, 206, 224, 75, 252 and 209 m. Two stops were neighbouring
    rooms only a few cells apart, which the meander cannot lengthen. Picking the stops differently gave the same
    result.
  - With four intermediate stops, the rides are 267, 206, 281, 252 and 209 m, about 1.2 km in all, and the cavern span
    still fits on the ride before the last.
  - So the dark and the runaway open ride 4, and the cavern closes it.

## Calls left to the builder (mine)

- **Numbers:**
  - barricade hits (3 for a sword, 1 for a heavy spell);
  - smash damage (25 plating, 15% HP);
  - the runaway's speed (cruise + 15%);
  - the loop's entry speed (about 8 m/s);
  - the spiral's tip speed;
  - the leap distance.
- **The ride table,** set pieces and `At` fractions, by what each seed's geometry allows.
- **How the loop and the spiral are posed:**
  - The track is a free 3D curve inside the cavern, with proper up vectors.
  - Riders are posed by the cart frame, upside down at the top.
  - The cart's physics gains a normal-force check for the loop.
  - **VR:** the view fades through the loop and on the spiral's steepest turns under the Recommended and Strong
    presets. Off skips the fade.
- **Crew count:** eight named crew of Grisel's, two per wagon across four stops. The two new names are written into
  `floor_12.json`.
- **Music:** the climax track plays on the last ride, worked out from the stop count, not "ride 4".
- **Debug flows:** the existing ride, crew, chase and net flows are generalised to any stop count.
- **Build order:**
  1. the full-circle line and plan re-home;
  2. crevasses, death and the wipe rise;
  3. barricades and live switches with deadly branches;
  4. the dark and the runaway;
  5. the cavern: collapsing bridge and spiral (free 3D track);
  6. the leap to the enemy cart;
  7. ~~the loop-the-loop~~ (dropped by the owner 2026-11-21).
