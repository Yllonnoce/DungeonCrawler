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
    /// <summary>Each brace's middle stands this far on from the beam, and it is this long, tilted this far (radians) down to its foot.</summary>
    const float BraceBack = 0.4f, BraceLength = 1f, BraceTilt = 0.6f;
    static readonly SpurStopShape StopShape = new(StopBack, StopDepth, BraceBack + BraceLength / 2f * MathF.Cos(BraceTilt));

    /// <summary>The party line's S beside the points.</summary>
    public float PointsS { get; init; }
    /// <summary>
    /// The lever stands <see cref="SplitRules.LeverPast"/> past the points instead (plan 15, Ruling 43: <see cref="LeverLead"/> short of
    /// them lies on the exit bridge, over the lava), and its seated window keeps its start, <see cref="LeverLead"/> +
    /// <see cref="SeatedSwitchRules.WindowAhead"/> short of the points, ending at them.
    /// </summary>
    public bool LeverPastPoints { get; init; }
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
    /// <paramref name="rockAt"/> says whether a world point lies in rock: the spur bends as far as it can (<see cref="SetPieceRules.PickSpurBend"/>)
    /// with its sleepers, stop and braces clear of the rock and its end clear of the main line, and its stop beam cut back clear of the
    /// main line's sleepers.
    /// </summary>
    public List<Vector3> Build(TrackSampler party, TrackSampler enemy, LaneMap lanes, Material steel, Material sleeper, Func<Vector3, bool> rockAt)
    {
        EnemyPointsS = lanes.EnemyS(PointsS);
        var leverS = LeverPastPoints ? Mathf.Min(party.Length, PointsS + SplitRules.LeverPast) : Mathf.Max(0f, PointsS - LeverLead);
        var frame = party.Frame(leverS);
        var x = frame.Basis.X.Normalized();
        var side = (enemy.Point(lanes.EnemyS(leverS)) - frame.Origin).Dot(x) >= 0f ? 1f : -1f; // between the lanes
        Lever = new TrackSwitch
        {
            Name = "Lever", PointsS = PointsS, LeverS = leverS, NetRef = LeverRef,
            WindowStartS = LeverPastPoints ? PointsS - LeverLead - SeatedSwitchRules.WindowAhead : float.NaN,
        };
        AddChild(Lever);
        Lever.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + x * (side * RailEncounterController.LeverOut));

        // the spur: off the enemy line at the points, bending ever further out from the party's lane, as far as the rock and the main
        // line let it (PickSpurBend)
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
        static System.Numerics.Vector2 Flat(Vector3 p) => new(p.X, p.Z);
        var main = new List<System.Numerics.Vector2>();
        for (var e = EnemyPointsS; e <= Mathf.Min(enemy.Length, EnemyPointsS + 2f * SpurLength); e += 0.1f) main.Add(Flat(enemy.Point(e))); // fine chords: a curve's 0.5 m chord sags 4 mm
        var pick = SetPieceRules.PickSpurBend(b => Lay(b).Select(Flat).ToList(), main, p => rockAt(new Vector3(p.X, 0f, p.Y)),
            DungeonBuilder.CellSize / 2f, StopShape);
        var bend = pick.Bend;
        if (pick.NoFit)
            GD.PushError($"[rails] ERROR: spur switch at party S={PointsS:0.0}: no bend from {SetPieceRules.SpurBendMax:0.00} m down to {bend:0.00} m keeps clear of the rock and the main line; laid at {bend:0.00} m");
        var points = Lay(bend);
        var spur = new RailTrack { Name = "Spur" };
        AddChild(spur);
        spur.Build(points, steel, sleeper);
        Sampler = new TrackSampler(spur);

        // the stop at the end: a beam on two iron posts across the spur, braced back
        // (the beam reaches pick.Stop.Outer away from the main line and pick.Stop.Inner toward it: the footprint's numbers)
        var end = Sampler.Frame(Mathf.Max(0f, Sampler.Length - StopBack));
        var basis = end.Basis.Orthonormalized();
        var stop = pick.Stop;
        var away = basis.X.X * stop.Across.X + basis.X.Z * stop.Across.Y >= 0f ? 1f : -1f;
        Box(new Vector3(stop.Inner + stop.Outer, 0.34f, StopDepth),
            new Transform3D(basis, end.Origin + basis.X * (away * (stop.Outer - stop.Inner) / 2f) + basis.Y * 0.62f), _wood);
        foreach (var s in new[] { -1f, 1f })
        {
            Box(new Vector3(0.14f, 0.85f, 0.14f), new Transform3D(basis, end.Origin + basis.X * (s * RailTrack.Gauge / 2f) + basis.Y * 0.42f), _iron);
            Box(new Vector3(0.12f, 0.12f, BraceLength), new Transform3D(basis * new Basis(Vector3.Right, -BraceTilt), end.Origin + basis.X * (s * RailTrack.Gauge / 2f) + basis.Y * 0.35f - basis.Z * BraceBack), _wood);
        }
        points.Add(Lever.GlobalPosition);
        GD.Print($"[rails] spur switch: points at party S={PointsS:0.0} (enemy S={EnemyPointsS:0.0}), lever at S={leverS:0.0}{(LeverPastPoints ? $" (past the points: the exit bridge; the seated window from S={Lever.WindowStartS:0.0})" : "")} side {side:+0;-0}, spur {Sampler.Length:0.0} m, bend {bend:0.00} m{(pick.BentIn ? " (rock beyond)" : "")}, stop -{stop.Inner:0.00}/+{stop.Outer:0.00} m, sleeper gap {pick.SleeperGap:0.00} m");
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
