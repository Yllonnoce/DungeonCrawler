using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The safe room's keeper on one floor (server or solo). Tracks which living members are inside (each of them heals
/// <see cref="SafeRoomRules.RegenMultiplier"/> times faster; a member outside heals normally) and whether any is, which
/// puts "Safe room" on the objective line; opens the iron-banded door when a living member comes within <see cref="SafeRoomRules.DoorOpenRadius"/> of it and
/// shuts it <see cref="SafeRoomRules.DoorCloseDelay"/> seconds after all of them are beyond
/// <see cref="SafeRoomRules.DoorCloseRadius"/>; runs the <see cref="GatheringClock"/> of the creatures shut outside.
/// </summary>
public partial class SafeRoomController : Node
{
    public static SafeRoomController? Instance { get; private set; }
    /// <summary>What the objective line ends with while the party is inside.</summary>
    public static string? HudText => Instance is { Occupied: true } ? "Safe room" : null;

    public bool Occupied { get; private set; }
    /// <summary>Groups the shut-out creatures have sent so far (saved with the floor; the clock and the cap run on it).</summary>
    public int Gathered => _clock.Spawned;
    /// <summary>Creatures gathered on this floor since it was built or loaded (what <see cref="EventBus.Gathered"/> carries; not saved).</summary>
    public int GatheredCreatures => _creatures;
    /// <summary>Whether the party member at <paramref name="memberIndex"/> (an index into the party's adventurers) is alive and inside the room this tick.</summary>
    public bool IsInside(int memberIndex) => _inside.Contains(memberIndex);

    private readonly GatheringClock _clock = new();
    private int _creatures;
    private readonly List<string> _enemyIds = new();
    private readonly List<Enemy> _alive = new();
    private Random _rng = new();
    private FloorLayout _layout = null!;
    private int _floor;
    private readonly HashSet<int> _inside = new();
    private Door? _door;
    // seconds until the next look for the door while none is found
    private float _doorLookIn;
    // seconds left before the door shuts; negative while no close is pending
    private float _closeIn = -1f;

    public void Setup(FloorLayout layout, int floor, IReadOnlyList<string> enemyIds, int gathered)
    {
        Instance = this;
        Name = "SafeRoom";
        _layout = layout;
        _floor = floor;
        _rng = new Random(layout.Seed ^ 0x6A7E);
        _enemyIds.AddRange(enemyIds);
        if (gathered > 0) _clock.Restore(gathered);
    }

    public void Restore(int gathered) { if (gathered > 0) _clock.Restore(gathered); }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        _inside.Clear();
        if (!Occupied) return;
        Occupied = false;
        // the next floor starts without the HUD line
        SetHud(false);
    }

    /// <summary>The local screen's flag (a dedicated server has none) and the objective line, which reaches the clients either way.</summary>
    private static void SetHud(bool inside)
    {
        if (Main.Instance is { } main && IsInstanceValid(main) && main.Hud is { } hud && IsInstanceValid(hud)) hud.SafeRoom = inside; // its setter recomposes the line
        else AdventureDirector.Instance?.RefreshObjective();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient) return;
        var main = Main.Instance;
        if (main == null || !main.InGame || main.Builder.Layout != _layout) return;
        var dt = (float)delta;
        var bodies = main.Party.Adventurers;
        var members = Enumerable.Range(0, bodies.Length).Where(i => IsInstanceValid(bodies[i]) && !bodies[i].Parked)
            .Select(i => (index: i, body: bodies[i], cell: DungeonBuilder.WorldToCell(bodies[i].GlobalPosition), alive: !bodies[i].Downed)).ToList();

        _inside.Clear();
        foreach (var m in members) if (m.alive && _layout.IsInSafeRoom(m.cell)) _inside.Add(m.index);
        var occupied = _inside.Count > 0; // = SafeRoomRules.Occupied over the same members
        if (occupied != Occupied)
        {
            Occupied = occupied;
            EventBus.Instance.RaiseSafeRoomChanged(occupied);
            SetHud(occupied);
        }

        TickDoor(dt, main, members.Where(m => m.alive).Select(m => m.body.GlobalPosition));
        _alive.RemoveAll(e => !IsInstanceValid(e) || !e.IsAlive);
        if (DebugTools.Instance?.NoGathering == true) return;
        _clock.Update(dt, Occupied, _alive.Count);
        if (_clock.TakeDue()) SpawnGroup(main);
    }

    /// <summary>One group of creatures arrives outside the door and holds there, awake.</summary>
    private void SpawnGroup(Main main)
    {
        if (_enemyIds.Count == 0 || SafeRoomRules.GatherCell(_layout) is not { } cell) return;
        var at = main.Builder.Surface(cell) + Vector3.Up * 0.15f;
        var count = _rng.Next(2) + 1;
        var made = 0;
        for (var i = 0; i < count; i++)
        {
            var jitter = new Vector3((float)(_rng.NextDouble() - 0.5) * 1.2f, 0f, (float)(_rng.NextDouble() - 0.5) * 1.2f);
            var id = _enemyIds[_rng.Next(_enemyIds.Count)];
            if (main.Builder.SpawnEnemy(id, at + jitter, _floor) is not { } e) continue;
            e.HoldAt(at);
            _alive.Add(e);
            made++;
        }
        if (made > 0)
        {
            _creatures += made;
            EventBus.Instance.RaiseGathered(_creatures);
        }
        GD.Print($"[gather] floor {_floor}: {made} creature(s) at {cell} (groups {_clock.Spawned}, creatures {_creatures})");
    }

    private void TickDoor(float dt, Main main, IEnumerable<Vector3> living)
    {
        if (_door != null && !IsInstanceValid(_door)) _door = null;
        if (_door == null)
        {
            _doorLookIn -= dt;
            if (_doorLookIn > 0f) return;
            _doorLookIn = 1f; // look again at most once a second
            _door = main.Builder.FloorRoot?.GetChildren().OfType<Door>().FirstOrDefault(d => d.Kind == VaultKind.SafeDoor && d.Cell == _layout.SafeRoomDoor);
            if (_door == null) return;
        }
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
