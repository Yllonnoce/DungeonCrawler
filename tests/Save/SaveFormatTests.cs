using System.Text.Json.Nodes;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Items;
using DungeonCrawler.Save;
using DungeonCrawler.Tests.Characters;
using DungeonCrawler.Tests.Items;
using Xunit;

namespace DungeonCrawler.Tests.Save;

public class SaveFormatTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();
    private ClassProfile? Classes(string id) => id switch { "class.warrior" => TestClasses.Warrior, "class.mage" => TestClasses.Mage, "class.rogue" => TestClasses.Rogue, _ => null };

    private CharacterState SampleWarrior()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front, _cat.Create("item.longsword"));
        var plate = LootRoller.Generate(_cat, _cat.Base("item.plate"), Rarity.Rare, 6, new Random(3));
        w.Equipment.Equip(EquipSlot.Body, plate);
        w.Equipment.Equip(EquipSlot.Offhand, LootRoller.MakeLegendary(_cat, _cat.Legendaries["leg.bulwark"], 6, new Random(4)));
        w.Recalculate();
        Leveling.AddXp(w, 150);
        w.AllocateStat(Stat.Vitality);
        w.Hp = 33; w.Stamina = 12; w.Mana = 4;
        w.Effects.Apply(new StatusApplication(StatusKind.Poison, 4.5f, 2f));
        w.Effects.Apply(new StatusApplication(StatusKind.Poison, 4.5f, 2f));
        w.Effects.Apply(new StatusApplication(StatusKind.Shield, 9f, 25f));
        w.CycleAbility(1);
        return w;
    }

    [Fact]
    public void Character_RoundTripsThroughDto()
    {
        var w = SampleWarrior();
        var dto = SaveMapper.ToDto(w);
        var back = SaveMapper.FromDto(dto, Classes, _cat);

        Assert.Equal(w.Name, back.Name);
        Assert.Equal(w.Level, back.Level);
        Assert.Equal(w.Xp, back.Xp);
        Assert.Equal(w.StatPoints, back.StatPoints);
        Assert.Equal(w.Allocated[Stat.Vitality], back.Allocated[Stat.Vitality]);
        Assert.Equal(w.Hp, back.Hp); Assert.Equal(w.Stamina, back.Stamina); Assert.Equal(w.Mana, back.Mana);
        Assert.Equal(w.ActiveAbilityId, back.ActiveAbilityId);
        Assert.Equal(w.Stats[Stat.Armor], back.Stats[Stat.Armor]);
        Assert.Equal(w.Stats[Stat.MaxHp], back.Stats[Stat.MaxHp]);
        Assert.Equal(2, back.Effects.Get(StatusKind.Poison)!.Stacks);
        Assert.Equal(25f, back.Effects.ShieldPoints);

        var plate = back.Equipment[EquipSlot.Body]!;
        Assert.Equal(w.Equipment[EquipSlot.Body]!.DisplayName, plate.DisplayName);
        Assert.Equal(w.Equipment[EquipSlot.Body]!.Id, plate.Id);
        Assert.Equal(Rarity.Rare, plate.Rarity);
        var shield = back.Equipment[EquipSlot.Offhand]!;
        Assert.Equal("Bulwark", shield.DisplayName);
        Assert.Contains(shield.Modifiers, m => m.Stat == Stat.Armor && m.Value == 15);
    }

    [Fact]
    public void Item_WithOnHitAffix_RoundTrips()
    {
        var rng = new Random(11);
        var sword = LootRoller.Generate(_cat, _cat.Base("item.longsword"), Rarity.Magic, 3, rng);
        sword.Affixes.Add(LootRoller.RollAffix(_cat.Affixes["affix.venomous"], 3, rng));
        var back = SaveMapper.FromDto(SaveMapper.ToDto(sword), _cat);
        Assert.Equal(sword.DisplayName, back.DisplayName);
        Assert.Contains(back.Weapon!.OnHit, o => o.Kind == StatusKind.Poison && Math.Abs(o.Chance - 0.3f) < 0.001f);
    }

    [Fact]
    public void UnknownItem_BecomesPlaceholderInsteadOfCrashing()
    {
        var back = SaveMapper.FromDto(new ItemDto { DefId = "item.removed_in_patch", StackCount = 2 }, _cat);
        Assert.Equal(SlotKind.Junk, back.Kind);
        Assert.StartsWith("Lost", back.DisplayName);
        Assert.Equal(2, back.StackCount);
    }

    [Fact]
    public void Bag_RoundTripsWithCellPositions()
    {
        var bag = new Inventory(10);
        bag.Set(0, _cat.Create("item.potion", 3));
        bag.Set(7, _cat.Create("item.ring"));
        var dto = SaveMapper.ToDto(bag);
        var other = new Inventory(10);
        other.Set(3, _cat.Create("item.longsword")); // should be cleared
        SaveMapper.Restore(other, dto, _cat);
        Assert.Equal(3, other[0]!.StackCount);
        Assert.Null(other[3]);
        Assert.Equal("Ring", other[7]!.DisplayName);
    }

    [Fact]
    public void SaveData_JsonRoundTrip()
    {
        var data = new SaveData
        {
            Label = "Test", GameSeed = 1234567890123UL, CurrentFloor = 3, SavedAtUtc = SaveMapper.Now(), PlaySeconds = 61.5,
            Party = new PartyDto { X = 10, Y = 0.3f, Z = 42, Yaw = 1.2f, FocusedIndex = 2, Members = { SaveMapper.ToDto(SampleWarrior()) } },
        };
        data.Floors[2] = new FloorStateDto
        {
            Visited = true, Explored = { new[] { 3, 4 }, new[] { 5, 6 } }, DeadEnemies = { 0, 2 },
            Enemies = { new EnemyDto { SpawnIndex = 1, Hp = 12, X = 1, Y = 0, Z = 2, EmbeddedArrows = 1 } },
            OpenedChests = { 0 }, BrokenBarrels = { 1, 3 },
            Items = { new WorldItemDto { Item = SaveMapper.ToDto(_cat.Create("item.potion")), X = 4, Y = 0.4f, Z = 8 } },
        };

        var json = SaveFormat.Serialize(data);
        var back = SaveFormat.Deserialize(json);

        Assert.Equal(SaveFormat.CurrentVersion, back.Version);
        Assert.Equal(data.GameSeed, back.GameSeed);
        Assert.Equal(3, back.CurrentFloor);
        Assert.Equal(61.5, back.PlaySeconds);
        Assert.Equal(2, back.Party.FocusedIndex);
        Assert.Equal("Brand", back.Party.Members[0].Name);
        var f = back.Floors[2];
        Assert.Equal(new[] { 5, 6 }, f.Explored[1]);
        Assert.Equal(new List<int> { 0, 2 }, f.DeadEnemies);
        Assert.Equal(12, f.Enemies[0].Hp);
        Assert.Single(f.Items);
        Assert.Equal("item.potion", f.Items[0].Item.DefId);

        var header = SaveFormat.ReadHeader(json)!.Value;
        Assert.Equal(3, header.floor);
        Assert.Equal("Test", header.label);
        Assert.Contains("Brand L2", header.members[0]);
    }

    [Fact]
    public void Migration_FromUnversionedFile()
    {
        var old = new JsonObject { ["GameSeed"] = 5, ["CurrentFloor"] = 2 };
        var data = SaveFormat.Deserialize(old.ToJsonString());
        Assert.Equal(SaveFormat.CurrentVersion, data.Version);
        Assert.Equal(5UL, data.GameSeed);
        Assert.Equal(2, data.CurrentFloor);
        Assert.NotNull(data.Floors);
        Assert.NotNull(data.Party);
    }

    [Fact]
    public void Migration_From1To2_AddsVictoryAndEmptyLists()
    {
        var v1 = new JsonObject { ["Version"] = 1, ["GameSeed"] = 9, ["CurrentFloor"] = 4, ["Floors"] = new JsonObject { ["3"] = new JsonObject { ["Visited"] = true } } };
        var data = SaveFormat.Deserialize(v1.ToJsonString());
        Assert.Equal(SaveFormat.CurrentVersion, data.Version);
        Assert.False(data.Victory);
        Assert.Empty(data.Floors[3].TriggeredTraps);
        Assert.Empty(data.Floors[3].PlacedTraps);
    }

    [Fact]
    public void Migration_RejectsNewerFiles()
    {
        var future = new JsonObject { ["Version"] = SaveFormat.CurrentVersion + 5 };
        Assert.Throws<InvalidDataException>(() => SaveFormat.Deserialize(future.ToJsonString()));
    }
}

