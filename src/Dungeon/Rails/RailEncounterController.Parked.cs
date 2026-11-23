using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 5's parked train (plan 15 Task 5, spec B.5; Rulings 14, 23-25): a <c>parked</c> piece names a <see cref="ParkedTrain"/> standing on the
/// party lane with its cart's front <see cref="ParkedRules.FrontShort"/> short of the exit bridge (<see cref="BuildParked"/>, every role, from the
/// layout's bridge). While its ride is armed and it is in the way, the server's tick (<see cref="TickParked"/>) blinks its lantern by the heap's
/// rule with the bell at the alarm's start, has a seated hero call it 35 m before its rear (<see cref="BarkTrigger.ParkedCart"/>), and smashes it
/// as the party's front reaches its rear face: riders aboard thrown clear onto the outer bank first, then the barricade's cost on the whole train
/// (<see cref="SmashCost"/>), five tubs tumbling. Every tick it steps <see cref="ParkedRules.Next"/>: a lift stands once one is due and no chaser
/// or relief is on the line. Its ride's arming and every restore stand it in the way again; it is not saved. On the wire: <c>PropEvt parked:{piece}</c>
/// "standing" (a lift only), "smashed", "gone" and "inway" (a restore's or an arming's), and its state by name in the prop check's rail part.
/// A client's copy shows what it is told and blinks its lantern from its own cart, judging nothing.
/// </summary>
public partial class RailEncounterController
{
    private Piece? _parkedPiece;
    private readonly List<ICartRider> _parkedRiders = new();

    /// <summary>The parked train's state now (every role; a client's is the server's, from its PropEvts and the prop check). In the way on a floor without one.</summary>
    public ParkedState ParkedNow { get; private set; } = ParkedState.InTheWay;
    /// <summary>
    /// The lift took over as the party's train (plan 15 Task 6, Ruling 25's latch): set at the takeover's press (<see cref="RailController.TakeOverAt"/>),
    /// cleared when the party's train arrives at the last stop and at every restore. While it is set <see cref="ParkedRules.Next"/> keeps the parked train gone.
    /// Server and solo.
    /// </summary>
    public bool TakenOver { get; internal set; }
    /// <summary>Times the party's train smashed the parked train on this floor build (tests read it).</summary>
    internal int ParkedSmashes { get; private set; }
    /// <summary>The parked train when built live, else null.</summary>
    internal ParkedTrain? Parked => _parkedPiece?.Parked is { } t && IsInstanceValid(t) ? t : null;
    /// <summary>The plan id of the live parked piece (r5_parked on the shipped floor), or null.</summary>
    internal string? ParkedId => Parked != null ? _parkedPiece!.Plan.Id : null;
    /// <summary>The live parked piece's index (its wire name's number), or −1.</summary>
    internal int ParkedIndex => Parked != null ? _parkedPiece!.Index : -1;
    /// <summary>Its rear face and its cart's front face on the party lane (--print-map's island line), NaN without one.</summary>
    internal float ParkedRearS => Parked?.RearFaceS ?? float.NaN;
    internal float ParkedFrontS => Parked is { } t ? t.CartS + CartRules.HalfLength : float.NaN;

    // ---- building --------------------------------------------------------------------------------

