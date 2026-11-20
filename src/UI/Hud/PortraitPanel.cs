using DungeonCrawler.Characters;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>One party member's slot on the HUD: name, HP, stamina, mana, statuses, and the active ability button.</summary>
public partial class PortraitPanel : PanelContainer
{
    private static readonly Color FocusColor = new(1f, 0.9f, 0.5f);

    private PartyController _party = null!;
    private int _index;
    private Label _name = null!;
    private ProgressBar _hp = null!;
    private ProgressBar _stamina = null!;
    private ProgressBar _mana = null!;
    private Label _status = null!;
    private Button _attack = null!;
    private Button _stance = null!;
    private float _flashLeft, _pulseLeft, _pulseTotal;
    private Color _pulseColor = Colors.White;

    /// <summary>A red flash when this member is hurt, easing back over 0.3 s.</summary>
    public void Flash() => _flashLeft = 0.3f;
    /// <summary>The portrait oscillates toward <paramref name="c"/> for <paramref name="seconds"/> (a level-up's gold).</summary>
    public void Pulse(Color c, float seconds) { _pulseColor = c; _pulseLeft = _pulseTotal = seconds; }

    public override void _Process(double delta)
    {
        if (_flashLeft <= 0f && _pulseLeft <= 0f) return;
        var dt = (float)delta;
        var m = _party.Members[_index];
        var baseColor = m.IsAlive ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
        var tint = baseColor;
        if (_pulseLeft > 0f)
        {
            _pulseLeft -= dt;
            var k = 0.5f + 0.5f * Mathf.Sin((_pulseTotal - _pulseLeft) * 8f);
            tint = baseColor.Lerp(_pulseColor, k * Math.Min(1f, _pulseLeft / 0.4f));
        }
        if (_flashLeft > 0f) { _flashLeft -= dt; tint = tint.Lerp(new Color(1f, 0.6f, 0.6f), Math.Max(0f, _flashLeft / 0.3f)); }
        Modulate = tint;
    }

    public override void _Ready()
    {
        _name = GetNode<Label>("%Name");
        _hp = GetNode<ProgressBar>("%Hp");
        _stamina = GetNode<ProgressBar>("%Stamina");
        _mana = GetNode<ProgressBar>("%Mana");
        _status = GetNode<Label>("%Status");
        _attack = GetNode<Button>("%Attack");
        _attack.Pressed += () => { if (_party.LocalIndex == _index) _party.Attack(_index); else if (_party.AllowPossession) _party.Possess(_index); };
        _attack.FocusMode = FocusModeEnum.None;
        _stance = new Button { Text = "Defensive", FocusMode = FocusModeEnum.None };
        _stance.AddThemeFontSizeOverride("font_size", 11);
        _stance.Pressed += () => _party.CycleStance(_index);
        _attack.GetParent().AddChild(_stance);
    }

    public void Bind(PartyController party, int index)
    {
        _party = party;
        _index = index;
        _wrote = false; // a slot bound again writes everything afresh
        _statusKey.Clear();
        _hpMax = _hpNow = _staminaMax = _staminaNow = _manaMax = _manaNow = float.NaN;
        Refresh();
    }

    // ---- what was last written: Refresh runs every frame and writes a label, a bar or a colour only when its value changed ----
    private bool _wrote;
    private (int Index, bool Focused, string Name, int Level, int Tag, string? BotOwner, ActionKind Action) _nameKey;
    private (bool Alive, bool Focused, bool Stunned, string Ability, int Cooldown, bool Recovering, int Arrows, bool Fallen) _attackKey;
    private readonly List<int> _statusKey = new();
    private bool _attackShown, _stanceShown, _attackDisabled;
    private Stance _stanceShownAs;
    private float _hpMax = float.NaN, _hpNow = float.NaN, _staminaMax = float.NaN, _staminaNow = float.NaN, _manaMax = float.NaN, _manaNow = float.NaN;

