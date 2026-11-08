namespace DungeonCrawler.Enemies;

/// <summary>
/// Names for elites from a syllable table, seeded like their affixes. The default register is guttural (Grukk, Morvex, Thrane); a campaign
/// faction of "bandits" draws plain human names instead. One in four carries an honorific. The register is dry: no adjectives, no titles
/// beyond the affix.
/// </summary>
public static class EliteNames
{
    private static readonly string[] First =
    {
        "Gru", "Mor", "Ul", "Thra", "Vor", "Ha", "Kar", "Dro", "Skar", "Bel", "Nar", "Oth",
        "Za", "Gor", "Ru", "Vel", "Mag", "Tor", "Ash", "Kre", "Ol", "Dur", "Fen", "Yar",
    };
    private static readonly string[] Second =
    {
        "kk", "vex", "gar", "ne", "rik", "sk", "mund", "th", "gash", "dun", "zek", "ral",
        "gul", "vash", "nok", "dric", "mar", "ok", "grim", "ven", "tak", "rosh", "lun", "zar",
    };
    private static readonly string[] Human =
    {
        "Hask", "Dunmore", "Pell", "Corrin", "Ashby", "Tamsin", "Wyatt", "Marrow", "Ebb", "Tolly", "Garth", "Isolde",
        "Rook", "Brennan", "Cade", "Odile", "Fenwick", "Maud", "Sayer", "Lowry", "Hollis", "Brannock", "Tess", "Varley",
    };
    private static readonly string[] Honorifics = { "Mother", "Old", "Brother", "Sister", "Deacon", "the Widow", "Warden", "Uncle" };
    private const int HonorificOneIn = 4;

    /// <summary>A titled name: the base name (with an honorific one time in four) and the affix title.</summary>
    public static string Make(Random rng, IReadOnlyList<EliteAffix> affixes, string faction = "")
    {
        var honorific = rng.Next(HonorificOneIn) == 0 ? Honorifics[rng.Next(Honorifics.Length)] : null;
        string baseName;
        if (faction == "bandits") baseName = Human[rng.Next(Human.Length)];
        else
        {
            var a = First[rng.Next(First.Length)]; var b = Second[rng.Next(Second.Length)];
            // a syllable that ends on the vowel the suffix starts with would double it; "Thra" + "ne" reads, "Ha" + "ok" does not
            if (a[^1] == b[0]) b = Second[(Array.IndexOf(Second, b) + 1) % Second.Length];
            baseName = a + b;
        }
        return Title(honorific == null ? baseName : honorific + " " + baseName, affixes);
    }

    /// <summary>The name with its affixes spelled out, added only when the name does not already carry them (a planner's bare name on the HUD).</summary>
    public static string Titled(string name, IReadOnlyList<EliteAffix> affixes)
    {
        if (affixes.Count == 0) return name;
        var tail = affixes.Count == 1 ? $" the {affixes[0]}" : EliteRules.Describe(affixes);
        return name.EndsWith(tail, StringComparison.Ordinal) ? name : Title(name, affixes);
    }

    /// <summary>"Grukk the Swift" for one affix, "Mother Hask, Warded and Vampiric" for more.</summary>
    public static string Title(string name, IReadOnlyList<EliteAffix> affixes)
        => affixes.Count == 0 ? name : affixes.Count == 1 ? $"{name} the {affixes[0]}" : $"{name}, {EliteRules.Describe(affixes)}";
}
