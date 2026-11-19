using DungeonCrawler.Net;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>The rise over run of a track at an S (a client's <see cref="CartFollower"/> steps the same physics as the server: the slope is not on the wire).</summary>
public interface ISlopeSource
{
    /// <param name="track">0 the party line, 1 the enemy line, 2+k the k-th spur.</param>
    float SlopeAt(byte track, float s);
}

/// <summary>
/// A client's copy of one server cart, dead-reckoned. <see cref="Apply"/> takes the server's state and steps a copy of it with
/// <see cref="CartPhysics"/> from the state's server time to now (fixed 1/60 s steps, at most <see cref="MaxCatchUp"/>), on the
/// state's own input and Limit. <see cref="Advance"/> steps that authoritative copy on (through a <see cref="CartClock"/>) and
/// moves the rendered S toward it: at the copy's speed plus 2·error, the correction held within ±max(0.15·V, 0.5 m/s), never
/// against the way of travel while V ≥ 0.5 m/s (below it easing back at most 0.5 m/s). It snaps when the error passes
/// <see cref="SnapError"/>, when the state turns Held or Wrecked (Held to the state's own S: a halt sits short of any stop) and
/// when the track changes. Engine-free, no allocation.
/// </summary>
public sealed class CartFollower
{
    public const int MaxCatchUp = 30;
    public const float SnapError = 3f;
    public const float Gain = 2f;
    public const float CorrectShare = 0.15f;
    public const float MinCorrect = 0.5f;
    /// <summary>Below this speed the rendered S may ease back (at most this fast).</summary>
    public const float SlowSpeed = 0.5f;
    /// <summary>A reversing cart is stepped mirrored about this S (CartPhysics only runs forward): far past any floor's track.</summary>
    const float Mirror = 4096f;

    private readonly ISlopeSource? _slopes;
    private CartState _auth;
    private CartInput _input;
    private CartClock _clock;
    private int _dir = 1;
    private float _rendered;
    /// <summary>The authoritative copy's S now (its S plus its speed through the part step banked).</summary>
    private float _authNow;
    private double _lastTime = double.NegativeInfinity;
    private float _v;
    /// <summary>The cart's middle stops here while an obstacle stands ahead on its lane (its face minus a half length in Dir), or NaN.</summary>
    private float _block = float.NaN;

    public CartFollower(ISlopeSource? slopes) => _slopes = slopes;

    /// <summary>A state has been applied (until then S is meaningless).</summary>
    public bool HasState { get; private set; }
    /// <summary>The rendered S (the last <see cref="Advance"/>'s, or the snap's).</summary>
    public float S => _rendered;
    /// <summary>The rendered speed along the way of travel (0 while not Rolling).</summary>
    public float V => _v;
    /// <summary>The authoritative copy's mode (a predicted arrival turns it Held).</summary>
    public CartMode Mode => _auth.Mode;
    public byte Track { get; private set; }
    public int Dir => _dir;
    /// <summary>The authoritative copy's S now (tests, the RIDE pass's trace, and a seated headset's comfort inputs).</summary>
    public float AuthS => _authNow;
    /// <summary>The authoritative copy's speed along the way of travel (0 while not Rolling): the ride without the rendered corrections.</summary>
    public float AuthV => _auth.Mode == CartMode.Rolling ? _auth.V : 0f;
    /// <summary>How many times the rendered S has jumped (a first state, a track change, an error past <see cref="SnapError"/>, a Held or Wrecked state): a reader that saw it change knows the pose jumped.</summary>
    public int Snaps { get; private set; }
    /// <summary>The integrator's StopAt (the DTO's Limit).</summary>
    public float Limit => _input.StopAt;
    public float Plating => _auth.Plating;

    /// <summary>Forget everything: the next <see cref="Apply"/> snaps.</summary>
    public void Reset()
    {
        HasState = false;
        _lastTime = double.NegativeInfinity;
        _clock.Reset();
        _v = 0f;
    }

    /// <summary>The server's state <paramref name="s"/>, applied at <paramref name="serverNow"/> (the client's estimate of the server's clock).</summary>
    public void Apply(in CartStateDto s, double serverNow)
    {
        var trackChanged = HasState && s.Track != Track;
        if (HasState && !trackChanged && s.ServerTime < _lastTime) return; // an older state than one already applied
        _lastTime = s.ServerTime;
        var mode = s.Mode <= (byte)CartMode.Wrecked ? (CartMode)s.Mode : CartMode.Held;
        _dir = s.Dir >= 0 ? 1 : -1;
        Track = s.Track;
        _auth = new CartState(s.S, s.V, mode, s.Plating);
        _input = new CartInput(0f, s.Brake, s.Pumpers, s.CrewWagons, s.Limit, s.TargetOverride);
        // an obstacle ahead (the heap's face): the server's cart crashes there, short of the Limit it brakes for, so both S stop
        // at it (the physics input keeps the Limit: braking toward the face would mispredict the run-in); one already past it is free
        var block = float.IsNaN(s.BlockedAt) ? float.NaN : s.BlockedAt - _dir * CartRules.HalfLength;
        _block = !float.IsNaN(block) && _dir * (s.S - block) <= 0f ? block : float.NaN;

        // catch up from the state's time to now, in whole fixed steps; the part step left is banked
        var elapsed = serverNow - s.ServerTime;
        var banked = 0f;
        if (elapsed > 0 && mode == CartMode.Rolling)
        {
            var n = (int)(elapsed / CartClock.FixedStep + 1e-4);
            if (n >= MaxCatchUp) n = MaxCatchUp;
            else banked = (float)(elapsed - n * (double)CartClock.FixedStep);
            for (var i = 0; i < n; i++) StepAuth();
        }
        _clock.Reset(banked);
        _authNow = AuthNow();

        var snap = !HasState || trackChanged || mode != CartMode.Rolling || Math.Abs(_authNow - _rendered) > SnapError;
        HasState = true;
        if (!snap) return;
        var to = mode == CartMode.Rolling ? _authNow : s.S; // Held sits where the server says (a halt short of any stop)
        if (to != _rendered || Snaps == 0) Snaps++;
        _rendered = to;
        _v = mode == CartMode.Rolling ? _auth.V : 0f;
    }

    /// <summary>Move on by <paramref name="dt"/> seconds (a rendered frame): the authoritative copy steps on, the rendered S follows it. Returns the rendered S.</summary>
    public float Advance(float dt)
    {
        if (!HasState) return _rendered;
        var error = _dir * (_authNow - _rendered); // along the way of travel, both as of the frame before
        if (_auth.Mode == CartMode.Rolling)
        {
            var steps = _clock.Advance(dt);
            for (var i = 0; i < steps; i++) StepAuth();
        }
        _authNow = AuthNow();
        if (_auth.Mode == CartMode.Wrecked)
        {
            if (Math.Abs(_authNow - _rendered) > 1e-3f) Snaps++;
            _rendered = _authNow; _v = 0f; return _rendered;
        }

        var speed = _auth.Mode == CartMode.Rolling ? _auth.V : 0f;
        var limit = Math.Max(CorrectShare * speed, MinCorrect);
        var v = speed + Math.Clamp(Gain * error, -limit, limit);
        v = speed >= SlowSpeed ? Math.Max(v, 0f) : Math.Max(v, -SlowSpeed);
        _rendered += _dir * v * dt;
        if (Math.Abs(_authNow - _rendered) > SnapError) { _rendered = _authNow; Snaps++; }
        if (_dir * (_rendered - _input.StopAt) > 0f) _rendered = _input.StopAt; // never past the stop
        if (!float.IsNaN(_block) && _dir * (_rendered - _block) > 0f) { _rendered = _block; v = 0f; } // nor into the obstacle
        _v = v;
        return _rendered;
    }

    /// <summary>One fixed step of the authoritative copy (a reversing cart mirrored, as CartRun does), clamped to the Limit.</summary>
    private void StepAuth()
    {
        if (_auth.Mode != CartMode.Rolling) return;
        var slope = _slopes != null ? _dir * _slopes.SlopeAt(Track, _auth.S) : 0f;
        if (_dir >= 0)
            _auth = CartPhysics.Step(_auth, _input with { Slope = slope }, CartClock.FixedStep);
        else
        {
            var mirrored = CartPhysics.Step(_auth with { S = Mirror - _auth.S }, _input with { Slope = slope, StopAt = Mirror - _input.StopAt }, CartClock.FixedStep);
            _auth = mirrored with { S = Mirror - mirrored.S };
        }
        if (_dir * (_auth.S - _input.StopAt) > 0f) _auth = _auth with { S = _input.StopAt };
        if (!float.IsNaN(_block) && _dir * (_auth.S - _block) > 0f) _auth = _auth with { S = _block, V = 0f }; // it ran into the face: it stands there until the server says
    }

    /// <summary>The authoritative S now: its last step's S carried on by its speed through the banked part step, short of the Limit.</summary>
    private float AuthNow()
    {
        if (_auth.Mode != CartMode.Rolling) return _auth.S;
        var s = _auth.S + _dir * _auth.V * _clock.Accumulated;
        if (_dir * (s - _input.StopAt) > 0f) s = _input.StopAt;
        return !float.IsNaN(_block) && _dir * (s - _block) > 0f ? _block : s;
    }
}
