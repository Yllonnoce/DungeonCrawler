using DungeonCrawler.Adventure;
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
/// <para>The drop (Task 5, Rulings 16-18): live, the slag track is cut bare over the drop's twins and a <see cref="SlagDrop"/> lays their rails.
/// With the party's front <see cref="LeapRules.CallAhead"/> short of the drop (<see cref="TickHeroLeap"/>, once per arming) its cells give way,
/// the ride's slag carts ring their bells and a seated companion calls it; no leap across is offered after it (<see cref="LeapCalled"/>, a
/// client's from the drop's tell). A slag cart whose front reaches the drop goes over (<see cref="DropsOverTheLeap"/>): its creatures and any
/// able human still on its deck fall (<see cref="LeapRules.FallsWithIt"/>), any other hero is hauled back into the party's cart. The drop stands
/// again at ride 4's arming, a restore and a spare's takeover.</para>
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The live leap's stretch start (the piece's At) and the drop's near edge, party S; NaN with no live leap.</summary>
    public float LeapFromS { get; private set; } = float.NaN;
    public float LeapDropS { get; private set; } = float.NaN;
    /// <summary>The drop's slag twins on the enemy track: the near edge of the first and the far edge of the last (enemy S); NaN with no live leap.</summary>
    internal float LeapDropEnemyS { get; private set; } = float.NaN;
    internal float LeapDropFarEnemyS { get; private set; } = float.NaN;
    /// <summary>
    /// The slag line's going has been called this arming: no leap across is offered after it (Ruling 16). The server's and single player's
    /// is set at the call (<see cref="TickHeroLeap"/>); a client's is its drop's state, told with the drop (<see cref="ApplyDropTold"/>, the props' catch-up).
    /// </summary>
    public bool LeapCalled { get; private set; }
    /// <summary>Leaps across and back made on this floor build (tests read them).</summary>
    internal int Leaps { get; private set; }
    internal int LeapsBack { get; private set; }
    /// <summary>Slag carts gone over the drop, heroes who fell with one, and heroes hauled back at it, on this floor build (tests read them).</summary>
    internal int LeapDrops { get; private set; }
    internal int LeapDropFalls { get; private set; }
    internal int LeapHauls { get; private set; }
    /// <summary>The live leap's slag-line drop, or null (tests read it).</summary>
    internal SlagDrop? Drop => _leapPiece?.Drop is { } d && IsInstanceValid(d) ? d : null;

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
        var twins = new List<GridPos>();
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
                for (var i = d.From; i <= d.To; i++) twins.Add(rail.Party[i] + step);
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
        _enemy.Track.CutMore(new[] { (near, far) }); // the piece's one cut: the slag line laid again once, bare over the drop (the ride-3 gap kept)
        var slagDrop = new SlagDrop { Name = $"SlagDrop_{id}", NetRef = NetRefs.Leap(piece.Index), Layout = _layout, Twins = twins };
        root.AddChild(slagDrop); // in the tree first: LaySpan places in its frame
        slagDrop.Build(_enemy, near, far, InnerSideAt(near));
        piece.Drop = slagDrop;
        return marker;
    }

    // ---- the drop (server and single player; a client shows what is told) -------------------------------------------

    /// <summary>
    /// Server and single player, each tick while the leap's ride is armed and the drop not yet called: with the party's front
    /// <see cref="LeapRules.CallAhead"/> short of the drop (<see cref="LeapRules.Calls"/>), once per arming, the drop's cells give way, no leap
    /// across is offered after it, each live slag cart of the ride rings its bell and a seated companion calls it (Ruling 16).
    /// </summary>
    private void TickHeroLeap(Piece piece, PartyController party)
    {
        if (NetSession.IsClient || piece != _leapPiece || LeapCalled || ArmedRide != piece.Plan.Ride || piece.Drop is not { } drop || !IsInstanceValid(drop)) return;
        if (_rails.State.Mode == CartMode.Wrecked || !LeapRules.Calls(_rails.Party.FrontS, LeapDropS)) return;
        LeapCalled = true;
        drop.GiveWay(GetParent() as Node3D);
        var aboard = 0;
        foreach (var cart in _carts)
        {
            if (!IsInstanceValid(cart) || cart.Derailed || cart.IsQueuedForDeletion()) continue;
            if (cart.Sound is { } bell && IsInstanceValid(bell)) bell.RingNow(); // cart_bell
            aboard += cart.Leapers;
        }
        var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.SlagDrop, "The slag line's going — leap back!");
        GD.Print($"[rails] t={_now:0.00} leap {piece.Plan.Id}: the slag line drops at enemy S={LeapDropEnemyS:0.0}..{LeapDropFarEnemyS:0.0}; called by {by ?? "none (logged)"}; {aboard} hero(es) on a slag deck");
    }

    /// <summary>
    /// A slag (or traffic) cart after its step, with the drop called: its front on the drop (at or past its near edge, its back short of the far
    /// edge) goes over (Rulings 16-17). Each hero on its deck: an able human (<see cref="LeapRules.FallsWithIt"/>) falls with it
    /// (<see cref="CrevasseWatch.Take"/>), any other is hauled back into a free hero seat of the party's cart by the first seated companion
    /// (<see cref="LeapBack"/>; with no seat, set down); each creature aboard falls; then it plunges quietly (<see cref="SlagCart.PlungeThrough"/>:
    /// gone with <c>Derailed</c> on the wire, read as the plunge by every client). True when it went: the caller forgets it.
    /// </summary>
    private bool DropsOverTheLeap(SlagCart cart)
    {
        if (!LeapCalled || float.IsNaN(LeapDropEnemyS) || !IsInstanceValid(cart) || cart.Derailed || cart.IsQueuedForDeletion()) return false;
        var run = cart.Run;
        if (run.Dir < 0 || run.FrontS < LeapDropEnemyS || run.BackS >= LeapDropFarEnemyS) return false; // on the drop: never one wholly past it
        var watch = CrevasseWatch.Instance is { } w && IsInstanceValid(w) ? w : null;
        var over = _enemy.Point((LeapDropEnemyS + LeapDropFarEnemyS) / 2f); // they go down into the drop itself (the cart's middle is still over the deck short of it)
        int fell = 0, hauled = 0, riders = 0;
        if (cart.Riders.Count > 0)
        {
            string? hauler = null;
            foreach (var mate in Main.Instance.Party.Adventurers)
                if (IsInstanceValid(mate) && mate.Cart == _rails.Cart && mate.Control == ControlKind.Companion && !mate.Downed && !mate.Parked) { hauler = mate.State.Name; break; }
            _leapersOff.Clear();
            foreach (var rider in cart.Riders.Values)
                if (rider is Adventurer hero && IsInstanceValid(hero)) _leapersOff.Add(hero);
            foreach (var hero in _leapersOff)
            {
                var human = hero.Control is ControlKind.LocalHuman or ControlKind.Remote;
                var up = !hero.Downed && !hero.Parked;
                if (LeapRules.FallsWithIt(human, up) && watch != null)
                {
                    GD.Print($"[rails] leap: {hero.State.Name} falls with slag cart {cart.CartId} (human, up)");
                    watch.Take(hero, over); // out of the deck seat, into the crevasse: back when the party next reaches a stop
                    fell++;
                    continue;
                }
                if (LeapBack(hero, hauledBy: hauler ?? "")) { hauled++; continue; } // logged per Ruling 17: "{companion} hauls {name} back across!"
                SetDown(hero, cart, "no seat to haul them into"); // never stranded on a cart going over
            }
            _leapersOff.Clear();
        }
        foreach (var rider in cart.Aboard)
        {
            if (!IsInstanceValid(rider) || rider.IsQueuedForDeletion() || !rider.IsAlive) continue;
            if (watch != null) watch.Take(rider, over);
            else rider.KillByEnvironment("The crevasse", HazardDamage.Fall);
            riders++;
        }
        var ride = cart.Kind == CartKind.Slag;
        var told = ride || SwitchBackRules.PlungeTold(NearestHeroTo(over)); // a ride's goes over beside the party; traffic is told only near
        if (told) EventBus.Instance.Log("The slag cart goes over into the crevasse!");
        GD.Print($"[rails] t={_now:0.00} slag cart {cart.CartId} ({cart.EncounterId}) goes over the drop at enemy S={run.FrontS:0.0}: {riders} rider(s), {fell} hero(es) fell, {hauled} hauled back");
        LeapDrops++;
        LeapDropFalls += fell;
        LeapHauls += hauled;
        cart.PlungeThrough(UnderTheDrop());
        if (IsInstanceValid(cart) && !cart.IsQueuedForDeletion()) cart.QueueFree(); // a plunge refused (it was not in the tree): freed all the same
        return true;
    }

    /// <summary>Where the drop's dead lie: the middle of the drop, <see cref="CrevasseRules.HoldDepth"/> below the deck, out of sight (every role reads the same).</summary>
    internal Vector3 UnderTheDrop()
    {
        var cell = DungeonBuilder.WorldToCell(_enemy.Point((LeapDropEnemyS + LeapDropFarEnemyS) / 2f));
        return _builder.Surface(cell) + Vector3.Down * CrevasseRules.HoldDepth;
    }

    /// <summary>Client: a slag or traffic cart the server took off the line at enemy S <paramref name="cartS"/> went over the slag line's drop (<see cref="SwitchBackRules.PlungedAt"/> on its near edge).</summary>
    internal bool DropPlungedAt(float cartS) => !float.IsNaN(LeapDropEnemyS) && SwitchBackRules.PlungedAt(cartS, LeapDropEnemyS);

    /// <summary>Ride <paramref name="ride"/> arms: its drop stands again and may be called again (Ruling 18).</summary>
    private void ArmLeap(int ride)
    {
        if (_leapPiece is { } p && p.Plan.Ride == ride) StandDrop(p, "the ride arms");
    }

    /// <summary>The party cart's Wrecked event (a fall too): with a spare already taken over, the drop stands again; a plain wreck leaves it as it is.</summary>
    private void LeapOnWreck()
    {
        if (_leapPiece is { } p && _rails.State.Mode != CartMode.Wrecked) StandDrop(p, "a spare takes over");
    }

    /// <summary>The cart is restored (a wipe's rise or a load): the drop stands again.</summary>
    private void LeapOnRestore()
    {
        if (_leapPiece is { } p) StandDrop(p, "the cart is restored");
    }

    /// <summary>The drop stands again and its call is undone (server and single player).</summary>
    private void StandDrop(Piece p, string why)
    {
        LeapCalled = false;
        if (p.Drop is not { } drop || !IsInstanceValid(drop)) return;
        var was = drop.Dropped;
        drop.Stand();
        if (was) GD.Print($"[rails] t={_now:0.00} leap {p.Plan.Id}: the slag line's drop stands again ({why})");
    }

    /// <summary>
    /// Client: the server told the drop <paramref name="drop"/> "drops" (its cells given way, the timbers tumbling under <paramref name="root"/>,
    /// this screen's slag copies ringing their bells) or "stands". <see cref="LeapCalled"/> follows it: no "Leap across" after the call. True when it was one of those.
    /// </summary>
    internal bool ApplyDropTold(SlagDrop drop, string what, Node3D root)
    {
        if (!NetSession.IsClient || !IsInstanceValid(drop)) return false;
        var was = drop.Dropped;
        if (!drop.ShowTold(what, root)) return false;
        LeapCalled = drop.Dropped;
        if (!was && drop.Dropped)
            foreach (var slag in SlagCart.Live)
                if (IsInstanceValid(slag) && !slag.Derailed && slag.Kind == CartKind.Slag && slag.GetParent() == root && slag.Sound is { } bell && IsInstanceValid(bell)) bell.RingNow();
        return true;
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
    internal void SetDownLeapers(SlagCart cart, string why)
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
