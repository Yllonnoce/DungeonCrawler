using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>Barrels and crates: any hit smashes them, sometimes spilling loot.</summary>
public partial class Breakable : StaticBody3D, ICombatTarget
{
    [Export] public string DisplayName { get; set; } = "Barrel";
    [Export] public string TableId { get; set; } = "table.breakable";

    public int SpawnIndex { get; set; } = -1;
    public bool IsAlive { get; private set; } = true;

    /// <summary>Wooden props from Poly Haven (CC0); breakables alternate between them by spawn index.</summary>
    private static readonly (string Path, string Name, float Height)[] Models =
    {
        ("res://assets/models/wine_barrel_01/wine_barrel_01_1k.gltf", "Barrel", 1.0f),
        ("res://assets/models/wooden_crate_01/wooden_crate_01_1k.gltf", "Crate", 0.8f),
    };

    public override void _Ready()
    {
        var pick = Models[Math.Abs(SpawnIndex) % Models.Length];
        if (!ResourceLoader.Exists(pick.Path)) return;
        DisplayName = pick.Name;
        GetNode<MeshInstance3D>("Mesh").Visible = false;
        var model = GD.Load<PackedScene>(pick.Path).Instantiate<Node3D>();
        AddChild(model);
        // scale to a known height and stand it on the floor, whatever units the file uses
        var aabb = new Aabb();
        var first = true;
        foreach (var mi in Core.AnimatedModel.Meshes(model))
        {
            var box = mi.GetAabb().Abs();
            aabb = first ? box : aabb.Merge(box);
            first = false;
        }
        var scale = pick.Height / Math.Max(0.05f, aabb.Size.Y);
        model.Scale = Vector3.One * scale;
        model.Position = new Vector3(0, -aabb.Position.Y * scale, 0);
        model.RotateY((SpawnIndex * 37 % 8) * Mathf.Pi / 4f);
    }
    public bool IsUnaware => false;
    public void ApplyStatus(StatusApplication status) { }

    public DamageResult ReceiveAttack(Stats attackerStats, AttackProfile attack, HitContext ctx, Random rng, string attackerName)
    {
        if (!IsAlive) return DamageResult.Miss(attack.Type);
        IsAlive = false;
        EventBus.Instance.PlaySfx("barrel");
        EventBus.Instance.Log($"{attackerName} smashes the {DisplayName.ToLower()}.");

        var party = Main.Instance.Party;
        var items = LootRoller.RollMany(Game.Instance.Catalog, Game.Instance.Catalog.Table(TableId), Game.Instance.CurrentFloor, rng);
        for (var i = 0; i < items.Count; i++)
            party.SpawnLoot(items[i], GlobalPosition + new Vector3((i - 1) * 0.5f, 0.4f, 0));

        Vanish();
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Barrel(SpawnIndex), "broken");
        return new DamageResult(true, false, false, false, 1, attack.Type, Array.Empty<StatusApplication>());
    }

    /// <summary>Gone without loot or noise (a client showing a barrel the server broke).</summary>
    public void Vanish()
    {
        IsAlive = false;
        CollisionLayer = 0;
        SetDeferred(Node3D.PropertyName.Visible, false);
        CallDeferred(Node.MethodName.QueueFree);
    }
}
