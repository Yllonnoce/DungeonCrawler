using System.Text.Json.Serialization;
using DungeonCrawler.Adventure;
using DungeonCrawler.Party;
using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

/// <summary>Tags a message record with its wire kind. Every concrete message carries exactly one.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MsgAttribute : Attribute
{
    public MsgKind Kind { get; }
    public MsgAttribute(MsgKind kind) => Kind = kind;
}

/// <summary>Base of everything that crosses the wire. Plain records, JSON by default, binary for the hot three.</summary>
public abstract record NetMessage
{
    [JsonIgnore] public MsgKind Kind => MsgRegistry.KindOf(GetType());
}

// ---- handshake and roster ----------------------------------------------------------------------

public sealed record NetPlayerDto(string Name, int MemberIndex, string ClassId, int PeerId, bool Online);

/// <summary>An enemy that was not part of the floor plan (summoned, horde) and so must be described to a joiner.</summary>
public sealed record DynamicEnemyDto(int NetId, string DefId, float X, float Y, float Z, float Yaw, float Hp, string? HordeId = null);

[Msg(MsgKind.Hello)] public sealed record Hello(int Protocol, string PlayerName, string ClassId, string Build) : NetMessage;
[Msg(MsgKind.Reject)] public sealed record Reject(string Reason) : NetMessage;
[Msg(MsgKind.Welcome)] public sealed record Welcome(
    int YourPeerId, int YourMemberIndex, ulong GameSeed, int CurrentFloor, double PlaySeconds, bool Victory,
    AdventureDto? Adventure, PartySnapshot BuildSnapshot, FloorStateDto FloorState, List<DynamicEnemyDto> DynamicEnemies,
    PartyDto Party, List<NetPlayerDto> Players, string Objective, int ServerTick, ulong LayoutHash = 0) : NetMessage;
[Msg(MsgKind.Ready)] public sealed record Ready(int Floor) : NetMessage;
[Msg(MsgKind.PlayerJoined)] public sealed record PlayerJoined(NetPlayerDto Player) : NetMessage;
[Msg(MsgKind.PlayerLeft)] public sealed record PlayerLeft(int MemberIndex, string Name) : NetMessage;

// ---- movement (binary, see Snapshot.cs) --------------------------------------------------------

[Msg(MsgKind.Pose)] public sealed record Pose(int Tick, int Member, NetVec Position, float Yaw, float Pitch, NetVec Velocity, PoseFlags Flags) : NetMessage;
[Msg(MsgKind.WorldSnapshot)] public sealed record WorldSnapshot(int Tick, double ServerTime, EntityState[] Enemies, NpcState[] Npcs, PlayerPoseState[] Players) : NetMessage;
[Msg(MsgKind.Vitals)] public sealed record Vitals(MemberVitals[] Members) : NetMessage;

// ---- intents client -> server ------------------------------------------------------------------

[Msg(MsgKind.AttackIntent)] public sealed record AttackIntent(bool Heavy, bool Perfect, NetVec Position, float Yaw, float Pitch) : NetMessage;
[Msg(MsgKind.InteractIntent)] public sealed record InteractIntent(string Ref) : NetMessage;
[Msg(MsgKind.BagMoveIntent)] public sealed record BagMoveIntent(BagSlot From, BagSlot To) : NetMessage;
[Msg(MsgKind.QuickActionIntent)] public sealed record QuickActionIntent(BagSlot Slot) : NetMessage;
[Msg(MsgKind.CycleAbilityIntent)] public sealed record CycleAbilityIntent(int Dir) : NetMessage;
[Msg(MsgKind.AllocateStatIntent)] public sealed record AllocateStatIntent(string Stat) : NetMessage;
[Msg(MsgKind.LearnSkillIntent)] public sealed record LearnSkillIntent(string SkillId) : NetMessage;
[Msg(MsgKind.RespecIntent)] public sealed record RespecIntent : NetMessage;
[Msg(MsgKind.DialogueChoiceIntent)] public sealed record DialogueChoiceIntent(string NpcId, DialogueEffect Effect, string? Flag = null) : NetMessage;
[Msg(MsgKind.ChatIntent)] public sealed record ChatIntent(string Text) : NetMessage;
[Msg(MsgKind.StanceIntent)] public sealed record StanceIntent(int Member) : NetMessage;

