# The Gullet of Gorm: campaign narrative

Date: 2026-10-22. Status: narrative bible for review; nothing authored into `campaign.json` yet.
Written for: the owner, and whoever authors this into the campaign format (by hand through the replay bake, or as the outline Claude expands).

Companion documents: the systems design at `2026-10-22-living-npcs-and-lethal-traps-design.md` (traits, allies, banks, Press, Spike Beds), and the writers' room material in the session scratchpad (four pitches, a puzzle catalogue, a decision memo). This bible is the chosen spine, "The Gullet of Gorm", with two fixes and three transplants from the other pitches, as the decision memo recommended.

---

## 1. In one breath

**Logline.** Hired by a small town to fix "a problem with the plumbing", four adventurers walk twenty floors down the throat of a sleeping god and discover they are the lullaby.

**Why this story.** It is nothing like the campaign it replaces: no clerk, no ledger, no drowning, no debt. It is awe and tenderness at scale, with the humour of a town that treats a deity as a drainage issue. Every tier is a station of a pilgrimage the party does not know it is on. The puzzles are the god's body; the quests are the people who live in it; the end boss is the man who has kept the god asleep for three hundred years, and the player decides whether to wake it.

**Tone rule.** Dark with humour, as the owner asked. The whimsy belongs to two characters, Alderman Crane and Old Ulf. Everything about Grisel, the moulds, and the Verger's euphemism for dying ("lying down") stays cold. The Press kills someone on floor 3 before anyone has made a joke about it.

---

## 2. The world

**Dunmarrow** is a decent market town built, as everyone knows and nobody says, on the chest of **Gorm**, a god who fell out of the sky before calendars and never got up. The town's wells come from Him, its fields are warm because of Him, and every soul born in Dunmarrow is cast in a forge in His heart. Under the lychgate is the **Gullet**: twenty floors down His throat.

**The Four Stations** are the four tiers, and the tier entrance narration names them:

| Tier | Floors | Station | What it is | Themes to prefer |
|---|---|---|---|---|
| Crypt | 1-5 | **The Skin** | the church built on Him: lychgate, nave, chantry, ossuary; the dead of Dunmarrow laid on His chest | crypt_brick, crypt_mossy, crypt_ossuary, crypt_undercroft |
| Caves | 6-10 | **The Gut** | the swallow, the crop, the craw; wet, acid-green, full of what He ate | caves_rock, caves_lichen, caves_boulder, caves_sandstone |
| Forge | 11-15 | **The Heart** | the Forge that casts souls; bellows that are His breath; the heat of a living thing | forge_iron, forge_kiln, forge_plate, forge_rust |
| Abyss | 16-20 | **The Dream** | marble because He dreams in marble; the Choir that keeps Him under; the Mouth | abyss_palace, abyss_slate, abyss_stone, abyss_tiles |

**Every nineteen years** the wells go sour, and the council sends four "inspectors" down. None come back. This is this year's four. The contract says plumbing. The memorial stone by the lychgate already has their names on it.

**The enemy factions**, one per tier, renamed:

| Tier | Faction name | Creatures | What they are |
|---|---|---|---|
| Crypt | The Vestry | ghoul, skeleton_archer, bone_priest | Dunmarrow's dead, kept in service by the Verger |
| Caves | The Swallowed | cave_spider, rock_thrower, cave_shaman | what Gorm ate before He slept, still alive in Him |
| Forge | The Bellows-Men | forge_golem, ember_hound, forge_priest | the Forge's own tenders, cast without souls |
| Abyss | The Choir | void_stalker, abyss_archer, void_caller | the Verger's singers; the lullaby made flesh |

Bandits appear only in camps (floors 8 and 11): "the Oddfellows", a crew who came down for the gold teeth and think they are the only people here.

---

## 3. The villain: Absalom Quill, the Verger

**Who.** Three centuries ago the church's verger went down the Gullet to tend the god and found the Forge making souls. An awake god does not need a verger. So he keeps Gorm dreaming, and the dream is fed on adventurers: four every nineteen years, by contract with the council, who think they are buying clean water. He calls the party "the congregation", calls dying "lying down", calls the god "Himself".

