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
    const int SamplesPerChunk = (int)(ChunkLength / Sample);
    static readonly Vector3 RailSize = new(0.07f, 0.12f, 0f);
    static readonly Vector3 SleeperSize = new(1.4f, 0.12f, 0.22f);
    /// <summary>The gravel bed under the sleepers: this wide, this far above the floor.</summary>
    public const float BallastWidth = 1.5f, BallastLift = 0.02f;

    public Curve3D Curve { get; } = new() { BakeInterval = 0.2f, UpVectorEnabled = true };
    public float Length { get; private set; }

    private Material? _steel, _sleeper, _ballast;

    /// <summary>
    /// Lay the track along the points; a second call replaces the first (the old path and meshes are freed). Each of
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
        if (points.Count < 2 || Length < 0.1f) return;
        BuildMeshes(gaps);
    }

    /// <summary>Lay the rails and sleepers again on the same curve, bare over <paramref name="gaps"/>: the path (and every S along it) is unchanged.</summary>
    public void Cut(IReadOnlyList<(float from, float to)> gaps)
    {
        if (_steel == null || _sleeper == null || Length < 0.1f) return;
        foreach (var name in new[] { "Rails", "Sleepers", "Ballast" })
            if (GetNodeOrNull(name) is { } old) { RemoveChild(old); old.QueueFree(); }
        BuildMeshes(gaps);
    }

    private static bool InGap(float s, IReadOnlyList<(float from, float to)>? gaps)
    {
        if (gaps == null) return false;
        foreach (var (from, to) in gaps) if (s > from && s < to) return true;
        return false;
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

        var samples = new List<Transform3D>();
        for (var s = 0f; s < Length; s += Sample) samples.Add(Curve.SampleBakedWithRotation(s, true, true));
        samples.Add(Curve.SampleBakedWithRotation(Length, true, true));

        // Rails: a rectangular tube per side, quad strips between consecutive samples; the strip from sample k-1 to k is in
        // chunk (k-1) / SamplesPerChunk.
        foreach (var side in new[] { -1f, 1f })
        {
            Vector3[]? prev = null;
            for (var k = 0; k < samples.Count; k++)
            {
                var t = samples[k];
                if (InGap(k * Sample, gaps)) { prev = null; continue; } // a bare stretch: the rail ends at its lip and starts again past it
                var c = t.Origin + t.Basis.X.Normalized() * (side * Gauge / 2f) + t.Basis.Y.Normalized() * (SleeperSize.Y + RailSize.Y / 2f);
                var x = t.Basis.X.Normalized() * (RailSize.X / 2f);
                var y = t.Basis.Y.Normalized() * (RailSize.Y / 2f);
                var ring = new[] { c - x - y, c + x - y, c + x + y, c - x + y };
                if (prev != null)
                    for (var i = 0; i < 4; i++)
                    {
                        var j = (i + 1) % 4;
                        Quad(railSt, prev[i], prev[j], ring[j], ring[i]);
                        railChunks.Add((k - 1) / SamplesPerChunk);
                    }
                prev = ring;
            }
        }

        // Sleepers: each whole in the chunk of its S.
        for (var s = SleeperEvery / 2f; s < Length; s += SleeperEvery)
        {
            if (InGap(s, gaps)) continue;
            var t = Curve.SampleBakedWithRotation(s, true, true);
            var b = t.Basis.Orthonormalized();
            AddBox(sleeperSt, b, t.Origin + b.Y * (SleeperSize.Y / 2f), SleeperSize);
            for (var f = 0; f < 6; f++) sleeperChunks.Add((int)(s / ChunkLength));
        }

        CommitChunks(railSt, railChunks, "Rails", steel);
        CommitChunks(sleeperSt, sleeperChunks, "Sleepers", sleeper);
        if (_ballast != null) BuildBallast(samples, gaps, _ballast);
    }

    /// <summary>The gravel ribbon: a flat strip across the samples' frames, broken where the rails are.</summary>
    private void BuildBallast(List<Transform3D> samples, IReadOnlyList<(float from, float to)>? gaps, Material ballast)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var chunks = new List<int>();
        Vector3[]? prev = null;
        for (var k = 0; k < samples.Count; k++)
        {
            if (InGap(k * Sample, gaps)) { prev = null; continue; }
            var t = samples[k];
            var c = t.Origin + t.Basis.Y.Normalized() * BallastLift;
            var x = t.Basis.X.Normalized() * (BallastWidth / 2f);
            var edge = new[] { c - x, c + x };
            if (prev != null)
            {
                Quad(st, prev[0], edge[0], edge[1], prev[1]); // its face up (+Y)
                chunks.Add((k - 1) / SamplesPerChunk);
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
            GD.PushWarning($"[rails] {Name}/{name}: {verts.Length} vertices for {chunkOfQuad.Count} quads; laid as one mesh");
            chunkOfQuad = Enumerable.Repeat(0, verts.Length / 6).ToList();
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
