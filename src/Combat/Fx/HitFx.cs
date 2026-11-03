using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Combat.Fx;

/// <summary>
/// What a hit leaves in the air: blood from a melee blow, a coloured burst where a spell lands. Each is an
/// <see cref="OneShotFx"/> added to <c>parent</c> (the floor root) that frees itself. Local on every machine, never relayed.
/// </summary>
public static class HitFx
{
    private static QuadMesh? _bloodQuad, _burstQuad;
    private static StandardMaterial3D? _bloodMat, _burstMat;
    private static GradientTexture1D? _bloodRamp;

    /// <summary>The soft radial blob the fire props draw with, shared.</summary>
    public static Texture2D Puff() => Fire.Puff();

    /// <summary>
    /// A spray of dark red droplets at <paramref name="at"/>, thrown toward the attacker and falling. The throw is tilted
    /// upward so the drops arc down in front of the wound instead of flying flat into the striker's eye 1.5 m away.
    /// </summary>
    public static void Blood(Node parent, Vector3 at, Vector3 towardAttacker)
    {
        if (!Usable(parent)) return;
        var flat = new Vector3(towardAttacker.X, 0f, towardAttacker.Z);
        flat = flat.LengthSquared() > 1e-6f ? flat.Normalized() : Vector3.Forward;
        var dir = (flat * 0.5f + Vector3.Up * 0.85f).Normalized();
        if (_bloodRamp == null)
        {
            var g = new Gradient();
            g.SetColor(0, new Color(0.55f, 0.02f, 0.02f, 1f));
            g.SetColor(1, new Color(0.55f, 0.02f, 0.02f, 0f));
            _bloodRamp = new GradientTexture1D { Gradient = g };
        }
        _bloodQuad ??= new QuadMesh { Size = new Vector2(0.10f, 0.10f) };
        _bloodMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoTexture = Puff(), DisableReceiveShadows = true,
        };
        var process = new ParticleProcessMaterial
        {
            Direction = dir, Spread = 35f,
            InitialVelocityMin = 2.5f, InitialVelocityMax = 4.5f,
            Gravity = new Vector3(0, -9.8f, 0),
            ScaleMin = 0.4f, ScaleMax = 1f,
            ColorRamp = _bloodRamp,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.12f,
        };
        var fx = new OneShotFx
        {
            Name = "Blood", Amount = 24, Lifetime = 0.5, Ttl = 1.0f, OneShot = true, Explosiveness = 1f,
            ProcessMaterial = process, DrawPass1 = _bloodQuad, MaterialOverride = _bloodMat,
        };
        Place(parent, fx, at);
    }

    /// <summary>A burst of <paramref name="color"/> light and sparks where a spell landed, on an enemy or a wall.</summary>
    public static void SpellBurst(Node parent, Vector3 at, Color color)
    {
        if (!Usable(parent)) return;
        var g = new Gradient();
        g.SetColor(0, new Color(color.R, color.G, color.B, 0.9f));
        g.SetColor(1, new Color(color.R, color.G, color.B, 0f));
        _burstQuad ??= new QuadMesh { Size = new Vector2(0.10f, 0.10f) };
        _burstMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true, AlbedoTexture = Puff(), DisableReceiveShadows = true,
        };
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 180f,
            InitialVelocityMin = 1.5f, InitialVelocityMax = 3.5f,
            Gravity = Vector3.Zero,
            ColorRamp = new GradientTexture1D { Gradient = g },
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.12f,
        };
        var fx = new OneShotFx
        {
            Name = "SpellBurst", Amount = 32, Lifetime = 0.5, Ttl = 1.0f, OneShot = true, Explosiveness = 1f,
            ProcessMaterial = process, DrawPass1 = _burstQuad, MaterialOverride = _burstMat,
        };
        var light = new OmniLight3D { Name = "Flash", LightColor = color, LightEnergy = 3f, OmniRange = 4f, ShadowEnabled = false };
        fx.AddChild(light);
        fx.Flash = light;
        Place(parent, fx, at);
    }

    private static bool Usable(Node? parent) => parent != null && GodotObject.IsInstanceValid(parent) && parent.IsInsideTree() && !parent.IsQueuedForDeletion();

    private static void Place(Node parent, OneShotFx fx, Vector3 at)
    {
        parent.AddChild(fx);
        fx.GlobalPosition = at;
        fx.Emitting = true;
    }
}
