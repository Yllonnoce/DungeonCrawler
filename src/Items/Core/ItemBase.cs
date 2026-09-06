using DungeonCrawler.Characters;
using DungeonCrawler.Combat;

namespace DungeonCrawler.Items;

/// <summary>What a potion or scroll does when used.</summary>
public sealed record ConsumableProfile
{
    public float HealHp { get; init; }
    public float HealMana { get; init; }
    public float RestoreStamina { get; init; }
    public StatusKind[] Cures { get; init; } = Array.Empty<StatusKind>();
    public StatusApplication? Buff { get; init; }
    public bool TargetsParty { get; init; }
    /// <summary>Trap kind placed in front of the party when used (Spikes, Fire, Gas); empty for none.</summary>
    public string PlaceTrap { get; init; } = "";
    /// <summary>Refunds the target's skill points.</summary>
    public bool Respec { get; init; }
}

/// <summary>Plain-data view of an ItemDef Resource: the template every ItemInstance is stamped from.</summary>
public sealed record ItemBase
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public SlotKind Kind { get; init; } = SlotKind.Junk;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public int ItemLevelMin { get; init; } = 1;
    public bool Stackable { get; init; }
    public int MaxStack { get; init; } = 99;
    public string Color { get; init; } = "#9a9a9a";
    public WeaponProfile? Weapon { get; init; }
    public bool TwoHanded { get; init; }
    public ArmorClass ArmorClass { get; init; }
    public OffhandKind Offhand { get; init; }
    public StatModifier[] BaseModifiers { get; init; } = Array.Empty<StatModifier>();
    public ConsumableProfile? Consumable { get; init; }

    public bool IsEquippable => SlotRules.IsEquippable(Kind);
    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
}

public sealed record AffixTier(int ItemLevelMin, float Min, float Max);

/// <summary>A rollable prefix or suffix. For Mul stats the tier range is in percent (5..10 means +5%..+10%).</summary>
public sealed record AffixProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsPrefix { get; init; }
    /// <summary>The item needs at least one of these tags.</summary>
    public string[] Tags { get; init; } = Array.Empty<string>();
    public float Weight { get; init; } = 1f;
    public Stat Stat { get; init; }
    public ModOp Op { get; init; } = ModOp.Add;
    public AffixTier[] Tiers { get; init; } = Array.Empty<AffixTier>();
    /// <summary>Weapon-only: status applied on hit (e.g. Venomous).</summary>
    public StatusApplication? OnHit { get; init; }

    public bool AppliesTo(ItemBase b) => Tags.Any(b.HasTag);
    public bool AvailableAt(int itemLevel) => Tiers.Any(t => t.ItemLevelMin <= itemLevel) || (Tiers.Length == 0 && OnHit != null);
}

public sealed class RolledAffix
{
    public string AffixId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsPrefix { get; init; }
    public int Tier { get; init; }
    public List<StatModifier> Modifiers { get; init; } = new();
    public StatusApplication? OnHit { get; init; }
}

/// <summary>A named item with fixed powers plus two rolled affixes.</summary>
public sealed record LegendaryProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string BaseId { get; init; } = "";
    public string Flavor { get; init; } = "";
    public int ItemLevelMin { get; init; } = 1;
    public float Weight { get; init; } = 1f;
    public StatModifier[] Modifiers { get; init; } = Array.Empty<StatModifier>();
    public StatusApplication? OnHit { get; init; }
}

/// <summary>Filter syntax: "any", "id:item.x", "tag:weapon", "kind:Body".</summary>
public sealed record LootEntry(string Filter, float Weight)
{
    public bool Matches(ItemBase b)
    {
        var f = Filter.Trim();
        if (f.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;
        var sep = f.IndexOf(':');
        if (sep < 0) return false;
        var key = f[..sep].ToLowerInvariant();
        var val = f[(sep + 1)..];
        return key switch
        {
            "id" => b.Id.Equals(val, StringComparison.OrdinalIgnoreCase),
            "tag" => b.HasTag(val),
            "kind" => b.Kind.ToString().Equals(val, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}

public sealed record LootTableProfile
{
    public string Id { get; init; } = "";
    public LootEntry[] Entries { get; init; } = Array.Empty<LootEntry>();
    /// <summary>Common, Magic, Rare, Legendary.</summary>
    public float[] RarityWeights { get; init; } = { 60, 28, 10, 2 };
    public int Rolls { get; init; } = 1;
    public float DropChance { get; init; } = 1f;
}

/// <summary>Everything the roller needs, in plain data. Built from the Registry on the Godot side, by hand in tests.</summary>
public sealed class ItemCatalog
{
    public Dictionary<string, ItemBase> Bases { get; } = new();
    public Dictionary<string, AffixProfile> Affixes { get; } = new();
    public Dictionary<string, LegendaryProfile> Legendaries { get; } = new();
    public Dictionary<string, LootTableProfile> Tables { get; } = new();

    public ItemBase Base(string id) => Bases.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException($"No item base '{id}'");
    public bool TryBase(string id, out ItemBase b) => Bases.TryGetValue(id, out b!);
    public ItemInstance Create(string baseId, int count = 1, Guid? id = null) => ItemInstance.FromBase(Base(baseId), count, id);
    public LootTableProfile Table(string id) => Tables.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException($"No loot table '{id}'");
}
