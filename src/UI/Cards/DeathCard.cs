using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.UI.Cards;

/// <summary>The death card: "THE PARTY HAS FALLEN", the epitaph, what did it and where, the wipe count here, and one button (Rise, or To the Records under the Death Clause). Modal.</summary>
public partial class DeathCard : PanelContainer
{
    private Label _epitaph = null!, _line = null!;
    private Button _button = null!;
    private Action? _onClose;

    public event Action? Closed;
    public DeathCardModel? Model { get; private set; }
    public string ButtonText => _button.Text;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CustomMinimumSize = new Vector2(620, 0);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 24);
        AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        margin.AddChild(v);
        var title = new Label { Text = "THE PARTY HAS FALLEN", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.85f, 0.33f, 0.17f) };
        title.AddThemeFontSizeOverride("font_size", 32);
        v.AddChild(title);
        _epitaph = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0), Modulate = new Color(0.9f, 0.85f, 0.75f) };
        _epitaph.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(_epitaph);
        _line = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.75f, 0.75f, 0.75f) };
        v.AddChild(_line);
        _button = new Button { Text = RunRules.RiseButton, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 40) };
        _button.Pressed += Press;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(_button);
        v.AddChild(row);
    }

    public void Open(DeathCardModel m, Action? onClose)
    {
        Model = m;
        _onClose = onClose;
        _epitaph.Text = m.Epitaph;
        _line.Text = RunRules.DeathLine(m);
        _button.Text = m.Button;
        Visible = true;
        EventBus.Instance.PlaySfx("ui_open");
        Callable.From(() => { if (IsInsideTree()) Position = ((GetViewportRect().Size - Size) / 2f).Floor(); }).CallDeferred();
    }

    /// <summary>The one button (also the test seam): close, then run what the card was waiting on.</summary>
    public void Press()
    {
        if (!Visible) return;
        Visible = false;
        Model = null;
        var close = _onClose;
        _onClose = null;
        EventBus.Instance.PlaySfx("ui_close");
        Closed?.Invoke();
        close?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Enter or Key.Space or Key.KpEnter) { Press(); GetViewport().SetInputAsHandled(); }
    }
}
