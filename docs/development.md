# DungeonCrawler: developer manual

First-person, real-time, party-based dungeon crawler. Godot 4.7 with C# (.NET 8). This is the engineering reference: how
the game is built, tested, exported and run as a server. Players want the [README](../README.md); setting up a server is in
[server-setup.md](server-setup.md).

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
One look belongs to no tier: `tavern` (`data/themes/tavern.tres`: plank floor, plaster and timber walls, firelight; the
builder adds its furniture and hearth, and the title tune plays on) is worn only by floor 0, which takes it by name
whatever the seed, the campaign or `--theme=` say.

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

**Floor 0: the tavern.** A new game opens above the dungeon, in the tavern: one 7×5 room (`LayoutKind.Tavern`, never
dealt by the seed nor forced by `--layout=`), theme `tavern`, the up-stairs cell at the room's corner with no flight up
(climbing from floor 1 is refused) and the down flight in the opposite corner, asking "Go down into the tunnels?".
No enemies, traps, vaults or safe room. The campaign's merchant (or "The Merchant") stands at the room's centre and,
the first time the local player comes within 4 m (`TavernRules.GreetRadius`) with no screen open, opens his briefing
(hire, quakes, deal, sendoff; the campaign's own when it has a `deal` node, else the generic bank's). The seed money is
150 gold (`TavernRules.SeedGold`), paid once per game into the party purse: when the briefing reaches the `deal` node,
or else on the first descent (`AdventureDto.SeedMoneyPaid` keeps it in the save; `Greeted` is not saved, so a loaded
tavern briefs again). `--floor=0` opens on it for checks. The title screen and a plain `--server` start in the tavern;
every `--test-*` flow, the other automation flags and `--test-net-server` start on floor 1 unless given `--floor=0`
(`DebugTools.StartFloor`); `--test-tavern` asks for it itself, and `START_FLOOR=0 scripts/net_test.sh` runs the co-op
check from the tavern (the client joins, strikes the deal and leaves).

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
  The jail's key door answers to its jailers (`JailbreakController.TendJailDoor`): the enemy pathfinder treats closed doors as walls, so a
  creature that carries the jail key may path through the jail door alone (`JailbreakRules.BlockedFor`, `DungeonBuilder.JailDoorCell`); the
  door unlocks for an escort within `DoorUnlockRange` (5.5 m, cells are 4 m), and locks again once no creature stands inside or within
  `DoorLockClearance` and no adventurer is at it. A delivering jailer leaves its collective (its post could lie behind a vault door) and
  holds the doorstep; an escort that makes no progress for `EscortStuckSeconds` lets the prisoner go and walks home instead of standing
  with its face in the wall (the anomaly a player reported on floor 2, 2026-11-08). `--test-jailer` (with `--builtin --floor=2` and the
  calm flags) proves the whole march: seize, unlock, deliver, lock, post.
  Two more rules from the same hunt: nothing solid stands in a **doorway cell** (`FloorLayout.IsDoorway`: a room cell that opens onto
  walkable ground outside its room), neither the generator's chests and crates nor a plan's extra chests (`PlanResolver.TakeSolid`, which
  also spares the room's anchor), because creatures path around props and cannot move what the party can (a chest in a doorway on floor 8
  cut a patrol off from its stop and the whole group stood in a wall); and `PatrolState` skips a stop it has had no path to for
  `NoPathSkipSeconds` (3 s, `Enemy.PathFailed`), becoming a guard when no stop is reachable.
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
- The orc's true form: on floor 20 the last boss wears `big_baddy.glb` (owner's decision 2026-11-12; `NpcBodies.Villain` returns `big_baddy` for the orc, scaled 2x in `DungeonBuilder.SpawnEnemy`); floor 1 keeps `orc1.glb`.
- All sixteen bodies from the briefs are in (party, fighters, archetypes, the Verger).
- Victory dance: a hero whose blow kills something opens with a Squat, then two different moves drawn fresh from the rest
  of a pool of fourteen (HipBuck, HipRock, Cheer, Punch, Shrug, HitReact, Cower, Pain, Talk, Flinch, Laugh, Point,
  ShakeHead), each done once (the three hip moves at 4x speed, the gestures at their natural pace), then a Wave if
  nothing is left fighting the party by then (`VictoryDance`, engine-free). The dance starts
  once the swing has played out. A companion stands still for it unless the fight is still on, and no companion goes for
  loot or a chest while anyone is dancing; a player's body stops the moment it walks off, and anyone stops when they swing
  again or are hit. The dancer crows one of twenty voiced lines (`Voice/DanceBarks.cs`, per hero by name, five by class and five
  generic as fallbacks, `BarkTrigger.Dance`, "Booty be all that..."), which stands in for the kill bark that kill. Sable's
  lines, like her voice (Inworld Tessa), are a woman's; her kill bank also has fifteen dance-themed taunts beside the
  original ten. A bark set is topped up from stock until it holds 20 lines of a trigger (`KillBarks.MinLines`, `DanceBarks.MinLines`). Your own body is
  hidden in first person, so it does not dance (your kill bark plays as before); you see your companions' dances. In co-op
  the server tells every screen whose body dances (`EmoteEvt`) and the line arrives as a bark. The stock Quaternius models
  have no such clips and sit it out. Logs `[Dance] Name: Clip xSpeed`. `--dance` starts the dance on every companion at
  `--after` for a screenshot (`--possess=3 --face-node=Brand:3 --dance ...`). New dance text is voiced with `--bake-kill-barks`.
- Travel grumbles: once nothing has happened for 25 s (no fight, dialogue or menu) and the party has walked 12 m since the
  last one, whoever's turn it is (the player's own hero included) says one of twenty bored lines (`Voice/TravelBarks.cs`,
  `BarkTrigger.Travel`, "I guess we're getting paid by the mile", "Did somebody just pass gas?"), then not again for a gap drawn fresh each time between 15 and 32 s
  and another 12 m (`TravelWatch`, driven by `AdventureDirector.TickTravel` on the server or in solo). `--wander=SECONDS`
  walks the lead ahead, turning when it bumps into something, so a headless run covers ground: `--builtin --no-ai
  --clear-enemies --floor=1 --wander=110 --quit-after=115` and grep `[Log] Brand: "`.
- Talking to an NPC: while its line is spoken (voice playing, or an unvoiced line "read" at about 2.6 words a second)
  the body plays Talk; while it waits for your answer it rocks slowly on the spot (HipRock at half speed); `TalkPose`,
  driven by the dialogue screen through `Npc.Converse`. A move picked from Foehammer's grid plays through first. Logs `[Talk]`.
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
Enemy grunts are CC0 creature SFX from rubberduck's packs, fetched by `scripts/fetch_grunts.py` into `assets/audio/grunts/<set>/`;
`VoiceSets` picks the set by enemy size and plays a hurt or death clip (pitch varied), and falls back to the old `enemy_hurt` and
`enemy_die` cues when a set has no clips. They are listed in `assets/LICENSES.md`.
After adding files under `assets/`, run `godot --headless --import --path .` once so Godot imports them.

## Build, test, run

Players never see this section: `get.sh` / `get.bat` at the repository root download the latest `game-*` release and run
its setup program, `build.sh` / `build.bat` fetch Godot 4.7.2 .NET, the export templates and the .NET 8 SDK user-local,
build, export for the current system and run the same setup program (outside a clone they `git clone` into
~/DungeonCrawler; build.bat fetches a portable MinGit into %LOCALAPPDATA%\DungeonCrawlerTools when the PC has no git),
and `update.sh` / `update.bat` pull, rebuild and reinstall keeping the Desktop launcher choice. The setup programs (`setup.sh`, `Setup.bat`,
`Setup.command`: Install / Uninstall menu, optional Desktop launcher) and the Host scripts (run the installed game as a
dedicated server on port 7777) live in `installer/<os>/`; Windows also has `Launch.bat`, which the shortcuts open: a boot menu with Play (asks GitHub for a newer `game-*` release, installs it on request, starts the game) and Debug (`DungeonCrawler.console.exe --verbose -- <flags>`, or the exe plus the Godot log followed live when a build has no console wrapper; the Windows preset now exports the wrapper for release too). The release check judges by the `VERSION` file that `package.sh` and `build.bat` put beside the exe (`<hash> source` marks a from-source build, which it leaves to `update.bat`). All of them are copied into every archive by `scripts/package.sh` and
next to the export by the build scripts. Windows scripts are classic `cmd` batch files on purpose (PowerShell's
execution policy stops ordinary users from running `.ps1` files); `.gitattributes` keeps them CRLF.

The test suite (`tests/`, xUnit, ~3,600 tests over the engine-free rules) is kept on the development machine and is
not in the repository, by decision; a fresh clone builds and runs the game without it.

```bash
dotnet build DungeonCrawler.csproj          # game assembly -> .godot/mono/temp/bin/Debug
dotnet test tests/DungeonCrawler.Tests.csproj   # engine-free logic (generator, rng, ...)
godot --path .                              # play
godot -e --path .                           # open the editor
```


On Windows (the other half of the dual boot, same checkout): run `build.bat` once; it fetches Godot 4.7.2 .NET, the export
templates and a .NET 8 SDK into `%LOCALAPPDATA%\DungeonCrawlerTools` without touching the system. F5 then works from VS Code:
`.vscode/tasks.json` builds through `scripts/dotnet.bat` (the SDK on PATH when it is .NET 8, else that private copy) and
`.vscode/launch.json` starts the fetched Godot console exe with the private SDK in front of its PATH, because Godot .NET finds
its runtime by running `dotnet --list-sdks` and crashes at startup without one. `nuget.config` at the root names nuget.org, so
the build does not depend on the user-level NuGet config (empty on one Windows PC: MSB4236, Godot.NET.Sdk not found).
`scripts/godot.bat` is the same fallback for the command line: `scripts\godot.bat --path .` plays, `-e` opens the editor.

Headless checks:

