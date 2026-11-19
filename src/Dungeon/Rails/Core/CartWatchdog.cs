namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The cart watchdog (plan 5 task 8): a cart that should be moving but has not moved <see cref="Threshold"/> metres in
/// <see cref="WindowSeconds"/> is stuck, and its owner frees it (the party's cart grinds on to its next stop, a slag, traffic or
/// runaway cart is freed). Engine-free: the callers keep one <see cref="Window"/> per cart and decide whether it is watched.
/// <list type="bullet">
/// <item>The party's cart is watched only while it rolls, not halted at an obstacle, with no seated rider (player or companion)
/// holding the brake (<see cref="PartyWatched"/>): a player who stops the cart on purpose is not stuck. Its target is
/// <see cref="float.PositiveInfinity"/>: stuck means it moved under the threshold.</item>
/// <item>A pacing slag cart is watched only while the party's cart rolls, not halted (<see cref="SlagWatched"/>), and judged against its
/// pace point: stuck when its target S moved at least the threshold while the cart moved less.</item>
/// <item>Traffic carts and runaway wagons are judged on their own progress (target +∞). The golem's runner is never watched.</item>
/// </list>
/// </summary>
/// <summary>Where the watchdog frees a stuck party cart to (<see cref="CartWatchdog.PartyFreeTo"/>).</summary>
public enum FreeTo : byte { NextStop, GapLip, ObstacleFace }

public static class CartWatchdog
{
    public const float WindowSeconds = 10f;
    /// <summary>Metres a watched cart must move in a window.</summary>
    public const float Threshold = 0.5f;

    /// <summary>
    /// Stuck: watched, the target moved at least <see cref="Threshold"/> over the window (+∞ for a cart judged on its own progress)
    /// and the cart moved less.
    /// </summary>
    public static bool Stuck(float progress10s, float targetProgress10s, bool watched)
        => watched && targetProgress10s >= Threshold && progress10s < Threshold;

    /// <summary>The party's cart is watched: Rolling, not Halted, and no seated rider brakes.</summary>
    public static bool PartyWatched(bool rolling, bool halted, bool anyRiderBrakes) => rolling && !halted && !anyRiderBrakes;

    /// <summary>A pacing slag cart is watched: the party's cart is Rolling and not Halted.</summary>
    public static bool SlagWatched(bool partyRolling, bool partyHalted) => partyRolling && !partyHalted;

    /// <summary>A freed cart's front is set this far past the gap's lip, so float rounding cannot leave it a hair short (the next step's drag would judge a slower jump).</summary>
    public const float LipNudge = 0.01f;

    /// <summary>
    /// Where a stuck party cart (centre <paramref name="cart"/>.S, running toward higher S) is freed to, and its state there. Every S is
    /// the cart's centre: <paramref name="nextStop"/> its next stop; <paramref name="gapLip"/> the centre with the front at an unjudged
    /// gap's near lip (NaN: no gap this side of the stop); <paramref name="obstacleFace"/> the centre with the front at an obstacle's
    /// face on the party lane (NaN: none). The nearer of the lip and the face ahead of the cart and short of the stop wins (the face on a
    /// tie): at the face the cart stands, its plating untouched (no crash, no dent; the caller halts it there); at the lip it rolls at
    /// <see cref="SetPieceRules.JumpSpeed"/> with its front <see cref="LipNudge"/> past the lip (the caller judges the jump on that tick);
    /// with neither it is Held at the stop, plating untouched (the caller makes it arrive). The watchdog never carries a cart across a
    /// gap or through an obstacle.
    /// </summary>
    public static FreeTo PartyFreeTo(in CartState cart, float nextStop, float gapLip, float obstacleFace, out CartState freed)
    {
        var lip = !float.IsNaN(gapLip) && gapLip > cart.S && gapLip < nextStop;
        var face = !float.IsNaN(obstacleFace) && obstacleFace > cart.S && obstacleFace < nextStop;
        if (face && (!lip || obstacleFace <= gapLip))
        {
            freed = new CartState(obstacleFace, 0f, CartMode.Rolling, cart.Plating);
            return FreeTo.ObstacleFace;
        }
        if (lip)
        {
            freed = new CartState(gapLip + LipNudge, SetPieceRules.JumpSpeed, CartMode.Rolling, cart.Plating);
            return FreeTo.GapLip;
        }
        freed = new CartState(nextStop, 0f, CartMode.Held, cart.Plating);
        return FreeTo.NextStop;
    }

    /// <summary>
    /// One cart's window: where it and its target stood when the window opened (or when the cart last made <see cref="Threshold"/> of
    /// progress) and how long it has run since. So "stuck" means no half-metre of progress in the last 10 s: a cart that sticks is freed
    /// 10 s after it last moved half a metre, whenever in a window that was. An unwatched tick closes it (the next watched one opens a
    /// fresh window), and a judged window starts over. A value type: no allocation.
    /// </summary>
    public struct Window
    {
        private bool _open;
        private float _elapsed, _s0, _target0;

        /// <summary>The window runs (it has seen a watched tick since it last closed).</summary>
        public readonly bool Open => _open;

        public void Reset() => _open = false;

        /// <summary>
        /// One tick of <paramref name="dt"/> seconds with the cart at <paramref name="s"/> and its target at <paramref name="target"/>
        /// (+∞: judged on its own progress). True when the window has run <see cref="WindowSeconds"/> and the cart is <see cref="Stuck"/>.
        /// </summary>
        public bool Observe(float dt, float s, float target, bool watched)
        {
            if (!watched) { _open = false; return false; }
            if (!_open)
            {
                _open = true;
                _elapsed = 0f;
                _s0 = s;
                _target0 = target;
                return false;
            }
            _elapsed += dt;
            if (MathF.Abs(s - _s0) >= Threshold) { _elapsed = 0f; _s0 = s; _target0 = target; return false; } // it made progress: the 10 s run from here
            if (_elapsed < WindowSeconds) return false;
            var progress = MathF.Abs(s - _s0);
            var targetProgress = float.IsInfinity(target) || float.IsInfinity(_target0) ? float.PositiveInfinity : MathF.Abs(target - _target0);
            _open = false;
            return Stuck(progress, targetProgress, true);
        }
    }
}
