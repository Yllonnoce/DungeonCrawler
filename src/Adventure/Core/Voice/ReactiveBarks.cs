namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a hero says when something specific just happened to them: the thirteen reactive moments of the Depths update
/// (a trap, a fall, a perfect blow, a parry, a mimic, a secret, an elite, an empty purse, a shrine's gift or price, a
/// gamble, a dangerous wound) and the six old triggers grown to eight lines. Own lines by roster name, five per class,
/// five generic; <see cref="TopUp"/> pours them into a campaign's bark set as <see cref="KillBarks.TopUp"/> does, so a
/// campaign written before these triggers existed still speaks in every hero's voice. Lines are the deliverable and
/// stay in this order (it is the order the owner bakes in). Engine-free.
/// </summary>
public static class ReactiveBarks
{
    private sealed record Bank(string[] Brand, string[] Sable, string[] Wren, string[] Ilex, string[] Warrior, string[] Rogue, string[] Ranger, string[] Mage, string[] Generic);

    /// <summary>The thirteen new non-cheer triggers followed by the six that grow to eight lines.</summary>
    public static readonly BarkTrigger[] Triggers =
    {
        BarkTrigger.TrapSprung, BarkTrigger.Fell, BarkTrigger.PerfectBlow, BarkTrigger.Parried, BarkTrigger.Mimic, BarkTrigger.SecretFound, BarkTrigger.EliteSeen,
        BarkTrigger.Broke, BarkTrigger.ShrineBoon, BarkTrigger.ShrineCurse, BarkTrigger.GambleWin, BarkTrigger.GambleDud, BarkTrigger.LowHp,
        BarkTrigger.Hurt, BarkTrigger.Loot, BarkTrigger.Downed, BarkTrigger.Revived, BarkTrigger.BossSeen, BarkTrigger.LevelUp,
    };

    public static bool Covers(BarkTrigger t) => Banks.ContainsKey(t);

    /// <summary>A hero's lines for a trigger: their own by roster name, else their class's, else the plain set; empty for a trigger this bank does not cover.</summary>
    public static IReadOnlyList<string> For(BarkTrigger t, string name, string classId)
    {
        if (!Banks.TryGetValue(t, out var b)) return Array.Empty<string>();
        switch (name.Trim().ToLowerInvariant())
        {
            case "brand": return b.Brand;
            case "sable": return b.Sable;
            case "wren": return b.Wren;
            case "ilex": return b.Ilex;
        }
        switch (classId.Trim().ToLowerInvariant())
        {
            case "class.warrior": return b.Warrior;
            case "class.rogue": return b.Rogue;
            case "class.ranger": return b.Ranger;
            case "class.mage": return b.Mage;
        }
        return b.Generic;
    }

