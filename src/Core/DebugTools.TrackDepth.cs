using System.Globalization;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using Godot;

namespace DungeonCrawler.Core;

// ---- --track-depth[=CM] [--track-depth-float] ---------------------------------------------------------------------
// On --print-map (which it implies): once the floor stands, every RailTrack under the floor root is probed against the drawn
// floor, every TrackDepthRules.Step of S (.superpowers/sdd/track-under-floor-investigation.md, "Headless measurement"). At each S
// the frame's X and Y give five probes: the rail tops (railL, railR), the rail-top height between them (mid) and the sleeper tops
// (sleeperL, sleeperR). A layer-1 ray straight down over each probe, from RayUp over the curve to RayDown under it, finds the drawn
// floor: only the Structure GridMap and the Chasms/Footings body count, any other collider is excluded and the ray cast again.
// A probe with more than CM cm of floor over it is buried. Point queries at each rail's middle and each sleeper top find the probe
// inside a GridMap item (a plinth, a wall cube, a ramp box). Findings merge into runs per track; the ramps on both lanes, the set
// pieces' S and the cavern's span follow, and the verdict is the last line. A probe whose ray finds no drawn floor is counted
// nofloorDrop over a chasm or pit cell (a legitimate drop) and nofloorOff anywhere else (a track deeper or higher than the ray's
// window, or a probe inside a wall cube); a ray that spent its MaxCasts on other colliders is counted budget, never a drop: the pass
// is buried=0 inside=0 nofloorOff=0 budget=0. Every track is sampled at each Step and at its own Length. Each per-track line and the
// verdict carry the worst float (the curve over the highest floor under its probes); with --track-depth-float each sample standing
// more than CM cm over it is listed (floats are not counted as failures). The ramp lines' sag and crest S are the curve's nearest
// point to the ramp's end edge in XZ (the track's height there does not matter). The physics queries run on the physics tick after
// the --print-map block, which then prints the ASCII map and quits.
// Blind spots: a track 3 m or more under the upper half of a ramp is silent (the ray starts inside or under the slope box and reads
// the ramp item's under-slab as the floor: neither buried, inside nor nofloorOff; the next samples enter the plinth and are caught).
// RayUp cannot grow past 3.8 m: the vaults spring 3.8 m over the floor top, and a ray started above that would read a ceiling as floor.
public partial class DebugTools
{
    private bool _trackDepth, _trackDepthFloat, _trackDepthDue;
    private int _trackDepthCm = TrackDepthRules.DefaultCm;

    private static readonly string[] DepthRayProbes = { "railL", "railR", "mid", "sleeperL", "sleeperR" };

    private static void DepthLine(FormattableString line) => GD.Print("[track-depth] " + line.ToString(CultureInfo.InvariantCulture));

    private static int Cm(float m) => (int)MathF.Round(m * 100f);

