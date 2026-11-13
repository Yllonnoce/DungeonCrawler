# The Gullet of Gorm

_Act 4: floors 16-20_

## Floor 16: The Dream Door

_Mood: veined marble, blue light, the dream wearing thin_

**Story beat:** The dream wears thin here and the Sleepless's own kind come through. clue_letter, the Verger's letter to the Herald: 'The way will be open by midwinter.' With told_tobit cast_tobit brings it himself and turns on his master, wanting him stopped; without it cast_maud carries it (it came down to her on the water) and says the deacon has been taken for the rite. fighter_ione, ashamed of what the Verger has done to her church, joins to take back the church's silver ewer.

**Narrator:** _(tier 4 entry)_ This close to the sleeping god, the dream wears thin. It is the only door between this world and the Sleepless, and things from the other side are pressing through. The Dream.

**Narrator:** _(floor intro)_ You come down onto marble with blue light running in its veins. Near the foot of the stair, a young sister of Gorm's church is keeping a lantern lit.

**Narrator:** _(RoomEnter room 5, needs told_tobit)_ Deacon Tobit stands among the fallen pillars, soaked from the climb down, with a letter in his hand. He has plainly read it more than once.

**Narrator:** _(RoomEnter room 9, not if told_tobit)_ Maud Sorrel stands in the weeping font with water running from her sleeves, holding a wet letter out to you as if it were nothing much.

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

**Deacon Tobit Hale:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ More of my master's letters are lying in these halls. Bring them to me and I'll read them; I'm thorough. I always am.

**Deacon Tobit Hale:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Thank you. Mind the marble where it's cracked; things come through the cracks down here.

**Deacon Tobit Hale:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ No. Quite right. I'll look for them myself, slowly.

**Deacon Tobit Hale:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ His letters, every one. Now I know why he did it.

**Deacon Tobit Hale:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Then I'll never know why. I'll have to make do with knowing that he did.

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

**Maud Sorrel:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ More of the Verger's letters, dear, wherever the water left them. Bring them to me.

**Maud Sorrel:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Good. Mind the marble where it's cracked, dears.

**Maud Sorrel:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ As you like, dear. The water keeps what it's given.

**Maud Sorrel:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ All his letters. Now we know why.

**Maud Sorrel:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Gone to pulp in the wet, then. No matter, dear. We know what he did.

### Floor taunts

**Morg Ashtusk:** _(Descend, needs told_tobit)_ Your deacon found my letter from the Verger, hirelings: 'The way will be open by midwinter.' Bloody priests always write it down. The Verger opened the crypt door for me in spring.

**Morg Ashtusk:** _(Descend, not if told_tobit)_ Read my letter from the Verger, hirelings? 'The way will be open by midwinter.' Bloody priests always write it down. The Verger opened the crypt door for me, and now his deacon kneels in his rite.

### Hordes

**Narrator:** _(The Door Gives Way, Enter)_ The marble splits along its blue veins and the shades pour through at you, grey and alike, with the stalkers from the other side behind them.

## Floor 17: The Windpipe

_Mood: purple gloom, gas climbing, an alderman with a shovel_

_Objective: toxic in the frame; the shipped seed generates toxic_

**Story beat:** Gas climbs the galleries. cast_crane has come down himself with a shovel and owns the truth: the council was glad of the Verger's order, because stopping the wardens' pay saved money, and he signed it. He holds a sluice open so the party can get past the gas, and stays holding it; he does not betray them. With grisel_forgiven he can say she and her crew are back on the upper drains.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ Purple slate, and the gas climbing the galleries off the standing water. Somewhere ahead a sluice wheel groans, and a man out of breath is apologising to it.

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

**Alderman Hesketh Crane:** _(node n_verger)_ Then he opened the door to that orc. He did it for a god, and I signed away the wardens without once asking why he'd shut the mines. I can't tell which of us I'm angrier with.

  - **Party (choice):** Why come down yourself, Crane? _[to n_confess]_
  - **Party (choice):** Did Grisel get her crew out? _[needs grisel_forgiven; to n_grisel]_
  - **Party (choice):** Hold it, then. We're going. _[Farewell]_

