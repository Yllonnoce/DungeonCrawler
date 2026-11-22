using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 13's leap on ride 4 (a set piece "leap", Rulings 9-15 and 20): a human seated in the party's rolling cart, in the cavern's stretch
/// (the piece's At, <see cref="LeapFromS"/>, to the drop's near edge, <see cref="LeapDropS"/>), may leap across onto a slag cart of the ride
/// running alongside on its twin lane, and back. A leap is a seat change (<see cref="Adventurer.SitIn"/>), never a GetOut: onto one of the slag
/// cart's two deck seats (<see cref="SlagCart.DeckSeat"/>) and back into a free hero seat of the party's cart. Both are offered whatever the
/// aim (<see cref="LeapOfferFor"/>, every role from its own carts: "Leap across" on the slag cart, "Leap back" on the party's), and the server
/// takes a press with <see cref="LeapRules.AcceptSlack"/> more (<see cref="LeapAccepts"/>). A leaper is not aboard the party's cart (Ruling 14).
/// A slag cart that goes any way but the drop (freed, rammed) sets its leapers down first, quietly, on <see cref="CrevasseRules.NearestSetDown"/>
/// (<see cref="SetDownLeapers"/>). The piece is dead (a marker, logged <c>(dead: …)</c>) with no cavern or by <see cref="LeapRules.Dead"/>.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The live leap's stretch start (the piece's At) and the drop's near edge, party S; NaN with no live leap.</summary>
    public float LeapFromS { get; private set; } = float.NaN;
    public float LeapDropS { get; private set; } = float.NaN;
    /// <summary>The drop's slag twins on the enemy track: the near edge of the first and the far edge of the last (enemy S); NaN with no live leap.</summary>
    internal float LeapDropEnemyS { get; private set; } = float.NaN;
    internal float LeapDropFarEnemyS { get; private set; } = float.NaN;
    /// <summary>The slag line's going has been called this arming: no leap across is offered after it (Ruling 16; Task 5 calls it).</summary>
    public bool LeapCalled { get; private set; }
    /// <summary>Leaps across and back made on this floor build (tests read them).</summary>
    internal int Leaps { get; private set; }
    internal int LeapsBack { get; private set; }

    private Piece? _leapPiece;
    private readonly List<Adventurer> _leapersOff = new();

    /// <summary>
    /// The leap piece (every role): its stretch from the piece's At to the drop's near edge (<see cref="LeapRules.DropSpan"/>'s first party cell,
    /// a half cell back from its centre), the drop's slag twins on the enemy track (<see cref="LeapRules.SlagSide"/>), and whether it is live
    /// (<see cref="LeapRules.Dead"/>: flat, a drop, the stretch inside the cavern and 30 m or more). Always a marker at its At.
    /// </summary>
    private Node3D? BuildLeap(Node3D root, Piece piece)
    {
        var id = piece.Plan.Id;
        var track = _rails.Party.Track;
        var half = DungeonBuilder.CellSize / 2f;
        float drop = float.NaN, near = float.NaN, far = float.NaN;
        var over = false;
        string? dead;
        if (_layout.Rail is not { Cavern: { } cav } rail || !track.Valid || !_enemy.Valid) dead = "no cavern on this floor";
        else
        {
            var cavFrom = track.ClosestS(_builder.Surface(rail.Party[cav.From])) - half; // as the probe's cavern party S= and --print-map's cavern line
            var span = LeapRules.DropSpan(cav);
            var side = LeapRules.SlagSide(_layout, rail, cav);
            if (span is { } d && side is { } step)
            {
                drop = track.ClosestS(_builder.Surface(rail.Party[d.From])) - half;
                GridPos t0 = rail.Party[d.From] + step, t1 = rail.Party[d.To] + step;
                near = _enemy.ClosestS(_builder.Surface(t0)) - half;
                far = _enemy.ClosestS(_builder.Surface(t1)) + half;
                over = CrevasseRules.OverCrevasse(_layout, t0) && CrevasseRules.OverCrevasse(_layout, t1);
            }
            dead = LeapRules.Dead(piece.S, drop, cavFrom, LeapRules.Flat(_layout, cav), hasDrop: span != null && side != null);
        }
        GD.Print($"[rails] leap {id}: stretch party S={piece.S:0.0}..{drop:0.0}, the slag line's drop at enemy S={near:0.0}..{far:0.0} over the crevasse {over} ({(dead == null ? "live" : $"dead: {dead}")})");
        var marker = new Node3D { Name = $"Leap_{id}" };
        root.AddChild(marker);
        if (track.Valid) marker.GlobalPosition = track.Point(piece.S);
        if (dead != null) return marker;
        _leapPiece = piece;
        LeapFromS = piece.S;
        LeapDropS = drop;
        LeapDropEnemyS = near;
        LeapDropFarEnemyS = far;
        return marker;
    }

    // ---- the offer (every role) ------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="hero"/> is offered now, whatever the aim (Ruling 12), from this peer's own carts (a client's copies, through
    /// <see cref="SlagCart.Live"/>): seated in the party's cart, the nearest-along slag cart offered across; on a slag cart's deck, the party's cart
    /// offered back; else null.
    /// </summary>
    public IInteractable? LeapOfferFor(Adventurer hero)
    {
        if (_leapPiece == null || !Current || !IsInstanceValid(hero) || !IsInstanceValid(_rails.Cart)) return null;
        if (hero.Cart is SlagCart on) return IsInstanceValid(on) && BackOk(hero, on, 0f, accept: false) ? _rails.Cart : null;
        if (hero.Cart != _rails.Cart) return null;
        SlagCart? best = null;
        var bestAlong = float.PositiveInfinity;
        foreach (var slag in SlagCart.Live)
        {
            if (!AcrossCandidate(slag)) continue;
            var (lateral, along) = LeapGap(slag);
            if (!AcrossOk(hero, slag, lateral, along, 0f) || Mathf.Abs(along) >= bestAlong) continue;
            best = slag;
            bestAlong = Mathf.Abs(along);
        }
        return best;
    }

    /// <summary>
    /// The server (and single player) takes <paramref name="body"/>'s leap press on <paramref name="target"/> (Rulings 11-12): a slag cart offered
    /// across, or the party's cart offered back, each with <see cref="LeapRules.AcceptSlack"/> on the reach; the back to the drop's very edge
    /// (<see cref="LeapRules.BackAccepted"/>). False on a client.
    /// </summary>
    public bool LeapAccepts(Adventurer body, Node target)
    {
        if (NetSession.IsClient || _leapPiece == null || !Current || !IsInstanceValid(body)) return false;
        if (target is SlagCart slag)
        {
            if (!AcrossCandidate(slag)) return false;
            var (lateral, along) = LeapGap(slag);
            return AcrossOk(body, slag, lateral, along, LeapRules.AcceptSlack);
        }
        return target == _rails.Cart && body.Cart is SlagCart on && IsInstanceValid(on) && BackOk(body, on, LeapRules.AcceptSlack, accept: true);
    }

    /// <summary>
    /// <paramref name="other"/>'s centre in the party cart's frame, between the cart centres (Ruling 11): its lateral offset (+X) and along the
    /// party's line (+Z, back).
    /// </summary>
    internal (float Lateral, float Along) LeapGap(Node3D other)
    {
        var local = _rails.Cart.GlobalTransform.AffineInverse() * other.GlobalPosition;
        return (local.X, local.Z);
    }

    /// <summary>A slag cart that may be leapt onto: one of a ride's (not traffic), on its rails in this floor, not over a gone deck cell.</summary>
    private bool AcrossCandidate(SlagCart slag)
        => IsInstanceValid(slag) && !slag.IsQueuedForDeletion() && slag.IsInsideTree() && !slag.Derailed && slag.Kind == CartKind.Slag
           && slag.GetParent() == GetParent() && !OverGone(slag);

    private bool AcrossOk(Adventurer hero, SlagCart slag, float lateral, float along, float slack)
        => LeapRules.AcrossOffered(human: hero.Control is ControlKind.LocalHuman or ControlKind.Remote, up: !hero.Downed && !hero.Parked,
            seatedInParty: hero.Cart == _rails.Cart, rolling: _rails.State.Mode == CartMode.Rolling, stretchLive: StretchLive, called: LeapCalled,
            inReach: LeapRules.InReach(lateral, along, slack), leapersAboard: slag.Leapers);

    private bool BackOk(Adventurer hero, SlagCart on, float slack, bool accept)
    {
        var (lateral, along) = LeapGap(on);
        var inReach = LeapRules.InReach(lateral, along, slack);
        var seatFree = PartyHeroSeatFree() && _rails.State.Mode != CartMode.Wrecked && !OverGone(_rails.Cart); // never into a wreck or over a gone cell
        var up = !hero.Downed && !hero.Parked;
        var front = on.Run.FrontS;
        return accept ? LeapRules.BackAccepted(up, true, inReach, seatFree, front, LeapDropEnemyS) : LeapRules.BackOffered(up, true, inReach, seatFree, front, LeapDropEnemyS);
    }

    /// <summary>The stretch is live: the leap's ride armed and the party's cart between its start and the drop's near edge.</summary>
    private bool StretchLive => _leapPiece is { } p && ArmedRide == p.Plan.Ride && _rails.State.S >= LeapFromS && _rails.State.S < LeapDropS;

    /// <summary>A body over a deck cell that has given way (<see cref="FloorLayout.GoneBridges"/>): a leap never puts a rider there.</summary>
    private bool OverGone(Node3D body) => _layout.GoneBridges.Count > 0 && _layout.GoneBridges.Contains(DungeonBuilder.WorldToCell(body.GlobalPosition));

    /// <summary>A hero seat (0-3) of the party cart's book is free. No allocation.</summary>
    private bool PartyHeroSeatFree()
    {
        for (var i = 0; i < CartSeats.HeroSeats; i++) if (_rails.Cart.Seats.OccupantOf(i) == null) return true;
        return false;
    }

    // ---- the leaps (server and single player) ----------------------------------------------------

    /// <summary>
    /// "Leap across" (server and solo; taken by <see cref="LeapAccepts"/>): the first free deck seat of <paramref name="cart"/> is booked and
    /// <paramref name="hero"/> sits in it (leaving the party's cart), with the jump's and the landing's cues and a small shake. True when it leapt.
    /// </summary>
    public bool LeapAcross(Adventurer hero, SlagCart cart)
    {
        if (NetSession.IsClient || !LeapAccepts(hero, cart)) return false;
        var seat = -1;
        for (var k = 0; k < LeapRules.MaxLeapers; k++)
            if (cart.Seats.OccupantOf(k) == null) { seat = k; break; }
        if (seat < 0 || !cart.Seats.Put(hero.RiderId, seat)) return false;
        var (lateral, along) = LeapGap(cart);
        var from = hero.GlobalPosition;
        hero.SitIn(cart, seat);
        Leaps++;
        EventBus.Instance.Log($"{hero.State.Name} leaps onto the slag cart!");
        LeapCue(hero, from, hero.GlobalPosition, shake: true);
        GD.Print($"[rails] leap: {hero.State.Name} across onto slag cart {cart.CartId} ({cart.EncounterId}) seat {seat} at party S={_rails.State.S:0.0} (gap {Mathf.Abs(lateral) - 2f * CartRules.HalfWidth:0.00} m, along {along:0.00} m)");
        return true;
    }

    /// <summary>
    /// "Leap back" (server and solo; taken by <see cref="LeapAccepts"/>): <paramref name="hero"/>, on a slag cart's deck, takes a free hero seat of
    /// the party's cart. <paramref name="hauledBy"/> (Task 5's haul at the drop, Ruling 17): not judged as a press, only a seat is needed; a
    /// companion's name says who hauls them, "" nobody. True when they are back.
    /// </summary>
    public bool LeapBack(Adventurer hero, string? hauledBy = null)
    {
        if (NetSession.IsClient || !Current || !IsInstanceValid(hero) || hero.Cart is not SlagCart from || !IsInstanceValid(_rails.Cart)) return false;
        if (hauledBy == null ? !LeapAccepts(hero, _rails.Cart) : _rails.State.Mode == CartMode.Wrecked) return false;
        if (_rails.Cart.Seats.Take(hero.RiderId, isHero: true) is not int seat) return false;
        var at = hero.GlobalPosition;
        hero.SitIn(_rails.Cart, seat); // leaves the slag cart's deck (its book frees the seat)
        LeapsBack++;
        var name = hero.State.Name;
        EventBus.Instance.Log(hauledBy == null ? $"{name} leaps back into the cart!" : hauledBy.Length > 0 ? $"{hauledBy} hauls {name} back across!" : $"{name} is hauled back across.");
        LeapCue(hero, at, hero.GlobalPosition, shake: true);
        GD.Print($"[rails] leap: {name} back into the cart seat {seat} at party S={_rails.State.S:0.0} from slag cart {from.CartId} (its front at enemy S={from.Run.FrontS:0.0}, the drop's edge at {LeapDropEnemyS:0.0}){(hauledBy == null ? "" : $", hauled by {(hauledBy.Length > 0 ? hauledBy : "nobody")}")}");
        return true;
    }

    /// <summary>A leap's cues, at once: the jump where the body left and the landing where it sits now; a small shake (none in a headset here; a remote player's screen is told).</summary>
    internal static void LeapCue(Adventurer hero, Vector3 from, Vector3 to, bool shake)
    {
        RideAudio.At("cart_jump", from);
        RideAudio.At("cart_land", to);
        if (shake && (hero.Control == ControlKind.Remote || !Vr.Active)) hero.Shake(0.05f, 0.3f);
    }

    /// <summary>
    /// <paramref name="cart"/> goes any way but the drop (Ruling 15: freed or rammed): each hero on its deck is set down first, quietly (as
    /// <see cref="RailController.UnseatQuietly"/>: no throw, no run-over, no log), on <see cref="CrevasseRules.NearestSetDown"/>'s cell, never into a drop.
    /// </summary>
    private void SetDownLeapers(SlagCart cart, string why)
    {
        if (cart.Riders.Count == 0) return;
        _leapersOff.Clear();
        foreach (var rider in cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) _leapersOff.Add(hero);
        foreach (var hero in _leapersOff) SetDown(hero, cart, why);
        _leapersOff.Clear();
    }

    /// <summary>Server: a leaper's player leaves the game (before the body is parked): set down off the deck as <see cref="SetDownLeapers"/>.</summary>
    internal void SetDownLeaper(Adventurer hero, string why)
    {
        if (NetSession.IsClient || !IsInstanceValid(hero) || hero.Cart is not SlagCart cart || !IsInstanceValid(cart)) return;
        SetDown(hero, cart, why);
    }

    private void SetDown(Adventurer hero, SlagCart cart, string why)
    {
        var cell = CrevasseRules.NearestSetDown(_layout, DungeonBuilder.WorldToCell(hero.GlobalPosition));
        var at = cell is { } c ? _builder.Surface(c) : _rails.StandBesideLane(hero.GlobalPosition);
        hero.GetOut(at, Vector3.Zero); // the slag cart's book frees the seat and tells it (an unseat SeatMsg with the landing)
        GD.Print($"[rails] leap: {hero.State.Name} set down off slag cart {cart.CartId} ({why}) at cell {(cell is { } k ? k.ToString() : $"{DungeonBuilder.WorldToCell(at)} (beside the lane)")}");
    }
}