    /// <summary>
    /// For every covered trigger with fewer than <see cref="BarkRules.ReactiveMinLines"/> distinct lines, append the stock
    /// lines the set lacks (keyed by <see cref="BarkRepeat.Key"/>); returns how many were added.
    /// </summary>
    public static int TopUp(BarkSet set, string classId)
    {
        var added = 0;
        foreach (var t in Triggers)
        {
            var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == t).Select(l => BarkRepeat.Key(l.Line.Text)));
            if (have.Count >= BarkRules.ReactiveMinLines) continue;
            foreach (var text in For(t, set.Member, classId))
            {
                if (!have.Add(BarkRepeat.Key(text))) continue;
                set.Lines.Add(new BarkLine { Trigger = t, Line = new Line(text) });
                added++;
            }
        }
        return added;
    }

    private static readonly Dictionary<BarkTrigger, Bank> Banks = new()
    {
        [BarkTrigger.TrapSprung] = new Bank(
            Brand: new[] { "Trap. Of course it's a trap. Everything's a trap.", "Who puts a trap in a corridor? Everyone, apparently.", "That's my good leg. Was.", "Right. Nobody follow me. Ever.", "Spotted it. Late. Spotted it late.", "I've marched through worse. Not by much.", "Wren, you said this one was safe.", "Somebody write that one down. Not me. I'm busy bleeding." },
            Sable: new[] { "Oh, that's rude. That's just rude.", "I find the traps. That's my whole job. I found one.", "Nobody saw that. Nobody saw that.", "A girl steps on one plate. One.", "Ow. Also: ow.", "I'd have disarmed it if it had introduced itself.", "Fine. It's a lesson. I hate lessons.", "Who sets a trap for a lady? Someone I'd like to meet." },
            Wren: new[] { "That one's Agnes. Agnes got me.", "Missed the plate. First time.", "Noted. Won't be a second.", "Everyone stop. Let me look at my foot.", "Trap. Yes. I know.", "Should've seen the dust line. Didn't.", "Count that one against me.", "They hide them better down here. Noted." },
            Ilex: new[] { "A mechanism! Fascinating. Painful, but fascinating.", "I was observing the ceiling. The floor took offence.", "Note for the journal: do not step on the obvious slab.", "Crude. Effective. I shall be insufferable about it later.", "Someone with a very simple mind built that. It worked.", "A puddle would never do this to me.", "I detected it. Afterwards. Detection is detection.", "Ingenious. Would somebody remove it from my leg." },
            Warrior: new[] { "Trap.", "Walked right into it.", "That'll bruise.", "Nobody step where I stepped.", "Next one, I go round." },
            Rogue: new[] { "I missed one. Say nothing.", "That was meant for someone else.", "Ow.", "A plate. Under me. Humbling.", "Fine. Noted." },
            Ranger: new[] { "Trap. Mine.", "Missed the sign.", "Won't happen again.", "Mark that one.", "Stop. Let me look." },
            Mage: new[] { "A mechanism. Regrettable.", "I was looking elsewhere.", "Crude. Effective.", "Noted, with resentment.", "The floor is hostile." },
            Generic: new[] { "Trap!", "Didn't see that.", "Watch your step.", "Mark it.", "That hurt." }),

        [BarkTrigger.Fell] = new Bank(
            Brand: new[] { "Floor's further down than advertised.", "I meant to do that. Badly.", "Who dug a hole there? Fill it in.", "Down. Not dead. Give me a moment.", "The shield broke the fall. The shield and my spine.", "Nobody saw that. Good.", "Hole. Deep one. Mind it.", "That's thirty years of knees in one landing." },
            Sable: new[] { "Graceful. I'm told I'm graceful.", "A lady doesn't fall. She descends quickly.", "Who moved the floor?", "I'll have you know I landed on my feet. Eventually.", "That's a hole. Nobody said hole.", "Nothing broken. Nothing of mine, anyway.", "I've fallen off better buildings than this.", "Stop laughing, Brand. I can hear you not laughing." },
            Wren: new[] { "Down. Still here.", "Ledge gave. Or I did.", "Deeper than it looked.", "Landed. Mostly.", "Trees have branches. Note the difference.", "Ankle's fine. Pride isn't.", "Mind that edge.", "I'll climb back. Don't help." },
            Ilex: new[] { "Gravity remains undefeated.", "I was falling forwards with manners. Then without.", "A drop. Unmarked. Typical workmanship.", "Observe: the bottom. I have found it.", "My dignity is up there. Fetch it, would you.", "A hole of considerable depth. I have measured it with my face.", "Nothing is broken that cannot be argued back together.", "If Gerald lives down here he has gone very quiet." },
            Warrior: new[] { "Down. Not out.", "Hole. Mind it.", "That's the knees gone.", "Up. Keep moving.", "Who dug that?" },
            Rogue: new[] { "Meant to do that.", "Landed. Mostly.", "Nothing broken.", "Who moved the floor?", "Graceful, that." },
            Ranger: new[] { "Deeper than it looked.", "Down. Still here.", "Mind the edge.", "Landed badly.", "Climbing back." },
            Mage: new[] { "Gravity. Undefeated.", "An unmarked drop.", "I have found the bottom.", "Dignity: lost.", "Noted. Painfully." },
            Generic: new[] { "Ow.", "Mind the drop.", "Still alive.", "Deep one.", "Up you get." }),

        [BarkTrigger.PerfectBlow] = new Bank(
            Brand: new[] { "That's the one. That's how it's taught.", "Clean through. Textbook.", "Felt that in the shoulder. Good.", "Timing. It's all timing.", "Eleven lads learned that swing. Still works.", "There. Perfect. Don't expect it twice.", "Weight behind it. Like I said.", "That's a sergeant's swing." },
            Sable: new[] { "Right in the soft bit.", "Oh, that was pretty. Did everyone see?", "Perfect. Like me.", "Knife says hello. Knife says goodbye.", "I'd bow but there's blood on the floor.", "Clean. I'm very clean.", "Precision, darling. Precision.", "That one I'll remember fondly." },
            Wren: new[] { "Clean.", "Centre. Where I aimed.", "That's the shot.", "Good arrow. Good release.", "Through. Done.", "Breathe, loose, done.", "I'll want that one back. It earned it.", "Dead centre." },
            Ilex: new[] { "Precisely as calculated.", "Observe: perfection. Briefly.", "The timing was exact. The result, inevitable.", "I rarely miss. I never gloat. Much.", "A clean casting. Note it.", "There. Theory, applied.", "Exquisite. I surprise even myself.", "That was the correct amount of fire." },
            Warrior: new[] { "Clean hit.", "Textbook.", "Weight behind it.", "That's the one.", "Felt that." },
            Rogue: new[] { "Pretty.", "Soft bit found.", "Clean.", "Precision.", "Did you see that?" },
            Ranger: new[] { "Centre.", "Clean shot.", "Through.", "Good release.", "Dead centre." },
            Mage: new[] { "As calculated.", "Exact.", "Theory, applied.", "Exquisite.", "Correct." },
            Generic: new[] { "Perfect.", "Clean.", "That's the one.", "Got it.", "Right there." }),

        [BarkTrigger.Parried] = new Bank(
            Brand: new[] { "Not today.", "Turned it. Steel knows steel.", "Swing like that and I'll do it all day.", "Nice try.", "Off the edge. Where it belongs.", "Slow. You're slow.", "Parried. Now mine.", "That's a block with manners." },
            Sable: new[] { "Nope.", "Ooh, close. Not close enough.", "Too slow, sweetheart.", "Turned it. Turn around now, there's a good lad.", "You telegraphed that. I read it twice.", "My blade, your blade, my blade. Sorted.", "Parry. Riposte. Pockets.", "I've dodged drunks faster than you." },
            Wren: new[] { "No.", "Turned it.", "Saw it coming.", "Not that one.", "Off the bow. Mind the bow.", "Slow swing.", "Read it.", "Try again." },
            Ilex: new[] { "Declined.", "A deflection. Elementary.", "You'll have to be quicker than that. Everyone is.", "Staff meets steel. Staff wins.", "I anticipated that. I anticipate most things.", "Not today, and probably not tomorrow.", "Reflexes. Trained. Expensive.", "Turned aside. Noted. Returned." },
            Warrior: new[] { "Not today.", "Turned it.", "Nice try.", "Too slow.", "Mine now." },
            Rogue: new[] { "Nope.", "Too slow.", "Read it.", "Turned it.", "Close." },
            Ranger: new[] { "No.", "Saw it.", "Turned.", "Try again.", "Slow." },
            Mage: new[] { "Declined.", "Elementary.", "Anticipated.", "Turned aside.", "Not today." },
            Generic: new[] { "Parried.", "Not that one.", "Turned it.", "Too slow.", "Again." }),

        [BarkTrigger.Mimic] = new Bank(
            Brand: new[] { "The chest bit me. The chest. Bit me.", "Kill the box! Kill the box!", "Shields up. It's furniture. Angry furniture.", "I've fought men. Dogs. Never luggage.", "That's not a chest. Hit it till it is.", "Teeth. On a lid. Right.", "Nobody open anything. Ever.", "It's got a tongue. Why has it got a tongue." },
            Sable: new[] { "Oh no. No no no. Chests are friends.", "That's a betrayal. That's personal.", "I touched that. I TOUCHED that.", "Gold doesn't bite. Gold doesn't bite!", "Kill it. Then we rob it. In that order.", "I'll never trust a hinge again.", "It's chewing. Why is it chewing.", "Right. New rule. Brand opens everything." },
            Wren: new[] { "Chest's alive. Shoot the chest.", "Knew the lock looked wrong.", "Teeth. Lid. Kill it.", "Told you it was breathing.", "That's not wood.", "Back. Give me a line.", "It's hungry. Don't be the meal.", "Never trusted that one." },
            Ilex: new[] { "A predatory container. Remarkable.", "It mimics a chest and resents being opened. Relatable.", "Burn the furniture. Burn it now.", "Teeth in oak. The anatomy is absurd.", "I shall name it after it dies. Not Gerald.", "Observe: the lid is a jaw. Observe faster.", "Nature is vile and so is carpentry.", "Fire. The universal lockpick." },
            Warrior: new[] { "Kill the box!", "The chest bit me.", "It's alive. Hit it.", "Teeth. On a lid.", "Shields up." },
            Rogue: new[] { "Chests don't bite!", "That's personal.", "Kill it, then rob it.", "I touched that.", "Nope. No." },
            Ranger: new[] { "Chest's alive.", "Shoot the chest.", "It's hungry.", "Knew it.", "Back. Give me a line." },
            Mage: new[] { "Predatory furniture.", "Burn the chest.", "A jaw. Observe.", "Absurd anatomy.", "Fire." },
            Generic: new[] { "The chest is alive!", "Teeth!", "Kill it!", "Back!", "It bit me!" }),

        [BarkTrigger.SecretFound] = new Bank(
            Brand: new[] { "A door in a wall. Somebody's clever.", "Hidden room. Mind the hidden trap.", "Well. Nobody's been in here for a while.", "Found something. Don't touch it yet.", "Secret passage. Soldiers love those.", "Somebody wanted this kept quiet.", "Go on, then. I'll stand in the gap.", "There's always a back way. Always." },
            Sable: new[] { "Oh, hello. What are you hiding?", "A secret. My favourite kind of door.", "Somebody didn't want me in here. Rude of them.", "Dibs. Dibs on everything.", "The wall was lying. I like a wall that lies.", "Quiet, everyone. It might have gold in it.", "This is why you bring me.", "Hidden. Not from me." },
            Wren: new[] { "Seam in the wall. Knew it.", "Hidden room. Watch the floor.", "Somebody built this to be missed.", "Found it. Don't crowd.", "Air moved. That's how.", "A room nobody meant us to see.", "Quiet in here. Good.", "Mark it on the map." },
            Ilex: new[] { "A concealed chamber. Deduction rewarded.", "Someone hid this. Someone was wrong to.", "Observe: the wall was a lie of masonry.", "Architecture with a secret. Delicious.", "Let the record show I suspected the wall.", "Dust undisturbed. We are the first in an age.", "A hidden room. Possibly a hidden Gerald.", "Knowledge, concealed. I am offended and delighted." },
            Warrior: new[] { "Hidden room.", "A door in a wall.", "Mind the trap.", "I'll hold the gap.", "Somebody's clever." },
            Rogue: new[] { "Oh, hello.", "Dibs.", "A lying wall. Lovely.", "Hidden. Not from me.", "Gold, maybe." },
            Ranger: new[] { "Seam. Knew it.", "Watch the floor.", "Air moved.", "Mark it.", "Don't crowd." },
            Mage: new[] { "A concealed chamber.", "Deduction rewarded.", "The wall lied.", "Dust undisturbed.", "Delicious." },
            Generic: new[] { "A secret room.", "Hidden.", "Look here.", "Well now.", "Somebody hid this." }),

        [BarkTrigger.EliteSeen] = new Bank(
            Brand: new[] { "That one's got a name. Names cost extra.", "Big one. Named one. Form up.", "It's wearing something. Doesn't matter. Hit it.", "Officer type. Take it down first.", "That's not a grunt. Watch its hands.", "Named. Means it's killed before.", "On me. Shields. This one's special.", "Right. Somebody important wants to die." },
            Sable: new[] { "Ooh, a name. Fancy. Still bleeds.", "It's got airs. I hate airs.", "Named ones carry better pockets.", "Look at it. Thinks it's somebody.", "A champion. Lovely. Stab the champion.", "It glows. Things that glow die interesting.", "Oh, it's special. Let's make it less so.", "That one has a title. I'll have it off him." },
            Wren: new[] { "Named one. Mark it.", "Watch its tells. It's different.", "That's the dangerous one.", "Big. Marked. Mine.", "It's not like the others. Move like it isn't.", "Named. Means careful.", "Eyes on the glowing one.", "That one leads. Take the leader." },
            Ilex: new[] { "A named specimen. How vain.", "It has affixes. Read them. Then kill them.", "Ah. Something with a title. Titles burn.", "Note the aura. Note it from range.", "Distinct. Dangerous. Documented.", "A champion of the dark. I am unimpressed and alert.", "Special, is it? Fire is also special.", "Observe its marks. They mean something. Probably pain." },
            Warrior: new[] { "Named one. Form up.", "That's no grunt.", "On me.", "Take it first.", "Watch its hands." },
            Rogue: new[] { "Fancy. Still bleeds.", "Better pockets.", "Thinks it's somebody.", "Stab the champion.", "It glows. Noted." },
            Ranger: new[] { "Mark it.", "The dangerous one.", "Watch its tells.", "Named. Careful.", "Take the leader." },
            Mage: new[] { "A named specimen.", "Read its marks.", "Titles burn.", "From range.", "Documented." },
            Generic: new[] { "That one's named.", "Careful. It's special.", "Big one.", "Watch it.", "Named." }),

        [BarkTrigger.Broke] = new Bank(
            Brand: new[] { "Not enough. Never enough.", "Soldier's pay. Soldier's purse.", "Put it back. Gently.", "We'll come back. Richer. Probably.", "I can afford to look.", "That's a month's wages for a hat.", "Walk away, Brand. Walk away.", "Coin's short. Patience shorter." },
            Sable: new[] { "I could just... take it.", "Short. By a lot. By a scandalous lot.", "Put the knife away, Sable. It's a shop.", "Nobody tell him I counted twice.", "This is why I steal. Honest work doesn't pay.", "Too dear. Everything's too dear when you're dead broke.", "I'll have that. Later. Quietly.", "A lady should never hear the word 'afford'." },
            Wren: new[] { "Can't.", "Too much. Walk on.", "Not for that price.", "No coin. No matter.", "I've lived on less.", "Put it down.", "Trees were free.", "Next floor, maybe." },
            Ilex: new[] { "Insufficient funds. Insulting price.", "I am a scholar. Scholars are poor on principle.", "Daylight robbery, in a place with no daylight.", "I could afford it if I sold Brand.", "Economics. The dismal science.", "Knowledge is priceless. That is priced.", "Put it back before I calculate the markup.", "I shall simply want it from a distance." },
            Warrior: new[] { "Not enough.", "Put it back.", "Too dear.", "Next time.", "Soldier's purse." },
            Rogue: new[] { "I could just take it.", "Too dear.", "Short. Scandalously.", "Later. Quietly.", "Honest work doesn't pay." },
            Ranger: new[] { "Can't.", "Too much.", "Walk on.", "No coin.", "Next floor." },
            Mage: new[] { "Insufficient funds.", "Daylight robbery.", "Dismal science.", "I'll want it from afar.", "Insulting." },
            Generic: new[] { "Can't afford it.", "Too much.", "Not enough gold.", "Put it back.", "Maybe later." }),

        [BarkTrigger.ShrineBoon] = new Bank(
            Brand: new[] { "Feels... warmer. Don't like it. Keeping it.", "That'll do. Whatever it is.", "Something's in my arm now. Good something.", "A blessing. Thirty years late.", "Right. Stronger. Don't make a fuss.", "If it helps, it helps.", "Took it. Don't tell the chaplain.", "I'll pay for that later. Everything costs." },
            Sable: new[] { "Ooh. Tingly. More, please.", "Free things. My favourite things.", "Stolen from a god. A good day.", "I feel marvellous. I was marvellous before, but still.", "That's going straight to my hips.", "A gift. Nobody gives me gifts.", "Shiny inside now. Lovely.", "Don't ask what it cost. I didn't." },
            Wren: new[] { "Took it.", "Feels right. Like good wind.", "Something settled. Good.", "I'll trust it. Carefully.", "Better. Noted.", "A gift from the stone. Odd.", "Keener. Keep moving.", "That'll help." },
            Ilex: new[] { "Power, freely offered. Suspicious. Accepted.", "I feel improved. Statistically.", "The altar approves of me. Naturally.", "A boon. I shall document the side effects.", "Knowledge enters the blood. Pleasant.", "I am, if anything, more remarkable.", "Accepted. I will want a receipt.", "The stone is generous. Stone rarely is." },
            Warrior: new[] { "Stronger. Good.", "Took it.", "That'll do.", "Feels warmer.", "Don't make a fuss." },
            Rogue: new[] { "Tingly.", "Free things.", "Marvellous.", "Shiny inside.", "Don't ask." },
            Ranger: new[] { "Took it.", "Feels right.", "Keener.", "Better.", "That'll help." },
            Mage: new[] { "Improved. Statistically.", "Accepted.", "Suspicious. Accepted.", "Documenting.", "Remarkable." },
            Generic: new[] { "Stronger.", "That helps.", "A blessing.", "Took it.", "Good." }),

        [BarkTrigger.ShrineCurse] = new Bank(
            Brand: new[] { "That's a cost. Felt it go in.", "Something's wrong with me now. Add it to the list.", "A curse. I've had worse from a quartermaster.", "Heavier. Everything's heavier.", "Paid in blood. Usual rate.", "Don't look at me like that. It was the deal.", "I'll live. Shorter, but I'll live.", "Marked. Fine. I was never pretty." },
            Sable: new[] { "Oh, I don't like that. I don't like that at all.", "It took something. Nobody takes from me.", "Cursed. On a Tuesday.", "That's going to show, isn't it.", "Fine. A curse. I'll steal it back later.", "I feel grubby. Grubbier.", "Worth it. Probably. Ask me later.", "A lady accepts a curse with grace. And swearing." },
            Wren: new[] { "Paid.", "Something's wrong. Carry it.", "Marked. Felt it.", "Heavier. Walk anyway.", "A price. Fine.", "Cursed. Noted.", "It'll pass. Or it won't.", "I've carried worse." },
            Ilex: new[] { "A malediction. I shall study it from inside.", "Ah. The price. One always forgets the price.", "Cursed. How very medieval.", "Something has attached itself to my person. Rude.", "I accept this with dignity and resentment.", "The altar has opinions about me. Noted.", "A curse. I'll write a paper. Posthumously, perhaps.", "Unpleasant. Instructive. Unpleasant." },
            Warrior: new[] { "Felt that go in.", "A cost.", "Heavier.", "I'll live.", "Marked." },
            Rogue: new[] { "Don't like that.", "It took something.", "Grubby.", "Worth it. Probably.", "Cursed. Lovely." },
            Ranger: new[] { "Paid.", "Marked.", "Carry it.", "Noted.", "It'll pass." },
            Mage: new[] { "A malediction.", "The price.", "How medieval.", "Instructive.", "Rude." },
            Generic: new[] { "Cursed.", "That cost me.", "Something's wrong.", "A price.", "Marked." }),

        [BarkTrigger.GambleWin] = new Bank(
            Brand: new[] { "Luck. Don't trust it. Take it.", "That's better than it should be.", "Well. The cloth was hiding something.", "First time a merchant's done right by me.", "Worth it. Say nothing.", "Good steel from a bad bet.", "Don't get used to it.", "Somebody's smiling on us. Suspicious." },
            Sable: new[] { "Yes! I knew it. I always know.", "Luck is a skill, darling.", "Oh, it's gorgeous. Mine.", "The house loses. Lovely house.", "Three guesses and one of them was right.", "I was born lucky. And pretty.", "Pay me again, little bundle.", "I'll never doubt a bit of cloth again." },
            Wren: new[] { "Good one.", "Lucky. Take it.", "Better than the last.", "The cloth hid something worth it.", "Fortune. Don't chase it.", "That'll do.", "Keep it. Move.", "Won. Odd feeling." },
            Ilex: new[] { "Probability smiles. Briefly, and on me.", "A fine result. I shall pretend I predicted it.", "The expected value was poor. The value was not.", "Chance, correctly guessed.", "Luck is statistics in a good mood.", "Excellent. Do not tell Sable I gambled.", "A worthy bundle. Shocking.", "Fortune, for once, reads my notes." },
            Warrior: new[] { "Lucky.", "Worth it.", "Good steel.", "Take it.", "Don't trust it." },
            Rogue: new[] { "Yes!", "Mine.", "Luck's a skill.", "The house loses.", "Gorgeous." },
            Ranger: new[] { "Good one.", "Lucky.", "That'll do.", "Keep it.", "Won." },
            Mage: new[] { "Probability smiles.", "Predicted.", "A fine result.", "Chance, guessed.", "Shocking." },
            Generic: new[] { "Worth it!", "Lucky.", "Good one.", "That paid off.", "Nice." }),

        [BarkTrigger.GambleDud] = new Bank(
            Brand: new[] { "Cloth. Wrapped round nothing. Figures.", "That's a week's pay for a disappointment.", "Never gamble. I say that every time.", "Well. It's a thing. Technically.", "Rubbish. Expensive rubbish.", "He knew. He always knows.", "Right. Lesson paid for. Again.", "I could have had a chair for that." },
            Sable: new[] { "Oh, that's a swindle. I admire it. I hate it.", "No. No, put it back. Put the gold back.", "I've been robbed. Me. By cloth.", "That's a rag. I paid for a rag.", "Professional respect. Personal fury.", "I'm stealing that money back. Watch me not.", "Junk. Pretty junk. Still junk.", "A lady never bets. A lady just did." },
            Wren: new[] { "Junk.", "Knew it.", "Cloth and nothing.", "Last time.", "Wasted.", "Put it down. Walk.", "Money gone. Fine.", "Should've bought arrows." },
            Ilex: new[] { "The expected value asserts itself.", "A lesson in probability. Overpriced.", "Rubbish, elegantly wrapped. I respect the wrapping.", "I knew the odds. I ignored them. Human.", "Disappointing. Like most empirical results.", "It is, technically, an object.", "Sable, stop laughing. Sable.", "A dud. The universe keeps its books." },
            Warrior: new[] { "Rubbish.", "Never gamble.", "Expensive nothing.", "He knew.", "Lesson paid." },
            Rogue: new[] { "A swindle.", "Robbed by cloth.", "Put the gold back.", "Junk.", "Fury." },
            Ranger: new[] { "Junk.", "Knew it.", "Wasted.", "Walk.", "Arrows, next time." },
            Mage: new[] { "Expected value.", "Overpriced lesson.", "Technically an object.", "Disappointing.", "A dud." },
            Generic: new[] { "Junk.", "Wasted.", "Never again.", "Rubbish.", "Figures." }),

        [BarkTrigger.LowHp] = new Bank(
            Brand: new[] { "I'm hurt. Properly. Someone fill the gap.", "Blood's mine this time. Cover me.", "Not done. Nearly. Not done.", "Need a moment. Won't get one.", "That's deep. Keep fighting.", "Shield's heavy. Arm's heavier.", "Don't wait for me. Finish it.", "Thirty years and this is the one that gets me." },
            Sable: new[] { "Right, that's a lot of blood for a small girl.", "I'm fine. I'm lying. Help.", "Potion. Anyone. Please. Pretty please.", "This dress is ruined. So am I, a bit.", "Not dying. Refusing. Firmly.", "Somebody stand in front of me. Brand. You.", "I'd run but my legs have opinions.", "Hurt. Badly. Still gorgeous." },
            Wren: new[] { "Hurt. Bad.", "Low. Cover me.", "Can't take another.", "Bleeding. Keep shooting.", "Need a moment.", "Still up. Barely.", "Step back. Need air.", "One more and I'm down." },
            Ilex: new[] { "I am rather badly damaged.", "Blood. Mine. Unacceptable.", "The body fails before the mind. Hurry.", "Potion. Now. I'll explain the chemistry later.", "This is a critical quantity of injury.", "I am not well. Someone be useful.", "Fading. Documenting. Fading.", "Help, in the plainest possible terms." },
            Warrior: new[] { "Hurt bad.", "Cover me.", "Not done yet.", "Fill the gap.", "Nearly down." },
            Rogue: new[] { "Help. Please.", "Not dying. Refusing.", "A lot of blood.", "Stand in front of me.", "Hurt." },
            Ranger: new[] { "Low.", "Cover me.", "Bleeding.", "One more and I'm down.", "Need air." },
            Mage: new[] { "Badly damaged.", "Potion. Now.", "Fading.", "Help.", "Critical." },
            Generic: new[] { "I'm hurt!", "Help!", "Low!", "Can't take much more.", "Cover me!" }),

        // the six that grow to eight (the shipped campaign keeps its three written ones in front of these)
        [BarkTrigger.Hurt] = new Bank(
            Brand: new[] { "Agh. Felt that.", "That one landed. Won't again.", "Right. Now I'm cross.", "Armour's dented. So am I.", "Hit me again and mean it.", "Blood. Mine. Fine.", "Nothing a drink won't fix. Later.", "Through the guard. How." },
            Sable: new[] { "Ow! That's my good side!", "Rude. Rude and painful.", "You've marked me. I mark back.", "That'll bruise. I bruise prettily.", "Hands off the merchandise.", "Hit a lady, would you.", "Ugh. Blood on the leathers.", "That one stung. Properly." },
            Wren: new[] { "Hit.", "Hurt. Still shooting.", "Felt that.", "Deep. Not done.", "Close. Too close.", "Marked me.", "Bleeding. Fine.", "Should've moved." },
            Ilex: new[] { "Ow. That is quite enough.", "A hit. On me. Outrageous.", "Pain. Noted. Returned shortly.", "You struck a scholar. Barbarian.", "That will bruise interestingly.", "Blood is not an argument. Fire is.", "I am wounded and irritated.", "Unpleasant. Instructive." },
            Warrior: new[] { "Agh!", "Felt that.", "That landed.", "Now I'm cross.", "Again, then." },
            Rogue: new[] { "Ow!", "Rude.", "That'll bruise.", "Hands off.", "Stung." },
            Ranger: new[] { "Hit.", "Felt that.", "Still shooting.", "Too close.", "Bleeding." },
            Mage: new[] { "Ow.", "Outrageous.", "Noted.", "Barbarian.", "Irritated." },
            Generic: new[] { "Agh!", "That hurt.", "I'm hit.", "Felt that.", "Ow." }),

        [BarkTrigger.Loot] = new Bank(
            Brand: new[] { "Useful. Maybe.", "Take it. Weigh it later.", "Somebody dropped this. Their loss.", "Better than what I've got. Barely.", "In the bag. Move on.", "Spoils. Don't get greedy.", "That'll sell, if nothing else.", "Mine. Unless Sable gets to it." },
            Sable: new[] { "Mine. Obviously.", "Ooh. Shiny. Pocket.", "Finders keepers. I'm the finder.", "Lovely. That's going somewhere safe. Me.", "A gift from the dead. Thoughtful.", "I'll appraise it. Later. Privately.", "Something sparkly. My day improves.", "That'll fetch a price. Or a smile." },
            Wren: new[] { "Useful.", "Taking it.", "Might serve.", "Bag it.", "Good find.", "Someone left this. Shame.", "Worth the weight.", "Keep." },
            Ilex: new[] { "Interesting. Possibly valuable. Mine.", "A find. I shall catalogue it.", "Someone dropped knowledge. Or a hat.", "Noted and pocketed.", "Potentially useful. Probably cursed. Keeping it.", "An artefact. Or junk. Research required.", "I claim this in the name of scholarship.", "Acquired. Observe my restraint." },
            Warrior: new[] { "Useful.", "Take it.", "Bag it.", "Spoils.", "Mine." },
            Rogue: new[] { "Mine.", "Shiny.", "Pocket.", "Finders keepers.", "Lovely." },
            Ranger: new[] { "Useful.", "Taking it.", "Keep.", "Good find.", "Worth it." },
            Mage: new[] { "Interesting.", "Catalogued.", "Acquired.", "Research required.", "Mine, for science." },
            Generic: new[] { "Got it.", "Useful.", "Mine.", "Taking this.", "Good find." }),

        [BarkTrigger.Downed] = new Bank(
            Brand: new[] { "Down. Not out. Get me up.", "Can't... hold the line...", "Somebody. Take the shield.", "That's it. That's me.", "Floor's cold. Hurry.", "Not here. Not like this.", "Get up, Brand. ...Can't.", "Finish it. I'll watch." },
            Sable: new[] { "Oh. That's a lot of floor.", "Not dying. Just lying down. Fiercely.", "Somebody pick me up. Gently.", "I'm fine. I'm very not fine.", "Dark. Is it dark? It's dark.", "Tell my knives I loved them.", "Down. Humiliating. Help.", "I'll haunt whoever did that." },
            Wren: new[] { "Down.", "Can't... get up.", "Help. Quiet about it.", "Floor. Cold.", "Not yet. Not yet.", "Someone. Please.", "Lost my footing. Lost the rest.", "Dark. Hurry." },
            Ilex: new[] { "I appear to be horizontal.", "Down. Undignified. Help.", "The floor and I have met.", "Consciousness... negotiable.", "Somebody competent, please.", "This is not how the paper ends.", "I can see the ceiling. It is poor.", "Assistance. Promptly. I'll be gracious." },
            Warrior: new[] { "Down.", "Get me up.", "Can't hold.", "Not like this.", "Finish it." },
            Rogue: new[] { "Down.", "Pick me up.", "Not dying. Lying.", "Help.", "Dark." },
            Ranger: new[] { "Down.", "Can't get up.", "Help.", "Not yet.", "Hurry." },
            Mage: new[] { "Horizontal.", "Undignified.", "Assistance.", "Negotiable.", "Help." },
            Generic: new[] { "I'm down!", "Help me!", "Get me up!", "Can't...", "Down." }),

        [BarkTrigger.Revived] = new Bank(
            Brand: new[] { "Up. Right. Where were we.", "I owe you. Add it to the pile.", "Back. Angrier.", "That was a rest. Short one.", "Shield's still here. Good.", "Never again. Until next time.", "Breathing. Count that as a win.", "Thanks. Don't mention it. Ever." },
            Sable: new[] { "Alive! Still gorgeous. Checked.", "Ugh. Who dragged me? Thank you. Ugh.", "Back from the dead. Owe you a drink. Stolen.", "Right. Revenge. Let me find my knives.", "I'm up. Nobody saw me down.", "Thank you. Don't make it a habit.", "Death didn't want me. Can't blame it.", "Up. Pockets intact. Good." },
            Wren: new[] { "Up.", "Owe you.", "Back. Quiet.", "Breathing. Good.", "Thanks.", "Still here. Still shooting.", "Head's ringing. Fine.", "Again, then." },
            Ilex: new[] { "Vertical again. Praise competence.", "Thank you. I shall return the favour academically.", "Alive. The paper continues.", "My head. My everything. Thank you.", "Revived. Hypothesis: I am hard to kill.", "I saw nothing. Disappointing.", "Up. Irritated. Functional.", "Back. The dark was underwhelming." },
            Warrior: new[] { "Up.", "Owe you.", "Back.", "Angrier now.", "Thanks." },
            Rogue: new[] { "Alive!", "Owe you a drink.", "Up. Revenge.", "Thanks.", "Pockets intact." },
            Ranger: new[] { "Up.", "Thanks.", "Breathing.", "Again.", "Still here." },
            Mage: new[] { "Vertical.", "Thank you.", "Functional.", "Alive.", "Underwhelming." },
            Generic: new[] { "I'm up.", "Thanks.", "Still here.", "Back.", "Owe you." }),

        [BarkTrigger.BossSeen] = new Bank(
            Brand: new[] { "There. That's the big one. Shields.", "Right. Everyone behind me.", "That's a lot of monster. Hit the knees.", "Form up. This one's earned a name.", "Steady. Steady. Now.", "Been waiting for you. Didn't want to.", "It's big. It'll fall big.", "Hold. Wait for the swing. Then." },
            Sable: new[] { "Oh. Oh, that's large.", "That's the boss. Where's its purse?", "Big and ugly. I'll go round the back.", "Right. Nobody die. Especially me.", "Lovely. A fight with a name on it.", "I've robbed bigger. I haven't, actually.", "Watch its hands. All of them.", "Hello, gorgeous. Goodbye, gorgeous." },
            Wren: new[] { "There. The big one.", "Mark it. Everyone.", "Keep moving. Don't stand.", "It's slow. Make it slower.", "Big. Loud. Mine.", "Range. Give me range.", "That's the one that guards the stairs.", "Breathe. Loose. Again." },
            Ilex: new[] { "Ah. The resident. Enormous.", "Observe its pattern. Then end it.", "A guardian. Impressive. Flammable.", "Large. Slow. Over-confident. Noted.", "It has a signature. Everything burns the same.", "Behold: a problem with a name.", "Stay out of its reach. I shall stay out of all reach.", "A boss. The paper gets a chapter." },
            Warrior: new[] { "The big one. Shields.", "Form up.", "Steady.", "Behind me.", "Hit the knees." },
            Rogue: new[] { "That's large.", "Round the back.", "Nobody die.", "Watch its hands.", "Hello, gorgeous." },
            Ranger: new[] { "The big one.", "Mark it.", "Keep moving.", "Range.", "Breathe." },
            Mage: new[] { "Enormous.", "Observe. Then end it.", "Flammable.", "A problem with a name.", "Out of reach." },
            Generic: new[] { "There it is.", "The boss.", "Big one.", "Steady.", "Everyone, on me." }),

        [BarkTrigger.LevelUp] = new Bank(
            Brand: new[] { "Stronger. Took long enough.", "Learned something. Hurt doing it.", "That's experience. The painful kind.", "Better. Not good. Better.", "Still breathing, still learning.", "Another notch. Don't celebrate.", "Steel feels lighter. Won't last.", "Thirty years and I'm still improving. Slowly." },
            Sable: new[] { "Ooh. I feel sharper. Sharper than before.", "Better. Prettier too. Side effect.", "Levelled. Is that the word? Levelled.", "Growth. In a dungeon. Who'd have thought.", "I'm getting good at this. I was good before.", "More tricks. Fewer manners.", "A girl improves. Watch out.", "Sharper knives, sharper me." },
            Wren: new[] { "Better.", "Learned.", "Keener.", "Something clicked.", "Good. Move on.", "Stronger. Don't fuss.", "Eye's sharper.", "Growing. Odd place for it." },
            Ilex: new[] { "I have improved. Measurably.", "Knowledge, earned by bleeding. Inefficient.", "Stronger. I shall adjust the paper.", "A new tier of me. Gasp.", "Progress. Documented.", "Power accrues to the deserving. Eventually.", "I feel smarter. Imagine.", "Experience. Technically a kind of learning." },
            Warrior: new[] { "Stronger.", "Learned something.", "Better.", "Another notch.", "Took long enough." },
            Rogue: new[] { "Sharper.", "Better.", "More tricks.", "Growth.", "Watch out." },
            Ranger: new[] { "Better.", "Keener.", "Learned.", "Clicked.", "Move on." },
            Mage: new[] { "Improved.", "Measurably.", "Documented.", "Smarter.", "Progress." },
            Generic: new[] { "Stronger.", "Levelled up.", "Better.", "Learned something.", "Good." }),
    };
}
