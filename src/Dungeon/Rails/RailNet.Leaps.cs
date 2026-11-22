using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>The rails on the wire: a client's leaps and boarders (see the main part).</summary>
public partial class RailNet
{
    // ---- leaps and boarders (a client) ---------------------------------------------------------

    /// <summary>A hound in the air here: flown from where it stood (riding on the cart it left) to its spot, or beside the body it misses.</summary>
    private sealed class Flight
    {
        public Enemy Hound = null!;
        public Node3D? Source;
        public Vector3 FromLocal, FromWorld, To;
        public bool HasLocal;
        public Node3D? Dest;
        public int Spot;
        public bool Miss;
        public double Start;
    }

    private readonly List<Flight> _flights = new();

    /// <summary>Client: hounds in the air here now (the RIDE pass reads it).</summary>
    internal int ClientFlights => _flights.Count;

    /// <summary>
    /// Client: a hound left its slag cart on a leap (or the catch-up says one is in the air): fly it from where it stands, already
    /// <c>serverNow − ServerTime</c> into the arc. Ignored when that hound already flies, or already rides the spot named.
    /// </summary>
    internal void OnLeap(LeapMsg m)
    {
        if (!NetSession.IsClient || !Current || NetSession.Instance.Client is not { } client) return;
        if (client.EnemyByNetId(m.NetId) is not { } hound || !hound.IsAlive) return;
        foreach (var f in _flights) if (f.Hound == hound) return;
        var dest = BodyById(m.ToCart);
        if (!m.Miss && dest != null && hound.Carrier == dest && HeldAt(dest, m.Spot) == hound) return; // it landed here already (the boarder list)
        Node3D? source = _carts.TryGetValue(m.FromCart, out var cc) && IsInstanceValid(cc.Body) ? cc.Body : null;
        if (cc?.Slag is { } slag)
            for (var i = 0; i < SlagCart.MaxRiders; i++)
                if (slag.RiderAt(i) == hound) { slag.Unbind(i); break; }
        ForgetBoarder(hound); // a boarder the client had elsewhere (a stale list) leaves that spot
        if (hound.Carried) hound.Drop(hound.GlobalPosition);
        var flight = new Flight
        {
            Hound = hound, Source = source, FromWorld = hound.GlobalPosition, Dest = dest, Spot = m.Spot, Miss = m.Miss || dest == null || m.Spot < 0, Start = m.ServerTime,
        };
        if (source != null) { flight.FromLocal = source.GlobalTransform.AffineInverse() * hound.GlobalPosition; flight.HasLocal = true; }
        if (flight.Miss)
        {
            // where the server sets it down: beside the party lane, ahead by where the body it missed will be when it lands
            var anchor = dest ?? _rails.Cart;
            var lead = anchor is ICartBody body ? body.Velocity : Vector3.Zero;
            var left = Mathf.Max(0f, RailEncounterController.LeapSeconds - (float)(client.ServerNow - m.ServerTime));
            flight.To = _rails.StandBesideLane(anchor.GlobalPosition + lead * left);
        }
        hound.BeginNetLeap();
        _flights.Add(flight);
        if (NetSession.Instance.TestRide)
            GD.Print(FormattableString.Invariant($"[net] ride: hound {m.NetId} leaps from cart {m.FromCart} to {(flight.Miss ? "beside" : "cart")} {m.ToCart} spot {m.Spot}, {client.ServerNow - m.ServerTime:0.000} s into the arc"));
    }

    private Node3D? BodyById(int id) => CartById(id) as Node3D;

    /// <summary>The boarder spot <paramref name="spot"/> of the party's cart or a train wagon (its rim spot), in world space.</summary>
    private static Transform3D SpotOf(Node3D dest, int spot) => dest is MineCart cart ? cart.BoarderSpotTransform(spot) : ((TrainWagon)dest).BoarderSpotTransform(spot);

    private static Enemy?[]? HeldOf(Node3D body) => body is MineCart cart ? cart.Boarders : body is TrainWagon wagon ? wagon.Boarders : null;

    private static Enemy? HeldAt(Node3D body, int spot) => HeldOf(body) is { } held && spot >= 0 && spot < held.Length ? held[spot] : null;

