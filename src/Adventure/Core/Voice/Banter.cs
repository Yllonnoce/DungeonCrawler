using System.Text;

namespace DungeonCrawler.Adventure.Voice;

/// <summary>The banter's numbers, verbatim from the design spec (section 6). Engine-free.</summary>
public static class BanterRules
{
    /// <summary>"Lines queue ... with a 1.4 s gap."</summary>
    public const float LineGapSeconds = 1.4f;
    /// <summary>"In place of one travel grumble in three."</summary>
    public const int TravelShare = 3;
    /// <summary>"In a safe room after 8 s of rest."</summary>
    public const double SafeRoomRestSeconds = 8;
    /// <summary>The tavern's first exchange, and the gap between the rest.</summary>
    public const double TavernFirstSeconds = 5, TavernRepeatSeconds = 45;
    /// <summary>"Exchanges (2-4 lines ...)".</summary>
    public const int MinLines = 2, MaxLines = 4;
    /// <summary>"Forty-five exchanges ship: every pair of heroes at least five."</summary>
    public const int Count = 45, PerPairMin = 5;
    /// <summary>The said-key prefix: an exchange is remembered by id, not by text.</summary>
    public const string SaidPrefix = "banter:";
    /// <summary>The running-gag counters: what Ilex has named, what Brand has counted, what Sable is owed.</summary>
    public const string GeraldCounter = "gerald", DoorsCounter = "doors", EmptyCounter = "empty";
    public static readonly string[] Heroes = { "Brand", "Sable", "Wren", "Ilex" };
    public static string SaidKey(BanterPlan p) => SaidPrefix + p.Id;
    /// <summary>The banter sets the profile's deeds have unlocked (run-structure); an exchange with a Set plays only when its set is here.</summary>
    public static HashSet<string> UnlockedSets { get; } = new();
}

/// <summary>Fills the gag tokens in a banter line: <c>{count}</c> with the number, <c>{roman}</c> with its numeral.</summary>
public static class BanterText
{
    public static string Render(string text, int count)
        => text.Replace("{count}", count <= 0 ? "" : count.ToString()).Replace("{roman}", Roman(count));

    private static readonly (int Value, string Numeral)[] Numerals =
    {
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    };

    public static string Roman(int n)
    {
        if (n <= 0) return "";
        var sb = new StringBuilder();
        foreach (var (value, numeral) in Numerals)
            while (n >= value) { sb.Append(numeral); n -= value; }
        return sb.ToString();
    }
}

/// <summary>Which exchanges may play now, and one of them at random.</summary>
public static class BanterPicker
{
    public static List<BanterPlan> Eligible(IEnumerable<BanterPlan> bank, IReadOnlyCollection<string> living, int floor, int tier, IReadOnlyCollection<string> flags, IReadOnlyDictionary<string, int> said, IReadOnlyDictionary<string, int> counts)
        => bank.Where(p => p.Lines.Count >= BanterRules.MinLines && p.Lines.Count <= BanterRules.MaxLines
                && p.Lines.All(l => living.Contains(l.Member))
                && p.MinFloor <= Math.Max(1, floor) // the tavern is floor 0: the floor-1 stock plays there
                && (p.Tier == null || p.Tier == tier)
                && FlagRules.Allowed(p.RequiresFlag, null, flags)
                && (p.Counter == null || p.MinCount <= counts.GetValueOrDefault(p.Counter, 0))
                && (p.Set == null || BanterRules.UnlockedSets.Contains(p.Set))
                && BarkRepeat.CanSay(said, BanterRules.SaidKey(p), floor))
            .ToList();

    public static BanterPlan? Choose(IEnumerable<BanterPlan> bank, IReadOnlyCollection<string> living, int floor, int tier, IReadOnlyCollection<string> flags, IReadOnlyDictionary<string, int> said, IReadOnlyDictionary<string, int> counts, Random rng)
    {
        var fits = Eligible(bank, living, floor, tier, flags, said, counts);
        return fits.Count == 0 ? null : fits[rng.Next(fits.Count)];
    }

