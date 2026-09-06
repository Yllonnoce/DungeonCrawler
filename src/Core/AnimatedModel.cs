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

        var aabb = new Aabb();
        var first = true;
        foreach (var mi in Meshes(root))
        {
            var box = mi.GetAabb().Abs();
            aabb = first ? box : aabb.Merge(box);
            first = false;
        }
        var height = Math.Max(0.5f, aabb.Size.Y);
        var scale = targetHeight / height;
        root.Scale = Vector3.One * scale;
        root.Position = new Vector3(0, -aabb.Position.Y * scale, 0);
        root.RotateY(Mathf.Pi); // glTF forward is +Z; our bodies face -Z

        var model = new AnimatedModel(root, FindPlayer(root));
        foreach (var mi in Meshes(root))
            for (var i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mi.GetActiveMaterial(i)?.Duplicate() is not StandardMaterial3D mat) continue;
                mat.AlbedoColor = Colors.White.Lerp(tint, tintAmount);
                mi.SetSurfaceOverrideMaterial(i, mat);
                model._materials.Add(mat);
            }
        model.Play("Idle");
        return model;
    }

    /// <summary>Play a clip if the model has it; Idle/Walk/Run loop, the rest play once.</summary>
    public void Play(string name, bool force = false)
    {
        if (Player == null || (!force && Current == name) || !Player.HasAnimation(name)) return;
        var loop = name is "Idle" or "Walk" or "Run";
        Player.GetAnimation(name).LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        Player.Play(name, customBlend: 0.15);
        Current = name;
    }

    public bool IsPlayingOneShot => Player != null && Player.IsPlaying() && Current is not ("Idle" or "Walk" or "Run");

    public void Flash(float amount)
    {
        foreach (var m in _materials)
        {
            m.EmissionEnabled = amount > 0f;
            m.Emission = Colors.White * amount;
        }
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
