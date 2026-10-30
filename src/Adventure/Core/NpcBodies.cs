using System.Text.RegularExpressions;
using DungeonCrawler.Core;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Which of the owner's viewer-made bodies a person wears, read off who they are: a prisoner is always the prisoner,
/// an alderman is the merchant, a deacon the priest, a soldier the guard, and everyone else one of the four plain
/// bodies by the age and sex their name and description give away. Engine-free; the builder checks the file exists.
/// </summary>
public static class NpcBodies
{
    public const string Dir = "res://assets/models/viewer/";
    public static readonly string[] Archetypes =
    {
        "npc_old_man", "npc_old_woman", "npc_young_man", "npc_young_woman", "npc_merchant", "npc_guard", "npc_priest", "npc_prisoner",
    };

    public static string Path(string key) => $"{Dir}{key}.glb";

    /// <summary>The body for a floor's person. Never null: with nothing to go on, one of the plain bodies, the same one every time for that id.</summary>
    public static string For(string id, string name, bool prisoner, params string?[] descriptions)
        => prisoner ? "npc_prisoner" : Guess(name, descriptions) ?? Fallback(id);

    /// <summary>The body the words point at, or null when they point at nothing.</summary>
    public static string? Guess(string name, params string?[] descriptions)
    {
        var text = Normalise(string.Join(" ", new[] { name }.Concat(descriptions ?? Array.Empty<string?>()).Where(s => !string.IsNullOrWhiteSpace(s))));
        foreach (var (body, words) in Roles) if (Any(text, words)) return body;
        var female = Any(text, Female);
        var male = Any(text, Male);
        var old = Any(text, Old);
        var young = Any(text, Young);
        if (!female && !male && !old && !young) return null;
        var woman = female && !male; // a man described by his wife stays a man
        return old && !young ? (woman ? "npc_old_woman" : "npc_old_man") : woman ? "npc_young_woman" : "npc_young_man";
    }

    /// <summary>A villain who has a body of their own; null keeps the boss's creature model.</summary>
    public static string? Villain(string name, string? epithet, string? style)
        => Any(Normalise($"{name} {epithet} {style}"), new[] { "verger" }) ? "verger" : null;

    private static string Fallback(string id) => Plain[(int)(Rng.StableHash(id) % (ulong)Plain.Length)];
    private static readonly string[] Plain = { "npc_young_man", "npc_young_woman", "npc_old_man" };

    private static readonly (string Body, string[] Words)[] Roles =
    {
        ("npc_prisoner", new[] { "prisoner", "captive", "convict", "shackle", "shackled", "chained", "jailed", "survivor", "underground", "ragged", "starved", "gaolbird" }),
        ("npc_merchant", new[] { "alderman", "merchant", "trader", "mayor", "burgher", "banker", "moneylender", "pedlar", "peddler", "shopkeeper", "innkeeper", "chain of office", "invoice", "coin purse", "plump", "well fed", "portly", "factor" }),
        ("npc_priest", new[] { "deacon", "priest", "father", "brother", "monk", "friar", "curate", "chaplain", "cleric", "acolyte", "abbot", "prior", "vicar", "canon", "bishop", "preacher", "sexton" }),
        ("npc_guard", new[] { "guard", "soldier", "sergeant", "watchman", "sentry", "retainer", "mercenary", "sellsword", "captain", "man at arms", "constable", "bailiff", "warden", "corporal", "veteran" }),
    };
    private static readonly string[] Female = { "woman", "women", "girl", "she", "her", "hers", "lass", "maid", "maiden", "daughter", "wife", "mother", "sister", "widow", "nun", "matron", "crone", "grandmother", "lady", "mistress", "dame", "goodwife", "aunt" };
    private static readonly string[] Male = { "man", "men", "boy", "he", "him", "his", "lad", "son", "husband", "widower", "grandfather", "fellow", "gent", "goodman", "beard", "bearded", "uncle", "moustache" };
    private static readonly string[] Old = { "old", "elderly", "aged", "ancient", "grey", "gray", "white haired", "wizened", "stooped", "crone", "mother", "grandmother", "grandfather", "widow", "widower", "seventy", "eighty", "ninety", "grizzled", "wheezing" };
    private static readonly string[] Young = { "young", "boy", "girl", "lad", "lass", "youth", "apprentice", "novice", "child", "maiden", "twenty", "eager", "student", "junior" };

    private static string Normalise(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z]+", " ");
    private static bool Any(string text, string[] words)
    {
        foreach (var w in words) if (Regex.IsMatch(text, $@"\b{Regex.Escape(w)}\b")) return true;
        return false;
    }
}