    /// <summary>The value the counter will have once this exchange has played: Gerald's second exchange says "Gerald II"; the door count says the doors already opened.</summary>
    public static int CountFor(BanterPlan p, IReadOnlyDictionary<string, int> counts)
        => p.Counter == null ? 0 : counts.GetValueOrDefault(p.Counter) + (p.Bumps ? 1 : 0);
}

/// <summary>One exchange in flight: hands out its lines <c>gapSeconds</c> apart, and drops the rest the moment a speaker is no longer standing.</summary>
public sealed class BanterRun
{
    private readonly double _startAt;
    private readonly float _gap;
    private readonly int _count;

    public BanterRun(BanterPlan plan, double startAt, float gapSeconds, int count)
    {
        Plan = plan; _startAt = startAt; _gap = gapSeconds; _count = count;
    }

    public BanterPlan Plan { get; }
    public bool Done { get; private set; }
    public int Index { get; private set; }

    /// <summary>The next line when its time has come and its speaker is living; null otherwise (and <see cref="Done"/> when the speaker is not).</summary>
    public (string Member, string Text)? Next(double now, Func<string, bool> living)
    {
        if (Done) return null;
        if (now < _startAt + Index * _gap) return null;
        var line = Plan.Lines[Index];
        if (!living(line.Member)) { Done = true; return null; }
        Index++;
        if (Index >= Plan.Lines.Count) Done = true;
        return (line.Member, BanterText.Render(line.Line.Text, _count));
    }

    public void Abort() => Done = true;
}

/// <summary>Fluent setters for the stock bank.</summary>
internal static class BanterPlanSetters
{
    public static BanterPlan From(this BanterPlan p, int minFloor) { p.MinFloor = minFloor; return p; }
    public static BanterPlan InTier(this BanterPlan p, int tier) { p.Tier = tier; return p; }
    public static BanterPlan Needs(this BanterPlan p, string flag) { p.RequiresFlag = flag; return p; }
    public static BanterPlan Once(this BanterPlan p) { p.OncePerRun = true; return p; }
    public static BanterPlan Counts(this BanterPlan p, string counter, int minCount, bool bumps = true) { p.Counter = counter; p.MinCount = minCount; p.Bumps = bumps; return p; }
}

/// <summary>
/// The forty-five exchanges the four heroes have on quiet miles: every pair at least five, running gags with counters
/// (Gerald the puddle, Brand's doors, Sable's tally of empty rooms), callbacks to what this run did, and a few for the
/// whole party. Stock: names only the heroes, never a campaign's people or places. Engine-free.
/// </summary>
public static class BanterBank
{
    private static BanterPlan E(string id, params (string Who, string Text)[] lines)
    {
        var p = new BanterPlan { Id = id };
        foreach (var (who, text) in lines) p.Lines.Add(new BanterLinePlan { Member = who, Line = new Line(text) });
        return p;
    }

