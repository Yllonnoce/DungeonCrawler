using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// The jailbreak on a railed floor: Grisel's crew, two to a cage wagon, wait on a siding at each intermediate stop.
/// The party opens a wagon (the orc's keys, Grisel's voice, or a key or a pick); at the cart's next departure from that
/// stop the open wagon couples on behind, its crew aboard, or the party lets them out to follow on foot. A crew member
/// is home in the exit room (seated in the train once the cart is at the end of the line, or on foot there); a dead one
/// is lost. Every cage open and every crew member home or lost opens the reward vault and the stairs. Server only for
/// every change; a client reads the 2 s objective state for the HUD, shows the cages and the keys as the server tells them,
/// and offers the wagons' and the keyring's prompts from its copies (<see cref="PropPromptRules"/>; a use is an InteractIntent).
/// </summary>
public partial class CrewRescueController : ObjectiveController, IPrisonerHost
{
    /// <summary>Grisel opens a cage when she stands this close to it, following the party.</summary>
    public const float GriselReach = 8f;
    public const string GriselId = "npc_grisel";
    /// <summary>Grisel's word at a cage she opens is said within this long of the lock, or not at all (it belongs to the lock).</summary>
    public const double CageCallSeconds = 10;

    private readonly List<CageWagon> _wagons = new();
    private readonly List<Npc> _crew = new();
    private readonly Dictionary<Npc, CageWagon> _wagonOf = new();
    private readonly Dictionary<Npc, CrewState> _state = new();
    private readonly HashSet<string> _home = new();
    private readonly List<CrewState> _states = new();
    /// <summary>Each wagon's (open, left behind) for the win rule, reused every tick.</summary>
    private readonly List<(bool Open, bool LeftBehind)> _cages = new();
    private readonly List<bool> _uncaged = new();
    private RailController? _rails;
    private DungeonBuilder? _builder;
    private Node3D? _root;
    private Room? _exitRoom;
    private Door? _jailDoor, _rewardDoor;
    private Npc? _grisel;

    public IReadOnlyList<CageWagon> Wagons => _wagons;
    public IReadOnlyList<Npc> Crew => _crew;
    /// <summary>The party has the orc's keys: every cage opens without a pick (set when they are taken, plan 3 task 6).</summary>
    public bool OrcKeys
    {
        get => _orcKeys;
        set
        {
            var taken = value && !_orcKeys;
            _orcKeys = value;
            if (value && _keyring != null && IsInstanceValid(_keyring)) _keyring.Take();
            if (taken && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRefs.Keyring, "taken"); // a client's keys come only from that (and the prop check)
        }
    }
    private bool _orcKeys;
    private OrcKeyring? _keyring;
    /// <summary>The orc's keyring at Grisel's belt ("keyring:0"), or null (the forgiven branch).</summary>
    public OrcKeyring? Keyring => _keyring != null && IsInstanceValid(_keyring) ? _keyring : null;

    /// <summary>Client: the keys are taken, as the server said; silently. True when it changed.</summary>
    public bool ShowKeysTaken()
    {
        if (_orcKeys) return false;
        _orcKeys = true;
        Keyring?.Take();
        return true;
    }

    /// <summary>Client: the stop's cage stands open, as the server said; silently. True when it changed.</summary>
    public bool ShowCageOpen(int stop)
    {
        foreach (var wagon in _wagons)
            if (wagon.Stop == stop && !wagon.Open) { wagon.SetOpen(true); return true; }
        return false;
    }
    /// <summary>The dead Grisel on the condemned branch (null on the forgiven one).</summary>
    public Npc? Body { get; private set; }
    public CrewState StateOf(Npc npc) => _state.GetValueOrDefault(npc, CrewState.Caged);
    /// <summary>Wagons still locked.</summary>
    public int ClosedCages { get { var n = 0; foreach (var w in _wagons) if (!w.Open) n++; return n; } }

    public override string HudText => CrewRules.HudText(States(), Uncaged());

    /// <summary>Each crew member's cage stands open, or they had none (the HUD's "freed"), into the reused list.</summary>
    private List<bool> Uncaged()
    {
        _uncaged.Clear();
        foreach (var npc in _crew) _uncaged.Add(WagonOf(npc) is not { Open: false });
        return _uncaged;
    }

    private List<CrewState> States()
    {
        _states.Clear();
        foreach (var npc in _crew) _states.Add(StateOf(npc));
        return _states;
    }

    public override void _ExitTree()
    {
        if (_rails != null && IsInstanceValid(_rails))
        {
            _rails.CartEvents -= OnCartEvent;
            if (Equals(_rails.CageHolds, (Func<int, bool>)HoldsCart)) _rails.CageHolds = null; // still ours (a delegate equals another on the same target and method)
        }
        base._ExitTree();
    }

    // ---- building --------------------------------------------------------------------------------

    public override void Build(DungeonBuilder builder, Node3D root)
    {
        _builder = builder;
        _root = root;
        var objective = Layout.Objective!;
        // the jail room stays the walk-in vault it always was; nobody is spawned in its cells on this floor
        _jailDoor = root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == objective.JailVault);
        if (_jailDoor != null) _jailDoor.ObjectiveLock = true;
        _rewardDoor = objective.RewardVault >= 0 ? root.GetChildren().OfType<Door>().FirstOrDefault(d => d.VaultIndex == objective.RewardVault) : null;
        _rails = RailController.Instance is { } rc && rc.GetParent() == root ? rc : null;
        if (_rails == null || Layout.Rail is not { Stops.Count: >= 3 } rail || builder.PartyTrack is not { } track)
        {
            GD.PushError("[CrewRescue] a railed jailbreak without a line to put its wagons on");
            return;
        }
        _rails.CartEvents += OnCartEvent;
        _rails.CageHolds = HoldsCart;
        _exitRoom = Layout.Rooms[rail.Stops[^1].Room];
        if (root.GetNodeOrNull<Stairs>("DownStairs") is { } stairs) stairs.LockReason = ObjectiveText.CrewLockReason(Game.Instance?.Adventure?.Campaign?.Antagonist?.Name);

