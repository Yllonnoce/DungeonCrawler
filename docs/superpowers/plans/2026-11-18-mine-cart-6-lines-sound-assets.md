# Mine Cart Chase, Plan 6: lines, taunts, sound, music, CC0 assets and polish

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12's chase gets its voice, its sound and its look:
- The orc taunts the cart moments.
- The heroes call out every cart warning in their own words.
- Grisel names her crew stop by stop.
- The ride sounds like a ride: rolling, clacks, screech, bells, the foundry, and every set piece. Every peer hears it where it happens.
- A Chase music mood plays on the rides, and the ride-4 hammers fall on its beat.
- CC0 models and textures dress the carts and props.
- Four leftovers from plans 3–4 are fixed.

**Architecture:** Plan 6 has three parts.
- **Lines live in the banks and data the voice pipeline already reads, so a later voice pass needs no code change.**
  - Taunts go in the campaign's `Taunts` and `TauntBank.Stock`.
  - Hero call-outs go in a new stock bank, `RideBarks`, topped up like the other bark banks.
  - Grisel's calls go in a new `NpcPlan.Calls` list. They are said as narration with her name in the text, as the villain's taunts are, so a co-op client shows who speaks.
  - Rail narration becomes a real `Line`.
  - All of them are subtitles (`VoiceId` null) until the owner OKs a voice pass.
- **Sound is local on every peer.**
  - Each cart body carries a `CartSound`, fed each frame from its run (server or single player) or its `CartFollower` (client, plan 5).
  - Edges in the synced state trigger the one-shots: the Halted flag, plating, track, lane crossing and the jump arc.
  - The wreck and the derail play from the tumble that already runs on every role, because a spare takeover and a freed slag cart hide those edges.
  - Prop sounds come from the live `PropEvt` path.
  - Set-piece sounds come from the floor clock that drives their visuals.
  - No rail cue goes through the broadcast `EventBus.PlaySfx` any more.
- **Rules are engine-free** and live in `src/Dungeon/Rails/Core` (`RideLines`, `RideSound`, `RideMusic`, `RailHud`), where the test project compiles them.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (tests/ is local, not in git), ffmpeg (for cutting clips; on the PATH).

**Spec:** `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md`. The sections that apply:
- "Grisel and the story": the taunt triggers and the voice;
- "Feel": the sound list and the chase track;
- "Assets";
- "Rolled over": the warnings;
- the "Boarding and departing" call-out.

**Builds on:**
- Plans 1–4: `RailController`, its `CartEvents`, `RailEncounterController` and its partials, the set pieces, `CrewRescueController`.
- Plan 5, as written (it runs before this plan):
  - `CartStateDto` and `CartFlags` (Halted 1, Wrecked 2, Hidden 4, LineBlocked 8, GateClear 16, Derailed 32), plus the party fields `GapFrom`, `GapTo`, `JumpV` and `BoardingLeft`;
  - `CartKind`, `CartFollower` and `RailNet`;
  - client posing that sets `Cart.Speed` and `Cart.Curvature`;
  - `RefreshHud` on clients;
  - `DungeonBuilder.FloorSeconds` and `FloorTick` on every role;
  - set pieces as client visual copies, with `Show*` methods and live `PropEvt`s;
  - the client bucket schedule and heap-lantern rule (plan 5 Task 7);
  - the golem windup raising `EventBus.EnemyTelegraph` (plan 5 Task 7);
  - the `RIDE=1` net test.
- Code map: `.superpowers/sdd/shared/plan6-code-map.md`.
- Asset picks: `.superpowers/sdd/shared/plan6-assets.md`.
- Rulings: `.superpowers/sdd/mine-cart-rulings.md`.

## Global Constraints

- **No paid voice.**
  - Never run a `--bake-*` command, `--ai`, or anything with `RUNWARE_API_KEY` or Gemini keys.
  - Run every headless check from the terminal profile, which has no keys. `EnqueueFloorVoice` and `EnqueueBarks` queue every new line, and with a key present the game would render them as it plays.
  - Every new line is a subtitle with `VoiceId` null, written where the pipeline reads it (Task 1 makes sure it does).
  - Speakers and their voices follow the voice table in memory (`voice-assignments.md`):
    - Morg Ashtusk, man (Fenrir → Dominus): every taunt. He calls the party "hirelings".
    - Grisel Thorn, woman (Despina → Abby): flat and wry, says the worst about herself first.
    - Brand (blunt soldier), Wren (dry, few words) and Ilex (precise scholar) are men. Sable (sly thief) is a woman.
    - The narrator (Charon): rail encounter lines.
    - The crew stay silent ("past speaking"). Oskar Vell keeps only his kill barks.
- **Line rules.** These are the shipped-story tests in `tests/Adventure/StoryRewireTests.cs` and `WritingCampaignTests.cs`:
  - ≤ 220 characters.
  - Shipped taunts carry no `{token}`, and every one has a `CleanLine`.
  - No NPC, villain or narration line names Brand, Sable, Wren or Ilex.
  - Only known flags.
  - No old-plot words (nineteen, plumb, inspector, pilgrim, lullaby, memorial stone).
  - Hero lines are in the speaker's own person and sex.
- **Log lines keep their exact text.** `src/Core/DebugTools.Chase.cs` matches them (for example "The trestle groans under the cart.", "The golem slams the cart!"). New lines are added beside the log lines, never instead of them. One exception: the lock log `Grisel works the lock. '…'` becomes "Grisel works the lock." because her line now follows as narration. Nothing matches it.
- **CC0 only.** The repo is public.
  - Every file added under `assets/` gets an `assets/LICENSES.md` entry: source URL, author, licence, and the cut taken.
  - No CC-BY, so no `CREDITS.md` (ruling of 2026-11-18).
  - Downloads go into a fresh scratch directory first; only the listed files are copied into the repo, at the listed paths.
  - After adding assets, run `godot --headless --import --path .` and commit the `.import` siblings.
  - No edits under `scripts/`. Running the existing `scripts/fetch_polyhaven.py` is fine.
- **Single player changes only in audio, visuals and lines.**
  - Collision shapes, layers, timings, damage, AI and the saves are unchanged. The one timing exception is the hammer stagger, 40 → 45 ticks (Task 6, controller ruling).
  - After every task these print RESULT ok, each run after `dotnet build DungeonCrawler.csproj`:
    - `godot --headless --path . -- --test-ride`
    - `godot --headless --path . -- --test-ride-edges`
    - `godot --headless --path . -- --test-crew=condemned`
    - `godot --headless --path . -- --test-crew=forgiven`
    - `godot --headless --path . -- --test-chase`
  - So does `dotnet test tests/DungeonCrawler.Tests.csproj`.
- **Lines are chosen on the server or in single player only.** Clients hear them through the existing NarrationMsg and BarkMsg paths. No new wire messages; the protocol stays at plan 5's version 17.
  - `BarkMsg` names its speaker by party member index (`ServerWorld.cs:73`), so it can only carry a hero.
  - An NPC (Grisel) speaks as narration, `Grisel Thorn: "…"`, through the director's narration queue. `NarrationMsg` carries the text.
- **Positional sound uses plan 5's synced state on clients:** follower S and V, `CartStateDto` mode, flags, plating, track and gap, `FloorSeconds`, and live `PropEvt`s.
  - A PropState catch-up (a joiner, a load or a resync) plays nothing.
  - A dedicated server (`NetSession.IsServer`) does no sound work.
- **No per-frame allocation** in `CartSound`, `RideSoundscape` or the music decision. Players are pooled per cart.
- **Shipped JSON edits keep System.Text.Json's escaping**, so the diff shows only the new entries:
  - two-space indent;
  - `'`, `&`, `<`, `>`, `+` and non-ASCII written as upper-case `\uXXXX`.
- **Housekeeping.**
  - `tests/` is not in git.
  - Flags go after `--`.
  - No installers or dist builds.
  - Commit only `src/`, `data/`, `docs/` and `assets/`.
  - Every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Single player plays the same.** Only audio, visuals and lines differ. The five SP runs print RESULT ok with their new sound, music and line checks, and every collision shape keeps its size and place under the new models.
2. **Nothing is heard twice or by the wrong peer.** No rail cue goes through `EventBus.Sfx`, and the RIDE client counts zero rail `SfxMsg`s. Positional sounds play locally from synced state. A load or a joiner hears no replayed props.
3. **Every new line reaches the voice pipeline unvoiced.** `VoiceBudget.Candidates`, `TauntCandidates` and `VoiceRefs.Ids` see every new line (unit tests). `EnqueueFloorVoice` walks the same lists (code review: it is Godot-side). No line is rendered. The line rules hold.
4. **Every asset is CC0 and credited.** Every cue the rail code raises has a clip. Test: `RideSoundTests` scans the sources and the disk.
5. **The orc and the heroes speak at the right moments.** A rolled-over hero gets a RolledOver taunt, never the Press's Crushed line. Warnings still come ≥ 1.5 s before hits (`--test-chase`). Grisel's calls come only in the forgiven branch, and they tell that branch's story: she ran and left her crew; she never kept them caged for the orc.

---

