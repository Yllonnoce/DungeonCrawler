using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Enemies;

/// <summary>
/// The arena change a boss makes once at phase two (Depths spec, section 2): the Crypt Lord's braziers go out, the Broodmother webs the floor,
/// the Colossus sets two slow embers moving, the Herald bends the floor into a gravity well. <see cref="Prepare"/> dresses the arena when the
/// floor is built (server and clients alike, deterministic); <see cref="Change"/> runs when the boss enters phase two and does nothing twice.
/// </summary>
public static partial class BossArena
{
    private const float BrazierOffset = 3.2f;
    private static readonly HashSet<ulong> Changed = new();

    /// <summary>Darken: four lit braziers around the arena's centre (ArenaBrazier0..3), ready to go out. Other changes need nothing in advance.</summary>
    public static void Prepare(Enemy boss, FloorLayout layout, Node3D root, int seed)
    {
        Changed.Remove(boss.GetInstanceId());
        if (boss.Profile.ArenaChange != ArenaChange.Darken || layout.BossArena is not { } arena || Main.Instance?.Builder is not { } builder) return;
        var free = RoomCells.Free(layout, arena);
        if (free.Count == 0) return;
        var centre = builder.Surface(arena.Center);
        var i = 0;
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var at = centre + new Vector3(sx * BrazierOffset, 0, sz * BrazierOffset);
            var cell = DungeonBuilder.WorldToCell(at);
            if (!free.Contains(cell)) { cell = free.MinBy(c => c.ManhattanDistance(cell))!; at = builder.Surface(cell) + new Vector3(sx * 0.9f, 0, sz * 0.9f); }
            else at.Y = builder.Surface(cell).Y;
            var brazier = new Brazier { Name = $"ArenaBrazier{i++}" };
            root.AddChild(brazier);
            brazier.GlobalPosition = at;
        }
    }

    /// <summary>The change itself, by the boss's data. Server and clients both run it (the result is cosmetic on a client: its patches never hurt).</summary>
    public static void Change(Enemy boss)
    {
        if (!Changed.Add(boss.GetInstanceId())) return;
        if (Main.Instance?.Builder is not { } builder || builder.FloorRoot is not { } root || builder.Layout is not { } layout) return;
        var server = !Net.NetSession.IsClient;
        switch (boss.Profile.ArenaChange)
        {
            case ArenaChange.Darken:
                foreach (var b in root.GetChildren().OfType<Brazier>().Where(b => b.Name.ToString().StartsWith("ArenaBrazier")))
                    foreach (var fire in b.GetChildren().OfType<Fire>()) { fire.Visible = false; fire.SetProcess(false); }
                if (server) EventBus.Instance.Log("The braziers die. The arena goes dark.");
                break;
            case ArenaChange.Webs:
            {
                if (layout.BossArena is not { } arena) break;
                var rng = new Random(Rng.ActorSeed(builder.GameSeed, boss.Floor, Enemy.BossNetId));
                var bossCell = DungeonBuilder.WorldToCell(boss.GlobalPosition);
                var cells = RoomCells.Free(layout, arena).Where(c => c != bossCell).ToList();
                for (var i = 0; i < BossRules.SlowPatchCount && cells.Count > 0; i++)
                {
                    var cell = cells[rng.Next(cells.Count)];
                    cells.Remove(cell);
                    var web = new SlowPatch { Name = $"ArenaWeb{i}" };
                    root.AddChild(web);
                    web.GlobalPosition = builder.Surface(cell);
                }
                if (server) EventBus.Instance.Log("Webs spill across the floor.");
                break;
            }
            case ArenaChange.Embers:
            {
                if (layout.BossArena is not { } arena) break;
                var centre = builder.Surface(arena.Center);
                for (var i = 0; i < BossRules.EmberPatchCount; i++)
                {
                    var ember = new EmberPatch { Name = $"ArenaEmber{i}", Floor = boss.Floor, Centre = centre, Phase = i * Mathf.Pi };
                    root.AddChild(ember);
                }
                if (server) EventBus.Instance.Log("The ground glows; embers begin to crawl.");
                break;
            }
            case ArenaChange.GravityWell:
                boss.SlowAuraRadius = BossRules.GravityWellRadius;
                boss.AddChild(new MeshInstance3D
                {
                    Name = "GravityWell",
                    Mesh = new CylinderMesh { TopRadius = BossRules.GravityWellRadius, BottomRadius = BossRules.GravityWellRadius, Height = 0.02f, RadialSegments = 40 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.05f, 0.2f, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                    Position = new Vector3(0, 0.03f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                if (server) EventBus.Instance.Log("The floor bends toward the Herald.");
                break;
        }
    }
}
