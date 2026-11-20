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
// cell by the drop) and revivable. After every step nobody but the test's own victim is fallen. The variants (Tasks 7 and 8)
// add steps after A3. Prints [crevasse] lines and a RESULT.
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

    private void CrevasseListen()
    {
        if (_crvListening) return;
        _crvListening = true;
        EventBus.Instance.Message += text =>
        {
            if (_done || !_testCrevasse) return;
            if (text.Contains("falls into the crevasse") || text.Contains("slips into a crack")) _crvFallLog.Add(text);
            if (text.StartsWith("The crevasse swallows")) _crvSwallowed = true;
        };
    }

    private void RunCrevasseTest()
    {
        var main = Main.Instance;
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
        // nobody falls but the test's own victim, at any step
        if (party.Active.FirstOrDefault(a => a.State.Fallen && a != _crvVictim) is { } stray) { Fail($"{stray.State.Name} fell at step {_crvStep} (cell {DungeonBuilder.WorldToCell(stray.GlobalPosition)}): nobody fell but the test's"); return; }

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
                // Tasks 7 and 8 add their steps here
                GD.Print($"[crevasse] RESULT ok ({_crvVariant}): A0 nobody fell on the deck or the plank; A1 {_crvVictim!.State.Name} fell in, still fallen 8 s on; A2 an enemy swallowed; A3 {_crvCrew!.NpcName} lost, Grisel caught at the lip and revived");
                party.ForcedMoveInput = null;
                _done = true;
                GetTree().Quit(0);
                break;
        }
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
