using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The prop check's two halves that touch the scene: reading a floor root's props into a <see cref="PropStateDto"/>
/// (server and client read the same way, so their <see cref="PropCheck.Hash"/> agree when the floors agree) and putting
/// a client's props where the server's stand. <see cref="FloorStateCapture.Apply"/> cannot do the second: it frees
/// enemies and spawns loot and placed traps again, which is right for a freshly built floor and wrong for a live one.
/// </summary>
public static class PropSync
{
    public static PropStateDto Capture(Node3D root)
    {
        var p = new PropStateDto();
        foreach (var child in root.GetChildren())
        {
            switch (child)
            {
                case Chest ch:
                    if (ch.Opened) p.OpenedChests.Add(ch.SpawnIndex);
                    else
                    {
                        if (ch.Sprung) p.SprungChests.Add(ch.SpawnIndex);
                        if (ch.Jammed) p.JammedChests.Add(ch.SpawnIndex); // an opened jammed chest is simply open (a joiner's never jammed)
                    }
                    break;
                case Door d:
                    if (d.IsOpen) p.OpenDoors.Add(d.VaultIndex);
                    if (d.Detected) p.RevealedDoors.Add(d.VaultIndex);
                    break;
                case Lever l when l.Pulled:
                    p.PulledLevers.Add(l.VaultIndex);
                    break;
                case SecretWall s:
                    if (s.Detected) p.RevealedSecrets.Add(s.Index);
                    if (s.IsOpen) p.OpenSecrets.Add(s.Index);
                    break;
                case Trap { PlacedByParty: true } placed:
                    if (placed.Armed && !placed.IsQueuedForDeletion()) p.PlacedTraps.Add(new PlacedTrapDto { Id = placed.SpawnIndex, Kind = placed.Kind.ToString(), X = placed.GlobalPosition.X, Y = placed.GlobalPosition.Y, Z = placed.GlobalPosition.Z });
                    break;
                case Trap t when t.SpawnIndex >= 0:
                    if (t.Detected) p.RevealedTraps.Add(t.SpawnIndex);
                    if (t.Triggered) p.TriggeredTraps.Add(t.SpawnIndex);
                    else if (t.Disarmed) p.DisarmedTraps.Add(t.SpawnIndex);
                    break;
                case Breakable b when b.SpawnIndex >= 0 && b.IsAlive && !b.IsQueuedForDeletion():
                    p.Barrels.Add(b.SpawnIndex);
                    break;
                case Shrine { Used: true }:
                    p.ShrineUsed = true;
                    break;
            }
        }
        foreach (var list in new[] { p.OpenedChests, p.SprungChests, p.JammedChests, p.OpenDoors, p.RevealedDoors, p.PulledLevers, p.RevealedSecrets, p.OpenSecrets, p.RevealedTraps, p.DisarmedTraps, p.TriggeredTraps, p.Barrels })
            list.Sort();
        p.PlacedTraps.Sort((a, b) => a.Id.CompareTo(b.Id));
        return p;
    }

