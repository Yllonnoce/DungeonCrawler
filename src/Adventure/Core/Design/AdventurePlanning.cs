using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Enemies;

namespace DungeonCrawler.Adventure;

/// <summary>Engine-free glue: generate the menu for a floor, ask a designer, validate. Shared by the game and tests.</summary>
public static class AdventurePlanning
{
    public static FloorMenu MenuFor(FloorLayout layout, FloorSpec spec, TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, int floor, PartySnapshot party)
        => FloorMenu.Build(layout, spec, tier, roster, floor, party);

    /// <summary>Design a floor offline and validate it. The report is informational: the plan is always usable.</summary>
    public static (FloorPlan Plan, ValidationReport Report) DesignOffline(FloorLayout layout, FloorSpec spec, TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, int floor, int seed, PartySnapshot party, float difficulty = 1f)
    {
        var menu = MenuFor(layout, spec, tier, roster, floor, party);
        var plan = OfflineDesigner.PlanFloor(menu, layout, seed, difficulty);
        var report = PlanValidator.Validate(layout, plan, menu);
        return (plan, report);
    }

    /// <summary>Resolve a stored plan against a freshly generated layout; null when the plan no longer fits (fall back to legacy).</summary>
    public static FloorContents? ResolveStored(FloorLayout layout, FloorSpec spec, FloorPlan plan, TierProfile? tier, IReadOnlyDictionary<string, EnemyProfile> roster, int floor, int seed, PartySnapshot party, out ValidationReport report, IReadOnlyCollection<string>? flags = null)
    {
        var menu = MenuFor(layout, spec, tier, roster, floor, party);
        report = PlanValidator.Validate(layout, plan, menu, enforceBudget: false);
        if (!report.Ok) return null;
        return PlanResolver.Resolve(layout, plan, tier, roster, floor, seed, flags);
    }
}
