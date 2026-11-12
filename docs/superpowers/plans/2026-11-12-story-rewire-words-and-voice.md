# Story Rewire, Phase 1 (Words and Voice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the shipped campaign's plot and every line that carries it with the owner's new storyline, make the orc the antagonist and last boss, and re-record every changed line in each speaker's existing voice.

**Architecture:** The campaign is data: `data/adventures/builtin/campaign.json` and `floor_01.json` to `floor_20.json`. Phase 1 rewrites text inside the existing schema by hand and changes one small piece of code (which body the antagonist wears). Layouts, seeds, objectives and `LayoutHash` values are not touched. A new test file pins the story's structure, the banned old-plot words and the voice assignments; a script exports the campaign as a readable script for the owner.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, untracked by owner decision), Python 3 for the export script, Runware text-to-speech (`inworld:tts@1.5-max`).

**Spec:** `docs/superpowers/specs/2026-11-12-gullet-story-arc.md`. Read it first; it holds the storyline, the floor-by-floor beats and every character's need. Phases 2 to 4 of the spec are out of scope here.

## Global Constraints

- The campaign is rewritten by hand in the shipped files. Never run `--bake-campaign`; it regenerates every floor.
- Do not change `LayoutHash`, `Rooms`, `Collectives`, `Loners`, `Chests`, `Traps`, `EnemyTotal`, `GameSeed` (7319), any `Objective`, `Theme`, `Layout` or `SpecOverrides`.
- Choice flags keep their ids: `told_tobit` (set floor 3, pays floor 16), `grisel_forgiven` (set floor 9, pays floor 12), `wake_gorm` and `let_him_dream` (set floor 18, pay floors 19 and 20). "Lend us your strength" sets `wake_gorm`; "sleep" sets `let_him_dream`.
- The four clue ids and floors stay: `clue_ledger` 4, `clue_contract` 9, `clue_moulds` 13, `clue_letter` 16. Their text becomes the wardens' log, the dismissal order, the moulds, the Verger's letter.
- Every NPC and hero keeps the voice already assigned (table in Task 3). The orc is the only new speaker: Gemini name `Fenrir` (rendered as Inworld Dominus).
- Every line is written in the speaker's own sex: Sable, Tamsin, Ione, Grisel and Maud are women; everyone else is a man.
- Writing rules from the spec: a character states what they want the first time they speak; no riddles; at most one image per speech; a character speaks differently once the party holds a clue.
- New and rewritten lines are at most 220 characters.
- Hero barks, banter and dance lines are kept unless they refer to the removed plot or a new line helps the story. No dance line is removed.
- Voice spend: Runware only, stop and report if the bake's estimate exceeds 5 dollars (cap 15).
- Run `dotnet build DungeonCrawler.csproj` before any `godot --headless` command. Game flags go after `--`.
- Do not build installers or run `export_all`/`package.sh`.

## Review Focus

1. **A player with a save from the old story.** Flags and saved bark history must still load; a save on floor 12 or 19 must find its NPCs. Test in Task 8.
2. **A taunt with no clean twin.** Players with villain cursing off, and co-op clients, are shown `CleanLine`; every taunt must have one. Test in Task 3.
3. **A co-op party with renamed heroes.** No NPC, taunt or narration line may address a hero by name (Brand, Sable, Wren, Ilex). Test in Task 3.
4. **A stale user copy of the campaign.** A campaign under `user://adventures/builtin` shadows the shipped one, so the tester may be reading the old story. Checked in Task 10.
5. **A changed line that was never re-recorded.** After the bake every `VoiceId` in the campaign must resolve in `assets/voice/builtin`. Checked in Task 9.

---

### Task 1: Branch, baseline and the readable script export

**Files:**
- Create: `scripts/story_script.py`
- Create: `docs/story/` (output folder, committed)

**Interfaces:**
- Produces: `python3 scripts/story_script.py [--act N] [--out FILE]` writes the campaign as Markdown: per floor, its title, the campaign `Story` beat, then every spoken line in play order as `**Speaker:** text` (intro line, narration, NPC scripts with their choices indented, quest lines, floor taunts, horde lines, boss lines). `--act 1..4` limits it to floors 1-5, 6-10, 11-15, 16-20. A final section lists campaign taunts, merchant lines and banter.

