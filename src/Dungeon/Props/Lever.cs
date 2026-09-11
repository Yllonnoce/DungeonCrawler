using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Props;

/// <summary>
/// Opens the gate of the vault with the same index. A lever whose index starts at <see cref="Adventure.FloorContents.ExtraIndexBase"/>
/// belongs to the floor's objective instead and reports to it; a <see cref="Toggle"/> lever can be pulled back and forth.
/// </summary>
public partial class Lever : Area3D, IInteractable
{
    public int VaultIndex { get; set; }
    public bool Pulled { get; private set; }
    /// <summary>Can be pulled again to put it back (a puzzle lever); an ordinary lever stays pulled.</summary>
    public bool Toggle { get; set; }
    /// <summary>What the prompt calls it ("Pull lever" by default).</summary>
    public string? Label { get; set; }
    public bool IsObjectiveLever => VaultIndex >= Adventure.FloorContents.ExtraIndexBase;
    public int ObjectiveIndex => VaultIndex - Adventure.FloorContents.ExtraIndexBase;

    public string Prompt => Pulled && !Toggle ? "" : Pulled ? $"Reset {Label ?? "lever"}" : $"Pull {Label ?? "lever"}";

    public void Interact(PartyController party, Adventurer actor)
    {
        if (Pulled && !Toggle) return;
        if (Pulled) { Reset(); EventBus.Instance.Log("The lever clunks back."); }
        else { Pull(); EventBus.Instance.Log("The lever clunks into place."); }
    }

    public void Pull(bool silent = false)
    {
        if (Pulled) return;
        Pulled = true;
        if (!silent) EventBus.Instance.PlaySfx("lever");
        GetNode<MeshInstance3D>("Handle").Transform = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-45)), new Vector3(0, 1.1f, 0.25f));
        if (!Toggle) CollisionLayer = 0;
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Lever(VaultIndex), "pulled");
        if (Net.NetSession.IsClient) return; // the server decides what a lever does; a client only shows it moved
        if (IsObjectiveLever) { Adventure.Objectives.ObjectiveController.Instance?.OnLever(ObjectiveIndex, true); return; }
        foreach (var d in GetParent().GetChildren().OfType<Door>())
            if (d.VaultIndex == VaultIndex) d.Open(silent);
    }

    /// <summary>A toggle lever put back.</summary>
    public void Reset(bool silent = false)
    {
        if (!Pulled || !Toggle) return;
        Pulled = false;
        if (!silent) EventBus.Instance.PlaySfx("lever");
        GetNode<MeshInstance3D>("Handle").Transform = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(45)), new Vector3(0, 1.1f, -0.25f));
        EventBus.Instance.RaisePropChanged(Net.NetRefs.Lever(VaultIndex), "reset");
        if (Net.NetSession.IsClient) return;
        if (IsObjectiveLever) Adventure.Objectives.ObjectiveController.Instance?.OnLever(ObjectiveIndex, false);
    }

    /// <summary>Put the handle where a save or the server says it stands, without telling anyone.</summary>
    public void SetPulled(bool pulled)
    {
        if (pulled == Pulled) return;
        Pulled = pulled;
        GetNode<MeshInstance3D>("Handle").Transform = pulled
            ? new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-45)), new Vector3(0, 1.1f, 0.25f))
            : new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(45)), new Vector3(0, 1.1f, -0.25f));
        if (!Toggle) CollisionLayer = pulled ? 0u : 8u;
    }
}
