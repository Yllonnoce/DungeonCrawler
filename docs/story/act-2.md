# The Gullet of Gorm

_Act 2: floors 6-10_

## Floor 6: The Swallow

_Mood: wading in standing water that should be running_

**Story beat:** The backed-up water of the Swallow, stagnant and full of vermin. fighter_tamsin is salvaging the wardens' abandoned tools; her partner Jory went under ahead of her, and she joins for a share. cast_maud stands in the water and speaks clearly for the first time: 'Keep the water going down to him. He is thirsty.'

**Narrator:** _(tier 2 entry)_ Below the church the water stands. It should be running down to the god, but the drains beneath it are choked, so it sits here and goes sour. The Gut.

**Narrator:** _(floor intro)_ You wade into the Swallow. The first drain runs now, but the drains below it are still choked, so the water backs up here, knee-deep and going sour. Ahead, a miner is pulling wardens' tools out of the muck.

### Maud Sorrel

**Maud Sorrel:** _(node m6_dry)_ Soaked through and cold, dear, and no matter. Keep the water going down to Gorm. He is thirsty. Thirty-eight years I kept it moving, and now it stands here going sour.

  - **Party (choice):** Why is the water standing? _[to m6_ahead]_
  - **Party (choice):** Who else is down here? _[to m6_tamsin]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m6_ahead)_ The drains under us are choked, dear, packed solid like the grates up top, and not by accident. So the water backs up here and the god goes without. Clear them and it runs down to him again.

  - **Party (choice):** Who else is down here? _[to m6_tamsin]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m6_tamsin)_ Tamsin Rook, dear. Good miner. She came down after the wardens' tools, and her partner Jory went into this water ahead of her and hasn't come up. Help her look. The water won't give him back by itself.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

### Hordes

**Narrator:** _(The Swallowed Brood, Alarm)_ Something runs for the stairs, and the breaches in the shallows answer it: the water boils white with small wet backs.

## Floor 7: The Crop

_Mood: a seized pump, a camp, a rite to wake a god_

_Objective: ritual_

**Story beat:** Ritual: the Herald's minions hold a rite to hurry the god awake. cast_grisel, camped close by, works a seized pump to push water down while the party holds the minions off; getting it turning breaks the rite. She asks for help finding her crew, caged somewhere below, and says nothing more about them.

**Narrator:** _(floor intro)_ You come into the Crop: a warden's camp beside a seized pump, and at the old altar the orc's minions holding a rite to hurry the god awake.

**Narrator:** _(RoomEnter room 9)_ The orc's minions keep a rite at this altar to hurry the god awake. Beside it, Grisel Thorn, a warden, heaves at a seized pump and shouts to you: hold them off while she frees it, then help her find her crew.

### Grisel Thorn

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, ritual)_ My minions are holding a rite at the altar, hirelings, to hurry your god awake. That warden at the pump thinks a little bloody water will stop it.

**Morg Ashtusk:** _(RitualistDown, ritual)_ Down goes the warden. Pick her up, hirelings, or the pump stays seized and my rite goes on.

**Morg Ashtusk:** _(RitualDone, ritual)_ So the pump turns and my rite breaks. Enjoy it, hirelings. Your god is still hungry, and the drains below are still mine.

### Hordes

**Narrator:** _(The Two Mouths, Enter)_ Both breaches cough at once, and the grey pack pours out at you, crawling now. They were townspeople once.

**Narrator:** _(The Weeping Breach, Chest)_ The lid lifts, the wall behind it does too, and the drift fills with wet scrabbling.

## Floor 8: The Craw

_Mood: tremors, split boulders, bandits in the stores_

**Story beat:** Bandits paid by the Herald to keep townspeople out are looting the wardens' abandoned stores, cast_pellet's stock among them, and he wants it back. Tremors split the boulders: the starving god is stirring.

**Narrator:** _(floor intro)_ You squeeze into the Craw, where the wardens kept their stores. Bandits have been looting them, paid by the orc to keep the town out, and vermin nest in what they left. The ground lurches as you go.

**Narrator:** _(RoomEnter room 4)_ Crates broken open and half carted off, some with Master Pellet's mark on them. On top sits a bandit's purse full of the orc's coin: wages for keeping the town out of these stores.

**Narrator:** _(RoomEnter room 5)_ The floor heaves and a boulder beside you splits in two. That is the god turning over below, starved of water and closer to waking.

### Hordes

