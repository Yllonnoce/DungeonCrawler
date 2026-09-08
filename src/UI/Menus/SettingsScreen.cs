using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.UI.Menus;

/// <summary>Camera, audio, display and key settings. Changes apply immediately and save on Back.</summary>
public partial class SettingsScreen : PanelContainer
{
    public event Action? Closed;

    private string? _rebinding;
    private Button _bakeCampaign = null!, _bakeVoice = null!;
    private Label _bakeStatus = null!;

    private void RefreshBake()
    {
        var baker = Adventure.CampaignBaker.Instance;
        var inGame = Main.Instance?.InGame == true;
        var busy = baker?.Running == true;
        _bakeCampaign.Disabled = busy || inGame || !Settings.HasPlannerKey;
        _bakeVoice.Disabled = busy || inGame || !Settings.HasVoiceKey || !Adventure.BuiltinCampaign.Exists;
        _bakeCampaign.Text = $"Rebuild campaign with {Settings.PlannerLabel}";
        _bakeVoice.Text = $"Render voice with {Settings.VoiceLabel}";
        _bakeStatus.Text = baker?.Status ?? "";
    }

    public override void _Process(double delta)
    {
        if (Visible && _bakeStatus != null && Adventure.CampaignBaker.Instance is { } baker && (baker.Running || _bakeStatus.Text != baker.Status)) RefreshBake();
    }
    private readonly Dictionary<string, Button> _keyButtons = new();
    private Label _hint = null!;

