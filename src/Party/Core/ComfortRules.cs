namespace DungeonCrawler.Party;

/// <summary>How much a headset's view is narrowed while a cart carries it (the <c>VrComfort</c> setting).</summary>
public enum ComfortLevel
{
    /// <summary>No vignette and no fade.</summary>
    Off,
    /// <summary>The default: the view narrows with acceleration and turning, never past <see cref="ComfortRules.RecommendedFloor"/>.</summary>
    Recommended,
    /// <summary>Narrower (<see cref="ComfortRules.StrongFloor"/>), and the view dims briefly on a big drop.</summary>
    Strong,
}

/// <summary>
/// The comfort vignette's state: <see cref="Radius"/> 1 is fully open, smaller closes the view toward its centre;
/// <see cref="Fade"/> (0..1) dims the whole view (Strong's drop fade); <see cref="FadeLeft"/> is how long the fade still holds.
/// </summary>
public readonly record struct ComfortState(float Radius, float Fade, float FadeLeft)
{
    public static ComfortState Open => new(1f, 0f, 0f);
}

/// <summary>
/// The comfort vignette a headset rider sees (Meta's comfort guidance): the view narrows as the cart speeds up, slows
/// down or turns, and opens again when the ride is steady; Strong also dims the view for a moment on a big drop. The
/// vignette is never fully black. Engine-free; <see cref="ComfortSampler"/> turns the seat's motion into the inputs.
/// </summary>
public static class ComfortRules
{
    public const ComfortLevel Default = ComfortLevel.Recommended;
    /// <summary>The narrowest the view gets at each level (the radius at full input).</summary>
    public const float RecommendedFloor = 0.55f, StrongFloor = 0.35f;
    /// <summary>The acceleration ramp (m/s², either sign): no narrowing below the first, full at the second.</summary>
    public const float AccelFrom = 1.5f, AccelTo = 4f;
    /// <summary>The turn-rate ramp (degrees a second, either way).</summary>
    public const float YawFrom = 30f, YawTo = 90f;
    /// <summary>The radius eases toward its target with this time constant (seconds).</summary>
    public const float RadiusTimeConstant = 0.25f;
    /// <summary>A vertical acceleration below this (m/s², downward) is a big drop: Strong dims the view.</summary>
    public const float DropAccel = -4f;
    /// <summary>The drop fade holds at <see cref="FadePeak"/> this long, then eases out with <see cref="FadeTimeConstant"/>.</summary>
    public const float FadeHold = 0.4f, FadePeak = 0.6f, FadeTimeConstant = 0.1f;

    /// <summary>0 at or below <paramref name="a"/>, 1 at or above <paramref name="b"/>, linear between.</summary>
    public static float Ramp(float x, float a, float b) => Math.Clamp((x - a) / (b - a), 0f, 1f);

    /// <summary>The narrowest the view may get at <paramref name="level"/> (1 for Off).</summary>
    public static float Floor(ComfortLevel level) => level switch
    {
        ComfortLevel.Strong => StrongFloor,
        ComfortLevel.Recommended => RecommendedFloor,
        _ => 1f,
    };

    /// <summary>The radius the vignette eases toward for this acceleration (m/s²) and turn rate (degrees a second).</summary>
    public static float TargetRadius(float accel, float yawRateDeg, ComfortLevel level)
    {
        if (level == ComfortLevel.Off) return 1f;
        var t = Math.Max(Ramp(Math.Abs(accel), AccelFrom, AccelTo), Ramp(Math.Abs(yawRateDeg), YawFrom, YawTo));
        return 1f - t * (1f - Floor(level));
    }

    /// <summary>
    /// One rendered frame of the vignette. Off opens it at once. The radius eases toward <see cref="TargetRadius"/>; in Strong a
    /// vertical acceleration below <see cref="DropAccel"/> holds the fade at <see cref="FadePeak"/> for <see cref="FadeHold"/>, after
    /// which it eases to nothing. Any other level lets a fade in progress go.
    /// </summary>
    public static ComfortState Step(ComfortState s, float dt, float accel, float yawRateDeg, float verticalAccel, ComfortLevel level)
    {
        if (level == ComfortLevel.Off) return ComfortState.Open;
        dt = Math.Max(dt, 0f);
        var target = TargetRadius(accel, yawRateDeg, level);
        var radius = s.Radius + (target - s.Radius) * (1f - MathF.Exp(-dt / RadiusTimeConstant));
        radius = Math.Clamp(radius, Floor(level), 1f); // a level just made gentler never shows a tighter view than it allows
        var fadeLeft = level == ComfortLevel.Strong ? s.FadeLeft : 0f;
        if (level == ComfortLevel.Strong && verticalAccel < DropAccel) fadeLeft = FadeHold;
        float fade;
        if (fadeLeft > 0f)
        {
            fade = FadePeak;
            fadeLeft = Math.Max(0f, fadeLeft - dt);
        }
        else
        {
            fade = s.Fade * MathF.Exp(-dt / FadeTimeConstant);
            if (fade < 1e-4f) fade = 0f;
        }
        return new ComfortState(radius, fade, fadeLeft);
    }

