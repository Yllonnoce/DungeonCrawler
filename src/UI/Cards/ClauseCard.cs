using DungeonCrawler.Adventure.Run;
using DungeonCrawler.Core;
using DungeonCrawler.Save.Profile;
using Godot;

namespace DungeonCrawler.UI.Cards;

/// <summary>
/// The factor's contract: one row per clause with a rank stepper (capped by the profile's deeds), the heat and the
/// multiplier it buys, Sign or Skip. Read-only (the active list and Close) on a client and in the Daily.
/// </summary>
public partial class ClauseCard : PanelContainer
{
    private Label _title = null!, _total = null!, _note = null!;
    private VBoxContainer _rows = null!;
    private Button _sign = null!, _skip = null!;
    private readonly Dictionary<Clause, int> _picked = new();
    private readonly Dictionary<Clause, Label> _rankLabels = new();
    private bool _readOnly;

    public event Action? Closed;
    /// <summary>What the stepper rows currently say, normalized.</summary>
    public List<string> Selected => ClauseRules.Normalize(_picked.Where(kv => kv.Value > 0).Select(kv => ClauseRules.Entry(ClauseRules.Def(kv.Key), kv.Value)));
    public bool ReadOnly => _readOnly;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CustomMinimumSize = new Vector2(760, 0);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 16);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);
        _title = new Label { Text = "THE CLAUSES" };
        _title.AddThemeFontSizeOverride("font_size", 22);
        root.AddChild(_title);
        _note = new Label { Modulate = new Color(0.75f, 0.7f, 0.6f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(720, 0) };
        root.AddChild(_note);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        root.AddChild(_rows);
        _total = new Label { Modulate = new Color(0.85f, 0.75f, 0.55f) };
        _total.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(_total);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttons);
        _sign = new Button { Text = "Sign", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(160, 36) };
        _sign.Pressed += Sign;
        buttons.AddChild(_sign);
        _skip = new Button { Text = "Skip", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(160, 36) };
        _skip.Pressed += Close;
        buttons.AddChild(_skip);
        var hint = new Label { Text = "Esc: skip", Modulate = new Color(0.6f, 0.6f, 0.6f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(hint);
    }

    /// <summary>Open on the active clauses (a stepper per clause, or the list alone when read-only).</summary>
    public void Open(bool readOnly)
    {
        _readOnly = readOnly;
        var active = ClauseRules.Active;
        var deeds = ProfileStore.Current.Deeds.Keys.ToHashSet();
        _picked.Clear();
        _rankLabels.Clear();
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        foreach (var def in ClauseRules.All)
        {
            _picked[def.Id] = active.Rank(def.Id);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var name = new Label { Text = $"{def.Name}   (heat {def.HeatPerRank}/rank{(def.MaxRank > 1 ? $", up to {def.MaxRank}" : "")})" };
            name.AddThemeFontSizeOverride("font_size", 15);
            text.AddChild(name);
            var desc = new Label { Text = def.Description, Modulate = new Color(0.7f, 0.7f, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) };
            desc.AddThemeFontSizeOverride("font_size", 13);
            text.AddChild(desc);
            var cap = ClauseRules.MaxRankAllowed(def, deeds);
            if (cap < def.MaxRank && def.RankUnlockedByDeed[cap] is { } deed && DeedRules.TryGet(deed, out var d))
            {
                var locked = new Label { Text = $"Rank {Roman(cap + 1)} needs the deed {d.Name} ({d.Condition}).", Modulate = new Color(0.5f, 0.5f, 0.55f) };
                locked.AddThemeFontSizeOverride("font_size", 12);
                text.AddChild(locked);
            }
            row.AddChild(text);
            if (readOnly)
            {
                var rank = new Label { Text = _picked[def.Id] == 0 ? "—" : Roman(_picked[def.Id]), CustomMinimumSize = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Center };
                _rankLabels[def.Id] = rank;
                row.AddChild(rank);
            }
            else
            {
                var minus = new Button { Text = "−", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 32) };
                var rank = new Label { CustomMinimumSize = new Vector2(44, 0), HorizontalAlignment = HorizontalAlignment.Center };
                var plus = new Button { Text = "+", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 32) };
                var id = def.Id;
                minus.Pressed += () => Step(id, -1, cap);
                plus.Pressed += () => Step(id, +1, cap);
                _rankLabels[id] = rank;
                row.AddChild(minus); row.AddChild(rank); row.AddChild(plus);
            }
            _rows.AddChild(row);
        }
        _note.Text = readOnly ? "The contract as signed. A client reads it; the host signs it, and the Daily's clauses are the day's." : "Every clause is optional and every one is paid for: heat multiplies the gold and XP found by one tenth per point.";
        _sign.Visible = !readOnly;
        _skip.Text = readOnly ? "Close" : "Skip";
        RefreshTotals();
        Visible = true;
        EventBus.Instance.PlaySfx("ui_open");
        Callable.From(() => { if (IsInsideTree()) Position = ((GetViewportRect().Size - Size) / 2f).Floor(); }).CallDeferred();
    }

    /// <summary>A stepper press (also the test seam): the rank moves within 0..cap.</summary>
    public void Step(Clause id, int delta, int? cap = null)
    {
        if (_readOnly) return;
        var def = ClauseRules.Def(id);
        var max = cap ?? ClauseRules.MaxRankAllowed(def, ProfileStore.Current.Deeds.Keys.ToHashSet());
        _picked[id] = Math.Clamp(_picked.GetValueOrDefault(id) + delta, 0, max);
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        foreach (var (id, label) in _rankLabels) label.Text = _picked.GetValueOrDefault(id) == 0 ? "—" : Roman(_picked[id]);
        var set = new ClauseSet(Selected);
        _total.Text = $"Heat {set.Heat} · ×{set.RewardMultiplier.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} gold and XP";
    }

    /// <summary>Sign what the rows say (solo and server) and close.</summary>
    public void Sign()
    {
        if (!Visible || _readOnly) return;
        Main.Instance?.SignClauses(Selected);
        Close();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        EventBus.Instance.PlaySfx("ui_close");
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { Close(); GetViewport().SetInputAsHandled(); }
    }

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", _ => n.ToString() };
}