public class SaveV7Tests
{
    [Xunit.Fact]
    public void V6_WithoutPlayers_MigratesToSinglePlayerV7()
    {
        var json = "{\"Version\":6,\"GameSeed\":5,\"CurrentFloor\":2,\"Party\":{},\"Floors\":{}}";
        var data = SaveFormat.Deserialize(json);
        Xunit.Assert.Equal(SaveFormat.CurrentVersion, data.Version);
        Xunit.Assert.False(data.Multiplayer);
        Xunit.Assert.Empty(data.Players);
    }

    [Xunit.Fact]
    public void ServerSave_KeepsPlayers()
    {
        var data = new SaveData { Multiplayer = true, Players = { new PlayerDto { Name = "Eric", ClassId = "class.rogue", MemberIndex = 1, ParkedFloor = 3, ParkedPose = new PoseDto { X = 1, Y = 2, Z = 3, Yaw = 0.5f }, LastSeenUtc = "t" } } };
        var back = SaveFormat.Deserialize(SaveFormat.Serialize(data));
        Xunit.Assert.True(back.Multiplayer);
        Xunit.Assert.Equal(1, back.Players[0].MemberIndex);
        Xunit.Assert.Equal(3f, back.Players[0].ParkedPose!.Z);
        var slot = DungeonCrawler.Net.PlayerSlot.FromSave(back.Players[0]);
        Xunit.Assert.Equal("Eric", slot.Name);
        Xunit.Assert.False(slot.Online);
        Xunit.Assert.Equal("Eric", slot.ToSave().Name);
    }

