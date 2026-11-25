# Floor 12: the stop trap, the alcove shooters, and the frame rate

Owner's request, 2026-11-24, after a play test: the train is fun; the stops were "too easy". The owner wants each stop to feel like a trap: hidden hordes keep attacking while the party frees the prisoners, using the existing portal scheme ("we just need to make it more interesting and challenging at these areas"), and enemies jump into the train and drive it backwards up the track. More enemies should shoot from the alcoves beside the track as the train goes by. **The track does not change at all** ("you did amazing"); this is enemy behaviour only. The owner also reports a **constant low frame rate**.

The owner picked "Go, all of it" from a menu that offered: portals around the platform in waves while prisoners are freed, ambushers bursting from the stop walls, hijackers who roll the train backwards (if it gets away, the prisoners aboard are thrown off and a relief cart comes: option 1, catchable and recoverable), and shooters in the alcoves. Everything below marked **(call)** is the controller's decision, open to the owner's correction.

Recon: `.superpowers/sdd/stop-trap-recon/` (stops-portals.md, train-reverse.md, alcoves-perf.md). File:line references there.

## Goals

1. A stop is a fight you have to win *while* you free the crew: waves keep coming until the train leaves, each stop harder than the last.
2. Leaving the train unattended at a stop is a real risk: enemies climb in and roll it away backwards. You can run it down and win it back; if you don't, it costs you crew and time, never the run.
3. Rides gain trackside shooters in the alcoves, so there is something to shoot back at between the set pieces.
4. The frame rate is measured in the player's own game, and the biggest known CPU cost (every enemy's full tick at any distance) is cut.

## Non-goals

- No track, layout, pocket or LayoutHash change. No generator change. Floor 12's shipped seed and every pinned layout test stay as they are.
- No new voice spend: new lines are subtitles only (VoiceId null); sexes stay right (Sable a woman; Brand, Wren, Ilex men).
- No change to other floors' portal or ambush behaviour (they keep `PortalRules` as today).
- No change to the exit island (stop 5) or the landing (stop 0).

## Anti-goals (a failure even if it works)

- A stop that becomes a grind the party cannot leave: the gate still lifts when the planned guards die, and portal creatures never hold the gate.
- A hijack that strands the party or ends the run, or a train that rolls back into a hole, a pit or a live hazard.
- A train that moves for one co-op peer and not another.
- Frame rate made worse by the extra enemies: the stop siege must be measured, not assumed.

## Constraints

- Godot 4.7.2 .NET, C#/.NET 8, xUnit. Engine-free rules in `*Rules` classes with unit tests, as every rail plan has done.
- Server authority for everything that spawns, judges or moves; clients follow. The wire changes, so **Protocol.Version 17 -> 18** (the cart DTO gains a reverse/hijack field; `CartFlags` has no free bit).
- CC0 assets only; the repo is public.
- Run `dotnet build DungeonCrawler.csproj` before headless godot; engine flags before `--`, game flags after. Never `pkill -f` your own pattern. tests/ is not in git.
- The RIDE pass (`RIDE=1 scripts/net_test.sh`) is run only by the controller, alone, under `.superpowers/sdd/shared/RIDE.lock`.
- Every existing floor-12 test flow sets `NoPortals`/`NoAmbush`; they keep doing so, and the new behaviours get their own flows and flags (a flow opts in).

## 1. The stop siege (portals at the stops)

**Approach (chosen):** a *stop mode* on the existing `PortalSpawner`, switched on by the train's `Arrived` event at an intermediate stop and off by `Departed` (and by `Wrecked`/`OnFoot`/`Restored`/a floor change). Rejected: more planned stop guards (finite: the stop goes quiet once they die, so it never feels like a trap); a new dedicated spawner (duplicates the portal visual, sync and pacing). The portal visual (`ShowPortal`), its `PortalOpenedEvt` sync and the dynamic `EnemySpawned` path are reused as they are.

- **Which stops:** 1 to 4 (the cage stops). Not stop 0 (landing) or stop 5 (the island).
- **While a siege runs** the floor-wide pressure (portals behind the party) pauses; its clock keeps its saved value. One portal system, two modes.
- **Several portals at once.** Today `PortalSpawner` has one pending portal (`_portalAt`, `_pending`); stop mode needs each wave's portals to stream independently (a small list of pending portals).
- **Sites (call):** walkable, non-reserved floor cells in the stop room or within 3 cells of it (corridor mouths), at least 6 m from every up hero and at least 4 m from the cage and the platform's boarding side; spread so a wave's portals are at least 8 m apart; prefer cells out of the heroes' current view (behind them) when there is a choice. Never on rail, platform, pocket or ledge cells (`IsReserved` already excludes rail).
- **Waves (call, tunables in one `StopSiegeRules` table):**

  | Stop | First wave after arrival | Gap between waves | Portals per wave | Creatures per portal | Alive cap (siege) |
  |---|---|---|---|---|---|
  | 1 | 4 s | 14 s | 1 | 2 | 6 |
  | 2 | 4 s | 12 s | 2 | 2 | 8 |
  | 3 | 3 s | 10 s | 2 | 3 | 10 |
  | 4 | 3 s | 8 s | 3 | 2-3 | 12 |

  Co-op: +1 creature per portal for each human past the first, the cap +2 per extra human. `Settings.Reinforcements` 0 turns the siege off with the rest of the portals; 2 (heavy) shortens the gaps by a third. Waves continue until the train leaves; there is no last wave. Alive cap reached = the next wave waits.
