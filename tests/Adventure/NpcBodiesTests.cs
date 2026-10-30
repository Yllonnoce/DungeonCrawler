using DungeonCrawler.Adventure;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

/// <summary>Quest NPCs, prisoners, ritualists and the villain wear the owner's archetype bodies, chosen from who they are.</summary>
public class NpcBodiesTests
{
    [Theory]
    [InlineData("npc_maud", "Maud Sorrel", "A sleepwalking guide, always dry", "an old woman talking in her sleep", "npc_old_woman")]
    [InlineData("npc_crane", "Alderman Hesketh Crane", "The employer, who follows the party down to check the invoice", "a plump market-town alderman", "npc_merchant")]
    [InlineData("npc_tobit", "Deacon Tobit Hale", "The Verger's junior, sent up to greet the congregation", "an eager young deacon", "npc_priest")]
    [InlineData("npc_grisel", "Grisel Thorn", "Sole survivor of the last four, nineteen years in the Gut", "nineteen years alone underground: flat, wry", "npc_prisoner")]
    [InlineData("npc_ulf", "Old Ulf", "Keeper of the Forge, who will tell you cheerfully that he casts tools", "a jovial old founder, soot in his beard", "npc_old_man")]
    public void TheGulletCast_WearTheirBodies(string id, string name, string role, string style, string expected)
        => Assert.Equal(expected, NpcBodies.For(id, name, false, role, style));

    [Theory]
    [InlineData("Sergeant Wat", "a tired soldier of the watch", "npc_guard")]
    [InlineData("Nell", "a sturdy young woman with a tin cup", "npc_young_woman")]
    [InlineData("Piran", "a freckled lad of twenty", "npc_young_man")]
    [InlineData("Old Wenna", "", "npc_old_man")]
    [InlineData("Mother Hesper", "", "npc_old_woman")]
    [InlineData("Brother Anselm", "", "npc_priest")]
    [InlineData("Hollis Penn", "a company man with a ledger", "npc_young_man")]
    public void Archetypes_FollowTheWordsThatDescribeThem(string name, string style, string expected)
        => Assert.Equal(expected, NpcBodies.For(name.ToLowerInvariant().Replace(' ', '_'), name, false, style));

    [Fact]
    public void APrisoner_IsAlwaysThePrisonerBody_WhateverTheName()
    {
        Assert.Equal("npc_prisoner", NpcBodies.For("prisoner1", "Garrick", true));
        Assert.Equal("npc_prisoner", NpcBodies.For("prisoner2", "Deacon Rolf", true, "a young deacon"));
    }

    [Fact]
    public void NoCluesAtAll_StillGivesABody_AndTheSameOneEveryTime()
    {
        var first = NpcBodies.For("idris", "Idris the Pale", false);
        Assert.NotNull(first);
        Assert.Contains(first, NpcBodies.Archetypes);
        Assert.Equal(first, NpcBodies.For("idris", "Idris the Pale", false));
        Assert.Null(NpcBodies.Guess("Idris the Pale"));
    }

    [Fact]
    public void Path_PointsIntoTheViewerFolder()
        => Assert.Equal("res://assets/models/viewer/npc_guard.glb", NpcBodies.Path("npc_guard"));

    [Fact]
    public void TheVerger_IsTheOnlyVillainWithABody()
    {
        Assert.Equal("verger", NpcBodies.Villain("Absalom Quill", "The Verger", "a dry, precise church verger of great age"));
        Assert.Equal("verger", NpcBodies.Villain("The Verger", null, null));
        Assert.Null(NpcBodies.Villain("Corvus Marl", "The Toll-Keeper", "a clerk who counts the dead"));
    }
}
