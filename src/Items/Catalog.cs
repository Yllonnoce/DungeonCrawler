using DungeonCrawler.Core;
using DungeonCrawler.Items.Defs;

namespace DungeonCrawler.Items;

/// <summary>Builds the plain ItemCatalog from every definition Resource the Registry loaded.</summary>
public static class Catalog
{
    public static ItemCatalog FromRegistry(Registry registry)
    {
        var cat = new ItemCatalog();
        foreach (var d in registry.All<ItemDef>()) cat.Bases[d.Id] = d.ToBase();
        foreach (var d in registry.All<AffixDef>()) cat.Affixes[d.Id] = d.ToProfile();
        foreach (var d in registry.All<LegendaryDef>()) cat.Legendaries[d.Id] = d.ToProfile();
        foreach (var d in registry.All<LootTableDef>()) cat.Tables[d.Id] = d.ToProfile();
        return cat;
    }
}
