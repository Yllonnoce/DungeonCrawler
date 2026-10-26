# Viewer cast in their campaign roles

**Date:** 2026-10-24. **Status:** design, awaiting approval.

## Intent

Four characters were modelled, rigged and exported for Godot from the 3dObject viewer today:
`tamsin_rook_godot.glb`, `oskar_vell_godot.glb`, `brand._godot.glb` and `sister_ione_godot.glb` (each 15 k
triangles, one 2048 texture, about 10 MB, 26 clips including `Idle`/`Walk`/`Run`/`Punch`/`Death`/`HitReact`/`GetUp`).
They are not generic extras: the campaign design (`2026-10-22-campaign-gullet-of-gorm.md`, character briefs in
`2026-10-22-character-briefs.md`) gives each a place in the story.

- **Brand** is one of the four party members (`PartyController.DefaultRoster`: Brand the warrior, Sable, Wren, Ilex).
- **Tamsin Rook** (Caves, floor 6 "The Swallow"), **Oskar Vell** (Forge, floor 11 "The Hearth") and
  **Sister Ione** (Abyss, floor 16 "The Dream Door") are the three recruitable fighters, one per tier, in the mould of
  Foehammer on floor 1.

The owner's instruction: "wire them for their campaign roles". Today the game has nowhere for these files to plug in:
party members use Quaternius class models hard-wired in `Adventurer`, and the three fighters exist only in the design
documents, not in the built floor data.

## Goals

1. Brand's party member wears `brand.glb` instead of the Quaternius Warrior, with the gait, attack, hit, death and
   revive clips the viewer export carries.
2. Tamsin Rook, Oskar Vell and Sister Ione stand on floors 6, 11 and 16 of the builtin campaign as fighter NPCs: they
   fight enemies that come near, can be asked to join, follow the party across floors once they have, and do not
   reappear on their home floor while they are travelling with the party, exactly as Foehammer does. (Companion
   travel is server-side today: a co-op client does not see a travelling companion on later floors. Existing gap,
   unchanged here.)
3. Fighters become a thing the floor data can declare (`NpcPlan` gains a mode and a model path), so future authored
   floors can place them without C#.
4. Build, unit tests and the headless run stay green; the new behaviour has unit tests where the logic is engine-free.

## Non-goals (this round)

