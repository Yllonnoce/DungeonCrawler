using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-relief[=beside|rails|refuse|ahead] -------------------------------------------------------------
// The relief cart (plan 7) on the built-in campaign's floor 12, solo, lethality 1, no enemies. beside, rails and refuse open alike:
// board at the landing, jump from the cart well short of stop 1 and stand beside the line (beside, refuse) or on it (rails); the
// player is left behind within 6 s and a relief comes from the landing 15 s later. beside: it stops beside them, is boarded, runs up
// behind the train at stop 1 and sets them down; no second comes. rails: it runs them over once (hurt, not downed) and stops just
// past them, then is boarded as beside. refuse: it waits 30 s and rolls on; another comes 15 s later; a save and a load while it
// waits leave no relief, nobody seated and the clock clean, and a relief comes 15 s after the load. ahead: the player rides to
// stop 1, gets out and walks ahead of the train, then back behind the cart that met them: no relief comes. Prints [relief] lines
// and a RESULT.
public partial class DebugTools
{
    private bool _testRelief;
    private string _relVariant = "beside";
    private int _relStep;
    /// <summary>Seconds in this step, and seconds since the flow began (the flow's own clock).</summary>
    private float _relClock, _relTime;
    private float _relT0, _relWaitAt, _relLeaveAt, _relLoadAt;
    private float _relMaxHp, _relHitS = float.NaN, _relLeaveS;
    private bool _relLoadSeen;

    private void RunReliefTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        var dt = (float)GetProcessDeltaTime();
        _relClock += dt;
        _relTime += dt;
        var variant = _relVariant;
        void Fail(string why) { GD.Print($"[relief] RESULT FAIL ({variant}): {why}"); _done = true; GetTree().Quit(1); }
        void Next() { _relStep++; _relClock = 0f; }
        var party = main.Party;
        var rc = RailController.Instance;
        // no enemies: each is freed as it appears, never killed, so no XP is paid and nobody levels up mid-test (a level-up raises max HP
        // after the jump's reading, and the rails variant's run-over check divides by it; plan 14 Task 8: r1_hounds' hound did that)
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (!e.IsQueuedForDeletion()) e.QueueFree();
        if (rc == null || !IsInstanceValid(rc)) { if (_relClock > 3f) Fail("no RailController on floor 12"); return; }
        if (party.Local is not { } me) { if (_relClock > 3f) Fail("no local hero"); return; }

        // one relief at a time: never two ReliefCart nodes standing under the floor root
        var standing = root.GetChildren().OfType<MineCart>().Count(m => m.Name.ToString().StartsWith("ReliefCart") && !m.IsQueuedForDeletion());
        if (standing > 1) { Fail($"{standing} relief carts stand under the floor root"); return; }

        if (variant == "ahead") { RunReliefAhead(main, layout, rc, me, Fail, Next); return; }

