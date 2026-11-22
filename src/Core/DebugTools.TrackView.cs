using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-track-view --------------------------------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3, solo; every enemy is culled as it appears and the runaway on ride 4 is held.
// The exposed-track fix's view from the seat: the rails over the line's first party ramp (RailBed.RampsOn(layout, rail.Party)[0])
// and over a lane cell whose buckle roll was laid flat (DungeonBuilder.SparedRailBuckles): one the cart approaches on the cell's own
// level (no ramp and no other level from the cart's length short of the sighting point to the cell), on the ramp's ride k first, else
// on any ride k2, which takes a second pass; with none such, the first spared cell on ride k, else the first of all. Each pass kills the enemies, raises the gates, holds the runaway, restores the cart at
// the stop before the ride (rc.Restore(k - 1)), boards, departs and pumps with the lead; a heap on the ride is cleared by the test.
// As the front reaches each target's S less SightLead in travel order, the seated eye is aimed (me.AimAt) at the target's rail mid
// point and a layer-1 ray is cast from me.Camera to each rail top (the track frame's ±RailHalf across and +RailTop up), stopped 0.05 m
// short. The targets: the ramp's crest edge, the crest ± 1 m, its sag edge, and the spared cell's centre. Every ray must be clear
// ("[trackview] sight {name} S=... railL ... railR ..."). Three control rays guard the sightings: at the crest's moment a ray from the
// same eye to 0.10 m under the drawn floor at the crest edge (RailBed.DrawnAt) must be blocked within ControlNear of its end, or the
// check is vacuous; and at a level-approached spared cell (seen from over its rail tops) a ray to 3 cm under the bed at each rail must
// be stopped by the drawn floor within HeaveBedSlack of the bed's height (the cell's slab is flat under the track). With no spared cell
// approached on its own level, the fallback cell is sighted without its controls and the RESULT says so. Windowed with
// --screenshot=PATH it saves the HUD-hidden views PATH_crest.png, PATH_sag.png and PATH_heave.png at those moments. Prints
// [trackview] lines and a RESULT.
public partial class DebugTools
{
    private bool _testTrackView;
    private int _tkvStep, _tkvPass;
    private float _tkvClock;
    private int _tkvRide, _tkvHeaveRide, _tkvRays;
    private bool _tkvClimb, _tkvControlBlocked;
    private float _tkvCrestS = float.NaN, _tkvSagS = float.NaN, _tkvHeaveS = float.NaN;
    private GridPos _tkvHeaveCell;
    /// <summary>Each pass's targets (name, S) in travel order, and how many of the current pass's are sighted.</summary>
    private readonly List<(string Name, float S)>[] _tkvTargets = { new(), new() };
    private int _tkvNext;
    /// <summary>The shots asked for, and the ones saved.</summary>
    private readonly List<string> _tkvShotsSaved = new();
    private int _tkvShotsWanted;

    /// <summary>A target is sighted with the front this far short of it, in travel order.</summary>
    private const float SightLead = 6f;
    /// <summary>The control ray ends this far under the drawn floor at the crest edge.</summary>
    private const float ControlUnder = 0.10f;
    /// <summary>A control ray must be stopped by the drawn floor within this of its end.</summary>
    private const float ControlNear = 1f;
    /// <summary>The spared cell's controls end this far under the track's bed at each rail.</summary>
    private const float HeaveUnder = 0.03f;
    /// <summary>A spared cell's control must be stopped by the cell's own slab at the bed's height, within this.</summary>
    private const float HeaveBedSlack = 0.01f;
    /// <summary>The heave cell was chosen approached on its own level (else the fallback), and its controls were blocked.</summary>
    private bool _tkvHeaveLevel, _tkvHeaveControlBlocked;
    /// <summary>A sight ray stops this far short of the rail top it aims at.</summary>
    private const float SightShort = 0.05f;

