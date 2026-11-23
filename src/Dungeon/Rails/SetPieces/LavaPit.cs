using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 5's lava pit's dressing (plan 15 Task 4, spec B.4: dressed as the broken bridge's gap). The party track is cut bare over the pit
/// (<see cref="RailEncounterController.PitS"/> to <see cref="RailEncounterController.PitFarS"/>); here the two rails end in stubs bent
/// <see cref="BrokenBridge.StubBend"/>° down into the lava at the lip and rise again out of it at the far edge, a steady red lamp stands on a
/// stub post at each edge on the side away from the slag line (an <see cref="OmniLight3D"/>, <see cref="BrokenBridge.LampRange"/> m, energy
/// <see cref="BrokenBridge.LampEnergy"/>, no shadow), the lava's glow lights the pit from below and its warm haze rises out of it (the
/// broken bridge's <see cref="BrokenBridge.EmberGlow"/> and <see cref="BrokenBridge.EmberHaze"/>). Placed in the party track's frame at the
/// lip (−Z along the line, across the pit). Dressing only: no collision. Every role builds the same, from the layout.
/// </summary>
public partial class LavaPit : Node3D
{
    /// <summary>The lamps' posts stand this far from the party lane's middle, on the side away from the slag line.</summary>
    public const float PostOut = 1.6f;
    const float PostHeight = 1.3f;
    /// <summary>From the track to the rails' middle (a sleeper and half a rail: <see cref="Props.RailTrack"/>'s).</summary>
    const float RailMid = 0.18f;
    static readonly Vector3 RailSize = new(0.07f, 0.12f, 0.6f);

    /// <summary>The pit's length along the line: the far edge is this far along −Z.</summary>
    public float PitLength { get; init; } = DungeonBuilder.CellSize;
    /// <summary>Which side of the party lane the slag line lies on: +1 this frame's +X. The lamps stand on the other.</summary>
    public int SlagSide { get; init; } = 1;

    /// <summary>The near lamp's place in the world (the sight check from the lever aims at it).</summary>
    public Vector3 LampPoint => ToGlobal(new Vector3(-SlagSide * PostOut, PostHeight + 0.12f, 0.3f));

    public override void _Ready()
    {
        var wood = DungeonBuilder.Material("weathered_planks", new Color(0.5f, 0.38f, 0.24f), 1.6f);
        var steel = DungeonBuilder.RailSteel();
        var bend = Mathf.DegToRad(BrokenBridge.StubBend);
        foreach (var (edge, way, tag) in new[] { (0f, -1f, "Near"), (-PitLength, 1f, "Far") })
            foreach (var side in new[] { -1f, 1f })
            {
                // into the pit from its edge: forward (−Z) at the lip, back (+Z) at the far edge, bent down
                var dir = new Vector3(0f, -Mathf.Sin(bend), way * Mathf.Cos(bend));
                AddChild(new MeshInstance3D
                {
                    Name = $"{tag}RailStub{(side < 0 ? "L" : "R")}", Mesh = new BoxMesh { Size = RailSize }, MaterialOverride = steel,
                    Transform = new Transform3D(new Basis(Vector3.Right, way < 0f ? -bend : bend), new Vector3(side * Props.RailTrack.Gauge / 2f, RailMid, edge) + dir * (RailSize.Z / 2f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        Lamp(wood, "", new Vector3(-SlagSide * PostOut, 0f, 0.3f));
        Lamp(wood, "Far", new Vector3(-SlagSide * PostOut, 0f, -PitLength - 0.3f));
        var glow = BrokenBridge.EmberGlow("PitGlow");
        glow.Position = new Vector3(0f, -BrokenBridge.GlowDepth, -PitLength / 2f);
        AddChild(glow);
        var haze = BrokenBridge.EmberHaze("PitHaze", BrokenBridge.HazeSize);
        haze.Position = new Vector3(0f, 0f, -PitLength / 2f);
        AddChild(haze);
    }

    /// <summary>A red lamp on a stub post standing at <paramref name="post"/> (its foot, on the floor beside the lane): the post, the emissive lamp and its light.</summary>
    private void Lamp(Material wood, string name, Vector3 post)
    {
        var red = new Color(1f, 0.08f, 0.04f);
        AddChild(new MeshInstance3D { Name = $"{name}LampPost", Mesh = new BoxMesh { Size = new Vector3(0.12f, PostHeight, 0.12f) }, MaterialOverride = wood, Position = post + Vector3.Up * (PostHeight / 2f) });
        AddChild(new MeshInstance3D
        {
            Name = $"{name}Lamp", Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.24f, 0.2f) }, Position = post + Vector3.Up * (PostHeight + 0.12f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = red, EmissionEnabled = true, Emission = red, EmissionEnergyMultiplier = 3f },
        });
        AddChild(new OmniLight3D { Name = $"{name}LampLight", LightColor = red, LightEnergy = BrokenBridge.LampEnergy, OmniRange = BrokenBridge.LampRange, ShadowEnabled = false, Position = post + Vector3.Up * (PostHeight + 0.2f) });
    }
}