    /// <summary>
    /// A <c>parked</c> piece (every role, after the exit bridge is read): live when its S lies within <see cref="ParkedRules.AtSlack"/> of its place's
    /// rear (<see cref="ParkedRules.RearS"/> from the bridge's start), else a dead marker with an error line (the bridge is what counts).
    /// </summary>
    private Node3D? BuildParked(Node3D root, Piece piece)
    {
        var id = piece.Plan.Id;
        Node3D Dead(string why)
        {
            GD.PushError($"[rails] ERROR: parked {id} on ride {piece.Plan.Ride} at S={piece.S:0.0}: {why}; a dead marker stands in its place");
            var marker = new Node3D { Name = $"Parked_{id}" };
            root.AddChild(marker);
            return marker;
        }
        if (float.IsNaN(ExitFromS) || !_rails.Party.Track.Valid) return Dead("the layout has no exit bridge to stand it short of");
        if (_parkedPiece != null) return Dead($"{_parkedPiece.Plan.Id} already stands before the exit bridge");
        var rear = ParkedRules.RearS(ExitFromS, ParkedRules.Wagons);
        if (Mathf.Abs(piece.S - rear) > ParkedRules.AtSlack) return Dead($"its At lies {Mathf.Abs(piece.S - rear):0.0} m from its place (rear S={rear:0.0}, more than {ParkedRules.AtSlack:0} m)");
        var cartS = ParkedRules.CartS(ExitFromS);
        var train = new ParkedTrain { Name = $"ParkedTrain_{id}", Index = piece.Index, NetRef = NetRefs.Parked(piece.Index) };
        root.AddChild(train);
        train.OuterSide = OuterSideOfPartyLane(cartS);
        train.Pose(cartS, _rails.Party.Track);
        train.PromptFor = _rails.LiftPromptFor; // the lift's "Board the cart" and its rider's "Get out" on all five bodies (plan 15 Task 6, Ruling 28), on every role
        train.OnUse = _rails.UseLift;
        piece.Parked = train;
        _parkedPiece = piece;
        ParkedNow = ParkedState.InTheWay;
        GD.Print($"[rails] parked {id}: rear S={rear:0.0}, cart S={cartS:0.0}, front S={ParkedRules.FrontS(ExitFromS):0.0} ({ParkedRules.FrontShort:0.0} m short of the exit bridge at {ExitFromS:0.0}), a cart and {ParkedRules.Wagons} wagons");
        return train;
    }

    /// <summary>The side of the party lane at <paramref name="partyS"/> away from the slag line: +1 its frame's +X.</summary>
    private int OuterSideOfPartyLane(float partyS)
    {
        var frame = _rails.Party.Track.Frame(partyS).Orthonormalized();
        if (!_enemy.Valid) return 1;
        var toSlag = _enemy.Point(Lanes.EnemyS(partyS)) - frame.Origin;
        return toSlag.Dot(frame.Basis.X) >= 0f ? -1 : 1;
    }

    // ---- arming, restoring ------------------------------------------------------------------------

    /// <summary>Its ride arms (server and solo): it stands in the way again and its call may come again.</summary>
    private void ArmParked(int ride)
    {
        if (_parkedPiece is not { } piece || Parked == null || piece.Plan.Ride != ride) return;
        if (piece.S < _armFromS) return; // armed from past it (the lift's takeover, Ruling 27): a passed parked piece does not stand
        piece.Barked = false;
        SetParked(ParkedState.InTheWay, smashed: false, why: $"ride {ride}'s arming");
    }

    /// <summary>A restore (a load, a rise, a test's): anyone still aboard it is set down beside it, and it stands in the way again (Ruling 25).</summary>
    private void ParkedOnRestore()
    {
        if (_parkedPiece is not { } piece || Parked is not { } train) return;
        piece.Barked = false;
        TakenOver = false; // Ruling 25: a restore clears the takeover's latch
        SetDownParkedRiders(train, "the cart is restored");
        SetParked(ParkedState.InTheWay, smashed: false, why: "a restore");
    }

    // ---- ticking (server and solo) ---------------------------------------------------------------

