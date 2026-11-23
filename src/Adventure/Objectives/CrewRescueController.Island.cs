using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>
/// Home and lost on a floor with an exit island (plan 15 Task 7, Rulings 32-37; server and solo for every change, a client only stands the chest):
/// home is anywhere in the island (<see cref="FloorLayout.InIsland"/> for the exit room), or seated in a coupled party wagon held at the last stop; once
/// every up player is inside with the train at the end or wrecked inside, the unopened cages are left behind (<see cref="ResolveUnopened"/>); a crew
/// member on foot outside with nobody outside to fetch it is lost while that lasts (<see cref="CrewRules.Stranded"/>, judged live until the win);
/// Grisel left outside is brought in beside the held train (<see cref="BringEscortsIn"/>); and the win stands a strongbox of the vault's table by the
/// stairs (<see cref="StandRewardChest"/>), saved in <c>ObjectiveStateDto.RewardChest</c> and told to clients as <c>PropEvt chest:9000 "stood"</c>.
/// </summary>
public partial class CrewRescueController
{
    private const string ChestScene = "res://scenes/Props/Chest.tscn";

    /// <summary>The floor's line has an exit island (floor 12): the island's rules replace the exit room's.</summary>
    private bool IslandFloor => Layout.Rail?.Island != null;

    // ---- the island as this tick reads it ----------------------------------------------------------------------------------------

    private int _upPlayers, _playersOutside;
    private bool _trainAtEnd, _wreckedInside, _noTrain;

    /// <summary>The train held at the last stop, or wrecked past the exit bridge's start (Ruling 11: for the crew, the train at the end).</summary>
    private bool TrainAtEndOrWreckedInside => _trainAtEnd || _wreckedInside;

    /// <summary>
    /// The players (every human's hero that is not parked or fallen): how many are up (not down), and how many stand outside the island, a downed
    /// one too (it may be revived there: fix round 1, I2); and where the train is: held at the last stop, wrecked inside, or gone (wrecked and
    /// hidden). Read once a tick and at each judging. No allocation.
    /// </summary>
    private void ReadIsland()
    {
        _upPlayers = _playersOutside = 0;
        if (Main.Instance?.Party is { } party && IsInstanceValid(party))
            foreach (var hero in party.Adventurers)
            {
                if (!IsInstanceValid(hero) || hero.Parked || hero.State.Fallen || hero.Control is not (ControlKind.LocalHuman or ControlKind.Remote)) continue;
                if (!hero.Downed) _upPlayers++;
                if (!Layout.InIsland(DungeonBuilder.WorldToCell(hero.GlobalPosition))) _playersOutside++;
            }
        var rails = _rails != null && IsInstanceValid(_rails) ? _rails : null;
        _trainAtEnd = rails != null && rails.State.Mode == CartMode.Held && rails.StopIndex >= rails.StopS.Length - 1;
        _wreckedInside = rails != null && rails.State.Mode == CartMode.Wrecked && rails.LastWreckInside;
        _noTrain = rails != null && rails.State.Mode == CartMode.Wrecked && !rails.Cart.Visible;
    }

    /// <summary>
    /// Ruling 32 and 34: a crew member out of its cage, alive, judged by <see cref="CrewRules.JudgeOnIsland"/>: in the island means on foot on an
    /// island cell (a lift's tub is never home); <paramref name="seatedAtEnd"/> is a coupled party wagon held at the last stop. Keeps the home set.
    /// </summary>
    private CrewState JudgeOnIsland(Npc npc, bool seatedAtEnd)
    {
        var inIsland = !npc.Seated && Layout.InIsland(DungeonBuilder.WorldToCell(npc.GlobalPosition));
        var stranded = CrewRules.Stranded(!npc.Seated, inIsland, _trainAtEnd || _noTrain, _upPlayers, _playersOutside);
        var state = CrewRules.JudgeOnIsland(_home.Contains(npc.Id), npc.Downed, inIsland, seatedAtEnd, stranded);
        if (state == CrewState.Home) _home.Add(npc.Id);
        else _home.Remove(npc.Id);
        return state;
    }

    // ---- Ruling 33: the unopened cages ---------------------------------------------------------------------------------------------

    /// <summary>The unopened cages resolved by <see cref="ResolveUnopened"/> on this floor build (whether or not their stop was left already).</summary>
    private readonly HashSet<int> _cagesResolved = new();

    /// <summary>The stops whose unopened cage <see cref="ResolveUnopened"/> resolved (the flows read it: fix round 1, I3).</summary>
    internal IReadOnlyCollection<int> CagesResolved => _cagesResolved;