- **Roster (call):** the stop's own kinds first: cinder imps (`horde_cinder`) and ember hounds about 70 %, forge priests about 25 %, a cinder brute at most one per wave from stop 3; never a forge golem (too slow to matter, too heavy for the cap). All `IsReinforcement` (little loot), `ForceAggro`.
- **They do not hold the gate.** The gate still lifts when the planned stop guards die (`StopGates`, unchanged). The siege is pressure, not a lock.
- **After the train leaves**, siege creatures left alive keep hunting on foot (they can be run over, can chase the train). Any more than 60 m from every hero and out of sight for 10 s are freed quietly (they would cost CPU for nothing).
- **Message log and barks (subtitles):** the first wave at each stop gets a log line ("The air tears open around the platform!"), later waves the existing "A portal tears open behind you." Grisel or a crew member calls a warning on the first siege (subtitle only, existing speaker voices for the text, VoiceId null).

## 2. Ambushers in the stop walls

- On arrival at stops 1-4, **2 / 3 / 3 / 4** ambushers (call) burst out of the stop room's walls 1.5 s after the first hero steps off (or 6 s after arrival if nobody does).
- **Placement:** seeded per floor (`seed ^ const`, built identically on every peer, like `AmbushRules.Place`): room cells next to a solid wall (rubble preferred), not reserved, not rail/platform, at least 4 m from the cage and at least 3 cells apart. `AmbushRules.Place` keeps its corridor-only rule for the floor-wide ambushes; the stop points are a separate list.
- **Burst:** the existing `AmbushSpawner.ShowBurst` (thud, shake, dust) and `AmbushSprungEvt`-style sync; spawned dynamic, `ForceAggro`, `RunBoost`.
- **Once per stop per floor visit:** saved with the floor like `SprungAmbushes`. A wipe-rise or a load at the stop does not re-arm them (call: the portals already make a re-fight hard).

## 3. The hijackers (the train rolls backwards)

**When it can happen:** the train is Held at stop 1-4 and **no hero is seated anywhere in the train** (cart or wagon). That is the trap: in solo the player must leave the train to open the cage. A hero seated aboard prevents it entirely.

**The hijacker:** each siege wave tags at most one of its creatures (a cinder imp or hound, never a priest or brute) as a hijacker. While the hijack condition holds it runs for the cart instead of the heroes; it still fights back if hit. Only one hijacker may hold the cart at a time; a second tagged one goes for a wagon instead and rides as an ordinary wagon boarder (the existing grip and knock-off), so that the train fills with enemies.

**Climbing in:** within 2 m of the cart's side it climbs in (0.8 s, a new **driver spot** on the cart, separate from the two rim spots). On a wagon it takes the wagon's existing boarder spot. These boarders live outside the ride's `_leaps` list (which `ClearRide` frees on every `Arrived`/`Departed`) or are spared by it.

**The brake comes off:** the driver calls and glows (the thrower's glow) for **3 s** (call), then the train starts rolling **backwards**, against the way of travel, with the cart still facing forward and the wagons, now leading, behind it on the line. It gains speed slowly: **0.5 m/s² up to 4 m/s** (call; below a hero's run speed so it can be caught on foot, quick enough that a slow party loses it). A crew bark ("They've got the cart!") and a log line ("A cinder imp lets the brake off! The train rolls back!").

**Winning it back:**
- A hero on foot beside the cart may strike the driver (on-foot blows always land, `BoarderRules.Strikes`), or board the rolling-back train: boarding the cart is allowed while it rolls back at up to 4 m/s (today only while Held).
- The driver has the boarder grip (3 hits, a spell all of it). Knocked off, it drops beside the lane, alive and hostile.
- With no driver aboard, the train brakes (6 m/s²) and then rolls **forward back to its stop** at walking pace (2 m/s, call) and is Held there again as it was: the gate's state, the cage holds and the boarding countdown work as before. Wagon boarders still aboard keep fighting as boarders.