- [ ] **Step 1: Branch.** `git switch -c story/rewire` from `feature/depths`. Commit the spec and this plan: `docs: story rewire design and phase 1 plan`.
- [ ] **Step 2: Record the baseline.** Run `dotnet build DungeonCrawler.csproj`, `dotnet test tests/DungeonCrawler.Tests.csproj`, `godot --headless --path . -- --check-voice-bank`. Note the passing test count and the bank's line count in the commit message of Step 4. Any failure here is pre-existing: list it, do not fix it.
- [ ] **Step 3: Write `scripts/story_script.py`** to the interface above. It reads only; it must not write under `data/`.
- [ ] **Step 4: Verify.** `python3 scripts/story_script.py --out docs/story/before.md` produces a file whose floor 3 section contains "Welcome to the First Station". Commit: `chore: story script export and the old story as docs/story/before.md`.

### Task 2: Character sheets

**Files:**
- Create: `docs/story/character-sheets.md`

**Interfaces:**
- Produces: the reference every writing task follows. One section per speaker: the six cast (Maud, Crane, Tobit, Grisel, Ulf, Pellet), the Verger (new cast id `cast_quill`), the orc (Morg Ashtusk, the Herald), the four companions, the narrator, the four heroes.

- [ ] **Step 1: Write a sheet per speaker** with: sex and voice; who they are in the new story (from the spec's character table); the one thing they want from the party, as a sentence they could say aloud; their state in each of the four acts; how they talk (two or three traits and one sample line); three things they would never say. For the orc add: he was killed on floor 1 and recast, he serves the Sleepless, he mocks the party as hired help, he never begs.
- [ ] **Step 2: Add the shared word list.** Use: the mines, the drains, the sluice, the drain-wardens, the Herald, the Sleepless, the dream. Never use: nineteen, plumb-, inspector, pilgrim, lullaby, memorial stone, "the gag".
- [ ] **Step 3: Self-check** each sheet against the spec's floor table: every floor where the spec names a character has a matching act state. Commit: `docs: character sheets for the story rewire`.

### Task 3: Tests that pin the new story

**Files:**
- Create: `tests/Adventure/StoryRewireTests.cs` (load the shipped campaign and floors with the same helper `tests/Adventure/GulletCampaignTests.cs` uses at its top)
- Modify: `tests/Adventure/GulletCampaignTests.cs` (the Verger tests at lines 24, 57, 87, 97)

- [ ] **Step 1: Write the failing tests** in `StoryRewireTests.cs`:

| Test | Asserts |
|---|---|
| `TheAntagonist_IsTheOrc_OnFenrir` | `Antagonist.Name == "Morg Ashtusk"`, `Epithet == "The Herald"`, `Persona.Voice == "Fenrir"` |
| `TheVerger_IsACastMember_OnHisOwnVoice` | cast holds `cast_quill`, name "Absalom Quill", `Persona.Voice == "Sadaltager"` |
| `EveryCastVoice_IsUnchanged` | `cast_maud` Gacrux, `cast_crane` Umbriel, `cast_tobit` Puck, `cast_grisel` Despina, `cast_ulf` Orus, `cast_pellet` Achird, narrator `Charon` |
| `TheFourClues_KeepTheirIdsAndFloors` | ledger 4, contract 9, moulds 13, letter 16; `PayoffFloor == 18` |
| `EveryFloor_HasAStoryBeat` | all twenty campaign `Floors[].Story` are non-empty |
| `NoLine_UsesTheOldPlot` | no `Text` in the campaign or any floor, and no `Story`, `Synopsis`, `Role`, `Secret`, `Beat`, contains (case-insensitive) `nineteen`, `plumb`, `inspector`, `pilgrim`, `lullaby`, `memorial stone` |
| `NoNpcOrVillainLine_NamesAHero` | no NPC script, quest, narration or taunt text contains `Brand`, `Sable`, `Wren` or `Ilex` as a whole word |
| `EveryTaunt_HasACleanTwin` | every campaign and floor taunt has a non-null `CleanLine` with non-empty text |
| `NoNewLine_IsOver220` | every `Text` in NPC scripts, quests, narration, taunts, tier and boss lines is at most 220 characters |
| `OnlyKnownFlags_AreUsed` | every `Flag`, `RequiresFlag`, `ForbidsFlag`, `AltDeathFlag` is one of `told_tobit`, `grisel_forgiven`, `wake_gorm`, `let_him_dream` or starts with `left:` |
| `TheLastBoss_IsTheHerald` | floor 20 `Boss.Name == "Morg Ashtusk"` and `AltDeathFlag == "wake_gorm"` |
| `CraneBetraysNobody` | no floor NPC with `CastId == "cast_crane"` has a non-null `Betrayal` |
| `TheVerger_StandsOnFloorNineteen` | floor 19 has an NPC with `CastId == "cast_quill"` and `Ritualist == false`; Maud is still the floor's ritualist |

- [ ] **Step 2: Rewrite the four Verger tests** in `GulletCampaignTests.cs` for the new roles: the orc and the Verger both have bodies in the viewer folder; the Herald dies in different words under `wake_gorm`; the Herald speaks at half health either way.
- [ ] **Step 3: Run** `dotnet test tests/DungeonCrawler.Tests.csproj --filter "StoryRewireTests|GulletCampaignTests"`. Expected: the new tests FAIL (old data), everything else as in the baseline. No commit (tests are untracked); note the failing set.

### Task 4: The antagonist wears the orc's body

**Files:**
- Modify: `src/Adventure/Core/NpcBodies.cs:51` (`Villain`)
- Test: `tests/Adventure/GulletCampaignTests.cs`

**Interfaces:**
- Produces: `NpcBodies.Villain(string name, string? epithet, string? style)` returns `"orc1"` when the normalised text contains `orc` or `ashtusk`, `"verger"` when it contains `verger`, else null. `NpcBodies.ForCast` returns `"verger"` for `cast_quill`.

- [ ] **Step 1: Write the failing tests:** `Villain("Morg Ashtusk", "The Herald", "a huge orc...") == "orc1"`; `Villain("Absalom Quill", "The Verger", null) == "verger"` (unchanged); `ForCast` of a cast member named "Absalom Quill" with role containing "verger" is `"verger"`.
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement** in `NpcBodies.cs`. `DungeonBuilder.cs:1090` already calls `Villain` for the last boss; confirm `NpcBodies.Path("orc1")` resolves to `assets/models/viewer/orc1.glb`.
- [ ] **Step 4: Run them; expect PASS.** Commit: `feat(story): the antagonist can wear the orc's body`.

### Task 5: The campaign file's story frame

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (`Synopsis`, `Antagonist`, `Cast`, `Mystery`, `Factions[].Name`, `Tiers[].Beat` and `EnterLine`, `Floors[].Story` and `Mood`, `ArcNotes`)

- [ ] **Step 1: Rewrite the frame** from the spec: synopsis from "The story in brief"; `Antagonist` to Morg Ashtusk / The Herald with a persona for a huge, contemptuous orc chieftain and `Voice: "Fenrir"`; add `cast_quill` to `Cast` with `Voice: "Sadaltager"` and the old antagonist's voice style; each cast `Role` becomes who they are now and each `Secret` becomes what they want and when it comes out; `Mystery.Question` becomes "Who shut the drains, and why?", `Truth` the Verger letting the Herald in, the four clue texts as the Global Constraints give them, `Payoff` the floor 18 scene; one `Story` beat for each of the twenty floors copied from the spec's floor table; `ArcNotes` restating where each flag pays.
- [ ] **Step 2: Leave alone** `Barks`, `Banter`, `Taunts`, `Merchant` (Tasks 7 and 8) and every non-text field.
- [ ] **Step 3: Verify.** `dotnet test ... --filter StoryRewireTests`: `TheAntagonist_IsTheOrc_OnFenrir`, `TheVerger_IsACastMember_OnHisOwnVoice`, `EveryCastVoice_IsUnchanged`, `TheFourClues_KeepTheirIdsAndFloors`, `EveryFloor_HasAStoryBeat` PASS. `godot --headless --path . -- --builtin --quit-after 30` boots floor 1 with no "adapted" or repair warning.
- [ ] **Step 4: Commit:** `story: the campaign frame for the new storyline`.

### Task 6: The floors, one act at a time (four passes)

Do this task four times: floors 1-5, 6-10, 11-15, 16-20. Each pass is its own commit and its own review.

**Files:**
- Modify: `data/adventures/builtin/floor_NN.json` for the act's five floors

**Interfaces:**
- Consumes: `docs/story/character-sheets.md`; the campaign `Floors[].Story` beats from Task 5.

- [ ] **Step 1: Rewrite each floor's text** to its beat: `IntroLine`, every `Npcs[].Script` node and choice, `Quests[]` title, objective and lines, `Narration[]`, floor `Taunts[]` (in the orc's voice, each with a `CleanLine`), `Hordes[].Line`, and on boss floors `Boss` lines. Set `VoiceId` to null on every line whose text changed. Keep each NPC's `Id`, `Room`, `CastId`, `QuestId` and each quest's `Template` and `Params`.
- [ ] **Step 2: Add NPCs only where the spec's floor table needs one that the floor lacks.** A new NPC copies the shape of an existing cast NPC on another floor (same `CastId` and `Persona`), takes a `Room` that already exists on the floor, and has no quest unless the spec names one.
- [ ] **Step 3: Act-specific changes.**
  - Act 1: floor 1's narration states that the orc killed at the mine head will be recast; floor 3's choice still sets `told_tobit`.
  - Act 2: floor 7 keeps Grisel as the ritualist, now working a seized pump; floor 9's choice still sets `grisel_forgiven`.
  - Act 3: floor 12 keeps both Grisel branches (`RequiresFlag`/`ForbidsFlag` on `grisel_forgiven`); floor 13 names the orc's own mould among the moulds.
  - Act 4: floor 16 keeps both Tobit branches on `told_tobit`; floor 17 sets Crane's `Betrayal` to null and gives him the sluice scene; floor 18's two choices set `wake_gorm` and `let_him_dream`; floor 19 adds the `cast_quill` NPC (not the ritualist) and keeps Maud as ritualist; floor 20's `Boss.Name` becomes "Morg Ashtusk" with new intro, half, death, alt-half and alt-death lines and `AltDeathFlag: "wake_gorm"`.
