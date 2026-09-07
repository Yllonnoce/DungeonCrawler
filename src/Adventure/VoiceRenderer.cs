using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Adventure.AiClients;
using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Audio;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// Renders plan lines to audio in the background and plays them back. Lines are content-addressed: the shipped bank
/// under assets/voice/builtin is checked first, then user://voice; anything missing is rendered by the configured
/// synth, two at a time, never while enemies are close. Playback goes to the Voice bus with narration interrupting barks.
/// </summary>
public partial class VoiceRenderer : Node
{
    public static VoiceRenderer? Instance { get; private set; }
    public const string BuiltinDir = "res://assets/voice/builtin";
    public const int MaxConcurrent = 2;
    public const float CombatRadius = 20f;

    private Func<string, IVoiceSynth>? _factory;
    private readonly Dictionary<string, IVoiceSynth> _synths = new();
    /// <summary>Models whose quota is gone for today.</summary>
    private readonly HashSet<string> _exhausted = new();
    private readonly VoiceQueue _queue = new();
    private int _active;
    private float _combatTimer;
    private double _backoffUntil;
    /// <summary>Requests are spaced so no more than this many start per minute; each per-minute 429 lowers it for the session.</summary>
    public float PaceRpm { get; private set; } = 8f;
    private double _nextStartAt;
    private double _rateLimitedSince = -1;
    private float _lastBackoff;
    private string _cacheDir = "";
    public bool Enabled => _factory != null;
    /// <summary>Lines rendered per model this session (the bake reports it).</summary>
    public Dictionary<string, int> RenderedByModel { get; } = new();
    public bool InCombat { get; private set; }
    public int Pending => _queue.Count + _active;
    public int Rendered { get; private set; }
    public int Failed { get; private set; }
    /// <summary>Why rendering stopped for this session (quota, repeated failures); null while running.</summary>
    public string? StoppedReason { get; private set; }
    private int _consecutiveRateLimits;
    /// <summary>Where new files go; the bake flag points it at the shipped bank.</summary>
    public string CacheDir { get => _cacheDir; set { _cacheDir = value; Directory.CreateDirectory(value); } }

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        CacheDir = ProjectSettings.GlobalizePath("user://voice");
        EventBus.Instance.Narration += (text, voiceId) => { if (voiceId != null) Play(voiceId, interrupt: true); };
    }

    /// <summary>Pick the synth from settings and debug flags; safe to call any time.</summary>
    public void Configure()
    {
        var debug = DebugTools.Instance;
        StoppedReason = null;
        _consecutiveRateLimits = 0;
        // the tone synth writes to its own folder so placeholder files never shadow real speech
        CacheDir = ProjectSettings.GlobalizePath(debug?.FakeVoice == true ? "user://voice_fake" : "user://voice");
        _synths.Clear();
        _exhausted.Clear();
        var fake = debug?.FakeVoice == true;
        // the pace exists for Google's per-minute quota on the preview TTS models; the tone synth and Runware (which asks for a few concurrent requests, not a rate) run at full speed
        PaceRpm = fake || Settings.VoiceViaRunware ? 120f : Math.Clamp(Settings.VoiceRequestsPerMinute, 1f, 120f);
        _rateLimitedSince = -1;
        _lastBackoff = 0f;
        _factory = fake ? _ => new FakeVoiceSynth()
            : debug?.NoVoice == true ? null
            : !Settings.AiVoice ? null
            : Settings.VoiceViaRunware && Settings.HasRunwareKey ? model => new RunwareTtsClient(Settings.RunwareKey.Trim(), model)
            : !Settings.VoiceViaRunware && Settings.HasGeminiKey ? model => new GeminiTtsClient(Settings.GeminiKey.Trim(), model)
            : null;
    }

    /// <summary>Every model we may use, best first: the main model, the minor one, then the fallbacks.</summary>
    private static List<string> ModelChain()
    {
        var chain = new List<string> { Voices.Model, Voices.MinorModel };
        chain.AddRange(Settings.ActiveVoiceFallbackModels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return chain.Distinct().ToList();
    }

    /// <summary>The model a job actually renders with: its own unless that quota is gone, then the next one down the chain.</summary>
    private string? ResolveModel(string wanted)
    {
        if (!_exhausted.Contains(wanted)) return wanted;
        var chain = ModelChain();
        var from = Math.Max(0, chain.IndexOf(wanted));
        return chain.Skip(from).Concat(chain).FirstOrDefault(m => !_exhausted.Contains(m));
    }

    private IVoiceSynth SynthFor(string model)
    {
        if (!_synths.TryGetValue(model, out var s)) _synths[model] = s = _factory!(model);
        return s;
    }

    /// <summary>The main model rendering now (after any fallback).</summary>
    public string CurrentModel => ResolveModel(Voices.Model) ?? Voices.Model;
    /// <summary>Raised when rendering stops for the session, with the reason: menus and the bake surface it.</summary>
    public event Action<string>? Stopped;

    public bool HasAudio(string id) => File.Exists(CachePath(id)) || Godot.FileAccess.FileExists($"{BuiltinDir}/{id}.wav");
    private string CachePath(string id) => Path.Combine(CacheDir, $"{id}.wav");

    /// <summary>Give a line an id and queue it if it has no audio yet. Returns false when voice is off entirely.</summary>
    /// <summary>When set, lines it rejects are not rendered (a bake keeping to its voice budget).</summary>
    public Func<Line, bool>? Filter { get; set; }

    /// <param name="major">A main character (the cast): rendered with the main model like the narrator and bosses; one-off NPCs and barks use the minor model.</param>
    public bool Enqueue(Line line, string voice, Speaker speaker, VoicePriority priority, bool major = true)
    {
        if (Filter != null && !Filter(line)) { line.VoiceId = null; return false; }
        if (string.IsNullOrWhiteSpace(line.Text)) return false;
        var style = Voices.Style(speaker);
        var model = Voices.ModelFor(speaker, major);
        var id = VoiceLineId.For(line.Text, voice, style, model);
        line.VoiceId = id;
        if (!Enabled) return HasAudio(id);
        _queue.Enqueue(line.Text, voice, style, priority, HasAudio, model);
        return true;
    }

    public AudioStream? Load(string id)
    {
        try
        {
            var builtin = $"{BuiltinDir}/{id}.wav";
            if (Godot.FileAccess.FileExists(builtin) && ResourceLoader.Exists(builtin)) return GD.Load<AudioStream>(builtin);
            var path = CachePath(id);
            if (!File.Exists(path)) return null;
            var (pcm, rate) = WavWriter.Unwrap(File.ReadAllBytes(path));
            return new AudioStreamWav { Data = pcm, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false };
        }
        catch (Exception e) { GD.PushWarning($"[Voice] cannot load {id}: {e.Message}"); return null; }
    }

    /// <summary>Play a rendered line now; false when it has no audio or a more important line is speaking.</summary>
    public bool Play(string? id, bool interrupt)
    {
        if (id == null || AudioManager.Instance == null) return false;
        var stream = Load(id);
        return stream != null && AudioManager.Instance.PlayVoice(stream, interrupt);
    }

    public bool PlayLine(Line line, bool interrupt) => Play(line.VoiceId, interrupt);

    public override void _Process(double delta)
    {
        _combatTimer -= (float)delta;
        if (_combatTimer <= 0f)
        {
            _combatTimer = 0.5f;
            InCombat = SenseCombat();
            _queue.Paused = InCombat;
        }
        if (_factory == null || _active >= MaxConcurrent || Time.GetTicksMsec() < _backoffUntil || _queue.Count == 0) return;
        // a fight can start between samples; look again right before sending anything
        InCombat = SenseCombat();
        _queue.Paused = InCombat;
        // pace the starts: the preview TTS models allow only a handful of requests a minute, and a burst of 429s reads as "rate stopped" to the player
        var now = Time.GetTicksMsec() / 1000.0;
        while (_active < MaxConcurrent && now >= _nextStartAt && _queue.Next() is { } job)
        {
            _nextStartAt = now + 60.0 / PaceRpm;
            StartJob(job);
        }
    }

    /// <summary>Stop rendering for the session. Queued lines are dropped but not marked done, so they render on the next request.</summary>
    public void Stop(string reason)
    {
        if (_factory == null) return;
        _factory = null;
        _synths.Clear();
        StoppedReason = reason;
        _queue.Clear();
        GD.PushWarning($"[Voice] STOPPED: {reason}");
        EventBus.Instance.Log($"Voice stopped: {reason}");
        Main.Instance?.Menu?.ShowStatus($"Voice stopped: {reason}");
        Stopped?.Invoke(reason);
    }

    private static bool SenseCombat()
    {
        var party = Main.Instance?.Party;
        var root = Main.Instance?.Builder.FloorRoot;
        if (party == null || root == null || !GodotObject.IsInstanceValid(party) || !party.HasBodies) return false;
        var lead = party.Lead.GlobalPosition;
        foreach (var e in root.GetChildren().OfType<Enemy>())
            if (e.IsHostile && e.GlobalPosition.DistanceTo(lead) < CombatRadius) return true;
        return false;
    }

    private void StartJob(VoiceJob job)
    {
        var model = ResolveModel(job.Model == "" ? Voices.Model : job.Model);
        if (model == null) { Stop(Settings.VoiceViaRunware ? "the Runware balance is used up. Top it up and press \"Render voice\" again; nothing is lost." : "every configured Gemini model has used up its quota for today. Add a model in Settings or try again tomorrow; nothing is lost."); return; }
        var synth = SynthFor(model);
        _active++;
        _ = Task.Run(async () =>
        {
            byte[]? pcm = null;
            string? error = null;
            try { pcm = await synth.RenderPcmAsync(job.Text, job.Voice, job.Style, CancellationToken.None); }
            catch (Exception e) { error = e.Message; }
            if (pcm != null)
            {
                try { File.WriteAllBytes(CachePath(job.Id), WavWriter.Wrap(pcm)); }
                catch (Exception e) { error = e.Message; pcm = null; }
            }
            Callable.From(() =>
            {
                _active--;
                if (pcm != null)
                {
                    Rendered++; RenderedByModel[model] = RenderedByModel.GetValueOrDefault(model) + 1; _consecutiveRateLimits = 0; _rateLimitedSince = -1; _lastBackoff = 0f;
                    return;
                }
                Failed++;
                _queue.Forget(job.Id);
                var diag = synth as IVoiceSynthDiagnostics;
                var why = error ?? diag?.LastError ?? "unknown";
                GD.PushWarning($"[Voice] {job.Id} ({job.Priority}) failed: {why}");
                var provider = Settings.VoiceLabel;
                if (diag != null && diag.QuotaExhausted)
                {
                    // a daily quota (Gemini) or an empty balance (Runware): this model is done for now, so its lines move down the chain
                    _exhausted.Add(diag.Model);
                    _consecutiveRateLimits = 0;
                    var next = Settings.VoiceViaRunware ? null : ResolveModel(diag.Model); // every Runware model draws on the same balance
                    if (next == null) Stop(Settings.VoiceViaRunware ? "the Runware balance is used up. Top it up and press \"Render voice\" again; nothing is lost." : $"Gemini quota for {diag.Model} is used up and no other model is left. Add a fallback model in Settings or try again tomorrow; nothing is lost.");
                    else { var note = $"Gemini quota for {diag.Model} is used up; its lines continue with {next}."; GD.Print($"[Voice] {note}"); EventBus.Instance.Log(note); }
                }
                else if (diag is { LastStatus: 429 or 503 or 504 })
                {
                    // a per-minute limit or a full queue: wait as long as the provider asked (or a little longer each time), slow the pace, and only give up after a quarter hour of nothing else
                    _consecutiveRateLimits++;
                    var seconds = Time.GetTicksMsec() / 1000.0;
                    if (_rateLimitedSince < 0) _rateLimitedSince = seconds;
                    _lastBackoff = Math.Clamp(diag.RetryAfterSeconds ?? (_lastBackoff <= 0f ? 15f : _lastBackoff * 2f), 5f, 120f);
                    PaceRpm = Math.Max(2f, PaceRpm * 0.75f);
                    _backoffUntil = Time.GetTicksMsec() + (ulong)(_lastBackoff * 1000f);
                    _queue.Forget(job.Id);
                    GD.Print($"[Voice] {diag.Model} {RunwareWire.DescribeStatus(diag.LastStatus)}: waiting {_lastBackoff:0} s, pace now {PaceRpm:0.0} requests/min");
                    if (seconds - _rateLimitedSince > 900) Stop($"{provider} has rate limited every request for fifteen minutes ({diag.Model}). Lower \"voice requests per minute\" in Settings and press \"Render voice\" again; nothing is lost.");
                }
                else if (Failed >= 5 && Rendered == 0) Stop("Voice stopped after repeated failures; check the key and the console.");
            }).CallDeferred();
        });
    }
}
