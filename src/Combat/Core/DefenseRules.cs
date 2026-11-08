namespace DungeonCrawler.Combat;

/// <summary>The Depths' defensive verbs, in numbers: dodge, parry, perfect block, the ordinary block's cost, the stagger they earn.</summary>
public static class DefenseRules
{
    public const float DodgeSeconds = 0.25f, DodgeStamina = 20f, DodgeCooldown = 0.6f;
    /// <summary>(tune) metres per second for the burst: 2.75 m.</summary>
    public const float DodgeSpeed = 11f;
    public const float ParrySeconds = 0.25f, ParryStamina = 10f, ParryCooldown = 0.8f;
    public const float PerfectBlockSeconds = 0.2f;
    /// <summary>The guard must have been down this long before raising it opens another perfect window: tapping the guard is not free.</summary>
    public const float PerfectBlockRearmSeconds = 0.5f;
    /// <summary>An ordinary block's stamina; <c>PartyController.BlockStaminaCost</c> reads this.</summary>
    public const float BlockStamina = 12f;
    public const float StaggerSeconds = 1.2f;
    /// <summary>(tune) how long the server keeps a remote window open after the flag drops.</summary>
    public const float RemoteGraceSeconds = 0.1f;
}

public enum DefenseOutcome { None, Dodged, Parried, PerfectBlock, Blocked }

/// <summary>One body's defensive windows. Engine-free: driven by <see cref="Update"/> locally, by <see cref="ApplyRemote"/> on the server for a remote player.</summary>
public sealed class DefenseState
{
    public float DodgeLeft { get; private set; }
    public float DodgeCooldownLeft { get; private set; }
    public float ParryLeft { get; private set; }
    public float ParryCooldownLeft { get; private set; }
    /// <summary>Seconds the guard has been held; -1 when not guarding.</summary>
    public float GuardHeld { get; private set; } = -1f;

    // Remote flags: the edge opens a window, never the level. A rise the server refused (cooldown, stamina, class) stays consumed until the flag drops.
    private float _guardDownFor = float.MaxValue; // seconds the guard has been down; a raise arms the perfect window only after the rearm
    private bool _perfectArmed;
    private bool _remoteDodgeFlag, _remoteParryFlag;
    private bool _remoteDodgeLive, _remoteParryLive;     // the current rise opened a window that a held flag may keep open
    private float _remoteDodgeAge, _remoteParryAge;
    public bool Dodging => DodgeLeft > 0f;
    public bool Parrying => ParryLeft > 0f;
    public bool InPerfectWindow => _perfectArmed && GuardHeld >= 0f && GuardHeld <= DefenseRules.PerfectBlockSeconds;

    public bool CanDodge(float stamina) => !Dodging && DodgeCooldownLeft <= 0f && stamina >= DefenseRules.DodgeStamina;
    public bool CanParry(float stamina) => !Parrying && ParryCooldownLeft <= 0f && stamina >= DefenseRules.ParryStamina;

    public bool TryDodge(float stamina)
    {
        if (!CanDodge(stamina)) return false;
        DodgeLeft = DefenseRules.DodgeSeconds;
        DodgeCooldownLeft = DefenseRules.DodgeCooldown;
        return true;
    }

    /// <param name="windowBonus">Seconds an item adds to the window.</param>
    public bool TryParry(float stamina, float windowBonus = 0f)
    {
        if (!CanParry(stamina)) return false;
        ParryLeft = DefenseRules.ParrySeconds + Math.Max(0f, windowBonus);
        ParryCooldownLeft = DefenseRules.ParryCooldown;
        return true;
    }

    /// <summary>Timers count down; the guard's age runs from the tick it was raised.</summary>
    public void Update(float dt, bool guarding)
    {
        TickWindows(dt);
        UpdateGuard(dt, guarding);
    }

    /// <summary>The guard half of <see cref="Update"/>, for a remote body whose windows <see cref="ApplyRemote"/> already ticked.</summary>
    public void UpdateGuard(float dt, bool guarding)
    {
        if (!guarding) { GuardHeld = -1f; _guardDownFor += dt; }
        else if (GuardHeld < 0f) { GuardHeld = 0f; _perfectArmed = _guardDownFor >= DefenseRules.PerfectBlockRearmSeconds; _guardDownFor = 0f; }
        else GuardHeld += dt;
    }