    public static IReadOnlyList<BanterPlan> Stock { get; } = new List<BanterPlan>
    {
        // pairs: Brand + Sable
        E("bs_hum",      ("Sable", "Brand. Hum something."), ("Brand", "No."), ("Sable", "Then I'll sing."), ("Brand", "I'll hit something. Possibly you.")),
        E("bs_purse",    ("Brand", "Stop walking behind me."), ("Sable", "Someone has to watch your back."), ("Brand", "My purse is on my back."), ("Sable", "Then I'm watching it very closely.")),
        E("bs_corporal", ("Sable", "You were a sergeant."), ("Brand", "Corporal. Twice."), ("Sable", "Twice?"), ("Brand", "They kept taking it back.")),
        E("bs_lifted",   ("Brand", "How much have you lifted off the party so far?"), ("Sable", "Nothing. Everyone's broke."), ("Brand", "Good."), ("Sable", "It's not good, Brand. It's tragic.")),
        E("bs_knives",   ("Sable", "If I die, take my knives."), ("Brand", "If you die I'm taking everything."), ("Sable", "That's the most romantic thing anyone's said to me.")),
        // Brand + Wren
        E("bw_bricks",    ("Wren", "Brick count's at four thousand."), ("Brand", "You're counting bricks."), ("Wren", "You count doors."), ("Brand", "Doors matter.")),
        E("bw_something", ("Brand", "Say something, Wren. Anything."), ("Wren", "Something."), ("Brand", "Thank you.")),
        E("bw_root",      ("Wren", "I miss trees."), ("Brand", "There was a root on the last floor."), ("Wren", "That was a leg."), ("Brand", "It was a root-shaped leg.")).InTier(0),
        E("bw_quiet",     ("Brand", "You walk quiet for a big lad."), ("Wren", "I walk quiet for anything."), ("Brand", "Teach me."), ("Wren", "Take the shield off.")),
        E("bw_shield",    ("Wren", "Shield up."), ("Brand", "Nothing's there."), ("Wren", "Then it's cheap practice.")),
        // Brand + Ilex
        E("bi_manners", ("Ilex", "Walking is merely falling forwards with manners."), ("Brand", "Then I've been falling for thirty years."), ("Ilex", "It shows in the knees.")),
        E("bi_fire",    ("Brand", "Can you magic the stairs closer?"), ("Ilex", "I can set fire to you so the walking feels shorter."), ("Brand", "Pass.")).InTier(2),
        E("bi_eleven",  ("Ilex", "Brand, how many men did you command?"), ("Brand", "Eleven."), ("Ilex", "And where are they now?"), ("Brand", "Alive. I made sure of that. Shut up and walk.")),
        E("bi_gerald",  ("Brand", "Ilex. The puddle."), ("Ilex", "Gerald."), ("Brand", "I'm not calling it Gerald."), ("Ilex", "He'll not mind. He's a very forgiving puddle.")).Counts("gerald", 1, bumps: false),
        E("bi_slower",  ("Ilex", "Your sword arm is twelve percent slower than on the first floor."), ("Brand", "You measured."), ("Ilex", "I observe. It is different."), ("Brand", "Observe your own feet, then. There's a hole.")),
        // Sable + Wren
        E("sw_sentence", ("Sable", "Wren. Say a whole sentence."), ("Wren", "No."), ("Sable", "That's one word."), ("Wren", "It had everything it needed.")),
        E("sw_oil",      ("Wren", "You smell of lamp oil."), ("Sable", "I smell of commerce."), ("Wren", "I'm downwind of commerce. Move.")),
        E("sw_miss",     ("Sable", "What do you miss most, up there?"), ("Wren", "Trees."), ("Sable", "Gold."), ("Wren", "Trees don't get stolen.")),
        E("sw_mildred",  ("Wren", "Every trap on this floor has a name."), ("Sable", "Go on."), ("Wren", "That one's Mildred. Don't step on Mildred.")),
        E("sw_locks",    ("Sable", "If you see a lock, tell me first."), ("Wren", "I saw six."), ("Sable", "Six? Where?"), ("Wren", "Behind us. You were talking.")),
        // Sable + Ilex
        E("si_pockets",   ("Sable", "Ilex. Hold still."), ("Ilex", "Why?"), ("Sable", "Practice."), ("Ilex", "Those are my pockets.")),
        E("si_etymology", ("Ilex", "Do you know the etymology of 'thief'?"), ("Sable", "Do you know the etymology of 'shut up'?"), ("Ilex", "Middle period, regrettably.")),
        E("si_brazier",   ("Sable", "Set fire to something. I'm bored."), ("Ilex", "I am not a brazier, Sable."), ("Sable", "You're shaped like one.")),
        E("si_damp",      ("Ilex", "I have catalogued four kinds of damp."), ("Sable", "I've catalogued one kind of wizard."), ("Ilex", "Which?"), ("Sable", "Damp.")).InTier(1),
        E("si_notes",     ("Sable", "If I ever rob you properly, what's worth taking?"), ("Ilex", "My notes."), ("Sable", "Nobody's buying notes."), ("Ilex", "Then I am safe, and you are poor.")),
        // Wren + Ilex
        E("wi_count",    ("Ilex", "Wren, what is the brick count?"), ("Wren", "Four thousand two hundred."), ("Ilex", "Impressive."), ("Wren", "You've named six puddles.")),
        E("wi_loud",     ("Wren", "Fire's loud."), ("Ilex", "Fire is thorough."), ("Wren", "I liked it quiet."), ("Ilex", "The dead liked it quiet. Note where that got them.")),
        E("wi_theology", ("Ilex", "There is a theological argument for this place."), ("Wren", "Is it 'leave'?"), ("Ilex", "It is now.")),
        E("wi_rations",  ("Wren", "Rations."), ("Ilex", "What about them?"), ("Wren", "Yours are in Sable's bag."), ("Ilex", "Since when?")),
        E("wi_opinion",  ("Ilex", "You never speak at length, Wren."), ("Wren", "Nothing's needed it."), ("Ilex", "Fascinating. A man without an opinion."), ("Wren", "I have one. It's about you.")),
        // running gags
        E("gag_gerald_1",     ("Ilex", "That puddle is Gerald."), ("Sable", "Why Gerald?"), ("Ilex", "He has the look of a Gerald.")).Counts("gerald", 0).Once(),
        E("gag_gerald_n",     ("Ilex", "Gerald {roman}, I presume. Long may he reign."), ("Brand", "You're naming puddles."), ("Ilex", "Somebody must.")).Counts("gerald", 1),
        E("gag_gerald_widow", ("Ilex", "Observe. A puddle beside a dry patch. Gerald's widow."), ("Sable", "You've given a puddle a marriage."), ("Ilex", "A happy one, until the drain."), ("Wren", "Can we go.")).Counts("gerald", 3).Once(),
        E("gag_doors_10",     ("Brand", "That's {count} doors. Who builds {count} doors?"), ("Sable", "A man with a hinge habit."), ("Brand", "I'm counting them. Somebody should.")).Counts("doors", 10, bumps: false),
        E("gag_doors_40",     ("Brand", "Door {count}."), ("Wren", "You said you stopped at forty."), ("Brand", "I lied. I can't stop."), ("Ilex", "It is a sickness, Brand. Doors.")).Counts("doors", 40, bumps: false),
        E("gag_empty_3",      ("Sable", "{count} empty rooms. I'm keeping a tally."), ("Brand", "Of what?"), ("Sable", "Of what I'm owed. Boredom's a cost.")).Counts("empty", 3, bumps: false),
        E("gag_empty_8",      ("Sable", "That's {count}. Empty rooms. Ilex, your pockets. Hold still."), ("Ilex", "There is nothing in them."), ("Sable", "Then it'll be quick.")).Counts("empty", 8, bumps: false),
        // callbacks to what this run did (flags set by AdventureDirector.Talk from the EventBus moments)
        E("cb_mimic",  ("Sable", "I'm not opening the next chest."), ("Brand", "You open every chest."), ("Sable", "The last one had teeth, Brand. Teeth."), ("Wren", "It also had a tongue. I checked.")).Needs("seen_mimic").Once(),
        E("cb_wipe",   ("Brand", "We died."), ("Ilex", "We did not die. We were interrupted."), ("Brand", "All four of us. Face down."), ("Ilex", "Interrupted at length.")).Needs("wiped_once").Once(),
        E("cb_boon",   ("Ilex", "I feel improved."), ("Sable", "You feel smug."), ("Ilex", "The two present identically.")).Needs("took_boon").Once(),
        E("cb_curse",  ("Wren", "Who took the curse."), ("Sable", "It was that or the gold."), ("Wren", "You took the gold too."), ("Sable", "I'm a thief, Wren. Be reasonable.")).Needs("took_curse").Once(),
        E("cb_gamble", ("Sable", "The merchant's cloth bundles are a swindle."), ("Brand", "You bought three."), ("Sable", "I'm thorough about being swindled.")).Needs("gambled").Once(),
        // the whole party
        E("grp_alive",   ("Brand", "Right. Nobody's dead. Good."), ("Sable", "Low bar."), ("Wren", "Cleared it."), ("Ilex", "Barely.")),
        E("grp_watched", ("Sable", "Does anyone else feel watched?"), ("Wren", "Yes."), ("Ilex", "Constantly."), ("Brand", "Walk faster, then.")).InTier(3).From(16),
        E("bs_halfway",  ("Brand", "Halfway."), ("Sable", "Says who?"), ("Brand", "My knees.")).From(10),
    };

