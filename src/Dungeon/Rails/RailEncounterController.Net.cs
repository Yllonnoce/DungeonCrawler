using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The ride's carts on the wire: the server numbers each slag, traffic, runaway and golem cart as it spawns (<see cref="CartIds"/>,
/// from 1 per floor build) and lists the live ones for <see cref="RailNet"/>, which sends what changed (a spawn, a spur taken, the
/// riders, a derail, a free) and corrects every live one five times a second. A client's copies are RailNet's, not this controller's.
/// </summary>
public partial class RailEncounterController
{
    private int _nextCartId = CartIds.FirstDynamic;

    /// <summary>The next cart id of this floor build (server).</summary>
    private int NextCartId() => _nextCartId < CartIds.WagonBase - 1 ? _nextCartId++ : _nextCartId;

    /// <summary>The k-th spur's track (the spurs in set-piece order, the same on every peer), or null.</summary>
    internal TrackSampler? SpurTrack(int k)
    {
        if (k < 0) return null;
        foreach (var p in _pieces)
            if (p.Spur != null && k-- == 0) return p.Spur.Sampler;
        return null;
    }

    /// <summary>The wire's track number of the spur <paramref name="spur"/> (2+k), or 1 (the enemy line) when it is none of this floor's.</summary>
    private byte SpurTrackNumber(SpurSwitch? spur)
    {
        var k = 0;
        foreach (var p in _pieces)
        {
            if (p.Spur == null) continue;
            if (p.Spur == spur) return (byte)(2 + k);
            k++;
        }
        return 1;
    }

    // ---- a client's copies of the set pieces: the server's ride, shown from this screen's own cart ---------------

    /// <summary>
    /// Client: the party cart's state named the armed ride and the server's clock when it armed. A new arming (another ride, or the same
    /// ride armed again after a wreck or a load) starts its pieces as the server's arming does: the beam may duck again and the buckets
    /// start their round; the other rides' buckets hang still and their lanterns stop. The ride's encounter lines are prefetched here, as
    /// the server's arming prefetches them there.
    /// </summary>
    internal void ApplyArmNet(int ride, double armedAt)
    {
        if (!NetSession.IsClient) return;
        ride = ride >= 1 && ride < _rails.StopS.Length ? ride : -1;
        if (ride == ArmedRide && (ride < 1 || armedAt == ArmedAt)) return;
        ArmedRide = ride;
        ArmedAt = armedAt;
        if (ride >= 1) WarmLines(ride); // this screen plays the lines: read on a loader thread before the cart comes up to them
        var front = _rails.Party.FrontS;
        foreach (var p in _pieces)
        {
            var armed = ride >= 1 && p.Plan.Ride == ride;
            if (armed) p.Fired = p.Plan.Kind switch // a joiner mid-ride past the beam does not duck for it, nor hear a trestle already behind its front creak
            {
                "beam" => front > p.S + 0.5f,
                "trestle" => p.Trestle is { } t && front >= t.From,
                _ => false,
            };
            if (p.Buckets is { } buckets) { if (armed) buckets.Arm(); else buckets.Clear(); }
            if (!armed && p.Heap is { } heap) heap.Alarm = false;
            if (!armed && p.Barricade is { } barricade) barricade.Alarm = false;
        }
    }