**Voice.** Church-warden passive-aggression in liturgical cadence; the sermon of a man whose congregation left and who blames the pews. Never raised. He is the only person in the story who is never funny on purpose. Persona: `VoiceStyle` "a dry, precise church verger of great age, offended by noise and blood, liturgical cadence, never raised"; `DefaultDelivery` Calm; Mocking on taunts.

**What he is on floor 20.** He has become Gorm's gag. The `abyss_herald` is renamed **The Verger**; his Volley is the Litany. Killing him un-gags a god.

**What he wants from the party.** To lie down. He would prefer it quietly. He will lie about the levers to arrange it.

---

## 4. The cast

Five recurring characters plus the four recruitable fighters. Bodies are the owner's archetypes from the character list. A name pass was done against the other pitches and the party roster (the ranger companion is called Wren, so no cast member is).

| Name | Role | Secret | Body | Labels (axes) | Floors |
|---|---|---|---|---|---|
| **Maud Sorrel** | Sleepwalking guide, always one floor further down than she should be, always dry, always calm | She is Gorm's dream speaking through a woman who died in the Gullet thirty-eight years ago | old woman | Calm (Temper low), Curious | 1, 6, 11, 16, 18, 19 |
| **Alderman Hesketh Crane** | The employer, who follows the party down "to check the invoice" | He carved the party's names on the memorial before they left; he has done it four times | merchant | Proud, Craven | 1, 4, 9, 17 |
| **Deacon Tobit Hale** | The Verger's junior, sent up to greet them; wants to believe | He is being groomed as the next gag; Quill means to stop being human | priest | Pious, Agreeable | 3, 14, 16 |
| **Grisel Thorn** | Sole survivor of the last four, nineteen years in the Gut | She bought her life by leading her party to the jailers, and still does | prisoner | Cynical, Contrary | 7, 9, 12 |
| **Old Ulf** | Keeper of the Forge, thinks he casts tools | Knows exactly what he casts; proud of every baby | old man (not Brannock, who stays the floor-1 debug greeter) | Jovial, Proud | 11, 13, 15 |

**The fighters** (one per tier, recruitable, with their own traits from the systems design):

- **Foehammer** (Crypt, floor 1): the mason who carved the memorial stone; recognises the party's names on it before they do. Joins out of shame. Brave, Humble.
- **Tamsin Rook** (Caves, floor 6): mining Gorm's gold teeth in the Swallow; thinks the god is a vein. Jovial, Greedy. Her greed is tested at the floor-8 Oddfellows camp.
- **Oskar Vell** (Forge, floor 11): Ulf's apprentice, who left when he understood the moulds and is too proud to say so. Patient, Proud.
- **Sister Ione** (Abyss, floor 16): came down as a pilgrim; believes the Verger is a saint. Pious, Craven. Her fear is tested by the floor-17 gas.

---

## 5. The mystery

**Question.** Why does Dunmarrow send four people down the Gullet every nineteen years?

**Truth.** The council's contract with the Verger: four souls a generation keep Gorm dreaming; the dream keeps the Forge casting Dunmarrow's souls; the party's names were on the stone before they left. Gorm is not ill. Gorm is trying to wake up, and the sour wells are Him stirring.

| Clue | Floor | Item | Who gives it |
|---|---|---|---|
| 1 | 4 | The sexton's ledger of "plumbing inspections": every nineteen years since 1612, four names each, no return dates | Crane, Investigate, flustered |
| 2 | 9 | The last four's contract in Grisel's camp: same wording as the party's, same alderman's seal | Crane, Investigate; Grisel watches him read it |
| 3 | 13 | Casting moulds in the Mould Room shaped like Dunmarrow faces, including Crane's grandchildren | Ulf, Investigate (or handed over in dialogue if the Heartbeat Doors are cut, see section 9) |
| 4 | 16 | The Verger's letter to the council: "Send the usual. Something fierce this time; He stirs." | Tobit if `told_tobit`, else Maud |

**Payoff, floor 18.** Maud stops sleepwalking, sits down on the floor of the Dreaming Hall, and speaks as Gorm.