**Alderman Hesketh Crane:** _(node n_grisel)_ Her crew got out, what's left of them. They're back on the upper drains, on full pay back to spring. I signed that too, and gladly.

  - **Party (choice):** Why come down yourself, Crane? _[to n_confess]_
  - **Party (choice):** Hold it, then. We're going. _[Farewell]_

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, toxic)_ Smell that, hirelings? The water stands all the way down now, and the gas comes off it. Your alderman came down after you with a bloody shovel. Hurry, before he hurts himself.

**Morg Ashtusk:** _(GasLethal, toxic)_ That's my air in your lungs now, hirelings. Your alderman's holding a sluice open for you, and you're still too bloody slow to use it.

**Morg Ashtusk:** _(Descend)_ Your alderman's still hanging off his wheel up there, hirelings. His council saved itself the wardens' wages and left my drains to me. Cheapest bloody bargain my master ever got.

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

**Maud Sorrel:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Ask nothing of me, or ask for my strength. Either way, bring the water down.

**Maud Sorrel:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Said, then. Carry it down with you.

**Maud Sorrel:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Then sit a while. I have slept three hundred years; I can wait a little longer.

**Maud Sorrel:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Thank you for coming down to me.

**Maud Sorrel:** _(quest fail; shown in the message log if the quest fails, not voiced)_ No matter. Bring the water down, and I will sleep either way.

### Floor taunts

**Morg Ashtusk:** _(Descend, needs wake_gorm)_ Your god is half awake, hirelings, and the dream is thin as paper. I can hear my master on the other side of it. You did that for him, and nobody paid you a bloody penny.

**Morg Ashtusk:** _(Descend, needs let_him_dream)_ You told your god to go back to sleep, hirelings. A god too bloody frightened to wake. My master is frightened of nothing, and he is still at the door.

### Hordes

**Narrator:** _(The Near Mouth, Chest)_ The lid lifts and the wall behind it lets go: shades pour out of the breaches like cold poured from a jug, and the corridor into the rotunda fills from end to end.

**Narrator:** _(The Far Mouth, Enter)_ Two breaches open in the marble together, and the Sleepless's kind come through at you: shades in front, and the archers of the other side behind.

## Floor 19: The Last Station

_Mood: cold tiles, three galleries, a rite to wake a god_

_Objective: ritual in the frame; the shipped seed generates ritual_

**Story beat:** Ritual: cast_quill, the Verger, conducts the rite to wake the god with the church's ewer in his hands, while cast_maud steadies the god against it and the party holds off the waves around her; he speaks to the party but is not fought, and fighter_ione faces him. Without told_tobit, cast_tobit is in the rite, half-changed, and breaking it lets him go. With wake_gorm the shaking is worse and more of the Sleepless's kind come through. When the rite breaks the Verger is left kneeling with nothing answering him.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ Three cold galleries meet at a shrine of white tile. Somewhere ahead the Verger is at his rite to wake the god, and the floor shakes in time with his prayer.

**Narrator:** _(RoomEnter room 1, not if wake_gorm)_ Maud Sorrel sits at the altar with her hands flat on the stone, holding the god asleep. From the next gallery comes the Verger's voice, praying for the god to wake. Keep the Sleepless's kind off her.

**Narrator:** _(RoomEnter room 1, needs wake_gorm)_ Maud Sorrel sits at the altar with her hands flat on the stone, holding the god asleep, but the floor will not stop shaking: Gorm is nearer waking for the strength he lent you. Keep the Sleepless's kind off her.

**Narrator:** _(RoomEnter room 1, needs told_tobit)_ Forty years the Verger prayed to Gorm and was never answered. The orc promised him a god who would answer, and for that the Verger opened the crypt door. Now he prays that god through.

**Narrator:** _(RoomEnter room 1, not if told_tobit)_ Forty years the Verger prayed to Gorm unanswered; for the orc's promise of a god who would answer, he opened the crypt door. Deacon Tobit's voice is in that rite, slow and grey. Break it, and it lets the deacon go.

**Narrator:** _(RoomEnter room 7)_ Absalom Quill, the Verger, kneels here with the church's silver ewer in his hands, praying aloud for the god to wake. He is the man who opened the crypt door to the orc.

**Narrator:** _(RoomEnter room 7, not if told_tobit)_ Deacon Tobit kneels at his master's side, grey to the wrists and half turned, his eyes on the ewer.

