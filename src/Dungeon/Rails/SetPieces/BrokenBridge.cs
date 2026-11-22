using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The broken bridge's dressing (plan 11, Ruling 18): the slag line's deck ends at its 4 m gap (<see cref="RailEncounterController.BreakGapS"/>),
/// its track cut over it. Splintered plank ends hang down over the gap from both edges, the two rails end in stubs bent
/// <see cref="StubBend"/>° down at the near edge, and a steady red lamp (an <see cref="OmniLight3D"/>, <see cref="LampRange"/> m,
/// energy <see cref="LampEnergy"/>, no shadow) stands on a stub post at the near edge on the party lane's side. So the break reads from
/// the switch back's lever (~25 m off, Ruling 19), the embers below light the gap from under the deck (<see cref="GlowRange"/> m,
/// energy <see cref="GlowEnergy"/>: the hanging boards, the far deck's broken face and the rock around it glow), their warm haze rises
/// out of it (a soft camera-facing glow, <see cref="HazeSize"/>, procedural), and a second red lamp stands at the far edge on the outer
/// side, the two framing the gap. Placed in the enemy track's frame at the near edge (−Z along the line, across the gap). Dressing only:
/// no collision (the deck's footings end where the gap begins). Every role builds the same, from the layout.
/// </summary>
public partial class BrokenBridge : Node3D
{
    /// <summary>The rail stubs are bent this far down (degrees).</summary>
    public const float StubBend = 30f;
    public const float LampRange = 6f, LampEnergy = 2f;
    /// <summary>The ember glow out of the gap: its reach and energy, and how far under the deck it shines from.</summary>
    public const float GlowRange = 8f, GlowEnergy = 4f, GlowDepth = 1.2f;
    /// <summary>The haze out of the gap: a round glow as wide as the deck, centred on the deck's level (its lower half is down in the gap).</summary>
    public const float HazeSize = 3.6f;
    /// <summary>The lamp's post stands this far from the slag line's middle, toward the party lane.</summary>
    const float PostOut = 1.6f;
    const float PostHeight = 1.3f;
    const float PlankThick = 0.08f, PlankWide = 0.55f;
    /// <summary>From the track to the rails' middle (a sleeper and half a rail: <see cref="Props.RailTrack"/>'s).</summary>
    const float RailMid = 0.18f;
    static readonly Vector3 RailSize = new(0.07f, 0.12f, 0.6f);
    /// <summary>The splintered board ends at each edge: (lateral place, length, droop in degrees), fixed so every peer draws the same.</summary>
    static readonly (float X, float Length, float Droop)[] NearEnds = { (-1.65f, 0.9f, 34f), (-1.05f, 0.45f, 18f), (-0.45f, 1.1f, 42f), (0.15f, 0.6f, 25f), (0.75f, 0.95f, 38f), (1.4f, 0.35f, 14f) };
    static readonly (float X, float Length, float Droop)[] FarEnds = { (-1.5f, 0.5f, 22f), (-0.9f, 0.85f, 36f), (-0.3f, 0.3f, 12f), (0.35f, 0.75f, 30f), (1.0f, 0.4f, 20f), (1.6f, 0.8f, 40f) };

    /// <summary>The gap's length along the line: the far edge is this far along −Z.</summary>
    public float GapLength { get; init; } = DungeonBuilder.CellSize;
    /// <summary>Which side of the slag line the party lane lies on: +1 this frame's +X.</summary>
    public int InnerSide { get; init; } = 1;

    /// <summary>The lamp's place in the world (a flow's sight check may aim at it).</summary>
    public Vector3 LampPoint => ToGlobal(new Vector3(InnerSide * PostOut, PostHeight + 0.12f, 0.3f));

