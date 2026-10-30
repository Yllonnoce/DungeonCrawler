namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a hero crows while dancing over a kill (<see cref="Party.VictoryDance"/>): twenty lines per hero by roster name
/// (Sable's written for a woman, like her voice), else five by class, else five generic, topped up into the campaign's
/// bark set like the kill lines. The first five of each hero were voiced first; new text goes at the end. Engine-free.
/// </summary>
public static class DanceBarks
{
    /// <summary>A set with fewer Dance lines than this is topped up from stock.</summary>
    public const int MinLines = 20;

    private static readonly Dictionary<string, string[]> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[]
        {
            "Dancing is my speciality. Dying, it seems, is yours.",
            "That's how Brand does it. Watch the hips, not the sword.",
            "Down you go, and up go my heels.",
            "Shield, sword, and a little shimmy. Full service.",
            "Who's next? I've got five more of these in me.",
            "Brand's rule: kill it, then shake it.",
            "The shield arm's for blocking. The hips are for this.",
            "Don't tell the captain I can move like this.",
            "Squat, buck, rock. Drill of the day.",
            "That's a soldier's dance. Learned it in a tavern.",
            "Two of each, then back to work. Discipline.",
            "Watch the veteran. Learn something.",
            "I don't do it pretty. I do it done.",
            "My knees disagree with the squats. My knees lost.",
            "There's a wave at the end if you behave.",
            "Stand back. Brand is celebrating.",
            "Even a wall can dance when the enemy's down.",
            "Shields down, hips up.",
            "Call that a funeral march. A quick one.",
            "Right. That's enough feeling good. Where's the next one?",
        },
        ["Sable"] = new[]
        {
            "Booty be all that, and the booty's all mine.",
            "Look at me go. Look at you not.",
            "Stabbed, dropped, and danced on. The Sable special.",
            "Shake it, Sable. Nobody left to watch but the rats.",
            "Oh, I'm good. I'm so good I'm dancing about it.",
            "Hips don't lie, and neither do I. Much.",
            "Pockets picked, hips kicked. Try and keep up.",
            "Nobody said a girl can't gloat. Watch me gloat.",
            "Two squats, two shimmies, one less problem.",
            "I'd curtsey, but this is better.",
            "A lady celebrates how she likes. This is how I like.",
            "Dead men tell no tales, but this one's getting a dance.",
            "Light fingers, loose hips.",
            "Call it a victory wiggle. I call it Tuesday.",
            "The rats are clapping. Good rats.",
            "If you'd paid up, I'd have danced for you alive.",
            "Shh. Don't talk over the hip rock.",
            "This body was built for stealing and swaying. Mostly swaying.",
            "Another one in the ledger, another little squat.",
            "I take what I like. Tonight I like dancing.",
        },
        ["Wren"] = new[]
        {
            "One arrow, one corpse, one little dance.",
            "I don't dance for just anyone. You're welcome, dead thing.",
            "The hunt's done. The hips take it from here.",
            "Rangers don't dance, they say. Rangers lie.",
            "Marked, shot, dropped. Now the fun part.",
            "Quiet, both of you. I'm dancing.",
            "Tracked it, dropped it, now I shake it off.",
            "A ranger celebrates once. This is the once.",
            "My arrows fly straight. My hips don't.",
            "Forest rules: kill clean, dance short.",
            "Two squats for the bow, two for the arrow.",
            "I learned this from a stag. Don't ask.",
            "Hunt's over. Hips, you have the floor.",
            "Try the hip rock in a treeline. Builds balance.",
            "I don't smile. I squat.",
            "Something had to die for me to dance. It did.",
            "Nock, loose, wiggle. In that order.",
            "The woods never saw me do this. The woods are lucky.",
            "It stood still. I didn't.",
            "One wave for the dead, then we move.",
        },
        ["Ilex"] = new[]
        {
            "Observe: the victory squat. Taught at no academy.",
            "Spell, then shimmy. The second part is not in the grimoire.",
            "I have reduced you to ash, and I shall now wiggle about it.",
            "A scholar's dance. Dignified. Mostly.",
            "Science wins again, and science has rhythm.",
            "Thaumaturgy, then choreography.",
            "The academy would expel me for this hip rock. Worth it.",
            "Two squats: the recommended dose after any incineration.",
            "Observe the mage in motion. Rare. Treasure it.",
            "Cinders settle; a scholar sways.",
            "My robes were not cut for this. I persist.",
            "Fire, then footwork. Both precise.",
            "Ashes to ashes, hips to hips.",
            "The spell cost mana. The dance is free.",
            "A wiggle in the name of natural philosophy.",
            "Let the record show: the mage can move.",
            "Combustion complete. Commence the shimmy.",
            "I calculated the angle of every squat. Flawless.",
            "Dignity is for the unburnt.",
            "Rhythm is merely applied mathematics. Behold.",
        },
    };

    private static readonly Dictionary<string, string[]> ByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class.warrior"] = new[]
        {
            "Dancing is my speciality.",
            "Down you go, up go my heels.",
            "Sword, shield, shimmy.",
            "Watch the hips, not the blade.",
            "Who's next? I've more of these.",
        },
        ["class.rogue"] = new[]
        {
            "Booty be all that.",
            "Look at me go. Look at you not.",
            "Stabbed, dropped, danced on.",
            "Nobody left to watch but the rats.",
            "So good I'm dancing about it.",
        },
        ["class.ranger"] = new[]
        {
            "One arrow, one corpse, one dance.",
            "The hunt's done. Hips, take over.",
            "Rangers don't dance. Rangers lie.",
            "Marked, shot, dropped. Fun part.",
            "You're welcome, dead thing.",
        },
        ["class.mage"] = new[]
        {
            "Observe: the victory squat.",
            "Spell, then shimmy.",
            "Ash first. Wiggling second.",
            "A scholar's dance. Dignified, mostly.",
            "Science has rhythm.",
        },
    };

    private static readonly string[] Generic =
    {
        "That's one for the dance floor.",
        "Dead, and I've still got moves.",
        "Watch this. No, really, watch.",
        "Victory has hips, apparently.",
        "Another one down. Another little dance.",
    };

    /// <summary>The dance lines for a hero: their own by name, else the class's, else the generic five.</summary>
    public static IReadOnlyList<string> For(string name, string classId)
    {
        if (ByName.TryGetValue(name, out var own)) return own;
        if (ByClass.TryGetValue(classId, out var cls)) return cls;
        return Generic;
    }

    /// <summary>Append stock Dance lines a bark set lacks until it holds at least <see cref="MinLines"/>; returns how many were added.</summary>
    public static int TopUp(BarkSet set, string classId)
    {
        var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == BarkTrigger.Dance).Select(l => BarkRepeat.Key(l.Line.Text)));
        if (have.Count >= MinLines) return 0;
        var added = 0;
        foreach (var text in For(set.Member, classId))
        {
            if (!have.Add(BarkRepeat.Key(text))) continue;
            set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Dance, Line = new Line(text) });
            added++;
        }
        return added;
    }
}
