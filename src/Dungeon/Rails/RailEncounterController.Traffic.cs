using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Traffic on a railed floor. Every <see cref="SetPieceRules.SlagTrafficEvery"/> s with no ride's slag cart live, a riderless slag
/// cart rolls the whole enemy line at cruise (at most one); every <see cref="SetPieceRules.RunawayEvery"/> s with the party's cart
/// held at a stop and nobody aboard, a <see cref="RunawayWagon"/> runs the party line from just past that stop to just short of
/// the next (at most one), halts and is freed. Both ring the bell, warn a body on their lane (<see cref="RailEncounterController"/>'s
/// Warn) and run over whoever is on it; a pocket is safe. An AI-controlled body on foot on a warned lane (a companion, a crew or
/// escorting NPC) walks to the nearest pocket of that lane within <see cref="SeekRange"/> and holds until the cart has passed, else
/// to the nearest walkable cell off the rails. Server only.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>A runaway wagon starts this far past the held cart's stop.</summary>
    public const float RunawayAhead = 6f;
    /// <summary>It halts this far short of the next stop (its gate stands just past that stop).</summary>
    public const float RunawayShort = 3f;
    /// <summary>It is freed this long after it halts.</summary>
    public const float RunawayLinger = 1f;
    /// <summary>An AI body looks for a pocket this close.</summary>
    public const float SeekRange = 12f;
    /// <summary>Only bodies this close to a cart are scanned for a refuge.</summary>
    public const float SeekScan = 30f;
    /// <summary>A held body is let go once the cart's front is this far past it.</summary>
    const float SeekPast = 2f;
    /// <summary>With no pocket near, a cell off the rails is looked for within this many cells.</summary>
    const int OffLineCells = 3;

    /// <summary>Print the warnings and refuges as they happen (the smoke tests set it).</summary>
    public static bool TraceTraffic;

    private sealed class Runaway
    {
        public CartRun Run = null!;
        public RunawayWagon Body = null!;
        public float Halted = -1f;
    }

    private sealed class Seeker
    {
        public Node3D Body = null!;
        public CartRun Run = null!;
        public Node3D Carrier = null!;
        public Vector3 Spot;
        public Vector3? PriorTarget;
        public bool PriorOverride;
    }

    private SlagCart? _traffic;
    private Runaway? _runaway;
    private float _trafficClock, _runawayClock;
    private readonly Dictionary<int, double> _warnedTraffic = new(), _warnedRunaway = new();
    private readonly Dictionary<ulong, Seeker> _seekers = new();
    private readonly List<ulong> _seekerGone = new();
    private Vector3[]? _partyPockets, _enemyPockets;

    /// <summary>The riderless slag cart on the enemy line now, or null.</summary>
    public SlagCart? TrafficCart => _traffic != null && IsInstanceValid(_traffic) && !_traffic.Derailed ? _traffic : null;
    /// <summary>The runaway wagon on the party line now (running or halted), or null.</summary>
    public RunawayWagon? RunawayCart => _runaway != null && IsInstanceValid(_runaway.Body) ? _runaway.Body : null;
    /// <summary>Seconds toward the next traffic slag cart (tests set it to start one).</summary>
    public float TrafficClock { get => _trafficClock; set => _trafficClock = value; }
    /// <summary>Seconds toward the next runaway wagon (tests set it to start one).</summary>
    public float RunawayClock { get => _runawayClock; set => _runawayClock = value; }
    /// <summary>Bodies walking to or waiting in a refuge now (tests read it).</summary>
    public int Seeking => _seekers.Count;

    // ---- clocks and spawning ---------------------------------------------------------------------

    private void TickTrafficClocks(float dt)
    {
        if (!_enemy.Valid || _rails.StopS.Length < 2) return;
        if (_traffic != null || _carts.Count > 0 || ArmedRide >= 1 || _rails.State.Mode == CartMode.Rolling) _trafficClock = 0f; // never while a ride is armed or the party's cart rolls
        else if ((_trafficClock += dt) >= SetPieceRules.SlagTrafficEvery) { _trafficClock = 0f; SpawnTraffic(); }

        var held = _rails.State.Mode == CartMode.Held && !_rails.Halted && ArmedRide < 1 && _rails.Cart.Riders.Count == 0 && _rails.StopIndex < _rails.StopS.Length - 1 && _rails.GateUp(_rails.StopIndex);
        if (!held) _runawayClock = 0f;
        else if (_runaway == null && (_runawayClock += dt) >= SetPieceRules.RunawayEvery) { _runawayClock = 0f; SpawnRunaway(); }
    }

    /// <summary>The traffic cart goes (a ride has armed).</summary>
    private void FreeTraffic()
    {
        if (_traffic == null) return;
        if (IsInstanceValid(_traffic)) Free(_traffic);
        _traffic = null;
        _warnedTraffic.Clear();
    }

    private void SpawnTraffic()
    {
        if (GetParent() is not Node3D root) return;
        var cart = new SlagCart { Name = "SlagCart_traffic", EncounterId = "traffic", InnerSide = InnerSideAt(0f), CartId = NextCartId() };
        cart.Setup(_enemy, new CartState(0f, 0f, CartMode.Rolling, CartRules.MaxPlating), 0f);
        root.AddChild(cart);
        cart.Run.PoseAt(0f);
        cart.Run.Sync();
        _traffic = cart;
        EventBus.Instance.PlaySfx("cart_bell");
        GD.Print($"[rails] t={_now:0.00} traffic slag cart enters the enemy line (party cart S={_rails.State.S:0.0})");
    }

    private void SpawnRunaway()
    {
        if (GetParent() is not Node3D root) return;
        var from = _rails.StopS[_rails.StopIndex] + RunawayAhead;
        var to = _rails.StopS[_rails.StopIndex + 1] - RunawayShort;
        if (to - from < 10f) return;
        var wagon = new RunawayWagon { CartId = NextCartId() };
        var run = new CartRun(_rails.Party.Track, wagon, new CartState(from, 0f, CartMode.Rolling, CartRules.MaxPlating), 1) { Limit = to };
        root.AddChild(wagon);
        run.PoseAt(from);
        run.Sync();
        _runaway = new Runaway { Run = run, Body = wagon };
        EventBus.Instance.PlaySfx("cart_bell");
        EventBus.Instance.Log("An ore wagon breaks loose up the line!");
        GD.Print($"[rails] t={_now:0.00} runaway wagon at party S={from:0.0}, to S={to:0.0}");
    }

    // ---- ticks -----------------------------------------------------------------------------------

    private void TickTraffic(float dt, PartyController party)
    {
        var cart = _traffic;
        if (cart == null) return;
        if (!IsInstanceValid(cart) || cart.Derailed) { _traffic = null; return; }
        var run = cart.Run;
        run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit, CartRules.Cruise), dt);
        run.Sync();
        run.PoseAt(run.State.S);
        RunOver(run, cart.HitAt, TrafficSource, SlagCart.HazardIndex, _setAside);
        Warn(run, cart, _warnedTraffic, false, party);
        if (run.State.S >= _enemy.Length - LineEnd || run.State.Mode != CartMode.Rolling)
        {
            GD.Print($"[rails] t={_now:0.00} traffic slag cart leaves the enemy line");
            Free(cart);
            _traffic = null;
            _warnedTraffic.Clear();
        }
    }

    private const string TrafficSource = "a slag cart";

    private void TickRunaway(float dt, PartyController party)
    {
        var w = _runaway;
        if (w == null) return;
        // it is gone with the floor, once the party's cart sets off or is wrecked (the line is the party's then), or after its halt
        if (!IsInstanceValid(w.Body) || _rails.State.Mode != CartMode.Held || (w.Halted >= 0f && (w.Halted += dt) >= RunawayLinger))
        {
            if (IsInstanceValid(w.Body)) w.Body.QueueFree();
            _runaway = null;
            _warnedRunaway.Clear();
            return;
        }
        var run = w.Run;
        if (w.Halted < 0f)
        {
            run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit, CartRules.Cruise), dt);
            run.Sync();
            run.PoseAt(run.State.S);
            RunOver(run, w.Body.HitAt, RunawayWagon.SourceName, RunawayWagon.HazardIndex, _setAsideParty);
            Warn(run, w.Body, _warnedRunaway, true, party);
            if (run.State.Mode != CartMode.Rolling)
            {
                w.Halted = 0f;
                GD.Print($"[rails] t={_now:0.00} runaway wagon halts at party S={run.State.S:0.0}");
            }
        }
    }

    // ---- refuge ----------------------------------------------------------------------------------

    private void EnsurePockets()
    {
        if (_partyPockets != null) return;
        var party = new List<Vector3>();
        var enemy = new List<Vector3>();
        var rail = _layout.Rail!;
        foreach (var cell in rail.Pockets)
        {
            var onParty = _layout.IsPartyRail(cell + new Dungeon.Generation.GridPos(1, 0)) || _layout.IsPartyRail(cell + new Dungeon.Generation.GridPos(-1, 0))
                || _layout.IsPartyRail(cell + new Dungeon.Generation.GridPos(0, 1)) || _layout.IsPartyRail(cell + new Dungeon.Generation.GridPos(0, -1));
            (onParty ? party : enemy).Add(_builder.Surface(cell));
        }
        _partyPockets = party.ToArray();
        _enemyPockets = enemy.ToArray();
    }

    /// <summary>
    /// <paramref name="body"/> stands in the way of <paramref name="run"/> (at <paramref name="bodyS"/>): send it to the nearest
    /// pocket of that lane within <see cref="SeekRange"/> that the cart's front has not reached, else to the nearest walkable cell
    /// off the rails, and hold it there (<see cref="TickSeekers"/> lets it go once the cart has passed).
    /// </summary>
    private void SeekRefuge(Node3D body, CartRun run, Node3D carrier, bool partyLane, float bodyS)
    {
        var id = body.GetInstanceId();
        if (_seekers.ContainsKey(id)) return;
        EnsurePockets();
        var pockets = partyLane ? _partyPockets! : _enemyPockets!;
        var pos = body.GlobalPosition;
        Vector3? best = null;
        var bestD = SeekRange * SeekRange;
        for (var i = 0; i < pockets.Length; i++)
        {
            var p = pockets[i];
            float dx = p.X - pos.X, dz = p.Z - pos.Z, d = dx * dx + dz * dz;
            if (d >= bestD || Mathf.Abs(p.Y - pos.Y) > 3f) continue;
            if (!run.Track.Measure(p, bodyS, SeekRange + 2f, out var pb) || pb.S < run.FrontS + 1.5f) continue; // its front is there already
            bestD = d;
            best = p;
        }
        var kind = "pocket";
        if (best == null && OffLine(pos) is { } off) { best = off; kind = "off the rails"; }
        if (best is not { } spot) return;
        _seekers[id] = new Seeker { Body = body, Run = run, Carrier = carrier, Spot = spot, PriorTarget = (body as Npc)?.MoveTarget, PriorOverride = (body as Npc)?.MoveOverride ?? false };
        switch (body)
        {
            case Adventurer hero: hero.SeekSpot = spot; break;
            case Npc npc: npc.MoveTarget = spot; npc.MoveOverride = true; break;
        }
        if (TraceTraffic) GD.Print($"[rails] t={_now:0.00} {body.Name} seeks a {kind} at {spot} ({new Vector2(spot.X - pos.X, spot.Z - pos.Z).Length():0.0} m from {pos}); cart front S={run.FrontS:0.0}, body S={bodyS:0.0}");
    }

    /// <summary>The nearest walkable cell within <see cref="OffLineCells"/> of <paramref name="pos"/> that is no rail cell.</summary>
    private Vector3? OffLine(Vector3 pos)
    {
        var here = DungeonBuilder.WorldToCell(pos);
        Dungeon.Generation.GridPos? best = null;
        var bestD = int.MaxValue;
        for (var dy = -OffLineCells; dy <= OffLineCells; dy++)
            for (var dx = -OffLineCells; dx <= OffLineCells; dx++)
            {
                var c = new Dungeon.Generation.GridPos(here.X + dx, here.Y + dy);
                var d = dx * dx + dy * dy;
                if (d >= bestD || !_layout.IsWalkable(c) || _layout.IsRail(c)) continue;
                bestD = d;
                best = c;
            }
        return best is { } cell ? _builder.Surface(cell) : null;
    }

    /// <summary>Everyone held in a refuge is let go (the controller is leaving).</summary>
    private void ReleaseSeekers()
    {
        foreach (var k in _seekers.Values)
        {
            if (!IsInstanceValid(k.Body)) continue;
            switch (k.Body)
            {
                case Adventurer hero: hero.SeekSpot = null; break;
                case Npc npc: npc.MoveTarget = k.PriorTarget; npc.MoveOverride = k.PriorOverride; break;
            }
        }
        _seekers.Clear();
    }

    private void TickSeekers()
    {
        if (_seekers.Count == 0) return;
        _seekerGone.Clear();
        foreach (var (id, k) in _seekers)
        {
            var body = k.Body;
            var alive = IsInstanceValid(body) && !body.IsQueuedForDeletion() && body switch
            {
                Adventurer hero => !hero.Downed && !hero.Parked && !hero.Seated,
                Npc npc => !npc.Dead && !npc.Downed && !npc.Seated,
                _ => false,
            };
            var passed = !alive;
            if (alive)
            {
                var carrierLive = IsInstanceValid(k.Carrier) && !k.Carrier.IsQueuedForDeletion() && k.Carrier is not SlagCart { Derailed: true } && k.Run.State.Mode == CartMode.Rolling;
                passed = !carrierLive || (k.Run.Track.Measure(body.GlobalPosition, k.Run.FrontS, 40f, out var b) && k.Run.FrontS > b.S + SeekPast);
            }
            if (passed) _seekerGone.Add(id);
        }
        foreach (var id in _seekerGone)
        {
            var k = _seekers[id];
            _seekers.Remove(id);
            if (!IsInstanceValid(k.Body)) continue;
            switch (k.Body)
            {
                case Adventurer hero: hero.SeekSpot = null; break;
                case Npc npc: npc.MoveTarget = k.PriorTarget; npc.MoveOverride = k.PriorOverride; break;
            }
            if (TraceTraffic) GD.Print($"[rails] t={_now:0.00} {k.Body.Name} leaves its refuge (cart front S={k.Run.FrontS:0.0})");
        }
    }
}
