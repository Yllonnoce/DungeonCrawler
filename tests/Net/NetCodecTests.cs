using DungeonCrawler.Adventure;
using DungeonCrawler.Items;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Xunit;

namespace DungeonCrawler.Tests.Net;

public class NetCodecTests
{
    private static T RoundTrip<T>(T m) where T : NetMessage
    {
        var bytes = NetCodec.Encode(m);
        Assert.Equal(m.Kind, NetCodec.PeekKind(bytes));
        return NetCodec.Decode<T>(bytes);
    }

    [Fact]
    public void EveryMessageType_HasAUniqueKind()
    {
        var kinds = MsgRegistry.AllTypes.Select(MsgRegistry.KindOf).ToList();
        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.True(kinds.Count > 50);
        foreach (var t in MsgRegistry.AllTypes) Assert.Same(t, MsgRegistry.TypeOf(MsgRegistry.KindOf(t)));
    }

    [Fact]
    public void Hello_Reject_Ready_RoundTrip()
    {
        var h = RoundTrip(new Hello(Protocol.Version, "Eric", "class.rogue", "dev"));
        Assert.Equal("Eric", h.PlayerName);
        Assert.Equal("class.rogue", h.ClassId);
        Assert.Equal("The server is full.", RoundTrip(new Reject("The server is full.")).Reason);
        Assert.Equal(3, RoundTrip(new Ready(3)).Floor);
        Assert.IsType<RespecIntent>(RoundTrip(new RespecIntent()));
        Assert.IsType<PartyWipedEvt>(RoundTrip(new PartyWipedEvt()));
    }

    [Fact]
    public void Welcome_CarriesSaveDtosAndPlan()
    {
        var plan = new FloorPlan { Floor = 2, Title = "The Drowned Hall", LayoutHash = 12345UL };
        var adventure = new AdventureDto { Mode = AdventureMode.Offline, FloorPlans = { [2] = plan }, Quests = { ["2.q1"] = new QuestState { Id = "q1", Floor = 2, Title = "Find the key", Status = QuestStatus.Active } } };
        var party = new PartyDto { FocusedIndex = 1, Members = { new CharacterDto { Name = "Eric", ClassId = "class.rogue", Level = 3 } }, Bag = { new BagCellDto { Index = 0, Item = new ItemDto { Id = Guid.NewGuid(), DefId = "item.arrow", StackCount = 12 } } } };
        var state = new FloorStateDto { Visited = true, DeadEnemies = { 1, 4 }, OpenedChests = { 0 }, SpentHordes = { "h1" } };
        var w = new Welcome(2, 0, 987654321UL, 2, 12.5, false, adventure, new PartySnapshot(3, 2.5f, 1), state,
            new List<DynamicEnemyDto> { new(200001, "enemy.horde_husk", 1, 2, 3, 0.5f, 9f, "h1") }, party,
            new List<NetPlayerDto> { new("Eric", 0, "class.rogue", 2, true) }, "Find the stairs", 600);
        var back = RoundTrip(w);
        Assert.Equal(987654321UL, back.GameSeed);
        Assert.Equal("The Drowned Hall", back.Adventure!.FloorPlans[2].Title);
        Assert.Equal(12345UL, back.Adventure.FloorPlans[2].LayoutHash);
        Assert.Equal(QuestStatus.Active, back.Adventure.Quests["2.q1"].Status);
        Assert.Equal(12, back.Party.Bag[0].Item.StackCount);
        Assert.Equal(party.Bag[0].Item.Id, back.Party.Bag[0].Item.Id);
        Assert.Equal(new[] { 1, 4 }, back.FloorState.DeadEnemies);
        Assert.Equal("h1", back.DynamicEnemies[0].HordeId);
        Assert.Equal(new PartySnapshot(3, 2.5f, 1), back.BuildSnapshot);
        Assert.True(back.Players[0].Online);
    }

    [Fact]
    public void LargeJson_IsCompressed_AndDecodes()
    {
        var lines = Enumerable.Range(0, 2000).Select(i => $"Line {i}: the torchlight gutters and something moves in the dark").ToList();
        var bytes = NetCodec.Encode(new LogMsg(lines));
        Assert.True((bytes[0] & 0x80) != 0, "should carry the compressed bit");
        Assert.True(bytes.Length < 20_000, $"compressed to {bytes.Length}");
        Assert.Equal(MsgKind.Log, NetCodec.PeekKind(bytes));
        var back = NetCodec.Decode<LogMsg>(bytes);
        Assert.Equal(lines, back.Lines);
    }

    [Fact]
    public void SmallJson_IsNotCompressed()
    {
        var bytes = NetCodec.Encode(new ChatIntent("hi"));
        Assert.Equal((byte)MsgKind.ChatIntent, bytes[0]);
        Assert.Equal("hi", NetCodec.Decode<ChatIntent>(bytes).Text);
    }

    [Fact]
    public void Pose_BinaryRoundTrip()
    {
        var p = new Pose(1234, 2, new NetVec(1.5f, -2.25f, 3f), 0.7f, -0.2f, new NetVec(0, 0, -4f), PoseFlags.Sprint | PoseFlags.OnFloor);
        var bytes = NetCodec.Encode(p);
        Assert.Equal(1 + 4 + 2 + 12 + 4 + 4 + 12 + 1, bytes.Length);
        Assert.Equal(p, RoundTrip(p));
    }

