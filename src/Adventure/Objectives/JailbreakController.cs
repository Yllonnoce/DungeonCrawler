using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The prisoners, their jailers and the jail. Villagers wander and flee the creatures; a creature that reaches one
/// becomes its jailer and marches it to the jail; jailers carry keys. The party opens the jail door with a key or a
/// lockpick and frees each prisoner the same way; freed villagers follow the party. Every prisoner free opens the
/// treasure vault and the stairs.
/// </summary>
public partial class JailbreakController : ObjectiveController
{
    public const float DecideEvery = 0.5f;

    private readonly List<Npc> _prisoners = new();
    private readonly Dictionary<Npc, PrisonerState> _state = new();
    private readonly Dictionary<Npc, Enemy> _jailer = new();
    private readonly Dictionary<Npc, float> _timeInRoom = new();
    private readonly Dictionary<Npc, int> _lastRoom = new();
    private Door? _jailDoor, _rewardDoor;
    private Vector3 _jailAnchor;
    private List<List<int>> _neighbours = new();
    private List<List<GridPos>> _roomCells = new();
    private float _decide;

    public IReadOnlyList<Npc> Prisoners => _prisoners;
    public PrisonerState StateOf(Npc npc) => _state.GetValueOrDefault(npc, PrisonerState.Wandering);
    public int Freed => _state.Values.Count(s => s == PrisonerState.Freed);

    public override string HudText => Completed ? "Every prisoner is free. Take the stairs." : JailbreakRules.HudText(_prisoners.Select(StateOf).ToList());

    public string PromptFor(Npc npc) => StateOf(npc) switch
    {
        PrisonerState.Wandering => $"Rescue {npc.NpcName}",
        PrisonerState.Jailed => $"Unlock {npc.NpcName}'s shackles",
        _ => "",
    };

    public override void Setup(FloorLayout layout, ObjectiveContents contents, int floor, int seed)
    {
        base.Setup(layout, contents, floor, seed);
        var rooms = layout.Rooms;
        _neighbours = Enumerable.Range(0, rooms.Count).Select(_ => new List<int>()).ToList();
        foreach (var (a, b) in layout.Connections)
        {
            if (a < 0 || b < 0 || a >= rooms.Count || b >= rooms.Count || a == b) continue;
            if (!_neighbours[a].Contains(b)) _neighbours[a].Add(b);
            if (!_neighbours[b].Contains(a)) _neighbours[b].Add(a);
        }
        _roomCells = rooms.Select(r => RoomCells.Free(layout, r)).ToList();
        EventBus.Instance.EnemyDied += OnEnemyDied;
    }

    public override void _ExitTree()
    {
        EventBus.Instance.EnemyDied -= OnEnemyDied;
        base._ExitTree();
    }

