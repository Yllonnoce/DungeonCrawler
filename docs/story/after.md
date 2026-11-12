# The Gullet of Gorm

A good god, Gorm, sleeps under the town of Dunmarrow, fed by the water that runs down through the old mines, and his dreams hold the Sleepless, an evil god, out of this world. For three hundred years the town's drain-wardens kept the mine drains clear. Now Morg Ashtusk, the Herald, an orc chieftain serving the Sleepless, has seized the mines, filled them with traps and minions and choked the drains: the lower levels flood, the wells run sour, and the starving god stirs. Dunmarrow hires mercenaries to clear the way so the drains can run again. If Gorm wakes, the Sleepless breaks through; if they fail, the mines collapse and the town perishes.

## Floor 1: The Lychgate

_Mood: a mine head under a church, a dead orc still talking_

**Story beat:** The Herald (Morg Ashtusk, the orc) attacks at the mine head. viewer_foehammer, who keeps the gate, and the party kill him, and his voice comes straight back up from below: the foundry will recast him. cast_crane hires the party and explains the job (sour wells, shaking ground, wardens who went down and did not return) and asks for the council notices posted at the mine head. cast_maud goes down ahead on her rounds, not knowing she drowned: help me clear the grates, the water has to go down.

**Narrator:** _(tier 1 entry)_ The lychgate opens on the church's crypts, and the crypts on the mines. Dunmarrow's dead are up and walking, raised to keep you from the drains below. The Skin.

**Narrator:** _(floor intro)_ You arrive at the head of the old mines, the crypt stair under the lychgate. A huge orc has come up it, and the dwarf who keeps the gate is already fighting him.

**Narrator:** _(RoomEnter room 0)_ The orc at the gate is Morg Ashtusk, Herald of the Sleepless, an evil god. He has taken these mines for his master, and he means to keep you from the drains below.

### Alderman Hesketh Crane

**Alderman Hesketh Crane:** _(node gate)_ Alderman Crane, for the council. That orc came up the shaft shouting that the mines are his master's now, and that he can't be killed for good. The drains need clearing, and I am not the man to do it.

  **Sable:** _(aside)_ He means us.

  - **Party (choice):** What's gone wrong down there? _[to bread]_
  - **Party (choice):** So what's the job? _[to bread]_
  - **Party (choice):** Your basket. Notices and all. _[TurnIn; to done]_

**Alderman Hesketh Crane:** _(node bread)_ Sour wells, shaking ground, and wardens who went down and never came up. Clear the traps and the dead so the drains can run. And fetch my basket from the first cellar; it has council notices in it. Unread, please.

  - **Party (choice):** We'll fetch your basket. _[AcceptQuest]_
  - **Party (choice):** Fetch your own basket, alderman. _[DeclineQuest]_
  - **Party (choice):** Your basket. Notices and all. _[TurnIn; to done]_

**Alderman Hesketh Crane:** _(node done)_ You didn't read them? Don't answer; I'd rather believe it. Council notices, nothing more, and the council thanks you in writing.

  - **Party (choice):** Go back up, Crane. While the gate's open. _[Farewell]_

_Quest: Council Property (Recover): Fetch the alderman's bread-basket, and the council notices in it, up from the first cellar._

**Alderman Hesketh Crane:** _(quest offer)_ I took the council notices off the board at the mine head this morning and put them in my bread-basket. Then the orc came up the stair, I ran, and the basket stayed in the first cellar. Bring it back. Don't read them.

**Alderman Hesketh Crane:** _(quest accept)_ Good. Good. Don't eat the cloth.

**Alderman Hesketh Crane:** _(quest decline)_ Then it rots down there with the rest of the parish's goodwill. Suit yourselves.

**Alderman Hesketh Crane:** _(quest complete)_ Every notice, and the cloth folded on top. Good. Nothing anyone needs to read. The council thanks you in writing.

**Alderman Hesketh Crane:** _(quest fail)_ Keep it. I shall enter the basket on the loss ledger, under drainage.

### Maud Sorrel

**Maud Sorrel:** _(node m1_step)_ Mind the step, dear, it's wet. Help me clear the grates; I'm late on my rounds. The water has to go down. It always has to go down.

  - **Party (choice):** Who are you, mother? _[to m1_who]_
  - **Party (choice):** You should go back up. It isn't safe. _[to m1_back]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m1_who)_ Maud Sorrel, dear. Drain-warden, thirty-eight years. I was on my rounds, and then the water... well, never mind. Wet? I've been wetter.

  **Wren:** _(aside)_ She's soaked through. Wrong.

  - **Party (choice):** Thirty-eight years? _[to m1_back]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m1_back)_ Thirty-eight years, dear, and I've never missed a round, so I'm not going up now. The wells up top are sour; that's the grates, packed solid. The water has to go down.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

### Floor taunts

**Morg Ashtusk:** _(Descend)_ Down you come, hirelings. Kill me at the gate or not; my foundry pours another. These mines are my master's now, and your bloody god is going to wake.

### Hordes

**Narrator:** _(Seam in the Vestry Wall, Enter)_ The vestry wall opens along a seam, and dry things come out of it sideways.

**Narrator:** _(Coats Off Their Hooks, Chest)_ You lift the chest lid, and the cloakroom answers: grey husks shrug out of the coats on the hooks and come for you.

**Narrator:** _(The Diggers Come Up, Enter)_ Gravediggers' walk, and the gravediggers come up through their own breaches.

**Narrator:** _(The Rubble Stands Up, Chest)_ Behind the fallen columns the rubble stands up. One of them stands taller than the rest.

**Narrator:** _(Wading Out of the Font, Enter)_ The font water goes grey, and things wade out of the breaches to meet you in it.

**Narrator:** _(Neighbours at the Hut, Enter)_ The squatters' hut shudders. The breaches behind it cough up neighbours.

## Floor 2: The Vestibule

_Mood: moss in the mortar, grey prisoners who look alike_

_Objective: jailbreak_

**Story beat:** Jailbreak: the prisoners are captured drain-wardens, grey and half-turned in the Herald's cages, already hard to tell apart. The ones the party frees say the traps are new and somebody is building them, that the ones they finish go grey and join the packs (husks), that cast_grisel's crew went down first and nobody has seen them since, and that cast_ulf still keeps the pump works but nobody has heard from him since spring.

**Narrator:** _(floor intro)_ These cells hold the drain-wardens who went down and never came up: grey, half-turned, already hard to tell apart. Get them out while you can. Nobody has heard from Old Ulf at the pump works since spring either.

**Narrator:** _(RoomEnter room 7)_ From the cells, the wardens call to you: the traps are new and somebody is building them, and anyone who finishes turning goes grey and joins the packs. Grisel Thorn's crew, they say, went down first.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ I am Morg Ashtusk, Herald of the Sleepless, and these mines are his now. Kill me at the gate if you like; I come back. Those are wardens in my bloody cages, and the finished ones join my packs.

**Morg Ashtusk:** _(Capture, jailbreak)_ {prisoner} goes back in the cage. Another week and nobody will tell that one from the rest.

### Hordes

**Narrator:** _(The Wandered Congregation, Chest)_ The mortar splits at both ends of the vestibule, and the grey pack comes at you. Some of them were drain-wardens this spring.

## Floor 3: The Nave

_Mood: chalk-white bone chapel, a deacon praying alone_

**Story beat:** cast_tobit prays in the Nave for Gorm to save the town; his master the Verger went below in spring, he says, to pray for it. The first crushing trap is on this floor. Decide: tell him the god is starving and no help is coming (flag told_tobit), or let him pray (flag kept_his_saint). Told, he is shaken and resolves to find out what happened to the drains; not told, he is grateful and keeps praying.

**Narrator:** _(floor intro)_ Chalk-white bone stacked to the vaulting, and one candle lit in the chancel, where a young deacon is praying. Mind the walls: some down here are new, and built to crush.

### Deacon Tobit Hale

**Deacon Tobit Hale:** _(node greet)_ Pray with me, would you? The wells are sour and the ground shakes, but if we ask him properly, Gorm will save the town. He always has.

  **Brand:** _(aside)_ No promises, deacon.

  - **Party (choice):** You're down here alone? _[to ask]_
  - **Party (choice):** Talk, deacon. We're listening. _[AcceptQuest; to ask]_

**Deacon Tobit Hale:** _(node ask)_ It's only me now. My master the Verger went below in spring to pray for the town, and I keep the church till he's back. Tell me true: does Gorm hear us down there?

  **Ilex:** _(aside)_ Theologically, deacon, 'hears' and 'answers' are different words.

  - **Party (choice):** Nobody's coming, deacon. Your god is starving. That's why we were hired. _[Choose; sets told_tobit; to end]_
  - **Party (choice):** He hears you. Keep praying. _[Choose; sets kept_his_saint; to end_kept]_

**Deacon Tobit Hale:** _(node end)_ Starving. And I've been asking him to feed us. If the water isn't reaching him, I'll find out why; I'm thorough, I always am. Mind the walls past here. Some are new, and they crush.

  - **Party (choice):** Keep your head down, deacon. _[Farewell]_

**Deacon Tobit Hale:** _(node end_kept)_ Thank you. I knew he heard. I'll keep the candle lit until my master comes back up. Mind the walls past here; some of them are new, and they crush.

  - **Party (choice):** Keep your head down, deacon. _[Farewell]_

_Quest: What to Tell the Deacon (Decide): Tell Deacon Hale the god is starving and no help is coming, or let him keep praying._

**Deacon Tobit Hale:** _(quest offer)_ You've seen more of these crypts than I have lately. Tell me plainly how it is. I am told I am a good listener.

**Deacon Tobit Hale:** _(quest accept)_ Say it plainly. I would rather be wrong out loud than obedient in a whisper.

**Deacon Tobit Hale:** _(quest decline)_ As you wish. The candle will keep until you change your mind. It keeps very well.

**Deacon Tobit Hale:** _(quest complete)_ Thank you for telling me how it is. I know what I have to do now.

**Deacon Tobit Hale:** _(quest fail)_ Go down, then. I'll keep praying. Somebody has to.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter)_ My dead keep this church now. Let the deacon pray, hirelings; nobody down here is bloody listening but me.

**Morg Ashtusk:** _(Crushed)_ Flat as a coin. I built that press this spring, and it does a better day's work than you sorry sods.

### Hordes

**Narrator:** _(The Shelved Dead, Enter)_ The stacked bone shifts, and shifts again, and then the wall gives up its tenants all at once.

## Floor 4: The Chantry

_Mood: rotten timber, rising foul air, a log that stops_

_Objective: toxic_

**Story beat:** Foul gas rises from the stagnant water while the party searches the chantry. cast_crane has come down to check progress and finds clue_ledger, the wardens' log: the drains cleared every season for three hundred years, until this spring's last entry, 'By the Verger's order, no one below.' Relieved it was the church and not the council, he says so too quickly.

**Narrator:** _(floor intro)_ You come into rotten timber and a green air rising from cellars where the water has stood still since spring. Somewhere in it, a man with a ledger is apologising to nobody.

**Narrator:** _(QuestDone)_ Crane holds the pages at arm's length, as a man holds something that has bitten him before.

### Alderman Hesketh Crane

**Alderman Hesketh Crane:** _(node n1)_ Ah. You. I came down to check progress, that's all, and found the wardens' log with its last pages torn out and scattered through these drifts. Council property. I'd like them back.

  - **Party (choice):** What's in the log? _[to n2]_
  - **Party (choice):** Keep your paper. The air is going green. _[Farewell]_
  - **Party (choice):** Your pages, alderman. _[TurnIn; to n3]_

**Alderman Hesketh Crane:** _(node n2)_ Every clearing of the drains, every season, written down by the wardens for three hundred years. The last three pages are in the drifts. Bring them to me before anyone else reads them.

  - **Party (choice):** I'll gather your pages. _[AcceptQuest]_
  - **Party (choice):** Gather them yourself. Mind the gas. _[DeclineQuest]_
  - **Party (choice):** The pages are here, Alderman. _[TurnIn; to n3]_

**Alderman Hesketh Crane:** _(node n3)_ Cleared every season for three hundred years. Then this spring: 'By the Verger's order, no one below.' The church stopped them. Not the council. Good. I mean, how dreadful.

  - **Party (choice):** Whatever you say, alderman. _[Farewell]_

_Quest: The Wardens' Log (Investigate): Recover the scattered pages of the wardens' log from the drifts and bring them to Crane_

**Alderman Hesketh Crane:** _(quest offer)_ Three pages, no more. One in the alms drift, one along the timbering, one up in the chantry. I would go myself, but the chain of office is heavy and I am, frankly, a coward.

**Alderman Hesketh Crane:** _(quest accept)_ Good. Good. I shall stand here and guard the heap, which is important work.

**Alderman Hesketh Crane:** _(quest decline)_ Quite right. Paper is paper, and nobody reads the old ones.

**Alderman Hesketh Crane:** _(quest complete)_ Three hundred years of clearings, and then this spring: 'By the Verger's order, no one below.' So it was the church. Not the council. What a relief. I mean, what a shame.

**Alderman Hesketh Crane:** _(quest fail)_ Burnt, soaked, eaten. Just as well. A ledger is only a ledger if somebody reads it.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, toxic)_ Smell that, hirelings? Standing water, gone rotten, rising out of the cellars. The stairs are at the bottom of it. Do hurry; you're paid by the floor.

**Morg Ashtusk:** _(GasThickens, toxic)_ Up to your knees now, you poor sods. The town bought your swords, not your lungs.

### Hordes

**Narrator:** _(The Parish Overflow, Enter)_ The crossing wall gives up its mortar and the pack pours through at you, grey and alike. A few of them still wear wardens' boots.

## Floor 5: Canon Ludd

_Mood: a dead canon standing guard on a choked drain_

**Story beat:** Boss: Canon Ludd, the Herald's first lieutenant, raised from the church's dead, guards the first great drain, choked on purpose. When he falls the party turns the sluice wheel, the first drain runs and the shaking eases for a while; the narrator says so. The Herald sneers and promises worse.

**Narrator:** _(floor intro)_ Under the chantry runs the first great drain, or it should. You find its grate packed solid on purpose, and a dead canon standing over it, still saying the office to empty pews.

**Narrator:** _(BossSeen)_ Canon Ludd, raised from the church's own dead to guard the drain, swings his censer at empty benches. The ledge above him is bare of ornament; use it.

**Narrator:** _(BossDead)_ The first drain runs again, and above you the shaking eases for a while. Far below, the orc's voice: 'One drain, hirelings. I choked three more, and your god is still hungry.'

### Hordes

**Narrator:** _(The Charnel Overflow, Enter)_ The charnel wall gives at both seams, and the church's dead walk out at you, a few greyer things in wardens' coats among them.

### Boss: Canon Ludd

**Canon Ludd:** _(intro)_ I am the Herald's first lieutenant. He shut this drain, and I keep it shut. Kneel, strangers; the church's dead have room for you.

**Canon Ludd:** _(death)_ Turn the sluice wheel, then. Let the water down to the god, and the ground will lie still a while. I was a canon here, once.

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

**Narrator:** _(The Shallows Brood, Alarm)_ Something runs for the stairs, and the breaches in the shallows answer it: the water boils white with small wet backs.

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

_Mood: warm sandstone, sour standing water, a seal on a page_

_Objective: shiftingvault_

**Story beat:** An ordinary floor (no objective generates here on the shipped seed): the Cistern, where the wardens worked, its tanks standing sour. clue_contract, the order dismissing the drain-wardens under the council's seal, lies torn in three about the cisterns; the party gathers the leaves for cast_crane, who reads his own signature. Caught, he gives the official excuse (the Verger had already shut the mines, so why pay wardens who could not go below) and says nothing of money. cast_grisel watches him read it, then admits she ran and left her crew in the cages. Decide: forgive her (flag grisel_forgiven) or not (flag grisel_condemned).

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

**Narrator:** _(The Cistern Breach, Enter)_ The cistern wall splits down its middle, and what is behind it is not rock at all; it is legs, and all of them coming.

**Narrator:** _(What Waited Behind the Wall, Chest)_ The lid lifts, and the hollow behind the stonework answers it: a long wet scrabble, getting closer in the dark.

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

## Floor 11: The Hearth

_Mood: riveted plate, ember light, a cheerful old founder_

**Story beat:** The old pump works, now the Herald's foundry. cast_ulf keeps it, cheerfully casting 'tools' for a customer who pays in gold; his eyes are going and he never sees the whole of what he pours. His journeyman fighter_oskar saw the whole drawings, knows better, and joins the party. cast_maud says plainly that she drowned, that the engine here once sent the water down, and that the god is weaker every day the third drain stays shut.

**Narrator:** _(tier 3 entry)_ Heat, and the thump of an engine. This is the pump works, built to push the water down to the god. The Herald has taught it to cast iron instead. The Heart.

**Narrator:** _(floor intro)_ Heat, and the thump of a great engine. This is the pump works, which pushed the water down the drains for three hundred years. The orc has made it his foundry, and somebody at the hearth is singing.

**Narrator:** _(RoomEnter room 8)_ The Great Hearth. Its engine pushed the water down to the god for three hundred years; now it drives a foundry, and the orc's iron comes off its sand.

### Old Ulf

**Old Ulf:** _(node ulf_hail)_ Off the sand, off the sand! I'm Ulf; I keep the pump works. I've an order to pour by morning for a customer who pays in gold, and hounds have got at my coal. Clear them off for me, you lot.

  - **Party (choice):** What hounds? _[to ulf_cinders]_
  - **Party (choice):** Who's the customer? What do you cast? _[to ulf_customer]_
  - **Party (choice):** The pit is quiet. Your coal is yours again. _[TurnIn; to ulf_thanks]_
  - **Party (choice):** We're here for the drains, old man. _[Farewell]_

**Old Ulf:** _(node ulf_customer)_ Never met him. The orders come down the shaft wrapped in gold: plates, springs, hinges, great iron limbs. Tools, all of it. My eyes are going, so Oskar reads me the drawings.

  - **Party (choice):** What hounds? _[to ulf_cinders]_
  - **Party (choice):** We're here for the drains, old man. _[Farewell]_

**Old Ulf:** _(node ulf_cinders)_ Ember hounds, three of them, up out of the old pit and down the slag run. They eat heat, see, and a cold forge pours slow. I'd see to them myself, but I'm eighty and I've an order on.

  - **Party (choice):** We'll put them down. _[AcceptQuest]_
  - **Party (choice):** Feed them yourself, founder. _[DeclineQuest]_

**Old Ulf:** _(node ulf_thanks)_ Hear that? The draw's come back up, and the order goes out by morning. Take a cup of the small beer; it's warm on principle. And mind the shelves on your way by.

  - **Party (choice):** Mind your eyes, old man. _[Farewell]_

_Quest: Hounds in the Coal (KillTarget): Kill the ember hounds loose in the hound pit_

**Old Ulf:** _(quest offer)_ Three hounds out of the pit and into my coal. Kill them and I'll show you a mould I'm proud of, which is all of them, so bring patience.

**Old Ulf:** _(quest accept)_ Good lad. Go down the slag run and don't let them get behind you; they bite where the armour's thin and they know where that is.

**Old Ulf:** _(quest decline)_ Suit yourselves. They'll eat, the forge runs cool, and my order goes out late. I've not sent an order late in sixty years.

**Old Ulf:** _(quest complete)_ There's the draw. Here, take this off the shelf for your trouble. Good work, that. Everything off my shelves is.

**Old Ulf:** _(quest fail)_ Still chewing, are they. Well. I've worked colder, and I've worked with worse than you.

### Maud Sorrel

**Maud Sorrel:** _(node m11_shh)_ There you are, dear. I worked it out on the way down: I drowned, back in the spring. No matter. Keep the water going down to Gorm. He gets weaker every day the third drain stays shut.

  - **Party (choice):** What was this place, Maud? _[to m11_faces]_
  - **Party (choice):** How did you drown? _[to m11_warm]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m11_faces)_ The pump works, dear. That big engine pushed the water down the drains for three hundred years. Now it drives a forge and casts things instead, and the water stands.

  - **Party (choice):** How did you drown? _[to m11_warm]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m11_warm)_ After the Verger's order, dear. Somebody had to keep the water moving, so I went down alone, and it had backed up deeper than I knew. Thirty-eight years a warden, and I never could swim.

  - **Party (choice):** What was this place, Maud? _[to m11_faces]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, shiftingvault)_ Welcome to my foundry, hirelings. My founder at the hearth pours my iron and never once asks what it's for. The stairs are behind my gates; work the bloody levers if you can count.

### Hordes