// ---- server state ------------------------------------------------------------------------------

[Msg(MsgKind.FloorChangeBegin)] public sealed record FloorChangeBegin(int Floor, string Status) : NetMessage;
[Msg(MsgKind.FloorChange)] public sealed record FloorChange(
    int Floor, int Delta, FloorPlan? Plan, PartySnapshot BuildSnapshot, FloorStateDto? State,
    List<PoseDto> SpawnPoses, List<DynamicEnemyDto> DynamicEnemies, bool FirstVisit, ulong LayoutHash = 0) : NetMessage;
[Msg(MsgKind.PartySync)] public sealed record PartySync(int Index, CharacterDto Member) : NetMessage;
[Msg(MsgKind.BagSync)] public sealed record BagSync(List<BagCellDto> Bag) : NetMessage;
[Msg(MsgKind.QuestSync)] public sealed record QuestSync(QuestState Quest) : NetMessage;
[Msg(MsgKind.Objective)] public sealed record ObjectiveMsg(string Text) : NetMessage;
[Msg(MsgKind.Log)] public sealed record LogMsg(List<string> Lines) : NetMessage;
[Msg(MsgKind.Sfx)] public sealed record SfxMsg(string Name, float Volume = 0f) : NetMessage;
[Msg(MsgKind.Narration)] public sealed record NarrationMsg(string Text, string? VoiceId) : NetMessage;
[Msg(MsgKind.Bark)] public sealed record BarkMsg(int Member, string Text, string? VoiceId) : NetMessage;
[Msg(MsgKind.Chat)] public sealed record ChatMsg(string From, string Text) : NetMessage;
[Msg(MsgKind.Ping)] public sealed record Ping(int Tick, double SentAt) : NetMessage;
[Msg(MsgKind.Pong)] public sealed record Pong(int Tick, double SentAt, double ServerTime) : NetMessage;

// ---- world events ------------------------------------------------------------------------------

