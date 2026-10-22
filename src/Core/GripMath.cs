using System.Numerics;

namespace DungeonCrawler.Core;

/// <summary>
/// Engine-free half of <see cref="WeaponGrip"/>: where a weapon file lives and how to turn a grip's blade direction
/// into a rotation. Kept free of Godot types so the tests can compile it.
/// </summary>
public static class GripMath
{
    public const string ItemsDir = "res://assets/models/kaykit_items/";

    /// <summary>A plain name is a KayKit item (sword_1handed, dagger, axe_1handed…); a name with a slash is any glTF under assets/models.</summary>
    public static string WeaponPath(string name) => name.Contains('/') ? $"res://assets/models/{name}.gltf" : $"{ItemsDir}{name}.gltf";

    /// <summary>
    /// A right-handed orthonormal basis whose Y axis is <paramref name="bladeDir"/>: KayKit weapons stand along +Y with the
    /// hand at the origin, so a weapon given this rotation points its blade the way the grip's end joint does. Roll is
    /// not chosen here; it comes from the grip bone itself, which the viewer's twist turns. No direction (a rig without
    /// an end joint) leaves the blade upright.
    /// </summary>
    public static (Vector3 X, Vector3 Y, Vector3 Z) BasisFor(Vector3 bladeDir)
    {
        var y = bladeDir.LengthSquared() > 1e-12f ? Vector3.Normalize(bladeDir) : Vector3.UnitY;
        var forward = new Vector3(0, 0, -1);
        var helper = MathF.Abs(Vector3.Dot(y, forward)) < 0.9f ? forward : Vector3.UnitX;
        var x = Vector3.Normalize(Vector3.Cross(helper, y));
        var z = Vector3.Normalize(Vector3.Cross(x, y));
        return (x, y, z);
    }
}
