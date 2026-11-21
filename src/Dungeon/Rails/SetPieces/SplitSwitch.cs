using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's split (plan 12, Rulings 4 and 5): a spur off the party line at its points into the crevasse beyond the slag line, and its deadly
/// lever. The spur follows exactly the path <see cref="RailController.Cross"/> blends along (a smoothstep over
/// <see cref="RailController.SwitchStretch"/> onto the slag line), then bends <see cref="SplitRules.SpurOut"/> further out over the rest of its
/// <see cref="SplitRules.SpurLength"/>, ending over the drop with its rail ends bent down. The lever (a <see cref="TrackSwitch"/>, deadly: a
/// human's E only, no hits) stands <see cref="SplitRules.LeverPast"/> past the points on the party lane's side away from the slag line, clear of
/// the spur, with a steady amber lamp on its post; a red lamp stands on a post at the lip beyond the slag line by the spur's end. Both lamps are
/// <see cref="OmniLight3D"/>s at <see cref="RailEncounterController.SignalRange"/> / <see cref="RailEncounterController.SignalEnergy"/> with no
/// shadow, lit through the dark (Ruling 16). The seated window opens at the points less the straight run before them, measured here on the track
/// (ruling R-bend, <see cref="WindowAhead"/>), and ends only when the lever is taken (<see cref="TrackSwitch.WindowEndS"/> +∞). Dressing plus the
/// lever, built the same on every role; the <see cref="RailEncounterController"/> judges it on the server. The node stays at the origin.
/// </summary>
public partial class SplitSwitch : Node3D
{
    /// <summary>The red lamp's post stands this far out from the slag line's middle at the spur's end: on its lip, the crevasse beyond.</summary>
    public const float LipOut = 1.7f;
    /// <summary>The lever's post keeps at least this far from the spur's middle line (a cart's half width, the post's, and some).</summary>
    public const float SpurClear = 1.4f;
    const float LampPostHeight = 1.6f;
    /// <summary>The rail ends at the spur's end bend this far down (degrees), as the broken bridge's do.</summary>
    const float StubBend = 30f;
    static readonly Vector3 RailSize = new(0.07f, 0.12f, 0.6f);
    const float RailMid = 0.18f;
    /// <summary>Where the lever may stand, nearest first: (metres inward from the party lane's middle, metres further along past <see cref="SplitRules.LeverPast"/>).</summary>
    static readonly (float Out, float Along)[] LeverSpots =
    {
        (RailEncounterController.LeverOut, 0f), (1.8f, 0f), (1.6f, 0f),
        (RailEncounterController.LeverOut, 0.5f), (1.8f, 0.5f), (1.6f, 0.5f), (RailEncounterController.LeverOut, 1f), (1.6f, 1f),
    };

    /// <summary>The party line's S at the points.</summary>
    public float PointsS { get; init; }
    /// <summary>The lever's wire name ("switch:{piece}"), set by the builder.</summary>
    public string LeverRef { get; init; } = "";
    /// <summary>The lever is offered now (its builder's <see cref="SplitRules.Offered"/>), on every peer.</summary>
    public Func<bool>? Offered { get; init; }
    /// <summary>A throw by this actor now is held to the points (its builder's <see cref="SplitRules.HoldPress"/>). Server and solo.</summary>
    public Func<Adventurer, bool>? Hold { get; init; }
    /// <summary>The cart's front is past the points now (the lever's prompt reads it).</summary>
    public Func<bool>? Past { get; init; }
    /// <summary>The lever may stand at this place: walkable floor, not a drop (the builder's layout).</summary>
    public Func<Vector3, bool>? Stands { get; init; }