    /// <summary>
    /// Each physics tick: while its ride is armed and it stands in the way, the lantern blinks as the party's train bears down on it (the bell at
    /// the alarm's start), a seated hero calls it once with the front within <see cref="ParkedRules.CallAt"/> of its rear face, and the front reaching
    /// that face smashes it (<see cref="SmashParked"/>). Then <see cref="ParkedRules.Next"/> steps its state, with the takeover's latch (<see cref="TakenOver"/>)
    /// and any chase on the line (a chaser, a relief or the lift itself: it stands again only once that is gone).
    /// </summary>
    private void TickParked(PartyController party, bool rolling)
    {
        if (_parkedPiece is not { } piece || Parked is not { } train) return;
        var smashed = false;
        if (ParkedNow == ParkedState.InTheWay && train.Standing && ArmedRide == piece.Plan.Ride)
        {
            var run = _rails.Party;
            float prevFront = BarricadeRules.FrontOf(run.Dir, run.PrevS), front = run.FrontS, rear = train.RearFaceS;
            var onItsLane = !_rails.OnEnemyLane(rear) && run.Dir > 0;
            SetParkedAlarm(train, SetPieceRules.HeapAlarm(SetPieceRules.HeapHeaded(rolling, rear, front, !onItsLane, false), rear, front, _rails.State.V));
            if (onItsLane && ParkedRules.CallDue(rolling, front, rear, piece.Barked))
            {
                piece.Barked = true;
                RailCalls.Say(party, _rails.Cart, BarkTrigger.ParkedCart, ParkedRules.CallLog);
                GD.Print($"[rails] parked {piece.Plan.Id} called at front S={front:0.0} ({rear - front:0.0} m to its rear)");
            }
            if (rolling && onItsLane && ParkedRules.Smashes(prevFront, front, rear)) smashed = SmashParked(piece, train, front);
        }
        else if (train.Alarm) SetParkedAlarm(train, false);
        var next = ParkedRules.Next(ParkedNow, smashed, ParkedLiftDue(), takenOver: TakenOver, chaseOnLine: _rails.Chaser != null);
        if (next != ParkedNow) SetParked(next, smashed, smashed ? "the smash" : "a lift is due");
    }

    /// <summary>A lift is due now (<see cref="ParkedRules.LiftDue"/>): the train's rear past the bridge's far edge, held at the last stop, no live cart, or wrecked inside.</summary>
    private bool ParkedLiftDue()
        => ParkedRules.LiftDue(TrainRearS, ExitToS, _rails.State.Mode == CartMode.Held && _rails.StopIndex >= _rails.StopS.Length - 1,
            liveCart: IsInstanceValid(_rails.Cart) && _rails.Cart.Visible, wreckedInside: _rails.LastWreckInside);

    /// <summary>The lantern blinks or not, the bell ringing at it as it starts (every role, by the same rule).</summary>
    private static void SetParkedAlarm(ParkedTrain train, bool alarm)
    {
        if (train.Alarm == alarm) return;
        train.Alarm = alarm;
        if (alarm) RideAudio.At("cart_bell", train.SoundPoint);
    }

    /// <summary>
    /// The party's train smashes the parked train (Ruling 24): logged first (the flows read the before-values in that call), riders aboard thrown
    /// clear onto the outer bank, the barricade's cost on the whole train (<see cref="SmashCost"/>), and its five tubs tumble. True when it was smashed.
    /// </summary>
    private bool SmashParked(Piece piece, ParkedTrain train, float front)
    {
        if (GetParent() is not Node3D) return false;
        float before = _rails.State.Plating, v0 = _rails.State.V;
        EventBus.Instance.Log(ParkedRules.SmashLog);
        var velocity = _rails.Cart.Velocity;
        SetDownParkedRiders(train, "the smash");
        var hurt = SmashCost("The parked train", wholeTrain: true); // pre-flight F4: the whole train's seated heroes pay
        train.Smash(velocity);
        ParkedSmashes++;
        GD.Print($"[rails] the cart smashes the parked train at S={front:0.0}: plating {before:0} -> {_rails.State.Plating:0}, {hurt} hero(es) hurt, V {v0:0.0} -> {_rails.State.V:0.0}");
        return true;
    }

