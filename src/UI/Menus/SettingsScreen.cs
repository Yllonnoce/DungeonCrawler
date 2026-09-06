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
        _bakeCampaign.Disabled = busy || inGame || !Settings.HasClaudeKey;
        _bakeVoice.Disabled = busy || inGame || !Settings.HasGeminiKey || !Adventure.BuiltinCampaign.Exists;
        _bakeStatus.Text = baker?.Status ?? "";
    }

    public override void _Process(double delta)
    {
        if (Visible && _bakeStatus != null && Adventure.CampaignBaker.Instance is { } baker && (baker.Running || _bakeStatus.Text != baker.Status)) RefreshBake();
    }
    private readonly Dictionary<string, Button> _keyButtons = new();
    private Label _hint = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var margin = new MarginContainer();
        foreach (var edge in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(edge, 16);
        AddChild(margin);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(760, 620) };
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

        Header(v, "AI (optional)");
        var note = new Label { Text = "Keys stay in user://keys.cfg on this machine and are only ever sent to api.anthropic.com and generativelanguage.googleapis.com.", AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.7f, 0.7f, 0.7f) };
        note.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(note);
        Text(v, "Claude API key", Settings.ClaudeKey, x => { Settings.ClaudeKey = x; RefreshBake(); }, secret: true);
        Text(v, "Gemini API key", Settings.GeminiKey, x => { Settings.GeminiKey = x; RefreshBake(); }, secret: true);
        Check(v, "Let Claude design the campaign and floors (needs a key)", Settings.AiPlanning, x => Settings.AiPlanning = x);
        Check(v, "Voice narration with Gemini (needs a key)", Settings.AiVoice, x => Settings.AiVoice = x);
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
        var test = new Button { Text = "Test Claude connection", FocusMode = FocusModeEnum.None };
        var testResult = new Label { Modulate = new Color(0.8f, 0.8f, 0.7f) };
        test.Pressed += () =>
        {
            testResult.Text = "Testing...";
            Adventure.AdventureDirector.Instance?.TestConnection(Settings.ClaudeKey, r => testResult.Text = r);
        };
        Row(v, "", test);
        v.AddChild(testResult);

        Header(v, "Built-in campaign");
        var bakeNote = new Label { Text = "Rebuild the shipped campaign with your keys. Planning twenty floors with Claude takes several minutes and roughly a dollar; voice renders every line with Gemini. Results are kept in user://adventures/builtin and user://voice on this machine. Title screen only.", AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(700, 0), Modulate = new Color(0.7f, 0.7f, 0.7f) };
        bakeNote.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(bakeNote);
        _bakeCampaign = new Button { Text = "Rebuild campaign with Claude", FocusMode = FocusModeEnum.None };
        _bakeCampaign.Pressed += () => { Settings.Save(); Adventure.CampaignBaker.Instance?.StartCampaign(withVoice: Settings.AiVoice && Settings.HasGeminiKey); RefreshBake(); };
        _bakeVoice = new Button { Text = "Render voice with Gemini", FocusMode = FocusModeEnum.None };
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
        foreach (var (action, label) in Settings.Rebindable)
        {
            var b = new Button { Text = KeyName(Settings.KeyFor(action)), CustomMinimumSize = new Vector2(180, 30), FocusMode = FocusModeEnum.None };
            var a = action;
            b.Pressed += () => { _rebinding = a; b.Text = "press a key..."; };
            Row(v, label, b);
            _keyButtons[action] = b;
        }
        var reset = new Button { Text = "Reset keys to defaults", FocusMode = FocusModeEnum.None };
        reset.Pressed += () => { Settings.ResetKeys(); foreach (var (a2, b2) in _keyButtons) b2.Text = KeyName(Settings.KeyFor(a2)); };
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
        _keyButtons[action].Text = KeyName(Settings.KeyFor(action));
        GetViewport().SetInputAsHandled();
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
