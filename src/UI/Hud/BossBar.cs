using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>
/// Top centre: the boss in sight, its name (and the plan's title when it differs), its HP with a tick at the half, a flash at the phase change.
/// Polls rather than listens, since both the server and a client have the enemies and a client never raises BossSeen. Elites get a slimmer bar
/// through <see cref="ShowSlim"/> (the elites plan calls it).
/// </summary>
public partial class BossBar : Control
{
    private const float PollSeconds = 0.25f, HideDelay = 0.5f, FlashSeconds = 0.6f, SightRange = 40f;
    private PartyController _party = null!;
    private Label _name = null!, _title = null!, _slimName = null!;
    private ProgressBar _bar = null!, _slim = null!;
    private Control _main = null!, _slimRoot = null!;
    private Enemy? _boss, _elite;
    private float _poll, _hideLeft, _flashLeft;
    private bool _wasAboveHalf = true;

    public string ShownName => _name.Text;
    public bool Flashing => _flashLeft > 0f;
    public Enemy? Boss => _boss;

    public void Bind(PartyController party) => _party = party;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.CenterTop);
        OffsetLeft = -260; OffsetRight = 260; OffsetTop = 14; OffsetBottom = 14 + 44 + 30;
        _main = new VBoxContainer { Name = "Main", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _main.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _main.AddThemeConstantOverride("separation", 2);
        AddChild(_main);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.85f, 0.75f, 0.55f), Visible = false };
        _title.AddThemeFontSizeOverride("font_size", 13);
        _main.AddChild(_title);
        _name = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 0.92f, 0.8f) };
        _name.AddThemeFontSizeOverride("font_size", 18);
        _main.AddChild(_name);
        _bar = new ProgressBar { CustomMinimumSize = new Vector2(520, 14), ShowPercentage = false, MinValue = 0, MaxValue = 1, Value = 1 };
        _bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.75f, 0.12f, 0.1f) });
        _bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f) });
        _bar.Draw += DrawTick;
        _main.AddChild(_bar);

        _slimRoot = new VBoxContainer { Name = "Slim", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _slimRoot.SetAnchorsPreset(LayoutPreset.CenterTop);
        _slimRoot.Position = new Vector2(100, 50);
        AddChild(_slimRoot);
        _slimName = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.9f, 0.85f, 1f) };
        _slimName.AddThemeFontSizeOverride("font_size", 13);
        _slimRoot.AddChild(_slimName);
        _slim = new ProgressBar { CustomMinimumSize = new Vector2(320, 8), ShowPercentage = false, MinValue = 0, MaxValue = 1, Value = 1 };
        _slim.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(0.55f, 0.2f, 0.75f) });
        _slim.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f) });
        _slimRoot.AddChild(_slim);
    }

    private void DrawTick()
    {
        var x = _bar.Size.X * 0.5f;
        _bar.DrawLine(new Vector2(x, 0), new Vector2(x, _bar.Size.Y), new Color(1f, 1f, 1f, 0.8f), 1f);
    }

    /// <summary>An elite in sight: the slim bar under the main one (its DisplayName carries the affixes).</summary>
    public void ShowSlim(Enemy elite) { _elite = elite; _slimRoot.Visible = true; }
    public void HideSlim() { _elite = null; _slimRoot.Visible = false; }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (_flashLeft > 0f) { _flashLeft -= dt; _bar.Modulate = _flashLeft > 0f && Mathf.Sin(_flashLeft * 40f) > 0f ? new Color(1.6f, 1.6f, 1.4f) : Colors.White; }
        _poll -= dt;
        if (_poll <= 0f) { _poll = PollSeconds; Poll(); }
        if (_boss != null && IsInstanceValid(_boss))
        {
            var frac = Mathf.Clamp(_boss.Hp / Math.Max(1f, _boss.MaxHp), 0f, 1f);
            _bar.Value = frac;
            if (_wasAboveHalf && frac <= 0.5f && frac > 0f) { _flashLeft = FlashSeconds; }
            _wasAboveHalf = frac > 0.5f;
            if (!_boss.IsAlive) { _hideLeft -= dt; if (_hideLeft <= 0f) { _boss = null; _main.Visible = false; } }
        }
        if (_elite != null && (!IsInstanceValid(_elite) || !_elite.IsAlive)) HideSlim();
        else if (_elite != null) { _slimName.Text = _elite.DisplayName; _slim.Value = Mathf.Clamp(_elite.Hp / Math.Max(1f, _elite.MaxHp), 0f, 1f); }
        Visible = _main.Visible || _slimRoot.Visible; // the control itself reads as shown only while a bar is
    }

    private void Poll()
    {
        if (_party == null || !IsInstanceValid(_party) || !_party.HasBodies || Main.Instance?.Builder.FloorRoot is not { } root) { if (_boss == null) _main.Visible = false; return; }
        var anchor = _party.Anchor.GlobalPosition;
        var seen = root.GetChildren().OfType<Enemy>()
            .Where(e => e.IsBoss && e.IsAlive && !e.IsUnaware && e.GlobalPosition.DistanceTo(anchor) <= SightRange)
            .OrderBy(e => e.GlobalPosition.DistanceSquaredTo(anchor)).FirstOrDefault();
        if (seen != null)
        {
            if (seen != _boss) { _boss = seen; _wasAboveHalf = seen.Hp > seen.MaxHp * 0.5f; _bar.Modulate = Colors.White; }
            _hideLeft = HideDelay;
            _name.Text = seen.DisplayName;
            var planName = Main.Instance.Builder.Contents?.Plan?.Boss?.Name;
            _title.Visible = !string.IsNullOrWhiteSpace(planName) && planName != seen.DisplayName;
            _title.Text = planName ?? "";
            _main.Visible = true;
        }
        else if (_boss != null && (!IsInstanceValid(_boss) || _boss.IsAlive)) { _hideLeft -= PollSeconds; if (_hideLeft <= 0f) { _boss = null; _main.Visible = false; } }
    }
}
