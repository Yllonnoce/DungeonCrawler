# Living NPCs, Local Voice and Lethal Traps: design

Date: 2026-10-22, revised the same day for pre-generated banks; local AI (Ollama, OllamaWeb, Piper) removed from scope at the owner's direction and kept only as a note for a future real-time path. Status: design for review, nothing implemented.
Written for: the builder of the next phases (a Claude session or the owner), who knows this codebase and has read the README.

This document builds on the game as it stands at commit 7b26849 (Foehammer). It covers three linked pieces of work:

- **Part A. Living NPCs.** Personality traits, fear, humour, agreement and disagreement for every speaking character; NPCs that join, follow, fight and leave; a reaction system that fires lines and gestures off game events.
- **Part B. Voice and pre-generated conversation.** Every line a character can say is written ahead of play by Claude, which already writes the story line, and voiced by the Runware bake already built; typed questions are matched to the pre-generated bank; the same in single-player and co-op, with the server owning the words. No local model and no real-time generation; that is a possible later path, noted in Phase 15.
- **Part C. Lethal traps.** Crushing walls and floor spikes that kill players, enemies and NPCs alike, placed fairly by the generator, synchronised by the server, with a rescue loop so a death is a setback and not a spectator seat.

Part D is the build order, test strategy and the calls I made without the owner. Appendix E lists the research the numbers come from.

---

## 0. Where the game stands (audit summary, 2026-10-22)

Facts from a read-only audit of the repository. The full test suite passes: 3410 xUnit tests, 46 s, two compiler warnings. Nothing is uncommitted.

**What already exists and is reused by this design**

| Area | What is there | Where |
|---|---|---|
| NPCs | `Npc : CharacterBody3D, IInteractable, ICombatTarget` with modes Quest, Ritualist, Prisoner, Fighter; flight from enemies (`NpcFlight`); fighter combat (`NpcFight`, 3x HP, 8-14 dmg scaled); `AskToJoin` = a seeded 70% coin; joined fighters are `Escorting` followers carried between floors by `Main.Companions`, which is not saved | `src/Adventure/Npc.cs`, `src/Adventure/Core/NpcFight.cs`, `src/Core/Main.cs:417-466` |
| Dialogue | Branching `NpcScript` nodes with 2-4 choices, effects `AcceptQuest, DeclineQuest, TurnIn, Farewell, Choose, Betray, Join`; `DialogueRepair` guarantees reachability; no free text anywhere except the multiplayer chat line | `src/UI/Dialogue/DialogueScreen.cs`, `src/Adventure/Core/Plans/FloorPlan.cs` |
| Voice | `IVoiceSynth.RenderPcmAsync(text, voice, style, ct)` returning 24 kHz mono PCM; Gemini, Runware and Fake implementations; content-addressed cache; one flat voice channel in `AudioManager`; rendering paused in combat; budget of 80 + 30 lines because every line costs API quota | `src/Adventure/Core/Voice/VoiceLines.cs`, `src/Adventure/VoiceRenderer.cs`, `src/Audio/AudioManager.cs` |
| Persona | `Persona { VoiceStyle, DefaultDelivery, Voice }`, `Delivery` enum of 13 moods, style is part of the voice line id | `VoiceLines.cs:12-53` |
| Barks | `BarkTrigger { Kill, Hurt, Loot, LevelUp, BossSeen, Possess, Downed, Revived }`, 24 lines per member written by the planner, 5 s global and 25 s per-trigger cooldowns, `BarkRepeat` every 2 floors | `src/Adventure/AdventureDirector.cs:156-178` |
| Villain | `TauntTrigger` (19 kinds) with `TauntDirector` cooldowns 12 s / 45 s and clean twins | `src/Adventure/Core/Objectives/Taunts.cs` |
| LLM | `IClaudeClient.CreateAsync(ClaudeRequest)`: one system prompt, one user string, one JSON schema, no history, no streaming; Claude SDK and Runware implementations; replay and recording wrappers | `src/Adventure/Core/Ai/ClaudeTypes.cs` |
| Companions | `CompanionBrain.Decide(CompanionContext)` engine-free; formation has exactly four offsets | `src/Characters/Companions/CompanionBrain.cs`, `src/Party/PartyController.cs:253` |
| Multiplayer | WebSocket, server simulates everything, clients own movement and send intents; `BarkMsg`, `NarrationMsg`, `ChatMsg` exist; dialogue is client-local and only effects cross the wire; story flags are never replicated to clients; `ShakeEvt` and `NpcChangedEvt` are defined but never sent | `src/Net/Core/Messages.cs`, `src/Net/ServerWorld.cs` |
| Traps | `TrapKind { Spikes, Fire, Gas }`, hidden until Perception reveals them, disarm by DEX, four per floor plus up to three from the plan; floor traps spring only for the party, party-placed traps only for enemies, burning-floor fire already hurts both; nothing hurts NPCs | `src/Dungeon/TrapRules.cs`, `src/Dungeon/Props/Trap.cs`, `src/Dungeon/Props/FloorFire.cs` |
| Damage | `ICombatTarget` on Enemy, Npc, Breakable; `Adventurer.ReceiveAttack` has a different signature; no shared hazard path; no moving geometry anywhere; A* has one flat cost per cell and a static blocked set | `src/Combat/ICombatTarget.cs`, `src/Dungeon/Generation/GridPathfinder.cs` |
| Local AI (not in scope) | Ollama is installed on the owner's machine with `qwen3.5:9b`, `qwen3:8b` and two roleplay 8-9B models; GPU is an RTX 5070 Laptop with 8 GB, fully used by the game while it runs; 32 threads, 30 GB RAM. Recorded for a future real-time path only | machine check |
| OllamaWeb (not in scope) | The owner's sibling project at `../OllamaWeb` (commit ff53629, 2026-10-15), reviewed as the shape a future real-time path would take: a FastAPI service that streams Ollama tokens over a WebSocket, splits them into sentences, synthesises each with in-process Piper 1.8 on one worker thread, and streams raw PCM frames tagged with a generation and sequence number for gapless playback; 101 offline tests; seven Piper voices in `voices/`; sentence splitter, markdown-to-speech cleaner and a chat-template token filter are pure and tested | `../OllamaWeb/ollamaweb/{pipeline,tts,text_clean,chat,security}.py`, `../OllamaWeb/docs/superpowers/specs/2026-10-15-ollamaweb-design.md` |

**Gaps this design closes**

1. A joined NPC is a follower, not a party member: no slot, no portrait, no save, no multiplayer rule, hard-coded on every floor, decided by a coin.
2. No character has a personality the game can read. Mood exists only as a per-line delivery hint.
3. Voice costs quota, so most lines are text. Live lines are impossible.
4. Dialogue is closed; the chat line is the only free text and it is broadcast to everyone, unaddressed.
5. Hazards never touch NPCs and never touch enemies except fire. The private escort-wound helper `Npc.Hazard` kills an escorted NPC outright in any mode, and a dead ritualist or prisoner makes a Ritual or Jailbreak floor impossible to finish.
6. Clients never learn story flags, so flag-gated choices already desync in co-op.

---

## 1. Intent

The owner wants the dungeon to feel inhabited and dangerous. Characters should have opinions, get scared, laugh, agree and disagree, and can be talked into walking with you. The dungeon should be able to kill anyone, including your allies and the monsters hunting you, with traps that are readable and fair. Everything must work the same in single-player and in four-player co-op, and local AI must stay optional: a player without Ollama or Piper gets the same game with prebaked lines and subtitles.

## 2. Goals, non-goals, anti-goals

**Goals**

- Every speaking character (four companions, campaign cast, recruitable fighters, villain) has readable traits and a mood that visibly changes play.
- NPCs can join the party as allies, with a real slot, real consequences, saves, and co-op support.
- Players can type to an NPC and hear an in-character reply at once, matched from a bank generated before play, with a graceful fallback at every stage.
- Two new lethal hazard kinds that kill players, enemies and allies, with telegraphing that a first-time player can read and a rescue loop that keeps a killed player in the game.
- Cloud spend stays under the existing cap. Claude credits write the story line and the banks; Runware credits voice them within the budget; the stock bank covers any gap when credits run out.

**Non-goals**

