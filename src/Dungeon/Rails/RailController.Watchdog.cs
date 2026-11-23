using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party cart's watchdog (<see cref="CartWatchdog"/>, plan 5 task 8): a cart that rolls, not halted, with nobody on the brake,
/// and has not moved half a metre in 10 s is stuck. It grinds free to its next stop as an arrival ("The cart grinds free."), never
/// across the jump gap: a next stop past an unjudged gap sets the cart at the gap's near lip at <see cref="SetPieceRules.JumpSpeed"/>
/// so the jump is judged as it would be, and never through an obstacle on the line: it is put at the obstacle's face and halts there.
/// Nor across the broken bridge (plan 11): a train stuck across on the slag line with its return open, short of a live switch back's
/// seated window with the lever straight, is set at the window's start (the calls and the offer run as they would); inside the window
/// and short of the last call, at the last call's mark (6 m before the points: "Switch back — now!" still comes, final review M1); past
/// that, or with the lever thrown, or short of a fixed back, at the back's points (taken as its lever stands); past every back, at the
/// gap, where it falls as it would have. Nor into ride 5's lava pit (plan 15): a train stuck on its own lane short of the pit switch's points with
/// the lever straight steps down the same way over the out lever (<see cref="RailEncounterController.WatchdogPit"/>: the window's start, the last
/// call's mark, the points, taken as the lever stands), and past the points on its own lane it is ground on into the pit. Nor onto ride 4's split (plan 12): a train stuck short of its points with the lever thrown has it set
/// back first (<see cref="RailEncounterController.WatchdogSplit"/>). Server and solo; the window counts elapsed time, so any physics rate gives the same 10 s, from the cart's last half-metre. No allocation.
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
    /// The cart is stuck (<see cref="CartWatchdog.PartyFreeTo(in CartState, float, float, float, float, float, float, out CartState)"/>): to the nearest
    /// of an unjudged gap's lip, an obstacle's face, a live switch back's window start or points and the broken bridge's gap (across on the slag
    /// line) short of its next stop, else on to that stop as an arrival.
    /// </summary>
    private void GrindFree()
    {
        GroundFree++;
        DebugStall = false;
        var from = State.S;
        EventBus.Instance.Log("The cart grinds free.");
        var next = Party.Limit;
        // the party cart runs toward higher S (Dir +1): its front leads its centre by a half length
        var lip = GapSet && !_jumpJudged && next > _gapFrom && LaneBlendAt(_gapFrom) < 0.5f ? _gapFrom - CartRules.HalfLength : float.NaN; // only on its own lane there (B.4's lane check)
        var face = LineBlocked && !OnEnemyLane(BlockedAt) ? BlockedAt - CartRules.HalfLength : float.NaN;
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) && e.GetParent() == GetParent() ? e : null;
        enc?.WatchdogSplit(); // short of the split's points with it thrown: set back first, so the grind never takes the spur (plan 12, Ruling 15)
        var (backWindow, backPoints, gap) = enc?.WatchdogBreak() ?? (float.NaN, float.NaN, float.NaN); // front S, NaN unless across toward the broken bridge
        var pit = false; // the pit's step-down (plan 15 Ruling 18) fills the same three slots when the break gives nothing: the out lever's window, its points, the lip
        if (enc != null && float.IsNaN(backWindow) && float.IsNaN(backPoints) && float.IsNaN(gap))
        {
            var (pitWindow, pitPoints, pitLip) = enc.WatchdogPit();
            if (!float.IsNaN(pitWindow) || !float.IsNaN(pitPoints) || !float.IsNaN(pitLip)) { (backWindow, backPoints, gap) = (pitWindow, pitPoints, pitLip); pit = true; }
        }
        var to = CartWatchdog.PartyFreeTo(State, next, lip, face, backWindow - CartRules.HalfLength, backPoints - CartRules.HalfLength, gap - CartRules.HalfLength, out var freed);
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
            case FreeTo.BackWindow: // nor past a live back's window, or its last call, unshown: there, rolling, the calls and the offer to come
            {
                var atWindow = backPoints - backWindow > SwitchBackRules.CallLast + 0.01f; // else the last call's mark, inside the window
                GD.Print(pit
                    ? $"[rails] watchdog: the cart was stuck at S={from:0.0} on the party lane, short of the pit switch's {(atWindow ? "window" : "last call")} (its lever straight); set with its front at {(atWindow ? "the window's start" : "the last call's mark")} (front S={freed.S + CartRules.HalfLength:0.0}, {backPoints - freed.S - CartRules.HalfLength:0.0} m short of its points)"
                    : $"[rails] watchdog: the cart was stuck at S={from:0.0} across on the slag line, short of the live switch back's {(atWindow ? "window" : "last call")} (its lever straight); set with its front at {(atWindow ? "the window's start" : "the last call's mark")} (front S={freed.S + CartRules.HalfLength:0.0}, {backPoints - freed.S - CartRules.HalfLength:0.0} m short of its points)");
                break;
            }
            case FreeTo.BackPoints: // nor past a switch back it has not taken: the back is taken as its lever stands (the encounter's next tick); the pit's out lever too (R-lone applies)
                GD.Print(pit
                    ? $"[rails] watchdog: the cart was stuck at S={from:0.0} on the party lane; set with its front at the pit switch's points (front S={freed.S + CartRules.HalfLength:0.0}), taken as its lever stands"
                    : $"[rails] watchdog: the cart was stuck at S={from:0.0} across on the slag line; set with its front at the switch back's points (front S={freed.S + CartRules.HalfLength:0.0}), taken as its lever stands");
                break;
            case FreeTo.BreakLip: // nor across the broken bridge: still across there, it falls as it would have; nor over the pit: on its own lane there, it falls in
                if (pit)
                {
                    GD.Print($"[rails] watchdog: the cart was stuck at S={from:0.0} past the pit switch's points on the party lane; ground on into the pit (front S={freed.S + CartRules.HalfLength:0.0}): the train falls");
                    FallNow(PitRules.FallLog);
                    break;
                }
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