    /// <summary>
    /// Bring the slot up to date (every frame, from the HUD): the strings are built only when what they show changed
    /// (<see cref="PortraitText"/>'s keys), so a quiet frame allocates nothing; what is shown is what a full rebuild would show.
    /// </summary>
    public void Refresh()
    {
        var m = _party.Members[_index];
        var focused = _party.LocalIndex == _index;
        var adv = _party.Adventurers.Length > _index ? _party.Adventurers[_index] : null;
        var first = !_wrote;
        _wrote = true;
        var attackShown = focused || (_party.AllowPossession && adv is { Control: ControlKind.Companion });
        if (first || attackShown != _attackShown) _attack.Visible = _attackShown = attackShown;
        // a co-op hero bot says whose it is
        var botOwner = Net.NetSession.Instance?.Client?.Roster.TryGetValue(_index, out var known) == true ? known.BotOwner : null;
        var tag = focused ? 0 : adv switch { null => 1, { Parked: true } => 2, { Control: ControlKind.Remote } => 3, _ => 4 };
        var nameKey = (_index, focused, m.Name, m.Level, tag, tag == 3 ? botOwner : null, tag == 4 ? adv!.CurrentActionKind : ActionKind.Idle);
        if (first || !nameKey.Equals(_nameKey))
        {
            var tagText = tag switch { 0 => "[YOU]", 1 => "", 2 => "[away]", 3 => botOwner != null ? $"[{botOwner}'s bot]" : "[player]", _ => $"[{adv!.CurrentActionName}]" };
            _name.Text = $"{_index + 1} {m.Name}  L{m.Level}  {tagText}";
            if (first || focused != _nameKey.Focused) _name.AddThemeColorOverride("font_color", focused ? FocusColor : Colors.White);
            _nameKey = nameKey;
        }
        var stanceShown = adv is { Control: ControlKind.Companion };
        if (first || stanceShown != _stanceShown) _stance.Visible = _stanceShown = stanceShown;
        if (adv != null && (first || adv.Stance != _stanceShownAs)) { _stanceShownAs = adv.Stance; _stance.Text = adv.Stance.ToString(); }

        Bar(_hp, m.Stats[Stat.MaxHp], m.Hp, ref _hpMax, ref _hpNow);
        Bar(_stamina, m.Stats[Stat.MaxStamina], m.Stamina, ref _staminaMax, ref _staminaNow);
        Bar(_mana, Math.Max(1, m.Stats[Stat.MaxMana]), m.Mana, ref _manaMax, ref _manaNow);

        if (PortraitText.StatusesChanged(m, _statusKey)) _status.Text = PortraitText.Statuses(m);

        var ability = _party.GetAbility(m.ActiveAbilityId); // the name, id and kind are the upgraded ability's too (upgrades change numbers only)
        var cd = m.AbilityCooldown(ability.Id);
        var arrows = focused && m.WeaponProfile.IsRanged && ability.Kind == DungeonCrawler.Combat.AbilityKind.WeaponAttack ? _party.ArrowCount : -1;
        var attackKey = (m.IsAlive, focused, m.IsStunned, ability.Name, PortraitText.CooldownKey(cd), m.CooldownRemaining > 0, arrows, m.Fallen);
        if (first || !attackKey.Equals(_attackKey))
        {
            _attackKey = attackKey;
            _attack.Text = PortraitText.Attack(m.IsAlive, focused, _index, m.IsStunned, ability.Name, cd, m.CooldownRemaining, arrows >= 0 ? arrows : null, m.Fallen);
        }
        if (first || _attackDisabled != !m.IsAlive) _attack.Disabled = _attackDisabled = !m.IsAlive;
        if (_flashLeft <= 0f && _pulseLeft <= 0f) Modulate = m.IsAlive ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
    }

    /// <summary>Set a bar's maximum and value, each only when it changed (the maximum first, so the value is never clamped to a stale one).</summary>
    private static void Bar(ProgressBar bar, float max, float value, ref float shownMax, ref float shownValue)
    {
        if (max != shownMax) { bar.MaxValue = shownMax = max; shownValue = float.NaN; }
        if (value != shownValue) bar.Value = shownValue = value;
    }
}
