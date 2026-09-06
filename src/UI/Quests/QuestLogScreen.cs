using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.UI.Quests;

/// <summary>Journal (J): open quests on this floor first, then the rest of the run.</summary>
public partial class QuestLogScreen : PanelContainer
{
    private VBoxContainer _list = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        CustomMinimumSize = new Vector2(620, 320);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 18);
        AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        margin.AddChild(v);
        var title = new Label { Text = "Journal" };
        title.AddThemeFontSizeOverride("font_size", 24);
        v.AddChild(title);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(600, 260) };
        v.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_list);
    }

    public void Refresh()
    {
        foreach (var c in _list.GetChildren()) c.QueueFree();
        var quests = AdventureDirector.Instance?.Quests.Values.ToList() ?? new List<QuestState>();
        var floor = Game.Instance.CurrentFloor;
        var ordered = quests.OrderBy(q => q.IsTerminal ? 1 : 0).ThenBy(q => q.Floor == floor ? 0 : 1).ThenBy(q => q.Floor).ToList();
        if (ordered.Count == 0) { _list.AddChild(new Label { Text = "No tasks yet. Someone down here may need help." }); return; }
        foreach (var q in ordered)
        {
            var head = new Label { Text = $"[{q.Status}]  {q.Title}   (floor {q.Floor})" };
            head.AddThemeFontSizeOverride("font_size", 17);
            if (q.IsTerminal) head.Modulate = new Color(0.6f, 0.6f, 0.6f);
            _list.AddChild(head);
            _list.AddChild(new Label { Text = "    " + q.ObjectiveText, AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(580, 0), Modulate = new Color(0.85f, 0.85f, 0.8f) });
        }
    }
}