    private void RunTrackDepth()
    {
        if (Main.Instance?.Builder is not { Layout: { } layout, FloorRoot: { } root } builder) { GD.PrintErr("[track-depth] no floor"); return; }
        var space = root.GetWorld3D().DirectSpaceState;
        var cm = _trackDepthCm;
        var tracks = new List<RailTrack>();
        CollectTracks(root, tracks);
        static string Label(RailTrack t) => t.Name.ToString() is "PartyTrack" or "EnemyTrack" ? t.Name.ToString() : $"{t.GetParent()?.Name}/{t.Name}";
        DepthLine($"threshold={cm}cm step={TrackDepthRules.Step}m tracks={string.Join(",", tracks.Select(Label))}");

        int totalBuried = 0, totalInside = 0, totalRuns = 0, totalOff = 0, totalBudget = 0;
        float totalWorst = 0f, totalFloat = 0f;
        foreach (var track in tracks)
        {
            var label = Label(track);
            var hits = new List<DepthHit>();
            var floats = new List<DepthHit>();
            var buriedS = new HashSet<float>();
            var insideS = new HashSet<float>();
            int samples = 0, noFloorDrop = 0, noFloorOff = 0, budget = 0;
            DepthHit? worst = null;
            float worstFloat = 0f, worstFloatS = 0f;
            foreach (var s in SampleS(track.Length))
            {
                samples++;
                var f = track.GlobalTransform * track.Curve.SampleBakedWithRotation(s, true, true);
                var x = f.Basis.X.Normalized();
                var y = f.Basis.Y.Normalized();
                var bed = f.Origin;
                var laneCell = DungeonBuilder.WorldToCell(bed);
                var near = new[] { laneCell, DungeonBuilder.WorldToCell(TrackPoint(track, s - DungeonBuilder.CellSize)), DungeonBuilder.WorldToCell(TrackPoint(track, s + DungeonBuilder.CellSize)) };
                bool RampNear(GridPos c) => layout[c] == Cell.Ramp || near.Any(n => layout[n] == Cell.Ramp);

                // the floor under each probe, by a ray from above
                var probes = new[]
                {
                    bed - x * TrackDepthRules.RailHalf + y * TrackDepthRules.RailTop,
                    bed + x * TrackDepthRules.RailHalf + y * TrackDepthRules.RailTop,
                    bed + y * TrackDepthRules.RailTop,
                    bed - x * TrackDepthRules.SleeperHalf + y * TrackDepthRules.SleeperTop,
                    bed + x * TrackDepthRules.SleeperHalf + y * TrackDepthRules.SleeperTop,
                };
                float? highest = null;
                for (var i = 0; i < probes.Length; i++)
                {
                    var p = probes[i];
                    var (found, exhausted) = DrawnFloorUnder(space, p, bed.Y);
                    if (found is not { } floorY)
                    {
                        if (exhausted) budget++;
                        else if (TrackDepthRules.OverDrop(layout[DungeonBuilder.WorldToCell(p)])) noFloorDrop++;
                        else noFloorOff++;
                        continue;
                    }
                    highest = highest is { } h ? MathF.Max(h, floorY) : floorY;
                    var under = floorY - p.Y;
                    if (!TrackDepthRules.Buried(under, cm)) continue;
                    var cell = DungeonBuilder.WorldToCell(p);
                    var buckled = builder.BuckledCells.TryGetValue(cell, out var variant);
                    var cause = TrackDepthRules.Cause(buckled, RampNear(cell), false, null, false);
                    var hit = new DepthHit(label, s, cell, DepthRayProbes[i], under, cause);
                    hits.Add(hit);
                    buriedS.Add(s);
                    if (worst is not { } w || under > w.Under) worst = hit;
                    DepthLine($"{label} S={s:0.0} cell={cell} {layout[cell]} L{layout.LevelOf(cell)} items={builder.DebugCell(cell)} probe={DepthRayProbes[i]} top={p.Y:0.000} floor={floorY:0.000} under={Cm(under)}cm cause={CauseName(cause, variant)}");
                }

                // the probe inside a drawn item: each rail's middle and each sleeper top
                var points = new[]
                {
                    (probes[0] - y * TrackDepthRules.MidRail, "railL"),
                    (probes[1] - y * TrackDepthRules.MidRail, "railR"),
                    (probes[3], "sleeperL"),
                    (probes[4], "sleeperR"),
                };
                foreach (var (p, name) in points)
                {
                    if (InsideDrawn(space, p) is not { } inside) continue;
                    var cell = new GridPos(inside.Map.X, inside.Map.Z);
                    var buckled = builder.BuckledCells.TryGetValue(cell, out var variant);
                    var cause = TrackDepthRules.Cause(buckled, RampNear(cell), false, inside.Item, inside.Map.Y < layout.LevelOf(laneCell));
                    hits.Add(new DepthHit(label, s, cell, name, 0f, cause));
                    insideS.Add(s);
                    DepthLine($"{label} S={s:0.0} cell={cell} {layout[cell]} L{layout.LevelOf(cell)} probe={name} inside={inside.Item}@({inside.Map.X},{inside.Map.Y},{inside.Map.Z}) cause={CauseName(cause, variant)}");
                }

                // the curve standing over the floor (a sag): the worst always, each sample on request
                if (highest is { } over && bed.Y - over > worstFloat) { worstFloat = bed.Y - over; worstFloatS = s; }
                if (_trackDepthFloat && highest is { } top && TrackDepthRules.Buried(bed.Y - top, cm))
                {
                    var buckled = builder.BuckledCells.TryGetValue(laneCell, out var variant);
                    var cause = TrackDepthRules.Cause(buckled, RampNear(laneCell), true, null, false);
                    floats.Add(new DepthHit(label, s, laneCell, "bed", bed.Y - top, cause));
                    DepthLine($"float {label} S={s:0.0} cell={laneCell} {layout[laneCell]} L{layout.LevelOf(laneCell)} base={bed.Y:0.000} floor={top:0.000} over={Cm(bed.Y - top)}cm cause={CauseName(cause, variant)}");
                }
            }

            var runs = TrackDepthRules.Runs(hits);
            foreach (var run in runs)
                DepthLine($"run {run.Track} S={run.FromS:0.0}..{run.ToS:0.0} cells=[{string.Join(",", run.Cells)}] samples={run.Samples} worst={Cm(run.Worst)}cm probe={run.Probe} cause={CauseName(run.Cause, RunVariant(builder, run))}");
            foreach (var run in TrackDepthRules.Runs(floats))
                DepthLine($"float run {run.Track} S={run.FromS:0.0}..{run.ToS:0.0} cells=[{string.Join(",", run.Cells)}] samples={run.Samples} worst={Cm(run.Worst)}cm cause={CauseName(run.Cause, -1)}");
            var at = worst is { } ww ? $" at S={ww.S.ToString("0.0", CultureInfo.InvariantCulture)} cell={ww.Cell}" : "";
            var floatAt = Cm(worstFloat) > 0 ? $" at S={worstFloatS.ToString("0.0", CultureInfo.InvariantCulture)}" : "";
            DepthLine($"{label} length={track.Length:0.0} samples={samples} buried={buriedS.Count} inside={insideS.Count} runs={runs.Count} worst={Cm(worst?.Under ?? 0f)}cm{at} float={Cm(worstFloat)}cm{floatAt} nofloorDrop={noFloorDrop} nofloorOff={noFloorOff} budget={budget}");
            totalBuried += buriedS.Count;
            totalInside += insideS.Count;
            totalRuns += runs.Count;
            totalOff += noFloorOff;
            totalBudget += budget;
            totalFloat = MathF.Max(totalFloat, worstFloat);
            if (worst is { } tw) totalWorst = MathF.Max(totalWorst, tw.Under);
        }

        // the line's ramps on both lanes, the set pieces and the cavern: what a later S shift is measured against
        var ramps = 0;
        if (layout.Rail is { } rail)
        {
            var stops = RailController.Instance?.StopS ?? Array.Empty<float>();
            int RideAt(float s)
            {
                if (stops.Length < 2) return 0;
                var k = 1;
                while (k < stops.Length - 1 && s >= stops[k]) k++;
                return k;
            }
            foreach (var (name, lane, track) in new[] { ("PartyTrack", rail.Party, builder.PartyTrack), ("EnemyTrack", rail.Enemy, builder.EnemyTrack) })
            {
                if (track == null) continue;
                var onLane = RailBed.RampsOn(layout, lane);
                if (name == "PartyTrack") ramps = onLane.Count;
                foreach (var r in onLane)
                {
                    var sag2 = RailBed.SagEdge(r, DungeonBuilder.CellSize);
                    var crest2 = RailBed.CrestEdge(r, DungeonBuilder.CellSize);
                    var sag = new Vector3(sag2.X, DungeonBuilder.FloorTop + layout.Ramps[r.Low].LowHeight, sag2.Y);
                    var crest = new Vector3(crest2.X, DungeonBuilder.FloorTop + layout.Ramps[r.High].HighHeight, crest2.Y);
                    var ride = builder.PartyTrack is { } party ? RideAt(ClosestSInXz(party, sag)) : 0;
                    DepthLine($"ramp {name} ride={ride} cells={r.Low}->{r.High} {(r.Climb ? "climb" : "descent")} sag S={ClosestSInXz(track, sag):0.0} crest S={ClosestSInXz(track, crest):0.0}");
                }
            }
            if (RailEncounterController.Instance is { } enc)
                foreach (var (id, ride, kind, s) in enc.DebugPieces()) DepthLine($"piece {id} ride={ride} kind={kind} S={s:0.0}");
            if (rail.Cavern is { } cav && builder.PartyTrack is { } pt)
            {
                float S(int i) => pt.Curve.GetClosestOffset(pt.ToLocal(builder.Surface(rail.Party[i])));
                var half = DungeonBuilder.CellSize / 2f;
                DepthLine($"cavern party S={S(cav.From) - half:0.0}..{S(cav.To) + half:0.0}");
            }
            else DepthLine($"cavern none");
        }

        var buckledLane = builder.BuckledCells.Keys.Count(layout.IsRail);
        GD.Print("[track-depth] " + TrackDepthRules.Verdict(totalBuried, totalInside, totalOff, totalBudget, totalRuns, totalWorst, totalFloat, buckledLane, ramps));
    }

