using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Which bodies a cart's front swept over its last step. It judges, the owner applies (the run-over outcome, a knock
/// aside, dented plating, a shake): any cart can share it. No allocation.
/// </summary>
public static class RunOverSweep
{
    /// <summary>Only bodies this close (flat) to the cart's front are measured.</summary>
    public const float ScanRange = 6f;
    /// <summary>A body that was hit is not judged again for this long (seconds of physics time).</summary>
    public const float JudgeAgain = 1f;
    /// <summary>The stretch of line either side of the front that a body is measured against.</summary>
    public const float Window = 8f;
    /// <summary>A body's radius on the rails; an enemy's scales with its profile.</summary>
    public const float BodyRadius = 0.4f;

    private static readonly List<ulong> Stale = new();

    public static float RadiusOf(Node3D body) => body is Enemy enemy ? BodyRadius * enemy.Profile.Scale : BodyRadius;

    /// <summary>
    /// Fill <paramref name="hits"/> with the <paramref name="bodies"/> the cart ran over between PrevS and S: near its
    /// front, measured within <see cref="Window"/> of it, swept by <see cref="RunOverRules.Hits"/> in the cart's
    /// direction, and not hit within <see cref="JudgeAgain"/> (<paramref name="hitAt"/>, which this keeps). A body riding
    /// a cart (this one's <see cref="ICartBody.Riders"/>, or any other) is carried, never run over. The owner filters the
    /// fallen and the dead out of <paramref name="bodies"/>.
    /// </summary>
    public static void Judge(CartRun run, IReadOnlyList<Node3D> bodies, double now, Dictionary<ulong, double> hitAt, List<(Node3D body, TrackBody tb)> hits)
    {
        hits.Clear();
        Forget(hitAt, now);
        if (!run.Moving) return;
        float sPrev = run.PrevS, sNow = run.State.S, frontS = run.FrontS;
        var front = run.Track.Point(frontS);
        var cart = run.Body as ICartBody;
        for (var i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            if (!GodotObject.IsInstanceValid(body)) continue;
            if (body is ICartRider rider && (rider.Cart != null || (cart != null && cart.Riders.TryGetValue(rider.RiderId, out var r) && r == rider))) continue;
            var pos = body.GlobalPosition;
            float dx = pos.X - front.X, dz = pos.Z - front.Z;
            if (dx * dx + dz * dz > ScanRange * ScanRange) continue;
            if (!run.Track.Measure(pos, frontS, Window, out var b)) continue;
            b = b with { Radius = RadiusOf(body) };
            if (!RunOverRules.Hits(sPrev, sNow, b, run.Dir)) continue;
            var id = body.GetInstanceId();
            if (hitAt.TryGetValue(id, out var t) && now - t < JudgeAgain) continue;
            hitAt[id] = now;
            hits.Add((body, b));
        }
    }

    /// <summary>Drop the hits older than <see cref="JudgeAgain"/>.</summary>
    private static void Forget(Dictionary<ulong, double> hitAt, double now)
    {
        if (hitAt.Count == 0) return;
        Stale.Clear();
        foreach (var kv in hitAt) if (now - kv.Value >= JudgeAgain) Stale.Add(kv.Key);
        foreach (var id in Stale) hitAt.Remove(id);
    }
}
