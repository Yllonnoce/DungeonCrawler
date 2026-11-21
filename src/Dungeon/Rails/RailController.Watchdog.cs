using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party cart's watchdog (<see cref="CartWatchdog"/>, plan 5 task 8): a cart that rolls, not halted, with nobody on the brake,
/// and has not moved half a metre in 10 s is stuck. It grinds free to its next stop as an arrival ("The cart grinds free."), never
/// across the jump gap: a next stop past an unjudged gap sets the cart at the gap's near lip at <see cref="SetPieceRules.JumpSpeed"/>
/// so the jump is judged as it would be, and never through an obstacle on the line: it is put at the obstacle's face and halts there.
/// Nor across the broken bridge (plan 11): a train stuck across on the slag line with its return open is set at a live switch back's
/// points it has not taken (the back is then taken as its lever stands), else at the gap, where it falls as it would have. Server and
/// solo; the window counts elapsed time, so any physics rate gives the same 10 s, from the cart's last half-metre. No allocation.
/// </summary>
public partial class RailController
{
    private CartWatchdog.Window _partyWatch;
    /// <summary>A seated player held the brake on the last <see cref="Inputs"/> (companions never brake).</summary>
    private bool _anyBrake;
    /// <summary>How often the party's cart has ground free (tests read it).</summary>
    public int GroundFree { get; private set; }

    /// <summary>Test hook: the cart rolls but goes nowhere (its S and V are put back after each step), as a cart caught on something would.</summary>
    internal bool DebugStall { get; set; }

    /// <summary>One rolling tick that ended neither in a wreck, a halt nor an arrival: watch the cart.</summary>
    private void Watch(float dt)
    {
        var watched = CartWatchdog.PartyWatched(State.Mode == CartMode.Rolling, Halted, _anyBrake);
        if (_partyWatch.Observe(dt, State.S, float.PositiveInfinity, watched)) GrindFree();
    }

    /// <summary>
    /// The cart is stuck (<see cref="CartWatchdog.PartyFreeTo(in CartState, float, float, float, float, float, out CartState)"/>): to the nearest
    /// of an unjudged gap's lip, an obstacle's face, a live switch back's points and the broken bridge's gap (across on the slag line) short of its
    /// next stop, else on to that stop as an arrival.
    /// </summary>
    private void GrindFree()
    {
        GroundFree++;
        DebugStall = false;
        var from = State.S;
        EventBus.Instance.Log("The cart grinds free.");
        var next = Party.Limit;
        // the party cart runs toward higher S (Dir +1): its front leads its centre by a half length
        var lip = GapSet && !_jumpJudged && next > _gapFrom ? _gapFrom - CartRules.HalfLength : float.NaN;
        var face = LineBlocked && !OnEnemyLane(BlockedAt) ? BlockedAt - CartRules.HalfLength : float.NaN;
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) && e.GetParent() == GetParent() ? e : null;
        var (backPoints, gap) = enc?.WatchdogBreak() ?? (float.NaN, float.NaN); // front S, NaN unless across toward the broken bridge
        var to = CartWatchdog.PartyFreeTo(State, next, lip, face, backPoints - CartRules.HalfLength, gap - CartRules.HalfLength, out var freed);
        Party.Reset(freed);
        Party.Limit = next;
        Moved();
        switch (to)
        {
            case FreeTo.GapLip: // the watchdog never carries a cart across the gap: the jump is judged now, at the jump's speed
                GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0}; set at the gap's lip (S={freed.S:0.0}) at {freed.V:0.0} m/s");
                if (JudgeJump()) DropIn(); // made at the jump's speed (JumpMade): this only wrecks if the rules change
                break;
            case FreeTo.ObstacleFace: // nor through an obstacle: it halts at the face (no crash, no dent)
                GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0}; set at the obstacle's face (S={freed.S:0.0})");
                Halt();
                break;
            case FreeTo.BackPoints: // nor past a live switch back it has not taken: the back is taken as its lever stands (the encounter's next tick)
                GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0} across on the slag line; set with its front at the live switch back's points (front S={freed.S + CartRules.HalfLength:0.0}), taken as its lever stands");
                break;
            case FreeTo.BreakLip: // nor across the broken bridge: still across there, it falls as it would have
                GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0} across on the slag line, its return open; ground on to the broken bridge's gap (front S={freed.S + CartRules.HalfLength:0.0}): the train falls");
                FallNow("The slag line ends at the broken bridge!");
                break;
            default:
                GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0}; it grinds on to stop {StopIndex + 1} (S={next:0.0})");
                SetCartLayer();
                Arrive();
                break;
        }
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
