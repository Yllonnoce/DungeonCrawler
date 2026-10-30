using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using DungeonCrawler.UI;
using DungeonCrawler.UI.Inventory;
using DungeonCrawler.UI.Menus;
using DungeonCrawler.UI.Dialogue;
using DungeonCrawler.UI.Quests;
using DungeonCrawler.UI.Skills;
using Godot;

namespace DungeonCrawler.UI.Hud;

/// <summary>In-game overlay: portraits, floor label, message log, crosshair and interaction prompt.</summary>
public partial class Hud : Control
{
    private const int MaxLogLines = 6;

    private PartyController _party = null!;
    private Label _floorLabel = null!;
    private Label _log = null!;
    private Label _prompt = null!;
    private HBoxContainer _portraits = null!;
    private readonly List<PortraitPanel> _panels = new();
    private readonly LinkedList<string> _lines = new();
    private PackedScene _portraitScene = null!;
    private MapView _minimap = null!;
    private MapView _fullMap = null!;
    private CharacterSheet _sheet = null!;
    private InventoryScreen _inventory = null!;
    public InventoryScreen Inventory => _inventory;
    public bool InventoryOpen => _inventory.Visible;
    public bool DialogueOpen => _dialogue.Visible;
    private PauseMenu _pause = null!;
    private SkillTreeScreen _skills = null!;
    public bool Paused => _pause.Visible;
    private DialogueScreen _dialogue = null!;
    public DialogueScreen Dialogue => _dialogue;
    private QuestLogScreen _questLog = null!;
    private StairsPrompt _stairsPrompt = null!;
    private Label _objective = null!;
    private Label _subtitle = null!;
    private Label _mangled = null!;
    private float _mangledSince;
    private float _subtitleTimer;

    public void Bind(PartyController party) => _party = party;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _floorLabel = GetNode<Label>("%FloorLabel");
        _log = GetNode<Label>("%MessageLog");
        _prompt = GetNode<Label>("%Prompt");
        _portraits = GetNode<HBoxContainer>("%Portraits");
        _portraitScene = GD.Load<PackedScene>("res://scenes/UI/PortraitPanel.tscn");

        BuildPortraits();

