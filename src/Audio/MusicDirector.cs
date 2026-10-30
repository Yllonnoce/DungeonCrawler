using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Audio;

public enum Mood { Silent, Menu, Explore, Combat, Boss }

/// <summary>
/// Picks and crossfades the CC0 tracks under assets/audio/music: a tavern tune on the title screen, an exploration
/// track per tier (shuffled, with a breath of silence between), a battle track while anything is fighting the party,
/// the boss track from the moment a guardian notices you until it falls, and short stings for victory and a wipe.
/// Music ducks under spoken lines. A dedicated server plays nothing.
/// </summary>
public partial class MusicDirector : Node
{
    public static MusicDirector? Instance { get; private set; }

    private const string Dir = "res://assets/audio/music/";
    private const float BaseDb = -7f;
    private const float DuckDb = -9f;
    private const float CombatHoldSeconds = 6f;
    private const float EngageRadius = 26f;

    private static readonly string[] MenuTracks = { "menu_old_tower_inn.mp3" };
    private static readonly string[][] ExploreByTier =
    {
        new[] { "crypt_dungeon_theme.mp3", "crypt_dark_theme.ogg", "crypt_medieval_exploration.mp3" },
        new[] { "caves_dark_cavern_1.ogg", "caves_dark_cavern_2.ogg", "caves_going_deep.ogg" },
        new[] { "forge_heavy_dungeon.ogg", "forge_cursed_tower_veranda.ogg", "forge_ghosts_and_heroes.ogg" },
        new[] { "abyss_dark_shrine_loop.ogg", "abyss_dungeon_ambience.ogg", "abyss_dark_intro.ogg" },
    };
    private static readonly string[] CombatTracks = { "combat_battle_theme_a.mp3", "combat_battle_theme_b.mp3", "combat_medieval_battle.mp3" };
    private static readonly string[] BossTracks = { "boss_battle_rpg_theme.ogg" };
    private const string VictorySting = "sting_victory.mp3";
    private const string DefeatSting = "sting_defeat.mp3";

    private readonly Dictionary<string, AudioStream> _cache = new();
    private AudioStreamPlayer _a = null!, _b = null!, _sting = null!;
    private AudioStreamPlayer _front = null!;
    private float _frontTarget = -80f, _backTarget = -80f;
    private float _fadeSpeed = 6f; // dB per second
    private readonly Random _rng = new();
    private float _pollTimer;
    private double _combatUntil = -1;
    private double _clock;
    private float _gapLeft;
    private bool _bossActive;
    private bool _victoryPlayed;
    private string _lastExplore = "";
    private int _tier;

