using DungeonCrawler.Combat;

namespace DungeonCrawler.Characters;

public enum Stance { Defensive, Aggressive, Hold }

/// <summary>
/// What a companion knows about one enemy this tick. <paramref name="RoofBoarder"/>: a hound riding a coupled cage wagon's roof
/// (a melee blow reaches it up to <see cref="CompanionBrain.BoarderReach"/>); <paramref name="BitingCrew"/>: that boarder is biting
/// the crew inside (companions go for it first).
/// </summary>
public sealed record EnemyView(int Id, float Distance, bool Hostile, bool Visible, float HpFraction, bool RoofBoarder = false, bool BitingCrew = false);

/// <summary>One ability the companion could use now. Range is the distance at which it can be used; <paramref name="Melee"/>: a blow of the held weapon (a swing or a melee strike), not a shot or a spell.</summary>
public sealed record AbilityView(int Index, AbilityProfile Ability, bool Ready, float Range, bool Melee = false);

/// <summary>Snapshot handed to the brain. Built by the Godot layer; kept plain so decisions are unit-testable.</summary>
public sealed record CompanionContext
{
    public float HpFraction { get; init; } = 1f;
    public bool HasHealingPotion { get; init; }
    public Stance Stance { get; init; } = Stance.Defensive;
    public bool PrefersRange { get; init; }
    /// <summary>Sitting in a mine cart: it cannot step back, so it never retreats (it fights from the seat instead).</summary>
    public bool Seated { get; init; }
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
    /// <summary>The nearest telegraphed boss signature: its id (-1 when none), seconds until it lands, how far the boss is, and the last id this companion rolled for.</summary>
    public int TelegraphId { get; init; } = -1;
    public float TelegraphRemaining { get; init; } = -1f;
    public float TelegraphDistance { get; init; } = float.MaxValue;
    public int LastTelegraphDecided { get; init; } = -1;
    /// <summary>Whether a dodge can start now (off cooldown, stamina enough).</summary>
    public bool DodgeReady { get; init; }
    /// <summary>The roll against <see cref="CompanionBrain.DodgeChance"/>; below it the companion dodges.</summary>
    public float DodgeRoll { get; init; } = 1f;
}

