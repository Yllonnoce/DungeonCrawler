using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The spare cart for a co-op player left behind (the spec's "Co-op and saving"). Online, a stop's spare offers "Take the spare cart"
/// to a player on foot once the train has left that stop (<see cref="SpareRules.MayChase"/>); taking it spends the spare (its "used"
/// PropEvt) and puts a chaser on the party line at that stop: a <see cref="MineCart"/> of kind <see cref="CartKind.Chaser"/>, id
/// <see cref="CartIds.Chaser"/>(stop), Held, with the player and their own companions (and escorts on foot near them, on its bench)
/// seated and its own 5 s <see cref="Boarding"/> counting down. It then runs on the party track with <see cref="CartRun"/> at the
/// party cart's physics, with no pumping and no set pieces judged, its run-over sweep on and its warning given as every party-lane
/// cart's (the bell, the bark, AI bodies to the pockets), its Limit recomputed every tick (<see cref="SpareRules.ChaseLimit"/>) so it
/// halts <see cref="SpareRules.ChaseGap"/> behind the train's last body and never passes it. It never floats over a pit: its front
/// reaching a jump's gap or a fallen trestle lands it just past (<see cref="SpareRules.HopPast"/>); a live ore heap halts it at the face
/// (<see cref="SpareRules.HeapLimit"/>) until the heap is cleared. While it stands its riders may get out ("Get out",
/// <see cref="SpareRules.ChaserPrompt"/>, never in the countdown's first <see cref="Boarding.MinWait"/>, never while downed); with no player
/// left in it the chase ends. Once it stands behind a held train (at a stop or halted) its riders step out beside the lane, the chase ends
/// and the chaser is gone after <see cref="SpareRules.HideSeconds"/>. A wreck, a load or nobody in it before it leaves ends it at once. A
/// chase that ends before its chaser ever rolled gives the spare back (<see cref="SpareRules.GivesSpareBack"/>: parked and offered again);
/// once it has rolled the spare stays spent. The chase is not saved. One chase at a time. Never solo: solo the spares stay looks only.
/// Solo, the same chaser is sent to a player left behind as a relief cart (plan 7, <see cref="Chase.Relief"/>, RailController.Relief):
/// it spends no spare, is never sent to a client, and runs its own phases (<see cref="ReliefPhase"/>) over the shared run below.
/// </summary>
public partial class RailController
{
    const string ChaserSourceName = RunOverRules.ChaserSource;

    private sealed class Chase
    {
        public int Stop;
        public MineCart Cart = null!;
        public CartRun Run = null!;
        /// <summary>The chaser's own countdown (the party cart's is <see cref="RailController.Boarding"/>).</summary>
        public readonly Boarding Boarding = new();
        /// <summary>The countdown ran out: it may roll.</summary>
        public bool Boarded;
        /// <summary>It has rolled at least once.</summary>
        public bool Departed;
        /// <summary>Its riders are set down: it only waits to be hidden.</summary>
        public bool Ended;
        public double HideAt = double.PositiveInfinity;
        /// <summary>A relief cart sent to a solo player left behind (plan 7): no spare spent or given back, never sent online.</summary>
        public bool Relief;
        /// <summary>The relief's phase (a relief only).</summary>
        public ReliefPhase Phase;
        /// <summary>Seconds the relief has stood Waiting.</summary>
        public float Waited;
        /// <summary>Where the relief was placed (centre S).</summary>
        public float SpawnS;
        /// <summary>The coming relief's stop point (centre S): it only moves forward.</summary>
        public float Target;
        /// <summary>Ride 5's parked train running as the lift (plan 15 Task 6, Ruling 26): one chase at a time; no spare spent or given back, its own limit (<see cref="ParkedRules.LiftLimit"/>).</summary>
        public bool Lift;
        /// <summary>The lift's parked train (its cart is <see cref="Cart"/>, its four tubs ride behind it), or null for a chaser or relief.</summary>
        public ParkedTrain? Train;
    }

    private Chase? _chase;
    private readonly Dictionary<ulong, double> _chaseHitAt = new();
    private readonly List<(Node3D body, TrackBody tb)> _chaseHits = new();
    private readonly List<ICartRider> _chaseRiders = new();
    private readonly Dictionary<int, double> _chaseWarned = new();
    private readonly List<(float From, float To)> _chaseHoles = new();

    /// <summary>The chaser standing on the line now (running, or waiting to be hidden), or null. Server and solo.</summary>
    public MineCart? Chaser => _chase is { } c && IsInstanceValid(c.Cart) ? c.Cart : null;
    /// <summary>The chaser's run (server), or null.</summary>
    internal CartRun? ChaserRun => _chase is { } c && IsInstanceValid(c.Cart) ? c.Run : null;
    /// <summary>The stop whose spare the chaser is, or −1.</summary>
    internal int ChaserStop => _chase?.Stop ?? -1;
    /// <summary>A chase runs now (taken, not yet over): no other spare is offered. A client reads the server's flag.</summary>
    public bool ChaseLive => NetSession.IsClient ? (_netFlags & (byte)CartFlags.ChaseLive) != 0 : _chase is { Ended: false };
    /// <summary>Chases that ended behind the train (test hooks read it), and the gap between the bodies the last one left (m).</summary>
    internal int ChasesDone { get; private set; }
    internal float LastChaseGap { get; private set; } = float.NaN;
    /// <summary>Holes in the line the chasers have hopped (test hooks read it).</summary>
    internal int ChaserHops { get; private set; }
    /// <summary>A chaser was placed (before anyone is seated in it): the cart sender listens to its book from here.</summary>
    public event Action<MineCart, int>? ChaserPlaced;

    /// <summary>The stop whose spare <paramref name="cart"/> is, or −1 (its wire name is "spare:{stop}").</summary>
    public int SpareStopOf(MineCart cart) => _spares.StopOf(cart);

    /// <summary>The S of the train's last body's back, one half length further back: the S the chaser's centre stops short of (<see cref="SpareRules.TrainBackS"/>).</summary>
    private float TrainBackS => SpareRules.TrainBackS(Party.BackS, _train.Count, WagonSpacing);

    /// <summary>
    /// The chaser's (and a relief's) limit now: behind the train's last body, never past the stop after the one the train last reached, and with a
    /// live parked train (plan 15 Ruling 29) behind its place (<see cref="ParkedRules.BackS"/>, whether or not it stands) and short of the exit bridge.
    /// </summary>
    private float ChaseLimitNow() => SpareRules.ChaseLimit(TrainBackS, StopS[Math.Min(StopIndex + 1, StopS.Length - 1)], ParkedBackS, ParkedBridgeFromS);

    /// <summary>With a live parked train, the S just behind its place (a centre S, <see cref="ParkedRules.BackS"/>), else NaN.</summary>
    private static float ParkedBackS => Exit is { Parked: not null } enc ? ParkedRules.BackS(enc.ExitFromS, ParkedRules.Wagons) : float.NaN;
    /// <summary>With a live parked train, the exit bridge's near edge, else NaN.</summary>
    private static float ParkedBridgeFromS => Exit is { Parked: not null } enc ? enc.ExitFromS : float.NaN;
    /// <summary>The floor's encounter controller, or null.</summary>
    private static RailEncounterController? Exit => RailEncounterController.Instance is { } e && IsInstanceValid(e) ? e : null;

    /// <summary>
    /// The chaser's or relief's limit now came from the parked train's place or the exit bridge, not from the train or the next stop (Ruling 29): its
    /// chase ends there, outside, once the train is held at the last stop (or wrecked inside).
    /// </summary>
    private bool LimitAtParked(float limit) => !float.IsNaN(ParkedBackS) && limit < SpareRules.ChaseLimit(TrainBackS, StopS[Math.Min(StopIndex + 1, StopS.Length - 1)]) - 1e-3f;

    /// <summary>
    /// The train ahead of a chaser, relief or lift has stopped for good there: held (at a stop or halted), or wrecked past the exit bridge's start (Ruling 29).
    /// The wrecked-inside arm is live for the lift only: every wreck ends a chaser's or a relief's chase at once (<c>Wreck</c>'s first line), its riders set
    /// down beside it on its own side (<see cref="SetDownChasers"/> through <c>ExitCellNear</c>'s side rule: the chaser never passes the parked train's place,
    /// so outside), before <see cref="LastWreckInside"/> is known. Kept so the rule reads as Ruling 29 has it.
    /// </summary>
    private bool TrainStoppedAhead => State.Mode == CartMode.Held || LastWreckInside;

    /// <summary>
    /// A chase that ended up against its limit (<see cref="SpareRules.ChaseDone"/>): its gap is the last body's back to its front, or, halted at the parked
    /// train's place (Ruling 29), the place's rear face to its front, with the server's line there.
    /// </summary>
    private void NoteChaseDone(Chase c, float limit)
    {
        var front = c.Run.State.S + CartRules.HalfLength;
        LastChaseAtParked = LimitAtParked(limit);
        LastChaseGap = LastChaseAtParked && Exit is { } enc ? ParkedRules.RearS(enc.ExitFromS, ParkedRules.Wagons) - front : TrainBackS + CartRules.HalfLength - front;
        ChasesDone++;
        if (LastChaseAtParked) GD.Print($"[rails] chase-done: the {(c.Relief ? "relief cart" : "chaser")} halted {LastChaseGap:0.0} m behind the parked train's place at S={c.Run.State.S:0.0}, outside the island; it hopped {ChaserHops} hole(s) in the line");
    }

    /// <summary>The last chase ended at the parked train's place (Ruling 29), not behind the train (test hooks read it).</summary>
    internal bool LastChaseAtParked { get; private set; }

    /// <summary>The spare at <paramref name="stop"/> may be taken now, as this peer knows the cart (a client: the server's mode, stop, flags and used spares).</summary>
    private bool MayChaseAt(int stop)
    {
        if (!NetSession.IsNetworked || StopS.Length == 0 || State.Mode == CartMode.Wrecked) return false;
        var heldThere = State.Mode == CartMode.Held && !Halted && StopIndex == stop;
        return SpareRules.MayChase(stop, _spares.IsUsed(stop), StopIndex, heldThere, ChaseLive);
    }

    /// <summary>The spare's prompt for <paramref name="hero"/> on every peer (online only; solo a spare never asks).</summary>
    private string SparePrompt(int stop, Adventurer hero)
    {
        if (!NetSession.IsNetworked || !Current || _spares.At(stop) is not { } spare) return "";
        if (HijackLive) return ""; // no spare is offered while the train is hijacked (plan 16)
        var onFoot = SpareRules.OnFootPlayer(IsPlayer(hero), hero.Seated, hero.Downed || hero.Parked, spare.TubDistance(hero));
        return SpareRules.Prompt(onFoot, MayChaseAt(stop));
    }

    /// <summary>
    /// A hero used the stop's spare (the server's work; a remote player's through its InteractIntent, whose reach the server checked to the
    /// tub, as the look ray does a host's). The same rule as the prompt, without the reach: a player up on foot, and <see cref="MayChaseAt"/>.
    /// </summary>
    private void UseSpare(int stop, Adventurer actor)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(actor) || _spares.At(stop) == null) return;
        if (HijackLive) { GD.Print($"[rails] spare {stop}: refused (a hijack is live)"); return; } // nor taken (plan 16)
        var onFoot = SpareRules.OnFootPlayer(IsPlayer(actor), actor.Seated, actor.Downed || actor.Parked, 0f);
        if (SpareRules.Prompt(onFoot, MayChaseAt(stop)).Length == 0) return;
        StartChase(stop, actor);
        Flush();
    }

    /// <summary>The spare is spent and a chaser stands Held on the line at its stop (or back at its limit while the train's tail is still there), the actor and theirs seated.</summary>
    private void StartChase(int stop, Adventurer actor)
    {
        if (PlaceChaser(stop) is not { } chase) return;
        var cart = chase.Cart;
        SeatIn(cart, actor);
        foreach (var mate in Main.Instance.Party.Active)
            if (mate != actor && !mate.Downed && !mate.Seated && OwnCompanion(mate, actor) && mate.GlobalPosition.DistanceTo(actor.GlobalPosition) <= CompanionBoardRange)
                SeatIn(cart, mate);
        SeatBench(cart, actor.GlobalPosition);
        chase.Boarding.Start();
        EventBus.Instance.Log($"{actor.State.Name} takes the spare cart after the train.");
        RefreshHud();
    }

    /// <summary>The spare at <paramref name="stop"/> is spent and its chaser stands Held, empty, at its stop (or back at its limit while the train's tail is still there). Null without a floor root.</summary>
    private Chase? PlaceChaser(int stop)
    {
        if (_chase != null) FreeChaser(); // one still waiting to be hidden goes now
        if (GetParent() is not Node3D root) return null;
        _spares.Use(stop); // the "used" PropEvt: it vanishes from beside the platform
        var s = SpareRules.ChaserStart(StopS[stop], ChaseLimitNow());
        var cart = new MineCart { Name = $"ChaserCart{stop}", CartId = CartIds.Chaser(stop), PromptFor = ChaserPromptFor, OnUse = GetOutOfChaser, CollisionLayer = ChaserHeldLayer }.AsChaser();
        root.AddChild(cart);
        var run = new CartRun(Party.Track, cart, new CartState(s, 0f, CartMode.Held, CartRules.MaxPlating)) { Limit = s };
        run.PoseAt(s);
        run.Sync();
        ChaserPlacedSound(cart, run.Track, s); // its departure is announced; a body at it hears the bell now
        _chase = new Chase { Stop = stop, Cart = cart, Run = run };
        _chaseHitAt.Clear();
        _chaseWarned.Clear();
        ChaserPlaced?.Invoke(cart, stop);
        return _chase;
    }

    private static bool SeatIn(MineCart cart, Adventurer hero)
    {
        if (cart.Seats.Take(hero.RiderId, isHero: true) is not int seat) return false;
        hero.SitIn(cart, seat);
        return true;
    }

    /// <summary>A standing chaser is solid and a thing to use (its riders' "Get out"); a rolling one neither.</summary>
    private const uint ChaserHeldLayer = 1u | 8u;

    /// <summary>The chaser's prompt for <paramref name="hero"/> (server): "Get out" for its rider while it stands.</summary>
    private string ChaserPromptFor(Adventurer hero)
        => _chase is { Ended: false } c && IsInstanceValid(c.Cart) ? SpareRules.ChaserPrompt(hero.Cart == c.Cart, c.Run.State.Mode == CartMode.Held, hero.Downed, ChaserBoardingLeft) : "";

    /// <summary>
    /// A rider uses the standing chaser (the server's work; a remote player's through its InteractIntent, "spare:{stop}"): it gets out beside
    /// the lane with its own companions; with no player left in it the chase ends (the rest set down, the chaser gone 2 s later). One that
    /// never rolled is gone at once and gives its spare back. A downed rider, or one in the countdown's first <see cref="Boarding.MinWait"/>, stays.
    /// </summary>
    private void GetOutOfChaser(Adventurer actor)
    {
        if (NetSession.IsClient || !Current || _chase is not { Ended: false } c || !IsInstanceValid(c.Cart) || !IsInstanceValid(actor)) return;
        if (SpareRules.ChaserPrompt(actor.Cart == c.Cart, c.Run.State.Mode == CartMode.Held, actor.Downed, ChaserBoardingLeft).Length == 0) return;
        _exitTaken.Clear();
        SetDownFromChaser(c.Cart, actor);
        if (IsPlayer(actor))
        {
            _chaseRiders.Clear();
            foreach (var rider in c.Cart.Riders.Values) _chaseRiders.Add(rider);
            foreach (var rider in _chaseRiders)
                if (rider is Adventurer mate && IsInstanceValid(mate) && OwnCompanion(mate, actor)) SetDownFromChaser(c.Cart, mate);
            _chaseRiders.Clear();
        }
        if (!PlayerIn(c.Cart))
        {
            if (SpareRules.GivesSpareBack(c.Departed)) EventBus.Instance.Log($"{actor.State.Name} steps back out of the spare cart.");
            EndChase(atOnce: !c.Departed); // one that never rolled goes now, and its spare is parked again (EndChase)
        }
        RefreshHud();
        Flush();
    }

    private void SetDownFromChaser(MineCart chaser, ICartRider rider)
    {
        var cell = ExitCellNear(chaser.GlobalPosition, true, true);
        _exitTaken.Add(cell);
        rider.GetOut(_builder.Surface(cell), Vector3.Zero);
    }

    /// <summary>A player's hero, up and unparked, rides <paramref name="cart"/>. No allocation.</summary>
    private static bool PlayerIn(MineCart cart)
    {
        foreach (var rider in cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero) && !hero.Downed && !hero.Parked && IsPlayer(hero)) return true;
        return false;
    }

    /// <summary>
    /// Each physics tick on the server (after the party cart's step): the chaser's countdown, its departure and its run behind the train
    /// (its Limit from where the train stands now), its run-over sweep, and its end once it is up behind a held train. No allocation.
    /// </summary>
    private void TickChase(PartyController party, float dt)
    {
        if (_chase is not { } c) return;
        if (!IsInstanceValid(c.Cart)) { _chase = null; return; }
        if (c.Ended)
        {
            if (_now >= c.HideAt) FreeChaser();
            return;
        }
        var trainLimit = ChaseLimitNow();
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) && e.GetParent() == GetParent() ? e : null;
        var run = c.Run;
        // a live ore heap ahead halts it at the face, as the party cart is halted there (the chase itself ends only behind the train);
        // a limit it could only reach standing over a hole is pulled back to that hole's near lip (it waits there, never over the pit)
        if (enc != null) enc.CollectPartyLaneHoles(_chaseHoles);
        else _chaseHoles.Clear();
        var limit = enc != null ? SpareRules.HeapLimit(trainLimit, run.FrontS, enc.LiveHeapFaceAhead(run.FrontS)) : trainLimit;
        if (_chaseHoles.Count > 0) limit = SpareRules.HoleLimit(limit, _chaseHoles);
        if (c.Relief) { TickReliefChase(party, c, enc, limit, trainLimit, dt); return; }
        if (c.Lift) { TickLift(party, c, enc, dt); return; }
        if (!c.Boarded)
        {
            CountChasers(party, c.Cart, out var seated, out var living);
            if (seated == 0) { EndChase(atOnce: true); return; } // nobody is left in it: nothing to chase for
            if (c.Boarding.Tick(dt, seated, living)) c.Boarded = true;
        }
        if (run.State.Mode == CartMode.Held && SpareRules.ChaseRolls(c.Boarded, run.State.S, limit))
        {
            run.State = run.State with { Mode = CartMode.Rolling };
            run.Settle(); // from a standstill: nothing banked from the wait
            c.Cart.CollisionLayer = 0; // a rolling chaser runs bodies over, as the party cart does; it offers nothing to use
            if (!c.Departed) EventBus.Instance.Log("The spare cart rolls after the train.");
            c.Departed = true;
        }
        StepChaser(party, c, enc, limit, dt);
        if (SpareRules.ChaseDone(c.Boarded, run.State.Mode == CartMode.Held, TrainStoppedAhead, run.State.S, trainLimit))
        {
            NoteChaseDone(c, trainLimit); // the last body's back (or the parked train's place) to the chaser's front
            EventBus.Instance.Log(SpareRules.PullUpLine(relief: false, LastChaseAtParked));
            EndChase(atOnce: false);
        }
    }

    /// <summary>
    /// One physics step of the chaser's run (the co-op chaser's and a relief's): a rolling one steps toward <paramref name="limit"/>, hops
    /// the line's holes, sweeps bodies and warns the lane, and is solid and usable again once it stands; a standing one is settled.
    /// </summary>
    private void StepChaser(PartyController party, Chase c, RailEncounterController? enc, float limit, float dt)
    {
        var run = c.Run;
        if (run.State.Mode == CartMode.Rolling)
        {
            run.Limit = Mathf.Max(limit, run.State.S); // the train never backs: a limit behind S would only pull the chaser back
            var prevFront = run.FrontS;
            run.Step(new CartInput(run.SlopeAhead, false, 0, c.Lift ? ParkedRules.Wagons : 0, run.Limit), dt); // the lift draws its four tubs
            HopHoles(party, c, prevFront);
            ChaseRunOver(party, c); // nothing left to sweep after a hop (it settled)
            if (run.State.Mode == CartMode.Rolling) enc?.WarnPartyLane(run, c.Cart, _chaseWarned, party);
            if (run.State.Mode == CartMode.Held) { run.Settle(); SetChaseLayers(c, ChaserHeldLayer); }
        }
        else run.Settle();
        run.Sync();
        c.Train?.SetSpeed(run.State.Mode == CartMode.Rolling ? run.State.V : 0f);
    }

    /// <summary>
    /// The chaser's front reached a hole in the party line this step (a jump's gap, a fallen trestle): it is set down just past it, its
    /// riders shaken, never past its limit and never over a pit (<see cref="SpareRules.Hop"/>: with no room past the hole it stops at the
    /// near lip). Anything on the rails is rolled over: the step up to the near lip is swept as ever, then the landing from the far lip to
    /// the landing front (the pit itself sweeps nothing). The client's follower catches up (a snap past 3 m). No allocation.
    /// </summary>
    private void HopHoles(PartyController party, Chase c, float prevFront)
    {
        var run = c.Run;
        if (_chaseHoles.Count == 0 || SpareRules.Hop(prevFront, run.FrontS, _chaseHoles, run.Limit) is not { } hop) return;
        var from = run.FrontS;
        run.State = run.State with { S = Mathf.Min(run.State.S, hop.NearS) };
        ChaseRunOver(party, c); // this step's run up to the near lip (PrevS .. the lip)
        run.State = run.State with { S = hop.S, Mode = hop.Held ? CartMode.Held : CartMode.Rolling, V = hop.Held ? 0f : run.State.V };
        if (hop.Landed)
        {
            run.PrevS = hop.SweepFromS;
            ChaseRunOver(party, c); // the landing: from the far lip to where its front comes down
            ChaserHops++;
            GD.Print($"[rails] the spare cart hops the line's hole at front S={from:0.0}: set down at S={hop.S:0.0}");
            foreach (var rider in c.Cart.Riders.Values)
                if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.1f, 0.4f);
        }
        else GD.Print($"[rails] the spare cart has no room past the line's hole at front S={from:0.0}: it stops at the near lip, S={hop.S:0.0}");
        run.Settle(); // nothing swept or rendered across the pit
    }

    /// <summary>Heroes up in the world not riding the train, and how many of them sit in the chaser: its countdown's "all aboard". No allocation.</summary>
    private void CountChasers(PartyController party, MineCart chaser, out int seated, out int living)
    {
        seated = living = 0;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Downed || CarOf(hero) >= 0) continue; // the train: a wagon's hero rides it too (Ruling 12)
            living++;
            if (hero.Cart == chaser) seated++;
        }
    }

    /// <summary>The chaser's front sweeps bodies as the party cart's does (its own riders and anyone riding are carried); the hits take the run-over outcome. Its plating is never judged.</summary>
    private void ChaseRunOver(PartyController party, Chase c)
    {
        if (!c.Run.Moving) return;
        _bodies.Clear();
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && !hero.Parked && !hero.Downed) _bodies.Add(hero);
        var parent = GetParent();
        for (var i = 0; i < parent.GetChildCount(); i++)
        {
            var child = parent.GetChild(i);
            if (child is Enemy { IsAlive: true, Carried: false, Leaping: false } enemy) _bodies.Add(enemy);
            else if (child is Npc { Dead: false, Downed: false, Seated: false } npc) _bodies.Add(npc);
        }
        RunOverSweep.Judge(c.Run, _bodies, _now, _chaseHitAt, _chaseHits);
        if (_chaseHits.Count == 0) return;
        if (c.Relief) CountReliefHits(); // the local player only
        RunOverEffects.Apply(_chaseHits, ChaserSourceName, TierRules.Lethality, _setAside);
        foreach (var rider in c.Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.06f, 0.4f);
    }

    /// <summary>
    /// The chase is over: the chaser stands Held, its riders step out beside the lane where it stands (as at a stop), and it is gone
    /// <see cref="SpareRules.HideSeconds"/> later, or now (<paramref name="atOnce"/>: a wreck, a load, nobody left in it).
    /// </summary>
    private void EndChase(bool atOnce)
    {
        if (_chase is not { Ended: false } c) return;
        c.Ended = true;
        if (!c.Relief && !c.Lift && SpareRules.GivesSpareBack(c.Departed)) _spares.GiveBack(c.Stop); // it never rolled: the spare stands parked again ("restored"); a relief or the lift never spent one
        if (c.Relief) ReliefSpawnStop = -1;
        c.HideAt = _now + SpareRules.HideSeconds;
        if (IsInstanceValid(c.Cart))
        {
            c.Run.State = c.Run.State with { Mode = CartMode.Held, V = 0f };
            c.Run.Settle();
            c.Run.Sync();
            SetChaseLayers(c, 1); // over: solid, nothing left to use
            if (c.Lift) SetDownLift(c, "the lift ends");
            else SetDownChasers(c.Cart);
        }
        if (atOnce) FreeChaser();
        RefreshHud();
    }

    /// <summary>Every rider of the chaser steps out beside the party lane near it, each on its own cell.</summary>
    private void SetDownChasers(MineCart chaser)
    {
        if (chaser.Riders.Count == 0) return;
        _chaseRiders.Clear();
        foreach (var rider in chaser.Riders.Values) _chaseRiders.Add(rider); // getting out leaves the book: read it first
        _exitTaken.Clear();
        foreach (var rider in _chaseRiders)
        {
            if (rider is GodotObject o && !IsInstanceValid(o)) { chaser.Riders.Remove(rider.RiderId); chaser.Seats.Leave(rider.RiderId); continue; }
            var cell = ExitCellNear(chaser.GlobalPosition, true, true);
            _exitTaken.Add(cell);
            rider.GetOut(_builder.Surface(cell), Vector3.Zero);
        }
        _chaseRiders.Clear();
    }

    /// <summary>The chaser goes (anyone still in it gets out where it stands first). The lift is never freed: it is hidden, or stands again at its place (<see cref="FreeLift"/>).</summary>
    private void FreeChaser()
    {
        if (_chase is not { } c) return;
        _chase = null;
        if (!IsInstanceValid(c.Cart)) return;
        if (c.Lift) { FreeLift(c); return; }
        if (c.Cart.Riders.Count > 0) SetDownChasers(c.Cart);
        c.Cart.Visible = false;
        c.Cart.CollisionLayer = 0;
        c.Cart.QueueFree();
    }

    /// <summary>A player who left the game while seated in the chaser steps out beside the lane where it stands (no throw, no run-over, no log).</summary>
    private void UnseatChaserQuietly(Adventurer hero)
    {
        if (_chase is not { } c || !IsInstanceValid(c.Cart) || hero.Cart != c.Cart) return;
        var at = c.Lift ? LiftSetDownSpot(c, hero) : _builder.Surface(ExitCellNear(c.Cart.GlobalPosition, atStop: true, useTaken: false)); // the lift's: never over the bridge (Ruling 31)
        hero.GetOut(at, Vector3.Zero);
    }

    /// <summary>Server, each rendered frame: the chaser and its riders posed, and its sound fed while the chase runs.</summary>
    private void PoseChaser()
    {
        if (_chase is not { } c || !IsInstanceValid(c.Cart)) return;
        var s = c.Run.RenderS((float)Engine.GetPhysicsInterpolationFraction());
        c.Run.PoseAt(s);
        if (c.Train is { } lift && IsInstanceValid(lift)) { lift.PoseWagons(s, c.Run.Track); lift.FollowWagonRiders(); } // the lift's four tubs behind its cart
        FeedChaserSound(c.Cart, c.Run, s); // an ended chase stands Held: its loops fall silent
        foreach (var rider in c.Cart.Riders.Values)
            if (rider is not GodotObject o || IsInstanceValid(o)) rider.FollowSeat();
    }

    /// <summary>The chaser as a live cart for the cart sender (server), or nothing.</summary>
    internal void CollectChaser(List<RailNet.LiveCart> into)
    {
        if (_chase is not { } c || c.Relief || !IsInstanceValid(c.Cart) || c.Cart.IsQueuedForDeletion()) return; // a relief is solo only: never sent
        into.Add(new RailNet.LiveCart(c.Cart.CartId, CartKind.Chaser, 0, c.Run, c.Cart, 0f, null, null, null));
    }

    /// <summary>The chaser's countdown seconds left, or −1 when none runs (the wire).</summary>
    internal float ChaserBoardingLeft => _chase is { Boarded: false, Ended: false } c && c.Boarding.Running ? Mathf.Max(0f, c.Boarding.Left) : -1f;
}