- Voice input from a microphone (designed for, scheduled last, not required).
- Facial animation beyond a jaw or mouth-open blend shape.
- NPC-to-NPC free conversation driven by the LLM (banter stays bank-driven with LLM-generated banks, see A4).
- A fifth party class, ally inventories, ally levelling.
- Any language model running while the 3D scene is on screen. Banks are written at bake time, on a loading screen, or on the CPU in the background.
- Free-form improvised replies. Every line is pre-generated, validated and editable; typed questions are matched to topics.

**Anti-goals** (would make this a failure even if it works)

- An LLM that can change game state by being talked into it. The model writes words; rules decide outcomes.
- A trap that kills with no tell, or that blocks the only route with no dodge.
- A crushed co-op player who sits in a black screen until the floor ends.
- A joined NPC who is invulnerable scenery, or one whose death silently breaks a quest.
- A build that requires any key or any local AI service to start.

## 3. Constraints

- Godot 4.7.2 .NET, C#, .NET 8. Engine-free rules live under `*/Core` and are covered by xUnit; Godot nodes are thin.
- Save-stable indices: new content gets `SpawnIndex`-style stable ids; floors with no new content must hash identically to today so existing seed tests keep passing where they can (see D3 for the one deliberate change).
- Multiplayer: server authoritative for every outcome; clients own only movement and rendering. `MsgKind` is append-only. The Render server has no GPU and few cores.
- No local model runtime is linked or launched by the game. Everything generated comes from the cloud planner at bake time or from stock data.
- The game uses the whole 8 GB card while it runs. No model inference on the GPU during play; bank writing happens when the scene is not rendering, or on the CPU.
- The Lethality setting (1-3, default 3) remains the one dial for how deadly the dungeon is.

---

# Part A. Living NPCs

## A1. Who has a personality

Every AI-driven speaker gets a `Personality` record: the four default companions (Brand, Sable, Wren, Ilex) in single-player, every campaign cast member and quest NPC, every recruitable fighter, the villain. Human-controlled bodies have none: the possessed lead in single-player keeps its companion's record but is not spoken for while possessed, and remote players in co-op speak for themselves.

**Co-op has no AI companions** (a locked decision from the multiplayer plan; empty slots are parked bodies). So in co-op the speakers with personality are quest NPCs, the one ally, and the villain. The reaction rules in A4 and A6 cast among whoever is present and AI-driven; with nobody in range they stay silent. The co-op experience of Part A is therefore "the dungeon's inhabitants and your recruit react to you", not "your party chatters", and the co-op test scope in D2 is written for that. Filling parked slots with server-driven companions is out of scope.

The viewer-made characters (Brannock, the Goddess, Foehammer, the Orc Brute) move out of hard-coded C# in `DungeonBuilder.PlaceGreeter/PlaceViewerCast` into data: `data/npcs/*.tres` (`NpcDef`: id, name, model path, weapon and scale, mode, personality, persona, home tier range, script id). The builder places them from the plan like every other NPC. The floor-1 greeter and the moves grid stay as a debug showcase behind `--showcase`.

## A2. The trait model

Two layers, after Crusader Kings 3: readable labels for players and prompts, hidden axes for rules.

**Ten axes, 0-100, each the midpoint of an opposed pair.** A label is shown when the axis is at or above 70 or at or below 30. A character shows at most three labels; the rest of its axes sit between 35 and 65.

| Axis | Low label | High label | What it drives |
|---|---|---|---|
| Courage | Craven | Brave | stress gain multiplier, flee threshold, join refusal under threat |
| Humour | Dour | Jovial | laugh gestures, joke lines, relief barks after a kill |
| Deference | Contrary | Agreeable | agrees or argues with the leader's choices, Decide quests, stance changes |
| Loyalty | Fickle | Loyal | leave threshold, betrayal susceptibility, stays when the party is losing |
| Greed | Generous | Greedy | loot reactions, joins for a cut, resents the leader taking everything |
| Temper | Patient | Hothead | preferred stance, interrupts banter, blames allies when hurt |
| Curiosity | Wary | Curious | trap-check lines, wants to open vault doors, warns about secrets |
| Faith | Cynical | Pious | reactions to rituals, altars, the villain's taunts |
| Pride | Humble | Proud | reaction to being possessed or ordered, refuses to flee first |
| Talk | Quiet | Chatty | bark frequency multiplier 0.5x to 1.6x |

Companions get fixed axes in `ClassDef` defaults (Brand brave-proud, Sable greedy-contrary, Wren wary-quiet, Ilex curious-cynical, the builder may tune). Campaign cast axes are chosen by the planner (`CastMember.Personality`, validated to the limits above, filled by `CampaignRepair` from a seeded deal when missing). Offline designer deals them from the seed.

**Why ten and not forty.** The research consistently lands on three to five visible labels from a pool of ten to forty, and five to ten hidden axes (CK3 has 36 labels on 9 axes, Wildermyth has 11 hooks, The Sims 4 has 35 labels with 14 exclusion pairs). One axis per pair keeps the mapping trivially explainable to the planner and the LLM.

**Relative casting.** When the reaction system needs "the coward" or "the joker", it picks whoever in the present party has the lowest or highest axis value, Wildermyth style. Every party therefore always has someone scared and someone laughing, so the content always fires.

## A3. Mood: stress, affliction, affinity

**Stress, 0-100, per AI-driven body: companions, allies and NPCs.** Human-controlled bodies carry no stress (the human is the morale). When the player possesses a companion, that companion's stress and stat effects persist but its act-outs and refusals are suspended until the player switches away; they resume afterwards. Remote players in co-op have none. Stress lives on `CharacterState` for companions (saved in `CharacterDto`) and on the ally record for allies; it is never synced to clients except through the portrait tint byte already carried in `Vitals` (one new byte). Not shown as a bar; shown by behaviour, portrait tint and lines. Gains and losses are flat numbers scaled by `(1.5 - Courage/100)` for gains:

| Event | Stress |
|---|---|
| Ally downed within 12 m | +15 |
| Took a hit above 25% max HP | +8 |
| Lethal trap triggered within 8 m | +10 |
| Party member killed by a trap | +25 (everyone who saw it) |
| Horde triggered, portal opened, boss seen | +10 |
| Villain taunt heard | +4 (Pious +8) |
| Idle 60 s with no enemy within 30 m | -5 per 10 s |
| Kill | -3 (Jovial -5) |
| Trap disarmed by this character | -8 |
| Joke line from a Jovial character | -4 to everyone in 10 m |

**Bands.** Calm 0-39, Shaken 40-69, Breaking 70-99. At 100 a resolve test fires once: 25% chance of a Virtue (Stalwart: takes 15% less damage for the floor; Rallying: everyone in 10 m -15 stress) else an Affliction, both from Darkest Dungeon's split. Stress then resets to 45.

**Afflictions, one per character, cleared at the next floor or by a rest.** Four, each with one stat effect, one refusal and one act-out:

| Affliction | Effect | Refusal | Act-out every 15-25 s in combat |
|---|---|---|---|
| Fearful | -20% damage dealt | will not hold Aggressive stance | runs one cell away from the nearest enemy and says so |
| Reckless | +15% damage, -15 armour | will not Hold | charges the nearest enemy regardless of stance |
| Sullen | affinity gains halved | will not answer typed questions (bank line "Not now.") | mutters a line that adds +4 stress to one ally |
| Hysterical | none | will not disarm traps | laughs at the wrong moment, +4 stress to everyone in 8 m, -4 to self |

Rules copied from Darkest Dungeon: the same character never acts out twice in a row, and two characters never act out back to back.

