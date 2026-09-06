using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Items;

/// <summary>An item lying in the world. Looked at + interact moves it into the party bag.</summary>
public partial class LootDrop : Area3D, IInteractable
{
    public ItemInstance Item { get; set; } = ItemInstance.Simple("item.unknown", "Unknown Item");
    /// <summary>For keys placed by the generator: index used to remember which keys were taken. -1 for ordinary drops.</summary>
    public int KeyIndex { get; set; } = -1;
    /// <summary>Set for Recover-quest items: the live quest key that picking this up advances.</summary>
    public string? QuestId { get; set; }
    /// <summary>Put down on purpose by the party: companions walk past it.</summary>
    public bool DroppedByParty { get; set; }

    public void NotifyPicked() { if (QuestId != null) Core.EventBus.Instance.RaiseQuestItemPicked(QuestId); }

    public string Prompt => Item.Rarity == Rarity.Common ? $"Pick up {Item}" : $"Pick up {Item} ({Item.Rarity})";

    private Node3D? _spin;

    public override void _Ready()
    {
        var mesh = GetNode<MeshInstance3D>("Mesh");
        var rarityColor = new Color(RarityInfo.ColorHex(Item.Rarity));
        if (ItemVisuals.Attach(this, Item))
        {
            mesh.Visible = false;
            _spin = GetNode<Node3D>("ItemModel");
            // a blade lies at a slant; everything else hovers upright
            if (Item.IsWeapon) _spin.RotateZ(Mathf.Pi / 3f);
        }
        else
        {
            var color = Item.Rarity == Rarity.Common ? new Color(Item.Color) : rarityColor;
            mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = Item.Rarity == Rarity.Common ? 0.15f : 0.7f,
            });
            if (Item.Rarity >= Rarity.Rare) mesh.Scale = Vector3.One * 1.25f;
            _spin = mesh;
        }
        if (Item.Rarity >= Rarity.Magic || QuestId != null)
        {
            // a halo on the floor in the rarity's colour, and a glow for the rare and better
            var halo = new Color(QuestId != null ? "#ffd070" : RarityInfo.ColorHex(Item.Rarity));
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.02f, RadialSegments = 24 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = halo with { A = 0.22f }, EmissionEnabled = true, Emission = halo, EmissionEnergyMultiplier = 0.5f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                Position = new Vector3(0, -0.3f, 0), Name = "Halo",
            });
            if (Item.Rarity >= Rarity.Rare || QuestId != null)
                AddChild(new OmniLight3D { LightColor = halo, LightEnergy = 0.45f, OmniRange = 2.5f, ShadowEnabled = false });
        }
    }

    public override void _Process(double delta)
    {
        if (_spin == null) return;
        _spin.RotateY((float)delta * 1.5f);
        _spin.Position = _spin.Position with { Y = Mathf.Sin((float)Time.GetTicksMsec() / 700f + GetInstanceId() % 7) * 0.04f };
    }

    public void Interact(PartyController party, Adventurer actor)
    {
        if (party.PickUp(Item)) { NotifyPicked(); Core.EventBus.Instance.RaiseLootRemoved(Item.Id, actor.Index); QueueFree(); }
    }
}
