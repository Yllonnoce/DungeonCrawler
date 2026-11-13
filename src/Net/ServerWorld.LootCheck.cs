using DungeonCrawler.Core;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The item check: every <see cref="Protocol.LootCheckSeconds"/> the server lists the items on its floor by id. A client
/// drops any item it shows that is not on the list (a pickup it never heard about) and asks for the ones it lacks (a
/// spawn it missed), so a missed message costs a few seconds of a wrong floor, never a ghost for good.
/// </summary>
public partial class ServerWorld
{
    private float _lootCheckTimer = Protocol.LootCheckSeconds;

    private void TickLootCheck(float dt)
    {
        _lootCheckTimer -= dt;
        if (_lootCheckTimer > 0f) return;
        _lootCheckTimer = Protocol.LootCheckSeconds;
        if (Quiet || !Handshake.InGamePeers.Any() || Main.Instance is not { InGame: true } main || main.Builder.FloorRoot is not { } root) return;
        Broadcast(new LootManifest(Game.Instance.CurrentFloor, LootOnFloor(root).Select(d => d.Item.Id).ToList()));
    }

    private static IEnumerable<LootDrop> LootOnFloor(Node3D root) => root.GetChildren().OfType<LootDrop>().Where(d => !d.Taken);

    /// <summary>A client lacks some listed items: send it each one still on this floor (at most 64 a time).</summary>
    private void OnLootResync(PlayerSlot slot, LootResyncIntent r)
    {
        if (r.Floor != Game.Instance.CurrentFloor || Main.Instance?.Builder.FloorRoot is not { } root) return;
        var wanted = r.Missing.Take(64).ToHashSet();
        var sent = 0;
        foreach (var d in LootOnFloor(root))
        {
            if (!wanted.Contains(d.Item.Id)) continue;
            SendTo(slot.PeerId, new LootSpawned(Save.SaveMapper.ToDto(d.Item), Vec(d.GlobalPosition), d.KeyIndex, d.QuestId, d.Coins));
            sent++;
        }
        if (sent > 0) { GD.Print($"[net-server] item check: sent {slot.Name} {sent} item(s) they lacked"); Trace.Add("loot-resent"); }
    }
}
