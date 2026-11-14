using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The enemy check and the floor resync. With each item check the server lists its living enemies by net id
/// (<see cref="EnemyCheckMsg"/>): a client frees a living enemy the server lacks (a ghost whose death or end never arrived,
/// or one its floor state brought back) and asks for the ones it lacks, which go to it alone as <see cref="EnemySpawned"/>.
/// A client whose floor change never arrived or would not build asks for the floor (<see cref="FloorResyncIntent"/>) and
/// gets it as it stands, as a <see cref="FloorChange"/> with Resync set.
/// </summary>
public partial class ServerWorld
{
    private readonly Dictionary<int, double> _lastEnemyResync = new();
    private readonly Dictionary<int, double> _lastFloorResync = new();

    /// <summary>A peer left: its resync rate limits go with it (every connection gets a fresh peer id).</summary>
    private void ForgetResyncLimits(int peer)
    {
        _lastEnemyResync.Remove(peer);
        _lastFloorResync.Remove(peer);
        _lastPropResync.Remove(peer);
        _lastStateResync.Remove(peer);
        _lastLootResync.Remove(peer);
        foreach (var k in _lastBagRefusal.Keys.Where(k => k.Peer == peer).ToList()) _lastBagRefusal.Remove(k);
    }

    private static IEnumerable<Enemy> LivingEnemies(Node3D root) => root.GetChildren().OfType<Enemy>().Where(e => e.IsAlive && !e.IsQueuedForDeletion());

    private EnemyCheckMsg EnemyCheckOf(Node3D root) => new(Game.Instance.CurrentFloor, _floorEpoch, LivingEnemies(root).Select(e => e.NetId).OrderBy(id => id).ToList());

    /// <summary>A client has no living copy of some listed enemies: send it each one still alive here (at most 64, once per check interval).</summary>
    private void OnEnemyResync(PlayerSlot slot, EnemyResyncIntent r)
    {
        if (r.Floor != Game.Instance.CurrentFloor || !WorldCheck.SameBuild(_floorEpoch, r.Epoch) || _floorChanging || Main.Instance?.Builder.FloorRoot is not { } root) return;
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastEnemyResync.TryGetValue(slot.PeerId, out var last) && now - last < Protocol.LootCheckSeconds - 0.5) return;
        _lastEnemyResync[slot.PeerId] = now;
        var wanted = r.Missing.Take(64).ToHashSet();
        var sent = 0;
        foreach (var e in LivingEnemies(root))
        {
            if (!wanted.Contains(e.NetId)) continue;
            SendTo(slot.PeerId, new EnemySpawned(EnemyDto(e)));
            sent++;
        }
        if (sent == 0) return;
        GD.Print($"[net-server] enemy check: sent {slot.Name} {sent} creature(s) they lacked");
        Trace.Add("enemies-resent");
    }

    /// <summary>
    /// A client's floor change never arrived or failed to build: send it the floor as it stands. Not while we are building
    /// one ourselves (the FloorChange is coming; the client asks again later), and at most every few seconds per peer.
    /// </summary>
    private void OnFloorResync(PlayerSlot slot, FloorResyncIntent r)
    {
        var main = Main.Instance;
        var game = Game.Instance;
        GD.Print($"[net-server] {slot.Name} asks for the floor again (theirs {r.Floor}, build {r.Epoch}; ours {game.CurrentFloor}, build {_floorEpoch}): {r.Reason}");
        if (_floorChanging || main.Rebuilding || main.Builder.CurrentFloor != game.CurrentFloor || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || main.Exploration is not { } exploration)
        {
            GD.Print("[net-server] the floor is still being built; the FloorChange will follow");
            // a plan the AI is still writing can outlast the client's watchdog (3 asks, then a rejoin): say the change is still
            // under way, which restarts its watch. Only for as long as a plan may be waited for; past that the build is stuck
            // here and the client's rejoin is its way out
            var waited = Time.GetTicksMsec() / 1000.0 - _floorChangeStarted;
            if (_floorChanging && _floorChangeBegin is { } begin && waited < Settings.PlanWaitSeconds + 30) SendTo(slot.PeerId, begin);
            return;
        }
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastFloorResync.TryGetValue(slot.PeerId, out var last) && now - last < 3.0) return;
        _lastFloorResync[slot.PeerId] = now;
        // its poses stop counting until it says it has built this floor (OnFloorReady), as after any floor change
        slot.Ready = false;
        var party = main.Party;
        if (slot.MemberIndex < party.Adventurers.Length) party.Adventurers[slot.MemberIndex].Remote = null;
        var state = FloorStateCapture.Capture(root, layout, main.Builder.Contents, exploration);
        var poses = party.Adventurers.Select(a => new PoseDto { X = a.GlobalPosition.X, Y = a.GlobalPosition.Y, Z = a.GlobalPosition.Z, Yaw = a.Rotation.Y }).ToList();
        // the change it missed, if it holds an older build (its log says it descended); a rebuild in place otherwise
        var delta = r.Epoch != _floorEpoch ? _floorDelta : 0;
        SendTo(slot.PeerId, FloorChangeOf(game.CurrentFloor, delta, state, poses, firstVisit: false, resync: true));
        GD.Print($"[net-server] sent {slot.Name} floor {game.CurrentFloor} (build {_floorEpoch}) again, {now - _floorBuiltAt:0.0} s after it was built");
        Trace.Add("floor-resent");
    }
}