    /// <summary>
    /// Everyone aboard the parked train (a race's riders: boarding is refused while it is in the way) is set down clear of it, each on a cell of its
    /// own on the outer bank beside it (<see cref="ParkedSetDown"/>, never a drop and never inside the island). Server and solo.
    /// </summary>
    private void SetDownParkedRiders(ParkedTrain train, string why)
    {
        if (NetSession.IsClient) return;
        _parkedRiders.Clear();
        _parkedRiders.AddRange(train.Cart.Riders.Values);
        foreach (var wagon in train.Wagons) _parkedRiders.AddRange(wagon.Riders.Values);
        if (_parkedRiders.Count == 0) return;
        var taken = new HashSet<GridPos>();
        foreach (var rider in _parkedRiders)
        {
            if (rider is not Node3D body || !IsInstanceValid(body)) continue;
            var at = ParkedSetDown(train, body.GlobalPosition, taken);
            rider.GetOut(at, Vector3.Zero);
            GD.Print($"[rails] parked {_parkedPiece?.Plan.Id}: {(rider is Adventurer hero ? hero.State.Name : rider is Npc npc ? npc.NpcName : body.Name)} thrown clear onto {DungeonBuilder.WorldToCell(at)} ({why})");
        }
        _parkedRiders.Clear();
    }

    /// <summary>
    /// Where a rider of the parked train at <paramref name="at"/> is set down: the nearest cell round it that may take a body, off the rails, off a
    /// prop or a barrel, outside the island and not <paramref name="taken"/>, the outer bank's first; passed through
    /// <see cref="CrevasseRules.SetDownNear"/> from the rider's own footing. With none near, beside the lane outside (<see cref="RailController.StandBesideCart(Vector3)"/>'s rule).
    /// </summary>
    private Vector3 ParkedSetDown(ParkedTrain train, Vector3 at, HashSet<GridPos> taken)
    {
        var here = DungeonBuilder.WorldToCell(at);
        var frame = _rails.Party.Track.Frame(_rails.Party.Track.ClosestS(at)).Orthonormalized();
        bool Ok(GridPos c) => CrevasseRules.SetDownOk(_layout, c) && !_layout.IsRail(c) && !_layout.InIsland(c) && !_builder.IsCellBlocked(c) && !_layout.BreakableSpawns.Contains(c) && !taken.Contains(c);
        int Outer(GridPos c) => (_builder.Surface(c) - frame.Origin).Dot(frame.Basis.X) * train.OuterSide > 0f ? 0 : 1;
        var cell = CrevasseRules.SetDownCells(_layout, here, 24, reach: 4).Where(Ok).Distinct().OrderBy(Outer).Cast<GridPos?>().FirstOrDefault();
        Vector3 spot;
        if (cell is { } c)
        {
            var target = _builder.Surface(c);
            var p = CrevasseRules.SetDownNear(_layout, DungeonBuilder.CellSize, new System.Numerics.Vector2(at.X, at.Z), new System.Numerics.Vector2(target.X, target.Z));
            var landed = DungeonBuilder.WorldToCell(new Vector3(p.X, target.Y, p.Y));
            spot = CrevasseRules.SetDownOk(_layout, landed) && !_layout.InIsland(landed) ? new Vector3(p.X, _builder.Surface(landed).Y, p.Y) : target;
        }
        else spot = _rails.StandBesideLane(at); // the lane's own exit cell (its side kept: outside), never a drop
        taken.Add(DungeonBuilder.WorldToCell(spot));
        return spot + Vector3.Up * 0.1f;
    }

    /// <summary>
    /// The state moves to <paramref name="next"/> (server and solo), told on the wire: in the way, it stands at its place ("inway"); gone, it is
    /// hidden ("gone"), or, <paramref name="smashed"/>, already tumbled ("smashed"); a lift, it stands at its place again with its log line ("standing").
    /// </summary>
    private void SetParked(ParkedState next, bool smashed, string why)
    {
        if (Parked is not { } train || next == ParkedNow) return;
        ParkedNow = next;
        var id = _parkedPiece!.Plan.Id;
        switch (next)
        {
            case ParkedState.InTheWay:
                train.Pose(ParkedRules.CartS(ExitFromS), _rails.Party.Track);
                train.Stand();
                TellParked(train, "inway");
                break;
            case ParkedState.Gone:
                if (!smashed)
                {
                    SetDownParkedRiders(train, why);
                    train.Hide();
                }
                TellParked(train, smashed ? "smashed" : "gone");
                break;
            case ParkedState.Lift:
                train.Pose(ParkedRules.CartS(ExitFromS), _rails.Party.Track);
                train.Stand();
                EventBus.Instance.Log(ParkedRules.LiftLog);
                TellParked(train, "standing");
                break;
        }
        GD.Print($"[rails] parked {id}: {next} ({why})");
    }

