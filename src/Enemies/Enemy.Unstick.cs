using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>
/// The safety net for a creature stuck inside the level (under a ramp's slope, in a sealed breach, pushed into a wall by
/// a spawn or a shove). Once a second, on the server or in solo, the body's shape is tested against the solid world,
/// shrunk and lifted clear of the floor so standing or brushing a wall never counts. Embedded for two checks in a row,
/// it goes back to the last place it stood free; a creature that never stood free (still in its nest or pen) is left alone.
/// Clients follow through the snapshots.
/// </summary>
public partial class Enemy
{
    private const float UnstickCheckSeconds = 1f;
    private const int UnstickAfterChecks = 2;
    private double _unstickTimer;
    private int _embeddedChecks;
    private Vector3? _lastFree;

    private void TickUnstick(float dt)
    {
        _unstickTimer -= dt;
        if (_unstickTimer > 0) return;
        _unstickTimer = UnstickCheckSeconds;
        if (!IsAlive || IsBoss || Captive != null || GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is not CapsuleShape3D capsule) return;
        if (!Embedded(capsule))
        {
            _embeddedChecks = 0;
            if (IsOnFloor()) _lastFree = GlobalPosition;
            return;
        }
        if (++_embeddedChecks < UnstickAfterChecks || _lastFree is not { } back) return;
        GD.Print($"[unstick] {Name} {DisplayName} embedded at {GlobalPosition:0.00}: back to {back:0.00}");
        GlobalPosition = back;
        Velocity = Vector3.Zero;
        _path = null;
        _repathTimer = 0f;
        _embeddedChecks = 0;
    }

    /// <summary>Whether a slightly smaller copy of the body, lifted off the floor, overlaps solid level geometry (layer 1).</summary>
    private bool Embedded(CapsuleShape3D capsule)
    {
        var probe = new CapsuleShape3D { Radius = capsule.Radius * 0.7f, Height = Math.Max(capsule.Radius * 1.4f + 0.05f, capsule.Height * 0.6f) };
        var col = GetNode<CollisionShape3D>("Collision");
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = probe,
            Transform = new Transform3D(Basis.Identity, GlobalPosition + col.Position + Vector3.Up * 0.15f),
            CollisionMask = 1,
            CollideWithBodies = true,
            CollideWithAreas = false,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count > 0;
    }
}
