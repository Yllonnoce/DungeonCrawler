using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 5's parked train as the lift (plan 15 Task 6, spec B.5 and B.8; Rulings 25-31). Once it stands again as a lift (<see cref="ParkedState.Lift"/>)
/// every one of its five bodies offers "Board the cart" (<see cref="ParkedRules.Prompt"/>) to a player on foot in reach, unless a chaser or relief is on
/// the line (Ruling 26: the lift is a chase too, so one chase runs at a time). A press on any of them is the same board (<see cref="BoardLift"/>):
/// <list type="bullet">
/// <item>With the party's train alive (held at the last stop, past the bridge, or wrecked inside) the press seats the player, their own companions within
/// 25 m, the escorts on its bench and the freed crew on foot within 25 m in its wagons (<see cref="TrainRules.SeatOrder"/>), with no walk, and starts its
/// own 5 s countdown ("Get out" closed for its first 2 s). It rolls only with a player aboard, as the chaser runs (no pumping, no set piece judged, its
/// four tubs drawn behind it), to its limit <see cref="ParkedRules.LiftLimit"/>: 1.5 m behind the held train inside the island, else short of the
/// standing buffer or at the last stop. While any of its bodies is over the exit bridge its riders get no "Get out". At the halt every rider is set down on
/// the island (<see cref="RailEncounterController.IslandSetDown"/>) and it is hidden <see cref="SpareRules.HideSeconds"/> later; it stands again at its
/// place on the next tick it may (<see cref="ParkedRules.Next"/>).</item>
/// <item>With no live cart and no wreck inside (<see cref="ParkedRules.TakesOver"/>) it takes over (<see cref="TakeOverAt"/>): it goes with no tumble, and
/// the party's cart with four fresh wagons stands Held in its place, past the last ride's stop with no gate or cage holding it (<see cref="StandingPast"/>),
/// the presser seated as at a stop. Its departure arms the last ride from there (<see cref="RailEncounterController.ArmFrom"/>).</item>
/// </list>
/// A save replaces a lift rider's pose with <see cref="SavePose"/>'s spot, never over the bridge (Ruling 31). On the wire the lift is one more live cart
/// (<see cref="CartIds.Lift"/>, its wagons <see cref="CartIds.LiftWagon"/>): a client follows it with its own <see cref="ParkedTrain"/> (RailNet).
/// </summary>
public partial class RailController
{
    /// <summary>The lift's cart while it runs (boarded, rolling, or halted and waiting to be hidden), or null. Server and solo.</summary>
    public MineCart? Lift => _chase is { Lift: true } c && IsInstanceValid(c.Cart) ? c.Cart : null;

    /// <summary>
    /// The party's cart stands past its stop as the lift's takeover (Ruling 27): <see cref="GateClear"/> holds and <see cref="CageHeld"/> does not, so it
    /// counts down at once and leaves no stop's prisoners behind; crew on foot board it as at a held stop. Cleared at its departure and by a restore.
    /// </summary>
    public bool StandingPast { get; private set; }

    /// <summary>The S the last departure left from while standing past its stop (the takeover's place), else NaN: the encounter controller arms the ride from it.</summary>
    internal float DepartedPastS { get; private set; } = float.NaN;

    /// <summary>Lift runs that halted inside and set their riders down (test hooks read them), with the last one's halt S, its gap to the train ahead and its riders set down.</summary>
    internal int LiftsDone { get; private set; }
    internal float LastLiftHaltS { get; private set; } = float.NaN;
    internal float LastLiftGap { get; private set; } = float.NaN;
    internal int LastLiftSetDown { get; private set; }
    /// <summary>Takeovers on this floor build (tests read it).</summary>
    internal int Takeovers { get; private set; }

    /// <summary>A chaser or a relief (not the lift) stands on the line, running or waiting to be hidden: the lift offers and takes no boarding (Ruling 26).</summary>
    private bool ChaserOrReliefOnLine => _chase is { Lift: false } c && IsInstanceValid(c.Cart);

    /// <summary>The party has a cart that can carry it: shown and not wrecked.</summary>
    private bool PartyLiveCart => Cart.Visible && State.Mode != CartMode.Wrecked;

    private readonly List<ICartRider> _liftRiders = new();

    // ---- the prompt and the press (every role; the press is the server's) ----------------------------------------------

