using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The scripted multiplayer check (<c>scripts/net_test.sh</c>): a headless server and a headless client prove the
/// join, the identical floor, movement echoed back, chat, and a clean leave. Each side prints one RESULT line.
/// </summary>
public partial class NetTestFlow : Node
{
    public bool ServerSide { get; set; }
    private int _step;
    private double _clock, _elapsed;
    private Vector3 _start;
    private bool _done;

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        _elapsed += delta;
        if (ServerSide) ServerTick(); else ClientTick(delta);
    }

    private void Finish(string tag, bool ok, string detail)
    {
        _done = true;
        GD.Print($"{tag} RESULT {(ok ? "ok" : "FAIL")}: {detail}");
        GetTree().CreateTimer(0.5).Timeout += () => GetTree().Quit(ok ? 0 : 1);
    }

    private void ServerTick()
    {
        var world = NetSession.Instance.Server;
        if (world == null) return;
        if (NetSession.Instance.TestRide) { RideServerTick(world); return; }
        var resumed = NetSession.Instance.Resumed;
        if (Main.Instance?.Party?.Adventurers.FirstOrDefault(a => a != null && IsInstanceValid(a)) is { } leadNow) _lastLead = leadNow.GlobalPosition;
        if (!_pileLeft && !resumed && Game.Instance.CurrentFloor != 0 && _lastLead is { } lastLead && world.Trace.Any(t => t.StartsWith("left:")) && Main.Instance?.Party is { } sp)
        {
            // leave a coin pile on the floor while the client is away: it must come back through the floor state with an id the server knows
            _pileLeft = true;
            var leftPile = sp.SpawnCoins(37, lastLead + new Vector3(1.5f, 0.4f, 0));
            if (leftPile != null) leftPile.DroppedByParty = true; // companions leave it alone: only the rejoined client takes it
            GD.Print($"[net-server] left a 37 gold pile at {lastLead:0.0}: {leftPile != null}");
        }
        if (!_potionsGranted && !resumed && world.Trace.Contains("bot:Sable") && Main.Instance?.Party is { } stocked && Game.Instance.Catalog.TryBase("item.health_potion", out _))
        {
            // the client's bag guard step (34) needs something in the shared bag: a potion for its bot to drink
            _potionsGranted = stocked.Bag.TryAdd(Game.Instance.Catalog.Create("item.health_potion", 3));
            GD.Print($"[net-server] put 3 health potions in the bag for the bag guard: {_potionsGranted}");
        }
        if (!_propsRevealed && !resumed && world.Trace.Contains("bot-aside:Sable") && Main.Instance?.Builder.FloorRoot is { } propRoot)
        {
            // the client's prop check step (35) spoils a door and a trap it has seen: make sure one of each is seen, on both ends
            _propsRevealed = true;
            var door = propRoot.GetChildren().OfType<Dungeon.Props.Door>().FirstOrDefault(d => !d.IsOpen);
            var trap = propRoot.GetChildren().OfType<Dungeon.Props.Trap>().FirstOrDefault(t => !t.PlacedByParty && t.SpawnIndex >= 0 && t.Armed);
            door?.Reveal();
            trap?.Reveal();
            GD.Print($"[net-server] revealed door {door?.VaultIndex.ToString() ?? "-"} and trap {trap?.SpawnIndex.ToString() ?? "-"} for the prop check");
        }
        if (!_goldGranted && !resumed && world.Trace.Contains("floor:2") && Main.Instance?.Party is { } rich)
        {
            // the client buys at the merchant on floor 2: enough gold for any of his wares
            _goldGranted = true;
            rich.AddGold(10_000);
            GD.Print($"[net-server] granted 10000 gold for the shop: purse {rich.Gold}");
        }
        if (!_hasteGiven && !resumed && world.Trace.Contains("cheer") && Main.Instance?.Party is { } hasted && world.Registry.Online.FirstOrDefault(s => s.BotOwner == null) is { } cheerer && cheerer.MemberIndex < hasted.Members.Length)
        {
            // the effect sync (v14): a short buff on the client's hero, with no MemberChanged anywhere; its start and its end must
            // both reach the client's sheet (step 17), because the server now compares sheets instead of trusting the event
            _hasteGiven = true;
            var applied = hasted.Members[cheerer.MemberIndex].Effects.Apply(new Combat.StatusApplication(Combat.StatusKind.Haste, 2.5f, 0.2f));
            GD.Print($"[net-server] gave {cheerer.Name} 2.5 s of Haste for the effect sync: {applied}");
        }
        if (((resumed || Game.Instance.CurrentFloor == 0) && world.Trace.Contains("empty")) || world.Trace.Count(t => t == "empty") >= 2 || (world.Trace.Contains("empty") && _elapsed > 150)) // the v14 steps (bag guard, prop, enemy and floor checks) run the pass past 90 s
        {
            var pressOnFloor = Main.Instance?.Builder.Hazards.OfType<Dungeon.Props.Press>().Any() == true;
            var want = resumed ? new[] { "rejoin:", "ready:", "left:" }
                : Game.Instance.CurrentFloor == 0 ? new[] { "join:", "ready:", "node:deal", "left:" } // --floor=0: the client joins the tavern, strikes the deal and leaves
                : pressOnFloor ? new[] { "join:", "ready:", "moved:", "chat", "ping", "kill", "stairs:immediate", "floor:2", "hazard:", "shop", "cheer", "left:", "rejoin:" }
                : new[] { "join:", "ready:", "moved:", "chat", "ping", "kill", "stairs:immediate", "floor:2", "shop", "cheer", "left:", "rejoin:" };
            var missing = want.Where(w => !world.Trace.Any(t => t.StartsWith(w))).ToList();
            var ordered = true;
            var last = -1;
            foreach (var w in want)
            {
                var i = world.Trace.FindIndex(t => t.StartsWith(w));
                if (i < last) ordered = false;
                last = Math.Max(last, i);
            }
            Finish("[net-server]", missing.Count == 0 && ordered, $"trace=[{string.Join(", ", world.Trace)}]" + (missing.Count > 0 ? $" missing=[{string.Join(", ", missing)}]" : "") + (ordered ? "" : " out of order"));
            return;
        }
        if (_elapsed > 170) Finish("[net-server]", false, $"timed out, trace=[{string.Join(", ", world.Trace)}]");
    }

    // ---- the RIDE pass (--net-test-ride): floor 12's rides on a client ----------------------------

    private bool _rideJumped, _rideBuiltSeen, _rideThrown;
    private double _rideReadyAt = -1;
    /// <summary>How many times the cart has left a stop (every one a boarding's countdown running out).</summary>
    private int _rideDeparts;
    /// <summary>The client's hero is put beside the cart this long after the client is ready (its prop checks run first).</summary>
    private const double RideFirstDeparture = 14;
    /// <summary>The RIDE pass rides the cart from the landing to this stop (ride 3 has a slag cart; ride 4 runs with the first client aboard while the joiner chases it in a spare).</summary>
    private const int RideLastStop = 4;

    /// <summary>The server's half of the RIDE pass: once a client is ready the party goes to floor 12; the client checks it there and leaves.</summary>
    private void RideServerTick(ServerWorld world)
    {
        if (!_rideJumped && world.Trace.Any(t => t.StartsWith("ready:")) && Game.Instance.CurrentFloor != 12)
        {
            _rideJumped = true;
            GD.Print("[net-server] ride: a client is ready; the party goes to floor 12");
            Game.Instance.JumpToFloor(12);
        }
        if (!_rideBuiltSeen && Main.Instance is { InGame: true } main && main.Builder.CurrentFloor == 12 && main.Builder.FloorRoot is { } root
            && Dungeon.Rails.RailEncounterController.Instance is { } enc && IsInstanceValid(enc) && enc.GetParent() == root)
        {
            _rideBuiltSeen = true;
            var props = PropSync.Capture(root);
            GD.Print($"[net-server] ride: floor 12 built with {enc.SetPieceCount} set pieces; rail props {(props.Rail != null ? "captured" : "MISSING")}, prop hash {PropCheck.Hash(props):X8}");
        }
        // a PropEvt on the wire: once the client has built floor 12 too, ride 4's spur lever is thrown here; the client must show it.
        // Ride 4 never runs in this pass, and ride 3's switch is the client's to throw from the seat
        if (_rideBuiltSeen && !_rideThrown && world.Registry.Online.Any(sl => sl.BotOwner == null && sl.Ready))
        {
            if (_rideReadyAt < 0) _rideReadyAt = _elapsed;
            if (_elapsed - _rideReadyAt >= 2 && Dungeon.Rails.RailEncounterController.Instance is { } thrower && IsInstanceValid(thrower))
            {
                _rideThrown = true;
                var spur = thrower.PieceIndexOf("spur_switch", 4);
                var lever = spur >= 0 ? thrower.NetNode(NetRefs.SpurKind, spur) as Dungeon.Rails.TrackSwitch : null;
                lever?.DebugThrow();
                GD.Print($"[net-server] ride: threw ride 4's spur lever ({(lever != null ? NetRefs.Spur(spur) : "NONE")}) for the client to see");
            }
        }
        // riding (task 5): at the landing the client's hero is put beside the cart and boards it through its own InteractIntent; it pumps
        // out of the landing and brakes on the way (RideIntent), and gets out at stop 1 through the intent. The props (task 6): stop 1's
        // holders die by the test's hand, so its gate lifts for real; the hero is put by the orc's keyring, then by cage 1, and takes the
        // keys and opens the cage through its intents; a crewman in that cage goes down and the client revives him through the intent;
        // put beside the cart, the hero boards again and rides on (stop 2's holders die too), and throws ride 3's switch from the seat.
        // Every departure is a real one; at stop 3 the client leaves the game seated
        if (_rideThrown && _elapsed - _rideReadyAt >= RideFirstDeparture && Dungeon.Rails.RailController.Instance is { } rc && IsInstanceValid(rc)
            && world.Registry.Online.FirstOrDefault(sl => sl.BotOwner == null && sl.Name != RideJoinerName) is { } rider && Main.Instance?.Party is { } party && rider.MemberIndex < party.Adventurers.Length)
        {
            var hero = party.Adventurers[rider.MemberIndex];
            var held = rc.State.Mode == Dungeon.Rails.CartMode.Held;
            // the pass checks the wire, not the fight: the client's hero rides rides 2 and 3 through the hounds (it swings at its boarders from
            // the seat, FightBoarders) and ride 2's Forge Priest, and its fall would wipe the party (the only player) and reload the floor under
            // the script, so the server keeps it topped up (a test hook, TopUp); on ride 4 the kiln's
            // golem, hammers and pour dent the cart, and a wreck would end the joiner's chase under the script, so its plating is kept up too
            TopUp(hero);
            if (rc.StopIndex == 3 && rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.State.Plating < 50f)
            {
                rc.DebugSetPlating(Dungeon.Rails.CartRules.MaxPlating);
                _ridePlatingTopUps++;
            }
            if (!_rideShoved && held && rc.StopIndex == 0 && !hero.Seated)
            {
                _rideShoved = true;
                _rideMember = rider.MemberIndex;
                var beside = WithinReach(rc);
                hero.Shove(beside);
                GD.Print(FormattableString.Invariant($"[net-server] ride: {rider.Name} is put beside the cart at the landing ({beside.X:0.00}, {beside.Y:0.00}, {beside.Z:0.00}), {beside.DistanceTo(rc.Cart.GlobalPosition):0.00} m from it"));
            }
            if (_rideShoved && !_rideSeated && hero.Seated && hero.Cart == rc.Cart)
            {
                _rideSeated = true;
                GD.Print($"[net-server] ride: {rider.Name} boarded through the intent in seat {hero.Seat} (traced {(world.Trace.Contains($"aboard:{rider.Name}") ? "aboard" : "NOTHING")}); the countdown runs: {rc.Boarding.Running}");
            }
            if (_rideSeated && !_rideLeftLanding && rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 0)
            {
                _rideLeftLanding = true;
                _rideRollingSince = _elapsed;
                GD.Print($"[net-server] ride: the cart departs the landing with {rider.Name} aboard (ride 1 armed: {Dungeon.Rails.RailEncounterController.Instance?.ArmedRide})");
            }
            if (_rideLeftLanding && rc.StopIndex == 0 && rc.State.Mode == Dungeon.Rails.CartMode.Rolling)
            {
                var input = rc.Party.LastInput;
                if (_elapsed - _rideRollingSince <= 0.5) _rideMostPumpers = Math.Max(_rideMostPumpers, input.Pumpers);
                else if (!_ridePumpTold)
                {
                    _ridePumpTold = true;
                    _ridePumpOk = _rideMostPumpers >= 1;
                    GD.Print($"[net-server] ride: the cart left the landing with {_rideMostPumpers} pumper(s) from the client's RideIntent ({(_ridePumpOk ? "ok" : "NONE")})");
                }
                if (!_rideHeroPumped && hero.Pumping) { _rideHeroPumped = true; GD.Print($"[net-server] ride: {rider.Name}'s hero pumps here (Pumping, in the snapshots)"); }
                if (input.Brake)
                {
                    if (_rideBrakeFromV < 0f) _rideBrakeFromV = rc.State.V;
                    _rideBrakeMinV = Mathf.Min(_rideBrakeMinV, rc.State.V);
                }
                else if (_rideBrakeFromV >= 0f && !_rideBrakeTold)
                {
                    _rideBrakeTold = true;
                    _rideBrakeOk = _rideBrakeFromV - _rideBrakeMinV >= 1f;
                    GD.Print(FormattableString.Invariant($"[net-server] ride: the client's brake took V from {_rideBrakeFromV:0.00} to {_rideBrakeMinV:0.00} m/s ({(_rideBrakeOk ? "ok" : "TOO LITTLE")})"));
                }
            }
            // every departure from a stop (held there, not halted on the line) is a real one: a boarding's countdown ran out
            var heldAtStop = held && !rc.Halted;
            if (_rideHeldAtStop && rc.State.Mode == Dungeon.Rails.CartMode.Rolling)
            {
                _rideDeparts++;
                var aboard = 0;
                foreach (var r in rc.Cart.Riders.Values) if (r is Party.Adventurer { Control: Party.ControlKind.Remote }) aboard++;
                GD.Print($"[net-server] ride: the cart departs stop {rc.StopIndex} with {aboard} player(s) aboard (ride {rc.StopIndex + 1} armed: {Dungeon.Rails.RailEncounterController.Instance?.ArmedRide})");
            }
            _rideHeldAtStop = heldAtStop;
            RideProps(world, rc, hero);
            RideJoinerServer(world, rc, party);
        }
        if (_rideSeated && !_rideSetDown && _rideMember >= 0 && Main.Instance?.Party is { } p2 && _rideMember < p2.Adventurers.Length && !p2.Adventurers[_rideMember].Seated)
        {
            _rideSetDown = true;
            _rideSetDownAt = _elapsed;
            var at = p2.Adventurers[_rideMember].GlobalPosition;
            GD.Print(FormattableString.Invariant($"[net-server] ride: set down at ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}) at stop {Dungeon.Rails.RailController.Instance?.StopIndex}"));
        }
        if (!_rideCrewTold && _rideReadyAt >= 0 && Adventure.Objectives.ObjectiveController.Instance is Adventure.Objectives.CrewRescueController crew)
        {
            _rideCrewTold = true;
            GD.Print($"[net-server] ride: crew seated in wagons: {crew.Wagons.Sum(w => w.Riders.Count)} in {crew.Wagons.Count} wagon(s)");
        }
        RideWreckServer(world);
        if (world.Trace.Contains("empty"))
        {
            var keys = _rideKeyring ? new[] { $"prop:{NetRefs.Keyring}:taken" } : Array.Empty<string>();
            var want = new[] { "join:", "ready:", "floor:12", "aboard:" }.Concat(keys)
                .Concat(new[] { $"prop:{NetRefs.Wagon(1)}:open", $"prop:{NetRefs.Switch(_rideSwitch)}:thrown", "chase:", "chase-done", "unseat-quiet:", "left:", "wreck-spare:", "heap-halt", "wreck-last" }).ToArray();
            var missing = want.Where(w => !world.Trace.Any(t => t.StartsWith(w))).ToList();
            var ordered = true;
            var last = -1;
            foreach (var w in want)
            {
                var i = world.Trace.FindIndex(t => t.StartsWith(w));
                if (i < last) ordered = false;
                last = Math.Max(last, i);
            }
            var reached = _rideMaxStop; // the furthest stop the cart reached (the wreck at the end of the line puts it back at a spare's stop)
            var rode = _rideDeparts >= RideLastStop && reached >= RideLastStop;
            var seats = _rideSeated && _rideSetDown;
            var riding = _ridePumpOk && _rideHeroPumped && _rideBrakeOk;
            var quiet = QuietUnseatChecked(world, out var quietWhy);
            var props = _rideGateUp && _rideCageOpen && _rideRevived && _rideReboarded && _rideCrossed;
            var joiner = _joinReadyMidRide && _joinSeated;
            var chase = _joinSpareHeldRefused && _chaseTaken && _chaseDone && _chaseGapOk && _chaseSetDown && _chaseHops >= 1; // ride 4's gap is always cut
            var thrown = _joinThrown;
            var ok = missing.Count == 0 && ordered && _rideBuiltSeen && rode && seats && riding && quiet && props && joiner && chase && thrown && _wreckOk && _heapHalt && _lastWreckOk;
            Finish("[net-server]", ok, $"ride: {_rideDeparts} departure(s), the cart at stop {reached}; the client's hero boarded and got out: {(seats ? "yes" : "NO")}; pumped {(_ridePumpOk ? "yes" : "NO")} (shown {(_rideHeroPumped ? "yes" : "NO")}), braked {(_rideBrakeOk ? "yes" : "NO")}; props: gate 1 up {Yes(_rideGateUp)}, keys {(_rideKeyring ? Yes(_rideKeysTaken) : "none on this branch")}, cage 1 {Yes(_rideCageOpen)}, revived {Yes(_rideRevived)}, boarded at stop 1 {Yes(_rideReboarded)}, crossed on ride 3 {Yes(_rideCrossed)}; the joiner ready mid-ride 2 {Yes(_joinReadyMidRide)}, boarded at stop 2 {Yes(_joinSeated)}; the left-behind spare: refused while the train was held at stop 3 {Yes(_joinSpareHeldRefused)}, taken {Yes(_chaseTaken)}, halted behind the train {(_chaseDone ? FormattableString.Invariant($"{_chaseGap:0.00} m") : "NO")} ({(_chaseGapOk ? "within 3 m" : "NOT within 3 m")}), riders set down {Yes(_chaseSetDown)}, holes hopped {_chaseHops}; the joiner thrown at speed on ride 3 {Yes(_joinThrown)}; the wreck at the end of the line with a spare {(_wreckAsked ? (_wreckOk ? $"taken over at stop {_wreckSpare}" : "NOT taken over") : "NOT done")}, then halted at the ore heap on ride 3 {Yes(_heapHalt)}, then wrecked until no spare was left {Yes(_lastWreckOk)}; the riders fought their boarders and were topped up {_rideTopUps} time(s), the cart's plating {_ridePlatingTopUps} time(s); left seated: {quietWhy}; trace=[{string.Join(", ", world.Trace)}]" + (missing.Count > 0 ? $" missing=[{string.Join(", ", missing)}]" : "") + (ordered ? "" : " out of order") + (_rideBuiltSeen ? "" : " floor 12 never built its rides") + (rode ? "" : $" the cart never rode to stop {RideLastStop}"));
            return;
        }
        if (_elapsed > 560) Finish("[net-server]", false, $"ride: timed out, trace=[{string.Join(", ", world.Trace)}]");
    }

    // the end of the pass (plan 5 task 11): once the first client has left, the cart is wrecked where it stands by a test hook, with the joiner
    // still watching; a spare takes over (a wreck frame first, then the cart held at the spare's stop). The grid lines stop first: the jump
    // to the spare's stop is not a pair
    private bool _wreckAsked, _wreckOk, _joinThrown, _joinThrowTold;
    private int _wreckSpare = -1, _rideMaxStop = -1;

    private void RideWreckServer(ServerWorld world)
    {
        if (Dungeon.Rails.RailController.Instance is not { } rc || !IsInstanceValid(rc)) return;
        if (!_wreckAsked) _rideMaxStop = Math.Max(_rideMaxStop, rc.StopIndex);
        if (!_joinThrowTold && _joinSeated && !_joinOut && Main.Instance?.Party is { } party
            && world.Registry.Online.FirstOrDefault(sl => sl.BotOwner == null && sl.Name == RideJoinerName) is { } js && js.MemberIndex < party.Adventurers.Length
            && !party.Adventurers[js.MemberIndex].Seated && rc.StopIndex == 2 && rc.State.Mode == Dungeon.Rails.CartMode.Rolling)
        {
            _joinThrowTold = true;
            var at = party.Adventurers[js.MemberIndex].GlobalPosition;
            var onLine = Main.Instance.Builder.Layout is { } layout && layout.IsRail(Dungeon.DungeonBuilder.WorldToCell(at));
            GD.Print(FormattableString.Invariant($"[net-server] ride: {RideJoinerName} got out of the rolling cart on ride 3 (V={rc.State.V:0.00} m/s): thrown, down at ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}) {(onLine ? "on the line" : "OFF THE LINE")} behind the cart"));
            _joinThrown = onLine;
        }
        if (_wreckAsked) { RideHeapServer(world, rc); return; }
        if (!_chaseDone || !world.Trace.Any(t => t.StartsWith("unseat-quiet:")) || !world.Trace.Any(t => t.StartsWith("left:"))) return;
        if (!world.Registry.Online.Any(sl => sl.BotOwner == null && sl.Name == RideJoinerName && sl.Ready)) return;
        if (rc.State.Mode != Dungeon.Rails.CartMode.Held || rc.Chaser != null) return;
        _wreckAsked = true;
        if (Dungeon.Rails.RailNet.Instance is { } rn) rn.RideTraceOff = true;
        var from = rc.StopIndex;
        var fromS = rc.State.S;
        var unused = Enumerable.Range(1, Math.Max(0, rc.StopS.Length - 2)).Where(i => !rc.SparesUsed.Contains(i)).ToList();
        rc.DebugWreck();
        _wreckSpare = rc.StopIndex;
        _wreckOk = rc.State.Mode == Dungeon.Rails.CartMode.Held && unused.Contains(_wreckSpare) && rc.SparesUsed.Contains(_wreckSpare) && rc.Cart.Visible;
        world.Trace.Add($"wreck-spare:{_wreckSpare}");
        GD.Print(FormattableString.Invariant($"[net-server] ride: wreck: the test wrecked the cart at stop {from} (S={fromS:0.0}) with spares [{string.Join(",", unused)}] unused; spare {_wreckSpare} {(_wreckOk ? "took over" : "did NOT take over")}: the cart {rc.State.Mode} at stop {_wreckSpare} (S={rc.State.S:0.0}), spares used [{string.Join(",", rc.SparesUsed)}]"));
    }

    // after the wreck (plan 5 task 11): the joiner, put beside the spare's cart, boards it and rides ride 3 again, with its switch straight
    // this time, so the cart halts at the ore heap's face (the face the client's follower clamps its S at); the joiner is kept topped up
    private bool _heapShoved, _heapBoarded, _heapRolled, _heapHalt;

    private void RideHeapServer(ServerWorld world, Dungeon.Rails.RailController rc)
    {
        if (!_wreckOk || Main.Instance?.Party is not { } party
            || world.Registry.Online.FirstOrDefault(sl => sl.BotOwner == null && sl.Name == RideJoinerName) is not { } js || js.MemberIndex >= party.Adventurers.Length) return;
        var hero = party.Adventurers[js.MemberIndex];
        TopUp(hero);
        if (!_heapShoved && js.Ready && rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == _wreckSpare && !hero.Seated)
        {
            _heapShoved = true;
            var beside = WithinReach(rc);
            hero.Shove(beside);
            GD.Print(FormattableString.Invariant($"[net-server] ride: heap: {RideJoinerName} is put beside the spare's cart at stop {_wreckSpare}, {beside.DistanceTo(rc.Cart.GlobalPosition):0.00} m from it; ride 3's switch is {(Dungeon.Rails.RailEncounterController.Instance?.NetNode(NetRefs.SwitchKind, _rideSwitch) is Dungeon.Rails.TrackSwitch { Thrown: true } ? "THROWN" : "straight")}"));
        }
        if (_heapShoved && !_heapBoarded && hero.Seated && hero.Cart == rc.Cart)
        {
            _heapBoarded = true;
            GD.Print($"[net-server] ride: heap: {RideJoinerName} boarded the spare's cart at stop {rc.StopIndex} through the intent");
        }
        if (_heapBoarded && !_heapRolled && rc.State.Mode == Dungeon.Rails.CartMode.Rolling)
        {
            _heapRolled = true;
            GD.Print($"[net-server] ride: heap: the cart departs stop {rc.StopIndex} with {RideJoinerName} aboard (ride {Dungeon.Rails.RailEncounterController.Instance?.ArmedRide} armed)");
        }
        if (_heapRolled && !_heapHalt && rc.Halted && rc.State.Mode == Dungeon.Rails.CartMode.Held && !float.IsPositiveInfinity(rc.BlockedAt))
        {
            _heapHalt = true;
            world.Trace.Add("heap-halt");
            GD.Print(FormattableString.Invariant($"[net-server] ride: heap: the cart halted at the ore heap on ride 3 at S={rc.State.S:0.000} (the face at S={rc.BlockedAt:0.000}, plating {rc.State.Plating:0})"));
        }
        // the last wreck (plan 6 final review): 3 s after the joiner is out of the halted cart, the test wrecks it until no spare is left,
        // so the cart stands wrecked and hidden; the joiner then has the floor resent, and its first cart state (Wrecked) must play nothing
        if (_heapHalt && !_lastWreckDone && !hero.Seated)
        {
            if (_heapOutAt < 0) _heapOutAt = _elapsed;
            if (_elapsed - _heapOutAt < 3) return;
            _lastWreckDone = true;
            var wrecks = 0;
            while (wrecks < 8 && (rc.Cart.Visible || rc.State.Mode != Dungeon.Rails.CartMode.Wrecked)) { rc.DebugWreck(); wrecks++; }
            _lastWreckOk = rc.State.Mode == Dungeon.Rails.CartMode.Wrecked && !rc.Cart.Visible;
            world.Trace.Add("wreck-last");
            GD.Print($"[net-server] ride: last wreck: the test wrecked the cart {wrecks} time(s) {(_lastWreckOk ? "until no spare was left" : "and a spare STILL stands")}: the cart {rc.State.Mode}, shown {rc.Cart.Visible}, spares used [{string.Join(",", rc.SparesUsed)}]");
        }
    }

    private bool _lastWreckDone, _lastWreckOk;
    private double _heapOutAt = -1;

    // riding (plan 5 task 5): the server's side of the boarding, the pump, the brake and the quiet unseat
    private bool _rideShoved, _rideLeftLanding, _ridePumpTold, _ridePumpOk, _rideHeroPumped, _rideBrakeTold, _rideBrakeOk, _rideHeldAtStop;
    private double _rideRollingSince = -1;
    private int _rideMostPumpers;
    private float _rideBrakeFromV = -1f, _rideBrakeMinV = float.MaxValue;

    private static string Yes(bool b) => b ? "yes" : "NO";

    /// <summary>
    /// The riders fight their boarders from the seat (FightBoarders, on each client), but a lone warrior cannot answer ride 2's Forge Priest
    /// shooting from its slag cart 5-6 m off (the fix wave's first run: 129 hp down to 23 by stop 2), so a player's hero is still kept
    /// above this share of its hp (its fall would wipe the party under the script; the pass checks the wire, not the fight). Each top-up is logged.
    /// </summary>
    private const float TopUpBelow = 0.75f;

    private void TopUp(Party.Adventurer hero)
    {
        if (hero.Downed || hero.State.Hp >= hero.State.Stats[Characters.Stat.MaxHp] * TopUpBelow) return;
        GD.Print(FormattableString.Invariant($"[net-server] ride: top-up: {hero.State.Name} at {hero.State.Hp:0}/{hero.State.Stats[Characters.Stat.MaxHp]:0} hp (stop {Dungeon.Rails.RailController.Instance?.StopIndex}, ride {Dungeon.Rails.RailEncounterController.Instance?.ArmedRide} armed, seated {hero.Seated})"));
        hero.State.FullRestore();
        EventBus.Instance.RaiseMemberChanged(hero.Index);
        _rideTopUps++;
    }

    // a joiner mid-ride (plan 5 task 8): the second client joins during ride 2, is put beside the cart at stop 2 and boards for ride 3
    private const string RideJoinerName = "Joiner";
    private bool _joinReadySeen, _joinReadyMidRide, _joinShoved, _joinSeated;

    private void RideJoinerServer(ServerWorld world, Dungeon.Rails.RailController rc, Party.PartyController party)
    {
        if (world.Registry.Online.FirstOrDefault(sl => sl.BotOwner == null && sl.Name == RideJoinerName) is not { } slot || slot.MemberIndex >= party.Adventurers.Length) return;
        var hero = party.Adventurers[slot.MemberIndex];
        if (!_joinReadySeen)
        {
            if (!world.Trace.Contains($"ready:{RideJoinerName}")) return;
            _joinReadySeen = true;
            var armed = Dungeon.Rails.RailEncounterController.Instance?.ArmedRide ?? -1;
            _joinReadyMidRide = rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 1 && armed == 2;
            GD.Print(FormattableString.Invariant($"[net-server] ride: {RideJoinerName} is ready at t={NetSession.Instance.Now:0.000} with the cart {rc.State.Mode} at stop {rc.StopIndex} (S={rc.State.S:0.0}, ride {armed} armed): {(_joinReadyMidRide ? "mid-ride 2" : "NOT mid-ride 2")}"));
        }
        TopUp(hero);
        if (!_joinShoved && rc.State.Mode == Dungeon.Rails.CartMode.Held && !rc.Halted && rc.StopIndex == 2 && !hero.Seated)
        {
            _joinShoved = true;
            var beside = WithinReach(rc);
            hero.Shove(beside);
            GD.Print(FormattableString.Invariant($"[net-server] ride: {RideJoinerName} is put beside the cart at stop 2, {beside.DistanceTo(rc.Cart.GlobalPosition):0.00} m from it (the countdown runs: {rc.Boarding.Running})"));
        }
        if (_joinShoved && !_joinSeated && hero.Seated && hero.Cart == rc.Cart)
        {
            _joinSeated = true;
            GD.Print($"[net-server] ride: {RideJoinerName} boarded at stop 2 through the intent in seat {hero.Seat}");
        }
        RideChaseServer(world, rc, hero);
    }

    // the left-behind spare (plan 5 task 9): at stop 3 the joiner gets out and stays on foot while the train leaves on ride 4 with the first client
    private bool _joinOut, _joinHoldersKilled, _joinSpareHeldRefused, _chaseTaken, _chaseDone, _chaseGapOk, _chaseSetDown, _chaseDepartTold;
    private double _joinOutAt;
    private float _chaseGap = float.NaN;
    private int _chaseHops;
    private int _ridePlatingTopUps;

    /// <summary>
    /// The server's side of the chase: the joiner, out at stop 3, is put beside stop 3's spare (which offers nothing while the train is held
    /// there); 2 s later stop 3's holders die by the test's hand, so the train leaves on ride 4 with the first client aboard; the joiner takes
    /// the spare through its intent once the train has left; the chaser runs up behind the train and halts within 3 m of its last body
    /// (traced chase-done), and its riders are set down beside the lane.
    /// </summary>
    private void RideChaseServer(ServerWorld world, Dungeon.Rails.RailController rc, Party.Adventurer hero)
    {
        if (!_joinSeated || Main.Instance is not { } main) return;
        if (!_joinOut && !hero.Seated && rc.StopIndex == 3 && rc.State.Mode == Dungeon.Rails.CartMode.Held && !rc.Halted)
        {
            _joinOut = true;
            _joinOutAt = _elapsed;
            if (rc.SpareAt(3) is not { } spare) { GD.Print("[net-server] ride: stop 3 has no spare cart"); return; }
            var spot = BesideSpare(main, rc, spare);
            hero.Shove(spot);
            var offer = spare.PromptFor?.Invoke(hero) ?? "";
            GD.Print(FormattableString.Invariant($"[net-server] ride: {RideJoinerName} is on foot at stop 3 (thrown from the cart on ride 3) and stays so; put beside stop 3's spare, {spot.DistanceTo(spare.GlobalPosition):0.00} m from it ({spare.TubDistance(spot):0.00} m from its tub); with the train held at stop 3 the spare offers \"{offer}\" here"));
        }
        if (_joinOut && !_joinHoldersKilled && _elapsed - _joinOutAt >= 2)
        {
            _joinHoldersKilled = true;
            GD.Print($"[net-server] ride: stop 3's {rc.DebugKillHolders(3)} holder(s) die by the test's hand; gate up: {rc.GateUp(3)}; the train boards with {rc.Cart.Riders.Count} rider(s), {RideJoinerName} on foot");
        }
        if (_joinOut && !_chaseDepartTold && rc.StopIndex == 3 && rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.SpareAt(3) is { } left)
        {
            _chaseDepartTold = true;
            var offerNow = left.PromptFor?.Invoke(hero) ?? "";
            GD.Print($"[net-server] ride: the train leaves stop 3 without {RideJoinerName}; stop 3's spare now offers \"{offerNow}\" to {RideJoinerName} beside it");
        }
        if (_joinOut && !_chaseTaken && rc.Chaser is { } chaser)
        {
            _chaseTaken = true;
            world.Trace.Add($"chase:{rc.ChaserStop}");
            GD.Print(FormattableString.Invariant($"[net-server] ride: {RideJoinerName} took spare {rc.ChaserStop} through the intent (spares used [{string.Join(",", rc.SparesUsed)}]): the chaser {chaser.Name} stands at S={rc.ChaserRun!.State.S:0.0}, the train {rc.State.Mode} at S={rc.State.S:0.0} with {rc.Train.Count} wagon(s); {chaser.Riders.Count} rider(s), {RideJoinerName} seated in it: {hero.Cart == chaser}"));
        }
        if (_chaseTaken && !_chaseDone && rc.ChasesDone > 0)
        {
            _chaseDone = true;
            _chaseGap = rc.LastChaseGap;
            _chaseGapOk = _chaseGap >= 0f && _chaseGap <= 3f;
            _chaseSetDown = !hero.Seated && Main.Instance?.Builder.Layout is { } layout && !layout.IsRail(Dungeon.DungeonBuilder.WorldToCell(hero.GlobalPosition));
            _chaseHops = rc.ChaserHops;
            world.Trace.Add("chase-done");
            GD.Print(FormattableString.Invariant($"[net-server] ride: chase-done: the chaser halted {_chaseGap:0.00} m behind the train's last body (the train held at stop {rc.StopIndex}, S={rc.State.S:0.0}, {rc.Train.Count} wagon(s)); {RideJoinerName} set down beside the lane: {(_chaseSetDown ? "yes" : "NO")}; it hopped {_chaseHops} hole(s) in the line (ride 4's gap, the fallen trestle)"));
            GD.Print($"[net-server] ride: relief carts sent online: {rc.ReliefsSent}"); // a tripwire for the relief's single-player guard (plan 7)
        }
        if (_joinOut && !_joinSpareHeldRefused && !_chaseDepartTold && rc.SpareAt(3) is { } held && rc.StopIndex == 3 && rc.State.Mode == Dungeon.Rails.CartMode.Held)
            _joinSpareHeldRefused = (held.PromptFor?.Invoke(hero) ?? "") == "" && held.TubDistance(hero) <= Dungeon.Rails.SpareRules.SpareReach;
    }

    /// <summary>A spot near the spare's side toward the line (within its prompt's reach, off the rails, on a walkable cell): where the joiner is put at stop 3.</summary>
    private static Vector3 BesideSpare(Main main, Dungeon.Rails.RailController rc, Dungeon.Rails.MineCart spare)
    {
        var from = spare.GlobalPosition;
        var toward = Flat(rc.Cart.GlobalPosition - from);
        var layout = main.Builder.Layout;
        foreach (var d in new[] { 2.0f, 1.7f, 1.4f })
        {
            var at = OnFloor(main, from + toward * d);
            var cell = Dungeon.DungeonBuilder.WorldToCell(at);
            if (layout != null && layout.IsWalkable(cell) && !layout.IsRail(cell)) return at;
        }
        return OnFloor(main, from + toward * 1.4f);
    }
    /// <summary>How often the server topped the client's hero up.</summary>
    private int _rideTopUps;

    // the props (plan 5 task 6): the server's side of the gate, the keys, the cage, the revive, the second boarding and the switch
    private bool _rideHoldersKilled, _rideGateUp, _rideKeyring, _rideAtKeys, _rideKeysTaken, _rideAtCage, _rideCageOpen, _rideRevived, _rideAtCart,
        _rideReboarded, _rideCrossed;
    private Npc? _rideDowned;
    /// <summary>Ride 3's switch_out (its wire name's number), the one the client throws from the seat.</summary>
    private int _rideSwitch = -1;

    /// <summary>
    /// The server's steps at stop 1 and on: the stop's holders die once the client's hero is set down there (its gate lifts for real), the
    /// hero is put by the keyring (condemned branch) and then by cage 1, a crewman in the opened cage goes down for the client to revive,
    /// the hero is put beside the cart to board again (stop 2's holders die then, so ride 3 leaves for real), and ride 3's switch is the
    /// client's. Each step waits for the client's intent to have done the last one here.
    /// </summary>
    private void RideProps(ServerWorld world, Dungeon.Rails.RailController rc, Party.Adventurer hero)
    {
        if (!_rideSetDown || Adventure.Objectives.ObjectiveController.Instance is not Adventure.Objectives.CrewRescueController crew || Main.Instance is not { } main) return;
        if (_rideSwitch < 0 && Dungeon.Rails.RailEncounterController.Instance is { } enc && IsInstanceValid(enc)) _rideSwitch = enc.PieceIndexOf("switch_out", 3);
        var wagon = crew.Wagons.FirstOrDefault(w => w.Stop == 1);
        if (!_rideHoldersKilled && rc.StopIndex == 1)
        {
            _rideHoldersKilled = true;
            _rideKeyring = crew.Keyring != null;
            GD.Print($"[net-server] ride: stop 1's {rc.DebugKillHolders(1)} holder(s) die by the test's hand; gate up: {rc.GateUp(1)}; keyring: {(_rideKeyring ? "at Grisel's belt" : "none")}");
        }
        if (_rideHoldersKilled && !_rideGateUp && rc.GateUp(1))
        {
            _rideGateUp = true;
            GD.Print("[net-server] ride: gate 1 is up (lifted by the gate check, told as gate:1 up)");
        }
        if (!_rideGateUp || _elapsed - _rideSetDownAt < 1.0 || wagon == null) return; // the client's screen sees where it was set down first
        if (_rideKeyring && !_rideAtKeys && crew.Keyring is { } ring)
        {
            _rideAtKeys = true;
            var away = Flat(ring.GlobalPosition - wagon.GlobalPosition);
            var spot = OnFloor(main, ring.GlobalPosition + away * 1.0f);
            hero.Shove(spot);
            GD.Print(FormattableString.Invariant($"[net-server] ride: {hero.State.Name} is put by the orc's keyring, {spot.DistanceTo(ring.GlobalPosition):0.00} m from it"));
        }
        if (_rideKeyring && !_rideKeysTaken)
        {
            if (!crew.OrcKeys) return;
            _rideKeysTaken = true;
            GD.Print($"[net-server] ride: the orc's keys are taken (traced {(world.Trace.Contains($"prop:{NetRefs.Keyring}:taken") ? "through the intent" : "NOT through the intent")})");
        }
        if (!_rideAtCage)
        {
            _rideAtCage = true;
            var spot = OnFloor(main, wagon.GlobalPosition + wagon.GlobalBasis.X.Normalized() * (wagon.PlatformSide * 2.2f));
            hero.Shove(spot);
            GD.Print(FormattableString.Invariant($"[net-server] ride: {hero.State.Name} is put at cage 1's door, {spot.DistanceTo(wagon.GlobalPosition):0.00} m from it"));
        }
        if (!_rideCageOpen)
        {
            if (!wagon.Open) return;
            _rideCageOpen = true;
            _rideDowned = crew.Crew.FirstOrDefault(n => IsInstanceValid(n) && !n.Dead && !n.Downed && n.Ride == wagon);
            _rideDowned?.GoDown();
            GD.Print($"[net-server] ride: cage 1 is open (traced {(world.Trace.Contains($"prop:{NetRefs.Wagon(1)}:open") ? "through the intent" : "NOT through the intent")}); {_rideDowned?.NpcName ?? "NOBODY"} aboard goes down by the test's hand");
            if (_rideDowned == null) return;
        }
        if (!_rideRevived)
        {
            if (_rideDowned == null || !IsInstanceValid(_rideDowned) || _rideDowned.Downed) return;
            _rideRevived = true;
            GD.Print($"[net-server] ride: {_rideDowned.NpcName} is up again ({world.Trace.Count(t => t == $"prop:{NetRefs.Wagon(1)}:open")} use(s) of wagon:1 through the intent)");
        }
        if (!_rideAtCart && rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == 1 && !hero.Seated)
        {
            _rideAtCart = true;
            var beside = WithinReach(rc);
            hero.Shove(beside);
            GD.Print(FormattableString.Invariant($"[net-server] ride: {hero.State.Name} is put beside the cart at stop 1 to board again ({beside.DistanceTo(rc.Cart.GlobalPosition):0.00} m from it)"));
        }
        if (_rideAtCart && !_rideReboarded && hero.Seated && hero.Cart == rc.Cart)
        {
            _rideReboarded = true;
            GD.Print($"[net-server] ride: {hero.State.Name} boarded again at stop 1 in seat {hero.Seat}; stop 2's {rc.DebugKillHolders(2)} holder(s) die by the test's hand");
        }
        if (_rideReboarded && !_rideCrossed && rc.Crossing && rc.StopIndex == 2)
        {
            _rideCrossed = true;
            GD.Print($"[net-server] ride: the cart takes ride 3's switch at S={rc.State.S:0.0} (the lever traced {(world.Trace.Contains($"prop:{NetRefs.Switch(_rideSwitch)}:thrown") ? "thrown through the intent" : "NOT thrown through the intent")})");
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0f;
        return v.LengthSquared() > 0.0001f ? v.Normalized() : Vector3.Forward;
    }

    /// <summary>The point at floor height (its cell's surface, a little above it).</summary>
    private static Vector3 OnFloor(Main main, Vector3 at)
    {
        var floor = main.Builder.Surface(Dungeon.DungeonBuilder.WorldToCell(at));
        return new Vector3(at.X, floor.Y + 0.1f, at.Z);
    }

    /// <summary>A spot 2.2 m from the cart's middle toward where a rider steps out (that cell's middle is a cell away, past the 3.5 m reach of an intent).</summary>
    private static Vector3 WithinReach(Dungeon.Rails.RailController rc)
    {
        var cart = rc.Cart.GlobalPosition;
        var beside = rc.StandBesideCart();
        var flat = new Vector3(beside.X - cart.X, 0f, beside.Z - cart.Z);
        if (flat.LengthSquared() < 0.01f) return beside;
        var at = cart + flat.Normalized() * 2.2f;
        return new Vector3(at.X, beside.Y, at.Z);
    }

    /// <summary>The client left seated at stop 3: its body stands where the quiet unseat put it (beside the lane, off the rails), its seat is free, and its parked pose is that spot.</summary>
    private bool QuietUnseatChecked(ServerWorld world, out string why)
    {
        if (world.LastQuietUnseat is not { } at) { why = "NO quiet unseat"; return false; }
        var slot = world.Registry.ByMember(_rideMember);
        var parked = slot?.ParkedPose is { } pp ? new Vector3(pp.X, pp.Y, pp.Z) : (Vector3?)null;
        var rc = Dungeon.Rails.RailController.Instance;
        var hero = Main.Instance?.Party is { } party && _rideMember >= 0 && _rideMember < party.Adventurers.Length ? party.Adventurers[_rideMember] : null;
        var free = rc != null && IsInstanceValid(rc) && hero != null && rc.Cart.Seats.SeatOf(hero.RiderId) == null && !rc.Cart.Riders.ContainsKey(hero.RiderId) && !hero.Seated;
        var offRails = Main.Instance?.Builder.Layout is { } layout && !layout.IsRail(Dungeon.DungeonBuilder.WorldToCell(at)) && layout.IsWalkable(Dungeon.DungeonBuilder.WorldToCell(at));
        var gap = parked is { } p ? p.DistanceTo(at) : float.PositiveInfinity;
        why = FormattableString.Invariant($"unseated at ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}) {(offRails ? "beside the lane" : "ON THE RAILS")}, seat freed {(free ? "yes" : "NO")}, parked pose {(parked == null ? "NONE" : $"{gap:0.00} m from it")}");
        return free && offRails && gap <= 0.05f;
    }

    private int _ridePropChecks, _rideAsks, _ridePropMatched, _rideMostCarts;
    private bool _rideSawRolling;
    // seats (plan 5 task 4): the server's side seats the client's hero and sees it set down; the client's checks its own body
    private int _rideMember = -1;
    private bool _rideSeated, _rideSetDown, _rideCrewTold;
    private double _rideSetDownAt;
    private bool _rideCrewOk, _rideSeatOk, _rideLandOk;
    private int _rideCrewSeated = -1;
    private string? _rideSeatFail;

    /// <summary>
    /// The client's seat checks, every tick of step 52: the crew sit in their wagons here (the catch-up's SeatMsgs); this client's hero,
    /// seated by the server, sits in a hero seat (0-3) of its copy of the cart; and once the server sets it down, the body stands within
    /// 0.5 m of the server's landing within 0.5 s of that SeatMsg.
    /// </summary>
    private void RideSeatChecks(Main main, Dungeon.Rails.RailController rc)
    {
        if (!_rideCrewOk && Adventure.Objectives.ObjectiveController.Instance is Adventure.Objectives.CrewRescueController crew && main.Builder.FloorRoot is { } root)
        {
            var seated = root.GetChildren().OfType<Npc>().Count(n => n.Crew && n.Cart is Dungeon.Rails.CageWagon);
            var inWagons = crew.Wagons.Sum(w => w.Riders.Count);
            if (seated > 0 && seated == inWagons)
            {
                _rideCrewOk = true;
                _rideCrewSeated = seated;
                GD.Print($"[net] ride: crew seated in their wagons here: {seated} in {crew.Wagons.Count} wagon(s)");
            }
        }
        if (main.Party.Local is not { } me) return;
        if (!_rideSeatOk && me.Seated)
        {
            _rideSeatOk = me.Cart == rc.Cart && me.Seat is >= 0 and <= 3;
            GD.Print($"[net] ride: seated here in seat {me.Seat} ({(me.Cart == rc.Cart ? "the party's cart" : "another cart")}) as the cart is {rc.State.Mode}");
            if (!_rideSeatOk) _rideSeatFail ??= $"the server seated our hero in seat {me.Seat} of {(me.Cart == rc.Cart ? "the cart" : "something else")}";
        }
        if (_rideSeatOk && !_rideLandOk && _rideSeatFail == null && Dungeon.Rails.RailNet.Instance?.LastLocalLanding is { } landing)
        {
            var off = me.GlobalPosition.DistanceTo(landing.At);
            var after = NetSession.Instance.Now - landing.When;
            if (off <= 0.5f && !me.Seated)
            {
                _rideLandOk = true;
                GD.Print(FormattableString.Invariant($"[net] ride: set down within {off:0.00} m of the server's landing ({landing.At.X:0.00}, {landing.At.Y:0.00}, {landing.At.Z:0.00}) {after:0.00} s after its SeatMsg"));
            }
            else if (after > 0.5) _rideSeatFail ??= FormattableString.Invariant($"0.5 s after the server set our hero down it stood {off:0.00} m from the landing (seated here: {me.Seated})");
        }
    }

    // riding (plan 5 task 5): the client's side of the boarding, the pump, the brake and the get-out
    private int _ridePhase;
    private double _rideAskedAt = -1, _rideRolledAt = -1, _rideBrakeAt = -1;
    private bool _rideSawPumping, _rideBraked, _rideBrakeReleased;

    /// <summary>Ask to board through the intent once our hero stands within reach and the cart offers it (again every 2 s until seated).</summary>
    private void TryBoard(NetSession net, Dungeon.Rails.RailController rc, Party.Adventurer me, string where)
    {
        if (me.Seated || me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) > 3f) return;
        var prompt = rc.Cart.Prompt;
        if (prompt != "Board the cart" || (_rideAskedAt >= 0 && _clock - _rideAskedAt < 2)) return;
        if (NetRefResolver.RefOf(rc.Cart) is not { } cartRef) { Finish("[net]", false, "ride: the party's cart has no net ref here"); net.Disconnect("test finished"); return; }
        _rideAskedAt = _clock;
        net.Send(new InteractIntent(cartRef));
        GD.Print($"[net] ride: the cart offers \"{prompt}\" here at {where}; asked to board through the intent ({cartRef})");
    }

    /// <summary>
    /// Step 52's riding, every tick: board at the landing (the server puts us beside the cart), pump out of it with the stick forward
    /// (our RideIntent) until the server's snapshot shows our hero pumping, brake for 0.6 s on the way, and get out at stop 1 through the
    /// intent; then the props (<see cref="RideProps"/>). True when the step should wait (a failure was reported).
    /// </summary>
    private bool RideRiding(NetSession net, ClientWorld client, Main main, Dungeon.Rails.RailController rc)
    {
        if (main.Party.Local is not { } me) return false;
        switch (_ridePhase)
        {
            case 0: // the landing
                if (me.Seated && me.Cart == rc.Cart)
                {
                    _ridePhase = 1;
                    main.Party.ForcedMoveInput = new Vector2(0, -1); // the stick forward: pump
                    GD.Print($"[net] ride: boarded through the intent; the prompt here now says \"{rc.Cart.Prompt}\"; pumping (stick forward)");
                    if (rc.Cart.Prompt != "Get out") { Finish("[net]", false, $"ride: seated, the cart's prompt here says \"{rc.Cart.Prompt}\" (want Get out)"); net.Disconnect("test finished"); return true; }
                    return false;
                }
                if (rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == 0) TryBoard(net, rc, me, "the landing");
                return false;
            case 1: // ride 1 with us aboard
                if (rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 0)
                {
                    if (_rideRolledAt < 0) _rideRolledAt = _clock;
                    if (!_rideSawPumping && !_rideBraked && client.ServerSaysPumping && me.Pumping)
                    {
                        _rideSawPumping = true;
                        GD.Print($"[net] ride: the server shows our hero pumping (snapshot Pumping) {_clock - _rideRolledAt:0.00} s after the cart left");
                    }
                    if (!_rideBraked && _rideSawPumping && _clock - _rideRolledAt >= 2.0)
                    {
                        _rideBraked = true;
                        _rideBrakeAt = _clock;
                        main.Party.ForcedMoveInput = new Vector2(0, 1); // the stick back: brake
                        GD.Print(FormattableString.Invariant($"[net] ride: braking (stick back) at V={rc.State.V:0.00} m/s"));
                    }
                    if (_rideBraked && !_rideBrakeReleased && _clock - _rideBrakeAt >= 0.6)
                    {
                        _rideBrakeReleased = true;
                        main.Party.ForcedMoveInput = new Vector2(0, -1);
                        GD.Print(FormattableString.Invariant($"[net] ride: brake let go at V={rc.State.V:0.00} m/s; pumping again"));
                    }
                }
                if (rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == 1 && me.Seated)
                {
                    main.Party.ForcedMoveInput = null;
                    if (rc.GateAt(1) is { } downGate) GD.Print($"[net] ride: at stop 1 its gate is {(downGate.Up ? "up" : "down")} here and the winch offers \"{downGate.Winch?.Prompt}\"");
                    var prompt = rc.Cart.Prompt;
                    // cage 1 is locked: the cage holds the cart (CartFlags.CageHeld) and the seat offers the hold; a tap still gets out
                    if (prompt != Dungeon.Rails.CageHoldRules.SeatedPrompt || NetRefResolver.RefOf(rc.Cart) is not { } cartRef) { Finish("[net]", false, $"ride: at stop 1 the cart's prompt here says \"{prompt}\" (want {Dungeon.Rails.CageHoldRules.SeatedPrompt})"); net.Disconnect("test finished"); return true; }
                    GD.Print($"[net] ride: at stop 1 the cart offers \"{prompt}\" here (cage 1 locked)");
                    net.Send(new InteractIntent(cartRef));
                    _ridePhase = 2;
                    GD.Print("[net] ride: at stop 1; asked to get out through the intent");
                }
                return false;
            case 2: // set down at stop 1
                if (!me.Seated) { _ridePhase = 3; _ridePhaseAt = _clock; }
                return false;
            default:
                return RideProps(net, main, rc, me);
        }
    }

    // the props (plan 5 task 6): the client's side of the gate, the keys, the cage, the revive, the second boarding and the switch
    private double _ridePhaseAt, _rideUsedAt = -1;
    private string _rideCagePrompt = "";
    private Npc? _rideDownedHere;
    private bool _rideThrowAsked;
    private float _rideThrowDist;

    /// <summary>Use <paramref name="target"/> through the intent (its RefOf) when it offers <paramref name="prompt"/> and our hero stands within 3 m of it; again every 2 s until the server's answer shows here.</summary>
    private void UseProp(NetSession net, Party.IInteractable target, Node3D node, Party.Adventurer me, Func<string, bool> prompt, string what)
    {
        var offer = target.Prompt;
        var dist = me.GlobalPosition.DistanceTo(node.GlobalPosition);
        if (!prompt(offer) || dist > 3f || (_rideUsedAt >= 0 && _clock - _rideUsedAt < 2)) return;
        if (NetRefResolver.RefOf(target) is not { } netRef) { Finish("[net]", false, $"ride: {what} has no net ref here"); net.Disconnect("test finished"); return; }
        _rideUsedAt = _clock;
        net.Send(new InteractIntent(netRef));
        GD.Print(FormattableString.Invariant($"[net] ride: {what} offers \"{offer}\" here, {dist:0.00} m away; used through the intent ({netRef})"));
    }

    private bool RideFail(NetSession net, string why)
    {
        Finish("[net]", false, $"ride: {why}");
        net.Disconnect("test finished");
        return true;
    }

    /// <summary>
    /// Phases 3-9 of step 52: gate 1 rises here (its PropEvt, the winch no longer offered); the orc's keys (if this branch has them)
    /// and cage 1 are used through the intent with the prompts the shared rules give; a crewman the server downs in the open cage is
    /// revived through the intent ("Revive {name}"); our hero boards again at stop 1 and throws ride 3's switch from the seat as the
    /// lever passes (the server's lever, by its NetRefs name); the cart crosses here (the server's CrossOut). True when it failed.
    /// </summary>
    private bool RideProps(NetSession net, Main main, Dungeon.Rails.RailController rc, Party.Adventurer me)
    {
        if (Adventure.Objectives.ObjectiveController.Instance is not Adventure.Objectives.CrewRescueController crew || main.Builder.FloorRoot is not { } root) return false;
        var wagon = crew.Wagons.FirstOrDefault(w => w.Stop == 1);
        if (wagon == null) return RideFail(net, "no cage wagon at stop 1 here");
        if (_ridePhase >= 7) RideLeaveCrew(net, rc, me, "the rider"); // seated again from stop 1 on: stop 2's cage is left behind
        if (_ridePhase is >= 3 and <= 7 && _clock - _ridePhaseAt > 60) return RideFail(net, $"stuck at the props' phase {_ridePhase} (gate 1 up {rc.GateAt(1)?.Up}, keys {crew.OrcKeys}, cage open {wagon.Open}, wagon prompt \"{wagon.Prompt}\", seated {me.Seated})");
        switch (_ridePhase)
        {
            case 3: // gate 1 rises: its PropEvt
                if (rc.GateAt(1) is not { Up: true } gate) return false;
                GD.Print($"[net] ride: gate 1 is up here (its PropEvt); the winch offers \"{gate.Winch?.Prompt}\"");
                if (gate.Winch?.Prompt is { Length: > 0 }) return RideFail(net, "gate 1 is up but its winch still offers a crank");
                // the props this pass uses name themselves and resolve back to themselves here (RefOf, then the resolver)
                var named = new List<string>();
                foreach (var prop in new Party.IInteractable?[] { gate.Winch, wagon, crew.Keyring })
                {
                    if (prop is not Node3D node) continue;
                    var netRef = NetRefResolver.RefOf(prop);
                    if (netRef == null || NetRefResolver.Resolve(root, netRef) != node) return RideFail(net, $"{node.Name} names itself {netRef ?? "nothing"}, which does not resolve back to it");
                    named.Add(netRef);
                }
                GD.Print($"[net] ride: the props name themselves and resolve back here: {string.Join(", ", named)}");
                Next(4);
                return false;
            case 4: // the orc's keys
                if (crew.Keyring is not { } ring) { GD.Print("[net] ride: no orc's keyring on this branch"); Next(5); return false; }
                if (crew.OrcKeys) { GD.Print("[net] ride: took the orc's keys through the intent; the keyring offers nothing now"); Next(5); return false; }
                UseProp(net, ring, ring, me, p => p == "Take the orc's keys", "the orc's keyring");
                return false;
            case 5: // cage 1
                if (wagon.Open) { GD.Print($"[net] ride: opened cage 1 through the intent (it offered \"{_rideCagePrompt}\")"); Next(6); return false; }
                if (wagon.Prompt is { Length: > 0 } cagePrompt) _rideCagePrompt = cagePrompt;
                UseProp(net, wagon, wagon, me, p => p.StartsWith("Open the cage") || p == "Unlock the cage", "cage 1");
                return false;
            case 6: // a downed crewman in the open cage
                if (_rideDownedHere == null)
                {
                    foreach (var npc in crew.Crew)
                        if (IsInstanceValid(npc) && npc.Downed && npc.Ride == wagon) { _rideDownedHere = npc; break; }
                    if (_rideDownedHere == null) return false;
                    GD.Print($"[net] ride: {_rideDownedHere.NpcName} is down in cage 1 here; the cage offers \"{wagon.Prompt}\"");
                }
                if (!_rideDownedHere.Downed) { GD.Print($"[net] ride: revived {_rideDownedHere.NpcName} through the intent"); Next(7); return false; }
                if (!_rideBleedOk) return RideBleedCheck(net, crew);
                UseProp(net, wagon, wagon, me, p => p == $"Revive {_rideDownedHere.NpcName}", "cage 1");
                return false;
            case 7: // board again at stop 1
                if (me.Seated && me.Cart == rc.Cart) { GD.Print($"[net] ride: boarded again at stop 1 in seat {me.Seat}"); Next(8); return false; }
                if (rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == 1) TryBoard(net, rc, me, "stop 1");
                return false;
            case 8: // ride 3: throw the switch from the seat as the seated window offers its lever, beyond plain reach
            {
                if (_rideSwitchHere < 0 && Dungeon.Rails.RailEncounterController.Instance is { } enc && IsInstanceValid(enc)) _rideSwitchHere = enc.PieceIndexOf("switch_out", 3);
                if (NetRefResolver.Resolve(root, NetRefs.Switch(_rideSwitchHere)) is not Dungeon.Rails.TrackSwitch lever) return RideFail(net, $"ride 3's switch ({NetRefs.Switch(_rideSwitchHere)}) does not resolve here");
                if (rc.Crossing && rc.StopIndex == 2)
                {
                    GD.Print(FormattableString.Invariant($"[net] ride: the cart crosses here (CrossOut set, S={rc.State.S:0.0}); the lever shows {(lever.Thrown ? "thrown" : "straight")}, thrown through our intent at {_rideThrowDist:0.00} m"));
                    if (!_rideThrowAsked) return RideFail(net, "the cart crossed before we threw the switch");
                    Next(9);
                    return false;
                }
                if (!me.Seated || rc.StopIndex != 2 || rc.State.Mode != Dungeon.Rails.CartMode.Rolling || _rideThrowAsked || Main.Instance.Party.LookingAt != lever) return false;
                var dist = me.GlobalPosition.DistanceTo(lever.GlobalPosition);
                var ahead = lever.LeverS - rc.Party.FrontS;
                var prompt = lever.Prompt;
                if (prompt != "Throw the switch") return RideFail(net, $"the seated window offers ride 3's lever as \"{prompt}\" (want Throw the switch)");
                if (dist <= Dungeon.Rails.PropPromptRules.InteractReach + Dungeon.Rails.PropPromptRules.SeatedLagCap) return RideFail(net, FormattableString.Invariant($"the seated window first offered ride 3's lever {dist:0.00} m off, inside the plain reach"));
                _rideThrowAsked = true;
                _rideThrowDist = dist;
                Main.Instance.Party.VrUse(true); // the dispatch E uses: our TryInteract sends the lever's intent
                Main.Instance.Party.VrUse(false);
                GD.Print(FormattableString.Invariant($"[net] ride: ride 3's lever offers \"{prompt}\" in the seated window {ahead:0.0} m before the lever, {dist:0.00} m from our seat at V={rc.State.V:0.00} m/s; thrown through the intent from the seat ({NetRefs.Switch(_rideSwitchHere)})"));
                return false;
            }
            default:
                return false;
        }
    }

    private int _rideSwitchHere = -1;

    /// <summary>The stops whose prisoners this client left behind through the intent (once per stop).</summary>
    private readonly HashSet<int> _rideLeftAt = new();

    /// <summary>
    /// Seated in the party's cart held by its stop's locked cage (the server's CartFlags.CageHeld) with the seat offering the hold: leave
    /// the prisoners behind through the held intent (hold:cart:0), once per stop, so the countdown starts as soon as the hold shows (the
    /// RIDE pass departs stops 2 and 3 with their cages locked, as before the cage hold).
    /// </summary>
    private void RideLeaveCrew(NetSession net, Dungeon.Rails.RailController rc, Party.Adventurer me, string who)
    {
        if (me.Cart != rc.Cart || !rc.CageHeld) return;
        var prompt = rc.Cart.Prompt;
        if (prompt != Dungeon.Rails.CageHoldRules.SeatedPrompt || !_rideLeftAt.Add(rc.StopIndex)) return;
        net.Send(new InteractIntent(NetRefs.Hold(NetRefs.Cart)));
        GD.Print($"[net] ride: left the prisoners at stop {rc.StopIndex} behind through the intent (the prompt read \"{prompt}\"); {who}");
    }

    // crew, gates and set pieces (plan 5 task 7): the bleed label, the wagon coupled at the departure
    private bool _rideBleedOk, _rideTrainTold;
    private string? _rideBleedFirst;
    private int _rideBleedFirstN = -1;
    private double _rideBleedAt;

    /// <summary>The whole seconds a bleed label shows ("Hob Tallow (down 57s)" → 57), or −1.</summary>
    private static int BleedNumber(string label)
    {
        var at = label.LastIndexOf("(down ", StringComparison.Ordinal);
        if (at < 0) return -1;
        var end = label.IndexOf('s', at + 6);
        return end > at + 6 && int.TryParse(label.AsSpan(at + 6, end - at - 6), out var n) ? n : -1;
    }

    /// <summary>
    /// Phase 6, before the revive: the downed crewman's label counts down here (two numbers at least a second apart, the later smaller),
    /// and the server's objective state has named his seconds left (its CrewDownedLeft), which the label agrees with. False while it waits.
    /// </summary>
    private bool RideBleedCheck(NetSession net, Adventure.Objectives.CrewRescueController crew)
    {
        var downed = _rideDownedHere!;
        var text = downed.LabelText;
        var n = BleedNumber(text);
        if (_rideBleedFirst == null)
        {
            if (n < 0) return false;
            _rideBleedFirst = text;
            _rideBleedFirstN = n;
            _rideBleedAt = _clock;
            return false;
        }
        if (n < 0) return RideFail(net, $"{downed.NpcName}'s label went from \"{_rideBleedFirst}\" to \"{text}\" while still down");
        var synced = crew.NetDownedLeft is { } left && left.TryGetValue(downed.Id, out var secondsLeft) ? secondsLeft : float.NaN;
        if (_clock - _rideBleedAt < 1.2 || n >= _rideBleedFirstN || float.IsNaN(synced)) return false;
        var serverWhole = (int)MathF.Ceiling(synced);
        if (n > serverWhole || n < serverWhole - 3) return RideFail(net, $"{downed.NpcName}'s label shows {n} s here, the server's last state {synced:0.0} s");
        _rideBleedOk = true;
        GD.Print(FormattableString.Invariant($"[net] ride: {downed.NpcName}'s label counts down here: \"{_rideBleedFirst}\" -> \"{text}\" over {_clock - _rideBleedAt:0.0} s (the server's objective state: {synced:0.0} s left)"));
        return false;
    }

    /// <summary>
    /// Every tick of step 52 after we boarded again at stop 1: as the cart leaves stop 1 the opened cage wagon couples on behind it here
    /// (the cart's Train), within 0.5 s of the server's departure. Returns a failure, or null.
    /// </summary>
    private string? RideTrainCheck(ClientWorld client, Dungeon.Rails.RailController rc)
    {
        if (_rideTrainTold || _ridePhase < 8 || rc.StopIndex != 1 || rc.State.Mode != Dungeon.Rails.CartMode.Rolling || rc.NetDepartedAt < 0) return null;
        if (rc.Train.Count == 0)
            return client.ServerNow - rc.NetDepartedAt > 2 ? "no wagon was coupled behind the cart here 2 s after it left stop 1" : null;
        _rideTrainTold = true;
        var after = rc.NetTrainChangedAt - rc.NetDepartedAt;
        GD.Print(FormattableString.Invariant($"[net] ride: the cage wagon coupled here {after:0.000} s after the cart left stop 1 (Train [{string.Join(", ", rc.Train.Select(w => w.Stop))}])"));
        if (after < 0) return FormattableString.Invariant($"the wagon was coupled here {-after:0.000} s before the cart left stop 1 (the train must come with or after the departure)");
        return after <= 0.5 ? null : FormattableString.Invariant($"the wagon coupled here {after:0.000} s after the cart left stop 1 (over 0.5 s)");
    }

    private void Next(int phase)
    {
        _ridePhase = phase;
        _ridePhaseAt = _clock;
        _rideUsedAt = -1;
    }

    /// <summary>
    /// The client's half of the RIDE pass. Step 50: the server's floor 12 is built here with its set pieces (beams, switches,
    /// hammers, pours); step 51: for at least 10 s every prop check matches (the rail part included) and none asks for the props
    /// again, and the spur lever the server throws meanwhile shows here (its PropEvt); step 52: the rides with the seats, the riding and
    /// the props; step 53: still seated at stop 3, leave the game seated.
    /// </summary>
    private void RideClientTick(NetSession net, ClientWorld client, Main main)
    {
        FightBoarders(main);
        RideSoundSample(client);
        switch (_step)
        {
            case 50:
            {
                if (Game.Instance.CurrentFloor != 12 || main.Builder.CurrentFloor != 12 || main.Rebuilding || main.Builder.FloorRoot is not { } root
                    || Dungeon.Rails.RailEncounterController.Instance is not { } enc || !IsInstanceValid(enc) || enc.GetParent() != root)
                {
                    if (_clock > 60) Finish("[net]", false, $"ride: floor 12 never built here (floor {Game.Instance.CurrentFloor}, built {main.Builder.CurrentFloor})");
                    return;
                }
                int Named(string prefix) => root.GetChildren().Count(n => n.Name.ToString().StartsWith(prefix));
                var beams = Named("Beam_");
                var switches = Named("Switch_");
                var pours = Named("Pour_");
                var hammers = root.GetChildren().Where(n => n.Name.ToString().StartsWith("Hammers_")).Sum(h => h.GetChildren().OfType<Dungeon.Rails.DropHammer>().Count(d => d.Name.ToString().StartsWith("Hammer")));
                var rail = PropSync.Capture(root).Rail;
                GD.Print($"[net] ride: set pieces on the client: {enc.SetPieceCount} built, beams={beams} switches={switches} hammers={hammers} pours={pours}; rail props {(rail != null ? "captured" : "MISSING")}");
                if (beams == 0 || switches == 0 || hammers == 0 || pours == 0 || rail == null) { Finish("[net]", false, $"ride: the client's floor 12 lacks set pieces (beams={beams} switches={switches} hammers={hammers} pours={pours}, rail {(rail != null ? "yes" : "no")})"); return; }
                _ridePropChecks = client.PropChecksMatched;
                _rideAsks = client.Trace.Count(t => t == "prop-check");
                _clock = 0; _step = 51;
                return;
            }
            case 51:
            {
                if (client.Trace.Count(t => t == "prop-check") > _rideAsks) { Finish("[net]", false, "ride: the client's props differed from the server's on floor 12 (a prop-check asked again)"); net.Disconnect("test finished"); return; }
                if (_clock < 10) return;
                // ride 4's spur lever, by its wire name through the resolver (the server threw it)
                var spur = Dungeon.Rails.RailEncounterController.Instance is { } spurs && IsInstanceValid(spurs) ? spurs.PieceIndexOf("spur_switch", 4) : -1;
                var lever = main.Builder.FloorRoot is { } leverRoot && spur >= 0 ? NetRefResolver.Resolve(leverRoot, NetRefs.Spur(spur)) as Dungeon.Rails.TrackSwitch : null;
                if (lever is not { Thrown: true }) { if (_clock > 20) { Finish("[net]", false, $"ride: the server's thrown spur lever ({(spur >= 0 ? NetRefs.Spur(spur) : "none")}) never showed here (no PropEvt)"); net.Disconnect("test finished"); } return; }
                var matched = client.PropChecksMatched - _ridePropChecks;
                if (matched < 2) { Finish("[net]", false, $"ride: only {matched} prop check(s) reached us in 10 s on floor 12"); net.Disconnect("test finished"); return; }
                GD.Print($"[net] ride: the server's thrown switch shows here (ride 4's spur lever {NetRefs.Spur(spur)}, by PropEvt)");
                GD.Print($"[net] ride: prop hash matches the server's ({matched} checks matched, no prop-check in {_clock:0} s)");
                _ridePropMatched = matched;
                _clock = 0; _step = 52;
                return;
            }
            case 52:
            {
                // the cart rides stops 0 to 3 on the server; here it follows (the trace lines on the server-time grid are paired by the script)
                if (Dungeon.Rails.RailController.Instance is not { } rc || !IsInstanceValid(rc)) { if (_clock > 5) { Finish("[net]", false, "ride: no rail controller on the client"); net.Disconnect("test finished"); } return; }
                if (rc.Following && rc.State.Mode == Dungeon.Rails.CartMode.Rolling) _rideSawRolling = true;
                if (Dungeon.Rails.RailNet.Instance is { } rn && IsInstanceValid(rn)) _rideMostCarts = Math.Max(_rideMostCarts, rn.ClientCartCount);
                RideSeatChecks(main, rc);
                _rideSeatFail ??= RideTrainCheck(client, rc);
                if (_rideSeatFail != null) { Finish("[net]", false, $"ride: {_rideSeatFail}"); net.Disconnect("test finished"); return; }
                if (RideRiding(net, client, main, rc)) return;
                if (_rideSawRolling && rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex >= 3)
                {
                    GD.Print($"[net] ride: the cart followed the server's to stop {rc.StopIndex} (S={rc.State.S:0.0}); up to {_rideMostCarts} other cart(s) here at once");
                    if (!rc.Following || _rideMostCarts == 0) { Finish("[net]", false, $"ride: the cart never followed a state (following={rc.Following}) or no slag cart ever showed here ({_rideMostCarts})"); net.Disconnect("test finished"); return; }
                    if (!_rideCrewOk || !_rideSeatOk || !_rideLandOk) { Finish("[net]", false, $"ride: seats: crew in their wagons {(_rideCrewOk ? "yes" : "NO")}, our hero seated {(_rideSeatOk ? "yes" : "NO")}, set down at the server's landing {(_rideLandOk ? "yes" : "NO")}"); net.Disconnect("test finished"); return; }
                    if (!_rideSawPumping || !_rideBraked) { Finish("[net]", false, $"ride: riding: the server showed our pump {(_rideSawPumping ? "yes" : "NO")}, we braked {(_rideBraked ? "yes" : "NO")}"); net.Disconnect("test finished"); return; }
                    if (_ridePhase < 9) { Finish("[net]", false, $"ride: props: reached stop {rc.StopIndex} in the props' phase {_ridePhase} (9: the switch thrown and crossed)"); net.Disconnect("test finished"); return; }
                    if (!_rideBleedOk || !_rideTrainTold) { Finish("[net]", false, $"ride: crew: the bleed label counted down here {Yes(_rideBleedOk)}, the wagon coupled at stop 1's departure {Yes(_rideTrainTold)}"); net.Disconnect("test finished"); return; }
                    _clock = 0; _step = 53; // stay seated at stop 3: ride 4 with the train while the joiner chases it, then leave seated
                    return;
                }
                if (_clock > 300) { Finish("[net]", false, $"ride: the cart never reached stop 3 here (stop {rc.StopIndex}, mode {rc.State.Mode}, following={rc.Following})"); net.Disconnect("test finished"); }
                return;
            }
            case 53:
            {
                // our hero rode rides 2 and 3 seated and stays in the cart at stop 3 while the joiner gets out: the train leaves on ride 4 with
                // us (we pump: the kiln's jump wants speed); at the end of the line the joiner's chaser runs up behind the train and goes, and
                // we leave the game seated
                if (Dungeon.Rails.RailController.Instance is not { } rc || !IsInstanceValid(rc) || main.Party.Local is not { } me) return;
                if (!me.Seated || me.Cart != rc.Cart) { Finish("[net]", false, $"ride: our hero is not in the cart at stop {rc.StopIndex} (seated {me.Seated})"); net.Disconnect("test finished"); return; }
                RideLeaveCrew(net, rc, me, "the rider"); // stop 3's cage is left behind: the train leaves on ride 4 when its gate lifts
                if (rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 3)
                {
                    if (!_rideFourTold) { _rideFourTold = true; GD.Print($"[net] ride: the train leaves stop 3 on ride 4 with us aboard; pumping (stick forward)"); }
                    main.Party.ForcedMoveInput = new Vector2(0, -1);
                }
                if (Dungeon.Rails.RailNet.Instance?.ClientChaser is { } chaser && !_rideSawChaser)
                {
                    _rideSawChaser = true;
                    GD.Print(FormattableString.Invariant($"[net] ride: a chaser ({chaser.Cart.Name}) shows here at S={chaser.Follow.S:0.0}, the train at stop {rc.StopIndex} S={rc.State.S:0.0}"));
                }
                if (rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex >= RideLastStop && _rideAtEndAt < 0)
                {
                    main.Party.ForcedMoveInput = null;
                    _rideAtEndAt = _clock;
                    GD.Print($"[net] ride: the train reached stop {rc.StopIndex} (the end of the line) with us seated; waiting for the joiner's chaser");
                }
                if (_rideAtEndAt >= 0 && _rideSawChaser && Dungeon.Rails.RailNet.Instance?.ClientChaser == null)
                {
                    if (RideSoundVerdict(client, joiner: false) is { } unheard) { Finish("[net]", false, $"ride: sound: {unheard}"); net.Disconnect("test finished"); return; }
                    GD.Print($"[net] ride: seated at stop {rc.StopIndex} in seat {me.Seat}; the joiner's chaser came and went; leaving the game seated; {client.ShakesHeard} shake(s) from the server reached this screen");
                    Finish("[net]", true, $"ride: floor 12's set pieces built on the client and its props hash as the server's ({_ridePropMatched} checks); the cart followed the server's to stop {rc.StopIndex} with {_rideMostCarts} other cart(s) at most; {_rideCrewSeated} crew seated in their wagons; our hero boarded through the intent, pumped (shown by the server), braked, got out where the server put it; saw gate 1 rise, took the keys, opened cage 1 and revived {_rideDownedHere?.NpcName} through the intents; boarded again, threw ride 3's switch from the seat, crossed, rode ride 4 to the end of the line while the joiner chased the train in a spare, and leaves seated");
                    net.Disconnect("test finished");
                    return;
                }
                if (_rideAtEndAt >= 0 && _clock - _rideAtEndAt > 90) { Finish("[net]", false, $"ride: at the end of the line no chaser came and went here (seen {Yes(_rideSawChaser)})"); net.Disconnect("test finished"); return; }
                if (_clock > 200) { Finish("[net]", false, $"ride: ride 4 never reached the end of the line here (stop {rc.StopIndex}, mode {rc.State.Mode})"); net.Disconnect("test finished"); }
                return;
            }
            default:
                RideJoinerClient(net, client, main);
                return;
        }
    }

    // fighting back (the fix wave): seated in the party's cart, the RIDE pass's riders swing at the hounds on its rim and, from the seat,
    // at a hound on a coupled wagon's roof within reach (a player's blow reaches it as a companion's does). The server swings them
    private double _fightAt;
    private int _fightSwings;

    private void FightBoarders(Main main)
    {
        if (main.Party?.Local is not { Seated: true, Downed: false } me || Dungeon.Rails.RailController.Instance is not { } rc || !IsInstanceValid(rc) || me.Cart != rc.Cart) return;
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _fightAt < 0.5) return;
        Enemies.Enemy? target = null;
        var best = float.MaxValue;
        foreach (var h in rc.Cart.Boarders)
            if (h != null && IsInstanceValid(h) && h.IsAlive && h.Carrier == rc.Cart && h.GlobalPosition.DistanceTo(me.GlobalPosition) is var d && d < best) { best = d; target = h; }
        foreach (var wagon in rc.Train)
        {
            if (!IsInstanceValid(wagon) || !wagon.Coupled) continue;
            foreach (var h in wagon.Boarders)
                if (h != null && IsInstanceValid(h) && h.IsAlive && h.Carrier == wagon && h.GlobalPosition.DistanceTo(me.GlobalPosition) is var d && d <= Characters.CompanionBrain.BoarderReach && d < best) { best = d; target = h; }
        }
        if (target == null) return;
        _fightAt = now;
        me.AimAt(target.GlobalPosition + Vector3.Up * 0.4f);
        me.Attack();
        if (_fightSwings++ % 10 == 0) GD.Print(FormattableString.Invariant($"[net] ride: fighting from the seat: swing {_fightSwings} at {target.DisplayName} on {target.Carrier?.Name} {best:0.00} m away; our hp {me.State.Hp:0}"));
    }

    // the joiner (plan 5 task 8): the RIDE pass's second client joins during ride 2
    private double _joinReadyAt = -1, _joinStateAt = -1, _joinSeatsAt = -1;
    // the first client on ride 4 (plan 5 task 9)
    private bool _rideFourTold, _rideSawChaser;
    private double _rideAtEndAt = -1;
    // the joiner left behind at stop 3 (plan 5 task 9)
    private bool _joinHeldTold, _joinChaserRolled, _joinGapTold, _joinHudTold;
    /// <summary>When the joiner's chaser was first seen rolling here (its bar is read 0.6 s on).</summary>
    private double _joinChaserRolledAt = -1;
    private double _joinSpareAskedAt = -1;
    // the joiner thrown at speed on ride 3, its floor resent mid-ride, and the wreck it watches at the end (plan 5 task 11)
    private double _joinRolledAt = -1, _joinThrowNow = -1, _joinResyncAt = -1;
    private float _joinThrowV;
    /// <summary>Ride 3 (plan 8): the joiner pressed "Set the switch back" from the seat as the first client's throw showed here, or saw its seated window open (and close without one).</summary>
    private bool _joinSetBackSent, _joinWindowSeen, _joinWindowClosed;
    private bool _joinWreckTold;
    private int _joinSpareStop = -1;
    // the joiner's heap halt (task 11): the most S rendered while the face stood ahead, where its stop is, and the largest backward step while rolling
    private float _heapFace = float.NaN, _heapMost = float.MinValue, _heapPrevS = -1f, _heapBack;
    private bool _heapSawRolling;
    // the last wreck (plan 6 final review): cart_wreck plays counted when the sound was judged, the resends seen before our ask, and the rebuilt Ready's server time
    private int _lastWreckSoundsAt, _lastWreckResyncs;
    private double _lastWreckReadyAt = -1, _lastWreckSeenAt = -1;

    // ---- the ride's sound on a client (plan 6 task 5): the party cart's roll from the synced state, no rail cue relayed, the wreck heard once ----

    private bool _soundListening, _soundOk;
    private int _soundWrecks;
    private double _soundLineAt = double.NegativeInfinity;

    private void OnPlayedAt(string cue, Vector3 at, bool ambient) { if (cue == "cart_wreck") _soundWrecks++; }

    /// <summary>The static test hook (AudioManager.PlayedAt) is taken off as this node leaves the tree: no static event holds it past exit.</summary>
    public override void _ExitTree() => Audio.AudioManager.PlayedAt -= OnPlayedAt;

    /// <summary>Each physics tick of a RIDE client: while the server's cart rolls here, a <c>[net] ride sound</c> line every 2 s (and at once the first time the roll reaches RollPitch(5)).</summary>
    private void RideSoundSample(ClientWorld client)
    {
        if (!_soundListening)
        {
            _soundListening = true;
            Audio.AudioManager.PlayedAt += OnPlayedAt;
        }
        if (Dungeon.Rails.RailController.Instance is not { } rc || !IsInstanceValid(rc) || !rc.Following || rc.State.Mode != Dungeon.Rails.CartMode.Rolling || rc.Cart.Sound is not { } sound) return;
        var rises = !_soundOk && sound.RollDbNow > -80f && sound.RollPitchNow >= Dungeon.Rails.RideSound.RollPitch(5f);
        if (!rises && _elapsed - _soundLineAt < 2.0) return;
        _soundLineAt = _elapsed;
        RideSoundLine(client, rc);
    }

    private void RideSoundLine(ClientWorld client, Dungeon.Rails.RailController rc)
    {
        var sound = rc.Cart.Sound;
        var on = sound != null && sound.RollDbNow > -80f;
        var pitch = sound?.RollPitchNow ?? 0f;
        if (on && rc.State.Mode == Dungeon.Rails.CartMode.Rolling && pitch >= Dungeon.Rails.RideSound.RollPitch(5f)) _soundOk = true;
        GD.Print(FormattableString.Invariant($"[net] ride sound roll={(on ? "on" : "off")} pitch={pitch:0.000} railsfx={client.RailSfxSeen} wreck={_soundWrecks}"));
    }

    /// <summary>The RIDE client's sound, judged before its RESULT: the roll heard at speed while the server's cart rolled, no rail cue relayed, and (the joiner, who watches it) the wreck heard once. Null, or why not.</summary>
    private string? RideSoundVerdict(ClientWorld client, bool joiner)
    {
        if (Dungeon.Rails.RailController.Instance is { } rc && IsInstanceValid(rc)) RideSoundLine(client, rc);
        if (!_soundOk) return FormattableString.Invariant($"the party cart's roll was never heard on with its pitch at RollPitch(5) = {Dungeon.Rails.RideSound.RollPitch(5f):0.000} or more while the server's cart rolled");
        if (client.RailSfxSeen != 0) return $"{client.RailSfxSeen} rail cue(s) came relayed as SfxMsg";
        if (joiner && _soundWrecks != 1) return $"the wreck with a spare played cart_wreck {_soundWrecks} time(s) here, not once";
        return null;
    }

    /// <summary>
    /// The joiner's half of the RIDE pass. Step 60: in game on floor 12 (its Welcome's floor) with the rails: its Ready's server time is
    /// noted; step 61: 1 s after it, the ride as it is: the cart's state followed and Rolling on ride 2, the first client's hero seated in
    /// the party's cart, the crew in their wagons and the train behind the cart (the script compares the seats, the train and the props'
    /// hash with the server's on the grid, and the cart's S within 1.5 m); step 62: put beside the cart at stop 2 by the server, board
    /// through the intent; step 63: ride 3 seated; 2 s after it rolls, at speed, get out through the intent; step 67 (task 11): thrown, we stand
    /// at the server's landing on the line behind the cart, and ask for the floor again (a resync mid-ride 3); step 68: the floor is rebuilt
    /// here; step 69: 1 s after that Ready, the ride as it is again (resynced-mid-ride ok); step 64 (task 9): on foot (the server puts us beside
    /// stop 3's spare once the train is held there), the spare offers nothing while the train is held there, take it through the intent once
    /// the train has left on ride 4; step 65: seated in the chaser, it halts within 3 m behind the train's last body here and we are set down
    /// (chase-done); step 66 (task 11): once the first client has left, the server wrecks the cart at the end of the line by a test hook and a
    /// spare takes over: we see the Wrecked state, then the cart held at the spare's stop (wreck-spare); then leave.
    /// </summary>
    private void RideJoinerClient(NetSession net, ClientWorld client, Main main)
    {
        FightBoarders(main);
        var rc = Dungeon.Rails.RailController.Instance is { } r && IsInstanceValid(r) ? r : null;
        var me = main.Party?.Local;
        switch (_step)
        {
            case 60:
                if (Game.Instance.CurrentFloor != 12 || main.Builder.CurrentFloor != 12 || main.Rebuilding || rc == null || Dungeon.Rails.RailNet.Instance == null || me == null)
                {
                    if (_clock > 60) { Finish("[net]", false, $"ride: the joiner never stood on floor 12 with its rails (floor {Game.Instance.CurrentFloor})"); net.Disconnect("test finished"); }
                    return;
                }
                _joinReadyAt = client.ServerNow;
                GD.Print(FormattableString.Invariant($"[net] ride: joined mid-ride; ready at t={_joinReadyAt:0.000} (server time); the cart here {(rc.Following ? $"follows the server's ({rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})" : "has no state yet")}"));
                _clock = 0; _step = 61;
                return;
            case 61:
            {
                if (rc == null || me == null) return;
                if (_joinStateAt < 0 && rc.Following) _joinStateAt = client.ServerNow; // the catch-up's cart state is here
                if (_joinSeatsAt < 0 && main.Party.Adventurers.Any(a => IsInstanceValid(a) && a != me && a.Cart == rc.Cart)) _joinSeatsAt = client.ServerNow;
                if (client.ServerNow - _joinReadyAt < 1.0) return;
                var crew = Adventure.Objectives.ObjectiveController.Instance as Adventure.Objectives.CrewRescueController;
                var inWagons = crew?.Wagons.Sum(w => w.Riders.Count) ?? 0;
                var others = main.Party.Adventurers.Count(a => IsInstanceValid(a) && a != me && a.Cart == rc.Cart);
                var midRide = rc.Following && rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 1;
                var why = new List<string>();
                if (!midRide) why.Add($"the cart here is {(rc.Following ? $"{rc.State.Mode} at stop {rc.StopIndex}" : "not following the server")} (want Rolling on ride 2)");
                if (others == 0) why.Add("nobody sits in the party's cart here");
                if (inWagons == 0) why.Add("no crew sit in their wagons here");
                if (rc.Train.Count == 0) why.Add("no wagon is coupled behind the cart here");
                var line = FormattableString.Invariant($"the cart's state came {_joinStateAt - _joinReadyAt:0.000} s and the first client's seat {_joinSeatsAt - _joinReadyAt:0.000} s after Ready; {client.ServerNow - _joinReadyAt:0.00} s after Ready the cart is {rc.State.Mode} at stop {rc.StopIndex} (S={rc.State.S:0.0}), {others} hero(es) in it, {inWagons} crew in the wagons, train [{string.Join(", ", rc.Train.Select(w => w.Stop))}]");
                if (why.Count > 0) { Finish("[net]", false, $"ride: the joiner: {line}: {string.Join("; ", why)}"); net.Disconnect("test finished"); return; }
                client.Trace.Add("joined-mid-ride ok");
                GD.Print($"[net] ride: joined-mid-ride ok: {line}");
                _clock = 0; _step = 62;
                return;
            }
            case 62: // put beside the cart at stop 2: board through the intent
                if (rc == null || me == null) return;
                RideLeaveCrew(net, rc, me, "the joiner");
                if (me.Seated && me.Cart == rc.Cart)
                {
                    GD.Print($"[net] ride: the joiner boarded at stop 2 through the intent in seat {me.Seat}");
                    _clock = 0; _step = 63;
                    return;
                }
                if (rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == 2) TryBoard(net, rc, me, "stop 2");
                if (rc.StopIndex >= 3 || _clock > 150) { Finish("[net]", false, $"ride: the joiner never boarded at stop 2 (stop {rc.StopIndex}, mode {rc.State.Mode})"); net.Disconnect("test finished"); }
                return;
            case 63: // ride 3 seated: the first client's throw shows here and we set it back from the seat at once (the server refuses it);
                     // 2 s after it rolls, at speed, once that press went (or the window closed without one), get out through the intent: thrown onto the line behind the cart
            {
                if (rc == null || me == null) return;
                RideLeaveCrew(net, rc, me, "the joiner");
                if (!me.Seated || me.Cart != rc.Cart) { Finish("[net]", false, $"ride: the joiner is not in the cart on ride 3 (stop {rc.StopIndex}, mode {rc.State.Mode})"); net.Disconnect("test finished"); return; }
                var rolling3 = rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 2 && !rc.Halted;
                if (rolling3 && _joinRolledAt < 0) _joinRolledAt = _clock;
                if (!_joinSetBackSent && !_joinWindowClosed && rc.StopIndex == 2 && Dungeon.Rails.RailEncounterController.Instance is { } enc3 && IsInstanceValid(enc3)
                    && main.Builder.FloorRoot is { } root3 && NetRefResolver.Resolve(root3, NetRefs.Switch(enc3.PieceIndexOf("switch_out", 3))) is Dungeon.Rails.TrackSwitch lever3)
                {
                    if (main.Party.LookingAt == lever3)
                    {
                        _joinWindowSeen = true;
                        if (lever3.Thrown)
                        {
                            if (lever3.Prompt != "Set the switch back") { Finish("[net]", false, $"ride: the first client's throw shows here, but the joiner's seat offers ride 3's lever as \"{lever3.Prompt}\" (want Set the switch back)"); net.Disconnect("test finished"); return; }
                            _joinSetBackSent = true;
                            main.Party.VrUse(true);
                            main.Party.VrUse(false);
                            GD.Print(FormattableString.Invariant($"[net] ride: the joiner presses \"Set the switch back\" from the seat as the first client's throw shows here ({lever3.LeverS - rc.Party.FrontS:0.0} m before the lever, {me.GlobalPosition.DistanceTo(lever3.GlobalPosition):0.00} m off)"));
                        }
                    }
                    else if (_joinWindowSeen || lever3.Taken) _joinWindowClosed = true;
                }
                if (rolling3 && _joinRolledAt >= 0 && _clock - _joinRolledAt >= 2 && rc.State.V >= 2.5f && (_joinSetBackSent || _joinWindowClosed))
                {
                    if (rc.Cart.Prompt != "Get out" || NetRefResolver.RefOf(rc.Cart) is not { } cartRef) { Finish("[net]", false, $"ride: on ride 3 the joiner's cart offers \"{rc.Cart.Prompt}\" (want Get out)"); net.Disconnect("test finished"); return; }
                    _joinThrowV = rc.State.V;
                    _joinThrowNow = net.Now;
                    net.Send(new InteractIntent(cartRef));
                    GD.Print(FormattableString.Invariant($"[net] ride: the joiner gets out of the rolling cart on ride 3 (V={rc.State.V:0.00} m/s here, S={rc.State.S:0.0}) through the intent, in seat {me.Seat}"));
                    _clock = 0; _step = 67;
                    return;
                }
                if (rc.StopIndex >= 3 || _clock > 120) { Finish("[net]", false, $"ride: the joiner's ride 3 never ran at speed here (stop {rc.StopIndex}, mode {rc.State.Mode}, V={rc.State.V:0.00}, set back pressed {_joinSetBackSent}, window seen {_joinWindowSeen} closed {_joinWindowClosed})"); net.Disconnect("test finished"); }
                return;
            }
            case 67: // thrown: at the server's landing, on the line behind the cart; then ask for the floor again mid-ride 3
            {
                if (rc == null || me == null) return;
                var landing = Dungeon.Rails.RailNet.Instance?.LastLocalLanding;
                if (me.Seated || landing is not { } l || l.When < _joinThrowNow)
                {
                    if (_clock > 3) { Finish("[net]", false, $"ride: the joiner was not let out of the rolling cart (seated {me.Seated}, a landing {(landing != null ? "old" : "none")})"); net.Disconnect("test finished"); }
                    return;
                }
                var off = me.GlobalPosition.DistanceTo(l.At);
                var since = net.Now - l.When;
                if (off > 0.5f)
                {
                    if (since > 0.5) { Finish("[net]", false, FormattableString.Invariant($"ride: thrown at speed, the joiner stands {off:0.00} m from the server's landing {since:0.00} s after it")); net.Disconnect("test finished"); }
                    return;
                }
                var onLine = main.Builder.Layout is { } lay && lay.IsRail(Dungeon.DungeonBuilder.WorldToCell(l.At));
                var behind = rc.State.S - Dungeon.Rails.CartRules.HalfLength;
                GD.Print(FormattableString.Invariant($"[net] ride: thrown at speed: the joiner (out at {_joinThrowV:0.00} m/s) stands within {off:0.00} m of the server's landing ({l.At.X:0.00}, {l.At.Y:0.00}, {l.At.Z:0.00}) {since:0.00} s after it, {(onLine ? "on the line" : "OFF THE LINE")} behind the cart, which rolls on (V={rc.State.V:0.00} m/s, its back at S={behind:0.0})"));
                if (!onLine || rc.State.Mode != Dungeon.Rails.CartMode.Rolling) { Finish("[net]", false, $"ride: thrown at speed, the joiner landed {(onLine ? "on" : "off")} the line with the cart {rc.State.Mode} (want on the line, the cart rolling on)"); net.Disconnect("test finished"); return; }
                client.Trace.Add("thrown-at-speed");
                client.TestAskFloorAgain("the RIDE pass: a resync mid-ride 3");
                GD.Print("[net] ride: the joiner asks for floor 12 again mid-ride 3 (a resync: the server sends the floor as it stands)");
                _clock = 0; _step = 68;
                return;
            }
            case 68: // the floor resent: rebuilt here
                if (!client.Trace.Contains("floor-resync"))
                {
                    if (_clock > 30) { Finish("[net]", false, "ride: the server never sent floor 12 again for the joiner's resync"); net.Disconnect("test finished"); }
                    return;
                }
                if (main.Rebuilding || main.Builder.CurrentFloor != 12 || rc == null || Dungeon.Rails.RailNet.Instance == null || me == null)
                {
                    if (_clock > 60) { Finish("[net]", false, "ride: the joiner's floor 12 never stood again after the resync"); net.Disconnect("test finished"); }
                    return;
                }
                _joinResyncAt = client.ServerNow;
                _joinStateAt = _joinSeatsAt = -1;
                GD.Print(FormattableString.Invariant($"[net] ride: resynced; ready at t={_joinResyncAt:0.000} (server time); the cart here {(rc.Following ? $"follows the server's ({rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})" : "has no state yet")}"));
                _clock = 0; _step = 69;
                return;
            case 69: // 1 s after the resync's Ready: the ride as it is again (the catch-up), mid-ride 3
            {
                if (rc == null || me == null) return;
                if (_joinStateAt < 0 && rc.Following) _joinStateAt = client.ServerNow;
                if (_joinSeatsAt < 0 && main.Party.Adventurers.Any(a => IsInstanceValid(a) && a != me && a.Cart == rc.Cart)) _joinSeatsAt = client.ServerNow;
                if (client.ServerNow - _joinResyncAt < 1.0) return;
                var crew = Adventure.Objectives.ObjectiveController.Instance as Adventure.Objectives.CrewRescueController;
                var inWagons = crew?.Wagons.Sum(w => w.Riders.Count) ?? 0;
                var others = main.Party.Adventurers.Count(a => IsInstanceValid(a) && a != me && a.Cart == rc.Cart);
                var midRide = rc.Following && rc.State.Mode == Dungeon.Rails.CartMode.Rolling && rc.StopIndex == 2;
                var why = new List<string>();
                if (!midRide) why.Add($"the cart here is {(rc.Following ? $"{rc.State.Mode} at stop {rc.StopIndex}" : "not following the server")} (want Rolling on ride 3)");
                if (others == 0) why.Add("nobody sits in the party's cart here");
                if (inWagons == 0) why.Add("no crew sit in their wagons here");
                if (rc.Train.Count == 0) why.Add("no wagon is coupled behind the cart here");
                if (me.Seated) why.Add("we sit in a cart here (we were thrown out of it)");
                var line = FormattableString.Invariant($"the cart's state came {_joinStateAt - _joinResyncAt:0.000} s and the first client's seat {_joinSeatsAt - _joinResyncAt:0.000} s after the resync's Ready; {client.ServerNow - _joinResyncAt:0.00} s after it the cart is {rc.State.Mode} at stop {rc.StopIndex} (S={rc.State.S:0.0}), {others} hero(es) in it, {inWagons} crew in the wagons, train [{string.Join(", ", rc.Train.Select(w => w.Stop))}]");
                if (why.Count > 0) { Finish("[net]", false, $"ride: the joiner after its resync: {line}: {string.Join("; ", why)}"); net.Disconnect("test finished"); return; }
                client.Trace.Add("resynced-mid-ride ok");
                GD.Print($"[net] ride: resynced-mid-ride ok: {line}");
                _clock = 0; _step = 64;
                return;
            }
            case 64: // out at stop 3 and left behind: take stop 3's spare once the train has gone
            {
                if (rc == null || me == null) return;
                if (me.Seated && me.Cart is Dungeon.Rails.MineCart { Kind: CartKind.Chaser } ridden)
                {
                    GD.Print($"[net] ride: the joiner sits in the chaser ({ridden.Name}) in seat {me.Seat}");
                    _clock = 0; _step = 65;
                    return;
                }
                if (me.Seated) { if (_clock > 5) { Finish("[net]", false, "ride: the joiner was not let out at stop 3"); net.Disconnect("test finished"); } return; }
                if (rc.SpareAt(3) is not { } spare) { Finish("[net]", false, "ride: stop 3 has no spare cart here"); net.Disconnect("test finished"); return; }
                var offer = spare.Prompt;
                var dist = me.GlobalPosition.DistanceTo(spare.GlobalPosition);
                var held = rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == 3;
                if (held && dist <= 3f && !_joinHeldTold)
                {
                    _joinHeldTold = true;
                    GD.Print(FormattableString.Invariant($"[net] ride: beside stop 3's spare ({dist:0.00} m) with the train held at stop 3 it offers \"{offer}\" here (want nothing)"));
                    if (offer != "") { Finish("[net]", false, $"ride: stop 3's spare offers \"{offer}\" while the train is held there"); net.Disconnect("test finished"); return; }
                }
                if (!held && rc.StopIndex >= 3 && offer == "Take the spare cart" && dist <= 3f && (_joinSpareAskedAt < 0 || _clock - _joinSpareAskedAt >= 2))
                {
                    if (NetRefResolver.RefOf(spare) is not { } spareRef || spareRef != NetRefs.Spare(3)) { Finish("[net]", false, $"ride: stop 3's spare has the net ref {NetRefResolver.RefOf(spare) ?? "none"} here (want {NetRefs.Spare(3)})"); net.Disconnect("test finished"); return; }
                    _joinSpareAskedAt = _clock;
                    net.Send(new InteractIntent(spareRef));
                    GD.Print(FormattableString.Invariant($"[net] ride: the train left stop 3 without us ({rc.State.Mode}, S={rc.State.S:0.0}); stop 3's spare offers \"{offer}\" here, {dist:0.00} m away; took it through the intent ({spareRef})"));
                }
                if (_clock > 180) { Finish("[net]", false, $"ride: the joiner never took stop 3's spare (offers \"{offer}\", {dist:0.00} m, the train {rc.State.Mode} at stop {rc.StopIndex}, told held {Yes(_joinHeldTold)})"); net.Disconnect("test finished"); }
                return;
            }
            case 65: // in the chaser: it runs up behind the train and halts there; we are set down
            {
                if (rc == null || me == null) return;
                if (Dungeon.Rails.RailNet.Instance?.ClientChaser is { } ch)
                {
                    if (ch.Follow.Mode == Dungeon.Rails.CartMode.Rolling && !_joinChaserRolled) { _joinChaserRolled = true; _joinChaserRolledAt = _clock; }
                    // the cart bar while it rolls with us in it (refreshed every 0.25 s): the chaser's own line, however far the train is
                    if (_joinChaserRolled && !_joinHudTold && ch.Follow.Mode == Dungeon.Rails.CartMode.Rolling && ReferenceEquals(me.Cart, ch.Cart) && _clock - _joinChaserRolledAt >= 0.6)
                    {
                        _joinHudTold = true;
                        GD.Print($"[net] ride: chaser hud \"{rc.HudText ?? "none"}\"");
                        if (rc.HudText != "Spare cart — after the train") { Finish("[net]", false, $"ride: the cart bar reads \"{rc.HudText ?? "nothing"}\" in the rolling chaser (want \"Spare cart — after the train\")"); net.Disconnect("test finished"); return; }
                    }
                    if (_joinChaserRolled && !_joinGapTold && ch.Follow.Mode == Dungeon.Rails.CartMode.Held && rc.State.Mode == Dungeon.Rails.CartMode.Held)
                    {
                        if (!_joinHudTold) { Finish("[net]", false, "ride: the chaser halted before its cart bar was read while it rolled"); net.Disconnect("test finished"); return; }
                        _joinGapTold = true;
                        var lastBack = rc.State.S - rc.Train.Count * Dungeon.Rails.RailController.WagonSpacing - Dungeon.Rails.CartRules.HalfLength;
                        var gap = lastBack - (ch.Follow.S + Dungeon.Rails.CartRules.HalfLength);
                        GD.Print(FormattableString.Invariant($"[net] ride: our chaser halted {gap:0.00} m behind the train's last body here (the train held at stop {rc.StopIndex}, S={rc.State.S:0.0}, {rc.Train.Count} wagon(s); the chaser at S={ch.Follow.S:0.0})"));
                        if (gap < 0f || gap > 3f) { Finish("[net]", false, FormattableString.Invariant($"ride: the chaser halted {gap:0.00} m behind the train's last body here (want within 3 m)")); net.Disconnect("test finished"); return; }
                    }
                }
                if (!me.Seated)
                {
                    if (!_joinGapTold) { Finish("[net]", false, "ride: the joiner was set down before its chaser halted behind the train"); net.Disconnect("test finished"); return; }
                    client.Trace.Add("chase-done");
                    var cell = Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition);
                    var offRails = main.Builder.Layout is { } layout && !layout.IsRail(cell);
                    GD.Print(FormattableString.Invariant($"[net] ride: chase-done: set down {(offRails ? "beside the lane" : "ON THE RAILS")} at ({me.GlobalPosition.X:0.00}, {me.GlobalPosition.Y:0.00}, {me.GlobalPosition.Z:0.00}), {me.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition):0.0} m from the train's cart"));
                    if (!offRails) { Finish("[net]", false, "ride: the joiner was set down on the rails after the chase"); net.Disconnect("test finished"); return; }
                    _clock = 0; _step = 66;
                    return;
                }
                if (_clock > 150) { Finish("[net]", false, $"ride: the chaser never ran up behind the train here (rolled {Yes(_joinChaserRolled)}, halted {Yes(_joinGapTold)}, the train {rc.State.Mode} at stop {rc.StopIndex})"); net.Disconnect("test finished"); }
                return;
            }
            case 66: // the first client leaves; the server wrecks the cart at the end of the line (a test hook) and a spare takes over: seen here
            {
                if (rc == null) return;
                if (rc.NetWrecksSeen == 0)
                {
                    if (_clock > 45) { Finish("[net]", false, $"ride: the cart was never shown wrecked here (stop {rc.StopIndex}, mode {rc.State.Mode})"); net.Disconnect("test finished"); }
                    return;
                }
                if (!_joinWreckTold)
                {
                    _joinWreckTold = true;
                    GD.Print(FormattableString.Invariant($"[net] ride: wreck: the cart showed Wrecked here (its state's server time t={rc.NetWreckedAt:0.000}, {rc.NetWrecksSeen} wreck(s) shown)"));
                }
                var k = rc.StopIndex;
                var takenOver = rc.Following && rc.State.Mode == Dungeon.Rails.CartMode.Held && k is >= 1 and <= 3 && rc.SparesUsed.Contains(k) && rc.Cart.Visible;
                if (!takenOver)
                {
                    if (_clock > 50) { Finish("[net]", false, $"ride: after the wreck no spare took over here (stop {k}, mode {rc.State.Mode}, spares used [{string.Join(",", rc.SparesUsed)}], shown {rc.Cart.Visible})"); net.Disconnect("test finished"); }
                    return;
                }
                var fromStop = Mathf.Abs(rc.State.S - rc.StopS[k]);
                client.Trace.Add("wreck-spare");
                GD.Print(FormattableString.Invariant($"[net] ride: wreck-spare: after the Wrecked state, spare {k} took over here: the cart Held at stop {k} (S={rc.State.S:0.0}, {fromStop:0.00} m from the stop), spares used [{string.Join(",", rc.SparesUsed)}], train [{string.Join(", ", rc.Train.Select(w => w.Stop))}]"));
                if (fromStop > 0.1f) { Finish("[net]", false, FormattableString.Invariant($"ride: the spare's cart stands {fromStop:0.00} m from stop {k} here")); net.Disconnect("test finished"); return; }
                _joinSpareStop = k;
                _clock = 0; _step = 70;
                return;
            }
            case 70: // put beside the spare's cart by the server: board it through the intent
                if (rc == null || me == null) return;
                RideLeaveCrew(net, rc, me, "the joiner");
                if (me.Seated && me.Cart == rc.Cart)
                {
                    GD.Print($"[net] ride: heap: the joiner boarded the spare's cart at stop {rc.StopIndex} through the intent in seat {me.Seat}");
                    _clock = 0; _step = 71;
                    return;
                }
                if (rc.State.Mode == Dungeon.Rails.CartMode.Held && rc.StopIndex == _joinSpareStop) TryBoard(net, rc, me, $"stop {_joinSpareStop} (the spare's cart)");
                if (_clock > 40) { Finish("[net]", false, $"ride: the joiner never boarded the spare's cart at stop {_joinSpareStop} (stop {rc.StopIndex}, mode {rc.State.Mode})"); net.Disconnect("test finished"); }
                return;
            case 71: // ride 3 again with its switch straight: the cart halts at the heap; our rendered S never passes the face nor steps back
            {
                if (rc == null || me == null || rc.Follower is not { HasState: true } f) return;
                RideLeaveCrew(net, rc, me, "the joiner");
                if (f.Mode == Dungeon.Rails.CartMode.Rolling)
                {
                    _heapSawRolling = true;
                    if (_heapPrevS >= 0f && f.V >= 0.5f) _heapBack = Mathf.Max(_heapBack, _heapPrevS - f.S);
                    _heapPrevS = f.S;
                }
                if (!float.IsNaN(f.BlockedS)) { _heapFace = f.BlockedS; _heapMost = Mathf.Max(_heapMost, f.S); }
                if (_heapSawRolling && rc.Halted && f.Mode == Dungeon.Rails.CartMode.Held)
                {
                    _heapMost = Mathf.Max(_heapMost, f.S);
                    var over = float.IsNaN(_heapFace) ? float.NaN : _heapMost - _heapFace;
                    GD.Print(FormattableString.Invariant($"[net] ride: heap: halted here at S={f.S:0.000}; the face's stop S={_heapFace:0.000}; the most S rendered here {_heapMost:0.000} ({over:+0.000;-0.000} m past it); the rendered S stepped back at most {_heapBack:0.000} m while rolling"));
                    if (float.IsNaN(_heapFace) || over > 0.01f || _heapBack > 0.01f) { Finish("[net]", false, FormattableString.Invariant($"ride: heap: the client's cart passed the heap's face by {over:0.000} m or stepped back {_heapBack:0.000} m (face seen {Yes(!float.IsNaN(_heapFace))})")); net.Disconnect("test finished"); return; }
                    client.Trace.Add("heap-halt");
                    if (rc.Cart.Prompt == "Get out" && NetRefResolver.RefOf(rc.Cart) is { } cartRef) net.Send(new InteractIntent(cartRef));
                    _clock = 0; _step = 72;
                    return;
                }
                if (_clock > 90) { Finish("[net]", false, $"ride: heap: the spare's cart never halted at the heap here (stop {rc.StopIndex}, mode {f.Mode}, halted {rc.Halted})"); net.Disconnect("test finished"); }
                return;
            }
            case 72: // out of the halted cart beside the lane (no throw); the sound judged (the wreck with a spare heard once)
                if (me != null && me.Seated && _clock < 5) return;
                if (RideSoundVerdict(client, joiner: true) is { } unheard) { Finish("[net]", false, $"ride: sound: {unheard}"); net.Disconnect("test finished"); return; }
                _lastWreckSoundsAt = _soundWrecks;
                _clock = 0; _step = 73;
                return;
            case 73: // the server wrecks the cart until no spare is left (heard here live), then we ask for the floor again
            {
                if (rc == null || !rc.Following || rc.State.Mode != Dungeon.Rails.CartMode.Wrecked || rc.Cart.Visible)
                {
                    if (_clock > 60) { Finish("[net]", false, $"ride: last wreck: the cart never stood wrecked and hidden here (mode {rc?.State.Mode}, shown {rc?.Cart.Visible})"); net.Disconnect("test finished"); }
                    return;
                }
                if (_lastWreckSeenAt < 0) _lastWreckSeenAt = _elapsed;
                if (_elapsed - _lastWreckSeenAt < 7) return; // the live wreck's states settle and its tumble (6 s) is gone before the resend
                var live = _soundWrecks - _lastWreckSoundsAt;
                GD.Print($"[net] ride: last wreck: the cart stands wrecked and hidden here with no spare left (spares used [{string.Join(",", rc.SparesUsed)}]); shown live: cart_wreck played {live} time(s) here");
                if (live < 1) { Finish("[net]", false, "ride: last wreck: the live wreck played no cart_wreck here"); net.Disconnect("test finished"); return; }
                _lastWreckSoundsAt = _soundWrecks;
                _lastWreckResyncs = client.Trace.Count(t => t == "floor-resync");
                client.TestAskFloorAgain("the RIDE pass: a resync after the last spare is spent");
                GD.Print("[net] ride: the joiner asks for floor 12 again with the cart wrecked and no spare left (its first cart state carries Wrecked)");
                _clock = 0; _step = 74;
                return;
            }
            case 74: // rebuilt: the first state (Wrecked) is a catch-up and plays nothing: no wreck shown, no cart_wreck, no tumble
            {
                if (client.Trace.Count(t => t == "floor-resync") <= _lastWreckResyncs || main.Rebuilding || main.Builder.CurrentFloor != 12 || rc == null || !rc.Following)
                {
                    if (_clock > 60) { Finish("[net]", false, "ride: last-wreck-resync: floor 12 never stood again with the cart's state after the resend"); net.Disconnect("test finished"); }
                    return;
                }
                if (_lastWreckReadyAt < 0) _lastWreckReadyAt = client.ServerNow;
                if (client.ServerNow - _lastWreckReadyAt < 1.0) return; // a second of states after the first
                var played = _soundWrecks - _lastWreckSoundsAt;
                var tumbles = rc.GetParent()?.GetChildren().Count(n => n.Name.ToString().Contains("CartTumble")) ?? 0;
                var line = $"after the floor's resend the cart here is {rc.State.Mode}, shown {rc.Cart.Visible}; wreck={rc.NetWrecksSeen} shown, cart_wreck played {played} time(s), {tumbles} tumble(s)";
                if (rc.State.Mode != Dungeon.Rails.CartMode.Wrecked || rc.Cart.Visible || rc.NetWrecksSeen != 0 || played != 0 || tumbles != 0)
                {
                    GD.Print($"[net] ride: last-wreck-resync FAIL: {line}");
                    Finish("[net]", false, $"ride: last-wreck-resync: a catch-up of a standing wreck must play nothing: {line}");
                    net.Disconnect("test finished");
                    return;
                }
                client.Trace.Add("last-wreck-resync ok");
                GD.Print($"[net] ride: last-wreck-resync ok: {line}");
                Finish("[net]", true, $"ride: the joiner joined mid-ride 2 and saw the ride as it is (joined-mid-ride ok), boarded at stop 2, was thrown from the cart at speed on ride 3 and stood at the server's landing, had floor 12 resent mid-ride 3 and saw the ride as it is again (resynced-mid-ride ok), took stop 3's spare through the intent after the train, its chaser halted behind the train (chase-done), saw the server's wreck and spare {_joinSpareStop}'s takeover (wreck-spare), rode ride 3 again in it and halted at the heap's face without passing it here (heap-halt), heard the last wreck live, and had the floor resent with the cart wrecked for good and heard and saw no wreck from that catch-up (last-wreck-resync ok)");
                net.Disconnect("test finished");
                return;
            }
        }
    }

    private void ClientTick(double delta)
    {
        var net = NetSession.Instance;
        var client = net.Client;
        if (client == null)
        {
            if (_step == 9 && _clock > 1.5) { _clock = 0; _step = 10; net.Connect(net.JoinUrl!, net.JoinName == "" ? "Player" : net.JoinName, net.JoinClass); Trace("reconnecting"); return; }
            if (_step == 9) { _clock += delta; return; }
            if (_elapsed > 30) Finish("[net]", false, $"no client ({net.Status})");
            return;
        }
        if (client.Trace.Contains("failed") || client.Trace.Contains("rejected") || client.Trace.Contains("dropped")) { Finish("[net]", false, $"connection: {net.Status}"); return; }
        var main = Main.Instance;
        _clock += delta;
        if (_step >= 50) { RideClientTick(net, client, main); return; }
        switch (_step)
        {
            case 0:
                if (!client.Trace.Contains("ready") || main.Party?.Local is not { } local) { if (_elapsed > 40) Finish("[net]", false, $"never ready ({net.Status})"); return; }
                if (!net.TestRejoinOnly && client.SnapshotsSeen >= 3 && !client.Trace.Contains("shop")) { Finish("[net]", false, "no shop sync arrived on joining"); return; }
                GD.Print($"[net] in game as member {client.MemberIndex}, floor {Game.Instance.CurrentFloor}, layout hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, {main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count()} enemies");
                // join floor: the client built the floor the Welcome named, and a tavern is a tavern with its merchant in it
                if (client.Welcome is not { } welcome || Game.Instance.CurrentFloor != welcome.CurrentFloor || !main.LayoutHashMatched)
                { Finish("[net]", false, $"join floor: client on floor {Game.Instance.CurrentFloor}, welcome said {client.Welcome?.CurrentFloor}, layout hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}"); return; }
                if (welcome.CurrentFloor == 0)
                {
                    var kind = main.Builder.Layout?.Kind;
                    var merchant = main.Builder.FloorRoot?.GetNodeOrNull<Npc>("Npc_merchant");
                    if (kind != Dungeon.Generation.LayoutKind.Tavern || merchant == null) { Finish("[net]", false, $"tavern join: layout {kind}, merchant {(merchant == null ? "missing" : "present")}"); return; }
                    if (!_tavernJoinSeen) GD.Print($"[net-client] tavern join OK: layout {kind}, merchant {merchant.NpcName} present");
                    _tavernJoinSeen = true; // step 0 runs again while the join-only check waits for snapshots
                }
                if (net.TestJoinOnly)
                {
                    if (welcome.CurrentFloor != 0) { Finish("[net]", false, $"--net-test-join-only expects a server in the tavern, got floor {welcome.CurrentFloor}"); return; }
                    if (client.SnapshotsSeen < 3) return; // the server has our Ready, so it handles intents from us
                    // the briefing is local to this screen: its deal node goes to the server, which pays the shared purse once
                    _goldBefore = main.Party.Gold;
                    net.Send(new DialogueNodeIntent("merchant", "deal"));
                    _clock = 0; _step = 20;
                    return;
                }
                if (net.TestRide)
                {
                    GD.Print($"[net] ride: ready on floor {Game.Instance.CurrentFloor}; {(net.TestRideJoin ? "the joiner, mid-ride" : "waiting for the server's floor 12")}");
                    _clock = 0; _step = net.TestRideJoin ? 60 : 50;
                    return;
                }
                if (net.TestRejoinOnly)
                {
                    if (client.SnapshotsSeen < 3) return; // snapshots only flow once the server has our Ready
                    // a resumed server: the character, its bag and its floor must all have come back from the save
                    var bag = main.Party.Bag.Items.Count();
                    var okBack = client.MemberIndex == 0 && bag >= 1 && Game.Instance.CurrentFloor == 2 && main.LayoutHashMatched && client.Welcome?.Players.Count >= 1;
                    _done = true;
                    GD.Print($"[net] RESULT {(okBack ? "ok" : "FAIL")}: resumed server gave back member {client.MemberIndex} with {bag} bag item(s) on floor {Game.Instance.CurrentFloor} ({client.Welcome?.Players.Count} known players)");
                    net.Disconnect("test finished");
                    GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(okBack ? 0 : 1);
                    return;
                }
                var orcs = main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count(e => e.Profile.Id == Dungeon.DungeonBuilder.ViewerOrcId) ?? 0;
                GD.Print($"[net] viewer orcs on this floor: {orcs}");
                if (orcs > 1) { Finish("[net]", false, $"{orcs} copies of the viewer orc: the client spawned its own beside the server's"); return; }
                _start = local.GlobalPosition;
                _clock = 0; _step = 1;
                return;
            case 20:
            {
                var gained = main.Party.Gold - _goldBefore;
                if (gained == TavernRules.SeedGold)
                {
                    if (!_dealSentTwice) { _dealSentTwice = true; net.Send(new DialogueNodeIntent("merchant", "deal")); _clock = 0; return; } // a second deal must not pay again
                    if (_clock < 2) return;
                    Finish("[net]", true, $"tavern join: the deal reached the server and the purse went {_goldBefore} -> {main.Party.Gold} once");
                    net.Disconnect("test finished");
                    return;
                }
                if (gained != 0) { Finish("[net]", false, $"the deal paid {gained} gold, expected {TavernRules.SeedGold} once"); return; }
                if (_clock > 8) Finish("[net]", false, $"the deal never paid (gold {main.Party.Gold}, before {_goldBefore})");
                return;
            }
            case 1:
                if (_clock < 0.5) return; // let ForcePose land first
                main.Party.ForcedMoveInput = new Vector2(0, -1);
                if (_clock > 2.5) { main.Party.ForcedMoveInput = null; _start = _startAfterPose ?? _start; _clock = 0; _step = 2; }
                return;
            case 2:
                var moved = main.Party.Local!.GlobalPosition.DistanceTo(_startAfterPose ?? _start);
                if (client.Trace.Contains("echo") && moved > 0.8f) { GD.Print($"[net] moved {moved:0.0} m and saw the echo after {client.SnapshotsSeen} snapshots"); net.Send(new ChatIntent("hello from the test client")); _clock = 0; _step = 3; }
                else if (_clock > 15) Finish("[net]", false, $"no echo (moved {moved:0.0} m, snapshots {client.SnapshotsSeen}, echo {client.Trace.Contains("echo")})");
                return;
            case 3:
                if (client.Trace.Contains("chat"))
                {
                    // a ping: where this body looks (floor 1's walls and floor are within the aim ray), up to the server and back to every screen with our name
                    var pinged = main.Hud.Ping();
                    if (!pinged) { Finish("[net]", false, "Hud.Ping() refused (down, cooling down, or looking at nothing)"); return; }
                    _clock = 0; _step = 30;
                }
                else if (_clock > 10) Finish("[net]", false, "chat never came back");
                return;
            case 30:
            {
                var mark = UI.Hud.PingMarkers.Instance?.Board.Active.FirstOrDefault(m => m.Name == net.JoinName);
                if (client.Trace.Contains("ping") && mark is { } m)
                {
                    GD.Print($"[net] ping relayed: \"{m.Name}\" at {m.X:0.0},{m.Y:0.0},{m.Z:0.0}");
                    _clock = 0; _step = net.JoinName.Equals("Sable", StringComparison.OrdinalIgnoreCase) ? 4 : 31;
                }
                else if (_clock > 6) Finish("[net]", false, $"the ping never came back (trace ping {client.Trace.Contains("ping")}, marks {UI.Hud.PingMarkers.Instance?.Board.Active.Count ?? -1})");
                return;
            }
            case 31:
            {
                // hero bots: call Sable in; the server runs it and it follows us. Walk off and see it come after us.
                var me = main.Party.Local!;
                if (!_botAsked) { _botAsked = true; net.Send(new AddBotIntent("Sable")); return; }
                var entry = client.Roster.Values.FirstOrDefault(p => p.Name == "Sable");
                if (_botFollowFrom == null)
                {
                    if (client.Trace.Contains("bot:Sable") && entry is { Online: true } && entry.BotOwner == net.JoinName && entry.MemberIndex < main.Party.Adventurers.Length)
                    {
                        var layout = main.Builder.Layout!;
                        var start = Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition);
                        // a cell a few metres off on our own level (a drop to a cellar is a different test)
                        var away = Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(-2, 5).Select(dz => start + new Dungeon.Generation.GridPos(dx, dz)))
                            .Where(c => layout.IsWalkable(c) && Math.Abs(main.Builder.Surface(c).Y - me.GlobalPosition.Y) < 0.6f && main.Builder.Surface(c).DistanceTo(me.GlobalPosition) is > 4f and < 9f)
                            .OrderByDescending(c => main.Builder.Surface(c).DistanceTo(me.GlobalPosition)).FirstOrDefault(start);
                        me.Teleport(main.Builder.Surface(away) + Vector3.Up * 0.3f);
                        _botFollowFrom = me.GlobalPosition;
                        _clock = 0;
                        GD.Print($"[net] bot Sable joined as member {entry.MemberIndex}, owned by {entry.BotOwner}; walked to {_botFollowFrom:0.0}");
                    }
                    else if (_clock > 10) Finish("[net]", false, $"the bot never joined (trace bot {client.Trace.Contains("bot:Sable")}, roster {(entry == null ? "-" : $"{entry.Name} online {entry.Online} owner {entry.BotOwner}")})");
                    return;
                }
                var bot = main.Party.Adventurers[entry!.MemberIndex];
                var gap = bot.GlobalPosition.DistanceTo(me.GlobalPosition);
                _botPrint += delta;
                if (_botPrint > 2) { _botPrint = 0; GD.Print($"[net] bot Sable at {bot.GlobalPosition:0.0}, us at {me.GlobalPosition:0.0}: {gap:0.0} m"); }
                if (!bot.Parked && gap < 3.5f && _clock > 1.5)
                {
                    GD.Print($"[net] bot Sable follows: {gap:0.0} m behind us after {_clock:0.0}s");
                    // the Tab screen was made before we (and the bot) joined the party: it must grow a doll for each of us
                    var hud = main.Hud;
                    try { hud.ToggleInventory(); }
                    catch (Exception e) { Finish("[net]", false, $"the inventory failed to open with a bot in the party: {e.GetType().Name}: {e.Message}"); return; }
                    var dolls = hud.Inventory.HasDoll(client.MemberIndex) && hud.Inventory.HasDoll(entry.MemberIndex);
                    if (hud.InventoryOpen) hud.ToggleInventory();
                    if (!dolls) { Finish("[net]", false, $"the inventory has no doll for us (member {client.MemberIndex}) or the bot (member {entry.MemberIndex})"); return; }
                    GD.Print("[net] inventory opens with a doll for us and the bot");
                    _guardBot = entry.MemberIndex; _clock = 0; _step = 34; // the bag guard, with the bot still ours; step 34 sends it away after
                }
                else if (_clock > 15) Finish("[net]", false, $"the bot did not follow (parked {bot.Parked}, {gap:0.0} m away)");
                return;
            }
            case 34:
            {
                // the bag guard (v14): an intent naming an item the cell no longer holds is refused and the bag re-sent, never applied
                // to whatever is there now; we may act for a bot we own (it drinks our potion), and not for a hero who is not ours
                var bag = main.Party.Bag;
                if (_guardPhase == 0)
                {
                    if (!_guardListening) { _guardListening = true; EventBus.Instance.Message += GuardHeard; }
                    _guardIndex = Enumerable.Range(0, bag.Capacity).FirstOrDefault(i => bag[i] is { Consumable: { Respec: false, PlaceTrap: "" } }, -1);
                    if (_guardIndex < 0) _guardIndex = Enumerable.Range(0, bag.Capacity).FirstOrDefault(i => bag[i] != null, -1);
                    if (_guardIndex < 0) { if (_clock > 5) Finish("[net]", false, "the bag guard needs an item in the bag and there is none (the server puts potions in)"); return; }
                    _guardId = bag[_guardIndex]!.Id; _guardCount = bag[_guardIndex]!.StackCount;
                    net.Send(new BagMoveIntent(Party.BagSlot.Bag(_guardIndex), Party.BagSlot.Destroy, Guid.NewGuid())); // a stale screen's destroy
                    _guardPhase = 1; _clock = 0;
                    return;
                }
                var held = bag[_guardIndex];
                if (_guardPhase == 1)
                {
                    if (_heardMoved && held?.Id == _guardId && held.StackCount == _guardCount)
                    {
                        GD.Print($"[net] bag guard: a destroy aimed at an item that moved was refused ({held.DisplayName} x{held.StackCount} kept) after {_clock:0.0}s");
                        if (held.Consumable is not { Respec: false, PlaceTrap: "" }) { GD.Print("[net] bag guard: no potion in the bag; skipping the bot's drink"); _guardPhase = 3; }
                        else { net.Send(new QuickActionIntent(Party.BagSlot.Bag(_guardIndex), _guardBot, _guardId)); _guardPhase = 2; }
                        _clock = 0;
                    }
                    else if (_clock > 6) Finish("[net]", false, $"the stale destroy was not refused (heard {_heardMoved}, cell holds {held?.DisplayName ?? "nothing"} x{held?.StackCount ?? 0}, was x{_guardCount})");
                    return;
                }
                if (_guardPhase == 2)
                {
                    var left = bag.Items.Where(i => i.Id == _guardId).Sum(i => i.StackCount);
                    if (_heardNotYours) { Finish("[net]", false, "the server would not let us use a potion on our own bot"); return; }
                    if (left == _guardCount - 1) { GD.Print($"[net] bag guard: our bot drank a potion we gave it ({left} left)"); _guardPhase = 3; _clock = 0; }
                    else if (_clock > 6) Finish("[net]", false, $"our bot never drank the potion ({left} left, was {_guardCount})");
                    return;
                }
                if (_guardPhase == 3)
                {
                    // a hero who is neither ours nor our bot's: a member of the party that is someone else's, or past the end of it
                    var other = Enumerable.Range(0, main.Party.Members.Length).FirstOrDefault(i => i != client.MemberIndex && i != _guardBot, main.Party.Members.Length + 3);
                    net.Send(new AllocateStatIntent("Strength", other));
                    _guardPhase = 4; _clock = 0;
                    return;
                }
                if (_heardNotYours)
                {
                    GD.Print("[net] bag guard: a stat point for a hero who is not ours was refused out loud");
                    EventBus.Instance.Message -= GuardHeard;
                    _clock = 0; _step = 32; net.Send(new DismissBotIntent(_guardBot));
                }
                else if (_clock > 6) Finish("[net]", false, "a stat point for someone else's hero was not refused with a message");
                return;
            }
            case 32:
            {
                var entry = client.Roster.Values.FirstOrDefault(p => p.Name == "Sable");
                if (client.Trace.Contains("left:Sable") && entry is { Online: false } && main.Party.Adventurers[entry.MemberIndex].Parked) { GD.Print("[net] bot Sable sent back to town"); _clock = 0; _step = 33; }
                else if (_clock > 8) Finish("[net]", false, "the bot was never sent away");
                return;
            }
            case 33:
            {
                // the item check: a ghost only this screen shows goes, and a real item this screen lost comes back, within one check
                var root = main.Builder.FloorRoot!;
                if (_ghostId == Guid.Empty)
                {
                    var ghost = GD.Load<PackedScene>("res://scenes/Props/LootDrop.tscn").Instantiate<Items.LootDrop>();
                    ghost.Item = Items.ItemInstance.Simple("item.unknown", "Ghost", id: Guid.NewGuid());
                    _ghostId = ghost.Item.Id;
                    root.AddChild(ghost);
                    ghost.GlobalPosition = main.Party.Local!.GlobalPosition + Vector3.Forward;
                    if (root.GetChildren().OfType<Items.LootDrop>().FirstOrDefault(d => !d.Taken && d.Item.Id != _ghostId) is { } real) { _lostId = real.Item.Id; root.RemoveChild(real); real.QueueFree(); }
                    GD.Print($"[net] item check: made a ghost here{(_lostId == Guid.Empty ? "" : " and hid a real item")}");
                    // the state check: our purse, our bag and our stairs seal go wrong here only; the server's fingerprints must put them right
                    _goldTruth = main.Party.Gold;
                    main.Party.SetGold(_goldTruth + 1234);
                    if (main.Party.Bag.Items.FirstOrDefault() is { } bagItem) { _bagTruth = bagItem.Id; main.Party.Bag.Remove(bagItem); }
                    if (root.GetNodeOrNull<Dungeon.Stairs>("DownStairs") is { } st) { _stairsTruth = st.Locked; st.SetLocked(!st.Locked); }
                    GD.Print($"[net] state check: purse {_goldTruth} -> {main.Party.Gold} here, {(_bagTruth == Guid.Empty ? "bag empty" : "a bag item gone")}, stairs seal flipped to {!_stairsTruth}");
                    _clock = 0;
                    return;
                }
                var drops = root.GetChildren().OfType<Items.LootDrop>().Where(d => !d.Taken).ToList();
                var ghostGone = drops.All(d => d.Item.Id != _ghostId);
                var lostBack = _lostId == Guid.Empty || drops.Any(d => d.Item.Id == _lostId);
                var goldBack = main.Party.Gold == _goldTruth;
                var bagBack = _bagTruth == Guid.Empty || main.Party.Bag.Items.Any(i => i.Id == _bagTruth);
                var stairsBack = root.GetNodeOrNull<Dungeon.Stairs>("DownStairs") is not { } st2 || st2.Locked == _stairsTruth;
                if (ghostGone && lostBack && goldBack && bagBack && stairsBack) { GD.Print($"[net] item check: the ghost went{(_lostId == Guid.Empty ? "" : " and the hidden item came back")}; state check: purse, bag and stairs seal repaired, after {_clock:0.0}s"); _clock = 0; _step = 35; }
                else if (_clock > Protocol.LootCheckSeconds * 3 + 2) Finish("[net]", false, $"the checks did not repair this screen (ghost gone {ghostGone}, hidden item back {lostBack}, purse {goldBack}, bag {bagBack}, stairs {stairsBack})");
                return;
            }
            case 35:
            {
                // the prop check: props go wrong on this screen only (a trap of the party's the server never set, and a door, a
                // floor trap and a lever moved the other way); the server's fingerprint must make us ask, and its answer put them back
                var root = main.Builder.FloorRoot!;
                var me = main.Party.Local!;
                if (!_propsSpoiled)
                {
                    _propsSpoiled = true;
                    _fakeTrap = PropSync.SpawnPlaced(root, new Save.PlacedTrapDto { Id = PropCheck.PlacedTrapBase + 999, Kind = "Spikes", X = me.GlobalPosition.X + 1f, Y = me.GlobalPosition.Y, Z = me.GlobalPosition.Z }, Dungeon.TrapKind.Spikes);
                    var spoiled = new List<string> { "a party trap the server never set" };
                    // a door already made out (opening one marks it seen, which only the server's own sighting undoes), well away
                    // from us (closing one on our own cell would shut us in)
                    if (root.GetChildren().OfType<Dungeon.Props.Door>().FirstOrDefault(d => d.Detected && d.GlobalPosition.DistanceTo(me.GlobalPosition) > 8f) is { } door)
                    {
                        _spoiledDoor = door; _doorTruth = door.IsOpen;
                        door.SetOpen(!door.IsOpen, silent: true);
                        spoiled.Add($"door {door.VaultIndex} {(door.IsOpen ? "opened" : "shut")}");
                    }
                    // a trap already seen: springing or disarming one here also marks it seen, which only the server's own sighting undoes
                    if (root.GetChildren().OfType<Dungeon.Props.Trap>().FirstOrDefault(t => !t.PlacedByParty && t.SpawnIndex >= 0 && t.Detected) is { } trap)
                    {
                        _spoiledTrap = trap; _trapTruth = (trap.Triggered, trap.Disarmed);
                        if (trap.Armed) trap.RestoreState(triggered: false, disarmed: true); else trap.RestoreState(false, false); // disarmed here, or re-armed
                        spoiled.Add($"trap {trap.SpawnIndex} {(trap.Armed ? "re-armed" : "disarmed")}");
                    }
                    if (root.GetChildren().OfType<Dungeon.Props.Lever>().FirstOrDefault() is { } lever)
                    {
                        _spoiledLever = lever; _leverTruth = lever.Pulled;
                        lever.SetPulled(!lever.Pulled);
                        spoiled.Add($"lever {lever.VaultIndex} {(lever.Pulled ? "pulled" : "reset")}");
                    }
                    GD.Print($"[net] prop check: spoiled here: {string.Join(", ", spoiled)}");
                    _clock = 0;
                    return;
                }
                var fakeGone = _fakeTrap == null || !IsInstanceValid(_fakeTrap) || _fakeTrap.IsQueuedForDeletion();
                var doorBack = _spoiledDoor == null || !IsInstanceValid(_spoiledDoor) || _spoiledDoor.IsOpen == _doorTruth;
                var trapBack = _spoiledTrap == null || !IsInstanceValid(_spoiledTrap) || (_spoiledTrap.Triggered, _spoiledTrap.Disarmed) == _trapTruth;
                var leverBack = _spoiledLever == null || !IsInstanceValid(_spoiledLever) || _spoiledLever.Pulled == _leverTruth;
                if (fakeGone && doorBack && trapBack && leverBack)
                {
                    GD.Print($"[net] prop check: the spoiled props were repaired after {_clock:0.0}s (door {(_spoiledDoor != null ? "yes" : "none")}, trap {(_spoiledTrap != null ? "yes" : "none")}, lever {(_spoiledLever != null ? "yes" : "none")})");
                    _clock = 0; _step = 36;
                }
                else if (_clock > Protocol.LootCheckSeconds * 3 + 2) Finish("[net]", false, $"the prop check did not repair this screen (fake trap gone {fakeGone}, door {doorBack}, trap {trapBack}, lever {leverBack})");
                return;
            }
            case 36:
            {
                // the enemy check: a creature only this screen shows goes, and a living one this screen lost comes back; and the
                // item check's counts: an item shown with the wrong count is sent again and put right in place
                var root = main.Builder.FloorRoot!;
                var me = main.Party.Local!;
                if (!_enemiesSpoiled)
                {
                    _enemiesSpoiled = true;
                    client.MessageReceived += NoteEnemyCheck;
                    var living = root.GetChildren().OfType<Enemies.Enemy>().Where(e => e.IsAlive && !e.IsQueuedForDeletion() && !e.IsBoss).ToList();
                    if (living.Count == 0) { GD.Print("[net] enemy check: no living creature on this floor; skipping"); _clock = 0; _step = 4; return; }
                    _ghostEnemy = main.Builder.SpawnEnemy(living[0].Profile.Id, me.GlobalPosition + new Vector3(2f, 0.3f, 0), Game.Instance.CurrentFloor, netId: Enemies.Enemy.DynamicNetIdBase + 9999);
                    // the farthest from us (the fight below takes the nearest), gone from this screen only
                    var lost = living.OrderByDescending(e => e.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).First();
                    _lostEnemy = lost.NetId;
                    root.RemoveChild(lost);
                    lost.QueueFree();
                    var spoiled = $"made a ghost {_ghostEnemy?.Profile.Id} and hid {lost.Profile.Id} (net id {_lostEnemy})";
                    if (root.GetChildren().OfType<Items.LootDrop>().FirstOrDefault(d => !d.Taken) is { } drop)
                    {
                        _countDrop = drop.Item.Id;
                        _countTruth = WorldCheck.ItemCount(drop.Coins, drop.Item.StackCount);
                        if (drop.Coins > 0) drop.Coins += 5; else drop.Item.StackCount += 5;
                        spoiled += $"; {drop.Item} shows {WorldCheck.ItemCount(drop.Coins, drop.Item.StackCount)} here, {_countTruth} on the server";
                    }
                    GD.Print($"[net] enemy check: {spoiled}");
                    _clock = 0;
                    return;
                }
                var ghostGone = _ghostEnemy == null || !IsInstanceValid(_ghostEnemy) || _ghostEnemy.IsQueuedForDeletion();
                // back alive, or (it died on the server meanwhile) no longer on the server's list
                var lostBack = root.GetChildren().OfType<Enemies.Enemy>().Any(e => e.NetId == _lostEnemy && e.IsAlive && !e.IsQueuedForDeletion())
                    || (_enemyChecksSeen > 0 && _lastEnemyCheck != null && !_lastEnemyCheck.Contains(_lostEnemy));
                var countBack = _countDrop == Guid.Empty || root.GetChildren().OfType<Items.LootDrop>().Any(d => !d.Taken && d.Item.Id == _countDrop && WorldCheck.ItemCount(d.Coins, d.Item.StackCount) == _countTruth);
                if (ghostGone && lostBack && countBack)
                {
                    client.MessageReceived -= NoteEnemyCheck;
                    GD.Print($"[net] enemy check: the ghost creature went and the hidden one came back{(_countDrop == Guid.Empty ? "" : "; item check: the miscounted item was put right")} after {_clock:0.0}s");
                    _clock = 0; _step = 4;
                }
                else if (_clock > Protocol.LootCheckSeconds * 3 + 2) Finish("[net]", false, $"the enemy check did not repair this screen (ghost gone {ghostGone}, hidden creature back {lostBack}, count {countBack}; {_enemyChecksSeen} enemy checks seen)");
                return;
            }
            case 4:
            {
                // fight: stand in front of the nearest living enemy and swing until the server reports the kill
                var me = main.Party.Local!;
                if (!_fightHeard) { _fightHeard = true; _killsBefore = client.Trace.Count(t => t == "kill"); _hitFxBefore = client.Trace.Count(t => t == "hitfx"); EventBus.Instance.Sfx += FightCue; client.MessageReceived += CountPartySync; }
                if (_target == null || !IsInstanceValid(_target) || !_target.IsAlive)
                {
                    if (client.Trace.Contains("kill"))
                    {
                        // a second for the EnemyDied messages to land (a killing blow's EnemyHurt shows the death a tick earlier):
                        // then every death since the fight began was heard exactly once, and the blows were heard as grunts
                        _settle += delta;
                        if (_settle < 1.0) return;
                        EventBus.Instance.Sfx -= FightCue;
                        var kills = client.Trace.Count(t => t == "kill") - _killsBefore;
                        GD.Print($"[net] kill confirmed after {_swings} swings; heard {_deathCues} death cue(s) for {kills} kill(s), {_hurtCues} hurt cue(s)");
                        if (_deathCues != kills) { Finish("[net]", false, $"heard {_deathCues} death cue(s) for {kills} kill(s)"); return; }
                        if (_hurtCues < 1) { Finish("[net]", false, "heard no hurt cue while fighting"); return; }
                        // our sword blows bleed the enemy on the server, which relays each spatter as a HitFxEvt
                        var hitFx = client.Trace.Count(t => t == "hitfx") - _hitFxBefore;
                        GD.Print($"[net] hit fx: {hitFx} events");
                        if (hitFx < 1) { Finish("[net]", false, "no hit fx (blood) reached this client while fighting"); return; }
                        // the server tells every screen about each swing (hero bots' and other players' bodies play the clip)
                        if (!client.Trace.Contains("swing")) { Finish("[net]", false, "no swing event reached this client while fighting"); return; }
                        // a swing or a hit changes only what the Vitals carry: the whole sheet must not follow every blow (MemberSync)
                        client.MessageReceived -= CountPartySync;
                        GD.Print($"[net] fight traffic: {_partySyncs} PartySync for {_swings} swings");
                        if (_swings >= 4 && _partySyncs >= _swings) { Finish("[net]", false, $"{_partySyncs} PartySync for {_swings} swings: the sheet still follows every blow"); return; }
                        _clock = 0; _step = 5; return;
                    }
                    // a loner, not a camp or a nest: one level-1 warrior against a band would be a different test
                    // the viewer's 280 hp orc by the entrance is a showpiece, not a level-1 fight
                    var candidates = main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Where(e => e.IsAlive && !e.IsBoss && !e.IsHorde && e.Kind == Enemies.EnemyKind.Melee && e.Profile.Id != Dungeon.DungeonBuilder.ViewerOrcId).ToList() ?? new List<Enemies.Enemy>();
                    _target = candidates.Where(e => e.Group == null).OrderBy(e => e.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault()
                        ?? candidates.OrderBy(e => e.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
                    if (_target == null) { Finish("[net]", false, "no enemy to fight"); return; }
                    _placed = false;
                }
                if (!_placed)
                {
                    // step in front of it (the server accepts poses on walkable cells)
                    // beside it, on its level (it may stand in a cellar or on a ledge)
                    me.Teleport(_target.GlobalPosition + new Vector3(1.0f, 0.3f, 0), _target.GlobalPosition + Vector3.Up * 1.0f);
                    _placed = true; _swingClock = 0;
                    return;
                }
                me.FaceToward(_target.GlobalPosition);
                _swingClock += delta;
                _chaseClock += delta;
                // it moves; stay on its heels (the server sees our poses, so the swing lands where we stand)
                if (_chaseClock > 0.6 && _target.GlobalPosition.DistanceTo(me.GlobalPosition) > 1.4f)
                {
                    _chaseClock = 0;
                    var toward = (me.GlobalPosition - _target.GlobalPosition) with { Y = 0 };
                    var stand = _target.GlobalPosition + (toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward) * 1.0f;
                    if (main.Builder.Layout!.IsWalkable(Dungeon.DungeonBuilder.WorldToCell(stand))) me.Teleport(stand with { Y = _target.GlobalPosition.Y + 0.3f }, _target.GlobalPosition + Vector3.Up * 1.0f);
                }
                if (_swingClock > 0.45) { _swingClock = 0; _swings++; me.Attack(); }
                if (_clock > 40) Finish("[net]", false, $"could not kill {_target.DisplayName} (hp {_target.Hp:0}, {_target.GlobalPosition.DistanceTo(me.GlobalPosition):0.0} m away, it at {_target.GlobalPosition:0.0}, we at {me.GlobalPosition:0.0}) in {_swings} swings; we have {me.State.Hp:0} hp, trace=[{string.Join(", ", client.Trace)}]");
                return;
            }
            case 5:
            {
                // loot: walk over to whatever the kill dropped and pick it up through the server
                var me = main.Party.Local!;
                var drop = main.Builder.FloorRoot?.GetChildren().OfType<Items.LootDrop>().OrderBy(d => d.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
                if (drop == null) { if (_clock > 3) { GD.Print("[net] nothing dropped; skipping the pickup"); _clock = 0; _step = 6; } return; }
                if (!_placed2) { me.Teleport(drop.GlobalPosition + new Vector3(0.6f, 0.2f, 0.6f), drop.GlobalPosition); _placed2 = true; _bagBefore = main.Party.Bag.Items.Count(); return; }
                if (_clock > 0.8 && !_askedPickup) { _askedPickup = true; net.Send(new InteractIntent(NetRefs.Loot(drop.Item.Id))); }
                if (client.Trace.Contains("bag") && main.Party.Bag.Items.Count() > _bagBefore) { GD.Print($"[net] picked up {main.Party.Bag.Items.Count() - _bagBefore} item(s), bag now {main.Party.Bag.Items.Count()}"); _clock = 0; _step = 6; }
                else if (_clock > 8) Finish("[net]", false, $"pickup never landed (bag {main.Party.Bag.Items.Count()} vs {_bagBefore}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            }
            case 6:
            {
                // chest: open the nearest unlocked one; the server spills loot and everyone sees it
                var me = main.Party.Local!;
                _chest ??= main.Builder.FloorRoot?.GetChildren().OfType<Dungeon.Props.Chest>().Where(c => !c.Opened && !c.Locked).OrderBy(c => c.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault();
                var chest = _chest;
                if (chest == null) { GD.Print("[net] no unlocked chest on this floor; skipping"); _clock = 0; _step = 12; return; }
                if (!_placed3)
                {
                    // a step back from the chest on the walkable side, at its level
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(main.Builder.Layout!, Dungeon.DungeonBuilder.WorldToCell(chest.GlobalPosition));
                    var away = (main.Builder.Surface(cell) - chest.GlobalPosition) with { Y = 0 };
                    var stand = chest.GlobalPosition + (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward) * 1.3f;
                    me.Teleport(stand with { Y = chest.GlobalPosition.Y + 0.3f }, chest.GlobalPosition);
                    _placed3 = true; _goldBefore = main.Party.Gold; _lootBefore = client.Trace.Count(t => t == "loot");
                    return;
                }
                if (_clock > 0.8 && !_askedChest) { _askedChest = true; net.Send(new InteractIntent(NetRefs.Chest(chest.SpawnIndex))); }
                // the chest's coin pile reached the client as a pile; take it through the server like any pickup
                if (chest.Opened && !_askedCoins && main.Builder.FloorRoot?.GetChildren().OfType<Items.LootDrop>().Where(d => d.Coins > 0).OrderBy(d => d.GlobalPosition.DistanceSquaredTo(me.GlobalPosition)).FirstOrDefault() is { } pile)
                {
                    _askedCoins = true;
                    me.Teleport(pile.GlobalPosition + new Vector3(0.6f, 0.2f, 0.6f), pile.GlobalPosition);
                    net.Send(new InteractIntent(NetRefs.Loot(pile.Item.Id)));
                }
                if (client.Trace.Contains("chest") && chest.Opened && client.Trace.Contains("gold") && main.Party.Gold > _goldBefore)
                {
                    GD.Print($"[net] chest opened, {client.Trace.Count(t => t == "loot") - _lootBefore} item(s) spilled");
                    GD.Print($"[net] gold synced: {main.Party.Gold}");
                    _clock = 0; _step = 12;
                }
                else if (_clock > 8) Finish("[net]", false, $"chest never opened or its gold never synced (gold {main.Party.Gold} vs {_goldBefore}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            }
            case 12:
            {
                // a fighter on this floor: ask it to join through the dialogue; the server decides and its answer picks the line we see
                var me = main.Party.Local!;
                var fighter = main.Builder.FloorRoot?.GetChildren().OfType<Npc>().FirstOrDefault(n => n.Mode == NpcMode.Fighter && !n.Dead);
                if (fighter == null) { GD.Print("[net] no fighter on this floor; skipping the join"); _clock = 0; _step = 7; return; }
                var dialogue = main.Hud.Dialogue;
                if (!_placedJoin)
                {
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(main.Builder.Layout!, Dungeon.DungeonBuilder.WorldToCell(fighter.GlobalPosition));
                    me.Teleport(main.Builder.Surface(cell) + Vector3.Up * 0.3f, fighter.GlobalPosition);
                    main.Hud.OpenDialogue(fighter);
                    var join = dialogue.Choices.ToList().FindIndex(c => c.Effect == DialogueEffect.Join);
                    if (join < 0) { Finish("[net]", false, $"{fighter.NpcName}'s dialogue offers no Join choice"); return; }
                    dialogue.Choose(join);
                    _placedJoin = true; _clock = 0; return;
                }
                var answered = client.Trace.Contains("joined") || client.Trace.Contains("refused");
                if (answered && _clock > 1.5)
                {
                    var yes = client.Trace.Contains("joined");
                    var shown = dialogue.Node?.Id;
                    var agree = shown == (yes ? "yes" : "no") && fighter.Escorting == yes;
                    GD.Print($"[net] asked {fighter.NpcName} to join: server said {(yes ? "yes" : "no")}, dialogue shows '{shown}', escorting={fighter.Escorting}");
                    dialogue.Close();
                    if (!agree) { Finish("[net]", false, $"join answer and screen disagree (server {(yes ? "yes" : "no")}, node {shown}, escorting {fighter.Escorting})"); return; }
                    _clock = 0; _step = 7;
                }
                else if (_clock > 8) Finish("[net]", false, $"no answer to the join question (dialogue node {dialogue.Node?.Id}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
                return;
            }
            case 7:
                // stairs: any player, immediately; the whole party arrives on floor 2 built from the same plan
                if (!_placed4 && main.Builder.Layout is { } lay1)
                {
                    var down = main.Builder.Surface(lay1.DownStairs);
                    var cell = Dungeon.DungeonBuilder.WalkableNeighbor(lay1, lay1.DownStairs);
                    var away = (main.Builder.Surface(cell) - down) with { Y = 0 };
                    var stand = down + (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward) * 1.3f;
                    main.Party.Local!.Teleport(stand with { Y = down.Y + 0.3f }, down);
                    // a revive that never reached this screen (the server's self-recovery once skipped it): the body shows fallen
                    // while it is alive. The stairs landing cannot see a fallen body, so the question below fails unless it stands up again
                    main.Party.Local.ShowDowned();
                    GD.Print($"[net] showing our body fallen with no revive coming (layer {main.Party.Local.CollisionLayer}); the stairs must still ask");
                    _placed4 = true; _clock = 0; return;
                }
                if (!_askedStairs)
                {
                    // walk onto the landing: the stairs must ask before we answer (the question is what a player sees)
                    var stairs = main.Builder.FloorRoot?.GetChildren().OfType<Dungeon.Stairs>().FirstOrDefault(s => s.Direction > 0);
                    if (stairs == null) { Finish("[net]", false, "no down stairs on this floor"); return; }
                    if (!main.Hud.StairsPromptOpen)
                    {
                        if (_clock > 0.5) { main.Party.Local!.FaceToward(stairs.GlobalPosition); main.Party.ForcedMoveInput = new Vector2(0, -1); }
                        if (_clock > 6) { main.Party.ForcedMoveInput = null; Finish("[net]", false, $"the stairs never asked (we stand {main.Party.Local!.GlobalPosition.DistanceTo(stairs.GlobalPosition):0.0} m from them, prompt open {main.Hud.StairsPromptOpen})"); }
                        return;
                    }
                    main.Party.ForcedMoveInput = null;
                    GD.Print($"[net] the stairs asked after {_clock:0.0} s: \"{stairs.Question}\"; answering yes");
                    _askedStairs = true;
                    // the floor watchdog: this floor change is lost on its way here; the client must ask for the floor and build it
                    client.TestDropFloorChanges = 1;
                    stairs.Confirm(main.Party, main.Party.Local!); // the Yes button's path: a client sends the intent
                    _clock = 0; return;
                }
                if (client.Trace.Contains("floor:2") && Game.Instance.CurrentFloor == 2 && main.Builder.Layout != null)
                {
                    GD.Print($"[net] on floor 2: {main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Count()} enemies, hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, floor card {(client.Trace.Contains("floorcard") ? "received" : "MISSING")}: {main.Hud?.LastFloorCard}");
                    var elites = main.Builder.FloorRoot?.GetChildren().OfType<Enemies.Enemy>().Where(e => e.SpawnIndex >= 0 && e.IsElite).OrderBy(e => e.SpawnIndex).ToList() ?? new List<Enemies.Enemy>();
                    GD.Print($"[net] elites on floor 2: [{string.Join(", ", elites.Select(e => $"{e.SpawnIndex}:{e.DisplayName}"))}]"); // net_test.sh compares this with the server's line
                    if (!client.Trace.Contains("floorcard")) { Finish("[net]", false, "the floor card for floor 1 never reached the client (run-structure's FloorCardEvt)"); return; }
                    if (!client.Trace.Contains("floor-dropped") || !client.Trace.Contains("floor-resync")) { Finish("[net]", false, $"the floor watchdog: dropped {client.Trace.Contains("floor-dropped")}, fetched again {client.Trace.Contains("floor-resync")}"); return; }
                    GD.Print($"[net] floor watchdog: the lost floor change was asked for and built ({client.Trace.Count(t => t == "floor-ask")} ask(s))");
                    _placementsAtFloor2 = _placements; _clock = 0; _step = 11;
                }
                else if (_clock > 20) Finish("[net]", false, $"floor 2 never came (floor {Game.Instance.CurrentFloor}, trace=[{string.Join(", ", client.Trace)}])");
                return;
            case 11:
            {
                // the forced Press: stand on its plate, see the server's Warning phase arrive, step off before it closes
                // the server answers our Ready with a ForcePose to the arrival spot; stepping onto the plate before it lands would be undone by it
                if (_hazardReturn == null && _placements == _placementsAtFloor2 && _clock < 5) return;
                if (_placements != _placementsAtFloor2 && _hazardReturn == null && _clock < 0.3) return; // let the placement settle
                var press = main.Builder.Hazards.OfType<Dungeon.Props.Press>().FirstOrDefault();
                if (press == null) { GD.Print("[net] no Press on this floor; skipping the hazard step"); _clock = 0; _step = 14; return; }
                if (_hazardReturn == null)
                {
                    _hazardReturn = main.Party.Local!.GlobalPosition;
                    main.Party.Local.Teleport(main.Builder.Surface(press.Placement.Plate) + Vector3.Up * 0.3f);
                    GD.Print($"[net] stepping onto the Press plate at {main.Party.Local.GlobalPosition:0.0} (plate node {press.Plate.GlobalPosition:0.0}, cell {press.Placement.Plate}); body was at {_hazardReturn:0.0}; placements {_placements} (floor 2 arrival {(_placements > _placementsAtFloor2 ? "seen" : "NOT seen")})");
                    _placementsAtFloor2 = _placements; _clock = 0; return;
                }
                if (_placements != _placementsAtFloor2 && main.Party.Local!.GlobalPosition.DistanceTo(main.Builder.Surface(press.Placement.Plate)) > 1.5f)
                {
                    // a late placement pulled us off the plate: step back on
                    _placementsAtFloor2 = _placements;
                    main.Party.Local.Teleport(main.Builder.Surface(press.Placement.Plate) + Vector3.Up * 0.3f);
                    GD.Print($"[net] a late placement moved us; back onto the plate at {main.Party.Local.GlobalPosition:0.0}");
                    return;
                }
                if (client.Trace.Contains("hazard:Warning"))
                {
                    main.Party.Local!.Teleport(_hazardReturn.Value);
                    GD.Print($"[net] press warned after {_clock:0.0} s (phase {press.Phase}, {press.PhaseTime:0.00} s in); stepped clear");
                    _clock = 0; _step = 14;
                }
                else if (_clock > 10) Finish("[net]", false, $"the Press never warned (phase {press.Phase}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
                return;
            }
            case 14:
            {
                // the safe room: step inside; the server sees our body there and its objective line, which ends with "Safe room", reaches our HUD
                if (main.Builder.Layout is not { SafeRoom: >= 0 } safeLayout) { GD.Print("[net] no safe room on this floor; skipping"); _clock = 0; _step = 8; return; }
                var me = main.Party.Local!;
                if (_safeReturn == null)
                {
                    if (_clock < 0.5) return; // let the step off the plate land first
                    _safeReturn = me.GlobalPosition;
                    if (main.Hud.ObjectiveText.EndsWith("Safe room")) { Finish("[net]", false, $"safe room: the HUD says so before we stepped in (\"{main.Hud.ObjectiveText}\")"); return; }
                    me.Teleport(main.Builder.Surface(SafeRoomRules.MerchantCell(safeLayout)) + Vector3.Up * 0.3f, main.Builder.Surface(safeLayout.SafeRoomDoor));
                    _clock = 0; return;
                }
                var line = main.Hud.ObjectiveText;
                if (line.EndsWith("Safe room"))
                {
                    if (!client.Trace.Contains("shop")) { Finish("[net]", false, "the server never sent the shop's sold slots"); return; }
                    GD.Print($"[net] safe room: HUD shows \"{line}\" {_clock:0.0} s after stepping inside");
                    _clock = 0; _step = 15; // still inside: trade with the merchant before leaving the room
                }
                else if (_clock > 6) Finish("[net]", false, $"safe room: the HUD never showed it (line \"{line}\", we stand at {Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition)}, inside={safeLayout.IsInSafeRoom(Dungeon.DungeonBuilder.WorldToCell(me.GlobalPosition))})");
                return;
            }
            case 15:
            {
                // the merchant: buy his first ware and sell it back, from inside the safe room (the server refuses trades away from him)
                var me = main.Party.Local!;
                var bag = main.Party.Bag;
                var shops = client.Trace.Count(t => t == "shop");
                if (_shopPhase == 0)
                {
                    var stock = main.Party.ShopStockFor(Game.Instance.CurrentFloor);
                    if (stock.Items.Count == 0) { Finish("[net]", false, "the merchant has no stock"); return; }
                    var price = Items.Prices.BuyPrice(stock.Items[0]);
                    if (main.Party.Gold < price) { if (_clock > 8) Finish("[net]", false, $"never had the gold for the shop ({main.Party.Gold} < {price})"); return; }
                    _shopHeld = Enumerable.Range(0, bag.Capacity).Where(i => bag[i] != null).ToHashSet();
                    _bagBefore = bag.Items.Count(); _shopsBefore = shops; _goldBefore = main.Party.Gold;
                    main.Party.ShopBuy(0); // a client sends ShopBuyIntent
                    _shopPhase = 1; _clock = 0; return;
                }
                if (_shopPhase == 1)
                {
                    var bought = Enumerable.Range(0, bag.Capacity).FirstOrDefault(i => bag[i] != null && !_shopHeld.Contains(i), -1);
                    if (shops > _shopsBefore && bag.Items.Count() == _bagBefore + 1 && bought >= 0 && main.Party.Gold < _goldBefore)
                    {
                        GD.Print($"[net] bought {bag[bought]} into cell {bought}: gold {_goldBefore} -> {main.Party.Gold}, sold [{string.Join(",", main.Party.ShopSoldOnFloor)}]");
                        _bagBefore = bag.Items.Count(); _shopsBefore = shops; _goldBefore = main.Party.Gold;
                        main.Party.ShopSell(Party.BagSlot.Bag(bought)); // a client sends ShopSellIntent
                        _shopPhase = 2; _clock = 0;
                    }
                    else if (_clock > 8) Finish("[net]", false, $"the purchase never landed (bag {bag.Items.Count()} vs {_bagBefore}, gold {main.Party.Gold} vs {_goldBefore}, shop syncs {shops - _shopsBefore}, trace=[{string.Join(", ", client.Trace.Distinct())}])");
                    return;
                }
                if (shops > _shopsBefore && bag.Items.Count() == _bagBefore - 1 && main.Party.Gold > _goldBefore)
                {
                    GD.Print($"[net] sold it back: gold {_goldBefore} -> {main.Party.Gold}, bag {_bagBefore} -> {bag.Items.Count()}");
                    GD.Print("[net] bought and sold at the merchant");
                    me.Teleport(_safeReturn!.Value);
                    _clock = 0; _step = 16;
                }
                else if (_clock > 8) Finish("[net]", false, $"the sale never landed (bag {bag.Items.Count()} vs {_bagBefore}, gold {main.Party.Gold} vs {_goldBefore}, shop syncs {shops - _shopsBefore})");
                return;
            }
            case 16:
            {
                // cheer: V goes to the server as a CheerIntent; the server picks our hero's line and it comes back as a BarkMsg for our member
                if (!_cheerSent) { _cheerSent = true; _barksBefore = client.Trace.Count(t => t == $"bark:{client.MemberIndex}"); net.Send(new CheerIntent()); _clock = 0; return; }
                if (client.Trace.Count(t => t == $"bark:{client.MemberIndex}") > _barksBefore) { GD.Print($"[net] cheered: our hero's line came back from the server"); _clock = 0; _step = 17; }
                else if (_clock > 3) Finish("[net]", false, $"the cheer never came back as our bark (trace=[{string.Join(", ", client.Trace.Distinct())}])");
                return;
            }
            case 17:
            {
                // the effect sync (v14): the server gives our hero a 2.5 s Haste on our cheer; our sheet must show it start and end.
                // Both used to stay unsent (no MemberChanged), while every swing sent the whole sheet (MemberSync)
                var hasted = main.Party.Members[client.MemberIndex].Effects.Has(Combat.StatusKind.Haste);
                if (_hasteSeenAt < 0)
                {
                    if (hasted) { _hasteSeenAt = _clock; GD.Print($"[net] effect sync: the server's Haste reached our sheet after {_clock:0.0} s"); }
                    else if (_clock > 6) Finish("[net]", false, "effect sync: the Haste the server gave us never reached our sheet");
                    return;
                }
                if (!hasted) { GD.Print($"[net] effect sync: its end reached us {_clock - _hasteSeenAt:0.0} s later"); _clock = 0; _step = 8; }
                else if (_clock - _hasteSeenAt > 8) Finish("[net]", false, "effect sync: the Haste ran out on the server but our sheet still shows it");
                return;
            }
            case 8:
                if (client.SnapshotsSeen < 10 && _clock < 10) return;
                // leave, then come back under the same name: same member, same bag, same floor
                _firstRun = $"hash {(main.LayoutHashMatched ? "match" : "MISMATCH")}, snapshots {client.SnapshotsSeen}, rtt {client.Rtt * 1000:0} ms, trace=[{string.Join(", ", client.Trace.Distinct())}]";
                _bagAtLeave = main.Party.Bag.Items.Count();
                _memberAtLeave = client.MemberIndex;
                _goldAtLeave = main.Party.Gold;
                GD.Print($"[net] leaving with {_bagAtLeave} bag item(s) as member {_memberAtLeave}; rejoining shortly");
                net.Disconnect("test: leaving to rejoin");
                _clock = 0; _step = 9;
                return;
            case 10:
                if (!client.Trace.Contains("ready")) { if (_clock > 40) Finish("[net]", false, $"rejoin never became ready ({net.Status})"); return; }
                if (client.SnapshotsSeen < 5) return;
                var back = main.Party.Bag.Items.Count();
                var okRejoin = client.MemberIndex == _memberAtLeave && back >= _bagAtLeave && Game.Instance.CurrentFloor == 2 && main.LayoutHashMatched && main.Party.Gold == _goldAtLeave;
                _okRejoin = okRejoin;
                GD.Print($"[net] rejoined as member {client.MemberIndex} (was {_memberAtLeave}), bag {back} (was {_bagAtLeave}), gold {main.Party.Gold} (was {_goldAtLeave}), floor {Game.Instance.CurrentFloor}");
                _clock = 0; _step = 13;
                return;
            case 13:
            {
                // the pile the server left while we were away: pick it up through the server, which only works if the client rebuilt it with the server's id
                var me = main.Party.Local!;
                var pile = main.Builder.FloorRoot?.GetChildren().OfType<Items.LootDrop>().FirstOrDefault(d => d.Coins == 37);
                if (pile == null && !_askedRejoinPile) { if (_clock > 5) Finish("[net]", false, $"rejoin pile pickup: FAIL, the 37 gold pile never reached the rejoined client welcome carried {client.Welcome?.FloorState.Items.Count} item(s)"); return; }
                if (!_askedRejoinPile && pile != null)
                {
                    _askedRejoinPile = true; _goldBefore = main.Party.Gold;
                    me.Teleport(pile.GlobalPosition + new Vector3(0.6f, 0.2f, 0.6f), pile.GlobalPosition);
                    net.Send(new InteractIntent(NetRefs.Loot(pile.Item.Id)));
                    _clock = 0;
                    return;
                }
                var pileOk = main.Party.Gold == _goldBefore + 37;
                if (!pileOk && _clock < 8) return;
                GD.Print($"[net] rejoin pile pickup: {(pileOk ? "OK" : "FAIL")} (gold {_goldBefore} -> {main.Party.Gold})");
                var ok = _okRejoin && pileOk && _firstRun.Contains("hash match");
                _done = true;
                GD.Print($"[net] RESULT {(ok ? "ok" : "FAIL")}: first run {_firstRun}; rejoin {(_okRejoin ? "ok" : "FAIL")}");
                net.Disconnect("test finished");
                GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(ok ? 0 : 1);
                return;
            }
        }
    }

    private Enemies.Enemy? _target;
    private Dungeon.Props.Chest? _chest;
    private bool _placed, _placed2, _placed3, _placed4, _askedPickup, _askedChest, _askedStairs;
    private double _swingClock, _chaseClock;
    private int _swings, _bagBefore, _lootBefore;
    private bool _fightHeard;
    private bool _botAsked;
    private int _guardPhase, _guardBot = -1, _guardIndex = -1, _guardCount;
    private Guid _guardId;
    private bool _heardMoved, _heardNotYours, _guardListening;
    private void GuardHeard(string line)
    {
        if (line == ServerWorld.ItemMovedLine) _heardMoved = true;
        else if (line == ServerWorld.NotYoursLine) _heardNotYours = true;
    }
    private bool _propsSpoiled, _doorTruth, _leverTruth;
    private Dungeon.Props.Trap? _fakeTrap, _spoiledTrap;
    private Dungeon.Props.Door? _spoiledDoor;
    private Dungeon.Props.Lever? _spoiledLever;
    private (bool Triggered, bool Disarmed) _trapTruth;
    private Guid _ghostId, _lostId;
    private bool _enemiesSpoiled;
    private Enemies.Enemy? _ghostEnemy;
    private int _lostEnemy = -1, _countTruth, _enemyChecksSeen;
    private Guid _countDrop;
    private List<int>? _lastEnemyCheck;
    private void NoteEnemyCheck(NetMessage m) { if (m is EnemyCheckMsg ec) { _lastEnemyCheck = ec.Alive; _enemyChecksSeen++; } }
    private int _goldTruth;
    private Guid _bagTruth;
    private bool _stairsTruth;
    private Vector3? _botFollowFrom;
    private double _botPrint;
    private bool _cheerSent, _hasteGiven;
    private double _hasteSeenAt = -1;
    private int _partySyncs;
    private void CountPartySync(NetMessage m) { if (m is PartySync) _partySyncs++; }
    private int _barksBefore;
    private int _killsBefore, _deathCues, _hurtCues, _hitFxBefore;
    private double _settle;

    /// <summary>The fight's cues on the client: enemy death and hurt sounds, grunts or the generic thuds.</summary>
    private void FightCue(string cue)
    {
        if (cue.StartsWith("grunt_death_") || cue == "enemy_die") _deathCues++;
        else if (cue.StartsWith("grunt_hurt_") || cue == "enemy_hurt") _hurtCues++;
    }
    private string _firstRun = "";
    private int _bagAtLeave, _memberAtLeave, _goldBefore, _goldAtLeave;
    private Vector3? _lastLead;
    private bool _askedCoins, _pileLeft, _okRejoin, _askedRejoinPile;
    private void Trace(string what) => GD.Print($"[net] {what}");
    private Vector3? _startAfterPose, _hazardReturn, _safeReturn;
    private int _placements, _placementsAtFloor2;
    private bool _placedJoin;
    private bool _goldGranted, _dealSentTwice, _tavernJoinSeen, _potionsGranted, _propsRevealed;
    private int _shopPhase, _shopsBefore;
    private HashSet<int> _shopHeld = new();
    /// <summary>The server placed our body (a ForcePose): the start position on floor 1, and the arrival spot after a floor change.</summary>
    public void NotePlaced(Vector3 at) { _startAfterPose = at; _placements++; }
}
