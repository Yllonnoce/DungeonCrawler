using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// An open flame: glowing coals, a plume of additive billboard particles and a flickering light. Used on braziers,
/// campfires and torches; built in code so it needs no scene or texture files.
/// </summary>
public partial class Fire : Node3D
{
    public float Radius { get; set; } = 0.35f;
    public float Height { get; set; } = 1.0f;
    public float LightEnergy { get; set; } = 2.2f;
    public float LightRange { get; set; } = 9f;
    public Color Tint { get; set; } = new(1f, 0.62f, 0.25f);

    private OmniLight3D _light = null!;
    private float _phase;
    private static Texture2D? _puff;
    private static readonly Random Rng = new();

    public static Fire Create(float radius, float height, float energy, float range)
        => new() { Radius = radius, Height = height, LightEnergy = energy, LightRange = range };

    public override void _Ready()
    {
        _phase = (float)Rng.NextDouble() * 10f;
        var coals = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = Radius * 0.9f, Height = Radius * 0.6f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.08f, 0.02f), EmissionEnabled = true, Emission = new Color(1f, 0.35f, 0.05f), EmissionEnergyMultiplier = 2.5f, Roughness = 1f },
            Name = "Coals",
        };
        AddChild(coals);

        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 0.95f, 0.55f, 0.95f));
        gradient.SetColor(1, new Color(0.55f, 0.08f, 0.0f, 0f));
        gradient.AddPoint(0.35f, new Color(1f, 0.5f, 0.1f, 0.85f));
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 14f,
            InitialVelocityMin = Height * 0.9f, InitialVelocityMax = Height * 1.5f,
            Gravity = new Vector3(0, Height * 1.2f, 0),
            ScaleMin = 0.55f, ScaleMax = 1.0f,
            ColorRamp = new GradientTexture1D { Gradient = gradient },
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Radius * 0.6f,
            AngleMin = -180f, AngleMax = 180f,
        };
        var flame = new GpuParticles3D
        {
            Name = "Flame", Amount = 26, Lifetime = 0.75, Randomness = 0.4f, ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(Radius * 1.3f, Radius * 1.8f) },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true, AlbedoTexture = Puff(), DisableReceiveShadows = true, NoDepthTest = false,
            },
            Position = new Vector3(0, Radius * 0.2f, 0),
        };
        AddChild(flame);

        _light = new OmniLight3D { LightColor = Tint, LightEnergy = LightEnergy, OmniRange = LightRange, OmniAttenuation = 1.3f, ShadowEnabled = false, Position = new Vector3(0, Height * 0.5f, 0), Name = "Light" };
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        _phase += (float)delta;
        var flicker = 0.86f + 0.09f * Mathf.Sin(_phase * 9.3f) + 0.05f * Mathf.Sin(_phase * 23.7f + 1.3f);
        _light.LightEnergy = LightEnergy * flicker;
    }

    /// <summary>A soft radial blob, drawn once and shared.</summary>
    public static Texture2D Puff()
    {
        if (_puff != null) return _puff;
        const int n = 32;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var dx = (x + 0.5f) / n - 0.5f;
                var dy = (y + 0.5f) / n - 0.5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                var a = Mathf.Clamp(1f - d, 0f, 1f);
                a = a * a * (3f - 2f * a);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _puff = ImageTexture.CreateFromImage(img);
        return _puff;
    }
}

/// <summary>An iron bowl on a stand, burning. Flanks gates.</summary>
public partial class Brazier : StaticBody3D
{
    public override void _Ready()
    {
        CollisionLayer = 1;
        CollisionMask = 0;
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.7f, Roughness = 0.55f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.36f, Height = 0.06f }, MaterialOverride = iron, Position = new Vector3(0, 0.03f, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.85f }, MaterialOverride = iron, Position = new Vector3(0, 0.45f, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.22f, Height = 0.34f, RadialSegments = 16 }, MaterialOverride = iron, Position = new Vector3(0, 1.02f, 0) });
        for (var i = 0; i < 3; i++)
        {
            var leg = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.5f }, MaterialOverride = iron };
            var a = i * Mathf.Tau / 3f;
            leg.Position = new Vector3(Mathf.Cos(a) * 0.22f, 0.66f, Mathf.Sin(a) * 0.22f);
            leg.RotateZ(Mathf.Cos(a) * 0.5f);
            leg.RotateX(-Mathf.Sin(a) * 0.5f);
            AddChild(leg);
        }
        var fire = Fire.Create(0.3f, 0.9f, 2.4f, 10f);
        fire.Position = new Vector3(0, 1.16f, 0);
        AddChild(fire);
        AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.4f, Height = 1.3f }, Position = new Vector3(0, 0.65f, 0) });
    }
}