    /// <summary>
    /// Client, each rendered frame (after the cart is posed): the armed ride's presentation under the server's own rules, from this screen's
    /// cart (the follower's S, speed, lane and mode): the beam ducks our seated desktop camera as the front passes it, the heap's and the barricades' lanterns
    /// blink while the cart bears down on them (no bark: a companion's call comes from the server), the buckets run the server's schedule
    /// from its arm time, and the trestle groans as the front comes onto it. Nothing is judged here. No allocation.
    /// </summary>
    private void ClientPresent(float dt)
    {
        if (ArmedRide < 1 || _pieces.Count == 0 || NetSession.Instance?.Client is not { } client) return;
        var rolling = _rails.State.Mode == CartMode.Rolling;
        var front = _rails.Party.FrontS;
        foreach (var piece in _pieces)
        {
            if (piece.Plan.Ride != ArmedRide) continue;
            switch (piece.Plan.Kind)
            {
                case "beam":
                    if (SetPieceRules.BeamPasses(rolling, piece.Fired, front, piece.S)) { piece.Fired = true; DuckHere(piece, front); }
                    break;
                case "heap":
                    if (piece.Heap is { IsAlive: true } heap)
                        SetAlarm(heap, SetPieceRules.HeapAlarm(SetPieceRules.HeapHeaded(rolling, heap.FaceS, front, _rails.OnEnemyLane(heap.FaceS), SwitchSetBefore(piece)), heap.FaceS, front, _rails.State.V));
                    break;
                case "barricade":
                case "slag_barricade":
                    if (piece.Barricade is { IsStanding: true } barricade)
                    {
                        var (_, at, onItsLane) = PartyFrontAt(barricade); // the server's reading, from our own cart
                        SetBarricadeAlarm(barricade, SetPieceRules.HeapAlarm(SetPieceRules.HeapHeaded(rolling, barricade.FaceS, at, !onItsLane, false), barricade.FaceS, at, _rails.State.V));
                    }
                    break;
                case "buckets":
                    piece.Buckets?.ClientTick(client.ServerNow - ArmedAt, dt);
                    break;
                case "trestle":
                    // the server's rule from our own cart: it groans once as the front first comes onto it, while it stands
                    if (rolling && !piece.Fired && piece.Trestle is { Collapsed: false } trestle && front >= trestle.From) { piece.Fired = true; RideAudio.At("trestle_creak", trestle.Entry); }
                    break;
            }
        }
    }

    /// <summary>Client: our cart's front passed the beam: our hero, seated in it at a desktop, ducks (never in a headset).</summary>
    private void DuckHere(Piece piece, float front)
    {
        BeamPasses++;
        var me = Core.Main.Instance?.Party?.Local;
        var ducks = me != null && IsInstanceValid(me) && me.Seated && me.Cart == _rails.Cart && !Core.Vr.Active;
        if (ducks) me!.Shake(DuckAmplitude, DuckSeconds);
        if (NetSession.Instance.TestRide) GD.Print(FormattableString.Invariant($"[net] ride: the beam {piece.Plan.Id} passed here at front S={front:0.0} (beam S={piece.S:0.0}); our seated camera {(ducks ? "ducks" : "does not duck")}"));
    }

    /// <summary>Which side of the enemy track at <paramref name="enemyS"/> the party's lane lies on (a client's slag cart puts its shoe there too).</summary>
    internal int InnerSideFor(float enemyS) => InnerSideAt(enemyS);

    /// <summary>The live carts besides the party's, for the cart sender (server; <paramref name="into"/> is cleared first, nothing else allocated).</summary>
    internal void CollectNetCarts(List<RailNet.LiveCart> into)
    {
        into.Clear();
        foreach (var cart in _carts)
            if (IsInstanceValid(cart) && !cart.Derailed && !cart.IsQueuedForDeletion())
                into.Add(new RailNet.LiveCart(cart.CartId, cart.Kind, 1, cart.Run, cart, cart.Offset, cart.EncounterId, cart, null));
        if (TrafficCart is { } traffic && !traffic.IsQueuedForDeletion())
            into.Add(new RailNet.LiveCart(traffic.CartId, traffic.Kind, 1, traffic.Run, traffic, 0f, traffic.EncounterId, traffic, null));
        if (_runaway is { } w && IsInstanceValid(w.Body) && !w.Body.IsQueuedForDeletion())
            into.Add(new RailNet.LiveCart(w.Body.CartId, CartKind.Runaway, 0, w.Run, w.Body, 0f, null, null, null));
        if (_golem is { Done: false } g && IsInstanceValid(g.Runner) && !g.Runner.IsQueuedForDeletion())
            into.Add(new RailNet.LiveCart(g.CartId, CartKind.Golem, GolemTrack(g), g.Run, g.Runner, 0f, g.EncounterId,
                null, IsInstanceValid(g.Body) && g.Body.Carried ? g.Body : null));
    }
}