    /// <summary>Pose each hound in the air on its arc at the server's time; at the end it lands on its spot (carried from then on) or on the ground (the snapshots move it).</summary>
    private void FlyLeaps(double serverNow)
    {
        for (var i = _flights.Count - 1; i >= 0; i--)
        {
            var f = _flights[i];
            var h = f.Hound;
            if (!IsInstanceValid(h) || h.IsQueuedForDeletion() || !h.Leaping) { _flights.RemoveAt(i); continue; }
            var t = Mathf.Clamp((float)((serverNow - f.Start) / RailEncounterController.LeapSeconds), 0f, 1f);
            if (f.HasLocal && f.Source != null && IsInstanceValid(f.Source)) f.FromWorld = f.Source.GlobalTransform * f.FromLocal;
            var landing = !f.Miss && f.Dest != null && IsInstanceValid(f.Dest);
            var to = landing ? SpotOf(f.Dest!, f.Spot).Origin : f.To;
            var pos = f.FromWorld.Lerp(to, t) + Vector3.Up * (RailEncounterController.LeapApex * 4f * t * (1f - t));
            var flat = new Vector3(to.X - f.FromWorld.X, 0f, to.Z - f.FromWorld.Z);
            var yaw = flat.LengthSquared() > 0.01f ? Mathf.Atan2(-flat.X, -flat.Z) : h.GlobalRotation.Y;
            h.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), pos);
            if (t < 1f && h.IsAlive) continue;
            _flights.RemoveAt(i);
            if (landing && h.IsAlive) { Land(h, f.Dest!, f.Spot); NoteLanding(h, f.Start + RailEncounterController.LeapSeconds); }
            else h.LandAt(landing ? pos : f.To);
            if (NetSession.Instance.TestRide)
                GD.Print(FormattableString.Invariant($"[net] ride: hound {h.NetId} {(landing && h.IsAlive ? $"boarded {f.Dest!.Name} spot {f.Spot}" : "landed beside the lane")} at ({h.GlobalPosition.X:0.00}, {h.GlobalPosition.Y:0.00}, {h.GlobalPosition.Z:0.00})"));
        }
    }

    /// <summary>Hounds that landed here lately and when (the server's time of the landing): a cart state older than that does not let them go.</summary>
    private readonly List<(Enemy Hound, double At)> _landed = new();

    private void NoteLanding(Enemy hound, double at)
    {
        for (var i = _landed.Count - 1; i >= 0; i--)
            if (_landed[i].Hound == hound || at - _landed[i].At > 2.0) _landed.RemoveAt(i);
        _landed.Add((hound, at));
    }

    /// <summary>The state was taken before <paramref name="hound"/> landed here (its boarder list could not name it yet).</summary>
    private bool LandedAfter(Enemy hound, double stateTime)
    {
        foreach (var (h, at) in _landed) if (h == hound && at > stateTime) return true;
        return false;
    }

    /// <summary>A hound rides <paramref name="dest"/> at boarder spot <paramref name="spot"/> here (its leap landed, or the boarder list says so).</summary>
    private static void Land(Enemy hound, Node3D dest, int spot)
    {
        if (HeldOf(dest) is not { } held || spot < 0 || spot >= held.Length) { if (hound.Leaping) hound.LandAt(hound.GlobalPosition); return; }
        if (held[spot] is { } other && other != hound && IsInstanceValid(other) && other.Carrier == dest) other.Drop(other.GlobalPosition);
        held[spot] = hound;
        Func<Transform3D> at = () => IsInstanceValid(dest) ? SpotOf(dest, spot) : new Transform3D(Basis.Identity, hound.GlobalPosition);
        if (hound.Leaping) hound.LandOn(dest, at);
        else hound.Carry(dest, at);
    }

    /// <summary>A hound leaves whichever boarder spot this client had it at (the party cart's or a wagon's).</summary>
    private void ForgetBoarder(Enemy hound)
    {
        Forget(_rails.Cart.Boarders, hound);
        var train = _rails.Train;
        for (var k = 0; k < train.Count; k++) if (IsInstanceValid(train[k])) Forget(train[k].Boarders, hound);
        static void Forget(Enemy?[] held, Enemy hound) { for (var i = 0; i < held.Length; i++) if (held[i] == hound) held[i] = null; }
    }

    private bool InFlight(Enemy hound)
    {
        foreach (var f in _flights) if (f.Hound == hound) return true;
        return false;
    }

    /// <summary>
    /// Client: the party cart's state lists its boarders by spot and each coupled wagon's: a hound named is carried at its spot (unless it is
    /// still flying there), and one carried here that is no longer named is let go where it stands (the snapshots move it). No allocation
    /// unless a boarder changes.
    /// </summary>
    private void ApplyBoarders(CartStateDto d)
    {
        if (!IsInstanceValid(_rails.Cart)) return;
        Reconcile(_rails.Cart, _rails.Cart.Boarders, d.Boarders, 0, d.ServerTime);
        var train = _rails.Train;
        for (var k = 0; k < train.Count; k++)
            if (IsInstanceValid(train[k])) Reconcile(train[k], train[k].Boarders, d.WagonBoarders, k, d.ServerTime);
    }

    /// <summary>Make <paramref name="held"/> (a body's boarder spots here) what <paramref name="wanted"/> says, from index <paramref name="from"/> on (a wagon: its one entry).</summary>
    private void Reconcile(Node3D body, Enemy?[] held, List<int>? wanted, int from, double stateTime)
    {
        var client = NetSession.Instance?.Client;
        for (var i = 0; i < held.Length; i++)
        {
            var at = from + i;
            var want = wanted != null && at < wanted.Count ? wanted[at] : -1;
            var have = held[i];
            if (have != null && (!IsInstanceValid(have) || have.IsQueuedForDeletion() || have.Carrier != body)) { held[i] = null; have = null; }
            if (have != null && have.NetId == want) continue;
            if (have != null && want < 0 && LandedAfter(have, stateTime)) continue; // our flight landed it before the server's did: the next state names it
            if (have != null) { held[i] = null; have.Drop(have.GlobalPosition); }
            if (want < 0 || client?.EnemyByNetId(want) is not { } hound || InFlight(hound)) continue;
            foreach (var cc in _carts.Values) // still bound to a slag cart here (its leap never reached this client): off it first
                if (cc.Slag is { } slag)
                    for (var s = 0; s < SlagCart.MaxRiders; s++)
                        if (slag.RiderAt(s) == hound) slag.Unbind(s);
            ForgetBoarder(hound);
            if (hound.Leaping) hound.LandAt(hound.GlobalPosition);
            Land(hound, body, i);
        }
    }

    /// <summary>Pose the boarders this client holds on the carts it runs (each rendered frame, after the carts).</summary>
    private void FollowBoarders()
    {
        if (!IsInstanceValid(_rails.Cart)) return;
        Follow(_rails.Cart, _rails.Cart.Boarders);
        var train = _rails.Train; // by index: a foreach over the interface would box its enumerator every frame
        for (var k = 0; k < train.Count; k++) if (IsInstanceValid(train[k])) Follow(train[k], train[k].Boarders);
        static void Follow(Node3D body, Enemy?[] held)
        {
            foreach (var h in held) if (h != null && IsInstanceValid(h) && h.Carrier == body) h.FollowCarrier();
        }
    }
}
