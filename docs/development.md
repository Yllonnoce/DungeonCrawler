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
  reach, so no pull ever traps the party, and every cell the party could walk to with all gates open is open to it in
  some state it can reach, so no key, chest or lever is shut off for good. Patrols reroute to the rooms they can still
  reach whenever the gates shift. `VaultPuzzles.Solve` gives the shortest lever order from any state. A pulled puzzle
  lever stays on the map, dimmer, since it can be put back.
- **Every floor can be finished** (`Dungeon/Generation/FloorSolvability.cs`, tests in `tests/Dungeon/FloorSolvabilityTests.cs`):
  `FloorSolvability.Problems(layout)` lists every way a finished floor cannot be played through, with hidden passages
  counted as walls: a treasure vault whose key or lever is out of reach (a key may lie behind another lock the party can
  open first), the vault puzzle failing its proof, a lever with no plaque, the jail, a prisoner out on the floor or every
  spare jail key out of reach, the altar or a breach lever out of reach, the gas exit's lever behind its own gate, the
  stairs out of reach once every lock is worked. The unit test runs every shape with every objective it can hold over
  200 floors each; `--test-vault-puzzle` runs the story's floors and 150 seeded runs (3,000 floors) as the game deals
  them, prints each puzzle with its answer, and pulls that answer on the real levers. Rules the sweep forced: a cellar
  pit is never dug under a vault's key or lever or a cell the objective owns; spare jail keys lie only where the party
  can walk with the jail and the reward vault shut; a crowded lever room puts its plaque on the nearest free cell, against
  the wall if it must; and Butterfingers does not touch the jail's locks (door and shackles pick at the uncursed chance),
  or a cursed party could find the stairs sealed for good.

## The dungeon's master

The campaign's antagonist runs the place. Objective floors open with their line instead of the narrator's, the sealed
stairs and the HUD prefix name them, the reinforcement portals are their doing, and they taunt the party as things
happen (`Adventure/Core/Objectives/Taunts.cs`: `TauntTrigger`, `TauntDirector`, `TauntBank`): a floor entered, a
prisoner caught or freed, gas thickening, a wrong lever, a wave, the ritualist down, a member down, a wipe, a trial
passed, the stairs taken, a party dawdling a minute on an objective floor. Never while a narration, boss or taunt line
is still being heard (its recording's real length and a breath, `NarrationRules.HeardSeconds` with `VoiceRenderer.LengthOf`;
without a recording the estimate `SpeakSeconds`, 1.5 s plus 0.085 a character, at most twenty, fitted to the shipped bank), within
twelve seconds of their own last line, or a minute of the same trigger, and a line said stays out for two floors (`BarkRepeat`, saved with the barks). The campaign carries the lines (`CampaignPlan.Taunts`, two or three per
trigger, `FloorPlan.Taunts` for a floor's own); a planner writes them in the antagonist's voice, the offline designer
and any campaign missing a trigger take the stock bank (`CampaignRepair`). They may curse: every taunt has a
`CleanLine`, and "The dungeon's master may curse at you" in Settings picks which is shown (and, in multiplayer, each
client picks for itself: `NarrationMsg` carries both). The voice provider refusing a coarse line renders its clean
twin under the same id, so nothing goes silent. Taunts have a voice budget of their own (`VoiceBudget.Taunts`, 30 on
top of the campaign's 80).

In the shipped campaign the antagonist is Morg Ashtusk, the orc Herald: every taunt and his floor 20 boss lines are one voice (Gemini
name Fenrir, rendered as Inworld Dominus), and on floor 20 the boss is shown as "Morg Ashtusk" (`BossRules.ShownName`). A floor's own
`Descend` taunt plays when the party leaves it (`AdventureDirector`: `TauntDirector.DescendBank`, objective floor or not, ahead of the
stock descend line), and the next floor's opening line waits until that parting line, or the campaign's stock one, has been heard (`NarrationRules.Arrival`). An objective
floor with FloorEnter taunts of its own opens with one of them (`TauntDirector.FloorEnterBank`); the campaign's general FloorEnter
lines speak only for a floor that has none. The urgent taunts (an opening, a parting line, a crushing, a wipe) speak at once; the
objective's last words (all freed, rite done, vault open, objective done) wait for a line being said rather than cut it off,
and a rite's opening taunt (RitualStart) waits up to 30 s rather than being dropped (`TauntDirector.HoldSeconds`, `Held`); the
other moment-bound taunts are dropped when someone is speaking. A boss that is a beast or a machine says nothing under its own name: Mother Nit's and the Bellows'
boss lines are empty (the engine skips an empty line) and the narrator's BossSeen and BossDead entries speak for them. The
defend-the-figure objectives word their notice neutrally ("the work at the shrine", `ObjectiveText`), since the figure may be Grisel or
Maud and not a ritualist.

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
- The villain in person: on the last boss floor the tier's boss wears a body of the antagonist's own when the campaign's
  antagonist is an orc (`big_baddy.glb`, below) or a verger (`verger.glb`, scaled to 2.25 m) (`NpcBodies.Villain`); other
  campaigns keep the creature. The shipped Verger is an ordinary cast member now (`cast_quill`, floor 19), not the villain.
- The orc's true form: Morg Ashtusk is `viewer_orc` in `orc1.glb` on floor 1, where the party kills him; on floor 20 the last boss wears `big_baddy.glb` (owner's decision 2026-11-12; `NpcBodies.Villain` returns `big_baddy` for the orc, scaled 2x in `DungeonBuilder.SpawnEnemy`), and the name plate, boss bar and log show "Morg Ashtusk" instead of the stock creature's name (`BossRules.ShownName`; only the name changes, the stats stay).
- All sixteen bodies from the briefs are in (party, fighters, archetypes, the Verger, the orc's true form).
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
OpenGameArt (but for one owner-supplied track), fetched by `scripts/fetch_music.py` into `assets/audio/music/` (about 49 MB): a tavern tune for the title screen,
three exploration pieces per tier (Crypt, Caves, Forge, Abyss) shuffled with a few seconds of quiet between them (plus one owner-supplied exploration track, `explore_dungeon_walking.mp3`, that plays in every tier), three battle
tracks, a boss track, and victory and defeat stings. `Audio/MusicDirector.cs` crossfades between them: battle music starts the
moment an enemy is fighting the party within 26 m and holds for six seconds after the last one stops, the boss track runs from
the guardian noticing you until it falls, and everything ducks under spoken lines. Floor 12's rides play a Chase mood
(`Dungeon/Rails/Core/RideMusic.cs`): while the party cart rolls with the local hero seated in it or within 30 m of it, or a chaser
rolls with them in it, and for 4 s after. Rides 1-4 loop `chase_determined_pursuit.ogg` (CC0, downloaded by hand, not by the
script); the last ride (5) plays `forge_heavy_dungeon.ogg` started at `RideMusic.PhaseSeek` of the floor clock so its downbeat meets the
hammers' fall, locked again each time it ends. The "Music and ambience" slider covers it all.
Enemy grunts are CC0 creature SFX from rubberduck's packs, fetched by `scripts/fetch_grunts.py` into `assets/audio/grunts/<set>/`;
`VoiceSets` picks the set by enemy size and plays a hurt or death clip (pitch varied), and falls back to the old `enemy_hurt` and
`enemy_die` cues when a set has no clips. They are listed in `assets/LICENSES.md`.
Floor 12's mine cart chase is dressed from loafbrr's CC0 "Mine Object collection" (`assets/models/loafbrr_mine/MinesPack.gltf`,
its textures extracted beside it by the import): `Dungeon/Props/MinePack.cs` loads it once and hands out collision-free copies of
named pieces (the slag carts' and runaway wagons' tubs, every cart's axles, the switch levers, the ore buckets and their chain,
the buffer's blockade, the heap's rocks, the timber frames and the pocket lanterns), fitted over the boxes they replace; the boxes
stand in when a piece is missing, and a dedicated server loads nothing. The pack is stylised, so its timber and rocks are drawn in
the game's own materials. The track runs on Poly Haven's `rusty_metal_02` rails and `old_planks_02` sleepers over a
`gravel_floor_02` ribbon, and the slag, coals and cinders are ambientCG's `Lava004` with its emission map. `RideAssets` lists every file.
The slag carts' and runaway wagons' tubs (`Cart_2`, `Cart_1`) are the pack's bright cartoon red; `MinePack` tones them to a dark
rusted oxblood (`MinePack.EnemyTubTint`, one albedo multiply on a copy of the shared material, so the axles keep the original).
The ride's sound is 23 clips in `assets/audio/rails/`, cut by hand from Freesound CC0 previews; see LICENSES.md for each source,
author and cut. `Dungeon/Rails/Core/RideSound.cs` names them as cues (`RideSound.Clips`, with a few Kenney Impact reuses;
`RideSound.Loops` are the looping ones), and `AudioManager` loads them beside its own cues. The chase track,
`chase_determined_pursuit.ogg`, is CC0 from OpenGameArt, downloaded by hand (not by `scripts/fetch_music.py`), and listed in
LICENSES.md with the music.
Nothing a fight or a ride needs is read from disk on the frame it appears (each read was 3 to 11 ms through the NTFS mount):
`Enemies/EnemyModels.cs` keeps every creature model loaded for the session once a creature of its kind has spawned (Godot used to
let the scene go with the last creature wearing it), with each model's Idle-stance box measured once, and a railed floor's
`RailEncounterController` asks for its riders', standers' and golem's models on a loader thread as the floor is built
(`Rails/Core/RailWarmup.cs` lists them) and builds one slag cart off the tree so the first one spawns without its JIT. `MusicDirector`
reads the floor's tracks on a loader thread when it is built (the tier's exploration pool, battle and boss, and on a railed floor
`RideMusic.Tracks`) and lets go of every other track it holds but the one playing (`Audio/Core/FloorMusic.cs`), so only the
current floor's music stays loaded. `VoiceRenderer` keeps the last 16 recordings (`Adventure/Core/Voice/RecentCache.cs`), so a line
whose length was read to time what follows it is not read again to play it, and a ride's encounter lines are read on a loader thread
when the ride arms (`VoiceRenderer.Prefetch`; on a co-op client when its cart's state names the ride armed; a dedicated server
prefetches them too, since its director reads every narrated line's length to time what follows it). A loader-thread read that failed is still collected (`LoadThreadedGet`), so Godot forgets its task. The model cache, like
`MinePack`'s and the other static caches, is let go at Main's exit.
The HUD portraits (`UI/Hud/PortraitPanel.cs`) refresh every frame but build their strings only when what they show has changed
(`Characters/PortraitText.cs`), which took ~1 MB/s of garbage, and a collection every few seconds, off a quiet ride.
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
godot --headless --path . -- --join=ws://127.0.0.1:7777 --name=Probe --class=class.mage --press=inventory-probe --after=12 --quit-after=16   # open Tab on a client and print "[inv] ..." (or the exception)
godot --headless --path . -- --join=ws://127.0.0.1:7777 --name=Probe --class=class.warrior --probe-pickups --quit-after=240   # open every chest, then walk to each item, press E on it and print "[probe] ..." (pair with a server run with --clear-enemies for the kills' loot)
godot --path . -- --seed=1 --floor=1 --talk --showcase=Death --screenshot=/tmp/move.png --after=3   # floor 1's fighter: open his dialogue and play one of his model's clips from the moves grid
godot --headless --path . -- --test-ai-flow         # stand-in planner: New Game card, prefetch, wait for a plan, timeout fallback, memory summary
godot --headless --path . -- --builtin              # play the shipped campaign (data/adventures/builtin), no key needed
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-kill-barks=assets/voice/builtin   # render every party member's and fighter's kill line into the shipped bank (RUNWARE_API_KEY in the environment), then run the import
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-cast-voice=assets/voice/builtin   # render every line of the recurring cast, the merchant's Merchant section and every boss line (outside the 80-line budget) into the shipped bank; the plans with their ids land in assets/voice/builtin/cast-plans, to merge into data/adventures/builtin by hand, then --import
godot --headless --path . -- --test-hazard=press    # floor 1, lethality 3: an enemy and the lead in the run, crushed and mangled, no auto-revive, a companion pulls the lead free, the villain gloats, a barrel jams the second run, saved, loaded, phase and kills survive
godot --headless --path . -- --test-hazard=spikes   # a pressure bed clicks, raises, impales the lead and an enemy, is rescued; a periodic bed keeps the floor tick's phase; a jumping body is missed
godot --headless --path . -- --test-tick-rate --physics-hz=90   # floor 1: the drop hammer's beat and the periodic spike cycle on the floor clock (DungeonBuilder.FloorTick) take 4.5 s of wall time; prints [tick-rate] RESULT ok hammer=<s> spikes=<s> (run it bare for 60 Hz; --physics-hz=N runs the physics at a headset's rate, debug only)
godot --headless --path . -- --test-cart-watchdog   # floor 12, no enemies: a caught cart braked 15 s is not freed; let go it grinds free to stop 1 10 s later; caught short of ride 5's pit switch (the kiln's, from rc.Restore(LastRideStop)) it is set at the switch's window, the lever is thrown and it comes home round the pit to stop 5; prints [watchdog] RESULT
godot --headless --path . -- --test-relief[=beside|rails|refuse|ahead]   # floor 12 solo, lethality 1: the relief cart for a player who jumped from the cart (beside the line, on the rails, refused twice with a save and a load), and none while ahead of the train or behind the cart that met them; prints [relief] RESULT
godot --headless --path . -- --test-crevasse[=trestle|survivor]   # floor 12 solo, lethality 3: walk the deck and a plank with companions (nobody falls), a companion, an enemy, a crew member and Grisel over a chasm (fallen, swallowed, lost, caught at the lip), the fallen back at stop 1, then the train falls at the jump (=trestle: with the trestle) and the wipe rises beside a fresh cart at the stop it left (=survivor: the human on foot, nobody wipes, the fallen come back on the platform, a save and load, the floor change to 13); see Rails, "Crevasses"; prints [crevasse] RESULT
godot --headless --path . -- --test-barricade       # floor 12 solo, lethality 3: the lanterns in sight from the seat; A a spell cuts ride 1's barricade on foot; B on ride 1 the call 30-35 m out, the human brakes and the seated lead's blade cuts it in three hits (no smash, no dent); E r2_slag1 smashes the slag-line barricade while the party passes on its own lane; C the cart smashes ride 2's (plating -25, 15% of max HP to each seated hero, never below 1 HP, Grisel on the bench unhurt, V x0.8); D a restore stands both of ride 2's again at its next arming; prints [barricade] RESULT
godot --headless --path . -- --test-switch-back[=wrong|stall|lone]   # floor 12 solo, lethality 3: the broken bridge as built, traffic dropping through it quietly and starting past it; ride 3 across: the live switch back called early, untouched by companions, its bridge lamp in sight, thrown from the seat, home to stop 3 (=wrong: nobody throws it, both calls, the train falls at the gap, the wipe rises at stop 2; =stall: the watchdog sets a stuck train at the window's start, the last call's mark and the back's points, then grinds it into the gap; =lone: the only human downed in the seat, the cart takes the safe line home); prints [switchback] RESULT ok (thrown|wrong|stall|lone)
godot --headless --path . -- --test-pit[=thrown|wrong|lone|stall|late]   # floor 12 solo, lethality 3: ride 5's lava pit and its deadly switch (thrown from the seat and round the pit; left straight and into the lava; R-lone's safe line; the watchdog's four steps; the server's late check); see Rails, "The five rides"; prints [pit] RESULT
godot --headless --path . -- --test-island[=cavein|bridge|wreck|parked|lift|takeover|onfoot|stranded|grisel|chest]   # floor 12 solo, lethality 3, gathering on: the exit island (the cave-ins and the lava, the exit bridge's hold, a wreck inside, the parked train, the lift and its takeover, home and lost, Grisel brought in, the reward chest); see Rails, "The five rides" and "Stops, cages and the crew"; prints [island] RESULT
RIDE=1 scripts/net_test.sh                           # floor 12 in co-op: a server, a client and a joiner ride the whole line (see Rails, "Riding together"); run it alone, about 5 minutes
godot --headless --path . -- --test-jailer --builtin --floor=2 --no-ai --no-ambush --no-portals --no-gathering --lethality=1  # a creature set beside a prisoner must unlock the jail, deliver, lock and post
godot --headless --path . -- --builtin --floor=N --dump-enemies=40,70 --quit-after=72  # prints every creature's cell, state, facing and post and every NPC's place at those seconds (anomaly hunts)
godot --headless --path . -- --test-vault-puzzle --no-ai  # every floor of the story and of 150 seeded runs through FloorSolvability, each puzzle floor printed with its answer and solved on the real levers; prints [vault] RESULT
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
XDG_DATA_HOME=$(mktemp -d) godot --headless --path . -- --builtin --no-ai --floor=12 --print-map --track-depth=3 --track-depth-float   # (the XDG prefix keeps the run's autosave out of your own saves/auto.json) --track-depth[=CM] (implies --print-map; 3 cm by default) probes every rail track against the drawn floor every 0.5 m (rail tops, sleepers, walls) and lists the line's ramps, set pieces and cavern S, then the verdict ([track-depth] total buried=N inside=N nofloorOff=N budget=N floating=N ... buckledLaneCells=N ...; the pass is buried=0 inside=0 nofloorOff=0 budget=0 floating=0, floating counting samples more than 9 cm over the floor; ride 4's collapsing bridge and the slag line's drop add no RailTrack: their rails are the lanes' own (RailTrack.LaySpan), probed as PartyTrack and EnemyTrack, and each track's nofloorDrop counts its samples over a drop with no floor drawn: the verdict does not judge it, but floor 12's gate pins it at PartyTrack 201 and EnemyTrack 161 (plan 15 Task 3: 40 samples per 4 m cell of drop, so the party track's ride 5 pit (40) and the exit bridge's four cells (160), the enemy track's ride 3 broken-bridge gap (40) and the enemy bridge's three cells (120), plus one sample on each track at the bridge's first join, whose sleeper end lies over the drop cell; 0 and 40 before plan 15), and any other count on either track means a cavern deck built without its footing or a drop cut where none was planned (`TrackDepthRules.Floor12NoFloorDrop`; `--test-track-view` holds the probe to it at the build); the piece and encounter lines give each set piece's and encounter's party S (r4_bridge 903.1, r4_leap 913.0, r4_slag 848.0) and the cavern line its span (cavern party S=903.1..991.1); buckledLaneCells counts the lane cells the GridMap draws with a Buckled* slab); --track-depth-float also lists each sample standing more than CM over the floor
godot --headless --path . -- --test-track-view    # floor 12 solo, lethality 3: first the --track-depth probe at the build, its nofloorDrop pinned (PartyTrack 201, EnemyTrack 161; "[trackview] nofloorDrop at the build: …"); then ride 3 from stop 2 on the party lane (r3_switch_out left straight, r3_heap cleared by the test) over the line's first party ramp and a spared lane cell approached on its own level ((21,23) on floor 12); layer-1 rays from the seated eye to each rail top at the sag, the crest and the crest ± 1 m and over the spared cell, all clear, and three control rays: one under the drawn floor at the crest that must be blocked near its end, and one 3 cm under the bed at each rail over the spared cell that the cell's slab must stop within 1 cm of the bed's height (a fallback cell, when none is approached on its own level, is sighted without them and the RESULT says so); windowed with --screenshot=PATH it saves PATH_crest/_sag/_heave.png (HUD hidden); prints [trackview] RESULT
godot --headless --path . -- --test-structures                    # picks a seed whose floors 1-4 hold a gate, a camp, a pond and a stream; checks braziers, hut walls, bandits at the fire, water beds, 45° corners, wading
godot --path . -- --seed=2 --floor=2 --face-node=Camp_:3 --screenshot=out.png   # stand N cells back from a node (Gate_, Camp_, Water_, Barrel0, Enemy...) and look at it; --face-cell=X,Y:N for a grid cell; --face-node=chamfer:N for the first cut corner, --face-node=buckled for a heaved tile, --face-node=bridge for a stream's bridge, --face-node=vault / groin for the ceiling (add --vault=pointed etc. to force a profile)
godot --headless --path . -- --test-keys                          # a key rebind survives a settings reload without pressing Back; the VR comfort level round-trips through Save/Load
godot --path . -- --test-horde --screenshot=out.png                 # floor 3 nest room: stream, kill, spent; the screenshot is taken mid-stream
godot --path . -- --drop=item.iron_key,item.health_potion,item.longsword --screenshot=out.png   # lay sample items in front of the party (every 2nd magic, every 3rd rare)
godot --path . -- --builtin --equip=item.hand_axe --screenshot=out.png --after=3   # put a weapon in the lead's hands half a second in, to check the first-person view (any weapon item id: hand_axe, greatsword, mace, spear, ...; or a legendary's id, leg.sluicebreaker, to see its glow)
godot --path . -- --equip=leg.sluicebreaker --auto-attack --screenshot=out.png --after=3.1   # catch a live swing: the streak, the glow (vary --after to land on a different moment)
godot --path . -- --pose-sheet=/tmp/poses   # every weapon held still at each pose of its light, backhand and heavy swings, one PNG per pose (NN_item_move_K.png); --pose-items=item.wand,item.glaive narrows it, --pose-skills=ability.cleave,ability.meteor adds skill moves
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
godot --headless --path . -- --test-narration   # the shipped campaign: floor 1's tier line then intro, a room line once (through a save and load), floor 1's parting taunt then floor 2's own opening, floor 20's victory recap after the closing narration; prints [narration] RESULT
godot --headless --path . -- --test-writing   # banter in order and stopped by a fight, a TrapSprung bark from the lazy top-up, narration beats a bark on the subtitle, the cheer and its answers, the press epitaph, floor 3's Crushed taunt, Crane's aside
godot --headless --path . -- --test-hit-fx   # blood, bursts, stuck arrows, grunts (seed 7); prints [hitfx] RESULT
godot --headless --path . -- --test-combat-feel   # perfect block, block, parry and riposte, dodge (never through gas), stagger, damage numbers and bars, Shatter/Ignite/Bleed/Vulnerable, the boss's decal and the companions' dodge, boss bar, vignette, banner, toast (seed 7, lethality 1); prints [combat-feel] RESULT
godot --headless --path . -- --test-blade-contact   # the blade that hits what it touches: a blow lands when the blade arrives, nothing behind or beyond reach, a forehand cuts ahead then left and never right, a failed roll glances, the old cast with the blade off (seed 7, lethality 1); prints [blade] RESULT
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

### Running flows side by side

`scripts/flows.sh` runs several headless flows at once, each in its own `user://` (bash 5.1 or later):

```bash
scripts/flows.sh -j 6 "--test-crew --quit-after=570" "--test-ride" "--test-split=after" "--test-runaway=tail"
```

Each argument is one flow's game flags (what follows `godot --headless --path . --`), split on whitespace only, so a flag value
cannot hold a space. List the longest flow (e.g. --test-crew) first: flows start in the order given, and the batch can end no
sooner than the longest one. It runs `dotnet build DungeonCrawler.csproj` once (and stops if that fails), then up to `-j N`
flows at a time (default: min(6, the number of flows, half the cores)), each under `timeout`: its `--quit-after=S` plus
max(120, S/2) seconds, else `FLOWS_TIMEOUT` (default 1200). Put `--quit-after=` after the test flag; a `--test-*` flag sets its
own budget and overrides an earlier one, and the runner reads it the same way. `GODOT=path` picks another binary.

It ends with one line per flow: PASS/FAIL, the flags, the flow's own `[tag] RESULT` line, its exit status, its ERROR-line count
(marked `!` when not 0), its wall time and its log. A flow passes only if it exited 0 and printed its own `RESULT ok` with no
failing RESULT line: a timeout, or a crash after the RESULT line, is a FAIL. Every flow on the built-in campaign also prints
`[builtin] RESULT ok` 2.5 s in; that line never counts for a `--test-*` spec, and for a spec with no `--test-` flag (e.g.
`--builtin --floor=N ...`) it counts only with exit 0. Exit status: 0 all PASS, 1 any FAIL, 2 usage, refusal, temp dir or
build failure, 130 interrupted. Logs stay in `FLOWS_LOG_DIR` (default a fresh `/tmp/flows-<date>-<time>-XXXX`).

Isolation: two flows sharing `user://` clash (one run hit an ERROR on a `profile.json.bak` written by another). Godot on Linux
puts `user://` under `$XDG_DATA_HOME/godot/app_userdata/DungeonCrawler`, so each flow gets `XDG_DATA_HOME=$(mktemp -d)`. All
of them are made, absolute, before any flow starts, and the run aborts if one cannot be (an empty or relative `XDG_DATA_HOME`
makes Godot fall back to the real `~/.local/share`). They are removed once the flows have stopped (`KEEP_TMP=1` keeps them).
A flow starts from an empty `user://`: default settings (lethality 3 unless the flow forces another), no keys, an empty
profile, the shipped campaign from `res://`; the headless flows need nothing more and never read your real `user://`. The
API-key variables are removed from each flow's environment.

The RIDE pass (`RIDE=1 scripts/net_test.sh`) is never run through `flows.sh` or beside it: its set-piece clocks and party pairs
fail on a loaded machine, so it needs the machine alone. `flows.sh` refuses network flows (`--test-net-*`, `--server`,
`--join=`: they share a port) and empty specs.

### Frame rate

**The perf line.** Every role (solo, client, dedicated server) logs one line per 10 s of frames whenever a game is running
(`PerfMonitor`, a node Main adds the first time a floor is built; `PerfRules.LineEvery`):

```
[perf] floor=12 cart=yes fps avg=359 min=328 worst_ms=93.2 process_ms=18.28 physics_ms=7.97 draws=303 enemies=19 aware=2 asleep=11
```

- Frame times are wall time between consecutive frames (`FrameClock`, `Time.GetTicksUsec`), not the engine's process delta.
  Godot cuts that delta to at most `max_physics_steps_per_frame` physics steps (about 133 ms at 60 Hz), so a long hitch would
  read short.
- `avg`: frames over the window's time; `min`: the fewest frames in any complete second of it (a frame counts in the second it
  ends in); `worst_ms`: the longest frame.
- `process_ms` and `physics_ms`: Godot's `TIME_PROCESS` and `TIME_PHYSICS_PROCESS` monitors, averaged over the window's frames.
  The engine refreshes both once a second with the **longest** step of that second, so they read as the typical worst step per
  second, not a per-frame mean (at 300 fps a `process_ms` of 11 means one frame each second took about 11 ms).
- `draws`: the mean `RENDER_TOTAL_DRAW_CALLS_IN_FRAME` (0 headless).
- `enemies`: alive in the `enemies` group; `aware`: those of them not unaware (a client reads the server's flag); `asleep`:
  those on the tick diet's quarter rate this tick (`Enemy.Asleep`, below; always 0 on a client, which runs no enemy AI).
- `cart`: the local hero is seated (a dedicated server: any hero is).
- A headless server's fps is its own loop's rate.
- After a floor is built, 2 s of frames (`PerfRules.BuildGrace`) are left out: the load and its settling. They print no
  slow-frame line, and the window starts again after them. A frame the tree was paused through is skipped.

**The slow-frame line.** A frame over 50 ms (`PerfRules.SlowFrameMs`) prints
`[perf] slow frame 93 ms (floor 12, cart yes; 1 slow since the last line)`, at most once a second (`SlowLineGap`). The count
includes this frame and every slow frame since the last slow-frame line (those in between were not printed).

**Settings (Display, after "Haze in the air"):**
- "Show FPS" (`display/show_fps`, off by default): the frame rate top right under the minimap, refreshed four times a second.
- "Render scale" (`display/render_scale`): 100 % / 85 % (FSR) / 70 % (FSR) / 50 % (FSR). The root viewport's `Scaling3DScale`,
  with `Scaling3DMode` FSR below 100 % and bilinear at 100 %. A saved value is snapped to the nearest step
  (`GraphicsRules.SnapScale`; the higher on a tie, 100 % for a missing or non-numeric value). It does nothing in a headset,
  which drives its own scale (`GraphicsRules.ViewportScale`). Settings load and apply before the headset starts, so
  `Vr.Start` puts the viewport back to full scale and bilinear once the headset is the display.
- "Shadows" (`display/shadows`, on by default): the local hero's torch casts shadows. It is the only shadowed light in the game.
- The Haze slider at 0 already turns the volumetric fog off.

**The perf flow.** `--test-perf[=crowd|ride]` runs windowed only (headless it fails at once). It is never run in `flows.sh`.
Each run measures one phase on the built-in campaign's floor 12. Every phase uses the same settings, kept in memory and never
saved: VSync off, windowed 1280x720, Haze 0.5, render scale 100 %, shadows on, Show FPS off. It also sets lethality 1, tops
every hero up to full HP each second, and turns off gathering and portals. Nothing is culled. The mouse is never captured: an
unfocused window cannot grab it, and X11 logs `ERROR: NO GRAB` when it tries. A run passes with exit 0, its `[perf] RESULT ok`
line and no `ERROR` line.

- `crowd` (the default; ambushers off as well). The cart is restored at stop 4 (`rc.Restore(4)`) and the party stands beside it
  on the platform. 12 creatures (5 `enemy.horde_cinder`, 4 `enemy.ember_hound`, 3 `enemy.forge_priest`) are spawned 10-20 m
  around it and sent at the party. The floor's own enemies stay alive and the companions are on Hold. Every enemy is held at
  1 HP or more (`Enemy.DebugHoldHp`) and put back to full HP every frame (`Enemy.DebugFillHp`, since plan 16 Task 2: a
  forge priest under 30 % HP flees, which changed the load mid-measure), so the load stays the same through the measure. The flow prints the alive and aware count
  at its start and end, and fails if the alive count changed. It discards 5 s and measures 20 s of wall time. This phase is the enemy tick diet's before-and-after row. It also stands in for the stop siege's "before",
  since the siege does not exist yet when the baseline is taken.
- `ride` (ambushers on, so the alcove shooters appear in it once they exist). The cart is restored at stop 0, the party sits
  down, departs and rides ride 1 with every set piece and enemy alive. It measures from 5 s after the departure to the arrival
  at stop 1.

Each phase prints `[perf] {phase}: {frames} frames over {s} s: median … fps, 1% low …, worst second …, worst frame … ms; process
… ms, physics … ms, draws …, alloc_kb …; enemies n (k aware) asleep=m`, then `[perf] RESULT ok (…)`. The 1% low is the fps of the
slowest 1 % of frames (their mean frame time). `alloc_kb` is the managed memory allocated over the measured window
(`GC.GetTotalAllocatedBytes`). Run it with the machine otherwise idle:

```bash
dotnet build DungeonCrawler.csproj
for P in crowd ride; do XDG_DATA_HOME=$(mktemp -d) timeout 300 godot --path . --print-fps --resolution 1280x720 -- --test-perf=$P 2>&1 | tee /tmp/perf-$P.txt | grep -E "^\[perf\]|^ERROR"; done
```

**Baseline (plan 16 Task 1, 2026-11-24, after its fix round: wall-time frames, the crowd held).** Dev laptop: RTX 5070
Laptop GPU, 240 Hz panel, platform profile `performance`, machine otherwise idle. Two runs of each phase, before the tick diet
and before the siege. Each run had exit 0, `RESULT ok` and no `ERROR` line.

| Phase | Frames / s | Median fps | 1% low | Worst second | Worst frame | Process ms | Physics ms | Draws | alloc_kb | Enemies (aware) |
|---|---|---|---|---|---|---|---|---|---|---|
| crowd (run 1) | 4256 / 20.0 | 312 | 52 | 190 | 50.9 ms | 12.66 | 16.48 | 866 | 109827 | 29 (14) |
| crowd (run 2) | 4307 / 20.0 | 315 | 58 | 188 | 40.5 ms | 10.83 | 15.26 | 866 | 110890 | 29 (14) |
| ride (run 1) | 14378 / 48.1 | 347 | 85 | 217 | 94.3 ms | 13.47 | 6.34 | 954 | 186375 | 18 (1) |
| ride (run 2) | 14203 / 48.1 | 342 | 83 | 218 | 103.7 ms | 14.67 | 6.78 | 965 | 185488 | 18 (1) |

- Crowd: 29 enemies alive (the floor's 17 and the crowd's 12) and 14 aware, the same at the start and end of both runs. The
  mean fps (about 215) is far under the median: the slow frames carry the cost. The process allocates about 5.5 MB a second.
- Ride: both runs had one hitch of about 95-105 ms during ride 1, and a 53 ms frame later on.
- An earlier take, with the engine's capped delta and a crowd the party thinned to 23, is superseded. Its worst frames were
  floors rather than measurements.

**The tick diet (plan 16 Task 2).** What every enemy paid every physics tick at any distance, cut without moving any
behaviour a flow pins (all 22 floor-12 and general flows pass):
- Separation walks `Enemy.Live` (every enemy in the tree, kept by `_EnterTree`/`_ExitTree`), skipping those under another
  parent, instead of `GetParent().GetChildren()` (every child of the floor root, per enemy per tick). Same pushes, same order.
- No per-tick allocations on the enemy path: the effect ticks fill a buffer each enemy keeps
  (`StatusEffectController.Update(dt, into)`; the party's tick uses it too), the threat decay walks a kept key buffer, the
  unstick probe's shape and query are made once per enemy, the sight ray's query is made once and re-aimed,
  `StatusEffectController.Multiplier` is a loop rather than an iterator, and `PartyController.PartyAlive`, `HasBodies` and
  `NearestAlive` are loops rather than LINQ (every creature asks them every tick).
- **The sleep (`TickDietRules`, Ruling 20).** An enemy sleeps while it is unaware (not on a patrol or a walk home) or holding
  its post, moves under 0.1 m/s, is not carried or leaping, was last hurt over 1 s ago, and stands farther from every hero
  in `Party.Adventurers` (downed included) than the larger of 40 m and its own sight, `DetectRange × Aggression × 1.3`. A
  sleeper runs its AI state only on the ticks where `(NetId & 3) == (physics frame & 3)`, with the seconds it banked since
  (its wind-up and telegraph timers too). Water, unstick, animation, effects, tint and dying run every tick. A sleeper
  cannot see anyone, so no detection is late; a far guard picks up a group order up to three ticks (50 ms) late. The
  horde kinds and the twelve kinds with `DetectRange` 40 see to about 73 m, so they sleep only beyond it.

**After the diet (plan 16 Task 2, 2026-11-24, same machine and settings).** The crowd's "before" was taken again on the
code just before the diet, with the crowd now put back to full HP every frame (above), so before and after compare like
with like; T1's crowd rows held HP at 1 only. Every run had exit 0, `RESULT ok` and no `ERROR` line.

| Phase | Frames / s | Median fps | 1% low | Worst second | Worst frame | Process ms | Physics ms | Draws | alloc_kb | Enemies (aware) | Asleep |
|---|---|---|---|---|---|---|---|---|---|---|---|
| crowd before (run 1) | 4700 / 20.0 | 343 | 64 | 209 | 40.6 ms | 13.67 | 13.00 | 869 | 134710 | 29 (13) | - |
| crowd before (run 2) | 4230 / 20.0 | 309 | 55 | 187 | 43.0 ms | 11.21 | 15.25 | 867 | 110490 | 29 (14) | - |
| crowd before (run 3) | 4250 / 20.0 | 307 | 60 | 200 | 30.5 ms | 13.72 | 11.00 | 867 | 110675 | 29 (14) | - |
| crowd after (run 1) | 5876 / 20.0 | 348 | 77 | 269 | 67.3 ms | 11.69 | 12.38 | 867 | 52352 | 29 (14) | 13 |
| crowd after (run 2) | 6500 / 20.0 | 379 | 96 | 304 | 28.3 ms | 10.78 | 7.20 | 869 | 56955 | 29 (13) | 13 |
| ride after (run 1) | 16886 / 48.2 | 362 | 103 | 246 | 130.0 ms | 14.31 | 4.21 | 949 | 94165 | 18 (1) | 14 |
| ride after (run 2) | 16893 / 48.1 | 362 | 108 | 245 | 85.0 ms | 12.06 | 4.37 | 956 | 94245 | 18 (1) | 14 |

The change, after against before (the ride's before is T1's two rows above):
- **crowd**: median 307-343 to 348-379 fps (about +15 %), 1% low 55-64 to 77-96 (about +45 %), worst second 187-209 to
  269-304 (about +45 %), mean fps about 215 to about 310, `alloc_kb` 110-135 MB to 52-57 MB over the 20 s (about -55 %)
  although it ran about 40 % more frames. 12 of the 29 are asleep at the start of the measure, 13 at the end: the floor's
  own enemies far from stop 4.
- **ride**: median 342-347 to 362 fps (about +5 %), 1% low 83-85 to 103-108 (about +25 %), worst second 217-218 to 245-246
  (about +13 %), physics 6.3-6.8 to 4.2-4.4 ms, `alloc_kb` 185-186 MB to 94 MB (about -50 %); 14 of the 18 asleep. The one
  85-130 ms hitch in ride 1 is still there: it is not the enemies' tick.
- Before run 1 is the outlier of the "before" rows (one aware creature fewer by its end, more frames, more allocation);
  runs 2 and 3 agree with T1's crowd rows (312/315, about 110 MB).
- With the HP topped up every frame, the second 10 s line of two of the three "before" runs (and of after run 1) was still
  about 5 % slower than the first, so that drift was not only the priests' flight.

### Known gaps

- **Godot's exit count.** Godot's "resources still in use at exit" (and its leaked ObjectDB instances) were audio streams and their
  playbacks: whatever was playing when the engine shut down was still in the audio server, so the count moved from run to run. Main's
  exit now stops every player and gives the audio thread 100 ms to free them (`AudioManager.StopAllAtExit`; a dedicated server
  returns at once, as it plays nothing), and the static resource caches (`HitFx`, `Tremor`, `Fire`, `WaterSurface`, `WaterAudio`,
  `Ripple`, `EnemyBars`, `Synth`, `MinePack`) are released there; nine measured runs (`--quit-after=3`, `--test-ride`,
  `--test-crew=condemned`, three each) print none. A sound started after that point (nothing does today), or an audio thread that
  needs more than 100 ms, would bring one back. The wait is a blind sleep, not a poll. The `static readonly` materials in
  `EliteVisuals` and `SlowPatch` are not released (they are made at type load and never showed in the count).

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
| E (hold 1 s, VR grip) | Seated at a stop whose cage is locked: leave the prisoners behind (a tap still gets out) |
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
plays a move when you attack, lies across the body while guarding, and swaps when you equip a different weapon.
The moves are engine-free data in `Items/Core/WeaponMoves.cs`, tested in `tests/Items/WeaponMovesTests.cs`:

- **Styles** (`Items/Core/WeaponStyle.cs`) are finer than weapon kinds: a two-handed sword is a Greatsword, a
  two-handed mace a War Hammer, and the item tags `sabre`, `glaive`, `wand` and `crossbow` pick those styles. The style
  picks the model in the hand (`Items/Core/HeldWeapon.cs`) and the move set. Light (dagger, sabre, wand), medium and
  heavy (greatsword, battle axe, war hammer, glaive) weights set how long a swing winds up and whether it jolts the view.
- **Moves.** Every style has two light swings that alternate while you keep swinging (a forehand and a backhand, a
  jab and a flick, ...), a heavy swing of its own, and for bows a draw-and-loose (a crossbow kicks instead). Melee
  skills have signature moves whatever the weapon (cleave sweeps the whole view, backstab rises from low and twists,
  venom strike stabs twice, shield bash tucks the weapon and jolts); bow skills draw long, loose three or dip the head;
  spells thrust (bolts), call down (meteor) or slam (novas); shouts pump the weapon twice and blessings lift it. A move
  that would outlast the time until the next swing (`AbilityRules.SwingSeconds`) is played faster.
- **Keys** say where the blade points, not how the model turns: `Up` raises it (+50 cocks it back past upright, −50
  lays it level ahead), `Left` swings it (−100 out to the right, +60 across to the left), `Twist` turns it about its
  length, and X/Y/Z move the hand. The view samples the move each frame and turns the hand about the grip so the blade
  points there, whatever model it holds; Quaternius models are moved so their grip sits on the hand. Every melee blow
  lands in view above the party bar (blade level or higher, hand raised); a test holds that.
- **Flourishes.** A heavy weapon's impact shakes the camera. Skills leave a streak in their magic's colour; legendary
  weapons glow and streak in the colour of what they inflict (gold if nothing); a perfect blow streaks gold.

New weapons with their own styles: Sabre (rogues, warriors, rangers), Battle Axe and War Hammer (warriors), Glaive
(warriors, rangers), Wand (mages: spell power in one hand), Crossbow (rangers: slow, heavy bolts). Named ones on them:
Sluicebreaker (war hammer), The Reedcutter (glaive, closes the Polearm gap in the named drops) and Lullaby (wand).

Companions (`Party/Adventurer.cs`, decisions in `Characters/Companions/CompanionBrain.cs`): they keep a formation
behind the lead (melee classes close, ranged and casters further back), path through corridors and across levels,
teleport to the lead if lost for five seconds, drink a shared potion below 30% health, pick up loot they pass (coin piles too, even with a full bag) and
open chests they reach, and fight by stance. Defensive (default) engages enemies that are already fighting the party
or whatever the lead attacks; Aggressive engages anything seen within 12 m; Hold stays put and only fights what
reaches it. Ranged characters keep about 7 m away and back off when something closes in. They cast party buffs when
missing, novas when two or more enemies are close, and their strongest ready ability otherwise. Enemies pick their own
target by recent damage and proximity. A character at zero HP is down until no enemy has fought the party for three
seconds, then gets up at 1 HP; if the lead goes down, control passes to the next living character.

## Playing in a headset (PC VR, first walkable build)

`godot --xr-mode on --path . -- --vr` (the "Play in VR" launch entry) plays in a headset connected to the PC: a Meta Quest over Link or Air Link with Meta Quest Link set as the active OpenXR runtime, or any other OpenXR runtime. Both switches are needed: `--xr-mode on` makes Godot bring OpenXR up (the project leaves it off, so a flat game never looks for a headset) and `--vr` makes the game use it (`src/Core/Vr.cs`). With no headset linked the game prints a `[vr]` line saying so and plays flat. Nothing in `project.godot` changed; the controllers use Godot's default OpenXR action map.

- **The rig** (`src/Party/VrRig.cs`). The body this machine plays gets an `XROrigin3D` with the head and two controllers (`Adventurer.SetLocalView`; it moves with you when you possess another hero). The rig stands in the world on its own (`TopLevel`). Each physics tick the body first steps under the head, walls permitting (`BeforeMove`: walking about your room walks the body), and turns to face where the head faces, so the stick's "forward" is where you look; after the body has moved, the rig is brought back so the head is over the body (`AfterMove`: the stick, falls, teleports, and a wall you lean into pushes the view back). The flat `Camera` node is carried on the head every frame, so the torch, the interact ray, `MeleeCast`, the blade and the weapon in view all work unchanged; mouse look is ignored. Physics ticks are matched to the headset's refresh rate.
- **Controls.** Left stick walks, clicked in to sprint. Right stick left or right turns 30 degrees at a time. Right trigger attacks (hold for a heavy blow, as with the mouse), right grip uses what the right hand points at within 3 m (the body's `InteractRay` rides the right controller, `VrRig.TakeAim`; a thin beam ends in a spot that turns gold on something usable; the cross in the middle of the HUD sheet is not an aim in VR), A jumps, B dodges. Left trigger guards (or parries, for the classes that cannot block), left grip picks the next ability. A blade that bites buzzes the right hand. The left hand's menu button is Esc, Y opens the bag, X the map.
- **The interface** (`Vr.HostUi`, `src/Party/VrPanel.cs`, `src/Core/VrUiRelay.cs`). A headset shows nothing drawn flat on the screen, so in VR `Main` moves `UiRoot` into a 1920 x 1080 `SubViewport` (`Vr.Ui`) and the rig hangs its picture on a 1.6 m sheet 1.4 m ahead of the head, drawn over the walls. The sheet drifts round to stay ahead while you play and is put squarely in front and held still while `PartyController.InputLocked` is true (a menu, a card, a loading screen). Then the right controller is the mouse: its ray on the sheet moves the pointer (a yellow dot), the trigger is the left button, the right stick scrolls, B is Esc. Keys and actions are passed into the viewport by `VrUiRelay`; the PC's own mouse is not. In VR the hands are live whenever no menu is open (`MouseCaptured` is not asked).
- **Meta Quest Link.** In the PC app, Settings, General, **Unknown Sources** must be on, or Link connects the game and never shows it (the session stops at SYNCHRONIZED and the headset and the window keep the splash).
- **Not there yet.** In a headset the game skips the title screen and starts a new run at once (add `--builtin` for the shipped campaign): before a run there is no body to hang the interface on. The weapon is still fixed to the view and swung by the trigger, not by the hand. No crouch and no hero switching from the controllers. Anything the HUD places by where the flat camera sees it (bars over enemies) may sit wrong on the sheet. The player's eye height is measured half a second after the headset reports in and the room is raised or lowered so their eyes are at the hero's 1.6 m (`VrRig.MeasureHeight`), sitting or standing; clicking the right stick measures again. Untested in co-op.

## Story: cast, mystery, choices

The shipped campaign, The Gullet of Gorm (replaced on 2026-11-12; design `docs/superpowers/specs/2026-11-12-gullet-story-arc.md`, cast sheets `docs/story/character-sheets.md`, readable scripts `docs/story/act-N.md` and `after.md`, made by `scripts/story_script.py`): a good god, Gorm, sleeps under the town of Dunmarrow and his dreams keep the evil god, the Sleepless, out; water running down through the mines drains feeds him, and the town's drain-wardens always kept the drains clear. The orc Morg Ashtusk, the Herald of the Sleepless, has seized the mines and filled them with traps and minions, so the drains back up, the god starves and stirs and the mines shake. The town hires the party to clear them. If the god wakes the Sleepless breaks through and the town is destroyed (the god does not do it on purpose); if the party fails the mines collapse. Four acts end at four great drains, one per tier boss (Canon Ludd, Mother Nit, the Bellows, Morg Ashtusk). The cast: Alderman Crane (the employer, whose council dropped the wardens to save money, owned on floor 17), Master Pellet (the merchant who lent the council the hire money), Deacon Tobit, Grisel Thorn (the last warden), Old Ulf (the pump works, now a foundry), Maud Sorrel (a drowned warden the god speaks through) and Absalom Quill, the Verger (who let the orc in; met on floor 19), plus the companions Foehammer, Tamsin Rook, Oskar Vell and Sister Ione. The choices keep their flag ids: floor 3 `told_tobit` / `kept_his_saint` (pays on 14, 16 and 19), floor 9 `grisel_forgiven` / `grisel_condemned` (pays on 12, where a condemned Grisel is found dead), floor 18 `let_him_dream` / `wake_gorm` (pays on 19 and 20). The four clues are the wardens' log (4), the dismissal order (9), the moulds (13) and the Verger's letter (16). Floor 9 deals no objective on the shipped seed and is written as an ordinary floor (the generated objective of every floor is listed in `scripts/story_script.py`, which marks the lines the engine never plays). Each tier opens with the narrator's tier line on the first visit to floors 1, 6, 11 and 16, before the floor's opening. The whole campaign is voiced: the rewritten lines were recorded on 2026-11-12 (393 lines) and topped up the same day by the final fix wave.

**Saves from before plan 15 keep the old floor 12.** The same holds for the exit island (plan 15): a Story run saved before it carries its
own campaign and floor plans, so its floor 12 has no exit island, no parked train and no lift. A new Story run is needed to see the island.

**Saves from before the rewire keep the old story.** A save carries its own copy of the campaign and its floor plans (`AdventureDto.Campaign` and `FloorPlans`), and a load never re-reads the shipped files, so a Story run saved before 2026-11-12 continues the old story, with many of its lines now silent (their recordings were swept from the bank). Start a new Story run to play this one. On the server the same holds: `game.sh restart` resumes the saved world with the old story, so start it with `game.sh fresh`.

A campaign has a recurring **Cast** (four to six people with a role and a secret), a **Mystery** (a question, its hidden truth, clues spread over the descent, a payoff floor) and per-floor **Story** beats; about eight of the twenty floors carry the plot, the rest get one line of narration. Quest templates: KillTarget, Recover, Escort, HoldOut, and three story kinds: **Investigate** (clues lie in two to four rooms; bring them back), **Decide** (a dialogue choice sets a flag the run remembers and completes the quest), **Betrayal** (an NPC with a `Betrayal` turns when you take a `Betray` choice: they vanish, their creatures stand where they stood, the first wearing their name, and killing them completes the quest; a hand-authored `Betrayal.Escort: true` makes it an escort the NPC flees to instead: they leave alive, no creature wears their name, and the log and the quest say they fled). NPCs, quests, dialogue choices and narration can carry `RequiresFlag` / `ForbidsFlag`, so a floor can hold both branches of an earlier decision and only the matching one appears. Flags live in the campaign memory (saved) and reach the planner in the memory notes.

Two hand-authored hooks the designer is never asked for (both `[AiIgnore]`): an NPC with `"Ritualist": true` on a Ritual
floor leads the rite instead of a stock-named villager (placed beside the altar, no script needed; the validator clears
the flag with a repair note on a floor with no shrine), and a boss with `HalfLine` speaks once at half health, while
`AltDeathFlag` with `AltHalfLine` / `AltDeathLine` swaps its middle and last words when that flag was set earlier in
the run. The shipped Gullet of Gorm uses both: Grisel Thorn as the figure at the seized pump on floor 7 and Maud Sorrel at the
altar on floor 19 are the ritualists, and Morg's half-health and death lines on floor 20 turn on `wake_gorm`. The recurring cast's lines are recorded by the voice bake (see `--bake-cast-voice` and `--bake-all-voice`). Maud Sorrel also stands on floors 1, 6, 11 and 16
with a few lines and no quest, and speaks for the god on 18 and 19.

A floor's `Narration` entries are played by their trigger (`AdventureDirector`, server and solo only; clients hear them as `NarrationMsg`): `RoomEnter` when any active party body stands in that room (never written for a floor's start room, where the opening is still being heard), `BossSeen` / `BossDead` once the boss's own line has been heard (a silent boss's entries come at once, and the lead's BossSeen bark is skipped then so the narrator does not cut it), `QuestDone` / `QuestFailed` two seconds after a quest on the floor ends. `Voice/NarrationRules.cs` decides which entries are due (trigger, room, `RequiresFlag` / `ForbidsFlag`, not yet said) and keys each one by floor, trigger, room and text; the key is kept forever in `AdventureDto.BarksSaid`, so an entry plays once per run across saves and revisits, while one held back by a flag can still play later. Entries due together play one after another in plan order, each once the line raised before it has been heard: one voice channel (`Voice/SpeechClock`) is held by every line raised as narration (an entry, a taunt, a boss line, an intro, a tier or horde line) for its `HeardSeconds`: the recording's real length plus 0.3 s when the bank holds it, else the `SpeakSeconds` estimate; a queued entry waits until it is free. On the last boss's floor the victory recap and its sting wait until the closing `BossDead` entries have drained and been heard (`NarrationRules.RecapDue`, at most 30 s after the drain); a campaign with no closing narration shows the recap at once. In multiplayer the server decides and clients get the narration and then `RunRecapEvt` as before. A boss named like the campaign's antagonist is queued and voiced as the villain, so the Herald keeps one voice from the floor 1 taunts to the last boss. `--test-narration` checks the tier line opening floor 1 and the intro after it, a room line waiting its turn, the once-only rule through a save and load, floor 1's parting taunt before floor 2's own opening, and the victory recap held until floor 20's closing lines have been heard.

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

**The mine cart chase's lines are not voiced yet.** Every line plan 6 added for floor 12's rides is an unvoiced subtitle (`VoiceId`
null), waiting for the owner's OK for a paid voice pass. No bake has been run for them, and a headless check runs from a terminal with
no keys, so such a run renders none of them. A run with keys present is different: with `RUNWARE_API_KEY` or a Gemini key in reach
(an F5 run from the keyed VS Code profile, for one), floor 12 queues each new line as it plays and `VoiceRenderer` renders it into
user://voice live, piecemeal, and spends money. They are, by kind and speaker:
- the six ride taunts (16 lines in `campaign.json`, two stock lines each in `TauntBank.Stock`): Morg Ashtusk, a man (`Voices.Villain`,
  Fenrir, rendered as Dominus);
- Grisel Thorn's calls `ride1` to `ride5` and `cage` (`npc_grisel.Calls` in `floor_12.json`): a woman (her persona's Despina, rendered
  as Abby);
- the six ride barks (`RideBarks`, topped up into every bark set): each hero in their own class voice; Brand, Wren and Ilex are men,
  Sable is a woman;
- the three rail encounter lines (`RailEncounters[].Line` in `floor_12.json`): the narrator (Charon, rendered as Mortimer).

The crew stay silent, and Oskar Vell keeps only his kill barks. Once the owner OKs it, the pass needs no code change: run
`--bake-kill-barks=assets/voice/builtin` (it tops up every bark set from the stock banks, the ride barks among them, and gives the
lines their ids) and `--bake-all-voice=assets/voice/builtin` (every line the shipped campaign can say, the taunts, Grisel's calls and
the rail lines included), each with `--builtin --ai` and the voice flags as in "Build, test, run". Then copy
`assets/voice/builtin/all-plans` over `data/adventures/builtin` and run `--import`.

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
screen. The shipped campaign has six (Crane's "I am not the man to do it" earns Sable's "He means us.").

**What other pillars call.** `AdventureDirector.NoteDeathCause(WipeCause.X)` (elites: Bomber, Elite),
`AdventureDirector.LastWipe` (`WipeInfo(Floor, Cause, Count, Epitaph)`, set as `PartyWiped` fires; run-structure's death
card reads it on `PartyWipedBy`), and the `EventBus` moments: `RaiseEliteSeen`, `RaiseEliteSlain`, `RaiseMimicSprung`,
`RaiseShrineUsed`, `RaiseSecretFound`, `RaiseTrapSprung`, `RaiseMemberFell`, `RaisePerfectBlow`, `RaiseParried`,
`RaisePerfectBlock`, `RaiseGambled`, `RaiseCheer`, `RaiseBrokeAtShop`, `RaiseLowHp`, `RaiseBoonTaken`, `RaiseCurseTaken`,
`RaisePartyWipedBy`, `RaiseFloorLedgerClosed`. Trap, shop refusal, a fighter's refusal (`NpcEventKind.Refused`) and a
secret wall's opening are raised today; the rest wait on their pillars.

**Voice.** New text is unvoiced until a bake runs; the shipped campaign's every line is recorded. `--bake-all-voice=DIR` (with `--builtin --ai` and the voice flags) renders every
line the shipped campaign can say at once with no budget: tier lines, every taunt, every bark set topped up from every stock bank, the
banter (the stock and unlockable exchanges are poured into `CampaignPlan.Banter` by id so their ids ship; `BanterBank.BankFor` draws
from the campaign's copies first), the merchant, and every floor's intro, narration, boss, cast, aside, taunt and horde line; lines the
bank holds cost nothing, and the campaign and the twenty plans are written with their ids to DIR/all-plans to copy over
`data/adventures/builtin`. `--voice-concurrent=N` sets the lines in flight (2 by default; Runware takes 4). The older flags remain
(`--bake-kill-barks` tops up every bank and gives the lines their ids; `--bake-cast-voice` for the campaign's own lines); `VoiceBudget` counts asides beside their NPC line. In the shipped
campaign only the taunts' clean twins carry `VoiceId: null` (they render under their coarse line's id); a token such as `{party}` is never written into a shipped taunt, since the recording cannot say what the subtitle fills in (banter's `{count}` and `{roman}` are safe: a line whose text a token changed is said as a fresh, unvoiced line).

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
- **The blade** (`src/Combat/Core/BladeRules.cs`, `Adventurer.TickBlade`/`CutAlong`). The body you play hits what its blade touches, when it touches it. A melee swing no longer lands at the click: `Adventurer.MeleeSwing` keeps the `WeaponMove` the first-person view is playing (`FirstPersonWeapon.PlayAttack` returns it, fitted to the swing's time) and every physics tick walks the blade along it. Only a move's cutting strokes count (the key before each `Impact` key up to it, `BladeRules.Cuts`), and only while the blade is lowered (`LiveBelowElevation`, 45 degrees). The blade is a capsule 0.3 m thick (`Thickness`) laid from the chest along the move's bearing (`Left` plus `AimLean`) in the plane you are looking along, as long as the weapon's `Range` and shorter while the hand is drawn back; it is looked at every 8 degrees or less and always at the start and end of a stroke (`Steps`). So a sweep cuts what lies along its arc one body after another, a chop or a thrust only what stands ahead, and nothing behind you. The moves were drawn to look right on a screen: their timing, bearing and reach are taken literally, their height is not. Each target is cut once a swing; each body already cut leaves 75% of the blow for the next (`Carry`, `HitContext.Carry`); a wall between the eye and the spot stops the cut (a ray on layer 1). A touch always lands: with `HitContext.Contact` a failed hit roll is a glancing blow for half damage that cannot crit (`DamageResult.Glancing`, "glances off" in the log), not a miss. The first body bitten hangs the swing 0.03 s (0.06 s heavy; `FirstPersonWeapon.Hitch`), and the blood comes from the spot, along the cut (`Enemy.AimBlood`). Companions, hero bots, the bodies a server runs for its clients, a client's own swing (the server still rolls it from `AttackIntent`) and a blow with no blade (`WeaponMove.Blade` false: the shield bash; fists) keep the old cast: `MeleeCast` from the camera, at once. `Adventurer.BladeContact = false` gives everyone the old cast; every `--test-…` flag written before the blade sets it, because those flows read a blow's result in the frame they swing. `--test-blade-contact` checks the blade headless. In VR the same contact code is meant to take the blade's place from the controller instead of from the move.
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

Floor 12 is the island floor (`PitRide` 5, `ExitIsland` true, plan 15): a generator change that moves it shows as its `LayoutHash`
(10116286907070122254) changing in the rehash, and must also keep its island proven (`IslandTests`, the 116-row island sweep) and its
ride 5 measured (`--print-map --track-depth`, see "The five rides"); the pieces there are placed from the measured S, so a moved line moves
them.

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
  is not the stairs', the arena, the safe room, the objective's jail or shrine, a vault, a secret room or a mine-cart
  stop (`Rail.Stops`), with at least five free cells; dead ends first, then nearest the midpoint of the stairs route;
  the cell nearest the room's centre that holds nothing, is a cell away from every entrance and cuts nothing off when
  blocked. `DungeonBuilder.PlaceShrine` puts the altar there (`AltarMeshes`, shared with the objective's altar) with
  its price written over it. One in three
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
  in the same expression (`Enemy.OnDeath`). A cellar's chest is `SpawnIndex` `ChestSpawns.Count + cellar index` (it was
  `Count + 8` for every cellar, so two cellars shared one; `FloorStateCapture.Apply` reads that old number in a save as
  every cellar chest on the floor opened, as it meant then).
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

## Rails

Ring floors can carry a mine-cart line round their outside (the floor 12 chase). Five `FloorSpec` knobs drive it, each set
by a campaign floor through `SpecOverrides` (`"RailCount"`, `"RailLoop"`, `"RailBand"`, `"Crevasses"`, `"BrokenBridgeRide"`); they only apply to the ring layout,
and with `RailCount` 0 (the default) nothing changes, byte for byte:
- `RailCount` (0..6, clamped by `CampaignPlan`) is the number of stops past the start.
- `RailLoop` puts the down stairs in the start room's nearer ring neighbour (by centre distance, ties to the lower index),
  so the line has to run the long way, the whole ring round. An objective that moves the stairs (a ShiftingVault, a boss
  arena) wins, and such a floor may get no line.
- `RailBand` (default `RailRules.Band`, 6; clamped 6..16) is the band of rock outside the ring for the line. A railed ring
  grows the grid by the band on every side while the ring's radius shrinks by the same band, so the same rooms come out,
  translated by the band; room indices and order do not change, so plans keep pointing at the same rooms.
- `Crevasses` (bool, default false) cuts chasms beside a laid line and plank crossings in the halls (see "Crevasses"
  below). It is its own knob, not keyed on `RailBand`, so with it false every floor's `ContentHash` is unchanged. Floor 12
  sets it in the built-in campaign's overrides (`SpecOverrides.Crevasses`, which is `[AiIgnore]`: the planner never sees it).
- `BrokenBridgeRide` (int, default 0, clamped 0..6; `[AiIgnore]`) puts a broken bridge on that ride's slag line (plan 11; see
  "Crevasses" below). It acts only with `Crevasses` on, so with it 0 (or `Crevasses` off) every floor's `ContentHash` is
  unchanged. The generator never reads plans, so the ride is named here rather than inferred. Floor 12 sets it to 3.
- `PitRide` (int, default 0, clamped 0..6; `[AiIgnore]`; plan 15) cuts a lava pit into that ride's party lane, and `ExitIsland` (bool,
  default false; `[AiIgnore]`; plan 15) turns the exit room and the rooms only it reaches into an island over lava, reached on foot by
  nothing and by rail only over the exit bridge. Both act only with `Crevasses` on, and with both off every floor's `ContentHash` is
  unchanged (`RailLineTests.DefaultKnobs_BuildTodaysFloors`; the knobs-off floor 12 still hashes 16995108853750381062). Floor 12 sets
  `PitRide` 5 and `ExitIsland` true (`campaign.json`), and its `LayoutHash` is 10116286907070122254 (`8C644557F5D9890E`). See "The pit
  and the island in the generator" below.

A band wider than 6 meanders the line (`FloorGenerator.Meander.cs`, no rng). After each stretch is routed, a ride
(platform middle to platform middle) shorter than `RailRules.RideMinCells` (55 cells, 220 m) gets outward rectangular
bulges, the deepest first (up to `RailRules.BulgeMaxDepth`, 9), `BulgeSpan` (4) cells along, nearest the middle of the
stretch first, until it reaches 220 m, never past `RideMaxCells` (82, 330 m). Every new cell of both lanes must pass the
same lane rules as the route; bulges keep off the platforms' shadows and the ramp's run, and the lane must stay clear of
itself (`SelfClear`: line cells that touch are at most `RailRules.SelfClearGap`, 4, party indices apart, which allows the
enemy lane's inside corners but not two legs side by side). When the bent stretch, or the whole bent line (pockets,
validation), does not hold, the plain route is used instead, so a wider band never costs the line. Floor 12's chase is
tuned for band 14.

On a meandered line the stretch into the last stop before the exit keeps a cavern window: `RailLine.Cavern`, a span of
party-lane indices at least `RailRules.CavernCells` (10) long, straight, whose cells 3 to 2 + `RailRules.CavernDepth`
(3..7) steps outward were solid rock outside every room when the line was planned (the enemy lane is at 1 and its pockets
at 2, so every pocket rule still holds). The window is taken on the plain route and the meander keeps off it; it is null
when that stretch has no straight run long enough (about a third of seeds) and on every line that did not meander. It is
not part of the content hash. The box is enforced, not only tested: stretches routed after the cavern's stretch treat it as
foreign, no refuge pocket is placed in the cavern's reach (t = 2 .. 2 + `CavernDepth`) and the window's enemy-lane cells need
no pocket cover; if a line or pocket cell still lands in the box, `CommitRails` drops the span (`Cavern = null`,
`CavernBreached` true) rather than losing the line. With `Crevasses` on, `CarveChasms` cuts the cavern from it right after
`CommitRails` (see "Crevasses" below).

`FloorGenerator.LayRails` (`FloorGenerator.Rails.cs`) runs after the ring and its ramps, from its own salted rng
(`new Random(seed ^ 0x2A11)`). On a railed floor the grid grows and the keep-clear rules change corridors, side rooms,
ledges and alcoves, so the rest of the seed does not build as it would without rails. The line rooms are the start
(up stairs), the stops and the exit (down stairs). It lays twin lanes round the band (`FloorLayout.Rail`: the party lane
and the enemy lane), the stop platforms, side pockets where the party can step off, and ramps where a lane has to change
height. On a railed ring the outer edges of the ring rooms are kept clear so the lanes never run into a wall or a prop.

**The pit and the island in the generator** (plan 15, `FloorGenerator.Island.cs`). With either knob on, `CarveChasms` runs two passes
after the strips and the gorge are cut and before the plank crossings are laid; everything else in `CarveChasms` is as before.
- **The plank pass is picked before the cuts.** `PickWalkways` chooses the plank crossings exactly as the old `CarveWalkways` did, against
  the line as it stood, and `CutWalkways(picks)` lays them only after the pit and the island are cut. A pick on a hall the island
  sealed is skipped; one that no longer passes `WalkwayCell` is logged `[gen] island: plank at {c} dropped`. So the island never moves a
  plank on a floor where it is refused, and floor 12 keeps two of its three (`planks=2`).
- **The pit** (`CarvePit`): on ride `PitRide`, the first run of `RailRules.PitRunCells` (11) straight party-lane cells lying whole between
  fractions `PitFrom` 0.05 and `PitTo` 0.60 of the ride, each with its enemy twin a free straight enemy cell, both lanes at one level and
  no ramp on or beside it. The cell `PitLeadCells` (7) from the run's start becomes `Cell.Chasm`, with the rock or pockets inward of it
  (t = −1, −2); its enemy twin stays solid lane with no deck. Refused on a ride the line does not have, the broken bridge's ride, the
  split's ride or the cavern's ride (`[gen] pit: refused (…)`); `none` with no fitting run. `RailLine.Pit` is a `RailPit(Ride, Cell, Cut)`.
  Seven cells of lead (28 m) hold the deadly switch's 8 m lead, its lever's 5 m and its 15 m window; the three cells after it (12 m) hold
  its back's 1 m and the 6 m home stretch.
- **The island** (`CarveIsland`): the exit bridge is the last run of at least `BridgeMinCells` (4) straight party-lane cells on the last
  ride ending `BridgeIslandCells` (4) cells before the exit platform, its cells cut to chasm with their track kept, and each straight enemy
  twin too. The island's rooms are the exit room and every other room (not the start's) that the up stairs reach only through it with the
  rails blocked. Each hall mouth beside an island room is followed to the cell before a fork or a room: its mouth becomes a lava trench and
  the rest a cave-in (`Cell.Wall`). The moat cuts `MoatDepth` (2) cells beside each bridge cell (inward of the party lane, outward of the
  enemy lane). The parked train's place is the `ParkedCells` (4) cells before the bridge (`RailIsland.Parked`). A trap on a sealed hall is
  removed and logged (`[gen] island: trap at {c} sealed`); any other taken or reserved cell there refuses. It refuses
  (`[gen] island: refused (…)`), every cell it touched restored, when the exit room is the start's, the parked place reaches the ride's
  first platform, the pit lies on the bridge or in the parked place, a sealed hall holds a taken or reserved cell, or its own proof fails
  (the up stairs reach an island cell, or a key, vault lever, prisoner or jail cell lies inside); `none` when the last ride has no bridge
  run. A refused floor is today's floor byte for byte. `RailLine.Island` is a `RailIsland(Rooms, Bridge, Parked, EnemyBridge, Trenches,
  CaveIns, Moat, Cells)`; `FloorLayout.InIsland(p)` reads its cells.
- **The `[gen]` lines.** `FloorLayout.GenLog` (not hashed) collects the passes' notes, and `DungeonBuilder` prints each as `[gen] {line}` right
  after its `[DungeonBuilder] floor …` line. Floor 12 prints one: `[gen] island: trap at (46,49) sealed` (its traps go from 4 to 3).
- **Later passes keep off it.** `PlaceSecretRooms` (by the secret's wall cell), `PlaceCaches`, `PlaceBoundChest` and `PlaceShrine` take the
  island's cells and their one-cell ring as forbidden, so nothing placed later opens a way in or out or hides a key inside.
- **Over the generator sweep** (`IslandTests`, 116 `SplitSeeds` rows with the island on): 41 floors keep an island (12 with the pit as
  well) and 75 are the floor without it; `FloorSolvability.Problems` is empty on every row.
- **`layout.Connections` still lists the sealed room pairs** (on floor 12, 9–1, 0–9 and 8–9). This is harmless: the baked plan never reads
  them for a walk, and every walk the game makes reads the grid, where the trenches and cave-ins stand.

`FloorSolvability.Problems` also runs its checks with the lanes and pockets blocked (the platform cells stay open) and
reports every problem that appears only then, as "with the rails blocked, ...": the stairs, every cage and every key must
still be reachable on foot. The up stairs can never be on a lane. A requested line that could not be laid leaves
`FloorLayout.Rail` null and says why in `FloorLayout.RailFailure` (also warned from `DungeonBuilder.Build`; the floor log
line carries `rails=N`).

**The rail proof on an island floor** (plan 15, `FloorSolvability.Island.cs`). With `Rail.Island` set, the stairs check is replaced by
`IslandProblems`, since nobody walks to the down stairs there: the train carries them. In both runs (rails open, rails blocked) each
intermediate stop's platform must be reachable on foot from the up stairs (`on foot, stop {k}'s platform at {c} is out of reach`). With
the rails open: each cell of the parked train's place (`on foot, the parked train's place at {c} is out of reach`); every room outside
the island that the cuts would cut off (`on foot, room {i} is out of reach`); every key outside the island (`on foot, the key at {c} is out
of reach`); the island sealed with every door and secret open (`the exit island leaks at {c}`); the line into it whole by rail, from the
parked place to the exit platform, the bridge cells chasm and the rest walkable (`the line into the island breaks at {c}`); inside, every
island room and the down stairs reachable from the last stop's platform; and the pit's neighbours and enemy twins walkable (`the pit cuts
the line at {c}`). A platform or a parked place off the lane is a problem line, never a silent pass. `ChasmProblems` skips the island's
rooms, cells and the stairs check. Over the 41 island rows of the sweep, the old checks would have given `the stairs down at … are out of
reach` on every one; the proof gives nothing. `--print-map` draws the line: `=` party lane, `%` enemy lane, `)` pockets. Before the drawing it
prints `LayoutReport`: a `[map] room {i}: ({X},{Y}) {W}x{H} level={L} {hub|ring|side|secret}` line per room with its tags
(`up`, `down`, `stop={k}`, `vault={index}`, `jail`, `safe`, `nest`, `camp`, `shrine`), then
`[map] rail: stops=[rooms] rides~[m] total~{m} cavern={From}..{To}|none|breached chasm={cells} strips={n} gorge=[{From}..{To}@{fFrom}-{fTo},…] planks={n}`
followed, with `BrokenBridgeRide` set, by ` break={Gap}@{f} run={From}..{To}` (the gap's and its run's party-lane indices, f the gap's
fraction of its own ride), ` break=none` (no fitting run) or ` break=refused` (the cavern's ride, the last ride or a ride past it),
and, with `SplitRide` set (plan 12), by ` split={Points}@{f} crevasse={From}..{To}` (the points' and the crevasse's party-lane indices,
f the points' fraction of their own ride), ` split=none` or ` split=refused` (the last ride or the broken bridge's ride; see "Crevasses")
(rides in cells x 4 m, platform middle to platform middle; `chasm` the floor's chasm cells, `strips` the edge strips, `gorge`
each gorge span by party-lane index and as fractions of the last ride, `planks` the hall crossings), and the cart's measured
`[map] rail StopS=[...] rides=[...] m total=... m`, then, with a break, `[map] rail break: ride {r} gap at party S={S}` (the party
lane's S beside the gap's near edge, good to about 1 m; the switch back's build line prints the exact lead), and with a split
`[map] rail split: ride {r} points at party S={S}, crevasse S={from}..{to}`.
`DungeonBuilder.PlaceRails` dresses it (rails, sleepers, timbering, pocket lanterns) under `RailTrack` nodes named
`PartyTrack` and `EnemyTrack`, with a `RailStop0..N` marker at each stop (`--face-node=RailStop1:3` for a screenshot).
The centreline is straight 0.5 m pieces laid with no handles (finer only inside a ramp's eases, where the bed turns: see the
track bed), so `TrackSampler.Frame` takes its heading from the chord ±0.25 m about S (`Rails/Core/ChordHeading.cs`; the origin and the up are the baked curve's): every cart, rider and camera turns
evenly through a bend instead of in a 9.5 degree step at each join, and the points, the length and every S are unchanged.

**The track bed.** `PlaceRails` lays each lane with `RailCurves.CentrelineOver(lane, xz => FloorTop + RailBed.SurfaceAt(layout,
xz, CellSize))`: the same 2D path as `Centreline` (straights between cell centres, a quarter circle at each turn), its straight
pieces cut to 0.125 m, each point's height the drawn floor under that point, then the same 0.5 m resample, except where the bed
turns: every 0.125 m vertex whose pitch changes (inside the eases) is kept, and the resample points between them are left out, so an
ease is laid as its eight pieces instead of two or three 0.5 m chords (on flat ground and along a ramp's even slope the line is the
plain resample; each ramp's two eases lengthen the lane by about 4 mm against the chords, so S past a ramp moves by that). `RailBed.DrawnAt`
is the walk surface the GridMap draws: a flat cell's level·4, a ramp cell's tilted slab top `LowHeight + RampSlabRise +
RampSlope·u` (u along its `Dir` from the cell's low edge; `RampSlabRise` 0.0118 m is the 0.2 m slab's tilt, 0.1/cos 26.6° − 0.1,
so the ballast clears the slope instead of z-fighting it). `SurfaceAt` is that, eased over `EaseLength` (1 m) at every ramp end
with a parabola tangent to the flat and to the ramp (`RailBed.Ease`; the lines meet 0.0236 m outside a ramp's low edge and inside
its high edge). The ease costs a crest dip of at most about 7 cm under the upper floor's edge (6.25 cm from the ease plus the slab
rise's step) and a sag float of at most about 7 cm; the 0.125 m pieces miss the eased curve by under 0.1 cm (the 0.5 m chords
missed it by up to 1.6 cm more), and the probe reads a 4 cm float on floor 12 (6 cm on the chords). The sleeper tops stay proud on
floor 12 and wherever a straight runs onto a ramp; where an arc runs onto one (other seeds, no shipped floor) a sleeper end can
sit up to 1.9 cm under the slope, under the probe's 3 cm. Before this
the heights were taken only at the path's vertices, so a ramp's 4 m rise was spread over the whole straight between two corners
(a slope of 0.13 over about 30 m): the track ran up to 2.4 m inside the upper level and floated up to 2 m over the lower one. Now
the cart climbs the drawn 26.6° ramp itself (an unpumped cart arriving at cruise, 6 m/s: the haul rope holds 3.06 m/s up it, the
descent peaks at 7.56 m/s, `RampRideTests`),
and each ramp lengthens its lane by about 0.64 m (the 8.94 m slope against the old smeared rise; each `ramp` line's span on
floor 12 grew from 8.1 to 8.9 m): every S past a ramp moves up by that much per ramp, and a set piece on a ramp's ride (a fraction
of its ride) by its fraction of it. A lane cell is never heaved (`BuckleRules.Pick` spares every `IsRail` cell, keeping both rng
draws), so the track lies on a flat slab wherever the floor is not a ramp. `--track-depth` (the command list in "Build, test,
run") is the proof: floor 12 reads `buried=0 inside=0 nofloorOff=0 budget=0 floating=0`. A kiln spur bends the largest bend, from 1.8 m down to
1.1 m in 0.05 m steps, that passes the rock, sleeper-gap and beam checks (`SetPieceRules.PickSpurBend`): 1.50 m on floor 12, in short
of the wall at (48,62) (see the spur in "The five rides"). `--test-track-view` (the same command list) rides floor
12's first ramp and a spared lane cell and sights the rails from the seat, with three control rays: one under the drawn floor at the
crest that must be blocked, and one under the bed at each rail over the spared cell that its slab must stop at the bed's height.
`TrackSampler.Curvature` (the lean's input: the heading's change over the next 2 m) reads the same chord headings
(`ChordHeading.Curvature`), so the lean's target no longer steps by a join's worth as the span takes in four joins or five.
Each track's rails, sleepers and ballast are one shadowless mesh per 20 m of S (`RailTrack.ChunkLength`: `Rails0..N` under
`Rails`, likewise `Sleepers` and `Ballast`; the normals are generated over the whole track before the split, so no seam), so
the stretches out of view are culled. The timber frames and pocket lanterns fade out around 40 m (`VisibilityRangeEnd` with a
5 m margin) and the pocket lamps over 35 to 45 m (`DungeonBuilder.RailDressingRange`), where the fog has taken most of them.

**The ride over a ramp.** The seat's vertical acceleration (m/s², through the comfort sampler's 0.1 s smoothing, as
`RampRideTests.TheEasedRamp_VerticalJolts_StayUnderTheirCeilings` prints it; the cart arriving at the speed its drive aims for) at
the crest (the dip over the top, negative) and at the foot (the sag, positive):

| cart | arrives at | descent crest | descent foot | climb crest | climb foot |
|---|---|---|---|---|---|
| no pumpers | 6.00 m/s | -12.7 | +18.0 | -4.5 | +11.1 |
| 1 pumper | 7.20 m/s | -16.5 | +21.1 | -7.3 | +15.1 |
| 2 pumpers | 7.80 m/s | -18.2 | +22.8 | -9.4 | +17.3 |
| 2 pumpers, 3 or more wagons (every ride: the long train, plan 14) | 6.78 m/s | -14.8 | +20.0 | -5.8 | +13.5 |
| ceiling (the test fails over it) | | 20 | 25 | 10.4 | 19 |

The foot is the harder jolt in every row (about 1.1 to 2.3 g). Every flow pumps with the lead, and companions pump too. Since plan 14 the
train's four wagons are coupled from the landing, so the "3 or more wagons" row is every ride's from ride 1, not ride 5's alone (the load
counts three at most, `CartRules.MaxLoadWagons`); an unpumped train arrives at 5.22 m/s, under the empty cart's 6.0, so its jolts lie
under the "no pumpers" row (the test measures no such row). The figures
go with speed squared over `EaseLength`; on the 0.5 m chords (before the eases kept their pieces) they read -14.6/+16.8/-6.9/+14.5
unpumped and -19.9/+22.6/-12.1/+18.1 with two pumpers (the test reads the laid points as straight pieces; the engine reads the
baked curve, 0.2 m and cubic, which rounds each joint a little more). A longer ease would soften them but dips the bed further under each crest
(`EaseLength` 2 m puts the sleeper tops 1.7 cm inside the upper slab). `ComfortRules.DropAccel` is -4, so Strong's drop fade now
fires at every ramp crest, the climb's included (it never fired on the old smeared track); Recommended, the default, has no drop
fade, so a headset rider at the default level takes the crest and the foot unmitigated.

Riding (`Dungeon/Rails/RailController.cs`, a node under the floor root; the engine-free rules are in `Dungeon/Rails/Core`):
the party's cart waits at the first stop with its four wagons, W1..W4, coupled behind it (plan 14; see "Stops, cages and the crew").
Board it with E (not at the end of the line); companions within 25 m hop in with you. The cart leaves 5 s after the first hero boards; it leaves as soon as everyone living is aboard, but never sooner than
2 s after the countdown began, and not while freed crew are still walking to their seats, up to 8 s from its start (see "Running
to the seats"). It rolls to the next stop, where the countdown starts again. It only leaves with a player
aboard: a companion on foot whose lead is seated never regroups onto the lead (that would put it in the tub, or ahead of a
moving cart); while the cart is held at a stop it hops in (and any companion that comes within 3 m of the held cart does),
and while the cart rolls it holds where it stands. A cart that is held, or arrives, with no player aboard sets its riders
down beside the lane there and stays. Seated, forward pumps and back brakes; seated companions pump on climbs and when the
cart is slow. The train's drag counts from the landing: `CartInput.CrewWagons` is the number of coupled wagons (four, of which the
load counts three, `CartRules.MaxLoadWagons`, 5% each), so the train cruises at 6/1.15 = 5.22 m/s unpumped on every ride (6.0 m/s
for the cart alone), 6.26 m/s with one pumper, and climbs ride 3's ramp at about 2.61 m/s (the spec's recon). E again gets you out:
below 1 m/s you step out beside the lane and your companions follow you, but getting out of a moving cart (1 m/s or more) gets
you run over once, and you land on the line just behind the cart (the others ride on). A hero seated in a wagon is offered "Get
out" only while the train is held or rolls under 1 m/s (`TrainMoveRules.WagonGetOut`), and steps out alone; at speed a wagon
offers only the move car to car (see "Fighting along the train" under "The five rides"). The "Cart coming!" warning looks 3 s ahead, at 3 m/s at least. Anyone on foot in the cart's path is run over too, as the press does it, by the lethality
setting: mangled at the default 3, part of max HP below 2.5. Seated heroes are never hit, and a body already down is not
hit again. Big enemies (scale 1.05 and up, and bosses) are hurt, stunned and knocked aside instead, and dent the plating by
15. At 0 plating the cart is wrecked: it stops for good and the riders climb out beside the lane, each losing 25% of max
HP. The side pockets are off the lanes, so a hero standing in one is safe. Stop i is the `RailStop{i}` marker; the stops
must lie in order along the party track, or the floor gets no cart (an error is logged). The save keeps the stop reached
(`FloorStateDto.RailStop`; a ride under way keeps the stop it left) and who was seated (`FloorStateDto.RailRiders`, party
indices). A load or a re-entry puts the cart Held at the stop it last reached, with full plating and nobody aboard: the
saved riders, anyone set down inside the cart and anyone standing on a rail cell step out beside it, on cells of their own
where there is room. Online every client rides the same cart (see "Riding together" below). `--test-ride` rides floor 12
of the built-in campaign at lethality 3:
board, run over an enemy on the way to stop 1, save and load there, a companion jumping out at speed, and a wreck.
`--test-ride-edges` covers the edges: a companion downed at boarding gets up far behind the rolling cart and never
regroups into or ahead of it (it hops in at stop 1), a save between stops and a load, the human thrown at lethality 1 (hurt
once, not downed), and the companions it leaves riding are set down at the next stop, where the cart stays. It turns the
relief cart off from its throw (`DebugTools.NoRelief`): its "hit once, not again" check leaves the thrown human on the line,
where a relief would run them over (`--test-relief` covers the relief; see "The relief cart (single player)" below).

Floor 12's stops (built-in campaign; `Rail.Stops[i].Room`) and what waits at them:

| Stop | Room | Name | Mobs |
|---|---|---|---|
| 0 | 1 | Kiln-Mouth Landing (start) | Grisel, when forgiven |
| S1 | 2 | Ladle Ledge | `col_stop1` The Cage Watch, 2 cinders (on `col_ring`'s patrol route) |
| S2 | 4 | Oddfellows' Fire (camp) | `col_oddfellows`, 2 bandits, a bandit archer and the chief, hold the gate; `col_ring`'s patrol (a forge priest, hub 0, 3, S1, 3, S2) ends here |
| S3 | 6 | The Pouring Gallery | `col_stop3` The Pour Guard, a forge priest and a cinder brute |
| S4 | 8 | Shelf of Blanks | `col_stop4` The Clinker Watch, a cinder and an ember hound |
| 5 | 9 | Cooling Stair (exit, beside the landing) | `col_stairbell`, an ember hound |

Off the line: the hub (0, The Casting Floor, the shrine), The Slag Walk (3, the locked chest), The Cinder Breaches (5,
the horde nest and its chest), Tenders' Quarters (7, `col_stop2` The Slag Crew, a cinder and an ember hound), The
Founders' Strongbox (10, the reward vault, `col_vault`), The Kiln Cages (11, the jail) and the safe room (12). The line
runs the long way round the ring, from the landing to the Cooling Stair beside it (`RailCount` 4, `RailLoop`, `RailBand`
14): five rides of 206 to 281 m, 1214 m in all, with the cavern span on ride 4.

The eight named crew (`FloorPlan.Crew`, validated by `PlanValidator.ValidateCrew`: no cast names, capped at two per cage, two per cage across four stops)
are seated by the crew controller, not the resolver. A crew id another NPC of the plan holds, or `grisel_body` (dead
Grisel on the condemned branch), is dropped ("crew <id>: dropped (id taken)"). The "caged crew" narration fires at S1.

### Stops, cages and the crew

A jailbreak floor with a rail line runs `CrewRescueController` (`Adventure/Objectives/CrewRescueController.cs`) in place of
the walk-in `JailbreakController`; floors without rails (floors 2 and 13) keep the walk-in jail as it was. Every state
change is the server's (or solo's); a client shows the crew, the cages, the keys and the gates as the server tells them (see "Riding together").
The jail room stays a locked vault with nobody in its cells.

The gates. Each intermediate stop whose room holds a guard or defend collective gets a `RailGate` across both tracks (the
party lane and the enemy lane beside it) just past the platform, with a winch post on the outer side of the party lane.
The posts stand 1.05 m out from each lane's centre line (`RailController.GateSide`: the tub's half-width and a margin), so
a cart passes between them, and the lifted bar sits at 2.95 m (set clear of the old cage wagons' 2.57 m roof; plan 14's open wagons
stand far lower). A stop with no such mobs gets no gate. A gate starts down
while any member of those collectives lives. A patrol or alarm group passing through does not hold a gate. The cart
rolls into the stop as usual but does not count down to leave, and the HUD says "Cart — gate down". Every 0.25 s the
server lifts any down gate whose holders are all dead. Riders already seated then start the boarding. The winch ("Crank
the winch") lifts the gate by hand, but only when no living holder stands within 12 m of it; otherwise the log says
"Not while they're still here."

The train (plan 14; `Dungeon/Rails/TrainWagon.cs`, `CartTrain.cs`, the numbers in `Rails/Core/TrainRules.cs`). Four open wagons, W1..W4
(nearest the cart first; one per cage placed, `TrainRules.WagonsFor`), are built under the floor root on every role
(`RailController.BuildTrain`) and coupled behind the party's cart from the landing. They are box tubs the cart's size with no roof,
`TrainRules.Spacing` (2.6 m) apart, so the train is `TrainRules.Length` (12.6 m) long; held at stop 0 its rear is at S 4.5, on room 1's
platform. Each wagon has one hero place (seat 0, at the cart's seat-0 spot), two crew seats (the bench's 4 and 5,
`TrainRules.CrewSeats`) and one boarder spot on its rim at the cart's rim height. A coupled wagon is shown and on layer 8; it offers
"Revive {name}" for a downed crew member seated in it whose revive would be taken (`Npc.ReviveTaken`, the one rule `TryRevive` refuses by:
`RitualRules.ReviveTaken`, no living enemy aware of the party within `ReviveClearRadius`, 6 m, `RitualRules.CountsAgainstRevive`: alive and
`!IsUnaware`, which a client reads from the server's snapshot, so every peer offers it alike; every enemy, a client's copies included, and
the crew live under the floor root; plan 15 Task 0) and "Get out" to its own rider only as the move rules allow (its plain prompt,
`RailController.WagonPlainPrompt`), and only when that offers nothing the move car to car to a hero in another car (see "Fighting along
the train" under "The five rides"); else nothing. At speed the look reaches the revive (plan 14's final review I1): a seated hero's view
asks the ray first and takes what it finds within reach that offers something other than a move, the hero's own wagon included (its
revive, or its "Get out" under 1 m/s), and a car the seat's yaw offers a move to offers its revive in the move's place (a cart rider
looking back at W1, whose rim the ray's 3 m does not meet from the front row) only within the server's reach of it (`InteractReach`,
3.5 m to its tub: `RailController.PlainUseStands`); farther, it offers the move. A revive the game would refuse (a boarder beside the
downed crew member) is no prompt at all, so it never masks the climb (plan 15 Task 0, the plan 14 final re-review's N1 and N2). Seated crew are on no layer, so the ray finds their wagon,
not them. The interact ray passes through anything offering nothing to the next hit, up to six times (`Adventurer.FirstOffered`, a car of
the train read by `RailController.PlainPromptOf`, without its move), so a wagon never blocks a prompt behind it (the cart, a lever, a
downed NPC; a cage stands 4.18 m past a wagon's far side, out of the ray's 3 m). `RailController.CoupleFresh(why)`
empties every uncoupled wagon (its riders set down beside the lane quietly), couples W1..Wn in number order and prints
`[rails] train: W1..W{n} coupled at stop {s} ({why})`: at the build ("the build"), at every `Restore` ("restored whole") and when a spare
takes over a train that lost wagons ("a spare takes over"). Nothing couples at a departure. A fall loses every wagon (uncoupled and
hidden, with its crew; see "Crevasses"); a plain wreck keeps them and their crew, and the spare that takes over pulls them on.

The cages (`Dungeon/Rails/PlatformCage.cs`). At each intermediate stop a `PlatformCage`, an iron cage (`CageHeight` 1.95 m) on a plank
floor with no wheels or track, stands on a free cell beside the platform (`PickSiding`). The cell is blocked, and nobody getting out of
the cart is set down on it. Two crew stand in it, in stop order (`PlanValidator.ValidateCrew` caps two per cage). Looking at a locked
cage offers one of three ways in. "Open the cage (the orc's keys)" works once the party carries them. "Open the cage (Grisel)" works
when she follows the party within 8 m, or sits on the cart's bench at that stop. "Unlock the cage" spends an iron key or tries a pick
(`PartyController.TryUnlock`, an objective lock). An open cage frees its two: XP and a `freed:<id>` flag each, as the walk-in jailbreak
gives. Its living, up crew get out at its door at once (`PlatformCage.DoorSpot`; `[CrewRescue] {name} gets out of cage {s} at its door`)
and follow the party. One who went down inside the cage stays there until revived (the open cage offers "Revive {name}"), then gets
out the same way. "Let them out" is gone: an opened cage always lets its crew out.

Running to the seats (`CrewRescueController.TickBoarding`, server and solo, every tick). While the train is held at a stop that is not
the last and not halted (`TrainRules.BoardsHere`), every freed crew member who is up, on foot and within `TrainRules.BoardReach` (25 m)
of its seat's spot (beside its wagon on the lane) books the first seat in `TrainRules.SeatOrder` that nobody sits in or has booked:
W1 seat 4, W1 seat 5, W2 seat 4, and so on. So stop 2's crew sit in W1 when stop 1's were left. It walks there (`MoveTarget`; a pocket's
walk or a test's comes first) and sits in at `HopReach` (3 m). The prints are `[CrewRescue] {name} books W{k} seat {n} at stop {s}
({d} m)`, `… sits in W{k} seat {n} after {t} s`, `… sat in at once at 8 s ({d} m)` and `…'s booking of W{k} seat {n} ends ({why})`.
- **The countdown's wait.** While a crew member booked at this stop is on the way (`CrewRescueController.OnTheWay`), the departure that
  falls due is held at 0 left (`RailController.CrewBoardingHolds`, `TrainRules.HoldsDeparture`, passed as `Boarding.Tick`'s hold), up to
  `TrainRules.BoardMax` (8 s from the countdown's start, `Boarding.Elapsed`, which counts the hold).
- **At `BoardMax`** anyone still booked within 25 m sits in at once (`TrainRules.SeatsAtOnce`), as companions hop in; anyone farther
  stays on foot.
- **A booking ends** when the train leaves, wrecks or halts, at the end of the line, when the walker goes down or is more than 25 m
  off, or when its seat is taken or its wagon gone.
- **Crew on foot board again.** This reverses plan 7's rule: crew on foot who follow the party board the held train the same way at
  every stop short of the last. Grisel, an escort, never takes a wagon seat; she keeps the cart's bench.
- **Measured** (`--test-crew`, where the cages stand beside the train): each stop's crew sat 0.4–1.4 s after their cage opened, inside
  `Boarding.MinWait` (2 s), and the departure came 0.61–1.57 s after the last of them sat (Task 2's table; the sweep at plan 14 Task 8
  read the same). In `--test-chase` stop 3's walk to W4 held the departure to 2.4 s, so the wait does hold. No flow reaches the 8 s
  sit-at-once (see "Known gaps").

A follower whose lead rides the train (any car of it: `RailController.CarOf`) never regroups into a tub or onto the line: stuck, it holds
where it is, and only when it is more than 30 m behind (`Npc.SeatedLeadCatchUp`) is it set down beside the cart, as a rider steps out at
a stop. No flow exercises that set-down (`--test-crew`'s straggler walked back by its own path).

The cage hold (plan 7). The cart does not count down at a stop whose cage is still locked with living crew
(`RailController.CageHeld`, from `CageHoldRules.Held`: Held at an intermediate stop, not halted on the line, nobody has left
that stop's prisoners, and the stop's cage holds). The crew controller answers `RailController.CageHolds` for each stop: a
locked cage holds the cart while any of its crew lives (`CageHoldRules.Holds`). A downed crew member is living and still
holds it; once all of them are dead the cage holds nothing. The HUD says "Cart — cage locked", but a gate still down is
shown first ("Cart — gate down"); the hold does not depend on the gate. A countdown that would start there raises a
`CartEventKind.CageHeld` event instead (not on the wire), and the log says "The cage here is still locked." once for that
stop. A seated player who is on their feet is offered "Get out (hold to leave the prisoners behind)" (`CageHoldRules.SeatedPrompt`).
A tap of E still gets out; holding E for 1 s (`InteractHold.HoldSeconds`), or the VR use grip, leaves the prisoners behind
(`MineCart.HoldInteract`, `IHoldInteractable`). The tap and the held use both act on the cart looked at when E went down
(`PartyController`'s kept target), and only while it is still in reach (`Adventurer.InHoldReach`, the server's own check);
a release at or past 1 s, even before the frame's tick saw the mark, is the held use (`InteractRelease.Hold`), never a tap. The log says "<name> leaves the prisoners behind." The countdown the cage
held back then starts with its own call: the arrival's starts uncalled, a boarding's or a gate's lift calls all aboard.
Left while the gate is still down, the gate's lift starts it as usual. A cage opened while the riders sit waiting (Grisel
from the bench, a pick, the keys), or whose last crew die, ends the hold, and the countdown starts by itself. The hold
applies in every mode: solo, and online through the server's state (see "Riding together"). Leaving the prisoners is
not saved: `Restore` clears `RailController.CrewLeftAt` and refills it with every stop before the one the cart is restored at
(`CageHoldRules.LeftOnLoad`), since a cart restored past a stop left it. Only the cage at the stop the cart is restored at
holds the cart again after a load; a cage still locked at a stop the cart is past stays left behind (an open one ignores it).

A cage left behind. Its crew count as lost (`CrewRules.JudgeCaged`), and the cage no longer blocks the stairs
(`CrewRules.CagesBlocking` counts only the cages neither open nor left behind). Opening it later on foot frees its crew
as any cage does: they get out at its door and follow on foot. The save still lists only the dead as lost, but a load
counts every passed stop as left (above), so a left cage behind the cart stays left and its crew stay lost; only a cage at
the stop the cart is restored at holds again, with its crew caged. The crew line's "freed" (`CrewRules.HudText`) counts only crew whose
cage stands open (or who had none), so a left cage's crew, and crew who died still caged, are lost but never freed.

The crew. Floor 12's eight are Hob Tallow and Merrin Ash (cage 1), Dace Wicker and Old Pell (cage 2), Juno Brack and Selby Crane
(cage 3), and Hal Sumpter and Bet Wynn (cage 4). A plan with no `Crew` falls back to the floor's stock prisoners. Crew who cannot fit
in a cage wait freed at the landing. A crew member (`Npc.Crew`) who is hurt to nothing
goes down and bleeds out after 60 s (`CrewRules.BleedOutSeconds`), and the label counts the seconds left. "Revive <name>",
on the NPC, on the cage they stand in or on the wagon they sit in, gets them up at 35% HP, but not with a hostile within 6 m. One who bleeds out is
gone ("<name> is gone.") and counts as lost. The same "Revive <name>" now works for any downed escorting Quest NPC on
every floor (`Npc.CanBeRevived`), which closes the old gap where an escort put down by wounds could never be got up.

Grisel's two branches hang on the floor 9 flag `grisel_forgiven`; condemned and unset are the same. Forgiven, Grisel
(`npc_grisel`) waits at the landing with her escort quest `q_grisel_out`. Once she follows, she takes the cart's rear
bench (seats 4 and 5, Grisel first) whenever a player boards within 25 m of her. She opens every cage for free at its stop.
The quest completes when she reaches the down stairs. Condemned, she lies dead on a cell beside cage 1 with no dialogue,
and an `OrcKeyring` hangs at her belt. "Take the orc's keys" sets `OrcKeys`, and every cage then opens free. Her body is
built again with the floor every time, and the keyring is hidden once the save says the keys are taken.

Spare carts. A spare cart, for looks only, stands beside each intermediate stop's platform. When the party's cart is
wrecked, the nearest stop (along the line, ahead or behind) with an unused spare provides a new one. The spare vanishes,
and the party's cart stands Held there with full plating, its wagons still coupled behind it (after a fall that took them, four fresh
empty ones: `CoupleFresh`, so later prisoners always have seats while the party has a cart). The log says "A spare cart waits at stop
N." With every spare used, a wreck leaves the rest on foot ("No carts left. The rest is on foot.", a `CartEventKind.OnFoot` event). The
crew seated in the train then get out and follow (`LetOutOfTrain`), each on a free cell of its own round its wagon (plan 14's final review M3;
`--test-boarders=lone` lets W4's two out by the test's hook and finds them on two cells, neither a rail). A load after that brings the cart back whole at its last stop with
W1..W4 coupled fresh; the crew on foot board it again at the next hold within 25 m, as any crew on foot.

The stairs. On a railed floor the sealed down stairs say "<villain> has sealed the stairs. Bring Grisel's crew home."
(`ObjectiveText.CrewLockReason`) in place of the walk-in jail's line.

The win. A crew member is home once they stand in the exit room (`Stops[^1].Room`) on foot, or sit in a coupled wagon while
the cart is held at the end of the line. Home is kept unless they go down outside the exit room. The floor completes when
every cage is open or left behind and every crew member is home or lost. The lost never block it, and a left cage's crew are lost. The floor's reward vault, if it has one, opens, the flag
`jailbreak_freed_all` is set, and the log says "The crew are home. The stairs grind open." If no stop had room for a
cage, or cages stand but there is nobody to bring home and every cage is open, the trial is waived and the stairs open
on the next tick.

**On an island floor** (plan 15 Task 7, Rulings 32–37; `CrewRules`' island rules, `CrewRescueController.Island.cs`). Floor 12's exit room
and the room beside it are the island; nobody walks in or out, and the train, the lift or a wreck past the bridge's start carries them.
Once a tick (and at each `Evaluate`) `ReadIsland` counts the players by `CrewRules.PlayerCount`: a human's hero that is not parked or
fallen is **up** unless downed or Mangled, and **outside** when it stands off the island's cells, down or not (a downed or Mangled one can
be revived or pulled free where it lies; a fallen one, a parked one or a companion counts as neither).
- **Home** (`CrewRules.JudgeOnIsland`, after the caged and the dead): a crew member already home stays home while up or inside; else one down
  is Downed; else one on foot on any island cell, or seated in a coupled party wagon while the train is held at the end of the line, is
  Home; else Lost while stranded; else Freed. A crew member in a lift tub is never home there.
- **Stranded** (`CrewRules.Stranded`, Ruling 34): a crew member on foot outside the island, with the train at the end of the line or gone
  (wrecked and hidden), at least one human up and no player outside to fetch it, counts as Lost while that lasts; a player stepping back
  out makes it Freed again. The win, once it fires, stands (`Tick` returns before `Evaluate`).
- **Unopened cages** (`CrewRules.UnopenedLeftBehind`, Ruling 33, `ResolveUnopened`): with at least one human up, no player outside and the
  train held at the last stop or wrecked inside, every cage still locked is left behind (`RailController.LeaveCrewBehindAt`), once each:
  `[CrewRescue] island: cage at stop {s} left behind (never opened)`. Its crew are lost; the cages no longer block the win.
- **Grisel brought in** (`CrewRules.BringEscortIn`, Ruling 36, `BringEscortsIn`): an escort alive, escorting and on foot outside, up or
  downed (`CrewRules.EscortLeftOutside`; spec B.8: never stranded, and she does not bleed out), with
  at least one human up, no player outside and the train at the end or wrecked inside, is set down inside (`StandBesideLane(…, inside:
  true)`) beside the train's last coupled body, or after a wreck inside beside the last stop's platform, on the island's set-down spot
  nearest it (never a barrel's or a rail cell): "Grisel picks her way in over the bridge behind you." and `[CrewRescue] island: {name}
  brought in beside the held train at {cell}`. Downed, she is brought in downed and left down for a hero inside to revive ("Grisel is carried
  in over the bridge, still down.", `GriselCarriedInLine`, subtitle only; `… brought in downed beside …`). She is brought in whether the
  floor is won or not, once each time the condition starts.
- **The follower's catch-up never crosses the edge** (Ruling 35, `CrewRules.CatchUpAllowed`, `Npc`'s follow block). An escorting follower's
  set-down beside its lead (or beside a seated lead's cart, `StandBesideCart`, with the follower's own side passed explicitly: a bridge cell
  counts as outside) is allowed only with the follower, its lead and the spot on one side. The catch-up fires once `PathMover.Unreachable`
  passes 5 (`Npc.cs`, `_mover.Unreachable > 5f`): it grows by 0.5 per failed path search, and with no path the search reruns every physics
  frame (`PathMover.cs`: `_path == null || …`), so it fires after 11 failed searches, about **0.18 s** at the 60 Hz physics rate
  (`project.godot`'s `physics_ticks_per_second=60`), not the 5 s the plan's ruling named. Refused, the follower is **held**: it prints
  `[npc] {name}: catch-up held (the lead is across the island's edge)` once per hold and **holds where it is**, walking nowhere; its path
  search runs only to tell when the hold may end (a way found across), at most every 0.5 s, the first at once, and its side gate is read
  every frame (back on its lead's side, the hold is over). A hold that starts on a rail cell steps one cell off first
  (`ExitBridgeRules.HoldStepOff`: the nearest cell within 4 off the rails, on its own side, that takes a body, reached by 4-neighbour steps
  over cells with no door, prop, barrel or other body: `[npc] {name}: held on the line at {cell}: steps off to {cell}`), so a held body
  never stands where the lift stands again or a cart runs.
- **A Mangled body on an island floor** (co-op; the server's 30 s valve, `ServerWorld.TickBound`): `MangledRules.BoundCell` walks the
  critical path from the up stairs to the down stairs, and on an island floor that walk crosses the lava, so a body outside is never bound
  ahead (`MangledRules.IslandBind`). Once its valve has fired, while a human is up outside it stays where it fell, freeable there by them
  (`[net-server] {name} lies crushed outside the island: a friend up outside can free them there`); with none (the last of them rode the
  lift in, judged again every tick) it is bound, still Mangled, on one of the island's set-down spots no other body stands on, for anyone
  inside to free (`{name} is bound in the dark ahead.`, `[net-server] {name}, crushed outside with nobody up outside, is bound inside the
  island at {cell}`). Then it no longer holds the outside (cages, crew on foot, Grisel). A body inside, and every other floor, go on as
  before. A client's banner (`MangledRules.Banner`) reads "Crushed. A friend out here can free you" while its body lies outside after the
  valve, and the usual "Bound in the dark ahead; a friend can free you" once it is inside. In solo there is no valve: possession passes to
  the next member up, and the crushed body is a companion's, which the island's rules count as no player (it holds nothing); it lies where
  it fell until someone frees it.
- **The arrival snapshot after an inside wreck** (`RailArrival.TakesSnapshot`): a wreck past the bridge's start takes the snapshot too, after
  `WreckInside` set the last stop, so a wipe after it rises at stop 5, inside (`[rails] arrival snapshot at stop 5: …`). `RailArrival.Merge`
  keeps the reward chest when the live state is completed or the arrival had it (`o.Completed || a.RewardChest == true`), so a won and
  opened chest survives a wipe, opened, and no second chest stands.
- **The reward chest** (Ruling 37). On an island floor the win stands a strongbox in the exit room in place of the vault's line: "The
  prisoners point you to a strongbox by the stairs: the Founders' share is yours." `StandRewardChest` puts `Chest.tscn` (`SpawnIndex`
  `CrewRules.RewardChestIndex`, 9000, the vault's table, not a mimic, not locked, named `RewardChest`) on the first free exit cell nearest the
  stairs other than the flight's foot (`[CrewRescue] the reward chest stands in room 9 at (45,54) (table …)`, 4.0 m from the stairs at
  (46,54)); a body on its cell steps off by `Shove` (`ClearChestCell`, a remote hero's client told), and the cell itself is body-blind so
  every peer builds it on the same cell. It goes to clients as `PropEvt(chest:9000, "stood")` (and again in the catch-up), is saved as
  `ObjectiveStateDto.RewardChest`, and a load stands it again, opened when `OpenedChests` lists it. An unopened reward chest is not in the
  prop hash; a client missing one is mended by the objective broadcast (every 2 s: `ApplyNet` stands it when `RewardChest` is true, forward
  only and idempotent). Protocol stays 17.
- `--test-island=onfoot|stranded|grisel|chest` and `=wreck` pin each of these (see `--test-island` at the end of "The five rides").

The save. `FloorStateDto.SparesUsed` lists the stops whose spare is spent. `ObjectiveStateDto` adds `GatesUp`, `OpenCages` (stops),
`HomeCrew`, `LostCrew`, `OrcKeys` and, since plan 14, `AboardCrew` and `LostWagons`; every one is nullable, so old saves load.
- **`AboardCrew`** (`CrewSeatDto`: id, wagon number, seat) lists the crew seated in the train, by wagon then seat. A load seats each one
  there again (`TrainRules.Reseat`: the saved seat if it is free, else the first free seat in `SeatOrder`; the same id listed twice gets
  its first): `[CrewRescue] load: {name} back in W{k} seat {n}`, or `… (seat {m} of W{w} taken: first empty)`.
- **`LostWagons`** lists the wagons a fall took, and holds only between that fall and the next restore. It is a record only: every
  load restores the cart whole (`Restore`), and a cart restored whole couples W1..W4 fresh, so no load honours it (`[rails] train: the save
  had wagons [..] lost; the cart is restored whole, W1..Wn couple fresh`). The crew who fell stay lost through `LostCrew`.
- **`CoupledCages` and `LostCages`** are retired: never written, ignored on load. Floor-12 dev saves from before plan 14 are not supported.
- `RailArrival.Merge` takes `OpenCages`, `HomeCrew`, `LostCrew`, `FreedNpcs`, `LostWagons`, `AboardCrew` and `Completed` from the arrival
  snapshot, so a wipe's rise brings the train back as it arrived.

A load puts the cart Held at the stop it last reached with W1..W4 coupled fresh, seats the caged crew in their cages and `AboardCrew` in
the train, leaves crew who were on foot where the save had them (one downed on foot stays lying there; only one downed inside its open
cage stays in it, `CrewRules.StaysInOpenCage`), and sets the home crew in the exit room. Lifted gates come back up with no animation or
log line. The heroes are not seated again (a load puts nobody aboard). On a co-op client (a join or a revisit) the objective's restore only reads the state for the
HUD, as the 2 s objective sync does, and the cart's restore does nothing: seats, the train and the doors are the server's.

`--test-crew[=forgiven|condemned]` (condemned when no branch is given; `DebugTools.Crew.cs`, quits after 570 s, about 430 s a run)
plays the rescue on the built-in floor 12 at lethality 3 with no AI and prints `[crew]` lines. It is written for the train from the
landing (plan 14).
- **The floor check:** six stops, a cage at each intermediate stop (4), two caged crew per cage (8), W1..W4 coupled and empty at stop 0,
  and a gate down at every intermediate stop whose room a Guard or Defend group of the floor's contents holds (4: the Oddfellows hold
  stop 2's). Every count reads `LineShape`, so a line of another stop count needs no edit.
- **Each stop:** it boards at the landing, and at each stop it checks that the gate is down, kills the stop's guards by hand and waits
  for the gate to lift. Each cage must open free (an iron key in the bag stays unspent, and every pick is forced to fail). Condemned, the
  party takes the orc's keys from Grisel's body. Forgiven, Grisel must be on the bench (or following within reach) at every cage, and
  each cage must offer "Open the cage (Grisel)". Her escort quest is accepted by raising its accept event (`QuestAccepted`), so her
  dialogue's accept button is not covered. The opened cage's crew get out at its door and walk to the first empty seats in
  `TrainRules.SeatOrder` (`[crew] 3 cage {s}'s crew run to the first empty seats [...]`): each must sit in before the departure, which
  comes after the last of them sits and at most `BoardMax` + 0.5 s after the countdown started (`[crew] stop {s}: crew [...] seated {t} s
  after the cage opened`, `… the countdown ran {c} s …; the departure came {d} s after the last of them sat in`).
- **Ride 1:** `r1_beam` clears every seated head in the train (Grisel's on the bench when forgiven) by 0.05 m or more. The wagons are
  empty on ride 1 here (stop 1's cage opens at stop 1), so the default run measures the heroes only (`[crew] 1 beam: … by 0.25 m`, 56
  samples) and `=forgiven` Grisel on the cart's bench (`… by 0.14 m`, 14 of 68 samples hers).
- **The save between stops 2 and 3:** the train, the crew, `OrcKeys` and the gates come back, and every `AboardCrew` entry is back seat
  for seat (`[crew] load: AboardCrew [...] back seat for seat`). After the load Grisel must not stand on a rail cell.
- **Ride 3:** the seated lead throws the `switch_out` lever from the seat as the seated window offers it, out of the look ray's reach and
  through `PartyController.VrUse` (the dispatch E uses): the prompt must be "Throw the switch", the lever thrown and the lead still
  seated (`[crew] ride 3: the seated lead throws …`), and the lever must never pass its points straight.
- **A crew member lost:** one taken out of W2 goes down; a save and a load find it lying on foot where it lay, not back in its cage
  (`[crew] 6 after the load … 0.00 m from where the save had it`); it bleeds out (the clock wound on through `Npc.DebugBleed`) and is lost.
- **The straggler:** at stop 3 one freed crew member is held 30 m off while the countdown runs. Its booking ends (`(31.2 m off)`), it is
  on foot and following at the departure, it is held there through ride 4 (the collapsing bridge is no place to walk) and let go at stop
  4's arrival, where it walks back to the train, books W3 seat 4 at 24.9 m and boards (`[crew] straggler: … boards at stop 4 …`; it walked
  back by its own path, so `SeatedLeadCatchUp`'s set-down is not exercised). Stop 4's last guard is frozen alive meanwhile, so the gate
  stays down.
- **The end of the line:** every stop from 3 to the last crew stop is ridden to the same way, and the end of the line must bring the
  other seven home and unlock the stairs; forgiven, Grisel walks with the party to the stairs and `q_grisel_out` must complete.
- **The rebuilt floor:** the cart rides into stop 1 with cage 1 locked. It must be held there (`CageHeld`) a countdown and two seconds
  later with no countdown run, the bar "Cart — cage locked", the seat's prompt `CageHoldRules.SeatedPrompt` with the hold offered, and
  "The cage here is still locked." heard once. The lead then leaves the prisoners behind (`[crew] 8 held at stop 1 by its locked cage`).
  The cart must leave within 6 s with cage 1 still locked and W1..W4 empty, `CrewLeftAt` listing stop 1, its two crew lost, the crew
  line reading "Crew freed 0/8, home 0, 2 lost." and the floor not complete (`[crew] 8 departed stop 1 without its prisoners`). (The
  other solo ride flows, `--test-ride`, `--test-ride-edges`, `--test-chase` and `--test-cart-watchdog`, leave each held stop's prisoners
  the frame the hold shows, through `DebugTools.RideWaiveCages`.) Then a cart on thin plating hits a cinder brute between stops 1 and 2.
  One spare must take over, holding the cart at a stop (stop 1, whose prisoners were left, must not hold it). The brute dies and the cart
  rides on to stop 2, where cage 2 holds it, and the run saves and loads there. After the load `CrewLeftAt` must list stop 1 alone, cage
  1's crew must still be lost, three cages (2 to 4) must block the stairs (`CrewRescueController.CagesBlockingNow`), the crew line must
  still read "Crew freed 0/8, home 0, 2 lost." and cage 2 must hold the cart again (`[crew] 9 the load kept stop 1's prisoners left
  behind`).
- **The ray through a wagon** (`[crew] look: …`): aimed through a coupled wagon (its prompt empty), the real interact ray must offer what
  lies past it: a cage when one is within the ray's reach past a wagon, else the cart (on floor 12 a cage stands 4.18 m past the nearest
  wagon's far side, out of the 3 m ray, so the flow aims at the cart through W1).
- **W1 taken by the next stop's crew:** stop 2's cage opens by the test's hook and its crew take W1's seats 4 and 5, since stop 1's
  were left (`[crew] 10 stop 2's crew took W1 (stop 1's were left): […] 0.4 s after the cage opened`).

The run ends with `[crew] RESULT ok` or a FAIL line and exit code 1. Every enemy but the stop guards (and the test's brute) is culled
as it appears. A run with no RESULT line 10 s before its quit timeout fails with "timed out at step N" and exit code 1.
`--test-ride` and `--test-ride-edges` share this watchdog.

### The relief cart (single player)

Solo, a player left behind by the train gets a relief cart (plan 7; `Rails/Core/ReliefRules.cs`, `RailController.Relief.cs`).
It is the co-op chaser (see "The left-behind chaser" under "Riding together") sent to them.

**Left behind** (`ReliefRules.LeftBehind`). The local player is up (not downed, not parked), seated in no cart, and the train's
last body's back is more than 12 m (`LeftBehindRange`) ahead of them along the line (`ReliefRules.Behind`; their S is the
line's closest point, `TrackSampler.ClosestS`). The line must still be live (`LineLive`): two stops or more, the party cart
neither wrecked nor hidden, and not Held at the last stop. A standing cart that has *met* the player waits for them, so no
relief comes (`ReliefRules.Met`): the cart has met them when, since it stopped, they sat in it or stood within 12 m of its
train's back. Met is cleared while the cart rolls and starts false on a build and a load. So a player who jumps out gets a
relief (the cart rolls on and stands at the next stop without them), and one who gets out at a stop and walks back from the
cart does not. None comes while the player is ahead of the train, online (`!NetSession.IsNetworked`), or with
`DebugTools.NoRelief`. Nothing in a wreck touches met: a spare that takes over at a stop more than 12 m ahead of the set-down
player leaves them left behind, and a relief comes.

**Timings** (`ReliefClock`). The first relief comes 15 s (`FirstWait`) after the player is left behind; the clock starts
over whenever they are not. It waits beside them 30 s (`WaitBeside`), then rolls on empty after the train and is hidden once
it stops; the next comes 15 s (`NextWait`) after it set off. The clock does not count while a relief is out (it counts again
once the last one rolls on or has ended). One relief at a time; each is named `ReliefCart{n}`.

**Where it comes from and where it stops.** It is placed Held at the last stop at or behind the player (`SpawnStop`), with its
front at least 1 m short of their body (`SpawnS`), never past the chase limit (behind the train) and never over a hole
(`SpareRules.HoleLimit`). It comes down the line to its seat (`SeatS`): beside the player; on the rails, its back 1.5 m
(`PastRunOver`) past them, having run them over; or, when they stand within 6 m (`WaitAtStopRange`) of where it was placed,
it waits there. The seat only moves forward (`Target`): a player walking back never pulls it back. A seat over a gap or a
trestle span stops short of the near lip (`SeatLimit`).

**It is the chaser.** It is a `MineCart.AsChaser()` with the chaser's run: its limit behind the train
(`SpareRules.ChaseLimit`), hops over the line's holes, the halt at a live heap's face, the run-over sweep (its source is
"the spare cart"), the bell and the lane warning, the 5 s countdown with "Get out" closed for its first 2 s, and the bar,
which reads "Relief cart — departs in N", "Relief cart — after the train" or "Relief cart — behind the train"
(`RailHudState.ChaserRelief`). It is solid and usable while it stands. "Board the cart" seats the player, their companions on
foot within 25 m and the escorts on its bench, and starts its countdown; it then runs up behind the train and sets everyone
down there. Getting out while it stands sets the player and their companions down; with no player left in it, it waits again
and sets every other rider down. Its countdown ticks, and it rolls, only with a player in it (`ReliefRules.CountsDown`,
`Rolls`), so a get-out in the countdown's last tick leaves it standing too. The phases are `ReliefPhase` Coming, Waiting, Riding and Leaving (`ReliefRules.Next`, `Rolls`,
`Prompt`, `CountsDown`).

**Log lines.** "A relief cart comes down the line.", "The relief cart waits for you.", "{name} climbs into the relief cart.",
"The relief cart pulls up behind the train." and "The relief cart rolls on without you."; on stdout `[rails] relief {n}: sent
from stop …`, `[rails] the relief cart waits at S=…`, `[rails] the relief cart rolls on without the player …` and
`[rails] the relief cart pulls up behind the train …`. At a live parked train's place (plan 15, Ruling 29: the train held across the lava)
the log says "The relief cart halts short of the exit bridge." and stdout `[rails] the relief cart halts short of the exit bridge at S=…, … m
behind the parked train's place` (`SpareRules.PullUpLine`; not run end to end, Ruling 46). No line is voiced.

**Not saved, no spare, solo only.** A relief spends and gives back no spare, and is never sent to a client
(`CollectChaser` skips it). A load ends it and resets the clock (`RailController.Restore`); a player still left behind gets a
new one 15 s later. At the default lethality 3 a relief rolling over a player on the rails mangles them as any cart does; the
next relief comes only after they are back up.

`--test-relief[=beside|rails|refuse|ahead]` (beside by default) runs floor 12 solo at lethality 1 with no enemies and prints
`[relief]` lines. beside, rails and refuse board at the landing and jump from the cart 25 m on at 4 m/s or more; the player
stands beside the line (on it for rails), must be left behind within 6 s, and a relief must come from the landing 15 ± 0.5 s
later. beside: it waits beside them in reach with "Board the cart" and nobody hurt, is boarded ("Relief cart — departs in"),
pulls up within 3 m of the train held at stop 1 and sets the player down off the rails, is gone within 3 s, and no second
comes. rails: it runs the player over once (the lethality-1 share of max HP, not downed) and waits with its back past them,
then is boarded as beside. refuse: it rolls on 30 ± 0.5 s after it began to wait, a second comes 15 ± 0.5 s later, and a
save and a load while that one waits leave no relief node, nobody seated, the count at 0 and the cart held at a stop, and a
relief comes 15 ± 0.5 s after the load. ahead: the player rides to stop 1, gets out, stands 30 m ahead of the train for 20 s
and 30 m behind the cart for 20 s, and no relief comes. Every run also checks that no two relief carts ever stand at once.

### The five rides

Ride k is the stretch from stop k−1 to stop k. What happens on it is plan data: `FloorPlan.RailEncounters`
(`RailEncounterPlan`: Id, Ride, Kind, Riders, At, Offset, Line) and `FloorPlan.RailSetPieces` (`RailSetPiecePlan`: Id,
Ride, Kind, At). Both are `[AiIgnore]` and hand-authored, like `Crew`, and reach the floor through `PlanResolver` as
`FloorContents.RailEncounters` and `FloorContents.RailSetPieces`. `At` is the fraction of the ride where an item stands or
starts. `Offset` is a slag cart's place in metres ahead of (+) or behind (−) the party's cart. `Line` is a narration line
(subtitles only) shown the first time the item appears. Encounter kinds are `slag_cart`, `lone`, `thrower` and `golem`. Set-piece
kinds are `beam`, `switch_out`, `switch_back`, `heap`, `buckets`, `hammers`, `pour`, `trestle`, `jump`, `spur_switch`,
`buffer`, `barricade` (timber across the party line), `slag_barricade` (the same across the slag line, beside the piece's party S),
`dark`, `runaway` and `split` (plan 12), `bridge` and `leap` (plan 13, ride 4's cavern), and `pit` and `parked` (plan 15, ride 5's lava pit
and the parked train). `PlanValidator.ValidateRailPlans` drops both lists on a floor with no rails. It also drops an item with no id or a
duplicate id, an unknown kind, a ride outside 1..stops−1, no riders, an unknown enemy id or any boss rider ("no bosses").
It clamps `At` to 0.05..0.95, trims a slag cart to 3 riders, and caps the lists at `RailEncounterCap(rides)` = max(12, 3 × rides)
encounters and `RailSetPieceCap(rides)` = max(20, 5 × rides) set pieces: 15 and 25 on floor 12's five rides, which has 12 encounters
(of 15; plan 14 added the three hound groups) and 23 set pieces (of 25; plan 15 turned `r5_jump` into `r5_pit`, added
`r5_switch_out` and `r5_switch_back` and put `r5_parked` in `r5_barricade`'s place).

`RailEncounterController` (`Dungeon/Rails/RailEncounterController*.cs`) is a node under the floor root. It is made after
the objective only when the floor has a `RailController` and either list, so every other floor is untouched. It is server
only: a co-op client gets the enemies through the usual snapshots and builds the set pieces as dressing (see "Riding together"). The party
cart's departure from stop k−1 arms ride k. Lone standers, throwers and the golem appear at the departure, so they are
already there to be seen. Every other item starts as the cart passes its `At`. The cart arriving, being wrecked or restored,
or the party going on foot clears the ride's live items: slag carts and their riders go silently. A ride armed again (after
a wreck, or a load) runs again from its start. Every enemy a ride spawns is a reinforcement (no loot farm), except a
thrower the first time it stands (it stays after the cart passes and pays loot and XP like any enemy; one sent again after
it died, or after a load, is a reinforcement). None may be a boss: a boss dying on floor 12 would open the stairs through `Main.OnBossDefeated` and skip the crew. Every hazard warns
first, by light and a sound cue: one on a timer at least 1.5 s before it can hurt anyone, a cart by its bell at 3 s of
look-ahead. The cues (`cart_bell`, `switch_clunk`,
`cart_crash`, `crate_break`, `bucket_creak`, `hammer_wind`, `pour_hiss`, `golem_windup`, `golem_slam`, `trestle_creak`, `timber_collapse`,
`cart_jump`, `cart_land`) are placeholder names and stay silent until plan 6. The numbers are in
`Dungeon/Rails/Core/SetPieceRules.cs` (`PaceRules`, `SetPieceRules`) and the controller's constants.

Floor 12's five rides (`data/adventures/builtin/floor_12.json`; the lengths are the shipped seed's, `RailCount` 4 on a loop):

| Ride | Stops | Encounters | Set pieces | Slots left for later plans |
|---|---|---|---|---|
| 1 The Ore Run (≈267 m) | landing → S1 | `r1_lone` a lone cinder on the rails (0.45), `r1_thrower` a forge priest thrower in a pocket (0.8), `r1_hounds` an ember hound on a slag cart (0.3331, −8: S 105.0, plan 14) | `r1_barricade` (0.25, S 82.8), `r1_beam` the low beam (0.6) | — |
| 2 Side by Side (≈206 m) | S1 → S2 | `r2_slag1..3`: 2 cinders (0.15, −6), a priest and a hound (0.45, −4), a hound and 2 cinders (0.75, −3) | `r2_slag_barricade` on the slag line (0.3, ahead of `r2_slag1`), `r2_barricade` (0.7, S 427.1, defended by `r2_slag2` alongside) | — |
| 3 The Switchyard (≈281 m) | S2 → S3 | `r3_hounds` an ember hound on a slag cart (0.05, −5: S 502.9, plan 14), `r3_slag` a cinder brute (0.3, +8) | `r3_switch_out` (0.2), `r3_heap` (0.35), `r3_switch_back` (0.731, live: its points at S 694.4, 20.2 m before the gap), `r3_buckets` (0.832, past the gap); the broken bridge on its slag line (the gap at party S 714.7, plan 11) | — |
| 4 The Dark and the Throat (≈252 m) | S3 → S4 | `r4_hounds` 2 ember hounds (0.262, −4: S 836.2, the dark's middle), `r4_slag` a cinder and a priest (0.3086, 0: party S 848.0, spawned out of sight 40 m ahead round the hairpin; the leap's slag cart) | `r4_beam` (0.15); `r4_dark` (0.093, S 793.6–878.6), `r4_runaway` (0.172, S 813.5), `r4_split` (0.371, its points at S 863.6, plan 12); `r4_bridge` the collapsing bridge (0.5268, S 903.1: the cavern's party deck, S 903.1–991.1, 22 cells) and `r4_leap` the leap and the slag line's drop (0.5664, S 913.0: the stretch to S 975.1, the drop at enemy S 999.1–1007.1, plan 13) | — |
| 5 The Pour (≈209 m) | S4 → exit | `r5_hounds` two ember hounds on a slag cart (0.3561, −8: S 1097.0, plan 15), `r5_golem` the forge golem (0.49, S 1125.0) | `r5_switch_out` the pit's deadly switch (0.2075, its points S 1065.9, its lever S 1060.9), `r5_pit` the lava pit (0.2457, its lip S 1073.9, its far edge 1077.9), `r5_switch_back` its fixed back (0.2696, its points S 1078.9), `r5_pour` (0.4, S 1106.2), `r5_trestle` (0.565, S 1140.7), `r5_hammers` (0.6429, S 1154.0/1157.0/1160.0), `r5_parked` the parked train (0.7084, its rear S 1170.7, its front 1183.3), `r5_spur` (0.85, its points S 1200.3), `r5_buffer` (0.95, S 1221.3); the trestle stands over the gorge (plan 10), the exit bridge (S 1184.3–1200.3) over the lava into the island (plan 15) | — |

The S values here are `--track-depth`'s `piece` and `encounter` lines (the encounter lines since plan 13; measured at plan 13's Task 5,
`.superpowers/sdd/2026-11-22-mine-cart-13-bridge-and-leap/t5-depth.txt`, with `r4_slag` moved by Task 6 from S 900.0 to 848.0, and
re-run at Task 7), and before them the exposed-track fix's re-measure (each of floor 12's two ramps, on rides 3
and 5, lengthens its lane by about 0.64 m, so every S past ride 3's ramp moved up by about 0.6 m and past ride 5's by about 1.3 m,
and a piece on a ramp's ride by its fraction of it; no `At` changed and every placement rule still holds). No S moved in plan 14:
it changed no generator code, `--rehash-plans` left floor 12's `LayoutHash` (16995108853750381062) alone, and its static gate (plan 14
Task 6, `--print-map --track-depth`) read every piece and `StopS` as before, within 0.1 m; the three new encounters read S 105.0, 502.9
and 1080.0 (`r5_hounds` moved there from the planned 1100.0 by Task 6's fix round, below). The barricades' `At` are the measured ones: `r2_barricade` went from the plan's 0.6 to 0.7 and `r5_barricade` from 0.9 to 0.88,
because at the planned values each stood just past a tight bend and the 15 m sight ray from the seat hit the inner wall (0.7 is
still beside `r2_slag2`; 0.88 is still between `r5_spur` and `r5_buffer`; `r5_barricade` is gone since plan 15, `r5_parked` taking its place).

Plan 15 moved no line S. Its pit and island cut cells to chasm and lay no lane, so `StopS`, the rides and every piece and encounter S read
as before (Task 3's static gate: all 33 `piece` and `encounter` lines exact, `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`); only the
`LayoutHash` moved, to 10116286907070122254, for the cut cells. It re-placed ride 5's pieces from the measurement (`At` = (S − StopS[4]) /
(StopS[5] − StopS[4]), with StopS[4] 1022.470 and StopS[5] 1231.737): `r5_switch_out` at the pit's lip − 8.0 m (0.2075), `r5_pit` at the
lip (0.2457, `[rails] pit r5_pit: At S=1073.9 (0.0 m from the lip)`), `r5_switch_back` at the far edge + 1.0 m (0.2696), `r5_hammers`
from 0.2 to 0.6429 (S 1064.3 to 1157.0: the pit's stretch needs the room, so they stand after the trestle), `r5_parked` in
`r5_barricade`'s place at `ParkedRules.RearS(ExitFromS, 4)` (0.7084: 1170.715, 0.009 m from the measured 1170.724), and `r5_hounds` from
0.2749 to 0.3561 (S 1080.0 to 1097.0, below).

Ride 1. The lone cinder stands on the party lane where the cart will come, and the cart runs it over. The thrower stands in
the nearest party-side pocket and throws at the riders, with a hand glow of at least 1.5 s for its wind-up; it stays until a
load. The beam (`LowBeam`) spans the line just above the tallest seated head (0.1 m clear). It has no
collision, so the cart passes under it without a touch. Seated heroes duck (a small shake, none in VR) and a seated
companion calls "Heads!". A beam clears every seated head in the train: the cage roof is gone (plan 14), crew sit in the wagons
folded as a seated hero is (`Npc.Sitting`, `SitScale` 0.7, `HeadHeight` 1.26 m against 1.8 standing), and a wagon boarder stands at
rim height like the cart's. `--test-chase` measures `r1_beam` over crewed wagons (every head clear by 0.14 m, 28 of 84 samples crew in the wagons),
`--test-crew=forgiven` with Grisel on the cart's bench (0.14 m) and
`--test-split` `r4_beam` (0.25 m); `--test-boarders=move` rides it with the lead in W2 (0.25 m).

Slag carts (`SlagCart`; rides 2 and 4, and the hound groups of rides 1, 3 and 5). Each rolls up the enemy line from 10 m behind its
pace point at the party's speed. It then holds pace beside the party's cart: it aims at the party cart's place on the enemy lane plus
its offset, at the party's speed plus a correction of 0.8 × the gap, capped at ±3 m/s (`PaceRules`); a side-by-side cart (Offset 0) and,
since plan 14, a hounds-only cart close from behind at up to `PaceRules.AlongsideCatchUp` (6 m/s, `PaceRules.CatchUpFor`; the catch-down
stays 3 m/s). Its riders (up to 3) are carried on the heap and awake. A slag cart runs over whoever stands on the enemy lane, by the
lethality, and rings its bell first ("Cart coming!", 3 s of look-ahead at 3 m/s at least). A glowing brake shoe on the side that faces
the party is a layer-4 prop: one hit from a blade, a shove or an arrow breaks it. The cart then derails, its living riders are thrown
under it and die, and a cosmetic tub tumbles off the line.

Ember hounds aboard leap across (`RailEncounterController.Leaps.cs`).
- **The pick** (plan 14; `TrainRules.BoardingOrder`). A hound boards the party's cart or any coupled wagon, crewed or not, each with its
  rim spot: two on the cart's rim (one per awake hero seated in it, `SetPieceRules.RimSpotsOpen`), one on each wagon's. It takes the
  crewed wagons first (the one with the most crew, the nearer on a tie: a hound goes for the prisoners), then the cart, then the empty
  wagons nearest first, and of these only bodies within `TrainRules.LeapReach` (8.6 m: `LeapRange` 6 m + one car's `Spacing`) whose
  spot is within it too. A body past the reach is left out, whatever it is.
- **The crouch** (`SetPieceRules.HoundCrouches`). Only with a body it would pick within the reach, and a coupled car's body within
  `LeapRange` (6 m), does a hound crouch: 0.8 s (`LeapWindup`) with a glow and a "Hound!" call. A hound on a mixed cart (`r2_slag2`,
  `r2_slag3`) waits until its cart is alongside, as before; a hound on a hounds-only cart (every rider of its plan an ember hound:
  `r1_hounds`, `r3_hounds`, `r4_hounds`, `r5_hounds`) waits only for its cart's roll-up, `SetPieceRules.HoundRollUp` (2.5 s since the
  cart spawned), then leaps from reach, alongside or not.
- **The leap.** A 0.6 s arc onto its spot. With no spot free in reach it misses: it lands beside the nearest body, where that body will
  be after the 0.6 s, as a normal creature (the miss's flight is not capped in code: it lands on the centre of a walkable cell beside the party lane, often the far side, near where
  the body will be after 0.6 s of its own travel, so a healthy miss can read 8–12 m; what the crouch rule bounds is its take-off, a body it
  would pick within 8.6 m. The flows print a miss's length only, and gate its take-off: the hound's distance to the nearest coupled body on
  the frame before it leapt, at most `LeapReach`).
- **Aboard.** A boarder bites with its normal melee: a wagon boarder bites the crew inside (the nearest, then the next), a cart boarder
  the riders, and only a cart boarder dents a rolling cart's plating, by 3 every 2 s. It falls off when it dies, and a wagon boarder
  also when its grip is broken (below). When the cart is wrecked, live boarders drop beside the lane where it was wrecked
  (`RailController.WreckedAt`), not at the stop where a spare takes over: a wreck judged inside the encounter controller's tick is told at
  the rail controller's next flush, after the spare has moved the cart.
- **Measured** (plan 14 Task 6's final tree, `=groups` and `--test-chase` alike): every leap that landed on a body 4.4–6.4 m long (each gated at 8.6 m in both
  flows; before the cap a hound beside the cart leapt 15.1 m onto it on ride 1), `r4_hounds` boarding W4 and W3 at S 853.6, 3.32 s after
  its spawn at 836.3 (the roll-up and the crouch), 5 m before the split's seated window (858.6).

The hound groups (plan 14; Rulings 18 and 19). `r1_hounds` (one hound, its pace point S 105.0, Offset −8), `r3_hounds` (one, S 502.9, −5)
and `r5_hounds` (two, S 1097.0 since plan 15, −8) are `slag_cart` encounters of ember hounds only, appended after `r5_golem`, so that fighting along the
train runs the length of the line (floor 12 had four hounds before: one each on `r2_slag2` and `r2_slag3`, two on `r4_hounds`). They were
placed from the measured layout and gated by `--test-boarders=groups` (see "Fighting along the train" below): `r1_hounds` between
`r1_barricade` + 5 m and `r1_beam` (it leaps at S 122.3, 6.4 m onto W3, about 3.3 s after spawning beside W4), `r3_hounds` before the
switchyard (it leaps at S 521.2, 5.2–5.3 m onto W3; its cart freed short of `r3_switch_out`'s points − 2 m), and `r5_hounds` after the
pit switch and before the golem (S 1125.0). `r5_hounds` was planned at S 1100.0 (At 0.3705); there its cart never came alongside
before the golem's burst freed it, so plan 14's Task 6 gave hounds-only carts the 6 m/s catch-up and the leap from reach, and its fix round
moved the pace point to S 1080.0 (At 0.2749), where both hounds landed 4.55 s before the burst. Plan 15's pit stretch (about S 1045–1089)
took that place, and its Task 4 re-measured the group (Ruling 15): its gate (a) wants the train's rear home past `r5_switch_back` on the
spawn tick, no ram and no crossing free, its gate (b) a landing before the golem's burst. S 1096.0 failed (a) (the rear at 1084.6 against
home from 1084.9); S 1097.0 (At 0.3561) is the first S to pass it, and there the hounds land 0.60–0.68 s before the burst at
S 1125.1 in `=groups` and `--test-chase` (Task 4: 0.68 and 0.60 s in both; Task 9's sweep: `=groups` 0.68 and 0.60 s, `--test-chase` 0.68 and 0.62 s, each hound's
line `[chase] leap: r5_hounds's Dyn16 leapt W4 at party S=1117.5 …: 5.0 m (reach 8.6); lands 0.68 s before the golem's burst …; the pumped
estimate -0.40 s …`) (each metre later costs about 0.45 s: the cart rolls up from 10 m behind at 6 m/s against the
train's 5.22 m/s, and the burst is fixed). The flows gate it at `BdGolemMargin`, 0.4 s (`DebugTools.Boarders.Groups.cs`; 1.5 s before,
then 0.5 s, then 0.4 s for 0.2 s of headroom over the measured 0.60–0.68 s). Pumped, the estimate ((burst S − spawn S) ÷ 6.78 m/s, the
pumped speed with four wagons, less the measured spawn-to-landing seconds) is below zero, −0.40 to −0.48 s: a pumped party outruns them
and the burst frees their cart with the hounds aboard (an owner's feel call, under "Known gaps").

A boarding group's emptied cart goes (Ruling 19), each printed `[rails] slag cart {id} ({enc}) freed: {why}`, quietly (no derail, no
taunt):
- **The linger.** A hounds-only cart is freed `SetPieceRules.HoundCartLinger` (3 s) after its last rider left it (leapt or died), with no
  leap of its still in the air and no hero on its deck (`SetPieceRules.HoundCartGoes`; "its hounds are gone"). `r4_hounds` is included:
  its empty cart no longer paces the train through the cavern (the leap uses `r4_slag`).
- **The crossing.** Every live slag cart of the ride whose party-equivalent S is behind the party's front, with no hero on its deck, is
  freed when the front comes within `SetPieceRules.CrossFreeAhead` (5 m) of a thrown `switch_out`'s points (`CrossingFrees`; "the train
  crosses onto its lane"), so a hound cart never sits where the train crosses. Measured: `r3_hounds`' cart with its hound held aboard is
  freed with the front at S 540.2, the points at 545.1.
- **The golem.** `r5_golem`'s burst frees any other live slag cart of its ride ("the golem bursts out", with its distance to the golem's
  runner). With `r5_hounds` at S 1080.0 the linger frees its cart first (at S 1113.0), so the burst usually has nothing to free.

Fighting along the train (plan 14; `RailController.Moves.cs`, `RailEncounterController.Answers.cs` and `.Throws.cs`, the numbers in
`Rails/Core/TrainMoveRules.cs`, `BoarderRules.cs` and `ThrowClock.cs`).
- **The move** (Rulings 7–12). A seated hero who is up may climb car to car while the train rolls. It is a seat change, like the cavern's
  leap (`Adventurer.SitIn(…, keepYaw: true)`), never a get-out.
  - **The way and the target.** The way is read from the camera's flat heading against its seat's forward (the desktop's seat yaw and a
    headset's head yaw alike): more than `BackCone` (100°) off forward is back; otherwise from a wagon it is forward, and from the cart
    there is none (`TrainMoveRules.Direction`). The target is the nearest car that way with a free hero place (a wagon's seat 0, or the
    cart's first free hero seat 0–3), passing taken ones, so a companion in W2 never walls off W3 and W4 (`TrainMoveRules.Target`). One
    move is one hop, however many cars it passes.
  - **The prompt** is "Climb back to wagon N", "Climb forward to wagon N" or "Climb forward into the cart" (`TrainMoveRules.Prompt`), on the
    target car, through the seated look (`RailController.MoveOfferFor`, as the leap's offer); E and the VR grip both reach it. The look asks
    the ray first (plan 14's final review I1): what it finds within reach that offers something other than a move (the own wagon's "Revive
    {name}" or "Get out", a neighbouring car's revive) is offered instead, and the move only when it finds nothing; a target car with a
    downed crew member offers "Revive {name}" in the move's place, and its press is the revive (`IsMovePress` is false for a car whose plain
    use stands, `PlainUseStands`: its plain prompt, `PlainPromptOf`, offers the presser something and the car is within the server's 3.5 m
    reach), on the server and in solo alike. A revive is offered only when it would be taken (`Npc.ReviveTaken`): with a hound within 6 m
    of the downed crew member the wagon offers no revive and the move stands (plan 15 Task 0, N1); a target car past a taken place beyond
    the reach (W2 from the cart, 4.0–4.7 m) offers the move, not its revive (N2). A wagon press judged by the state it lands in that finds
    no move, no revive taken and no get-out (a "Revive" pressed behind the server: the crew member got up, died, was thrown or an enemy
    came near) is refused out loud (`[rails] a use of W{k} by {name} … refused`, `WagonUsesRefused`, and a downed one's "Not with enemies
    this close."; printed in solo and on test servers only), never dropped; from another car of the rolling train it is judged as the move
    (`IsMoveAttempt`), and the presser's machine starts the move's grace when it learns of it (solo in `Move`, a client at the server's seat:
    `MoveTakenFor`, after `TrainPlainPressed` at the press). A stale "Revive" is never a get-out: a wagon's own rider is neither offered nor
    given "Get out" for `TrainMoveRules.ReviveGetOutGrace` (1 s, on each machine's clock) after the wagon last offered a revive
    (`TrainWagon.ReviveSeenAt`, stamped each tick on every role through `WagonRevivable`). Measured by `--test-boarders=knock` at speed:
    from the cart's seat 0, looking back at the seat's 150° (30° off W1, whose rim is 2.35 m off the eye and its bench 3.96 m, past the
    ray's 3 m), W1 offered "Revive {name}" as the yaw's car and the press revived; seated in W2, the ray found W2 itself (0.39 m to its rim)
    and offered its downed crew member's revive with a move to W3 offered by the yaw, and the press revived; nobody downed, every move as
    before (`=move`).
  - **The hop.** The press books the target's place in its seat book; the body stays in its old seat for `HopSeconds` (0.6 s), counting as
    aboard the old car, then sits in. `cart_jump` plays at the press and `cart_land` at the landing, with a small shake (none in VR), and
    the log says "{name} climbs back to wagon 2." / "… forward to wagon 1." / "… forward into the cart." (`TrainMoveRules.Log`). The server
    prints `[rails] move: {name} {back|forward} from car {a} to car {b} ({why}) at S=…` and `[rails] move: {name} lands in car {b} seat {n}
    after {t} s`. A hop lands unless the hero went down, left the train or the target was uncoupled meanwhile (`Lands`); then the booking is
    dropped and the hero stays where it sits.
  - **The cooldown.** E does nothing for `MoveCooldown` (1 s) from the press, so a mashed key never becomes a get-out at speed. The server
    takes a remote move whose target is still `Target` on its own book, `MoveCooldown` − `AcceptSlack` (0.75 s) after the last it took for
    that hero (`TrainMoveRules.Accepted`).
  - **Never offered, and refused** (`RailController.MovesRefused`, `TrainMoveRules.Refused`): while the train is held or halted; while
    downed; inside any seated lever window of the armed ride, to anyone in the train (`RailEncounterController.AnyLeverWindow`: the window
    owns E); with the train on ride 4's live collapsing bridge (`HoldsRiders`). A press that asks for a move from another car of the
    rolling train on a car whose plain use offers nothing (`IsMoveAsk`: `IsMovePress` or `IsMoveAttempt`) is judged as a move, never as the
    car's plain use: refused, it is said out loud on the server and in solo (`[rails] move refused ({why}): …`, `[net-server] interact {ref}
    from {name}: move refused ({why})`; "the place is booked", "a lever window is open", "the train is on the live bridge"). Held, a press
    on another car is that car's plain use; rolling, so is a press on a car whose plain prompt offers the presser something (a downed crew
    member's revive), so a cart rider can revive a downed crew member in W1 at speed too.
  - **Seated in a wagon** a hero counts as aboard for the departure's player aboard and seated count, companions hopping in, an arrival's
    "no player aboard" set-down, the relief's left-behind test, the capture of the riders and a restore's set-down (Ruling 12). It does
    not pump or brake (only the cart's riders do: `RailController.Inputs`, `ServerWorld.RideInputOf`), it is not "able" for a deadly
    switch (`AbleToThrow`), it is offered no lever window and no leap, and `GetOutWithParty` reads the cart alone. A wreck sets a wagon's
    heroes down beside the lane with the wreck's 25% toll, as the cart's riders; crew stay seated. A companion on foot hops in while its
    lead rides any car of the train (`Adventurer.BesideTheCart` reads `CarOf`).
  - **Measured** (`--test-boarders=move`): every landing 0.600 s after its press, within one physics tick (0.017 s); a press 0.3 s after
    the first does nothing; the next move offered again 1.01 s after the first press.
- **The reach** (Ruling 13; `BoarderRules.Strikes`, `Adventurer.BlowReaches`). A seated hero's melee (the shapecast, the blade's contact,
  the companion's boarder path and the "met nothing" fallback) lands on a train boarder only when the boarder's car is the hero's own;
  carried and chained blows (`Cleave`, `ShatterChain`) from a seat in the train keep to it too. A hero on foot is unaffected, and so are
  arrows and spells: an archer in the cart can shoot W4's hound. `BoarderRules.WagonReach` (2.0 m) replaces the old cage-roof
  `CompanionBrain.BoarderReach` (3.9 m and 45°): a seated blade reaches its own car's rim boarder within 2.0 m and 45° of its aim.
  Measured by `--test-boarders=reach` off the wagons' own poses: W2's seat 0 to its own rim spot 1.789 m, straight and in the sharpest bend
  (0.334/m), the bodies' spread 1.80–1.85 m; the car ahead's spot 1.75–1.77 m (nearer than the own spot, so the own-car rule, not the
  distance, keeps the blow off it), the car behind's 3.66–3.96 m. The proposed 2.4 m was cut to 2.0 (the own spot + 0.15 rounded up,
  covering the widest body sample + 0.15, under the 2.6 m spacing; a unit test pins both bounds). A companion on a crew bench reaches too:
  W2's seat 5 to its rim spot 0.93 m, seat 4's 1.25 m (`=throw`). The reach for a hero on foot is not measured.
- **The grip** (Ruling 14; `BoarderRules.Grip`, `GripTaken`). A wagon boarder has a grip, as a barricade has its toughness: three hits,
  or one spell, knock it off alive, each `EventBus.EnemyHurt` by a party member (a player, a companion, a remote player: `byMember ≥ 0`)
  weighed by `BarricadeRules.HitWeight(Enemy.LastHitKind)`. Damage over time and thorns do not count, and a killing blow is left to the
  death drop. Knocked off, it drops beside the lane as a creature on foot, left behind, aggro on: "{name} knocks the hound off wagon {k}!"
  and `[rails] t=… {hound} knocked off wagon {k} by {name} after {n} hit(s)`. The cart's own rim boarders have no grip: they fall off only
  when they die. A client drops the hound as it leaves the boarder list (`RailNet.Leaps.Reconcile`); a remote hero's hits ride its
  `AttackIntent`s.
- **Companions answer** (Ruling 15 as amended at Task 4's review; `BoarderRules.PickAnswerer`, `TickAnswers`, server and solo). A wagon
  needs an answer when it has a boarder aboard, crew seated in it and no up hero (`BoarderRules.NeedsAnswer`). Its candidates are the
  seated, up companions and hero bots of the train not already answering and not hopping.
  - A candidate with a clear shot answers from its seat first: it targets that boarder and never moves (the nearest by cars, then the
    nearer the cart, then party order). "A clear shot" is exactly what the companion brain would fire at a single boarder
    (`CompanionBrain.HasShot`: a ranged weapon's swing or a projectile with an attack, arrows in the bag, in range and in sight; never a
    nova). A pumping companion is never a seat answerer.
  - Only when nobody has a shot does one climb: the nearest blade (a companion that knows no shot and has a blow, so a rogue with Smoke
    Bomb or Fan of Knives climbs) or hero bot, a blade first on a tie by cars. It moves one car per cooldown (`TrainMoveRules.AnswerStep`,
    which never passes its wagon), only while moves are not refused. A moving answerer stops advancing once its wagon is clear ("holds in
    car c"), and goes back toward the cart `AnswerBack` (5 s) after it is, while the cart has a free hero seat.
  - **Left in a wagon** (plan 14's final review M8). A companion an answer ends with in a wagon (the cart with no free hero seat, the
    answerer down), or a lead the player possessed away from in a wagon, goes back toward the cart by itself once a hero seat of the cart is
    free, by the answer's rules: one car per cooldown, none while moves are refused or its own hop is under way, and never while its own
    wagon has a boarder (it fights from there); answering again ends it (`TickStrays`). `[rails] answer: {name} is left in car {c} ({why})
    …` and `… is back in the cart by itself`. Measured in `--test-boarders=knock`: the player takes a cart companion with the lead in W1;
    the old lead is back in the cart's free seat one move later.
  - It lands in the wagon's hero place, or, when a downed hero holds that place, on a free crew bench seat (4, then 5;
    `TrainMoveRules.AnswerSeat`), so a hero down in seat 0 never keeps the answer out.
  - A seat answer ends on the tick its answerer starts pumping, goes down, is taken over by a player, or has been without its shot for more
    than `BoarderRules.ShotGrace` (0.5 s); the wagon is answered afresh that tick. A caster out of mana still counts as having a shot, as the
    brain's own `CanStrike` does.
  - Prints: `[rails] answer: {name} answers wagon {k} from its seat in car {c} (… m, a clear shot)` or `… (from car {c})`, `… reaches wagon
    {k} after n move(s) [route]`, `… goes back to the cart …`, `… is back in the cart …`, `… stays in car {c}: the cart has no free hero seat`.
  - The answer's reads allocate nothing (plan 14's final review M5): `Adventurer.Shooter`, `Blade` and `HasShotAt` read each known ability's
    kind (`CompanionBrain.IsShotKind`, `IsBlowKind`, which no upgrade changes; a unit test holds them to the views' `IsShot` and `IsBlow`)
    and the line of sight by one kept `RayCast3D` (`ClearLine`), and the throw tick walks the wagons by index.
  - Measured in `--test-chase` (Task 4's final tree): every hound needing an answer on ride 2 answered from Wren's seat, first struck
    0.13–0.92 s after it boarded; on ride 4 r4_hounds' two boarders answered from their seats by Wren and Ilex, nobody leaving the cart.
- **A boarder alone throws prisoners off** (owner's call; Rulings 16, 17 and 20; `ThrowClock`). A wagon is alone when a boarder is on its
  spot, at least one crew member sits in it, no up hero is in it and the train rolls (`BoarderRules.Alone`; a downed hero does not stop
  it).
  - **The call.** On each start of an alone stretch the seated up hero of the train nearest the wagon calls it through
    `RailCalls.SayNearest` (`BarkTrigger.BoarderThrow`, a Shout, appended after `SlagDrop`, with subtitle-only stock lines in `RideBarks`:
    three per roster name, Sable's in her own words, two per class, three generic; none names a wagon). With nobody to speak, the log
    reads "It's at the prisoners — wagon {k}!". `[rails] t=… wagon {k}: {hound} alone with {n} crew; called by {who|the log}`.
  - **The glow.** The hound glows `ThrowGlow` (1.5 s) before each throw (`TelegraphFx.HandGlow`; `[rails] t=… {hound} crouches over the crew
    of wagon {k} …`), told to clients as the `BoarderThrow` enemy telegraph (`TelegraphRules.BoarderThrow` through
    `EventBus.RaiseEnemyTelegraph`, the kiln golem's path; a client's copy glows from `Enemy.ClientTelegraph`). Hounds have no crouch clip:
    the crouch is the glow.
  - **The throws.** After `ThrowFirst` (6 s) alone it throws one crew member off, seat 5's first (`BoarderRules.ThrowSeat`), and one more
    after each `ThrowEvery` (4 s): `[rails] t=… {hound} throws {name} off wagon {k} (seat {n}) after {t} s alone ({p} s paused)`. 6 s
    covers a hero going from the cart to W4: four moves take 3 × 1 s of cooldown + a 0.6 s hop = 3.6 s, leaving 2.4 s to react; any later
    change to the wagon count or the cooldown must keep `ThrowFirst` ≥ `TrainMoveRules.ThrowBound(moves)` ((moves − 1) × cooldown + hop +
    2 s, 5.6 s for four), which a unit test pins.
  - **The clock** runs only while a hero could move to that wagon: it pauses, keeping its count, while moves are refused for everyone (a
    seated lever window, the live bridge). A throw never comes without a glow since the clock last resumed (plan 14's final review M4, the
    owner's "a warning first"): a throw that falls due after a pause whose glow came before it glows again first, and comes `ThrowGlow`
    (1.5 s) later, the throws after it `ThrowEvery` from it (`ThrowClock.PutBack`; the glow's print adds "its glow again after a pause: the
    throw put back to {t} s"). So a throw after the bridge's hold, or a lever window opened mid-glow, comes up to 1.5 s later. It resets to the full 6 s whenever the wagon stops being alone: a hero lands in it, the boarder
    dies or is knocked off, or the train is held; the clocks clear on an arrival, a wreck, a restore or the party on foot. So
    `r4_hounds`, boarding at S 853.6, runs its clock about 1 s before the split's window (from S 858.6) pauses it.
  - **A thrown prisoner** (`CrewRescueController.ThrowCrew`, `Npc.Throw`) is unseated (the usual `SeatMsg`) from over the rim above its
    seat, at the tub's top + 0.05 m, and thrown on a 0.9 s arc to a set-down spot on the enemy lane's side
    (`RailController.SetAsideBesideEnemyLane`, clamped 1.5–4.5 m out); for `Npc.ThrowLaunch` (0.1 s) it flies whatever its stale floor
    state says, so crew who walked to their seats are thrown too. It dies at once (`Dead`, no corpse, no revive, no loot), is lost at once
    (`LostCrew`), logged "{name} is thrown from the wagon!" and sent as `PropEvt wagon:{k} "thrown:{crewId}"`. `BoarderRules.ThrownHide`
    (1.2 s) later it is hidden and `Fled` set (gone, not a corpse), so a save, a load and a client keep it gone; `Fled` waits for the hide so
    that clients see the tumble, and `FloorStateCapture` writes a thrown crew member as fled for a save inside the 1.2 s. The arc ignores
    the train's speed (measured on a straight: it lands 3.6–4.4 m out of line; sharp curves are unmeasured). Escorts (Grisel) never ride
    in wagons and are never thrown.
  - **Measured** (`--test-boarders=throw`, physics frames from the hound's landing): the call at 0.017 s, the glows at 4.517 and 8.517 s,
    the throws at 6.017 s (6.000 counted, seat 5) and 10.017 s (10.000, seat 4), each hidden 1.22 s after its throw (one tick from the NPC
    tick order); each flight rose 1.05 m over its seat and lay 3.6–4.4 m out from the wagon's line, on the enemy lane's side. In
    `=lone` the lead climbed cart → W1 → W3 → W4 past a companion in W2 and landed 2.63 s after the hound, 3.37 s to spare; in `=pause` a
    3.83 s lever window paused the clock 3.83 s and the throw came at 6.000 s counted.

`--test-boarders[=move|reach|knock|throw|lone|pause|groups]` (`DebugTools.Boarders.cs`, the dispatcher and shared helpers, with one
partial file per variant, `DebugTools.Boarders.{Move,Reach,Knock,Throw,Lone,Pause,Groups}.cs`; `=throw` is the default; quits after
150 s, `=groups` after 600) plays floor 12 at lethality 3, solo, possession off, every enemy culled as it appears but the hounds the flow
puts on wagons (`RailEncounterController.DebugBoard(k, holdHp)`), and keeps the party's HP up. An unknown variant prints an ERROR and
fails. It prints `[boarders]` lines and `[boarders] RESULT ok ({variant}): …`.
- `=move` (from stop 0, ride 1): the lead's seat yaw turned and every press the VR grip's. "Climb back to wagon 1" lands after 0.6 s
  within a tick and logs; a press 0.3 s later does nothing; the next move at the cooldown; W2 to W4 past a companion seated in W3 in one
  move; forward to wagon 2, 1 and the cart; W2 under `r1_beam` (every head clear by 0.05 m or more); a double press for W1 (the lead's
  press and a cart companion's real press on the booked W1) refused out loud, the place booked, with no plain use of the wagon; a mid-hop
  down dropped (the lead stays in W1, W2's place free again); never offered held at stop 1 (W1's prompt "Get out"), downed or on foot; a
  point 4.1 m end-on from W1's middle reads 3.000 m from its tub (`TrainWagon.TubDistance`, the server's reach; 4.22 m from its origin);
  and at stop 1, with the lead in W1, a companion set on foot beside W3 (8.9 m from the cart) hops in by its own brain.
- `=reach` (from stop 1, ride 2): the lead in W2, hounds held above 1 HP on W1, W2 and W3, answers and fire held. The reach off the poses
  (above); every melee attack the lead's class has, aimed at each hound in turn, lands only on W2's (W1's and W3's take no hit and lose no
  grip); a cleave off W2's hound widened past both neighbours (2.91 m) and a shatter chain take neither; a cart blade's strikes at W1's
  hound never land, nor the same blade's from W3 at W2's hound within 2.0 m (1.77–1.95 m, the own-car rule).
- `=knock` (from stop 1, ride 2): the lead climbs into W1 by the grip's press; at speed after the move's grace W1's own prompt is "" (no
  Get out), and in ride 3's first seated lever window (`r3_switch_out`) the lead's move is refused ("a lever window is open") and none is
  offered. Pass 1: three melee blows knock a W1 hound off (grip 1, 2, off), standing alive beside the lane, aggro on. Pass 2: one spell
  (a Fireball) knocks a fresh one off. Pass 3: 3a, a W3 hound answered from a cart seat with a clear shot (no move; three ranged hits knock
  it off); 3b, each shooter made to pump in turn hands the answer to the next shooter within 0.25 s, and with none left the cart's blade
  climbs (0 > 2 > 3, 0.99–1.00 s apart), knocks the hound off (grip 1, 2, off) and moves back 5.03 s after W3 went clear (the cart's own
  pump held, `PumperCap` 0). Pass 4: a killed W1 hound lies beside the lane. The final review's I1: before the first move a W1 crew member
  put down by the test is offered ("Revive {name}") and revived from the cart's seat 0 looking back, and after it stop 2's crew are seated in
  W2, the lead climbs there, one is put down and, seated in W2 at speed, the lead's ray finds W2 and its revive (a move offered by the yaw
  all the while) and the press revives; the lead climbs forward to W1 again. Plan 15 Task 0: N1, after the cart's revive, a hound held on
  W1 beside a W1 crew member put down (1.25 m off): no revive offered, the cart rider looking back is offered "Climb back to wagon 1", the
  press lands it in W1 and W1's throw clock (alone until then) is reset in the landing's tick, no throw; there a press on W1 from its own
  seat at speed is refused out loud ("Not with enemies this close.", no move, no get-out). N2, after the climb forward to W1: the lead
  climbs into the cart, a companion holds W1's place, a W2 crew member is put down (no enemy near) and from the cart seat 0 (W2 4.0 m off,
  beyond the 3.5 m reach; a bend that brings it within reach is waited out) the look offers "Climb back to wagon 2", not the revive; in W2
  the ray offers the revive and the press takes it. The climb forward to W1 after it is a stale revive: a W1 crew member put down, the look
  from W2 shows "Revive {name}" on W1 (by the ray), a hound lands on W1 in the press's frame, and the press is taken as the move to W1 with
  the move's grace started on the presser's machine. M8: after the lever window the player takes a cart companion,
  and the old lead goes back from W1 into the cart by itself.
- `=throw` (from stop 1, ride 2; the default): stop 1's crew walk out of their cage to W3 as in play; nobody answers, fire held, the hound
  held above 1 HP, so the clock alone is measured: the call, the glows at 4.5 ± 0.1 and 8.5 ± 0.1 s, the throws at 6.0 ± 0.1 (seat 5) and
  10.0 ± 0.1 s (seat 4), each glow told as the `BoarderThrow` telegraph in its tick, each flight over the rim to more than 1 m out on the
  enemy lane's side, each thrown crew member dead, unseated, hidden 1.2 s (± 2 ticks) later and fled, lost, logged and sent. Part 2: a
  blade answering W2, whose place a downed hero holds and whose bench is full, waits one car short until the throw empties seat 5, lands
  there within a cooldown and a hop, and its blows from the bench end the hound. Part 3: a save and a load keep the thrown gone.
- `=lone` (from stop 1, crew in W4, a companion in W2's place): the lead climbs from the cart to W4 within `ThrowBound(4)` (5.6 s) of the
  hound's landing, the clock resets, and no glow or throw follows. Then (the final review's M3) W4's two crew, let out of the train by the
  test's hook (`CrewRescueController.DebugLetOutOfTrain`, the no-carts-left path), stand on two cells of their own, neither a rail.
- `=pause` (from stop 2, crew in W1, ride 3, `r3_heap` cleared by the test's hook): a hound lands 3.0 s before `r3_switch_out`'s seated
  window; inside it moves are refused, none is offered and the clock stands still; the throw comes at 6.0 ± 0.1 s counted with the
  window's length paused; a move pressed `HopSeconds` before the next throw is due lands on the throw's own tick and resets it.
- `=groups` (from the landing to the last stop, nobody pumping until 1 m past `r5_golem`'s S, stops 1–3's crew seated in W1..W3 by the
  test's hook, both ride-3 levers thrown at the arming, every enemy culled but `r1_hounds`', `r3_hounds`' and `r5_hounds`' hounds, which
  hold their HP only while they ride their cart): one `[boarders] group {id}: spawned at …, in reach at …, alongside at …, {hound} leapt
  at … onto …, its cart freed at … ({why})` line per hound and one `[boarders] leap: …` line per leap of the floor build with its length.
  It fails unless every hound leapt, each landing on a body within `LeapReach` (8.6 m) (a miss's length is printed only; its take-off, the
  nearest coupled body on the frame before the leap, must be within the reach), `r1_hounds` leapt with the cart 5 m past `r1_barricade` or more and the front at
  `r1_beam` or short of it, `r3_hounds`' cart was freed short of `r3_switch_out`'s points − 2 m, `r5_hounds` spawned with the train's rear
  home past the pit switch's back and was never rammed nor freed by the crossing, both its hounds landed `BdGolemMargin` (0.4 s) or
  more before the golem's burst and their cart was freed short of the golem or by its burst and never within 3 m of its runner, and every
  throw came after its call and glow. Then a crossing pass, restored at stop 2 with every hound held aboard
  (`RailEncounterController.DebugHoldCrouches`), must see `r3_hounds`' cart freed by the crossing with the front within
  `CrossFreeAhead` of the points, and no leap.

The cart's plating is mended to full whenever it rolls into a stop (`RailController.Arrive`), as a load would give it: each
ride has its own 100. A halt at the heap is not an arrival and mends nothing.

Traffic (`RailEncounterController.Traffic.cs`). Every 25 s with no ride armed, no ride's slag cart live and the party's
cart not rolling, a riderless slag cart runs the whole enemy line at cruise. There is at most one, and it goes when a ride
arms. Every 40 s while the party's cart is held at a stop with nobody aboard and the gate up, a `RunawayWagon` breaks loose
on the party line. It runs from 6 m past the stop to 3 m short of the next stop, halts, and is freed a second later. Both
ring the bell, warn a hero on their lane and run over whoever is on it. A pocket is safe from every cart. A companion, crew
member or escort on foot on a warned lane walks to the nearest pocket of that lane within 12 m (else to a walkable cell off
the rails within 3 cells) and holds there until the cart's front is 2 m past it.

Barricades (plan 11; `Rails/SetPieces/Barricade.cs`, `RailEncounterController.Barricades.cs`, the numbers in
`Rails/Core/BarricadeRules.cs`). A barricade is two posts and three crossed planks, 1.8 m wide and 1.2 m tall, on the
`weathered_planks` texture, with a lantern on a stake in front of it on the cart's side. It is solid (layer 1) and something to
hit (layer 4, an `ICombatTarget` like the heap). There are two set-piece kinds, not a lane field (Ruling 1): `barricade` stands on
the party line at the piece's S, and `slag_barricade` on the enemy track at the enemy-lane S beside it
(`[rails] barricade {id} on the {party|slag} line at S=…`).
- **Cutting it** (Ruling 2). It takes `Toughness` 3. Any hit that is not a spell (a blade, an arrow, a shove, a thrown weapon, a heavy
  blow included) knocks off 1 and takes a plank with it ("{name} hacks at the barricade."); any `AttackKind.Spell` hit clears it
  at once (`HitWeight`). Cut, it falls in splinters (`TimberDebris.Burst`, `crate_break`), the log says "{name} cuts the barricade
  down." and the server prints `[rails] barricade {id} cut by {name} at S=…`. A seated hero can hack one from the seat: brake to a
  stop with the front within half a metre of its face and swing from the front seat.
- **The warning** (Ruling 6). Companions and hero bots never attack a barricade (breaking it is the player's job; a companion's cleave
  or arrow on layer 4 may still hit one by chance). A seated hero calls it once, `BarkTrigger.Barricade` (a Shout; subtitle-only
  stock lines in `RideBarks`, and with nobody to speak the log gets "Timber across the line — cut it!"), when the cart's front comes
  within `CallAhead` (35 m, about 6 s at cruise) of its face on its line: `[rails] barricade {id} called at front S=… (… m to its
  face); bark=…`. Its lantern blinks by the heap's rule (`SetPieceRules.HeapAlarm`) while the cart bears down, and `cart_bell`
  rings as the blinking starts.
- **The smash** (Ruling 3). A cart whose front reaches the face (`FaceS`, `HalfDepth` 0.25 m short of its middle) while it stands,
  on its own line, smashes through it (`Smashes`, read along the cart's own way of travel by `SmashesAlong`): it goes in timbers
  (`timber_smash`). The party's cart loses `SmashDent` (25) plating through the cart's usual dent (0 wrecks it, as any dent does) and
  keeps `SpeedKept` (80%) of its speed. Each seated hero who is up takes `SmashFraction` of max HP: 15% × `TierRules.PartyDamageScale`
  / 3, so 15% at the default lethality of 3 and 5% at 1, never taking anyone below 1 HP (`SmashDamage`). NPCs on the bench and crew
  in the wagons are only jolted (a quest NPC dies at 0 HP). The log says "The cart smashes through the barricade!" and the server
  prints `[rails] barricade {id} smashed at S=…: plating {a} -> {b}, N hero(es) hit`.
- **Every cart on its line** (Ruling 4, `SmashAhead`). A ride's slag cart and the traffic cart on the enemy track, and a runaway
  wagon on the party's, smash a standing barricade on their own line too: the cart keeps 80% of its speed and each living rider
  takes a hit of 15% of its own max HP, before armour (`SmashHpFraction`, dealt through `ReceiveAttack(Trap.TrapStats, …)`, so armour
  can shave it; not scaled by lethality, so an enemy may die of it): `[rails] {cart} smashes the
  barricade {id} at S=…: N rider(s) hit`. Chasers, the relief cart and spares are not judged: they follow a train that already
  took the line. The party's cart judges a party-line barricade only while the face is not across on the enemy lane, and a
  slag-line one only while its own front is across (`OnItsLane`, `RailController.FrontOnEnemyLane`), so it passes
  `r2_slag_barricade` on its own lane.
- **Down until its ride is undone** (Ruling 5, the heap's rule). Cut or smashed, a barricade stays down. It stands whole again at its
  ride's next arming after a wreck or a `Restore` (a wipe's rise, a load), or when it went down while its ride was not armed
  (smashed by traffic or a runaway) (`StandsAgainAtArming`; the last case logs `[rails] barricade {id} stands again at ride N's
  arming (it went down while its ride was not armed)`). A cut is the
  party's work wherever it was made, so a barricade cut on foot before its ride stays down.
- **Placement** (Rulings 7 and 8). "Defended" is placement, not a guard kind: `r2_barricade` stands where `r2_slag2` (a priest
  thrower and a hound) runs alongside, and `r2_slag_barricade` stands on the slag line ahead of `r2_slag1`, whose carts smash it.
  Ride 5's barricade stands before the buffer. Every barricade's lantern is in sight from the seat 15 m out (the barricade flow's
  layer-1 ray from the seated lead's eye).
- **On the wire.** Every role builds the barricades from the plan; the server judges them and tells `PropEvt`s on `barricade:{piece}`
  (`NetRefs.Barricade`): "cut" and "smashed" (a client throws its own timbers, its cue from `RideAudio.OnProp`) and "standing".
  `RailPropsDto.DownBarricades` (hashed as `D` in `PropCheck`) carries the ones down for a catch-up, and a client's lantern blinks by
  the same rule (`RailEncounterController.Net.cs`). A partial hit is not mirrored: a client shows a knocked barricade whole until it
  falls. The RIDE pass cuts ride 1's from a remote seat and watches ride 2's smash on a client (see "The RIDE net test").
- **Test hooks** (internal): `DebugCutBarricades(ride)` (Ruling 24: a flow that is not about barricades may cut its ride's at the arming
  and log `[<flow>] barricades on ride N cut by the test`; none needs it today, since the ride flows smash through and keep their
  checks), `DebugRestandBarricades(ride)`, `Barricade.DebugCut(who)`, and the counters `BarricadesCut` and `BarricadesSmashed`.

`--test-barricade` (`DebugTools.Barricade.cs`, quits after 240 s) plays floor 12 of the built-in campaign at lethality 3, solo, Grisel
forgiven and on the bench, with every enemy culled as it appears (the flow owns the plating and the HP), and prints `[barricade]`
lines and a `[barricade] RESULT`. First, the sight: from the seated lead's eye a layer-1 ray to each barricade's lantern is clear 15 m
out. A: at stop 0, on foot, one `Spell` hit cuts `r1_barricade`, which is then stood up whole again. B: on ride 1 the call comes once
with the front 30–35 m from the face; from 12 m out the human brakes to a crawl, creeps up with the brake feathered and stops short of
the face; the seated lead's blade cuts it in three hits from the front seat (standing after hits 1 and 2); the brake is let go, no
smash is logged and the plating is unchanged. E (on ride 2, before C): `r2_slag1` smashes `r2_slag_barricade` on the slag line, each
cinder down 15% (±1) of its max HP or dead, while the party's cart passes it on its own lane with no smash and the plating unchanged.
C: a companion held at 5% HP and Grisel on the bench; nobody brakes or hits; the cart smashes `r2_barricade`: the plating −25, each
other seated hero −15% of max HP, the 5% companion up with 1 HP or more, Grisel unhurt, V × 0.8. D: arrived at stop 2,
`rc.Restore(1)`: `r2_barricade` and `r2_slag_barricade` stay down until ride 2 arms again, then stand whole.

Switches and the lane blend (ride 3). A `switch_out` lever (`TrackSwitch`) stands between the lanes, 5 m short of its
points. E toggles it (thrown, or set back); any hit only throws it, so a stray friendly shot never sets a thrown lever back. A
blade's sight ray ignores the lever's own collider. Once the cart's front has
passed the points the lever's prompt is gone. Thrown as the front reaches the points, the cart crosses
(`RailController.Cross`). The ride's next `switch_back` brings it home: a fixed one (always thrown, untouchable, `TrackSwitch.Fixed`)
at its points, set as the cart crosses out; a live one (below) only if a player throws it. The pose eases across
over 6 m after each switch (`LaneBlendAt`, read per S, so each coupled wagon crosses at its own S); crossover rails are
laid as dressing, and timber posts in the way are taken down. While the front is half way across or more
(`FrontOnEnemyLane`, the one test for spawning, pacing, ramming and the sweep), the run-over sweep and the warning judge
bodies against the enemy lane. There, a slag cart ahead cannot outrun the party's cart (it runs at most 2 m/s slower).
When the front reaches its back end it is rammed: it derails and the plating takes 10. Levers fall back to straight
when the cart is wrecked or restored, or the party goes on foot. An arrival at stop k sets back only the levers of rides
1..k, so a lever a walker threw ahead for a later ride (ride 3's switch, ride 5's spur (`r5_spur`)) stays thrown.

Throwing a switch from the seat (plan 8, `Rails/Core/SeatedSwitchRules.cs`). Nobody can aim at a thin post from a rolling
cart, so a lever has a seated window. It opens with the cart's front 15 m before the lever (`TrackSwitch.LeverS`) and
closes when the front reaches the points (or the lever is taken). It is offered to a seated, up rider of the party's cart
while it rolls, only for the armed ride's throwable levers (never a fixed `switch_back`), the nearer of two by S
(`RailEncounterController.SeatedLeverFor`; a live switch back only while the cart is across, below). In the window the rider's `LookingAt` is that lever whatever the aim
(`Adventurer.TickView`; the look ray still runs for the VR beam), so the HUD shows `[E] Throw the switch` (or "Set the switch
back"), E or the grip toggles it through the usual interact dispatch, and "Get out" is not offered. A second window press on
the same lever is ignored on the presser's machine until the lever changes there or 0.5 s passes (`SeatedPressGuard`), so a
client's double tap inside one round trip, or a solo mash, never sets back what the first press threw. For 1 s after the
window closes, timed on the rider's own machine, a seated rider's E does nothing, so a press mashed past the points cannot
throw them out at speed (`SeatedWindowClock`; a rider downed, or swapped out of the player's hands, mid-window owes no grace). A held cart never offers the lever, so E stays "Get out" (and the cage hold) at a stop or at the
heap. On a co-op client the press is the lever's ordinary `InteractIntent`. The server accepts it by the usual reach or when
the lever is in that rider's window as it recomputes it (`RailEncounterController.InSeatedWindow`), with the window's start moved
`SeatedSwitchRules.AcceptLead` (5 m) earlier (`SeatedSwitchRules.Accepts`): a client's cart runs a little ahead of the server's (it
extrapolates; a loaded server steps late), and the RIDE pass measured client leads of 1.5–2.8 m, so a press on the client's first
window frame could reach the server short of its own window. The offer itself still opens at 15 m, and the server never accepts a
press at or past the points or on a taken lever (the cart has taken the lever as it stood), which keeps the larger lead safe. A
refused press gives the client no feedback (see "Known gaps"), except on a deadly lever (a live switch back, ride 4's split): a press after its
moment (the lever taken, or the front at or past a live back's points) is judged by `RailEncounterController.LateFor`
(`SwitchBackRules.Late`, plan 12 Ruling 21, returning `None`, `TooLate` or `AlreadyThrown`). With the lever straight it is too late and the
server tells that rider "Too late to throw it." (`SwitchBackRules.TooLateLine`); with the lever thrown it is already thrown and is
ignored quietly (`[net-server] interact … the deadly lever was thrown (taken …): ignored quietly`): a mashed second press is never
told "Too late" and never un-throws it. The server refuses a set-back from inside the rider's window, in reach or not, within 1 s of another rider's toggle (the last toggle's
time and hero name are kept on the server's `TrackSwitch`), so two riders cued by the same call do not undo each other into
the heap (`[net-server] interact … seated window: set back … ignored`).

The live switch back and the broken bridge (plan 11; `Rails/Core/SwitchBackRules.cs`, `RailEncounterController.Switchyard.cs` and
`.Break.cs`). A `switch_back` is live, not fixed, only when a broken bridge's gap (see "Crevasses") lies `LeadMin` to `LeadMax`
(12–40 m) past its points on its own ride (`SwitchBackRules.LiveBack`, Ruling 13); otherwise it stays fixed as before. Every role
decides this the same way at build, and the build line says `[rails] switch {id} points … (live: the broken bridge {lead} m past)`
or `(fixed, thrown)`. On floor 12 `r3_switch_back`'s points stand at S 694.4, 20.2 m before the gap.
- **The late return** (Ruling 14). With a live back, `switch_out` crosses with the return unset (`BackAtSwitchOut`: NaN; the take line
  says `the return open: {back}'s lever decides at S=…`). At the back's points the cart takes the lever as it stands then
  (`TakeBack`, `BackAtPoints`, `TakesHome`): thrown, it calls `Cross(CrossOutS, points)` and comes home ("The cart takes the switch back
  onto the party line.", `[rails] switch {id} taken at S=…: back from {points}`); straight, it stays across (`… passed straight: the cart
  stays on the slag line`) and runs on to the gap. Any later switch back the cart reaches across with its return still open takes it
  home the same way when its lever is thrown, a fixed one always (final review I1), so a fixed back after a live one is never shown
  thrown in vain. Clients learn the late return from `CartStateDto.CrossBack`, already on the wire, so the protocol stays 17. A lever
  thrown, then set back, or thrown and set back by a walker earlier counts only as it stands at the points.
- **Nobody able to throw it** (ruling R-lone; the spec's anti-goal: no death the player could not have prevented). When the front
  reaches a live back's points with its lever straight and no able human aboard (`SwitchBackRules.AbleToThrow`: a `LocalHuman` or
  `Remote` hero, not downed or fallen, not parked, seated in the party's cart), the back is taken as if thrown: the lever stays straight,
  the log says "No one could reach the switch — the cart takes the safe line." (subtitle only) and the take line ends `(no human aboard
  able to throw it: the safe line)`. With an able human aboard the switch stays theirs, and left straight the train falls. Solo, a
  downed player's control passes to a companion, who is then the human.
- **Only a player's** (Ruling 15, `TrackSwitch.Deadly`). A live back's collision layers are 1|8, not 4, so no hit decides it
  (`ReceiveAttack` is a `Miss`: a companion's stray arrow or blade never throws it), and it moves only for a human's E
  (`SwitchBackRules.MayToggle`: `ControlKind.LocalHuman` or `Remote`); a companion's or a hero bot's `Interact` does nothing.
  `Fixed` and `Deadly` are never both true.
- **Offered from the seat while across** (Ruling 16). The seated window offers a live back only while the cart is across with no return
  set (`SwitchBackRules.Offered`: `Crossing && float.IsNaN(CrossBackS)`), on every peer, and the server's `InSeatedWindow` uses the
  same gate. It offers nothing on foot either unless the cart is across with its return open, so it cannot be thrown ahead from the
  platform. The window, the grace and the set-back guard are plan 8's.
- **Two calls** (Ruling 17, `SwitchBackRules.Call`). Only a deadly lever gets them; `switch_out` keeps its one "Line's blocked" call.
  The early call, "The slag line's out past the bridge — switch back!" (`BarkTrigger.SwitchBack`), comes once with the front
  `CallEarly` (30 m) before the lever, or at once on crossing if the lever is nearer. The last, "Switch back — now!"
  (`BarkTrigger.SwitchBackNow`), comes with the front `CallLast` (6 m) before the points while the lever is still straight; if both
  are due, the last is said. Both are Shouts with subtitle-only stock lines in `RideBarks`, said through `RailCalls.Say`:
  `[rails] switch {id} called {early|last} at front S=… (… m to the points); bark=…`. The flags reset at the ride's arming.
- **The warning by sight** (Ruling 18). A live back has a steady red signal lamp on its own post, on the slag line's outer side,
  `SignalPast` (3 m) past its points (an `OmniLight3D`, 5 m, energy 1.6). The gap is dressed so it reads from the lever, about 20 m
  off (`BrokenBridge`): splintered plank ends hanging into the gap, the rails ending in stubs bent 30° down, a red lamp at the near
  edge on the party lane's side and a second at the far edge on the outer side, an ember glow under the deck lighting the broken
  ends and the rock, and a warm haze rising out of it. The enemy track's rails and sleepers are cut over the gap (`RailTrack.Cut`).
- **The fall** (Ruling 12). The party's train falls (`RailController.Fall("The slag line ends at the broken bridge!")`, see
  "Crevasses") when its front reaches the gap's near edge still across there (`SwitchBackRules.Falls`: `LaneBlendAt(gapS)` ≥ 0.5);
  the server prints `[rails] the cart runs off the broken bridge at S=…`. A guard (`SwitchBackRules.FallArmed`) keeps a correct
  choice alive: if any `switch_out` short of the gap has no switch back after it short of the gap, or any switch back between it and
  the gap stands less than `HomeStretch` (6 m) short of the gap (every back is judged, not only the first: any thrown one takes the
  cart home), the gap's fall is disarmed on that ride and an ERROR line says so. The watchdog never carries a train across the
  gap either (see "The watchdog").
- **Other carts at the gap.** A ride's slag cart or the traffic cart whose front reaches the gap on the enemy track drops through it,
  its riders lost to the crevasse (`CrevasseWatch.Take`). It goes quietly (`SlagCart.PlungeThrough`): no derail line, no taunt, no
  tremor on any peer; "The slag cart plunges through the broken bridge." is logged only with a hero within `PlungeHeard` (40 m) of
  the gap (`PlungeTold`), and a client reads the drop from the gone cart's last position (`PlungedAt`). Traffic started while the
  party's cart is held past the gap starts `TrafficPastGap` (2 m) past its far edge (`TrafficStartS`), so the stops beyond keep their
  traffic.

`--test-switch-back[=wrong|stall|lone|fixedafter]` (`DebugTools.SwitchBack.cs`, quits after 300 s) plays floor 12 at lethality 3 and prints
`[switchback]` lines and a `[switchback] RESULT`. A: the broken bridge as built (the gap bare of footing and track, the deck and the
party lane beside it whole), a traffic cart with a rider dropping through the gap quietly (told only with a hero within 40 m), and,
with the cart at stop 4, traffic starting past the gap. B (thrown, the default): ride 3 across the slag line with no hook; the early
call is heard 25–30 m before the lever; until the lead's press the lever stays straight with `LastToggledBy` null (no companion
touched it); the seated window offers it ("Throw the switch") and the lead presses through `VrUse`; the take line says `back from`;
the lane blend is ≤ 0.01 by the points + 6 + 1.1 + 1 m; there is no fall and the cart arrives at stop 3; and a layer-1 ray from the
seated lead's eye at the lever's S to the bridge's lamp is clear (`RESULT ok (thrown)`). `=wrong`: nobody presses; both calls are
heard (the last 5–6.5 m before the points), the lever is never thrown, the take line says `passed straight`, the train falls at the
gap (every rider fallen, nobody but the train), and the wipe rises at stop 2 (`RESULT ok (wrong)`). `=stall`: as `=wrong`, with the
cart caught short of the back's window, inside it short of the last call, short of the points and past them: the watchdog sets it at
the window's start, then at the last call's mark (the last call is heard after it), then at the points, then grinds it into the gap
(a fall), never across. `=lone` (ruling R-lone): nobody presses, and the lead, the only human (possession off), is downed in the seat
3 m before the window and held down until the back is taken; at the points the cart takes the safe line with the lever straight, the
line is logged, and the cart comes home and arrives at stop 3 with no fall (`RESULT ok (lone)`). `=fixedafter` (plan 12 Ruling 20, plan 11's
first residual): a test hook (`RailEncounterController.DebugFixedBackBeforeGap` = 8) adds a fixed switch back `r3_switch_back2` 8 m before
the gap (built after the break with the next free piece index); nobody presses the live back, which is passed straight; the cart is caught
3 m past its points, and the watchdog (`WatchdogBreak`) grinds it to the fixed back's points, where `TakeBack` takes it home: no fall, home
with the front 1 m short of the gap, arrived at stop 3 (`RESULT ok (fixedafter)`). Every variant holds ride 4's runaway (plan 12 Ruling 22,
`[switchback] the runaway on ride 4 held by the test`).

The heap. An `OreHeap` blocks the party lane (`RailController.BlockLine`). Its lantern blinks while a cart bears down on
it. A seated companion calls "Line's blocked — switch!" once, as the seated window opens (the front 15 m before the lever)
while the lever is still ahead; with no lever ahead, at 3 s of look-ahead to the heap. A cart that reaches the heap on the party lane crashes: the plating takes 40, the cart stops at the heap's face and
is Held on the line (`RailController.Halted`, a `Halted` event). The HUD says "Cart — line blocked", and nobody boards on
until the heap is cleared by three hits from anyone on foot. Then the cart boards as at a stop and goes on to the next stop
(a `Resumed` event, not a `Departed` one: the ride stays armed). The heap stands again after a wreck or a load. Ore buckets
on a cable over the line tip one at a time every 3 s, after a 1.5 s creak and glow. Each pours a cinder patch that burns
for 4 s. A cart through a live patch loses 5 plating, and each seated hero takes one lick of the floor's cinder fire.

Ride 4: the dark, the runaway and the split (plan 12; `RailEncounterController.Dark.cs`, `.Runaway.cs` and `.Split.cs`,
`Rails/SetPieces/SplitSwitch.cs`, `RideDarkness.cs`; the numbers in `Rails/Core/DarkRules.cs`, `RunawayRules.cs` and `SplitRules.cs`).
Three set-piece kinds, appended at the end of `RailSetPieces` so the older pieces keep their indices and wire names: `dark`, `runaway` and
`split`. Floor 12's are placed from the shipped seed's measured layout (Ruling 17): `r4_dark` At 0.093 (party S 793.6–878.6, starting 70 m
before the points), `r4_runaway` 0.172 (S 813.5, 50 m before them), `r4_split` 0.371 (S 863.7, the points at 863.6), and `r4_hounds` moved from 0.3 to
0.262 (S 836.2, the dark's middle; at 0.3 it stood under 15 m short of the split's window). `r4_beam` stays. Ride 4 has no barricade (Ruling 18).
- **The dark** (Ruling 16). Its At is its start and it runs `DarkRules.Length` (85 m). Every role, the headless server included, puts out
  the pocket lamps whose pocket lies within `LampReach` (6 m, flat, on its level) of either lane over the span: each `RailLamp{i}` light is
  hidden and its `RailLantern{i}` gets copies of its materials with the emission off (the shared materials stay lit elsewhere). The count
  comes from the layout, so every peer prints the same line: `[rails] dark r4_dark from party S=793.6 to 878.6: 10 pocket lamp(s) out`
  (every lit pocket has a `RailLamp{i}` light, so all 10 lights are hidden; 104 stay lit outside the span). A peer that draws adds a `RideDarkness`. It darkens that
  peer's view by where its camera is in the span (`DarknessAt`, within 8 m of either lane; `DarkRules.Factor` ramps over 6 m at each end):
  ambient × 0.12, fog light × 0.25, volumetric emission × 0. Each hero's torch dims to × 0.1 by its own place in the span. Cart lamps,
  slag carts' lamps, wheel sparks, set pieces' warning lamps (the split's two) and the chasm's ember glow stay lit, and no new light is added.
  Nothing goes on the wire: every peer reads the dark from the plan. A floor has one dark; a second logs an ERROR and is not built.
- **The runaway** (Rulings 10–14). As the party's front first reaches its At, one `RunawayWagon` (the traffic runaway's body, with its orange
  lamp and bell) breaks loose on the party line `RunawayRules.Behind` (15 m) behind the train's last body (the cart and its coupled wagons). It
  rolls at `RunawayRules.Target`: the train's own unpumped speed for its load × (1 + `Boost`), `Boost` 0.15, so one pumper holds it off and no pumper lets
  it close. The log says "A runaway ore wagon breaks loose behind us!", a seated companion calls "Runaway on the line behind us — pump!"
  (`BarkTrigger.Runaway`), and the server prints `[rails] t=… runaway {id} breaks loose at party S=…, … m behind the train (N wagon(s)),
  target … m/s`. Its bell rings every 2 s while it is within 40 m behind the train. It is not sent (`[rails] runaway {id}: none (…)`) when:
  - its ride has no live split;
  - a chaser or the relief cart is on the party line;
  - the split's window opens under `MinLead` (10 m) past the trigger;
  - its start would lie under `MinRoom` (2 m) past the ride's stop;
  - a body on foot is on the line where it would break loose, or where it would hit before its warning (`RunawayRules.SpawnBlocked`, the
    relief cart's clearance rule: `none (a body on the line behind the train: …)`).

  Each tick it warns first and then runs over (`Warn`, then `RunOver`), smashes timber on its own line (`SmashAhead`) and runs over a body
  on the line as the traffic runaway does. Companions never pump for it (Ruling 11): pumping to stay ahead is the players' job.
  - **The ram** (Ruling 12). It comes with the train rolling and the runaway's front within `RamReach` (0.25 m) of the last body. The
    plating loses `RamDent` (40, `RailController.Smash(40, speedKept: 1)`, which wrecks at 0 like any dent; a wrecking ram applies the
    ordinary wreck toll, which has no 1 HP floor). Each seated hero who is up loses `RamHpFraction` (20%) of max HP × `PartyDamageScale`
    / 3, never below 1 HP. NPCs on the bench and in the wagons are only jolted, and nobody leaves a seat ("riders are thrown" means
    shaken in their seats: a rider thrown out at speed would land on the line in front of the runaway). There is one ram per runaway: it
    derails and is freed (`[rails] runaway rams the train at S=…: plating {a}->{b}; heroes hit {n}`). A train stuck in Rolling at V≈0 can
    be rammed; the watchdog frees it within seconds.
  - **A held train is never rammed** (Ruling 13). With the party's cart held at a stop or halted on the line, the runaway brakes to halt
    `HaltShort` (3 m) behind the last body (`[rails] t=… runaway {id} halts at party S=…, its front … m behind the held train`) and is
    freed a second later. A ram while the train is still braking into the stop stands.
  - **Freed at once** (Ruling 14) by a wreck (a fall too), a restore (a wipe's rise or a load), a chaser or the relief cart on the line, a
    new departure, the party on foot and the controller leaving; also by its own stall (`OwnProgressStuck`) and at the end of its line
    (`[rails] t=… runaway {id} freed (…)`). An arrival lets it halt first. A re-armed ride sends a fresh one.
  - **On a client** it is a copy built from `CartStateDto` (kind `Runaway`: track 0, then the spur's track), corrected five times a second
    like every cart, and ringing by the server's rule (`RunawayRules.Rings`). A client shows a ram only from the server's
    `CartFlags.Derailed` on the gone runaway on track 0 (`RunawayRules.ShowsRam`): the jolt and `timber_smash` at its copy, and
    `[net] ride: the runaway rammed the train here (…)`. A halt's free carries no flag, so a client never shows a ram the server did not make.
- **The split's points and spur** (Rulings 3 and 4). The `split` piece is built from the layout's points (`RailSplit.Points`, see
  "Crevasses"), measured on the party track, as a `SplitSwitch`. It is live only when:
  - the piece's At lies within `SplitRules.AtSlack` (8 m) of the points;
  - the spur's end lies over the crevasse a metre inside each edge (`SplitRules.Live`), and the built end's cell is a drop;
  - the slag line beside the fall's place borders the drop;
  - the lever has a walkable place clear of the spur.

  Otherwise it is dead: a marker only, with no spur, no lever and no runaway. The build line says which:
  `[rails] split {id}: points at party S=…, lever at S=…, spur … m, its end over the crevasse S=…..… (live | dead: …)`. The spur is a
  party-line spur track numbered like the ride-5 spur (`CartStateDto.Track` 2 + k; k counts the pieces with a spur or a split in piece order,
  so `r5_spur` stays track 2). From the points it follows the path `Cross` blends along (smoothstep over `SwitchStretch`, 6 m, onto the
  slag line), then bends `SpurOut` (3.5 m) further out over the next 5 m (`SpurLength` 11 m), ending over the crevasse with its rail ends
  bent down; timber in its way is taken down. Its post carries two warning lamps (amber and red), and a `DropGlow` ember light sits under the
  spur's end (Task 7's dressing fix, Ruling 23). On floor 12 the points are at S 863.6, the lever at S 866.6 on cell (59,10) (walkable, no
  drop, 1.4 m or more from the spur), and the spur's arc is 13.6 m long, ending over the chasm at (61,12); the crevasse is S 865.6–881.6.
- **The lever** (Ruling 5, ruling R-bend). It is deadly (`TrackSwitch.Deadly`: layers 1|8, so no hit moves it, and only a human's E does)
  and stands `LeverPast` (3 m) past the points, on the party lane's side away from the slag line. Its seated window opens at the points
  less the straight run before them, at most `SplitRules.WindowAhead` (12 m) (`WindowReach(StraightBefore)`: no corner lies inside the
  window, so a press is never made round a blind bend), and it stays open until the runaway's front takes the points. It is offered only
  while the ride's runaway is coming (`SplitRules.Offered`; on a client, while a runaway copy runs on the party line short of the points)
  and never on foot (`Withheld`). On floor 12 the straight before the points is 5.0 m (the bend at S 853.8–858.7, ride 4's old bend moved up with the
  ride's +0.64 m), so the window opens at S 858.6. The prompt (`SplitRules.Prompt`, through `PropPromptRules.SplitPrompt`) is "Throw the split — it throws as we pass" short of the
  points, "Throw the split behind us" past them, and "Set the split back" when thrown.
- **The party's branch** (Ruling 6) is decided as its front reaches the points (`SplitRules.PartyRoute`):
  - thrown, with an able human aboard (`SwitchBackRules.AbleToThrow`): the spur. The cart calls `Cross(points, NaN)`, the log says "The cart
    takes the spur!" (`[rails] split {id}: the train takes the spur at S=…`), and the train falls
    (`Fall("The cart runs off the spur into the crevasse!")`, see "Crevasses") with its front at the points + `FallPast` (7 m), wholly on
    the slag line beside the drop;
  - thrown, with no able human aboard (ruling R-lone): the main line, "No one could set the split back — the cart keeps the main line."
    (`SplitRules.SafeLineLog`), the lever left thrown for the runaway;
  - straight: the main line, the whole train following the cart (`[rails] split {id}: the train passes straight at S=… (front …)`).
- **The hold** (Ruling 7 and the controller's rulings). A throw pressed with the front short of the points by at most
  `SplitRules.HoldReach` is held (`TrackSwitch.HoldThrow`; "The split will throw as we pass.", `SplitRules.HeldLog`, shown on every peer;
  `[rails] Split_{id}/…: {name}'s throw held to the points`) and applied the instant after the party's branch is decided straight
  (`[rails] split {id}: {name}'s throw held to the points (… m short): thrown behind the train at front S=…`; `{name}` is the last to press
  it, since a held re-press takes the throw over). `HoldReach` is every press
  the server takes short of the points (`PressReach`: the window and `SeatedSwitchRules.AcceptLead`, 5 m, before it) and never under
  `HoldLead` (5 m): 10 m on floor 12. So a press before the points never sends the party down the spur, and on floor 12, whose whole
  pre-points window lies in the hold zone, no press can put the party on the spur at all; there the wrong choice is not throwing it (the
  ram). A press at or past the points while the party's branch is still undecided is held too. For `HeldGuardSeconds` (1 s) after a held
  throw goes in, a set-back is ignored (`SplitRules.HeldGuards`), so a second press just past the points never undoes it. A set-back is
  never held and never refused short of the points: plan 8's set-back guard applies to the split only past them (`GuardsSetBack`). The
  server takes a remote seat's press from its early lead (`InSeatedWindow` through `SplitAccepts`, `PressReach` short of the points) and
  holds it, not drops it.
- **The runaway's branch** (Ruling 8). As its own front reaches the points it takes the lever as it stands, and the lever is `Taken` (told:
  the window ends on every peer; `[rails] split {id}: the runaway takes the spur | passes straight at S=…`). Thrown, it goes onto the spur's
  track once its middle is at the points, coasts off its end and goes into the crevasse quietly: a thud and `cart_crash` where it goes,
  "The runaway goes off the spur into the crevasse!" only with a hero within `PlungeHeard` (40 m), and
  `[rails] t=… runaway {id} goes off the split's spur into the crevasse at spur S=… of …`. A runaway committed to the spur never rams.
  Straight, it stays behind the train. A client reads the plunge from the gone runaway's `Derailed` flag on the spur's track with its last
  S at the spur's end (`SplitRules.PlungedOffSpur`): `[net] ride: the runaway took the spur here and plunged (…)`.
- **The calls** (Ruling 9). Each comes once an arming, only while the runaway is coming, through `RailCalls.Say` as a Shout with
  subtitle-only stock lines in `RideBarks` (`BarkTrigger.SplitWait`, `SplitNotYet` and `SplitNow`, appended after `SwitchBackNow`, as
  `Runaway` is):
  - "Let us pass the split, then throw it!" at the window's start with the lever straight;
  - "Not yet — set it back, that's the spur!" with the lever thrown and the front from 12 m short of the points to the window's start (or 5 m
    short, whichever is farther): 12–5 m on floor 12. No press throws it there (a press 10–5 m short is held, and one farther out is not
    taken), so only a lever thrown some other way meets it, such as the test's hook;
  - "Now — throw the split!" with the front `NowPast` (1 m) past the points and the lever straight.

  Each prints `[rails] split {id} called wait | not yet | now at front S=…; bark=…`. Companions and hero bots never throw it.
- **The arrival's reset.** An arrival sets the split's lever back with the other finished levers, except while the ride's runaway is live
  short of the points: then the reset waits (`[rails] split {id}: the arrival's reset waits for the runaway …`), so a correct throw
  behind the train still sends it down the spur, and goes in when the runaway takes the points or is freed.
- **The wire** (Ruling 19). Nothing new goes on it and the protocol stays 17. The lever's thrown and taken states go by `PropEvt` and
  `ThrownSwitches` as `switch:{piece}`, the runaway by `CartStateDto`, the party's spur by `CrossOut` and `Fell`, and the dark comes from
  the plan. The held press is the server's alone.

`--test-runaway[=tail|ram]` and `--test-split[=after|early|hold|stall|lone]` (`--test-runaway=` with a split variant runs it too;
`DebugTools.Runaway.cs`, `_quitAfter` 300) play floor 12 at lethality 3, solo, every enemy culled as it appears. The cart is restored at
stop 3, everyone boards and it departs on ride 4. They print `[runaway]` lines and a `[runaway] RESULT` (tail, ram), or `[split]` lines
and a `[split] RESULT` (the split's five). With no variant, `--test-runaway` and `--test-split` both run `=after`.
- `=tail`: the lead pumps; the runaway breaks loose with its front 14–16 m behind the train's last body and its call is
  heard; the gap never shrinks under 3 m; at stop 4 it halts 2.5–4 m behind the last body (measured: the least gap 14.9 m, the halt 3.00 m)
  and is gone within 2 s; nobody is run over (`[runaway] RESULT ok (tail)`).
- `=ram`: the train's four wagons coupled (plan 14: no coupling by the test, the check reads `Train.Count` 4), nobody pumping; one ram: the plating 100 → 60, each seated hero up −20% (±1) and none
  under 1 HP, the seated NPCs unhurt, nobody unseated, `RunawayRams` 1 to stop 4; then restored at stop 3 with the plating set to 30, the
  ram wrecks the cart, the runaway is gone by the wreck's event and a spare takes over (`[runaway] RESULT ok (ram)`).
- `=after`: the seat offers the split from the window's start with the wait call there, the now call 1 m past the points, and the lead
  throws it from the seat 1–3 m past them: no cross, the runaway takes the spur and goes into the crevasse within 3 s (measured at plan 14
  Task 8's sweep, `17-test-split.log`: thrown 1.0 m past the points and into the crevasse 2.02 s later; before plan 14: thrown
  1.1 m past the points, the runaway at the points at S 863.7 and into the crevasse 1.75 s later), no ram, stop 4. It then checks the light
  is all back at stop 4 and, in a second pass with the runaway held, after leaving floor 12 for floor 13 from the dark's middle.
- `=early`: a press from the seat 9 m short throws nothing (the seat does not offer it there), while a remote seat's press taken as the
  server takes an `InteractIntent` (`InSeatedWindow`, then `LateFor`, then `Interact`) is not taken 10.5 m short (half a metre short of the server's early lead, `SplitRules.PressReach` of the window: 10 m) and is held
  to the points 9 m short (a metre inside it, still short of the window; the flow derives both from `PressReach`); the test's hook
  (`DebugThrow`) then throws it, "not yet" is called, the train takes the spur and falls with its front within 1 m of the points + 7, every rider fallen within 1 s; the wipe rises
  at stop 3 and the next departure sends a runaway again.
- `=hold`: thrown 9 m short by the hook, set back from the seat at the window's start (taken at once), then thrown from the seat short of
  the points: held, thrown just after the train passes straight, a second press just past the points ignored; the runaway takes the spur.
- `=stall`: thrown by the hook, the train caught 7 m short of the points (`RailController.DebugStall`); the watchdog sets the split back
  and grinds on to stop 4, never onto the spur.
- `=lone`: thrown by the hook, the only human (possession off) downed in the seat: at the points the safe line, the lever left thrown, and
  the runaway takes the spur.

Every variant checks that nobody falls but the train's riders. The flows that are not about the runaway (`--test-crew`, `--test-chase`,
`--test-ride`, `--test-switch-back`, `--test-barricade`, `--test-crevasse`) hold it (Ruling 22, `RailEncounterController.DebugHoldRunaway(4)`)
and log `[<flow>] the runaway on ride 4 held by the test`; the RIDE pass keeps it real.

**The legibility gate** (Ruling 23, in every variant above, headless too). At the build every `RailLamp{i}` inside the dark is hidden with
its lantern dark, and some lamp outside it is lit. Layer-1 rays from the seated lead's eye, with the front 8 m short of the points to the
split's two lamps and 3 s after the runaway breaks loose to its lamp (`[runaway] sight: …`), must be clear, and a ray that never ran fails
the variant. Windowed `=after` with `--screenshot=PATH` saves the seat's views as `PATH_lit`, `_dark` (both facing ahead, level, the HUD
hidden), `_split`, `_window`, `_lever`, `_behind` (the rearmost view turned back down the line), `_behind_nolamp` (the same with the
runaway's lamp, glow and lens hidden) and `_behind_seat` (the lead's eye turned 180°, shown only), and judges them on the scene crop (x 20–80%,
y 16–53%, the HUD out): the dark's mean luminance at most 0.6 of the lit shot's, at least 20 red pixels about the split's red lamp, and at
least 20 orange pixels about the runaway's lamp that go when its light is hidden (an A/B, so the rail's own glints are not counted).
Measured (Task 7): lit 0.095, dark 0.050 (0.52; 0.58 after the A/B fix moved the dark shot to S 838.8), the split's lamp 80 red pixels,
the runaway's 37 orange with its light and 0 without, the rays clear (the lever, the spur's end, the split's lamps at 11.1 and 20.5 m, the
runaway's lamp at 17–22 m), the 8 lamps where the dark is full hidden with their lanterns dark (of the 10 out; the flow does not judge
the 3 that stand in its ramps) and 104 lit outside, and the light back at stop 4 and on floor 13. Since the exposed-track fix (its Task 5)
the gate's two places are held and aimed: the cart is held still (V = 0, its S pinned with the front exactly there) 10 m before the dark and again
at the dark's middle until both of that place's shots are saved (the runaway behind is pinned too, so a long hold cannot let it close
in and ram), the seated eye is aimed along the party track's tangent, level, and each place is
shot twice, with the party cart's own head lamp (`Lamp`, `LampGlow`, `LampLens`) hidden and lit, each drawn 30 frames so the
volumetric fog settles. The gate judges the world's light, not the party's lamp: only the lamp-hidden ratio is held to 0.6; the ratio
with the lamp lit is logged. Measured over two windowed runs: lamp hidden 0.422 and 0.422 (lit 0.0706–0.0709, dark 0.0298–0.0299),
lamp lit 0.74 and 0.75, the straight before the points 5.0 m and 10 lamps out (8 judged). Before the hold a shot's S followed the shot queue: with
the lamp lit the ratio read 0.59 a few metres past the middle and 0.76 at it, and with the lamp hidden (but unheld) 0.52 or 0.61 with
shot order alone.

Ride 4's cavern: the collapsing bridge, the leap and the slag line's drop (plan 13; `RailEncounterController.Bridge.cs` and
`.HeroLeap.cs`, `Rails/SetPieces/CavernBridge.cs` and `SlagDrop.cs`; the numbers in `Rails/Core/BridgeRules.cs` and `LeapRules.cs`). Two
set-piece kinds, appended after plan 12's: `bridge` (`r4_bridge`, At 0.5268) and `leap` (`r4_leap`, At 0.5664). Both are built from the
layout's cavern (`RailLine.Cavern`, 238..259 on floor 12), not from their At; the At values (four decimals) are Task 2's own measure of
the shipped layout: the cavern's party S 903.06..991.06 (22 cells, 4.0 m each), the drop's near edge at 975.06, a 62.00 m stretch, and
stop 4 31.41 m past the cavern. `--print-map` prints it as `[map] rail cavern: ride {r} party S={from}..{to} ({n} cells, flat
{True|False}), drop {a}..{b} at party S={dp} (enemy S={e0}..{e1}), stretch {len} m, stop {r} at S={stopS} ({d} m past)` (floor 12: `ride
4 party S=903.1..991.1 (22 cells, flat True), drop 256..257 at party S=975.1 (enemy S=999.1..1007.1), stretch 62.0 m, stop 4 at
S=1022.5 (31.4 m past)`), and `ChasmDressing` its count of cavern deck looks as `[chasm] regions=12 decks=50 planks=3 cavern=44` (one
`DeckLook_{x}_{y}` per cavern deck cell, both lanes: see "Crevasses", gone decks). A cavern is `flat` (`LeapRules.Flat`) when every cell of
its span is at one level, none is a ramp, and neither lane's cell just before or just after it is a ramp cell; a cavern that is not flat
has neither a live bridge nor a live leap (each logged `(dead: …)` and left a plain deck).
- **The bridge** (Rulings 2–8). The cavern's party-lane deck cells. Live when its At lies within `AtSlack` (8 m) of the span's start on
  a flat cavern: the party track is cut bare over the span (`RailTrack.CutMore`) and `CavernBridge` lays each cell's rails
  (`RailTrack.LaySpan`, meeting the track's within 1 mm at both lips). Build line: `[rails] bridge r4_bridge: party S=903.1..991.1, 22
  deck cell(s), over the crevasse True (live)`. As the cart's front comes onto it "The bridge groans under the cart." (`trestle_creak`).
  `Grace` (1.5 s) after the train's rear (`RunawayRules.TrainRearS`) is on it, it starts to go from its start, called by a seated
  companion (`BarkTrigger.BridgeGoing`; with nobody to speak the log says "The bridge is going behind us — keep her rolling!"): `[rails]
  t=… bridge r4_bridge goes from S=903.1 at {pace} m/s ({w} wagon(s)); called by …`. Its front runs at `Pace` = `PaceShare` (0.8) ×
  `CartPhysics.TargetSpeed(0, wagons)` (4.8 m/s empty, 4.17 m/s with three wagons), fixed as it starts, and a cell goes as the front
  passes its far edge (debris and `timber_collapse` every `CueEveryCells` cells; `[rails] … bridge r4_bridge: {n}/22 gone, front S=…`).
  Unbraked, the front holds `HoldShort` (2 m) behind the train's rear (a stall, the watchdog, a heavy load or the stop's own braking:
  `[rails] … held by the stall guard at S=…`). It takes the train only while it rolls and a seated player of the party's cart braked within
  `BrakeMemory` (2 s), with its front at the rear (`BridgeRules.Catches`).
- **The brake's reaction window** (Task 3 review I1). A brake counts toward the take only when it was pressed `ReactionTime` (1.5 s) or more
  after the call (`BridgeRules.Counts`, the press stamped by `RailController.BrakePressedAt`; a brake held on is one press however long it
  is held, through a pause or a frame hitch alike, `BridgeRules.NewPress`, and the braking flag is cleared whenever the cart leaves Rolling,
  so a brake held into a stop is a fresh press on the next ride). A train braked onto the bridge before its warning is held short and
  never taken by that brake, however long it is held; a fresh press after the window takes it. A player braking with the front within
  `BrakeCallNear` (12 m) of the rear is told "Don't stop — it's going under us!" (`BarkTrigger.BridgeBrake`), once per arming. Companions
  never brake, so a solo player is never killed by a brake they could not know was theirs.
- **The seen fall** (`BridgeRules.FallSeen`, 1.4 s; the legibility round). A take is `RailController.Fall(…, seen: true)`: the cells under
  the train go with it, the cart's look, the wagons' and each seated hero's (`Adventurer.FallLook`, riding down with its own car's look:
  the cart's riders with `CartDrop`, a wagon's with that wagon's `WagonDrop`) drop through the gone cells on rigid bodies, each seated
  player's eye rides down with its car (`FallEye`; plan 14's final review M2: a wagon's rider is shown the fall as a cart's rider is, on
  every role; `--test-cavern=brake` seats a companion in W1 for its take and finds its look on a `WagonDrop`, the lead's on the
  `CartDrop`), and the wipe is held for it (`PartyController.HoldWipe`, read as
  `WipeHeldUntil`); the crevasse takes the riders at once, as any fall. A headset is not shown the fall (comfort: `FallEye` is skipped
  while `Vr.Active`, so the rig keeps its view; the final review's M10). The eye lets go 50 ms before the held wipe is judged and hands the
  view back to the local hero's camera (on the rebuilt floor too: Task 6 review I1). A client shows the same fall from the cart state's
  Wrecked edge with `Fell` over the bridge's span; its own taken player, whose unseat pose (sent at the take) reaches it before that
  state and whose Fallen sheet comes after it, still rode the train when it sat in the train (the cart or a coupled wagon, by `CarOf`) at
  the last applied state or on a frame since, or is told fallen within a cell of the car it rode (`BridgeRules.LocalRode`), and its eye
  starts where it was in the seat, riding down with that car (RIDE run, plan 13 Task 7). A wagon rider's eye on a client is unexercised
  (the RIDE pass's taken joiner rides the cart). `[rails] the train falls at S=…
  (The bridge goes out from under the train!): … seen for 1.4 s`.
- **What ends it** (Ruling 5). After an arrival it runs on to its end (every cell gone a few seconds after stop 4). A wreck or a fall stops
  it where it stands. A `Restore` (a wipe's rise or a load), a spare's takeover or ride 4's next arming stands it whole again (`[rails] …
  bridge r4_bridge stands whole again (…)`); after the bridge's own take with a hero still standing, the spare's takeover stands the bridge
  and the drop `FallSeen` (1.4 s) later, once the train has been seen falling through the gone cells (the final review's M5). While the train rolls on it, at any speed, nobody gets out (`HoldsRiders`; "Get out" is
  neither offered nor taken, remote seats included; the final review's I2 amends Ruling 6's 1 m/s: a train braked onto the bridge is held
  short alive, and a rider let out there would stand in the one-cell pockets the collapse then islands). A wreck, a halt and a held cart are
  not Rolling and are untouched; a stalled train is still freed by the watchdog. The ride's runaway whose front reaches the gone span goes down with it, freed plainly
  (neither rammed nor plunged; "The runaway goes down with the bridge!" only with a hero within 40 m; `[rails] … runaway r4_runaway goes
  down with the bridge at S=…`).
- **The leap** (Rulings 9–15). The stretch runs from the cavern's start + `LeapIn` (10 m) to the drop's near edge (S 913.1..975.1; the piece's own S, 913.0 in the probe, is its At's 913.04 against the cavern's 903.06 + 10); the leap
  is dead with no cavern, a stretch that starts short of it, or one under `MinStretch` (30 m). "Leap across" is offered, whatever the aim
  (as plan 8's seated window), to a human (`LocalHuman` or `Remote`) who is up and seated in the party's rolling cart while the stretch is
  live and not yet called, with a ride's slag cart in reach carrying fewer than `MaxLeapers` (2) heroes: the rim gap |lateral| − 2 ×
  `CartRules.HalfWidth` ≤ `Reach` (2.6 m) and |along| ≤ `AlongReach` (1.2 m), between the cart centres in the party cart's frame (twin lanes 4 m
  apart give 2.5 m). "Leap back" is offered on a slag deck while the party cart is in reach with a hero seat free and the slag cart's front
  is `BackCloses` (3 m) or more short of the drop. The server takes a press with `AcceptSlack` (1 m) more on both, and a leap back up to
  the drop's very edge (`LeapRules.BackAccepted`). A leap is `Adventurer.SitIn` onto one of the slag cart's two deck seats
  (`SlagCart.DeckSeat`: its party-side rim at heap height), never a get-out: `cart_jump` then `cart_land`, a small shake, the log
  "{name} leaps onto the slag cart!" / "{name} leaps back into the cart!", and `[rails] leap: {name} across onto slag cart {id} (r4_slag)
  seat {k} at party S=…` / `back into the cart seat {k} …`. A leaper is not aboard the party's cart: every party-cart judgement reads
  `Cart == _rails.Cart` (the departure, pumps and brakes, `AbleToThrow`, the fall's toll, the arrival's set-down, the rim spots). Its
  shoe takes no hits while a hero rides it. Freed any way but a wreck or a restore (the watchdog, an arrival's or a departure's clear, the
  line's end), each leaper is first hauled back into a free party hero seat (`HaulLeapers` → `LeapBack`, counted in `LeapHauls`; the final
  review's M6: Ruling 17's haul extended to Ruling 15's frees, so nobody is left in a pocket the collapse then islands); one with no seat,
  and every leaper of a rammed, wrecked or restored cart, is set down quietly on `CrevasseRules.NearestSetDown`. The fight from the deck is the existing seated combat.
- **The slag line's drop** (Rulings 16–18). The slag deck's 2 cells (`DropCells`) 2 short of the cavern's end (`DropFromEnd`), cut bare on
  the slag track and laid by `SlagDrop`. With the party's front `CallAhead` (25 m) short of it, once per arming, its cells give way
  (debris, `timber_collapse`), a red lamp with a halo lights at its near edge (stronger than the broken bridge's), the ride's slag carts
  ring their bells and a seated companion calls "The slag line's going — leap back!" (`BarkTrigger.SlagDrop`): `[rails] … leap r4_leap: the
  slag line drops at enemy S=999.1..1007.1; called by …; {n} hero(es) on a slag deck`. No leap across is offered after it (a client's from
  the drop's `PropEvt`). A slag cart whose front reaches the drop goes over: its creatures die to the crevasse, an able human still on
  its deck falls with it (back at the next stop, at 25 % HP), and any other hero there (downed, or run by the AI) is hauled back into a
  party hero seat ("{companion} hauls {name} back across!"): `[rails] … slag cart {id} (r4_slag) goes over the drop at enemy S=…: {r}
  rider(s), {f} hero(es) fell, {h} hauled back`. The drop stands again at ride 4's next arming, a `Restore` or a spare's takeover; while
  it is down and the party's cart is past it, traffic starts just past it (`LeapRules.TrafficStartS`).
- **r4_slag** (Offset 0: side by side) starts at party S 848.0 (At 0.3086), 40 m ahead of its pace point round the hairpin, where no
  seated eye can see it (a sight-ray test from every seat; Task 6's legibility round), and the party comes up to it. An Offset-0 pacing
  cart behind its pace point catches up at up to `PaceRules.AlongsideCatchUp` (6 m/s; its catch-down unchanged), so "Leap across" is
  offered 0.00–0.01 s into the stretch; one anywhere ahead of its pace point (inside the alongside radius too) is left alone by the slag watchdog (`CartWatchdog.WaitsAhead`).
- **The holes read from the seat.** An ember glow rises out of each gone bridge cell and out of the drop, with a haze quad at deck level
  (`BrokenBridge.EmberGlow`, `EmberHaze`, shown while the cell is gone); the drop's cells keep the slag deck's gravel bed.
- **The calls' copy** (Ruling 19). `BridgeGoing`, `BridgeBrake` and `SlagDrop` are appended after `SplitNow`, all Shouts, with
  subtitle-only stock lines in `RideBarks` (no voice). There is no leap-across call: the prompt is the offer.

`--test-cavern[=bridge|brake|early-brake|stall|runaway|leap|wreck|stay|lone|creep]` (`DebugTools.Cavern.cs`, `_quitAfter` 300) plays
floor 12 at lethality 3, solo, every enemy culled as it appears (r4_slag's riders spared, harmless, in the leap variants); the cart is
restored at stop 3 (`rc.Restore(3)`) and departs on ride 4, the runaway held but in `=runaway`. Every variant prints `[cavern]` lines, the
timing line (`[cavern] timing: bridge call t=…, earliest take t=…`, seconds since the rear came onto the bridge), and `[cavern] RESULT ok
({variant}): …`:
- `=bridge` (the default): nobody presses; the call with the collapse 1.5 s or more after the rear came on, its front never within 2 m of
  the rear (measured 7.99 m at plan 14 Task 8's sweep, `05-test-cavern.log`; 9.18 m with the empty train before plan 14), no "Get out" while it rolls on it, every cell gone within 6 s of the arrival, a restore stands it whole.
- `=brake`: the lead brakes 2.0 s after it starts (0.5 s past the reaction time): "Don't stop", the take 1.5 s or more after the call by a press 1.5 s or more after it
  (measured 4.97 s and 2.04 s at plan 14 Task 8's sweep, `06-test-cavern=brake.log`; 4.53 s and 1.64 s before plan 14), every rider fallen within 1 s, the wipe rises at stop 3 with the bridge whole and the next departure reaches
  stop 4; the camera after the rise is the local hero's.
- `=early-brake`: braked from a cart's length short of the bridge through the call + 3 s: held short and alive, no take; held there the
  lead's prompt is "" and a "Get out" press is refused; let go 1 s, then a fresh brake (4.00 s after the call): taken only after it
  (4.45 s after the call, the brake pressed 4.02 s after it), then as `=brake` (plan 14 Task 8's sweep, `07-test-cavern=early-brake.log`).
  **Re-pinned in plan 14 Task 1** (`DebugTools.Cavern.cs`, its `early-brake` case), the plan's one moved window: the trigger was the
  front reaching the bridge's start (S 903.1, the groan), where the empty train's rear stood at S 900.9; with W1..W4 coupled a brake
  there stops the 12.6 m train with its rear about 10 m short, so the rear never stamps on and no collapse starts (Task 1's measured
  failure: "the collapse never started under the braked train"). The trigger is now the train's rear at `from − 2·HalfLength` (S 900.9,
  where the rear was when the cart alone groaned), which puts the front at S 913.5 (`[cavern] early-brake: the lead brakes with the rear
  a cart's length short of the bridge (front S=913.5, rear S=900.9, v 5.22; the groan came with the front at S=903.1)`). Every check
  the variant judges is unchanged; before plan 14 the fresh brake came 4.01 s and the take 4.55 s after the call. **Its margin is thin**
  (a known gap, plan 14 Task 1's review): braked from that trigger, the 12.6 m train's rear comes onto the bridge at about 1.06 m/s and
  stands 0.11 m on it (`… held short and alive (front S=903.1, rear S=903.2, gap 0.11 m …)`; the empty train's rear came on at 3.10 m/s
  and stood 0.86 m on it), so a small change to the brake or the cruise turns the variant into "the collapse never started". A brake at
  the groan itself, the player's natural one, is in no flow for the long train (it stops the rear about 10 m short: nothing starts).
- `=stall`: caught on it (`rc.DebugStall`) nobody braking: held 2 m short (least gap 2.00 m), no take, the watchdog frees it to stop 4. The collapse
  runs on past the arrival, and the train may leave stop 4 meanwhile, so the flow throws ride 5's pit switch on the way (plan 15; without it
  the train fell into the pit and the collapse stopped where it stood, 14 of 22 cells gone).
- `=runaway`: the lead pumps, the split straight: the runaway goes down with the bridge within 8 m of its start, neither rammed nor
  plunged, no ram.
- `=leap`: "Leap across" within 2 s of the stretch (along ≤ 1.2 m), never to a companion; across, the lead is off the party cart's book
  and its stick pumps and brakes nothing; a seated blow from the deck lands; "Leap back" on its first frame after the slag line's call;
  r4_slag goes over 1.5 s or more after the call (4.79 s at plan 14 Task 8's sweep, `10-test-cavern=leap.log`; 4.16 s before), 0 heroes fallen, its riders dead; stop 4 with one leap each way.
- `=wreck`: across, then the train wrecked 1 s into the collapse: the lead set down up on footing (`SetDownOk`), the collapse's front still.
- `=stay`: across and stays: falls with the slag cart, the train does not; back at stop 4 within 1 s at 25 % HP.
- `=lone`: possession off, downed on the deck before the call: hauled back into a party seat at the drop, the hauler's line logged.
- `=creep`: as `=leap` after stopping before the cavern (party S 855), creeping 0.5 m or more and standing 12 s: r4_slag, waiting ahead, is
  still on the line and "Leap across" is still offered.

**The cavern's legibility gate** (Ruling 23, Task 6; every variant, headless too). Layer-1 rays from the seated eye, each with a control
0.3 m under the deck at its target that must be blocked no more than 1 m short of where it crosses the deck: the bridge's front (+0.5 m) 1 s
after the collapse starts, the slag cart's deck seat 0 on the first frame "Leap across" is offered, the drop's near edge (+0.3 m) at the
slag line's call from the leaper's eye, and r4_slag's spawn, which must be blocked from every seat (`[cavern] sight: {name} {clear at d
m|blocked by X at d m}`, `… control blocked by X at d m`). A blocked ray, a vacuous control or a sight that never ran fails the variant;
the RESULT ends `seen from the seat: r4_slag's spawn blocked from 4 seat(s); slag cart clear at 3.4–3.5 m; bridge front clear at 2.9–15.1 m;
drop clear at 25.5 m` (the distances of plan 13 Task 7's sweep; plan 14 Task 8's ten variants read the slag cart clear at 3.4–3.6 m, the
bridge front at 12.3–23.8 m and the drop at 25.6 m, `=early-brake` now printing a slag-cart sight too). Windowed `=leap --screenshot=PATH` saves held A/B twins (the party's cart and the ride's slag carts pinned in S and V
until both are saved, 30 frames drawn before each, the HUD hidden): `PATH_behind`/`_behind_standing` (front at the bridge's start + 30 m,
the collapse held short; the gone cells shown, then stood with their embers out), `_side`/`_side_nocart` (the first offer), and
`_drop`/`_drop_standing` (the slag line's call; the standing twin hides the lamp, halo and embers); for viewing also `_spawn_before`/
`_spawn_after` (party S 847 and 849), the joins (`_bridge_lip_from`, `_bridge_lip_to`, `_drop_lip_near`, `_drop_joint`, `_drop_lip_far`),
`_deckseats`/`_deckseats_marked` and `_stop4`/`_stop4_traffic`; windowed `=brake` shoots the take as it falls (`_take_seat`, `_take_side`,
`_take_seat_late`). It judges `[cavern] shots: behind diff a px; side diff b px; drop diff c px, drop ember e px` over x 20–80 %, y
16–80 % (Rec. 709 luma moved > 0.1) with the bounds behind ≥ 10 % of the scene crop (`CvBehindDiffShare`: 35,404 px at 1280 × 720, about half the measured; it scales with the window), `side and drop >= 400`, `ember >= 20`, the
drop's embers counted only inside its projected rect, never the slag cart's heap. Measured (Task 6, final windowed `=leap`): behind
71014 px (421 before the ember glow), side 48749, drop 14144, ember 139 px in a 62 × 39 px rect; plan 12's split gate re-run windowed:
dark/lit 0.420–0.433 with the party's lamp hidden.

The golem (the last ride, 5, `RailEncounterController.Kiln.cs`). As the cart reaches the golem's `At` (0.49) on that ride, an elite forge golem, "Kilnheart the Hulking",
bursts out of the kiln wall onto the enemy line (a thud, a shake, dust). It is never a boss: it has the slim elite bar and
its death opens nothing. It is carried by an unseen runner paced beside the party's cart. While it is alongside (or within
6.5 m) it slams the cart every 4 s after a 1.5 s hand glow, for 12 plating and a shake. The spur (`SpurSwitch`) is a dead-end
8 m track off the enemy line into the wall. It bends as far out from the enemy lane as it can (`SetPieceRules.PickSpurBend`): from 1.8 m
(`SpurBendMax`) down in 0.05 m steps (`SpurBendStep`) to 1.1 m (`SpurBend(2 m)`, a lane cell's wall face), it takes the first
bend that meets three tests:
- its footprint (both sleeper ends at every point of its path, the stop beam's corners and the stop's two brace feet,
  `SpurFootprint`) stays `SpurWallMargin` (0.1 m) clear of rock (`Cell.Wall` or `Cell.Secret`) in eight directions (`ClearOfRock`: the
  full 0.1 m from a wall face, about 0.077 m from a rock cell's outer corner);
- its inner sleeper ends at the stop and at its end clear the main line's (`SpurSleeperGap` at least 0; nearer the points the two
  share sleepers, as at any turnout);
- its stop beam, its inner end cut back to stand `SpurMainClear` (0.05 m) off the main line's sleeper ends (`SpurStopOf`; the outer
  end stays 0.8 m out), still spans both of the spur's rails.

If no bend passes, the build logs an ERROR and lays 1.1 m with the full beam. The build line says which:
`spur … m, bend 1.50 m (rock beyond), stop -0.62/+0.80 m, sleeper gap 0.02 m`. On floor 12 the full bend's stop ran 0.10 m into
the wall cube at (48,62), on the curve where the outward runs diagonally, so the spur bends 1.50 m (8.9 m long): its footprint
clears the wall by 0.13 m, its end's sleepers clear the main line's by 0.02 m, and its beam's cut-back inner end clears the main
line's sleeper ends by 0.05 m. Its lever stands between the lanes 2 m short of the party line's points; since plan 15 those points
(S 1200.3) fall at the exit bridge's far edge, so the lever stands past them instead (`LeverPastPoints`: at S 1203.3, its seated window
from S 1183.3); a rider throws it from the seat through the seated window, as ride 3's switch. If the lever is thrown when the runner reaches the points,
the golem goes down the spur into its stop: three big run-overs' worth of damage, then a creature on its own feet. If the
ride ends with the lever thrown and the golem still alive short of the stop, it goes off the spur then, so a thrown lever
always pays off. Once the golem has died on a floor it never comes again (`GolemDefeated`), because an elite pays its loot
even as a reinforcement.

The other pieces of the kiln (ride 5). Three drop hammers stand 3 m apart on the floor's metronome (`DungeonBuilder.FloorTick`). Each
cycle is 4.5 s: up 1.6 s, a red glow for 1.5 s (the warning, `hammer_wind`), a 0.15 s fall, 0.5 s down and 0.75 s to rise.
Hammer i runs i·45 ticks (0.75 s, two beats at 160 BPM) ahead of hammer 0 (`SetPieceRules.HammerIndexOffsetTicks`), so all three fall on the beat of the kiln's music. A hammer that falls on the cart takes 15 plating and shakes the riders (never
mangling a seated rider). A walker under it is crushed by the lethality. The pour runs on a 10.5 s clock: 6 s dry, 1.5 s of
warning (the lip glows, `pour_hiss`), then 3 s of pouring. A cart through the stream loses 20 plating and each seated hero
is burned twice, once per pour. A walker in it burns every 0.8 s. Its quench valve (`QuenchValve`, a layer-4 prop on the side
away from the enemy lane, 3.5 m before the pour) bursts to one hit, and the pour is out for good. The trestle falls 4 s
after the cart's front first runs onto it, and a cart still on it is wrecked; the log says "The trestle groans under the
cart." when the clock starts. The buffer (`BufferStop`) across the line just short of the last stop is smashed by the cart's front,
which does no harm; the cart rolls on into the stop. The hammers and the pour fall on their clocks whether or not the kiln is armed: a body
under them (a hero walking the hall, an enemy, an NPC under a hammer) is hurt any time; the cart is judged only while the kiln
is armed. Their warning cues still play only while the party's cart is within 25 m along the line.

**Ride 5's lava pit and its switch** (plan 15 Task 4, spec B.4; `Rails/Core/PitRules.cs`, `RailEncounterController.Pit.cs`,
`SetPieces/LavaPit.cs`). The layout's pit (`RailLine.Pit`, see "Crevasses") is one party-lane cell cut to lava: cell 282 (61,56) on
floor 12, its lip at party S 1073.9 and its far edge at 1077.9. Every role cuts the party track bare over it (`ReadPit`, before the
pieces: `[rails] pit on ride 5: cell 282 (61,56), lip at party S=1073.9, far edge 1077.9; the party track bare over it`) and dresses it
(`LavaPit`, no collision): the rail stubs bent down at both edges, red lamps at the near and far edges on the side away from the slag
line, and the broken bridge's ember glow and haze. Its plan piece `r5_pit` (kind `pit`) must stand within `PitRules.AtSlack` (2 m) of the
layout's lip (`[rails] pit r5_pit: At S=1073.9 (0.0 m from the lip)`), else an ERROR (the layout's pit stands regardless).
- **The deadly switch.** A `switch_out` on the pit's ride whose points stand `LeadMin` (7 m) to `LeadMax` (40 m) short of the lip is deadly
  (`PitRules.Deadly`). `r5_switch_out` stands at the lip less `OutLead` (8 m): its points at S 1065.9, its lever 5 m before them at
  1060.9 with a steady red lamp on its post (`[rails] switch r5_switch_out points at party S=1065.9, lever at S=1060.9 side -1 (deadly:
  the pit 8.0 m past)`). It is a human's lever in the seated window, as ride 3's live back is: the window opens 15 m before the lever (S
  1045.9), moves car to car are refused while it is open (`MovesRefused`), and a press from the seat throws it. Thrown, the train crosses
  onto the slag line, passes the pit across and comes home over its fixed back, `r5_switch_back`, at the far edge plus `BackPast` (1 m):
  S 1078.9, `(fixed, thrown)`. The crossing logs "The cart takes the switch round the pit." (`PitRules.RoundLog`).
- **The calls** (`PitRules.Call`, `TickPitCalls`). While the lever is straight and untaken: early, with the front `CallEarly` (30 m) before
  the lever, a seated companion's `BarkTrigger.PitSwitch` ("Lava pit on the line — throw the switch!" logged when nobody seated can speak
  it); last, `CallLast` (6 m) before the points, `PitSwitchNow` ("Throw it — now!"). Both are Shout barks, subtitle only, 3 lines per roster
  name (Sable's a woman's), 2 per class and 3 generic, naming no place; the log prints `[rails] … called early|last …`.
- **R-lone** (`PitRules.TakesRound`). At the points the train is taken round the pit when the lever is thrown, or when no human aboard is
  able to throw it (every human downed, or none in the cart): "No one could reach the switch — the cart takes the safe line."
  (`SwitchBackRules.SafeLineLog`). A human seated in a wagon counts as unable (the throw is from the cart's seat), so they get the safe line
  for free (F19, an owner's feel call under "Known gaps").
- **The late press.** Past the points with the lever straight, a remote rider's press is refused as too late and told "Too late to throw
  it." (`SwitchBackRules.Late` through `RailEncounterController.LateFor`, the server's check before its window's); a press on a lever already
  thrown is ignored quietly. Solo, the seat offers nothing past the points, so nobody is told.
- **The guard** (`GuardPit`, after `GuardBreak`; `PitRules.FallArmed`). The fall is armed only when some `switch_out` stands short of the
  lip, every one short of it stands at least `ArmLead` (the 6 m home stretch and a metre) short of it, so the train is wholly across at
  the lip, and each one's own next back is at or past the far edge, so a train taken round comes home only past the pit
  (`[rails] pit on ride 5: its fall armed (switch_out at [1065.9], switch_back at [1078.9])`). Otherwise an ERROR, and the pit is disarmed: a
  correct throw can never fall. A layout pit with no `pit` piece is an ERROR too.
- **The fall** (`TickPit`, server and solo, beside `TickBreak`; `PitRules.Falls`). A train whose front crosses the lip on its own lane (its
  lane blend at the lip under 0.5) falls, once: `RailController.Fall("The cart runs into the lava pit!")` (`PitRules.FallLog`, `[rails]
  the cart runs into the pit at S=…`), a fall as a crevasse's (see "The train falls" under "Crevasses"), and the wipe rises at the kiln's
  stop. `CollectPartyLaneHoles` lists the pit (lip to far edge), so a chaser hops it and a runaway ends short of it
  (`SetPieceRules.RunawayEnd`, `RunawayShort` 3 m).
- **The jump on other floors.** A `jump` piece is still a 5 m gap in the rails (`RailController.SetGap`): seated companions pump while its lip
  is within 30 m ahead (two at most), one calls "Pump it — we have to jump!" at 3 s of look-ahead to the lip, at 6.1 m/s or more the cart
  flies an arc over the gap and lands with a shake, and slower it drops in and is wrecked. 6.1 m/s is one pumper's speed with all four
  wagons coupled (the load counts three at most, `CartRules.MaxLoadWagons`: 6 × 1.2 / 1.15 = 6.26), while an unpumped cart reaches at most
  6.0, so a jump always needs a pump. Since plan 15 a gap is judged only when the train meets its lip on its own lane
  (`SetPieceRules.JudgesJump`: a blend at the lip under 0.5); a lip met across is passed, marked judged and never judged later, and the
  watchdog's gap lip is given only on the train's own lane. Floor 12 has no jump.

**The parked train** (plan 15 Task 5, spec B.5; `Rails/Core/ParkedRules.cs`, `SetPieces/ParkedTrain.cs`,
`RailEncounterController.Parked.cs`). `r5_parked` (kind `parked`) is a cart and four wagons parked on the party line with its front
`ParkedRules.FrontShort` (1 m) short of the exit bridge: `[rails] parked r5_parked: rear S=1170.7, cart S=1182.2, front S=1183.3 (1.0 m
short of the exit bridge at 1184.3), a cart and 4 wagons`. It is live only when its piece's S lies within `AtSlack` (2 m) of
`RearS(ExitFromS, 4)`, else a dead marker and an ERROR (an ERROR too without an exit bridge, or for a second parked piece). Its bodies are a
`MineCart.AsChaser()` cart (`CartIds.Lift` 2100, its wire ref `parked:{piece}`) and four `TrainWagon`s with `OfLift` set (`liftwagon:{k}`,
2100 + k), never in `RailController.Wagons`: chocks under the cart's front wheels, a lantern on W4's back, an alarm that blinks as the
heap's. A parked tub never offers a revive, a Get out or a climb, and a press on it is never a move (every `TrainWagon` reader of the
party's train checks `RailController.WagonAt(w.Number) == w`; `=parked` pins it). Its state (`ParkedState`, every role) is one of:
- **InTheWay** (each arming of ride 5, and every `Restored`): it stands in the party's way, solid. The server's tick (`TickParked`) rings its
  alarm (`cart_bell`), calls it `CallAt` (35 m) before its rear ("Cart on the line — brace!", `BarkTrigger.ParkedCart`, `[rails] parked
  r5_parked called at front S=… (… m to its rear)`), and when the front reaches its rear the cart **smashes** it (`SmashParked`): "The cart
  smashes through the parked train!" logged first, any rider aboard thrown clear onto a free cell outside the island, the toll of a
  barricade's smash (`SmashCost("The parked train", wholeTrain: ParkedRules.SmashReachesWholeTrain)`: the dent and the speed kept, and
  every seated up hero in the whole train hurt, where r1's and r2's barricades hurt the cart's only, `BarricadeRules.SmashReachesWholeTrain`
  false and `BarricadeRules.SmashBooks`), the barricade's CC0 `timber_smash` at its lantern (server and solo in `SmashParked`; a client's
  from its "smashed", `RideSound.PropCue("parked", "smashed")`, at the same point), the five bodies tumbling off the line on layer 0 for
  `TumbleSeconds` (6 s, one helper with the party cart's wreck: `MineCart.Tumble`), then hidden: `[rails] the cart smashes the parked train
  at S=1170.7: plating a -> b, n hero(es) hurt, V v0 -> v1`. A rider thrown clear is never left on its own rail footing or on a taken cell
  (`ParkedSetDown` takes `SetDownNear`'s answer only on such a free cell, else the chosen one).
- **Gone**: smashed, or the lift rolled out of its place. After a smash its place **stays Gone while its tubs tumble**
  (`ParkedRules.Tumbling`, the 6 s from the smash; `ParkedTumbleLeft` for the flows): "smashed" always reaches the wire before "standing",
  even when the smash wrecks the cart with no spare left and a lift is due in that same tick (`[rails] parked r5_parked: Gone (the smash)`,
  6 s later `Lift (a lift is due)`), and the tumble copies never spawn inside a lift standing solid again.
- **Lift**: once the party's train stands held at the last stop (or wrecked past the exit bridge's start) with its rear past the far edge
  (`ParkedRules.LiftDue`), it stands again at its place as the lift. Not while a chaser or relief is on the line, not while its tubs tumble,
  and never while the takeover's latch is set (`ParkedRules.Next`, in this order: `takenOver`, the smash, then a lift due). The latch clears
  on the last stop's arrival, a restore and a wreck of the taken-over train (`ParkedRules.LatchHolds`), so a wrecked takeover lets the lift
  stand again for anyone still outside. Its log line, "Another cart rolls out onto the line by the lava." (`LiftLog`), is said once as it
  newly stands in play (`ParkedRules.SaysLiftLog`): not again after each lift run, not on the step right after a restore (a load at the last
  stop stands it at once) and not with nobody standing (a fall that took everyone: the wipe comes next); a restore lets it come again. It is
  still said at a no-spare wreck anywhere on the floor (the place was in the way, and now a lift: an owner's feel call).

On the wire the state goes as a `PropEvt parked:{i}` word ("inway", "smashed", "gone", "standing"), sent again by name in a joiner's
catch-up, and it is part of the props hash (`RailPropsDto.Parked`, hashed as `|Q{name}` only when set, so every other floor hashes as
before). Protocol stays 17.

**The lift** (plan 15 Task 6, spec B.5 and B.8, Rulings 25–31; `RailController.Lift.cs`). Standing as a lift, the parked train carries
anyone left outside the island in over the bridge. Its five bodies offer "Board the cart" (`ParkedRules.BoardPrompt`) to a player up and
on foot within reach of any of their tubs (`ParkedRules.Prompt`); its cart's own rider gets the chaser's "Get out" (closed for its first
2 s). Boarding (`BoardLift`) is refused while a chaser or relief is on the line (`[rails] lift: refused (…)`) and for anyone seated, down
or not a player. It is one chase at a time (`Chase.Lift`, with `Train` its `ParkedTrain`). The press seats the player and their own
companions within 25 m in its cart, the escorts, and the freed crew on foot within 25 m in its wagons at once (`SeatInLift`,
`TrainRules.SeatOrder`): `[rails] lift: {name} boards (n companion(s), c crew, e escort(s))`. Only bodies outside the island hop in. A
crew member in a lift tub is never home there. Its countdown is the chaser's (5 s, Get out closed 2 s); with no player left in it before it
rolls it ends and stands on as a lift.
- **The run** (`TickLift`): the chaser's run with the four tubs' drag (`CrewWagons` 4), no pumping and no set piece. Its limit
  (`ParkedRules.LiftLimit`) is the nearest of the party train's back less `ChaseGap` (1.5 m) while the party has a live cart, the standing
  buffer's face ahead of it less half a cart, and the last stop. At its first roll its place goes Gone ("gone",
  `ParkedLiftDeparts`): `[rails] lift: rolls from S=1182.2 with n rider(s), its limit S=1217.6`.
- **Over the bridge** (`LiftOverBridge`, `ExitBridgeRules.Holds` on its own front and rear): no Get out, prompt or press.
- **The halt** (`LiftHalts`): held at its limit with the train ahead stopped for good, it sets every rider down on the island's set-down
  spots (`IslandSetDown`, each its own, never a rail cell; with too few the rest share them in turn, `ExitBridgeRules.SpotIndex`; crew follow): `[rails] lift: halted at S=1217.6, 1.5 m behind the train ahead; 1 rider(s) set down
  inside` (with no train ahead it names what it stopped at, "short of the standing buffer" or "short of the last stop",
  `ParkedRules.LiftHaltWhat`). It is hidden `HideSeconds` (2 s) later and stands again at its place within a second (the parked train's
  next tick: `ParkedRules.Next`'s rule 2). `=lift` measures 1.50 m, and the RIDE pass 1.50 m on the server (`NetTestFlow.cs`'s lift check:
  the joiner prints only the cell the lift set it down on; the joiner's own 1.50 m is its chaser's gap behind the parked train's place).
- **Its set-downs** (`LiftSetDownSpots`): a rider who gets out, a quiet unseat and a save's pose are set down beside the parked train's
  place, outside, until the lift's rear is past the bridge's far edge, and on the island's spots after (`ParkedRules.SavePoseS`); never over
  the bridge, never across it. The book is read once for a whole list (`ParkedRules.SetDownOrder`), so riders set down one after another
  keep their own spots (beside the parked place one cell is free, so there they share it). A save mid-bridge loads the rider beside the
  parked train outside, with the lift standing (Ruling 31; `PartyController.CaptureState` and `FloorStateCapture` read `SavePose`).
- **The takeover** (Ruling 27, `TakeOverAt`). With no live cart left (`ParkedRules.TakesOver`: every spare spent and the last wreck not
  past the bridge's start), a press on the lift makes it the party's train: the parked train goes with no tumble and its latch is set
  (`TakenOver`, cleared on the last stop's arrival, on `Restored` and on a wreck), the party's cart stands Held at the lift's cart S (1182.2) with full
  plating and four fresh wagons (`CoupleFresh`), at the last ride's stop by index and **standing past it** (`StandingPast`): no gate
  (`TrainRules.GateClear`) and no cage (`CageHoldRules.Held`'s `standingPast`) holds it, and the presser boards it as at a stop, its
  countdown at once. A second press in the same tick boards the party's cart. Its departure arms the last ride from its place
  (`ArmFrom(ride, s)`: `[rails] ride r armed from S=…: p piece(s) and e encounter(s) short of it passed`), so the golem, the hounds and the
  pit behind it count as passed.
- **The chaser and relief stop short of it** (Ruling 29, `SpareRules.ChaseLimit`'s four arguments): with a live parked train their limit is
  also its place's back less `ChaseGap` (whether it stands or not) and the bridge's near edge less half a cart and `BridgeClear` (0.5 m), so
  neither runs through a standing lift or onto the bridge. A chase ends there once the train is held at the last stop or wrecked inside:
  `[rails] chase-done: the chaser halted 1.5 m behind the parked train's place at S=1168.1, outside the island; it hopped 2 hole(s) in the
  line`, and the log says "The spare cart halts short of the exit bridge." (`SpareRules.PullUpLine`; "The relief cart …" for a relief).
- **A no-spare plain wreck in its span** (`ClearTheLiftsSpan`, `ParkedRules.InItsSpan`): when the wrecked train overlaps the parked
  train's span, every party wagon is uncoupled and hidden, so the lift that stands next tick shares its place with no party wagon.

Saving mid-ride. A ride under way is not saved: a load puts the cart Held at the stop it left with nobody aboard, and the
ride runs again from its start when the cart next departs (`RailEncounterController.Restore` clears every live item and
any lane or gap). Two things persist in `FloorStateDto` (both nullable, so older saves load): `GolemDefeated` (the golem
stays dead) and `ValveBroken` (a broken valve stays broken, for the floor). The heap stands again after the load. A wrecked
cart throws a cosmetic tub that tumbles and is freed after 6 s. With no spare left, the real cart is hidden at the wreck, and a
load brings it back whole at its last stop.

Debug hooks (internal, for the scripted tests): `TrackSwitch.DebugThrow()` throws one lever (the spur's is `SpurSwitch.Lever`), `OreHeap.DebugClear()`,
`RailController.DebugSetPlating(p)`, and the settable `TrafficClock` and `RunawayClock` start a traffic cart or a runaway
wagon now. `RailEncounterController.TraceTraffic` prints the warnings and refuges. For tests to read there are
`SetPieceNode(id)`, `SlagCarts`, `TrafficCart`, `RunawayCart`, `GolemBody`, `GolemRun`, `GolemDefeated`, and the counters
`BeamPasses`, `Rams`, `HammerHits`, `PourHits`, `GolemSlams`, `Leapers` and `Seeking`.

`--test-chase` rides the five rides of the built-in floor 12 at lethality 3 with no AI and prints `[chase]` lines (a 750 s
quit timeout (about 530–550 s a run since plan 14's 5.22 m/s train: 534 s in its Task 8 sweep) and the scripted rides' watchdog). Every number that names the kiln (`LineShape.LastRide`, the ride; `LastRideStop`,
the stop it leaves from) or the end of the line (`Rides`) reads the line's own stop count, and the kiln's pieces are `r{LastRide}_*`
(`r5_*`). The rides between ride 3 and the kiln ride for real through `DebugTools.RideOnTo` (`DebugTools.RideOn.cs`): at each
stop held on the way the stop's holders die by the test's hand (`DebugKillHolders`), the gate rises, the cage is left
(`RideWaiveCages`) and the party boards; `--test-crew` rides them with its own steps instead, opening every cage. At stop 1, before ride 2, it levels the party to `--party-level=N`
(not at the landing: a levelled seated rogue can kill ride 1's lone cinder before the cart runs it over; default 12: the XP curve
aims at level 18–20 after floor 20, and floors 1–11 give about 28% of a run's XP, which is level 11.6–12.9). Each member's
stat points go two to its class's best primary for one to Vitality, and it learns what its tree allows in tree order; no
gear is added. The built-in start puts the party at level 6, so a lower N only spends its unspent points. It plays the condemned branch: each stop's guards die by the test's hand on
arrival, and each cage opens with the orc's keys, so it is a ride test. The test mends the party at each stop and keeps
the floor's own mobs culled. Section 1 rides the whole line. On ride 1 the lone cinder must die under the cart, and the beam
must pass with no damage to the seated riders. At stop 1, with nobody aboard and no ride armed, the test sets the traffic
clock to a quarter second. A companion held on the enemy lane must get "Cart coming!" and then be mangled, and one held in an
enemy-lane pocket must be untouched. On ride 2 all three slag carts must appear and pull alongside. A debug hit breaks the first
one's brake shoe, which must derail it with its riders, and at least one hound must leap. The lead sits idle on ride 2.
Each boarder and who killed it (a hero by name, a crew member or a hazard) is logged, and at the default level or higher at
most one companion may go down by stop 2. Plan 14: the floor check reads W1..W4 coupled and empty at stop 0 (`[chase] 0 the train at
stop 0: wagons 4, train=[1,2,3,4], 0 rider(s) in it, its rear 4.5`); the kiln stop's cage is opened at the landing by the test's hook
(`DebugSeatCrew`), so ride 1 runs with a crewed W1 and `r1_beam` must clear every seated head in the train by 0.05 m (`[chase] 1 beam:
… by 0.14 m`); every other cage the party opens lets its crew walk to the first empty seats, each departure checking they sat there
before it. On ride 2 a hound must board a crewed wagon (the most crew first), every hound that boards a crewed wagon with no hero up in
it must be answered (a seat shot or a move, `ChaseAnswerFor`) and struck by a companion within `ChaseBoarderAnswer` + 3 s (9 s), and no
crewman may go down to a wagon boarder's bite; ride 2 is ridden so twice, the second time with a blade swapped into a rear seat. On
ride 4 the lead must not go down (`[chase] 4 ride 4: heroes down []; companions away from the cart [] …`, the baseline before Task 4's
answering). Every leap of the floor build is printed with its length (`[chase] leap: …`): one that lands on a body must be within
`LeapReach` (8.6 m) and a miss's take-off from the nearest body within it (its length printed only), and both `r5_hounds` hounds must land `BdGolemMargin` (0.4 s) or more before the golem's burst (0.68 and 0.62 s in Task 9's sweep, with the
pumped estimate, −0.40 and −0.46 s, printed only); `r4_hounds`'
boarding S and its roll-up are printed (`[chase] section 1 r4_hounds: spawned at party S=836.3 …; boarded at party S=[853.6 W4, 853.6
W3], the first 3.32 s after its spawn`). At every stop the cart must stand with full plating. The kiln's spur lever is
thrown through `DebugThrow()` as ride 2 starts, and must still be thrown at stops 2, 3 and 4. On ride 3 the switch is thrown by
a hit (`ReceiveAttack`), and a second hit must leave it thrown: the cart must cross, ram the slag cart ahead and cross back,
and the arrival at stop 3 must set that lever back. Ride 4 runs for real to stop 4, the kiln's stop. On ride 5, the kiln, the golem must
come out as the cart reaches its At (0.49): the party S at its spawn (the `(party S=…)` of `[rails] golem r5_golem … bursts onto
the enemy line`) must be at least StopS[4] + At × (StopS[5] − StopS[4]) − 1, and it must be a carried elite, never a boss. As
the kiln's seated window offers the spur lever (thrown on ride 2), the seated lead sets it back and throws it again from the
seat (`PartyController.VrUse`, the dispatch E uses; two `[chase] 4 seated window:` lines, the prompts "Set the switch back"
then "Throw the switch", the lead still seated each time). A mashed press at once after the set-back must be ignored (`[chase] 4
seated mash:`), and the throw waits out that half second. The golem must be diverted by the re-thrown lever. Ride 5's pit switch
must be thrown from the seat as the seated window offers it (`ThrowPitOnTheWay`; `[chase] pit: thrown from the seat at front S=1046.0 (14.9 m
before the lever)`) and the train taken round the pit with no fall (`[chase] 4 the pit switch thrown at front S=1046.0, across from 1065.9
(its points 1065.9), home by 1085.7 (the far edge 1077.9), no fall`; its warn-to-hit 6.733 s), the parked train smashed once with its call
(the hazard "parked", stamped by `BarkTrigger.ParkedCart`) and standing as a lift after (`[chase] 4 the parked train: smashed 1 time(s) on ride
5 (its call stamped "parked": 1 hit(s)), now Lift`), and the cart must reach the end of the line through the buffer. There the crew must be home and the stairs must open. Section 2 builds floor 12 again and restores the cart at stop
2 (`RestoreGates`, `Restore(2)`). Ride 3's switch is left straight: the cart must crash into the heap (40 plating, a `Halted`
event). The lead gets out and breaks the heap with real swings (mouse look and `PartyController.Attack`, the input path),
and the cart must resume (a `Resumed` event) and reach stop 3, then ride 4 to stop 4. On the kiln the test holds the cart under a
hammer and then under the pour until each hits (both stand before the golem's At), and kills the golem after its first slam. It
breaks the valve with a debug hit, then saves and loads mid-ride. The cart must come back Held at stop 4 with `GolemDefeated` and
the valve broken. With no ride armed, a stunned
cinder stood under the middle hammer must be hurt by its next fall. Departing again with every cage open (the far cages' crew seated by the test's hand), W1..W4 coupled as ever since
the landing, and the pumpers capped at one (`PumperCap`), no golem may come,
the pour must not run, the pit switch must be thrown again and the train taken round the pit with the pumpers capped at one (its log reads
`round the pit at front S=1066.0, 0 pumping`: nobody pumps there), and the buffer must work again. Throughout, every enemy on the floor root (plan
collectives included) is swept for a boss at each ride's start and every frame, and the stairs must stay sealed until the crew
are home. Each hazard's warning (the bell's "Cart coming!", the heap's call, a hammer's glow, the pour's warning, the golem's
wind-up, the pit's call, the parked train's call, the trestle's groan) and each hit are logged in physics frames. Every hit must come at least 1.5 s
after its warning, and the run ends with a table of each kind's first hit. A missing kind fails the run.

`--test-pit[=thrown|wrong|lone|stall|late]` (`DebugTools.Pit.cs`, plan 15 Task 4, `_quitAfter` 200) plays the built-in floor 12 at lethality
3, solo, every enemy culled, every stop and id read from the line's shape (`Shape(rc)`: the pit's ride is the last). The build prints
`[pit] floor 12: …` and fails unless the party track is bare over exactly the pit's [lip, far edge] (and a trestle's own span) on its ride,
the pit cell and its two inward cells are chasm, its enemy twin walkable and the fall armed. Then `rc.Restore(LastRideStop)` and the ride
departs. Every ride: no hand on the lever before the lead's press; the early call 25–30 m before the lever (30.0 m measured); in the seated
window `MovesRefused` holds and no move is offered; at the lever a ray from the seated lead's eye to the pit's near lamp is clear (13.3 m).
- `=thrown` (the default): the lead presses on the window's first frame (14.9 m before the lever, `ThrowPitOnTheWay`); every car passes the
  lip across (blend 1.000 there, at least 0.99); the train is home (blend ≤ 0.01) by the back's points + 8.1 m, no fall, and arrives.
- `=wrong`: nobody presses (`LeavePitStraight`); the last call 5–6.5 m before the points (5.9 m measured); the train falls with its front within
  0.6 m of the lip; every rider fallen within 1 s; the wipe rises at the kiln's stop.
- `=lone`: pass 1 the lead, the only human, downed in the seat before the window: the safe line with the lever straight, home, no fall; pass 2
  the lead up in W2 (not able: the throw is the cart's): the same safe line.
- `=stall`: the watchdog's four steps (see "The watchdog" under "Riding together"): caught 25 m before the lever, 8 m before it, 3 m short of
  the points and 5 m past them; never carried past the pit, the last grind falling 0.01 m from the lip.
- `=late`: the lead's press taken as the server takes one (its window, then `RailEncounterController.LateFor`) on the tick the front reaches the
  points and again 1 m past them: each reads `LatePress.TooLate` (a remote presser would be told "Too late to throw it."; nobody is told in
  solo), a local press there moves nothing, the lever stays straight and the train falls; the RESULT says it proves the server's late check.

`--test-island[=cavein|bridge|wreck|parked|lift|takeover|onfoot|stranded|grisel|chest]` (`DebugTools.Island*.cs`, plans 15 Tasks 3 and 5–8;
`_quitAfter` 300, 450 for `=onfoot`) plays the built-in floor 12, the island floor, at lethality 3, solo, gathering on (Ruling 41: the safe
room's gathering stands inside the island), every enemy culled as it appears and ride 4's runaway held, every stop and id from `Shape(rc)`.
- `=cavein` (the default): at stop 0 on foot, the island's build (`[island] floor 12: rooms 9+12, trenches [(46,49), (41,54)], 6 cave-in
  cells (faces 4), exit bridge S=1184.3..1200.3 (4 cells), lava regions 4`); the lead walked at each cave-in never passes its face in 8 s; a
  companion sent for the down stairs finds no path; two companions walked into the trench and onto the bridge's first cell fall within 2 s
  and come back when the lead steps onto stop 0's platform.
- `=bridge`: the last ride ridden (the pit thrown on the way); on every frame any car is over the exit bridge the lead's cart prompt is ""
  and no move is offered, a forced Get out does nothing and a forced move is refused "the train is on the exit bridge" (Task 9's sweep,
  `/tmp/flows-task9-sweep/08-test-island=bridge.log`: 1329 held frames on the first ride, rolling on and the 8 s stop braked on the span, 4
  rounds of presses, 3 of them stopped; 2124 in all with the second ride's 795); ridden again, it still holds for 262 frames with the cart past the far edge and only wagons over the span, a round of presses refused there; a crew member on foot behind is never on a drop and never crosses inside; a
  save there loads Held at stop 4 with everyone on footing.
- `=wreck`: wrecked as its front passes `ExitFromS` + 0.5: no spare spent, the last stop taken, the cart and W1..W4 hidden, 8 riders (crew too)
  set down inside within 1 s, none fallen 3 s on; the crew set down Home, the unopened cages left behind, the win and the reward chest, the
  arrival snapshot at stop 5; the parked train, smashed on the way about 3 s before, standing as a lift within 1 s of its tubs' tumble
  ending and never before, with no party body in its span; the lead put outside
  boards the lift (no takeover) and is set down inside; a save and load give the cart Held at stop 5.
- `=parked`: its build (the rear at `RearS(ExitFromS, 4)` ± 0.1, five solid bodies, its tubs never the party's); in the way at stop 4 the lead
  is offered nothing, and the guard (no Get out, move or revive from a parked tub); the sight (15.4 m to its lantern, clear); its call 33–35 m
  from its rear; a companion aboard thrown clear; the smash (plating −25, each other seated up hero −15 % of max HP, a W1 companion paid, the
  crew's HP unchanged, V × 0.8, `timber_smash` at its lantern); Lift within 1 s of the rear passing the far edge; a restore stands it in the
  way again. Last, every spare spent and the plating set to 25 three metres short of its rear, the smash wrecks the cart: the next frame the
  cart is wrecked and hidden, the place Gone "the smash", five tubs tumbling and only "smashed" told; Gone every frame while they tumble, then
  Lift ("a lift is due", 6.02 s after the smash), "standing" told after "smashed"; the lift's line logged once a ride, `timber_smash` once a smash.
- `=lift` (115 s): first a no-spare plain wreck in its span uncouples the party's wagons (`ClearTheLiftsSpan`, within 0.02 s of the wreck: 0.01 s in Task 9's sweep),
  and the lift stands within 1 s of the smash's tumble ending (5.9 s after that wreck, which comes just past the smash); then
  held at the last stop, the prompt from its W4 and its cart; the press seats the lead and a companion in its cart and a crew member on foot
  in its W1 seat 4 at once, the far companion left on foot; the lead gets out in the countdown on the first frame Get out is offered (2.1
  s), set down outside with the others, and boards again 1 s later; a 5 s countdown (5.08 s measured) with Get out closed for 2 s; over the
  bridge no prompt and a forced Get out does nothing (308 frames held); a save at mid-bridge (S 1192.4) loads the lead beside the parked
  train outside, the lift standing; it halts 1.50 m behind the held train's last body, every rider on island cells; hidden 2 s later,
  standing again within 1 s; the far companion regroups inside; last, a lift ended by `DebugEndChase` goes Gone and stands again within 1 s.
- `=takeover`: `Restore(2)` with every spare spent, wrecked on ride 3 ("No carts left. The rest is on foot."); refused while a chaser stands;
  the double press takes over once, both in the party's cart, nobody in the lift; the cart Held at the lift's cart S with W1..W4 fresh,
  `StandingPast`; the countdown at once with stop 4's gate down and its cage locked; armed from its place with the golem, the hounds and the
  pit passed; Gone through the rear passing the far edge to the arrival, `TakenOver` cleared on it (a wreck clears it too: `ParkedRules.LatchHolds`'s rows).
- `=onfoot`: the takeover with stops 1–2's crew aboard and stop 4's holders alive; inside, stops 3 and 4's cages left behind, the win and the
  chest.
- `=stranded`: a crew member downed in W1 and one put on foot outside once the rear is past the bridge; at the arrival the outsider is Lost
  within 1 s and stays outside and Lost 12 s, its catch-up held; moved for the pin to an outside cell whose straight line to the lead crosses
  no drop (so a held follower that walked would move), it moves 0.00 m (the pin is 0.5 m; with plan 15's fix round 2 walk restored it failed
  at 0.54 m); the lead outside makes it Freed, back inside Lost again; the lead downed inside with nobody else outside: no Lost, no win for
  2 s; the downed crewman revived (bleed clock under 50 s) is Home at once and the win fires when the lead rises, and stands.
- `=grisel`: forgiven, Grisel put on foot on ride 5's lane cell at S 1150: her hold steps her off the rails first, then she is never
  teleported until she is brought in within 1 s of the arrival, beside the train's last body; put back outside and downed, she is carried in
  downed within 1 s onto an island cell off the rails, offering "Revive Grisel Thorn", and stands revived; at the stairs `q_grisel_out` completes.
- `=chest`: the chest in room 9 at (45,54), table `vault`, index 9000, no mimic, no lock, within 1 s of the win; a companion on its cell steps
  off (`ClearChestCell moved 1`); opened, an item drops; a save and a load keep it opened; 40 s in the safe room, a group gathers inside.

Tests: `tests/Dungeon/RailLineTests.cs` (the generator and the solvability check), `tests/Dungeon/RailCurvesTests.cs`
(the smoothed rail centrelines), `tests/Dungeon/Rails/*` (the engine-free cart rules, `SetPieceRulesTests`, `PitRulesTests`,
`ParkedRulesTests`, `ExitBridgeRulesTests` and `SpareRulesTests` among them), `tests/Dungeon/IslandTests.cs` and
`IslandSolvabilityTests.cs` (the island's carving, its 116-row sweep and its proof), `tests/Adventure/CrewRulesTests.cs` (home and lost) and
`tests/Adventure/RailEncounterPlanTests.cs` (the ride plans and their validation). `tests/` is local and not in git.

### Crevasses

A railed floor with `FloorSpec.Crevasses` on has drops cut into its rock (plan 10; floor 12 in the built-in campaign).
Everything here is built from the shared seed: clients build the same chasms and bridges, and nothing new goes on the wire
but three fields in existing messages (`CharacterDto.Fallen`, `CartStateDto.Fell` and `ObjectiveStateDto.LostCages`, which plan 14
replaced with `LostWagons`). The
protocol stays at 17. No crevasse code reads `TierRules.Lethality`: a fall is the same at every lethality.

**Where the generator cuts them.** `FloorGenerator.CarveChasms` (`FloorGenerator.Chasms.cs`) runs in `LayRails` right after
`CommitRails`, draws from no rng, and turns only rock and listed pockets into `Cell.Chasm`, so no walk is ever cut. Every
chasm cell takes the level of the lane beside it, so its lip is that lane's floor. A crevasse never cuts a spawn, chest, key,
trap, breakable, hazard, vault door, lever, objective cell, stair or platform (`TakenCells`), and never lies beside a ramp or
over a cellar, a tunnel or the under layer. It cuts four kinds of drop:
- **The cavern.** When the line kept its cavern window (`RailLine.Cavern`, see above), every rock or pocket cell of the
  cavern's reach becomes chasm and both lanes over the span become timber **decks** (`BridgeKind.Deck`: walkable bridge
  cells with no stone floor under them). The party lane's deck is ride 4's collapsing bridge and the slag lane's carries the slag line's
  drop (plan 13: see "The five rides", ride 4's cavern); both can give way at runtime (gone decks, below).
- **Edge strips** (`RailLine.Strips`), 2 cells deep (`RailRules.StripDepth`), beside straight party-lane runs at least
  `StripPlatformGap` (4) cells from either platform, at level 0 or 1 when the run is flat (no ramp in, beside or within a
  cell of it, nothing walkable at another level round it), always with rock behind. A strip lies inward of the party lane
  (t = −1, −2) when that rock is free, else outward of the enemy lane (t = 2, 3). Each ride takes its longest run of at
  least `StripMinCells` (4; the middle `StripMaxCells`, 8, of a longer one), except the cavern's ride.
- **The gorge** (`RailLine.Gorge`): on the last ride, every eligible run of two or more cells between fractions
  `GorgeFrom` 0.30 and `GorgeTo` 0.90 of the ride (platform middle to platform middle) is cut, each run choosing its own
  side (inward where free, else outward), so the gorge can switch sides along the ride. A pocket inside a strip or the
  gorge is dropped, and a lane cell with a chasm within 2 cells needs no pocket cover.
- **Plank crossings** (`FloorLayout.Walkways`, `BridgeKind.Plank`): up to `WalkwayCount` (3) level-0 cells of straight
  one-cell halls at least `WalkwayRoomGap` (3) cells from any room, the rock `WalkwayReach` (2) cells deep either side cut
  to chasm, chosen farthest-point from the up stairs and then from each other, at least `WalkwaySpacing` (8) apart. They are
  planks only, never detours.
- **The broken bridge** (plan 11, `FloorSpec.BrokenBridgeRide`, Rulings 10 and 11; `RailLine.Break`, a `RailBreak(Ride, Run, Gap)`).
  On that ride the generator takes the longest outward strip-eligible straight run (`StripCells` with way +1) of at least
  `RailRules.BreakMinCells` (5) cells inside fractions `BreakFrom` 0.55 to `BreakTo` 0.90 of the ride (platform middle to platform
  middle, `StripPlatformGap` in from each platform; ties go to the later run, and a longer run is clipped to its middle
  `StripMaxCells`, 8). Each run cell's outward t = 2 and 3 become chasm at the lane's level; the run's enemy-lane cells become `Deck`
  bridges except its middle one (party index `From + Count / 2`), which becomes a `Cell.Chasm`: the 4 m gap. The gap stays in
  `RailLine.Enemy` (code that walks the enemy lane must skip it), and the party lane stays solid floor. The break is that ride's
  strip (it also appears in `RailLine.Strips`), so the ride gets no other. It is refused on the cavern's ride and on the last ride
  (`RailLine.BreakWhy` = `refused`); with no fitting run it is `none`, and the `switch_back` stays fixed (Ruling 13). `--print-map`
  reports it as `break=…` on the `[map] rail` line (see "Rails" above). Floor 12 sets `BrokenBridgeRide` 3:
  `break=188@0.81 run=185..191`, the gap at party S 714.7 on ride 3.
- **The split's crevasse** (plan 12, `FloorSpec.SplitRide`, Rulings 1 and 2; `RailLine.Split`, a `RailSplit(Ride, Crevasse, Points)`).
  `SplitRide` is an int, default 0, clamped 0..6, `[AiIgnore]` in `SpecOverrides`, and acts only with `Crevasses`; the generator never
  reads plans. On that ride `CarveSplit` takes the longest outward strip-eligible straight run (`StripCells` with way +1, its enemy twins
  taken by nothing) of at least `RailRules.SplitMinCells` (6) cells inside fractions `SplitFrom` 0.25 to `SplitTo` 0.55 of the ride
  (platform middle to platform middle); on the cavern's ride the run must end `SplitCavernGap` (3) cells or more before the cavern span,
  and ties go to the later run. The run's last `SplitCells` (4) cells have their outward t = 2 and 3 cut to chasm at the lane's level;
  the enemy lane beside them stays solid floor, with no deck. The points are the cell before the cut (`Crevasse.From − 1`). The split is
  that ride's strip (it appears in `RailLine.Strips`), so the ride gets no other. It is refused on the last ride and on the broken
  bridge's ride (`RailLine.SplitWhy` = `refused`); with no fitting run it is `none`, and the split piece is then dead (no runaway). The
  outward side is the only one: on floor 12's ride 4 the rock between the meander's legs is one cell, so an inward drop would open onto
  the other leg's pockets. `--print-map` reports it on the `[map] rail` line as `split={points}@{fraction} crevasse={from}..{to}` (or
  `split=refused` / `split=none`; nothing when no split was asked for). With `SplitRide` 0 every floor hashes as before
  (`ChasmTests.SplitRideZero_HashesAsTheFloorWithoutIt`, and `RailLineTests.DefaultKnobs_BuildTodaysFloors`). Over the generator sweep the split fits on 30 of 116 rows (26%), and on 30 of
  the 56 (54%) where the cavern leaves it room: the cavern on the second-to-last ride takes the window on most seeds, and a misfit split
  is dead with no runaway (Ruling 3). Floor 12 sets `SplitRide` 4: `split=227@0.38 crevasse=228..231`, the points at party S 863.6 and the
  crevasse S 865.6–881.6 (`floor_12.json`'s `LayoutHash` moved to 16995108853750381062). The party's train falls there only off the split's
  spur (see "The five rides", ride 4).
- **Lava: the pit and the island** (plan 15, `FloorSpec.PitRide` and `ExitIsland`; see "The pit and the island in the generator" under
  "Rails"). The pit's cut (`RailPit.Cut`: the pit cell and the rock inward of it) and the island's (`RailIsland.Cut`: the exit bridge's
  party and enemy cells, the moat and the trenches) are chasm cells like any other, so every fall rule below holds there, but a chasm
  region holding any of them is dressed as lava, the whole region: on floor 12 four regions, the pit, the exit bridge with its moat (which
  takes in the gorge's last span, 311..313, under the bridge, and the strip beside the moat) and the two trenches. The **trench** is a
  hall mouth beside an island room cut to lava at the room's level; the rest of that hall, to the cell before a fork or a room, is a
  **cave-in** (`Cell.Wall`). Floor 12: rooms 9+12, trenches at (46,49) and (41,54), six cave-in cells with four faces toward open ground.
  The **moat** is the two cells beside each bridge cell (inward of the party lane, outward of the enemy lane), so nothing walks round
  the bridge's ends.
  - **What lava looks like** (`ChasmDressing`, skipped headless): a shaft down `ExitBridgeRules.LavaDepth` (3 m), glowing toward the
    lava; a molten surface (`RailGap.Molten()`); one `RailGap.Glow()` light per region, its range clamped to 6–18 m; only the upper mist
    layer, at half the depth, and no ember floor. The molten material and glow are made by static factories, never held as static
    instances, so no Godot resource leaks at exit. The build prints `[chasm] regions=13 decks=50 planks=2 cavern=44 lava=4` on floor 12.
  - **The cave-ins' look** (`DungeonBuilder.DressCaveIns`, `RockfallAt`, look only, skipped headless): each face toward a walkable or
    chasm cell gets a rockfall of the CC0 MinesPack's rocks (`Rocks_1..Rocks_8`, `assets/models/loafbrr_mine/MinesPack.gltf`, listed in
    `assets/LICENSES.md`), in courses filling the hall wall to wall to near its vault, with fallen beams and rubble at its foot. A stone may
    reach past the open cell's span only where the cell beside it is rock, so its far half hides in the wall. Each face draws from its
    own `Random(x*73856093 ^ y*19349663 ^ CurrentFloor)`, as `SealMouth` does, never a gameplay stream. The rockfall has no collision;
    the cave-in cell is a wall. Every role prints `[DungeonBuilder] floor 12: 4 cave-in face(s)`; where the look is drawn,
    `… the cave-ins' rockfalls: 225 boulder(s) on 4 face(s)`. The pack's closest rocks are faceted shards, not rounded boulders (an
    owner's feel call, under "Known gaps" in "Riding together").
  - **The exit bridge** (`RailEncounterController.Island.cs`, `ExitBridgeRules`). Its party cells (311..314 on floor 12) and enemy cells
    keep their track, rails and sleepers, with no gravel (`RailTrack.OmitBallast`: the ballast has its own stations, so it breaks at the
    span's exact ends; the rails' stations, S and gaps are unchanged), no deck and no footing: a cart rolls over, a body on foot falls.
    Its look (`ExitBridge`, not built headless) is a trestle: two bents a cell, each a cap, two posts 0.6 m into the lava and a
    diagonal brace, and a stringer under each rail, on `weathered_planks`. Its span is the cells' centres' `ClosestS` ± 2 m, as the
    cavern bridge's: `[rails] exit bridge: party S=1184.3..1200.3 (4 cells), enemy S=1220.3..1232.3 (3 cells); rails kept, no deck, no
    footing` (`ExitFromS`/`ExitToS`, `ExitEnemyFromS`/`ExitEnemyToS`; NaN without an island). Ride 5's spur points at 1200.3 fall at its
    far edge, so the spur's lever stands past its points (`SpurSwitch.LeverPastPoints`: at S 1203.3, its seated window from 1183.3),
    not on the bridge. Its hold, its inside wreck and the lift are under "The five rides".
  - **The set-down guard** (Ruling 12, every floor). Every set-down beside the line (`RailController.ExitCellNear`) goes through
    `ExitBridgeRules.SafeExitCell`: today's `CartSeats.ExitCell` when that cell may take a body (`CrevasseRules.SetDownOk`), else the
    at-stop exit cell of the nearest lane index, searched i+1, i−1, i+2, … (ties to the higher), whose cell holds; none anywhere, today's
    cell. On an island floor the body keeps its side: every cell offered, in the exit cell's own window and in the search, is on the
    body's side of the island, so no set-down ever carries a body across the lava (an outside body by the bridge is set down at (56,61),
    the pocket beside lane 309; an inside one at (50,59), north of lane 315). A set-down that names no side takes its cell's, and on the
    bridge's span the nearer end's (`ExitBridgeRules.SideOfBody`: 311–312 outside, 313–314 inside). `PathMover` also holds a body with no
    path at a drop's lip, instead of walking it straight over (a crewman walked into the lava once).
  - **The map lines.** `[map] rail` gains ` pit={Cell}@{f}` (or ` pit={why}`) and ` island=room {a}+{b} sealed={n} bridge={From}..{To}`
    (or ` island={why}`), and two lines follow it: `[map] rail pit: ride 5 cell 282 (61,56), lip at party S=1073.9, far edge 1077.9` and
    `[map] rail island: rooms [9, 12], 2 hall(s) sealed, the exit bridge party S=1184.3..1200.3 (cells 311..314), enemy
    S=1220.3..1232.3, the parked train rear S=1170.7 .. front S=1183.3`. Floor 12's line reads `… chasm=233 strips=8 gorge=[…] planks=2
    break=188@0.81 run=185..191 split=227@0.38 crevasse=228..231 pit=282@0.25 island=room 9+12 sealed=2 bridge=311..314`.
  - **The probe** (`--track-depth`): the pit's cell and the bridges have no floor drawn, so `nofloorDrop` reads PartyTrack 201 and
    EnemyTrack 161 on floor 12 (`TrackDepthRules.Floor12NoFloorDrop`; 0 and 40 before): 40 samples per 4 m cell of drop, so the pit's 40 and the bridge's 4 × 40 on the party track, ride 3's gap's 40 and the enemy bridge's 3 × 40 on the enemy
    track, plus one sample on each track on the join into the bridge, whose bed lies in the cell before but whose left sleeper end lies
    over the drop. A count off the pin means a deck built without its footing or a drop cut where none was planned.

**Gone decks** (plan 13, Task 1). A cavern deck cell that has given way is runtime state, not layout: `FloorLayout.GoneBridges`, a set
of cells not hashed and not saved (a `Restore`, a load or the next arming stands every cell again). `CrevasseRules.Footing` reads it, so a
companion never steps onto a gone cell and a walker on one falls and is judged as any fall. `ChasmDressing` builds each cavern deck cell
(both lanes) as its own `DeckLook_{x}_{y}` mesh, 44 on floor 12 (`[chasm] regions=13 decks=50 planks=2 cavern=44 lava=4`), not distance-culled;
`ChasmDressing.SetGone(layout, cell, gone)` hides or shows the cell's look and disables or enables its footing box (`Deck_{x}_{y}`), and
logs an ERROR for a cell that is not a cavern deck. The live pieces cut their track bare over their span (`RailTrack.CutMore`, which,
like `Cut`, keeps every gap already cut: ride 5's pit's survives a later cut) and lay their own rails, one mesh per cell, with
`RailTrack.LaySpan` of the lane's own track (`{name}_Rails`, `{name}_Sleepers`; the rails end and restart with a ring at a gap's exact ends,
so a span meets the track with the same frame, measured 0.000 mm at both lips). They add no `RailTrack`: the probe measures them as the
lanes' own (`PartyTrack`, `EnemyTrack`).

`FloorLayout.Bridges`, `IsBridge`, `IsChasm` and `ChasmCount` read them; `--print-map` reports them on the `[map] rail` line
(see above). Floor 12 (the shipped layout): cavern 238..259 on ride 4, chasm=233 (217 before plan 15's pit and island), strips=8 (ride 3's
is the broken bridge, ride 4's the split's crevasse), four gorge spans on ride 5, planks=2 (3 before: the island sealed one hall), `break=188@0.81
run=185..191`, `split=227@0.38 crevasse=228..231` (`[map] rail split: ride 4 points at party S=863.6, crevasse S=865.6..881.6`), `pit=282@0.25`
and `island=room 9+12 sealed=2 bridge=311..314`. The trestle stands in the measured gorge: `r5_trestle` (0.565) needs only its middle
(At ± 2 m) inside a gorge span, since a trestle bridges a gorge from lip to lip; the golem went to 0.49 with it. Building the kiln logs
`[rails] r5_trestle over a crevasse: True`. Plan 10 stood `r5_jump` (0.65) over the gorge too, its whole gap (At ± 2.5 m) inside one span;
plan 15 took the jump off floor 12 for ride 5's pit, which is its own cut at 0.25 of the ride, ahead of the gorge (S 1073.9..1077.9).
The last gorge span (311..313) lies under the exit bridge and reads as lava with it.

**What it looks like.** `ChasmDressing` (`Dungeon/ChasmDressing.cs`) builds in two halves. The footings are built on every
role, headless and the dedicated server included: a bridge cell has no stone floor, so a deck's or a plank's
`StaticBody3D` box (a plank's boards 1.6 m wide, `CrevasseRules.PlankWidth`) is the only thing a walker stands on there.
The look is skipped headless: per 4-connected region of chasm-or-bridge cells, shaft walls (near-black at the rim, warming
with depth), an ember floor and its light far below, two drifting smoke layers that fade as you near them (never a pale
plane), the deck slabs on bents and the planks, on the existing `weathered_planks` texture. Every build prints
`[chasm] regions= decks= planks=` (floor 12: 11, 50, 3; 44 decks before plan 11's broken bridge added its six). A level-1 bridge beside lower walkable ground would leave an
open band under its deck; `FloorLayout.IsShutUnder` keeps a plinth there, so nobody walks in under a deck.

**Who falls.** `CrevasseWatch` (`Dungeon/CrevasseWatch.cs`, a node on a crevasse floor) judges falls on the server and in
single player only. Each physics tick it checks every hero on foot (a remote player's by the server's copy of its body),
every living enemy no cart carries and every NPC alive and on foot: a body over a drop cell (`CrevasseRules.IsDrop`, a
chasm or a bridge) and `FallDepth` (2.5 m) below that cell's floor has fallen (`CrevasseRules.Fell`), and is taken
(`CrevasseWatch.Take`). For a remote player the server's copy follows its client's poses into the drop: `ServerWorld.ApplyPose`
drops a pose off the map (`PoseGuardRules.OffTheMap`: rock, a wall, not the under layer) but keeps one over a drop cell at or
below that cell's floor (`DropPoseSlack`, 0.05 m, for a body just stepping off the lip; above it is walking on air), so a
player who walks off an edge on their own screen is judged by the server like anyone else. A fallen body's poses are ignored
(the server holds it under the lip), and its return shoves it out first so the poses still in flight from its client's held
body are dropped (`PoseGuardRules.DropAfterShove`). Its return out of the crevasse is guarded the same way (plan 11, Ruling 20):
the server notes each returned member (`ServerWorld.NoteReturned`) and drops that member's poses over a drop cell
(`PoseGuardRules.DropAfterReturn`) until one arrives off a drop, giving up after `ReturnHoldSeconds` (3 s) so a lost ForcePose
cannot freeze a body; poses from the client's still-held body would otherwise send it straight back in. Its fate (`CrevasseRules.FateOf`; an NPC's kind by `CrevasseRules.NpcBody`):
- **A hero** is `CharacterState.Fallen`: down at 0 HP, hidden, not solid and held 6 m under the lip (`HoldDepth`, above
  `Adventurer.FellOutY`, so it never trips the fell-out guard). Heal, Regenerate, Revive and a level-up's heal do nothing
  while fallen, and `Downed` counts it, so a party with every hero fallen or down wipes. `Fallen` is saved
  (`CharacterDto.Fallen`) and reaches clients in the `PartySync` sheet. `Adventurer.FellOut` over a chasm or bridge cell
  holds the body still instead of putting it back on its last ground: a client waits for the server's word, which comes
  because the server keeps its poses into the drop (above). The log says "{name} falls into the crevasse! Back when the
  party next reaches a stop."
- **Crew and any other NPC** are lost: they die, hidden and held where they fell (a dead fallen NPC is hidden on clients
  and after a load too, unseated below the lip).
- **An escort** (any NPC escorting the party, and Grisel, any id in `CrewRescueController.EscortIds`, whether or not she is
  escorting at that moment) is caught at the lip: laid down but not Mangled on the lip cell (`CrevasseRules.LipCell`: the
  nearest settable cell off the lanes within `LipReach`, 3, else the nearest within `LipSearch`, 6; else any cell a body may
  be set down on, `EscortCell`), so the usual "Revive {name}" prompt serves. A fall never kills an escort and never lays
  her over a drop; with no walkable cell at all (it should not happen) she is held on the stop platform and an ERROR is logged.
- **An enemy** (a boss too) dies with no loot; the log says "The crevasse swallows …" (`HazardDamage.Fall`, byte 3).

**The walking warning.** A hero on foot within `WarnCells` (2) of a chasm region not yet warned of on this floor visit makes
the nearest companion within 12 m (else that hero) call `BarkTrigger.Crevasse` (subtitle-only stock lines in `RideBarks`);
the log says "Mind the edge — that drop has no bottom." The jump keeps its own `JumpCall`.

**The footing guard** (no death the player could not see coming). A body the game steers (a companion, a hero bot) never
walks itself into a drop: `CrevasseRules.KeepFooting` clamps its velocity before `MoveAndSlide` so it never leaves a chasm's
edge or a plank's boards (`FootingMargin` 0.2 m inside them), `FollowSpot` puts its formation slot and regroup point on
footing (followers go in file along a bridge, `FileGap` 1.5 m apart), `NearestStandable` finds the nearest footing to a
point, `PathMover` refuses a shortcut that crosses a drop, and every set-down (a stop, a traffic refuge, the cells crew
are let out on) goes through `SetDownOk`. A body put down behind a lead (a hero bot brought in behind its owner,
`ServerWorld.BringIn`; an escort caught up once unreachable, after about 0.18 s, `Npc`: see "On an island floor" under "Stops, cages and the crew") goes through `CrevasseRules.SetDownNear`: the spot
when its cell may take a body and the ground to it from the lead's footing holds, else the lead's footing. The existing regroup teleport usually pulls a companion knocked over a lip back before it drops 2.5 m.

**The train falls.** Over a crevasse is geometry, not data: a party-lane cell that is a bridge or has a chasm within
`OverReach` (2) cells (`CrevasseRules.OverCrevasse`). A seated hero calls the drop as the cart's front enters a trestle over a crevasse (`RailCalls`, `BarkTrigger.Crevasse`;
`[rails] ... over the crevasse, called by ...`). A missed jump or a trestle collapse over such cells calls
`RailController.Fall(why)` (`RailController.Fall.cs`), the entry later plans call; anywhere else it is the plain wreck.
The broken bridge's gap is the third cause (plan 11): the party's train still across on the slag line as its front reaches the gap
falls there (see "The live switch back and the broken bridge" under "The five rides"). A fall is a wreck with no tumble: the riders, the hounds aboard, every coupled wagon and its crew go into the crevasse
(`TakeTheTrain` takes the riders over the nearest drop), the wagons are uncoupled and hidden, and the capture names them in
`ObjectiveStateDto.LostWagons` until the next restore of the cart whole, which couples four fresh ones (plan 14; see "The save" under
"Stops, cages and the crew"). A spare takes over only if a hero still stands (`CrevasseRules.SpareAfterFall`), so a fall
that takes everyone spends no spare before the wipe. On the wire it is the usual Wrecked state plus `CartStateDto.Fell`
(`CartFlags` has no free bit): a client plays the plunge, not the tumble, on that rising edge; the sound reuses `cart_wreck`.

**The fallen come back** (`FallenReturn`, `RailController.ReturnFallen`, `PartyController.ReturnFallen`) at 25 % of max HP
(`ReturnHpFraction`) with effects cleared, on a cell of their own (`SetDownCells`), when:
- the party's cart rolls into a stop (its arrival, never a spare's takeover), beside the cart;
- a living hero on foot steps onto any stop's platform after the fall (one standing on a platform at the fall must step off
  and back on), beside that hero;
- the cart is restored (`RailController.Restore`: a load, a wipe's rise), beside the cart at its stop;
- the floor changes, or a floor with no line is loaded (`CrevasseRules.ReturnOnRebuild`), at the arrival spot;
- a parked hero (a player who left fallen) unparks: it comes back after the unpark's teleport. A rejoining player is put
  at its parked pose only when `CrevasseRules.RejoinAtParkedPose` holds (never for a fallen hero, never below a lip);
  otherwise at the stairs, so it never rejoins in the dark.

**The arrival snapshot and the wipe's rise** (rulings 14 and 15). On the server and in single player,
`RailController.ArrivalSnapshot` is a whole `FloorStateDto` captured one frame after every `Arrived` and `Restored` event.
A spare's takeover raises `Arrived` at its stop, so it counts as the last stop reached for the rise (though it never returns
the fallen).
A wipe on a floor whose cart has a snapshot (any cause, on foot or aboard; `Main.RiseAtTheLastStop`) keeps its own live
capture and takes the train from the snapshot (`Save.Core.RailArrival.Merge`): always the `RailStop`. On the crew trial's
floor (a `CrewRescueController` is built) it also takes the crew's `NpcDto`s and the objective's `Completed`, `FreedNpcs`,
`OpenCages`, `HomeCrew`, `LostCrew`, `LostWagons` and `AboardCrew` (plan 14); Grisel's (an escort's) entry comes from the arrival too,
unless she is Dead or Fled at the wipe (a fall never kills her, so that came from combat or her betrayal), when her live
entry and quest state stand. On any other railed floor only the stop is taken: its objective stands as the wipe found it.
Spares, gates, the orc's keys, enemies and chests stay as they are at the wipe. A win since the arrival is taken back with
the train (`CrewRescueController.TakeBackWin` clears the flag this build set, `AdventureDirector.ClearFlag`, e.g.
`jailbreak_freed_all`, and the stairs stay shut); while the rise is pending (`Main.RiseAtCartPending`) the crew trial wins
nothing. The rebuild then calls `RailController.RiseBesideCart` deferred, after `Restore`'s own set-down: every hero not
parked (remote players' too) and every escort on foot is put on its own cell beside the fresh cart at that stop
(`ExitCellNear`: never a drop, a rail or a siding), facing it, and no spare is spent (`[rails] wipe-rise: N hero(es) beside
the cart at stop K`). The server places a remote hero before its client is ready again, so the `OnFloorReady` ForcePose
lands it there (the RIDE pass's last step proves it). A wipe before the first arrival rises at the up stairs, beside stop 0.

`--test-crevasse[=trestle|survivor]` (`DebugTools.Crevasse.cs`) plays floor 12 of the built-in campaign at lethality 3, solo,
Grisel forgiven, and prints `[crevasse]` lines and a `[crevasse] RESULT`. A0: the human walks the cavern deck end to end and a
plank there and back with the companions following, and nobody falls (the footing guard). A1: a companion over a chasm falls
(fallen, down, hidden, no auto-revive 8 s on). A2: an enemy dies to it ("The crevasse swallows"). A3: a crew member out of
the train is lost, Grisel caught at the lip (down, not dead, escorting, revivable). A4: on ride 1's arrival at stop 1 A1's
companion is back within 1 s, up at a quarter of its HP beside the cart. B: with stop 1's crew seated in W1 and Grisel on the bench,
the cart restored at the kiln's stop and ride 5's pit switch left straight (`LeavePitStraight`), the train runs into the lava pit ("The
cart runs into the lava pit!") and falls: every rider
fallen, W1..W4 uncoupled and hidden, stop 1's crew seated in W1 dead, the capture naming `LostWagons` [1,2,3,4], Grisel down at the lip,
no spare spent. C: the wipe that follows rises at the stop the train left within 10 s: the cart Held, whole and seen, W1..W4 coupled
fresh, the train's crew back in their seats (`AboardCrew`) and alive again, Grisel up and
escorting, every hero up beside the cart at full HP, no spare spent, a fifth of the gold gone, the Scar on, the trial (its flag,
the stairs) as at the arrival. After every step nobody but the step's own victim is fallen. `=trestle` makes the human brake on
the trestle, which falls with the train on it, then C. `=survivor` has the human get out at the stop as the cart departs: after
the fall nobody wipes, a spare waits with four fresh wagons (the capture names no lost wagon), and the human stepping onto its platform
brings the fallen back; a fresh capture names the lost crew, a save and load leave W1..W4 coupled and empty and the crew dead below the
lip, and a hero fallen again comes back on the floor change to 13. Since plan 15 `=trestle` throws the pit switch first (its fall is the
trestle's), and `=survivor`'s human is set down at the kiln's stop once the train has passed the pit's points straight (with the human gone
before the points the pit's R-lone rule would take the safe line, and nothing would fall).

Tests: `tests/Dungeon/ChasmTests.cs` (the carving, the walkways, `IsShutUnder`), `tests/Dungeon/CrevasseRulesTests.cs` (the
fates, `NpcBody`, the lip, the footing guard, the rejoin and return rules), `tests/Dungeon/Rails/FallenReturnTests.cs`,
`tests/Save/RailArrivalTests.cs` (the merge), `tests/Adventure/RideBarksTests.cs` and `tests/Dungeon/RailLineTests.cs`
(`DefaultKnobs_BuildTodaysFloors` pins every floor with `Crevasses` off; the strip, gorge and cavern sweeps). The low-ground
sweep allows a diagonal (corner) neighbour of a chasm to sit at most `CrevasseRules.CornerLipSlack` (1 m, the observed value) below
its lip, not `FallDepth` (plan 11, Ruling 21), so a future layout that touches a lip diagonally by more fails it loudly.

### Lines, sound and music

Every line the ride says is chosen on the server or in single player; clients hear it on the paths they already had:
`NarrationMsg` for the orc and the narrator, `BarkMsg` for the heroes (it names its speaker by party index, so it only carries a
hero) and `NpcSay` for Grisel. The ride adds no wire message (the protocol stays at 17). Every new line is a subtitle until the
owner OKs a paid voice pass (see "Voice").

**The orc's taunts.** Morg Ashtusk taunts six ride moments (`TauntTrigger.RideStart`, `RolledOver`, `CartWrecked`,
`SlagDerailed`, `GolemEntrance`, `BufferSmashed`), each through `AdventureDirector.RailTaunt`, which does nothing on a client:
- RideStart: `RailController.Flush`, on every Departed event (`RideLines.TauntFor`), the kiln's departure too: the golem
  bursts out later, as the cart reaches its At. It is urgent and held up to `TauntDirector.RideHoldSeconds` (10 s) while a line is being said. It is not raised
  when a `CartEvents` listener claims the departure (`RailController.ClaimDeparture`, called synchronously inside the handler: Grisel's
  ride call carries it). `[rails] departed stop N: ride-start taunt said or held=…` is printed.
- CartWrecked: `Flush`, on a Wrecked event, urgent.
- RolledOver: the director's `MemberKilledByHazard` handler, in place of Crushed when the source is a cart (`RunOverRules.IsCartSource`:
  the warden cart, a slag cart, a runaway wagon, the spare cart).
- SlagDerailed: `SlagCart.Derail`, after its log line.
- GolemEntrance: `SpawnGolem`, after the wall line, urgent and held up to `GolemHoldSeconds` (10 s) for it.
- BufferSmashed: `TickBuffer`, after the smash log, urgent and held up to `RideHoldSeconds`.

The campaign carries 16 of his own (`campaign.json`: RideStart 4, RolledOver 3, CartWrecked 2, SlagDerailed 3, GolemEntrance 2,
BufferSmashed 2; the RideStart line that names Grisel forbids `grisel_forgiven`), and `TauntBank.Stock` two plain lines with clean
twins for each. They are house triggers: the planner's prompt does not offer them. `CartEvents` listeners are isolated: one that throws
is logged (`GD.PushError`) and costs neither that event's taunt nor the rest of the batch.

**The heroes' calls.** `BarkTrigger.CartComing`, `AllAboard`, `Heads`, `HoundLeap`, `LineBlocked` and `JumpCall`
(`Adventure/Core/Voice/RideBarks.cs`, topped up into every bark set by `CampaignRepair.TopUpSet`) are said through
`RailCalls.Say` and `AdventureDirector.CallOut`, which skips the bark cooldowns. The caller is the first hero seated in the cart
who is up and is a companion, else the first player's hero there (`RideLines.CallerIndex`: companions speak first). With nobody up in
the cart and the call the party cart's, the up hero riding the train (`CarOf` ≥ 0, a wagon's) nearest the call's place speaks it: the
leaping hound for HoundLeap, the cart for the rest (plan 14's final review M7). With nobody to say it the old text goes to the log. Where each is called:
- CartComing: both `Warn`s (the party cart's and the encounter carts'), 3 s ahead (`RailWarn`);
- Heads: a low beam;
- HoundLeap: a hound leaping for the cart;
- LineBlocked: a live ore heap on the line;
- JumpCall: the jump;
- AllAboard: `StartBoarding(call: true)`, for a boarding someone got into or one a gate's lift started, never for an arrival or a
  cleared halt restarting the countdown under seated riders.

Each raises `RailCalls.Called` (a test hook). `Voices.BarkDelivery` shouts CartComing, Heads and LineBlocked, says HoundLeap and
JumpCall frantically and AllAboard cheerfully.

**Grisel's calls.** `NpcPlan.Calls` (keyed lines; `CallFor(key)`) on `npc_grisel` in `floor_12.json` holds `ride1` to `ride5`
(`RideLines.GriselCallFor`: the ride from stop 0 to 4; `ride4` opens "Last cage.", `ride5` names no crew) and `cage` (`RideLines.CageCall`). `CrewRescueController` says them with
`AdventureDirector.SayAs(npc, line, onceKey, stillDue)`: `npc.Say` here and `NpcSay` to clients, once per floor and key
(`call:{floor}:{key}`, marked in `BarksSaid`), 0.05 s after the speech channel frees, never as narration. Forgiven and seated in the
party's cart, she names the ride ahead at each departure and claims it, so no RideStart taunt is raised for it. A ride call still
waiting is dropped once it is stale (`RideLines.CallStillDue`: the cart has gone on from the stop it rode to, or a spare set it back).
Her `cage` word at a lock she works is said within `CrewRescueController.CageCallSeconds` (10 s) of the lock or not at all.
Condemned, she says nothing.

**Sound.** Every cart body carries a `CartSound`: the party's cart, its spares and chasers, the runaway wagons, the slag and traffic
carts, and the golem's runner with its wheels off. It keeps five pooled 3D players on the SFX bus (the roll and screech loops, the
clack, the edge cues, and the bell on its own so a hit never cuts a warning off) and a wheel-spark emitter. Each rendered frame it is
fed what that peer shows: the server's and single player's own `CartRun`, a client's `CartFollower` (plan 5's synced S, V, mode, flags,
plating and track). `RideSound` (engine-free) turns that into:
- the roll's volume and pitch by speed;
- a screech on a bend or under hard braking;
- a clack at every joint (2.4 m);
- the bell for a body ahead (`RailWarn`, at most every 4 s);
- one edge cue a feed: a crash, a jump, a landing, a hit (the golem's slam when the golem stands beside it), the brake squeal of an
  arrival, or the points' clunk of a track change or a lane crossing.

The first feed, and the first after `Prime()` (a restore, a client's follower snap of more than 1 m or onto another track), only
primes. A wrecked or hidden cart, or one beyond 45 m (`RideSound.Audible`) of the camera, is silent; a dedicated server builds nothing.

`RideAudio.At` plays a prop's or a set piece's one-shot at a point (`AudioManager.PlayAt`), culled when it starts beyond its range of
the camera. On the server and in single player each prop plays its cue in the method that makes the noisy change (`Lift`, `Toggle`, the
heap's knock, the valve's `Burst`, `Collapse`, `Smash`); a client plays it from the live `PropEvt` through `RideAudio.OnProp`
(`RideSound.PropCueOf`), at the prop's one `ISoundPoint`. The hammers, the pour and the buckets hear their own floor clock on every
peer, priming on the first frame and on a clock that jumped. The hammers' and the pour's cue range ends 2 m short of the kiln's stop
(`SetPieceRules.CueRange`, from `StopS[^2]`), so a cart held there never hears them (`--test-ride` and `--test-crew` check it
against every stop, `DebugTools.StopsCheck`); a range of 0 plays nothing (`SetPieceRules.CueAudible`: a 3D
player reads a `MaxDistance` of 0 as unlimited). `RideSoundscape`, a child of the RailController, keeps the foundry rumble loop, the
rail song as a rolling cart nears the local hero on foot (`RideSound.SingDb`), and a trestle creak every 9 to 16 s at a timber frame
near the camera.

**Why nothing rail-side is broadcast.** No rail cue goes through `EventBus.PlaySfx` (a source scan in `RideSoundTests` pins it, and
the RIDE client counts zero rail `SfxMsg`s). A relayed cue would play twice on the peer that made it, and flat, wherever the listener
stands. Every peer already has the synced state, so each plays every cue once, where its own copy of the cart or prop stands. The
quiet paths (a restore or a load, a quiet gate lift, a PropState catch-up, a follower snap) tell distinct values or nothing, and
play nothing.

**Music.** The Chase mood (`MusicDirector`, `Dungeon/Rails/Core/RideMusic.cs`) is wanted while the party cart rolls with the local
hero seated in it or within 30 m, or while a live chase's chaser rolls with them in it, and for 4 s after (`RideMusic.Wanted`). Rides 1
to 3 loop `RideMusic.ChaseTrack`. Ride 4 plays `RideMusic.PourTrack` once through, started at `RideMusic.PhaseSeek` of the floor
clock so its downbeat (`PourBeatOrigin`) meets hammer 0's fall, and locked again each time it ends (`RideMusic.TrackFor`; a chaser's
ride is the one from `RideMusic.StopBehind` its S). That is the phase lock:
- the hammers' cycle is 4.5 s, twelve beats at 160 BPM;
- hammer i runs i·45 ticks (two beats) ahead of hammer 0, so every hammer falls on a beat;
- the beat meets the start of each fall, where `hammer_fall` plays, 0.15 s before the visual slam;
- the track measures 159.66 BPM, so it plays at `RideMusic.PourPitch` (160/159.66, +3.7 cents) and every other track at pitch 1 (the two
  players are reused). `--test-chase` checks the front player's pitch every frame.

**The cart bar.** `RailHud.Text(RailHudState)` (engine-free) gives the HUD line, branch for branch as before ("Cart — wrecked",
"Cart — line blocked", "Cart — gate down", "Cart — 80% — departs in 3", "Cart — rolling", "Cart — on the line", "Cart — at the
landing", "Cart — end of the line", "Cart — at stop N"). A hidden party cart (a wreck no spare replaced; on a client, the Hidden flag)
shows nothing. While the local hero sits in the chaser (`ChaseLive`; on a client `RailNet.ClientChaserHud`), the bar shows the
chaser's own line at any distance from the train, with no plating bar: "Spare cart — departs in N", "Spare cart — after the train"
while it rolls, "Spare cart — behind the train" once held.

**Lamps and labels.** Every cart carries a head lamp (`Dungeon/Rails/CartLamp.cs`): a spot down the track, a small glow and an
emissive lens facing forward, warm white on the party's cart, its spares and chasers, hot orange on the slag carts and runaway wagons.
The lamp hangs on the cart node, never on its "Body", which a tumble duplicates. The two crew in a cage stand at its seats 4
and 5, and two in a wagon sit at its bench's 4 and 5; the second one's name is raised 0.32 m (`CrewRules.LabelLift`, `Npc.SetLabelLift`,
set in `PlatformCage.Admit` and `TrainWagon.Admit` and reset in `Release`) so the names do not overlap.

Tests: `RideLinesTests`, `RideBarksTests`, `TauntTests`, `TauntTimingTests`, `WritingCampaignTests`, `RideSoundTests`,
`RideMusicTests`, `RailHudTests` and `CrewRulesTests`; headless, `--test-ride` (the taunts, AllAboard, the cart's sound and music),
`--test-crew` (Grisel's calls through `Npc.Said`, the props' sound after a load, the bar after the last wreck: `[crew] hud after the
last wreck`) and `--test-chase` (every warning before its hit, the music's pitch); online, the RIDE pass's `[net] ride sound`,
`[net] ride: chaser hud` and `[net] ride: last-wreck-resync ok` lines.

### Riding together (online co-op)

On a dedicated server the rails belong to the server.
- **The server** steps every cart and judges every run-over, hit, jump, halt, wreck and gate. It owns the seat books.
- **A client** builds the same floor and the same set pieces, but only as dressing. It poses and animates them, and it asks through intents. It never changes ride state.
- **Single player** has none of this: there is no `RailNet` and no clock to follow. Its rides are the ones above, at any physics rate.

Protocol 17 carries it (see the note under Multiplayer). The code:
- the wire: `src/Net/Core/CartNet.cs`;
- the sender and a client's copies: `Dungeon/Rails/RailNet.cs` (with `RailNet.Seats.cs`, `RailNet.Carts.cs`, `RailNet.Leaps.cs`), `RailController.Net.cs` and `RailEncounterController.Net.cs`;
- the engine-free rules, each with its tests in `tests/`: `Dungeon/Rails/Core/` (`CartFollower`, `CartSendRules`, `CartSeats`, `RideInputRules`, `PropPromptRules`, `SetPieceRules`, `CartWatchdog`, `SpareRules`) and `Party/Core/ComfortRules.cs`.

#### The server clock and the floor clock

**The server clock.** A client keeps an estimate of the server's `NetSession.Instance.Now` (`ClientWorld.Clock`, a `NetClock`; `Net/Core/NetClock.cs`).
- The Welcome's `ServerTime` seeds it.
- Every pong is a sample: offset `serverTime − (sentAt + now) / 2`, round trip `now − sentAt`.
- It takes the median offset of the last 9 samples whose round trip is at most twice the median round trip. `ClientWorld.Rtt` is that median round trip.
- A pong for a ping sent before a floor build here ended is not a sample (`NetClock.IgnoreBefore`, called after the Welcome's and each `FloorChange`'s build, review I1). Such a ping waited through the server's build (it answers no ping while it builds) and then the client's own (it reads the pong only after it), so its round trip is lopsided by hundreds of ms; with one good sample two of them carried the median 0.17 s behind. A fresh ping goes out on the next tick after the build.
- The client pings every second (it was every two). Its `Ping` also carries that round trip, and the server keeps it per member (capped at 2 s) for the lever's lag allowance (below).

**The floor clock.** `DungeonBuilder.FloorSeconds` is the floor's running time.
- On the server and in single player it is the sum of the physics steps' `delta` since the floor was built. `ElapsedSeconds` is a compensated sum, exact for hours, so a 90 Hz headset in single player keeps real time.
- `FloorTick` is `FloorClock.TickOf(FloorSeconds)`. It is the 60 Hz metronome that the periodic spike beds, the drop hammers, the pour and the Kiln run on. At 60 Hz it counts the physics ticks as before.

**The pair.** The server publishes pairs of (`FloorSeconds`, its `Now`).
- **Where the pair is stamped:** in `DungeonBuilder._PhysicsProcess`, where the floor time is made. `DungeonBuilder.FloorStampNow` is the `Now` at which `FloorSeconds` was set. `ServerWorld.FloorClockNow()` sends that stamp, so the pair is not a physics step stale. Before this fix it was, which made a client's hammers fall 15-35 ms late.
- **When it is sent:**
  - in a transient `FloorClockMsg` (kind 7), every second on a railed floor and every two elsewhere;
  - to a peer on its Ready (`OnFloorReady`) and in the join catch-up;
  - never while the server changes floor or rebuilds one.
- **The Welcome and every `FloorChange`** build their own pair from `Now`, which also seeds the `NetClock`. That pair can be up to a step stale. The `FloorClockMsg` sent on Ready replaces it within a round trip.

**On a client.** `FloorSeconds` is `FloorClock.Read(ServerNow)`, read once per physics tick.
- It slews to the latest pair at 0.9-1.1x real time.
- It snaps only when more than 0.25 s off (a server stall, a new floor), and otherwise never goes back. A snap takes its own server time, so the reads after a backward step run on (they stood still until the next snap, review I1).
- For `FloorClock.SettleSeconds` (5 s) after a floor's first read (a new clock or a `Reset`) it settles: more than one metronome tick (1/60 s) behind its target it snaps forward, and more than a tick ahead (under 0.25 s) it holds until the target catches it. The first pairs of a floor can disagree with the first reads by more than the slew closes in seconds; after the 5 s it slews as above.
- It drops a pair about another floor or build, and starts again at every `FloorChange`.

**Timed things go in server seconds**, never in ticks: hazard phases (`HazardPhaseMsg` carries the server's time and the phase's length; `HazardSync.SecondsIn`), cart states, `ArmedAt`, leaps and the boarding countdown.

**Cadences run by elapsed time.** Every send cadence goes through the engine-free `Cadence`, on both sides:
- snapshots 1/20 s, vitals 1/10 s, `FlushSync` 0.1 s, the objective state 2 s;
- the ready-check count 1 s;
- the client's pose 1/30 s, with its 0.5 s idle heartbeat;
- the cart corrections 0.2 s, and the ride intent's 0.25 s resend.

At 60 Hz each fires every n ticks as the old `tick % n` did, counted from the cadence owner's first step (the absolute phase moved). The ping is the exception: it went from 2 s to 1 s on purpose.

**Hammers and the pour in a headset.** They read the integer `FloorTick`, so on a faster headset their motion steps at 60 Hz (accepted). `--test-tick-rate`, bare and with `--physics-hz=90`, checks that the hammer's and the spike bed's 4.5 s cycles take 4.5 s of wall time.

#### Cart state, corrections and dead reckoning

**Ids.** Every cart has an id (`CartIds`):
- the party's cart is 0;
- a ride's slag, traffic, runaway and golem carts are numbered from 1 for each floor build;
- a wagon of the party's train is 1000 plus its number (`CartIds.Wagon`, plan 14), and a stop's cage (its crew's seat book) 1500 plus
  its stop (`CartIds.Cage`);
- a chaser is 2000 plus its stop.

**The state.** Each cart has a `CartStateDto`:
- track (0 the party line, 1 the enemy line, 2+k the k-th spur) and direction;
- S, V, mode and Limit (the integrator's StopAt);
- the integrator's input: pumpers, brake, crew wagons, commanded speed;
- plating and stop;
- flags: Halted 1, Wrecked 2, Hidden 4, LineBlocked 8, GateClear 16, Derailed 32, ChaseLive 64, CageHeld 128 (plan 7: the cage hold, the byte's last free bit);
- the server's time of the state.

The party's cart adds:
- the lane blend's two S, the gap and the jump's take-off speed (NaN for none);
- `BlockedAt`, the heap's face (NaN for none);
- the boarding countdown (−1 for none);
- the coupled wagons' numbers (`Train`, W1 nearest; the train hash and the RIDE books name them `W{k}`);
- the boarders (the rim's, and each coupled wagon's rim boarder in `WagonBoarders`);
- the armed ride and the server's time when it armed.

Slag and golem carts name their riders' net ids by spot (−1 for an empty spot; net id 0 is a real id).

**The sender.** A `RailNet` node exists only on a networked floor. On the server it runs after the rail controllers' ticks (`ProcessPhysicsPriority` 100).
- **One `CartStateMsg` (kind 8)** goes out per physics tick that changed any cart.
  - The party's cart changes on any `CartEvent`, or on a change of `RailController.NetSignature()`: mode, stop, flags, lane and gap, train, boarding, plating, the wreck count, the boarders, a player's brake or pump, and the armed ride.
  - The other carts change on a spawn, a spur taken or a change of riders. A free or a derail is sent as a state flagged Hidden (plus Derailed).
- **The wreck frame.** A wreck is counted (`RailController.Wrecks`), not read from its event, because a wreck judged inside the encounter controller's tick is flushed one tick late. When a spare took over in the same tick, `CartSendRules.NeedsWreckFrame` sends a state with Wrecked set first, then the takeover's Held state. A client plays the tumble on Wrecked's rising edge (`CartSendRules.WreckedRose`), before the state moves the cart.
- **Hidden.** On the party's cart, Hidden means a wreck with no spare left. A client hides the cart only and leaves the coupled wagons shown, as the server's `HideWreck` does.
- **Corrections.** A transient `CartCorrectionMsg` (kind 9) with every live cart goes out every 0.2 s. A client frees any other cart that a correction leaves out.

**The follower.** A client's cart follows a `CartFollower`, which allocates nothing per frame.
- **`Apply(state, serverNow)`:**
  - A state older than the last one on the same track is ignored.
  - It steps a copy of the state with `CartPhysics` from the state's server time to now, in whole 1/60 s steps (at most 30), and banks the part step. Slopes come from the client's own tracks (`ISlopeSource`).
  - It snaps on the first state, on a track change, on Held or Wrecked (Held snaps to the server's S), and when the error is over 3 m.
  - A snap to a state too old to catch up in full (more than a step past the 30) stands short of the server's cart, so the next state within reach snaps again. The case is a floor rebuilt mid-ride: the corrections sent while the client built it are read after the build, oldest first. Without this, the joiner's cart sat about 1.5 m back after a resync and crept up at the 0.15·V correction limit for 2 s.
- **`Advance(dt)`, each rendered frame:**
  - It measures the error at the start of the frame.
  - It renders S at V + clamp(2·error, ±max(0.15·V, 0.5 m/s)), clamped to the Limit.
  - The rendered S never moves backwards while V ≥ 0.5 m/s. Below that it may ease back at up to 0.5 m/s.
- **The heap (`BlockedAt`).**
  - The server sends `BlockedAt` while the line is blocked and the cart is not across on the enemy lane, which is the test `HitBlock` applies.
  - While the state's S has not passed the face, the follower clamps both its own S and the rendered S at `BlockedAt − Dir·HalfLength`, with V 0 there.
  - The Limit and the physics input are left alone, so the prediction runs into the face rather than braking for it. The Held state that follows lands exactly there, so there is no backward step.
- **A spur track the client cannot find** is skipped. It logs one warning per track, and the next state retries.
- **`Snaps`** counts the jumps of the rendered S. **`AuthS` and `AuthV`** give the server's ride without the rendered corrections; VR comfort reads them.

**The client's party cart.** `RailController.ApplyNet` applies the stop, the halt, the lane, the gap, the train (`RailController.ApplyTrain`: each listed number looked up with `WagonAt`,
coupled in order, the rest hidden), the flags and the HUD line. `LineBlocked` and `GateClear` come from the flags. The countdown is `BoardingLeft` less the server time since the state.

`ClientPose`, each rendered frame, poses the cart, its train, its riders and its boarders at the follower's S.

**The client's other carts.** `RailNet` builds them with the server's own constructors, as dressing.
- They have no sweep, pace or warning. A brake shoe still misses to a client's hit.
- Each follows its own follower on its own track.
- Their riders are bound lazily by net id, because a cart's state can arrive before its riders spawn. A carried enemy ignores snapshot positions.
- A client's slag cart places its brake shoe from its own build S. A cart first seen through a correction on a bend may put the shoe on the other side; this is visual only.

#### Seats and poses

**`SeatMsg` (kind 13).** It carries the rider kind (0 a hero by member index, 1 an NPC by id), the cart's id, the seat, and seated or not, with the landing spot when not.
- The server hears `RiderChanged` on the party's cart, on every train wagon (by its number, `CartIds.Wagon(k)`), on every cage (by its stop, `CartIds.Cage(stop)`; subscribed on the first tick each stands) and on the chaser. Each change goes out after the tick's cart state; `RailNet.CartById` resolves both id ranges, and the catch-up sends both.
- A client books exactly that seat (`CartSeats.Put`). It refuses an index outside 0..5. A different occupant already there gets out where it sits, then `Put` runs again.
- The message is idempotent. An unseat sets the rider down at the landing.
- **Slag cart ids** (plan 13). A leap is a `SeatMsg` naming the slag cart's own `CartId` (the id its `CartStateDto` carries) and the deck
  seat (0 or 1); a client resolves it from its slag copies (`RailNet.CartById`: a live copy, not freed and not derailed). The server hears
  `RiderChanged` on each slag cart's real seat book (`SlagCart`'s `Admit` and `Release`). An unseat whose rider sits in another cart at the
  flush is not sent (`LeapRules.SendsUnseat`): its seat in the new cart moves it on every client. A client never sets a leaper down where
  its slag copy went (Task 7 ruling (d)): every rider still on a copy as it goes is taken out of the copy's book, and another player's
  hero is hidden and held (`RailNet.HoldLeapers`, `[net] ride: {name} was on slag cart {id}'s copy as it went here: held hidden …`) until
  the server's word (`LeapRules.HeldLeaperNow`, each frame): a seat (a haul back) shows it; Fallen leaves it hidden to the crevasse's hold,
  whatever else; the server's set-down on real footing off the copy shows it once the body's poses have brought it within
  `LeapRules.LandingReached` (1 m) of it (`RailNet.PlaceHeld`: its unseat's landing, which must name the copy that went, or a ForcePose,
  each more than `HoldMoveOff`, 0.5 m, from where it sat and `CrevasseRules.StandsAt`: footing under it and not below a drop's lip). A
  bare pose never ends a hold (the final review's M3: the body is eased 100 ms late, back along the standing deck). Our own hero is not
  held (M4): its ForcePose for a fall or a set-down reaches us before the cart state that frees the copy, so it is out of the seat already.
  The fall's unseat lands at the crevasse hold under the deck,
  which is no footing, so a leaper who fell stays hidden until its Fallen sheet. `HoldForServer` (2 s) is the last fallback, and a floor
  reset, a rebuild, a FloorChange, a leave or a rejoin ends every hold (`EndHolds`, also from `RailNet._ExitTree`; a disconnect frees the
  bodies with the world). Each end is logged with its reason and the time held (`[net] ride: {name}'s hold ended here after {t} s: …`).

**Pose v2.** The binary `Pose` is 45 bytes with the kind. It appends:
- `SeatYaw`: where a seated body looks from its seat, all the way round for a companion;
- `PoseFlags2`: Seated 1, Pumping 2.

The snapshot's `PlayerPoseState` carries both, and NPCs carry `NpcState` bit 128 while seated. `PoseBuffer` blends the seat yaw the short way round and takes `Flags2` from the nearer sample. A client's pose also goes out when its `SeatYaw` changes by more than 0.001 rad or its `Flags2` changes.

**The server's side.**
- **A body the server has seated** is judged by `body.Seated`, never by the client's flag. From its pose the server takes only:
  - the seat yaw (wrapped, clamped to ±150°, 0 if not finite);
  - the pitch;
  - the flags.

  It ignores the position, the client's Pumping bit and the Dodge flag (a seat allows guard and parry only, as for a local rider).
- **Every server unseat of a remote player** goes through one place. That covers `Adventurer.GetOut` (stops, set-downs, wrecks, throws) and a `Teleport` of a seated body. It:
  - resets the body's remote position, yaw and velocity;
  - sends that peer a `ForcePose` at the landing;
  - drops its poses still flagged Seated until one is not;
  - clears its ride intent.
- **A server `Shove`** of a remote body drops its poses still in flight from the old spot, for up to 1 s (`PoseGuardRules.DropAfterShove`).
- **An `AttackIntent` from a seated body** turns the seat yaw (`Adventurer.AimSeated`), never the body.

**Others.**
- **A seated remote body** (another player, a hero bot) is shown as a local rider is (`SeatedTick`): it sits still on every screen. Its pose's velocity is the cart's, so `RemoteTick` uses no gait speed, no dance, no footsteps and no wading for it while seated.
- A seated NPC never takes a snapshot position on a client. NPC seat yaw is not on the wire, so a seated NPC faces its seat's forward.
- In a headset the seat yaw follows the head, in single player too.

#### Leaps and boarders

**`LeapMsg` (kind 14, transient).** It carries the hound's net id, the cart it leaves, the body it lands on, the spot (−1 a miss) and the server's start time.
- Every client flies the same 0.6 s arc with a 1.2 m apex from that time. The take-off rides on its copy of the source cart, and the target is the spot's live transform.
- The hound is then carried at its spot. On a miss it is set down at the client's own `StandBesideLane` estimate, and the snapshots correct it.
- A leap is ignored for a hound already flying, or already carried at that spot.
- It is transient because the catch-up resends the leaps still in the air, so a stale one never flies twice.

**The player's leap** (plan 13, ride 4's cavern; see "The five rides"). It is not a `LeapMsg`: a hero's leap is a seat change.
- Every role offers it from its own carts (`RailEncounterController.LeapOfferFor`; a client's from its copies through `SlagCart.Live`): the
  slag cart's prompt "Leap across" (`SlagCart.Prompt`) to a seat in the party's cart, the party cart's "Leap back" to a slag deck.
- The presses are `InteractIntent("slag:{cartId}")` across (`NetRefs.Slag`, `SlagCart.NetRef`) and `cart:0` back. The server takes either
  from wherever the sender sits when the leap's rules take it (`LeapAccepts`, the offer with `AcceptSlack`; a leap back up to the drop's
  edge) and logs `[net-server] interact {ref} from {name}: leap accepted|refused`; a refused one does nothing. A leap back is never a
  "get out" (`HoldsRiders` refuses those on the bridge) and never traced `aboard:`.
- The seat it takes goes out as a `SeatMsg` (above); a client plays `cart_jump` and `cart_land` where the body left and lands, and its own
  player's shake comes from the server.
- A leaper's `RideIntent` pumps and brakes nothing (`ServerWorld.RideInputOf` reads only a body in the party's cart).
- A client offers no "Leap across" after the slag line's call: its `LeapCalled` follows the drop's `PropEvt` (`ApplyDropTold`) and the
  props' catch-up.

**Boarders.** The party cart's state lists the hounds that have landed on it, by spot, and each coupled wagon's rim boarder (−1 for free). Reserved spots and hounds in the air are not listed. A hound knocked off a wagon by its grip leaves the list, so a client drops it there (`RailNet.Leaps.Reconcile`), with no new wire state.
- A client carries the named hounds at their spots, unless they are still flying there, and drops the others where they stand.
- A hound this client landed a moment before the server's state named it is kept.
- A boarder the server frees silently at a ride's end can stand as a ghost for up to about 4 s, until the enemy check.

**Rim spots.** The party cart opens one rim spot per awake hero seated in it, at most its two (`SetPieceRules.RimSpotsOpen`, checked in `MineCart.ReserveBoarderSpot`). A lone rider faces one hound at a time: the next tries a crewed wagon, or leaps and misses beside the lane. A party of two or more awake riders is unchanged, in single player too. A hound already on the rim stays when a rider goes down.

**A player's blow reaches its own car's boarder** (plan 14, Ruling 13; it was the cage roof's 3.9 m before). A seated player's melee blow that meets nothing (an instant one, a remote player's through `AttackIntent`, or a blade's swing that cut nothing) lands on the nearest boarder of its own car (the cart's rim, or its wagon's rim spot) within `BoarderRules.WagonReach` (2.0 m) and 45° of where it aims (`CompanionBrain.InBoarderSwing`, `Adventurer.CarBoarderInSwing`), as a companion's does; another car's boarder is never struck by a seated blow (`BoarderRules.Strikes`). The server swings it, so it works for remote players too, and a remote player's hits count toward a wagon boarder's grip.

#### Remote riding and the shared prompt rules

**Boarding.** The party's cart is `cart:0` on the wire, so a client's interact key sends `InteractIntent("cart:0")`.
- The server's `UseCart` boards or unseats a remote player with the checks a solo player meets: Held, short of the end of the line, a hero seat free. The gate only starts the countdown. A parked actor is refused.
- Reach is the intent's 3.5 m, measured to the tub's nearest point (`RideInputRules.TubDistance`), as the prompt measures it.
- A player who boards brings their own companions within 25 m (online, the bots whose `BotOwner` is that player) and the bench.
- A companion near a held cart hops in only when its own player is seated in it.
- `PlayerAboard` counts every seated, awake, unparked player's hero (LocalHuman or Remote).

**The cage hold (plan 7).** The server decides the hold; a client reads it from `CartFlags.CageHeld` (128), so its HUD and
its seated prompt ("Get out (hold to leave the prisoners behind)") match the server's. A client's held interact (E held 1 s,
or the VR grip) sends `InteractIntent("hold:cart:0")`: `NetRefs.HoldKind` ("hold") wraps the ref (`NetRefs.Hold`). The server
resolves the inner ref to an `IHoldInteractable`, checks the reach as for the cart (`PropPromptRules.InteractReach` to the
tub, `Adventurer.HoldDistance`), and calls `HoldInteract`. A tap still sends the plain `InteractIntent("cart:0")`. The protocol is unchanged (17,
`MessageKindCount` 114): the hold rides on a flag bit and a ref kind.

**The move car to car** (plan 14). A client offers the move from its own book and seat yaw (`RailController.MoveOfferFor`) and
presses it as the target car's `InteractIntent`: `wagon:{k}` (`NetRefs.Wagon`, `TrainWagon.NetRef`) or `cart:0`. The server reads a
press from a rider of another car of the rolling train as a move whenever it asks for one (`IsMoveAsk`: the target is its `Target`
that way, or the car's plain use does not stand for that rider; never when it stands, `PlainUseStands`: a downed crew member's revive
that would be taken, within the 3.5 m reach, which the client's look shows in the move's place: plan 14's final review I1, plan 15 Task 0) and judges it by `WhyMoveRefused` (`TrainMoveRules.Accepted`: the target
still `Target` on the server's book, `MoveCooldown` − `AcceptSlack` (0.75 s) since the last move it took for that hero, no hop under
way, moves not refused). Accepted, it passes the reach check (`[net-server] interact {ref} from {name}: move accepted`); refused, it
does nothing and says why (`… move refused (the place is booked)`; printed on test servers only, as the leap's line). The seat goes
out as a `SeatMsg`; a client sits the hero in with its yaw kept and plays `cart_jump`/`cart_land` where it left and landed, and its own
player's shake comes from the server. A wagon's reach is measured to its tub box (`TrainWagon.TubDistance`, as the cart's), not its
origin, so an end-on press 4.1 m from a wagon's middle reads 3.0 m and is inside the 3.5 m reach. A wagon's "Revive {name}" goes through
the same `InteractIntent("wagon:{k}")`.

**Getting out.**
- A downed rider cannot get out (`RideInputRules.MayGetOut`): a "Get out" intent from one is refused.
- Out of a slow cart, a player's own companions follow. The bench follows once no player is aboard.
- At 1 m/s or more the player is thrown, like anyone: set down on the line just behind the cart, sent the `ForcePose`, and run over once.

**Pumping and braking.** A seated client sends `RideIntent(Pump, Brake)` (kind 15) from its stick: forward pumps and back brakes (`RideInputRules.FromMove`).
- It sends on every change, and every 0.25 s while either is held.
- The server keeps the last intent per member. It lets go of it on an unseat, a leave, or 1 s without one while holding.
- `RailController.Inputs` decides with `RideInputRules.Inputs`:
  - any seated player's brake cancels every pump;
  - players pump first;
  - companions fill up to two.
- Every pumping rider is marked `Pumping`, shown through the snapshot's `PoseFlags2.Pumping`. A client takes its own hero's from its own snapshot entry.
- A player's brake or pump change is in `NetSignature`, so it reaches clients at once.

**Leaving seated.** A player who leaves while seated, or a bot set aside in its seat, is unseated quietly before it is parked (`RailController.UnseatQuietly`):
- it is set beside the lane where the cart is;
- there is no throw, no run-over and no log;
- the seat is freed and the `SeatMsg` is sent.

So its parked pose is that spot. A chaser's rider is handled the same way.

**One rule per prompt.** The server, solo play and a client (with its synced copies) all call the same engine-free rule:
- `RideInputRules.BoardPrompt(seatedHere, held, lastStop, heroSeatFree, inReach)`: "Get out" when seated, "Board the cart" otherwise;
- `PropPromptRules.SwitchPrompt(fixed, taken, thrown)`, `SplitPrompt(taken, past, thrown)` (ride 4's split, `SplitRules.Prompt`), `WinchPrompt(gateUp)`, `CagePrompt(open, orcKeys, griselOpens, downedAboard)` (no "Let them out" since plan 14), `WagonPrompt(downedAboard)` (a wagon's "Revive {name}") and `KeyringPrompt(orcKeys)`;
- `TrainMoveRules.Prompt(fromCar, toCar)` (the move car to car) and `WagonGetOut(rolling, speed)` (a wagon rider's "Get out");
- `SpareRules.Prompt(onFootPlayer, mayChase)` and `SpareRules.ChaserPrompt(seatedHere, held, downed, boardingLeft)`.

What a prompt cannot know stays on the server, which refuses with its log line: a key or a pick in the bag, a holder still near the winch.

#### Props: PropEvt and the Rail part

**Wire names.** Every rail prop has one (`NetRefs`; `NetRefResolver.RefOf` and `Resolve`):
- by stop: `gate:{stop}`, `winch:{stop}`, `cage:{stop}` (`NetRefs.Cage`; it was `wagon:{stop}` before plan 14) and `spare:{stop}`;
- by train wagon: `wagon:{k}` (`NetRefs.Wagon`, the wagon's number, plan 14);
- by set piece: `switch:{piece}` (ride 4's split's lever too, plan 12: its `SplitSwitch.LeverRef`), `spur:{piece}` (the spur's lever), `heap:{piece}`, `valve:{piece}`, `trestle:{piece}`, `buffer:{piece}`, `barricade:{piece}` (plan 11), `bridge:{piece}` (ride 4's collapsing bridge) and `leap:{piece}` (the slag line's drop, plan 13);
- by cart: `slag:{cartId}` (a ride's slag cart, the leap's press; plan 13);
- `keyring:0` and `cart:0`.

**PropEvt.** Each prop tells its own change on the server as a `PropEvt`:
- a gate "up";
- a lever "thrown", "straight", "taken", "free" (a ride's arming cleared it) and "reset";
- the heap "cleared" and "rebuilt";
- the valve "broken" and "rebuilt";
- a trestle "collapsed" and "rebuilt";
- a buffer "smashed" and "restored";
- a barricade "cut", "smashed" and "standing" (plan 11; the Rail part's `DownBarricades`);
- the cavern bridge "gone {n}" (its party cells gone now; a client gives way to n, tumbling the cells' timbers) and "stands" (plan 13);
- the slag line's drop "drops" (its cells, debris, the lamp; a client's slag copies ring their bells and its `LeapCalled` follows) and "stands" (plan 13);
- a spare "used" and "restored";
- a cage "opened" (`cage:{stop}`);
- a wagon "thrown:{crewId}" (`wagon:{k}`, plan 14): a prisoner thrown off it; a client hides its copy `ThrownHide` (1.2 s) later
  (`ShowThrown`), so it sees the tumble, and `FlagFled` follows in the snapshots;
- the keyring "taken".

A client's `Show*` methods apply them silently. A live event plays the cosmetic debris; a quiet repair does not.

**The Rail part.** `PropStateDto.Rail` (`RailPropsDto`) holds the same facts as lists, plus `KeysTaken`, and since plan 13 two scalars:
`BridgeGone` (int, the bridge's party cells gone now; 0 while it stands or with none) and `SlagLineDropped` (bool), hashed as `|N{n}` and
`L`/`-` in `PropCheck`. A client applies them quietly (no timbers, no cue): fewer cells gone stands the bridge and gives way again to n.
- It rides in the prop state that a join gets and that `OnFloorReady` now sends, and in the prop check's hash (`|R…`). The check repairs drift every 4 s.
- Set pieces and spares are applied both ways. Gates, cages and keys only go forward.
- **One writer.** On a client, gates, cages and keys come only from `PropEvt` and the Rail part. The objective state keeps the crew states and `CrewDownedLeft`. At a client's floor build, `CrewRescueController.Restore` opens the cages and takes the keys that the floor state names, forward only.

**Using the props.** A remote player uses the switch, the winch, a cage, a wagon (its revive, and the move) and the keyring through `InteractIntent`.
- A downed actor does nothing.
- **A lever thrown from the seat of a rolling cart** is judged with `PropPromptRules.LeverReach`: 3.5 m plus the cart's speed times half the member's round trip, at most 1.5 m more; in the seated window by `InSeatedWindow`, accepted up to `SeatedSwitchRules.AcceptLead` (5 m) before the server's own window opens (see "Switches and the lane blend").
- **Ride 4's split** (plan 12) is a deadly lever like a live switch back. Its seated window and its early lead are the server's (`InSeatedWindow` through `SplitAccepts`): a remote press that lands short of the server's points, in its early lead, is held to them and thrown as the train passes (`[rails] split {id}: {name}'s throw held to the points …`), never dropped and never sending the party down the spur; one past them, with the party's branch decided, is thrown at once. A press after the runaway has taken it is too late while straight and ignored quietly while thrown (`LateFor`). In the RIDE pass (step 7) the joiner's press in the server's early lead is the throw: held, then thrown as the train passes. The first client's press on its first frame past the points is a second press, and the server keeps the lever thrown.
- **The runaway on the wire** (plan 12). A client's copy comes from `CartStateDto` (kind `Runaway`, `RailNet.Carts.cs`; the ride's runaway names its set piece in `EncounterId`, the traffic runaway none), corrected with every cart; when it goes, the server sets `CartFlags.Derailed` only for a ram (on track 0) or a plunge off the split's spur (on the spur's track, `S` its last spur S), so a client shows a ram (`RunawayRules.ShowsRam`) or a plunge (`SplitRules.PlungedOffSpur`) only when the server made one, and a halt's free shows neither.
- **Hits need nothing new.** Brake shoes, the heap, the valve, a barricade and a lever thrown by a blow all go through `AttackIntent`, which runs the server's melee (a live switch back and the split take no hits). A seated rider's blow at a barricade 0.5 m ahead of the cart's front lands from the front seat (the RIDE pass).
- A test server traces each use as `prop:{ref}:{state}`.
- Partial knocks on the heap are not synced, so a partly knocked heap shows whole on clients until it is cleared.

#### Crew, gates and set-piece visuals

**Crew.**
- The objective state names each downed crew member's seconds left (`ObjectiveStateDto.CrewDownedLeft`). Only a server fills it, so a solo save is unchanged; a server's own autosaves carry it, and its `Restore` ignores it.
- A client's bleed label counts down locally from it. On the Downed edge it starts full until the next broadcast, at most 2 s later.
- On a dedicated server, Grisel's cage line goes to every screen as her own NPC line (`NpcSay`, as the merchant's).
- The crew's runs to their seats are the server's walks, seen in the snapshots; their seats come as `SeatMsg` under the wagons' ids.
- A boarder's throw (plan 14) reaches every client: its 1.5 s glow as the `BoarderThrow` telegraph in an `EnemyStateMsg` (a client's
  copy glows from `Enemy.ClientTelegraph`), the call through the bark path, the unseat as a `SeatMsg`, the death in the snapshots and the
  tumble as `PropEvt wagon:{k} "thrown:{id}"`.

**The armed ride.** The party cart's state names the armed ride and its arm time. A client's copies of that ride's pieces run from the follower under the server's own rules:
- **The ore buckets** follow `SetPieceRules.BucketStateAt(serverNow − ArmedAt, n)`: bucket n creaks at n·3 s and pours 1.5 s later. A client's pour is a visual-only cinder patch. A joiner silently skips the buckets whose patch has already burnt out. The server's `OreBuckets.Tick` asks the same rule, tick for tick as before.
- **The beam** ducks the seated desktop camera as the client's own cart front passes it (`SetPieceRules.BeamPasses`). The server no longer shakes a remote rider for it. A beam already behind a joiner's cart when the ride arms counts as fired.
- **The heap's lantern** blinks under `SetPieceRules.HeapHeaded` and `HeapAlarm`, without the bark.
- **The kiln golem's wind-up** raises `EnemyTelegraph` with `RailSlam` (`TelegraphRules.RailSlam`), and a client's golem glows its fist for 1.5 s.
- **The hammers and the pour** run on the floor clock.

**Shakes.**
- A server `Adventurer.Shake` on a remote player's body (the rails' per-rider shakes) raises `EventBus.RemoteShake`. Only that player's peer gets a `ShakeEvt`, and a headset ignores it.
- The party-wide tremor, horde and ambush shakes (`Tremor`, `relay: false`) are never relayed, because every screen makes its own.

#### Joiners mid-ride and `SendRailCatchUp`

**What a joiner's queue holds.** A joiner's held broadcasts are replayed on its Ready, before the catch-up.
- `CartState` and `Seat` are not transient, so the fresh state arrives last.
- `FloorClock`, `CartCorrection` and `Leap` are transient (`ServerWorld.Transient`).

**`RailNet.SendRailCatchUp(peer)`** runs on a join's Ready (`OnPlayerReady`, when not behind) and on every `OnFloorReady`: a floor change, a `FloorResyncIntent`'s rebuild, or a wipe's reload. It sends:
1. one `CartStateMsg` with every live cart: the party's (train, boarders, flags, armed ride), the ride's slag, traffic, runaway and golem carts with their riders, and a chaser;
2. a `SeatMsg` for each seated rider in the party's cart, the train's wagons, the cages and the chaser;
3. every leap still in the air.

The callers send the floor clock's pair just before it, and `OnFloorReady` also sends the prop state.

**Measured on localhost.** In the RIDE pass a joiner holds the server's seats, train, props and cart (within 1.5 m) within 1 s of its Ready, at 0.1-0.6 s. A resync mid-ride does the same, at about 0.15 s. A joiner's first cart pair is its worst, at up to about 0.8 m: its `NetClock` has few pongs yet. Later pairs are under 0.15 m, and the follower never steps back.

#### The exit island on the wire (plan 15)

**The exit bridge's hold** (Task 3, Ruling 10; `ExitBridgeRules.Holds`). While the party's train rolls (`CartMode.Rolling`, at any speed)
with any car over the exit bridge (its front past the bridge's near edge, its rear short of the far edge), nobody gets out and nobody
moves car to car: `RailEncounterController.HoldsRiders()` is `CavernHoldsRiders() || ExitBridgeHolds()`, and every reader of the
cavern's hold (the cart's `BoardPrompt`, a wagon's Get out, `MovesRefused`, the throw clocks' pause) reads it on every peer from the cart
state's S, so a client's cart offers "" on every frame of it. A train braked to a stop on the span still holds its riders until it
rolls off. The server refuses a remote rider's Get out out loud: `[net-server] interact cart:0 from {name}: get out refused (the train is on
the exit bridge)` (`HoldReason()`: "the train is on the exit bridge", or the cavern's "the train rolls on the collapsing bridge"), and a
move is refused with the same reason.

**A wreck inside** (Task 3, Ruling 11; `ExitBridgeRules.WreckInside`). A plain wreck with the cart's front at or past the bridge's start
(`ExitFromS`) spends no spare: "The cart is wrecked on the bridge. You scramble across onto the island." Every rider of the cart and the
wagons, at or short of the far edge, is set down on the island's set-down spots (`IslandSetDown`, round the lane cell past the bridge,
island cells only, never a rail cell, the overflow sharing them in turn), one already past it where it is; heroes take the plain wreck's toll and crew none (F9), and the
crew follow. The train is uncoupled and hidden, the stop is the last (`StopIndex`), `LastWreckInside` is set (cleared by `Restore`), and the
arrival snapshot is taken there. On the wire it is the usual Wrecked state; the joiner's screen shows it Wrecked with no takeover.

**The lift on the wire** (Task 6). The lift is the chaser's live cart while it runs: id `CartIds.Lift` (2100), kind `Chaser`, its state
with `StopIndex` −1, `Train [1,2,3,4]` and `CrewWagons` 4. Its five books are heard under 2100..2104 (`HearWagons`) and sent in a joiner's
catch-up (`SendSeated`). A client's `CartById` resolves 2100..2104 before the chaser range and drives its own `ParkedTrain` (no copy):
layers by mode, the wagons posed and their riders followed each frame; `Free` never frees it (anyone still seated gets out where they sit,
then it is hidden, or stands at its place). On "gone" a client keeps the bodies shown while the lift runs on the wire (`ClientLiftLive`).
Presses go to `parked:{piece}` (its cart) or `liftwagon:{k}` (`NetRefResolver`; a press that seats the sender from a lift body traces
`lift:{name}` and, on a test server, prints `interact … boarded the lift …`). The RIDE book shows `L[…]` and `L1..L4[…]` while any lift body
has a rider, and the `carts=` book lists `2100Chaser` on both sides.

**The parked train's props.** Its state goes as `PropEvt parked:{i}` ("inway", "smashed", "gone", "standing"), sent again by name in a
joiner's catch-up, and it is in the props hash (`RailPropsDto.Parked`, `|Q{name}`, `PropCheck.Parked`; listed in `Differences`). A client
stands it in the way on ride 5's arming only while its own front is short of the parked train's rear face (`ApplyParkedArm`), so a takeover's
departure, or a joiner's first state between the smash and the last stop, leaves it as the server has it. The reward chest stands on a
client from `PropEvt(chest:9000, "stood")`, handled before its ref resolves, sent again in `SendRailCatchUp`; a prop catch-up that lists it
opened stands it first.

**Each peer's enemy snapshot memory** (Task 8 fix round 1; `Net/Core/SnapshotRules.cs`: `EnemySnapshotMemory`, `SnapshotRules.SendEnemy`).
The server skips an enemy in a peer's 20 Hz snapshot unless the snapshot is full (every `Protocol.FullSnapshotSeconds`, 2 s), the enemy
moved, it is near a body and aware, or its flags (alive, hostile, aware, boss, in water) changed. It used to keep one `_lastSent` and
`_flagsSent` row per enemy for every peer, so the first peer's send in a tick marked the enemy sent, and the second and later peers got a
far or unaware mover's move, and an enemy turning unaware while it stood still, only on the 2 s full snapshot (a client's revive rule,
`RitualRules.ReviveTaken`, reads its copies' aware flag, so it could be 2 s stale). `EnemySnapshotMemory` now records, per (peer, net id),
the place and flags each enemy was last sent to that peer, and each peer is judged against its own row: a second peer gets an unaware
mover's move on the same tick the first does. A peer's rows go when it leaves or has the floor resent, a freed enemy's at that peer's next
full snapshot (`EnemySnapshotMemory.Keep`: the full snapshot's roster is kept, so a long floor 12 of portal waves, hounds and slag riders
never grows it), and all go with a floor change. No message changed shape; Protocol stays 17.

#### The watchdog

`CartWatchdog` (engine-free) frees a cart that should move but has made no 0.5 m of progress in the last 10 s. It has:
- `Stuck`;
- `PartyWatched` and `SlagWatched`;
- a `Window` that the owner keeps per cart, counting elapsed time.

**The sliding window.** The window re-anchors each time the cart makes the half-metre, so a cart that sticks is freed 10 s after it last moved.

**The party's cart** (`RailController.Watchdog.cs`).
- It is watched only while Rolling and not Halted, with no seated player on the brake. Companions never brake.
- Stuck, it "grinds free" to where `CartWatchdog.PartyFreeTo` says:
  - **The heap face.** An obstacle on the party lane short of the next stop: the cart is set at its face and halted there, with no dent.
  - **The gap lip.** An unjudged jump gap short of the next stop: the cart is set with its front at the near lip plus `LipNudge` (0.01 m), at `SetPieceRules.JumpSpeed`, and the jump is judged on that same tick.
  - With both short of the stop, the nearer one along the line wins (a tie goes to the face).
  - **The broken bridge** (plan 11, `RailEncounterController.WatchdogBreak`, `SwitchBackRules.GrindBackTo`). A train stuck across on the
    slag line with its return open, short of a live switch back it has not taken, is never carried past a window its player was
    not shown: with the lever straight and the front short of the back's seated window, it is set with its front at the window's
    start (`lever − 15 m`, still rolling: the calls and the offer run as they would); inside the window and short of the last call,
    at the last call's mark (`points − 6 m`, so "Switch back — now!" still comes and the offer stays up; final review M1); past that,
    with the lever thrown, or short of a fixed back, at the back's points, where the back is taken as its lever stands. Stuck past the back with its return open, it is ground on
    to the gap, where it falls as it would have (`FallNow`). Each prints its own `[rails] watchdog:` line.
  - **Ride 4's split** (plan 12, Ruling 15, `RailEncounterController.WatchdogSplit`). A train stuck with its front short of the split's points
    and the lever thrown (or a throw held to the points) has the lever set straight first, a held throw dropped, logged
    `[rails] watchdog: the split set back (front S=…, … m short of its points[, a held throw dropped])`, and is then freed as ever: the
    watchdog never carries a train onto the spur (`--test-split=stall`).
  - **Ride 5's pit** (plan 15, `RailEncounterController.WatchdogPit`, taken when `WatchdogBreak` gives nothing; armed pit, its ride armed,
    not crossing, the front short of the lip). A train stuck on the party lane short of the pit's deadly switch is never carried past a
    window its player was not shown, nor over the pit, in four steps, each with its own `[rails] watchdog:` line:
    1. with the lever straight and the front short of the seated window: set with its front at the window's start (lever − 15 m, S 1045.9
       on floor 12), still rolling, so the calls and the offer run as they would;
    2. inside the window and short of the last call: at the last call's mark (points − 6 m, S 1059.9), so "Throw it — now!" still comes;
    3. past that, or with the lever thrown: at the switch's points (S 1065.9), where it is taken as its lever stands (thrown: round the pit;
       straight: on toward it; R-lone applies);
    4. stuck past the points on its own lane (its blend at the lip under 0.5): ground on to the lip, where it falls into the pit
       (`FallNow(PitRules.FallLog)`).
    `--test-pit=stall` sets each in turn (caught 25 m before the lever, 8 m before it, 3 m short of the points and 5 m past them: four grinds,
    the last falling 0.01 m from the lip); `--test-cart-watchdog`'s kiln step is set at the window's start, throws it there and comes home past
    the far edge (S 1087.0) to stop 5.
  - Otherwise the cart arrives at the next stop, Held there.
- **Ride 4's bridge: the stall guard** (plan 13, `BridgeRules.Advance`). A train stuck on the collapsing bridge with nobody braking is
  never taken: the collapse's front holds `HoldShort` (2 m) behind its rear while it stands (`[rails] … bridge r4_bridge held by the stall
  guard at S=…`), and the watchdog frees it as ever, to stop 4 (`--test-cavern=stall`). A seated player on the brake keeps the watchdog
  off; a brake pressed before the bridge's call never counts toward the take (the reaction window), one pressed after it does.

**The other carts.**
- A ride's pacing slag cart is watched only while the party's cart rolls and is not halted. It is judged against its pace point and freed with its riders (a leaper on its deck set down first, plan 13). An Offset-0 pacing cart anywhere ahead of its pace point (r4_slag, which starts 40 m ahead round the hairpin, or stands a metre or two ahead of a creeping party) is not watched while it is ahead (`CartWatchdog.WaitsAhead`, Task 6 review I2 and the final review's M1; `--test-cavern=creep`); one that truly cannot move falls behind its pace point as the party passes and is watched from there.
- Traffic carts and runaway wagons are judged on their own progress; ride 4's runaway is freed by its own stall (`OwnProgressStuck`, `[rails] t=… runaway {id} freed (stuck)`).
- The golem's runner is never watched.

**In single player.** The watchdog runs in single player too. `--test-cart-watchdog` (floor 12, no enemies) proves three things:
1. A caught cart (`RailController.DebugStall`) held on the brake for 15 s is not freed.
2. Let go, it grinds free to stop 1 about 10 s later.
3. Restored at the kiln's stop (`rc.Restore(LineShape.LastRideStop)`, stop 4 on the shipped floor: the test's shortcut over the rides between) and caught short of ride 5's pit switch, it is set at the switch's window's start (S 1045.9), the lever is thrown there, and it comes home round the pit past the far edge (S 1087.0) and on to stop 5 (before plan 15 it was set at the jump's lip and leapt it).

The broken bridge's cases are proven by `--test-switch-back=stall` and `=fixedafter` (see "The live switch back and the broken bridge"), the split's by `--test-split=stall` (see "The five rides", ride 4).

#### The left-behind chaser

`RailController.Chase.cs` and `SpareRules`. Online, a stop's spare is a thing to use (layer 8) whose wire name is `spare:{stop}`. In single player it stays looks only.

**The prompt.** Both sides call `SpareRules.Prompt(onFootPlayer, mayChase)`. It offers "Take the spare cart" to a player's hero, up and on foot within 3 m of the tub, when `SpareRules.MayChase` holds:
- the spare is unused;
- the train has gone from that stop: it last arrived at a later stop, is rolling out of this one, or is halted past it;
- no chase is live (`ChaseLive` rides in the party cart's flags).

A wrecked train offers no chase.

**Taking the spare.** The server checks the intent's reach and the same rule.
- It spends the spare (its "used" PropEvt), so a later wreck finds one spare fewer.
- It puts a chaser on the party line: a `MineCart` of kind `Chaser` (`AsChaser`), with id 2000 + the stop. It stands Held at the stop, or back at its limit while the train's tail still stands there.
- It seats the player, their own companions within 25 m and the escorts on foot near them. The party cart and the chaser share one bench rule (`SeatBench(cart, from)`).
- The chaser then runs its own 5 s `Boarding`.

**On the line.**
- It rolls on the party track at the party cart's physics, with no pumping and no set piece judged. Its run-over sweep is on, and its plating is never judged.
- It warns like every party-lane cart: the bark, the bell, and AI bodies sent to the party-lane pockets (`RailEncounterController.WarnPartyLane`).
- **Its Limit** is recomputed every tick: `SpareRules.ChaseLimit(TrainBackS, the next stop)`. The bodies end 1.5 m apart, so it never passes the train.
  With a live parked train (plan 15, Ruling 29) the limit also stops 1.5 m behind the parked train's place and short of the exit bridge
  (see "The lift" under "The five rides"): the chase ends there once the train is held at the last stop or wrecked inside, outside the
  island, and its riders are set down outside.
- **Holes.** When its front reaches a jump's gap, ride 5's pit or a fallen trestle (`CollectPartyLaneHoles`), it is set just past (`SpareRules.Hop`, from `HopPast`) and its riders are shaken. A client's follower snaps after it.
  - Anything on the rails is rolled over: the step up to the near lip is swept as ever, then the landing from the far lip to the landing front (`HopLanding.SweepFromS` to `S`). The pit itself sweeps nothing.
  - It never comes down over a pit. A landing past its Limit is pulled back to the Limit if the whole body still clears the hole there; otherwise it stops with its front at the near lip, Held. A Limit it could reach only standing over a hole is pulled back to that hole's near lip every tick (`SpareRules.HoleLimit`), so it waits there instead of hopping and stopping short each tick. Floor 12's layout never needs either.
- **A live heap** halts it at the face until the heap is cleared (`SpareRules.HeapLimit`).

**Ending.**
- While the chaser stands, its riders may get out ("Get out", `SpareRules.ChaserPrompt`, through the intent `spare:{stop}`). With no player left in it, the chase ends.
  - Not while downed, and not in the countdown's first 2 s (`SpareRules.GetOutOpen`, `Boarding.MinWait`): the key that took the spare, pressed again a beat later, does nothing. A client's prompt runs the countdown on from the chaser's `BoardingLeft`.
  - **The spare given back.** A chase that ends before its chaser ever rolled (its riders got out in the countdown, or nobody is left in it) frees the chaser at once and gives the spare back (`SpareRules.GivesSpareBack`, `SpareYard.GiveBack`): parked beside the platform again, told "restored", and offered again. The log says "{name} steps back out of the spare cart." Once it has rolled, the spare stays spent.
- Once it stands behind a held train, its riders step out beside the lane and it is gone 2 s later.
- A wreck, a load, or nobody in it before it leaves ends the chase at once (with the spare given back if it never rolled; a load then sets the spares as the save has them).
- The chase is not saved: a load sets its riders down.

**On the wire.** The chaser is one more live cart. Its state is sent again when its mode changes, and as Hidden once it is gone. Its seats go out as `SeatMsg` with its id, and a client books them in its copy, NPCs too. Both are in the catch-up.

#### VR comfort

**The vignette.** `ComfortRules.Step` runs once a rendered frame (`VrRig._Process`) at the level in `Settings.VrComfort`.
- **The target radius** is 1 − t·(1 − floor), where t is the larger of two ramps:
  - |acceleration| over 1.5..4 m/s²;
  - |turn rate| over 30..90 °/s.
- **The floor** is 0.55 for Recommended and 0.35 for Strong. It only tightens with acceleration and turning.
- The radius eases toward the target with a 0.25 s time constant. A level changed mid-ride eases the same way, from below as well. Off opens it at once.
- **Strong only:** a vertical acceleration below −4 m/s² (a big drop) fades the view to 0.6 for 0.4 s, which then eases out with a 0.1 s time constant.
- It never goes fully black: the edge's darkness is capped at 0.9.

**Its inputs** (`ComfortSampler`). They count only while the body is seated and was fed within 10 frames; otherwise the vignette eases open.
- **On the host and in single player**, from the seat's pose, sampled at the time the pose stands for ((physics frames + interpolation fraction) / tick rate):
  - acceleration is d(cart speed)/dt, smoothed over 0.1 s;
  - the turn rate is the heading's change per second;
  - the vertical acceleration is the second difference of the seat's height, smoothed over 0.1 s.
- **On a co-op client**, from the server's ride: the follower's `AuthV`, and `CartRun.MotionAt` at its `AuthS` for the turn rate and the vertical speed. Only first differences are taken. A follower snap (`Snaps` changed) resets the sampler, so a snap never fires Strong's fade.

**The per-eye angular vignette.** `assets/shaders/comfort_vignette.gdshader` draws on a full-view quad (`ComfortVignette`), hung 0.5 m ahead of the XR camera and hidden while open.
- For each eye, the fragment turns back into that eye's view-space ray: `INV_PROJECTION_MATRIX` unprojects the fragment at two depths (z = 1.0 and 0.5) and the direction is the difference of the two points, never one point measured from the head (that matrix bakes each eye's offset in, which put each eye's cone about 18° toward the nose). The darkening is measured as the angle of that ray from straight ahead (−Z), so both eyes agree under OpenXR's uneven frusta. On desktop the result is unchanged.
- The clear cone's half-angle is radius × 75°, and the dark comes in over the next 15°:
  - open is 75°;
  - Recommended is 41°;
  - Strong is 26°, fully dark from 41°.
- A camera more than 1 m from the quad (a SubViewport, a probe) never draws it.

**Seated eye height.** While seated, the rig sits 0.45 m lower (`VrRig.EyeHeight` 1.6 less `Adventurer.SeatedEyeHeight` 1.15), so the cart's rim stays in view. Standing restores it.

**The headset camera.** Nothing shakes, rolls or forces pitch on it: `ShakeEvt` is ignored in VR, and the beam's duck is desktop only.

**Snap turn while seated.** Snap turn works while seated (`SnapTurn.Read`: 30°, push 0.6, release 0.3). The seat yaw follows the head.

**The settings.** The Settings screen's "VR" header has "Comfort vignette in a mine cart": Off, Recommended (the default) or Strong. It is saved as `[vr] comfort="off|recommended|strong"`, and an unknown word reads as Recommended. `--test-keys` round-trips it.

Tests: `ComfortRulesTests` and `SnapTurnTests`.

#### The RIDE net test

**How to run it.** Build first, then run `RIDE=1 scripts/net_test.sh`.
- It starts one server on the built-in campaign: `--test-net-server --net-test-ride --objective=jailbreak --seed=4242 --lethality=1`.
- It starts the first client ("Rider") once floor 1 is built, and a second ("Joiner", `--net-test-ride-join`) as the cart leaves stop 1.
- It takes about 6 minutes (351–355 s measured at plan 13 Task 7, with the bridge's take at the end; 291 s before it; about 6 minutes
  again in the controller's two passing runs at plan 14 Task 7, at the train's 5.22 m/s with the move, the knock-off, the throw and the
  double press added). Seed 4242's floor 12 builds the shipped layout (the same line, five rides).
- Every stop and ride the pass names for the kiln or the end of the line reads `LineShape` (`NetTestFlow.Line`): the kiln is ride
  `LastRide` (5) from stop `LastRideStop` (4), the end of the line stop `Rides` (5). The script's strings name them as numbers.

**Run it alone, on an unclamped CPU: the RIDE gate needs a platform profile other than `low-power` (plan 13's gate runs were on `performance`).** Its set-piece clock and party-pairs checks fail on a loaded or clamped machine: a slowed server's floor clock falls behind its own wall clock. An xUnit suite running at the same time is enough to do it. The barricade and switch-back checks (plan 11) run before the clock checks, so a slow server's clocks do not hide them.
- **The low-power profile invalidates a run** (measured 2026-11-21, not a code change). On the dev laptop the ACPI `platform_profile` (`/sys/firmware/acpi/platform_profile`) read `low-power`. With three godots running, the firmware clamps every core to about 600 MHz (550–700), against 2.6–3.9 GHz with two.
- The onset is in ride 2, when the third godot (the joiner) starts as the cart leaves stop 1. The server then needs 15–20 ms a physics tick instead of about 3.4 ms, runs 40–56 of its 60 steps a second, and its bucket pours come 3.25–4.7 s apart instead of 3.0. The lag ends when the first client leaves.
- Every code section slowed 3.6–5.5× at the same instant, and node, physics-pair and GC counts stayed flat. The first plan-11 commit, a890483, which passed before, lags the same way under the same profile. So the failures were the machine, not plan 11's code.
- The remedy is to switch to `balanced` or `performance` (Fn+Q on the Legion, or `echo balanced | sudo tee /sys/firmware/acpi/platform_profile`) and run it alone. The script warns before a RIDE run when the profile reads `low-power`. A run with three godots at about 600 MHz (`grep MHz /proc/cpuinfo`) is invalid.

**Timeouts.**
- The server runs with `--quit-after=660` (600 before plan 14 Task 7; a test server quits after 120 s unless told) and ends anyway once
  empty. Its own RESULT fails at 560 s.
- The first client and the joiner run with `--quit-after=540` (480 before plan 14 Task 7, which raised all three for the long train's
  slower rides and its new steps; 390 for the joiner before plan 13 Task 7). The script's header puts the first client's own run at
  about 330 s.

**The script, in order:**
1. **Floor 12.** The party goes to floor 12. The server throws the kiln's (ride 5's) spur lever, and the client must show it. The client's set pieces stand and its props, rail part included, hash as the server's.
2. **Ride 1.** The client boards at the landing through its intent. It pumps out (the server's pumpers ≥ 1, and the client sees its own Pumping) and brakes for 0.6 s (the server's V drops at least 1 m/s). Then it cuts ride 1's barricade from the seat over the wire (plan 11): it hears the call (`[net] ride: barricade r1_barricade called here (BarkMsg …)`, or the log's plain words), from 12 m out sends the brake intent until its cart state shows V < 0.5, creeps up with the brake feathered until the front stops within 0.5 m of the face (a stop 12 m out leaves the blade metres short), and, held on the brake, aims at `r1_barricade` (`Adventurer.AimAt`, as `FightBoarders` does, which stands aside meanwhile) and swings every 0.5 s until the "cut" PropEvt arrives (`[net] ride: barricade r1_barricade cut here by our blade from the seat ({n} swings, V=…)`; the server prints `[rails] barricade r1_barricade cut by Rider`). It lets the brake go and pumps on; at stop 1 the props' hash must have matched since the cut, with no prop-check asked (`[net] ride: prop hash still matches the server's after the cut`). Plan 14 adds two steps on ride 1, before the barricade's stop:
   - **The move.** 10 m past the cut the client turns its seat yaw to 150° and, on the first frame wagon 1 reads "Climb back to wagon 1"
     and is what it looks at, presses through `party.VrUse` (`InteractIntent(wagon:1)`); the server accepts it (`[net-server] interact
     wagon:1 from Rider: move accepted`, `[rails] move: Rider lands in car 1 seat 0 after 0.60 s`) and the client sees itself in W1 seat 0
     through the SeatMsg with its seat yaw kept (±1°).
   - **The knock-off.** Once ride 1's own `r1_hounds` has leapt, missed or been freed and W1's rim is free, the server holds the answers
     and the hero bots' fire and boards a test hound on W1 (`DebugBoard(1, holdHp: true)`). The client's `FightBoarders` strikes only its
     own car's boarder (`BoarderRules.Strikes`, through `AttackIntent`): three blows knock it off (`[rails] t=… Dyn3 knocked off wagon 1 by
     Rider after 3 hit(s)`), and the client shows it off W1 within 1 s (a window of −0.05..+1.0 s; it tells the test hound from ride 1's
     own as the one it never saw ride a slag cart). A second later the client turns forward and presses "Climb forward into the cart"
     (`cart:0`), landing in a cart hero seat.
   It gets out at stop 1, within 0.5 m of the server's landing within 0.5 s. Cage 1 is locked there, so the client's seat must offer "Get out (hold to leave the prisoners behind)" (the server's `CartFlags.CageHeld`); the client still gets out with the plain tap.
3. **Stop 1's props.** The holders die by a hook (`DebugKillHolders`), and the gate rises on the client. The server downs a crewman while
   cage 1 is still locked (an opened cage lets its up crew out at once, and one downed inside stays there, `StaysInOpenCage`); the client
   takes the keys, opens cage 1 (`prop:cage:1:open`) and revives him through `cage:1`, each through its intent. The bleed label counts down
   on the client. Stop 1's crew then run to W1: the server and the client each print "stop 1's crew seated in W1" in server time, within
   1.0 s of each other, with the crew counted in the train's wagons on both. **End-on** (Task 3's carry): the server downs W1's seat-4
   crewman and puts the client's hero by W1's back end, more than 3.5 m from W1's origin and within 3 m of its tub; the client's
   `InteractIntent(wagon:1)` ("Revive {name}") is judged by the tub distance (at most 3.0 m) and revives him.
4. **Ride 2.** The client boards again, and the beam ducks its seated camera. **The throw**: 2 s after the joiner's Ready the server
   checks that the train rolls with crew in W1 and no hero there, holds the answers, the crouches and the bots' fire (freeing a natural
   boarder on W1's rim first) and boards a test hound on W1. It follows `ThrowLog`: the call, the glow, the throw of seat 5's crewman at
   6.0 ± 0.3 s by physics frames and by its clock, no second throw; 1.5 s after the throw it kills the hound (about 7.5 s after it
   boarded, before 10 s). Both clients must see the call (a `BarkMsg`, or the log's words), the `BoarderThrow` telegraph before the
   throw's PropEvt, the PropEvt within −0.05..+0.5 s of the server's throw, the copy hidden 1.2 ± 0.3 s after it and seated nowhere
   (`[net] ride: {name} thrown from wagon 1 here (PropEvt), hidden … s later, seated nowhere (…) (ok)`). Nobody cuts `r2_barricade`: the cart smashes it, and the client prints `[net] ride: barricade r2_barricade smashed here (PropEvt), plating {a} -> {b}` from the party cart's plating in the cart states just before and just after the PropEvt (the script wants a drop of 24.5–25.5). The joiner is ready mid-ride 2. Within 1 s it holds the cart, the seats, the train and the props as the server has them (`joined-mid-ride ok`), and, since plan 14, the crew too (`JoinerTrainWhy`): W1..W4 coupled, stop 1's two crew in W1's bench with the copies and the books agreeing, cage 1 open and two crew in each locked cage.
5. **Ride 3.** Stops 2 to 4 are left with their cages locked: a client seated in the held cart sends the held intent (`hold:cart:0`) the moment the hold shows (`RideLeaveCrew`), so the departures keep their timing. The script checks that a client left stop 2's prisoners through the intent and the server logged it (`[rails] the prisoners at stop 2 are left behind (`), and that the server left stop 3's and stop 4's for the first client (`(Rider)`). The joiner boards at stop 2. On ride 3 the client throws switch 1 from its seat through the seated window (no aim, the
first offer beyond the plain reach plus the seated lag allowance; the server logs `seated window yes`), and the cart crosses on
both sides. As the throw shows on the joiner's screen, the joiner presses "Set the switch back" from its seat, and the server
refuses it within 1 s of the first client's toggle (`seated window: set back … ignored`); the joiner's get-out waits for that press. Across, the live switch back is offered in the client's seated window only now (plan 11): the client presses it on the window's first frame through the intent (`[net] ride: ride 3's switch_back offers "Throw the switch" in the seated window … thrown through the intent from the seat`), the server traces `prop:switch:{back}:thrown` and takes it (`[rails] switch r3_switch_back taken at S=…: back from …`), and the cart comes home. Each client (the joiner, thrown and resynced, from its own screen) prints the first time its cart state's `CrossBack` turns from NaN to a number on ride 3, with the lane blend read `SwitchStretch` + 2 m on: `[net] ride: crossed back here: CrossBack {cb} told at front S={f} (the back's points {p}), blend {b} at S={s}`; the script wants `|cb − p|` ≤ 0.5 and the blend ≤ 0.01 on both, and no `[rails] the cart runs off the broken bridge` anywhere in the server's log. Home with the back taken (its `CrossBack` here), the client presses it once more from the seat, the intent a mashed second press would send (`[net] ride: ride 3's switch_back pressed once more from the seat after the cart took it (a mashed press, switch:{back}) …`, plan 12 Ruling 21): after the server's `back from` it must print `[net-server] interact switch:{back} from Rider: the deadly lever was thrown (taken …): ignored quietly`, and `Too late to throw it.` must never appear in the client's log. Ride 3's buckets pour on the server's schedule.
6. **The throw and the resync.** 2 s into ride 3, at speed, the joiner gets out and is thrown onto the line behind the cart, at the server's landing. It then asks for the floor again (`ClientWorld.TestAskFloorAgain`). Rebuilt mid-ride 3, it holds the ride again within 1 s (`resynced-mid-ride ok`), with one of stop 1's crew left in W1 and the thrown one gone, no corpse (`[net] ride: after the rebuild the thrown … gone here: no corpse (ok)`).
7. **Ride 4, both aboard.** At stop 3 the server puts the thrown joiner beside the cart; it boards again through its intent, and only then do stop 3's holders die, so the cart leaves for real with both clients aboard (`the cart departs stop 3 with 2 player(s) aboard`). On a longer line every stop up to the one before the kiln's goes the same way. **The double press** (plan 14): 6 m out of stop 3 both clients turn back and press "Climb back to wagon 1" on their first offered frame (a client whose screen already shows W1 taken sends it in flight); the next two server lines for `wagon:1` must be one `move accepted` and one `move refused (the place is booked)`, from different names, and the winner climbs forward into the cart again a second later. Each client prints the train as it rolls out of the cavern's stop: four wagons, the rear the front less `TrainRules.Length` (`[net] ride: cavern: the train rolls out of stop 3 here with 4 wagon(s) coupled (Train [1, 2, 3, 4]), its rear at S=758.6, 12.6 m behind its front`); every rear in the cavern, runaway and take code reads `Party.FrontS − TrainRules.Length(Train.Count)`. Ride 4's dark, runaway and split (plan 12) are real in the pass (no hold), and the script checks them before the clock checks:
   - **The dark.** Each client prints `[net] ride: dark stretch here from S={from} lamps out {n} ({k} pocket light(s) hidden here)` from its own build, and its start and count must be the server's `[rails] dark r4_dark from party S={from} to …: {n} pocket lamp(s) out` (n > 0). Its own scene must have k = n of those lamps' lights hidden.
   - **The runaway.** The server prints `[rails] t=… runaway r4_runaway breaks loose …`, and each client, from the server's cart corrections, `[net] ride: the runaway runs behind the train here (cart {id}, r4_runaway; its S=…, our train's rear S=…, … m between)`. The ride's runaway carries its set piece's id as its `CartStateDto.EncounterId` (`RailNet.ClientRideRunaway`), so a traffic runaway, which goes unnamed, is never taken for it. On the grid (`ride cart={id} kind=Runaway`, the server's at `armed=4`), that cart needs at least 8 pairs on the party line on each client (S ≥ 100: the spur's own S is short), each within 2 m.
   - **The split from the remote seats.** The joiner sends its seat's press (the lever's `InteractIntent`) with its front 2.5 m short of the window's start, as a cart running ahead of the server's would on its first window frame (`[net] ride: the joiner presses ride 4's split from the seat … m short of the points here, short of its window …`). The server must take it (`seated window yes`), hold it with its own front in its early lead, short of its window and within 5 m before it (`[net-server] ride: ride 4's split held to the points for Joiner with the server's front {d} m short of them …`, read within a tick of the press; the window from the split's build line), pass the points straight (`[rails] split r4_split: the train passes straight`) and then throw it behind the train (`[rails] split r4_split: {name}'s throw held to the points (… m short): thrown behind the train`, or `(pressed … m past them, the branch not yet decided)`; `{name}` is the last presser's, the Joiner's or the first client's when its press re-held the throw). The first client presses it on its own first frame with its front past the points, through `VrUse` (`[net] ride: ride 4's split offers "Throw the split behind us" | "Set the split back" in the seated window, … through the intent from the seat ({past} m past the points here) as switch:{split}`: which it offers depends on whether the held throw shows there yet). The server must take that press too and keep the lever thrown. A throw that lands while the server is still short of the points (the client's cart ahead) is held again, and the server echoes it as `Rider used switch:{split}: now straight`, since a held lever stands straight. A set-back is refused by plan 8's guard or the held throw's 1 s guard. So the script fails only on a `Rider used switch:{split}: now straight` after the held throw went in, which would be a set-back that went through. The party must never take the spur (no `the train takes the spur` or `The cart takes the spur`). Then the runaway takes the spur and goes into the crevasse (`[rails] split r4_split: the runaway takes the spur at S=…`, then `[rails] t=… runaway r4_runaway goes off the split's spur into the crevasse …`, in that order after the throw).
   - **On both screens.** Each client prints `[net] ride: ride 4's split shows thrown here (its PropEvt) …` and `[net] ride: the runaway took the spur here and plunged (it went at spur S={g}; our copy at S={c})` (`RailNet` itself, from the gone runaway's Derailed flag and its last S on the spur's track). g must be the server's `… goes off the split's spur into the crevasse at spur S={g}`, and c within 2 m of it, so the copy on the spur is compared too. Nobody shows a ram: no `[rails] runaway rams the train` on the server and no `[net] ride: the runaway rammed the train here` on either client.
   - **The cavern** (plan 13 Task 7; the script's checks come after the split's and before the clocks). The bridge: the server's build line
     (`[rails] bridge r4_bridge: … 22 deck cell(s) … (live)`) gives N, it goes behind the train (`… bridge r4_bridge goes from S=…`), the train
     never falls on the main pass (no `[rails] the train falls` before the take), and each client prints `[net] ride: the bridge behind us
     gone {n}/{N} cells here {dt} s from our arrival at stop 4` with n = N and dt ≤ 10. The brake's reaction window (ruling (a)): the joiner
     brakes once its own train's rear is 0.5 m on the bridge and holds it until 2 s after it hears the call (`BarkMsg`, or the log's plain
     words; its first cell gone failing that); the server reads the call's time and the press (`RailController.BrakePressedAt`) and prints
     `[net-server] ride: bridge: Joiner held the brake across the call: pressed {a} s before it, held until {b} s after it (the reaction time
     1.5 s); counted toward the take NO; the train taken 0 time(s) (ok)`. The leaps: the first client presses "Leap across" on its first
     frame offered it (`[net] ride: cavern: "Leap across" offered to the rider here … pressed on our first frame offered it through the intent
     (slag:{id})`; the server's `[rails] leap: Rider across onto slag cart {id} (r4_slag) seat 0`), the joiner once the first client is on
     the deck (seat 1); both clients print `[net] ride: {name} rides slag cart {id} seat {k} here` for both (their SeatMsgs). The joiner
     presses "Leap back" on its last offered frames (`… the joiner presses "Leap back" on its last offered frames: … {d} m short of the
     offer's close`, d ≤ 0.5; its slag copy's front against `LeapDropEnemyS − BackCloses`), and the server's `interact cart:0 from Joiner:
     leap accepted` and `leap: Joiner back into the cart seat` follow. The first client is offered it on its first frame after the slag
     line's call and does not press it (`… "Leap back" offered on our first frame after the slag line's call here … and not pressed: the
     rider stays on slag cart {id}'s deck through the drop`; the final review's I1): it stays and falls with the slag cart, which goes over
     with `1 hero(es) fell, 0 hauled back` (exactly one `[rails] leap: Rider falls with slag cart {id} (human, up)`, no `Rider back into the
     cart`; on its screen `[net] ride: cavern: the rider fell with slag cart {id} here …`); both clients print `[net] ride: slag cart {id}
     went over the drop here`. Ruling (d)'s case is the joiner's screen: the cart state frees the copy with the first client still in its
     book, so `RailNet.HoldLeapers` holds it hidden (`[net] ride: Rider was on slag cart {id}'s copy as it went here: held hidden …`), its
     unseat's landing (the crevasse hold under the deck) is no set-down, and its Fallen sheet ends the hold within 1 s (`Rider's hold ended
     here after {t} s: fallen`, t ≤ 1.0); a hold ended by the fallback, a pose or a landing fails the script on either client. On the first
     client's own screen its ForcePose comes before the cart state, so the case is not reached there (a NOTE). At stop 4 the joiner prints
     that its hero sits in the party's cart and the first client that its hero, fallen with slag cart {id}, comes back at this arrival; each
     prints that no "Leap across" was offered after the call (ruling (e), from its `LeapCalled`: deck and cart frames after the call on the
     joiner, deck frames on the first client), that no hero was shown standing over a gone cell, with who left a slag copy and how (a hero
     shown on foot at a gone cell's deck height fails it, so does one on foot, not fallen, kept hidden past the hold's fallback + 0.5 s, and
     so does a held hero shown at all between its copy's going and its Fallen sheet; the joiner's line must end `on its copy when it went:
     Rider (held here for the server's word: 1 hero(es)); then Rider fallen {t} s after its copy went, never shown before it (ok)`, the
     first client's `nobody (ruling (d)'s case not reached here)`), that nobody sits on a freed copy, and 3 s on that every hero not parked
     is shown and up there (the return over the wire). The server, `[net-server] ride: cavern: the train arrives at stop 4 with Joiner in the
     party's cart yes and Rider back from the crevasse yes {d} m from the cart …; leaps across 2, back 1; … (1 hero(es) fell, 0 hauled back);
     … (ok)`, judged once the first client is back (a tick or so after the arrival, or 3 s on); a return outside the client's 3 m or board
     reach is shoved within it, and the first client boards again through its intent (`[net] ride: cavern: at stop 4 our hero is back from
     the crevasse beside the cart, up …`, then `[net] ride: at stop 4 our hero boarded again after its fall …`, within 25 s of the arrival).
     Each check fails the script
     with its own reason. `FightBoarders` and every party-cart read in the flow use `Cart == rc.Cart`, so a client on the slag deck never aims
     at the party cart's rim. Measured (a run at 1b69ef8, `performance` profile; eee9360's two runs alike): the brake pressed 1.37 s before the call and held 2.03 s past
     it; "Leap across" 0.1–0.2 m into the stretch (along −0.57 and −0.48 m); the joiner's leap back 0.33 m short of the offer's close; every
     cell gone 1.8 s before the arrival; 819–820 frames watched after the call.
   - **Measured** (plan 12 Task 8, `performance` profile, four runs): the dark 10 lamps out from S 792.9 on every peer (10 lights hidden on each client); the runaway loose at S 793.1, 15.0 m behind with one wagon at 6.6 m/s, 14.9–15.0 m behind on both clients, 21–22 pairs within 0.15 m; the joiner's press held with the server's front 7.3–7.4 m short (the window 5.0 m), the train straight at front 863.0 and the held throw in at once; the first client's cart ran a little behind the server's there, so by its first frame past the points (0.1 m) the throw already showed and it pressed "Set the split back", refused by plan 8's guard 0.02 s after the joiner's throw; the runaway at the points at S 863.1 and off the spur's end (spur S 12.1 of 13.6) about 12.6 s after it broke loose. Before the joiner's early press was added, the first client's press 0.1 m past the points reached a server already past them and was thrown at once.
8. **The kiln and the chase.** Held at stop 4, the kiln's, the first client, back from its fall with r4_slag, boards again through its intent, and the joiner gets out with a tap (the cage hold's seat still lets it out). On foot beside the spare, it is offered nothing while the train is held there. The train leaves on ride 5 with the client aboard (plan 15 removed ride 5's pumping: the remote pit throw replaces the jump's). Plan 15 adds the island's steps; the numbers are the controller's run after Task 8's fix round (8da7229, `NET TEST: ok`, kept `/tmp/tmp.wu6tJwqqQ2`):
   - **The pit switch** (Task 4, Ruling 22). The server names it (`[net-server] ride: ride 5's pit switch r5_switch_out is switch:21 (points S=1065.9, lever S=1060.9, the seated window from S=1045.9; the lip at S=1073.9, the far edge 1077.9)`); the first client presses it on the first frame its seated window offers "Throw the switch" (14.9 m before the lever at 5.21 m/s, `thrown through the intent from the seat (switch:21)`); the server takes it in the seated window (`[rails] switch r5_switch_out taken at S=1064.9: across from 1065.9, back from 1078.9 round the pit`, `[net-server] ride: across round the pit (CrossOut 1065.9, CrossBack 1078.9); the lever traced thrown through the intent (ok)`), and both clients print the same crossing (within 0.1 m). No log may hold "runs into the pit" or "The cart runs into the lava pit".
   - **The parked train's smash** (Task 8). The server traces each `parked:{i}` PropEvt with its time and the cart's plating; each client must show "smashed" within 1 s of the server's, with the server's plating in its first cart state after it. Measured: `[rails] the cart smashes the parked train at S=1170.7: plating 76 -> 51, 1 hero(es) hurt, V 5.2 -> 4.2`, the server's PropEvt at t=285.680, the client's at 285.685 and the joiner's at 285.688, both with plating 51. The props hash carries the parked state: at the first stable grid point a second or more after the smash (t=287.0) all three read `1AD91A9F` (982FB1F1 before it). The server's plating top-up holds off for 2 s after the smash, so the clients read the dent itself. The server's RESULT needs, in order, `chase:` < `prop:parked:{i}:smashed` < `chase-done` < `prop:parked:{i}:standing` < `lift-boarded` < `prop:parked:{i}:gone` < `wreck-inside`.
   - **No remote Get out over the exit bridge** (Task 8, B.10). On every frame the first client's train has a car over the bridge, its cart's own prompt (`rc.Cart.Prompt`) must be "", and once its front is 2 m into the span and the hold 0.5 s old (so the server, whose hold is a strict edge, holds too) it presses Get out through the intent anyway: `[net] ride: over the exit bridge here (front S=1187.1, the bridge S=1184.3..1200.3): the cart offers ""; pressed Get out through the intent anyway (cart:0) from seat 0, 2.8 m into the span and 0.52 s into the hold here`. The server refuses it (`[net-server] interact cart:0 from Rider: get out refused (the train is on the exit bridge)`), and the client is still seated when the hold ends (`… 328 frame(s), the cart's prompt "" on every one; still seated in seat 0 (ok)`). A hold that ends unpressed fails.
   - **The chase to the parked place** (Ruling 29). The joiner takes `spare:4`; its chaser hops ride 5's pit and the fallen trestle and halts behind the parked train's place, outside the island, with the train held at stop 5: `[net-server] ride: chase-done: the chaser halted 1.50 m behind the parked train's place at S=1168.1, outside the island (the train held at stop 5, S=1231.7, 4 wagon(s)); Joiner set down beside the lane outside at (56,65): yes; it hopped 2 hole(s) in the line (ride 5's pit, the fallen trestle)`, and the joiner's own screen measures its chaser 1.50 m behind the place. The joiner's check waits up to 1 s after its unseat for both Held copies (the unseat's SeatMsg can beat the snapshot), and fails if they never come.
   - **The lift** (Task 6, step 87; Task 8). Once it stands again the server shoves the joiner within reach of a lift wagon's tub (`[net-server] ride: Joiner is put beside the lift's W1 (liftwagon:1) at (56,61), 3.31 m from its tub; it offers "Board the cart" there`); the joiner presses the first lift body that offers "Board the cart" through the intent (`liftwagon:1`), the server's press seats it (`[net-server] interact liftwagon:1 from Joiner: boarded the lift (3.3 m)`), the lift rolls (`[rails] lift: rolls from S=1182.2 with 1 rider(s), its limit S=1217.6`) and halts inside: `[net-server] ride: lift halted at S=1217.6, 1.50 m behind the train's last body (the train held at stop 5), inside the island; 1 rider(s) set down inside; Joiner at (50,59) (ok)`, and the joiner prints `the lift set us down inside the island at (50,59)`. The seat books carry the lift with the joiner in it (`L[…]`), the joiner's the server's: 22 grid points, all 22 the same.
   - **The wreck inside** (Tasks 4 and 8). Since the end of the line takes no boarding, the server seats the joiner in the party's cart by the test's hook (`rc.DebugSeat`), waits 2 s, and wrecks it at stop 5: `[net-server] ride: wreck at the end of the line: past the exit bridge's start, no spare took over; 1 hero(es) set down inside (of 1 riding; …) (ok)` (the script needs set down = riding, both 1 or more), the joiner shows it Wrecked with no takeover and is set down on an island cell within 1 s; then `DebugRestoreAt(4)` (`[net-server] ride: test restore at stop 4 (not a rise)`) and step 10's wrecks follow.

   The server then prints how many relief carts it sent (`[net-server] ride: relief carts sent online: N`), and the script fails unless it is 0: a tripwire for the relief's single-player guard, not proof of it.
9. **The quiet unseat.** The client leaves seated, and the server checks the quiet unseat.
10. **The wrecks.** After the wreck inside and the restore at stop 4 (step 8), the server wrecks the cart at stop 4 (`RailController.DebugWreck`) with spares 1 to 3 unused, and spare 3 takes over (`SpareRules.Pick`: the nearest unused stop). The heap stands on ride 3, so 3 s later the server wrecks it again and spare 2 takes over, Held at stop 2 (`wreck-spare`): on a line of any length it wrecks every 3 s until the spare of the stop the heap's ride leaves stands (`NetTestFlow.HeapStop`). The joiner sees each Wrecked state (two) and the takeover at stop 2, and must hear `cart_wreck` once: the end's wreck stands beside it, the second far out of a wreck's 30 m earshot (`RideAudio.At`).
11. **The heap.** The joiner boards that cart and rides ride 3 again with its switch straight, into the ore heap. Its rendered S must never pass the face (the follower's `BlockedS`) nor step back (`heap-halt`).
12. **The last wreck.** The joiner gets out. 3 s later the server wrecks the cart (`DebugWreck`) until no spare is left, so it stands wrecked and hidden (`wreck-last`). The joiner hears that wreck live (`cart_wreck` at least once). 7 s later it asks for the floor again. Its rebuilt cart's first state carries Wrecked, and that catch-up must play nothing: no wreck shown (`NetWrecksSeen` 0), no `cart_wreck`, no tumble (`last-wreck-resync ok`).
13. **The walk-off and the wipe's rise** (plan 10). As the last wreck stands the server prints `[net-server] ride: walk-off: armed (the last wreck stands)` (plan 11 Task 1): the script's `FELL_AT` is the first `[crevasse] Joiner fell` line after that marker, and a missing marker fails it. First the joiner's own client puts its hero on the lip of the chasm nearest it (a cell a body may be set down on, level with the chasm, off the lanes when one is: `NetTestFlow.WalkOffEdge`) and, a second later, walks it off on its own input (`ForcedMoveInput`), printing `[net] ride: walk-off: the joiner steps onto the lip at …`. The server keeps its poses into the drop, the crevasse watch judges the server's copy (`[crevasse] Joiner fell at …: Dead`), and the joiner waits for its sheet to say Fallen (`[net] ride: walk-off: the sheet shows the joiner Fallen here …`). Then the joiner says it is ready (a chat line, `ride: ready for the wipe`); the server prints whether the joiner is already Fallen (`[net-server] ride: walk-off: Joiner is Fallen here before the wipe`, part of its RESULT) and gives every other hero not parked to the crevasse where it stands (`CrevasseWatch.Take`), so the party wipes; the wipe rises at the last stop reached beside a fresh cart (`RailController.RiseBesideCart`), and the server prints `[net-server] ride: wipe-rise: N hero(es) beside the cart at stop K` (its RESULT needs every hero not parked, the joiner among them, up within 10 m of the cart). From the tick after the rebuild stands until the joiner asks for the bridge's take (step 14), the server also watches its copy of the joiner's body every tick and prints `[net-server] ride: wipe-rise watch: … at most D m from the cart …; its client ready again here yes; N pose(s) dropped by the shove guard (ok)`. Its RESULT needs D ≤ 10 m and the joiner ready again. The joiner's client builds the floor where the FloorChange told it to stand (the stairs: the poses are taken before the deferred rise), and the poses it sends from there before the floor-ready ForcePose lands must not pull the body back there, not even for a tick. `OnFloorReady` arms the shove guard from that told spot (`_spawnSent`, `NoteShoved`). Without it the body flashed to the stairs for a tick, and Brand, the companion following the joiner, sometimes regrouped there (68 m from the cart). The joiner waits for the rebuilt floor 12 and for a ForcePose after it (the `OnFloorReady` answer to its Ready), then half a second, and prints `[net] ride: rose beside the cart at stop K (d=… m)`, its own hero's distance from the party's cart. The joiner then prints `[net] ride: walk-off: back from the fall here: up …` (not fallen, not down, shown) or fails. After the rise the
server prints `[net-server] ride: wipe-rise: bridge r4_bridge 0/22 gone, the slag line's drop (r4_leap) standing (ok)` and the joiner,
once a prop check has matched on the rebuilt floor, `[net] ride: after the wipe's rise our props hash as the server's: … BridgeGone 0,
SlagLineDropped False (ok)` (plan 13). The script fails with "the wipe did not set the remote player down beside the cart" unless the server's line is there and d ≤ 6.0, and with a "walk-off: …" reason unless the joiner stepped onto a lip, the server's `[crevasse] Joiner fell` line comes before the wipe's ask, the server saw the joiner Fallen, its sheet showed Fallen and it came back up. This is the proof that a remote player's own fall is judged by the server, and that the server places a remote hero before its client is ready again.
14. **The bridge's take** (plan 13 Task 7, the controller's rulings (b) and (c)): the main pass's ride 4 is never taken, so the take comes
    last, with the joiner alone. It sends its bot away (`DismissBotIntent`: alone, its own fall wipes the party) and says it is ready (a
    chat line, `ride: ready for the bridge's take`). The server judges the wipe-rise watch there (`… from the rise until the joiner asked for
    the bridge's take …`: the restore moves the cart away from the joiner for a tick), turns its grid trace off for good (`RideTraceOff`, on a
    rebuilt floor's `RailNet` too), restores the cart at stop 3 (`rc.Restore(3)`: `[net-server] ride: take: the joiner asked for the
    bridge's take; the cart restored at stop 3 …`) and puts the joiner beside it; it boards through its intent, stop 3's cage is left behind
    through the held intent, and it rides ride 4 pumping with the split left straight, so the runaway follows it into the cavern and goes
    down with the bridge (`[net-server] ride: take: the runaway went down with bridge r4_bridge (…; rammed False, plunged False); rams this
    build 0 (ok)`; the joiner, `[net] ride: take: the runaway's copy (cart {id}) went here …`). Past the call's reaction time, with the
    runaway gone (or its rear 35 m short of the bridge's end), the joiner brakes and the bridge takes the train (`[rails] … bridge r4_bridge
    takes the train …`, `[net] ride: take: the train taken here …`). The take's unseat pose reaches the joiner before the Wrecked|Fell
    state, frames apart (run 2 at 1b69ef8 failed on it in the flow's own read: the flow now waits up to 3 s for the state and says how long
    before it the hero left the cart). Risen at stop 3, it prints `[net] ride: take: after the bridge's take, risen at stop 3 …: the seen
    fall's eye shown here yes; the view after it ours; our current camera now our hero's (ok)`: the seen fall's eye must have been current on
    its screen, and its current camera after the eye and after the rise must be its own hero's. The checks above (no ram on any client, none
    on the server) cover the take too. It is one client's: the first client has left by then, so ruling (b)'s "each client" is the
    taken joiner alone, and a client watching a take (aboard or not) is not reached (see "Known gaps"). The top-up never stands a fallen hero up (`FullRestore` clears Fallen and would undo the wipe).

The kiln's first hammer (`HammerIndexOf(LastRide)`) is paired all through the pass.

**The riders fight back.** Seated in the train, both clients swing every 0.5 s at the nearest boarder of their own car (the cart's rim,
or their wagon's rim spot: `FightBoarders` filters by `BoarderRules.Strikes`, then `Adventurer.AimAt` and `Attack`, so the server's melee
judges a seated `AttackIntent`), and hold while a move turns the yaw. The top-up stays: on plan 11's fix wave's first run the lone warrior
killed every boarder, but ride 2's Forge Priest, shooting from its slag cart 5–6 m off, took it from 129 hp to 23 by stop 2, out of a
melee rider's reach.

**The hound carts' frees on the wire** (plan 14, Ruling 19). The server prints every `freed:` entry of its `SlagLog` at `NetSession.Now`
(`[net-server] ride: slag cart {id} ({enc}) freed: {why} at t=…`) and each client every slag copy that goes at `ServerNow` (`[net] ride:
slag cart {id} ({enc}) gone here at t=…`). The script pairs them by cart id for the first client, before `Rider left while seated`, each
within −0.05..+0.5 s, and requires `r1_hounds`' free by the linger (`its hounds are gone`). A free by the golem's burst is paired the same
way if one comes (the line says `by the golem burst N` or `none in this pass`); with `r5_hounds` at S 1080.0 the linger frees its cart
first, so none came in the runs.

**The books.** An awk (`TRAINS`) compares the grid's book points: every compared point must read `train=1,2,3,4` on both sides, the
first client's all along and the joiner's from 1 s after each Ready, with at least 20 client points and 10 joiner points.

**Measured** (plan 14 Task 7, the controller's runs 2 and 3, `performance` profile, load about 1.2; both `NET TEST: ok`):
- the move: "Climb back to wagon 1" offered 0.02 s after the turn (S 91.2) and pressed on that frame; the server's landing 0.60 s after
  the press, the client's SeatMsg 0.63 s after it, the seat yaw 150° kept; back into the cart the same way (0.62–0.63 s);
- the knock-off: the test hound boarded W1 at S 122.3–122.4 (as `r1_hounds` leapt onto W3), knocked off by three blows 2.2 s later, alive
  (14–15 of 68 hp), shown off W1 on the client 0.024–0.027 s after the server's;
- stop 1's crew seated in W1 on the server and the client 0.001–0.004 s apart; the end-on revive 3.72 m from W1's origin and 2.43–2.44 m
  from its tub (the server's interact line 2.4 m, reach 3.5);
- the throw: the call 0.015 s after the boarding, the glow 4.50 s and the throw (seat 5's Hob Tallow) 6.00 s after the call; on both
  clients the glow 1.50 s before the PropEvt, the PropEvt 0.005–0.008 s before the server's throw line (the same tick), the copy hidden
  1.22–1.23 s after it;
- the double press: the first client turned back at S 776.1 and pressed at 776.2, the joiner at 776.2–776.3; the server accepted the first and
  refused the second "the place is booked"; the winner back in the cart from S 784.8;
- the frees: the four hound carts' (`r1_hounds`, `r3_hounds`, `r4_hounds`, `r5_hounds`), each by the linger, gone on the first client
  −0.002..+0.012 s from the server's free; none by the golem's burst;
- the cavern: the train out of stop 3 with four wagons, its rear at S 758.6, 12.6 m behind its front; the chaser halted 1.50 m behind the
  train's last body at stop 5 (S 1231.7) with 4 wagons;
- the staging: 3 natural hounds freed off crewed wagons by the guard (below), the crew topped up twice.

**The markers the script pairs.** Both sides print on a shared 0.5 s server-time grid:
- `ride t=… S=… mode=… stop=… armed=…`;
- `ride cart=… kind=… t=… S=…`;
- `ride book t=… now=… seats=… train=… props=… carts=… boarders=…`.

They also print `ride hammer=… fall t=…` and `ride bucket pour n=… t=…`. From these the script requires:
- at least 20 party pairs while the cart rolls, each within 1.5 m;
- ride 3's slag cart within 2 m;
- at least 10 hammer falls, each within 50 ms;
- at least 3 bucket pours, each within 0.1 s;
- the joiner's match within 1 s of its Ready and of its resync's Ready, and every joiner pair within 1.5 m;
- no book of either client differing from the server's at a grid point whose neighbours agree with it;
- the same for the other carts with their living riders and for the boarders. The joiner is compared from its match on, except in the second after each Ready, and the script wants at least one compared point with a slag cart.

The server stops its grid lines before the wreck, because the jump to a spare's stop is not a pair.

**Test hooks used.** Every departure is a real one. The pass leans on these hooks:
- the server keeps every player's hero above 75% hp, and keeps the cart's plating up from ride 4 on (it checks the wire, not the fight). Each top-up is logged and counted in the RESULT;
- the long train's staging (plan 14 Task 7, ruled): the pass has no companions to answer a boarded crewed wagon (Rider and the Joiner
  only; Brand comes after the last wreck), so the server frees any natural hound on a crewed wagon 1 s after it boards, before its glow
  (`[net-server] ride: guard: …`), and keeps the crew seated in the rolling train above half their hp, reviving a downed one at most once a
  second (`[net-server] ride: crew top-up: …`); only the scripted throw is seen on the wire;
- `DebugBoard`, `DebugHoldAnswers`, the hero bots' `DebugHoldFire`, `DebugHoldCrouches` and `DebugKill` for the knock-off and the throw;
- the shoves that put a hero within reach;
- `DebugKillHolders`, `DebugWreck` and `TestAskFloorAgain`;
- `RailNet.RideTraceOff` (before the wrecks, and for good from the bridge's take on);
- the bridge's take's `rc.Restore(3)` (step 14), and the joiner's own `DismissBotIntent` of its bot before it.

#### Known gaps

**Not exercised live** (rule or unit tests only):
- real latency and jitter: the pass runs on localhost, and ±150 ms of jitter is tested only in `CartFollowerTests`;
- on a client: a seat taken by another occupant, a missed leap, and the catch-up re-flying a leap in the air;
- a bot hopping in with its owner online, and two players pumping or braking together;
- a winch cranked through the intent, "Open the cage (Grisel)", the forgiven branch, and the "free" lever event;
- on a client: the heap's lantern alarm and the golem's fist glow;
- for the chaser: its warning, a rider's "Get out" (and the 2 s it is hidden at the countdown's start), the spare given back when its riders get out before it rolls, a halt at a live heap, a chase ended by a wreck or a load, a joiner arriving mid-chase, an escort on its bench, a body run over on a hop's landing, and a hop with no room past the hole (floor 12 never needs one);
- a floor change with a hero seated in the chaser: it relies on the `Teleport` unseat, the same path the party cart's riders take live;
- a downed rider's "Get out" intent and a seated pose's Dodge flag, both now refused on the server (code and rule tests only);
- the rim-spot cap with one awake rider (the RIDE pass's hounds went to the crewed wagon), and a remote seated body's presentation (no run gait or footsteps; nothing checks animation or audio);
- a wipe's reload mid-ride. The pass's wipe (step 13) comes with the cart wrecked and held, and the resync runs the same `OnFloorReady` catch-up;
- a fall on a client: the plunge (`CartStateDto.Fell`), the lost wagons (`LostWagons`) and Grisel at the lip are proven in single player (`--test-crevasse`) and on the rules, not over the wire. A remote player's own fall is proven over the wire (step 13's walk-off): a walk off a lip on the joiner's own input, judged by the server, shown Fallen on the joiner's sheet and brought back by the wipe's rise. A return at a stop arrival for a remote player is proven too (the first client's fall with r4_slag, back at stop 4's arrival and boarded again). A return at a platform step for a remote player, and a jump across a chasm (its airborne poses above the lip are dropped until it lands), are not;
- a wreck in motion. The live wreck is the test's, with the cart held at the end of the line;
- a thrown remote player's run-over damage. The pass checks only its landing;
- plan 11 on a client: the train's fall at the broken bridge (the pass takes the live back; the fall is proven solo by `--test-switch-back=wrong`), a slag or traffic cart plunging through the gap (and a ride's slag cart reaching it with a living rider, end to end), a barricade cut on foot or with a spell over the wire, a slag-line barricade's smash watched on a client, and a barricade smashed while its ride is not armed standing again at its arming (rule-tested only);
- plan 13 on a client: a leaper the server sets down or hauls back as its copy goes, seen from another screen (`RailNet.PlaceHeld`'s
  on-footing branch and `LeapRules.LandingReached`; the pass's case is a fall: the first client stays on the deck and falls with r4_slag,
  held on the joiner's screen until its Fallen sheet; a downed leaper hauled back is `--test-cavern=lone`'s, solo), a joiner arriving mid-leap, a leaper leaving the game on the deck (its set-down, and the
  cavern's set-down niche at x=60 islanded once the bridge goes), a chaser over gone cells, a wreck with cells gone, the slag line's
  warning at pumped speed or with another player pumping, and the first client in the bridge's take (it has left by then: the take, its
  seen fall and the camera after the rise are the joiner's alone); a client shows the collapse's front only as its cells (`CollapseS` is the
  server's);
- plan 12 on a client: the party's train down the split's spur and its fall (on floor 12 no press can put it there; proven solo by `--test-split=early` through the test's hook), the runaway's ram (the pass's runaway takes the spur; the server's ram is `--test-runaway=ram`'s, a client's showing it rule-tested by `RunawayRules.ShowsRam`), the runaway halting behind a held train, its bell, the "Not yet" call (it fires with the lever thrown 12–5 m short of the points on floor 12, and no press throws it there), the arrival's reset waiting for the runaway, and the dark as a client draws it (the pass's clients are headless: they compare the lamps put out, not the view; the view is Task 7's windowed `--test-split=after --screenshot`);
- a refused seated press: the server refuses a seated `InteractIntent` it does not accept (out of its window and the 5 m lead, past the points, taken, or a set-back inside the guard) with its own log line. On a deadly lever a press refused as too late (taken, or the server's front at or past a live back's points, with the lever straight) tells that player "Too late to throw it." (`SwitchBackRules.Late` returning `TooLate`, `SwitchBackRules.TooLateLine`, a `LogMsg` to the one peer); any other refusal still gives the client no feedback. Neither is run over the wire (plan 8's measured client leads all had the client ahead; at ride 4's split in plan 12's runs the client ran behind). The quiet case, a press on a thrown deadly lever after its moment (`AlreadyThrown`), is run: the RIDE pass's mashed press on ride 3's taken back (step 5).

- **plan 14, the long train** (what its flows and the RIDE pass leave unverified):
  - no flow forces the countdown's hold to its end or the `SeatsAtOnce` sit-in at 8 s (`BoardMax`): the cages stand beside the train, so
    every walk ends inside the 2 s `MinWait`, and only `--test-chase`'s stop 3 holds a departure (to 2.4 s); both are unit-tested only;
  - the straggler's `SeatedLeadCatchUp` set-down is unexercised (`--test-crew`'s straggler walks back by its own path);
  - the hero-bot seat answer is not seen on the wire: no hero bot rides the RIDE pass's train when a crewed wagon is boarded, and
    `BoarderRules.PickAnswerer`'s bot branch is unit-tested only (a bot is told by `NetSession.IsServer && hero.Leader != null`);
  - the golem-burst free, and a hound in the air when its cart is freed, are not seen on a client: with `r5_hounds` at S 1080.0 (plan 14)
    the linger freed its cart before the burst; at S 1097.0 (plan 15) the hounds land only 0.60–0.68 s before the burst, which then frees
    their cart, and no RIDE check reads that on a client;
  - the RIDE pass's same-frame Fallen branch (`RideCavernWatch`, a copy going on the frame its leaper's Fallen sheet lands) has not fired
    yet: runs 2 and 3 took the later-frame path, and a Fallen-and-shown hero on that frame would fail a healthy run;
  - the pumped `r5_hounds` margin is estimated, not measured: below zero (−0.40 to −0.48 s) since plan 15 moved the group to S 1097.0
    (both flows ride that stretch unpumped, 0.60–0.68 s; see "Ride 5's lava pit" below);
  - a caster out of mana still counts as having a shot (`CompanionBrain.HasShot` checks arrows only, as `CanStrike` does), so it may hold a
    seat answer it cannot fire, until the 0.5 s grace or the wagon clears;
  - the 2.0 m `WagonReach` for a hero on foot beside the train is unmeasured (`=reach` measures seated reaches only);
  - a natural throw is not seen on a client: the pass's guard frees every natural hound on a crewed wagon 1 s after it boards, so only the
    scripted throw goes over the wire, and "only the scripted throw takes a prisoner" is not asserted;
  - `--test-chase`'s section-2 leap gate has nothing to check (its floor build after the mid-kiln load has no golem and no leap); section 1
    carries the chase's leap coverage;
  - a miss's flight is uncapped in game code (it lands beside the party lane's cell nearest where the body will be 0.6 s on, so a healthy
    miss can read 8–12 m); the flows print its length only and gate its take-off at `LeapReach`, which the crouch-within-reach rule bounds;
    no miss has occurred in any run, so the gate's miss branch is unexercised;
  - never exercised by any flow: the `Reseat` fall-back (`(seat … taken: first empty)`) and the `[rails] train: the save had wagons [..]
    lost` print (every load in the flows has `LostWagons` null);
  - unmeasured: the thrown prisoner's arc on a sharp curve (it ignores the train's speed: 3.6–4.4 m out of line on the measured straight).

**Known differences, accepted for now:**
- A partly knocked heap shows whole on clients, and so does a barricade with one or two hits in it (only "cut", "smashed" and "standing" go on the wire).
- **The RIDE pass's set-piece clocks and party pairs need an unclamped CPU.** Under the laptop's ACPI `low-power` profile the cores clamp to about 600 MHz once the third godot starts (ride 2), the server's physics falls behind its clock, and the clock and pairs checks fail. That was measured, not a code change: a890483 lags the same way, and every code section slows at once (see "The RIDE net test"). Run it on `balanced` or `performance`. The check is not loosened.
- A thrown remote body lands still, where a solo one slides at 0.3·V.
- A slag cart first seen through a correction on a bend may put its brake shoe on the other side.
- A boarder freed silently at a ride's end stands as a ghost for up to about 4 s.
- Hammers step at 60 Hz in a headset.
- **The long train** (plan 14's deferred Minors a player can meet):
  - a client plays a move's `cart_jump` and `cart_land` both at the landing, as the leap does (the server and solo play the jump at the
    press); an owner's feel call;
  - the throw's glow runs 1.5 s of real time: an `Enemy.Stagger` (a seat answerer's arrow in the last 1.5 s) lets it go out while the clock
    runs on, so a throw can come with no glow showing (a pause, a lever window or the bridge's hold, glows it again: see "The clock"); a
    client's glow is not cleared when the hound is knocked off mid-glow (it ends by itself within 1.5 s);
  - when a hero speaks the throw's call, nothing names the wagon (the log names it only when nobody speaks), and two wagons turning alone
    on one tick are called by the same hero at once;
  - an answerer on a crew bench stays there through a stop (arrivals do not end answers and moves are refused while held), so with two
    such answerers boarding crew could find a seat short;
  - "the place is booked" is also the refusal for a place simply occupied or a full cart, and a refused companion hop-in may walk to a held
    wagon;
  - a thrown prisoner's set-down is clamped 1.5–4.5 m out, so on a wide gap it can land on the enemy lane's cell short of its set-aside cell;
  - an emptied hound cart vanishes in view at the crossing (Ruling 19's cost);
  - a pumping shooter hands its seat answer on, and with both shooters pumping (a climb, a slow cart, before a jump) the wagon goes to a
    climbing blade;
  - the HUD's cart bar reaches a W4 rider only because its seat (10.4 m off) is inside `HudRange` (12 m);
  - a joiner on a floor whose train is lost shows the four wagons from the build until its first cart state lands;
  - `r4_hounds` boards at S 853.6, 5 m before the split's window, so its throw clock runs about 1 s before the window pauses it;
  - `r5_hounds`' cart (pace point S 1080.0) spawned inside plan 15's pit stretch (about S 1045–1089); plan 15 re-measured it to S 1097.0;
  - nobody may be left in the cart to pump `r5_jump` (plan 14's final review M1): its pumpers are counted among the cart's riders only
    (Ruling 12), so with a depleted party (the player in a wagon, every living companion down or climbing to a boarder) the train meets
    the lip at its 5.22 m/s cruise and drops in, and `SetPieceRules.JumpSpeed`'s "a lone player can always make it by pumping" holds only
    for a player in the cart; the jump's call is the cart's, so then it is spoken from a wagon (M7) or only logged. `r5_hounds` boards at
    S 1097, the pump window opens at S 1126 and the lip is at S 1156. Plan 15 Task 4 took the jump off floor 12 (ride 5's pit switch is
    thrown from the cart's seat, or taken round by R-lone), so this holds now only on a floor that lays a `jump`;
  - **owner's feel calls** (the final review, declined to judge): the hazard tolls and shakes (a barricade's smash, the runaway's ram, the
    cinders, the molten pour, every shake: `RailEncounterController.Barricades`, `.Runaway`, `.Switchyard`, `.Kiln`) reach the cart's
    riders only, so a wagon seat is exempt from them (Ruling 12 names neither side); and the seat answers make climbing to fight and the
    throw rare with the stock party: with Wren and Ilex alive every natural boarder in `--test-chase` was shot off from a cart seat within
    0.13–0.92 s (killed by damage at grip 1 of 3), so the natural rides never meet a knock-off by grip, a climbing answer or a throw, which
    only the hooked `--test-boarders` variants exercise (the amended Ruling 15, "ranged hits count").
- **Ride 5's lava pit** (plan 15 Task 4), an **owner's feel call**: a party pumping hard past ride 5's pit outruns `r5_hounds`: the
  golem's burst at S 1125.1 frees their cart with the hounds still aboard, in view. `r5_hounds` stands at S 1097.0, the first S with the
  train's rear home past `r5_switch_back` on its spawn tick; ridden unpumped its hounds land only 0.60–0.68 s before the burst (the flows'
  margin is 0.4 s, 0.2 s of headroom), and the pumped estimate is below zero.
- **Plan 15, the exit island** (the owner's feel calls, and what its flows and the RIDE pass leave):
  - **owner's feel calls:**
    - ride 5's descent foot reads about 2.3 g with the cart pumped;
    - how dark ride 4's dark section reads with the headlamp;
    - the exit bridge's and the cavern leap's feel;
    - the hazard tolls and shakes still reach the cart's riders only (a wagon seat is exempt, plan 14's call stands; the parked train's smash
      is the one toll that reaches the whole train, `SmashCost`'s `wholeTrain`);
    - the seat answers still make climbing to fight and the throw rare with the stock party;
    - the cave-ins' rockfalls are the MinesPack's faceted shards, not rounded boulders (with a thin brick band at room 1's doorway sides);
    - a pumped ride 5 outruns `r5_hounds` (above, "Ride 5's lava pit"); dropping the group is the owner's option;
    - a human seated in a wagon gets the pit's safe line for free (F19, as the spec has it: R-lone counts only a human able to throw from
      the cart's seat, so with the only human in a wagon the train takes the safe line);
    - the lift has no HUD line of its own (no new text was invented; its countdown is the chaser's);
    - boarding beside the parked train is mostly by its W1, from (56,61), with little standing room beside it (the cart's nearest free cell,
      (55,60), is a lane cell);
    - the lift's stand rule: the parked train stands as a lift whenever the party has no live cart, so at a no-spare wreck anywhere on the
      floor (the party at stop 1, say) "Another cart rolls out onto the line by the lava." is logged though nobody is near it (`SaysLiftLog`
      keeps it to once until a restore, never on a load and never before a fall's wipe; saying it only with a player outside is the owner's
      option);
    - after a smash that wrecks the cart with a spare left, the place is empty while the spare is held at stop 4: the parked train stands
      in the way again only at the next arming (the spare's departure), as the barricades do (B.5 says "in the way while held at stop 4");
  - **not run end to end:**
    - the relief at the lift (Ruling 46): no solo flow drives a relief up to the parked place, since a relief is sent only while the line is
      live; its halt there is pinned engine-free (`SpareRules.ChaseLimit`, `PullUpLine`) and the online chaser's by the RIDE pass;
    - a Mangled human outside the island (co-op): bound inside once its 30 s valve has fired and no human is up outside, else left where
      it fell for the one who is (`MangledRules.IslandBind`, `Banner`); this rests on those rows and on reading `ServerWorld.TickBound`: no
      solo flow runs the server's valve, and no co-op flow (the RIDE pass included) crushes a player outside on floor 12;
    - two RIDE checks are tight under load: each client's parked-smash PropEvt within 1.0 s of the server's, and the exit-bridge press 2 m
      and 0.5 s into the span. Both hold on an idle machine, which is why RIDE runs alone; if one ever goes red there, widen the window
      (1.5 s) rather than chase it;
    - a rider thrown from the parked train (only `DebugSeatParked` seats one: boarding is refused while it is in the way) and a client's
      reward-chest mend from the objective broadcast rest on reading;
    - a second player's press just after a takeover rests on reading (a second takeover is impossible once the place is Gone; `=takeover`'s
      double press is in one tick, solo);
    - a remote late press on the pit lever ("Too late to throw it." told over the wire) and a client's revive pin (a wagon's withheld revive
      read from a stale aware flag) are not in the RIDE pass: neither has a natural moment there (the pass throws the pit switch and it stays
      thrown; no aware enemy is within 6 m at its revives); they rest on `--test-pit=late`, `SnapshotRules`' rows and reading;
    - the straight answer from a held follower's search never ends its hold (on a sealed island no path and no straight answer can arise;
      that end of the hold is verified by reading), and a held follower stepped off the rails still stands where it was caught otherwise.
- **The chaser:**
  - it does not take crew on foot;
  - held short of a hole whose far side its limit cannot clear, it waits at the near lip; behind a train held at the end of the line it would wait there for good (its riders may get out).
  - its hop (`SpareRules.Hop`) handles one hole at a time: holes closer together than a cart length are not handled (floor 12 has none).
- **A lone remote rider** now faces one rim hound at a time and reaches only its own car's boarder with its blade (it climbs to a boarded wagon to fight there), but a ranged enemy riding a slag cart alongside (ride 2's Forge Priest) is out of a melee rider's reach and wears it down at lethality 1. The pass still tops heroes up.

**VR, not tried in a headset:**
- whether the eyes fuse the vignette;
- the feel of the ramps;
- the fill rate on a standalone headset.

The vignette also darkens the HUD sheet's edges while it is narrowed.

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

Co-op runs on a dedicated server (step-by-step setup of one, from a fresh cloud account to a running service, in [server-setup.md](server-setup.md)): the game started headless with `--server`, which any copy of the game joins from the title screen (Join Server) with a name and a class. Each player controls one adventurer; empty places can be filled with hero bots (below). The server simulates the world and every combat roll; clients own their own movement and send intents (attack, interact, bag moves, stat points, skills, dialogue choices, chat). Floors are built identically on every machine from the seed and the plan the server sends, so only divergent state crosses the wire. The stairs are a vote when two or more players are online: a Yes on the landing starts a ready-check and every HUD reads "Descend? 1/3 ready · 12 s" (`StairsReadyMsg`, once a second); the floor changes when all living players are ready (a downed player is not waited for) or when 15 s pass with a majority; a No from a living player on those stairs calls it off (so does nobody agreeing at 15 s); stepping off the landing is not a No. One player online takes the stairs at once, as a solo game does (`ReadyCheckRules.Needed`). The rule is the engine-free `ReadyCheck` in `src/Net/Core/ReadyCheck.cs`, the server's instance lives in `src/Net/ServerReadyCheck.cs` (`ServerWorld.StairsVote`, which `Stairs.Interact` consults on a server), and a client's Yes or No travels as `StairsReadyIntent` (`Stairs.Confirm`/`Decline`). The server re-checks `Locked` at Go time and logs the lock reason instead of changing floor. Talking to an NPC is local, but what the talk decides is the server's: a Join choice goes up as a `DialogueChoiceIntent` and the fighter's yes or no comes back as a `DialogueAnswer`, so the asker sees the right line and the follow starts on the server; freeing a prisoner, starting the rite or getting a downed fighter up go up as an `InteractIntent` on the NPC. Fighter NPCs only pick a fight while a standing party member is within 20 m (`NpcFight.PartyNear`), so Foehammer's bout with the orc by the stairs waits for the players instead of being settled while the server sits empty. A client never changes floor on its own: `Game.ChangeFloor`, `ReloadFloor` and `JumpToFloor` refuse on a client (a log line says the server decides), so debug keys, scripts and stray calls cannot leave a machine on a floor the server is not simulating; the only client path is the server's `FloorChange`.

```
godot --headless --path . -- --server --port=7777 --bind=0.0.0.0 --campaign=offline|builtin|ai --seed=N --clauses=a,b:2 --save=server --max-players=4 --autosave=60 --status-file=PATH --quit-when-empty
godot --path . -- --join=ws://192.168.1.5:7777 --name=Ash --class=class.rogue     # skip the title screen
scripts/export_server.sh                                                            # "Linux Server" preset (dedicated_server=true) into build/server/
SERVER_BIN=build/server/DungeonCrawler.x86_64 scripts/net_test.sh                   # the same check against the exported server
```

In a game: T opens the chat line, the floor label shows the round trip and how many are online, other players wear name tags, and the pause menu's Save/Load are replaced by Disconnect (the world never pauses on a server). The exported server prints a page of "Unreferenced static string" lines at startup; that is export-template noise, not a fault.

- Saves: the server autosaves to `user://saves/<--save>.json` every minute, on every floor change, and when someone leaves; started again on the same slot it resumes the world with every character parked, and the same name gets its character back (stored class wins). Save version 7 adds `Multiplayer` and `Players`. Clients never save; a single-player save in the server's slot is left alone.
- Render (`deploy/`): `Dockerfile` (debian-slim + the exported server + FastAPI), `frontdoor.py` (`GET /` health JSON from the server's status file, `GET /status`, `WebSocket /ws` relayed to the Godot server on 127.0.0.1:7777), `start.sh` (Godot then uvicorn on `$PORT`, saves and keys on the `/data` disk, `CLAUDE_API_KEY` secret written to `keys.cfg`), `render.yaml` (Docker web service, health check `/`, 1 GB disk, `CAMPAIGN`/`MAX_PLAYERS`/`SEED` env). Players join with `wss://<service>.onrender.com/ws`. Free instances sleep and drop everyone; use a paid plan. Local check: `docker build -f deploy/Dockerfile -t dc-server . && docker run -p 10000:10000 -v dc-data:/data dc-server`, then `scripts/net_test.sh relay ws://127.0.0.1:10000/ws`.

- Google Cloud (or any Linux VM) (`deploy/gce/`): no Docker and no front door, the server listens on the port you open in the firewall. On the dev machine `scripts/export_server.sh`, then `gcloud compute scp --recurse build/server deploy VM:~/dc --zone=ZONE`; on the VM `sudo bash ~/dc/deploy/gce/install.sh ~/dc/server` installs the runtime libraries, a `dungeon` system user, the build under `/opt/dungeoncrawler`, saves and keys under `/var/lib/dungeoncrawler`, and a systemd service (`dungeoncrawler.service`: port 80 by default, with `CAP_NET_BIND_SERVICE` so no root is needed; `sudo systemctl edit dungeoncrawler` to change the port, campaign or player count). Open the port with `gcloud compute firewall-rules create dungeoncrawler --allow=tcp:80 --target-tags=dungeoncrawler` and tag the VM. Players join with `ws://<external ip>` (port 80 needs no suffix). Re-run the same two commands to update; the service is stopped and restarted around the copy and keeps its saves. To run from the clone itself instead of an export, `deploy/gce/setup_source.sh` installs the runtime libraries, Godot 4.7.2 .NET and the .NET 8 SDK user-local (idempotent), imports and builds; `deploy/gce/start.sh` then runs the server in the foreground from `build/server` if present, else the latest published release, else from source. With git instead of scp: `scripts/publish_server.sh` exports and publishes the build as a GitHub release asset (`server-<date>` tag, the build itself stays out of git); on the VM, `git clone` the repo once and `deploy/gce/fetch.sh` pulls the scripts, downloads the latest `server-*` release (the repository is public, so no login) and installs it. Day to day on the VM, `deploy/gce/game.sh` is the menu: `restart` (resume the saved game; a world saved before the 2026-11-12 story rewire resumes the old story with many lines silent, so start that server fresh), `fresh` (a new game from floor 1, the old save kept as `server-<date>.json`), `update` (fetch.sh), `status` (service, status file, the address players use), `logs`, `stop`.
- macOS: the same server runs on a Mac. Either run it from the project with the macOS Godot 4.7.2 .NET editor and the .NET 8 SDK (`godot --headless --path . -- --server ...`), run the Linux Docker image under Docker Desktop, or use the exported bundle: `scripts/export_server.sh` also produces `build/macos-server/DungeonCrawler-server.zip` (a universal, ad-hoc-signed `.app`; the "macOS" preset makes the client the same way). Unzip and run `DungeonCrawler.app/Contents/MacOS/DungeonCrawler --headless -- --server --port=7777 --bind=0.0.0.0 --campaign=builtin`; macOS asks once to allow incoming connections, and saves go under `~/Library/Application Support/Godot/app_userdata/DungeonCrawler/`. The bundle is not notarized, so on another Mac right-click, Open once (or `xattr -dr com.apple.quarantine DungeonCrawler.app`).
- Transport: Godot's `WebSocketMultiplayerPeer` used as a raw packet peer (`src/Net/WebSocketTransport.cs`) behind the engine-free `INetTransport`; `ws://` on a LAN, `wss://` through a TLS-terminating host such as Render. No RPC, no `MultiplayerSpawner`: every message is a record in `src/Net/Core/Messages.cs`, encoded by `NetCodec` (one kind byte, JSON with the save layer's options, gzip above 512 B when that is smaller (it was 8 KB; every build decodes the flag); `Pose`, `WorldSnapshot` and `Vitals` are binary).
- **Traffic** (`Net/Core/TrafficStats.cs`, counted in `WebSocketTransport`): every packet in and out is counted by kind (messages and bytes, the kind read from the first byte, so it costs four increments). The server's status file carries a `net` object (totals, bytes a second, the busiest second, per kind); `--net-stats` makes the server print a line a minute (`[net-stats] ... top out: WorldSnapshot ...`) and any process print the whole table when it leaves or quits. `--net-soak` keeps a joining client's hero fighting (`Net/NetSoak.cs`) for measurements: a local server on floor 1 (`--server --floor=1 --campaign=builtin --seed=7319 --lethality=1 --net-stats`) and two `--join ... --call-bot=Brand --net-soak --net-stats --quit-after=150` clients. Measured that way (2026-11-13, 2 players + 2 bots in fights): about 7 KB/s to each client, of which `WorldSnapshot` (20 Hz, about 283 B) is 80%, `Vitals` 5-10%, `PartySync` 3%; under 0.2 KB/s from each client; the Welcome (113 KB gzipped, builtin campaign) is the peak second. WebSocket is not the bottleneck at these rates and gRPC would not change any of it (the drift bugs were protocol logic, not transport).
- **Sheet sync** (`Net/Core/MemberSync.cs`, protocol 14): a member's `PartySync` goes out when its sheet's content changes (the CharacterDto as JSON with hp, stamina, mana and effect timers left out: gear, level, xp, points, skills, an effect starting, ending or stacking, Mangled), or when an effect is refreshed (its timer differs by more than a second from the client's countdown), or when forced (a bot called in). It used to go out on every `MemberChanged`, which fires on every swing, hit, dodge and parry, while an effect running out raised nothing and clients kept an expired buff. Clients count effect timers down for the portraits only. `Vitals` skip a send whose bytes match the last one, with a one-second heartbeat (`Protocol.VitalsHeartbeatSeconds`). Net test step 17: the server gives the client's hero 2.5 s of Haste on its cheer, and the client's sheet must show it start and end; the fight step fails if the sheet followed every blow (`[net] fight traffic: N PartySync for M swings`).
- Engine-free core in `src/Net/Core/` (linked into the tests): `Protocol`, `PoseBuffer` (100 ms interpolation, 150 ms extrapolation cap), `PlayerRegistry` (name = identity, stored class wins, park/unpark), `ServerHandshake`/`ClientHandshake` (Hello -> Welcome -> Ready), `InMemoryTransport` for tests, `NetRefs` (`chest:3`, `door:1`, `stairs:down`, `loot:<guid>`).
- Godot glue: `NetSession` autoload (role, transport, command-line flags), `ServerWorld` (registry, welcome from a live `FloorStateCapture`, poses in, snapshots at 20 Hz with interest filtering, vitals at 10 Hz, log/sfx/objective forwarding, status file), `ClientWorld` (poses out at 30 Hz coalesced, snapshot/vitals in, remote bodies through `RemotePose`), `NetTestFlow` (the scripted check).
- **Hero bots** (protocol 11): a player fills an empty place with a story hero from `PartyController.DefaultRoster` (Esc
  menu, Heroes: `AddBotIntent(Hero)`; `DismissBotIntent(Member)` sends one back to town). The rules are engine-free in
  `src/Net/Core/PlayerRegistry.cs` (tests in `tests/Net/HeroBotTests.cs`): at most `MaxOnline` heroes in play, players
  and bots together; a hero already in play or a name a player uses cannot be called; only the owner sends a bot away;
  a player joining a full party sends the newest bot aside (`BotSteppedAside`), so nobody is turned away for a bot;
  joining under a bot's name takes that hero over; when the owner leaves, their bots follow the remaining player with
  the fewest (`BotHandedOver`) or, with nobody left, wait out of the world and come back with their owner
  (`ResumeBots`, also after a server restart: `PlayerDto.BotOwner/BotActive/BotWaiting`). A bot set aside keeps its
  member, so it returns with its level and gear. The server (`src/Net/ServerWorld.Bots.cs`) unparks the bot's body as
  `ControlKind.Companion` just behind its owner and sets `Adventurer.Leader`: the single-player companion AI then
  follows that body (`FollowLead`, formation from `PartyController.FormationSlotBehind`). Clients see a bot like any
  other remote body (`PlayerJoined`/`PlayerLeft` with `NetPlayerDto.BotOwner`, poses in the snapshot); `ClientWorld.Roster`
  feeds the Esc menu, the portrait tag `[Kim's bot]` and the "1 online, 1 bot" label. Every member's swing, shot or cast
  now reaches every screen as `SwingEvt` (`Adventurer.ShowSwing`), so bots and other players are seen attacking.
  `scripts/net_test.sh` calls Sable in, checks she follows the test player and sends her away (steps 31-32);
  `--call-bot=Sable` makes a client call one in three seconds into a game (for screenshots).
- Protocol 7 (the Depths): `PoseFlags.Parry = 64` and `PoseFlags.Dodge = 128` carry a client's defensive windows (the server judges the hit with its own `DefenseState`, fed by `ApplyRemote`); `EnemyHurt(NetId, Amount, Flavor, Hp, ByMember)` replaced the crit flag; `EnemyStateMsg` relays a non-horde enemy's wind-up or signature telegraph. `MessageKindCount` 82 (`CheerIntent` from the writing pillar, `ShopBuybackIntent` from items). Protocol 8: elites and fortune (88 kinds). Protocol 9 (run-structure): `DeathCardEvt`, `FloorCardEvt`, `RunRecapEvt` appended (91 kinds); the Welcome's `AdventureDto` carries `Clauses`, `Run` and `DailyDate`. Protocol 11 (hero bots): `AddBotIntent`, `DismissBotIntent`, `SwingEvt` appended (98 kinds); `NetPlayerDto` gained `BotOwner`. Protocol 12 (the item check): `LootManifest` and `LootResyncIntent` appended (100 kinds). Every `Protocol.LootCheckSeconds` (4 s) the server lists the ids of the items on its floor (`ServerWorld.LootCheck.cs`); a client frees any drop it shows that is not listed, or shown twice, and asks for the listed ones it lacks, which the server re-sends as `LootSpawned` to that client only. A client reaching for loot the server does not have also gets a `LootRemoved` at once. So a missed message costs seconds of a wrong floor, never a ghost item for good. The net test makes a ghost and hides a real drop on the client (step 33) and waits for both to be repaired. Protocol 13 (the state check): `StateCheckMsg` and `StateResyncIntent`, numbered 31 and 32 because the kind byte's top bit is NetCodec's gzip flag (127 is the last usable kind; `MsgRegistry` refuses 128 and up, and a test checks every kind). With each item check the server sends FNV-1a fingerprints (`Net/Core/StateCheck.cs`) of the bag, each member's lasting state (level, points, allocations, learned skills, equipment, rank, Mangled; never vitals, xp or effect timers) and the purse, plus whether the stairs down are sealed. A client re-hashes its copies, asks again for what differs (answered once per check per peer), and takes the server's word on the seal (`Stairs.SetLocked`). Step 33 of the net test also spoils the client's purse, bag and seal and waits for the repair.
- **Join window** (`ServerWorld`): broadcasts to a peer between its Welcome and its Ready (still building the floor) are held per peer and delivered in order on Ready (`FlushJoinQueue`), a FloorChange included. Moments (Vitals, Sfx, hits, swings, emotes, projectiles, barks) are never held; past 600 held messages only floor changes are kept and the catch-up repairs the rest, so the flush never meets the socket's 4096-packet limit. A join that throws in the handshake drops the peer instead of leaving it Welcomed. A joiner whose Ready names an older floor (or arrives mid floor-change, or whose Welcome described an earlier build of the same floor number: a wipe's reload during the join, by the floor epoch) keeps `Ready = false` until its FloorReady. Every join then gets `SendCatchUp`: the bag, every member, the purse, an altar card still waiting for a pick, and the floor's props (`PropStateMsg`); the others get the joiner's sheet right after `PlayerJoined`. Traps, secrets and hidden doors are spotted on the server only (`ScanForTrapsAndSecrets`), which sends each reveal and its line: a client's own scan revealed what the server had not, and the prop check could never take that back.
- **The prop check** (protocol 14, kinds 37-39; `Net/Core/PropCheck.cs`, `Net/PropSync.cs`, `Net/ServerWorld.PropCheck.cs`): with each item check the server sends `PropCheckMsg(Floor, Hash)`, the FNV-1a fingerprint of its floor's props: chests opened, sprung and jammed; every door open or seen (vaults, the safe room's, objective gates, the jail); levers pulled; secrets seen and open; the floor's traps seen, disarmed and sprung; barrels standing; the party's traps still armed (by id); the shrine used. A client captures its own props the same way (`PropSync.Capture`) and, on a mismatch, sends `PropResyncIntent`; the server answers that client alone with the whole `PropStateDto` (`PropStateMsg`, once per check interval per peer), which `PropSync.ApplyLive` applies silently: doors open and shut, levers go up and down, floor traps re-arm or spring, the party's traps appear or go; chests, barrels, secrets, sightings and the shrine only go forward. (`FloorStateCapture.Apply` is not used live: it frees enemies and spawns loot and placed traps again.) A difference the apply cannot mend is asked about once per server state, or every 20 s (`PropCheck.ShouldAsk`), and logged with what differs (`PropCheck.Differences`). Props used to travel only as one-shot `PropEvt`s, so one that went astray stayed wrong until a reconnect. Net test step 35 sets a party trap the server never set and shuts or opens a seen door, disarms or re-arms a seen trap and flips a lever (the server reveals a door and a trap first), and waits for the repair (`[net] prop check: the spoiled props were repaired`).
- **Props that never reached clients:** the party's traps have ids (`PropCheck.PlacedTrapBase` 2000 up, one past the highest on the floor; `PlacedTrapDto.Id`, saved, and carried by `TrapPlacedEvt`; a save from before ids gets fresh ones), so a creature springing one on the server reaches clients as `trap:N sprung` (`NetRefResolver` resolves the party's traps too). A press that splinters a barrel raises `barrel:N broken` (`Press.JamCheck`; `Breakable.Vanish` raises nothing). A ritual breach the server sealed is sealed on connected clients too (`RitualController.ApplyNet` applies `SealedMouths`, silently). A client's jailbreak prompts read the prisoner's snapshot flags (`JailbreakRules.StateFromFlags`) and its HUD count follows the objective state (`JailbreakController.ApplyNet`), never the follow.
- **The enemy check, the floor epoch and the floor watchdog** (protocol 14, kinds 54-56; rules in `Net/Core/WorldCheck.cs`, server side in `Net/ServerWorld.WorldCheck.cs`, tests in `tests/Net/WorldCheckTests.cs`):
  - *Floor epoch:* the server counts every floor it builds (`ServerWorld.FloorEpoch`, bumped in `OnFloorBuilt`, so a wipe's rebuild of the same floor number is a new build). The Welcome (`FloorEpoch`), each `FloorChange` (`Epoch`), the item check and the enemy check carry it; a client ignores a check about another build and the server ignores an ask about one (`WorldCheck.SameBuild`; 0 means unknown and matches). The server sends no checks while a floor change or a planner wait is under way (the floor number has moved on, the root has not).
  - *Item check, completed:* `LootManifest` also carries each item's count (a pile's coins, a stack's size); a client asks again for an item it shows with another count. `LootSpawned` is idempotent on a client (an item already shown is updated in place, so a resend that crossed the original never shows two). A joiner (`OnPlayerReady`) and a peer that built a floor it followed us to (`OnFloorReady`) get both checks at once instead of up to 4 s later.
  - *Enemy check:* with each item check the server sends `EnemyCheckMsg`, the net ids of its living enemies. A client frees a living enemy the server has no living copy of (unless it spawned it from a message in the last 1.5 s) and a second living copy of one net id; corpses are left to finish their fall. For listed enemies it has no living copy of it sends `EnemyResyncIntent`, answered once per check interval with `EnemySpawned` to that client alone (`DynamicEnemyDto.SpawnIndex` lets a planned enemy keep its identity). A ghost (an enemy whose death or freeing never reached a screen) used to stand frozen until a reconnect.
  - *Viewer orc:* `DungeonBuilder.SpawnedPlanned` records every planned spawn index a build placed, and `FloorStateCapture` saves any of them whose corpse is gone as dead, so the viewer orc (index 90000) no longer comes back on a reload or revisit, nor stands frozen for a late joiner.
  - *Floor watchdog* (`FloorWatchdog`, `ClientWorld.TickFloorWatch`): `FloorChangeBegin` starts it. With no `FloorChange` after 20 s, or 2 s after the client's own build of one threw (now caught, the loading screen stays up), the client sends `FloorResyncIntent`. The server answers that client alone with the floor as it stands (a `FloorChange` with `Resync` set, a live capture, the bodies where they are), unless it is still building one itself; then, for as long as an AI plan may be waited for (`Settings.PlanWaitSeconds` + 30 s since the change began), it sends that client the change's `FloorChangeBegin` again, which restarts its watch, so a long planner wait does not make every client rejoin. After three unanswered asks or two failed builds the client rejoins the server under its name (`NetSession.ServerUrl`), which the server treats as a rejoin with a whole new Welcome. Both worlds also catch an exception per message and log its kind, so one bad message costs that message only. A Welcome whose floor will not build leaves to the title screen with the reason instead of hanging.
  - Net test: step 36 makes a ghost creature and hides a real one on the client (and miscounts an item when one lies on the floor) and waits for the checks to repair them (`[net] enemy check: the ghost creature went ...`). Step 7 drops the FloorChange to floor 2 on the client (`ClientWorld.TestDropFloorChanges`; the watchdog waits 4 s in the test) and requires the floor to be asked for and built (`[net] floor watchdog: the lost floor change was asked for and built`).
- **Protocol 17** (plan 5: the mine cart in co-op, VR comfort and the tick-rate fix; `Protocol.Version` 17, `MessageKindCount` 114; a client of another version is refused at the handshake as before):
  - New kinds: 7 `FloorClock`, 8 `CartState`, 9 `CartCorrection`, 13 `Seat`, 14 `Leap` and 15 `RideIntent`. `FloorClock`, `CartCorrection` and `Leap` are transient (never held for a joiner); `CartState` and `Seat` are held and replayed before the rail catch-up.
  - Changed shapes: the Welcome and `FloorChange` carry `ServerTime` and `FloorSeconds`; `HazardPhaseMsg` carries the server's time and the phase's length in seconds, not ticks; the binary `Pose` is 45 bytes (`SeatYaw`, `PoseFlags2`) and the snapshot's `PlayerPoseState` carries both; `NpcState` bit 128 is seated; `Ping` carries the client's round trip.
  - Additive JSON fields: `PropStateDto.Rail`, `ObjectiveStateDto.CrewDownedLeft` (server only) and `FloorStateDto.ThrowersSent` (saved; null reads as empty).
  - Every send cadence runs by elapsed time (`Cadence`), so a server or a client at any physics rate keeps time; the client pings every second.
  - How the rails use all this (the two clocks, the carts, seats, leaps, remote riding, props, crew, joiners, the watchdog, the chaser, VR comfort and the `RIDE=1` pass) is under Rails, "Riding together".
- **Other co-op recovery:** the stairs landing re-asks twice a second while the local body stands on it alive with no question open and no answer given since it stepped on (`Stairs._PhysicsProcess`); a body that shows fallen while its state is alive stands up (`Adventurer._shownDowned`), and `Revive` counts any fallen body as a real revive so `MemberRevived` reaches clients whichever caller restored the hp; pressing E on a paid altar whose card a screen lost sends the card again. `Hud.Refresh` only redraws the floor label (it used to run the floor-change cleanup on every bag sync and closed the shrine card as "gone").
- **The bag guard** (protocol 14, `Net/ServerWorld.BagGuard.cs`): the shared bag is changed by many hands at once (bots drink and pick up, arrows run out, other players move things), so a cell index alone can land on a different item. `BagMoveIntent`, `QuickActionIntent` and `ShopSellIntent` carry `Expect`, the id of the item the screen showed (`ItemSlotControl` puts it in the drag data and the click; `PartyController` falls back to its own copy of the cell); the server acts only while the cell still holds it (`BagActions.Holds`; `Guid.Empty` checks nothing), else it answers that player at once with "That item moved; try again.", a `BagSync` and the member involved. `QuickActionIntent`, `AllocateStatIntent` and `LearnSkillIntent` also carry `Member`, the hero the screen acts for (the focused member; -1 means the sender's own): a player may act for their own hero and the bots that follow them (`PlayerRegistry.MayChange`, also the rule for drags onto a doll), so bots can be geared, given potions and have their stat and skill points spent by their owner. Anything else is refused out loud ("You can only change your own hero and your own bots.") with a fresh `BagSync`, never in silence. `Inventory.TryAdd` is all or nothing: a pickup that only part-fits stays whole on the floor (no stale stack count on other screens), and a Scavenger recovery the full bag cannot take says so instead of losing the arrows. Net test step 34 (with the bot in): a destroy aimed at an item that moved is refused, the bot drinks a potion its owner gives it, and a stat point for a hero who is not ours is refused with the line (the server puts 3 health potions in the bag for it).
- **Bots and the shared bag** (`Items/Core/LootPolicy.cs`): in co-op a bot always takes gold, keys, quest items and stacks that merge fully, takes a new-cell item only while more than 8 cells are free, never takes an item within 3 m of a standing human player, and never opens a bound chest. Items the party drops keep `DroppedByParty` through saves and rebuilds (`WorldItemDto.DroppedByParty`).
- **Stuck creatures:** `DungeonBuilder.WallRampSides` closes the sides of lower-level ramps that face a cellar tunnel (every peer builds it; the layout and its hash are unchanged); enemies take the straight shortcut only on their target's layer and stand still rather than walk at an unreachable target; sealing a breach crushes a creature in its mouth; `Enemy.Unstick.cs` returns a creature embedded in level geometry for two checks to the last place it stood free (server and solo, `[unstick]` in the log). `--dump-enemies` reports the cellar layer (`under=`). Protocol 10 (co-op): `StairsReadyIntent`, `StairsReadyMsg`, `PingIntent`, `PingEvt` appended at the end (95 kinds); a ping is `Hud.Ping` -> `PingIntent` -> the server's cooldown and range check (`PingRules`) -> `PingEvt` to everyone -> `PingMarkers.Place` (the `Label3D` under the floor root and the `PingBoard` the minimap draws). `FloorChange` gained `EliteSpawns` (the floor's elite spawn indices): a client's first visit places the server's roster (`DungeonBuilder.ForcedEliteSpawns`) instead of rolling its own from its copy of the extras, and prints `[net] floor N elites match|MISMATCH`.
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
