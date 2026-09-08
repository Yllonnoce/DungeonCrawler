using DungeonCrawler.Audio;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>Player preferences, saved to user://settings.cfg and applied on load and on change.</summary>
public static class Settings
{
    private const string Path = "user://settings.cfg";
    /// <summary>API keys live apart from the other settings and never leave this machine.</summary>
    private const string KeysPath = "user://keys.cfg";

    public static float MouseSensitivity { get; set; } = 1.0f;   // multiplier on the party's base sensitivity
    public static float Fov { get; set; } = 80f;
    public static bool InvertY { get; set; }
    public static float MasterVolume { get; set; } = 1f;
    public static float MusicVolume { get; set; } = 0.8f;
    public static float SfxVolume { get; set; } = 1f;
    public static bool Fullscreen { get; set; }
    public static bool VSync { get; set; } = true;
    /// <summary>Volumetric haze in the air, 0 (off) to 1 (thick); torchlight shows in it.</summary>
    public static float Haze { get; set; } = 0.5f;
    public static Vector2I Resolution { get; set; } = new(1920, 1080);

    // danger: ambushers along the corridors and the boss's reinforcement portals
    public static bool Ambushes { get; set; } = true;
    /// <summary>Reinforcement pressure: 0 off, 1 normal, 2 heavy (every wait halved, more through each portal).</summary>
    public static float Reinforcements { get; set; } = 1f;

    // AI planner and voice
    public static bool AiPlanning { get; set; } = true;
    public static bool AiVoice { get; set; } = true;
    public static double SpendCapUsd { get; set; } = 3.0;
    public static float PlanWaitSeconds { get; set; } = 20f;
    public static float VoiceVolume { get; set; } = 1f;
    public static string PlayerName { get; set; } = "";
    public static string LastServerUrl { get; set; } = "ws://127.0.0.1:7777";
    public static string LastClassId { get; set; } = "class.warrior";
    /// <summary>Gemini text-to-speech model, and the ones to fall back to (in order) when its quota is used up.</summary>
    public static string VoiceModel { get; set; } = Adventure.Voice.Voices.DefaultModel;
    public static string VoiceFallbackModels { get; set; } = Adventure.Voice.Voices.DefaultFallbacks;
    /// <summary>The model for one-off NPCs and companion barks, so the main model's quota goes to the narrator, bosses and cast.</summary>
    public static string VoiceModelMinor { get; set; } = Adventure.Voice.Voices.DefaultMinorModel;
    /// <summary>How many TTS requests may start per minute; Google's per-minute limits on the preview models are low.</summary>
    public static float VoiceRequestsPerMinute { get; set; } = 8f;
    public static string ClaudeKey { get; set; } = "";
    public static string GeminiKey { get; set; } = "";
    public static string RunwareKey { get; set; } = "";
    public static bool HasClaudeKey => ClaudeKey.Trim().Length > 10;
    public static bool HasGeminiKey => GeminiKey.Trim().Length > 10;
    public static bool HasRunwareKey => RunwareKey.Trim().Length > 10;