    public override void _Ready()
    {
        var wood = DungeonBuilder.Material("weathered_planks", new Color(0.5f, 0.38f, 0.24f), 1.6f);
        var steel = DungeonBuilder.RailSteel();
        // the splintered deck boards: hinged at each edge's top, drooping into the gap
        foreach (var (x, length, droop) in NearEnds) Board(wood, new Vector3(x, -PlankThick / 2f, 0f), length, droop, way: -1f);
        foreach (var (x, length, droop) in FarEnds) Board(wood, new Vector3(x, -PlankThick / 2f, -GapLength), length, droop, way: 1f);
        // the rails end at the near edge, bent down
        foreach (var side in new[] { -1f, 1f })
        {
            var bend = Mathf.DegToRad(StubBend);
            var dir = new Vector3(0f, -Mathf.Sin(bend), -Mathf.Cos(bend));
            AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "RailStubL" : "RailStubR", Mesh = new BoxMesh { Size = RailSize }, MaterialOverride = steel,
                Transform = new Transform3D(new Basis(Vector3.Right, -bend), new Vector3(side * Props.RailTrack.Gauge / 2f, RailMid, 0f) + dir * (RailSize.Z / 2f)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        // the red lamps on their stub posts, steady: at the near edge on the deck short of the gap (the party lane's side), and at the far
        // edge on the outer side
        Lamp(wood, "", new Vector3(InnerSide * PostOut, 0f, 0.3f));
        Lamp(wood, "Far", new Vector3(-InnerSide * PostOut, 0f, -GapLength - 0.3f));
        // the embers' glow up through the gap, from under the deck: no shadow, so it reaches the boards' undersides and the broken faces
        var glow = EmberGlow("GapGlow");
        glow.Position = new Vector3(0f, -GlowDepth, -GapLength / 2f);
        AddChild(glow);
        // and the embers' warm haze rising out of it: brightest at the deck's level, fading to nothing at its rim (additive, turned to the eye
        // about the vertical, not lost in the floor's haze; the decks hide its lower half from afar)
        var haze = EmberHaze("GapHaze", HazeSize);
        haze.Position = new Vector3(0f, 0f, -GapLength / 2f);
        AddChild(haze);
    }

    /// <summary>The embers' glow up out of a drop (<see cref="GlowRange"/>, <see cref="GlowEnergy"/>, no shadow); the cavern's gone decks and the slag line's drop glow as this gap does.</summary>
    internal static OmniLight3D EmberGlow(string name) => new()
    {
        Name = name, LightColor = new Color(1f, 0.45f, 0.15f), LightEnergy = GlowEnergy, OmniRange = GlowRange, ShadowEnabled = false,
    };

    /// <summary>
    /// The embers' warm haze rising out of a drop, <paramref name="size"/> across: a round additive glow, brightest at its middle, turned to the
    /// eye about the vertical, not lost in the floor's haze. Centre it on the deck's level (its lower half is down in the drop).
    /// </summary>
    internal static MeshInstance3D EmberHaze(string name, float size)
    {
        var haze = new GradientTexture2D
        {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, 0.45f, 1f },
                Colors = new[] { new Color(1f, 0.5f, 0.16f, 0.75f), new Color(1f, 0.32f, 0.07f, 0.28f), new Color(1f, 0.25f, 0.05f, 0f) },
            },
        };
        return new MeshInstance3D
        {
            Name = name, Mesh = new QuadMesh { Size = new Vector2(size, size) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY, CullMode = BaseMaterial3D.CullModeEnum.Disabled, DisableFog = true, AlbedoTexture = haze,
            },
        };
    }

    /// <summary>A red lamp on a stub post standing at <paramref name="post"/> (its foot, on the deck): the post, the emissive lamp and its light.</summary>
    private void Lamp(Material wood, string name, Vector3 post)
    {
        var red = new Color(1f, 0.08f, 0.04f);
        AddChild(new MeshInstance3D { Name = $"{name}LampPost", Mesh = new BoxMesh { Size = new Vector3(0.12f, PostHeight, 0.12f) }, MaterialOverride = wood, Position = post + Vector3.Up * (PostHeight / 2f) });
        AddChild(new MeshInstance3D
        {
            Name = $"{name}Lamp", Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.24f, 0.2f) }, Position = post + Vector3.Up * (PostHeight + 0.12f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = red, EmissionEnabled = true, Emission = red, EmissionEnergyMultiplier = 3f },
        });
        AddChild(new OmniLight3D { Name = $"{name}LampLight", LightColor = red, LightEnergy = LampEnergy, OmniRange = LampRange, ShadowEnabled = false, Position = post + Vector3.Up * (PostHeight + 0.2f) });
    }

    /// <summary>One broken board: hinged at <paramref name="hinge"/>, running <paramref name="way"/> along Z into the gap, drooping <paramref name="droop"/>°.</summary>
    private void Board(Material wood, Vector3 hinge, float length, float droop, float way)
    {
        var a = Mathf.DegToRad(droop);
        var dir = new Vector3(0f, -Mathf.Sin(a), way * Mathf.Cos(a));
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(PlankWide, PlankThick, length) }, MaterialOverride = wood,
            Transform = new Transform3D(new Basis(Vector3.Right, way < 0f ? -a : a), hinge + dir * (length / 2f)),
        });
    }
}
