using DungeonCrawler.Dungeon.Rails;
using Godot;

namespace DungeonCrawler.Core;

// ---- the solo ride flows' way along the line (plan 9) -------------------------------------------------------
// The flows script the stops they test (the landing, stops 1–3, the kiln's) and ride the rest for real with RideOnTo: at every
// intermediate stop the cart is held at on the way, the stop's holders die by the test's hand, its gate rises, the cage is left
// (RideWaiveCages, as the flows leave a locked cage), the party boards and the cart departs when its countdown runs out. Every
// literal a flow names for the kiln or the end of the line reads LineShape, so a line of any stop count needs no edits.
public partial class DebugTools
{
    /// <summary>The shipped floor 12's line: five rides, six stops (the landing, four crew stops, the Cooling Stair).</summary>
    private const int Floor12Stops = 6;
    /// <summary>A ride RideOnTo waits on must bring the cart to its next stop within this long (the longest shipped ride is ~280 m).</summary>
    private const float RideOnRideSeconds = 90f;

    private int _rideOnStop = -1, _rideOnPhase;
    private float _rideOnClock;

    /// <summary>The line the cart runs: its rides, its intermediate stops, the kiln's ride and the stop it leaves from.</summary>
    private static LineShape Shape(RailController rc) => new(rc.StopS.Length);

    /// <summary>The kiln's piece <paramref name="piece"/>'s id on this line: "r{LastRide}_{piece}" (r5_pour on the shipped floor).</summary>
    private static string Kiln(RailController rc, string piece) => $"r{Shape(rc).LastRide}_{piece}";

    /// <summary>
    /// Called every tick: true once the cart is held at <paramref name="stop"/> (not halted on the line). On the way, at each intermediate
    /// stop it is held at, the stop's holders die by the test's hand (<see cref="RailController.DebugKillHolders"/>), the gate rises, a
    /// locked cage is left behind (<see cref="RideWaiveCages"/>), the party boards if the local hero is not seated, and the countdown
    /// departs it. A stop past <paramref name="stop"/>, a halt or a ride that never ends fails.
    /// </summary>
    private bool RideOnTo(Main main, RailController rc, int stop, string tag, Action<string> fail)
    {
        _rideOnClock += (float)GetProcessDeltaTime();
        var held = rc.State.Mode == CartMode.Held && !rc.Halted;
        if (held && rc.StopIndex == stop)
        {
            if (_rideOnStop >= 0) GD.Print($"[{tag}] ride on: held at stop {stop} after {_rideOnClock:0.0} s (S={rc.State.S:0.0})");
            _rideOnStop = -1;
            _rideOnPhase = 0;
            _rideOnClock = 0f;
            return true;
        }
        if (rc.State.Mode == CartMode.Rolling)
        {
            if (_rideOnPhase == 2) { GD.Print($"[{tag}] ride on: departed stop {_rideOnStop} for stop {_rideOnStop + 1} (ride {_rideOnStop + 1} armed: {RailEncounterController.Instance?.ArmedRide}), {main.Party.Active.Count(a => a.Seated)} heroes seated, train=[{string.Join(",", rc.Train.Select(w => w.Number))}]"); _rideOnPhase = 3; _rideOnClock = 0f; }
            if (_rideOnClock > RideOnRideSeconds) fail($"ride on to stop {stop}: the cart rolled {_rideOnClock:0} s from stop {rc.StopIndex} without being held at the next (S={rc.State.S:0.0})");
            return false;
        }
        if (!held || rc.StopIndex > stop || rc.StopIndex < 1 || rc.StopIndex > Shape(rc).Intermediate)
        {
            fail($"ride on to stop {stop}: the cart is {rc.State.Mode} at stop {rc.StopIndex} (halted {rc.Halted})");
            return false;
        }
        var k = rc.StopIndex;
        if (_rideOnStop != k) { _rideOnStop = k; _rideOnPhase = 0; _rideOnClock = 0f; }
        switch (_rideOnPhase)
        {
            case 0:
            {
                var killed = rc.GateUp(k) ? 0 : rc.DebugKillHolders(k);
                GD.Print($"[{tag}] ride on: held at stop {k} on the way to stop {stop} (S={rc.State.S:0.0}, plating {rc.State.Plating:0}): {killed} holder(s) die by the test's hand");
                _rideOnPhase = 1;
                _rideOnClock = 0f;
                break;
            }
            case 1:
            {
                if (!rc.GateUp(k)) { if (_rideOnClock > 3f) fail($"ride on: stop {k}'s gate did not lift with its holders dead"); break; }
                RideWaiveCages(main, rc, tag);
                if (main.Party.Local is not { } me) { fail("ride on: no local hero"); break; }
                if (me.Cart != rc.Cart && !RideBoard(main, main.Builder.Layout!, rc, out var why)) { fail(why); break; }
                _rideOnPhase = 2;
                _rideOnClock = 0f;
                break;
            }
            case 2:
                RideWaiveCages(main, rc, tag);
                if (_rideOnClock > 12f) fail($"ride on: the cart did not leave stop {k} within 12 s (boarding {rc.Boarding.Running}, cage held {rc.CageHeld}, gate up {rc.GateUp(k)})");
                break;
        }
        return false;
    }
}
