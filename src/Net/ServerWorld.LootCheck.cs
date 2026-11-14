using DungeonCrawler.Core;
using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The item check: every <see cref="Protocol.LootCheckSeconds"/> the server lists the items on its floor by id and count. A
/// client drops any item it shows that is not on the list (a pickup it never heard about) and asks for the ones it lacks (a
/// spawn it missed) or shows with another count, so a missed message costs a few seconds of a wrong floor, never a ghost
/// for good. Every list names the floor build (<see cref="FloorEpoch"/>), so one about a rebuilt floor of the same number
/// is never applied to the other.
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
        // not while the floor changes, or while a plan is awaited (the number moved on, the root has not): a list of the old
        // floor's items under the new floor's number is what left a joiner on the wrong floor ignoring every check
        if (_floorChanging || main.Builder.CurrentFloor != Game.Instance.CurrentFloor) return;
        Broadcast(LootManifestOf(root));
        Broadcast(EnemyCheckOf(root)); // the living enemies (ServerWorld.WorldCheck.cs)
        FlushSync(); // anything changed since the last flush goes out first, so the check below never races its own sync
        var party = main.Party;
        var locked = root.GetNodeOrNull<Dungeon.Stairs>("DownStairs")?.Locked ?? false;
        Broadcast(new StateCheckMsg(Game.Instance.CurrentFloor, StateCheck.BagHash(Save.SaveMapper.ToDto(party.Bag)), party.Gold,
            party.Members.Select(m => StateCheck.MemberHash(Save.SaveMapper.ToDto(m))).ToList(), locked));
        BroadcastPropCheck(root); // the props: doors, levers, chests, secrets, traps, barrels, the shrine (ServerWorld.PropCheck.cs)
    }

    private readonly Dictionary<int, double> _lastStateResync = new();
    private readonly Dictionary<int, double> _lastLootResync = new();

    /// <summary>A client's copy differs from the state check: send it the parts it asked for (once per check interval per peer).</summary>
    private void OnStateResync(PlayerSlot slot, StateResyncIntent r)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastStateResync.TryGetValue(slot.PeerId, out var last) && now - last < Protocol.LootCheckSeconds - 0.5) return;
        _lastStateResync[slot.PeerId] = now;
        var party = Main.Instance.Party;
        if (r.Bag) SendTo(slot.PeerId, new BagSync(Save.SaveMapper.ToDto(party.Bag)));
        foreach (var i in r.Members.Distinct().Take(8)) if (i >= 0 && i < party.Members.Length) SendTo(slot.PeerId, new PartySync(i, Save.SaveMapper.ToDto(party.Members[i])));
        if (r.Gold) SendTo(slot.PeerId, new GoldSync(party.Gold));
        GD.Print($"[net-server] state check: resent {slot.Name} {(r.Bag ? "the bag " : "")}{(r.Members.Count > 0 ? $"member(s) {string.Join(",", r.Members)} " : "")}{(r.Gold ? "the purse" : "")}");
        Trace.Add("state-resent");
    }

    private static IEnumerable<LootDrop> LootOnFloor(Node3D root) => root.GetChildren().OfType<LootDrop>().Where(d => !d.Taken);

    /// <summary>The item check's list: every item on the floor by id, with its count (a pile's coins, a stack's size) and the floor build.</summary>
    private LootManifest LootManifestOf(Node3D root)
    {
        var drops = LootOnFloor(root).ToList();
        return new LootManifest(Game.Instance.CurrentFloor, drops.Select(d => d.Item.Id).ToList(), _floorEpoch, drops.Select(d => WorldCheck.ItemCount(d.Coins, d.Item.StackCount)).ToList());
    }

    /// <summary>The item and enemy checks for one peer, at once: on its join and when it has built a floor it followed us to.</summary>
    private void SendWorldChecks(int peer)
    {
        if (Main.Instance is not { InGame: true } main || main.Rebuilding || _floorChanging || main.Builder.FloorRoot is not { } root || main.Builder.CurrentFloor != Game.Instance.CurrentFloor) return;
        SendTo(peer, LootManifestOf(root));
        SendTo(peer, EnemyCheckOf(root));
    }

    /// <summary>A client lacks some listed items: send it each one still on this floor (at most 64 a time).</summary>
    private void OnLootResync(PlayerSlot slot, LootResyncIntent r)
    {
        if (r.Floor != Game.Instance.CurrentFloor || !WorldCheck.SameBuild(_floorEpoch, r.Epoch) || Main.Instance?.Builder.FloorRoot is not { } root) return; // an ask about an earlier build of this floor: its answer would double an item
        var now = Time.GetTicksMsec() / 1000.0; // one answer per check interval per peer, as the other resyncs
        if (_lastLootResync.TryGetValue(slot.PeerId, out var last) && now - last < Protocol.LootCheckSeconds - 0.5) return;
        _lastLootResync[slot.PeerId] = now;
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
