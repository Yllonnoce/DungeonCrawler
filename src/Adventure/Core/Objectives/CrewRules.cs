namespace DungeonCrawler.Adventure.Objectives;

public enum CrewState : byte { Caged, Freed, Downed, Home, Lost }

/// <summary>The railed jailbreak's crew arithmetic: who is out, who is home, who is lost, and when the floor is won. Engine-free.</summary>
public static class CrewRules
{
    /// <summary>A downed crew member bleeds out after this long unless revived.</summary>
    public const float BleedOutSeconds = 60f;
    /// <summary>Share of max health a revived crew member gets back.</summary>
    public const float ReviveHpFraction = 0.35f;
    /// <summary>Crew caged in one platform cage (two per cage; plan 14: the train has a wagon per cage, two crew seats in each).</summary>
    public const int PerWagon = 2;
    /// <summary>The dead Grisel's NPC id on the condemned branch: no crew member may take it.</summary>
    public const string GriselBodyId = "grisel_body";

    /// <summary>No cage blocking (<paramref name="cagesClosed"/>: <see cref="CagesBlocking"/>) and every crew member Home or Lost. An empty crew means not set up yet.</summary>
    public static bool Completed(IReadOnlyList<CrewState> crew, int cagesClosed)
    {
        if (crew.Count == 0 || cagesClosed != 0) return false;
        foreach (var s in crew)
            if (s is CrewState.Caged or CrewState.Freed or CrewState.Downed) return false;
        return true;
    }

    /// <summary>A caged crew member's state: Lost once a player left their cage behind (plan 7), else still Caged.</summary>
    public static CrewState JudgeCaged(bool leftBehind) => leftBehind ? CrewState.Lost : CrewState.Caged;

    /// <summary>The cages that still block the win: neither open nor left behind (a left cage's crew count as lost).</summary>
    public static int CagesBlocking(IReadOnlyList<(bool Open, bool LeftBehind)> cages)
    {
        var n = 0;
        foreach (var (open, leftBehind) in cages)
            if (!open && !leftBehind) n++;
        return n;
    }

    /// <summary>
    /// The crew line. "Freed" counts only crew whose cage stands open (or who had none): <paramref name="uncaged"/>, by crew index
    /// (null: everyone), so crew who died still caged, or whose cage was left behind (plan 7), are lost but never freed.
    /// </summary>
    public static string HudText(IReadOnlyList<CrewState> crew, IReadOnlyList<bool>? uncaged = null)
    {
        int freed = 0, home = 0, lost = 0;
        for (var i = 0; i < crew.Count; i++)
        {
            var s = crew[i];
            if (s != CrewState.Caged && (uncaged == null || i >= uncaged.Count || uncaged[i])) freed++;
            if (s == CrewState.Home) home++;
            if (s == CrewState.Lost) lost++;
        }
        var text = $"Crew freed {freed}/{crew.Count}, home {home}";
        if (lost > 0) text += $", {lost} lost";
        return text + ".";
    }

    /// <summary>
    /// A load (plan 14 Task 2): an open cage's crew member not seated in the train stays standing in its cage only when it went down in
    /// there, never let out to follow (<paramref name="escorting"/> false); one downed on foot keeps following and lies where the save had it.
    /// </summary>
    public static bool StaysInOpenCage(bool downed, bool escorting) => downed && !escorting;

    public static bool StopCleared(int aliveInStopCollective) => aliveInStopCollective <= 0;

    /// <summary>The CartInput.CrewWagons value: one coupled wagon is one load step.</summary>
    public static float LoadFactorWagons(int coupledWagons) => coupledWagons;

    /// <summary>
    /// How far a caged (or seated) crew member's name rises above its usual place (m), by the bench seat it holds (0 the first,
    /// 1 the second: the seat book's seat less <c>CartSeats.HeroSeats</c>): the two names over one cage or wagon stand apart.
    /// </summary>
    public static float LabelLift(int benchIndex) => benchIndex * 0.32f;
    // ---- plan 15 Task 7: home and lost on an island floor (Rulings 32-37) ----------------------------------------------------------

