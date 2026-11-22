using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 4's collapsing bridge (plan 13, Rulings 2-7): the cavern's party-lane deck cells, each with its own rails and sleepers. The party
/// track is cut bare over the whole span (<see cref="RailTrack.CutMore"/>, by the builder) and each cell's are laid here by
/// <see cref="RailTrack.LaySpan"/> of that same track, so they lie where the lane's own would and meet the track at both lips. The deck
/// itself is the cavern's (<see cref="ChasmDressing"/>): <see cref="GiveWay"/> hides a cell's rails, sends its deck and footing with
/// <see cref="ChasmDressing.SetGone"/>, tumbles a few timbers and plays <c>timber_collapse</c> every <see cref="BridgeRules.CueEveryCells"/>
/// cells; <see cref="Stand"/> puts every cell back. Headless no mesh is built, but the cells are kept (the footing goes on every role). The
/// <see cref="RailEncounterController"/> runs the collapse on the server and in single player; it tells "gone {n}" and "stands"
/// (<see cref="NetRef"/>), which a client's copy shows. The node stays at the origin.
/// </summary>
public partial class CavernBridge : Node3D, ISoundPoint
{
    /// <summary>The floor whose cavern decks give way (<see cref="FloorLayout.GoneBridges"/>).</summary>
    public FloorLayout Layout { get; init; } = null!;
    /// <summary>Its wire name ("bridge:{piece}"), set by the builder; empty: not told.</summary>
    public string NetRef { get; init; } = "";

    /// <summary>The cells (in line order) gone now: always the first <see cref="Gone"/> of them.</summary>
    public int Gone { get; private set; }
    /// <summary>Its deck cells.</summary>
    public int Count => _cells.Count;
    /// <summary>The party S of its start (the first cell's near edge) and end (the last cell's far edge); NaN before <see cref="Build"/>.</summary>
    public float From { get; private set; } = float.NaN;
    public float To { get; private set; } = float.NaN;
    /// <summary>The far edge of each cell, in line order (<see cref="BridgeRules.GoneCells"/> reads them).</summary>
    public IReadOnlyList<float> FarS => _far;
    /// <summary>Its cells: the grid cell and its party S span, in line order.</summary>
    public IReadOnlyList<(GridPos Cell, float FromS, float ToS)> Cells => _cells;
    /// <summary>The party S the gone cells reach to (the last gone cell's far edge), or <see cref="From"/> while none is gone.</summary>
    public float GoneToS => Gone > 0 ? _far[Gone - 1] : From;

    /// <summary>Where it groans and falls from, on every role: the line at its middle.</summary>
    public Vector3 SoundPoint { get; private set; }
    /// <summary>Where the cart's front comes onto it.</summary>
    public Vector3 Entry { get; private set; }

    private readonly List<(GridPos Cell, float FromS, float ToS)> _cells = new();
    private readonly List<float> _far = new();
    private readonly List<Transform3D> _middles = new();
    /// <summary>Each cell's rails and sleepers (null headless).</summary>
    private readonly List<Node3D?> _looks = new();
    private readonly StandardMaterial3D _wood = new() { AlbedoColor = new Color(0.3f, 0.2f, 0.11f), Roughness = 0.95f };
    private readonly StandardMaterial3D _dark = new() { AlbedoColor = new Color(0.17f, 0.11f, 0.06f), Roughness = 1f };

    /// <summary>
    /// Lay each cell's rails and sleepers along <paramref name="track"/> (the party lane's: its <see cref="TrackSampler.Track"/> cut bare
    /// over the span), one mesh pair per cell under its own node <c>BridgeCell_{x}_{y}</c>; headless none, but the cells are kept.
    /// Add this node to the tree first (<see cref="RailTrack.LaySpan"/> places in its frame).
    /// </summary>
    public void Build(TrackSampler track, IReadOnlyList<(GridPos Cell, float FromS, float ToS)> cells)
    {
        _cells.Clear(); _far.Clear(); _middles.Clear(); _looks.Clear();
        Gone = 0;
        if (cells.Count == 0) return;
        From = cells[0].FromS;
        To = cells[^1].ToS;
        SoundPoint = track.Point((From + To) / 2f);
        Entry = track.Point(From);
        foreach (var c in cells)
        {
            _cells.Add(c);
            _far.Add(c.ToS);
            _middles.Add(track.Frame((c.FromS + c.ToS) / 2f).Orthonormalized());
            Node3D? look = null;
            if (ChasmDressing.DrawsLook)
            {
                look = new Node3D { Name = $"BridgeCell_{c.Cell.X}_{c.Cell.Y}" };
                AddChild(look);
                track.Track.LaySpan(look, c.FromS, c.ToS, "Deck");
            }
            _looks.Add(look);
        }
    }

    /// <summary>
    /// Cells [<see cref="Gone"/>, <paramref name="upTo"/>) give way: their rails hidden, their decks and footings gone
    /// (<see cref="ChasmDressing.SetGone"/>). With a <paramref name="debrisRoot"/> (the live collapse, not a catch-up) a few timbers of
    /// each tumble under it and <c>timber_collapse</c> plays every <see cref="BridgeRules.CueEveryCells"/> cells. The server tells "gone {n}".
    /// </summary>
    public void GiveWay(int upTo, Node3D? debrisRoot)
    {
        upTo = Math.Min(upTo, _cells.Count);
        if (upTo <= Gone) return;
        var dressing = ChasmDressing.Instance;
        for (var k = Gone; k < upTo; k++)
        {
            if (_looks[k] is { } look && IsInstanceValid(look)) look.Visible = false;
            if (dressing != null && IsInstanceValid(dressing)) dressing.SetGone(Layout, _cells[k].Cell, true);
            if (debrisRoot == null) continue;
            if (ChasmDressing.DrawsLook) Tumble(debrisRoot, k);
            if (k % BridgeRules.CueEveryCells == 0) RideAudio.At("timber_collapse", _middles[k].Origin);
        }
        Gone = upTo;
        Tell($"gone {Gone}");
    }