    public Mood Mood { get; private set; } = Mood.Silent;
    /// <summary>File name of the track in front, "" while silent.</summary>
    public string CurrentTrack { get; private set; } = "";
    public bool Enabled { get; set; } = true;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // keeps fading while the pause menu holds the tree
        _a = new AudioStreamPlayer { Name = "MusicA", Bus = "Music", VolumeDb = -80f };
        _b = new AudioStreamPlayer { Name = "MusicB", Bus = "Music", VolumeDb = -80f };
        _sting = new AudioStreamPlayer { Name = "Sting", Bus = "Music", VolumeDb = -3f };
        AddChild(_a); AddChild(_b); AddChild(_sting);
        _front = _a;
        _a.Finished += () => OnFinished(_a);
        _b.Finished += () => OnFinished(_b);
        var bus = EventBus.Instance;
        bus.BossSeen += _ => _bossActive = true;
        bus.BossDefeated += _ => _bossActive = false;
        bus.FloorChanged += (_, _) => { _bossActive = false; _combatUntil = -1; };
        bus.PartyWiped += () => PlaySting(DefeatSting);
        var found = MenuTracks.Concat(ExploreByTier.SelectMany(t => t)).Concat(CombatTracks).Concat(BossTracks).Count(f => ResourceLoader.Exists(Dir + f));
        GD.Print($"[Music] {found} tracks available");
        if (found == 0) Enabled = false;
    }

    private AudioStream? Load(string file, bool loop)
    {
        if (!_cache.TryGetValue(file, out var stream))
        {
            var path = Dir + file;
            if (!ResourceLoader.Exists(path)) return null;
            stream = GD.Load<AudioStream>(path);
            _cache[file] = stream;
        }
        switch (stream)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = loop; break;
            case AudioStreamMP3 mp3: mp3.Loop = loop; break;
        }
        return stream;
    }

    // ---- mood --------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _clock += delta;
        if (!Enabled || Net.NetSession.IsServer) { if (_front.Playing) { _frontTarget = -80f; } Fade(dt); return; }
        _pollTimer -= dt;
        if (_pollTimer <= 0f) { _pollTimer = 0.5f; Decide(); }
        if (_gapLeft > 0f) { _gapLeft -= dt; if (_gapLeft <= 0f && Mood == Mood.Explore) Play(PickExplore(), loop: false, fadeSeconds: 4f); }
        Fade(dt);
    }

    private void Decide()
    {
        var main = Main.Instance;
        var wanted = Mood.Menu;
        if (main != null && main.InGame)
        {
            if (Game.Instance.Victory && !_victoryPlayed) { _victoryPlayed = true; PlaySting(VictorySting); }
            if (!Game.Instance.Victory) _victoryPlayed = false;
            var tier = main.Builder.CurrentTier?.Index ?? 0;
            if (tier != _tier && Mood == Mood.Explore) { _tier = tier; Play(PickExplore(), loop: false, fadeSeconds: 3f); }
            _tier = tier;
            if (_bossActive) wanted = Mood.Boss;
            else
            {
                if (AnyoneFighting(main)) _combatUntil = _clock + CombatHoldSeconds;
                wanted = _clock < _combatUntil ? Mood.Combat : Mood.Explore;
            }
        }
        if (wanted == Mood) return;
        var from = Mood;
        Mood = wanted;
        switch (wanted)
        {
            case Mood.Menu: Play(MenuTracks[0], loop: true, fadeSeconds: 2f); break;
            case Mood.Explore: _gapLeft = 0f; Play(PickExplore(), loop: false, fadeSeconds: from == Mood.Menu ? 2f : 3.5f); break;
            case Mood.Combat: Play(CombatTracks[_rng.Next(CombatTracks.Length)], loop: true, fadeSeconds: 1.2f); break;
            case Mood.Boss: Play(BossTracks[_rng.Next(BossTracks.Length)], loop: true, fadeSeconds: 1.5f); break;
        }
    }

    /// <summary>An engaged enemy within earshot of a standing member: the party is in a fight (the idle razzing holds its tongue too).</summary>
    public static bool AnyoneFighting(Main main)
    {
        if (main.Builder.FloorRoot is not { } root || !main.Party.HasBodies) return false;
        var bodies = main.Party.Active.Where(a => !a.Downed).Select(a => a.GlobalPosition).ToList();
        if (bodies.Count == 0) return false;
        foreach (var e in root.GetChildren().OfType<Enemy>())
        {
            if (!e.IsAlive) continue;
            var engaged = Net.NetSession.IsClient ? !e.IsUnaware : e.IsEngaged;
            if (!engaged) continue;
            foreach (var b in bodies) if (b.DistanceSquaredTo(e.GlobalPosition) < EngageRadius * EngageRadius) return true;
        }
        return false;
    }

    private string PickExplore()
    {
        var pool = ExploreByTier[Math.Clamp(_tier, 0, ExploreByTier.Length - 1)].Where(f => ResourceLoader.Exists(Dir + f)).ToList();
        if (pool.Count == 0) pool = ExploreByTier[0].ToList();
        var choices = pool.Count > 1 ? pool.Where(f => f != _lastExplore).ToList() : pool;
        _lastExplore = choices[_rng.Next(choices.Count)];
        return _lastExplore;
    }

    // ---- players -----------------------------------------------------------------------------

    private void Play(string file, bool loop, float fadeSeconds)
    {
        var stream = Load(file, loop);
        if (stream == null) return;
        var back = _front;
        _front = back == _a ? _b : _a;
        _front.Stream = stream;
        _front.VolumeDb = -80f;
        _front.Play();
        _frontTarget = BaseDb;
        _backTarget = -80f;
        _fadeSpeed = Math.Max(4f, 75f / Math.Max(fadeSeconds, 0.1f));
        CurrentTrack = file;
    }

    private void OnFinished(AudioStreamPlayer p)
    {
        if (p != _front || Mood != Mood.Explore) return;
        _gapLeft = 4f + (float)_rng.NextDouble() * 6f; // a breath before the next piece
        CurrentTrack = "";
    }

    private void Fade(float dt)
    {
        var duck = (AudioManager.Instance?.VoiceBusy == true ? DuckDb : 0f) + (_sting.Playing ? -12f : 0f);
        var back = _front == _a ? _b : _a;
        _front.VolumeDb = Mathf.MoveToward(_front.VolumeDb, _frontTarget + duck, _fadeSpeed * dt);
        back.VolumeDb = Mathf.MoveToward(back.VolumeDb, _backTarget, _fadeSpeed * dt);
        if (back.Playing && back.VolumeDb <= -79f) back.Stop();
    }

    private void PlaySting(string file)
    {
        var stream = Load(file, loop: false);
        if (stream == null) return;
        _sting.Stream = stream;
        _sting.Play();
    }
}
