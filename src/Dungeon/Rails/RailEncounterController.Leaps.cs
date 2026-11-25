using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Combat.Fx;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ember hounds on a slag cart leap across to the party's side. A carried hound that is alongside and within
/// <see cref="SetPieceRules.LeapRange"/> of the party cart (or any coupled wagon of its train, crewed or not; a hounds-only cart's hound
/// needs only the reach once its cart has rolled up, and every hound a body it would pick within <see cref="TrainRules.LeapReach"/>:
/// <see cref="SetPieceRules.HoundCrouches"/>) crouches for
/// <see cref="SetPieceRules.LeapWindup"/> with a glow and a "Hound!" bark, then leaps on a <see cref="LeapSeconds"/> arc onto a
/// boarder spot (two on the cart's rim, one on a wagon's), reserved when the leap starts: a crewed wagon's first (the most crew, the
/// nearer on a tie), else the cart's, else an empty wagon's (<see cref="TrainRules.BoardingOrder"/>). With no spot free it
/// leaps and misses: it lands beside the party lane as a normal creature. A boarder is carried by what it boarded, bites with
/// its normal melee (a wagon boarder: the crew inside, else nobody in particular) and, on the party cart, dents the plating. It falls
/// off, beside the lane, when it dies. A wagon boarder also has a grip (plan 14, Ruling 14): the party's hits on it
/// (<see cref="BoarderRules.Counts"/>), weighed by <see cref="BoarderRules.GripTaken"/>, knock it off alive at <see cref="BoarderRules.Grip"/>
/// (<see cref="OnEnemyHurt"/>): it drops beside the lane, aggro on, left behind. The cart's own rim boarders have none. Server only.
/// </summary>
public partial class RailEncounterController
{
    public const string HoundId = "enemy.ember_hound";
    public const float LeapSeconds = 0.6f;
    public const float LeapApex = 1.2f;
    /// <summary>A boarder on the party cart takes this much plating every <see cref="DentEvery"/> seconds.</summary>
    public const float DentAmount = 3f;
    public const float DentEvery = 2f;

    private enum LeapPhase { Crouch, Air, Aboard }

    private sealed class Leap
    {
        public Enemy Hound = null!;
        public SlagCart Source = null!;
        public LeapPhase Phase;
        public float T;
        public Vector3 FromLocal, FromWorld, To;
        public Node3D? Dest;
        public int Spot;
        public bool Miss;
        public float Dent;
        /// <summary>Where it leapt to on the wire (<see cref="Net.CartIds"/>: the cart it aims at, or the body it misses beside) and when (the server's clock).</summary>
        public int ToCart;
        public double SentAt;
        /// <summary>A wagon boarder's grip taken so far (<see cref="BoarderRules.GripTaken"/> of each party hit) and the hits that took it.</summary>
        public int GripTaken, GripHits;
        /// <summary>
        /// A hijacker that climbed onto a wagon at a stop (plan 16 Task 6, <see cref="BoardWagonAtStop"/>): it is no ride's, so the ride's end
        /// (a departure, an arrival) keeps it aboard; a wreck, a restore or the rest on foot let it go as any boarder (<see cref="ClearLeaps"/>).
        /// </summary>
        public bool Spared;
    }

    private readonly List<Leap> _leaps = new();

    /// <summary>Hounds that crouch, fly or ride on the party's side now (tests and the smoke read it).</summary>
    public int Leapers => _leaps.Count;

    private Vector3 Beside(Vector3 at) => _rails.StandBesideLane(at);

    private bool HasLeap(Enemy hound)
    {
        for (var i = 0; i < _leaps.Count; i++) if (_leaps[i].Hound == hound) return true;
        return false;
    }

    /// <summary>The target body <paramref name="i"/> (0 the party cart, k the k-th coupled wagon, crewed or not) if it can be leapt to, else null.</summary>
    private Node3D? Candidate(int i)
    {
        if (i == 0) return IsInstanceValid(_rails.Cart) ? _rails.Cart : null;
        var train = _rails.Train;
        if (i - 1 >= train.Count) return null;
        var wagon = train[i - 1];
        return IsInstanceValid(wagon) && wagon.Coupled ? wagon : null;
    }

