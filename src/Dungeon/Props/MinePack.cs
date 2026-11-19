using DungeonCrawler.Net;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// loafbrr's CC0 "Mine Object collection" (one glTF of every piece, <see cref="Dungeon.Rails.RideAssets"/>): the carts' tubs and
/// axles, the levers, the bucket and chain, the blockade, the rocks, the timber and the post light that dress the mine cart chase.
/// Visual only: <see cref="Piece"/> hands out a copy of one named piece with no collision, and the callers keep their own
/// collision shapes and their boxes as the fallback when it returns null (the file not imported, the piece not found, or a
/// dedicated server, which draws nothing). The scene is loaded once and one off-tree instance of it is kept to copy from, freed
/// by <see cref="Release"/> (Main's exit) so nothing outlives the tree.
/// </summary>
public static class MinePack
{
    public const string Path = "res://assets/models/loafbrr_mine/MinesPack.gltf";

    private static PackedScene? _scene;
    private static Node3D? _source;
    private static bool _tried;

    /// <summary>
    /// A copy of the piece named <paramref name="nodeName"/>, wrapped in a Node3D whose origin is the piece's bottom centre: its translation
    /// and scale zeroed (its rotation kept only with <paramref name="keepRotation"/>), its children copied only <paramref name="withChildren"/>,
    /// every CollisionObject3D stripped. With <paramref name="fit"/> it is scaled to that size (W × H × D; an axis ≤ 0 is free): with
    /// <paramref name="keepAspect"/> uniformly by the largest of the given axes' ratios, else each given axis to its size (a free axis takes
    /// the largest given ratio). Null when the pack or the piece is missing, or on a dedicated server.
    /// </summary>
    public static Node3D? Piece(string nodeName, Vector3? fit = null, bool keepAspect = true, bool withChildren = true, bool keepRotation = false)
    {
        if (NetSession.IsServer || Source() is not { } source || source.FindChild(nodeName, true, false) is not Node3D found) return null;

        var copy = withChildren ? (Node3D)found.Duplicate() : Alone(found);
        var rotation = keepRotation ? found.Basis.Orthonormalized() : Basis.Identity;
        copy.Transform = Transform3D.Identity;
        Strip(copy);

        // the bounds of every mesh in the copy, in the copy's frame turned by its kept rotation
        var bounds = new Aabb();
        var any = false;
        Bounds(copy, new Transform3D(rotation, Vector3.Zero), ref bounds, ref any);
        if (!any) bounds = new Aabb(Vector3.Zero, Vector3.Zero);

        var scale = Vector3.One;
        if (fit is { } f)
        {
            float Ratio(float want, float have) => want > 0f && have > 1e-4f ? want / have : 0f;
            var r = new Vector3(Ratio(f.X, bounds.Size.X), Ratio(f.Y, bounds.Size.Y), Ratio(f.Z, bounds.Size.Z));
            var largest = Mathf.Max(r.X, Mathf.Max(r.Y, r.Z));
            if (largest > 0f)
                scale = keepAspect ? Vector3.One * largest : new Vector3(r.X > 0f ? r.X : largest, r.Y > 0f ? r.Y : largest, r.Z > 0f ? r.Z : largest);
        }
        var bottom = new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
        copy.Transform = new Transform3D(Basis.FromScale(scale) * rotation, -(scale * bottom));

        var piece = new Node3D { Name = $"Model_{found.Name}" };
        piece.AddChild(copy);
        return piece;
    }

    /// <summary>
    /// Draw every mesh of <paramref name="piece"/> in <paramref name="material"/>: the model's shape in the game's own look (the pack is
    /// stylised: its timber trim sheet is flat orange, its rocks blue crystal). Returns the piece; null stays null.
    /// </summary>
    public static Node3D? Paint(Node3D? piece, Material material)
    {
        if (piece != null) PaintAll(piece, material);
        return piece;
    }

    private static void PaintAll(Node node, Material material)
    {
        if (node is MeshInstance3D mi) mi.MaterialOverride = material;
        foreach (var child in node.GetChildren()) PaintAll(child, material);
    }

    /// <summary>The first mesh in a piece (its own body), for a caller that swaps its material: a bucket that glows while it creaks.</summary>
    public static MeshInstance3D? MeshOf(Node piece)
    {
        if (piece is MeshInstance3D mi) return mi;
        foreach (var child in piece.GetChildren())
            if (MeshOf(child) is { } found) return found;
        return null;
    }

