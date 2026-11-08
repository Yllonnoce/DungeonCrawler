using System.Globalization;
using DungeonCrawler.Combat;

namespace DungeonCrawler.Characters;

/// <summary>
/// Reads an ability upgrade from one line of data: "ability.x[,ability.y]: Key=Value Key=Value", keys DamageMul CooldownMul
/// CostMul BuffMul RadiusAdd ExtraProjectiles OnHitDurationMul AddStatus=Kind,Duration,Magnitude[,Chance] AddEffect=Kind,Duration,Magnitude.
/// </summary>
public static class UpgradeParser
{
    public static AbilityUpgrade Parse(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0) throw new FormatException($"Upgrade '{text}' needs '<abilityId>: Key=Value ...'");
        var u = new AbilityUpgrade { AbilityId = string.Join(",", text[..colon].Split(',').Select(s => s.Trim())) };
        foreach (var token in text[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = token.IndexOf('=');
            if (eq <= 0) throw new FormatException($"Bad upgrade token '{token}' in '{text}'");
            var key = token[..eq]; var value = token[(eq + 1)..];
            u = key switch
            {
                "DamageMul" => u with { DamageMul = F(value) },
                "CooldownMul" => u with { CooldownMul = F(value) },
                "CostMul" => u with { CostMul = F(value) },
                "BuffMul" => u with { BuffMul = F(value) },
                "RadiusAdd" => u with { RadiusAdd = F(value) },
                "ExtraProjectiles" => u with { ExtraProjectiles = (int)F(value) },
                "OnHitDurationMul" => u with { OnHitDurationMul = F(value) },
                "AddStatus" => u with { AddStatus = Status(value, text) },
                "AddEffect" => u with { AddEffect = Status(value, text) },
                _ => throw new FormatException($"Unknown upgrade key '{key}' in '{text}'"),
            };
        }
        return u;
    }

    public static AbilityUpgrade[] ParseAll(IEnumerable<string> lines) => lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(Parse).ToArray();

    private static float F(string v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : throw new FormatException($"Bad number '{v}'");

    private static StatusApplication Status(string value, string text)
    {
        var parts = value.Split(',');
        if (parts.Length is < 3 or > 4 || !Enum.TryParse<StatusKind>(parts[0], true, out var kind)) throw new FormatException($"Bad status '{value}' in '{text}'");
        return new StatusApplication(kind, F(parts[1]), F(parts[2]), parts.Length == 4 ? F(parts[3]) : 1f);
    }
}