### Maud Sorrel

**Maud Sorrel:** _(node m_open; not played: the rite's figure, whom the party defends, cannot be talked to)_ There you are, dears. I'll sit at the altar and hold him asleep against the Verger's rite. You keep the Sleepless's kind off me while I do.

  - **Party (choice):** We'll hold them off, Maud. _[AcceptQuest]_
  - **Party (choice):** Why does he need holding? _[to m_which]_
  - **Party (choice):** Not yet, Maud. _[DeclineQuest]_
  - **Party (choice):** It's done, Maud. _[TurnIn; to m_done]_

**Maud Sorrel:** _(node m_which; not played: the rite's figure, whom the party defends, cannot be talked to)_ The Verger is praying him awake, dear, and the orc's kind are helping. I can keep him asleep if nobody pulls me off the stone.

  - **Party (choice):** We asked for his strength. Hold him anyway. _[AcceptQuest; needs wake_gorm]_
  - **Party (choice):** We let him sleep. Keep him there. _[AcceptQuest; not if wake_gorm]_
  - **Party (choice):** Rest a moment, Maud. _[Farewell]_

**Maud Sorrel:** _(node m_done; not played: the rite's figure, whom the party defends, cannot be talked to)_ He's settled, dear. The Verger's rite has nothing left to pull on. Go on down to the last drain; he's thirsty.

  - **Party (choice):** Then we go down. _[TurnIn]_

_Quest: Hold Him Steady (HoldOut): Hold the three galleries while Maud Sorrel keeps the god asleep._

**Maud Sorrel:** _(quest offer; not played: the rite's figure, whom the party defends, cannot be talked to)_ Three galleries and one old woman at the altar, dears. Keep them off me until he settles.

**Maud Sorrel:** _(quest accept; not played: the rite's figure, whom the party defends, cannot be talked to)_ Good. Don't let them pull me off the stone.

**Maud Sorrel:** _(quest decline; not played: the rite's figure, whom the party defends, cannot be talked to)_ Then I'll hold him as long as I can on my own, dear.

**Maud Sorrel:** _(quest complete; not played: the rite's figure, whom the party defends, cannot be talked to)_ There. He's settled, and the rite has nothing left to pull on.

**Maud Sorrel:** _(quest fail; not played: the rite's figure, whom the party defends, cannot be talked to)_ I'm down, dears. Pick me up, or the Verger's rite gets its way.

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

**Absalom Quill:** _(node v_ewer)_ That ewer blessed the drains every spring for three hundred years. Tonight it pours for the god who comes after. The girl who kept my candles may have it back when the new god has come.

  - **Party (choice):** You let the orc in. _[to v_door]_
  - **Party (choice):** Kneel there, then. _[Farewell]_

### Floor taunts

**Morg Ashtusk:** _(RitualStart, ritual, needs wake_gorm)_ Go on, warden, hold your god steady. He's half awake already, thanks to the town's hirelings, and every bloody wave I send shakes him further.

**Morg Ashtusk:** _(RitualStart, ritual, not if wake_gorm)_ So a drowned warden thinks she can hold a god asleep against my priest's rite. Bloody touching. Send them in.

**Morg Ashtusk:** _(FloorEnter, ritual, needs wake_gorm)_ My old priest is down here praying your god the rest of the way awake, hirelings. A drowned warden is all that holds your god asleep now. I'd bloody bet on the priest.

**Morg Ashtusk:** _(FloorEnter, ritual, not if wake_gorm)_ The old priest is down here praying your god awake, hirelings, and a drowned warden is trying to keep him asleep. I know which one I'd bloody bet on.

**Morg Ashtusk:** _(RitualistDown, ritual)_ Down goes the warden. Pick her up, hirelings, or the old priest gets his god.

**Morg Ashtusk:** _(RitualDone, ritual)_ So the rite breaks. Look at the old priest, hirelings: on his knees, and nothing answering him. Forty years he prayed, and he still can't get a god to come when called.

**Morg Ashtusk:** _(Descend, needs told_tobit)_ Your old priest dropped the ewer when his rite broke, hirelings. The church can have its jug back. One drain left, and I'm standing on it.

**Morg Ashtusk:** _(Descend, not if told_tobit)_ The old priest dropped the ewer when his rite broke, hirelings, and your half-grey deacon walked out of it. The church can have its jug back.

### Hordes

**Narrator:** _(The Breath of the Mouths, Enter)_ The three mouths in the tilework exhale at once, and the dark comes out of them on its hands and knees.

**Narrator:** _(What the Oddfellows Dug Into, Chest)_ The lid lifts, and the wall behind the old camp gives way: shades come crawling out at you, grey and alike, with a caller of the other side behind them.

## Floor 20: The Mouth

_Mood: the last great drain, braziers, the orc again_

**Story beat:** Boss: the Herald, Morg Ashtusk, recast whole and larger in his true form, waits at the last great drain and speaks of the Verger with contempt. Kill him, turn the sluice wheel, and the water runs down to Gorm; the narrator makes plain what it did: the god drinks (sinking back from waking with wake_gorm, never stirring without it), the dream holds, the ground goes still and the wells of Dunmarrow run sweet. With wake_gorm, Gorm, half awake, strikes at him and he dies saying his master is almost through; without it he dies contemptuous, saying the Sleepless will send another. viewer_foehammer finishes the job.

**Narrator:** _(floor intro)_ The last stair brings you down to the Mouth, where the last great drain should pour down to the god. Its grate is packed solid, braziers burn around it, and the orc is waiting there.

**Narrator:** _(BossSeen)_ Morg Ashtusk stands over the last great drain, not the orc body you killed at the gate but what the foundry was building him into: his true form. Kill him and turn the sluice wheel, or the mines come down on Dunmarrow.

**Narrator:** _(BossDead, needs wake_gorm)_ You turn the sluice wheel, and the last great drain runs. The water pours down to the god, and he drinks and slowly sinks back from waking. The dream holds; it is thin, but the door stays shut.

**Narrator:** _(BossDead, not if wake_gorm)_ You turn the sluice wheel, and the last great drain runs. The water pours down to the god, and he drinks without ever stirring. The dream holds whole, and the door to the Sleepless stays shut.

**Narrator:** _(BossDead)_ Far above you the ground goes still, and the wells of Dunmarrow run sweet. The mines will stand. One floor up the Verger kneels on, and nothing answers him. The job is done, and the town lives.

### Floor taunts

**Morg Ashtusk:** _(Descend; not played in the story: it would only be heard going on past floor 20 into the endless depths)_ Come down to the Mouth, hirelings. The last drain is mine.

**Morg Ashtusk:** _(FloorEnter; not played: only an objective floor opens with a taunt)_ The Mouth, hirelings. The last drain, and your god right under your feet. Mind where you stand.

**Morg Ashtusk:** _(MemberDowned)_ That's one of the town's hired shovels broken. Nobody will pay you for it.

**Morg Ashtusk:** _(Idle; not played: the dawdle taunt is for objective floors)_ Standing about, hirelings? You're paid by the floor, and this is the last one. Come and earn it.

### Hordes

**Narrator:** _(The Gallery Shades, Enter)_ The gallery wall splits between the pillars, and the shades pour out at you in one long grey rush, with the stalkers of the other side behind them.

**Narrator:** _(The Breach Shades, Alarm)_ Somewhere behind you a runner reaches the breach, and it answers: shade on shade, climbing out of the stone with archers at their backs.

### Boss: Morg Ashtusk

**Morg Ashtusk:** _(intro)_ Killed me at the gate, didn't you? I came back bigger. Your Verger is still upstairs on his knees, waiting for his reward. Priests. Turn that sluice wheel if you can reach it, hirelings.

**Morg Ashtusk:** _(half)_ Is this what the town paid for? Your god is hiding in his sleep, hirelings, and you are only day labour. I can do this all night.

**Morg Ashtusk:** _(althalf, if wake_gorm)_ That was your god. He reached up out of his sleep and struck me. Good. Every blow he lands wakes him further, and my master is right behind the door.

**Morg Ashtusk:** _(death)_ Turn your wheel, then. My master will send another. Your god has to sleep for ever, hirelings. My master only has to wait.

**Morg Ashtusk:** _(altdeath, if wake_gorm)_ Your god struck me down from his sleep. Ha. It cost him, hirelings: he's nearly awake, and my master is almost through. Listen at the door.
