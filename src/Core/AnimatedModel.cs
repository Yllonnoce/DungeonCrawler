using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// A glTF character model attached under a body: scaled to a target height, tinted, with looping/one-shot clip playback.
/// Used by adventurers (the enemy class has its own older copy of this logic).
/// </summary>
public sealed class AnimatedModel
{
    public Node3D Root { get; }
    public AnimationPlayer? Player { get; }
    public string Current { get; private set; } = "";
    private readonly List<StandardMaterial3D> _materials = new();

    private AnimatedModel(Node3D root, AnimationPlayer? player) { Root = root; Player = player; }

    public static AnimatedModel? Attach(Node3D parent, string path, float targetHeight, Color tint, float tintAmount = 0.2f)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var root = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        root.Name = "Model";
        parent.AddChild(root);

        // Pose the rig first: a model whose bind pose lies flat (a character exported from the 3dObject viewer) only
        // stands up once Idle is applied, and the height must be measured standing.
        var model = new AnimatedModel(root, FindPlayer(root));
        model.Play("Idle");
        model.Player?.Seek(0, update: true);

        var aabb = new Aabb();
        var first = true;
        foreach (var mi in Meshes(root))
        {
            var box = (SkinnedBox(mi) ?? mi.GetAabb()).Abs();
            aabb = first ? box : aabb.Merge(box);
            first = false;
        }
        var height = Math.Max(0.5f, aabb.Size.Y);
        var scale = targetHeight / height;
        GD.Print($"[AnimatedModel] {path}: box {aabb.Position:0.000} size {aabb.Size:0.000} height {height:0.000} scale {scale:0.00} skinned {Meshes(root).Count(m => SkinnedBox(m) != null)}/{Meshes(root).Count()}");
        root.Scale = Vector3.One * scale;
        root.Position = new Vector3(0, -aabb.Position.Y * scale, 0);
        root.RotateY(Mathf.Pi); // glTF forward is +Z; our bodies face -Z

        foreach (var mi in Meshes(root))
            for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mi.GetActiveMaterial(i)?.Duplicate() is not StandardMaterial3D mat) continue;
                mat.AlbedoColor = Colors.White.Lerp(tint, tintAmount);
                mi.SetSurfaceOverrideMaterial(i, mat);
                model._materials.Add(mat);
            }
        return model;
    }

    /// <summary>True while the current clip was started as a loop (the gaits, or a held pose such as an NPC's Talk).</summary>
    public bool Looping { get; private set; }

    /// <summary>
    /// Play a clip if the model has it; Idle/Walk/Run loop, the rest play once unless <paramref name="loop"/> says
    /// otherwise. <paramref name="speed"/> is the player's speed scale for the clip (the gait code rescales gaits itself).
    /// </summary>
    public void Play(string name, bool force = false, bool? loop = null, float speed = 1f)
    {
        if (Player == null || !Player.HasAnimation(name)) return;
        if (!force && Current == name) { if (Looping) Player.SpeedScale = speed; return; }
        var loops = loop ?? name is "Idle" or "Walk" or "Run";
        Player.GetAnimation(name).LoopMode = loops ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        Player.SpeedScale = speed;
        Player.Play(name, customBlend: 0.15);
        Current = name;
        Looping = loops;
    }

    public bool IsPlayingOneShot => Player != null && Player.IsPlaying() && !Looping;

    public void Flash(float amount)
    {
        foreach (var m in _materials)
        {
            m.EmissionEnabled = amount > 0f;
            m.Emission = Colors.White * amount;
        }
    }

    /// <summary>
    /// The bounds of a skinned mesh in its current pose, in the mesh's own space: every vertex run through its bone
    /// weights. MeshInstance3D.GetAabb only knows the bind pose, which need not be upright. Null when the mesh is not skinned.
    /// </summary>
    public static Aabb? SkinnedBox(MeshInstance3D mi)
    {
        if (mi.Mesh == null || mi.Skin is not { } skin || mi.Skeleton.IsEmpty || mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton) is not { } skel) return null;
        skel.ForceUpdateAllBoneTransforms();
        var binds = new Transform3D[skin.GetBindCount()];
        for (var b = 0; b < binds.Length; b++)
        {
            var bone = skin.GetBindBone(b);
            if (bone < 0) bone = skel.FindBone(skin.GetBindName(b));
            if (bone < 0) return null;
            binds[b] = skel.GetBoneGlobalPose(bone) * skin.GetBindPose(b);
        }
        var toMesh = Relative(skel, mi).AffineInverse(); // skeleton space -> mesh space
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        var any = false;
        for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = mi.Mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bonesV = arrays[(int)Mesh.ArrayType.Bones];
            var bones = bonesV.VariantType == Variant.Type.PackedFloat32Array ? Array.ConvertAll(bonesV.AsFloat32Array(), f => (int)f) : bonesV.AsInt32Array();
            var weightsV = arrays[(int)Mesh.ArrayType.Weights];
            var weights = weightsV.VariantType == Variant.Type.PackedFloat64Array ? Array.ConvertAll(weightsV.AsFloat64Array(), d => (float)d) : weightsV.AsFloat32Array();
            if (verts.Length == 0 || bones.Length < verts.Length || weights.Length != bones.Length) continue;
            var per = bones.Length / verts.Length;
            for (var i = 0; i < verts.Length; i++)
            {
                var p = Vector3.Zero;
                for (var k = 0; k < per; k++)
                {
                    var w = weights[i * per + k];
                    if (w == 0f) continue;
                    var b = bones[i * per + k];
                    if ((uint)b >= (uint)binds.Length) continue;
                    p += binds[b] * verts[i] * w;
                }
                p = toMesh * p;
                lo = lo.Min(p);
                hi = hi.Max(p);
                any = true;
            }
        }
        return any ? new Aabb(lo, hi - lo) : null;
    }

    /// <summary>The transform of <paramref name="node"/> relative to <paramref name="ancestor"/> (identity when they are the same node).</summary>
    private static Transform3D Relative(Node3D ancestor, Node3D node)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != null && n != ancestor; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }

    public static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in Meshes(c)) yield return m;
    }

    private static AnimationPlayer? FindPlayer(Node n)
    {
        if (n is AnimationPlayer ap) return ap;
        foreach (var c in n.GetChildren()) if (FindPlayer(c) is { } found) return found;
        return null;
    }
}
