using DungeonCrawler.Characters;
using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.UI.Skills;

/// <summary>The K screen: one tab per member, three branch columns of chained skill nodes with the duo node drawn between the first two, keystones starred. Click a node to learn it.</summary>
public partial class SkillTreeScreen : PanelContainer
{
    private static readonly Color LearnedColor = new(1f, 0.85f, 0.4f);
    private static readonly Color AvailableColor = new(0.85f, 0.95f, 1f);
    private static readonly Color LockedColor = new(0.45f, 0.45f, 0.5f);

    private PartyController _party = null!;
    private HBoxContainer _tabs = null!;
    private Label _header = null!;
    private HBoxContainer _columns = null!;
    private readonly List<Button> _tabButtons = new();
    private int[] _tabMembers = Array.Empty<int>();
    private readonly List<(Button button, SkillProfile skill)> _nodes = new();

    public void Bind(PartyController party) => _party = party;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 14);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 6);
        root.AddChild(_tabs);
        BuildTabs();
        var hint = new Label { Text = "   Click a skill to learn it. Branches are chains; the Duo node opens from any branch's second tier. K: close", Modulate = new Color(0.7f, 0.7f, 0.7f) };
        hint.AddThemeFontSizeOverride("font_size", 12);
        _tabs.AddChild(hint);

        _header = new Label();
        root.AddChild(_header);

        _columns = new HBoxContainer();
        _columns.AddThemeConstantOverride("separation", 24);
        root.AddChild(_columns);
    }

    /// <summary>One tab per member in play, rebuilt when that set changes (in co-op the party grows after this screen is made).</summary>
    private void BuildTabs()
    {
        foreach (var b in _tabButtons) { _tabs.RemoveChild(b); b.QueueFree(); }
        _tabButtons.Clear();
        _tabMembers = _party.InPlayIndices();
        foreach (var idx in _tabMembers)
        {
            var b = new Button { Text = _party.Members[idx].Name, CustomMinimumSize = new Vector2(140, 34), FocusMode = FocusModeEnum.None };
            b.Pressed += () => { _party.SetFocus(idx); Refresh(); };
            _tabs.AddChild(b);
            _tabs.MoveChild(b, _tabButtons.Count); // the tabs stay ahead of the hint label
            _tabButtons.Add(b);
        }
    }

    public void Refresh()
    {
        if (!_party.InPlayIndices().SequenceEqual(_tabMembers)) BuildTabs();
        var m = _party.Members[_party.FocusedIndex];
        var cat = Game.Instance.Skills;
        for (var i = 0; i < _tabButtons.Count; i++)
            _tabButtons[i].AddThemeColorOverride("font_color", _tabMembers[i] == _party.FocusedIndex ? LearnedColor : Colors.White);
        _header.Text = $"{m.Name}  —  {m.Class.Name} L{m.Level}     Skill points: {m.SkillPoints}     Learned: {m.LearnedSkills.Count}";

        foreach (var c in _columns.GetChildren()) c.QueueFree();
        _nodes.Clear();
        var duo = cat.Duo(m.Class.Id);
        var columnsAdded = 0;
        foreach (var branch in cat.Branches(m.Class.Id))
        {
            // the duo node sits between the first two branch columns, level with their tier-3 row, pointing at both neighbours
            if (columnsAdded == 1 && duo != null) _columns.AddChild(DuoColumn(duo, m));
            columnsAdded++;
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 2);
            col.CustomMinimumSize = new Vector2(250, 0);
            var title = new Label { Text = branch, HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontSizeOverride("font_size", 16);
            col.AddChild(title);
            var first = true;
            foreach (var skill in cat.ForClass(m.Class.Id).Where(s => s.Branch == branch).OrderBy(s => s.Tier))
            {
                if (!first) col.AddChild(new Label { Text = "│", HorizontalAlignment = HorizontalAlignment.Center, Modulate = LockedColor });
                first = false;
                var b = new Button { CustomMinimumSize = new Vector2(250, 40), FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
                var sk = skill;
                b.Pressed += () => TryLearn(sk);
                col.AddChild(b);
                _nodes.Add((b, skill));
                Style(b, skill, m);
            }
            _columns.AddChild(col);
        }
    }

    /// <summary>A narrow column: a "Duo" title, three blank rows so the button lands level with tier 3, the duo button, and arrows at its neighbours.</summary>
    private VBoxContainer DuoColumn(SkillProfile duo, CharacterState m)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        col.CustomMinimumSize = new Vector2(170, 0);
        var title = new Label { Text = "Duo", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 16);
        col.AddChild(title);
        for (var row = 0; row < 3; row++)
        {
            if (row > 0) col.AddChild(new Label { Text = "│", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(LockedColor, 0f) });
            col.AddChild(new Control { CustomMinimumSize = new Vector2(170, 40) });
        }
        col.AddChild(new Label { Text = "│", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(LockedColor, 0f) });
        var b = new Button { CustomMinimumSize = new Vector2(170, 40), FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        b.Pressed += () => TryLearn(duo);
        col.AddChild(b);
        _nodes.Add((b, duo));
        Style(b, duo, m);
        foreach (var arrows in new[] { "◄  ►", "◄  ►" })
        {
            var l = new Label { Text = arrows, HorizontalAlignment = HorizontalAlignment.Center, Modulate = LockedColor };
            l.AddThemeFontSizeOverride("font_size", 12);
            col.AddChild(l);
        }
        return col;
    }

    private void Style(Button b, SkillProfile s, CharacterState m)
    {
        var learned = m.LearnedSkills.Contains(s.Id);
        var why = learned ? null : SkillRules.WhyCannotLearn(m, s);
        var available = !learned && why == null;
        b.Text = $"{(learned ? "✓ " : available ? "● " : "○ ")}{(s.IsKeystone ? "★ " : "")}{s.Name}{(s.Cost > 1 ? $"  ({s.Cost} pts)" : "")}";
        b.AddThemeColorOverride("font_color", learned ? LearnedColor : available ? AvailableColor : LockedColor);
        b.TooltipText = Describe(s, m, learned, why);
    }

    private string Describe(SkillProfile s, CharacterState m, bool learned, string? why)
    {
        var lines = new List<string> { s.Name, s.Description };
        foreach (var mod in s.Modifiers) lines.Add("  " + Items.StatModifierParser.Describe(mod));
        if (s.GrantsAbility != "") lines.Add($"  Grants: {_party.AbilityName(s.GrantsAbility)}");
        foreach (var u in s.AllUpgrades)
        {
            var parts = new List<string>();
            if (u.DamageMul != 1) parts.Add($"damage x{u.DamageMul:0.##}");
            if (u.CooldownMul != 1) parts.Add($"cooldown x{u.CooldownMul:0.##}");
            if (u.CostMul != 1) parts.Add($"cost x{u.CostMul:0.##}");
            if (u.BuffMul != 1) parts.Add($"effect x{u.BuffMul:0.##}");
            if (u.RadiusAdd != 0) parts.Add($"radius +{u.RadiusAdd:0.#} m");
            if (u.ExtraProjectiles != 0) parts.Add($"+{u.ExtraProjectiles} projectiles");
            if (u.AddStatus is { } st) parts.Add($"adds {st.Kind}");
            if (u.OnHitDurationMul != 1) parts.Add($"status duration x{u.OnHitDurationMul:0.##}");
            if (u.AddEffect is { } ef) parts.Add($"also {ef.Kind} {ef.Magnitude:0}");
            var names = string.Join(", ", u.AbilityId.Split(',').Select(id => _party.AbilityName(id.Trim())));
            lines.Add($"  Upgrades {names}: {string.Join(", ", parts)}");
        }
        if (s.Trigger != TriggerKind.None) lines.Add($"  Passive: {s.Trigger} ({s.TriggerMagnitude:0.##})");
        if (s.Trigger2 != TriggerKind.None) lines.Add($"  Passive: {s.Trigger2} ({s.TriggerMagnitude2:0.##})");
        if (s.IsDuo) lines.Add($"  Needs one of: {string.Join(", ", s.AnyOf.Select(id => Game.Instance.Skills.Get(id)?.Name ?? id))}");
        if (s.IsKeystone) lines.Add("  Keystone: a price comes with it.");
        lines.Add($"Cost: {s.Cost} skill point{(s.Cost == 1 ? "" : "s")}");
        if (learned) lines.Add("Learned");
        else if (why != null) lines.Add(why);
        return string.Join("\n", lines);
    }

    private void TryLearn(SkillProfile s)
    {
        _party.LearnSkill(_party.FocusedIndex, s);
        Refresh();
    }
}