        _minimap = new MapView
        {
            Name = "Minimap",
            CornerCellPixels = 7f,
            CustomMinimumSize = new Vector2(230, 230),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _minimap.AnchorLeft = 1; _minimap.AnchorRight = 1; _minimap.AnchorTop = 0; _minimap.AnchorBottom = 0;
        _minimap.OffsetLeft = -246; _minimap.OffsetRight = -16; _minimap.OffsetTop = 16; _minimap.OffsetBottom = 246;
        _minimap.GrowHorizontal = GrowDirection.Begin;
        AddChild(_minimap);

        _fullMap = new MapView { Name = "FullMap", FullMap = true, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _fullMap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _fullMap.OffsetLeft = 80; _fullMap.OffsetTop = 60; _fullMap.OffsetRight = -80; _fullMap.OffsetBottom = -150;
        AddChild(_fullMap);
        MoveChild(_fullMap, 0); // behind portraits and labels

        _sheet = new CharacterSheet { Name = "CharacterSheet", Visible = false };
        _sheet.Bind(_party);
        AddChild(_sheet);
        _sheet.SetAnchorsPreset(LayoutPreset.Center);
        _sheet.Position = new Vector2(GetViewportRect().Size.X / 2 - 280, 80);

        _inventory = new InventoryScreen { Name = "Inventory", Visible = false };
        _inventory.Bind(_party);
        AddChild(_inventory);
        _inventory.SetAnchorsPreset(LayoutPreset.CenterTop);
        _inventory.Position = new Vector2(GetViewportRect().Size.X / 2 - 480, 30);

        _skills = new SkillTreeScreen { Name = "SkillTree", Visible = false };
        _skills.Bind(_party);
        AddChild(_skills);
        _skills.SetAnchorsPreset(LayoutPreset.CenterTop);
        _skills.Position = new Vector2(GetViewportRect().Size.X / 2 - 430, 40);

        _chat = new LineEdit { Name = "ChatLine", Visible = false, PlaceholderText = "Say something (Enter to send, Esc to cancel)", MaxLength = Net.Protocol.MaxChatLength, CustomMinimumSize = new Vector2(520, 34) };
        _chat.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _chat.Position = new Vector2(-260, -120);
        _chat.TextSubmitted += text => { var t = text.Trim(); if (t.Length > 0 && Net.NetSession.IsClient) Net.NetSession.Instance.Send(new Net.ChatIntent(t)); CloseChat(); };
        AddChild(_chat);
        _pause = new PauseMenu { Name = "PauseMenu", Visible = false };
        AddChild(_pause);

        _objective = new Label { Name = "Objective", Modulate = new Color(0.95f, 0.85f, 0.55f) };
        _objective.AnchorLeft = 0; _objective.AnchorRight = 0; _objective.AnchorTop = 0; _objective.AnchorBottom = 0;
        _objective.OffsetLeft = 16; _objective.OffsetTop = 44; _objective.OffsetRight = 620; _objective.OffsetBottom = 68;
        AddChild(_objective);
        _subtitle = new Label { Name = "Subtitle", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Word, Modulate = new Color(0.95f, 0.93f, 0.85f) };
        _subtitle.AddThemeFontSizeOverride("font_size", 18);
        _subtitle.AnchorLeft = 0.5f; _subtitle.AnchorRight = 0.5f; _subtitle.AnchorTop = 1; _subtitle.AnchorBottom = 1;
        _subtitle.OffsetLeft = -420; _subtitle.OffsetRight = 420; _subtitle.OffsetTop = -215; _subtitle.OffsetBottom = -165;
        AddChild(_subtitle);
        _mangled = new Label { Name = "Mangled", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.95f, 0.4f, 0.35f), Visible = false };
        _mangled.AddThemeFontSizeOverride("font_size", 20);
        _mangled.AnchorLeft = 0.5f; _mangled.AnchorRight = 0.5f; _mangled.AnchorTop = 0; _mangled.AnchorBottom = 0;
        _mangled.OffsetLeft = -360; _mangled.OffsetRight = 360; _mangled.OffsetTop = 80; _mangled.OffsetBottom = 110;
        AddChild(_mangled);

        _dialogue = new DialogueScreen { Name = "Dialogue", Visible = false };
        _dialogue.Closed += UpdateModalState;
        AddChild(_dialogue);
        _stairsPrompt = new StairsPrompt { Name = "StairsPrompt" };
        _stairsPrompt.Closed += UpdateModalState;
        AddChild(_stairsPrompt);
        _questLog = new QuestLogScreen { Name = "QuestLog", Visible = false };
        AddChild(_questLog);
        _questLog.SetAnchorsPreset(LayoutPreset.CenterTop);
        _questLog.Position = new Vector2(GetViewportRect().Size.X / 2 - 310, 80);

        var bus = EventBus.Instance;
        bus.FloorChanged += OnFloorChanged;
        bus.MemberChanged += OnMemberChanged;
        bus.RosterChanged += BuildPortraits;
        bus.ItemPickedUp += OnItemPickedUp;
        bus.Message += OnMessage;
        bus.ObjectiveChanged += OnObjectiveChanged;
        bus.Narration += OnNarration;
        bus.QuestChanged += OnQuestChanged;
        OnFloorChanged(Game.Instance.CurrentFloor, 0);
    }

    public override void _ExitTree()
    {
        var bus = EventBus.Instance;
        bus.FloorChanged -= OnFloorChanged;
        bus.MemberChanged -= OnMemberChanged;
        bus.RosterChanged -= BuildPortraits;
        bus.ItemPickedUp -= OnItemPickedUp;
        bus.Message -= OnMessage;
        bus.ObjectiveChanged -= OnObjectiveChanged;
        bus.Narration -= OnNarration;
        bus.QuestChanged -= OnQuestChanged;
    }

    public string ObjectiveText => _objective.Text;
    private void OnObjectiveChanged(string text) => _objective.Text = text;
    private void OnQuestChanged(QuestState q) { if (_questLog.Visible) _questLog.Refresh(); }
    private void OnNarration(string text, string? voiceId)
    {
        _subtitle.Text = text;
        _subtitleTimer = Math.Clamp(2f + text.Length * 0.06f, 3f, 10f);
    }

    public void OpenDialogue(Npc npc)
    {
        if (_pause.Visible) return;
        _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _questLog.Visible = false;
        _dialogue.Open(npc);
        UpdateModalState();
    }