[Msg(MsgKind.EnemySpawned)] public sealed record EnemySpawned(DynamicEnemyDto Enemy) : NetMessage;
[Msg(MsgKind.EnemyDied)] public sealed record EnemyDied(int NetId, int KillerMember) : NetMessage;
[Msg(MsgKind.EnemyHurt)] public sealed record EnemyHurt(int NetId, float Amount, bool Crit, float Hp) : NetMessage;
[Msg(MsgKind.EnemyState)] public sealed record EnemyStateMsg(int NetId, string State) : NetMessage;
[Msg(MsgKind.ProjectileSpawned)] public sealed record ProjectileSpawned(int Id, NetVec Position, NetVec Velocity, bool Arrow, uint Color, int SourceMember) : NetMessage;
[Msg(MsgKind.ProjectileEnded)] public sealed record ProjectileEnded(int Id, NetVec Position) : NetMessage;
[Msg(MsgKind.LootSpawned)] public sealed record LootSpawned(ItemDto Item, NetVec Position, int KeyIndex, string? QuestKey) : NetMessage;
[Msg(MsgKind.LootRemoved)] public sealed record LootRemoved(Guid Id, int ByMember) : NetMessage;
[Msg(MsgKind.ChestOpened)] public sealed record ChestOpenedEvt(int Index) : NetMessage;
[Msg(MsgKind.ChestLocked)] public sealed record ChestLockedEvt(int Index, bool Locked) : NetMessage;
[Msg(MsgKind.DoorOpened)] public sealed record DoorOpenedEvt(int Vault) : NetMessage;
[Msg(MsgKind.LeverPulled)] public sealed record LeverPulledEvt(int Vault) : NetMessage;
[Msg(MsgKind.SecretRevealed)] public sealed record SecretRevealedEvt(int Index) : NetMessage;
[Msg(MsgKind.SecretOpened)] public sealed record SecretOpenedEvt(int Index) : NetMessage;
[Msg(MsgKind.TrapRevealed)] public sealed record TrapRevealedEvt(int Index) : NetMessage;
[Msg(MsgKind.TrapDisarmed)] public sealed record TrapDisarmedEvt(int Index) : NetMessage;
[Msg(MsgKind.TrapTriggered)] public sealed record TrapTriggeredEvt(int Index) : NetMessage;
[Msg(MsgKind.TrapPlaced)] public sealed record TrapPlacedEvt(PlacedTrapDto Trap) : NetMessage;
[Msg(MsgKind.BarrelBroken)] public sealed record BarrelBrokenEvt(int Index) : NetMessage;
[Msg(MsgKind.NpcChanged)] public sealed record NpcChangedEvt(NpcDto Npc) : NetMessage;
[Msg(MsgKind.HordeTriggered)] public sealed record HordeTriggeredEvt(string Id) : NetMessage;
[Msg(MsgKind.HordeSpent)] public sealed record HordeSpentEvt(string Id) : NetMessage;
[Msg(MsgKind.BossDefeated)] public sealed record BossDefeatedEvt(int Floor) : NetMessage;
[Msg(MsgKind.MemberDowned)] public sealed record MemberDownedEvt(int Index) : NetMessage;
[Msg(MsgKind.MemberRevived)] public sealed record MemberRevivedEvt(int Index) : NetMessage;
[Msg(MsgKind.PartyWiped)] public sealed record PartyWipedEvt : NetMessage;
[Msg(MsgKind.LevelUp)] public sealed record LevelUpEvt(int Index) : NetMessage;
[Msg(MsgKind.ForcePose)] public sealed record ForcePose(int Member, PoseDto Pose) : NetMessage;
[Msg(MsgKind.Shake)] public sealed record ShakeEvt(float Amplitude, float Seconds) : NetMessage;
[Msg(MsgKind.Victory)] public sealed record VictoryEvt : NetMessage;
/// <summary>Something on the floor changed state: the client applies it to the node the ref names.</summary>
[Msg(MsgKind.Prop)] public sealed record PropEvt(string Ref, string What) : NetMessage;

/// <summary>Kind &lt;-&gt; type, built once from the <see cref="MsgAttribute"/> tags.</summary>
public static class MsgRegistry
{
    private static readonly Dictionary<Type, MsgKind> KindByType = new();
    private static readonly Dictionary<MsgKind, Type> TypeByKind = new();

    static MsgRegistry()
    {
        foreach (var t in typeof(NetMessage).Assembly.GetTypes())
        {
            if (!t.IsSubclassOf(typeof(NetMessage)) || t.IsAbstract) continue;
            var attr = (MsgAttribute?)Attribute.GetCustomAttribute(t, typeof(MsgAttribute));
            if (attr == null) throw new InvalidOperationException($"{t.Name} needs a [Msg] attribute");
            if (TypeByKind.ContainsKey(attr.Kind)) throw new InvalidOperationException($"{t.Name} reuses kind {attr.Kind} of {TypeByKind[attr.Kind].Name}");
            KindByType[t] = attr.Kind;
            TypeByKind[attr.Kind] = t;
        }
    }

    public static MsgKind KindOf(Type t) => KindByType.TryGetValue(t, out var k) ? k : throw new InvalidOperationException($"{t.Name} is not a registered message");
    public static Type? TypeOf(MsgKind kind) => TypeByKind.TryGetValue(kind, out var t) ? t : null;
    public static IEnumerable<Type> AllTypes => TypeByKind.Values;
}
