using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The <see cref="RailGate"/>s at a railed floor's stops, owned by the RailController: it builds one at each intermediate
/// stop whose room holds a guard or defend collective, lifts a gate on the server once its holders are dead, lets the winch
/// work when none of them stands near, and keeps which gates stand up for the save. The gates are nodes under the floor
/// root; this is a plain class holding them.
/// </summary>
public sealed class StopGates
{
    /// <summary>A post stands this far out from each lane's centre line: the tub's half-width (0.7 m) and a hand's clearance, so a cart passes between them (still inside the 4 m cell).</summary>
    public const float GateSide = MineCart.TubWidth / 2f + 0.35f;
    /// <summary>The winch works when no living holder of the stop is within this horizontal range of the gate.</summary>
    public const float WinchClearRange = 12f;
    const float GateEvery = 0.25f;
    /// <summary>Gap kept between a gate and the next stop's cart.</summary>
    const float GateMargin = 2f;
    const float GateBeyondCart = 1.5f;

    private readonly DungeonBuilder _builder;
    private readonly FloorLayout _layout;
    private readonly Action<RailGate> _onLifted;
    private readonly Action _onQuietLift;
    /// <summary>The gate at each stop (null: the landing, the end of the line, or a stop with no mobs of its own). Indexed by stop.</summary>
    private RailGate?[] _gates = Array.Empty<RailGate?>();
    private float _timer;
    /// <summary>The next gate check after a restore lifts quietly (no log, no tween).</summary>
    private bool _restored;

    /// <param name="onLifted">A gate went up (the kill check or the winch; not a restore).</param>
    /// <param name="onQuietLift">A gate stood up quietly at the first check after a restore.</param>
    public StopGates(DungeonBuilder builder, FloorLayout layout, Action<RailGate> onLifted, Action onQuietLift)
    {
        _builder = builder;
        _layout = layout;
        _onLifted = onLifted;
        _onQuietLift = onQuietLift;
    }

    /// <summary>The stop's gate stands open (or the stop has none). A stop's gate starts down while any of its mobs live.</summary>
    public bool Up(int stopIndex) => stopIndex < 0 || stopIndex >= _gates.Length || _gates[stopIndex] is not { } g || !GodotObject.IsInstanceValid(g) || g.Up;

    /// <summary>The intermediate stops (indices) whose gate is up, for the save. Stops with no gate are not listed.</summary>
    public IReadOnlyList<int> UpList
    {
        get
        {
            var list = new List<int>();
            for (var i = 0; i < _gates.Length; i++)
                if (_gates[i] is { } g && GodotObject.IsInstanceValid(g) && g.Up) list.Add(i);
            return list;
        }
    }

    /// <summary>Back from a save: the listed gates stand up at once (no animation, no log). Gates not listed stay as they are.</summary>
    public void Restore(IEnumerable<int> up)
    {
        _restored = true; // a down gate whose mobs are already dead lifts quietly at the next check
        foreach (var i in up)
            if (i >= 0 && i < _gates.Length && _gates[i] is { } g && GodotObject.IsInstanceValid(g)) g.SetUp();
    }

    /// <summary>A gate across both lanes just past each intermediate stop that holds mobs, with its winch on the outer side of the party lane.</summary>
    public void Build(Node3D root, IReadOnlyList<float> stopS, TrackSampler party)
    {
        _gates = new RailGate?[stopS.Count];
        if (_builder.EnemyTrack is not { } enemy) return;
        for (var i = 1; i < stopS.Count - 1; i++)
        {
            if (!HoldsMobs(i)) continue; // no mobs of its own: the gate starts up (there is none to see)
            var s = stopS[i] + CartRules.HalfLength + GateBeyondCart;
            s = Mathf.Min(s, stopS[i + 1] - GateMargin);
            s = Mathf.Max(s, stopS[i]);
            var frame = party.Frame(s);
            var at = frame.Origin;
            var right = frame.Basis.X.Normalized();
            var near = enemy.ToGlobal(enemy.Curve.GetClosestPoint(enemy.ToLocal(at)));
            var lateral = (near - at).Dot(right);
            float left = Mathf.Min(0f, lateral) - GateSide, rightEdge = Mathf.Max(0f, lateral) + GateSide;
            // the winch stands on the outer side of the party lane (away from the enemy lane)
            var gate = new RailGate(i, left, rightEdge, winchRight: lateral < 0f) { Transform = new Transform3D(frame.Basis.Orthonormalized(), at) };
            root.AddChild(gate);
            gate.CanCrank = NoHolderNear;
            gate.Lifted += _onLifted;
            _gates[i] = gate;
        }
    }

    /// <summary>Server: every 0.25 s a down gate whose stop has no living mob lifts (quietly at the first check after a restore).</summary>
    public void Tick(float dt)
    {
        _timer -= dt;
        if (_timer > 0f) return;
        _timer = GateEvery;
        var quiet = _restored;
        for (var i = 0; i < _gates.Length; i++)
        {
            if (_gates[i] is not { Up: false } g || !GodotObject.IsInstanceValid(g)) continue;
            if (!Adventure.Objectives.CrewRules.StopCleared(AliveAtStop(i))) continue;
            if (quiet) { g.SetUp(); _onQuietLift(); } else g.Lift(); // Lifted tells the rest
        }
        _restored = false;
    }

    private bool NoHolderNear(RailGate gate)
    {
        var room = _layout.Rail!.Stops[gate.Stop].Room;
        var at = gate.GlobalPosition;
        if (_builder.Collectives is { } reg)
            foreach (var c in reg.Collectives)
                if (c.Room == room && c.Goal is GoalKind.Guard or GoalKind.Defend)
                    foreach (var e in c.Alive)
                    {
                        var d = e.GlobalPosition - at;
                        if (d.X * d.X + d.Z * d.Z <= WinchClearRange * WinchClearRange) return false;
                    }
        return true;
    }

    /// <summary>The mobs that hold the stop: every collective in the stop's room that stays put (guards and defenders; a patrol or alarm group does not hold a gate).</summary>
    private int AliveAtStop(int stopIndex)
    {
        var room = _layout.Rail!.Stops[stopIndex].Room;
        var n = 0;
        if (_builder.Collectives is { } reg)
            foreach (var c in reg.Collectives)
                if (c.Room == room && c.Goal is GoalKind.Guard or GoalKind.Defend) n += c.Alive.Count();
        return n;
    }

    private bool HoldsMobs(int stopIndex)
    {
        var room = _layout.Rail!.Stops[stopIndex].Room;
        return _builder.Collectives is { } reg && reg.Collectives.Any(c => c.Room == room && c.Goal is GoalKind.Guard or GoalKind.Defend);
    }
}