**Narrator:** _(The Burrow's Litter, Chest)_ The lid lifts and the wall behind it splits. Out of two breaches pour the cinders, grey and smouldering, and the hounds run with them. The cinders were townsfolk once.

## Floor 12: The Casting Hall

_Mood: kiln brick, a grey crew in cages, keys on a belt_

_Objective: jailbreak_

**Story beat:** Jailbreak: cast_grisel's crew are caged here, taken first and further gone than the prisoners on floor 2, nearly grey and nearly pack (cinders): proof the turned become the packs. With grisel_forgiven she helps bring them out one at a time; with grisel_condemned she took the Herald's keys to save herself and stood as his head jailer, and the party finds her dead by the cage door with his keys still on her belt, cut down by his iron when they came for the cages.

**Narrator:** _(floor intro)_ Kiln brick and cages. The orc keeps his longest prisoners here: Grisel Thorn's crew, taken first in the spring and turned the furthest.

**Narrator:** _(RoomEnter room 11)_ The cages hold Grisel Thorn's crew, taken first in the spring and nearly gone: grey to the eyes and past speaking. A few days more and they would be running with the cinders.

**Narrator:** _(RoomEnter room 11)_ Grisel Thorn lies dead by the cage door, the orc's keys still on her belt. She took them to save herself and kept her own crew locked in; when you came for the cages, his iron cut her down.

### Grisel Thorn _(needs grisel_forgiven)_

**Grisel Thorn:** _(node g1)_ You found them. That's my crew in the cages: taken first, nearly grey, nearly gone. Walk me through to the stair and keep the jailers off me, and I'll bring them out as we go.

  - **Party (choice):** Walk with us. We'll get them out. _[AcceptQuest; to g2]_
  - **Party (choice):** Stay put. You'd slow us down. _[DeclineQuest; to g3]_
  - **Party (choice):** There. The stair. Go up it or down it, but go. _[TurnIn; to g4]_

**Grisel Thorn:** _(node g2)_ Right. Keep me between you and the iron. They still know my voice, most of them. I'll call them out one at a time.

  - **Party (choice):** Move. _[Farewell]_

**Grisel Thorn:** _(node g3)_ Fair. I'd have left me too. Get them out without me, then, and tell them Grisel was here.

  - **Party (choice):** Keep your head down. _[Farewell]_

**Grisel Thorn:** _(node g4)_ That's the stair. I'll wait here for my crew and take them up myself. Don't wait for me to thank you properly; I'm out of practice.

  - **Party (choice):** Mind yourself, Grisel. _[Farewell]_

_Quest: Grisel's Crew (Escort): Bring Grisel Thorn down to the stairs alive, past the jailers, while her crew are freed._

**Grisel Thorn:** _(quest offer)_ I know every cage on this floor by the noise it makes. Get me across the casting floor to the stair, and I'll bring my crew out as we go.

**Grisel Thorn:** _(quest accept)_ Right. Slowly. They'll know my voice before they know my face.

**Grisel Thorn:** _(quest decline)_ Suit yourself. I've been left in worse rooms by better liars.

**Grisel Thorn:** _(quest complete)_ That's the stair. I'll wait here and take my crew up myself, every one you bring me. Don't wait for thanks; I'm out of practice.

**Grisel Thorn:** _(quest fail)_ Get them out. Don't you leave them now.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ My Casting Hall, hirelings. Grisel Thorn's crew are in my cages, first down and nearly finished. Another week and they run with my bloody cinders.

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ Meet my head jailer, hirelings: Grisel Thorn. She took my keys to save her own bloody neck, and now she locks her own crew in every night.

**Morg Ashtusk:** _(AllFreed, jailbreak)_ Every cage empty, hirelings. Grisel Thorn sold herself to me for a ring of keys, and died holding them. Cheap at the price.

### Hordes

**Narrator:** _(The Spill, Enter)_ The kiln wall cracks and the cinders pour out at you, grey and burning. The ones finished in these cages run with them now.

## Floor 13: The Mould Room

_Mood: cold steel plate, white lamps, shelves of moulds_

**Story beat:** cast_ulf shows off his moulds and they are clue_moulds: patterns for the crushing walls, the spike beds, the iron minions, and the Herald's own mould, larger. He finally sees what he has been making and wants every mould broken. fighter_oskar admits he knew all along and kept quiet because the Herald threatened the old man; viewer_foehammer wants the Herald's mould smashed and will do it himself.

**Narrator:** _(floor intro)_ Cold plate, white lamps, and shelf upon shelf of moulds packed in casting sand. This is where Old Ulf keeps his patterns, and he is proud of every one.

**Narrator:** _(RoomEnter room 4)_ Shelf upon shelf of casting moulds. Up close you can make out what they are for: the face of a crushing wall, the teeth of a spike bed, the limbs of the iron brutes.

**Narrator:** _(RoomEnter room 2)_ By the stair stands the biggest mould in the works, taller than two men: the orc's own shape, larger than the body you killed at the gate. This is where he was poured again.

### Old Ulf

**Old Ulf:** _(node u1)_ Mind the shelves, mind the shelves. Forty years I've kept this sand dry and you come in dripping. Come on, then. Have a look at the good ones.

  - **Party (choice):** What are you casting down here, old man? _[to u2]_
  - **Party (choice):** We're not here for pottery. _[Farewell]_
  - **Party (choice):** Your moulds, old man. Look at them properly. _[TurnIn; to u4]_

**Old Ulf:** _(node u2)_ Tools, lad. Plates, springs, hinges, great iron limbs, all for my customer. My eyes can't read the patterns any more. Fetch me three off the shelves and I'll name them under the lamp.

  - **Party (choice):** We'll bring your moulds. _[AcceptQuest; to u3]_
  - **Party (choice):** Keep your sand. We've stairs to find. _[DeclineQuest]_

**Old Ulf:** _(node u3)_ Carry them flat. Tip one and the sand cracks, and a cracked mould pours crooked.

  **Sable:** _(aside)_ Flat, darling? I once carried a duke's mirror down a drainpipe.

  - **Party (choice):** Here. Three moulds, flat as you asked. _[TurnIn; to u4]_
  - **Party (choice):** Later. They're heavy. _[Farewell]_

**Old Ulf:** _(node u4)_ That's a crushing wall. That's a spike bed. That's an arm off one of those iron brutes. And the big one by the stair, the last I poured, is the orc himself, larger. I called every one good work.

  - **Party (choice):** You didn't know. _[to u5]_
  - **Party (choice):** So you built the orc's traps. _[to u5]_

**Old Ulf:** _(node u5)_ No excuses. I took the gold and never once asked what it was for. Help me break them, every mould on these shelves, and the orc's first.

  - **Party (choice):** Your journeyman Oskar knew. _[to u6]_
  - **Party (choice):** Break them, old man. We've a drain to clear. _[Farewell]_

**Old Ulf:** _(node u6)_ Oskar saw the whole drawings; I only ever saw the parts. If that lad kept quiet, it was the orc made him. Don't you blame him.

  - **Party (choice):** Break them, old man. We've a drain to clear. _[Farewell]_

_Quest: The Good Ones (Investigate): Bring Old Ulf three casting moulds from the shelves of the Mould Room_

**Old Ulf:** _(quest offer)_ Three patterns off the shelves, that's all. The hall, the hole behind it, and the one they keep by the stair. Don't drop them; a cracked mould pours crooked.

**Old Ulf:** _(quest accept)_ Good. Flat, mind, and keep your thumbs out of the sand.

**Old Ulf:** _(quest decline)_ Suit yourselves. They'll get poured whether you look at them or not. Everything here does.

**Old Ulf:** _(quest complete)_ A crushing wall. A spike bed. The orc himself, larger. I poured them all. Help me break them.

**Old Ulf:** _(quest fail)_ Left them lying, did you. Well. There's plenty more on the shelves.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ More of the town in my cages, hirelings, and my old founder minding his shelves next door. Free them if you like. He won't look up; he never does.

**Morg Ashtusk:** _(Descend)_ So now you've seen my shelves, hirelings: the walls, the spikes, the iron, all mine. My founder poured every one with his bad eyes, and the big mould by the stair is me.

**Morg Ashtusk:** _(AllFreed, jailbreak)_ So my cages stand empty. Now ask my founder's lad why he kept his bloody mouth shut while the old man poured my iron. I said I'd break the old man's neck if the lad talked.

### Hordes

**Narrator:** _(The Spoiled Casting, Enter)_ The wall behind the shelves gives way and the cinders pour through, grey and smouldering: townsfolk the orc finished turning.

## Floor 14: The Quench

_Mood: rusted grates under black water, vestments afloat_

_Objective: shiftingvault_

**Story beat:** Flooded vault. cast_tobit has followed the party down and found the Verger's vestments thrown off in the water. With told_tobit he asks plainly whether the Verger did this and wants proof. Without it he believes his master is the demon's prisoner, summoned below by a message in the Verger's hand to help with a rite that will save the town. Pressed, he flees to the escort the Herald sent for him: the Betrayal encounter is that escort, the party fights it, not him, and he is alive afterwards and goes on down. Let be, he goes on down to the summons quietly. Either way he is taken for the rite (16, 19).

**Narrator:** _(floor intro)_ Black water stands knee-deep over the quench grates, where the orc's iron is cooled. A churchman's robes float against a grate, and the stairs lie behind gates that move.

**Narrator:** _(RoomEnter room 8)_ Church vestments float against the grate, black and gold, thrown off in the water: the Verger's. Beside them a young deacon stands soaked to the waist.

**Narrator:** _(QuestDone)_ The orc's escort lies broken in the water, and the deacon is not among them. He ran on down to his master's summons.

### Deacon Tobit Hale _(needs told_tobit)_

**Deacon Tobit Hale:** _(node tob_a)_ I followed you down; I had to know. Those are my master's vestments in the water, thrown off like rags. Tell me plainly: did the Verger do this to the town?

  - **Party (choice):** His order kept the wardens out. That much is true. _[to tob_b]_
  - **Party (choice):** We don't know yet, deacon. _[to tob_b]_
  - **Party (choice):** Go back up, deacon. _[Farewell]_
  - **Party (choice):** Your psalter, deacon. _[TurnIn; to tob_d]_

**Deacon Tobit Hale:** _(node tob_b)_ Then I won't accuse him without proof, and I mean to be thorough. I've written down all I've found in my psalter, and I dropped it in the font room when the cinders came. Will you fetch it?

  - **Party (choice):** We'll fetch it. _[AcceptQuest; to tob_c]_
  - **Party (choice):** Fetch it yourself, deacon. _[DeclineQuest]_

**Deacon Tobit Hale:** _(node tob_c)_ Thank you. It's by the font, where the wall broke open. The cinders came out of the breach and I ran. Mind them.

  - **Party (choice):** Here's your psalter. _[TurnIn; to tob_d]_
  - **Party (choice):** Stay by the grate. We'll be back. _[Farewell]_

**Deacon Tobit Hale:** _(node tob_d)_ Thank you. I'll write it in: his vestments, in the water, thrown off. If he did this, it will be in his own hand somewhere, and I'll find it. I'm thorough. I always am.

  - **Party (choice):** Keep your head down, deacon. _[Farewell]_

_Quest: The Deacon's Psalter (Recover): Recover Tobit's psalter from the breached font room_

**Deacon Tobit Hale:** _(quest offer)_ Small black book, water-swollen, dropped by the font where the wall is broken. Everything I've found is written in it, in my own bad hand.

**Deacon Tobit Hale:** _(quest accept)_ Thank you. Mind the cinders in there; they come out of the wall like wasps out of a loaf.

**Deacon Tobit Hale:** _(quest decline)_ No. Quite right. I dropped it; I should fetch it. I only hoped you'd be quicker.

**Deacon Tobit Hale:** _(quest complete)_ My psalter. I'll write the vestments in, and then I'll find the rest in his own hand.

**Deacon Tobit Hale:** _(quest fail)_ Then it stays in the water, and I start again from memory. I have a good memory.

### Deacon Tobit Hale _(not if told_tobit)_

**Deacon Tobit Hale:** _(node bet_a)_ You're here too! Those are my master's vestments in the water. He's alive, though; he's sent for me. The orc holds him prisoner, doesn't it? Tell me true.

  - **Party (choice):** We don't know, deacon. What did he send? _[to bet_b]_
  - **Party (choice):** We can't say. Don't go. _[to bet_b]_

**Deacon Tobit Hale:** _(node bet_b)_ A note came to me at the church, in his own hand: 'Come below and help me with a rite that will save the town.' His escort waits for me by the grate. I'm going to him.

  - **Party (choice):** You're not going anywhere, deacon. _[Betray]_
  - **Party (choice):** Go to him, then. _[Farewell]_

_Quest: The Deacon's Escort (Betrayal): Put down the escort the orc sent to fetch the deacon_

**Deacon Tobit Hale:** _(quest offer)_ He's sent for me. A rite to save the town, in his own hand. I'm going to him.

**Deacon Tobit Hale:** _(quest accept)_ I'm going to him, and you won't stop me. Those are his escort; don't follow.

**Deacon Tobit Hale:** _(quest decline)_ Thank you. Pray for me, if you pray.

**Deacon Tobit Hale:** _(quest complete)_ His escort lies in the water. The deacon is gone on down to his master.

**Deacon Tobit Hale:** _(quest fail)_ I'm going to him. Don't follow.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, shiftingvault)_ Your deacon followed you down, hirelings. He's in my water, fishing out his master's robes. The stairs are behind my gates; pull the bloody levers and guess.

**Morg Ashtusk:** _(WrongLever, shiftingvault)_ Wrong lever. I built these gates to fool better heads than yours, and they're doing it for free.

**Morg Ashtusk:** _(WrongLever, shiftingvault)_ Wrong again, hirelings. The town pays you by the floor, not by the lever.

**Morg Ashtusk:** _(GateShift, shiftingvault)_ Feel that? My gates have moved. Did you think I'd leave the way down standing still for hired help?

### Hordes

**Narrator:** _(The Font Spill, Chest)_ The lid lifts, the water hisses, and the breaches in the font wall cough out cinders that walk.

**Narrator:** _(The Grate Pack, Alarm)_ Something small and fast reaches the vault, and behind you the grate room empties itself into the flood.

## Floor 15: The Bellows

_Mood: red-hot air, an engine casting instead of pumping_

**Story beat:** Boss: the Bellows, the great engine that once pumped the drains and now casts minions, guards the third great drain; fighter_oskar knows its weak points. When it stops the party turns the sluice wheel and the third drain runs; the narrator makes plain what it did: the heat goes out of the works, the shaking above eases, and the god drinks.

**Narrator:** _(floor intro)_ Red-hot air, and a thump that shakes the floor plates. Ahead, the biggest engine in the works is still running, and under it the third great drain lies choked.

**Narrator:** _(BossSeen)_ The Bellows heaves over the choked drain, and fresh iron drops hissing from its ladles to come at you. Stop the engine and the drain can run.

**Narrator:** _(BossDead)_ You turn the sluice wheel under the dead engine, and the third great drain runs. The heat goes out of the works, the shaking above you eases, and the god drinks. One great drain left.

### Floor taunts

**Morg Ashtusk:** _(Descend)_ Three drains, hirelings. Count your bloody wages. My foundry finished me before you broke it: I am whole again, larger, and waiting for you at the last drain.

### Hordes

**Narrator:** _(The Cinder Spill, Enter)_ The wall splits along a seam of soot and the cinders pour out at you, burning, with the big ones stooping behind. They were Dunmarrow's, once.

### Boss: The Bellows

**The Bellows:** _(intro)_ The great engine of the pump works, which pushed the water down the third drain for three hundred years. The orc set it to pouring his minions instead.

**The Bellows:** _(death)_ Its last stroke comes down on an empty ladle, and the orc's great engine does not lift again.

## Floor 16: The Dream Door

_Mood: veined marble, blue light, the dream wearing thin_

**Story beat:** The dream wears thin here and the Sleepless's own kind come through. clue_letter, the Verger's letter to the Herald: 'The way will be open by midwinter.' With told_tobit cast_tobit brings it himself and turns on his master, wanting him stopped; without it cast_maud carries it (it came down to her on the water) and says the deacon has been taken for the rite. fighter_ione, ashamed of what the Verger has done to her church, joins to take back the church's silver ewer.

**Narrator:** _(tier 4 entry)_ This close to the sleeping god, the dream wears thin. It is the only door between this world and the Sleepless, and things from the other side are pressing through. The Dream.

**Narrator:** _(floor intro)_ Blue light runs in the veins of the marble, and the god sleeps close beneath it. Near the foot of the stair, a young sister of Gorm's church is keeping a lantern lit.

**Narrator:** _(RoomEnter room 5)_ Deacon Tobit stands among the fallen pillars, soaked from the climb down, with a letter in his hand. He has plainly read it more than once.

**Narrator:** _(RoomEnter room 9)_ Maud Sorrel stands in the weeping font with water running from her sleeves, holding a wet letter out to you as if it were nothing much.

### Deacon Tobit Hale _(needs told_tobit)_

**Deacon Tobit Hale:** _(node t1)_ I found it. My master's letter to the orc, in his own hand: 'The way will be open by midwinter.' He opened the crypt door to that thing. I want him stopped.

  - **Party (choice):** What do you need from us? _[to t2]_
  - **Party (choice):** Your master's letters, deacon. _[TurnIn; to t4]_
  - **Party (choice):** Stay behind us, deacon. _[Farewell]_

**Deacon Tobit Hale:** _(node t2)_ There are more of his letters in these halls, dropped where the orc's creatures came through. Bring them to me. Before anyone stops him, I want to know why he did it.

  - **Party (choice):** We'll find them. _[AcceptQuest; to t3]_
  - **Party (choice):** Find them yourself, deacon. _[DeclineQuest]_
  - **Party (choice):** Here. Every letter we found. _[TurnIn; to t4]_
  - **Party (choice):** Later, deacon. _[Farewell]_

**Deacon Tobit Hale:** _(node t3)_ Thank you. Mind the marble where it's cracked; things come through the cracks down here.

  - **Party (choice):** Keep your head down, deacon. _[Farewell]_

**Deacon Tobit Hale:** _(node t4)_ 'You promised the god who comes through will reward the man who opens the door. Forty years Gorm never once answered me.' That's why. He wanted a god who answers.

  - **Party (choice):** Then we'll stop him. _[to t5]_

**Deacon Tobit Hale:** _(node t5)_ Thank you. I'll take his letters up to the town, so nobody can say it was a lie. I was thorough. I always am.

  - **Party (choice):** Go carefully, deacon. _[Farewell]_

_Quest: The Verger's Letters (Investigate): Gather the Verger's letters from the marble halls and bring them to Tobit._

**Deacon Tobit Hale:** _(quest offer)_ More of my master's letters are lying in these halls. Bring them to me and I'll read them; I'm thorough. I always am.

**Deacon Tobit Hale:** _(quest accept)_ Thank you. Mind the marble where it's cracked; things come through the cracks down here.

**Deacon Tobit Hale:** _(quest decline)_ No. Quite right. I'll look for them myself, slowly.

**Deacon Tobit Hale:** _(quest complete)_ His letters, every one. Now I know why he did it.

**Deacon Tobit Hale:** _(quest fail)_ Then I'll never know why. I'll have to make do with knowing that he did.

### Maud Sorrel _(not if told_tobit)_

**Maud Sorrel:** _(node m1)_ There you are, dears. Keep the water going down to Gorm; that's all I want. And here: this came down to me on the water. The Verger's letter to the orc: 'The way will be open by midwinter.'

  - **Party (choice):** Where's the deacon, Maud? _[to m2]_
  - **Party (choice):** What else came down the water? _[to m3]_
  - **Party (choice):** The Verger's letters, Maud. _[TurnIn; to m4]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m2)_ Taken for the rite, dear. His master sent for him, and he went down gladly. The Verger's the one who opened the crypt door to the orc, and he means to wake the god.

  - **Party (choice):** What else came down the water? _[to m3]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m3)_ More of his letters, dear, spread through these halls by the water. Bring them to me. The deacon would want to know why his master did it, and so would I.

  - **Party (choice):** We'll fetch them. _[AcceptQuest; to m5]_
  - **Party (choice):** Fetch them yourself, Maud. _[DeclineQuest]_
  - **Party (choice):** Here. Every letter we found. _[TurnIn; to m4]_
  - **Party (choice):** Later, Maud. _[Farewell]_

**Maud Sorrel:** _(node m4)_ 'You promised the god who comes through will reward the man who opens the door. Forty years Gorm never once answered me.' That's why, dear. He took the first answer that came.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m5)_ Good. Mind the marble where it's cracked, dears; things come through the cracks down here.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

_Quest: What the Water Brought (Investigate): Gather the Verger's letters from the marble halls and bring them to Maud._

**Maud Sorrel:** _(quest offer)_ More of the Verger's letters, dear, wherever the water left them. Bring them to me.

**Maud Sorrel:** _(quest accept)_ Good. Mind the marble where it's cracked, dears.

**Maud Sorrel:** _(quest decline)_ As you like, dear. The water keeps what it's given.

**Maud Sorrel:** _(quest complete)_ All his letters. Now we know why.

**Maud Sorrel:** _(quest fail)_ Gone to pulp in the wet, then. No matter, dear. We know what he did.

### Floor taunts

**Morg Ashtusk:** _(Descend)_ Your deacon found my letter from the Verger, hirelings: 'The way will be open by midwinter.' Bloody priests always write it down. The Verger opened the crypt door for me in spring.

**Morg Ashtusk:** _(Descend)_ Read my letter from the Verger, hirelings? 'The way will be open by midwinter.' Bloody priests always write it down. The Verger opened the crypt door for me, and now his deacon kneels in his rite.

### Hordes

**Narrator:** _(The Door Gives Way, Enter)_ The marble splits along its blue veins and the shades pour through at you, grey and alike, with the stalkers from the other side behind them.

## Floor 17: The Windpipe

_Mood: purple gloom, gas climbing, an alderman with a shovel_

_Objective: toxic_

**Story beat:** Gas climbs the galleries. cast_crane has come down himself with a shovel and owns the truth: the council was glad of the Verger's order, because stopping the wardens' pay saved money, and he signed it. He holds a sluice open so the party can get past the gas, and stays holding it; he does not betray them. With grisel_forgiven he can say she and her crew are back on the upper drains.

**Narrator:** _(floor intro)_ Purple slate, and the gas climbing the galleries off the standing water. Somewhere ahead a sluice wheel groans, and a man out of breath is apologising to it.

**Narrator:** _(RoomEnter room 9)_ In the Long Gallery the alderman hangs his whole weight on a sluice wheel, a shovel jammed through the spokes to hold it. The water drains off through the gate, and past it the gas is thinner.

### Alderman Hesketh Crane

**Alderman Hesketh Crane:** _(node n_meet)_ There you are. I came down myself, shovel and all. Go on through while I hold this sluice open: the water's running off, and the gas is thinner past the gate.

  - **Party (choice):** Why come down yourself, Crane? _[to n_confess]_
  - **Party (choice):** The Verger let the orc in. We read his letter. _[to n_verger]_
  - **Party (choice):** Did Grisel get her crew out? _[needs grisel_forgiven; to n_grisel]_
  - **Party (choice):** Hold it, then. We're going. _[Farewell]_

