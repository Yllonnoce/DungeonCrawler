using DungeonCrawler.Adventure.Ai;

namespace DungeonCrawler.Adventure;

public enum RoomRole { Passage, Start, Exit, Arena, Treasury, Gate, Shrine, Cell, Barracks, Kennel, Lair, Hall, Camp, Ruin, Jail }
public enum GoalKind { Guard, Defend, Patrol, Alarm }
public enum ObjectiveKind { Chest, Lever, Door, Stairs, Npc, Gate }
public enum QuestTemplate { KillTarget, Recover, Escort, HoldOut, Investigate, Decide, Betrayal }
public enum NarrationTrigger { Enter, RoomEnter, BossSeen, BossDead, QuestDone, QuestFailed }
public enum DialogueEffect { None, AcceptQuest, DeclineQuest, TurnIn, Farewell, Choose, Betray }
/// <summary>What lets a horde loose: the party stepping into the nest room, any collective raising the alarm, or a chest opened in the nest room.</summary>
public enum HordeTrigger { Enter, Alarm, Chest }

/// <summary>A spoken or written line. VoiceId is filled in once audio has been rendered for it.</summary>
public sealed class Line
{
    public string Text { get; set; } = "";
    [AiIgnore] public string? VoiceId { get; set; }
    [AiDescription("How this line is delivered when not in the character's usual manner: one of whisper, weary, calm, warm, cheerful, sly, pleading, angry, mocking, shout, booming, frantic; null for the character's default.")]
    [AiNotNull] public string? Delivery { get; set; }
    public Line() { }
    public Line(string text) => Text = text;
}

public sealed class RoomPlan
{
    public int Index { get; set; }
    public RoomRole Role { get; set; } = RoomRole.Passage;
    public string Name { get; set; } = "";
}

public sealed class MemberPlan
{
    public string EnemyId { get; set; } = "";
    public int Count { get; set; } = 1;
}

public sealed class ObjectivePlan
{
    public ObjectiveKind Kind { get; set; }
    /// <summary>Chest spawn index, vault index (Lever/Door), NPC id, "down"/"up" for stairs, or the room index of a gate.</summary>
    [AiDescription("Chest: spawn index. Lever/Door: vault index. Npc: npc id. Stairs: \"down\" or \"up\". Gate: the index of the room flagged gate.")]
    public string Ref { get; set; } = "";
}

public sealed class GoalPlan
{
    public GoalKind Kind { get; set; } = GoalKind.Guard;
    /// <summary>Room the group is posted in (Patrol: the first room of the route).</summary>
    public int Room { get; set; }
    [AiNotNull] public ObjectivePlan? Objective { get; set; }
    /// <summary>Patrol: room indices visited in order (loops back to the first).</summary>
    public List<int> Route { get; set; } = new();
    /// <summary>Alarm: id of the collective a runner fetches.</summary>
    [AiNotNull] public string? AlarmTarget { get; set; }
    /// <summary>Room the group falls back to when it decides to regroup.</summary>
    [AiNotNull] public int? RallyRoom { get; set; }
}

public sealed class CollectivePlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [AiNotNull] public string? FactionId { get; set; }
    public List<MemberPlan> Members { get; set; } = new();
    public GoalPlan Goal { get; set; } = new();
}

/// <summary>Swarm creatures streaming out of a nest room's breaches once triggered. Outside the enemy budget.</summary>
public sealed class HordePlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [AiDescription("A room flagged nest.")]
    public int Room { get; set; }
    [AiDescription("What pours out: mostly roster entries marked horde; from floor 4 a few of the tier's own creatures may march with them (at most a quarter of the total). Total 3 to 60: a handful on floor 1, fifty or sixty by floor 20.")]
    public List<MemberPlan> Members { get; set; } = new();
    /// <summary>Older plans named one creature and a count; the validator folds these into Members.</summary>
    [AiIgnore] public string? EnemyId { get; set; }
    [AiIgnore] public int? Count { get; set; }
    public int Total => Members.Sum(m => m.Count);
    public HordeTrigger Trigger { get; set; } = HordeTrigger.Enter;
    [AiDescription("Narration when the horde breaks loose.")]
    public Line Line { get; set; } = new();
}