### Task 1: Lines the voice pipeline can read; the director's entry points

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/CartEvents.cs`;
  - `src/Adventure/Core/Plans/LineOrTextConverter.cs`;
  - `src/Dungeon/Rails/Core/RideLines.cs`.
- Modify:
  - `src/Dungeon/Rails/CartRun.cs` (:134-140): the event types move out;
  - `src/Adventure/Core/Plans/FloorPlan.cs`: `RailEncounterPlan.Line` (:253), `NpcPlan` (:205), a new `NpcCallPlan`;
  - `src/Adventure/AdventureDirector.cs`: `EnqueueFloorVoice` (:104-136), `CallOut`, `SayAs`;
  - `src/Adventure/AdventureDirector.Talk.cs`: `RailTaunt`;
  - `src/Adventure/Core/Voice/VoiceBudget.cs`: `Candidates` (:64);
  - `src/Dungeon/Rails/RailEncounterController.cs`: `ShowLine` (:188);
  - `data/adventures/builtin/floor_12.json`: the three `RailEncounters` lines.
- Test:
  - `tests/Adventure/LineOrTextConverterTests.cs`;
  - `tests/Adventure/RideVoiceTests.cs`;
  - `tests/Dungeon/Rails/RideLinesTests.cs`;
  - `tests/Adventure/RailEncounterPlanTests.cs` (:127, :131);
  - `tests/Adventure/StoryRewireTests.cs` (`Spoken`, :179).

**Interfaces:**
- **`CartEventKind` and `CartEvent`** move unchanged, with their doc comments, from `CartRun.cs:134-140` to `src/Dungeon/Rails/Core/CartEvents.cs`, in the same namespace `DungeonCrawler.Dungeon.Rails`.
  - `CartRun.cs` uses Godot, and the test project compiles only `Rails/Core/**`.
  - `RideLines` and its tests need the enum.
  - No caller changes.
- **`RailEncounterPlan.Line`** becomes `[JsonConverter(typeof(LineOrTextConverter))] public Line? Line`.
  - `LineOrTextConverter : JsonConverter<Line?>` reads a JSON string as `new Line(text)`, so a save from plans 2–4 still loads. It reads an object as a `Line` and null as null. It always writes the object form.
- **`public sealed class NpcCallPlan { string Key; Line Line; }`**, plus:
  - `[AiIgnore] public List<NpcCallPlan> Calls` on `NpcPlan`;
  - `public Line? CallFor(string key)` on `NpcPlan` (first match, case-blind).
- **`RideLines`** (engine-free; Tasks 2 and 3 add to it):
  - `public const string CageCall = "cage";`
  - `public static string? GriselCallFor(CartEventKind kind, int stopLeft)` returns `$"ride{stopLeft + 1}"` for `Departed`, and null otherwise.
- **`AdventureDirector`** (all return false or do nothing on a client):
  - `public bool CallOut(Characters.CharacterState? member, BarkTrigger trigger)` is `Bark(member, trigger, scripted: true)`. Warnings skip the bark gaps, but still choose with `ChooseOrOldest`, mark said and set the clock.
  - `public bool SayAs(string speaker, Line line, string onceKey)`: an NPC's line, said once per run.
    - It queues `$"{speaker}: \"{line.Text}\""` with `line.VoiceId` on the director's narration queue, under the key `$"call:{floor}:{onceKey}"`. The entry is the same tuple `PlayNarration` adds (`AdventureDirector.cs:511`).
    - It returns false, and queues nothing, when that key is already in `BarksSaid` or `_narrationQueued`.
    - `TickNarration` raises it when the voice channel is free and marks the key `Forever`. The line waits for a taunt being said, and comes once per run.
    - It goes out as `RaiseNarration` → `NarrationMsg`, so a co-op client sees the name in the text. A `BarkMsg` would show no name: it carries a member index, which is −1 for an NPC (`ServerWorld.cs:73`, `PlayBark` :301).
  - `public bool RailTaunt(Objectives.TauntTrigger trigger, string? arg = null, bool urgent = false)` is `Authority && Taunt(trigger, Kind, arg, urgent)`. It goes in Talk.cs, beside `Kind`.
- **The voice pipeline:**
  - `EnqueueFloorVoice` queues every non-empty `plan.RailEncounters[i].Line` in the narrator's voice (`VoicePriority.Narrator`), as hordes are.
  - It also queues every `npc.Calls[i].Line` with the NPC's `VoiceId`, `Speaker.Npc`, `VoicePriority.Npc`, `major` and persona, as script nodes are.
  - `VoiceBudget.Candidates` adds each NPC's calls right after its script nodes, with reason `$"{npc.Name}"`, in both the story and the quiet loops. It adds rail lines after hordes, with reason `"rail"`.
  - `VoiceRefs.Ids` keeps them through `Candidates`; no change is needed there.
- **`ShowLine`** raises `RaiseNarration(enc.Line.Text, enc.Line.VoiceId)` when the text is not empty.
- **`floor_12.json`:** the three rail lines become objects (`VoiceId` null), reworded for the narrator:
  - `r1_lone`: "A cinder stands square on the rails ahead, waiting for the cart."
  - `r2_slag1`: "Lamps on the slag line: the orc's carts are coming up alongside."
  - `r4_golem`: "The kiln wall bursts outward, and something huge drops onto the slag line."

- [ ] **Step 0: Move the event types.** Move `CartEventKind` and `CartEvent` to `Rails/Core/CartEvents.cs`, then run `dotnet build DungeonCrawler.csproj` and `dotnet test tests/DungeonCrawler.Tests.csproj`. Both pass with no other change.
- [ ] **Step 1: Tests.** Write them, watch them fail, then implement.
  - `LineOrTextConverterTests`:
    - `"Line":"x"` → Text "x", VoiceId null;
    - an object round trip keeps VoiceId and Delivery;
    - null stays null;
    - a whole `FloorPlan` with the old shape deserializes through `PlanFormat.Deserialize`.
  - `RideVoiceTests`:
    - `Candidates` holds a rail line with reason "rail" and an NPC call right after that NPC's nodes;
    - `VoiceRefs.Ids` contains a call's VoiceId and a rail line's VoiceId once they are set.
  - `RideLinesTests`: `GriselCallFor(Departed, 0) == "ride1"`; Arrived → null.
  - `RailEncounterPlanTests` read `.Line!.Text` and expect the new texts.
  - `StoryRewireTests.Spoken` yields `f.RailEncounters` lines and every `npc.Calls` line, so the 220-character and hero-name rules cover them.
- [ ] **Step 2: Implement.** Before the edit, check the floor: `godot --headless --path . -- --builtin --no-ai --floor=12 --quit-after=6 | grep -E "repaired|dropped|demoted|adapted"`. After the edit, the same command prints nothing new.
- [ ] **Step 3: Build and the regression runs** (Global Constraints). `--test-chase` still sees "something huge" through its own checks. If it matches the old golem text, switch that match to the encounter id `r4_golem`.
- [ ] **Step 4: Commit** `feat(lines): rail narration and NPC calls reach the voice pipeline`.

### Task 2: The orc taunts the cart moments

**Files:**
- Modify:
  - `src/Adventure/Core/Objectives/Taunts.cs`: the enum (:7), `HoldSeconds` (:105), `TauntBank.Stock` (:133);
  - `src/Dungeon/Rails/Core/RideLines.cs`;
  - `src/Dungeon/Rails/Core/RunOverRules.cs`: the source names;
  - `src/Dungeon/Rails/RailController.cs` (`SourceName` :32, `Wreck` :669, the CartEvents raise);
  - `src/Dungeon/Rails/SlagCart.cs` (`SourceName` :22, `Derail` :122);
  - `src/Dungeon/Rails/RunawayWagon.cs` (`SourceName` :14);
  - `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (`SpawnGolem` :249, `TickBuffer`);
  - `src/Adventure/AdventureDirector.cs` (:610);
  - `data/adventures/builtin/campaign.json` (`Taunts`);
  - `src/Core/DebugTools.Ride.cs`: two checks.
- Test:
  - `tests/Adventure/BarkTriggerTests.cs` (:19);
  - `tests/Adventure/TauntTests.cs` (:136);
  - `tests/Adventure/WritingCampaignTests.cs` (:57);
  - `tests/Dungeon/Rails/RideLinesTests.cs`.

**Interfaces:**
- **`TauntTrigger`** appends, after `Gamble`: `RideStart, RolledOver, CartWrecked, SlagDerailed, GolemEntrance, BufferSmashed`.
  - `KindOf` returns None for all six.
  - `VoiceBudget` rank stays at the default, 5.
  - They are **not** added to `PromptBuilder.cs:53` or `CampaignPlan.cs:144`. They are house triggers: the designer never makes railed floors, and the stock covers any campaign.
- **`HoldSeconds`:** `GolemEntrance => GolemHoldSeconds` (`public const double GolemHoldSeconds = 10`). The taunt waits for the narrator's wall line instead of being dropped.
- **`RideLines.TauntFor(CartEventKind kind, int stopLeft, int stopCount) → TauntTrigger?`:**
  - Departed with `stopLeft <= stopCount − 3` → RideStart. That is rides 1–3; ride 4's opening belongs to the golem.
  - Wrecked → CartWrecked.
  - Anything else → null.
- **`RunOverRules`:**
  - `public const string PartyCartSource = "the warden cart", SlagCartSource = "the slag cart", RunawaySource = "a runaway wagon";`
  - `public static bool IsCartSource(string source)`.
  - The three Godot classes' `SourceName`s point at these constants, with values unchanged.
- **Hooks** (server and single player only):
  - `AdventureDirector.cs:610` uses `RunOverRules.IsCartSource(src) ? RolledOver : Crushed`. A cart run-over is no longer gloated over as the Press.
  - In `RailController`, where `Raise` hands the queued events out: `TauntFor(...)` → `RailTaunt(t, urgent: t == CartWrecked)`.
  - `SlagCart.Derail`, after the derail log → `RailTaunt(SlagDerailed)`.
  - `SpawnGolem`, after `ShowLine` → `RailTaunt(GolemEntrance, urgent: true)`.
  - The buffer smash in `TickBuffer` → `RailTaunt(BufferSmashed)`.
- **`TauntBank.Stock`:** two lines per new trigger, `Kind` null, no tokens. Each clean twin is a fixed point of `Soften`.
  - RideStart:
    - coarse "All aboard my little railway. Mind the bends, mind the carts, mind everything, you bloody tourists."; clean "All aboard my little railway. Mind the bends, mind the carts, mind everything, you tourists."
    - "Ride my rails, then. Nobody has reached the end of the line yet. Not in one piece." (same both)
  - RolledOver:
    - coarse "Should have stayed in the cart. My carts don't stop for anyone, you poor sod."; clean "Should have stayed in the cart. My carts don't stop for anyone, you poor fool."
    - "Flattened. The rails will want a scrub. Somebody fetch a bucket." (same)
  - CartWrecked:
    - "Your cart is scrap. Walk, then, and listen for the bell. It rings for you." (same)
    - "Wrecked! Oh, that was lovely. Do it again; there's a spare at the next stop." (same)
  - SlagDerailed:
    - coarse "Derailed my cart? Damned vandals. I'll send two more."; clean "Derailed my cart? Vandals. I'll send two more."
    - "Off the rails and into the slag. Never mind. I've a yard full of carts." (same)
  - GolemEntrance:
    - "You thought that was the end of it? Something in the wall disagrees." (same)
    - "Meet the one I keep for heroes who get this far." (same)
  - BufferSmashed:
    - "Through the buffer and off my line. Fine. The stairs are yours. So is the bill." (same)
    - coarse "Smashed my buffer. Bloody marvellous. Go on, then. Go down."; clean "Smashed my buffer. Marvellous. Go on, then. Go down."
- **`campaign.json` `Taunts`:** Morg's own lines, `Objective` null, each with a `CleanLine` (`VoiceId` null). `TauntBank.Fill` must stay 0 for the shipped campaign.
  - RideStart:
    1. "My ore line, hirelings. That cart hauled slag at a penny a ton. You're worth less, and you'll go out the same way." (clean the same)
    2. "Ride, then. My cinder crews push carts down that line all day, and not one of them looks where it's going." (same)
    3. "Hold on tight, hirelings. I lose a cart a week down there, and I've never once sent anyone to look for it." (same)
    4. With `ForbidsFlag: grisel_forgiven`: "Grisel's lying by my first cage, hirelings, with my keys still on her belt. Mind you don't run her over twice." (same). It is ride-neutral because RideStart fires on rides 1–3.
  - RolledOver:
    1. "Under the wheels. Should have stayed in the cart, hireling. The cart was the one thing down here I gave you for free." (same)
    2. "Flat on my rails. My crews will scrape you off at the end of the shift, and dock themselves the time." (same)
    3. coarse "That's what the bell was for, you stupid bugger. My carts don't stop. They never bloody stop."; clean "That's what the bell was for, you fool. My carts don't stop. They never stop."
  - CartWrecked:
    1. "There goes the warden cart. Good iron, that was. I'll take it out of your wages, if you live to draw any." (same)
    2. "Wrecked. Walk the rest, hirelings, and listen for the bell. It rings for you now." (same)
  - SlagDerailed:
    1. "That was my cart and my crew, and neither cost me a penny. Throw all you like. I've a yard full of both." (same)
    2. coarse "Off the rails and into the slag. Bloody vandals. The next cart is heavier."; clean "Off the rails and into the slag. Vandals. The next cart is heavier."
    3. "Break my brake shoes, then. I buy them by the cartload, hirelings, and I pay the smith in cinders." (same)
  - GolemEntrance:
    1. "Thought the last cage was the end of it? I keep something in my kiln wall for hirelings who get this far." (same)
    2. "One stop from my stair, hirelings. Very good. Now outrun what I built to haul the heavy loads." (same). It does not claim every cage is open: ride 4 departs whether or not they are.
  - BufferSmashed:
    1. "Through my buffer and off my line. Fine. Take your grey little crew home. I'll send the town the bill for the buffer." (same)
    2. coarse "You broke my line from one end to the other, hirelings. Bloody expensive. I'll be waiting where the rails don't reach."; clean "You broke my line from one end to the other, hirelings. Expensive. I'll be waiting where the rails don't reach."

- [ ] **Step 1: Tests.** Write them, watch them fail, then implement.
  - `BarkTriggerTests`: the taunt tail `names[^13..]` is Crushed … Gamble, RideStart, RolledOver, CartWrecked, SlagDerailed, GolemEntrance, BufferSmashed.
  - `TauntTests.Stock_HasTwoLines_ForEveryNewTrigger…` covers the six. `Stock_CoversEveryTrigger_WithCleanTwins` passes unchanged.
  - `WritingCampaignTests.TheVerger_HasHisOwnLines_ForEveryNewTrigger` adds the six names: ≥ 2 own lines each, and `Fill == 0`.
  - `RideLinesTests`:
    - `TauntFor(Departed, 0, 5) == RideStart`;
    - `TauntFor(Departed, 3, 5) == null`;
    - `TauntFor(Wrecked, 2, 5) == CartWrecked`;
    - `IsCartSource` is true for the three sources and false for "The Press" and "The drop hammer".
  - `TauntTimingTests`: `HoldSeconds(GolemEntrance, false) == 10`.
- [ ] **Step 2: Implement.** Edit `campaign.json` with the escaping rule.
- [ ] **Step 3: Headless checks** in `DebugTools.Ride.cs`. They subscribe to `EventBus.NarrationEx`:
  - after the companion is thrown and mangled, a `Morg Ashtusk:` narration whose text is one of the RolledOver lines (campaign or stock) arrives within 1 s, and no Crushed line does. It prints `[ride] taunt RolledOver "…"`;
  - after the wreck step, a CartWrecked line arrives, printing `[ride] taunt CartWrecked "…"`.
- [ ] **Step 4: Build and the regression runs.**
- [ ] **Step 5: Commit** `feat(lines): the orc taunts the ride, the run-overs, the wrecks, the derails, the golem and the buffer`.

### Task 3: The heroes call the warnings; Grisel names her crew

**Files:**
- Create:
  - `src/Adventure/Core/Voice/RideBarks.cs`;
  - `src/Dungeon/Rails/RailCalls.cs`.
- Modify:
  - `src/Adventure/Core/Plans/CampaignPlan.cs` (the enum, :6);
  - `src/Adventure/Core/Voice/VoiceLines.cs` (`BarkDelivery` :119);
  - `src/Adventure/Core/Validate/CampaignRepair.cs` (`TopUpSet` :186);
  - `src/Dungeon/Rails/Core/RideLines.cs`;
  - `src/Dungeon/Rails/RailController.cs` (`Warn` :628-659, the Boarding raise);
  - `src/Dungeon/Rails/RailEncounterController.cs` (:340-351);
  - `src/Dungeon/Rails/RailEncounterController.SetPieces.cs` (:270-283);
  - `src/Dungeon/Rails/RailEncounterController.Leaps.cs` (:162, and the helper at :293);
  - `src/Dungeon/Rails/RailEncounterController.Switchyard.cs` (:222, and the helper at :288);
  - `src/Dungeon/Rails/RailEncounterController.Kiln.cs` (`TickJump` :595);
  - `src/Adventure/Objectives/CrewRescueController.cs` (`GriselLine` :27, :345-346, `OnCartEvent`);
  - `data/adventures/builtin/floor_12.json` (`npc_grisel.Calls`);
  - `src/Core/DebugTools.Chase.cs` (`OnChaseMessage`; the file is new and moving, so find methods by name) and `src/Core/DebugTools.Crew.cs`.
- Test:
  - `tests/Adventure/RideBarksTests.cs`;
  - `tests/Adventure/BarkTriggerTests.cs`;
  - `tests/Adventure/CampaignRepairBarksTests.cs`;
  - `tests/Adventure/AiPlannerTests.cs` (:417);
  - `tests/Dungeon/Rails/RideLinesTests.cs`.

**Interfaces:**
- **`BarkTrigger`** appends, after `CheerAnswer`: `CartComing, AllAboard, Heads, HoundLeap, LineBlocked, JumpCall`.
  - `BarkDelivery`: CartComing, Heads and LineBlocked → Shout; HoundLeap and JumpCall → Frantic; AllAboard → Cheerful.
  - In `AiPlannerTests.BarksPrompt…` the six join the house-trigger list (stock only, never asked of the planner), and "exactly 69 lines" stays.
- **`RideBarks`**, shaped like `ReactiveBarks`:
  - `Triggers` (the six);
  - `MinLines = 2`. A trigger with fewer lines gets the whole tier: 3 for a hero or the generic bank, 2 for a class. A class-only member therefore reaches it too;
  - `For(t, name, classId)`, by roster name, then class, then generic;
  - `int TopUp(BarkSet set, string classId)`.
  - `CampaignRepair.TopUpSet` adds `+ Voice.RideBarks.TopUp(set, classId)`.
  - The lines (hero lines 3 each; class lines 2 each; generic 3):

  | Trigger | Brand | Sable | Wren | Ilex |
  |---|---|---|---|---|
  | CartComing | "Cart! Off the rails!" / "Into the wall, now!" / "Cart coming. Move your feet." | "Cart! Get in a hole, darling!" / "Off the line, off the line!" / "Something heavy's coming. Move!" | "Cart. Pocket. Now." / "Off the rails." / "Wheels coming. Move." | "A cart! Kindly vacate the rails!" / "Into the niche! Momentum is not negotiable!" / "Mass approaching at speed! Move!" |
  | AllAboard | "All aboard! Five seconds, then we roll." / "Get in or get walking." / "Seats, everyone. We're leaving." | "All aboard! Mind the splinters." / "In you get. I'm not waiting." / "Last call for the cart, loves." | "All aboard." / "In. We go." / "Seats. Now." | "All aboard! I have claimed the dignified seat." / "Do hurry. The cart keeps a timetable." / "Boarding, everyone. Punctuality, please." |
  | Heads | "Heads!" / "Duck!" / "Beam! Heads down!" | "Heads, darlings!" / "Duck! Mind the hair!" / "Low beam! Down!" | "Heads." / "Down." / "Beam." | "Heads! Beam ahead!" / "Duck! Mind the timber!" / "Lower your heads, all of you!" |
  | HoundLeap | "Hound! It's jumping!" / "Dog on the cart!" / "Hound coming over!" | "Hound! Not on my lap!" / "Dog! Jumping!" / "It's leaping! Swat it!" | "Hound." / "It'll jump." / "Hound. Watch it." | "A hound, airborne! Repel it!" / "It's leaping! Somebody hit it!" / "Hound! I object to this!" |
  | LineBlocked | "Line's blocked! Take the switch!" / "Pull the lever! Pull it!" / "Heap ahead! Switch!" | "Line's blocked! Lever, quick!" / "Switch it or we eat that heap!" / "Pull something, anything!" | "Blocked. Switch." / "Lever. Now." / "Heap ahead. Switch." | "The line is blocked! The switch!" / "Throw the lever, unless we enjoy collisions!" / "Obstruction ahead! Change tracks!" |
  | JumpCall | "Pump it! We have to jump!" / "Gap ahead! Push!" / "Everyone pump! Now!" | "Pump! We're jumping, apparently!" / "Faster, or we swim in that!" / "Push, push, push!" | "Gap. Pump." / "Faster." / "Push. Now." | "Pump! We need the velocity!" / "A gap! Speed is our only argument!" / "Push harder! I have done the sums!" |

  | Trigger | Warrior | Rogue | Ranger | Mage | Generic |
  |---|---|---|---|---|---|
  | CartComing | "Cart! Move!" / "Off the line!" | "Cart! Hide!" / "Off the rails, quick!" | "Cart. Move." / "Pocket. Now." | "A cart! Clear the line!" / "Into the wall!" | "Cart coming!" / "Off the rails!" / "Get in a pocket!" |
  | AllAboard | "All aboard!" / "Get in!" | "All aboard, quick!" / "In you get!" | "In. Now." / "Aboard." | "All aboard, please." / "Board the cart!" | "All aboard!" / "Get in the cart!" / "We're leaving!" |
  | Heads | "Heads!" / "Duck!" | "Heads!" / "Down!" | "Heads." / "Down." | "Heads!" / "Mind the beam!" | "Heads!" / "Duck!" / "Low beam!" |
  | HoundLeap | "Hound!" / "It's jumping!" | "Hound! Jumping!" / "Dog!" | "Hound." / "Leaper." | "A hound!" / "It's leaping!" | "Hound!" / "It's jumping!" / "Dog on the cart!" |
  | LineBlocked | "Line's blocked! Switch!" / "Pull the lever!" | "Switch! Now!" / "Blocked! Lever!" | "Blocked. Switch." / "Lever." | "Blocked ahead! The switch!" / "Change tracks!" | "Line's blocked, switch!" / "Pull the lever!" / "Switch tracks!" |
  | JumpCall | "Pump it!" / "Push!" | "Pump! Faster!" / "Push it!" | "Pump." / "Faster." | "Pump! More speed!" / "Faster, all of you!" | "Pump it, we have to jump!" / "Faster!" / "Push!" |

- **`RideLines.BarkFor(CartEventKind kind) → BarkTrigger?`:** Boarding → AllAboard; otherwise null.
- **`RailCalls`** (Godot; server and single player only):
  - `public static string? Say(PartyController party, ICartBody cart, BarkTrigger trigger, string logIfSilent)`. The first seated, not-downed companion in `cart` speaks through `AdventureDirector.Instance.CallOut(hero.State, trigger)`. With nobody to speak, or when `CallOut` returns false (no campaign, not in game), it logs `logIfSilent` (today's literal placeholder text), so a warning is never lost. It returns the speaker's name when a line was said, else null.
  - `public static event Action<BarkTrigger, string?>? Called`, raised either way.
- **Call sites:** each keeps its `GD.Print` trace, its throttle and its counters (`BeamPasses`, `_headsBarked`, `bark=`).
  - `RailController.Warn` and the encounter warning → `Say(..., CartComing, "Cart coming!")`. Drop their `RaiseBark` and `ShowBark` lines.
  - The beam → Heads, `"Heads!"`.
  - The hound → HoundLeap, `"Hound!"`. Delete the Leaps `Bark` helper.
  - The heap → LineBlocked, `"Line's blocked — switch!"`. Delete the Switchyard `Bark` helper.
  - The jump → JumpCall, `"Pump it — we have to jump!"`.
  - `RailController` on a Boarding event → `Say(..., AllAboard, "All aboard!")`.
- **Grisel** (forgiven only: she is not on the floor otherwise).
  - **The branch's facts:**
    - The wardens went down unpaid against the Verger's order.
    - The orc's things caged her crew, and she ran and left them (floor 9 `greet`; `campaign.json` cast_grisel Secret; floor 9 Story).
    - Forgiven, she "brings them out one at a time" (floor 12 Story).
    - Keeping them caged for the orc and taking his keys is the **condemned** branch only, so no call may say it.
    - The crew are past speaking.
  - **Her voice:** flat, wry, short sentences, the worst about herself first. Compare floor 9's "Then the orc's things caged my crew, and I ran. Left them there." and floor 12's g3 "Fair. I'd have left me too."
  - `floor_12.json` `npc_grisel.Calls`, `VoiceId` null:
    - `ride1`: "Hob Tallow and Merrin Ash, first cage. I ran and left them there. I'll be the one who lets them out."
    - `ride2`: "Next cage: Dace Wicker and Old Pell. Pell taught me these drains. I left him to the orc all the same."
    - `ride3`: "Last cage. Juno Brack and Selby Crane. Selby said we shouldn't go down unpaid. She was right."
    - `ride4`: "That's the last cage behind us. Get us to the stair before he thinks of something worse."
    - `cage`: "They'll know my voice before they know my face." (the text of today's `GriselLine`)
  - `CrewRescueController`:
    - The `GriselLine` const goes.
    - At the lock, the log becomes "Grisel works the lock.", then `SayAs(grisel.NpcName, call, $"grisel:cage{wagon.Stop}")` when `plan.CallFor(RideLines.CageCall)` is not null.
    - In `OnCartEvent`, a Departed event with Grisel alive, not downed and seated in the party cart → `SayAs(grisel.NpcName, call, $"grisel:{key}")` with `key = GriselCallFor(...)`, when the call exists.
    - The once-per-run key means a second departure from the same stop (after a wreck or a load) does not repeat the call.
    - Her plan comes from `Main.Instance.Builder.Contents.Plan.Npcs` by `GriselId`.
- **`DebugTools.Chase.cs`:** the warning stamps move from text matches to `RailCalls.Called`:
  - CartComing → "bell", only while traffic is on, as today;
  - LineBlocked → "heap";
  - JumpCall → "jump".
  - Log-text matches for hits stay.

- [ ] **Step 1: Tests.** Write them, watch them fail, then implement.
  - `BarkTriggerTests`: the bark names gain the six; `(int)BarkTrigger.TrapSprung == 11` still holds.
  - `RideBarksTests`:
    - every trigger has ≥ 3 lines for each hero, ≥ 2 for each class and ≥ 3 generic;
    - no duplicate `BarkRepeat.Key` within a set;
    - every line is ≤ 60 characters;
    - no hero line contains its own speaker's name;
    - `TopUp` adds them, and a second `TopUp` adds 0;
    - a co-op name ("Pip", class.rogue) gets the rogue lines.
  - `CampaignRepairBarksTests`: `RideBarks.Triggers` each reach `MinLines`. `ReactiveBarksTests` (:54) passes, since no delivery is Default.
  - `RideLinesTests`: `BarkFor(Boarding) == AllAboard`.
  - `StoryRewireTests` passes with Grisel's calls in `Spoken`.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Headless checks.**
  - `--test-crew=forgiven`: listen on `EventBus.NarrationEx` for text starting `Grisel Thorn: "`. By the time the test ends (the queue drains between stops), her `ride1`, `ride2` and `ride3` texts are each said exactly once, and her `cage` text at least once. It prints `[crew] grisel said <key>`.
  - `--test-crew=condemned`: no `Grisel Thorn:` narration at all.
  - `--test-ride`: `RailCalls.Called` raises AllAboard exactly once before step 5's load, and once more at the re-boarding after it.
  - `--test-chase`: the bell, heap and jump warnings still precede their hits by ≥ 1.5 s.
- [ ] **Step 4: Build and the regression runs.**
- [ ] **Step 5: Commit** `feat(lines): the heroes call the cart warnings; Grisel names her crew at each stop`.

### Task 4: The clips — download, cut, register, credit

**Files:**
- Create:
  - `assets/audio/rails/*.ogg` (the list below) and their `.import` files;
  - `src/Dungeon/Rails/Core/RideSound.cs` (the `Clips` table; Task 5 adds the rules).
- Modify:
  - `src/Audio/AudioManager.cs` (`Cues` :25, `_Ready` :98): merge `RideSound.Clips`; `Stream`, `PlayAt`, `PlayedAt`;
  - `src/Audio/Synth.cs` (`PlayAt` :87): a trailing `float pitch = 1f` sets `PitchScale`; existing callers are unchanged;
  - `assets/LICENSES.md`.
- Test `tests/Dungeon/Rails/RideSoundTests.cs`.

**Downloads.** Every source page's licence link was read on 2026-11-18 and is CC0 1.0. All 18 `-hq.ogg` preview URLs were checked and return audio/ogg. They are Freesound's public HQ previews, so no login is needed.

Cut each clip with the command below. `END` is the computed number `D − 0.05`, and `NORM` depends on the clip:
- **Loops and clips ≥ 3 s:** `loudnorm=I=-18:TP=-1.5`.
- **Shorter one-shots:** `volume=<gain>dB`, where the gain brings the peak to −1.5 dB. Read the peak with `-af volumedetect`. `loudnorm` is unreliable under 3 s.

```
ffmpeg -y -ss A -t D -i IN -af "afade=t=in:d=0.01,afade=t=out:st=END:d=0.05,NORM" -ac 1 -ar 44100 -c:a libvorbis -q:a 5 OUT
```

`-ar 44100` is needed because `loudnorm` resamples to 192 kHz. Use mono, because Godot spatialises mono. Loops get 20 ms fades at both ends. Find `A` from the first loud onset (`-af silencedetect=n=-35dB:d=0.05 -f null -`) or the loudest window. Record `A`/`D` in the licence entry.

| Repo file (`assets/audio/rails/`) | Cue | Source preview | Author | Cut |
|---|---|---|---|---|
| `cart_roll.ogg` (loop) | cart_roll | https://cdn.freesound.org/previews/351/351382_2032854-hq.ogg (sound 351382) | wmquincy101 | 10 s steady stretch |
| `clack_1.ogg`, `clack_2.ogg`, `clack_3.ogg` | rail_clack | https://cdn.freesound.org/previews/689/689228_7088538-hq.ogg (689228) | Mouffette | three single joint hits, ~0.25 s each |
| `screech_loop.ogg` (loop) | rail_screech | https://cdn.freesound.org/previews/181/181868_57789-hq.ogg (181868) | cognito perceptu | 4 s sustained squeal |
| `brake_squeal.ogg` | brake_squeal | same as above | cognito perceptu | 1.5 s onset |
| `rail_sing.ogg` (loop) | rail_sing | same as above, plus `highpass=f=2500,volume=-6dB` | cognito perceptu | 4 s |
| `lever_1.ogg` | switch_clunk | https://cdn.freesound.org/previews/151/151271_2578041-hq.ogg (151271) | timgormly | whole (0.62 s) |
| `lever_2.ogg` | switch_clunk | https://cdn.freesound.org/previews/94/94127_1554918-hq.ogg (94127) | BMacZero | whole (0.67 s) |
| `gate_chain.ogg` | gate_lift, hammer_wind | https://cdn.freesound.org/previews/448/448917_9094336-hq.ogg (448917) | Kp69606 | 2.5 s rattle |
| `bell.ogg` | cart_bell | https://cdn.freesound.org/previews/611/611113_1629501-hq.ogg (611113) | 5ro4 | whole (2.5 s) |
| `metal_crash.ogg` | cart_crash, cart_wreck | https://cdn.freesound.org/previews/475/475416_7009965-hq.ogg (475416) | o_ciz | 1.2 s |
| `beams_collapse.ogg` | cart_wreck, timber_smash | https://cdn.freesound.org/previews/675/675898_2524442-hq.ogg (675898) | craigsmith | 2.5 s |
| `timber_collapse.ogg` | timber_collapse | https://cdn.freesound.org/previews/487/487142_2524442-hq.ogg (487142) | craigsmith | 4 s |
| `creak_1.ogg` | trestle_creak, bucket_creak | https://cdn.freesound.org/previews/502/502504_4921277-hq.ogg (502504) | Rudmer_Rotteveel | whole (0.9 s) |
| `anvil_1.ogg`, `anvil_2.ogg` | golem_slam, hammer_fall | https://cdn.freesound.org/previews/434/434339_6599148-hq.ogg (434339) | draftcraft | two single strikes, ~0.8 s |
| `windup.ogg` | golem_windup | https://cdn.freesound.org/previews/258/258341_1038806-hq.ogg (258341) | unfa | 1.5 s ending at the peak |
| `lava_hiss.ogg` | pour_hiss | https://cdn.freesound.org/previews/474/474851_9395330-hq.ogg (474851) | Fission9 | 1.5 s |
| `lava_loop.ogg` (loop) | molten_loop | same as above | Fission9 | 8 s |
| `steam.ogg` | steam_burst | https://cdn.freesound.org/previews/365/365071_6639672-hq.ogg (365071) | sniperous | 2 s |
| `rumble_bed.ogg` (loop) | foundry_rumble | https://cdn.freesound.org/previews/524/524307_6714882-hq.ogg (524307) | Bertsz | 12 s |
| `cinders.ogg` | cinder_pour | https://cdn.freesound.org/previews/51/51262_179538-hq.ogg (51262) | RutgerMuller | whole (1.15 s) |

There is no `sparks` sound. Nothing would raise it: the wheel sparks are visual, and the screech carries them.

Some cues reuse the Kenney CC0 packs already in the repo:
- `crate_break` = `impactWood_heavy_000..004` and `impactMining_000..004`;
- `cart_jump` = `impactPlate_light_000..004`;
- `cart_land` and `cart_hit` = `impactPlate_heavy_000..004` and `impactMetal_heavy_000..004`.

**Interfaces:**
- **`RideSound`** (engine-free):
  - `public const string Dir = "res://assets/audio/rails/";`
  - `public static readonly IReadOnlyDictionary<string, string[]> Clips`: cue → res:// paths, every row above plus the Kenney reuses. It covers the 18 cues the rail code raises today (`cart_wreck` included: today it plays nothing) and the new cart_roll, rail_clack, rail_screech, brake_squeal, rail_sing, hammer_fall, molten_loop, foundry_rumble and cart_hit.
  - `public static readonly IReadOnlySet<string> Loops` = { cart_roll, rail_screech, rail_sing, molten_loop, foundry_rumble }.
  - `public static string? PropCue(string kind, string what)`:
    - gate/up → gate_lift;
    - switch or spur, thrown or straight → switch_clunk. Only a lever thrown or set back by hand (`TrackSwitch.Toggle`) may raise "thrown" or "straight". If plan 5's `TrackSwitch.Reset()` raises "straight" at a ride's end, Task 5 changes it to raise "reset". The `ClientWorld.OnProp` case for "reset" calls the same silent Show method, and `PropCue` maps "reset" to null, so a reset never clunks;
    - heap/cleared → crate_break;
    - valve/broken → steam_burst;
    - trestle/collapsed → timber_collapse;
    - buffer/smashed → timber_smash;
    - anything else → null. The wagon and keyring keep their existing `door` and `pickup` cues.
- **`AudioManager`:**
  - `_Ready` merges `RideSound.Clips` into the cue table with the same `ResourceLoader.Exists` filter.
  - `public AudioStream? Stream(string cue)` round-robins and returns null for an unknown cue. For a cue in `Loops` it sets `Loop = true` on the ogg.
  - `public void PlayAt(string cue, Vector3 at, float volumeDb = 0f, float maxDistance = 30f, bool ambient = false)` plays through `Synth.PlayAt` under `Main.Instance?.Builder.FloorRoot ?? this`, passing the same pitch jitter as `Play` (0.92–1.08) through the new `pitch` argument. It never plays a cue in `Loops`: a looping one-shot would never free itself. It does nothing on a dedicated server.
  - `public static event Action<string, Vector3, bool>? PlayedAt` (cue, position, ambient) is raised on every `PlayAt`, including on a dedicated server, before the no-sound return. It is a test hook.
- **Until Task 5,** the rail code's existing `EventBus.PlaySfx` calls play these clips non-positionally. That is an interim step.

- [ ] **Step 1: Tests.** Write `RideSoundTests`, watch them fail, then implement.
  - (a) Every cue literal that `src/Dungeon/Rails/**/*.cs` passes to `PlaySfx(`, `PlayAt(`, `RideAudio.At(` or `Stream(` (regex `"([a-z_]+)"`) is a key of `Clips` or one of the core cues (`hit_flesh`, `pickup`, `door`, `lever`, `trap`).
  - (b) Every path in `Clips` exists on disk (res:// → repo root).
  - (c) Every `assets/audio/rails/*.ogg` appears in `Clips` and by name in `assets/LICENSES.md`.
  - (d) The `PropCue` table.
- [ ] **Step 2: Download and cut** into a scratch directory, copy only the listed files in, then run `godot --headless --import --path .`.
- [ ] **Step 3: Licences.** Add `## Mine cart chase sounds (Freesound, CC0)` to `assets/LICENSES.md`. One line per file: repo file, "title" (Freesound id) by author, the page URL (https://freesound.org/people/<author>/sounds/<id>/), CC0, and the cut.
- [ ] **Step 4: Check.**
  - `godot --headless --path . -- --quit-after=3 2>&1 | grep "\[Audio\]"` shows the higher cue count and no "no clips found" warning for a rail cue.
  - Build and the regression runs.
- [ ] **Step 5: Commit** `feat(audio): CC0 clips for every rail cue, cart_wreck included`.

### Task 5: The ride heard where it happens, on every peer

**Files:**
- Create:
  - `src/Dungeon/Rails/CartSound.cs`;
  - `src/Dungeon/Rails/RideAudio.cs`;
  - `src/Dungeon/Rails/RideSoundscape.cs`.
- Modify:
  - `src/Dungeon/Rails/Core/RideSound.cs` (the rules);
  - `src/Dungeon/Rails/RailController.cs`, `RailController.Jump.cs` and `RailController.Lanes.cs`: feed the party cart; extract the warning geometry; drop the PlaySfx calls;
  - `src/Dungeon/Rails/SlagCart.cs` and `src/Dungeon/Rails/RunawayWagon.cs`: carry and feed a `CartSound`;
  - `src/Dungeon/Rails/RailEncounterController.cs` and its `Kiln`, `Traffic`, `Switchyard` and `SetPieces` partials;
  - `src/Dungeon/Rails/RailGate.cs`;
  - `src/Dungeon/Rails/SetPieces/{DropHammer,MoltenPour,OreBuckets,OreHeap,TrackSwitch,Buffer,Trestle}.cs`;
  - `src/Dungeon/Rails/RailNet.cs`: the client feed;
  - `src/Net/ClientWorld.cs`: `OnProp`'s rail cases;
  - `src/Enemies/Enemy.cs` (`ClientTelegraph` :647);
  - `src/Core/DebugTools.Chase.cs` (`OnChaseSfx`), `src/Core/DebugTools.Ride.cs` and `src/Core/DebugTools.Crew.cs`;
  - `src/Net/NetTestFlow.cs`: the RIDE pass's sound step.
- Test `tests/Dungeon/Rails/RideSoundTests.cs`.

**Carry-over from plan 4.** The `hammer_wind` and `pour_hiss` cues are gated on the armed ride as well as on distance, so a cart held at stop 3 no longer hears them. The unarmed fall and pour body scans are gated to bodies within 30 m of the piece. This was a plan 4 re-review Minor.

**Interfaces:**
- **`RideSound` rules** (engine-free):
  - `JointSpacing = 2.4f`, `BellEvery = 4f` (matches `WarnEvery`), `Audible = 45f`.
  - `float RollPitch(float v) = 0.7 + 0.6·clamp(v / CartRules.MaxSpeed, 0, 1)`.
  - `float RollDb(float v, int wagons)`: −80 below 0.3 m/s, else `−20 + 14·clamp(v/MaxSpeed) + wagons`.
  - `float ClackInterval(float v)`: +∞ below 0.5 m/s, else `max(0.09, JointSpacing / v)`.
  - `float ScreechDb(float curvature, float v)`, where `a = |curvature|·v²`: −80 below 3 m/s², then `−22 + 16·clamp((a − 3)/5)`.
  - `bool Squeals(float dvdt, float v) = dvdt <= −1.5 && v >= 1`.
  - `float SingDb(float secondsAway)`: −80 beyond 6 s, then `−26 + 18·(1 − s/6)`.
  - `readonly record struct CartSoundInput(float S, float V, CartMode Mode, float Plating, byte Track, bool Airborne, bool Halted, bool Crossing)`.
  - `[Flags] enum CartCue { None, Crash, Jump, Land, Hit, Arrive, TrackChange, Cross }`.
    - There is no Wreck cue: a wreck with a spare resets the cart to Held at the spare's stop in the same call (`RailController.Wreck` → `SpareTakesOver`), so no feed ever sees Mode Wrecked.
    - There is no Derail cue: `SlagCart.Derail` frees the cart, and its CartSound, in the same call.
    - Both sounds are one-shots instead (below).
  - `CartCue Edges(in CartSoundInput prev, in CartSoundInput now)`:
    - Crash: Halted rises;
    - Jump: Airborne rises;
    - Land: Airborne falls;
    - Hit: Plating drops by ≥ 1;
    - Arrive: Rolling → Held, a real arrival only. Halted does not rise, Plating does not rise (a spare takeover resets it to full), and |S − prev.S| < 1 m (a takeover moves the cart to the spare's stop);
    - TrackChange: Track changes;
    - Cross: Crossing rises (the cart takes a thrown switch onto the other lane).
  - `CartCue Loudest(CartCue edges)` keeps one cue per feed, by priority Crash > Land > Jump > Hit > TrackChange > Cross > Arrive.
    - Arrive is dropped when any other edge fired on the same feed.
    - Test case: the heap crash (Halted rises, Plating drops, Rolling → Held) gives Crash only.
  - `bool BellDue(bool rolling, float v, bool anyoneAhead, double now, double lastRing) = rolling && v >= 0.5 && anyoneAhead && now − lastRing >= BellEvery`.
    - Today's `Warn` runs only while Rolling (`RailController.cs:582-592`). Its look-ahead is at least 9 m even at V = 0, so a Held cart must never ring.
  - `string? TelegraphCue(string state)`: "RailSlam" → golem_windup; anything else → null. The golem's windup raises exactly `RaiseEnemyTelegraph(g.Body, "RailSlam")` at `Kiln.cs:347`.
    - If plan 5's built code raises another string there, change it to "RailSlam" (no wire change).
    - Never reuse `"Signature:Slam"`: `ClientTelegraph` already handles it for every Slam-signature enemy.
    - `Enemy.ClientTelegraph` gains a "RailSlam" case that plays the hand glow (`TelegraphFx.HandGlow`, `GolemWindup` s) and the cue.
- **`CartSound : Node3D`**, a child of every `MineCart` (party, spares and chaser), `SlagCart` and `RunawayWagon`, and of the golem runner with `Wheels = false`.
  - It owns four pooled `AudioStreamPlayer3D`s on the SFX bus: Roll (loop), Screech (loop), Clack and Event.
  - It also owns a `CpuParticles3D` wheel-spark emitter, on while screeching or squealing.
  - `public bool Wheels { get; init; } = true;`
  - `public bool AnnounceOnStart { get; init; }`: traffic and runaways ring once on their first Rolling feed. This replaces the spawn bells at `Traffic.cs:105` and `:121`.
  - `public float RollDbNow`, `RollPitchNow` (the values last set) and `public static event Action<string, Vector3>? Played`, raised for every one-shot it plays (clack, bell, edge cue). These are test hooks: headless runs use a dummy audio driver, so `AudioStreamPlayer3D.Playing` is not trusted.
  - `public void Feed(in CartSoundInput s, int wagons, Func<bool>? anyoneAhead)` is called once per rendered frame by the owner:
    - The first feed only primes `prev` and plays nothing. So does the first feed after `public void Prime()`, which `RailController.Restore`, a load and plan 5's client catch-up call.
    - `Loudest(Edges(prev, now))` → Event:
      - Crash → cart_crash; Jump → cart_jump; Land → cart_land;
      - Hit → golem_slam when the golem is within `RailEncounterController.GolemReach + 1` m, else cart_hit. The golem is `RailEncounterController.Instance.GolemBody` on the server and in single player, and the `CartKind.Golem` cart on clients;
      - Arrive → brake_squeal;
      - TrackChange and Cross → switch_clunk.
    - Roll and Screech volume and pitch come from the rules. While `Squeals`, the Screech loop plays at no less than −14 dB and the sparks emit.
    - A clack plays every `ClackInterval`.
    - `BellDue` → cart_bell.
  - Everything stops beyond `Audible` from the active camera, and on a dedicated server.
- **Who feeds each cart** (every role):
  - **The party cart**, from `RailController._Process` (:797).
    - Server and single player read `Party.State`, `Halted`, the plating, `Crossing`, and a new public `RailController.Airborne`: `Jumped && S` lies within `[ArcFrom, ArcFrom + ArcLength]`.
    - Clients read the follower state, the Halted flag and the DTO plating.
    - Clients' `Airborne` is `JumpV > 0` with the rendered S in `[GapFrom − HalfLength, GapTo + HalfLength]`. `Crossing` is whether `CrossOut` is finite.
  - **Slag and traffic carts**, from their run on the server and from their follower on clients.
  - **Runaways**, likewise.
  - **The golem runner**, its Track only.
    - On the server and in single player: 0 on the enemy line, 2 once `Golem.OnSpur`.
    - On clients: the DTO's Track.
  - **Spares** are never fed, so they stay silent. The chaser is fed by `RailController.Chase` from its run or follower, like the party cart. Cage wagons carry no CartSound: the party cart's roll covers the train through `wagons`.
- **The one-shots that are not edges:**
  - **cart_wreck:** `RailController.Tumble()` (`:692-706`) calls `RideAudio.At("cart_wreck", Cart.GlobalPosition)` in place of its `PlaySfx`.
    - Tumble runs once per wreck on the server and in single player.
    - Plan 5 runs it on clients on the Wrecked flag's rising edge (P5 Task 3).
    - Check that the built plan 5 raises that flag for a wreck even when a spare takes over in the same tick. If it derives the flag from Mode, set it from the Wrecked `CartEvent` instead, and add it to the RIDE pass.
  - **cart_crash for a derail:** `SlagCart.Derail()` calls `RideAudio.At("cart_crash", at)` beside its `Synth.PlayAt(Thud)`. Plan 5's client Derailed-flag tumble handler calls it too.
- **`RailWarn.AnyoneAhead(TrackSampler track, float s, float v, IReadOnlyList<Node3D> bodies)`** is the geometry of `RailController.Warn` (:628-646), extracted as a static helper in `src/Dungeon/Rails/RailController.cs` with no allocation.
  - `track` and `s` are the run the caller judged: `JudgedRun()` for the party cart, which is lane-aware, as today.
  - The server's bark `Warn` and every peer's `CartSound` bell both use it.
  - The bodies are the heroes this peer can see on foot (not seated, not downed), in a list reused every frame.
- **`RideAudio`** (static):
  - `At(string cue, Vector3 at, float db = 0, float range = 30, bool ambient = false)` → `AudioManager.Instance?.PlayAt`.
  - `OnProp(string netRef, string what)` → `PropCue`, then `NetRefResolver.Resolve` to the node, then `At(node.GlobalPosition)`.
  - On the server and in single player it is subscribed to `EventBus.PropChanged`. On clients it is called from `ClientWorld.OnProp`'s rail cases after the `Show*`. If `OnProp` re-raises `PropChanged` locally, use that one subscription instead. It is never called from a PropState apply.
- **Every rail `EventBus.PlaySfx` call goes.** Each sound moves to where every peer can play it:
  - **`CartSound` edges and bell:**
    - cart_bell (`RailController.cs:658`, `RailEncounterController.cs:351`, `Traffic.cs:105`, `Traffic.cs:121`);
    - cart_jump and cart_land (`Jump.cs:105`, `:116`);
    - cart_crash for the heap (`Lanes.cs:170`, the Crash edge). The drop into the gap (`Jump.cs:122`) ends in a wreck, so it is covered by cart_wreck;
    - switch_clunk as the cart takes a thrown switch (`Switchyard.cs:206`, the Cross edge). Taking the points is not a prop change, and the party Track does not change on a lane crossing.
  - **One-shots:** cart_wreck from `Tumble()` (`RailController.cs:705`); cart_crash from `SlagCart.Derail()`.
  - **The heap's warning bell** (`Switchyard.cs:223`) plays at the heap when its lantern starts blinking, under plan 5 Task 7's presentation rule, on every role.
  - **The `PropEvt` path, through `RideAudio.OnProp`:**
    - gate_lift (`RailGate.cs:112`);
    - switch_clunk for a lever thrown or set back (`TrackSwitch.cs:79`);
    - crate_break (`OreHeap.cs:94`);
    - steam_burst (`MoltenPour.cs:172`);
    - timber_collapse (`Kiln.cs:582`);
    - timber_smash (`Buffer.cs:69`).
  - **The floor clock:**
    - hammer_wind on Glow entry and hammer_fall on Fall entry, in `DropHammer._Process` (:62), replacing `Kiln.cs:469`;
    - pour_hiss on Warn entry and a `molten_loop` player while Pouring, in `MoltenPour._Process` (:99), replacing `Kiln.cs:529`;
    - bucket_creak and cinder_pour at the buckets' creak and pour moments, on the server's tick and the client's schedule (`OreBuckets.cs:165`, `:185`).
  - **trestle_creak** (`Kiln.cs:574`): a presentation rule on every role. It plays once, at the trestle, when the party cart's front S first passes `trestle.From` while the trestle stands.
  - **golem_windup** (`Kiln.cs:348`): `RideAudio.At` at the golem on the server and in single player, beside the "RailSlam" `EnemyTelegraph`. `Enemy.ClientTelegraph` plays `TelegraphCue(state)` at the enemy.
  - **golem_slam** (`Kiln.cs:357`): the `PlaySfx` goes with nothing in its place. Every role hears the slam once, through the party cart's Hit edge (the dent follows at `:361`).
  - **switch_clunk for the spur** (`Kiln.cs:378`): the golem runner's TrackChange edge (0 → 2).
  - **`TrackSwitch.Reset()`:** if plan 5 made it raise "straight", it raises "reset" instead (Task 4's `PropCue` note). Add the "reset" case to `ClientWorld.OnProp`.
  - The `SfxReach` gate (`Kiln.cs:37`) goes; the 3D player's range replaces it.
- **`RideSoundscape : Node`**, a child of `RailController`, on every role but a dedicated server:
  - `foundry_rumble` as a non-positional loop at −22 dB while the floor has rails;
  - `rail_sing` as a non-positional loop at the listener, at `SingDb(the fewest seconds any rolling cart needs to reach the local hero on foot on its lane)`;
  - every 9–16 s (unseeded RNG; presentation only), `trestle_creak` at a random timber frame within 15 m of the camera. It plays through `RideAudio.At(..., ambient: true)`, and `PlayedAt` carries that flag so tests can ignore it.
- **`DebugTools.Chase.cs`:** `OnChaseSfx` listens to `AudioManager.PlayedAt` for golem_windup, pour_hiss and hammer_wind, with its timing rules unchanged.

- [ ] **Step 1: Tests.** Write them, watch them fail, then implement.
  - `RollPitch` is monotone, 0.7 at 0 and 1.3 at MaxSpeed.
  - `ClackInterval(6) == 0.4`; at 0.3 it is +∞; at 30 it is 0.09.
  - `ScreechDb` is −80 at a = 2 and rises to −6 at a ≥ 8.
  - `Edges`:
    - each flag alone, and none for identical inputs;
    - a spare takeover (Rolling, S 80, Plating 0 → Held, S 40, Plating 100) gives no Arrive;
    - a halt (Halted rises) gives Crash and no Arrive.
  - `Loudest`: the heap crash gives Crash only.
  - `SingDb(7) == −80` and `SingDb(0) == −8`.
  - `BellDue`: respects 4 s; false while not rolling or below 0.5 m/s.
  - `TelegraphCue`: "RailSlam" → golem_windup; "Signature:Slam" → null.
  - The source scan from Task 4 now finds no `PlaySfx("` with a rail cue under `src/Dungeon/Rails`.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Headless checks.**
  - `--test-ride`, counting `AudioManager.PlayedAt` (ambient plays ignored), `CartSound.Played` and `EventBus.Sfx`:
    - within 10 s of departure, ≥ 5 `rail_clack` from `CartSound.Played`;
    - the party `CartSound.RollDbNow > −80` and `RollPitchNow > 1.0` once V > 5;
    - at the wreck step (a spare takes over), exactly one `cart_wreck` and no `brake_squeal` in the same second;
    - for the whole run, no `EventBus.Sfx` cue that is a key of `RideSound.Clips`.
  - `--test-crew=condemned`: the save and load between stops 2 and 3 plays none of gate_lift, timber_collapse, timber_smash, crate_break, steam_burst or switch_clunk (ambient plays ignored).
  - `--test-chase`: every warning still comes ≥ 1.5 s before its hit.
- [ ] **Step 4: Net.** In the RIDE pass the client prints `[net] ride sound roll=<on|off> pitch=<x> railsfx=<n>`, from `RollDbNow` and `RollPitchNow`. The script requires roll=on and pitch > 1.0 on at least one line while the server cart rolls, and `railsfx=0` (no `SfxMsg` with a rail cue). Run `scripts/net_test.sh`, `START_FLOOR=0 scripts/net_test.sh` and `RIDE=1 scripts/net_test.sh`; each prints `NET TEST: ok`.
- [ ] **Step 5: Build, the suite and the SP regression runs.**
- [ ] **Step 6: Commit** `feat(audio): the ride is heard where it happens, on every peer, from the synced state`.

### Task 6: The Chase music mood

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/RideMusic.cs`;
  - `assets/audio/music/chase_determined_pursuit.ogg` and its `.import`.
- Modify:
  - `src/Audio/MusicDirector.cs` (the enum :7, the tracks :26-35, `Decide` :109, `Play` :168, `OnFinished` :183);
  - `src/Dungeon/Rails/Core/SetPieceRules.cs`: two read-only accessors;
  - `src/Core/DebugTools.Ride.cs`;
  - `assets/LICENSES.md`.
- Test:
  - `tests/Dungeon/Rails/RideMusicTests.cs`;
  - `tests/Dungeon/Rails/SetPieceRulesTests.cs`.

**Download.**
- Get `https://opengameart.org/sites/default/files/determined_pursuit_loop.wav`.
- Convert it with `ffmpeg -i determined_pursuit_loop.wav -c:a libvorbis -q:a 5 assets/audio/music/chase_determined_pursuit.ogg`.
- Source: "Determined Pursuit (epic orchestra loop)" by Emma_MA, https://opengameart.org/content/determined-pursuit-epic-orchestra-loop. It is a 1:48 loop, about 160 BPM, with strings, horns and drums.
- The page says CC0. Re-read it on the day. If it is not CC0, fall back to the existing `forge_heavy_dungeon.ogg` for rides 1–3 too, and report it.
- Set loop on import. Check the seam by listening to the last 2 s and the first 2 s, and report it.
- Controller ruling: the earlier chiptune pick, "Fast fight", was dropped because it clashes with the orchestral score. Research is in `.superpowers/sdd/shared/plan6-chase-music.md`.
- Its `assets/LICENSES.md` line under "## Music" adds "(downloaded by hand; not in `scripts/fetch_music.py`)", because that section's header says its tracks are fetched by the script.

**Interfaces:**
- **`Mood`** appends `Chase`. `Decide`'s order becomes Tavern → Boss → **Chase** → Combat → Explore.
- **`ChaseTracks = { "chase_determined_pursuit.ogg" }`** plays on rides 1–3, looping.
- **`PourTrack = "forge_heavy_dungeon.ogg"`** plays on ride 4. It is already in the repo, and its source file name says 160 BPM.
  - It plays non-looping and phase-locked: `Play(file, loop: false, fadeSeconds: 1.2f, from: RideMusic.PhaseSeek(...))`.
  - `OnFinished` re-locks it while the Mood is still Chase. The audio clock drifts slowly from `FloorSeconds`, and only the re-lock corrects it; that is accepted.
  - `Play(string file, bool loop, float fadeSeconds)` gains a trailing `float from = 0` start position, passed to `_front.Play(from)`.
- **`RideMusic`** (engine-free):
  - `HoldSeconds = 4`, `NearMetres = 30`.
  - `bool Wanted(bool cartRolling, bool listenerSeated, float listenerDistance, double now, ref double lastSeen)`: true while the party cart rolls with the local hero seated in it, or within 30 m of it; then for 4 s after. At stop 3 the music settles before the golem.
  - `MusicDirector` resets `lastSeen` to −∞ on `EventBus.FloorChanged` and after a load, so a rebuilt floor never inherits a pre-load hold.
  - `int Ride(int stopLeft) = stopLeft + 1`.
  - `double PhaseSeek(double floorSeconds, double fallSeconds, double cycleSeconds, double beatOrigin, double trackLength) = beatOrigin + Mod(floorSeconds − fallSeconds, cycleSeconds)`, wrapped below `trackLength` by whole cycles. `Mod` snaps a result within 1e-6 of `cycleSeconds` to 0.
  - `PourBeatOrigin`: the seconds to the track's first downbeat, pinned as a constant.
    - Find the first onset with `ffmpeg -i forge_heavy_dungeon.ogg -af silencedetect=n=-30dB:d=0.05 -f null -` (the end of the leading silence).
    - Confirm it by ear against a 160 BPM click: silencedetect finds silence, not beats.
- **`SetPieceRules`:**
  - **One timing change (controller ruling).** `HammerIndexOffsetTicks` goes from 40 to 45. That is 0.75 s, two beats at 160 BPM, so every hammer falls on a beat, not just hammer 0. Update the `SetPieceRulesTests` that pin 40 (:49 `Phase(40, 0)`, :58 `270 - index * 40`) and any `--test-chase` timing that does. Each hammer still cycles every 4.5 s. A new test checks that a gap is still open between them.
  - Read-only additions:
  - `public static double HammerCycleSeconds => HammerCycleT / (double)TicksPerSecond;` (4.5)
  - `public static double HammerFallSeconds(int index)`: `Mod(UpT + GlowT − index·HammerIndexOffsetTicks, HammerCycleT) / 60.0` (index 0 → 3.1).
- **Inputs on every role:**
  - `RailController.Instance` for State.Mode and StopIndex (the follower on clients);
  - the local hero's `Cart`;
  - `Main.Instance.Builder.FloorSeconds`.
  - A dedicated server plays nothing, as today.

- [ ] **Step 1: Tests.** Write them, watch them fail, then implement.
  - `RideMusicTests`:
    - `Wanted` is true when seated and rolling;
    - it stays true 3.9 s after the cart is Held and is false at 4.1 s;
    - it is false for a walker 31 m away;
    - `PhaseSeek` at `3.1 + k·4.5` returns `beatOrigin` within 1e-6 for k = 0..5;
    - its result always lies in [0, trackLength).
  - `SetPieceRulesTests`:
    - `HammerCycleSeconds == 4.5`;
    - for i = 0..2 (three hammers, `Kiln.cs:88`), `Phase(round(HammerFallSeconds(i)·60), i)` is Fall;
    - the gap is still open: over one 270-tick cycle, some tick has all three hammers in Up or Rise, so none is in Glow, Fall or Down.
- [ ] **Step 2: Implement.** Download, import, and add the licence line under "## Music". The hammers then fall on the beat: hammer 0 every 4.5 s, which is 12 beats.
- [ ] **Step 3: Headless check** in `--test-ride`:
  - `MusicDirector.Instance.Mood == Mood.Chase` within 1 s of departure.
  - Not Chase 4.6 s after the cart is Held at stop 1 (HoldSeconds 4, the 0.5 s poll, and a margin).
    - Keep a wall timer from the arrival that survives step 4's save and load.
    - The cart re-departs about 6 s after arrival (1 s, then the 5 s boarding), so it is still Held at 4.6 s. Assert that it is.
    - The load resets `lastSeen`, which can only end the hold sooner.
  - It prints `[ride] music <mood> <track>`.
- [ ] **Step 4: Build and the regression runs.**
- [ ] **Step 5: Commit** `feat(music): a Chase mood for the rides; the pour's hammers fall on the beat`.

### Task 7: CC0 models and textures

**Files:**
- Create:
  - `assets/models/loafbrr_mine/MinesPack.gltf` and `assets/models/loafbrr_mine/README.txt`, with their imports;
  - `assets/textures/gravel_floor_02/*`, `assets/textures/old_planks_02/*` and `assets/textures/lava004/*`;
  - `src/Dungeon/Props/MinePack.cs`.
- Modify:
  - `src/Dungeon/Rails/MineCart.cs` (materials :41-42, the wheels);
  - `src/Dungeon/Rails/SlagCart.cs` (body :201);
  - `src/Dungeon/Rails/RunawayWagon.cs`;
  - `src/Dungeon/Rails/SetPieces/{TrackSwitch,SpurSwitch,OreBuckets,Buffer,OreHeap,MoltenPour}.cs`;
  - `src/Dungeon/Props/RailTrack.cs` (`Build` :26);
  - `src/Dungeon/DungeonBuilder.cs` (`PlaceRails` :1876: materials, timbering and pocket lanterns);
  - `assets/LICENSES.md`.
- Test `tests/Dungeon/Rails/RideAssetsTests.cs`.

**Downloads:**
- **loafbrr's "Mine Object collection"** (CC0; the bundled README.txt says "LICENSE: Creative Commons 0/ CC0"):
  - Zip: https://opengameart.org/sites/default/files/mines_asset_fbx_gltf_blend.zip (53 MB). Page: https://opengameart.org/content/mine-object-collection.
  - Copy only `GLTF/MinesPack.gltf` (13 MB, 26 images embedded) and `README.txt`.
  - The import extracts the embedded images beside the glTF as `.png` files with `.import` siblings (Godot's default). Commit them, list them in `RideAssets`, and cover them in the loafbrr licence entry ("and its textures").
  - Do not use the `mineasset_godot.zip`: it is Godot 3 format (`SpatialMaterial`, format=2).
- **Poly Haven** (CC0): `scripts/fetch_polyhaven.py gravel_floor_02 old_planks_02`.
- **ambientCG Lava004** (CC0; the asset has color, normal, roughness and emission maps): `https://ambientcg.com/get?file=Lava004_1K-JPG.zip`, which redirects to the zip. Rename the files to the names `DungeonBuilder.Material` loads (diff, nor, rough), plus one it does not:
  - `Lava004_1K-JPG_Color.jpg` → `lava004_diff_1k.jpg`;
  - `_NormalGL` → `lava004_nor_1k.jpg`;
  - `_Roughness` → `lava004_rough_1k.jpg`;
  - `_Emission` → `lava004_emit_1k.jpg`. `Material` (`DungeonBuilder.cs:734-752`) never loads an emission map, so the lava code loads this one itself (below).
- Run `godot --headless --import --path .`.

**Interfaces:**
- **`MinePack`:**
  - `public const string Path = "res://assets/models/loafbrr_mine/MinesPack.gltf";`
  - `public static Node3D? Piece(string nodeName, Vector3? fit = null, bool keepAspect = true, bool withChildren = true, bool keepRotation = false)`:
    - loads the scene once and keeps the instance off-tree;
    - duplicates the named node (with its subtree only when `withChildren`) and zeroes its translation and scale, keeping its rotation when `keepRotation`;
    - stands it on its AABB bottom, centred;
    - scales it to `fit` (uniform by the largest ratio when `keepAspect`);
    - strips every `CollisionObject3D`;
    - returns null when the file or node is missing, so callers keep today's boxes.
  - The node names and sizes below were read from the glTF (W × H × D, metres):
    - `Cart_1` (1.0 × 1.03 × 1.35) and `Cart_2` (1.0 × 0.74 × 1.35) are wheel-less tubs, children of `CartFrame`.
    - `CartWheels_1` and `CartWheel_2` are **axles** (0.76 m, two wheels each).
    - `Lever` (1.13 m), `Bucket`, `Blockade` (2.07 × 0.98 × 0.49), `Rocks_1`..`Rocks_8`.
    - `Chain` (2.94 m) has `Guillotine_1` and `Guillotine_2` as children.
    - `PostLight_1` (0.36 m, rotated 90° in yaw).
    - `WoodPost` (a 2.6 m post) has 15 children: braces, beams, the post light.
    - `WoodPostBeams_2` and `_4` (2 m lintels).
    - `WoodSupport_1..4` are 0.84 m diagonal braces, not frames.
- **Uses.** Each is a visual child of the existing "Body" or prop node. Collision shapes, layers and sizes are untouched.
  - `SlagCart` uses `Cart_2` fitted to its tub box, and `RunawayWagon` uses `Cart_1`. Each adds `CartWheels_1` and `CartWheel_2` as front and rear axles at the box's wheel height. Their tub boxes (not their wheels) are hidden only when the pieces loaded.
  - `MineCart` (party, spares and chaser) keeps its box tub, because the seat and bench layout depends on it.
    - `_iron` becomes `Material("metal_plate_02", …)` with metallic 0.8, and `_wood` becomes `old_planks_02`.
    - Its front and rear wheel pairs become `CartWheels_1` and `CartWheel_2`, one axle each, in place of the four wheel boxes.
  - The levers in `TrackSwitch` and `SpurSwitch` use `Lever` at 1.1 m tall.
  - `OreBuckets` uses `Bucket` and `Chain` with `withChildren: false`, so no guillotine blades hang over the line. `Buffer` uses `Blockade`. `OreHeap` is dressed with `Rocks_*`.
  - `PlaceRails`:
    - pocket lanterns use `PostLight_1` with `keepRotation: true` in place of the 0.14 m box (the light stays);
    - each timber frame is two `WoodPost` posts (`withChildren: false`, fitted to the cell's height) and a `WoodPostBeams_2` lintel across the cell's width, still without collision;
    - rails take `Material("rusty_metal_02", …)` (already in the repo), metallic;
    - sleepers take `old_planks_02`.
  - `RailTrack.Build(..., Material? ballast = null)` adds a 1.5 m gravel ribbon (`gravel_floor_02`) 0.02 m above the floor along the same frames.
  - Lava: the molten and lip materials of `MoltenPour`, the slag cart's coal top and `CinderPatch` take `Material("lava004", …)`. Then `EmissionEnabled = true`, `EmissionTexture` is `lava004_emit_1k.jpg` (loaded when `ResourceLoader.Exists`), and `EmissionEnergyMultiplier` is today's emission strength.
- **`RideAssets`** (engine-free, in `src/Dungeon/Rails/Core/RideSound.cs` or its own file): `Models` and `Textures` path lists for the test.

- [ ] **Step 1: Tests.** `RideAssetsTests`: every listed path exists, and each is named in `assets/LICENSES.md`. Watch it fail, then implement.
- [ ] **Step 2: Implement.** Add these to `assets/LICENSES.md`:
  - `## Mine cart chase models` (loafbrr: page URL, CC0, "attribution appreciated" kept as courtesy);
  - gravel_floor_02 and old_planks_02 under the Poly Haven textures;
  - `## ambientCG textures (CC0)` for Lava004 (https://ambientcg.com/view?id=Lava004).
- [ ] **Step 3: Look.** Each is a screenshot:
  - `xvfb-run -a -s "-screen 0 1280x720x24" godot --path . -- --builtin --no-ai --floor=12 --face-node=MineCart:5 --screenshot=/tmp/cart.png --after=5`
  - the same with `--face-node=CageWagon1:4`
  - the same with `--face-node=RailStop2:6`
  - View them: the carts, levers and timbering show models or the fallback boxes, never nothing.
  - The log has `[MinePack] loaded N pieces`.
- [ ] **Step 4: Build and the regression runs.**
- [ ] **Step 5: Commit** `feat(rails): CC0 models and textures for the carts, the props and the tunnel`.

### Task 8: Polish — crew labels, lamps, the hidden wreck's HUD; docs

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/RailHud.cs`;
  - `src/Dungeon/Rails/CartLamp.cs`.
- Modify:
  - `src/Dungeon/Rails/RailController.cs` (`RefreshHud` :813, `Line` :822);
  - `src/Dungeon/Rails/MineCart.cs` (the lamp :208-230);
  - `src/Dungeon/Rails/SlagCart.cs` and `src/Dungeon/Rails/RunawayWagon.cs`;
  - `src/Adventure/Npc.cs` (`_label` :105);
  - `src/Dungeon/Rails/CageWagon.cs` (`Admit` :109, `Release` :115);
  - `src/Adventure/Core/Objectives/CrewRules.cs`;
  - `docs/development.md` ("## Assets" :244, "## Voice" :573, "## Rails" :1027).
- Test:
  - `tests/Dungeon/Rails/RailHudTests.cs`;
  - `tests/Adventure/CrewRulesTests.cs`.

**Interfaces:**
- **The HUD:**
  - `public readonly record struct RailHudState(CartMode Mode, bool Hidden, bool Halted, bool LineBlocked, bool GateClear, bool Boarding, float BoardingLeft, float Plating01, int StopIndex, int StopCount)`.
  - `public static string? RailHud.Text(in RailHudState s)` returns today's `Line()` strings exactly, branch for branch, and **null when Hidden**.
  - `RefreshHud` builds the state with `Hidden = !Cart.Visible`. On clients that comes from plan 5's Hidden flag. Hidden calls `SetCart(null, null)`, so a no-spare wreck no longer shows "Cart — wrecked" over a cart nobody can see.
- **Crew labels:**
  - `public static float CrewRules.LabelLift(int seat) => seat * 0.32f;`
  - `public void Npc.SetLabelLift(float metres)` raises the `Label` node above its scene position.
  - `CageWagon.Admit(ICartRider rider)` has no seat argument. When `rider is Npc n`, it reads the seat booked for `rider.RiderId` with the existing `Seats.SeatOf(int) → int?` (`CartSeats.cs:17`) and, when there is one, calls `n.SetLabelLift(CrewRules.LabelLift(seat))`. `Release` resets the lift to 0. The two names above a cage then no longer overlap.
- **Lamps:**
  - `public static void CartLamp.Attach(Node3D body, Vector3 at, bool enemy)` builds three parts:
    - a `SpotLight3D` "Lamp": party energy 6, range 26, angle 30°, attenuation 0.7, colour (1, 0.93, 0.78); enemy energy 4, colour (1, 0.55, 0.3);
    - an `OmniLight3D` "LampGlow": energy 1.2, range 3.5;
    - a 0.16 m emissive lens disc (`EmissionEnergyMultiplier = 3`) facing forward, so the lamp reads from ahead.
  - `MineCart` uses it with the same node names, replacing :210-230. `SlagCart` and `RunawayWagon` gain one at their front. The spec says every cart carries a lamp.
- **`cart_wreck`** was registered in Task 4 and plays from `Tumble()` in Task 5. This task only re-runs those checks.
- **Docs** (`docs/development.md`):
  - "## Assets": the mine pack, the new textures, the rail clips (with "cut by hand from Freesound CC0 previews; see LICENSES.md") and the chase track.
  - A new "### Lines, sound and music" under "## Rails":
    - the six taunt triggers and where they fire;
    - the six bark triggers;
    - Grisel's `Calls`;
    - `CartSound`, `RideAudio` and `RideSoundscape`, and why nothing rail-side is broadcast;
    - the Chase mood and the phase lock.
  - "## Voice": the pending voice pass. These lines are unvoiced, and the bake commands to run once the owner OKs it are the existing `--bake-all-voice=assets/voice/builtin` and `--bake-kill-barks` (:317-326). Name the speakers per line kind.

- [ ] **Step 1: Tests.** Write them, watch them fail, then implement.
  - `RailHudTests`: one case per branch of today's `Line()`, with equal strings:
    - wrecked → "Cart — wrecked";
    - line blocked;
    - gate down;
    - boarding: "Cart — 80% — departs in 3";
    - rolling;
    - on the line;
    - landing;
    - end of the line;
    - stop N.
    - Plus: Hidden with Wrecked → null.
  - `CrewRulesTests`: `LabelLift(0) == 0`, `LabelLift(1) == 0.32f`.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Look.** Screenshots as in Task 7, at `RailStop0` (the party lamp's cone reaches past the landing) and `CageWagon1` (two names, one above the other). Take one more with `--face-node=SlagCart` if a slag cart stands at build time; otherwise rely on the `--test-chase` log.
- [ ] **Step 4: Headless checks.** `--test-chase` or `--test-crew`, whichever flow reaches `CartEventKind.OnFoot` (grep for it), asserts `rc.HudText == null` after the no-spare wreck and prints `[chase] hud after the last wreck: none`. If no flow reaches it, `RailHudTests` covers it; say so in the report.
- [ ] **Step 5: Build, the suite, the five SP runs, and the three net_test passes.**
- [ ] **Step 6: Commit** `fix(rails): crew labels stack, every cart carries a lamp, a hidden wreck shows no cart bar; docs`.
