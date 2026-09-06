using DungeonCrawler.Characters;

namespace DungeonCrawler.Combat;

public enum StatusKind { Bleed, Poison, Burn, Frost, Stun, Slow, Regen, Haste, Shield }

public sealed class StatusEffect
{
    public StatusKind Kind { get; init; }
    public float Remaining { get; set; }
    public float Magnitude { get; set; }
    public int Stacks { get; set; } = 1;
    public float TickTimer { get; set; }

    public bool IsDebuff => Kind is StatusKind.Bleed or StatusKind.Poison or StatusKind.Burn or StatusKind.Frost or StatusKind.Stun or StatusKind.Slow;
    public override string ToString() => Stacks > 1 ? $"{Kind} x{Stacks} {Remaining:0}s" : $"{Kind} {Remaining:0}s";
}

/// <summary>Damage or healing produced by a periodic effect on one update.</summary>
public readonly record struct StatusTick(StatusKind Kind, float Amount, DamageType Type, bool IsHeal);

/// <summary>
/// Per-combatant status container. Pure C#, driven by <see cref="Update"/> with a delta so tests can use a fake clock.
/// </summary>
public sealed class StatusEffectController
{
    public const float TickInterval = 1f;
    public const int MaxDotStacks = 3;
    public const float MaxShield = 200f;

    private readonly List<StatusEffect> _active = new();

    public IReadOnlyList<StatusEffect> Active => _active;
    /// <summary>Bumps whenever the set of effects (or their modifiers) changes, so owners know to recalculate stats.</summary>
    public int Version { get; private set; }

    public bool IsStunned => _active.Any(e => e.Kind == StatusKind.Stun);
    public float ShieldPoints => _active.Where(e => e.Kind == StatusKind.Shield).Sum(e => e.Magnitude);
    public bool Has(StatusKind kind) => _active.Any(e => e.Kind == kind);
    public StatusEffect? Get(StatusKind kind) => _active.FirstOrDefault(e => e.Kind == kind);

    /// <summary>Apply an effect. Chance below 1 needs an rng; without one the effect always applies.</summary>
    public bool Apply(StatusApplication app, Random? rng = null)
    {
        if (app.Chance < 1f && rng != null && rng.NextDouble() >= app.Chance) return false;
        if (app.Duration <= 0) return false;

        var e = Get(app.Kind);
        if (e == null)
        {
            _active.Add(new StatusEffect { Kind = app.Kind, Remaining = app.Duration, Magnitude = app.Magnitude });
        }
        else
        {
            switch (app.Kind)
            {
                case StatusKind.Bleed or StatusKind.Poison:
                    e.Stacks = Math.Min(MaxDotStacks, e.Stacks + 1);
                    e.Magnitude = Math.Max(e.Magnitude, app.Magnitude);
                    e.Remaining = Math.Max(e.Remaining, app.Duration);
                    break;
                case StatusKind.Shield:
                    e.Magnitude = Math.Min(MaxShield, e.Magnitude + app.Magnitude);
                    e.Remaining = Math.Max(e.Remaining, app.Duration);
                    break;
                default:
                    e.Magnitude = Math.Max(e.Magnitude, app.Magnitude);
                    e.Remaining = Math.Max(e.Remaining, app.Duration);
                    break;
            }
        }
        Version++;
        return true;
    }

    /// <summary>Advance time. Returns the damage/heal ticks that came due; the owner applies them.</summary>
    public List<StatusTick> Update(float dt)
    {
        var ticks = new List<StatusTick>();
        var changed = false;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var e = _active[i];
            var step = Math.Min(dt, e.Remaining);
            e.Remaining -= dt;

            if (IsPeriodic(e.Kind))
            {
                e.TickTimer += step;
                while (e.TickTimer >= TickInterval - 1e-4f)
                {
                    e.TickTimer -= TickInterval;
                    ticks.Add(MakeTick(e));
                }
            }

            if (e.Remaining <= 0)
            {
                _active.RemoveAt(i);
                changed = true;
            }
        }
        if (changed) Version++;
        return ticks;
    }

    private static bool IsPeriodic(StatusKind k) => k is StatusKind.Bleed or StatusKind.Poison or StatusKind.Burn or StatusKind.Regen;

    private static StatusTick MakeTick(StatusEffect e) => e.Kind switch
    {
        StatusKind.Bleed => new(e.Kind, e.Magnitude * e.Stacks, DamageType.Physical, false),
        StatusKind.Poison => new(e.Kind, e.Magnitude * e.Stacks, DamageType.Poison, false),
        StatusKind.Burn => new(e.Kind, e.Magnitude, DamageType.Fire, false),
        StatusKind.Regen => new(e.Kind, e.Magnitude, DamageType.Physical, true),
        _ => throw new InvalidOperationException($"{e.Kind} is not periodic"),
    };

    /// <summary>Stat changes while effects are active. Frost and Slow hamper; Haste speeds up.</summary>
    public IEnumerable<StatModifier> Modifiers()
    {
        foreach (var e in _active)
        {
            switch (e.Kind)
            {
                case StatusKind.Frost:
                    yield return new StatModifier(Stat.MoveSpeed, ModOp.Mul, 0.6f, "Frost");
                    yield return new StatModifier(Stat.AttackSpeed, ModOp.Mul, 0.7f, "Frost");
                    break;
                case StatusKind.Slow:
                    yield return new StatModifier(Stat.MoveSpeed, ModOp.Mul, 0.7f, "Slow");
                    break;
                case StatusKind.Haste:
                    yield return new StatModifier(Stat.AttackSpeed, ModOp.Mul, 1.35f, "Haste");
                    yield return new StatModifier(Stat.MoveSpeed, ModOp.Mul, 1.2f, "Haste");
                    break;
            }
        }
    }

    public float Multiplier(Stat stat)
    {
        var m = 1f;
        foreach (var mod in Modifiers()) if (mod.Stat == stat && mod.Op == ModOp.Mul) m *= mod.Value;
        return m;
    }

    /// <summary>Spend shield points against incoming damage. Returns the damage that gets through.</summary>
    public float AbsorbWithShield(float damage)
    {
        if (damage <= 0) return 0;
        for (var i = _active.Count - 1; i >= 0 && damage > 0; i--)
        {
            var e = _active[i];
            if (e.Kind != StatusKind.Shield) continue;
            var absorbed = Math.Min(e.Magnitude, damage);
            e.Magnitude -= absorbed;
            damage -= absorbed;
            if (e.Magnitude <= 0) _active.RemoveAt(i);
            Version++;
        }
        return damage;
    }

    public bool Remove(StatusKind kind)
    {
        var n = _active.RemoveAll(e => e.Kind == kind);
        if (n > 0) Version++;
        return n > 0;
    }

    public void Clear()
    {
        if (_active.Count == 0) return;
        _active.Clear();
        Version++;
    }
}
