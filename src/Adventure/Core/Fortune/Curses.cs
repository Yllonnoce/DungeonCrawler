using DungeonCrawler.Characters;
using DungeonCrawler.Items;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>One curse: id, name, the line on the card, and its plain modifiers (the rest are factors read by <see cref="FortuneRules"/>).</summary>
public sealed record CurseDef(string Id, string Name, string Text, string[] Modifiers, bool Transient = false);

/// <summary>The eight curses a shrine can ask, plus <see cref="Scar"/>: a transient one run-structure's wipe penalty applies, never offered by a shrine.</summary>
public static class Curses
{
    public const string Scar = "scar";
    private static readonly string[] None = Array.Empty<string>();

    public static readonly IReadOnlyList<CurseDef> All = new CurseDef[]
    {
        new("hollow", "Hollow", "−20% max HP.", new[] { "MaxHp*0.8" }),
        new("misers_mark", "Miser's Mark", "Prices ×1.4.", None),
        new("loud", "Loud", "Creatures notice you from 30% farther.", None),
        new("brittle", "Brittle", "Traps and hazards hurt ×1.5.", None),
        new("butterfingers", "Butterfingers", "Lockpicks always fail.", None),
        new("dim", "Dim", "−20 Perception.", new[] { "Perception-20" }),
        new("marked", "Marked", "One extra elite on every floor.", None),
        new("thirst", "Thirst", "Potions heal half.", None),
        new(Scar, "Scar", "−10% max HP until the next floor.", new[] { "MaxHp*0.9" }, Transient: true),
    };

    private static readonly Dictionary<string, CurseDef> ById = All.ToDictionary(c => c.Id);

    /// <summary>The eight a shrine may lay or lift (Scar is not one of them).</summary>
    public static IEnumerable<CurseDef> Offerable => All.Where(c => !c.Transient);
    public static CurseDef Get(string id) => ById.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No curse '{id}'");
    public static bool TryGet(string id, out CurseDef def) => ById.TryGetValue(id, out def!);
    public static IEnumerable<StatModifier> Modifiers(IEnumerable<string> ids)
        => ids.Where(ById.ContainsKey).Select(Get).SelectMany(def => StatModifierParser.ParseAll(def.Modifiers, def.Name));
}