    // Runware model menus: the planner list comes from Runware when there is a key (the shipped catalogue until then), the TTS list is ours
    private OptionButton _plannerModels = null!;
    private Label _plannerModelStatus = null!;
    private List<RunwareTextModel> _catalogue = RunwareCatalog.TextModels.ToList();
    private bool _catalogueFetched;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 16);
        AddChild(margin);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(760, 620), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(v);

        Header(v, "Camera");
        Slider(v, "Mouse sensitivity", 0.2f, 3f, Settings.MouseSensitivity, x => { Settings.MouseSensitivity = x; Settings.Apply(); }, "0.00");
        Slider(v, "Field of view", 60f, 110f, Settings.Fov, x => { Settings.Fov = x; Settings.Apply(); }, "0");
        Check(v, "Invert mouse Y", Settings.InvertY, x => { Settings.InvertY = x; Settings.Apply(); });

        Header(v, "Audio");
        Slider(v, "Master volume", 0f, 1f, Settings.MasterVolume, x => { Settings.MasterVolume = x; Settings.Apply(); }, "P0");
        Slider(v, "Music and ambience", 0f, 1f, Settings.MusicVolume, x => { Settings.MusicVolume = x; Settings.Apply(); }, "P0");
        Slider(v, "Effects", 0f, 1f, Settings.SfxVolume, x => { Settings.SfxVolume = x; Settings.Apply(); EventBus.Instance.PlaySfx("ui_click"); }, "P0");

        Header(v, "Display");
        var res = new OptionButton { FocusMode = FocusModeEnum.None };
        for (var i = 0; i < Settings.Resolutions.Length; i++)
        {
            var r = Settings.Resolutions[i];
            res.AddItem($"{r.X} x {r.Y}", i);
            if (r == Settings.Resolution) res.Selected = i;
        }
        res.ItemSelected += i => { Settings.Resolution = Settings.Resolutions[(int)i]; Settings.Apply(); };
        Row(v, "Resolution", res);
        Check(v, "Fullscreen", Settings.Fullscreen, x => { Settings.Fullscreen = x; Settings.Apply(); });
        Check(v, "VSync", Settings.VSync, x => { Settings.VSync = x; Settings.Apply(); });
        Slider(v, "Haze in the air", 0f, 1f, Settings.Haze, x => { Settings.Haze = x; Settings.Apply(); }, "P0");

        Header(v, "Danger");
        var dangerNote = new Label { Text = "Ambushers hide in rubble and wall niches along the corridors and burst out as you pass (three on floor 1, ten by floor 20). After a minute and a half on a floor the boss starts sending reinforcements through portals behind you, faster the longer you stay; the stairs end it, and a dead tier boss silences its tier. Both apply from the next floor you enter.", AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.7f, 0.7f, 0.7f) };
        dangerNote.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(dangerNote);
        Check(v, "Ambushers along the corridors", Settings.Ambushes, x => Settings.Ambushes = x);
        Slider(v, "Reinforcement pressure (0 off, 1 normal, 2 heavy)", 0f, 2f, Settings.Reinforcements, x => { Settings.Reinforcements = x; Adventure.PortalSpawner.Instance?.RefreshSilence(); }, "0.00", step: 0.25f);

        Header(v, "AI (optional)");
        var note = new Label { Text = "Keys stay in user://keys.cfg on this machine and are only ever sent to api.anthropic.com, generativelanguage.googleapis.com and api.runware.ai.", AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.7f, 0.7f, 0.7f) };
        note.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(note);
        Text(v, "Claude API key", Settings.ClaudeKey, x => { Settings.ClaudeKey = x; RefreshBake(); }, secret: true);
        Text(v, "Gemini API key", Settings.GeminiKey, x => { Settings.GeminiKey = x; RefreshBake(); }, secret: true);
        Text(v, "Runware API key (one key for any model Runware routes: planner and voice)", Settings.RunwareKey, x => { Settings.RunwareKey = x; RefreshBake(); }, secret: true);
        var runwareTest = new Button { Text = "Test Runware key", FocusMode = FocusModeEnum.None };
        var runwareResult = new Label { Modulate = new Color(0.8f, 0.8f, 0.7f) };
        runwareTest.Pressed += () =>
        {
            if (!Settings.HasRunwareKey) { runwareResult.Text = "Paste a Runware API key first."; return; }
            runwareResult.Text = "Asking Runware...";
            var key = Settings.RunwareKey.Trim();
            _ = Task.Run(async () =>
            {
                var (balance, error) = await Adventure.AiClients.RunwareTextClient.CheckKeyAsync(key, CancellationToken.None);
                Callable.From(() => runwareResult.Text = error != null ? $"Failed: {error}" : $"Connected; balance ${balance:0.00}.").CallDeferred();
            });
        };
        Row(v, "", runwareTest);
        v.AddChild(runwareResult);
        var plannerProvider = new OptionButton { FocusMode = FocusModeEnum.None };
        plannerProvider.AddItem("Claude (Anthropic, direct)", 0);
        plannerProvider.AddItem("Runware (any routed model)", 1);
        plannerProvider.Selected = Settings.PlannerViaRunware ? 1 : 0;
        plannerProvider.ItemSelected += i => { Settings.PlannerProvider = i == 1 ? Settings.ProviderRunware : Settings.ProviderClaude; RefreshBake(); };
        Row(v, "Campaign planner runs on", plannerProvider);
        _plannerModels = new OptionButton { FocusMode = FocusModeEnum.None };
        _plannerModels.ItemSelected += i => { if (_plannerModels.GetItemMetadata((int)i).AsString() is { Length: > 0 } air) Settings.RunwareTextModel = air; RefreshBake(); };
        Row(v, "Runware planner model (dearest first)", _plannerModels);
        FillPlannerModels();
        var listText = new Button { Text = "Refresh the list from Runware", FocusMode = FocusModeEnum.None };
        _plannerModelStatus = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.8f, 0.8f, 0.7f) };
        _plannerModelStatus.AddThemeFontSizeOverride("font_size", 12);
        listText.Pressed += () => RefreshPlannerModels(force: true);
        Row(v, "", listText);
        v.AddChild(_plannerModelStatus);
        VisibilityChanged += () => { if (Visible) RefreshPlannerModels(force: false); };
        var voiceProvider = new OptionButton { FocusMode = FocusModeEnum.None };
        voiceProvider.AddItem("Gemini (Google, direct)", 0);
        voiceProvider.AddItem("Runware (xAI, Inworld, MiniMax, Gemini TTS...)", 1);
        voiceProvider.Selected = Settings.VoiceViaRunware ? 1 : 0;
        voiceProvider.ItemSelected += i => { Settings.VoiceProvider = i == 1 ? Settings.ProviderRunware : Settings.ProviderGemini; Settings.Apply(); Adventure.VoiceRenderer.Instance?.Configure(); RefreshBake(); };
        Row(v, "Voice runs on", voiceProvider);
        TtsDropdown(v, "Runware TTS model for the narrator, bosses and the cast (dearest first)", Settings.RunwareVoiceModel, x => { Settings.RunwareVoiceModel = x; Settings.Apply(); });
        TtsDropdown(v, "Runware TTS model for one-off NPCs and companion barks (dearest first)", Settings.RunwareVoiceModelMinor, x => { Settings.RunwareVoiceModelMinor = x; Settings.Apply(); });
        Text(v, "Runware fallback TTS models (comma-separated)", Settings.RunwareVoiceFallbackModels, x => Settings.RunwareVoiceFallbackModels = x, secret: false);
        var runwareModels = new Label { Text = "Prices are Runware's listed rates per thousand characters; the game casts its voices on every model in the list.", AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.7f, 0.7f, 0.7f) };
        runwareModels.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(runwareModels);
        Check(v, "In an AI campaign, keep the planner writing the floors ahead (off: the offline designer takes over; New Game itself never plans)", Settings.AiPlanning, x => { Settings.AiPlanning = x; Main.Instance?.Menu?.RefreshSubtitle(); });
        Check(v, "Voice narration (needs the selected provider's key)", Settings.AiVoice, x => { Settings.AiVoice = x; Adventure.VoiceRenderer.Instance?.Configure(); });
        Slider(v, "Voice volume", 0f, 1f, Settings.VoiceVolume, x => { Settings.VoiceVolume = x; Settings.Apply(); }, "P0");
        Text(v, "Gemini TTS model for the narrator, bosses and the cast", Settings.VoiceModel, x => { Settings.VoiceModel = x; Settings.Apply(); }, secret: false);
        Text(v, "TTS model for one-off NPCs and companion barks", Settings.VoiceModelMinor, x => { Settings.VoiceModelMinor = x; Settings.Apply(); }, secret: false);
        Slider(v, "Voice requests per minute (Google's per-minute limit, not the daily quota)", 1f, 30f, Settings.VoiceRequestsPerMinute, x => Settings.VoiceRequestsPerMinute = x, "0", step: 1f);
        Text(v, "Fallback TTS models (comma-separated, used when the quota runs out)", Settings.VoiceFallbackModels, x => Settings.VoiceFallbackModels = x, secret: false);
        var listModels = new Button { Text = "List the TTS models this key can use", FocusMode = FocusModeEnum.None };
        var listResult = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.8f, 0.8f, 0.7f) };
        listModels.Pressed += () =>
        {
            if (!Settings.HasGeminiKey) { listResult.Text = "Paste a Gemini API key first."; return; }
            listResult.Text = "Asking Google...";
            var key = Settings.GeminiKey.Trim();
            _ = Task.Run(async () =>
            {
                var (models, error) = await Adventure.AiClients.GeminiTtsClient.ListTtsModelsAsync(key, CancellationToken.None);
                Callable.From(() => listResult.Text = error != null ? $"Failed: {error}" : models.Count == 0 ? "No text-to-speech models are available to this key." : "Available: " + string.Join(", ", models)).CallDeferred();
            });
        };
        Row(v, "", listModels);
        v.AddChild(listResult);
        Slider(v, "Spend cap per run ($)", 0f, 20f, (float)Settings.SpendCapUsd, x => Settings.SpendCapUsd = Math.Round(x, 2), "0.00", step: 0.25f);
        Slider(v, "Wait for a floor plan (s)", 0f, 90f, Settings.PlanWaitSeconds, x => Settings.PlanWaitSeconds = x, "0", step: 1f);
        var test = new Button { Text = "Test planner connection", FocusMode = FocusModeEnum.None };
        var testResult = new Label { Modulate = new Color(0.8f, 0.8f, 0.7f) };
        test.Pressed += () =>
        {
            testResult.Text = $"Testing {(Settings.PlannerViaRunware ? Adventure.AdventureDirector.RunwareModel + " via Runware" : "Claude")}...";
            Adventure.AdventureDirector.Instance?.TestConnection(Settings.PlannerViaRunware ? Settings.RunwareKey : Settings.ClaudeKey, Settings.PlannerViaRunware, r => testResult.Text = r);
        };
        Row(v, "", test);
        v.AddChild(testResult);

        Header(v, "Built-in campaign");
        var bakeNote = new Label { Text = "Rebuild the shipped campaign with your keys, on the providers selected above. Planning twenty floors takes several minutes and a few dollars with Claude (less on cheaper Runware models); voice renders the budgeted lines. Results are kept in user://adventures/builtin and user://voice on this machine. Title screen only.", AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.7f, 0.7f, 0.7f) };
        bakeNote.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(bakeNote);
        _bakeCampaign = new Button { Text = $"Rebuild campaign with {Settings.PlannerLabel}", FocusMode = FocusModeEnum.None };
        _bakeCampaign.Pressed += () => { Settings.Save(); Adventure.CampaignBaker.Instance?.StartCampaign(withVoice: Settings.AiVoice && Settings.HasVoiceKey); RefreshBake(); };
        _bakeVoice = new Button { Text = $"Render voice with {Settings.VoiceLabel}", FocusMode = FocusModeEnum.None };
        _bakeVoice.Pressed += () => { Settings.Save(); Adventure.CampaignBaker.Instance?.StartVoice(); RefreshBake(); };
        var bakeRow = new HBoxContainer();
        bakeRow.AddThemeConstantOverride("separation", 10);
        bakeRow.AddChild(_bakeCampaign);
        bakeRow.AddChild(_bakeVoice);
        v.AddChild(bakeRow);
        _bakeStatus = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.8f, 0.8f, 0.7f) };
        v.AddChild(_bakeStatus);
        RefreshBake();

        Header(v, "Keys");
        _hint = new Label { Text = "Click a key, then press the new key. Escape cancels.", Modulate = new Color(0.7f, 0.7f, 0.7f) };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(_hint);
        var keys = new GridContainer { Columns = 2 }; // label, key: fixed widths so the key sits right beside its action
        keys.AddThemeConstantOverride("h_separation", 12);
        keys.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(keys);
        foreach (var (action, label) in Settings.Rebindable)
        {
            keys.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(240, 0) });
            var b = new Button { Text = KeyName(Settings.KeyFor(action)), CustomMinimumSize = new Vector2(180, 30), FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            var a = action;
            b.Pressed += () => { _rebinding = a; b.Text = "press a key..."; };
            keys.AddChild(b);
            _keyButtons[action] = b;
        }
        var reset = new Button { Text = "Reset keys to defaults", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        reset.Pressed += () => { Settings.ResetKeys(); RefreshKeys(); };
        v.AddChild(reset);

        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 36), FocusMode = FocusModeEnum.None };
        back.Pressed += () => { Settings.Save(); EventBus.Instance.PlaySfx("ui_close"); Closed?.Invoke(); };
        VisibilityChanged += () => { if (!Visible) Settings.Save(); }; // closed by Escape or by a menu switching away: still saved
        v.AddChild(back);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_rebinding == null || @event is not InputEventKey { Pressed: true } key) return;
        var action = _rebinding;
        _rebinding = null;
        if (key.Keycode != Key.Escape)
        {
            var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            Settings.Rebind(action, code);
        }
        RefreshKeys(); // the rebound action and any action that just lost this key
        GetViewport().SetInputAsHandled();
    }

    private void RefreshKeys()
    {
        foreach (var (a, b) in _keyButtons) b.Text = KeyName(Settings.KeyFor(a));
    }

    /// <summary>The planner menu from the current catalogue, keeping the saved model selected (a model not in the list is added as typed).</summary>
    private void FillPlannerModels()
    {
        _plannerModels.Clear();
        var current = RunwareWire.ToAir(Settings.RunwareTextModel);
        var selected = -1;
        foreach (var m in _catalogue)
        {
            var i = _plannerModels.ItemCount;
            _plannerModels.AddItem(m.Label, i);
            _plannerModels.SetItemMetadata(i, m.Air);
            if (m.Air.Equals(current, StringComparison.OrdinalIgnoreCase)) selected = i;
        }
        if (selected < 0 && current.Length > 0)
        {
            selected = _plannerModels.ItemCount;
            _plannerModels.AddItem($"{current} (as typed)", selected);
            _plannerModels.SetItemMetadata(selected, current);
        }
        _plannerModels.Selected = selected;
    }

    /// <summary>Fetch Runware's priced list with the key (once per visit, or on demand) and rebuild the menu from it.</summary>
    private void RefreshPlannerModels(bool force)
    {
        if (!Settings.HasRunwareKey) { _plannerModelStatus.Text = $"Showing the shipped list of {_catalogue.Count} models; paste a Runware key to fetch the current one."; return; }
        if (_catalogueFetched && !force) return;
        _catalogueFetched = true;
        _plannerModelStatus.Text = "Asking Runware for its models and prices...";
        var key = Settings.RunwareKey.Trim();
        _ = Task.Run(async () =>
        {
            var (models, error) = await Adventure.AiClients.RunwareTextClient.ListTextCatalogueAsync(key, CancellationToken.None);
            Callable.From(() =>
            {
                GD.Print($"[settings] Runware catalogue: {(error != null ? "failed: " + error : models.Count + " models, dearest " + models.FirstOrDefault()?.Slug + ", cheapest " + models.LastOrDefault()?.Slug)}");
                if (error != null || models.Count == 0) { _plannerModelStatus.Text = $"Could not fetch the list ({error ?? "empty"}); showing the shipped one."; return; }
                _catalogue = models;
                RunwareCatalog.Live = models;
                FillPlannerModels();
                _plannerModelStatus.Text = $"{models.Count} models this key can use, dearest first (prices per million tokens).";
            }).CallDeferred();
        });
    }

    /// <summary>A menu of the TTS models the game can voice, dearest first, keeping the saved model selected.</summary>
    private static void TtsDropdown(VBoxContainer v, string label, string value, Action<string> onChange)
    {
        var o = new OptionButton { FocusMode = FocusModeEnum.None };
        var selected = -1;
        foreach (var m in Adventure.Voice.RunwareVoices.KnownModels)
        {
            var i = o.ItemCount;
            o.AddItem(m.Label, i);
            o.SetItemMetadata(i, m.Model);
            if (m.Model.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase)) selected = i;
        }
        if (selected < 0 && value.Trim().Length > 0)
        {
            selected = o.ItemCount;
            o.AddItem($"{value.Trim()} (as typed)", selected);
            o.SetItemMetadata(selected, value.Trim());
        }
        o.Selected = selected;
        o.ItemSelected += i => { if (o.GetItemMetadata((int)i).AsString() is { Length: > 0 } id) onChange(id); };
        Row(v, label, o);
    }

    private static bool Headless => DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase);
    private static string KeyName(Key k) => k == Key.None ? "unbound" : Headless ? OS.GetKeycodeString(k) : OS.GetKeycodeString(DisplayServer.KeyboardGetKeycodeFromPhysical(k));

    private static void Header(VBoxContainer v, string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(l);
    }

    private static void Row(VBoxContainer v, string label, Control control)
    {
        var h = new HBoxContainer();
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(240, 0) });
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(control);
        v.AddChild(h);
    }

    private static void Slider(VBoxContainer v, string label, float min, float max, float value, Action<float> onChange, string format, float step = 0f)
    {
        var h = new HBoxContainer();
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(240, 0) });
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step > 0f ? step : (max - min) / 100f, Value = value, CustomMinimumSize = new Vector2(300, 0), FocusMode = FocusModeEnum.None };
        var val = new Label { Text = value.ToString(format), CustomMinimumSize = new Vector2(60, 0) };
        s.ValueChanged += x => { val.Text = ((float)x).ToString(format); onChange((float)x); };
        h.AddChild(s);
        h.AddChild(val);
        v.AddChild(h);
    }

    private static void Text(VBoxContainer v, string label, string value, Action<string> onChange, bool secret)
    {
        var e = new LineEdit { Text = value, Secret = secret, PlaceholderText = "paste here", CustomMinimumSize = new Vector2(420, 0) };
        e.TextChanged += x => onChange(x);
        Row(v, label, e);
    }

    private static void Check(VBoxContainer v, string label, bool value, Action<bool> onChange)
    {
        var c = new CheckBox { Text = label, ButtonPressed = value, FocusMode = FocusModeEnum.None };
        c.Toggled += x => onChange(x);
        v.AddChild(c);
    }
}
