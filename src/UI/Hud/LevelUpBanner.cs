using DungeonCrawler.Combat;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>"Brand reaches level 7" across the top for <see cref="FeedbackRules.BannerSeconds"/>.</summary>
public partial class LevelUpBanner : Label
{
    private float _left;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        HorizontalAlignment = HorizontalAlignment.Center;
        Modulate = new Color(1f, 0.85f, 0.3f);
        AddThemeFontSizeOverride("font_size", 28);
        AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        AddThemeConstantOverride("outline_size", 6);
        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0; AnchorBottom = 0;
        OffsetLeft = -360; OffsetRight = 360; OffsetTop = 120; OffsetBottom = 160;
        Visible = false;
    }

    public void Show(string text) { Text = text; _left = FeedbackRules.BannerSeconds; Visible = true; Modulate = new Color(1f, 0.85f, 0.3f); }

    public override void _Process(double delta)
    {
        if (_left <= 0f) return;
        _left -= (float)delta;
        if (_left < 0.5f) Modulate = new Color(1f, 0.85f, 0.3f, Math.Max(0f, _left / 0.5f));
        if (_left <= 0f) Visible = false;
    }
}
