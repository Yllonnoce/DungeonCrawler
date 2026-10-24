using DungeonCrawler.Dungeon;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

public class VaultRulesTests
{
    public static IEnumerable<object[]> Curved => new[] { ArchKind.Semicircular, ArchKind.Pointed, ArchKind.Segmental, ArchKind.Catenary }.Select(k => new object[] { k });

    [Theory, MemberData(nameof(Curved))]
    public void Profile_springs_at_the_walls_and_peaks_at_the_centre(ArchKind kind)
    {
        Assert.Equal(0f, VaultRules.Height(kind, -1f, 2f), 3);
        Assert.Equal(0f, VaultRules.Height(kind, 1f, 2f), 3);
        Assert.Equal(VaultRules.Rise(kind, 2f), VaultRules.Height(kind, 0f, 2f), 3);
        Assert.True(VaultRules.Rise(kind, 2f) > 0.5f);
    }

    [Theory, MemberData(nameof(Curved))]
    public void Profile_is_symmetric_and_rises_monotonically_to_the_centre(ArchKind kind)
    {
        var last = -1f;
        for (var t = -1f; t <= 0f; t += 0.05f)
        {
            var h = VaultRules.Height(kind, t, 2f);
            Assert.True(h >= last - 1e-4f, $"{kind} dips at t={t}");
            Assert.Equal(h, VaultRules.Height(kind, -t, 2f), 4);
            last = h;
        }
    }

    [Fact]
    public void RampVault_ClimbsWithTheRamp_AndMeetsBothLevels()
    {
        // a ramp's ceiling rises 2 m over each half, from the lower level's spring line to the upper level's
        Assert.Equal(0f, VaultRules.RampRise(-1f, half: 0, rising: true), 3);
        Assert.Equal(2f, VaultRules.RampRise(1f, half: 0, rising: true), 3);
        Assert.Equal(2f, VaultRules.RampRise(-1f, half: 1, rising: true), 3);
        Assert.Equal(4f, VaultRules.RampRise(1f, half: 1, rising: true), 3);
        // a ramp climbing the other way mirrors it
        Assert.Equal(0f, VaultRules.RampRise(1f, half: 0, rising: false), 3);
        Assert.Equal(4f, VaultRules.RampRise(-1f, half: 1, rising: false), 3);
    }

    [Fact]
    public void Vaults_fit_inside_the_ceiling_layer()
    {
        foreach (var kind in Enum.GetValues<ArchKind>()) Assert.True(VaultRules.Rise(kind, 2f) < 4f, kind.ToString());
        Assert.True(VaultRules.Rise(ArchKind.Pointed, 2f) > VaultRules.Rise(ArchKind.Semicircular, 2f));
        Assert.True(VaultRules.Rise(ArchKind.Segmental, 2f) < VaultRules.Rise(ArchKind.Semicircular, 2f));
        Assert.Equal(0f, VaultRules.Rise(ArchKind.Flat, 2f));
    }

    [Fact]
    public void Groin_follows_the_higher_barrel_so_its_edges_are_arches()
    {
        // the bay's edge (u = 1) is the cross barrel's profile, the centre is the apex, the corners come down to the spring line
        Assert.Equal(VaultRules.Height(ArchKind.Semicircular, 0.3f, 2f), VaultRules.GroinHeight(ArchKind.Semicircular, 1f, 0.3f, 2f), 4);
        Assert.Equal(2f, VaultRules.GroinHeight(ArchKind.Semicircular, 0f, 0f, 2f), 3);
        Assert.Equal(0f, VaultRules.GroinHeight(ArchKind.Semicircular, 1f, 1f, 2f), 3);
        Assert.Equal(VaultRules.GroinHeight(ArchKind.Pointed, 0.4f, 0.7f, 2f), VaultRules.GroinHeight(ArchKind.Pointed, 0.7f, 0.4f, 2f), 4);
    }

    [Fact]
    public void Corridors_get_barrels_along_their_run_and_everything_else_a_groin()
    {
        Assert.Equal(VaultPiece.BarrelX, VaultRules.PieceFor(west: true, east: true, north: false, south: false));
        Assert.Equal(VaultPiece.BarrelX, VaultRules.PieceFor(west: true, east: false, north: false, south: false)); // dead end
        Assert.Equal(VaultPiece.BarrelZ, VaultRules.PieceFor(west: false, east: false, north: true, south: true));
        Assert.Equal(VaultPiece.Groin, VaultRules.PieceFor(west: true, east: true, north: true, south: true)); // room / crossing
        Assert.Equal(VaultPiece.Groin, VaultRules.PieceFor(west: true, east: false, north: true, south: false)); // corner
        Assert.Equal(VaultPiece.Groin, VaultRules.PieceFor(west: true, east: true, north: true, south: false)); // T
        Assert.Equal(VaultPiece.Groin, VaultRules.PieceFor(false, false, false, false)); // a lone cell
    }

    [Fact]
    public void Theme_text_parses_with_tier_defaults()
    {
        Assert.Equal(ArchKind.Semicircular, VaultRules.Parse("", 0));
        Assert.Equal(ArchKind.Catenary, VaultRules.Parse(null, 1));
        Assert.Equal(ArchKind.Segmental, VaultRules.Parse(" ", 2));
        Assert.Equal(ArchKind.Pointed, VaultRules.Parse("", 3));
        Assert.Equal(ArchKind.Pointed, VaultRules.Parse("Gothic", 0));
        Assert.Equal(ArchKind.Segmental, VaultRules.Parse("segmented", 0));
        Assert.Equal(ArchKind.Flat, VaultRules.Parse("flat", 3));
        Assert.Equal(ArchKind.Semicircular, VaultRules.Parse("nonsense", 0));
    }
}
