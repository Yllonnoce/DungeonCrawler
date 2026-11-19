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
/// party cart's physics, with no pumping and no set pieces judged, its run-over sweep on, its Limit recomputed every tick
/// (<see cref="SpareRules.ChaseLimit"/>) so it halts <see cref="SpareRules.ChaseGap"/> behind the train's last body and never passes
/// it. Once it stands there behind a held train (at a stop or halted) its riders step out beside the lane, the chase ends and the
/// chaser is gone after <see cref="SpareRules.HideSeconds"/>. A wreck, a load or a quiet unseat of its last player ends it at once.
/// The chase is not saved; its spare stays spent. One chase at a time. Never solo: solo the spares stay looks only.
/// </summary>
public partial class RailController
{
    const string ChaserSourceName = "the spare cart";

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
    }

    private Chase? _chase;
    private readonly Dictionary<ulong, double> _chaseHitAt = new();
    private readonly List<(Node3D body, TrackBody tb)> _chaseHits = new();
    private readonly List<ICartRider> _chaseRiders = new();

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
    /// <summary>A chaser was placed (before anyone is seated in it): the cart sender listens to its book from here.</summary>
    public event Action<MineCart, int>? ChaserPlaced;

    /// <summary>The stop whose spare <paramref name="cart"/> is, or −1 (its wire name is "spare:{stop}").</summary>
    public int SpareStopOf(MineCart cart) => _spares.StopOf(cart);

    /// <summary>The S of the train's last body's back, one half length further back: the S the chaser's centre stops short of (<see cref="SpareRules.TrainBackS"/>).</summary>
    private float TrainBackS => SpareRules.TrainBackS(Party.BackS, _train.Count, WagonSpacing);

    /// <summary>The chaser's limit now: behind the train's last body, never past the stop after the one the train last reached.</summary>
    private float ChaseLimitNow() => SpareRules.ChaseLimit(TrainBackS, StopS[Math.Min(StopIndex + 1, StopS.Length - 1)]);

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
        var onFoot = SpareRules.OnFootPlayer(IsPlayer(actor), actor.Seated, actor.Downed || actor.Parked, 0f);
        if (SpareRules.Prompt(onFoot, MayChaseAt(stop)).Length == 0) return;
        StartChase(stop, actor);
        Flush();
    }

    /// <summary>The spare is spent and a chaser stands Held on the line at its stop (or back at its limit while the train's tail is still there), the actor and theirs seated.</summary>
    private void StartChase(int stop, Adventurer actor)
    {
        if (_chase != null) FreeChaser(); // one still waiting to be hidden goes now
        if (GetParent() is not Node3D root) return;
        _spares.Use(stop); // the "used" PropEvt: it vanishes from beside the platform
        var s = SpareRules.ChaserStart(StopS[stop], ChaseLimitNow());
        var cart = new MineCart { Name = $"ChaserCart{stop}", CartId = CartIds.Chaser(stop), PromptFor = _ => "", CollisionLayer = 1 }.AsChaser();
        root.AddChild(cart);
        var run = new CartRun(Party.Track, cart, new CartState(s, 0f, CartMode.Held, CartRules.MaxPlating)) { Limit = s };
        run.PoseAt(s);
        run.Sync();
        _chase = new Chase { Stop = stop, Cart = cart, Run = run };
        _chaseHitAt.Clear();
        ChaserPlaced?.Invoke(cart, stop);
        SeatIn(cart, actor);
        foreach (var mate in Main.Instance.Party.Active)
            if (mate != actor && !mate.Downed && !mate.Seated && OwnCompanion(mate, actor) && mate.GlobalPosition.DistanceTo(actor.GlobalPosition) <= CompanionBoardRange)
                SeatIn(cart, mate);
        SeatChaserBench(cart, actor.GlobalPosition);
        _chase.Boarding.Start();
        EventBus.Instance.Log($"{actor.State.Name} takes the spare cart after the train.");
        RefreshHud();
    }

    private static bool SeatIn(MineCart cart, Adventurer hero)
    {
        if (cart.Seats.Take(hero.RiderId, isHero: true) is not int seat) return false;
        hero.SitIn(cart, seat);
        return true;
    }

    /// <summary>Escorts on foot near the player (as the party cart's bench takes them) ride the chaser's bench: left at the stop, they would be stranded.</summary>
    private void SeatChaserBench(MineCart cart, Vector3 from)
    {
        if (GetParent() is not { } floor) return;
        _bench.Clear();
        for (var i = 0; i < floor.GetChildCount(); i++)
            if (floor.GetChild(i) is Npc { Dead: false, Downed: false, Escorting: true, Seated: false, Crew: false } npc
                && npc.GlobalPosition.DistanceTo(from) <= CompanionBoardRange) _bench.Add(npc);
        if (_bench.Count == 0) return;
        _benchFrom = from;
        _bench.Sort(_benchOrder);
        foreach (var npc in _bench)
        {
            if (cart.Seats.Take(npc.RiderId, isHero: false) is not int seat) break;
            npc.SitIn(cart, seat);
        }
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
        var limit = ChaseLimitNow();
        if (!c.Boarded)
        {
            CountChasers(party, c.Cart, out var seated, out var living);
            if (seated == 0) { EndChase(atOnce: true); return; } // nobody is left in it: nothing to chase for
            if (c.Boarding.Tick(dt, seated, living)) c.Boarded = true;
        }
        var run = c.Run;
        if (run.State.Mode == CartMode.Held && SpareRules.ChaseRolls(c.Boarded, run.State.S, limit))
        {
            run.State = run.State with { Mode = CartMode.Rolling };
            run.Settle(); // from a standstill: nothing banked from the wait
            c.Cart.CollisionLayer = 0; // a rolling chaser runs bodies over, as the party cart does; it offers nothing to use
            if (!c.Departed) EventBus.Instance.Log("The spare cart rolls after the train.");
            c.Departed = true;
        }
        if (run.State.Mode == CartMode.Rolling)
        {
            run.Limit = Mathf.Max(limit, run.State.S); // the train never backs: a limit behind S would only pull the chaser back
            run.Step(new CartInput(run.SlopeAhead, false, 0, 0, run.Limit), dt);
            ChaseRunOver(party, c);
            if (run.State.Mode == CartMode.Held) { run.Settle(); c.Cart.CollisionLayer = 1; }
        }
        else run.Settle();
        run.Sync();
        if (SpareRules.ChaseDone(c.Boarded, run.State.Mode == CartMode.Held, State.Mode == CartMode.Held, run.State.S, limit))
        {
            LastChaseGap = TrainBackS + CartRules.HalfLength - (run.State.S + CartRules.HalfLength); // the last body's back to the chaser's front
            ChasesDone++;
            EventBus.Instance.Log("The spare cart pulls up behind the train.");
            EndChase(atOnce: false);
        }
    }

    /// <summary>Heroes up in the world not riding the train, and how many of them sit in the chaser: its countdown's "all aboard". No allocation.</summary>
    private void CountChasers(PartyController party, MineCart chaser, out int seated, out int living)
    {
        seated = living = 0;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Downed || hero.Cart == Cart) continue;
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
        c.HideAt = _now + SpareRules.HideSeconds;
        if (IsInstanceValid(c.Cart))
        {
            c.Run.State = c.Run.State with { Mode = CartMode.Held, V = 0f };
            c.Run.Settle();
            c.Run.Sync();
            c.Cart.CollisionLayer = 1;
            SetDownChasers(c.Cart);
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

    /// <summary>The chaser goes (anyone still in it gets out where it stands first).</summary>
    private void FreeChaser()
    {
        if (_chase is not { } c) return;
        _chase = null;
        if (!IsInstanceValid(c.Cart)) return;
        if (c.Cart.Riders.Count > 0) SetDownChasers(c.Cart);
        c.Cart.Visible = false;
        c.Cart.CollisionLayer = 0;
        c.Cart.QueueFree();
    }

    /// <summary>A player who left the game while seated in the chaser steps out beside the lane where it stands (no throw, no run-over, no log).</summary>
    private void UnseatChaserQuietly(Adventurer hero)
    {
        if (_chase is not { } c || !IsInstanceValid(c.Cart) || hero.Cart != c.Cart) return;
        var at = _builder.Surface(ExitCellNear(c.Cart.GlobalPosition, atStop: true, useTaken: false));
        hero.GetOut(at, Vector3.Zero);
    }

    /// <summary>Server, each rendered frame: the chaser and its riders posed.</summary>
    private void PoseChaser()
    {
        if (_chase is not { } c || !IsInstanceValid(c.Cart)) return;
        c.Run.Pose((float)Engine.GetPhysicsInterpolationFraction());
        foreach (var rider in c.Cart.Riders.Values)
            if (rider is not GodotObject o || IsInstanceValid(o)) rider.FollowSeat();
    }

    /// <summary>The chaser as a live cart for the cart sender (server), or nothing.</summary>
    internal void CollectChaser(List<RailNet.LiveCart> into)
    {
        if (_chase is not { } c || !IsInstanceValid(c.Cart) || c.Cart.IsQueuedForDeletion()) return;
        into.Add(new RailNet.LiveCart(c.Cart.CartId, CartKind.Chaser, 0, c.Run, c.Cart, 0f, null, null, null));
    }

    /// <summary>The chaser's countdown seconds left, or −1 when none runs (the wire).</summary>
    internal float ChaserBoardingLeft => _chase is { Boarded: false, Ended: false } c && c.Boarding.Running ? Mathf.Max(0f, c.Boarding.Left) : -1f;
}