    private static void TellParked(ParkedTrain train, string what)
    {
        if (train.NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(train.NetRef, what);
    }

    /// <summary>
    /// The lift takes over as the party's train (Ruling 27; <see cref="RailController.TakeOverAt"/>, server and solo): the latch is set and it goes with no
    /// tumble ("gone"); anyone aboard it is set down first.
    /// </summary>
    internal void ParkedTakenOver()
    {
        TakenOver = true;
        SetParked(ParkedState.Gone, smashed: false, why: "the lift takes over");
    }

    /// <summary>The party's train arrived (server and solo): at the last stop the takeover's latch clears (Ruling 25), so the lift may stand again.</summary>
    private void ParkedOnArrived(int stop)
    {
        if (stop >= _rails.StopS.Length - 1) TakenOver = false;
    }

    /// <summary>The nearest standing buffer's face ahead of party S <paramref name="aheadOf"/> (the lift halts short of it, <see cref="ParkedRules.LiftLimit"/>), or NaN: none, or smashed.</summary>
    internal float StandingBufferFaceS(float aheadOf)
    {
        var face = float.NaN;
        foreach (var p in _pieces)
            if (p.Buffer is { Smashed: false } buffer && IsInstanceValid(buffer) && buffer.FaceS > aheadOf && (float.IsNaN(face) || buffer.FaceS < face)) face = buffer.FaceS;
        return face;
    }

    /// <summary>Its state as a PropEvt word for a joiner's catch-up (the Task 5 review's Minor 1: no in-the-way glimpse until the prop check), or null without one.</summary>
    internal string? ParkedCatchUpWord() => Parked == null ? null : ParkedNow switch { ParkedState.Lift => "standing", ParkedState.Gone => "gone", _ => "inway" };

    /// <summary>
    /// The lift rolls out (pre-flight F5; Task 6 calls it at the lift's departure): its place is <see cref="ParkedState.Gone"/> and told "gone"; its
    /// bodies are the lift's, rolling, so they are not hidden here. <see cref="ParkedRules.Next"/>'s rule 2 stands a lift again once the lift is off the line.
    /// </summary>
    internal void ParkedLiftDeparts()
    {
        if (NetSession.IsClient || Parked is not { } train) return;
        var next = ParkedRules.LiftDeparts(ParkedNow);
        if (next == ParkedNow) return;
        ParkedNow = next;
        TellParked(train, "gone");
        GD.Print($"[rails] parked {_parkedPiece!.Plan.Id}: {next} (the lift departs)");
    }

    // ---- a client's copy -------------------------------------------------------------------------

    /// <summary>
    /// Client: the server told the parked train <paramref name="what"/> ("standing" a lift, "inway", "smashed" with its five tumbles, "gone").
    /// True when it was one of those.
    /// </summary>
    internal bool ApplyParkedTold(ParkedTrain train, string what, Node3D root)
    {
        if (!NetSession.IsClient || !IsInstanceValid(train) || train != Parked) return false;
        switch (what)
        {
            case "standing": ShowParked(train, ParkedState.Lift); return true;
            case "inway": ShowParked(train, ParkedState.InTheWay); return true;
            case "smashed":
                ParkedNow = ParkedState.Gone;
                train.Smash(IsInstanceValid(_rails.Cart) ? _rails.Cart.Velocity : Vector3.Zero);
                return true;
            case "gone": ShowParked(train, ParkedState.Gone); return true; // the lift rolling away keeps its bodies shown (ShowParked)
            default: return false;
        }
    }

    /// <summary>Client: the parked train shown in <paramref name="state"/>, quietly (no tumble): standing at its place, or hidden.</summary>
    private void ShowParked(ParkedTrain train, ParkedState state)
    {
        ParkedNow = state;
        if (state == ParkedState.Gone)
        {
            if (RailNet.Instance is { ClientLiftLive: true }) return; // pre-flight F5: its place is empty, but its bodies are the lift's, rolling: shown
            train.Hide();
            return;
        }
        train.Pose(ParkedRules.CartS(ExitFromS), _rails.Party.Track);
        train.Stand();
    }

    /// <summary>Client: its ride armed (anew): it stands in the way, as the server's arming stands it.</summary>
    /// <summary>
    /// Client: its ride armed (anew): it stands in the way, as the server's arming stands it, only while our cart's front is short of its rear face. A ride armed
    /// from past it (the lift's takeover, Ruling 27) leaves it as the server has it, and so does a joiner's first state between the smash and the last stop (the
    /// Task 5 review's Minor 1: its prop state names it).
    /// </summary>
    private void ApplyParkedArm(int ride)
    {
        if (_parkedPiece is not { } piece || Parked is not { } train) return;
        if (piece.Plan.Ride == ride) { if (ParkedNow != ParkedState.InTheWay && _rails.Party.FrontS < train.RearFaceS) ShowParked(train, ParkedState.InTheWay); }
        else train.Alarm = false;
    }

    /// <summary>Client: the lift's copy went off the wire (its Hidden state): its bodies hidden while its place is empty, else standing at its place.</summary>
    internal void ApplyLiftGone(ParkedTrain train)
    {
        if (!NetSession.IsClient || train != Parked) return;
        if (ParkedNow == ParkedState.Gone) train.Hide();
        else ShowParked(train, ParkedNow);
    }

    /// <summary>Client, each rendered frame on its armed ride: the lantern blinks by the server's rule from our own cart while it stands in the way.</summary>
    private void PresentParked(bool rolling, float front)
    {
        if (Parked is not { } train) return;
        if (ParkedNow != ParkedState.InTheWay || !train.Standing) { SetParkedAlarm(train, false); return; }
        var rear = train.RearFaceS;
        var onItsLane = !_rails.OnEnemyLane(rear) && _rails.Party.Dir > 0;
        SetParkedAlarm(train, SetPieceRules.HeapAlarm(SetPieceRules.HeapHeaded(rolling, rear, front, !onItsLane, false), rear, front, _rails.State.V));
    }

    /// <summary>The prop check's rail part: the parked state by name (null without a live parked train: every other floor's hash is as it was).</summary>
    private void CaptureParked(RailPropsDto r)
    {
        if (Parked != null) r.Parked = ParkedNow.ToString();
    }

    /// <summary>Client: the parked state moved to the server's rail part, silently (no tumble). Adds what changed.</summary>
    private void ApplyParkedProps(RailPropsDto r, List<string> changed)
    {
        if (Parked is not { } train || r.Parked is not { } name || !Enum.TryParse<ParkedState>(name, out var state) || state == ParkedNow) return;
        ShowParked(train, state);
        changed.Add($"parked {_parkedPiece!.Index} {state}");
    }

    // ---- the wire's names ------------------------------------------------------------------------

    /// <summary>The parked train's wagon <paramref name="k"/> ("liftwagon:{k}"), or null.</summary>
    internal TrainWagon? ParkedWagon(int k) => Parked is { } t && k >= 1 && k <= t.Wagons.Count ? t.Wagons[k - 1] : null;

    // ---- test hooks ------------------------------------------------------------------------------

    /// <summary>Test hook (server and solo): <paramref name="a"/> is seated in the parked train's cart now (a race: boarding is refused while it is in the way).</summary>
    internal bool DebugSeatParked(Adventurer a)
    {
        if (NetSession.IsClient || Parked is not { Standing: true } train || !IsInstanceValid(a)) return false;
        if (train.Cart.Seats.Take(a.RiderId, isHero: true) is not int seat) return false;
        a.SitIn(train.Cart, seat);
        GD.Print($"[rails] parked {_parkedPiece!.Plan.Id}: {a.State.Name} seated in its cart, seat {seat} (a test's hook)");
        return true;
    }
}
