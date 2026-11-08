using DungeonCrawler.Combat;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>A Rare or Legendary pickup's name in its rarity colour, beside the portraits, for <see cref="FeedbackRules.ToastSeconds"/>.</summary>
public partial class LootToast : Label
{
    private float _left;
    private Color _color = Colors.White;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeFontSizeOverride("font_size", 18);
        AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        AddThemeConstantOverride("outline_size", 4);
        Visible = false;
    }

    public void Show(string text, Color color) { Text = text; _color = color; Modulate = color; _left = FeedbackRules.ToastSeconds; Visible = true; }

    public override void _Process(double delta)
    {
        if (_left <= 0f) return;
        _left -= (float)delta;
        if (_left < 0.6f) Modulate = _color with { A = Math.Max(0f, _left / 0.6f) };
        if (_left <= 0f) Visible = false;
    }
}
