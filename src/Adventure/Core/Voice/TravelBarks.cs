namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What a hero says when the party has walked a long way with nothing to fight: twenty grumbles per hero by roster
/// name (Sable's a woman's), else five by class, else five generic, topped up into the campaign's bark set like the
/// other stock lines. <see cref="TravelWatch"/> decides when the miles have been quiet enough. Engine-free.
/// </summary>
public static class TravelBarks
{
    /// <summary>A set with fewer Travel lines than this is topped up from stock.</summary>
    public const int MinLines = 20;

    private static readonly Dictionary<string, string[]> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brand"] = new[]
        {
            "I guess we're getting paid by the mile.",
            "Walking for a living. Didn't see that in the contract.",
            "If I'd wanted to march I'd have stayed in the levy.",
            "My feet have opinions. None of them polite.",
            "Another corridor. Somebody's very proud of these corridors.",
            "Anyone counting the doors? I stopped at forty.",
            "Shield's getting heavy. Nothing to hit with it.",
            "Wake me if something growls.",
            "I've had quieter patrols. Not many.",
            "At this rate the Verger dies of old age before we find him.",
            "Rations are low. Patience is lower.",
            "A fight would be nice. A chair would be nicer.",
            "Left, left, left again. I'm starting to suspect a circle.",
            "The dead are doing a poor job of defending this place.",
            "I've seen livelier graveyards. In fact, this is one.",
            "My sword is sulking. It hasn't been out in an hour.",
            "Somebody hum something. Not Sable.",
            "Mind the puddle. Mind the other puddle.",
            "The alderman said dangerous. The alderman lied, or is bad at walking.",
            "Boots. Stone. Boots. Stone. Gripping stuff.",
        },
        ["Sable"] = new[]
        {
            "Did somebody just pass gas? Brand. It was Brand.",
            "I could be robbing someone right now. Someone rich. Someone indoors.",
            "A girl walks into a dungeon. That's it. That's the whole joke.",
            "Nothing to steal, nothing to stab. Worst holiday ever.",
            "I spy, with my little eye, something beginning with stone.",
            "If the next room's empty too I'm picking Ilex's pockets for practice.",
            "I've cased houses faster than we're casing this hole.",
            "Who built this? And can I rob them instead?",
            "I'd sing, but Brand said not to. Brand has no ear.",
            "My knives are bored. Bored knives slip.",
            "Twenty more steps and I'm charging a walking fee.",
            "Is it just me, or does every corridor smell of regret?",
            "A lady needs excitement. A lady is getting corridors.",
            "Wake me when there's a lock. Any lock.",
            "I've stolen things that moved more than this dungeon.",
            "Shall we take bets on the next room? I say empty. I always win.",
            "If I see one more torch bracket I'm stealing it out of spite.",
            "Nothing's tried to kill us in ages. I'm almost insulted.",
            "I miss the city. The city had things worth taking.",
            "Walk, walk, walk. My thighs will be magnificent and nobody will see them.",
        },
        ["Wren"] = new[]
        {
            "Fighting for a living is overrated anyway.",
            "No tracks. Nothing's been through here but us and the damp.",
            "I've stalked deer for a day and been less bored.",
            "The wind would tell me something. There's no wind.",
            "My arrows are getting dusty in the quiver.",
            "Mark the wall. We've been here. Twice, now.",
            "Quiet dungeon. Quiet's where things hide. Or where nothing is.",
            "I could track a mouse through this. There are no mice.",
            "Nothing to shoot. I'm counting bricks instead. Four thousand.",
            "Out in the woods there'd be a bird by now. Something.",
            "Anyone else's boots talking to them? Mine won't shut up.",
            "If this is a trail, it's the dullest one I've walked.",
            "A ranger's patience is long. Not this long.",
            "I'll scout ahead. Ahead is more of this.",
            "I miss trees. I'd settle for a shrub.",
            "Nothing moving. I checked. I keep checking.",
            "The bowstring's going slack from boredom. So am I.",
            "Steady pace. Steady nothing.",
            "If something jumps us now I'll thank it.",
            "The stone has no smell. Give me mud and a dead fox any day.",
        },
        ["Ilex"] = new[]
        {
            "At this pace my thesis will write itself from boredom.",
            "I have memorised the mortar pattern. It is not interesting.",
            "A spell unused is a spell wasted. I am very wasteful today.",
            "I could enchant a lantern to walk for us. Nobody asked.",
            "The academy never covered tedium. An oversight.",
            "Corridor. Corridor. A rich variety of corridor.",
            "My robes gather dust at a measurable rate. I have measured it.",
            "I count forty-one torches, no foes and one pressing need to sit.",
            "If knowledge is power, I am learning a great deal about flagstones.",
            "Observe: nothing. Again.",
            "Walking is merely falling forwards with manners.",
            "I would summon something to fight, but it would also be bored.",
            "My feet file a formal complaint.",
            "I have begun naming the puddles. This one is Gerald.",
            "A dungeon this empty should be ashamed of itself.",
            "Somebody say something clever. Not Brand.",
            "Fireball. Just to see colour. No? Very well.",
            "My mana regenerates faster than my interest.",
            "The council pays by the floor, not by the fight. Walk on.",
            "Even the echoes have stopped bothering.",
        },
    };

    private static readonly Dictionary<string, string[]> ByClass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["class.warrior"] = new[] { "Paid by the mile, then.", "Shield's heavy. Nothing to hit.", "Wake me if something growls.", "I've had quieter patrols. Not many.", "A fight would be nice. A chair, nicer." },
        ["class.rogue"] = new[] { "Did somebody just pass gas?", "Nothing to steal, nothing to stab.", "Wake me when there's a lock.", "I could be robbing someone indoors.", "Every corridor smells of regret." },
        ["class.ranger"] = new[] { "Fighting for a living is overrated.", "No tracks. Nothing's been through.", "My arrows are getting dusty.", "I miss trees. I'd settle for a shrub.", "If something jumps us I'll thank it." },
        ["class.mage"] = new[] { "A spell unused is a spell wasted.", "Corridor. Corridor. Corridor.", "Observe: nothing. Again.", "My feet file a formal complaint.", "Even the echoes stopped bothering." },
    };

    private static readonly string[] Generic =
    {
        "Are we there yet? There being anywhere.",
        "Long walk for nothing so far.",
        "I'd take a fight over another corridor.",
        "Somebody say something. Anything.",
        "My feet vote we turn back. Overruled.",
    };

    /// <summary>The travel grumbles for a hero: their own by name, else the class's, else the generic five.</summary>
    public static IReadOnlyList<string> For(string name, string classId)
    {
        if (ByName.TryGetValue(name, out var own)) return own;
        if (ByClass.TryGetValue(classId, out var cls)) return cls;
        return Generic;
    }

    /// <summary>Append stock Travel lines a bark set lacks until it holds at least <see cref="MinLines"/>; returns how many were added.</summary>
    public static int TopUp(BarkSet set, string classId)
    {
        var have = new HashSet<string>(set.Lines.Where(l => l.Trigger == BarkTrigger.Travel).Select(l => BarkRepeat.Key(l.Line.Text)));
        if (have.Count >= MinLines) return 0;
        var added = 0;
        foreach (var text in For(set.Member, classId))
        {
            if (!have.Add(BarkRepeat.Key(text))) continue;
            set.Lines.Add(new BarkLine { Trigger = BarkTrigger.Travel, Line = new Line(text) });
            added++;
        }
        return added;
    }

    /// <summary>Who grumbles this time: the living party in turn, the player's own hero included; null for an empty party.</summary>
    public static string? Speaker(IReadOnlyList<string> party, int turn) => party.Count == 0 ? null : party[Math.Abs(turn) % party.Count];
}

