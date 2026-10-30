# DungeonCrawler

First-person, real-time, party-based dungeon crawler. Godot 4.7 with C# (.NET 8).
Design and roadmap: see the plan (Phase 0 setup and Phase 1 gray-box loop are done).

## Toolchain (user-local, no sudo)

| Tool | Location |
|---|---|
| Godot 4.7.2 .NET | `~/.local/opt/godot/…`, symlinked as `~/.local/bin/godot` |
| .NET 8 SDK | `~/.dotnet`, symlinked as `~/.local/bin/dotnet` |

## Themes

**Tremors.** Now and then the ground shudders: the camera shakes, dust sifts from the ceiling around the party, a
few stones drop from above and thud onto the floor (matching the theme's stone, lying for a while before they crumble
away) under a rumble made in code (`Dungeon/Props/Tremor.cs`, `Audio/Synth.cs`). Every minute or three, a little more
often and heavier in the caves; a horde tearing out of a wall borrows the shake. `--tremor` sets one off on demand and
`--test-tremor` checks the stones fall, land and the camera shook.

A slight haze hangs in the air (volumetric fog, Forward+): thin enough to keep rooms readable, thick enough that
torches, braziers, campfires and the party's own light throw a glow into it. The Video settings have a "Haze in the
air" slider from off to thick; each theme's fog colour and density tint and scale it.

Every floor wears one of the looks in `data/themes/*.tres` (`ThemeDef`): floor, wall and ceiling textures with tints,
tile scale, ambient and fog colour, torch colour and lamps per room. Four looks per tier ship (for example the Crypt has
red brick, a white ossuary, a mossy catacomb and a rotten undercroft); a theme with `TierIndex -1` may appear in any
tier. Which look a floor gets is decided by the run seed with the theme weights, unless the campaign outline names one:
the planner is shown each tier's theme ids and descriptions and can set `Floors[].Theme`, so a floor written as
"drowned" can look drowned. The HUD floor label shows the theme name after the tier. Add a look by dropping a texture
set into `assets/textures/<id>/` (`scripts/fetch_polyhaven.py <id>` downloads a CC0 texture set and `scripts/fetch_polyhaven_model.py <id>` a glTF
model from Poly Haven, then run the import) and writing a `.tres`; `--theme=ID` forces every floor to it for screenshots.

**Floor shapes.** Besides its look, every floor has a shape (`LayoutKind`, `src/Dungeon/Generation/LayoutKinds.cs`):
*standard* rooms and corridors; *labyrinth*, a maze of one-cell passages with small chambers set into it (flat, most
dead ends pruned, a few loops); *ring*, a great rotunda in the middle (the arena on a boss floor) with rooms around it
on a ring corridor and spokes leading in; *mine*, narrow timbered galleries with dead-end drifts dug into the rock,
shafts to cellars and heaps of spoil; *flooded*, most rooms under knee-deep water; and *burning*, fires scattered over
the room floors that burn whoever stands in them (bodies path around them, the party may wade through) under a hot,
smoky sky. The run seed deals the twenty shapes so that floor 1 is ordinary, every shape turns up at least once, no two
floors in a row share one, and boss floors keep to standard, ring, flooded or burning; each tier favours the shapes
that suit it (crypts wind, caves flood and are mined, forges burn). The campaign outline may name one per floor
(`Floors[].Layout`, listed to the planner with a line on each), and `--layout=KIND` forces every floor to one shape.
Everything after room placement (vaults, verticality, structures, water, rubble, nests, spawns, traps) runs unchanged
on any shape, and the ordinary floor hashes exactly as before, so shipped plans still fit.

## Floor objectives

Eight of the twenty floors carry a trial beyond "find the stairs" (`Dungeon/Generation/FloorObjectives.cs`, dealt
from the run seed like the shapes: two per tier, never floor 1 or a boss floor, never two in a row, every kind at
least once, only where the shape allows; `Floors[].Objective` lets the campaign name one, `--objective=KIND` forces
one). All but the gas seal the down-stairs until they are met (`Stairs.LockReason` says so in the villain's name), and
the HUD's objective line (`ObjectiveLine.Compose`: the trial first, then a loose horde, the open quest, the
reinforcement count) tracks them. Each runs in a server-only `ObjectiveController` under the floor root, saves through
`FloorStateDto.Objective` (save v8), and reaches clients by `ObjectiveStateMsg` every couple of seconds and
`ObjectiveDoneEvt` when it opens the stairs; the dealt kind rides `FloorChange`/`Welcome` so peers build the same floor.

- **Jailbreak** (`Adventure/Objectives/JailbreakController.cs`, `PrisonerBrain`, `JailbreakRules`): three to five
  villagers wander the floor and run from the creatures (to the emptiest room farthest from the nearest one, never the
  jail). A creature that reaches one becomes its jailer, carries a key, and marches it to the jail, a dead end behind a
  key door with no key of its own; one or two start behind bars, one or two spare keys lie in the floor's vaults or
  beside locked chests. Open the door with a key or a pick, unshackle each prisoner the same way (a wandering one is
  rescued by talking); the freed follow the party, give XP, set `freed:<id>` and `jailbreak_freed_all` flags, and the
  last one opens a treasure vault and the stairs. A jailer killed mid-escort drops its key and its captive slips free.
- **Rising gas** (`ToxicController`, `GasRules`, `GasField`): poison rises from the low rooms, from the start room
  outward (45 s grace, 14 s a room, 40 s to thicken), the high ground last; corridors take their nearest room's time.
  The stairs move to the low room farthest from the start, behind a lever gate whose lever stands on the high ground
  when the room can be sealed. Standing in gas poisons once a second by danger (thin, thick, lethal); the minimap goes
  green and the air with it. The stairs are never locked: leaving is the objective; the clock is saved and replicated.
