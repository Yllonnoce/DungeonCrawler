using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// The perf line in the game's log, on every role (solo, client, server) whenever <see cref="Main.InGame"/>: every
/// <see cref="PerfRules.LineEvery"/> s of frames, the mean and worst-second fps, the worst frame, the mean process and physics ms, the
/// mean draw calls, and the enemies alive, aware and asleep (the tick diet's quarter rate), with the floor and whether the party is on a cart. A frame over
/// <see cref="PerfRules.SlowFrameMs"/> gets its own line, at most one each <see cref="PerfRules.SlowLineGap"/> s, carrying the slow frames
/// counted since the last one. Frame times are wall time between frames (<see cref="FrameClock"/>), not the engine's process delta, which
/// Godot caps on a long frame. A headless server's fps is its own loop's rate. After a floor is built, <see cref="PerfRules.BuildGrace"/> s of
/// frames are not counted (the load), and a frame the tree was paused through is skipped.
/// </summary>
public partial class PerfMonitor : Node
{
    private readonly PerfWindow _window = new();
    private readonly FrameClock _clock = new();
    private Node? _floorRoot;
    private float _grace;
    private float _sinceSlowLine = PerfRules.SlowLineGap;
    private int _slowSince;

    public override void _Process(double delta)
    {
        var wall = _clock.Tick();
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Builder.FloorRoot is not { } root) { _floorRoot = null; return; }
        if (root != _floorRoot)
        {   // a new floor (or the same one built again): the load and its settling are not play
            _floorRoot = root;
            _grace = PerfRules.BuildGrace;
            _window.Reset();
            return;
        }
        if (wall is not { } dt) return; // the tree was paused through it: not one frame
        if (_grace > 0f)
        {
            _grace -= dt;
            if (_grace <= 0f) { _window.Reset(); _sinceSlowLine = PerfRules.SlowLineGap; _slowSince = 0; }
            return;
        }
        _window.Add(dt);
        _sinceSlowLine += dt;
        var ms = dt * 1000f;
        if (PerfRules.Slow(ms))
        {
            _slowSince++;
            if (_sinceSlowLine >= PerfRules.SlowLineGap)
            {
                GD.Print($"[perf] slow frame {ms:0} ms (floor {main.Builder.CurrentFloor}, cart {YesNo(OnCart(main.Party))}; {_slowSince} slow since the last line)");
                _slowSince = 0;
                _sinceSlowLine = 0f;
            }
        }
        if (_window.Frames.Elapsed < PerfRules.LineEvery) return;
        var f = _window.Frames;
        var (alive, aware, asleep) = PerfWindow.CountEnemies(GetTree());
        GD.Print($"[perf] floor={main.Builder.CurrentFloor} cart={YesNo(OnCart(main.Party))} fps avg={f.MeanFps:0} min={f.WorstSecondFps:0} worst_ms={f.WorstFrameMs:0.0} process_ms={_window.ProcessMs:0.00} physics_ms={_window.PhysicsMs:0.00} draws={_window.Draws} enemies={alive} aware={aware} asleep={asleep}");
        _window.Reset();
    }

    private static string YesNo(bool b) => b ? "yes" : "no";

    /// <summary>The local hero seated in a cart; with no local hero (a dedicated server), any hero seated.</summary>
    internal static bool OnCart(PartyController? party)
    {
        if (party == null || !IsInstanceValid(party)) return false;
        if (party.Local is { } me) return IsInstanceValid(me) && me.Seated;
        foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Seated) return true;
        return false;
    }
}

/// <summary>
/// Wall time between frames: <see cref="Tick"/> once a frame gives the seconds since the call on the frame before, or null when the last
/// call was not on the frame before (the first call, a paused tree, a skipped call). Not the engine's process delta, which Godot cuts to
/// at most <c>max_physics_steps_per_frame</c> physics steps (about 133 ms at 60 Hz) however long the frame really took.
/// </summary>
public sealed class FrameClock
{
    private ulong _lastUsec, _lastFrame;

    public float? Tick()
    {
        var now = Time.GetTicksUsec();
        var frame = Engine.GetProcessFrames();
        float? dt = _lastUsec != 0 && frame == _lastFrame + 1 ? (float)((now - _lastUsec) / 1_000_000.0) : null;
        _lastUsec = now;
        _lastFrame = frame;
        return dt;
    }
}

/// <summary>
/// One measured window: the frame times (<see cref="FrameStats"/>), the means of the engine's process and physics times and draw calls
/// sampled each frame, and the managed bytes allocated since it began. The perf line's 10 s and each --test-perf phase use one.
/// </summary>
public sealed class PerfWindow
{
    public FrameStats Frames { get; } = new();
    private double _process, _physics, _draws;
    private long _allocStart = GC.GetTotalAllocatedBytes();

    public void Add(float frameSeconds)
    {
        Frames.Add(frameSeconds);
        _process += Performance.GetMonitor(Performance.Monitor.TimeProcess);
        _physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess);
        _draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
    }

    public float ProcessMs => Frames.Frames > 0 ? (float)(_process / Frames.Frames * 1000.0) : 0f;
    public float PhysicsMs => Frames.Frames > 0 ? (float)(_physics / Frames.Frames * 1000.0) : 0f;
    public int Draws => Frames.Frames > 0 ? (int)Math.Round(_draws / Frames.Frames) : 0;
    /// <summary>Managed bytes allocated since the window began (every thread), in KiB.</summary>
    public long AllocKb => (GC.GetTotalAllocatedBytes() - _allocStart) / 1024;

    public void Reset()
    {
        Frames.Reset();
        _process = _physics = _draws = 0;
        _allocStart = GC.GetTotalAllocatedBytes();
    }

    /// <summary>
    /// The enemies alive in the "enemies" group, how many of them are aware (a client reads the server's flag), and how many are on the
    /// tick diet's quarter rate this tick (<see cref="Enemy.Asleep"/>; always 0 on a client, which runs no enemy AI).
    /// </summary>
    public static (int Alive, int Aware, int Asleep) CountEnemies(SceneTree tree)
    {
        int alive = 0, aware = 0, asleep = 0;
        foreach (var n in tree.GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || !GodotObject.IsInstanceValid(e) || !e.IsAlive) continue;
            alive++;
            if (!e.IsUnaware) aware++;
            if (e.Asleep) asleep++;
        }
        return (alive, aware, asleep);
    }
}
