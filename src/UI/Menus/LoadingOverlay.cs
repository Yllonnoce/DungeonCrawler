using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>Full-screen card shown while the planner works: a heading, the campaign title and synopsis once known, and a status line.</summary>
public partial class LoadingOverlay : Control
{
    private Label _heading = null!, _title = null!, _body = null!, _status = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        this.FillParent();
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        var bg = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.96f) };
        bg.FillParent();
        AddChild(bg);
        var centre = new CenterContainer();
        centre.FillParent();
        AddChild(centre);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        v.AddThemeConstantOverride("separation", 14);
        centre.AddChild(v);
        _heading = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.8f, 0.75f, 0.65f) };
        _heading.AddThemeFontSizeOverride("font_size", 22);
        v.AddChild(_heading);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Word };
        _title.AddThemeFontSizeOverride("font_size", 40);
        v.AddChild(_title);
        _body = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Word, Modulate = new Color(0.9f, 0.88f, 0.82f) };
        _body.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(_body);
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.6f, 0.6f, 0.6f) };
        v.AddChild(_status);
    }

    public void Open(string heading, string status)
    {
        _heading.Text = heading;
        _title.Text = "";
        _body.Text = "";
        _status.Text = status;
        Visible = true;
    }

    public void SetStatus(string status) => _status.Text = status;

    public void ShowCampaign(string title, string synopsis)
    {
        _title.Text = title;
        _body.Text = synopsis;
    }

    public void Close() => Visible = false;
}
