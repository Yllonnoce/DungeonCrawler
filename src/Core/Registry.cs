using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// Loads every definition Resource under res://data at startup and resolves them by string id.
/// Runtime state stores ids, never Resource references, so it stays serializable.
/// </summary>
public partial class Registry : Node
{
    public static Registry Instance { get; private set; } = null!;

    private readonly Dictionary<string, Resource> _defs = new();

    public override void _Ready()
    {
        Instance = this;
        LoadDirectory("res://data");
        GD.Print($"[Registry] loaded {_defs.Count} definitions");
    }

    public T Get<T>(string id) where T : Resource
        => _defs.TryGetValue(id, out var r) && r is T t ? t : throw new KeyNotFoundException($"No definition '{id}' of type {typeof(T).Name}");

    public bool TryGet<T>(string id, out T def) where T : Resource
    {
        if (_defs.TryGetValue(id, out var r) && r is T t) { def = t; return true; }
        def = null!;
        return false;
    }

    public IEnumerable<T> All<T>() where T : Resource => _defs.Values.OfType<T>();

    private void LoadDirectory(string path)
    {
        using var dir = DirAccess.Open(path);
        if (dir == null) return;
        dir.ListDirBegin();
        for (var name = dir.GetNext(); name != ""; name = dir.GetNext())
        {
            if (name.StartsWith('.')) continue;
            var full = $"{path}/{name}";
            if (dir.CurrentIsDir()) { LoadDirectory(full); continue; }
            // exported builds convert .tres to binary and list them as .res or .tres.remap; load by the original path
            if (name.EndsWith(".remap")) { name = name[..^".remap".Length]; full = $"{path}/{name}"; }
            if (name.EndsWith(".import")) continue;
            if (!name.EndsWith(".tres") && !name.EndsWith(".res")) continue;
            if (!ResourceLoader.Exists(full)) continue;
            var res = ResourceLoader.Load<Resource>(full);
            if (res is IHasId hasId) _defs[hasId.Id] = res;
        }
        dir.ListDirEnd();
    }
}

/// <summary>Implemented by every definition Resource so Registry can index it.</summary>
public interface IHasId
{
    string Id { get; }
}
