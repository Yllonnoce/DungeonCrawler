using DungeonCrawler.Core;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>
/// Esc menu: Resume, Save, Load, Main Menu, Quit. Pauses the tree while open. In a co-op game it also lists the story
/// heroes a player can call in as bots to fill empty places, and their own bots to send away.
/// </summary>
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
        _clauses = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.85f, 0.75f, 0.55f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
        _clauses.AddThemeFontSizeOverride("font_size", 13);
        v.AddChild(_clauses);

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 6);
        v.AddChild(_buttons);
        AddButton("Resume", () => Main.Instance.Hud.TogglePause());
        _heroes = new VBoxContainer { Visible = false };
        _heroes.AddThemeConstantOverride("separation", 6);
        _buttons.AddChild(_heroes);
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
    private VBoxContainer _heroes = null!;
    private Label _clauses = null!;

    private Button AddButton(string text, Action action)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(260, 40), FocusMode = FocusModeEnum.None };
        b.Pressed += () => { EventBus.Instance.PlaySfx("ui_click"); action(); };
        _buttons.AddChild(b);
        return b;
    }

    /// <summary>Co-op: a button to call in each story hero not in play (while there is room), and one to send away each of this player's bots.</summary>
    private void RebuildHeroes()
    {
        foreach (var child in _heroes.GetChildren()) child.QueueFree();
        var client = Net.NetSession.IsClient ? Net.NetSession.Instance.Client : null;
        _heroes.Visible = client != null;
        if (client == null) return;
        var title = new Label { Text = "Heroes", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 18);
        _heroes.AddChild(title);
        var roster = client.Roster.Values.ToList();
        var inPlay = roster.Count(p => p.Online);
        if (inPlay < Net.Protocol.MaxPlayers)
            foreach (var (name, classId) in Net.HeroBots.Offer(Party.PartyController.DefaultRoster.Select(h => (h.Name, h.ClassId)), roster))
                AddHeroButton($"Call {name} ({ClassName(classId)})", new Net.AddBotIntent(name));
        else _heroes.AddChild(new Label { Text = "The party has four heroes.", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.8f, 0.8f, 0.8f) });
        foreach (var bot in roster.Where(p => p.Online && string.Equals(p.BotOwner, client.PlayerName, StringComparison.OrdinalIgnoreCase)))
            AddHeroButton($"Send {bot.Name} back to town", new Net.DismissBotIntent(bot.MemberIndex));
        var hint = new Label { Text = "Heroes you call follow you and fight on their own.\nA joining player always gets a place.", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.75f, 0.75f, 0.75f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        _heroes.AddChild(hint);
    }

    private void AddHeroButton(string text, Net.NetMessage intent)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(260, 36), FocusMode = FocusModeEnum.None };
        b.Pressed += () => { EventBus.Instance.PlaySfx("ui_click"); Net.NetSession.Instance.Send(intent); Main.Instance.Hud.TogglePause(); };
        _heroes.AddChild(b);
    }

    private static string ClassName(string classId) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(classId.Replace("class.", ""));

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
        _load.Visible = !net && !Adventure.Run.ClauseRules.Active.Hardcore; // the Death Clause allows no second chances
        _clauses.Text = Adventure.Run.ClauseRules.Active.Summary;
        _leave.Text = net ? "Disconnect" : "Main Menu";
        RebuildHeroes();
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