    // Providers: the planner runs on Anthropic directly ("claude") or through Runware ("runware"); voice on Google directly ("gemini") or through Runware.
    public const string ProviderClaude = "claude", ProviderGemini = "gemini", ProviderRunware = "runware";
    public static string PlannerProvider { get; set; } = ProviderClaude;
    public static string VoiceProvider { get; set; } = ProviderGemini;
    public static bool PlannerViaRunware => PlannerProvider == ProviderRunware;
    public static bool VoiceViaRunware => VoiceProvider == ProviderRunware;
    /// <summary>The model Runware runs the planner on (Claude, Gemini, GPT, DeepSeek... in Runware's creator:family@version form).</summary>
    public static string RunwareTextModel { get; set; } = Adventure.Ai.RunwareWire.DefaultTextModel;
    /// <summary>Runware TTS models for the main voices, the minor ones, and the fallbacks, in Runware's form.</summary>
    public static string RunwareVoiceModel { get; set; } = Adventure.Voice.RunwareVoices.DefaultModel;
    public static string RunwareVoiceModelMinor { get; set; } = Adventure.Voice.RunwareVoices.DefaultMinorModel;
    public static string RunwareVoiceFallbackModels { get; set; } = Adventure.Voice.RunwareVoices.DefaultFallbacks;
    /// <summary>Whether the selected planner provider has its key.</summary>
    public static bool HasPlannerKey => PlannerViaRunware ? HasRunwareKey : HasClaudeKey;
    public static bool HasVoiceKey => VoiceViaRunware ? HasRunwareKey : HasGeminiKey;
    /// <summary>Short names for menus: who writes the campaign, who speaks it.</summary>
    public static string PlannerLabel => PlannerViaRunware ? "Runware" : "Claude";
    public static string VoiceLabel => VoiceViaRunware ? "Runware" : "Gemini";
    /// <summary>The fallback TTS list for the selected voice provider.</summary>
    public static string ActiveVoiceFallbackModels => VoiceViaRunware ? RunwareVoiceFallbackModels : VoiceFallbackModels;

    public static readonly Vector2I[] Resolutions = { new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };

    /// <summary>Actions the player may rebind, in menu order.</summary>
    public static readonly (string action, string label)[] Rebindable =
    {
        ("move_forward", "Move forward"), ("move_back", "Move back"), ("move_left", "Move left"), ("move_right", "Move right"),
        ("sprint", "Sprint"), ("jump", "Jump"), ("crouch", "Crouch"), ("interact", "Interact"), ("attack_1", "Member 1 attack"), ("attack_2", "Member 2 attack"),
        ("attack_3", "Member 3 attack"), ("attack_4", "Member 4 attack"), ("ability_next", "Cycle ability"), ("focus_next", "Next member"),
        ("swap_rank", "Swap rank"), ("inventory", "Inventory"), ("character", "Character sheet"), ("skills", "Skill tree"),
        ("map", "Map"), ("quest_log", "Quest log"), ("quicksave", "Quicksave"), ("quickload", "Quickload"), ("chat", "Chat (multiplayer)"),
    };

    /// <summary>Set when any key came from the environment, so Save() leaves keys.cfg alone.</summary>
    private static bool _envKeys;
    private static readonly Dictionary<string, Key> _defaults = new();
    private static readonly Dictionary<string, Key> _binds = new();

    public static Key KeyFor(string action) => _binds.TryGetValue(action, out var k) ? k : Key.None;

