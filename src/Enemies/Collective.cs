using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Enemies.Collectives;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>
/// A runtime enemy group: who belongs, what it protects (in world coordinates), and its shared alert state.
/// Ticked by <see cref="CollectiveRegistry"/>; decisions come from the engine-free <see cref="CollectiveBrain"/>.
/// </summary>
public sealed class Collective
{
    public string Id { get; }
    public string Name { get; }
    public GoalKind Goal { get; }
    public int Room { get; }
    public Vector3? ObjectivePosition { get; }
    public IReadOnlyList<Vector3> Route { get; private set; }
    /// <summary>The rooms the patrol walks, in order (the plan's route), as it stands now.</summary>
    public List<int> RouteRooms { get; private set; }
    private readonly IReadOnlyList<int> _plannedRooms;
    private readonly IReadOnlyList<Vector3> _plannedRoute;
    public string? AlarmTargetId { get; }
    public Vector3? RallyPosition { get; }
    public List<Enemy> Members { get; } = new();
    public int InitialCount { get; private set; }
    public AlertLevel Alert { get; private set; } = AlertLevel.Calm;
    public bool AlarmRaised { get; private set; }
    public bool Regrouped { get; private set; }
    /// <summary>Set once enough survivors reached the rally point (or the wait timed out); members then re-engage.</summary>
    public bool RegroupDone { get; private set; }
    /// <summary>Where the runner came from; investigated when Suspicious.</summary>
    public Vector3? AlarmOrigin { get; private set; }
    public float SecondsSinceContact { get; private set; } = 999f;
    public int? RunnerOrdinal { get; private set; }
    public CollectiveOrders LastOrders { get; private set; } = new(AlertLevel.Calm, Array.Empty<MemberOrder>(), false, false);

    private readonly CollectiveRegistry _registry;
    private float _regroupTimer;

    public Collective(CollectiveContents contents, Func<GridPos, Vector3> surface, CollectiveRegistry registry)
    {
        Id = contents.Id;
        Name = contents.Name;
        Goal = contents.Goal;
        Room = contents.Room;
        ObjectivePosition = contents.ObjectiveCell is { } o ? surface(o) : null;
        Route = contents.RouteCells.Select(surface).ToList();
        _plannedRoute = Route;
        _plannedRooms = contents.Route.ToList();
        RouteRooms = contents.Route.ToList();
        AlarmTargetId = contents.AlarmTarget;
        RallyPosition = contents.RallyCell is { } r ? surface(r) : null;
        _registry = registry;
    }

    /// <summary>Walk a different set of rooms (the gates shifted): planned rooms keep their planned waypoint, new ones take the room's centre.</summary>
    public void SetRoute(IReadOnlyList<int> rooms, Func<int, Vector3> centreOf)
    {
        RouteRooms = rooms.ToList();
        var route = new List<Vector3>();
        foreach (var r in rooms)
        {
            var i = _plannedRooms.ToList().IndexOf(r);
            route.Add(i >= 0 && i < _plannedRoute.Count ? _plannedRoute[i] : centreOf(r));
        }
        Route = route;
        foreach (var m in Alive) m.PatrolIndex = 0;
    }

    public void Add(Enemy e)
    {
        Members.Add(e);
        InitialCount = Members.Count;
    }

    public IEnumerable<Enemy> Alive => Members.Where(m => Godot.GodotObject.IsInstanceValid(m) && m.IsAlive);
    public Collective? AlarmTarget => AlarmTargetId == null ? null : _registry.Find(AlarmTargetId);
    /// <summary>Where a runner heads: the target group's post (its members may be off chasing).</summary>
    public Vector3? AlarmTargetPosition => AlarmTarget?.Members.FirstOrDefault()?.HomePosition ?? AlarmTarget?.RallyPosition;

    /// <summary>The post a member returns to: the objective for Defend, otherwise its own spawn cell.</summary>
    public Vector3 PostFor(Enemy e) => Goal == GoalKind.Defend && ObjectivePosition is { } o ? o : e.HomePosition;

    public AiState RestState(Enemy e) => Goal == GoalKind.Patrol && Route.Count >= 2 ? new PatrolState() : new GuardState();
    public AiState LostContactState(Enemy e) => Goal == GoalKind.Patrol && Route.Count >= 2 ? new PatrolState() : new ReturnState();

    /// <summary>A member noticed or was hit by the party.</summary>
    public void Notify(Enemy e)
    {
        Alert = AlertLevel.Alarmed;
        SecondsSinceContact = 0f;
    }