    private readonly List<(int Crew, float Distance)> _boardBodies = new();

    /// <summary>The living crew seated in <paramref name="wagon"/>.</summary>
    private static int CrewIn(TrainWagon wagon)
    {
        var n = 0;
        foreach (var rider in wagon.Riders.Values)
            if (rider is Npc { Crew: true, Dead: false } npc && IsInstanceValid(npc)) n++;
        return n;
    }

    private static float Flat(Vector3 a, Vector3 b) { var dx = a.X - b.X; var dz = a.Z - b.Z; return Mathf.Sqrt(dx * dx + dz * dz); }

    private bool Reserve(Node3D dest, Enemy hound, out int spot)
    {
        // the party cart opens one rim spot per awake hero seated in it (at most its two): a lone rider faces one hound at a time
        spot = dest is MineCart cart ? cart.ReserveBoarderSpot(hound, SetPieceRules.RimSpotsOpen(cart.AwakeHeroesSeated())) : ((TrainWagon)dest).ReserveBoarderSpot(hound);
        return spot >= 0;
    }

    private static void FreeSpot(Node3D? dest, Enemy hound)
    {
        if (dest is MineCart cart) cart.FreeBoarderSpot(hound);
        else if (dest is TrainWagon wagon) wagon.FreeBoarderSpot(hound);
    }

    private static Transform3D SpotOf(Node3D dest, int spot) => dest is MineCart cart ? cart.BoarderSpotTransform(spot) : ((TrainWagon)dest).BoarderSpotTransform(spot);

    private void ReleaseLeap(Leap l) => FreeSpot(l.Dest, l.Hound);

    // ---- the tick --------------------------------------------------------------------------------

    private void TickLeaps(float dt)
    {
        if (_carts.Count > 0) StartCrouches();
        for (var i = _leaps.Count - 1; i >= 0; i--)
        {
            if (i >= _leaps.Count) continue; // defensive: the list emptied under the loop
            var l = _leaps[i];
            var h = l.Hound;
            if (!IsInstanceValid(h) || h.IsQueuedForDeletion()) { ReleaseLeap(l); _leaps.RemoveAt(i); continue; }
            if (!h.IsAlive)
            {
                // it died hanging on (its DeathDrop usually has set the body down already) or in the air: the body goes beside the lane;
                // one that died crouching still rides its slag cart on the enemy line, and the cart frees it with the rest
                if (l.Phase != LeapPhase.Crouch)
                {
                    if (h.Carried) h.Drop(Beside(h.GlobalPosition));
                    else if (h.Leaping) h.LandAt(Beside(h.GlobalPosition));
                }
                ReleaseLeap(l);
                _leaps.RemoveAt(i);
                continue;
            }
            switch (l.Phase)
            {
                case LeapPhase.Crouch:
                    if (!IsInstanceValid(l.Source) || l.Source.Derailed || h.Carrier != l.Source) { _leaps.RemoveAt(i); break; } // its cart is gone: the derail has it
                    l.T += dt;
                    if (l.T >= SetPieceRules.LeapWindup) StartLeap(l);
                    break;
                case LeapPhase.Air:
                    FlyLeap(l, dt, i);
                    break;
                default:
                    if (!TickBoarder(l, dt) && i < _leaps.Count && _leaps[i] == l) _leaps.RemoveAt(i);
                    break;
            }
        }
    }

    private void StartCrouches()
    {
        var partyS = _rails.State.S;
        foreach (var cart in _carts)
        {
            if (!IsInstanceValid(cart) || cart.Derailed) continue;
            var alongside = PaceRules.Alongside(cart.Run.State.S, PaceRules.TargetS(Lanes.EnemyS(partyS), cart.Offset));
            foreach (var rider in cart.Aboard)
            {
                if (!IsInstanceValid(rider) || !rider.IsAlive || rider.Carrier != cart || rider.Profile.Id != HoundId || HasLeap(rider)) continue;
                var nearest = NearestTargetDistance(rider.GlobalPosition);
                if (nearest <= SetPieceRules.LeapRange && !cart.InReachLogged)
                {
                    cart.InReachLogged = true;
                    SlagLog.Add((cart.EncounterId, "in reach", rider.Name, _rails.State.S, _rails.Party.FrontS, nearest, _now));
                }
                if (DebugHoldCrouches) continue; // a test holds the boarders aboard
                // a boarding group leaps from reach once it has rolled up (fix round 1, I2); a mixed cart waits until it is alongside; either only
                // with a body it would pick within TrainRules.LeapReach (I1)
                if (!SetPieceRules.HoundCrouches(cart.HoundsOnly, alongside, cart.SinceSpawn, nearest, nearest <= SetPieceRules.LeapRange && BoardOrder(rider).Count > 0)) continue;
                Crouch(cart, rider);
            }
        }
    }