    /// <summary>The reward chest's <c>SpawnIndex</c> (Ruling 37): clear of every floor's own chests, cellars and extras.</summary>
    public const int RewardChestIndex = 9000;

    /// <summary>
    /// How one hero counts for the island's rules (fix round 3): <c>Up</c> for a human's hero that is not parked, fallen or down; <c>Outside</c> for one
    /// not parked or fallen standing off the island's cells, down or not. A downed one (a Mangled one too: it can be pulled free or revived where it
    /// lies) is not up but holds the outside; a fallen one (only the crevasse's return brings it back), a parked one or a companion counts as neither.
    /// A Mangled hero is always down in play (Mangled is set only at 0 HP); <paramref name="mangled"/> alone, with <paramref name="downed"/> false, still
    /// counts as not up (and outside when outside), as a downed one does (Task 9, the Task 7 re-review 3's Minor 3).
    /// </summary>
    public static (bool Up, bool Outside) PlayerCount(bool human, bool parked, bool fallen, bool downed, bool mangled, bool inIsland)
    {
        if (!human || parked || fallen) return (false, false);
        return (!downed && !mangled, !inIsland);
    }

    /// <summary>
    /// A crew member on foot outside the island (<paramref name="onFoot"/>, not <paramref name="inIsland"/>) with the train at the end of the line
    /// or gone (<paramref name="trainAtEndOrNone"/>: wrecked and hidden), at least one human up (<paramref name="upPlayers"/>), and no player outside
    /// to fetch it (<paramref name="playersOutside"/>: <see cref="PlayerCount"/>'s Outside, a downed or Mangled one too, who may be revived or pulled
    /// free there) (Ruling 34, fix rounds 1 and 3): it counts as lost while that lasts.
    /// </summary>
    public static bool Stranded(bool onFoot, bool inIsland, bool trainAtEndOrNone, int upPlayers, int playersOutside)
        => onFoot && !inIsland && trainAtEndOrNone && upPlayers > 0 && playersOutside <= 0;

    /// <summary>
    /// Ruling 33: the unopened cages are left behind once at least one human is up, no player is outside the island (<paramref name="playersOutside"/>,
    /// a downed or Mangled one counted: <see cref="PlayerCount"/>), and the train is held at the last stop or wrecked inside.
    /// </summary>
    public static bool UnopenedLeftBehind(int upPlayers, int playersOutside, bool trainAtEndOrWreckedInside)
        => upPlayers > 0 && playersOutside <= 0 && trainAtEndOrWreckedInside;

    /// <summary>
    /// Ruling 36: an escort (Grisel) left outside is brought in once at least one human is up and no player is outside (a downed or Mangled one counted), with
    /// the train at the end or wrecked inside.
    /// </summary>
    public static bool BringEscortIn(bool escortOutside, int upPlayers, int playersOutside, bool trainAtEndOrWreckedInside)
        => escortOutside && upPlayers > 0 && playersOutside <= 0 && trainAtEndOrWreckedInside;

    /// <summary>
    /// Ruling 35: a follower's catch-up (its set-down beside its lead, or beside a seated lead's cart) never crosses the island's edge: off an
    /// island floor always, on one only with the follower, its lead and the spot it would be set down on all on one side (a bridge cell is outside).
    /// </summary>
    public static bool CatchUpAllowed(bool islandFloor, bool selfInIsland, bool leadInIsland, bool destInIsland)
        => !islandFloor || (selfInIsland == leadInIsland && leadInIsland == destInIsland);

    /// <summary>
    /// A crew member's state on an island floor (Rulings 32 and 34), once the caged and the dead are judged: Home when already home and up or
    /// in the island; else Downed when down; else Home when in the island or seated in the party's wagon at the end; else Lost while
    /// <paramref name="stranded"/>; else Freed.
    /// </summary>
    public static CrewState JudgeOnIsland(bool homeAlready, bool downed, bool inIsland, bool seatedAtEnd, bool stranded)
    {
        if (homeAlready && (!downed || inIsland)) return CrewState.Home;
        if (downed) return CrewState.Downed;
        if (inIsland || seatedAtEnd) return CrewState.Home;
        return stranded ? CrewState.Lost : CrewState.Freed;
    }
}
