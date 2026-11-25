using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The stop trap's hijackers (plan 16 Task 6, spec section 3 and its Ruling 2). A siege wave from the second on tags at most one of its
/// creatures, a cinder imp or an ember hound (<see cref="HijackRules.MayTag"/>: one hijack live at a time, <see cref="HijackRules.NextTagAfter"/>
/// after the last ended, only where the train has room to roll). A tagged runner runs for the train while <see cref="HijackRules.MayRun"/> holds
/// (nobody seated, the train held at a sieged stop or in its glow or roll back; Ruling 25), ticked at 10 Hz: for the cart's side while the cart
/// has no driver and no other runner climbs it, else for the nearest coupled wagon with a free boarder spot (crewed first). Within
/// <see cref="HijackRules.ClimbReach"/> it climbs for <see cref="HijackRules.ClimbSeconds"/>, then <see cref="RailController.TakeDriver"/> has it
/// (the cart) or <see cref="RailEncounterController.BoardWagonAtStop"/> (a wagon, a spared boarder). A party hit makes it fight its attacker for
/// <see cref="HijackRules.FightBackSeconds"/> and cancels a climb (Ruling 9). The brake, the return or a got-away release every runner to the
/// chase, still tagged (the straggler cull spares a hijacker: <see cref="IsHijacker"/>); a restore, a wreck, the rest on foot or a departure
/// release them untagged. Server and solo only; nothing of it is saved.
/// </summary>
public partial class StopTrapController
{
    /// <summary>The runners decide their target this often (s): 10 Hz.</summary>
    private const float RunEvery = 0.1f;
    /// <summary>A runner's goal stands this far out from the tub's side (m): its body's reach of the rim.</summary>
    private const float SideOut = 0.7f;
    /// <summary>A climb is lost when its climber falls this far past the climb's reach (m): the train rolled away from it.</summary>
    private const float ClimbSlack = 0.5f;

    private sealed class Runner
    {
        public readonly Enemy Enemy;
        /// <summary>What it runs for: the party's cart or a coupled wagon (null: nothing to board now, it fights).</summary>
        public Node3D? Target;
        /// <summary>The side of the target it goes to, a unit vector across the tub (world), refreshed at each decision.</summary>
        public Vector3 Side;
        /// <summary>Seconds into its climb, or NaN while it is not climbing.</summary>
        public float Climb = float.NaN;
        /// <summary>Seconds since a party hit (Ruling 9's fight back), +inf before one.</summary>
        public float SinceHit = float.PositiveInfinity;
        public Runner(Enemy enemy) => Enemy = enemy;
        public bool Climbing => !float.IsNaN(Climb);
        /// <summary>Where it runs: beside the target's tub on its chosen side (read each tick: the train may be rolling back).</summary>
        public Vector3 Goal() => Target != null && IsInstanceValid(Target) ? Target.GlobalPosition + Side * (MineCart.TubWidth / 2f + SideOut) : Enemy.GlobalPosition;
    }

    private readonly List<Runner> _runners = new();
    private readonly List<Enemy> _runnerView = new();
    /// <summary>Every creature tagged as a hijacker and not yet untagged (runners, drivers knocked off, released runners).</summary>
    private readonly HashSet<Enemy> _tagged = new();
    /// <summary>The stop and wave that last tagged (Ruling 2: one a wave), or −1.</summary>
    private int _tagStop = -1, _tagWave = -1;
    /// <summary>Seconds since the last hijack ended (HijackOver), +inf before the first.</summary>
    private float _sinceHijackOver = float.PositiveInfinity;
    private float _runClock;
    /// <summary>Test hook: the next siege spawns of an allowed kind to tag whatever their wave (<see cref="DebugTagNext"/>).</summary>
    private int _debugTagsDue;
    private bool _hurtHooked;

    /// <summary>The tagged creatures running for the train now (a driver, a wagon's boarder or a released one is not).</summary>
    public IReadOnlyList<Enemy> Runners
    {
        get
        {
            _runnerView.Clear();
            foreach (var r in _runners) _runnerView.Add(r.Enemy);
            return _runnerView;
        }
    }

