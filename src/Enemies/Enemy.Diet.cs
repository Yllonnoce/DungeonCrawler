using DungeonCrawler.Combat;
using DungeonCrawler.Enemies.Ai;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>
/// The tick diet (plan 16, Ruling 20). <see cref="Live"/> keeps every enemy in the tree, so separation walks the enemies
/// rather than every child of the floor root; the effect ticks and the threat keys fill buffers each enemy keeps. A far,
/// still creature that is unaware or holding its post sleeps (<see cref="TickDietRules"/>): its AI state runs one tick in
/// four, staggered by its net id, with the seconds it banked meanwhile. Water, unstick, animation, effects, tint and dying
/// still run every tick.
/// </summary>
public partial class Enemy
{
    private static readonly List<Enemy> LiveList = new();
    /// <summary>Every enemy in the tree, in the order they entered it (kept by <c>_EnterTree</c> and <c>_ExitTree</c>).</summary>
    public static IReadOnlyList<Enemy> Live => LiveList;
    /// <summary>The parent this enemy entered the tree under: separation pushes only between enemies that share one, as the floor root's children did.</summary>
    private Node? _treeParent;

    /// <summary>On a quarter rate this tick: far from every hero and beyond its own sight, still, and unaware or holding (server and solo only).</summary>
    public bool Asleep { get; private set; }
    private float _sleepBank;

    private readonly List<StatusTick> _effectTicks = new();
    private readonly List<int> _threatKeys = new();

    public override void _EnterTree()
    {
        _treeParent = GetParent();
        LiveList.Add(this);
    }

    private void LeaveLive()
    {
        LiveList.Remove(this);
        _treeParent = null;
        Asleep = false;
    }

    /// <summary>
    /// Ruling 20's test for this tick. A patrol and a walk home never sleep (their pauses and give-ups stay to the tick);
    /// an alarm run, a chase or a fight is never unaware.
    /// </summary>
    private bool SleepsNow()
    {
        if (!IsAlive || Party == null) return false;
        var calm = State is HoldState || (State.Unaware && State is not (PatrolState or ReturnState));
        if (!calm) return false;
        var nearest = float.MaxValue;
        foreach (var adv in Party.Adventurers) // downed heroes count: one standing up is seen at once
        {
            if (!IsInstanceValid(adv)) continue;
            var d = GlobalPosition.DistanceTo(adv.GlobalPosition);
            if (d < nearest) nearest = d;
        }
        var horizontal = new Vector2(Velocity.X, Velocity.Z).Length();
        var sight = Profile.DetectRange * Aggression * 1.3f; // the farthest it sees: a wading hero's
        return TickDietRules.Sleeps(calm, horizontal, Carried, Leaping, SinceHurt, nearest, sight);
    }

    /// <summary>
    /// The seconds the AI state runs with this tick: this tick's and any banked while asleep. Null on a sleeper's skipped
    /// tick, whose seconds are banked for its next run.
    /// </summary>
    private float? StateStep(float dt)
    {
        if (Asleep && !TickDietRules.RunsThisTick(NetId, Engine.GetPhysicsFrames())) { _sleepBank += dt; return null; }
        var step = _sleepBank + dt;
        _sleepBank = 0f;
        return step;
    }
}
