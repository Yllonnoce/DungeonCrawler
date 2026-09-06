using DungeonCrawler.Characters;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.UI;

/// <summary>Simple character sheet: primaries with + buttons for unspent points, derived stats, level and XP.</summary>
public partial class CharacterSheet : PanelContainer
{
    private PartyController _party = null!;
    private Label _title = null!;
    private Label _derived = null!;
    private GridContainer _grid = null!;
    private readonly Dictionary<Stat, (Label value, Button plus)> _rows = new();

    public void Bind(PartyController party) => _party = party;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(560, 0);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        _title = new Label();
        vbox.AddChild(_title);

        var hint = new Label { Text = "F: next character   Q / wheel: cycle ability   R: swap rank   C: close", Modulate = new Color(0.7f, 0.7f, 0.7f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(hint);

        _grid = new GridContainer { Columns = 3 };
        _grid.AddThemeConstantOverride("h_separation", 16);
        vbox.AddChild(_grid);
        foreach (var s in StatInfo.Primaries)
        {
            var name = new Label { Text = $"{StatInfo.ShortName(s)}  {s}", CustomMinimumSize = new Vector2(220, 0) };
            var value = new Label { Text = "0", CustomMinimumSize = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(36, 0), FocusMode = FocusModeEnum.None };
            var stat = s;
            plus.Pressed += () => { _party.AllocateStat(_party.FocusedIndex, stat); Refresh(); };
            _grid.AddChild(name); _grid.AddChild(value); _grid.AddChild(plus);
            _rows[s] = (value, plus);
        }

        _derived = new Label();
        _derived.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_derived);
    }

    public void Refresh()
    {
        var m = _party.Members[_party.FocusedIndex];
        var st = m.Stats;
        _title.Text = $"{m.Name}  —  {m.Class.Name}  L{m.Level}    XP {m.Xp}/{Leveling.XpToNext(m.Level)}    Stat points: {m.StatPoints}   Skill points: {m.SkillPoints}";
        foreach (var (s, (value, plus)) in _rows)
        {
            value.Text = $"{st[s]:0}";
            // hidden children vacate their grid cell, so fade and disable instead of hiding
            plus.Disabled = m.StatPoints <= 0;
            plus.Modulate = m.StatPoints > 0 ? Colors.White : new Color(1, 1, 1, 0.25f);
        }
        var weapon = m.Weapon == null ? "Fists" : $"{m.Weapon.DisplayName} ({m.WeaponProfile.MinDamage:0}-{m.WeaponProfile.MaxDamage:0})";
        _derived.Text =
            $"HP {m.Hp:0}/{st[Stat.MaxHp]:0}   Stamina {m.Stamina:0}/{st[Stat.MaxStamina]:0}   Mana {m.Mana:0}/{st[Stat.MaxMana]:0}\n" +
            $"Weapon: {weapon}   Rank: {m.Rank}   {(m.Class.CanBlock ? "Can block" : "")}\n" +
            $"Melee +{st[Stat.MeleeDamage]:0.0}   Ranged +{st[Stat.RangedDamage]:0.0}   Spell +{st[Stat.SpellPower]:0.0}\n" +
            $"Accuracy {st[Stat.Accuracy]:0}   Evasion {st[Stat.Evasion]:0}   Armor {st[Stat.Armor]:0}   Crit {st[Stat.CritChance]:P0} x{st[Stat.CritMultiplier]:0.00}\n" +
            $"Regen  HP {st[Stat.HpRegen]:0.00}/s   Stamina {st[Stat.StaminaRegen]:0.0}/s   Mana {st[Stat.ManaRegen]:0.0}/s   Stealth {st[Stat.Stealth]:0}   Perception {st[Stat.Perception]:0}\n" +
            $"Resist  Fire {st[Stat.ResistFire]:0}%  Frost {st[Stat.ResistFrost]:0}%  Poison {st[Stat.ResistPoison]:0}%  Arcane {st[Stat.ResistArcane]:0}%\n" +
            $"Abilities: {string.Join(", ", m.KnownAbilities.Select(id => id == m.ActiveAbilityId ? $"[{_party.AbilityName(id)}]" : _party.AbilityName(id)))}";
    }
}
