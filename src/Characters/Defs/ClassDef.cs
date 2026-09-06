using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Characters.Defs;

/// <summary>Editor-facing class definition (data/classes/*.tres). Converted to a plain ClassProfile at runtime.</summary>
[GlobalClass]
public partial class ClassDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";

    [ExportGroup("Primaries")]
    [Export] public int Strength { get; set; } = 10;
    [Export] public int Dexterity { get; set; } = 10;
    [Export] public int Vitality { get; set; } = 10;
    [Export] public int Intelligence { get; set; } = 10;
    [Export] public int Willpower { get; set; } = 10;
    [Export] public int Luck { get; set; } = 10;

    [ExportGroup("Growth")]
    [Export] public float HpPerLevel { get; set; } = 8;
    [Export] public float StaminaPerLevel { get; set; } = 5;
    [Export] public float ManaPerLevel { get; set; } = 3;

    [ExportGroup("Kit")]
    [Export] public bool CanBlock { get; set; }
    [Export] public string[] AllowedWeapons { get; set; } = Array.Empty<string>();
    [Export] public string[] StartingAbilities { get; set; } = Array.Empty<string>();
    [Export] public string StartingWeapon { get; set; } = "";
    /// <summary>Cloth, Leather, Plate.</summary>
    [Export] public string[] AllowedArmor { get; set; } = { "Cloth" };
    /// <summary>Shield, Quiver, Focus.</summary>
    [Export] public string[] AllowedOffhands { get; set; } = Array.Empty<string>();
    /// <summary>Item base ids equipped at creation.</summary>
    [Export] public string[] StartingItems { get; set; } = Array.Empty<string>();
    [Export] public float SneakAttackMultiplier { get; set; } = 1.5f;
    [Export] public float StealthBonus { get; set; }
    /// <summary>Added to Perception: how far away this class notices a pressure plate or a loose seam in a wall.</summary>
    [Export] public float PerceptionBonus { get; set; }
    [Export] public float ArmorBonus { get; set; }

    public ClassProfile ToProfile()
    {
        var weapons = AllowedWeapons
            .Select(w => Enum.TryParse<WeaponKind>(w, true, out var k) ? k : (WeaponKind?)null)
            .Where(k => k != null).Select(k => k!.Value).ToArray();
        var mods = new List<StatModifier>();
        if (StealthBonus != 0) mods.Add(new StatModifier(Stat.Stealth, ModOp.Add, StealthBonus, "class"));
        if (PerceptionBonus != 0) mods.Add(new StatModifier(Stat.Perception, ModOp.Add, PerceptionBonus, "class"));
        if (ArmorBonus != 0) mods.Add(new StatModifier(Stat.Armor, ModOp.Add, ArmorBonus, "class"));
        return new ClassProfile
        {
            Id = Id, Name = DisplayName, Description = Description,
            BasePrimaries = new Dictionary<Stat, int>
            {
                [Stat.Strength] = Strength, [Stat.Dexterity] = Dexterity, [Stat.Vitality] = Vitality,
                [Stat.Intelligence] = Intelligence, [Stat.Willpower] = Willpower, [Stat.Luck] = Luck,
            },
            HpPerLevel = HpPerLevel, StaminaPerLevel = StaminaPerLevel, ManaPerLevel = ManaPerLevel,
            CanBlock = CanBlock, AllowedWeapons = weapons, StartingAbilities = StartingAbilities.ToArray(),
            AllowedArmor = AllowedArmor.Select(a => Enum.TryParse<ArmorClass>(a, true, out var ac) ? ac : ArmorClass.None).Where(a => a != ArmorClass.None).ToArray(),
            AllowedOffhands = AllowedOffhands.Select(o => Enum.TryParse<OffhandKind>(o, true, out var ok) ? ok : OffhandKind.None).Where(o => o != OffhandKind.None).ToArray(),
            StartingItems = StartingItems.ToArray(),
            StartingWeaponId = StartingWeapon, SneakAttackMultiplier = SneakAttackMultiplier,
            BaseModifiers = mods.ToArray(),
        };
    }
}
