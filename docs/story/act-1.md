# The Gullet of Gorm

_Act 1: floors 1-5_

## Floor 1: The Lychgate

_Mood: a mine head under a church, a dead orc still talking_

**Story beat:** The Herald (Morg Ashtusk, the orc) attacks at the mine head. viewer_foehammer, who keeps the gate, and the party kill him, and his voice comes straight back up from below: the foundry will recast him. cast_crane hires the party and explains the job (sour wells, shaking ground, wardens who went down and did not return) and asks for the council notices posted at the mine head. cast_maud goes down ahead on her rounds, not knowing she drowned: help me clear the grates, the water has to go down.

**Narrator:** _(tier 1 entry)_ The lychgate opens on the church's crypts, and the crypts on the mines. Dunmarrow's dead are up and walking, raised to keep you from the drains below. The Skin.

**Narrator:** _(floor intro)_ You arrive at the head of the old mines. A huge orc has come up the crypt stair, and the dwarf who keeps the gate is already fighting him.

**Narrator:** _(RoomEnter room 0)_ The orc at the gate is Morg Ashtusk, Herald of the Sleepless, an evil god. He has taken these mines as his master's base, and everything down here answers to him.

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

**Alderman Hesketh Crane:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ I took the council notices off the board at the mine head this morning and put them in my bread-basket. Then the orc came up the stair, I ran, and the basket stayed in the first cellar. Bring it back. Don't read them.

**Alderman Hesketh Crane:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Good. Good. Don't eat the cloth.

**Alderman Hesketh Crane:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Then it rots down there with the rest of the parish's goodwill. Suit yourselves.

**Alderman Hesketh Crane:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Every notice, and the cloth folded on top. Good. Nothing anyone needs to read. The council thanks you in writing.

**Alderman Hesketh Crane:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Keep it. I shall enter the basket on the loss ledger, under drainage.

### Maud Sorrel

**Maud Sorrel:** _(node m1_step)_ Mind the step, dear, it's wet. Help me clear the grates; I'm late on my rounds. The water has to go down. It always has to go down.

  - **Party (choice):** Who are you, mother? _[to m1_who]_
  - **Party (choice):** You should go back up. It isn't safe. _[to m1_back]_
  - **Party (choice):** Mind how you go, mother. _[Farewell]_

**Maud Sorrel:** _(node m1_who)_ Maud Sorrel, dear. Drain-warden, thirty-eight years. I was on my rounds, and then the water... well, never mind. Wet? I've been wetter.

  **Wren:** _(aside)_ She's soaked through. Wrong.

  - **Party (choice):** Thirty-eight years? _[to m1_back]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m1_back)_ Thirty-eight years, dear, and I've never missed a round, so I'm not going up now. The wells up top are sour; that's the grates, packed solid. The water has to go down.

  - **Party (choice):** Mind how you go, mother. _[Farewell]_

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

_Objective: jailbreak in the frame; the shipped seed generates jailbreak_

**Story beat:** Jailbreak: the prisoners are captured drain-wardens, grey and half-turned in the Herald's cages, already hard to tell apart. The ones the party frees say the traps are new and somebody is building them, that the ones they finish go grey and join the packs (husks), that cast_grisel's crew went down first and nobody has seen them since, and that cast_ulf still keeps the pump works but nobody has heard from him since spring.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ These cells hold the drain-wardens who went down and never came up: grey, half-turned, already hard to tell apart. Get them out while you can. Nobody has heard from Old Ulf at the pump works since spring either.

**Narrator:** _(RoomEnter room 7)_ Even here you can hear the wardens in the cells calling out: the traps are new and somebody is building them, and anyone who finishes turning goes grey and joins the packs. Grisel Thorn's crew, they say, went down first.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ I am Morg Ashtusk, Herald of the Sleepless, and you will hear me on every floor down. Those are wardens in my bloody cages, hirelings, and the finished ones join my packs.

**Morg Ashtusk:** _(Capture, jailbreak)_ Back in the cage. Another week and nobody will tell that one from the rest.

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

**Deacon Tobit Hale:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ You've seen more of these crypts than I have lately. Tell me plainly how it is. I am told I am a good listener.

