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
            into.Add(new RailNet.LiveCart(g.CartId, CartKind.Golem, g.OnSpur ? SpurTrackNumber(g.Spur) : (byte)1, g.Run, g.Runner, 0f, g.EncounterId,
                null, IsInstanceValid(g.Body) && g.Body.Carried ? g.Body : null));
    }
}
