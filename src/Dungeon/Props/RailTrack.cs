using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// One mine-cart track: a Path3D through the lane centreline, with two steel rails and sleepers extruded along it.
/// Dressing only: no collision (bodies walk the cell floor).
/// </summary>
public partial class RailTrack : Node3D
{
    public const float Gauge = 0.9f;
    public const float Sample = 0.25f;
    public const float SleeperEvery = 0.6f;
    static readonly Vector3 RailSize = new(0.07f, 0.12f, 0f);
    static readonly Vector3 SleeperSize = new(1.4f, 0.12f, 0.22f);

    public Curve3D Curve { get; } = new() { BakeInterval = 0.2f, UpVectorEnabled = true };
    public float Length { get; private set; }

    public void Build(IReadOnlyList<Vector3> points, Material steel, Material sleeper)
    {
        foreach (var p in points) Curve.AddPoint(p);
        Length = Curve.GetBakedLength();
        AddChild(new Path3D { Curve = Curve, Name = "Path" });
        if (points.Count < 2 || Length < 0.1f) return;

        var railSt = new SurfaceTool();
        railSt.Begin(Mesh.PrimitiveType.Triangles);
        var sleeperSt = new SurfaceTool();
        sleeperSt.Begin(Mesh.PrimitiveType.Triangles);

        var samples = new List<Transform3D>();
        for (var s = 0f; s < Length; s += Sample) samples.Add(Curve.SampleBakedWithRotation(s, true, true));
        samples.Add(Curve.SampleBakedWithRotation(Length, true, true));

        // Rails: a rectangular tube per side, quad strips between consecutive samples.
        foreach (var side in new[] { -1f, 1f })
        {
            Vector3[]? prev = null;
            foreach (var t in samples)
            {
                var c = t.Origin + t.Basis.X.Normalized() * (side * Gauge / 2f) + t.Basis.Y.Normalized() * (SleeperSize.Y + RailSize.Y / 2f);
                var x = t.Basis.X.Normalized() * (RailSize.X / 2f);
                var y = t.Basis.Y.Normalized() * (RailSize.Y / 2f);
                var ring = new[] { c - x - y, c + x - y, c + x + y, c - x + y };
                if (prev != null)
                    for (var i = 0; i < 4; i++)
                    {
                        var j = (i + 1) % 4;
                        Quad(railSt, prev[i], prev[j], ring[j], ring[i]);
                    }
                prev = ring;
            }
        }

        // Sleepers.
        for (var s = SleeperEvery / 2f; s < Length; s += SleeperEvery)
        {
            var t = Curve.SampleBakedWithRotation(s, true, true);
            var b = t.Basis.Orthonormalized();
            AddBox(sleeperSt, b, t.Origin + b.Y * (SleeperSize.Y / 2f), SleeperSize);
        }

        railSt.GenerateNormals();
        sleeperSt.GenerateNormals();
        AddChild(new MeshInstance3D { Name = "Rails", Mesh = railSt.Commit(), MaterialOverride = steel });
        AddChild(new MeshInstance3D { Name = "Sleepers", Mesh = sleeperSt.Commit(), MaterialOverride = sleeper });
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