**Alderman Hesketh Crane:** _(node n_confess)_ Because I owe it. We were glad of the Verger's order. Stopping the wardens' pay saved the council money, and I signed it. That's what I didn't tell you at the cistern.

  - **Party (choice):** The Verger let the orc in. We read his letter. _[to n_verger]_
  - **Party (choice):** Did Grisel get her crew out? _[needs grisel_forgiven; to n_grisel]_
  - **Party (choice):** Hold it, then. We're going. _[Farewell]_

**Alderman Hesketh Crane:** _(node n_verger)_ Then he opened the door to that orc, and I stopped paying the wardens who'd have found it. He did it for a god and I did it for the wages. I can't tell which of us I'm angrier with.

  - **Party (choice):** Why come down yourself, Crane? _[to n_confess]_
  - **Party (choice):** Did Grisel get her crew out? _[needs grisel_forgiven; to n_grisel]_
  - **Party (choice):** Hold it, then. We're going. _[Farewell]_

**Alderman Hesketh Crane:** _(node n_grisel)_ She did. Grisel Thorn and what's left of her crew are back on the upper drains, on full pay back to spring. I signed that too, and gladly.

  - **Party (choice):** Why come down yourself, Crane? _[to n_confess]_
  - **Party (choice):** Hold it, then. We're going. _[Farewell]_

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, toxic)_ Smell that, hirelings? The water stands all the way down now, and the gas comes off it. Your alderman came down after you with a bloody shovel. Hurry, before he hurts himself.

**Morg Ashtusk:** _(GasLethal, toxic)_ That's my air in your lungs now, {party}. Your alderman's holding a sluice open for you, and you're still too bloody slow to use it.

**Morg Ashtusk:** _(Descend)_ Your alderman's still hanging off his wheel up there, hirelings. His council saved a few months of wardens' wages and left my drains to me. Cheapest bloody bargain my master ever got.

### Hordes

**Narrator:** _(The Drift Shades, Chest)_ The lid lifts and the timbering splits: shades spill out of every seam, grey and alike. Some of them were wardens this spring.

**Narrator:** _(The Wheezing Shades, Alarm)_ The runner's cry goes down the gallery, and the breach answers it: shades, and the archers of the other side behind them.

## Floor 18: The Dreaming Hall

_Mood: violet dark, one corridor in, a hungry god speaking_

**Story beat:** Payoff: cast_maud sits down and Gorm speaks through her, starving, half awake and sorry for the damage, asking for water and for sleep and saying plainly what his waking would let in. Decide how much to ask of him: 'Lend us what strength you have' (flag wake_gorm: he strikes at the Herald in his sleep, but the dream thins) or 'Sleep. We will finish it' (flag let_him_dream).

**Narrator:** _(floor intro)_ Violet dark, and one corridor into a round hall where the shaking is worst. Maud Sorrel has stopped walking. She sits on the floor with her hands folded in her lap, waiting for you.

**Narrator:** _(RoomEnter room 0)_ When Maud looks up, it is not Maud looking. Gorm, the god under Dunmarrow, is close enough here to speak through her, and each word comes slowly.

### Maud Sorrel

**Maud Sorrel:** _(node n_sit)_ I am Gorm; Maud has lent me her mouth. Forgive the shaking and the people hurt. I am starving and half awake, and my hunger will bring the mines down on Dunmarrow. Send the water down, and let me sleep.

  - **Party (choice):** What happens if you wake? _[to n_wake]_
  - **Party (choice):** Who did this to you? _[to n_verger]_
  - **Party (choice):** What do you want from us? _[to n_choose]_
  - **Party (choice):** Not yet. We'll come back. _[Farewell]_

**Maud Sorrel:** _(node n_wake)_ My dream is the door that keeps the Sleepless out. If I wake, the door opens, and Dunmarrow is the first thing the Sleepless finds. I would not mean it. It would happen all the same.

  - **Party (choice):** Who did this to you? _[to n_verger]_
  - **Party (choice):** What do you want from us? _[to n_choose]_

**Maud Sorrel:** _(node n_verger)_ My Verger prayed to me for forty years, and I was asleep and could not answer. The orc's master answered him instead. So the Verger opened the crypt door, and now he is below you, at a rite to wake me.

  - **Party (choice):** What happens if you wake? _[to n_wake]_
  - **Party (choice):** What do you want from us? _[to n_choose]_

**Maud Sorrel:** _(node n_choose)_ Ask nothing, and I sleep deep while you face the orc alone. Ask for my strength, and I strike at him, but I come nearer waking; if I wake, the Sleepless comes through and Dunmarrow dies. Which?

  - **Party (choice):** Sleep. We will finish it. _[Choose; sets let_him_dream; to n_dream]_
  - **Party (choice):** Lend us what strength you have. _[Choose; sets wake_gorm; to n_lend]_

**Maud Sorrel:** _(node n_dream)_ He's gone back down, dear, deep and quiet. Good. I'll go on ahead to the shrine below and hold him there against the Verger's rite. You keep the Sleepless's kind off me.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node n_lend)_ He heard you, dear. He'll reach for that orc from his sleep when you need it, but he's nearer waking now. I'll go ahead to the shrine below and hold him steady. Keep the Sleepless's kind off me.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

_Quest: What to Ask of Gorm (Decide): Tell Gorm how much to ask of him_

**Maud Sorrel:** _(quest offer)_ Ask nothing of me, or ask for my strength. Either way, bring the water down.

**Maud Sorrel:** _(quest accept)_ Said, then. Carry it down with you.

**Maud Sorrel:** _(quest decline)_ Then sit a while. I have slept three hundred years; I can wait a little longer.

**Maud Sorrel:** _(quest complete)_ Thank you for coming down to me.

**Maud Sorrel:** _(quest fail)_ No matter. Bring the water down, and I will sleep either way.

### Floor taunts

**Morg Ashtusk:** _(Descend)_ Your god is half awake, hirelings, and the dream is thin as paper. I can hear my master on the other side of it. You did that for him, and nobody paid you a bloody penny.

**Morg Ashtusk:** _(Descend)_ You told your god to go back to sleep, hirelings. A god too bloody frightened to wake. My master is frightened of nothing, and he is still at the door.

### Hordes

**Narrator:** _(The Near Mouth, Chest)_ The lid lifts and the wall behind it lets go: shades pour out of the breaches like cold poured from a jug, and the corridor into the rotunda fills from end to end.

**Narrator:** _(The Far Mouth, Enter)_ Two breaches open in the marble together, and the Sleepless's kind come through at you: shades in front, and the archers of the other side behind.

## Floor 19: The Last Station

_Mood: cold tiles, three galleries, a rite to wake a god_

_Objective: ritual_

**Story beat:** Ritual: cast_quill, the Verger, conducts the rite to wake the god with the church's ewer in his hands, while cast_maud steadies the god against it and the party holds off the waves around her; he speaks to the party but is not fought, and fighter_ione faces him. Without told_tobit, cast_tobit is in the rite, half-changed, and breaking it lets him go. With wake_gorm the shaking is worse and more of the Sleepless's kind come through. When the rite breaks the Verger is left kneeling with nothing answering him.

**Narrator:** _(floor intro)_ Three cold galleries meet at a shrine of white tile. Somewhere ahead the Verger is at his rite to wake the god, and the floor shakes in time with his prayer.

**Narrator:** _(RoomEnter room 1)_ Maud Sorrel sits at the altar with her hands flat on the stone, holding the god asleep. From the next gallery comes the Verger's voice, praying for the god to wake. Keep the Sleepless's kind off her.

**Narrator:** _(RoomEnter room 1)_ Forty years the Verger prayed to Gorm and was never answered. The orc promised him a god who would answer, and for that the Verger opened the crypt door. Now he prays that god through.

**Narrator:** _(RoomEnter room 1)_ The floor here will not stop shaking. Gorm is nearer waking for the strength he promised you, and more of the Sleepless's kind are getting through.

**Narrator:** _(RoomEnter room 1)_ Among the voices at the Verger's rite is Deacon Tobit's, slow and grey: the rite is turning him, and he repeats his master's every word. Break it, and it lets him go.

**Narrator:** _(RoomEnter room 7)_ Absalom Quill, the Verger, kneels here with the church's silver ewer in his hands, praying aloud for the god to wake. He is the man who opened the crypt door to the orc.

**Narrator:** _(RoomEnter room 7)_ Deacon Tobit kneels at his master's side, grey to the wrists and half turned, his eyes on the ewer.

### Maud Sorrel

**Maud Sorrel:** _(node m_open)_ There you are, dears. I'll sit at the altar and hold him asleep against the Verger's rite. You keep the Sleepless's kind off me while I do.

  - **Party (choice):** We'll hold them off, Maud. _[AcceptQuest]_
  - **Party (choice):** Why does he need holding? _[to m_which]_
  - **Party (choice):** Not yet, Maud. _[DeclineQuest]_
  - **Party (choice):** It's done, Maud. _[TurnIn; to m_done]_

**Maud Sorrel:** _(node m_which)_ The Verger is praying him awake, dear, and the orc's kind are helping. I can keep him asleep if nobody pulls me off the stone.

  - **Party (choice):** We asked for his strength. Hold him anyway. _[AcceptQuest; needs wake_gorm]_
  - **Party (choice):** We let him sleep. Keep him there. _[AcceptQuest; not if wake_gorm]_
  - **Party (choice):** Rest a moment, Maud. _[Farewell]_

**Maud Sorrel:** _(node m_done)_ He's settled, dear. The Verger's rite has nothing left to pull on. Go on down to the last drain; he's thirsty.

  - **Party (choice):** Then we go down. _[TurnIn]_

_Quest: Hold Him Steady (HoldOut): Hold the three galleries while Maud Sorrel keeps the god asleep._

**Maud Sorrel:** _(quest offer)_ Three galleries and one old woman at the altar, dears. Keep them off me until he settles.

**Maud Sorrel:** _(quest accept)_ Good. Don't let them pull me off the stone.

**Maud Sorrel:** _(quest decline)_ Then I'll hold him as long as I can on my own, dear.

**Maud Sorrel:** _(quest complete)_ There. He's settled, and the rite has nothing left to pull on.

**Maud Sorrel:** _(quest fail)_ I'm down, dears. Pick me up, or the Verger's rite gets its way.

### Absalom Quill

**Absalom Quill:** _(node v1)_ Do keep your voices down; this is a rite. Forty years I swept Gorm's church, and he never once answered me. Something else has. Stand aside and let him wake.

  - **Party (choice):** You let the orc in. _[to v_door]_
  - **Party (choice):** Tobit found your letter. _[needs told_tobit; to v_letter_t]_
  - **Party (choice):** We read your letter. _[not if told_tobit; to v_letter]_
  - **Party (choice):** Let the deacon go. _[not if told_tobit; to v_tobit]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_door)_ I opened a door; I did not walk through it. My guest below promised that the god who comes through will reward the man who opened it. Gorm never rewarded anyone for anything.

  - **Party (choice):** If Gorm wakes, the town dies. _[to v_town]_
  - **Party (choice):** Give back the church's ewer. _[to v_ewer]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_town)_ You will not frighten me with Dunmarrow. I have swept up after that town for forty years, and the god who comes after will find me standing in it, answered at last.

  - **Party (choice):** Gorm lent us his strength. _[needs wake_gorm; to v_wake]_
  - **Party (choice):** Gorm is going back to sleep. _[not if wake_gorm; to v_sleep]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_wake)_ Did he? Then he is nearer waking than my rite could bring him, and you have done my work for me. Thank you. I shall mention you to the god who comes after.

  - **Party (choice):** Give back the church's ewer. _[to v_ewer]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_sleep)_ He will sleep while that drowned warden holds him, and not a moment longer. Maud Sorrel kept his drains. I kept the church's crypt door, and I opened it.

  - **Party (choice):** Give back the church's ewer. _[to v_ewer]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_letter)_ Read aloud, I expect, in my own church's halls. 'The way will be open by midwinter.' Yes, I wrote it, and I would write it again. I only ask that you stop reading my letters to strangers.

  - **Party (choice):** You let the orc in. _[to v_door]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_letter_t)_ Tobit. Of course. I taught him to be thorough, and he was. 'The way will be open by midwinter.' I wrote it, and I would write it again. I only wish he had not read it aloud.

  - **Party (choice):** You let the orc in. _[to v_door]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_tobit)_ I sent for Tobit and he came, as a good deacon should. He is my deacon. I do not let go of what is mine, and nor, I find, does the rite.

  - **Party (choice):** You let the orc in. _[to v_door]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

**Absalom Quill:** _(node v_ewer)_ That ewer blessed the drains every spring for three hundred years. Tonight it pours for the god who comes after. Sister Ione may have it back when the new god has come.

  - **Party (choice):** You let the orc in. _[to v_door]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

### Floor taunts

**Morg Ashtusk:** _(RitualStart, ritual)_ Go on, warden, hold your god steady. He's half awake already, thanks to the town's hirelings, and every bloody wave I send shakes him further.

**Morg Ashtusk:** _(RitualStart, ritual)_ So a drowned warden thinks she can hold a god asleep against my priest's rite. Bloody touching. Send them in.

**Morg Ashtusk:** _(FloorEnter, ritual)_ Feel that, hirelings? Your god, turning over. You asked him for his strength, and he's halfway awake giving it. The old priest only has to finish the bloody job.

**Morg Ashtusk:** _(FloorEnter, ritual)_ The old priest is down here praying your god awake, hirelings, and a drowned warden is trying to keep him asleep. I know which one I'd bloody bet on.

**Morg Ashtusk:** _(RitualistDown, ritual)_ Down goes the warden. Pick her up, hirelings, or the old priest gets his god.

**Morg Ashtusk:** _(RitualDone, ritual)_ So the rite breaks. Look at the old priest, hirelings: on his knees, and nothing answering him. Forty years he prayed, and he still can't get a god to come when called.

**Morg Ashtusk:** _(Descend)_ So the rite broke, hirelings. The old priest is on his knees with nothing answering, and the church's ewer lies where he dropped it. Sister Ione can have her jug back. I'm at the last drain.

**Morg Ashtusk:** _(Descend)_ So the rite broke, hirelings, and your half-grey deacon walked out of it. The old priest dropped the church's ewer to kneel, and nothing answers him. Sister Ione can have her jug back.

### Hordes

**Narrator:** _(The Breath of the Mouths, Enter)_ The three mouths in the tilework exhale at once, and the dark comes out of them on its hands and knees.

**Narrator:** _(What the Oddfellows Dug Into, Chest)_ The lid lifts, and the wall behind the old camp gives way: shades come crawling out at you, grey and alike, with a caller of the other side behind them.

## Floor 20: The Mouth

_Mood: the last great drain, braziers, the orc again_

**Story beat:** Boss: the Herald, Morg Ashtusk, recast whole and larger in his true form, waits at the last great drain and speaks of the Verger with contempt. Kill him, turn the sluice wheel, and the water runs down to Gorm; the narrator makes plain what it did: the god drinks and sinks back from waking, the dream holds, the ground goes still and the wells of Dunmarrow run sweet. With wake_gorm, Gorm, half awake, strikes at him and he dies saying his master is almost through; without it he dies contemptuous, saying the Sleepless will send another. viewer_foehammer finishes the job.

**Narrator:** _(floor intro)_ The last stair brings you down to the Mouth, where the last great drain should pour down to the god. Its grate is packed solid, braziers burn around it, and the orc is waiting there.

**Narrator:** _(BossSeen)_ Morg Ashtusk stands over the last great drain, not the orc body you killed at the gate but what the foundry was building him into: his true form. Kill him and turn the sluice wheel, or the mines come down on Dunmarrow.

**Narrator:** _(BossDead)_ You turn the sluice wheel, and the last great drain runs. The water pours down to the god, and he drinks and slowly sinks back from waking. The dream holds; it is thin, but the door stays shut.

**Narrator:** _(BossDead)_ You turn the sluice wheel, and the last great drain runs. The water pours down to the god, and he drinks without ever stirring. The dream holds whole, and the door to the Sleepless stays shut.

**Narrator:** _(BossDead)_ Far above you the ground goes still, and the wells of Dunmarrow run sweet. The mines will stand. One floor up the Verger kneels on, and nothing answers him. The job is done, and the town lives.

### Floor taunts

**Morg Ashtusk:** _(Descend)_ Come down to the Mouth, hirelings. The last drain is mine.

**Morg Ashtusk:** _(FloorEnter)_ The Mouth, hirelings. The last drain, and your god right under your feet. Mind where you stand.

**Morg Ashtusk:** _(MemberDowned)_ That's one of the town's hired shovels broken. Nobody will pay you for it.

**Morg Ashtusk:** _(Idle)_ Standing about, hirelings? You're paid by the floor, and this is the last one. Come and earn it.

### Hordes

**Narrator:** _(The Gallery Shades, Enter)_ The gallery wall splits between the pillars, and the shades pour out at you in one long grey rush, with the stalkers of the other side behind them.

**Narrator:** _(The Breach Shades, Alarm)_ Somewhere behind you a runner reaches the breach, and it answers: shade on shade, climbing out of the stone with archers at their backs.

### Boss: Morg Ashtusk

**Morg Ashtusk:** _(intro)_ Killed me at the gate, didn't you? I came back bigger. Your Verger is still upstairs on his knees, waiting for his reward. Priests. Turn that sluice wheel if you can reach it, hirelings.

**Morg Ashtusk:** _(half)_ Is this what the town paid for? Your god is hiding in his sleep, hirelings, and you are only day labour. I can do this all night.

**Morg Ashtusk:** _(althalf, if wake_gorm)_ That was your god. He reached up out of his sleep and struck me. Good. Every blow he lands wakes him further, and my master is right behind the door.

**Morg Ashtusk:** _(death)_ Turn your wheel, then. My master will send another. Your god has to sleep for ever, hirelings. My master only has to wait.

**Morg Ashtusk:** _(altdeath, if wake_gorm)_ Your god struck me down from his sleep. Ha. It cost him, hirelings: he's nearly awake, and my master is almost through. Listen at the door.

## Campaign lines

### Campaign taunts (Morg Ashtusk)

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ Floor {floor}, and my cages are full: townsfolk, going grey a little more each day. Open them if you can. The finished ones already run with my bloody packs.

**Morg Ashtusk:** _(FloorEnter, toxic)_ Floor {floor} stinks. That's what my choked drains do to standing water. The stairs are at the bottom of the bloody stink. Hold your breath, hirelings.

**Morg Ashtusk:** _(FloorEnter, ritual)_ Floor {floor}. Someone is at work at the shrine, {party}, trying to undo what I've done. My minions are coming for her. Stand in the bloody way if you like.

**Morg Ashtusk:** _(FloorEnter, shiftingvault)_ The stairs on floor {floor} sit behind my gates, and only my levers open them. Pull and guess. I'm in no hurry; my master has waited a long time for these mines.

**Morg Ashtusk:** _(Capture)_ Caught. Back to the cages, and back to turning. You can't hold on to anything, can you?

**Morg Ashtusk:** _(Capture, jailbreak)_ {prisoner} goes back in my cage, and the turning picks up where it stopped. Some rescue. You'll have to do bloody better than that.

**Morg Ashtusk:** _(Freed)_ One out of my cages. Enjoy it. I have more cages than you have time.

**Morg Ashtusk:** _(Freed, jailbreak)_ Out you come, {prisoner}. Run home to your frightened town. It never came down for you itself; it hired these sods to do it.

**Morg Ashtusk:** _(AllFreed)_ All out. Fine. Everyone who comes down after the drains walks into my cages in the end. I'll fill them again.

**Morg Ashtusk:** _(AllFreed, jailbreak)_ All my cages empty. Bloody good work, hirelings. It changes nothing: the drains are still mine, and your god is still hungry.

**Morg Ashtusk:** _(GasThickens, toxic)_ It's rising. That's the air off my choked drains, and it doesn't care who hired you.

**Morg Ashtusk:** _(GasThickens, toxic)_ Higher now. Choke on it, you poor sods. I choked those drains on purpose, and this is what standing water does.

**Morg Ashtusk:** _(GasLethal, toxic)_ That's the foul air of my drains in your lungs now. Lie down in it. It comes to the same thing, only sooner.

**Morg Ashtusk:** _(GasLethal, toxic)_ Deep enough to drown in, {party}. Kneel in it. Your frightened town won't pay for the bloody funeral.

**Morg Ashtusk:** _(WrongLever, shiftingvault)_ Wrong one. Keep pulling, hirelings. I set those gates to keep the whole town out, and they'll keep you out too.

**Morg Ashtusk:** _(WrongLever, shiftingvault)_ Wrong. Did the town hire you for your heads, or only for your backs?

**Morg Ashtusk:** _(GateShift, shiftingvault)_ Hear that? My gates have moved. The way down is wherever I say it is.

**Morg Ashtusk:** _(GateShift, shiftingvault)_ The gates have shifted. You won't out-think iron I set, hirelings, and nobody paid you enough to try.

**Morg Ashtusk:** _(VaultOpened)_ So you found the right levers. Bloody hell. Go on down, then; it's all mine further down.

**Morg Ashtusk:** _(VaultOpened)_ The gate is open. Take the stairs, and whatever you found by them. It all belongs to my master; you are only borrowing it.

**Morg Ashtusk:** _(RitualStart, ritual)_ So she's started her work at the shrine. Good. My minions have been waiting for somebody to stand still. Guard her, hirelings, if you think you can.

**Morg Ashtusk:** _(RitualStart, ritual)_ One woman at a shrine, working against everything I've done down here. I'll send everything I have at her. Hold the bloody line if you can.

**Morg Ashtusk:** _(RitualistDown, ritual)_ She's down. Leave her lying. Her work stops, and mine goes on.

**Morg Ashtusk:** _(RitualistDown, ritual)_ There she goes, flat on the stones. Pick her up or don't, hirelings; my minions won't wait for you to decide.

**Morg Ashtusk:** _(WaveBreak, ritual)_ My minions fall back. Breathe while you can. There are more where they came from, and they never tire.

**Morg Ashtusk:** _(WaveBreak)_ A lull. Count your wounds and I'll count my minions. Then I'll send the rest. I have a foundry; you have a bloody fee.