- [ ] **Step 4: Verify the act.** `python3 scripts/story_script.py --act N --out docs/story/act-N.md`; read it top to bottom against the writing rules. For each floor: `godot --headless --path . -- --builtin --floor=NN --no-ai --quit-after 20` loads with no "adapted" or repair warning. `dotnet test ... --filter "StoryRewireTests|GulletCampaignTests"`: no new failures on this act's floors.
- [ ] **Step 5: Commit:** `story: act N rewritten (floors A-B)` including `docs/story/act-N.md`.
- [ ] **Step 6 (after act 1 only): owner checkpoint.** Give the owner `docs/story/act-1.md` and wait for their read before writing acts 2 to 4. Their corrections go into the character sheets first, then the lines.

### Task 7: The Herald's taunts and the merchant

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (`Taunts`, `Merchant`)

- [ ] **Step 1: Rewrite all campaign taunts** as the orc, keeping each entry's `Trigger`, `Objective`, `RequiresFlag`, `ForbidsFlag` and placeholders (`{floor}`, `{party}`). Every taunt gets a `CleanLine`; where the coarse line has no coarse word the twin is the same text. Add flag-gated variants where a clue or choice changes what he would say (at least one each for `told_tobit`, `grisel_forgiven`, `wake_gorm`). Null the `VoiceId` of every changed line.
- [ ] **Step 2: Review the merchant section.** Keep any line that fits; rewrite those that name the old plot. Keep the briefing's node ids `hire`, `quakes`, `deal`, `sendoff`, every path reaching `deal`, and the seed gold of 150 in the deal line. Pellet's arc stays bluster, unease, fear. He still never states the Mystery's `Truth`.
- [ ] **Step 3: Verify.** `dotnet test ... --filter "StoryRewireTests|GulletCampaignTests|WritingCampaignTests"`: `EveryTaunt_HasACleanTwin` and all merchant tests PASS except those that require voiced lines (they pass after Task 9). `godot --headless --path . -- --test-writing` and `--test-objective=toxic` print their RESULT lines as in the baseline.
- [ ] **Step 4: Commit:** `story: the Herald's taunts and the merchant's lines`.

