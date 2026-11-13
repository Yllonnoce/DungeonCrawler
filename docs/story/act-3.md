# The Gullet of Gorm

_Act 3: floors 11-15_

## Floor 11: The Hearth

_Mood: riveted plate, ember light, a cheerful old founder_

_Objective: none in the frame; the shipped seed generates shiftingvault_

**Story beat:** The old pump works, now the Herald's foundry. cast_ulf keeps it, cheerfully casting 'tools' for a customer who pays in gold; his eyes are going and he never sees the whole of what he pours. His journeyman fighter_oskar saw the whole drawings, knows better, and joins the party. cast_maud says plainly that she drowned, that the engine here once sent the water down, and that the god is weaker every day the third drain stays shut.

**Narrator:** _(tier 3 entry)_ Heat, and the thump of an engine. This is the pump works, built to push the water down to the god. The Herald has taught it to cast iron instead. The Heart.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ Heat, and the thump of a great engine. This is the pump works, which pushed the water down the drains for three hundred years. The orc has made it his foundry, and somebody at the hearth is singing.

**Narrator:** _(RoomEnter room 8)_ The Great Hearth. The orc's iron comes warm off the old founder's sand, and the engine that should be pumping water feeds the fire instead.

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

**Old Ulf:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Three hounds out of the pit and into my coal. Kill them and I'll show you a mould I'm proud of, which is all of them, so bring patience.

**Old Ulf:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Good lad. Go down the slag run and don't let them get behind you; they bite where the armour's thin and they know where that is.

**Old Ulf:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Suit yourselves. They'll eat, the forge runs cool, and my order goes out late. I've not sent an order late in sixty years.

**Old Ulf:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ There's the draw. Here, take this off the shelf for your trouble. Good work, that. Everything off my shelves is.

**Old Ulf:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Still chewing, are they. Well. I've worked colder, and I've worked with worse than you.

### Maud Sorrel

**Maud Sorrel:** _(node m11_shh)_ There you are, dear. I worked it out on the way down: I drowned, back in the spring. No matter. Keep the water going down to Gorm. He gets weaker every day the third drain stays shut.

  - **Party (choice):** What was this place, Maud? _[to m11_faces]_
  - **Party (choice):** How did you drown? _[to m11_warm]_
  - **Party (choice):** Mind how you go, Maud. _[Farewell]_

**Maud Sorrel:** _(node m11_faces)_ The pump works, dear. That big engine used to push the water down the drains. Now it drives a forge and casts things instead, and the water stands.

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

_Objective: jailbreak in the frame; the shipped seed generates jailbreak_

**Story beat:** Jailbreak: cast_grisel's crew are caged here, taken first and further gone than the prisoners on floor 2, nearly grey and nearly pack (cinders): proof the turned become the packs. With grisel_forgiven she helps bring them out one at a time; with grisel_condemned she took the Herald's keys to save herself and stood as his head jailer, and the party finds her dead by the cage door with his keys still on her belt, cut down by his iron when they came for the cages.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ Kiln brick and cages. The orc keeps his longest prisoners here: Grisel Thorn's crew, taken first in the spring and turned the furthest.

**Narrator:** _(RoomEnter room 11)_ The cages hold Grisel Thorn's crew, taken first in the spring and nearly gone: grey to the eyes and past speaking. A few days more and they would be running with the cinders.

**Narrator:** _(RoomEnter room 11, not if grisel_forgiven)_ Grisel Thorn lies dead by the cage door, the orc's keys still on her belt. To save herself she kept her own crew caged for him, and the moment you reached this floor, he had his iron cut her down.

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

**Grisel Thorn:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ I know every cage on this floor by the noise it makes. Get me across the casting floor to the stair, and I'll bring my crew out as we go.

**Grisel Thorn:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Right. Slowly. They'll know my voice before they know my face.

**Grisel Thorn:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Suit yourself. I've been left in worse rooms by better liars.

**Grisel Thorn:** _(quest complete; shown as a subtitle when the escort arrives, not voiced)_ That's the stair. I'll wait here and take my crew up myself, every one you bring me. Don't wait for thanks; I'm out of practice.

