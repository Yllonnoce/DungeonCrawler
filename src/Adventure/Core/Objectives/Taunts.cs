using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>What sets the villain off. The first fifteen mirror <see cref="ObjectiveTrigger"/> by name; the rest are the dungeon's own moments.</summary>
public enum TauntTrigger
{
    FloorEnter, Capture, Freed, AllFreed, GasThickens, GasLethal, WrongLever, GateShift, VaultOpened,
    RitualStart, RitualistDown, WaveBreak, RitualDone, ObjectiveDone, Idle,
    MemberDowned, PartyWiped, Descend, Reinforcements,
}

public static class TauntTriggers
{
    public static readonly TauntTrigger[] All = Enum.GetValues<TauntTrigger>();
    public static string Id(TauntTrigger t) => t.ToString();
    public static bool TryParse(string? id, out TauntTrigger t)
    {
        t = TauntTrigger.FloorEnter;
        return !string.IsNullOrWhiteSpace(id) && Enum.TryParse(id.Trim(), ignoreCase: true, out t) && Enum.IsDefined(t);
    }
    public static TauntTrigger From(ObjectiveTrigger o) => Enum.Parse<TauntTrigger>(o.ToString());
    /// <summary>Whether a trigger belongs to one objective kind in particular (a bank line for it may say which).</summary>
    public static FloorObjectiveKind KindOf(TauntTrigger t) => t switch
    {
        TauntTrigger.Capture or TauntTrigger.Freed or TauntTrigger.AllFreed => FloorObjectiveKind.Jailbreak,
        TauntTrigger.GasThickens or TauntTrigger.GasLethal => FloorObjectiveKind.Toxic,
        TauntTrigger.WrongLever or TauntTrigger.GateShift or TauntTrigger.VaultOpened => FloorObjectiveKind.ShiftingVault,
        TauntTrigger.RitualStart or TauntTrigger.RitualistDown or TauntTrigger.WaveBreak or TauntTrigger.RitualDone => FloorObjectiveKind.Ritual,
        _ => FloorObjectiveKind.None,
    };
}

/// <summary>
/// Picks what the villain says, if anything: never within a few seconds of the last line, never the same trigger
/// twice in under a minute, never a line said in the last two floors, only lines whose objective and flags fit.
/// Engine-free; the director feeds it the clock.
/// </summary>
public sealed class TauntDirector
{
    public const double GlobalCooldown = 12, PerTriggerCooldown = 45, NarrationGap = 4, IdleAfter = 60;

    private double _lastAny = double.NegativeInfinity, _lastNarration = double.NegativeInfinity;
    private readonly Dictionary<TauntTrigger, double> _lastPer = new();

    /// <summary>Someone (narrator, boss, NPC) just spoke: the villain waits its turn.</summary>
    public void NoteNarration(double now) => _lastNarration = now;

    public bool CanSpeak(TauntTrigger trigger, double now, bool urgent = false)
    {
        if (now - _lastNarration < NarrationGap && !urgent) return false;
        if (now - _lastAny < GlobalCooldown && !urgent) return false;
        return !_lastPer.TryGetValue(trigger, out var last) || now - last >= PerTriggerCooldown;
    }

    public TauntPlan? Pick(IReadOnlyList<TauntPlan> bank, TauntTrigger trigger, FloorObjectiveKind kind, IReadOnlyDictionary<string, int> saidOnFloor, IReadOnlyCollection<string>? flags, int floor, double now, Random rng, bool urgent = false)
    {
        if (!CanSpeak(trigger, now, urgent)) return null;
        var fits = bank.Where(t => TauntTriggers.TryParse(t.Trigger, out var tt) && tt == trigger && t.Line.Text.Trim().Length > 0)
            .Where(t => string.IsNullOrWhiteSpace(t.Objective) || (FloorObjectives.TryParse(t.Objective, out var k) && k == kind))
            .Where(t => FlagRules.Allowed(t.RequiresFlag, t.ForbidsFlag, flags))
            .ToList();
        if (fits.Count == 0) return null;
        // the kind's own lines before the general ones, when it has any that are fresh
        var own = fits.Where(t => !string.IsNullOrWhiteSpace(t.Objective)).ToList();
        var pool = own.Count > 0 && own.Any(t => Voice.BarkRepeat.CanSay(saidOnFloor, t.Line.Text, floor)) ? own : fits;
        var text = Voice.BarkRepeat.Choose(saidOnFloor, pool.Select(t => t.Line.Text).ToList(), floor, rng);
        if (text == null) return null;
        var pick = pool.First(t => t.Line.Text == text);
        _lastAny = now;
        _lastPer[trigger] = now;
        return pick;
    }

