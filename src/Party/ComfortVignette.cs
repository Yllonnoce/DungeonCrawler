using Godot;

namespace DungeonCrawler.Party;

/// <summary>
/// The headset's comfort vignette (<see cref="ComfortRules"/>): a quad hung on the head camera whose shader
/// (assets/shaders/comfort_vignette.gdshader) fills each eye's view and darkens it outside a clear cone about that eye's
/// forward axis (radius × 75° half-angle, measured in the eye's own view space, so the two eyes fuse). Hidden while
/// fully open, so a rider at rest, anyone on foot and the comfort setting Off draw nothing. Head-locked, it never moves
/// the view; only the darkness around it changes. Allocation-free per frame.
/// </summary>
public partial class ComfortVignette : MeshInstance3D
{
    public const string ShaderPath = "res://assets/shaders/comfort_vignette.gdshader";
    private static readonly StringName RadiusParam = "radius", FadeParam = "fade";

    private ShaderMaterial _material = null!;
    private float _radius = -1f, _fade = -1f;

    /// <summary>A vignette on <paramref name="camera"/> (the headset's head; a debug screenshot may hang one on a flat camera), hidden until <see cref="Apply"/> narrows it.</summary>
    public static ComfortVignette Attach(Camera3D camera)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath), RenderPriority = (int)Material.RenderPriorityMax };
        var v = new ComfortVignette
        {
            Name = "ComfortVignette",
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
            MaterialOverride = material,
            CastShadow = ShadowCastingSetting.Off,
            GIMode = GIModeEnum.Disabled,
            ExtraCullMargin = 16384f, // the shader places it over the whole view, wherever the quad's box is
            Position = new Vector3(0f, 0f, -0.5f),
            Visible = false,
            _material = material,
        };
        camera.AddChild(v);
        return v;
    }

    /// <summary>Show the vignette as <paramref name="s"/> stands; the shader's parameters are set only when they change.</summary>
    public void Apply(in ComfortState s)
    {
        var show = s.Radius < 0.999f || s.Fade > 0.001f;
        if (Visible != show) Visible = show;
        if (!show) return;
        if (s.Radius != _radius) { _radius = s.Radius; _material.SetShaderParameter(RadiusParam, _radius); }
        if (s.Fade != _fade) { _fade = s.Fade; _material.SetShaderParameter(FadeParam, _fade); }
    }
}
