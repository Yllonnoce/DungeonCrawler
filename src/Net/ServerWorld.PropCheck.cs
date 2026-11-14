using DungeonCrawler.Core;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The prop check: with every item check the server sends the fingerprint of its floor's props (<see cref="PropCheck"/>).
/// A client whose own props hash differently asks once, and gets the whole <see cref="PropStateDto"/> to itself. Props
/// otherwise travel only as one-shot <see cref="PropEvt"/>s, so one that went astray (a door, a sprung trap, a barrel a
/// press splintered) used to stay wrong on that screen until a reconnect or a floor change.
/// </summary>
public partial class ServerWorld
{
    private readonly Dictionary<int, double> _lastPropResync = new();

    /// <summary>The fingerprint of the props on this floor, for every peer in game (called with the item check).</summary>
    private void BroadcastPropCheck(Node3D root) => Broadcast(new PropCheckMsg(Game.Instance.CurrentFloor, PropCheck.Hash(PropSync.Capture(root))));

    /// <summary>The whole prop state of this floor, to one peer.</summary>
    private void SendPropState(int peer)
    {
        if (Main.Instance?.Builder.FloorRoot is not { } root) return;
        SendTo(peer, new PropStateMsg(Game.Instance.CurrentFloor, PropSync.Capture(root)));
    }

    /// <summary>A client's props differ from the prop check: send it the server's, once per check interval at most.</summary>
    private void OnPropResync(PlayerSlot slot, PropResyncIntent r)
    {
        if (r.Floor != Game.Instance.CurrentFloor) return;
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastPropResync.TryGetValue(slot.PeerId, out var last) && now - last < Protocol.LootCheckSeconds - 0.5) return;
        _lastPropResync[slot.PeerId] = now;
        SendPropState(slot.PeerId);
        GD.Print($"[net-server] prop check: resent {slot.Name} the props of floor {r.Floor}");
        Trace.Add("props-resent");
    }
}
