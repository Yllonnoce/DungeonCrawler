namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The mine cart chase's CC0 models and textures, every file of them (each named in <c>assets/LICENSES.md</c>): loafbrr's
/// "Mine Object collection" glTF (with the textures Godot's import extracts beside it) that <c>MinePack</c> dresses the
/// carts and props from, and the Poly Haven and ambientCG texture sets the carts, the track and the lava take. Engine-free.
/// </summary>
public static class RideAssets
{
    public const string ModelDir = "res://assets/models/loafbrr_mine/";
    public const string TextureDir = "res://assets/textures/";

    public static readonly IReadOnlyList<string> Models = new[] { ModelDir + "MinesPack.gltf", ModelDir + "README.txt" };

    /// <summary>The images embedded in the glTF, as the import extracts them (MinesPack_&lt;image&gt;.png).</summary>
    static readonly string[] PackImages =
    {
        "Cart_1k_Base Color", "Cart_1k_EdgeMask", "Cart_1k_METALLIC-Cart_1k_ROUGHNESS", "Cart_1k_NORMAL",
        "Contraptions_1k_Base Color", "Contraptions_1k_METALLIC-Contraptions_1k_ROUGHNESS", "Contraptions_1k_NORMAL",
        "Jars_1k_Base Color", "Jars_1k_METALLIC-Jars_1k_ROUGHNESS", "Jars_1k_NORMAL",
        "Pillars_1k_Base Color", "Pillars_1k_METALLIC-Pillars_1k_ROUGHNESS", "Pillars_1k_NORMAL",
        "Pipes_1k_Base Color", "Pipes_1k_METALLIC-Pipes_1k_ROUGHNESS", "Pipes_1k_NORMAL",
        "Rails_albedo", "Rails_metalness-Rails_roughness", "Rails_normal",
        "Rocks_1k_Base Color", "Rocks_1k_EdgeMask", "Rocks_1k_NORMAL", "Rocks_1k_ROUGHNESS",
        "Tools_1k_Base Color", "Tools_1k_METALLIC-Tools_1k_ROUGHNESS", "Tools_1k_NORMAL",
    };

    /// <summary>The texture sets, named as <c>DungeonBuilder.Material</c> loads them; lava004 also has an emission map.</summary>
    static readonly (string Id, string[] Maps)[] Sets =
    {
        ("gravel_floor_02", new[] { "diff", "nor", "rough" }),
        ("old_planks_02", new[] { "diff", "nor", "rough" }),
        ("lava004", new[] { "diff", "nor", "rough", "emit" }),
    };

    public static readonly IReadOnlyList<string> Textures =
        PackImages.Select(i => $"{ModelDir}MinesPack_{i}.png")
            .Concat(Sets.SelectMany(s => s.Maps.Select(m => $"{TextureDir}{s.Id}/{s.Id}_{m}_1k.jpg")))
            .ToArray();
}