    /// <summary>
    /// Moves this floor's props to the server's state, silently (the server already said and played everything) and in
    /// both directions where the prop allows it: doors open and shut, levers go up and down, floor traps re-arm or spring,
    /// the party's traps appear or go. A chest cannot close, a broken barrel cannot stand again, an opened secret cannot
    /// shut: those only go forward. Returns what it changed, for the log.
    /// </summary>
    public static List<string> ApplyLive(PropStateDto p, Node3D root)
    {
        var changed = new List<string>();
        var opened = p.OpenedChests.ToHashSet();
        var sprung = p.SprungChests.ToHashSet();
        var jammed = p.JammedChests.ToHashSet();
        var openDoors = p.OpenDoors.ToHashSet();
        var revealedDoors = p.RevealedDoors.ToHashSet();
        var pulled = p.PulledLevers.ToHashSet();
        var revealedSecrets = p.RevealedSecrets.ToHashSet();
        var openSecrets = p.OpenSecrets.ToHashSet();
        var revealedTraps = p.RevealedTraps.ToHashSet();
        var disarmed = p.DisarmedTraps.ToHashSet();
        var triggered = p.TriggeredTraps.ToHashSet();
        var barrels = p.Barrels.ToHashSet();
        var placed = p.PlacedTraps.ToDictionary(t => t.Id);
        var placedShown = new HashSet<int>();

        foreach (var child in root.GetChildren().ToList())
        {
            switch (child)
            {
                case Chest ch:
                    if (opened.Contains(ch.SpawnIndex) && !ch.Opened) { ch.MarkOpened(); changed.Add($"chest {ch.SpawnIndex} opened"); }
                    else if (!ch.Opened)
                    {
                        if (sprung.Contains(ch.SpawnIndex) && !ch.Sprung) { ch.ShowSprung(); changed.Add($"chest {ch.SpawnIndex} sprung"); }
                        if (jammed.Contains(ch.SpawnIndex) && !ch.Jammed) { ch.ShowJammed(); changed.Add($"chest {ch.SpawnIndex} jammed"); }
                    }
                    break;
                case Door d:
                {
                    var open = openDoors.Contains(d.VaultIndex);
                    if (open != d.IsOpen) { d.SetOpen(open, silent: true); changed.Add($"door {d.VaultIndex} {(open ? "opened" : "closed")}"); }
                    if (!open && revealedDoors.Contains(d.VaultIndex) && !d.Detected) { d.Reveal(); changed.Add($"door {d.VaultIndex} revealed"); }
                    break;
                }
                case Lever l:
                {
                    var up = pulled.Contains(l.VaultIndex);
                    if (up != l.Pulled) { l.SetPulled(up); changed.Add($"lever {l.VaultIndex} {(up ? "pulled" : "reset")}"); }
                    break;
                }
                case SecretWall s:
                    if (openSecrets.Contains(s.Index) && !s.IsOpen) { s.Open(silent: true); changed.Add($"secret {s.Index} opened"); }
                    else if (revealedSecrets.Contains(s.Index) && !s.Detected) { s.Reveal(); changed.Add($"secret {s.Index} revealed"); }
                    break;
                case Trap { PlacedByParty: true } mine:
                    if (!mine.Armed || mine.IsQueuedForDeletion()) break;
                    if (placed.ContainsKey(mine.SpawnIndex) && placedShown.Add(mine.SpawnIndex)) break;
                    mine.RestoreState(triggered: true, disarmed: false); // sprung on the server, or never there, or shown twice: gone from view
                    mine.QueueFree();
                    changed.Add($"placed trap {mine.SpawnIndex} gone");
                    break;
                case Trap t when t.SpawnIndex >= 0:
                {
                    var trig = triggered.Contains(t.SpawnIndex);
                    var dis = !trig && disarmed.Contains(t.SpawnIndex);
                    if (trig != t.Triggered || dis != t.Disarmed) { t.RestoreState(trig, dis); changed.Add($"trap {t.SpawnIndex} {(trig ? "sprung" : dis ? "disarmed" : "armed")}"); }
                    if (revealedTraps.Contains(t.SpawnIndex) && !t.Detected) { t.Reveal(); changed.Add($"trap {t.SpawnIndex} revealed"); }
                    break;
                }
                case Breakable b when b.SpawnIndex >= 0 && b.IsAlive && !barrels.Contains(b.SpawnIndex):
                    b.Vanish();
                    changed.Add($"barrel {b.SpawnIndex} broken");
                    break;
                case Shrine sh when p.ShrineUsed && !sh.Used:
                    sh.MarkUsed(silent: true);
                    changed.Add("shrine used");
                    break;
            }
        }
        foreach (var (id, t) in placed)
        {
            if (placedShown.Contains(id) || !Enum.TryParse<TrapKind>(t.Kind, out var kind)) continue;
            SpawnPlaced(root, t, kind);
            changed.Add($"placed trap {id} set");
        }
        return changed;
    }

    /// <summary>A trap the party set, as the server described it (TrapPlacedEvt, or the prop check).</summary>
    public static Trap SpawnPlaced(Node3D root, PlacedTrapDto dto, TrapKind kind)
    {
        var trap = GD.Load<PackedScene>("res://scenes/Props/Trap.tscn").Instantiate<Trap>();
        trap.Kind = kind;
        trap.Floor = Game.Instance.CurrentFloor;
        trap.PlacedByParty = true;
        trap.SpawnIndex = dto.Id;
        root.AddChild(trap);
        trap.GlobalPosition = new Vector3(dto.X, dto.Y, dto.Z);
        return trap;
    }
}