    /// <summary>Free the off-tree instance and let the scene go (Main's exit): no node or resource is kept in a static past the tree.</summary>
    public static void Release()
    {
        if (_source != null && GodotObject.IsInstanceValid(_source)) _source.Free(); // off-tree: QueueFree would never run
        _source = null;
        _scene = null;
        _tried = false;
    }

    private static Node3D? Source()
    {
        if (_source != null && GodotObject.IsInstanceValid(_source)) return _source;
        if (_tried) return null;
        _tried = true;
        if (!ResourceLoader.Exists(Path)) { GD.PushWarning($"[MinePack] {Path} not imported; the boxes stand in"); return null; }
        _scene = GD.Load<PackedScene>(Path);
        _source = _scene?.Instantiate<Node3D>();
        if (_source == null) { _scene = null; return null; }
        Strip(_source);
        Unlit(_source);
        TintEnemyTubs(_source);
        GD.Print($"[MinePack] loaded {Count(_source)} pieces");
        return _source;
    }

    /// <summary>The piece without its children: its own mesh (shared, not copied) and materials.</summary>
    private static Node3D Alone(Node3D found)
    {
        if (found is not MeshInstance3D mi)
        {
            var bare = (Node3D)found.Duplicate();
            foreach (var child in bare.GetChildren()) { bare.RemoveChild(child); child.Free(); }
            return bare;
        }
        var copy = new MeshInstance3D { Name = mi.Name, Mesh = mi.Mesh, MaterialOverride = mi.MaterialOverride, CastShadow = mi.CastShadow };
        for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            if (mi.GetSurfaceOverrideMaterial(i) is { } m) copy.SetSurfaceOverrideMaterial(i, m);
        return copy;
    }

    private static void Strip(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is CollisionObject3D)
            {
                node.RemoveChild(child);
                child.Free();
                continue;
            }
            Strip(child);
        }
    }

    /// <summary>
    /// The cart and rock materials glow by an edge mask (the pack's "bake preview"): white seams in the dark. Off, once, on the loaded
    /// materials themselves (every copy shares them).
    /// </summary>
    private static void Unlit(Node node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh })
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
                if (mesh.SurfaceGetMaterial(i) is BaseMaterial3D { EmissionEnabled: true } m) m.EmissionEnabled = false;
        foreach (var child in node.GetChildren()) Unlit(child);
    }

    /// <summary>
    /// The enemy carts' tubs (Cart_1 the runaway wagon's, Cart_2 the slag cart's) are the pack's bright cartoon red: toned to a dark rusted
    /// oxblood by one albedo multiply, about half the value and the saturation roughly halved, still red so an enemy cart reads apart from
    /// the party's iron. The texture under these tubs is red throughout (no grey trim to shift). Multiplied by its albedo colour.
    /// </summary>
    public static readonly Color EnemyTubTint = new(0.5f, 0.95f, 1.05f);
    static readonly string[] EnemyTubs = { "Cart_1", "Cart_2" };

    /// <summary>
    /// The tint goes on a copy of the shared imported material, set as a surface override on the two tub pieces of the off-tree source (the
    /// copies <see cref="Piece"/> hands out keep it): the axles and frame share the original and keep it. Nothing static holds the copy; it
    /// goes with the source at <see cref="Release"/>.
    /// </summary>
    private static void TintEnemyTubs(Node source)
    {
        BaseMaterial3D? from = null, tinted = null;
        foreach (var name in EnemyTubs)
        {
            if (source.FindChild(name, true, false) is not MeshInstance3D { Mesh: { } mesh } mi) continue;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                if ((mi.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i)) is not BaseMaterial3D m) continue;
                if (m != from)
                {
                    from = m;
                    tinted = (BaseMaterial3D)m.Duplicate();
                    tinted.AlbedoColor = m.AlbedoColor * EnemyTubTint;
                }
                mi.SetSurfaceOverrideMaterial(i, tinted);
            }
        }
    }

    private static void Bounds(Node node, Transform3D toPiece, ref Aabb bounds, ref bool any)
    {
        if (node is MeshInstance3D { Mesh: not null } mi)
        {
            var box = toPiece * mi.GetAabb();
            bounds = any ? bounds.Merge(box) : box;
            any = true;
        }
        foreach (var child in node.GetChildren())
            if (child is Node3D n) Bounds(n, toPiece * n.Transform, ref bounds, ref any);
    }

    private static int Count(Node node)
    {
        var n = node is MeshInstance3D ? 1 : 0;
        foreach (var child in node.GetChildren()) n += Count(child);
        return n;
    }
}
