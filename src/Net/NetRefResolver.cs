using DungeonCrawler.Adventure;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
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
            case NetRefs.TrapKind: return NetRefs.TryIndex(id, out var ti) ? children.OfType<Trap>().FirstOrDefault(t => t.SpawnIndex == ti && !t.PlacedByParty) : null;
            case NetRefs.BarrelKind: return NetRefs.TryIndex(id, out var bi) ? children.OfType<Breakable>().FirstOrDefault(b => b.SpawnIndex == bi) : null;
            case NetRefs.StairsKind: return root.GetNodeOrNull<Stairs>(id == "down" ? "DownStairs" : "UpStairs");
            case NetRefs.NpcKind: return children.OfType<Npc>().FirstOrDefault(n => n.Id == id);
            case NetRefs.PlaqueKind: return NetRefs.TryIndex(id, out var pi) ? children.OfType<Plaque>().FirstOrDefault(p => p.Index == pi) : null;
            case NetRefs.LootKind: return Guid.TryParseExact(id, "N", out var g) ? children.OfType<LootDrop>().FirstOrDefault(l => l.Item.Id == g) : null;
            default: return null;
        }
    }
}
