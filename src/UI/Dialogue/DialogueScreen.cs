using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.UI.Dialogue;

/// <summary>
/// Modal conversation with an NPC: the current line and up to four numbered choices. Choices that need a quest state
/// the player is not in (accept when already accepted, turn in before it is done) are hidden. An NPC with
/// <see cref="Npc.ShowMoves"/> also gets a grid of its model's clips under the choices: click one and the character
/// plays it while the conversation stays open.
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
    private CenterContainer _centre = null!;
    private VBoxContainer _moves = null!;
    private Label _moveStatus = null!;
    private readonly List<ChoicePlan> _shown = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        this.FillParent();
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.FillParent();
        AddChild(dim);
        _centre = new CenterContainer();
        _centre.FillParent();
        AddChild(_centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        _centre.AddChild(panel);
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
        _moves = new VBoxContainer { Visible = false };
        _moves.AddThemeConstantOverride("separation", 8);
        v.AddChild(_moves);
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
        BuildMoves();
    }

    /// <summary>One button per clip the NPC's model has, four to a row; only for an NPC that shows its moves.</summary>
    private void BuildMoves()
    {
        foreach (var c in _moves.GetChildren()) c.QueueFree();
        var clips = Npc is { ShowMoves: true } npc ? npc.ModelClips : Array.Empty<string>();
        _moves.Visible = clips.Count > 0;
        _centre.AnchorBottom = clips.Count > 0 ? 0.6f : 1f; // with moves on offer the panel sits high, so the character below stays in view
        if (clips.Count == 0) return;
        _moves.AddChild(new HSeparator());
        var title = new Label { Text = $"{Npc!.NpcName} can show you a move:" };
        title.AddThemeFontSizeOverride("font_size", 15);
        _moves.AddChild(title);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        _moves.AddChild(grid);
        foreach (var clip in clips)
        {
            var name = clip;
            var btn = new Button { Text = name, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            btn.Pressed += () => ShowMove(name);
            grid.AddChild(btn);
        }
        _moveStatus = new Label { Text = "" };
        _moveStatus.AddThemeFontSizeOverride("font_size", 14);
        _moves.AddChild(_moveStatus);
    }

    /// <summary>Play one of the NPC's clips; the dialogue stays open. Unknown clips are ignored.</summary>
    public void ShowMove(string clip)
    {
        if (Npc is not { ShowMoves: true } npc || !npc.ModelClips.Contains(clip)) return;
        npc.Showcase(clip);
        _moveStatus.Text = $"Showing: {clip}";
        GD.Print($"[Dialogue] {npc.Id} shows {clip}");
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
        Npc?.Showcase(null); // back to Idle once the conversation ends
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