    /// <summary>
    /// What the lift's <paramref name="body"/> (its cart or a tub) offers <paramref name="hero"/>, on every role: to its own rider (in the cart) the chaser's
    /// "Get out" while it stands and is not over the bridge; to a player on foot "Board the cart" by <see cref="ParkedRules.Prompt"/> (a client's state and
    /// chasers are the server's as told here). Nothing else, ever: a parked tub offers no revive, no Get out and no climb (Ruling 23).
    /// </summary>
    internal string LiftPromptFor(ICartBody body, Adventurer hero)
    {
        if (!Current || !IsInstanceValid(hero) || Exit is not { Parked: { } train } enc) return "";
        if (hero.Seated)
        {
            if (hero.Cart != train.Cart) return "";
            if (NetSession.IsClient)
                return RailNet.Instance is { } net && IsInstanceValid(net) && net.ClientLift is { } l && !LiftOverBridge(enc, l.S)
                    ? SpareRules.ChaserPrompt(true, l.Mode == CartMode.Held, hero.Downed, l.BoardingLeft) : "";
            return _chase is { Lift: true, Ended: false } c && !LiftOverBridge(enc, c.Run.State.S)
                ? SpareRules.ChaserPrompt(true, c.Run.State.Mode == CartMode.Held, hero.Downed, ChaserBoardingLeft) : "";
        }
        var onFoot = IsPlayer(hero) && !hero.Downed && !hero.Parked;
        var reach = RideInputRules.InBoardReach(body is TrainWagon w ? w.TubDistance(hero) : train.Cart.TubDistance(hero));
        var chase = NetSession.IsClient ? RailNet.Instance is { } rn && IsInstanceValid(rn) && rn.ClientChaser != null : ChaserOrReliefOnLine;
        return ParkedRules.Prompt(enc.ParkedNow, chase, onFoot, reach);
    }

