namespace DungeonCrawler.Characters;

/// <summary>
/// Fully resolved stat values. Built by <see cref="Compute"/> from a class, level, allocated points and every
/// active modifier, so there is exactly one place where numbers come from.
/// </summary>
public sealed class Stats
{
    private readonly Dictionary<Stat, float> _values;

    private Stats(Dictionary<Stat, float> values) => _values = values;

    public static Stats Empty { get; } = new(new Dictionary<Stat, float>());

    public float this[Stat s] => _values.GetValueOrDefault(s);

    public static Stats FromValues(IDictionary<Stat, float> values) => new(new Dictionary<Stat, float>(values));

    public static Stats Compute(ClassProfile cls, int level, IReadOnlyDictionary<Stat, int>? allocated, IEnumerable<StatModifier>? modifiers)
    {
        var mods = new List<StatModifier>(cls.BaseModifiers);
        if (modifiers != null) mods.AddRange(modifiers);

        var v = new Dictionary<Stat, float>();
        foreach (var p in StatInfo.Primaries)
        {
            float b = cls.BasePrimaries.GetValueOrDefault(p) + (allocated?.GetValueOrDefault(p) ?? 0);
            v[p] = Math.Max(1f, Apply(p, b, mods));
        }

        float str = v[Stat.Strength], dex = v[Stat.Dexterity], vit = v[Stat.Vitality];
        float intel = v[Stat.Intelligence], wil = v[Stat.Willpower], lck = v[Stat.Luck];
        var lv = Math.Max(0, level - 1);

        v[Stat.MaxHp] = 30 + vit * 5 + lv * cls.HpPerLevel;
        v[Stat.MaxStamina] = 50 + vit * 2 + dex + lv * cls.StaminaPerLevel;
        v[Stat.MaxMana] = 10 + intel * 3 + wil * 2 + lv * cls.ManaPerLevel;
        v[Stat.MeleeDamage] = str * 0.6f;
        v[Stat.RangedDamage] = dex * 0.5f;
        v[Stat.SpellPower] = intel * 0.8f;
        v[Stat.Accuracy] = 60 + dex * 2 + lck * 0.5f;
        v[Stat.Evasion] = dex * 1.5f + lv;
        v[Stat.Armor] = 0;
        v[Stat.CritChance] = 0.05f + lck * 0.005f;
        v[Stat.CritMultiplier] = 1.5f + lck * 0.01f;
        v[Stat.AttackSpeed] = 1f;
        v[Stat.MoveSpeed] = 1f;
        v[Stat.HpRegen] = 0.1f + vit * 0.02f;
        v[Stat.StaminaRegen] = 6 + vit * 0.2f + dex * 0.1f;
        v[Stat.ManaRegen] = 0.5f + wil * 0.15f;
        v[Stat.Stealth] = dex * 0.5f;
        v[Stat.Perception] = wil * 0.4f + dex * 0.3f; // noticing plates and seams: a keen will and a steady eye, plus the class bonus
        v[Stat.ResistPhysical] = 0;
        v[Stat.ResistFire] = wil * 0.5f;
        v[Stat.ResistFrost] = wil * 0.5f;
        v[Stat.ResistPoison] = wil * 0.5f;
        v[Stat.ResistArcane] = wil * 0.5f;

        foreach (var s in StatInfo.All)
        {
            if (StatInfo.IsPrimary(s)) continue;
            v[s] = Apply(s, v.GetValueOrDefault(s), mods);
        }

        v[Stat.CritChance] = Math.Clamp(v[Stat.CritChance], 0f, 0.95f);
        v[Stat.MoveSpeed] = Math.Max(0.2f, v[Stat.MoveSpeed]);
        v[Stat.AttackSpeed] = Math.Max(0.2f, v[Stat.AttackSpeed]);
        foreach (var r in new[] { Stat.ResistPhysical, Stat.ResistFire, Stat.ResistFrost, Stat.ResistPoison, Stat.ResistArcane })
            v[r] = Math.Min(75f, v[r]);
        foreach (var m in new[] { Stat.MaxHp, Stat.MaxStamina, Stat.MaxMana })
            v[m] = Math.Max(1f, v[m]);

        return new Stats(v);
    }

    private static float Apply(Stat s, float baseValue, List<StatModifier> mods)
    {
        float add = 0, mul = 1;
        foreach (var m in mods)
        {
            if (m.Stat != s) continue;
            if (m.Op == ModOp.Add) add += m.Value; else mul *= m.Value;
        }
        return (baseValue + add) * mul;
    }

    public IEnumerable<KeyValuePair<Stat, float>> All => _values;
}