```bash
godot --headless --path . --quit-after 60          # boots and builds floor 1
godot --headless --path . -- --smoke               # scripted fight, loot, stairs 1->2->3->2, prints a REPORT line
godot --headless --path . -- --test-save           # save, mutate, load, compare; prints RESULT match
godot --path . -- --test-drag                      # synthetic inventory drag-and-drop through the GUI
godot --headless --path . -- --test-depth          # traps, doors, levers, pathing, boss stairs, victory, endless
godot --headless --path . -- --test-skills         # learn chains, upgrades, multishot, respec, save round trip
godot --headless --path . -- --test-abilities --no-ambush --no-portals   # every hero learns every skill and uses every ability on a shielded enemy: costs, damage, statuses, buffs; then a fireball and an arrow into a wall; prints RESULT (seed 7 unless --seed= is given: its fights are flaky on random floors)
godot --headless --path . -- --test-vertical --seed=5
godot --path . -- --seed=3 --test-stairs                                   # climb the up flight, get asked, decline, accept, then take the well down (needs a window: the question is HUD)   # climb a ledge, fall into a cellar, open a secret, cross-level chase
godot --headless --path . -- --test-gold --no-ambush --no-portals   # chest spills a coin pile, a full bag still picks it up, a companion picks coins up, enemies drop coins one time in four; prints RESULT
godot --headless --path . -- --test-party          # companions follow, possession switch, companions kill an enemy, auto-revive
godot --headless --path . -- --test-music          # exploration track plays, battle music on aggro, back to exploration after the kill, tavern tune in the menu
godot --headless --path . -- --test-story          # Investigate clues, a Decide flag, a Betrayal that spawns the turncoat's creatures, an escort Betrayal the NPC flees to, a flag-gated NPC on the next floor
godot --headless --path . -- --test-path --seed=11 --floor=3   # enemies reach the party without line of sight
godot --headless --path . -- --test-plan            # offline designer: plan 20 floors, validate, round-trip JSON, build one and compare
godot --headless --path . -- --test-collectives     # guard leash and return, patrol, alarm runner, regroup; fixed seed 4242 unless --seed
godot --headless --path . -- --test-quest           # one NPC per quest template, played through dialogue to completion, saved
godot --path . -- --floor=3 --talk --screenshot=/tmp/talk.png --after=2   # open the first NPC's dialogue (add --press=quest_log for the journal)
godot --path . -- --seed=1 --floor=1 --talk --showcase=Death --screenshot=/tmp/move.png --after=3   # floor 1's fighter: open his dialogue and play one of his model's clips from the moves grid
godot --headless --path . -- --test-ai-flow         # stand-in planner: New Game card, prefetch, wait for a plan, timeout fallback, memory summary
godot --headless --path . -- --builtin              # play the shipped campaign (data/adventures/builtin), no key needed
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-kill-barks=assets/voice/builtin   # render every party member's and fighter's kill line into the shipped bank (RUNWARE_API_KEY in the environment), then run the import
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-cast-voice=assets/voice/builtin   # render every line of the recurring cast, the merchant's Merchant section and every boss line (outside the 80-line budget) into the shipped bank; the plans with their ids land in assets/voice/builtin/cast-plans, to merge into data/adventures/builtin by hand, then --import
godot --headless --path . -- --test-hazard=press    # floor 1, lethality 3: an enemy and the lead in the run, crushed and mangled, no auto-revive, a companion pulls the lead free, the villain gloats, a barrel jams the second run, saved, loaded, phase and kills survive
godot --headless --path . -- --test-hazard=spikes   # a pressure bed clicks, raises, impales the lead and an enemy, is rescued; a periodic bed keeps the floor tick's phase; a jumping body is missed
godot --headless --path . -- --test-jailer --builtin --floor=2 --no-ai --no-ambush --no-portals --no-gathering --lethality=1  # a creature set beside a prisoner must unlock the jail, deliver, lock and post
godot --headless --path . -- --builtin --floor=N --dump-enemies=40,70 --quit-after=72  # prints every creature's cell, state, facing and post and every NPC's place at those seconds (anomaly hunts)
godot --headless --path . -- --test-objective=KIND  # floor 2 with that objective (toxic, shiftingvault, ritual, jailbreak): stairs sealed, the villain speaks, driven to its end, stairs open, saved; --objective=KIND forces a kind on every eligible floor, --lethality=N pins the damage multiplier
godot --headless --path . -- --test-gas-wipe        # floor 2 toxic, lethality 3: the gas full and the floor remembered as the server autosaves it, the party gassed to a wipe; the floor comes back with the gas at its grace and nobody falls again
godot --headless --path . -- --purge-voice          # sweep assets/voice/builtin of lines the shipped campaign no longer refers to, and user://voice of lines no campaign, run or save slot refers to; every bake does the same for the bank it wrote unless --keep-old-voice
godot --headless --path . -- --check-voice-bank     # every voice line the campaign on disk refers to: how many the shipped bank holds and how many load; run it on an exported binary (build/linux/DungeonCrawler.x86_64 --headless -- --check-voice-bank) to prove the bank was packed
scripts/net_test.sh                                  # dedicated server + client over WebSocket: join, same floor, movement echoed, chat, a ping relayed back with the client's name, the one-player stairs path (no ready-check), the same elite roster as the server on floor 2, the Press on floor 2 replicated, a purchase and a sale at the merchant, leave (the server runs at lethality 1 with --hazard=press@2)
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
godot --headless --path . -- --test-safe-room   # the floor's safe room: door, exclusion, the open door holding a creature out, x6 regen, gathering, a gatherer back at its post, save, HUD (seed 7, lethality 1, no ambush or portals; add --no-gathering to check none gather, --objective=toxic for floor 2's gas staying out); prints [saferoom] RESULT
godot --headless --path . -- --test-safe-room --no-gathering   # door, rest, gathering (plan B) then the merchant: stock, buy, refuse, sell, reload, shop screen
godot --headless --path . -- --test-elites-and-bosses   # floor 8 seed 8: the two rolled elites by name, the same after a save/load, Warded absorbs and regrows, Volatile bursts (even under the Press), Thorned reflects, the Shrouded dome stops a bolt and an outside nova, x2.5 XP and a table.elite drop, a kiting archer, a shielding shaman, a bomber; floor 5: the Crypt Lord roars at half, speeds up, alternates Slam and Summon, the braziers go out, phase survives a load; floor 23 no boss, floor 25 the First Herald; prints [elites] RESULT
godot --headless --path . -- --test-items-and-builds   # loot curve 8 vs 16, stack prices, buyback, a keystone's price, the duo node from two branches, a legendary trigger, Burn spread, Gerald, save round trip; prints [items] RESULT
godot --headless --path . -- --test-fortune   # floor 2 shrine and card, a boon's stat, a gold and a blood price, a curse lifted, vault table odds, a forced mimic (save/load mid-spring), the bound chest's hunt, a secret room found, the third lockpick jams, the Lucky Dip, save round trip (seed 7, lethality 1); prints [fortune] RESULT
godot --headless --path . -- --test-run-structure   # Story vs New Descent seeds, a stable Daily, clauses saved and priced, a wipe that keeps the floor, costs gold, scars and shows a death card, the Death Clause's memorial and recap, the autosave and floor card on a descent, the recap model, the profile and a deed, Victory saved (seed 7, lethality 1, profile-test.json); prints [runstructure] RESULT
godot --headless --path . -- --test-tavern   # floor 0: one room, no enemies, the merchant, empty purse, "The Tavern", the tavern tune; an open inventory holds his greeting back, closed he briefs within 4 m; a save mid-briefing comes back on floor 0 unpaid and he briefs again; walked to the deal the purse gets 150; the down landing asks and floor 1 opens with 150 (paid once); prints [tavern] RESULT
godot --headless --path . -- --test-tavern-skip   # the same opening but the briefing is closed unread: the purse stays empty until the descent stakes it with 150 on floor 1
godot --headless --path . -- --test-writing   # banter in order and stopped by a fight, a TrapSprung bark from the lazy top-up, narration beats a bark on the subtitle, the cheer and its answers, the press epitaph, floor 3's Crushed taunt, Crane's aside
godot --headless --path . -- --test-hit-fx   # blood, bursts, stuck arrows, grunts (seed 7); prints [hitfx] RESULT
godot --headless --path . -- --test-combat-feel   # perfect block, block, parry and riposte, dodge (never through gas), stagger, damage numbers and bars, Shatter/Ignite/Bleed/Vulnerable, the boss's decal and the companions' dodge, boss bar, vignette, banner, toast (seed 7, lethality 1); prints [combat-feel] RESULT
godot --headless --path . -- --test-co-op   # friends: the ping key bound to X, a ping at a creature landing on the creature (not the wall behind), a ping marker and its Label3D that the cooldown refuses to repeat and that expire after 2 s, the stairs ready-check rule table, the HUD line, chat in solo writing to the log, T ignored while the stairs question is up (Esc answers the question, never the chat), solo stairs unchanged (seed 7, lethality 1); prints [co-op] RESULT
godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2   # regenerate each floor_NN.json's layout from its campaign and rewrite only its LayoutHash (after a generator change, so the shipped plans load without "adapted")
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
scripts/export_all.sh            # all three of the above after a Release build (add --server for the dedicated server bundles too); first copies your latest rebake (user://adventures/builtin and the voice lines it uses)
scripts/export_all.sh --no-ship  # the same, exporting the campaign as it is in the repository (use this when the repo's copy is the newer one)
scripts/package.sh               # installable archives under dist/, each with a setup program offering Install or Uninstall and, on install, a Desktop launcher: Linux tar.gz (setup.sh: ~/.local/opt, app menu, dungeoncrawler command), Windows zip (Setup.bat: %LOCALAPPDATA%\Programs, Start Menu), macOS zip (Setup.command: Applications, quarantine cleared); INSTALL.txt and SHA256SUMS beside them; VERSION=1.2 names them into the project so the build plays it; --no-ship exports the repository's copy as it is, BAKE_DIR=... names another user data folder
godot --path . -- --face-enemy --screenshot=/tmp/shot.png --after=2   # rendered screenshot (--open-map for the full map)
```

## Controls

You possess one of the four characters at a time (the local body); the other three follow and fight on their own.

