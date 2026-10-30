namespace DungeonCrawler.Net;

/// <summary>Wire constants shared by server and client. Bump <see cref="Version"/> whenever a message shape changes.</summary>
public static class Protocol
{
    public const int Version = 2;
    public const int DefaultPort = 7777;
    public const int MaxPlayers = 4;
    public const int PoseHz = 30;
    public const int SnapshotHz = 20;
    public const int VitalsHz = 10;
    /// <summary>Clients render remote bodies this far behind the newest snapshot so there is always something to interpolate toward.</summary>
    public const int InterpolationMs = 100;
    /// <summary>Beyond this the body freezes rather than guessing further.</summary>
    public const int MaxExtrapolationMs = 150;
    /// <summary>JSON payloads above this are gzipped (flag bit in the kind byte).</summary>
    public const int CompressThreshold = 8 * 1024;
    public const int MaxNameLength = 16;
    public const int MaxChatLength = 200;
    /// <summary>Snapshots list an enemy when it is awake or moved, and every enemy at least this often.</summary>
    public const float FullSnapshotSeconds = 2f;
    public const float SnapshotInterestRadius = 40f;
}

/// <summary>Delivery class. WebSocket carries everything reliably; a future transport may honour the difference.</summary>
public enum NetChannel : byte { Reliable = 0, State = 1 }

/// <summary>One byte on the wire in front of every message. Values are part of the protocol: only append.</summary>
public enum MsgKind : byte
{
    // handshake and roster
    Hello = 1, Reject, Welcome, Ready, PlayerJoined, PlayerLeft,
    // movement (binary)
    Pose = 10, WorldSnapshot, Vitals,
    // intents client -> server
    AttackIntent = 20, InteractIntent, BagMoveIntent, QuickActionIntent, CycleAbilityIntent, AllocateStatIntent, LearnSkillIntent, RespecIntent, DialogueChoiceIntent, ChatIntent, StanceIntent,
    // server state
    FloorChangeBegin = 40, FloorChange, PartySync, BagSync, QuestSync, Objective, Log, Sfx, Narration, Bark, Chat, Ping, Pong,
    // world events
    EnemySpawned = 60, EnemyDied, EnemyHurt, ProjectileSpawned, ProjectileEnded, LootSpawned, LootRemoved, ChestOpened, DoorOpened, LeverPulled,
    SecretRevealed, SecretOpened, TrapRevealed, TrapDisarmed, TrapTriggered, TrapPlaced, BarrelBroken, NpcChanged, HordeTriggered, HordeSpent,
    BossDefeated, MemberDowned, MemberRevived, PartyWiped, LevelUp, ForcePose, Shake, Victory, EnemyState, ChestLocked, Prop,
    AmbushSprung, PortalOpened,
    // floor objectives (v2)
    ObjectiveState, ObjectiveDone,
    // lethal hazards (v3): server-owned phases, a client asking to rescue or shouting for help
    HazardPhase, RescueIntent, ShoutIntent,
    // a member's body doing something for show (v4): the victory dance over a kill
    Emote,
}
