namespace DungeonCrawler.Adventure.Objectives;

public enum CrewState : byte { Caged, Freed, Downed, Home, Lost }

/// <summary>The railed jailbreak's crew arithmetic: who is out, who is home, who is lost, and when the floor is won. Engine-free.</summary>
public static class CrewRules
{
    /// <summary>A downed crew member bleeds out after this long unless revived.</summary>
    public const float BleedOutSeconds = 60f;
    /// <summary>Share of max health a revived crew member gets back.</summary>
    public const float ReviveHpFraction = 0.35f;
    /// <summary>Crew carried by one cage wagon.</summary>
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

    public static string HudText(IReadOnlyList<CrewState> crew)
    {
        int freed = 0, home = 0, lost = 0;
        foreach (var s in crew)
        {
            if (s != CrewState.Caged) freed++;
            if (s == CrewState.Home) home++;
            if (s == CrewState.Lost) lost++;
        }
        var text = $"Crew freed {freed}/{crew.Count}, home {home}";
        if (lost > 0) text += $", {lost} lost";
        return text + ".";
    }

    public static bool StopCleared(int aliveInStopCollective) => aliveInStopCollective <= 0;

    /// <summary>The CartInput.CrewWagons value: one coupled wagon is one load step.</summary>
    public static float LoadFactorWagons(int coupledWagons) => coupledWagons;

    /// <summary>
    /// How far a cage wagon's crew member's name rises above its usual place (m), by the bench seat it stands at (0 the first,
    /// 1 the second: the seat book's seat less <c>CartSeats.HeroSeats</c>): the two names over one cage stand apart.
    /// </summary>
    public static float LabelLift(int benchIndex) => benchIndex * 0.32f;
}
