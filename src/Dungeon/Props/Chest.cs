using DungeonCrawler.Core;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>A chest: interact once to spill its loot onto the floor.</summary>
public partial class Chest : Area3D, IInteractable
{
    [Export] public string TableId { get; set; } = "table.chest";
    public int SpawnIndex { get; set; } = -1;
    public bool Locked { get; set; }
    public bool Opened { get; private set; }

    public void MarkOpened()
    {
        Opened = true;
        Tint(new Color(0.35f, 0.3f, 0.25f));
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Chest(SpawnIndex), "opened");
    }

    public string Prompt => Opened ? "Empty chest" : Locked ? "Locked chest: use a key or pick the lock" : "Open chest";

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Mesh");
        if (ResourceLoader.Exists("res://assets/models/treasure_chest/treasure_chest_1k.gltf"))
        {
            mesh.Visible = false;
            var model = GD.Load<PackedScene>("res://assets/models/treasure_chest/treasure_chest_1k.gltf").Instantiate<Node3D>();
            model.Name = "Model";
            AddChild(model);
            model.Scale = Vector3.One * 1.6f;
            model.RotateY(Mathf.Pi);
        }
        if (Locked) Tint(new Color(0.75f, 0.7f, 0.55f));
    }

    private void Tint(Color color)
    {
        if (GetNodeOrNull<Node3D>("Model") is { } model)
            foreach (var mi in FindMeshes(model)) { var mat = (mi.GetActiveMaterial(0)?.Duplicate() as StandardMaterial3D) ?? new StandardMaterial3D(); mat.AlbedoColor = color; mi.SetSurfaceOverrideMaterial(0, mat); }
        else GetNode<MeshInstance3D>("Mesh").SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = color });
    }

    private static IEnumerable<MeshInstance3D> FindMeshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in FindMeshes(c)) yield return m;
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Opened) return;
        if (Locked)
        {
            if (!party.TryUnlock("the chest")) return;
            Locked = false;
        }
        MarkOpened();
        EventBus.Instance.PlaySfx("chest");
        EventBus.Instance.RaiseChestOpened(GlobalPosition);

        var rng = new Random(Rng.ActorSeed(Game.Instance.GameSeed, Game.Instance.CurrentFloor, 5000 + SpawnIndex)); // the same contents on every peer and reload
        var items = LootRoller.RollMany(Game.Instance.Catalog, Game.Instance.Catalog.Table(TableId), TierRules.ItemLevel(Game.Instance.CurrentFloor), rng);
        var toward = (actor.GlobalPosition - GlobalPosition); toward.Y = 0;
        toward = toward.LengthSquared() > 0.01f ? toward.Normalized() : Vector3.Forward;
        var side = toward.Cross(Vector3.Up);
        for (var i = 0; i < items.Count; i++)
            party.SpawnLoot(items[i], GlobalPosition + toward * 1.0f + side * (i - (items.Count - 1) / 2f) * 0.7f + Vector3.Up * 0.4f);
        EventBus.Instance.Log(items.Count == 0 ? "The chest is empty." : $"The chest holds {string.Join(", ", items.Select(i => i.ToString()))}.");
    }
}