    /// <summary>
    /// Ruling 33 (server and solo): once <see cref="CrewRules.UnopenedLeftBehind"/> holds, each cage still locked is resolved: left behind at its stop
    /// (<see cref="RailController.LeaveCrewBehindAt"/>; its crew count as lost and it no longer blocks the stairs), printed only when that added the stop
    /// (one a restore or a waiver left already is resolved silently). A takeover added no stop on its way past (Ruling 27): they are resolved here, at the end.
    /// </summary>
    private void ResolveUnopened()
    {
        if (_rails == null || !IsInstanceValid(_rails) || !CrewRules.UnopenedLeftBehind(_upPlayers, _playersOutside, TrainAtEndOrWreckedInside)) return;
        foreach (var cage in _cages)
        {
            if (!IsInstanceValid(cage) || cage.Open || !_cagesResolved.Add(cage.Stop)) continue;
            if (!_rails.LeaveCrewBehindAt(cage.Stop)) continue;
            GD.Print($"[CrewRescue] island: cage at stop {cage.Stop} left behind (never opened)");
            RefreshHud();
        }
    }

    // ---- Ruling 36: Grisel brought in ------------------------------------------------------------------------------------------------

    /// <summary>The bring-in condition held at the last tick: she is set down once each time it starts.</summary>
    private bool _escortInHeld;

    /// <summary>The line logged when Grisel is brought in (a log line, not a bark).</summary>
    public const string GriselInLine = "Grisel picks her way in over the bridge behind you.";

    /// <summary>
    /// Ruling 36 (server and solo, every tick, won or not): Grisel alive, up, escorting and on foot outside the island, with a human up, no player outside and
    /// the train held at the end or wrecked inside (<see cref="CrewRules.BringEscortIn"/>), is set down on the island's set-down spot nearest the held train's
    /// last body (after a wreck inside, beside the last stop's platform), once each time the condition starts; the log tells it.
    /// </summary>
    private void BringEscortsIn()
    {
        var g = _grisel != null && IsInstanceValid(_grisel) ? _grisel : null;
        var outside = g is { Dead: false, Downed: false, Escorting: true, Seated: false } && !Layout.InIsland(DungeonBuilder.WorldToCell(g.GlobalPosition));
        if (!CrewRules.BringEscortIn(outside, _upPlayers, _playersOutside, TrainAtEndOrWreckedInside) || _rails == null || !IsInstanceValid(_rails))
        {
            _escortInHeld = false;
            return;
        }
        if (_escortInHeld) return;
        _escortInHeld = true;
        var near = EscortInNear();
        // the island's set-down spot nearest it (RailEncounterController.IslandSetDown: island cells, never a drop, a prop or a barrel's cell, here never a rail; fix round 1:
        // a lane-side cell could be a barrel's, and she stood wedged in it)
        var at = near;
        var best = float.MaxValue;
        if (RailEncounterController.Instance is { } enc && IsInstanceValid(enc))
            foreach (var spot in enc.IslandSetDown(IslandSpotsLooked))
                if (!Layout.IsRail(DungeonBuilder.WorldToCell(spot)) && Flat(spot, near) is var d && d < best) { best = d; at = spot; } // never the line, where the train stands
        if (best == float.MaxValue) at = _rails.StandBesideLane(near);
        at += Vector3.Up * 0.1f;
        g!.GlobalPosition = at;
        g.Velocity = Vector3.Zero;
        g.FaceToward(near);
        EventBus.Instance.Log(GriselInLine);
        GD.Print($"[CrewRescue] island: {g.NpcName} brought in beside the held train at {DungeonBuilder.WorldToCell(at)}");
    }

    /// <summary>How many of the island's set-down spots (nearest the lane's first cell past the bridge) the bring-in looks among.</summary>
    private const int IslandSpotsLooked = 24;

    /// <summary>Where an escort is brought in beside: the held train's last body (its last coupled wagon, else the cart), or after a wreck inside the last stop's platform.</summary>
    private Vector3 EscortInNear()
    {
        var rails = _rails!;
        if (_trainAtEnd)
        {
            TrainWagon? last = null;
            foreach (var w in rails.Train) if (IsInstanceValid(w) && w.Coupled) last = w;
            return last?.GlobalPosition ?? rails.Cart.GlobalPosition;
        }
        var stop = Layout.Rail!.Stops[^1];
        return _builder!.Surface(stop.Platform.Count > 0 ? stop.Platform[stop.Platform.Count / 2] : Layout.DownStairs);
    }

    // ---- Ruling 37: the reward chest -------------------------------------------------------------------------------------------------

    private Chest? _rewardChest;

    /// <summary>The win's reward chest standing on this floor (server, solo and client), or null.</summary>
    internal Chest? RewardChest => _rewardChest != null && IsInstanceValid(_rewardChest) ? _rewardChest : null;

    /// <summary>The log line the win reads on an island floor, in the vault door's line's place.</summary>
    public const string RewardChestLine = "The prisoners point you to a strongbox by the stairs: the Founders' share is yours.";

