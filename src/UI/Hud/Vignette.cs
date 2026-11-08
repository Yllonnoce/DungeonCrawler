using DungeonCrawler.Combat;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>A red edge over the screen when the local member is hurt, scaled by the fraction lost, fading over <see cref="FeedbackRules.VignetteSeconds"/>.</summary>
public partial class Vignette : TextureRect
{
    public float Alpha { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        StretchMode = StretchModeEnum.Scale;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        var g = new Gradient();
        g.SetColor(0, new Color(0.6f, 0f, 0f, 0f));
        g.SetColor(1, new Color(0.6f, 0f, 0f, 1f));
        g.AddPoint(0.55f, new Color(0.6f, 0f, 0f, 0f));
        Texture = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 256, Height = 256 };
        Modulate = new Color(1, 1, 1, 0);
    }

    public void Hit(float fraction)
    {
        Alpha = Math.Clamp(Alpha + fraction * 1.5f, 0f, 0.85f);
        Modulate = new Color(1, 1, 1, Alpha);
    }

    public override void _Process(double delta)
    {
        if (Alpha <= 0f) return;
        Alpha = Math.Max(0f, Alpha - (float)delta * 0.85f / FeedbackRules.VignetteSeconds);
        Modulate = new Color(1, 1, 1, Alpha);
    }
}
