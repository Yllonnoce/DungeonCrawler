namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a hero crows while dancing over a kill (<see cref="Party.VictoryDance"/>): five lines per hero by roster name,
/// else by class, else generic, topped up into the campaign's bark set like the kill lines. Engine-free.
/// </summary>
public static class DanceBarks
{
    /// <summary>A set with fewer Dance lines than this is topped up from stock.</summary>
    public const int MinLines = 5;

    private static readonly Dictionary<string, string[]> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[]
        {
            "Dancing is my speciality. Dying, it seems, is yours.",
            "That's how Brand does it. Watch the hips, not the sword.",
            "Down you go, and up go my heels.",
            "Shield, sword, and a little shimmy. Full service.",
            "Who's next? I've got five more of these in me.",
        },
        ["Sable"] = new[]
        {
            "Booty be all that, and the booty's all mine.",
            "Look at me go. Look at you not.",
            "Stabbed, dropped, and danced on. The Sable special.",
            "Shake it, Sable. Nobody left to watch but the rats.",
            "Oh, I'm good. I'm so good I'm dancing about it.",
        },
        ["Wren"] = new[]
        {
            "One arrow, one corpse, one little dance.",
            "I don't dance for just anyone. You're welcome, dead thing.",
            "The hunt's done. The hips take it from here.",
            "Rangers don't dance, they say. Rangers lie.",
            "Marked, shot, dropped. Now the fun part.",
        },
        ["Ilex"] = new[]
        {
            "Observe: the victory squat. Taught at no academy.",
            "Spell, then shimmy. The second part is not in the grimoire.",
            "I have reduced you to ash, and I shall now wiggle about it.",
            "A scholar's dance. Dignified. Mostly.",
            "Science wins again, and science has rhythm.",
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
