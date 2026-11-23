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

    private int _upPlayers, _upPlayersInside;
    private bool _trainAtEnd, _wreckedInside, _noTrain;

    /// <summary>The train held at the last stop, or wrecked past the exit bridge's start (Ruling 11: for the crew, the train at the end).</summary>
    private bool TrainAtEndOrWreckedInside => _trainAtEnd || _wreckedInside;

    /// <summary>
    /// The up players (every human's hero that is not parked, down or fallen) and how many stand in the island, and where the train is: held at the last
    /// stop, wrecked inside, or gone (wrecked and hidden). Read once a tick and at each judging. No allocation.
    /// </summary>
    private void ReadIsland()
    {
        _upPlayers = _upPlayersInside = 0;
        if (Main.Instance?.Party is { } party && IsInstanceValid(party))
            foreach (var hero in party.Adventurers)
            {
                if (!IsInstanceValid(hero) || hero.Parked || hero.Downed || hero.State.Fallen || hero.Control is not (ControlKind.LocalHuman or ControlKind.Remote)) continue;
                _upPlayers++;
                if (Layout.InIsland(DungeonBuilder.WorldToCell(hero.GlobalPosition))) _upPlayersInside++;
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
        var stranded = CrewRules.Stranded(!npc.Seated, inIsland, _trainAtEnd || _noTrain, _upPlayers - _upPlayersInside);
        var state = CrewRules.JudgeOnIsland(_home.Contains(npc.Id), npc.Downed, inIsland, seatedAtEnd, stranded);
        if (state == CrewState.Home) _home.Add(npc.Id);
        else _home.Remove(npc.Id);
        return state;
    }

    // ---- Ruling 33: the unopened cages ---------------------------------------------------------------------------------------------

    /// <summary>The unopened cages already resolved (printed once each).</summary>
    private readonly HashSet<int> _cagesResolved = new();

    /// <summary>
    /// Ruling 33 (server and solo): once <see cref="CrewRules.UnopenedLeftBehind"/> holds, each cage still locked is left behind at its stop
    /// (<see cref="RailController.LeaveCrewBehindAt"/>; its crew count as lost and it no longer blocks the stairs), printed once each. A takeover added no
    /// stop on its way past (Ruling 27): they are resolved here, at the end.
    /// </summary>
    private void ResolveUnopened()
    {
        if (_rails == null || !IsInstanceValid(_rails) || !CrewRules.UnopenedLeftBehind(_upPlayers, _upPlayersInside, TrainAtEndOrWreckedInside)) return;
        foreach (var cage in _cages)
        {
            if (!IsInstanceValid(cage) || cage.Open || !_cagesResolved.Add(cage.Stop)) continue;
            _rails.LeaveCrewBehindAt(cage.Stop);
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
    /// Ruling 36 (server and solo, every tick, won or not): Grisel alive, up, escorting and on foot outside the island, with no up player outside and
    /// the train held at the end or wrecked inside (<see cref="CrewRules.BringEscortIn"/>), is set down on the island's cell beside the held train's
    /// last body (after a wreck inside, beside the last stop's platform), once each time the condition starts; the log tells it.
    /// </summary>
    private void BringEscortsIn()
    {
        var g = _grisel != null && IsInstanceValid(_grisel) ? _grisel : null;
        var outside = g is { Dead: false, Downed: false, Escorting: true, Seated: false } && !Layout.InIsland(DungeonBuilder.WorldToCell(g.GlobalPosition));
        if (!CrewRules.BringEscortIn(outside, _upPlayers - _upPlayersInside, TrainAtEndOrWreckedInside) || _rails == null || !IsInstanceValid(_rails))
        {
            _escortInHeld = false;
            return;
        }
        if (_escortInHeld) return;
        _escortInHeld = true;
        var near = EscortInNear();
        var at = _rails.StandBesideLane(near, inside: true);
        g!.GlobalPosition = at;
        g.Velocity = Vector3.Zero;
        g.FaceToward(near);
        EventBus.Instance.Log(GriselInLine);
        GD.Print($"[CrewRescue] island: {g.NpcName} brought in beside the held train at {DungeonBuilder.WorldToCell(at)}");
    }

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

    /// <summary>The win (server and solo): the chest stands, clients are told (<c>PropEvt chest:9000 "stood"</c>) and the log says where.</summary>
    private void StandRewardChestOnWin()
    {
        if (StandRewardChest() == null) return;
        EventBus.Instance.RaisePropChanged(NetRefs.Chest(CrewRules.RewardChestIndex), "stood");
        EventBus.Instance.Log(RewardChestLine);
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
        GD.Print($"[CrewRescue] the reward chest stands in room {Layout.Rail!.Stops[^1].Room} at {cell} (table {chest.TableId})");
        return chest;
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