        switch (_relStep)
        {
            // ---- the common opening: board at the landing, jump well short of stop 1 ----
            case 0 when _relClock >= 1.5f:
            {
                if (rc.StopS.Length < 3 || rc.StopS[1] - rc.StopS[0] < 45f) { Fail($"stop 1 is too close to the landing (StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}])"); return; }
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                GD.Print($"[relief] 0 boarded at the landing: StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}]");
                Next();
                break;
            }
            case 1:
            {
                if (rc.State.Mode != CartMode.Rolling || rc.State.S < rc.StopS[0] + 25f || rc.State.V < 4f)
                {
                    if (_relClock > 30f) Fail($"the cart never rolled 25 m past the landing at 4 m/s (mode {rc.State.Mode}, S={rc.State.S:0.0}, V={rc.State.V:0.00})");
                    break;
                }
                if (me.Cart != rc.Cart) { Fail("the human is not seated in the rolling cart"); return; }
                var v = rc.State.V;
                rc.Cart.Interact(party, me); // a tap at speed: thrown
                if (me.Seated) { Fail("the human is still seated after the jump"); return; }
                var landS = rc.Party.Track.ClosestS(me.GlobalPosition);
                var at = variant == "rails" ? rc.Party.Track.Point(landS) + Vector3.Up * 0.05f : rc.StandBesideLane(me.GlobalPosition);
                me.Teleport(at);
                _relMaxHp = me.State.Stats[Characters.Stat.MaxHp];
                me.State.Hp = _relMaxHp;
                GD.Print($"[relief] 1 jumped at V={v:0.00} m/s, the cart at S={rc.State.S:0.0}; stands {(variant == "rails" ? "on the rails" : "beside the line")} at S={landS:0.0}");
                Next();
                break;
            }
            case 2: // R1
            {
                if (rc.LeftBehindNow)
                {
                    _relT0 = _relTime;
                    GD.Print($"[relief] R1 left behind {_relClock:0.0} s after the jump: the player at S={rc.PlayerLineS:0.0}, the cart {rc.State.Mode} at S={rc.State.S:0.0}");
                    Next();
                }
                else if (_relClock > 6f) Fail($"not left behind within 6 s (the player at S={rc.PlayerLineS:0.0}, the cart {rc.State.Mode} at S={rc.State.S:0.0})");
                break;
            }
            case 3: // R2
            {
                var since = _relTime - _relT0;
                if (rc.ReliefsSent == 0) { if (since > ReliefRules.FirstWait + 0.5f) Fail($"no relief {since:0.0} s after the player was left behind"); break; }
                GD.Print($"[relief] R2 relief 1 sent {since:0.00} s after the player was left behind, from stop {rc.ReliefSpawnStop} at S={rc.ReliefRunS:0.0}");
                if (rc.ReliefsSent != 1 || Mathf.Abs(since - ReliefRules.FirstWait) > 0.5f) { Fail($"relief {rc.ReliefsSent} sent at {since:0.00} s, not 1 at 15 ± 0.5 s"); return; }
                if (rc.ReliefSpawnStop != 0) { Fail($"the relief came from stop {rc.ReliefSpawnStop}, not the landing"); return; }
                _relStep = variant == "rails" ? 20 : 4;
                _relClock = 0f;
                break;
            }
            case 4: // R3
            {
                if (!ReliefWaitsBeside(rc, me, Fail, "R3")) break;
                _relWaitAt = _relTime;
                _relStep = variant == "refuse" ? 30 : 5;
                _relClock = 0f;
                break;
            }
            case 5: // R4
            {
                if (rc.ReliefCart is not { } relief) { Fail("the relief is gone before boarding"); return; }
                relief.Interact(party, me);
                if (me.Cart != relief || rc.ReliefPhaseNow != ReliefPhase.Riding) { Fail($"boarding the relief: seated in it={me.Cart == relief}, phase {rc.ReliefPhaseNow}"); return; }
                GD.Print($"[relief] R4 boarded the relief at S={rc.ReliefRunS:0.0}: {relief.Riders.Count} rider(s)");
                Next();
                break;
            }
            case 6:
            {
                var hud = rc.HudText ?? "";
                if (hud.StartsWith("Relief cart — departs in")) { GD.Print($"[relief] R4 the bar reads \"{hud}\""); Next(); }
                else if (_relClock > 1f) Fail($"the bar reads \"{hud}\", not the relief's countdown");
                break;
            }
            case 7: // R5
            {
                if (rc.ChasesDone < 1 || me.Seated) { if (_relClock > 90f) Fail($"the relief never pulled up behind the train within 90 s (phase {rc.ReliefPhaseNow}, S={rc.ReliefRunS:0.0}, the train at S={rc.State.S:0.0})"); break; }
                var cell = DungeonBuilder.WorldToCell(me.GlobalPosition);
                GD.Print($"[relief] R5 pulled up behind the train after {_relClock:0.0} s: gap {rc.LastChaseGap:0.00} m, the train {rc.State.Mode} at stop {rc.StopIndex}; the player set down at {cell} (rail {layout.IsRail(cell)})");
                if (rc.ChasesDone != 1) { Fail($"{rc.ChasesDone} chases done"); return; }
                if (layout.IsRail(cell)) { Fail($"the player was set down on a rail cell {cell}"); return; }
                if (!(rc.LastChaseGap >= 0f && rc.LastChaseGap <= 3f)) { Fail($"the relief stopped {rc.LastChaseGap:0.00} m short of the train's last body"); return; }
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1) { Fail($"the party cart is {rc.State.Mode} at stop {rc.StopIndex}, not held at stop 1"); return; }
                Next();
                break;
            }
            case 8:
            {
                if (rc.ReliefCart == null) { GD.Print($"[relief] R5 the relief is gone {_relClock:0.0} s later"); Next(); }
                else if (_relClock > 3f) Fail("the relief was not hidden within 3 s");
                break;
            }
            case 9: // R6
            {
                if (rc.LeftBehindNow) { if (_relClock > 2f) Fail("still left behind beside the train"); break; }
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                if (me.Cart != rc.Cart) { Fail("the player could not board the party cart at stop 1"); return; }
                GD.Print("[relief] R6 no longer left behind; boarded the party cart at stop 1");
                Next();
                break;
            }
            case 10 when _relClock >= 2f:
            {
                if (rc.ReliefsSent != 1) { Fail($"{rc.ReliefsSent} reliefs sent, not 1"); return; }
                ReliefPass();
                break;
            }