**Grisel Thorn:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Get them out. Don't you leave them now.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ My Casting Hall, hirelings. Grisel Thorn's crew are in my cages, first down and nearly finished. Another week and they run with my bloody cinders.

**Morg Ashtusk:** _(FloorEnter, jailbreak, not if grisel_forgiven)_ Grisel Thorn was my head jailer, hirelings. She took my keys to save her own bloody neck and kept her crew caged for me. Now you're here I've no more use for a jailer, so she's dead. The keys are on her belt.

**Morg Ashtusk:** _(AllFreed, jailbreak, not if grisel_forgiven)_ Every cage empty, hirelings. Grisel Thorn sold herself to me for a ring of keys, and died holding them. Cheap at the price.

### Hordes

**Narrator:** _(The Spill, Enter)_ The kiln wall cracks and the cinders pour out at you, grey and burning. The ones finished in these cages run with them now.

## Floor 13: The Mould Room

_Mood: cold steel plate, white lamps, shelves of moulds_

_Objective: none in the frame; the shipped seed generates jailbreak_

**Story beat:** cast_ulf shows off his moulds and they are clue_moulds: patterns for the crushing walls, the spike beds, the iron minions, and the Herald's own mould, larger. He finally sees what he has been making and wants every mould broken. fighter_oskar admits he knew all along and kept quiet because the Herald threatened the old man; viewer_foehammer wants the Herald's mould smashed and will do it himself.

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ Cold plate, white lamps, and shelf upon shelf of moulds packed in casting sand. This is where Old Ulf keeps his patterns, and he is proud of every one.

**Narrator:** _(RoomEnter room 4)_ Up close, the moulds on these shelves show what they are for: the plates of a crushing wall, the points of a spike bed, the limbs of the iron brutes.

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

**Old Ulf:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Three patterns off the shelves, that's all. The hall, the hole behind it, and the one they keep by the stair. Don't drop them; a cracked mould pours crooked.

**Old Ulf:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Good. Flat, mind, and keep your thumbs out of the sand.

**Old Ulf:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Suit yourselves. They'll get poured whether you look at them or not. Everything here does.

**Old Ulf:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ A crushing wall. A spike bed. The orc himself, larger. I poured them all. Help me break them.

**Old Ulf:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Left them lying, did you. Well. There's plenty more on the shelves.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, jailbreak)_ More of the town in my cages, hirelings, and my old founder minding his shelves next door. Free them if you like. He won't look up; he never does.

**Morg Ashtusk:** _(Descend)_ So now you've seen my shelves, hirelings: the walls, the spikes, the iron, all mine. My founder poured every one with his bad eyes, and the big mould by the stair is me.

**Morg Ashtusk:** _(AllFreed, jailbreak)_ So my cages stand empty. Now ask my founder's lad why he kept his bloody mouth shut while the old man poured my iron. I said I'd break the old man's neck if the lad talked.

### Hordes

**Narrator:** _(The Spoiled Casting, Enter)_ The wall behind the shelves gives way and the cinders pour through, grey and smouldering: townsfolk the orc finished turning.

## Floor 14: The Quench

_Mood: rusted grates under black water, vestments afloat_

_Objective: shiftingvault in the frame; the shipped seed generates shiftingvault_

**Story beat:** Flooded vault. cast_tobit has followed the party down and found the Verger's vestments thrown off in the water. With told_tobit he asks plainly whether the Verger did this and wants proof. Without it he believes his master is the demon's prisoner, summoned below by a message in the Verger's hand to help with a rite that will save the town. Pressed, he flees to the escort the Herald sent for him: the Betrayal encounter is that escort, the party fights it, not him, and he is alive afterwards and goes on down. Let be, he goes on down to the summons quietly. Pressed or let be, he is taken for the rite (16, 19).

**Narrator:** _(floor intro; not played: the floor opens with its FloorEnter taunt instead)_ Black water stands knee-deep over the quench grates, where the orc's iron is cooled. A churchman's robes float against a grate, and the stairs lie behind gates that move.