- The trait systems from the campaign design (Jovial/Greedy, the floor-8 greed test, `left:tamsin`, Ione's gas test).
  Nothing in the code models traits yet; the fighters join and fight with the same `NpcFight` rules as Foehammer.
- Sable, Wren and Ilex: no exports exist yet. The member-to-model table is written so adding them is one line each.
- Weapons in the party's hands: companions with Quaternius bodies do not show their equipped weapon either. Brand's
  export carries grip bones, so hanging the equipped KayKit weapon from them is a natural follow-up, not this change.
- Restoring joined companions on load-game (a known gap, unchanged).
- Adding the three fighters to the campaign `Cast` list or giving them barks.
- Surviving a campaign rebake. `BuiltinCampaign.Dir` prefers `user://adventures/builtin` once the owner has pressed
  "Bake campaign", and the baker regenerates every floor from the planner, so hand-written fighter entries in the
  shipped floor files would be shadowed or lost by a rebake. No user copy exists on the owner's machine today
  (checked 2026-10-24). The fighters go in as data, as the campaign design intends; code-placing them as a fallback
  is a separate decision (see the open question in the handover).

## Anti-goals

- Breaking the AI planner's output schema: the new `NpcPlan` fields must be `[AiIgnore]`, as `VoiceId` is, so the
  designer never has to produce them and old authored plans still validate.
- Changing how Quaternius party members and NPCs move or animate. Viewer-specific behaviour (no tint, `ViewerGait`
  pacing, `Punch`/`HitReact` clip names) is switched on only when a viewer model path is in use.
- Regressing floor 1's debug cast (Brannock, Goddess, Foehammer, the orc).

## Constraints

- Godot 4.7 mono, C#. Verify with `dotnet build DungeonCrawler.csproj`, `dotnet test tests/DungeonCrawler.Tests.csproj`,
  `godot --headless --import --path .` after adding assets, a headless run
  (`godot --headless --path . -- --builtin --no-ai --seed=7 --print-map --quit-after=10`) and xvfb screenshots with
  `--floor N --face-node=<NodeName>:2`.
- Floor data is JSON under `data/adventures/builtin/floor_NN.json`, deserialised through `PlanFormat` (System.Text.Json
  with `SaveFormat.Options`) and passed through `PlanValidator`, which drops NPCs in vault/arena/jail/shrine rooms and
  NPCs with no dialogue, and clips names to 40 characters.
- Model files live at `assets/models/viewer/<name>.glb`; `AnimatedModel.Attach` scales any glTF to a target height
  measured in its Idle pose, rotates it 180°, and plays clips by name.
- The owner commits; leave work uncommitted on `master`. The viewer's dev server on port 3000 is not to be touched.

## Design

### 1. Assets

Copy the four exports into `assets/models/viewer/` as `brand.glb`, `tamsin_rook.glb`, `oskar_vell.glb`,
`sister_ione.glb` (the viewer's `brand._godot.glb` double dot is dropped). Run the headless import so `.import` files
exist. Sizes match the existing viewer assets (11–14 MB each).

### 2. Brand as a party member

`Adventurer` currently picks a Quaternius model per class (`ClassModels`) and clip names per class (`ClassClips`).
Add a small engine-free table, `Party/Core/PartyModels.cs`:

- `PartyModels.For(memberName, classId)` returns `(path, melee, cast, hit, viewer)`. A member in the viewer table,
  keyed by roster name **and** class (`("Brand", "class.warrior") → res://assets/models/viewer/brand.glb`), gets the
  viewer path with clips `("Punch", "Punch", "HitReact")` and `viewer = true`; anyone else gets the Quaternius path
  and clips from the existing per-class tables, moved into this file unchanged.
- Both keys because names are not fixed everywhere: co-op players name their own members (`Hello.PlayerName`,
  `ServerWorld.CreateMember`) and saves carry saved names. Requiring the class too means a co-op rogue who types
  "Brand" keeps a rogue's body and dagger clips; a co-op warrior named Brand gets the viewer body, which is fine.

`Adventurer._Ready` calls it. When `viewer` is true: tint amount 0 (the texture is the character), and the gait is
chosen with `ViewerGait.GaitFor(1.8f, planar, current)` plus `Player.SpeedScale = ViewerGait.SpeedScaleFor(...)`
while walking or running, the same lines `Npc.ShowGait` uses, applied at both places `Adventurer` chooses
Idle/Walk/Run (the local-physics path and the networked-pose path). Everything else (`Death`, `Idle` on revive,
crouch scaling, hiding the body for the local camera, flashes) is already clip-name agnostic.

Visibility note for the owner: the local player's own model is hidden so it does not block the first-person camera.
Brand is roster slot 0, so the player sees Brand only after switching lead to another member, in co-op, or in a
`--possess`/`--face-node` headless screenshot. That is existing behaviour, not a change.

### 3. Fighters declared in floor data

`NpcPlan` gains two properties, both `[AiIgnore]`, both optional, both absent from every existing file:

- `Mode` (`NpcMode`, default `Quest`), serialised as a string like other enums in the plan format.
- `ModelPath` (`string?`): a `res://` path; null keeps the per-id Quaternius pick.

`DungeonBuilder`'s plan-NPC loop copies both onto the spawned `Npc`, and skips an NPC whose id is in
`Main.Instance.Companions` (a fighter travelling with the party), which is the rule `PlaceViewerCast` already applies
to Foehammer. `Npc._Ready` already sizes HP by mode and uses `ModelPath` when set, so nothing else changes in `Npc`.

`PlanValidator` keeps a fighter with no quest (it already does: quests are optional) and additionally clears a
`ModelPath` that is not a `res://` path, with a repair note, so a bad hand edit degrades to a Quaternius body
rather than a missing model.

### 4. The three fighters in the builtin floors

Add one `Npcs` entry each, hand-written in the same shape as Old Ulf's entry (id, name, room, script, persona;
`QuestId` null; `VoiceId` null so `Voices.ForNpc` assigns one; `Mode: "Fighter"`; `ModelPath`):

| Floor | Id | Name | Room | Why that room |
|---|---|---|---|---|
| 6 The Swallow | `npc_tamsin` | Tamsin Rook | 1 "The Tooth Shelf" (Hall) | the gold teeth she is mining; first hall past the start |
| 11 The Hearth | `npc_oskar` | Oskar Vell | 3 "Crucible Walk" (Hall) | away from Ulf in the Great Hearth (8): he left and is too proud to go back |
| 16 The Dream Door | `npc_ione` | Sister Ione | 2 "Antechamber of Soft Singing" (Passage) | a pilgrim at the threshold, before the Choir |

Each script is three nodes, Foehammer's shape: a greeting with the choices "Will you fight alongside us?" (`Join`,
`Next: yes`, `NextIfRefused: no`) and a farewell; a `yes` node; a `no` node. Lines are written in each character's
voice from the briefs (Tamsin jovial and greedy, Oskar patient and proud, Ione pious and frightened), at most
two sentences each, no system talk. Persona `VoiceStyle` is a short phrase in the style of the existing entries;
`DefaultDelivery` is `cheerful` for Tamsin, `calm` for Oskar and `whisper` or the nearest existing delivery id for
Ione (the builder checks the delivery ids in `Voice` and picks from them).

