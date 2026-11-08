using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.UI.Cards;

/// <summary>The run recap: the headline, floor by floor, per hero, the players' roles, elites, boons, clauses, deepest floor, score, and one button. Modal.</summary>
public partial class RunRecap : PanelContainer
{
    private Label _headline = null!, _score = null!;
    private VBoxContainer _body = null!;
    private Button _button = null!;
    private Action? _onClose;

    public RunRecapModel? Model { get; private set; }
    public string ButtonText => _button.Text;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CustomMinimumSize = new Vector2(720, 0);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 20);
        AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        margin.AddChild(v);
        _headline = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(680, 0) };
        _headline.AddThemeFontSizeOverride("font_size", 24);
        v.AddChild(_headline);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(680, 360), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_body);
        _score = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.85f, 0.75f, 0.55f) };
        _score.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(_score);
        _button = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(300, 40) };
        _button.Pressed += Press;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(_button);
        v.AddChild(row);
    }

    public void Open(RunRecapModel m, string button, Action? onClose)
    {
        Model = m;
        _onClose = onClose;
        _headline.Text = m.Headline;
        _headline.Modulate = m.Victory ? new Color(0.95f, 0.85f, 0.5f) : new Color(0.85f, 0.45f, 0.3f);
        foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        void Head(string text) { var l = new Label { Text = text, Modulate = new Color(0.7f, 0.65f, 0.55f) }; l.AddThemeFontSizeOverride("font_size", 13); _body.AddChild(l); }
        void Line(string text) => _body.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (m.FloorLines.Count > 0) { Head("FLOOR BY FLOOR"); foreach (var f in m.FloorLines) Line(f); }
        if (m.Heroes.Count > 0) { Head("THE PARTY"); foreach (var h in m.Heroes) Line(h.Text); }
        if (m.PlayerLines.Count > 0) { Head("THE PLAYERS"); foreach (var p in m.PlayerLines) Line(p); }
        Head("THE RUN");
        Line($"Deepest floor {m.DeepestFloor} · {m.ElitesSlain} elites slain");
        Line($"Boons: {(m.Boons.Count == 0 ? "none" : string.Join(", ", m.Boons))}");
        Line($"Clauses: {(m.Clauses.Count == 0 ? "none" : new ClauseSet(m.Clauses).Summary)}");
        _score.Text = $"Score {m.Score}";
        _button.Text = button;
        Visible = true;
        EventBus.Instance.PlaySfx(m.Victory ? "victory" : "ui_open");
        Callable.From(() => { if (IsInsideTree()) Position = ((GetViewportRect().Size - Size) / 2f).Floor(); }).CallDeferred();
    }

    /// <summary>The one button (also the test seam).</summary>
    public void Press()
    {
        if (!Visible) return;
        Visible = false;
        Model = null;
        var close = _onClose;
        _onClose = null;
        EventBus.Instance.PlaySfx("ui_close");
        close?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Enter or Key.Space or Key.KpEnter) { Press(); GetViewport().SetInputAsHandled(); }
    }
}