---

## 6. The three choices

| Floor | Who | Choice | Flag | Consequence |
|---|---|---|---|---|
| 3 | Tobit | Tell him the truth: you are plumbers, not pilgrims | `told_tobit` | 14: with the flag Tobit defects, gives the parity of the Quench and a Recover quest (his psalter); without it his appearance is a Betrayal and the Choir pours from the confessional. 16: clue 4 comes from Tobit with the flag, from Maud without |
| 9 | Grisel | Forgive her for the last four | `grisel_forgiven` | 12: forgiven, she is an Escort you walk out past the jailers; unforgiven, she is the head jailer, `NameOverride` "Grisel Thorn", and you kill her |
| 18 | Maud, as Gorm | Finish the pilgrimage (wake Him) or burn the contract (let Him dream, keep Dunmarrow's souls) | `wake_gorm` | 19: the rite is the Waking or the Unbinding. 20: the Verger's half-health and death lines change, and so does the victory narration |

A fourth consequence comes from the systems, not a choice: if **Tamsin** leaves the party at the floor-8 camp (Greedy, low affinity, the Oddfellows offer a share), the fighter-left flag `left:tamsin` puts her back on floor 8's camp as "Tamsin Rook, Oddfellow" the next time the floor is visited, and the Verger notes it. If **Ione** bolts at the floor-17 gas (`left:ione`), the floor-18 narration says the lantern went with her.

---

## 7. The twenty floors

Star = voiced story floor. Degree = one-quest cameo, subtitled. Objectives sit on 2/4, 7/9, 12/14, 17/19: two per tier, none on floor 1 or a boss floor, none adjacent, every kind twice, all layout-compatible. No layout repeats consecutively. Presses only on floors 3, 18 and 19 (never on a labyrinth). Boss floors are ring, burning, standard, ring.

| Floor | Title | Theme | Layout | Objective | What happens |
|---|---|---|---|---|---|
| 1 ★ | The Lychgate | crypt_brick | standard | - | Crane hands over the contract and one sincere line. Maud wanders past the gate going down. Foehammer at the memorial stone, reading the names. |
| 2 | The Vestibule | crypt_mossy | labyrinth | Jailbreak | The Vestry holds Dunmarrow's missing sexton and gravediggers; freed, they babble about "the Stations". |
| 3 ★ | The Nave | crypt_ossuary | standard | - | Tobit greets. Decide `told_tobit`. The first Press guards the bone-chapel chest (puzzle 1). The Verger's first taunt. |
| 4 ° | The Chantry | crypt_undercroft | mine | Toxic | Gas from the start cellar; Crane's Investigate: the ledger, clue 1. |
| 5 | Canon Ludd | crypt_brick | ring | boss | `crypt_lord` renamed Canon Ludd. First Station, the Skin. The Gallery arena (section 9). |
| 6 ° | The Swallow | caves_rock | flooded | - | Wading in the god's spit. Tamsin Rook, mining gold teeth. Maud ahead, dry. |
| 7 ★ | The Crop | caves_lichen | mine | Ritual | Grisel's camp. Her rite, the Unswearing, against the Swallowed; she is the ritualist. |
| 8 | The Craw | caves_boulder | labyrinth | - | Quiet and lethal: tremors, periodic Spike Beds on the straights, the Oddfellows' camp. Tamsin's greed test. |
| 9 ★ | The Cistern | caves_sandstone | standard | ShiftingVault | The Lullaby Levers (puzzle 2). Crane's Investigate: the contract, clue 2. Grisel's Decide. |
| 10 | Mother Nit | caves_lichen | burning | boss | `broodmother` renamed, Gorm's louse-queen. Second Station, the Gut. The Hatchery arena. |
| 11 ° | The Hearth | forge_iron | standard | - | Old Ulf and Oskar Vell. KillTarget "Cinders", the ember hound eating Ulf's coal. Maud asleep at the anvil. Second Oddfellows camp. |
| 12 | The Casting Hall | forge_kiln | ring | Jailbreak | The prisoners are the uncast. Grisel Escort or Grisel jailer by flag. |
| 13 ° | The Mould Room | forge_plate | labyrinth | - | Ulf's Investigate: the moulds, clue 3. The Heartbeat Doors (puzzle 3). Periodic Spike Bed on the approach. |
| 14 ★ | The Quench | forge_rust | flooded | ShiftingVault | Flooded levers under ledge archers. Tobit defects or betrays by `told_tobit`. The Verger lies on WrongLever. |
| 15 | The Bellows | forge_kiln | standard | boss | `iron_colossus` renamed The Bellows, Gorm's heart-valve. Ulf refuses to help, then does. Third Station, the Heart. The Gauntlet arena. |
| 16 ° | The Dream Door | abyss_palace | ring | - | Sister Ione recruitable. Clue 4 from Tobit or Maud by flag. The Choir begins. |
| 17 ★ | The Windpipe | abyss_slate | mine | Toxic | Gas rises while Crane admits everything and betrays. Ione's fear test. |
| 18 ★ | The Dreaming Hall | abyss_stone | ring | - | Payoff: Maud speaks as Gorm. Decide `wake_gorm`. Feeding the Press guards the one corridor into the rotunda (puzzle 4). |
| 19 ★ | The Last Station | abyss_tiles | standard | Ritual | Maud's rite, the Waking or the Unbinding, down three galleries (puzzle 5). The Verger speaks only through the ritual taunts. |
| 20 ★ | The Mouth | abyss_palace | ring | boss | The Verger, `abyss_herald`. Fourth Station. The Colonnade arena. |

Eight starred floors; four cameos carry one quest each with subtitles.

---

## 8. Floor by floor, the story floors

**1. The Lychgate.** Crane in his chain of office at the top of the stair, Maud already two steps down it. Crane: "Nobody said god. I said plumbing. Legally those are very different words." Then, the one sincere line before the jokes: "There's bread in the first cellar. I put it there myself." Foehammer stands at the memorial stone with his chisel; his join line is that he carved the fourth name this morning and did not read it until now. The Vestry's ghouls wear last generation's coats.

**3. The Nave.** Tobit Hale, young, lit by a candle he does not need, bowing: "Welcome to the First Station. The Verger sends his regrets, and asks that you not bleed on the flagstones; they were only just done." The Decide: tell him the truth about the contract or let him believe. Beyond the chancel, the Press: grooves in the walls, bones in the middle cell, the bone-chapel chest behind it. The Curious character warns. Someone, player or ghoul, is crushed here, and the Verger's first taunt follows: "Oh, good. The Press works. Nobody tells me anything down here."

**7. The Crop.** Grisel Thorn's camp of nineteen years: a cave-spider shell for a roof, a banner of the last four's company sewn from their coats. She is the ritualist; the rite is the Unswearing, undoing the oath she swore to the jailers. "Nineteen years. The spiders are fine once you stop expecting them to be friends." She does not tell you what she did. The player learns it from Crane's contract on 9.

**9. The Cistern.** The Lullaby Levers (puzzle 2). Crane arrives, wheezing, to "check progress", and finds the last four's contract in Grisel's old pack: his own seal. Grisel's Decide: forgive her. If the player asks her through the interaction bank about traps at high enough affinity, she gives the lever order for free (a stolen idea from the heist pitch).

**14. The Quench.** Flooded levers, archers on ledges. The Verger lies on every WrongLever: "Correct. Entirely correct. Himself approves." With `told_tobit`, Tobit is here as a defector with the parity in his psalter and a Recover quest to fetch it from the ledge; without it, his appearance is a Betrayal and the Choir pours from the confessional where he stood.

**17. The Windpipe.** Gas rising through the galleries. Crane, finally, at the junction: "I signed it four times. Each time I thought, this is the last. Each time the wells cleared." His Betrayal: Choir stalkers where he stood, `NameOverride` "Crane's Retainers". Ione's fear test fires at GasThickens.

**18. The Dreaming Hall.** The rotunda. Feeding the Press on the one corridor in (puzzle 4). Inside, Maud sits down for the first time and the narration changes register: Gorm, through her, slow, kind, apologetic for the size of Himself. The four clues are laid out by whoever carries them. The Decide: wake Him, or burn the contract and let the dream and the Forge go on. Gorm does not argue either way. He says thank you for coming.

**19. The Last Station.** Maud's rite at the junction of three galleries (puzzle 5). The Verger is present only as the ritual taunts: RitualStart "You will not finish the verse. Nobody finishes the verse.", WaveBreak, RitualistDown, RitualDone. If `wake_gorm` the rite is the Waking and the floor shakes with every verse; otherwise it is the Unbinding and the Choir stops singing one gallery at a time.

**20. The Mouth.** Marble pillars are teeth, the floor is tongue, braziers for a uvula (Crane called it on floor 1). The Verger waits by the throat.

- Start: "Fourth Station. You have come a long way to lie down. Kneel; it is the same thing, only sooner."
- Half health, `wake_gorm`: "He is turning over. Do you know what a town looks like from underneath? You will."
- Half health, otherwise: "You would keep Him asleep and kill me? Then one of you is the new Verger. I don't make the rules; I only wrote them down."
- Death, `wake_gorm`: "...He's saying something. He's saying thank you. Three hundred years and that's what it..."
- Death, otherwise: "Hush. Hush. He's still... hush."

**Victory narration.** `wake_gorm`: far above, Dunmarrow lurches; the wells run clear and cold; the endless floors are Gorm's waking thoughts, and Maud walks beside you in them. Otherwise: the dream goes on, the Forge casts, and so do you; the endless floors are the dream deepening, and Tobit's candle is seen ahead on the first of them.

---

## 9. Puzzles and arenas, with what each costs

All five puzzles use the mechanisms in the puzzle catalogue. Ratings: S data only, M about a day, L a phase. Every one leaves the floor finishable from any state, and every one is solvable by one human plus three companions on Hold, and by one human alone in co-op.

| # | Floor | Name | Mechanism | Solve | Fail and recovery | Solo vs four | Cost |
|---|---|---|---|---|---|---|---|
| 1 | 3 | **The Bone-Chapel Press** | The Press with its plate on the near side, a barrel in a Perception alcove, two ghouls patrolling beyond | Jam it with the barrel and walk, or stand on the plate, let the ghouls come, step off | Crushed: Mangled, body shoved clear, rescued by E; the Press re-arms, never seals | Solo: companions Hold short of the plate, the lead crosses in the 3.6 s window. Four: one on the plate calling, one baiting, two looting | S once the Press ships |
| 2 | 9 | **The Lullaby Levers** | ShiftingVault parity plus a Carillon sequence: four levers in four cistern rooms, a hymn verse carved over each ("Pull the second while the first still sleeps") | Read all four, pull in verse order; gates open to the stairs | Wrong pull resets and reroutes a Swallowed patrol; the Verger lies; the third wrong opens a portal behind you | Solo: Hold a companion in each read room as a marker. Four: one per room, read aloud, pull on a count; a bonus vault opens if all four land within 6 s | S (sequence), M (bonus timer) |
| 3 | 13 | **The Heartbeat Doors** | Twin plates (the Weighing Room): plate A outside the Mould Room, plate B inside past a periodic Spike Bed; the mould gate opens only while both are held; a lever inside locks it open | Hold A and B, send a third body through, pull the lever | Step off and the gate drops on whoever is inside; the inside lever opens it, so nobody is sealed; plates ignore Downed bodies | Solo: Hold a companion on A, time the spikes, stand on B, a third companion on Aggressive goes through. Four: two hold and complain, two loot | M (new Plate prop, Door.HeldOpen) |
| 4 | 18 | **Feeding the Press** | The Mangle: the Press on the one corridor into the rotunda, plate on the far side, a Choir nest beside the near mouth | Trigger the nest, sprint the corridor, step the plate; the walls close on the horde, every time | Hesitate and it closes on you; revive after calm, with a lecture from the Verger | Solo: companions Hold beyond the plate, you are the bait. Four: one camps the plate, three bait in relays | S (placement preference) |
| 5 | 19 | **The Last Station** | Three galleries into the rite junction: A has periodic Spike Beds, B has a Press with its plate on the enemy side, C is open (the Dispatch Fire structure from the siege pitch) | Knock the barrel out of B so it is live; start the rite; fight C; let A's waves impale themselves; let B's trigger their own Press | Maud downed pauses the rite; revive with 6 m clear; jamming B makes it the open flank | Solo: companions Aggressive at C, you guard Maud and watch timers. Four: one per gallery, one on Maud; the B player sprints through between cycles | S (placement preferences on a Ritual floor) |

**Boss arenas** (from the catalogue):

| Floor | Boss | Arena | Cost |
|---|---|---|---|
| 5 | Canon Ludd (Slam) | **The Gallery**: a ledge on one arena wall; Slam cannot reach it; ranged members shoot from it while melee kite the ramp foot | M (allow a ledge in the boss arena) |
| 10 | Mother Nit (Summon) | **The Hatchery**: burning layout with fire laid in a ring around the egg mound she returns to; her brood spawns into flame | S (fire placement bias) |
| 15 | The Bellows (Charge) | **The Gauntlet**: the Press in a spoke off the arena; bait the charge into the slabs for 40% of its health and a 3 s stun, never a kill | M (Press-vs-boss rule, spoke placement) |
| 20 | The Verger (Volley) | **The Colonnade**: a Ruin structure in the arena; pillars block the Litany; move pillar to pillar | S (Ruin allowed on boss floors) |

**Engine asks, complete list.** (1) Twin plates and a held-open gate, M. (2) A flag set when a fighter leaves, S. (3) A cast member as the Ritual objective's ritualist, S or M depending on how the controller spawns her. (4) Press and Spike Bed placement preferences (guard a chest, guard the rotunda corridor, three-gallery rite), S. (5) The four arena features above. Everything else is data and lines. If the Heartbeat Doors slip, Ulf hands clue 3 over in dialogue and the chain holds.

---

## 10. The Verger's taunts

Thirty voiced, clean twins for every coarse line. Trigger list from `TauntTrigger`.

| Trigger | Coarse | Clean |
|---|---|---|
| FloorEnter | "Wipe your bloody feet. This is a church." | "Wipe your feet. This is a church." |
| FloorEnter | "Mind the step. It is His. He would like it back eventually." | same |
| FloorEnter | "Please do not bleed on this Station. We have only just had it done." | same |
| Capture | "The sexton goes back in his box. Everything in its place." | same |
| Freed | "One of the congregation up and about. He was never going to make deacon." | same |
| AllFreed | "All of them? Well. They'll only wander back. They always do." | same |
| GasThickens | "That is Himself breathing out. Do try not to take it personally." | same |
| GasLethal | "Lie down, there's a good congregation. It is the same thing, only sooner." | same |
| WrongLever | "Correct. Entirely correct. Himself approves." | same |
| WrongLever | "Wrong. Himself heard that. So did everything else." | same |
| GateShift | "The doors move. They are His throat. Did you think they would hold still for you?" | same |
| VaultOpened | "You've opened something. Bloody hell. Close it on your way down." | "You've opened something. Close it on your way down." |
| RitualStart | "You will not finish the verse. Nobody finishes the verse." | same |
| RitualistDown | "She's lying down. Sensible. Leave her." | same |
| WaveBreak | "The Choir takes a breath. So should you. It will be your last for a while." | same |
| RitualDone | "...Finished. Well. There's a first time, and I shan't pretend I'm not put out." | same |
| ObjectiveDone | "Tick. I keep the book, you know. Somebody must." | same |
| Idle | "Standing about in a church. Sit, then. Kneel. Lie down. In that order, ideally." | same |
| MemberDowned | "Lie still, that's it. Don't make a sodding fuss, the others haven't." | "Lie still, that's it. No fuss. The others never did." |
| PartyWiped | "There. Four. Bang on schedule. I'll tell the council to bloody well pay you." | "There. Four. On schedule. I'll tell the council to settle your invoice." |
| Descend | "Deeper. Yes. He swallows; you go down. There is no cleverness in it." | same |
| Reinforcements | "More of the Choir. Do you hear it yet? You will." | same |
| Crushed | "Oh, good. The Press works. Nobody tells me anything down here." | same |
| AllyDead | "One of yours stayed behind. We'll find a use. We always find a use." | same |
| AllyLeft | "One of the congregation has gone home. Do keep up." | same |
| FloorEnter (16+) | "The Dream. Lower your voices. He hears you now, and He is not fond of shouting." | same |
| FloorEnter (18, `wake_gorm` pending) | "This is the hall where He turns over. Please do not encourage Him." | same |
| MemberDowned (19+) | "Nearly. Nearly there. A little further down and you can stop." | same |
| Idle (Forge) | "The bellows don't stop for you. Nothing here stops for you. That's rather the point of a heart." | same |
| Descend (into 20) | "Fourth Station. Come in. Come in. Mind the teeth." | same |

---

## 11. Narration

**Tier entrances.**
- Crypt: "The lychgate stands on His chest. Below it, Dunmarrow laid its dead on Him for six hundred years and called it consecrated ground. First Station: the Skin."
- Caves: "Below the church the stone turns soft and wet and green, and it is warm, and it moves when nothing should. Second Station: the Gut."
- Forge: "Heat, and a beat under the heat. Something here is being made, over and over, and it has never once been asked. Third Station: the Heart."
- Abyss: "Marble, because He dreams in marble. Somewhere ahead a choir is singing something very old and very soft. Fourth Station: the Dream."

**Floor intros** are one line each, written in the same voice; the twenty are left to the authoring pass with these as the register. Quiet floors get only the intro and the objective.

**Boss lines** (two each, enter and death):
- Canon Ludd: "He is only doing the liturgy." / "The liturgy continues without him. It always has."
- Mother Nit: "The god has lice. The lice have a queen. Everything has a queen." / "Her children scatter into the dark and He does not scratch."
- The Bellows: "The heart-valve knows you are foreign. It beats faster." / "The beat does not stop. It only loses count for a while."
- The Verger: see section 8.

---

## 12. Voice budget

The Runware bake voices about 80 campaign lines plus 30 taunts; everything else is subtitled.

| Pool | Lines |
|---|---|
| Floor intros | 20 |
| Tier entrances | 4 |
| Boss enter and death | 8 |
| The Verger on 20 (start, two half-health, two death) | 5 |
| Cast key lines: Crane 7, Maud 8 (voiced on 1, 11, 18, 19 only), Tobit 5, Grisel 6, Ulf 5 | 31 |
| Fighter join and leave lines | 8 |
| Spare for the authoring pass | 4 |
| Taunts | 30 |

Maud on floors 6 and 16 is subtitled. The Verger on 19 speaks only through taunts already in the thirty.

---

## 13. Solo and co-op

**Solo.** The three companions carry the arguing and laughing between NPC floors: Brand approves of Foehammer, Sable wants the gold teeth before Tamsin gets them, Wren names every trap, Ilex argues with Tobit about theology. Every puzzle has a Hold solution. The fear and greed tests fire on the fighters, so the player sees an ally leave and can win one back.

**Four friends.** The puzzles are faster and louder: four levers on a count, two on plates complaining, relays through the Press, one gallery each at the last rite. One player owns each NPC conversation and the others listen; whoever forgives Grisel does it for the table. The Decide on 18 is the conversation owner's, so the table should decide who talks to the god.

**Replayability.** Three flags and two fighter-fate flags give eight meaningful endings to the last three floors; the Lullaby order, the parity on 14 and every generated floor change per seed.

---

## 14. What happens next

1. The owner reads this and changes anything: names, the villain's voice, the ending choice, how dark.
2. The bible becomes the outline the planner expands: either Claude writes the twenty floor plans and barks from it (credits, under the spend cap, recorded for replay), or the floors are hand-authored into the replay format as Ashmere was. Claude is the recommendation; this document is the outline prompt.
3. The engine asks in section 9 are scheduled into Phase 11 (Press placement preferences, arenas) and Phase 12 (fighter-left flag, cast-as-ritualist, twin plates).
4. The interaction banks (Phase 14) give every cast member answers on the 28 topics; the persona cards come straight from section 4.