    private static BanterPlan S(string set, string id, params (string Who, string Text)[] lines) { var p = E(id, lines); p.Set = set; return p; }

    /// <summary>
    /// The exchanges the deeds unlock (run-structure), by set: they join the stock bank only while <see cref="BanterRules.UnlockedSets"/>
    /// holds their set. Two per set, the same dry register as the stock, never a campaign's people or places.
    /// </summary>
    public static IReadOnlyList<BanterPlan> Unlockable { get; } = new List<BanterPlan>
    {
        // wipes: Bang on Schedule
        S("wipes", "un_wipes_1", ("Brand", "Fourth time I've died on a Tuesday."), ("Sable", "It isn't Tuesday."), ("Brand", "It is down here. It's always Tuesday down here.")),
        S("wipes", "un_wipes_2", ("Ilex", "Statistically we are improving."), ("Wren", "We died."), ("Ilex", "Later than last time. By a full minute.")),
        // mimic: Bit First
        S("mimic", "un_mimic_1", ("Sable", "Knock first."), ("Brand", "On a chest?"), ("Sable", "On anything with a lid, Brand. Manners.")),
        S("mimic", "un_mimic_2", ("Wren", "That one's breathing."), ("Ilex", "Chests do not breathe."), ("Wren", "Tell it that."), ("Ilex", "I'd rather you did.")),
        // shrines: Devout
        S("shrines", "un_shrines_1", ("Ilex", "Six altars. We are practically clergy."), ("Sable", "Clergy don't get paid in gold."), ("Ilex", "You have not met clergy.")),
        S("shrines", "un_shrines_2", ("Brand", "I don't trust a god that hands out favours."), ("Wren", "You took three."), ("Brand", "I don't trust them. I didn't say I'd refuse.")),
        // guard: Iron Manners
        S("guard", "un_guard_1", ("Brand", "Shield up, feet planted, breathe out."), ("Sable", "Is that a prayer?"), ("Brand", "It's a parry. The prayer comes after.")),
        S("guard", "un_guard_2", ("Wren", "Twenty-five perfect blocks."), ("Brand", "Who's counting?"), ("Wren", "Your shield. There's a notch for each one.")),
        // parry: Riposte Society
        S("parry", "un_parry_1", ("Sable", "You turned that one on the edge."), ("Brand", "Luck."), ("Sable", "Twenty-five times. That's not luck, that's a habit.")),
        S("parry", "un_parry_2", ("Ilex", "There is a geometry to the riposte."), ("Sable", "There's a wrist to it."), ("Ilex", "The wrist is the geometry.")),
        // elites: Landlord
        S("elites", "un_elites_1", ("Brand", "Another one with a name."), ("Wren", "They've all got names. We just don't ask."), ("Brand", "Fifty of them never got to say.")),
        S("elites", "un_elites_2", ("Sable", "The named ones used to scare me."), ("Ilex", "And now?"), ("Sable", "Now they're a ledger entry.")),
        // rescue: Pulled Free
        S("rescue", "un_rescue_1", ("Wren", "Hold still. I've done this nine times."), ("Brand", "To me?"), ("Wren", "Mostly to you.")),
        S("rescue", "un_rescue_2", ("Ilex", "You pulled me out of a Press."), ("Sable", "You were in the way of the loot."), ("Ilex", "That is the kindest thing you have said to me.")),
        // hardcore: Signed in Blood
        S("hardcore", "un_hardcore_1", ("Brand", "No second chances this time."), ("Sable", "There never were. We just used to pretend."), ("Brand", "I liked pretending.")),
        S("hardcore", "un_hardcore_2", ("Wren", "If I go down, leave me."), ("Ilex", "We are contractually obliged to."), ("Wren", "Good."), ("Ilex", "It does not say we have to like it.")),
    };
}