    public TrackSwitch Lever { get; private set; } = null!;
    /// <summary>The spur's own track, from the points (S = 0) to its end.</summary>
    public TrackSampler Sampler { get; private set; } = null!;
    /// <summary>The straight run before the points, measured on the party track (<see cref="SplitRules.StraightBefore"/>).</summary>
    public float StraightBefore { get; private set; }
    /// <summary>How far before the points the seated window opens (ruling R-bend: <see cref="SplitRules.WindowReach"/>).</summary>
    public float WindowAhead => SplitRules.WindowReach(StraightBefore);
    /// <summary>The party line's S where the seated window opens.</summary>
    public float WindowStartS => PointsS - WindowAhead;
    /// <summary>A throw is held to the points from this far short of them (<see cref="SplitRules.HoldReach"/>: never less than the window).</summary>
    public float HoldReach => SplitRules.HoldReach(WindowAhead);
    /// <summary>The spur's end in the world (over the crevasse when live).</summary>
    public Vector3 SpurEnd { get; private set; }
    /// <summary>The lever found a place by the rules (<see cref="Stands"/>, clear of the spur), and which one (metres inward, metres past the points).</summary>
    public bool LeverPlaced { get; private set; }
    public float LeverOutUsed { get; private set; }
    public float LeverS { get; private set; }
    /// <summary>The two warning lamps' places in the world (a flow's sight check may aim at them).</summary>
    public Vector3 RedLampPoint { get; private set; }
    public Vector3 AmberLampPoint { get; private set; }

