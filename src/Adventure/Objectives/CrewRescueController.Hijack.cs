using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The crew a hijack put off the train (plan 16 Task 7, spec section 3 "Getting away" and its Ruling 3). A prisoner put off the rolling-back
/// train, or set down where it got away, is set down alive beside its wagon (<see cref="PutOffAlive"/>, never the lethal <see cref="ThrowCrew"/>)
/// and follows the party. It is "thrown" until it sits, dies, or the floor changes (a load or a restore: it is then an ordinary freed crew
/// member), and at most until the 30 s cap after the train is held again or the train's departure (Ruling 15 as ruled in the T7 review). A thrown crew member books a seat from <see cref="HijackRules.ThrownBoardReach"/> (60 m; everyone else 25 m), and the departure from
/// its stop waits while any is on the way, up to <see cref="HijackRules.ThrownWaitMax"/> after the train is held there again (HijackOver: the
/// return or the relief train). At that cap a thrown walker within 60 m of its seat sits in at once; farther ones stay on foot. A hijack never
/// kills crew and never counts them lost: crew who die on the way are lost as in any fight. Server and solo; nothing of it is saved.
/// </summary>
public partial class CrewRescueController
{
    /// <summary>The crew a hijack put off, by id, with the stop the train was hijacked from.</summary>
    private readonly Dictionary<string, int> _thrown = new();
    /// <summary>The stop the train was last held at again after a hijack (HijackOver), or −1, and the seconds since.</summary>
    private int _heldAgainStop = -1;
    private float _sinceHeldAgain = float.PositiveInfinity;
    private readonly List<string> _thrownGone = new();

    /// <summary>Test hook: the ids of the crew a hijack put off who are still "thrown" (not yet seated, not dead, the floor unchanged).</summary>
    internal IReadOnlyCollection<string> ThrownIds => _thrown.Keys;

    /// <summary>A hijack put <paramref name="npc"/> off and it has not sat or died since.</summary>
    private bool IsThrown(Npc npc) => _thrown.ContainsKey(npc.Id);

    /// <summary>The booking reach of <paramref name="npc"/>: 60 m for one a hijack put off, else <see cref="TrainRules.BoardReach"/>.</summary>
    private float BookingReach(Npc npc) => IsThrown(npc) ? HijackRules.ThrownBoardReach : TrainRules.BoardReach;

    /// <summary>
    /// Where <paramref name="npc"/> walks to sit in <paramref name="wagon"/>: beside the lane by the wagon (<see cref="RailController.StandBesideLane(Vector3)"/>).
    /// One a hijack put off comes back along the line, often on the far side of the held train (solid between the two sides, the platform's cells
    /// beyond it): it walks to the wagon's tub on its own side instead (as a runner reaches a rim) and hops in from there.
    /// </summary>
    private Vector3 BookingSpot(Npc npc, TrainWagon wagon)
    {
        var beside = _rails!.StandBesideLane(wagon.GlobalPosition);
        if (!IsThrown(npc)) return beside;
        var at = wagon.GlobalPosition;
        var right = wagon.GlobalTransform.Basis.X with { Y = 0f };
        right = right.LengthSquared() > 1e-4f ? right.Normalized() : Vector3.Right;
        var across = (npc.GlobalPosition - at).Dot(right);
        var side = Mathf.Abs(across) > 0.3f ? Mathf.Sign(across) : Mathf.Sign((beside - at).Dot(right));
        return at + right * (side == 0f ? 1f : side) * (MineCart.TubWidth / 2f + ThrownSideOut);
    }

    /// <summary>A thrown crew member's spot stands this far out from its wagon's tub side (m): a body's reach of the rim.</summary>
    private const float ThrownSideOut = 0.7f;

    /// <summary>Seconds since the train was held at <paramref name="stop"/> again after a hijack there (+inf: it was not).</summary>
    private float SinceHeldAgain(int stop) => stop == _heldAgainStop ? _sinceHeldAgain : float.PositiveInfinity;

