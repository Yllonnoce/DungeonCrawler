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
/// The jailbreak on a railed floor: Grisel's crew, two to a cage, wait in an iron cage on the siding beside each intermediate stop's
/// platform (<see cref="PlatformCage"/>), and the party's train (plan 14: <see cref="RailController.BuildTrain"/>, one open wagon per cage,
/// coupled from the landing) has a seat for each. The party opens a cage (the orc's keys, Grisel's voice, or a key or a pick); its crew
/// are freed, get out at its door and follow the party, and whenever the train is held at a stop short of the last every freed crew
/// member up, on foot and within <see cref="TrainRules.BoardReach"/> books the first empty seat, walks to it and sits in
/// (<see cref="TickBoarding"/>; the countdown waits for them up to <see cref="TrainRules.BoardMax"/>). A crew member is home in the exit
/// room (seated in the train once the cart is at the end of the line, or on foot there); a dead one is lost. Every cage open and
/// every crew member home or lost opens the reward vault and the stairs. Server only for every change; a client reads the 2 s
/// objective state for the HUD, shows the cages and the keys as the server tells them, and offers the cages' and the keyring's
/// prompts from its copies (<see cref="PropPromptRules"/>; a use is an InteractIntent).
/// </summary>
public partial class CrewRescueController : ObjectiveController, IPrisonerHost
{
    /// <summary>Grisel opens a cage when she stands this close to it, following the party.</summary>
    public const float GriselReach = 8f;
    public const string GriselId = "npc_grisel";
    /// <summary>Grisel's word at a cage she opens is said within this long of the lock, or not at all (it belongs to the lock).</summary>
    public const double CageCallSeconds = 10;

    private readonly List<PlatformCage> _cages = new();
    private readonly List<Npc> _crew = new();
    private readonly Dictionary<Npc, PlatformCage> _cageOf = new();
    private readonly Dictionary<Npc, CrewState> _state = new();
    private readonly HashSet<string> _home = new();
    private readonly List<CrewState> _states = new();
    /// <summary>Each cage's (open, left behind) for the win rule, reused every tick.</summary>
    private readonly List<(bool Open, bool LeftBehind)> _cageStates = new();
    private readonly List<bool> _uncaged = new();
    private RailController? _rails;
    private DungeonBuilder? _builder;
    private Node3D? _root;
    private Room? _exitRoom;
    private Door? _jailDoor, _rewardDoor;
    private Npc? _grisel;

