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
    /// <summary>A hero bot's owner (null for a player's own character); see Net.PlayerSlot.</summary>
    public string? BotOwner { get; set; }
    public bool BotActive { get; set; }
    public bool BotWaiting { get; set; }
}

public enum AdventureMode { Legacy, Offline, Claude, Builtin }

/// <summary>Which title-screen mode started the run (v11, run-structure): the shipped Story, a New Descent on a fresh seed, or the Daily on the UTC date's seed.</summary>
public enum RunKind { Story, Descent, Daily }

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
    /// <summary>The tavern's seed money has been paid (at the briefing's deal or on the first descent): it is paid once a run.</summary>
    public bool SeedMoneyPaid { get; set; }
    /// <summary>Set once the AI was switched off mid-run (cap, refusal, no key) so later floors are designed offline.</summary>
    public string? AiStoppedReason { get; set; }
    /// <summary>Companion barks already said, by text, and the floor they were said on: a line waits two floors before it may repeat.</summary>
    public Dictionary<string, int> BarksSaid { get; set; } = new();
    /// <summary>Running-gag counters (gerald, doors, empty): what Ilex has named, what Brand has counted, what Sable is owed.</summary>
    public Dictionary<string, int> GagCounts { get; set; } = new();
    /// <summary>Floors below this were built before the lethal hazards existed (an old save): they stay as they were. New runs start at 1.</summary>
    public int HazardsFromFloor { get; set; } = 1;
    /// <summary>Boon ids the party holds (v11+, fortune): run-long, applied through <c>FortuneRules</c>.</summary>
    public List<string> Boons { get; set; } = new();
    /// <summary>Curse ids the party carries (v11+, fortune) until lifted at a shrine.</summary>
    public List<string> Curses { get; set; } = new();
    /// <summary>Run-structure (v11+): the title mode that started the run; an older save is a Story.</summary>
    public RunKind Run { get; set; } = RunKind.Story;
    /// <summary>A fresh id per run: the profile's last-twenty list upserts by it.</summary>
    public string RunId { get; set; } = "";
    /// <summary>The signed clauses ("key" or "key:rank", normalized by <c>ClauseRules</c>); empty for none.</summary>
    public List<string> Clauses { get; set; } = new();
    /// <summary>The Daily's date key (yyyy-MM-dd, UTC); null for the other modes.</summary>
    public string? DailyDate { get; set; }
    /// <summary>The Death Clause ended this run: the slot is a memorial and never loads.</summary>
    public bool Fallen { get; set; }
    /// <summary>The death card's line, kept for the memorial and the records.</summary>
    public string? Epitaph { get; set; }
    /// <summary>Wipes this run, all floors.</summary>
    public int Wipes { get; set; }
    /// <summary>The Scar curse holds until the party stands on a floor other than floor ScarUntilFloor − 1; 0 for none.</summary>
    public int ScarUntilFloor { get; set; }
    /// <summary>The hidden stall's deed-unlocked lines this run plays with (fixed at the run's start from the host's profile, so every peer rolls the same stall).</summary>
    public List<string> UnlockedMerchantLines { get; set; } = new();
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
    /// <summary>The party's shared gold purse. Saves before version 10 have none, so it loads as 0.</summary>
    public int Gold { get; set; }
    /// <summary>The merchant's last three sales, newest first, for buyback. Older saves have none, so it loads empty.</summary>
    public List<SoldDto> Buyback { get; set; } = new();
}

/// <summary>One sale the merchant remembers: the item and what he paid for it.</summary>
public sealed class SoldDto
{
    public ItemDto Item { get; set; } = new();
    public int Price { get; set; }
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
    /// <summary>Down to a lethal hazard (v9+); old saves read false / -1.</summary>
    public bool Mangled { get; set; }
    public int MangledBy { get; set; } = -1;
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
    /// <summary>Groups the shut-out creatures have sent to the safe room's door on this floor.</summary>
    public int Gathered { get; set; }
    /// <summary>Spawn indices of the floor's elites (alive or dead): the roster stands however the schedule has moved since.</summary>
    public List<int> EliteSpawns { get; set; } = new();
    /// <summary>Merchant stock slots (by index) already bought on this floor.</summary>
    public List<int> ShopSold { get; set; } = new();
    /// <summary>Alert state per enemy collective (v6+).</summary>
    public List<CollectiveDto> Collectives { get; set; } = new();
    public List<NpcDto> NpcStates { get; set; } = new();
    /// <summary>Quest keys whose item was already picked up on this floor.</summary>
    public List<string> TakenQuestItems { get; set; } = new();
    /// <summary>The floor objective's progress (v8+); null on a floor without one.</summary>
    public ObjectiveStateDto? Objective { get; set; }
    /// <summary>The lethal hazards' state (v9+): phase, time into it, kills, jam.</summary>
    public List<HazardStateDto> Hazards { get; set; } = new();
    /// <summary>The floor's shrine has been touched (v11+, fortune): it is one use.</summary>
    public bool ShrineUsed { get; set; }
    /// <summary>The bound chest was opened and its hunt started: a reload does not hunt twice.</summary>
    public bool BoundOpened { get; set; }
    /// <summary>Chest spawn indices whose mimic sprang but has not died yet (the chest still owes its roll).</summary>
    public List<int> MimicsSprung { get; set; } = new();
    /// <summary>Secret indices the party has found on this floor (rooms entered, alcoves opened).</summary>
    public List<int> SecretsFound { get; set; } = new();
    /// <summary>Lucky Dip slots (0..2) already bought on this floor.</summary>
    public List<int> LuckyDipBought { get; set; } = new();
    /// <summary>Chest spawn indices whose lock was jammed by a third failed pick (a key still opens them).</summary>
    public List<int> JammedChests { get; set; } = new();
}

/// <summary>One Press or Spike Bed mid-life. Phase is a PressPhase or SpikePhase byte by Kind.</summary>
public sealed class HazardStateDto
{
    public int Index { get; set; }
    public byte Kind { get; set; }
    public byte Phase { get; set; }
    public float PhaseTime { get; set; }
    public int Spent { get; set; }
    public bool Jammed { get; set; }
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
    /// <summary>Ran to the enemy (an escort betrayal): restored gone, not as a corpse.</summary>
    public bool Fled { get; set; }
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
    /// <summary>Above zero: a pile of gold, and Item is only a placeholder.</summary>
    public int Coins { get; set; }
}
