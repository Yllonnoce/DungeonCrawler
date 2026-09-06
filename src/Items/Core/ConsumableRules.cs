using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

public static class ConsumableRules
{
    /// <summary>Apply a consumable to its targets. Returns a one-line summary for the log.</summary>
    public static string Use(ConsumableProfile c, IReadOnlyList<CharacterState> targets)
    {
        var parts = new List<string>();
        foreach (var t in targets)
        {
            if (!t.IsAlive) continue;
            var got = new List<string>();
            if (c.HealHp > 0) { var before = t.Hp; t.Heal(c.HealHp); got.Add($"+{t.Hp - before:0} HP"); }
            if (c.HealMana > 0) { var before = t.Mana; t.Mana = Math.Min(t.Stats[Stat.MaxMana], t.Mana + c.HealMana); got.Add($"+{t.Mana - before:0} mana"); }
            if (c.RestoreStamina > 0) { var before = t.Stamina; t.Stamina = Math.Min(t.Stats[Stat.MaxStamina], t.Stamina + c.RestoreStamina); got.Add($"+{t.Stamina - before:0} stamina"); }
            foreach (var kind in c.Cures)
                if (t.Effects.Remove(kind)) got.Add($"cured {kind}");
            if (c.Buff is { } buff) { t.Effects.Apply(buff); got.Add(buff.Kind.ToString()); }
            t.Recalculate();
            if (got.Count > 0) parts.Add($"{t.Name}: {string.Join(", ", got)}");
        }
        return parts.Count == 0 ? "Nothing happens." : string.Join("; ", parts) + ".";
    }
}