**Affinity, -50 to +100, per NPC toward the party.** In co-op it is per player (Baldur's Gate 3 keeps a separate value per avatar). Event sizes are 1, 5 and 10. Reaching -20 and -40 triggers a warning line; at -50 an ally leaves at the next stairs and a quest NPC refuses dialogue beyond Farewell. Gains come from: freeing them, reviving them, completing their quest, siding with them in a Decide, killing their named enemy, handing them a potion. Losses come from: ordering them into a crusher, letting them bleed while potions exist, choosing against them in a Decide, insulting them in free text (classified by the intent enum in B3, never by raw text), looting their quest item in front of them (Greedy halves this). Each gain or loss is announced the BG3 way: a short toast "Foehammer approves" and, when a line exists, the line.

## A4. The reaction system

A new engine-free `ReactionDirector` in `src/Adventure/Core/Reactions` replaces the hand-wired bark calls in `AdventureDirector`. It is the only thing allowed to start a line, so spam control lives in one place.

**Inputs.** Existing `EventBus` events plus new ones this work adds: `CombatStarted`, `CombatEnded` (replacing the two polling sensors in `VoiceRenderer` and `MusicDirector`), `EnemySpotted(Enemy, byMember)`, `TrapRevealed`, `HazardPhase(index, phase)`, `MemberKilledByHazard`, `AllyHurt`, `NpcFleeing`, `AffinityChanged`, `ChoiceMade`, `Laughed`.

**Intensity.** A Left 4 Dead style 0-1 value per party, rising on damage, downs and nearby deaths, decaying only when no enemy is engaged, with phases Build, Peak (held 3-5 s), Fade, Relax (30-45 s). Ambient banter is allowed only in Relax; alerts are allowed in any phase.

**Categories and the interrupt matrix.** After Vermintide 2's dialogue system, which selects on the server and lets every client play audio and subtitle from an id:

| Category | Examples | Interrupted by | Can play during |
|---|---|---|---|
| Alert | "Spikes!", "Behind you", "He's down" | nothing | anything except Story |
| Story | narration, villain taunts, quest lines | nothing | nothing |
| Reaction | scared, laugh, approve, disapprove, flee | Alert, Story | Banter |
| Banter | idle chatter, opinions about the floor | everything | nothing |
| Answer | a bank reply to a typed question | Alert, Story | nothing |

One line per speaker at a time. Global gap 5 s between lines from different speakers except Alerts. Per (speaker, trigger) cooldown 25 s as today. Banter cadence 90-180 s while in Relax, scaled by the speaker's Talk axis. `BarkRepeat` continues to suppress lines said in the last two floors.

**Choosing the speaker.** Role lines use relative casting from A2 (the lowest Courage speaks "scared" lines, the highest Humour laughs). Knowledge lines use proximity: whoever is within 12 m and has line of sight. In co-op the same rule runs on the server, and the result is broadcast.

**Choosing the line.** Three sources in order: (1) the authored lines for this speaker and trigger (planner barks, taunts, quest lines); (2) the generated interaction bank (B3) for this speaker, trigger, labels, mood band and affinity band; (3) silence. Nothing is generated at play time.

**Gestures.** A closed enum `Gesture { None, Laugh, Flinch, Cower, Nod, ShakeHead, Shrug, Point, Cheer, Wave, Grieve }` generated into the LLM schema and the prompt from the same C# enum so the two cannot drift. Each maps to a one-shot clip on the model when the clip exists (viewer models carry clips named by convention, the moves grid already lists them) and to nothing when it does not. A `Laugh` also plays a short laugh sample from the speaker's voice (prebaked once per voice by Piper, see B2).

**Banks.** The planner's bark prompt grows from 8 triggers x 3 lines to the full trigger list and asks for lines tagged by label and mood band. `VoiceBudget` stops mattering once Piper renders locally, but stays as the cap for cloud voice.

## A5. Allies: NPCs that join the party

**The Ally tier.** An ally is an `Npc` in Fighter mode holding an `AllySlot` in `PartyController` (new list `Allies`, max 2 in single-player, max 1 in co-op). Allies are not `Adventurer`s and not `CharacterState`s: no bag, no paper doll, no XP, no possession. They get formation offsets 5 and 6 (two new entries behind the back rank), follow the party stance (R cycles them too), a small portrait row above the bag with HP and mood tint, and a name tag.

**Combat.** `NpcFight` scales as today by floor. Allies gain the party's status effects and damage-over-time (today `Npc.ApplyStatus` is a no-op; allies implement it with the normal controller). Enemies target allies by the same threat rules as members.

**Joining.** The 70% coin in `NpcFight.Decides` becomes `JoinRules.Decide(personality, affinity, stress, threatNearby, asks, seed)`:
- Base 35%, +Loyalty/4 %, +affinity, -stress/2, -30 if an enemy is within 16 m and Courage < 50, +15 per earlier refusal (persistence is rewarded, as the Foehammer refusal text already promises), Greedy characters add +25 when the party has handed over gold or an item in this conversation.
- The seeded hash stays as the dice so tests and replays are deterministic.
- Refusal lines name the reason from the formula (too scared, does not trust you yet, wants pay), which tells the player what to change.

**Dismissing and leaving.** New dialogue effects `Dismiss` (player) and automatic `Leave` (affinity -50, or Fearful plus Craven after a party wipe, or Fickle when a better-paying faction NPC is met, used sparingly by the planner). A leaving ally walks to the up-stairs and despawns; the campaign memory records it; the villain gets a `AllyLeft` taunt.

**Death.** Allies go Downed from combat (revive with E as today). Lethal hazards put them in Downed with the Mangled flag (C6). If the party changes floor with an ally still Downed, the ally dies: recorded in `FloorRecord.NpcFates`, affinity of every other NPC of the same cast drops 10, villain taunt `AllyDead`. The `Npc.Hazard` helper (escort damage) can no longer kill outright; it downs.

**Persistence.** `AdventureDto.Allies` (id, def id, name, model, hp fraction, personality, stress, affliction, affinity, floor joined, kills). `Main.Companions` goes away. Save version 9.

**Planner and designer.** `NpcPlan.Mode` (Quest default, Fighter) and the `Join` effect are exposed to the planner with a one-line rule in the prompt: at most one recruitable fighter per tier, never the betrayer. `OfflineDesigner` places one per tier from `data/npcs`. `PlanValidator` enforces the cap and that a fighter has a `hello` node with a Join choice and a `NextIfRefused`.

**Co-op.** The Join choice already travels as `DialogueChoiceIntent`, but today the client then shows the "yes" node whatever the server rolled. A new `JoinResultMsg(npcId, member, accepted)` closes that loop: the client's dialogue screen shows a "..." beat until the result arrives (2 s timeout shows the refusal node), then `Next` or `NextIfRefused`. The server decides, the `AllySync` message (new) carries the ally record to all clients, and the ally follows the player who recruited it (its "owner"); if the owner is parked it follows the anchor. Two players asking the same NPC within the same conversation are resolved by conversation ownership (B4).

## A6. Scared, agreeing, laughing: the concrete behaviours

These are the moments the owner asked for, written as rules the ReactionDirector produces.

- **Scared.** When intensity crosses Peak or a lethal hazard arms within 8 m: the lowest-Courage character in range plays Cower or Flinch and a Reaction line from the "scared" pool; if their stress is Breaking they also take one step back from the threat. A Craven ally in Fearful affliction refuses to enter a corridor cell flagged dangerous and says so.
- **Agree or disagree.** Any `ChoiceMade` on a Decide quest, any stance change by the leader, any vault door opened, any quest declined: the highest-Deference character approves out loud, the lowest-Deference argues; both apply ±1 affinity. In co-op the server picks and broadcasts.
- **Laugh.** On a kill by an environmental hazard, on a critical hit by the party, on a Jovial banter line, on an enemy falling into a pit: the highest-Humour character laughs (gesture plus sample) and the party loses 4 stress; a Dour character in range may answer with a "Nothing funny about it" line.
- **Warn.** The highest-Curiosity character names revealed traps and visible hazards: "Grooves in the walls. A press." This doubles as the player's tutorial.
- **Extra.** Pious characters pray at altars and flinch at taunts; Greedy ones comment on loot rarity and sulk when the leader takes a legendary; Proud ones object to being possessed ("Don't steer me") when the Possess bark fires.

---

# Part B. Voice and pre-generated conversation

## B0. Why nothing is generated during play

The owner's direction: real-time generation is not being built now, and the game uses the whole graphics card while it runs anyway. So **no language model runs during play, locally or in the cloud**. Every line an NPC can ever say is generated ahead of time into an interaction bank by Claude at bake time, voiced ahead of time by the Runware bake, and at runtime the game only matches, picks and plays.

This is the shape the "author-led" studios landed on anyway (Dead Meat, Where Winds Meet): generated text that a person can read and edit before players meet it, and game rules that never depend on a model being awake. It also removes the host-brain co-op path and the prompt-injection surface, because nothing a player types ever reaches a model.

Generation windows, in order of preference:
1. **Campaign bake** (Settings "Rebuild campaign", `--bake-campaign`): banks are written right after the floor plans, through the same `IClaudeClient`, metered by the same cap, recorded for replay.
2. **New Game loading screen** for a campaign whose banks are missing: the existing loading overlay with narration, using the planner's prefetch path.
3. **Stock.** Any bank still missing when play starts is filled from `BankStock` by `CampaignRepair`, so a campaign always has lines.

## B1. Architecture in one paragraph

Text is authoritative and crosses the wire; audio never does. A `Utterance` is (speaker ref, bank line id, text, clean text, delivery, gesture, voice id, utterance id, server tick). In single-player the ReactionDirector or the conversation system creates utterances from the banks locally. In co-op the server creates them and broadcasts `UtteranceMsg`; every client plays the cached Runware audio for that voice id, or shows a subtitle if it has none, exactly as `NarrationMsg` works today. Banks travel with the plan (`CampaignPlan.Banks` and `FloorPlan.Banks`), which already reach clients in `Welcome` and `FloorChange`.

**The bank writer is the campaign planner.** Banks are one more planning call per speaker, built by `PromptBuilder.Banks` beside the outline, floor and bark calls, answered by whichever `IClaudeClient` the planner already uses (Claude directly, or Runware's text models), validated like every other plan, and recorded so a replay bake needs no network.

```
Bake time                                                     Play time
PromptBuilder.Banks -> IClaudeClient -> BankValidator          ReactionDirector / TopicMatcher pick a bank line
saves data/adventures/<campaign>/banks/<speaker>.json          cached Runware wav for its voice id -> AudioStreamPlayer3D
VoiceRenderer renders bank lines with Runware by priority      no audio cached -> subtitle
```

## B2. Voice: the Runware bake, as built

The owner's decision (2026-10-22): voices are recorded with Runware through the existing `VoiceRenderer`, `RunwareTtsClient` and content-addressed cache. No local TTS runs at play time. Local voice is out of scope; Phase 15 notes it only as a possible later path.

**What changes in the existing bake.**
- `VoiceBudget` becomes bank-aware. Priority order for a Runware render: authored story and quest lines, villain taunts, reactions for the Alert and Reaction categories, then topic answers for `Greeting`, `WhoAreYou`, `TheVillain`, `TheObjective`, `YourQuest`, `JoinUs`, `WhyNot`, `Farewell`, then everything else. The budget and the cap in Settings stay the owner's dial; lines outside it play as subtitles, exactly as today.
- Per-character voices: each `NpcDef`, cast member and fighter carries a `Persona.Voice` that `RunwareVoices.Map` resolves per model family; the owner picks voices by ear from the Runware catalogue and writes them into the data files. Fighters and archetype bodies share one voice per body so a re-cast NPC never changes voice mid-campaign.
- Next-floor pre-render stays as built (the queue pauses in combat); bank lines for floor n+1 join the queue behind authored lines.
- Rate and quota handling stays as built (per-minute pacing, model fallback chain, stop on daily 429). At the first bake's measured 8 lines a minute a 6,000-line campaign is 12 hours of rendering, so the budget, not the bank, decides how much is voiced; the builder reports lines rendered against lines in the banks on the Settings status line.

**Playback.** Speech becomes positional: each `Npc`, companion and ally gets an `AudioStreamPlayer3D` on the Voice bus (12 m unit size, 30 m max) playing the cached `AudioStreamWav`. Narration and taunts stay on the flat channel. The one-voice-channel rule in `AudioManager` becomes one channel per speaker with the ReactionDirector matrix as the arbiter. Music ducking keys off any speaker being busy.

**Mouth.** Amplitude-driven: an `AudioEffectSpectrumAnalyzer` on each speaker's bus gives an RMS per frame that drives either a bone named `Jaw` or a blend shape named `MouthOpen` when the model has one (a documented convention for the owner's viewer exports), else nothing. Zero latency, good enough at first-person distances.

**Laugh and grunt samples.** One short laugh and one grunt per voice, rendered once by Runware, used by the Laugh gesture and as the 0.3 s filler before an unvoiced line's subtitle appears.

**Settings.** The AI section gains "Write interaction banks now" (using the planner provider already selected), a bank line count, and the voice status line shows rendered lines against bank lines.

**Fallbacks.** No cached audio for a line: subtitle, as today. No planner key or cap reached: banks come from stock.

## B3. Interaction banks: pre-generated conversation

**What a bank is.** Per speaker, a JSON file written at bake time and shipped with the campaign (`data/adventures/<campaign>/banks/<speakerId>.json`, same place for the user rebake under `user://adventures`). Engine-free DTOs in `src/Adventure/Core/Banks`:

```
InteractionBank { SpeakerId, Version, PersonaHash,
  Reactions:  [ { Trigger, MoodBand, Labels[], Line } ],          // A4 triggers x 3 mood bands x 2 variants
  Topics:     [ { Topic, AffinityBand, MoodBand, Line, FollowUps[2], Effects? } ],
  Keywords:   { Topic -> [ words and short phrases ] },           // for free-text matching
  Deflections:[ { MoodBand, Line } ],                             // "I don't follow you" in character
  Repeats:    [ { Line } ] }                                      // "You asked me that already"
```

**Topics** are a closed enum of about 28 ids: `Greeting, WhoAreYou, ThisPlace, TheVillain, TheObjective, YourQuest, YourPast, YourFear, YourSecret (gated by flags), Cast1..Cast6 (one per campaign cast member), JoinUs, WhyNot, Leave, Payment, Loot, Danger, Traps, TheWarrior, TheRogue, TheRanger, TheMage, Joke, Insult, Flattery, Thanks, Farewell`. Each topic has lines for three affinity bands (hostile, neutral, friendly) and three mood bands, written as 9 short variants; most topics are 1-2 sentences so a bank is 300-400 lines, about 8,000 tokens to generate. A campaign of 8 quest NPCs, 6 cast members, 4 companions and the villain is roughly 6,000 lines: 10-15 minutes on the owner's GPU at bake, well under the existing voice bake's cloud time, and free.

**Follow-ups.** Every topic line carries two follow-up topic ids the writer chose to fit the line ("Ask about the bell" after a line that mentioned it). The dialogue screen shows them as extra choices under the authored script, so a player who never types still gets a conversation that branches further than today's 2-4 node scripts. The authored script keeps priority: quest effects stay on authored choices only.

**Who writes banks.** `IBankWriter.WriteAsync(persona card, topics, ct)` with two implementations behind the existing planner-provider choice: `ClaudeBankWriter` (default when a Claude key exists; the owner has credits and Claude already writes the story line, so the banks match its cast and voice; through the existing `IClaudeClient`, metered by `CostMeter` under the spend cap, recorded for replay), and `RunwareBankWriter` (the same path through Runware's text models). Ten topics per request keep each reply under 600 tokens. The offline designer ships a stock bank per label pair (`BankStock`) so a campaign with no writer at all still has lines; `CampaignRepair` fills any gap from stock, the same way `TauntBank` does for taunts. The persona card from the earlier draft (name, role, labels, mood, what they know, secrets marked never-reveal, three style lines from the authored script) is the system prompt for every request.

**Validation and editing.** `BankValidator` checks the schema, sentence count (1-3), length (under 180 characters), banned words (clean-twin list), that no `never-reveal` secret string appears in any line, and that every topic and band is filled; a failing topic is regenerated once, then taken from stock. Because banks are plain JSON next to the campaign, the owner can read and hand-edit any line, and the bake preserves lines marked `"locked": true`.

**Typing to an NPC.** The chat line (T) works in single-player too. While a dialogue screen is open the line sits under the choices with the prompt "Say something". Outside dialogue, T while looking at an NPC or ally within 4 m addresses them; otherwise in co-op it is party chat, in single-player it addresses the nearest ally within 6 m or does nothing. A typed line is capped at 200 characters (the existing `MaxChatLength`).

**Matching, on the CPU, in under a millisecond.** `TopicMatcher.Match(text, bank, context)` in `src/Adventure/Core/Banks`: lower-case, strip punctuation, tokenise; score each topic by weighted overlap with its keyword set (keywords written at bake plus fixed seeds per topic plus the live names of cast members, party members and the villain); boost `Insult` and `Flattery` from a small fixed lexicon; return the best topic above a threshold, else `Deflection`. A topic already asked in this conversation returns a `Repeats` line the second time and the normal line again the third. The match result is logged with the text so misfires can be reviewed and keywords improved. The upgrade path, Phase 15 if ever, is embedding similarity with a small local model; it is not needed to ship.

**Effects.** A topic can carry an `Effect` from the same advisory set as before (`Insulted` costs 1 affinity, `Flattered` gains 1, both capped at 3 per floor; `JoinUs` opens the normal Join roll; `Payment` enables the hand-over choice). The effect is attached to the topic by the game's rules table, never written by the model.

**Memory.** Per NPC: the list of topics asked and by whom (player name in co-op), persisted in `AdventureDto.NpcMemories`, fed into `CampaignMemory` so the cloud planner can reference what the player asked the cast. Settings has "Forget conversations between floors" for public servers.

**Latency at play time** (targets, measured in Phase 14):

| Stage | Target |
|---|---|
| Typed line to matched topic | under 1 ms |
| Cached audio | plays at once |
| Unvoiced line | subtitle at once |

**Guardrails.** Player text never reaches a model, so the only input check is a denylist that replaces slurs with asterisks in chat and logs. Output is pre-generated, validated and editable. Per-player rate limit one typed line per 2 s; per-floor cap none needed.

**Reactions and banter from banks.** The ReactionDirector's second source becomes "the generated bank for this speaker, trigger, labels and mood band" instead of a live request; banter lines are a `Banter` trigger with 12 variants per mood band. The 24-line limit on today's planner barks goes away because the writer is local and free.

## B4. Co-op

**Conversation ownership** (Baldur's Gate 3 and the Bounded Autonomy arbiter): the player who opens dialogue or first addresses an NPC owns the conversation (`ConversationMsg(npcId, ownerMember)`), others within 12 m auto-listen (subtitles and voice), the NPC answers only the owner, a listener's typed line is queued and acknowledged with a Nod; ownership releases on Farewell, on the owner walking more than 6 m away, or after 20 s silence. The dialogue screen itself stays client-local for branching; only effects, free text and the lock cross the wire.

**Where matching runs.** On the server, always. A typed line travels as `SayIntent(npcId, text)`; the server runs `TopicMatcher` against the bank it already holds, applies the effect, and broadcasts `UtteranceMsg(speaker, lineId, text, cleanText, delivery, gesture, utteranceId, serverTick)`. Every client plays it from its Runware cache or shows the subtitle. There is no model on the server and no host-brain election; the earlier `--llm` flag and `UtteranceProposal` message are gone. A dedicated server that was started with `--campaign=ai` writes banks on the server with its cloud writer at plan time, like floor plans today.

**Flag replication.** `FlagsSync(List<string>)` is added and sent on Welcome, on every `SetFlag`, and on floor change, fixing the existing co-op desync in flag-gated choices.

**Subtitles.** Every client renders the subtitle from the `UtteranceMsg` text at the server tick, not from its audio, so players with and without the audio cached read the same line at the same time. Lines from speakers more than 20 m away are not shown.

**Replay and tests.** Bank writes are recorded like plans (`user://ai/log`) and `--replay-banks` feeds them back, so `--bake-campaign`, `--test-say` and `net_test.sh` are deterministic without a service.

---

# Part C. Lethal traps

## C1. Principles the research supports

- Telegraph before lethality. The mechanism is visible in the idle state; the trigger may be hidden. Once a tell is taught, never lie with it.
- Warning then squeeze. Every lethal hazard has a pause between trigger and lethal phase that a running character can use.
- Everyone dies. Lethal hazards kill enemies and allies by the same rule as players. Killing enemies with the dungeon is a core joy (Hades, Dead Cells, Spelunky, Dishonored).
- The route is never sealed. Generator guarantees a path, and lethal one-shot hazards sit off the critical path or guard rewards; periodic hazards on the path are dodgeable by timing.
- One-of-a-kind per floor for the nastiest thing (Spelunky's giant spider rule).
- A death is a setback with a rescue loop (Vermintide, Deep Rock Galactic), not a spectator seat.

## C2. Shared hazard pipeline

A new `IHazardVictim` interface implemented by `Adventurer`, `Enemy` and `Npc` (today `Adventurer` does not implement `ICombatTarget`):

```csharp
enum HazardKind { Crush, Impale, Fire, Gas, Fall }
void ReceiveHazard(HazardKind kind, float damage, bool lethal, string sourceName);
```

`lethal: true` bypasses mitigation, guard and shields. `Settings.Lethality` is a float from 1 to 3 (default 3). A hazard is lethal to players and allies when Lethality is 2.5 or above; below that it deals `(0.5 + 0.25 * (Lethality - 1))` of max HP, so 50% at 1 and 87.5% at 2.5. Ally and NPC max HP already scale with Lethality (`TierRules.NpcHp`), which is kept: a deadlier dungeon has tougher allies. What "lethal" means per victim:

| Victim | Lethality at or above 2.5 (default 3) | Below 2.5 |
|---|---|---|
| Party member | Downed + Mangled (C6) | fraction of max HP as above |
| Ally | Downed + Mangled | fraction of max HP as above |
| Quest NPC (Quest mode) | Downed, never dead | same |
| Ritualist, Prisoner | Downed; never dead | same |
| Enemy | dies, no loot, XP to the nearest member within 10 m, counts as an environmental kill | dies |
| Boss | 35% max HP and a 2 s stun; a boss is never one-shot by geometry | same |

**Bodies leave the footprint.** At the Crush or Impale instant every Downed body (member, ally, NPC) inside the hazard footprint is moved to the nearest walkable cell outside it, facing away, before the Hold phase begins; the flattened or skewered decal stays inside. Enemy corpses are removed as today. Plates ignore Downed bodies and props on them. So a rescuer never has to stand in a press to reach a body, a periodic spike bed never re-impales anyone, and the co-op bound timer (C6) starts at the instant of the kill.

Existing fire and gas keep their numbers but move onto this interface so they can finally hit NPCs (quest NPCs down, allies take damage). The `Npc.Hazard` escort helper downs instead of killing.

## C3. The Press: crushing walls

**Where.** A straight corridor run of exactly three cells (12 m) with walls on both long sides. The two side walls of all three cells are replaced by two slabs (`AnimatableBody3D`, full height, 0.5 m thick, in the theme's wall material with visible vertical grooves and, in Forge and Abyss, inward spikes) that slide toward each other.

**Tells in the idle state.** Grooves in the floor and ceiling along the slabs; a thin gap of darkness behind each slab; scattered bones or a crushed barrel in the middle cell (the Rule of the Dead Adventurer); a faint periodic creak within 10 m; the Curious character's warning line.

**Trigger.** A pressure plate in the middle cell, hidden until Perception reveals it (today's `TrapRules.DetectRadiusCells`), disarmable by DEX as today, or stepped on by anyone, including enemies and allies. A thrown object or arrow landing on the plate also triggers it.

**Timeline** (server-driven state machine, `HazardRules.Press`):

| Phase | Duration | What happens |
|---|---|---|
| Armed | until trigger | idle tells |
| Warning | 1.2 s | grinding start, dust from the ceiling, torches in the run flicker, camera rumble within 10 m, slabs move 0.2 m |
| Closing | 2.4 s, ease-in | slabs close from 3.5 m to 0.8 m apart; danger value of the three cells rises to 1.0 |
| Crush | instant | any victim whose capsule centre lies inside the run at this moment receives `Crush, lethal` |
| Hold | 1.5 s | slabs pressed; crunch; a flattened body decal where a victim was; victims' bodies are hidden by the slabs |
| Retract | 4.0 s | slabs open |
| Cooldown | 6.0 s | plate inert; then Armed |

Total warning before the crush is 3.6 s. A character in the middle cell needs 6 m to clear the run, 1.0-1.5 s at run speed, so a player who reacts within two seconds lives. Enemies chasing the party through the run during Warning are the point.

**Jamming.** A barrel, crate or rubble stone left in the run stops the slabs at 1.0 m (the Hexen rule): no crush, Hold 1.5 s, then Retract, and the prop breaks after its second squeeze. A Downed body does not jam.

**Counterplay.** Disarm the plate; trigger from range and walk through during Cooldown; bait a horde in and step out; a party-placed Spike or Fire trap on the plate springs it when an enemy arrives.

**Reset.** Always re-arms. A kill of a party member or ally adds a flattened decal that persists on the floor and in the save (one bit per press).

## C4. The Spike Bed

**Where.** A 2x2 or 3x3 cell patch inside a room, or a single corridor cell. Holes in the floor slabs are visible always. Two variants:

- **Pressure.** Anyone stepping on any cell: 0.5 s click and a pre-raise of 10 cm, then spikes up for 1.0 s (lethal to anyone whose feet are in the patch at full height), retract 1.5 s, re-arm 4 s.
- **Periodic.** A metronome from the floor's start tick: 3 s down, 0.5 s click and pre-raise, 1 s up. Phase is `(serverTick + patchIndex * 37) mod period` so beds on one floor are out of step. Always on the critical path when it is chosen there, because it is dodgeable by watching.

**Dodge.** Jumping (Space exists) over a cell during the up phase is a dodge; crouching is not. Being Downed in the patch when it rises is a kill. Enemies with a jump animation do not get the dodge; they take it, which is the fun.

**Tells.** Holes, dried blood in the Crypt, a skewered rat in the Caves, scorch in the Forge, and the click. Periodic beds also show a faint rhythmic glow in the holes.

## C5. Placement in the generator

New `HazardKind { Press, SpikeBed }` with `HazardPlacement { Index, Kind, Cells, Variant, Period, Phase, GuardsCell }` on `FloorLayout.Hazards`, dealt in `FloorGenerator.PlaceHazards` after `PlaceTraps` with its own seed `Rng.Mix(seed ^ 0x4A2D)`.

**Counts.**

| Floors | Press | Spike beds |
|---|---|---|
| 1-2 | 0 | 0 |
| 3-5 (Crypt) | 0-1 | 1 pressure |
| 6-10 (Caves) | 1 | 1-2, periodic allowed |
| 11-15 (Forge) | 1 | 2 |
| 16-20 (Abyss) and endless | 1 | 2-3 |

At most one Press per floor. Boss floors get none inside the arena.

**Rules** (each is a unit test):
1. Compute the critical path start to down-stairs first. A Press never sits on a cell whose removal disconnects start from stairs. A pressure spike bed never does either. A periodic bed may.
2. Press needs three straight corridor cells with walls on both sides, no doors, no ramps, no water, no vault, not within two cells of stairs, the start cell, an NPC home, a jail cell, an altar, a nest mouth, or an existing trap.
3. Spike beds sit in rooms of at least 5x5 or in corridor cells, never adjoining a doorway cell, never in Flooded floors (water hides holes), never in a cellar, never in the Ritual altar room or jail rooms, never under a ledge.
4. Labyrinth floors get no Press (every passage is a unique route). Burning floors get no spike bed (two floor hazards is noise).
5. 60% of hazards guard a reward. Generator chests are already placed in `PlaceSpawns` before hazards, and moving them would change the RNG draw order for every floor, so the rule runs the other way: `PlaceHazards` prefers candidate runs and patches that lie between the critical path and an existing chest, vault door or key cell, and marks the guarded cell. The designer's extra chests (`PlanResolver` from `FloorPlan.Chests`) prefer marked cells, and `RoomCells.Free` excludes hazard cells so plan content never lands on them.
6. Flow distance escalation: hazards closer to the start are periodic or longer-warning; pressure variants and the Press prefer the back half of the floor by path distance.
7. Hazard cells join `IsReserved`, so later placers (spawns, traps, chests, structures) stay out; `ContentHash` includes hazards.
8. `--hazard=KIND` forces one near the start for screenshots and `--test-hazard=press|spikes` drives it.

**Compatibility with objectives.** Toxic gas floors keep their hazards (gas adds pressure to timing); Shifting Vault gates never share a corridor with a Press; Jailbreak jailers path around danger like any smart enemy.

## C6. Death, Mangled, rescue

Today a downed member auto-revives at 1 HP after 3 s of calm, and there is no permanent death. That stays for ordinary combat. Lethal hazards add one flag:

**Mangled** (on Downed): auto-revive does not count while the hazard that caused it is not Armed, and when it is, the member needs a living character within 2 m holding E for 3 s (the Deep Rock rule: no limit on attempts, interrupted by damage). On success: 1 HP, 25 stress, the `Revived` bark. In single-player, `CompanionBrain` gains a `Rescue` action that walks the nearest standing companion to a Mangled member when no enemy is within 12 m. If every active member is Mangled or Downed, it is a party wipe as today (floor reload).

**Co-op** adds the Vermintide loop: a Mangled player sees a third-person spectate of the nearest living teammate with a 30 s timer; if nobody rescues them in that time, their body is moved to the "bound" cell (the next explored room on the critical path beyond the party's furthest member, guaranteed reachable by the generator's path test) with the Captured flag already used by Jailbreak, where any teammate frees them with E. The dead player can type in chat throughout and may trigger the party's "Shout" ping (a new intent that fires an Alert bark and a marker for everyone). Allies in co-op are revived at the body only.

**Enemies** crushed or impaled die at once, count toward collectives' `OnMemberDied`, and feed `EnemyDied` with killer = environment (XP to the nearest member within 10 m, loot none). Horde creatures crushed in a press are the designed way to survive a horde floor.

## C7. Hazard-aware pathing

`GridPathfinder.FindPath` gains an optional `Func<GridPos, float> danger` (0-1) added to the step cost as `danger * weight`. Weights by caller: companions and allies 50 (never enter a Warning or Closing press unless no other route), smart enemies and jailers 12 (they will cross a press during Armed and avoid it during Warning if the detour is under 12 cells), horde and bandit factions 0 (they pour in). `DungeonBuilder.DangerAt` is written by each hazard's state machine (0 Armed, ramps to 1 over Warning, 1 through Crush, 0.3 during Retract). The straight-line shortcut under 5 m also checks `DangerAt` along the segment. `NpcFlight` picks refuge cells with danger 0.

## C8. Co-op synchronisation

The server owns every hazard state machine. On each phase change it broadcasts `HazardPhaseMsg(index, phase, serverTick, durationTicks)`; clients animate the slabs or spikes against their synced server-tick estimate (the existing `Pong.ServerTime` offset), never applying kills. Late joiners get `FloorStateDto.Hazards` (phase, phase start tick, spent count, decals). Periodic beds derive their phase from the server tick, so no message is needed except on join. The server's `ApplyPose` already rejects non-walkable cells; it additionally rejects a pose inside a press during Hold (the body is where the server says it is).

## C9. Reactions and the villain

Hazards feed Part A: `HazardPhase(Warning)` within 8 m fires the Alert "Press!" or "Spikes!" from the nearest speaker; a crush kill of an enemy fires the Laugh rule; a Mangled member fires +25 stress and the new `TauntTrigger.Crushed`; an ally dying to a hazard fires `TauntTrigger.AllyDead`. `TauntBank.Stock` gets lines for both.

---

# Part D. Build order, tests, and the calls I made

## D1. Phases

Each phase is independently shippable and leaves single-player green. Estimated in sessions of the kind that built the earlier phases.

| Phase | Scope | Why this order |
|---|---|---|
| 11. Hazards | C2 interface, Press and Spike Bed rules and nodes, placement, danger map, Mangled and rescue, co-op messages, taunts | Independent of everything else, highest feel payoff, exercises the shared damage path the allies need |
| 12. Personality and allies | A2, A3, A4 ReactionDirector on bank lines only, A5 ally tier with save v9 and `AllySync`, data-driven viewer cast, planner and designer exposure, FlagsSync | Needs no AI installed; makes the existing Foehammer real |
| 13. Voice at scale | Bank-aware `VoiceBudget` priorities, per-body `Persona.Voice` in data files, positional speakers, jaw, laugh and grunt samples, rendered-against-bank status line | Small, all on the Runware path already built; needs the owner's voice picks per character |
| 14. Interaction banks | Bank DTOs and validator, `PromptBuilder.Banks` and the Claude and Runware writers, stock banks, bake and loading-screen windows, `TopicMatcher`, say line, follow-up choices, memory, B4 ownership and server-side matching, replay | Last because it depends on all three above and is the piece whose quality must be judged by reading the banks and by ear |
| 15. Later, if ever | Real-time narration: a local model and local voice through the owner's OllamaWeb service (its streaming sentence-to-Piper pipeline was reviewed and would fit as a sidecar), embedding-based topic matching, mic input via Whisper.net, Rhubarb visemes | Explicitly out of scope now, at the owner's direction |

**One repository.** Every phase is game-side only. Nothing in this plan changes or depends on the OllamaWeb repository.

## D2. Test strategy

Engine-free, xUnit, in the existing test project:
- `HazardRules` timing tables, `PressPlacement` and `SpikePlacement` rules 1-7 as property tests over seeds 1-200 and all layout kinds and objectives, `DangerCost` pathfinder tests, `MangledRules`, `RescueRules`.
- `TraitRules` label derivation, `StressRules` gains and bands with Courage scaling, `AfflictionRules` act-out ordering, `AffinityRules` thresholds, `JoinRules` formula and determinism.
- `ReactionDirector` arbitration matrix, cooldowns, relative casting, bank filtering.
- `PersonaPrompt` builder, `InteractionBank` schema round trip, `BankValidator` (bands filled, lengths, banned words, secrets never leak), `BankStock` coverage for every label pair, `TopicMatcher` against a fixture of 200 typed lines with expected topics (including names, insults, nonsense), `Repeats` behaviour, bank-aware `VoiceBudget` ordering.
- Net: codec tests for `UtteranceMsg`, `SayIntent`, `HazardPhaseMsg`, `AllySync`, `JoinResultMsg`, `FlagsSync`, `ConversationMsg`, `ShoutIntent`.
- Save: v8 to v9 migration test (`HazardsFromFloor`, allies absent, stress absent).

Headless flags: `--test-hazard=press|spikes` (trigger, enemy crushed, member Mangled, rescue, save round trip), `--test-ally` (recruit, follow across stairs, down, revive, leave at -50, save round trip), `--test-reactions` (fake events produce the expected speaker and category), `--test-banks` (writes one ten-topic bank page through the fake planner and validates it), `--test-say` (replayed banks, matcher, effects, deflection, repeat), `net_test.sh` grows a hazard phase check, an ally join and a say round trip.

Verification by ear is a required step in Phases 13 and 14: a short checklist in the README (five typed lines to three NPCs, one Press with a horde) with expected latencies recorded in a table.

## D3. One deliberate break, and what does not break

Floors 3 and up gain hazards, so `ContentHash` for those floors changes and the fixed-seed hash tests (`LayoutKindTests.StandardShape_Unchanged`, `FloorObjectiveTests.NoObjective_GeneratesExactlyAsBefore`) must be re-baselined once, in Phase 11, with the change called out in the commit. The rule that an objective pass leaves `Objective == None` floors identical still holds because hazards are dealt independently of objectives.

**Old saves.** A v8 save has no hazard state. The v9 migrator sets `AdventureDto.HazardsFromFloor = CurrentFloor + 1` on old runs, so floors already visited in that run are rebuilt without hazards (their saved item and enemy positions stay valid) and hazards start on the next new floor. New runs set it to 1.

**The shipped campaign.** The hand-authored bake stores planner answers (rooms, NPCs, hordes, chests by room), not cells; cells are resolved at build time. Because hazard cells join `IsReserved` and `RoomCells.Free`, the existing plans resolve around them. No rebake is needed; `--test-plan` over the shipped floors is the check.

## D4. Calls I made without the owner

Change any of these by editing the document; nothing is built yet.

1. Ten opposed-pair axes with three visible labels, rather than Big Five or a free tag list.
2. Stress 0-100 with four afflictions, a simplification of Darkest Dungeon's seven.
3. Allies are a separate tier (no bag, no XP), max 2 in single-player and 1 in co-op.
4. Join is a formula with the seeded dice kept; Foehammer's 70% coin goes away.
5. Lethality 3 means Downed + Mangled for players and allies, not permanent death. Enemies always die. Bosses are never one-shot.
6. The Press is a three-cell corridor with both side walls closing; not a ceiling, not a single advancing wall.
7. Spike beds come in pressure and periodic variants; periodic ones may sit on the critical path.
8. Voices are Runware, chosen per body by the owner and stored in the data files; one voice per body for the whole campaign.
9. Banks are written by Claude (the owner has credits), or by Runware's text models through the same client; stock banks fill any gap. No local model.
20. Local AI is out of scope (owner, 2026-10-22). OllamaWeb was reviewed as the shape a future real-time path would take and is referenced in Phase 15 only; no code in this plan touches it.
22. Nothing is generated by a model while the game renders. All character lines live in interaction banks written at bake time, on the New Game loading screen, or on the CPU in the background for the next floor only.
23. Typed questions are matched to a closed set of about 28 topics by keyword overlap; a miss gets an in-character deflection. Effects attached to topics come from a rules table, never from generated text.
21. Voice stays on the Runware bake as built (owner's decision 2026-10-22); the voice budget, not the bank size, decides how much is voiced, and unvoiced lines are subtitles.
10. In co-op the server matches typed lines against the banks it already holds and broadcasts the result; no client or server runs a model during play.
11. The typed line is text only and is matched to a closed topic set; the microphone and embedding matching are Phase 15.
12. Story flags get replicated to clients (a fix the live system needs; it also fixes an existing bug).
13. Viewer-made characters become data files; the floor-1 greeter stays as a debug showcase.
14. Fixed-seed layout hash tests are re-baselined once in Phase 11.
15. Co-op gets no AI companions (unchanged); personality in co-op belongs to NPCs, the ally and the villain.
16. Stress and afflictions apply only to AI-driven bodies; a possessed companion keeps its stress but does not act out while possessed.
17. Downed bodies are pushed out of a hazard's footprint at the kill instant, so rescue never requires standing in the trap.
18. Bank writing happens on whichever machine bakes, through the cloud planner; players on any platform receive finished banks with the campaign.
19. Old v8 saves get hazards only on floors they have not visited; the shipped campaign is not rebaked.

## D5. Questions only the owner can answer

These change what gets built. The design proceeds on the stated assumption until answered.

1. **Permanent death.** Assumed no: a crushed player is Mangled and rescued. If you want hardcore (a crushed player is gone for the floor, or for the run), C6 changes and the villain needs more lines.
2. **Ally count.** Assumed two in single-player. Four plus two means six bodies in corridors four metres wide; one may feel better.
3. **Public servers.** Assumed friends only. If strangers will join, "forget between floors" should default on. Nothing a stranger types reaches a model, so the risk is limited to chat text.
4. **Voice for the four companions.** Assumed yes, Runware voices by class persona within the budget. If you would rather keep them silent except for barks, B2's per-speaker players are only for NPCs.
5. (Withdrawn.) The platform question only applied to local AI, which is out of scope.

---

# Appendix E. Research the numbers come from

Numbers and rules above were taken from these sources (verified by the research agents on 2026-10-22 unless marked).

**Approval and traits**
- Baldur's Gate 3 approval: bands, -50 leaves, warnings at -20 and -40, event sizes 1/5/10, per-player values in co-op. https://bg3.wiki/wiki/Approval
- Dragon Age: Inquisition approval 1/5/25 and bands -75..125. https://www.gamerguides.com/dragon-age-inquisition/guide/dialogue-choices/blackwall/approval
- Crusader Kings 3 traits: 16 opposed pairs projected onto 9 hidden AI axes, max 3 per character. https://ck3.paradoxwikis.com/Traits
- Wildermyth hooks and relative casting (highest stat in the party plays the role). https://wildermyth.com/wiki/Stat
- The Sims 4 trait exclusion pairs. https://www.gamepressure.com/thesims4/the-traits-of-the-sim/z268d0
- Personality-conditioned LLM NPCs: short prompts with few trait statements stay stable; Neuroticism drifts most. https://arxiv.org/abs/2508.06149 , https://arxiv.org/abs/2502.11843 , https://etasr.com/index.php/ETASR/article/download/12631/5504

**Stress and fear**
- Darkest Dungeon affliction and virtue tables, act-out rules, bark stress 6, kill -3, disarm -8. https://darkestdungeon.wiki.gg/wiki/Affliction , https://darkestdungeon.wiki.gg/wiki/Virtue , https://darkestdungeon.wiki.gg/wiki/Stress
- X-COM morale formula (panic chance 100 - 2 x morale below 50). https://www.ufopaedia.org/index.php/Morale
- RimWorld break thresholds 35/20/5. https://rimworldwiki.com/wiki/Mental_break
- Left 4 Dead AI Director intensity phases (sustain 3-5 s, relax 30-45 s) and survivor vocalisation goals. https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf , https://cdn.fastly.steamstatic.com/apps/valve/2009/GDC2009_ReplayableCooperativeGameDesign_Left4Dead.pdf

**Banter and reactions**
- Vermintide 2 dialogue system (server-only selection, category interrupt matrix, hear distances, story tick 10 s). https://github.com/Aussiemon/Vermintide-2-Source-Code/blob/master/scripts/entity_system/systems/dialogues/dialogue_system.lua , https://github.com/Aussiemon/Vermintide-2-Source-Code/blob/master/scripts/settings/dialogue_settings.lua
- Darktide category query scores and subtitle gating. https://github.com/Aussiemon/Darktide-Source-Code/blob/master/scripts/settings/dialogue/dialogue_category_config.lua
- Hades dialogue selection (priority, run scope, never repeat until exhausted). https://www.christi-kerr.com/post/how-the-dialogue-system-in-hades-rewards-failure
- Valve Response Rules via Mimir (most-specific rule wins). https://github.com/subtalegames/mimir
- The Last of Us buddy AI (enemies ignore Ellie in stealth, proximity-gated lines). https://www.gamedeveloper.com/design/ellie-s-buddy-ai-in-i-the-last-of-us-i-explained-at-gdc-2014
- Resident Evil 4 remake Ashley (no health bar, downed then revive, "the game should change when she is with you"). https://www.gameinformer.com/2023/02/02/why-capcom-changed-ashley-in-resident-evil-4

**The owner's prior work**
- OllamaWeb at `/mnt/Transfer/5FD1E1415AC80F29/dev/OllamaWeb` (commit ff53629): FastAPI, `ollama` 0.6.3 AsyncClient, `piper-tts` 1.8.0 in-process on one worker thread, `/ws/chat` protocol (JSON `start`, `token`, `sentence`, `done`, `audio_done`, `notice`, `error`; binary frames with an 8-byte little-endian header of generation and seq followed by int16 mono PCM; client `{"type":"stop"}`), `SentenceSplitter` (boundary at `.!?` followed by whitespace or newline, hold under 12 characters, force-break over 200 at a comma), `clean_for_tts`, `SpecialTokenFilter`, `VoiceRegistry`, LAN-only security with Host and Origin guards. Design: `docs/superpowers/specs/2026-10-15-ollamaweb-design.md`; plan: `docs/superpowers/plans/2026-10-15-ollamaweb.md`; notes: `CLAUDE.md`.
- The sibling Tkinter app at `../Ollama` (bundled `piper/` binary and voices, `make_release.sh` zip plus `install.sh`): the model for handing OllamaWeb to friends.

**Local LLM and TTS**
- Ollama chat API, structured outputs, think, keep_alive, options. https://docs.ollama.com/api/chat , https://ollama.com/blog/structured-outputs , https://docs.ollama.com/faq
- Qwen3.5 and Gemma 4 sizes on Ollama. https://ollama.com/library/qwen3.5 , https://ollama.com/library/gemma4
- OllamaSharp 5.x. https://github.com/awaescher/OllamaSharp
- Piper (OHF-Voice, GPL-3.0): CLI with `--output-raw`, Python API, HTTP server buffers whole utterances, libpiper C API. https://github.com/OHF-Voice/piper1-gpl , https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/CLI.md
- Piper voice catalogue (libritts_r 2306 speakers, vctk 109). https://huggingface.co/rhasspy/piper-voices/raw/main/voices.json , https://rhasspy.github.io/piper-samples/
- Piper real-time factor 0.15-0.2 on laptop CPU. https://arxiv.org/pdf/2512.08006 , https://mustafa.net/2026/08/31/piper-review-after-6-months-running-local-tts-in-production/
- Alternatives with emotion tags: Chatterbox (MIT, CPU), Orpheus, Kokoro and KokoroSharp. https://github.com/resemble-ai/chatterbox , https://github.com/canopyai/Orpheus-TTS , https://github.com/Lyrcaxis/KokoroSharp
- Mantella defaults (max 4 sentences, TTS after 3 words, 250 max tokens, summary memory, Piper default, fallback line). https://github.com/art-from-the-machine/Mantella , https://art-from-the-machine.github.io/Mantella/pages/real_time_npcs.html
- Whisper.net, godot-whisper, Moonshine for Phase 15. https://github.com/ggml-org/whisper.cpp , https://github.com/appsinacup/godot-whisper , https://github.com/moonshine-ai/moonshine
- Godot mic capture. https://docs.godotengine.org/en/stable/classes/class_audioeffectcapture.html

**Co-op AI NPCs and safety**
- Bounded Autonomy (priority arbiter, talk lock, reply-chain decay, bundle grounding). https://arxiv.org/html/2604.04703v2
- Convai multiplayer guidance (server owns the session, one active speaker). https://docs.convai.com/api-docs/plugins-and-integrations/unreal-engine/guides/enable-multiplayer-support
- Roblox text generation guidance (private outputs, 20-message history, output guard). https://devforum.roblox.com/t/text-generation-api-beta-expanding-access/3952348
- Fortnite Vader moderation failure and fix. https://decrypt.co/320500/fortnite-ai-powered-darth-vader-saying-slurs
- Convai guardrail stack. https://convai.com/blog/safety-guardrails-with-convai-ai-characters
- "Architecture, not prompts": LLM writes words, rules decide outcomes. https://veriprajna.com/blog/game-ai-npcs-architecture-not-better-prompts
- Talking Villagers (server-side Ollama, silent when slow). https://www.curseforge.com/minecraft/mc-mods/talkingvillagers
- BG3 co-op dialogue ownership and listen-in. https://www.pcgamesn.com/baldurs-gate-3/multiplayer

**Traps**
- Telegraphing and the Rule of the Dead Adventurer. https://blogofholding.com/?p=8117 , https://kateplays.substack.com/p/telegraphing-danger
- Spelunky fairness, crush trap behaviour, traps stamped into obstacle slots off the solution path. https://gamedeveloper.com/design/fairness-discovery-spelunky , https://www.tinysubversions.com/2009/09/spelunkys-procedural-space/index.html
- Hades spike pits (short delay, high damage to enemies). https://hades.wiki.fextralife.com/Walkthrough
- Dead Cells hazards (30% HP cap, pressure spikes flash before firing, traps hit enemies). https://deadcells.wiki.gg/wiki/Hazards
- Darkest Dungeon trap numbers (25-30% HP, +15 stress, disarm = resist + 40%). https://darkestdungeon.wiki.gg/wiki/Trap
- Sen's Fortress traps usable against enemies, the paused elevator. https://darksouls.wiki.fextralife.com/Sen's+Fortress
- Zelda compactors stopped by a block; Hexen crushers stop when blocked. https://zeldawiki.wiki/wiki/Compactor , https://doomwiki.org/wiki/Crusher
- Influence maps for hazard-aware pathing (Game AI Pro 2, ch. 30). https://www.gameaipro.com/GameAIPro2/GameAIPro2_Chapter30_Modular_Tactical_Influence_Maps.pdf
- Server-authoritative timed hazards; do not drive lethal timers from client determinism. https://gafferongames.com/post/deterministic_lockstep/ , https://foxssake.github.io/netfox/latest/netfox/guides/network-time/

**Death and rescue in co-op**
- Vermintide 2 bound respawn ahead after 30 s. https://steamcommunity.com/app/552500/discussions/0/1728701877522157276/
- Deep Rock Galactic revive (6 s hold, unlimited attempts, interrupted by damage). https://deeprockgalactic.wiki.gg/wiki/Health
- Helldivers 2 reinforcement budget. https://helldivers.wiki.gg/wiki/Reinforcement
- Risk of Rain 2 community replacing spectate with controlling an ally. https://thunderstore.io/c/riskofrain2/p/Evaisa/FallenFriends/