    private static void CollectTracks(Node node, List<RailTrack> into)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is RailTrack t) into.Add(t);
            CollectTracks(child, into);
        }
    }

    /// <summary>The S of every sample: each <see cref="TrackDepthRules.Step"/> from 0, and the track's own end.</summary>
    private static IEnumerable<float> SampleS(float length)
    {
        var last = 0f;
        for (var k = 0; k * TrackDepthRules.Step <= length + 1e-4f; k++) yield return last = k * TrackDepthRules.Step;
        if (length - last > 1e-3f) yield return length;
    }

    /// <summary>
    /// The S of the track's point nearest <paramref name="world"/> in XZ: the 3D-nearest point first, then a scan 4 m either side of
    /// it every centimetre. The 3D-nearest alone slides along a slope when the track runs over or under the point's height.
    /// </summary>
    private static float ClosestSInXz(RailTrack t, Vector3 world)
    {
        var s0 = t.Curve.GetClosestOffset(t.ToLocal(world));
        float best = s0, bestD = float.MaxValue;
        for (var s = Mathf.Max(0f, s0 - 4f); s <= Mathf.Min(t.Length, s0 + 4f); s += 0.01f)
        {
            var p = TrackPoint(t, s);
            var d = (p.X - world.X) * (p.X - world.X) + (p.Z - world.Z) * (p.Z - world.Z);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    private static Vector3 TrackPoint(RailTrack t, float s) => t.ToGlobal(t.Curve.SampleBaked(Mathf.Clamp(s, 0f, t.Length), true));

    /// <summary>The drawn floor: the cell grid, or a crevasse's deck footings. Every other collider (carts, barricades, props) is not.</summary>
    private static bool IsDrawnFloor(GodotObject? o) => o is GridMap grid && grid.Name.ToString() == "Structure"
        || o is StaticBody3D body && body.Name.ToString() == "Footings" && body.GetParent()?.Name.ToString() == "Chasms";

    /// <summary>
    /// The height of the drawn floor straight under <paramref name="p"/>, from <see cref="TrackDepthRules.RayUp"/> over the curve to
    /// <see cref="TrackDepthRules.RayDown"/> under it; null with no floor in that window, and <c>Exhausted</c> when every one of the
    /// <see cref="TrackDepthRules.MaxCasts"/> casts met another collider (no answer, not a drop).
    /// </summary>
    private static (float? Y, bool Exhausted) DrawnFloorUnder(PhysicsDirectSpaceState3D space, Vector3 p, float bedY)
    {
        var from = new Vector3(p.X, bedY + TrackDepthRules.RayUp, p.Z);
        var to = new Vector3(p.X, bedY - TrackDepthRules.RayDown, p.Z);
        var exclude = new Godot.Collections.Array<Rid>();
        for (var cast = 0; cast < TrackDepthRules.MaxCasts; cast++)
        {
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1, exclude));
            if (hit.Count == 0) return (null, false);
            if (IsDrawnFloor(hit["collider"].AsGodotObject())) return (hit["position"].AsVector3().Y, false);
            exclude.Add(hit["rid"].AsRid());
        }
        return (null, true);
    }

    /// <summary>The drawn item <paramref name="p"/> lies inside, by its map cell and library name; null when none (a concave bump is caught by the ray instead).</summary>
    private static (Vector3I Map, string Item)? InsideDrawn(PhysicsDirectSpaceState3D space, Vector3 p)
    {
        foreach (var r in space.IntersectPoint(new PhysicsPointQueryParameters3D { Position = p, CollisionMask = 1 }, 8))
        {
            var o = r["collider"].AsGodotObject();
            if (!IsDrawnFloor(o)) continue;
            if (o is GridMap grid)
            {
                var map = grid.LocalToMap(grid.ToLocal(p));
                var item = grid.GetCellItem(map);
                return (map, item == GridMap.InvalidCellItem ? "?" : grid.MeshLibrary?.GetItemName(item) ?? "?");
            }
            var cell = DungeonBuilder.WorldToCell(p);
            return (new Vector3I(cell.X, (int)Mathf.Floor(p.Y / DungeonBuilder.CellSize), cell.Y), "Footings");
        }
        return null;
    }

    /// <summary>A run's buckle variant: its first buckled cell's, or -1.</summary>
    private static int RunVariant(DungeonBuilder builder, DepthRun run)
    {
        foreach (var c in run.Cells) if (builder.BuckledCells.TryGetValue(c, out var v)) return v;
        return -1;
    }

    private static string CauseName(DepthCause cause, int variant) => cause switch
    {
        DepthCause.Buckled => variant >= 0 ? $"buckled{variant}" : "buckled",
        DepthCause.RampCrest => "ramp-crest",
        DepthCause.RampSag => "ramp-sag",
        DepthCause.Wall => "wall",
        DepthCause.Plinth => "plinth",
        _ => "other",
    };
}
