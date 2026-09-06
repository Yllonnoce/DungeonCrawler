using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>Opens the gate of the vault with the same index.</summary>
public partial class Lever : Area3D, IInteractable
{
    public int VaultIndex { get; set; }
    public bool Pulled { get; private set; }

    public string Prompt => Pulled ? "" : "Pull lever";

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Pulled) return;
        Pull();
        EventBus.Instance.Log("The lever clunks into place.");
    }

    public void Pull(bool silent = false)
    {
        Pulled = true;
        if (!silent) EventBus.Instance.PlaySfx("lever");
        GetNode<MeshInstance3D>("Handle").Transform = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-45)), new Vector3(0, 1.1f, 0.25f));
        CollisionLayer = 0;
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Lever(VaultIndex), "pulled");
        foreach (var d in GetParent().GetChildren().OfType<Door>())
            if (d.VaultIndex == VaultIndex) d.Open(silent);
    }
}