    /// <summary>Test hook: no hound crouches while set (its boarders stay on their slag carts).</summary>
    internal bool DebugHoldCrouches { get; set; }

    /// <summary>
    /// The bodies <paramref name="hound"/> would board, in the order it tries them (<see cref="TrainRules.BoardingOrder"/>, every one within
    /// <see cref="TrainRules.LeapReach"/>), filling <see cref="_boardBodies"/> (index 0 the party's cart, k the wagon Wk).
    /// </summary>
    private List<int> BoardOrder(Enemy hound)
    {
        _boardBodies.Clear();
        for (var i = 0; i <= _rails.Train.Count; i++)
            _boardBodies.Add(Candidate(i) is { } body ? (i == 0 ? 0 : CrewIn((TrainWagon)body), Flat(hound.GlobalPosition, body.GlobalPosition)) : (0, float.MaxValue));
        return TrainRules.BoardingOrder(_boardBodies, TrainRules.LeapReach);
    }

    private float NearestTargetDistance(Vector3 from)
    {
        var best = float.MaxValue;
        for (var i = 0; ; i++)
        {
            if (i > 0 && i - 1 >= _rails.Train.Count) break;
            if (Candidate(i) is { } body) best = Mathf.Min(best, Flat(from, body.GlobalPosition));
        }
        return best;
    }

    private void Crouch(SlagCart cart, Enemy hound)
    {
        _leaps.Add(new Leap { Hound = hound, Source = cart, Phase = LeapPhase.Crouch });
        TelegraphFx.HandGlow(hound, SetPieceRules.LeapWindup);
        RailCalls.Say(Main.Instance.Party, _rails.Cart, BarkTrigger.HoundLeap, "Hound!", place: hound); // the cart's call; from the wagons, the hero nearest the hound
        GD.Print($"[rails] {hound.Name} crouches on {cart.EncounterId} for the leap");
    }

    private void StartLeap(Leap l)
    {
        var h = l.Hound;
        // the crewed wagons first (the most crew, the nearer on a tie), then the cart, then an empty wagon, the first with a spot free
        // (Task 1 review, I1: a hound goes for the prisoners), each within TrainRules.LeapReach and its spot too (Task 2 review M2; Task 6
        // fix round 1, I1); none: it leaps and misses beside the nearest
        var order = BoardOrder(h);
        Node3D? dest = null;
        var spot = -1;
        var nearest = (Node3D?)null;
        var nearestD = float.MaxValue;
        for (var i = 0; i < _boardBodies.Count; i++)
            if (_boardBodies[i].Distance < nearestD && Candidate(i) is { } body) { nearestD = _boardBodies[i].Distance; nearest = body; }
        foreach (var i in order)
        {
            if (Candidate(i) is not { } body || !Reserve(body, h, out spot)) continue;
            if (Flat(h.GlobalPosition, SpotOf(body, spot).Origin) <= TrainRules.LeapReach) { dest = body; break; }
            FreeSpot(body, h); // its spot lies past the reach: the next
            spot = -1;
        }
        l.Source.Aboard.Remove(h);
        l.FromLocal = l.Source.GlobalTransform.AffineInverse() * h.GlobalPosition;
        l.FromWorld = h.GlobalPosition;
        h.BeginLeap();
        h.DeathDrop = () => Beside(h.GlobalPosition);
        l.Phase = LeapPhase.Air;
        l.T = 0f;
        if (dest != null)
        {
            l.Dest = dest;
            l.Spot = spot;
            l.To = SpotOf(dest, spot).Origin;
            GD.Print($"[rails] {h.Name} leaps from {l.FromWorld} to {dest.Name} spot {spot} ({l.To}), {Flat(l.FromWorld, l.To):0.0} m");
            SlagLog.Add((l.Source.EncounterId, "leapt " + (dest is TrainWagon w ? $"W{w.Number}" : "the cart"), h.Name, _rails.State.S, _rails.Party.FrontS, Flat(l.FromWorld, l.To), _now));
        }
        else
        {
            l.Miss = true;
            var anchor = nearest ?? _rails.Cart;
            var lead = anchor is MineCart cart ? cart.Velocity : anchor is TrainWagon w ? w.Velocity : Vector3.Zero;
            l.To = Beside(anchor.GlobalPosition + lead * LeapSeconds); // the cart will have moved on by the time it lands
            l.Spot = -1;
            GD.Print($"[rails] {h.Name} leaps and misses: no boarder spot free ({nearestD:0.0} m to the nearest body), lands at {l.To}");
            SlagLog.Add((l.Source.EncounterId, "leapt a miss", h.Name, _rails.State.S, _rails.Party.FrontS, Flat(l.FromWorld, l.To), _now));
        }
        // every client flies the arc itself from here (a LeapMsg); the boarder lists in the cart's state say who rides where once it lands
        l.ToCart = WireId(l.Dest ?? nearest ?? _rails.Cart);
        l.SentAt = NetSession.Instance?.Now ?? 0;
        if (NetSession.IsServer && NetSession.Instance?.Server is { } world) world.Broadcast(LeapOf(l));
    }