| Key | Action |
|---|---|
| W A S D, Shift | Move, sprint. Holding forward for two seconds sprints on its own until you let go |
| Space | Jump (clears a pit at a sprint) |
| Ctrl (hold) | Crouch: slower, a lower eye, and enemies only notice you from 60% of their usual range |
| Left Alt | Dodge: a 0.25 s burst in the movement direction (back when still), 20 stamina, 0.6 s cooldown; a creature's strike or bolt passes through you for the burst, but gas, flames, traps and falls do not care (`DefenseRules`) |
| Mouse | Look |
| Left click | The lead uses their active ability. Tap = light swing, hold and release = heavy (release at 0.6-1.0 s for a perfect blow). A heavy or perfect blow staggers a regular enemy for its hit-react clip (never a boss) |
| 1 / 2 / 3 / 4 | Take control of that character (camera snaps to their eyes) |
| F | Take control of the next living character |
| Right mouse | Blocking classes (Warrior): hold to guard, which absorbs 75% of a hit for 12 stamina with no stamina regen while guarding; a hit that lands in the first 0.2 s of the guard is a perfect block: free, negated, and the attacker reels 1.2 s. The others (Rogue, Ranger, Mage): tap to parry, a 0.25 s window for 10 stamina (0.8 s cooldown) in which a melee blow is negated and the attacker reels 1.2 s; bolts and spells are not parried, a missed parry is just the stamina. Riposte and Second Wind fire on a parry and a perfect block as on a block |
| Q or mouse wheel | Cycle the lead's active ability |
| R | Cycle every companion's stance: Defensive, Aggressive, Hold |
| V | Cheer: the lead rallies, every other living hero answers (20 s cooldown); in co-op everyone's body cheers |
| X | Ping: a 2 s marker with your name where you look, in the world (a `Label3D`) and on the minimap; in co-op everyone sees it (relayed by the server, 1 s cooldown, refused farther than 45 m from your body) |
| T | Chat line (solo and host too: the line goes to the log as `[Name] text`; on a server everyone reads it) |
| Portrait buttons | "Control" possesses that character; the small button cycles their stance |
| Perception | Hidden pressure plates, loose seams in walls and the locked doors of vaults (a stretch of ordinary wall until someone makes out the lock plate or the gate's seams) are spotted by whichever member has the Perception for the distance: rangers (+40) from across the room, rogues (+25) a few steps out, mages (+15) and warriors (+0) about a step short. A spotted plate is a dull slab of slightly wrong stone and a seam a hairline crack; look at them for the prompt |
| Inventory icons | Bag and paper-doll cells show the item's own model, rendered once per item kind into a small texture by a hidden viewport (`Items/ItemIcons.cs`) and cached for the session; stack counts overlay it. Headless runs keep the old coloured squares |
| Inventory DROP / DESTROY cells | Drag a bag item onto DROP to leave it on the floor (companions leave it alone) or onto DESTROY to be rid of it for good; quest items cannot be destroyed |
| C | Character sheet (spend stat points) |
| K | Skill tree (spend skill points). F switches character |
| Tab or I | Inventory: drag to move or equip, double-click or right-click to equip, unequip, or drink. Drag to DROP to throw away. The party's gold shows at the top of the info panel |
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
teleport to the lead if lost for five seconds, drink a shared potion below 30% health, pick up loot they pass (coin piles too, even with a full bag) and
open chests they reach, and fight by stance. Defensive (default) engages enemies that are already fighting the party
or whatever the lead attacks; Aggressive engages anything seen within 12 m; Hold stays put and only fights what
reaches it. Ranged characters keep about 7 m away and back off when something closes in. They cast party buffs when
missing, novas when two or more enemies are close, and their strongest ready ability otherwise. Enemies pick their own
target by recent damage and proximity. A character at zero HP is down until no enemy has fought the party for three
seconds, then gets up at 1 HP; if the lead goes down, control passes to the next living character.

## Story: cast, mystery, choices

A campaign has a recurring **Cast** (four to six people with a role and a secret), a **Mystery** (a question, its hidden truth, clues spread over the descent, a payoff floor) and per-floor **Story** beats; about eight of the twenty floors carry the plot, the rest get one line of narration. Quest templates: KillTarget, Recover, Escort, HoldOut, and three story kinds: **Investigate** (clues lie in two to four rooms; bring them back), **Decide** (a dialogue choice sets a flag the run remembers and completes the quest), **Betrayal** (an NPC with a `Betrayal` turns when you take a `Betray` choice: they vanish, their creatures stand where they stood, the first wearing their name, and killing them completes the quest; a hand-authored `Betrayal.Escort: true` makes it an escort the NPC flees to instead: they leave alive, no creature wears their name, and the log and the quest say they fled). NPCs, quests, dialogue choices and narration can carry `RequiresFlag` / `ForbidsFlag`, so a floor can hold both branches of an earlier decision and only the matching one appears. Flags live in the campaign memory (saved) and reach the planner in the memory notes.

Two hand-authored hooks the designer is never asked for (both `[AiIgnore]`): an NPC with `"Ritualist": true` on a Ritual
floor leads the rite instead of a stock-named villager (placed beside the altar, no script needed; the validator clears
the flag with a repair note on a floor with no shrine), and a boss with `HalfLine` speaks once at half health, while
`AltDeathFlag` with `AltHalfLine` / `AltDeathLine` swaps its middle and last words when that flag was set earlier in
the run. The shipped Gullet of Gorm uses both: Grisel Thorn's Unswearing on floor 7, and the Verger's half-health and
death lines on floor 20 turning on `wake_gorm`. The recurring cast is voiced in full (see `--bake-cast-voice`). Maud Sorrel also stands on floors 1, 6 and 11
(the bible's cameos) with a few lines and no quest.

A floor's `Narration` entries are played by their trigger (`AdventureDirector`, server and solo only; clients hear them as `NarrationMsg`): `RoomEnter` when any active party body stands in that room, `BossSeen` / `BossDead` four seconds after the boss's own line, `QuestDone` / `QuestFailed` when a quest on the floor ends. `Voice/NarrationRules.cs` decides which entries are due (trigger, room, `RequiresFlag` / `ForbidsFlag`, not yet said) and keys each one by floor, trigger, room and text; the key is kept forever in `AdventureDto.BarksSaid`, so an entry plays once per run across saves and revisits, while one held back by a flag can still play later. Entries due together play four seconds apart, in plan order. A boss named like the campaign's antagonist is queued and voiced as the villain, so the Herald keeps one voice from the floor 1 taunts to the last boss. `--test-narration` checks the once-only rule through a save and load.

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
the campaign's bark sets and voiced with `--bake-kill-barks`. `--bake-cast-voice` also renders the campaign's `Merchant` section (his briefing, his twenty floors of lines and his
gathering lines) in the merchant cast member's own voice, so Master Oswin Pellet's 67 lines ship in the bank, beside
Old Ulf's reworded floor-13 pattern-rooms line, rebaked in his own voice in the same pass. Without a key the game is silent apart from the shipped bank.
When Gemini answers with a quota error (HTTP 429), rendering stops for the session rather than burning requests: a
daily-quota error stops at once, a per-minute limit is retried after the delay Google suggests and stops after three
in a row. Nothing is lost; the lines that were not rendered are queued again the next time you press "Render voice"
or enter a floor that uses them. The reason is shown in the Settings status line and the message log.

## A party that talks

The heroes talk to each other and to what just happened. Everything is engine-free under `src/Adventure/Core/Voice/`
and poured into a campaign's bark sets the way the kill, idle, dance and travel banks always were
(`CampaignRepair.TopUpSet` runs every bank at once; `AdventureDirector.Bark` runs it lazily, so a campaign baked before
a trigger existed still speaks, and a co-op player under their own name gets a stock set for their class). The Godot
side is `src/Adventure/AdventureDirector.Talk.cs`.

**Banks and sizes.** `ReactiveBarks`: 19 triggers (TrapSprung, Fell, PerfectBlow, Parried, Mimic, SecretFound,
EliteSeen, Broke, ShrineBoon, ShrineCurse, GambleWin, GambleDud, LowHp, and Hurt, Loot, Downed, Revived, BossSeen,
LevelUp grown to eight), 8 lines per hero (Brand, Sable, Wren, Ilex), 5 per class, 5 generic. `CheerBarks`: 4 cheers and
6 answers per hero (4/5 per class and generic). `BanterBank`: 45 exchanges of 2-4 lines, every pair of heroes at least
five. `Epitaphs`: 10 causes x 4 tiers. `TauntBank`: +12 stock lines (two each for EliteSlain, ShrineUsed, MimicSprung,
AllyDead, AllyLeft, Gamble; tokens `{elite}` and `{name}`). A perfect block speaks from the Parried bank. Stock banks never
name the shipped campaign's people or places (`StockBankTests`).

**Rules.** `BarkRules` (5 s global gap, 25 s per member per trigger, LowHp under 25% once per member per floor,
`QuickChance` 0.4 for PerfectBlow and Parried, subtitle 2.5-6 s, `ChooseOrOldest` for everything but Possess),
`CheerRules` (answers at 0.8 s then 1.2 s apart, 20 s cooldown), `BanterRules` (1.4 s between lines; one travel grumble
in three is an exchange; 8 s of rest in the safe room; 5 s after arriving in the tavern, then every 45 s; never through a
fight: `MusicDirector.AnyoneFighting` aborts the run and a speaker who goes down drops the rest), `WipeCause` (a note's
validity: 20 s, gas 120, horde 60, elite 30; then the boss if one is up, else generic).

**Gating and state.** Banter is remembered by id through `BarkRepeat` (`banter:<id>`, `SaidForever` for OncePerRun).
Gag counters live in `AdventureDto.GagCounts` (save v11, defaults only): `gerald` (Ilex's puddles, `{roman}`), `doors`
(opened vault doors, `{count}`), `empty` (quiet travel ticks). Run flags in `Memory.Flags` set by
`AdventureDirector.SetRunFlag` (no record entry): `seen_mimic`, `wiped_once`, `took_boon`, `took_curse`, `gambled`.
`CampaignPlan.Banter` holds hand-authored exchanges beside the stock; the planner is not asked for them.

**Subtitles.** `Hud.ShowBark(speaker, text, seconds)`: one speaker at a time with their name; narration wins (a bark never
overwrites a narration still on screen, a later bark replaces an earlier bark). `AdventureDirector.Say` is the one exit
for a companion's line: log, `EventBus.Bark`, subtitle, voice.

**Co-op.** Server-authoritative: every line the server picks reaches clients as a `BarkMsg` (member index, text, voice
id) and `AdventureDirector.PlayBark(member, line)` shows it with the name. V on a client sends `CheerIntent` (protocol 7);
the server raises `EventBus.Cheer`, picks the lines and broadcasts `EmoteEvt(member, "cheer")`; `Adventurer.PlayCheer`
plays the viewer bodies' Cheer clip. `scripts/net_test.sh` sends one cheer after the shop step and expects the hero's own
line back.

**Asides.** `DialogueNode.Aside { Member, Line }` is a companion's remark once the NPC has finished the line, shown under
it in the dialogue (label `Aside`) and logged; skipped when the member is not in the party, down or parked. Local to each
screen. The shipped campaign has six (Crane's "plumbing" earns Sable's "He means us.").

**What other pillars call.** `AdventureDirector.NoteDeathCause(WipeCause.X)` (elites: Bomber, Elite),
`AdventureDirector.LastWipe` (`WipeInfo(Floor, Cause, Count, Epitaph)`, set as `PartyWiped` fires; run-structure's death
card reads it on `PartyWipedBy`), and the `EventBus` moments: `RaiseEliteSeen`, `RaiseEliteSlain`, `RaiseMimicSprung`,
`RaiseShrineUsed`, `RaiseSecretFound`, `RaiseTrapSprung`, `RaiseMemberFell`, `RaisePerfectBlow`, `RaiseParried`,
`RaisePerfectBlock`, `RaiseGambled`, `RaiseCheer`, `RaiseBrokeAtShop`, `RaiseLowHp`, `RaiseBoonTaken`, `RaiseCurseTaken`,
`RaisePartyWipedBy`, `RaiseFloorLedgerClosed`. Trap, shop refusal, a fighter's refusal (`NpcEventKind.Refused`) and a
secret wall's opening are raised today; the rest wait on their pillars.

**Voice.** New text is unvoiced until a bake runs. `--bake-all-voice=DIR` (with `--builtin --ai` and the voice flags) renders every
line the shipped campaign can say at once with no budget: tier lines, every taunt, every bark set topped up from every stock bank, the
banter (the stock and unlockable exchanges are poured into `CampaignPlan.Banter` by id so their ids ship; `BanterBank.BankFor` draws
from the campaign's copies first), the merchant, and every floor's intro, narration, boss, cast, aside, taunt and horde line; lines the
bank holds cost nothing, and the campaign and the twenty plans are written with their ids to DIR/all-plans to copy over
`data/adventures/builtin`. `--voice-concurrent=N` sets the lines in flight (2 by default; Runware takes 4). The older flags remain
(`--bake-kill-barks` tops up every bank and gives the lines their ids; `--bake-cast-voice` for the campaign's own lines); `VoiceBudget` counts asides beside their NPC line. The shipped
campaign's new Possess lines, the Verger's twelve taunts and the six asides carry `VoiceId: null`.

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

Saves are v10 (the party purse and any coin piles on the floor are saved; an older save loads with none): each floor keeps every hazard's phase, time into it, kills and jam; an older save gets hazards only on
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

## Hit effects

A sword hit spatters a short burst of dark blood (`HitFx.Blood`), none when the shield takes the whole blow. A spell bursts where it
lands on an enemy or a wall (`HitFx.SpellBurst`), and an arrow stays stuck in the enemy it hits (`HitFx.StickArrow`): at most six are
drawn, while the true `EmbeddedArrows` count is saved and restored. Enemies grunt when hurt and when they die. In co-op, blood and
stuck arrows reach clients as one `HitFxEvt` (protocol version 6; 7 since the Depths) and grunts travel through the existing `SfxMsg` relay; spell
bursts stay local on every machine (each has the bolt) and are never relayed. `--test-hit-fx` checks it all headless.

## Combat feel

A fight reads without the log. Every number lives in an engine-free rules class, tested in `tests/`.

- **Verbs** (`src/Combat/Core/DefenseRules.cs`: `DefenseRules`, `DefenseOutcome`, `DefenseState`). Dodge: 0.25 s at 11 m/s, 20 stamina, 0.6 s cooldown, every class, Left Alt; untouchable by a creature's melee strike or projectile (anything reaching `Adventurer.ReceiveAttack` with an attacker, or an enemy `Projectile`, which flies on), never by gas, flames, traps or falls (`attacker == null`, `ReceiveHazard`). Perfect block: a blow in the first 0.2 s of a guard (`DefenseState.GuardHeld`, measured from the guard's start, so a long-held guard is an ordinary block), and only when the guard had been down for 0.5 s first (`PerfectBlockRearmSeconds`: tapping the guard is not a free parry). Parry: right mouse for the classes that cannot block, 0.25 s window, 10 stamina per attempt, 0.8 s cooldown, melee only. Each body owns one `DefenseState`; `Adventurer.ReceiveAttack` judges every incoming blow with `Judge` before the hit roll. A parry or a perfect block staggers the attacker 1.2 s (`Enemy.Stagger(seconds, fromDefense)`); a heavy or perfect blow staggers a regular for its hit-react clip (`Enemy.HitReactSeconds`) and clears its wind-up, the attack cycle starting over when the reel ends; a boss reels only for a parry or a perfect block, and an enemy with `StaggerImmune` (the elites plan's Juggernaut) never. Companions on Defensive or Aggressive dodge a telegraphed boss signature half the time (`CompanionBrain.DodgeChance`, decided once per `Enemy.TelegraphId` in its last 0.2 s within 6 m; the companion thinks at once in that window so the burst is still running when the blow lands).
- **Telegraphs** (`src/Enemies/Core/TelegraphRules.cs`). `AttackState`/`ShootState` call `Enemy.BeginWindup`: the attack clip plays from the start of the wind-up at `ClipSpeed` so its strike frame (55% of the clip) falls at the blow, the body tints toward white for the last 0.2 s (`TintAmount`), a shooter's hand glows (`TelegraphFx.HandGlow`). Frost and Haste scale the wind-up and the recovery through the attack-speed factor (`Scaled`, floored at 0.2). A boss signature draws a red `TelegraphDecal` under it for its delay (Slam 1.1 s: a ring of the slam's 4.5 m; Charge 0.7 s: a lane; Volley and Summon 1.1 s: a pulsing circle), removed when it lands or the boss is staggered (`SignatureState.Exit`).
- **Numbers and statuses** (`CombatRules` in `src/Combat/Core/HitResolver.cs`). Hit chance `clamp(0.72 + (Acc - Eva) x 0.006, 0.35, 0.95)`. Shatter: a crit or heavy on a Frosted target deals +50% and spends the Frost. Ignite: Burn landing on a Poisoned target bursts 2 m for the poison's magnitude x 4, once (the first Burn). Bleed halves healing received (`StatusEffectController.HealingMultiplier`). Vulnerable: +25% damage taken for 6 s (`DamageTakenMultiplier`, read by `DamagePipeline.Mitigate`), left by a spell's stun (Deep Freeze); elites and shrines apply it directly. Resists are clamped at 75 in `Mitigate` and in data. `SkillProfile.Trigger2`/`TriggerMagnitude2` let a node grant a second trigger (the items plan gives Bulwark tier 2 Thorns 4).
- **Feedback** (`src/Combat/Core/FeedbackRules.cs`, `src/UI/Hud/`). `EventBus.EnemyHurt(enemy, amount, HitFlavor, byMember)` drives `DamageNumbers` (pooled `Label3D`s, 0.9 s rise, numbers within 0.15 s on one target merged by `DamageNumberMerger`, ticks with ticks only, gold x1.6 for a crit, perfect blow or Shatter, grey "Blocked" when a shield ate it; the "Damage numbers" setting, `Settings.DamageNumbers`, hides them), `HitMarker` (ticks flick 0.12 s when the local member's hit lands, a red X on a kill) and `EnemyBars` (a bar over anything hurt in the last 4 s, 1.2 m above its collider, the name for bosses, none for hordes). `BossBar` polls the nearest aware boss within 40 m (name, the plan's title when it differs, a tick at 50%, a flash at the half, hidden 0.5 s after death); `ShowSlim`/`HideSlim` are the elite bar the elites plan calls. `MemberDamaged` on the local member: a red `Vignette` by the fraction lost, the portrait flashes, and a hit of 15% max HP or more shakes the camera. `LevelUp`: the `LevelUpBanner` and a gold portrait pulse for 2 s. `ItemPickedUp` of Rare or better: the `LootToast`; a Legendary `LootDrop` carries a beam and chimes.
- **Co-op.** A client's dodge and parry windows reach the server as `PoseFlags.Dodge`/`PoseFlags.Parry` on the pose it already sends (sent at once when the flags change); the server's `DefenseState.ApplyRemote` opens a window only on the flag's rising edge, only off cooldown and only when it can charge the stamina in the same step, holds it `RemoteGraceSeconds` (0.1 s) past the client's, and a refused rise stays refused until the flag drops. `EnemyHurt` on the wire carries the flavor and the striker (protocol 7). Telegraphs reach clients as the previously unused `EnemyStateMsg` (`Enemy.ClientTelegraph`: "Attack", "Shoot", "Signature:Slam", and "Stagger", which drops the decal, the tint and the glow); hordes get no message and wind up from the snapshot's state change instead. `--test-combat-feel` checks the whole pillar headless.

## Safe rooms

Every floor seals one room as a safe room (`Adventure/Core/SafeRoomRules.cs`, placed by `FloorGenerator.PlaceSafeRoom`
right after the objective, so the jail, shrine and stairs vault are fixed first and the route runs between the stairs
as the objective left them, and before pits, structures, nests and spawns, so nothing lands inside it): a dead end with
a single entrance within ten cells of the stairs-to-stairs route, on the ground with no ramp, ledge or cellar, never a
stairs room, the arena, a vault, the shrine or a room holding an objective's cell; failing that a side room carved off
a room on the route, a dead end farther away, a side room off any open room, or a small two-door chamber with one door
walled up. A floor of one room (the tavern) has none. A room the floor already had is shuffled with the others when
the nests are placed and skipped after, so sealing it leaves the breaches where they were.
Its iron-banded door (`VaultKind.SafeDoor`, no prompt, a lit lantern on the corridor side) opens by itself when a
living member comes within 2.5 m and shuts 2 s after all of them are beyond 3 m; no creature passes it, paths into the
room or spawns there (`DungeonBuilder.IsCellBlockedForEnemies`), even while it stands open (the open door keeps an
enemy-only barrier on collision layer 16, below), and the toxic gas never gets in (`GasField`).
`Adventure/SafeRoomController.cs` runs it on the server or in solo: each living member inside regenerates hp, stamina
and mana six times faster (a member outside heals normally; a downed body is not revived), and while anyone is inside
the objective line ends with "Safe room" (the line reaches clients with the objective message). Resting is not free:
after 20 s inside, one or two of the tier's creatures gather outside, 4 to 8 cells from the door and out of its
straight line, and hold there awake (`HoldState`, log `[gather] ... (groups G, creatures C)`; `EventBus.Gathered`
carries the creatures gathered on the floor so far, for the merchant's remarks); another group every 40 s, never while
eight of them are alive. They go for the party when it steps out, and one that loses it walks back to its post and
holds there again, awake (`Enemy.HoldsPost`). An empty room pauses the clock and after
30 s starts it over; the count of groups sent is saved with the floor (`FloorStateDto.Gathered`), the creatures
themselves are dynamic and not saved. The gathering ignores the "Reinforcement pressure" setting; `--no-gathering`
turns it off for a run. A safe room changes a floor's `ContentHash`, so plans written before it log "adapted";
`--rehash-plans=DIR` refreshes the `LayoutHash` of every `floor_NN.json` in DIR (the shipped plans in
`data/adventures/builtin` were rehashed when safe rooms arrived, and on five floors what stood in the room the safe
room took was moved by hand). `--test-safe-room` checks all of it headless, including a creature held at the open
door and a gatherer going back to its post.

**After any generator change, sweep the shipped floors.** A rehash only stops "adapted"; a plan whose rooms changed
role still loads with its pieces silently dropped. Every shipped floor must load with no repair line:

```bash
dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly
godot --headless --path . -- --rehash-plans=data/adventures/builtin --quit-after=2
for N in $(seq 1 20); do echo "== floor $N"; timeout 60 godot --headless --path . -- --builtin --floor=$N --quit-after=4 --no-ambush --no-portals --no-gathering 2>&1 | grep -iE "repaired|dropped|demoted|adapted"; done
timeout 120 godot --headless --path . -- --builtin --quit-after=4 2>&1 | grep -c adapted   # 0
```

Any floor that prints a line gets its `floor_NN.json` edited by hand, minimally, keeping the story: move what was
dropped to a live room (not the safe room, start, a vault, the jail or the shrine), and when a room named in
narration, a quest's lines or a horde's line lost its role, move the room's identity (role, name, its narration,
traps and clue) with its contents rather than re-pointing the story. Edit with a .NET-compatible JSON writer (two-space
indent, `\u0027`-style escapes) so only the edited values change. `could not place: npc ...: waits on flag ...` lines
are expected (NPCs gated on an earlier floor's choice).

## Elites, boss phases and the endless Heralds

From floor 2 one planned creature per floor is an elite (two from 8, three from 14; `EliteRules.CountFor`, plus whatever *Hostile
Tenants* and *Marked* add through `DungeonBuilder.ExtraElitesPerFloor`), chosen and rolled in `PlanResolver` from the floor seed and
the spawn index (`src/Enemies/Core/Elites/EliteRules.cs`, names from `EliteNames`), so peers and reloads agree; the save keeps the
floor's `EliteSpawns` so a lifted curse never changes who stands there. Twelve affixes (table in the spec) each with a tell
(`EliteVisuals`); Shrouded's dome is checked in `Projectile` and `Adventurer.Nova` (`ShroudRules`), Thorned and Hexed answer through
`PartyController.OnHitLanded` → `Enemy.OnStruckBy`. Reward ×2.5 XP, `table.elite`, coins; events `EliteSeen` (once per enemy) and
`EliteSlain`. The planner may name an elite (`MemberPlan.Elite`/`LonerPlan.Elite`); the validator fills and trims. Bosses: at half
health a 1.2 s roar (untargetable), then speed ×1.2, the data's `SecondSignature` alternating with the first, and the `ArenaChange`
(`BossArena`: braziers out, webs, moving embers, gravity well); the boss's HP is saved with the floor, so a boss loaded below half is
already in phase two. Endless floors 25, 30, … seat "the Nth Herald" (`TierRules.IsEndlessBossFloor`), whose death silences portals on
its five floors (`PortalRules.BossFloorFor`). Archetype flags on `EnemyDef`: `Kites` (archers back off inside half their range),
`Support` (`SupportKind` Shield or Regen on a hurt ally within 8 m every 8 s), `Bomber` (`horde_*_bomber`, one in eight of a stream
from floor 6, 3 m blast ×3, no corpse; never a planned member).
## Fortune and misfortune

Every floor offers decisions that trade risk for power. The numbers live in `src/Adventure/Core/Fortune/` (engine-free,
tested in `tests/`); one Godot node, `Adventure/FortuneDirector.cs` (a child of `Game`, `FortuneDirector.Instance`),
makes them real and is the authority (the server in co-op, solo otherwise).

- **Boons and curses** (`Boons.cs`, `Curses.cs`, `FortuneRules.cs`). 24 boons in four houses of six (the Ember, the Cold,
  the Toll, the Stone), one crown each; 8 curses, plus `scar` (transient, run-structure's wipe penalty, never offered).
  The party holds them as id lists in the save (`AdventureDto.Boons`/`Curses`, v11). A boon reaches play one of four
  ways: plain `StatModifier`s (`Boons.Modifiers`, parsed with `StatModifierParser`) written into every member's
  `CharacterState.FortuneModifiers` by `FortuneDirector.Refresh` and summed in `Recalculate`; a `TriggerKind`
  (`KillStamina`: Hungry Edge) summed into `PartyController.Trigger` by `Boons.TriggerMagnitude`; a static factor the
  director refreshes (`Prices.BuyFactor` from `FortuneRules.PriceFactor` x run-structure's clause factor,
  `TierRules.HazardFactor` read by `TrapRules`/`HazardRules`, `ConsumableRules.HealFactor`); or a hook the director or a
  rules class reads (`FortuneHook`: Ember's Due extends a Burn you land, Kindled Blades 20% Burn on a melee hit, Rime
  Edge Frost on a crit, Stone Crown heals 10 on `PerfectBlock`/`Parried`, Toll Crown pays an elite's XP again and a
  chest-sized purse on `EliteSlain`, Ferryman and Second Skin refill `CharacterState.DeathWards`/`NegateHits` on every
  `FloorBuilt` (consumed in `TakeDamage`), Cold Comfort and Slow Blood halve a status on you through
  `StatusEffectController.DurationScale`, Stone Lungs thins the gas for the party loop only (`ToxicController`), Light
  Fingers adds 0.25 to `LockRules.Chance`, Ember Crown widens `Enemy.IgniteAround` to 4 m, Cold Crown makes a Shatter
  leap to the nearest Frosted foes within 4 m (`Adventurer.ShatterChain`), Loud scales `Enemy.Aggression`, Marked is read
  by the elites plan as `FortuneRules.ExtraElites`). `Refresh` runs on `FloorBuilt`, `RosterChanged`,
  `BoonTaken`/`CurseTaken` and whenever the party's member array changes (a load rebuilds it), and on the server
  broadcasts `FortuneSync`.
- **The shrine** (`ShrineRules.cs`, `Dungeon/Props/Shrine.cs`, `UI/Fortune/ShrineCard.cs`). `FloorGenerator.PlaceShrine`
  (the last pass, no rng) reserves one cell per floor from floor 2 (`FloorLayout.ShrineCell`, `IsReserved`): a room that
  is not the stairs', the arena, the safe room, the objective's jail or shrine, a vault or a secret room, with at least
  five free cells; dead ends first, then nearest the midpoint of the stairs route; the cell nearest the room's centre
  that holds nothing, is a cell away from every entrance and cuts nothing off when blocked. `DungeonBuilder.PlaceShrine`
  puts the altar there (`AltarMeshes`, shared with the objective's altar) with its price written over it. One in three
  shrines is priced (`PriceFor`, actor seed 9001): blood (a quarter of every living member's current HP, straight off the
  breath), gold (60 + 15 x floor; a broke party is refused and the shrine stays usable) or a curse (then four boons on
  the card). The card (`ShrineCardMsg`): three boons by `Offer` (actor seed 9000; boons not held, weighted 1 + 2 per
  boon of the house held; a crown enters when two of its house are held and is always the first offer; two under
  run-structure's Fine Print, read reflectively until `AdventureDto.Clauses` exists), a lift row for the first curse
  carried (80 + 20 x floor), and walking away (40 + 10 x floor gold). Keys 1-4, L, Esc or Space; mouse the same; one
  pick per player. Solo the card calls `FortuneDirector.ResolveShrine`; a client sends `ShrineChoiceIntent`, the server
  takes the first pick, closes the card everywhere with `ShrineClosedEvt` and sends `FortuneSync`. The shrine is one
  use (`FloorStateDto.ShrineUsed`, `PropEvt "used"`). `ShrineUsed(member, isBoon)` feeds the barks and the villain.
- **Chest kinds** (`ChestRules.cs`, `data/loot_tables/{vault,secret,cellar,boss}.tres`). `DungeonBuilder` gives every chest
  a `TableId` by `ChestRules.KindOf(layout, spawnIndex)`: a vault's prize rolls `table.vault` (0/25/55/20, 3 rolls), a
  secret alcove's or room's `table.secret` (0/40/45/15, 2 rolls, plus one consumable), a cellar's and a ledge's
  `table.cellar` (the chest odds with 4 rolls; the spec names no ledge table), the rest `table.chest`. The boss rolls
  `table.boss` (0/15/50/35, 3 rolls) two item levels up (`BossItemLevelBonus`); the elites plan inserts `table.elite`
  in the same expression (`Enemy.OnDeath`).
- **Mimics** (`MimicRules.cs`). From floor 4 a tenth of the ordinary chests (a fifth from floor 12) are mouths, rolled
  from actor seed 7000 + spawn index (`IsMimic`; `ForcedSpawnIndex` for tests). A living member within
  `TrapRules.DetectRadiusCells(Perception)` cells + 1 m reads "the lock is breathing" in the prompt. Opening one
  (`Chest.Spring`) hides the chest and spawns the tier pool's heaviest non-boss melee creature (`BruteFor`; the data's
  `horde_*_brute` are 34-HP swarm fodder) named "Mimic", Hulking, with twice the XP, awake; `PropEvt "mimic"` hides it
  on clients; `MimicSprung(at)`. The chest yields its own roll (`Chest.Yield`, the chest's seed 5000 + index) when the
  brute dies (`FortuneDirector` on `EnemyDied`). A save mid-spring keeps the spawn index in `FloorStateDto.MimicsSprung`
  and `RestoreSprung` respawns the brute idle at the chest, so the roll is neither lost nor free.
- **The bound chest** (`FortuneRules.BoundFromFloor` 5). `FloorGenerator.PlaceBoundChest` (seed ^ 0xB0D) picks one
  ordinary chest outside the boss arena (`FloorLayout.BoundChest`); the builder marks it `Chest.Bound` (a purple seam,
  never a mimic, never locked, `table.vault`), prompt "Bound chest: open and be Hunted?". Opening it rolls and then
  `FortuneDirector.StartHunt`: every living member Vulnerable for 60 s (`HuntedSeconds`, magnitude 0.25) and "the
  Hunter" (the pool creature by actor seed 7999, affixes `HunterAffixes` = Swift, Vampiric: the contract's list has no
  "Marked") spawns awake at the centre of the nearest other room. `FloorStateDto.BoundOpened` so a reload does not hunt
  twice.
- **Fortune's elites.** `Adventure/FortuneSpawns.cs`: `Elite(builder, baseProfile, name, affixNames)` runs the affix names through
  `EliteRules.Decode` and `EliteRules.Apply`, so the mimic's brute (Hulking) and the hunter (Swift, Vampiric) are elites in full: tells,
  x2.5 XP (the mimic's own `XpFactor` applies only when no affix made an elite), `table.elite`, `EliteSeen`/`EliteSlain` (the Toll Crown
  pays on them), and `DynamicEnemyDto` carries name and affixes to clients. Marked adds one to the floor's elite count through
  `DungeonBuilder.ExtraElites` (`FortuneRules.ExtraElites`).
- **Secret rooms** (`SecretRoomRules.cs`, `FloorGenerator.PlaceSecretRooms`/`TryCarveSecretRoom`, seed ^ 0x5EC2). Half
  the floor's secret alcoves (`RoomTarget`) open into carved 3-5 cell rooms behind their wall: the even indices try
  first (`WantsRoom`), the odd ones only until half are rooms (alcoves on the map's edge or beside a corridor cannot
  carve). Every size and doorway offset is tried in a seeded order, flush against the alcove first and then up to
  three cells of passage behind it; a room may touch no walkable cell but the alcove, so the alcove stays its only way
  in. Rooms are appended to `Rooms` (plan indices hold) with a `Connections` entry to the host; `FloorLayout.SecretRooms`
  records `SecretRoom(SecretIndex, Room, Doorway, Chest, Plaque, Merchant)`; the alcove's chest moves to the room's
  centre (`ChestKind.Secret`); `RoomCells.Free` is empty for a secret room so no planner puts anything in a sealed
  room, and `ShrineRules.Choose` skips them. `DungeonBuilder.PlaceSecretRooms` (after `PlaceProps`) spawns the plaque
  (`SpawnPlaque`, index 100 + secret index) with the campaign's clue for the floor (`Mystery.Clues`) or an epigraph
  (`Epigraphs.For(seed, floor, index)`, forty stock lines that never name a shipped campaign), and on the tier's
  hidden-stall floor (`HiddenStallFloor(tier, seed)`: the second, third or fourth floor of the tier, never the boss
  floor) a merchant who was never here in the first room (`PlaceMerchant(..., hidden: true)`: `Npc_merchant_hidden`,
  `Npc.HiddenStall`, lines from `MerchantLineBank.HiddenGreeting`; the stall is simply absent when that floor carved
  no room). `ShopRules.Roll(..., hiddenStall)` appends `HiddenStallCount` (6) Rare pieces of gear after the regular
  wares from its own seed (`ShopStock.HiddenFrom`); `ShopScreen.Open(floor, keeper, hidden)` shows one segment, and
  `PartyController.TradeReachFor` says which keeper a body reached (`TradeReach`: None/Merchant/SafeRoom/Hidden) so
  `ShopRules.SellsHere`/`TryBuy` sell only that keeper's segment ("Not sold here."), on the server path too. Finding:
  `FortuneDirector` (every 0.25 s) raises `SecretFound(floor)` once per secret when a living member stands in a secret
  room (or its doorway) or an alcove-only secret's wall is open; saved per floor as `FloorStateDto.SecretsFound`;
  `AdventureDirector` counts `FloorRecord.SecretsFound`/`SecretsTotal` for the floor card ("Secrets 1/3").
- **Locks** (`LockRules.cs`). `PartyController.TryUnlock(what, out failed)`: a key opens at once; else the best hands pick
  with `LockRules.Chance` (the trap rule by Dexterity, +0.25 Light Fingers, 0 under Butterfingers; `ForcedChance` for
  tests). A failed pick (not a cooldown refusal) is a noise: every unaware creature within 10 m of the lead
  (`NoiseRadius`) wakes. `Chest` counts `LockFails`; the third jams the lock (`Jammed`, "The pick snaps off in the
  lock.", `PropEvt "jammed"`, `FloorStateDto.JammedChests`), after which only a key opens it; an opened jammed chest is
  simply open. Doors do not jam (the noise applies).
- **The Lucky Dip** (`GambleRules.cs`). The shop's third tab: three bundles a floor (`DipSlot` Weapon/Armour/Jewellery)
  at `Price(floor, discount)` = 120 + 30 x floor less Pellet's Receipt's fraction (`PartyController.LuckyDipDiscount`),
  each rolled once by `Roll(cat, seed, floor, slot)` (actor seed 8000 + slot) at item level floor + 2 with `table.chest`'s
  odds under a 10% legendary floor, carried in `ShopStock.Dip` so a reload cannot reroll it. `PartyController.LuckyDipBuy`
  (refusals "Nothing there.", "Already bought.", "Not enough gold." + `BrokeAtShop`, "The bag is full.", and "Not sold
  here." at the hidden stall) reveals the item, raises `Gambled(member, win)` (Rare or better wins); bought slots live in
  `LuckyDipBoughtOnFloor` (saved as `FloorStateDto.LuckyDipBought`, cleared on a floor change, synced by `LuckyDipSync`;
  a client sends `LuckyDipIntent`). `ShopScreen.BuyDip(slot)` is the tab's path; the tab is hidden at the stall.
- **Drift caches** (`FloorGenerator.PlaceCaches`, seed ^ 0xCAC4E). `DigDrifts` records each mine drift in
  `FloorLayout.Drifts` (its first step may touch the gallery's own edge cells: before that check the drifts were never
  dug). Each drift whose end is free holds a `DriftCache`: one in three an ambusher (an idle pool creature spawned by
  the builder with spawn index `CacheAmbusherBase` 91000 + cache index; like the viewer orc, a corpse freed before a
  save returns on reload), else one in three a locked chest (the floor's keys serve) or a breakable crate. Junk sells
  for 2 gold (`Prices.JunkValue`) so the companions still pick it up.
- **Save and wire.** Save v11 fields: `AdventureDto.Boons`/`Curses`, `FloorStateDto.ShrineUsed`, `BoundOpened`,
  `MimicsSprung`, `SecretsFound`, `LuckyDipBought`, `JammedChests`; a v10 save loads with none. Protocol 8 (88 kinds):
  `ShrineCard`, `ShrineChoiceIntent`, `ShrineClosed`, `FortuneSync`, `LuckyDipIntent`, `LuckyDipSync`;
  `NetRefs.Shrine(0)` = "shrine:0". Test hooks: `ShrineRules.ForcedPrice`, `MimicRules.ForcedSpawnIndex`,
  `LockRules.ForcedChance`. Secret rooms and drifts change a floor's `ContentHash`: the shipped plans were rehashed when
  fortune arrived (see the sweep above), and the same sweep is due after any further generator change.

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

## Items, legendaries and skill trees

Item level is the floor (`TierRules.ItemLevel`). Base damage and the Armor base modifier scale by +4% per item level above the base's own `ItemLevelMin` (`ItemRules.BaseScale`, derived from `ItemInstance.ItemLevel`, so saves store nothing new). Affixes have tiers at item levels 1–8 and 12/16/20 (data/affixes); on-hit affixes grow their status magnitude +25% per tier; rolled values round to whole numbers, 2 decimals for crit stats, 1 otherwise. Six affixes added: Cruel, of Reverie, of Breath, of Dimming, of the Owl, of Alacrity (the `Cooldown` stat, applied in `SkillRules.ApplyUpgrades`). Smart loot: with `ItemCatalog.SmartLootClasses` set (the party does it), 60% of rolls restrict the base pool to what a living member can use. Legendaries (`LegendaryDef`: `Trigger`, `TriggerMagnitude`, `Trigger2`, `TriggerMagnitude2`, `Upgrades` in the `UpgradeParser` syntax `"ability.x,ability.y: Key=Value ..."`) are read off equipment by `LegendaryRules`; `PartyController.Trigger` sums them with skills, `GetAbilityFor` applies their upgrades. Firing sites: kills/blocks/hits as before, `OnItemHit` (Burn spread), `OnItemMemberDamaged` (Gerald), coins in `LootDrop.Interact`. Hooks other systems read: `CanGuard`, `ParryWindowBonus`, `StaggerImmune`, `HeavyStunSeconds`/`ShatterStunSeconds`, `PerfectBlowCleave`, `SneakBonus`, `QuietKills`, `ArrowsShatter`, `LuckyDipDiscount`, `SellBonus`, `GoldFindBonus` (`PartyController.Items.cs`). Skills: 88 nodes; each class has a Duo node at tier 3 with `AnyOf` prerequisites (any branch's tier 2); tier 6 is a keystone with a price (data/skills, `IsKeystone`); the Ranger's third branch is Wild Hunt (ids keep `beastcraft`). XP: `25L²+50L`, cap 30. Shop: stacks are priced by count, the last three sales sit in the Buyback tab (`BuybackLedger`, saved in `PartyDto.Buyback`, synced in `ShopSync`, protocol 7), cells show ▲ for an upgrade over what the focused member wears and a class glyph row.

## Layout

- `src/` C# by namespace (`Core`, `Party`, `Characters`, `Combat`, `Enemies`, `Dungeon`, `Items`, `Save`, `UI`, `Adventure`).
  Engine-free logic (`Characters/*.cs`, `Combat/Core`, `Items` core, `Dungeon/Generation`, `Core/Rng.cs`, `Save/Core`, `Adventure/Core`, `Enemies/Core`, `Party/Core`) must never reference `Godot`; the test project compiles it directly.
  Party bodies carry a `ControlKind` (`Party/Control/Control.cs`: LocalHuman, Companion, Remote, Parked); `PartyController.LocalIndex` is the body this machine drives (-1 on a dedicated server), `FocusedIndex` the member the screens show, `Anchor` the body everyone else follows, `Active` the bodies in the world. Movement keys are read in one place (`LocalInput.Read`) into an `InputIntent`; bag moves go through `Party/Core/BagActions.cs` so a server can run them from the wire. Godot Resources live in `*/Defs` and `Items/ItemDef.cs`.
- `scenes/` `.tscn` files. `data/` definition Resources (`.tres`): `classes/`, `abilities/`, `items/`, `affixes/`, `legendaries/`, `loot_tables/`. Registry loads them by `Id` at startup; `Items/Catalog.cs` turns them into the plain `ItemCatalog` the roller uses. `assets/` third-party art (log licenses in `assets/LICENSES.md`).
- `tests/` xUnit project, ignored by Godot via `.gdignore`.

## Runs, clauses, deaths and records

The Depths' pillar 5 (`docs/superpowers/specs/2026-11-07-the-depths-update-design.md` section 5). Engine-free rules under
`src/Adventure/Core/Run/` and `src/Save/Core/Profile/`; the Godot glue in `Main`, `MainMenu`, `Hud`, `src/UI/Cards/`,
`src/UI/Menus/RecordsScreen.cs`, `src/Adventure/Run/DeedWatcher.cs` and `src/Save/Profile/ProfileStore.cs`.

- **Three title modes** (`AdventureDto.Run`, a `RunKind`; `AdventureDto.Mode` keeps meaning who planned): *Story* plays the
  campaign on disk (`StartBuiltinCampaign`), *New Descent* rolls a fresh seed through the offline designer or the planner when
  a key and the toggle allow it (`Main.StartDescent`), *Daily Descent* (`Main.StartDaily`) seeds from the UTC date
  (`DailyRules.SeedFor`, the same on every machine) with two fixed rank-1 clauses (`DailyRules.ClausesFor`, never the Death
  Clause); the button is disabled once the day is recorded (`ProfileRules.DailyDone`); the attempt's save still loads and quickloads.
  Every run carries a fresh `RunId` (a save from before run ids gets one on migration or load); `SaveManager.MarkFallen` marks the
  slots of that run only (`SaveFormat.SameRun`), never every Story on the shipped seed.
- **The clauses** (`ClauseRules`, `ClauseSet`; keys `deathclause, conveniencefee, inventoryaudit, fulloccupancy, hostiletenants,
  expediteddelivery, hazardpaywaived, norest, opendoor, fineprint`, "key:rank" above rank 1). The factor offers them after
  the deal: `TavernRules.WithClauseNode` splices a `clauses` node between `deal` and `sendoff` ("Not today." at index 0,
  "Read them." raises `DialogueEffect.Clauses`); the bank and the shipped `campaign.json` carry it (Pellet's line is unvoiced
  until the voice pass). The `ClauseCard` (Sign/Skip, read-only on a client and in the Daily) caps each rank by the deeds
  (`ClauseRules.MaxRankAllowed`). `Main.SignClauses` writes `AdventureDto.Clauses` and `Game.ApplyClauses` activates them:
  `ClauseRules.Active`, `TierRules.ClauseDamageMultiplier`, `DungeonBuilder.ExtraElitesPerFloor`, a fortune `Refresh` for prices.
  The pause menu shows `ClauseSet.Summary`. `--clauses=a,b:2` binds every new game in the process (a server's world too; the
  Welcome carries them). Heat multiplies gold and XP found by `1 + 0.1 × heat` (`ClauseSet.ScaleReward` in `AwardXp` and
  `AddFoundGold`).

| Clause | Heat | Knob |
|---|---|---|
| Death Clause | 3 | a wipe ends the run (memorial, recap, quickload refused, the pause menu's Load hidden) |
| Convenience Fee | 1 | `Prices.BuyFactor` ×1.4 through `FortuneRules.PriceFactor(adv, clauseFactor)` |
| Inventory Audit | 2 | `InventoryAudit.Apply` at floors 6, 11, 16 (first visit): the lowest-level equipped item per member loses its last rolled affix (never a legendary's own line) |
| Full Occupancy | 1–2 | `OccupancyRules.Apply` adds round(n × 0.25 × rank) copies to the resolved contents, on free cells of the same room, seeded |
| Hostile Tenants | 1–2 | `DungeonBuilder.ExtraElitesPerFloor` += rank (summed with fortune's Marked in `ExtraElites`) |
| Expedited Delivery | 1–2 | `PortalRules.Grace(intensity, rank)` is 45 s at any rank; `Interval`'s ramp halves per rank |
| Hazard Pay Waived | 1–3 | `TierRules.PartyDamageScale = Lethality × (1 + 0.2 × rank)` at `EnemyProfile.BuildAttack`, `GasRules.Tick`, `TrapRules.Attack` (lethal); the hazards' kill threshold keeps the lethality alone |
| No Rest for the Hired | 2 | `FloorSpec.SafeRoom = false` below the tavern: no safe room, the merchant at `NoRestRules.MerchantCell` by the up stairs (the layout hash changes, so shipped plans log "adapted" under this clause) |
| Open-Door Policy | 1 | `AmbushRules.Count(floor, doubled: true)` |
| Fine Print | 1 | `ClauseSet.ShrineOffers(3)` = 2 in `FortuneDirector.OpenShrine` |

- **The stakes.** `PartyController.OnPartyWiped` reads the cause off `CharacterState.LastHarm` (`WipeRules.CauseOf`: the
  latest attack or hazard any member took; status ticks never overwrite it, so a poison death blames the biter), lets the
  director refine a generic one from its notes (`AdventureDirector.ResolveWipeCause`), then `Main.OnPartyWiped` captures the
  floor (`FloorStateCapture`, so the dead stay dead through the reload), takes a fifth of the gold (`WipeRules.GoldAfterWipe`),
  lays fortune's `scar` curse (−10% max HP) with `AdventureDto.ScarUntilFloor = floor + 1` (`RunRules.ScarLifted` on any other
  floor), and shows the `DeathCard` (epitaph from writing's `Epitaphs.Pick`, cause, killer, floor, wipe count; "Rise").
  Causes: `gas|press|spikes|boss|horde|elite|fall|mimic|bomber|generic` (`HarmCauses`, the same ids as `WipeCause`). Under the
  Death Clause the button is "To the Records": `Adventure.Fallen`/`Epitaph`, `Main.RunOver`, the memorial written to the auto
  slot, `SaveManager.MarkFallen` greys every slot of that seed in Load ("Fallen on floor N — epitaph"), `LoadGame` refuses it.
- **Autosave** (solo): the `auto` slot on every floor change and every 180 s (`RunRules.AutosaveDue`: never while rebuilding,
  paused, networked, under a card or after the run ended).
- **The cards.** `FloorCard` (4 s, Esc dismisses, never locks input) from the closed `FloorRecord` once the deeper floor stands
  (`AdventureDirector.CloseRecord`/`OnFloorBuilt`, `RecapBuilder.FloorCard`; the ledger's `WorstMoment` from `LedgerRules`;
  the reaction is the lead's Travel line, not marked said). `RunRecap` on victory (`Main.OnBossDefeated` → `ShowRecap`,
  headline `RecapBuilder.VictoryHeadline(boss, antagonist)` from the floor plan's boss or the last tier's `BossName` and the
  campaign's antagonist) and on a Death Clause end; per hero (`FloorRecord.KillsBy/DamageBy/DownsBy/RescuesBy/PossessSeconds`),
  elites, boons, clauses, deepest floor, score; in co-op `RecapRules.RoleLines(floors, players)` (Most rescues, Vanguard,
  Steadiest, Longest watch; never a damage rank). `Victory` is saved. Score: `depth × 100 × (1 + 0.1 × heat) + 25 × elites
  + 10 × secrets − 50 × wipes`, never below 0 (`ScoreRules`).
- **Profile** `user://profile.json` (`ProfileStore`: atomic temp + `.bak` + rename, `.bak` fallback; `ProfileFormat`; the
  flows use `profile-test.json`): `Records` (best depth, fastest story clear, the first clear per `{run}:{heat}`, victories,
  elites, runs), the last twenty runs (`ProfileRules.RecordRun`, upsert by `RunId`, written at every floor change and the
  run's end), the dailies, the deeds and their unlocks. Heat 5 and 10 clears give the title ("Tenant in Good Standing",
  "Whole Contract") and the gold or ember border on the **Records** page (`RecordsScreen`).
- **Deeds** (`DeedRules`, 24; `DeedTracker` turns `DeedSignal`s into earned deeds; `DeedWatcher` feeds it from the EventBus,
  solo and server): each unlocks a clause rank (`ClauseDef.RankUnlockedByDeed`), a legendary (`LegendaryProfile.RequiresDeed`,
  `ItemCatalog.Unlocked`, the six: Toll Road Boots, Old Ulf's Chin, Gerald, Gorm's Molar, Hush, Pellet's Receipt), a banter set
  (`BanterBank.Unlockable`, gated by `BanterRules.UnlockedSets`) or a hidden-stall line (`MerchantLineBank.UnlockableLines`,
  `UnlockedLines`, one extra Rare roll each through `ShopRules.HiddenStallExtraRolls`). `ProfileStore.ApplyUnlocks` applies
  the legendaries and banter sets at start and after every deed; the stall's lines are fixed at a run's start from the host's
  profile (`AdventureDto.UnlockedMerchantLines`, in the save and the Welcome, applied by `Game.ApplyClauses` on every peer, so
  all of them roll the same stall; a line earned mid-run joins the next run). A deed pops a `DeedToast` and a log line.
- **Co-op**: the server decides and sends `DeathCardEvt`, `FloorCardEvt`, `RunRecapEvt` (protocol 9, 91 kinds); a client
  shows them and signs nothing (its clause card is read-only). Hooks left for the co-op plan: `RecapRules.RoleLines` is the
  scoreboard's seam; the stairs ready-check and the ping are not here.

## Multiplayer (dedicated server)

Co-op runs on a dedicated server (step-by-step setup of one, from a fresh cloud account to a running service, in [server-setup.md](server-setup.md)): the game started headless with `--server`, which any copy of the game joins from the title screen (Join Server) with a name and a class. Each player controls one adventurer; there are no AI companions on a server. The server simulates the world and every combat roll; clients own their own movement and send intents (attack, interact, bag moves, stat points, skills, dialogue choices, chat). Floors are built identically on every machine from the seed and the plan the server sends, so only divergent state crosses the wire. The stairs are a vote when two or more players are online: a Yes on the landing starts a ready-check and every HUD reads "Descend? 1/3 ready · 12 s" (`StairsReadyMsg`, once a second); the floor changes when all living players are ready (a downed player is not waited for) or when 15 s pass with a majority; a No from a living player on those stairs calls it off (so does nobody agreeing at 15 s); stepping off the landing is not a No. One player online takes the stairs at once, as a solo game does (`ReadyCheckRules.Needed`). The rule is the engine-free `ReadyCheck` in `src/Net/Core/ReadyCheck.cs`, the server's instance lives in `src/Net/ServerReadyCheck.cs` (`ServerWorld.StairsVote`, which `Stairs.Interact` consults on a server), and a client's Yes or No travels as `StairsReadyIntent` (`Stairs.Confirm`/`Decline`). The server re-checks `Locked` at Go time and logs the lock reason instead of changing floor. Talking to an NPC is local, but what the talk decides is the server's: a Join choice goes up as a `DialogueChoiceIntent` and the fighter's yes or no comes back as a `DialogueAnswer`, so the asker sees the right line and the follow starts on the server; freeing a prisoner, starting the rite or getting a downed fighter up go up as an `InteractIntent` on the NPC. Fighter NPCs only pick a fight while a standing party member is within 20 m (`NpcFight.PartyNear`), so Foehammer's bout with the orc by the stairs waits for the players instead of being settled while the server sits empty. A client never changes floor on its own: `Game.ChangeFloor`, `ReloadFloor` and `JumpToFloor` refuse on a client (a log line says the server decides), so debug keys, scripts and stray calls cannot leave a machine on a floor the server is not simulating; the only client path is the server's `FloorChange`.

```
godot --headless --path . -- --server --port=7777 --bind=0.0.0.0 --campaign=offline|builtin|ai --seed=N --clauses=a,b:2 --save=server --max-players=4 --autosave=60 --status-file=PATH --quit-when-empty
godot --path . -- --join=ws://192.168.1.5:7777 --name=Ash --class=class.rogue     # skip the title screen
scripts/export_server.sh                                                            # "Linux Server" preset (dedicated_server=true) into build/server/
SERVER_BIN=build/server/DungeonCrawler.x86_64 scripts/net_test.sh                   # the same check against the exported server
```

In a game: T opens the chat line, the floor label shows the round trip and how many are online, other players wear name tags, and the pause menu's Save/Load are replaced by Disconnect (the world never pauses on a server). The exported server prints a page of "Unreferenced static string" lines at startup; that is export-template noise, not a fault.

- Saves: the server autosaves to `user://saves/<--save>.json` every minute, on every floor change, and when someone leaves; started again on the same slot it resumes the world with every character parked, and the same name gets its character back (stored class wins). Save version 7 adds `Multiplayer` and `Players`. Clients never save; a single-player save in the server's slot is left alone.
- Render (`deploy/`): `Dockerfile` (debian-slim + the exported server + FastAPI), `frontdoor.py` (`GET /` health JSON from the server's status file, `GET /status`, `WebSocket /ws` relayed to the Godot server on 127.0.0.1:7777), `start.sh` (Godot then uvicorn on `$PORT`, saves and keys on the `/data` disk, `CLAUDE_API_KEY` secret written to `keys.cfg`), `render.yaml` (Docker web service, health check `/`, 1 GB disk, `CAMPAIGN`/`MAX_PLAYERS`/`SEED` env). Players join with `wss://<service>.onrender.com/ws`. Free instances sleep and drop everyone; use a paid plan. Local check: `docker build -f deploy/Dockerfile -t dc-server . && docker run -p 10000:10000 -v dc-data:/data dc-server`, then `scripts/net_test.sh relay ws://127.0.0.1:10000/ws`.

- Google Cloud (or any Linux VM) (`deploy/gce/`): no Docker and no front door, the server listens on the port you open in the firewall. On the dev machine `scripts/export_server.sh`, then `gcloud compute scp --recurse build/server deploy VM:~/dc --zone=ZONE`; on the VM `sudo bash ~/dc/deploy/gce/install.sh ~/dc/server` installs the runtime libraries, a `dungeon` system user, the build under `/opt/dungeoncrawler`, saves and keys under `/var/lib/dungeoncrawler`, and a systemd service (`dungeoncrawler.service`: port 80 by default, with `CAP_NET_BIND_SERVICE` so no root is needed; `sudo systemctl edit dungeoncrawler` to change the port, campaign or player count). Open the port with `gcloud compute firewall-rules create dungeoncrawler --allow=tcp:80 --target-tags=dungeoncrawler` and tag the VM. Players join with `ws://<external ip>` (port 80 needs no suffix). Re-run the same two commands to update; the service is stopped and restarted around the copy and keeps its saves. To run from the clone itself instead of an export, `deploy/gce/setup_source.sh` installs the runtime libraries, Godot 4.7.2 .NET and the .NET 8 SDK user-local (idempotent), imports and builds; `deploy/gce/start.sh` then runs the server in the foreground from `build/server` if present, else the latest published release, else from source. With git instead of scp: `scripts/publish_server.sh` exports and publishes the build as a GitHub release asset (`server-<date>` tag, the build itself stays out of git); on the VM, `git clone` the repo once and `deploy/gce/fetch.sh` pulls the scripts, downloads the latest `server-*` release (the repository is public, so no login) and installs it. Day to day on the VM, `deploy/gce/game.sh` is the menu: `restart` (resume the saved game), `fresh` (a new game from floor 1, the old save kept as `server-<date>.json`), `update` (fetch.sh), `status` (service, status file, the address players use), `logs`, `stop`.
- macOS: the same server runs on a Mac. Either run it from the project with the macOS Godot 4.7.2 .NET editor and the .NET 8 SDK (`godot --headless --path . -- --server ...`), run the Linux Docker image under Docker Desktop, or use the exported bundle: `scripts/export_server.sh` also produces `build/macos-server/DungeonCrawler-server.zip` (a universal, ad-hoc-signed `.app`; the "macOS" preset makes the client the same way). Unzip and run `DungeonCrawler.app/Contents/MacOS/DungeonCrawler --headless -- --server --port=7777 --bind=0.0.0.0 --campaign=builtin`; macOS asks once to allow incoming connections, and saves go under `~/Library/Application Support/Godot/app_userdata/DungeonCrawler/`. The bundle is not notarized, so on another Mac right-click, Open once (or `xattr -dr com.apple.quarantine DungeonCrawler.app`).
- Transport: Godot's `WebSocketMultiplayerPeer` used as a raw packet peer (`src/Net/WebSocketTransport.cs`) behind the engine-free `INetTransport`; `ws://` on a LAN, `wss://` through a TLS-terminating host such as Render. No RPC, no `MultiplayerSpawner`: every message is a record in `src/Net/Core/Messages.cs`, encoded by `NetCodec` (one kind byte, JSON with the save layer's options, gzip above 8 KB; `Pose`, `WorldSnapshot` and `Vitals` are binary).
- Engine-free core in `src/Net/Core/` (linked into the tests): `Protocol`, `PoseBuffer` (100 ms interpolation, 150 ms extrapolation cap), `PlayerRegistry` (name = identity, stored class wins, park/unpark), `ServerHandshake`/`ClientHandshake` (Hello -> Welcome -> Ready), `InMemoryTransport` for tests, `NetRefs` (`chest:3`, `door:1`, `stairs:down`, `loot:<guid>`).
- Godot glue: `NetSession` autoload (role, transport, command-line flags), `ServerWorld` (registry, welcome from a live `FloorStateCapture`, poses in, snapshots at 20 Hz with interest filtering, vitals at 10 Hz, log/sfx/objective forwarding, status file), `ClientWorld` (poses out at 30 Hz coalesced, snapshot/vitals in, remote bodies through `RemotePose`), `NetTestFlow` (the scripted check).
- Protocol 7 (the Depths): `PoseFlags.Parry = 64` and `PoseFlags.Dodge = 128` carry a client's defensive windows (the server judges the hit with its own `DefenseState`, fed by `ApplyRemote`); `EnemyHurt(NetId, Amount, Flavor, Hp, ByMember)` replaced the crit flag; `EnemyStateMsg` relays a non-horde enemy's wind-up or signature telegraph. `MessageKindCount` 82 (`CheerIntent` from the writing pillar, `ShopBuybackIntent` from items). Protocol 8: elites and fortune (88 kinds). Protocol 9 (run-structure): `DeathCardEvt`, `FloorCardEvt`, `RunRecapEvt` appended (91 kinds); the Welcome's `AdventureDto` carries `Clauses`, `Run` and `DailyDate`. Protocol 10 (co-op): `StairsReadyIntent`, `StairsReadyMsg`, `PingIntent`, `PingEvt` appended at the end (95 kinds); a ping is `Hud.Ping` -> `PingIntent` -> the server's cooldown and range check (`PingRules`) -> `PingEvt` to everyone -> `PingMarkers.Place` (the `Label3D` under the floor root and the `PingBoard` the minimap draws). `FloorChange` gained `EliteSpawns` (the floor's elite spawn indices): a client's first visit places the server's roster (`DungeonBuilder.ForcedEliteSpawns`) instead of rolling its own from its copy of the extras, and prints `[net] floor N elites match|MISMATCH`.
- A player who leaves is parked (body out of the world, pose remembered); the same name rejoins into the same character.
- `scripts/net_test.sh` runs the whole thing headless: join, identical layout hash, movement echoed, chat, a kill through intents, the loot picked up into a synced bag, a chest, the stairs to floor 2, a purchase and a sale at the merchant, a ping relayed back with the client's name, the stairs to floor 2 taken at once with one player online (`stairs:immediate` in the server trace), a cheer, the same elite roster on floor 2 as the server (both print `elites on floor 2: [...]`), leave and rejoin; then restarts the server on its save and rejoins once more. Both processes print a RESULT line. The multi-player vote itself is pinned by `tests/Net/ReadyCheckTests.cs` (one client is all the script runs).

## Collision layers

| Bit | Layer |
|---|---|
| 1 | World geometry (GridMap) |
| 2 | Party body |
| 4 | Enemies |
| 8 | Interactables (loot, stairs, NPCs) |
| 16 | Open safe door: an enemy-only barrier (`Door.SafeDoorBarrierLayer`; in every enemy's mask, 19 alive and 17 dead; adventurers, projectiles, NPCs and sight rays do not collide with it) |