**Getting away:**
- The roll-back has a limit: **40 m** back from the stop (call), shortened so it always stops **at least 5 m short of** any party-lane hole (the pit, a gap, a fallen trestle, ride 4's gone bridge span) or live hazard of the ride behind it (the ride-4 runaway still lingering, a standing heap). The plan measures each stop's limit on the shipped layout and pins it in a test.
- While it rolls back, the prisoners aboard are thrown off **one every 3 s** (call), rear seat first: set down beside the lane **alive** (not the lethal `ThrowCrew`; `GetOut`/`EmptyWagon`-style), where they get up and follow the party again. The ones still aboard at the limit are set down there.
- At the limit the train is **gone**: the driver and any wagon boarders leap off into the fight, and the train is hidden and uncoupled (it "rolls on into the dark"). After **12 s** (call) a **relief train** (a cart with fresh empty wagons, `SpareTakesOver`/`CoupleFresh` semantics) rolls in at the stop from behind and is Held there, with a log line and a bark. A hijack always gets a relief train, even if the stop's spare was used (it never strands).
- A hijack never kills crew, never counts crew as lost, and never undoes a freed cage. Its cost: thrown-off crew must walk back and re-board while the waves keep coming, and the time spent.

**Run-over in reverse:** the leading end is now the last wagon's back. Anyone on the rails behind the train is judged by the run-over rules from that end (at 4 m/s the existing speed threshold decides who is hurt or rolled over). The warning (`RailWarn`) looks the way of travel. A pocket stays safe.

**Cannot happen during:** a ride (no hijack while rolling forward), the countdown with a hero seated, a wreck, a wipe-rise, the island, or while the cart is the chaser/relief/spare of a co-op player.

**Save/load:** a load during a hijack puts the train back whole at the stop it was at (as `Restore` does today). The hijack state is not saved.

**Co-op:** protocol 18 carries the reverse roll (way of travel apart from facing, so `CartTrain.Pose`/`PoseAt` keep the wagons on the lower-S side and the cart unturned on every peer; the follower replays it) and the driver as a boarder. The RIDE pass covers: a hijack started at a stop with the joiner on foot, the joiner boarding the rolling-back cart and knocking the driver off, the return to the stop; and a got-away with the relief train, both peers agreeing on the train's place (≤ 1.5 m, as today's limit).

## 4. Shooters in the alcoves

