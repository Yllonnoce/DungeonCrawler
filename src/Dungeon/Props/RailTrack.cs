using DungeonCrawler.Dungeon.Rails;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// One mine-cart track: a Path3D through the lane centreline, with two steel rails and sleepers extruded along it, over a
/// gravel ribbon when given a ballast material. Dressing only: no collision (bodies walk the cell floor). Each of the rails,
/// sleepers and ballast is a node of that name holding one mesh per <see cref="ChunkLength"/> of S (Rails0, Rails1, ...), so
/// the stretches out of view are culled; none casts a shadow.
/// </summary>
public partial class RailTrack : Node3D
{
    public const float Gauge = 0.9f;
    public const float Sample = 0.25f;
    public const float SleeperEvery = 0.6f;
    /// <summary>The S each mesh chunk covers: a whole-floor mesh is never out of view, nor out of the torch's shadow.</summary>
    public const float ChunkLength = 20f;
    static readonly Vector3 RailSize = new(0.07f, 0.12f, 0f);
    static readonly Vector3 SleeperSize = new(1.4f, 0.12f, 0.22f);
    /// <summary>The gravel bed under the sleepers: this wide, this far above the floor.</summary>
    public const float BallastWidth = 1.5f, BallastLift = 0.02f;

    public Curve3D Curve { get; } = new() { BakeInterval = 0.2f, UpVectorEnabled = true };
    public float Length { get; private set; }

    private Material? _steel, _sleeper, _ballast;
    private readonly List<(float from, float to)> _gaps = new();
    private readonly List<(float from, float to)> _noBallast = new();

    /// <summary>
    /// The stretches (S from, S to) this track is bare over: those it was last built with and every one cut since that
    /// <see cref="Build"/> (<see cref="Cut"/>, <see cref="CutMore"/>), in the order they came. Since the last build, a gap once
    /// cut stays bare whatever the order of the cuts; a build starts the list again from its own gaps.
    /// </summary>
    public IReadOnlyList<(float from, float to)> Gaps => _gaps;

    /// <summary>The stretches (S from, S to) laid with rails and sleepers but no gravel (<see cref="OmitBallast"/>) since the last <see cref="Build"/>.</summary>
    public IReadOnlyList<(float from, float to)> BallastOmitted => _noBallast;

    /// <summary>
    /// Lay the track along the points; a second call replaces the first (the old path, meshes and gaps are freed). Each of
    /// <paramref name="gaps"/> (S from, S to) is left bare: no rail, no sleeper and no gravel over it (a jump's gap). The curve runs on through them.
    /// With <paramref name="ballast"/> a <see cref="BallastWidth"/> gravel ribbon lies along the same frames, <see cref="BallastLift"/> above the floor.
    /// </summary>
    public void Build(IReadOnlyList<Vector3> points, Material steel, Material sleeper, IReadOnlyList<(float from, float to)>? gaps = null, Material? ballast = null)
    {
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        Curve.ClearPoints();
        foreach (var p in points) Curve.AddPoint(p);
        Length = Curve.GetBakedLength();
        AddChild(new Path3D { Curve = Curve, Name = "Path" });
        _steel = steel;
        _sleeper = sleeper;
        _ballast = ballast;
        _gaps.Clear();
        _noBallast.Clear();
        AddGaps(gaps);
        if (points.Count < 2 || Length < 0.1f) return;
        BuildMeshes(_gaps);
    }

    /// <summary>
    /// Leave <paramref name="gaps"/> bare as well as every gap the track already keeps (<see cref="Gaps"/>), and lay the rails,
    /// sleepers and gravel again on the same curve: the path (and every S along it) is unchanged.
    /// </summary>
    public void Cut(IReadOnlyList<(float from, float to)> gaps)
    {
        AddGaps(gaps);
        if (_steel == null || _sleeper == null || Length < 0.1f) return;
        foreach (var name in new[] { "Rails", "Sleepers", "Ballast" })
            if (GetNodeOrNull(name) is { } old) { RemoveChild(old); old.QueueFree(); }
        BuildMeshes(_gaps);
    }

