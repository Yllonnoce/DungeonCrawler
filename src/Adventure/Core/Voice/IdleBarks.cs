namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a companion says when the player has stood in one place too long: ten razzings per hero (by roster name, else
/// by class, else generic), topped up into the campaign's bark set like the kill lines, and a watch that decides when
/// the party counts as dawdling. Engine-free.
/// </summary>
public static class IdleBarks
{
    /// <summary>Seconds of standing still before the first razzing.</summary>
    public const double IdleAfterSeconds = 10;
    /// <summary>Seconds between razzings while the player keeps standing there.</summary>
    public const double RepeatSeconds = 20;
    /// <summary>Metres the lead may drift and still count as standing still.</summary>
    public const float StillRadius = 0.6f;
    /// <summary>A set with fewer Idle lines than this is topped up from stock.</summary>
    public const int MinLines = 8;

    private static readonly Dictionary<string, string[]> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[]
        {
            "Are we camping here? Say so and I'll light a fire.",
            "The stairs are that way. Or that way. Pick one. Any one.",
            "I've seen statues with more go in them.",
            "Moss is forming on your boots. I can hear it.",
            "If you're waiting for the dungeon to come to you, it will, and you won't like it.",
            "Move, or I start charging rent for standing in my shadow.",
            "Thinking? Don't strain yourself. Walk and think at the same time; the rest of us manage.",
            "I've held shield walls shorter than this.",
            "Left foot. Then the right. It's a system. Try it.",
            "Fine. I'll go alone, and you can explain to the alderman why.",
        },
        ["Sable"] = new[]
        {
            "You're standing like somebody owes you money. Nobody down here pays.",
            "Shall I pick the lock on your feet?",
            "I've robbed people faster than you decide which way to face.",
            "Go on, dawdle. The rats are taking bets on you.",
            "Don't worry, I'm sure the treasure will wait. Treasure's patient like that. It isn't.",
            "Is this a plan, or did your brain step out for a smoke?",
            "I could rob you blind right now and you'd still be standing here.",
            "Lovely spot. Shall we die in it, or somewhere with better light?",
            "Blink twice if you've forgotten how legs work.",
            "You know the walls don't move, yes? You have to.",
        },
        ["Wren"] = new[]
        {
            "I've tracked deer that moved more than you. Dead ones.",
            "Standing still works for a tree. You lack the leaves.",
            "The trail's cold and so are my feet. Walk.",
            "If this is stealth, it's the loud kind.",
            "Two things don't move down here: the dead and you. Keep it to the one.",
            "The arrow doesn't fly until you nock it. Same with you and this corridor.",
            "I count sixty breaths. The dungeon counted them too.",
            "Lost? The way on is the one you're not looking at.",
            "Even a stump knows which way the sun went. Pick a direction.",
            "I'll scout ahead. By which I mean anywhere but here.",
        },
        ["Ilex"] = new[]
        {
            "Fascinating. A man standing in a corridor. I shall write a monograph.",
            "The mind moves the body. In your case the message appears to be lost in transit.",
            "I have watched candles make decisions faster.",
            "Shall I cast Haste on you, or would that only waste the spell?",
            "We are losing light, warmth and, I suspect, brain tissue. Walk.",
            "Contemplation is a virtue. Loitering is merely a habit. Guess which this is.",
            "The door will not open from pure longing. I have tested this.",
            "Is this a tactical pause, or did you forget what feet are for?",
            "Every second you stand there the dungeon grows a little wiser than you. It did not need the help.",
            "Marvellous. We have found the one puzzle you cannot solve: forwards.",
        },
    };

    private static readonly Dictionary<string, string[]> ByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class.warrior"] = new[]
        {
            "Are we camping here? Say so.",
            "I've seen statues with more go in them.",
            "Left foot, then right. It's a system.",
            "The stairs are that way. Pick a way.",
            "Move, or the dungeon moves first.",
            "I've held shield walls shorter than this.",
            "Thinking? Walk while you do it.",
            "Fine. I'll go alone and you can explain it.",
            "Rust forms on a sword that waits. Same for us.",
            "Is this a stand, or a nap?",
        },
        ["class.rogue"] = new[]
        {
            "Shall I pick the lock on your feet?",
            "The rats are taking bets on you.",
            "Is this a plan, or did your brain step out?",
            "Lovely spot. Shall we die in it?",
            "Blink twice if you've forgotten how legs work.",
            "The walls don't move. You have to.",
            "I've robbed faster than you decide.",
            "Standing like somebody owes you money.",
            "Treasure's patient, they say. It isn't.",
            "Go on, I'll time you. Again.",
        },
        ["class.ranger"] = new[]
        {
            "Standing still works for a tree.",
            "I've tracked deer that moved more. Dead ones.",
            "The trail's cold and so are my feet.",
            "If this is stealth, it's the loud kind.",
            "Sixty breaths. The dungeon counted too.",
            "Lost? Try the way you're not looking.",
            "Even a stump knows which way the sun went.",
            "I'll scout ahead. Meaning anywhere but here.",
            "Two things don't move down here. Be the other one.",
            "Nock the arrow, or it never flies. Same with you.",
        },
        ["class.mage"] = new[]
        {
            "I have watched candles decide faster.",
            "Shall I cast Haste, or would it be wasted?",
            "Contemplation is a virtue. This is loitering.",
            "The door will not open from longing. I tested it.",
            "We are losing light and, I suspect, brain tissue.",
            "A tactical pause, or did you forget your feet?",
            "The one puzzle you cannot solve: forwards.",
            "The mind moves the body. Yours lost the message.",
            "A man standing in a corridor. I shall write it up.",
            "Every second here the dungeon grows wiser than you.",
        },
    };

    private static readonly string[] Generic =
    {
        "Are we going, or are we furniture?",
        "The way on is the one you're not looking at.",
        "I've seen walls with more initiative.",
        "Standing still is a tactic. A bad one.",
        "Move, or I start naming the cobwebs after you.",
        "Is this the plan? Standing here is the plan?",
        "Your feet work. I've seen them do it.",
        "Pick a door. Any door. A wall, even.",
        "The dungeon isn't coming to us. Sadly.",
        "At this pace the next generation finishes the job.",
    };

    /// <summary>The razzings for a hero: their own by name, else the class's, else the generic ten.</summary>
    public static IReadOnlyList<string> For(string name, string classId)
    {
        if (ByName.TryGetValue(name, out var own)) return own;
        if (ByClass.TryGetValue(classId, out var cls)) return cls;
        return Generic;
    }

    /// <summary>Append stock Idle lines a bark set lacks until it holds at least <see cref="MinLines"/>; returns how many were added.</summary>
    public static int TopUp(BarkSet set, string classId)
    {
        var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == BarkTrigger.Idle).Select(l => BarkRepeat.Key(l.Line.Text)));
        if (have.Count >= MinLines) return 0;
        var added = 0;
        foreach (var text in For(set.Member, classId))
        {
            if (!have.Add(BarkRepeat.Key(text))) continue;
            set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Idle, Line = new Line(text) });
            added++;
        }
        return added;
    }

    /// <summary>Who razzes this time: the companions take turns, never the one the player is playing; null when the lead is alone.</summary>
    public static string? Razzer(IReadOnlyList<string> party, string lead, int turn)
    {
        var others = party.Where(p => p != lead).ToList();
        return others.Count == 0 ? null : others[Math.Abs(turn) % others.Count];
    }
}

/// <summary>Keeps the lead's last resting place and says when they have stood on it long enough to deserve a word.</summary>
public sealed class IdleWatch
{
    private float _x, _z;
    private double _since = double.NaN;
    private double _lastRazz = double.NegativeInfinity;

    /// <summary>Feed the lead's position each tick; true on the ticks a razzing is due. <paramref name="busy"/> (a fight, a dialogue, a menu, a dead party) restarts the clock.</summary>
    public bool Update(float x, float z, double now, bool busy)
    {
        var dx = x - _x; var dz = z - _z;
        if (busy || double.IsNaN(_since) || dx * dx + dz * dz > IdleBarks.StillRadius * IdleBarks.StillRadius)
        {
            _x = x; _z = z; _since = now; _lastRazz = double.NegativeInfinity;
            return false;
        }
        var still = now - _since;
        if (still < IdleBarks.IdleAfterSeconds) return false;
        if (now - _lastRazz < IdleBarks.RepeatSeconds) return false;
        _lastRazz = now;
        return true;
    }
}
