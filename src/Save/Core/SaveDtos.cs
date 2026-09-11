using System.Text.Json.Serialization;
using DungeonCrawler.Adventure;

namespace DungeonCrawler.Save;

/// <summary>Root of a save file. Only deltas from regeneration are stored per floor; layouts come from the seed.</summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveFormat.CurrentVersion;
    public string Label { get; set; } = "";
    public ulong GameSeed { get; set; }
    public int CurrentFloor { get; set; } = 1;
    public string SavedAtUtc { get; set; } = "";
    public double PlaySeconds { get; set; }
    public bool Victory { get; set; }
    public PartyDto Party { get; set; } = new();
    public Dictionary<int, FloorStateDto> Floors { get; set; } = new();
    /// <summary>Campaign and per-floor plans (v6). Null in older saves means legacy random population.</summary>
    public AdventureDto? Adventure { get; set; }
    /// <summary>True for a dedicated server's save (v7): members belong to named players and nobody is possessed.</summary>
    public bool Multiplayer { get; set; }
    /// <summary>Who owns which member on a server (v7). Empty for single-player saves.</summary>
    public List<PlayerDto> Players { get; set; } = new();
}

/// <summary>A player's claim on a party member, kept across server restarts.</summary>
public sealed class PlayerDto
{
    public string Name { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int MemberIndex { get; set; }
    public PoseDto? ParkedPose { get; set; }
    public int ParkedFloor { get; set; }
    public string LastSeenUtc { get; set; } = "";
}

public enum AdventureMode { Legacy, Offline, Claude, Builtin }

/// <summary>Who authored this run and what they decided so far. Floor plans are kept so reloads rebuild the same floor.</summary>
public sealed class AdventureDto
{
    public AdventureMode Mode { get; set; } = AdventureMode.Legacy;
    public CampaignPlan? Campaign { get; set; }
    public Dictionary<int, FloorPlan> FloorPlans { get; set; } = new();
    /// <summary>Live quests keyed by "floor.questId".</summary>
    public Dictionary<string, QuestState> Quests { get; set; } = new();
    /// <summary>What happened on each floor, condensed for the planner and kept for the record.</summary>
    public CampaignMemoryDto Memory { get; set; } = new();
    /// <summary>Tokens and dollars spent on the AI planner for this run.</summary>
    public Adventure.Ai.SpendDto Spend { get; set; } = new();
    /// <summary>Set once the AI was switched off mid-run (cap, refusal, no key) so later floors are designed offline.</summary>
    public string? AiStoppedReason { get; set; }
    /// <summary>Companion barks already said, by text, and the floor they were said on: a line waits two floors before it may repeat.</summary>
    public Dictionary<string, int> BarksSaid { get; set; } = new();
}

public sealed class PartyDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public int FocusedIndex { get; set; }
    public List<PoseDto> Poses { get; set; } = new();
    public List<string> Stances { get; set; } = new();
    public List<CharacterDto> Members { get; set; } = new();
    public List<BagCellDto> Bag { get; set; } = new();
}

public sealed class PoseDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
}

public sealed class BagCellDto
{
    public int Index { get; set; }
    public ItemDto Item { get; set; } = new();
}

