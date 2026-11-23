using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-barricade ----------------------------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo, Grisel forgiven (her escort taken, so she takes the bench). Every enemy is
// culled as it appears: the flow owns the plating and the HP. Sight: from the seated lead's eye (the cart set 15 m, then 35 m, short of
// each barricade's face) a layer-1 ray to its lantern is clear at 15 m. A, a spell: at stop 0, on foot, r1_barricade takes one Spell hit
// and is cut; it is stood up again. B, ride 1's cut: the party boards and departs; the Barricade call comes with the front 30-35 m from
// the face; from 12 m out the human brakes to a crawl, creeps up with the brake feathered and stops short of the face; the seated lead
// swings its weapon from the front seat (the blade's own contact), and the barricade stands after hits 1 and 2 and is cut by hit 3; the
// brake is let go; the plating is unchanged across it and no smash is logged. C, ride 2's smash: at stop 1 a companion is set to 5% HP
// (held there until the smash) with Grisel on the bench; nobody brakes or hits; the cart smashes r2_barricade: the plating drops by 25,
// each other seated hero loses 15% of max HP, the 5% companion is up with 1 HP or more, Grisel's HP is unchanged, and V is 0.8 of what
// it was. D: arrived at stop 2, rc.Restore(1): r2_barricade and r2_slag_barricade stay down until ride 2 arms again, then stand. E, on
// ride 2 before C's smash (r2_slag1's two cinders are spared the cull until then, and the companion goes to 5% only after it): r2_slag1
// smashes r2_slag_barricade on the slag line, each cinder down 15% (+-1) of its max HP or dead, while the party's cart, on its own lane,
// passes it with no smash and the plating unchanged. Prints [barricade] lines and a RESULT.
public partial class DebugTools
{
    private bool _testBarricade;
    private int _brStep;
    private float _brClock;
    private bool _brListening;
    private Barricade? _brR1, _brR2, _brSlag; // r5_barricade is ride 5's parked train now (plan 15 Task 5)
    /// <summary>Step E: r2_slag1's riders and their HP the frame before the smash; the plating as ride 2 departed; E judged.</summary>
    private readonly Dictionary<Enemies.Enemy, (float Hp, float Max)> _brSlagRiders = new();
    private float _brPlatingRide2 = float.NaN;
    private bool _brEDone;
    private Adventurer? _brLow;
    private float _brLowMax;
    private Npc? _brGrisel;
    private float _brCallGap = float.NaN, _brPlatingAtDeparture;
    private int _brCalls, _brSwings, _brHits;
    private int _brLeftBefore;
    private bool _brSwingPending;
    /// <summary>The smash log: true once "The cart smashes through the barricade!" was logged; every before-value is read in that very call.</summary>
    private bool _brSmashLogged;
    private float _brPlatingBefore, _brVBefore, _brGriselBefore;
    private readonly Dictionary<Adventurer, (float Hp, float Max)> _brHpBefore = new();

