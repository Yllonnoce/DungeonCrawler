using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// --probe-pickups (a network client): walk up to every item lying on the floor, press E on it the way a player does
/// (the look ray, then TryInteract), and print whether the server took it. Finds items a client sees but cannot pick up.
/// </summary>
public partial class DebugTools
{
    private bool _probePickups;
    private List<(Guid id, string name, Vector3 at)>? _probeQueue;
    private int _probeIndex;
    private double _probeClock;
    private int _probeStage;
    private int _probeTaken, _probeRefused;
    private List<Dungeon.Props.Chest>? _probeChests;
    private int _probeChestIndex;

    private void TickPickupProbe(double delta)
    {
        if (!_probePickups || !Net.NetSession.IsClient || Net.NetSession.Instance.Client?.Phase != Net.ClientHandshake.Phase.InGame) return;
        if (Main.Instance?.Builder.FloorRoot is not { } root || Main.Instance.Party.Local is not { } me) return;
        _probeClock += delta;
        if (_probeClock < 3 && _probeChests == null) return; // snapshots flowing, so the server listens to our intents
        if (_probeChests == null) { _probeChests = root.GetChildren().OfType<Dungeon.Props.Chest>().Where(c => !c.Opened).ToList(); GD.Print($"[probe] opening {_probeChests.Count} chest(s) first"); _probeClock = 0; }
        if (_probeChestIndex < _probeChests.Count)
        {
            var chest = _probeChests[_probeChestIndex];
            if (_probeStage == 0) { me.Teleport(Main.Instance.Builder.Surface(Dungeon.DungeonBuilder.WorldToCell(chest.GlobalPosition + chest.GlobalTransform.Basis.Z * 1.5f)) + Vector3.Up * 0.3f, chest.GlobalPosition); _probeClock = 0; _probeStage = 1; return; }
            if (_probeStage == 1 && _probeClock > 0.6) { Net.NetSession.Instance.Send(new Net.InteractIntent(Net.NetRefs.Chest(chest.SpawnIndex))); _probeClock = 0; _probeStage = 2; return; }
            if (_probeStage == 2 && _probeClock > 1.5) { GD.Print($"[probe] chest {chest.SpawnIndex}: {(chest.Opened ? "opened" : "did not open")}"); _probeChestIndex++; _probeStage = 0; _probeClock = 0; }
            return;
        }
        if (_probeQueue == null)
        {
            if (_probeClock < 1.5) return; // the last chest's spill arrives
            _probeQueue = root.GetChildren().OfType<LootDrop>().Where(d => !d.Taken)
                .Select(d => (d.Item.Id, d.Coins > 0 ? $"{d.Coins} gold" : d.Item.ToString(), d.GlobalPosition)).ToList();
            GD.Print($"[probe] {_probeQueue.Count} item(s) on floor {Game.Instance.CurrentFloor}: {string.Join(", ", _probeQueue.Select(q => q.name))}");
            _probeClock = 0;
            return;
        }
        if (_probeIndex >= _probeQueue.Count)
        {
            GD.Print($"[probe] RESULT taken {_probeTaken}, refused {_probeRefused} of {_probeQueue.Count}");
            _probePickups = false;
            return;
        }
        var (id, name, at) = _probeQueue[_probeIndex];
        var drop = root.GetChildren().OfType<LootDrop>().FirstOrDefault(d => d.Item.Id == id && !d.Taken);
        switch (_probeStage)
        {
            case 0:
                if (drop == null) { GD.Print($"[probe] {name}: gone before we got there (a companion or the server took it)"); _probeIndex++; return; }
                // stand 1.4 m off on the item's level, facing it
                var cell = Dungeon.DungeonBuilder.WorldToCell(at);
                var stand = Main.Instance.Builder.Surface(cell);
                if (Math.Abs(stand.Y - (at.Y - 0.4f)) > 1.5f) stand = at with { Y = at.Y - 0.4f }; // the item lies on another level of this cell (under a ledge): stand on its level
                var flat = new Vector3(at.X - stand.X, 0, at.Z - stand.Z);
                if (flat.Length() < 1.2f) stand -= (flat.LengthSquared() > 0.01f ? flat.Normalized() : Vector3.Forward) * (1.4f - flat.Length());
                me.Teleport(stand + Vector3.Up * 0.3f, at);
                _probeClock = 0; _probeStage = 1;
                return;
            case 1:
                if (_probeClock < 0.6) return;
                // look at it the way a player does, and press E
                me.Camera.LookAt(at, Vector3.Up);
                me.Rotation = me.Rotation with { Y = me.Camera.GlobalRotation.Y };
                me.Camera.Rotation = me.Camera.Rotation with { Y = 0, Z = 0 };
                _probeClock = 0; _probeStage = 2;
                return;
            case 2:
                if (_probeClock < 0.3) return;
                var looking = me.LookingAt;
                var dist = me.GlobalPosition.DistanceTo(at);
                if (looking is not LootDrop seen || seen.Item.Id != id)
                    GD.Print($"[probe] {name}: no pick-up prompt from {dist:0.0} m (looking at {(looking == null ? "nothing" : looking.Prompt)}), sending it anyway");
                if (looking is LootDrop l && l.Item.Id == id) me.TryInteract();
                else Net.NetSession.Instance.Send(new Net.InteractIntent(Net.NetRefs.Loot(id)));
                _probeClock = 0; _probeStage = 3;
                return;
            case 3:
                if (drop == null) { _probeTaken++; GD.Print($"[probe] {name}: taken"); _probeIndex++; _probeStage = 0; return; }
                if (_probeClock < 2.5) return;
                _probeRefused++;
                GD.Print($"[probe] {name}: REFUSED (item at {at:0.0}, us at {me.GlobalPosition:0.0}, {me.GlobalPosition.DistanceTo(at):0.0} m)");
                _probeIndex++; _probeStage = 0;
                return;
        }
    }
}
