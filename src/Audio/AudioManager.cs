using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Audio;

/// <summary>
/// Plays sound cues (Kenney CC0 clips) and per-tier ambience. Autoload. Gameplay raises cues through
/// EventBus.Sfx; this class maps cue names to clip pools and rotates through them.
/// Buses: Master, Music, SFX (created here so no editor setup is needed). Music tracks are chosen by <see cref="MusicDirector"/>.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    private const string Rpg = "res://assets/audio/kenney/kenney_rpg-audio/Audio/";
    private const string Impact = "res://assets/audio/kenney/kenney_impact-sounds/Audio/";
    private const string Ui = "res://assets/audio/kenney/kenney_interface-sounds/Audio/";

    private static readonly Dictionary<string, string[]> Cues = new()
    {
        ["swing"] = Range(Rpg, "knifeSlice", "", 1, 2, ".ogg", plain: true),
        ["swing_heavy"] = new[] { Rpg + "chop.ogg" },
        ["hit_flesh"] = Range(Impact, "impactPunch_medium", "_", 0, 4),
        ["hit_heavy"] = Range(Impact, "impactPunch_heavy", "_", 0, 4),
        ["hit_metal"] = Range(Impact, "impactMetal_medium", "_", 0, 4),
        ["block"] = Range(Impact, "impactPlate_medium", "_", 0, 4),
        ["miss"] = Range(Rpg, "cloth", "", 1, 4, ".ogg", plain: true),
        ["bow"] = new[] { Rpg + "drawKnife1.ogg", Rpg + "drawKnife2.ogg", Rpg + "drawKnife3.ogg" },
        ["cast"] = Range(Impact, "impactBell_heavy", "_", 0, 4),
        ["cast_hit"] = Range(Impact, "impactGlass_medium", "_", 0, 4),
        ["enemy_hurt"] = Range(Impact, "impactSoft_medium", "_", 0, 4),
        ["enemy_die"] = Range(Impact, "impactSoft_heavy", "_", 0, 4),
        ["barrel"] = Range(Impact, "impactWood_heavy", "_", 0, 4),
        ["footstep"] = Range(Impact, "footstep_concrete", "_", 0, 4),
        ["pickup"] = new[] { Rpg + "handleSmallLeather.ogg", Rpg + "handleSmallLeather2.ogg" },
        ["coins"] = new[] { Rpg + "handleCoins.ogg", Rpg + "handleCoins2.ogg" },
        ["equip"] = new[] { Rpg + "clothBelt.ogg", Rpg + "clothBelt2.ogg" },
        ["potion"] = new[] { Rpg + "bookClose.ogg" },
        ["chest"] = new[] { Rpg + "metalLatch.ogg", Rpg + "creak1.ogg" },
        ["door"] = new[] { Rpg + "doorOpen_1.ogg", Rpg + "doorOpen_2.ogg" },
        ["lever"] = new[] { Rpg + "metalClick.ogg" },
        ["lock_fail"] = new[] { Rpg + "metalClick.ogg" },
        ["secret"] = new[] { Rpg + "creak2.ogg", Rpg + "creak3.ogg" },
        ["trap"] = Range(Impact, "impactMining", "_", 0, 4),
        ["stairs"] = Range(Impact, "footstep_wood", "_", 0, 4),
        ["level_up"] = Range(Ui, "confirmation", "_", 1, 4),
        ["skill"] = Range(Ui, "select", "_", 1, 4),
        ["ui_click"] = Range(Ui, "click", "_", 1, 5),
        ["ui_open"] = Range(Ui, "open", "_", 1, 4),
        ["ui_close"] = Range(Ui, "close", "_", 1, 4),
        ["error"] = Range(Ui, "error", "_", 1, 4),
        ["victory"] = new[] { Ui + "bong_001.ogg" },
        ["fall"] = Range(Impact, "impactPlank_medium", "_", 0, 4),
        ["boss"] = new[] { Impact + "impactBell_heavy_001.ogg", Impact + "impactBell_heavy_003.ogg" },
    };

    private static string[] Range(string dir, string stem, string sep, int from, int to, string ext = ".ogg", bool plain = false)
        => Enumerable.Range(from, to - from + 1).Select(i => plain ? $"{dir}{stem}{i}{ext}" : $"{dir}{stem}{sep}{i:000}{ext}").ToArray();

    private readonly Dictionary<string, AudioStream[]> _streams = new();
    private readonly Dictionary<string, int> _next = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private AudioStreamPlayer _ambient = null!;
    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _voice = null!;
    public bool VoiceBusy => _voice != null && _voice.Playing;
    private AudioStreamGenerator? _ambientGen;
    private AudioStreamGeneratorPlayback? _ambientPlayback;
    private Random _rng = new(3);
    private float _ambientHum = 55f;
    private float _ambientNoise = 0.03f;
    private double _phase;
    private float _lowpass;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        EnsureBus("Music");
        EnsureBus("SFX");
        EnsureBus("Voice");

        for (var i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(p);
            _players.Add(p);
        }
        _ambient = new AudioStreamPlayer { Bus = "Music", VolumeDb = -14 };
        AddChild(_ambient);
        _music = new AudioStreamPlayer { Bus = "Music", VolumeDb = -10 };
        AddChild(_music);
        _voice = new AudioStreamPlayer { Bus = "Voice", VolumeDb = 0 };
        AddChild(_voice);

        foreach (var (cue, files) in Cues)
        {
            var list = files.Where(f => ResourceLoader.Exists(f)).Select(f => GD.Load<AudioStream>(f)).ToArray();
            if (list.Length == 0) GD.PushWarning($"[Audio] no clips found for cue '{cue}'");
            _streams[cue] = list;
        }
        EventBus.Instance.Sfx += cue => Play(cue);
        EventBus.Instance.FloorChanged += (_, _) => Play("stairs");
        GD.Print($"[Audio] {_streams.Count(kv => kv.Value.Length > 0)} cues ready");
        StartAmbient();
        AddChild(new MusicDirector { Name = "Music" });
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        AudioServer.AddBus();
        AudioServer.SetBusName(AudioServer.BusCount - 1, name);
        AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
    }

    /// <summary>Speak a rendered line. Without <paramref name="interrupt"/> a busy voice channel wins.</summary>
    public bool PlayVoice(AudioStream stream, bool interrupt)
    {
        if (_voice.Playing && !interrupt) return false;
        _voice.Stream = stream;
        _voice.Play();
        return true;
    }

    /// <summary>Play a cue at a volume (dB offset) with slight pitch variation.</summary>
    public void Play(string cue, float volumeDb = 0f)
    {
        if (!_streams.TryGetValue(cue, out var list) || list.Length == 0) return;
        var idx = _next.GetValueOrDefault(cue);
        _next[cue] = (idx + 1) % list.Length;
        var player = _players.FirstOrDefault(p => !p.Playing) ?? _players[_rng.Next(_players.Count)];
        player.Stream = list[idx];
        player.VolumeDb = volumeDb;
        player.PitchScale = 0.92f + (float)_rng.NextDouble() * 0.16f;
        player.Play();
    }

    // ---- ambience: a generated low hum plus filtered noise, colored per tier ---------------------

    public void SetTierAmbience(int tierIndex)
    {
        (_ambientHum, _ambientNoise) = tierIndex switch
        {
            1 => (48f, 0.05f),   // caves: deeper, more wind
            2 => (70f, 0.04f),   // forge: rumble
            3 => (38f, 0.06f),   // abyss: very low
            _ => (55f, 0.03f),   // crypt
        };
    }

    private void StartAmbient()
    {
        _ambientGen = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.5f };
        _ambient.Stream = _ambientGen;
        _ambient.Play();
        _ambientPlayback = _ambient.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_ambientPlayback == null || _ambientGen == null) return;
        var frames = _ambientPlayback.GetFramesAvailable();
        if (frames <= 0) return;
        var rate = _ambientGen.MixRate;
        var buffer = new Vector2[frames];
        for (var i = 0; i < frames; i++)
        {
            _phase += Math.Tau * _ambientHum / rate;
            var hum = (float)(Math.Sin(_phase) * 0.35 + Math.Sin(_phase * 2.01) * 0.15);
            var white = (float)(_rng.NextDouble() * 2 - 1);
            _lowpass += (white - _lowpass) * 0.02f; // crude low-pass for a wind-like rumble
            var sample = hum * 0.4f + _lowpass * _ambientNoise * 8f;
            buffer[i] = new Vector2(sample, sample);
        }
        _ambientPlayback.PushBuffer(buffer);
    }

    public static void SetBusVolume(string bus, float linear)
    {
        var idx = AudioServer.GetBusIndex(bus);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, linear <= 0.001f ? -80f : Mathf.LinearToDb(linear));
    }
}
