using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 3's pieces. A "switch_out" lever stands between the lanes <see cref="LeverLead"/> metres short of its points: thrown
/// when the cart's front reaches the points, the cart crosses onto the enemy lane (<see cref="RailController.Cross"/>) and a
/// "switch_back" (always thrown, untouchable) brings it home. A switch back with the broken bridge 12-40 m past it is live
/// (<see cref="SwitchBackRules.LiveBack"/>, plan 11): a player's lever (<see cref="TrackSwitch.Deadly"/>), offered from the seat only
/// while the cart is across with no return set, called twice by a companion, and taken at its points as it stands (<see cref="TakeBack"/>);
/// left straight, the train runs on to the gap. On the enemy lane a slag cart ahead cannot outrun the party's
/// (<see cref="RamCloseIn"/>) and is rammed when the front reaches its back end: it derails, the plating takes
/// <see cref="RamDent"/>. A "heap" blocks the party lane (<see cref="RailController.BlockLine"/>) until it is cleared; its
/// lantern and a companion's call warn first. "buckets" tip cinders onto the line: a cart through a live patch loses
/// <see cref="PatchDent"/> plating and each seated hero takes one lick of <see cref="TrapRules.FloorFire"/>. Judged on the server.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The lever stands this far short of its points, so a rider can pull it in passing before the cart takes them.</summary>
    public const float LeverLead = 5f;
    /// <summary>The lever stands this far from the party lane's centre, toward the enemy lane (the lanes are one cell apart).</summary>
    public const float LeverOut = 2f;
    public const float HeapDent = 40f;
    public const float RamDent = 10f;
    public const float PatchDent = 5f;
    /// <summary>A slag cart ahead on the lane the party's cart took runs at most this much slower than it: it is caught.</summary>
    public const float RamCloseIn = 2f;
    /// <summary>The party cart's front within this of a slag cart's back end (on the enemy lane) rams it.</summary>
    const float RamReach = 0.25f;
    /// <summary>A slag cart started while the party's cart is on its lane starts at least this far ahead of the front.</summary>
    const float SpawnClear = 3f;
    /// <summary>Timber posts this close to a crossover's path (or a lever) are taken down: the cart would pass through them.</summary>
    const float TimberClear = 1.1f;

    /// <summary>Slag carts rammed off the line (tests and the smoke read it).</summary>
    public int Rams { get; private set; }
    private readonly List<ICartRider> _burnt = new();

    /// <summary>A set piece's node by its plan id (the smoke and tests read it), or null.</summary>
    internal Node3D? SetPieceNode(string id)
    {
        foreach (var p in _pieces) if (p.Plan.Id == id) return p.Node;
        return null;
    }

    // ---- building --------------------------------------------------------------------------------

    private Node3D? BuildSwitch(Node3D root, Piece piece)
    {
        var party = _rails.Party.Track;
        var back = piece.Plan.Kind == "switch_back";
        var leverS = Mathf.Max(0f, piece.S - LeverLead);
        var frame = party.Frame(leverS);
        var x = frame.Basis.X.Normalized();
        var side = (_enemy.Point(Lanes.EnemyS(leverS)) - frame.Origin).Dot(x) >= 0f ? 1f : -1f; // between the lanes
        piece.Live = back && piece.Plan.Ride == BreakRide && SwitchBackRules.LiveBack(piece.S, BreakGapS); // every role alike, from the layout
        var sw = new TrackSwitch { Name = $"Switch_{piece.Plan.Id}", PointsS = piece.S, LeverS = leverS, Fixed = back && !piece.Live, Deadly = piece.Live, NetRef = Net.NetRefs.Switch(piece.Index), OfferedNow = () => BackOffered(piece) };
        root.AddChild(sw);
        sw.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + x * (side * LeverOut));
        piece.Switch = sw;

        // the crossover rails: dressing along the path the cart takes when it is set
        var points = new List<Vector3>();
        for (var u = -1f; u <= RailController.SwitchStretch + 1f; u += 0.5f)
        {
            var s = piece.S + u;
            var t = Mathf.Clamp(u / RailController.SwitchStretch, 0f, 1f);
            var blend = t * t * (3f - 2f * t);
            if (back) blend = 1f - blend;
            points.Add(party.Point(s).Lerp(_enemy.Point(Lanes.EnemyS(s)), blend));
        }
        var rails = root.GetNodeOrNull<Node3D>("Rails") ?? root;
        var crossover = new RailTrack { Name = $"Crossover_{piece.Plan.Id}" };
        rails.AddChild(crossover);
        crossover.Build(points, _steel ??= DungeonBuilder.RailSteel(),
            _sleeper ??= DungeonBuilder.RailSleeper());
        points.Add(sw.GlobalPosition); // and nothing stands in the lever's way
        if (piece.Live) points.Add(BuildSignal(root, piece));
        ClearTimber(root, points);
        var how = !back ? "straight" : piece.Live ? $"live: the broken bridge {BreakGapS - piece.S:0.0} m past" : "fixed, thrown";
        GD.Print($"[rails] switch {piece.Plan.Id} points at party S={piece.S:0.0}, lever at S={leverS:0.0} side {side:+0;-0} ({how})");
        return sw;
    }

    /// <summary>A live back's signal lamp stands this far from the slag line's middle, on its outer side (clear of a cart's 0.75 m half width).</summary>
    private const float SignalOut = 1.5f;
    private const float SignalHeight = 1.6f;
    public const float SignalRange = 5f, SignalEnergy = 1.6f;

    /// <summary>
    /// A live switch back's warning by sight (Ruling 18): a steady red signal lamp on its own post <see cref="SwitchBackRules.SignalPast"/> past
    /// its points, on the slag line's outer side (an <see cref="OmniLight3D"/>, <see cref="SignalRange"/> m, energy <see cref="SignalEnergy"/>,
    /// no shadow). Dressing only, every role alike. Returns where it stands (timber there is taken down).
    /// </summary>
    private Vector3 BuildSignal(Node3D root, Piece piece)
    {
        var enemyS = Lanes.EnemyS(piece.S + SwitchBackRules.SignalPast);
        var frame = _enemy.Frame(enemyS).Orthonormalized();
        var outer = -InnerSideAt(enemyS);
        var post = new Node3D { Name = $"Signal_{piece.Plan.Id}" };
        root.AddChild(post);
        post.GlobalTransform = new Transform3D(frame.Basis, frame.Origin + frame.Basis.X * (outer * SignalOut));
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Metallic = 0.7f, Roughness = 0.5f };
        var red = new Color(1f, 0.08f, 0.04f);
        post.AddChild(new MeshInstance3D { Name = "Post", Mesh = new BoxMesh { Size = new Vector3(0.12f, SignalHeight, 0.12f) }, MaterialOverride = iron, Position = Vector3.Up * (SignalHeight / 2f) });
        post.AddChild(new MeshInstance3D
        {
            Name = "Lamp", Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 12, Rings = 6 }, Position = Vector3.Up * (SignalHeight + 0.1f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = red, EmissionEnabled = true, Emission = red, EmissionEnergyMultiplier = 3f, Roughness = 0.4f },
        });
        post.AddChild(new OmniLight3D { Name = "SignalLight", LightColor = red, LightEnergy = SignalEnergy, OmniRange = SignalRange, ShadowEnabled = false, Position = Vector3.Up * (SignalHeight + 0.2f) });
        return post.GlobalPosition;
    }

    private StandardMaterial3D? _steel, _sleeper;

    /// <summary>Take down the timber frames whose posts stand within <see cref="TimberClear"/> of <paramref name="path"/> (posts and cap together).</summary>
    private static void ClearTimber(Node3D root, List<Vector3> path)
    {
        if (root.GetNodeOrNull<Node3D>("Rails/Timbering") is not { } frames) return;
        bool Near(Vector3 p)
        {
            foreach (var q in path)
            {
                float dx = p.X - q.X, dz = p.Z - q.Z;
                if (dx * dx + dz * dz < TimberClear * TimberClear) return true;
            }
            return false;
        }
        var ends = new List<Vector3>();
        foreach (var child in frames.GetChildren())
        {
            if (child is not MeshInstance3D { Mesh: BoxMesh box } cap || box.Size.X < 1f) continue; // a cap spans the lane cell
            var along = cap.GlobalBasis.X.Normalized() * (box.Size.X / 2f - 0.15f);
            Vector3 a = cap.GlobalPosition + along, b = cap.GlobalPosition - along;
            if (!Near(a) && !Near(b)) continue;
            cap.Visible = false;
            ends.Add(a);
            ends.Add(b);
        }
        foreach (var child in frames.GetChildren())
        {
            if (child is not MeshInstance3D { Mesh: BoxMesh box } post || box.Size.X >= 1f) continue;
            var p = post.GlobalPosition;
            var hide = Near(p);
            foreach (var e in ends) if (!hide && (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z) < 0.5f * 0.5f) hide = true;
            if (hide) post.Visible = false;
        }
    }

    private Node3D? BuildHeap(Node3D root, Piece piece)
    {
        var heap = new OreHeap { Name = $"Heap_{piece.Plan.Id}", S = piece.S, NetRef = Net.NetRefs.Heap(piece.Index) };
        root.AddChild(heap);
        heap.GlobalTransform = _rails.Party.Track.Frame(piece.S).Orthonormalized();
        heap.Cleared += h => OnHeapCleared(piece, h);
        piece.Heap = heap;
        return heap;
    }

    private Node3D? BuildBuckets(Node3D root, Piece piece)
    {
        var buckets = new OreBuckets { Name = $"Buckets_{piece.Plan.Id}", S = piece.S, Floor = _builder.CurrentFloor };
        root.AddChild(buckets);
        buckets.Place(_rails.Party.Track);
        piece.Buckets = buckets;
        return buckets;
    }

    // ---- arming and clearing ---------------------------------------------------------------------

    /// <summary>Ride <paramref name="ride"/> arms: a heap undone by a wreck or a load stands again and blocks the line, the buckets start.</summary>
    private void ArmSwitchyard(int ride)
    {
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != ride) continue;
            p.Barked = p.BarkedLast = false;
            if (p.Switch != null) p.Switch.Taken = false; // told ("free") when it was still taken, so a client's lever offers itself again
            if (p.Heap is { } heap)
            {
                if (p.Rebuild && !heap.IsAlive) heap.Rebuild();
                p.Rebuild = false;
                if (heap.IsAlive) _rails.BlockLine(heap.FaceS, HeapDent);
            }
            p.Buckets?.Arm();
        }
    }

    /// <summary>The ride's live pieces stop (any clear, the departure's too): the cinders go, the heap's lantern stops blinking.</summary>
    private void ClearSwitchyard()
    {
        foreach (var p in _pieces)
        {
            p.Buckets?.Clear();
            if (p.Heap is { } heap) heap.Alarm = false;
        }
    }

    /// <summary>
    /// The ride is over or undone (arrived, wrecked, restored, on foot): the levers fall back to straight, only those of rides up to
    /// <paramref name="upToRide"/> (an arrival at stop k: rides 1..k are behind the cart, and a lever a walker threw ahead for a later
    /// ride stays thrown); a wreck or a load stacks the heap again, and stands the barricades again, at the next arming.
    /// </summary>
    private void ResetSwitchyard(bool undone, int upToRide = int.MaxValue)
    {
        if (undone)
        {
            _partyOnSpur = false; // a wreck (the fall too) or a load: the split's spur run is over (review M5)
            _splitResetWaits = false;
        }
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride <= upToRide && (undone || !SplitResetWaits(p))) p.Switch?.Reset(); // an arrival's split waits for the runaway behind (review M2)
            if (undone && (p.Heap != null || p.Barricade != null)) p.Rebuild = true; // the heap and the barricades stand again at the ride's next arming
        }
    }

    private void OnHeapCleared(Piece piece, OreHeap heap)
    {
        GD.Print($"[rails] heap {piece.Plan.Id} cleared (ride {piece.Plan.Ride}, armed {ArmedRide}); cart {_rails.State.Mode} halted={_rails.Halted} S={_rails.State.S:0.0}");
        if (ArmedRide == piece.Plan.Ride && Mathf.IsEqualApprox(_rails.BlockedAt, heap.FaceS)) _rails.ClearBlock();
    }

    // ---- ticking ---------------------------------------------------------------------------------

    /// <summary>
    /// The front reached the points: a thrown lever sends the cart across, and the next switch_back of the ride brings it home: a fixed one from
    /// its points, set now; a live one as its lever stands when the front reaches it (<see cref="TakeBack"/>), so the return is left open.
    /// </summary>
    private void TakeSwitch(Piece piece)
    {
        piece.Fired = true;
        if (piece.Switch != null) piece.Switch.Taken = true; // its points are behind the cart: nothing left to pull this ride
        if (piece.Switch is not { Thrown: true }) { GD.Print($"[rails] switch {piece.Plan.Id} passed straight at S={_rails.State.S:0.0}"); return; }
        Piece? back = null;
        foreach (var p in _pieces)
            if (p.Plan.Ride == piece.Plan.Ride && p.Plan.Kind == "switch_back" && p.S > piece.S && (back == null || p.S < back.S)) back = p;
        var backS = back == null ? float.NaN : SwitchBackRules.BackAtSwitchOut(back.Live, back.S);
        _rails.Cross(piece.S, backS);
        EventBus.Instance.Log("The cart takes the switch onto the slag line."); // the clunk is the cart's sound's (its Cross edge, on every peer)
        GD.Print($"[rails] switch {piece.Plan.Id} taken at S={_rails.State.S:0.0}: across from {piece.S:0.0}, {(back is { Live: true } ? $"the return open: {back.Plan.Id}'s lever decides at S={back.S:0.0}" : $"back from {backS:0.0}")}");
    }

    /// <summary>
    /// The front reached a switch back's points: its lever is behind the cart this ride. With the cart across and its return still open, the
    /// back is taken as it stands (Ruling 14, <see cref="SwitchBackRules.TakesHome"/>): any thrown lever (a fixed back always is, final review I1)
    /// crosses it home from here; a live one left straight keeps the cart on the slag line, toward the gap, unless no human aboard could have
    /// thrown it (ruling R-lone, <see cref="AbleHumanAboard"/>): then the cart takes the safe line.
    /// </summary>
    private void TakeBack(Piece piece)
    {
        piece.Fired = true;
        if (piece.Switch != null) piece.Switch.Taken = true;
        var across = _rails.Crossing;
        var lone = false;
        if (across && float.IsNaN(_rails.CrossBackS))
        {
            var thrown = piece.Switch is not { } sw || sw.Fixed || sw.Thrown; // a back without its lever is taken as fixed
            lone = !thrown && piece.Live && !AbleHumanAboard();
            var back = SwitchBackRules.BackAtPoints(SwitchBackRules.TakesHome(thrown, piece.Live, ableHumanAboard: !lone), piece.S);
            if (!float.IsNaN(back))
            {
                _rails.Cross(_rails.CrossOutS, back);
                EventBus.Instance.Log(lone ? SwitchBackRules.SafeLineLog : "The cart takes the switch back onto the party line.");
            }
        }
        var home = across && !float.IsNaN(_rails.CrossBackS);
        GD.Print(!across ? $"[rails] switch {piece.Plan.Id} passed at S={_rails.State.S:0.0}: the cart on its own lane"
            : home ? $"[rails] switch {piece.Plan.Id} taken at S={_rails.State.S:0.0}: back from {_rails.CrossBackS:0.0}{(lone ? " (no human aboard able to throw it: the safe line)" : "")}"
            : $"[rails] switch {piece.Plan.Id} passed straight at S={_rails.State.S:0.0}: the cart stays on the slag line");
    }

    /// <summary>A human rider able to throw a deadly lever sits in the party's cart (<see cref="SwitchBackRules.AbleToThrow"/>: up, connected, seated).</summary>
    private bool AbleHumanAboard()
    {
        foreach (var rider in _rails.Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero) && SwitchBackRules.AbleToThrow(hero.Control, hero.Downed, hero.Seated && hero.Cart == _rails.Cart)) return true;
        return false;
    }

    /// <summary>
    /// Server: a press on <paramref name="lever"/> after its moment (final review M4, Ruling 21): a deadly lever of the armed ride that is taken,
    /// or with the cart's front at or past its points (the seated window's end), is too late while straight (refused out loud) and already thrown
    /// when thrown (ignored quietly). <see cref="LatePress.None"/> otherwise (<see cref="SwitchBackRules.Late"/>). The split's window ends only when
    /// it is taken (its <see cref="TrackSwitch.WindowEndS"/> is +∞): its "points" here are its Taken, and a press past its points is in time.
    /// </summary>
    public LatePress LateFor(TrackSwitch lever)
    {
        if (!Current || !lever.Deadly) return LatePress.None;
        foreach (var p in _pieces)
        {
            if (p.Switch != lever) continue;
            if (p.Plan.Ride != ArmedRide) return LatePress.None;
            var view = lever.SeatedView;
            return SwitchBackRules.Late(view.Taken, lever.Thrown, _rails.Party.FrontS, view.PointsS);
        }
        return LatePress.None;
    }

    /// <summary>
    /// A live switch back's calls (Ruling 17, server): a seated hero calls it early, <see cref="SwitchBackRules.CallEarly"/> before the lever
    /// (or at once on crossing nearer), and at the last moment, <see cref="SwitchBackRules.CallLast"/> before the points, while the cart is
    /// across with its return open and the lever straight (<see cref="SwitchBackRules.Call"/>). Each once an arming.
    /// </summary>
    private void TickBackCalls(Piece piece, PartyController party)
    {
        if (!piece.Live || piece.Fired || piece.Switch is not { } sw || _rails.State.Mode != CartMode.Rolling) return;
        var front = _rails.Party.FrontS;
        var call = SwitchBackRules.Call(_rails.Crossing, !float.IsNaN(_rails.CrossBackS), sw.Thrown, front, sw.LeverS, piece.S, piece.Barked, piece.BarkedLast);
        if (call == BackCall.None) return;
        piece.Barked = true; // the last call stands for the early one too
        if (call == BackCall.Last) piece.BarkedLast = true;
        var by = call == BackCall.Early
            ? RailCalls.Say(party, _rails.Cart, BarkTrigger.SwitchBack, "The slag line's out past the bridge — switch back!")
            : RailCalls.Say(party, _rails.Cart, BarkTrigger.SwitchBackNow, "Switch back — now!");
        GD.Print($"[rails] switch {piece.Plan.Id} called {(call == BackCall.Early ? "early" : "last")} at front S={front:0.0} ({piece.S - front:0.0} m to the points); bark={by ?? "none"}");
    }

    /// <summary>
    /// The heap ahead: its lantern blinks while a cart bears down on it, and a seated hero calls it once (<see cref="RailCalls"/>), while it
    /// can still be switched round: as the seated window opens (<see cref="SeatedSwitchRules.WindowAhead"/> before the lever), or with no
    /// lever ahead, <see cref="SetPieceRules.HeapLook"/> before the heap.
    /// </summary>
    private void TickHeap(Piece piece, PartyController party, bool rolling)
    {
        if (piece.Heap is not { IsAlive: true } heap) return;
        var front = _rails.Party.FrontS;
        var headed = SetPieceRules.HeapHeaded(rolling, heap.FaceS, front, _rails.OnEnemyLane(heap.FaceS), SwitchSetBefore(piece));
        var look = SetPieceRules.HeapLook(_rails.State.V);
        SetAlarm(heap, SetPieceRules.HeapAlarm(headed, heap.FaceS, front, _rails.State.V));
        if (piece.Barked || !headed) return;
        var lever = LeverAheadOf(piece, front); // the call is useful while the lever is still ahead: at the seated window's start
        var aim = lever ?? heap.FaceS;
        if (lever != null ? aim - front > SeatedSwitchRules.WindowAhead : aim - front > look) return;
        piece.Barked = true;
        var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.LineBlocked, "Line's blocked — switch!");
        GD.Print($"[rails] heap {piece.Plan.Id} called at front S={front:0.0} ({heap.FaceS - front:0.0} m to the heap, aim {aim:0.0}); bark={by ?? "none"}");
    }

    /// <summary>
    /// A switch_out of the heap's ride before it, not yet taken, is thrown: the cart will go round. The server knows a taken switch by its
    /// piece having fired; a client by the lever's Taken, which the server tells as it takes it.
    /// </summary>
    private bool SwitchSetBefore(Piece heap)
    {
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != heap.Plan.Ride || p.Plan.Kind != "switch_out" || p.S >= heap.S || p.Switch is not { Thrown: true } sw) continue;
            if (!(NetSession.IsClient ? sw.Taken : p.Fired)) return true;
        }
        return false;
    }

    /// <summary>The heap's lantern blinks or not, its bell ringing at the heap as it starts (every role, by the same rule); the RIDE pass prints each change on both sides (server time).</summary>
    private static void SetAlarm(OreHeap heap, bool alarm)
    {
        if (heap.Alarm == alarm) return;
        heap.Alarm = alarm;
        if (alarm) RideAudio.At("cart_bell", heap.SoundPoint);
        if (NetSession.Instance?.TestRide == true)
            GD.Print(FormattableString.Invariant($"[{(NetSession.IsClient ? "net" : "net-server")}] ride heap alarm={(alarm ? "on" : "off")} t={(NetSession.IsClient ? NetSession.Instance.Client?.ServerNow ?? 0 : NetSession.Instance.Now):0.000}"));
    }

    /// <summary>The lever's S (<see cref="TrackSwitch.LeverS"/>, which the seated window measures from) of an untaken switch_out before the heap that the front has not reached yet, or null.</summary>
    private float? LeverAheadOf(Piece heap, float front)
    {
        foreach (var p in _pieces)
            if (p.Plan.Ride == heap.Plan.Ride && p.Plan.Kind == "switch_out" && !p.Fired && p.S < heap.S && p.Switch!.LeverS > front) return p.Switch!.LeverS;
        return null;
    }

    // ---- the seated window (SeatedSwitchRules) ---------------------------------------------------

    private readonly List<SeatedLever> _seatedViews = new();
    private readonly List<TrackSwitch> _seatedSwitches = new();

    /// <summary>
    /// The armed ride's lever offered to <paramref name="hero"/> from the seat (the nearer of two), or null: seated in the party's cart,
    /// up, the cart rolling, the front in the lever's window (<see cref="SeatedSwitchRules.Nearest"/>); a live switch back only while the
    /// cart is across with no return set (<see cref="SwitchBackRules.Offered"/>). Every peer: a client's pieces, <see cref="ArmedRide"/>,
    /// the levers' Taken and the cart's state (its crossing too) are the server's, as told.
    /// </summary>
    public TrackSwitch? SeatedLeverFor(Adventurer hero)
    {
        if (!Current) return null;
        _seatedViews.Clear();
        _seatedSwitches.Clear();
        for (var i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (p.Plan.Ride != ArmedRide || p.Switch is not { } sw || !LeverOffered(p)) continue;
            _seatedViews.Add(sw.SeatedView);
            _seatedSwitches.Add(sw);
        }
        var k = SeatedSwitchRules.Nearest(hero.Seated && hero.Cart == _rails.Cart, !hero.Downed && !hero.Parked, _rails.State.Mode == CartMode.Rolling, _rails.Party.FrontS, _seatedViews);
        return k < 0 ? null : _seatedSwitches[k];
    }

    /// <summary>
    /// <paramref name="lever"/>, a piece of the armed ride, is in <paramref name="hero"/>'s seated window (not only the nearest), up to
    /// <see cref="SeatedSwitchRules.AcceptLead"/> before it opens here (<see cref="SeatedSwitchRules.Accepts"/>): the server's acceptance of a use out of reach.
    /// </summary>
    public bool InSeatedWindow(Adventurer hero, TrackSwitch lever)
    {
        if (!Current) return false;
        for (var i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (p.Plan.Ride != ArmedRide || p.Switch != lever) continue;
            return LeverOffered(p) && SeatedSwitchRules.Accepts(hero.Seated && hero.Cart == _rails.Cart, !hero.Downed && !hero.Parked, _rails.State.Mode == CartMode.Rolling, _rails.Party.FrontS, lever.SeatedView);
        }
        return false;
    }

    /// <summary>A piece's lever may be offered from the seat: any but a live switch back, which only while the cart is across with no return set (Ruling 16).</summary>
    private bool BackOffered(Piece p) => !p.Live || SwitchBackRules.Offered(_rails.Crossing, !float.IsNaN(_rails.CrossBackS));

    /// <summary>A piece's lever may be offered from the seat now: the split's only while its runaway is coming (<see cref="SplitOffered"/>), a live back by <see cref="BackOffered"/>.</summary>
    private bool LeverOffered(Piece p) => p.Split != null ? SplitOffered(p) : BackOffered(p);

    /// <summary>The buckets tip; a live patch the cart's body is over dents the plating and burns each seated hero, once per patch.</summary>
    private void TickBuckets(Piece piece, float dt)
    {
        if (piece.Buckets is not { } buckets) return;
        buckets.Tick(dt);
        if (_rails.State.Mode == CartMode.Wrecked) return;
        foreach (var patch in buckets.Patches)
        {
            if (patch.HitCart || !IsInstanceValid(patch)) continue;
            if (Mathf.Abs(patch.S - _rails.State.S) > CartRules.HalfLength + CinderPatch.Radius || _rails.OnEnemyLane(patch.S)) continue;
            patch.HitCart = true;
            EventBus.Instance.Log("The cart rolls through the cinders!");
            _burnt.Clear();
            foreach (var rider in _rails.Cart.Riders.Values) _burnt.Add(rider); // a burn can unseat: read the book first
            var attack = TrapRules.FloorFire(_builder.CurrentFloor);
            var burnt = 0;
            foreach (var rider in _burnt)
                if (rider is Adventurer { Downed: false } hero && IsInstanceValid(hero)) { hero.ReceiveAttack(Trap.TrapStats, attack, "A cinder"); burnt++; }
            GD.Print($"[rails] cinders {patch.Name} at S={patch.S:0.0} hit the cart at S={_rails.State.S:0.0}: plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - PatchDent):0}, {burnt} riders burnt");
            _rails.DentPlating(PatchDent);
            if (_rails.State.Mode == CartMode.Wrecked) return;
        }
    }

    /// <summary>
    /// The party's cart on the enemy lane runs into <paramref name="cart"/> from behind: the slag cart derails (its riders under
    /// it), the plating takes <see cref="RamDent"/> and the riders feel it. True when it was rammed.
    /// </summary>
    private bool Ram(SlagCart cart)
    {
        if (_rails.State.Mode != CartMode.Rolling || !_rails.FrontOnEnemyLane) return false;
        if (cart.Run.State.S < Lanes.EnemyS(_rails.State.S)) return false; // behind the party's cart
        var front = Lanes.EnemyS(_rails.Party.FrontS);
        if (cart.Run.BackS - front > RamReach) return false;
        Rams++;
        GD.Print($"[rails] t={_now:0.00} the party's cart rams {cart.Name} (party S={_rails.State.S:0.0} front on the enemy lane {front:0.0}, slag back {cart.Run.BackS:0.0}, v {_rails.State.V:0.0} vs {cart.Run.State.V:0.0}); plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - RamDent):0}");
        EventBus.Instance.Log("The cart rams the slag cart!");
        cart.Derail();
        foreach (var rider in _rails.Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.09f, 0.5f);
        _rails.DentPlating(RamDent);
        return true;
    }
}