**Narrator:** _(RoomEnter room 8)_ Church vestments float against the grate, black and gold, thrown off in the water: the Verger's. Beside them a young deacon stands soaked to the waist.

**Narrator:** _(QuestDone, not if told_tobit)_ The orc's escort lies broken in the water, and the deacon is not among them. He ran on down to his master's summons.

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

**Deacon Tobit Hale:** _(node tob_d)_ My psalter. I'll write it all down: his vestments, in the water, thrown off. If he did this, it will be in his own hand somewhere, and I'll find it. I'm thorough. I always am.

  - **Party (choice):** Keep your head down, deacon. _[Farewell]_

_Quest: The Deacon's Psalter (Recover): Recover Tobit's psalter from the breached font room_

**Deacon Tobit Hale:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Small black book, water-swollen, dropped by the font where the wall is broken. Everything I've found is written in it, in my own bad hand.

**Deacon Tobit Hale:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Thank you. Mind the cinders in there; they come out of the wall like wasps out of a loaf.

**Deacon Tobit Hale:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ No. Quite right. I dropped it; I should fetch it. I only hoped you'd be quicker.

**Deacon Tobit Hale:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ My psalter. I'll write the vestments in, and then I'll find the rest in his own hand.

**Deacon Tobit Hale:** _(quest fail; shown in the message log if the quest fails, not voiced)_ Then it stays in the water, and I start again from memory. I have a good memory.

### Deacon Tobit Hale _(not if told_tobit)_

**Deacon Tobit Hale:** _(node bet_a)_ You're here too! Those are my master's vestments in the water. He's alive, though; he's sent for me. The orc holds him prisoner, doesn't he? Tell me true.

  - **Party (choice):** We don't know, deacon. What did he send? _[to bet_b]_
  - **Party (choice):** We can't say. Don't go. _[to bet_b]_

**Deacon Tobit Hale:** _(node bet_b)_ A note came to me at the church, in his own hand: 'Come below and help me with a rite that will save the town.' His escort waits for me by the grate. I'm going to him.

  - **Party (choice):** You're not going anywhere, deacon. _[Betray]_
  - **Party (choice):** Go to him, then. _[Farewell]_

_Quest: The Deacon's Escort (Betrayal): Put down the escort the orc sent to fetch the deacon_

**Deacon Tobit Hale:** _(quest offer; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ He's sent for me. A rite to save the town, in his own hand. I'm going to him.

**Deacon Tobit Hale:** _(quest accept; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ I'm going to him, and you won't stop me. Those are his escort; don't follow.

**Deacon Tobit Hale:** _(quest decline; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ Thank you. Pray for me, if you pray.

**Deacon Tobit Hale:** _(quest complete; not played: quest lines are not spoken, the dialogue nodes carry the scene)_ His escort lies in the water. The deacon is gone on down to his master.

**Deacon Tobit Hale:** _(quest fail; shown in the message log if the quest fails, not voiced)_ I'm going to him. Don't follow.

### Floor taunts

**Morg Ashtusk:** _(FloorEnter, shiftingvault)_ Your deacon is down here too, hirelings, in my water, fishing out his master's robes. The stairs are behind my gates; pull the bloody levers and guess.

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

**Narrator:** _(BossSeen)_ The Bellows, the engine that pushed the water down this drain for three hundred years, heaves over it choked. The orc set it to pouring his minions: fresh iron drops hissing from its ladles. Stop it and the drain runs.

**Narrator:** _(BossDead)_ The engine's last stroke comes down on an empty ladle. You turn the sluice wheel under it, and the third great drain runs: the heat goes out of the works, the shaking above you eases, and the god drinks.

### Floor taunts

**Morg Ashtusk:** _(Descend)_ Three drains, hirelings. Count your bloody wages. My foundry finished me before you broke it: I am whole again, larger, and waiting for you at the last drain.

### Hordes

**Narrator:** _(The Cinder Spill, Enter)_ The wall splits along a seam of soot and the cinders pour out at you, burning, with the big ones stooping behind. They were Dunmarrow's, once.

### Boss: The Bellows

_No lines of its own: the narrator's BossSeen and BossDead lines speak for it._
