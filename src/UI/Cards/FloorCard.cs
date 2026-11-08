using DungeonCrawler.Adventure.Run;
using Godot;

namespace DungeonCrawler.UI.Cards;

/// <summary>The floor card on a descent: top-centre, four seconds (Esc dismisses), never takes input or locks the party.</summary>
public partial class FloorCard : PanelContainer
{
    private Label _title = null!, _stats = null!, _worst = null!, _reaction = null!;
    private float _left;

    public FloorCardModel? Model { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(620, 0);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 14);
        AddChild(margin);
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 4);
        margin.AddChild(v);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 22);
        v.AddChild(_title);
        _stats = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.85f, 0.85f, 0.85f) };
        v.AddChild(_stats);
        _worst = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.85f, 0.6f, 0.45f) };
        v.AddChild(_worst);
        _reaction = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.75f, 0.75f, 0.85f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0) };
        v.AddChild(_reaction);
    }

    public void Show(FloorCardModel m)
    {
        Model = m;
        _title.Text = m.Title;
        _stats.Text = $"{m.Time}   ·   {m.Kills} kills   ·   {m.Elites} elites   ·   secrets {m.Secrets}   ·   {m.Downs} downs   ·   {m.GoldFound} gold";
        _worst.Text = m.WorstMoment;
        _reaction.Text = m.Reaction;
        _reaction.Visible = m.Reaction != "";
        Modulate = Colors.White;
        _left = RunRules.FloorCardSeconds;
        Visible = true;
        Callable.From(() => { if (IsInsideTree()) Position = new Vector2(Mathf.Round((GetViewportRect().Size.X - Size.X) / 2f), 60); }).CallDeferred();
    }

    public void Dismiss() { _left = 0f; Visible = false; }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _left -= (float)delta;
        if (_left < 0.5f) Modulate = Colors.White with { A = Math.Max(0f, _left / 0.5f) };
        if (_left <= 0f) Visible = false;
    }
}
