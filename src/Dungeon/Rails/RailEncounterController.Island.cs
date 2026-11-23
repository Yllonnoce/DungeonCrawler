using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 15's exit island at runtime (spec B.2; Rulings 9-11): the exit bridge, the last straight party-lane run before the exit platform,
/// is chasm with its track kept. Every role builds it from the layout (<see cref="BuildExitBridge"/>): both tracks keep their rails and
/// sleepers over it with the gravel taken off (<see cref="Props.RailTrack.OmitBallast"/>), and an open trestle (<see cref="ExitBridge"/>)
/// stands under them in the lava. While the party's train rolls with any car over it nobody gets out and nobody moves car to car
/// (<see cref="ExitBridgeHolds"/>, the second term of <see cref="HoldsRiders"/>); a wreck at or past its start sets everyone down inside
/// (<see cref="IslandSetDown"/>, <see cref="RailController"/>'s wreck).
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The exit bridge's party S span (its first cell's near edge to its last cell's far edge), NaN on a floor without an island.</summary>
    public float ExitFromS { get; private set; } = float.NaN;
    public float ExitToS { get; private set; } = float.NaN;
    /// <summary>The enemy bridge's span on the enemy track (its twins of the party bridge's straight cells), NaN without an island.</summary>
    public float ExitEnemyFromS { get; private set; } = float.NaN;
    public float ExitEnemyToS { get; private set; } = float.NaN;
    /// <summary>The exit bridge's party cells and enemy cells (0 without an island).</summary>
    internal int ExitCells { get; private set; }
    internal int ExitEnemyCells { get; private set; }

    /// <summary>
    /// The exit bridge (every role, after the broken bridge and before the set pieces): each track's span over its bridge cells, the gravel
    /// taken off both spans (rails and sleepers kept, the curve, S and gaps unchanged) and, drawn, the open trestle under them down to the lava.
    /// </summary>
    private void BuildExitBridge(Node3D root)
    {
        if (_layout.Rail is not { Island: { } island } rail || !_enemy.Valid || !IsInstanceValid(_rails.Party.Track.Track)) return;
        var party = _rails.Party.Track;
        var partyCells = new List<GridPos>();
        for (var i = island.Bridge.From; i <= island.Bridge.To; i++) partyCells.Add(rail.Party[i]);
        var (from, to) = SpanOf(party, partyCells);
        var (enemyFrom, enemyTo) = SpanOf(_enemy, island.EnemyBridge);
        if (float.IsNaN(from) || float.IsNaN(enemyFrom))
        {
            GD.PushError($"[rails] ERROR: the exit bridge's cells {island.Bridge.From}..{island.Bridge.To} give no span on {(float.IsNaN(from) ? "the party" : "the enemy")} track");
            return;
        }
        ExitFromS = from;
        ExitToS = to;
        ExitEnemyFromS = enemyFrom;
        ExitEnemyToS = enemyTo;
        ExitCells = partyCells.Count;
        ExitEnemyCells = island.EnemyBridge.Count;
        party.Track.OmitBallast(new[] { (from, to) });
        _enemy.Track.OmitBallast(new[] { (enemyFrom, enemyTo) });
        if (ChasmDressing.DrawsLook)
        {
            var look = new ExitBridge { Name = "ExitBridge" };
            root.AddChild(look);
            look.Build(party, _enemy, (from, to), (enemyFrom, enemyTo), _builder.Surface(partyCells[0]).Y - ExitBridgeRules.LavaDepth);
        }
        GD.Print($"[rails] exit bridge: party S={from:0.0}..{to:0.0} ({ExitCells} cells), enemy S={enemyFrom:0.0}..{enemyTo:0.0} ({ExitEnemyCells} cells); rails kept, no deck, no footing");
    }

    /// <summary>
    /// The span <paramref name="track"/> runs over <paramref name="cells"/>: from the lowest cell centre's S less half a cell to the highest's
    /// plus half (the cells are one straight run). NaN when there are none or the track is gone.
    /// </summary>
    private (float From, float To) SpanOf(TrackSampler track, IReadOnlyList<GridPos> cells)
    {
        if (cells.Count == 0 || !track.Valid) return (float.NaN, float.NaN);
        var mids = cells.Select(c => track.ClosestS(_builder.Surface(c))).ToList();
        var half = DungeonBuilder.CellSize / 2f;
        return (mids.Min() - half, mids.Max() + half);
    }

    /// <summary>The party's train holds its riders on the exit bridge (<see cref="ExitBridgeRules.Holds"/>): Rolling, any car over it. Every role, from this peer's own cart.</summary>
    public bool ExitBridgeHolds()
    {
        if (float.IsNaN(ExitFromS) || !IsInstanceValid(_rails)) return false;
        return ExitBridgeRules.Holds(_rails.State.Mode == CartMode.Rolling, _rails.Party.FrontS, TrainRearS, ExitFromS, ExitToS);
    }

    /// <summary>
    /// <paramref name="count"/> places a wreck inside sets its riders down on (Ruling 11): from <see cref="CrevasseRules.SetDownCells"/> round
    /// the party lane's first cell past the bridge, island cells only, never a drop, the cells off the rails first; when too few, the rest
    /// share that lane cell. Each is the cell's floor point.
    /// </summary>
    internal List<Vector3> IslandSetDown(int count)
    {
        var spots = new List<Vector3>(Math.Max(0, count));
        if (_layout.Rail is not { Island: { } island } rail || count <= 0) return spots;
        var near = rail.Party[Math.Min(island.Bridge.To + 1, rail.Party.Count - 1)];
        var found = CrevasseRules.SetDownCells(_layout, near, 81)
            .Where(c => island.Cells.Contains(c) && CrevasseRules.SetDownOk(_layout, c)).Distinct()
            .OrderBy(c => _layout.IsRail(c) ? 1 : 0).ToList(); // a stable sort: nearest first within each
        for (var i = 0; i < count; i++) spots.Add(_builder.Surface(i < found.Count ? found[i] : near));
        return spots;
    }
}
