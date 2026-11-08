namespace DungeonCrawler.Enemies;

/// <summary>
/// The twelve elite affixes, in the spec's order (section 2 of the Depths design). Every number lives in <see cref="EliteRules"/>.
/// Shared members verified 2026-11-08 against feature/depths@92a6d37: EventBus.EliteSeen/EliteSlain (Action&lt;Enemy&gt;) present;
/// StatusKind.Vulnerable present with the +25% intake; Enemy.Stagger(float, bool) and Enemy.StaggerImmune present; BossBar.ShowSlim/HideSlim
/// present (the elites plan subscribes them); Adventurer.ReceiveAttack was void (Task 6 returns DamageResult); FloorRecord.Elites was missing (added here).
/// </summary>
public enum EliteAffix { Swift, Hulking, Warded, Vampiric, Volatile, Splitting, Thorned, Cursing, Shrouded, Hexed, Alarmist, Juggernaut }