    /// <summary>Every cell stands again: its rails shown, its deck and footing back. The server tells "stands" when cells were gone.</summary>
    public void Stand()
    {
        var was = Gone;
        var dressing = ChasmDressing.Instance;
        for (var k = 0; k < _cells.Count; k++)
        {
            if (_looks[k] is { } look && IsInstanceValid(look)) look.Visible = true;
            if ((k < was || Layout.GoneBridges.Contains(_cells[k].Cell)) && dressing != null && IsInstanceValid(dressing)) dressing.SetGone(Layout, _cells[k].Cell, false);
        }
        Gone = 0;
        if (was > 0) Tell("stands");
    }

    /// <summary>Client: what the server told ("gone {n}", "stands"), shown here with the collapse's timbers under <paramref name="root"/>.</summary>
    public bool ShowTold(string what, Node3D root)
    {
        if (what == "stands") { Stand(); return true; }
        if (what.StartsWith("gone ", StringComparison.Ordinal) && int.TryParse(what.AsSpan(5), out var n))
        {
            if (n < Gone) Stand();
            GiveWay(n, root);
            return true;
        }
        return false;
    }

    private void Tell(string what)
    {
        if (NetRef.Length > 0 && !NetSession.IsClient) EventBus.Instance?.RaisePropChanged(NetRef, what);
    }

    /// <summary>A plank across the deck and a sleeper of cell <paramref name="k"/> fall away under <paramref name="root"/> (cosmetic).</summary>
    private void Tumble(Node3D root, int k)
    {
        var parts = new List<(Vector3, Transform3D, Material)>
        {
            (new Vector3(2.1f, 0.06f, 0.92f), new Transform3D(Basis.Identity, new Vector3(0f, -0.05f, -1f)), _dark),
            (new Vector3(2.1f, 0.06f, 0.92f), new Transform3D(Basis.Identity, new Vector3(0f, -0.05f, 0f)), _dark),
            (new Vector3(1.4f, 0.12f, 0.22f), new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 1f)), _wood),
            (new Vector3(1.4f, 0.12f, 0.22f), new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 0.4f)), _wood),
        };
        TimberDebris.Tumble(root, _middles[k], parts, Vector3.Down * 4f, "BridgeTimber"); // down, not on ahead: the deck drops away
    }

    /// <summary>
    /// The join at both lips (test hook, every role): the largest distance (mm) from a vertex of the first cell's first rail ring at
    /// <see cref="From"/>, and of the last cell's last ring at <see cref="To"/>, to the nearest vertex of <paramref name="track"/>'s own rail
    /// meshes. Headless the end cells are laid into a probe node for the measurement and freed after; NaN when nothing can be measured.
    /// </summary>
    internal (float FromMm, float ToMm) MeasureJoin(RailTrack track)
    {
        if (_cells.Count == 0 || track.GetNodeOrNull<Node3D>("Rails") is not { } rails) return (float.NaN, float.NaN);
        var probe = new Node3D { Name = "JoinProbe" };
        AddChild(probe);
        try
        {
            var first = SpanRails(track, probe, 0, "JoinFrom");
            var last = SpanRails(track, probe, _cells.Count - 1, "JoinTo");
            var trackVerts = new List<Vector3>();
            foreach (var child in rails.GetChildren())
                if (child is MeshInstance3D { Mesh: { } mesh } mi)
                    for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                        foreach (var v in mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) trackVerts.Add(mi.GlobalTransform * v);
            return (RingGap(track, first, From, trackVerts), RingGap(track, last, To, trackVerts));
        }
        finally
        {
            RemoveChild(probe);
            probe.Free();
        }
    }

    /// <summary>Cell <paramref name="k"/>'s rail vertices in global space: its laid mesh, or one laid into <paramref name="probe"/> now.</summary>
    private List<Vector3> SpanRails(RailTrack track, Node3D probe, int k, string name)
    {
        var holder = _looks[k];
        MeshInstance3D? mi = holder?.GetNodeOrNull<MeshInstance3D>("Deck_Rails");
        if (mi == null)
        {
            track.LaySpan(probe, _cells[k].FromS, _cells[k].ToS, name);
            mi = probe.GetNodeOrNull<MeshInstance3D>($"{name}_Rails");
        }
        var verts = new List<Vector3>();
        if (mi?.Mesh is not { } mesh) return verts;
        for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            foreach (var v in mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array()) verts.Add(mi.GlobalTransform * v);
        return verts;
    }

    /// <summary>The largest distance (mm) from a span vertex of the ring at <paramref name="s"/> (within 1 cm of its plane) to the nearest track vertex; NaN with none.</summary>
    private static float RingGap(RailTrack track, List<Vector3> span, float s, List<Vector3> trackVerts)
    {
        var frame = track.GlobalTransform * track.Curve.SampleBakedWithRotation(s, true, true);
        var toFrame = frame.AffineInverse();
        var worst = float.NaN;
        foreach (var v in span)
        {
            if (Mathf.Abs((toFrame * v).Z) > 0.01f) continue; // not the ring at s
            var best = float.MaxValue;
            foreach (var t in trackVerts)
            {
                var d = t.DistanceSquaredTo(v);
                if (d < best) best = d;
            }
            var mm = Mathf.Sqrt(best) * 1000f;
            worst = float.IsNaN(worst) ? mm : Mathf.Max(worst, mm);
        }
        return worst;
    }
}