    /// <summary>Seconds since the last hijack ended (+inf before the first): the next tag waits <see cref="HijackRules.NextTagAfter"/>.</summary>
    public float SinceHijackOver => _sinceHijackOver;

    /// <summary>
    /// The hijack's moments on this floor build (tests read them): physics seconds, what ("tagged", "climbs the cart", "climbs wagon k", "driver",
    /// "boards wagon k", "climb lost", "climb cut by a hit", "released", "untagged"), the stop and wave of a tag (else −1), and who.
    /// </summary>
    internal readonly List<(double At, string What, int Stop, int Wave, Enemy Who)> HijackLog = new();

    private static double PhysicsNow => Engine.GetPhysicsFrames() / (double)Engine.PhysicsTicksPerSecond;

    /// <summary>
    /// The hijack holds <paramref name="e"/>: tagged (running, released or knocked off, until a restore, a wreck, the rest on foot or a departure),
    /// driving the cart, or riding a wagon as a spared boarder. The straggler cull never frees one (Ruling 32).
    /// </summary>
    public bool IsHijacker(Enemy e)
        => _tagged.Contains(e) || IsInstanceValid(_rails) && _rails.Cart.Driver == e || Encounters?.IsSparedBoarder(e) == true;

    /// <summary>This floor's encounter controller (the wagons' boarders), or null.</summary>
    private RailEncounterController? Encounters
        => RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.GetParent() == GetParent() ? enc : null;

    /// <summary>The party's hits on a runner (server and solo): it fights back for <see cref="HijackRules.FightBackSeconds"/>, its climb cut (Ruling 9).</summary>
    private void HookHijackHurt()
    {
        if (_hurtHooked || EventBus.Instance is not { } bus) return;
        bus.EnemyHurt += OnRunnerHurt;
        _hurtHooked = true;
    }

    private void UnhookHijackHurt()
    {
        if (_hurtHooked && EventBus.Instance is { } bus) bus.EnemyHurt -= OnRunnerHurt;
        _hurtHooked = false;
    }

    private void OnRunnerHurt(Enemy e, float amount, Combat.HitFlavor flavor, int byMember)
    {
        if (NetSession.IsClient || !BoarderRules.Counts(byMember) || !IsInstanceValid(e) || !e.IsAlive) return;
        foreach (var r in _runners)
        {
            if (r.Enemy != e) continue;
            r.SinceHit = 0f;
            if (r.Climbing)
            {
                r.Climb = float.NaN;
                GD.Print($"[hijack] {e.Name}'s climb into {TargetName(r.Target)} is cut by a hit");
                HijackLog.Add((PhysicsNow, "climb cut by a hit", -1, -1, e));
            }
            if (!e.Carried && e.State is not ChaseState) e.ChangeState(new ChaseState()); // at its attacker (the hit's threat retargets it)
            return;
        }
    }

    /// <summary>A siege creature came through (server and solo; before the warning's part of <see cref="OnSiegeSpawned"/>): tagged when the rules allow.</summary>
    private void OnHijackSpawned(Enemy e, int stop, int wave)
    {
        if (NetSession.IsClient || DebugTools.Instance?.NoHijack == true || !IsInstanceValid(e) || !e.IsAlive) return;
        if (!HijackRules.MayTagKind(e.Profile.Id)) return;
        if (_debugTagsDue > 0) { _debugTagsDue--; Tag(e, stop, wave, " (the test's)"); return; }
        if (stop != _rails.StopIndex) return; // a portal of a siege the train has left
        var taggedThisWave = _tagStop == stop && _tagWave == wave;
        if (!HijackRules.MayTag(wave, _rails.HijackLive, _sinceHijackOver, taggedThisWave, e.Profile.Id, _rails.RollRoomAt(stop))) return;
        Tag(e, stop, wave, "");
    }

