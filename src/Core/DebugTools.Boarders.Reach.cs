using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders=reach (plan 14, Task 4): see DebugTools.Boarders.cs for the flow as a whole ----------------------------------
// =reach (Task 4, Ruling 13): restored at stop 1; on ride 2 the lead sits in W2 (rc.DebugSeat) and hounds that never drop below 1 HP are put on W1,
// W2 and W3; companions hold fire and nobody answers (DebugHoldAnswers).
//  - The reach: from W2's seat 0 to each rim spot, read off the wagons' poses every frame (the bodies' own spread printed beside it), on the straight and in bends: printed as "[boarders] reach: W2 seat 0
//    to its own spot ... m (straight), ... m (in a bend); to W1's spot ... m, to W3's ... m" (the nearest each neighbour came); BoarderRules.WagonReach
//    must be at least the larger own distance + 0.15 and under TrainRules.Spacing.
//  - The lead's forced blows: every melee attack its class has (the weapon's light, heavy and perfect swing, each melee strike ability), aimed at
//    each hound in turn, refilled before each: W1's and W3's hounds never lose HP or grip and take no hit at all; W2's takes the blows that land (a
//    W2 hound knocked off by its grip is replaced by a fresh one).
//  - A carried blow (fix round 1, review M6): a cleave off W2's hound (Adventurer.DebugCleave), widened past both neighbours' hounds, takes neither.
//  - From the cart: a blade companion seated in the cart strikes at W1's hound three times (Adventurer.DebugStrikeAt): it never lands. Then
//    the same companion, seated in W3, strikes at W2's hound (nearer than WagonReach) three times: it never lands either (the own-car rule).
public partial class DebugTools
{
    /// <summary>
    /// W2's seat 0 to each rim spot this frame, read off the wagons' own poses as they stand now (fix round 1, review M5): where a seated body
    /// and a boarder are both posed when a blow is judged in the physics tick. Its own (W2's) on the straight and in bends, the neighbours'
    /// nearest. The bodies themselves are also sampled (the lead's body to its own hound): in a rendered frame one of the two may already be
    /// posed for the frame while the other still stands where the physics tick left it, which is the 0.02 m spread a run measured.
    /// </summary>
    private void BdSampleReach(RailController rc, Adventurer me)
    {
        if (rc.WagonAt(2) is not { } w2 || rc.WagonAt(1) is not { } w1 || rc.WagonAt(3) is not { } w3) return;
        var k = Mathf.Abs(w2.Curvature);
        _bdMaxBend = Mathf.Max(_bdMaxBend, k);
        var seat = w2.SeatTransform(TrainRules.HeroPlace).Origin;
        var own = seat.DistanceTo(w2.BoarderSpotTransform().Origin);
        if (k < 0.01f) _bdOwnStraight = Mathf.Max(_bdOwnStraight, own);
        else if (k >= BdBendK) _bdOwnBend = Mathf.Max(_bdOwnBend, own);
        _bdOwnMin = Mathf.Min(_bdOwnMin, own);
        _bdToW1 = Mathf.Min(_bdToW1, seat.DistanceTo(w1.BoarderSpotTransform().Origin));
        _bdToW3 = Mathf.Min(_bdToW3, seat.DistanceTo(w3.BoarderSpotTransform().Origin));
        if (_bdH2 is { } h2 && IsInstanceValid(h2) && h2.Carrier == w2 && rc.CarOf(me) == 2)
        {
            var bodies = me.GlobalPosition.DistanceTo(h2.GlobalPosition);
            _bdBodyMin = Mathf.Min(_bdBodyMin, bodies);
            _bdBodyMax = Mathf.Max(_bdBodyMax, bodies);
        }
    }

