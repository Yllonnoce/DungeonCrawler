# Run Structure Implementation Plan (Depths pillar 5: descents that differ, deaths that count)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run two is a different run (New Descent, Daily Descent), the contract has clauses that raise heat and reward, a wipe costs gold and leaves a scar and a death card (the Death Clause ends the run and leaves a memorial), every descent prints a floor card, victory prints a recap that names the campaign's own boss and is saved, and a profile on disk remembers records, the last twenty runs and twenty-four deeds that widen the pool.

**Architecture:** Engine-free rules under `src/Adventure/Core/Run/` (`ClauseRules` with an `Active` clause set the Godot side reads, `DailyRules`, `ScoreRules`, `DeedRules` + `DeedTracker`, `RecapBuilder`, `LedgerRules`, `InventoryAudit`, `OccupancyRules`, the `WipeRules` extension) and under `src/Save/Core/Profile/` (`ProfileDto`, `ProfileRules`, `ProfileFormat`). The ledger is the existing `FloorRecord` (`CampaignMemory.cs`) widened with the contract's fields and filled by `AdventureDirector` subscribers. Godot glue: `Main` (new game paths, autosave, the wipe hook that captures the floor before the reload, victory), `MainMenu` (eight buttons), new controls under `src/UI/Cards/` (`ClauseCard`, `FloorCard`, `DeathCard`, `RunRecap`, `DeedToast`) exposed through `Hud.Show*`, `RecordsScreen`, `ProfileStore` (atomic `user://profile.json`), `DeedWatcher` (an EventBus listener node). Co-op: the server decides and sends three new messages (`DeathCardEvt`, `FloorCardEvt`, `RunRecapEvt`); clauses ride in the Welcome's `AdventureDto`; `--clauses=` sets them.

**Tech Stack:** Godot 4.7.2 .NET / C#, xUnit (`tests/`, dev machine only; `tests/DungeonCrawler.Tests.csproj` links `../src/Adventure/Core/**`, `../src/Save/Core/**`, `../src/Items/Core/**`, `../src/Net/Core/**`, `../src/Characters/*.cs`, `../src/Dungeon/TierRules.cs` with recursive globs, so new folders under them need no csproj change), headless `--test-run-structure` in `src/Core/DebugTools.cs`.

**Spec:** `docs/superpowers/specs/2026-11-07-the-depths-update-design.md` section 5 (binding), plus the contract `/tmp/.../plan-contract.md` item 8 (shared names). This plan runs after combat-feel, writing, items-and-builds, elites-and-bosses and fortune have landed; Task 1 verifies every member it borrows from them.

## Global Constraints