**Morg Ashtusk:** _(RitualDone, ritual)_ So her work is done and my minions are broken. Enjoy it. The drains below are still mine, and so are the bloody stairs down to them.

**Morg Ashtusk:** _(RitualDone, ritual)_ Finished, then. One woman at one shrine, and it took all of you to guard her. Your god is still starving, hirelings.

**Morg Ashtusk:** _(ObjectiveDone)_ Done, and done badly, but done. Go on down. The next floor is worse; I set its traps myself.

**Morg Ashtusk:** _(ObjectiveDone)_ Fine. Take the stairs. One more floor of mine behind you. Every one below is mine too, hirelings, and so is the last drain.

**Morg Ashtusk:** _(Idle)_ Resting? Take your time. Every minute you stand there, your god gets hungrier and nearer waking.

**Morg Ashtusk:** _(Idle)_ Admiring my work, hirelings? Take a good look. These are my master's mines now, and I keep them the way he likes.

**Morg Ashtusk:** _(MemberDowned)_ One down. Lie still, and don't make a bloody fuss. Nobody in that frightened town will come down for you.

**Morg Ashtusk:** _(MemberDowned)_ One of you down, {party}. Is that what the town hired? I've seen more fight in my raised dead.

**Morg Ashtusk:** _(PartyWiped)_ There. All of you, down in my mines. Your town can hire more, and my foundry can pour more. We'll see who runs out first.

**Morg Ashtusk:** _(PartyWiped)_ Rest now, hirelings. The town will scrape together another fee for another lot, and they'll step over you to reach me.

**Morg Ashtusk:** _(Descend)_ Another floor down. I've had since bloody spring to get this one ready for you.

**Morg Ashtusk:** _(Descend)_ Lower still. Down here your god is closer, hungrier, and nearer waking. I like it down here.

**Morg Ashtusk:** _(Reinforcements)_ More of mine, through my own doors. You can't kill them as fast as I can send them.

**Morg Ashtusk:** _(Reinforcements)_ I've sent for help, hirelings. Mine comes when it's called and asks no fee. That is the whole bloody difference between us.

**Morg Ashtusk:** _(Crushed)_ Crushed flat. I set that wall myself, and it does its work better than you do yours.

**Morg Ashtusk:** _(Crushed)_ Pressed and finished. Thanks for the spine, you poor bugger. Your town can come and collect the rest.

**Morg Ashtusk:** _(EliteSlain)_ That was one of my best, you bloody vandals. Never mind. I have more, and they cost me nothing.

**Morg Ashtusk:** _(EliteSlain)_ {elite} is dead. I'll remember that, hirelings. I remember everything I'm owed.

**Morg Ashtusk:** _(ShrineUsed)_ Help yourselves at the shrine. Whatever it gave you, I'll have back before the bottom, with interest.

**Morg Ashtusk:** _(ShrineUsed)_ Pray at my shrines all you like, hirelings. Your god is asleep and starving. He won't hear a bloody word.

**Morg Ashtusk:** _(MimicSprung)_ Hungry box, that one. Everything in my mines is hungry, and your god most of all. Mind your bloody fingers.

**Morg Ashtusk:** _(MimicSprung)_ Nobody told you not to open things down here? No. The town never tells its hired help anything.

**Morg Ashtusk:** _(AllyDead)_ {name} is dead, then. One more of the town's own, left lying in my mines. They do pile up.

**Morg Ashtusk:** _(AllyDead)_ Another body for my mines. Carry it yourselves, hirelings. I'm not clearing up after you, you sorry sods.

**Morg Ashtusk:** _(AllyLeft)_ Your friend has gone home. Sensible. Nobody follows hirelings far, not for what the town pays.

**Morg Ashtusk:** _(AllyLeft)_ Walked out on you, did they? I don't blame them. I wouldn't follow you down my bloody stairs either.

**Morg Ashtusk:** _(Gamble)_ Dicing with the factor behind his locked door? Throw the town's money about while you can. My drains stay choked.

**Morg Ashtusk:** _(Gamble)_ Gambling your fee? Good. Lose the lot, you cheap little sods, and see who pays to bury you.

**Morg Ashtusk:** _(Descend)_ You told the little deacon his god is starving, hirelings. Honest of you. It won't feed his bloody god.

**Morg Ashtusk:** _(Descend)_ You forgave the warden who ran. Generous. Now forgive her bloody council: it sacked its own wardens, and they came down to me unpaid.

**Morg Ashtusk:** _(Descend)_ You wouldn't forgive the warden who ran, hirelings. Hard. Her council sacked every warden it had, and you still work for it.

**Morg Ashtusk:** _(MemberDowned)_ One down. You asked your god for his strength, and he's half awake for it. Ask again, hirelings. The more he wakes, the nearer my master comes.

**Morg Ashtusk:** _(MemberDowned)_ One down. You told your god to sleep through this, and he is. Nobody is coming to help you.

### Merchant (Master Oswin Pellet)

**Master Oswin Pellet:** _(node hire)_ Oswin Pellet, factor. The town hired you to clear the traps and creatures out of the mines so the drains can run again, and I lent the council the money. I have a weak chest and a strong sense of who ought to go down.

  - **Party (choice):** A factor, paying for drain work? _[to quakes]_
  - **Party (choice):** Spare us the chest. What's the pay? _[to deal]_

**Master Oswin Pellet:** _(node quakes)_ You'll have felt the ground go. The drains are backed up, the wells run sour, and the quakes come oftener every month. My carts haven't run since spring. Clear the mines and I get my money back.

  - **Party (choice):** Never mind your carts. What's in it for us? _[to deal]_
  - **Party (choice):** And where does our money come into it? _[to deal]_

**Master Oswin Pellet:** _(node deal)_ To business. The council's advance, through my hands and less my cut: a hundred and fifty gold, counted. Sell me what you find; buy what I carry. I wait behind a locked door on every floor. I am paying for the door.

  - **Party (choice):** Count it out, then. _[to clauses]_
  - **Party (choice):** Your cut looks generous. To you. _[to clauses]_

**Master Oswin Pellet:** _(node clauses)_ One more thing. The contract has clauses, factor's clauses, every one optional and every one paid for. Will you read them, or shall I fold them away?

  - **Party (choice):** Not today. _[to sendoff]_
  - **Party (choice):** Read them. _[Clauses; to sendoff]_

**Master Oswin Pellet:** _(node sendoff)_ Splendid. Keep every receipt I give you, folded, somewhere dry. Should anything happen to you below, the receipts are how I'll know what I've lost.

  - **Party (choice):** Lock your door, Pellet. _[Farewell]_
  - **Party (choice):** We'll see you below, factor. _[Farewell]_

**Master Oswin Pellet:** _(floor 1 greeting)_ In here. Quickly, and mind the step.

**Master Oswin Pellet:** _(floor 1 progress)_ One floor down and nothing of mine broken. I've paced it out already: three good rooms off the passage, south light if one brings the light.

**Master Oswin Pellet:** _(floor 1 trade)_ Blades on the left, mending on the right. I'll buy whatever you drag in, provided it has stopped moving.

**Master Oswin Pellet:** _(floor 2 greeting)_ In, and wipe your boots; I paid a man to sweep this room. Old Ulf at the pump works hasn't paid his coal account since spring. If you see him, remind him.

**Master Oswin Pellet:** _(floor 2 progress)_ Moss in the mortar, but moss is rustic, and rustic lets for a third more. I've written to my backers with the good news.

**Master Oswin Pellet:** _(floor 2 trade)_ Out with the plunder, then. I pay fair, by which I mean I pay, which is more than the previous tenants ever did.

**Master Oswin Pellet:** _(floor 3 greeting)_ Come in, come in. There's a deacon about, very civil. I gave him my card.

**Master Oswin Pellet:** _(floor 3 progress)_ A vaulted chapel. Do you know what a vaulted ceiling fetches up top? Nor do I. Nobody's ever sold one this far down. I mean to be first.

**Master Oswin Pellet:** _(floor 3 trade)_ Something special in the case today. I didn't find it lying about; I paid good money for it, and so will you.

**Master Oswin Pellet:** _(floor 4 greeting)_ Shut it behind you. The alderman went past with his papers and never once looked at my door. I don't care to be seen by the council in a cellar.

**Master Oswin Pellet:** _(floor 4 progress)_ Rotten timber, which is a builder's word for cheap. Strip it out, plaster over, call it the Chantry Apartments. My backers adore a name.

**Master Oswin Pellet:** _(floor 4 trade)_ Name your price and I'll name mine, and then we'll use mine.

**Master Oswin Pellet:** _(floor 5 greeting)_ There you are. Was that singing? Never mind. Acoustics. I shall put it in the particulars.

**Master Oswin Pellet:** _(floor 5 progress)_ Five floors. I've had a sign painted: Pellet's Deep, Quiet Living. The painter wanted paying in advance. Everyone does, lately.

**Master Oswin Pellet:** _(floor 5 trade)_ Sell me the bones of whatever that was. I know a man who grinds them for the market gardens.

**Master Oswin Pellet:** _(floor 6 greeting)_ Mind the puddle. I don't know what it is either, and I've decided not to have it assayed.

**Master Oswin Pellet:** _(floor 6 progress)_ Young Deacon Tobit has been at my door, asking after his master. I told him I'd seen nobody's master and sold him a candle. I charged extra for the wick.

**Master Oswin Pellet:** _(floor 6 trade)_ Have a look under the cloth. One piece, priced like a dowry, and worth every penny of it, I'm told. You may admire it for nothing.

**Master Oswin Pellet:** _(floor 7 greeting)_ There you are. Good. Was it this quiet last time? I thought it was louder. I'm sure it was louder.

**Master Oswin Pellet:** _(floor 7 progress)_ There's a tunnel past the camp that isn't on my survey. I paid for that survey. I paid for every inch of it, and somebody has added an inch.

**Master Oswin Pellet:** _(floor 7 trade)_ Buy something, would you. It steadies my hands to count.

**Master Oswin Pellet:** _(floor 8 greeting)_ In, in. Did you feel that one? Never mind. Nor did I.

**Master Oswin Pellet:** _(floor 8 progress)_ The quakes are keeping time. I set my watch by the last three and it hasn't lost a minute since. Ground has no business being punctual.

**Master Oswin Pellet:** _(floor 8 trade)_ Short stock today. Bandits have the rest, paid by that orc to keep the town out, and I want it back. Bring it, and I'll knock something off the price. Not much. Something.

**Master Oswin Pellet:** _(floor 9 greeting)_ Oh, it's you. Thank heaven. I mean, good day. Good day!

**Master Oswin Pellet:** _(floor 9 progress)_ My backers write to ask what I've seen down here. I write back that I've seen the investment. I've stopped writing what else.

**Master Oswin Pellet:** _(floor 9 trade)_ I kept one back for you, wrapped in sacking. I'd like it off my hands. I don't care for the way it sits there.

**Master Oswin Pellet:** _(floor 10 greeting)_ Mind your step, and mind mine. I keep hearing it behind me, a little after I take it.

**Master Oswin Pellet:** _(floor 10 progress)_ Ten floors. A round number. I've always liked round numbers; there are no corners in them for anything to wait in.

**Master Oswin Pellet:** _(floor 10 trade)_ Usual rates. I'll not haggle today. I haven't the stomach, and the stomach is the part of me that does the haggling.

**Master Oswin Pellet:** _(floor 11 greeting)_ Come in. Warm, isn't it? Nobody lit a fire. I asked.

**Master Oswin Pellet:** _(floor 11 progress)_ This is a forge, and I never bought a forge. There's a door along here I didn't pay for. I have a horror of things I didn't pay for.

**Master Oswin Pellet:** _(floor 11 trade)_ Wares are on the bench. The bench was here when I came. I don't know whose it is, so I'm selling off it quickly.

**Master Oswin Pellet:** _(floor 12 greeting)_ I hoped it was you. Every footstep, I hoped it was you, and I've heard a great many footsteps.

**Master Oswin Pellet:** _(floor 12 progress)_ Word from up top: the alderman has been seen buying a shovel. The council never bought anything without borrowing from me first, so I checked my ledger. It wasn't me.

**Master Oswin Pellet:** _(floor 12 trade)_ The pick of the stock is under the counter today. Take it. I'd sooner it went down with you than stayed here with me.

**Master Oswin Pellet:** _(floor 13 greeting)_ Ah. You. Alive. Good. That makes two of us, and I am counting.

**Master Oswin Pellet:** _(floor 13 progress)_ Thirteen floors. I'm not a superstitious man, but I've begun keeping a short list of exits, and it keeps getting shorter.

**Master Oswin Pellet:** _(floor 13 trade)_ Business as usual. Business is a great comfort. I've been buying my own stock off myself all morning, for the nerves.

**Master Oswin Pellet:** _(floor 14 greeting)_ Quickly. Shut it. Quietly, they hear the latch.

**Master Oswin Pellet:** _(floor 14 progress)_ I went and looked. I wish I hadn't. Iron things cooling in the black water by the grates, and every one of them the shape of something that walks.

**Master Oswin Pellet:** _(floor 14 trade)_ Take what you need and pay quickly. I shan't count it now. I'll count it later. Upstairs.

**Master Oswin Pellet:** _(floor 15 greeting)_ Another floor, and you're still with me. I told myself you would be. I didn't believe me.

**Master Oswin Pellet:** _(floor 15 progress)_ Every night it sounds a little nearer. Not louder; nearer. I've stopped talking about lodgings. Nobody will want to live on top of that.

**Master Oswin Pellet:** _(floor 15 trade)_ There's a fine piece in the case. I've priced it, I think. I can't recall the number. It's on the tag.

**Master Oswin Pellet:** _(floor 16 greeting)_ Oh, thank you. I was talking to the crates, and the crates were starting to answer.

**Master Oswin Pellet:** _(floor 16 progress)_ Marble, all the way along, with a blue light in it I can't account for. I account for everything. I'd sooner not account for this.

**Master Oswin Pellet:** _(floor 16 trade)_ The good stock, all of it. I was keeping it for a better day. There won't be one, so it may as well be today.

**Master Oswin Pellet:** _(floor 17 greeting)_ Don't mind my hands. They've a mind of their own, and it's a frightened one.

**Master Oswin Pellet:** _(floor 17 progress)_ The wall by my cot was cold yesterday. Today it's warm, and it hums. I've moved the cot. I've moved it three times.

**Master Oswin Pellet:** _(floor 17 trade)_ Buy quickly, please. The smell of it comes in under the door, and I can't charge it rent.

**Master Oswin Pellet:** _(floor 18 greeting)_ Is that you? Say something, so I know it's you.

**Master Oswin Pellet:** _(floor 18 progress)_ It was meant to be a short job and a modest profit. Now I sleep in my boots with the takings in my shirt, and I don't sleep.

**Master Oswin Pellet:** _(floor 18 trade)_ The best I have left, and I've little left. Buy it. Buy all of it, if you can carry it. Only leave me the lamp.

**Master Oswin Pellet:** _(floor 19 greeting)_ There you are. There you are. I counted every step you took coming down.

**Master Oswin Pellet:** _(floor 19 progress)_ It's under us. I can feel it through my boots, and it's less under us than it was this morning. Please hurry. Please.

**Master Oswin Pellet:** _(floor 19 trade)_ Prices are on the tags. I can't think about prices. I keep counting the coin anyway; it's the only thing down here that adds up.

**Master Oswin Pellet:** _(floor 20 greeting)_ The last quiet room, I think. You've come further than anyone I ever sold to, and I've sold to some very determined people.

**Master Oswin Pellet:** _(floor 20 progress)_ I'm going back up, to count what's left. Of the money. Of the town. I'd be very much obliged if you came up too, after.

**Master Oswin Pellet:** _(floor 20 trade)_ I'm selling off the lot, at prices I'm ashamed of. The last fine piece was always meant for you. Go well, and bring me a receipt.

**Master Oswin Pellet:** _(gathering)_ Something's at my door. Two somethings. I'm sure the lock is equal to it; I paid for a good one.

**Master Oswin Pellet:** _(gathering)_ Four of them out there now, by the breathing. Do finish your shopping, would you, and go and be brave at them.

**Master Oswin Pellet:** _(gathering)_ Six at the door! Six! The lock is good, but it was never priced for six. Out you go and see them off, I beg you.

### Banter

_bs_hum floors 1+_

**Sable:** _(weary)_ Brand. Hum something.

**Brand:** _(weary)_ No.

**Sable:** _(weary)_ Then I'll sing.

**Brand:** _(weary)_ I'll hit something. Possibly you.

_bs_purse floors 1+_

**Brand:** _(weary)_ Stop walking behind me.

**Sable:** _(weary)_ Someone has to watch your back.

**Brand:** _(weary)_ My purse is on my back.

**Sable:** _(weary)_ Then I'm watching it very closely.

_bs_corporal floors 1+_

**Sable:** _(weary)_ You were a sergeant.

**Brand:** _(weary)_ Corporal. Twice.

**Sable:** _(weary)_ Twice?

**Brand:** _(weary)_ They kept taking it back.

_bs_lifted floors 1+_

**Brand:** _(weary)_ How much have you lifted off the party so far?

**Sable:** _(weary)_ Nothing. Everyone's broke.

**Brand:** _(weary)_ Good.

**Sable:** _(weary)_ It's not good, Brand. It's tragic.

_bs_knives floors 1+_

**Sable:** _(weary)_ If I die, take my knives.

**Brand:** _(weary)_ If you die I'm taking everything.

**Sable:** _(weary)_ That's the most romantic thing anyone's said to me.

_bw_bricks floors 1+_

**Wren:** _(weary)_ Brick count's at four thousand.

**Brand:** _(weary)_ You're counting bricks.

**Wren:** _(weary)_ You count doors.

**Brand:** _(weary)_ Doors matter.

_bw_something floors 1+_

**Brand:** _(weary)_ Say something, Wren. Anything.

**Wren:** _(weary)_ Something.

**Brand:** _(weary)_ Thank you.

_bw_root floors 1+_

**Wren:** _(weary)_ I miss trees.

**Brand:** _(weary)_ There was a root on the last floor.

**Wren:** _(weary)_ That was a leg.

**Brand:** _(weary)_ It was a root-shaped leg.

_bw_quiet floors 1+_

**Brand:** _(weary)_ You walk quiet for a big lad.

**Wren:** _(weary)_ I walk quiet for anything.

**Brand:** _(weary)_ Teach me.

**Wren:** _(weary)_ Take the shield off.

_bw_shield floors 1+_

**Wren:** _(weary)_ Shield up.

**Brand:** _(weary)_ Nothing's there.

**Wren:** _(weary)_ Then it's cheap practice.

_bi_manners floors 1+_

**Ilex:** _(weary)_ Walking is merely falling forwards with manners.

**Brand:** _(weary)_ Then I've been falling for thirty years.

**Ilex:** _(weary)_ It shows in the knees.

_bi_fire floors 1+_

**Brand:** _(weary)_ Can you magic the stairs closer?

**Ilex:** _(weary)_ I can set fire to you so the walking feels shorter.

**Brand:** _(weary)_ Pass.

_bi_eleven floors 1+_

**Ilex:** _(weary)_ Brand, how many men did you command?

**Brand:** _(weary)_ Eleven.

**Ilex:** _(weary)_ And where are they now?

**Brand:** _(weary)_ Alive. I made sure of that. Shut up and walk.

_bi_gerald floors 1+_

**Brand:** _(weary)_ Ilex. The puddle.

**Ilex:** _(weary)_ Gerald.

**Brand:** _(weary)_ I'm not calling it Gerald.

**Ilex:** _(weary)_ He'll not mind. He's a very forgiving puddle.

_bi_slower floors 1+_

**Ilex:** _(weary)_ Your sword arm is twelve percent slower than on the first floor.

**Brand:** _(weary)_ You measured.

**Ilex:** _(weary)_ I observe. It is different.

**Brand:** _(weary)_ Observe your own feet, then. There's a hole.

_sw_sentence floors 1+_

**Sable:** _(weary)_ Wren. Say a whole sentence.

**Wren:** _(weary)_ No.

**Sable:** _(weary)_ That's one word.

**Wren:** _(weary)_ It had everything it needed.

_sw_oil floors 1+_

**Wren:** _(weary)_ You smell of lamp oil.

**Sable:** _(weary)_ I smell of commerce.

**Wren:** _(weary)_ I'm downwind of commerce. Move.

_sw_miss floors 1+_

**Sable:** _(weary)_ What do you miss most, up there?

**Wren:** _(weary)_ Trees.

**Sable:** _(weary)_ Gold.

**Wren:** _(weary)_ Trees don't get stolen.

_sw_mildred floors 1+_

**Wren:** _(weary)_ Every trap on this floor has a name.

**Sable:** _(weary)_ Go on.

**Wren:** _(weary)_ That one's Mildred. Don't step on Mildred.

_sw_locks floors 1+_

**Sable:** _(weary)_ If you see a lock, tell me first.

**Wren:** _(weary)_ I saw six.

**Sable:** _(weary)_ Six? Where?

**Wren:** _(weary)_ Behind us. You were talking.

_si_pockets floors 1+_

**Sable:** _(weary)_ Ilex. Hold still.

**Ilex:** _(weary)_ Why?

**Sable:** _(weary)_ Practice.

**Ilex:** _(weary)_ Those are my pockets.

_si_etymology floors 1+_

**Ilex:** _(weary)_ Do you know the etymology of 'thief'?

**Sable:** _(weary)_ Do you know the etymology of 'shut up'?

**Ilex:** _(weary)_ Middle period, regrettably.

_si_brazier floors 1+_

**Sable:** _(weary)_ Set fire to something. I'm bored.

**Ilex:** _(weary)_ I am not a brazier, Sable.

**Sable:** _(weary)_ You're shaped like one.

_si_damp floors 1+_

**Ilex:** _(weary)_ I have catalogued four kinds of damp.

**Sable:** _(weary)_ I've catalogued one kind of wizard.

**Ilex:** _(weary)_ Which?