    /// <summary>A body a hound leaps to, as the wire names it: the party's cart, or a train wagon by its number.</summary>
    private static int WireId(Node3D body) => body is TrainWagon wagon ? CartIds.Wagon(wagon.Number) : CartIds.Party;

    private static LeapMsg LeapOf(Leap l)
        => new(l.Hound.NetId, IsInstanceValid(l.Source) ? l.Source.CartId : -1, l.ToCart, l.Miss ? -1 : l.Spot, l.Miss, l.SentAt);

    /// <summary>The leaps in the air now, for a joiner's catch-up (each as first sent: a client flies it on from its start time).</summary>
    internal void CollectAirLeaps(List<LeapMsg> into)
    {
        into.Clear();
        foreach (var l in _leaps)
            if (l.Phase == LeapPhase.Air && IsInstanceValid(l.Hound) && !l.Hound.IsQueuedForDeletion()) into.Add(LeapOf(l));
    }

    private void FlyLeap(Leap l, float dt, int index)
    {
        var h = l.Hound;
        l.T += dt;
        var t = Mathf.Min(1f, l.T / LeapSeconds);
        // the take-off point rides on the cart it left (both carts move; a world point would drag the arc backwards)
        if (IsInstanceValid(l.Source) && !l.Source.Derailed) l.FromWorld = l.Source.GlobalTransform * l.FromLocal;
        var to = l.Miss || l.Dest == null ? l.To : SpotOf(l.Dest, l.Spot).Origin;
        var pos = l.FromWorld.Lerp(to, t) + Vector3.Up * (LeapApex * 4f * t * (1f - t));
        var flat = new Vector3(to.X - l.FromWorld.X, 0f, to.Z - l.FromWorld.Z);
        var yaw = flat.LengthSquared() > 0.01f ? Mathf.Atan2(-flat.X, -flat.Z) : h.GlobalRotation.Y;
        h.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
        if (t < 1f) return;

        if (l.Miss || l.Dest == null || !IsInstanceValid(l.Dest))
        {
            ReleaseLeap(l);
            h.DeathDrop = null;
            h.LandAt(l.Miss ? l.To : Beside(h.GlobalPosition));
            h.ForceAggro();
            EventBus.Instance.Log("An ember hound misses the cart and lands beside the line.");
            GD.Print($"[rails] {h.Name} landed beside the lane at {h.GlobalPosition}");
            _leaps.RemoveAt(index);
            return;
        }
        var dest = l.Dest;
        var spot = l.Spot;
        h.LandOn(dest, dest is MineCart cart ? () => cart.BoarderSpotTransform(spot) : () => ((TrainWagon)dest).BoarderSpotTransform(spot));
        h.ForceAggro();
        l.Phase = LeapPhase.Aboard;
        l.Dent = 0f;
        if (dest is TrainWagon wagon) h.BoardTarget = NearestCrew(wagon, h.GlobalPosition); // the crew inside, else nobody in particular
        EventBus.Instance.Log(dest is MineCart ? "An ember hound lands on the cart!" : "An ember hound lands on a wagon!");
        GD.Print($"[rails] {h.Name} boarded {dest.Name} spot {spot} at {h.GlobalPosition}");
    }

