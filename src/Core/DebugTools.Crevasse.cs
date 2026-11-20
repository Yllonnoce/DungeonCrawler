using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-crevasse[=trestle|survivor] --------------------------------------------------------------------------
// The built-in campaign's floor 12 (Crevasses on) at lethality 3, solo, Grisel forgiven. A0: the human walks the cavern deck
// from end to end and then a plank there and back at walking speed, the companions following, and nobody falls (R7: a steered
// companion never walks itself into the drop). A1: a companion put over a chasm falls in: fallen, down, hidden, and no
// auto-revive 8 s on. A2: an enemy put over a chasm dies to it ("The crevasse swallows"). A3: a crew member out of their wagon
// and Grisel (escorting), put over a chasm: the crew member is lost, Grisel caught at the lip (down, not dead, escorting, on a
// cell by the drop) and revivable. After every step to A4 nobody but the test's own victim is fallen. A4: the party boards at the
// landing with A1's companion still fallen; on ride 1's arrival at stop 1 that companion is back within 1 s (up, a quarter of their
// HP, beside the cart). B, the fall: stop 1's cage opened and its wagon coupled, the cart restored at the kiln's stop, everyone
// boarded with Grisel on the bench, no pumping: the train misses the jump over the gorge and falls; every rider fallen, the wagon
// lost, its crew dead, Grisel down at the lip, no spare spent (the wipe that follows is Task 8's). =trestle: as B, but the human
// brakes on the trestle and it falls with the train on it. =survivor: as B, but the human gets out at the stop as the cart departs;
// after the fall nobody wipes, a spare waits, and the human stepping onto that stop's platform brings the fallen back beside them;
// a fresh capture names the lost wagon and its crew. Prints [crevasse] lines and a RESULT.
public partial class DebugTools
{
    private bool _testCrevasse;
    private string _crvVariant = "fall";
    private int _crvStep;
    private float _crvClock;
    private bool _crvListening;
    /// <summary>Every log line telling of a fall (into a crevasse, or a crack in the dark) since the test began.</summary>
    private readonly List<string> _crvFallLog = new();
    private bool _crvSwallowed;
    /// <summary>A2's enemy, spawned over the chasm; every other is culled as it appears.</summary>
    private Enemies.Enemy? _crvEnemy;
    /// <summary>The test's own victim in A1 (null before it).</summary>
    private Adventurer? _crvVictim;
    private Npc? _crvCrew, _crvGrisel;
    private readonly List<Vector3> _crvWay = new();
    private int _crvWayAt;
    private float _crvWalked;
    private Vector3 _crvLast;
    // ---- A4, B and the variants (Task 7)
    private CageWagon? _crvWagon;
    private readonly List<Npc> _crvWagonCrew = new();
    private readonly List<Adventurer> _crvRiders = new();
    private int _crvSparesBefore;
    private RailController? _crvHooked;
    private bool _crvWiped;
    /// <summary>The fall as the train's Wrecked event found it: null before it; "" when every check held, else what failed.</summary>
    private string? _crvFallVerdict;
    private string _crvFallSummary = "";
    private string? _crvFallWhy;
    /// <summary>The stop a spare waits at after the survivor's fall (its log line), or −1.</summary>
    private int _crvSpareStop = -1;
    /// <summary>The heroes (by name: a load makes new bodies) the flow lets fall now; any other fallen hero fails it.</summary>
    private readonly HashSet<string> _crvMayFall = new();
    // ---- the survivor's load and floor change (fix round 1)
    private const string CrvSlot = "crevassetest";
    private string _crvA4Name = "";
    private int _crvLostStop = -1;
    private readonly List<string> _crvLostIds = new();
    private string _crvFloorVictim = "";

