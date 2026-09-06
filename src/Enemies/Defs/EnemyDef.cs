using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Enemies.Defs;

[GlobalClass]
public partial class EnemyDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>Melee, Ranged, Caster.</summary>
    [Export] public string Kind { get; set; } = "Melee";
    [Export] public float MaxHp { get; set; } = 30;
    [Export] public float MinDamage { get; set; } = 6;
    [Export] public float MaxDamage { get; set; } = 10;
    [Export] public string DamageType { get; set; } = "Physical";
    [Export] public float MoveSpeed { get; set; } = 3f;
    [Export] public float DetectRange { get; set; } = 14f;
    [Export] public float AttackRange { get; set; } = 1.9f;
    [Export] public float PreferredRange { get; set; } = 7f;
    [Export] public float WindupSeconds { get; set; } = 0.5f;
    [Export] public float RecoverSeconds { get; set; } = 0.9f;
    [Export] public float ProjectileSpeed { get; set; } = 16f;
    [Export] public int Xp { get; set; } = 25;
    [Export] public float Accuracy { get; set; } = 62;
    [Export] public float Evasion { get; set; } = 8;
    [Export] public float Armor { get; set; } = 5;
    [Export] public float ResistFire { get; set; }
    [Export] public float ResistFrost { get; set; }
    [Export] public float ResistPoison { get; set; }
    [Export] public float ResistArcane { get; set; }
    [Export] public string OnHitStatus { get; set; } = "";
    [Export] public float OnHitDuration { get; set; }
    [Export] public float OnHitMagnitude { get; set; }
    [Export] public float OnHitChance { get; set; } = 1f;
    [Export] public Color Color { get; set; } = new(0.75f, 0.15f, 0.12f);
    [Export] public float Scale { get; set; } = 1f;
    [Export] public bool IsBoss { get; set; }
    /// <summary>None, Slam, Volley, Charge, Summon.</summary>
    [Export] public string Signature { get; set; } = "None";
    [Export] public float SignatureCooldown { get; set; } = 8f;
    [Export] public string SummonId { get; set; } = "";
    /// <summary>Quaternius model name under assets/models/quaternius_monsters (empty = capsule).</summary>
    [Export] public string Model { get; set; } = "";
    /// <summary>"bandits": only placed in bandit camps, on any tier.</summary>
    [Export] public string Faction { get; set; } = "";
    /// <summary>Horde creatures belong to one tier (-1 for any).</summary>
    [Export] public int TierIndex { get; set; } = -1;
    [Export] public bool PackHunter { get; set; }
    [Export] public float FleeHpFraction { get; set; }

    public EnemyProfile ToProfile()
    {
        StatusApplication? onHit = null;
        if (Enum.TryParse<StatusKind>(OnHitStatus, true, out var sk) && OnHitDuration > 0)
            onHit = new StatusApplication(sk, OnHitDuration, OnHitMagnitude, OnHitChance);
        return new EnemyProfile
        {
            Id = Id, Name = DisplayName,
            Kind = Enum.TryParse<EnemyKind>(Kind, true, out var k) ? k : EnemyKind.Melee,
            MaxHp = MaxHp, MinDamage = MinDamage, MaxDamage = MaxDamage,
            DamageType = Enum.TryParse<DamageType>(DamageType, true, out var dt) ? dt : Combat.DamageType.Physical,
            MoveSpeed = MoveSpeed, DetectRange = DetectRange, AttackRange = AttackRange, PreferredRange = PreferredRange,
            WindupSeconds = WindupSeconds, RecoverSeconds = RecoverSeconds, ProjectileSpeed = ProjectileSpeed, Xp = Xp,
            Accuracy = Accuracy, Evasion = Evasion, Armor = Armor,
            ResistFire = ResistFire, ResistFrost = ResistFrost, ResistPoison = ResistPoison, ResistArcane = ResistArcane,
            OnHit = onHit, Color = Color.ToHtml(false), Scale = Scale, IsBoss = IsBoss,
            Signature = Enum.TryParse<Signature>(Signature, true, out var sig) ? sig : Enemies.Signature.None,
            SignatureCooldown = SignatureCooldown, SummonId = SummonId, PackHunter = PackHunter, FleeHpFraction = FleeHpFraction, Model = Model, Faction = Faction, TierIndex = TierIndex,
        };
    }
}
