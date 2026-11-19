using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The relief cart (plan 7, the spec's "The relief cart"): single player, a player left behind by the train
/// (<see cref="ReliefRules.LeftBehind"/>: up on foot, the train's back more than 12 m ahead, and not a standing cart that has met
/// them) gets the co-op chaser sent to them <see cref="ReliefRules.FirstWait"/> later (<see cref="ReliefClock"/>). It is placed at the
/// stop behind them, clear of them, and comes down the line to them (<see cref="ReliefPhase.Coming"/>): it stops beside them, or with
/// its back <see cref="ReliefRules.PastRunOver"/> past a player on the rails whom it ran over, or waits where it was placed when they
/// stand within <see cref="ReliefRules.WaitAtStopRange"/> of it. It waits <see cref="ReliefRules.WaitBeside"/> ("Board the cart"); boarded,
/// it counts down and runs up behind the train as the chaser does, and sets its riders down there. Unboarded it rolls on empty after
/// the train and is hidden once it stops; the next comes <see cref="ReliefRules.NextWait"/> after it set off. It spends no spare, is
/// not saved (a load starts the clock clean), and never runs online.
/// </summary>
public partial class RailController
{
    private readonly ReliefClock _reliefClock = new();
    /// <summary>The standing cart has met the player since it stopped (<see cref="ReliefRules.Met"/>). False on a build and a load.</summary>
    private bool _met;
    /// <summary>The player stood on the rails at the last tick's test.</summary>
    private bool _playerOnRails;

    /// <summary>Reliefs sent since the floor was built or loaded (test hook).</summary>
    internal int ReliefsSent { get; private set; }
    /// <summary>Times a relief ran over the local player (test hook): companions, crew and enemies are not counted.</summary>
    internal int ReliefHits { get; private set; }
    /// <summary>The player is left behind now (<see cref="ReliefRules.LeftBehind"/>; test hook).</summary>
    internal bool LeftBehindNow { get; private set; }
    /// <summary>The live relief's phase, or null when none runs (test hook).</summary>
    internal ReliefPhase? ReliefPhaseNow => _chase is { Relief: true, Ended: false } c ? c.Phase : null;
    /// <summary>The relief cart standing on the line (running, or waiting to be hidden), or null (test hook).</summary>
    internal MineCart? ReliefCart => _chase is { Relief: true } c && IsInstanceValid(c.Cart) ? c.Cart : null;
    /// <summary>The relief's centre S, or NaN when none (test hook).</summary>
    internal float ReliefRunS => _chase is { Relief: true } c && IsInstanceValid(c.Cart) ? c.Run.State.S : float.NaN;
    /// <summary>The stop the live relief was sent from, or −1 (test hook).</summary>
    internal int ReliefSpawnStop { get; private set; } = -1;
    /// <summary>The local player's S on the party line at the last tick, or NaN when none (test hook).</summary>
    internal float PlayerLineS { get; private set; } = float.NaN;

    /// <summary>A load: no relief counted or sent, and the standing cart has met nobody yet.</summary>
    private void ResetRelief()
    {
        _reliefClock.Reset();
        _met = false;
        ReliefsSent = 0;
        ReliefHits = 0;
        ReliefSpawnStop = -1;
    }

    /// <summary>
    /// Each physics tick, solo, after the chase's tick: where the player stands on the line, whether the standing cart has met them,
    /// whether they are left behind, and the relief's clock (it counts while no relief is out, or while the last one rolls on without
    /// them). No allocation.
    /// </summary>
    private void TickRelief(PartyController party, float dt)
    {
        if (NetSession.IsNetworked || DebugTools.Instance?.NoRelief == true || StopS.Length < 2) return;
        if (party.Local is not { } me || !IsInstanceValid(me)) { PlayerLineS = float.NaN; LeftBehindNow = false; return; }
        PlayerLineS = Party.Track.ClosestS(me.GlobalPosition);
        Party.Track.Measure(me.GlobalPosition, PlayerLineS, 2f, out var b);
        _playerOnRails = RunOverRules.OnRails(b with { Radius = RunOverSweep.BodyRadius });
        var lastBackS = TrainBackS + CartRules.HalfLength; // the train's last body's back
        var behind = ReliefRules.Behind(lastBackS, PlayerLineS);
        _met = ReliefRules.Met(_met, State.Mode == CartMode.Rolling, me.Cart == Cart, behind);
        var live = ReliefRules.LineLive(StopS.Length, StopIndex, State.Mode, Halted, !Cart.Visible);
        LeftBehindNow = ReliefRules.LeftBehind(new ReliefView(true, live, !me.Downed && !me.Parked, me.Seated, State.Mode != CartMode.Rolling, _met, behind));
        var counting = _chase is null or { Ended: true } or { Relief: true, Phase: ReliefPhase.Leaving };
        if (_reliefClock.Tick(dt, LeftBehindNow, counting)) SendRelief(me, PlayerLineS, _playerOnRails);
    }

    /// <summary>A relief cart is placed Held at the stop behind the player (clear of them, behind the train), on its way to their seat.</summary>
    private void SendRelief(Adventurer me, float playerS, bool onRails)
    {
        FreeChaser(); // one still standing (hidden soon, or rolling on without the player) goes now: one relief at a time
        if (GetParent() is not Node3D root) return;
        var stop = ReliefRules.SpawnStop(StopS, playerS);
        var enc = RailEncounterController.Instance is { } e && IsInstanceValid(e) && e.GetParent() == GetParent() ? e : null;
        if (enc != null) enc.CollectPartyLaneHoles(_chaseHoles);
        else _chaseHoles.Clear();
        var s = SpareRules.HoleLimit(ReliefRules.SpawnS(StopS[stop], playerS, ChaseLimitNow()), _chaseHoles);
        var n = _reliefClock.Sent;
        // a unique name (Godot renames a second "ReliefCart" while the last is queued for deletion); solid and usable from the start: one
        // placed beside the player never rolls, and the look ray sees only layer 8
        var cart = new MineCart { Name = $"ReliefCart{n}", CartId = CartIds.Chaser(stop), PromptFor = ReliefPromptFor, OnUse = UseRelief, CollisionLayer = ChaserHeldLayer }.AsChaser();
        root.AddChild(cart);
        var run = new CartRun(Party.Track, cart, new CartState(s, 0f, CartMode.Held, CartRules.MaxPlating)) { Limit = s };
        run.PoseAt(s);
        run.Sync();
        ChaserPlacedSound(cart, run.Track, s);
        var target = ReliefRules.SeatS(s, playerS, onRails);
        _chase = new Chase { Relief = true, Phase = ReliefPhase.Coming, Stop = stop, Cart = cart, Run = run, SpawnS = s, Target = target, Boarded = false };
        _chaseHitAt.Clear();
        _chaseWarned.Clear();
        ReliefsSent = n;
        ReliefSpawnStop = stop;
        EventBus.Instance.Log("A relief cart comes down the line.");
        GD.Print($"[rails] relief {n}: sent from stop {stop} at S={s:0.0} for the player at S={playerS:0.0} ({(onRails ? "on the rails" : "beside the line")}), seat at S={target:0.0}");
        RefreshHud();
    }

    /// <summary>
    /// The relief's part of the chase's tick (<see cref="TickChase"/>, after the shared limit: the train, a live heap's face, the line's
    /// holes): its phase's work, its run (<see cref="StepChaser"/>), the next phase (<see cref="ReliefRules.Next"/>), and its end once it
    /// stands behind a held train with its rider, or has rolled on without them and stopped. No allocation.
    /// </summary>
    private void TickReliefChase(PartyController party, Chase c, RailEncounterController? enc, float limit, float trainLimit, float dt)
    {
        var run = c.Run;
        var playerIn = PlayerIn(c.Cart);
        switch (c.Phase)
        {
            case ReliefPhase.Coming:
                // the player's S and rails test are the last tick's (TickRelief runs after this); the seat only moves forward and never over a hole
                if (!float.IsNaN(PlayerLineS)) c.Target = ReliefRules.Target(c.Target, ReliefRules.SeatS(c.SpawnS, PlayerLineS, _playerOnRails));
                limit = ReliefRules.SeatLimit(limit, c.Target, _chaseHoles);
                break;
            case ReliefPhase.Waiting:
                c.Waited += dt;
                break;
            case ReliefPhase.Riding:
                if (ReliefRules.CountsDown(c.Boarded, playerIn)) // the chaser's countdown, only with a player in it: with nobody left in it (even in its last tick) it waits again (below), it never ends here
                {
                    CountChasers(party, c.Cart, out var seated, out var living);
                    if (c.Boarding.Tick(dt, seated, living)) c.Boarded = true;
                }
                break;
        }
        if (run.State.Mode == CartMode.Held && ReliefRules.Rolls(c.Phase, c.Boarded, playerIn, run.State.S, limit))
        {
            run.State = run.State with { Mode = CartMode.Rolling };
            run.Settle(); // from a standstill: nothing banked from the wait
            c.Cart.CollisionLayer = 0; // a rolling relief runs bodies over; it offers nothing to use
            c.Departed = true;
        }
        StepChaser(party, c, enc, limit, dt);
        var held = run.State.Mode == CartMode.Held;
        var next = ReliefRules.Next(c.Phase, held, PlayerIn(c.Cart), c.Waited);
        if (next != c.Phase)
        {
            var from = c.Phase;
            c.Phase = next;
            if (next == ReliefPhase.Waiting)
            {
                c.Waited = 0f;
                c.Boarded = false;
                c.Boarding.Cancel();
                if (from == ReliefPhase.Riding) SetDownChasers(c.Cart); // no player left in it: the bench and a downed rider never ride up without them
                EventBus.Instance.Log("The relief cart waits for you.");
                var me = Main.Instance?.Party?.Local;
                var tub = me != null && IsInstanceValid(me) ? c.Cart.TubDistance(me) : float.NaN;
                GD.Print($"[rails] the relief cart waits at S={run.State.S:0.0}; the player at S={PlayerLineS:0.0}, {tub:0.0} m from its tub");
            }
            else if (next == ReliefPhase.Leaving)
            {
                _reliefClock.Went();
                c.Departed = false; // it ends only after it has rolled on (below)
                c.Cart.CollisionLayer = 0;
                EventBus.Instance.Log("The relief cart rolls on without you.");
                GD.Print($"[rails] the relief cart rolls on without the player from S={run.State.S:0.0}");
            }
            else if (next == ReliefPhase.Riding && !c.Boarded && !c.Boarding.Running) c.Boarding.Start();
            RefreshHud();
            return;
        }
        if (c.Phase == ReliefPhase.Riding && SpareRules.ChaseDone(c.Boarded, held, State.Mode == CartMode.Held, run.State.S, trainLimit))
        {
            LastChaseGap = TrainBackS + CartRules.HalfLength - (run.State.S + CartRules.HalfLength); // the last body's back to the relief's front
            ChasesDone++;
            EventBus.Instance.Log("The relief cart pulls up behind the train.");
            GD.Print($"[rails] the relief cart pulls up behind the train at S={run.State.S:0.0}, {LastChaseGap:0.00} m short of its last body");
            EndChase(atOnce: false);
        }
        else if (c.Phase == ReliefPhase.Leaving && held)
        {
            if (c.Departed) EndChase(atOnce: false); // it rolled on and stopped: hidden after HideSeconds
            else if (!ReliefRules.Rolls(c.Phase, c.Boarded, playerIn, run.State.S, limit)) EndChase(atOnce: true); // nowhere to roll on to: gone now
        }
    }

    /// <summary>The live relief's prompt for <paramref name="hero"/>: "Board the cart" while it waits in reach, the chaser's "Get out" for its rider.</summary>
    private string ReliefPromptFor(Adventurer hero)
    {
        if (NetSession.IsNetworked || _chase is not { Relief: true, Ended: false } c || !IsInstanceValid(c.Cart) || !IsInstanceValid(hero)) return "";
        var onFoot = IsPlayer(hero) && !hero.Seated && !hero.Downed;
        return ReliefRules.Prompt(c.Phase, c.Run.State.Mode == CartMode.Held, hero.Cart == c.Cart, onFoot, RideInputRules.InBoardReach(c.Cart.TubDistance(hero)),
            hero.Downed, ChaserBoardingLeft);
    }

    /// <summary>
    /// The player uses the relief: boarding it seats them, their companions on foot nearby and the escorts on its bench, and starts its
    /// countdown; getting out sets them and their companions down beside the lane (with no player left in it, it waits again: the
    /// relief's next tick sets every other rider down). It never ends the relief or touches a spare.
    /// </summary>
    private void UseRelief(Adventurer actor)
    {
        if (NetSession.IsNetworked || !Current || _chase is not { Relief: true, Ended: false } c || !IsInstanceValid(c.Cart) || !IsInstanceValid(actor)) return;
        if (ReliefPromptFor(actor).Length == 0) return;
        if (actor.Cart == c.Cart)
        {
            _exitTaken.Clear();
            SetDownFromChaser(c.Cart, actor);
            _chaseRiders.Clear();
            foreach (var rider in c.Cart.Riders.Values) _chaseRiders.Add(rider);
            foreach (var rider in _chaseRiders)
                if (rider is Adventurer mate && IsInstanceValid(mate) && OwnCompanion(mate, actor)) SetDownFromChaser(c.Cart, mate);
            _chaseRiders.Clear();
        }
        else
        {
            if (!SeatIn(c.Cart, actor)) return;
            foreach (var mate in Main.Instance.Party.Active)
                if (mate != actor && !mate.Downed && !mate.Seated && OwnCompanion(mate, actor) && mate.GlobalPosition.DistanceTo(actor.GlobalPosition) <= CompanionBoardRange)
                    SeatIn(c.Cart, mate);
            SeatBench(c.Cart, actor.GlobalPosition);
            c.Boarding.Start();
            c.Phase = ReliefPhase.Riding;
            EventBus.Instance.Log($"{actor.State.Name} climbs into the relief cart.");
        }
        RefreshHud();
        Flush();
    }

    /// <summary>A relief's run-over hit the local player (one count per hit; companions, crew and enemies are not counted). No allocation.</summary>
    private void CountReliefHits()
    {
        var me = Main.Instance?.Party?.Local;
        if (me == null) return;
        foreach (var (body, _) in _chaseHits)
            if (body == me) ReliefHits++;
    }
}
