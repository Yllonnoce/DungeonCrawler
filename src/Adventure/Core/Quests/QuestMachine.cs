namespace DungeonCrawler.Adventure;

public enum QuestStatus { Offered, Active, ReadyToTurnIn, Completed, Failed }

/// <summary>Live state of one side quest. Saved as-is; mutated only through <see cref="QuestMachine.Apply"/>.</summary>
public sealed class QuestState
{
    public string Id { get; set; } = "";
    public int Floor { get; set; }
    public QuestTemplate Template { get; set; }
    public QuestStatus Status { get; set; } = QuestStatus.Offered;
    public string Title { get; set; } = "";
    public string GiverNpc { get; set; } = "";
    public string GiverName { get; set; } = "";
    public int Progress { get; set; }
    public int Goal { get; set; } = 1;
    public string? TargetCollective { get; set; }
    public string TargetName { get; set; } = "";
    public string? EscortNpc { get; set; }
    public string EscortName { get; set; } = "";
    public string? ItemName { get; set; }
    public int? ItemRoom { get; set; }
    public string PlaceName { get; set; } = "";
    public string? WaveCollective { get; set; }
    public int RewardXp { get; set; }
    public string? RewardTable { get; set; }
    public bool RewardGranted { get; set; }
    public string CompleteLine { get; set; } = "";
    public string FailLine { get; set; } = "";
    public string? ClueName { get; set; }
    /// <summary>Decide: the flag the party chose.</summary>
    public string? ChosenFlag { get; set; }

    public bool IsTerminal => Status is QuestStatus.Completed or QuestStatus.Failed;
    public bool IsOpen => Status is QuestStatus.Active or QuestStatus.ReadyToTurnIn;

    /// <summary>One line for the HUD and quest log.</summary>
    public string ObjectiveText => Status switch
    {
        QuestStatus.Offered => $"{GiverName} has a task for you.",
        QuestStatus.ReadyToTurnIn => $"Return to {GiverName}.",
        QuestStatus.Completed => "Completed.",
        QuestStatus.Failed => "Failed.",
        _ => Template switch
        {
            QuestTemplate.KillTarget => $"Slay the {TargetName} ({Progress}/{Goal}).",
            QuestTemplate.Recover => $"Recover the {ItemName} from the {PlaceName}.",
            QuestTemplate.Escort => $"Bring {EscortName} to the stairs down alive.",
            QuestTemplate.HoldOut => $"Hold out with {GiverName} against {Goal} waves ({Progress}/{Goal}).",
            QuestTemplate.Investigate => $"Find the {ClueName ?? "clues"} ({Progress}/{Goal}).",
            QuestTemplate.Decide => $"Decide what to tell {GiverName}.",
            QuestTemplate.Betrayal => $"{GiverName} has turned. Put them down ({Progress}/{Goal}).",
            _ => Title,
        },
    };
}

public abstract record QuestEvent;
public sealed record QuestAccepted(string QuestId) : QuestEvent;
public sealed record QuestDeclined(string QuestId) : QuestEvent;
public sealed record QuestTurnedIn(string QuestId) : QuestEvent;
public sealed record EnemyDiedEvent(string? CollectiveId, string EnemyId, int Floor) : QuestEvent;
public sealed record QuestItemPicked(string QuestId) : QuestEvent;
public sealed record NpcReachedStairs(string NpcId) : QuestEvent;
public sealed record NpcDied(string NpcId) : QuestEvent;
public sealed record WaveCleared(string QuestId, int Wave) : QuestEvent;
public sealed record FloorLeft(int Floor) : QuestEvent;
/// <summary>Decide: a dialogue choice with Effect Choose set <paramref name="Flag"/>.</summary>
public sealed record ChoiceMade(string QuestId, string Flag) : QuestEvent;
/// <summary>Betrayal: the giver turned; its creatures are loose under the group id <see cref="QuestMachine.BetrayalGroup"/>.</summary>
public sealed record BetrayalStarted(string QuestId) : QuestEvent;

/// <summary>Pure rules for the quest templates. The Godot layer feeds it events and reacts to status changes.</summary>
public static class QuestMachine
{
    /// <summary>The pseudo collective id the creatures of a turned NPC die under, so a Betrayal quest can count them.</summary>
    public static string BetrayalGroup(string npcId) => $"betrayal:{npcId}";

