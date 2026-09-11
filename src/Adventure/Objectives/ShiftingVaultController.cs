using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The lever-and-gate puzzle around the stairs. Each lever flips its set of gates; when the gates match the target the
/// vault door opens and the stairs with it. Patrols reroute whenever the gates shift. Without a puzzle (a floor with
/// nowhere to put one) the vault has an ordinary lever and the objective just waits for its door.
/// </summary>
public partial class ShiftingVaultController : ObjectiveController
{
    /// <summary>Bit i set when lever i stands pulled.</summary>
    public int Parity { get; private set; }
    public VaultPuzzle? Puzzle => Layout.Objective?.Puzzle;
    public int State => Puzzle?.StateFor(Parity) ?? 0;
    public IReadOnlyList<Door> Gates => _gates;
    public IReadOnlyList<Lever> Levers => _levers;

    private readonly List<Door> _gates = new();
    private readonly List<Lever> _levers = new();
    private Door? _vaultDoor;
    private int _lastDistance;

    /// <summary>How many gates already stand as the target wants.</summary>
    public int Right => Puzzle is { } p ? Enumerable.Range(0, p.GateCount).Count(g => p.IsOpen(State, g) == p.IsOpen(p.Target, g)) : 0;
    private int Distance(int state) => Puzzle is { } p ? Enumerable.Range(0, p.GateCount).Count(g => p.IsOpen(state, g) != p.IsOpen(p.Target, g)) : 0;

    public override string HudText => Completed ? "The vault stands open. Take the stairs."
        : Puzzle is { } p ? $"Set the gates to open the vault ({Right} of {p.GateCount} stand right)."
        : "Find the lever that opens the vault around the stairs.";

    public override void Build(DungeonBuilder builder, Node3D root)
    {
        var objective = Layout.Objective!;
        _vaultDoor = root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == objective.StairsVault);
        if (Puzzle is not { } puzzle) return;
        for (var g = 0; g < puzzle.GateCount; g++)
        {
            var door = builder.SpawnGate(root, puzzle.Gates[g], FloorContents.ExtraIndexBase + g, VaultPuzzles.GateName(g));
            _gates.Add(door);
        }
        for (var i = 0; i < puzzle.LeverCount; i++)
            _levers.Add(builder.SpawnLever(root, puzzle.Levers[i], FloorContents.ExtraIndexBase + i, toggle: true, label: "lever"));
        foreach (var (cell, clue) in objective.Plaques)
            builder.SpawnPlaque(root, cell, clue < 0 ? puzzle.LeverCount : clue, clue < 0 ? VaultPuzzles.VaultClue(puzzle, Layout) : VaultPuzzles.LeverClue(puzzle, clue));
        Apply(silent: true);
        _lastDistance = Distance(State);
    }

    public override void OnLever(int index, bool pulled)
    {
        if (Puzzle is not { } puzzle || Completed || index < 0 || index >= puzzle.LeverCount) return;
        Parity ^= 1 << index;
        Apply(silent: false);
        var distance = Distance(State);
        Raise(ObjectiveTrigger.GateShift, index.ToString());
        if (distance > _lastDistance) Raise(ObjectiveTrigger.WrongLever, index.ToString());
        _lastDistance = distance;
        Reroute();
        if (State == puzzle.Target) OpenVault();
        RefreshHud();
    }

    private void OpenVault()
    {
        _vaultDoor?.Open();
        Raise(ObjectiveTrigger.VaultOpened);
        Complete();
    }

    /// <summary>Put every gate and lever where the parity says.</summary>
    private void Apply(bool silent)
    {
        if (Puzzle is not { } puzzle) return;
        var state = puzzle.StateFor(Parity);
        for (var g = 0; g < _gates.Count; g++) _gates[g].SetOpen(puzzle.IsOpen(state, g), silent);
        for (var i = 0; i < _levers.Count; i++) _levers[i].SetPulled((Parity & (1 << i)) != 0);
    }

    /// <summary>Patrols walk the rooms they can still reach with the gates as they stand.</summary>
    private void Reroute()
    {
        if (Main.Instance?.Builder is not { } builder || builder.Collectives is not { } registry || Puzzle is not { } puzzle) return;
        var state = State;
        var closed = new HashSet<GridPos>();
        for (var g = 0; g < puzzle.GateCount; g++) if (!puzzle.IsOpen(state, g)) closed.Add(puzzle.Gates[g]);
        var rooms = Layout.Rooms;
        var neighbours = Enumerable.Range(0, rooms.Count).Select(_ => new List<int>()).ToList();
        foreach (var (a, b) in Layout.Connections)
        {
            if (a < 0 || b < 0 || a >= rooms.Count || b >= rooms.Count || a == b) continue;
            if (!neighbours[a].Contains(b)) neighbours[a].Add(b);
            if (!neighbours[b].Contains(a)) neighbours[b].Add(a);
        }
        foreach (var c in registry.Collectives)
        {
            if (c.Goal != GoalKind.Patrol || c.Alive.FirstOrDefault() is not { } member) continue;
            var from = DungeonBuilder.WorldToCell(member.GlobalPosition);
            var reach = Layout.Reachable(from, p => closed.Contains(p));
            var reachable = new HashSet<int>();
            for (var i = 0; i < rooms.Count; i++) if (reach.Contains(rooms[i].Center)) reachable.Add(i);
            var home = Layout.RoomAt(from) is { } h ? rooms.IndexOf(h) : -1;
            var route = PatrolRerouter.Reroute(c.RouteRooms, reachable, neighbours.Select(n => (IReadOnlyList<int>)n).ToList(), home);
            if (PatrolRerouter.Changed(c.RouteRooms, route)) c.SetRoute(route, r => builder.Surface(PlanResolver.Anchor(Layout, r) ?? rooms[r].Center));
        }
    }

    protected override void Tick(float dt, Main main)
    {
        // no puzzle: an ordinary lever opens the vault, and that is the objective
        if (Puzzle == null && !Completed && _vaultDoor is { IsOpen: true }) Complete();
    }

    public override ObjectiveStateDto Capture() { var dto = base.Capture(); dto.LeverParity = Parity; return dto; }

    public override void Restore(ObjectiveStateDto dto)
    {
        base.Restore(dto);
        Parity = dto.LeverParity;
        Apply(silent: true);
        _lastDistance = Distance(State);
        if (Completed) _vaultDoor?.Open(silent: true);
    }

    public override void ApplyNet(ObjectiveStateDto dto) => Restore(dto);

    /// <summary>Pull the levers that solve it (tests).</summary>
    public override void Hurry()
    {
        if (Puzzle is not { } puzzle) { _vaultDoor?.Open(); return; }
        for (var p = 1; p < 1 << puzzle.LeverCount && !Completed; p++)
        {
            if (puzzle.StateFor(p) != puzzle.Target) continue;
            var want = p;
            for (var i = 0; i < puzzle.LeverCount && !Completed; i++)
                if (((want ^ Parity) & (1 << i)) != 0) OnLever(i, (want & (1 << i)) != 0);
            return;
        }
    }
}
