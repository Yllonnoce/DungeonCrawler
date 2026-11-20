using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>
/// The creatures' animated models, kept loaded for the session: a model is read from disk the first time a creature of its
/// kind spawns (or when <see cref="Warm"/> asks for it on a loader thread) and never again, where before the scene was let go
/// with the last creature wearing it and read again (3 to 10 ms on the physics tick) when the next one spawned: a slag cart's
/// riders, a lone stander, a summoner's minions. Main's exit lets them all go (<see cref="Release"/>).
/// </summary>
public static class EnemyModels
{
    private static readonly Dictionary<string, PackedScene> Scenes = new();
    /// <summary>A profile's model name to its file (null: none on disk), so the existence checks run once a kind.</summary>
    private static readonly Dictionary<string, string?> Paths = new();
    /// <summary>Paths asked of the loader thread and not collected yet.</summary>
    private static readonly HashSet<string> Pending = new();
    private static readonly List<string> Collected = new();
    /// <summary>Each model's box in its Idle stance at time 0, by model and the one weapon mesh it shows (<c>Enemy.LoadModel</c> measures it once).</summary>
    private static readonly Dictionary<(string Model, string Weapon), Aabb> StandBoxes = new();

    /// <summary>The box <c>Enemy.LoadModel</c> measured for this model showing this weapon, if it has.</summary>
    public static bool TryStandBox(string model, string weapon, out Aabb box) => StandBoxes.TryGetValue((model, weapon), out box);

    public static void KeepStandBox(string model, string weapon, Aabb box) => StandBoxes[(model, weapon)] = box;

    /// <summary>The file a profile's model name stands for (a plain name is a Quaternius monster; a path under assets/models any other pack, .glb or .gltf), or null.</summary>
    public static string? PathOf(string model)
    {
        if (model == "") return null;
        if (Paths.TryGetValue(model, out var known)) return known;
        var path = model.Contains('/') ? $"res://assets/models/{model}.glb" : $"res://assets/models/quaternius_monsters/{model}.gltf";
        if (!ResourceLoader.Exists(path) && model.Contains('/')) path = $"res://assets/models/{model}.gltf";
        string? found = ResourceLoader.Exists(path) ? path : null;
        Paths[model] = found;
        return found;
    }

    /// <summary>The model's scene, loaded once: from the cache, from the loader thread's finished (or, waited for, unfinished) load, or from disk now.</summary>
    public static PackedScene? Scene(string model)
    {
        if (PathOf(model) is not { } path) return null;
        Collect();
        if (Scenes.TryGetValue(path, out var scene)) return scene;
        if (Pending.Remove(path) && ResourceLoader.LoadThreadedGet(path) is PackedScene threaded) scene = threaded; // waits for it if still loading
        scene ??= GD.Load<PackedScene>(path);
        if (scene != null) Scenes[path] = scene;
        return scene;
    }

    /// <summary>Start reading the model on a loader thread (nothing when it is loaded, loading or missing), so the spawn that wants it finds it ready.</summary>
    public static void Warm(string model)
    {
        if (PathOf(model) is not { } path) return;
        Collect();
        if (Scenes.ContainsKey(path) || Pending.Contains(path)) return;
        if (ResourceLoader.LoadThreadedRequest(path) == Error.Ok) Pending.Add(path);
    }

    /// <summary>Take in every threaded load that has finished; one that failed is collected too, so the loader forgets its task, and then forgotten (a spawn loads it the plain way).</summary>
    private static void Collect()
    {
        if (Pending.Count == 0) return;
        Collected.Clear();
        foreach (var path in Pending)
        {
            var status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress) continue;
            Collected.Add(path);
            if (status is not (ResourceLoader.ThreadLoadStatus.Loaded or ResourceLoader.ThreadLoadStatus.Failed)) continue; // InvalidResource: no task to collect
            if (ResourceLoader.LoadThreadedGet(path) is PackedScene scene && status == ResourceLoader.ThreadLoadStatus.Loaded) Scenes[path] = scene;
        }
        foreach (var path in Collected) Pending.Remove(path);
    }

    /// <summary>Main's exit: every model let go (a load still on its thread is waited for, then dropped). Nothing kept in a static past the tree.</summary>
    public static void Release()
    {
        foreach (var path in Pending) ResourceLoader.LoadThreadedGet(path);
        Pending.Clear();
        Scenes.Clear();
        Paths.Clear();
        StandBoxes.Clear();
    }
}