### Task 8: Hero lines, banter and dances

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (`Barks`, `Banter`)
- Create: `docs/story/hero-line-changes.md`

- [ ] **Step 1: List the lines to change.** Search `Barks` and `Banter` for the banned words and for references to the removed plot (the contract as a trap, names carved, "the verger" as the one in charge, the party as victims). Write each hit and its replacement to `docs/story/hero-line-changes.md`. Everything not listed stays byte-for-byte, with its `VoiceId`.
- [ ] **Step 2: Apply the replacements,** nulling only those lines' `VoiceId`.
- [ ] **Step 3: Add twelve new banter exchanges,** three per act (`MinFloor` 1, 6, 11, 16), that move the mercenaries from "it's a job" to caring whether the town lives; two of them gated on `grisel_forgiven` and `told_tobit`. Two to four lines each, in the existing `Banter` entry shape.
- [ ] **Step 4: Dance lines.** Keep all twenty per hero. Replace at most five per hero, only where a funnier line is clearly better; list each swap in the changes file.
- [ ] **Step 5: Save-compatibility test.** Add `OldSaveFlags_StillFindTheirFloors` to `StoryRewireTests.cs`: with `grisel_forgiven` set, floor 12 offers an NPC with `CastId == "cast_grisel"` and no `ForbidsFlag` on that flag; with it unset, likewise for the other branch; the same for `told_tobit` on floor 16 and `wake_gorm`/`let_him_dream` on floors 19 and 20.
- [ ] **Step 6: Verify.** Full `dotnet test`: only the "is voiced in the shipped bank" tests fail. Commit: `story: hero lines, banter and dances`.

