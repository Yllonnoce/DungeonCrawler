using DungeonCrawler.Core;
using Godot;
using Rails = DungeonCrawler.Dungeon.Rails;

namespace DungeonCrawler.Net;

/// <summary>
/// The RIDE pass's exit island checks (plan 15 Task 8): the first client's Get out refused over the exit bridge, the parked train's smash seen
/// on both clients with the server's plating and its PropEvts traced on the server, the lift boarded by a tub, and a hero seated for the
/// end-of-line wreck and set down inside. The server's and the clients' halves are called from the ride loops in <c>NetTestFlow.cs</c>.
/// </summary>
public partial class NetTestFlow
{
    // ---- the server: the parked train's PropEvts traced, the smash told with its time and plating ------------------------------------------

    private bool _parkedHooked;
    private ServerWorld? _parkedWorld;
    /// <summary>The parked train's wire name as its first PropEvt named it (parked:{piece}), or null before one.</summary>
    private string? _parkedRef;
    /// <summary>The server's clock (<see cref="_elapsed"/>) at the parked train's "smashed" PropEvt, or −1: the plating top-up waits 2 s after it.</summary>
    private double _parkedSmashAt = -1;

    /// <summary>Server, each RIDE tick: the bus's prop changes heard once (the parked train's are traced and told).</summary>
    private void RideParkedServerHook(ServerWorld world)
    {
        if (_parkedHooked || EventBus.Instance is not { } bus) return;
        _parkedHooked = true;
        _parkedWorld = world;
        bus.PropChanged += OnParkedProp;
    }

    /// <summary>
    /// Server: a PropEvt of the parked train ("smashed", "standing", "gone", "inway") is traced prop:parked:{piece}:{what} and told with the
    /// server's time and the party cart's plating now (after the smash's dent: the PropEvt is raised once it is paid).
    /// </summary>
    private void OnParkedProp(string netRef, string what)
    {
        if (_parkedWorld is not { } world || !netRef.StartsWith(NetRefs.ParkedKind + ":", StringComparison.Ordinal)) return;
        _parkedRef ??= netRef;
        world.Trace.Add($"prop:{netRef}:{what}");
        if (what == "smashed" && _parkedSmashAt < 0) _parkedSmashAt = _elapsed;
        var plating = Rails.RailController.Instance is { } rc && IsInstanceValid(rc) ? rc.State.Plating : float.NaN;
        GD.Print(FormattableString.Invariant($"[net-server] ride: parked: {what} (PropEvt {netRef}) at t={NetSession.Instance.Now:0.000}, plating {plating:0}"));
    }

    /// <summary>The plating top-up holds off for 2 s after the parked train's smash, so each client's first cart state after it carries the smash's plating.</summary>
    private bool ParkedSmashFresh => _parkedSmashAt >= 0 && _elapsed - _parkedSmashAt < 2.0;

    /// <summary>
    /// The parked train's PropEvts in ride order (Task 8): the joiner's chase taken, smashed by the train, the chase done, standing as a lift, the
    /// lift boarded, gone as it departs, then the wreck at the end of the line. The missing ones, and whether the rest came in that order.
    /// </summary>
    private (List<string> Missing, bool Ordered) ParkedTraceOrder(ServerWorld world)
    {
        var pk = _parkedRef ?? NetRefs.ParkedKind + ":?";
        var want = new[] { "chase:", $"prop:{pk}:smashed", "chase-done", $"prop:{pk}:standing", "lift-boarded", $"prop:{pk}:gone", "wreck-inside" };
        var missing = want.Where(w => !world.Trace.Any(t => t.StartsWith(w, StringComparison.Ordinal))).ToList();
        var ordered = true;
        var last = -1;
        foreach (var w in want)
        {
            var i = world.Trace.FindIndex(t => t.StartsWith(w, StringComparison.Ordinal));
            if (i < 0) continue;
            if (i < last) ordered = false;
            last = Math.Max(last, i);
        }
        return (missing, ordered);
    }

    // ---- the server: a hero seated for the end-of-line wreck (Task 4's carry) ----------------------------------------------------------

    private bool _wreckSeated;
    private double _wreckSeatedAt;

