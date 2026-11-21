using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 12's dark stretch (a set piece "dark", Ruling 16): from its At the line runs <see cref="DarkRules.Length"/> m with its pocket lamps out.
/// Every role puts the same lamps out at the build (their count comes from the layout, so a headless server prints the same line); a peer that
/// draws adds a <see cref="RideDarkness"/>, which darkens its own view and each hero's torch by where each is in the span. Nothing goes on the
/// wire (Ruling 19): every peer reads the dark from the plan. Cart lamps, sparks, warning lamps and the chasm's glow are never touched.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>A view or a hero farther than this (flat) from both lanes is not in the dark.</summary>
    const float DarkNear = 8f;
    /// <summary>The span's sample spacing for <see cref="DarknessAt"/>'s first, coarse look (a refine by <see cref="TrackSampler.Measure"/> follows).</summary>
    const float DarkSampleStep = 1f;

    /// <summary>The party S where the floor's dark stretch starts, or NaN without one.</summary>
    public float DarkFromS { get; private set; } = float.NaN;

    /// <summary>The span's lanes sampled every <see cref="DarkSampleStep"/> m: each point with its party S, the enemy lane's beside the party lane's.</summary>
    private Vector3[] _darkParty = System.Array.Empty<Vector3>(), _darkEnemy = System.Array.Empty<Vector3>();
    private float[] _darkS = System.Array.Empty<float>();
    /// <summary>The span's samples' bounds grown by <see cref="DarkNear"/>: a point outside is not in the dark, with no look at the line.</summary>
    private Aabb _darkBounds;

    /// <summary>
    /// The dark stretch (every role): the pocket lamps lit by <see cref="DungeonBuilder"/> (every other pocket) whose pocket lies within
    /// <see cref="DarkRules.LampReach"/> of either lane over the span are put out, light hidden and lantern dark, and a drawing peer gets its
    /// <see cref="RideDarkness"/>. One dark per floor: a second is refused in the log.
    /// </summary>
    private Node3D? BuildDark(Node3D root, Piece piece)
    {
        if (_layout.Rail is not { } rail || !IsInstanceValid(_rails.Party.Track.Track)) return null;
        if (!float.IsNaN(DarkFromS))
        {
            GD.PushError($"[rails] ERROR: dark {piece.Plan.Id}: the floor already has its dark from party S={DarkFromS:0.0}; one dark per floor");
            return null;
        }
        var party = _rails.Party.Track;
        float from = piece.S, to = Mathf.Min(piece.S + DarkRules.Length, party.Length);
        DarkFromS = from;
        SampleDark(from, to);

        var lamps = root.GetNodeOrNull<Node3D>("Rails");
        var dimmed = new System.Collections.Generic.Dictionary<Material, Material>();
        var n = 0;
        for (var i = 0; i < rail.Pockets.Count; i += 2) // the lit ones, as DungeonBuilder lights them
        {
            if (!NearTheDark(_builder.Surface(rail.Pockets[i]), from, to)) continue;
            n++;
            if (lamps?.GetNodeOrNull<Light3D>($"RailLamp{i}") is { } light) light.Visible = false;
            if (lamps?.GetNodeOrNull<Node>($"RailLantern{i}") is { } lantern) DimLantern(lantern, dimmed);
        }
        GD.Print($"[rails] dark {piece.Plan.Id} from party S={from:0.0} to {to:0.0}: {n} pocket lamp(s) out");

        var marker = new Node3D { Name = $"Dark_{piece.Plan.Id}" };
        root.AddChild(marker);
        marker.GlobalPosition = party.Point(from);
        if (!DisplayServer.GetName().Equals("headless", System.StringComparison.OrdinalIgnoreCase)) AddChild(new RideDarkness(this));
        return marker;
    }

    /// <summary>A pocket at <paramref name="pocket"/> lies within <see cref="DarkRules.LampReach"/> (flat, on its level) of either lane over party S [<paramref name="from"/>, <paramref name="to"/>].</summary>
    private bool NearTheDark(Vector3 pocket, float from, float to)
    {
        static bool Near(TrackBody b) => b.Lateral <= DarkRules.LampReach && Mathf.Abs(b.Height) < DungeonBuilder.LevelHeight * 0.75f;
        _rails.Party.Track.Measure(pocket, (from + to) / 2f, (to - from) / 2f, out var pb); // nearest in the span: its end's point when past it
        if (Near(pb)) return true;
        if (!_enemy.Valid) return false;
        float ef = Lanes.EnemyS(from), et = Lanes.EnemyS(to);
        _enemy.Measure(pocket, (ef + et) / 2f, (et - ef) / 2f, out var eb);
        return Near(eb);
    }

    /// <summary>A dead lantern: every emissive material under <paramref name="node"/> swapped for a copy with its emission off (copies shared through <paramref name="dimmed"/>; the source materials are shared by every lantern and stay as they are).</summary>
    private static void DimLantern(Node node, System.Collections.Generic.Dictionary<Material, Material> dimmed)
    {
        Material? Dark(Material? m)
        {
            if (m is not BaseMaterial3D { EmissionEnabled: true } lit) return null;
            if (!dimmed.TryGetValue(lit, out var copy))
            {
                var d = (BaseMaterial3D)lit.Duplicate();
                d.EmissionEnabled = false;
                dimmed[lit] = copy = d;
            }
            return copy;
        }
        if (node is MeshInstance3D mi)
        {
            if (Dark(mi.MaterialOverride) is { } over) mi.MaterialOverride = over;
            else if (mi.MaterialOverride == null && mi.Mesh is { } mesh)
                for (var s = 0; s < mesh.GetSurfaceCount(); s++)
                    if (Dark(mi.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is { } surf) mi.SetSurfaceOverrideMaterial(s, surf);
        }
        foreach (var child in node.GetChildren()) DimLantern(child, dimmed);
    }

    /// <summary>Sample both lanes over party S [<paramref name="from"/>, <paramref name="to"/>] for <see cref="DarknessAt"/>, and bound them.</summary>
    private void SampleDark(float from, float to)
    {
        var party = _rails.Party.Track;
        var count = Mathf.Max(2, Mathf.CeilToInt((to - from) / DarkSampleStep) + 1);
        _darkS = new float[count];
        _darkParty = new Vector3[count];
        _darkEnemy = _enemy.Valid ? new Vector3[count] : System.Array.Empty<Vector3>();
        var bounds = new Aabb(party.Point(from), Vector3.Zero);
        for (var i = 0; i < count; i++)
        {
            var s = Mathf.Min(from + i * DarkSampleStep, to);
            _darkS[i] = s;
            bounds = bounds.Expand(_darkParty[i] = party.Point(s));
            if (_enemy.Valid) bounds = bounds.Expand(_darkEnemy[i] = _enemy.Point(Lanes.EnemyS(s)));
        }
        _darkBounds = bounds.Grow(DarkNear + DarkSampleStep);
    }

    /// <summary>
    /// How dark it is at <paramref name="at"/> (a view or a hero): <see cref="DarkRules.Factor"/> of the party S beside it when it is within
    /// <see cref="DarkNear"/> (flat, on its level) of either lane over the span, measured on the nearer lane by <see cref="TrackSampler.Measure"/>
    /// about the span's nearest sample (an enemy-lane S read back through <see cref="Lanes"/>); else 0. Cheap: a box test outside the span's
    /// bounds, and a short search inside.
    /// </summary>
    public float DarknessAt(Vector3 at)
    {
        if (float.IsNaN(DarkFromS) || !_darkBounds.HasPoint(at) || !IsInstanceValid(_rails.Party.Track.Track)) return 0f;
        var (pi, pd) = NearestSample(_darkParty, at);
        var (ei, ed) = NearestSample(_darkEnemy, at);
        var reach = (DarkNear + DarkSampleStep) * (DarkNear + DarkSampleStep);
        if (pd > reach && ed > reach) return 0f;
        TrackBody b;
        float s;
        if (pd <= ed)
        {
            _rails.Party.Track.Measure(at, _darkS[pi], 2f * DarkSampleStep, out b);
            s = b.S;
        }
        else
        {
            if (!_enemy.Valid) return 0f;
            _enemy.Measure(at, Lanes.EnemyS(_darkS[ei]), 4f * DarkSampleStep, out b); // a bend's outer lane runs up to three times the party's
            s = Lanes.PartyS(b.S);
        }
        if (b.Lateral > DarkNear || Mathf.Abs(b.Height) >= DungeonBuilder.LevelHeight * 0.75f) return 0f;
        return DarkRules.Factor(s, DarkFromS);
    }

    /// <summary>The index of <paramref name="points"/>' nearest (flat, on <paramref name="at"/>'s level) to <paramref name="at"/> and its squared flat distance; +∞ when none is.</summary>
    private static (int Index, float DistanceSquared) NearestSample(Vector3[] points, Vector3 at)
    {
        int best = 0;
        var bestD = float.PositiveInfinity;
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            if (Mathf.Abs(p.Y - at.Y) >= DungeonBuilder.LevelHeight * 0.75f) continue;
            float dx = p.X - at.X, dz = p.Z - at.Z, d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = i; }
        }
        return (best, bestD);
    }
}
