using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Characters.Defs;

/// <summary>Editor-facing skill (data/skills/*.tres).</summary>
[GlobalClass]
public partial class SkillDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public string ClassId { get; set; } = "";
    [Export] public string Branch { get; set; } = "";
    [Export] public int Tier { get; set; }
    [Export] public string[] Prerequisites { get; set; } = Array.Empty<string>();
    [Export] public int Cost { get; set; } = 1;
    /// <summary>"Armor+6", "AttackSpeed*1.1" ...</summary>
    [Export] public string[] Modifiers { get; set; } = Array.Empty<string>();
    [Export] public string GrantsAbility { get; set; } = "";

    [ExportGroup("Upgrade")]
    [Export] public string UpgradeAbility { get; set; } = "";
    [Export] public float DamageMul { get; set; } = 1f;
    [Export] public float CooldownMul { get; set; } = 1f;
    [Export] public float CostMul { get; set; } = 1f;
    [Export] public float BuffMul { get; set; } = 1f;
    [Export] public float RadiusAdd { get; set; }
    [Export] public int ExtraProjectiles { get; set; }
    [Export] public string AddStatus { get; set; } = "";
    [Export] public float AddStatusDuration { get; set; }
    [Export] public float AddStatusMagnitude { get; set; }
    [Export] public float AddStatusChance { get; set; } = 1f;

    [ExportGroup("Trigger")]
    /// <summary>Riposte, VenomCrit, Scavenger, SoulSiphon, Bloodlust, Thorns, SecondWind, Momentum.</summary>
    [Export] public string Trigger { get; set; } = "None";
    [Export] public float TriggerMagnitude { get; set; }

    public SkillProfile ToProfile()
    {
        AbilityUpgrade? upgrade = null;
        if (UpgradeAbility != "")
        {
            StatusApplication? add = null;
            if (Enum.TryParse<StatusKind>(AddStatus, true, out var sk) && AddStatusDuration > 0) add = new StatusApplication(sk, AddStatusDuration, AddStatusMagnitude, AddStatusChance);
            upgrade = new AbilityUpgrade { AbilityId = UpgradeAbility, DamageMul = DamageMul, CooldownMul = CooldownMul, CostMul = CostMul, BuffMul = BuffMul, RadiusAdd = RadiusAdd, ExtraProjectiles = ExtraProjectiles, AddStatus = add };
        }
        return new SkillProfile
        {
            Id = Id, Name = DisplayName, Description = Description, ClassId = ClassId, Branch = Branch, Tier = Tier,
            Prerequisites = Prerequisites.ToArray(), Cost = Cost, Modifiers = StatModifierParser.ParseAll(Modifiers, DisplayName),
            GrantsAbility = GrantsAbility, Upgrade = upgrade,
            Trigger = Enum.TryParse<TriggerKind>(Trigger, true, out var t) ? t : TriggerKind.None, TriggerMagnitude = TriggerMagnitude,
        };
    }
}
