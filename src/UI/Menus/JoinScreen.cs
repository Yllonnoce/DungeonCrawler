using DungeonCrawler.Characters.Defs;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>Title-screen panel: server address, your name, your class, Connect. Remembers the last values.</summary>
public partial class JoinScreen : VBoxContainer
{
    public event Action? Cancelled;
    private LineEdit _url = null!, _name = null!;
    private OptionButton _class = null!;
    private Label _status = null!;
    private Button _connect = null!;
    private readonly List<string> _classIds = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "Join a server", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        AddChild(title);
        AddChild(new Label { Text = "Server address (ws://host:7777 on a LAN, wss://name.onrender.com/ws over the internet)", Modulate = new Color(0.7f, 0.7f, 0.7f) });
        _url = new LineEdit { CustomMinimumSize = new Vector2(420, 36), PlaceholderText = "ws://192.168.1.5:7777" };
        AddChild(_url);
        AddChild(new Label { Text = "Your name (the server keeps your character under it)", Modulate = new Color(0.7f, 0.7f, 0.7f) });
        _name = new LineEdit { CustomMinimumSize = new Vector2(420, 36), MaxLength = Protocol.MaxNameLength, PlaceholderText = "Adventurer" };
        AddChild(_name);
        AddChild(new Label { Text = "Class (for a new character; a returning name keeps its class)", Modulate = new Color(0.7f, 0.7f, 0.7f) });
        _class = new OptionButton { CustomMinimumSize = new Vector2(420, 36), FocusMode = FocusModeEnum.None };
        foreach (var def in Registry.Instance.All<ClassDef>())
        {
            var p = def.ToProfile();
            _classIds.Add(p.Id);
            _class.AddItem(p.Name);
        }
        AddChild(_class);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        AddChild(row);
        _connect = new Button { Text = "Connect", CustomMinimumSize = new Vector2(200, 40), FocusMode = FocusModeEnum.None };
        _connect.Pressed += Connect;
        row.AddChild(_connect);
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(200, 40), FocusMode = FocusModeEnum.None };
        back.Pressed += () => { EventBus.Instance.PlaySfx("ui_click"); Cancelled?.Invoke(); };
        row.AddChild(back);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(420, 0) };
        AddChild(_status);
        NetSession.Instance.StatusChanged += s => { if (Visible) _status.Text = s; };
    }

    public void Open()
    {
        Visible = true;
        _url.Text = Settings.LastServerUrl;
        _name.Text = Settings.PlayerName != "" ? Settings.PlayerName : OS.GetEnvironment("USER") is { Length: > 0 } user ? user[..Math.Min(user.Length, Protocol.MaxNameLength)] : "";
        var idx = _classIds.IndexOf(Settings.LastClassId);
        if (idx >= 0) _class.Select(idx);
        _status.Text = "";
        _connect.Disabled = false;
    }

    private void Connect()
    {
        EventBus.Instance.PlaySfx("ui_click");
        var url = _url.Text.Trim();
        var name = PlayerRegistry.Normalize(_name.Text);
        if (!url.StartsWith("ws://") && !url.StartsWith("wss://")) { _status.Text = "The address must start with ws:// or wss://."; return; }
        if (!PlayerRegistry.ValidName(name)) { _status.Text = PlayerRegistry.ReasonText(ClaimResult.BadName); return; }
        if (_class.Selected < 0 || _class.Selected >= _classIds.Count) { _status.Text = "Pick a class."; return; }
        Settings.LastServerUrl = url;
        Settings.PlayerName = name;
        Settings.LastClassId = _classIds[_class.Selected];
        Settings.Save();
        _connect.Disabled = true;
        if (!NetSession.Instance.Connect(url, name, _classIds[_class.Selected])) { _status.Text = NetSession.Instance.Status; _connect.Disabled = false; }
    }
}
