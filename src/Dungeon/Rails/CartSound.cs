using DungeonCrawler.Audio;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// One cart's sound, a child of its body (the party's cart, a spare, a chaser, a slag or traffic cart, a runaway wagon; the golem's
/// runner with <see cref="Wheels"/> off). Its owner feeds it the cart's state once per rendered frame (<see cref="Feed"/>), on every peer
/// from what that peer shows: the server's and single player's own runs, a client's followers. It keeps five pooled players on the SFX bus
/// (the roll and screech loops, the clack, one for the edges and one for the bell, so a hit never cuts a warning off) and a wheel-spark
/// emitter, and follows
/// <see cref="RideSound"/>'s rules: the roll's volume and pitch by speed, the screech on a bend or under hard braking, a clack at every
/// joint, the bell for a body ahead, and one edge cue a feed (a crash, a jump, a landing, a hit, an arrival, a track change or a thrown
/// switch taken). The first feed, and the first after <see cref="Prime"/> (a restore, a client's follower snap), only primes: a quiet
/// path plays no edge. Beyond <see cref="RideSound.Audible"/> of the active camera, wrecked or hidden, it is silent; a dedicated server
/// builds nothing and plays nothing. No allocation per frame.
/// </summary>
public partial class CartSound : Node3D
{
    /// <summary>A cart on wheels (false: the golem's runner, whose only sound is a track change).</summary>
    public bool Wheels { get; init; } = true;
    /// <summary>The cart rings its bell once on the first feed that sees it rolling, priming or not (traffic, a runaway, a chaser). A later <see cref="Prime"/> does not re-arm it.</summary>
    public bool AnnounceOnStart { get; set; }
    /// <summary>The roll loop's volume and pitch as last set (test hooks: a headless run's dummy driver plays nothing).</summary>
    public float RollDbNow { get; private set; } = -80f;
    public float RollPitchNow { get; private set; } = RideSound.RollPitch(0f);
    /// <summary>Every one-shot a cart's sound plays (a clack, a bell, an edge cue): its cue and where. A test hook.</summary>
    public static event Action<string, Vector3>? Played;

    const float SquealFloorDb = -14f;
    const float ClackOverRoll = 6f;
    /// <summary>The braking rate is smoothed over about this long (V changes per physics tick, frames come between).</summary>
    const float DvdtSmoothing = 0.12f;

    private AudioStreamPlayer3D? _roll, _screech, _clack, _event, _bell;
    private CpuParticles3D? _sparks;
    private bool _silent;
    private CartSoundInput _prev;
    private bool _primed, _announced;
    private double _lastRing = double.NegativeInfinity;
    private float _clackClock, _lastV, _dvdt;

    public CartSound() => Name = "CartSound";