    private void CrevasseListen()
    {
        if (_crvListening) return;
        _crvListening = true;
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testCrevasse) return;
            if (text.Contains("falls into the crevasse") || text.Contains("slips into a crack")) _crvFallLog.Add(text);
            if (text.StartsWith("The crevasse swallows")) _crvSwallowed = true;
            if (text is "The cart drops into the crevasse!" or "The trestle gives way into the crevasse!") _crvFallWhy ??= text;
            if (text.StartsWith("A spare cart waits at stop ") && int.TryParse(text["A spare cart waits at stop ".Length..].TrimEnd('.'), out var n)) _crvSpareStop = n;
        };
        EventBus.Instance.PartyWiped += () => { if (!_done && _testCrevasse) _crvWiped = true; };
    }

    private void RunCrevasseTest()
    {
        var main = Main.Instance;
        if (_crvStep == CrvRide + 6) { RunCrevasseFloorChange(main); return; } // on floor 13
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _crvClock += (float)GetProcessDeltaTime();
        var party = main.Party;
        void Fail(string why) { GD.Print($"[crevasse] RESULT FAIL ({_crvVariant}): {why}"); party.ForcedMoveInput = null; _done = true; GetTree().Quit(1); }
        CrevasseListen();
        if (layout.Rail is not { } rail || RailController.Instance is not { } rc) { if (_crvClock > 3f) Fail("floor 12 has no line"); return; }

        // the test owns the fights: every enemy is culled as it appears but A2's own, spawned over the chasm
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
            if (e.IsAlive && e != _crvEnemy) e.Kill();
        RideWaiveCages(main, rc, "crevasse");
        if (party.Local is not { } me) { Fail("no human"); return; }
        var companions = party.Active.Where(a => a.Control == ControlKind.Companion).ToList();
        if (_crvHooked != rc) { _crvHooked = rc; rc.CartEvents += e => { if (e.Kind == CartEventKind.Wrecked) CrevasseFallSeen(); }; } // each floor build's controller (the floor may be built again before the flow starts)
        // nobody falls but the test's own victims, at any step
        if (party.Active.FirstOrDefault(a => a.State.Fallen && !_crvMayFall.Contains(a.State.Name)) is { } stray) { Fail($"{stray.State.Name} fell at step {_crvStep} (cell {DungeonBuilder.WorldToCell(stray.GlobalPosition)}): nobody fell but the test's"); return; }

        switch (_crvStep)
        {
            case 0 when _crvClock >= 1.5f:
            {
                var watch = CrevasseWatch.Instance;
                GD.Print($"[crevasse] floor 12 ({_crvVariant}): chasm cells {layout.ChasmCount}, cavern {(rail.Cavern is { } cv ? $"{cv.From}..{cv.To}" : "none")}, planks {layout.Walkways.Count}, watch {(watch == null ? "none" : "on")}, lethality {TierRules.Lethality}, companions {companions.Count}");
                if (layout.ChasmCount == 0 || rail.Cavern == null || layout.Walkways.Count == 0) { Fail("floor 12 has no cavern, chasm or plank"); return; }
                if (watch == null) { Fail("no CrevasseWatch on a crevasse floor"); return; }
                if (companions.Count < 3) { Fail($"{companions.Count} companions, not 3"); return; }
                var span = rail.Cavern;
                foreach (var c in companions) c.Stance = Characters.Stance.Defensive;
                // A0, the deck: from three lane cells short of the span to two past it
                CrevasseWalkFrom(main, party, rail.Party[span.From - 3], Enumerable.Range(span.From - 2, span.To - span.From + 5).Select(i => rail.Party[i]));
                GD.Print($"[crevasse] A0 the deck: the party set down at lane {span.From - 3} {rail.Party[span.From - 3]}, the human to walk lanes {span.From - 2}..{span.To + 2}");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 1 when _crvClock >= 2f: // the companions settle in their slots, then the walk starts
            case 4 when _crvClock >= 2f:
            case 7 when _crvClock >= 1f:
                _crvLast = me.GlobalPosition;
                _crvStep++; _crvClock = 0f;
                break;
            case 2:
            case 5:
            case 8:
            {
                if (!CrevasseWalk(me, out var stuck)) { if (stuck != null) Fail(stuck); break; }
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 3 when _crvClock >= 4f:
            case 6 when _crvClock >= 4f:
            case 9 when _crvClock >= 4f:
            {
                var what = _crvStep == 3 ? "the deck" : _crvStep == 6 ? "the plank, across" : "the plank, back";
                var standing = companions.Select(c => $"{c.State.Name}:{c.CurrentActionName} {c.GlobalPosition.DistanceTo(me.GlobalPosition):0.0} m cell {DungeonBuilder.WorldToCell(c.GlobalPosition)}").ToList();
                GD.Print($"[crevasse] A0 {what}: the human walked {_crvWalked:0} m; companions [{string.Join(", ", standing)}]; falls logged {_crvFallLog.Count}; regions warned {CrevasseWatch.Instance?.Warned}");
                if (companions.FirstOrDefault(c => c.State.Fallen || c.Downed) is { } down) { Fail($"after {what}, {down.State.Name} is not standing (fallen={down.State.Fallen}, hp {down.State.Hp:0})"); return; }
                if (_crvFallLog.Count > 0) { Fail($"after {what}, a fall was logged: \"{_crvFallLog[0]}\""); return; }
                if (companions.FirstOrDefault(c => c.GlobalPosition.DistanceTo(me.GlobalPosition) > 10f) is { } behind) { Fail($"after {what}, {behind.State.Name} did not follow ({behind.GlobalPosition.DistanceTo(me.GlobalPosition):0.0} m off at {DungeonBuilder.WorldToCell(behind.GlobalPosition)})"); return; }
                if (_crvStep == 3)
                {
                    // the plank: from two hall cells short of it to two past, then back
                    var plank = layout.Walkways[0];
                    var along = CrevasseRules.PlankRunsAlongX(layout, plank) ? new GridPos(1, 0) : new GridPos(0, 1);
                    GridPos At(int k) => new(plank.X + along.X * k, plank.Y + along.Y * k);
                    CrevasseWalkFrom(main, party, At(-2), new[] { At(-1), At(0), At(1), At(2) });
                    GD.Print($"[crevasse] A0 the plank at {plank}: the party set down at {At(-2)}, the human to walk to {At(2)} and back");
                }
                else if (_crvStep == 6)
                {
                    _crvWay.Reverse();
                    _crvWay.RemoveAt(0);
                    _crvWay.Add(main.Builder.Surface(DungeonBuilder.WorldToCell(_crvWay[^1]) + (DungeonBuilder.WorldToCell(_crvWay[^1]) - DungeonBuilder.WorldToCell(_crvWay[^2]))));
                    _crvWayAt = 0;
                }
                else GD.Print($"[crevasse] A0 ok: the deck end to end and a plank there and back at walking speed, {companions.Count} companions following, nobody fell");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 10:
            {
                // A1: a companion put over the nearest chasm cell beside a walkable one, 0.5 m above its lip
                _crvVictim = companions[0];
                _crvMayFall.Add(_crvVictim.State.Name);
                var cell = NearestChasm(layout, DungeonBuilder.WorldToCell(_crvVictim.GlobalPosition));
                if (cell is not { } c) { Fail("no chasm cell beside a walkable one"); return; }
                _crvVictim.Stance = Characters.Stance.Hold; // held where it is put: off the grid, a following companion's failed paths would regroup it out of the drop first
                _crvVictim.Teleport(main.Builder.Surface(c) + Vector3.Up * 0.5f);
                GD.Print($"[crevasse] A1 {_crvVictim.State.Name} put over the chasm at {c} (lip y {main.Builder.Surface(c).Y:0.0})");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 11:
            {
                var v = _crvVictim!;
                if (!(v.State.Fallen && v.Downed && !v.Visible)) { if (_crvClock > 3f) Fail($"{v.State.Name} did not fall in within 3 s (fallen={v.State.Fallen}, downed={v.Downed}, visible={v.Visible}, y {v.GlobalPosition.Y:0.0})"); break; }
                GD.Print($"[crevasse] A1 {v.State.Name} fell in after {_crvClock:0.00} s: fallen, down, hidden at y {v.GlobalPosition.Y:0.0}; log \"{_crvFallLog.LastOrDefault()}\"");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 12 when _crvClock >= 8f:
            {
                var v = _crvVictim!;
                var near = root.GetChildren().OfType<Enemies.Enemy>().Count(e => IsInstanceValid(e) && e.IsAlive && e.GlobalPosition.DistanceTo(me.GlobalPosition) < 25f);
                GD.Print($"[crevasse] A1 8 s on, with {near} enemies near: {v.State.Name} fallen={v.State.Fallen} downed={v.Downed} hp={v.State.Hp:0}");
                if (!v.State.Fallen || !v.Downed) { Fail($"{v.State.Name} came back without a stop (an auto-revive?)"); return; }
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 13:
            {
                // A2: a living enemy put over the chasm nearest the party (one of the floor's regulars, spawned there: the floor's own are culled)
                var cell = NearestChasm(layout, DungeonBuilder.WorldToCell(me.GlobalPosition));
                if (cell is not { } c) { Fail("no chasm cell for the enemy"); return; }
                _crvEnemy = main.Builder.SpawnEnemy(main.Builder.RegularEnemies(main.Builder.CurrentFloor)[0], main.Builder.Surface(c) + Vector3.Up * 0.5f, main.Builder.CurrentFloor);
                if (_crvEnemy == null) { Fail("no enemy spawned for A2"); return; }
                var e = _crvEnemy;
                GD.Print($"[crevasse] A2 {e.DisplayName} put over the chasm at {c}");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 14:
            {
                var e = _crvEnemy!;
                if (!IsInstanceValid(e)) { Fail("A2's enemy was freed before it was judged"); return; }
                if (e.IsAlive || !_crvSwallowed) { if (_crvClock > 3f) Fail($"{e.DisplayName} did not die to the crevasse within 3 s (alive={e.IsAlive}, swallowed logged={_crvSwallowed}, y {e.GlobalPosition.Y:0.0})"); break; }
                GD.Print($"[crevasse] A2 {e.DisplayName} died to the crevasse after {_crvClock:0.00} s; \"The crevasse swallows\" logged");
                _crvEnemy = null;
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 15:
            {
                // A3: a crew member out of their wagon, and Grisel escorting, each over a chasm; each is held at the drop point (a test hook) so neither walks out of it first
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                _crvCrew = ctrl.Crew.FirstOrDefault(c => !c.Dead && c.Seated);
                _crvGrisel = GriselNpc(root);
                if (_crvCrew == null || _crvGrisel is not { Dead: false }) { Fail($"no crew member in a wagon ({_crvCrew?.NpcName ?? "none"}) or no living Grisel"); return; }
                if (!_crvGrisel.Escorting && AdventureDirector.Instance?.QuestGivenBy(CrewRescueController.GriselId) is { } q) AdventureDirector.Instance.Apply(new QuestAccepted(q.Id)); // her escort, as her dialogue's accept does
                if (!_crvGrisel.Escorting) { Fail("Grisel's escort did not start"); return; }
                foreach (var npc in new[] { _crvCrew, _crvGrisel })
                {
                    if (NearestChasm(layout, DungeonBuilder.WorldToCell(npc.GlobalPosition)) is not { } c) { Fail($"no chasm for {npc.NpcName}"); return; }
                    var drop = main.Builder.Surface(c) + Vector3.Up * 0.5f;
                    npc.GetOut(drop);
                    npc.MoveTarget = drop;
                    npc.MoveOverride = true;
                    GD.Print($"[crevasse] A3 {npc.NpcName} put over the chasm at {c}");
                }
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 16:
            {
                var ctrl = (CrewRescueController)ObjectiveController.Instance!;
                var crew = _crvCrew!;
                var g = _crvGrisel!;
                var crewLost = crew.Dead && ctrl.StateOf(crew) == CrewState.Lost;
                if (!crewLost || !g.Downed) { if (_crvClock > 3f) Fail($"within 3 s: {crew.NpcName} dead={crew.Dead} ({ctrl.StateOf(crew)}), Grisel downed={g.Downed} dead={g.Dead} at y {g.GlobalPosition.Y:0.0}"); break; }
                g.MoveTarget = null;
                g.MoveOverride = false;
                var cell = DungeonBuilder.WorldToCell(g.GlobalPosition);
                var byDrop = Enumerable.Range(-CrevasseRules.LipReach, 2 * CrevasseRules.LipReach + 1).Any(dy => Enumerable.Range(-CrevasseRules.LipReach, 2 * CrevasseRules.LipReach + 1).Any(dx => layout.IsChasm(new GridPos(cell.X + dx, cell.Y + dy))));
                GD.Print($"[crevasse] A3 after {_crvClock:0.00} s: {crew.NpcName} lost (hud \"{ctrl.HudText}\"); Grisel downed={g.Downed} dead={g.Dead} escorting={g.Escorting} at {cell} (walkable {layout.IsWalkable(cell)}, a drop within {CrevasseRules.LipReach}: {byDrop})");
                if (g.Dead || !g.Escorting || !CrevasseRules.SetDownOk(layout, cell) || !byDrop) { Fail("Grisel was not caught at the lip (down, not dead, escorting, on a walkable cell by the drop)"); return; }
                if (!g.TryRevive() || g.Downed) { Fail("Grisel could not be revived at the lip"); return; }
                GD.Print($"[crevasse] A3 Grisel revived at the lip: downed={g.Downed}, escorting={g.Escorting}");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 17:
            {
                // A4: the party boards at the landing with A1's companion still fallen (seated in the same frame it is set down there: a
                // hero stepping onto a platform would bring them back first; this checks the arrival)
                var v = _crvVictim!;
                if (!v.State.Fallen) { Fail($"{v.State.Name} came back before A4"); return; }
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                GD.Print($"[crevasse] A4 the party boards at stop {rc.StopIndex} with {v.State.Name} still fallen: {party.Active.Count(a => a.Cart == rc.Cart)} seated");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 18:
                if (rc.State.Mode == CartMode.Held && !rc.Halted && rc.StopIndex == 1) { _crvStep++; _crvClock = 0f; break; }
                if (_crvClock > 120f) Fail($"ride 1 did not reach stop 1 in 120 s (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})");
                break;
            case 19:
            {
                var v = _crvVictim!;
                var maxHp = v.State.Stats[Characters.Stat.MaxHp];
                var off = v.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition);
                var hpOk = Mathf.Abs(v.State.Hp - maxHp * CrevasseRules.ReturnHpFraction) <= 1f;
                if (v.State.Fallen || v.Downed || !v.Visible || !hpOk || off > 6f)
                {
                    if (_crvClock > 1f) Fail($"A4 within 1 s of the arrival at stop 1: {v.State.Name} fallen={v.State.Fallen} downed={v.Downed} visible={v.Visible} hp {v.State.Hp:0}/{maxHp:0} {off:0.0} m from the cart");
                    break;
                }
                GD.Print($"[crevasse] A4 ride 1 rolled into stop 1: {v.State.Name} back after {_crvClock:0.00} s, up, hp {v.State.Hp:0}/{maxHp:0} ({v.State.Hp / maxHp:P0}), {off:0.0} m from the cart at {DungeonBuilder.WorldToCell(v.GlobalPosition)}");
                if (!CrevasseRules.SetDownOk(layout, DungeonBuilder.WorldToCell(v.GlobalPosition))) { Fail($"{v.State.Name} came back on a cell no body may be set down on"); return; }
                _crvA4Name = v.State.Name;
                _crvMayFall.Clear();
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 20:
            {
                // B: a wagon with crew aboard coupled on (stop 1's, as a departure would couple it), the cart restored at the kiln's stop
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller"); return; }
                bool Manned(CageWagon w) => !w.Lost && !w.Coupled && ctrl.Crew.Any(c => !c.Dead && c.Ride == w);
                _crvWagon = ctrl.Wagons.FirstOrDefault(w => w.Stop == 1 && Manned(w)) ?? ctrl.Wagons.OrderBy(w => w.Stop).FirstOrDefault(Manned);
                if (_crvWagon == null || !ctrl.DebugCouple(_crvWagon.Stop)) { Fail($"no wagon with crew aboard to couple ({_crvWagon?.Stop})"); return; }
                _crvWagonCrew.Clear();
                _crvWagonCrew.AddRange(ctrl.Crew.Where(c => !c.Dead && c.Ride == _crvWagon));
                rc.Restore(Shape(rc).LastRideStop);
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate));
                _crvSparesBefore = rc.SparesUsed.Count;
                PumperCap = 0; // nobody pumps: the cart comes to the jump too slow
                if (_crvVariant == "survivor") NoRelief = true; // the spare's way, not a relief's: the human stays on foot at the stop
                GD.Print($"[crevasse] B wagon {_crvWagon.Stop} coupled with [{string.Join(", ", _crvWagonCrew.Select(c => c.NpcName))}] aboard; the cart restored at stop {rc.StopIndex} (S={rc.State.S:0.0}), train=[{string.Join(",", rc.Train.Select(w => w.Stop))}], spares used {_crvSparesBefore}, pumpers capped at 0");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case 21 when _crvClock >= 1f:
            {
                var g = _crvGrisel!;
                if (!IsInstanceValid(g) || g.Dead) { Fail("Grisel is gone before B"); return; }
                if (g.Downed) g.TryRevive();
                g.GetOut(rc.StandBesideCart()); // beside the cart, on foot: the bench takes her as the human boards
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _crvRiders.Clear();
                _crvRiders.AddRange(party.Active.Where(a => a.Cart == rc.Cart));
                foreach (var a in _crvRiders) _crvMayFall.Add(a.State.Name); // the train's fall takes them
                if (_crvRiders.Count != party.Active.Count() || g.Cart != rc.Cart) { Fail($"not everyone boarded: {_crvRiders.Count} of {party.Active.Count()} heroes seated, Grisel seated={g.Cart == rc.Cart}"); return; }
                GD.Print($"[crevasse] B boarded at stop {rc.StopIndex}: [{string.Join(", ", _crvRiders.Select(a => a.State.Name))}] and Grisel on the bench, wagon {_crvWagon!.Stop} coupled");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case CrvRide:
            {
                if (_crvFallVerdict != null) { _crvStep++; _crvClock = 0f; break; }
                if (rc.State.Mode == CartMode.Held && rc.StopIndex > Shape(rc).LastRideStop) { Fail($"the cart reached stop {rc.StopIndex}: it did not fall"); return; }
                if (rc.State.Mode == CartMode.Wrecked || rc.Falls == 0 && rc.State.Mode == CartMode.Held && rc.SparesUsed.Count > _crvSparesBefore) { Fail("the cart was wrecked, not fallen"); return; }
                if (rc.State.Mode == CartMode.Rolling)
                {
                    if (_crvVariant == "trestle" && root.GetNodeOrNull<Trestle>($"Trestle_{Kiln(rc, "trestle")}") is { } trestle && rc.Party.FrontS >= trestle.From && party.ForcedMoveInput == null)
                    {
                        party.ForcedMoveInput = new Vector2(0, 1); // the stick back: the brake, until the trestle falls with the cart on it
                        GD.Print($"[crevasse] =trestle the human brakes at front S={rc.Party.FrontS:0.0} (the trestle {trestle.From:0.0}-{trestle.To:0.0}), v {rc.State.V:0.00}");
                    }
                    if (_crvVariant == "survivor" && me.Cart == rc.Cart)
                    {
                        me.Teleport(rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[rc.StopIndex]))); // out at the stop as the cart departs (no throw)
                        _crvRiders.Remove(me);
                        _crvMayFall.Remove(me.State.Name); // on foot: the human never falls
                        GD.Print($"[crevasse] =survivor the human gets out at stop {rc.StopIndex} as the cart departs (S={rc.State.S:0.0}) and stays on foot at {DungeonBuilder.WorldToCell(me.GlobalPosition)}");
                    }
                }
                if (_crvClock > 120f) Fail($"the train did not fall within 120 s (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0})");
                break;
            }
            case CrvRide + 1:
            {
                party.ForcedMoveInput = null;
                if (_crvFallVerdict!.Length > 0) { Fail($"B the fall: {_crvFallVerdict}"); return; }
                if (_crvVariant != "survivor")
                {
                    GD.Print($"[crevasse] RESULT ok ({_crvVariant}): A0-A3 as ever; A4 {_crvVictim!.State.Name} back at stop 1's arrival; B the train fell ({_crvFallWhy}): {_crvFallSummary}");
                    _done = true;
                    GetTree().Quit(0);
                    return;
                }
                if (_crvClock < 1f) break;
                if (_crvWiped) { Fail("the party wiped with the human standing"); return; }
                if (_crvSpareStop < 1 || rc.State.Mode != CartMode.Held) { Fail($"no spare waits after the survivor's fall (spare stop {_crvSpareStop}, cart {rc.State.Mode} at stop {rc.StopIndex})"); return; }
                // off every platform first (the human stood on stop N's when the train fell), then onto stop N's
                var away = Enumerable.Range(1, 40).Select(k => rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[_crvSpareStop] + 5f * k))).FirstOrDefault(p => CrevassePlatform(layout, p) < 0);
                if (away == default) { Fail($"no spot off every platform past stop {_crvSpareStop}"); return; }
                me.Teleport(away);
                GD.Print($"[crevasse] =survivor nobody wiped; a spare waits at stop {_crvSpareStop}; the human steps off every platform to {DungeonBuilder.WorldToCell(away)}");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case CrvRide + 2 when _crvClock >= 0.5f:
            {
                if (party.Active.FirstOrDefault(a => a.Control == ControlKind.Companion && !a.State.Fallen) is { } early) { Fail($"{early.State.Name} came back off every platform"); return; }
                var onto = rc.StandBesideLane(rc.Party.Track.Point(rc.StopS[_crvSpareStop]));
                if (CrevassePlatform(layout, onto) != _crvSpareStop) { Fail($"stop {_crvSpareStop}'s platform spot {DungeonBuilder.WorldToCell(onto)} is not in its room"); return; }
                me.Teleport(onto);
                GD.Print($"[crevasse] =survivor the human steps onto stop {_crvSpareStop}'s platform at {DungeonBuilder.WorldToCell(onto)}");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case CrvRide + 3:
            {
                var fallen = _crvRiders.Where(a => a.Control == ControlKind.Companion).ToList();
                var notBack = fallen.FirstOrDefault(a => a.State.Fallen || a.Downed || a.GlobalPosition.DistanceTo(me.GlobalPosition) > CrvBeside);
                if (notBack != null) { if (_crvClock > 1f) Fail($"{notBack.State.Name} not back beside the human within 1 s (fallen={notBack.State.Fallen}, {notBack.GlobalPosition.DistanceTo(me.GlobalPosition):0.0} m off)"); break; }
                if (_crvWagonCrew.FirstOrDefault(c => !c.Dead) is { } risen) { Fail($"{risen.NpcName} who fell is not dead"); return; }
                if (_crvWiped) { Fail("the party wiped"); return; }
                var dto = Save.FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                var lostCages = dto.Objective?.LostCages ?? new List<int>();
                var lostCrew = dto.Objective?.LostCrew ?? new List<string>();
                GD.Print($"[crevasse] =survivor back beside the human: [{string.Join(", ", fallen.Select(a => $"{a.State.Name} hp {a.State.Hp:0}/{a.State.Stats[Characters.Stat.MaxHp]:0} {a.GlobalPosition.DistanceTo(me.GlobalPosition):0.0} m"))}]; capture LostCages=[{string.Join(",", lostCages)}] LostCrew=[{string.Join(",", lostCrew)}]");
                if (!lostCages.Contains(_crvWagon!.Stop)) { Fail($"the capture does not name wagon {_crvWagon.Stop} lost"); return; }
                if (_crvWagonCrew.FirstOrDefault(c => !lostCrew.Contains(c.Id)) is { } missing) { Fail($"the capture does not name {missing.NpcName} lost"); return; }
                if (fallen.FirstOrDefault(a => Mathf.Abs(a.State.Hp - a.State.Stats[Characters.Stat.MaxHp] * CrevasseRules.ReturnHpFraction) > 1f) is { } hp) { Fail($"{hp.State.Name} came back with {hp.State.Hp:0} HP, not a quarter"); return; }
                _crvMayFall.Clear(); // back: nobody falls again but the floor change's own victim
                // I1: a save and a load after the fall: nothing lost may come back, and nobody the crevasse took is lifted to the lip
                _crvLostStop = _crvWagon.Stop;
                _crvLostIds.Clear();
                _crvLostIds.AddRange(lostCrew);
                GD.Print($"[crevasse] =survivor the fallen came back on stop {_crvSpareStop}'s platform; saved and loaded (lost crew [{string.Join(",", _crvLostIds)}])");
                main.SaveGame(CrvSlot);
                if (!main.LoadGame(CrvSlot)) { Fail("the save after the fall would not load"); return; }
                _crvStep++; _crvClock = 0f;
                break;
            }
            case CrvRide + 4 when _crvClock >= 1.5f:
            {
                if (ObjectiveController.Instance is not CrewRescueController ctrl) { Fail("no crew controller after the load"); return; }
                var wagon = ctrl.Wagons.FirstOrDefault(w => w.Stop == _crvLostStop);
                if (wagon is not { Lost: true, Visible: false, Coupled: false }) { Fail($"wagon {_crvLostStop} came back after the load (lost={wagon?.Lost}, visible={wagon?.Visible})"); return; }
                var npcs = root.GetChildren().OfType<Npc>().ToDictionary(n => n.Id);
                var again = Save.FloorStateCapture.Capture(root, layout, main.Builder.Contents, main.Exploration!);
                var saved = again.NpcStates.ToDictionary(n => n.Id);
                var lines = new List<string>();
                foreach (var id in _crvLostIds)
                {
                    if (!npcs.TryGetValue(id, out var n)) { Fail($"no {id} after the load"); return; }
                    var cell = DungeonBuilder.WorldToCell(n.GlobalPosition);
                    var lip = main.Builder.Surface(cell).Y;
                    lines.Add($"{n.NpcName} dead={n.Dead} visible={n.Visible} seated={n.Seated} y {n.GlobalPosition.Y:0.0} (lip {lip:0.0}) at {cell}");
                    if (!n.Dead || n.Visible || n.Seated || !n.InTheDark()) { Fail($"after the load {lines[^1]}: not dead, hidden and below the lip"); return; }
                    if (!saved.TryGetValue(id, out var d) || !CrevasseRules.Fell(layout, DungeonBuilder.WorldToCell(new Vector3(d.X, d.Y, d.Z)), d.Y, main.Builder.Surface(DungeonBuilder.WorldToCell(new Vector3(d.X, d.Y, d.Z))).Y))
                    { Fail($"the capture after the load records {n.NpcName} at y {d?.Y:0.0}, not below the lip"); return; }
                }
                if ((again.Objective?.LostCages ?? new List<int>()).Contains(_crvLostStop) != true) { Fail($"the capture after the load does not name wagon {_crvLostStop} lost"); return; }
                GD.Print($"[crevasse] =survivor after the load: wagon {_crvLostStop} lost and hidden; [{string.Join("; ", lines)}]; the second capture keeps them below the lip");
                // I2: a companion falls, then the party goes down to floor 13: nobody is fallen there
                var victim = companions.FirstOrDefault();
                var drop = victim == null ? null : NearestChasm(layout, DungeonBuilder.WorldToCell(victim.GlobalPosition));
                if (victim == null || drop is not { } c) { Fail("no companion or chasm for the floor change"); return; }
                _crvFloorVictim = victim.State.Name;
                _crvMayFall.Add(_crvFloorVictim);
                victim.Stance = Characters.Stance.Hold;
                victim.Teleport(main.Builder.Surface(c) + Vector3.Up * 0.5f);
                GD.Print($"[crevasse] =survivor {_crvFloorVictim} put over the chasm at {c} before the floor change");
                _crvStep++; _crvClock = 0f;
                break;
            }
            case CrvRide + 5:
            {
                if (party.Active.FirstOrDefault(a => a.State.Name == _crvFloorVictim) is not { State.Fallen: true }) { if (_crvClock > 3f) Fail($"{_crvFloorVictim} did not fall within 3 s"); break; }
                GD.Print($"[crevasse] =survivor {_crvFloorVictim} fell; the party goes down to floor 13");
                if (!Game.Instance.ChangeFloor(+1)) { Fail("could not go down to floor 13"); return; }
                _crvStep++; _crvClock = 0f;
                break;
            }
        }
    }

    /// <summary>
    /// The survivor's last step, on floor 13 (ruling 8): the floor change brought every fallen hero back, at a quarter of their HP,
    /// beside the party's arrival.
    /// </summary>
    private void RunCrevasseFloorChange(Main? main)
    {
        if (main == null || !main.InGame || Game.Instance.CurrentFloor != 13 || main.Builder.Layout == null || main.Party?.Local is not { } me) return;
        _crvClock += (float)GetProcessDeltaTime();
        if (_crvClock < 1.5f) return;
        var party = main.Party;
        void Fail(string why) { GD.Print($"[crevasse] RESULT FAIL ({_crvVariant}): {why}"); _done = true; GetTree().Quit(1); }
        if (party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.State.Fallen) is { } still) { Fail($"{still.State.Name} is still fallen on floor 13"); return; }
        if (party.Adventurers.FirstOrDefault(a => IsInstanceValid(a) && a.State.Name == _crvFloorVictim) is not { } v) { Fail($"no {_crvFloorVictim} on floor 13"); return; }
        var maxHp = v.State.Stats[Characters.Stat.MaxHp];
        var off = v.GlobalPosition.DistanceTo(me.GlobalPosition);
        GD.Print($"[crevasse] =survivor on floor 13: nobody fallen; {v.State.Name} up={!v.Downed} visible={v.Visible} hp {v.State.Hp:0}/{maxHp:0}, {off:0.0} m from the human");
        if (v.Downed || !v.Visible || off > 12f || v.State.Hp > maxHp * CrevasseRules.ReturnHpFraction + 1f + maxHp * 0.05f) { Fail($"{v.State.Name} did not come back as a fallen hero does on the floor change"); return; }
        GD.Print($"[crevasse] RESULT ok (survivor): A0-A3 as ever; A4 {_crvA4Name} back at stop 1's arrival; B the train fell ({_crvFallWhy}) with the human on foot: {_crvFallSummary}; nobody wiped, the fallen came back on stop {_crvSpareStop}'s platform, the capture names wagon {_crvLostStop} and its crew lost; a save and load kept the lost wagon hidden and its crew dead below the lip; {_crvFloorVictim} fell and came back on the floor change to 13");
        _done = true;
        GetTree().Quit(0);
    }

    /// <summary>"Beside" the human for the survivor's return: each fallen hero on its own 4 m cell by the lane, two cells off at most.</summary>
    private const float CrvBeside = 2.5f * DungeonBuilder.CellSize;

    /// <summary>The ride step of B: the train rolls from the kiln's stop until it falls.</summary>
    private const int CrvRide = 22;

    /// <summary>
    /// The train's Wrecked event (told inside the fall, before anything else in the frame: the wipe that follows is Task 8's): the
    /// fall checked as it stands. Every rider fallen, the coupled wagon lost and hidden, its crew dead, Grisel down at the lip, and a
    /// spare spent only with the human standing (=survivor).
    /// </summary>
    private void CrevasseFallSeen()
    {
        if (_done || _crvStep != CrvRide || _crvFallVerdict != null || RailController.Instance is not { } rc || Main.Instance?.Builder.Layout is not { } layout) return;
        var problems = new List<string>();
        if (rc.Falls == 0) problems.Add("a wreck, not a fall");
        foreach (var h in _crvRiders) if (!h.State.Fallen) problems.Add($"{h.State.Name} not fallen"); // hidden at its next physics tick (a possession passing on may show a body for that tick)
        var w = _crvWagon!;
        if (!w.Lost || w.Visible || w.Coupled || rc.Train.Count > 0) problems.Add($"wagon {w.Stop} lost={w.Lost} visible={w.Visible} coupled={w.Coupled}, train {rc.Train.Count}");
        foreach (var c in _crvWagonCrew) if (!c.Dead || c.Visible) problems.Add($"{c.NpcName} dead={c.Dead} visible={c.Visible}");
        var g = _crvGrisel!;
        var cell = DungeonBuilder.WorldToCell(g.GlobalPosition);
        var byDrop = Enumerable.Range(-CrevasseRules.LipReach, 2 * CrevasseRules.LipReach + 1).Any(dy => Enumerable.Range(-CrevasseRules.LipReach, 2 * CrevasseRules.LipReach + 1).Any(dx => CrevasseRules.IsDrop(layout, new GridPos(cell.X + dx, cell.Y + dy))));
        if (g.Dead || !g.Downed || !g.Escorting || g.Seated || !CrevasseRules.SetDownOk(layout, cell) || !byDrop) problems.Add($"Grisel dead={g.Dead} downed={g.Downed} escorting={g.Escorting} seated={g.Seated} at {cell} (set-down {CrevasseRules.SetDownOk(layout, cell)}, a drop within {CrevasseRules.LipReach}: {byDrop})");
        var survivor = _crvVariant == "survivor";
        var spares = rc.SparesUsed.Count - _crvSparesBefore;
        if (spares != (survivor ? 1 : 0)) problems.Add($"{spares} spare(s) spent");
        if (survivor && Main.Instance.Party.Local is { } me && (me.State.Fallen || me.Downed)) problems.Add("the human on foot fell too");
        _crvFallSummary = $"{_crvRiders.Count} hero rider(s) fallen [{string.Join(", ", _crvRiders.Select(a => a.State.Name))}], wagon {w.Stop} lost with [{string.Join(", ", _crvWagonCrew.Select(c => c.NpcName))}] dead, Grisel down at {cell} (a lip cell), spares used {_crvSparesBefore}+{spares}, the cart went over at {rc.FellAt?.ToString() ?? "?"}";
        GD.Print($"[crevasse] B the train fell ({_crvFallWhy}): {_crvFallSummary}{(problems.Count > 0 ? $"; WRONG: {string.Join("; ", problems)}" : "")}");
        _crvFallVerdict = string.Join("; ", problems);
    }

    /// <summary>The stop whose room <paramref name="at"/> stands in (its platform, as the fallen's return reads it), or −1.</summary>
    private static int CrevassePlatform(FloorLayout layout, Vector3 at)
    {
        if (layout.Rail is not { } rail) return -1;
        var cell = DungeonBuilder.WorldToCell(at);
        for (var i = 0; i < rail.Stops.Count; i++)
            if (rail.Stops[i].Room is var r && r >= 0 && r < layout.Rooms.Count && layout.Rooms[r].Contains(cell)) return i;
        return -1;
    }

    /// <summary>The party set down at <paramref name="start"/>, facing the first of <paramref name="cells"/>: the human's walk, one cell centre after another.</summary>
    private void CrevasseWalkFrom(Main main, PartyController party, GridPos start, IEnumerable<GridPos> cells)
    {
        _crvWay.Clear();
        _crvWay.AddRange(cells.Select(c => main.Builder.Surface(c)));
        _crvWayAt = 0;
        _crvWalked = 0f;
        party.Teleport(main.Builder.Surface(start) + Vector3.Up * 0.1f, _crvWay[0]);
    }

    /// <summary>The human walks on (forward, never sprinting) toward the next point of the way; true once at the end. <paramref name="stuck"/> says why it cannot go on.</summary>
    private bool CrevasseWalk(Adventurer me, out string? stuck)
    {
        stuck = null;
        var flat = new Vector2(_crvWay[_crvWayAt].X - me.GlobalPosition.X, _crvWay[_crvWayAt].Z - me.GlobalPosition.Z);
        if (flat.Length() < 0.6f && ++_crvWayAt >= _crvWay.Count) { Main.Instance.Party.ForcedMoveInput = null; return true; }
        me.FaceToward(_crvWay[_crvWayAt]);
        Main.Instance.Party.ForcedMoveInput = new Vector2(0f, -0.5f); // forward, under the auto-run's threshold: a walk
        var moved = new Vector2(me.GlobalPosition.X - _crvLast.X, me.GlobalPosition.Z - _crvLast.Z).Length();
        _crvWalked += moved;
        _crvLast = me.GlobalPosition;
        if (_crvClock > 60f) stuck = $"the human did not get along the way in 60 s (at {DungeonBuilder.WorldToCell(me.GlobalPosition)}, point {_crvWayAt} of {_crvWay.Count})";
        return false;
    }

    /// <summary>The chasm cell nearest <paramref name="from"/> (straight distance, ties by Y then X) beside a walkable cell (a plank counts).</summary>
    private static GridPos? NearestChasm(FloorLayout layout, GridPos from)
    {
        GridPos? best = null;
        var bestD = long.MaxValue;
        for (var y = 0; y < layout.Height; y++)
            for (var x = 0; x < layout.Width; x++)
            {
                var c = new GridPos(x, y);
                if (!layout.IsChasm(c) || !c.Neighbours4().Any(layout.IsWalkable)) continue;
                long d = (long)(c.X - from.X) * (c.X - from.X) + (long)(c.Y - from.Y) * (c.Y - from.Y);
                if (d < bestD) { bestD = d; best = c; }
            }
        return best;
    }
}
