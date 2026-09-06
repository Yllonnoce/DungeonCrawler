using DungeonCrawler.Dungeon;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>
/// The question a stair landing asks: take the stairs, or turn back. E / Enter or the Yes button confirms, Esc or No
/// (or stepping off the landing) dismisses. Sealed stairs only offer to turn back.
/// </summary>
public partial class StairsPrompt : Control
{
    public event Action? Closed;
    public Stairs? Current { get; private set; }
    private Adventurer? _actor;
    private Label _text = null!;
    private Button _yes = null!;
    private Button _no = null!;

    public override void _Ready()
    {
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
        this.FillParent();
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.35f) };
        dim.FillParent();
        AddChild(dim);
        var centre = new CenterContainer();
        centre.FillParent();
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0) };
        centre.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 20);
        panel.AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        margin.AddChild(box);
        _text = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_text);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        _yes = new Button { Text = "Yes  (E)", CustomMinimumSize = new Vector2(150, 0), FocusMode = FocusModeEnum.None };
        _no = new Button { Text = "No  (Esc)", CustomMinimumSize = new Vector2(150, 0), FocusMode = FocusModeEnum.None };
        _yes.Pressed += Accept;
        _no.Pressed += Close;
        row.AddChild(_yes);
        row.AddChild(_no);
        box.AddChild(row);
    }

    public void Open(Stairs stairs, Adventurer actor)
    {
        Current = stairs;
        _actor = actor;
        _text.Text = stairs.Question;
        _yes.Visible = !stairs.Locked;
        _no.Text = stairs.Locked ? "Turn back  (Esc)" : "No  (Esc)";
        Visible = true;
    }

    /// <summary>Closes when the prompt is for these stairs (the player stepped off the landing).</summary>
    public void Dismiss(Stairs stairs)
    {
        if (Current == stairs) Close();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        Current = null;
        _actor = null;
        Closed?.Invoke();
    }

    private void Accept()
    {
        if (Current is not { } stairs || _actor is not { } actor) { Close(); return; }
        var party = actor.Party;
        Close();
        stairs.Confirm(party, actor);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event.IsActionPressed("interact") || @event.IsActionPressed("ui_accept"))
        {
            if (Current is { Locked: false }) Accept(); else Close();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