    public override void _Ready()
    {
        if (NetSession.IsServer) { _silent = true; return; } // a dedicated server does no sound work
        _event = Player("Event");
        _bell = Player("Bell");
        if (!Wheels) return;
        _roll = Player("Roll");
        _screech = Player("Screech");
        _clack = Player("Clack");
        _sparks = new CpuParticles3D
        {
            Name = "WheelSparks", Emitting = false, Amount = 24, Lifetime = 0.35, Position = new Vector3(0, 0.3f, 0),
            Direction = Vector3.Up + Vector3.Back, Spread = 40f, InitialVelocityMin = 1.5f, InitialVelocityMax = 3.5f, Gravity = new Vector3(0, -9f, 0),
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.45f, 0.05f, CartRules.HalfLength * 0.6f),
            Mesh = new QuadMesh { Size = new Vector2(0.04f, 0.04f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.7f, 0.25f), EmissionEnabled = true, Emission = new Color(1f, 0.55f, 0.15f), EmissionEnergyMultiplier = 3f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            },
        };
        AddChild(_sparks);
    }

    private AudioStreamPlayer3D Player(string name)
    {
        var p = new AudioStreamPlayer3D { Name = name, Bus = "SFX", MaxDistance = RideSound.Audible, UnitSize = 6f };
        AddChild(p);
        return p;
    }

    /// <summary>The next feed only primes (no edge cue): the cart's state jumped by a path that makes no sound.</summary>
    public void Prime() => _primed = false;

    /// <summary>
    /// A client's follower snapped and the cart is now at <paramref name="s"/> on <paramref name="track"/>: a snap that moves it more than
    /// a metre from the last feed, or onto another track, primes (it would fake an edge); a small one (the server's Held state settling an
    /// arrival) does not.
    /// </summary>
    public void Snapped(float s, byte track)
    {
        if (_primed && (Mathf.Abs(s - _prev.S) > 1f || track != _prev.Track)) Prime();
    }

    /// <summary>The bell, once, at the cart now (a chaser placed beside a body: the one Held ring).</summary>
    public void RingNow()
    {
        _lastRing = Now;
        if (Hearable()) OneShot(_bell, "cart_bell");
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>
    /// The cart's state this rendered frame (<paramref name="wagons"/> coupled behind it; <paramref name="anyoneAhead"/> asked only when the
    /// bell could ring: a delegate the owner built once). Plays the loops, the clacks, the bell and one edge cue.
    /// </summary>
    public void Feed(in CartSoundInput s, int wagons, Func<bool>? anyoneAhead)
    {
        var priming = !_primed;
        _primed = true;
        var prev = _prev;
        _prev = s;
        if (_silent) return;
        var dt = (float)GetProcessDeltaTime();
        if (dt > 0f)
        {
            var raw = (s.V - _lastV) / dt;
            _dvdt += (raw - _dvdt) * (1f - Mathf.Exp(-dt / DvdtSmoothing));
        }
        _lastV = s.V;
        var rolling = s.Mode == CartMode.Rolling;
        var hearable = s.Mode != CartMode.Wrecked && Hearable();
        if (!hearable)
        {
            StopLoops();
            _clackClock = 0f;
            if (AnnounceOnStart && rolling) _announced = true; // the start happened out of earshot: it is not rung later
            return;
        }
        if (AnnounceOnStart && !_announced && rolling)
        {
            _announced = true;
            RingNow();
        }
        if (!priming && RideSound.Loudest(RideSound.Edges(prev, s)) is var cue and not CartCue.None) PlayEdge(cue);
        if (!Wheels) return;

        var v = rolling ? s.V : 0f;
        RollDbNow = RideSound.RollDb(v, wagons);
        RollPitchNow = RideSound.RollPitch(v);
        Loop(_roll, "cart_roll", RollDbNow, RollPitchNow);
        var curvature = GetParent() is ICartBody body ? body.Curvature : 0f;
        var screech = RideSound.ScreechDb(curvature, v);
        if (rolling && RideSound.Squeals(_dvdt, v)) screech = Mathf.Max(screech, SquealFloorDb);
        Loop(_screech, "rail_screech", screech, 1f);
        if (_sparks != null && _sparks.Emitting != screech > -80f) _sparks.Emitting = screech > -80f;

        var interval = RideSound.ClackInterval(v);
        if (float.IsPositiveInfinity(interval)) _clackClock = 0f;
        else if ((_clackClock += dt) >= interval)
        {
            _clackClock = Mathf.Min(_clackClock - interval, interval); // a long frame drops the clacks it missed rather than rattle them off
            OneShot(_clack, "rail_clack", Mathf.Min(0f, RollDbNow + ClackOverRoll), RollPitchNow);
        }

        if (rolling) OfferSing(s);
        if (rolling && s.V >= 0.5f && Now - _lastRing >= RideSound.BellEvery && anyoneAhead != null
            && RideSound.BellDue(rolling, s.V, anyoneAhead(), Now, _lastRing)) RingNow();
    }

    /// <summary>The soundscape's rail song: how soon this rolling cart reaches the local hero on foot on its lane (its track as the wire numbers it).</summary>
    private static void OfferSing(in CartSoundInput s)
    {
        if (RailController.Instance is not { } rails || rails.Soundscape is not { } scape || rails.TrackOf(s.Track) is not { Valid: true } track) return;
        if (Core.Main.Instance?.Party?.Local is not { Seated: false, Downed: false } me) return;
        var seconds = RailWarn.SecondsTo(track, s.S, s.V, me.GlobalPosition);
        if (seconds < float.PositiveInfinity) scape.Offer(seconds);
    }

    private CartRun? _run;
    private Func<bool>? _runAhead;

    /// <summary>
    /// A cart that runs on its own (a slag or traffic cart, a runaway, a client's chaser): fed from <paramref name="run"/>'s track at
    /// <paramref name="s"/>, its bell looking ahead on that track for the heroes on foot (<see cref="RailController.FootHeroes"/>).
    /// </summary>
    public void FeedRun(CartRun run, float s, float v, CartMode mode, float plating, byte track)
    {
        _run = run;
        Feed(new CartSoundInput(s, mode == CartMode.Rolling ? v : 0f, mode, plating, track, false, false, false), 0, _runAhead ??= RunAhead);
    }

    private bool RunAhead() => _run is { } r && RailController.Instance is { } rails && RailWarn.AnyoneAhead(r.Track, _prev.S, _prev.V, rails.FootHeroes);

    /// <summary>The golem's runner: its track only (1 the enemy line, 2+k the k-th spur, as the wire numbers it): a change is the points' clunk.</summary>
    public void FeedTrack(byte track) => Feed(new CartSoundInput(0f, 0f, CartMode.Rolling, CartRules.MaxPlating, track, false, false, false), 0, null);

    /// <summary>Not wrecked out of sight: the cart is shown and within <see cref="RideSound.Audible"/> of the active camera.</summary>
    private bool Hearable()
    {
        if (_silent || !IsInsideTree() || !IsVisibleInTree()) return false;
        if (GetViewport()?.GetCamera3D() is not { } camera) return true;
        return camera.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= RideSound.Audible * RideSound.Audible;
    }

    private void PlayEdge(CartCue cue)
    {
        var name = cue switch
        {
            CartCue.Crash => "cart_crash",
            CartCue.Jump => "cart_jump",
            CartCue.Land => "cart_land",
            CartCue.Hit => GolemNear(GlobalPosition) ? "golem_slam" : "cart_hit",
            CartCue.Arrive => "brake_squeal",
            CartCue.TrackChange or CartCue.Cross => "switch_clunk",
            _ => null,
        };
        if (name != null) OneShot(_event, name);
    }

    /// <summary>The kiln golem stands within its reach (and a metre) of <paramref name="at"/>: the server's or single player's golem, a client's golem runner.</summary>
    private static bool GolemNear(Vector3 at)
    {
        const float reach = RailEncounterController.GolemReach + 1f;
        Node3D? golem = NetSession.IsClient ? RailNet.Instance?.ClientGolem : RailEncounterController.Instance?.GolemBody;
        return golem != null && IsInstanceValid(golem) && golem.GlobalPosition.DistanceSquaredTo(at) <= reach * reach;
    }

    private void OneShot(AudioStreamPlayer3D? player, string cue, float db = 0f, float pitch = 1f)
    {
        if (player == null || AudioManager.Instance?.Stream(cue) is not { } stream) return;
        player.Stream = stream;
        player.VolumeDb = db;
        player.PitchScale = pitch;
        player.Play();
        Played?.Invoke(cue, GlobalPosition);
    }

    /// <summary>A loop on <paramref name="player"/> at <paramref name="db"/> (stopped at −80) and <paramref name="pitch"/>; its clip comes from Stream() once.</summary>
    private static void Loop(AudioStreamPlayer3D? player, string cue, float db, float pitch)
    {
        if (player == null) return;
        if (db <= -80f)
        {
            if (player.Playing) player.Stop();
            return;
        }
        player.VolumeDb = db;
        player.PitchScale = pitch;
        if (player.Playing) return;
        player.Stream ??= AudioManager.Instance?.Stream(cue);
        if (player.Stream != null) player.Play();
    }

    private void StopLoops()
    {
        RollDbNow = -80f;
        if (_roll is { Playing: true }) _roll.Stop();
        if (_screech is { Playing: true }) _screech.Stop();
        if (_sparks != null) _sparks.Emitting = false;
    }
}
