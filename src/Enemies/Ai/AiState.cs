using Godot;

namespace DungeonCrawler.Enemies.Ai;

/// <summary>One state of an enemy's finite state machine.</summary>
public abstract class AiState
{
    public abstract string Name { get; }
    /// <summary>Not fighting or hunting: sneak attacks land, companions in Defensive stance ignore it.</summary>
    public virtual bool Unaware => false;
    public virtual void Enter(Enemy enemy) { }
    public abstract void Update(Enemy enemy, float dt);
    public virtual void Exit(Enemy enemy) { }
}

public sealed class IdleState : AiState
{
    public override string Name => "Idle";
    public override bool Unaware => true;
    public override void Update(Enemy enemy, float dt)
    {
        enemy.MoveToward(enemy.GlobalPosition, dt); // keep gravity active
        if (enemy.CanSeeParty()) enemy.ChangeState(new ChaseState());
    }
}

public sealed class ChaseState : AiState
{
    public override string Name => "Chase";
    private float _lostTimer;

    public override void Update(Enemy enemy, float dt)
    {
        if (!enemy.HasTarget && enemy.Party?.NearestAlive(enemy.GlobalPosition) == null) { enemy.ChangeState(enemy.Group?.LostContactState(enemy) ?? enemy.RestState()); return; }
        if (enemy.Group != null && enemy.Order is Collectives.OrderKind.ReturnHome or Collectives.OrderKind.FallBack or Collectives.OrderKind.RunAlarm or Collectives.OrderKind.Regroup && GroupOrders.Apply(enemy, allowChase: false)) return;

        if (enemy.TrySignature()) return;

        var dist = enemy.DistanceToParty();
        if (enemy.Kind == EnemyKind.Melee)
        {
            if (dist <= enemy.AttackRange) { enemy.ChangeState(new AttackState()); return; }
        }
        else if (dist <= enemy.Profile.PreferredRange && enemy.HasLineOfSight())
        {
            enemy.ChangeState(new ShootState());
            return;
        }

        if (enemy.CanSeeParty()) _lostTimer = 0f;
        else if ((_lostTimer += dt) > enemy.GiveUpSeconds) { enemy.ChangeState(enemy.Group?.LostContactState(enemy) ?? enemy.RestState()); return; }

        enemy.MoveAlongPathToward(enemy.TargetPosition, dt);
    }
}

public sealed class AttackState : AiState
{
    public override string Name => "Attack";
    private float _timer;
    private bool _swung;

    public override void Enter(Enemy enemy) { _timer = 0f; _swung = false; enemy.BeginWindup(enemy.WindupSeconds); }

    public override void Update(Enemy enemy, float dt)
    {
        if (enemy.Party == null || !enemy.Party.PartyAlive) { enemy.ChangeState(new IdleState()); return; }

        enemy.FaceToward(enemy.TargetPosition);
        enemy.MoveToward(enemy.GlobalPosition, dt);
        _timer += dt;

        if (!_swung && _timer >= enemy.WindupSeconds)
        {
            _swung = true;
            enemy.StrikeParty();
        }

        if (_timer >= enemy.WindupSeconds + enemy.RecoverSeconds)
        {
            if (enemy.TrySignature()) return;
            if (enemy.Order is Collectives.OrderKind.Regroup or Collectives.OrderKind.RunAlarm && GroupOrders.Apply(enemy, allowChase: false)) return;
            if (enemy.DistanceToParty() <= enemy.AttackRange) { _timer = 0f; _swung = false; enemy.BeginWindup(enemy.WindupSeconds); }
            else enemy.ChangeState(new ChaseState());
        }
    }
}

/// <summary>Ranged and caster enemies: stand, wind up, fire a projectile, recover.</summary>
public sealed class ShootState : AiState
{
    public override string Name => "Shoot";
    private float _timer;
    private bool _fired;

    public override void Enter(Enemy enemy) { _timer = 0f; _fired = false; enemy.BeginWindup(enemy.WindupSeconds); }

