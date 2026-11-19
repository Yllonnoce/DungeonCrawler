using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party cart's watchdog (<see cref="CartWatchdog"/>, plan 5 task 8): a cart that rolls, not halted, with nobody on the brake,
/// and has not moved half a metre in 10 s is stuck. It grinds free to its next stop as an arrival ("The cart grinds free."), never
/// across the jump gap: a next stop past an unjudged gap sets the cart at the gap's near lip at <see cref="SetPieceRules.JumpSpeed"/>
/// so the jump is judged as it would be, and never through an obstacle on the line: it is put at the obstacle's face and halts there.
/// Server and solo; the window counts elapsed time, so any physics rate gives the same 10 s. No allocation.
/// </summary>
public partial class RailController
{
    private CartWatchdog.Window _partyWatch;
    /// <summary>A seated player held the brake on the last <see cref="Inputs"/> (companions never brake).</summary>
    private bool _anyBrake;
    /// <summary>How often the party's cart has ground free (tests read it).</summary>
    public int GroundFree { get; private set; }

    /// <summary>The front is set this far past the lip, so float rounding cannot leave it a hair short (the next step's drag would judge a slower jump).</summary>
    private const float LipNudge = 0.01f;

    /// <summary>Test hook: the cart rolls but goes nowhere (its S and V are put back after each step), as a cart caught on something would.</summary>
    internal bool DebugStall { get; set; }

    /// <summary>One rolling tick that ended neither in a wreck, a halt nor an arrival: watch the cart.</summary>
    private void Watch(float dt)
    {
        var watched = CartWatchdog.PartyWatched(State.Mode == CartMode.Rolling, Halted, _anyBrake);
        if (_partyWatch.Observe(dt, State.S, float.PositiveInfinity, watched)) GrindFree();
    }

    /// <summary>The cart is stuck: on to its next stop as an arrival, or to the gap's near lip at the jump's speed, or to an obstacle's face.</summary>
    private void GrindFree()
    {
        GroundFree++;
        DebugStall = false;
        var from = State.S;
        EventBus.Instance.Log("The cart grinds free.");
        var next = Party.Limit;
        if (GapSet && !_jumpJudged && Party.FrontS < _gapFrom && next > _gapFrom)
        {
            // the watchdog never carries a cart across the gap: its front at the lip, at the jump's speed, and the jump judged now
            var lip = _gapFrom - Party.Dir * CartRules.HalfLength + Party.Dir * LipNudge; // the front just at the lip: the jump is judged on this tick, at this speed
            Party.Reset(new CartState(lip, SetPieceRules.JumpSpeed, CartMode.Rolling, State.Plating));
            Party.Limit = next;
            GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0}; set at the gap's lip (S={lip:0.0}) at {SetPieceRules.JumpSpeed:0.0} m/s");
            Moved();
            if (JudgeJump()) Wreck(); // made at the jump's speed (JumpMade): this only wrecks if the rules change
            return;
        }
        if (LineBlocked && !OnEnemyLane(BlockedAt) && BlockedAt - CartRules.HalfLength > from && BlockedAt - CartRules.HalfLength < next)
        {
            // an obstacle stands between it and the stop: it grinds up to the face and halts there (no crash, no dent)
            var face = BlockedAt - CartRules.HalfLength;
            Party.Reset(new CartState(face, 0f, CartMode.Rolling, State.Plating));
            GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0}; set at the obstacle's face (S={face:0.0})");
            Moved();
            Halt();
            return;
        }
        Party.Reset(new CartState(next, 0f, CartMode.Held, State.Plating));
        GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0}; it grinds on to stop {StopIndex + 1} (S={next:0.0})");
        Moved();
        SetCartLayer();
        Arrive();
    }

    /// <summary>The cart was moved by the watchdog: posed there at once, its train behind it, and the window starts over.</summary>
    private void Moved()
    {
        _partyWatch.Reset();
        Party.Sync();
        Party.PoseAt(State.S);
        _train.Pose(State.S);
    }
}
