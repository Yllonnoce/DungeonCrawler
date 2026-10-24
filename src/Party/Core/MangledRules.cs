namespace DungeonCrawler.Party;

/// <summary>
/// What happens to a party member or ally a lethal hazard caught: they are down and Mangled, which auto-revive never
/// touches. A living body within reach pulls them free once the hazard that took them is armed again; in co-op a body
/// nobody reaches in time is bound further ahead for the party to find. Engine-free.
/// </summary>
public static class MangledRules
{
    public const float RescueSeconds = 3f;
    public const float RescueRange = 2f;
    /// <summary>A rescuer who moves further than this from where they started the rescue interrupts it.</summary>
    public const float RescueInterruptMove = 1f;
    public const float BoundAfterSeconds = 30f;
    public const float ReviveHp = 1f;
    public const float ReviveStress = 25f;

    /// <summary>Calm auto-revive stands a downed member up; a Mangled one waits for hands.</summary>
    public static bool CanAutoRevive(bool mangled, bool hazardArmed) => !mangled;

    /// <summary>The party is wiped only when nobody is standing; a Mangled body counts as down, never as gone.</summary>
    public static bool IsWipe(IReadOnlyList<(bool Downed, bool Mangled)> members)
    {
        if (members.Count == 0) return false;
        foreach (var (downed, _) in members) if (!downed) return false;
        return true;
    }

    public static bool RescueAllowed(bool hazardArmed, float distance, bool rescuerDowned)
        => hazardArmed && distance <= RescueRange && !rescuerDowned;
}