### Task 9: Re-record

**Files:**
- Modify: `data/adventures/builtin/*.json` (new `VoiceId` values written by the bake)
- Modify: `assets/voice/builtin/` (new recordings, old ones swept)

- [ ] **Step 1: Count.** Count lines with a null `VoiceId` across the campaign and floors, by speaker. Check each speaker against the voice table in the spec and Task 3; the orc's lines must resolve to Fenrir/Dominus, the Verger's to Sadaltager/Conrad.
- [ ] **Step 2: Estimate and gate.** The last bake cost 1.94 dollars for 1,162 lines. If the estimate for this count exceeds 5 dollars, stop and report. Note the Runware balance.
- [ ] **Step 3: Bake** with `RUNWARE_API_KEY` in the environment:
  `godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-all-voice=assets/voice/builtin`
  Copy the bake's `all-plans` output over `data/adventures/builtin`, then `godot --headless --path . --import`. If some renders land in the user voice cache instead of the bank, copy them into the bank by id and import again.
- [ ] **Step 4: Sweep and check.** `godot --headless --path . -- --purge-voice`, then `--check-voice-bank`: every referenced line is held and loads. No `VoiceId` in the campaign or floors is null except the taunts' `CleanLine` twins.
- [ ] **Step 5: Pitch check** the new recordings by speaker (median f0): women 160-235 Hz, men 70-145 Hz; Brand, Ulf and the narrator are known to read high. Any speaker on the wrong side is a wrong voice: fix the mapping and re-render that speaker only.
- [ ] **Step 6: Verify.** Full `dotnet test` passes at or above the baseline count. Note the balance after. Commit: `voice: the rewired story recorded (N lines, $X)`.

### Task 10: Sweep, documents and hand-back

**Files:**
- Modify: `docs/development.md` ("Story: cast, mystery, choices", "The dungeon's master", "Viewer characters")
- Modify: `README.md` (the Story mode blurb)
- Modify: `docs/superpowers/specs/2026-10-22-campaign-gullet-of-gorm.md` (one line at the top: superseded by the 2026-11-12 design)

- [ ] **Step 1: Play-check from a clean state.** Move any campaign under the Godot user folder's `adventures/builtin` aside so the shipped files are the ones read. Run `--builtin` on floors 1, 3, 9, 12, 16, 18, 19, 20 with `--dump-enemies=40 --quit-after=45`; floor 20's boss is the orc body; floor 19 holds the Verger.
- [ ] **Step 2: Run every flow that touches story:** `--smoke`, `--test-story`, `--test-writing`, `--test-quest`, `--test-tavern`, `--test-objective=` for `toxic`, `shiftingvault`, `ritual`, `jailbreak`, and `scripts/net_test.sh`. Compare with the Task 1 baseline; a flow that failed there may still fail, nothing else may.
- [ ] **Step 3: Export the whole story.** `python3 scripts/story_script.py --out docs/story/after.md`.
- [ ] **Step 4: Update the documents** to the new storyline, roles and antagonist.
- [ ] **Step 5: Commit:** `docs: the rewired story in the manual and README`. Report to the owner: line counts changed and kept, voice cost, the flows' results, and `docs/story/after.md` to read.
