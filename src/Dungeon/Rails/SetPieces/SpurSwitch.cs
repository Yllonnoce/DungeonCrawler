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
    /// <summary>How far the spur's end stands out from the enemy lane's centre, away from the party's lane.</summary>
    public const float SpurOut = 1.8f;
    /// <summary>A runner on the spur hits the stop with its middle this far short of the spur's end.</summary>
    public const float EndClear = 0.9f;
    /// <summary>The lever stands this far short of the points: on floor 12 that puts it past the jump's landing. A rider throws it from the seat through the seated window (<see cref="SeatedSwitchRules"/>), with no aiming.</summary>
    public const float LeverLead = 2f;

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

    /// <summary>Lay the spur and stand the lever (inside the tree). Returns the spur's path and the lever's place, for clearing timber out of the way.</summary>
    public List<Vector3> Build(TrackSampler party, TrackSampler enemy, LaneMap lanes, Material steel, Material sleeper)
    {
        EnemyPointsS = lanes.EnemyS(PointsS);
        var leverS = Mathf.Max(0f, PointsS - LeverLead);
        var frame = party.Frame(leverS);
        var x = frame.Basis.X.Normalized();
        var side = (enemy.Point(lanes.EnemyS(leverS)) - frame.Origin).Dot(x) >= 0f ? 1f : -1f; // between the lanes
        Lever = new TrackSwitch { Name = "Lever", PointsS = PointsS, LeverS = leverS, NetRef = LeverRef };
        AddChild(Lever);
        Lever.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + x * (side * RailEncounterController.LeverOut));

        // the spur: off the enemy line at the points, bending ever further out from the party's lane
        var points = new List<Vector3>();
        for (var u = 0f; u <= SpurLength + 0.01f; u += 0.5f)
        {
            var e = EnemyPointsS + u;
            var at = enemy.Point(e);
            var inward = party.Point(lanes.PartyS(e)) - at;
            inward.Y = 0f;
            var outward = inward.LengthSquared() > 0.01f ? -inward.Normalized() : -enemy.Frame(e).Basis.X.Normalized();
            var t = u / SpurLength;
            points.Add(at + outward * (SpurOut * t * t));
        }
        var spur = new RailTrack { Name = "Spur" };
        AddChild(spur);
        spur.Build(points, steel, sleeper);
        Sampler = new TrackSampler(spur);

        // the stop at the end: a beam on two iron posts across the spur, braced back
        var end = Sampler.Frame(Mathf.Max(0f, Sampler.Length - 0.25f));
        var basis = end.Basis.Orthonormalized();
        Box(new Vector3(1.6f, 0.34f, 0.3f), new Transform3D(basis, end.Origin + basis.Y * 0.62f), _wood);
        foreach (var s in new[] { -1f, 1f })
        {
            Box(new Vector3(0.14f, 0.85f, 0.14f), new Transform3D(basis, end.Origin + basis.X * (s * RailTrack.Gauge / 2f) + basis.Y * 0.42f), _iron);
            Box(new Vector3(0.12f, 0.12f, 1f), new Transform3D(basis * new Basis(Vector3.Right, -0.6f), end.Origin + basis.X * (s * RailTrack.Gauge / 2f) + basis.Y * 0.35f - basis.Z * 0.4f), _wood);
        }
        points.Add(Lever.GlobalPosition);
        GD.Print($"[rails] spur switch: points at party S={PointsS:0.0} (enemy S={EnemyPointsS:0.0}), lever at S={leverS:0.0} side {side:+0;-0}, spur {Sampler.Length:0.0} m");
        return points;
    }

    private void Box(Vector3 size, Transform3D at, Material material)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        AddChild(mesh);
        mesh.GlobalTransform = at;
    }
}