    public void ToggleQuestLog()
    {
        EventBus.Instance.PlaySfx(_questLog.Visible ? "ui_close" : "ui_open");
        _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false;
        _questLog.Visible = !_questLog.Visible;
        if (_questLog.Visible) _questLog.Refresh();
        UpdateModalState();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause"))
        {
            TogglePause();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_pause.Visible) return;
        if (_chat.Visible)
        {
            if (@event.IsActionPressed("ui_cancel")) { CloseChat(); GetViewport().SetInputAsHandled(); }
            return; // the chat line owns the keys while open
        }
        if (@event.IsActionPressed("chat") && Net.NetSession.IsClient && !_sheet.Visible && !_inventory.Visible && !_skills.Visible && !_dialogue.Visible && !_questLog.Visible)
        {
            _chat.Text = "";
            _chat.Visible = true;
            _chat.GrabFocus();
            UpdateModalState();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_dialogue.Visible || _stairsPrompt.Visible) return; // the dialogue / stair question owns input while open
        if (@event.IsActionPressed("quest_log"))
        {
            ToggleQuestLog();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed("quicksave") && !Net.NetSession.IsNetworked)
        {
            Main.Instance.SaveGame(Save.SaveManager.QuickSlot);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed("quickload") && !Net.NetSession.IsNetworked)
        {
            if (!Main.Instance.LoadGame(Save.SaveManager.QuickSlot)) EventBus.Instance.Log("No quicksave to load.");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed("map"))
        {
            _fullMap.Visible = !_fullMap.Visible;
            _minimap.Visible = !_fullMap.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("character"))
        {
            ToggleSheet();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("inventory"))
        {
            ToggleInventory();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("skills"))
        {
            ToggleSkills();
            GetViewport().SetInputAsHandled();
        }
        else if ((_sheet.Visible || _skills.Visible) && @event.IsActionPressed("focus_next"))
        {
            _party.SetFocus((_party.FocusedIndex + 1) % _party.Members.Length);
            if (_sheet.Visible) _sheet.Refresh();
            if (_skills.Visible) _skills.Refresh();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (GetTree().Paused || !IsInstanceValid(_party) || !_party.HasBodies) return;
        var target = _party.LookingAt;
        _prompt.Text = target == null || _sheet.Visible || _inventory.Visible || _skills.Visible || _dialogue.Visible || _questLog.Visible ? "" : $"[E] {target.Prompt}";
        if (_subtitleTimer > 0f) { _subtitleTimer -= (float)delta; if (_subtitleTimer <= 0f) _subtitle.Text = ""; }
        TickMangledBanner((float)delta);
        if (_sheet.Visible) _sheet.Refresh();
        TickNetLabel();
        foreach (var p in _panels) p.Refresh(); // cheap; keeps cooldown text live
    }

    /// <summary>A crushed player on a client: how long until the server carries them ahead, then that they can be freed.</summary>
    private void TickMangledBanner(float dt)
    {
        if (_party.Local is { Downed: true, State.Mangled: true } && Net.NetSession.IsClient)
        {
            _mangledSince += dt;
            var left = MangledRules.BoundAfterSeconds - _mangledSince;
            _mangled.Text = left > 0f ? $"Crushed. Rescue in {Math.Ceiling(left):0}s, or wait to be found" : "Crushed. Bound in the dark ahead; a friend can free you";
            _mangled.Visible = true;
        }
        else { _mangledSince = 0f; _mangled.Visible = false; }
    }

    public void Refresh()
    {
        foreach (var p in _panels) p.Refresh();
        OnFloorChanged(Game.Instance.CurrentFloor, 0);
    }

    public void RefreshFloorLabel() => OnFloorChanged(Game.Instance.CurrentFloor, 0);

    public void ToggleSkills()
    {
        EventBus.Instance.PlaySfx(_skills.Visible ? "ui_close" : "ui_open");
        _inventory.Visible = false;
        _sheet.Visible = false;
        _skills.Visible = !_skills.Visible;
        if (_skills.Visible) _skills.Refresh();
        UpdateModalState();
    }

    public void ToggleSheet()
    {
        if (_inventory.Visible) _inventory.Visible = false;
        _skills.Visible = false;
        _sheet.Visible = !_sheet.Visible;
        if (_sheet.Visible) _sheet.Refresh();
        UpdateModalState();
    }

    public void ToggleInventory()
    {
        EventBus.Instance.PlaySfx(_inventory.Visible ? "ui_close" : "ui_open");
        if (_sheet.Visible) _sheet.Visible = false;
        _skills.Visible = false;
        _inventory.Visible = !_inventory.Visible;
        if (_inventory.Visible) _inventory.Refresh();
        UpdateModalState();
    }

    public void TogglePause()
    {
        if (_pause.Visible) _pause.Visible = false;
        else { _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _pause.Open(); }
        _prompt.Text = "";
        GetTree().Paused = _pause.Visible && !Net.NetSession.IsNetworked; // a server does not wait for anyone
        UpdateModalState();
    }

    /// <summary>A landing was reached: ask whether to take the stairs.</summary>
    public void AskStairs(Dungeon.Stairs stairs, Adventurer actor)
    {
        if (_pause.Visible || _dialogue.Visible || _victory != null) return;
        _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _questLog.Visible = false;
        _prompt.Text = "";
        _stairsPrompt.Open(stairs, actor);
        UpdateModalState();
    }

    /// <summary>The player stepped off the landing: the question goes away.</summary>
    public void DismissStairs(Dungeon.Stairs stairs) => _stairsPrompt.Dismiss(stairs);

    public bool StairsPromptOpen => _stairsPrompt.Visible;

    public void UpdateModalState()
    {
        var modal = _sheet.Visible || _inventory.Visible || _skills.Visible || _pause.Visible || _victory != null || _dialogue.Visible || _questLog.Visible || _chat.Visible || _stairsPrompt.Visible;
        _party.InputLocked = modal;
        _party.SetMouseCaptured(!modal);
    }

    private LineEdit _chat = null!;
    private string _floorText = "";
    private int _pingFrames;

    private void CloseChat()
    {
        _chat.Visible = false;
        _chat.ReleaseFocus();
        UpdateModalState();
    }

    private void OnFloorChanged(int floor, int delta)
    {
        var tier = Main.Instance?.Builder.CurrentTier?.DisplayName ?? "";
        var theme = Main.Instance?.Builder.CurrentTheme?.DisplayName;
        var place = theme != null && theme != tier ? $"{tier}, {theme}" : tier;
        var suffix = DungeonCrawler.Dungeon.TierRules.IsEndless(floor) ? "  (endless)" : DungeonCrawler.Dungeon.TierRules.IsBossFloor(floor) ? "  (guardian)" : "";
        _floorText = tier == "" ? $"Floor {floor}" : $"Floor {floor}  —  {place}{suffix}";
        _floorLabel.Text = _floorText;
    }

    /// <summary>On a server: the round trip and who is online, beside the floor name.</summary>
    private void TickNetLabel()
    {
        if (!Net.NetSession.IsClient || Net.NetSession.Instance.Client is not { } client) return;
        if (++_pingFrames % 30 != 0) return;
        var online = _party.Adventurers.Count(a => !a.Parked);
        _floorLabel.Text = $"{_floorText}   ·   {client.Rtt * 1000:0} ms, {online} online";
    }

    private Control? _victory;

    public void ShowVictory()
    {
        if (_victory != null) return;
        EventBus.Instance.PlaySfx("victory");
        var game = Game.Instance;
        _victory = new PanelContainer { Name = "Victory" };
        AddChild(_victory);
        _victory.SetAnchorsPreset(LayoutPreset.Center);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 24);
        _victory.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        margin.AddChild(v);
        var title = new Label { Text = "VICTORY", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 36);
        v.AddChild(title);
        v.AddChild(new Label { Text = "The Abyss Herald is slain. The dungeon's heart lies open.", HorizontalAlignment = HorizontalAlignment.Center });
        v.AddChild(new Label { Text = $"Time: {Save.SlotInfo.FormatTime(game.PlaySeconds)}    Party: {string.Join(", ", _party.Members.Select(m => $"{m.Name} L{m.Level}"))}", HorizontalAlignment = HorizontalAlignment.Center });
        v.AddChild(new Label { Text = "The stairs still lead down. Beyond here the depths only grow crueler.", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.75f, 0.75f, 0.75f) });
        var btn = new Button { Text = "Continue into the endless depths", FocusMode = FocusModeEnum.None };
        btn.Pressed += () => { _victory.QueueFree(); _victory = null; UpdateModalState(); };
        v.AddChild(btn);
        _party.InputLocked = true;
        _party.SetMouseCaptured(false);
        Callable.From(() => _victory.Position = (GetViewportRect().Size - _victory.Size) / 2f).CallDeferred();
    }
    private void OnMemberChanged(int index) { if (index < _panels.Count) _panels[index].Refresh(); }

    /// <summary>One portrait per member; rebuilt when players join or leave a server.</summary>
    private void BuildPortraits()
    {
        foreach (var p in _panels) p.QueueFree();
        _panels.Clear();
        for (var i = 0; i < _party.Members.Length; i++)
        {
            var panel = _portraitScene.Instantiate<PortraitPanel>();
            _portraits.AddChild(panel);
            panel.Bind(_party, i);
            _panels.Add(panel);
        }
    }
    private void OnItemPickedUp(ItemInstance item) { if (_inventory.Visible) _inventory.Refresh(); }

    private void OnMessage(string text)
    {
        _lines.AddLast(text);
        while (_lines.Count > MaxLogLines) _lines.RemoveFirst();
        _log.Text = string.Join('\n', _lines);
    }
}