    private void Tag(Enemy e, int stop, int wave, string why)
    {
        HookHijackHurt();
        _tagged.Add(e);
        _tagStop = stop;
        _tagWave = wave;
        _runners.Add(new Runner(e));
        _runClock = RunEvery; // decided on the next tick
        GD.Print($"[hijack] stop {stop} wave {wave}: {e.Name} tagged{why}");
        HijackLog.Add((PhysicsNow, "tagged", stop, wave, e));
    }

    /// <summary>The cart's events as the hijack hears them (server and solo; called from <see cref="OnCartEvent"/>).</summary>
    private void OnHijackCartEvent(CartEvent e)
    {
        switch (e.Kind)
        {
            case CartEventKind.HijackOver:
                _sinceHijackOver = 0f; // the next tag waits 20 s from here (spec Ruling 2)
                break;
            case CartEventKind.Arrived:
                _tagStop = _tagWave = -1;
                break;
            case CartEventKind.Restored or CartEventKind.Wrecked or CartEventKind.OnFoot or CartEventKind.Departed:
                _tagStop = _tagWave = -1;
                ReleaseRunners(e.Kind.ToString(), untag: true);
                break;
        }
    }

    /// <summary>Every runner to the chase (<paramref name="why"/>); <paramref name="untag"/>: every hijacker's tag goes too (the cull may take them).</summary>
    private void ReleaseRunners(string why, bool untag)
    {
        var n = _runners.Count;
        foreach (var r in _runners)
        {
            var e = r.Enemy;
            if (!IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion()) continue;
            if (!e.Carried && !e.Leaping && e.State is not ChaseState) e.ChangeState(new ChaseState());
            HijackLog.Add((PhysicsNow, "released", -1, -1, e));
        }
        _runners.Clear();
        var tags = _tagged.Count;
        if (untag)
        {
            foreach (var e in _tagged) if (IsInstanceValid(e)) HijackLog.Add((PhysicsNow, "untagged", -1, -1, e));
            _tagged.Clear();
        }
        if (n > 0 || untag && tags > 0) GD.Print($"[hijack] {n} runner(s) released ({why}){(untag ? $", {tags} hijacker(s) untagged" : ", still tagged")}");
    }

    /// <summary>The runners run now (<see cref="HijackRules.MayRun"/> on the train as it stands).</summary>
    private bool MayRunNow()
    {
        var stop = _rails.HijackLive ? _rails.HijackStop : _rails.StopIndex;
        var held = _rails.State.Mode == CartMode.Held && !_rails.Halted && !_rails.StandingPast;
        return HijackRules.MayRun(stop, _rails.StopS.Length, _rails.Hijack, held, _rails.HeroesSeatedInTrain(), _rails.InRiseGrace);
    }

    /// <summary>
    /// Each physics tick (server and solo): the 20 s clock, the runners' climbs (every tick), and their running (decided at 10 Hz). The brake, the
    /// return or a got-away release them (still tagged).
    /// </summary>
    private void TickRunners(float dt)
    {
        if (!float.IsPositiveInfinity(_sinceHijackOver)) _sinceHijackOver += dt;
        if (_runners.Count == 0) return;
        if (_rails.Hijack is HijackPhase.Braking or HijackPhase.Returning or HijackPhase.Gone) { ReleaseRunners($"the train's {_rails.Hijack}", untag: false); return; }
        _runClock += dt;
        var decide = _runClock >= RunEvery - 1e-4f;
        if (decide) _runClock = 0f;
        var mayRun = MayRunNow();
        var cartFree = decide && mayRun && CartFree();
        for (var i = _runners.Count - 1; i >= 0; i--)
        {
            if (i >= _runners.Count) continue;
            var r = _runners[i];
            var e = r.Enemy;
            if (!IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion()) { _runners.RemoveAt(i); continue; }
            if (e.Carried || e.Leaping) { _runners.RemoveAt(i); continue; } // something else has it
            r.SinceHit += dt;
            if (r.SinceHit < HijackRules.FightBackSeconds) continue; // fighting its attacker (Ruling 9)
            if (!mayRun)
            {
                if (r.Climbing) r.Climb = float.NaN;
                if (e.State is RunForCartState) e.ChangeState(new ChaseState()); // a hero sat down, or a rise's grace: it fights meanwhile
                continue;
            }
            if (r.Climbing) { TickClimb(r, i, dt); continue; }
            if (decide) Choose(r, cartFree && !OtherClimbs(r, _rails.Cart));
            if (r.Target != null && IsInstanceValid(r.Target) && FlatReach(r.Target, e.GlobalPosition) <= HijackRules.ClimbReach) BeginClimb(r);
        }
    }

