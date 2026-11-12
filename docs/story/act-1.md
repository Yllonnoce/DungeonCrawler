# The Gullet of Gorm

_Act 1: floors 1-5_

## Floor 1: The Lychgate

_Mood: a mine head under a church, a dead orc still talking_

**Story beat:** The Herald (Morg Ashtusk, the orc) attacks at the mine head. viewer_foehammer, who keeps the gate, and the party kill him, and his voice comes straight back up from below: the foundry will recast him. cast_crane hires the party and explains the job (sour wells, shaking ground, wardens who went down and did not return) and asks for the council notices posted at the mine head. cast_maud goes down ahead on her rounds, not knowing she drowned: help me clear the grates, the water has to go down.

**Narrator:** _(tier 1 entry)_ The lychgate opens on the church's crypts, and the crypts on the mines. Dunmarrow's dead are up and walking, raised to keep you from the drains below. The Skin.

**Narrator:** _(floor intro)_ The crypt stair under the lychgate is the head of the old mines. Up it has come a huge orc, Morg Ashtusk, and the dwarf who keeps the gate is already fighting him.

**Narrator:** _(RoomEnter room 0)_ The orc lies dead at the gate, and still his voice comes back up the shaft from far below: 'Kill me, hirelings. My foundry will cast me again, bigger.' He calls himself the Herald.

### Alderman Hesketh Crane

**Alderman Hesketh Crane:** _(node gate)_ Alderman Crane, for the council. That orc's dead, and still I heard its voice come back up the shaft, swearing it would be cast again. The drains below need clearing, and I am not the man to do it.

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

**Maud Sorrel:** _(node m1_back)_ Up? Not yet, dear. Thirty-eight years I've never missed a round. The wells up top are sour, you know; that's the grates, packed solid. The water has to go down.

  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

### Hordes

**Narrator:** _(Seam in the Vestry Wall, Enter)_ The vestry wall opens along a seam, and dry things come out of it sideways.

**Narrator:** _(Coats Off Their Hooks, Chest)_ The chest lid lifts, and the cloakroom answers: grey husks shrugging their way out of the coats on the hooks.

**Narrator:** _(The Diggers Come Up, Enter)_ Gravediggers' walk, and the gravediggers come up through their own breaches.

**Narrator:** _(The Rubble Stands Up, Chest)_ Behind the fallen columns the rubble stands up. One of them stands taller than the rest.

**Narrator:** _(Wading Out of the Font, Enter)_ The font water goes grey, and things wade out of the breaches to meet you in it.

**Narrator:** _(Neighbours at the Hut, Enter)_ The squatters' hut shudders. The breaches behind it cough up neighbours.

## Floor 2: The Vestibule

_Mood: moss in the mortar, grey prisoners who look alike_

_Objective: jailbreak_

**Story beat:** Jailbreak: the prisoners are captured drain-wardens, grey and half-turned in the Herald's cages, already hard to tell apart. The ones the party frees say the traps are new and somebody is building them, that the ones they finish go grey and join the packs (husks), that cast_grisel's crew went down first and nobody has seen them since, and that cast_ulf still keeps the pump works but nobody has heard from him since spring.

**Narrator:** _(floor intro)_ These cells hold the drain-wardens who went down and never came up, grey and half-turned, already hard to tell apart. Grisel Thorn's crew went down first. Get them out while you can.

**Narrator:** _(RoomEnter room 7)_ Freed, the wardens say the traps are new and somebody is building them, and the finished ones go grey and join the packs. Nobody has heard from Old Ulf at the pump works since spring.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ I am Morg Ashtusk, the Herald, and you killed me at the gate. Here I am anyway. Those are drain-wardens in my bloody cages; the ones I finish go grey and join my packs.

**Morg Ashtusk:** _(Capture, jailbreak)_ {prisoner} goes back in the cage. Another week and nobody will tell that one from the rest.

### Hordes

**Narrator:** _(The Wandered Congregation, Chest)_ The mortar splits at both ends of the vestibule, and the grey pack comes through. Some of them were drain-wardens this spring.

## Floor 3: The Nave

_Mood: chalk-white bone chapel, a deacon praying alone_

**Story beat:** cast_tobit prays in the Nave for Gorm to save the town; his master the Verger went below in spring, he says, to pray for it. The first crushing trap is on this floor. Decide: tell him the god is starving and no help is coming (flag told_tobit), or let him pray (flag kept_his_saint). Told, he is shaken and resolves to find out what happened to the drains; not told, he is grateful and keeps praying.

**Narrator:** _(floor intro)_ Chalk-white bone stacked to the vaulting, and one candle lit in the chancel, where a young deacon is praying. Some of the walls down here are new, and built to crush.

### Deacon Tobit Hale

**Deacon Tobit Hale:** _(node greet)_ Pray with me, would you? The wells are sour and the ground shakes, but if we ask him properly, Gorm will save the town. He always has.

  **Brand:** _(aside)_ No promises, deacon.

  - **Party (choice):** You're down here alone? _[to ask]_
  - **Party (choice):** Talk, deacon. We're listening. _[AcceptQuest; to ask]_

**Deacon Tobit Hale:** _(node ask)_ Quite alone. My master the Verger went below in spring to pray for the town, and I keep the church till he's back. Tell me true: does Gorm hear us down there?

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

**Narrator:** _(floor intro)_ Rotten timber, and a green air rising from cellars where the water has stood still since spring. Somewhere in it, a man with a ledger is apologising to nobody.

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

**Narrator:** _(The Parish Overflow, Enter)_ The crossing wall gives up its mortar and the pack pours through, grey and alike. A few of them still wear wardens' boots.

## Floor 5: Canon Ludd

_Mood: a dead canon standing guard on a choked drain_

**Story beat:** Boss: Canon Ludd, the Herald's first lieutenant, raised from the church's dead, guards the first great drain, choked on purpose. When he falls the party turns the sluice wheel, the first drain runs and the shaking eases for a while; the narrator says so. The Herald sneers and promises worse.

**Narrator:** _(floor intro)_ Under the chantry runs the first great drain, or it should. Its grate has been packed solid on purpose, and a dead canon stands over it, still saying the office to empty pews.

**Narrator:** _(BossSeen)_ Canon Ludd, raised from the church's own dead to guard the drain, swings his censer at empty benches. The ledge above him is bare of ornament; use it.

**Narrator:** _(BossDead)_ The first drain runs again. Above, the shaking eases for a while. Far below, the orc's voice: 'One drain, hirelings. I choked three more.'

### Hordes

**Narrator:** _(The Charnel Overflow, Enter)_ The charnel wall gives at both seams. The church's dead come out of it walking, and a few greyer things in wardens' coats among them.

### Boss: Canon Ludd

**Canon Ludd:** _(intro)_ I am the Herald's first lieutenant. He shut this drain, and I keep it shut. Kneel, strangers; the church's dead have room for you.

**Canon Ludd:** _(death)_ Turn the wheel, then. Let the water down to him, and the ground will lie still a while. I was a canon here, once.
