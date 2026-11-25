using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-perf[=crowd|ride] ----------------------------------------------------------------------------------
// Windowed only (never headless, never in flows.sh): the built-in campaign's floor 12 measured at fixed settings, kept in memory and
// never saved (VSync off, windowed 1280x720, Haze 0.5, render scale 100 %, shadows on, Show FPS off). Lethality pinned to the minimum,
// every hero topped up to full HP each second (a downed one stood up), no gathering, no portals, nothing culled, the mouse never captured
// (a window without focus cannot grab it). Frame times are wall time (FrameClock). Each run measures one phase. =crowd (the default; no
// ambushers either): rc.Restore(4), the party steps out beside the cart on stop 4's platform, 12 creatures (5 horde cinders, 4 ember
// hounds, 3 forge priests) spawned 10-20 m around it and sent at the party, the floor's own enemies alive; every enemy is held at 1 HP or
// more (Enemy.DebugHoldHp) and put back to full HP every frame (Enemy.DebugFillHp: a forge priest under 30 % would flee and change the
// load), so the load stays the same through the measure (it fails if the alive count changes); 5 s discarded, 20 s measured. It is the tick diet's before-and-after row, and stands in for the stop siege's "before". =ride
// (ambushers on): rc.Restore(0), the party seated, departs and rides ride 1 with every set piece and enemy alive; measured from 5 s after
// the departure to the arrival at stop 1. Prints the phase's [perf] line and [perf] RESULT ok.
public partial class DebugTools
{
    private bool _testPerf;
    private string _pfPhase = "crowd";
    private int _pfStep;
    private float _pfClock, _pfTopUp;
    private int _pfRevived;
    private readonly PerfWindow _pfWindow = new();
    private readonly FrameClock _pfWall = new();
    private (int Alive, int Aware, int Asleep) _pfAtStart;
    /// <summary>The crowd phase's enemies, gathered each second and topped up to full HP every frame (a list kept: nothing allocated per frame).</summary>
    private readonly List<Enemies.Enemy> _pfHeld = new();
    /// <summary>--test-perf: the mouse is never captured (an unfocused window cannot grab it, and X11 logs an ERROR when it tries).</summary>
    public bool NoMouseCapture { get; private set; }

    private const float PerfCrowdDiscard = 5f, PerfCrowdMeasure = 20f, PerfRideSettle = 5f;
    private static readonly (string Id, int Count)[] PerfCrowd = { ("enemy.horde_cinder", 5), ("enemy.ember_hound", 4), ("enemy.forge_priest", 3) };

    /// <summary>The phase's settings, in memory only: plan 16's Ruling 22, after the spec's Ruling 5 (1280x720 windowed, VSync off, Haze 0.5, render scale 100 %).</summary>
    private void PerfSettings()
    {
        NoMouseCapture = true;
        Settings.VSync = false;
        Settings.Fullscreen = false;
        Settings.Resolution = new Vector2I(1280, 720);
        Settings.Haze = 0.5f;
        Settings.RenderScale = 1f;
        Settings.Shadows = true;
        Settings.ShowFps = false;
    }

