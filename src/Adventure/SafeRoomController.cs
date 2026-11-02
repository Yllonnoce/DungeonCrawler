using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The safe room's keeper on one floor (server or solo). Tracks whether a living member holds the room, heals the
/// party <see cref="SafeRoomRules.RegenMultiplier"/> times faster while one does and puts "Safe room" on the objective
/// line; opens the iron-banded door when a living member comes within <see cref="SafeRoomRules.DoorOpenRadius"/> of it and
/// shuts it <see cref="SafeRoomRules.DoorCloseDelay"/> seconds after all of them are beyond
/// <see cref="SafeRoomRules.DoorCloseRadius"/>; runs the <see cref="GatheringClock"/> of the creatures shut outside.
/// </summary>
public partial class SafeRoomController : Node
{
    public static SafeRoomController? Instance { get; private set; }
    /// <summary>What the objective line ends with while the party is inside.</summary>
    public static string? HudText => Instance is { Occupied: true } ? "Safe room" : null;

    public bool Occupied { get; private set; }
    /// <summary>Groups the shut-out creatures have sent so far (saved with the floor).</summary>
    public int Gathered => _clock.Spawned;

    private readonly GatheringClock _clock = new();
    private readonly List<string> _enemyIds = new();
    private FloorLayout _layout = null!;
    private int _floor;
    private Door? _door;
    // seconds left before the door shuts; negative while no close is pending
    private float _closeIn = -1f;

    public void Setup(FloorLayout layout, int floor, IReadOnlyList<string> enemyIds, int gathered)
    {
        Instance = this;
        Name = "SafeRoom";
        _layout = layout;
        _floor = floor;
        _enemyIds.AddRange(enemyIds);
        if (gathered > 0) _clock.Restore(gathered);
    }

    public void Restore(int gathered) { if (gathered > 0) _clock.Restore(gathered); }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (!Occupied) return;
        Occupied = false;
        // the next floor starts at the normal rates and without the HUD line
        var main = Main.Instance;
        if (main == null || !IsInstanceValid(main)) return;
        if (IsInstanceValid(main.Party)) main.Party.RegenMultiplier = 1f;
        if (IsInstanceValid(main.Hud)) main.Hud.SafeRoom = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Builder.Layout != _layout) return;
        var dt = (float)delta;
        var members = main.Party.Active.Select(a => (body: a, cell: DungeonBuilder.WorldToCell(a.GlobalPosition), alive: !a.Downed)).ToList();

        var occupied = SafeRoomRules.Occupied(members.Select(m => (m.cell, m.alive)), _layout);
        if (occupied != Occupied)
        {
            Occupied = occupied;
            main.Party.RegenMultiplier = occupied ? SafeRoomRules.RegenMultiplier : 1f;
            EventBus.Instance.RaiseSafeRoomChanged(occupied);
            main.Hud.SafeRoom = occupied;
        }

        TickDoor(dt, main, members.Where(m => m.alive).Select(m => m.body.GlobalPosition));
        // the gatherers themselves are spawned from TakeDue() by the gathering wave (Task 6); none are alive yet
        if (DebugTools.Instance?.NoGathering != true) _clock.Update(dt, Occupied, 0);
    }

    private void TickDoor(float dt, Main main, IEnumerable<Vector3> living)
    {
        _door ??= main.Builder.FloorRoot?.GetChildren().OfType<Door>().FirstOrDefault(d => d.Kind == VaultKind.SafeDoor && d.Cell == _layout.SafeRoomDoor);
        if (_door == null || !IsInstanceValid(_door)) return;
        var centre = _door.GlobalPosition;
        var nearest = living.Select(p => new Vector2(p.X - centre.X, p.Z - centre.Z).Length()).DefaultIfEmpty(float.MaxValue).Min();
        if (nearest <= SafeRoomRules.DoorOpenRadius)
        {
            _closeIn = -1f;
            if (!_door.IsOpen) _door.Open();
            return;
        }
        if (!_door.IsOpen) { _closeIn = -1f; return; }
        if (nearest <= SafeRoomRules.DoorCloseRadius) { _closeIn = -1f; return; } // someone lingers by the door: no close pending
        if (_closeIn < 0f) _closeIn = SafeRoomRules.DoorCloseDelay;
        _closeIn -= dt;
        if (_closeIn > 0f) return;
        _closeIn = -1f;
        _door.Close();
    }
}
