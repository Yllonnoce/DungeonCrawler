using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-boarders[=move|reach|knock|throw|lone|pause|groups] (plan 14, Tasks 3-7) ------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo; every enemy is culled as it appears but the hounds the flow puts on wagons
// (RailEncounterController.DebugBoard), and the flow keeps the party's HP up. The variants are plan 14's (Ruling 22): =move (Task 3), =reach
// and =knock (Task 4), =throw (the default), =lone and =pause (Task 5) are built, one partial file each (DebugTools.Boarders.{Variant}.cs);
// groups is a later task's and, like an unknown name, prints an ERROR and fails.
// =reach and =knock are restored at stop 1 with its cage opened by the test's hook (DebugSeatCrew: its crew sit in W1 at once); =throw and =lone
// at stop 1 with its crew seated in W3 (=throw) or W4 (=lone); =pause at stop 2 with stop 2's crew seated in W1. Each rides the next ride.
// Prints [boarders] lines and a RESULT.
public partial class DebugTools
{
    private bool _testBoarders;
    private string _bdVariant = "throw";
    private int _bdStep, _bdPhase;
    private float _bdClock, _bdPhaseT;
    private bool _bdListening, _bdLeadUnderBeam, _bdSecondPressed;
    private readonly List<string> _bdLog = new();
    private readonly List<string> _bdSaid = new();
    private readonly BeamHeads _bdHeads = new();
    private int _bdMovesBefore;
    private float _bdHopNearest;
    private Adventurer? _bdMate3, _bdMate2;
    // =reach and =knock (Task 4)
    private readonly Dictionary<Enemies.Enemy, List<(int By, Combat.AttackKind Kind, int Grip, float At)>> _bdHurt = new();
    private Enemies.Enemy? _bdH1, _bdH2, _bdH3;
    private readonly List<Enemies.Enemy> _bdW2Hounds = new();
    private readonly List<(int Index, bool Heavy, bool Perfect, string Name)> _bdBlows = new();
    private int _bdBlow, _bdKnockBase, _bdSwings;
    private float _bdBlowAt, _bdHp1, _bdHp3, _bdClearAt = -1f;
    private float _bdOwnStraight = -1f, _bdOwnBend = -1f, _bdToW1 = float.MaxValue, _bdToW3 = float.MaxValue, _bdMaxBend;
    private float _bdOwnMin = float.MaxValue, _bdBodyMin = float.MaxValue, _bdBodyMax = -1f;
    private Adventurer? _bdMate;
    private Enemies.Enemy? _bdHound;
    private bool _bdLeverChecked, _bdWasHopping;
    private readonly List<(float At, int From, int To)> _bdAnswerHops = new();
    private readonly List<Adventurer> _bdShootersDown = new(); // pass 3b: the seat answerers made to pump, in turn
    private float _bdHandoffAt = -1f;
    // =throw, =lone and =pause (Task 5)
    private readonly List<string> _bdThrowSaid = new(); // the log's throw lines and silent calls
    private readonly List<string> _bdThrowProps = new(); // PropEvts "wagon:{k} thrown:{id}"
    private readonly List<(string Id, string Name, int Wagon, ulong Frame, ulong HiddenFrame)> _bdThrownCrew = new();
    private ulong _bdLandFrame, _bdMarkFrame, _bdClimbFrame;
    private int _bdThrowsBase;
    private int _bdLogMark;
    private readonly Dictionary<int, string> _bdSeatOf = new(); // a wagon's crew by bench seat
    private readonly HashSet<string> _bdWalked = new(); // =throw: the crew seen on foot before the ride (they walked to their seats)
    private readonly List<(Enemies.Enemy Hound, ulong Frame)> _bdTelegraphs = new(); // the throw's glow told as a telegraph
    /// <summary>A wagon's track curvature (1/m) from which the reach sample counts as "in a bend" (a radius of 25 m or less); under 0.01 is the straight.</summary>
    private const float BdBendK = 0.04f;

    /// <summary>The variants plan 14 names for this flow (Ruling 22), and the ones built so far.</summary>
    private static readonly string[] BoardersVariants = { "move", "reach", "knock", "throw", "lone", "pause", "groups" };
    private static readonly string[] BoardersBuilt = { "move", "reach", "knock", "throw", "lone", "pause" };