    public override void Build(DungeonBuilder builder, Node3D root)
    {
        var objective = Layout.Objective!;
        _jailDoor = root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == objective.JailVault);
        _rewardDoor = objective.RewardVault >= 0 ? root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == objective.RewardVault) : null;
        _jailAnchor = builder.Surface(objective.JailRoom!.Center);
        foreach (var p in Contents.Prisoners)
        {
            var npc = builder.SpawnObjectiveNpc(root, p.Id, p.Name, p.Cell, NpcMode.Prisoner);
            _prisoners.Add(npc);
            _state[npc] = p.Jailed ? PrisonerState.Jailed : PrisonerState.Wandering;
            npc.Jailed = p.Jailed;
            _timeInRoom[npc] = 0f;
            _lastRoom[npc] = RoomIndex(npc.GlobalPosition);
        }
    }

    private int RoomIndex(Vector3 world) => Layout.RoomAt(DungeonBuilder.WorldToCell(world)) is { } r ? Layout.Rooms.IndexOf(r) : -1;

    private int[] Hops(int from)
    {
        var hops = Enumerable.Repeat(-1, _neighbours.Count).ToArray();
        if (from < 0 || from >= hops.Length) return hops;
        var queue = new Queue<int>();
        hops[from] = 0; queue.Enqueue(from);
        while (queue.Count > 0) { var r = queue.Dequeue(); foreach (var n in _neighbours[r]) if (hops[n] < 0) { hops[n] = hops[r] + 1; queue.Enqueue(n); } }
        return hops;
    }

    protected override void Tick(float dt, Main main)
    {
        if (Completed) return;
        foreach (var npc in _prisoners) _timeInRoom[npc] = _timeInRoom.GetValueOrDefault(npc) + dt;
        _decide -= dt;
        if (_decide > 0f) return;
        _decide = DecideEvery;
        if (main.Builder.FloorRoot is not { } root) return;
        var enemies = root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsBoss).ToList();
        var perRoom = new int[_neighbours.Count];
        foreach (var e in enemies) { var r = RoomIndex(e.GlobalPosition); if (r >= 0) perRoom[r]++; }
        var jailIndex = Layout.Objective!.JailRoom is { } jail ? Layout.Rooms.IndexOf(jail) : -1;

        foreach (var npc in _prisoners)
        {
            if (!IsInstanceValid(npc) || npc.Dead) continue;
            var state = StateOf(npc);
            var room = RoomIndex(npc.GlobalPosition);
            if (room != _lastRoom.GetValueOrDefault(npc, -2)) { _lastRoom[npc] = room; _timeInRoom[npc] = 0f; }
            switch (state)
            {
                case PrisonerState.Wandering:
                {
                    // caught?
                    var catcher = enemies.Where(e => e.Captive == null && e.NpcTarget == null).OrderBy(e => e.GlobalPosition.DistanceTo(npc.GlobalPosition)).FirstOrDefault();
                    if (catcher != null && catcher.GlobalPosition.DistanceTo(npc.GlobalPosition) <= JailbreakRules.CaptureRange) { Capture(npc, catcher); break; }
                    var nearest = enemies.Count == 0 ? float.MaxValue : enemies.Min(e => e.GlobalPosition.DistanceTo(npc.GlobalPosition));
                    var hopsFromMe = Hops(room);
                    var threatRoom = enemies.Count == 0 ? -1 : RoomIndex(enemies.OrderBy(e => e.GlobalPosition.DistanceTo(npc.GlobalPosition)).First().GlobalPosition);
                    var hopsFromThreat = Hops(threatRoom);
                    var views = new List<RoomView>();
                    for (var i = 0; i < _neighbours.Count; i++) views.Add(new RoomView(i, perRoom[i], hopsFromMe[i], hopsFromThreat[i] < 0 ? 99 : hopsFromThreat[i], i == jailIndex));
                    var decision = PrisonerBrain.Decide(new PrisonerContext(state, room, nearest, views, _timeInRoom[npc]), Rng);
                    switch (decision)
                    {
                        case Flee f: WalkTo(npc, f.Room); break;
                        case WanderTo w: WalkTo(npc, w.Room); _timeInRoom[npc] = 0f; break;
                        default: if (npc.MoveTarget is { } t && t.DistanceTo(npc.GlobalPosition) < 1f) npc.MoveTarget = null; break;
                    }
                    break;
                }
                case PrisonerState.Captured:
                {
                    var jailer = _jailer.GetValueOrDefault(npc);
                    if (jailer == null || !IsInstanceValid(jailer) || !jailer.IsAlive || jailer.State is not Enemies.Ai.EscortCaptiveState) { Release(npc); break; }
                    // trail the jailer; delivered when the jailer stands at the jail's middle
                    var flat = new Vector3(_jailAnchor.X, jailer.GlobalPosition.Y, _jailAnchor.Z);
                    if (jailer.GlobalPosition.DistanceTo(flat) < JailbreakRules.DeliverRange) { Deliver(npc, jailer); break; }
                    npc.MoveTarget = jailer.GlobalPosition + (npc.GlobalPosition - jailer.GlobalPosition).Normalized() * 0.5f;
                    break;
                }
                case PrisonerState.Jailed:
                    npc.MoveTarget = null;
                    break;
            }
        }
    }

    private void WalkTo(Npc npc, int room)
    {
        if (room < 0 || room >= _roomCells.Count || _roomCells[room].Count == 0 || Main.Instance?.Builder is not { } builder) return;
        var cell = _roomCells[room][Rng.Next(_roomCells[room].Count)];
        npc.MoveTarget = builder.Surface(cell);
    }

    private void Capture(Npc npc, Enemy jailer)
    {
        _state[npc] = JailbreakRules.Apply(StateOf(npc), new PrisonerCaught());
        _jailer[npc] = jailer;
        npc.Captured = true;
        npc.MoveTarget = null;
        jailer.Captive = npc;
        jailer.CarriesJailKey = true;
        jailer.ChangeState(new Enemies.Ai.EscortCaptiveState(_jailAnchor));
        EventBus.Instance.Log($"{jailer.DisplayName} seizes {npc.NpcName} and drags them toward the jail!");
        EventBus.Instance.RaiseNpcEvent(npc.Id, NpcEventKind.Captured);
        Raise(ObjectiveTrigger.Capture, npc.NpcName);
        RefreshHud();
    }

    private void Release(Npc npc)
    {
        _state[npc] = JailbreakRules.Apply(StateOf(npc), new JailerLost());
        if (_jailer.Remove(npc, out var jailer) && IsInstanceValid(jailer)) jailer.Captive = null;
        npc.Captured = false;
        npc.MoveTarget = null;
        EventBus.Instance.Log($"{npc.NpcName} slips free of their captor.");
        RefreshHud();
    }

    private void Deliver(Npc npc, Enemy jailer)
    {
        _state[npc] = JailbreakRules.Apply(StateOf(npc), new PrisonerDelivered());
        _jailer.Remove(npc);
        jailer.Captive = null;
        npc.Captured = false;
        npc.Jailed = true;
        var jail = Layout.Objective!.JailRoom!;
        var cells = _roomCells[Layout.Rooms.IndexOf(jail)];
        if (cells.Count > 0 && Main.Instance?.Builder is { } builder) npc.MoveTarget = builder.Surface(cells[Rng.Next(cells.Count)]);
        // the jailer takes up a post outside the door
        if (_jailDoor != null && VaultPuzzles.Doorstep(Layout, _jailDoor.Cell, jail) is { } step && Main.Instance?.Builder is { } b) jailer.HomePosition = b.Surface(step);
        jailer.ChangeState(new Enemies.Ai.ReturnState());
        EventBus.Instance.Log($"{npc.NpcName} is thrown into the jail. The jailer keeps the key.");
        EventBus.Instance.RaiseNpcEvent(npc.Id, NpcEventKind.Jailed);
        RefreshHud();
    }

    private void OnEnemyDied(Enemy e)
    {
        if (e.Captive is { } captive && _state.ContainsKey(captive) && StateOf(captive) == PrisonerState.Captured) Release(captive);
    }

    /// <summary>E on a prisoner: rescue a wanderer outright, unlock a jailed one's shackles with a key or a pick.</summary>
    public void OnInteract(Npc npc, PartyController party, Adventurer actor)
    {
        switch (StateOf(npc))
        {
            case PrisonerState.Wandering: Free(npc, party, "rescued"); break;
            case PrisonerState.Jailed: if (party.TryUnlock($"{npc.NpcName}'s shackles")) Free(npc, party, "unshackled"); break;
        }
    }

    private void Free(Npc npc, PartyController party, string how)
    {
        _state[npc] = JailbreakRules.Apply(StateOf(npc), new PrisonerFreed());
        npc.Jailed = false;
        npc.Captured = false;
        npc.Freed = true;
        npc.MoveTarget = null;
        npc.StartFollowing();
        party.AwardXp(JailbreakRules.XpForFreeing(Floor));
        AdventureDirector.Instance?.SetFlag($"freed:{npc.Id}", npc.NpcName);
        EventBus.Instance.Log($"{npc.NpcName} is {how} and falls in behind you.");
        EventBus.Instance.RaiseNpcEvent(npc.Id, NpcEventKind.Freed);
        Raise(ObjectiveTrigger.Freed, npc.NpcName);
        RefreshHud();
        if (JailbreakRules.Completed(_prisoners.Select(StateOf))) AllFree();
    }

    private void AllFree()
    {
        AdventureDirector.Instance?.SetFlag("jailbreak_freed_all", "the prisoners");
        if (_rewardDoor is { IsOpen: false }) { _rewardDoor.Reveal(); _rewardDoor.Open(); EventBus.Instance.Log("Somewhere on this floor, a vault door swings wide for you."); }
        Raise(ObjectiveTrigger.AllFreed);
        Complete();
    }

    public override ObjectiveStateDto Capture()
    {
        var dto = base.Capture();
        dto.FreedNpcs = _prisoners.Where(p => StateOf(p) == PrisonerState.Freed).Select(p => p.Id).ToList();
        dto.JailedNpcs = _prisoners.Where(p => StateOf(p) == PrisonerState.Jailed).Select(p => p.Id).ToList();
        return dto;
    }

    public override void Restore(ObjectiveStateDto dto)
    {
        base.Restore(dto);
        foreach (var npc in _prisoners)
        {
            if (dto.FreedNpcs.Contains(npc.Id)) { _state[npc] = PrisonerState.Freed; npc.Freed = true; npc.Jailed = false; npc.Captured = false; if (!npc.Escorting) npc.StartFollowing(); }
            else if (dto.JailedNpcs.Contains(npc.Id)) { _state[npc] = PrisonerState.Jailed; npc.Jailed = true; npc.Captured = false; }
            else { _state[npc] = PrisonerState.Wandering; npc.Jailed = false; npc.Captured = false; } // a capture in progress starts over
        }
        if (Completed && _rewardDoor is { IsOpen: false }) { _rewardDoor.Reveal(); _rewardDoor.Open(silent: true); }
    }

    public override void ApplyNet(ObjectiveStateDto dto) => base.Restore(dto);

    /// <summary>Free everyone and open everything (tests).</summary>
    public override void Hurry()
    {
        if (Main.Instance?.Party is not { } party) return;
        _jailDoor?.Open();
        foreach (var npc in _prisoners.Where(p => StateOf(p) != PrisonerState.Freed).ToList()) Free(npc, party, "freed");
    }
}
