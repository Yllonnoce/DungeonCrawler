using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// --probe-near (a network client, read-only): once in game, for every other player in the world, list the items lying
/// within 5 m of them and what a look from their eyes toward each one meets first, both as builds before the opened-chest
/// fix saw it (an opened chest's use box still on the interact layer) and as this build sees it. Nothing is sent.
/// </summary>
public partial class DebugTools
{
    private bool _probeNear, _probeNearDone;
    private double _probeNearClock;

    private void TickProbeNear(double delta)
    {
        if (!_probeNear || _probeNearDone || !Net.NetSession.IsClient || Net.NetSession.Instance.Client?.Phase != Net.ClientHandshake.Phase.InGame) return;
        if (Main.Instance?.Builder.FloorRoot is not { } root || Main.Instance.Party is not { } party) return;
        _probeNearClock += delta;
        if (_probeNearClock < 4) return; // a few snapshots in, so the other bodies stand where they are
        _probeNearDone = true;
        var client = Net.NetSession.Instance.Client!;
        var drops = root.GetChildren().OfType<LootDrop>().Where(d => !d.Taken).ToList();
        var chests = root.GetChildren().OfType<Chest>().ToList();
        GD.Print($"[near] floor {Game.Instance.CurrentFloor}: {drops.Count} item(s) on the floor, {chests.Count(c => c.Opened)} of {chests.Count} chest(s) open");
        var space = root.GetWorld3D().DirectSpaceState;
        foreach (var adv in party.Active.Where(a => a.Index != party.LocalIndex))
        {
            var who = client.Roster.Values.FirstOrDefault(p => p.MemberIndex == adv.Index);
            var eye = adv.GlobalPosition + Vector3.Up * 1.6f;
            GD.Print($"[near] {adv.State.Name}{(who?.BotOwner != null ? " (bot)" : "")} at {adv.GlobalPosition:0.0}");
            foreach (var d in drops.Where(d => d.GlobalPosition.DistanceTo(adv.GlobalPosition) < 5f).OrderBy(d => d.GlobalPosition.DistanceTo(adv.GlobalPosition)))
            {
                var old = FirstHit(space, eye, d.GlobalPosition, chests, emulateOldChests: true);
                var now = FirstHit(space, eye, d.GlobalPosition, chests, emulateOldChests: false);
                var name = d.Coins > 0 ? $"{d.Coins} gold" : d.Item.ToString();
                GD.Print($"[near]   {name} at {d.GlobalPosition:0.0}, {d.GlobalPosition.DistanceTo(adv.GlobalPosition):0.0} m: old build looks at {old}; this build looks at {now}");
            }
        }
    }

    /// <summary>What a 3 m look from <paramref name="eye"/> toward <paramref name="target"/> meets first on the interact layer, past the walls.</summary>
    private static string FirstHit(PhysicsDirectSpaceState3D space, Vector3 eye, Vector3 target, List<Chest> chests, bool emulateOldChests)
    {
        var saved = chests.Select(c => c.CollisionLayer).ToList();
        if (emulateOldChests) foreach (var c in chests) if (c.Opened) c.CollisionLayer = 8;
        try
        {
            var to = eye + (target - eye).Normalized() * 3f;
            var hit = space.IntersectRay(new PhysicsRayQueryParameters3D { From = eye, To = to, CollisionMask = 8, CollideWithAreas = true, CollideWithBodies = true });
            if (hit.Count == 0) return target.DistanceTo(eye) > 3.4f ? "nothing (out of reach)" : "nothing";
            return hit["collider"].As<GodotObject>() switch
            {
                LootDrop l => $"the item{(l.GlobalPosition.DistanceTo(target) < 0.01f ? "" : $" {l.Item}")}",
                IInteractable i => $"\"{i.Prompt}\"",
                Node n => n.Name.ToString(),
                _ => "?",
            };
        }
        finally { for (var i = 0; i < chests.Count; i++) chests[i].CollisionLayer = saved[i]; }
    }
}