    /// <summary>Fill the tokens a line may carry.</summary>
    public static string Render(string text, string? party, int floor, string? arg) => text
        .Replace("{party}", string.IsNullOrWhiteSpace(party) ? "little rats" : party)
        .Replace("{floor}", floor.ToString())
        .Replace("{prisoner}", string.IsNullOrWhiteSpace(arg) ? "that one" : arg)
        .Replace("{room}", string.IsNullOrWhiteSpace(arg) ? "there" : arg);
}

/// <summary>
/// The villain's stock lines, for campaigns nobody wrote taunts for (the offline designer, older plans, a planner that
/// left a trigger empty). Coarse by default, each with a clean twin. Tokens: {party}, {floor}, {prisoner}.
/// </summary>
public static class TauntBank
{
    private static readonly (TauntTrigger Trigger, string? Kind, string Coarse, string Clean)[] Stock =
    {
        (TauntTrigger.FloorEnter, "jailbreak", "Oh good, the heroes. I've penned a few of the locals for you, {party}. Go on, play the shepherd. See how many you lose.", "Oh good, the heroes. I've penned a few of the locals for you, {party}. Go on, play the shepherd. See how many you lose."),
        (TauntTrigger.FloorEnter, "jailbreak", "Every bastard on this floor has a key and a grudge. The villagers are running. So should you.", "Every wretch on this floor has a key and a grudge. The villagers are running. So should you."),
        (TauntTrigger.FloorEnter, "toxic", "Breathe deep, {party}. That's my floor in your lungs. The stairs are down where the air is worst, you poor sods.", "Breathe deep, {party}. That's my floor in your lungs. The stairs are down where the air is worst, you poor fools."),
        (TauntTrigger.FloorEnter, "toxic", "I've opened the vents. Run or choke, it's all the same to me. Shit, it's better than a fight.", "I've opened the vents. Run or choke, it's all the same to me. Frankly, it's better than a fight."),
        (TauntTrigger.FloorEnter, "ritual", "A rite to seal me out? Let the old fool chant. My walls are full of teeth, and they're bloody hungry.", "A rite to seal me out? Let the old fool chant. My walls are full of teeth, and they are hungry."),
        (TauntTrigger.FloorEnter, "ritual", "Hold the shrine, then. Hold it with your damned bare hands while everything I own comes through the stone.", "Hold the shrine, then. Hold it with your bare hands while everything I own comes through the stone."),
        (TauntTrigger.FloorEnter, "shiftingvault", "The stairs are in a box and the box has a bloody combination. Pull my levers, {party}. Guess. I do love a guess.", "The stairs are in a box and the box has a combination. Pull my levers, {party}. Guess. I do love a guess."),
        (TauntTrigger.FloorEnter, "shiftingvault", "Gates, levers, a little arithmetic. Too much for you? Then rot on floor {floor}, it's a lovely floor to rot on.", "Gates, levers, a little arithmetic. Too much for you? Then stay on floor {floor}. It's a lovely floor to stay on."),
        (TauntTrigger.FloorEnter, null, "Floor {floor}, {party}. I've had this one arranged just for you. Try not to bleed on the good stone.", "Floor {floor}, {party}. I've had this one arranged just for you. Try not to bleed on the good stone."),
        (TauntTrigger.Capture, null, "Got one! {prisoner} goes in the cage. Keep running, the rest of you, it makes it so much sweeter.", "Got one! {prisoner} goes in the cage. Keep running, the rest of you, it makes it so much sweeter."),
        (TauntTrigger.Capture, null, "There goes {prisoner}, dragged off by the hair. Where were you, hero? Looting a fucking barrel?", "There goes {prisoner}, dragged off by the hair. Where were you, hero? Looting a barrel?"),
        (TauntTrigger.Freed, null, "One rat out of the cage. Congratulations. I have more cages than you have hours.", "One rat out of the cage. Congratulations. I have more cages than you have hours."),
        (TauntTrigger.Freed, null, "{prisoner} walks free. Fine. Keep them close, {party}. Things that follow you tend to die.", "{prisoner} walks free. Fine. Keep them close, {party}. Things that follow you tend to die."),
        (TauntTrigger.AllFreed, null, "All of them? All of them. Well, damn. Take your stairs. I'll be less gentle below.", "All of them? All of them. Well then. Take your stairs. I'll be less gentle below."),
        (TauntTrigger.AllFreed, null, "Every last peasant. You must feel so bloody noble. Enjoy it. It doesn't last.", "Every last peasant. You must feel so noble. Enjoy it. It doesn't last."),
        (TauntTrigger.GasThickens, null, "Coughing already? That's the cheap stuff. Wait till it's thick enough to chew, you bastards.", "Coughing already? That's the cheap stuff. Wait till it's thick enough to chew."),
        (TauntTrigger.GasThickens, null, "The green is rising, {party}. Higher ground buys you minutes. The stairs are not on higher ground.", "The green is rising, {party}. Higher ground buys you minutes. The stairs are not on higher ground."),
        (TauntTrigger.GasLethal, null, "That's the smell of your lungs giving up. Crawl to the stairs or lie down, I honestly don't give a shit which.", "That's the smell of your lungs giving up. Crawl to the stairs or lie down; I honestly don't mind which."),
        (TauntTrigger.GasLethal, null, "Lethal now. Every breath is mine. Go on, take another.", "Lethal now. Every breath is mine. Go on, take another."),
        (TauntTrigger.WrongLever, null, "Wrong bloody lever. Did you even read the plaque, or do you just yank things?", "Wrong lever. Did you even read the plaque, or do you just pull things?"),
        (TauntTrigger.WrongLever, null, "That shut more than it opened. Brilliant. Truly. My patrols thank you.", "That shut more than it opened. Brilliant. Truly. My patrols thank you."),
        (TauntTrigger.GateShift, null, "Hear that? Iron on stone. My whole floor just rearranged itself around you, {party}.", "Hear that? Iron on stone. My whole floor just rearranged itself around you, {party}."),
        (TauntTrigger.GateShift, null, "Pull, pull, pull. Like children with a bell rope. The gates don't care how hard you pull.", "Pull, pull, pull. Like children with a bell rope. The gates don't care how hard you pull."),
        (TauntTrigger.VaultOpened, null, "Oh, it's open. Fine. You solved a puzzle a bright dog could solve. Go down. It gets worse.", "Oh, it's open. Fine. You solved a puzzle a bright dog could solve. Go down. It gets worse."),
        (TauntTrigger.VaultOpened, null, "The vault yields. Bugger. Well. I have nineteen more floors and a great deal of spite.", "The vault yields. Well. I have nineteen more floors and a great deal of spite."),
        (TauntTrigger.RitualStart, null, "Chanting? In my house? Let's see how much of that prayer gets said with my teeth in the old fool's neck.", "Chanting? In my house? Let's see how much of that prayer gets said with my teeth in the old fool's neck."),
        (TauntTrigger.RitualStart, null, "The rite begins. So does the bleeding. Everything I own is coming through those walls, {party}.", "The rite begins. So does the bleeding. Everything I own is coming through those walls, {party}."),
        (TauntTrigger.RitualistDown, null, "Down goes the priest! Ha! Pick them up, pick them up, I want to see it happen again.", "Down goes the priest! Ha! Pick them up, pick them up, I want to see it happen again."),
        (TauntTrigger.RitualistDown, null, "Your chanter's face-down in the dust. The prayer stops. Shame. I was almost worried, you useless sods.", "Your chanter's face-down in the dust. The prayer stops. Shame. I was almost worried."),
        (TauntTrigger.WaveBreak, null, "More! Through the walls! Eat the priest, eat the heroes, eat each other for all I care!", "More! Through the walls! Take the priest, take the heroes, take each other for all I care!"),
        (TauntTrigger.WaveBreak, null, "Another wave, {party}. Keep swinging. Your arms will give out long before my walls do.", "Another wave, {party}. Keep swinging. Your arms will give out long before my walls do."),
        (TauntTrigger.RitualDone, null, "The rite is done. Damn it all. Very well. The stairs are yours. So is what's under them.", "The rite is done. Very well. The stairs are yours. So is what's under them."),
        (TauntTrigger.RitualDone, null, "A prayer against me, finished in my own house. I'll remember that face, priest.", "A prayer against me, finished in my own house. I'll remember that face, priest."),
        (TauntTrigger.ObjectiveDone, null, "Fine. FINE. Go down, then. I've got worse than this, {party}, and I've got all the time in the world.", "Fine. FINE. Go down, then. I've got worse than this, {party}, and I've got all the time in the world."),
        (TauntTrigger.ObjectiveDone, null, "You passed. Don't smile. Nobody who passed my trials ever smiled for long, you smug bastards.", "You passed. Don't smile. Nobody who passed my trials ever smiled for long."),
        (TauntTrigger.Idle, null, "Are you lost? You are. You're standing about like sheep in a pen. Shall I send someone to hurry you along?", "Are you lost? You are. You're standing about like sheep in a pen. Shall I send someone to hurry you along?"),
        (TauntTrigger.Idle, null, "Tick, tock, {party}. I've had guests die of old age on floor {floor}. It was bloody boring to watch.", "Tick, tock, {party}. I've had guests die of old age on floor {floor}. It was boring to watch."),
        (TauntTrigger.MemberDowned, null, "One down! Look at them lying there. Get up, get up, I'm not finished with you!", "One down! Look at them lying there. Get up, get up, I'm not finished with you!"),
        (TauntTrigger.MemberDowned, null, "Down in the dirt. That's where you all end up, {party}. Some of you just take the scenic route.", "Down in the dirt. That's where you all end up, {party}. Some of you just take the scenic route."),
        (TauntTrigger.PartyWiped, null, "All of you. All of you at once. Oh, that was beautiful. Get up and do it again, I've nowhere to be.", "All of you. All of you at once. Oh, that was beautiful. Get up and do it again, I've nowhere to be."),
        (TauntTrigger.PartyWiped, null, "Dead as stones, the lot of you. Well, nearly. Shame the dungeon's so damned patient.", "Dead as stones, the lot of you. Well, nearly. Shame the dungeon's so patient."),
        (TauntTrigger.Descend, null, "Deeper, then. Every step down is a step toward me, and I have such plans for the meeting.", "Deeper, then. Every step down is a step toward me, and I have such plans for the meeting."),
        (TauntTrigger.Descend, null, "Off you go. Mind the stairs. Wouldn't want you dying of something as boring as a fall, you clumsy sods.", "Off you go. Mind the stairs. Wouldn't want you dying of something as boring as a fall."),
        (TauntTrigger.Reinforcements, null, "Taking your time? Then I'll send company. Behind you, {party}. Always behind you.", "Taking your time? Then I'll send company. Behind you, {party}. Always behind you."),
        (TauntTrigger.Reinforcements, null, "Reinforcements. Push deeper or drown in them. I've got a bloody endless supply.", "Reinforcements. Push deeper or drown in them. I've got an endless supply."),
    };

