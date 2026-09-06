using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Items;

/// <summary>Editor-facing item definition (data/items/*.tres). Converted to a plain ItemBase for the catalog.</summary>
[GlobalClass]
public partial class ItemDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    /// <summary>Weapon, Offhand, Head, Body, Hands, Feet, Ring, Amulet, Consumable, Ammo, Junk.</summary>
    [Export] public string Kind { get; set; } = "Junk";
    [Export] public string[] Tags { get; set; } = Array.Empty<string>();
    [Export] public int ItemLevelMin { get; set; } = 1;
    [Export] public bool Stackable { get; set; }
    [Export] public int MaxStack { get; set; } = 99;
    [Export] public Color Color { get; set; } = new(0.6f, 0.6f, 0.6f);
    /// <summary>Compact modifiers, e.g. "Armor+12", "AttackSpeed*1.1".</summary>
    [Export] public string[] BaseModifiers { get; set; } = Array.Empty<string>();

    [ExportGroup("Weapon")]
    [Export] public string WeaponKind { get; set; } = "";
    [Export] public float MinDamage { get; set; }
    [Export] public float MaxDamage { get; set; }
    [Export] public string DamageType { get; set; } = "Physical";
    [Export] public float Cooldown { get; set; } = 0.8f;
    [Export] public float StaminaCost { get; set; } = 8f;
    [Export] public float HeavyMultiplier { get; set; } = 1.8f;
    [Export] public float Range { get; set; } = 1.8f;
    [Export] public bool TwoHanded { get; set; }
    [Export] public string OnHitStatus { get; set; } = "";
    [Export] public float OnHitDuration { get; set; }
    [Export] public float OnHitMagnitude { get; set; }
    [Export] public float OnHitChance { get; set; } = 1f;

    [ExportGroup("Armor / offhand")]
    /// <summary>None, Cloth, Leather, Plate.</summary>
    [Export] public string ArmorClass { get; set; } = "None";
    /// <summary>None, Shield, Quiver, Focus.</summary>
    [Export] public string Offhand { get; set; } = "None";

    [ExportGroup("Consumable")]
    [Export] public float HealHp { get; set; }
    [Export] public float HealMana { get; set; }
    [Export] public float RestoreStamina { get; set; }
    [Export] public string[] Cures { get; set; } = Array.Empty<string>();
    [Export] public string BuffStatus { get; set; } = "";
    [Export] public float BuffDuration { get; set; }
    [Export] public float BuffMagnitude { get; set; }
    [Export] public bool TargetsParty { get; set; }
    /// <summary>Spikes, Fire, Gas: the consumable places a trap instead of being drunk.</summary>
    [Export] public string PlaceTrap { get; set; } = "";
    [Export] public bool Respec { get; set; }

    public WeaponProfile? ToWeapon()
    {
        if (WeaponKind == "" || !Enum.TryParse<WeaponKind>(WeaponKind, true, out var kind)) return null;
        var onHit = new List<StatusApplication>();
        if (Enum.TryParse<StatusKind>(OnHitStatus, true, out var sk) && OnHitDuration > 0)
            onHit.Add(new StatusApplication(sk, OnHitDuration, OnHitMagnitude, OnHitChance));
        return new WeaponProfile
        {
            Kind = kind, MinDamage = MinDamage, MaxDamage = MaxDamage,
            Type = Enum.TryParse<DamageType>(DamageType, true, out var dt) ? dt : Combat.DamageType.Physical,
            Cooldown = Cooldown, StaminaCost = StaminaCost, HeavyMultiplier = HeavyMultiplier, Range = Range,
            OnHit = onHit.ToArray(),
        };
    }

    public ItemBase ToBase()
    {
        ConsumableProfile? consumable = null;
        var kind = Enum.TryParse<SlotKind>(Kind, true, out var k) ? k : SlotKind.Junk;
        if (kind == SlotKind.Consumable)
        {
            StatusApplication? buff = null;
            if (Enum.TryParse<StatusKind>(BuffStatus, true, out var bk) && BuffDuration > 0) buff = new StatusApplication(bk, BuffDuration, BuffMagnitude);
            consumable = new ConsumableProfile
            {
                HealHp = HealHp, HealMana = HealMana, RestoreStamina = RestoreStamina,
                Cures = Cures.Select(c => Enum.TryParse<StatusKind>(c, true, out var s) ? s : (StatusKind?)null).Where(s => s != null).Select(s => s!.Value).ToArray(),
                Buff = buff, TargetsParty = TargetsParty, PlaceTrap = PlaceTrap, Respec = Respec,
            };
        }
        return new ItemBase
        {
            Id = Id, Name = DisplayName, Description = Description, Kind = kind, Tags = Tags.ToArray(),
            ItemLevelMin = ItemLevelMin, Stackable = Stackable, MaxStack = MaxStack, Color = Color.ToHtml(false),
            Weapon = kind == SlotKind.Weapon ? ToWeapon() : null, TwoHanded = TwoHanded,
            ArmorClass = Enum.TryParse<ArmorClass>(ArmorClass, true, out var ac) ? ac : Items.ArmorClass.None,
            Offhand = Enum.TryParse<OffhandKind>(Offhand, true, out var oh) ? oh : OffhandKind.None,
            BaseModifiers = StatModifierParser.ParseAll(BaseModifiers, DisplayName),
            Consumable = consumable,
        };
    }

    public ItemInstance CreateInstance(int count = 1) => ItemInstance.FromBase(ToBase(), count);
}
