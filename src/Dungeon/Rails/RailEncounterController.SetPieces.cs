using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 1's stand-still pieces and the extension points for the others. A "lone" cinder and a "thrower" are spawned when their
/// ride ARMS (the cart's departure), standing where the cart will come (<see cref="SpawnStander"/>), so they are already there
/// to be seen. Set pieces (<see cref="RailSetPiecePlan"/>, kinds "beam", ...) are built once when the floor starts
/// (<see cref="BuildSetPieces"/>) and ticked while their ride is armed (<see cref="TickSetPieces"/>). A later kind adds a case to
/// each of the three switches.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>A thrower's wind-up is at least this long: its hand glows the whole time (the Shoot state glows for its wind-up).</summary>
    public const float ThrowerWindup = 1.5f;

    private sealed class Piece
    {
        public RailSetPiecePlan Plan = null!;
        /// <summary>Its plan's index in <see cref="FloorContents.RailSetPieces"/>: its wire name's number, the same on every peer.</summary>
        public int Index;
        public Node3D Node = null!;
        public float S;
        public bool Fired;
        public TrackSwitch? Switch;
        public OreHeap? Heap;
        public OreBuckets? Buckets;
        /// <summary>Its warning has been called this arming (a live switch back's early call).</summary>
        public bool Barked;
        /// <summary>A live switch back's last call has been made this arming.</summary>
        public bool BarkedLast;
        /// <summary>A switch back with the broken bridge 12-40 m past it (<see cref="SwitchBackRules.LiveBack"/>): a player's deadly lever.</summary>
        public bool Live;
        /// <summary>A wreck or a load undid the ride: the heap or the barricade stands again at the next arming (a broken valve stays broken for the floor).</summary>
        public bool Rebuild;
        /// <summary>The barricade went down while its ride was armed (or was cut): it stays down until the ride is undone (Ruling 5).</summary>
        public bool DownArmed;
        public DropHammer[]? Hammers;
        public MoltenPour? Pour;
        public Trestle? Trestle;
        public RailGap? Gap;
        public SpurSwitch? Spur;
        /// <summary>Ride 4's split (plan 12): its spur and its deadly lever (also <see cref="Switch"/>).</summary>
        public SplitSwitch? Split;
        public BufferStop? Buffer;
        public Barricade? Barricade;
        /// <summary>Ride 4's collapsing bridge (plan 13), when built live.</summary>
        public CavernBridge? Bridge;
        /// <summary>The trestle's seconds left before it falls, or −1 before the cart's front is on it.</summary>
        public float Timer = -1f;
        /// <summary>The pour's phase last tick (a <see cref="PourPhase"/>).</summary>
        public int Phase;
        /// <summary>Pours begun this arming, and the one that last caught the cart.</summary>
        public int PourCount, PourHit;
        /// <summary>Seconds to the pour's next burn of a walker.</summary>
        public float BurnClock;
    }

    private readonly List<Piece> _pieces = new();
    /// <summary>Stand-ins of the armed ride that go with it if the cart stops short ("lone"); a thrower is not on it.</summary>
    private readonly List<Enemy> _lones = new();
    private readonly List<(string id, Enemy enemy)> _throwers = new();
    /// <summary>Thrower encounters that have stood once on this floor: the first is a full enemy (it stays after the cart passes, for loot and XP); one sent again after it died, a wreck or a load is a reinforcement (no loot farm). Saved (<see cref="Capture"/>).</summary>
    private readonly ThrowerMemory _throwersSent = new();
    private int _headsBarked;

    /// <summary>The set pieces built on this floor (tests and the smoke read it).</summary>
    public int SetPieceCount => _pieces.Count;
    /// <summary>Every built set piece in index order: its id, ride, kind and party S (--track-depth reads it).</summary>
    internal IEnumerable<(string Id, int Ride, string Kind, float S)> DebugPieces()
        => _pieces.OrderBy(p => p.Index).Select(p => (p.Plan.Id, p.Plan.Ride, p.Plan.Kind, p.S));
    /// <summary>Whether <paramref name="enemy"/> is one of the floor's trackside throwers (tests read it).</summary>
    internal bool IsThrower(Enemy enemy)
    {
        foreach (var (_, e) in _throwers) if (e == enemy) return true;
        return false;
    }

    /// <summary>Times the beam has ducked the riders (tests read it).</summary>
    public int BeamPasses { get; private set; }

    // ---- extension point 1: arming (spawn what stands on the line) ------------------------------

    private void ArmStanders(int ride)
    {
        for (var i = 0; i < _encounters.Count; i++)
        {
            var enc = _encounters[i];
            if (enc.Ride != ride || !SetPieceRules.StandsFromDeparture(enc.Kind)) continue;
            _started[i] = true; // StartReached leaves it be
            SpawnStander(i, enc);
        }
        for (var i = 0; i < _pieces.Count; i++) if (_pieces[i].Plan.Ride == ride) _pieces[i].Fired = false;
        ArmSwitchyard(ride);
        ArmSplit(ride);
        ArmKiln(ride);
        ArmBarricades(ride);
        ArmBridge(ride);
    }

    private void SpawnStander(int index, RailEncounterPlan enc)
    {
        if (enc.Riders.Count == 0 || !IsInstanceValid(_rails.Party.Track.Track)) return;
        var track = _rails.Party.Track;
        var s = RideS(enc.Ride, enc.At);
        var id = enc.Riders[0].EnemyId;
        var frame = track.Frame(s);
        // the party-lane cell nearest S: the lane's own point (its cell is checked by the smoke), standing on the rails
        var at = track.Point(s) + Vector3.Up * 0.05f;
        switch (enc.Kind)
        {
            case "lone":
            {
                if (_builder.SpawnEnemy(id, at, _builder.CurrentFloor) is not { } lone) return;
                lone.IsReinforcement = true;
                lone.HoldAt(at);
                FaceCart(lone, frame);
                _lones.Add(lone);
                GD.Print($"[rails] lone {enc.Id} ({lone.Profile.Name}) stands on cell {DungeonBuilder.WorldToCell(at)} at party S={s:0.0} (ride {enc.Ride})");
                ShowLine(index, enc);
                break;
            }
            case "thrower":
            {
                foreach (var (tid, old) in _throwers) if (tid == enc.Id && IsInstanceValid(old) && old.IsAlive) return; // still standing from an earlier try
                EnsurePockets();
                var pocket = NearestPocket(_partyPockets!, track.Point(s));
                if (pocket is not { } spot) return;
                var baseProfile = _builder.EnemyProfile(id);
                var profile = baseProfile == null ? null : baseProfile with { WindupSeconds = Mathf.Max(ThrowerWindup, baseProfile.WindupSeconds) };
                if (_builder.SpawnEnemy(id, spot, _builder.CurrentFloor, profile: profile) is not { } thrower) return;
                thrower.HoldAt(spot); // (it marks a holder a reinforcement: the flag is set after)
                thrower.IsReinforcement = _throwersSent.Send(enc.Id); // the first pays like any enemy; a re-armed ride's next one (a load's too) only gives XP
                var toward = track.Point(s) - spot;
                toward.Y = 0f;
                if (toward.LengthSquared() > 0.01f) thrower.LookAt(thrower.GlobalPosition + toward, Vector3.Up);
                _throwers.Add((enc.Id, thrower));
                GD.Print($"[rails] thrower {enc.Id} ({thrower.Profile.Name}, wind-up {thrower.Profile.WindupSeconds:0.0} s, reinforcement={thrower.IsReinforcement}) stands in the pocket at {spot} for party S={s:0.0} (ride {enc.Ride})");
                break;
            }
        }
    }

    private static Vector3? NearestPocket(Vector3[] pockets, Vector3 to)
    {
        Vector3? best = null;
        var bestD = float.MaxValue;
        foreach (var p in pockets)
        {
            float dx = p.X - to.X, dz = p.Z - to.Z, d = dx * dx + dz * dz;
            if (d >= bestD || Mathf.Abs(p.Y - to.Y) > 3f) continue;
            bestD = d;
            best = p;
        }
        return best;
    }

    /// <summary>Face back along the line, toward the cart that comes from lower S: the frame's +Z.</summary>
    private static void FaceCart(Node3D body, Transform3D frame)
    {
        var back = frame.Basis.Z;
        back.Y = 0f;
        if (back.LengthSquared() > 0.01f) body.LookAt(body.GlobalPosition + back, Vector3.Up);
    }

    // ---- extension point 2: clearing -------------------------------------------------------------

    /// <summary>The ride is over or undone: a lone cinder the cart never reached goes silently.</summary>
    private void ClearStanders()
    {
        foreach (var e in _lones)
            if (IsInstanceValid(e) && !e.IsQueuedForDeletion()) e.QueueFree();
        _lones.Clear();
        _throwers.RemoveAll(t => !IsInstanceValid(t.enemy) || !t.enemy.IsAlive);
    }

    /// <summary>The cart is restored (a wreck undone): the throwers go too; until then they stay as any enemy would.</summary>
    private void ClearThrowers()
    {
        foreach (var (_, e) in _throwers)
            if (IsInstanceValid(e) && !e.IsQueuedForDeletion()) e.QueueFree();
        _throwers.Clear();
    }

    // ---- extension point 3: set pieces -----------------------------------------------------------

    /// <summary>Build every set piece of the floor (they are dressing until their ride is armed), on every role. Later kinds add a case.</summary>
    private void BuildSetPieces()
    {
        if (GetParent() is not Node3D root || _rails.StopS.Length < 2) return;
        BuildBreak(root); // first: a later piece (a live switch back) reads the gap
        var gaps = new List<(float from, float to)>();
        for (var index = 0; index < _setPieces.Count; index++)
        {
            var plan = _setPieces[index];
            if (plan.Ride < 1 || plan.Ride >= _rails.StopS.Length) continue;
            var s = RideS(plan.Ride, plan.At);
            var piece = new Piece { Plan = plan, Index = index, S = s };
            Node3D? node = null;
            switch (plan.Kind)
            {
                case "beam":
                    var beam = new LowBeam { Name = $"Beam_{plan.Id}", S = s };
                    root.AddChild(beam);
                    beam.Place(_rails.Party.Track.Frame(s));
                    node = beam;
                    break;
                case "switch_out":
                case "switch_back":
                    node = BuildSwitch(root, piece);
                    break;
                case "heap":
                    node = BuildHeap(root, piece);
                    break;
                case "buckets":
                    node = BuildBuckets(root, piece);
                    break;
                case "hammers":
                    node = BuildHammers(root, piece);
                    break;
                case "pour":
                    node = BuildPour(root, piece);
                    break;
                case "trestle":
                    node = BuildTrestle(root, piece);
                    break;
                case "jump":
                    node = BuildGap(root, piece, gaps);
                    break;
                case "spur_switch":
                    node = BuildSpur(root, piece);
                    break;
                case "buffer":
                    node = BuildBuffer(root, piece);
                    break;
                case "barricade":
                case "slag_barricade":
                    node = BuildBarricade(root, piece, slagLine: plan.Kind == "slag_barricade");
                    break;
                case "dark":
                    node = BuildDark(root, piece);
                    break;
                case "runaway":
                    node = BuildRunawayMarker(root, piece); // the wagon breaks loose as the front reaches it (ArmRunaway)
                    break;
                case "split":
                    node = BuildSplit(root, piece); // the layout's points: the spur and its lever, or a dead marker
                    break;
                case "bridge":
                    node = BuildBridge(root, piece); // the cavern's party deck: its span cut bare and laid cell by cell, or a dead marker
                    break;
            }
            if (node == null) continue;
            piece.Node = node;
            _pieces.Add(piece);
        }
        if (DebugFixedBackFor(root) is { } extra) _pieces.Add(extra);
        CutGaps(gaps);
        GuardBreak(); // last: it reads the ride's switches
    }

    /// <summary>
    /// Test hook (solo flows only, Ruling 20): metres before the broken bridge's gap at which <see cref="BuildSetPieces"/> adds a second, fixed
    /// switch back on the break's ride (<c>r3_switch_back2</c>, plan index <c>RailSetPieces.Count</c>), past the live one; NaN (the default) for none.
    /// </summary>
    internal static float DebugFixedBackBeforeGap = float.NaN;

    /// <summary>The <see cref="DebugFixedBackBeforeGap"/> switch back, built after the break (it reads the gap); null without the hook or a break.</summary>
    private Piece? DebugFixedBackFor(Node3D root)
    {
        if (float.IsNaN(DebugFixedBackBeforeGap) || float.IsNaN(BreakGapS) || BreakRide < 1 || BreakRide >= _rails.StopS.Length) return null;
        var s = BreakGapS - DebugFixedBackBeforeGap;
        float from = _rails.StopS[BreakRide - 1], to = _rails.StopS[BreakRide];
        var plan = new RailSetPiecePlan { Id = $"r{BreakRide}_switch_back2", Ride = BreakRide, Kind = "switch_back", At = (s - from) / (to - from) };
        var piece = new Piece { Plan = plan, Index = _setPieces.Count, S = s };
        if (BuildSwitch(root, piece) is not { } node) return null;
        piece.Node = node;
        return piece;
    }

    /// <summary>
    /// The armed ride's pieces: the beam and the points act as the rolling cart's front reaches them; the heap and the buckets tick
    /// while the ride is armed (a cart halted at the heap too). The hammers and the pour fall on their clocks whether or not their
    /// ride is armed: a body under them (a walker in the hall, an enemy) is hurt any time; the cart is judged only on its ride.
    /// </summary>
    private void TickSetPieces(PartyController party, float dt)
    {
        if (_pieces.Count == 0) return;
        var rolling = _rails.State.Mode == CartMode.Rolling;
        foreach (var piece in _pieces)
        {
            if (ArmedRide < 1 || piece.Plan.Ride != ArmedRide)
            {
                if (piece.Plan.Kind == "hammers") TickHammers(piece, judgeCart: false);
                else if (piece.Plan.Kind == "pour") TickPour(piece, dt, judgeCart: false);
                else if (piece.Plan.Kind == "bridge") TickBridge(piece, party, dt); // a collapse under way runs on to the bridge's end after the arrival (Ruling 5)
                continue;
            }
            switch (piece.Plan.Kind)
            {
                case "beam":
                    if (SetPieceRules.BeamPasses(rolling, piece.Fired, _rails.Party.FrontS, piece.S)) { piece.Fired = true; DuckUnder(party, piece); }
                    break;
                case "switch_out":
                    if (rolling && !piece.Fired && _rails.Party.FrontS >= piece.S) TakeSwitch(piece);
                    break;
                case "switch_back":
                    TickBackCalls(piece, party);
                    if (rolling && !piece.Fired && _rails.Party.FrontS >= piece.S) TakeBack(piece);
                    break;
                case "heap":
                    TickHeap(piece, party, rolling);
                    break;
                case "buckets":
                    TickBuckets(piece, dt);
                    break;
                case "hammers":
                    TickHammers(piece, judgeCart: true);
                    break;
                case "pour":
                    TickPour(piece, dt, judgeCart: true);
                    break;
                case "trestle":
                    TickTrestle(piece, party, rolling, dt);
                    break;
                case "jump":
                    TickJump(piece, party, rolling);
                    break;
                case "buffer":
                    TickBuffer(piece, rolling);
                    break;
                case "barricade":
                case "slag_barricade":
                    TickBarricade(piece, party, rolling);
                    break;
                case "runaway":
                    if (rolling && !piece.Fired && _rails.Party.FrontS >= piece.S) { piece.Fired = true; ArmRunaway(piece.Plan.Ride); }
                    break;
                case "split":
                    TickSplit(piece, party, rolling);
                    break;
                case "bridge":
                    TickBridge(piece, party, dt);
                    break;
            }
        }
    }

    /// <summary>The duck under the beam: a short dip of the seated desktop camera.</summary>
    private const float DuckAmplitude = 0.05f, DuckSeconds = 0.15f;

    /// <summary>
    /// The cart's front is at the beam: seated desktop heroes duck (no shake in VR), a seated hero calls it once (<see cref="RailCalls"/>). A remote player's
    /// screen ducks itself as its own cart's front passes (<see cref="ClientPresent"/>), so it is not shaken from here.
    /// </summary>
    private void DuckUnder(PartyController party, Piece piece)
    {
        BeamPasses++;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || !hero.Seated || hero.Cart != _rails.Cart) continue;
            if (!Vr.Active && hero.Control != ControlKind.Remote) hero.Shake(DuckAmplitude, DuckSeconds);
        }
        var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.Heads, "Heads!");
        if (by != null) _headsBarked++;
        GD.Print($"[rails] beam {piece.Plan.Id} passed at S={piece.S:0.0} (cart front {_rails.Party.FrontS:0.0}); bark={by ?? "none"}");
    }

    // ---- the wire: the set pieces' names and the prop check's rail part --------------------------

    /// <summary>The metronome index of the first drop hammer of ride <paramref name="ride"/>'s hammers, or −1 (the RIDE pass).</summary>
    internal int HammerIndexOf(int ride)
    {
        foreach (var p in _pieces)
            if (p.Plan.Kind == "hammers" && p.Plan.Ride == ride && p.Hammers is { Length: > 0 } hammers) return hammers[0].Index;
        return -1;
    }

    /// <summary>The index (its wire name's number) of the first set piece of plan kind <paramref name="kind"/> on ride <paramref name="ride"/>, or −1 (tests).</summary>
    internal int PieceIndexOf(string kind, int ride)
    {
        foreach (var p in _pieces)
            if (p.Plan.Kind == kind && p.Plan.Ride == ride) return p.Index;
        return -1;
    }

    /// <summary>The node a set piece's wire name names (<see cref="Net.NetRefs"/> kinds switch, spur, heap, valve, trestle, buffer, barricade, bridge), or null.</summary>
    internal Node3D? NetNode(string kind, int index)
    {
        foreach (var p in _pieces)
        {
            if (p.Index != index) continue;
            return kind switch
            {
                Net.NetRefs.SwitchKind => p.Spur == null ? p.Switch : null,
                Net.NetRefs.SpurKind => p.Spur?.Lever,
                Net.NetRefs.HeapKind => p.Heap,
                Net.NetRefs.ValveKind => p.Pour?.Valve,
                Net.NetRefs.TrestleKind => p.Trestle,
                Net.NetRefs.BufferKind => p.Buffer,
                Net.NetRefs.BarricadeKind => p.Barricade,
                Net.NetRefs.BridgeKind => p.Bridge,
                _ => null,
            };
        }
        return null;
    }

    /// <summary>The set pieces' lasting state into the prop check's rail part (server and client read alike).</summary>
    internal void CaptureProps(Net.RailPropsDto r)
    {
        foreach (var p in _pieces)
        {
            if (p.Switch is { } sw)
            {
                if (sw.Thrown) (p.Spur != null ? r.ThrownSpurs : r.ThrownSwitches).Add(p.Index);
                if (sw.Taken) r.TakenSwitches.Add(p.Index);
            }
            if (p.Heap is { IsAlive: false }) r.ClearedHeaps.Add(p.Index);
            if (p.Pour is { Quenched: true }) r.BrokenValves.Add(p.Index);
            if (p.Trestle is { Collapsed: true }) r.CollapsedTrestles.Add(p.Index);
            if (p.Buffer is { Smashed: true }) r.SmashedBuffers.Add(p.Index);
            if (p.Barricade is { IsStanding: false }) r.DownBarricades.Add(p.Index);
            if (p.Bridge is { } bridge && IsInstanceValid(bridge)) r.BridgeGone = Math.Max(r.BridgeGone, bridge.Gone);
        }
    }

    /// <summary>Client: the set pieces moved to the server's rail part, silently and both ways (no debris, no steam). Adds what changed to <paramref name="changed"/>.</summary>
    internal void ApplyProps(Net.RailPropsDto r, List<string> changed)
    {
        foreach (var p in _pieces)
        {
            var i = p.Index;
            if (p.Switch is { } sw)
            {
                var thrown = (p.Spur != null ? r.ThrownSpurs : r.ThrownSwitches).Contains(i);
                if (thrown != sw.Thrown && !sw.Fixed) { sw.ShowThrown(thrown); changed.Add($"{(p.Spur != null ? "spur" : "switch")} {i} {(thrown ? "thrown" : "straight")}"); }
                var taken = r.TakenSwitches.Contains(i);
                if (taken != sw.Taken) { sw.ShowTaken(taken); changed.Add($"lever {i} {(taken ? "taken" : "free")}"); }
            }
            if (p.Heap is { } heap && r.ClearedHeaps.Contains(i) == heap.IsAlive)
            {
                if (heap.IsAlive) heap.ShowCleared(); else heap.ShowRebuilt();
                changed.Add($"heap {i} {(heap.IsAlive ? "rebuilt" : "cleared")}");
            }
            if (p.Pour is { } pour && r.BrokenValves.Contains(i) != pour.Quenched)
            {
                if (pour.Quenched) pour.Valve.ShowRebuilt(); else pour.Valve.ShowBroken(steam: false);
                changed.Add($"valve {i} {(pour.Quenched ? "broken" : "rebuilt")}");
            }
            if (p.Trestle is { } trestle && r.CollapsedTrestles.Contains(i) != trestle.Collapsed)
            {
                if (trestle.Collapsed) trestle.ShowRebuilt(); else trestle.ShowCollapsed(null, debris: false);
                changed.Add($"trestle {i} {(trestle.Collapsed ? "collapsed" : "rebuilt")}");
            }
            if (p.Buffer is { } buffer && r.SmashedBuffers.Contains(i) != buffer.Smashed && GetParent() is Node3D root)
            {
                if (buffer.Smashed) buffer.ShowRestored(); else buffer.ShowSmashed(root, Vector3.Zero, debris: false);
                changed.Add($"buffer {i} {(buffer.Smashed ? "smashed" : "restored")}");
            }
            if (p.Barricade is { } barricade && r.DownBarricades.Contains(i) == barricade.IsStanding && GetParent() is Node3D at)
            {
                if (barricade.IsStanding) barricade.ShowSmashed(at, debris: false); else barricade.ShowStanding();
                changed.Add($"barricade {i} {(barricade.IsStanding ? "standing" : "down")}");
            }
            if (p.Bridge is { } bridge && IsInstanceValid(bridge) && r.BridgeGone != bridge.Gone)
            {
                if (r.BridgeGone < bridge.Gone) bridge.Stand();
                bridge.GiveWay(r.BridgeGone, null); // quietly: no timbers, no cue
                changed.Add($"bridge {i} {(bridge.Gone == 0 ? "standing" : $"gone {bridge.Gone}")}");
            }
        }
    }
}