    /// <summary>
    /// Before the end-of-line wreck: the joiner, set down inside by the lift, is seated in the party's cart held at the end of the line. A player
    /// cannot board there (<c>RailController.UseCart</c>: "the end of the line: nowhere left to ride", and its prompt offers nothing), so the test
    /// seats it by the hook (<c>RailController.DebugSeat</c>), told to every client by its SeatMsg. True once 2 s have passed since (the joiner's
    /// screen sees itself seated first); the wreck then has a hero to set down inside.
    /// </summary>
    private bool RideSeatForWreck(ServerWorld world, Rails.RailController rc)
    {
        if (_wreckSeated) return _elapsed - _wreckSeatedAt >= 2.0;
        if (Main.Instance?.Party is not { } party || world.Registry.Online.FirstOrDefault(sl => sl.BotOwner == null && sl.Name == RideJoinerName) is not { } js || js.MemberIndex >= party.Adventurers.Length) return false;
        var hero = party.Adventurers[js.MemberIndex];
        var offered = rc.Cart.PromptFor?.Invoke(hero) ?? "";
        var ok = !hero.Seated && rc.DebugSeat(hero, 0) && hero.Cart == rc.Cart;
        _wreckSeated = true;
        _wreckSeatedAt = _elapsed;
        GD.Print($"[net-server] ride: {RideJoinerName} is seated in the party's cart at stop {rc.StopIndex} by the test's hook (the end of the line takes no boarding; its cart offers \"{offered}\" to {RideJoinerName}): {(ok ? $"seat {hero.Seat}" : "NOT SEATED")}, for the wreck at the end of the line");
        return false;
    }

    // ---- the first client: no Get out over the exit bridge (B.10) -----------------------------------------------------------------------

    private int _exitFrames;
    private bool _exitPressed, _exitTold;
    /// <summary>When this screen first held over the exit bridge (<see cref="_elapsed"/>), or −1.</summary>
    private double _exitHeldAt = -1;
    /// <summary>Fix round 1 (I1): the press waits until our front is this far into the bridge's span and the hold this old here, so the server holds too.</summary>
    private const float ExitPressInto = 2f;
    private const double ExitPressAfter = 0.5;

