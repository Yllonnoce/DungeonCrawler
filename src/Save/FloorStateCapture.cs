using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Save;

/// <summary>Reads the deltas of the current floor out of the scene tree, and writes them back after a rebuild.</summary>
public static class FloorStateCapture
{
    public static FloorStateDto Capture(Node3D floorRoot, FloorLayout layout, FloorContents? contents, ExplorationMap exploration)
    {
        var dto = new FloorStateDto { Visited = true };
        foreach (var c in exploration.Seen) dto.Explored.Add(new[] { c.X, c.Y });

        var seenEnemies = new HashSet<int>();
        foreach (var child in floorRoot.GetChildren())
        {
            switch (child)
            {
                case Enemy e when e.SpawnIndex >= 0:
                    seenEnemies.Add(e.SpawnIndex);
                    if (!e.IsAlive) dto.DeadEnemies.Add(e.SpawnIndex);
                    else dto.Enemies.Add(new EnemyDto { SpawnIndex = e.SpawnIndex, Hp = e.Hp, X = e.GlobalPosition.X, Y = e.GlobalPosition.Y, Z = e.GlobalPosition.Z, EmbeddedArrows = e.EmbeddedArrows });
                    break;
                case Chest ch when ch.Opened:
                    dto.OpenedChests.Add(ch.SpawnIndex);
                    break;
                case LootDrop { KeyIndex: >= 0 }:
                    break; // generator keys are tracked by TakenKeys, not as world items
                case LootDrop { QuestId: not null }:
                    break; // quest items are tracked by TakenQuestItems
                case Npc npc:
                    dto.NpcStates.Add(new NpcDto { Id = npc.Id, Dead = npc.Dead, Escorting = npc.Escorting, Rescued = npc.Rescued, Downed = npc.Downed, Captured = npc.Captured, Jailed = npc.Jailed, Freed = npc.Freed, Hp = npc.Hp, X = npc.GlobalPosition.X, Y = npc.GlobalPosition.Y, Z = npc.GlobalPosition.Z });
                    break;
                case LootDrop drop:
                    dto.Items.Add(new WorldItemDto { Item = SaveMapper.ToDto(drop.Item), X = drop.GlobalPosition.X, Y = drop.GlobalPosition.Y, Z = drop.GlobalPosition.Z });
                    break;
                case Trap { PlacedByParty: true, Armed: true } placed:
                    dto.PlacedTraps.Add(new PlacedTrapDto { Kind = placed.Kind.ToString(), X = placed.GlobalPosition.X, Y = placed.GlobalPosition.Y, Z = placed.GlobalPosition.Z });
                    break;
                case Trap { PlacedByParty: false } trap:
                    if (trap.Triggered) dto.TriggeredTraps.Add(trap.SpawnIndex);
                    else if (trap.Disarmed) dto.DisarmedTraps.Add(trap.SpawnIndex);
                    break;
                case Door { IsOpen: true, IsObjectiveGate: false } door:
                    dto.OpenedVaults.Add(door.VaultIndex);
                    break;
                case SecretWall { IsOpen: true } secret:
                    dto.OpenedSecrets.Add(secret.Index);
                    break;
                case Press press:
                    dto.Hazards.Add(new HazardStateDto { Index = press.Index, Kind = (byte)Dungeon.HazardKind.Press, Phase = (byte)press.Phase, PhaseTime = press.PhaseTime, Spent = press.Spent, Jammed = press.Jammed });
                    break;
                case SpikeBed bed:
                    dto.Hazards.Add(new HazardStateDto { Index = bed.Index, Kind = (byte)Dungeon.HazardKind.SpikeBed, Phase = (byte)bed.Phase, PhaseTime = bed.PhaseTime, Spent = bed.Spent });
                    break;
            }
        }
        // enemies already freed (corpse timer ran out) are dead too
        var enemyCount = contents?.EnemyCount ?? layout.TotalEnemySpawns;
        for (var i = 0; i < enemyCount; i++) if (!seenEnemies.Contains(i) && !dto.DeadEnemies.Contains(i)) dto.DeadEnemies.Add(i);
        // barrels that no longer exist were broken
        var barrels = floorRoot.GetChildren().OfType<Breakable>().Select(b => b.SpawnIndex).ToHashSet();
        for (var i = 0; i < layout.BreakableSpawns.Count; i++) if (!barrels.Contains(i)) dto.BrokenBarrels.Add(i);
        var keysPresent = floorRoot.GetChildren().OfType<LootDrop>().Where(d => d.KeyIndex >= 0).Select(d => d.KeyIndex).ToHashSet();
        var keyCount = layout.Vaults.Count(v => v.Key != null) + layout.KeySpawns.Count;
        for (var i = 0; i < keyCount; i++) if (!keysPresent.Contains(i)) dto.TakenKeys.Add(i);
        var questDrops = floorRoot.GetChildren().OfType<LootDrop>().Where(d => d.QuestId != null).Select(d => d.QuestId!).ToHashSet();
        if (contents != null) foreach (var q in contents.QuestItems) { var key = Adventure.QuestKeys.Item(Game.Instance.CurrentFloor, q.QuestId, q.Ordinal); if (!questDrops.Contains(key)) dto.TakenQuestItems.Add(key); }
        if (floorRoot.GetNodeOrNull<CollectiveRegistry>("Collectives") is { } registry)
            foreach (var c in registry.Collectives)
                dto.Collectives.Add(new CollectiveDto { Id = c.Id, Alert = c.Alert.ToString(), AlarmRaised = c.AlarmRaised, Regrouped = c.Regrouped });
        foreach (var h in floorRoot.GetChildren().OfType<Adventure.HordeSpawner>())
            if (h.Triggered || h.Spent) dto.SpentHordes.Add(h.Horde.Id);
        if (floorRoot.GetChildren().OfType<Adventure.AmbushSpawner>().FirstOrDefault() is { } ambushes) dto.SprungAmbushes.AddRange(ambushes.Sprung.OrderBy(i => i));
        if (floorRoot.GetChildren().OfType<Adventure.PortalSpawner>().FirstOrDefault() is { } portals) { dto.PressureSeconds = portals.PressureSeconds; dto.PortalWaves = portals.Waves; }
        if (floorRoot.GetChildren().OfType<Adventure.Objectives.ObjectiveController>().FirstOrDefault() is { } objective) dto.Objective = objective.Capture();
        dto.BossDefeated = Main.Instance?.BossDefeatedThisFloor == true || floorRoot.GetChildren().OfType<Enemy>().Any(e => e.IsBoss && !e.IsAlive) || (layout.BossArena != null && !floorRoot.GetChildren().OfType<Enemy>().Any(e => e.IsBoss));
        return dto;
    }