    /// <summary>The cart may be had: no driver, no hijack, and a hijack may start now (<see cref="RailController.HijackMayStart"/>).</summary>
    private bool CartFree() => _rails.Cart.Driver == null && !_rails.HijackLive && _rails.HijackMayStart();

    private bool OtherClimbs(Runner me, Node3D target)
    {
        foreach (var r in _runners) if (r != me && r.Climbing && r.Target == target) return true;
        return false;
    }

    private bool OtherWants(Runner me, Node3D target)
    {
        foreach (var r in _runners) if (r != me && r.Target == target) return true;
        return false;
    }

    /// <summary>
    /// A runner's target at a decision (Ruling 25): the cart while it is free, else the nearest coupled wagon with a free boarder spot, a crewed
    /// one first (one another runner already goes for last); none: it fights. It runs for the target's side nearer a walkable cell beside the lane.
    /// </summary>
    private void Choose(Runner r, bool cartFree)
    {
        var e = r.Enemy;
        Node3D? target = cartFree ? _rails.Cart : null;
        if (target == null)
        {
            TrainWagon? best = null;
            var bestKey = (Taken: true, Crewless: true, D: float.MaxValue);
            foreach (var w in _rails.Train)
            {
                if (!IsInstanceValid(w) || !w.Coupled || !SpotFree(w) || OtherClimbs(r, w)) continue;
                var key = (Taken: OtherWants(r, w), Crewless: CrewIn(w) == 0, D: e.GlobalPosition.DistanceTo(w.GlobalPosition));
                if (best == null || key.CompareTo(bestKey) < 0) { best = w; bestKey = key; }
            }
            target = best;
        }
        if (target == null)
        {
            r.Target = null;
            if (e.State is RunForCartState) e.ChangeState(new ChaseState()); // nothing to board: it fights where it is
            return;
        }
        var right = target.GlobalTransform.Basis.X with { Y = 0f };
        right = right.LengthSquared() > 1e-4f ? right.Normalized() : Vector3.Right;
        var beside = _rails.StandBesideLane(target.GlobalPosition) - target.GlobalPosition;
        var sign = Mathf.Abs(beside.Dot(right)) > 0.2f ? Mathf.Sign(beside.Dot(right)) : Mathf.Sign((e.GlobalPosition - target.GlobalPosition).Dot(right));
        r.Side = right * (sign == 0f ? 1f : sign);
        if (r.Target != target) GD.Print($"[hijack] {e.Name} runs for {TargetName(target)} ({e.GlobalPosition.DistanceTo(target.GlobalPosition):0.0} m)");
        r.Target = target;
        if (e.State is not RunForCartState) e.ChangeState(new RunForCartState(r.Goal));
    }

    private static bool SpotFree(TrainWagon w) => w.Boarders[0] is not { } b || !IsInstanceValid(b) || b.IsQueuedForDeletion() || !b.IsAlive;

    private static int CrewIn(TrainWagon w)
    {
        var n = 0;
        foreach (var rider in w.Riders.Values) if (rider is Npc { Crew: true, Dead: false } npc && IsInstanceValid(npc)) n++;
        return n;
    }