    private static Npc? NearestCrew(TrainWagon wagon, Vector3 from)
    {
        Npc? best = null;
        var bestD = float.MaxValue;
        foreach (var rider in wagon.Riders.Values)
        {
            if (rider is not Npc { IsAlive: true } npc || !IsInstanceValid(npc)) continue;
            var d = from.DistanceSquaredTo(npc.GlobalPosition);
            if (d < bestD) { bestD = d; best = npc; }
        }
        return best;
    }

    /// <summary>One tick of a boarder: false once it is gone from its carrier (the leap is over).</summary>
    private bool TickBoarder(Leap l, float dt)
    {
        var h = l.Hound;
        if (l.Dest == null || !IsInstanceValid(l.Dest) || h.Carrier != l.Dest || (l.Dest is TrainWagon { Coupled: false }))
        {
            if (h.Carried) h.Drop(Beside(h.GlobalPosition));
            h.BoardTarget = null;
            h.DeathDrop = null;
            ReleaseLeap(l);
            return false;
        }
        h.FollowCarrier();
        if (l.Dest is TrainWagon wagon)
        {
            if (h.BoardTarget is not { IsAlive: true } || !IsInstanceValid(h.BoardTarget)) h.BoardTarget = NearestCrew(wagon, h.GlobalPosition); // the one it bit went down: the next
            return true;
        }
        if (_rails.State.Mode != CartMode.Rolling) return true; // it only bites the plating of a cart that rolls (a halted one stands still; arriving at a stop mends the cart to full)
        l.Dent += dt;
        if (l.Dent >= DentEvery)
        {
            l.Dent -= DentEvery;
            _rails.DentPlating(DentAmount); // plating gone wrecks the cart; the Wrecked event comes at the RailController's next flush, and ClearLeaps then drops the boarders at the wreck's place
        }
        return true;
    }

    /// <summary>Pose the boarders on what they ride (every rendered frame, after the carts are posed).</summary>
    private void FollowBoarders()
    {
        for (var i = 0; i < _leaps.Count; i++)
            if (_leaps[i].Phase == LeapPhase.Aboard && IsInstanceValid(_leaps[i].Hound)) _leaps[i].Hound.FollowCarrier();
    }

    // ---- the grip (plan 14, Ruling 14) ----------------------------------------------------------------

    /// <summary>Wagon boarders knocked off on this floor build (tests read it).</summary>
    internal int KnockOffs { get; private set; }

    /// <summary>The aboard leap of <paramref name="hound"/> riding a coupled wagon's rim, or null (the cart's rim boarders have no grip).</summary>
    private Leap? WagonBoarderLeap(Enemy hound)
    {
        for (var i = 0; i < _leaps.Count; i++)
        {
            var l = _leaps[i];
            if (l.Hound == hound && l.Phase == LeapPhase.Aboard && l.Dest is TrainWagon { Coupled: true } wagon && IsInstanceValid(wagon) && hound.Carrier == wagon) return l;
        }
        return null;
    }

    /// <summary>The grip taken off <paramref name="hound"/> so far while it rides a wagon's rim (test hook), or −1 when it does not.</summary>
    internal int GripOf(Enemy hound) => WagonBoarderLeap(hound)?.GripTaken ?? -1;

    private static string MemberName(int index)
        => Main.Instance?.Party is { } party && index >= 0 && index < party.Members.Length ? party.Members[index].Name : $"member {index}";