    /// <summary>The stop a variant is restored at (it rides the ride after it), and the wagon its stop's crew are seated in (0: the first free seats).</summary>
    private static (int Stop, int CrewWagon) BdStart(string v) => v switch { "move" => (0, 0), "throw" => (1, 3), "lone" => (1, 4), "pause" => (2, 1), _ => (1, 0) };

    private void RunBoardersTest()
    {
        var v = _bdVariant;
        void Fail(string why) { GD.Print($"[boarders] RESULT FAIL ({v}): {why}"); if (Main.Instance?.Party is { } p) { p.AllowPossession = true; p.ForcedMoveInput = null; } PumperCap = null; _done = true; GetTree().Quit(1); }
        if (!BoardersBuilt.Contains(v))
        {
            GD.PrintErr($"[boarders] ERROR: --test-boarders={v} is {(BoardersVariants.Contains(v) ? "not built yet" : "not a variant")} (the variants: {string.Join(", ", BoardersVariants)}; built: {string.Join(", ", BoardersBuilt)})");
            Fail($"the variant {v} is {(BoardersVariants.Contains(v) ? "not built yet" : "unknown")}");
            return;
        }
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        var dt = (float)GetProcessDeltaTime();
        _bdClock += dt;
        var party = main.Party;
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc) { if (_bdClock > 3f) Fail("floor 12 has no line"); return; }
        BoardersListen();
        BdReadGrips(enc);
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive && !enc.IsDebugBoarder(e)) e.Kill(); // the flow owns the plating and the HP
        if (v != "move") foreach (var a in party.Adventurers) if (IsInstanceValid(a) && !a.Parked && !a.Downed) a.State.Hp = a.State.Stats[Characters.Stat.MaxHp]; // the hounds bite: nobody goes down
        enc.RunawayClock = 0f; // no traffic runaway on the party line
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (v == "throw" && _bdStep is >= 1 and <= 3 && ObjectiveController.Instance is CrewRescueController walkers) // who of stop 1's crew stood on the ground
            foreach (var c in walkers.Crew) if (IsInstanceValid(c) && c.Freed && !c.Dead && !c.Seated) _bdWalked.Add(c.Id);

        switch (_bdStep)
        {
            case 0 when _bdClock >= 1.5f:
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                var (startStop, crewWagon) = BdStart(v);
                rc.Restore(startStop);
                GD.Print($"[boarders] floor 12 ({v}): restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Number))}]; lethality {TierRules.Lethality}");
                if (rc.Train.Count != 4) { Fail($"the train has {rc.Train.Count} wagons, not 4"); return; }
                if (v == "throw")
                {
                    // fix round 1 (I1): stop 1's crew walk out of the opened cage to their seats in W3, as in play (they stand on the ground first)
                    if (ObjectiveController.Instance is not CrewRescueController walk) { Fail("no crew controller"); return; }
                    walk.DebugBoardWagon = 3;
                    if (!walk.DebugOpen(startStop)) { Fail($"stop {startStop}'s cage could not be opened"); return; }
                    _bdWalked.Clear();
                    GD.Print($"[boarders] stop {startStop}'s cage opened by the test's hook: its crew walk to W3");
                }
                else if (v != "move")
                {
                    // the stop's cage holds the cart while locked (CageHoldRules): opened by the test's hook, its crew seated at once (in W1, or the variant's wagon)
                    if (ObjectiveController.Instance is not CrewRescueController ctrl || ctrl.DebugSeatCrew(startStop, crewWagon) is not { Count: > 0 } crew) { Fail($"stop {startStop}'s crew could not be seated"); return; }
                    GD.Print($"[boarders] stop {startStop}'s cage opened by the test's hook: [{string.Join(", ", crew.Select(c => $"{c.Npc.NpcName} W{c.Wagon} seat {c.Seat}"))}]");
                    if (crewWagon > 0 && (crew.Count != 2 || crew.Any(c => c.Wagon != crewWagon))) { Fail($"stop {startStop}'s crew are not the two in W{crewWagon}"); return; }
                }
                {
                    // the server's reach for a use of a wagon (fix round 1, I3): measured to its tub, as a cart's; a point 4.1 m end-on from
                    // W1's middle, at the tub's height, is 3.0 m off its box (inside the 3.5 m reach), where its origin is 4.1 m off (outside)
                    var w1 = rc.WagonAt(1)!;
                    var endOn = w1.GlobalTransform * new Vector3(0f, 1f, 4.1f);
                    var tub = w1.TubDistance(endOn);
                    var origin = w1.GlobalPosition.DistanceTo(endOn);
                    GD.Print($"[boarders] reach: a point 4.1 m end-on from W1's middle reads {tub:0.000} m from its tub (origin {origin:0.000} m; reach {PropPromptRules.InteractReach} m)");
                    if (Mathf.Abs(tub - 3.0f) > 0.01f || tub > PropPromptRules.InteractReach || origin <= PropPromptRules.InteractReach) { Fail($"W1's tub reads {tub:0.000} m for a point 4.1 m end-on, not 3.0"); return; }
                }
                _bdStep++; _bdClock = 0f;
                break;
            case 1 when _bdClock >= 1f:
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _bdStep++; _bdClock = 0f;
                break;
            case 2:
            {
                var heroes = party.Active.Count();
                var seated = party.Active.Count(a => a.Cart == rc.Cart);
                if ((!me.Seated || seated < heroes) && _bdClock < 3f) break;
                if (me.Cart != rc.Cart || seated < 3) { Fail($"the lead seated in the cart {me.Cart == rc.Cart}, {seated} of {heroes} heroes in it (3 needed)"); return; }
                party.AllowPossession = false; // the mid-hop down keeps the lead the human
                GD.Print($"[boarders] boarded at stop {rc.StopIndex}: {seated} of {heroes} heroes in the cart");
                _bdStep++; _bdClock = 0f;
                break;
            }
            case 3:
            {
                var ride = BdStart(v).Stop + 1;
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != ride || rc.State.V < 1f || _bdClock < 1f) { if (_bdClock > 20f) Fail($"ride {ride} did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                GD.Print($"[boarders] rolling on ride {ride} at S={rc.State.S:0.0} (v {rc.State.V:0.00}); moves refused {rc.MovesRefused}");
                _bdStep++; _bdClock = 0f; _bdPhase = 0; _bdPhaseT = 0f;
                break;
            }
            case 4:
                if (v == "reach") BoardersReach(rc, enc, party, me, Fail);
                else if (v == "knock") BoardersKnock(rc, enc, party, me, Fail);
                else if (v == "throw") BoardersThrow(main, rc, enc, party, me, Fail);
                else if (v == "lone") BoardersLone(rc, enc, party, me, Fail);
                else if (v == "pause") BoardersPause(rc, enc, party, me, Fail);
                else BoardersMove(rc, enc, party, me, Fail);
                break;
        }
    }

    /// <summary>The landing and refusal lines, heard once per run.</summary>
    private void BoardersListen()
    {
        if (_bdListening) return;
        _bdListening = true;
        EventBus.Instance.EnemyHurt += (e, _, _, by) =>
        {
            if (_done || !_testBoarders || RailEncounterController.Instance is not { } enc || !enc.IsDebugBoarder(e)) return;
            if (!_bdHurt.TryGetValue(e, out var list)) _bdHurt[e] = list = new();
            list.Add((by, e.LastHitKind, BdGripUnread, _bdClock)); // the grip is read after the controller has counted the hit (BdReadGrips)
        };
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testBoarders) return;
            if (text.Contains(" is thrown from the wagon!", StringComparison.Ordinal) || text.StartsWith("It's at the prisoners", StringComparison.Ordinal)) _bdThrowSaid.Add(text);
            else if (!text.Contains(" climbs ", StringComparison.Ordinal)) return;
            else { _bdSaid.Add(text); _bdClimbFrame = Engine.GetPhysicsFrames(); } // a landing's line, logged in the rail controller's tick
            GD.Print($"[boarders] logged: \"{text}\"");
        };
        EventBus.Instance.EnemyTelegraph += (e, state) =>
        {
            if (_done || !_testBoarders || state != Enemies.TelegraphRules.BoarderThrow) return;
            _bdTelegraphs.Add((e, Engine.GetPhysicsFrames()));
        };
        EventBus.Instance.PropChanged += (r, what) =>
        {
            if (_done || !_testBoarders || !what.StartsWith("thrown:", StringComparison.Ordinal)) return;
            _bdThrowProps.Add($"{r} {what}");
            GD.Print($"[boarders] prop: {r} \"{what}\"");
        };
    }

    /// <summary>The seat yaw that looks back over the shoulder (120 degrees) and the one that looks forward.</summary>
    private static readonly float BdBack = Mathf.DegToRad(120f), BdForward = 0f;


    // ---- shared by the variants ---------------------------------------------------------------------------------------------

    private const int BdGripUnread = int.MinValue;

    /// <summary>Each hit heard since the last frame gets the grip its hound has after it (−1 once knocked off): the controller counts it in its own handler.</summary>
    private void BdReadGrips(RailEncounterController enc)
    {
        foreach (var (hound, list) in _bdHurt)
            for (var i = 0; i < list.Count; i++)
                if (list[i].Grip == BdGripUnread) list[i] = list[i] with { Grip = IsInstanceValid(hound) ? enc.GripOf(hound) : -1 };
    }

    /// <summary>Hurts a party member dealt <paramref name="hound"/> (by member index; all party hits when <paramref name="by"/> is null).</summary>
    private int BdHits(Enemies.Enemy? hound, int? by = null)
        => hound != null && _bdHurt.TryGetValue(hound, out var list) ? list.Count(h => h.By >= 0 && (by == null || h.By == by)) : 0;

    /// <summary>The party member index of <paramref name="a"/> (the striker index EnemyHurt reports).</summary>
    private static int BdMember(PartyController party, Adventurer a) => Array.FindIndex(party.Members, x => x == a.State);

    /// <summary>Every melee attack <paramref name="a"/>'s class has: the weapon's light, heavy and perfect swing and each melee strike ability.</summary>
    private static List<(int Index, bool Heavy, bool Perfect, string Name)> BdMeleeAttacks(PartyController party, Adventurer a)
    {
        var list = new List<(int, bool, bool, string)>();
        for (var i = 0; i < a.State.KnownAbilities.Count; i++)
        {
            var ab = party.GetAbilityFor(a.State, a.State.KnownAbilities[i]);
            if (ab.Kind == Combat.AbilityKind.WeaponAttack && !a.State.WeaponProfile.IsRanged)
            {
                list.Add((i, false, false, "a light swing"));
                list.Add((i, true, false, "a heavy swing"));
                list.Add((i, false, true, "a perfect swing"));
            }
            else if (ab.Kind == Combat.AbilityKind.MeleeStrike) list.Add((i, false, false, ab.Name));
        }
        return list;
    }

    /// <summary>A companion that fights with a blade (a warrior or a rogue).</summary>
    private static bool BdBlade(Adventurer a) => a.State.Class.Id is "class.warrior" or "class.rogue";

    /// <summary>The lead's blow <paramref name="d"/> aimed at <paramref name="at"/> (refilled first, as a fresh press each time).</summary>
    private static void BdLeadBlow(PartyController party, Adventurer me, Enemies.Enemy at, (int Index, bool Heavy, bool Perfect, string Name) d)
    {
        AbilitiesRefill(me.State);
        me.State.ActiveAbilityIndex = d.Index;
        BarricadeAim(me, at.GlobalPosition + Vector3.Up * 0.4f);
        party.Attack(Array.IndexOf(party.Adventurers, me), d.Heavy, d.Perfect);
    }

    /// <summary>The metres to the next stop ahead of the train (its S), or +inf past the last.</summary>
    private static float BdRideLeft(RailController rc)
    {
        foreach (var s in rc.StopS) if (s > rc.State.S + 0.5f) return s - rc.State.S;
        return float.PositiveInfinity;
    }

    /// <summary>The index of a spell <paramref name="a"/> knows that strikes one enemy (a projectile of the Spell kind), or −1.</summary>
    private static int BdSpell(PartyController party, Adventurer a)
    {
        for (var i = 0; i < a.State.KnownAbilities.Count; i++)
            if (party.GetAbilityFor(a.State, a.State.KnownAbilities[i]) is { Kind: Combat.AbilityKind.Projectile, Attack.Kind: Combat.AttackKind.Spell, ArrowCost: 0 }) return i;
        return -1;
    }

    /// <summary>The answerer's moves as they are taken (a hop's start), with the car it leaves and the car it lands in once known.</summary>
    private void BdTrackHops(RailController rc, Adventurer mate)
    {
        var hopping = rc.Hopping(mate);
        if (hopping && !_bdWasHopping) _bdAnswerHops.Add((_bdClock, rc.CarOf(mate), -1));
        if (!hopping && _bdWasHopping && _bdAnswerHops.Count > 0) _bdAnswerHops[^1] = (_bdAnswerHops[^1].At, _bdAnswerHops[^1].From, rc.CarOf(mate));
        _bdWasHopping = hopping;
    }
}