    /// <summary>
    /// Step 53, each tick: while the train has a car over the exit bridge here (<c>RailEncounterController.ExitBridgeHolds</c>, this screen's own
    /// train) the cart's own prompt must read "" on every frame; once our front is <see cref="ExitPressInto"/> into the span and the hold is
    /// <see cref="ExitPressAfter"/> old here (fix round 1, I1: never at the hold's edge, where the server may not hold yet) our seat presses Get out
    /// through the intent anyway (cart:0), which the server refuses ("get out refused (the train is on the exit bridge)"). Once the hold ends here we are still seated.
    /// True when it failed.
    /// </summary>
    private bool RideExitBridge(NetSession net, Rails.RailController rc, Party.Adventurer me)
    {
        if (_exitTold || Rails.RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc)) return false;
        if (enc.ExitBridgeHolds())
        {
            _exitFrames++;
            if (_exitHeldAt < 0) _exitHeldAt = _elapsed;
            var prompt = rc.Cart.Prompt;
            if (prompt != "") return RideFail(net, FormattableString.Invariant($"over the exit bridge (front S={rc.Party.FrontS:0.0}) the cart offers \"{prompt}\" here, frame {_exitFrames} (want nothing)"));
            if (_exitPressed || rc.Party.FrontS < enc.ExitFromS + ExitPressInto || _elapsed - _exitHeldAt < ExitPressAfter) return false;
            _exitPressed = true;
            if (NetRefResolver.RefOf(rc.Cart) is not { } cartRef) return RideFail(net, "the party's cart has no net ref here");
            net.Send(new InteractIntent(cartRef));
            GD.Print(FormattableString.Invariant($"[net] ride: over the exit bridge here (front S={rc.Party.FrontS:0.0}, the bridge S={enc.ExitFromS:0.0}..{enc.ExitToS:0.0}): the cart offers \"\"; pressed Get out through the intent anyway ({cartRef}) from seat {me.Seat}, {rc.Party.FrontS - enc.ExitFromS:0.0} m into the span and {_elapsed - _exitHeldAt:0.00} s into the hold here"));
            return false;
        }
        if (_exitFrames == 0) return false;
        _exitTold = true;
        if (!_exitPressed) return RideFail(net, $"the hold over the exit bridge ended here after {_exitFrames} frame(s) before our front was {ExitPressInto:0} m into it and the hold {ExitPressAfter:0.0} s old: no Get out was pressed");
        var seated = me.Seated && me.Cart == rc.Cart;
        GD.Print(FormattableString.Invariant($"[net] ride: over the exit bridge: {_exitFrames} frame(s), the cart's prompt \"\" on every one; {(seated ? $"still seated in seat {me.Seat}" : "NOT SEATED")} ({(seated ? "ok" : "FAIL")})"));
        return !seated && RideFail(net, "the Get out pressed over the exit bridge let our hero out");
    }

    // ---- both clients: the parked train's smash seen here ----------------------------------------------------------------------------

    /// <summary>The parked train's "smashed" PropEvt here: this screen's server time when it arrived, and the party cart's plating in the first cart state after it.</summary>
    private double _pkSmashT = -1;
    private float _pkPlating = float.NaN;
    private bool _pkSeen, _pkTold;

    /// <summary>Each message as it arrives (both clients, before it is applied): the parked train's smash and the cart state after it.</summary>
    private void NoteParkedWire(NetMessage m)
    {
        switch (m)
        {
            case PropEvt pe when !_pkSeen && pe.What == "smashed" && pe.Ref.StartsWith(NetRefs.ParkedKind + ":", StringComparison.Ordinal):
                _pkSeen = true;
                _pkSmashT = _wireClient?.ServerNow ?? -1;
                break;
            case CartStateMsg cs when _pkSeen && float.IsNaN(_pkPlating):
                foreach (var c in cs.Carts)
                    if (c.Kind == (byte)CartKind.Party) { _pkPlating = c.Plating; break; }
                break;
        }
    }

    /// <summary>Each RIDE client tick: once the smash and the cart state after it are in, told once (the script holds it to the server's line).</summary>
    private void RideParkedTell()
    {
        if (_pkTold || !_pkSeen || float.IsNaN(_pkPlating)) return;
        _pkTold = true;
        GD.Print(FormattableString.Invariant($"[net] ride: parked: smashed here (PropEvt) {_pkSmashT:0.000}, plating {_pkPlating:0}"));
    }

    // ---- the joiner: the lift boarded by a tub (Task 6's ⚠️4) --------------------------------------------------------------------------

    /// <summary>
    /// The body of the lift the joiner presses: the first of its wagons (W1..W4) that offers "Board the cart" here, else its cart (the server puts the
    /// joiner in reach of a wagon when one is in reach of a walkable cell beside the parked train, else of its cart).
    /// </summary>
    private static (Party.IInteractable Body, string Name, string Offer, float Tub) LiftBodyToPress(Rails.ParkedTrain lift, Party.Adventurer me)
    {
        foreach (var w in lift.Wagons)
            if (IsInstanceValid(w) && w.Prompt == Rails.ParkedRules.BoardPrompt) return (w, $"W{w.Number}", w.Prompt, w.TubDistance(me));
        return (lift.Cart, "cart", lift.Cart.Prompt, lift.Cart.TubDistance(me));
    }

    /// <summary>
    /// Server: where the joiner is put to board the lift: beside the lift wagon nearest a walkable cell outside (its tub within the board reach from
    /// that cell's floor point, by the body or the eye), else beside its cart. The body's name ("W{k}" or "cart") with it.
    /// </summary>
    private static (Vector3 Spot, Party.IInteractable Body, string Name) LiftBoardSpot(Main main, Dungeon.Generation.FloorLayout layout, Rails.RailController rc, Rails.RailEncounterController enc, Rails.ParkedTrain train)
    {
        Rails.TrainWagon? best = null;
        var bestSpot = Vector3.Zero;
        var bestReach = float.MaxValue;
        foreach (var w in train.Wagons)
        {
            if (!IsInstanceValid(w)) continue;
            var spot = Core.DebugTools.BesideLift(main, layout, rc, enc, w); // its floor point + 0.3 m (the body), or the lane ahead of the cart when none is in reach
            var reach = Mathf.Min(w.TubDistance(spot), w.TubDistance(spot + Vector3.Up * 1.3f));
            var cell = Dungeon.DungeonBuilder.WorldToCell(spot);
            if (reach > Rails.RideInputRules.BoardReach - 0.05f || layout.IsRail(cell) || layout.InIsland(cell) || reach >= bestReach) continue;
            (best, bestSpot, bestReach) = (w, spot, reach);
        }
        return best != null ? (bestSpot, best, $"W{best.Number}") : (Core.DebugTools.BesideLift(main, layout, rc, enc, train.Cart), train.Cart, "cart");
    }
}