    /// <summary>
    /// <see cref="EventBus.EnemyHurt"/> (server and solo): a party member's hit on a wagon boarder that lives through it takes
    /// <see cref="BoarderRules.GripTaken"/> of its kind (<see cref="Enemy.LastHitKind"/>) off its grip; at <see cref="BoarderRules.KnockedOff"/>
    /// it is knocked off (<see cref="KnockOff"/>). A killing blow leaves it to its death's drop, as before.
    /// </summary>
    private void OnEnemyHurt(Enemy e, float amount, Combat.HitFlavor flavor, int byMember)
    {
        if (NetSession.IsClient || !Current || !BoarderRules.Counts(byMember) || !IsInstanceValid(e) || !e.IsAlive) return;
        if (WagonBoarderLeap(e) is not { } l || l.Dest is not TrainWagon wagon) return;
        l.GripTaken += BoarderRules.GripTaken(e.LastHitKind);
        l.GripHits++;
        var by = MemberName(byMember);
        GD.Print($"[rails] t={_now:0.00} {e.Name} on wagon {wagon.Number}: grip {l.GripTaken}/{BoarderRules.Grip} after a {e.LastHitKind} hit by {by} (hp {e.Hp:0}/{e.MaxHp:0})");
        if (BoarderRules.KnockedOff(l.GripTaken)) KnockOff(l, wagon, by);
    }

    /// <summary>The grip is gone: the hound drops beside the lane alive, aggro on, and its rim spot is free; <see cref="TickBoarder"/> lets the leap go.</summary>
    private void KnockOff(Leap l, TrainWagon wagon, string by)
    {
        var h = l.Hound;
        h.Drop(Beside(h.GlobalPosition));
        h.BoardTarget = null;
        h.DeathDrop = null;
        h.ForceAggro();
        ReleaseLeap(l);
        KnockOffs++;
        EventBus.Instance.Log($"{by} knocks the {(h.Profile.Id == HoundId ? "hound" : Adventure.Voice.StopTrapLines.Lower(h.DisplayName))} off wagon {wagon.Number}!"); // a hijacker may be a cinder imp (plan 16)
        GD.Print($"[rails] t={_now:0.00} {h.Name} knocked off wagon {wagon.Number} by {by} after {l.GripHits} hit(s)");
    }

    // ---- test hooks ----------------------------------------------------------------------------------

    private readonly HashSet<Enemy> _debugBoarders = new();

    /// <summary>A hound <see cref="DebugBoard"/> put on a wagon: the test flows' culls spare it.</summary>
    internal bool IsDebugBoarder(Enemy e) => _debugBoarders.Contains(e);

