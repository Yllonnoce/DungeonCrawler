using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon;

/// <summary>
/// The crevasses' judge on a floor that has them, on the server and in single player (a client hears of a fall in the party's
/// sheet and the NPC and enemy states). Each physics tick every hero in the world and on foot (a remote player's by the
/// server's copy of its body), every living enemy no cart carries and every NPC alive and on foot is judged with
/// <see cref="CrevasseRules.Fell"/>; one that fell is <see cref="Take"/>n. Also the walking warning: a hero on foot within
/// <see cref="CrevasseRules.WarnCells"/> of a chasm region not yet warned of on this floor visit makes the nearest companion
/// within 12 m (else that hero) call <see cref="BarkTrigger.Crevasse"/>.
/// </summary>
public partial class CrevasseWatch : Node
{
    public static CrevasseWatch? Instance { get; private set; }

    /// <summary>A companion this close to the hero nearing a drop speaks the warning for them.</summary>
    public const float WarnSpeakerReach = 12f;
    public const string WarnLog = "Mind the edge — that drop has no bottom.";

    private DungeonBuilder? _builder;
    private FloorLayout? _layout;
    private Dictionary<GridPos, int> _regions = new();
    private readonly HashSet<int> _warned = new();

    /// <summary>The chasm regions warned of on this floor visit.</summary>
    public int Warned => _warned.Count;

    public static CrevasseWatch Build(DungeonBuilder builder, FloorLayout layout)
        => new() { Name = "CrevasseWatch", _builder = builder, _layout = layout, _regions = CrevasseRules.Regions(layout) };

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _PhysicsProcess(double delta)
    {
        if (Net.NetSession.IsClient || _builder == null || _layout == null || _builder.Layout != _layout || _builder.FloorRoot is not { } root || Main.Instance?.Party is not { HasBodies: true } party) return;
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && !hero.Parked && !hero.State.Fallen && !hero.Seated) Judge(hero);
        foreach (var child in root.GetChildren())
        {
            if (child is Enemy { IsAlive: true, Carried: false } enemy) Judge(enemy);
            else if (child is Npc { Dead: false, Seated: false } npc) Judge(npc);
        }
        Warn(party);
    }

    private void Judge(Node3D body)
    {
        if (_layout == null || _builder == null) return;
        var at = body.GlobalPosition;
        var cell = DungeonBuilder.WorldToCell(at);
        if (!CrevasseRules.IsDrop(_layout, cell)) return;
        if (CrevasseRules.Fell(_layout, cell, at.Y, _builder.Surface(cell).Y)) Take(body, at);
    }

    /// <summary>
    /// <paramref name="body"/> went into the crevasse at <paramref name="at"/> (a fall judged here, or the train's: Task 7). Its fate
    /// (<see cref="CrevasseRules.FateOf"/>, an NPC's by <see cref="CrevasseRules.NpcBody"/>): a hero falls in, held under the lip;
    /// crew and other NPCs die; an escort (Grisel always, escorting or not) is laid on the lip cell (<see cref="CrevasseRules.LipCell"/>),
    /// down but not Mangled, her escort as it was; an enemy dies (no loot). Server and single player only.
    /// </summary>
    public void Take(Node3D body, Vector3 at)
    {
        if (Net.NetSession.IsClient || _layout == null || _builder == null) return;
        var cell = DungeonBuilder.WorldToCell(at);
        var hold = new Vector3(at.X, _builder.Surface(cell).Y - CrevasseRules.HoldDepth, at.Z);
        string who;
        FallFate fate;
        switch (body)
        {
            case Adventurer hero:
                if (hero.State.Fallen) return;
                who = hero.State.Name;
                fate = CrevasseRules.FateOf(FallBody.Hero);
                hero.FallIntoCrevasse(hold);
                break;
            case Npc npc:
                if (npc.Dead) return;
                who = npc.NpcName;
                fate = CrevasseRules.FateOf(CrevasseRules.NpcBody(npc.Crew, npc.Escorting, Adventure.Objectives.CrewRescueController.EscortIds.Contains(npc.Id)));
                if (fate == FallFate.CaughtAtLip)
                {
                    // a fall never kills an escort, nor lays her where she falls again: the lip cell, else the nearest cell anywhere a body
                    // may be set down, else any footing (CrevasseRules.EscortCell); else (a floor with no walkable cell: it should not happen)
                    // the line's stop platform, held there, said as an error
                    Vector3 layAt;
                    if (CrevasseRules.EscortCell(_layout, cell) is { } lip)
                    {
                        layAt = _builder.Surface(lip) + Vector3.Up * 0.1f;
                        who += $" (laid at {lip})";
                    }
                    else
                    {
                        var stops = _layout.Rail?.Stops;
                        var stop = stops is { Count: > 0 } ? stops[Math.Clamp(Rails.RailController.Instance?.StopIndex ?? 0, 0, stops.Count - 1)] : null;
                        var platform = stop is { Platform.Count: > 0 } ? stop.Platform[stop.Platform.Count / 2] : DungeonBuilder.WorldToCell(at);
                        layAt = _builder.Surface(platform) + Vector3.Up * 0.1f;
                        npc.SetPhysicsProcess(false); // held: never judged over a drop again
                        GD.PushError($"[crevasse] ERROR: no walkable cell on the floor to lay {npc.NpcName} at after a fall at {cell}: held on the stop platform at {platform}");
                        who += $" (held on the stop platform at {platform})";
                    }
                    npc.GetOut(layAt); // out of any seat, at the lip, still
                    npc.ReceiveHazard(HazardDamage.Fall, new HazardOutcome(false, 1f, 0f), "The crevasse", -1); // down, revivable, escorting
                }
                else
                {
                    npc.Die();
                    npc.GlobalPosition = hold; // gone into the dark: hidden, held where it fell
                    npc.Velocity = Vector3.Zero;
                    npc.Visible = false;
                    npc.SetPhysicsProcess(false);
                }
                break;
            case Enemy enemy:
                if (!enemy.IsAlive) return;
                who = enemy.DisplayName;
                fate = CrevasseRules.FateOf(FallBody.Enemy);
                enemy.KillByEnvironment("The crevasse", HazardDamage.Fall);
                break;
            default:
                return;
        }
        GD.Print($"[crevasse] {who} fell at {cell}: {fate}");
    }

    private void Warn(PartyController party)
    {
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || hero.Downed || hero.Seated) continue;
            var cell = DungeonBuilder.WorldToCell(hero.GlobalPosition);
            for (var region = CrevasseRules.UnwarnedNear(_regions, cell, _warned); region >= 0; region = CrevasseRules.UnwarnedNear(_regions, cell, _warned))
            {
                _warned.Add(region);
                var speaker = party.Adventurers
                    .Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion && !a.Parked && !a.Downed && a.GlobalPosition.DistanceTo(hero.GlobalPosition) <= WarnSpeakerReach)
                    .OrderBy(a => a.GlobalPosition.DistanceSquaredTo(hero.GlobalPosition)).FirstOrDefault() ?? hero;
                var said = AdventureDirector.Instance?.CallOut(speaker.State, BarkTrigger.Crevasse) == true;
                if (!said) EventBus.Instance.Log(WarnLog);
                GD.Print($"[crevasse] {hero.State.Name} nears chasm region {region} at {cell}: {(said ? $"{speaker.State.Name} warns" : "logged")}");
            }
        }
    }
}