    public override void Update(Enemy enemy, float dt)
    {
        if (enemy.Party == null || !enemy.Party.PartyAlive) { enemy.ChangeState(new IdleState()); return; }

        enemy.FaceToward(enemy.TargetPosition);
        enemy.MoveToward(enemy.GlobalPosition, dt);
        _timer += dt;

        if (!_fired && _timer >= enemy.WindupSeconds)
        {
            _fired = true;
            if (enemy.HasLineOfSight()) enemy.FireAtParty();
        }

        if (_timer >= enemy.WindupSeconds + enemy.RecoverSeconds)
        {
            if (enemy.TrySignature()) return;
            if (enemy.Order is Collectives.OrderKind.Regroup or Collectives.OrderKind.RunAlarm && GroupOrders.Apply(enemy, allowChase: false)) return;
            var dist = enemy.DistanceToParty();
            if (dist <= enemy.Profile.PreferredRange * 1.3f && enemy.HasLineOfSight()) { _timer = 0f; _fired = false; enemy.BeginWindup(enemy.WindupSeconds); }
            else enemy.ChangeState(new ChaseState());
        }
    }
}

/// <summary>Boss signature abilities, each drawn on the floor for its delay (<see cref="TelegraphRules"/>): Slam, Volley and Summon land at the delay; Charge winds up then runs.</summary>
public sealed class SignatureState : AiState
{
    public override string Name => $"Signature:{_sig}";
    private readonly Signature _sig;
    private float _timer;
    private bool _done;

    public SignatureState(Signature sig) => _sig = sig;

    public override void Enter(Enemy enemy)
    {
        _timer = 0f;
        _done = false;
        enemy.BeginSignatureTelegraph(_sig);
    }

    public override void Exit(Enemy enemy) => enemy.EndTelegraph(); // a stagger mid-telegraph takes the decal with it

    public override void Update(Enemy enemy, float dt)
    {
        if (enemy.Party == null || !enemy.Party.PartyAlive) { enemy.ChangeState(new IdleState()); return; }
        var partyPos = enemy.TargetPosition;
        _timer += dt;
        switch (_sig)
        {
            case Signature.Slam:
            case Signature.Volley:
            case Signature.Summon:
                enemy.FaceToward(partyPos);
                enemy.MoveToward(enemy.GlobalPosition, dt);
                if (!_done && _timer >= TelegraphRules.Delay(_sig)) { _done = true; enemy.EndTelegraph(); enemy.PerformSignature(_sig); }
                if (_timer >= TelegraphRules.Delay(_sig) + TelegraphRules.SignatureRecover) enemy.ChangeState(new ChaseState());
                break;
            case Signature.Charge:
                if (_timer < TelegraphRules.ChargeDelay) { enemy.FaceToward(partyPos); enemy.MoveToward(enemy.GlobalPosition, dt); break; }
                enemy.EndTelegraph();
                enemy.ChargeStep(dt);
                if (!_done && enemy.DistanceToParty() <= enemy.AttackRange * 1.3f) { _done = true; enemy.PerformSignature(_sig); enemy.ChangeState(new AttackState()); }
                else if (_timer >= TelegraphRules.ChargeDelay + TelegraphRules.ChargeRun) enemy.ChangeState(new ChaseState());
                break;
            default:
                enemy.ChangeState(new ChaseState());
                break;
        }
    }
}

/// <summary>Wounded ranged enemies back off for a few seconds, then return to the fight.</summary>
public sealed class FleeState : AiState
{
    public override string Name => "Flee";
    private float _timer;

    public override void Enter(Enemy enemy)
    {
        _timer = 0f;
        Core.EventBus.Instance.Log($"{enemy.DisplayName} falls back!");
    }

    public override void Update(Enemy enemy, float dt)
    {
        _timer += dt;
        enemy.FleeStep(dt);
        if (_timer >= 3f || enemy.DistanceToParty() > 14f) enemy.ChangeState(new ChaseState());
    }
}

/// <summary>A boss at half health: 1.2 s untargetable, roaring, then phase two.</summary>
public sealed class RoarState : AiState
{
    public override string Name => "Roar";
    private float _timer;
    private int _pulses;

    public override void Enter(Enemy enemy)
    {
        _timer = 0f; _pulses = 0;
        enemy.SetUntargetable(true);
        Core.EventBus.Instance.PlaySfx("boss");
        Core.EventBus.Instance.Log($"{enemy.DisplayName} roars!");
        enemy.FlashWhite(0.3f);
    }

