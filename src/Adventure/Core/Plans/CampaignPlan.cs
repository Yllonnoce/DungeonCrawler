using DungeonCrawler.Adventure.Ai;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Adventure;

public enum BarkTrigger { Kill, Hurt, Loot, LevelUp, BossSeen, Possess, Downed, Revived, Idle, Dance, Travel }

public sealed class VoicedCharacter
{
    public string Name { get; set; } = "";
    public string Epithet { get; set; } = "";
    [AiIgnore] public string? VoiceId { get; set; }
    [AiDescription("How they sound: VoiceStyle (a few words, at most 120 characters), DefaultDelivery (a delivery id), Voice (a listed voice name); null for the game's own villain voice.")]
    [AiNotNull] public Voice.Persona? Persona { get; set; }
}

public sealed class FactionPlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> EnemyIds { get; set; } = new();
}

public sealed class TierPlan
{
    public int Index { get; set; }
    public string Theme { get; set; } = "";
    public string Beat { get; set; } = "";
    public string BossName { get; set; } = "";
    [AiNotNull] public string? BossSignature { get; set; }
    public Line EnterLine { get; set; } = new();
}

/// <summary>Generator knobs a campaign may override per floor. Null keeps the tier's value. Clamped on apply.</summary>
public sealed class SpecOverrides
{
    [AiNotNull] public int? EnemyCount { get; set; }
    [AiNotNull] public int? ChestCount { get; set; }
    [AiNotNull] public int? LockedChestCount { get; set; }
    [AiNotNull] public int? TrapCount { get; set; }
    [AiNotNull] public int? VaultCount { get; set; }
    [AiNotNull] public int? LedgeCount { get; set; }
    [AiNotNull] public int? PitCount { get; set; }
    [AiNotNull] public int? SecretCount { get; set; }
    [AiNotNull] public float? HighRoomChance { get; set; }
    [AiDescription("Gatehouses, bandit camps or ruined colonnades built inside big rooms, 0 to 3.")]
    [AiNotNull] public int? StructureCount { get; set; }
    [AiDescription("Rubble mounds heaped on room floors, 0 to 6.")]
    [AiNotNull] public int? RubbleCount { get; set; }
    [AiDescription("Rooms with breaches in their walls that a horde can pour from, 0 to 8.")]
    [AiNotNull] public int? NestCount { get; set; }
    [AiDescription("Ponds or streams laid in rooms, 0 to 3.")]
    [AiNotNull] public int? WaterCount { get; set; }

    public bool IsEmpty => EnemyCount == null && ChestCount == null && LockedChestCount == null && TrapCount == null && VaultCount == null
                           && LedgeCount == null && PitCount == null && SecretCount == null && HighRoomChance == null && StructureCount == null && WaterCount == null && RubbleCount == null && NestCount == null;

    public FloorSpec Apply(FloorSpec spec) => spec with
    {
        EnemyCount = Math.Clamp(EnemyCount ?? spec.EnemyCount, 3, 16),
        ChestCount = Math.Clamp(ChestCount ?? spec.ChestCount, 1, 4),
        LockedChestCount = Math.Clamp(LockedChestCount ?? spec.LockedChestCount, 0, 2),
        TrapCount = Math.Clamp(TrapCount ?? spec.TrapCount, 0, 8),
        VaultCount = Math.Clamp(VaultCount ?? spec.VaultCount, 0, 2),
        LedgeCount = Math.Clamp(LedgeCount ?? spec.LedgeCount, 0, 2),
        PitCount = spec.BossFloor ? 0 : Math.Clamp(PitCount ?? spec.PitCount, 0, 2),
        SecretCount = Math.Clamp(SecretCount ?? spec.SecretCount, 0, 3),
        HighRoomChance = Math.Clamp(HighRoomChance ?? spec.HighRoomChance, 0f, 1f),
        StructureCount = Math.Clamp(StructureCount ?? spec.StructureCount, 0, 3),
        RubbleCount = Math.Clamp(RubbleCount ?? spec.RubbleCount, 0, 6),
        NestCount = Math.Clamp(NestCount ?? spec.NestCount, 0, 8),
        WaterCount = Math.Clamp(WaterCount ?? spec.WaterCount, 0, 3),
    };
}

/// <summary>A recurring character: the same name and voice whenever they turn up, and a secret the run pays off.</summary>
public sealed class CastMember
{
    [AiDescription("Short id such as cast_warden; NPCs on floors refer to it with CastId.")]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [AiDescription("Who they are to the party in a few words: a guide who talks too much, a rival captain, a prisoner.")]
    public string Role { get; set; } = "";
    [AiDescription("What they hide and when it should come out; never said outright before its floor.")]
    public string Secret { get; set; } = "";
    [AiIgnore] public string? VoiceId { get; set; }
    [AiDescription("How they sound: VoiceStyle (a few words, at most 120 characters, a distinct person: a boastful captain, a cheerful gravedigger, a sly fence), DefaultDelivery (a delivery id), Voice (a listed voice name).")]
    [AiNotNull] public Voice.Persona? Persona { get; set; }
}

public sealed class CluePlan
{
    public string Id { get; set; } = "";
    [AiDescription("The floor an Investigate quest will place this clue on.")]
    public int Floor { get; set; }
    [AiDescription("What the clue says or shows, at most 160 characters; each clue points at part of the truth.")]
    public string Text { get; set; } = "";
}

