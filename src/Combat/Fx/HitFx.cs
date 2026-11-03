using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
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

    /// <summary>The name of an arrow node drawn in an enemy's model (Godot suffixes repeats: StuckArrow2, ...).</summary>
    public const string StuckArrowName = "StuckArrow";

    /// <summary>How far the arrowhead sits past the point where the arrow met the body.</summary>
    public const float ArrowBuried = 0.18f;

    private const float ArrowLength = 0.55f;

    private static BoxMesh? _shaftMesh, _fletchMesh;
    private static StandardMaterial3D? _shaftMat, _fletchMat;

    /// <summary>
    /// An arrow left standing in <paramref name="enemy"/>: a Node3D named StuckArrow under its <see cref="Enemy.Model"/>
    /// (so it rides the body's transform), its tip <see cref="ArrowBuried"/> past <paramref name="worldAt"/> along the flight
    /// <paramref name="direction"/> (so its centre is half the length minus that back), pointing along the flight, 0.55 m long in world units whatever
    /// the model's scale. Nothing for a stock capsule or a body that already shows <see cref="Enemy.MaxStuckArrows"/>.
    /// </summary>
    public static void StickArrow(Enemy enemy, Vector3 worldAt, Vector3 direction)
    {
        var model = enemy.Model;
        if (model == null || !GodotObject.IsInstanceValid(model) || !model.IsInsideTree() || enemy.StuckArrowCount >= Enemy.MaxStuckArrows) return;
        if (direction.LengthSquared() < 1e-6f) return;
        var dir = direction.Normalized();
        _shaftMesh ??= new BoxMesh { Size = new Vector3(0.025f, 0.025f, ArrowLength) };
        _fletchMesh ??= new BoxMesh { Size = new Vector3(0.07f, 0.012f, 0.09f) };
        _shaftMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.22f, 0.10f), Roughness = 0.9f };
        _fletchMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.88f, 0.87f, 0.84f), Roughness = 1f };
        var arrow = new Node3D { Name = StuckArrowName };
        // the arrow's -Z is its point; the fletching sits at the tail (+Z)
        arrow.AddChild(new MeshInstance3D { Name = "Shaft", Mesh = _shaftMesh, MaterialOverride = _shaftMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        arrow.AddChild(new MeshInstance3D { Name = "Fletching", Mesh = _fletchMesh, MaterialOverride = _fletchMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = new Vector3(0, 0, ArrowLength / 2f - 0.045f) });
        model.AddChild(arrow, forceReadableName: true);
        var up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
        // the global transform at unit scale: the local one under the scaled model comes out as 1 / model scale
        arrow.GlobalTransform = new Transform3D(Basis.LookingAt(dir, up), worldAt - dir * (ArrowLength / 2f - ArrowBuried)); // the tip, half a length ahead of the centre, ends ArrowBuried in
    }

    /// <summary>The slim body an arrow sinks into, as a share of the collision capsule's 0.45 m radius (times scale).</summary>
    public const float BodyRadius = 0.22f;

    /// <summary>
    /// Where an arrow that struck <paramref name="enemy"/>'s collision capsule at <paramref name="hitAt"/> would meet the body
    /// itself: the capsule is wider than most models, so an arrow left at the capsule's skin floats off a thin neck or a
    /// hunched back. Carries on along <paramref name="direction"/> to a vertical cylinder of <see cref="BodyRadius"/> times
    /// the profile scale around the enemy's axis (or to the line's closest approach to it, for a shot that grazes the edge).
    /// </summary>
    public static Vector3 BodyEntry(Enemy enemy, Vector3 hitAt, Vector3 direction)
    {
        var s = enemy.Profile.Scale;
        var dir = direction.LengthSquared() > 1e-6f ? direction.Normalized() : Vector3.Forward;
        var p = new Vector2(hitAt.X - enemy.GlobalPosition.X, hitAt.Z - enemy.GlobalPosition.Z);
        var d = new Vector2(dir.X, dir.Z);
        var a = d.Dot(d);
        if (a < 1e-6f) return hitAt; // straight down or up: no horizontal way in
        var r = BodyRadius * s;
        var b = 2f * p.Dot(d);
        var c = p.Dot(p) - r * r;
        var disc = b * b - 4f * a * c;
        var t = disc >= 0f ? (-b - Mathf.Sqrt(disc)) / (2f * a) : -b / (2f * a);
        return hitAt + dir * Mathf.Clamp(t, 0f, 0.45f * s);
    }

    private static bool Usable(Node? parent) => parent != null && GodotObject.IsInstanceValid(parent) && parent.IsInsideTree() && !parent.IsQueuedForDeletion();

    private static void Place(Node parent, OneShotFx fx, Vector3 at)
    {
        parent.AddChild(fx);
        fx.GlobalPosition = at;
        fx.Emitting = true;
    }
}