    /// <summary>A runner from another group arrived.</summary>
    public void ReceiveAlarm(Vector3 origin)
    {
        if (Alert == AlertLevel.Calm) Alert = AlertLevel.Suspicious;
        AlarmOrigin = origin;
        SecondsSinceContact = 0f;
        EventBus.Instance.Log($"The {Name} stir at the alarm.");
    }

    public void OnRunnerArrived(Enemy runner)
    {
        runner.Order = OrderKind.Chase; // the stale RunAlarm order would otherwise re-enter the state until the next tick
        if (AlarmRaised) return;
        AlarmRaised = true;
        AlarmTarget?.ReceiveAlarm(runner.HomePosition);
        EventBus.Instance.RaiseAlarmRaised(Id);
    }

    public void OnMemberDied(Enemy e) { }

    public void Tick(float dt, PartyController? party)
    {
        var alive = Alive.ToList();
        var contact = alive.Any(m => m.IsEngaged || m.CanSeeParty());
        SecondsSinceContact = contact ? 0f : SecondsSinceContact + dt;

        var views = new List<MemberView>();
        foreach (var m in Members)
        {
            var ok = Godot.GodotObject.IsInstanceValid(m) && m.IsAlive;
            views.Add(new MemberView(m.GroupOrdinal, ok, ok ? m.Hp / Math.Max(1f, m.MaxHp) : 0f,
                ok ? Flat(m.GlobalPosition).DistanceTo(Flat(PostFor(m))) : 0f,
                ok ? m.DistanceToParty() : float.MaxValue,
                ok && m.CanSeeParty(), ok && m.IsEngaged, m.Profile.MoveSpeed));
        }
        var partyHp = 1f;
        var partyToObjective = float.MaxValue;
        if (party != null && party.Members.Length > 0)
        {
            partyHp = party.Members.Sum(x => x.Hp) / Math.Max(1f, party.Members.Sum(x => x.Stats[Stat.MaxHp]));
            if (ObjectivePosition is { } obj && party.NearestAlive(obj) is { } near) partyToObjective = near.GlobalPosition.DistanceTo(obj);
        }
        var ctx = new CollectiveContext
        {
            Goal = Goal, Alert = Alert, Members = views, InitialCount = InitialCount, SecondsSinceContact = SecondsSinceContact,
            AlarmRaised = AlarmRaised, Runner = RunnerOrdinal, Regrouped = Regrouped, HasRally = RallyPosition != null, HasObjective = ObjectivePosition != null,
            PartyDistToObjective = partyToObjective, PartyHpFraction = partyHp,
        };
        var orders = CollectiveBrain.Decide(ctx);
        LastOrders = orders;
        Alert = orders.Alert;
        if (orders.RaiseAlarm) RunnerOrdinal = orders.Members.FirstOrDefault(m => m.Kind == OrderKind.RunAlarm)?.Ordinal;
        if (orders.Regroup && !Regrouped)
        {
            Regrouped = true;
            RegroupDone = false;
            _regroupTimer = 0f;
            EventBus.Instance.Log($"The {Name} fall back!");
        }
        foreach (var m in alive) m.Order = orders.For(m.GroupOrdinal);

        if (Regrouped && !RegroupDone && RallyPosition is { } rally)
        {
            _regroupTimer += dt;
            var there = alive.Count(m => Flat(m.GlobalPosition).DistanceTo(Flat(rally)) < 4f);
            if (alive.Count == 0 || there * 10 >= alive.Count * 6 || _regroupTimer >= 8f) RegroupDone = true;
        }
    }

    public void Restore(string alert, bool alarmRaised, bool regrouped)
    {
        if (Enum.TryParse<AlertLevel>(alert, true, out var a)) Alert = a;
        AlarmRaised = alarmRaised;
        Regrouped = regrouped;
        RegroupDone = regrouped;
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
}

/// <summary>Holds a floor's collectives under the floor root and ticks them twice a second.</summary>
public partial class CollectiveRegistry : Node
{
    public const float TickSeconds = 0.5f;
    public List<Collective> Collectives { get; } = new();
    private float _accum;

    public Collective? Find(string id) => Collectives.FirstOrDefault(c => c.Id == id);
    public Collective? GroupOf(Enemy e) => Collectives.FirstOrDefault(c => c.Members.Contains(e));

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        _accum += (float)delta;
        if (_accum < TickSeconds) return;
        var dt = _accum;
        _accum = 0f;
        var party = Main.Instance?.Party;
        if (party == null || !Godot.GodotObject.IsInstanceValid(party) || !party.HasBodies) return;
        foreach (var c in Collectives) c.Tick(dt, party);
    }
}