            // ---- rails: run over once, stops just past ----
            case 20: // R3'
            {
                if (float.IsNaN(_relHitS) && rc.ReliefHits > 0)
                {
                    _relHitS = rc.PlayerLineS;
                    GD.Print($"[relief] R3' run over at S={_relHitS:0.0} by the relief at S={rc.ReliefRunS:0.0}: hp {me.State.Hp:0}/{_relMaxHp:0} downed={me.Downed}");
                }
                if (rc.ReliefPhaseNow != ReliefPhase.Waiting) { if (_relClock > 25f) Fail($"the relief did not wait within 25 s (phase {rc.ReliefPhaseNow}, hits {rc.ReliefHits})"); break; }
                var lost = (_relMaxHp - me.State.Hp) / _relMaxHp;
                var past = _relHitS + CartRules.HalfLength + ReliefRules.BodyRadius + ReliefRules.PastRunOver;
                GD.Print($"[relief] R3' waits at S={rc.ReliefRunS:0.0} (at least {past - 0.1f:0.0}): hits {rc.ReliefHits}, lost {lost * 100f:0.0}% (expected {HazardRules.DamageFraction(1f) * 100f:0.0}%), downed={me.Downed}");
                if (rc.ReliefHits < 1 || float.IsNaN(_relHitS)) { Fail("the relief never ran the player over"); return; }
                if (me.Downed || Mathf.Abs(lost - HazardRules.DamageFraction(1f)) > 0.02f) { Fail($"the player lost {lost * 100f:0.0}% (downed={me.Downed}): not hurt once"); return; }
                if (rc.ReliefRunS < past - 0.1f) { Fail($"the relief waits at S={rc.ReliefRunS:0.0}, not past the player hit at S={_relHitS:0.0}"); return; }
                if (rc.ReliefCart is { } relief && !RideInputRules.InBoardReach(relief.TubDistance(me)))
                {
                    me.Teleport(rc.StandBesideLane(relief.GlobalPosition));
                    GD.Print($"[relief] R3' out of reach: stepped beside the relief ({relief.TubDistance(me):0.0} m from its tub)");
                }
                _relStep = 5;
                _relClock = 0f;
                break;
            }