    /// <summary>
    /// The win (server and solo): the chest stands, clients are told (<c>PropEvt chest:9000 "stood"</c>) and the log says where; one kept standing (a
    /// win a wipe took back, won again) is only pointed to. With no free exit cell for it the vault door's line is logged instead (fix round 1, M9).
    /// </summary>
    private void StandRewardChestOnWin()
    {
        if (StandRewardChest() != null) EventBus.Instance.RaisePropChanged(NetRefs.Chest(CrewRules.RewardChestIndex), "stood");
        EventBus.Instance.Log(RewardChest != null ? RewardChestLine : "Somewhere on this floor, a vault door swings wide for you.");
    }

    /// <summary>Client: the server stood the reward chest (its <c>PropEvt</c>, the floor state at a build, or the prop state's catch-up). True when it changed.</summary>
    public bool ShowRewardChest() => IslandFloor && StandRewardChest() != null;

    /// <summary>
    /// The reward chest (Ruling 37): a <see cref="Chest"/> of the vault's table (<see cref="ChestRules.VaultTable"/>), <see cref="CrewRules.RewardChestIndex"/>,
    /// never a mimic, never locked, on the exit room's first free cell nearest the stairs (<see cref="ExitCells"/>: no line cell, nothing standing
    /// there), never the flight's foot (the cell the stairs face, where the way down starts), the same on every peer. The cell is blocked and kept clear of riders getting out. Null when it stands already or there is no cell.
    /// </summary>
    private Chest? StandRewardChest()
    {
        if (RewardChest != null || _exitRoom == null || _builder == null || _root == null) return null;
        var front = DungeonBuilder.WalkableNeighbor(Layout, Layout.DownStairs); // the flight's own foot (StairsTransform faces it): never taken, or the stairs are walled off
        var cells = ExitCells();
        cells.Remove(front);
        if (cells.Count == 0) { GD.PushWarning("[CrewRescue] the exit room has no free cell for the reward chest"); return null; }
        var cell = cells[0];
        var chest = GD.Load<PackedScene>(ChestScene).Instantiate<Chest>();
        chest.SpawnIndex = CrewRules.RewardChestIndex;
        chest.TableId = ChestRules.VaultTable;
        chest.IsMimic = false;
        chest.Locked = false;
        chest.Name = "RewardChest";
        _root.AddChild(chest);
        chest.GlobalPosition = _builder.Surface(cell);
        _builder.BlockCell(cell);
        _rails?.ReserveSiding(cell);
        _rewardChest = chest;
        var moved = NetSession.IsClient ? 0 : ClearChestCell(cell);
        GD.Print($"[CrewRescue] the reward chest stands in room {Layout.Rail!.Stops[^1].Room} at {cell} (table {chest.TableId}){(moved > 0 ? $"; {moved} body(ies) stepped off its cell" : "")}");
        return chest;
    }

    /// <summary>
    /// Fix round 1 (M7), server and solo: every body on foot standing on the chest's <paramref name="cell"/> (a hero, an NPC, a creature) steps off to the
    /// nearest free cell round it, so the solid chest never stands on anyone. The cell itself stays the rule's (build-time state only), so every peer
    /// stands its chest on the same cell. Returns how many moved.
    /// </summary>
    private int ClearChestCell(GridPos cell)
    {
        if (_root == null || _builder == null) return 0;
        var moved = 0;
        Vector3 Off() => _builder.Surface(FreeCellsNear(cell, 1)[0]) + Vector3.Up * 0.1f;
        if (Main.Instance?.Party is { } party && IsInstanceValid(party))
            foreach (var hero in party.Adventurers)
            {
                if (!IsInstanceValid(hero) || hero.Parked || hero.Seated || DungeonBuilder.WorldToCell(hero.GlobalPosition) != cell) continue;
                var at = Off();
                hero.Teleport(at);
                if (hero.Remote != null) { hero.Remote.Position = at; hero.Remote.Velocity = Vector3.Zero; }
                moved++;
            }
        foreach (var child in _root.GetChildren())
        {
            if (child is Npc { Seated: true } || child is not (Npc or Enemies.Enemy) || child is not CharacterBody3D body || DungeonBuilder.WorldToCell(body.GlobalPosition) != cell) continue;
            body.GlobalPosition = Off();
            body.Velocity = Vector3.Zero;
            moved++;
        }
        return moved;
    }

    // ---- test hooks ------------------------------------------------------------------------------------------------------------------

    /// <summary>Test hook (server and solo; plan 15 Task 7): crew member <paramref name="crewId"/> goes down where it sits or stands. Nothing on a client or for an unknown or dead one.</summary>
    internal void DebugDown(string crewId)
    {
        if (NetSession.IsClient || _crew.FirstOrDefault(n => IsInstanceValid(n) && n.Id == crewId) is not { Dead: false } npc) return;
        npc.GoDown();
        GD.Print($"[CrewRescue] test: {npc.NpcName} downed at {DungeonBuilder.WorldToCell(npc.GlobalPosition)} (seated {npc.Seated})");
        RefreshHud();
    }
}