public enum ActionKind { Idle, Follow, Hold, DrinkPotion, Approach, Retreat, Attack, Regroup, Rescue, Dodge }

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
    /// <summary>Half the time a companion dodges a telegraphed signature, so bosses still hurt.</summary>
    public const float DodgeChance = 0.5f;
    /// <summary>The dodge starts this long before the signature lands; at most the burst's length, so the body is still untouchable when the blow falls.</summary>
    public const float DodgeLead = 0.2f;
    /// <summary>Only a signature this close is worth dodging.</summary>
    public const float DodgeReach = 6f;
    /// <summary>
    /// A melee blow reaches a hound on a coupled cage wagon's roof this far (body to body). Measured on ride 2 of --test-chase,
    /// which prints it: from the cart's rear seats the first wagon's roof hound is 3.38 m away on the level (2.73 m back, 1.98 m
    /// up) and 3.21–3.37 m in the bends; from the front seats 3.79–3.83 m (3.64 m in a bend); a climb or a dip of 10% under the
    /// 2.6 m to the wagon adds about 0.15 m. So every seat reaches the first wagon's roof, while the second wagon's roof, one
    /// wagon spacing (2.6 m) further (about 5.8 m from the rear seats, 6.4 m from the front), stays out of reach. Only roof
    /// boarders get it: a sword still reaches nothing else beside the line that it did not before.
    /// </summary>
    public const float BoarderReach = 3.9f;
    /// <summary>A boarder biting crew, that this companion can strike from where it is, counts as this much nearer: it comes first.</summary>
    public const float CrewBiterPull = 10f;

    /// <summary>True once per telegraph, in its last <see cref="DodgeLead"/> seconds, within reach, for a companion that is not holding.</summary>
    public static bool DodgeDecisionDue(CompanionContext ctx)
        => ctx.TelegraphId >= 0 && ctx.TelegraphId != ctx.LastTelegraphDecided && ctx.TelegraphRemaining >= 0f && ctx.TelegraphRemaining <= DodgeLead
           && ctx.TelegraphDistance <= DodgeReach && ctx.Stance != Stance.Hold;

    public static CompanionAction Decide(CompanionContext ctx)
    {
        if (ctx.HpFraction < PotionThreshold && ctx.HasHealingPotion) return CompanionAction.Drink;
        if (ctx.MangledMemberId >= 0 && ctx.NearestEnemyDistance > RescueCalmDistance && ctx.Stance != Stance.Hold) return new CompanionAction(ActionKind.Rescue, ctx.MangledMemberId);
        if (DodgeDecisionDue(ctx) && ctx.DodgeReady && ctx.DodgeRoll < DodgeChance) return new CompanionAction(ActionKind.Dodge); // a lost roll falls through to the usual fight

        var target = PickTarget(ctx);
        if (target == null)
        {
            if (ctx.Stance == Stance.Hold) return CompanionAction.HoldPosition;
            if (!ctx.FormationReachable) return new CompanionAction(ActionKind.Regroup);
            return ctx.DistanceToFormation > 1.5f ? CompanionAction.Follow : CompanionAction.Idle;
        }

        if (!ctx.Seated) // a seat cannot be backed out of: a seated companion fights at any distance (WhileSeated would only turn the retreat into Hold)
        {
            if (ctx.HpFraction < PanicThreshold && target.Distance < 3f) return new CompanionAction(ActionKind.Retreat, target.Id);
            if (ctx.PrefersRange && target.Distance < RangedTooClose) return new CompanionAction(ActionKind.Retreat, target.Id);
        }

        var ability = ChooseAbility(ctx, target);
        if (ability != null && target.Distance <= RangeFor(ability, target) && (target.Visible || ability.Ability.Kind is AbilityKind.PartyBuff or AbilityKind.SelfBuff))
        {
            var heavy = ability.Ability.Kind == AbilityKind.WeaponAttack && !ctx.PrefersRange && ctx.StaminaFraction > 0.6f;
            return new CompanionAction(ActionKind.Attack, target.Id, ability.Index, heavy);
        }

        var desired = ctx.PrefersRange ? ctx.RangedDistance : (target.RoofBoarder ? Math.Max(ctx.MeleeRange, BoarderReach) : ctx.MeleeRange) * 0.85f; // a roof boarder is struck from below: no closer than that
        if (target.Distance > desired || !target.Visible) return new CompanionAction(ActionKind.Approach, target.Id);
        return CompanionAction.Idle; // in position, waiting for a cooldown
    }

    /// <summary>
    /// A companion seated in a mine cart cannot walk: following, holding, regrouping, closing in, backing off, a rescue and a
    /// dodge all become Hold (keeping the enemy to face, never a member to rescue). An attack (chosen only when the target is
    /// in reach), a potion and idling stand.
    /// </summary>
    public static CompanionAction WhileSeated(CompanionAction a) => a.Kind switch
    {
        ActionKind.Follow or ActionKind.Hold or ActionKind.Regroup or ActionKind.Approach or ActionKind.Retreat => new CompanionAction(ActionKind.Hold, a.TargetId),
        ActionKind.Rescue or ActionKind.Dodge => CompanionAction.HoldPosition,
        _ => a,
    };

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
                Stance.Hold => e.Distance <= 3f || (e.RoofBoarder && e.Distance <= BoarderReach), // what it can strike without a step: a roof boarder above it too
                _ => (e.Hostile && e.Distance <= ctx.EngageRadius) || e.Id == ctx.LeadTargetId || e.Distance <= 4f,
            };
            if (!allowed) continue;
            var score = e.Distance - (e.Id == ctx.LeadTargetId ? 3f : 0f) - (e.Visible ? 1f : 0f) - (e.BitingCrew && CanStrike(ctx, e) ? CrewBiterPull : 0f);
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    /// <summary>How far <paramref name="a"/> reaches <paramref name="target"/>: its own range, and for a melee blow at a roof boarder at least <see cref="BoarderReach"/>.</summary>
    public static float RangeFor(AbilityView a, EnemyView target) => target.RoofBoarder && a.Melee ? Math.Max(a.Range, BoarderReach) : a.Range;

    /// <summary>Some damaging ability of this companion (ready or cooling down) reaches <paramref name="e"/> from where it is, and it sees it.</summary>
    public static bool CanStrike(CompanionContext ctx, EnemyView e)
    {
        if (!e.Visible) return false;
        foreach (var a in ctx.Abilities)
            if (a.Ability.Kind is AbilityKind.WeaponAttack or AbilityKind.MeleeStrike or AbilityKind.Projectile && e.Distance <= RangeFor(a, e)) return true;
        return false;
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

        var damage = ready.Where(a => a.Ability.Kind is AbilityKind.Projectile or AbilityKind.MeleeStrike && a.Ability.Attack != null && target.Distance <= RangeFor(a, target))
            .OrderByDescending(a => a.Ability.Attack!.MaxDamage).FirstOrDefault();
        if (damage != null) return damage;

        return Find(a => a.Ability.Kind == AbilityKind.WeaponAttack);
    }
}