**Sable:** _(weary)_ Damp.

_si_notes floors 1+_

**Sable:** _(weary)_ If I ever rob you properly, what's worth taking?

**Ilex:** _(weary)_ My notes.

**Sable:** _(weary)_ Nobody's buying notes.

**Ilex:** _(weary)_ Then I am safe, and you are poor.

_wi_count floors 1+_

**Ilex:** _(weary)_ Wren, what is the brick count?

**Wren:** _(weary)_ Four thousand two hundred.

**Ilex:** _(weary)_ Impressive.

**Wren:** _(weary)_ You've named six puddles.

_wi_loud floors 1+_

**Wren:** _(weary)_ Fire's loud.

**Ilex:** _(weary)_ Fire is thorough.

**Wren:** _(weary)_ I liked it quiet.

**Ilex:** _(weary)_ The dead liked it quiet. Note where that got them.

_wi_theology floors 1+_

**Ilex:** _(weary)_ There is a theological argument for this place.

**Wren:** _(weary)_ Is it 'leave'?

**Ilex:** _(weary)_ It is now.

_wi_rations floors 1+_

**Wren:** _(weary)_ Rations.

**Ilex:** _(weary)_ What about them?

**Wren:** _(weary)_ Yours are in Sable's bag.

**Ilex:** _(weary)_ Since when?

_wi_opinion floors 1+_

**Ilex:** _(weary)_ You never speak at length, Wren.

**Wren:** _(weary)_ Nothing's needed it.

**Ilex:** _(weary)_ Fascinating. A man without an opinion.

**Wren:** _(weary)_ I have one. It's about you.

_gag_gerald_1 floors 1+_

**Ilex:** _(weary)_ That puddle is Gerald.

**Sable:** _(weary)_ Why Gerald?

**Ilex:** _(weary)_ He has the look of a Gerald.

_gag_gerald_n floors 1+_

**Ilex:** _(weary)_ Gerald {roman}, I presume. Long may he reign.

**Brand:** _(weary)_ You're naming puddles.

**Ilex:** _(weary)_ Somebody must.

_gag_gerald_widow floors 1+_

**Ilex:** _(weary)_ Observe. A puddle beside a dry patch. Gerald's widow.

**Sable:** _(weary)_ You've given a puddle a marriage.

**Ilex:** _(weary)_ A happy one, until the drain.

**Wren:** _(weary)_ Can we go.

_gag_doors_10 floors 1+_

**Brand:** _(weary)_ That's {count} doors. Who builds {count} doors?

**Sable:** _(weary)_ A man with a hinge habit.

**Brand:** _(weary)_ I'm counting them. Somebody should.

_gag_doors_40 floors 1+_

**Brand:** _(weary)_ Door {count}.

**Wren:** _(weary)_ You said you stopped at forty.

**Brand:** _(weary)_ I lied. I can't stop.

**Ilex:** _(weary)_ It is a sickness, Brand. Doors.

_gag_empty_3 floors 1+_

**Sable:** _(weary)_ {count} empty rooms. I'm keeping a tally.

**Brand:** _(weary)_ Of what?

**Sable:** _(weary)_ Of what I'm owed. Boredom's a cost.

_gag_empty_8 floors 1+_

**Sable:** _(weary)_ That's {count}. Empty rooms. Ilex, your pockets. Hold still.

**Ilex:** _(weary)_ There is nothing in them.

**Sable:** _(weary)_ Then it'll be quick.

_cb_mimic floors 1+_

**Sable:** _(weary)_ I'm not opening the next chest.

**Brand:** _(weary)_ You open every chest.

**Sable:** _(weary)_ The last one had teeth, Brand. Teeth.

**Wren:** _(weary)_ It also had a tongue. I checked.

_cb_wipe floors 1+_

**Brand:** _(weary)_ We died.

**Ilex:** _(weary)_ We did not die. We were interrupted.

**Brand:** _(weary)_ All four of us. Face down.

**Ilex:** _(weary)_ Interrupted at length.

_cb_boon floors 1+_

**Ilex:** _(weary)_ I feel improved.

**Sable:** _(weary)_ You feel smug.

**Ilex:** _(weary)_ The two present identically.

_cb_curse floors 1+_

**Wren:** _(weary)_ Who took the curse.

**Sable:** _(weary)_ It was that or the gold.

**Wren:** _(weary)_ You took the gold too.

**Sable:** _(weary)_ I'm a thief, Wren. Be reasonable.

_cb_gamble floors 1+_

**Sable:** _(weary)_ The merchant's cloth bundles are a swindle.

**Brand:** _(weary)_ You bought three.

**Sable:** _(weary)_ I'm thorough about being swindled.

_grp_alive floors 1+_

**Brand:** _(weary)_ Right. Nobody's dead. Good.

**Sable:** _(weary)_ Low bar.

**Wren:** _(weary)_ Cleared it.

**Ilex:** _(weary)_ Barely.

_grp_watched floors 16+_

**Sable:** _(weary)_ Does anyone else feel watched?

**Wren:** _(weary)_ Yes.

**Ilex:** _(weary)_ Constantly.

**Brand:** _(weary)_ Walk faster, then.

_bs_halfway floors 10+_

**Brand:** _(weary)_ Halfway.

**Sable:** _(weary)_ Says who?

**Brand:** _(weary)_ My knees.

_un_wipes_1 floors 1+_

**Brand:** _(weary)_ Fourth time I've died on a Tuesday.

**Sable:** _(weary)_ It isn't Tuesday.

**Brand:** _(weary)_ It is down here. It's always Tuesday down here.

_un_wipes_2 floors 1+_

**Ilex:** _(weary)_ Statistically we are improving.

**Wren:** _(weary)_ We died.

**Ilex:** _(weary)_ Later than last time. By a full minute.

_un_mimic_1 floors 1+_

**Sable:** _(weary)_ Knock first.

**Brand:** _(weary)_ On a chest?

**Sable:** _(weary)_ On anything with a lid, Brand. Manners.

_un_mimic_2 floors 1+_

**Wren:** _(weary)_ That one's breathing.

**Ilex:** _(weary)_ Chests do not breathe.

**Wren:** _(weary)_ Tell it that.

**Ilex:** _(weary)_ I'd rather you did.

_un_shrines_1 floors 1+_

**Ilex:** _(weary)_ Six altars. We are practically clergy.

**Sable:** _(weary)_ Clergy don't get paid in gold.

**Ilex:** _(weary)_ You have not met clergy.

_un_shrines_2 floors 1+_

**Brand:** _(weary)_ I don't trust a god that hands out favours.

**Wren:** _(weary)_ You took three.

**Brand:** _(weary)_ I don't trust them. I didn't say I'd refuse.

_un_guard_1 floors 1+_

**Brand:** _(weary)_ Shield up, feet planted, breathe out.

**Sable:** _(weary)_ Is that a prayer?

**Brand:** _(weary)_ It's a parry. The prayer comes after.

_un_guard_2 floors 1+_

**Wren:** _(weary)_ Twenty-five perfect blocks.

**Brand:** _(weary)_ Who's counting?

**Wren:** _(weary)_ Your shield. There's a notch for each one.

_un_parry_1 floors 1+_

**Sable:** _(weary)_ You turned that one on the edge.

**Brand:** _(weary)_ Luck.

**Sable:** _(weary)_ Twenty-five times. That's not luck, that's a habit.

_un_parry_2 floors 1+_

**Ilex:** _(weary)_ There is a geometry to the riposte.

**Sable:** _(weary)_ There's a wrist to it.

**Ilex:** _(weary)_ The wrist is the geometry.

_un_elites_1 floors 1+_

**Brand:** _(weary)_ Another one with a name.

**Wren:** _(weary)_ They've all got names. We just don't ask.

**Brand:** _(weary)_ Fifty of them never got to say.

_un_elites_2 floors 1+_

**Sable:** _(weary)_ The named ones used to scare me.

**Ilex:** _(weary)_ And now?

**Sable:** _(weary)_ Now they're a ledger entry.

_un_rescue_1 floors 1+_

**Wren:** _(weary)_ Hold still. I've done this nine times.

**Brand:** _(weary)_ To me?

**Wren:** _(weary)_ Mostly to you.

_un_rescue_2 floors 1+_

**Ilex:** _(weary)_ You pulled me out of a Press.

**Sable:** _(weary)_ You were in the way of the loot.

**Ilex:** _(weary)_ That is the kindest thing you have said to me.

_un_hardcore_1 floors 1+_

**Brand:** _(weary)_ No second chances this time.

**Sable:** _(weary)_ There never were. We just used to pretend.

**Brand:** _(weary)_ I liked pretending.

_un_hardcore_2 floors 1+_

**Wren:** _(weary)_ If I go down, leave me.

**Ilex:** _(weary)_ We are contractually obliged to.

**Wren:** _(weary)_ Good.

**Ilex:** _(weary)_ It does not say we have to like it.

_bs_extra floors 1+_

**Sable:** _(weary)_ Remind me. What's the job?

**Brand:** _(weary)_ Clear the drains. Water runs. Town pays.

**Sable:** _(weary)_ And whatever's living in the drains?

**Brand:** _(weary)_ Costs extra.

_wi_monograph floors 1+_

**Ilex:** _(weary)_ A god asleep under a market town. I may get a monograph out of this.

**Wren:** _(weary)_ Wrong.

**Ilex:** _(weary)_ The monograph?

**Wren:** _(weary)_ The place.

_bw_theory floors 1+_

**Brand:** _(weary)_ Clear the drains and the wells come sweet again. That's the theory.

**Wren:** _(weary)_ And the pay?

**Brand:** _(weary)_ That's the practice.

_sw_still floors 6+_

**Wren:** _(weary)_ Water's standing. Going sour.

**Sable:** _(weary)_ Lovely. The pay's the same, the water's worse, and I'm still here. Why am I still here?

**Wren:** _(weary)_ You like us.

**Sable:** _(weary)_ I like your purses. Different thing entirely.

_bi_counting floors 6+_

**Ilex:** _(weary)_ The grey ones in the packs. The freed wardens said the finished ones join them.

**Brand:** _(weary)_ I heard.

**Ilex:** _(weary)_ So, statistically, some of what we have killed used to clear these drains.

**Brand:** _(weary)_ I'm counting them. Somebody should.

_bs_runs floors 6+_

**Sable:** _(weary)_ We forgave that warden awfully quick, Brand.

**Brand:** _(weary)_ Everybody runs once. Only counts if you stay gone.

**Sable:** _(weary)_ Is that soldier's wisdom?

**Brand:** _(weary)_ Corporal's. Sergeants don't run. They send corporals.

_bw_wheat floors 11+_

**Wren:** _(weary)_ Fields up top. They need this water.

**Brand:** _(weary)_ Since when do you care about a town?

**Wren:** _(weary)_ Don't. Care about wheat.

**Brand:** _(weary)_ Fine. Shields up for the wheat.

_bi_mould floors 14+_

**Ilex:** _(weary)_ That mould by the stair back there was the orc himself, poured larger. It wants smashing.

**Brand:** _(weary)_ Foehammer'd want first swing.

**Ilex:** _(weary)_ And Ulf's journeyman saw the whole drawings, and said nothing.

**Brand:** _(weary)_ The orc threatened the old man. You keep quiet for your own.

_wi_bellows floors 15+_

**Wren:** _(weary)_ Big engine.

**Ilex:** _(weary)_ The Bellows. Oskar says its joints are where the old iron is thinnest. I concur, which he would find tiresome.

**Wren:** _(weary)_ Ladles?

**Ilex:** _(weary)_ What drops off them stays soft until it cools, he says. I see no reason to doubt him.

_si_footnote floors 16+_

**Ilex:** _(weary)_ I came down here for a footnote.

**Sable:** _(weary)_ And now?

**Ilex:** _(weary)_ A god whose sleep holds a door shut is not a footnote. It is, at minimum, a chapter. Walk faster.

_bs_sums floors 16+_

**Sable:** _(weary)_ I've done the sums. This town can't pay for the last floor.

**Brand:** _(weary)_ So go home.

**Sable:** _(weary)_ I like the town. Tell anyone and I'll have your boots.

**Brand:** _(weary)_ Wouldn't fit you.

_bw_deacon floors 16+_

**Brand:** _(weary)_ We told that deacon his god was starving.

**Wren:** _(weary)_ We did.

**Brand:** _(weary)_ Lad came down after us for it. Wanted proof of his own master.

**Wren:** _(weary)_ Good.

### Barks

**Brand:** _(Kill)_ Down. Next one, don't dawdle.

**Brand:** _(Kill)_ One less between us and the drains.

**Brand:** _(Kill)_ Consecrated ground. It'll soak that up.

**Brand:** _(Hurt)_ Felt worse from a cart horse.

**Brand:** _(Hurt)_ Good. Now I know where you are.

**Brand:** _(Hurt)_ That's coming out of somebody's wages.

**Brand:** _(Loot)_ Dead men's coin spends the same.

**Brand:** _(Loot)_ Call it expenses. The contract said expenses.

**Brand:** _(Loot)_ Still warm. I'll not ask why.

**Brand:** _(LevelUp)_ Arm's steadier. Shame about the rest.

**Brand:** _(LevelUp)_ Harder going down than I was going in.

**Brand:** _(LevelUp)_ Thirty years of sergeants taught me that trick.

**Brand:** _(BossSeen)_ Big. Big things fall loudest.

**Brand:** _(BossSeen)_ Behind me. All of you. Now.

**Brand:** _(BossSeen)_ Drains, they said. Nobody mentioned that.

**Brand:** _(Possess)_ Brand. Right. Try to keep up.

**Brand:** _(Possess)_ Shield's mine again. Stay behind it.

**Brand:** _(Possess)_ Sergeant's back. Corporal. Whatever. Move.

**Brand:** _(Downed)_ Not lying down. Not for him.

**Brand:** _(Downed)_ Floor's warm. That's the worst of it.

**Brand:** _(Downed)_ Get the others out. Argue later.

**Brand:** _(Revived)_ Up. Say nothing about it.

**Brand:** _(Revived)_ I owe you a drink we'll never reach.

**Brand:** _(Revived)_ Where's my shield. Where is my shield.

**Brand:** _(Kill)_ That's how it's done. Watch the flank.

**Brand:** _(Kill)_ Stay down. I mean it.

**Brand:** _(Kill)_ One. Keep count, somebody.

**Brand:** _(Kill)_ Clean. Mostly.

**Brand:** _(Kill)_ Not today. Not by you.

**Brand:** _(Kill)_ Shield up, blade through. Simple.

**Brand:** _(Kill)_ He swung first. He'll not swing again.

**Brand:** _(Kill)_ Rest easy, whatever you were.

**Brand:** _(Kill)_ Move up. There'll be more.

**Brand:** _(Idle)_ Are we camping here? Say so and I'll light a fire.

**Brand:** _(Idle)_ The stairs are that way. Or that way. Pick one. Any one.

**Brand:** _(Idle)_ I've seen statues with more go in them.

**Brand:** _(Idle)_ Moss is forming on your boots. I can hear it.

**Brand:** _(Idle)_ If you're waiting for the dungeon to come to you, it will, and you won't like it.

**Brand:** _(Idle)_ Move, or I start charging rent for standing in my shadow.

**Brand:** _(Idle)_ Thinking? Don't strain yourself. Walk and think at the same time; the rest of us manage.

**Brand:** _(Idle)_ I've held shield walls shorter than this.

**Brand:** _(Idle)_ Left foot. Then the right. It's a system. Try it.

**Brand:** _(Idle)_ Fine. I'll go alone, and you can explain to the alderman why.

**Brand:** _(Dance)_ Dancing is my speciality. Dying, it seems, is yours.

**Brand:** _(Dance)_ That's how Brand does it. Watch the hips, not the sword.

**Brand:** _(Dance)_ Down you go, and up go my heels.

**Brand:** _(Dance)_ Shield, sword, and a little shimmy. Full service.

**Brand:** _(Dance)_ Who's next? I've got five more of these in me.

**Brand:** _(Dance)_ Brand's rule: kill it, then shake it.

**Brand:** _(Dance)_ The shield arm's for blocking. The hips are for this.

**Brand:** _(Dance)_ Don't tell the captain I can move like this.

**Brand:** _(Dance)_ Squat, buck, rock. Drill of the day.

**Brand:** _(Dance)_ That's a soldier's dance. Learned it in a tavern.

**Brand:** _(Dance)_ Two of each, then back to work. Discipline.

**Brand:** _(Dance)_ Watch the veteran. Learn something.

**Brand:** _(Dance)_ I don't do it pretty. I do it done.

**Brand:** _(Dance)_ My knees disagree with the squats. My knees lost.

**Brand:** _(Dance)_ There's a wave at the end if you behave.

**Brand:** _(Dance)_ Stand back. Brand is celebrating.

**Brand:** _(Dance)_ Even a wall can dance when the enemy's down.

**Brand:** _(Dance)_ Shields down, hips up.

**Brand:** _(Dance)_ Call that a funeral march. A quick one.

**Brand:** _(Dance)_ Right. That's enough feeling good. Where's the next one?

**Brand:** _(Travel)_ I guess we're getting paid by the mile.

**Brand:** _(Travel)_ Walking for a living. Didn't see that in the contract.

**Brand:** _(Travel)_ If I'd wanted to march I'd have stayed in the levy.

**Brand:** _(Travel)_ My feet have opinions. None of them polite.

**Brand:** _(Travel)_ Another corridor. Somebody's very proud of these corridors.

**Brand:** _(Travel)_ Anyone counting the doors? I stopped at forty.

**Brand:** _(Travel)_ Shield's getting heavy. Nothing to hit with it.

**Brand:** _(Travel)_ Wake me if something growls.

**Brand:** _(Travel)_ I've had quieter patrols. Not many.

**Brand:** _(Travel)_ At this rate the orc dies of old age before we reach him.

**Brand:** _(Travel)_ Rations are low. Patience is lower.

**Brand:** _(Travel)_ A fight would be nice. A chair would be nicer.

**Brand:** _(Travel)_ Left, left, left again. I'm starting to suspect a circle.

**Brand:** _(Travel)_ The dead are doing a poor job of defending this place.

**Brand:** _(Travel)_ I've seen livelier graveyards. In fact, this is one.

**Brand:** _(Travel)_ My sword is sulking. It hasn't been out in an hour.

**Brand:** _(Travel)_ Somebody hum something. Not Sable.

**Brand:** _(Travel)_ Mind the puddle. Mind the other puddle.

**Brand:** _(Travel)_ The alderman said dangerous. The alderman lied, or is bad at walking.

**Brand:** _(Travel)_ Boots. Stone. Boots. Stone. Gripping stuff.

**Brand:** _(TrapSprung)_ Trap. Of course it's a trap. Everything's a trap.

**Brand:** _(TrapSprung)_ Who puts a trap in a corridor? Everyone, apparently.

**Brand:** _(TrapSprung)_ That's my good leg. Was.

**Brand:** _(TrapSprung)_ Right. Nobody follow me. Ever.

**Brand:** _(TrapSprung)_ Spotted it. Late. Spotted it late.

**Brand:** _(TrapSprung)_ I've marched through worse. Not by much.

**Brand:** _(TrapSprung)_ Wren, you said this one was safe.

**Brand:** _(TrapSprung)_ Somebody write that one down. Not me. I'm busy bleeding.

**Brand:** _(Fell)_ Floor's further down than advertised.

**Brand:** _(Fell)_ I meant to do that. Badly.

**Brand:** _(Fell)_ Who dug a hole there? Fill it in.

**Brand:** _(Fell)_ Down. Not dead. Give me a moment.

**Brand:** _(Fell)_ The shield broke the fall. The shield and my spine.

**Brand:** _(Fell)_ Nobody saw that. Good.

**Brand:** _(Fell)_ Hole. Deep one. Mind it.

**Brand:** _(Fell)_ That's thirty years of knees in one landing.

**Brand:** _(PerfectBlow)_ That's the one. That's how it's taught.

**Brand:** _(PerfectBlow)_ Clean through. Textbook.

**Brand:** _(PerfectBlow)_ Felt that in the shoulder. Good.

**Brand:** _(PerfectBlow)_ Timing. It's all timing.

**Brand:** _(PerfectBlow)_ Eleven lads learned that swing. Still works.

**Brand:** _(PerfectBlow)_ There. Perfect. Don't expect it twice.

**Brand:** _(PerfectBlow)_ Weight behind it. Like I said.

**Brand:** _(PerfectBlow)_ That's a sergeant's swing.

**Brand:** _(Parried)_ Not today.

**Brand:** _(Parried)_ Turned it. Steel knows steel.

**Brand:** _(Parried)_ Swing like that and I'll do it all day.

**Brand:** _(Parried)_ Nice try.

**Brand:** _(Parried)_ Off the edge. Where it belongs.

**Brand:** _(Parried)_ Slow. You're slow.

**Brand:** _(Parried)_ Parried. Now mine.

**Brand:** _(Parried)_ That's a block with manners.

**Brand:** _(Mimic)_ The chest bit me. The chest. Bit me.

**Brand:** _(Mimic)_ Kill the box! Kill the box!

**Brand:** _(Mimic)_ Shields up. It's furniture. Angry furniture.

**Brand:** _(Mimic)_ I've fought men. Dogs. Never luggage.

**Brand:** _(Mimic)_ That's not a chest. Hit it till it is.

**Brand:** _(Mimic)_ Teeth. On a lid. Right.

**Brand:** _(Mimic)_ Nobody open anything. Ever.

**Brand:** _(Mimic)_ It's got a tongue. Why has it got a tongue.

**Brand:** _(SecretFound)_ A door in a wall. Somebody's clever.

**Brand:** _(SecretFound)_ Hidden room. Mind the hidden trap.

**Brand:** _(SecretFound)_ Well. Nobody's been in here for a while.

**Brand:** _(SecretFound)_ Found something. Don't touch it yet.

**Brand:** _(SecretFound)_ Secret passage. Soldiers love those.

**Brand:** _(SecretFound)_ Somebody wanted this kept quiet.