    public override void Update(Enemy enemy, float dt)
    {
        _timer += dt;
        enemy.MoveToward(enemy.GlobalPosition, dt);
        if (_timer >= 0.4f * (_pulses + 1) && _pulses < 2) { _pulses++; enemy.FlashWhite(0.3f); } // three pulses over the roar
        if (_timer >= BossRules.RoarSeconds) enemy.ChangeState(new ChaseState());
    }

    public override void Exit(Enemy enemy)
    {
        enemy.SetUntargetable(false);
        enemy.EnterPhaseTwo(silent: false);
    }
}

public sealed class DeadState : AiState
{
    public override string Name => "Dead";
    private float _timer;

    public override void Enter(Enemy enemy) => enemy.OnDeath();

    public override void Update(Enemy enemy, float dt)
    {
        _timer += dt;
        var linger = enemy.CorpseSeconds + 1.5f; // let the death clip play, then sink away
        if (_timer > enemy.CorpseSeconds) enemy.Position += Vector3.Down * dt * 0.8f;
        if (_timer >= linger) enemy.QueueFree();
    }
}

/// <summary>
/// Go for an NPC (the ritualist) and keep hitting them; the party only matters when it hurts this creature or stands in
/// its way. Losing the NPC (down, dead, gone) turns the creature on the party.
/// </summary>
public sealed class HuntNpcState : AiState
{
    public override string Name => "Hunt";
    private float _timer;
    private bool _swung;

    public override void Update(Enemy enemy, float dt)
    {
        var npc = enemy.NpcTarget;
        if (npc == null || !Godot.GodotObject.IsInstanceValid(npc) || !npc.IsAlive) { enemy.ChangeState(new ChaseState()); return; }
        // the party got in the way: a fresh wound, or a body within arm's reach between us and the prize
        if (enemy.SinceHurt < 4f || (enemy.CanSeeParty() && enemy.DistanceToParty() < enemy.AttackRange * 1.5f)) { enemy.ChangeState(new ChaseState()); return; }
        var flat = new Godot.Vector3(npc.GlobalPosition.X, enemy.GlobalPosition.Y, npc.GlobalPosition.Z);
        var dist = enemy.GlobalPosition.DistanceTo(flat);
        if (dist <= enemy.AttackRange)
        {
            enemy.FaceToward(npc.GlobalPosition);
            enemy.MoveToward(enemy.GlobalPosition, dt);
            _timer += dt;
            if (!_swung && _timer >= enemy.WindupSeconds) { _swung = true; enemy.StrikeNpc(npc); }
            if (_timer >= enemy.WindupSeconds + enemy.RecoverSeconds) { _timer = 0f; _swung = false; }
            return;
        }
        _timer = 0f; _swung = false;
        enemy.MoveAlongPathToward(npc.GlobalPosition, dt);
    }
}

/// <summary>
/// A jailer marching a prisoner to the jail. The captive trails behind (the controller moves them); at the jail the
/// controller takes over. A wound from the party turns the jailer on it, and the captive slips away.
/// </summary>
public sealed class EscortCaptiveState : AiState
{
    public override string Name => "Escort";
    public override bool Unaware => true; // busy with its prize: the party can get the drop on it
    private readonly Godot.Vector3 _jail;
    public EscortCaptiveState(Godot.Vector3 jail) => _jail = jail;

    public override void Update(Enemy enemy, float dt)
    {
        if (enemy.Captive == null || !Godot.GodotObject.IsInstanceValid(enemy.Captive) || !enemy.Captive.Captured) { enemy.ChangeState(new ChaseState()); return; }
        if (enemy.SinceHurt < 4f) { enemy.ChangeState(new ChaseState()); return; }
        var flat = new Godot.Vector3(_jail.X, enemy.GlobalPosition.Y, _jail.Z);
        if (enemy.GlobalPosition.DistanceTo(flat) < Adventure.Objectives.JailbreakRules.DeliverRange) { enemy.MoveToward(enemy.GlobalPosition, dt); return; } // the controller delivers
        enemy.MoveAlongPathToward(_jail, dt);
    }
}