        // the crew: the plan's named people, or the floor's stock prisoners when a plan names none
        var plans = builder.Contents?.Crew is { Count: > 0 } named
            ? named.Select(c => (c.Id, c.Name, c.Body)).ToList()
            : Contents.Prisoners.Select(p => (p.Id, p.Name, (string?)null)).ToList();
        _grisel = root.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == GriselId); // placed with the floor's NPCs, before the objective
        var taken = OccupiedCells(root);
        for (var stop = 1; stop < rail.Stops.Count - 1; stop++)
        {
            if (PickSiding(builder, rail.Stops[stop], taken) is not { } pick) { GD.PushWarning($"[CrewRescue] stop {stop}: no free cell beside the platform for a cage wagon"); continue; }
            taken.Add(pick.Cell);
            PlaceWagon(builder, root, track, stop, pick.Cell, pick.Platform);
        }
        if (_wagons.Count == 0)
        {
            // nowhere to put a cage: the floor must not stay sealed, so the trial is waived (the first server tick completes it)
            GD.PushWarning("[CrewRescue] no stop had room for a cage wagon: the crew trial is waived and the stairs open");
            return;
        }
        // the crew fill the wagons in stop order, two to a wagon; a stop that had no room passes its crew on to the wagons
        // that stand, and whoever is left over when those are full waits freed at the landing and follows the party
        var next = 0;
        foreach (var wagon in _wagons)
            for (var k = 0; k < CrewRules.PerWagon && next < plans.Count; k++, next++)
            {
                var npc = SpawnCrew(builder, root, plans[next], wagon.Cell);
                _wagonOf[npc] = wagon;
                _state[npc] = CrewState.Caged;
                if (!NetSession.IsClient) Seat(npc, wagon); // a client's crew stand where the snapshots put them
            }
        var landing = next < plans.Count ? LandingCells(rail.Stops[0], taken) : new List<GridPos>();
        for (var i = 0; next < plans.Count; next++, i++)
        {
            var cell = landing.Count > 0 ? landing[i % landing.Count] : Layout.UpStairs;
            var npc = SpawnCrew(builder, root, plans[next], cell);
            npc.Freed = true;
            _state[npc] = CrewState.Freed;
            if (!NetSession.IsClient) npc.Restore(false, true, false, npc.MaxHp, null); // following from the start, without a log line each
            GD.PushWarning($"[CrewRescue] {npc.NpcName} has no cage wagon: freed at the landing on cell {cell}");
        }
        if (!IsForgiven()) PlaceBody(builder, root, taken);
        foreach (var wagon in _wagons)
            GD.Print($"[CrewRescue] stop {wagon.Stop} (room {rail.Stops[wagon.Stop].Room}): cage wagon on cell {wagon.Cell}, crew {string.Join(", ", _crew.Where(c => WagonOf(c) == wagon).Select(c => c.NpcName))}");
    }

    private static bool IsForgiven() => Game.Instance.Adventure.Memory.Flags.Contains("grisel_forgiven");

    /// <summary>
    /// Condemned: Grisel lies dead on a free cell beside wagon 1, no dialogue, her keyring at her belt. Not saved on its own:
    /// built again on every build of the floor, with the keyring hidden when the save says the keys are taken.
    /// </summary>
    private void PlaceBody(DungeonBuilder builder, Node3D root, HashSet<GridPos> taken)
    {
        var wagon = _wagons[0];
        var cell = FreeCellsNear(wagon.Cell, 1)[0];
        taken.Add(cell);
        builder.BlockCell(cell);
        var body = builder.SpawnObjectiveNpc(root, CrewRules.GriselBodyId, "Grisel Thorn", cell, NpcMode.Quest, NpcBodies.Path("npc_prisoner"));
        body.Restore(true, false, false, 0f, null);
        Body = body;
        var keyring = new OrcKeyring { Name = "OrcKeyring", PromptFor = _ => KeyringPrompt(), OnUse = _ => TakeKeys() };
        root.AddChild(keyring);
        var right = body.GlobalBasis.X;
        keyring.GlobalPosition = body.GlobalPosition + right * 0.55f - Vector3.Up * 0.08f;
        _keyring = keyring;
        if (OrcKeys) keyring.Take();
        GD.Print($"[CrewRescue] Grisel lies dead on cell {cell} beside wagon {wagon.Stop}, her keys at her belt");
    }

    /// <summary>The same rule on every peer: a client's keys are the ones the server told it of.</summary>
    private string KeyringPrompt() => PropPromptRules.KeyringPrompt(OrcKeys);

    private void TakeKeys()
    {
        if (NetSession.IsClient || OrcKeys) return;
        OrcKeys = true;
        EventBus.Instance.Log("You take the orc's keys from Grisel's belt.");
        EventBus.Instance.PlaySfx("pickup");
        RefreshHud();
    }

    private Npc SpawnCrew(DungeonBuilder builder, Node3D root, (string Id, string Name, string? Body) plan, GridPos cell)
    {
        var path = plan.Body != null && ResourceLoader.Exists(NpcBodies.Path(plan.Body)) ? NpcBodies.Path(plan.Body) : null;
        var npc = builder.SpawnObjectiveNpc(root, plan.Id, plan.Name, cell, NpcMode.Prisoner, path);
        npc.Crew = true;
        _crew.Add(npc);
        return npc;
    }

    /// <summary>Free floor of the start room, off the line and away from anything standing there, nearest its platform's middle.</summary>
    private List<GridPos> LandingCells(RailStop start, HashSet<GridPos> taken)
    {
        if (start.Room < 0 || start.Room >= Layout.Rooms.Count || _builder == null) return new List<GridPos>();
        var mid = start.Platform.Count > 0 ? start.Platform[start.Platform.Count / 2] : Layout.UpStairs;
        return RoomCells.Free(Layout, Layout.Rooms[start.Room]).Where(c => !Layout.IsRail(c) && !_builder.IsCellBlocked(c) && !taken.Contains(c))
            .OrderBy(c => Sq(c, mid)).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
    }

    /// <summary>The wagon a crew member was caged in, or null for one freed at the landing.</summary>
    private CageWagon? WagonOf(Npc npc) => _wagonOf.GetValueOrDefault(npc);

    /// <summary>Cells something already stands on: an NPC, an enemy, a chest (props and vault cells the builder blocks).</summary>
    private static HashSet<GridPos> OccupiedCells(Node3D root)
    {
        var cells = new HashSet<GridPos>();
        foreach (var child in root.GetChildren())
            if (child is Npc or Enemy or Chest && child is Node3D n) cells.Add(DungeonBuilder.WorldToCell(n.GlobalPosition));
        return cells;
    }

    /// <summary>
    /// The siding: a walkable cell of the stop's room beside its platform, never a rail cell or one something stands on,
    /// on the platform's level, nearest the platform's middle (ties: nearest platform cell, then by position).
    /// </summary>
    private (GridPos Cell, GridPos Platform)? PickSiding(DungeonBuilder builder, RailStop stop, HashSet<GridPos> taken)
    {
        if (stop.Platform.Count == 0 || stop.Room < 0 || stop.Room >= Layout.Rooms.Count) return null;
        var room = Layout.Rooms[stop.Room];
        var free = RoomCells.Free(Layout, room).ToHashSet();
        var mid = stop.Platform[stop.Platform.Count / 2];
        (GridPos Cell, GridPos Platform)? best = null;
        var bestKey = (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (var p in stop.Platform)
            foreach (var n in new[] { new GridPos(p.X, p.Y - 1), new GridPos(p.X + 1, p.Y), new GridPos(p.X, p.Y + 1), new GridPos(p.X - 1, p.Y) })
            {
                if (!free.Contains(n) || Layout.IsRail(n) || builder.IsCellBlocked(n) || taken.Contains(n) || Layout.LevelOf(n) != Layout.LevelOf(p)) continue;
                var key = (Sq(n, mid), Sq(p, mid), n.X, n.Y);
                if (key.CompareTo(bestKey) >= 0) continue;
                bestKey = key;
                best = (n, p);
            }
        return best;
    }

    private static int Sq(GridPos a, GridPos b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>The wagon on its siding cell, facing along the track beside it, with a stub of rail under it; the cell is blocked and kept clear of riders getting out.</summary>
    private void PlaceWagon(DungeonBuilder builder, Node3D root, RailTrack track, int stop, GridPos cell, GridPos platform)
    {
        var at = builder.Surface(cell);
        var beside = builder.Surface(platform);
        var s = track.Curve.GetClosestOffset(track.ToLocal(beside));
        var along = -(track.GlobalTransform * track.Curve.SampleBakedWithRotation(s, true, true)).Basis.Z;
        var basis = new Basis(Vector3.Up, Mathf.Atan2(-along.X, -along.Z));
        var side = (beside - at).Dot(basis.X) >= 0f ? 1 : -1;
        var wagon = new CageWagon { Name = $"CageWagon{stop}", Stop = stop, Cell = cell, PlatformSide = side };
        wagon.PromptFor = hero => WagonPrompt(wagon, hero);
        wagon.OnUse = hero => UseWagon(wagon, hero);
        root.AddChild(wagon);
        wagon.GlobalTransform = new Transform3D(basis, at);
        root.AddChild(Siding($"Siding{stop}", new Transform3D(basis, at)));
        builder.BlockCell(cell);
        _rails?.ReserveSiding(cell);
        _wagons.Add(wagon);
    }

    /// <summary>A short stub of track on sleepers for a wagon to stand on: looks only.</summary>
    private static Node3D Siding(string name, Transform3D at)
    {
        var node = new Node3D { Name = name, Transform = at };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.18f, 0.2f), Metallic = 0.8f, Roughness = 0.45f };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.21f, 0.13f), Roughness = 0.9f };
        const float length = 3.2f;
        foreach (var z in new[] { -1.2f, -0.4f, 0.4f, 1.2f })
            node.AddChild(new MeshInstance3D { Name = "Sleeper", Mesh = new BoxMesh { Size = new Vector3(1.5f, 0.12f, 0.25f) }, MaterialOverride = wood, Position = new Vector3(0, 0.06f, z) });
        foreach (var x in new[] { -RailTrack.Gauge / 2f, RailTrack.Gauge / 2f })
            node.AddChild(new MeshInstance3D { Name = "Rail", Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.12f, length) }, MaterialOverride = iron, Position = new Vector3(x, 0.18f, 0) });
        node.AddChild(new MeshInstance3D { Name = "Stop", Mesh = new BoxMesh { Size = new Vector3(1.2f, 0.3f, 0.2f) }, MaterialOverride = wood, Position = new Vector3(0, 0.27f, length / 2f) });
        return node;
    }

    /// <summary>Book a bench seat in <paramref name="wagon"/> and sit the NPC there (off any other ride first).</summary>
    private static void Seat(Npc npc, CageWagon wagon)
    {
        if (npc.Cart == wagon) { npc.FollowSeat(); return; }
        if (npc.Seated) npc.GetOut(npc.GlobalPosition);
        if (wagon.Seats.Take(npc.RiderId, isHero: false) is int seat) npc.SitIn(wagon, seat);
    }

    // ---- the server's tick ------------------------------------------------------------------------

    protected override void Tick(float dt, Main main)
    {
        if (Completed) return;
        if (_wagons.Count == 0 || (_crew.Count == 0 && ClosedCages == 0)) { Complete(); return; } // no cage could stand anywhere (Build warned), or nobody to bring home: waived
        Evaluate();
        if (CrewRules.Completed(States(), CrewRules.CagesBlocking(Cages()))) Win();
    }

    /// <summary>Each wagon's (open, left behind), into the reused list.</summary>
    private List<(bool Open, bool LeftBehind)> Cages()
    {
        _cages.Clear();
        foreach (var w in _wagons) _cages.Add((w.Open, LeftBehind(w)));
        return _cages;
    }

    /// <summary>The locked cages that still block the stairs (<see cref="CrewRules.CagesBlocking"/>; test hook).</summary>
    internal int CagesBlockingNow => CrewRules.CagesBlocking(Cages());

    /// <summary>A player left this wagon's prisoners behind (plan 7): its crew count as lost and it no longer blocks the stairs. Not saved.</summary>
    private bool LeftBehind(CageWagon w) => _rails != null && IsInstanceValid(_rails) && _rails.CrewLeftAt.Contains(w.Stop);

    /// <summary>
    /// The rail controller's <see cref="RailController.CageHolds"/>: the stop's cage wagon is locked with living crew (a downed crew
    /// member is living and still holds the cart; the dead do not). No allocation.
    /// </summary>
    private bool HoldsCart(int stop)
    {
        foreach (var wagon in _wagons)
        {
            if (wagon.Stop != stop) continue;
            var living = 0;
            if (!wagon.Open)
                foreach (var c in _crew)
                    if (WagonOf(c) == wagon && IsInstanceValid(c) && !c.Dead) living++;
            return CageHoldRules.Holds(wagon.Open, living);
        }
        return false;
    }

    /// <summary>Where each crew member stands now: caged, freed, down, home (and staying home) or lost.</summary>
    private void Evaluate()
    {
        var atEnd = _rails != null && IsInstanceValid(_rails) && _rails.State.Mode == CartMode.Held && _rails.StopIndex >= _rails.StopS.Length - 1;
        foreach (var npc in _crew)
        {
            var was = StateOf(npc);
            var now = Judge(npc, atEnd);
            if (now == was) continue;
            _state[npc] = now;
            if (now == CrewState.Home) EventBus.Instance.Log($"{npc.NpcName} is home.");
            RefreshHud();
        }
    }

    private CrewState Judge(Npc npc, bool atEnd)
    {
        if (!IsInstanceValid(npc) || npc.Dead) { _home.Remove(npc.Id); return CrewState.Lost; }
        if (WagonOf(npc) is { Open: false } w) return CrewRules.JudgeCaged(LeftBehind(w)); // a cage left behind: its crew are lost
        var inTrain = npc.Ride is CageWagon { Coupled: true };
        var inExit = (inTrain && atEnd) || (!npc.Seated && InExitRoom(npc));
        if (_home.Contains(npc.Id))
        {
            if (!npc.Downed || inExit) return CrewState.Home;
            _home.Remove(npc.Id); // down outside the exit room: out of the count until they are back
            return CrewState.Downed;
        }
        if (npc.Downed) return CrewState.Downed;
        if (!inExit) return CrewState.Freed;
        _home.Add(npc.Id);
        return CrewState.Home;
    }

    private bool InExitRoom(Npc npc) => _exitRoom != null && _exitRoom.Contains(DungeonBuilder.WorldToCell(npc.GlobalPosition));

    private void Win()
    {
        AdventureDirector.Instance?.SetFlag("jailbreak_freed_all", "the prisoners");
        if (_rewardDoor is { IsOpen: false }) { _rewardDoor.Reveal(); _rewardDoor.Open(); EventBus.Instance.Log("Somewhere on this floor, a vault door swings wide for you."); }
        Complete(); // its line is CompletedLine
    }

    protected override string CompletedLine => _wagons.Count == 0 ? ObjectiveText.Completed(FloorObjectiveKind.None) : "The crew are home. The stairs grind open.";

    // ---- the wagons -------------------------------------------------------------------------------

    /// <summary>
    /// The same rule on every peer (<see cref="PropPromptRules.CagePrompt"/>). A client reads its copies: the cage's door and the keys
    /// as the server told them, Grisel and the crew (up, down, following, seated) from the snapshots and the seats, the cart's stop
    /// and mode from its state; the server judges the use again when the intent arrives.
    /// </summary>
    private string WagonPrompt(CageWagon wagon, Adventurer hero)
        => wagon.Lost ? "" : PropPromptRules.CagePrompt(wagon.Open, OrcKeys, !wagon.Open && !OrcKeys && GriselOpens(wagon) is not null, wagon.Open && CanLetOut(wagon),
            wagon.Open ? DownedAboard(wagon)?.NpcName : null);

    /// <summary>A hero used the wagon: the server's (or solo) work, a remote player's through its InteractIntent (the server checked the reach); a hero who is down does nothing.</summary>
    private void UseWagon(CageWagon wagon, Adventurer actor)
    {
        if (NetSession.IsClient || actor.Downed || wagon.Lost || Main.Instance is not { InGame: true } main || main.Builder.Layout != Layout) return;
        var party = main.Party;
        if (!wagon.Open)
        {
            if (OrcKeys) EventBus.Instance.Log("The orc's keys turn in the cage's padlock.");
            else if (GriselOpens(wagon) is { } grisel)
            {
                // her word at the cage goes out as her own line once the channel is free (npc.Say here, NpcSay to every screen on a server)
                EventBus.Instance.Log("Grisel works the lock.");
                var at = Game.Instance.PlaySeconds;
                if (GriselCall(RideLines.CageCall) is { } call) AdventureDirector.Instance?.SayAs(grisel, call, $"grisel:cage{wagon.Stop}", () => Game.Instance.PlaySeconds - at <= CageCallSeconds);
            }
            else if (!party.TryUnlock("the cage", out _, wagon.GlobalPosition, objectiveLock: true)) return;
            OpenCage(wagon, party);
            return;
        }
        if (CanLetOut(wagon)) { LetOut(wagon); return; }
        DownedAboard(wagon)?.TryRevive();
    }

    /// <summary>Grisel, alive and up, following the party within <see cref="GriselReach"/> of the wagon (or seated in the cart held at its stop); else null.</summary>
    private Npc? GriselOpens(CageWagon wagon)
    {
        if (_grisel == null || !IsInstanceValid(_grisel) || _grisel is not { Dead: false, Downed: false } g) return null;
        if (g.Escorting && !g.Seated && g.GlobalPosition.DistanceTo(wagon.GlobalPosition) <= GriselReach) return g;
        if (_rails != null && IsInstanceValid(_rails) && g.Cart == _rails.Cart && _rails.State.Mode == CartMode.Held && !_rails.Halted && _rails.StopIndex == wagon.Stop) return g; // halted on the line is not at the stop
        return null;
    }

    /// <summary>
    /// An open wagon with crew aboard lets them out only when it cannot ride: on its siding while the cart is not held at
    /// its stop (it would couple at the departure), or anywhere once the cart is wrecked or the line is done (no carts
    /// left). Crew let out walk with the party and never re-board, so the choice is not offered while a ride waits.
    /// </summary>
    private bool CanLetOut(CageWagon wagon)
    {
        if (!wagon.Open || !AnyAboard(wagon)) return false;
        if (_rails == null || !IsInstanceValid(_rails) || _rails.State.Mode == CartMode.Wrecked) return true; // wrecked with no spare to take over: on foot
        if (wagon.Coupled) return false;
        return !(_rails.State.Mode == CartMode.Held && !_rails.Halted && _rails.StopIndex == wagon.Stop); // halted on the line past its stop: the wagon is left behind
    }

    private bool AnyAboard(CageWagon wagon)
    {
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && !npc.Dead && npc.Ride == wagon) return true;
        return false;
    }

    /// <summary>The living crew seated in <paramref name="wagon"/>.</summary>
    private List<Npc> Aboard(CageWagon wagon)
    {
        var aboard = new List<Npc>(CrewRules.PerWagon);
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && !npc.Dead && npc.Ride == wagon) aboard.Add(npc);
        return aboard;
    }

    private Npc? DownedAboard(CageWagon wagon)
    {
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && !npc.Dead && npc.Downed && npc.Ride == wagon) return npc;
        return null;
    }

    /// <summary>The cage opens: its crew are free (XP, the walk-in jailbreak's flag each), and once every cage is open, all are.</summary>
    private void OpenCage(CageWagon wagon, PartyController party)
    {
        wagon.SetOpen(true);
        if (!NetSession.IsClient) EventBus.Instance.RaisePropChanged(NetRefs.Wagon(wagon.Stop), "opened");
        EventBus.Instance.PlaySfx("door");
        var crew = _crew.Where(c => WagonOf(c) == wagon).ToList();
        foreach (var npc in crew)
        {
            if (!IsInstanceValid(npc) || npc.Dead) continue;
            npc.Freed = true;
            _state[npc] = npc.Downed ? CrewState.Downed : CrewState.Freed;
            party.AwardXp(JailbreakRules.XpForFreeing(Floor));
            AdventureDirector.Instance?.SetFlag($"freed:{npc.Id}", npc.NpcName);
            EventBus.Instance.RaiseNpcEvent(npc.Id, NpcEventKind.Freed);
        }
        var names = crew.Count switch
        {
            0 => "",
            1 => crew[0].NpcName,
            _ => $"{string.Join(", ", crew.Take(crew.Count - 1).Select(c => c.NpcName))} and {crew[^1].NpcName}",
        };
        if (names.Length > 0)
        {
            EventBus.Instance.Log(crew.Count == 1 ? $"{names} is free." : $"{names} are free.");
            Raise(ObjectiveTrigger.Freed, names);
        }
        if (ClosedCages == 0) Raise(ObjectiveTrigger.AllFreed);
        RefreshHud();
    }

    /// <summary>The crew of an open wagon climb out beside it and follow the party on foot.</summary>
    private void LetOut(CageWagon wagon)
    {
        var aboard = Aboard(wagon);
        var cells = FreeCellsNear(wagon.Coupled ? DungeonBuilder.WorldToCell(wagon.GlobalPosition) : wagon.Cell, aboard.Count);
        for (var i = 0; i < aboard.Count; i++)
        {
            var npc = aboard[i];
            npc.GetOut(_builder!.Surface(cells[Math.Min(i, cells.Count - 1)]) + Vector3.Up * 0.1f);
            if (!npc.Escorting) npc.StartFollowing();
        }
        RefreshHud();
    }

    /// <summary>Up to <paramref name="count"/> distinct walkable cells round <paramref name="at"/>, nearest first: no rail, nothing blocking, no siding.</summary>
    private List<GridPos> FreeCellsNear(GridPos at, int count)
    {
        var cells = new List<GridPos>();
        var sidings = _wagons.Select(w => w.Cell).ToHashSet();
        for (var r = 1; r <= 3 && cells.Count < count; r++)
            for (var dy = -r; dy <= r && cells.Count < count; dy++)
                for (var dx = -r; dx <= r && cells.Count < count; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var c = new GridPos(at.X + dx, at.Y + dy);
                    if (!Layout.IsWalkable(c) || Layout.IsRail(c) || sidings.Contains(c) || _builder!.IsCellBlocked(c) || Layout.LevelOf(c) != Layout.LevelOf(at)) continue;
                    cells.Add(c);
                }
        if (cells.Count == 0) cells.Add(DungeonBuilder.WalkableNeighbor(Layout, at));
        return cells;
    }

    /// <summary>The cart left a stop: an open wagon there with crew aboard couples on behind, and Grisel on the bench names the cage ahead.</summary>
    private void OnCartEvent(CartEvent e)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails)) return;
        if (e.Kind == CartEventKind.OnFoot) // no cart left: every coupled wagon's crew get out and follow the lead
        {
            foreach (var wagon in _wagons)
                if (wagon.Coupled && wagon.Open && AnyAboard(wagon)) LetOut(wagon);
            return;
        }
        if (e.Kind is CartEventKind.Boarding or CartEventKind.CageHeld) { WarnLocked(e.Stop); return; }
        if (e.Kind != CartEventKind.Departed) return;
        foreach (var wagon in _wagons)
        {
            if (wagon.Stop != e.Stop || !wagon.Open || wagon.Coupled || wagon.Lost || !AnyAboard(wagon)) continue;
            Couple(wagon);
            EventBus.Instance.Log("The cage wagon couples on behind.");
        }
        GriselNamesTheRide(e);
    }

    /// <summary>
    /// Forgiven: Grisel, up and seated in the party's cart, calls the ride ahead (her crew in the next cage, then the stair),
    /// once a run. Her call carries that departure: the orc's RideStart taunt is not raised for it (the ride's opening would
    /// otherwise fill the channel). A call still waiting when the cart has left the stop it rides to is dropped, so it never
    /// names the wrong cage. The departure is claimed when the call is queued, not when it is said: a call later dropped as
    /// stale (<see cref="RideLines.CallStillDue"/> false, the channel busy until the cart moved on) does not give the taunt back,
    /// so that departure has neither her line nor the orc's (rare; accepted, ruled 2026-11-18).
    /// </summary>
    private void GriselNamesTheRide(CartEvent e)
    {
        if (_grisel is not { Dead: false, Downed: false } g || !IsInstanceValid(g) || _rails == null || g.Cart != _rails.Cart) return;
        if (RideLines.GriselCallFor(e.Kind, e.Stop) is not { } key || GriselCall(key) is not { } call) return;
        var rails = _rails;
        var left = e.Stop;
        if (AdventureDirector.Instance?.SayAs(g, call, $"grisel:{key}", () => IsInstanceValid(rails)
            && RideLines.CallStillDue(rails.StopIndex, left, rails.State.Mode == CartMode.Held, rails.Halted)) == true)
            rails.ClaimDeparture(); // claimed at the queue: a call dropped as stale later leaves this departure silent (no taunt back)
    }

    /// <summary>Keys of Grisel's calls the floor's plan lacks, warned about once.</summary>
    private readonly HashSet<string> _callsMissing = new();

    /// <summary>Grisel's call for <paramref name="key"/> in the floor's plan; one it lacks is warned about once and nothing is said.</summary>
    private Line? GriselCall(string key)
    {
        var line = Main.Instance?.Builder.Contents?.Plan?.Npcs.FirstOrDefault(n => n.Id == GriselId)?.CallFor(key);
        if (line == null && _callsMissing.Add(key)) GD.PushWarning($"[CrewRescue] the floor's plan gives Grisel no \"{key}\" call: nothing said");
        return line;
    }

    /// <summary>Stops whose locked cage has been pointed out: once per stop.</summary>
    private readonly HashSet<int> _warnedLocked = new();

    /// <summary>The boarding starts (or the cage holds the cart) at a stop whose cage is still locked: say so, once.</summary>
    private void WarnLocked(int stop)
    {
        if (!_wagons.Any(w => w.Stop == stop && !w.Open) || !_warnedLocked.Add(stop)) return;
        EventBus.Instance.Log("The cage here is still locked.");
    }

    private void Couple(CageWagon wagon)
    {
        wagon.SetLost(false); // a wagon on the train is never lost
        _rails!.Couple(wagon);
        _builder?.UnblockCell(wagon.Cell);
        foreach (var npc in Aboard(wagon)) npc.FollowSeat();
    }

    // ---- prisoners (IPrisonerHost) ----------------------------------------------------------------

    /// <summary>The crew are reached through their wagon; on foot only a downed one is (Revive, which the NPC offers itself).</summary>
    public string PromptFor(Npc npc) => "";

    public bool OnInteract(Npc npc, PartyController party, Adventurer actor) => false;

    // ---- save -------------------------------------------------------------------------------------

    public override ObjectiveStateDto Capture()
    {
        var dto = base.Capture();
        dto.FreedNpcs = _crew.Where(c => StateOf(c) != CrewState.Caged).Select(c => c.Id).ToList();
        dto.OpenCages = _wagons.Where(w => w.Open).Select(w => w.Stop).ToList();
        dto.CoupledCages = _rails != null && IsInstanceValid(_rails) ? _rails.Train.Select(w => w.Stop).ToList() : new List<int>();
        dto.HomeCrew = _crew.Where(c => _home.Contains(c.Id)).Select(c => c.Id).ToList();
        dto.LostCrew = _crew.Where(c => !IsInstanceValid(c) || c.Dead).Select(c => c.Id).ToList();
        dto.LostCages = _wagons.Any(w => w.Lost) ? _wagons.Where(w => w.Lost).Select(w => w.Stop).ToList() : null;
        dto.OrcKeys = OrcKeys;
        dto.GatesUp = _rails != null && IsInstanceValid(_rails) ? _rails.GatesUpList.ToList() : null;
        dto.CrewDownedLeft = NetSession.IsServer ? DownedLeft() : null; // the broadcast's (a client counts its labels down); a solo save stays as it was
        return dto;
    }

    /// <summary>The seconds each downed crew member has left to bleed, by id; null when nobody is down.</summary>
    private Dictionary<string, float>? DownedLeft()
    {
        Dictionary<string, float>? left = null;
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && npc.Downed && !npc.Dead) (left ??= new())[npc.Id] = Math.Max(0f, CrewRules.BleedOutSeconds - npc.DownedSeconds);
        return left;
    }

    /// <summary>Client: the downed crew's seconds left as the last broadcast gave them (the RIDE pass reads it), or null.</summary>
    internal IReadOnlyDictionary<string, float>? NetDownedLeft { get; private set; }

    /// <summary>
    /// Back from a save, after the floor's NPC states (alive, down, following, where they stood) and the cart (held at
    /// the stop it had reached) are applied: the cages open as saved, the train coupled again behind the cart, the crew
    /// reseated (seats are not saved): caged ones and the train's in their wagons, an open siding's in theirs unless they
    /// had been let out. The lost lie dead on the floor, and those home stand in the exit room unless the train holds
    /// them at the end of the line.
    /// </summary>
    public override void Restore(ObjectiveStateDto dto)
    {
        if (NetSession.IsClient) { ShowForward(dto); ApplyNet(dto); return; } // a join or revisit: a client only reads the state (the seats, the train and the doors are the server's)
        base.Restore(dto);
        OrcKeys = dto.OrcKeys ?? false;
        if (dto.GatesUp != null && _rails != null && IsInstanceValid(_rails)) _rails.RestoreGates(dto.GatesUp);
        _grisel = _root?.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == GriselId);
        var open = dto.OpenCages ?? new List<int>();
        foreach (var wagon in _wagons) wagon.SetOpen(open.Contains(wagon.Stop));
        _home.Clear();
        foreach (var id in dto.HomeCrew ?? new List<string>()) _home.Add(id);
        var lost = (dto.LostCrew ?? new List<string>()).ToHashSet();
        var lostCages = dto.LostCages ?? new List<int>();
        foreach (var wagon in _wagons)
        {
            wagon.SetLost(lostCages.Contains(wagon.Stop)); // gone into a crevasse with the train: hidden for good, its siding free
            if (wagon.Lost) _builder?.UnblockCell(wagon.Cell);
        }
        if (_rails != null && IsInstanceValid(_rails))
            foreach (var stop in dto.CoupledCages ?? new List<int>())
                if (_wagons.FirstOrDefault(w => w.Stop == stop) is { Open: true, Coupled: false } wagon) Couple(wagon);
        var atEnd = _rails != null && IsInstanceValid(_rails) && _rails.State.Mode == CartMode.Held && _rails.StopIndex >= _rails.StopS.Length - 1;
        var exitCells = ExitCells();
        var nextExit = 0;
        // every spare spent: crew let out on foot (the train wrecked, no carts left) stay on foot though the load brings the cart back whole
        var onFootStays = _rails != null && IsInstanceValid(_rails) && _rails.SparesLeft == 0;
        foreach (var npc in _crew)
        {
            if (!IsInstanceValid(npc)) continue;
            var wagon = WagonOf(npc); // null: freed at the landing (no wagon had room for them)
            npc.Freed = wagon?.Open ?? true;
            if (lost.Contains(npc.Id) && !npc.Dead) npc.Restore(true, false, false, 0f, null);
            if (npc.Dead)
            {
                _home.Remove(npc.Id);
                if (npc.Seated) npc.GetOut(_builder!.Surface(DungeonBuilder.WorldToCell(npc.GlobalPosition)));
                continue;
            }
            if (wagon is { Open: false }) { Seat(npc, wagon); continue; }
            if (_home.Contains(npc.Id))
            {
                if (wagon is { Coupled: true } && atEnd) { Seat(npc, wagon); continue; }
                var cell = exitCells.Count > 0 ? exitCells[nextExit++ % exitCells.Count] : DungeonBuilder.WorldToCell(npc.GlobalPosition);
                npc.GetOut(_builder!.Surface(cell) + Vector3.Up * 0.1f);
                if (!npc.Escorting) npc.Restore(false, true, false, npc.Hp, null, npc.Downed);
                continue;
            }
            if (wagon == null) continue; // on foot where the save had them
            if (!npc.Escorting || (wagon.Coupled && !onFootStays)) Seat(npc, wagon);
            else if (npc.Seated) npc.GetOut(npc.GlobalPosition); // let out before the save: on foot where it stood
        }
        _state.Clear();
        foreach (var npc in _crew) _state[npc] = npc.Dead ? CrewState.Lost : WagonOf(npc) is { Open: false } ? CrewState.Caged : _home.Contains(npc.Id) ? CrewState.Home : CrewState.Freed;
        Evaluate();
        if (Completed && _rewardDoor is { IsOpen: false }) { _rewardDoor.Reveal(); _rewardDoor.Open(silent: true); }
        RefreshHud();
    }

    /// <summary>
    /// Client, at the floor's build (a join, a revisit, a resync): the cages the floor state names open and the orc's keys taken, at
    /// once and forward only (a cage is never shut and the keys never given back here). The floor state is the server's at that
    /// build, so a revisited floor does not show its cages shut until the prop check; after the build the cages and the keys come
    /// only from the PropEvts and the prop state (one writer per fact, as <see cref="ApplyNet"/> says).
    /// </summary>
    private void ShowForward(ObjectiveStateDto dto)
    {
        if (dto.OpenCages is { } open)
            foreach (var wagon in _wagons)
                if (IsInstanceValid(wagon) && !wagon.Open && open.Contains(wagon.Stop)) wagon.SetOpen(true);
        if (dto.OrcKeys == true && !OrcKeys) OrcKeys = true; // a client's setter takes the keyring off Grisel's belt and tells nothing
        ShowLost(dto);
    }

    /// <summary>Client: the wagons the state names lost are hidden, forward only (a wagon lost into a crevasse never comes back on this floor visit).</summary>
    private void ShowLost(ObjectiveStateDto dto)
    {
        if (dto.LostCages is not { } lost) return;
        foreach (var wagon in _wagons)
            if (IsInstanceValid(wagon) && !wagon.Lost && !wagon.Coupled && lost.Contains(wagon.Stop)) wagon.SetLost(true);
    }

    /// <summary>The exit room's free floor (no rail, nothing blocking), nearest the down stairs first.</summary>
    private List<GridPos> ExitCells()
    {
        if (_exitRoom == null || _builder == null) return new List<GridPos>();
        var stairs = Layout.DownStairs;
        return RoomCells.Free(Layout, _exitRoom).Where(c => !Layout.IsRail(c) && !_builder.IsCellBlocked(c)).OrderBy(c => Sq(c, stairs)).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
    }

    /// <summary>
    /// A client's copy for the HUD: home and lost crew from the server's state (the wagons' poses and the crew's seats are plan 5).
    /// The open cages and the orc's keys are rail props: they come only from the server's PropEvts and the prop check's rail
    /// part, never from here (one writer per fact: a stale objective state cannot undo a newer PropEvt).
    /// </summary>
    public override void ApplyNet(ObjectiveStateDto dto)
    {
        base.Restore(dto);
        var home = (dto.HomeCrew ?? new List<string>()).ToHashSet();
        var lost = (dto.LostCrew ?? new List<string>()).ToHashSet();
        ShowLost(dto); // the lost wagons (the cart state's Fell hid them already on a fall seen here)
        foreach (var npc in _crew)
            _state[npc] = lost.Contains(npc.Id) ? CrewState.Lost
                : WagonOf(npc) is { Open: false } ? CrewState.Caged
                : home.Contains(npc.Id) ? CrewState.Home
                : IsInstanceValid(npc) && npc.Downed ? CrewState.Downed : CrewState.Freed;
        // the bleed-out clocks: each downed crew member's label counts down here from the server's seconds left (refreshed each broadcast)
        NetDownedLeft = dto.CrewDownedLeft;
        if (dto.CrewDownedLeft is { } left)
            foreach (var npc in _crew)
                if (IsInstanceValid(npc) && left.TryGetValue(npc.Id, out var secondsLeft)) npc.ApplyBleedLeft(secondsLeft);
    }

    /// <summary>
    /// Test hook (--test-crevasse): the stop's cage opened (as a key would) and its wagon coupled on behind the cart with its crew aboard,
    /// as a departure couples it. False when there is no such wagon, it is coupled or lost already, or this is a client.
    /// </summary>
    internal bool DebugCouple(int stop)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails) || Main.Instance?.Party is not { } party) return false;
        if (_wagons.FirstOrDefault(w => w.Stop == stop) is not { Coupled: false, Lost: false } wagon) return false;
        if (!wagon.Open) OpenCage(wagon, party);
        Couple(wagon);
        return true;
    }

    /// <summary>Open every cage and bring every living crew member to the exit room (tests).</summary>
    public override void Hurry()
    {
        if (Main.Instance?.Party is not { } party) return;
        foreach (var wagon in _wagons.Where(w => !w.Open).ToList()) OpenCage(wagon, party);
        var cells = ExitCells();
        var i = 0;
        foreach (var npc in _crew)
        {
            if (!IsInstanceValid(npc) || npc.Dead) continue;
            if (npc.Downed) npc.TryRevive();
            if (cells.Count > 0) npc.GetOut(_builder!.Surface(cells[i++ % cells.Count]) + Vector3.Up * 0.1f);
            if (!npc.Escorting) npc.StartFollowing();
        }
    }
}
