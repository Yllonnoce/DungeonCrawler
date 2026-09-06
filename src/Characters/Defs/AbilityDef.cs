using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Characters.Defs;

/// <summary>Editor-facing ability/spell definition (data/abilities/*.tres).</summary>
[GlobalClass]
public partial class AbilityDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    /// <summary>WeaponAttack, Projectile, MeleeStrike, SelfBuff, PartyBuff, Nova.</summary>
    [Export] public string Kind { get; set; } = "Projectile";

    [ExportGroup("Costs")]
    [Export] public float ManaCost { get; set; }
    [Export] public float StaminaCost { get; set; }
    [Export] public int ArrowCost { get; set; }
    [Export] public float Cooldown { get; set; }

    [ExportGroup("Damage")]
    [Export] public float MinDamage { get; set; }
    [Export] public float MaxDamage { get; set; }
    /// <summary>Physical, Fire, Frost, Poison, Arcane.</summary>
    [Export] public string DamageType { get; set; } = "Physical";
    /// <summary>Melee, Ranged, Spell. Decides which stat adds flat damage.</summary>
    [Export] public string AttackKind { get; set; } = "Spell";
    [Export] public float CritBonus { get; set; }

    [ExportGroup("On hit status")]
    /// <summary>Empty for none; otherwise Bleed, Poison, Burn, Frost, Stun, Slow.</summary>
    [Export] public string OnHitStatus { get; set; } = "";
    [Export] public float OnHitDuration { get; set; }
    [Export] public float OnHitMagnitude { get; set; }
    [Export] public float OnHitChance { get; set; } = 1f;

    [ExportGroup("Buff")]
    /// <summary>Applied to self/party for buff kinds: Regen, Haste, Shield.</summary>
    [Export] public string BuffStatus { get; set; } = "";
    [Export] public float BuffDuration { get; set; }
    [Export] public float BuffMagnitude { get; set; }

    [ExportGroup("Shape")]
    [Export] public float Radius { get; set; } = 6f;
    [Export] public float ProjectileSpeed { get; set; } = 18f;
    [Export] public int ProjectileCount { get; set; } = 1;
    [Export] public Color Color { get; set; } = Colors.White;

    public AbilityProfile ToProfile()
    {
        var kind = Enum.TryParse<AbilityKind>(Kind, true, out var k) ? k : AbilityKind.Projectile;
        AttackProfile? attack = null;
        if (MaxDamage > 0 || OnHitStatus != "")
        {
            var onHit = new List<StatusApplication>();
            if (Enum.TryParse<StatusKind>(OnHitStatus, true, out var sk) && OnHitDuration > 0)
                onHit.Add(new StatusApplication(sk, OnHitDuration, OnHitMagnitude, OnHitChance));
            attack = new AttackProfile
            {
                MinDamage = MinDamage, MaxDamage = MaxDamage,
                Type = Enum.TryParse<DamageType>(DamageType, true, out var dt) ? dt : Combat.DamageType.Physical,
                Kind = Enum.TryParse<AttackKind>(AttackKind, true, out var ak) ? ak : Combat.AttackKind.Spell,
                CritBonus = CritBonus, OnHit = onHit.ToArray(),
            };
        }
        var effects = new List<StatusApplication>();
        if (Enum.TryParse<StatusKind>(BuffStatus, true, out var bk) && BuffDuration > 0)
            effects.Add(new StatusApplication(bk, BuffDuration, BuffMagnitude));
        return new AbilityProfile
        {
            Id = Id, Name = DisplayName, Description = Description, Kind = kind,
            ManaCost = ManaCost, StaminaCost = StaminaCost, ArrowCost = ArrowCost, Cooldown = Cooldown,
            Attack = attack, Effects = effects.ToArray(), Radius = Radius, ProjectileSpeed = ProjectileSpeed, ProjectileCount = Math.Max(1, ProjectileCount),
            Color = Color.ToHtml(),
        };
    }
}
