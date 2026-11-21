using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A dead-end spur off the enemy line into the wall, with its points' lever (a <see cref="TrackSwitch"/>) between the lanes
/// <see cref="LeverLead"/> metres short of them, where a rider can throw it in passing (from the seat, through the seated window). The spur is a
/// short <see cref="RailTrack"/> bending away from the party's lane (<see cref="SpurLength"/> metres) to a timber stop at its
/// end. Thrown when the golem's runner reaches the points, the golem is sent down it (<see cref="Sampler"/>) into the stop.
/// Dressing plus the lever; the <see cref="RailEncounterController"/> runs it on the server. The node stays at the origin.
/// </summary>
public partial class SpurSwitch : Node3D
{
    public const float SpurLength = 8f;
    /// <summary>A runner on the spur hits the stop with its middle this far short of the spur's end.</summary>
    public const float EndClear = 0.9f;
    /// <summary>The lever stands this far short of the points: on floor 12 that puts it past the jump's landing. A rider throws it from the seat through the seated window (<see cref="SeatedSwitchRules"/>), with no aiming.</summary>
    public const float LeverLead = 2f;
    /// <summary>The stop beam's middle stands this far short of the spur's end, and it is this deep along the spur.</summary>
    const float StopBack = 0.25f, StopDepth = 0.3f;

    /// <summary>The party line's S beside the points.</summary>
    public float PointsS { get; init; }
    /// <summary>The enemy line's S at the points (where the spur leaves it).</summary>
    public float EnemyPointsS { get; private set; }
    public TrackSwitch Lever { get; private set; } = null!;
    /// <summary>The lever's wire name ("spur:{piece}"), set by the builder.</summary>
    public string LeverRef { get; init; } = "";
    /// <summary>The spur's own track, from the points (S = 0) to its end.</summary>
    public TrackSampler Sampler { get; private set; } = null!;
    /// <summary>The spur's S where a runner's middle meets the stop.</summary>
    public float CrashS => Mathf.Max(0f, Sampler.Length - EndClear);

    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.3f, 0.2f, 0.11f), Roughness = 0.9f };
    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.75f, Roughness = 0.5f };

    /// <summary>
    /// Lay the spur and stand the lever (inside the tree). Returns the spur's path and the lever's place, for clearing timber out of the way.
    /// <paramref name="rockAt"/> says whether a world point lies in rock: where the full bend's sleeper ends or stop beam would
    /// (<see cref="SetPieceRules.SpurBendFor"/>), the spur bends only to its lane cell's wall face (<see cref="SetPieceRules.SpurBend"/>), so
    /// its rails, sleepers and stop never run into the wall.
    /// </summary>
    public List<Vector3> Build(TrackSampler party, TrackSampler enemy, LaneMap lanes, Material steel, Material sleeper, Func<Vector3, bool> rockAt)
    {
        EnemyPointsS = lanes.EnemyS(PointsS);
        var leverS = Mathf.Max(0f, PointsS - LeverLead);
        var frame = party.Frame(leverS);
        var x = frame.Basis.X.Normalized();
        var side = (enemy.Point(lanes.EnemyS(leverS)) - frame.Origin).Dot(x) >= 0f ? 1f : -1f; // between the lanes
        Lever = new TrackSwitch { Name = "Lever", PointsS = PointsS, LeverS = leverS, NetRef = LeverRef };
        AddChild(Lever);
        Lever.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + x * (side * RailEncounterController.LeverOut));

        // the spur: off the enemy line at the points, bending ever further out from the party's lane; only to the lane cell's wall
        // face where the full bend's sleepers or stop would touch rock
        List<Vector3> Lay(float bend)
        {
            var path = new List<Vector3>();
            for (var u = 0f; u <= SpurLength + 0.01f; u += 0.5f)
            {
                var e = EnemyPointsS + u;
                var t = u / SpurLength;
                path.Add(enemy.Point(e) + Outward(party, enemy, lanes, e) * (bend * t * t));
            }
            return path;
        }
        var (bend, rock, stillInRock) = SetPieceRules.SpurBendFor(
            b => Lay(b).Select(p => new System.Numerics.Vector2(p.X, p.Z)).ToList(),
            p => rockAt(new Vector3(p.X, 0f, p.Y)), DungeonBuilder.CellSize / 2f, StopBack, StopDepth);
        if (stillInRock)
            GD.PushError($"[rails] ERROR: spur switch at party S={PointsS:0.0}: its {bend:0.0} m bend still runs into rock");
        var points = Lay(bend);
        var spur = new RailTrack { Name = "Spur" };
        AddChild(spur);
        spur.Build(points, steel, sleeper);
        Sampler = new TrackSampler(spur);

        // the stop at the end: a beam on two iron posts across the spur, braced back
        var end = Sampler.Frame(Mathf.Max(0f, Sampler.Length - StopBack));
        var basis = end.Basis.Orthonormalized();
        Box(new Vector3(SetPieceRules.SpurStopHalf * 2f, 0.34f, StopDepth), new Transform3D(basis, end.Origin + basis.Y * 0.62f), _wood);
        foreach (var s in new[] { -1f, 1f })
        {
            Box(new Vector3(0.14f, 0.85f, 0.14f), new Transform3D(basis, end.Origin + basis.X * (s * RailTrack.Gauge / 2f) + basis.Y * 0.42f), _iron);
            Box(new Vector3(0.12f, 0.12f, 1f), new Transform3D(basis * new Basis(Vector3.Right, -0.6f), end.Origin + basis.X * (s * RailTrack.Gauge / 2f) + basis.Y * 0.35f - basis.Z * 0.4f), _wood);
        }
        points.Add(Lever.GlobalPosition);
        GD.Print($"[rails] spur switch: points at party S={PointsS:0.0} (enemy S={EnemyPointsS:0.0}), lever at S={leverS:0.0} side {side:+0;-0}, spur {Sampler.Length:0.0} m, bend {bend:0.0} m{(rock ? " (rock beyond)" : "")}");
        return points;
    }

    /// <summary>The level unit direction away from the party's lane at the enemy line's S <paramref name="e"/>.</summary>
    private static Vector3 Outward(TrackSampler party, TrackSampler enemy, LaneMap lanes, float e)
    {
        var inward = party.Point(lanes.PartyS(e)) - enemy.Point(e);
        inward.Y = 0f;
        return inward.LengthSquared() > 0.01f ? -inward.Normalized() : -enemy.Frame(e).Basis.X.Normalized();
    }

    private void Box(Vector3 size, Transform3D at, Material material)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        AddChild(mesh);
        mesh.GlobalTransform = at;
    }
}
