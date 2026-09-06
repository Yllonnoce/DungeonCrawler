using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// A* path following for a body that walks the floor grid: companions and escorted NPCs share it. Keeps the current
/// path and re-plans every half second or when the goal cell changes; goes straight when the target is close and visible.
/// </summary>
public sealed class PathMover
{
    private List<PathNode>? _path;
    private int _pathIndex;
    private float _repathTimer;
    private GridPos _pathTarget;

    /// <summary>Seconds' worth of failed path searches in a row (0.5 per attempt); callers teleport when it grows.</summary>
    public float Unreachable { get; private set; }

    public void Reset()
    {
        _path = null;
        Unreachable = 0f;
    }

    /// <summary>
    /// The point to walk toward this frame, or null when within <paramref name="stopWithin"/> of the target.
    /// <paramref name="straight"/> is true when no path was needed (close and visible).
    /// </summary>
    public Vector3? Next(Node3D body, Vector3 target, float dt, float stopWithin, Func<Vector3, bool> clearLine, out bool straight)
    {
        straight = false;
        var builder = Main.Instance?.Builder;
        var layout = builder?.Layout;
        var pos = body.GlobalPosition;
        var flatDist = new Vector2(target.X - pos.X, target.Z - pos.Z).Length();
        if (flatDist <= stopWithin) { Unreachable = 0f; return null; }
        if (layout == null || builder == null || (flatDist < 5f && Math.Abs(target.Y - pos.Y) < 1.5f && clearLine(target)))
        {
            _path = null;
            straight = true;
            return target;
        }
        var me = new PathNode(DungeonBuilder.WorldToCell(pos), builder.IsUnderAt(pos));
        var goal = new PathNode(DungeonBuilder.WorldToCell(target), builder.IsUnderAt(target));
        _repathTimer -= dt;
        if (_path == null || goal.Cell != _pathTarget || _repathTimer <= 0f)
        {
            _path = GridPathfinder.FindPath(layout, me, goal, builder.IsCellBlocked, builder.IsSecretOpen);
            _pathIndex = 1;
            _pathTarget = goal.Cell;
            _repathTimer = 0.5f;
        }
        if (_path == null)
        {
            Unreachable += 0.5f;
            straight = true;
            return target;
        }
        Unreachable = 0f;
        if (_path.Count < 2) { straight = true; return target; }
        _pathIndex = Math.Clamp(_pathIndex, 1, _path.Count - 1);
        var wp = builder.Surface(_path[_pathIndex].Cell, _path[_pathIndex].Under) with { Y = pos.Y };
        if (new Vector2(wp.X - pos.X, wp.Z - pos.Z).Length() < 0.7f && _pathIndex < _path.Count - 1)
        {
            _pathIndex++;
            wp = builder.Surface(_path[_pathIndex].Cell, _path[_pathIndex].Under) with { Y = pos.Y };
        }
        return _pathIndex >= _path.Count - 1 ? target : wp;
    }

    public static Vector3 Planar(Vector3 from, Vector3 to)
    {
        var d = to - from; d.Y = 0;
        return d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Zero;
    }
}
