using DungeonCrawler.Combat;

namespace DungeonCrawler.Characters;

public enum Stance { Defensive, Aggressive, Hold }

/// <summary>What a companion knows about one enemy this tick.</summary>
public sealed record EnemyView(int Id, float Distance, bool Hostile, bool Visible, float HpFraction);

/// <summary>One ability the companion could use now. Range is the distance at which it can be used.</summary>
public sealed record AbilityView(int Index, AbilityProfile Ability, bool Ready, float Range);

/// <summary>Snapshot handed to the brain. Built by the Godot layer; kept plain so decisions are unit-testable.</summary>
public sealed record CompanionContext
{
    public float HpFraction { get; init; } = 1f;
    public bool HasHealingPotion { get; init; }
    public Stance Stance { get; init; } = Stance.Defensive;
    public bool PrefersRange { get; init; }
    public float MeleeRange { get; init; } = 1.8f;
    public float EngageRadius { get; init; } = 12f;
    public float RangedDistance { get; init; } = 7f;
    public IReadOnlyList<EnemyView> Enemies { get; init; } = Array.Empty<EnemyView>();
    public IReadOnlyList<AbilityView> Abilities { get; init; } = Array.Empty<AbilityView>();
    public int? LeadTargetId { get; init; }
    public bool PartyMissingShield { get; init; } = true;
    public bool PartyMissingHaste { get; init; } = true;
    public bool PartyWounded { get; init; }
    public bool SelfHasShield { get; init; }
    public float StaminaFraction { get; init; } = 1f;
    public float DistanceToFormation { get; init; }
    public bool FormationReachable { get; init; } = true;
    /// <summary>A party member a hazard mangled and nobody has pulled free yet; -1 when none.</summary>
    public int MangledMemberId { get; init; } = -1;
    public float MangledDistance { get; init; }
    /// <summary>How far the nearest living enemy is; nothing in sight reads as very far.</summary>
    public float NearestEnemyDistance { get; init; } = float.MaxValue;
}

public enum ActionKind { Idle, Follow, Hold, DrinkPotion, Approach, Retreat, Attack, Regroup, Rescue }

public sealed record CompanionAction(ActionKind Kind, int TargetId = -1, int AbilityIndex = -1, bool Heavy = false)
{
    public static readonly CompanionAction Idle = new(ActionKind.Idle);
    public static readonly CompanionAction Follow = new(ActionKind.Follow);
    public static readonly CompanionAction HoldPosition = new(ActionKind.Hold);
    public static readonly CompanionAction Drink = new(ActionKind.DrinkPotion);
}

/// <summary>Priority list: survive, then pull a crushed friend free when nothing is near, then fight what the stance allows, then keep formation.</summary>
public static class CompanionBrain
{
    public const float PotionThreshold = 0.3f;
    public const float PanicThreshold = 0.15f;
    public const float RangedTooClose = 3f;
    /// <summary>No enemy closer than this: calm enough to kneel and pull.</summary>
    public const float RescueCalmDistance = 12f;

    public static CompanionAction Decide(CompanionContext ctx)
    {
        if (ctx.HpFraction < PotionThreshold && ctx.HasHealingPotion) return CompanionAction.Drink;
        if (ctx.MangledMemberId >= 0 && ctx.NearestEnemyDistance > RescueCalmDistance && ctx.Stance != Stance.Hold) return new CompanionAction(ActionKind.Rescue, ctx.MangledMemberId);

        var target = PickTarget(ctx);
        if (target == null)
        {
            if (ctx.Stance == Stance.Hold) return CompanionAction.HoldPosition;
            if (!ctx.FormationReachable) return new CompanionAction(ActionKind.Regroup);
            return ctx.DistanceToFormation > 1.5f ? CompanionAction.Follow : CompanionAction.Idle;
        }

        if (ctx.HpFraction < PanicThreshold && target.Distance < 3f) return new CompanionAction(ActionKind.Retreat, target.Id);
        if (ctx.PrefersRange && target.Distance < RangedTooClose) return new CompanionAction(ActionKind.Retreat, target.Id);

        var ability = ChooseAbility(ctx, target);
        if (ability != null && target.Distance <= ability.Range && (target.Visible || ability.Ability.Kind is AbilityKind.PartyBuff or AbilityKind.SelfBuff))
        {
            var heavy = ability.Ability.Kind == AbilityKind.WeaponAttack && !ctx.PrefersRange && ctx.StaminaFraction > 0.6f;
            return new CompanionAction(ActionKind.Attack, target.Id, ability.Index, heavy);
        }

        var desired = ctx.PrefersRange ? ctx.RangedDistance : ctx.MeleeRange * 0.85f;
        if (target.Distance > desired || !target.Visible) return new CompanionAction(ActionKind.Approach, target.Id);
        return CompanionAction.Idle; // in position, waiting for a cooldown
    }

    /// <summary>Nearest enemy the stance allows; the lead's target counts as closer so the party focuses fire.</summary>
    public static EnemyView? PickTarget(CompanionContext ctx)
    {
        EnemyView? best = null;
        var bestScore = float.MaxValue;
        foreach (var e in ctx.Enemies)
        {
            if (e.HpFraction <= 0) continue;
            var allowed = ctx.Stance switch
            {
                Stance.Aggressive => e.Visible && e.Distance <= ctx.EngageRadius,
                Stance.Hold => e.Distance <= 3f,
                _ => (e.Hostile && e.Distance <= ctx.EngageRadius) || e.Id == ctx.LeadTargetId || e.Distance <= 4f,
            };
            if (!allowed) continue;
            var score = e.Distance - (e.Id == ctx.LeadTargetId ? 3f : 0f) - (e.Visible ? 1f : 0f);
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    public static AbilityView? ChooseAbility(CompanionContext ctx, EnemyView target)
    {
        var ready = ctx.Abilities.Where(a => a.Ready).ToList();
        if (ready.Count == 0) return null;
        var nearby = ctx.Enemies.Count(e => e.HpFraction > 0 && e.Distance <= 6f);

        AbilityView? Find(Func<AbilityView, bool> pred) => ready.FirstOrDefault(pred);
        bool Buffs(AbilityProfile a, StatusKind k) => a.Effects.Any(e => e.Kind == k);

        if (ctx.PartyMissingShield && Find(a => a.Ability.Kind == AbilityKind.PartyBuff && Buffs(a.Ability, StatusKind.Shield)) is { } shield) return shield;
        if (ctx.PartyWounded && Find(a => a.Ability.Kind == AbilityKind.PartyBuff && Buffs(a.Ability, StatusKind.Regen)) is { } regen) return regen;
        if (ctx.PartyMissingHaste && nearby >= 2 && Find(a => a.Ability.Kind == AbilityKind.PartyBuff && Buffs(a.Ability, StatusKind.Haste)) is { } haste) return haste;
        if (!ctx.SelfHasShield && ctx.HpFraction < 0.7f && Find(a => a.Ability.Kind == AbilityKind.SelfBuff && Buffs(a.Ability, StatusKind.Shield)) is { } iron) return iron;
        if (nearby >= 2 && Find(a => a.Ability.Kind == AbilityKind.Nova && a.Ability.Attack != null && target.Distance <= a.Range) is { } nova) return nova;

        var damage = ready.Where(a => a.Ability.Kind is AbilityKind.Projectile or AbilityKind.MeleeStrike && a.Ability.Attack != null && target.Distance <= a.Range)
            .OrderByDescending(a => a.Ability.Attack!.MaxDamage).FirstOrDefault();
        if (damage != null) return damage;

        return Find(a => a.Ability.Kind == AbilityKind.WeaponAttack);
    }
}