    public static void Load()
    {
        foreach (var (action, _) in Rebindable)
        {
            var ev = InputMap.ActionGetEvents(action).OfType<InputEventKey>().FirstOrDefault();
            var key = ev == null ? Key.None : ev.PhysicalKeycode != Key.None ? ev.PhysicalKeycode : ev.Keycode;
            _defaults[action] = key;
            _binds[action] = key;
        }

        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            MouseSensitivity = (float)cfg.GetValue("camera", "sensitivity", MouseSensitivity).AsDouble();
            Fov = (float)cfg.GetValue("camera", "fov", Fov).AsDouble();
            InvertY = cfg.GetValue("camera", "invert_y", InvertY).AsBool();
            MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume).AsDouble();
            MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume).AsDouble();
            SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume).AsDouble();
            Fullscreen = cfg.GetValue("display", "fullscreen", Fullscreen).AsBool();
            VSync = cfg.GetValue("display", "vsync", VSync).AsBool();
            Haze = (float)cfg.GetValue("display", "haze", Haze).AsDouble();
            Resolution = cfg.GetValue("display", "resolution", Resolution).AsVector2I();
            Ambushes = cfg.GetValue("danger", "ambushes", Ambushes).AsBool();
            Reinforcements = (float)cfg.GetValue("danger", "reinforcements", Reinforcements).AsDouble();
            foreach (var (action, _) in Rebindable)
                if (cfg.HasSectionKey("keys", action)) _binds[action] = (Key)cfg.GetValue("keys", action).AsInt64();
            AiPlanning = cfg.GetValue("ai", "planning", AiPlanning).AsBool();
            AiVoice = cfg.GetValue("ai", "voice", AiVoice).AsBool();
            SpendCapUsd = cfg.GetValue("ai", "spend_cap_usd", SpendCapUsd).AsDouble();
            PlanWaitSeconds = (float)cfg.GetValue("ai", "plan_wait_seconds", PlanWaitSeconds).AsDouble();
            VoiceVolume = (float)cfg.GetValue("ai", "voice_volume", VoiceVolume).AsDouble();
            VoiceModel = cfg.GetValue("ai", "voice_model", VoiceModel).AsString();
            VoiceFallbackModels = cfg.GetValue("ai", "voice_fallback_models", VoiceFallbackModels).AsString();
            VoiceModelMinor = cfg.GetValue("ai", "voice_model_minor", VoiceModelMinor).AsString();
            VoiceRequestsPerMinute = (float)cfg.GetValue("ai", "voice_rpm", VoiceRequestsPerMinute).AsDouble();
            PlannerProvider = cfg.GetValue("ai", "planner_provider", PlannerProvider).AsString();
            VoiceProvider = cfg.GetValue("ai", "voice_provider", VoiceProvider).AsString();
            RunwareTextModel = cfg.GetValue("ai", "runware_text_model", RunwareTextModel).AsString();
            RunwareVoiceModel = cfg.GetValue("ai", "runware_voice_model", RunwareVoiceModel).AsString();
            RunwareVoiceModelMinor = cfg.GetValue("ai", "runware_voice_model_minor", RunwareVoiceModelMinor).AsString();
            RunwareVoiceFallbackModels = cfg.GetValue("ai", "runware_voice_fallback_models", RunwareVoiceFallbackModels).AsString();
            if (PlannerProvider != ProviderRunware) PlannerProvider = ProviderClaude;
            if (VoiceProvider != ProviderRunware) VoiceProvider = ProviderGemini;
            PlayerName = cfg.GetValue("multiplayer", "name", PlayerName).AsString();
            LastServerUrl = cfg.GetValue("multiplayer", "last_url", LastServerUrl).AsString();
            LastClassId = cfg.GetValue("multiplayer", "last_class", LastClassId).AsString();
        }
        var keys = new ConfigFile();
        if (keys.Load(KeysPath) == Error.Ok)
        {
            ClaudeKey = keys.GetValue("keys", "claude", "").AsString();
            GeminiKey = keys.GetValue("keys", "gemini", "").AsString();
            RunwareKey = keys.GetValue("keys", "runware", "").AsString();
        }
        // environment overrides for headless runs and servers; they are never written back to keys.cfg
        if (System.Environment.GetEnvironmentVariable("CLAUDE_API_KEY") is { Length: > 10 } claudeEnv) { ClaudeKey = claudeEnv; _envKeys = true; }
        if (System.Environment.GetEnvironmentVariable("GEMINI_API_KEY") is { Length: > 10 } geminiEnv) { GeminiKey = geminiEnv; _envKeys = true; }
        if (System.Environment.GetEnvironmentVariable("RUNWARE_API_KEY") is { Length: > 10 } runwareEnv) { RunwareKey = runwareEnv; _envKeys = true; }
        Apply();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("camera", "sensitivity", MouseSensitivity);
        cfg.SetValue("camera", "fov", Fov);
        cfg.SetValue("camera", "invert_y", InvertY);
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("display", "vsync", VSync);
        cfg.SetValue("display", "haze", Haze);
        cfg.SetValue("display", "resolution", Resolution);
        cfg.SetValue("danger", "ambushes", Ambushes);
        cfg.SetValue("danger", "reinforcements", Reinforcements);
        foreach (var (action, key) in _binds) cfg.SetValue("keys", action, (long)key);
        cfg.SetValue("ai", "planning", AiPlanning);
        cfg.SetValue("ai", "voice", AiVoice);
        cfg.SetValue("ai", "spend_cap_usd", SpendCapUsd);
        cfg.SetValue("ai", "plan_wait_seconds", PlanWaitSeconds);
        cfg.SetValue("ai", "voice_volume", VoiceVolume);
        cfg.SetValue("ai", "voice_model", VoiceModel);
        cfg.SetValue("ai", "voice_fallback_models", VoiceFallbackModels);
        cfg.SetValue("ai", "voice_model_minor", VoiceModelMinor);
        cfg.SetValue("ai", "voice_rpm", VoiceRequestsPerMinute);
        cfg.SetValue("ai", "planner_provider", PlannerProvider);
        cfg.SetValue("ai", "voice_provider", VoiceProvider);
        cfg.SetValue("ai", "runware_text_model", RunwareTextModel);
        cfg.SetValue("ai", "runware_voice_model", RunwareVoiceModel);
        cfg.SetValue("ai", "runware_voice_model_minor", RunwareVoiceModelMinor);
        cfg.SetValue("ai", "runware_voice_fallback_models", RunwareVoiceFallbackModels);
        cfg.SetValue("multiplayer", "name", PlayerName);
        cfg.SetValue("multiplayer", "last_url", LastServerUrl);
        cfg.SetValue("multiplayer", "last_class", LastClassId);
        cfg.Save(Path);
        if (_envKeys) return; // keys from the environment stay there
        var keys = new ConfigFile();
        keys.SetValue("keys", "claude", ClaudeKey.Trim());
        keys.SetValue("keys", "gemini", GeminiKey.Trim());
        keys.SetValue("keys", "runware", RunwareKey.Trim());
        keys.Save(KeysPath);
    }

    public static void Apply()
    {
        // the voice model names feed line ids, so they follow the selected provider
        if (VoiceViaRunware)
        {
            Adventure.Voice.Voices.Model = string.IsNullOrWhiteSpace(RunwareVoiceModel) ? Adventure.Voice.RunwareVoices.DefaultModel : RunwareVoiceModel.Trim();
            Adventure.Voice.Voices.MinorModel = string.IsNullOrWhiteSpace(RunwareVoiceModelMinor) ? Adventure.Voice.Voices.Model : RunwareVoiceModelMinor.Trim();
        }
        else
        {
            Adventure.Voice.Voices.Model = string.IsNullOrWhiteSpace(VoiceModel) ? Adventure.Voice.Voices.DefaultModel : VoiceModel.Trim();
            Adventure.Voice.Voices.MinorModel = string.IsNullOrWhiteSpace(VoiceModelMinor) ? Adventure.Voice.Voices.Model : VoiceModelMinor.Trim();
        }
        AudioManager.SetBusVolume("Master", MasterVolume);
        AudioManager.SetBusVolume("Music", MusicVolume);
        AudioManager.SetBusVolume("SFX", SfxVolume);
        AudioManager.SetBusVolume("Voice", VoiceVolume);
        if (!DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
        {
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
            if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
            if (!Fullscreen && DisplayServer.WindowGetSize() != Resolution) DisplayServer.WindowSetSize(Resolution);
        }
        foreach (var (action, key) in _binds) Bind(action, key);
        if (Main.Instance?.InGame == true) { Main.Instance.Party.ApplySettings(); Main.Instance.ApplyHaze(); }
    }

    public static void Rebind(string action, Key key)
    {
        if (key != Key.None)
            foreach (var other in _binds.Where(kv => kv.Key != action && kv.Value == key).Select(kv => kv.Key).ToList())
            {
                _binds[other] = Key.None; // one key, one action: the old owner goes blank
                Bind(other, Key.None);
            }
        _binds[action] = key;
        Bind(action, key);
        Save(); // a rebind is worth keeping even if the screen is closed with Escape or the game quits
    }

    public static void ResetKeys()
    {
        foreach (var (action, key) in _defaults) { _binds[action] = key; Bind(action, key); }
        Save();
    }

    private static void Bind(string action, Key key)
    {
        if (!InputMap.HasAction(action)) return;
        foreach (var ev in InputMap.ActionGetEvents(action).OfType<InputEventKey>().ToList()) InputMap.ActionEraseEvent(action, ev);
        if (key != Key.None) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