/// <summary>The question the whole descent asks, its hidden answer, the clues that lead there and where it pays off.</summary>
public sealed class MysteryPlan
{
    [AiDescription("The question the party is trying to answer, one sentence.")]
    public string Question { get; set; } = "";
    [AiDescription("The answer, one or two sentences; only the payoff floor may say it.")]
    public string Truth { get; set; } = "";
    public List<CluePlan> Clues { get; set; } = new();
    [AiDescription("The floor where the truth comes out, between 12 and 19.")]
    public int PayoffFloor { get; set; }
    [AiDescription("How the truth changes the payoff floor: what the party finds or who they face, in two sentences.")]
    public string Payoff { get; set; } = "";
}

public sealed class FloorOutline
{
    public int Floor { get; set; }
    public string Title { get; set; } = "";
    public string Mood { get; set; } = "";
    [AiDescription("Empty for a quiet floor (at most one spoken line, no NPC). For a story floor: the beat in one or two sentences naming the cast id who appears, the clue or choice or betrayal that happens, and any earlier flag it answers.")]
    public string Story { get; set; } = "";
    [AiDescription("The floor's look: one of the theme ids listed for its tier, or null for the game's own pick.")]
    [AiNotNull] public string? Theme { get; set; }
    [AiDescription("The floor's shape: one of the layout ids listed (standard, labyrinth, ring, mine, flooded, burning), or null for the game's own deal. Boss floors take standard, ring, flooded or burning only.")]
    [AiNotNull] public string? Layout { get; set; }
    [AiDescription("What the floor asks beyond the stairs: one of the objective ids listed (jailbreak, toxic, ritual, shiftingvault), or null for none or the game's own deal. Never floor 1 or a boss floor, never two objective floors in a row, about eight in twenty; toxic needs two heights (not labyrinth or flooded), shiftingvault not labyrinth.")]
    [AiNotNull] public string? Objective { get; set; }
    public SpecOverrides SpecOverrides { get; set; } = new();
}

/// <summary>One thing the antagonist says when something happens. Coarse by default; the clean twin is used when cursing is off or a voice provider refuses the words.</summary>
public sealed class TauntPlan
{
    [AiDescription("When it is said: FloorEnter, Capture, Freed, AllFreed, GasThickens, GasLethal, WrongLever, GateShift, VaultOpened, RitualStart, RitualistDown, WaveBreak, RitualDone, ObjectiveDone, Idle, MemberDowned, PartyWiped, Descend, Reinforcements.")]
    public string Trigger { get; set; } = "";
    [AiDescription("The objective kind the line belongs to (jailbreak, toxic, ritual, shiftingvault) or null for any floor; FloorEnter lines should name one.")]
    [AiNotNull] public string? Objective { get; set; }
    [AiDescription("What the antagonist says, in their own voice, at most 220 characters; may curse. Tokens {party}, {floor} and {prisoner} are filled in.")]
    public Line Line { get; set; } = new();
    [AiDescription("The same line with the cursing taken out, for players who turned it off.")]
    [AiNotNull] public Line? CleanLine { get; set; }
    [AiNotNull] public string? RequiresFlag { get; set; }
    [AiNotNull] public string? ForbidsFlag { get; set; }
    /// <summary>Added from the stock bank, not written for this campaign.</summary>
    [AiIgnore] public bool FromStock { get; set; }
}

public sealed class BarkLine
{
    public BarkTrigger Trigger { get; set; }
    public Line Line { get; set; } = new();
}

/// <summary>All of one party member's barks.</summary>
public sealed class BarkSet
{
    public string Member { get; set; } = "";
    public List<BarkLine> Lines { get; set; } = new();
}

/// <summary>The arc of a whole run, written once at New Game. Floors are detailed later as <see cref="FloorPlan"/>s.</summary>
public sealed class CampaignPlan
{
    [AiIgnore] public int SchemaVersion { get; set; } = PlanFormat.CurrentVersion;
    [AiIgnore] public string Source { get; set; } = PlanSources.Offline;
    [AiIgnore] public ulong GameSeed { get; set; }
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public VoicedCharacter Antagonist { get; set; } = new();
    [AiIgnore] public string? NarratorVoiceId { get; set; }
    [AiDescription("Four to six recurring characters the party meets more than once.")]
    public List<CastMember> Cast { get; set; } = new();
    public MysteryPlan Mystery { get; set; } = new();
    public List<FactionPlan> Factions { get; set; } = new();
    public List<TierPlan> Tiers { get; set; } = new();
    public List<FloorOutline> Floors { get; set; } = new();
    [AiDescription("Leave empty in the outline; barks are written in a separate call.")]
    public List<BarkSet> Barks { get; set; } = new();
    [AiDescription("The antagonist's taunts, thrown at the party as the floors' trials play out: two or three per trigger, spiteful and theatrical, free to curse (period oaths and modern profanity both), each with a clean twin.")]
    public List<TauntPlan> Taunts { get; set; } = new();
    /// <summary>Free-form continuity notes the planner keeps for itself between floors.</summary>
    public string ArcNotes { get; set; } = "";

    public FloorOutline? OutlineFor(int floor) => Floors.FirstOrDefault(f => f.Floor == floor);
    public CastMember? CastFor(string? id) => id == null ? null : Cast.FirstOrDefault(c => c.Id == id);
    public bool IsStoryFloor(int floor) => !string.IsNullOrWhiteSpace(OutlineFor(floor)?.Story);
    public TierPlan? TierFor(int tierIndex) => Tiers.FirstOrDefault(t => t.Index == tierIndex);
}
