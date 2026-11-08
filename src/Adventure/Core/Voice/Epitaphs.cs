namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// What killed the party, as an id the epitaph bank is keyed by, and how the director infers it from the last things
/// that happened: the newest note still within its cause's validity, else the boss if one is up, else generic. Engine-free.
/// </summary>
public static class WipeCause
{
    public const string Gas = "gas", Press = "press", Spikes = "spikes", Boss = "boss", Horde = "horde", Elite = "elite", Fall = "fall", Mimic = "mimic", Bomber = "bomber", Generic = "generic";
    public static readonly string[] All = { Gas, Press, Spikes, Boss, Horde, Elite, Fall, Mimic, Bomber, Generic };

    /// <summary>How long a note stays a plausible cause: most things 20 s, a horde a minute, an elite half of one, gas two minutes (it stays lethal).</summary>
    public const double RecentSeconds = 20, GasSeconds = 120, HordeSeconds = 60, EliteSeconds = 30;

    public static double Validity(string cause) => cause switch
    {
        Gas => GasSeconds,
        Horde => HordeSeconds,
        Elite => EliteSeconds,
        _ => RecentSeconds,
    };

    /// <summary>A hazard's source name to a cause: "press" in it is the press, "spike" the spikes, anything else generic.</summary>
    public static string FromHazardSource(string sourceName)
    {
        if (sourceName.Contains("press", StringComparison.OrdinalIgnoreCase)) return Press;
        if (sourceName.Contains("spike", StringComparison.OrdinalIgnoreCase)) return Spikes;
        return Generic;
    }

    /// <summary>The newest note whose age is within its cause's validity, else the boss when one is alive, else generic.</summary>
    public static string Infer(IReadOnlyList<(string Cause, double At)> notes, double now, bool bossAlive)
    {
        for (var i = notes.Count - 1; i >= 0; i--)
        {
            var (cause, at) = notes[i];
            if (now - at <= Validity(cause)) return cause;
        }
        return bossAlive ? Boss : Generic;
    }
}

/// <summary>
/// The death card's line: four per cause, escalating with the wipe count on that floor; the fourth carries the count
/// and the floor ("Fourth time on floor 9. He has started using pencil."). Engine-free.
/// </summary>
public static class Epitaphs
{
    public const int Tiers = 4;

    private static readonly Dictionary<string, string[]> Bank = new()
    {
        [WipeCause.Gas]     = new[] { "The air was the first thing to turn on them. It was not the last.", "Twice now the floor has breathed them in. It has not yet breathed them out.", "Third time. The gas knows their names. It has stopped bothering to learn them.", "{N} time on floor {floor}. The lungs gave up before the pride did." },
        [WipeCause.Press]   = new[] { "The Press works. Somebody should tell the next four.", "Pressed again. Thinner. Flatter. No wiser.", "The third time, the stone did not even hurry.", "{N} time on floor {floor}. He has started using pencil." },
        [WipeCause.Spikes]  = new[] { "The floor had opinions. They were sharp.", "Impaled twice. The bed remembers the shape of them.", "Three times onto the spikes. The spikes are getting bored.", "{N} time on floor {floor}. There is a groove where they land." },
        [WipeCause.Boss]    = new[] { "It was bigger. That was the whole of the argument.", "It had a second wind. They did not have a first.", "Three times it has stood over them. It has learned to wait.", "{N} time on floor {floor}. It no longer bothers to roar." },
        [WipeCause.Horde]   = new[] { "Not one of them. All of them. That was rather the point.", "The second swarm was smaller. It did not need to be bigger.", "Three times under the many. The many have not yet run out.", "{N} time on floor {floor}. The teeth come out of the walls in shifts." },
        [WipeCause.Elite]   = new[] { "It had a name. They did not stay long enough to learn it.", "It was named for a reason. Twice, now, the reason.", "Three times to the one with the title. The title is well earned.", "{N} time on floor {floor}. It has stopped introducing itself." },
        [WipeCause.Fall]    = new[] { "The drop was further than it looked. It usually is.", "Fell twice. The bottom was in the same place both times.", "Three times down the same hole. The hole is unimpressed.", "{N} time on floor {floor}. Gravity is keeping count." },
        [WipeCause.Mimic]   = new[] { "The chest was hungry. They were generous.", "Twice bitten. The lid is still smiling.", "Three times a chest has eaten them. They keep opening chests.", "{N} time on floor {floor}. The furniture has them figured out." },
        [WipeCause.Bomber]  = new[] { "It ran at them. It did not stop at them.", "The second one was louder. Or they were closer.", "Three times a small hissing thing has solved them.", "{N} time on floor {floor}. The scorch marks are starting to overlap." },
        [WipeCause.Generic] = new[] { "They fell. The dungeon did not notice.", "Twice. The stone has room for more.", "Three times on this floor. The dark is patient and they are not.", "{N} time on floor {floor}. The dungeon has started a tally." },
    };

    /// <summary>The four lines for a cause; generic for an id the bank does not know.</summary>
    public static IReadOnlyList<string> Lines(string cause) => Bank.TryGetValue(cause, out var lines) ? lines : Bank[WipeCause.Generic];

    /// <summary>The line for this wipe: tier by the wipe count on the floor (clamped to the fourth), tokens filled.</summary>
    public static string Pick(string cause, int wipesOnFloor, int floor)
    {
        var line = Lines(cause)[Math.Clamp(wipesOnFloor, 1, Tiers) - 1];
        var ordinal = Ordinal(Math.Max(1, wipesOnFloor));
        var opener = OrdinalWord(Math.Max(1, wipesOnFloor)); // the sentence opener reads in words as far as twentieth
        return line.Replace("{N}", char.ToUpperInvariant(opener[0]) + opener[1..]).Replace("{n}", ordinal).Replace("{floor}", floor.ToString());
    }

    private static readonly string[] Words = { "", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth" };
    private static readonly string[] Teens = { "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth" };

    private static string OrdinalWord(int n) => n >= 11 && n <= 20 ? Teens[n - 11] : Ordinal(n);

    /// <summary>"first" to "tenth" in words, then "11th", "21st", "22nd", "23rd", "24th".</summary>
    public static string Ordinal(int n)
    {
        if (n >= 1 && n <= 10) return Words[n];
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n + suffix;
    }
}