    private void RunPerfTest()
    {
        var main = Main.Instance;
        void Fail(string why) { GD.Print($"[perf] RESULT FAIL ({_pfPhase}): {why}"); _done = true; GetTree().Quit(1); }
        if (_pfStep == 0 && DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase)) { Fail("--test-perf is windowed only"); return; }
        var wall = _pfWall.Tick(); // every call: a frame's wall time is measured from the call on the frame before
        if (main?.Builder.Layout is not { } layout || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        var dt = (float)GetProcessDeltaTime();
        _pfClock += dt;
        var party = main.Party;
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_pfClock > 3f) Fail("floor 12 has no line"); return; }
        PerfTopUp(party, dt);
        foreach (var e in _pfHeld) if (IsInstanceValid(e)) e.DebugFillHp(); // every frame: no creature's behaviour turns on its wounds mid-measure
        if (_pfStep > 0 && wall is { } frame) _pfWindow.Add(frame);

        if (_pfPhase == "crowd") RunPerfCrowd(main, layout, rc, Fail);
        else RunPerfRide(main, layout, rc, enc, Fail);
    }

    /// <summary>Every second, every hero back to full HP (a downed one, not fallen, stood up first): the party never wipes mid-measure.</summary>
    private void PerfTopUp(PartyController party, float dt)
    {
        if ((_pfTopUp -= dt) > 0f) return;
        _pfTopUp = 1f;
        foreach (var a in party.Adventurers)
        {
            if (!IsInstanceValid(a) || a.Parked || a.State.Fallen) continue;
            if (a.Downed) { a.Revive(); _pfRevived++; }
            a.State.Hp = a.State.Stats[Characters.Stat.MaxHp];
        }
        if (_pfPhase != "crowd" || _pfStep == 0) return;
        _pfHeld.Clear();
        foreach (var n in GetTree().GetNodesInGroup("enemies")) // the crowd's load held: nobody dies of the party's blows (new arrivals too)
            if (n is Enemies.Enemy e && IsInstanceValid(e) && e.IsAlive) { e.DebugHoldHp = true; _pfHeld.Add(e); }
    }

    private void RunPerfCrowd(Main main, FloorLayout layout, RailController rc, Action<string> fail)
    {
        var party = main.Party;
        switch (_pfStep)
        {
            case 0 when _pfClock >= 1.5f:
            {
                if (rc.StopS.Length < 6) { fail($"floor 12's line has {rc.StopS.Length} stops, not 6"); return; }
                rc.Restore(4);
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.Stance = Characters.Stance.Hold; // they fight what reaches them, not the whole crowd
                if (PerfBeside(main, layout, rc.Cart.GlobalPosition) is not { } stand) { fail("no walkable cell beside the cart at stop 4"); return; }
                party.Teleport(stand + Vector3.Up * 0.3f, rc.Cart.GlobalPosition);
                var spawned = PerfSpawnCrowd(main, layout, rc.Cart.GlobalPosition);
                if (spawned < 12) { fail($"only {spawned} of the 12 creatures spawned"); return; }
                foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemies.Enemy e && IsInstanceValid(e) && e.IsAlive) e.DebugHoldHp = true; // before the first blow lands
                GD.Print($"[perf] crowd: rc.Restore(4), the party beside the cart at {DungeonBuilder.WorldToCell(stand)}, 12 creatures sent 10-20 m out; lethality {TierRules.Lethality}");
                _pfStep++; _pfClock = 0f;
                break;
            }
            case 1 when _pfClock >= PerfCrowdDiscard:
                _pfWindow.Reset();
                _pfAtStart = PerfWindow.CountEnemies(GetTree());
                GD.Print($"[perf] crowd: the measure starts with {_pfAtStart.Alive} enemies alive ({_pfAtStart.Aware} aware, {_pfAtStart.Asleep} asleep)");
                _pfStep++; _pfClock = 0f;
                break;
            case 2 when _pfWindow.Frames.Elapsed >= PerfCrowdMeasure:
            {
                var end = PerfWindow.CountEnemies(GetTree());
                GD.Print($"[perf] crowd: the measure ends with {end.Alive} enemies alive ({end.Aware} aware, {end.Asleep} asleep)");
                if (end.Alive != _pfAtStart.Alive) { fail($"the crowd's load changed: {_pfAtStart.Alive} alive at the start, {end.Alive} at the end"); return; }
                PerfReport();
                break;
            }
        }
    }

    private void RunPerfRide(Main main, FloorLayout layout, RailController rc, RailEncounterController enc, Action<string> fail)
    {
        var party = main.Party;
        switch (_pfStep)
        {
            case 0 when _pfClock >= 1.5f:
                rc.Restore(0);
                _pfStep++; _pfClock = 0f;
                break;
            case 1 when _pfClock >= 1f: // a beat after the restore, as the other ride flows board
            {
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                _pfStep++; _pfClock = 0f;
                break;
            }
            case 2:
            {
                if (party.Local is not { } me) { fail("no human"); return; }
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 1) { if (_pfClock > 15f) fail($"ride 1 did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide}, lead seated {me.Seated})"); break; }
                GD.Print($"[perf] ride: departed stop 0 with {party.Active.Count(a => a.Seated)} heroes seated; lethality {TierRules.Lethality}");
                _pfStep++; _pfClock = 0f;
                break;
            }
            case 3 when _pfClock >= PerfRideSettle:
                if (rc.State.Mode == CartMode.Held) { fail($"the cart was held at stop {rc.StopIndex} {PerfRideSettle:0} s after the departure"); return; }
                _pfWindow.Reset();
                _pfStep++; _pfClock = 0f;
                break;
            case 4:
                if (rc.State.Mode == CartMode.Wrecked) { fail("the cart wrecked on ride 1"); return; }
                if (rc.State.Mode == CartMode.Held && rc.StopIndex == 1) PerfReport();
                break;
        }
    }

    private void PerfReport()
    {
        var f = _pfWindow.Frames;
        var (alive, aware, asleep) = PerfWindow.CountEnemies(GetTree());
        if (_pfRevived > 0) GD.Print($"[perf] note: {_pfRevived} downed heroes stood up by the top-up");
        GD.Print($"[perf] {_pfPhase}: {f.Frames} frames over {f.Elapsed:0.0} s: median {f.MedianFps:0} fps, 1% low {f.LowFps():0}, worst second {f.WorstSecondFps:0}, worst frame {f.WorstFrameMs:0.0} ms; process {_pfWindow.ProcessMs:0.00} ms, physics {_pfWindow.PhysicsMs:0.00} ms, draws {_pfWindow.Draws}, alloc_kb {_pfWindow.AllocKb}; enemies {alive} ({aware} aware) asleep={asleep}");
        GD.Print($"[perf] RESULT ok ({_pfPhase}: median {f.MedianFps:0} fps, 1% low {f.LowFps():0}, worst second {f.WorstSecondFps:0})");
        _done = true;
        GetTree().Quit();
    }

    /// <summary>The nearest walkable floor cell (not the line, not a prop) within 3 cells of <paramref name="at"/>: the stop's platform beside the cart.</summary>
    private static Vector3? PerfBeside(Main main, FloorLayout layout, Vector3 at)
    {
        var here = DungeonBuilder.WorldToCell(at);
        Vector3? best = null;
        var bestD = float.MaxValue;
        for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                if (!layout.IsWalkable(c) || layout.IsRail(c) || main.Builder.IsCellBlocked(c)) continue;
                var p = main.Builder.Surface(c);
                var d = p.DistanceTo(at);
                if (d < bestD) { bestD = d; best = p; }
            }
        return best;
    }

    /// <summary>The crowd: walkable floor cells 10-20 m (level) from the cart on its own level, picked by a fixed seed; a cell is used again (jittered) if there are fewer than 12.</summary>
    private int PerfSpawnCrowd(Main main, FloorLayout layout, Vector3 at)
    {
        var here = DungeonBuilder.WorldToCell(at);
        var cells = new List<Vector3>();
        for (var dy = -6; dy <= 6; dy++)
            for (var dx = -6; dx <= 6; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                if (!layout.IsWalkable(c) || layout.IsRail(c) || main.Builder.IsCellBlockedForEnemies(c)) continue;
                var p = main.Builder.Surface(c);
                var flat = new Vector2(p.X - at.X, p.Z - at.Z).Length();
                if (flat < 10f || flat > 20f || Mathf.Abs(p.Y - at.Y) > 3f) continue;
                cells.Add(p);
            }
        if (cells.Count == 0) return 0;
        var rng = new Random(1216);
        for (var i = cells.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }
        var spawned = 0;
        var k = 0;
        foreach (var (id, count) in PerfCrowd)
            for (var n = 0; n < count; n++, k++)
            {
                var jitter = k < cells.Count ? Vector3.Zero : new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 2f;
                if (main.Builder.SpawnEnemy(id, cells[k % cells.Count] + jitter + Vector3.Up * 0.1f, main.Builder.CurrentFloor) is not { } e) continue;
                e.ForceAggro();
                spawned++;
            }
        GD.Print($"[perf] crowd: {cells.Count} cells 10-20 m from the cart, {spawned} creatures spawned");
        return spawned;
    }
}
