# The Gullet of Gorm: hero and companion line changes

Status: written 2026-11-12 for the story rewire (task 8). Covers `Barks` and `Banter` in `data/adventures/builtin/campaign.json` and the stock lines in `src/Adventure/Core/Voice/`.

Owner's direction: keep the heroes' lines and dances; change only what refers to the removed plot or contradicts the new one; add a little banter where it helps the story. Every line below was read, not only the word-search hits. Everything not listed here is unchanged, byte for byte, with its recording. Every changed or new line has `VoiceId: null` and waits for the recording step, in its speaker's existing voice.

## Bark changes

Each keeps its speaker, its trigger and its delivery.

### Brand (warrior, man)

| Trigger | Old | New | Why |
|---|---|---|---|
| Kill | That's one less mouth in the throat. | One less between us and the drains. | The mines as the god's throat (old plot). |
| LevelUp | Nineteen years of fools taught me that trick. | Thirty years of sergeants taught me that trick. | "Nineteen years" (banned). His own thirty years in. |
| BossSeen | Nobody said god. They said plumbing. | Drains, they said. Nobody mentioned that. | "Plumbing" (banned); same joke on the job's fine print. |
| Travel | At this rate the Verger dies of old age before we find him. | At this rate the orc dies of old age before we reach him. | Nobody is hunting the Verger now; the orc waits at the bottom. |

### Sable (rogue, woman)

| Trigger | Old | New | Why |
|---|---|---|---|
| Loot | Gold teeth. The god won't miss a molar. | Silver. Call it an advance on the advance. | Gold teeth, the god's body (old plot). The job is paid with an advance. |
| Downed | Don't let the verger write my name neat. | Don't leave me down here. I'm worth far more alive. | Names written in advance, the Verger keeping the tally (old plot). |

### Wren (ranger, man)

| Trigger | Old | New | Why |
|---|---|---|---|
| LevelUp | I'm learning His corridors. That frightens me. | I'm learning these tunnels. That frightens me. | The mines as the god's body, capital "His". |
| BossSeen | It's breathing in time with the walls. | Ground's shaking. Not from that one. | Breathing walls, the god's body; the shaking is the starving god stirring. |
| Revived | Alive, and still owed nineteen years of wages. | Alive, and still owed every penny. | "Nineteen years" (banned). |

### Ilex (mage, man)

| Trigger | Old | New | Why |
|---|---|---|---|
| Kill | Burned clean. His throat may thank me. | Burned clean. The drains may thank me. | The god's throat, capital "His". (The stock twin "Its throat", about the enemy, stays.) |
| Kill | That one had a soul cast in a mould. Pity. | That one had a soul once. Pity. | Souls cast in moulds is the old plot; nobody is turned in a mould now. Also in `KillBarks.cs`. |
| Loot | Nineteen years old and still legible. Unlucky. | Council paper, and still legible. The alderman will be appalled. | "Nineteen" (banned). Crane wants his papers unread. |
| LevelUp | Power, acquired in a stomach. Note the irony. | Enlightenment, acquired in a drain. Note the irony. | The mines as the god's stomach. |
| BossSeen | An organ with opinions. Keep it off me. | A specimen with opinions. Keep it off me. | Bosses as the god's organs (old plot). |
| BossSeen | This was never plumbing. It never once was. | A matter of drains, we were told. Observe the matter of drains. | "Plumbing" (banned). |
| Downed | He dreams. I can hear what He dreams of. Us. | He dreams. I can hear it from here. Thirsty, mostly. | Capital "He", and a god who dreams of eating them; Gorm is good and thirsty. |
| Revived | I saw the memorial stone. My name was neat. | Upright again. The far side was poorly lit and smelt of orc. | "Memorial stone" (banned), names carved. |

### Tamsin Rook (`fighter_tamsin`, woman)

| Trigger | Old | New | Why |
|---|---|---|---|
| Kill | Ha! That one had teeth. Gold ones? No. Shame. | Ha! Went down like a rotten prop. That's a share, that is. | Gold teeth (old plot: she mined the god's teeth). |
| Kill | Mind the wet, it'll slip you. Not him. He's done slipping. | Hit him like a hard seam. He cracked first. | Her partner Jory went under in the water; she never jokes about it, and this read as a joke about him. |

Both also changed in `KillBarks.cs` (same text).

### Sister Ione (`fighter_ione`, woman)

| Trigger | Old | New | Why |
|---|---|---|---|
| Kill | He would have understood. The Verger. He would. | The Verger taught me the prayers for the dead. I say them anyway. | A defence of the Verger; she is ashamed of him (her sheet). |