**Brand:** _(SecretFound)_ Go on, then. I'll stand in the gap.

**Brand:** _(SecretFound)_ There's always a back way. Always.

**Brand:** _(EliteSeen)_ That one's got a name. Names cost extra.

**Brand:** _(EliteSeen)_ Big one. Named one. Form up.

**Brand:** _(EliteSeen)_ It's wearing something. Doesn't matter. Hit it.

**Brand:** _(EliteSeen)_ Officer type. Take it down first.

**Brand:** _(EliteSeen)_ That's not a grunt. Watch its hands.

**Brand:** _(EliteSeen)_ Named. Means it's killed before.

**Brand:** _(EliteSeen)_ On me. Shields. This one's special.

**Brand:** _(EliteSeen)_ Right. Somebody important wants to die.

**Brand:** _(Broke)_ Not enough. Never enough.

**Brand:** _(Broke)_ Soldier's pay. Soldier's purse.

**Brand:** _(Broke)_ Put it back. Gently.

**Brand:** _(Broke)_ We'll come back. Richer. Probably.

**Brand:** _(Broke)_ I can afford to look.

**Brand:** _(Broke)_ That's a month's wages for a hat.

**Brand:** _(Broke)_ Walk away, Brand. Walk away.

**Brand:** _(Broke)_ Coin's short. Patience shorter.

**Brand:** _(ShrineBoon)_ Feels... warmer. Don't like it. Keeping it.

**Brand:** _(ShrineBoon)_ That'll do. Whatever it is.

**Brand:** _(ShrineBoon)_ Something's in my arm now. Good something.

**Brand:** _(ShrineBoon)_ A blessing. Thirty years late.

**Brand:** _(ShrineBoon)_ Right. Stronger. Don't make a fuss.

**Brand:** _(ShrineBoon)_ If it helps, it helps.

**Brand:** _(ShrineBoon)_ Took it. Don't tell the chaplain.

**Brand:** _(ShrineBoon)_ I'll pay for that later. Everything costs.

**Brand:** _(ShrineCurse)_ That's a cost. Felt it go in.

**Brand:** _(ShrineCurse)_ Something's wrong with me now. Add it to the list.

**Brand:** _(ShrineCurse)_ A curse. I've had worse from a quartermaster.

**Brand:** _(ShrineCurse)_ Heavier. Everything's heavier.

**Brand:** _(ShrineCurse)_ Paid in blood. Usual rate.

**Brand:** _(ShrineCurse)_ Don't look at me like that. It was the deal.

**Brand:** _(ShrineCurse)_ I'll live. Shorter, but I'll live.

**Brand:** _(ShrineCurse)_ Marked. Fine. I was never pretty.

**Brand:** _(GambleWin)_ Luck. Don't trust it. Take it.

**Brand:** _(GambleWin)_ That's better than it should be.

**Brand:** _(GambleWin)_ Well. The cloth was hiding something.

**Brand:** _(GambleWin)_ First time a merchant's done right by me.

**Brand:** _(GambleWin)_ Worth it. Say nothing.

**Brand:** _(GambleWin)_ Good steel from a bad bet.

**Brand:** _(GambleWin)_ Don't get used to it.

**Brand:** _(GambleWin)_ Somebody's smiling on us. Suspicious.

**Brand:** _(GambleDud)_ Cloth. Wrapped round nothing. Figures.

**Brand:** _(GambleDud)_ That's a week's pay for a disappointment.

**Brand:** _(GambleDud)_ Never gamble. I say that every time.

**Brand:** _(GambleDud)_ Well. It's a thing. Technically.

**Brand:** _(GambleDud)_ Rubbish. Expensive rubbish.

**Brand:** _(GambleDud)_ He knew. He always knows.

**Brand:** _(GambleDud)_ Right. Lesson paid for. Again.

**Brand:** _(GambleDud)_ I could have had a chair for that.

**Brand:** _(LowHp)_ I'm hurt. Properly. Someone fill the gap.

**Brand:** _(LowHp)_ Blood's mine this time. Cover me.

**Brand:** _(LowHp)_ Not done. Nearly. Not done.

**Brand:** _(LowHp)_ Need a moment. Won't get one.

**Brand:** _(LowHp)_ That's deep. Keep fighting.

**Brand:** _(LowHp)_ Shield's heavy. Arm's heavier.

**Brand:** _(LowHp)_ Don't wait for me. Finish it.

**Brand:** _(LowHp)_ Thirty years and this is the one that gets me.

**Brand:** _(Hurt)_ Agh. Felt that.

**Brand:** _(Hurt)_ That one landed. Won't again.

**Brand:** _(Hurt)_ Right. Now I'm cross.

**Brand:** _(Hurt)_ Armour's dented. So am I.

**Brand:** _(Hurt)_ Hit me again and mean it.

**Brand:** _(Hurt)_ Blood. Mine. Fine.

**Brand:** _(Hurt)_ Nothing a drink won't fix. Later.

**Brand:** _(Hurt)_ Through the guard. How.

**Brand:** _(Loot)_ Useful. Maybe.

**Brand:** _(Loot)_ Take it. Weigh it later.

**Brand:** _(Loot)_ Somebody dropped this. Their loss.

**Brand:** _(Loot)_ Better than what I've got. Barely.

**Brand:** _(Loot)_ In the bag. Move on.

**Brand:** _(Loot)_ Spoils. Don't get greedy.

**Brand:** _(Loot)_ That'll sell, if nothing else.

**Brand:** _(Loot)_ Mine. Unless Sable gets to it.

**Brand:** _(Downed)_ Down. Not out. Get me up.

**Brand:** _(Downed)_ Can't... hold the line...

**Brand:** _(Downed)_ Somebody. Take the shield.

**Brand:** _(Downed)_ That's it. That's me.

**Brand:** _(Downed)_ Floor's cold. Hurry.

**Brand:** _(Downed)_ Not here. Not like this.

**Brand:** _(Downed)_ Get up, Brand. ...Can't.

**Brand:** _(Downed)_ Finish it. I'll watch.

**Brand:** _(Revived)_ Up. Right. Where were we.

**Brand:** _(Revived)_ I owe you. Add it to the pile.

**Brand:** _(Revived)_ Back. Angrier.

**Brand:** _(Revived)_ That was a rest. Short one.

**Brand:** _(Revived)_ Shield's still here. Good.

**Brand:** _(Revived)_ Never again. Until next time.

**Brand:** _(Revived)_ Breathing. Count that as a win.

**Brand:** _(Revived)_ Thanks. Don't mention it. Ever.

**Brand:** _(BossSeen)_ There. That's the big one. Shields.

**Brand:** _(BossSeen)_ Right. Everyone behind me.

**Brand:** _(BossSeen)_ That's a lot of monster. Hit the knees.

**Brand:** _(BossSeen)_ Form up. This one's earned a name.

**Brand:** _(BossSeen)_ Steady. Steady. Now.

**Brand:** _(BossSeen)_ Been waiting for you. Didn't want to.

**Brand:** _(BossSeen)_ It's big. It'll fall big.

**Brand:** _(BossSeen)_ Hold. Wait for the swing. Then.

**Brand:** _(LevelUp)_ Stronger. Took long enough.

**Brand:** _(LevelUp)_ Learned something. Hurt doing it.

**Brand:** _(LevelUp)_ That's experience. The painful kind.

**Brand:** _(LevelUp)_ Better. Not good. Better.

**Brand:** _(LevelUp)_ Still breathing, still learning.

**Brand:** _(LevelUp)_ Another notch. Don't celebrate.

**Brand:** _(LevelUp)_ Steel feels lighter. Won't last.

**Brand:** _(LevelUp)_ Thirty years and I'm still improving. Slowly.

**Brand:** _(Cheer)_ Right, you lot. Still alive. Keep it that way.

**Brand:** _(Cheer)_ Heads up! Nobody's dead and I'm counting that.

**Brand:** _(Cheer)_ On me! Still breathing, still paid!

**Brand:** _(Cheer)_ Hoy! Shields high, chins higher!

**Brand:** _(CheerAnswer)_ Aye.

**Brand:** _(CheerAnswer)_ Still here. Unfortunately.

**Brand:** _(CheerAnswer)_ Right behind you. Mind the shield.

**Brand:** _(CheerAnswer)_ Hah. Fine. Hoy.

**Brand:** _(CheerAnswer)_ Keep it up and I'll hum.

**Brand:** _(CheerAnswer)_ That's the spirit. Now walk.

**Sable:** _(Kill)_ Quiet as a kept promise.

**Sable:** _(Kill)_ He had nothing. Typical of this parish.

**Sable:** _(Kill)_ Don't look. I didn't do it neatly.

**Sable:** _(Hurt)_ That's going to scar somewhere unfashionable.

**Sable:** _(Hurt)_ Rude. I was being so careful.

**Sable:** _(Hurt)_ Bleeding. Fine. I had spare.

**Sable:** _(Loot)_ Silver. Call it an advance on the advance.

**Sable:** _(Loot)_ Finders, keepers, swallowers, weepers.

**Sable:** _(Loot)_ Locked. Was locked. Past tense is my trade.

**Sable:** _(LevelUp)_ Faster hands. Slower conscience.

**Sable:** _(LevelUp)_ I'm improving. The dungeon's improving quicker.

**Sable:** _(LevelUp)_ Another trick for a town that won't pay.

**Sable:** _(BossSeen)_ Nothing on it worth stealing. Shame.

**Sable:** _(BossSeen)_ Brand. You're the wall. Be a wall.

**Sable:** _(BossSeen)_ I'll take the flank. The flank is traditional.

**Sable:** _(Possess)_ Sable. Finally, someone with taste.

**Sable:** _(Possess)_ Me? Lovely. Everyone check your pockets.

**Sable:** _(Possess)_ A lady leads. Try to look like you belong.

**Sable:** _(Downed)_ Pockets first. Then me. I'm joking. Mostly.

**Sable:** _(Downed)_ I'm not lying down. I'm resting flat.

**Sable:** _(Downed)_ Don't leave me down here. I'm worth far more alive.

**Sable:** _(Revived)_ I kept the purse. Priorities.

**Sable:** _(Revived)_ Up, and I remember every face that waited.

**Sable:** _(Revived)_ Back from the floor. Charming floor. Hate it.

**Sable:** _(Kill)_ He had nothing on him. Typical of this parish.

**Sable:** _(Kill)_ Oh, you'll want to check his pockets. I already did.

**Sable:** _(Kill)_ From behind. Where the gold is.

**Sable:** _(Kill)_ Hush now. There. All better.

**Sable:** _(Kill)_ Nobody saw that. Which is the point.

**Sable:** _(Kill)_ I liked him better quiet.

**Sable:** _(Kill)_ Shh. The dead don't haggle.

**Sable:** _(Kill)_ That one owed me. Now we're square.

**Sable:** _(Idle)_ You're standing like somebody owes you money. Nobody down here pays.

**Sable:** _(Idle)_ Shall I pick the lock on your feet?

**Sable:** _(Idle)_ I've robbed people faster than you decide which way to face.

**Sable:** _(Idle)_ Go on, dawdle. The rats are taking bets on you.

**Sable:** _(Idle)_ Don't worry, I'm sure the treasure will wait. Treasure's patient like that. It isn't.

**Sable:** _(Idle)_ Is this a plan, or did your brain step out for a smoke?

**Sable:** _(Idle)_ I could rob you blind right now and you'd still be standing here.

**Sable:** _(Idle)_ Lovely spot. Shall we die in it, or somewhere with better light?

**Sable:** _(Idle)_ Blink twice if you've forgotten how legs work.

**Sable:** _(Idle)_ You know the walls don't move, yes? You have to.

**Sable:** _(Dance)_ Booty be all that, and the booty's all mine.

**Sable:** _(Dance)_ Look at me go. Look at you not.

**Sable:** _(Dance)_ Stabbed, dropped, and danced on. The Sable special.

**Sable:** _(Dance)_ Shake it, Sable. Nobody left to watch but the rats.

**Sable:** _(Dance)_ Oh, I'm good. I'm so good I'm dancing about it.

**Sable:** _(Kill)_ Dead. Cue the music.

**Sable:** _(Kill)_ Stabbed in time with the beat.

**Sable:** _(Kill)_ That's the knife work done. Now the footwork.

**Sable:** _(Kill)_ One less partner. Shame, he had rhythm.

**Sable:** _(Kill)_ Drop. Good. Now watch me not.

**Sable:** _(Kill)_ Quiet now. I like to dance in silence.

**Sable:** _(Kill)_ Pockets first, then the hips.

**Sable:** _(Kill)_ Fell like a drunk on a dance floor. Stays like one too.

**Sable:** _(Kill)_ Knife in, knife out, step to the left.

**Sable:** _(Kill)_ I've killed quieter, but never prettier.

**Sable:** _(Kill)_ Boot on the chest, shimmy to follow.

**Sable:** _(Kill)_ That one didn't see the blade or the squat coming.

**Sable:** _(Kill)_ Oh, you dropped something. Everything, actually.

**Sable:** _(Kill)_ A girl's got to warm up before she dances.

**Sable:** _(Kill)_ Nothing personal. Everything rhythmic.

**Sable:** _(Dance)_ Hips don't lie, and neither do I. Much.

**Sable:** _(Dance)_ Pockets picked, hips kicked. Try and keep up.

**Sable:** _(Dance)_ Nobody said a girl can't gloat. Watch me gloat.

**Sable:** _(Dance)_ Two squats, two shimmies, one less problem.

**Sable:** _(Dance)_ I'd curtsey, but this is better.

**Sable:** _(Dance)_ A lady celebrates how she likes. This is how I like.

**Sable:** _(Dance)_ First dance is free, darlings. The encore goes on the alderman's bill.

**Sable:** _(Dance)_ Light fingers, loose hips.

**Sable:** _(Dance)_ Call it a victory wiggle. I call it Tuesday.

**Sable:** _(Dance)_ The rats are clapping. Good rats.

**Sable:** _(Dance)_ If you'd paid up, I'd have danced for you alive.

**Sable:** _(Dance)_ Shh. Don't talk over the hip rock.

**Sable:** _(Dance)_ This body was built for stealing and swaying. Mostly swaying.

**Sable:** _(Dance)_ Another one in the ledger, another little squat.

**Sable:** _(Dance)_ I take what I like. Tonight I like dancing.

**Sable:** _(Travel)_ Did somebody just pass gas? Brand. It was Brand.

**Sable:** _(Travel)_ I could be robbing someone right now. Someone rich. Someone indoors.

**Sable:** _(Travel)_ A girl walks into a dungeon. That's it. That's the whole joke.

**Sable:** _(Travel)_ Nothing to steal, nothing to stab. Worst holiday ever.

**Sable:** _(Travel)_ I spy, with my little eye, something beginning with stone.

**Sable:** _(Travel)_ If the next room's empty too I'm picking Ilex's pockets for practice.

**Sable:** _(Travel)_ I've cased houses faster than we're casing this hole.

**Sable:** _(Travel)_ Who built this? And can I rob them instead?

**Sable:** _(Travel)_ I'd sing, but Brand said not to. Brand has no ear.

**Sable:** _(Travel)_ My knives are bored. Bored knives slip.

**Sable:** _(Travel)_ Twenty more steps and I'm charging a walking fee.

**Sable:** _(Travel)_ Is it just me, or does every corridor smell of regret?

**Sable:** _(Travel)_ A lady needs excitement. A lady is getting corridors.

**Sable:** _(Travel)_ Wake me when there's a lock. Any lock.

**Sable:** _(Travel)_ I've stolen things that moved more than this dungeon.

**Sable:** _(Travel)_ Shall we take bets on the next room? I say empty. I always win.

**Sable:** _(Travel)_ If I see one more torch bracket I'm stealing it out of spite.

**Sable:** _(Travel)_ Nothing's tried to kill us in ages. I'm almost insulted.

**Sable:** _(Travel)_ I miss the city. The city had things worth taking.

**Sable:** _(Travel)_ Walk, walk, walk. My thighs will be magnificent and nobody will see them.

**Sable:** _(TrapSprung)_ Oh, that's rude. That's just rude.

**Sable:** _(TrapSprung)_ I find the traps. That's my whole job. I found one.

**Sable:** _(TrapSprung)_ Nobody saw that. Nobody saw that.

**Sable:** _(TrapSprung)_ A girl steps on one plate. One.

**Sable:** _(TrapSprung)_ Ow. Also: ow.

**Sable:** _(TrapSprung)_ I'd have disarmed it if it had introduced itself.

**Sable:** _(TrapSprung)_ Fine. It's a lesson. I hate lessons.

**Sable:** _(TrapSprung)_ Who sets a trap for a lady? Someone I'd like to meet.

**Sable:** _(Fell)_ Graceful. I'm told I'm graceful.

**Sable:** _(Fell)_ A lady doesn't fall. She descends quickly.

**Sable:** _(Fell)_ Who moved the floor?

**Sable:** _(Fell)_ I'll have you know I landed on my feet. Eventually.

**Sable:** _(Fell)_ That's a hole. Nobody said hole.

**Sable:** _(Fell)_ Nothing broken. Nothing of mine, anyway.

**Sable:** _(Fell)_ I've fallen off better buildings than this.

**Sable:** _(Fell)_ Stop laughing, Brand. I can hear you not laughing.

**Sable:** _(PerfectBlow)_ Right in the soft bit.

**Sable:** _(PerfectBlow)_ Oh, that was pretty. Did everyone see?

**Sable:** _(PerfectBlow)_ Perfect. Like me.

**Sable:** _(PerfectBlow)_ Knife says hello. Knife says goodbye.

**Sable:** _(PerfectBlow)_ I'd bow but there's blood on the floor.

**Sable:** _(PerfectBlow)_ Clean. I'm very clean.

**Sable:** _(PerfectBlow)_ Precision, darling. Precision.

**Sable:** _(PerfectBlow)_ That one I'll remember fondly.

**Sable:** _(Parried)_ Nope.

**Sable:** _(Parried)_ Ooh, close. Not close enough.

**Sable:** _(Parried)_ Too slow, sweetheart.

**Sable:** _(Parried)_ Turned it. Turn around now, there's a good lad.

**Sable:** _(Parried)_ You telegraphed that. I read it twice.

**Sable:** _(Parried)_ My blade, your blade, my blade. Sorted.

**Sable:** _(Parried)_ Parry. Riposte. Pockets.

**Sable:** _(Parried)_ I've dodged drunks faster than you.

**Sable:** _(Mimic)_ Oh no. No no no. Chests are friends.

**Sable:** _(Mimic)_ That's a betrayal. That's personal.

**Sable:** _(Mimic)_ I touched that. I TOUCHED that.

**Sable:** _(Mimic)_ Gold doesn't bite. Gold doesn't bite!

**Sable:** _(Mimic)_ Kill it. Then we rob it. In that order.

**Sable:** _(Mimic)_ I'll never trust a hinge again.

**Sable:** _(Mimic)_ It's chewing. Why is it chewing.

**Sable:** _(Mimic)_ Right. New rule. Brand opens everything.

**Sable:** _(SecretFound)_ Oh, hello. What are you hiding?

**Sable:** _(SecretFound)_ A secret. My favourite kind of door.

**Sable:** _(SecretFound)_ Somebody didn't want me in here. Rude of them.

**Sable:** _(SecretFound)_ Dibs. Dibs on everything.

**Sable:** _(SecretFound)_ The wall was lying. I like a wall that lies.

**Sable:** _(SecretFound)_ Quiet, everyone. It might have gold in it.

**Sable:** _(SecretFound)_ This is why you bring me.

**Sable:** _(SecretFound)_ Hidden. Not from me.

**Sable:** _(EliteSeen)_ Ooh, a name. Fancy. Still bleeds.

**Sable:** _(EliteSeen)_ It's got airs. I hate airs.

**Sable:** _(EliteSeen)_ Named ones carry better pockets.

**Sable:** _(EliteSeen)_ Look at it. Thinks it's somebody.

**Sable:** _(EliteSeen)_ A champion. Lovely. Stab the champion.

**Sable:** _(EliteSeen)_ It glows. Things that glow die interesting.

**Sable:** _(EliteSeen)_ Oh, it's special. Let's make it less so.

**Sable:** _(EliteSeen)_ That one has a title. I'll have it off him.

**Sable:** _(Broke)_ I could just... take it.

**Sable:** _(Broke)_ Short. By a lot. By a scandalous lot.

**Sable:** _(Broke)_ Put the knife away, Sable. It's a shop.

**Sable:** _(Broke)_ Nobody tell him I counted twice.

**Sable:** _(Broke)_ This is why I steal. Honest work doesn't pay.

**Sable:** _(Broke)_ Too dear. Everything's too dear when you're dead broke.

**Sable:** _(Broke)_ I'll have that. Later. Quietly.

**Sable:** _(Broke)_ A lady should never hear the word 'afford'.

**Sable:** _(ShrineBoon)_ Ooh. Tingly. More, please.

**Sable:** _(ShrineBoon)_ Free things. My favourite things.

**Sable:** _(ShrineBoon)_ Stolen from a god. A good day.

**Sable:** _(ShrineBoon)_ I feel marvellous. I was marvellous before, but still.

**Sable:** _(ShrineBoon)_ That's going straight to my hips.

**Sable:** _(ShrineBoon)_ A gift. Nobody gives me gifts.

**Sable:** _(ShrineBoon)_ Shiny inside now. Lovely.

**Sable:** _(ShrineBoon)_ Don't ask what it cost. I didn't.

**Sable:** _(ShrineCurse)_ Oh, I don't like that. I don't like that at all.

**Sable:** _(ShrineCurse)_ It took something. Nobody takes from me.

**Sable:** _(ShrineCurse)_ Cursed. On a Tuesday.

**Sable:** _(ShrineCurse)_ That's going to show, isn't it.

**Sable:** _(ShrineCurse)_ Fine. A curse. I'll steal it back later.

**Sable:** _(ShrineCurse)_ I feel grubby. Grubbier.

**Sable:** _(ShrineCurse)_ Worth it. Probably. Ask me later.