public sealed class LonerPlan
{
    public string EnemyId { get; set; } = "";
    public int Room { get; set; }
}

public sealed class BossPlan
{
    public string Name { get; set; } = "";
    /// <summary>Slam, Volley, Charge or Summon; null keeps the enemy's own signature.</summary>
    [AiNotNull] public string? SignatureOverride { get; set; }
    public Line IntroLine { get; set; } = new();
    public Line DeathLine { get; set; } = new();
}

public sealed class ChestPlan
{
    public int Room { get; set; }
    public bool Locked { get; set; }
    [AiIgnore] public string? QuestId { get; set; }
}

public sealed class TrapPlan
{
    public int Room { get; set; }
    /// <summary>Spikes, Fire or Gas.</summary>
    public string Kind { get; set; } = "Spikes";
}

public sealed class ChoicePlan
{
    public string Text { get; set; } = "";
    [AiNotNull] public string? Next { get; set; }
    public DialogueEffect Effect { get; set; } = DialogueEffect.None;
    [AiDescription("With Effect Choose: the flag this choice sets for the rest of the run (a short snake_case word such as spared_the_warden). Null otherwise.")]
    [AiNotNull] public string? Flag { get; set; }
    [AiDescription("Only offered when this flag was set on an earlier floor. Null for always.")]
    [AiNotNull] public string? RequiresFlag { get; set; }
    [AiDescription("Hidden once this flag was set. Null for always.")]
    [AiNotNull] public string? ForbidsFlag { get; set; }
}

public sealed class DialogueNode
{
    public string Id { get; set; } = "";
    public Line Line { get; set; } = new();
    public List<ChoicePlan> Choices { get; set; } = new();
}

/// <summary>Flat dialogue graph: nodes reference each other by id, so it fits a non-recursive JSON schema.</summary>
public sealed class NpcScript
{
    public string Start { get; set; } = "";
    public List<DialogueNode> Nodes { get; set; } = new();
}

/// <summary>An NPC who turns: taken with a choice whose Effect is Betray, they vanish and these creatures stand where they stood.</summary>
public sealed class BetrayalPlan
{
    [AiDescription("A roster id from this floor's menu.")]
    public string EnemyId { get; set; } = "";
    [AiDescription("1 to 4.")]
    public int Count { get; set; } = 1;
}

public sealed class NpcPlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Room { get; set; }
    [AiIgnore] public string? VoiceId { get; set; }
    public NpcScript Script { get; set; } = new();
    [AiNotNull] public string? QuestId { get; set; }
    [AiDescription("The id of a campaign Cast member this is; the NPC then wears that name and voice. Null for a one-off.")]
    [AiNotNull] public string? CastId { get; set; }
    [AiDescription("How this person sounds: VoiceStyle (a few words, at most 120 characters, e.g. \"a gruff old smith, gravel voice, dry humour\"), DefaultDelivery (one of the delivery ids), Voice (one of the voice names listed); null takes the cast member's or a plain voice.")]
    [AiNotNull] public Voice.Persona? Persona { get; set; }
    [AiDescription("The NPC only appears when this flag was set on an earlier floor. Null for always.")]
    [AiNotNull] public string? RequiresFlag { get; set; }
    [AiDescription("The NPC stays away once this flag was set. Null for always.")]
    [AiNotNull] public string? ForbidsFlag { get; set; }
    [AiDescription("Set when a dialogue choice with Effect Betray makes this NPC turn on the party. Null otherwise.")]
    [AiNotNull] public BetrayalPlan? Betrayal { get; set; }
}

public sealed class QuestParams
{
    [AiNotNull] public string? TargetCollective { get; set; }
    [AiNotNull] public string? ItemName { get; set; }
    [AiNotNull] public int? ItemRoom { get; set; }
    [AiNotNull] public string? EscortNpc { get; set; }
    [AiNotNull] public int? Waves { get; set; }
    [AiNotNull] public string? WaveCollective { get; set; }
    [AiDescription("Investigate: 2 to 4 distinct rooms (not the start room) where one clue each lies.")]
    public List<int> ClueRooms { get; set; } = new();
    [AiDescription("Investigate: what the clues are, at most 40 characters (a torn ledger page, a wax seal).")]
    [AiNotNull] public string? ClueName { get; set; }
}