- Numbers, verbatim from the spec: heat multiplier `1 + 0.1 × heat` on gold and XP found; clauses: Death Clause 3, Convenience Fee 1 (prices ×1.4), Inventory Audit 2 (lowest-level equipped item per member loses an affix at each tier gate: floors 6, 11, 16), Full Occupancy 1–2 (+25% creatures per rank), Hostile Tenants 1–2 (+1 elite per floor per rank), Expedited Delivery 1–2 (portals start at 45 s and shrink twice as fast, per rank), Hazard Pay Waived 1–3 (+20% damage taken per rank, stacks with lethality), No Rest for the Hired 2 (no safe rooms, the merchant by the stairs), Open-Door Policy 1 (ambushes doubled), Fine Print 1 (shrines offer two boons, not three); wipe: −20% gold, *Scar* −10% max HP until the next floor; floor card 4 s, Esc dismisses, never blocks input; solo autosave to `auto` on every floor change and every 180 s; profile keeps the last twenty runs and twenty-four deeds; heat 5 and 10 unlock a title and a border on the records page.
- Engine-free code never references `Godot`; every rule is a `*Rules` class with the numbers as constants; xUnit tests before Godot glue.
- Save format: v10 today; this plan bumps `SaveFormat.CurrentVersion` to 11 with `From10To11` (other Depths plans add defaulted fields under v11). A v10 save loads with no clauses, Story mode, not fallen.
- Co-op is server-authoritative: the server computes causes, cards and recaps and sends them; clients never roll. Anything rolled at floor build (Full Occupancy's extra creatures) is rolled from `(GameSeed, floor, spawn index)`.
- Protocol: three messages are appended (`DeathCardEvt`, `FloorCardEvt`, `RunRecapEvt`), so `Protocol.Version` becomes the live value + 1 and `MessageKindCount` the live value + 3 (`tests/Net/NetCodecTests.cs:229-232` pins both). The sibling plans that land first each bump to 7 and add `CheerIntent` and `ShopBuybackIntent`, so this plan's numbers are written as 8 and 85 below; Task 1 reads `Protocol.cs` and corrects them.
- Build before any headless run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly`. Headless flows use `--no-ambush --no-portals --no-gathering --lethality=1 --seed=7`.
- Text register: dry, in-world (the dungeon is a drowned god's plumbing; the merchant is a factor who speaks in contracts). Stock text never names Gorm, the Verger, Pellet or Ashmere (the shipped `campaign.json` may).
- Files other plans own are touched only at the members this plan names (each task's Interfaces block lists them as "Adds to <file>").

## Review Focus

Spec-implied cases no task's main tests cover; each has its pin named in its owning task.

1. A v10 save (no `Clauses`, no `Run`, no `Fallen`) must load as a Story run with every clause off and the shop at normal prices. Pin: Task 4 `V10Save_LoadsAsStory_NoClauses_NotFallen`.
2. A wipe whose last damage was a poison tick (the bite that applied it landed seconds earlier) must blame the biter, not "generic": status ticks never overwrite `LastHarm`. Pin: Task 9 `CauseOf_PicksTheLatestHarm_AndIgnoresMembersWithNone`.
3. The Daily seed must be the same on every machine and time zone: it is a function of a `DateOnly` built from UTC only. Pin: Task 3 `SeedFor_IsStable_AndDiffersBetweenDays`.
4. Autosave must never fire while a floor is being rebuilt, while the game is paused, on a server or a client, or under the death card. Pin: Task 9 `AutosaveDue_RefusesRebuildPauseNetAndCard`.
5. The Inventory Audit on a member with nothing equipped, or whose only affix line is a legendary's own (Tier 0), must do nothing and never throw. Pin: Task 7 `InventoryAudit_SkipsEmptyMembers_AndNeverStripsALegendaryLine`.

---

### Task 1: Verify the shared surface; widen the ledger; the director's ledger hooks

**Files:**
- Modify: `src/Adventure/Core/Memory/CampaignMemory.cs:6-29` (FloorRecord fields), `src/Adventure/AdventureDirector.cs` (subscriptions at 466-472, `OpenRecord` 383-390, `CloseRecord` 392-401), `src/Core/EventBus.cs` (contract events that are still missing), `src/Characters/CharacterState.cs` (`LastHarm`), `src/Party/Adventurer.cs:897-906,950-976` (set `LastHarm`), `src/Party/PartyController.cs:486-495` (`MemberRescued`)
- Create: `src/Adventure/Core/Run/LedgerRules.cs`, `src/Adventure/Core/Run/HarmCauses.cs`
- Test: `tests/Adventure/Run/LedgerRulesTests.cs`

**Interfaces:**
- Consumes (verify each exists; where a name differs, adapt this plan's later tasks and write the real name in the commit message): `EventBus` events `EliteSeen, EliteSlain, MimicSprung, ShrineUsed, SecretFound, TrapSprung, MemberFell, PerfectBlow, Parried, PerfectBlock, Gambled, Cheer, BrokeAtShop, LowHp, BoonTaken, CurseTaken, PartyWipedBy, FloorLedgerClosed` with `Raise*` methods (contract item 8; add any missing one with exactly the contract's shape); `Adventure.Core.Voice.Epitaphs.Pick(string cause, int wipeCountOnFloor, int floor, Random rng) : string` (writing); `EliteRules.CountFor(int floor, int extra)` and `Enemy.IsElite` (elites); `EnemyProfile.Bomber` (elites' `Bomber` flag; name may differ); fortune's mimic marker on the spawned brute (`Enemy.FromMimic` or similar), `ShrineRules` offer count, the `Curses` registry and `AdventureDto.Curses`; writing's `Banter` bank; items' `LegendaryDef`/`LegendaryProfile` and the ids of *Gerald*, *Gorm's Molar*, *Old Ulf's Chin*, *Hush*, *Pellet's Receipt*, *Toll Road Boots* under `data/items/`; whether `ShopRules`/`Prices` already carry a price multiplier (fortune's *Miser's Mark*). Record findings as a comment block at the top of `ClauseRules.cs` (Task 2) headed `// Shared surface as found on <date>:`.
- Already decided by the sibling plans (read them under `docs/superpowers/plans/2026-11-07-*.md`; fortune's was not written when this plan was): writing gives `Adventure.Core.Voice.WipeCause` (string consts `Gas, Press, Spikes, Boss, Horde, Elite, Fall, Mimic, Bomber, Generic`, `FromHazardSource`) and `Epitaphs.Pick(string cause, int wipesOnFloor, int floor)` (three arguments, no rng: deterministic by itself); elites give `EnemyProfile.IsElite`, `Enemy.IsElite`, `EnemyProfile.Bomber`, `EliteRules.CountFor(floor, extra)` and `AdventurePlanning.ResolveStored(..., int extraElites = 0)` / `LegacyPopulator.Populate(..., int extraElites = 0)`; combat-feel gives `DamageResult.Shatter`; items give the legendary ids `leg.gerald, leg.gorms_molar, leg.old_ulfs_chin, leg.hush, leg.pellets_receipt, leg.toll_road_boots` and rewrites `LootDrop.cs:85` to `party.AddGold((int)LegendaryRules.GoldFound(Coins, party.GoldFindBonus()))`; combat-feel, writing and items each set `Protocol.Version = 7` and writing/items append one `MsgKind` each (`CheerIntent`, `ShopBuybackIntent`), elites bumps the version once more for `DynamicEnemyDto`: read the live `Protocol.cs` and use *current + 1* for the version and *current + 3* for the count wherever this plan says 8 and 85. `HarmCauses` below reuses `WipeCause`'s ids (same strings) and adds the mapping from an enemy's flags.
- Produces: `FloorRecord` gains `int Elites, SecretsFound, SecretsTotal, Rescues, GoldFound; float BiggestHit; string BiggestHitOn = "", BiggestHitBy = "", WorstMoment = ""; List<FallRecord> Falls; List<string> WipeCauses; Dictionary<string,int> KillsBy, DownsBy, RescuesBy; Dictionary<string,float> DamageBy` (all keyed by roster name); `public sealed class FallRecord { string Member, Cause, Killer }`. `public sealed record HarmSource(string CauseId, string Name, double At);` `CharacterState.LastHarm : HarmSource?` (settable). `public static class HarmCauses { const string Gas="gas", Press="press", Spikes="spikes", Boss="boss", Horde="horde", Elite="elite", Fall="fall", Mimic="mimic", Bomber="bomber", Generic="generic"; static string ForEnemy(bool isBoss, bool isElite, bool isHorde, bool isBomber, bool isMimic); static string FromSource(string sourceName); static string Describe(string cause, string killer) }` (`FromSource`: "The gas"→gas, "The fall"→fall, `Press.SourceName` "The press"→press, `SpikeBed.SourceName` "The spikes"→spikes, anything else→generic; `Describe`: gas→"the gas", press→"the Press", spikes→"the spikes", fall→"the fall", horde→"the horde", else the killer's name or "the dark"). `LedgerRules.WorstMoment(FloorRecord r) : string`. `EventBus`: `event Action<int,int>? MemberRescued; RaiseMemberRescued(int member, int rescuer)`; `event Action<int>? GoldFound; RaiseGoldFound(int coins)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Adventure/Run/LedgerRulesTests.cs
public class LedgerRulesTests
{
    [Fact] public void WorstMoment_PrefersTheWipe()
    {
        var r = new FloorRecord { Floor = 9, WipeCauses = { "press" }, Falls = { new FallRecord { Member = "Sable", Cause = "press", Killer = "The press" } } };
        Assert.Equal("The party fell to the Press", LedgerRules.WorstMoment(r));
    }
    [Fact] public void WorstMoment_ThenAHazardFall_ThenAnyFall()
    {
        Assert.Equal("Sable fell to the Press", LedgerRules.WorstMoment(new FloorRecord { Falls = { new FallRecord { Member = "Brand", Cause = "generic", Killer = "Grukk" }, new FallRecord { Member = "Sable", Cause = "press", Killer = "The press" } } }));
        Assert.Equal("Brand went down to Grukk the Swift", LedgerRules.WorstMoment(new FloorRecord { Falls = { new FallRecord { Member = "Brand", Cause = "elite", Killer = "Grukk the Swift" } } }));
    }
    [Fact] public void WorstMoment_ThenTheBiggestHit_ThenNothing()
    {
        Assert.Equal("Wren took 41 from a skeleton archer", LedgerRules.WorstMoment(new FloorRecord { BiggestHit = 41.2f, BiggestHitOn = "Wren", BiggestHitBy = "a skeleton archer" }));
        Assert.Equal("Nothing worse than a stubbed toe", LedgerRules.WorstMoment(new FloorRecord()));
    }
    [Fact] public void HarmCauses_MapSourcesAndEnemies()
    {
        Assert.Equal("gas", HarmCauses.FromSource("The gas")); Assert.Equal("press", HarmCauses.FromSource("The press")); Assert.Equal("spikes", HarmCauses.FromSource("The spikes"));
        Assert.Equal("fall", HarmCauses.FromSource("The fall")); Assert.Equal("generic", HarmCauses.FromSource("The fire trap"));
        Assert.Equal("boss", HarmCauses.ForEnemy(true, true, false, false, false)); Assert.Equal("mimic", HarmCauses.ForEnemy(false, true, false, false, true));
        Assert.Equal("bomber", HarmCauses.ForEnemy(false, false, true, true, false)); Assert.Equal("elite", HarmCauses.ForEnemy(false, true, false, false, false));
        Assert.Equal("horde", HarmCauses.ForEnemy(false, false, true, false, false)); Assert.Equal("generic", HarmCauses.ForEnemy(false, false, false, false, false));
    }
    [Fact] public void FloorRecord_RoundTripsTheNewFields()
    {
        var r = new FloorRecord { Floor = 2, Elites = 1, SecretsFound = 1, SecretsTotal = 3, Rescues = 2, GoldFound = 77, KillsBy = { ["Ash"] = 4 }, DamageBy = { ["Ash"] = 120.5f }, RescuesBy = { ["Ash"] = 2 } };
        var back = System.Text.Json.JsonSerializer.Deserialize<FloorRecord>(System.Text.Json.JsonSerializer.Serialize(r))!;
        Assert.Equal(3, back.SecretsTotal); Assert.Equal(4, back.KillsBy["Ash"]); Assert.Equal(2, back.RescuesBy["Ash"]);
    }
}
```

Precedence in `WorstMoment`: boss > mimic > elite > others among `ForEnemy` (a boss that is also an elite is "boss"; a mimic brute carries Hulking so mimic beats elite; a bomber is a horde creature so bomber beats horde).

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~LedgerRulesTests"` → build errors (`FallRecord`, `LedgerRules`, `HarmCauses`).
- [ ] **Step 3: Implement**
  - `CampaignMemory.cs`: the fields above (initialised collections, string defaults `""`).
  - `LedgerRules.WorstMoment`: `WipeCauses` first (`$"The party fell to {Describe(cause, killer-of-the-last-fall-with-that-cause)}"`), then the first `Falls` entry whose cause is press/spikes/gas/fall (`$"{Member} fell to {Describe}"`), then the first fall (`$"{Member} went down to {Killer}"`), then `BiggestHit > 0` (`$"{On} took {hit:0} from {By}"`), else `"Nothing worse than a stubbed toe"`.
  - `CharacterState.LastHarm`; `Adventurer.ReceiveAttack` (after `m.TakeDamage(r.Amount)` at :905): `m.LastHarm = new HarmSource(attacker is Enemy e ? HarmCauses.ForEnemy(e.IsBoss, e.IsElite, e.IsHorde, e.Profile.Bomber, <fortune's mimic marker, else false>) : HarmCauses.FromSource(attackerName), attackerName, Game.Instance.PlaySeconds);` `Adventurer.ReceiveHazard` (both branches, before `OnDowned`): `m.LastHarm = new HarmSource(kind == HazardDamage.Crush ? HarmCauses.Press : HarmCauses.Spikes, sourceName, Game.Instance.PlaySeconds);`. The status-tick path in `PartyController._Process:401-407` is left alone on purpose (Review Focus 2). The fall path (`Adventurer.cs:516,524`) already goes through `ReceiveAttack("The fall")`.
  - `PartyController.TickRescues` (:489-494): after `r.Target.Revive();` add `EventBus.Instance.RaiseMemberRescued(r.Target.Index, r.Rescuer.Index);`.
  - `EventBus`: `MemberRescued`, `GoldFound` (and any contract event found missing in Step 0, with the contract's exact shape).
  - `AdventureDirector._Ready` additions (beside :466-472), each guarded by `_record != null`: `MemberKill → KillsBy[m.Name]++`; extend the `HitLanded` line with `DamageBy[m.Name] += r.Amount`; `MemberDowned i → DownsBy[name]++, Falls.Add(new FallRecord { Member = name, Cause = m.LastHarm?.CauseId ?? "generic", Killer = m.LastHarm?.Name ?? "" })`; `MemberDamaged (i, amount) → if (amount > BiggestHit) { BiggestHit = amount; BiggestHitOn = name; BiggestHitBy = m.LastHarm?.Name ?? "" }`; `MemberRescued (member, rescuer) → Rescues++, RescuesBy[rescuerName]++`; `EliteSlain _ → Elites++`; `SecretFound floor → if (floor == _currentFloor) SecretsFound++`; `PartyWipedBy (floor, cause) → WipeCauses.Add(cause)`; `GoldFound n → GoldFound += n`. `OpenRecord`: `_record.SecretsTotal = Math.Max(_record.SecretsTotal, Main.Instance?.Builder.Layout?.Secrets.Count ?? 0)`. `CloseRecord`: after `Completed = true`: `_record.WorstMoment = LedgerRules.WorstMoment(_record); EventBus.Instance.RaiseFloorLedgerClosed(_record.Floor, (int)_record.Seconds);`.
- [ ] **Step 4: Run** — the filter above → PASS; `cd tests && dotnet test --nologo -v q` whole suite PASS (MemorySummaryTests still pass: the summary ignores the new fields); `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Adventure/Core/Memory/CampaignMemory.cs src/Adventure/Core/Run src/Adventure/AdventureDirector.cs src/Core/EventBus.cs src/Characters/CharacterState.cs src/Party/Adventurer.cs src/Party/PartyController.cs tests/Adventure/Run && git commit -m "feat(run): the floor ledger knows who did what, what hurt most, and who last hurt each member"`

### Task 2: ClauseRules

**Files:**
- Create: `src/Adventure/Core/Run/ClauseRules.cs`
- Test: `tests/Adventure/Run/ClauseRulesTests.cs`

**Interfaces:**
- Produces: `public enum Clause { DeathClause, ConvenienceFee, InventoryAudit, FullOccupancy, HostileTenants, ExpeditedDelivery, HazardPayWaived, NoRest, OpenDoor, FinePrint }`; `public sealed record ClauseDef(Clause Id, string Key, string Name, string Description, int HeatPerRank, int MaxRank, string?[] RankUnlockedByDeed)` (`RankUnlockedByDeed[i]` = the deed id rank `i+1` needs, null = always available); `public static class ClauseRules { static readonly ClauseDef[] All; const float HeatStep = 0.1f, ConvenienceFeePrices = 1.4f, FullOccupancyPerRank = 0.25f, ExpeditedGraceSeconds = 45f, HazardPayPerRank = 0.2f, ScarMaxHp = 0.9f; const int OpenDoorAmbushFactor = 2, FinePrintOffers = 2; static readonly int[] AuditFloors = { 6, 11, 16 }; static ClauseDef Def(Clause c); static List<string> Normalize(IEnumerable<string> raw)` (entries `key` or `key:rank`, unknown keys dropped, rank clamped to 1..MaxRank, duplicates keep the higher rank, sorted by `Clause` order); `static List<string> Parse(string flag)` (comma list for `--clauses=`); `static int MaxRankAllowed(ClauseDef def, IReadOnlySet<string> deeds)`; `static ClauseSet Active { get; private set; } = ClauseSet.None; static void Activate(IEnumerable<string>? clauses)` (also sets `ShopRules.ClausePriceMultiplier` and `TierRules.ClauseDamageMultiplier`, Task 7) `; static void Reset()` }`; `public sealed class ClauseSet { IReadOnlyList<string> Clauses; int Rank(Clause c); bool Has(Clause c); int Heat; float RewardMultiplier => 1 + HeatStep*Heat; int ScaleReward(int v) => (int)Math.Round(v * RewardMultiplier); float PriceMultiplier; float EnemyCountMultiplier => 1 + FullOccupancyPerRank*Rank(FullOccupancy); int ExtraElites; int ExpeditedRank; float DamageTakenMultiplier => 1 + HazardPayPerRank*Rank(HazardPayWaived); bool NoSafeRoom; bool AmbushesDoubled; int ShrineOffers(int baseCount) => Has(FinePrint) ? Math.Max(2, baseCount-1) : baseCount; bool Hardcore; bool AuditsAt(int floor); string Summary` ("Convenience Fee, Hazard Pay Waived II · heat 4 · ×1.4 gold and XP"; "No clauses signed." when empty); `static ClauseSet None` }`. Keys: `deathclause, conveniencefee, inventoryaudit, fulloccupancy, hostiletenants, expediteddelivery, hazardpaywaived, norest, opendoor, fineprint`. Rank unlocks (deed ids from Task 6): FullOccupancy rank 2 `small_print`; HostileTenants rank 2 `below_the_lease`; ExpeditedDelivery rank 2 `express_lodgings`; HazardPayWaived rank 2 `tenant_eviction`, rank 3 `plumber`. Descriptions in the merchant's register, e.g. Convenience Fee: "Every price in my ledger carries a forty-per-cent handling charge."; No Rest: "No locked rooms below. I shall wait by the stairs, in the open, and think of you."

- [ ] **Step 1: Write the failing tests**

```csharp
public class ClauseRulesTests
{
    [Fact] public void TenClauses_WithTheSpecsHeat()
    {
        Assert.Equal(10, ClauseRules.All.Length);
        Assert.Equal((3, 1), (ClauseRules.Def(Clause.DeathClause).HeatPerRank, ClauseRules.Def(Clause.DeathClause).MaxRank));
        Assert.Equal((1, 2), (ClauseRules.Def(Clause.FullOccupancy).HeatPerRank, ClauseRules.Def(Clause.FullOccupancy).MaxRank));
        Assert.Equal((1, 3), (ClauseRules.Def(Clause.HazardPayWaived).HeatPerRank, ClauseRules.Def(Clause.HazardPayWaived).MaxRank));
        Assert.Equal(2, ClauseRules.Def(Clause.NoRest).HeatPerRank); Assert.Equal(2, ClauseRules.Def(Clause.InventoryAudit).HeatPerRank);
    }
    [Fact] public void Normalize_ClampsDedupesAndOrders()
    {
        var list = ClauseRules.Normalize(new[] { "hazardpaywaived:9", "fineprint", "nonsense", "fineprint", "HazardPayWaived:2" });
        Assert.Equal(new[] { "hazardpaywaived:3", "fineprint" }, list);
        Assert.Equal(new[] { "deathclause", "opendoor" }, ClauseRules.Parse(" opendoor , deathclause "));
    }
    [Fact] public void HeatAndMultipliers_FollowTheSpec()
    {
        var set = new ClauseSet(ClauseRules.Normalize(new[] { "conveniencefee", "hazardpaywaived:2", "fulloccupancy:2", "expediteddelivery", "hostiletenants:2", "opendoor", "fineprint", "norest", "deathclause" }));
        Assert.Equal(1 + 2 + 2 + 1 + 2 + 1 + 1 + 2 + 3, set.Heat);
        Assert.Equal(1f + 0.1f * set.Heat, set.RewardMultiplier, 3);
        Assert.Equal(1.4f, set.PriceMultiplier); Assert.Equal(1.5f, set.EnemyCountMultiplier); Assert.Equal(1.4f, set.DamageTakenMultiplier, 3);
        Assert.Equal(2, set.ExtraElites); Assert.Equal(1, set.ExpeditedRank); Assert.True(set.NoSafeRoom); Assert.True(set.AmbushesDoubled); Assert.True(set.Hardcore);
        Assert.Equal(2, set.ShrineOffers(3)); Assert.Equal(3, set.ShrineOffers(4));
        Assert.Equal(250, set.ScaleReward(100)); // heat 15: ×2.5
    }
    [Fact] public void ScaleReward_RoundsToNearest() { var s = new ClauseSet(new[] { "conveniencefee" }); Assert.Equal(11, s.ScaleReward(10)); Assert.Equal(0, s.ScaleReward(0)); }
    [Fact] public void None_IsInert()
    {
        var n = ClauseSet.None; Assert.Equal(0, n.Heat); Assert.Equal(1f, n.RewardMultiplier); Assert.Equal(1f, n.PriceMultiplier); Assert.Equal(3, n.ShrineOffers(3)); Assert.False(n.Hardcore); Assert.Equal("No clauses signed.", n.Summary);
    }
    [Fact] public void AuditsAt_TheTierGates() { var s = new ClauseSet(new[] { "inventoryaudit" }); Assert.True(s.AuditsAt(6)); Assert.True(s.AuditsAt(11)); Assert.True(s.AuditsAt(16)); Assert.False(s.AuditsAt(5)); Assert.False(ClauseSet.None.AuditsAt(6)); }
    [Fact] public void MaxRankAllowed_FollowsTheDeeds()
    {
        var hp = ClauseRules.Def(Clause.HazardPayWaived);
        Assert.Equal(1, ClauseRules.MaxRankAllowed(hp, new HashSet<string>()));
        Assert.Equal(2, ClauseRules.MaxRankAllowed(hp, new HashSet<string> { "tenant_eviction" }));
        Assert.Equal(3, ClauseRules.MaxRankAllowed(hp, new HashSet<string> { "tenant_eviction", "plumber" }));
        Assert.Equal(1, ClauseRules.MaxRankAllowed(ClauseRules.Def(Clause.DeathClause), new HashSet<string>()));
    }
    [Fact] public void Activate_SetsTheActiveSet_AndResetClearsIt()
    {
        ClauseRules.Activate(new[] { "deathclause" }); Assert.True(ClauseRules.Active.Hardcore);
        ClauseRules.Reset(); Assert.Same(ClauseSet.None, ClauseRules.Active);
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter ClauseRulesTests` → build errors.
- [ ] **Step 3: Implement** as specified. `Activate` also assigns `Items.ShopRules.ClausePriceMultiplier = Active.PriceMultiplier` and `Dungeon.TierRules.ClauseDamageMultiplier = Active.DamageTakenMultiplier` once those exist (Task 7; until then leave two `// Task 7` comments and add the assignments there).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `git add src/Adventure/Core/Run/ClauseRules.cs tests/Adventure/Run/ClauseRulesTests.cs && git commit -m "feat(run): the ten clauses, their heat and the multipliers they set"`

### Task 3: DailyRules and ScoreRules

**Files:**
- Create: `src/Adventure/Core/Run/DailyRules.cs`, `src/Adventure/Core/Run/ScoreRules.cs`
- Test: `tests/Adventure/Run/DailyRulesTests.cs`, `tests/Adventure/Run/ScoreRulesTests.cs`

**Interfaces:**
- Consumes: `Core.Rng.StableHash`, `Core.Rng.Mix`, `ClauseRules.All`.
- Produces: `public static class DailyRules { static string Key(DateOnly d) => d.ToString("yyyy-MM-dd"); static ulong SeedFor(DateOnly d) => Rng.Mix(Rng.StableHash(Key(d)) ^ 0xDA11DA11UL); static List<string> ClausesFor(DateOnly d)` (two distinct rank-1 clauses, never `deathclause`, chosen with `new Random(unchecked((int)SeedFor(d)))` from `ClauseRules.All` in order, normalized); `static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow)` }`. `public static class ScoreRules { const int DepthPoints = 100, ElitePoints = 25, SecretPoints = 10, WipePenalty = 50; static int Score(int deepestFloor, int heat, int elitesSlain, int secretsFound, int wipes) => Math.Max(0, (int)Math.Round(deepestFloor * DepthPoints * (1 + ClauseRules.HeatStep * heat)) + elitesSlain * ElitePoints + secretsFound * SecretPoints - wipes * WipePenalty) }`. Written formula (goes in the docs): `depth × 100 × (1 + 0.1 × heat) + 25 × elites + 10 × secrets − 50 × wipes`, never below 0.

- [ ] **Step 1: Write the failing tests**

```csharp
public class DailyRulesTests
{
    [Fact] public void SeedFor_IsStable_AndDiffersBetweenDays()
    {
        var d = new DateOnly(2026, 11, 7);
        Assert.Equal(DailyRules.SeedFor(d), DailyRules.SeedFor(new DateOnly(2026, 11, 7)));
        Assert.NotEqual(DailyRules.SeedFor(d), DailyRules.SeedFor(d.AddDays(1)));
        Assert.Equal("2026-11-07", DailyRules.Key(d));
    }
    [Fact] public void ClausesFor_TwoFixedRankOne_NeverTheDeathClause()
    {
        for (var i = 0; i < 60; i++)
        {
            var d = new DateOnly(2026, 1, 1).AddDays(i);
            var c = DailyRules.ClausesFor(d);
            Assert.Equal(2, c.Count); Assert.Equal(2, c.Distinct().Count()); Assert.DoesNotContain("deathclause", c); Assert.All(c, k => Assert.DoesNotContain(":", k));
            Assert.Equal(c, DailyRules.ClausesFor(d));
        }
        Assert.NotEqual(DailyRules.ClausesFor(new DateOnly(2026, 1, 1)), DailyRules.ClausesFor(new DateOnly(2026, 1, 9)));
    }
}
public class ScoreRulesTests
{
    [Fact] public void Score_IsTheWrittenFormula() { Assert.Equal((int)Math.Round(12 * 100 * 1.4) + 3 * 25 + 4 * 10 - 2 * 50, ScoreRules.Score(12, 4, 3, 4, 2)); }
    [Fact] public void Score_NeverNegative() => Assert.Equal(0, ScoreRules.Score(0, 0, 0, 0, 9));
}
```

- [ ] **Step 2: Run to see them fail**; **Step 3: Implement**; **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** — `git add src/Adventure/Core/Run/DailyRules.cs src/Adventure/Core/Run/ScoreRules.cs tests/Adventure/Run && git commit -m "feat(run): the daily's seed and clauses from the UTC date; the score formula"`

### Task 4: Save v11

**Files:**
- Modify: `src/Save/Core/SaveDtos.cs:39-59` (AdventureDto), `src/Save/Core/SaveFormat.cs:9,27-50,52-56` (version, header, migration), `src/Save/SaveManager.cs:13-15,46` (`auto` slot, `DisplayName`, `MarkFallen`), `src/Core/Main.cs:323-336` (`BuildSaveData` sets `Victory`), `src/Core/Game.cs:53-64,80-93,104-117,119-125` (run kind, clause activation)
- Test: `tests/Save/SaveFormatTests.cs` (add), `tests/Save/SaveAdventureTests.cs` (add)

**Interfaces:**
- Produces: `public enum RunKind { Story, Descent, Daily }`; `AdventureDto` gains `RunKind Run = RunKind.Story; string RunId = ""; List<string> Clauses = new(); string? DailyDate; bool Fallen; string? Epitaph; int Wipes` (total this run). `SaveFormat.CurrentVersion = 11`; `SaveMigrations.From10To11` writes `Adventure.Clauses ??= []`, `Adventure.Run ??= "Story"`, `Adventure.Fallen ??= false`, `Adventure.Wipes ??= 0` when `Adventure` is an object. `SaveFormat.ReadHeader` tuple gains `ulong gameSeed, bool fallen, string? epitaph, string run`; `SlotInfo` gains `bool Fallen, string Epitaph, string Run` and its `Summary` reads `"Fallen on floor {Floor} — {Epitaph}"` when fallen. `SaveManager.AutoSlot = "auto"`, `AllSlots` = auto, quick, slot1..3; `DisplayName("auto") == "Autosave"`; `SaveManager.MarkFallen(ulong gameSeed, string epitaph)` rewrites every existing slot whose header seed matches with `Adventure.Fallen = true, Epitaph = epitaph` (through `Read`/`Write`). `Main.BuildSaveData` sets `Victory = game.Victory` (the bug). `Game.PrepareNewGame(ulong? seed = null, RunKind run = RunKind.Story)` sets `Adventure.Run = run; Adventure.RunId = Guid.NewGuid().ToString("N")` and calls `ClauseRules.Activate(Adventure.Clauses)`; `BeginLoaded`/`BeginNetwork` call `ClauseRules.Activate(Adventure.Clauses)` after assigning `Adventure`; `EndSession` calls `ClauseRules.Reset()`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Save/SaveFormatTests.cs (add)
[Fact] public void CurrentVersion_Is11() => Assert.Equal(11, SaveFormat.CurrentVersion);
[Fact] public void V10Save_LoadsAsStory_NoClauses_NotFallen()
{
    var json = """{"Version":10,"GameSeed":7319,"CurrentFloor":4,"Party":{"Gold":30},"Floors":{},"Adventure":{"Mode":"Builtin"}}""";
    var data = SaveFormat.Deserialize(json);
    Assert.Equal(11, data.Version); Assert.Equal(RunKind.Story, data.Adventure!.Run); Assert.Empty(data.Adventure.Clauses); Assert.False(data.Adventure.Fallen); Assert.Null(data.Adventure.Epitaph);
    Assert.Equal(1f, new ClauseSet(data.Adventure.Clauses).PriceMultiplier);
}
[Fact] public void ClausesRunAndMemorial_RoundTrip()
{
    var data = new SaveData { GameSeed = 5, Victory = true, Adventure = new AdventureDto { Run = RunKind.Daily, DailyDate = "2026-11-07", Clauses = { "fineprint", "opendoor" }, Fallen = true, Epitaph = "He has started using pencil.", Wipes = 4, RunId = "abc" } };
    var back = SaveFormat.Deserialize(SaveFormat.Serialize(data));
    Assert.True(back.Victory); Assert.Equal(RunKind.Daily, back.Adventure!.Run); Assert.Equal(new[] { "fineprint", "opendoor" }, back.Adventure.Clauses); Assert.True(back.Adventure.Fallen); Assert.Equal(4, back.Adventure.Wipes);
    var h = SaveFormat.ReadHeader(SaveFormat.Serialize(data))!.Value;
    Assert.True(h.fallen); Assert.Equal("He has started using pencil.", h.epitaph); Assert.Equal(5UL, h.gameSeed); Assert.Equal("Daily", h.run);
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "SaveFormatTests|SaveAdventureTests"`.
- [ ] **Step 3: Implement** as in Interfaces. `SlotInfo` (`SaveManager.cs:4-8`) gains the three fields with `Summary` as above; `SaveManager.Info` fills them from the header. `SlotList.Show` (`src/UI/Menus/SlotList.cs:46`): `b.Disabled = mode == Mode.Load && (!info.Exists || info.Corrupt || info.Fallen); b.Modulate = info.Fallen ? new Color(0.55f, 0.55f, 0.6f) : Colors.White;` (the greyed memorial).
- [ ] **Step 4: Run** → PASS, whole suite PASS (`tests/Save/SaveFormatTests.cs:125,149,161,182,206` compare against `CurrentVersion`, so they follow), build clean.
- [ ] **Step 5: Commit** — `git add src/Save src/Core/Main.cs src/Core/Game.cs src/UI/Menus/SlotList.cs tests/Save && git commit -m "feat(save): v11 carries the run kind, the clauses, the daily date and a memorial; Victory is finally saved"`

### Task 5: Profile on disk

**Files:**
- Create: `src/Save/Core/Profile/ProfileDto.cs`, `src/Save/Core/Profile/ProfileRules.cs`, `src/Save/Core/Profile/ProfileFormat.cs` (engine-free, linked by the `Save/Core/**` glob), `src/Save/Profile/ProfileStore.cs` (Godot)
- Test: `tests/Save/ProfileTests.cs`

**Interfaces:**
- Consumes: `ScoreRules.Score`, `SaveFormat.Options` (same JSON options).
- Produces: `public sealed class ProfileDto { int Version = 1; RecordsDto Records = new(); List<RunSummaryDto> Runs = new(); Dictionary<string,string> Deeds = new()` (id → earned UTC) `; Dictionary<string,int> Counters = new()` (all-time counters the deeds need) `; Dictionary<string,DailyResultDto> Daily = new(); List<string> Unlocks = new() }`; `RecordsDto { int BestDepth; double? FastestStorySeconds; Dictionary<string,HeatClearDto> HeatClears` (key `"{run}:{heat}"`, first clear only) `; int ElitesSlain; int Runs; int Victories }`; `HeatClearDto { double Seconds; string Utc }`; `RunSummaryDto { string RunId, Run, StartedUtc, UpdatedUtc; ulong Seed; int DeepestFloor, Heat, ElitesSlain, SecretsFound, Wipes, Score; double Seconds; bool Victory, Fallen; string? Epitaph; List<string> Clauses }`; `DailyResultDto { int Depth, Score; double Seconds; bool Victory, Fallen }`. `public static class ProfileRules { const int KeepRuns = 20; static void RecordRun(ProfileDto p, RunSummaryDto run)` (upsert by `RunId`, newest first, trim to 20; `BestDepth = max`; on `Victory`: `HeatClears.TryAdd($"{run.Run}:{run.Heat}", ...)`, `Victories++`, `FastestStorySeconds = min` when `Run == "Story"`; `Runs` counts distinct RunIds ever seen: increment on first upsert) `; static void RecordDaily(ProfileDto p, string date, RunSummaryDto run)` (overwrite with the greater depth/score) `; static bool DailyDone(ProfileDto p, string date); static int HighestHeatClear(ProfileDto p); static string? Title(ProfileDto p)` ("Whole Contract" at a heat ≥10 clear, "Tenant in Good Standing" at ≥5, else null) `; static int BorderTier(ProfileDto p)` (0/1/2) `}`. `public static class ProfileFormat { static string Serialize(ProfileDto); static ProfileDto? Deserialize(string json)` (null on bad JSON; a missing/older `Version` is accepted) `}`. Godot `public static class ProfileStore { static ProfileDto Current { get; private set; } = new(); static string FileName = "profile.json"` (tests set `"profile-test.json"`) `; static void Load(); static void Save()` (temp + `.bak` + rename exactly like `SaveManager.Write`; on an unreadable file `Load` tries `.bak`, then starts empty) `; static void Delete() }`. `Game._Ready` calls `ProfileStore.Load()` after `Settings.Load()`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ProfileTests
{
    private static RunSummaryDto Run(string id, int depth, int heat = 0, bool victory = false, string run = "Story", double seconds = 600) => new() { RunId = id, Run = run, DeepestFloor = depth, Heat = heat, Victory = victory, Seconds = seconds, Score = ScoreRules.Score(depth, heat, 0, 0, 0) };
    [Fact] public void RecordRun_UpsertsKeepsTwentyAndTracksRecords()
    {
        var p = new ProfileDto();
        for (var i = 0; i < 25; i++) ProfileRules.RecordRun(p, Run($"r{i}", i + 1));
        Assert.Equal(20, p.Runs.Count); Assert.Equal("r24", p.Runs[0].RunId); Assert.Equal(25, p.Records.BestDepth); Assert.Equal(25, p.Records.Runs);
        ProfileRules.RecordRun(p, Run("r24", 30)); Assert.Equal(20, p.Runs.Count); Assert.Equal(30, p.Records.BestDepth); Assert.Equal(25, p.Records.Runs);
    }
    [Fact] public void Victory_WritesTheFirstClearPerHeatAndMode_AndTheFastestStory()
    {
        var p = new ProfileDto();
        ProfileRules.RecordRun(p, Run("a", 20, 5, true, seconds: 5000)); ProfileRules.RecordRun(p, Run("b", 20, 5, true, seconds: 4000)); ProfileRules.RecordRun(p, Run("c", 20, 5, true, "Descent", 100));
        Assert.Equal(5000, p.Records.HeatClears["Story:5"].Seconds); Assert.True(p.Records.HeatClears.ContainsKey("Descent:5")); Assert.Equal(4000, p.Records.FastestStorySeconds); Assert.Equal(3, p.Records.Victories);
        Assert.Equal(5, ProfileRules.HighestHeatClear(p)); Assert.Equal("Tenant in Good Standing", ProfileRules.Title(p)); Assert.Equal(1, ProfileRules.BorderTier(p));
        ProfileRules.RecordRun(p, Run("d", 20, 10, true)); Assert.Equal("Whole Contract", ProfileRules.Title(p)); Assert.Equal(2, ProfileRules.BorderTier(p));
        Assert.Null(ProfileRules.Title(new ProfileDto()));
    }
    [Fact] public void Daily_RecordsOnceADay_KeepsTheBetter()
    {
        var p = new ProfileDto(); Assert.False(ProfileRules.DailyDone(p, "2026-11-07"));
        ProfileRules.RecordDaily(p, "2026-11-07", Run("d1", 3, 2, run: "Daily")); Assert.True(ProfileRules.DailyDone(p, "2026-11-07"));
        ProfileRules.RecordDaily(p, "2026-11-07", Run("d1", 7, 2, run: "Daily")); Assert.Equal(7, p.Daily["2026-11-07"].Depth);
        ProfileRules.RecordDaily(p, "2026-11-07", Run("d1", 2, 2, run: "Daily")); Assert.Equal(7, p.Daily["2026-11-07"].Depth);
    }
    [Fact] public void Format_RoundTrips_AndRefusesGarbage()
    {
        var p = new ProfileDto { Deeds = { ["plumber"] = "2026-11-07T10:00:00Z" }, Counters = { ["elites_total"] = 51 }, Unlocks = { "legendary:leg.gerald" } };
        var back = ProfileFormat.Deserialize(ProfileFormat.Serialize(p))!;
        Assert.Equal("2026-11-07T10:00:00Z", back.Deeds["plumber"]); Assert.Equal(51, back.Counters["elites_total"]); Assert.Contains("legendary:leg.gerald", back.Unlocks);
        Assert.Null(ProfileFormat.Deserialize("{not json")); Assert.NotNull(ProfileFormat.Deserialize("{}"));
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter ProfileTests`.
- [ ] **Step 3: Implement** the DTOs, rules and format; then `ProfileStore` (path `ProjectSettings.GlobalizePath("user://" + FileName)`); `Game._Ready` loads it.
- [ ] **Step 4: Run** → PASS; build clean.
- [ ] **Step 5: Commit** — `git add src/Save/Core/Profile src/Save/Profile src/Core/Game.cs tests/Save/ProfileTests.cs && git commit -m "feat(profile): records, the last twenty runs and the dailies in user://profile.json, written atomically"`

### Task 6: Deeds

**Files:**
- Create: `src/Adventure/Core/Run/DeedRules.cs`, `src/Adventure/Core/Run/DeedTracker.cs`, `src/Adventure/Run/DeedWatcher.cs` (Godot node), `src/UI/Cards/DeedToast.cs`
- Modify: `src/Items/Core/ItemBase.cs:122-133` (`ItemCatalog.Unlocked`), `src/Items/Core/ItemBase.cs:76` (`LegendaryProfile.RequiresDeed`), `src/Items/Defs/LegendaryDef.cs` (`[Export] string RequiresDeed = ""`), `src/Items/Core/LootRoller.cs:70-71` (the gate), the six legendary `.tres` files (set `RequiresDeed`), `src/UI/Hud/Hud.cs` (`ShowDeedToast`), `src/Core/Main.cs` (`CreateSession` adds a `DeedWatcher`), writing's `Banter` bank and fortune's hidden-merchant stock (one static set each)
- Test: `tests/Adventure/Run/DeedRulesTests.cs`, `tests/Items/LootRollerTests.cs` (add)

**Interfaces:**
- Consumes: `ProfileDto` (Task 5), `ClauseRules` deed ids (Task 2), EventBus events (Task 1 list), `Enemy.IsElite`, combat-feel's Shatter mark on `DamageResult` (verify its name; the test below assumes `bool Shatter`; if it is absent, Load-Bearing is judged by `EliteSlain` within 0.2 s of a `HitLanded` whose `Applied` contains a consumed Frost — write the real rule in the commit).
- Produces: `public enum DeedUnlockKind { ClauseRank, Legendary, Banter, MerchantLine }`; `public sealed record DeedDef(string Id, string Name, string Condition, DeedUnlockKind UnlockKind, string UnlockId, string UnlockText)`; `public enum DeedSignal { StoryCleared, HeatClear, FloorReached, PressJammed, GeraldNamed, EliteShattered, Wipe, FloorClosed, SecretFound, FloorSecretsComplete, MimicSprung, CurseTaken, ShrineBoon, Gamble, PerfectBlock, Parried, BossSeen, BossDown, EliteSlain, Rescue, DailyDone }`; `public sealed record DeedEvent(DeedSignal Signal, int Floor = 0, int Amount = 0, bool Flag = false, double At = 0)`; `public static class DeedRules { static readonly DeedDef[] All` (24) `; static DeedDef Def(string id); static string UnlockKey(DeedDef d) => $"{kind.ToString().ToLower()}:{UnlockId}" }`; `public sealed class DeedTracker { DeedTracker(ProfileDto profile); IReadOnlyList<DeedDef> Note(DeedEvent e)` (newly earned, written into `profile.Deeds` with `DateTime.UtcNow.ToString("o")` and `profile.Unlocks`) `; void NewRun() }`. The 24 deeds:

| Id | Name | Condition | Unlock |
|---|---|---|---|
| plumber | Plumber | finish the story | ClauseRank `hazardpaywaived:3` |
| signed_in_blood | Signed in Blood | finish the story under the Death Clause | Banter `hardcore` |
| small_print | Small Print | a clear at heat 5 or more | ClauseRank `fulloccupancy:2` |
| whole_contract | Whole Contract | a clear at heat 10 or more | MerchantLine `merchant.contract` |
| below_the_lease | Below the Lease | reach floor 25 | ClauseRank `hostiletenants:2` |
| deep_tenant | Deep Tenant | reach floor 30 | Legendary *Toll Road Boots* |
| jammed_it | Jammed It | a barrel stops the Press | Legendary *Old Ulf's Chin* |
| geralds_widow | Gerald's Widow | Ilex names a tenth puddle (all runs) | Legendary *Gerald* |
| load_bearing | Load-Bearing | kill an elite with Shatter | Legendary *Gorm's Molar* |
| bang_on_schedule | Bang on Schedule | wipe four times on one floor | Banter `wipes` |
| express_lodgings | Express Lodgings | a floor from 3 down cleared in under 90 s | ClauseRank `expediteddelivery:2` |
| behind_the_wainscot | Behind the Wainscot | ten secrets found in one run | MerchantLine `merchant.wainscot` |
| nosy | Nosy | every secret on a floor that has at least two | Legendary *Hush* |
| bit_first | Bit First | three mimics sprung in one run | Banter `mimic` |
| sin_eater | Sin Eater | take a curse at a shrine | MerchantLine `merchant.sineater` |
| devout | Devout | boons from six shrines in one run | Banter `shrines` |
| pellets_favourite | Pellet's Favourite | five Lucky Dips in one run | Legendary *Pellet's Receipt* |
| iron_manners | Iron Manners | twenty-five perfect blocks in one run | Banter `guard` |
| riposte_society | Riposte Society | twenty-five parries in one run | Banter `parry` |
| short_audience | Short Audience | a tier boss down within 45 s of being seen | MerchantLine `merchant.audience` |
| tenant_eviction | Tenant Eviction | ten elites slain in one run | ClauseRank `hazardpaywaived:2` |
| landlord | Landlord | fifty elites slain, all runs | Banter `elites` |
| pulled_free | Pulled Free | ten Mangled members pulled free, all runs | Banter `rescue` |
| regular | Regular | five Daily Descents on five dates | MerchantLine `merchant.regular` |

  All-time counters live in `profile.Counters` under `elites_total`, `rescues_total`, `gerald_total`, `dailies_total`; per-run counters in the tracker (`NewRun()` clears them). `ItemCatalog.Unlocked : HashSet<string>` (legendary ids); `LegendaryProfile.RequiresDeed : string?`; `LootRoller.Roll` filters `(l.RequiresDeed == null || cat.Unlocked.Contains(l.Id))` beside `!l.ShopOnly`; the six legendaries above get `RequiresDeed = "<deed id>"` in their `.tres`. `ProfileStore.ApplyUnlocks(ItemCatalog cat)` fills `cat.Unlocked` from `Unlocks` entries `legendary:<id>`, and sets writing's `BanterRules.UnlockedSets` (`static HashSet<string>`, new): add `[AiNotNull] public string? Set { get; set; }` to writing's `BanterPlan` and one clause to `BanterPicker.Eligible`: `(p.Set == null || BanterRules.UnlockedSets.Contains(p.Set))`; the unlocked sets' exchanges themselves (two to three per set, `Set = "wipes"` etc., in the stock bank) are written here in the register of writing's Task on banter and fortune's `HiddenMerchantRules.UnlockedLines` (`static HashSet<string>`; each adds one Rare roll and a greeting line to the hidden stall: add the set and the two lines in its stock roll if absent). `Hud.ShowDeedToast(string name, string unlockText)` (bottom-right panel, 4 s, `MouseFilter.Ignore`). `DeedWatcher : Node` (added by `Main.CreateSession` beside the Hud, solo and server only): subscribes to EventBus and feeds the tracker: `PartyWipedBy → Wipe(floor)`; `FloorLedgerClosed(floor, seconds) → FloorClosed(floor, seconds)`; `SecretFound → SecretFound` and, at `FloorLedgerClosed`, `FloorSecretsComplete` when the ledger says `SecretsFound == SecretsTotal >= 2`; `MimicSprung`; `CurseTaken`; `ShrineUsed(_, true) → ShrineBoon`; `Gambled → Gamble`; `PerfectBlock`; `Parried`; `BossSeen → BossSeen(At)`; `BossDefeated → BossDown(At)`; `EliteSlain → EliteSlain`, plus `EliteShattered` when the last `HitLanded` within 0.2 s was a Shatter; `MemberRescued → Rescue`; `HazardPhase(index, _) → PressJammed` when `Main.Builder.Hazards.OfType<Press>().FirstOrDefault(p => p.Index == index)?.Jammed == true` (once per index per floor); `Bark("Ilex", line)` with `line.Text.Contains("Gerald", OrdinalIgnoreCase) → GeraldNamed`; `FloorBuilt(floor, _, _) → FloorReached(floor)`; Main calls `watcher.Note(StoryCleared / HeatClear(heat) / DailyDone)` at victory and daily end (Task 10). Every earned deed → `ProfileStore.Save()`, `ProfileStore.ApplyUnlocks(Game.Instance.Catalog)`, `Hud?.ShowDeedToast(...)`, server: `Broadcast(new LogMsg(...))` via `EventBus.Log($"Deed: {name} — {unlockText}")`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class DeedRulesTests
{
    [Fact] public void TwentyFourDeeds_UniqueIds_EveryUnlockWidensThePool()
    {
        Assert.Equal(24, DeedRules.All.Length); Assert.Equal(24, DeedRules.All.Select(d => d.Id).Distinct().Count());
        Assert.All(DeedRules.All, d => Assert.False(string.IsNullOrWhiteSpace(d.UnlockId)));
        Assert.Equal(6, DeedRules.All.Count(d => d.UnlockKind == DeedUnlockKind.Legendary));
        foreach (var d in DeedRules.All.Where(d => d.UnlockKind == DeedUnlockKind.ClauseRank))
        {
            var (key, rank) = (d.UnlockId.Split(':')[0], int.Parse(d.UnlockId.Split(':')[1]));
            var def = ClauseRules.All.Single(c => c.Key == key); Assert.Equal(d.Id, def.RankUnlockedByDeed[rank - 1]);
        }
    }
    [Fact] public void BangOnSchedule_FourWipesOnOneFloor()
    {
        var p = new ProfileDto(); var t = new DeedTracker(p);
        for (var i = 0; i < 3; i++) Assert.Empty(t.Note(new DeedEvent(DeedSignal.Wipe, Floor: 9)));
        Assert.Empty(t.Note(new DeedEvent(DeedSignal.Wipe, Floor: 10)));
        Assert.Equal("bang_on_schedule", Assert.Single(t.Note(new DeedEvent(DeedSignal.Wipe, Floor: 9))).Id);
        Assert.True(p.Deeds.ContainsKey("bang_on_schedule")); Assert.Contains("banter:wipes", p.Unlocks);
        Assert.Empty(t.Note(new DeedEvent(DeedSignal.Wipe, Floor: 9))); // earned once
    }
    [Fact] public void Landlord_CountsAcrossRuns_ThroughTheProfile()
    {
        var p = new ProfileDto(); var t = new DeedTracker(p);
        for (var i = 0; i < 30; i++) t.Note(new DeedEvent(DeedSignal.EliteSlain));
        Assert.Equal(30, p.Counters["elites_total"]);
        var t2 = new DeedTracker(p); IReadOnlyList<DeedDef> got = Array.Empty<DeedDef>();
        for (var i = 0; i < 20; i++) got = t2.Note(new DeedEvent(DeedSignal.EliteSlain));
        Assert.Contains(got, d => d.Id == "landlord"); Assert.Contains(p.Deeds.Keys, k => k == "tenant_eviction"); // ten in one run came first
    }
    [Fact] public void ShortAudience_AndExpressLodgings_UseTheClock()
    {
        var t = new DeedTracker(new ProfileDto());
        t.Note(new DeedEvent(DeedSignal.BossSeen, At: 100)); Assert.Contains(t.Note(new DeedEvent(DeedSignal.BossDown, At: 144)), d => d.Id == "short_audience");
        var t2 = new DeedTracker(new ProfileDto()); Assert.Empty(t2.Note(new DeedEvent(DeedSignal.FloorClosed, Floor: 2, Amount: 60))); Assert.Contains(t2.Note(new DeedEvent(DeedSignal.FloorClosed, Floor: 3, Amount: 89)), d => d.Id == "express_lodgings");
    }
    [Fact] public void StoryAndHeatClears() { var t = new DeedTracker(new ProfileDto()); var got = t.Note(new DeedEvent(DeedSignal.StoryCleared, Flag: true)); Assert.Contains(got, d => d.Id == "plumber"); Assert.Contains(got, d => d.Id == "signed_in_blood"); Assert.Contains(t.Note(new DeedEvent(DeedSignal.HeatClear, Amount: 10)), d => d.Id == "whole_contract"); }
}
// tests/Items/LootRollerTests.cs (add)
[Fact] public void LockedLegendary_RollsOnlyWhenUnlocked()
{
    var cat = TestCatalog.Build();
    cat.Legendaries["leg.locked"] = new LegendaryProfile { Id = "leg.locked", Name = "Locked", BaseId = "item.longsword", RequiresDeed = "plumber" };
    cat.Legendaries.Remove("leg.ember");
    var table = cat.Tables["table.weapons"]; // legendary weight only
    var names = Enumerable.Range(0, 40).Select(i => LootRoller.Roll(cat, table, 10, new Random(i))!.LegendaryName).ToList();
    Assert.DoesNotContain("Locked", names);
    cat.Unlocked.Add("leg.locked");
    Assert.Contains("Locked", Enumerable.Range(0, 40).Select(i => LootRoller.Roll(cat, table, 10, new Random(i))!.LegendaryName));
}
```

(`Flag: true` on `StoryCleared` means "under the Death Clause".)

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "DeedRulesTests|LootRollerTests"`.
- [ ] **Step 3: Implement** the rules, tracker, catalog gate and `.tres` edits; then `DeedWatcher`, `DeedToast`, `Hud.ShowDeedToast`, `ProfileStore.ApplyUnlocks` (called in `Game._Ready` after the catalog is built and after every earned deed) and the two consumer sets in writing's and fortune's files (exact members above).
- [ ] **Step 4: Run** → PASS; build clean; `timeout 60 godot --headless --path . -- --seed=7 --quit-after=4 --no-ambush --no-portals 2>&1 | grep -E "ERROR|profile"` → no errors.
- [ ] **Step 5: Commit** — `git add -A src/Adventure/Core/Run src/Adventure/Run src/UI/Cards src/UI/Hud/Hud.cs src/Items src/Save/Profile src/Core data/items tests && git commit -m "feat(deeds): twenty-four deeds that unlock clauses, legendaries, banter and the hidden merchant's lines"`

### Task 7: Every clause does what it says

**Files:**
- Create: `src/Adventure/Core/Run/InventoryAudit.cs`, `src/Adventure/Core/Run/OccupancyRules.cs`
- Modify: `src/Items/Core/ShopRules.cs` (`ClausePriceMultiplier`), `src/Items/Core/Prices.cs:19`, `src/Dungeon/TierRules.cs` (`ClauseDamageMultiplier`, `PartyDamageScale`), `src/Adventure/Core/Objectives/GasRules.cs:58`, `src/Dungeon/TrapRules.cs:20`, `src/Enemies/Core/EnemyProfile.cs:67`, `src/Adventure/Core/Design/Reinforcements.cs:61-66,133-139` (`AmbushRules.Count`, `PortalRules.Grace/Interval`), `src/Adventure/PortalSpawner.cs:64,94,103`, `src/Dungeon/DungeonBuilder.cs:1113` (ambush count), `:179-180` (occupancy after `contents ??=`), `:352-361` (merchant fallback), `src/Dungeon/Generation/FloorSpec.cs` (`bool SafeRoom = true`), `src/Dungeon/Generation/FloorGenerator.cs:54` (`if (spec.SafeRoom)`), `src/Dungeon/DungeonBuilder.cs:131-139` (`SpecFor` sets `SafeRoom`), `src/Dungeon/DungeonBuilder.cs:179,185` (pass `extraElites: ClauseRules.Active.ExtraElites` to `AdventurePlanning.ResolveStored` and `LegacyPopulator.Populate`, the parameters elites added), fortune's shrine offer count (`ClauseRules.Active.ShrineOffers(n)`), `src/Party/PartyController.cs:556-570,854-864` (`AwardXp` scale, `AddFoundGold`), `src/Items/LootDrop.cs:85`, `src/Core/Main.cs:447-457` (the audit at tier gates)
- Test: `tests/Adventure/Run/InventoryAuditTests.cs`, `tests/Adventure/Run/OccupancyRulesTests.cs`, `tests/Items/PricesTests.cs` (add), `tests/Adventure/ReinforcementTests.cs` (add), `tests/Dungeon/FloorGeneratorTests.cs` (add)

**Interfaces:**
- Consumes: `ClauseRules.Active` (Task 2), `Equipment.Items`, `ItemInstance.Affixes` (`RolledAffix.Tier > 0` = a rolled line; Tier 0 = a legendary's own line), `PlanResolver`'s free-cell helper (`RoomCells.Free(layout, room)`, `PlanResolver.cs:19`), `FloorContents.Enemies` (mutable list of `EnemyPlacement(SpawnIndex, EnemyId, Cell, Under, CollectiveId, Ordinal)`).
- Produces: `ShopRules.ClausePriceMultiplier : float = 1f`; `Prices.BuyPrice(item) => (int)Math.Floor(BuyMultiplier * ShopRules.ClausePriceMultiplier * Value(item) + 1e-9)`; `TierRules.ClauseDamageMultiplier : float = 1f`, `TierRules.PartyDamageScale => Lethality * ClauseDamageMultiplier` used at the three damage sites (`GasRules.Tick`, `TrapRules.Scale` when lethal, `EnemyProfile.BuildAttack`); the hazards' lethal threshold keeps `Lethality`. `AmbushRules.Count(int floor, bool doubled = false)` (×`OpenDoorAmbushFactor`). `PortalRules.Grace(float intensity, int expedited = 0)` (`expedited > 0 ? ClauseRules.ExpeditedGraceSeconds : GraceSeconds`, over `Max(0.25, intensity)`), `PortalRules.Interval(float seconds, float intensity, int expedited = 0)` (ramp `RampSeconds / 2^expedited`); `PortalSpawner` passes `ClauseRules.Active.ExpeditedRank` at its three calls. `FloorSpec.SafeRoom : bool = true`; `FloorGenerator.Generate` line 54 becomes `if (spec.SafeRoom) PlaceSafeRoom(layout, new Random(seed ^ 0x5AFE));`; `DungeonBuilder.SpecFor` sets `SafeRoom = !ClauseRules.Active.NoSafeRoom` (floor 0 keeps its tavern); `PlaceMerchant`: when `layout.SafeRoom < 0 && ClauseRules.Active.NoSafeRoom && cast != null`, place him at `WalkableNeighbor(layout, layout.UpStairs)` facing the stairs, log `"merchant {name} in the open by the stairs (No Rest)"`, `MerchantLines` as usual (trade works within `MerchantReach`). `OccupancyRules.Apply(FloorContents contents, FloorLayout layout, int rank, int seed) : int` (adds `round(n × 0.25 × rank)` copies of existing non-boss, non-`Under` placements chosen round-robin from a seeded shuffle of `new Random(seed ^ 0x0CC0)`, each on a free cell of the same room (`RoomCells.Free` minus used cells), appended as loners with `SpawnIndex = contents.Enemies.Count`; returns how many were added; 0 when rank 0). `DungeonBuilder.Build` after `contents ??= ...`: `if (ClauseRules.Active.Rank(Clause.FullOccupancy) > 0) OccupancyRules.Apply(contents, layout, rank, seed);` `InventoryAudit.Apply(IEnumerable<CharacterState> members) : List<string>` (per living-or-not member: the equipped item with the lowest `ItemLevel` (ties: slot order) that has at least one `Tier > 0` affix loses its last such affix; `Recalculate()`; returns lines `"{Name}'s {item.DisplayName} loses {affix.Name}."`). `Main.RebuildFloorInner`, after `FloorStateCapture.Apply` and only when `delta > 0 && saved == null && !NetSession.IsClient && ClauseRules.Active.AuditsAt(floor)`: `foreach (var line in InventoryAudit.Apply(Party.Members)) EventBus.Instance.Log(line); Hud?.Refresh();`. `PartyController.AwardXp(int xp)` first line: `xp = ClauseRules.Active.ScaleReward(xp);` `PartyController.AddFoundGold(int coins)` (`var scaled = ClauseRules.Active.ScaleReward(coins); AddGold(scaled); if (scaled > 0) EventBus.Instance.RaiseGoldFound(scaled);`), `LootDrop.cs:85` → `party.AddFoundGold((int)LegendaryRules.GoldFound(Coins, party.GoldFindBonus()))` (items' line, wrapped).

- [ ] **Step 1: Write the failing tests**

```csharp
public class InventoryAuditTests
{
    private readonly ItemCatalog _cat = TestCatalog.Build();
    [Fact] public void StripsOneAffixFromTheLowestLevelItem_PerMember()
    {
        var w = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front, LootRoller.Generate(_cat, _cat.Base("item.longsword"), Rarity.Rare, 9, new Random(1)));
        w.Equipment.Equip(EquipSlot.Body, LootRoller.Generate(_cat, _cat.Base("item.plate"), Rarity.Rare, 4, new Random(2))); w.Recalculate();
        var before = w.Equipment[EquipSlot.Body]!.Affixes.Count(a => a.Tier > 0);
        var lines = InventoryAudit.Apply(new[] { w });
        Assert.Single(lines); Assert.StartsWith("Brand's", lines[0]);
        Assert.Equal(before - 1, w.Equipment[EquipSlot.Body]!.Affixes.Count(a => a.Tier > 0));
        Assert.Equal(4, w.Equipment[EquipSlot.Weapon]!.Affixes.Count(a => a.Tier > 0)); // the higher item is untouched
    }
    [Fact] public void InventoryAudit_SkipsEmptyMembers_AndNeverStripsALegendaryLine()
    {
        var bare = CharacterState.Create("Wren", TestClasses.Rogue, Rank.Back);
        var leg = LootRoller.MakeLegendary(_cat, _cat.Legendaries["leg.bulwark"], 6, new Random(3)); leg.Affixes.RemoveAll(a => a.Tier > 0); // only its own line left
        var holder = CharacterState.Create("Ilex", TestClasses.Mage, Rank.Back); holder.Equipment.Equip(EquipSlot.Offhand, leg); holder.Recalculate();
        Assert.Empty(InventoryAudit.Apply(new[] { bare, holder }));
        Assert.Single(leg.Affixes);
    }
}
public class OccupancyRulesTests
{
    [Fact] public void Apply_AddsAQuarterPerRank_OnFreeCells_Deterministically()
    {
        var (layout, _, menu) = TestAdventure.Floor(3, 11);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 11);
        FloorContents Build() => PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 3, 11);
        var a = Build(); var n = a.Enemies.Count;
        Assert.Equal((int)Math.Round(n * 0.25), OccupancyRules.Apply(a, layout, 1, 11));
        var b = Build(); OccupancyRules.Apply(b, layout, 1, 11);
        Assert.Equal(a.Enemies.Select(e => (e.EnemyId, e.Cell)), b.Enemies.Select(e => (e.EnemyId, e.Cell)));
        Assert.Equal(a.Enemies.Count, a.Enemies.Select(e => e.SpawnIndex).Distinct().Count());
        Assert.Equal(a.Enemies.Count, a.Enemies.Select(e => e.Cell).Distinct().Count());
        var c = Build(); Assert.Equal((int)Math.Round(n * 0.5), OccupancyRules.Apply(c, layout, 2, 11));
        Assert.Equal(0, OccupancyRules.Apply(Build(), layout, 0, 11));
    }
}
// tests/Items/PricesTests.cs (add)
[Fact] public void ConvenienceFee_RaisesBuyPricesByFortyPercent_SellUnchanged()
{
    var cat = TestCatalog.Build(); var item = LootRoller.Generate(cat, cat.Base("item.longsword"), Rarity.Magic, 5, new Random(1));
    var normal = Prices.BuyPrice(item);
    try { ShopRules.ClausePriceMultiplier = 1.4f; Assert.Equal((int)Math.Floor(2.5 * 1.4 * Prices.Value(item) + 1e-9), Prices.BuyPrice(item)); Assert.Equal(Prices.Value(item), Prices.SellPrice(item)); }
    finally { ShopRules.ClausePriceMultiplier = 1f; }
    Assert.Equal(normal, Prices.BuyPrice(item));
}
// tests/Adventure/ReinforcementTests.cs (add)
[Fact] public void Expedited_StartsAt45_AndShrinksTwiceAsFastPerRank()
{
    Assert.Equal(45f, PortalRules.Grace(1f, 1)); Assert.Equal(45f, PortalRules.Grace(1f, 2)); Assert.Equal(90f, PortalRules.Grace(1f));
    Assert.Equal(PortalRules.Interval(180f, 1f, 0), PortalRules.Interval(90f, 1f, 1), 3); Assert.Equal(PortalRules.Interval(180f, 1f, 0), PortalRules.Interval(45f, 1f, 2), 3);
}
[Fact] public void OpenDoor_DoublesAmbushes() { Assert.Equal(AmbushRules.Count(5) * 2, AmbushRules.Count(5, doubled: true)); Assert.Equal(0, AmbushRules.Count(0, true)); }
// tests/Dungeon/FloorGeneratorTests.cs (add)
[Fact] public void NoSafeRoom_LeavesTheFloorWithoutOne() { var l = FloorGenerator.Generate(FloorSpec.Default with { SafeRoom = false }, 7); Assert.Equal(-1, l.SafeRoom); Assert.DoesNotContain(l.Vaults, v => v.Kind == VaultKind.SafeDoor); }
```

Also pin the damage scale: in `tests/Dungeon/` (any existing TierRules/TrapRules test file) `[Fact] public void HazardPay_StacksWithLethality() { TierRules.Lethality = 2f; TierRules.ClauseDamageMultiplier = 1.4f; try { Assert.Equal(2.8f, TierRules.PartyDamageScale, 3); } finally { TierRules.Lethality = TierRules.DefaultLethality; TierRules.ClauseDamageMultiplier = 1f; } }`.

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "InventoryAuditTests|OccupancyRulesTests|PricesTests|ReinforcementTests|FloorGeneratorTests"`.
- [ ] **Step 3: Implement** everything in Interfaces, then wire `ClauseRules.Activate` to set the two statics (the Task 2 placeholders).
- [ ] **Step 4: Run** → PASS; whole suite PASS (an existing `PortalRules`/`AmbushRules` test that calls the old signatures still compiles through the defaults); build clean. Note: under No Rest the shipped plans log `plan was written for an older layout; adapted` (the layout hash covers the door cell) — expected, say so in the docs (Task 12).
- [ ] **Step 5: Commit** — `git add -A src tests && git commit -m "feat(clauses): prices, damage, creatures, elites, portals, ambushes, shrines, safe rooms, the audit and the heat multiplier all answer to the contract"`

### Task 8: Title screen, the merchant's clause offer, the clause card, the server flag

**Files:**
- Modify: `src/UI/Menus/MainMenu.cs:45-49,78-85` (eight buttons, subtitle), `src/Core/Main.cs:222-294` (`StartDescent`, `StartDaily`, `SignClauses`; `StartBuiltinCampaign`/`StartNewGame` pass `RunKind`), `src/Adventure/Core/Plans/FloorPlan.cs:10` (`DialogueEffect.Clauses` appended), `src/Adventure/Core/Voice/MerchantLineBank.cs:37-56` (the `clauses` node), `src/Adventure/Core/TavernRules.cs:329-335` (`WithClauseNode`), `data/adventures/builtin/campaign.json` (Merchant.Briefing: the `clauses` node), `src/UI/Hud/Hud.cs` (open the card on the effect), `src/UI/Menus/PauseMenu.cs:36-39` (the clause line), `src/Net/NetSession.cs:54-72` (`--clauses=`), `src/Net/ServerWorld.cs:195-199` (Welcome's `AdventureDto` copies `Clauses`, `Run`), `src/Core/Game.cs:53-64` (a new game adopts `NetSession.Instance?.Clauses`)
- Create: `src/UI/Cards/ClauseCard.cs`
- Test: `tests/Adventure/TavernRulesTests.cs` (add), `tests/Adventure/MerchantPlanTests.cs` (add)

**Interfaces:**
- Consumes: `ClauseRules` (Task 2), `DailyRules` (Task 3), `ProfileStore.Current` (Task 5), `Main.StartNewGame(ulong? seed, bool? aiCampaign)` (Main.cs:222).
- Produces: `MainMenu` buttons in order: Story → `Main.Instance.StartNewGameFromDisk()`; New Descent → `Main.Instance.StartDescent()`; Daily Descent → `Main.Instance.StartDaily(DailyRules.Today())`, disabled with text `"Daily Descent — done: floor {d.Depth}, score {d.Score}"` when `ProfileRules.DailyDone(ProfileStore.Current, DailyRules.Key(today))`; Load Game; Join Server; Records → `OpenRecords()` (Task 11); Settings; Quit. Subtitle: `"Story plays {plays}. New Descent rolls a fresh seed{(canPlan ? $" and lets {PlannerLabel} write it" : "")}. Daily Descent: everyone gets today's dungeon and two clauses."`. `Main.StartDescent()` = `StartNewGame(null, aiCampaign: null)` with `Game.PrepareNewGame(seed, RunKind.Descent)`; `Main.StartDaily(DateOnly date)`: `StartNewGame(DailyRules.SeedFor(date), aiCampaign: false)` then `Adventure.Run = Daily, DailyDate = DailyRules.Key(date), Clauses = DailyRules.ClausesFor(date)`, `ClauseRules.Activate`, `ProfileRules.RecordDaily(profile, key, summary-with-depth-0)` + `ProfileStore.Save()`; `StartNewGame` gains an optional `RunKind run = RunKind.Descent` parameter passed to `PrepareNewGame`; `StartBuiltinCampaign` passes `RunKind.Story`. `Main.SignClauses(IReadOnlyList<string> clauses)` (floor 0, solo or server only): `Adventure.Clauses = ClauseRules.Normalize(clauses); ClauseRules.Activate(...); Log($"The contract is signed: {Active.Summary}")`. `DialogueEffect.Clauses` (appended last). `TavernRules.WithClauseNode(NpcScript script) : NpcScript` (idempotent: if no node `clauses` exists and `deal` exists, insert `clauses` with the stock line `"One more thing. The contract has clauses, every one optional and every one paid for. Will you read them?"`, choices `[0] "Not today." → sendoff`, `[1] "Read them." Effect = Clauses, Next = sendoff`, and repoint every `deal` choice whose `Next == "sendoff"` to `clauses`); `BriefingFor` returns `WithClauseNode(own)` / the bank's briefing already carries the node; `campaign.json` gets the same node by hand (`VoiceId: null`), with Pellet's own words: `"One more thing. The contract has clauses, factor's clauses, every one optional and every one paid for. Will you read them, or shall I fold them away?"`. `Hud`: on `DialogueEffectChosen(_, DialogueEffect.Clauses, _)` set `_clausesPending = true`; on `_dialogue.Closed` when pending → `_clauseCard.Open(readOnly: NetSession.IsClient || Game.Instance.Adventure.Run == RunKind.Daily)`; `UpdateModalState` includes `_clauseCard.Visible`. `ClauseCard : Control` (centre panel; one row per `ClauseRules.All`: name, description, `heat {HeatPerRank}/rank`, a rank stepper `−`/`+` capped by `ClauseRules.MaxRankAllowed(def, deeds)` with the locked rank's deed name greyed beside it; a total line `"Heat {heat} · ×{mult:0.0} gold and XP"`; buttons `Sign` → `Main.Instance.SignClauses(selected)` + close, `Skip` → close; read-only mode shows the active list and one `Close`); Esc = Skip. `PauseMenu.Open` adds a label under the title with `ClauseRules.Active.Summary`. `NetSession.Clauses : List<string>?` from `--clauses=a,b:2` via `ClauseRules.Parse`; `Game.PrepareNewGame` ends with `if (Net.NetSession.Instance?.Clauses is { } c) Adventure.Clauses = ClauseRules.Normalize(c);` before `Activate`. `ServerWorld.BuildWelcome`'s `AdventureDto` initializer gains `Clauses = game.Adventure.Clauses, Run = game.Adventure.Run, DailyDate = game.Adventure.DailyDate`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Adventure/TavernRulesTests.cs (add)
[Fact] public void BriefingFor_AlwaysOffersTheClauses_BetweenTheDealAndTheSendoff()
{
    var script = TavernRules.BriefingFor(null);
    var clauses = Assert.Single(script.Nodes, n => n.Id == "clauses");
    Assert.Equal(2, clauses.Choices.Count); Assert.Equal(DialogueEffect.Clauses, clauses.Choices[1].Effect); Assert.All(clauses.Choices, c => Assert.Equal("sendoff", c.Next));
    Assert.All(script.Nodes.Single(n => n.Id == "deal").Choices, c => Assert.Equal("clauses", c.Next));
    Assert.True(TavernRules.ReachesDeal(script));
    Assert.Same(script, TavernRules.WithClauseNode(script)); // idempotent: nothing to add
}
[Fact] public void WithClauseNode_SplicesIntoAFourNodeBriefing()
{
    var own = new NpcScript { Start = "hire", Nodes = { new DialogueNode { Id = "hire", Choices = { new ChoicePlan { Text = "a", Next = "deal" } } }, new DialogueNode { Id = "deal", Choices = { new ChoicePlan { Text = "b", Next = "sendoff" } } }, new DialogueNode { Id = "sendoff", Choices = { new ChoicePlan { Text = "c", Effect = DialogueEffect.Farewell } } } } };
    var fixedUp = TavernRules.WithClauseNode(own);
    Assert.Equal("clauses", fixedUp.Nodes.Single(n => n.Id == "deal").Choices[0].Next); Assert.Equal(4, fixedUp.Nodes.Count);
}
// tests/Adventure/MerchantPlanTests.cs (add): the shipped campaign carries the node itself
[Fact] public void ShippedBriefing_HasTheClausesNode() { var c = GulletCampaignTests.LoadShipped(); var node = Assert.Single(c.Merchant!.Briefing.Nodes, n => n.Id == "clauses"); Assert.Equal(DialogueEffect.Clauses, node.Choices[1].Effect); }
```

(`GulletCampaignTests.LoadShipped()` or the equivalent loader those tests already use; adapt the name.)

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "TavernRulesTests|MerchantPlanTests"`.
- [ ] **Step 3: Implement** the engine-free parts, the JSON edit, then the Godot parts. The `--test-tavern` flow walks `Choose(0)` to the end (`DebugTools.cs:3424-3439`): "Not today." sits at index 0 so the walk still closes.
- [ ] **Step 4: Run** → PASS; build; `timeout 60 godot --headless --path . -- --test-tavern 2>&1 | grep RESULT` → `[tavern] RESULT ok`; `timeout 40 godot --headless --path . -- --builtin --quit-after=4 2>&1 | grep -c adapted` → 0.
- [ ] **Step 5: Commit** — `git add -A src data/adventures/builtin/campaign.json tests && git commit -m "feat(run): Story, New Descent and Daily Descent on the title; the factor offers the clauses; --clauses= for servers"`

### Task 9: Stakes: the wipe captures the floor, costs gold, scars, shows a death card; the Death Clause ends the run; solo autosave

**Files:**
- Modify: `src/Adventure/Core/Objectives/WipeRules.cs` (extension), `src/Party/PartyController.cs:498-508` (`OnPartyWiped`), `src/Core/Main.cs` (`OnPartyWiped`, `CaptureCurrentFloor`, autosave, `RunOver`), `src/UI/Hud/Hud.cs:261-272` (quickload refusal), `src/UI/Menus/PauseMenu.cs:60-67` (`_load.Visible` also `!Hardcore`), `src/Net/Core/Messages.cs`, `src/Net/Core/Protocol.cs:7,9,51-52` (`DeathCardEvt`, version +1, count +1 now and +3 after Task 10), `src/Net/ServerWorld.cs` (broadcast), `src/Net/ClientWorld.cs:80-110` (handle), fortune's `Curses` (Scar + `UntilFloor`)
- Create: `src/Adventure/Core/Run/RunRules.cs`, `src/UI/Cards/DeathCard.cs`
- Test: `tests/Adventure/WipeRulesTests.cs` (add), `tests/Adventure/Run/RunRulesTests.cs`, `tests/Net/NetCodecTests.cs` (add)

**Interfaces:**
- Consumes: `FloorStateCapture.Capture` (Main.cs:433 pattern), `Epitaphs.Pick` (writing), `AdventureDirector.Taunt(PartyWiped)` (already fires on `PartyWiped`), `ProfileRules`/`ProfileStore`, `SaveManager.MarkFallen` (Task 4), fortune's curse registry.
- Produces: `WipeRules` gains `const float GoldLoss = 0.20f; static int GoldAfterWipe(int gold) => gold - (int)Math.Floor(gold * GoldLoss); static (string Cause, string Killer) CauseOf(IEnumerable<CharacterState> members)` (the member whose `LastHarm.At` is greatest; `("generic", "the dark")` when none) `; static string ScarId = "scar"`. Fortune's `Curses`: add `Scar` (id `scar`, "Scar", "−10% max HP until the next floor", `MaxHp Mul ClauseRules.ScarMaxHp`) and, if its curse state has no expiry, `int? UntilFloor` plus `Curses.ExpireAt(list, floor)` (removes entries with `UntilFloor <= floor`), called in `Main.RebuildFloorInner` when `_lastBuiltFloor != floor` before the party is placed; members `Recalculate()` after. `public sealed record DeathCardModel(string Epitaph, string Cause, string Killer, int Floor, int WipesOnFloor, bool RunOver, string Button)`; `RunRules { const float AutosaveSeconds = 180f; static bool AutosaveDue(float sinceLast, bool paused, bool rebuilding, bool networked, bool cardUp, bool runOver) }`. `Main.CaptureCurrentFloor()` (`game.Floors[CurrentFloor] = FloorStateCapture.Capture(root, layout, Builder.Contents, Exploration)` when a floor is built); `Main.OnPartyWiped(string cause, string killer) : bool` (server/solo): `CaptureCurrentFloor()`; `Adventure.Wipes++`; `var onFloor = Memory.For(floor).Wipes + 1`; gold: `Party.AddGold(WipeRules.GoldAfterWipe(Party.Gold) - Party.Gold)`; Scar through fortune's API with `UntilFloor = floor + 1`, members recalculated; `epitaph = Epitaphs.Pick(cause, onFloor, floor)` (writing's bank; deterministic by itself); hardcore = `ClauseRules.Active.Hardcore`; model = `new DeathCardModel(epitaph, cause, killer, floor, onFloor, hardcore, hardcore ? "To the Records" : "Rise")`; `Hud?.ShowDeathCard(model, onClose)`; server: `Broadcast(new DeathCardEvt(model))`; if hardcore: `Adventure.Fallen = true; Adventure.Epitaph = epitaph; RunOver = true; SaveManager.Write(SaveManager.AutoSlot, BuildSaveData("Memorial")); SaveManager.MarkFallen(GameSeed, epitaph); ProfileRules.RecordRun(...)` (Task 10's `RunSummaryOf()`), `ProfileStore.Save()`, and the card's close action shows the recap (Task 10) whose button returns to the menu and opens Records; returns `!hardcore`. `Main.RunOver : bool` (reset in `CreateSession`). `Hud.ShowDeathCard(DeathCardModel m, Action? onClose)` (modal centre panel: "THE PARTY HAS FALLEN", the epitaph in italics, `"{Describe(cause,killer)} did it, on floor {Floor}. Wipe {WipesOnFloor} on this floor."`, one button `m.Button`); the villain's `PartyWiped` taunt plays over it (unchanged). `PartyController.OnPartyWiped` becomes exactly:

```csharp
private bool _wipeJudged;
private void OnPartyWiped()
{
    if (_wipeJudged) return;                                                  // under the Death Clause the bodies stay down
    var (cause, killer) = Adventure.Objectives.WipeRules.CauseOf(Members);
    var rise = Main.Instance.OnPartyWiped(cause, killer);                     // captures the floor first, takes the gold, scars, shows the card
    EventBus.Instance.Log("The party has fallen. The dungeon shifts...");
    EventBus.Instance.RaisePartyWiped();
    EventBus.Instance.RaisePartyWipedBy(Game.Instance.CurrentFloor, cause);
    if (!rise) { _wipeJudged = true; return; }
    foreach (var m in Members) m.FullRestore();
    foreach (var a in Adventurers) a.Revive();
    for (var i = 0; i < Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
    if (Game.Instance.Floors.TryGetValue(Game.Instance.CurrentFloor, out var remembered)) Adventure.Objectives.WipeRules.RestartClocks(remembered.Objective);
    Game.Instance.ReloadFloor();
}
```

  (`FullRestore` clears status effects but not the curse, which is a modifier in the curses layer.) Autosave: `Main._Process` accumulates `_sinceAutosave` and, when `RunRules.AutosaveDue(_sinceAutosave, GetTree().Paused, Rebuilding, NetSession.IsNetworked, Hud?.CardUp == true, RunOver)` → `SaveGame(SaveManager.AutoSlot)` quietly (`GD.Print` instead of the log line); `EventBus.FloorBuilt` (solo, `delta != 0`, floor ≥ 1) → the same. `Hud.CardUp => _deathCard.Visible || _recap.Visible`. `Hud` quickload (`:267`): `if (ClauseRules.Active.Hardcore) { EventBus.Instance.Log("The Death Clause allows no second chances."); handled; return; }`; `PauseMenu.Open`: `_load.Visible = !net && !ClauseRules.Active.Hardcore`. `[Msg(MsgKind.DeathCardEvt)] public sealed record DeathCardEvt(DeathCardModel Card) : NetMessage;` appended to `MsgKind` after `HitFxEvt`; client: `case DeathCardEvt dc: Main.Instance.Hud?.ShowDeathCard(dc.Card, null); break;`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Adventure/WipeRulesTests.cs (add)
[Fact] public void GoldAfterWipe_TakesAFifth_RoundingInThePartysFavour() { Assert.Equal(80, WipeRules.GoldAfterWipe(100)); Assert.Equal(6, WipeRules.GoldAfterWipe(7)); Assert.Equal(0, WipeRules.GoldAfterWipe(0)); Assert.Equal(1, WipeRules.GoldAfterWipe(1)); }
[Fact] public void CauseOf_PicksTheLatestHarm_AndIgnoresMembersWithNone()
{
    var a = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front); a.LastHarm = new HarmSource("elite", "Grukk the Swift", 10);
    var b = CharacterState.Create("Sable", TestClasses.Rogue, Rank.Front); b.LastHarm = new HarmSource("press", "The press", 14);
    var c = CharacterState.Create("Wren", TestClasses.Rogue, Rank.Back);
    Assert.Equal(("press", "The press"), WipeRules.CauseOf(new[] { a, b, c }));
    Assert.Equal(("generic", "the dark"), WipeRules.CauseOf(new[] { c }));
}
// tests/Adventure/Run/RunRulesTests.cs
public class RunRulesTests
{
    [Fact] public void AutosaveDue_RefusesRebuildPauseNetAndCard()
    {
        Assert.True(RunRules.AutosaveDue(180f, false, false, false, false, false));
        Assert.False(RunRules.AutosaveDue(179f, false, false, false, false, false));
        Assert.False(RunRules.AutosaveDue(500f, true, false, false, false, false)); Assert.False(RunRules.AutosaveDue(500f, false, true, false, false, false));
        Assert.False(RunRules.AutosaveDue(500f, false, false, true, false, false)); Assert.False(RunRules.AutosaveDue(500f, false, false, false, true, false)); Assert.False(RunRules.AutosaveDue(500f, false, false, false, false, true));
    }
}
// tests/Net/NetCodecTests.cs (add; raise the Version pin at line 232 by one now; the count rises by one here and two more in Task 10)
[Fact] public void DeathCard_RoundTrips() { var c = RoundTrip(new DeathCardEvt(new DeathCardModel("Fourth time on floor nine.", "press", "The press", 9, 4, false, "Rise"))); Assert.Equal(4, c.Card.WipesOnFloor); Assert.Equal("press", c.Card.Cause); }
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "WipeRulesTests|RunRulesTests|NetCodecTests"`.
- [ ] **Step 3: Implement** engine-free first, then `Main`, `PartyController`, `DeathCard`, `Hud`, the net message and handlers, the fortune `Scar`.
- [ ] **Step 4: Run** → PASS (`MessageKindCount` = live + 1 now, + 3 after Task 10; `Version` = live + 1 now and unchanged in Task 10); build clean; `timeout 90 godot --headless --path . -- --test-gas-wipe 2>&1 | grep RESULT` → ok (the wipe path still restarts the gas; now the floor is captured first).
- [ ] **Step 5: Commit** — `git add -A src tests && git commit -m "feat(run): a wipe keeps the floor as it was, costs a fifth of the gold, scars the party and shows a death card; the Death Clause ends the run with a memorial; solo autosaves"`

### Task 10: Cards: the floor card on every descent, the run recap on victory and on a hardcore end

**Files:**
- Create: `src/Adventure/Core/Run/RecapBuilder.cs`, `src/UI/Cards/FloorCard.cs`, `src/UI/Cards/RunRecap.cs`
- Modify: `src/UI/Hud/Hud.cs:429-457` (replace `ShowVictory` with `ShowFloorCard`, `ShowRunRecap`; Esc on a floor card at `:233`), `src/Core/Main.cs:587-600` (`OnBossDefeated` → recap), `src/Adventure/AdventureDirector.cs:392-401,497-507,529-565` (floor card from the closed record), `src/Net/Core/Messages.cs`, `src/Net/Core/Protocol.cs` (`FloorCardEvt`, `RunRecapEvt`; count + 2), `src/Net/ServerWorld.cs`, `src/Net/ClientWorld.cs:264-267` (recap from the server instead of a local `ShowVictory`)
- Test: `tests/Adventure/Run/RecapBuilderTests.cs`, `tests/Net/NetCodecTests.cs` (add)

**Interfaces:**
- Consumes: the ledger (Task 1), `ScoreRules`, `ClauseRules.Active`, `BarkRepeat.ChooseOrOldest` + `TravelBarks.TopUp` (the reaction line), `CampaignPlan.Antagonist.Name`, `FloorPlan.Boss?.Name`, `TierPlan.BossName`, `PlayerRegistry.ToDtos()` (name → member index), fortune's `AdventureDto.Boons` ids (names through its registry).
- Produces: `public sealed record FloorCardModel(string Title, string Time, int Kills, int Elites, string Secrets, int Downs, int GoldFound, string WorstMoment, string Reaction)`; `public sealed record HeroLine(string Name, int Kills, float Damage, int Downs, int Rescues, float PossessSeconds, string Text)`; `public sealed record RunRecapModel(string Headline, List<string> FloorLines, List<HeroLine> Heroes, int ElitesSlain, List<string> Boons, List<string> Clauses, int DeepestFloor, int Score, List<string> PlayerLines, bool Victory)`; `public static class RecapBuilder { static FloorCardModel FloorCard(FloorRecord r, string title, string reaction)` (`Time` via `SlotInfo.FormatTime`, `Secrets = $"{found}/{total}"`) `; static RunRecapModel Build(CampaignMemoryDto memory, IReadOnlyList<string> heroes, IReadOnlyList<string> clauses, IReadOnlyList<string> boons, bool victory, string headline, int wipes, IReadOnlyList<string>? players = null)` (FloorLines `"Floor {n} {title}: {kills} kills, {downs} downs, {time}{(wipes>0 ? ", wiped" : "")}"`; Heroes summed over `KillsBy/DamageBy/DownsBy/RescuesBy/PossessSeconds`, `Text` = `"{Name}: {kills} kills, {damage:0} dealt, down {downs}×, {rescues} rescues, {minutes} min in the lead"`; `ElitesSlain = Σ Elites`; `DeepestFloor = max Floor`; `Score = ScoreRules.Score(deepest, new ClauseSet(clauses).Heat, elites, Σ SecretsFound, wipes)`; PlayerLines when `players` given: `RecapRules.RoleLines(memory.Floors, players)` rendered `"{Role}: {Player}"`; never a damage rank) `; static string VictoryHeadline(string? bossName, string? antagonist)` → `$"{boss ?? "The guardian"} is slain. {(antagonist is null ? "The depths lie open." : $"{antagonist}'s hold on the depths is broken.")}"`; `static string Reaction(BarkSet? set, Dictionary<string,int> said, int floor, Random rng)` (the lead's Travel line via `BarkRepeat.ChooseOrOldest`, not marked said, `""` when none) }`. `Hud.ShowFloorCard(FloorCardModel)` (top-centre panel, `MouseFilter.Ignore`, never locks input, hides after `RunRules.FloorCardSeconds = 4f` or on Esc: `_UnhandledInput` checks `_floorCard.Visible && IsActionPressed("pause")` before `TogglePause`); `Hud.ShowRunRecap(RunRecapModel m, string button, Action onClose)` (modal scroll panel: headline, per-floor lines, heroes, player lines, elites, boons, clauses, deepest floor, score; input locked while up); `Hud.ShowVictory` is deleted. `AdventureDirector`: `CloseRecord` builds `_pendingFloorCard = RecapBuilder.FloorCard(_record, _record.Title != "" ? _record.Title : $"Floor {_record.Floor}", Reaction(...))` when `_record.Floor >= 1`; `OnFloorBuilt` (when `_pendingFloorCard` is set and the new floor is deeper) → `Main.Instance.Hud?.ShowFloorCard(card)`; server: `Broadcast(new FloorCardEvt(card))`; then clears it. `Main.OnBossDefeated` (:595-599): `Game.Victory = true; ShowRecap(victory: true)` where `Main.ShowRecap(bool victory)` builds `RecapBuilder.Build(...)` with `headline = victory ? VictoryHeadline(Builder.Contents?.Plan?.Boss?.Name ?? campaign?.Tiers.LastOrDefault()?.BossName, campaign?.Antagonist.Name) : $"The contract is void. {Adventure.Epitaph}"`, players = `NetSession.Instance?.Server?.Registry.ToDtos().Select(p => p.Name)` (null in solo); `Hud?.ShowRunRecap(model, victory ? "Continue into the endless depths" : "To the Records", onClose)`; server: `Broadcast(new RunRecapEvt(model, button))`; records: `ProfileRules.RecordRun(profile, RunSummaryOf())`, `DeedWatcher.Note(StoryCleared(Flag: Hardcore))` when `Run == Story`, `Note(HeatClear(Active.Heat))`, daily → `RecordDaily` + `Note(DailyDone)`; `ProfileStore.Save()`. `Main.RunSummaryOf() : RunSummaryDto` (from `Adventure`, the ledger and `PlaySeconds`); also called at every floor change (`RebuildFloorInner`, solo/server) to upsert the profile and the daily. `[Msg] FloorCardEvt(FloorCardModel Card)`, `[Msg] RunRecapEvt(RunRecapModel Recap, string Button)`; `ClientWorld.OnBossDefeated` (:263-267) drops the `ShowVictory` line (keeps `Game.Instance.Victory = true`) and handles `case RunRecapEvt r: Main.Instance.Hud?.ShowRunRecap(r.Recap, r.Button, () => { }); break; case FloorCardEvt f: Main.Instance.Hud?.ShowFloorCard(f.Card); break;`. `Protocol.Version` = live + 1 (once, in Task 9), `MessageKindCount` = live + 3. Co-op's plan (`2026-11-07-co-op.md`, Task 6) reads the role lines through `RecapRules.RoleLines(IReadOnlyList<FloorRecord> floors, IReadOnlyList<string> players) : IReadOnlyList<(string Role, string Player)>`: expose exactly that as `public static class RecapRules` in `RecapBuilder.cs` (namespace `DungeonCrawler.Adventure`), where `players` are roster names (on a server a member is named after its player). Roles, assigned in this order to the best player who holds no role yet, stopping once every player has one and skipping a role nobody qualifies for: `"Most rescues"` (max `RescuesBy`, > 0), `"Vanguard"` (max `KillsBy`), `"Steadiest"` (fewest `DownsBy`), `"Longest watch"` (max `PossessSeconds`); never damage. `RunRecapModel.PlayerLines` are `$"{Role}: {Player}"`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class RecapBuilderTests
{
    private static CampaignMemoryDto Memory()
    {
        var m = new CampaignMemoryDto();
        var f1 = m.For(1); f1.Title = "The Narthex"; f1.Seconds = 200; f1.Kills = 9; f1.Elites = 1; f1.SecretsFound = 1; f1.SecretsTotal = 2; f1.Downs = 1; f1.GoldFound = 40; f1.WorstMoment = "Sable fell to the Press";
        f1.KillsBy["Brand"] = 5; f1.KillsBy["Sable"] = 4; f1.DamageBy["Brand"] = 300; f1.DownsBy["Sable"] = 1; f1.RescuesBy["Wren"] = 2; f1.Rescues = 2; f1.PossessSeconds["Brand"] = 150; f1.Completed = true;
        var f2 = m.For(2); f2.Seconds = 100; f2.Kills = 3; f2.Wipes = 1; f2.Completed = true;
        return m;
    }
    [Fact] public void FloorCard_HasEveryField()
    {
        var c = RecapBuilder.FloorCard(Memory().For(1), "The Narthex", "Brand: \"Stairs. Good. I was tired of this one.\"");
        Assert.Equal("The Narthex", c.Title); Assert.Equal("3m 20s", c.Time); Assert.Equal(9, c.Kills); Assert.Equal(1, c.Elites); Assert.Equal("1/2", c.Secrets); Assert.Equal(1, c.Downs); Assert.Equal(40, c.GoldFound);
        Assert.Equal("Sable fell to the Press", c.WorstMoment); Assert.StartsWith("Brand:", c.Reaction);
    }
    [Fact] public void Build_PerHeroLines_RolePraise_Score()
    {
        var heroes = new[] { "Brand", "Sable", "Wren", "Ilex" };
        var players = new[] { "Wren", "Brand" }; // on a server these are the players' names
        var r = RecapBuilder.Build(Memory(), heroes, new[] { "conveniencefee" }, new[] { "Toll Road" }, true, "X is slain.", wipes: 1, players);
        Assert.Equal(4, r.Heroes.Count); Assert.Equal(5, r.Heroes[0].Kills); Assert.Contains("5 kills", r.Heroes[0].Text); Assert.Equal(2, r.Heroes[2].Rescues);
        Assert.Equal(2, r.FloorLines.Count); Assert.Contains("wiped", r.FloorLines[1]);
        Assert.Equal(1, r.ElitesSlain); Assert.Equal(2, r.DeepestFloor); Assert.Equal(ScoreRules.Score(2, 1, 1, 1, 1), r.Score);
        Assert.Equal(new[] { "Most rescues: Wren", "Vanguard: Brand" }, r.PlayerLines);
        Assert.DoesNotContain(r.PlayerLines, l => l.Contains("damage", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(RecapBuilder.Build(Memory(), heroes, Array.Empty<string>(), Array.Empty<string>(), false, "h", 0).PlayerLines);
    }
    [Fact] public void RoleLines_GiveEveryPlayerADifferentRole_AndSkipEmptyRoles()
    {
        var floors = new List<FloorRecord> { new() { KillsBy = { ["Ash"] = 9, ["Bryn"] = 2 }, DownsBy = { ["Ash"] = 2 }, RescuesBy = { ["Bryn"] = 3 } } };
        var lines = RecapRules.RoleLines(floors, new[] { "Ash", "Bryn" });
        Assert.Equal(2, lines.Count); Assert.Equal(("Most rescues", "Bryn"), lines[0]); Assert.Equal(("Vanguard", "Ash"), lines[1]);
        Assert.Equal(("Vanguard", "Ash"), Assert.Single(RecapRules.RoleLines(new List<FloorRecord> { new() { KillsBy = { ["Ash"] = 1 } } }, new[] { "Ash" })));
    }
    [Fact] public void VictoryHeadline_NamesTheCampaignsBossAndAntagonist()
    {
        Assert.Equal("The Verger is slain. Absalom Quill's hold on the depths is broken.", RecapBuilder.VictoryHeadline("The Verger", "Absalom Quill"));
        Assert.Equal("The guardian is slain. The depths lie open.", RecapBuilder.VictoryHeadline(null, null));
    }
}
// tests/Net/NetCodecTests.cs (add)
[Fact] public void Cards_RoundTrip_AndTheProtocolIsSeven()
{
    var f = RoundTrip(new FloorCardEvt(new FloorCardModel("Floor 3", "1m 2s", 4, 1, "0/1", 0, 12, "Nothing worse than a stubbed toe", ""))); Assert.Equal("0/1", f.Card.Secrets);
    var r = RoundTrip(new RunRecapEvt(new RunRecapModel("h", new() { "a" }, new(), 1, new(), new() { "fineprint" }, 5, 600, new() { "Most rescues: Ash" }, true), "Continue")); Assert.Equal(600, r.Recap.Score); Assert.Equal("Continue", r.Button);
    Assert.Equal(8, Protocol.Version); Assert.Equal(85, Protocol.MessageKindCount); // live values + 1 and + 3: Task 1 corrects the numbers
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "RecapBuilderTests|NetCodecTests"`.
- [ ] **Step 3: Implement** `RecapBuilder`, then the controls, Hud, Main, director, messages, client.
- [ ] **Step 4: Run** → PASS; build; `timeout 60 godot --headless --path . -- --test-depth 2>&1 | grep -E "victory|RESULT"` → the depth flow still reports `victory=True` (it reads `Game.Victory`, Main.cs:2272 in DebugTools).
- [ ] **Step 5: Commit** — `git add -A src tests && git commit -m "feat(run): a floor card on every descent and a run recap on victory that names the campaign's own boss"`

### Task 11: The Records page

**Files:**
- Create: `src/UI/Menus/RecordsScreen.cs`
- Modify: `src/UI/Menus/MainMenu.cs` (`OpenRecords()`, the Records button, `Reset`)

**Interfaces:**
- Consumes: `ProfileStore.Current`, `ProfileRules.Title/BorderTier/HighestHeatClear`, `DeedRules.All`, `SlotInfo.FormatTime`.
- Produces: `RecordsScreen : Control` with `event Action? Closed` and `Refresh()`: a `PanelContainer` whose `StyleBoxFlat` border is 2 px grey at tier 0, gold (`#c9a44a`) at tier 1, ember (`#d9532b`) at tier 2; heading "RECORDS" with the title beneath when `Title != null`; a records block (Best depth `floor N`, Fastest story clear `h m`, Highest heat clear `N` with the mode list from `HeatClears`, Victories, Elites slain, Runs); a dailies block (the last seven `Daily` entries by date: `date  floor N  score S`); a runs block (`Runs`, newest first: `{Run} · seed {Seed} · floor {DeepestFloor} · heat {Heat} · score {Score} · {Victory ? "cleared" : Fallen ? "fallen" : "left"}`); a deeds block (24 rows: earned rows bright with `"{Name} — {Condition} · unlocked {UnlockText} · {date}"`, unearned rows at 55% alpha `"{Name} — {Condition} · unlocks {UnlockText}"`); a `Back` button. `MainMenu.OpenRecords()` hides the buttons, refreshes and shows it; `Reset` hides it.

- [ ] **Step 1: Implement** (Godot only; the flow in Task 12 pins that it opens and lists 24 deeds).
- [ ] **Step 2: Build** → 0 errors; `timeout 30 godot --headless --path . -- --test-keys 2>&1 | grep -E "ERROR|RESULT"` (the menu still boots).
- [ ] **Step 3: Commit** — `git add src/UI/Menus/RecordsScreen.cs src/UI/Menus/MainMenu.cs && git commit -m "feat(run): the Records page: depth, clears, dailies, elites, the last runs and the deeds"`

### Task 12: `--test-run-structure`, docs

**Files:**
- Modify: `src/Core/DebugTools.cs` (flag beside `:327`, dispatch beside `:738`, the flow), `docs/development.md` (test list after the `--test-safe-room` lines; a new section "Runs, clauses, deaths and records" before "Multiplayer"; the server command line gains `--clauses=a,b:2`), `README.md` (only if it describes the title screen: one line on New Descent, Daily Descent and Records; no keys changed)

**Interfaces:**
- Consumes: everything above; `Main.StartDescent/StartDaily/StartBuiltinCampaign/SignClauses/OnBossDefeated/SaveGame/LoadGame`, `ProfileStore.FileName`, `Hud` card visibility (`Hud.CardUp`, `Hud.FloorCardVisible`, `Hud.ClauseCardVisible`), `DeedWatcher.Note`.
- Produces: `--test-run-structure` (`_testRunStructure = true; NoAmbush = NoPortals = NoGathering = true; TraceAi = false; _seed ??= 7; ForcedLethality ??= 1f; _quitAfter = 120`), `ProfileStore.FileName = "profile-test.json"` (deleted at the end), printing `[runstructure] ...` lines and `[runstructure] RESULT ok|FAIL (why)`. Steps (a step machine like `RunSafeRoomTest`, each step waits for the floor it needs):
  1. Story seed: `Main.StartBuiltinCampaign()`; after the floor builds, `storySeed = Game.GameSeed` (7319 with the shipped campaign) and `Adventure.Run == Story`.
  2. New Descent: `Main.StartDescent()` → `Game.GameSeed != storySeed`, `Run == Descent`.
  3. Daily: `Main.StartDaily(new DateOnly(2026, 11, 7))` → `GameSeed == DailyRules.SeedFor(date)`, `Adventure.Clauses == DailyRules.ClausesFor(date)`, `ProfileRules.DailyDone(ProfileStore.Current, "2026-11-07")`; start it again → the same seed (stable).
  4. Clauses and prices: `Main.StartNewGame(7, false)` (floor 1), `Main.SignClauses(new[] { "conveniencefee" })` → `ShopRules.ClausePriceMultiplier == 1.4f` and `Prices.BuyPrice(stock[0]) == floor(2.5 × 1.4 × Value)`; `SaveGame("rs")`, `LoadGame("rs")` → after the rebuild `Adventure.Clauses == ["conveniencefee"]` and the multiplier is 1.4 again.
  5. Wipe: kill the first `Enemy` on the floor (`ReceiveAttack` with a 9999 attack, remember its `SpawnIndex`), `goldBefore = Party.Gold` (give 100 first), spawn `enemy = Builder.SpawnEnemy(first regular id, near the party, floor)`, then for every member `adv.ReceiveAttack(enemy.Stats, new AttackProfile { MinDamage = 9999, MaxDamage = 9999 }, enemy.DisplayName, enemy)`; next frame the party has wiped: assert `Game.Floors[1].DeadEnemies` contains the index, `Party.Gold == WipeRules.GoldAfterWipe(goldBefore)`, the Scar curse is present with `UntilFloor == 2` and `Members[0].Stats[MaxHp]` is 90% of its value before (±1), `Hud.CardUp`, the death card's epitaph is non-empty and its cause is `"generic"` with killer `enemy.DisplayName`, `Memory.For(1).Wipes == 1`; after the rebuild no `Enemy` with that `SpawnIndex` exists; press the card's button (call its close action).
  6. Death Clause: `Main.SignClauses(["deathclause"])` (allowed by the flow through a `force` parameter), wipe again as in 5 → `Adventure.Fallen`, `Main.RunOver`, `SaveManager.Info("auto").Fallen`, `LoadGame("auto") == false`, the card's button text is "To the Records"; close it → the recap is up with `Victory == false`; close → the menu is visible.
  7. Autosave: `Main.StartNewGame(7, false)`; delete `auto`; `Game.ChangeFloor(+1)`; after floor 2 builds → `SaveManager.Exists("auto")`, `Hud.FloorCardVisible` with a model whose `Secrets` matches `"\d+/\d+"` and `Elites >= 0` (read back through `Hud.LastFloorCard`), gone after 4.5 s.
  8. Recap model: `RecapBuilder.Build(Memory, member names, clauses, [], false, "h", 0).Heroes.Count == 4`.
  9. Profile and a deed: `ProfileStore.Current.Records.BestDepth >= 2`; raise `EventBus.RaisePartyWipedBy(2, "generic")` four times → `ProfileStore.Current.Deeds.ContainsKey("bang_on_schedule")`, `Unlocks` contains `banter:wipes`, a toast is visible.
  10. Victory: `Game.JumpToFloor(20)`; after it builds `Main.OnBossDefeated(20)` → `Game.Victory`, `Hud.CardUp`, the recap headline contains `Builder.Contents?.Plan?.Boss?.Name ?? campaign.Tiers.Last().BossName` and `campaign.Antagonist.Name`; close; `SaveGame("rs")`, `LoadGame("rs")` → `Game.Victory` still true.
  11. `File.Delete(profile-test.json)`, `RESULT ok`.

- [ ] **Step 1: Write the flow** as above (add `Hud.FloorCardVisible`, `Hud.LastFloorCard`, `Hud.ClauseCardVisible`, `Hud.CloseCard()` as test seams).
- [ ] **Step 2: Run** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && timeout 150 godot --headless --path . -- --test-run-structure 2>&1 | grep -E "^\[runstructure\]|RESULT|ERROR"` → `[runstructure] RESULT ok`, no `ERROR`.
- [ ] **Step 3: Docs** — `docs/development.md`: add `godot --headless --path . -- --test-run-structure   # Story vs New Descent seeds, a stable Daily, clauses saved and priced, a wipe that keeps the floor, costs gold, scars and shows a death card, the Death Clause's memorial, the autosave, floor card and recap models, the profile and a deed, Victory saved; prints [runstructure] RESULT` to the list; the new section: the three title modes, the clause table with heat and the knob each one turns, the heat multiplier, the stakes (floor captured before the reload, −20% gold, Scar until the next floor, the death card and causes `gas|press|spikes|boss|horde|elite|fall|mimic|bomber|generic` from `CharacterState.LastHarm`), the Death Clause (memorial slot, quickload refused, pause-menu Load hidden), autosave (`auto` slot, floor change and 180 s), the cards, Records, `user://profile.json` (atomic, `.bak`, last 20 runs, 24 deeds and their unlocks, `--test-run-structure` uses `profile-test.json`), the score formula, the note that No Rest makes shipped plans log "adapted", and `--clauses=` (any new game in the process, so a server's world too) in the server command line; the Multiplayer section notes protocol 7 and the three card messages.
- [ ] **Step 4: Full verification** — `cd tests && dotnet test --nologo -v q` → all PASS; `--test-run-structure`, `--test-tavern`, `--test-safe-room`, `--test-gas-wipe`, `--test-save`, `--test-depth` → their RESULT lines ok; `scripts/net_test.sh` → both RESULT lines ok (the client receives the floor card on the floor-2 arrival: add one Trace assertion `floorcard` in `NetTestFlow` beside the floor-2 step).
- [ ] **Step 5: Commit** — `git add src/Core/DebugTools.cs src/Net/NetTestFlow.cs src/UI/Hud/Hud.cs docs/development.md README.md && git commit -m "test(run): the headless run-structure flow and the docs"`

---

## Self-review notes

- Spec coverage: title modes (T8), clauses on a card after the briefing with heat and `1+0.1×heat` (T2, T8), saved in `AdventureDto.Clauses` and shown in the pause menu (T4, T8), `--clauses=` and the Welcome (T8), every clause's effect on its knob (T7, T9 for hardcore), stakes: capture before reload, −20% gold, Scar until the next floor, villain gloat (existing), death card with epitaph/killer/floor/wipe count and "Rise" (T9), Death Clause memorial greyed in Load with the epitaph (T4, T9), autosave `auto` on floor change and every three minutes, quickload refused (T9), floor card fields and the reaction line (T1, T10), recap per hero and per player with role praise, victory naming the campaign's boss and antagonist, Victory saved (T4, T10), Records page (T11), profile with records, last twenty runs, deeds and unlocks, toasts (T5, T6), heat 5/10 cosmetics (T5, T11), first clear per heat per mode (T5), score (T3), headless flow (T12).
- Type consistency: `ClauseSet` methods used in T7–T10 match T2; `FloorCardModel`/`RunRecapModel`/`DeathCardModel` records are the wire payloads in T9–T10; `RunSummaryDto` (T5) is what `Main.RunSummaryOf()` (T10) builds; deed ids in `ClauseRules.RankUnlockedByDeed` (T2) match `DeedRules` (T6, pinned by `TwentyFourDeeds_...`).
- Resolutions of spec gaps and collisions (reported to the owner): `AdventureDto.Mode` already means "who planned", so the run kind is `AdventureDto.Run : RunKind`; Full Occupancy is applied to the resolved contents (shipped plans are pre-written, so a spec-build multiplier alone would never touch the Story); No Rest removes the room at the generator (`FloorSpec.SafeRoom`), which changes the layout hash and makes shipped plans log "adapted" under that clause; Expedited Delivery: grace 45 s at any rank, the ramp halves per rank; Hazard Pay Waived multiplies damage only, never the hazards' kill threshold; the Death Clause also hides the pause menu's Load; the Daily is "one attempt" by disabling the button for the date (its save still loads); six of items' twelve legendaries are deed-gated, the other six stay in the pool; protocol +1 with three messages (the live number depends on which sibling plans landed first); the recap's role lines take the shape co-op's plan expects (`RecapRules.RoleLines(floors, players)`), and "Vanguard" (most kills) is a role so every player can be named, while damage is never ranked; `MemberRescued` and `GoldFound` are two events beyond the contract list; the "fall to a status tick" cause stays with the hit that applied it.
