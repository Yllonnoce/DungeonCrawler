using System.Numerics;
using DungeonCrawler.Core;
using Xunit;

namespace DungeonCrawler.Tests.Core;

public class GripMathTests
{
    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-5f;

    [Fact]
    public void BasisFor_PointsTheBladeAlongTheGripEnd()
    {
        var dir = new Vector3(0.3f, 0.4f, 1.2f);
        var (x, y, z) = GripMath.BasisFor(dir);
        Assert.True(Near(y, Vector3.Normalize(dir)), $"Y was {y}");
        Assert.True(MathF.Abs(x.Length() - 1f) < 1e-5f && MathF.Abs(z.Length() - 1f) < 1e-5f);
        Assert.True(MathF.Abs(Vector3.Dot(x, y)) < 1e-5f && MathF.Abs(Vector3.Dot(x, z)) < 1e-5f && MathF.Abs(Vector3.Dot(y, z)) < 1e-5f);
        Assert.True(Vector3.Dot(Vector3.Cross(x, y), z) > 0.99f, "right-handed, no mirroring");
    }

    [Fact]
    public void BasisFor_CopesWithABladeAlongTheHelperAxis_AndWithNoDirection()
    {
        var forward = new Vector3(0, 0, -1);
        Assert.True(Near(GripMath.BasisFor(forward).Y, forward));
        Assert.True(Near(GripMath.BasisFor(-forward * 3f).Y, -forward));
        Assert.True(Near(GripMath.BasisFor(Vector3.Zero).Y, Vector3.UnitY)); // a missing end joint leaves the blade upright
    }

    [Fact]
    public void WeaponPath_NamesTheKayKitItem_OrAnyPathUnderModels()
    {
        Assert.Equal("res://assets/models/kaykit_items/sword_1handed.gltf", GripMath.WeaponPath("sword_1handed"));
        Assert.Equal("res://assets/models/quaternius_rpg/weapons/Axe.gltf", GripMath.WeaponPath("quaternius_rpg/weapons/Axe"));
    }
}

public class EnemyProfileWeaponTests
{
    [Fact]
    public void Weapon_IsEmptyByDefault_AndNamesAKayKitItem()
    {
        Assert.Equal("", new DungeonCrawler.Enemies.EnemyProfile().Weapon);
        var armed = new DungeonCrawler.Enemies.EnemyProfile { Weapon = "axe_1handed" };
        Assert.Equal("res://assets/models/kaykit_items/axe_1handed.gltf", GripMath.WeaponPath(armed.Weapon));
    }
}