- **Where:** the refuge pockets beside the track (`RailLine.Pockets`). **Enemy-lane pockets first** (about 8 m from the party cart: a clean shot across), party-lane pockets only where a ride has too few. Never a pocket within 30 m of a set piece, a switch's window, a barricade, the dark, the cavern or a stop; never the thrower's pocket; never a pocket a companion or crew member is using as refuge at that moment (a shooter is only placed in a pocket no warned body has claimed).
- **How many (call):** ride 1: 4, ride 2: 5, ride 3: 5, ride 4: 3 (crowded), ride 5: 4, placed in the quiet stretches the recon found (ride 1 about 0.5-0.75, ride 2 about 0.55-0.65, ride 3 about 0.4-0.7, ride 5 about 0.05-0.18; ride 4 wherever it fits). Seeded per ride, so every peer and every run agrees.
- **Who:** forge priests and bandit archers (call; the archer is the faster shot), with **lead aim** at a seated rider (aim at where the rider will be when the bolt arrives, from the train's speed), so side-on shots can hit.
- **Behaviour (a new pinned state):** spawned with the ride's arming, hidden at the back of the alcove (unseen, no ticking AI beyond a cheap distance check) until the train's front is within 35 m; then it steps to the alcove mouth and shoots at the nearest seated hero in line of sight every 1.6 s (wind-up as its kind) while within 25 m. It **never leaves the alcove** (never chases, never kites, never flees onto the rails); it can be shot, cut down from a seat if within reach, or killed on foot. When the train's back is 30 m past it, it stops and is freed with the ride.
- **Who it hurts:** any seated hero, cart or wagon (its bolts are physics hits, not a hazard loop, so the wagon-seat hazard exemption does not apply; call: a shooter is an enemy shooting at you, not a hazard). Crew are not targets.
- **Kinds:** a new rail encounter kind `alcove` in floor_12.json's RailEncounters (validator entry, `SpawnStander` case), so the counts and stretches live in data.

## 5. The frame rate

Facts (measured 2026-11-24 on the dev laptop, RTX 5070, 240 Hz): a windowed `--test-chase` (the flow kills every enemy as it appears) ran median 238 fps, worst one-second sample 35 at ride 4's start; a 10-creature horde on floor 3 held 240. The owner plays 1280x720 windowed on the same GPU and sees a constant low frame rate. An earlier server profile put two thirds of a tick in `Enemy._PhysicsProcess`. The recon found every enemy runs its full tick at any distance, with per-enemy-per-tick allocations (`Separation()` calls `GetParent().GetChildren()`; `Effects.Update` and `Retarget` allocate lists) and up to four sight rays per tick for idle and holding enemies.

1. **A performance line in the game's log** (every 10 s in game, always on, cheap): average and worst fps, the worst frame in ms, process and physics ms, draw calls, enemies alive and how many are awake, the floor and whether on a cart. Plus a single line for any frame over 50 ms. The owner's next session then shows where the time goes.
2. **An on-screen FPS counter** (Settings, Display: "Show FPS", off by default).
3. **Graphics quality** (Settings, Display): **render scale** 100 / 85 / 70 / 50 % with FSR upscaling, and **shadows** on/off (the local hero's torch is the only shadowed light). The existing Haze slider already turns the fog off at 0.
4. **An enemy tick diet** (server and solo; the cut must not change any behaviour a test pins):
   - `Separation()` stops allocating: it reads a shared, per-tick list of live enemies (or a spatial grid) instead of the floor root's children.
   - The per-tick allocations in `Effects.Update`, `Retarget` and `TickUnstick` are removed.
   - **Far and unaware enemies sleep:** an enemy that is unaware (idle/holding, not hunting), more than 40 m from every hero and not carried runs its sight checks and movement at a quarter rate (call: every 4th tick, staggered by NetId). Waking is unchanged in feel: anything that hurts, alarms or sees it wakes it at once.
5. **Measured, not assumed:** a new windowed perf flow on floor 12 with enemies alive (a stop siege running, a ride with alcove shooters), run on this laptop before and after the diet, recorded in the docs. Target: the siege at stop 4 holds at least 120 fps on this laptop at the owner's settings.

## Rulings after the builder check (2026-11-24, controller's calls)

1. **A load or a wipe-rise at a stop restarts the siege from that stop's first wave**, after a grace (10 s after a load, 15 s after a wipe-rise). Nothing of the siege is saved (no wave index, no clock). Reloading does not silence a stop, and a party that rises there starts against the soft first wave, never mid-ramp.
2. **In solo a hijack is a real threat at every stop, by design** (the player must leave the train to open the cage; companions are set down with them). To keep it fair: no hijacker is tagged in a stop's first wave; only one hijack is live at a time; after a hijack ends (won back or relief arrived) the next tag waits **20 s**. The defence is killing the runner before it climbs in, or catching the train. No new seat or companion-order work (a companion left aboard as a guard is not built).
3. **The departure waits for crew a hijack threw off**: until they are seated or dead, at most **30 s** after the train is Held at its stop again (or the relief train arrives). Their booking reach is **60 m** (the usual 25 m stays for everyone else). Crew who die on the way are lost, as any crew killed in a fight.
4. **The alcove shooters are placed by code, not by floor_12.json data** (an `AlcoveRules` table of counts and stretches per ride, applied on any railed floor with this ride count), so they reach existing saves as the siege, the wall ambushers and the hijack do. No validator change.
5. **The fps target is measured as:** the windowed perf flow, 1280x720, VSync off, Haze 0.5, render scale 100 %, on this laptop. If stop 4's siege misses 120 fps after the tick diet, lower its alive cap (12 to 9) before anything else, then record the gap in docs Known gaps. The perf log line is the real answer to the owner's "constant low": it shows on their next session whether the cost is enemies, the GPU, or something else.
6. **In co-op, a siege pauses the floor-wide portals for everyone** while it runs (one system, one cap; the siege is the stronger pressure). A player elsewhere on the floor during a siege is a rare case and gets no portals meanwhile.

## What is left to the builder

Exact class and file names, the per-tick list's shape, the reverse-roll's representation (a signed speed or a way-of-travel field) as long as facing and wagon order are kept apart on every peer, the flow flags, the stop-wall placement scoring, and how each tunable above is tested. Numbers marked (call) are first guesses; tune them by test and log them as rulings.

## Testing

- Unit: `StopSiegeRules` (waves, caps, co-op scaling, sites), stop-wall placement (seeded, rules), hijack rules (when it may start, roll-back limit per stop on the shipped layout, throw-off order, relief), reverse-roll physics (ramp, cap, brake, return), alcove placement (counts, exclusions), lead aim, the tick diet's sleep rule.
- Headless flows (new flags; existing floor-12 flows unchanged): the siege at a stop (waves come, cap holds, gate still lifts on the guards, siege ends on departure); the stop ambush; a hijack won back on foot; a hijack got away with the relief train and the crew re-boarding; alcove shooters firing at riders and staying in their alcoves; the perf flow (windowed).
- The RIDE pass gains the hijack steps (section 3, Co-op).
- The whole suite and `scripts/flows.sh` stay green; the RIDE pass ×2 at the end.
