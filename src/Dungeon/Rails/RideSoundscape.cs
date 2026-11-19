using DungeonCrawler.Audio;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The railed floor's bed of sound, a child of the <see cref="RailController"/> on every role but a dedicated server: the foundry's
/// rumble as a quiet loop the whole time; the rails singing under the local hero's feet, louder the sooner a rolling cart reaches them on
/// its lane (<see cref="RideSound.SingDb"/>; each fed cart offers its seconds, <see cref="Offer"/>, and the fewest win); and now and then
/// a timber frame near the camera creaking (presentation only: unseeded, an ambient play the tests ignore). Its loops are its own players,
/// their clips from <see cref="AudioManager.Stream"/>. No allocation per frame.
/// </summary>
public partial class RideSoundscape : Node
{
    public const float RumbleDb = -22f;
    const float CreakEveryMin = 9f, CreakEveryMax = 16f, CreakNear = 15f;

    private AudioStreamPlayer? _rumble, _sing;
    private float _fewest = float.PositiveInfinity;
    private double _creakIn;
    private readonly Random _rng = new(); // presentation only: unseeded, nothing here is game state

    /// <summary>The rail song's volume as last set (a test hook).</summary>
    public float SingDbNow { get; private set; } = -80f;

    public RideSoundscape() => Name = "RideSoundscape";

    public override void _Ready()
    {
        if (NetSession.IsServer) return;
        _rumble = new AudioStreamPlayer { Name = "Rumble", Bus = "SFX", VolumeDb = RumbleDb, Stream = AudioManager.Instance?.Stream("foundry_rumble") };
        AddChild(_rumble);
        if (_rumble.Stream != null) _rumble.Play();
        _sing = new AudioStreamPlayer { Name = "Sing", Bus = "SFX", VolumeDb = -80f, Stream = AudioManager.Instance?.Stream("rail_sing") };
        AddChild(_sing);
        _creakIn = NextCreak();
    }

    /// <summary>A rolling cart reaches the local hero on foot in <paramref name="seconds"/> (this frame): the fewest sets the song.</summary>
    public void Offer(float seconds)
    {
        if (seconds < _fewest) _fewest = seconds;
    }

    private double NextCreak() => CreakEveryMin + _rng.NextDouble() * (CreakEveryMax - CreakEveryMin);

    public override void _Process(double delta)
    {
        if (_sing == null) return;
        SingDbNow = RideSound.SingDb(_fewest);
        _fewest = float.PositiveInfinity; // the carts offer again as they are fed
        if (SingDbNow <= -80f) { if (_sing.Playing) _sing.Stop(); }
        else
        {
            _sing.VolumeDb = SingDbNow;
            if (!_sing.Playing && _sing.Stream != null) _sing.Play();
        }
        if ((_creakIn -= delta) > 0) return;
        _creakIn = NextCreak();
        Creak();
    }

    /// <summary>A timber frame within <see cref="CreakNear"/> of the camera, picked at random, creaks (ambient: the tests ignore it).</summary>
    private void Creak()
    {
        if (GetViewport()?.GetCamera3D() is not { } camera || Main.Instance?.Builder.FloorRoot?.GetNodeOrNull<Node3D>("Rails/Timbering") is not { } frames) return;
        var eye = camera.GlobalPosition;
        Node3D? pick = null;
        var seen = 0;
        for (var i = 0; i < frames.GetChildCount(); i++)
        {
            if (frames.GetChild(i) is not Node3D timber || timber.GlobalPosition.DistanceSquaredTo(eye) > CreakNear * CreakNear) continue;
            if (_rng.Next(++seen) == 0) pick = timber; // one of the near ones, each as likely
        }
        if (pick != null) RideAudio.At("trestle_creak", pick.GlobalPosition, -6f, CreakNear + 5f, ambient: true);
    }
}