    /// <summary>
    /// The hijacked train puts <paramref name="npc"/> off <paramref name="wagon"/> (server and solo; Ruling 13): out of its seat and set down alive
    /// beside the lane by its wagon (<see cref="RailController.StandBesideLane(Vector3)"/>), following the party (a downed one lies there). It is
    /// thrown from here on (<see cref="ThrownIds"/>). Never lost, never the boarder's lethal throw.
    /// </summary>
    internal void PutOffAlive(Npc npc, TrainWagon wagon)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails) || !IsInstanceValid(npc) || npc.Dead || !IsInstanceValid(wagon)) return;
        if (_bookings.Remove(npc, out var booked)) StopWalking(npc, booked);
        npc.GetOut(_rails.StandBesideLane(wagon.GlobalPosition), Vector3.Zero);
        if (!npc.Escorting && !npc.Downed) npc.StartFollowing();
        _thrown[npc.Id] = _rails.HijackStop >= 0 ? _rails.HijackStop : _rails.StopIndex;
        GD.Print($"[CrewRescue] {npc.NpcName} put off wagon {wagon.Number} alive by the hijack");
        RefreshHud();
    }

    /// <summary>The crew a hijack at <paramref name="stop"/> put off who are still on the way back (alive, up or down, not yet seated).</summary>
    internal int ThrownOnTheWay(int stop)
    {
        var n = 0;
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && !npc.Dead && !npc.Seated && _thrown.TryGetValue(npc.Id, out var at) && at == stop) n++;
        return n;
    }

    /// <summary>Each tick (server and solo), before the boarding: the clock since the train was held again runs; a thrown crew member who sat or died is thrown no more.</summary>
    private void TickThrown(float dt)
    {
        if (!float.IsPositiveInfinity(_sinceHeldAgain)) _sinceHeldAgain += dt;
        if (_thrown.Count == 0) return;
        _thrownGone.Clear();
        foreach (var npc in _crew)
            if ((!IsInstanceValid(npc) || npc.Dead || npc.Seated) && _thrown.ContainsKey(npc.Id)) _thrownGone.Add(npc.Id); // Id is a plain property: readable on a freed one
        EndThrown(_thrownGone, "sat or died");
    }

    /// <summary>
    /// Each tick (server and solo), after the boarding (so the cap's sit-in at once comes first): at <see cref="HijackRules.ThrownWaitMax"/> since the
    /// train was held again, the crew of that stop still thrown (beyond 60 m, or not booked) are thrown no more (<see cref="HijackRules.StillThrown"/>):
    /// ordinary freed crew on foot.
    /// </summary>
    private void ExpireThrown()
    {
        if (_thrown.Count == 0) return;
        _thrownGone.Clear();
        foreach (var (id, stop) in _thrown)
            if (!HijackRules.StillThrown(SinceHeldAgain(stop), departed: false)) _thrownGone.Add(id);
        EndThrown(_thrownGone, $"the {HijackRules.ThrownWaitMax:0} s cap");
    }

    private void EndThrown(List<string> ids, string why)
    {
        foreach (var id in ids)
            if (_thrown.Remove(id)) GD.Print($"[CrewRescue] {id} is no longer thrown ({why})");
        ids.Clear();
    }

    /// <summary>The train's hijack events as the crew hear them: held again (HijackOver) starts the 30 s wait; a restore makes every thrown one ordinary.</summary>
    private void OnHijackCartEvent(CartEvent e)
    {
        switch (e.Kind)
        {
            case CartEventKind.HijackOver:
                _heldAgainStop = e.Stop;
                _sinceHeldAgain = 0f;
                if (ThrownOnTheWay(e.Stop) is var n and > 0) GD.Print($"[CrewRescue] the train held again at stop {e.Stop}: the departure waits up to {HijackRules.ThrownWaitMax:0} s for {n} thrown crew");
                break;
            case CartEventKind.Hijacked:
                _heldAgainStop = -1;
                _sinceHeldAgain = float.PositiveInfinity;
                break;
            case CartEventKind.Departed: // the train left (Ruling 15 as ruled in the T7 review): those still on foot are ordinary freed crew
                _thrownGone.Clear();
                foreach (var (id, stop) in _thrown) if (!HijackRules.StillThrown(SinceHeldAgain(stop), departed: true)) _thrownGone.Add(id);
                EndThrown(_thrownGone, "the train left");
                _heldAgainStop = -1;
                _sinceHeldAgain = float.PositiveInfinity;
                break;
            case CartEventKind.Restored:
                _thrown.Clear(); // a load (Ruling 16): crew on foot are ordinary freed crew
                _heldAgainStop = -1;
                _sinceHeldAgain = float.PositiveInfinity;
                break;
        }
    }
}