    private void RunTrackViewTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _tkvClock += (float)GetProcessDeltaTime();
        HoldTheRunaway("trackview"); // Ruling 22: not this flow's business
        var party = main.Party;
        void Fail(string why)
        {
            GD.Print($"[trackview] RESULT FAIL: {why}");
            party.ForcedMoveInput = null;
            main.Hud.Visible = true;
            _done = true;
            GetTree().Quit(1);
        }
        if (RailController.Instance is not { } rc || RailEncounterController.Instance is not { } enc || layout.Rail is not { } rail) { if (_tkvClock > 3f) Fail("floor 12 has no line"); return; }
        foreach (var e in root.GetChildren().OfType<Enemies.Enemy>()) if (e.IsAlive) e.Kill(); // the flow owns the ride
        RideWaiveCages(main, rc, "trackview");
        enc.RunawayClock = 0f; // no traffic runaway on the party line while the cart stands
        if (party.Local is not { } me) { Fail("no human"); return; }
        if (party.Active.FirstOrDefault(a => a.State.Fallen) is { } fell) { Fail($"{fell.State.Name} fell at step {_tkvStep} (cell {DungeonBuilder.WorldToCell(fell.GlobalPosition)})"); return; }
        if (TrackViewShot() is { } bad) { Fail(bad); return; }
        var pass = _tkvTargets[_tkvPass];
        var ride = _tkvPass == 0 ? _tkvRide : _tkvHeaveRide;