**Deacon Tobit Hale:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Say it plainly. I would rather be wrong out loud than obedient in a whisper.

**Deacon Tobit Hale:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ As you wish. The candle will keep until you change your mind. It keeps very well.

**Deacon Tobit Hale:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Thank you for telling me how it is. I know what I have to do now.

**Deacon Tobit Hale:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Go down, then. I'll keep praying. Somebody has to.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter; not played: only an objective floor opens with a taunt)_ My dead keep this church now. Let the deacon pray, hirelings; nobody down here is bloody listening but me.

**Morg Ashtusk:** _(Crushed)_ Flat as a coin. I built that press this spring, and it does a better day's work than you sorry sods.

### Hordes

**Narrator:** _(The Shelved Dead, Enter)_ The stacked bone shifts, and shifts again, and then the wall gives up its tenants all at once.

## Floor 4: The Chantry

_Mood: rotten timber, rising foul air, a log that stops_

_Objective: toxic in the frame; the shipped seed generates toxic_

**Story beat:** Foul gas rises from the stagnant water while the party searches the chantry. cast_crane has come down to check progress and finds clue_ledger, the wardens' log: the drains cleared every season for three hundred years, until this spring's last entry, 'By the Verger's order, no one below.' Relieved it was the church and not the council, he says so too quickly.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ You come into rotten timber and a green air rising from cellars where the water has stood still since spring. Somewhere in it, a man with a ledger is apologising to nobody.

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

**Alderman Hesketh Crane:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Three pages, no more. One in the alms drift, one along the timbering, one up in the chantry. I would go myself, but the chain of office is heavy and I am, frankly, a coward.

**Alderman Hesketh Crane:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Good. Good. I shall stand here and guard the heap, which is important work.

**Alderman Hesketh Crane:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Quite right. Paper is paper, and nobody reads the old ones.

**Alderman Hesketh Crane:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Three hundred years of clearings, and then this spring: 'By the Verger's order, no one below.' So it was the church. Not the council. What a relief. I mean, what a shame.

**Alderman Hesketh Crane:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Burnt, soaked, eaten. Just as well. A ledger is only a ledger if somebody reads it.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, toxic)_ Smell that, hirelings? Standing water, gone rotten, rising out of the cellars. The stairs are at the bottom of it. Do hurry; you're paid by the floor.

**Morg Ashtusk:** _(GasThickens, toxic)_ Up to your knees now, you poor sods. The town bought your swords, not your lungs.

### Hordes

**Narrator:** _(The Parish Overflow, Enter)_ The crossing wall gives up its mortar and the pack pours through at you, grey and alike. A few of them still wear wardens' boots.

## Floor 5: Canon Ludd

_Mood: a dead canon standing guard on a choked drain_

**Story beat:** Boss: Canon Ludd, the Herald's first lieutenant, raised from the church's dead, guards the first great drain, choked on purpose. When he falls the party turns the sluice wheel, the first drain runs and the shaking eases for a while; the narrator says so. The Herald sneers and promises worse.

**Narrator:** _(floor intro)_ The mines have stood shut since spring by order of the Verger, who keeps the crypt door. Under the chantry you find the first great drain, its grate packed solid on purpose, and a dead canon saying the office over it.

**Narrator:** _(BossSeen)_ Canon Ludd, raised from the church's own dead to guard the drain, swings his censer at empty benches. The ledge above him is bare of ornament; use it.

**Narrator:** _(BossDead)_ The first drain runs again, and above you the shaking eases for a while. Far below, the orc's voice: 'One drain, hirelings. I choked three more, and your god is still hungry.'

### Hordes

**Narrator:** _(The Charnel Overflow, Enter)_ The charnel wall gives at both seams, and the church's dead walk out at you, a few greyer things in wardens' coats among them.

### Boss: Canon Ludd

**Canon Ludd:** _(intro)_ I am the Herald's first lieutenant. He shut this drain, and I keep it shut. Kneel, strangers; the church's dead have room for you.

**Canon Ludd:** _(death)_ Turn the sluice wheel, then. Let the water down to the god, and the ground will lie still a while. I was a canon here, once.