    private void TickWindows(float dt)
    {
        if (DodgeLeft > 0f) DodgeLeft -= dt;
        if (DodgeCooldownLeft > 0f) DodgeCooldownLeft -= dt;
        if (ParryLeft > 0f) ParryLeft -= dt;
        if (ParryCooldownLeft > 0f) ParryCooldownLeft -= dt;
        _remoteDodgeAge += dt;
        _remoteParryAge += dt;
    }

    /// <summary>What becomes of a blow. Hazards (<paramref name="fromCreature"/> false) are never dodged, parried or blocked.</summary>
    public DefenseOutcome Judge(AttackKind kind, bool fromCreature, bool canBlock, bool guarding, float stamina)
    {
        if (!fromCreature) return DefenseOutcome.None;
        if (Dodging) return DefenseOutcome.Dodged;
        if (kind == AttackKind.Melee && Parrying && !canBlock) return DefenseOutcome.Parried;
        if (canBlock && guarding && kind != AttackKind.Spell)
            return InPerfectWindow ? DefenseOutcome.PerfectBlock : stamina >= DefenseRules.BlockStamina ? DefenseOutcome.Blocked : DefenseOutcome.None;
        return DefenseOutcome.None;
    }

    /// <summary>
    /// Server side, for a remote body: mirror the flags the client sent and tick the windows by <paramref name="dt"/>.
    /// Only a rising flag opens a window, and only when the cooldown is over and <paramref name="stamina"/> covers the cost (a parry also needs
    /// <paramref name="canParry"/>): the caller charges the stamina when a start is reported, in this same step. A refused rise stays refused until the
    /// flag drops. An opened window lasts Seconds + <see cref="DefenseRules.RemoteGraceSeconds"/>; a held flag keeps it open at least the grace, never past
    /// that total from the rise; a dropped flag lets it run out. Returns which windows began this call.
    /// </summary>
    public (bool dodgeStarted, bool parryStarted) ApplyRemote(bool dodgeFlag, bool parryFlag, float dt, float stamina = float.MaxValue, bool canParry = true)
    {
        var dodgeStarted = false;
        var parryStarted = false;
        if (dodgeFlag)
        {
            if (!_remoteDodgeFlag)
            {
                _remoteDodgeLive = CanDodge(stamina);
                if (_remoteDodgeLive) { DodgeLeft = DefenseRules.DodgeSeconds + DefenseRules.RemoteGraceSeconds; DodgeCooldownLeft = DefenseRules.DodgeCooldown; _remoteDodgeAge = 0f; dodgeStarted = true; }
            }
            else if (_remoteDodgeLive) DodgeLeft = HeldWindow(DodgeLeft, DefenseRules.DodgeSeconds, _remoteDodgeAge);
        }
        else _remoteDodgeLive = false;
        if (parryFlag)
        {
            if (!_remoteParryFlag)
            {
                _remoteParryLive = canParry && CanParry(stamina);
                if (_remoteParryLive) { ParryLeft = DefenseRules.ParrySeconds + DefenseRules.RemoteGraceSeconds; ParryCooldownLeft = DefenseRules.ParryCooldown; _remoteParryAge = 0f; parryStarted = true; }
            }
            else if (_remoteParryLive) ParryLeft = HeldWindow(ParryLeft, DefenseRules.ParrySeconds, _remoteParryAge);
        }
        else _remoteParryLive = false;
        _remoteDodgeFlag = dodgeFlag;
        _remoteParryFlag = parryFlag;
        TickWindows(dt);
        return (dodgeStarted, parryStarted);
    }

    private static float HeldWindow(float left, float seconds, float age)
    {
        var budget = seconds + DefenseRules.RemoteGraceSeconds - age;
        return budget <= 0f ? left : Math.Max(left, Math.Min(DefenseRules.RemoteGraceSeconds, budget));
    }

    /// <summary>Close a window the server will not honour; a held remote flag cannot reopen it.</summary>
    public void CancelDodge() { DodgeLeft = 0f; _remoteDodgeLive = false; }
    public void CancelParry() { ParryLeft = 0f; _remoteParryLive = false; }
}