**Sable:** _(ShrineCurse)_ A lady accepts a curse with grace. And swearing.

**Sable:** _(GambleWin)_ Yes! I knew it. I always know.

**Sable:** _(GambleWin)_ Luck is a skill, darling.

**Sable:** _(GambleWin)_ Oh, it's gorgeous. Mine.

**Sable:** _(GambleWin)_ The house loses. Lovely house.

**Sable:** _(GambleWin)_ Three guesses and one of them was right.

**Sable:** _(GambleWin)_ I was born lucky. And pretty.

**Sable:** _(GambleWin)_ Pay me again, little bundle.

**Sable:** _(GambleWin)_ I'll never doubt a bit of cloth again.

**Sable:** _(GambleDud)_ Oh, that's a swindle. I admire it. I hate it.

**Sable:** _(GambleDud)_ No. No, put it back. Put the gold back.

**Sable:** _(GambleDud)_ I've been robbed. Me. By cloth.

**Sable:** _(GambleDud)_ That's a rag. I paid for a rag.

**Sable:** _(GambleDud)_ Professional respect. Personal fury.

**Sable:** _(GambleDud)_ I'm stealing that money back. Watch me not.

**Sable:** _(GambleDud)_ Junk. Pretty junk. Still junk.

**Sable:** _(GambleDud)_ A lady never bets. A lady just did.

**Sable:** _(LowHp)_ Right, that's a lot of blood for a small girl.

**Sable:** _(LowHp)_ I'm fine. I'm lying. Help.

**Sable:** _(LowHp)_ Potion. Anyone. Please. Pretty please.

**Sable:** _(LowHp)_ This dress is ruined. So am I, a bit.

**Sable:** _(LowHp)_ Not dying. Refusing. Firmly.

**Sable:** _(LowHp)_ Somebody stand in front of me. Brand. You.

**Sable:** _(LowHp)_ I'd run but my legs have opinions.

**Sable:** _(LowHp)_ Hurt. Badly. Still gorgeous.

**Sable:** _(Hurt)_ Ow! That's my good side!

**Sable:** _(Hurt)_ Rude. Rude and painful.

**Sable:** _(Hurt)_ You've marked me. I mark back.

**Sable:** _(Hurt)_ That'll bruise. I bruise prettily.

**Sable:** _(Hurt)_ Hands off the merchandise.

**Sable:** _(Hurt)_ Hit a lady, would you.

**Sable:** _(Hurt)_ Ugh. Blood on the leathers.

**Sable:** _(Hurt)_ That one stung. Properly.

**Sable:** _(Loot)_ Mine. Obviously.

**Sable:** _(Loot)_ Ooh. Shiny. Pocket.

**Sable:** _(Loot)_ Finders keepers. I'm the finder.

**Sable:** _(Loot)_ Lovely. That's going somewhere safe. Me.

**Sable:** _(Loot)_ A gift from the dead. Thoughtful.

**Sable:** _(Loot)_ I'll appraise it. Later. Privately.

**Sable:** _(Loot)_ Something sparkly. My day improves.

**Sable:** _(Loot)_ That'll fetch a price. Or a smile.

**Sable:** _(Downed)_ Oh. That's a lot of floor.

**Sable:** _(Downed)_ Not dying. Just lying down. Fiercely.

**Sable:** _(Downed)_ Somebody pick me up. Gently.

**Sable:** _(Downed)_ I'm fine. I'm very not fine.

**Sable:** _(Downed)_ Dark. Is it dark? It's dark.

**Sable:** _(Downed)_ Tell my knives I loved them.

**Sable:** _(Downed)_ Down. Humiliating. Help.

**Sable:** _(Downed)_ I'll haunt whoever did that.

**Sable:** _(Revived)_ Alive! Still gorgeous. Checked.

**Sable:** _(Revived)_ Ugh. Who dragged me? Thank you. Ugh.

**Sable:** _(Revived)_ Back from the dead. Owe you a drink. Stolen.

**Sable:** _(Revived)_ Right. Revenge. Let me find my knives.

**Sable:** _(Revived)_ I'm up. Nobody saw me down.

**Sable:** _(Revived)_ Thank you. Don't make it a habit.

**Sable:** _(Revived)_ Death didn't want me. Can't blame it.

**Sable:** _(Revived)_ Up. Pockets intact. Good.

**Sable:** _(BossSeen)_ Oh. Oh, that's large.

**Sable:** _(BossSeen)_ That's the boss. Where's its purse?

**Sable:** _(BossSeen)_ Big and ugly. I'll go round the back.

**Sable:** _(BossSeen)_ Right. Nobody die. Especially me.

**Sable:** _(BossSeen)_ Lovely. A fight with a name on it.

**Sable:** _(BossSeen)_ I've robbed bigger. I haven't, actually.

**Sable:** _(BossSeen)_ Watch its hands. All of them.

**Sable:** _(BossSeen)_ Hello, gorgeous. Goodbye, gorgeous.

**Sable:** _(LevelUp)_ Ooh. I feel sharper. Sharper than before.

**Sable:** _(LevelUp)_ Better. Prettier too. Side effect.

**Sable:** _(LevelUp)_ Levelled. Is that the word? Levelled.

**Sable:** _(LevelUp)_ Growth. In a dungeon. Who'd have thought.

**Sable:** _(LevelUp)_ I'm getting good at this. I was good before.

**Sable:** _(LevelUp)_ More tricks. Fewer manners.

**Sable:** _(LevelUp)_ A girl improves. Watch out.

**Sable:** _(LevelUp)_ Sharper knives, sharper me.

**Sable:** _(Cheer)_ Darlings! Still gorgeous, still rich-ish!

**Sable:** _(Cheer)_ Hey! Who's the best party in this hole? Us. Obviously.

**Sable:** _(Cheer)_ Cheer up, you lot. I'm here. That's the good news.

**Sable:** _(Cheer)_ Oi! Alive and pretty! Say it back!

**Sable:** _(CheerAnswer)_ Woo! Someone had to say it.

**Sable:** _(CheerAnswer)_ Yes, darling. Now where's the gold?

**Sable:** _(CheerAnswer)_ Gorgeous and alive. Both true.

**Sable:** _(CheerAnswer)_ Cheering. Quietly. Don't look.

**Sable:** _(CheerAnswer)_ Right behind you. Checking your pockets.

**Sable:** _(CheerAnswer)_ Oi, oi! Wait, that's mine.

**Wren:** _(Kill)_ Through the eye. It won't walk back.

**Wren:** _(Kill)_ One down. I'll want that arrow back.

**Wren:** _(Kill)_ Nothing down here dies surprised.

**Wren:** _(Hurt)_ Clipped. Still shooting.

**Wren:** _(Hurt)_ Mark the archer on the ledge. I have.

**Wren:** _(Hurt)_ Blood on the string. That slows a draw.

**Wren:** _(Loot)_ Fletchings. Whoever's they were is past caring.

**Wren:** _(Loot)_ Someone packed this for a trip home.

**Wren:** _(Loot)_ Take the rope. Down here rope is kindness.

**Wren:** _(LevelUp)_ Steadier draw. The dark's still steadier.

**Wren:** _(LevelUp)_ I'm learning these tunnels. That frightens me.

**Wren:** _(LevelUp)_ Longer sight. Wish I liked what's in it.

**Wren:** _(BossSeen)_ Wide target. Thick hide. Keep it at range.

**Wren:** _(BossSeen)_ Pillars first, then shoot. Not the reverse.

**Wren:** _(BossSeen)_ Ground's shaking. Not from that one.

**Wren:** _(Possess)_ Wren.

**Wren:** _(Possess)_ Quiet now. I'm in front.

**Wren:** _(Possess)_ Follow. Don't talk.

**Wren:** _(Downed)_ Quiver's empty anyway. Go on without me.

**Wren:** _(Downed)_ Count the exits. I counted. Use the second.

**Wren:** _(Downed)_ The floor's beating. I'd rather it didn't.

**Wren:** _(Revived)_ Hands on the bow. Good. Step aside.

**Wren:** _(Revived)_ Thank you. Now watch the high ledges.

**Wren:** _(Revived)_ Alive, and still owed every penny.

**Wren:** _(Kill)_ Marked it. Loosed. Done.

**Wren:** _(Kill)_ Two steps closer and it would have been you.

**Wren:** _(Kill)_ Wind's dead down here. Makes it easy.

**Wren:** _(Kill)_ It stood still. Its mistake.

**Wren:** _(Kill)_ Good arrow. Shame about the fletching.

**Wren:** _(Kill)_ Quiet. Listen for the next one.

**Wren:** _(Kill)_ Drop. Stay dropped.

**Wren:** _(Idle)_ I've tracked deer that moved more than you. Dead ones.

**Wren:** _(Idle)_ Standing still works for a tree. You lack the leaves.

**Wren:** _(Idle)_ The trail's cold and so are my feet. Walk.

**Wren:** _(Idle)_ If this is stealth, it's the loud kind.

**Wren:** _(Idle)_ Two things don't move down here: the dead and you. Keep it to the one.

**Wren:** _(Idle)_ The arrow doesn't fly until you nock it. Same with you and this corridor.

**Wren:** _(Idle)_ I count sixty breaths. The dungeon counted them too.

**Wren:** _(Idle)_ Lost? The way on is the one you're not looking at.

**Wren:** _(Idle)_ Even a stump knows which way the sun went. Pick a direction.

**Wren:** _(Idle)_ I'll scout ahead. By which I mean anywhere but here.

**Wren:** _(Dance)_ One arrow, one corpse, one little dance.

**Wren:** _(Dance)_ I don't dance for just anyone. You're welcome, dead thing.

**Wren:** _(Dance)_ The hunt's done. The hips take it from here.

**Wren:** _(Dance)_ Rangers don't dance, they say. Rangers lie.

**Wren:** _(Dance)_ Marked, shot, dropped. Now the fun part.

**Wren:** _(Dance)_ Quiet, both of you. I'm dancing.

**Wren:** _(Dance)_ Tracked it, dropped it, now I shake it off.

**Wren:** _(Dance)_ A ranger celebrates once. This is the once.

**Wren:** _(Dance)_ My arrows fly straight. My hips don't.

**Wren:** _(Dance)_ Forest rules: kill clean, dance short.

**Wren:** _(Dance)_ Two squats for the bow, two for the arrow.

**Wren:** _(Dance)_ I learned this from a stag. Don't ask.

**Wren:** _(Dance)_ Don't tell the trees.

**Wren:** _(Dance)_ Four thousand bricks. One dance. Fair.

**Wren:** _(Dance)_ I don't smile. I squat.

**Wren:** _(Dance)_ Something had to die for me to dance. It did.

**Wren:** _(Dance)_ Nock, loose, wiggle. In that order.

**Wren:** _(Dance)_ The woods never saw me do this. The woods are lucky.

**Wren:** _(Dance)_ It stood still. I didn't.

**Wren:** _(Dance)_ One wave for the dead, then we move.

**Wren:** _(Travel)_ Fighting for a living is overrated anyway.

**Wren:** _(Travel)_ No tracks. Nothing's been through here but us and the damp.

**Wren:** _(Travel)_ I've stalked deer for a day and been less bored.

**Wren:** _(Travel)_ The wind would tell me something. There's no wind.

**Wren:** _(Travel)_ My arrows are getting dusty in the quiver.

**Wren:** _(Travel)_ Mark the wall. We've been here. Twice, now.

**Wren:** _(Travel)_ Quiet dungeon. Quiet's where things hide. Or where nothing is.

**Wren:** _(Travel)_ I could track a mouse through this. There are no mice.

**Wren:** _(Travel)_ Nothing to shoot. I'm counting bricks instead. Four thousand.

**Wren:** _(Travel)_ Out in the woods there'd be a bird by now. Something.

**Wren:** _(Travel)_ Anyone else's boots talking to them? Mine won't shut up.

**Wren:** _(Travel)_ If this is a trail, it's the dullest one I've walked.

**Wren:** _(Travel)_ A ranger's patience is long. Not this long.

**Wren:** _(Travel)_ I'll scout ahead. Ahead is more of this.

**Wren:** _(Travel)_ I miss trees. I'd settle for a shrub.

**Wren:** _(Travel)_ Nothing moving. I checked. I keep checking.

**Wren:** _(Travel)_ The bowstring's going slack from boredom. So am I.

**Wren:** _(Travel)_ Steady pace. Steady nothing.

**Wren:** _(Travel)_ If something jumps us now I'll thank it.

**Wren:** _(Travel)_ The stone has no smell. Give me mud and a dead fox any day.

**Wren:** _(TrapSprung)_ That one's Agnes. Agnes got me.

**Wren:** _(TrapSprung)_ Missed the plate. First time.

**Wren:** _(TrapSprung)_ Noted. Won't be a second.

**Wren:** _(TrapSprung)_ Everyone stop. Let me look at my foot.

**Wren:** _(TrapSprung)_ Trap. Yes. I know.

**Wren:** _(TrapSprung)_ Should've seen the dust line. Didn't.

**Wren:** _(TrapSprung)_ Count that one against me.

**Wren:** _(TrapSprung)_ They hide them better down here. Noted.

**Wren:** _(Fell)_ Down. Still here.

**Wren:** _(Fell)_ Ledge gave. Or I did.

**Wren:** _(Fell)_ Deeper than it looked.

**Wren:** _(Fell)_ Landed. Mostly.

**Wren:** _(Fell)_ Trees have branches. Note the difference.

**Wren:** _(Fell)_ Ankle's fine. Pride isn't.

**Wren:** _(Fell)_ Mind that edge.

**Wren:** _(Fell)_ I'll climb back. Don't help.

**Wren:** _(PerfectBlow)_ Clean.

**Wren:** _(PerfectBlow)_ Centre. Where I aimed.

**Wren:** _(PerfectBlow)_ That's the shot.

**Wren:** _(PerfectBlow)_ Good arrow. Good release.

**Wren:** _(PerfectBlow)_ Through. Done.

**Wren:** _(PerfectBlow)_ Breathe, loose, done.

**Wren:** _(PerfectBlow)_ I'll want that one back. It earned it.

**Wren:** _(PerfectBlow)_ Dead centre.

**Wren:** _(Parried)_ No.

**Wren:** _(Parried)_ Turned it.

**Wren:** _(Parried)_ Saw it coming.

**Wren:** _(Parried)_ Not that one.

**Wren:** _(Parried)_ Off the bow. Mind the bow.

**Wren:** _(Parried)_ Slow swing.

**Wren:** _(Parried)_ Read it.

**Wren:** _(Parried)_ Try again.

**Wren:** _(Mimic)_ Chest's alive. Shoot the chest.

**Wren:** _(Mimic)_ Knew the lock looked wrong.

**Wren:** _(Mimic)_ Teeth. Lid. Kill it.

**Wren:** _(Mimic)_ Told you it was breathing.

**Wren:** _(Mimic)_ That's not wood.

**Wren:** _(Mimic)_ Back. Give me a line.

**Wren:** _(Mimic)_ It's hungry. Don't be the meal.

**Wren:** _(Mimic)_ Never trusted that one.

**Wren:** _(SecretFound)_ Seam in the wall. Knew it.

**Wren:** _(SecretFound)_ Hidden room. Watch the floor.

**Wren:** _(SecretFound)_ Somebody built this to be missed.

**Wren:** _(SecretFound)_ Found it. Don't crowd.

**Wren:** _(SecretFound)_ Air moved. That's how.

**Wren:** _(SecretFound)_ A room nobody meant us to see.

**Wren:** _(SecretFound)_ Quiet in here. Good.

**Wren:** _(SecretFound)_ Mark it on the map.

**Wren:** _(EliteSeen)_ Named one. Mark it.

**Wren:** _(EliteSeen)_ Watch its tells. It's different.

**Wren:** _(EliteSeen)_ That's the dangerous one.

**Wren:** _(EliteSeen)_ Big. Marked. Mine.

**Wren:** _(EliteSeen)_ It's not like the others. Move like it isn't.

**Wren:** _(EliteSeen)_ Named. Means careful.

**Wren:** _(EliteSeen)_ Eyes on the glowing one.

**Wren:** _(EliteSeen)_ That one leads. Take the leader.

**Wren:** _(Broke)_ Can't.

**Wren:** _(Broke)_ Too much. Walk on.

**Wren:** _(Broke)_ Not for that price.

**Wren:** _(Broke)_ No coin. No matter.

**Wren:** _(Broke)_ I've lived on less.

**Wren:** _(Broke)_ Put it down.

**Wren:** _(Broke)_ Trees were free.

**Wren:** _(Broke)_ Next floor, maybe.

**Wren:** _(ShrineBoon)_ Took it.

**Wren:** _(ShrineBoon)_ Feels right. Like good wind.

**Wren:** _(ShrineBoon)_ Something settled. Good.

**Wren:** _(ShrineBoon)_ I'll trust it. Carefully.

**Wren:** _(ShrineBoon)_ Better. Noted.

**Wren:** _(ShrineBoon)_ A gift from the stone. Odd.

**Wren:** _(ShrineBoon)_ Keener. Keep moving.

**Wren:** _(ShrineBoon)_ That'll help.

**Wren:** _(ShrineCurse)_ Paid.

**Wren:** _(ShrineCurse)_ Something's wrong. Carry it.

**Wren:** _(ShrineCurse)_ Marked. Felt it.

**Wren:** _(ShrineCurse)_ Heavier. Walk anyway.

**Wren:** _(ShrineCurse)_ A price. Fine.

**Wren:** _(ShrineCurse)_ Cursed. Noted.

**Wren:** _(ShrineCurse)_ It'll pass. Or it won't.

**Wren:** _(ShrineCurse)_ I've carried worse.

**Wren:** _(GambleWin)_ Good one.

**Wren:** _(GambleWin)_ Lucky. Take it.

**Wren:** _(GambleWin)_ Better than the last.

**Wren:** _(GambleWin)_ The cloth hid something worth it.

**Wren:** _(GambleWin)_ Fortune. Don't chase it.

**Wren:** _(GambleWin)_ That'll do.

**Wren:** _(GambleWin)_ Keep it. Move.

**Wren:** _(GambleWin)_ Won. Odd feeling.

**Wren:** _(GambleDud)_ Junk.

**Wren:** _(GambleDud)_ Knew it.

**Wren:** _(GambleDud)_ Cloth and nothing.

**Wren:** _(GambleDud)_ Last time.

**Wren:** _(GambleDud)_ Wasted.

**Wren:** _(GambleDud)_ Put it down. Walk.

**Wren:** _(GambleDud)_ Money gone. Fine.

**Wren:** _(GambleDud)_ Should've bought arrows.

**Wren:** _(LowHp)_ Hurt. Bad.

**Wren:** _(LowHp)_ Low. Cover me.

**Wren:** _(LowHp)_ Can't take another.

**Wren:** _(LowHp)_ Bleeding. Keep shooting.

**Wren:** _(LowHp)_ Need a moment.

**Wren:** _(LowHp)_ Still up. Barely.

**Wren:** _(LowHp)_ Step back. Need air.

**Wren:** _(LowHp)_ One more and I'm down.

**Wren:** _(Hurt)_ Hit.

**Wren:** _(Hurt)_ Hurt. Still shooting.

**Wren:** _(Hurt)_ Felt that.

**Wren:** _(Hurt)_ Deep. Not done.

**Wren:** _(Hurt)_ Close. Too close.

**Wren:** _(Hurt)_ Marked me.

**Wren:** _(Hurt)_ Bleeding. Fine.

**Wren:** _(Hurt)_ Should've moved.

**Wren:** _(Loot)_ Useful.

**Wren:** _(Loot)_ Taking it.

**Wren:** _(Loot)_ Might serve.

**Wren:** _(Loot)_ Bag it.

**Wren:** _(Loot)_ Good find.

**Wren:** _(Loot)_ Someone left this. Shame.

**Wren:** _(Loot)_ Worth the weight.

**Wren:** _(Loot)_ Keep.

**Wren:** _(Downed)_ Down.

**Wren:** _(Downed)_ Can't... get up.

**Wren:** _(Downed)_ Help. Quiet about it.

**Wren:** _(Downed)_ Floor. Cold.

**Wren:** _(Downed)_ Not yet. Not yet.

**Wren:** _(Downed)_ Someone. Please.

**Wren:** _(Downed)_ Lost my footing. Lost the rest.

**Wren:** _(Downed)_ Dark. Hurry.

**Wren:** _(Revived)_ Up.

**Wren:** _(Revived)_ Owe you.

**Wren:** _(Revived)_ Back. Quiet.

**Wren:** _(Revived)_ Breathing. Good.

**Wren:** _(Revived)_ Thanks.

**Wren:** _(Revived)_ Still here. Still shooting.

**Wren:** _(Revived)_ Head's ringing. Fine.

**Wren:** _(Revived)_ Again, then.

**Wren:** _(BossSeen)_ There. The big one.

**Wren:** _(BossSeen)_ Mark it. Everyone.

**Wren:** _(BossSeen)_ Keep moving. Don't stand.

**Wren:** _(BossSeen)_ It's slow. Make it slower.

**Wren:** _(BossSeen)_ Big. Loud. Mine.

**Wren:** _(BossSeen)_ Range. Give me range.

**Wren:** _(BossSeen)_ That's the one that guards the stairs.

**Wren:** _(BossSeen)_ Breathe. Loose. Again.

**Wren:** _(LevelUp)_ Better.

**Wren:** _(LevelUp)_ Learned.

**Wren:** _(LevelUp)_ Keener.

**Wren:** _(LevelUp)_ Something clicked.

**Wren:** _(LevelUp)_ Good. Move on.

**Wren:** _(LevelUp)_ Stronger. Don't fuss.

**Wren:** _(LevelUp)_ Eye's sharper.

**Wren:** _(LevelUp)_ Growing. Odd place for it.

**Wren:** _(Cheer)_ Still here.

**Wren:** _(Cheer)_ Hey. Good work. Walk on.

**Wren:** _(Cheer)_ Up, all of you. Not dead yet.