    public static QuestState Create(QuestPlan q, FloorPlan plan, int floor, int targetCount)
    {
        string NpcName(string? id) => plan.Npcs.FirstOrDefault(n => n.Id == id)?.Name ?? id ?? "someone";
        var target = plan.Collectives.FirstOrDefault(c => c.Id == q.Params.TargetCollective);
        return new QuestState
        {
            Id = q.Id, Floor = floor, Template = q.Template, Title = q.Title,
            GiverNpc = q.GiverNpc, GiverName = NpcName(q.GiverNpc),
            TargetCollective = q.Params.TargetCollective, TargetName = target?.Name ?? "enemy",
            EscortNpc = q.Params.EscortNpc, EscortName = NpcName(q.Params.EscortNpc),
            ItemName = q.Params.ItemName, ItemRoom = q.Params.ItemRoom,
            PlaceName = q.Params.ItemRoom is { } r ? plan.RoomPlanFor(r)?.Name ?? "chamber" : "",
            WaveCollective = q.Params.WaveCollective,
            ClueName = q.Params.ClueName,
            Goal = q.Template switch
            {
                QuestTemplate.KillTarget => Math.Max(1, targetCount),
                QuestTemplate.HoldOut => Math.Max(1, q.Params.Waves ?? 2),
                QuestTemplate.Investigate => Math.Max(1, q.Params.ClueRooms.Count),
                QuestTemplate.Betrayal => Math.Max(1, plan.Npcs.FirstOrDefault(n => n.Id == q.GiverNpc)?.Betrayal?.Count ?? 1),
                _ => 1,
            },
            RewardXp = q.Reward.Xp, RewardTable = q.Reward.Table,
            CompleteLine = q.Lines.Complete.Text, FailLine = q.Lines.Fail.Text,
        };
    }

    /// <summary>Apply an event; true when the quest changed.</summary>
    public static bool Apply(QuestState q, QuestEvent e)
    {
        if (q.IsTerminal) return false;
        switch (e)
        {
            case QuestAccepted a when a.QuestId == q.Id && q.Status == QuestStatus.Offered:
                q.Status = QuestStatus.Active;
                return true;
            case QuestDeclined d when d.QuestId == q.Id:
                return false;
            case QuestTurnedIn t when t.QuestId == q.Id && q.Status == QuestStatus.ReadyToTurnIn:
                q.Status = QuestStatus.Completed;
                return true;
            case EnemyDiedEvent died when q.Status == QuestStatus.Active && q.Template == QuestTemplate.KillTarget && died.Floor == q.Floor && died.CollectiveId != null && died.CollectiveId == q.TargetCollective:
                q.Progress++;
                if (q.Progress >= q.Goal) q.Status = QuestStatus.ReadyToTurnIn;
                return true;
            case QuestItemPicked p when p.QuestId == q.Id && q.Status == QuestStatus.Active && q.Template == QuestTemplate.Recover:
                q.Progress = 1;
                q.Status = QuestStatus.ReadyToTurnIn;
                return true;
            case QuestItemPicked p2 when p2.QuestId == q.Id && q.Status == QuestStatus.Active && q.Template == QuestTemplate.Investigate:
                q.Progress = Math.Min(q.Goal, q.Progress + 1);
                if (q.Progress >= q.Goal) q.Status = QuestStatus.ReadyToTurnIn;
                return true;
            case ChoiceMade c when c.QuestId == q.Id && q.Template == QuestTemplate.Decide && q.Status is QuestStatus.Offered or QuestStatus.Active:
                q.ChosenFlag = c.Flag;
                q.Progress = 1;
                q.Status = QuestStatus.Completed;
                return true;
            case BetrayalStarted b when b.QuestId == q.Id && q.Template == QuestTemplate.Betrayal && q.Status is QuestStatus.Offered or QuestStatus.Active:
                q.Status = QuestStatus.Active;
                q.Progress = 0;
                return true;
            case EnemyDiedEvent turned when q.Status == QuestStatus.Active && q.Template == QuestTemplate.Betrayal && turned.Floor == q.Floor && turned.CollectiveId == BetrayalGroup(q.GiverNpc):
                q.Progress++;
                if (q.Progress >= q.Goal) q.Status = QuestStatus.Completed;
                return true;
            case NpcReachedStairs s when q.Status == QuestStatus.Active && q.Template == QuestTemplate.Escort && s.NpcId == q.EscortNpc:
                q.Progress = 1;
                q.Status = QuestStatus.Completed;
                return true;
            case NpcDied n when q.Template != QuestTemplate.Betrayal && q.Status is QuestStatus.Active or QuestStatus.ReadyToTurnIn && (n.NpcId == q.EscortNpc || n.NpcId == q.GiverNpc):
                q.Status = QuestStatus.Failed;
                return true;
            case NpcDied n2 when q.Template != QuestTemplate.Betrayal && q.Status == QuestStatus.Offered && n2.NpcId == q.GiverNpc:
                q.Status = QuestStatus.Failed;
                return true;
            case WaveCleared w when w.QuestId == q.Id && q.Status == QuestStatus.Active && q.Template == QuestTemplate.HoldOut:
                q.Progress = Math.Max(q.Progress, w.Wave);
                if (q.Progress >= q.Goal) q.Status = QuestStatus.ReadyToTurnIn;
                return true;
            case FloorLeft f when f.Floor == q.Floor && q.Status == QuestStatus.Active && q.Template is QuestTemplate.Escort or QuestTemplate.HoldOut or QuestTemplate.Betrayal:
                q.Status = QuestStatus.Failed;
                return true;
        }
        return false;
    }
}