        switch (_tkvStep)
        {
            case 0 when _tkvClock >= 1.5f:
            {
                if (TrackViewTargets(main, layout, rail, rc) is { } why) { Fail(why); return; }
                _tkvStep++; _tkvClock = 0f;
                break;
            }
            case 1:
            {
                // the pass's setup: the gates up, the cart at the stop before the ride
                rc.RestoreGates(Enumerable.Range(1, Shape(rc).Intermediate)); // the test's shortcut: no holders to kill
                rc.Restore(ride - 1);
                GD.Print($"[trackview] pass {_tkvPass + 1}: restored at stop {rc.StopIndex} (S={rc.State.S:0.0}) for ride {ride}; targets [{string.Join(", ", pass.Select(t => $"{t.Name} S={t.S:0.0}"))}]");
                _tkvNext = 0;
                _tkvStep++; _tkvClock = 0f;
                break;
            }
            case 2 when _tkvClock >= 1f:
            {
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                _tkvStep++; _tkvClock = 0f;
                break;
            }
            case 3:
            {
                var heroes = party.Active.Count();
                var seated = party.Active.Count(a => a.Cart == rc.Cart);
                if ((!me.Seated || seated < heroes) && _tkvClock < 3f) break;
                if (!me.Seated || me.Cart != rc.Cart) { Fail("the lead did not take a seat"); return; }
                GD.Print($"[trackview] pass {_tkvPass + 1} boarded at stop {rc.StopIndex}: {seated} of {heroes} heroes seated");
                _tkvStep++; _tkvClock = 0f;
                break;
            }
            case 4:
            {
                if (rc.State.Mode != CartMode.Rolling || enc.ArmedRide != ride) { if (_tkvClock > 15f) Fail($"ride {ride} did not depart (mode {rc.State.Mode}, armed {enc.ArmedRide})"); break; }
                // a heap on the ride would halt the cart short of the targets: the test clears it (it is --test-chase's to break)
                foreach (var (id, pr, kind, _) in enc.DebugPieces())
                    if (pr == ride && kind == "heap" && enc.SetPieceNode(id) is OreHeap { IsAlive: true } heap) { heap.DebugClear(); GD.Print($"[trackview] {id} cleared by the test"); }
                party.ForcedMoveInput = new Vector2(0, -1); // the stick forward: the lead pumps
                GD.Print($"[trackview] pass {_tkvPass + 1} departed stop {ride - 1} at S={rc.State.S:0.0} (travel {(rc.Party.Dir > 0 ? "+" : "-")}S), the lead pumping");
                _tkvStep++; _tkvClock = 0f;
                break;
            }
            case 5:
            {
                party.ForcedMoveInput = new Vector2(0, -1);
                if (rc.State.Mode != CartMode.Rolling || rc.StopIndex != ride - 1) { Fail($"pass {_tkvPass + 1} left ride {ride} short of its targets (mode {rc.State.Mode}, stop {rc.StopIndex}, S={rc.State.S:0.0}, next {pass[_tkvNext].Name})"); return; }
                if (_tkvClock > 90f) { Fail($"pass {_tkvPass + 1} did not reach {pass[_tkvNext].Name} (S={pass[_tkvNext].S:0.0}) within 90 s (front S={rc.Party.FrontS:0.0})"); return; }
                var dir = rc.Party.Dir;
                var front = rc.Party.FrontS;
                while (_tkvNext < pass.Count && _tkvShot == null && dir * (front - pass[_tkvNext].S) >= -SightLead)
                {
                    var (name, s) = pass[_tkvNext];
                    if (TrackViewSight(layout, rc, me, name, s, front) is { } why) { Fail(why); return; }
                    _tkvNext++;
                }
                if (_tkvNext < pass.Count || _tkvShot != null) break;
                party.ForcedMoveInput = null;
                if (_tkvPass == 0 && _tkvTargets[1].Count > 0) { _tkvPass = 1; _tkvStep = 1; _tkvClock = 0f; break; }
                var shots = Shooting3D ? $", shots saved ({string.Join(", ", _tkvShotsSaved)})" : "";
                if (Shooting3D && _tkvShotsSaved.Count != _tkvShotsWanted) { Fail($"{_tkvShotsSaved.Count} of {_tkvShotsWanted} shots saved"); return; }
                if (!_tkvControlBlocked || _tkvHeaveLevel && !_tkvHeaveControlBlocked) { Fail("a control never ran"); return; }
                var heaveControls = _tkvHeaveLevel ? ", the spared cell's controls stopped at the bed's height"
                    : ", the spared cell sighted WITHOUT its controls (the fallback: no spared cell is approached on its own level)";
                GD.Print($"[trackview] RESULT ok: crest S={_tkvCrestS:0.0} sag S={_tkvSagS:0.0} ({(_tkvClimb ? "climb" : "descent")}, ride {_tkvRide}), heave cell {_tkvHeaveCell} S={_tkvHeaveS:0.0} (ride {_tkvHeaveRide}), {_tkvRays} rays clear, control blocked{heaveControls}{shots}");
                _done = true;
                GetTree().Quit(0);
                return;
            }
        }
    }

    /// <summary>
    /// The first party ramp on the line, its ride and its crest and sag S; and the spared party-lane cell to sight: one approached on
    /// its own level, on the ramp's ride first, else on any ride; with none such (the fallback, sighted without its controls) the first
    /// spared cell on the ramp's ride, else the first of all. Null, or why not.
    /// </summary>
    private string? TrackViewTargets(Main main, FloorLayout layout, RailLine rail, RailController rc)
    {
        var ramps = RailBed.RampsOn(layout, rail.Party);
        if (ramps.Count == 0) return "no rail ramp on floor 12";
        var ramp = ramps[0];
        var track = main.Builder.PartyTrack!;
        int RideAt(float s)
        {
            var k = 1;
            while (k < rc.StopS.Length - 1 && s >= rc.StopS[k]) k++;
            return k;
        }
        Vector3 At(System.Numerics.Vector2 xz, float y) => new(xz.X, y, xz.Y);
        _tkvSagS = ClosestSInXz(track, At(RailBed.SagEdge(ramp, DungeonBuilder.CellSize), DungeonBuilder.FloorTop + layout.Ramps[ramp.Low].LowHeight));
        _tkvCrestS = ClosestSInXz(track, At(RailBed.CrestEdge(ramp, DungeonBuilder.CellSize), DungeonBuilder.FloorTop + layout.Ramps[ramp.High].HighHeight));
        _tkvClimb = ramp.Climb;
        _tkvRide = RideAt(_tkvSagS);
        GD.Print($"[trackview] ramp {ramp.Low}->{ramp.High} ({(ramp.Climb ? "climb" : "descent")}) on ride {_tkvRide}: sag S={_tkvSagS:0.0}, crest S={_tkvCrestS:0.0}");

        var spared = main.Builder.SparedRailBuckles.Where(layout.IsPartyRail).Select(c => (Cell: c, S: ClosestSInXz(track, main.Builder.Surface(c)))).ToList();
        GD.Print($"[trackview] spared party-lane cells: {string.Join(", ", spared.Select(c => $"{c.Cell} S={c.S:0.0} ride {RideAt(c.S)}"))}");
        if (spared.Count == 0) return "no spared lane cell";
        // the cell is sighted from its own level: a spared cell whose approach, from the whole cart's length short of the sighting point
        // (target - SightLead) to the cell, lies on cells of the cell's own level with no ramp; on the ramp's ride first, else on any ride.
        // With none such, the brief's rule: the first spared cell on the ramp's ride, else the first of all
        var travel = rc.Party.Dir; // the approach is the stretch before the cell in travel order
        bool LevelApproach((GridPos Cell, float S) c)
        {
            var level = layout.LevelOf(c.Cell);
            for (var d = SightLead + 2f * CartRules.HalfLength; d >= 0f; d -= 0.5f)
            {
                var s = c.S - travel * d;
                var g = DungeonBuilder.WorldToCell(TrackPoint(track, s));
                if (layout.Ramps.ContainsKey(g) || layout.LevelOf(g) != level) return false;
            }
            return true;
        }
        var level = spared.Where(LevelApproach).ToList();
        GD.Print($"[trackview] spared cells approached on their own level: {(level.Count == 0 ? "none" : string.Join(", ", level.Select(c => $"{c.Cell} S={c.S:0.0} ride {RideAt(c.S)}")))}");
        var onRide = level.FindIndex(c => RideAt(c.S) == _tkvRide);
        (GridPos Cell, float S) heave;
        if (level.Count > 0) { heave = level[onRide >= 0 ? onRide : 0]; _tkvHeaveLevel = true; }
        else
        {
            var any = spared.FindIndex(c => RideAt(c.S) == _tkvRide);
            heave = spared[any >= 0 ? any : 0];
        }
        (_tkvHeaveCell, _tkvHeaveS, _tkvHeaveRide) = (heave.Cell, heave.S, RideAt(heave.S));

        var dir = rc.Party.Dir;
        _tkvTargets[0].AddRange(new[] { ("sag", _tkvSagS), ("crest-1", _tkvCrestS - 1f), ("crest", _tkvCrestS), ("crest+1", _tkvCrestS + 1f) });
        _tkvTargets[_tkvHeaveRide == _tkvRide ? 0 : 1].Add(("heave", _tkvHeaveS));
        foreach (var t in _tkvTargets) t.Sort((a, b) => (dir * a.S).CompareTo(dir * b.S));
        _tkvShotsWanted = Shooting3D ? 3 : 0;
        GD.Print($"[trackview] heave cell {_tkvHeaveCell} at S={_tkvHeaveS:0.0} on ride {_tkvHeaveRide} ({(_tkvHeaveLevel ? "approached on its own level" : "the fallback: no spared cell is approached on its own level")}){(_tkvHeaveRide == _tkvRide ? "" : " (a second pass)")}; sighted from the seat {SightLead:0} m short of each target in travel order");
        return null;
    }

    /// <summary>
    /// The target <paramref name="name"/> at <paramref name="s"/>: the seated eye aimed at its rail mid point and a layer-1 ray to each rail top;
    /// at the crest the control under the drawn floor, and at a level-approached spared cell the two controls under its bed at each rail;
    /// a shot queued windowed. Null, or why it failed.
    /// </summary>
    private string? TrackViewSight(FloorLayout layout, RailController rc, Adventurer me, string name, float s, float front)
    {
        var f = rc.Party.Track.Frame(s);
        var x = f.Basis.X.Normalized();
        var y = f.Basis.Y.Normalized();
        var mid = f.Origin + y * TrackDepthRules.RailTop;
        me.AimAt(mid);
        var eye = me.Camera.GlobalPosition;
        string Ray(Vector3 to) => Sight(me, eye, eye + (to - eye).Normalized() * Mathf.Max(0f, eye.DistanceTo(to) - SightShort));
        var left = Ray(mid - x * TrackDepthRules.RailHalf);
        var right = Ray(mid + x * TrackDepthRules.RailHalf);
        GD.Print($"[trackview] sight {name} S={s:0.0} railL {left} railR {right} (front S={front:0.0}, the eye {eye.DistanceTo(mid):0.0} m off, {eye.Y - mid.Y:+0.00;-0.00} m over the rail tops)");
        if (left != "clear" || right != "clear") return $"the rails at {name} (S={s:0.0}) are not in sight from the seat: railL {left}, railR {right}";
        _tkvRays += 2;
        // a control must hit the drawn floor near its target, or the sight check is vacuous; given slabY (a spared cell's), it must
        // be stopped by the drawn floor at that height, within HeaveBedSlack: the slab lies flat at the bed under the rails
        string? Control(string what, Vector3 under, float? slabY = null)
        {
            var hit = me.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, under, 1));
            var at = hit.Count == 0 ? (Vector3?)null : hit["position"].AsVector3();
            var floor = hit.Count > 0 && IsDrawnFloor(hit["collider"].AsGodotObject());
            var off = at is { } h && slabY is { } y0 ? h.Y - y0 : 0f;
            var ok = at is { } p && (slabY == null ? p.DistanceTo(under) <= ControlNear : floor && Mathf.Abs(off) <= HeaveBedSlack);
            GD.Print($"[trackview] control {what}: a ray to {under.Y:0.000} m {(at is { } a ? $"blocked by {(hit["collider"].AsGodotObject() as Node)?.Name} at {a.DistanceTo(eye):0.00} m, {a.DistanceTo(under):0.00} m from its end" : "clear")}{(slabY != null && at != null ? $", {off * 100f:+0.0;-0.0} cm from the bed's height" : "")}");
            if (ok) return null;
            return slabY == null
                ? $"the sight check is vacuous: the control {what} is {(at == null ? "clear" : $"blocked {at.Value.DistanceTo(under):0.00} m from its end, not within {ControlNear:0.0} m")}"
                : $"the spared cell's slab is not at the bed's height: the control {what} is {(at == null ? $"clear (the slab lies more than {HeaveUnder * 100f:0} cm under the bed)" : $"stopped {off * 100f:+0.0;-0.0} cm from it{(floor ? "" : ", not by the drawn floor")}")}";
        }
        if (name == "crest")
        {
            var edge = RailBed.CrestEdge(RailBed.RampsOn(layout, layout.Rail!.Party)[0], DungeonBuilder.CellSize);
            var drawn = DungeonBuilder.FloorTop + RailBed.DrawnAt(layout, edge, DungeonBuilder.CellSize);
            if (Control($"{ControlUnder:0.00} m under the drawn floor at the crest edge", new Vector3(edge.X, drawn - ControlUnder, edge.Y)) is { } vacuous) return vacuous;
            _tkvControlBlocked = true;
        }
        if (name == "heave" && _tkvHeaveLevel)
        {
            // from its own level the eye looks down on the cell; the rails lowered under the bed must be stopped by its slab at the bed's
            // height (the curve's point, on a flat cell the slab's top)
            if (eye.Y <= mid.Y) return $"the spared cell is sighted from under its rail tops (the eye {eye.Y - mid.Y:+0.00;-0.00} m over them)";
            foreach (var side in new[] { -1f, 1f })
                if (Control($"{HeaveUnder * 100f:0} cm under the bed at rail {(side < 0 ? "L" : "R")} over the spared cell", f.Origin + x * (side * TrackDepthRules.RailHalf) - y * HeaveUnder, f.Origin.Y) is { } vacuous) return vacuous;
            _tkvHeaveControlBlocked = true;
        }
        if (Shooting3D && name is "crest" or "sag" or "heave")
        {
            Main.Instance.Hud.Visible = false;
            _tkvShot = (name, 3);
        }
        return null;
    }

    /// <summary>The windowed run with --screenshot=PATH: the views are drawn and saved.</summary>
    private bool Shooting3D => _testTrackView && _screenshotPath != null && Drawn;
    /// <summary>The shot being drawn: its name and the frames left before it is saved.</summary>
    private (string Name, int Frames)? _tkvShot;

    /// <summary>The shot in hand, drawn for three frames with the HUD hidden and the aim held, then saved as PATH_name.png. Null, or why it failed.</summary>
    private string? TrackViewShot()
    {
        if (_tkvShot is not { } shot) return null;
        if (shot.Frames > 1) { _tkvShot = (shot.Name, shot.Frames - 1); return null; }
        _tkvShot = null;
        var img = GetViewport().GetTexture().GetImage();
        var path = System.IO.Path.ChangeExtension(_screenshotPath!, null) + "_" + shot.Name + ".png";
        var err = img.SavePng(path);
        Main.Instance.Hud.Visible = true;
        GD.Print($"[trackview] shot {shot.Name} ({err}) {path}: {img.GetWidth()}x{img.GetHeight()}, front S={RailController.Instance?.Party.FrontS:0.0}");
        if (err != Error.Ok) return $"the {shot.Name} shot was not saved ({err})";
        _tkvShotsSaved.Add(shot.Name);
        return null;
    }
}
