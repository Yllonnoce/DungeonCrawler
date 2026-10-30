namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a killer says over the body: a stock of victory lines and taunts per party member (by roster name, else by
/// class) and per recruitable fighter, topped up into a campaign's bark set so a planner's three lines are never all
/// there is. Kill barks run on quicker rules than the other barks: a short cooldown, a roll so not every kill is
/// remarked on, and the oldest line comes back rather than the character falling silent. Engine-free.
/// </summary>
public static class KillBarks
{
    /// <summary>Seconds a member stays quiet after a kill line (other barks wait 25).</summary>
    public const float CooldownSeconds = 6f;
    /// <summary>Seconds between any two barks when the second is a kill line (other barks wait 5).</summary>
    public const float GlobalGapSeconds = 2f;
    /// <summary>The share of kills that earn a line.</summary>
    public const float Chance = 0.7f;
    /// <summary>A set with fewer Kill lines than this is topped up from stock.</summary>
    public const int MinLines = 20;

    private static readonly Dictionary<string, string[]> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[]
        {
            "Down. Next one, don't dawdle.",
            "That's how it's done. Watch the flank.",
            "Stay down. I mean it.",
            "One. Keep count, somebody.",
            "Clean. Mostly.",
            "Not today. Not by you.",
            "Shield up, blade through. Simple.",
            "He swung first. He'll not swing again.",
            "Rest easy, whatever you were.",
            "Move up. There'll be more.",
        },
        ["Sable"] = new[]
        {
            "Quiet as a kept promise.",
            "He had nothing on him. Typical of this parish.",
            "Don't look. I didn't do it neatly.",
            "Oh, you'll want to check his pockets. I already did.",
            "From behind. Where the gold is.",
            "Hush now. There. All better.",
            "Nobody saw that. Which is the point.",
            "I liked him better quiet.",
            "Shh. The dead don't haggle.",
            "That one owed me. Now we're square.",
            // the dance-themed set (2026-10-30): a woman's voice, a thief's manners
            "Dead. Cue the music.",
            "Stabbed in time with the beat.",
            "That's the knife work done. Now the footwork.",
            "One less partner. Shame, he had rhythm.",
            "Drop. Good. Now watch me not.",
            "Quiet now. I like to dance in silence.",
            "Pockets first, then the hips.",
            "Fell like a drunk on a dance floor. Stays like one too.",
            "Knife in, knife out, step to the left.",
            "I've killed quieter, but never prettier.",
            "Boot on the chest, shimmy to follow.",
            "That one didn't see the blade or the squat coming.",
            "Oh, you dropped something. Everything, actually.",
            "A girl's got to warm up before she dances.",
            "Nothing personal. Everything rhythmic.",
        },
        ["Wren"] = new[]
        {
            "Through the eye. It won't walk back.",
            "One down. I'll want that arrow back.",
            "Nothing down here dies surprised.",
            "Marked it. Loosed. Done.",
            "Two steps closer and it would have been you.",
            "Wind's dead down here. Makes it easy.",
            "It stood still. Its mistake.",
            "Good arrow. Shame about the fletching.",
            "Quiet. Listen for the next one.",
            "Drop. Stay dropped.",
        },
        ["Ilex"] = new[]
        {
            "Burned clean. Its throat may thank me.",
            "Reduced to component parts. Tidy enough.",
            "That one had a soul cast in a mould. Pity.",
            "Fire answers most theological questions.",
            "Observe: it stops when the heat exceeds the will.",
            "I did warn it. Briefly.",
            "Entropy. I merely hurried it along.",
            "Note the smell. Sulphur, mostly. Mostly.",
            "Another argument settled in my favour.",
            "Do try to leave the next one for the sword. My hands are cold.",
        },
    };

    private static readonly Dictionary<string, string[]> ByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class.warrior"] = new[] { "Down.", "Stay down.", "One less.", "Next.", "That's for the last one.", "Hold the line. Mine's done.", "Clean kill.", "Who's next?" },
        ["class.rogue"] = new[] { "Quietly.", "Didn't see me. Nobody does.", "One less.", "Pockets first, questions never.", "In and out.", "That'll do.", "Easy.", "Sleep." },
        ["class.ranger"] = new[] { "Dropped.", "Through and through.", "One less.", "That arrow's mine.", "Next target.", "Clean shot.", "It stopped. They do.", "Mark the next." },
        ["class.mage"] = new[] { "Ash.", "As calculated.", "One less.", "Observe.", "It burns well.", "Settled.", "Next question.", "Tidy." },
    };

    private static readonly string[] Generic = { "Down.", "Stay down.", "One less.", "Next.", "That's done.", "Keep moving.", "Clean.", "Another." };

    private static readonly Dictionary<string, string[]> Fighters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["viewer_foehammer"] = new[]
        {
            "Ha! Felt that one in my teeth.",
            "Stand behind me, I said. See why?",
            "Hammer says no.",
            "Another for the stone.",
            "They don't make them like they used to. Good.",
            "Next! My arm's warm now.",
            "Down you go. Mind the step.",
        },
        ["fighter_tamsin"] = new[]
        {
            "Ha! That one had teeth. Gold ones? No. Shame.",
            "Partners, remember. Even shares of whatever that was.",
            "Mind the wet, it'll slip you. Not him. He's done slipping.",
            "I've broken harder rock before breakfast.",
            "Check it for coin. Go on. I'll wait.",
            "Right. Who's next for the lamp?",
            "Not bad for a miner, eh?",
        },
        ["fighter_oskar"] = new[]
        {
            "Poor iron. It bent.",
            "Don't thank me.",
            "That's what the old man's tools are for.",
            "Patience. They always come to the hammer.",
            "Cast badly. Broke easily.",
            "Next. I've all day.",
            "I've quenched hotter.",
        },
        ["fighter_ione"] = new[]
        {
            "Forgive me. Forgive me.",
            "The light held. It held.",
            "I did not want that. I did it.",
            "He would have understood. The Verger. He would.",
            "Stay close. Please. Stay close to the lantern.",
            "Is it... it is. All right. All right.",
            "Saints keep it, whatever it was.",
        },
    };

    /// <summary>A party member's kill lines: their own by roster name, else their class's, else the plain set.</summary>
    public static IReadOnlyList<string> For(string name, string classId)
    {
        if (ByName.TryGetValue(name, out var own)) return own;
        if (ByClass.TryGetValue(classId, out var byClass)) return byClass;
        return Generic;
    }

    /// <summary>A recruitable fighter's kill lines by NPC id; empty for anyone else.</summary>
    public static IReadOnlyList<string> ForFighter(string npcId) => Fighters.TryGetValue(npcId, out var lines) ? lines : Array.Empty<string>();

    /// <summary>Whether a bark set's Member is one of the recruitable fighters (their sets ride in the campaign beside the party's).</summary>
    public static bool IsFighter(string member) => Fighters.ContainsKey(member);

    /// <summary>
    /// Give every fighter a bark set in the campaign, keyed by NPC id, so their lines carry voice ids like the party's and
    /// play from the shipped bank whatever the player's voice settings. Returns how many sets were added.
    /// </summary>
    public static int TopUpFighters(CampaignPlan campaign)
    {
        var added = 0;
        foreach (var (id, lines) in Fighters)
        {
            if (campaign.Barks.Any(b => b.Member == id)) continue;
            var set = new BarkSet { Member = id };
            foreach (var text in lines) set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line(text) });
            campaign.Barks.Add(set);
            added++;
        }
        return added;
    }

    /// <summary>Append stock Kill lines a bark set lacks until it holds at least <see cref="MinLines"/>; returns how many were added.</summary>
    public static int TopUp(BarkSet set, string classId)
    {
        var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == BarkTrigger.Kill).Select(l => BarkRepeat.Key(l.Line.Text)));
        if (have.Count >= MinLines) return 0;
        var added = 0;
        foreach (var text in For(set.Member, classId))
        {
            if (!have.Add(BarkRepeat.Key(text))) continue;
            set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line(text) });
            added++;
        }
        return added;
    }
}