    [Fact]
    public void V8_To_V9_SetsHazardsFromFloor_AfterCurrentFloor()
    {
        var root = JsonNode.Parse("{\"Version\":8,\"CurrentFloor\":6,\"Adventure\":{}}")!.AsObject();
        var migrated = SaveMigrations.Migrate(root);
        Assert.Equal(SaveFormat.CurrentVersion, migrated["Version"]!.GetValue<int>());
        Assert.Equal(9, SaveFormat.CurrentVersion);
        Assert.Equal(7, migrated["Adventure"]!["HazardsFromFloor"]!.GetValue<int>());
    }

    [Fact]
    public void V8_WithoutAdventure_StillMigrates()
    {
        var root = JsonNode.Parse("{\"Version\":8,\"CurrentFloor\":3}")!.AsObject();
        var migrated = SaveMigrations.Migrate(root);
        Assert.Equal(SaveFormat.CurrentVersion, migrated["Version"]!.GetValue<int>());
    }

    [Fact]
    public void HazardState_RoundTrips()
    {
        var dto = new FloorStateDto { Hazards = { new HazardStateDto { Index = 0, Kind = (byte)DungeonCrawler.Dungeon.HazardKind.Press, Phase = (byte)DungeonCrawler.Dungeon.PressPhase.Closing, PhaseTime = 1.1f, Spent = 2, Jammed = false } } };
        var json = System.Text.Json.JsonSerializer.Serialize(dto, SaveFormat.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<FloorStateDto>(json, SaveFormat.Options)!;
        Assert.Single(back.Hazards);
        Assert.Equal(1.1f, back.Hazards[0].PhaseTime);
        Assert.Equal(2, back.Hazards[0].Spent);
        Assert.Equal((byte)DungeonCrawler.Dungeon.PressPhase.Closing, back.Hazards[0].Phase);
    }

    [Fact]
    public void NewAdventure_HasHazardsFromFloorOne()
    {
        Assert.Equal(1, new AdventureDto().HazardsFromFloor);
    }

    [Fact]
    public void Restore_MidClosing_ResumesRemainingTime()
    {
        var remaining = DungeonCrawler.Dungeon.HazardRules.PressDuration(DungeonCrawler.Dungeon.PressPhase.Closing) - 1.1f;
        Assert.Equal(1.3f, remaining, 3);
        Assert.True(remaining > 0, "a restored Closing phase must still have time before Crush");
    }
}