    /// <summary><see cref="Cut"/> by another name: a live piece's span cut after the floor's own gaps, which stay bare.</summary>
    public void CutMore(IReadOnlyList<(float from, float to)> gaps) => Cut(gaps);

    /// <summary>
    /// Take the gravel off <paramref name="spans"/> (S from, S to) and lay the track again: the rails and sleepers stay, and so do the curve,
    /// every S and <see cref="Gaps"/> (an open trestle's sleepers lie on its stringers, the lava showing between them). Kept through a later
    /// <see cref="Cut"/>; a <see cref="Build"/> starts again with gravel everywhere.
    /// </summary>
    public void OmitBallast(IReadOnlyList<(float from, float to)> spans)
    {
        foreach (var span in spans) if (!_noBallast.Contains(span)) _noBallast.Add(span);
        if (_steel == null || _sleeper == null || Length < 0.1f) return;
        foreach (var name in new[] { "Rails", "Sleepers", "Ballast" })
            if (GetNodeOrNull(name) is { } old) { RemoveChild(old); old.QueueFree(); }
        BuildMeshes(_gaps);
    }

    private void AddGaps(IReadOnlyList<(float from, float to)>? gaps)
    {
        if (gaps == null) return;
        foreach (var g in gaps) if (!_gaps.Contains(g)) _gaps.Add(g);
    }