    /// <summary>The level's name in settings.cfg.</summary>
    public static string Name(ComfortLevel level) => level switch
    {
        ComfortLevel.Off => "off",
        ComfortLevel.Strong => "strong",
        _ => "recommended",
    };

    /// <summary>A level from settings.cfg; anything unknown is <see cref="Default"/>.</summary>
    public static ComfortLevel Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "off" => ComfortLevel.Off,
        "strong" => ComfortLevel.Strong,
        _ => Default,
    };
}

/// <summary>
/// The comfort inputs from a seat's motion, sampled each rendered frame while a headset rides (Adventurer.FollowSeat):
/// <see cref="Accel"/> is the change of the cart's speed (m/s², smoothed over 0.1 s), <see cref="YawRateDeg"/> the seat
/// heading's turn (degrees a second, either way) and <see cref="VerticalAccel"/> the seat height's second difference,
/// smoothed over 0.1 s like the acceleration so frame-to-frame jitter at 90-120 Hz cannot pass for a drop. A second call in
/// the same frame is ignored; a gap (a menu, a load) or a clock that went back starts a fresh baseline. Allocation-free.
/// </summary>
public sealed class ComfortSampler
{
    /// <summary>The smoothing time constant of the two accelerations (seconds).</summary>
    public const float SmoothSeconds = 0.1f;
    /// <summary>Calls closer together than this are one frame's; further apart than <see cref="MaxGap"/> start over.</summary>
    public const double MinStep = 0.002, MaxGap = 0.25;

    private int _samples;
    private double _at;
    private float _speed, _heading, _y, _vy;

    public float Accel { get; private set; }
    public float YawRateDeg { get; private set; }
    public float VerticalAccel { get; private set; }

    /// <summary>Forget the ride: the next sample is a baseline and every input reads calm.</summary>
    public void Reset()
    {
        _samples = 0;
        Accel = YawRateDeg = VerticalAccel = 0f;
    }

    /// <summary>The seat at time <paramref name="now"/> (seconds): the cart's speed, the seat's flat heading (radians) and its height. False when ignored (the same frame).</summary>
    public bool Sample(double now, float speed, float heading, float y)
    {
        if (_samples > 0)
        {
            var gap = now - _at;
            if (gap < 0 || gap > MaxGap) Reset(); // the clock went back (the physics rate changed) or the ride was away a while
            else if (gap < MinStep) return false;
        }
        if (_samples == 0)
        {
            (_at, _speed, _heading, _y, _vy) = (now, speed, heading, y, 0f);
            _samples = 1;
            return true;
        }
        var dt = (float)(now - _at);
        var k = 1f - MathF.Exp(-dt / SmoothSeconds);
        var vy = (y - _y) / dt;
        Accel += ((speed - _speed) / dt - Accel) * k;
        YawRateDeg = MathF.Abs(Wrap(heading - _heading)) * (180f / MathF.PI) / dt;
        if (_samples >= 2) VerticalAccel += ((vy - _vy) / dt - VerticalAccel) * k; // the first velocity has nothing to difference against
        (_at, _speed, _heading, _y, _vy) = (now, speed, heading, y, vy);
        if (_samples < 2) _samples++;
        return true;
    }

    /// <summary>An angle wrapped into −π..π.</summary>
    private static float Wrap(float a)
    {
        a %= 2f * MathF.PI;
        if (a > MathF.PI) a -= 2f * MathF.PI;
        else if (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }
}

/// <summary>
/// The right stick's snap turn (VrRig.Read, every physics tick, seated or on foot): pushed past <see cref="Push"/> it turns the
/// room <see cref="Degrees"/> once; it must come back inside <see cref="Release"/> before it turns again. Engine-free.
/// </summary>
public static class SnapTurn
{
    /// <summary>A flick turns the view this far at once (a smooth turn is what makes people ill).</summary>
    public const float Degrees = 30f;
    /// <summary>How far the stick must be pushed to turn, and how far it must come back before it turns again.</summary>
    public const float Push = 0.6f, Release = 0.3f;

    /// <summary>The turn this tick in radians (positive turns left, the body's measure of yaw; a stick pushed right turns right), or 0.</summary>
    public static float Read(ref bool armed, float stickX)
    {
        if (MathF.Abs(stickX) < Release) { armed = true; return 0f; }
        if (!armed || MathF.Abs(stickX) <= Push) return 0f;
        armed = false;
        return -MathF.Sign(stickX) * Degrees * (MathF.PI / 180f);
    }
}