**Narrator:** _(The Craw's Lice, Chest)_ The lid lifts, the shell roof shivers, and both breaches open at once: the hoard was bait, and the vermin were waiting behind the walls for whoever came to take it.

## Floor 9: The Cistern

_Mood: warm sandstone, four levers, a seal on a page_

_Objective: shiftingvault_

**Story beat:** Vault: the party works the levers and opens the vault, which holds clue_contract, the order dismissing the drain-wardens under the council's seal. cast_crane arrives and reads his own signature; caught, he gives the official excuse (the Verger had already shut the mines, so why pay wardens who could not go below) and says nothing of money. cast_grisel watches him read it, then admits she ran and left her crew in the cages. Decide: forgive her (flag grisel_forgiven) or not (flag grisel_condemned).

**Narrator:** _(floor intro)_ You come down into the Cistern, where the wardens worked. The water stands in its sandstone tanks, going sour. Ahead, the alderman has come down again, out of breath, and the warden from the Crop has followed you.

**Narrator:** _(RoomEnter room 13)_ The wardens' strongroom, stripped of its tools. A leaf of a council order lies on the floor with the council's seal on it: the drain-wardens are to be paid no longer.

### Alderman Hesketh Crane

**Alderman Hesketh Crane:** _(node greet)_ Progress check, that's all; the council likes a tally. Only, there's a council paper torn in three and loose about these cisterns. Bring me the leaves, would you? Unread, if you can manage it.

  - **Party (choice):** We'll gather your papers. _[AcceptQuest]_
  - **Party (choice):** Gather your own litter, alderman. _[DeclineQuest]_
  - **Party (choice):** Here. Every leaf of it. _[TurnIn; to read]_

**Alderman Hesketh Crane:** _(node read)_ 'The drain-wardens are to be paid no longer, since the Verger has forbidden anyone to go below.' Under the council's seal. And that is my hand. I signed it.

  - **Party (choice):** Why stop paying them? _[to excuse]_
  - **Party (choice):** You sacked your own wardens. _[to excuse]_

**Alderman Hesketh Crane:** _(node excuse)_ The Verger had already shut the mines. What use is a warden who can't go down? So the council stopped paying them. It seemed only sensible at the time.

  - **Party (choice):** They went down anyway. Unpaid. _[to after]_
  - **Party (choice):** Go back up, Crane. _[Farewell]_

**Alderman Hesketh Crane:** _(node after)_ I know. They went down after we stopped paying them, and they never came up. I'll keep this, and nobody burns it. Go on down.

  - **Party (choice):** Go back up, Crane. _[Farewell]_

_Quest: The Council's Order (Investigate): Gather the torn leaves of a council order from the cisterns and bring them to Crane_

**Alderman Hesketh Crane:** _(quest offer)_ Three leaves, by my count, soaked and scattered. Bring them here and we shall say no more about it. Paperwork out of its folder makes me unwell.

**Alderman Hesketh Crane:** _(quest accept)_ Good. Quickly, and do not read them on the way. They are council property and very dull.

**Alderman Hesketh Crane:** _(quest decline)_ Then they stay down here getting damp, and so, I suppose, does my peace of mind.

**Alderman Hesketh Crane:** _(quest complete)_ All three. My hand, under the council's seal. I'll keep it, and nobody burns it.

**Alderman Hesketh Crane:** _(quest fail)_ Gone soft in the wet, then. Well. The council keeps its own copy, and I know what it says.

### Grisel Thorn

**Grisel Thorn:** _(node greet)_ The council dropped us once the Verger shut the mines. We went down anyway, unpaid. Then the orc's things caged my crew, and I ran. Left them there. Say what you think of that, then help me or don't.

  **Brand:** _(aside)_ Fear does that. What you do after is what counts.

  - **Party (choice):** You were afraid. It's forgiven. _[Choose; sets grisel_forgiven; to forgiven]_
  - **Party (choice):** You left them to turn. That stands. _[Choose; sets grisel_condemned; to kept]_

**Grisel Thorn:** _(node forgiven)_ Forgiven. Well. I'll not waste it. Find where the orc keeps them caged, and I'll be there to bring them out.

  - **Party (choice):** Mind yourself, Grisel. _[Farewell]_

**Grisel Thorn:** _(node kept)_ Fair. I'd have said the same. Then I'm on my own, and on my own I look after myself. Go on down.

  - **Party (choice):** We'll remember. _[Farewell]_

_Quest: What Grisel Did (Decide): Answer Grisel Thorn for leaving her crew in the orc's cages_

**Grisel Thorn:** _(quest offer)_ Don't look at me like a jury. Say the thing or don't say it. I've carried it since spring; another moment won't kill me, though plenty else might.

**Grisel Thorn:** _(quest accept)_ Said, then.

**Grisel Thorn:** _(quest decline)_ Silence is also an answer. A coward's, but an answer.

**Grisel Thorn:** _(quest complete)_ That's that. The water's still sour and we're both still standing in it.

**Grisel Thorn:** _(quest fail)_ Nothing, then. Fine. The spiders are better company and they bite less.

### Hordes

**Narrator:** _(The Fourth Verse Breach, Enter)_ The carved verse splits down its middle and the wall behind it is not wall at all; it is legs, and all of them coming.

**Narrator:** _(What Waited Behind the Hymn, Chest)_ The lid lifts, and the hollow behind the stonework answers it: a long wet scrabble, getting closer in the dark.

## Floor 10: Mother Nit

_Mood: a hatchery ringed in fire over a choked drain_

**Story beat:** Boss: Mother Nit, bloated in the stagnant water, breeds her vermin in a nest over the second great drain. When she falls the party turns the sluice wheel, the second drain runs, the water drops and the air clears; the narrator and cast_maud say so.

**Narrator:** _(floor intro)_ Under a nest of silk and eggs lies the second great drain, choked solid. Mother Nit has bred her vermin in its standing water all season. Mind the lichen; it burns in slow rings across the floor.

**Narrator:** _(BossSeen)_ Mother Nit, swollen on the standing water, squats on her nest over the drain. She calls her brood to her; cut them down, or they will bury you.

**Narrator:** _(BossDead)_ Her nest breaks apart and goes down with the water: the second great drain runs. Back up the Gut the standing water drops, the air clears, and above you the shaking eases. Two great drains to go.

### Hordes

**Narrator:** _(The Crack Spills, Enter)_ The wall of the crack moves, and keeps moving, and is not a wall.

**Narrator:** _(The Silk Cellar Hatch, Chest)_ The lid lifts, the silk above it splits, and the ceiling comes down on small wet legs.

### Boss: Mother Nit

**Mother Nit:** _(intro)_ Queen of the Gut's vermin, set here by the orc to keep the second drain choked. Every egg she lays packs the grate tighter.

**Mother Nit:** _(death)_ She falls across the grate the orc gave her to keep, and what is left of her brood scatters into the dark.
