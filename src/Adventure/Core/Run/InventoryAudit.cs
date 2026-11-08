using DungeonCrawler.Characters;
using DungeonCrawler.Items;

namespace DungeonCrawler.Adventure.Run;

/// <summary>
/// The Inventory Audit clause at a tier gate: on each member, the equipped item with the lowest item level (ties by
/// slot order) that carries at least one rolled affix loses its last one. A legendary's own line (tier 0) is never
/// taken; a member with nothing to take is skipped. Engine-free.
/// </summary>
public static class InventoryAudit
{
    /// <summary>Applies the audit and returns one line per member it touched ("Brand's Rusty Plate loses of the Bear.").</summary>
    public static List<string> Apply(IEnumerable<CharacterState> members)
    {
        var lines = new List<string>();
        foreach (var m in members)
        {
            var target = m.Equipment.Items
                .Where(t => t.Item.Affixes.Any(a => a.Tier > 0))
                .OrderBy(t => t.Item.ItemLevel).ThenBy(t => (int)t.Slot)
                .Select(t => t.Item).FirstOrDefault();
            if (target == null) continue;
            var affix = target.Affixes.Last(a => a.Tier > 0);
            var name = target.DisplayName;
            target.Affixes.Remove(affix);
            m.Recalculate();
            lines.Add($"{m.Name}'s {name} loses {affix.Name}.");
        }
        return lines;
    }
}
