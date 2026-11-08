using DungeonCrawler.Characters;

namespace DungeonCrawler.Items;

/// <summary>Reads legendary effects off a member's equipment through the catalog: nothing is cached, so unequipping stops the effect.</summary>
public static class LegendaryRules
{
    /// <summary>The legendary an item is, found by its fixed (tier 0) affix; null for an ordinary item.</summary>
    public static LegendaryProfile? Of(ItemInstance item, ItemCatalog cat)
    {
        foreach (var a in item.Affixes)
            if (a.Tier == 0 && cat.Legendaries.TryGetValue(a.AffixId, out var l)) return l;
        return null;
    }

    private static IEnumerable<LegendaryProfile> Worn(Equipment eq, ItemCatalog cat) => eq.Items.Select(t => Of(t.Item, cat)).Where(l => l != null)!;

    /// <summary>The summed magnitude of that trigger over every equipped legendary.</summary>
    public static float Trigger(Equipment eq, ItemCatalog cat, TriggerKind kind)
        => Worn(eq, cat).SelectMany(l => l.Triggers).Where(t => t.Kind == kind).Sum(t => t.Magnitude);

    public static IEnumerable<AbilityUpgrade> Upgrades(Equipment eq, ItemCatalog cat) => Worn(eq, cat).SelectMany(l => l.Upgrades);

    /// <summary>What a trigger does, for a tooltip: "Kills heal 8", "+25% gold found" ...</summary>
    public static string EffectText(TriggerKind kind, float m) => kind switch
    {
        TriggerKind.Riposte => $"After a block, counter for {m:P0} weapon damage",
        TriggerKind.VenomCrit => $"Crits poison ({m:0.#} a tick)",
        TriggerKind.Scavenger => $"Bow kills recover {m:0} arrow{(m == 1 ? "" : "s")}",
        TriggerKind.SoulSiphon => $"Kills restore {m:0} mana",
        TriggerKind.Bloodlust => $"Kills heal {m:0}",
        TriggerKind.Thorns => $"Attackers take {m:0} damage back",
        TriggerKind.SecondWind => $"Blocking restores {m:0} stamina",
        TriggerKind.Momentum => $"Kills grant Haste for {m * 3:0} s",
        TriggerKind.HeavyStun => $"Heavy blows stun for {m:0.#} s; Shatter stuns too",
        TriggerKind.GoldFind => $"+{m:P0} gold found",
        TriggerKind.SellBonus => $"The merchant pays {m:P0} more",
        TriggerKind.LastGasp => "Below 30% HP, once a floor: frost bursts from you",
        TriggerKind.LuckyDip => $"The Lucky Dip costs {m:P0} less",
        TriggerKind.PerfectCleave => $"Perfect blows strike every enemy in the arc for {m:P0} weapon damage",
        TriggerKind.FrostSneak => $"Sneak attacks on a Frosted target deal x{1 + m:0.##}",
        TriggerKind.QuietKill => "Your kills never wake kin",
        TriggerKind.ParryWindow => $"Parry window +{m:0.##} s",
        TriggerKind.IronChin => $"Stagger immunity for {m:0.#} s after getting up",
        TriggerKind.BurnSpread => $"Burn you apply spreads to {m:0} neighbour{(m == 1 ? "" : "s")}",
        TriggerKind.NoGuard => "You cannot guard",
        TriggerKind.ArrowShatter => "Arrows Shatter a Frosted target on any hit",
        _ => kind.ToString(),
    };

    /// <summary>What an upgrade does, for a tooltip: "Mend also applies Shield 15, cooldown x0.8".</summary>
    public static string UpgradeText(AbilityUpgrade u)
    {
        static string Name(string id) { var tail = id.Trim().Split('.').Last().Replace('_', ' '); return tail.Length == 0 ? id : char.ToUpperInvariant(tail[0]) + tail[1..]; }
        var names = string.Join(" and ", u.AbilityId.Split(',').Select(Name));
        var parts = new List<string>();
        if (u.DamageMul != 1) parts.Add($"damage x{u.DamageMul:0.##}");
        if (u.CooldownMul != 1) parts.Add($"cooldown x{u.CooldownMul:0.##}");
        if (u.CostMul != 1) parts.Add($"cost x{u.CostMul:0.##}");
        if (u.BuffMul != 1) parts.Add($"effect x{u.BuffMul:0.##}");
        if (u.RadiusAdd != 0) parts.Add($"radius +{u.RadiusAdd:0.#} m");
        if (u.ExtraProjectiles != 0) parts.Add($"+{u.ExtraProjectiles} projectiles");
        if (u.AddStatus is { } st) parts.Add($"adds {st.Kind}");
        if (u.OnHitDurationMul != 1) parts.Add($"status duration x{u.OnHitDurationMul:0.##}");
        if (u.AddEffect is { } ef) parts.Add($"also applies {ef.Kind} {ef.Magnitude:0}");
        return $"{names} {string.Join(", ", parts)}";
    }

    /// <summary>Coins picked up with a gold-find bonus, rounded to the nearest coin.</summary>
    public static float GoldFound(int coins, float bonus) => (int)Math.Round(coins * (1 + bonus), MidpointRounding.AwayFromZero);
}
