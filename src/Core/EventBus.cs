using DungeonCrawler.Items;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// Gameplay -> UI event hub. Gameplay code raises these; UI subscribes in _Ready and unsubscribes in _ExitTree.
/// Uses plain C# events (not Godot signals) so payloads can be plain C# types.
/// Registered as an autoload so it exists for the whole session.
/// </summary>
public partial class EventBus : Node
{
    public static EventBus Instance { get; private set; } = null!;

    /// <summary>(newFloorIndex, delta) where delta is +1 descending, -1 ascending, 0 reload.</summary>
    public event Action<int, int>? FloorChanged;
    /// <summary>A party member's stats changed (hp, mana, cooldown). Payload is the member index.</summary>
    public event Action<int>? MemberChanged;
    public event Action<ItemInstance>? ItemPickedUp;
    /// <summary>Free-text line for the HUD message log.</summary>
    public event Action<string>? Message;
    /// <summary>A sound cue name (see AudioManager.Cues).</summary>
    public event Action<string>? Sfx;
    /// <summary>An enemy just died (raised once, before loot drops).</summary>
    public event Action<Enemies.Enemy>? EnemyDied;
    public event Action<Adventure.QuestState>? QuestChanged;
    /// <summary>A Recover-quest item entered the bag (payload: live quest key).</summary>
    public event Action<string>? QuestItemPicked;
    public event Action<string, Adventure.NpcEventKind>? NpcEvent;
    /// <summary>The player picked a dialogue option with an effect (npc id, effect).</summary>
    public event Action<string, Adventure.DialogueEffect, string?>? DialogueEffectChosen;
    /// <summary>Narrator or character line for the subtitle strip (text, voice line id).</summary>
    public event Action<string, string?>? Narration;
    /// <summary>The HUD objective line (empty to clear).</summary>
    public event Action<string>? ObjectiveChanged;
    public event Action<int>? BossDefeated;
    /// <summary>One of ours hit something (member, result).</summary>
    public event Action<Characters.CharacterState, Combat.DamageResult>? HitLanded;
    public event Action<int, float>? MemberDamaged;
    public event Action<int>? MemberDowned;
    public event Action? PartyWiped;
    public event Action<int, Combat.AbilityKind>? AbilityUsed;
    public event Action<int>? LeadChanged;
    /// <summary>Members were added, parked or unparked: the HUD rebuilds its portraits.</summary>
    public event Action? RosterChanged;
    public event Action<Enemies.Enemy>? BossSeen;
    /// <summary>An alarm runner reached its target collective; hordes set to the alarm break loose.</summary>
    public event Action<string>? AlarmRaised;
    /// <summary>A chest was opened at a world position; hordes set to a chest in their room break loose.</summary>
    public event Action<Vector3>? ChestOpened;
    /// <summary>A member's hit finished off a target.</summary>
    public event Action<Characters.CharacterState>? MemberKill;
    public event Action<int>? LevelUp;
    public event Action<int>? MemberRevived;
    // ---- world hooks a dedicated server forwards to its clients ----
    public event Action<Items.LootDrop>? LootSpawned;
    public event Action<Guid, int>? LootRemoved;
    /// <summary>(net ref such as "door:1", what happened: opened, pulled, revealed, disarmed, sprung, broken).</summary>
    public event Action<string, string>? PropChanged;
    public event Action<Enemies.Enemy, float, bool>? EnemyHurt;
    public event Action<Combat.Projectile>? ProjectileFired;
    public event Action<Dungeon.Props.Trap>? TrapPlaced;
    /// <summary>(floor, delta, first visit): Main finished building and restoring a floor.</summary>
    public event Action<int, int, bool>? FloorBuilt;
    /// <summary>An enemy that was not in the plan appeared (summon, horde).</summary>
    public event Action<Enemies.Enemy>? EnemySpawned;
    public event Action<string>? HordeTriggered;
    public event Action<string>? HordeSpent;
    /// <summary>(member name, line): a companion spoke; a client plays the voice with its own key.</summary>
    public event Action<string, Adventure.Line>? Bark;

