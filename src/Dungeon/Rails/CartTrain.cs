using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The cage wagons coupled behind a lead cart (the party's, owned by the RailController), nearest first: posed on the line
/// behind it, told its speed and their bends, their riders seated where they stand. It also keeps the siding cells
/// (wagons and spares stand there beside the platforms) that nobody getting out is set down on.
/// </summary>
public sealed class CartTrain
{
    /// <summary>From one body's middle to the next along the train: a cart's length and a coupling's gap.</summary>
    public const float WagonSpacing = 2f * CartRules.HalfLength + 0.4f;

    private readonly CartRun _lead;
    private readonly List<CageWagon> _wagons = new();
    private readonly List<ICartRider> _riders = new();

    public CartTrain(CartRun lead) => _lead = lead;

    /// <summary>The coupled wagons, nearest the lead first.</summary>
    public IReadOnlyList<CageWagon> Wagons => _wagons;
    public int Count => _wagons.Count;

    /// <summary>Cells a cage wagon or a spare cart stands on beside a platform: nobody getting out is set down there.</summary>
    public HashSet<GridPos> Sidings { get; } = new();

    /// <summary>A siding cell: riders getting out (and enemies knocked aside) are never set down on it.</summary>
    public void ReserveSiding(GridPos cell) => Sidings.Add(cell);

    /// <summary>Couple <paramref name="wagon"/> on behind the last body of the train. It is posed behind the lead at once. False when it is gone or already coupled.</summary>
    public bool Couple(CageWagon wagon)
    {
        if (!GodotObject.IsInstanceValid(wagon) || _wagons.Contains(wagon)) return false;
        _wagons.Add(wagon);
        wagon.SetCoupled(true);
        Pose(_lead.State.S);
        Sync();
        return true;
    }

    /// <summary>Each coupled wagon on the line at <paramref name="s"/> − <see cref="WagonSpacing"/>·k (k its 1-based place, clamped at the track's start), facing the lead's way.</summary>
    public void Pose(float s)
    {
        if (_wagons.Count == 0 || !_lead.Track.Valid) return;
        for (var k = 0; k < _wagons.Count; k++)
        {
            var wagon = _wagons[k];
            if (!GodotObject.IsInstanceValid(wagon)) continue;
            var at = Mathf.Clamp(s - _lead.Dir * WagonSpacing * (k + 1), 0f, _lead.Track.Length);
            var frame = _lead.FrameOf(at);
            wagon.Pose(_lead.Dir >= 0 ? frame : new Transform3D(frame.Basis * new Basis(Vector3.Up, Mathf.Pi), frame.Origin));
        }
    }

    /// <summary>The wagons learn the lead's speed and the bend each stands in (their riders' velocity).</summary>
    public void Sync()
    {
        var state = _lead.State;
        for (var k = 0; k < _wagons.Count; k++)
        {
            var wagon = _wagons[k];
            if (!GodotObject.IsInstanceValid(wagon)) continue;
            wagon.Speed = state.Mode == CartMode.Rolling ? state.V : 0f;
            wagon.Curvature = _lead.Dir * _lead.Track.Curvature(Mathf.Max(0f, state.S - _lead.Dir * WagonSpacing * (k + 1)));
        }
    }

    /// <summary>Seat every rider on its wagon as it stands now (copied first: a rider whose ride is gone leaves the book as it follows).</summary>
    public void Follow()
    {
        if (_wagons.Count == 0) return;
        _riders.Clear();
        foreach (var wagon in _wagons)
            if (GodotObject.IsInstanceValid(wagon)) _riders.AddRange(wagon.Riders.Values);
        foreach (var rider in _riders)
            if (rider is not GodotObject o || GodotObject.IsInstanceValid(o)) rider.FollowSeat();
    }
}