**Wren:** _(Cheer)_ Steady. Together. Good.

**Wren:** _(CheerAnswer)_ Mm.

**Wren:** _(CheerAnswer)_ Aye.

**Wren:** _(CheerAnswer)_ Here.

**Wren:** _(CheerAnswer)_ Good.

**Wren:** _(CheerAnswer)_ Still up.

**Wren:** _(CheerAnswer)_ Walk on.

**Ilex:** _(Kill)_ Burned clean. The drains may thank me.

**Ilex:** _(Kill)_ Reduced to component parts. Tidy enough.

**Ilex:** _(Kill)_ That one had a soul once. Pity.

**Ilex:** _(Hurt)_ My robe. My good robe. My only robe.

**Ilex:** _(Hurt)_ Pain is data. I would prefer less data.

**Ilex:** _(Hurt)_ Someone intercept the next one, as a courtesy.

**Ilex:** _(Loot)_ Ink, and the hand shook. They knew.

**Ilex:** _(Loot)_ Council paper, and still legible. Unprecedented.

**Ilex:** _(Loot)_ A reliquary. Nobody tell the priest downstairs.

**Ilex:** _(LevelUp)_ The words sit easier. The dungeon taught them.

**Ilex:** _(LevelUp)_ I understand more. I recommend understanding less.

**Ilex:** _(LevelUp)_ Enlightenment, acquired in a drain. Note the irony.

**Ilex:** _(BossSeen)_ A specimen with opinions. Keep it off me.

**Ilex:** _(BossSeen)_ I can unmake it. Buy me nine slow breaths.

**Ilex:** _(BossSeen)_ A matter of drains, we were told. Observe the matter of drains.

**Ilex:** _(Possess)_ Ilex. The thinking has arrived.

**Ilex:** _(Possess)_ Ah. The competent hands. At last.

**Ilex:** _(Possess)_ Lead by me. Mind Gerald.

**Ilex:** _(Downed)_ Not lying down. That is his word, not mine.

**Ilex:** _(Downed)_ He dreams. I can hear it from here. Thirsty, mostly.

**Ilex:** _(Downed)_ Carry the notes if you can't carry me.

**Ilex:** _(Revived)_ Upright, and considerably less sanguine.

**Ilex:** _(Revived)_ Good. The spell was half said. Stand back.

**Ilex:** _(Revived)_ Upright again. The far side was poorly lit and smelt of orc.

**Ilex:** _(Kill)_ Burned clean. Its throat may thank me.

**Ilex:** _(Kill)_ Fire answers most theological questions.

**Ilex:** _(Kill)_ Observe: it stops when the heat exceeds the will.

**Ilex:** _(Kill)_ I did warn it. Briefly.

**Ilex:** _(Kill)_ Entropy. I merely hurried it along.

**Ilex:** _(Kill)_ Note the smell. Sulphur, mostly. Mostly.

**Ilex:** _(Kill)_ Another argument settled in my favour.

**Ilex:** _(Kill)_ Do try to leave the next one for the sword. My hands are cold.

**Ilex:** _(Idle)_ Fascinating. A man standing in a corridor. I shall write a monograph.

**Ilex:** _(Idle)_ The mind moves the body. In your case the message appears to be lost in transit.

**Ilex:** _(Idle)_ I have watched candles make decisions faster.

**Ilex:** _(Idle)_ Shall I cast Haste on you, or would that only waste the spell?

**Ilex:** _(Idle)_ We are losing light, warmth and, I suspect, brain tissue. Walk.

**Ilex:** _(Idle)_ Contemplation is a virtue. Loitering is merely a habit. Guess which this is.

**Ilex:** _(Idle)_ The door will not open from pure longing. I have tested this.

**Ilex:** _(Idle)_ Is this a tactical pause, or did you forget what feet are for?

**Ilex:** _(Idle)_ Every second you stand there the dungeon grows a little wiser than you. It did not need the help.

**Ilex:** _(Idle)_ Marvellous. We have found the one puzzle you cannot solve: forwards.

**Ilex:** _(Dance)_ Observe: the victory squat. Taught at no academy.

**Ilex:** _(Dance)_ Spell, then shimmy. The second part is not in the grimoire.

**Ilex:** _(Dance)_ I have reduced you to ash, and I shall now wiggle about it.

**Ilex:** _(Dance)_ A scholar's dance. Dignified. Mostly.

**Ilex:** _(Dance)_ Science wins again, and science has rhythm.

**Ilex:** _(Dance)_ Thaumaturgy, then choreography.

**Ilex:** _(Dance)_ The academy would expel me for this hip rock. Worth it.

**Ilex:** _(Dance)_ Two squats: the recommended dose after any incineration.

**Ilex:** _(Dance)_ Observe the mage in motion. Rare. Treasure it.

**Ilex:** _(Dance)_ Footnote seventeen: the author danced. Reviewers were unkind.

**Ilex:** _(Dance)_ My robes were not cut for this. I persist.

**Ilex:** _(Dance)_ Gerald, avert your eyes. This is not for puddles.

**Ilex:** _(Dance)_ Ashes to ashes, hips to hips.

**Ilex:** _(Dance)_ The spell cost mana. The dance is free.

**Ilex:** _(Dance)_ A wiggle in the name of natural philosophy.

**Ilex:** _(Dance)_ Let the record show: the mage can move.

**Ilex:** _(Dance)_ Combustion complete. Commence the shimmy.

**Ilex:** _(Dance)_ I calculated the angle of every squat. Flawless.

**Ilex:** _(Dance)_ Dignity is for the unburnt.

**Ilex:** _(Dance)_ Rhythm is merely applied mathematics. Behold.

**Ilex:** _(Travel)_ At this pace my thesis will write itself from boredom.

**Ilex:** _(Travel)_ I have memorised the mortar pattern. It is not interesting.

**Ilex:** _(Travel)_ A spell unused is a spell wasted. I am very wasteful today.

**Ilex:** _(Travel)_ I could enchant a lantern to walk for us. Nobody asked.

**Ilex:** _(Travel)_ The academy never covered tedium. An oversight.

**Ilex:** _(Travel)_ Corridor. Corridor. A rich variety of corridor.

**Ilex:** _(Travel)_ My robes gather dust at a measurable rate. I have measured it.

**Ilex:** _(Travel)_ I count forty-one torches, no foes and one pressing need to sit.

**Ilex:** _(Travel)_ If knowledge is power, I am learning a great deal about flagstones.

**Ilex:** _(Travel)_ Observe: nothing. Again.

**Ilex:** _(Travel)_ Walking is merely falling forwards with manners.

**Ilex:** _(Travel)_ I would summon something to fight, but it would also be bored.

**Ilex:** _(Travel)_ My feet file a formal complaint.

**Ilex:** _(Travel)_ I have begun naming the puddles. This one is Gerald.

**Ilex:** _(Travel)_ A dungeon this empty should be ashamed of itself.

**Ilex:** _(Travel)_ Somebody say something clever. Not Brand.

**Ilex:** _(Travel)_ Fireball. Just to see colour. No? Very well.

**Ilex:** _(Travel)_ My mana regenerates faster than my interest.

**Ilex:** _(Travel)_ The council pays by the floor, not by the fight. Walk on.

**Ilex:** _(Travel)_ Even the echoes have stopped bothering.

**Ilex:** _(TrapSprung)_ A mechanism! Fascinating. Painful, but fascinating.

**Ilex:** _(TrapSprung)_ I was observing the ceiling. The floor took offence.

**Ilex:** _(TrapSprung)_ Note for the journal: do not step on the obvious slab.

**Ilex:** _(TrapSprung)_ Crude. Effective. I shall be insufferable about it later.

**Ilex:** _(TrapSprung)_ Someone with a very simple mind built that. It worked.

**Ilex:** _(TrapSprung)_ A puddle would never do this to me.

**Ilex:** _(TrapSprung)_ I detected it. Afterwards. Detection is detection.

**Ilex:** _(TrapSprung)_ Ingenious. Would somebody remove it from my leg.

**Ilex:** _(Fell)_ Gravity remains undefeated.

**Ilex:** _(Fell)_ I was falling forwards with manners. Then without.

**Ilex:** _(Fell)_ A drop. Unmarked. Typical workmanship.

**Ilex:** _(Fell)_ Observe: the bottom. I have found it.

**Ilex:** _(Fell)_ My dignity is up there. Fetch it, would you.

**Ilex:** _(Fell)_ A hole of considerable depth. I have measured it with my face.

**Ilex:** _(Fell)_ Nothing is broken that cannot be argued back together.

**Ilex:** _(Fell)_ If Gerald lives down here he has gone very quiet.

**Ilex:** _(PerfectBlow)_ Precisely as calculated.

**Ilex:** _(PerfectBlow)_ Observe: perfection. Briefly.

**Ilex:** _(PerfectBlow)_ The timing was exact. The result, inevitable.

**Ilex:** _(PerfectBlow)_ I rarely miss. I never gloat. Much.

**Ilex:** _(PerfectBlow)_ A clean casting. Note it.

**Ilex:** _(PerfectBlow)_ There. Theory, applied.

**Ilex:** _(PerfectBlow)_ Exquisite. I surprise even myself.

**Ilex:** _(PerfectBlow)_ That was the correct amount of fire.

**Ilex:** _(Parried)_ Declined.

**Ilex:** _(Parried)_ A deflection. Elementary.

**Ilex:** _(Parried)_ You'll have to be quicker than that. Everyone is.

**Ilex:** _(Parried)_ Staff meets steel. Staff wins.

**Ilex:** _(Parried)_ I anticipated that. I anticipate most things.

**Ilex:** _(Parried)_ Not today, and probably not tomorrow.

**Ilex:** _(Parried)_ Reflexes. Trained. Expensive.

**Ilex:** _(Parried)_ Turned aside. Noted. Returned.

**Ilex:** _(Mimic)_ A predatory container. Remarkable.

**Ilex:** _(Mimic)_ It mimics a chest and resents being opened. Relatable.

**Ilex:** _(Mimic)_ Burn the furniture. Burn it now.

**Ilex:** _(Mimic)_ Teeth in oak. The anatomy is absurd.

**Ilex:** _(Mimic)_ I shall name it after it dies. Not Gerald.

**Ilex:** _(Mimic)_ Observe: the lid is a jaw. Observe faster.

**Ilex:** _(Mimic)_ Nature is vile and so is carpentry.

**Ilex:** _(Mimic)_ Fire. The universal lockpick.

**Ilex:** _(SecretFound)_ A concealed chamber. Deduction rewarded.

**Ilex:** _(SecretFound)_ Someone hid this. Someone was wrong to.

**Ilex:** _(SecretFound)_ Observe: the wall was a lie of masonry.

**Ilex:** _(SecretFound)_ Architecture with a secret. Delicious.

**Ilex:** _(SecretFound)_ Let the record show I suspected the wall.

**Ilex:** _(SecretFound)_ Dust undisturbed. We are the first in an age.

**Ilex:** _(SecretFound)_ A hidden room. Possibly a hidden Gerald.

**Ilex:** _(SecretFound)_ Knowledge, concealed. I am offended and delighted.

**Ilex:** _(EliteSeen)_ A named specimen. How vain.

**Ilex:** _(EliteSeen)_ It has affixes. Read them. Then kill them.

**Ilex:** _(EliteSeen)_ Ah. Something with a title. Titles burn.

**Ilex:** _(EliteSeen)_ Note the aura. Note it from range.

**Ilex:** _(EliteSeen)_ Distinct. Dangerous. Documented.

**Ilex:** _(EliteSeen)_ A champion of the dark. I am unimpressed and alert.

**Ilex:** _(EliteSeen)_ Special, is it? Fire is also special.

**Ilex:** _(EliteSeen)_ Observe its marks. They mean something. Probably pain.

**Ilex:** _(Broke)_ Insufficient funds. Insulting price.

**Ilex:** _(Broke)_ I am a scholar. Scholars are poor on principle.

**Ilex:** _(Broke)_ Daylight robbery, in a place with no daylight.

**Ilex:** _(Broke)_ I could afford it if I sold Brand.

**Ilex:** _(Broke)_ Economics. The dismal science.

**Ilex:** _(Broke)_ Knowledge is priceless. That is priced.

**Ilex:** _(Broke)_ Put it back before I calculate the markup.

**Ilex:** _(Broke)_ I shall simply want it from a distance.

**Ilex:** _(ShrineBoon)_ Power, freely offered. Suspicious. Accepted.

**Ilex:** _(ShrineBoon)_ I feel improved. Statistically.

**Ilex:** _(ShrineBoon)_ The altar approves of me. Naturally.

**Ilex:** _(ShrineBoon)_ A boon. I shall document the side effects.

**Ilex:** _(ShrineBoon)_ Knowledge enters the blood. Pleasant.

**Ilex:** _(ShrineBoon)_ I am, if anything, more remarkable.

**Ilex:** _(ShrineBoon)_ Accepted. I will want a receipt.

**Ilex:** _(ShrineBoon)_ The stone is generous. Stone rarely is.

**Ilex:** _(ShrineCurse)_ A malediction. I shall study it from inside.

**Ilex:** _(ShrineCurse)_ Ah. The price. One always forgets the price.

**Ilex:** _(ShrineCurse)_ Cursed. How very medieval.

**Ilex:** _(ShrineCurse)_ Something has attached itself to my person. Rude.

**Ilex:** _(ShrineCurse)_ I accept this with dignity and resentment.

**Ilex:** _(ShrineCurse)_ The altar has opinions about me. Noted.

**Ilex:** _(ShrineCurse)_ A curse. I'll write a paper. Posthumously, perhaps.

**Ilex:** _(ShrineCurse)_ Unpleasant. Instructive. Unpleasant.

**Ilex:** _(GambleWin)_ Probability smiles. Briefly, and on me.

**Ilex:** _(GambleWin)_ A fine result. I shall pretend I predicted it.

**Ilex:** _(GambleWin)_ The expected value was poor. The value was not.

**Ilex:** _(GambleWin)_ Chance, correctly guessed.

**Ilex:** _(GambleWin)_ Luck is statistics in a good mood.

**Ilex:** _(GambleWin)_ Excellent. Do not tell Sable I gambled.

**Ilex:** _(GambleWin)_ A worthy bundle. Shocking.

**Ilex:** _(GambleWin)_ Fortune, for once, reads my notes.

**Ilex:** _(GambleDud)_ The expected value asserts itself.

**Ilex:** _(GambleDud)_ A lesson in probability. Overpriced.

**Ilex:** _(GambleDud)_ Rubbish, elegantly wrapped. I respect the wrapping.

**Ilex:** _(GambleDud)_ I knew the odds. I ignored them. Human.

**Ilex:** _(GambleDud)_ Disappointing. Like most empirical results.

**Ilex:** _(GambleDud)_ It is, technically, an object.

**Ilex:** _(GambleDud)_ Sable, stop laughing. Sable.

**Ilex:** _(GambleDud)_ A dud. The universe keeps its books.

**Ilex:** _(LowHp)_ I am rather badly damaged.

**Ilex:** _(LowHp)_ Blood. Mine. Unacceptable.

**Ilex:** _(LowHp)_ The body fails before the mind. Hurry.

**Ilex:** _(LowHp)_ Potion. Now. I'll explain the chemistry later.

**Ilex:** _(LowHp)_ This is a critical quantity of injury.

**Ilex:** _(LowHp)_ I am not well. Someone be useful.

**Ilex:** _(LowHp)_ Fading. Documenting. Fading.

**Ilex:** _(LowHp)_ Help, in the plainest possible terms.

**Ilex:** _(Hurt)_ Ow. That is quite enough.

**Ilex:** _(Hurt)_ A hit. On me. Outrageous.

**Ilex:** _(Hurt)_ Pain. Noted. Returned shortly.

**Ilex:** _(Hurt)_ You struck a scholar. Barbarian.

**Ilex:** _(Hurt)_ That will bruise interestingly.

**Ilex:** _(Hurt)_ Blood is not an argument. Fire is.

**Ilex:** _(Hurt)_ I am wounded and irritated.

**Ilex:** _(Hurt)_ Unpleasant. Instructive.

**Ilex:** _(Loot)_ Interesting. Possibly valuable. Mine.

**Ilex:** _(Loot)_ A find. I shall catalogue it.

**Ilex:** _(Loot)_ Someone dropped knowledge. Or a hat.

**Ilex:** _(Loot)_ Noted and pocketed.

**Ilex:** _(Loot)_ Potentially useful. Probably cursed. Keeping it.

**Ilex:** _(Loot)_ An artefact. Or junk. Research required.

**Ilex:** _(Loot)_ I claim this in the name of scholarship.

**Ilex:** _(Loot)_ Acquired. Observe my restraint.

**Ilex:** _(Downed)_ I appear to be horizontal.

**Ilex:** _(Downed)_ Down. Undignified. Help.

**Ilex:** _(Downed)_ The floor and I have met.

**Ilex:** _(Downed)_ Consciousness... negotiable.

**Ilex:** _(Downed)_ Somebody competent, please.

**Ilex:** _(Downed)_ This is not how the paper ends.

**Ilex:** _(Downed)_ I can see the ceiling. It is poor.

**Ilex:** _(Downed)_ Assistance. Promptly. I'll be gracious.

**Ilex:** _(Revived)_ Vertical again. Praise competence.

**Ilex:** _(Revived)_ Thank you. I shall return the favour academically.

**Ilex:** _(Revived)_ Alive. The paper continues.

**Ilex:** _(Revived)_ My head. My everything. Thank you.

**Ilex:** _(Revived)_ Revived. Hypothesis: I am hard to kill.

**Ilex:** _(Revived)_ I saw nothing. Disappointing.

**Ilex:** _(Revived)_ Up. Irritated. Functional.

**Ilex:** _(Revived)_ Back. The dark was underwhelming.

**Ilex:** _(BossSeen)_ Ah. The resident. Enormous.

**Ilex:** _(BossSeen)_ Observe its pattern. Then end it.

**Ilex:** _(BossSeen)_ A guardian. Impressive. Flammable.

**Ilex:** _(BossSeen)_ Large. Slow. Over-confident. Noted.

**Ilex:** _(BossSeen)_ It has a signature. Everything burns the same.

**Ilex:** _(BossSeen)_ Behold: a problem with a name.

**Ilex:** _(BossSeen)_ Stay out of its reach. I shall stay out of all reach.

**Ilex:** _(BossSeen)_ A boss. The paper gets a chapter.

**Ilex:** _(LevelUp)_ I have improved. Measurably.

**Ilex:** _(LevelUp)_ Knowledge, earned by bleeding. Inefficient.

**Ilex:** _(LevelUp)_ Stronger. I shall adjust the paper.

**Ilex:** _(LevelUp)_ A new tier of me. Gasp.

**Ilex:** _(LevelUp)_ Progress. Documented.

**Ilex:** _(LevelUp)_ Power accrues to the deserving. Eventually.

**Ilex:** _(LevelUp)_ I feel smarter. Imagine.

**Ilex:** _(LevelUp)_ Experience. Technically a kind of learning.

**Ilex:** _(Cheer)_ Colleagues! We persist. Remarkable.

**Ilex:** _(Cheer)_ Attention! A moment of unearned confidence!

**Ilex:** _(Cheer)_ Behold us: alive, and in defiance of the evidence.

**Ilex:** _(Cheer)_ Onward! For scholarship, and for spite!

**Ilex:** _(CheerAnswer)_ Quite.

**Ilex:** _(CheerAnswer)_ Hear, hear. Reluctantly.

**Ilex:** _(CheerAnswer)_ I concur, loudly.

**Ilex:** _(CheerAnswer)_ Yes. Onward. Et cetera.

**Ilex:** _(CheerAnswer)_ A cheer. How tribal. Hurrah.

**Ilex:** _(CheerAnswer)_ Noted. Enthusiastically noted.

**viewer_foehammer:** _(Kill)_ Ha! Felt that one in my teeth.

**viewer_foehammer:** _(Kill)_ Stand behind me, I said. See why?

**viewer_foehammer:** _(Kill)_ Hammer says no.

**viewer_foehammer:** _(Kill)_ Another for the stone.

**viewer_foehammer:** _(Kill)_ They don't make them like they used to. Good.

**viewer_foehammer:** _(Kill)_ Next! My arm's warm now.

**viewer_foehammer:** _(Kill)_ Down you go. Mind the step.

**fighter_tamsin:** _(Kill)_ Ha! Went down like a rotten prop. That's a share, that is.

**fighter_tamsin:** _(Kill)_ Partners, remember. Even shares of whatever that was.

**fighter_tamsin:** _(Kill)_ Hit him like a hard seam. He cracked first.

**fighter_tamsin:** _(Kill)_ I've broken harder rock before breakfast.

**fighter_tamsin:** _(Kill)_ Check it for coin. Go on. I'll wait.

**fighter_tamsin:** _(Kill)_ Right. Who's next for the lamp?

**fighter_tamsin:** _(Kill)_ Not bad for a miner, eh?

**fighter_oskar:** _(Kill)_ Poor iron. It bent.

**fighter_oskar:** _(Kill)_ Don't thank me.

**fighter_oskar:** _(Kill)_ That's what the old man's tools are for.

**fighter_oskar:** _(Kill)_ Patience. They always come to the hammer.

**fighter_oskar:** _(Kill)_ Cast badly. Broke easily.

**fighter_oskar:** _(Kill)_ Next. I've all day.

**fighter_oskar:** _(Kill)_ I've quenched hotter.

**fighter_ione:** _(Kill)_ Forgive me. Forgive me.

**fighter_ione:** _(Kill)_ The light held. It held.

**fighter_ione:** _(Kill)_ I did not want that. I did it.

**fighter_ione:** _(Kill)_ The Verger taught me the prayers for the dead. I say them anyway.

**fighter_ione:** _(Kill)_ Stay close. Please. Stay close to the lantern.

**fighter_ione:** _(Kill)_ Is it... it is. All right. All right.

**fighter_ione:** _(Kill)_ Saints keep it, whatever it was.
