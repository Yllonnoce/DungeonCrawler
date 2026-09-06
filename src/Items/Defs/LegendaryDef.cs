using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Items.Defs;

[GlobalClass]
public partial class LegendaryDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public string BaseId { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Flavor { get; set; } = "";
    [Export] public int ItemLevelMin { get; set; } = 1;
    [Export] public float Weight { get; set; } = 1f;
    [Export] public string[] Modifiers { get; set; } = Array.Empty<string>();
    [Export] public string OnHitStatus { get; set; } = "";
    [Export] public float OnHitDuration { get; set; }
    [Export] public float OnHitMagnitude { get; set; }
    [Export] public float OnHitChance { get; set; } = 1f;

    public LegendaryProfile ToProfile()
    {
        StatusApplication? onHit = null;
        if (Enum.TryParse<StatusKind>(OnHitStatus, true, out var sk) && OnHitDuration > 0)
            onHit = new StatusApplication(sk, OnHitDuration, OnHitMagnitude, OnHitChance);
        return new LegendaryProfile
        {
            Id = Id, Name = DisplayName, BaseId = BaseId, Flavor = Flavor, ItemLevelMin = ItemLevelMin, Weight = Weight,
            Modifiers = StatModifierParser.ParseAll(Modifiers, DisplayName), OnHit = onHit,
        };
    }
}