    /// <summary>
    /// A creature on foot climbs onto <paramref name="w"/>'s boarder spot (server and solo; plan 16 Task 6: a hijacker the stop trap sends to a
    /// wagon of the held or rolling-back train): it is carried there as a leap lands a hound (aggro on, biting the nearest crew inside, its death
    /// dropping it beside the lane) and has the wagon boarder's grip. <paramref name="spared"/> (a hijacker's: the default) keeps it aboard
    /// through a departure or an arrival (<see cref="Leap.Spared"/>). False when the wagon is not a coupled wagon of the train, its spot is held,
    /// or the creature is not on its feet.
    /// </summary>
    internal bool BoardWagonAtStop(Enemy e, TrainWagon w, bool spared = true)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(e) || !e.IsAlive || e.IsQueuedForDeletion() || e.Carried || e.Leaping) return false;
        if (!IsInstanceValid(w) || !w.Coupled || _rails.WagonAt(w.Number) != w) return false;
        if (w.ReserveBoarderSpot(e) < 0) return false;
        e.Carry(w, () => w.BoarderSpotTransform(0));
        e.ForceAggro();
        e.DeathDrop = () => Beside(e.GlobalPosition);
        e.BoardTarget = NearestCrew(w, e.GlobalPosition);
        _leaps.Add(new Leap { Hound = e, Phase = LeapPhase.Aboard, Dest = w, Spot = 0, ToCart = WireId(w), SentAt = NetSession.Instance?.Now ?? 0, Spared = spared });
        if (spared) GD.Print($"[rails] {e.Name} climbs onto wagon {w.Number} at S={_rails.State.S:0.0} ({_rails.Hijack}, V {_rails.State.V:0.00})");
        return true;
    }

    /// <summary>A hijacker aboard a wagon of the train (<see cref="BoardWagonAtStop"/>'s spared boarder), still on its spot.</summary>
    internal bool IsSparedBoarder(Enemy e)
    {
        for (var i = 0; i < _leaps.Count; i++)
        {
            var l = _leaps[i];
            if (l.Spared && l.Hound == e && l.Phase == LeapPhase.Aboard && l.Dest != null && e.Carrier == l.Dest) return true;
        }
        return false;
    }

    /// <summary>
    /// Test hook (server and solo): an ember hound landed on wagon <paramref name="wagon"/>'s rim spot, as a leap lands one (carried, aggro on,
    /// biting the nearest crew inside, its death dropping it beside the lane): <see cref="BoardWagonAtStop"/>'s steps, not spared (the ride's end
    /// takes it). With <paramref name="holdHp"/> its HP never drops below 1, so only the grip ends it. Null when the wagon is not coupled or its spot is held.
    /// </summary>
    internal Enemy? DebugBoard(int wagon, bool holdHp = false)
    {
        if (NetSession.IsClient || !Current || _rails.WagonAt(wagon) is not { Coupled: true } w) return null;
        if (_builder.SpawnEnemy(HoundId, w.BoarderSpotTransform().Origin, _builder.CurrentFloor) is not { } h) return null;
        h.IsReinforcement = true; // no loot farm
        h.DebugHoldHp = holdHp;
        if (!BoardWagonAtStop(h, w, spared: false)) { h.QueueFree(); return null; }
        _debugBoarders.RemoveWhere(e => !IsInstanceValid(e)); // the hounds freed since (Task 4 review M7)
        _debugBoarders.Add(h);
        GD.Print($"[rails] test: a hound boards wagon {wagon}");
        return h;
    }

    /// <summary>Test hook: <paramref name="hound"/> dies as a blow would kill it (its death drops it beside the lane).</summary>
    internal void DebugKill(Enemy hound)
    {
        if (!IsInstanceValid(hound) || !hound.IsAlive) return;
        hound.DebugHoldHp = false;
        hound.Kill();
        GD.Print($"[rails] test: {hound.Name} killed on {(hound.Carrier as Node)?.Name ?? "foot"}");
    }

    /// <summary>
    /// The ride is over: boarders and leapers go silently like riders, or, when the cart wrecked, drop beside the lane where it was
    /// wrecked (<see cref="RailController.WreckedAt"/>: a spare may have moved the cart, and its boarders with it, before the
    /// Wrecked event is told) and fight on foot. A wreck that was a fall (<see cref="RailController.LastWreckFell"/>) took the boarders
    /// with the train: one still aboard and alive here goes into the crevasse too; a leaper in the air lands beside the lip.
    /// A spared boarder (a hijacker on a wagon, plan 16) is kept aboard unless <paramref name="all"/> (a wreck, a restore, the rest on foot).
    /// </summary>
    private void ClearLeaps(bool wrecked, bool all)
    {
        var wreck = _rails.WreckedAt;
        for (var i = _leaps.Count - 1; i >= 0; i--)
        {
            var l = _leaps[i];
            var h = l.Hound;
            if (l.Spared && !all && IsInstanceValid(h) && !h.IsQueuedForDeletion()) continue; // the hijacker on its wagon rides on
            _leaps.RemoveAt(i);
            ReleaseLeap(l);
            if (!IsInstanceValid(h) || h.IsQueuedForDeletion()) continue;
            h.BoardTarget = null;
            h.DeathDrop = null;
            if (l.Phase == LeapPhase.Crouch) continue; // still a rider of its slag cart: that cart frees it
            if (wrecked && _rails.LastWreckFell && l.Phase == LeapPhase.Aboard && h.IsAlive && CrevasseWatch.Instance is { } watch)
                watch.Take(h, wreck ?? h.GlobalPosition); // the train's fall took it (RailController.Fall); dead, it is freed below
            if (wrecked && h.IsAlive)
            {
                var at = Beside(wreck ?? h.GlobalPosition);
                if (h.Carried) h.Drop(at);
                else if (h.Leaping) h.LandAt(at);
                h.ForceAggro();
                GD.Print($"[rails] {h.Name} drops off the wrecked cart at {at}");
            }
            else h.QueueFree();
        }
    }
}
