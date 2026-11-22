using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The train's wagons (<see cref="TrainWagon"/>, plan 14) coupled behind a lead cart (the party's, owned by the RailController), nearest
/// first: posed on the line behind it, told its speed and their bends, their riders seated where they stand. It also keeps the siding
/// cells (cages and spares stand there beside the platforms) that nobody getting out is set down on.
/// </summary>
public sealed class CartTrain
{
    /// <summary>From one body's middle to the next along the train: a cart's length and a coupling's gap.</summary>
    public const float WagonSpacing = TrainRules.Spacing;

    private readonly CartRun _lead;
    private readonly List<TrainWagon> _wagons = new();
    private readonly List<ICartRider> _riders = new();

    public CartTrain(CartRun lead) => _lead = lead;

    /// <summary>The coupled wagons, nearest the lead first.</summary>
    public IReadOnlyList<TrainWagon> Wagons => _wagons;
    public int Count => _wagons.Count;

    /// <summary>Cells a cage or a spare cart stands on beside a platform: nobody getting out is set down there.</summary>
    public HashSet<GridPos> Sidings { get; } = new();

    /// <summary>A siding cell: riders getting out (and enemies knocked aside) are never set down on it.</summary>
    public void ReserveSiding(GridPos cell) => Sidings.Add(cell);

    /// <summary>
    /// The train is <paramref name="wagons"/>, in that order (nearest the lead first): each is coupled (shown) and posed behind the lead at
    /// once; a wagon coupled before and not listed is uncoupled (lost: hidden). Both peers' (a client's from the server's cart state).
    /// </summary>
    public void CoupleAll(IReadOnlyList<TrainWagon> wagons)
    {
        foreach (var wagon in _wagons)
            if (GodotObject.IsInstanceValid(wagon) && !Contains(wagons, wagon)) wagon.SetCoupled(false);
        _wagons.Clear();
        foreach (var wagon in wagons)
        {
            if (!GodotObject.IsInstanceValid(wagon) || _wagons.Contains(wagon)) continue;
            _wagons.Add(wagon);
            wagon.SetCoupled(true);
        }
        Pose(_lead.State.S);
        Sync();
    }

    private static bool Contains(IReadOnlyList<TrainWagon> list, TrainWagon wagon)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == wagon) return true;
        return false;
    }

    /// <summary>Every wagon off the train, lost (hidden, not solid, standing where it was): a fall's, and a client told no train.</summary>
    public void UncoupleAll()
    {
        foreach (var wagon in _wagons)
            if (GodotObject.IsInstanceValid(wagon)) wagon.SetCoupled(false);
        _wagons.Clear();
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
