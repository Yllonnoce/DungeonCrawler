using DungeonCrawler.Characters;
using System.Globalization;

namespace DungeonCrawler.Items;

/// <summary>Parses compact modifier strings used in data files: "Armor+12" (add), "AttackSpeed*1.15" (multiply).</summary>
public static class StatModifierParser
{
    public static StatModifier Parse(string text, string source = "")
    {
        var t = text.Trim();
        var idx = t.IndexOfAny(new[] { '+', '-', '*' });
        if (idx <= 0) throw new FormatException($"Bad modifier '{text}'");
        var statName = t[..idx].Trim();
        var opChar = t[idx];
        var valueText = opChar == '*' ? t[(idx + 1)..] : t[idx..];
        if (!Enum.TryParse<Stat>(statName, true, out var stat)) throw new FormatException($"Unknown stat '{statName}' in '{text}'");
        var value = float.Parse(valueText, CultureInfo.InvariantCulture);
        return new StatModifier(stat, opChar == '*' ? ModOp.Mul : ModOp.Add, value, source);
    }

    public static StatModifier[] ParseAll(IEnumerable<string> lines, string source = "")
        => lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => Parse(l, source)).ToArray();

    /// <summary>Human text for a modifier, e.g. "+12 Armor" or "+15% Attack speed".</summary>
    public static string Describe(StatModifier m)
    {
        var name = StatInfo.IsPrimary(m.Stat) ? m.Stat.ToString() : Humanize(m.Stat);
        if (m.Op == ModOp.Mul)
        {
            var pct = (m.Value - 1f) * 100f;
            return $"{(pct >= 0 ? "+" : "")}{pct:0}% {name}";
        }
        if (StatInfo.IsPercent(m.Stat))
        {
            var v = m.Stat == Stat.CritChance ? m.Value * 100f : m.Value;
            return $"{(v >= 0 ? "+" : "")}{v:0.#}% {name}";
        }
        return $"{(m.Value >= 0 ? "+" : "")}{m.Value:0.#} {name}";
    }

    private static string Humanize(Stat s)
    {
        var text = s.ToString();
        var sb = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
            sb.Append(sb.Length == 0 ? c : char.ToLowerInvariant(c));
        }
        return sb.ToString().Replace("Max hp", "Max HP").Replace("Hp regen", "HP regen");
    }
}