The stock twin in `KillBarks.cs`, "He would have understood. He would. He must.", becomes "I was taught the prayers for the dead. I say them anyway." (stock lines may not name the Verger: `StockBankTests`).

### Foehammer and Oskar Vell

No changes. Foehammer's "Another for the stone." is a tally. Oskar's "That's what the old man's tools are for." fits Ulf's "tools".

## Looked at and kept

These came up in the search or the read-through and stay as they are, because they still make sense in the new story:

- Brand: "Call it expenses. The contract said expenses." and "Walking for a living. Didn't see that in the contract." (a plain job, honestly paid); "Consecrated ground. It'll soak that up." (the Skin is church ground); "Rest easy, whatever you were." (fits the turned); "Floor's warm. That's the worst of it."; "Not lying down. Not for him."
- Sable: "Another trick for a town that won't pay." (a mercenary's grumble, not a trap); "He had nothing. Typical of this parish."; "Finders, keepers, swallowers, weepers."; "If you'd paid up, I'd have danced for you alive."
- Wren: "The floor's beating. I'd rather it didn't." (the engine's thump in the pump works); "Nothing down here dies surprised."
- Ilex: "Ink, and the hand shook. They knew." (a found note); "A reliquary. Nobody tell the priest downstairs." (the Verger is downstairs); "Not lying down. That is his word, not mine."; "The council pays by the floor, not by the fight." (his sheet: fine as it is); "Burned clean. Its throat may thank me." (the enemy's throat); "Fire answers most theological questions."
- Banter: all 61 existing exchanges stay. "A happy one, until the drain." (Gerald's widow) fits the new story better than the old; "We are contractually obliged to." is a plain job.

## Stock code lines

`src/Adventure/Core/Voice/KillBarks.cs`, changed in place (four strings): Ilex's "soul cast in a mould", Tamsin's two, Ione's. Ilex's had to change in the code too: his campaign set holds fewer than 20 kill lines, so `KillBarks.TopUp` would otherwise add the old stock line back, unvoiced, at load. No test or voice-id table pins any of the four old texts.

Searched and left alone: `DanceBarks`, `ReactiveBarks`, `CheerBarks`, `TravelBarks` (its Brand line already reads "whoever's down there"), `IdleBarks` and `BanterBank` hold nothing of the old plot. `Epitaphs` is death-card text for every campaign, not a hero line; "The Press works. Somebody should tell the next four." and "He has started using pencil." (pinned by `EpitaphTests` and `SaveRunFieldsTests`) read as generic dark jokes and stay.

## Dance swaps

Twenty dance lines per hero, as before. Six swaps in all (Brand 1, Sable 1, Wren 2, Ilex 2), each where the old line was the weakest or repeated another:

| Hero | Old | New |
|---|---|---|
| Brand | Call that a funeral march. A quick one. | The council pays for the sword work. The hips are on the house. |
| Sable | Dead men tell no tales, but this one's getting a dance. | First dance is free, darlings. The encore goes on the alderman's bill. |
| Wren | Hunt's over. Hips, you have the floor. (repeated "The hunt's done. The hips take it from here.") | Don't tell the trees. |
| Wren | Try the hip rock in a treeline. Builds balance. (too many words for Wren) | Four thousand bricks. One dance. Fair. |
| Ilex | Cinders settle; a scholar sways. (cinders are now turned townsfolk) | Footnote seventeen: the author danced. Reviewers were unkind. |
| Ilex | Fire, then footwork. Both precise. (repeated "Spell, then shimmy" and "Thaumaturgy, then choreography") | Gerald, avert your eyes. This is not for puddles. |

The swaps are in the campaign only; the stock `DanceBarks` lines serve other campaigns and stay. The campaign sets hold twenty dance lines each, so the stock top-up never adds the old ones back.

## New banter

Twelve exchanges, all heroes (banter is spoken only by the party's own members, never by companions; see below), every line `weary` like the existing ones, all `OncePerRun`. Read in order they take the party from "it's a job" to staying when the pay no longer covers it.

| Id | MinFloor | Tier | Flag | Lines |
|---|---|---|---|---|
| `bs_extra` | 1 | | | Sable: Remind me. What's the job? / Brand: Clear the drains. Water runs. Town pays. / Sable: And whatever's living in the drains? / Brand: Costs extra. |
| `wi_monograph` | 1 | | | Ilex: A god asleep under a market town. I may get a monograph out of this. / Wren: Wrong. / Ilex: The monograph? / Wren: The place. |
| `bw_theory` | 1 | | | Brand: Clear the drains and the wells come sweet again. That's the theory. / Wren: And the pay? / Brand: That's the practice. |
| `sw_still` | 6 | | | Wren: Water's standing. Going sour. / Sable: Lovely. The pay's the same, the water's worse, and I'm still here. Why am I still here? / Wren: You like us. / Sable: I like your purses. Different thing entirely. |
| `bi_counting` | 6 | | | Ilex: The grey ones in the packs. The freed wardens said the finished ones join them. / Brand: I heard. / Ilex: So, statistically, some of what we have killed used to clear these drains. / Brand: I'm counting them. Somebody should. |
| `bs_runs` | 6 | | `grisel_forgiven` | Sable: We forgave that warden awfully quick, Brand. / Brand: Everybody runs once. Only counts if you stay gone. / Sable: Is that soldier's wisdom? / Brand: Corporal's. Sergeants don't run. They send corporals. |
| `bw_wheat` | 11 | | | Wren: Fields up top. They need this water. / Brand: Since when do you care about a town? / Wren: Don't. Care about wheat. / Brand: Fine. Shields up for the wheat. |
| `bi_mould` | 13 | 2 | | Ilex: That mould by the stair is the orc himself, poured larger. It wants smashing. / Brand: Foehammer'd want first swing. / Ilex: And Ulf's journeyman saw the whole drawings, and said nothing. / Brand: The orc threatened the old man. You keep quiet for your own. |
| `wi_bellows` | 15 | 2 | | Wren: Big engine next. / Ilex: The Bellows. Oskar would say strike the joints, where the old iron is thinnest. I concur, which he would find tiresome. / Wren: Ladles? / Ilex: Whatever drops from them, kill before it cools. Then the third drain. |
| `si_footnote` | 16 | | | Ilex: I came down here for a footnote. / Sable: And now? / Ilex: A god whose sleep holds a door shut is not a footnote. It is, at minimum, a chapter. Walk faster. |
| `bs_sums` | 16 | | | Sable: I've done the sums. This town can't pay for the last floor. / Brand: So go home. / Sable: I like the town. Tell anyone and I'll have your boots. / Brand: Wouldn't fit you. |
| `bw_deacon` | 16 | | `told_tobit` | Brand: We told that deacon his god was starving. / Wren: We did. / Brand: Lad came down after us anyway. Wanted proof of his own master. / Wren: Good. |

What each one may assume (the who-knows table), and why it stays true later:

- **Act 1 (MinFloor 1).** The tavern (floor 0) plays the floor-1 bank, so these assume only the hire: the drains, the sour wells, the pay, a god under the town. No orc, no prisoners.
- **`sw_still`, `bi_counting` (6).** The water standing (from 6, Maud's "He is thirsty"), the freed wardens on floor 2 ("the ones they finish go grey and join the packs"). No council and no money (clue 2 is floor 9).
- **`bs_runs` (6, `grisel_forgiven`).** The flag is set on 9, so it plays from 9. It talks about the forgiving, not the crew, so it stays true whether the crew came out on 12 or not.
- **`bw_wheat` (11).** Only the water and the town above.
- **`bi_mould` (13, tier 2: floors 13-15).** Clue 3: the orc's own mould by the stair (13). Carries two beats the floor cannot: Foehammer wanting the Herald's mould smashed, and the journeyman having kept quiet because the orc threatened Ulf (Ulf's own guess on 13, "it was the orc made him").
- **`wi_bellows` (15, tier 2: floor 15 only).** Carries Oskar's knowledge of the Bellows' weak points: the joints, and the castings dropping from its ladles (the floor's own boss narration).
- **Act 4 (16).** The dream as a door (the tier 4 entry line on 16). `bs_sums` says the town cannot pay, not why the wardens were sacked: no money motive before 18. `bw_deacon` (`told_tobit`) looks back to floor 14, where the told deacon followed the party down and wanted proof; it does not assume the letter, which he brings later on 16.

**Why three exchanges are not on MinFloor 11.** The brief asks for three per act at MinFloor 1, 6, 11 and 16, and also for the mould and the journeyman "from floor 13 on" and the Bellows "from floor 15". An exchange on MinFloor 11 can play on 11 and 12, before the moulds are found, so those two sit on MinFloor 13 and 15 inside act 3, held to act 3 by `Tier` 2.

**Companions.** `BanterPicker.Eligible` takes the speakers from `party.Active` (the heroes' own bodies, by name); recruited companions are NPCs, not party members, so they never speak in banter. The three carried beats are given to heroes as remarks about Foehammer and Oskar, worded as what those two want or would say, so they stand whether or not either is with the party.
