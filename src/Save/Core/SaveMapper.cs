using System.Globalization;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Items;

namespace DungeonCrawler.Save;

/// <summary>Converts runtime state to DTOs and back. Engine-free; the catalog and class lookup are passed in.</summary>
public static class SaveMapper
{
    // ---- items ---------------------------------------------------------------------------------

    public static ItemDto ToDto(ItemInstance item) => new()
    {
        Id = item.Id, DefId = item.DefId, StackCount = item.StackCount, Rarity = item.Rarity.ToString(),
        ItemLevel = item.ItemLevel, LegendaryName = item.LegendaryName, Flavor = item.Flavor,
        Affixes = item.Affixes.Select(a => new AffixDto
        {
            AffixId = a.AffixId, Name = a.Name, IsPrefix = a.IsPrefix, Tier = a.Tier,
            Modifiers = a.Modifiers.Select(ToDto).ToList(),
            OnHit = a.OnHit is { } oh ? new StatusDto { Kind = oh.Kind.ToString(), Remaining = oh.Duration, Magnitude = oh.Magnitude, Chance = oh.Chance } : null,
        }).ToList(),
    };

    public static ItemInstance FromDto(ItemDto dto, ItemCatalog cat)
    {
        var item = cat.TryBase(dto.DefId, out var b)
            ? ItemInstance.FromBase(b, dto.StackCount, dto.Id)
            : new ItemInstance { Id = dto.Id, DefId = dto.DefId, BaseName = "Lost " + dto.DefId, Kind = SlotKind.Junk, StackCount = dto.StackCount };
        item.Rarity = Enum.TryParse<Rarity>(dto.Rarity, out var r) ? r : Rarity.Common;
        item.ItemLevel = dto.ItemLevel;
        item.LegendaryName = dto.LegendaryName;
        item.Flavor = dto.Flavor;
        foreach (var a in dto.Affixes)
        {
            item.Affixes.Add(new RolledAffix
            {
                AffixId = a.AffixId, Name = a.Name, IsPrefix = a.IsPrefix, Tier = a.Tier,
                Modifiers = a.Modifiers.Select(FromDto).ToList(),
                OnHit = a.OnHit is { } oh && Enum.TryParse<StatusKind>(oh.Kind, out var kind) ? new StatusApplication(kind, oh.Remaining, oh.Magnitude, oh.Chance) : null,
            });
        }
        return item;
    }

    public static ModDto ToDto(StatModifier m) => new() { Stat = m.Stat.ToString(), Op = m.Op.ToString(), Value = m.Value, Source = m.Source };

    public static StatModifier FromDto(ModDto d) => new(
        Enum.TryParse<Stat>(d.Stat, out var s) ? s : Stat.Armor,
        d.Op == "Mul" ? ModOp.Mul : ModOp.Add, d.Value, d.Source);

    // ---- characters ----------------------------------------------------------------------------

    public static CharacterDto ToDto(CharacterState c) => new()
    {
        Name = c.Name, ClassId = c.Class.Id, Level = c.Level, Xp = c.Xp, StatPoints = c.StatPoints, SkillPoints = c.SkillPoints,
        Allocated = c.Allocated.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        Hp = c.Hp, Stamina = c.Stamina, Mana = c.Mana, Rank = c.Rank.ToString(),
        KnownAbilities = c.KnownAbilities.ToList(), LearnedSkills = c.LearnedSkills.ToList(), ActiveAbilityIndex = c.ActiveAbilityIndex,
        Equipment = c.Equipment.Items.ToDictionary(t => t.Slot.ToString(), t => ToDto(t.Item)),
        Effects = c.Effects.Active.Select(e => new StatusDto { Kind = e.Kind.ToString(), Remaining = e.Remaining, Magnitude = e.Magnitude, Stacks = e.Stacks }).ToList(),
        Mangled = c.Mangled, MangledBy = c.MangledBy,
    };

    public static CharacterState FromDto(CharacterDto d, Func<string, ClassProfile?> classes, ItemCatalog cat, SkillCatalog? skills = null)
    {
        var cls = classes(d.ClassId) ?? ClassProfile.Empty;
        var c = new CharacterState { Name = d.Name, Class = cls, Rank = Enum.TryParse<Rank>(d.Rank, out var rank) ? rank : Rank.Front };
        c.Level = d.Level; c.Xp = d.Xp; c.StatPoints = d.StatPoints; c.SkillPoints = d.SkillPoints;
        c.Mangled = d.Mangled; c.MangledBy = d.MangledBy;
        foreach (var (k, v) in d.Allocated) if (Enum.TryParse<Stat>(k, out var st)) c.Allocated[st] = v;

        c.KnownAbilities.Clear();
        c.KnownAbilities.AddRange(d.KnownAbilities.Count > 0 ? d.KnownAbilities : new[] { AbilityProfile.BasicId });
        c.ActiveAbilityIndex = d.ActiveAbilityIndex;

        foreach (var (slotName, itemDto) in d.Equipment)
            if (Enum.TryParse<EquipSlot>(slotName, out var slot)) c.Equipment.Equip(slot, FromDto(itemDto, cat));

        foreach (var e in d.Effects)
        {
            if (!Enum.TryParse<StatusKind>(e.Kind, out var kind)) continue;
            c.Effects.Apply(new StatusApplication(kind, e.Remaining, e.Magnitude));
            if (c.Effects.Get(kind) is { } effect) effect.Stacks = Math.Max(1, e.Stacks);
        }

        foreach (var id in d.LearnedSkills) c.LearnedSkills.Add(id);
        if (skills != null) SkillRules.RebuildSkillEffects(c, skills);
        c.Recalculate();
        c.Hp = d.Hp; c.Stamina = d.Stamina; c.Mana = d.Mana;
        c.ClampResources();
        return c;
    }

    // ---- bag -----------------------------------------------------------------------------------

    public static List<BagCellDto> ToDto(Inventory bag)
    {
        var list = new List<BagCellDto>();
        for (var i = 0; i < bag.Capacity; i++)
            if (bag[i] is { } item) list.Add(new BagCellDto { Index = i, Item = ToDto(item) });
        return list;
    }

    public static void Restore(Inventory bag, List<BagCellDto> cells, ItemCatalog cat)
    {
        for (var i = 0; i < bag.Capacity; i++) bag.Set(i, null);
        foreach (var cell in cells)
        {
            var item = FromDto(cell.Item, cat);
            if (cell.Index >= 0 && cell.Index < bag.Capacity && bag[cell.Index] == null) bag.Set(cell.Index, item);
            else bag.TryAdd(item);
        }
    }

    public static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
}