public sealed class CharacterDto
{
    public string Name { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public int StatPoints { get; set; }
    public int SkillPoints { get; set; }
    public Dictionary<string, int> Allocated { get; set; } = new();
    public float Hp { get; set; }
    public float Stamina { get; set; }
    public float Mana { get; set; }
    public string Rank { get; set; } = "Front";
    public List<string> KnownAbilities { get; set; } = new();
    public List<string> LearnedSkills { get; set; } = new();
    public int ActiveAbilityIndex { get; set; }
    public Dictionary<string, ItemDto> Equipment { get; set; } = new();
    public List<StatusDto> Effects { get; set; } = new();
}

public sealed class StatusDto
{
    public string Kind { get; set; } = "";
    public float Remaining { get; set; }
    public float Magnitude { get; set; }
    public int Stacks { get; set; } = 1;
    public float Chance { get; set; } = 1f;
}

public sealed class ItemDto
{
    public Guid Id { get; set; }
    public string DefId { get; set; } = "";
    public int StackCount { get; set; } = 1;
    public string Rarity { get; set; } = "Common";
    public int ItemLevel { get; set; } = 1;
    public string? LegendaryName { get; set; }
    public string Flavor { get; set; } = "";
    public List<AffixDto> Affixes { get; set; } = new();
}

public sealed class AffixDto
{
    public string AffixId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPrefix { get; set; }
    public int Tier { get; set; }
    public List<ModDto> Modifiers { get; set; } = new();
    public StatusDto? OnHit { get; set; }
}

public sealed class ModDto
{
    public string Stat { get; set; } = "";
    public string Op { get; set; } = "Add";
    public float Value { get; set; }
    public string Source { get; set; } = "";
}

public sealed class FloorStateDto
{
    public bool Visited { get; set; }
    public List<int[]> Explored { get; set; } = new();
    public List<int> DeadEnemies { get; set; } = new();
    public List<EnemyDto> Enemies { get; set; } = new();
    public List<int> OpenedChests { get; set; } = new();
    public List<int> BrokenBarrels { get; set; } = new();
    public List<WorldItemDto> Items { get; set; } = new();
    public bool BossDefeated { get; set; }
    public List<int> TriggeredTraps { get; set; } = new();
    public List<int> DisarmedTraps { get; set; } = new();
    /// <summary>Key spawn indices (vault keys first, then chest keys) already picked up.</summary>
    public List<int> TakenKeys { get; set; } = new();
    public List<int> OpenedVaults { get; set; } = new();
    public List<PlacedTrapDto> PlacedTraps { get; set; } = new();
    public List<int> OpenedSecrets { get; set; } = new();
    /// <summary>Hordes already let loose on this floor (they never come twice).</summary>
    public List<string> SpentHordes { get; set; } = new();
    /// <summary>Ambush points (by index) that already went off on this floor.</summary>
    public List<int> SprungAmbushes { get; set; } = new();
    /// <summary>Seconds spent on the floor under the reinforcement clock, and portals opened so far.</summary>
    public float PressureSeconds { get; set; }
    public int PortalWaves { get; set; }
    /// <summary>Alert state per enemy collective (v6+).</summary>
    public List<CollectiveDto> Collectives { get; set; } = new();
    public List<NpcDto> NpcStates { get; set; } = new();
    /// <summary>Quest keys whose item was already picked up on this floor.</summary>
    public List<string> TakenQuestItems { get; set; } = new();
    /// <summary>The floor objective's progress (v8+); null on a floor without one.</summary>
    public ObjectiveStateDto? Objective { get; set; }
}

/// <summary>Where a floor's objective stands: saved with the floor, sent to late joiners, broadcast every couple of seconds.</summary>
public sealed class ObjectiveStateDto
{
    public string Kind { get; set; } = "none";
    public bool Completed { get; set; }
    /// <summary>The objective's clock: gas risen, ritual channelled.</summary>
    public float Seconds { get; set; }
    public List<string> FreedNpcs { get; set; } = new();
    public List<string> JailedNpcs { get; set; } = new();
    public float RitualProgress { get; set; }
    public bool RitualStarted { get; set; }
    public bool RitualistDowned { get; set; }
    public List<int> SealedMouths { get; set; } = new();
    /// <summary>Shifting Vault: bit i set when lever i stands pulled.</summary>
    public int LeverParity { get; set; }
}

public sealed class NpcDto
{
    public string Id { get; set; } = "";
    public bool Dead { get; set; }
    public bool Escorting { get; set; }
    public bool Rescued { get; set; }
    /// <summary>v8: the objective's people.</summary>
    public bool Downed { get; set; }
    public bool Captured { get; set; }
    public bool Jailed { get; set; }
    public bool Freed { get; set; }
    public float Hp { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class CollectiveDto
{
    public string Id { get; set; } = "";
    public string Alert { get; set; } = "Calm";
    public bool AlarmRaised { get; set; }
    public bool Regrouped { get; set; }
}

public sealed class PlacedTrapDto
{
    public string Kind { get; set; } = "Spikes";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class EnemyDto
{
    public int SpawnIndex { get; set; }
    public float Hp { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int EmbeddedArrows { get; set; }
}

public sealed class WorldItemDto
{
    public ItemDto Item { get; set; } = new();
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}
