using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>Interactable nodes to their wire names and back. Floors build identically, so the names agree on every peer.</summary>
public static class NetRefResolver
{
    public static string? RefOf(IInteractable target) => target switch
    {
        Chest c => NetRefs.Chest(c.SpawnIndex),
        Door d => NetRefs.Door(d.VaultIndex),
        Lever l => NetRefs.Lever(l.VaultIndex),
        SecretWall s => NetRefs.Secret(s.Index),
        Trap t when t.SpawnIndex >= 0 => NetRefs.Trap(t.SpawnIndex),
        Stairs st => st.Direction > 0 ? NetRefs.StairsDown : NetRefs.StairsUp,
        LootDrop ld => NetRefs.Loot(ld.Item.Id),
        Npc n => NetRefs.Npc(n.Id),
        Plaque pl => NetRefs.Plaque(pl.Index),
        Shrine => NetRefs.Shrine(0),
        MineCart c when c == RailController.Instance?.Cart => NetRefs.Cart, // the party's cart only: a spare or a runaway wagon is no cart:0
        _ => null,
    };

    public static string? RefOf(Breakable b) => b.SpawnIndex >= 0 ? NetRefs.Barrel(b.SpawnIndex) : null;

    public static Node3D? Resolve(Node3D root, string netRef)
    {
        if (!NetRefs.TryParse(netRef, out var kind, out var id)) return null;
        var children = root.GetChildren();
        switch (kind)
        {
            case NetRefs.ChestKind: return NetRefs.TryIndex(id, out var ci) ? children.OfType<Chest>().FirstOrDefault(c => c.SpawnIndex == ci) : null;
            case NetRefs.DoorKind: return NetRefs.TryIndex(id, out var di) ? children.OfType<Door>().FirstOrDefault(d => d.VaultIndex == di) : null;
            case NetRefs.LeverKind: return NetRefs.TryIndex(id, out var li) ? children.OfType<Lever>().FirstOrDefault(l => l.VaultIndex == li) : null;
            case NetRefs.SecretKind: return NetRefs.TryIndex(id, out var si) ? children.OfType<SecretWall>().FirstOrDefault(s => s.Index == si) : null;
            case NetRefs.TrapKind: return NetRefs.TryIndex(id, out var ti) ? children.OfType<Trap>().FirstOrDefault(t => t.SpawnIndex == ti) : null; // the party's own traps too: their ids start at PropCheck.PlacedTrapBase
            case NetRefs.BarrelKind: return NetRefs.TryIndex(id, out var bi) ? children.OfType<Breakable>().FirstOrDefault(b => b.SpawnIndex == bi) : null;
            case NetRefs.StairsKind: return root.GetNodeOrNull<Stairs>(id == "down" ? "DownStairs" : "UpStairs");
            case NetRefs.NpcKind: return children.OfType<Npc>().FirstOrDefault(n => n.Id == id);
            case NetRefs.HazardKind: return NetRefs.TryIndex(id, out var hi) ? children.FirstOrDefault(n => n is Press { Index: var pi } && pi == hi || n is SpikeBed { Index: var bi2 } && bi2 == hi) as Node3D : null;
            case NetRefs.ShrineKind: return children.OfType<Shrine>().FirstOrDefault();
            case NetRefs.PlaqueKind: return NetRefs.TryIndex(id, out var pi) ? children.OfType<Plaque>().FirstOrDefault(p => p.Index == pi) : null;
            case NetRefs.LootKind: return Guid.TryParseExact(id, "N", out var g) ? children.OfType<LootDrop>().FirstOrDefault(l => l.Item.Id == g && !l.IsQueuedForDeletion()) : null;
            default: return ResolveRail(root, kind, id);
        }
    }

    /// <summary>
    /// A railed floor's props, found through their owners (not the root's direct children): the stops' gates, winches and
    /// spares through the <see cref="RailController"/>, the set pieces through the <see cref="RailEncounterController"/>, the
    /// cage wagons and the keyring through the crew objective. Only the owners of this very floor root answer.
    /// </summary>
    private static Node3D? ResolveRail(Node3D root, string kind, string id)
    {
        if (!NetRefs.TryIndex(id, out var index)) return null;
        var rails = RailController.Instance is { } rc && GodotObject.IsInstanceValid(rc) && rc.GetParent() == root ? rc : null;
        switch (kind)
        {
            case NetRefs.GateKind: return rails?.GateAt(index);
            case NetRefs.WinchKind: return rails?.GateAt(index)?.Winch;
            case NetRefs.SpareKind: return rails?.SpareAt(index);
            case NetRefs.CartKind: return index == 0 && rails != null && GodotObject.IsInstanceValid(rails.Cart) ? rails.Cart : null;
            case NetRefs.SwitchKind or NetRefs.SpurKind or NetRefs.HeapKind or NetRefs.ValveKind or NetRefs.TrestleKind or NetRefs.BufferKind:
                return RailEncounterController.Instance is { } enc && GodotObject.IsInstanceValid(enc) && enc.GetParent() == root ? enc.NetNode(kind, index) : null;
            case NetRefs.WagonKind or NetRefs.KeyringKind:
                if (Adventure.Objectives.ObjectiveController.Instance is not Adventure.Objectives.CrewRescueController crew || !GodotObject.IsInstanceValid(crew) || crew.GetParent() != root) return null;
                if (kind == NetRefs.KeyringKind) return index == 0 ? crew.Keyring : null;
                foreach (var wagon in crew.Wagons) if (wagon.Stop == index && GodotObject.IsInstanceValid(wagon)) return wagon;
                return null;
            default: return null;
        }
    }
}
