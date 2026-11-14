namespace DungeonCrawler.Net;

/// <summary>Wire constants shared by server and client. Bump <see cref="Version"/> whenever a message shape changes.</summary>
public static class Protocol
{
    public const int Version = 14;
    /// <summary>How many <see cref="MsgKind"/> values this <see cref="Version"/> knows; a test fails when a kind is appended without bumping both.</summary>
    public const int MessageKindCount = 108;
    /// <summary>Seconds between the server's item checks (<see cref="LootManifest"/>).</summary>
    public const float LootCheckSeconds = 4f;
    public const int DefaultPort = 7777;
    public const int MaxPlayers = 4;
    public const int PoseHz = 30;
    public const int SnapshotHz = 20;
    public const int VitalsHz = 10;
    /// <summary>Unchanged Vitals are skipped, but sent at least this often (a heartbeat for joiners and lost copies).</summary>
    public const float VitalsHeartbeatSeconds = 1f;
    /// <summary>Clients render remote bodies this far behind the newest snapshot so there is always something to interpolate toward.</summary>
    public const int InterpolationMs = 100;
    /// <summary>Beyond this the body freezes rather than guessing further.</summary>
    public const int MaxExtrapolationMs = 150;
    /// <summary>
    /// JSON payloads above this are gzipped (flag bit in the kind byte), when that makes them smaller. It was 8 KB; at 512 B
    /// a member's sheet (PartySync, about 1 KB of JSON, sent on every kill for every member) goes out at about half the
    /// size, and so do full bags and long item lists. Every build already decodes the flag, so this is not a wire change.
    /// </summary>
    public const int CompressThreshold = 512;
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
    // the server's answer to a Join choice (v4): the client shows the yes or the no line
    DialogueAnswer,
    // the party purse (plan A): the server sends the total whenever it changes
    GoldSync,
    // the merchant's shop
    ShopBuyIntent, ShopSellIntent, ShopSync,
    // an NPC's line raised on the server
    NpcSay,
    // a client reached a dialogue node on its own screen (v5): the server relays it so the merchant's deal pays the purse once
    DialogueNodeIntent,
    // blood or a stuck arrow where the server resolved a hit (v6); spell bursts stay local
    HitFxEvt,
    // a client pressed Cheer (v7): the server picks the lines and relays the bodies' emote
    CheerIntent,
    // buy back one of the merchant's last three sales (v7); the ledger itself rides in ShopSync
    ShopBuybackIntent,
    // fortune (v8): the shrine's card and the first pick, the party's boons and curses, the merchant's Lucky Dip
    ShrineCard, ShrineChoiceIntent, ShrineClosed, FortuneSync, LuckyDipIntent, LuckyDipSync,
    // run-structure (v9): the server's death card on a wipe, the floor card on a descent, the run recap on victory or a Death Clause end
    DeathCardEvt, FloorCardEvt, RunRecapEvt,
    // friends (v10): the stairs ready-check (a client's Yes or No, the server's count for every HUD) and pings
    StairsReadyIntent, StairsReadyMsg, PingIntent, PingEvt,
    // hero bots (v11): a player brings a story hero in or sends one away; every member's swing shows on every screen
    AddBotIntent, DismissBotIntent, SwingEvt,
    // item check (v12): the server's list of the items on the floor every few seconds; a client drops what is not on it and asks for what it lacks
    LootManifest, LootResyncIntent,
    // state check (v13): the server's fingerprints of the bag, each member, the purse and the stairs lock; a client asks again for what differs.
    // The kind byte's top bit is NetCodec's gzip flag, so 127 is the last usable kind (LootResyncIntent is 126): new kinds fill the free
    // numbers below, here 31.., and MsgRegistry refuses any kind of 128 or more.
    StateCheckMsg = 31, StateResyncIntent = 32,
    // prop check (v14): the server's fingerprint of the floor's props with each item check; a client whose own differs asks for the whole
    // prop state, sent to it alone (doors, levers, chests, secrets, traps, barrels, the shrine, the party's traps)
    PropCheckMsg = 37, PropResyncIntent = 38, PropStateMsg = 39,
    // enemy check and floor resync (v14): the server's living enemies with each item check, a client's ask for the ones it lacks,
    // and a client's ask for the whole floor when its floor change never arrived or would not build (its watchdog)
    EnemyCheckMsg = 54, EnemyResyncIntent = 55, FloorResyncIntent = 56,
}
