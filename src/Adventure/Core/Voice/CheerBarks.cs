namespace DungeonCrawler.Adventure.Voice;

/// <summary>The cheer's numbers (design spec, section 6: "the lead says a cheer line, every other living hero answers within 2 s ... one each, 1.2 s apart; 20 s cooldown"). Engine-free.</summary>
public static class CheerRules
{
    /// <summary>"20 s cooldown."</summary>
    public const float CooldownSeconds = 20f;
    /// <summary>The first answer lands inside the spec's 2 s.</summary>
    public const float FirstAnswerSeconds = 0.8f;
    /// <summary>"1.2 s apart."</summary>
    public const float AnswerGapSeconds = 1.2f;
    /// <summary>Cheer lines a set is topped up to (the cooldown means eight would never all be heard on a floor).</summary>
    public const int CheerMinLines = 4;
    /// <summary>Answers a set is topped up to.</summary>
    public const int AnswerMinLines = 6;

    /// <summary>Everyone living but the cheerer, in the order given.</summary>
    public static IReadOnlyList<string> Answerers(IReadOnlyList<string> living, string cheerer)
        => living.Where(n => !string.Equals(n, cheerer, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>When the n-th answer (0-based) lands after the cheer: 0.8, 2.0, 3.2 s.</summary>
    public static float AnswerDelay(int index) => FirstAnswerSeconds + index * AnswerGapSeconds;
}

/// <summary>What a hero shouts when the lead presses Cheer, and what the others answer: own lines by roster name, else by class, else generic. Engine-free.</summary>
public static class CheerBarks
{
    private static readonly Dictionary<string, string[]> CheersByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[] { "Right, you lot. Still alive. Keep it that way.", "Heads up! Nobody's dead and I'm counting that.", "On me! Still breathing, still paid!", "Hoy! Shields high, chins higher!" },
        ["Sable"] = new[] { "Darlings! Still gorgeous, still rich-ish!", "Hey! Who's the best party in this hole? Us. Obviously.", "Cheer up, you lot. I'm here. That's the good news.", "Oi! Alive and pretty! Say it back!" },
        ["Wren"] = new[] { "Still here.", "Hey. Good work. Walk on.", "Up, all of you. Not dead yet.", "Steady. Together. Good." },
        ["Ilex"] = new[] { "Colleagues! We persist. Remarkable.", "Attention! A moment of unearned confidence!", "Behold us: alive, and in defiance of the evidence.", "Onward! For scholarship, and for spite!" },
    };
    private static readonly Dictionary<string, string[]> CheersByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class.warrior"] = new[] { "On me! Keep it tight!", "Still standing! Good!", "Heads up, you lot!", "Chins up. Steel out." },
        ["class.rogue"] = new[] { "Hey! Still gorgeous!", "Cheer up, darlings!", "Oi! Say it back!", "Alive! Rich soon!" },
        ["class.ranger"] = new[] { "Still here.", "Walk on.", "Together. Good.", "Not dead yet." },
        ["class.mage"] = new[] { "Colleagues! We persist.", "Onward!", "Alive, remarkably.", "Attention! Confidence!" },
    };
    private static readonly string[] CheersGeneric = { "Hey! Still alive!", "On me!", "Keep going!", "We've got this!" };

    private static readonly Dictionary<string, string[]> AnswersByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[] { "Aye.", "Still here. Unfortunately.", "Right behind you. Mind the shield.", "Hah. Fine. Hoy.", "Keep it up and I'll hum.", "That's the spirit. Now walk." },
        ["Sable"] = new[] { "Woo! Someone had to say it.", "Yes, darling. Now where's the gold?", "Gorgeous and alive. Both true.", "Cheering. Quietly. Don't look.", "Right behind you. Checking your pockets.", "Oi, oi! Wait, that's mine." },
        ["Wren"] = new[] { "Mm.", "Aye.", "Here.", "Good.", "Still up.", "Walk on." },
        ["Ilex"] = new[] { "Quite.", "Hear, hear. Reluctantly.", "I concur, loudly.", "Yes. Onward. Et cetera.", "A cheer. How tribal. Hurrah.", "Noted. Enthusiastically noted." },
    };
    private static readonly Dictionary<string, string[]> AnswersByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class.warrior"] = new[] { "Aye!", "Still here!", "Right behind you!", "Hoy!", "Walk on!" },
        ["class.rogue"] = new[] { "Woo!", "Yes, darling!", "Oi, oi!", "Still gorgeous!", "Behind you!" },
        ["class.ranger"] = new[] { "Aye.", "Here.", "Mm.", "Good.", "Still up." },
        ["class.mage"] = new[] { "Quite.", "Hear, hear.", "Concur.", "Hurrah.", "Onward." },
    };
    private static readonly string[] AnswersGeneric = { "Yeah!", "Still here!", "Aye!", "Onward!", "Together!" };

    /// <summary>The lines a hero rallies the others with.</summary>
    public static IReadOnlyList<string> Cheers(string name, string classId) => Pick(CheersByName, CheersByClass, CheersGeneric, name, classId);
    /// <summary>The lines a hero answers a rally with.</summary>
    public static IReadOnlyList<string> Answers(string name, string classId) => Pick(AnswersByName, AnswersByClass, AnswersGeneric, name, classId);

    private static IReadOnlyList<string> Pick(Dictionary<string, string[]> byName, Dictionary<string, string[]> byClass, string[] generic, string name, string classId)
    {
        if (byName.TryGetValue(name, out var own)) return own;
        if (byClass.TryGetValue(classId, out var cls)) return cls;
        return generic;
    }

    /// <summary>Top a set up to <see cref="CheerRules.CheerMinLines"/> Cheer lines and <see cref="CheerRules.AnswerMinLines"/> answers; returns how many were added.</summary>
    public static int TopUp(BarkSet set, string classId)
        => Fill(set, BarkTrigger.Cheer, Cheers(set.Member, classId), CheerRules.CheerMinLines)
         + Fill(set, BarkTrigger.CheerAnswer, Answers(set.Member, classId), CheerRules.AnswerMinLines);

    private static int Fill(BarkSet set, BarkTrigger trigger, IReadOnlyList<string> stock, int min)
    {
        var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == trigger).Select(l => BarkRepeat.Key(l.Line.Text)));
        if (have.Count >= min) return 0;
        var added = 0;
        foreach (var text in stock)
        {
            if (!have.Add(BarkRepeat.Key(text))) continue;
            set.Lines.Add(new BarkLine { Trigger = trigger, Line = new Line(text) });
            added++;
        }
        return added;
    }
}
