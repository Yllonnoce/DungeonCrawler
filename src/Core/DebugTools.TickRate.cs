using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-tick-rate (with --physics-hz=N to run the physics at a headset's rate) ------------------------------------
// The floor's metronome keeps real time at any physics rate: on floor 1 it samples the drop hammer's beat and the periodic
// spike bed's cycle at the floor's tick (index 0, the pure rules the set pieces and the bed run on) every physics step, and
// measures the wall time between two entries into the hammer's fall and between two cycle starts. Both are 4.5 s at 60 Hz;
// they must stay 4.5 s at 90 Hz (before the floor clock they took 3 s). Prints [tick-rate] lines and the RESULT.
public partial class DebugTools
{
    private bool _testTickRate;
    private int? _physicsHz;
    private HammerPhase? _trHammerWas;
    private SpikePhase? _trSpikesWas;
    private readonly List<ulong> _trHammerAt = new();
    private readonly List<ulong> _trSpikesAt = new();
    private double _trWaited;
    private ulong? _trFrame0;
    private int _trTick0;

    public const double TickRateCycleSeconds = 4.5;
    public const double TickRateTolerance = 0.1;
    private const int FloorClockTicksPerSecond = Net.FloorClock.TicksPerSecond;

    /// <summary>Called every physics step while --test-tick-rate runs.</summary>
    private void TickRatePoll(double delta)
    {
        if (_done) return;
        var main = Main.Instance;
        if (main?.Builder.Layout is null || !main.InGame || main.Rebuilding || Game.Instance.CurrentFloor != 1) return;
        _trWaited += delta;
        var b = main.Builder;
        var hammer = SetPieceRules.Phase(b.FloorTick, 0, out _);
        var spikes = HazardRules.PeriodicPhaseAt(b.FloorTick, 0, out _);
        var now = Time.GetTicksMsec();
        if (_trFrame0 == null) { _trFrame0 = Engine.GetPhysicsFrames(); _trTick0 = b.FloorTick; }
        if (_trHammerWas is { } hw && hw != HammerPhase.Fall && hammer == HammerPhase.Fall) _trHammerAt.Add(now);
        if (_trSpikesWas is { } sw && sw != SpikePhase.Down && spikes == SpikePhase.Down) _trSpikesAt.Add(now);
        _trHammerWas = hammer;
        _trSpikesWas = spikes;
        if (_trHammerAt.Count >= 2 && _trSpikesAt.Count >= 2)
        {
            var hammerS = (_trHammerAt[1] - _trHammerAt[0]) / 1000.0;
            var spikesS = (_trSpikesAt[1] - _trSpikesAt[0]) / 1000.0;
            // the metronome against the physics steps: at 60 Hz one floor tick a step exactly (as before the floor clock), at 90 Hz two every three
            var frames = (long)(Engine.GetPhysicsFrames() - _trFrame0!.Value);
            var ticks = b.FloorTick - _trTick0;
            var expected = frames * FloorClockTicksPerSecond / Engine.PhysicsTicksPerSecond;
            var ticksOk = Engine.PhysicsTicksPerSecond == FloorClockTicksPerSecond ? ticks == expected : Math.Abs(ticks - expected) <= 1;
            var ok = ticksOk && Math.Abs(hammerS - TickRateCycleSeconds) <= TickRateTolerance && Math.Abs(spikesS - TickRateCycleSeconds) <= TickRateTolerance;
            GD.Print($"[tick-rate] physics {Engine.PhysicsTicksPerSecond} Hz; floor {b.FloorSeconds:0.000} s, tick {b.FloorTick}; {ticks} floor ticks over {frames} physics steps (expected {expected}{(ticksOk ? "" : ", WRONG")})");
            GD.Print($"[tick-rate] RESULT {(ok ? "ok" : "fail")} hammer={hammerS:0.000} spikes={spikesS:0.000}");
            _done = true;
            GetTree().Quit(ok ? 0 : 1);
            return;
        }
        if (_trWaited > 20)
        {
            GD.Print($"[tick-rate] RESULT fail: no two cycles in 20 s (hammer entries {_trHammerAt.Count}, spike cycles {_trSpikesAt.Count}, tick {b.FloorTick})");
            _done = true;
            GetTree().Quit(1);
        }
    }
}