    private void BeginClimb(Runner r)
    {
        r.Climb = 0f;
        var what = r.Target is TrainWagon w ? $"wagon {w.Number}" : "the cart";
        GD.Print($"[hijack] {r.Enemy.Name} climbs into {TargetName(r.Target)}");
        HijackLog.Add((PhysicsNow, r.Target is TrainWagon ? $"climbs {what}" : "climbs the cart", -1, -1, r.Enemy));
    }

    /// <summary>A climb under way: lost when the target goes, is taken, or rolls out of reach; at <see cref="HijackRules.ClimbSeconds"/> the climber is aboard.</summary>
    private void TickClimb(Runner r, int index, float dt)
    {
        var e = r.Enemy;
        var target = r.Target;
        var lost = target == null || !IsInstanceValid(target) || target is TrainWagon { Coupled: false } || target is TrainWagon w0 && !SpotFree(w0)
                   || target is MineCart && (_rails.Cart.Driver != null || _rails.HijackLive) || FlatReach(target, e.GlobalPosition) > HijackRules.ClimbReach + ClimbSlack;
        if (lost)
        {
            r.Climb = float.NaN;
            GD.Print($"[hijack] {e.Name}'s climb into {TargetName(target)} is lost");
            HijackLog.Add((PhysicsNow, "climb lost", -1, -1, e));
            return;
        }
        r.Climb += dt;
        if (r.Climb < HijackRules.ClimbSeconds - 1e-4f) return;
        var aboard = target is MineCart ? _rails.TakeDriver(e) : target is TrainWagon w && Encounters is { } enc && enc.BoardWagonAtStop(e, w);
        if (!aboard)
        {
            r.Climb = float.NaN;
            r.Target = null;
            GD.Print($"[hijack] {e.Name} could not get into {TargetName(target)}: it runs on");
            HijackLog.Add((PhysicsNow, "climb lost", -1, -1, e));
            return;
        }
        _runners.RemoveAt(index);
        HijackLog.Add((PhysicsNow, target is TrainWagon tw ? $"boards wagon {tw.Number}" : "driver", -1, -1, e));
    }

    private static string TargetName(Node3D? target) => target is TrainWagon w ? $"wagon {w.Number}" : target is MineCart ? "the cart" : "nothing";

    /// <summary>How far <paramref name="at"/> stands from <paramref name="body"/>'s tub, flat (across and along it, the height left out).</summary>
    private static float FlatReach(Node3D body, Vector3 at)
    {
        var local = body.GlobalTransform.AffineInverse() * at;
        var dx = Mathf.Max(0f, Mathf.Abs(local.X) - MineCart.TubWidth / 2f);
        var dz = Mathf.Max(0f, Mathf.Abs(local.Z) - CartRules.HalfLength);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---- test hooks ------------------------------------------------------------------------------

    /// <summary>
    /// Test hook (server and solo; the flows and the RIDE hook): a live siege creature of an allowed kind on its feet and not yet tagged, the
    /// nearest the cart, is tagged now and returned; with none, the next siege spawn of an allowed kind is tagged whatever its wave (and null is
    /// returned). Nothing while <see cref="DebugTools.NoHijack"/> is set.
    /// </summary>
    internal Enemy? DebugTagNext()
    {
        if (NetSession.IsClient || DebugTools.Instance?.NoHijack == true) return null;
        if (Portals is { } portals)
        {
            Enemy? best = null;
            var bestD = float.MaxValue;
            foreach (var e in Enemy.Live)
            {
                if (!IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion() || e.Carried || e.Leaping || _tagged.Contains(e) || !portals.IsSiegeCreature(e) || !HijackRules.MayTagKind(e.Profile.Id)) continue;
                var d = e.GlobalPosition.DistanceSquaredTo(_rails.Cart.GlobalPosition);
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best != null) { Tag(best, portals.SiegeStop, portals.SiegeWaves, " (the test's, now)"); return best; }
        }
        _debugTagsDue++;
        GD.Print("[hijack] test: the next siege spawn of a cinder or a hound is tagged");
        return null;
    }
}