    /// <summary>Every stock line as a plan entry.</summary>
    public static List<TauntPlan> All() => Stock.Select(s => new TauntPlan { Trigger = TauntTriggers.Id(s.Trigger), Objective = s.Kind, Line = new Line(s.Coarse), CleanLine = new Line(s.Clean) }).ToList();

    /// <summary>Add stock lines for every trigger the campaign has none for, and a clean twin to every line without one. Returns how many were added.</summary>
    public static int Fill(CampaignPlan campaign)
    {
        var added = 0;
        var have = new HashSet<TauntTrigger>();
        foreach (var t in campaign.Taunts)
        {
            if (TauntTriggers.TryParse(t.Trigger, out var tt) && t.Line.Text.Trim().Length > 0) have.Add(tt);
            if (t.CleanLine == null || t.CleanLine.Text.Trim().Length == 0) t.CleanLine = new Line(Soften(t.Line.Text));
        }
        foreach (var s in Stock)
        {
            if (have.Contains(s.Trigger)) continue;
            campaign.Taunts.Add(new TauntPlan { Trigger = TauntTriggers.Id(s.Trigger), Objective = s.Kind, Line = new Line(s.Coarse), CleanLine = new Line(s.Clean), FromStock = true });
            added++;
        }
        return added;
    }

    private static readonly string[] Oaths = { "fucking ", "bloody ", "damned ", "goddamned ", "shitting " };
    private static readonly (string From, string To)[] Words = { (" bastards", " wretches"), (" bastard", " wretch"), (" shit", " filth"), (" sods", " fools"), (" sod", " fool"), (" bugger", " bother"), (" damn it all", " so be it"), (" damn", " blast"), (" hell", " the pit"), (" arse", " backside"), (" bollocks", " nonsense"), (" piss", " spit") };

    /// <summary>A clean twin for a coarse line the planner gave none: oaths out, the sense kept.</summary>
    public static string Soften(string text)
    {
        var s = text;
        foreach (var o in Oaths) s = s.Replace(o, "", StringComparison.OrdinalIgnoreCase);
        foreach (var (from, to) in Words) s = System.Text.RegularExpressions.Regex.Replace(s, System.Text.RegularExpressions.Regex.Escape(from) + @"(?=[\s.,!?;:]|$)", to, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s.Replace("  ", " ").Trim();
    }
}
