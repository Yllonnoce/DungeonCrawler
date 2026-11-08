namespace DungeonCrawler.Adventure.Run;

/// <summary>The death card as the server decides it and a client shows it: the epitaph, what did it, where, how many times here, whether the run is over, and the button's word.</summary>
public sealed record DeathCardModel(string Epitaph, string Cause, string Killer, int Floor, int WipesOnFloor, bool RunOver, string Button);

/// <summary>The run's clocks and gates: the solo autosave, the floor card's stay, the Scar's lifting, the death card's line. Engine-free.</summary>
public static class RunRules
{
    /// <summary>Solo autosaves to the auto slot every floor change and every three minutes.</summary>
    public const float AutosaveSeconds = 180f;
    /// <summary>The floor card stays this long (Esc dismisses it sooner).</summary>
    public const float FloorCardSeconds = 4f;
    public const string RiseButton = "Rise", RecordsButton = "To the Records";

    /// <summary>Never while a floor is being rebuilt, while paused, on a server or a client, under a card, or once the run is over.</summary>
    public static bool AutosaveDue(float sinceLast, bool paused, bool rebuilding, bool networked, bool cardUp, bool runOver)
        => sinceLast >= AutosaveSeconds && !paused && !rebuilding && !networked && !cardUp && !runOver;

    /// <summary>The Scar (earned on floor untilFloor − 1) lifts the moment the party stands on any other floor; 0 is no scar.</summary>
    public static bool ScarLifted(int untilFloor, int floor) => untilFloor > 0 && floor != untilFloor - 1;

    /// <summary>"The Press did it, on floor 9. Wipe 4 on this floor."</summary>
    public static string DeathLine(DeathCardModel m)
    {
        var who = HarmCauses.Describe(m.Cause, m.Killer);
        return $"{char.ToUpperInvariant(who[0])}{who[1..]} did it, on floor {m.Floor}. Wipe {m.WipesOnFloor} on this floor.";
    }
}