`ShowMoves` stays off for the three: they are story characters, not the floor-1 demo cast.

Joining uses the existing `NpcFight.Decides` rule: a 70 % roll hashed from the game seed, the NPC id and the ask
number. The builtin campaign plays with its fixed seed (7319), so the answers are fixed for every player. With the
ids above they are: Tamsin yes on the first ask (no, no after that if declined and re-asked), Oskar yes, Ione yes.
A unit test pins those three first-ask answers so a renamed id or a changed rule is noticed.

### 5. Tests

- `PartyModelsTests`: Brand resolves to the viewer path with `Punch`/`HitReact` and `viewer = true`; Sable the rogue
  resolves to the Quaternius Rogue with `Dagger_Attack`; an unknown class falls back to the existing defaults.
- `NpcPlan` round-trip through `PlanFormat`: `Mode` and `ModelPath` survive serialise/deserialise; a plan without
  them deserialises to `Quest`/null.
- `PlanValidator`: a fighter NPC with no quest in a Hall room is kept; a non-`res://` model path is cleared with a
  repair note.
- `BuiltinCampaign`: floors 6, 11 and 16 each contain their fighter with `Mode == Fighter`, a `res://` model path
  whose file exists under `assets/`, a start node and a `Join` choice.

### 6. Verification the builder runs

Build, tests, headless run, then screenshots: `--floor 6 --face-node=Npc_npc_tamsin:2`, floor 11 for Oskar, floor
16 for Ione, and Brand via `--possess` of another member or `--face-node` on Brand's adventurer node, each with
`--after=10 --quit-after=16`. The log line `[AnimatedModel] res://assets/models/viewer/<name>.glb: ... scale ...`
confirms each model loaded and stood up.

## Left to the builder

- Exact dialogue wording within the voice notes above.
- Whether `PartyModels` keeps the class colour tint for Quaternius bodies by passing it through unchanged (it should).
- Cell choice within a room is `PlanResolver`'s, as for every NPC.
- Delivery ids for the three personas, from the existing `Voice` delivery list.
