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
    private static Texture2D? _drop;

    /// <summary>The soft radial blob the fire props draw with, shared.</summary>
    public static Texture2D Puff() => Fire.Puff();

    /// <summary>
    /// A short spatter of dark drops from the wound at <paramref name="at"/>: thrown mostly sideways toward the attacker
    /// with a small lift, slow enough to fall to the floor in front of the enemy instead of reaching the striker's eye.
    /// Hard-edged 2-4 cm dots that hold their colour most of their life, then fade.
    /// </summary>
    public static void Blood(Node parent, Vector3 at, Vector3 towardAttacker)
    {
        if (!Usable(parent)) return;
        var flat = new Vector3(towardAttacker.X, 0f, towardAttacker.Z);
        flat = flat.LengthSquared() > 1e-6f ? flat.Normalized() : Vector3.Forward;
        var dir = (flat * 0.85f + Vector3.Up * 0.2f).Normalized();
        if (_bloodRamp == null)
        {
            var g = new Gradient();
            var blood = new Color(0.30f, 0f, 0.01f);
            g.SetColor(0, blood);
            g.SetColor(1, new Color(blood, 0f));
            g.AddPoint(0.7f, blood);
            _bloodRamp = new GradientTexture1D { Gradient = g };
        }
        _bloodQuad ??= new QuadMesh { Size = new Vector2(0.035f, 0.035f) };
        _bloodMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoTexture = Drop(), DisableReceiveShadows = true,
        };
        var process = new ParticleProcessMaterial
        {
            Direction = dir, Spread = 45f,
            InitialVelocityMin = 1.0f, InitialVelocityMax = 2.2f,
            Gravity = new Vector3(0, -12f, 0),
            ScaleMin = 0.5f, ScaleMax = 1.2f,
            ColorRamp = _bloodRamp,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.04f,
        };
        var fx = new OneShotFx
        {
            Name = "Blood", Amount = 18, Lifetime = 0.4, Ttl = 0.8f, OneShot = true, Explosiveness = 1f,
            ProcessMaterial = process, DrawPass1 = _bloodQuad, MaterialOverride = _bloodMat,
        };
        Place(parent, fx, at);
    }

    /// <summary>A hard-edged round dot (alpha 1 inside r = 0.8, a one-pixel edge), drawn once and shared: a drop, not a glow.</summary>
    public static Texture2D Drop()
    {
        if (_drop != null) return _drop;
        const int n = 16;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var dx = (x + 0.5f) / n - 0.5f;
                var dy = (y + 0.5f) / n - 0.5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy) * n; // pixels from the centre
                var r = 0.8f * n * 0.5f;
                img.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(r + 0.5f - d, 0f, 1f)));
            }
        _drop = ImageTexture.CreateFromImage(img);
        return _drop;
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
