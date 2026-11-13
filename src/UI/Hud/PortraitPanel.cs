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
        Refresh();
    }

    public void Refresh()
    {
        var m = _party.Members[_index];
        var focused = _party.LocalIndex == _index;
        var adv = _party.Adventurers.Length > _index ? _party.Adventurers[_index] : null;
        _attack.Visible = focused || (_party.AllowPossession && adv is { Control: ControlKind.Companion });
        // a co-op hero bot says whose it is
        var botOwner = Net.NetSession.Instance?.Client?.Roster.TryGetValue(_index, out var known) == true ? known.BotOwner : null;
        var tag = focused ? "[YOU]" : adv switch { null => "", { Parked: true } => "[away]", { Control: ControlKind.Remote } => botOwner != null ? $"[{botOwner}'s bot]" : "[player]", _ => $"[{adv.CurrentActionName}]" };
        _name.Text = $"{_index + 1} {m.Name}  L{m.Level}  {tag}";
        _stance.Visible = adv is { Control: ControlKind.Companion };
        if (adv != null) _stance.Text = adv.Stance.ToString();
        _name.AddThemeColorOverride("font_color", focused ? FocusColor : Colors.White);

        _hp.MaxValue = m.Stats[Stat.MaxHp]; _hp.Value = m.Hp;
        _stamina.MaxValue = m.Stats[Stat.MaxStamina]; _stamina.Value = m.Stamina;
        _mana.MaxValue = Math.Max(1, m.Stats[Stat.MaxMana]); _mana.Value = m.Mana;

        var statuses = string.Join("  ", m.Effects.Active.Select(e => e.ToString()));
        if (m.StatPoints > 0) statuses = (statuses.Length > 0 ? statuses + "  " : "") + $"+{m.StatPoints} pts (C)";
        if (m.SkillPoints > 0) statuses = (statuses.Length > 0 ? statuses + "  " : "") + $"+{m.SkillPoints} skill (K)";
        _status.Text = statuses;

        var ability = _party.GetAbilityFor(m, m.ActiveAbilityId);
        var cd = m.AbilityCooldown(ability.Id);
        _attack.Disabled = !m.IsAlive;
        _attack.Text = !m.IsAlive ? "Down"
            : !focused ? $"Control ({_index + 1})"
            : m.IsStunned ? "Stunned"
            : cd > 0 ? $"{ability.Name} ({cd:0.0}s)"
            : m.CooldownRemaining > 0 ? $"{ability.Name} ..."
            : ability.Name;
        if (focused && m.WeaponProfile.IsRanged && ability.Kind == DungeonCrawler.Combat.AbilityKind.WeaponAttack) _attack.Text += $"  [{_party.ArrowCount}]";
        if (_flashLeft <= 0f && _pulseLeft <= 0f) Modulate = m.IsAlive ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
    }
}