    /// <summary>
    /// A hero pressed interact on one of the lift's bodies (server and solo; a remote's through its InteractIntent, "parked:{piece}" or "liftwagon:{k}"):
    /// its own rider gets out (<see cref="GetOutOfLift"/>); anyone else boards (<see cref="BoardLift"/>). A client's press goes as its intent.
    /// </summary>
    internal void UseLift(ICartBody body, Adventurer actor)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(actor)) return;
        if (_chase is { Lift: true } c && actor.Cart == c.Cart) GetOutOfLift(actor);
        else BoardLift(actor);
        Flush();
    }

    /// <summary>
    /// <paramref name="player"/> boards the lift (Rulings 26-28; server and solo): refused while a chaser or relief is on the line, for anyone seated, down
    /// or not a player, and while no lift stands (a press a stale screen made just after a takeover boards the party's cart that took its place). With no
    /// live cart and no wreck inside it takes over (<see cref="TakeOverAt"/>); otherwise the player, their companions within 25 m, the escorts on its
    /// bench and the freed crew on foot within 25 m sit in it at once and its countdown runs. True when the presser sits in a cart now.
    /// </summary>
    public bool BoardLift(Adventurer player)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(player) || Exit is not { Parked: { } train } enc) return false;
        string? why = null;
        if (ChaserOrReliefOnLine) why = "a chaser is on the line";
        else if (player.Seated) why = "seated";
        else if (player.Downed || player.Parked) why = "down";
        else if (!IsPlayer(player)) why = "a companion boards only with its player";
        else if (enc.ParkedNow != ParkedState.Lift)
        {
            if (enc.TakenOver && StandingPast && State.Mode == CartMode.Held && RideInputRules.InBoardReach(Cart.TubDistance(player)))
            {
                UseCart(player); // the double press (Review Focus 5): the lift took over this tick, and the party's cart stands in its place
                return player.Cart == Cart;
            }
            why = $"it is {enc.ParkedNow}";
        }
        else if (_chase is { Lift: true } running && (running.Departed || running.Ended)) why = "it has left";
        if (why != null)
        {
            GD.Print($"[rails] lift: refused ({why})");
            return false;
        }
        if (ParkedRules.TakesOver(PartyLiveCart, LastWreckInside))
        {
            TakeOverAt(ParkedRules.CartS(enc.ExitFromS), player);
            return player.Cart == Cart;
        }
        var lift = _chase is { Lift: true } c ? c : StartLift(train);
        if (lift == null || !SeatIn(lift.Cart, player))
        {
            GD.Print("[rails] lift: refused (no hero seat free)");
            return false;
        }
        var companions = 0;
        foreach (var mate in Main.Instance.Party.Active)
            if (mate != player && !mate.Downed && !mate.Seated && OwnCompanion(mate, player) && mate.GlobalPosition.DistanceTo(player.GlobalPosition) <= CompanionBoardRange
                && OnTheLiftsSide(mate) && SeatIn(lift.Cart, mate))
                companions++;
        var bench = lift.Cart.Riders.Count;
        SeatBench(lift.Cart, player.GlobalPosition, OnTheLiftsSide);
        var escorts = lift.Cart.Riders.Count - bench;
        var crew = ObjectiveController.Instance is CrewRescueController ctrl && IsInstanceValid(ctrl) ? ctrl.SeatInLift(train.Wagons, player.GlobalPosition) : 0;
        if (!lift.Boarding.Running) lift.Boarding.Start();
        GD.Print($"[rails] lift: {player.State.Name} boards ({companions} companion(s), {crew} crew, {escorts} escort(s))");
        RefreshHud();
        return true;
    }

    /// <summary>A body that may hop into the lift: one outside the island, on its side of the lava (nobody inside is carried back across to ride in again).</summary>
    private bool OnTheLiftsSide(Node3D body) => !_layout.InIsland(DungeonBuilder.WorldToCell(body.GlobalPosition));

    /// <summary>The lift is placed Held at its place, the parked train's own bodies (no spare is spent): one chase at a time.</summary>
    private Chase? StartLift(ParkedTrain train)
    {
        if (_chase != null) FreeChaser(); // one waiting to be hidden goes now
        if (!IsInstanceValid(train) || Exit is not { } enc) return null;
        var s = ParkedRules.CartS(enc.ExitFromS);
        var run = new CartRun(Party.Track, train.Cart, new CartState(s, 0f, CartMode.Held, CartRules.MaxPlating)) { Limit = s };
        run.PoseAt(s);
        run.Sync();
        _chase = new Chase { Lift = true, Train = train, Stop = -1, Cart = train.Cart, Run = run };
        _chaseHitAt.Clear();
        _chaseWarned.Clear();
        ChaserPlacedSound(train.Cart, run.Track, s);
        return _chase;
    }

    /// <summary>The lift's rider gets out while it stands, by the chaser's rule and never over the bridge; with no player left in it, it ends (the rest set down).</summary>
    private void GetOutOfLift(Adventurer actor)
    {
        if (_chase is not { Lift: true, Ended: false } c || Exit is not { } enc || !IsInstanceValid(actor)) return;
        if (LiftOverBridge(enc, c.Run.State.S) || SpareRules.ChaserPrompt(actor.Cart == c.Cart, c.Run.State.Mode == CartMode.Held, actor.Downed, ChaserBoardingLeft).Length == 0) return;
        actor.GetOut(LiftSetDownSpot(c, actor), Vector3.Zero);
        if (IsPlayer(actor))
        {
            _chaseRiders.Clear();
            foreach (var rider in c.Cart.Riders.Values) _chaseRiders.Add(rider);
            foreach (var rider in _chaseRiders)
                if (rider is Adventurer mate && IsInstanceValid(mate) && OwnCompanion(mate, actor)) mate.GetOut(LiftSetDownSpot(c, mate), Vector3.Zero);
            _chaseRiders.Clear();
        }
        if (!PlayerIn(c.Cart)) EndChase(atOnce: !c.Departed);
        RefreshHud();
    }

    // ---- the run (server and solo) -------------------------------------------------------------------------------------

    /// <summary>
    /// The lift's limit now (<see cref="ParkedRules.LiftLimit"/>): behind the party's train while it has a live cart (held or not), short of the standing
    /// buffer, never past the last stop.
    /// </summary>
    private float LiftLimitNow(RailEncounterController? enc, float liftS)
        => ParkedRules.LiftLimit(PartyLiveCart ? TrainBackS : float.NaN, enc?.StandingBufferFaceS(liftS) ?? float.NaN, StopS.Length > 0 ? StopS[^1] : float.NaN);

    /// <summary>
    /// The lift's part of the chase's tick (<see cref="TickChase"/>): its countdown (it rolls only with a player in it), its departure (its place empty:
    /// <see cref="RailEncounterController.ParkedLiftDeparts"/>), its run (<see cref="StepChaser"/>) and its halt behind the train ahead.
    /// </summary>
    private void TickLift(PartyController party, Chase c, RailEncounterController? enc, float dt)
    {
        var run = c.Run;
        var limit = LiftLimitNow(enc, run.State.S);
        if (_chaseHoles.Count > 0) limit = SpareRules.HoleLimit(limit, _chaseHoles);
        if (!c.Boarded)
        {
            if (!PlayerIn(c.Cart)) { EndChase(atOnce: true); return; } // nobody to carry: its riders get out where it stands, and it stands on as a lift
            CountChasers(party, c.Cart, out var seated, out var living);
            if (c.Boarding.Tick(dt, seated, living)) c.Boarded = true;
        }
        if (run.State.Mode == CartMode.Held && SpareRules.ChaseRolls(c.Boarded, run.State.S, limit))
        {
            run.State = run.State with { Mode = CartMode.Rolling };
            run.Settle();
            SetChaseLayers(c, 0); // a rolling lift runs bodies over and offers nothing to use
            if (!c.Departed)
            {
                enc?.ParkedLiftDeparts(); // its place is empty ("gone"); it stands again once it is off the line (Ruling 25's rule 2)
                CollectLiftRiders(c);
                GD.Print($"[rails] lift: rolls from S={run.State.S:0.0} with {_liftRiders.Count} rider(s), its limit S={limit:0.0}");
                _liftRiders.Clear();
            }
            c.Departed = true;
        }
        StepChaser(party, c, enc, limit, dt);
        if (SpareRules.ChaseDone(c.Boarded, run.State.Mode == CartMode.Held, TrainStoppedAhead || !PartyLiveCart, run.State.S, limit)) LiftHalts(c, enc);
    }

    /// <summary>
    /// The lift stands at its limit inside the island with the train ahead stopped for good: every rider is set down on the island's set-down spots, and it
    /// is hidden <see cref="SpareRules.HideSeconds"/> later (<see cref="EndChase"/>).
    /// </summary>
    private void LiftHalts(Chase c, RailEncounterController? enc)
    {
        var s = c.Run.State.S;
        var front = s + CartRules.HalfLength;
        var bufferFace = enc?.StandingBufferFaceS(s) ?? float.NaN;
        var gap = PartyLiveCart ? TrainBackS + CartRules.HalfLength - front : !float.IsNaN(bufferFace) ? bufferFace - front : StopS[^1] - s;
        CollectLiftRiders(c);
        var n = 0;
        if (_liftRiders.Count > 0 && enc != null)
        {
            var spots = enc.IslandSetDown(_liftRiders.Count);
            for (var i = 0; i < _liftRiders.Count; i++)
            {
                var rider = _liftRiders[i];
                if (rider is GodotObject o && !IsInstanceValid(o)) continue;
                SetDownLiftRider(rider, spots[i] + Vector3.Up * 0.1f);
                n++;
            }
        }
        _liftRiders.Clear();
        LiftsDone++;
        LastLiftHaltS = s;
        LastLiftGap = gap;
        LastLiftSetDown = n;
        GD.Print($"[rails] lift: halted at S={s:0.0}, {gap:0.0} m behind the train ahead; {n} rider(s) set down inside");
        EndChase(atOnce: false);
    }

    /// <summary>A rider leaves the lift at <paramref name="at"/>; a crew member set down follows the lead (as a wreck inside sets its crew down).</summary>
    private static void SetDownLiftRider(ICartRider rider, Vector3 at)
    {
        rider.GetOut(at, Vector3.Zero);
        if (rider is Npc { Escorting: false, Downed: false, Dead: false, Crew: true } npc) npc.StartFollowing();
    }

    /// <summary>Every rider still in the lift's five bodies is set down by <see cref="LiftSetDownSpot"/> (its end: at once, or after the halt), each on its own cell.</summary>
    private void SetDownLift(Chase c, string why)
    {
        CollectLiftRiders(c);
        if (_liftRiders.Count == 0) return;
        var riders = _liftRiders.ToList();
        _liftRiders.Clear();
        foreach (var rider in riders)
        {
            if (rider is GodotObject o && !IsInstanceValid(o)) continue;
            SetDownLiftRider(rider, LiftSetDownSpot(c, rider));
        }
        GD.Print($"[rails] lift: {riders.Count} rider(s) set down ({why})");
    }

    /// <summary>The lift goes off the line (<see cref="FreeChaser"/>): anyone left aboard is set down; a lift that rolled is hidden (its place stands it again), one that never rolled stands on.</summary>
    private void FreeLift(Chase c)
    {
        SetDownLift(c, "the lift goes");
        if (c.Train is not { } train || !IsInstanceValid(train)) return;
        if (c.Departed) train.Hide(); // its place is empty: the parked train's next tick stands it there again (ParkedRules.Next)
        else train.SetLayers(ParkedTrain.StandingLayers); // it never rolled: it stands as a lift at its place, solid again
    }

    /// <summary>The chase's body on <paramref name="layers"/>: the chaser's cart, or the lift's five bodies.</summary>
    private static void SetChaseLayers(Chase c, uint layers)
    {
        if (c.Train is { } train && IsInstanceValid(train)) train.SetLayers(layers);
        else c.Cart.CollisionLayer = layers;
    }

    private void CollectLiftRiders(Chase c)
    {
        if (c.Train is { } train && IsInstanceValid(train)) train.CollectRiders(_liftRiders);
        else _liftRiders.Clear();
    }

    /// <summary>The lift's last body's back face at a cart centre of <paramref name="cartS"/>.</summary>
    private static float LiftRearS(float cartS) => cartS - ParkedRules.Wagons * TrainRules.Spacing - CartRules.HalfLength;

    /// <summary>Any body of the lift with its cart's centre at <paramref name="cartS"/> is over the exit bridge (<see cref="ExitBridgeRules.Holds"/>, at any speed): its riders stay put.</summary>
    private static bool LiftOverBridge(RailEncounterController enc, float cartS)
        => ExitBridgeRules.Holds(true, cartS + CartRules.HalfLength, LiftRearS(cartS), enc.ExitFromS, enc.ExitToS);

    /// <summary>
    /// Where a rider of the lift steps out (its end, a quiet unseat, a save's pose): on the island's set-down spots once the lift's rear is past the bridge's
    /// far edge, else beside the parked train's place, outside (<see cref="ParkedRules.SavePoseS"/>): never over the bridge, never across it. Each rider of
    /// the lift has its own spot, by its place in the lift's book.
    /// </summary>
    private Vector3 LiftSetDownSpot(Chase c, ICartRider rider)
    {
        if (Exit is not { } enc || c.Train is not { } train) return ((Node3D)rider).GlobalPosition;
        train.CollectRiders(_liftRiders);
        var index = Math.Max(0, _liftRiders.IndexOf(rider));
        var count = Math.Max(_liftRiders.Count, index + 1);
        _liftRiders.Clear();
        var parked = ParkedRules.CartS(enc.ExitFromS);
        var s = ParkedRules.SavePoseS(c.Run.State.S, LiftRearS(c.Run.State.S), enc.ExitToS, parked);
        if (s != parked) return enc.IslandSetDown(count)[index] + Vector3.Up * 0.1f;
        _exitTaken.Clear();
        var at = Party.Track.Point(parked);
        var cell = ExitCellNear(at, atStop: true, useTaken: true, inside: false);
        for (var i = 0; i < index; i++)
        {
            _exitTaken.Add(cell);
            cell = ExitCellNear(at, atStop: true, useTaken: true, inside: false);
        }
        _exitTaken.Clear();
        return _builder.Surface(cell) + Vector3.Up * 0.1f;
    }

    /// <summary>
    /// A rider's pose for the save (Ruling 31; <c>PartyController</c>'s and <c>FloorStateCapture</c>'s captures read it): a hero or NPC seated in the lift is
    /// saved at <see cref="LiftSetDownSpot"/> (beside the parked train outside, or on the island once the lift's rear is past the far edge), so a load never
    /// stands a body over the lava; anyone else at <paramref name="at"/>. Server and solo.
    /// </summary>
    public Vector3 SavePose(ICartRider r, Vector3 at)
    {
        if (NetSession.IsClient || _chase is not { Lift: true } c || c.Train is not { } train || !IsInstanceValid(train) || !train.Owns(r.Cart)) return at;
        return LiftSetDownSpot(c, r);
    }

    // ---- the takeover (Ruling 27) ----------------------------------------------------------------------------------------

    /// <summary>
    /// The lift takes over as the party's train (server and solo; <see cref="BoardLift"/> with <see cref="ParkedRules.TakesOver"/>): the parked train goes
    /// with no tumble and its latch is set (<see cref="RailEncounterController.TakenOver"/>); the party's cart with four fresh wagons (<see cref="CoupleFresh"/>)
    /// stands Held at <paramref name="cartS"/> with full plating, at the last ride's stop by index and standing past it (<see cref="StandingPast"/>), and
    /// <paramref name="presser"/> boards it as at a stop (their companions within 25 m and the escorts too), its countdown starting at once.
    /// </summary>
    public void TakeOverAt(float cartS, Adventurer presser)
    {
        if (NetSession.IsClient || !Current || StopS.Length < 2) return;
        GD.Print($"[rails] lift: the party has no live cart: it takes over at S={cartS:0.0}");
        Exit?.ParkedTakenOver();
        Takeovers++;
        StopIndex = new LineShape(StopS.Length).LastRideStop;
        StandingPast = true;
        DepartedPastS = float.NaN;
        Party.Reset(new CartState(cartS, 0f, CartMode.Held, CartRules.MaxPlating));
        Cart.Visible = true;
        SetCartLayer();
        Party.Limit = cartS;
        Cart.Speed = 0f;
        ResetLine();
        Boarding.Cancel();
        _heldStartCall = null;
        _hitAt.Clear();
        _warnedAt.Clear();
        Party.Sync();
        Party.PoseAt(cartS);
        Cart.Sound?.Prime(); // the jump from the wreck to its place makes no sound
        _train.UncoupleAll(); // four fresh wagons: the wreck's are emptied and coupled again behind the cart here
        CoupleFresh("the lift takes over");
        Raise(new CartEvent(CartEventKind.Arrived, Stop: StopIndex)); // as a spare's takeover tells it: the cart stands at its stop again
        GD.Print($"[rails] lift: the party's cart stands at S={cartS:0.0}, past stop {StopIndex}; no gate or cage holds it");
        if (IsInstanceValid(presser)) UseCart(presser); // boarded as at a stop: the countdown starts with no gate or cage to hold it
        RefreshHud();
    }

    /// <summary>
    /// A no-spare plain wreck (Task 5 review 7): any party wagon left shown within the parked train's span is uncoupled (lost: hidden, as an inside wreck's
    /// train), so the lift that stands on the next tick shares its place with no party body. A restore or a takeover couples them fresh.
    /// </summary>
    private void ClearTheLiftsSpan()
    {
        if (Exit is not { Parked: not null } enc || _train.Count == 0) return;
        var rear = RunawayRules.TrainRearS(Party.BackS, _train.Count, WagonSpacing);
        if (!ParkedRules.InItsSpan(Party.FrontS, rear, enc.ParkedRearS, enc.ParkedFrontS)) return;
        _train.UncoupleAll();
        GD.Print($"[rails] lift: the wrecked train (front S={Party.FrontS:0.0}, rear S={rear:0.0}) stood in the parked train's place: its wagons are uncoupled");
    }

    // ---- test hooks (server and solo) --------------------------------------------------------------------------------------

    /// <summary>The lift's cart centre S while it runs, else NaN (test hooks read it).</summary>
    internal float LiftS => _chase is { Lift: true } c && IsInstanceValid(c.Cart) ? c.Run.State.S : float.NaN;
    /// <summary>A body of the running lift is over the exit bridge: its riders get no Get out (test hooks read it).</summary>
    internal bool LiftHoldsRiders => _chase is { Lift: true } c && Exit is { } enc && LiftOverBridge(enc, c.Run.State.S);

    /// <summary>Test hook: <paramref name="a"/> presses the lift (any of its bodies), as a remote's press would be taken. True when it now sits in a cart.</summary>
    internal bool DebugBoardLift(Adventurer a)
    {
        if (NetSession.IsClient || !IsInstanceValid(a)) return false;
        var seated = BoardLift(a);
        Flush();
        return seated;
    }

    /// <summary>Test hook: every spare is marked spent, as a save with them all spent loads.</summary>
    internal void DebugSpendSpares()
    {
        if (NetSession.IsClient) return;
        _spares.Restore(Enumerable.Range(1, Math.Max(0, StopS.Length - 2)));
        GD.Print($"[rails] test: every spare spent ({SparesLeft} left)");
    }

    /// <summary>Test hook (solo too): stop <paramref name="stop"/>'s chaser placed empty on the line, as a take would place it.</summary>
    internal void DebugPlaceChaser(int stop)
    {
        if (NetSession.IsClient || stop < 1 || stop >= StopS.Length - 1) return;
        if (PlaceChaser(stop) is { } c) GD.Print($"[rails] test: stop {stop}'s chaser placed empty at S={c.Run.State.S:0.0}");
        RefreshHud();
    }

    /// <summary>Test hook: the chase ends at once and the chaser is hidden at once (a spare it took stays spent: it counts as having rolled).</summary>
    internal void DebugEndChase()
    {
        if (NetSession.IsClient || _chase is not { } c) return;
        c.Departed = true;
        EndChase(atOnce: true);
        FreeChaser();
        RefreshHud();
    }
}
