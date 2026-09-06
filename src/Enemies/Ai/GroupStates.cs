using DungeonCrawler.Enemies.Collectives;
using Godot;

namespace DungeonCrawler.Enemies.Ai;

/// <summary>Shared handling of group orders that pull a member out of whatever calm thing it is doing.</summary>
internal static class GroupOrders
{
    /// <summary>Switch state for orders that always apply; true when a switch happened.</summary>
    public static bool Apply(Enemy enemy, bool allowChase)
    {
        if (enemy.Group == null) return false;
        switch (enemy.Order)
        {
            case OrderKind.RunAlarm when enemy.State is not AlarmRunState: enemy.ChangeState(new AlarmRunState()); return true;
            case OrderKind.Regroup when enemy.State is not RegroupState && !enemy.Group.RegroupDone: enemy.ChangeState(new RegroupState()); return true;
            case OrderKind.Investigate when enemy.State is not InvestigateState && enemy.Group.AlarmOrigin is { } origin: enemy.ChangeState(new InvestigateState(origin)); return true;
            case OrderKind.Chase when allowChase && enemy.State is not ChaseState: enemy.ChangeState(new ChaseState()); return true;
            case OrderKind.ReturnHome or OrderKind.FallBack when enemy.State is not ReturnState && enemy.DistanceToPost() > 1.2f: enemy.ChangeState(new ReturnState()); return true;
            case OrderKind.Patrol when enemy.State is not PatrolState && enemy.Group.Route.Count >= 2: enemy.ChangeState(new PatrolState()); return true;
        }
        return false;
    }
}

/// <summary>Grouped idle: stand at the post, watch, follow group orders.</summary>
public sealed class GuardState : AiState
{
    public override string Name => "Guard";
    public override bool Unaware => true;

    public override void Update(Enemy enemy, float dt)
    {
        enemy.MoveToward(enemy.GlobalPosition, dt);
        if (enemy.CanSeeParty()) { enemy.ChangeState(new ChaseState()); return; }
        GroupOrders.Apply(enemy, allowChase: true);
    }
}

/// <summary>Walk back to the post after losing the party or being ordered to fall back.</summary>
public sealed class ReturnState : AiState
{
    public override string Name => "Return";
    public override bool Unaware => true;
    private float _timer;

    public override void Update(Enemy enemy, float dt)
    {
        _timer += dt;
        if (enemy.CanSeeParty() && enemy.Order is not (OrderKind.FallBack or OrderKind.ReturnHome)) { enemy.ChangeState(new ChaseState()); return; }
        if (GroupOrders.Apply(enemy, allowChase: false)) return;
        var post = enemy.PostPosition;
        if (enemy.DistanceToPost() < 1.0f || _timer > 20f)
        {
            enemy.FaceToward(post);
            enemy.ChangeState(enemy.Group?.RestState(enemy) ?? new IdleState());
            return;
        }
        enemy.MoveAlongPathToward(post, dt);
    }
}

/// <summary>Walk the group's route, pausing at each stop.</summary>
public sealed class PatrolState : AiState
{
    public override string Name => "Patrol";
    public override bool Unaware => true;
    public const float PauseSeconds = 1.5f;
    private float _pause;

    public override void Update(Enemy enemy, float dt)
    {
        if (enemy.CanSeeParty()) { enemy.ChangeState(new ChaseState()); return; }
        if (GroupOrders.Apply(enemy, allowChase: true)) return;
        var route = enemy.Group?.Route;
        if (route == null || route.Count < 2) { enemy.ChangeState(new GuardState()); return; }
        var wp = route[enemy.PatrolIndex % route.Count];
        var flat = new Vector3(wp.X, enemy.GlobalPosition.Y, wp.Z);
        if (enemy.GlobalPosition.DistanceTo(flat) < 1.2f)
        {
            enemy.MoveToward(enemy.GlobalPosition, dt);
            _pause += dt;
            if (_pause >= PauseSeconds) { _pause = 0f; enemy.PatrolIndex = (enemy.PatrolIndex + 1) % route.Count; }
            return;
        }
        enemy.MoveAlongPathToward(wp, dt);
    }
}

/// <summary>Run to another group and wake it, then rejoin the fight.</summary>
public sealed class AlarmRunState : AiState
{
    public override string Name => "AlarmRun";
    private float _timer;

    public const float SprintBoost = 2.0f;

    public override void Enter(Enemy enemy)
    {
        enemy.RunBoost = SprintBoost;
        Core.EventBus.Instance.Log($"{enemy.DisplayName} runs for help!");
    }

    public override void Exit(Enemy enemy) => enemy.RunBoost = 1f;

    public override void Update(Enemy enemy, float dt)
    {
        _timer += dt;
        var group = enemy.Group;
        if (group?.AlarmTargetPosition is not { } target) { group?.OnRunnerArrived(enemy); enemy.ChangeState(new ChaseState()); return; }
        var flat = new Vector3(target.X, enemy.GlobalPosition.Y, target.Z);
        if (enemy.GlobalPosition.DistanceTo(flat) < 2.5f || _timer > 20f)
        {
            group.OnRunnerArrived(enemy);
            enemy.ChangeState(new ChaseState());
            return;
        }
        enemy.MoveAlongPathToward(target, dt);
    }
}

/// <summary>Fall back to the rally point; re-engage together once enough of the group is there.</summary>
public sealed class RegroupState : AiState
{
    public override string Name => "Regroup";

    public override void Update(Enemy enemy, float dt)
    {
        var group = enemy.Group;
        if (group?.RallyPosition is not { } rally || group.RegroupDone) { enemy.ChangeState(new ChaseState()); return; }
        var flat = new Vector3(rally.X, enemy.GlobalPosition.Y, rally.Z);
        if (enemy.GlobalPosition.DistanceTo(flat) < 2f) { enemy.FaceToward(enemy.TargetPosition); enemy.MoveToward(enemy.GlobalPosition, dt); return; }
        enemy.MoveAlongPathToward(rally, dt);
    }
}

/// <summary>Suspicious: walk toward where the alarm came from, ready to chase.</summary>
public sealed class InvestigateState : AiState
{
    public override string Name => "Investigate";
    private readonly Vector3 _point;
    private float _timer;

    public InvestigateState(Vector3 point) => _point = point;

    public override void Update(Enemy enemy, float dt)
    {
        _timer += dt;
        if (enemy.CanSeeParty()) { enemy.ChangeState(new ChaseState()); return; }
        if (enemy.Order is OrderKind.Regroup or OrderKind.RunAlarm && GroupOrders.Apply(enemy, allowChase: false)) return;
        var flat = new Vector3(_point.X, enemy.GlobalPosition.Y, _point.Z);
        if (enemy.GlobalPosition.DistanceTo(flat) < 2f || _timer > 12f || enemy.Order is OrderKind.ReturnHome or OrderKind.HoldPost or OrderKind.Patrol)
        {
            enemy.ChangeState(enemy.Group?.LostContactState(enemy) ?? new IdleState());
            return;
        }
        enemy.MoveAlongPathToward(_point, dt);
    }
}
