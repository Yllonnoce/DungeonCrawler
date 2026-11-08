using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// What equipped legendaries do to the party: the firing sites this file owns (Burn spread on a hit, Gerald below 30% HP, smart loot),
/// and the readable hooks other systems poll (guarding, parry window, stagger immunity, heavy stuns, perfect-blow cleave, sneak bonus,
/// quiet kills, arrow Shatter, Lucky Dip discount, gold find, sell bonus). Every hook is one line over <see cref="Trigger"/>.
/// </summary>
public partial class PartyController
{
    private readonly Dictionary<int, ulong> _gotUpAt = new();
    private readonly HashSet<int> _lastGaspUsed = new();

    /// <summary>Called once from _Ready: smart loot reads the living classes, and the item triggers subscribe to their events.</summary>
    private void HookItemTriggers()
    {
        Game.Instance.Catalog.SmartLootClasses = () => Members.Where(m => m.IsAlive).Select(m => m.Class);
        EventBus.Instance.MemberDamaged += OnItemMemberDamaged;
        EventBus.Instance.MemberRevived += i => { if (IsInsideTree()) _gotUpAt[i] = Time.GetTicksMsec(); };
        EventBus.Instance.FloorChanged += (_, _) => _lastGaspUsed.Clear();
    }

    /// <summary>Candle of the Congregation: a Burn one of ours applies leaps to the nearest neighbours within 3 m.</summary>
    private void OnItemHit(CharacterState m, ICombatTarget target, DamageResult r)
    {
        if (!IsInsideTree() || Trigger(m, TriggerKind.BurnSpread) is not (> 0 and var n) || target is not Enemy e) return;
        if (r.Applied.FirstOrDefault(s => s.Kind == StatusKind.Burn) is not { Kind: StatusKind.Burn } burn) return;
        if (Main.Instance.Builder.FloorRoot is not { } root) return;
        var others = root.GetChildren().OfType<Enemy>()
            .Where(o => o != e && o.IsAlive && o.GlobalPosition.DistanceTo(e.GlobalPosition) <= 3f)
            .OrderBy(o => o.GlobalPosition.DistanceTo(e.GlobalPosition)).Take((int)n);
        foreach (var other in others)
        {
            other.ApplyStatus(burn with { Chance = 1f });
            EventBus.Instance.Log($"{m.Name}'s flame leaps to {other.DisplayName}.");
        }
    }

    /// <summary>Gerald: below 30% HP, once per floor, a Frost Nova bursts from the wearer. Server and solo only.</summary>
    private void OnItemMemberDamaged(int index, float amount)
    {
        if (!IsInsideTree() || Net.NetSession.IsClient || index < 0 || index >= Members.Length) return;
        var m = Members[index];
        if (Trigger(m, TriggerKind.LastGasp) <= 0 || !m.IsAlive || m.Hp >= 0.3f * m.Stats[Stat.MaxHp] || _lastGaspUsed.Contains(index)) return;
        if (index >= Adventurers.Length || Main.Instance.Builder.FloorRoot is not { } root) return;
        var a = GetAbilityFor(m, "ability.frost_nova");
        if (a.Attack == null) return;
        _lastGaspUsed.Add(index);
        EventBus.Instance.Log($"Gerald remembers being bigger: frost bursts from {m.Name}.");
        var origin = Adventurers[index].GlobalPosition;
        foreach (var child in root.GetChildren())
        {
            if (child is not Enemy { IsAlive: true } e || e.GlobalPosition.DistanceTo(origin) > a.Radius) continue;
            var ctx = new HitContext { SneakAttack = e.IsUnaware, SneakMultiplier = m.Class.SneakAttackMultiplier };
            var r = e.ReceiveAttack(m.Stats, a.Attack, ctx, Rng, m.Name);
            LogResult(m.Name, e.DisplayName, r, false, false);
            OnHitLanded(m, e, r);
        }
    }

    // ---- hooks other systems read ----

    /// <summary>Red Mist takes the guard away; the class must be able to block in the first place.</summary>
    public bool CanGuard(CharacterState m) => m.Class.CanBlock && Trigger(m, TriggerKind.NoGuard) <= 0;
    /// <summary>Sister's Thimble: seconds added to the parry window.</summary>
    public float ParryWindowBonus(CharacterState m) => Trigger(m, TriggerKind.ParryWindow);
    /// <summary>Old Ulf's Chin: true within the IronChin seconds after the member got back up.</summary>
    public bool StaggerImmune(CharacterState m)
    {
        var index = Array.IndexOf(Members, m);
        if (index < 0 || !_gotUpAt.TryGetValue(index, out var at)) return false;
        var seconds = Trigger(m, TriggerKind.IronChin);
        return seconds > 0 && (Time.GetTicksMsec() - at) / 1000f <= seconds;
    }
    /// <summary>Gorm's Molar: seconds a heavy blow stuns for (0: none).</summary>
    public float HeavyStunSeconds(CharacterState m) => Trigger(m, TriggerKind.HeavyStun);
    /// <summary>Gorm's Molar: Shatter stuns the same.</summary>
    public float ShatterStunSeconds(CharacterState m) => HeavyStunSeconds(m);
    /// <summary>Levy Steel: the fraction of weapon damage a perfect blow deals to every other enemy in the arc (0: none).</summary>
    public float PerfectBlowCleave(CharacterState m) => Trigger(m, TriggerKind.PerfectCleave);
    /// <summary>Hush: the factor on a sneak attack against a Frosted target (1 otherwise).</summary>
    public float SneakBonus(CharacterState m, ICombatTarget target)
        => target is Enemy { } e && e.Effects.Has(StatusKind.Frost) ? 1f + Trigger(m, TriggerKind.FrostSneak) : 1f;
    /// <summary>Hush: this member's kills never wake kin.</summary>
    public bool QuietKills(CharacterState m) => Trigger(m, TriggerKind.QuietKill) > 0;
    /// <summary>Alpha: arrows Shatter a Frosted target on any hit.</summary>
    public bool ArrowsShatter(CharacterState m) => Trigger(m, TriggerKind.ArrowShatter) > 0;
    /// <summary>Pellet's Receipt: the best Lucky Dip discount a living member carries (0.3 = 30% off).</summary>
    public float LuckyDipDiscount() => Members.Where(m => m.IsAlive).Select(m => Trigger(m, TriggerKind.LuckyDip)).DefaultIfEmpty(0).Max();
    /// <summary>The Alderman's Seal: the best gold-find bonus a living member carries.</summary>
    public float GoldFindBonus() => Members.Where(m => m.IsAlive).Select(m => Trigger(m, TriggerKind.GoldFind)).DefaultIfEmpty(0).Max();
    /// <summary>The Alderman's Seal: the best sell bonus a living member carries.</summary>
    public float SellBonus() => Members.Where(m => m.IsAlive).Select(m => Trigger(m, TriggerKind.SellBonus)).DefaultIfEmpty(0).Max();
}
