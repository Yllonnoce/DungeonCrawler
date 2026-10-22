using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// Hangs one of the game's weapons from a character's grip bone. A character rigged in the 3dObject viewer carries
/// <c>LeftHandGrip</c> / <c>RightHandGrip</c> bones inside each fist, each with a <c>_End</c> child that says which
/// way the blade runs; they never move the skin, so the weapon can be swapped without touching the character.
/// </summary>
public static class WeaponGrip
{
    /// <summary>
    /// Attach the weapon named <paramref name="weapon"/> (see <see cref="GripMath.WeaponPath"/>) to the grip of
    /// <paramref name="hand"/> ("Right" or "Left") under <paramref name="modelRoot"/>. The weapon keeps its native size
    /// times <paramref name="scale"/> whatever the character was scaled to. Null when the model has no such grip or the
    /// weapon file is missing.
    /// </summary>
    public static Node3D? Attach(Node3D modelRoot, string weapon, string hand = "Right", float scale = 1f)
    {
        var skeleton = Find<Skeleton3D>(modelRoot);
        if (skeleton == null) return null;
        var grip = skeleton.FindBone($"{hand}HandGrip");
        var end = skeleton.FindBone($"{hand}HandGrip_End");
        if (grip < 0) return null;
        var path = GripMath.WeaponPath(weapon);
        if (!ResourceLoader.Exists(path)) return null;
        var mount = new BoneAttachment3D { Name = $"{hand}Weapon", BoneName = $"{hand}HandGrip" };
        skeleton.AddChild(mount);
        var node = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        node.Name = "Weapon";
        mount.AddChild(node);
        // the end joint's rest offset is expressed in the grip bone's own space (the viewer exports bones with
        // position-only rests), so it is the blade direction as seen from the grip
        var dir = end >= 0 ? skeleton.GetBoneRest(end).Origin : Vector3.Zero;
        var (x, y, z) = GripMath.BasisFor(new System.Numerics.Vector3(dir.X, dir.Y, dir.Z));
        node.Basis = new Basis(new Vector3(x.X, x.Y, x.Z), new Vector3(y.X, y.Y, y.Z), new Vector3(z.X, z.Y, z.Z));
        // the character's height scaling sits on the nodes above the skeleton; undo it so the weapon keeps its native size
        var inherited = 1f;
        for (Node? n = skeleton; n != null; n = n.GetParent())
        {
            if (n is Node3D n3) inherited *= n3.Scale.Y;
            if (n == modelRoot) break;
        }
        node.Scale = Vector3.One * (scale / Mathf.Max(1e-4f, inherited));
        return node;
    }

    private static T? Find<T>(Node n) where T : Node
    {
        if (n is T hit) return hit;
        foreach (var c in n.GetChildren()) if (Find<T>(c) is { } found) return found;
        return null;
    }
}
