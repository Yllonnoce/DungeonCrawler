using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Core;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>Title screen: New Game, Load Game, Quit.</summary>
public partial class MainMenu : Control
{
    private VBoxContainer _buttons = null!;
    private SlotList _slots = null!;
    private Label _status = null!;
    private SettingsScreen _settings = null!;

    public override void _Ready()
    {
        this.FillParent();
        MouseFilter = MouseFilterEnum.Stop;
        var bg = new ColorRect { Color = new Color(0.03f, 0.03f, 0.04f) };
        bg.FillParent();
        AddChild(bg);

        var centre = new CenterContainer();
        centre.FillParent();
        AddChild(centre);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        centre.AddChild(v);

        var title = new Label { Text = "DUNGEON CRAWLER", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        v.AddChild(title);
        var sub = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.6f, 0.6f, 0.6f) };
        v.AddChild(sub);
        _subtitle = sub;
        RefreshSubtitle();
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 8);
        v.AddChild(_buttons);
        // New Game never plans: it plays the campaign on disk (your rebake, else the shipped one). A fresh AI-written
        // campaign is only ever made from Settings ("Rebuild campaign"), where its cost is spelled out.
        AddButton("Story", () => Main.Instance.StartNewGameFromDisk());
        AddButton("New Descent", () => Main.Instance.StartDescent());
        _daily = AddButton("Daily Descent", () => Main.Instance.StartDaily(DailyRules.Today()));
        AddButton("Load Game", () => { _buttons.Visible = false; _slots.Show(SlotList.Mode.Load); });
        AddButton("Join Server", () => { _buttons.Visible = false; _join.Open(); EventBus.Instance.PlaySfx("ui_open"); });
        AddButton("Settings", () => { _buttons.Visible = false; _settings.Visible = true; EventBus.Instance.PlaySfx("ui_open"); });
        AddButton("Quit", () => GetTree().Quit());
        RefreshSubtitle();

        _settings = new SettingsScreen { Visible = false };
        _settings.Closed += () => { _settings.Visible = false; _buttons.Visible = true; RefreshSubtitle(); };
        v.AddChild(_settings);

        _join = new JoinScreen { Visible = false };
        _join.Cancelled += () => { _join.Visible = false; _buttons.Visible = true; };
        v.AddChild(_join);

        _slots = new SlotList { Visible = false };
        _slots.SlotChosen += slot => { if (!Main.Instance.LoadGame(slot)) _status.Text = "Could not load that slot."; };
        _slots.Cancelled += () => { _slots.Visible = false; _buttons.Visible = true; };
        v.AddChild(_slots);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 0.6f, 0.5f) };
        v.AddChild(_status);
    }

    private Button AddButton(string text, Action action)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(260, 44), FocusMode = FocusModeEnum.None };
        b.Pressed += () => { EventBus.Instance.PlaySfx("ui_click"); action(); };
        _buttons.AddChild(b);
        return b;
    }

    private JoinScreen _join = null!;
    private Label _subtitle = null!;
    private Button _daily = null!;
    public void RefreshSubtitle()
    {
        var plays = BuiltinCampaign.IsUserCopy ? "your rebaked campaign" : BuiltinCampaign.Exists ? "the built-in campaign" : "a campaign from the offline designer";
        var canPlan = Settings.HasPlannerKey && DebugTools.Instance?.NoAi != true;
        _subtitle.Text = $"Story plays {plays}. New Descent rolls a fresh seed{(canPlan ? $" and lets {Settings.PlannerLabel} write it" : "")}. Daily Descent: everyone gets today's dungeon and two clauses.";
        if (_daily != null)
        {
            var key = DailyRules.Key(DailyRules.Today());
            var done = Save.Profile.ProfileRules.DailyDone(Save.Profile.ProfileStore.Current, key);
            _daily.Disabled = done;
            _daily.Text = done && Save.Profile.ProfileStore.Current.Daily.TryGetValue(key, out var d) ? $"Daily Descent — done: floor {d.Depth}, score {d.Score}" : "Daily Descent";
        }
    }

    public void ShowStatus(string text) => _status.Text = text;

    /// <summary>Debug: jump straight to the settings panel.</summary>
    public void OpenSettings() { _buttons.Visible = false; _settings.Visible = true; }

    public void Reset()
    {
        RefreshSubtitle();
        _buttons.Visible = true;
        _slots.Visible = false;
        _settings.Visible = false;
        _join.Visible = false;
        _status.Text = "";
    }
}
