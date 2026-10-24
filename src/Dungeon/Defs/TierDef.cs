using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using Godot;

namespace DungeonCrawler.Dungeon.Defs;

/// <summary>A dungeon theme: look, room shapes, enemy pool and boss (data/tiers/*.tres). Tier index = order by Index.</summary>
[GlobalClass]
public partial class TierDef : Resource, IHasId
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public int Index { get; set; }

    [ExportGroup("Look")]
    [Export] public Color FloorColor { get; set; } = new(0.32f, 0.30f, 0.28f);
    [Export] public Color WallColor { get; set; } = new(0.45f, 0.42f, 0.40f);
    [Export] public Color CeilingColor { get; set; } = new(0.18f, 0.17f, 0.17f);
    [Export] public Color AmbientColor { get; set; } = new(0.35f, 0.33f, 0.3f);
    [Export] public float AmbientEnergy { get; set; } = 0.35f;
    [Export] public Color FogColor { get; set; } = new(0.05f, 0.05f, 0.06f);
    [Export] public float FogDensity { get; set; } = 0.035f;
    [Export] public Color TorchColor { get; set; } = new(1f, 0.85f, 0.65f);
    /// <summary>Poly Haven texture folder names under assets/textures; empty = flat color.</summary>
    [Export] public string FloorTexture { get; set; } = "";
    [Export] public string WallTexture { get; set; } = "";
    [Export] public string CeilingTexture { get; set; } = "";
    [Export] public int WallLightsPerRoom { get; set; } = 2;

    [ExportGroup("Layout")]
    [Export] public int Width { get; set; } = 48;
    [Export] public int Height { get; set; } = 48;
    [Export] public int MinRooms { get; set; } = 8;
    [Export] public int MaxRooms { get; set; } = 15;
    [Export] public int MinRoomSize { get; set; } = 4;
    [Export] public int MaxRoomSize { get; set; } = 10;
    [Export] public float ExtraEdgeChance { get; set; } = 0.15f;
    [Export] public int TrapCount { get; set; } = 4;
    [Export] public int VaultCount { get; set; } = 1;
    [Export] public int ChestCount { get; set; } = 2;
    [Export] public int BreakableCount { get; set; } = 6;
    [Export] public float HighRoomChance { get; set; } = 0.4f;
    [Export] public int LedgeCount { get; set; } = 1;
    [Export] public int PitCount { get; set; } = 1;
    [Export] public int SecretCount { get; set; } = 1;
    [Export] public int StructureCount { get; set; } = 2;
    [Export] public int RubbleCount { get; set; } = 2;
    [Export] public int NestCount { get; set; } = 6;
    [Export] public int WaterCount { get; set; } = 1;

    [ExportGroup("Enemies")]
    [Export] public string[] EnemyPool { get; set; } = Array.Empty<string>();
    [Export] public float[] EnemyWeights { get; set; } = Array.Empty<float>();
    [Export] public int BaseEnemyCount { get; set; } = 4;
    [Export] public string BossId { get; set; } = "";

    public FloorSpec ToSpec(int floor)
    {
        var inTier = TierRules.FloorInTier(floor);
        return new FloorSpec
        {
            Width = Width, Height = Height, MinRooms = MinRooms, MaxRooms = MaxRooms, MinRoomSize = MinRoomSize, MaxRoomSize = MaxRoomSize,
            ExtraEdgeChance = ExtraEdgeChance,
            EnemyCount = Math.Min(16, BaseEnemyCount + inTier + (TierRules.IsEndless(floor) ? (floor - TierRules.FinalFloor) / 2 : 0)),
            ChestCount = ChestCount, BreakableCount = BreakableCount, TrapCount = TrapCount,
            VaultCount = VaultCount, LockedChestCount = floor >= 2 ? 1 : 0,
            BossFloor = TierRules.IsBossFloor(floor), Floor = floor,
            HighRoomChance = HighRoomChance, LedgeCount = LedgeCount, PitCount = TierRules.IsBossFloor(floor) ? 0 : PitCount, SecretCount = SecretCount, StructureCount = StructureCount, WaterCount = WaterCount, RubbleCount = RubbleCount, NestCount = NestCount,
        };
    }

    /// <summary>Engine-free view of the enemy roster for designers and tests.</summary>
    public TierProfile ToProfile() => new()
    {
        Id = Id, DisplayName = DisplayName, Index = Index,
        EnemyPool = EnemyPool, EnemyWeights = EnemyWeights, BaseEnemyCount = BaseEnemyCount, BossId = BossId,
    };

    public string PickEnemy(Random rng) => ToProfile().PickEnemy(rng);
}