- **The ritual** (`RitualController`, `RitualRules`): a ritualist waits at an altar in a roomy shrine with three
  breaches (the nests favour it) and a lever by each. Talking to them begins a 90 to 150 s rite; waves stream from the
  open breaches (the tier's swarm mostly) and hunt the ritualist (`HuntNpcState`), turning on the party only when hurt
  or blocked. Pulling a breach lever drops rubble across its mouth; with every breach sealed a slower trickle comes in
  by the doors. The ritualist goes down at 0 HP, the rite pauses, anyone revives them with E once nothing hostile is
  within six metres; a party wipe starts the rite over. Reinforcement portals stand down while it runs. Done opens the
  stairs and the altar yields a chest roll.
- **The shifting vault** (`ShiftingVaultController`, `Dungeon/Generation/VaultPuzzles.cs`, `PatrolRerouter`): the
  stairs sit in a sealed room (the exit, another dead end, or a side room carved for them). Three to five levers in
  different rooms each flip a set of three to five named gates standing in corridors (Gate I, II...); the vault opens
  when the gates match a target. A plaque beside each lever says which gates it turns, a notice near the start says
  the setting. The puzzle is proven before it is accepted: from the start with the gates as they first stand, every
  state the party can reach (levers pulled, where it can walk) can still reach the target with the vault's doorstep in
  reach, so no pull ever traps the party. Patrols reroute to the rooms they can still reach whenever the gates shift.

## The dungeon's master

The campaign's antagonist runs the place. Objective floors open with their line instead of the narrator's, the sealed
stairs and the HUD prefix name them, the reinforcement portals are their doing, and they taunt the party as things
happen (`Adventure/Core/Objectives/Taunts.cs`: `TauntTrigger`, `TauntDirector`, `TauntBank`): a floor entered, a
prisoner caught or freed, gas thickening, a wrong lever, a wave, the ritualist down, a member down, a wipe, a trial
passed, the stairs taken, a party dawdling a minute on an objective floor. Never within four seconds of anyone else
speaking, twelve seconds of their own last line, or a minute of the same trigger, and a line said stays out for two
floors (`BarkRepeat`, saved with the barks). The campaign carries the lines (`CampaignPlan.Taunts`, two or three per
trigger, `FloorPlan.Taunts` for a floor's own); a planner writes them in the antagonist's voice, the offline designer
and any campaign missing a trigger take the stock bank (`CampaignRepair`). They may curse: every taunt has a
`CleanLine`, and "The dungeon's master may curse at you" in Settings picks which is shown (and, in multiplayer, each
client picks for itself: `NarrationMsg` carries both). The voice provider refusing a coarse line renders its clean
twin under the same id, so nothing goes silent. Taunts have a voice budget of their own (`VoiceBudget.Taunts`, 30 on
top of the campaign's 80).

**Lethality.** Every enemy attack and every trap, floor fire and gas the party suffers is multiplied by
`TierRules.Lethality` (Settings "Dungeon lethality": 1 normal, 2 hard, 3 brutal, the default; `--lethality=N` pins it
for a run). The party's own traps against enemies, fall damage and the enemy budgets a floor is planned with are not
touched; NPCs carry the same multiplier in their hit points so escorts, prisoners and the ritualist last as they did.
At 2.5 and above the lethal traps (below) kill a party member outright; under that they take a fraction of max HP.

**Stairs you climb.** The up-stairs are a real flight: seven stone treads between brick parapets rising to a landing
2 m up against the back wall; the down-stairs are a well sunk into the floor with the same flight descending to a
landing 2 m below (the well replaces the cell's floor slab and plinth, so a level-1 room's stairs sink into its
plinth). Both open toward the cell the party arrives on. Stepping onto the landing asks "Descend to floor N?" /
"Climb back to floor N?" with Yes (E / Enter) and No (Esc, or just walk back off the landing); sealed boss-floor stairs
only offer to turn back. Saying yes does what pressing E on the old stairs did, so servers, clients (an
`InteractIntent`), the smoke test and the net script are unchanged. `--ask-stairs` opens the question directly for screenshots. A capsule cannot climb a quarter-metre riser, so
each flight carries an invisible slope through the step nosings that the feet actually walk. Stair cells are blocked
for pathing, so enemies and companions do not squeeze between the parapets. `--test-stairs` walks the up flight, declines,
steps off and back, accepts, then walks the well down and descends.

**Vaulted ceilings.** Ceilings are not flat slabs: the vault springs from the top of the walls and rises into the
space above (the walls beside a corridor are 8 m tall, so nothing else changes). A corridor gets a barrel vault along
its run (dead ends too), and every room cell, corner and junction gets a groin bay, two barrels crossing so each of
its four edges is an arch that meets the next bay or a wall without a seam. The arch profile comes from the theme's
`Vault` field (`semicircular`, `pointed`, `segmental`, `catenary`, or `flat` for the old slab); an empty field takes
the tier default: round Crypt barrels, hanging Caves catenaries, low Forge segmentals, soaring pointed Abyss vaults. A
few themes override that for variety (the ossuary and sandstone caves are segmental, the kiln is round, cold tiles and
slate are catenary). Profiles and the piece rule are engine-free in `src/Dungeon/VaultRules.cs`; the meshes are built
once per theme in `DungeonBuilder.AddVaultItems` (12×12 grids, each web of a groin in its own smoothing group so the
ribs stay crisp, trimesh collision). `--vault=KIND` forces every ceiling to one profile; `--face-node=vault` stands at
the end of the longest straight corridor looking up its barrel and `--face-node=groin` looks up across a room's bays.
Everything that used to sit under the flat slab follows the vault: a ruin's columns rise to the crown of the bay above
them, tremor rocks fall out of the vault, and where a ruin's ceiling gave way the cell's ceiling piece is swapped for
a holed variant (items 23–25) so the crown is really missing, with a dark throat hung above the gap, broken voussoirs
on its wall and stone still clinging to the rim (`Rubble.Collapse`; `--face-node=collapse:N` looks up at one). Under
a `flat` theme the old black disc stands in for the hole.

## Viewer characters

Characters rigged in the 3dObject viewer ("Export for Godot") go in `assets/models/viewer/` under the file names in
`docs/superpowers/specs/2026-10-22-character-briefs.md`, then `godot --headless --path . --import` (or open the editor
once) writes their `.import` files. A missing file never breaks anything: the slot keeps its stock model or stays empty.

- Party bodies by class: `brand.glb` (warrior), `sable.glb` (rogue), `wren.glb` (ranger), `ilex.glb` (mage); `Adventurer.ClassViewerBodies`. Untinted, Walk/Run paced to the ground speed, weapons hung from `RightHandGrip`.
- Fighters who can be asked to join, a few steps from the entrance of their floor: Foehammer (1), `tamsin_rook.glb` (6), `oskar_vell.glb` (11), `sister_ione.glb` (16); `DungeonBuilder.ViewerFighters` holds their floors, weapons and lines.
- Floor 1 showcase: the orc `orc1.glb` to try fighting. Foehammer's dialogue offers a moves grid to try each clip.
- Everyone else wears one of the eight archetype bodies, chosen by `NpcBodies` from who they are: a Jailbreak prisoner is
  always `npc_prisoner.glb`; otherwise the NPC's name, persona and (for a cast member) campaign role are read for words
  such as alderman or merchant (`npc_merchant`), deacon, father, brother (`npc_priest`), guard, soldier, sergeant
  (`npc_guard`), prisoner, captive, survivor (`npc_prisoner`), and failing those for sex and age (`npc_old_man`,
  `npc_old_woman`, `npc_young_man`, `npc_young_woman`; with no clue at all, a plain body picked by the NPC's id). In the
  shipped campaign Maud is the old woman, Crane the merchant, Tobit the priest, Grisel the prisoner and Ulf the old man.
- The villain in person: on the last boss floor the tier's boss wears `verger.glb` (scaled to 2.25 m) when the campaign's
  antagonist is a verger (`NpcBodies.Villain`); other campaigns keep the creature.
- All sixteen bodies from the briefs are in (party, fighters, archetypes, the Verger).
- The Oddfellows' camps: `npc_bandit.glb` is the bandit (sword) and the bandit archer (crossbow), `npc_large_bandit.glb` the
  bandit chief (two-handed axe); set in `data/enemies/bandit*.tres` through `Model = "viewer/..."` and `Weapon`, the weapon
  scaled by `GripMath.HeldScale`. `--face-node=camp:3` stands the camera off a floor's camp for a screenshot.

Screenshots: `godot --path . -- --seed=11 --floor=6 --face-node=Npc_fighter_tamsin:1 --screenshot=/tmp/t.png --after=4`
stands the party a cell from a fighter; `--possess=1 --face-node=Brand:4` puts a party member in front of the camera
(the rest wait behind it).

## Assets

Real materials and models are CC0: Poly Haven PBR textures per tier (triplanar on the GridMap boxes) and props
(treasure chest, wooden wine barrels and crates as breakables, wall lanterns, statue), Quaternius Ultimate Monsters for enemies and Quaternius RPG Characters for the four party members (animated glTF,
scaled to size and lightly tinted; the possessed character's own model is hidden so it never blocks the camera), and Kenney sound packs for effects. Everything is listed with its source in
`assets/LICENSES.md`. Ambience is generated in code (a low hum plus filtered noise, coloured per tier). Music is CC0 from
OpenGameArt, fetched by `scripts/fetch_music.py` into `assets/audio/music/` (about 49 MB): a tavern tune for the title screen,
three exploration pieces per tier (Crypt, Caves, Forge, Abyss) shuffled with a few seconds of quiet between them, three battle
tracks, a boss track, and victory and defeat stings. `Audio/MusicDirector.cs` crossfades between them: battle music starts the
moment an enemy is fighting the party within 26 m and holds for six seconds after the last one stops, the boss track runs from
the guardian noticing you until it falls, and everything ducks under spoken lines. The "Music and ambience" slider covers it all.
After adding files under `assets/`, run `godot --headless --import --path .` once so Godot imports them.

## Build, test, run

```bash
dotnet build DungeonCrawler.csproj          # game assembly -> .godot/mono/temp/bin/Debug
dotnet test tests/DungeonCrawler.Tests.csproj   # engine-free logic (generator, rng, ...)
godot --path .                              # play
godot -e --path .                           # open the editor
```

Headless checks:

```bash
godot --headless --path . --quit-after 60          # boots and builds floor 1
godot --headless --path . -- --smoke               # scripted fight, loot, stairs 1->2->3->2, prints a REPORT line
godot --headless --path . -- --test-save           # save, mutate, load, compare; prints RESULT match
godot --path . -- --test-drag                      # synthetic inventory drag-and-drop through the GUI
godot --headless --path . -- --test-depth          # traps, doors, levers, pathing, boss stairs, victory, endless
godot --headless --path . -- --test-skills         # learn chains, upgrades, multishot, respec, save round trip
godot --headless --path . -- --test-vertical --seed=5
godot --path . -- --seed=3 --test-stairs                                   # climb the up flight, get asked, decline, accept, then take the well down (needs a window: the question is HUD)   # climb a ledge, fall into a cellar, open a secret, cross-level chase
godot --headless --path . -- --test-party          # companions follow, possession switch, companions kill an enemy, auto-revive
godot --headless --path . -- --test-music          # exploration track plays, battle music on aggro, back to exploration after the kill, tavern tune in the menu
godot --headless --path . -- --test-story          # Investigate clues, a Decide flag, a Betrayal that spawns the turncoat's creatures, a flag-gated NPC on the next floor
godot --headless --path . -- --test-path --seed=11 --floor=3   # enemies reach the party without line of sight
godot --headless --path . -- --test-plan            # offline designer: plan 20 floors, validate, round-trip JSON, build one and compare
godot --headless --path . -- --test-collectives     # guard leash and return, patrol, alarm runner, regroup; fixed seed 4242 unless --seed
godot --headless --path . -- --test-quest           # one NPC per quest template, played through dialogue to completion, saved
godot --path . -- --floor=3 --talk --screenshot=/tmp/talk.png --after=2   # open the first NPC's dialogue (add --press=quest_log for the journal)
godot --path . -- --seed=1 --floor=1 --talk --showcase=Death --screenshot=/tmp/move.png --after=3   # floor 1's fighter: open his dialogue and play one of his model's clips from the moves grid
godot --headless --path . -- --test-ai-flow         # stand-in planner: New Game card, prefetch, wait for a plan, timeout fallback, memory summary
godot --headless --path . -- --builtin              # play the shipped campaign (data/adventures/builtin), no key needed
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-kill-barks=assets/voice/builtin   # render every party member's and fighter's kill line into the shipped bank (RUNWARE_API_KEY in the environment), then run the import
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-cast-voice=assets/voice/builtin   # render every line of the recurring cast and every boss line (outside the 80-line budget) into the shipped bank; the plans with their ids land in assets/voice/builtin/cast-plans, to merge into data/adventures/builtin by hand, then --import
godot --headless --path . -- --test-hazard=press    # floor 1, lethality 3: an enemy and the lead in the run, crushed and mangled, no auto-revive, a companion pulls the lead free, the villain gloats, a barrel jams the second run, saved, loaded, phase and kills survive
godot --headless --path . -- --test-hazard=spikes   # a pressure bed clicks, raises, impales the lead and an enemy, is rescued; a periodic bed keeps the floor tick's phase; a jumping body is missed
godot --headless --path . -- --test-objective=KIND  # floor 2 with that objective (toxic, shiftingvault, ritual, jailbreak): stairs sealed, the villain speaks, driven to its end, stairs open, saved; --objective=KIND forces a kind on every eligible floor, --lethality=N pins the damage multiplier
godot --headless --path . -- --purge-voice          # sweep assets/voice/builtin of lines the shipped campaign no longer refers to, and user://voice of lines no campaign, run or save slot refers to; every bake does the same for the bank it wrote unless --keep-old-voice
godot --headless --path . -- --check-voice-bank     # every voice line the campaign on disk refers to: how many the shipped bank holds and how many load; run it on an exported binary (build/linux/DungeonCrawler.x86_64 --headless -- --check-voice-bank) to prove the bank was packed
scripts/net_test.sh                                  # dedicated server + client over WebSocket: join, same floor, movement echoed, chat, the Press on floor 2 replicated, leave (the server runs at lethality 1 with --hazard=press@2)
godot --headless --path . -- --bake-campaign=data/adventures/builtin --ai --seed=N   # write a campaign + 20 floors with Claude (or --fake-ai=0 for the offline designer)
godot --path . -- --ai --record-plans=user://ai/log # force the Claude planner (needs a key in Settings); requests/responses land in that folder
godot --headless --path . -- --replay-plans=DIR     # answer planning calls from a recorded folder instead of the network
godot --headless --path . -- --test-floors --seed=N # walk-off detector: steps from every surface of floors 1-6 (secrets opened) and reports any step onto nothing; --builtin for the shipped campaign
godot --headless --path . -- --builtin --floor=N --inspect=X,Y   # print a cell's 7x7 neighbourhood: cell type, level, grid items, ray hits
godot --headless --path . -- --test-structures                    # picks a seed whose floors 1-4 hold a gate, a camp, a pond and a stream; checks braziers, hut walls, bandits at the fire, water beds, 45° corners, wading
godot --path . -- --seed=2 --floor=2 --face-node=Camp_:3 --screenshot=out.png   # stand N cells back from a node (Gate_, Camp_, Water_, Barrel0, Enemy...) and look at it; --face-cell=X,Y:N for a grid cell; --face-node=chamfer:N for the first cut corner, --face-node=buckled for a heaved tile, --face-node=bridge for a stream's bridge, --face-node=vault / groin for the ceiling (add --vault=pointed etc. to force a profile)
godot --headless --path . -- --test-keys                          # a key rebind survives a settings reload without pressing Back
godot --path . -- --test-horde --screenshot=out.png                 # floor 3 nest room: stream, kill, spent; the screenshot is taken mid-stream
godot --path . -- --drop=item.iron_key,item.health_potion,item.longsword --screenshot=out.png   # lay sample items in front of the party (every 2nd magic, every 3rd rare)
godot --headless --path . -- --test-ambush          # floor 2: the first corridor ambush springs when stood on, fights, saves as sprung
godot --headless --path . -- --test-portal          # floor 1: hurry the reinforcement clock; a portal opens behind the party, creatures hunt, a dead tier boss silences it
godot --headless --path . -- --test-voice           # tone synth: intro line rendered to user://voice, played, rendering paused during a fight
godot --headless --path . -- --bake-campaign=data/adventures/builtin --bake-voice=assets/voice/builtin --ai --seed=N --spend-cap=10 --voice-budget=80 --quit-after=5800   # render the budgeted lines with Gemini into the shipped bank
godot --headless --path . -- --bake-campaign=DIR --replay-plans=data/adventures/builtin/authored --seed=7319 --story-brief=docs/superpowers/specs/2026-10-22-campaign-gullet-of-gorm.md --voice-budget=0   # rebuild the shipped campaign "The Gullet of Gorm" from Claude's recorded answers, no network; with --bake-voice=assets/voice/builtin and a voice key it renders the lines too. A replay with a missing answer writes NAME.request.json and stops (strict replay): write NAME.response.json and run again with --bake-resume
godot --headless --path . -- --bake-campaign=DIR --bake-voice=assets/voice/builtin --trace-voice ...   # --trace-voice logs every voice job the renderer starts (id, model, voice, style, text); the bake's pick listing also marks each line cached or render. Run `godot --headless --path . --import` after a voice bake so the new wavs load in the game
godot --headless --path . -- --bake-campaign=DIR --ai --seed=N --story-brief=FILE   # --story-brief=FILE attaches an author-written campaign bible (markdown) to every planning call as a cached system block; the outline is told to follow it and each floor is told to use its puzzles and lines. The Gullet of Gorm was baked this way from its bible in docs/superpowers/specs for $5.82 (23 calls, Opus 5, two floors retried after malformed JSON). The previous campaign, "The Toll of Ashmere", lives in data/adventures/ashmere with its hand-written answers
godot --headless --path . -- --bake-campaign=data/adventures/builtin --bake-resume --ai ...   # pick up a bake that stopped (out of credit, a crash): the outline and floors already written are read from data/adventures/builtin.partial
godot --headless --path . -- --list-voice-models                    # which Gemini text-to-speech models the stored key may call
godot --headless --path . -- --probe-voice-model=gemini-3.1-flash-tts-preview   # render one line with a model: proves the request and the quota
# --voice-model=NAME and --voice-fallback=A,B override the Settings values for one run
godot --headless --path . -- --voice-provider=runware --probe-voice-model=xai:tts@0   # the same probe through Runware (--voice-provider=runware also lists the mapped TTS models with --list-voice-models)
godot --headless --path . -- --planner=runware --probe-planner        # one tiny structured call through the selected planner provider (Claude direct without --planner=runware)
godot --headless --path . -- --planner=runware --planner-model=anthropic:claude@sonnet-4.6 --bake-campaign=... --ai   # bake through Runware instead of Anthropic directly
# CLAUDE_API_KEY, GEMINI_API_KEY and RUNWARE_API_KEY in the environment override user://keys.cfg for a run and are never written to it
godot --path . -- --floor=15 --face-boss --screenshot=/tmp/boss.png --after=3   # jump to a floor

Exports (templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono`; presets in `export_presets.cfg`):

godot --headless --path . --export-release "Linux" build/linux/DungeonCrawler.x86_64
godot --headless --path . --export-release "Windows" build/windows/DungeonCrawler.exe
godot --headless --path . --export-release "macOS" build/macos/DungeonCrawler.zip   # universal, ad-hoc signed, not notarized
scripts/export_all.sh            # all three of the above after a Release build (add --server for the dedicated server bundles too); first copies your latest rebake (user://adventures/builtin and the voice lines it uses) into the project so the build plays it; --no-ship exports the repository's copy as it is, BAKE_DIR=... names another user data folder
godot --path . -- --face-enemy --screenshot=/tmp/shot.png --after=2   # rendered screenshot (--open-map for the full map)
```

## Controls

You possess one of the four characters at a time (the local body); the other three follow and fight on their own.

| Key | Action |
|---|---|
| W A S D, Shift | Move, sprint. Holding forward for two seconds sprints on its own until you let go |
| Space | Jump (clears a pit at a sprint) |
| Ctrl (hold) | Crouch: slower, a lower eye, and enemies only notice you from 60% of their usual range |
| Mouse | Look |
| Left click | The lead uses their active ability. Tap = light swing, hold and release = heavy (release at 0.6-1.0 s for a perfect blow) |
| 1 / 2 / 3 / 4 | Take control of that character (camera snaps to their eyes) |
| F | Take control of the next living character |
| Right mouse (hold) | Guard (lead only, blocking classes). Absorbs 75% of a hit for 12 stamina; no stamina regen while guarding |
| Q or mouse wheel | Cycle the lead's active ability |
| R | Cycle every companion's stance: Defensive, Aggressive, Hold |
| Portrait buttons | "Control" possesses that character; the small button cycles their stance |
| Perception | Hidden pressure plates, loose seams in walls and the locked doors of vaults (a stretch of ordinary wall until someone makes out the lock plate or the gate's seams) are spotted by whichever member has the Perception for the distance: rangers (+40) from across the room, rogues (+25) a few steps out, mages (+15) and warriors (+0) about a step short. A spotted plate is a dull slab of slightly wrong stone and a seam a hairline crack; look at them for the prompt |
| Inventory icons | Bag and paper-doll cells show the item's own model, rendered once per item kind into a small texture by a hidden viewport (`Items/ItemIcons.cs`) and cached for the session; stack counts overlay it. Headless runs keep the old coloured squares |
| Inventory DROP / DESTROY cells | Drag a bag item onto DROP to leave it on the floor (companions leave it alone) or onto DESTROY to be rid of it for good; quest items cannot be destroyed |
| C | Character sheet (spend stat points) |
| K | Skill tree (spend skill points). F switches character |
| Tab or I | Inventory: drag to move or equip, double-click or right-click to equip, unequip, or drink. Drag to DROP to throw away |
| E | Interact (pick up loot, use stairs, open doors and chests, disarm traps, push secret walls) |
| M | Toggle full-floor map (corner minimap is always on) |
| J | Journal: quests on this floor and the rest of the run |
| 1-4 in a conversation | Pick that reply (Esc leaves) |
| Esc | Pause menu: Resume, Save, Load, Settings, Main Menu, Quit |
| F5 / F9 | Quicksave / quickload |
| F1 | Restore party (debug) |
| F2 | Kill all enemies (debug) |
| F3 / F4 | Next / previous floor (debug; single player only, a client cannot leave the server's floor) |
| F6 | Screenshot to `user://` (debug) |
| F7 | Roll six chest items into the bag (debug) |
| F8 | Give 20 arrows (debug) |

The possessed character's weapon is visible in first person (`Party/FirstPersonWeapon.cs`): it bobs as you walk,
swings on light and heavy attacks, draws back for bow shots, lifts for spells, and rises while guarding. It swaps when
you equip a different weapon.

Companions (`Party/Adventurer.cs`, decisions in `Characters/Companions/CompanionBrain.cs`): they keep a formation
behind the lead (melee classes close, ranged and casters further back), path through corridors and across levels,
teleport to the lead if lost for five seconds, drink a shared potion below 30% health, pick up loot they pass and
open chests they reach, and fight by stance. Defensive (default) engages enemies that are already fighting the party
or whatever the lead attacks; Aggressive engages anything seen within 12 m; Hold stays put and only fights what
reaches it. Ranged characters keep about 7 m away and back off when something closes in. They cast party buffs when
missing, novas when two or more enemies are close, and their strongest ready ability otherwise. Enemies pick their own
target by recent damage and proximity. A character at zero HP is down until no enemy has fought the party for three
seconds, then gets up at 1 HP; if the lead goes down, control passes to the next living character.

## Story: cast, mystery, choices

A campaign has a recurring **Cast** (four to six people with a role and a secret), a **Mystery** (a question, its hidden truth, clues spread over the descent, a payoff floor) and per-floor **Story** beats; about eight of the twenty floors carry the plot, the rest get one line of narration. Quest templates: KillTarget, Recover, Escort, HoldOut, and three story kinds: **Investigate** (clues lie in two to four rooms; bring them back), **Decide** (a dialogue choice sets a flag the run remembers and completes the quest), **Betrayal** (an NPC with a `Betrayal` turns when you take a `Betray` choice: they vanish, their creatures stand where they stood, the first wearing their name, and killing them completes the quest). NPCs, quests, dialogue choices and narration can carry `RequiresFlag` / `ForbidsFlag`, so a floor can hold both branches of an earlier decision and only the matching one appears. Flags live in the campaign memory (saved) and reach the planner in the memory notes.

Two hand-authored hooks the designer is never asked for (both `[AiIgnore]`): an NPC with `"Ritualist": true` on a Ritual
floor leads the rite instead of a stock-named villager (placed beside the altar, no script needed; the validator clears
the flag with a repair note on a floor with no shrine), and a boss with `HalfLine` speaks once at half health, while
`AltDeathFlag` with `AltHalfLine` / `AltDeathLine` swaps its middle and last words when that flag was set earlier in
the run. The shipped Gullet of Gorm uses both: Grisel Thorn's Unswearing on floor 7, and the Verger's half-health and
death lines on floor 20 turning on `wake_gorm`. The recurring cast is voiced in full (see `--bake-cast-voice`). Maud Sorrel also stands on floors 1, 6 and 11
(the bible's cameos) with a few lines and no quest.

Voice is budgeted: `Adventure/Core/Voice/VoiceBudget.cs` picks at most 80 lines of a baked campaign to render (tier entrances and boss lines, then story floors' intros, NPC dialogue and quest lines, quiet floors' intros, barks last); everything else stays a subtitle. `--voice-budget=N` changes it for a bake.

## Claude as dungeon master (optional)

Practicalities learned baking with the real API: the plan schemas are large, so structured output is tried first and, when the API refuses the compiled grammar, the same request is repeated with the schema as text and the reply parsed leniently (unknown enum names fall back, malformed JSON gets one retry); the outline needs a 48k-token limit because adaptive thinking counts against it; a full twenty-floor bake with Opus 5 is roughly $4 to $6, and the API stops with "credit balance is too low" when the account runs dry, so the bake writes each floor to `<dir>.partial` as it lands and `--bake-resume` continues from there.

New Game never plans anything: it plays the campaign on disk (your rebake under `user://adventures/builtin` if you
made one, else the shipped `data/adventures/builtin`), or, with neither, a run the offline designer writes on the spot.
In Settings the Runware planner model and both Runware TTS models are drop-downs ordered dearest first: the planner
list is fetched from Runware with your key (prices per million tokens; the shipped list of 2026-09-07 stands in until
then, "Refresh the list from Runware" fetches again) and the TTS list is the models the game has voice banks for, with
Runware's listed price per thousand characters. A model saved by hand that is not in the list shows as "(as typed)". That list is the one Runware's OpenAI-compatible
chat endpoint serves, and Runware's own task endpoint (the planner's first choice, with async delivery for long plans)
refuses a third of it with `invalidModel` (DeepSeek V4 Pro, Kimi, GLM 5.2+, GPT-5.1/5.6, Qwen, several Gemini
Flash versions on 2026-09-07); the client then re-sends the request through the chat endpoint, remembers the model
as chat-only for the session, and prices the reply from the catalogue since that endpoint reports no cost.

The title screen has no way to plan a fresh run: a new AI-written campaign is only ever made from Settings ("Rebuild
campaign with Claude/Runware"), which writes it to `user://adventures/builtin` so that New Game plays it from then on.
Paste a Claude API key in Settings (it is stored in `user://keys.cfg` on this machine and only ever sent to
`api.anthropic.com`) and the rebuild asks Claude Opus 5 for a twenty-floor campaign: title, synopsis, an antagonist,
a faction and a story beat per tier, a mood, a shape and generator knobs per floor, plus the full plan for floor 1. From then on each floor is written in the background while
you play the previous one (`AdventureDirector.Prefetch`); if you outrun it the game waits up to the "Wait for a floor
plan" setting and then designs the floor offline. Every reply is structured JSON against a schema generated from the
plan classes (`Adventure/Core/Ai/PlanSchemas.cs`), validated and repaired like an offline plan, and logged with its
request under `user://ai/log`. A per-run spend cap (default $15; a full Opus 5 bake with a story brief is about $6) switches the run to the offline designer when reached. Note that a replay from recordings also counts the recorded usage against the cap, so pass --spend-cap=15 or more on replay bakes too.
The planner also hears a memory summary of the run so far (play style, difficulty, quests and NPC fates) and a bark
bank is written once per campaign. Without a key, or with the option off, the offline designer writes everything.
The New Game entry plays the shipped `data/adventures/builtin` campaign, produced with
`--bake-campaign`; the copy in this repository was baked with the offline designer. Settings has two buttons under
"Built-in campaign": "Rebuild campaign with Claude" plans a fresh twenty-floor campaign with your key (and renders
its voice if a Gemini key is set), and "Render voice with Gemini" voices the current built-in campaign. Both work
from the title screen only and write to `user://adventures/builtin` and `user://voice`, which the game prefers over
the shipped copy (the title screen then says New Game plays "your rebaked campaign"). To ship a rebake, copy those JSON files into
`data/adventures/builtin` and the wav files into `assets/voice/builtin`. `--test-bake-ui` exercises that path with
stand-ins; `--open-settings` opens the panel for a screenshot.

## Runware as a second provider (optional)

Both jobs can run through [Runware](https://runware.ai) instead: one key, `api.runware.ai`, any model in its catalogue.
Paste a Runware key in Settings, then pick "Campaign planner runs on: Runware" and/or "Voice runs on: Runware". The
existing Claude and Gemini paths are untouched, and the provider choice is saved in `settings.cfg` (`planner_provider`,
`voice_provider`); the key sits beside the others in `user://keys.cfg` (or `RUNWARE_API_KEY` in the environment).
"Test Runware key" asks for the account details without running a model and shows the balance (the live reply
carries `balance` as a plain number, not the documented object; both are read). "List the text models this Runware
key can use" fetches `GET /v1/models`, which returns slugs (`anthropic-claude-sonnet-4-6`); the planner model field
takes either that form or the AIR form (`anthropic:claude@sonnet-4.6`) and converts slugs on the wire
(`RunwareWire.ToAir`). Verified live on 2026-09-07 with the ScenePlay key: `--planner=runware --probe-planner`
(0.04 cents) and `--voice-provider=runware --probe-voice-model=xai:tts@0` (4 s of 48 kHz mono WAV with a
streaming-style header, $0.001).

Planner: `Adventure/Ai/RunwareTextClient.cs` sends one `textInference` task per planning call
(`Adventure/Core/Ai/RunwareWire.cs`): the system blocks as `settings.systemPrompt` (Runware messages take only user
and assistant roles), the schema appended to the user turn as text (the mode the direct Claude client falls back to for
these schemas; `jsonSchema` is not accepted by every routed model), `settings.thinkingLevel` (`off` for Claude
models, the nearest fixed level for others; dropped and retried once if a model rejects it), `includeUsage` and
`includeCost`. Thinking is off for Claude on purpose: measured on the real outline request, `max`, `high` and
`adaptive` each let Sonnet 4.6 draft the whole plan inside its thinking until all 48,000 tokens were spent
($0.75, fifteen minutes, empty text, `finishReason: length`), while `off` returned a complete campaign in 10k tokens,
$0.18 and 154 s. The direct Anthropic client keeps adaptive thinking; only the Runware path behaves this way. Tasks go with
`deliveryMethod: "async"` and are polled with a `getResponse` task (2 s growing to 10 s, up to 15 minutes): a
synchronous reply is cut off after about two minutes with `failedTaskTimeout`, which is what the first Runware bake
hit on the outline and floor calls, while an async task was seen running 250 s to completion. A model that rejects
async delivery falls back to sync for the session. The planner reads the dollar cost
Runware reports (`ClaudeUsage.CostUsd`) instead of the Claude list prices, so the spend cap counts real money on any
model. The model is a Settings text field ("Runware planner model", default `anthropic:claude@sonnet-4.6`; e.g.
`google:gemini@3.5-flash`, `openai:gpt@5.4-mini`, `deepseek:v4@flash`). The requests still log under `user://ai/log`.

Voice: `Adventure/Ai/RunwareTtsClient.cs` sends one `audioInference` task per line asking for WAV as base64, then
`PcmTools` decodes it (any channel count, 8/16/24/32-bit or float) and resamples to the cache's 24 kHz mono. Runware
fronts several TTS providers with their own voice names, so `Adventure/Core/Voice/RunwareVoices.cs` maps the game's
Gemini names per model family: xAI (`xai:tts@0`, the default, about $0.015 per thousand characters), Inworld
(`inworld:tts@2`), MiniMax (`minimax:speech@2.8`), and Google's Gemini TTS through Runware (`google:gemini@3.1-flash-tts`,
same names and it hears the delivery style; the others would read the style aloud, so it is dropped for them). Any
other model gets the Gemini name as typed. Main, minor and fallback models are separate Settings fields for Runware
(`--voice-model=` and `--voice-model-minor=` set both providers' fields for a run). Line ids include the model, so
switching provider renders afresh. A 402 (balance used up) stops rendering for the session; 429/503/504 back off like a
Gemini per-minute limit. The "voice requests per minute" pace is Google's problem, not Runware's, so Runware renders at
full speed (two lines at a time): the first Runware bake crawled at 8 lines a minute for ten minutes because the pace
still applied, which looked like a hang. ElevenLabs is no longer on Runware and there is no OpenAI TTS there.

The dedicated server takes `RUNWARE_API_KEY` beside `CLAUDE_API_KEY` (`deploy/start.sh`); with only the Runware
key set it starts with `--planner=runware`.

## Voice (optional)

**Old audio is swept.** A bank file's name is its line's content id, so a rebake or a changed delivery style leaves the
previous audio behind as dead weight. Every bake ends by sweeping the bank it wrote for (`VoiceBankSweep`,
`VoiceRefs.Ids`): the shipped bank keeps only the ids the shipped campaign refers to, and the user cache keeps only
the ids the built-in campaign, the run in progress or any save slot still needs. `--purge-voice` does the same from
the command line, Settings has a "Clear unused voice lines" button that shows what it would remove before it does,
and `--keep-old-voice` tells a bake to leave the bank alone.

Every character sounds like someone (`Voice/VoiceLines.cs`: `Delivery`, `Persona`, `Voices.Style`). The style the
synth is told is composed from the speaker's persona (VoiceStyle: a few words on the voice and manner; DefaultDelivery;
Voice: a name from `Voices.Catalog`) and the line's own `Delivery` (whisper, weary, calm, warm, cheerful, sly,
pleading, angry, mocking, shout, booming, frantic) when it should not be said in the character's usual manner. NPCs
speak plainly by default now rather than whispering, companions shout in a fight and talk between fights
(`Voices.BarkDelivery`), the villain mocks (`Voices.Villain`). A planner sets personas on the cast, NPCs and the
antagonist and deliveries on lines; the offline designer draws from a stock persona bank so no two people sound alike;
`CampaignRepair` and the validator clear anything unknown. The style is part of the line id, so a changed style
renders afresh and the old audio is simply unused; the shipped bank keeps playing its old ids until re-rendered.

With a Gemini API key in Settings, every spoken line of a plan (the narrator's floor intro and narration, the boss's
intro and death lines, NPC dialogue and quest lines, the companions' bark bank) is rendered by Gemini text-to-speech
in the background as soon as the plan exists, two lines at a time and never while an enemy is within 20 m. Audio is
content-addressed (`Adventure/Core/Voice/VoiceLines.cs`): the same text, voice and delivery always map to the same
Two TTS models are Settings: the main one (default `gemini-3.1-flash-tts-preview`) voices the narrator, the bosses and the campaign's cast, and the minor one (default `gemini-2.5-flash-preview-tts`) voices one-off NPCs and companion barks, so the best model's quota goes to the voices that carry the story; the queue also renders narrator and boss lines before anything else. Requests are paced (Settings "Voice requests per minute", default 8, flag `--voice-rpm=`): the preview TTS models allow only a handful of requests a minute regardless of the daily quota, and a per-minute 429 now waits what Google asks, slows the pace for the session, and only stops after fifteen minutes of nothing but 429s; before this the renderer gave up after three in a row, which looked like "rate stopped" while the daily quota still showed plenty of room. When a model's daily quota is used up its lines move down the chain (main, minor, then the fallback list), and when none is left it stops for the session and says so: a line in the message log, the title-screen status, the Settings bake status, and a headless bake prints `VOICE STOPPED`, keeps the plans it wrote and exits with `RESULT FAIL (voice stopped)`. "List the TTS models this key can use" in Settings asks Google directly. Line ids include the model name, so a model change renders afresh.
`user://voice/<id>.wav`, and the shipped bank under `assets/voice/builtin` is checked first, so replays and the
built-in campaign cost nothing. Playback is on its own Voice bus (volume in Settings); narration and dialogue
interrupt barks, barks never interrupt anything. Lines without audio still show as subtitles. Companion barks fire on
kills, heavy hits, loot, level-ups, seeing a boss, possession, going down and getting up, rate limited to one every
five seconds; they also print in the message log. A line that has been said stays out of the draw until the party is
two floors deeper (`Adventure/Core/Voice/BarkRepeat.cs`, the record travels in the save as `BarksSaid`); when every
line for a trigger is that recent the companion says nothing rather than repeat itself, except over a kill or a
dawdle, where the line said longest ago comes back. Kill lines (`KillBarks`, ten per hero) and idle razzings
(`IdleBarks`, ten per hero: a companion who is not being played mocks a player who has stood in one place for ten
seconds with no fight, dialogue or menu open, another one every twenty seconds after) are topped up from stock into
the campaign's bark sets and voiced with `--bake-kill-barks`. Without a key the game is silent apart from the shipped bank.
When Gemini answers with a quota error (HTTP 429), rendering stops for the session rather than burning requests: a
daily-quota error stops at once, a per-minute limit is retried after the delay Google suggests and stops after three
in a row. Nothing is lost; the lines that were not rendered are queued again the next time you press "Render voice"
or enter a floor that uses them. The reason is shown in the Settings status line and the message log.

## Lethal traps

From floor 3 the generator reserves one or two hazards on the critical path (`HazardPlanner`, `HazardCounts.For(floor)`),
never on the only route past a bridge cell and never where a vault, nest, water or stair already sits:

- **The Press**: a three-cell corridor run between two sliding stone slabs, the plate in the middle cell. Step on it and
  the walls grind (1.2 s warning), close over 2.4 s, crush, hold, retract and cool down before arming again. Anything with
  a body in the run when the gap closes is a victim: enemies die (no loot, XP only if a member saw it), bosses take 35%
  and stagger, quest NPCs go down, party members and fighting allies are **Mangled** (see below). A barrel left in the
  run jams the slabs at a metre; the second squeeze crushes it. Crypt floors favour presses.
- **Spike Beds**: a patch of holed floor. Pressure beds click, pre-raise a hand's breadth, then fire; periodic beds run
  on a metronome from the floor tick (4.5 s period, out of step with their neighbours) and glow red the second before.
  Feet on the patch at full height are impaled; a body in the air is not, so a jump clears them.

A **Mangled** member never auto-revives. Another living body stands within 2 m once the hazard is armed again and holds
E on them for 3 s (moving a metre, taking damage, or the hazard moving again interrupts); companions on Defensive or
Aggressive come and pull when no enemy is within 12 m. The villain gloats (`TauntTrigger.Crushed`). In co-op a crushed
player spectates the nearest teammate; after 30 s unrescued the server binds the body ahead on the critical path, in a
room the party has already seen, for a living player to free. Pathing treats a hazard's cells as danger: companions
detour hard, smart enemies lightly, hordes and bandits not at all.

Saves are v9: each floor keeps every hazard's phase, time into it, kills and jam; an older save gets hazards only on
floors it has not visited (`AdventureDto.HazardsFromFloor`). Flags: `--hazard=press|spikes` forces one hazard by the
door on every floor (`--hazard=press@2` on floor 2 only); `--test-hazard=press|spikes` runs the headless checks below.

## Ambushes and the boss's reinforcements

Two layers of pressure sit on top of every floor's planned population (Settings, "Danger"; flags `--no-ambush`,
`--no-portals`):

- **Ambushers** (`Adventure/Core/Design/Reinforcements.cs` `AmbushRules`, `Adventure/AmbushSpawner.cs`): three on
  floor 1 rising to ten by floor 20, drawn from the tier's ordinary creatures. Each hides in a rubble mound or a solid
  wall beside a corridor cell (never in a room, never within eight cells of the arrival stairs, six cells apart) and
  bursts out already hunting when an adventurer comes within about three metres: dust, a thud and a short shake.
  Placement is seeded, so a floor always hides the same ambushes; sprung points are saved (`FloorStateDto.SprungAmbushes`),
  the creatures themselves are dynamic like a horde's. `--test-ambush` stands on floor 2's first point and checks it
  springs, fights and saves.
- **Reinforcements** (`PortalRules`, `Adventure/PortalSpawner.cs`): the boss of the tier sends creatures through
  portals. After 90 s on a floor a portal opens every 60 s, shortening to every 20 s over six minutes, in an explored
  room behind the party (a third of it seen, nobody standing in it, about twelve cells away and nearer the arrival
  stairs than the lead); one creature at first, up to three as the pressure builds, never more than six loose at once.
  The first portal on a floor is narrated in the antagonist's name; the HUD shows how many are hunting you. Portal
  creatures give XP but almost never loot, so lingering never pays. Descending ends it; the pressure clock is saved
  with the floor (`PressureSeconds`, `PortalWaves`) so a quick load does not reset it; killing a tier's boss silences
  that tier for the run. "Reinforcement pressure" in Settings is 0 (off) to 2 (heavy: every wait halved, one more per
  portal, ten loose). Servers spawn them and clients see the rift (`PortalOpenedEvt`, `AmbushSprungEvt`).
  `--test-portal` hurries the clock on floor 1 and checks a portal opens away from the party, its creatures hunt,
  the clock saves, and a dead tier boss silences it.

## Floors are designed, not just rolled

Every floor gets a plan (`Adventure/Core/Plans/FloorPlan.cs`): named rooms with roles, enemy collectives with a
goal, extra chests and traps, a boss line, an NPC with a side quest, and narration. Plans come from the offline
designer (`OfflineDesigner.cs`, no network) today and from the Claude planner in the next phase; both go through the
same validator (`PlanValidator.cs`: rooms exist, budget, reachability, connected patrol routes, dialogue graphs) and
resolver (`PlanResolver.cs`: deterministic cells and save-stable spawn indices). Plans are stored in the save so a
reload rebuilds the same floor; saves from before plans existed load with the old random population.

Collectives (`Enemies/Collective.cs`, decisions in `Enemies/Core/Collectives/CollectiveBrain.cs`): a group shares an
alert level and a goal. Guards hold a post, chase within a leash and walk back; defenders hover at a chest, vault door
or stairs and fall back onto it; patrols walk a route between rooms; alarm groups send their fastest member sprinting
to wake another group, which comes to investigate; a group that loses half its members while the party is healthy
falls back to a rally room and re-engages together. Sneak attacks work on guards and patrols that have not noticed you. Enemies are keen: they see 40% further than
their data says, chase for nine seconds after losing sight, guards leash out to 20 m, and any enemy joining a fight
wakes everything unaware within 10 m, so a room tends to answer as one. Anyone who falls out of the world is put
back on the last ground they stood on with a small wound, and the log names the cell it happened at.

Rooms and hallways (`Dungeon/Generation/FloorGenerator.cs`): rooms six cells or wider lose their corners (two cells
of them at nine or wider), and the builder draws each cut corner with a 45° wall piece over a full floor slab, so big
rooms read as octagons and corridor bends are bevelled. Long corridor legs stagger sideways by a cell for a stretch in
the middle (only outside rooms, and never within three cells of a door, so ramps still find straight ground). One
structure per floor goes into a big room that carries nothing else special (`Structure.cs`): a **gatehouse**, a wall
of the theme's own cubes across the room with one arched opening, a raised portcullis and four braziers, which the
designer gives a Defend collective (objective kind `Gate`); or a **bandit camp**, a leaning plank hut with a doorway,
a collapsed wall or two, bedrolls, a barrel and a crate, a torch and a stone campfire out front. Hut walls are blocked
edges on the layout, so pathing goes round them like the party does. Camps are the only place the three bandits
(`data/enemies/bandit*.tres`, faction `bandits`, KayKit rogue and barbarian models) ever appear: the menu offers
them only when a camp exists, the offline designer posts a band of three to five at the fire, and the validator drops
bandits placed anywhere else and fills a camp the planner left empty. **Water** (`WaterCount` per tier, two in the
caves; ground-level rooms only): a pond of four to eight cells pooled in a room, or a stream cut wall to wall across
one. Water cells get no floor tile; `Dungeon/Props/WaterSurface.cs` builds a sunken bed instead, half a metre deep
with sloped banks (a height field over the cells, trimesh collision, shallowing to a ford under a bridge), and a
surface drawn by `assets/shaders/water.gdshader`: two scrolling normal maps, colour by the depth buffer, foam where
the bed meets the surface, refraction of what lies beneath and a faint glow so it reads in unlit rooms. Streams get a
plank bridge with rails at the crossing nearest the room's middle and iron culvert grates where they meet the walls;
ponds get drips from the ceiling. Every feature carries a looping trickle synthesised in code (`WaterAudio`, no audio
files), wading splashes and sends rings across the surface (`Ripple`), slows anyone in it to 65%, and lets enemies
notice a wading character from 30% further. Chests, barrels, keys and traps never land in water. Room flags `gate`,
`bandit-camp`, `ruin`, `pond` and `stream` reach the planner, and `StructureCount`/`WaterCount`/`RubbleCount` are floor
overrides it may set.

**Hordes.** Six rooms per floor (`NestCount`, rooms permitting, spread so nest rooms rarely adjoin) get a **nest**:
one to three one-cell breaches broken through the walls with solid rock behind them, each dressed as a black throat
ringed with jagged stone and lit from inside. The plan puts a **horde** on every nest room (`Hordes[]`: members, trigger,
name, line, at most eight per floor, three to sixty creatures each); the offline designer does so on every floor, and
the AI sees `nest(N mouths)` room flags and roster entries marked `horde`. Hordes scale with depth: a handful of
minions per hideaway on floor 1, growing every floor to fifty or sixty by floor 20. Each tier has a swarm creature
and a heavier **brute** of it (`horde_*_brute`, from the third floor, a growing share), and from the sixth floor a few
of the tier's own full-size creatures march among them (never more than a quarter, never more than eight); the
bigger ones come out spread through the stream, not all at the end. Triggers vary so hordes break out
of different parts of the floor at different moments: stepping into the room, opening a chest in it, or an alarm
runner arriving anywhere on the floor. Streams pause while more than seventy swarm creatures are alive at once, and
the HUD shows one count per horde that is loose.
Horde creatures are per-tier swarm fodder (`data/enemies/horde_*.tres`: husks, crawlers, cinder imps, shades) that
die in a hit or two, are worth a little XP and drop almost nothing; they only ever come out of a nest and count
against nothing in the budget. When a horde triggers, `Adventure/HordeSpawner.cs` speaks the line, rumbles, blows dust from the breaches and
streams the creatures out four a second, all of them already hunting, with a HUD count until the last one is down.
Horde creatures are drawn cheaply (`Enemy.SetSwarmDetail`): no shadows, coarser mesh LODs sooner, the model's own
shared materials instead of a tinted copy per creature, culled past fifty metres, their skeleton only animated within
twenty metres of the party, and a staler path. A horde that was set off never comes again: the save lists it as spent. `NestCount` is a tier knob and a planner
override, and `--test-horde` walks the party into a nest room on floor 3 and checks the stream, the kill and the save.

**Ruins and rubble** (`Dungeon/Props/Rubble.cs`, all built from boxes and cylinders in the theme's wall material, so
they match whatever the floor is made of). A **ruin** is the third structure kind: a colonnade along a big room's two
long sides, one column every other cell, with a third of them snapped to a stub and lying where they fell, extra
rubble, four wall lanterns instead of two, and a grey light where the ceiling gave way. Column cells are reserved and
blocked for pathing, so fights weave between them; ruins stay on ground level so nothing offers a step toward the
tops of the walls. **Rubble mounds** (`RubbleCount` per tier) are knee-high heaps on room floors that bodies path
around and the party can hop over; they never sit beside a doorway and never on anything else. Loose **scatter** lies
against walls and in corridors with no collision at all. **Buckled floors** are floor tiles whose slab is heaved into
a ridge, a dome or a diagonal fold that settles back to floor level at the tile's edge (a bump underfoot, never a step
a capsule would treat as a wall): nearly half the tiles of a ruined room, most tiles beside a mound, one in twenty
elsewhere. Rubble, columns and tilted tiles are all picked from the floor seed, so a reload rebuilds them the same.

Items on the floor (`Items/ItemVisuals.cs`) are models rather than cubes: KayKit keys, bottles (tinted by potion),
spellbook, swords, axes, dagger, staff, shields, quiver and arrows; built meshes for scrolls, bows, spears, maces,
helms, cuirasses, gloves, boots, rings, amulets, crystals and bones. Magic and better drops get a faint halo in their
rarity colour, rare and better a small light.

Geometry rules that keep the floor closed (`DungeonBuilder.BuildGridMap`): an opened secret wall becomes a floored
doorway; ramps carry a floor slab beneath the slope and a vault that climbs with them (`FloorLayout.CeilingLayer`,
`VaultRules.RampRise`), so a slope never looks out over the lower corridor's ceiling; upper-level floors stand on
solid plinths; a room with a ledge is built two layers tall so the gallery at 4 m really overlooks it, and every
corridor entering it gets a lintel cube over its last cell (the same rule that walls a low cell beside an upper floor);
tunnels are roofed with cubes and the cubes enclosing them are full height. `--test-floors` is the regression check
for all of this (it rolls a fresh seed each run; `--seed=N` pins one), and `--face-ramp[=bottom|top|ledge|gallery]`
stands the party at a ramp or on a gallery for a screenshot.

NPCs (`Adventure/Npc.cs`) stand where the plan put them; talk with E. Quests (`Adventure/Core/Quests/QuestMachine.cs`)
come in four shapes: slay a named group, recover a relic from a room and bring it back, escort the NPC to the stairs
down (they follow you and can be wounded by enemies standing next to them), or hold out with them against waves.
Rewards are XP and a chest-table roll at the giver's feet. The HUD shows the current objective under the floor label
and narration as a subtitle above the portraits; J opens the journal. The party cannot harm NPCs.

## Layout

- `src/` C# by namespace (`Core`, `Party`, `Characters`, `Combat`, `Enemies`, `Dungeon`, `Items`, `Save`, `UI`, `Adventure`).
  Engine-free logic (`Characters/*.cs`, `Combat/Core`, `Items` core, `Dungeon/Generation`, `Core/Rng.cs`, `Save/Core`, `Adventure/Core`, `Enemies/Core`, `Party/Core`) must never reference `Godot`; the test project compiles it directly.
  Party bodies carry a `ControlKind` (`Party/Control/Control.cs`: LocalHuman, Companion, Remote, Parked); `PartyController.LocalIndex` is the body this machine drives (-1 on a dedicated server), `FocusedIndex` the member the screens show, `Anchor` the body everyone else follows, `Active` the bodies in the world. Movement keys are read in one place (`LocalInput.Read`) into an `InputIntent`; bag moves go through `Party/Core/BagActions.cs` so a server can run them from the wire. Godot Resources live in `*/Defs` and `Items/ItemDef.cs`.
- `scenes/` `.tscn` files. `data/` definition Resources (`.tres`): `classes/`, `abilities/`, `items/`, `affixes/`, `legendaries/`, `loot_tables/`. Registry loads them by `Id` at startup; `Items/Catalog.cs` turns them into the plain `ItemCatalog` the roller uses. `assets/` third-party art (log licenses in `assets/LICENSES.md`).
- `tests/` xUnit project, ignored by Godot via `.gdignore`.

## Multiplayer (dedicated server)

Co-op runs on a dedicated server: the game started headless with `--server`, which any copy of the game joins from the title screen (Join Server) with a name and a class. Each player controls one adventurer; there are no AI companions on a server. The server simulates the world and every combat roll; clients own their own movement and send intents (attack, interact, bag moves, stat points, skills, dialogue choices, chat). Floors are built identically on every machine from the seed and the plan the server sends, so only divergent state crosses the wire. Any player using unlocked stairs takes the whole party down at once. A client never changes floor on its own: `Game.ChangeFloor`, `ReloadFloor` and `JumpToFloor` refuse on a client (a log line says the server decides), so debug keys, scripts and stray calls cannot leave a machine on a floor the server is not simulating; the only client path is the server's `FloorChange`.

```
godot --headless --path . -- --server --port=7777 --bind=0.0.0.0 --campaign=offline|builtin|ai --seed=N --save=server --max-players=4 --autosave=60 --status-file=PATH --quit-when-empty
godot --path . -- --join=ws://192.168.1.5:7777 --name=Eric --class=class.rogue     # skip the title screen
scripts/export_server.sh                                                            # "Linux Server" preset (dedicated_server=true) into build/server/
SERVER_BIN=build/server/DungeonCrawler.x86_64 scripts/net_test.sh                   # the same check against the exported server
```

In a game: T opens the chat line, the floor label shows the round trip and how many are online, other players wear name tags, and the pause menu's Save/Load are replaced by Disconnect (the world never pauses on a server). The exported server prints a page of "Unreferenced static string" lines at startup; that is export-template noise, not a fault.

- Saves: the server autosaves to `user://saves/<--save>.json` every minute, on every floor change, and when someone leaves; started again on the same slot it resumes the world with every character parked, and the same name gets its character back (stored class wins). Save version 7 adds `Multiplayer` and `Players`. Clients never save; a single-player save in the server's slot is left alone.
- Render (`deploy/`): `Dockerfile` (debian-slim + the exported server + FastAPI), `frontdoor.py` (`GET /` health JSON from the server's status file, `GET /status`, `WebSocket /ws` relayed to the Godot server on 127.0.0.1:7777), `start.sh` (Godot then uvicorn on `$PORT`, saves and keys on the `/data` disk, `CLAUDE_API_KEY` secret written to `keys.cfg`), `render.yaml` (Docker web service, health check `/`, 1 GB disk, `CAMPAIGN`/`MAX_PLAYERS`/`SEED` env). Players join with `wss://<service>.onrender.com/ws`. Free instances sleep and drop everyone; use a paid plan. Local check: `docker build -f deploy/Dockerfile -t dc-server . && docker run -p 10000:10000 -v dc-data:/data dc-server`, then `scripts/net_test.sh relay ws://127.0.0.1:10000/ws`.

- Google Cloud (or any Linux VM) (`deploy/gce/`): no Docker and no front door, the server listens on the port you open in the firewall. On the dev machine `scripts/export_server.sh`, then `gcloud compute scp --recurse build/server deploy VM:~/dc --zone=ZONE`; on the VM `sudo bash ~/dc/deploy/gce/install.sh ~/dc/server` installs the runtime libraries, a `dungeon` system user, the build under `/opt/dungeoncrawler`, saves and keys under `/var/lib/dungeoncrawler`, and a systemd service (`dungeoncrawler.service`: port 80 by default, with `CAP_NET_BIND_SERVICE` so no root is needed; `sudo systemctl edit dungeoncrawler` to change the port, campaign or player count). Open the port with `gcloud compute firewall-rules create dungeoncrawler --allow=tcp:80 --target-tags=dungeoncrawler` and tag the VM. Players join with `ws://<external ip>` (port 80 needs no suffix). Re-run the same two commands to update; the service is stopped and restarted around the copy and keeps its saves. To run from the clone itself instead of an export, `deploy/gce/setup_source.sh` installs the runtime libraries, Godot 4.7.2 .NET and the .NET 8 SDK user-local (idempotent), imports and builds; `deploy/gce/start.sh` then runs the server in the foreground from `build/server` if present, else the latest published release, else from source. With git instead of scp: `scripts/publish_server.sh` exports and publishes the build as a GitHub release asset (`server-<date>` tag, the build itself stays out of git); on the VM, `git clone` the repo once, `gh auth login`, and `deploy/gce/fetch.sh` pulls the scripts, downloads the latest `server-*` release and installs it.
- macOS: the same server runs on a Mac. Either run it from the project with the macOS Godot 4.7.2 .NET editor and the .NET 8 SDK (`godot --headless --path . -- --server ...`), run the Linux Docker image under Docker Desktop, or use the exported bundle: `scripts/export_server.sh` also produces `build/macos-server/DungeonCrawler-server.zip` (a universal, ad-hoc-signed `.app`; the "macOS" preset makes the client the same way). Unzip and run `DungeonCrawler.app/Contents/MacOS/DungeonCrawler --headless -- --server --port=7777 --bind=0.0.0.0 --campaign=builtin`; macOS asks once to allow incoming connections, and saves go under `~/Library/Application Support/Godot/app_userdata/DungeonCrawler/`. The bundle is not notarized, so on another Mac right-click, Open once (or `xattr -dr com.apple.quarantine DungeonCrawler.app`).
- Transport: Godot's `WebSocketMultiplayerPeer` used as a raw packet peer (`src/Net/WebSocketTransport.cs`) behind the engine-free `INetTransport`; `ws://` on a LAN, `wss://` through a TLS-terminating host such as Render. No RPC, no `MultiplayerSpawner`: every message is a record in `src/Net/Core/Messages.cs`, encoded by `NetCodec` (one kind byte, JSON with the save layer's options, gzip above 8 KB; `Pose`, `WorldSnapshot` and `Vitals` are binary).
- Engine-free core in `src/Net/Core/` (linked into the tests): `Protocol`, `PoseBuffer` (100 ms interpolation, 150 ms extrapolation cap), `PlayerRegistry` (name = identity, stored class wins, park/unpark), `ServerHandshake`/`ClientHandshake` (Hello -> Welcome -> Ready), `InMemoryTransport` for tests, `NetRefs` (`chest:3`, `door:1`, `stairs:down`, `loot:<guid>`).
- Godot glue: `NetSession` autoload (role, transport, command-line flags), `ServerWorld` (registry, welcome from a live `FloorStateCapture`, poses in, snapshots at 20 Hz with interest filtering, vitals at 10 Hz, log/sfx/objective forwarding, status file), `ClientWorld` (poses out at 30 Hz coalesced, snapshot/vitals in, remote bodies through `RemotePose`), `NetTestFlow` (the scripted check).
- A player who leaves is parked (body out of the world, pose remembered); the same name rejoins into the same character.
- `scripts/net_test.sh` runs the whole thing headless: join, identical layout hash, movement echoed, chat, a kill through intents, the loot picked up into a synced bag, a chest, the stairs to floor 2, leave and rejoin; then restarts the server on its save and rejoins once more. Both processes print a RESULT line.

## Collision layers

| Bit | Layer |
|---|---|
| 1 | World geometry (GridMap) |
| 2 | Party body |
| 4 | Enemies |
| 8 | Interactables (loot, stairs, NPCs) |
