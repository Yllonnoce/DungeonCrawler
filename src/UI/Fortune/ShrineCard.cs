using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.UI.Fortune;

/// <summary>
/// The shrine's choice card: one row per boon (1-4), a lift row (L) when the party carries a curse, and walking away (Esc or
/// Space). One pick per player; the pick goes to the <see cref="FortuneDirector"/> solo and to the server in co-op, which
/// closes the card everywhere with the outcome.
/// </summary>
public partial class ShrineCard : PanelContainer
{
    private Label _title = null!;
    private Label _price = null!;
    private VBoxContainer _rows = null!;
    private Label _outcome = null!;
    private readonly List<Button> _buttons = new();
    private ShrineCardMsg? _card;
    private bool _picked;

    public event Action? Closed;
    public ShrineCardMsg? Card => _card;
    /// <summary>Boon rows, then the lift row (when offered), then the walk-away row.</summary>
    public int RowCount => _buttons.Count;
    public string RowText(int i) => i >= 0 && i < _buttons.Count ? _buttons[i].Text : "";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CustomMinimumSize = new Vector2(560, 0);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 16);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);
        _title = new Label { Text = "The altar" };
        _title.AddThemeFontSizeOverride("font_size", 22);
        root.AddChild(_title);
        _price = new Label { Modulate = new Color(0.85f, 0.75f, 0.55f) };
        root.AddChild(_price);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        root.AddChild(_rows);
        _outcome = new Label { Modulate = new Color(0.7f, 0.7f, 0.7f), Visible = false };
        root.AddChild(_outcome);
        var hint = new Label { Text = "1-4: take a boon   L: lift a curse   Esc: walk away", Modulate = new Color(0.6f, 0.6f, 0.6f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(hint);
    }

    public void Open(ShrineCardMsg card)
    {
        _card = card;
        _picked = false;
        _price.Text = card.PriceLine;
        _outcome.Visible = false;
        foreach (var b in _buttons) b.QueueFree();
        _buttons.Clear();
        for (var i = 0; i < card.Boons.Count; i++)
        {
            var def = Boons.Get(card.Boons[i]);
            var crown = def.Crown ? " (crown)" : "";
            Add($"{i + 1}. {def.Name}{crown} — {def.Text}", i);
        }
        if (card.LiftCurse is { } lift && Curses.TryGet(lift, out var curse))
        {
            Add($"L. Lift {curse.Name} ({card.LiftPrice} gold)", -2);
            if (Main.Instance is { } main && main.InGame && main.Party.Gold < card.LiftPrice) _buttons[^1].Disabled = true; // the purse is short: the row shows the price and takes no pick
        }
        Add($"Esc. Walk away (+{card.SkipGold} gold)", -1);
        Visible = true;
        EventBus.Instance.PlaySfx("ui_open");
        Callable.From(() => { if (IsInsideTree()) Position = ((GetViewportRect().Size - Size) / 2f).Floor(); }).CallDeferred();

        void Add(string text, int choice)
        {
            var b = new Button { Text = text, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
            b.Pressed += () => Pick(choice);
            _rows.AddChild(b);
            _buttons.Add(b);
        }
    }

    /// <summary>The server (or the director) closed it: say what happened and hide.</summary>
    public void Close(string outcome)
    {
        if (!Visible) return;
        var text = outcome switch
        {
            var o when o.StartsWith("boon:") && Boons.TryGet(o[5..], out var b) => $"The altar gives: {b.Name}.",
            var o when o.StartsWith("lift:") && Curses.TryGet(o[5..], out var c) => $"The altar lifts {c.Name}.",
            "skip" => "You leave the altar alone.",
            "gone" => "The altar is gone.",
            _ => outcome,
        };
        EventBus.Instance.Log(text);
        Visible = false;
        _card = null;
        EventBus.Instance.PlaySfx("ui_close");
        Closed?.Invoke();
    }

    /// <summary>0..3 a boon, -1 walk away, -2 lift. One pick per player until the card closes. False when the pick was not taken.</summary>
    public bool Pick(int choice)
    {
        if (!Visible || _picked || _card == null) return false;
        if (choice >= 0 && choice >= _card.Boons.Count) return false;
        if (choice == -2 && (_card.LiftCurse == null || _buttons.Any(b => b.Disabled && b.Text.StartsWith("L.")))) return false;
        _picked = true;
        foreach (var b in _buttons) b.Disabled = true;
        _outcome.Text = "The altar considers..."; _outcome.Visible = true;
        if (NetSession.IsClient) { NetSession.Instance.Send(new ShrineChoiceIntent(choice)); return true; } // the server answers: ShrineClosedEvt, or the card again when it refused
        var party = Main.Instance.Party;
        if (FortuneDirector.Instance is not { } fortune || fortune.PendingShrine == null) { Close("gone"); return true; } // nothing is waiting on this card (the floor changed under it)
        fortune.ResolveShrine(party.FocusedIndex, choice);
        if (Visible && _card != null) Open(_card); // a refused lift: the card comes back alive for another pick
        return true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        var choice = key.Keycode switch
        {
            Key.Key1 or Key.Kp1 => 0, Key.Key2 or Key.Kp2 => 1, Key.Key3 or Key.Kp3 => 2, Key.Key4 or Key.Kp4 => 3,
            Key.L => -2,
            Key.Escape or Key.Space => -1,
            _ => int.MinValue,
        };
        if (choice == int.MinValue) return;
        if (Pick(choice)) GetViewport().SetInputAsHandled();
    }
}