    /// <summary>The platform cages placed, in stop order (one per intermediate stop with room for it).</summary>
    public IReadOnlyList<PlatformCage> Cages => _cages;
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
        foreach (var cage in _cages)
            if (cage.Stop == stop && !cage.Open) { cage.SetOpen(true); return true; }
        return false;
    }
    /// <summary>The dead Grisel on the condemned branch (null on the forgiven one).</summary>
    public Npc? Body { get; private set; }
    public CrewState StateOf(Npc npc) => _state.GetValueOrDefault(npc, CrewState.Caged);
    /// <summary>Cages still locked.</summary>
    public int ClosedCages { get { var n = 0; foreach (var c in _cages) if (!c.Open) n++; return n; } }

    public override string HudText => CrewRules.HudText(States(), Uncaged());

    /// <summary>Each crew member's cage stands open, or they had none (the HUD's "freed"), into the reused list.</summary>
    private List<bool> Uncaged()
    {
        _uncaged.Clear();
        foreach (var npc in _crew) _uncaged.Add(CageOf(npc) is not { Open: false });
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
            if (Equals(_rails.CrewBoardingHolds, (Func<int, bool>)HoldsDeparture)) _rails.CrewBoardingHolds = null;
            if (Equals(_rails.WagonCrewPrompt, (Func<TrainWagon, Adventurer, string>)WagonCrewPrompt)) _rails.WagonCrewPrompt = null;
            if (Equals(_rails.WagonCrewUse, (Action<TrainWagon, Adventurer>)WagonCrewUse)) _rails.WagonCrewUse = null;
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
            GD.PushError("[CrewRescue] a railed jailbreak without a line to put its cages beside");
            return;
        }
        _rails.CartEvents += OnCartEvent;
        _rails.CageHolds = HoldsCart;
        _rails.CrewBoardingHolds = HoldsDeparture;
        _rails.WagonCrewPrompt = WagonCrewPrompt; // every role: a client offers the revive from its copies, as the server takes it
        _rails.WagonCrewUse = WagonCrewUse;
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
            if (PickSiding(builder, rail.Stops[stop], taken) is not { } pick) { GD.PushWarning($"[CrewRescue] stop {stop}: no free cell beside the platform for a cage"); continue; }
            taken.Add(pick.Cell);
            PlaceCage(builder, root, track, stop, pick.Cell, pick.Platform);
        }
        _rails.BuildTrain(TrainRules.WagonsFor(_cages.Count)); // every role: the train from the landing, one wagon per cage placed
        if (_cages.Count == 0)
        {
            // nowhere to put a cage: the floor must not stay sealed, so the trial is waived (the first server tick completes it)
            GD.PushWarning("[CrewRescue] no stop had room for a cage: the crew trial is waived and the stairs open");
            return;
        }
        // the crew fill the cages in stop order, two to a cage; a stop that had no room passes its crew on to the cages
        // that stand, and whoever is left over when those are full waits freed at the landing and follows the party
        var next = 0;
        foreach (var cage in _cages)
            for (var k = 0; k < CrewRules.PerWagon && next < plans.Count; k++, next++)
            {
                var npc = SpawnCrew(builder, root, plans[next], cage.Cell);
                _cageOf[npc] = cage;
                _state[npc] = CrewState.Caged;
                if (!NetSession.IsClient) Seat(npc, cage); // a client's crew stand where the snapshots put them
            }
        var landing = next < plans.Count ? LandingCells(rail.Stops[0], taken) : new List<GridPos>();
        for (var i = 0; next < plans.Count; next++, i++)
        {
            var cell = landing.Count > 0 ? landing[i % landing.Count] : Layout.UpStairs;
            var npc = SpawnCrew(builder, root, plans[next], cell);
            npc.Freed = true;
            _state[npc] = CrewState.Freed;
            if (!NetSession.IsClient) npc.Restore(false, true, false, npc.MaxHp, null); // following from the start, without a log line each
            GD.PushWarning($"[CrewRescue] {npc.NpcName} has no cage: freed at the landing on cell {cell}");
        }
        if (!IsForgiven()) PlaceBody(builder, root, taken);
        foreach (var cage in _cages)
            GD.Print($"[CrewRescue] stop {cage.Stop} (room {rail.Stops[cage.Stop].Room}): cage on cell {cage.Cell}, crew {string.Join(", ", _crew.Where(c => CageOf(c) == cage).Select(c => c.NpcName))}");
    }

    private static bool IsForgiven() => Game.Instance.Adventure.Memory.Flags.Contains("grisel_forgiven");

    /// <summary>
    /// Condemned: Grisel lies dead on a free cell beside cage 1, no dialogue, her keyring at her belt. Not saved on its own:
    /// built again on every build of the floor, with the keyring hidden when the save says the keys are taken.
    /// </summary>
    private void PlaceBody(DungeonBuilder builder, Node3D root, HashSet<GridPos> taken)
    {
        var cage = _cages[0];
        var cell = FreeCellsNear(cage.Cell, 1)[0];
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
        GD.Print($"[CrewRescue] Grisel lies dead on cell {cell} beside cage {cage.Stop}, her keys at her belt");
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
        _crewIds.Add(npc.Id);
        return npc;
    }

    private readonly HashSet<string> _crewIds = new();
    private static readonly HashSet<string> EscortIdSet = new() { GriselId };
    /// <summary>The crew's ids: a wipe's rise (<see cref="Save.RailArrival.Merge"/>) always brings them back as the last stop's arrival had them.</summary>
    public IReadOnlySet<string> CrewIds => _crewIds;
    /// <summary>The escort's id (Grisel): a wipe's rise brings her back as the arrival had her unless she died or turned since, and a fall never kills her (<see cref="Dungeon.CrevasseRules.NpcBody"/>).</summary>
    public static IReadOnlySet<string> EscortIds => EscortIdSet;

    /// <summary>Free floor of the start room, off the line and away from anything standing there, nearest its platform's middle.</summary>
    private List<GridPos> LandingCells(RailStop start, HashSet<GridPos> taken)
    {
        if (start.Room < 0 || start.Room >= Layout.Rooms.Count || _builder == null) return new List<GridPos>();
        var mid = start.Platform.Count > 0 ? start.Platform[start.Platform.Count / 2] : Layout.UpStairs;
        return RoomCells.Free(Layout, Layout.Rooms[start.Room]).Where(c => !Layout.IsRail(c) && !_builder.IsCellBlocked(c) && !taken.Contains(c))
            .OrderBy(c => Sq(c, mid)).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
    }

    /// <summary>The cage a crew member was caged in, or null for one freed at the landing.</summary>
    private PlatformCage? CageOf(Npc npc) => _cageOf.GetValueOrDefault(npc);

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

    /// <summary>The cage on its siding cell, facing along the track beside it; the cell is blocked and kept clear of riders getting out.</summary>
    private void PlaceCage(DungeonBuilder builder, Node3D root, RailTrack track, int stop, GridPos cell, GridPos platform)
    {
        var at = builder.Surface(cell);
        var beside = builder.Surface(platform);
        var s = track.Curve.GetClosestOffset(track.ToLocal(beside));
        var along = -(track.GlobalTransform * track.Curve.SampleBakedWithRotation(s, true, true)).Basis.Z;
        var basis = new Basis(Vector3.Up, Mathf.Atan2(-along.X, -along.Z));
        var side = (beside - at).Dot(basis.X) >= 0f ? 1 : -1;
        var cage = new PlatformCage { Name = $"Cage{stop}", Stop = stop, Cell = cell, PlatformSide = side };
        cage.PromptFor = hero => CagePrompt(cage, hero);
        cage.OnUse = hero => UseCage(cage, hero);
        root.AddChild(cage);
        cage.GlobalTransform = new Transform3D(basis, at);
        builder.BlockCell(cell);
        _rails?.ReserveSiding(cell);
        _cages.Add(cage);
    }

    /// <summary>Book a bench seat in <paramref name="body"/> (a cage, a wagon) and sit the NPC there (off any other ride first).</summary>
    private static void Seat(Npc npc, ICartBody body)
    {
        if (npc.Cart == body) { npc.FollowSeat(); return; }
        if (npc.Seated) npc.GetOut(npc.GlobalPosition);
        if (body.Seats.Take(npc.RiderId, isHero: false) is int seat) npc.SitIn(body, seat);
    }

    // ---- the server's tick ------------------------------------------------------------------------

    protected override void Tick(float dt, Main main)
    {
        if (main.RiseAtCartPending) return; // a wipe's rise is coming: the floor is built again from the arrival, and nothing won now may outlive it
        TickBoarding(dt);
        if (Completed) return;
        if (_cages.Count == 0 || (_crew.Count == 0 && ClosedCages == 0)) { Complete(); return; } // no cage could stand anywhere (Build warned), or nobody to bring home: waived
        Evaluate();
        if (CrewRules.Completed(States(), CrewRules.CagesBlocking(CageStates()))) Win();
    }

    /// <summary>Each cage's (open, left behind), into the reused list.</summary>
    private List<(bool Open, bool LeftBehind)> CageStates()
    {
        _cageStates.Clear();
        foreach (var c in _cages) _cageStates.Add((c.Open, LeftBehind(c)));
        return _cageStates;
    }

    /// <summary>The locked cages that still block the stairs (<see cref="CrewRules.CagesBlocking"/>; test hook).</summary>
    internal int CagesBlockingNow => CrewRules.CagesBlocking(CageStates());

    /// <summary>A player left this cage's prisoners behind (plan 7): its crew count as lost and it no longer blocks the stairs. Not saved.</summary>
    private bool LeftBehind(PlatformCage c) => _rails != null && IsInstanceValid(_rails) && _rails.CrewLeftAt.Contains(c.Stop);

    /// <summary>
    /// The rail controller's <see cref="RailController.CageHolds"/>: the stop's cage is locked with living crew (a downed crew
    /// member is living and still holds the cart; the dead do not). No allocation.
    /// </summary>
    private bool HoldsCart(int stop)
    {
        foreach (var cage in _cages)
        {
            if (cage.Stop != stop) continue;
            var living = 0;
            if (!cage.Open)
                foreach (var c in _crew)
                    if (CageOf(c) == cage && IsInstanceValid(c) && !c.Dead) living++;
            return CageHoldRules.Holds(cage.Open, living);
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
        if (CageOf(npc) is { Open: false } cage) return CrewRules.JudgeCaged(LeftBehind(cage)); // a cage left behind: its crew are lost
        var inTrain = npc.Ride is TrainWagon { Coupled: true };
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

    /// <summary>The flag every crew member home (or lost) sets for the run.</summary>
    public const string FreedAllFlag = "jailbreak_freed_all";
    /// <summary>This build's win set <see cref="FreedAllFlag"/> (it was not set before): a wipe's rise from an arrival before the win takes it back.</summary>
    private bool _setFreedAll;

    private void Win()
    {
        _setFreedAll = AdventureDirector.Instance is { } director && !director.HasFlag(FreedAllFlag);
        AdventureDirector.Instance?.SetFlag(FreedAllFlag, "the prisoners");
        if (_rewardDoor is { IsOpen: false }) { _rewardDoor.Reveal(); _rewardDoor.Open(); EventBus.Instance.Log("Somewhere on this floor, a vault door swings wide for you."); }
        Complete(); // its line is CompletedLine
    }

    /// <summary>
    /// A wipe rises at a stop the cart reached before the crew were won (<see cref="Save.RailArrival.Merge"/> takes the completion back
    /// with the train): the run forgets the flag this build's win set. The stairs follow the merged state when the floor is built again.
    /// </summary>
    public void TakeBackWin()
    {
        if (!_setFreedAll) return;
        _setFreedAll = false;
        AdventureDirector.Instance?.ClearFlag(FreedAllFlag);
        GD.Print($"[CrewRescue] the wipe rises before the crew were won: {FreedAllFlag} taken back");
    }

    protected override string CompletedLine => _cages.Count == 0 ? ObjectiveText.Completed(FloorObjectiveKind.None) : "The crew are home. The stairs grind open.";

    // ---- the cages ---------------------------------------------------------------------------------

    /// <summary>
    /// The same rule on every peer (<see cref="PropPromptRules.CagePrompt"/>). A client reads its copies: the cage's door and the keys
    /// as the server told them, Grisel and the crew (up, down, following, seated) from the snapshots and the seats, the cart's stop
    /// and mode from its state; the server judges the use again when the intent arrives.
    /// </summary>
    private string CagePrompt(PlatformCage cage, Adventurer hero)
        => PropPromptRules.CagePrompt(cage.Open, OrcKeys, !cage.Open && !OrcKeys && GriselOpens(cage) is not null, cage.Open ? DownedIn(cage)?.NpcName : null);

    /// <summary>A hero used the cage: the server's (or solo) work, a remote player's through its InteractIntent (the server checked the reach); a hero who is down does nothing.</summary>
    private void UseCage(PlatformCage cage, Adventurer actor)
    {
        if (NetSession.IsClient || actor.Downed || Main.Instance is not { InGame: true } main || main.Builder.Layout != Layout) return;
        var party = main.Party;
        if (!cage.Open)
        {
            if (OrcKeys) EventBus.Instance.Log("The orc's keys turn in the cage's padlock.");
            else if (GriselOpens(cage) is { } grisel)
            {
                // her word at the cage goes out as her own line once the channel is free (npc.Say here, NpcSay to every screen on a server)
                EventBus.Instance.Log("Grisel works the lock.");
                var at = Game.Instance.PlaySeconds;
                if (GriselCall(RideLines.CageCall) is { } call) AdventureDirector.Instance?.SayAs(grisel, call, $"grisel:cage{cage.Stop}", () => Game.Instance.PlaySeconds - at <= CageCallSeconds);
            }
            else if (!party.TryUnlock("the cage", out _, cage.GlobalPosition, objectiveLock: true)) return;
            OpenCage(cage, party);
            return;
        }
        DownedIn(cage)?.TryRevive(); // up again, they get out and follow (TickBoarding)
    }

    /// <summary>Grisel, alive and up, following the party within <see cref="GriselReach"/> of the cage (or seated in the cart held at its stop); else null.</summary>
    private Npc? GriselOpens(PlatformCage cage)
    {
        if (_grisel == null || !IsInstanceValid(_grisel) || _grisel is not { Dead: false, Downed: false } g) return null;
        if (g.Escorting && !g.Seated && g.GlobalPosition.DistanceTo(cage.GlobalPosition) <= GriselReach) return g;
        if (_rails != null && IsInstanceValid(_rails) && g.Cart == _rails.Cart && _rails.State.Mode == CartMode.Held && !_rails.Halted && _rails.StopIndex == cage.Stop) return g; // halted on the line is not at the stop
        return null;
    }

    /// <summary>A downed living crew member seated in <paramref name="body"/> (a cage, a wagon), or null.</summary>
    private Npc? DownedIn(ICartBody body)
    {
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && !npc.Dead && npc.Downed && npc.Cart == body) return npc;
        return null;
    }

    /// <summary>The rail controller's <see cref="RailController.WagonCrewPrompt"/>, on every peer: "Revive {name}" for a downed crew member seated in the wagon.</summary>
    private string WagonCrewPrompt(TrainWagon wagon, Adventurer hero) => PropPromptRules.WagonPrompt(DownedIn(wagon)?.NpcName);

    /// <summary>The rail controller's <see cref="RailController.WagonCrewUse"/> (server and solo): a downed crew member seated in the wagon is revived.</summary>
    private void WagonCrewUse(TrainWagon wagon, Adventurer actor)
    {
        if (NetSession.IsClient || actor.Downed) return;
        DownedIn(wagon)?.TryRevive();
    }

    /// <summary>The cage opens: its crew are free (XP, the walk-in jailbreak's flag each), and once every cage is open, all are. Its living, up crew get out at its door and follow.</summary>
    private void OpenCage(PlatformCage cage, PartyController party)
    {
        cage.SetOpen(true);
        if (!NetSession.IsClient) EventBus.Instance.RaisePropChanged(cage.NetRef, "opened"); // "cage:{stop}"
        EventBus.Instance.PlaySfx("door");
        var crew = _crew.Where(c => CageOf(c) == cage).ToList();
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
        if (!NetSession.IsClient) { LetOutOfCage(cage); TickBoarding(0f); } // they book at once: a countdown already due must wait for them
        RefreshHud();
    }

    /// <summary>
    /// The living, up crew still standing in the open <paramref name="cage"/> get out at its door and follow the party (Ruling 5); a downed
    /// one stays in it until revived. Server and solo. No allocation when nobody gets out.
    /// </summary>
    private void LetOutOfCage(PlatformCage cage)
    {
        if (!cage.Open) return;
        foreach (var npc in _crew)
        {
            if (!IsInstanceValid(npc) || npc.Dead || npc.Downed || npc.Cart != cage) continue;
            var at = cage.DoorSpot(Math.Max(0, npc.Seat - CartSeats.HeroSeats));
            npc.GetOut(at + Vector3.Up * 0.1f);
            if (!npc.Escorting) npc.StartFollowing();
            GD.Print($"[CrewRescue] {npc.NpcName} gets out of cage {cage.Stop} at its door");
            RefreshHud();
        }
    }

    // ---- the crew running to the train (Ruling 5) ---------------------------------------------------

    /// <summary>A crew member's booked seat in the train: the wagon and seat, the stop it was booked at, where to walk to, and since when.</summary>
    private sealed class Booking
    {
        public int Wagon, Seat, Stop;
        public Vector3 Spot;
        public double Since;
    }

    private readonly Dictionary<Npc, Booking> _bookings = new();
    private readonly List<Npc> _bookingKeys = new();
    private IReadOnlyList<(int Wagon, int Seat)> _seatOrder = Array.Empty<(int, int)>();
    /// <summary>The crew controller's own clock (server and solo): a booking's walk is timed by it.</summary>
    private double _boardClock;

    /// <summary>Crew walking to a booked seat in the train at <paramref name="stop"/> now.</summary>
    internal int OnTheWay(int stop)
    {
        var n = 0;
        foreach (var b in _bookings.Values) if (b.Stop == stop) n++;
        return n;
    }

    /// <summary>The booking of <paramref name="npc"/> (its wagon and seat), or null when it walks to none (test hook and the prints).</summary>
    internal (int Wagon, int Seat)? BookingOf(Npc npc) => _bookings.TryGetValue(npc, out var b) ? (b.Wagon, b.Seat) : null;

    /// <summary>The rail controller's <see cref="RailController.CrewBoardingHolds"/>: crew on the way at that stop, under <see cref="TrainRules.BoardMax"/> (Ruling 6).</summary>
    private bool HoldsDeparture(int stop) => _rails != null && IsInstanceValid(_rails) && TrainRules.HoldsDeparture(OnTheWay(stop), _rails.BoardingElapsed);

    /// <summary>
    /// Every tick, server and solo (Ruling 5): an open cage's crew who are up get out and follow; each booking is walked, sat in at
    /// <see cref="TrainRules.HopReach"/> (or at once at <see cref="TrainRules.BoardMax"/> within <see cref="TrainRules.BoardReach"/>), or
    /// ended (the train left, wrecked or halted, the walker went down or fell behind); and while the train is held at a stop short of the
    /// last, every freed crew member up and on foot within <see cref="TrainRules.BoardReach"/> books the first empty seat in
    /// <see cref="TrainRules.SeatOrder"/>. Grisel (an escort) is no crew member: she never takes a wagon seat.
    /// </summary>
    private void TickBoarding(float dt)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails)) return;
        _boardClock += dt;
        foreach (var cage in _cages) if (cage.Open && cage.Riders.Count > 0) LetOutOfCage(cage);
        var rails = _rails;
        var boardsHere = rails.Train.Count > 0 && TrainRules.BoardsHere(rails.State.Mode == CartMode.Held, rails.Halted, rails.StopIndex >= rails.StopS.Length - 1);
        if (_bookings.Count > 0)
        {
            _bookingKeys.Clear();
            _bookingKeys.AddRange(_bookings.Keys);
            foreach (var npc in _bookingKeys)
            {
                var b = _bookings[npc];
                if (!IsInstanceValid(npc) || npc.Dead) { _bookings.Remove(npc); continue; }
                if (npc.Seated) { EndBooking(npc, b, "seated elsewhere"); continue; }
                var wagon = rails.WagonAt(b.Wagon);
                var distance = Flat(npc.GlobalPosition, b.Spot);
                string? why = null;
                if (!TrainRules.KeepsBooking(boardsHere, !npc.Downed, distance))
                    why = !boardsHere ? rails.State.Mode switch { CartMode.Rolling => "the train left", CartMode.Wrecked => "the cart was wrecked", _ => rails.Halted ? "the train is halted on the line" : "the end of the line" }
                        : npc.Downed ? "down" : $"{distance:0.0} m off";
                else if (wagon is not { Coupled: true }) why = $"W{b.Wagon} is gone";
                else if (wagon.Seats.OccupantOf(b.Seat) is int holder && holder != npc.RiderId) why = "its seat was taken";
                if (why != null) { EndBooking(npc, b, why); continue; }
                if (distance <= TrainRules.HopReach)
                {
                    if (SitInWagon(npc, wagon!, b)) GD.Print($"[CrewRescue] {npc.NpcName} sits in W{b.Wagon} seat {b.Seat} after {_boardClock - b.Since:0.0} s");
                }
                else if (TrainRules.SeatsAtOnce(rails.BoardingElapsed, distance))
                {
                    if (SitInWagon(npc, wagon!, b)) GD.Print($"[CrewRescue] {npc.NpcName} sat in at once at {TrainRules.BoardMax:0} s ({distance:0.0} m)");
                }
                else if (npc.MoveTarget is not { } goal || goal == b.Spot) { npc.MoveTarget = b.Spot; npc.MoveOverride = true; } // a pocket's or a test's walk comes first, and gives it back
            }
        }
        if (!boardsHere) return;
        if (_seatOrder.Count != rails.Wagons.Count * TrainRules.CrewSeats.Length) _seatOrder = TrainRules.SeatOrder(rails.Wagons.Count);
        var order = _seatOrder;
        foreach (var npc in _crew)
        {
            if (!IsInstanceValid(npc) || npc.Dead || npc.Downed || npc.Seated || !npc.Freed || _bookings.ContainsKey(npc) || CageOf(npc) is { Open: false }) continue;
            if (FirstFreeSeat(order) is not { } seat || rails.WagonAt(seat.Wagon) is not { } wagon) return; // the train is full
            if (Flat(npc.GlobalPosition, wagon.GlobalPosition) > TrainRules.BoardReach + 1.5f * DungeonBuilder.CellSize) continue; // far off: its spot is a cell from the wagon at most
            var spot = rails.StandBesideLane(wagon.GlobalPosition);
            var distance = Flat(npc.GlobalPosition, spot);
            if (distance > TrainRules.BoardReach) continue;
            var b = new Booking { Wagon = seat.Wagon, Seat = seat.Seat, Stop = rails.StopIndex, Spot = spot, Since = _boardClock };
            _bookings[npc] = b;
            GD.Print($"[CrewRescue] {npc.NpcName} books W{b.Wagon} seat {b.Seat} at stop {b.Stop} ({distance:0.0} m)");
            if (distance <= TrainRules.HopReach) { if (SitInWagon(npc, wagon, b)) GD.Print($"[CrewRescue] {npc.NpcName} sits in W{b.Wagon} seat {b.Seat} after 0.0 s"); }
            else { npc.MoveTarget = spot; npc.MoveOverride = true; }
        }
    }

    /// <summary>Test hook: freed crew book seats only in this wagon while it is set (0: the train's first free seats).</summary>
    internal int DebugBoardWagon { get; set; }

    /// <summary>The first seat of <paramref name="order"/> in a coupled wagon that nobody sits in or has booked, or null.</summary>
    private (int Wagon, int Seat)? FirstFreeSeat(IReadOnlyList<(int Wagon, int Seat)> order)
    {
        foreach (var p in order)
            if ((DebugBoardWagon <= 0 || p.Wagon == DebugBoardWagon) && SeatFree(p.Wagon, p.Seat)) return p;
        return null;
    }

    /// <summary>Seat <paramref name="seat"/> of coupled wagon <paramref name="wagon"/> is empty and booked by nobody.</summary>
    private bool SeatFree(int wagon, int seat)
    {
        if (_rails?.WagonAt(wagon) is not { Coupled: true } w || w.Seats.OccupantOf(seat) != null) return false;
        foreach (var b in _bookings.Values) if (b.Wagon == wagon && b.Seat == seat) return false;
        return true;
    }

    /// <summary>The walker sits in its booked seat; the booking and its walk end. False when the seat could not be taken.</summary>
    private bool SitInWagon(Npc npc, TrainWagon wagon, Booking b)
    {
        _bookings.Remove(npc);
        StopWalking(npc, b);
        if (!wagon.Seats.Put(npc.RiderId, b.Seat)) { GD.Print($"[CrewRescue] {npc.NpcName}'s booking of W{b.Wagon} seat {b.Seat} ends (the seat was taken)"); return false; }
        npc.SitIn(wagon, b.Seat);
        RefreshHud();
        return true;
    }

    private void EndBooking(Npc npc, Booking b, string why)
    {
        _bookings.Remove(npc);
        StopWalking(npc, b);
        GD.Print($"[CrewRescue] {npc.NpcName}'s booking of W{b.Wagon} seat {b.Seat} ends ({why})");
    }

    /// <summary>The walk to a booked seat is over: the walker's goal, when it is still the seat's spot, is cleared, and it follows the party again.</summary>
    private static void StopWalking(Npc npc, Booking b)
    {
        if (!IsInstanceValid(npc) || npc.MoveTarget != b.Spot) return; // someone else's walk (a pocket, a test) is left alone
        npc.MoveTarget = null;
        npc.MoveOverride = false;
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Up to <paramref name="count"/> distinct cells round <paramref name="at"/> a body may be set down on (<see cref="CrevasseRules.SetDownOk"/>: never a drop), nearest first: no rail, nothing blocking, no siding.</summary>
    private List<GridPos> FreeCellsNear(GridPos at, int count)
    {
        var cells = new List<GridPos>();
        var sidings = _cages.Select(c => c.Cell).ToHashSet();
        for (var r = 1; r <= 3 && cells.Count < count; r++)
            for (var dy = -r; dy <= r && cells.Count < count; dy++)
                for (var dx = -r; dx <= r && cells.Count < count; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var c = new GridPos(at.X + dx, at.Y + dy);
                    if (!CrevasseRules.SetDownOk(Layout, c) || Layout.IsRail(c) || sidings.Contains(c) || _builder!.IsCellBlocked(c) || Layout.LevelOf(c) != Layout.LevelOf(at)) continue;
                    cells.Add(c);
                }
        if (cells.Count == 0) cells.Add(DungeonBuilder.WalkableNeighbor(Layout, at));
        return cells;
    }

    /// <summary>The cart left a stop: Grisel on the bench names the cage ahead. No carts left (OnFoot): the crew seated in the train get out and follow.</summary>
    private void OnCartEvent(CartEvent e)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails)) return;
        if (e.Kind == CartEventKind.OnFoot) { LetOutOfTrain(); return; } // no cart left: the train's crew get out and follow the lead
        if (e.Kind is CartEventKind.Boarding or CartEventKind.CageHeld) { WarnLocked(e.Stop); return; }
        if (e.Kind != CartEventKind.Departed) return;
        GriselNamesTheRide(e);
    }

    /// <summary>
    /// Every living crew member seated in a train wagon climbs out beside it and follows the party on foot (no carts left), each on a cell of
    /// its own (the final review's M3, as the old cage wagon's <c>LetOut</c> did): the nearest free cell round its wagon that no one let out
    /// before it took, or, with every one taken, the farthest found.
    /// </summary>
    private void LetOutOfTrain()
    {
        var taken = new HashSet<GridPos>();
        foreach (var npc in _crew)
        {
            if (!IsInstanceValid(npc) || npc.Dead || npc.Ride is not TrainWagon wagon) continue;
            var cells = FreeCellsNear(DungeonBuilder.WorldToCell(wagon.GlobalPosition), taken.Count + 1);
            var cell = cells.FirstOrDefault(c => !taken.Contains(c), cells[^1]);
            taken.Add(cell);
            npc.GetOut(_builder!.Surface(cell) + Vector3.Up * 0.1f);
            if (!npc.Escorting) npc.StartFollowing();
            if (DebugLetOut) GD.Print($"[CrewRescue] test: {npc.NpcName} let out of W{wagon.Number} at {cell}");
        }
        RefreshHud();
    }

    /// <summary>Test hook: <see cref="LetOutOfTrain"/> prints each set-down (set by <see cref="DebugLetOutOfTrain"/>).</summary>
    private bool DebugLetOut;

    /// <summary>
    /// Test hook (server and solo): the train's crew are let out as when no cart is left (<see cref="LetOutOfTrain"/>); returns each one's
    /// name, wagon and the cell it stands on.
    /// </summary>
    internal List<(string Name, int Wagon, GridPos Cell)> DebugLetOutOfTrain()
    {
        var aboard = _crew.Where(n => IsInstanceValid(n) && !n.Dead && n.Ride is TrainWagon).Select(n => (Npc: n, Wagon: ((TrainWagon)n.Ride!).Number)).ToList();
        DebugLetOut = true;
        LetOutOfTrain();
        DebugLetOut = false;
        return aboard.Select(a => (a.Npc.NpcName, a.Wagon, DungeonBuilder.WorldToCell(a.Npc.GlobalPosition))).ToList();
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
        if (!_cages.Any(c => c.Stop == stop && !c.Open) || !_warnedLocked.Add(stop)) return;
        EventBus.Instance.Log("The cage here is still locked.");
    }

    // ---- prisoners (IPrisonerHost) ----------------------------------------------------------------

    /// <summary>The crew are reached through their cage or their wagon; on foot only a downed one is (Revive, which the NPC offers itself).</summary>
    public string PromptFor(Npc npc) => "";

    public bool OnInteract(Npc npc, PartyController party, Adventurer actor) => false;

    // ---- a boarder throws a prisoner off (plan 14 Task 5, Rulings 16-17) ---------------------------------------------

    /// <summary>A thrown prisoner flies this long from over the wagon's rim to its set-down spot beside the enemy lane (fix round 1, I1 and M1).</summary>
    private const float ThrowFlight = 0.9f;
    /// <summary>Its set-down spot is at least this far, and at most this far, from where it leaves the wagon (flat metres).</summary>
    private const float ThrowMin = 1.5f, ThrowMax = 4.5f;

    /// <summary>Prisoners thrown off a wagon on this floor build (tests read it).</summary>
    internal int Thrown { get; private set; }

    /// <summary>Each throw's launch (tests read it), by crew id: where it left (over the rim), the set-down spot it was aimed at, its velocity, and the wagon's pose then.</summary>
    internal readonly Dictionary<string, (Vector3 Seat, Vector3 From, Vector3 To, Vector3 Velocity, Transform3D Wagon)> ThrowLaunches = new();

    /// <summary>
    /// A boarder alone with the prisoners throws <paramref name="npc"/> off <paramref name="wagon"/> (server and solo): unseated (its seat's book
    /// freed, the usual seat message) from over the rim above its seat, and thrown on an arc of <see cref="ThrowFlight"/> to a set-down spot on the
    /// enemy lane's side (<see cref="RailController.SetAsideBesideEnemyLane"/>, Ruling 16 as ruled in fix round 1, M1), dead (<see cref="Npc.Throw"/>:
    /// gone <see cref="BoarderRules.ThrownHide"/> later, hidden and fled, never a corpse). It is lost at once (<c>LostCrew</c> holds the dead), the log
    /// says "{name} is thrown from the wagon!", and clients hear <c>PropEvt wagon:{k} "thrown:{id}"</c>. No rng: the arc is the spot's.
    /// </summary>
    internal bool ThrowCrew(Npc npc, TrainWagon wagon)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails) || !IsInstanceValid(npc) || npc.Dead || !IsInstanceValid(wagon)) return false;
        // it leaves from over the rim above its seat (the tub is solid to it: layer 4), clear of the box
        var seatAt = wagon.SeatTransform(npc.Seat).Origin;
        var seat = wagon.GlobalTransform.AffineInverse() * seatAt;
        var from = wagon.GlobalTransform * new Vector3(seat.X, MineCart.TubBottom + MineCart.TubHeight + 0.05f, seat.Z);
        var to = _rails.SetAsideBesideEnemyLane(wagon.GlobalPosition) + Vector3.Up * 0.05f;
        var flat = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
        if (flat.Length() < 0.1f) flat = wagon.GlobalTransform.Basis.X; // straight over the side (never met: the spot is off the lanes)
        var d = Mathf.Clamp(flat.Length(), ThrowMin, ThrowMax);
        flat = flat.Normalized() * d;
        to = new Vector3(from.X + flat.X, to.Y, from.Z + flat.Z);
        var g = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f).AsDouble();
        var velocity = flat / ThrowFlight + Vector3.Up * ((to.Y - from.Y) / ThrowFlight + 0.5f * g * ThrowFlight); // lands on `to` after ThrowFlight
        ThrowLaunches[npc.Id] = (seatAt, from, to, velocity, wagon.GlobalTransform);
        npc.Throw(from, velocity);
        _home.Remove(npc.Id);
        Thrown++;
        EventBus.Instance.Log($"{npc.NpcName} is thrown from the wagon!");
        EventBus.Instance.RaisePropChanged(wagon.NetRef, $"thrown:{npc.Id}");
        Evaluate(); // lost now, not at the next tick
        RefreshHud();
        return true;
    }

    /// <summary>
    /// Client: the server threw crew member <paramref name="id"/> off a wagon (its <c>PropEvt</c>): this copy, dead by the snapshots, is hidden
    /// after <see cref="BoarderRules.ThrownHide"/> as the server's is. False when there is no such crew member here.
    /// </summary>
    public bool ShowThrown(string id)
    {
        var npc = _crew.FirstOrDefault(c => IsInstanceValid(c) && c.Id == id);
        if (npc == null) return false;
        npc.HideAfter(BoarderRules.ThrownHide);
        return true;
    }

    // ---- save -------------------------------------------------------------------------------------

    public override ObjectiveStateDto Capture()
    {
        var dto = base.Capture();
        dto.FreedNpcs = _crew.Where(c => StateOf(c) != CrewState.Caged).Select(c => c.Id).ToList();
        dto.OpenCages = _cages.Where(c => c.Open).Select(c => c.Stop).ToList();
        dto.HomeCrew = _crew.Where(c => _home.Contains(c.Id)).Select(c => c.Id).ToList();
        dto.LostCrew = _crew.Where(c => !IsInstanceValid(c) || c.Dead).Select(c => c.Id).ToList();
        dto.LostWagons = _rails != null && IsInstanceValid(_rails) ? TrainRules.Lost(_rails.Wagons.Select(w => w.Coupled).ToList()) : null; // a record: every load couples fresh
        dto.AboardCrew = AboardCrew();
        dto.OrcKeys = OrcKeys;
        dto.GatesUp = _rails != null && IsInstanceValid(_rails) ? _rails.GatesUpList.ToList() : null;
        dto.CrewDownedLeft = NetSession.IsServer ? DownedLeft() : null; // the broadcast's (a client counts its labels down); a solo save stays as it was
        return dto;
    }

    /// <summary>The crew seated in the coupled wagons of the train, by wagon then seat; null when nobody is.</summary>
    private List<CrewSeatDto>? AboardCrew()
    {
        if (_rails == null || !IsInstanceValid(_rails)) return null;
        List<CrewSeatDto>? aboard = null;
        foreach (var npc in _crew)
            if (IsInstanceValid(npc) && !npc.Dead && npc.Cart is TrainWagon { Coupled: true } wagon && _rails.WagonAt(wagon.Number) == wagon)
                (aboard ??= new()).Add(new CrewSeatDto { Id = npc.Id, Wagon = wagon.Number, Seat = npc.Seat });
        aboard?.Sort((a, b) => a.Wagon != b.Wagon ? a.Wagon.CompareTo(b.Wagon) : a.Seat.CompareTo(b.Seat));
        return aboard;
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
    /// Back from a save, after the floor's NPC states (alive, down, following, where they stood) and the cart (held at the stop it had
    /// reached, restored whole: its train coupled fresh, so <c>LostWagons</c> is only printed) are applied: the cages open as saved; the
    /// crew the save had seated in the train (<c>AboardCrew</c>) sit in their seats again (<see cref="TrainRules.Reseat"/>: the first empty
    /// seat when theirs is taken or gone), caged ones stand in their cages, a downed one left in an open cage stays in it, and the rest
    /// are on foot where the save had them. The lost lie dead on the floor, and those home stand in the exit room.
    /// </summary>
    public override void Restore(ObjectiveStateDto dto)
    {
        if (NetSession.IsClient) { ShowForward(dto); ApplyNet(dto); return; } // a join or revisit: a client only reads the state (the seats, the train and the doors are the server's)
        base.Restore(dto);
        OrcKeys = dto.OrcKeys ?? false;
        if (dto.GatesUp != null && _rails != null && IsInstanceValid(_rails)) _rails.RestoreGates(dto.GatesUp);
        _grisel = _root?.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Id == GriselId);
        var open = dto.OpenCages ?? new List<int>();
        foreach (var cage in _cages) cage.SetOpen(open.Contains(cage.Stop));
        _home.Clear();
        foreach (var id in dto.HomeCrew ?? new List<string>()) _home.Add(id);
        var lost = (dto.LostCrew ?? new List<string>()).ToHashSet();
        if (dto.LostWagons is { Count: > 0 } lostWagons && _rails != null && IsInstanceValid(_rails)) // Ruling 3: recorded, never honoured
            GD.Print($"[rails] train: the save had wagons [{string.Join(",", lostWagons)}] lost; the cart is restored whole, W1..W{_rails.Wagons.Count} couple fresh");
        _bookings.Clear();
        var aboard = new Dictionary<string, CrewSeatDto>();
        foreach (var seat in dto.AboardCrew ?? new List<CrewSeatDto>()) aboard.TryAdd(seat.Id, seat); // the same id twice: its first seat
        var exitCells = ExitCells();
        var nextExit = 0;
        foreach (var npc in _crew)
        {
            if (!IsInstanceValid(npc)) continue;
            var cage = CageOf(npc); // null: freed at the landing (no cage had room for them)
            npc.Freed = cage?.Open ?? true;
            if (lost.Contains(npc.Id) && !npc.Dead) npc.Restore(true, false, false, 0f, null);
            if (npc.Dead)
            {
                _home.Remove(npc.Id);
                if (npc.Seated) npc.GetOut(npc.InTheDark() ? npc.GlobalPosition : _builder!.Surface(DungeonBuilder.WorldToCell(npc.GlobalPosition))); // one a crevasse took stays below the lip
                continue;
            }
            if (cage is { Open: false }) { Seat(npc, cage); continue; }
            if (_home.Contains(npc.Id))
            {
                var cell = exitCells.Count > 0 ? exitCells[nextExit++ % exitCells.Count] : DungeonBuilder.WorldToCell(npc.GlobalPosition);
                npc.GetOut(_builder!.Surface(cell) + Vector3.Up * 0.1f);
                if (!npc.Escorting) npc.Restore(false, true, false, npc.Hp, null, npc.Downed);
                continue;
            }
            if (aboard.TryGetValue(npc.Id, out var saved) && ReseatInTrain(npc, saved)) continue;
            // on foot where the save had them (Build stood every caged crew member in its cage, and the NPC states only moved it): only one
            // that went down inside its open cage, never let out to follow, stays in it (up, it gets out by TickBoarding)
            if (npc.Seated && !(cage != null && npc.Cart == cage && CrewRules.StaysInOpenCage(npc.Downed, npc.Escorting))) npc.GetOut(npc.GlobalPosition);
        }
        _state.Clear();
        foreach (var npc in _crew) _state[npc] = npc.Dead ? CrewState.Lost : CageOf(npc) is { Open: false } ? CrewState.Caged : _home.Contains(npc.Id) ? CrewState.Home : CrewState.Freed;
        Evaluate();
        if (Completed && _rewardDoor is { IsOpen: false }) { _rewardDoor.Reveal(); _rewardDoor.Open(silent: true); }
        RefreshHud();
    }

    /// <summary>A load seats <paramref name="npc"/> in the train where the save had it (<see cref="TrainRules.Reseat"/>); false when the train has no empty seat.</summary>
    private bool ReseatInTrain(Npc npc, CrewSeatDto saved)
    {
        if (_rails == null || !IsInstanceValid(_rails)) return false;
        if (TrainRules.Reseat((saved.Wagon, saved.Seat), SeatFree, _rails.Wagons.Count) is not { } at || _rails.WagonAt(at.Wagon) is not { } wagon) return false;
        if (npc.Seated) npc.GetOut(npc.GlobalPosition);
        if (!wagon.Seats.Put(npc.RiderId, at.Seat)) return false;
        npc.SitIn(wagon, at.Seat);
        GD.Print(at == (saved.Wagon, saved.Seat) ? $"[CrewRescue] load: {npc.NpcName} back in W{at.Wagon} seat {at.Seat}"
            : $"[CrewRescue] load: {npc.NpcName} back in W{at.Wagon} seat {at.Seat} (seat {saved.Seat} of W{saved.Wagon} taken: first empty)");
        return true;
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
            foreach (var cage in _cages)
                if (IsInstanceValid(cage) && !cage.Open && open.Contains(cage.Stop)) cage.SetOpen(true);
        if (dto.OrcKeys == true && !OrcKeys) OrcKeys = true; // a client's setter takes the keyring off Grisel's belt and tells nothing
    }

    /// <summary>The exit room's free floor (no rail, nothing blocking), nearest the down stairs first.</summary>
    private List<GridPos> ExitCells()
    {
        if (_exitRoom == null || _builder == null) return new List<GridPos>();
        var stairs = Layout.DownStairs;
        return RoomCells.Free(Layout, _exitRoom).Where(c => !Layout.IsRail(c) && !_builder.IsCellBlocked(c)).OrderBy(c => Sq(c, stairs)).ThenBy(c => c.X).ThenBy(c => c.Y).ToList();
    }

    /// <summary>
    /// A client's copy for the HUD: home and lost crew from the server's state (the crew's seats come from the seat messages).
    /// The open cages and the orc's keys are rail props: they come only from the server's PropEvts and the prop check's rail
    /// part, never from here (one writer per fact: a stale objective state cannot undo a newer PropEvt).
    /// </summary>
    public override void ApplyNet(ObjectiveStateDto dto)
    {
        base.Restore(dto);
        var home = (dto.HomeCrew ?? new List<string>()).ToHashSet();
        var lost = (dto.LostCrew ?? new List<string>()).ToHashSet();
        foreach (var npc in _crew)
            _state[npc] = lost.Contains(npc.Id) ? CrewState.Lost
                : CageOf(npc) is { Open: false } ? CrewState.Caged
                : home.Contains(npc.Id) ? CrewState.Home
                : IsInstanceValid(npc) && npc.Downed ? CrewState.Downed : CrewState.Freed;
        // the bleed-out clocks: each downed crew member's label counts down here from the server's seconds left (refreshed each broadcast)
        NetDownedLeft = dto.CrewDownedLeft;
        if (dto.CrewDownedLeft is { } left)
            foreach (var npc in _crew)
                if (IsInstanceValid(npc) && left.TryGetValue(npc.Id, out var secondsLeft)) npc.ApplyBleedLeft(secondsLeft);
    }

    /// <summary>
    /// Test hook (server and solo): the stop's cage opened as a key would (its crew then board by <see cref="TickBoarding"/>). False when
    /// there is no such cage, it is open already, or this is a client.
    /// </summary>
    internal bool DebugOpen(int stop)
    {
        if (NetSession.IsClient || Main.Instance?.Party is not { } party) return false;
        if (_cages.FirstOrDefault(c => c.Stop == stop) is not { Open: false } cage) return false;
        GD.Print($"[CrewRescue] test: cage {stop} opened as a key would");
        OpenCage(cage, party);
        return true;
    }

    /// <summary>
    /// Test hook (server and solo): the stop's cage opened if it is locked (as a key would) and its living crew seated at once in the first
    /// empty seats of the train (<see cref="TrainRules.SeatOrder"/>; only wagon <paramref name="wagon"/>'s when it is given), with no walk (a flow
    /// that restores the cart away from the cage). Returns who sat where (empty when nobody could), or null when there is no such cage or this is a client.
    /// </summary>
    internal List<(Npc Npc, int Wagon, int Seat)>? DebugSeatCrew(int stop, int wagon = 0)
    {
        if (NetSession.IsClient || _rails == null || !IsInstanceValid(_rails) || Main.Instance?.Party is not { } party) return null;
        if (_cages.FirstOrDefault(c => c.Stop == stop) is not { } cage) return null;
        if (!cage.Open) OpenCage(cage, party);
        var seated = new List<(Npc Npc, int Wagon, int Seat)>();
        var order = TrainRules.SeatOrder(_rails.Wagons.Count);
        foreach (var npc in _crew)
        {
            if (CageOf(npc) != cage || !IsInstanceValid(npc) || npc.Dead) continue;
            if (_bookings.Remove(npc, out var booked)) StopWalking(npc, booked); // its own seat counts as free again
            foreach (var (k, seat) in order)
            {
                if (wagon > 0 && k != wagon || _rails.WagonAt(k) is not { } w || !SeatFree(k, seat)) continue;
                if (npc.Seated) npc.GetOut(npc.GlobalPosition);
                if (!w.Seats.Put(npc.RiderId, seat)) continue;
                npc.SitIn(w, seat);
                seated.Add((npc, k, seat));
                break;
            }
        }
        GD.Print($"[CrewRescue] test: stop {stop}'s crew seated in [{string.Join(", ", seated.Select(c => $"W{c.Wagon} seat {c.Seat}"))}]");
        RefreshHud();
        return seated;
    }

    /// <summary>Open every cage and bring every living crew member to the exit room (tests).</summary>
    public override void Hurry()
    {
        if (Main.Instance?.Party is not { } party) return;
        foreach (var cage in _cages.Where(c => !c.Open).ToList()) OpenCage(cage, party);
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
