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

    /// <summary>
    /// One cart's window: where it and its target stood when the window opened and how long it has run. An unwatched tick closes it
    /// (the next watched one opens a fresh window), and a judged window starts over. A value type: no allocation.
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
            if (_elapsed < WindowSeconds) return false;
            var progress = MathF.Abs(s - _s0);
            var targetProgress = float.IsInfinity(target) || float.IsInfinity(_target0) ? float.PositiveInfinity : MathF.Abs(target - _target0);
            _open = false;
            return Stuck(progress, targetProgress, true);
        }
    }
}
