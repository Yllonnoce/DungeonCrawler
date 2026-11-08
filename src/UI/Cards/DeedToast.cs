using Godot;

namespace DungeonCrawler.UI.Cards;

/// <summary>A deed earned: its name and what it unlocked, bottom-right, for four seconds. Never takes input.</summary>
public partial class DeedToast : PanelContainer
{
    public const float Seconds = 4f;
    private Label _name = null!, _unlock = null!;
    private float _left;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 12);
        AddChild(margin);
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddChild(v);
        var head = new Label { Text = "DEED", Modulate = new Color(0.79f, 0.64f, 0.29f) };
        head.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(head);
        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 20);
        v.AddChild(_name);
        _unlock = new Label { Modulate = new Color(0.8f, 0.8f, 0.8f) };
        v.AddChild(_unlock);
    }

    public void Show(string name, string unlockText)
    {
        _name.Text = name;
        _unlock.Text = $"Unlocked: {unlockText}";
        Modulate = Colors.White;
        _left = Seconds;
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (_left <= 0f) return;
        _left -= (float)delta;
        if (_left < 0.6f) Modulate = Colors.White with { A = Math.Max(0f, _left / 0.6f) };
        if (_left <= 0f) Visible = false;
    }
}