    [Fact]
    public void Snapshot_And_Vitals_BinaryRoundTrip()
    {
        var s = new WorldSnapshot(77, 12.25,
            new[] { new EntityState(3, new NetVec(1, 2, 3), 0.5f, 40f, 2, EntityState.FlagAlive | EntityState.FlagHostile), new EntityState(200001, new NetVec(-1, 0, 9), 3.1f, 0f, 0, 0) },
            new[] { new NpcState("npc.smith", new NetVec(4, 0, 4), 1f, NpcState.FlagEscorting) },
            new[] { new PlayerPoseState(1, new NetVec(0, 1, 0), 0f, 0.1f, new NetVec(1, 0, 0), PoseFlags.Crouch) });
        var back = RoundTrip(s);
        Assert.Equal(s.Tick, back.Tick);
        Assert.Equal(s.ServerTime, back.ServerTime);
        Assert.Equal(s.Enemies, back.Enemies);
        Assert.Equal(s.Npcs, back.Npcs);
        Assert.Equal(s.Players, back.Players);

        var v = new Vitals(new[] { new MemberVitals(0, 50f, 20f, 0f, 0.4f, true), new MemberVitals(3, 0f, 0f, 12f, 0f, false) });
        Assert.Equal(v.Members, RoundTrip(v).Members);
        var empty = RoundTrip(new WorldSnapshot(1, 0, Array.Empty<EntityState>(), Array.Empty<NpcState>(), Array.Empty<PlayerPoseState>()));
        Assert.Empty(empty.Enemies);
    }

    [Fact]
    public void Intents_RoundTrip()
    {
        var bag = RoundTrip(new BagMoveIntent(BagSlot.Bag(3), BagSlot.Equip(1, EquipSlot.Body)));
        Assert.Equal(BagSlot.Bag(3), bag.From);
        Assert.Equal(BagSlot.Equip(1, EquipSlot.Body), bag.To);
        Assert.True(RoundTrip(new QuickActionIntent(BagSlot.Drop)).Slot.IsDrop);
        var a = RoundTrip(new AttackIntent(true, false, new NetVec(1, 2, 3), 1.5f, -0.3f));
        Assert.True(a.Heavy); Assert.Equal(1.5f, a.Yaw);
        Assert.Equal(DialogueEffect.AcceptQuest, RoundTrip(new DialogueChoiceIntent("npc.a", DialogueEffect.AcceptQuest)).Effect);
        Assert.Equal("chest:3", RoundTrip(new InteractIntent("chest:3")).Ref);
    }

    [Fact]
    public void Events_RoundTrip()
    {
        var loot = RoundTrip(new LootSpawned(new ItemDto { Id = Guid.NewGuid(), DefId = "item.iron_key" }, new NetVec(1, 0, 1), 2, "2.q1"));
        Assert.Equal(2, loot.KeyIndex);
        Assert.Equal("item.iron_key", loot.Item.DefId);
        var fp = RoundTrip(new ForcePose(1, new PoseDto { X = 1, Y = 2, Z = 3, Yaw = 0.5f }));
        Assert.Equal(3f, fp.Pose.Z);
        var npc = RoundTrip(new NpcChangedEvt(new NpcDto { Id = "npc.a", Escorting = true }));
        Assert.True(npc.Npc.Escorting);
        Assert.Equal(5, RoundTrip(new EnemyDied(5, 1)).NetId);
        var fc = RoundTrip(new FloorChange(3, 1, new FloorPlan { Floor = 3, Title = "Deep" }, PartySnapshot.Expected(3), null, new List<PoseDto> { new() { X = 1 } }, new List<DynamicEnemyDto>(), true));
        Assert.Equal("Deep", fc.Plan!.Title);
        Assert.Null(fc.State);
    }

    [Fact]
    public void UnknownKind_And_Garbage_Throw()
    {
        Assert.Throws<NetCodecException>(() => NetCodec.Decode(Array.Empty<byte>()));
        Assert.Throws<NetCodecException>(() => NetCodec.Decode(new byte[] { 250, 1, 2 }));
        Assert.Null(NetCodec.PeekKind(new byte[] { 250 }));
        Assert.Throws<NetCodecException>(() => NetCodec.Decode(new byte[] { (byte)MsgKind.Hello, (byte)'{', (byte)'x' }));
        Assert.Throws<NetCodecException>(() => NetCodec.Decode(new byte[] { (byte)MsgKind.Pose, 1, 2 }));
        Assert.Throws<NetCodecException>(() => NetCodec.Decode<Hello>(NetCodec.Encode(new Ready(1))));
    }
    [Fact]
    public void ObjectiveMessages_RoundTrip()
    {
        var state = new ObjectiveStateDto { Kind = "jailbreak", Completed = false, Seconds = 12.5f, FreedNpcs = { "villager1" }, JailedNpcs = { "villager2" }, SealedMouths = { 1, 3 }, LeverParity = 5, RitualProgress = 40f, RitualStarted = true };
        var back = RoundTrip(new ObjectiveStateMsg(state)).State;
        Assert.Equal("jailbreak", back.Kind);
        Assert.Equal(12.5f, back.Seconds);
        Assert.Equal(new[] { "villager1" }, back.FreedNpcs);
        Assert.Equal(new[] { 1, 3 }, back.SealedMouths);
        Assert.Equal(5, back.LeverParity);
        Assert.True(back.RitualStarted);
        var done = RoundTrip(new ObjectiveDoneEvt(7, "ritual"));
        Assert.Equal(7, done.Floor);
        Assert.Equal("ritual", done.ObjectiveKind);
    }
    [Fact]
    public void Narration_CarriesItsCleanTwin()
    {
        var n = RoundTrip(new NarrationMsg("The King: \"bastards\"", "abc", "The King: \"wretches\"", "def"));
        Assert.Equal("The King: \"wretches\"", n.CleanText);
        Assert.Equal("def", n.CleanVoiceId);
        var plain = RoundTrip(new NarrationMsg("hello", null));
        Assert.Null(plain.CleanText);
    }
}
