using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Audio;

public enum Mood { Silent, Menu, Tavern, Explore, Combat, Boss, Chase }

/// <summary>
/// Picks and crossfades the CC0 tracks under assets/audio/music: a tavern tune on the title screen and in the tavern, an exploration
/// track per tier (shuffled, with a breath of silence between), a battle track while anything is fighting the party,
/// the boss track from the moment a guardian notices you until it falls, and short stings for victory and a wipe. Floor 12's rides
/// play the Chase mood (<see cref="RideMusic.Wanted"/>): a looping chase on rides 1-3, and on ride 4 the forge piece phase-locked to
/// the floor clock so the drop hammers fall on its beat. Music ducks under spoken lines. A dedicated server plays nothing.
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
        new[] { "crypt_dungeon_theme.mp3", "crypt_dark_theme.ogg", "crypt_medieval_exploration.mp3", "explore_dungeon_walking.mp3" },
        new[] { "caves_dark_cavern_1.ogg", "caves_dark_cavern_2.ogg", "caves_going_deep.ogg", "explore_dungeon_walking.mp3" },
        new[] { "forge_heavy_dungeon.ogg", "forge_cursed_tower_veranda.ogg", "forge_ghosts_and_heroes.ogg", "explore_dungeon_walking.mp3" },
        new[] { "abyss_dark_shrine_loop.ogg", "abyss_dungeon_ambience.ogg", "abyss_dark_intro.ogg", "explore_dungeon_walking.mp3" },
    };
    private static readonly string[] CombatTracks = { "combat_battle_theme_a.mp3", "combat_battle_theme_b.mp3", "combat_medieval_battle.mp3" };
    private static readonly string[] BossTracks = { "boss_battle_rpg_theme.ogg" };
    private static readonly string[] ChaseTracks = { RideMusic.ChaseTrack };
    /// <summary>Ride 4's track (<see cref="RideMusic.PourTrack"/>), played on the hammers' beat.</summary>
    private const string PourTrack = RideMusic.PourTrack;
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
    /// <summary>The last poll the ride wanted the Chase music (−∞: never, or since a load or a floor change).</summary>
    private double _rideSeen = double.NegativeInfinity;
    /// <summary>The Chase track the ride last asked for (kept through the hold), and the one this mood last started.</summary>
    private string _chaseWanted = ChaseTracks[0], _chaseStarted = "";

    public Mood Mood { get; private set; } = Mood.Silent;
    /// <summary>File name of the track in front, "" while silent.</summary>
    public string CurrentTrack { get; private set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>The pitch scale of the player in front (1, but <see cref="RideMusic.PourPitch"/> for the pour on ride 4; tests read it).</summary>
    public float FrontPitch => _front.PitchScale;
    /// <summary>The pitch the front player should have now: the pour's while the Chase mood plays it, else 1 (tests check it).</summary>
    public float ExpectedPitch => Mood == Mood.Chase && CurrentTrack == PourTrack ? (float)RideMusic.PourPitch : 1f;

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
        bus.FloorChanged += (_, _) => { _bossActive = false; _combatUntil = -1; _rideSeen = double.NegativeInfinity; };
        bus.FloorBuilt += (_, _, _) => { _rideSeen = double.NegativeInfinity; PreloadFloor(); }; // a load rebuilds the floor: no hold carried over from before it
        bus.PartyWiped += () => PlaySting(DefeatSting);
        var found = MenuTracks.Concat(ExploreByTier.SelectMany(t => t)).Concat(CombatTracks).Concat(BossTracks).Concat(ChaseTracks).Count(f => ResourceLoader.Exists(Dir + f));
        GD.Print($"[Music] {found} tracks available");
        if (found == 0) Enabled = false;
    }

    /// <summary>Tracks asked of the loader thread (<see cref="PreloadFloor"/>) and not collected yet, by file name.</summary>
    private readonly HashSet<string> _preloading = new();
    private readonly List<string> _preloaded = new();

    /// <summary>
    /// A floor was built: have every track it can play read on a loader thread (the tier's exploration pool, the battle and
    /// boss tracks, and on a railed floor the rides' chase and pour), so no mood change reads one from disk as it starts (7 to
    /// 11 ms on the frame). A track loaded once stays in <see cref="_cache"/>; a dedicated server plays nothing and loads nothing.
    /// </summary>
    private void PreloadFloor()
    {
        if (!Enabled || Net.NetSession.IsServer || Main.Instance is not { InGame: true } main) return;
        var tier = Math.Clamp(main.Builder.CurrentTier?.Index ?? 0, 0, ExploreByTier.Length - 1);
        foreach (var file in ExploreByTier[tier]) Preload(file);
        foreach (var file in CombatTracks) Preload(file);
        foreach (var file in BossTracks) Preload(file);
        if (main.Builder.Layout?.Rail != null) foreach (var file in RideMusic.Tracks) Preload(file);
    }

    private void Preload(string file)
    {
        if (_cache.ContainsKey(file) || _preloading.Contains(file) || !ResourceLoader.Exists(Dir + file)) return;
        if (ResourceLoader.LoadThreadedRequest(Dir + file) == Error.Ok) _preloading.Add(file);
    }

    /// <summary>Take the finished preloads into the cache; one that failed is forgotten (its first play loads it the plain way).</summary>
    private void CollectPreloaded()
    {
        if (_preloading.Count == 0) return;
        _preloaded.Clear();
        foreach (var file in _preloading)
        {
            var status = ResourceLoader.LoadThreadedGetStatus(Dir + file);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress) continue;
            _preloaded.Add(file);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded && ResourceLoader.LoadThreadedGet(Dir + file) is AudioStream stream) _cache[file] = stream;
        }
        foreach (var file in _preloaded) _preloading.Remove(file);
    }

    public override void _ExitTree()
    {
        foreach (var file in _preloading) ResourceLoader.LoadThreadedGet(Dir + file); // a load still on its thread is waited for, then dropped
        _preloading.Clear();
    }

    private AudioStream? Load(string file, bool loop)
    {
        if (!_cache.TryGetValue(file, out var stream))
        {
            var path = Dir + file;
            if (!ResourceLoader.Exists(path)) return null;
            if (_preloading.Remove(file)) stream = ResourceLoader.LoadThreadedGet(path) as AudioStream; // a preload still on its thread: wait for it
            stream ??= GD.Load<AudioStream>(path);
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
        CollectPreloaded();
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
            if (Game.Instance.CurrentFloor == 0) wanted = Mood.Tavern; // the tavern over the dungeon keeps the title tune
            else if (_bossActive) wanted = Mood.Boss;
            else if (RideWanted(main)) wanted = Mood.Chase;
            else
            {
                if (AnyoneFighting(main)) _combatUntil = _clock + CombatHoldSeconds;
                wanted = _clock < _combatUntil ? Mood.Combat : Mood.Explore;
            }
        }
        if (wanted == Mood)
        {
            if (Mood == Mood.Chase && _chaseWanted != _chaseStarted) PlayChase(); // the next ride began before the hold ran out
            return;
        }
        var from = Mood;
        Mood = wanted;
        switch (wanted)
        {
            case Mood.Menu: Play(MenuTracks[0], loop: true, fadeSeconds: 2f); break;
            case Mood.Tavern: if (CurrentTrack != MenuTracks[0]) Play(MenuTracks[0], loop: true, fadeSeconds: 2f); break; // from the title it plays on unbroken
            case Mood.Explore: _gapLeft = 0f; Play(PickExplore(), loop: false, fadeSeconds: from == Mood.Menu ? 2f : 3.5f); break;
            case Mood.Combat: Play(CombatTracks[_rng.Next(CombatTracks.Length)], loop: true, fadeSeconds: 1.2f); break;
            case Mood.Boss: Play(BossTracks[_rng.Next(BossTracks.Length)], loop: true, fadeSeconds: 1.5f); break;
            case Mood.Chase: PlayChase(); break;
        }
    }

    /// <summary>
    /// The ride wants the Chase music, as this peer shows it (a client: the follower, the server's flags and its chaser copy), and which
    /// track (<see cref="_chaseWanted"/>, set while a cart rolls and kept through the hold). No allocation.
    /// </summary>
    private bool RideWanted(Main main)
    {
        if (RailController.Instance is not { } rc || !IsInstanceValid(rc) || !IsInstanceValid(rc.Cart)) return false;
        var me = main.Party.Local is { } local && IsInstanceValid(local) ? local : null;
        var cartRolling = rc.State.Mode == CartMode.Rolling;
        var seated = me != null && ReferenceEquals(me.Cart, rc.Cart);
        var distance = me != null ? me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) : float.PositiveInfinity;
        MineCart? chaser = null;
        var chaserRolling = false;
        var chaserS = 0f;
        if (rc.ChaseLive)
        {
            if (Net.NetSession.IsClient)
            {
                if (RailNet.Instance?.ClientChaser is { } cc) { chaser = cc.Cart; chaserRolling = cc.Follow.Mode == CartMode.Rolling; chaserS = cc.Follow.S; }
            }
            else if (rc.Chaser is { } c && rc.ChaserRun is { } run) { chaser = c; chaserRolling = run.State.Mode == CartMode.Rolling; chaserS = run.State.S; }
        }
        var inChaser = me != null && chaser != null && ReferenceEquals(me.Cart, chaser);
        var wanted = RideMusic.Wanted(cartRolling, seated, distance, chaserRolling, inChaser, _clock, ref _rideSeen);
        if (cartRolling && (seated || distance <= RideMusic.NearMetres)) _chaseWanted = RideMusic.TrackFor(RideMusic.Ride(rc.StopIndex));
        else if (chaserRolling && inChaser) _chaseWanted = RideMusic.TrackFor(RideMusic.Ride(RideMusic.StopBehind(rc.StopS, chaserS)));
        return wanted;
    }

    private void PlayChase()
    {
        _chaseStarted = _chaseWanted;
        if (_chaseWanted == PourTrack) PlayPour();
        else Play(_chaseWanted, loop: true, fadeSeconds: 1.2f);
    }

    /// <summary>The pour track from where its downbeat meets hammer 0's fall on the floor clock (every hammer falls a whole two beats apart).</summary>
    private void PlayPour()
    {
        var stream = Load(PourTrack, loop: false);
        if (stream == null || Main.Instance is not { } main) return;
        var from = RideMusic.PhaseSeek(main.Builder.FloorSeconds, SetPieceRules.HammerFallSeconds(0), SetPieceRules.HammerCycleSeconds,
            RideMusic.PourBeatOrigin, stream.GetLength(), RideMusic.PourPitch);
        Play(PourTrack, loop: false, fadeSeconds: 1.2f, from: (float)from, pitch: (float)RideMusic.PourPitch);
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

    /// <summary>Crossfade to <paramref name="file"/> from <paramref name="from"/> s at <paramref name="pitch"/> (every other track 1: the two players are reused).</summary>
    private void Play(string file, bool loop, float fadeSeconds, float from = 0, float pitch = 1f)
    {
        var stream = Load(file, loop);
        if (stream == null) return;
        var back = _front;
        _front = back == _a ? _b : _a;
        _front.Stream = stream;
        _front.VolumeDb = -80f;
        _front.PitchScale = pitch;
        _front.Play(from);
        _frontTarget = BaseDb;
        _backTarget = -80f;
        _fadeSpeed = Math.Max(4f, 75f / Math.Max(fadeSeconds, 0.1f));
        CurrentTrack = file;
    }

    private void OnFinished(AudioStreamPlayer p)
    {
        if (p != _front) return;
        if (Mood == Mood.Chase) { if (CurrentTrack == PourTrack) PlayPour(); return; } // locked again: the audio clock drifts from the floor's
        if (Mood != Mood.Explore) return;
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
