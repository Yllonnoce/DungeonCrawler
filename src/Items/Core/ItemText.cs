using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>Tooltip text for an item. Engine-free so it can be unit tested and reused by any UI.</summary>
public static class ItemText
{
    public static string Describe(ItemInstance item, ItemInstance? compareWith = null)
    {
        var lines = new List<string> { item.DisplayName, $"{item.Rarity} {KindLabel(item)}   Item level {item.ItemLevel}" };
        if (item.Weapon is { } w)
        {
            lines.Add($"Damage {w.MinDamage:0}-{w.MaxDamage:0} {w.Type.ToString().ToLower()}   Speed {w.Cooldown:0.00}s   Stamina {w.StaminaCost:0}");
            if (item.TwoHanded) lines.Add("Two-handed");
            foreach (var oh in w.OnHit) lines.Add($"{oh.Chance:P0} to apply {oh.Kind} ({oh.Duration:0}s)");
        }
        if (item.ArmorClass != ArmorClass.None) lines.Add($"{item.ArmorClass} armor");
        if (item.Offhand != OffhandKind.None) lines.Add(item.Offhand.ToString());
        foreach (var m in item.BaseModifiers) lines.Add(StatModifierParser.Describe(m));
        foreach (var a in item.Affixes)
        {
            foreach (var m in a.Modifiers) lines.Add($"{StatModifierParser.Describe(m)}  ({a.Name})");
            if (a.OnHit is { } oh && item.Weapon == null) lines.Add($"{oh.Kind} on hit ({a.Name})");
        }
        if (item.Consumable is { } c)
        {
            var effects = new List<string>();
            if (c.HealHp > 0) effects.Add($"heals {c.HealHp:0} HP");
            if (c.HealMana > 0) effects.Add($"restores {c.HealMana:0} mana");
            if (c.RestoreStamina > 0) effects.Add($"restores {c.RestoreStamina:0} stamina");
            if (c.Cures.Length > 0) effects.Add($"cures {string.Join("/", c.Cures)}");
            if (c.Buff is { } b) effects.Add($"{b.Kind} for {b.Duration:0}s");
            lines.Add((c.TargetsParty ? "Party: " : "Use: ") + string.Join(", ", effects));
            lines.Add("Double-click to use");
        }
        if (item.Stackable) lines.Add($"Stack {item.StackCount}/{item.MaxStack}");
        if (item.Flavor != "") lines.Add($"\"{item.Flavor}\"");
        if (item.Description != "" && item.Flavor == "") lines.Add(item.Description);
        if (compareWith != null && compareWith != item)
        {
            lines.Add("");
            lines.Add($"Equipped: {compareWith.DisplayName}");
            foreach (var d in Deltas(item, compareWith)) lines.Add("  " + d);
        }
        return string.Join("\n", lines);
    }

    public static string KindLabel(ItemInstance item) => item.Kind switch
    {
        SlotKind.Weapon when item.BaseWeapon != null => item.BaseWeapon.Kind.ToString(),
        _ => item.Kind.ToString(),
    };

    /// <summary>Net change per stat if <paramref name="item"/> replaced <paramref name="equipped"/>.</summary>
    public static IEnumerable<string> Deltas(ItemInstance item, ItemInstance equipped)
    {
        if (item.Weapon is { } a && equipped.Weapon is { } b)
        {
            var avgA = (a.MinDamage + a.MaxDamage) / 2f;
            var avgB = (b.MinDamage + b.MaxDamage) / 2f;
            if (Math.Abs(avgA - avgB) > 0.01f) yield return $"{(avgA >= avgB ? "+" : "")}{avgA - avgB:0.#} average damage";
        }
        var sums = new Dictionary<(Stat, ModOp), float>();
        foreach (var m in item.Modifiers) sums[(m.Stat, m.Op)] = sums.GetValueOrDefault((m.Stat, m.Op), m.Op == ModOp.Mul ? 1 : 0) + (m.Op == ModOp.Mul ? m.Value - 1 : m.Value);
        foreach (var m in equipped.Modifiers) sums[(m.Stat, m.Op)] = sums.GetValueOrDefault((m.Stat, m.Op), m.Op == ModOp.Mul ? 1 : 0) - (m.Op == ModOp.Mul ? m.Value - 1 : m.Value);
        foreach (var ((stat, op), v) in sums.OrderBy(kv => kv.Key.Item1))
        {
            var delta = op == ModOp.Mul ? v - 1 : v;
            if (Math.Abs(delta) < 0.001f) continue;
            yield return StatModifierParser.Describe(new StatModifier(stat, op, op == ModOp.Mul ? 1 + delta : delta));
        }
    }
}