    private void BarricadeListen(RailController rc)
    {
        if (_brListening) return;
        _brListening = true;
        Action<BarkTrigger, string?> called = (t, by) =>
        {
            if (_done || !_testBarricade || t != BarkTrigger.Barricade || RailController.Instance is not { } r || _brR1 == null || _brR2 == null) return;
            var face = r.StopIndex == 0 ? _brR1.FaceS : _brR2.FaceS;
            _brCalls++;
            _brCallGap = face - r.Party.FrontS;
            GD.Print($"[barricade] the Barricade call by {by ?? "none (logged)"} with the front {_brCallGap:0.0} m from the face (ride {RailEncounterController.Instance?.ArmedRide})");
        };
        RailCalls.Called += called;
        _staticUnhooks.Add(() => RailCalls.Called -= called);
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testBarricade || text != "The cart smashes through the barricade!" || RailController.Instance is not { } r) return;
            // logged before the dent, the slowing and the hits (SmashBarricade): every value here is the one the smash met
            _brSmashLogged = true;
            _brPlatingBefore = r.State.Plating;
            _brVBefore = r.State.V;
            _brHpBefore.Clear();
            foreach (var a in Main.Instance.Party.Active)
                if (a.Cart == r.Cart && !a.Downed) _brHpBefore[a] = (a.State.Hp, a.State.Stats[Characters.Stat.MaxHp]);
            _brGriselBefore = _brGrisel?.Hp ?? float.NaN;
        };
    }

    private void RunBarricadeTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _brClock += (float)GetProcessDeltaTime();
        HoldTheRunaway("barricade"); // Ruling 22: not this flow's business
        var party = main.Party;
        void Fail(string why) { GD.Print($"[barricade] RESULT FAIL: {why}"); party.ForcedMoveInput = null; _done = true; GetTree().Quit(1); }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_brClock > 3f) Fail("floor 12 has no line"); return; }
        BarricadeListen(rc);
        var spared = _brEDone ? null : enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r2_slag1")?.Aboard; // E: its cinders ride on to the slag-line barricade
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive && (spared == null || !spared.Contains(e))) e.Kill(); // the flow owns the plating and the HP
        RideWaiveCages(main, rc, "barricade");
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (_brLow != null && _brEDone && !_brSmashLogged && _brStep >= BrLowFrom && IsInstanceValid(_brLow) && !_brLow.Downed) _brLow.State.Hp = _brLowMax * 0.05f; // held at 5% from E to the smash (a potion would lift it)
        if (_brStep >= BrLowFrom && _brStep <= BrLowFrom + 1 && !_brEDone && BarricadeStepE(rc, enc, party, Fail)) return;

        switch (_brStep)
        {
            case 0 when _brClock >= 1.5f:
            {
                _brR1 = enc.SetPieceNode("r1_barricade") as Barricade;
                _brR2 = enc.SetPieceNode("r2_barricade") as Barricade;
                _brSlag = enc.SetPieceNode("r2_slag_barricade") as Barricade;
                if (_brR1 == null || _brR2 == null || _brSlag == null) { Fail($"floor 12 lacks a barricade (r1 {_brR1 != null}, r2 {_brR2 != null}, r2 slag {_brSlag != null})"); return; }
                if (_brR1.SlagLine || _brR2.SlagLine) { Fail("a party-line barricade is on the slag line"); return; }
                if (!_brSlag.SlagLine) { Fail("r2_slag_barricade is not on the slag line"); return; }
                GD.Print($"[barricade] floor 12: r1 S={_brR1.S:0.0}, r2 S={_brR2.S:0.0}, r2 slag S={_brSlag.S:0.0} on the enemy line (party S={enc.Lanes.PartyS(_brSlag.S):0.0}); stops [{string.Join(",", rc.StopS.Select(s => s.ToString("0.0")))}]; lethality {TierRules.Lethality}, party damage scale {TierRules.PartyDamageScale:0.00}: a smash takes {BarricadeRules.SmashFraction(TierRules.PartyDamageScale):P1} of max HP");
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                _brGrisel = GriselNpc(root);
                if (_brGrisel is not { Dead: false }) { Fail("no living Grisel on floor 12 (forgiven)"); return; }
                if (!_brGrisel.Escorting && AdventureDirector.Instance?.QuestGivenBy(CrewRescueController.GriselId) is { } q) AdventureDirector.Instance.Apply(new QuestAccepted(q.Id)); // her escort, as her dialogue's accept does
                if (!_brGrisel.Escorting) { Fail("Grisel's escort did not start"); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case 1:
            {
                // A: a spell, on foot at stop 0
                var bar = _brR1!;
                var r = bar.ReceiveAttack(me.State.Stats, new AttackProfile { MinDamage = 1, MaxDamage = 1, Kind = AttackKind.Spell, Type = DamageType.Fire }, new HitContext(), party.Rng, me.State.Name);
                GD.Print($"[barricade] A {me.State.Name} hits r1_barricade with a spell at stop {rc.StopIndex} (seated={me.Seated}): hit={r.Hit}, left {bar.Left}");
                _brStep++; _brClock = 0f;
                break;
            }
            case 2:
            {
                var bar = _brR1!;
                if (bar.IsStanding || enc.BarricadesCut < 1) { if (_brClock > 1f) Fail($"A: one spell did not cut r1_barricade (standing={bar.IsStanding}, left {bar.Left}, cut {enc.BarricadesCut})"); break; }
                if (bar.CutBy != me.State.Name) { Fail($"A: r1_barricade was cut by \"{bar.CutBy}\", not {me.State.Name}"); return; }
                var by = bar.CutBy;
                enc.DebugRestandBarricades(1);
                GD.Print($"[barricade] A ok: one spell cut r1_barricade (by {by}); DebugRestandBarricades(1): standing={bar.IsStanding}, left {bar.Left}, solid layer {bar.CollisionLayer}");
                if (!bar.IsStanding || bar.Left != BarricadeRules.Toughness || bar.CollisionLayer != (1u | 4u)) { Fail("A: r1_barricade did not stand again whole"); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case 3 when _brClock >= 0.5f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case 4:
            {
                if ((!me.Seated || _brGrisel!.Cart != rc.Cart) && _brClock < 1.5f) break; // she takes the bench within a hop-in tick
                if (!me.Seated || me.Cart != rc.Cart) { Fail("the lead did not take a seat"); return; }
                if (_brGrisel!.Cart != rc.Cart) { Fail("Grisel did not take the bench at the landing"); return; }
                GD.Print($"[barricade] B boarded at stop {rc.StopIndex}: {party.Active.Count(a => a.Cart == rc.Cart)} heroes seated (the lead in seat {rc.Cart.Seats.SeatOf(me.Index)}), Grisel on the bench");
                if (BarricadeSight(rc, enc, me) is { } blind) { Fail(blind); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case 5:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 1) { if (_brClock > 10f) Fail($"ride 1 did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                _brPlatingAtDeparture = rc.State.Plating;
                GD.Print($"[barricade] B departed stop 0: plating {rc.State.Plating:0}, r1_barricade's face at S={_brR1!.FaceS:0.0}");
                _brStep++; _brClock = 0f;
                break;
            }
            case 6:
            {
                var gap = _brR1!.FaceS - rc.Party.FrontS;
                if (!_brR1.IsStanding || _brSmashLogged) { Fail($"B: r1_barricade went down before the brake (standing={_brR1.IsStanding}, smashed={_brSmashLogged})"); return; }
                if (gap > 12f) { if (_brClock > 60f) Fail($"B: the cart did not come within 12 m of r1_barricade (front {rc.Party.FrontS:0.0}, mode {rc.State.Mode})"); break; }
                if (party.ForcedMoveInput == null) GD.Print($"[barricade] B the human brakes {gap:0.0} m from the face at v {rc.State.V:0.00}; lantern blinking={_brR1.Alarm}");
                party.ForcedMoveInput = new Vector2(0, 1); // the stick back: the brake
                if (rc.State.V >= 0.3f) break;
                GD.Print($"[barricade] B braked to v {rc.State.V:0.00} {gap:0.0} m from the face; creeping up");
                _brStep++; _brClock = 0f;
                break;
            }
            case 7:
            {
                // creep up with the brake feathered, then stop short of the face: the front seat within a blade's reach
                var bar = _brR1!;
                var gap = bar.FaceS - rc.Party.FrontS;
                if (!bar.IsStanding || _brSmashLogged) { Fail($"B: the creep met the barricade (gap {gap:0.00}, smashed={_brSmashLogged})"); return; }
                var cap = gap > 3f ? 2f : 0.8f;
                if (gap <= 0.6f) party.ForcedMoveInput = new Vector2(0, 1);
                else if (rc.State.V > cap) party.ForcedMoveInput = new Vector2(0, 1);
                else if (rc.State.V < cap * 0.5f) party.ForcedMoveInput = null;
                if (gap <= 0.6f && rc.State.V < 0.05f)
                {
                    GD.Print($"[barricade] B stopped {gap:0.00} m short of the face (v {rc.State.V:0.00}); lantern blinking={bar.Alarm}");
                    _brLeftBefore = bar.Left;
                    _brStep++; _brClock = 0f;
                    break;
                }
                if (_brClock > 40f) Fail($"B: the creep did not stop short of the face in 40 s (gap {gap:0.00}, v {rc.State.V:0.00})");
                break;
            }
            case 8:
            {
                party.ForcedMoveInput = new Vector2(0, 1); // held on the brake while the lead hacks
                var bar = _brR1!;
                if (_brSwingPending)
                {
                    if (me.BladeSwinging || _brClock < 0.1f) break;
                    _brSwingPending = false;
                    var left = bar.IsStanding ? bar.Left : 0;
                    if (left < _brLeftBefore) _brHits += _brLeftBefore - left;
                    GD.Print($"[barricade] B swing {_brSwings}: hits {_brHits}, standing={bar.IsStanding}, left {bar.Left}");
                    if (left < _brLeftBefore && _brHits < BarricadeRules.Toughness && !bar.IsStanding) { Fail($"B: r1_barricade fell after hit {_brHits}"); return; }
                    _brLeftBefore = left;
                    if (_brHits >= BarricadeRules.Toughness)
                    {
                        if (bar.IsStanding) { Fail("B: r1_barricade stands after hit 3"); return; }
                        _brClock = 0f;
                        _brStep++;
                        break;
                    }
                }
                if (_brSwings >= 8) { Fail($"B: {_brSwings} swings from the front seat landed {_brHits} hit(s): the blade does not reach r1_barricade (left {bar.Left})"); return; }
                if (me.State.CooldownRemaining > 0f || me.BladeSwinging) break;
                BarricadeAim(me, bar.GlobalPosition + Vector3.Up * 0.75f);
                party.Attack(Array.IndexOf(party.Adventurers, me));
                _brSwings++;
                _brSwingPending = true;
                _brClock = 0f;
                break;
            }
            case 9:
            {
                var bar = _brR1!;
                if (bar.IsStanding || enc.BarricadesCut < 2) { if (_brClock > 1f) Fail($"B: hit 3 did not cut r1_barricade (standing={bar.IsStanding}, cut {enc.BarricadesCut})"); break; }
                GD.Print($"[barricade] B ok so far: r1_barricade cut by {bar.CutBy} after {_brSwings} swing(s) from seat {rc.Cart.Seats.SeatOf(me.Index)}; the brake is let go");
                party.ForcedMoveInput = null;
                _brStep++; _brClock = 0f;
                break;
            }
            case 10:
            {
                if (rc.Party.FrontS < _brR1!.FaceS + 5f) { if (_brClock > 20f) Fail($"B: the cart did not roll past the cut barricade (front {rc.Party.FrontS:0.0}, mode {rc.State.Mode})"); break; }
                GD.Print($"[barricade] B the front is past r1_barricade's face: plating {_brPlatingAtDeparture:0} -> {rc.State.Plating:0}, smash logged={_brSmashLogged}, calls {_brCalls} (the front {_brCallGap:0.0} m out)");
                if (_brCalls != 1 || float.IsNaN(_brCallGap) || _brCallGap < 30f || _brCallGap > 35f) { Fail($"B: the Barricade call came {_brCalls} time(s), the front {_brCallGap:0.0} m from the face (want once, 30-35 m)"); return; }
                if (_brSmashLogged || enc.BarricadesSmashed > 0) { Fail("B: a smash was logged at a barricade that was cut"); return; }
                if (!Mathf.IsEqualApprox(rc.State.Plating, _brPlatingAtDeparture)) { Fail($"B: the plating changed across the cut barricade ({_brPlatingAtDeparture:0.0} -> {rc.State.Plating:0.0})"); return; }
                GD.Print("[barricade] B ok: called once 30-35 m out, braked, cut by the lead's blade from the seat in three hits, no smash, the plating unchanged");
                _brCalls = 0; _brCallGap = float.NaN;
                _brStep++; _brClock = 0f;
                break;
            }
            case 11:
            {
                if (rc.State.Mode != CartMode.Held || rc.Halted || rc.StopIndex != 1) { if (_brClock > 90f) Fail($"ride 1 did not reach stop 1 (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})"); break; }
                _brLow = party.Active.FirstOrDefault(a => a.Control == ControlKind.Companion && a.Cart == rc.Cart && !a.Downed);
                if (_brLow == null) { Fail("C: no seated companion to bring to 5%"); return; }
                _brLowMax = _brLow.State.Stats[Characters.Stat.MaxHp];
                GD.Print($"[barricade] C at stop 1: {_brLow.State.Name} goes to 5% once E is judged ({_brLow.State.Hp:0.0}/{_brLowMax:0} now); Grisel seated={_brGrisel!.Cart == rc.Cart}, hp {_brGrisel.Hp:0.0}");
                _brStep = BrLowFrom; _brClock = 0f;
                break;
            }
            case BrLowFrom:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != 2) { if (_brClock > 20f) Fail($"ride 2 did not depart stop 1 (mode {rc.State.Mode}, armed {enc.ArmedRide}, gate {rc.GateUp(1)}, cage held {rc.CageHeld})"); break; }
                if (_brGrisel!.Cart != rc.Cart) { Fail("C: Grisel is not on the bench for ride 2"); return; }
                _brPlatingRide2 = rc.State.Plating;
                GD.Print($"[barricade] C departed stop 1 with Grisel on the bench; nobody brakes or hits; r2_barricade's face at S={_brR2!.FaceS:0.0}");
                _brStep++; _brClock = 0f;
                break;
            }
            case BrLowFrom + 1:
            {
                party.ForcedMoveInput = null;
                if (!_brSmashLogged) { if (_brClock > 90f || rc.StopIndex >= 2) Fail($"C: the cart did not smash r2_barricade (front {rc.Party.FrontS:0.0}, face {_brR2!.FaceS:0.0}, standing={_brR2.IsStanding}, stop {rc.StopIndex})"); break; }
                if (!_brEDone) { Fail("E: r2_barricade was smashed before E was judged (the slag-line barricade)"); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case BrLowFrom + 2:
            {
                // the frame after the smash: the dent, the slowing and the hits are in
                var bar = _brR2!;
                var drop = _brPlatingBefore - rc.State.Plating;
                var kept = _brVBefore > 0f ? rc.State.V / _brVBefore : float.NaN;
                var fraction = BarricadeRules.SmashFraction(TierRules.PartyDamageScale);
                var lines = new List<string>();
                var problems = new List<string>();
                foreach (var (hero, (hp, max)) in _brHpBefore)
                {
                    var lost = (hp - hero.State.Hp) / max;
                    lines.Add($"{hero.State.Name} {hp:0.0}->{hero.State.Hp:0.0}/{max:0} ({lost:P1})");
                    if (hero == _brLow)
                    {
                        if (hero.Downed || hero.State.Hp < 0.999f) problems.Add($"{hero.State.Name} at 5% went down or below 1 HP ({hero.State.Hp:0.00})");
                    }
                    else if (Mathf.Abs(lost - fraction) > 0.005f) problems.Add($"{hero.State.Name} lost {lost:P1}, not {fraction:P0}");
                }
                if (_brLow == null || !_brHpBefore.ContainsKey(_brLow)) problems.Add("the 5% companion was not seated at the smash");
                var griselNow = _brGrisel?.Hp ?? float.NaN;
                GD.Print($"[barricade] C smashed r2_barricade (standing={bar.IsStanding}): plating {_brPlatingBefore:0.0} -> {rc.State.Plating:0.0} (-{drop:0.0}); v {_brVBefore:0.00} -> {rc.State.V:0.00} (x{kept:0.00}); heroes [{string.Join(", ", lines)}]; Grisel {_brGriselBefore:0.0} -> {griselNow:0.0}");
                if (bar.IsStanding || enc.BarricadesSmashed != 1) problems.Add($"r2_barricade standing={bar.IsStanding}, smashes {enc.BarricadesSmashed}");
                if (Mathf.Abs(drop - BarricadeRules.SmashDent) > 0.01f) problems.Add($"the plating dropped by {drop:0.00}, not {BarricadeRules.SmashDent:0}");
                if (float.IsNaN(kept) || Mathf.Abs(rc.State.V - BarricadeRules.SpeedKept * _brVBefore) > 0.2f) problems.Add($"v went {_brVBefore:0.00} -> {rc.State.V:0.00}, not {BarricadeRules.SpeedKept:0.0} of it");
                if (float.IsNaN(griselNow) || !Mathf.IsEqualApprox(griselNow, _brGriselBefore)) problems.Add($"Grisel's HP went {_brGriselBefore:0.0} -> {griselNow:0.0}");
                if (problems.Count > 0) { Fail($"C: {string.Join("; ", problems)}"); return; }
                GD.Print($"[barricade] C ok: the plating -25, each other seated hero -{fraction:P0} of max HP, {_brLow!.State.Name} up at {_brLow.State.Hp:0.0} HP, Grisel unhurt, v x{kept:0.00}");
                _brStep++; _brClock = 0f;
                break;
            }
            case BrLowFrom + 3:
            {
                if (rc.State.Mode != CartMode.Held || rc.Halted || rc.StopIndex != 2) { if (_brClock > 90f) Fail($"ride 2 did not reach stop 2 (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})"); break; }
                rc.Restore(1);
                GD.Print($"[barricade] D arrived at stop 2; rc.Restore(1): the cart at stop {rc.StopIndex}; r2_barricade standing={_brR2!.IsStanding}, r2_slag_barricade standing={_brSlag!.IsStanding} (they stand at ride 2's next arming)");
                if (_brR2.IsStanding || _brSlag.IsStanding) { Fail("D: a ride-2 barricade stood at the restore, before its ride armed"); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case BrLowFrom + 4 when _brClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _brStep++; _brClock = 0f;
                break;
            }
            case BrLowFrom + 5:
            {
                if (enc.ArmedRide != 2) { if (_brClock > 15f) Fail($"D: ride 2 did not arm again from stop 1 (mode {rc.State.Mode}, stop {rc.StopIndex}, armed {enc.ArmedRide})"); break; }
                var bar = _brR2!;
                GD.Print($"[barricade] D ride 2 armed again: r2_barricade standing={bar.IsStanding}, left {bar.Left}, solid layer {bar.CollisionLayer}; r2_slag_barricade standing={_brSlag!.IsStanding}, left {_brSlag.Left}, solid layer {_brSlag.CollisionLayer}");
                if (!bar.IsStanding || bar.Left != BarricadeRules.Toughness || bar.CollisionLayer != (1u | 4u)) { Fail("D: r2_barricade did not stand again at ride 2's arming"); return; }
                if (!_brSlag.IsStanding || _brSlag.Left != BarricadeRules.Toughness || _brSlag.CollisionLayer != (1u | 4u)) { Fail("D: r2_slag_barricade did not stand again at ride 2's arming"); return; }
                GD.Print($"[barricade] D ok: both of ride 2's barricades down from the restore until ride 2 armed, then standing whole");
                GD.Print("[barricade] RESULT ok");
                party.ForcedMoveInput = null;
                _done = true;
                GetTree().Quit(0);
                return;
            }
        }
    }

    /// <summary>The step from which the 5% companion is held at 5% (from E's judging until the smash).</summary>
    private const int BrLowFrom = 20;

    /// <summary>
    /// Step E, each frame of ride 2 until judged: r2_slag1's riders' HP is read every frame while r2_slag_barricade stands; once it is
    /// down, each must be down 15% (+-1) of its max HP or dead, the party's cart must not have smashed it, and the plating must be as it
    /// was at ride 2's departure with the front past it. Then the companion goes to 5% for C. True when the flow failed.
    /// </summary>
    private bool BarricadeStepE(RailController rc, RailEncounterController enc, PartyController party, Action<string> fail)
    {
        if (enc.ArmedRide != 2 || _brSlag == null) return false;
        var bar = _brSlag;
        var partyFace = enc.Lanes.PartyS(bar.FaceS);
        if (bar.IsStanding)
        {
            if (enc.SlagCarts.FirstOrDefault(c => c.EncounterId == "r2_slag1") is { } cart)
            {
                _brSlagRiders.Clear();
                foreach (var r in cart.Aboard) if (IsInstanceValid(r) && r.IsAlive) _brSlagRiders[r] = (r.Hp, r.MaxHp);
            }
            if (rc.Party.FrontS > partyFace + 40f) { fail($"E: the party's front is 40 m past r2_slag_barricade (party S {partyFace:0.0}) and it still stands (r2_slag1 on the line: {enc.SlagCarts.Any(c => c.EncounterId == "r2_slag1")})"); return true; }
            return false;
        }
        // down: r2_slag1 smashed it this frame or the last (the riders' HP above is the frame before)
        var lines = new List<string>();
        var problems = new List<string>();
        foreach (var (rider, (hp, max)) in _brSlagRiders)
        {
            var dead = !IsInstanceValid(rider) || !rider.IsAlive;
            var now = dead ? 0f : rider.Hp;
            var lost = hp - now;
            lines.Add($"{rider.Name} {hp:0.0}->{(dead ? "dead" : now.ToString("0.0"))}/{max:0} (-{lost:0.0}, want -{max * BarricadeRules.SmashHpFraction:0.0})");
            if (!dead && Mathf.Abs(lost - max * BarricadeRules.SmashHpFraction) > 1f) problems.Add($"{rider.Name} lost {lost:0.0} of {max:0}, not 15%");
        }
        if (_brSlagRiders.Count != 2) problems.Add($"r2_slag1 carried {_brSlagRiders.Count} rider(s) to the barricade, not its two cinders");
        if (_brSmashLogged || enc.BarricadesSmashed > 0) problems.Add("the party's cart logged a smash");
        if (rc.FrontOnEnemyLane) problems.Add("the party's cart is across on the slag line");
        GD.Print($"[barricade] E r2_slag_barricade down (cut by \"{bar.CutBy}\") with the party's front at S={rc.Party.FrontS:0.0} (the barricade beside party S={partyFace:0.0}); riders [{string.Join(", ", lines)}]");
        if (problems.Count > 0) { fail($"E: {string.Join("; ", problems)}"); return true; }
        if (rc.Party.FrontS < partyFace + 1f) return false; // the party's front past its place before the plating is read
        if (float.IsNaN(_brPlatingRide2) || !Mathf.IsEqualApprox(rc.State.Plating, _brPlatingRide2)) { fail($"E: the plating went {_brPlatingRide2:0.0} -> {rc.State.Plating:0.0} past r2_slag_barricade"); return true; }
        GD.Print($"[barricade] E ok: r2_slag1 smashed r2_slag_barricade on the slag line, its {_brSlagRiders.Count} cinders each down 15% of max HP or dead; the party's cart passed it on its own lane, no smash, plating {rc.State.Plating:0} unchanged");
        _brEDone = true;
        if (_brLow != null && IsInstanceValid(_brLow) && !_brLow.Downed)
        {
            _brLow.State.Hp = _brLowMax * 0.05f;
            GD.Print($"[barricade] C {_brLow.State.Name} set to 5% ({_brLow.State.Hp:0.0}/{_brLowMax:0}) for r2_barricade");
        }
        return false;
    }

    /// <summary>
    /// Each barricade seen from the seated lead's eye with the cart's front 15 m and 35 m short of its face (a slag-line one's face read
    /// across on the party's lane): a layer-1 ray to its lantern.
    /// The eye keeps its place in the cart's frame as it sits now. Null when every one is clear at 15 m, else why not.
    /// </summary>
    private string? BarricadeSight(RailController rc, RailEncounterController enc, Adventurer lead)
    {
        var track = rc.Party.Track;
        var here = track.Frame(rc.State.S);
        var eyeLocal = here.AffineInverse() * lead.Camera.GlobalPosition;
        var space = lead.GetWorld3D().DirectSpaceState;
        foreach (var bar in new[] { _brR1!, _brR2!, _brSlag! })
        {
            var results = new List<string>();
            string? blocked15 = null;
            foreach (var d in new[] { 15f, 35f })
            {
                var cartS = (bar.SlagLine ? enc.Lanes.PartyS(bar.FaceS - d) : bar.FaceS - d) - CartRules.HalfLength; // a slag-line one from the party's own lane beside it
                var eye = track.Frame(cartS) * eyeLocal;
                var ray = PhysicsRayQueryParameters3D.Create(eye, bar.LanternPoint, 1, new Godot.Collections.Array<Rid> { bar.GetRid() });
                var hit = space.IntersectRay(ray);
                var what = hit.Count == 0 ? "clear" : $"blocked by {(hit["collider"].AsGodotObject() as Node)?.Name ?? "something"} at {((Vector3)hit["position"]).DistanceTo(eye):0.0} m";
                results.Add($"at {d:0} m {what}");
                if (d == 15f && hit.Count > 0) blocked15 = what;
            }
            GD.Print($"[barricade] sight {bar.Name}: {string.Join(", ", results)}");
            if (blocked15 != null) return $"the lantern of {bar.Name} is not seen from the seat at 15 m ({blocked15})";
        }
        return null;
    }

    /// <summary>Turn the seated lead's head (the mouse-look path) to look at <paramref name="aim"/>.</summary>
    private static void BarricadeAim(Adventurer lead, Vector3 aim)
    {
        var sens = 0.0022f * Settings.MouseSensitivity;
        for (var k = 0; k < 2; k++)
        {
            var cam = lead.Camera.GlobalTransform;
            var to = aim - cam.Origin;
            var forward = -cam.Basis.Z;
            var flatForward = new Vector3(forward.X, 0f, forward.Z).Normalized();
            var flatTo = new Vector3(to.X, 0f, to.Z).Normalized();
            var yaw = flatForward.SignedAngleTo(flatTo, Vector3.Up);
            var pitch = Mathf.Atan2(to.Y, Mathf.Max(0.01f, new Vector2(to.X, to.Z).Length()));
            lead.Look(new Vector2(-yaw / sens, (lead.Camera.Rotation.X - pitch) / sens * (Settings.InvertY ? -1f : 1f)));
        }
    }
}
