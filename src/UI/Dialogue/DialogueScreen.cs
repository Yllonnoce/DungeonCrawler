using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.UI.Dialogue;

/// <summary>
/// Modal conversation with an NPC: the current line and up to four numbered choices. Choices that need a quest state
/// the player is not in (accept when already accepted, turn in before it is done) are hidden.
/// </summary>
public partial class DialogueScreen : Control
{
    public event Action? Closed;
    public Npc? Npc { get; private set; }
    public DialogueNode? Node { get; private set; }
    public IReadOnlyList<ChoicePlan> Choices => _shown;

    private Label _name = null!;
    private Label _text = null!;
    private VBoxContainer _choices = null!;
    private readonly List<ChoicePlan> _shown = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        this.FillParent();
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.FillParent();
        AddChild(dim);
        var centre = new CenterContainer();
        centre.FillParent();
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        centre.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 22);
        panel.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        margin.AddChild(v);
        _name = new Label();
        _name.AddThemeFontSizeOverride("font_size", 24);
        v.AddChild(_name);
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(660, 0) };
        _text.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(_text);
        v.AddChild(new HSeparator());
        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 6);
        v.AddChild(_choices);
    }

    public void Open(Npc npc)
    {
        Npc = npc;
        Visible = true;
        EventBus.Instance.PlaySfx("ui_open");
        // floors built from an older plan may still carry a script with no way to hand the quest in; mend it on the spot
        if (AdventureDirector.Instance?.QuestGivenBy(npc.Id) is { } given)
            foreach (var fix in DialogueRepair.EnsureReachable(npc.Script, given.Template, given.CompleteLine, given.Id)) GD.Print($"[Dialogue] {npc.Id}: {fix}");
        var start = npc.Script.Nodes.FirstOrDefault(n => n.Id == npc.Script.Start) ?? npc.Script.Nodes.FirstOrDefault();
        if (start == null) { Close(); return; }
        Show(start);
    }

    private void Show(DialogueNode node)
    {
        Node = node;
        _name.Text = Npc?.NpcName ?? "";
        _text.Text = node.Line.Text;
        VoiceRenderer.Instance?.PlayLine(node.Line, interrupt: true);
        foreach (var c in _choices.GetChildren()) c.QueueFree();
        _shown.Clear();
        var quest = Npc == null ? null : AdventureDirector.Instance?.QuestGivenBy(Npc.Id);
        var flags = Game.Instance.Adventure.Memory.Flags;
        foreach (var choice in node.Choices)
        {
            var visible = choice.Effect switch
            {
                DialogueEffect.AcceptQuest or DialogueEffect.DeclineQuest => quest is { Status: QuestStatus.Offered },
                DialogueEffect.TurnIn => quest is { Status: QuestStatus.ReadyToTurnIn },
                DialogueEffect.Choose => quest == null || quest.Template != QuestTemplate.Decide || quest.Status is QuestStatus.Offered or QuestStatus.Active,
                DialogueEffect.Betray => Npc?.Turned == false,
                _ => true,
            };
            if (!visible || !FlagRules.Allowed(choice.RequiresFlag, choice.ForbidsFlag, flags)) continue;
            _shown.Add(choice);
        }
        if (_shown.Count == 0) _shown.Add(new ChoicePlan { Text = "Farewell.", Effect = DialogueEffect.Farewell });
        for (var i = 0; i < _shown.Count && i < 4; i++)
        {
            var index = i;
            var btn = new Button { Text = $"{i + 1}.  {_shown[i].Text}", FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
            btn.Pressed += () => Choose(index);
            _choices.AddChild(btn);
        }
    }

    public void Choose(int index)
    {
        if (Npc == null || index < 0 || index >= _shown.Count) return;
        var choice = _shown[index];
        if (choice.Effect != DialogueEffect.None)
        {
            if (Net.NetSession.IsClient) Net.NetSession.Instance.Send(new Net.DialogueChoiceIntent(Npc.Id, choice.Effect, choice.Flag)); // the server keeps the quests
            else EventBus.Instance.RaiseDialogueEffect(Npc.Id, choice.Effect, choice.Flag);
        }
        if (choice.Effect is DialogueEffect.Farewell or DialogueEffect.Betray || choice.Next == null) { Close(); return; }
        var next = Npc.Script.Nodes.FirstOrDefault(n => n.Id == choice.Next);
        if (next == null) { Close(); return; }
        Show(next);
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        Npc = null;
        Node = null;
        EventBus.Instance.PlaySfx("ui_close");
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            var n = key.Keycode switch { Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, Key.Escape => -2, _ => -1 };
            if (n == -2) { Close(); GetViewport().SetInputAsHandled(); }
            else if (n >= 0) { Choose(n); GetViewport().SetInputAsHandled(); }
        }
    }
}