/// <summary>Counts quiet miles: a grumble is due once nothing has happened for <see cref="QuietSeconds"/> and the party has walked <see cref="MinDistance"/> since the last one, no oftener than every <see cref="RepeatSeconds"/>.</summary>
public sealed class TravelWatch
{
    /// <summary>Seconds without a fight, a conversation or a menu before the walking counts as boring.</summary>
    public const double QuietSeconds = 45;
    /// <summary>Least seconds between two grumbles.</summary>
    public const double RepeatSeconds = 40;
    /// <summary>Metres the party must cover between grumbles: standing about is the idle razzing's business.</summary>
    public const float MinDistance = 20f;

    private double _lastAction = double.NegativeInfinity;
    private double _lastLine = double.NegativeInfinity;
    private float _distanceAtLast;

    /// <summary>Feed the lead's distance travelled each tick; true on the ticks a grumble is due. <paramref name="action"/> (a fight, a dialogue, a menu, a downed lead) restarts the quiet clock.</summary>
    public bool Update(double now, float distance, bool action)
    {
        if (distance < _distanceAtLast) _distanceAtLast = distance; // a new body on a new floor starts from nothing
        if (action) { _lastAction = now; return false; }
        if (now - _lastAction < QuietSeconds) return false;
        if (now - _lastLine < RepeatSeconds) return false;
        if (distance - _distanceAtLast < MinDistance) return false;
        _lastLine = now;
        _distanceAtLast = distance;
        return true;
    }
}
