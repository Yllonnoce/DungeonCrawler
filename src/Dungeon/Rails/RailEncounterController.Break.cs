using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 11's broken bridge (<see cref="RailLine.Break"/>, Rulings 11, 12 and 18): on its ride the slag line crosses a strip of chasm on a
/// deck, but for one 4 m gap. Every role builds the same from the layout before the set pieces: the enemy track is cut bare over the gap
/// and the <see cref="BrokenBridge"/> dressing stands at its near edge. The server judges it: the party's train still across on the slag line
/// as its front reaches the gap falls (<see cref="RailController.Fall"/>); a ride's slag cart or the traffic cart whose front reaches it on
/// the enemy track drops through, its riders lost to the crevasse (<see cref="CrevasseWatch.Take"/>), and is derailed and freed.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The party lane's S beside the gap's near edge, or NaN on a floor without a broken bridge.</summary>
    public float BreakGapS { get; private set; } = float.NaN;
    /// <summary>The enemy track's S of the gap's near edge (where a cart on the slag line drops), or NaN.</summary>
    internal float BreakGapEnemyS { get; private set; } = float.NaN;
    /// <summary>The broken bridge's ride (1-based), or −1 without one.</summary>
    public int BreakRide { get; private set; } = -1;
    /// <summary>Carts (slag and traffic) that dropped through the gap on this floor build (tests read it).</summary>
    internal int BreakDrops { get; private set; }

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
        if (float.IsNaN(BreakGapS) || _rails.State.Mode != CartMode.Rolling) return;
        var run = _rails.Party;
        var prevFront = BarricadeRules.FrontOf(run.Dir, run.PrevS);
        if (!SwitchBackRules.Falls(prevFront, run.FrontS, BreakGapS, _rails.LaneBlendAt(BreakGapS))) return;
        var front = run.FrontS;
        _rails.Fall("The slag line ends at the broken bridge!");
        GD.Print($"[rails] the cart runs off the broken bridge at S={front:0.0}");
    }

    /// <summary>
    /// A slag or traffic cart after its step: its front reached the gap on the enemy track this step (it runs toward higher S). Its riders
    /// are lost to the crevasse and laid in the dark under the gap (nothing is left on the line or over the drop), then it derails and is
    /// freed. True when it dropped: the caller forgets it.
    /// </summary>
    private bool DropsThrough(SlagCart cart)
    {
        if (float.IsNaN(BreakGapEnemyS) || !IsInstanceValid(cart) || cart.Derailed) return false;
        var run = cart.Run;
        if (run.State.Mode != CartMode.Rolling || run.Dir < 0) return false;
        if (!SwitchBackRules.Falls(BarricadeRules.FrontOf(run.Dir, run.PrevS), run.FrontS, BreakGapEnemyS, 1f)) return false; // on its own track: always on the slag line
        var watch = CrevasseWatch.Instance;
        var lost = 0;
        var gapCell = DungeonBuilder.WorldToCell(_enemy.Point(BreakGapEnemyS + DungeonBuilder.CellSize / 2f));
        var dark = _builder.Surface(gapCell) + Vector3.Down * CrevasseRules.HoldDepth; // under the gap, out of sight below its lip
        foreach (var rider in cart.Aboard)
        {
            if (!IsInstanceValid(rider) || rider.IsQueuedForDeletion()) continue;
            if (rider.IsAlive)
            {
                if (watch != null && IsInstanceValid(watch)) watch.Take(rider, rider.GlobalPosition);
                else rider.KillByEnvironment("The crevasse", HazardDamage.Fall); // a broken bridge is cut only on a crevasse floor: never without a watch
                lost++;
            }
            if (rider.Carrier == cart) rider.Drop(dark); // the derail sets down only those it still carries
            rider.Visible = false;
        }
        GD.Print($"[rails] {cart.Name} drops through the broken bridge at enemy S={run.FrontS:0.0}: {lost} rider(s) lost");
        BreakDrops++;
        cart.Derail();
        if (IsInstanceValid(cart) && !cart.IsQueuedForDeletion()) cart.QueueFree(); // a derail refused (it was not in the tree): freed all the same
        return true;
    }

    /// <summary>Server test hook: a traffic cart starts on the enemy line at <paramref name="enemyS"/>, at cruise (one already there goes first).</summary>
    internal void DebugSpawnTrafficAt(float enemyS)
    {
        if (NetSession.IsClient || !_enemy.Valid) return;
        FreeTraffic();
        SpawnTraffic(Mathf.Clamp(enemyS, 0f, Mathf.Max(0f, _enemy.Length - LineEnd - 1f)), CartRules.Cruise);
    }
}