            // ---- refuse: it waits 30 s, rolls on, another comes; a save and a load while it waits ----
            case 30: // R7
            {
                if (rc.ReliefPhaseNow != ReliefPhase.Leaving) { if (_relTime - _relWaitAt > ReliefRules.WaitBeside + 0.5f) Fail($"the relief has not left {_relTime - _relWaitAt:0.0} s after it began to wait (phase {rc.ReliefPhaseNow})"); break; }
                var waited = _relTime - _relWaitAt;
                GD.Print($"[relief] R7 the relief rolls on {waited:0.00} s after it began to wait, from S={rc.ReliefRunS:0.0}; hits {rc.ReliefHits}");
                if (Mathf.Abs(waited - ReliefRules.WaitBeside) > 0.5f) { Fail($"it left after {waited:0.00} s, not 30 ± 0.5"); return; }
                if (rc.ReliefHits != 0) { Fail($"the relief ran the player over {rc.ReliefHits} time(s)"); return; }
                _relLeaveAt = _relTime;
                _relLeaveS = rc.ReliefRunS;
                Next();
                break;
            }
            case 31:
            {
                if (rc.ReliefRunS > _relLeaveS + 0.05f) { GD.Print($"[relief] R7 the leaving relief rolls: S={rc.ReliefRunS:0.0}"); Next(); }
                else if (_relClock > 1f) Fail($"the leaving relief does not roll (S={rc.ReliefRunS:0.0}, phase {rc.ReliefPhaseNow})");
                break;
            }
            case 32: // R8
            {
                var since = _relTime - _relLeaveAt;
                if (rc.ReliefsSent < 2) { if (since > ReliefRules.NextWait + 0.5f) Fail($"no second relief {since:0.0} s after the first rolled on"); break; }
                GD.Print($"[relief] R8 relief 2 sent {since:0.00} s after the first rolled on, from stop {rc.ReliefSpawnStop} at S={rc.ReliefRunS:0.0}");
                if (rc.ReliefsSent != 2 || Mathf.Abs(since - ReliefRules.NextWait) > 0.5f) { Fail($"relief {rc.ReliefsSent} sent at {since:0.00} s, not 2 at 15 ± 0.5 s"); return; }
                Next();
                break;
            }
            case 33:
            {
                if (!ReliefWaitsBeside(rc, me, Fail, "R8")) break;
                Next();
                break;
            }
            case 34: // R9
            {
                GD.Print($"[relief] R9 save and load while relief 2 waits at S={rc.ReliefRunS:0.0}");
                main.SaveGame("relieftest");
                if (!main.LoadGame("relieftest")) { Fail("the save would not load"); return; }
                _relLoadSeen = false;
                Next();
                break;
            }
            case 35:
            {
                // the load's own controller (a rebuilt floor's), from its first tick: when the player is first left behind again
                if (!_relLoadSeen && rc.GetParent() == root && !rc.IsQueuedForDeletion() && rc.ReliefsSent == 0 && rc.LeftBehindNow)
                {
                    _relLoadSeen = true;
                    _relT0 = _relTime;
                    GD.Print($"[relief] R10 left behind {_relClock:0.00} s after the load (the player at S={rc.PlayerLineS:0.0}, the cart at S={rc.State.S:0.0})");
                }
                if (_relClock < 1f) break;
                var reliefs = root.GetChildren().OfType<MineCart>().Count(m => m.Name.ToString().StartsWith("ReliefCart"));
                var atStop = rc.StopIndex >= 0 && rc.StopIndex < rc.StopS.Length && Mathf.Abs(rc.State.S - rc.StopS[rc.StopIndex]) < 0.05f;
                GD.Print($"[relief] R9 after the load: {reliefs} relief node(s), seated={me.Seated}, reliefs sent {rc.ReliefsSent}, the cart {rc.State.Mode} at stop {rc.StopIndex} (at it: {atStop}); left behind {rc.LeftBehindNow}");
                if (reliefs > 0) { Fail("a relief cart node stands after the load"); return; }
                if (me.Seated) { Fail("the player is seated after the load"); return; }
                if (rc.ReliefsSent != 0) { Fail($"{rc.ReliefsSent} reliefs counted after the load"); return; }
                if (rc.State.Mode != CartMode.Held || !atStop) { Fail("the cart is not held at a stop after the load"); return; }
                _relLoadAt = _relTime - _relClock; // the load's frame
                if (!_relLoadSeen) { Fail("not left behind within 1 s of the load (the player must never be stranded by a load)"); return; }
                Next();
                break;
            }
            case 36: // R10
            {
                var since = _relTime - _relT0;
                if (rc.ReliefsSent == 0) { if (since > ReliefRules.FirstWait + 0.5f) Fail($"no relief {since:0.0} s after the load"); break; }
                GD.Print($"[relief] R10 relief 1 sent {since:0.00} s after the load left the player behind ({_relTime - _relLoadAt:0.00} s after the load)");
                if (rc.ReliefsSent != 1 || Mathf.Abs(since - ReliefRules.FirstWait) > 0.5f) { Fail($"relief {rc.ReliefsSent} sent at {since:0.00} s, not 1 at 15 ± 0.5 s"); return; }
                ReliefPass();
                break;
            }
        }
    }

    /// <summary>R3: the relief waits beside the player in reach, offers "Board the cart", and has hurt nobody. False while it is still coming.</summary>
    private bool ReliefWaitsBeside(RailController rc, Adventurer me, Action<string> fail, string tag)
    {
        if (rc.ReliefPhaseNow != ReliefPhase.Waiting || rc.ReliefCart is not { } relief)
        {
            if (_relClock > 25f) fail($"{tag}: the relief did not wait beside the player within 25 s (phase {rc.ReliefPhaseNow}, S={rc.ReliefRunS:0.0})");
            return false;
        }
        var tub = relief.TubDistance(me);
        var prompt = relief.Prompt;
        GD.Print($"[relief] {tag} the relief waits at S={rc.ReliefRunS:0.0} after {_relClock:0.0} s, the player at S={rc.PlayerLineS:0.0}, {tub:0.0} m from its tub: prompt \"{prompt}\", hits {rc.ReliefHits}, hp {me.State.Hp:0}/{_relMaxHp:0}");
        if (Mathf.Abs(rc.ReliefRunS - rc.PlayerLineS) > 0.6f) { fail($"{tag}: the relief waits {rc.ReliefRunS - rc.PlayerLineS:0.00} m from the player along the line"); return false; }
        if (tub > RideInputRules.BoardReach) { fail($"{tag}: the relief's tub is {tub:0.0} m away, out of reach"); return false; }
        if (prompt != "Board the cart") { fail($"{tag}: the relief offers \"{prompt}\""); return false; }
        if (rc.ReliefHits != 0 || me.State.Hp < _relMaxHp - 0.01f) { fail($"{tag}: the player was hurt (hits {rc.ReliefHits}, hp {me.State.Hp:0}/{_relMaxHp:0})"); return false; }
        return true;
    }

    /// <summary>ahead: ride to stop 1, get out, walk ahead of the train and then back behind the cart that met the player: no relief comes.</summary>
    private void RunReliefAhead(Main main, Dungeon.Generation.FloorLayout layout, RailController rc, Adventurer me, Action<string> fail, Action next)
    {
        var party = main.Party;
        switch (_relStep)
        {
            case 0 when _relClock >= 1.5f:
            {
                if (rc.StopS.Length < 3 || rc.StopS[2] - rc.StopS[1] < 40f || rc.StopS[1] - rc.StopS[0] < 40f) { fail($"the stops are too close (StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}])"); return; }
                if (!RideBoard(main, layout, rc, out var why)) { fail(why); return; }
                next();
                break;
            }
            case 1:
            {
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1) { if (_relClock > 60f) fail($"the cart did not reach stop 1 within 60 s (mode {rc.State.Mode}, S={rc.State.S:0.0})"); break; }
                if (me.Cart != rc.Cart) { fail("the player is not seated in the cart at stop 1"); return; }
                rc.Cart.Interact(party, me); // a tap gets out
                if (me.Seated) { fail("the player could not get out at stop 1"); return; }
                me.Teleport(rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[1] + 30f)));
                GD.Print($"[relief] A1 got out at stop 1 (S={rc.State.S:0.0}) and walked 30 m ahead of the train");
                next();
                break;
            }
            case 2:
            {
                if (rc.ReliefsSent != 0 || rc.LeftBehindNow) { fail($"ahead of the train: reliefs sent {rc.ReliefsSent}, left behind {rc.LeftBehindNow} (the player at S={rc.PlayerLineS:0.0})"); return; }
                if (_relClock < 20f) break;
                me.Teleport(rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[1] - 30f)));
                GD.Print($"[relief] A2 20 s ahead of the train: no relief; walked 30 m behind the cart");
                next();
                break;
            }
            case 3:
            {
                if (rc.ReliefsSent != 0) { fail($"behind the standing cart that met the player: {rc.ReliefsSent} relief(s) sent (the player at S={rc.PlayerLineS:0.0}, left behind {rc.LeftBehindNow})"); return; }
                if (_relClock < 20f) break;
                GD.Print($"[relief] A3 20 s behind the cart where the player left it (S={rc.PlayerLineS:0.0}): no relief");
                ReliefPass();
                break;
            }
        }
    }

    private void ReliefPass()
    {
        GD.Print($"[relief] RESULT ok ({_relVariant})");
        _done = true;
        GetTree().Quit();
    }
}
