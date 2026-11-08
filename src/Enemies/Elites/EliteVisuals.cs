using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>
/// Every affix has a tell you can read before the first blow (Depths spec, section 2): tints, dust, a shell, spines, rings, a trail, a dome.
/// Built from primitives in shared materials; no shadows, no lights. <see cref="Dress"/> runs once in Enemy._Ready, <see cref="Tick"/> every frame.
/// </summary>
public static class EliteVisuals
{
    private static readonly Color SwiftTint = new(1f, 0.9f, 0.3f), IronTint = new(0.45f, 0.45f, 0.48f);
    private static readonly StandardMaterial3D Shell = Unshaded(new Color(0.4f, 0.6f, 1f, 0.22f));
    private static readonly StandardMaterial3D Dome = Unshaded(new Color(0.7f, 0.7f, 0.9f, 0.12f), cullDisabled: true);
    private static readonly StandardMaterial3D Seam = Emissive(new Color(0.2f, 0.9f, 0.3f));
    private static readonly StandardMaterial3D Ring = Emissive(new Color(0.6f, 0.3f, 0.9f));
    private static readonly StandardMaterial3D Iron = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.7f, Roughness = 0.55f };
    private static readonly StandardMaterial3D Dust = Particle(new Color(0.8f, 0.72f, 0.4f, 0.35f));
    private static readonly StandardMaterial3D Trail = Particle(new Color(0.6f, 0.2f, 0.9f, 0.45f));

    private static StandardMaterial3D Unshaded(Color c, bool cullDisabled = false) => new()
    {
        AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = cullDisabled ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back, NoDepthTest = false,
    };
    private static StandardMaterial3D Emissive(Color c) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private static StandardMaterial3D Particle(Color c) => new()
    {
        AlbedoColor = c, AlbedoTexture = Dungeon.Props.Fire.Puff(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>Swift leans the body yellow, Juggernaut iron grey; the rest keep the creature's colour.</summary>
    public static Color Tint(Color baseColor, IReadOnlyList<EliteAffix> affixes)
    {
        var c = baseColor;
        if (affixes.Contains(EliteAffix.Swift)) c = c.Lerp(SwiftTint, 0.4f);
        if (affixes.Contains(EliteAffix.Juggernaut)) c = c.Lerp(IronTint, 0.6f);
        return c;
    }

    public static void Dress(Enemy e)
    {
        var s = e.Profile.Scale;
        var a = e.Profile.Affixes;
        if (a.Contains(EliteAffix.Swift))
            e.AddChild(new GpuParticles3D
            {
                Name = "EliteDust", Amount = 14, Lifetime = 0.9, Emitting = false, Explosiveness = 0f,
                ProcessMaterial = new ParticleProcessMaterial { Direction = new Vector3(0, 1, 0), Spread = 60f, InitialVelocityMin = 0.4f, InitialVelocityMax = 1f, Gravity = new Vector3(0, -0.4f, 0), ScaleMin = 0.3f, ScaleMax = 0.7f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.3f * s },
                DrawPass1 = new QuadMesh { Size = new Vector2(0.4f, 0.4f) }, MaterialOverride = Dust, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, 0.1f, 0),
            });
        if (a.Contains(EliteAffix.Warded))
            e.AddChild(new MeshInstance3D { Name = "EliteWard", Mesh = new SphereMesh { Radius = 1.15f * s, Height = 1.8f * s, RadialSegments = 20, Rings = 10 }, MaterialOverride = Shell, Position = new Vector3(0, 0.9f * s, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (a.Contains(EliteAffix.Splitting))
            e.AddChild(new MeshInstance3D { Name = "EliteSeam", Mesh = new BoxMesh { Size = new Vector3(0.05f, 1.2f * s, 0.05f) }, MaterialOverride = Seam, Position = new Vector3(0, 1f * s, -0.42f * s), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (a.Contains(EliteAffix.Thorned))
        {
            var spines = new Node3D { Name = "EliteSpines", Position = new Vector3(0, 1.1f * s, 0) };
            for (var i = 0; i < 8; i++)
            {
                var ang = i * Mathf.Tau / 8f;
                var spine = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.05f, Height = 0.45f, RadialSegments = 6 }, MaterialOverride = Iron, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                spine.Position = dir * (0.45f * s + 0.2f);
                spine.Rotation = new Vector3(0, -ang, -Mathf.Pi / 2f); // the cylinder's up axis points outward
                spines.AddChild(spine);
            }
            e.AddChild(spines);
        }
        if (a.Contains(EliteAffix.Cursing))
            e.AddChild(new GpuParticles3D
            {
                Name = "EliteTrail", Amount = 20, Lifetime = 1.2, Emitting = true,
                ProcessMaterial = new ParticleProcessMaterial { Direction = new Vector3(0, 0, 1), Spread = 25f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.6f, Gravity = Vector3.Zero, ScaleMin = 0.3f, ScaleMax = 0.6f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.25f * s },
                DrawPass1 = new QuadMesh { Size = new Vector2(0.35f, 0.35f) }, MaterialOverride = Trail, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, 1f * s, 0.3f * s),
            });
        if (a.Contains(EliteAffix.Shrouded))
            e.AddChild(new MeshInstance3D { Name = "EliteDome", Mesh = new SphereMesh { Radius = EliteRules.ShroudRadius, Height = EliteRules.ShroudRadius * 2f, RadialSegments = 32, Rings = 16 }, MaterialOverride = Dome, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (a.Contains(EliteAffix.Hexed))
        {
            e.AddChild(new MeshInstance3D { Name = "EliteRingA", Mesh = new TorusMesh { InnerRadius = 0.55f * s, OuterRadius = 0.6f * s, Rings = 24, RingSegments = 8 }, MaterialOverride = Ring, Position = new Vector3(0, 0.6f * s, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            e.AddChild(new MeshInstance3D { Name = "EliteRingB", Mesh = new TorusMesh { InnerRadius = 0.55f * s, OuterRadius = 0.6f * s, Rings = 24, RingSegments = 8 }, MaterialOverride = Ring, Position = new Vector3(0, 1.3f * s, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    private static float _clock;

    /// <summary>Per frame: the shell follows the ward, the rings turn, the dust follows the feet, the glows breathe. Yields the body emission to a hit flash.</summary>
    public static void Tick(Enemy e, float dt)
    {
        var a = e.Profile.Affixes;
        _clock += dt;
        if (a.Contains(EliteAffix.Warded) && e.GetNodeOrNull<MeshInstance3D>("EliteWard") is { } ward) ward.Visible = e.IsAlive && e.WardPoints > 0f;
        if (a.Contains(EliteAffix.Swift) && e.GetNodeOrNull<GpuParticles3D>("EliteDust") is { } dust) dust.Emitting = e.IsAlive && new Vector2(e.Velocity.X, e.Velocity.Z).Length() > 0.5f;
        if (a.Contains(EliteAffix.Cursing) && e.GetNodeOrNull<GpuParticles3D>("EliteTrail") is { } trail) trail.Emitting = e.IsAlive;
        if (a.Contains(EliteAffix.Shrouded) && e.GetNodeOrNull<MeshInstance3D>("EliteDome") is { } dome) dome.Visible = e.IsAlive;
        if (a.Contains(EliteAffix.Hexed))
        {
            if (e.GetNodeOrNull<MeshInstance3D>("EliteRingA") is { } ra) { ra.RotateY(1.5f * dt); ra.Visible = e.IsAlive; }
            if (e.GetNodeOrNull<MeshInstance3D>("EliteRingB") is { } rb) { rb.RotateY(-1.5f * dt); rb.Visible = e.IsAlive; }
        }
        if (!e.IsAlive || e.FlashLeft > 0f) return;
        Color? glow = null;
        if (a.Contains(EliteAffix.Vampiric) && e.VampFlash > 0f) glow = new Color(1f, 0.1f, 0.1f) * (e.VampFlash / 0.3f);
        else if (a.Contains(EliteAffix.Volatile)) glow = new Color(1f, 0.5f, 0.1f) * (0.5f + 0.5f * Mathf.Sin(_clock * 6f));
        if (glow is not { } g) return;
        e.BodyMaterial.EmissionEnabled = true; e.BodyMaterial.Emission = g;
        foreach (var m in e.ModelMaterials) { m.EmissionEnabled = true; m.Emission = g; }
    }
}
