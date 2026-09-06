using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Items.Defs;

/// <summary>Entries are "filter=weight", e.g. "tag:weapon=3", "kind:Ring=1", "any=1".</summary>
[GlobalClass]
public partial class LootTableDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string[] Entries { get; set; } = Array.Empty<string>();
    [Export] public float CommonWeight { get; set; } = 60;
    [Export] public float MagicWeight { get; set; } = 28;
    [Export] public float RareWeight { get; set; } = 10;
    [Export] public float LegendaryWeight { get; set; } = 2;
    [Export] public int Rolls { get; set; } = 1;
    [Export] public float DropChance { get; set; } = 1f;

    public LootTableProfile ToProfile()
    {
        var entries = new List<LootEntry>();
        foreach (var e in Entries)
        {
            var eq = e.LastIndexOf('=');
            if (eq < 0) { entries.Add(new LootEntry(e.Trim(), 1)); continue; }
            var w = float.TryParse(e[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 1f;
            entries.Add(new LootEntry(e[..eq].Trim(), w));
        }
        return new LootTableProfile
        {
            Id = Id, Entries = entries.ToArray(),
            RarityWeights = new[] { CommonWeight, MagicWeight, RareWeight, LegendaryWeight },
            Rolls = Rolls, DropChance = DropChance,
        };
    }
}
