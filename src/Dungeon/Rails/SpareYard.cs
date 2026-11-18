using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The spare carts, owned by the RailController: one stand-in cart beside each intermediate stop's platform, for looks
/// only. When the party's cart is wrecked, <see cref="Pick"/> (by <see cref="SpareRules"/>) names the stop whose spare
/// takes over and <see cref="Use"/> spends it (it vanishes). Which are spent is kept for the save. The server tells "used" and
/// "restored" ("spare:{stop}"); a client's yard shows them (<see cref="Show"/>).
/// </summary>
public sealed class SpareYard
{
    private readonly DungeonBuilder _builder;
    private readonly FloorLayout _layout;
    private readonly RailTrack _track;
    /// <summary>Cells nobody is set down on (the train's sidings): a spare takes one too.</summary>
    private readonly HashSet<GridPos> _sidings;
    private readonly Dictionary<int, MineCart> _spares = new();
    private readonly HashSet<int> _used = new();

    public SpareYard(DungeonBuilder builder, FloorLayout layout, RailTrack track, HashSet<GridPos> sidings)
    {
        _builder = builder;
        _layout = layout;
        _track = track;
        _sidings = sidings;
    }

    /// <summary>Intermediate stops (indices) whose spare has been used, for the save.</summary>
    public IReadOnlyCollection<int> Used => _used;
    /// <summary>Spares still parked.</summary>
    public int Left => _spares.Count - _used.Count;

    /// <summary>One stand-in cart per intermediate stop, on a free cell of the stop's room beside the platform (never a rail, a siding or a blocked cell), facing along the track. It is looks only: no prompt, never boarded.</summary>
    public void Build(Node3D root, int stopCount)
    {
        if (_layout.Rail is not { } rail) return;
        for (var i = 1; i < stopCount - 1 && i < rail.Stops.Count; i++)
        {
            var stop = rail.Stops[i];
            if (stop.Platform.Count == 0 || stop.Room < 0 || stop.Room >= _layout.Rooms.Count) continue;
            var free = Adventure.RoomCells.Free(_layout, _layout.Rooms[stop.Room]);
            var mid = stop.Platform[stop.Platform.Count / 2];
            GridPos? best = null, beside = null;
            var bestKey = (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue); // a platform cell only when nothing beside it is free
            foreach (var c in free)
            {
                if (_layout.IsRail(c) || _builder.IsCellBlocked(c) || _sidings.Contains(c)) continue;
                GridPos? near = null;
                var nearD = int.MaxValue;
                foreach (var p in stop.Platform)
                {
                    var d = (p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y);
                    if (d < nearD) { nearD = d; near = p; }
                }
                if (near is not { } np || _layout.LevelOf(c) != _layout.LevelOf(np)) continue;
                var fromMid = (c.X - mid.X) * (c.X - mid.X) + (c.Y - mid.Y) * (c.Y - mid.Y);
                var key = (nearD == 0 ? 1 : 0, nearD, -fromMid, c.X, c.Y); // beside the platform, toward its end: the wagon's siding takes the middle
                if (key.CompareTo(bestKey) >= 0) continue;
                bestKey = key;
                best = c;
                beside = np;
            }
            if (best is not { } cell || beside is not { } platform) continue;
            var at = _builder.Surface(cell);
            var s = _track.Curve.GetClosestOffset(_track.ToLocal(_builder.Surface(platform)));
            var along = -(_track.GlobalTransform * _track.Curve.SampleBakedWithRotation(s, true, true)).Basis.Z;
            var basis = new Basis(Vector3.Up, Mathf.Atan2(-along.X, -along.Z));
            var spare = new MineCart { Name = $"SpareCart{i}", PromptFor = _ => "", CollisionLayer = 1 };
            root.AddChild(spare);
            spare.GlobalTransform = new Transform3D(basis, at);
            _builder.BlockCell(cell);
            _sidings.Add(cell);
            _spares[i] = spare;
        }
    }

    /// <summary>The stop whose unused spare takes over a cart wrecked at <paramref name="s"/> having last left <paramref name="stopIndex"/>, or −1 (<see cref="SpareRules.Pick"/>).</summary>
    public int Pick(IReadOnlyList<float> stopS, int stopIndex, float s) => SpareRules.Pick(stopS, _used, stopIndex, s);

    /// <summary>The stop's spare is spent: it vanishes.</summary>
    public void Use(int stop)
    {
        if (_used.Add(stop)) Tell(stop, "used");
        Hide(stop);
    }

    /// <summary>The stop's spare cart, or null (none stands there).</summary>
    public MineCart? At(int stop) => _spares.TryGetValue(stop, out var spare) && GodotObject.IsInstanceValid(spare) ? spare : null;

    /// <summary>The stop whose spare <paramref name="spare"/> is, or −1.</summary>
    public int StopOf(MineCart spare)
    {
        foreach (var (stop, cart) in _spares) if (cart == spare) return stop;
        return -1;
    }

    /// <summary>Client: the stop's spare spent (gone) or parked again, as on the server, silently. True when it changed.</summary>
    public bool Show(int stop, bool used)
    {
        if (!_spares.ContainsKey(stop) || _used.Contains(stop) == used) return false;
        if (used) { _used.Add(stop); Hide(stop); }
        else { _used.Remove(stop); Park(stop); }
        return true;
    }

    private static void Tell(int stop, string what)
    {
        if (!NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRefs.Spare(stop), what);
    }

    /// <summary>The stops whose spare cart is spent, or null when none is.</summary>
    public List<int>? Capture() => _used.Count == 0 ? null : _used.OrderBy(i => i).ToList();

    /// <summary>Back from a save: exactly the listed spares (those that exist) are spent and hidden.</summary>
    public void Restore(IEnumerable<int>? used)
    {
        var before = _used.ToList();
        _used.Clear();
        if (used != null) foreach (var i in used) if (_spares.ContainsKey(i)) { _used.Add(i); Hide(i); }
        foreach (var i in before) if (!_used.Contains(i)) Tell(i, "restored"); // (a load builds the floor afresh: its spares stand already)
        foreach (var i in _used) if (!before.Contains(i)) Tell(i, "used");
    }

    private void Hide(int stop)
    {
        if (!_spares.TryGetValue(stop, out var spare) || !GodotObject.IsInstanceValid(spare)) return;
        spare.Visible = false;
        spare.CollisionLayer = 0;
    }

    private void Park(int stop)
    {
        if (!_spares.TryGetValue(stop, out var spare) || !GodotObject.IsInstanceValid(spare)) return;
        spare.Visible = true;
        spare.CollisionLayer = 1;
    }
}