    private readonly StandardMaterial3D _iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.7f, Roughness = 0.5f };

    private static float Smooth(float x)
    {
        x = Mathf.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>Lay the spur and stand the lever and the lamps (inside the tree). Returns the spur's path and the posts' places, for clearing timber out of the way.</summary>
    public List<Vector3> Build(TrackSampler party, TrackSampler enemy, LaneMap lanes, Material steel, Material sleeper)
    {
        StraightBefore = SplitRules.StraightBefore(s => { var p = party.Point(s); return (p.X, p.Z); }, PointsS, SplitRules.WindowAhead);

        // the spur: Cross's own blend onto the slag line, then out over the crevasse
        var stretch = RailController.SwitchStretch;
        var path = new List<Vector3>();
        for (var u = 0f; u <= SplitRules.SpurLength + 0.01f; u += 0.5f)
        {
            var s = PointsS + u;
            var e = lanes.EnemyS(s);
            var slag = enemy.Point(e);
            var at = party.Point(s).Lerp(slag, Smooth(u / stretch));
            if (u > stretch) at += Outward(party, enemy, lanes, e) * (SplitRules.SpurOut * Smooth((u - stretch) / (SplitRules.SpurLength - stretch)));
            path.Add(at);
        }
        var spur = new RailTrack { Name = "Spur" };
        AddChild(spur);
        spur.Build(path, steel, sleeper);
        Sampler = new TrackSampler(spur);
        SpurEnd = Sampler.Point(Sampler.Length);

        // the rail ends bent down over the drop
        var end = Sampler.Frame(Sampler.Length).Orthonormalized();
        var bend = Mathf.DegToRad(StubBend);
        foreach (var side in new[] { -1f, 1f })
        {
            var dir = end.Basis * new Vector3(0f, -Mathf.Sin(bend), -Mathf.Cos(bend));
            var stub = new MeshInstance3D
            {
                Name = side < 0 ? "RailEndL" : "RailEndR", Mesh = new BoxMesh { Size = RailSize }, MaterialOverride = steel,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(stub);
            stub.GlobalTransform = new Transform3D(end.Basis * new Basis(Vector3.Right, -bend), end.Origin + end.Basis.X * (side * RailTrack.Gauge / 2f) + end.Basis.Y * RailMid + dir * (RailSize.Z / 2f));
        }

        // the red lamp at the lip beyond the slag line, by the spur's end
        var endE = lanes.EnemyS(PointsS + SplitRules.SpurLength);
        var lip = enemy.Point(endE) + Outward(party, enemy, lanes, endE) * LipOut;
        var red = new Color(1f, 0.08f, 0.04f);
        RedLampPoint = LampPost("SpurLamp", lip, red, LampPostHeight);

        // the lever: past the points on the party lane's side away from the slag line, clear of the spur
        Lever = PlaceLever(party, enemy, lanes, path);
        var amber = new Color(1f, 0.6f, 0.1f);
        AmberLampPoint = Lever.GlobalPosition + Vector3.Up * (TrackSwitch.PostHeight + 0.42f);
        Lever.AddChild(new MeshInstance3D { Name = "AmberStem", Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.26f, 0.06f) }, MaterialOverride = _iron, Position = Vector3.Up * (TrackSwitch.PostHeight + 0.22f) });
        Lever.AddChild(new MeshInstance3D
        {
            Name = "AmberLamp", Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 12, Rings = 6 }, Position = Vector3.Up * (TrackSwitch.PostHeight + 0.42f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = amber, EmissionEnabled = true, Emission = amber, EmissionEnergyMultiplier = 3f, Roughness = 0.4f },
        });
        Lever.AddChild(new OmniLight3D { Name = "AmberLight", LightColor = amber, LightEnergy = RailEncounterController.SignalEnergy, OmniRange = RailEncounterController.SignalRange, ShadowEnabled = false, Position = Vector3.Up * (TrackSwitch.PostHeight + 0.5f) });

        var clear = new List<Vector3>(path) { Lever.GlobalPosition, lip };
        return clear;
    }

    /// <summary>Away from the party lane across the slag line at enemy S <paramref name="e"/>, level.</summary>
    private static Vector3 Outward(TrackSampler party, TrackSampler enemy, LaneMap lanes, float e)
    {
        var o = enemy.Point(e) - party.Point(lanes.PartyS(e));
        o.Y = 0f;
        return o.LengthSquared() > 0.01f ? o.Normalized() : enemy.Frame(e).Basis.X.Normalized();
    }

    /// <summary>The lever at the first of <see cref="LeverSpots"/> that <see cref="Stands"/> allows and keeps <see cref="SpurClear"/> from the spur; else the first spot, unplaced.</summary>
    private TrackSwitch PlaceLever(TrackSampler party, TrackSampler enemy, LaneMap lanes, List<Vector3> spur)
    {
        Transform3D? chosen = null;
        float chosenS = PointsS + SplitRules.LeverPast, chosenOut = RailEncounterController.LeverOut;
        foreach (var (outBy, along) in LeverSpots)
        {
            var s = PointsS + SplitRules.LeverPast + along;
            var frame = party.Frame(s);
            var x = frame.Basis.X.Normalized();
            var towardSlag = (enemy.Point(lanes.EnemyS(s)) - frame.Origin).Dot(x) >= 0f ? 1f : -1f;
            var at = frame.Origin - x * (towardSlag * outBy);
            var place = new Transform3D(frame.Basis.Orthonormalized(), at);
            chosen ??= place;
            if (!Fits(at, x * -towardSlag, spur)) continue;
            chosen = place;
            chosenS = s;
            chosenOut = outBy;
            LeverPlaced = true;
            break;
        }
        LeverS = chosenS;
        LeverOutUsed = chosenOut;
        var lever = new TrackSwitch
        {
            Name = "Lever", PointsS = PointsS, LeverS = chosenS, Deadly = true, NetRef = LeverRef, OfferedNow = Offered, HoldThrow = Hold, SplitPast = Past,
            WindowStartS = WindowStartS, WindowEndS = float.PositiveInfinity,
        };
        AddChild(lever);
        lever.GlobalTransform = chosen!.Value;
        return lever;
    }

    /// <summary>The post (its 0.5 m box either side of <paramref name="at"/> across the line) stands on floor the builder allows and clear of the spur.</summary>
    private bool Fits(Vector3 at, Vector3 inward, List<Vector3> spur)
    {
        if (Stands != null && (!Stands(at) || !Stands(at + inward * 0.25f) || !Stands(at - inward * 0.25f))) return false;
        foreach (var p in spur)
        {
            float dx = p.X - at.X, dz = p.Z - at.Z;
            if (dx * dx + dz * dz < SpurClear * SpurClear) return false;
        }
        return true;
    }

    /// <summary>A steady lamp on its own post with its foot at <paramref name="foot"/>: the post, the emissive lamp and its light. Returns the lamp's place.</summary>
    private Vector3 LampPost(string name, Vector3 foot, Color color, float height)
    {
        var post = new Node3D { Name = name };
        AddChild(post);
        post.GlobalPosition = foot;
        post.AddChild(new MeshInstance3D { Name = "Post", Mesh = new BoxMesh { Size = new Vector3(0.12f, height, 0.12f) }, MaterialOverride = _iron, Position = Vector3.Up * (height / 2f) });
        post.AddChild(new MeshInstance3D
        {
            Name = "Lamp", Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 12, Rings = 6 }, Position = Vector3.Up * (height + 0.1f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 3f, Roughness = 0.4f },
        });
        post.AddChild(new OmniLight3D { Name = "Light", LightColor = color, LightEnergy = RailEncounterController.SignalEnergy, OmniRange = RailEncounterController.SignalRange, ShadowEnabled = false, Position = Vector3.Up * (height + 0.2f) });
        return foot + Vector3.Up * (height + 0.1f);
    }
}