    private void BoardersReach(RailController rc, RailEncounterController enc, PartyController party, Adventurer me, Action<string> Fail)
    {
        var t = _bdClock - _bdPhaseT;
        void Next(int phase) { _bdPhase = phase; _bdPhaseT = _bdClock; }
        if (rc.State.Mode == CartMode.Wrecked) { Fail("the train was wrecked"); return; }
        if (rc.State.Mode == CartMode.Held && _bdPhase < 9) { Fail($"the train reached stop {rc.StopIndex} at phase {_bdPhase}, before the script was done"); return; }
        var lead = BdMember(party, me);
        if (_bdPhase >= 2) BdSampleReach(rc, me);
        switch (_bdPhase)
        {
            case 0:
            {
                enc.DebugHoldAnswers = true;
                foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.DebugHoldFire = true;
                if (!rc.DebugSeat(me, 2) || rc.CarOf(me) != 2) { Fail("the lead could not be seated in W2"); return; }
                Next(1);
                break;
            }
            case 1:
            {
                if (t < 0.3f) break;
                _bdH1 = enc.DebugBoard(1, holdHp: true);
                _bdH2 = enc.DebugBoard(2, holdHp: true);
                _bdH3 = enc.DebugBoard(3, holdHp: true);
                if (_bdH1 == null || _bdH2 == null || _bdH3 == null) { Fail("a hound could not be put on W1, W2 or W3"); return; }
                _bdW2Hounds.Add(_bdH2);
                _bdHp1 = _bdH1.Hp;
                _bdHp3 = _bdH3.Hp;
                _bdBlows.Clear();
                var attacks = BdMeleeAttacks(party, me);
                foreach (var h in new[] { 1, 2, 3 }) foreach (var d in attacks) _bdBlows.Add(d); // each attack at each hound in turn: W1's, W2's, W3's
                GD.Print($"[boarders] reach: the lead ({me.State.Class.Name}) in {rc.CarOf(me) switch { var c => $"car {c}" }} seat {me.Seat}; hounds on W1, W2, W3 (held above 1 HP); its melee attacks [{string.Join(", ", attacks.Select(a => a.Name))}], {_bdBlows.Count} blows in all");
                if (attacks.Count == 0) { Fail($"the lead's class {me.State.Class.Name} has no melee attack"); return; }
                _bdBlow = 0;
                _bdBlowAt = -1f;
                Next(2);
                break;
            }
            case 2:
            {
                // a W2 hound knocked off by the lead's blows (its grip) makes way for a fresh one: the blows at W2 keep a target
                if (_bdH2 is not { } h2 || !IsInstanceValid(h2) || h2.Carrier != rc.WagonAt(2))
                {
                    _bdH2 = enc.DebugBoard(2, holdHp: true);
                    if (_bdH2 == null) { Fail("a fresh hound could not be put on W2"); return; }
                    _bdW2Hounds.Add(_bdH2);
                    GD.Print($"[boarders] reach: W2's hound was knocked off by its grip; a fresh one boards ({_bdW2Hounds.Count} so far)");
                }
                if (me.BladeSwinging || _bdClock - _bdBlowAt < 0.35f) break;
                if (_bdBlow >= _bdBlows.Count) { Next(3); break; }
                var per = _bdBlows.Count / 3;
                var target = (_bdBlow / per) switch { 0 => _bdH1!, 1 => _bdH2!, _ => _bdH3! };
                var d = _bdBlows[_bdBlow];
                BdLeadBlow(party, me, target, d);
                GD.Print($"[boarders] reach: blow {_bdBlow + 1}/{_bdBlows.Count}, {d.Name}, aimed at W{_bdBlow / per + 1}'s hound {target.GlobalPosition.DistanceTo(me.GlobalPosition):0.00} m away");
                _bdBlow++;
                _bdBlowAt = _bdClock;
                break;
            }
            case 3:
            {
                if (t < 0.8f || me.BladeSwinging) break;
                var w2Hits = _bdW2Hounds.Sum(h => BdHits(h, lead));
                int w1 = BdHits(_bdH1), w3 = BdHits(_bdH3);
                float hp1 = _bdH1!.Hp, hp3 = _bdH3!.Hp;
                int g1 = enc.GripOf(_bdH1), g3 = enc.GripOf(_bdH3);
                GD.Print($"[boarders] reach: the lead's {_bdBlows.Count} blows landed {w2Hits} time(s) on W2's hound(s) ({_bdW2Hounds.Count} boarded, knock-offs {enc.KnockOffs}); W1's hound hits {w1}, hp {hp1:0}/{_bdHp1:0}, grip {g1}; W3's hits {w3}, hp {hp3:0}/{_bdHp3:0}, grip {g3}");
                if (w2Hits == 0) { Fail("no blow of the lead's in W2 landed on W2's own hound"); return; }
                if (w1 != 0 || w3 != 0 || hp1 < _bdHp1 || hp3 < _bdHp3 || g1 != 0 || g3 != 0) { Fail("a blow from W2 landed on W1's or W3's hound"); return; }
                {
                    // a carried blow (fix round 1, review M6): a cleave off W2's hound, its reach widened past both neighbours' hounds, takes neither
                    var h2 = _bdH2!;
                    float d1 = h2.GlobalPosition.DistanceTo(_bdH1.GlobalPosition), d3 = h2.GlobalPosition.DistanceTo(_bdH3.GlobalPosition);
                    var radius = Mathf.Max(d1, d3) + 0.3f;
                    me.DebugCleave(h2, 10f, radius);
                    GD.Print($"[boarders] reach: a cleave off W2's hound out to {radius:0.00} m (W1's hound {d1:0.00} m from it, W3's {d3:0.00} m): W1's hits {BdHits(_bdH1)}, hp {_bdH1.Hp:0}/{_bdHp1:0}; W3's hits {BdHits(_bdH3)}, hp {_bdH3.Hp:0}/{_bdHp3:0}");
                    if (BdHits(_bdH1) != 0 || BdHits(_bdH3) != 0 || _bdH1.Hp < _bdHp1 || _bdH3.Hp < _bdHp3) { Fail("a cleave off W2's hound carried into W1's or W3's"); return; }
                    // and a chained one (fix round 2): both neighbours frozen and inside the chain's 4 m, a shatter chain off W2's hound takes neither
                    foreach (var n in new[] { _bdH1, _bdH3 }) n.ApplyStatus(new Combat.StatusApplication(Combat.StatusKind.Frost, 5f, 0));
                    var frozen = new[] { _bdH1, _bdH3 }.Count(n => n.Effects.Has(Combat.StatusKind.Frost) && n.GlobalPosition.DistanceTo(h2.GlobalPosition) <= Adventure.Fortune.FortuneRules.ShatterChainRadius);
                    me.DebugShatterChain(h2, 2);
                    GD.Print($"[boarders] reach: a shatter chain off W2's hound ({frozen} frozen neighbour(s) within {Adventure.Fortune.FortuneRules.ShatterChainRadius:0} m): W1's hits {BdHits(_bdH1)}, hp {_bdH1.Hp:0}/{_bdHp1:0}; W3's hits {BdHits(_bdH3)}, hp {_bdH3.Hp:0}/{_bdHp3:0}");
                    if (frozen != 2) { Fail($"the shatter chain's pin needs both neighbours frozen within reach ({frozen})"); return; }
                    if (BdHits(_bdH1) != 0 || BdHits(_bdH3) != 0 || _bdH1.Hp < _bdHp1 || _bdH3.Hp < _bdHp3) { Fail("a shatter chain off W2's hound carried into W1's or W3's"); return; }
                    foreach (var n in new[] { _bdH1, _bdH3 }) n.Effects.Remove(Combat.StatusKind.Frost);
                }
                _bdMate = party.Adventurers.Where(a => IsInstanceValid(a) && a != me && a.Control == ControlKind.Companion && a.Cart == rc.Cart && !a.Downed && BdBlade(a))
                    .OrderBy(a => a.GlobalPosition.DistanceTo(_bdH1.GlobalPosition)).FirstOrDefault();
                if (_bdMate == null) { Fail("no melee companion seated in the cart"); return; }
                _bdSwings = 0;
                _bdBlowAt = -1f;
                Next(4);
                break;
            }
            case 4:
            {
                // from the cart: a melee companion's blows at W1's hound never land
                var mate = _bdMate!;
                if (_bdClock - _bdBlowAt < 0.6f) break;
                if (_bdSwings >= 3) { Next(5); break; }
                AbilitiesRefill(mate.State);
                mate.State.ActiveAbilityIndex = 0;
                mate.DebugStrikeAt(_bdH1!);
                _bdSwings++;
                _bdBlowAt = _bdClock;
                GD.Print($"[boarders] reach: from the cart, {mate.State.Name} (car {rc.CarOf(mate)} seat {mate.Seat}) strikes at W1's hound {mate.GlobalPosition.DistanceTo(_bdH1!.GlobalPosition):0.00} m away (swing {_bdSwings})");
                break;
            }
            case 5:
            {
                if (t < 0.5f) break;
                var mate = _bdMate!;
                var hits = BdHits(_bdH1, BdMember(party, mate));
                GD.Print($"[boarders] reach: from the cart, {mate.State.Name}'s 3 strikes at W1's hound landed {hits}; its hp {_bdH1!.Hp:0}/{_bdHp1:0}, grip {enc.GripOf(_bdH1)}");
                if (hits != 0 || BdHits(_bdH1) != 0 || _bdH1.Hp < _bdHp1 || enc.GripOf(_bdH1) != 0) { Fail("a companion seated in the cart landed a blow on W1's hound"); return; }
                // the same blade seated in W3: W2's hound, nearer than its own reach, is still another car's (the own-car rule, not the distance, keeps it off)
                if (!rc.DebugSeat(mate, 3)) { Fail($"{mate.State.Name} could not be seated in W3"); return; }
                _bdSwings = 0;
                _bdBlowAt = -1f;
                _bdHp1 = _bdH2!.Hp; // W2's hound's HP now (its HP is held above 1)
                Next(7);
                break;
            }
            case 7:
            {
                var mate = _bdMate!;
                if (t < 0.3f || _bdClock - _bdBlowAt < 0.6f) break;
                if (_bdSwings >= 3) { Next(8); break; }
                AbilitiesRefill(mate.State);
                mate.State.ActiveAbilityIndex = 0;
                mate.DebugStrikeAt(_bdH2!);
                _bdSwings++;
                _bdBlowAt = _bdClock;
                GD.Print($"[boarders] reach: from W3, {mate.State.Name} (car {rc.CarOf(mate)} seat {mate.Seat}) strikes at W2's hound {mate.GlobalPosition.DistanceTo(_bdH2!.GlobalPosition):0.00} m away (WagonReach {BoarderRules.WagonReach:0.00}; swing {_bdSwings})");
                break;
            }
            case 8:
            {
                if (t < 0.5f) break;
                var mate = _bdMate!;
                var hits = BdHits(_bdH2, BdMember(party, mate));
                GD.Print($"[boarders] reach: from W3, {mate.State.Name}'s 3 strikes at W2's hound landed {hits}; its hp {_bdH2!.Hp:0}/{_bdHp1:0}, its own hound (W3's) hits {BdHits(_bdH3, BdMember(party, mate))}");
                if (hits != 0 || _bdH2.Hp < _bdHp1) { Fail("a companion seated in W3 landed a blow on W2's hound"); return; }
                Next(6);
                break;
            }
            case 6:
            {
                // the reach: a bend measured (ride on until one is), then the line and the constant's bounds
                if (_bdOwnBend < 0f || _bdOwnStraight < 0f) { if (t > 40f) Fail($"no {(_bdOwnBend < 0f ? "bend" : "straight")} sample of W2's own spot on ride 2 (the sharpest bend {_bdMaxBend:0.000}/m)"); break; }
                GD.Print($"[boarders] reach: W2 seat 0 to its own spot {_bdOwnStraight:0.00} m (straight), {_bdOwnBend:0.00} m (in a bend); to W1's spot {_bdToW1:0.00} m, to W3's {_bdToW3:0.00} m (the sharpest bend {_bdMaxBend:0.000}/m)");
                GD.Print($"[boarders] reach: the own spot read off W2's pose spans {_bdOwnMin:0.000}..{Mathf.Max(_bdOwnStraight, _bdOwnBend):0.000} m; the bodies sampled in the rendered frame (the lead to W2's hound) span {_bdBodyMin:0.000}..{_bdBodyMax:0.000} m");
                var need = Mathf.Max(_bdOwnStraight, _bdOwnBend) + 0.15f;
                GD.Print($"[boarders] reach: BoarderRules.WagonReach {BoarderRules.WagonReach:0.00} m: the own spot + 0.15 is {need:0.000} m, the car spacing {TrainRules.Spacing:0.00} m");
                if (BoarderRules.WagonReach < need - 1e-3f || BoarderRules.WagonReach >= TrainRules.Spacing) { Fail($"WagonReach {BoarderRules.WagonReach:0.00} is not in [{need:0.000}, {TrainRules.Spacing:0.00})"); return; }
                if (_bdBodyMax < 0f || _bdBodyMax > BoarderRules.WagonReach) { Fail($"the lead's body and W2's hound sampled {_bdBodyMax:0.000} m apart, past WagonReach {BoarderRules.WagonReach:0.00} (or never sampled)"); return; }
                foreach (var h in _bdW2Hounds.Append(_bdH1!).Append(_bdH3!)) if (IsInstanceValid(h) && h.IsAlive) enc.DebugKill(h);
                GD.Print($"[boarders] RESULT ok ({_bdVariant}): from W2 the lead's {_bdBlows.Count} blows landed only on W2's hound ({_bdW2Hounds.Sum(h => BdHits(h, lead))} hit(s)); W1's and W3's took none; a cart companion's strikes at W1's never landed; the own spot {Mathf.Max(_bdOwnStraight, _bdOwnBend):0.00} m (the bodies {_bdBodyMin:0.00}..{_bdBodyMax:0.00} m) within WagonReach {BoarderRules.WagonReach:0.00} m; a cleave off W2's hound took neither neighbour");
                _done = true;
                GetTree().Quit(0);
                break;
            }
        }
    }
}