public sealed class QuestReward
{
    public int Xp { get; set; }
    [AiNotNull] public string? Table { get; set; }
}

public sealed class QuestLines
{
    public Line Offer { get; set; } = new();
    public Line Accept { get; set; } = new();
    public Line Decline { get; set; } = new();
    public Line Complete { get; set; } = new();
    public Line Fail { get; set; } = new();
}

public sealed class QuestPlan
{
    public string Id { get; set; } = "";
    public QuestTemplate Template { get; set; }
    public string Title { get; set; } = "";
    public string Objective { get; set; } = "";
    public QuestParams Params { get; set; } = new();
    public string GiverNpc { get; set; } = "";
    public QuestReward Reward { get; set; } = new();
    public QuestLines Lines { get; set; } = new();
    [AiDescription("The quest is only offered when this flag was set on an earlier floor. Null for always.")]
    [AiNotNull] public string? RequiresFlag { get; set; }
    [AiDescription("The quest is withheld once this flag was set. Null for always.")]
    [AiNotNull] public string? ForbidsFlag { get; set; }
}

public sealed class NarrationPlan
{
    public NarrationTrigger Trigger { get; set; }
    [AiNotNull] public int? Room { get; set; }
    public Line Line { get; set; } = new();
    [AiDescription("Spoken only when this flag was set on an earlier floor. Null for always.")]
    [AiNotNull] public string? RequiresFlag { get; set; }
    [AiDescription("Skipped once this flag was set. Null for always.")]
    [AiNotNull] public string? ForbidsFlag { get; set; }
}

/// <summary>Flags set by dialogue choices: the run remembers them and later floors check them.</summary>
public static class FlagRules
{
    public const int MaxLength = 40;

    /// <summary>Normalise a flag id: lower snake_case, at most 40 characters; null when nothing usable remains.</summary>
    public static string? Clean(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return null;
        var chars = flag.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var text = new string(chars).Trim('_');
        while (text.Contains("__")) text = text.Replace("__", "_");
        if (text.Length == 0) return null;
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    /// <summary>Whether something gated by these two flags is available now.</summary>
    public static bool Allowed(string? requires, string? forbids, IReadOnlyCollection<string> flags)
        => (requires == null || flags.Contains(requires)) && (forbids == null || !flags.Contains(forbids));
}

/// <summary>
/// Everything a designer (offline or AI) decides about one generated floor. Validated and resolved against the
/// <c>FloorLayout</c> before use; stored in the save so a reload rebuilds the same floor.
/// </summary>
public sealed class FloorPlan
{
    [AiIgnore] public int SchemaVersion { get; set; } = PlanFormat.CurrentVersion;
    [AiIgnore] public int Floor { get; set; }
    [AiIgnore] public string Source { get; set; } = PlanSources.Offline;
    [AiIgnore] public ulong LayoutHash { get; set; }
    [AiDescription("At most 60 characters; the floor's name, usually its most important room.")]
    public string Title { get; set; } = "";
    public Line IntroLine { get; set; } = new();
    public List<RoomPlan> Rooms { get; set; } = new();
    public List<CollectivePlan> Collectives { get; set; } = new();
    public List<LonerPlan> Loners { get; set; } = new();
    public List<HordePlan> Hordes { get; set; } = new();
    [AiNotNull] public BossPlan? Boss { get; set; }
    public List<ChestPlan> Chests { get; set; } = new();
    public List<TrapPlan> Traps { get; set; } = new();
    public List<NpcPlan> Npcs { get; set; } = new();
    public List<QuestPlan> Quests { get; set; } = new();
    public List<NarrationPlan> Narration { get; set; } = new();
    [AiDescription("Taunts the antagonist keeps for this floor in particular (its story, its cast), on top of the campaign's; usually empty.")]
    public List<TauntPlan> Taunts { get; set; } = new();

    public RoomPlan? RoomPlanFor(int index) => Rooms.FirstOrDefault(r => r.Index == index);
    public int EnemyTotal => Collectives.Sum(c => c.Members.Sum(m => m.Count)) + Loners.Count;
}

public static class PlanSources
{
    public const string Offline = "offline";
    public const string Claude = "claude";
    public const string Builtin = "builtin";
}
