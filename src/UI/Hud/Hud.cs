using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using DungeonCrawler.UI;
using DungeonCrawler.UI.Inventory;
using DungeonCrawler.UI.Menus;
using DungeonCrawler.UI.Dialogue;
using DungeonCrawler.UI.Quests;
using DungeonCrawler.UI.Shop;
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
    public DamageNumbers DamageNumbers { get; private set; } = null!;
    public HitMarker HitMarker { get; private set; } = null!;
    public EnemyBars EnemyBars { get; private set; } = null!;
    public BossBar BossBar { get; private set; } = null!;
    public Vignette Vignette { get; private set; } = null!;
    public LevelUpBanner LevelUpBanner { get; private set; } = null!;
    public LootToast LootToast { get; private set; } = null!;
    private CharacterSheet _sheet = null!;
    private InventoryScreen _inventory = null!;
    public InventoryScreen Inventory => _inventory;
    public bool InventoryOpen => _inventory.Visible;
    public bool DialogueOpen => _dialogue.Visible;
    private ShopScreen _shop = null!;
    /// <summary>The merchant's shop; <see cref="ShopScreen.Open"/> closes the dialogue and the other screens first.</summary>
    public ShopScreen Shop => _shop;
    public bool ShopOpen => _shop.Visible;
    private UI.Fortune.ShrineCard _shrine = null!;
    /// <summary>The fortune shrine's boon card (fortune): opened by the FortuneDirector solo, by ShrineCardMsg on a client.</summary>
    public UI.Fortune.ShrineCard Shrine => _shrine;
    public bool ShrineOpen => _shrine.Visible;
    private PauseMenu _pause = null!;
    private SkillTreeScreen _skills = null!;
    public bool Paused => _pause.Visible;
    /// <summary>Whether the party is in the safe room; setting it recomposes the objective line, which then ends with "Safe room".</summary>
    public bool SafeRoom
    {
        get => _safeRoom;
        set { _safeRoom = value; AdventureDirector.Instance?.RefreshObjective(); }
    }
    private bool _safeRoom;
    private DialogueScreen _dialogue = null!;
    public DialogueScreen Dialogue => _dialogue;
    private QuestLogScreen _questLog = null!;
    private StairsPrompt _stairsPrompt = null!;
    private Label _objective = null!;
    private Label _subtitle = null!;
    private Label _mangled = null!;
    private Label _readyCheck = null!;
    private float _mangledSince;
    private float _subtitleTimer;
    private bool _subtitleIsBark; // a bark may replace a bark, never a narration still on screen

    public void Bind(PartyController party) => _party = party;

    private UI.Cards.DeathCard _deathCard = null!;
    /// <summary>The death card (run-structure): the server decides it, a client only shows it; the button runs <paramref name="onClose"/>.</summary>
    public void ShowDeathCard(Adventure.Run.DeathCardModel m, Action? onClose) => _deathCard.Open(m, onClose);
    public UI.Cards.DeathCard DeathCard => _deathCard;
    private UI.Cards.RunRecap _recap = null!;
    private UI.Cards.FloorCard _floorCard = null!;
    /// <summary>The run recap (run-structure): victory, or the Death Clause's end; the button runs <paramref name="onClose"/>.</summary>
    public void ShowRunRecap(Adventure.Run.RunRecapModel m, string button, Action? onClose) => _recap.Open(m, button, onClose);
    public UI.Cards.RunRecap Recap => _recap;
    /// <summary>The floor card (run-structure): four seconds at the top, Esc dismisses, never blocks input.</summary>
    public void ShowFloorCard(Adventure.Run.FloorCardModel m) { _floorCard.Show(m); LastFloorCard = m; }
    public bool FloorCardVisible => _floorCard.Visible;
    public Adventure.Run.FloorCardModel? LastFloorCard { get; private set; }
    /// <summary>A death card or run recap is up: the autosave waits and the input is the card's.</summary>
    public bool CardUp => _deathCard.Visible || _recap.Visible;
    /// <summary>Press the visible card's button (tests): the death card first, then the recap.</summary>
    public void CloseCard() { if (_deathCard.Visible) _deathCard.Press(); else if (_recap.Visible) _recap.Press(); }
    private UI.Cards.ClauseCard _clauseCard = null!;
    private bool _clausesPending;
    /// <summary>The factor's "Read them." was chosen (run-structure): the clause card opens when the briefing closes.</summary>
    public void NoteClausesAsked() => _clausesPending = true;
    public UI.Cards.ClauseCard ClauseCard => _clauseCard;
    public bool ClauseCardVisible => _clauseCard.Visible;
    private UI.Cards.DeedToast _deedToast = null!;
    /// <summary>A deed earned (run-structure): name and unlock, bottom-right, four seconds, never blocking input.</summary>
    public void ShowDeedToast(string name, string unlockText) => _deedToast.Show(name, unlockText);
    public bool DeedToastVisible => _deedToast.Visible;

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
        _chat.TextSubmitted += SubmitChat;
        AddChild(_chat);
        _pause = new PauseMenu { Name = "PauseMenu", Visible = false };
        AddChild(_pause);
        AddChild(new PingMarkers { Name = "PingMarkers" });

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
        _readyCheck = new Label { Name = "ReadyCheck", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.95f, 0.85f, 0.55f), Visible = false };
        _readyCheck.AddThemeFontSizeOverride("font_size", 20);
        _readyCheck.AnchorLeft = 0.5f; _readyCheck.AnchorRight = 0.5f; _readyCheck.AnchorTop = 0; _readyCheck.AnchorBottom = 0;
        _readyCheck.OffsetLeft = -360; _readyCheck.OffsetRight = 360; _readyCheck.OffsetTop = 116; _readyCheck.OffsetBottom = 146;
        AddChild(_readyCheck);

        // the combat read-outs: 3D nodes beside the Hud (not under a Control), the marker on the crosshair
        DamageNumbers = new DamageNumbers { Name = "DamageNumbers" };
        EnemyBars = new EnemyBars { Name = "EnemyBars" };
        Main.Instance.AddChild(DamageNumbers);
        Main.Instance.AddChild(EnemyBars);
        HitMarker = new HitMarker { Name = "HitMarker" };
        AddChild(HitMarker);
        Vignette = new Vignette { Name = "Vignette" };
        AddChild(Vignette);
        MoveChild(Vignette, 0); // under everything else
        BossBar = new BossBar { Name = "BossBar" };
        BossBar.Bind(_party);
        AddChild(BossBar);
        LevelUpBanner = new LevelUpBanner { Name = "LevelUpBanner" };
        AddChild(LevelUpBanner);
        LootToast = new LootToast { Name = "LootToast" };
        AddChild(LootToast);
        LootToast.AnchorLeft = 0; LootToast.AnchorRight = 0; LootToast.AnchorTop = 1; LootToast.AnchorBottom = 1;
        LootToast.OffsetLeft = 16; LootToast.OffsetRight = 420; LootToast.OffsetTop = -190; LootToast.OffsetBottom = -164; // beside (above) the portraits
        _deedToast = new UI.Cards.DeedToast { Name = "DeedToast" };
        AddChild(_deedToast);
        _deedToast.AnchorLeft = 1; _deedToast.AnchorRight = 1; _deedToast.AnchorTop = 1; _deedToast.AnchorBottom = 1;
        _deedToast.OffsetLeft = -380; _deedToast.OffsetRight = -16; _deedToast.OffsetTop = -150; _deedToast.OffsetBottom = -60; // bottom-right

        _dialogue = new DialogueScreen { Name = "Dialogue", Visible = false };
        _dialogue.Closed += UpdateModalState;
        AddChild(_dialogue);
        _stairsPrompt = new StairsPrompt { Name = "StairsPrompt" };
        _stairsPrompt.Closed += UpdateModalState;
        AddChild(_stairsPrompt);
        _shop = new ShopScreen { Name = "Shop", Visible = false };
        _shop.Bind(_party);
        _shop.Opening += () =>
        {
            _dialogue.Close();
            _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _questLog.Visible = false;
            _prompt.Text = "";
        };
        _shop.VisibilityChanged += UpdateModalState;
        AddChild(_shop);
        _shrine = new UI.Fortune.ShrineCard { Name = "Shrine", Visible = false };
        _shrine.VisibilityChanged += UpdateModalState;
        AddChild(_shrine);
        _clauseCard = new UI.Cards.ClauseCard { Name = "Clauses", Visible = false };
        _clauseCard.VisibilityChanged += UpdateModalState;
        AddChild(_clauseCard);
        _deathCard = new UI.Cards.DeathCard { Name = "DeathCard", Visible = false };
        _deathCard.VisibilityChanged += UpdateModalState;
        AddChild(_deathCard);
        _recap = new UI.Cards.RunRecap { Name = "Recap", Visible = false };
        _recap.VisibilityChanged += UpdateModalState;
        AddChild(_recap);
        _floorCard = new UI.Cards.FloorCard { Name = "FloorCard", Visible = false };
        AddChild(_floorCard);
        _dialogue.Closed += () => { if (_clausesPending) { _clausesPending = false; _clauseCard.Open(readOnly: Net.NetSession.IsClient || Game.Instance.Adventure.Run == Save.RunKind.Daily); } }; // the factor's offer: the card opens once his talk ends
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
        bus.EnemyHurt += OnEnemyHurt;
        bus.MemberDamaged += OnMemberDamaged;
        bus.LevelUp += OnLevelUp;
        bus.ItemPickedUp += OnLootToast;
        bus.EliteSeen += BossBar.ShowSlim;
        OnFloorChanged(Game.Instance.CurrentFloor, 0);
    }

    private void OnMemberDamaged(int index, float amount)
    {
        if (index < 0 || index >= _party.Members.Length) return;
        if (index < _panels.Count) _panels[index].Flash();
        if (index != _party.LocalIndex) return;
        var maxHp = Math.Max(1f, _party.Members[index].Stats[Characters.Stat.MaxHp]);
        Vignette.Hit(amount / maxHp);
        if (amount >= maxHp * Combat.FeedbackRules.HeavyHitFraction) Dungeon.Props.Tremor.ShakeOnly(Combat.FeedbackRules.ShakeAmplitude, Combat.FeedbackRules.ShakeSeconds);
    }

    private void OnLevelUp(int index)
    {
        if (index < 0 || index >= _party.Members.Length) return;
        var m = _party.Members[index];
        LevelUpBanner.Show($"{m.Name} reaches level {m.Level}");
        if (index < _panels.Count) _panels[index].Pulse(new Color(1f, 0.85f, 0.3f), Combat.FeedbackRules.LevelUpPulseSeconds);
    }

    private void OnLootToast(ItemInstance item)
    {
        if (item.Rarity < Rarity.Rare) return;
        LootToast.Show(item.DisplayName, new Color(RarityInfo.ColorHex(item.Rarity)));
    }

    private void OnEnemyHurt(Enemies.Enemy e, float amount, Combat.HitFlavor flavor, int byMember)
    {
        if (!IsInstanceValid(e)) return;
        DamageNumbers.Show(e, amount, flavor);
        EnemyBars.NoteHurt(e);
        if (byMember == _party.LocalIndex) HitMarker.Flick(e.Hp <= 0f);
    }

    public override void _ExitTree()
    {
        var bus = EventBus.Instance;
        bus.EnemyHurt -= OnEnemyHurt;
        bus.MemberDamaged -= OnMemberDamaged;
        bus.LevelUp -= OnLevelUp;
        bus.ItemPickedUp -= OnLootToast;
        bus.EliteSeen -= BossBar.ShowSlim;
        if (IsInstanceValid(DamageNumbers)) DamageNumbers.QueueFree();
        if (IsInstanceValid(EnemyBars)) EnemyBars.QueueFree();
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
    private void OnNarration(string text, string? voiceId) => ShowSubtitle(text);

    /// <summary>A line under the view for as long as it takes to read; local to this screen (narration arrives here too).</summary>
    public void ShowSubtitle(string text)
    {
        _subtitle.Text = text;
        _subtitleTimer = Math.Clamp(2f + text.Length * 0.06f, 3f, 10f);
        _subtitleIsBark = false;
    }

    /// <summary>A companion's bark in the subtitle strip with the speaker's name: narration wins (a bark never overwrites one still showing), a later bark replaces an earlier bark.</summary>
    public void ShowBark(string speaker, string text, float seconds)
    {
        if (_subtitleTimer > 0 && !_subtitleIsBark) return;
        _subtitle.Text = speaker.Length == 0 ? text : $"{speaker}: {text}";
        _subtitleTimer = seconds;
        _subtitleIsBark = true;
    }

    /// <summary>True while the dialogue is open on this NPC.</summary>
    public bool InDialogueWith(Npc npc) => _dialogue.Visible && _dialogue.Npc == npc;

    /// <summary>The merchant's three choices: trade, how it goes, leave.</summary>
    public void OpenMerchant(Npc npc)
    {
        if (_pause.Visible) return;
        _shop.Close();
        _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _questLog.Visible = false;
        _dialogue.OpenMerchant(npc);
        UpdateModalState();
    }

    /// <summary>The tavern merchant's briefing (on his greeting and whenever he is spoken to on floor 0): local to this screen.</summary>
    public void OpenBriefing(Npc npc)
    {
        if (_pause.Visible) return;
        _shop.Close();
        _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _questLog.Visible = false;
        _dialogue.OpenBriefing(npc, npc.Script); // DungeonBuilder.PlaceTavernMerchant set the briefing: the one source
        UpdateModalState();
    }

    public void OpenDialogue(Npc npc)
    {
        if (_pause.Visible) return;
        _shop.Close();
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
        if (CardUp) return; // the death card or the recap owns the input until its button is pressed
        if (_floorCard.Visible && @event.IsActionPressed("pause")) { _floorCard.Dismiss(); GetViewport().SetInputAsHandled(); return; } // Esc dismisses the floor card first
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
        if (@event.IsActionPressed("chat") && !_sheet.Visible && !_inventory.Visible && !_skills.Visible && !_dialogue.Visible && !_questLog.Visible && !_shop.Visible && !_stairsPrompt.Visible && !_shrine.Visible && !_clauseCard.Visible) // a stairs question keeps the keys: Esc answers it, never the chat
        {
            OpenChat();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_dialogue.Visible || _stairsPrompt.Visible || _shop.Visible || _shrine.Visible || _clauseCard.Visible) return; // the dialogue / stair question / shop / shrine card / clause card owns input while open
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
            if (Adventure.Run.ClauseRules.Active.Hardcore) { EventBus.Instance.Log("The Death Clause allows no second chances."); GetViewport().SetInputAsHandled(); return; }
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
        else if (@event.IsActionPressed("ping"))
        {
            Ping();
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
            _party.SetFocus(_party.NextInPlay(_party.FocusedIndex));
            if (_sheet.Visible) _sheet.Refresh();
            if (_skills.Visible) _skills.Refresh();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (GetTree().Paused || !IsInstanceValid(_party) || !_party.HasBodies) return;
        var target = _party.LookingAt;
        _prompt.Text = target == null || _sheet.Visible || _inventory.Visible || _skills.Visible || _dialogue.Visible || _questLog.Visible || _shop.Visible ? "" : $"[E] {target.Prompt}";
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
        if (_shop.Visible) _shop.Refresh();
        UpdateFloorLabel(Game.Instance.CurrentFloor);
    }

    /// <summary>The floor name only. A refresh (every co-op bag, shop or dip sync calls one) must not run the floor-change cleanup: that closed an open shrine card as "gone" and lost the boon.</summary>
    public void RefreshFloorLabel() => UpdateFloorLabel(Game.Instance.CurrentFloor);

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
        if (_pause.Visible || _dialogue.Visible || CardUp) return;
        _shop.Close();
        _sheet.Visible = false; _inventory.Visible = false; _skills.Visible = false; _questLog.Visible = false;
        _prompt.Text = "";
        _stairsPrompt.Open(stairs, actor);
        UpdateModalState();
    }

    /// <summary>The player stepped off the landing: the question goes away.</summary>
    public void DismissStairs(Dungeon.Stairs stairs) => _stairsPrompt.Dismiss(stairs);

    /// <summary>The server's stairs ready-check (co-op): "Descend? 1/3 ready · 12 s" under the objective line.</summary>
    public void ShowReadyCheck(int count, int total, int secondsLeft, int direction)
    {
        _readyCheck.Text = Net.ReadyCheckRules.HudLine(count, total, secondsLeft, direction);
        _readyCheck.Visible = _readyCheck.Text.Length > 0;
    }

    public void HideReadyCheck() { _readyCheck.Visible = false; _readyCheck.Text = ""; }

    /// <summary>What the ready-check line shows ("" when hidden).</summary>
    public string ReadyCheckText => _readyCheck.Visible ? _readyCheck.Text : "";

    public bool StairsPromptOpen => _stairsPrompt.Visible;

    private double _lastPingAt = -1;
    /// <summary>
    /// Ping (X): a marker with this player's name where the camera looks, 2 s, 1 s cooldown; on a client the server relays
    /// it to everyone as a <see cref="Net.PingEvt"/>. False when nothing was pinged (down, cooling down, or looking at nothing).
    /// </summary>
    public bool Ping()
    {
        if (_party.Local is not { Downed: false } me) return false;
        var now = Time.GetTicksMsec() / 1000.0;
        if (!Net.PingRules.CanPing(_lastPingAt, now)) return false;
        if (me.AimPoint(Net.PingRules.MaxAimDistance) is not { } at) return false;
        _lastPingAt = now;
        if (Net.NetSession.IsClient) Net.NetSession.Instance.Send(new Net.PingIntent(new Net.NetVec(at.X, at.Y, at.Z)));
        else PingMarkers.Instance?.Place(me.State.Name, at);
        return true;
    }

    public void UpdateModalState()
    {
        var modal = _sheet.Visible || _inventory.Visible || _skills.Visible || _pause.Visible || _dialogue.Visible || _questLog.Visible || _chat.Visible || _stairsPrompt.Visible || _shop.Visible || _shop.IsOpening || _shrine.Visible || _clauseCard.Visible || CardUp;
        _party.InputLocked = modal;
        _party.SetMouseCaptured(!modal);
    }

    private LineEdit _chat = null!;
    private string _floorText = "";
    private int _pingFrames;

    /// <summary>T: the chat line, in solo and on the host too (co-op): what is typed goes to the log, on a client through the server.</summary>
    public void OpenChat()
    {
        _chat.Text = "";
        _chat.Visible = true;
        _chat.GrabFocus();
        UpdateModalState();
    }

    /// <summary>Send the line: a client's goes up as a ChatIntent; anyone else writes "[Name] text" to the log (a server's log line reaches every client).</summary>
    public void SubmitChat(string text)
    {
        var t = text.Trim();
        if (t.Length > 0)
        {
            if (Net.NetSession.IsClient) Net.NetSession.Instance.Send(new Net.ChatIntent(t));
            else EventBus.Instance.Log($"[{_party.Local?.State.Name ?? "Host"}] {t}");
        }
        CloseChat();
    }

    /// <summary>Whether the chat line is open (tests).</summary>
    public bool ChatOpen => _chat.Visible;

    /// <summary>The newest line of the message log ("" when empty).</summary>
    public string LastLogLine => _lines.Last?.Value ?? "";

    private void CloseChat()
    {
        _chat.Visible = false;
        _chat.ReleaseFocus();
        UpdateModalState();
    }

    private void OnFloorChanged(int floor, int delta)
    {
        if (_shop.Visible && floor != _shop.Floor) _shop.Close(); // the merchant's wares are the floor's
        if (_shrine.Visible) _shrine.Close("gone"); // the altar stayed on the floor we left
        HideReadyCheck(); // the vote is over once the floor changes
        UpdateFloorLabel(floor);
    }

    private void UpdateFloorLabel(int floor)
    {
        var tier = Main.Instance?.Builder.CurrentTier?.DisplayName ?? "";
        var theme = Main.Instance?.Builder.CurrentTheme?.DisplayName;
        var place = theme != null && theme != tier ? $"{tier}, {theme}" : tier;
        var suffix = DungeonCrawler.Dungeon.TierRules.IsEndless(floor) ? "  (endless)" : DungeonCrawler.Dungeon.TierRules.IsBossFloor(floor) ? "  (guardian)" : "";
        _floorText = floor == 0 ? "The Tavern" : tier == "" ? $"Floor {floor}" : $"Floor {floor}  —  {place}{suffix}";
        _floorLabel.Text = _floorText;
    }

    /// <summary>On a server: the round trip and who is online, beside the floor name.</summary>
    private void TickNetLabel()
    {
        if (!Net.NetSession.IsClient || Net.NetSession.Instance.Client is not { } client) return;
        if (++_pingFrames % 30 != 0) return;
        // players only: hero bots are bodies in the world too, but nobody is at their keyboard
        var online = client.Roster.Count == 0 ? _party.Adventurers.Count(a => !a.Parked) : client.Roster.Values.Count(p => p.Online && p.BotOwner == null);
        var bots = client.Roster.Values.Count(p => p.Online && p.BotOwner != null);
        _floorLabel.Text = $"{_floorText}   ·   {client.Rtt * 1000:0} ms, {online} online{(bots > 0 ? $", {bots} {(bots == 1 ? "bot" : "bots")}" : "")}";
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
    private void OnItemPickedUp(ItemInstance item) { if (_inventory.Visible) _inventory.Refresh(); if (_shop.Visible) _shop.Refresh(); }

    private void OnMessage(string text)
    {
        _lines.AddLast(text);
        while (_lines.Count > MaxLogLines) _lines.RemoveFirst();
        _log.Text = string.Join('\n', _lines);
    }
}