    /// <summary>
    /// The rails and sleepers of S [<paramref name="from"/>, <paramref name="to"/>] alone, from this track's own baked frames
    /// (the same <see cref="Curve"/>, so they lie where the lane's own rails would), with its <see cref="Gauge"/>, rail and
    /// sleeper sizes, and its sleeper phase (<see cref="SleeperEvery"/>/2 + k·<see cref="SleeperEvery"/> along the whole
    /// track): one rail mesh (<c>{name}_Rails</c>) and one sleeper mesh (<c>{name}_Sleepers</c>) under <paramref name="into"/>,
    /// shadowless; with <paramref name="ballast"/> (and a track built with gravel) its gravel bed too (<c>{name}_Ballast</c>, as the track's
    /// own: <see cref="BallastWidth"/> wide, <see cref="BallastLift"/> up, on the same stations), else no gravel. A live piece lays a cell of
    /// its span with it over a stretch the track was cut bare at.
    /// <para>
    /// The join: the rails ring at the span's exact <paramref name="from"/> and <paramref name="to"/> and on the track's own
    /// <see cref="Sample"/> lattice between (<see cref="TrackStations.Span"/>), and the track itself rings at a gap's exact ends
    /// (<see cref="TrackStations.Rail"/>), so a span laid over a gap [from, to] meets the track's rails at each lip with the
    /// same frame, and a span laid beside another meets it at their shared S. Its normals are generated with a strip of the
    /// lattice past each end, then those strips are dropped, so its end rings shade as the track's inner ones do. It lays the
    /// sleepers of (from, to] (<see cref="TrackStations.SpanSleepers"/>): the gap's own, each once across spans that tile it.
    /// </para>
    /// Not called headless. Both <paramref name="into"/> and this track must be in the tree (the meshes are placed in
    /// <paramref name="into"/>'s frame): add and place the node, then lay; otherwise it is an error and nothing is laid.
    /// </summary>
    public void LaySpan(Node3D into, float from, float to, string name, bool ballast = false)
    {
        if (_steel == null || _sleeper == null || Length < 0.1f) return;
        if (!into.IsInsideTree() || !IsInsideTree())
        {
            GD.PushError($"[rails] ERROR: LaySpan {name} on {Name}: {(into.IsInsideTree() ? "the track" : into.Name.ToString())} is not in the tree; nothing laid");
            return;
        }
        from = Mathf.Clamp(from, 0f, Length);
        to = Mathf.Clamp(to, 0f, Length);
        if (to - from < 1e-3f) return;
        var toInto = into.GlobalTransform.AffineInverse() * GlobalTransform;
        Transform3D Frame(float s) => toInto * Curve.SampleBakedWithRotation(s, true, true);

        // the rails, with one lattice strip of padding past each end for the normals (dropped before the mesh is made)
        var stations = TrackStations.Span(from, to, Sample);
        var padFrom = from - Sample >= 0f ? 1 : 0;
        var padTo = to + Sample <= Length ? 1 : 0;
        if (padFrom == 1) stations.Insert(0, from - Sample);
        if (padTo == 1) stations.Add(to + Sample);
        var frames = stations.Select(Frame).ToList();
        var railSt = new SurfaceTool();
        railSt.Begin(Mesh.PrimitiveType.Triangles);
        var keep = new List<bool>();
        foreach (var side in new[] { -1f, 1f })
        {
            Vector3[]? prev = null;
            for (var k = 0; k < frames.Count; k++)
            {
                var ring = RailRing(frames[k], side);
                var kept = k - 1 >= padFrom && k < frames.Count - padTo; // the strip from station k-1 to k lies in [from, to]
                if (prev != null)
                    for (var i = 0; i < 4; i++) { Quad(railSt, prev[i], prev[(i + 1) % 4], ring[(i + 1) % 4], ring[i]); keep.Add(kept); }
                prev = ring;
            }
        }
        into.AddChild(new MeshInstance3D { Name = $"{name}_Rails", Mesh = CommitKept(railSt, keep, name), MaterialOverride = _steel, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        if (ballast && _ballast != null)
        {
            // the gravel strip over [from, to] on the span's own stations (no padding: a flat strip needs no normals from past its ends)
            var gravelSt = new SurfaceTool();
            gravelSt.Begin(Mesh.PrimitiveType.Triangles);
            Vector3[]? edge = null;
            foreach (var st in TrackStations.Span(from, to, Sample))
            {
                var t = Frame(st);
                var c = t.Origin + t.Basis.Y.Normalized() * BallastLift;
                var x = t.Basis.X.Normalized() * (BallastWidth / 2f);
                var next = new[] { c - x, c + x };
                if (edge != null) Quad(gravelSt, edge[0], next[0], next[1], edge[1]); // its face up (+Y)
                edge = next;
            }
            gravelSt.GenerateNormals();
            into.AddChild(new MeshInstance3D { Name = $"{name}_Ballast", Mesh = gravelSt.Commit(), MaterialOverride = _ballast, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        var sleepers = TrackStations.SpanSleepers(from, to, SleeperEvery, Length);
        if (sleepers.Count == 0) return;
        var sleeperSt = new SurfaceTool();
        sleeperSt.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var s in sleepers)
        {
            var t = Frame(s);
            var b = t.Basis.Orthonormalized();
            AddBox(sleeperSt, b, t.Origin + b.Y * (SleeperSize.Y / 2f), SleeperSize);
        }
        sleeperSt.GenerateNormals();
        into.AddChild(new MeshInstance3D { Name = $"{name}_Sleepers", Mesh = sleeperSt.Commit(), MaterialOverride = _sleeper, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>
    /// The surface tool's quads with normals generated over all of them, as one mesh of only the quads <paramref name="keep"/>
    /// marks (in the order they were added). When the arrays are not six plain vertices a quad, the whole of them, warned.
    /// </summary>
    private ArrayMesh CommitKept(SurfaceTool st, List<bool> keep, string name)
    {
        st.GenerateNormals();
        var arrays = st.CommitToArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var mesh = new ArrayMesh();
        if (verts.Length != keep.Count * 6 || normals.Length != verts.Length)
        {
            GD.PushWarning($"[rails] {Name}/{name}_Rails: {verts.Length} vertices, {normals.Length} normals for {keep.Count} quads; laid whole, padding included");
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
        var kept = keep.Count(k => k) * 6;
        var keptVerts = new Vector3[kept];
        var keptNormals = new Vector3[kept];
        var fill = 0;
        for (var q = 0; q < keep.Count; q++)
        {
            if (!keep[q]) continue;
            Array.Copy(verts, q * 6, keptVerts, fill, 6);
            Array.Copy(normals, q * 6, keptNormals, fill, 6);
            fill += 6;
        }
        var surface = new Godot.Collections.Array();
        surface.Resize((int)Mesh.ArrayType.Max);
        surface[(int)Mesh.ArrayType.Vertex] = keptVerts;
        surface[(int)Mesh.ArrayType.Normal] = keptNormals;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);
        return mesh;
    }

    /// <summary>One rail's cross-section at frame <paramref name="t"/>, on side <paramref name="side"/> (−1 or 1) of the centreline, on top of the sleepers.</summary>
    private static Vector3[] RailRing(Transform3D t, float side)
    {
        var c = t.Origin + t.Basis.X.Normalized() * (side * Gauge / 2f) + t.Basis.Y.Normalized() * (SleeperSize.Y + RailSize.Y / 2f);
        var x = t.Basis.X.Normalized() * (RailSize.X / 2f);
        var y = t.Basis.Y.Normalized() * (RailSize.Y / 2f);
        return new[] { c - x - y, c + x - y, c + x + y, c - x + y };
    }

    private void BuildMeshes(IReadOnlyList<(float from, float to)>? gaps)
    {
        var steel = _steel!;
        var sleeper = _sleeper!;
        var railSt = new SurfaceTool();
        railSt.Begin(Mesh.PrimitiveType.Triangles);
        var railChunks = new List<int>();
        var sleeperSt = new SurfaceTool();
        sleeperSt.Begin(Mesh.PrimitiveType.Triangles);
        var sleeperChunks = new List<int>();

        // the ring stations: the Sample lattice, the length, and each gap's exact ends (TrackStations.Rail)
        var stations = TrackStations.Rail(Length, Sample, gaps);
        var samples = new List<Transform3D>(stations.Count);
        foreach (var s in stations) samples.Add(Curve.SampleBakedWithRotation(s, true, true));

        // Rails: a rectangular tube per side, quad strips between consecutive stations; a strip is in the chunk of its near
        // station's S. A bare station breaks the rail: it ends with a ring at a gap's exact from and starts again at its to.
        foreach (var side in new[] { -1f, 1f })
        {
            Vector3[]? prev = null;
            for (var k = 0; k < samples.Count; k++)
            {
                if (TrackStations.Bare(stations[k], gaps)) { prev = null; continue; }
                var ring = RailRing(samples[k], side);
                if (prev != null)
                    for (var i = 0; i < 4; i++)
                    {
                        var j = (i + 1) % 4;
                        Quad(railSt, prev[i], prev[j], ring[j], ring[i]);
                        railChunks.Add((int)(stations[k - 1] / ChunkLength));
                    }
                prev = ring;
            }
        }

        // Sleepers: each whole in the chunk of its S, at SleeperEvery/2 + k·SleeperEvery (the phase LaySpan keeps).
        for (var k = 0; TrackStations.Sleeper(k, SleeperEvery) < Length; k++)
        {
            var s = TrackStations.Sleeper(k, SleeperEvery);
            if (TrackStations.Bare(s, gaps)) continue;
            var t = Curve.SampleBakedWithRotation(s, true, true);
            var b = t.Basis.Orthonormalized();
            AddBox(sleeperSt, b, t.Origin + b.Y * (SleeperSize.Y / 2f), SleeperSize);
            for (var f = 0; f < 6; f++) sleeperChunks.Add((int)(s / ChunkLength));
        }

        CommitChunks(railSt, railChunks, "Rails", steel);
        CommitChunks(sleeperSt, sleeperChunks, "Sleepers", sleeper);
        if (_ballast == null) return;
        if (_noBallast.Count == 0) { BuildBallast(stations, samples, gaps, _ballast); return; }
        // the gravel breaks at the gaps and at each omitted span's exact ends too: its own stations, so the rails' are unchanged
        var bare = new List<(float from, float to)>(gaps ?? Array.Empty<(float, float)>());
        bare.AddRange(_noBallast);
        var bedStations = TrackStations.Rail(Length, Sample, bare);
        var bedSamples = new List<Transform3D>(bedStations.Count);
        foreach (var s in bedStations) bedSamples.Add(Curve.SampleBakedWithRotation(s, true, true));
        BuildBallast(bedStations, bedSamples, bare, _ballast);
    }

    /// <summary>The gravel ribbon: a flat strip across the stations' frames, broken where the rails are (at a gap's exact ends).</summary>
    private void BuildBallast(List<float> stations, List<Transform3D> samples, IReadOnlyList<(float from, float to)>? gaps, Material ballast)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var chunks = new List<int>();
        Vector3[]? prev = null;
        for (var k = 0; k < samples.Count; k++)
        {
            if (TrackStations.Bare(stations[k], gaps)) { prev = null; continue; }
            var t = samples[k];
            var c = t.Origin + t.Basis.Y.Normalized() * BallastLift;
            var x = t.Basis.X.Normalized() * (BallastWidth / 2f);
            var edge = new[] { c - x, c + x };
            if (prev != null)
            {
                Quad(st, prev[0], edge[0], edge[1], prev[1]); // its face up (+Y)
                chunks.Add((int)(stations[k - 1] / ChunkLength));
            }
            prev = edge;
        }
        if (chunks.Count == 0) return;
        CommitChunks(st, chunks, "Ballast", ballast);
    }

    /// <summary>
    /// The surface tool's quads (two triangles each; <paramref name="chunkOfQuad"/> gives each quad's chunk, in the order they
    /// were added) as a node named <paramref name="name"/> holding one shadowless mesh per chunk. The normals are generated
    /// over the whole track first and then split, so a chunk's edge shades exactly as it did in one mesh: no seam.
    /// </summary>
    private void CommitChunks(SurfaceTool st, List<int> chunkOfQuad, string name, Material material)
    {
        st.GenerateNormals();
        var arrays = st.CommitToArrays();
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var holder = new Node3D { Name = name };
        AddChild(holder);
        if (verts.Length != chunkOfQuad.Count * 6 || normals.Length != verts.Length)
        {
            // not the six plain vertices per quad the split reads (an indexed result, normals missing): the committed arrays
            // as they came, whole, so whatever SurfaceTool made draws as it would have in one mesh
            GD.PushWarning($"[rails] {Name}/{name}: {verts.Length} vertices, {normals.Length} normals for {chunkOfQuad.Count} quads; laid as one mesh");
            var whole = new ArrayMesh();
            whole.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            holder.AddChild(new MeshInstance3D
            {
                Name = $"{name}0", Mesh = whole, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            return;
        }
        var count = 0;
        foreach (var c in chunkOfQuad) count = Math.Max(count, c + 1);
        var sizes = new int[count];
        foreach (var c in chunkOfQuad) sizes[c] += 6;
        var chunkVerts = new Vector3[count][];
        var chunkNormals = new Vector3[count][];
        var fill = new int[count];
        for (var c = 0; c < count; c++) { chunkVerts[c] = new Vector3[sizes[c]]; chunkNormals[c] = new Vector3[sizes[c]]; }
        for (var q = 0; q < chunkOfQuad.Count; q++)
        {
            var c = chunkOfQuad[q];
            Array.Copy(verts, q * 6, chunkVerts[c], fill[c], 6);
            Array.Copy(normals, q * 6, chunkNormals[c], fill[c], 6);
            fill[c] += 6;
        }
        for (var c = 0; c < count; c++)
        {
            if (sizes[c] == 0) continue;
            var surface = new Godot.Collections.Array();
            surface.Resize((int)Mesh.ArrayType.Max);
            surface[(int)Mesh.ArrayType.Vertex] = chunkVerts[c];
            surface[(int)Mesh.ArrayType.Normal] = chunkNormals[c];
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);
            holder.AddChild(new MeshInstance3D
            {
                Name = $"{name}{c}", Mesh = mesh, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    // One clockwise-from-outside winding (Godot's front face): a,b,c and a,c,d. Checked: the rail top comes out with normal +Y.
    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    static void AddBox(SurfaceTool st, Basis basis, Vector3 centre, Vector3 size)
    {
        var h = size / 2f;
        var v = new Vector3[8];
        for (var i = 0; i < 8; i++)
            v[i] = centre + basis * new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
        Quad(st, v[0], v[1], v[3], v[2]); Quad(st, v[4], v[6], v[7], v[5]);
        Quad(st, v[0], v[2], v[6], v[4]); Quad(st, v[1], v[5], v[7], v[3]);
        Quad(st, v[0], v[4], v[5], v[1]); Quad(st, v[2], v[3], v[7], v[6]);
    }
}
