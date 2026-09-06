using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Items.Defs;

/// <summary>Editor-facing affix (data/affixes/*.tres). Tier arrays are parallel: min level, min value, max value.</summary>
[GlobalClass]
public partial class AffixDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public bool IsPrefix { get; set; } = true;
    [Export] public string[] Tags { get; set; } = Array.Empty<string>();
    [Export] public float Weight { get; set; } = 1f;
    [Export] public string Stat { get; set; } = "Armor";
    /// <summary>Add or Mul. Mul tiers are percentages.</summary>
    [Export] public string Op { get; set; } = "Add";
    [Export] public int[] TierMinLevels { get; set; } = { 1 };
    [Export] public float[] TierMins { get; set; } = { 1 };
    [Export] public float[] TierMaxes { get; set; } = { 3 };
    [Export] public string OnHitStatus { get; set; } = "";
    [Export] public float OnHitDuration { get; set; }
    [Export] public float OnHitMagnitude { get; set; }
    [Export] public float OnHitChance { get; set; } = 1f;

    public AffixProfile ToProfile()
    {
        var tiers = new List<AffixTier>();
        for (var i = 0; i < TierMinLevels.Length && i < TierMins.Length && i < TierMaxes.Length; i++)
            tiers.Add(new AffixTier(TierMinLevels[i], TierMins[i], TierMaxes[i]));
        StatusApplication? onHit = null;
        if (Enum.TryParse<StatusKind>(OnHitStatus, true, out var sk) && OnHitDuration > 0)
            onHit = new StatusApplication(sk, OnHitDuration, OnHitMagnitude, OnHitChance);
        return new AffixProfile
        {
            Id = Id, Name = DisplayName, IsPrefix = IsPrefix, Tags = Tags.ToArray(), Weight = Weight,
            Stat = Enum.TryParse<Stat>(Stat, true, out var st) ? st : Characters.Stat.Armor,
            Op = Op.Equals("Mul", StringComparison.OrdinalIgnoreCase) ? ModOp.Mul : ModOp.Add,
            Tiers = tiers.ToArray(), OnHit = onHit,
        };
    }
}
