using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 11's broken bridge (<see cref="RailLine.Break"/>, Rulings 11, 12 and 18): on its ride the slag line crosses a strip of chasm on a
/// deck, but for one 4 m gap. Every role builds the same from the layout before the set pieces: the enemy track is cut bare over the gap
/// and the <see cref="BrokenBridge"/> dressing stands at its near edge. The server judges it: the party's train still across on the slag line
/// as its front reaches the gap falls (<see cref="RailController.Fall"/>); a ride's slag cart or the traffic cart whose front reaches it on
/// the enemy track drops through, its riders lost to the crevasse (<see cref="CrevasseWatch.Take"/>), quietly (<see cref="SlagCart.PlungeThrough"/>:
/// a line in the log only with a hero near, <see cref="SwitchBackRules.PlungeTold"/>). Traffic started with the party's cart past the gap starts
/// past its far edge (<see cref="SwitchBackRules.TrafficStartS"/>), so the stops beyond keep their traffic. A ride where a correct switch_out
/// could still leave the train across at the gap has the fall disarmed (<see cref="GuardBreak"/>), and the watchdog never grinds a train across
/// the gap (<see cref="WatchdogBreak"/>).
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The party lane's S beside the gap's near edge, or NaN on a floor without a broken bridge.</summary>
    public float BreakGapS { get; private set; } = float.NaN;
    /// <summary>The enemy track's S of the gap's near edge (where a cart on the slag line drops), or NaN.</summary>
    internal float BreakGapEnemyS { get; private set; } = float.NaN;
    /// <summary>The enemy track's S of the gap's far edge, or NaN.</summary>
    internal float BreakGapFarEnemyS { get; private set; } = float.NaN;
    /// <summary>The broken bridge's ride (1-based), or −1 without one.</summary>
    public int BreakRide { get; private set; } = -1;
    /// <summary>Carts (slag and traffic) that dropped through the gap on this floor build (tests read it).</summary>
    internal int BreakDrops { get; private set; }
    /// <summary>The train falls at the gap (<see cref="TickBreak"/>): false on a ride where a correct switch_out could still meet it across (<see cref="GuardBreak"/>).</summary>
    internal bool BreakArmed { get; private set; } = true;

    /// <summary>
    /// The broken bridge on this floor, if the layout has one (every role, before the set pieces): the gap's S on both lanes, the enemy
    /// track cut bare over [near edge, far edge], and the dressing at the near edge.
    /// </summary>
    private void BuildBreak(Node3D root)
    {
        if (_layout.Rail is not { Break: { } brk } rail || !_enemy.Valid || !IsInstanceValid(_rails.Party.Track.Track)) return;
        if (brk.Gap < 0 || brk.Gap >= rail.Party.Count) return;
        var gapCell = EnemyTwin(rail.Party[brk.Gap]);
        if (gapCell is not { } gap || !_layout.IsChasm(gap) || _layout.IsBridge(gap))
        {
            GD.PushError($"[rails] ERROR: the broken bridge on ride {brk.Ride} has no gap cell on the slag line beside party lane {brk.Gap} {rail.Party[brk.Gap]}");
            return;
        }
        var half = DungeonBuilder.CellSize / 2f;
        var partyS = _rails.Party.Track.ClosestS(_builder.Surface(rail.Party[brk.Gap])) - half;
        var enemyMid = _enemy.ClosestS(_builder.Surface(gap));
        float near = enemyMid - half, far = enemyMid + half;
        BreakRide = brk.Ride;
        BreakGapS = partyS;
        BreakGapEnemyS = near;
        BreakGapFarEnemyS = far;
        _enemy.Track.Cut(new[] { (near, far) }); // the slag line's rails, sleepers and gravel end at the gap's lip and start again past it
        var dressing = new BrokenBridge { Name = $"BrokenBridge_r{brk.Ride}", GapLength = far - near, InnerSide = InnerSideAt(near) };
        root.AddChild(dressing);
        dressing.GlobalTransform = _enemy.Frame(near).Orthonormalized(); // −Z along the line: across the gap
        var timbers = ClearTimberOver(root, gap);
        var decks = 0;
        for (var i = brk.Run.From; i <= brk.Run.To; i++)
            if (EnemyTwin(rail.Party[i]) is { } twin && _layout.Bridges.TryGetValue(twin, out var kind) && kind == BridgeKind.Deck) decks++;
        GD.Print($"[rails] broken bridge on ride {brk.Ride}: gap at party S={partyS:0.0} (enemy S={near:0.0}), deck {decks} cells{(timbers > 0 ? $", {timbers} timber part(s) over the gap taken down" : "")}");
    }

    /// <summary>A timber frame the line's dressing stood on the gap's cell would hang over the drop: its posts and cap are taken down. Returns how many parts.</summary>
    private int ClearTimberOver(Node3D root, GridPos cell)
    {
        if (root.GetNodeOrNull<Node3D>("Rails/Timbering") is not { } frames) return 0;
        var floor = _builder.Surface(cell).Y;
        const float inset = 0.05f;
        float x0 = cell.X * DungeonBuilder.CellSize + inset, x1 = (cell.X + 1) * DungeonBuilder.CellSize - inset;
        float z0 = cell.Y * DungeonBuilder.CellSize + inset, z1 = (cell.Y + 1) * DungeonBuilder.CellSize - inset;
        var n = 0;
        foreach (var child in frames.GetChildren())
        {
            if (child is not MeshInstance3D part || !part.Visible) continue;
            var p = part.GlobalPosition;
            if (p.X <= x0 || p.X >= x1 || p.Z <= z0 || p.Z >= z1 || p.Y < floor - 0.5f || p.Y > floor + DungeonBuilder.LevelHeight) continue; // this level's only
            part.Visible = false; // with the mine pack, its model is its child: hidden with it
            n++;
        }
        return n;
    }

    /// <summary>The slag-line cell beside party-lane cell <paramref name="party"/> (its 4-neighbour on the enemy lane), or null.</summary>
    private GridPos? EnemyTwin(GridPos party)
    {
        foreach (var n in party.Neighbours4())
            if (_layout.IsRail(n) && !_layout.IsPartyRail(n)) return n;
        return null;
    }

    /// <summary>
    /// Server, each tick after the cart stepped: the party's train, rolling and still across on the slag line at the gap
    /// (<see cref="RailController.LaneBlendAt"/> ≥ 0.5 there), whose front reached the gap's near edge this step falls (Ruling 12).
    /// </summary>
    private void TickBreak()
    {
        if (float.IsNaN(BreakGapS) || !BreakArmed || _rails.State.Mode != CartMode.Rolling) return;
        var run = _rails.Party;
        var prevFront = BarricadeRules.FrontOf(run.Dir, run.PrevS);
        if (!SwitchBackRules.Falls(prevFront, run.FrontS, BreakGapS, _rails.LaneBlendAt(BreakGapS))) return;
        var front = run.FrontS;
        _rails.Fall("The slag line ends at the broken bridge!");
        GD.Print($"[rails] the cart runs off the broken bridge at S={front:0.0}");
    }

    /// <summary>
    /// The guard (every role, after the set pieces): on the break's ride, every switch_out short of the gap must have a switch back after it short
    /// of the gap, and every such back must bring the cart wholly home before the gap (<see cref="SwitchBackRules.FallArmed"/>): any of them may
    /// take it home, a fixed one always, a live one if thrown. Otherwise a correct choice could fall, or a thrown return show what it cannot do,
    /// so the gap's fall is disarmed for that ride (an error in the log: the plan is wrong).
    /// </summary>
    private void GuardBreak()
    {
        BreakArmed = true;
        if (float.IsNaN(BreakGapS)) return;
        var outs = new List<float>();
        var backs = new List<float>();
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != BreakRide) continue;
            if (p.Plan.Kind == "switch_out") outs.Add(p.S);
            else if (p.Plan.Kind == "switch_back") backs.Add(p.S);
        }
        BreakArmed = SwitchBackRules.FallArmed(outs, backs, BreakGapS);
        var switches = $"switch_out at [{string.Join(", ", outs.Select(s => s.ToString("0.0")))}], switch_back at [{string.Join(", ", backs.Select(s => s.ToString("0.0")))}]";
        if (BreakArmed) GD.Print($"[rails] broken bridge on ride {BreakRide}: its fall armed ({switches}; every switch_out short of the gap at S={BreakGapS:0.0} has its switch back home before it)");
        else GD.PushError($"[rails] ERROR: broken bridge on ride {BreakRide}: a switch_out short of the gap at S={BreakGapS:0.0} has no switch back home before it, or one too near the gap ({switches}): the gap's fall is disarmed on ride {BreakRide}, so a correct choice cannot fall");
    }

    /// <summary>
    /// The watchdog's view of the broken bridge (server, <see cref="RailController"/>'s grind), all front S: with the break's ride armed, its fall
    /// armed and the train across at the gap as it stands (<see cref="RailController.LaneBlendAt"/> ≥ 0.5 there), the gap; for the nearest switch
    /// back of that ride short of the gap the front has not reached, live or fixed (final review I1: a fixed one takes the cart home too), its
    /// points, and, while a live one's lever is straight, where short of them it frees the cart (<see cref="SwitchBackRules.GrindBackTo"/>: the
    /// seated window's start or the last call's mark). NaN for what is not there.
    /// </summary>
    internal (float BackWindow, float BackPoints, float Gap) WatchdogBreak()
    {
        if (float.IsNaN(BreakGapS) || !BreakArmed || ArmedRide != BreakRide || _rails.LaneBlendAt(BreakGapS) < 0.5f) return (float.NaN, float.NaN, float.NaN);
        var front = _rails.Party.FrontS;
        Piece? back = null;
        foreach (var p in _pieces)
            if (p.Plan.Kind == "switch_back" && p.Plan.Ride == BreakRide && !p.Fired && p.S > front && p.S < BreakGapS && (back == null || p.S < back.S)) back = p;
        if (back?.Switch is not { } sw) return (float.NaN, back?.S ?? float.NaN, BreakGapS);
        var to = SwitchBackRules.GrindBackTo(front, sw.LeverS, back.S, sw.Fixed || sw.Thrown);
        return (to < back.S ? to : float.NaN, back.S, BreakGapS);
    }

    /// <summary>
    /// A slag or traffic cart after its step: its front reached the gap on the enemy track this step (it runs toward higher S). Its living
    /// riders are lost to the crevasse, then it plunges through quietly (<see cref="SlagCart.PlungeThrough"/>: every rider laid hidden in the
    /// dark under the gap, nothing left on the line or over the drop; no derail line, taunt or tremor). A hero within
    /// <see cref="SwitchBackRules.PlungeHeard"/> of the gap reads it in the log. True when it dropped: the caller forgets it.
    /// </summary>
    private bool DropsThrough(SlagCart cart)
    {
        if (float.IsNaN(BreakGapEnemyS) || !IsInstanceValid(cart) || cart.Derailed) return false;
        var run = cart.Run;
        if (run.State.Mode != CartMode.Rolling || run.Dir < 0) return false;
        if (!SwitchBackRules.Falls(BarricadeRules.FrontOf(run.Dir, run.PrevS), run.FrontS, BreakGapEnemyS, 1f)) return false; // on its own track: always on the slag line
        var watch = CrevasseWatch.Instance;
        var lost = 0;
        foreach (var rider in cart.Aboard)
        {
            if (!IsInstanceValid(rider) || rider.IsQueuedForDeletion() || !rider.IsAlive) continue;
            if (watch != null && IsInstanceValid(watch)) watch.Take(rider, rider.GlobalPosition);
            else rider.KillByEnvironment("The crevasse", HazardDamage.Fall); // a broken bridge is cut only on a crevasse floor: never without a watch
            lost++;
        }
        var nearest = NearestHeroTo(_enemy.Point(BreakGapEnemyS + DungeonBuilder.CellSize / 2f));
        var told = SwitchBackRules.PlungeTold(nearest);
        if (told) EventBus.Instance.Log("The slag cart plunges through the broken bridge.");
        GD.Print($"[rails] {cart.Name} drops through the broken bridge at enemy S={run.FrontS:0.0}: {lost} rider(s) lost; nearest hero {nearest:0.0} m{(told ? ", told" : ", unseen")}");
        BreakDrops++;
        cart.PlungeThrough(UnderTheGap());
        if (IsInstanceValid(cart) && !cart.IsQueuedForDeletion()) cart.QueueFree(); // a plunge refused (it was not in the tree): freed all the same
        return true;
    }

    /// <summary>Where the broken bridge's dead lie: the middle of its gap cell, <see cref="CrevasseRules.HoldDepth"/> below the lip, out of sight (every role reads the same).</summary>
    internal Vector3 UnderTheGap()
    {
        var gapCell = DungeonBuilder.WorldToCell(_enemy.Point(BreakGapEnemyS + DungeonBuilder.CellSize / 2f));
        return _builder.Surface(gapCell) + Vector3.Down * CrevasseRules.HoldDepth;
    }

    /// <summary>Client: a slag or traffic cart the server took off the line at enemy S <paramref name="cartS"/> went through this floor's gap (<see cref="SwitchBackRules.PlungedAt"/>).</summary>
    internal bool PlungedAt(float cartS) => SwitchBackRules.PlungedAt(cartS, BreakGapEnemyS);

    /// <summary>The straight distance from <paramref name="at"/> to the nearest hero in the world (a remote player's body too), or +∞.</summary>
    private static float NearestHeroTo(Vector3 at)
    {
        var best = float.PositiveInfinity;
        if (Main.Instance?.Party is not { } party) return best;
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && !hero.Parked) best = Mathf.Min(best, hero.GlobalPosition.DistanceTo(at));
        return best;
    }

    /// <summary>Server test hook: a traffic cart starts on the enemy line at <paramref name="enemyS"/>, at cruise (one already there goes first).</summary>
    internal void DebugSpawnTrafficAt(float enemyS)
    {
        if (NetSession.IsClient || !_enemy.Valid) return;
        FreeTraffic();
        SpawnTraffic(Mathf.Clamp(enemyS, 0f, Mathf.Max(0f, _enemy.Length - LineEnd - 1f)), CartRules.Cruise);
    }
}
