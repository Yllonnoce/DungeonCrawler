using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure.Objectives;

/// <summary>What a floor objective triggers, for the villain's taunts, the log and the tests.</summary>
public enum ObjectiveTrigger
{
    FloorEnter, Capture, Freed, AllFreed, GasThickens, GasLethal, WrongLever, GateShift, VaultOpened,
    RitualStart, RitualistDown, WaveBreak, RitualDone, ObjectiveDone, Idle,
}

/// <summary>The words around an objective: the sealed-stairs line, the HUD prefix, the completion line. The villain owns the dungeon, so they name it.</summary>
public static class ObjectiveText
{
    public const string DefaultVillain = "The dungeon's master";

    public static string Villain(string? name) => string.IsNullOrWhiteSpace(name) ? DefaultVillain : name.Trim();

    public static string LockReason(FloorObjectiveKind kind, string? villain) => kind switch
    {
        FloorObjectiveKind.Jailbreak => $"{Villain(villain)} has sealed the stairs. Free every prisoner.",
        FloorObjectiveKind.Ritual => $"{Villain(villain)} has sealed the stairs until the ritual is done.",
        FloorObjectiveKind.ShiftingVault => $"{Villain(villain)} has sealed the stairs. The vault answers only to the levers.",
        _ => "The stairs are sealed until the guardian falls.",
    };

    public static string Completed(FloorObjectiveKind kind) => kind switch
    {
        FloorObjectiveKind.Jailbreak => "The last prisoner is free. The stairs grind open.",
        FloorObjectiveKind.Ritual => "The rite is done. The stairs grind open.",
        FloorObjectiveKind.ShiftingVault => "The gates stand right. The vault opens and the stairs with it.",
        _ => "The way down opens.",
    };

    /// <summary>The HUD prefix: whose trial this is.</summary>
    public static string HudPrefix(string? villain) => $"{Villain(villain)}'s trial:";
}
