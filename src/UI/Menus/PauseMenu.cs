using DungeonCrawler.Core;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>Esc menu: Resume, Save, Load, Main Menu, Quit. Pauses the tree while open.</summary>
public partial class PauseMenu : Control
{
    private VBoxContainer _buttons = null!;
    private SlotList _slots = null!;
    private Label _status = null!;
    private SettingsScreen _settings = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        this.FillParent();
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.FillParent();
        AddChild(dim);

        var centre = new CenterContainer();
        centre.FillParent();
        AddChild(centre);
        var panel = new PanelContainer();
        centre.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 20);
        panel.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        margin.AddChild(v);

        var title = new Label { Text = "Paused", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        v.AddChild(title);

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 6);
        v.AddChild(_buttons);
        AddButton("Resume", () => Main.Instance.Hud.TogglePause());
        _save = AddButton("Save Game", () => ShowSlots(SlotList.Mode.Save));
        _load = AddButton("Load Game", () => ShowSlots(SlotList.Mode.Load));
        AddButton("Settings", () => { _buttons.Visible = false; _settings.Visible = true; EventBus.Instance.PlaySfx("ui_open"); });
        _leave = AddButton("Main Menu", () => { if (Net.NetSession.IsNetworked) Main.Instance.OnClientLeft("You left the server."); else Main.Instance.ReturnToMenu(); });
        AddButton("Quit", () => GetTree().Quit());

        _settings = new SettingsScreen { Visible = false };
        _settings.Closed += () => { _settings.Visible = false; _buttons.Visible = true; };
        v.AddChild(_settings);

        _slots = new SlotList { Visible = false };
        _slots.SlotChosen += OnSlotChosen;
        _slots.Cancelled += ShowButtons;
        v.AddChild(_slots);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        v.AddChild(_status);
    }

    private void OnSlotChosen(string slot)
    {
        if (_slotsMode == SlotList.Mode.Save)
        {
            Main.Instance.SaveGame(slot);
            _status.Text = $"Saved to {SaveManager.DisplayName(slot)}.";
            ShowButtons();
        }
        else if (!Main.Instance.LoadGame(slot))
        {
            _status.Text = "Could not load that slot.";
        }
    }

    private SlotList.Mode _slotsMode;

    private Button _save = null!, _load = null!, _leave = null!;

    private Button AddButton(string text, Action action)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(260, 40), FocusMode = FocusModeEnum.None };
        b.Pressed += () => { EventBus.Instance.PlaySfx("ui_click"); action(); };
        _buttons.AddChild(b);
        return b;
    }

    private void ShowButtons()
    {
        _slots.Visible = false;
        _settings.Visible = false;
        _buttons.Visible = true;
    }

    public void Open()
    {
        Visible = true;
        _status.Text = "";
        // on a server the world keeps going and only the server saves
        var net = Net.NetSession.IsNetworked;
        _save.Visible = !net;
        _load.Visible = !net;
        _leave.Text = net ? "Disconnect" : "Main Menu";
        ShowButtons();
    }

    public void OpenSettings() { _buttons.Visible = false; _slots.Visible = false; _settings.Visible = true; }

    public void ShowSlots(SlotList.Mode mode)
    {
        _slotsMode = mode;
        _buttons.Visible = false;
        _slots.Show(mode);
    }
}
