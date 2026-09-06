using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>A concrete item in the world, a bag, or an equipment slot. Plain C# so it serializes.</summary>
public sealed class ItemInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DefId { get; init; } = "";
    public string BaseName { get; init; } = "";
    public string Description { get; init; } = "";
    public SlotKind Kind { get; init; } = SlotKind.Junk;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public bool Stackable { get; init; }
    public int StackCount { get; set; } = 1;
    public int MaxStack { get; init; } = 99;
    public string Color { get; init; } = "#9a9a9a";
    public WeaponProfile? BaseWeapon { get; init; }
    public bool TwoHanded { get; init; }
    public ArmorClass ArmorClass { get; init; }
    public OffhandKind Offhand { get; init; }
    public ConsumableProfile? Consumable { get; init; }
    public List<StatModifier> BaseModifiers { get; init; } = new();

    public Rarity Rarity { get; set; }
    public int ItemLevel { get; set; } = 1;
    public List<RolledAffix> Affixes { get; } = new();
    public string? LegendaryName { get; set; }
    public string Flavor { get; set; } = "";

    public bool IsWeapon => BaseWeapon != null;
    public bool IsEquippable => SlotRules.IsEquippable(Kind);

    /// <summary>Prefix + base + suffix, or the legendary's own name.</summary>
    public string DisplayName
    {
        get
        {
            if (LegendaryName != null) return LegendaryName;
            var prefix = Affixes.FirstOrDefault(a => a.IsPrefix)?.Name;
            var suffix = Affixes.FirstOrDefault(a => !a.IsPrefix)?.Name;
            var name = BaseName;
            if (prefix != null) name = $"{prefix} {name}";
            if (suffix != null) name = $"{name} {suffix}";
            return name;
        }
    }

    /// <summary>All stat modifiers this item grants while equipped.</summary>
    public IEnumerable<StatModifier> Modifiers => BaseModifiers.Concat(Affixes.SelectMany(a => a.Modifiers));

    /// <summary>The weapon profile including on-hit effects added by affixes.</summary>
    public WeaponProfile? Weapon
    {
        get
        {
            if (BaseWeapon == null) return null;
            var extra = Affixes.Where(a => a.OnHit != null).Select(a => a.OnHit!.Value).ToArray();
            return extra.Length == 0 ? BaseWeapon : BaseWeapon with { OnHit = BaseWeapon.OnHit.Concat(extra).ToArray() };
        }
    }

    public static ItemInstance FromBase(ItemBase b, int count = 1, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        DefId = b.Id, BaseName = b.Name, Description = b.Description, Kind = b.Kind, Tags = b.Tags,
        Stackable = b.Stackable, StackCount = Math.Max(1, count), MaxStack = b.MaxStack, Color = b.Color,
        BaseWeapon = b.Weapon, TwoHanded = b.TwoHanded, ArmorClass = b.ArmorClass, Offhand = b.Offhand,
        Consumable = b.Consumable, BaseModifiers = b.BaseModifiers.ToList(), ItemLevel = b.ItemLevelMin,
    };

    /// <summary>Convenience for tests and debug drops.</summary>
    public static ItemInstance Simple(string defId, string name, SlotKind kind = SlotKind.Junk, bool stackable = false, int count = 1, Guid? id = null)
        => new() { Id = id ?? Guid.NewGuid(), DefId = defId, BaseName = name, Kind = kind, Stackable = stackable, StackCount = count };

    public override string ToString() => StackCount > 1 ? $"{DisplayName} x{StackCount}" : DisplayName;
}