    public static void Apply(FloorStateDto dto, Node3D floorRoot, ExplorationMap exploration, PartyController party)
    {
        exploration.Load(dto.Explored.Where(c => c.Length == 2).Select(c => new GridPos(c[0], c[1])));
        var dead = dto.DeadEnemies.ToHashSet();
        var live = dto.Enemies.ToDictionary(e => e.SpawnIndex);
        var opened = dto.OpenedChests.ToHashSet();
        var broken = dto.BrokenBarrels.ToHashSet();
        var triggered = dto.TriggeredTraps.ToHashSet();
        var disarmed = dto.DisarmedTraps.ToHashSet();
        var takenKeys = dto.TakenKeys.ToHashSet();
        var openedVaults = dto.OpenedVaults.ToHashSet();
        var openedSecrets = dto.OpenedSecrets.ToHashSet();
        var takenQuestItems = dto.TakenQuestItems.ToHashSet();
        var npcs = dto.NpcStates.ToDictionary(n => n.Id);
        var hazards = dto.Hazards.ToDictionary(h => (h.Kind, h.Index));

        foreach (var child in floorRoot.GetChildren().ToList())
        {
            switch (child)
            {
                case Enemy e:
                    if (dead.Contains(e.SpawnIndex)) e.QueueFree();
                    else if (live.TryGetValue(e.SpawnIndex, out var s)) e.RestoreFrom(s);
                    break;
                case Chest ch when opened.Contains(ch.SpawnIndex):
                    ch.MarkOpened();
                    break;
                case Breakable b when broken.Contains(b.SpawnIndex):
                    b.QueueFree();
                    break;
                case Trap { PlacedByParty: false } t when triggered.Contains(t.SpawnIndex) || disarmed.Contains(t.SpawnIndex):
                    t.RestoreState(triggered.Contains(t.SpawnIndex), disarmed.Contains(t.SpawnIndex));
                    break;
                case LootDrop { KeyIndex: >= 0 } key when takenKeys.Contains(key.KeyIndex):
                    key.QueueFree();
                    break;
                case LootDrop { QuestId: { } qk } questDrop when takenQuestItems.Contains(qk):
                    questDrop.QueueFree();
                    break;
                case Npc npc when npcs.TryGetValue(npc.Id, out var ns):
                    npc.Restore(ns.Dead, ns.Escorting, ns.Rescued, ns.Hp, new Vector3(ns.X, ns.Y, ns.Z), ns.Downed);
                    break;
                case Door d when !d.IsObjectiveGate && openedVaults.Contains(d.VaultIndex):
                    d.Open();
                    break;
                case Press press when hazards.TryGetValue(((byte)Dungeon.HazardKind.Press, press.Index), out var ps):
                    press.Restore((Dungeon.PressPhase)ps.Phase, ps.PhaseTime, ps.Jammed, ps.Spent);
                    break;
                case SpikeBed bed when hazards.TryGetValue(((byte)Dungeon.HazardKind.SpikeBed, bed.Index), out var bs):
                    bed.Restore((Dungeon.SpikePhase)bs.Phase, bs.PhaseTime, bs.Spent);
                    break;
                case Lever l when !l.IsObjectiveLever && openedVaults.Contains(l.VaultIndex):
                    l.Pull();
                    break;
                case SecretWall sw when openedSecrets.Contains(sw.Index):
                    sw.Open();
                    break;
            }
        }
        foreach (var h in floorRoot.GetChildren().OfType<Adventure.HordeSpawner>())
            if (dto.SpentHordes.Contains(h.Horde.Id)) h.MarkSpent();
        floorRoot.GetChildren().OfType<Adventure.AmbushSpawner>().FirstOrDefault()?.Restore(dto.SprungAmbushes);
        floorRoot.GetChildren().OfType<Adventure.PortalSpawner>().FirstOrDefault()?.Restore(dto.PressureSeconds, dto.PortalWaves);
        if (dto.Objective is { } os)
        {
            floorRoot.GetChildren().OfType<Adventure.Objectives.ObjectiveController>().FirstOrDefault()?.Restore(os);
            if (os.Completed && floorRoot.GetNodeOrNull<Stairs>("DownStairs") is { } sealedStairs) sealedStairs.Unlock();
        }
        if (floorRoot.GetNodeOrNull<CollectiveRegistry>("Collectives") is { } registry)
            foreach (var c in dto.Collectives)
                registry.Find(c.Id)?.Restore(c.Alert, c.AlarmRaised, c.Regrouped);
        foreach (var w in dto.Items)
            party.SpawnLoot(SaveMapper.FromDto(w.Item, Game.Instance.Catalog), new Vector3(w.X, w.Y, w.Z));
        foreach (var t in dto.PlacedTraps)
        {
            if (!Enum.TryParse<TrapKind>(t.Kind, out var kind)) continue;
            var trap = GD.Load<PackedScene>("res://scenes/Props/Trap.tscn").Instantiate<Trap>();
            trap.Kind = kind;
            trap.Floor = Game.Instance.CurrentFloor;
            trap.PlacedByParty = true;
            floorRoot.AddChild(trap);
            trap.GlobalPosition = new Vector3(t.X, t.Y, t.Z);
        }
        if (dto.BossDefeated)
        {
            foreach (var boss in floorRoot.GetChildren().OfType<Enemy>().Where(e => e.IsBoss).ToList()) boss.QueueFree();
            if (floorRoot.GetNodeOrNull<Stairs>("DownStairs") is { } stairs) stairs.Unlock();
        }
    }
}