    public void PlaySfx(string cue) => Sfx?.Invoke(cue);

    public override void _Ready() => Instance = this;

    public void RaiseFloorChanged(int floorIndex, int delta) => FloorChanged?.Invoke(floorIndex, delta);
    public void RaiseMemberChanged(int index) => MemberChanged?.Invoke(index);
    public void RaiseItemPickedUp(ItemInstance item) => ItemPickedUp?.Invoke(item);
    public void RaiseEnemyDied(Enemies.Enemy enemy) => EnemyDied?.Invoke(enemy);
    public void RaiseQuestChanged(Adventure.QuestState quest) => QuestChanged?.Invoke(quest);
    public void RaiseQuestItemPicked(string questKey) => QuestItemPicked?.Invoke(questKey);
    public void RaiseNpcEvent(string npcId, Adventure.NpcEventKind kind) => NpcEvent?.Invoke(npcId, kind);
    public void RaiseDialogueEffect(string npcId, Adventure.DialogueEffect effect, string? flag = null) => DialogueEffectChosen?.Invoke(npcId, effect, flag);
    public void RaiseNarration(string text, string? voiceId) { Log(text); Narration?.Invoke(text, voiceId); }
    public void RaiseObjectiveChanged(string text) => ObjectiveChanged?.Invoke(text);
    public void RaiseBossDefeated(int floor) => BossDefeated?.Invoke(floor);
    public void RaiseHitLanded(Characters.CharacterState m, Combat.DamageResult r) => HitLanded?.Invoke(m, r);
    public void RaiseMemberDamaged(int index, float amount) => MemberDamaged?.Invoke(index, amount);
    public void RaiseMemberDowned(int index) => MemberDowned?.Invoke(index);
    public void RaisePartyWiped() => PartyWiped?.Invoke();
    public void RaiseAbilityUsed(int index, Combat.AbilityKind kind) => AbilityUsed?.Invoke(index, kind);
    public void RaiseLeadChanged(int index) => LeadChanged?.Invoke(index);
    public void RaiseRosterChanged() => RosterChanged?.Invoke();
    public void RaiseBossSeen(Enemies.Enemy boss) => BossSeen?.Invoke(boss);
    public void RaiseAlarmRaised(string collectiveId) => AlarmRaised?.Invoke(collectiveId);
    public void RaiseChestOpened(Vector3 at) => ChestOpened?.Invoke(at);
    public void RaiseMemberKill(Characters.CharacterState m) => MemberKill?.Invoke(m);
    public void RaiseLevelUp(int index) => LevelUp?.Invoke(index);
    public void RaiseMemberRevived(int index) => MemberRevived?.Invoke(index);
    public void RaiseLootSpawned(Items.LootDrop drop) => LootSpawned?.Invoke(drop);
    public void RaiseLootRemoved(Guid id, int byMember) => LootRemoved?.Invoke(id, byMember);
    public void RaisePropChanged(string netRef, string what) => PropChanged?.Invoke(netRef, what);
    public void RaiseEnemyHurt(Enemies.Enemy enemy, float amount, bool crit) => EnemyHurt?.Invoke(enemy, amount, crit);
    public void RaiseProjectileFired(Combat.Projectile p) => ProjectileFired?.Invoke(p);
    public void RaiseTrapPlaced(Dungeon.Props.Trap trap) => TrapPlaced?.Invoke(trap);
    public void RaiseFloorBuilt(int floor, int delta, bool firstVisit) => FloorBuilt?.Invoke(floor, delta, firstVisit);
    public void RaiseEnemySpawned(Enemies.Enemy enemy) => EnemySpawned?.Invoke(enemy);
    public void RaiseHordeTriggered(string id) => HordeTriggered?.Invoke(id);
    public void RaiseHordeSpent(string id) => HordeSpent?.Invoke(id);
    public void RaiseBark(string member, Adventure.Line line) => Bark?.Invoke(member, line);
    public void Log(string text)
    {
        GD.Print($"[Log] {text}");
        Message?.Invoke(text);
    }
}
