# Elites and Bosses Implementation Plan (Depths pillar 2: "Creatures worth meeting")

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No two floors meet the same creatures: named elites with twelve readable affixes rolled deterministically per floor, tier bosses with a second phase and an arena change, bosses every five floors in the endless depths, and three new enemy behaviours (kiting shooters, support casters, horde bombers).

**Architecture:** An engine-free `EliteRules` (+ `EliteNames`, `BossRules`, `BomberRules`, `ShroudRules`) in `src/Enemies/Core/Elites/` holds every number and every roll; `PlanResolver` (and `LegacyPopulator`) stamp an `EliteRoll` onto `EnemyPlacement`s from `(floor seed, spawn index)`, `DungeonBuilder.SpawnEnemy` applies it to the `EnemyProfile` before the node exists, and `Enemy` carries the affix behaviours in its existing hooks (`_Ready`, `ChangeState`, `ReceiveAttack`, `StrikeParty`, `OnDeath`). Boss phases live in `Enemy` + a `RoarState`, arena changes are props spawned by a Godot `BossArena` helper, archetypes are three `EnemyDef` flags with three new `AiState`s. Save keeps only the floor's elite spawn indices (the roll is re-derived); the net carries elite identity only for dynamic spawns (`DynamicEnemyDto`, protocol bump).

**Tech Stack:** Godot 4.7.2 .NET / C#, xUnit (`tests/`, dev machine only), headless `--test-elites-and-bosses` flow in `src/Core/DebugTools.cs`.

**Spec:** `docs/superpowers/specs/2026-11-07-the-depths-update-design.md` section 2 (binding; every number below is copied from it; "(tune)" numbers are constants in a `*Rules` class). **Contract:** the shared-surface names in the 2026-11-07 plan contract (EventBus events, `BarkTrigger.EliteSeen`, `TauntTrigger.EliteSlain`, `StatusKind.Vulnerable`, `table.elite`, `EnemyProfile.Affixes`/`EliteName`, `Enemy.IsElite`, `FloorRecord.Elites`). This plan runs after **combat-feel**, **writing** and **items-and-builds** have merged; Task 1 verifies their members and adapts names if one differs.

## Global Constraints

- Engine-free rules live under `src/Enemies/Core/Elites/` (already linked: `tests/DungeonCrawler.Tests.csproj` includes `../src/Enemies/Core/**/*.cs`), `src/Adventure/Core/**` and `src/Dungeon/TierRules.cs`; they never reference `Godot` and are tested with xUnit before any Godot glue.
- Numbers, verbatim from the spec: elites per floor 1 from floor 2, 2 from 8, 3 from 14; affixes 1 on floors 2–7, 1–2 on 8–13, 2–3 from 14; Shrouded and Hexed never together; Swift move ×1.35 wind-up ×0.8; Hulking HP ×2.2 scale ×1.3 damage ×1.25; Warded shield 25% max HP regrowing 10 s after it breaks; Vampiric heals 40% of damage dealt; Volatile 4 m, 60% of its hit, Burn; Splitting two of the tier's horde brute; Thorned 30% back to melee attackers; Cursing hits apply Vulnerable; Shrouded 4 m dome; Hexed Slow 2 s on melee hitters; Alarmist wakes everything within 25 m; Juggernaut immune to stagger and stun, armor +20; kill reward XP ×2.5 + one `table.elite` roll (0 Common, Rare 50, Legendary 12, 1 roll) + coins; boss roar 1.2 s untargetable, phase two speed ×1.2, second signature on the shared cooldown; Broodmother summon cap 4; webs = three Slow patches; Colossus two Burn patches; Herald Slow aura 6 m; endless boss every five floors from 25, "the Nth Herald", death silences portals five floors; Kites retreat when the party is within half of PreferredRange; Support: ally within 8 m below 70% HP, 30-point Shield or 3/s Regen, every 8 s; Bomber 3 m, damage ×3, no corpse, from floor 6 at 1 in 8 of a stream; resists above 75 in data clamped to 75.
- Determinism: everything rolled at build time comes from `(floor seed = Rng.FloorSeed(GameSeed, floor), spawn index)`, never `Random.Shared`; peers and reloads agree.
- Co-op is server-authoritative: affix effects (damage, statuses, spawns, heals) run only where `!Net.NetSession.IsClient`; a client draws tells, bars and arena cosmetics from what it already receives (`EnemySpawned`, snapshots, `EnemyHurt`).
- Save: `FloorStateDto.EliteSpawns` (`List<int>`, default empty) is the only new field. Save v11: run-structure bumps `SaveFormat.CurrentVersion`; if it has not landed when Task 4 runs, bump to 11 yourself (defaults only, no migration code).
- Net: `DynamicEnemyDto` gains two optional fields (Task 5), so `Protocol.Version` is bumped by one (7 if nobody else has bumped it yet, else current + 1) and `tests/Net/NetCodecTests.cs:232` is updated to match.
- Build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before any `godot --headless` run; game flags after a bare `--`; the flow uses `--no-ambush --no-portals --no-gathering --lethality=1`.
- No new 3D art: tells are tints, scales, particles, decals and meshes built from boxes, cylinders, spheres and tori in the theme's or an iron material (as `Dungeon/Props/Rubble.cs` does).
- Shipped campaign: the plans in `data/adventures/builtin` carry no `Elite` fields; they load unchanged, and the elite roll adds no "adapted" line (the roll never moves a spawn). The sweep plan reruns `--builtin`.

## Review Focus

Spec-implied cases no task's main tests cover; each has its pin named in its owning task.

1. A planner that marks a horde member, a bandit chief in a camp, or more creatures than the floor allows as `Elite`: hordes are stripped (horde creatures are never elites), extras are trimmed in order, and the rest are filled; the count on the floor is always exactly `CountFor`. Pin: Task 3 `Validate_StripsHordeElites_AndTrimsToTheFloorsCount`.
2. Save mid-floor after the *Marked* curse is lifted (fewer elites would roll), load: the same elites stand there (`EliteSpawns` wins over the fresh roll). Pin: Task 4 `Capture_WritesEliteSpawns` and the flow step "save/load keeps the elites".
3. A Shrouded elite that is itself a shooter: its own bolts leave the dome; a party arrow loosed from inside the dome still hits; a nova cast from inside hits it, one cast from outside does not. Pin: Task 7 `SegmentEntersDisc_FromInsideIsNotAnEntry` and the flow's dome step.
4. A boss loaded at 40% HP comes back in phase two with its arena already changed and no roar; a Volatile or Splitting elite killed by the Press still explodes or splits (the split brutes carry no loot, like any dynamic spawn). Pin: Task 9 `RestoreFrom_BelowHalf_EntersPhaseTwoSilently` (headless, in the flow) and Task 6 flow step "hazard kill still detonates".
5. Endless: floor 23 has no boss and its stairs are open; floor 25's stairs are sealed until "the First Herald" dies; victory is not declared again; portals are silent on 25–29 after its death and active again on 30 until that Herald dies. Pin: Task 11 `BossFloorFor_EndlessHeraldsCommandTheirOwnFiveFloors` and `IsBossFloor_EveryFifthEndlessFloor_NotTwentyThree`.

---

### Task 1: Verify the shared surface and add the elite data on the profile

**Files:**
- Modify: `src/Enemies/Core/EnemyProfile.cs:11-56` (fields), `src/Enemies/Enemy.cs:39-41` (`DisplayName`, `IsElite`)
- Verify (add only what is missing, exactly as named): `src/Core/EventBus.cs` (`EliteSeen`, `EliteSlain`), `src/Adventure/Core/Plans/CampaignPlan.cs:6` (`BarkTrigger.EliteSeen`), `src/Adventure/Core/Objectives/Taunts.cs:6-12` (`TauntTrigger.EliteSlain`), `src/Combat/Core/StatusEffects.cs:5` (`StatusKind.Vulnerable`), `src/Adventure/Core/Memory/CampaignMemory.cs:6-29` (`FloorRecord.Elites`), `src/UI/Hud/Hud.cs` (boss bar + slim elite bar), `src/Enemies/Enemy.cs` (`Stagger`), `src/Party/Adventurer.cs:896` (`ReceiveAttack` return type)
- Test: `tests/Enemies/EliteProfileTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `public enum EliteAffix { Swift, Hulking, Warded, Vampiric, Volatile, Splitting, Thorned, Cursing, Shrouded, Hexed, Alarmist, Juggernaut }` (file `src/Enemies/Core/Elites/EliteAffix.cs`, namespace `DungeonCrawler.Enemies`); `EnemyProfile.Affixes` (`IReadOnlyList<EliteAffix>`, default `Array.Empty<EliteAffix>()`), `EnemyProfile.EliteName` (`string?`), `EnemyProfile.IsElite => Affixes.Count > 0`, `bool EnemyProfile.Has(EliteAffix a)`; `Enemy.IsElite => Profile.IsElite`; `Enemy.DisplayName => NameOverride ?? Profile.EliteName ?? Profile.Name`.
- Assumptions this plan makes about the three earlier plans (write the verified names into this task's notes; if one differs, use the real name everywhere below):
  - `EventBus`: `event Action<Enemies.Enemy>? EliteSeen; RaiseEliteSeen(Enemy)`, `event Action<Enemies.Enemy>? EliteSlain; RaiseEliteSlain(Enemy)` (writing). Missing → add beside `BossSeen` (`EventBus.cs:56-58`, raise methods beside `RaiseBossSeen` at `:141`).
  - `StatusKind.Vulnerable` appended (combat-feel), and the party's damage intake honours it (+25% taken, 6 s). Missing → append the enum value only (the intake rule is combat-feel's; note it in the commit).
  - The HUD boss bar is driven by `BossSeen`/`EnemyHurt`/`BossHalf`/`BossDefeated` and the slim elite bar subscribes to `EliteSeen` and reads `enemy.DisplayName` (combat-feel). Assumed API if a direct call is ever needed: `Hud.ShowBossBar(Enemy)` / `Hud.ShowEliteBar(Enemy)`. If the HUD has no `EliteSeen` subscription, add exactly one line in `Hud._Ready` beside its `BossSeen` subscription: `bus.EliteSeen += ShowEliteBar;` (and nothing else in Hud.cs).
  - `Enemy.Stagger(float seconds)` exists (combat-feel's stagger). Missing → Task 6 skips the Juggernaut guard on it and says so.
  - `Adventurer.ReceiveAttack(Stats, AttackProfile, string, ICombatTarget?)` is `void` today (`Adventurer.cs:896`). Task 6 changes it to return `DamageResult`; if combat-feel already did, keep theirs.
  - `FloorRecord.Elites` (`int`) exists (contract). Missing → add `public int Elites { get; set; }` after `Kills` (`CampaignMemory.cs:12`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Enemies/EliteProfileTests.cs
using DungeonCrawler.Enemies;
using Xunit;

namespace DungeonCrawler.Tests.Enemies;

public class EliteProfileTests
{
    [Fact] public void TwelveAffixes_InSpecOrder()
        => Assert.Equal(new[] { "Swift", "Hulking", "Warded", "Vampiric", "Volatile", "Splitting", "Thorned", "Cursing", "Shrouded", "Hexed", "Alarmist", "Juggernaut" }, Enum.GetNames<EliteAffix>());

    [Fact] public void Profile_IsElite_WhenItHasAnAffix()
    {
        var plain = new EnemyProfile { Name = "Ghoul" };
        Assert.False(plain.IsElite); Assert.Empty(plain.Affixes); Assert.Null(plain.EliteName);
        var elite = plain with { Affixes = new[] { EliteAffix.Swift }, EliteName = "Grukk the Swift" };
        Assert.True(elite.IsElite); Assert.True(elite.Has(EliteAffix.Swift)); Assert.False(elite.Has(EliteAffix.Hexed));
    }
}
```

- [ ] **Step 2: Run it to see it fail** — `cd tests && dotnet test --nologo -v q --filter EliteProfileTests` → build error: `EliteAffix` missing.
- [ ] **Step 3: Implement** the enum file and the three `EnemyProfile` members; `Enemy.IsElite` and the `DisplayName` change. Then open each file in the Verify list and record the verified names (a short comment block at the top of `EliteAffix.cs` is fine: "shared members verified 2026-..: EventBus.EliteSeen ✓ …"); add any missing one exactly as named above.
- [ ] **Step 4: Run** — same filter → PASS; `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Enemies tests/Enemies/EliteProfileTests.cs src/Core/EventBus.cs src/Adventure/Core src/Combat/Core src/UI/Hud && git commit -m "feat(elites): EliteAffix and the elite fields on EnemyProfile; shared surface verified"`

### Task 2: EliteRules and EliteNames (engine-free)

**Files:**
- Create: `src/Enemies/Core/Elites/EliteRules.cs`, `src/Enemies/Core/Elites/EliteNames.cs`
- Test: `tests/Enemies/EliteRulesTests.cs`, `tests/Enemies/EliteNamesTests.cs`

**Interfaces:**
- Consumes: `EnemyProfile`, `AttackProfile`, `StatusApplication`, `StatusKind.Vulnerable`, `Rng.Mix` (`src/Core/Rng.cs:7`).
- Produces (namespace `DungeonCrawler.Enemies`):
  - `public sealed record EliteRoll(IReadOnlyList<EliteAffix> Affixes, string Name);`
  - `public static class EliteRules` with constants `const int FirstEliteFloor = 2, SecondEliteFloor = 8, ThirdEliteFloor = 14; const float SwiftMove = 1.35f, SwiftWindup = 0.8f, HulkingHp = 2.2f, HulkingScale = 1.3f, HulkingDamage = 1.25f, WardFraction = 0.25f, WardRegrowSeconds = 10f, VampiricFraction = 0.4f, VolatileRadius = 4f, VolatileFraction = 0.6f, VolatileBurnSeconds = 4f, ThornedFraction = 0.3f, ShroudRadius = 4f, HexSlowSeconds = 2f, AlarmRadius = 25f, JuggernautArmor = 20f, VulnerableSeconds = 6f, XpMultiplier = 2.5f; const int SplitCount = 2;` and
    - `int CountFor(int floor, int extra)` → `floor < 2 ? 0 : (floor >= 14 ? 3 : floor >= 8 ? 2 : 1) + Math.Max(0, extra)` (floor 1 stays 0 even with extra: "From floor 2").
    - `(int Min, int Max) AffixRange(int floor)` → (1,1) on 2–7, (1,2) on 8–13, (2,3) from 14.
    - `bool Conflict(EliteAffix a, EliteAffix b)` → Shrouded/Hexed only.
    - `int RollSeed(int floorSeed, int spawnIndex)` → `unchecked((int)Rng.Mix(((ulong)(uint)floorSeed << 20) ^ (ulong)(uint)spawnIndex ^ 0xE117EUL))`.
    - `EliteRoll Roll(int floorSeed, int floor, int spawnIndex, string faction = "")`: `var rng = new Random(RollSeed(...))`; count from `AffixRange(floor)`; draw distinct affixes, rejecting any that conflicts with one already drawn; name from `EliteNames.Make(rng, affixes, faction)`.
    - `IReadOnlyList<int> Choose(int floorSeed, IReadOnlyList<int> eligibleSpawnIndices, int count)`: `new Random(unchecked((int)Rng.Mix((ulong)(uint)floorSeed ^ 0xE117E5UL)))`, a partial Fisher–Yates over a copy of the list, first `min(count, n)` entries, returned sorted ascending.
    - `EliteRoll Complete(IReadOnlyList<EliteAffix>? wanted, string? name, int floorSeed, int floor, int spawnIndex, string faction = "")`: the planner's fields filled: affixes kept when non-empty after `Sanitize`, else rolled; name kept when non-blank (clipped to 40), else `EliteNames.Make` with the same rng as `Roll`.
    - `List<EliteAffix> Sanitize(IEnumerable<string> names, int floor)`: parse case-insensitively, drop unknown, dedupe keeping order, drop the later of a conflicting pair, trim to `AffixRange(floor).Max`.
    - `EnemyProfile Apply(EnemyProfile p, EliteRoll roll)`: `p with { Affixes = roll.Affixes, EliteName = roll.Name, MoveSpeed = Swift ? ×1.35, WindupSeconds = Swift ? ×0.8, MaxHp = Hulking ? ×2.2, Scale = Hulking ? ×1.3, MinDamage/MaxDamage = Hulking ? ×1.25, Armor = Juggernaut ? +20 }`.
    - `AttackProfile ApplyAttack(AttackProfile a, IReadOnlyList<EliteAffix> affixes)`: Cursing → `a.WithExtraOnHit(new[] { new StatusApplication(StatusKind.Vulnerable, VulnerableSeconds, 0f) })`, else `a`.
    - `string Describe(IReadOnlyList<EliteAffix> affixes)` → "Swift" / "Warded and Vampiric" / "Swift, Warded and Vampiric".
    - `string? HordeBruteFor(IReadOnlyDictionary<string, EnemyProfile> roster, int tierIndex)` → the roster id with `Faction == "horde"`, `TierIndex == tierIndex` and `Id.EndsWith("_brute")`, else null.
  - `public static class EliteNames { static string Make(Random rng, IReadOnlyList<EliteAffix> affixes, string faction = ""); static string Title(string name, IReadOnlyList<EliteAffix> affixes); }` where `Title` is "{name} the {A}" for one affix and "{name}, {Describe}" for more; `Make` draws a base name from a syllable table (two syllables from two lists of 24 each, guttural for the default style: e.g. Grukk, Hask, Morvex, Ulgar, Thrane, Vorrik), prefixed one time in four by an honorific from a list of eight (Mother, Old, Brother, Sister, Deacon, the Widow, Warden, Uncle); faction `"bandits"` draws from a human-name list of 24 instead (Hask, Dunmore, Pell, Corrin, Ashby, Tamsin…). Write the tables well; the register is dry. Hard rules the tests pin: deterministic for the same rng seed, 3–24 characters before the title, never empty, honorific and name separated by a single space.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Enemies/EliteRulesTests.cs
public class EliteRulesTests
{
    [Theory, InlineData(1, 0), InlineData(2, 1), InlineData(7, 1), InlineData(8, 2), InlineData(13, 2), InlineData(14, 3), InlineData(25, 3)]
    public void CountFor_FollowsTheSchedule(int floor, int expected) => Assert.Equal(expected, EliteRules.CountFor(floor, 0));

    [Fact] public void CountFor_AddsExtras_ButNeverOnFloorOne() { Assert.Equal(3, EliteRules.CountFor(5, 2)); Assert.Equal(0, EliteRules.CountFor(1, 2)); }

    [Theory, InlineData(2, 1, 1), InlineData(7, 1, 1), InlineData(8, 1, 2), InlineData(13, 1, 2), InlineData(14, 2, 3), InlineData(30, 2, 3)]
    public void AffixRange_FollowsTheSchedule(int floor, int min, int max) => Assert.Equal((min, max), EliteRules.AffixRange(floor));

    [Fact] public void Roll_IsDeterministic_AndWithinRange()
    {
        for (var floor = 2; floor <= 20; floor += 3)
            for (var i = 0; i < 40; i++)
            {
                var a = EliteRules.Roll(1234, floor, i); var b = EliteRules.Roll(1234, floor, i);
                Assert.Equal(a, b);
                var (min, max) = EliteRules.AffixRange(floor);
                Assert.InRange(a.Affixes.Count, min, max);
                Assert.Equal(a.Affixes.Count, a.Affixes.Distinct().Count());
                Assert.False(a.Affixes.Contains(EliteAffix.Shrouded) && a.Affixes.Contains(EliteAffix.Hexed));
                Assert.EndsWith(a.Affixes.Count == 1 ? $" the {a.Affixes[0]}" : EliteRules.Describe(a.Affixes), a.Name);
            }
    }

    [Fact] public void Roll_DiffersAcrossSpawnIndicesAndSeeds()
    {
        var rolls = Enumerable.Range(0, 60).Select(i => EliteRules.Roll(77, 14, i)).ToList();
        Assert.True(rolls.Select(r => r.Name).Distinct().Count() > 40);
        Assert.NotEqual(EliteRules.Roll(77, 14, 3), EliteRules.Roll(78, 14, 3));
    }

    [Fact] public void Choose_IsDeterministic_Sorted_AndNeverMoreThanAsked()
    {
        var eligible = new[] { 0, 1, 4, 5, 9, 12, 13 };
        var a = EliteRules.Choose(99, eligible, 2); var b = EliteRules.Choose(99, eligible, 2);
        Assert.Equal(a, b); Assert.Equal(2, a.Count); Assert.Equal(a.OrderBy(x => x), a); Assert.All(a, x => Assert.Contains(x, eligible));
        Assert.Equal(eligible.Length, EliteRules.Choose(99, eligible, 10).Count);
        Assert.Empty(EliteRules.Choose(99, Array.Empty<int>(), 2));
    }

    [Fact] public void Sanitize_DropsUnknown_Dupes_Conflicts_AndTrims()
    {
        Assert.Equal(new[] { EliteAffix.Shrouded, EliteAffix.Swift }, EliteRules.Sanitize(new[] { "shrouded", "Bogus", "Hexed", "Swift", "swift" }, 14));
        Assert.Equal(new[] { EliteAffix.Swift }, EliteRules.Sanitize(new[] { "Swift", "Hulking" }, 3)); // one affix on floor 3
    }

    [Fact] public void Complete_KeepsThePlannersChoices_AndFillsTheRest()
    {
        var kept = EliteRules.Complete(new[] { EliteAffix.Warded }, "Mother Hask", 5, 9, 2);
        Assert.Equal("Mother Hask", kept.Name); Assert.Equal(new[] { EliteAffix.Warded }, kept.Affixes);
        var filled = EliteRules.Complete(null, "  ", 5, 9, 2);
        Assert.Equal(EliteRules.Roll(5, 9, 2), filled);
        var named = EliteRules.Complete(null, "Grukk", 5, 9, 2);
        Assert.Equal("Grukk", named.Name); Assert.Equal(EliteRules.Roll(5, 9, 2).Affixes, named.Affixes);
    }

    [Fact] public void Apply_ChangesOnlyWhatTheAffixesSay()
    {
        var p = new EnemyProfile { Name = "Ghoul", MaxHp = 100, MinDamage = 10, MaxDamage = 20, MoveSpeed = 3f, WindupSeconds = 0.5f, Scale = 1f, Armor = 5 };
        var roll = new EliteRoll(new[] { EliteAffix.Swift, EliteAffix.Hulking, EliteAffix.Juggernaut }, "Grukk, Swift, Hulking and Juggernaut");
        var e = EliteRules.Apply(p, roll);
        Assert.Equal(3f * 1.35f, e.MoveSpeed, 3); Assert.Equal(0.4f, e.WindupSeconds, 3);
        Assert.Equal(220f, e.MaxHp, 3); Assert.Equal(1.3f, e.Scale, 3); Assert.Equal(12.5f, e.MinDamage, 3); Assert.Equal(25f, e.MaxDamage, 3);
        Assert.Equal(25f, e.Armor, 3); Assert.Equal(roll.Name, e.EliteName); Assert.Equal(roll.Affixes, e.Affixes);
        var warded = EliteRules.Apply(p, new EliteRoll(new[] { EliteAffix.Warded }, "x"));
        Assert.Equal(p.MaxHp, warded.MaxHp); Assert.Equal(p.MoveSpeed, warded.MoveSpeed); Assert.Equal(p.Armor, warded.Armor);
    }

    [Fact] public void ApplyAttack_CursingAddsVulnerable()
    {
        var a = new AttackProfile { MinDamage = 5, MaxDamage = 9 };
        var cursed = EliteRules.ApplyAttack(a, new[] { EliteAffix.Cursing });
        var v = Assert.Single(cursed.OnHit); Assert.Equal(StatusKind.Vulnerable, v.Kind); Assert.Equal(6f, v.Duration); Assert.Equal(1f, v.Chance);
        Assert.Empty(EliteRules.ApplyAttack(a, new[] { EliteAffix.Swift }).OnHit);
    }

    [Fact] public void Describe_ListsAffixesInEnglish()
    {
        Assert.Equal("Swift", EliteRules.Describe(new[] { EliteAffix.Swift }));
        Assert.Equal("Warded and Vampiric", EliteRules.Describe(new[] { EliteAffix.Warded, EliteAffix.Vampiric }));
        Assert.Equal("Swift, Warded and Vampiric", EliteRules.Describe(new[] { EliteAffix.Swift, EliteAffix.Warded, EliteAffix.Vampiric }));
    }

    [Fact] public void HordeBruteFor_FindsTheTiersBrute()
    {
        var roster = new Dictionary<string, EnemyProfile>
        {
            ["enemy.horde_husk"] = new() { Id = "enemy.horde_husk", Faction = "horde", TierIndex = 0 },
            ["enemy.horde_husk_brute"] = new() { Id = "enemy.horde_husk_brute", Faction = "horde", TierIndex = 0 },
            ["enemy.horde_shade_brute"] = new() { Id = "enemy.horde_shade_brute", Faction = "horde", TierIndex = 3 },
        };
        Assert.Equal("enemy.horde_husk_brute", EliteRules.HordeBruteFor(roster, 0));
        Assert.Equal("enemy.horde_shade_brute", EliteRules.HordeBruteFor(roster, 3));
        Assert.Null(EliteRules.HordeBruteFor(roster, 1));
    }
}

// tests/Enemies/EliteNamesTests.cs
public class EliteNamesTests
{
    [Fact] public void Make_IsDeterministic_ForASeed() => Assert.Equal(EliteNames.Make(new Random(5), new[] { EliteAffix.Swift }), EliteNames.Make(new Random(5), new[] { EliteAffix.Swift }));
    [Fact] public void Title_UsesTheForOneAffix_AndACommaForMore()
    {
        Assert.Equal("Grukk the Swift", EliteNames.Title("Grukk", new[] { EliteAffix.Swift }));
        Assert.Equal("Mother Hask, Warded and Vampiric", EliteNames.Title("Mother Hask", new[] { EliteAffix.Warded, EliteAffix.Vampiric }));
    }
    [Fact] public void Make_NamesAreShortAndVaried()
    {
        var names = Enumerable.Range(0, 200).Select(i => EliteNames.Make(new Random(i), new[] { EliteAffix.Hexed })).ToList();
        Assert.All(names, n => { var baseName = n[..n.IndexOf(" the ")]; Assert.InRange(baseName.Length, 3, 24); Assert.DoesNotContain("  ", n); });
        Assert.True(names.Distinct().Count() > 120);
        Assert.Contains(names, n => n.Split(' ').Length >= 4); // some carry an honorific
    }
    [Fact] public void Make_BanditsGetHumanNames() => Assert.NotEqual(EliteNames.Make(new Random(3), new[] { EliteAffix.Swift }), EliteNames.Make(new Random(3), new[] { EliteAffix.Swift }, "bandits"));
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "EliteRulesTests|EliteNamesTests"` → build errors.
- [ ] **Step 3: Implement `EliteRules` and `EliteNames`** as specified in Interfaces.
- [ ] **Step 4: Run to see them pass** — same command → PASS.
- [ ] **Step 5: Commit** — `git add src/Enemies/Core/Elites tests/Enemies && git commit -m "feat(elites): EliteRules and EliteNames, the schedule, the roll and the syllable table"`

### Task 3: The roll in the resolver, the planner's `Elite` field, the validator's fill and trim

**Files:**
- Modify: `src/Adventure/Core/Plans/FloorPlan.cs:32-36` (`MemberPlan`), `:87-91` (`LonerPlan`), `src/Adventure/Core/Plans/FloorContents.cs:6` (`EnemyPlacement`), `src/Adventure/Core/Validate/PlanResolver.cs:15,33-60` (signature, elite stamping), `src/Adventure/Core/Validate/PlanValidator.cs:28,76-100,147,205-245` (sanitising), `src/Adventure/Core/Design/LegacyPopulator.cs:17-33`, `src/Adventure/Core/Design/AdventurePlanning.cs:23-30` (pass-through), `src/Adventure/Core/Ai/PromptBuilder.cs:37` (one sentence, writing's file)
- Test: `tests/Adventure/EliteResolveTests.cs`, `tests/Adventure/PlanValidatorTests.cs` (add)

**Interfaces:**
- Consumes: `EliteRules.Choose/Roll/Complete/Sanitize/CountFor`, `EnemyProfile.Faction/IsBoss`.
- Produces:
  - `public sealed class ElitePlan { public string Name { get; set; } = ""; public List<string> Affixes { get; set; } = new(); }` with `[AiDescription("A named champion: Name (at most 40 characters) and one to three affixes from Swift, Hulking, Warded, Vampiric, Volatile, Splitting, Thorned, Cursing, Shrouded, Hexed, Alarmist, Juggernaut; leave Name empty or Affixes empty for the game to fill them.")]`; `[AiNotNull] public ElitePlan? Elite { get; set; }` on `MemberPlan` (applies to the first creature of the entry) and on `LonerPlan`.
  - `EnemyPlacement(int SpawnIndex, string EnemyId, GridPos Cell, bool Under, string? CollectiveId = null, int Ordinal = 0, EliteRoll? Elite = null)`.
  - `PlanResolver.Resolve(layout, plan, tier, roster, floor, seed, flags = null, int extraElites = 0)`; `AdventurePlanning.ResolveStored(..., flags = null, int extraElites = 0)`; `LegacyPopulator.Populate(layout, tier, roster, floor, seed, int extraElites = 0)`.
  - `internal static void EliteRules.Assign(List<EnemyPlacement> enemies, IReadOnlyDictionary<string, EnemyProfile> roster, int floorSeed, int floor, int extra)` (in `EliteRules.cs`, public is fine): eligible = placements whose roster profile exists, `Faction != "horde"`, `!IsBoss`; placements that already carry `Elite` (from the plan) count first; `Choose(floorSeed, remainingEligibleIndices, CountFor(floor, extra) - preset)` picks the rest; each chosen placement is replaced by `p with { Elite = Roll(floorSeed, floor, p.SpawnIndex, roster[p.EnemyId].Faction) }`.
  - Resolver: a collective member entry with `Elite` stamps `EliteRules.Complete(Sanitize(e.Affixes, floor), e.Name, seed, floor, index, faction)` on its first placed creature (`k == 0`), a loner likewise; then `Assign` runs after the cellar spawns and before the boss block (`PlanResolver.cs:60`). `LegacyPopulator.Populate` calls `Assign` after its three spawn loops.
  - Validator (`PlanValidator.Validate`): for every `MemberPlan.Elite`/`LonerPlan.Elite`: `Affixes = Sanitize(...)` names back to strings, `Name = Clip(Name, 40)`; a horde member's `Elite` → null with repair "horde {id}: {enemy} cannot be an elite"; a bandit or boss id is fine; when the number of entries with `Elite` exceeds `EliteRules.CountFor(menu.Floor, 0)` the later ones (collectives in order then loners) lose it with repair "floor allows N elites, dropped the rest".
  - PromptBuilder: append to the Collectives paragraph (`PromptBuilder.cs:37`, the sentence after "Loners are single enemies with no group.") exactly: `A collective member entry or a loner may carry Elite {Name, Affixes}: a named champion with one to three affixes from Swift, Hulking, Warded, Vampiric, Volatile, Splitting, Thorned, Cursing, Shrouded, Hexed, Alarmist, Juggernaut (never Shrouded with Hexed); at most one elite on floors 2-7, two on 8-13, three from 14; the game names and rolls any you leave out, and horde members are never elites.`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Adventure/EliteResolveTests.cs
public class EliteResolveTests
{
    [Theory, InlineData(1), InlineData(2), InlineData(8), InlineData(14)]
    public void Resolve_StampsExactlyCountForElites_Deterministically(int floor)
    {
        var (layout, _, menu) = TestAdventure.Floor(floor, 31 + floor);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 31 + floor);
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        var a = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, floor, 31 + floor);
        var b = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, floor, 31 + floor);
        var elites = a.Enemies.Where(e => e.Elite != null).ToList();
        Assert.Equal(Math.Min(EliteRules.CountFor(floor, 0), a.Enemies.Count), elites.Count);
        Assert.Equal(a.Enemies, b.Enemies);
        foreach (var e in elites) Assert.Equal(EliteRules.Roll(31 + floor, floor, e.SpawnIndex, TestAdventure.Roster[e.EnemyId].Faction), e.Elite);
        Assert.All(elites, e => Assert.NotEqual("horde", TestAdventure.Roster[e.EnemyId].Faction));
        Assert.Equal(Enumerable.Range(0, a.EnemyCount), a.Enemies.Select(e => e.SpawnIndex)); // the roll never moves or renumbers a spawn
    }

    [Fact] public void Resolve_ExtraElites_AddToTheCount()
    {
        var (layout, _, menu) = TestAdventure.Floor(5, 9);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 9);
        var r = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 5, 9, extraElites: 2);
        Assert.Equal(Math.Min(3, r.Enemies.Count), r.Enemies.Count(e => e.Elite != null));
    }

    [Fact] public void Resolve_HonoursThePlannersElite()
    {
        var (layout, _, menu) = TestAdventure.Floor(9, 4);
        var plan = OfflineDesigner.PlanFloor(menu, layout, 4);
        var member = plan.Collectives[0].Members[0];
        member.Elite = new ElitePlan { Name = "Mother Hask", Affixes = new() { "Warded", "Vampiric" } };
        Assert.True(PlanValidator.Validate(layout, plan, menu).Ok);
        var r = PlanResolver.Resolve(layout, plan, TestAdventure.Crypt, TestAdventure.Roster, 9, 4);
        var first = r.Enemies.First(e => e.CollectiveId == plan.Collectives[0].Id && e.Ordinal == 0);
        Assert.Equal("Mother Hask", first.Elite!.Name);
        Assert.Equal(new[] { EliteAffix.Warded, EliteAffix.Vampiric }, first.Elite.Affixes);
        Assert.Equal(2, r.Enemies.Count(e => e.Elite != null)); // floor 9 allows two: the planner's plus one rolled
    }

    [Fact] public void Legacy_RollsElitesToo()
    {
        var layout = FloorGenerator.Generate(TestAdventure.SpecFor(8), 21);
        var c = LegacyPopulator.Populate(layout, TestAdventure.Crypt, TestAdventure.Roster, 8, 21);
        Assert.Equal(Math.Min(2, c.Enemies.Count), c.Enemies.Count(e => e.Elite != null));
    }
}

// tests/Adventure/PlanValidatorTests.cs (add)
[Fact] public void Validate_StripsHordeElites_AndTrimsToTheFloorsCount()
{
    var (layout, _, menu) = TestAdventure.Floor(5, 12);
    var plan = OfflineDesigner.PlanFloor(menu, layout, 12);
    foreach (var c in plan.Collectives) foreach (var m in c.Members) m.Elite = new ElitePlan { Affixes = new() { "Swift", "Nonsense", "Hexed", "Shrouded" } };
    foreach (var h in plan.Hordes) foreach (var m in h.Members) m.Elite = new ElitePlan { Name = "No" };
    var report = PlanValidator.Validate(layout, plan, menu);
    Assert.True(report.Ok);
    Assert.All(plan.Hordes.SelectMany(h => h.Members), m => Assert.Null(m.Elite));
    var kept = plan.Collectives.SelectMany(c => c.Members).Where(m => m.Elite != null).ToList();
    Assert.Equal(1, kept.Count); // floor 5 allows one
    Assert.Equal(new List<string> { "Swift" }, kept[0].Elite!.Affixes); // one affix on floor 5, unknown dropped, the conflict never arises
    Assert.Contains(report.Repairs, r => r.Contains("elite"));
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "EliteResolveTests|PlanValidatorTests"` → build errors (`Elite`, `ElitePlan`, `extraElites`).
- [ ] **Step 3: Implement** as specified. Existing `PlanResolverTests.Deterministic_Contiguous_And_InRoom` compares `a.Enemies` to `b.Enemies` by record equality: `EliteRoll` is a record with a list, so give `EliteRoll` value equality over its affixes (implement `Equals`/`GetHashCode` with `SequenceEqual`) so the existing assertion keeps passing.
- [ ] **Step 4: Run** — `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~Adventure"` → PASS (schema tests in `AiPlannerTests` that pin the floor schema's property set may need `Elite` added to their expectation; say so in the commit).
- [ ] **Step 5: Commit** — `git add src/Adventure tests/Adventure && git commit -m "feat(elites): the resolver rolls the floor's elites from the floor seed and spawn index; the planner may name one"`

### Task 4: The spawn site, the save, the reward and the two events

**Files:**
- Modify: `src/Dungeon/DungeonBuilder.cs:234-293` (fetch `floorState` before `PlaceEnemies`), `:1148-1164` (`PlaceEnemies`), `:1176-1197` (`SpawnEnemy`), `src/Enemies/Enemy.cs:355-363` (`ApplyNetState`), `:473-481` (`ChangeState`), `:848-882` (`OnDeath`), `src/Save/Core/SaveDtos.cs` (`FloorStateDto`, after `Gathered`), `src/Save/FloorStateCapture.cs:26-29` (capture), `src/Save/Core/SaveFormat.cs:9` (version, see Global Constraints), `src/Adventure/AdventureDirector.cs:718-722` (ledger)
- Create: `data/loot_tables/elite.tres`
- Test: `tests/Save/EliteSaveTests.cs`

**Interfaces:**
- Consumes: `EnemyPlacement.Elite`, `EliteRules.Apply/Roll/XpMultiplier`, `CoinRules.ChestCoins`, `LootRoller.RollMany`, `EventBus.RaiseEliteSeen/RaiseEliteSlain`, `FloorRecord.Elites`.
- Produces:
  - `DungeonBuilder.SpawnEnemy(string id, Vector3 position, int floor, int spawnIndex = -1, Collective? group = null, int ordinal = 0, int netId = -1, EliteRoll? elite = null)`; `private static EnemyProfile ApplyElite(EnemyProfile p, EliteRoll? e) => e == null ? p : EliteRules.Apply(p, e);` applied right after the villain-body line (`:1180`).
  - `public static int DungeonBuilder.ExtraElitesPerFloor { get; set; }` (fortune's *Marked* and run-structure's *Hostile Tenants* add to it; passed as `extraElites` to `ResolveStored` and `Populate`).
  - `PlaceEnemies(FloorContents contents, int floor, Save.FloorStateDto? state)`: when `state?.EliteSpawns is { Count: > 0 }` (a revisited floor, a loaded save, or a client's `Welcome`/`FloorChange` state), membership comes from the save: `elite = state.EliteSpawns.Contains(e.SpawnIndex) ? e.Elite ?? EliteRules.Roll(seed, floor, e.SpawnIndex, faction) : null`; otherwise `e.Elite`. `floorState` is read at the top of `Build` (move line `:273` up above `PlaceEnemies` at `:266`).
  - `FloorStateDto.EliteSpawns` (`List<int>`, default `new()`), captured in `FloorStateCapture.Capture` as the spawn indices of every `Enemy { IsElite: true, SpawnIndex: >= 0 }` alive or dead (dead ones are in `DeadEnemies` too; keeping them in `EliteSpawns` costs nothing and keeps the set complete).
  - `Enemy`: `private bool _eliteSeen;` raised once: server/solo in `ChangeState` when `wasUnaware && next is ChaseState && IsElite`; client in `ApplyNetState` when `IsElite && (s.Flags & EntityState.FlagAware) != 0`. `OnDeath`: XP = `(int)MathF.Round(Profile.ScaledXp(Floor) * (IsElite ? EliteRules.XpMultiplier : 1f))`; after `RaiseEnemyDied` → `if (IsElite) EventBus.Instance.RaiseEliteSlain(this);`; loot: an elite always drops coins `CoinRules.ChestCoins(Floor, _rng)` and rolls `table.elite` (the `tableId` expression becomes `IsBoss ? "table.chest" : IsElite ? "table.elite" : "table.enemy"`), the `IsReinforcement` early return does not apply to an elite.
  - `data/loot_tables/elite.tres`: `Id = "table.elite"`, Entries as `table.chest` minus the tome, `CommonWeight = 0, MagicWeight = 38, RareWeight = 50, LegendaryWeight = 12, Rolls = 1, DropChance = 1.0`.
  - `AdventureDirector.OnEnemyDied`: one line after `Kills++`: `if (_record != null && e.Floor == _currentFloor && e.IsElite) _record.Elites++;`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Save/EliteSaveTests.cs
public class EliteSaveTests
{
    [Fact] public void FloorStateDto_EliteSpawns_DefaultsEmpty_AndRoundTrips()
    {
        var dto = new FloorStateDto();
        Assert.Empty(dto.EliteSpawns);
        dto.EliteSpawns.AddRange(new[] { 3, 7 });
        var json = System.Text.Json.JsonSerializer.Serialize(dto, SaveFormat.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<FloorStateDto>(json, SaveFormat.Options)!;
        Assert.Equal(new List<int> { 3, 7 }, back.EliteSpawns);
    }

    [Fact] public void OldFloorState_WithoutTheField_Loads()
    {
        var back = System.Text.Json.JsonSerializer.Deserialize<FloorStateDto>("{\"Visited\":true,\"Gathered\":1}", SaveFormat.Options)!;
        Assert.True(back.Visited); Assert.Empty(back.EliteSpawns);
    }
}
```

- [ ] **Step 2: Run to see it fail** — `cd tests && dotnet test --nologo -v q --filter EliteSaveTests` → build error.
- [ ] **Step 3: Implement** everything in Interfaces. `Capture_WritesEliteSpawns` is pinned by the headless flow (Task 13 step "save/load keeps the elites"), since capture needs live nodes.
- [ ] **Step 4: Run** — tests PASS; `dotnet build ... --no-incremental` → 0 errors; smoke: `timeout 60 godot --headless --path . -- --seed=8 --floor=8 --quit-after=6 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "DungeonBuilder\] floor 8|ERROR"` → the floor builds, no errors. Add to the `[DungeonBuilder] floor N` print an `elites=[name; name]` segment listing each elite's `DisplayName` so the log shows them.
- [ ] **Step 5: Commit** — `git add src/Dungeon/DungeonBuilder.cs src/Enemies/Enemy.cs src/Save data/loot_tables/elite.tres src/Adventure/AdventureDirector.cs tests/Save && git commit -m "feat(elites): elites spawn from the roll, drop from table.elite with x2.5 XP, raise EliteSeen/EliteSlain, and the save keeps the floor's roster"`

### Task 5: Co-op: dynamic elites reach clients

**Files:**
- Modify: `src/Net/Core/Messages.cs:27` (`DynamicEnemyDto`), `src/Net/Core/Protocol.cs:6` (version), `src/Net/ServerWorld.cs:215,562` (the two constructions), `src/Net/ClientWorld.cs:161-170` (`OnEnemySpawned`)
- Test: `tests/Net/NetCodecTests.cs:229-232` (version pin), `tests/Net/EliteDtoTests.cs`

**Interfaces:**
- Produces: `public sealed record DynamicEnemyDto(int NetId, string DefId, float X, float Y, float Z, float Yaw, float Hp, string? HordeId = null, string? EliteName = null, string? EliteAffixes = null);` where `EliteAffixes` is the comma-joined enum names (`EliteRules.Encode(IReadOnlyList<EliteAffix>) / Decode(string?)` helpers in `EliteRules.cs`). Server fills both from `e.Profile` when `e.IsElite`. Client: `SpawnEnemy(d.DefId, pos, floor, netId: d.NetId, elite: d.EliteName != null ? new EliteRoll(EliteRules.Decode(d.EliteAffixes), d.EliteName) : null)`. `Protocol.Version` +1; the planned elites need no message (both peers resolve the same plan and read the same `FloorStateDto.EliteSpawns` from `Welcome`/`FloorChange`; verify in `ClientWorld` that `Game.Instance.Floors[floor]` is set from the message before `Builder.Build` runs; if it is set after, move the assignment before the build and note it).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Net/EliteDtoTests.cs
public class EliteDtoTests
{
    [Fact] public void DynamicEnemyDto_WithElite_RoundTrips()
    {
        var dto = new DynamicEnemyDto(200001, "enemy.ghoul", 1, 2, 3, 0.5f, 40, null, "Grukk the Swift", "Swift,Warded");
        var msg = NetCodec.Decode(NetCodec.Encode(new EnemySpawned(dto))) as EnemySpawned;
        Assert.Equal(dto, msg!.Enemy);
        Assert.Equal(new[] { EliteAffix.Swift, EliteAffix.Warded }, EliteRules.Decode(msg.Enemy.EliteAffixes));
        Assert.Equal("Swift,Warded", EliteRules.Encode(new[] { EliteAffix.Swift, EliteAffix.Warded }));
        Assert.Empty(EliteRules.Decode(null));
    }
    [Fact] public void DynamicEnemyDto_WithoutElite_StillDecodes() => Assert.Null((NetCodec.Decode(NetCodec.Encode(new EnemySpawned(new DynamicEnemyDto(1, "x", 0, 0, 0, 0, 1)))) as EnemySpawned)!.Enemy.EliteName);
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "EliteDtoTests|NetCodecTests"` → build error on the new fields.
- [ ] **Step 3: Implement**; bump `Protocol.Version` and the pin at `NetCodecTests.cs:232`.
- [ ] **Step 4: Run** — same filter → PASS; `dotnet build` → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Net tests/Net && git commit -m "feat(elites): a dynamic elite's name and affixes ride EnemySpawned (protocol bump)"`

### Task 6: Affix behaviours I: stats, ward, vampirism, curse, death effects, alarm

**Files:**
- Modify: `src/Enemies/Enemy.cs` (`_Ready` `:180-212`, `_PhysicsProcess` `:420-429`, `ChangeState` `:480`, `StrikeParty` `:653-658`, `PerformSignature` Slam/Charge `:709-724`, `ReceiveAttack` `:743-763`, `ApplyStatus` `:777-781`, `ReceiveHazard` `:833`, `WakeNearby` `:795-802`, `OnDeath` `:848-882`), `src/Party/Adventurer.cs:896-937` (return type only; combat-feel's file), `src/Combat/Projectile.cs:129` (one line), `src/Audio/Synth.cs` (`Horn()`)
- Test: none engine-free beyond Task 2; the headless flow (Task 13) pins Warded, Volatile and the hazard kill.

**Interfaces:**
- Consumes: `EliteRules` constants, `StatusEffectController.ShieldPoints/Apply/Remove/Has`, `Party.AdventurersWithin`, `DungeonBuilder.SpawnEnemy`, `EliteRules.HordeBruteFor(Builder.Roster, TierRules.TierIndex(Floor))`, `TierRules.TierIndex`.
- Produces:
  - `Adventurer.ReceiveAttack(...)` returns `DamageResult` (`DamageResult.Miss(attack.Type)` on the Downed early exit and the miss branch, `r` otherwise); no other change in Adventurer.cs.
  - `Enemy.OnDealtDamage(DamageResult r)`: Vampiric → `Hp = Math.Min(MaxHp, Hp + r.Amount * EliteRules.VampiricFraction)`, `_vampFlash = 0.3f` (Task 8 draws it). Called from `StrikeParty`, the Slam and Charge branches of `PerformSignature`, and `Projectile.OnBodyEntered` (`:129` becomes `var r = adv.ReceiveAttack(...); Shooter.OnDealtDamage(r);`).
  - `_Ready`: after `Attack = Profile.BuildAttack(Floor)` → `Attack = EliteRules.ApplyAttack(Attack, Profile.Affixes);` then `if (Profile.Has(EliteAffix.Warded)) ApplyWard();` where `ApplyWard()` applies `new StatusApplication(StatusKind.Shield, 3600f, Math.Min(MaxHp * EliteRules.WardFraction, StatusEffectController.MaxShield))` and `_wardUp = true`. `public float WardPoints => Effects.ShieldPoints;`
  - `_PhysicsProcess` (alive, server): `if (Profile.Has(Warded)) { if (_wardUp && Effects.ShieldPoints <= 0f) { _wardUp = false; _wardTimer = EliteRules.WardRegrowSeconds; Log($"{DisplayName}'s ward shatters."); } else if (!_wardUp && (_wardTimer -= dt) <= 0f) ApplyWard(); }`.
  - `ReceiveAttack`: after `Resolve`: `if (Profile.Has(Juggernaut)) Effects.Remove(StatusKind.Stun);` `ApplyStatus`: `if (status.Kind == StatusKind.Stun && Profile.Has(Juggernaut)) return;` `ReceiveHazard`: skip the stun line for Juggernaut. If combat-feel's `Enemy.Stagger(...)` exists: first line `if (Profile.Has(EliteAffix.Juggernaut)) return;`.
  - `ChangeState` wake line (`:480`): replace `WakeNearby()` with `WakeNearby(Profile.Has(EliteAffix.Alarmist) ? EliteRules.AlarmRadius : NoiseRadius)`; when Alarmist: `Log($"{DisplayName} sounds the alarm!")`, `Audio.Synth.PlayAt(GetParent(), Audio.Synth.Horn(), GlobalPosition, 0f, 40f)`. `WakeNearby(float radius)` takes the radius (the parameterless call sites keep `NoiseRadius`). `Synth.Horn()`: a 0.9 s two-note brass swell (square-ish wave 220→330 Hz with a 60 ms attack), same style as `Rumble()`/`Thud()`.
  - `OnDeath` (server, before the `_environmentalKill` return so a hazard kill still fires them): Volatile → `foreach (var v in Party.AdventurersWithin(GlobalPosition, EliteRules.VolatileRadius)) v.ReceiveAttack(Stats, Attack with { MinDamage = Attack.MinDamage * 0.6f, MaxDamage = Attack.MaxDamage * 0.6f, Type = DamageType.Fire, OnHit = new[] { new StatusApplication(StatusKind.Burn, EliteRules.VolatileBurnSeconds, Math.Max(1f, Attack.MaxDamage * 0.15f)) } }, $"{DisplayName}'s death")`, `HitFx.SpellBurst(GetParent(), pos, orange)`, `Tremor.ShakeOnly(0.05f, 0.4f)`, log "{name} bursts!"; Splitting → `var brute = EliteRules.HordeBruteFor(builder.Roster, TierRules.TierIndex(Floor)); for i in 0..SplitCount: builder.SpawnEnemy(brute, GlobalPosition + (i == 0 ? Left : Right) * 0.8f, Floor) is { } b → b.SetSwarmDetail(); b.ForceAggro();` log "{name} splits!". Both guarded by `!Net.NetSession.IsClient`.

- [ ] **Step 1: Implement** as specified (Godot side; the flow in Task 13 is the proof: Warded absorbs, Volatile explodes and burns, a Press kill still detonates).
- [ ] **Step 2: Build** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors; `cd tests && dotnet test --nologo -v q` → all PASS (nothing engine-free changed).
- [ ] **Step 3: Commit** — `git add src/Enemies/Enemy.cs src/Party/Adventurer.cs src/Combat/Projectile.cs src/Audio/Synth.cs && git commit -m "feat(elites): Swift, Hulking, Juggernaut, Warded, Vampiric, Cursing, Volatile, Splitting and Alarmist act"`

### Task 7: Affix behaviours II: Thorned, Hexed and the Shrouded dome

**Files:**
- Create: `src/Enemies/Core/Elites/ShroudRules.cs`
- Modify: `src/Enemies/Enemy.cs` (`ReceiveAttack`, new `OnStruckBy`, static shroud registry), `src/Party/PartyController.cs:520-523` (one line), `src/Party/Adventurer.cs:878-893` (`Nova`, one line), `src/Combat/Projectile.cs:84-97` (`_PhysicsProcess`)
- Test: `tests/Enemies/ShroudRulesTests.cs`

**Interfaces:**
- Consumes: `CharacterState.Effects/Recalculate`, `Party.Adventurers`, `Trap.TrapStats`.
- Produces:
  - `public static class ShroudRules { const float Radius = EliteRules.ShroudRadius; static bool Inside(float px, float pz, float cx, float cz, float r); static bool SegmentEntersDisc(float ax, float az, float bx, float bz, float cx, float cz, float r); }` — true only when A is outside the disc and the segment AB reaches a point inside it (B inside, or the closest point of the segment to C is within r). A starting inside is never an "entry".
  - `Enemy`: `public static readonly List<Enemy> Shrouds = new();` (added in `_Ready` when `Profile.Has(Shrouded)`, removed in `_ExitTree`); `public static bool ShroudBlocks(Vector3 from, Vector3 to, Enemy? shooter)` → any alive shroud `s != shooter` with `ShroudRules.SegmentEntersDisc(from.X, from.Z, to.X, to.Z, s.GlobalPosition.X, s.GlobalPosition.Z, Radius)`; `public static bool ShroudSeparates(Vector3 caster, Vector3 target)` → any alive shroud where `!Inside(caster) && Inside(target)`.
  - `Projectile._PhysicsProcess`: before `Sweep`: `if (!Cosmetic && Enemy.ShroudBlocks(from, to, Shooter)) { _spent = true; Fx.HitFx.SpellBurst(GetParent(), to, Color); if (IsArrow && Party != null) Party.SpawnArrowPickup(from, 0.4f); QueueFree(); return; }` (a cosmetic client copy stops where the server's `ProjectileEnded` says, as today).
  - `Adventurer.Nova` (`:883`): add `if (Enemy.ShroudSeparates(GlobalPosition, e.GlobalPosition)) continue;` as the first line inside the loop after the distance check. This is the one line this plan adds to combat-feel's file beyond Task 6's return type.
  - `Enemy`: `private AttackKind _lastHitKind;` set in `ReceiveAttack` from `attack.Kind` before `Resolve`; `public void OnStruckBy(CharacterState striker, DamageResult r)`: returns unless `r.Hit && _lastHitKind == AttackKind.Melee && !Net.NetSession.IsClient`; Thorned → find `var adv = Party.Adventurers.FirstOrDefault(a => a.State == striker)`; `adv?.ReceiveAttack(Dungeon.Props.Trap.TrapStats, new AttackProfile { MinDamage = r.Amount * EliteRules.ThornedFraction, MaxDamage = r.Amount * EliteRules.ThornedFraction }, $"{DisplayName}'s spines")`; Hexed → `striker.Effects.Apply(new StatusApplication(StatusKind.Slow, EliteRules.HexSlowSeconds, 0f)); striker.Recalculate(); Log($"{striker.Name} is hexed: slowed.")`.
  - `PartyController.OnHitLanded` (`:522`, right after the `if (!r.Hit) return;`): `if (target is Enemies.Enemy { IsElite: true } elite) elite.OnStruckBy(m, r);` — the only line this plan adds to PartyController.cs.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ShroudRulesTests
{
    [Fact] public void SegmentEntersDisc_FromOutsideCrossingIn() => Assert.True(ShroudRules.SegmentEntersDisc(-10, 0, 0, 0, 0, 0, 4));
    [Fact] public void SegmentEntersDisc_FromOutsideStoppingShort() => Assert.False(ShroudRules.SegmentEntersDisc(-10, 0, -5, 0, 0, 0, 4));
    [Fact] public void SegmentEntersDisc_PassingBesideTheDome() => Assert.False(ShroudRules.SegmentEntersDisc(-10, 5, 10, 5, 0, 0, 4));
    [Fact] public void SegmentEntersDisc_GrazingInsideTheEdge() => Assert.True(ShroudRules.SegmentEntersDisc(-10, 3.5f, 10, 3.5f, 0, 0, 4));
    [Fact] public void SegmentEntersDisc_FromInsideIsNotAnEntry() { Assert.False(ShroudRules.SegmentEntersDisc(1, 0, 10, 0, 0, 0, 4)); Assert.False(ShroudRules.SegmentEntersDisc(1, 0, 2, 0, 0, 0, 4)); }
    [Fact] public void Inside_IsStrictRadius() { Assert.True(ShroudRules.Inside(3.9f, 0, 0, 0, 4)); Assert.False(ShroudRules.Inside(4.1f, 0, 0, 0, 4)); }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter ShroudRulesTests` → build error.
- [ ] **Step 3: Implement** `ShroudRules`, then the Godot pieces in Interfaces.
- [ ] **Step 4: Run** — tests PASS; `dotnet build` → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Enemies src/Party/PartyController.cs src/Party/Adventurer.cs src/Combat/Projectile.cs tests/Enemies/ShroudRulesTests.cs && git commit -m "feat(elites): Thorned and Hexed answer melee hits; the Shrouded dome fizzles bolts and novas at its edge"`

### Task 8: Tells: EliteVisuals

**Files:**
- Create: `src/Enemies/Elites/EliteVisuals.cs` (Godot, namespace `DungeonCrawler.Enemies`)
- Modify: `src/Enemies/Enemy.cs:193` (`_baseColor`), `:440-461` (`UpdateTint`), `_Ready` end, `_PhysicsProcess`/`ClientTick` (one `EliteVisuals.Tick(this, dt)` call each)

**Interfaces:**
- Consumes: `Enemy.Model`, `Enemy.WardPoints`, `Profile.Scale/Affixes`, `Fire.Puff()` (particle texture), `Dungeon/Props/Fire.cs:104` iron material values.
- Produces: `public static class EliteVisuals { static Color Tint(Color baseColor, IReadOnlyList<EliteAffix> affixes); static void Dress(Enemy e); static void Tick(Enemy e, float dt); }` with, per affix: Swift → `Tint` lerps 40% toward `(1, 0.9, 0.3)` and `Dress` adds a `GpuParticles3D` of dust at the feet (Amount 14, Lifetime 0.9, emitting while `Velocity` planar > 0.5, using the `Fire.Puff()` texture like `HordeSpawner.Show`); Hulking → nothing (the scale is the tell); Warded → a `SphereMesh` shell (radius `1.15 × Scale`, 0.9×Scale high), unshaded, `(0.4, 0.6, 1, 0.22)`, `Visible = e.WardPoints > 0` each tick; Vampiric → `Tick` drives a red emission (`(1, 0.1, 0.1) × _vampFlash`) on the body materials while `e.VampFlash > 0` (`public float VampFlash` exposed by Enemy); Volatile → emission `(1, 0.5, 0.1) × (0.5 + 0.5 sin(6t))`; Splitting → a thin emissive green box `0.05 × 1.2×Scale × 0.05` down the chest at `z = -0.42×Scale`; Thorned → eight `CylinderMesh { TopRadius = 0, BottomRadius = 0.05, Height = 0.45 }` spines around the torso at height `1.1×Scale`, pointing outward, in an iron `StandardMaterial3D { AlbedoColor = (0.16, 0.15, 0.14), Metallic = 0.7, Roughness = 0.55 }`; Cursing → a `GpuParticles3D` trail behind (purple `(0.6, 0.2, 0.9)`, Amount 20, Lifetime 1.2, gravity 0); Shrouded → a `SphereMesh` dome of radius `EliteRules.ShroudRadius`, unshaded, `(0.7, 0.7, 0.9, 0.12)`, cull disabled, no shadow, parented to the enemy; Hexed → two `TorusMesh { InnerRadius = 0.55×Scale, OuterRadius = 0.6×Scale }` rings at heights 0.6 and 1.3 (×Scale), violet emissive, each `Tick` rotates them ±1.5 rad/s; Alarmist → nothing (the horn is the tell); Juggernaut → `Tint` lerps 60% toward iron grey `(0.45, 0.45, 0.48)`. `Enemy._Ready`: `_baseColor = EliteVisuals.Tint(_material.AlbedoColor, Profile.Affixes); _material.AlbedoColor = _baseColor;` then `if (IsElite) EliteVisuals.Dress(this);` after `LoadModel()`. The horde-detail path (`SetSwarmDetail`) is never an elite, so no interaction.

- [ ] **Step 1: Implement** as specified. Keep every tell cheap: shared materials per affix (static fields), no shadows on tell meshes, no lights.
- [ ] **Step 2: Build and look** — `dotnet build` → 0 errors; `timeout 60 godot --path . -- --seed=8 --floor=8 --face-enemy --screenshot=/tmp/elite.png --after=3 --no-ambush --no-portals --no-gathering` (a window is needed for the screenshot; headless: `--quit-after=5` and check the log prints `elites=[...]` with no `ERROR`).
- [ ] **Step 3: Commit** — `git add src/Enemies && git commit -m "feat(elites): every affix has a tell: tints, dust, shell, spines, rings, trail, dome"`

### Task 9: Boss phases: the roar, the second signature, phase two

**Files:**
- Create: `src/Enemies/Core/Elites/BossRules.cs`
- Modify: `src/Enemies/Defs/EnemyDef.cs:55-74` (`SecondSignature`, `ArenaChange` exports + `ToProfile`), `src/Enemies/Core/EnemyProfile.cs` (`SecondSignature`, `ArenaChange`), `src/Enemies/Ai/AiState.cs:124-166` (`SignatureState`, new `RoarState`), `src/Enemies/Enemy.cs:92` (`EffectiveMoveSpeed`), `:366-372` (`ClientHurt`), `:683-737` (`TrySignature`, `AnnounceSignature(Signature)`, `PerformSignature(Signature)`), `:743-763` (`ReceiveAttack`: untargetable, BossHalf), `:804-810` (`RestoreFrom`), `data/enemies/crypt_lord.tres`, `broodmother.tres`, `iron_colossus.tres`, `abyss_herald.tres`
- Test: `tests/Enemies/BossRulesTests.cs`

**Interfaces:**
- Consumes: `EventBus.RaiseBossHalf`, `BossArena.Change` (Task 10; stub a no-op `BossArena.Change(Enemy)` here so the build stays green, Task 10 fills it).
- Produces:
  - `public enum ArenaChange { None, Darken, Webs, Embers, GravityWell }` (in `BossRules.cs`); `EnemyProfile.SecondSignature` (`Signature`, default None), `EnemyProfile.ArenaChange` (default None); `EnemyDef.SecondSignature` (`string`, "None"), `EnemyDef.ArenaChange` (`string`, "None"), parsed in `ToProfile` like `Signature`.
  - `public static class BossRules { const float RoarSeconds = 1.2f, PhaseTwoSpeed = 1.2f, HalfFraction = 0.5f; const int BruteSummonFromFloor = 8, SlowPatchCount = 3, EmberPatchCount = 2; const float EmberSpeed = 0.5f, EmberOrbit = 3f, GravityWellRadius = 6f, GravityWellTick = 0.5f, GravityWellSlowSeconds = 1f, SlowPatchRadius = 1.2f, SlowPatchSeconds = 2f; static string SummonFor(string summonId, int floor)` → `floor >= 8 && summonId.StartsWith("enemy.horde_") && !summonId.EndsWith("_brute") ? summonId + "_brute" : summonId`; `static Signature NextSignature(Signature first, Signature second, int phase, ref bool useSecond)` → phase 1 or `second == None`: `first`; else alternate, starting with `second`; `static Signature RandomSecond(Signature first, Random rng)` → one of Slam/Volley/Charge/Summon ≠ first; `static string HeraldName(int floor)` → "the First Herald" for 25, "the Second Herald" for 30 … ordinal words to Twelfth then "the Nth Herald"; `static bool PhaseTwoAt(float hp, float maxHp) => hp > 0 && hp <= maxHp * HalfFraction`. }`
  - Data: crypt_lord `SecondSignature = "Summon"`, `SummonId = "enemy.horde_husk"`, `ArenaChange = "Darken"`; broodmother `SecondSignature = "Summon"`, `SummonId = "enemy.cave_spider"`, `ArenaChange = "Webs"`; iron_colossus `SecondSignature = "Slam"`, `ArenaChange = "Embers"`; abyss_herald `SecondSignature = "Volley"`, `ArenaChange = "GravityWell"`.
  - `Enemy`: `public int Phase { get; private set; } = 1; public bool Untargetable { get; private set; }`; `EffectiveMoveSpeed` multiplies by `(Phase == 2 ? BossRules.PhaseTwoSpeed : 1f)`; `ReceiveAttack`: `if (Untargetable) return DamageResult.Miss(attack.Type);` first; the BossHalf line becomes `if (IsBoss && !_halfSaid && BossRules.PhaseTwoAt(Hp, MaxHp)) { _halfSaid = true; EventBus.Instance.RaiseBossHalf(this); ChangeState(new RoarState()); }`; `public void EnterPhaseTwo(bool silent)`: `Phase = 2; BossArena.Change(this); if (!silent) Log($"{DisplayName} is enraged!")`; `RestoreFrom`: after Hp is set, `if (IsBoss && BossRules.PhaseTwoAt(Hp, MaxHp)) { _halfSaid = true; EnterPhaseTwo(silent: true); }`; `ClientHurt`: `if (IsBoss && Phase == 1 && BossRules.PhaseTwoAt(hp, MaxHp)) EnterPhaseTwo(silent: true);` (cosmetic arena change on clients); `TrySignature`: `var sig = BossRules.NextSignature(Profile.Signature, Profile.SecondSignature, Phase, ref _useSecond); ChangeState(new SignatureState(sig));`; `AnnounceSignature(Signature sig)` / `PerformSignature(Signature sig)` take the signature (called by `SignatureState` with its `_sig`); Summon uses `BossRules.SummonFor(Profile.SummonId, Floor)` and counts alive by that id. `public void SetUntargetable(bool on) => Untargetable = on;`
  - `RoarState : AiState { Name => "Roar"; Enter: enemy.SetUntargetable(true); PlaySfx("boss"); Log($"{DisplayName} roars!"); enemy.FlashWhite(3 pulses over 1.2 s) ; Update: stand (MoveToward self), after RoarSeconds → ChangeState(new ChaseState()); Exit: SetUntargetable(false); enemy.EnterPhaseTwo(silent: false) }`. The decal flash is combat-feel's telegraph decal; if it exposes a helper (`SignatureDecal`/`Telegraph`) call its flash here, else the white body pulse is the flash (say which in the commit).

- [ ] **Step 1: Write the failing tests**

```csharp
public class BossRulesTests
{
    [Fact] public void SummonFor_UpgradesHordeFodderToBrutesFromFloorEight()
    {
        Assert.Equal("enemy.horde_husk", BossRules.SummonFor("enemy.horde_husk", 5));
        Assert.Equal("enemy.horde_husk_brute", BossRules.SummonFor("enemy.horde_husk", 8));
        Assert.Equal("enemy.cave_spider", BossRules.SummonFor("enemy.cave_spider", 20));
        Assert.Equal("enemy.horde_husk_brute", BossRules.SummonFor("enemy.horde_husk_brute", 3));
    }
    [Fact] public void NextSignature_AlternatesOnlyInPhaseTwo()
    {
        var use = false;
        Assert.Equal(Signature.Slam, BossRules.NextSignature(Signature.Slam, Signature.Summon, 1, ref use));
        Assert.Equal(Signature.Summon, BossRules.NextSignature(Signature.Slam, Signature.Summon, 2, ref use));
        Assert.Equal(Signature.Slam, BossRules.NextSignature(Signature.Slam, Signature.Summon, 2, ref use));
        Assert.Equal(Signature.Summon, BossRules.NextSignature(Signature.Slam, Signature.Summon, 2, ref use));
        Assert.Equal(Signature.Slam, BossRules.NextSignature(Signature.Slam, Signature.None, 2, ref use));
    }
    [Fact] public void RandomSecond_NeverTheFirst_AndNeverNone()
    {
        for (var s = 0; s < 50; s++) { var sig = BossRules.RandomSecond(Signature.Summon, new Random(s)); Assert.NotEqual(Signature.Summon, sig); Assert.NotEqual(Signature.None, sig); }
        Assert.Equal(BossRules.RandomSecond(Signature.Slam, new Random(7)), BossRules.RandomSecond(Signature.Slam, new Random(7)));
    }
    [Theory, InlineData(25, "the First Herald"), InlineData(30, "the Second Herald"), InlineData(45, "the Fifth Herald"), InlineData(90, "the 14th Herald")]
    public void HeraldName_CountsFromTwentyFive(int floor, string name) => Assert.Equal(name, BossRules.HeraldName(floor));
    [Fact] public void PhaseTwoAt_HalfOrBelow_ButNotDead() { Assert.True(BossRules.PhaseTwoAt(50, 100)); Assert.False(BossRules.PhaseTwoAt(51, 100)); Assert.False(BossRules.PhaseTwoAt(0, 100)); }
    [Fact] public void ProfileParsesTheNewFields() { Assert.Equal(Signature.None, new EnemyProfile().SecondSignature); Assert.Equal(ArenaChange.None, new EnemyProfile().ArenaChange); }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter BossRulesTests` → build error.
- [ ] **Step 3: Implement** `BossRules`, the profile/def fields, the data edits, `RoarState`, and the `Enemy` changes; add the `BossArena` stub (`src/Enemies/Boss/BossArena.cs`: `public static partial class BossArena { public static void Change(Enemy boss) { } public static void Prepare(Enemy boss, FloorLayout layout, Node3D root, int seed) { } }`).
- [ ] **Step 4: Run** — tests PASS; `dotnet build` → 0 errors; `timeout 60 godot --headless --path . -- --seed=7 --floor=5 --quit-after=5 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "Boss|ERROR"` → the Crypt Lord spawns, no errors (an `[Enemy]` print of `sig=Slam/Summon arena=Darken` added to the boss spawn line in `PlaceEnemies` helps here).
- [ ] **Step 5: Commit** — `git add src/Enemies data/enemies tests/Enemies/BossRulesTests.cs && git commit -m "feat(bosses): a roar at half health, phase two speed, and a second signature on the shared cooldown"`

### Task 10: BossArena: braziers out, webs, moving embers, the gravity well

**Files:**
- Create: `src/Enemies/Boss/BossArena.cs` (replace the stub), `src/Enemies/Boss/SlowPatch.cs`, `src/Enemies/Boss/EmberPatch.cs`
- Modify: `src/Dungeon/DungeonBuilder.cs:1159-1163` (call `BossArena.Prepare` after the boss spawns), `src/Enemies/Enemy.cs` (`_PhysicsProcess`: gravity-well tick)

**Interfaces:**
- Consumes: `Brazier` (`Dungeon/Props/Fire.cs:98-122`), `Fire`, `FloorFire` (`Dungeon/Props/FloorFire.cs`), `TrapRules.FloorFire(floor)`, `RoomCells.Free(layout, room)`, `DungeonBuilder.Surface`, `Rng.ActorSeed(GameSeed, floor, Enemy.BossNetId)`.
- Produces:
  - `BossArena.Prepare(Enemy boss, FloorLayout layout, Node3D root, int seed)`: Darken → four `Brazier`s named `ArenaBrazier{0..3}` on the arena's floor at `centre ± 3.2 m` on both axes (snapped to a free cell's surface), lit; others → nothing. Called on server and clients alike (deterministic).
  - `BossArena.Change(Enemy boss)`: by `boss.Profile.ArenaChange`: Darken → every `ArenaBrazier*`'s `Fire` child `Visible = false`, `SetProcess(false)`, log "The braziers die. The arena goes dark." (log on the server only); Webs → `BossRules.SlowPatchCount` `SlowPatch`es at cells drawn from `RoomCells.Free(layout, arena)` with `new Random(Rng.ActorSeed(GameSeed, floor, Enemy.BossNetId))`, never the boss's own cell; Embers → `BossRules.EmberPatchCount` `EmberPatch`es orbiting the arena centre at radius `EmberOrbit`, speed `EmberSpeed`, phases 0 and π; GravityWell → `boss.SlowAuraRadius = BossRules.GravityWellRadius` and a flat dark translucent `CylinderMesh` disc (radius 6, height 0.02, `(0.1, 0.05, 0.2, 0.25)`) parented to the boss. Idempotent (a second call does nothing).
  - `SlowPatch : Area3D` (modelled on `FloorFire`): `CollisionMask = 2` (adventurers), a `CylinderShape3D { Radius = BossRules.SlowPatchRadius, Height = 1.6 }`, a whitish translucent disc mesh plus six thin strand cylinders radiating out; every 0.8 s on the server: each `Adventurer { Downed: false }` inside gets `State.Effects.Apply(new StatusApplication(StatusKind.Slow, BossRules.SlowPatchSeconds, 0)); State.Recalculate();`.
  - `EmberPatch : Node3D`: owns a `FloorFire { Floor = floor }` child; `_PhysicsProcess` moves itself along the orbit (both server and clients; the child burns only on the server as `FloorFire` already does).
  - `Enemy`: `public float SlowAuraRadius { get; set; }`; in `_PhysicsProcess` (alive, server) every `BossRules.GravityWellTick` s: `foreach adv in Party.AdventurersWithin(GlobalPosition, SlowAuraRadius)` apply `Slow` for `GravityWellSlowSeconds` and `Recalculate()`.

- [ ] **Step 1: Implement** as specified (Godot side; the flow in Task 13 pins Darken on floor 5 — count lit braziers before and after the phase).
- [ ] **Step 2: Build** — `dotnet build` → 0 errors.
- [ ] **Step 3: Commit** — `git add src/Enemies/Boss src/Dungeon/DungeonBuilder.cs src/Enemies/Enemy.cs && git commit -m "feat(bosses): arena changes at phase two: braziers out, webs, moving embers, a gravity well"`

### Task 11: Endless bosses: every fifth floor from 25, the Nth Herald

**Files:**
- Modify: `src/Dungeon/TierRules.cs:13` (`IsBossFloor`), `src/Adventure/Core/Design/Reinforcements.cs:119` (`BossFloorFor`), `src/Dungeon/DungeonBuilder.cs:1159-1163` (the Herald's profile), `src/Adventure/PortalSpawner.cs:76` (message text)
- Test: `tests/Dungeon/TierRulesTests.cs` (create if missing), `tests/Adventure/ReinforcementTests.cs:71-75`

**Interfaces:**
- Produces: `TierRules.IsEndlessBossFloor(int floor) => floor > FinalFloor && floor % FloorsPerTier == 0`; `TierRules.IsBossFloor(int floor) => (floor >= FloorsPerTier && floor % FloorsPerTier == 0 && floor <= FinalFloor) || IsEndlessBossFloor(floor)`; `IsFinalBossFloor` unchanged (victory stays on 20). `PortalRules.BossFloorFor(int floor) => floor <= TierRules.FinalFloor ? Math.Min(TierRules.FinalFloor, (TierRules.TierIndex(floor) + 1) * TierRules.FloorsPerTier) : Math.Max(TierRules.FinalFloor, floor / TierRules.FloorsPerTier * TierRules.FloorsPerTier)` (21–24 → 20; 25–29 → 25; 30–34 → 30: a Herald's death silences its own five floors). `DungeonBuilder.PlaceEnemies`: when `TierRules.IsEndless(floor)` the boss profile becomes `profile with { Name = BossRules.HeraldName(floor), Color = (CurrentTheme?.WallColor ?? baseColor).Lerp(baseColor, 0.5f).ToHtml(false), SecondSignature = BossRules.RandomSecond(profile.Signature, new Random(Rng.ActorSeed(GameSeed, floor, Enemy.BossNetId))), ArenaChange = ArenaChange.None }` (HP and damage already follow `TierRules.HpScale/DamageScale`'s endless term). Everything else flows from `IsBossFloor`: `LayoutKinds` deals an arena shape (`LayoutKinds.cs:68`), `FloorMenu`/`OfflineDesigner` plan a boss, `PlanResolver`/`LegacyPopulator` seat `tier.BossId` (tier index clamps to 3: the Abyss Herald), `PlaceStairs` seals the way down (`DungeonBuilder.cs:950`), `Main.OnBossDefeated` unlocks it.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Dungeon/TierRulesTests.cs
public class TierRulesTests
{
    [Theory, InlineData(5, true), InlineData(20, true), InlineData(21, false), InlineData(23, false), InlineData(25, true), InlineData(30, true), InlineData(33, false), InlineData(100, true)]
    public void IsBossFloor_EveryFifthEndlessFloor_NotTwentyThree(int floor, bool boss) => Assert.Equal(boss, TierRules.IsBossFloor(floor));
    [Fact] public void FinalBossFloor_StaysTwenty() { Assert.True(TierRules.IsFinalBossFloor(20)); Assert.False(TierRules.IsFinalBossFloor(25)); }
    [Fact] public void EndlessBossFloors_StayInTheLastTier() => Assert.Equal(3, TierRules.TierIndex(25));
}
// tests/Adventure/ReinforcementTests.cs: replace the `BossFloorFor(33)` pin with
[Fact] public void BossFloorFor_EndlessHeraldsCommandTheirOwnFiveFloors()
{
    Assert.Equal(20, PortalRules.BossFloorFor(21)); Assert.Equal(20, PortalRules.BossFloorFor(24));
    Assert.Equal(25, PortalRules.BossFloorFor(25)); Assert.Equal(25, PortalRules.BossFloorFor(29));
    Assert.Equal(30, PortalRules.BossFloorFor(30)); Assert.Equal(30, PortalRules.BossFloorFor(33));
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "TierRulesTests|ReinforcementTests"` → the endless pins fail.
- [ ] **Step 3: Implement**; sweep other tests that pin `IsBossFloor` on endless floors (`grep -rn "IsBossFloor\|IsEndless" tests`) and update expectations that assumed no endless boss; `PortalSpawner.cs:76` message: `$"the boss of floor {bossFloor} is dead"`.
- [ ] **Step 4: Run** — `cd tests && dotnet test --nologo -v q` → all PASS; `timeout 60 godot --headless --path . -- --seed=7 --floor=25 --quit-after=6 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "floor 25|Herald|ERROR"` → "boss" in the floor line and the Herald's name.
- [ ] **Step 5: Commit** — `git add src/Dungeon/TierRules.cs src/Adventure/Core/Design/Reinforcements.cs src/Dungeon/DungeonBuilder.cs src/Adventure/PortalSpawner.cs tests && git commit -m "feat(bosses): a Herald every five floors in the endless depths; its death silences the portals for five floors"`

### Task 12: Archetypes: Kites, Support, Bomber (data, AI states, the horde share)

**Files:**
- Create: `src/Enemies/Core/Elites/BomberRules.cs`, `data/enemies/horde_husk_bomber.tres`, `horde_crawler_bomber.tres`, `horde_cinder_bomber.tres`, `horde_shade_bomber.tres`
- Modify: `src/Enemies/Defs/EnemyDef.cs:55-74` (`Kites`, `Support`, `SupportKind`, `Bomber` exports + `ToProfile`), `src/Enemies/Core/EnemyProfile.cs` (same four), `src/Enemies/Ai/AiState.cs:27-55,90-121,188-202` (`ChaseState`, `ShootState`, `DeadState`; new `KiteState`, `SupportState`, `DetonateState`), `src/Enemies/Enemy.cs` (`SupportAlly()`, `CastSupport`, `Detonate`, `_noCorpse`), `src/Adventure/HordeSpawner.cs:163-179` (`SpawnOne`), `data/enemies/skeleton_archer.tres`, `abyss_archer.tres`, `bandit_archer.tres` (`Kites = true`), `cave_shaman.tres`, `bone_priest.tres` (`Support = true`, `SupportKind = "Shield"`), `forge_priest.tres` (`Support = true`, `SupportKind = "Regen"`), resist clamps: `broodmother.tres`, `bone_priest.tres`, `forge_golem.tres`, `iron_colossus.tres`, `skeleton_archer.tres` (`ResistPoison = 80` → `75`)
- Test: `tests/Enemies/BomberRulesTests.cs`, `tests/Enemies/EnemyDataTests.cs`

**Interfaces:**
- Produces:
  - `EnemyProfile.Kites` (bool), `Support` (bool), `SupportKind` (`SupportKind` enum `{ Shield, Regen }`, default Shield), `Bomber` (bool); `EnemyDef` exports of the same (`SupportKind` as a string).
  - `public static class BomberRules { const int FromFloor = 6, OneIn = 8; const float Radius = 3f, DamageMultiplier = 3f, FuseSeconds = 0.35f; static bool IsBomberSlot(int floor, int ordinal) => floor >= FromFloor && ordinal % OneIn == OneIn - 1; static string? BomberFor(IReadOnlyDictionary<string, EnemyProfile> roster, int tierIndex)` → the roster id with `Bomber && Faction == "horde" && TierIndex == tierIndex`. Also `public static class SupportRules { const float Range = 8f, AllyHpFraction = 0.7f, Cooldown = 8f, ShieldPoints = 30f, ShieldSeconds = 12f, RegenPerSecond = 3f, RegenSeconds = 6f; }` and `KiteRules { const float RetreatFraction = 0.5f, MaxSeconds = 2.5f; static bool ShouldKite(float dist, float preferred) => dist < preferred * RetreatFraction; }` in the same file.
  - Bomber defs: `Id = "enemy.horde_<x>_bomber"`, `DisplayName` "Husk Bomber" / "Crawler Bomber" / "Cinder Bomber" / "Shade Bomber", `Kind = "Melee"`, `MaxHp = 12`, `MinDamage = 4, MaxDamage = 6` (×3 on detonation), `MoveSpeed = 4.6`, `DetectRange = 40`, `AttackRange = 1.9`, `Xp = 4`, `Color` = the tier fodder's colour lerped 60% toward `(0.9, 0.15, 0.1)`, `Scale` = the fodder's, `Model` = the fodder's, `Faction = "horde"`, `TierIndex` 0..3, `Bomber = true`, `PackHunter = true`.
  - AI: `ChaseState.Update`: for `Kind != Melee`: `if (enemy.Profile.Support && enemy.SupportAlly() is { } ally && enemy.SupportReady) { ChangeState(new SupportState(ally)); return; }` then `if (enemy.Profile.Kites && KiteRules.ShouldKite(dist, PreferredRange)) { ChangeState(new KiteState()); return; }` before the Shoot branch; for `Kind == Melee && enemy.Profile.Bomber`: `if (dist <= enemy.AttackRange * 1.2f) { ChangeState(new DetonateState()); return; }` instead of `AttackState`. `ShootState` recover (`:116-118`): the same Support then Kite checks before the stay-or-chase decision.
  - `KiteState : AiState { Name => "Kite"; Update: FleeStep(dt) (backs straight away), FaceToward(target); exit to ShootState when dist >= PreferredRange && HasLineOfSight(), to ChaseState after KiteRules.MaxSeconds or when the party is lost }`.
  - `SupportState : AiState { Name => "Support"; ctor(Enemy ally); Update: stand, face the ally, after WindupSeconds → enemy.CastSupport(ally) once, after Windup + Recover → ChaseState }`. `Enemy.SupportAlly()` → the nearest other `Enemy { IsAlive: true }` within `SupportRules.Range` with `Hp < MaxHp * AllyHpFraction` (never itself, never a corpse), null when alone; `Enemy.SupportReady => _supportTimer <= 0f` (ticked in `_PhysicsProcess`); `Enemy.CastSupport(Enemy ally)`: `_supportTimer = Cooldown`; Shield → `ally.ApplyStatus(new(StatusKind.Shield, ShieldSeconds, ShieldPoints))`, Regen → `ally.ApplyStatus(new(StatusKind.Regen, RegenSeconds, RegenPerSecond))`; `HitFx.SpellBurst(GetParent(), ally.GlobalPosition + Up, Profile.Color)`; log "{name} shields/mends {ally}."; PlayAnim("Punch").
  - `DetonateState : AiState { Name => "Detonate"; Enter: Log($"{DisplayName} hisses!"), enemy.FlashWhite; Update: stand; after BomberRules.FuseSeconds → enemy.Detonate() }`. `Enemy.Detonate()`: server: `foreach v in Party.AdventurersWithin(pos, Radius) v.ReceiveAttack(Stats, Attack with { MinDamage ×3, MaxDamage ×3, Type = Fire }, $"{DisplayName}'s blast")`; `HitFx.SpellBurst`, `Tremor.ShakeOnly(0.04f, 0.3f)`; `_noCorpse = true; Kill();`. `DeadState.Enter`: after `OnDeath()`, `if (enemy.NoCorpse) enemy.QueueFree();` (`public bool NoCorpse => _noCorpse`); `OnDeath` returns before loot when `_noCorpse` (bombers are `IsHorde` anyway; the arrows/key lines stay).
  - `HordeSpawner.SpawnOne`: `var id = BomberRules.IsBomberSlot(_floor, Spawned) && BomberRules.BomberFor(builder.Roster, TierRules.TierIndex(_floor)) is { } bomber ? bomber : (Spawned < _order.Count ? _order[Spawned] : _order[^1]);`.
  - `EnemyDataTests` parses every `data/enemies/*.tres` as text (path relative to the test assembly: `../../../../data/enemies`) and asserts: no `Resist* = ` value above 75; exactly four files with `Bomber = true`, one per `TierIndex` 0–3, all `Faction = "horde"`; `Kites = true` in exactly the three archer files; `Support = true` in exactly the three priest/shaman files with the given `SupportKind`s; the four bosses carry a `SecondSignature` other than None and an `ArenaChange` other than None.

- [ ] **Step 1: Write the failing tests**

```csharp
public class BomberRulesTests
{
    [Fact] public void IsBomberSlot_OneInEightFromFloorSix()
    {
        Assert.False(BomberRules.IsBomberSlot(5, 7));
        Assert.Equal(5, Enumerable.Range(0, 40).Count(i => BomberRules.IsBomberSlot(6, i)));
        Assert.True(BomberRules.IsBomberSlot(12, 7)); Assert.False(BomberRules.IsBomberSlot(12, 0));
    }
    [Fact] public void BomberFor_FindsTheTiersBomber()
    {
        var roster = new Dictionary<string, EnemyProfile> { ["enemy.horde_husk_bomber"] = new() { Id = "enemy.horde_husk_bomber", Faction = "horde", TierIndex = 0, Bomber = true }, ["enemy.horde_husk"] = new() { Id = "enemy.horde_husk", Faction = "horde", TierIndex = 0 } };
        Assert.Equal("enemy.horde_husk_bomber", BomberRules.BomberFor(roster, 0)); Assert.Null(BomberRules.BomberFor(roster, 2));
    }
    [Fact] public void ShouldKite_InsideHalfThePreferredRange() { Assert.True(KiteRules.ShouldKite(3.9f, 8f)); Assert.False(KiteRules.ShouldKite(4.1f, 8f)); }
}

public class EnemyDataTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "enemies");
    private static Dictionary<string, string> Files() => Directory.GetFiles(Dir, "*.tres").ToDictionary(Path.GetFileNameWithoutExtension, File.ReadAllText);
    private static bool Has(string text, string line) => text.Split('\n').Any(l => l.Trim() == line);
    [Fact] public void NoResistAboveSeventyFive() => Assert.All(Files(), f => Assert.All(System.Text.RegularExpressions.Regex.Matches(f.Value, @"Resist\w+ = (\d+)").Select(m => int.Parse(m.Groups[1].Value)), v => Assert.True(v <= 75, f.Key)));
    [Fact] public void FourBombers_OnePerTier_AllHorde()
    {
        var bombers = Files().Where(f => Has(f.Value, "Bomber = true")).ToList();
        Assert.Equal(4, bombers.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, bombers.Select(f => int.Parse(System.Text.RegularExpressions.Regex.Match(f.Value, @"TierIndex = (\d)").Groups[1].Value)).OrderBy(x => x));
        Assert.All(bombers, f => Assert.True(Has(f.Value, "Faction = \"horde\"")));
    }
    [Fact] public void ArchersKite_PriestsSupport()
    {
        var f = Files();
        Assert.Equal(new[] { "abyss_archer", "bandit_archer", "skeleton_archer" }, f.Where(x => Has(x.Value, "Kites = true")).Select(x => x.Key).OrderBy(x => x));
        Assert.Equal(new[] { "bone_priest", "cave_shaman", "forge_priest" }, f.Where(x => Has(x.Value, "Support = true")).Select(x => x.Key).OrderBy(x => x));
        Assert.True(Has(f["forge_priest"], "SupportKind = \"Regen\"")); Assert.True(Has(f["cave_shaman"], "SupportKind = \"Shield\"")); Assert.True(Has(f["bone_priest"], "SupportKind = \"Shield\""));
    }
    [Fact] public void EveryTierBossHasASecondKit()
    {
        var f = Files();
        foreach (var boss in new[] { "crypt_lord", "broodmother", "iron_colossus", "abyss_herald" })
        {
            Assert.Matches(@"SecondSignature = ""(Slam|Volley|Charge|Summon)""", f[boss]);
            Assert.Matches(@"ArenaChange = ""(Darken|Webs|Embers|GravityWell)""", f[boss]);
        }
        Assert.True(Has(f["crypt_lord"], "SummonId = \"enemy.horde_husk\"")); Assert.True(Has(f["broodmother"], "SummonId = \"enemy.cave_spider\""));
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "BomberRulesTests|EnemyDataTests"` → build errors and data failures (the Task 9 boss assertions pass already).
- [ ] **Step 3: Implement** the rules, the def/profile fields, the data edits, the three states and the hooks, the horde share.
- [ ] **Step 4: Run** — tests PASS; `dotnet build` → 0 errors; `timeout 90 godot --headless --path . -- --test-horde --quit-after=40 2>&1 | grep -E "RESULT|ERROR"` still ok (floor 3: no bombers yet, the stream is unchanged).
- [ ] **Step 5: Commit** — `git add src/Enemies src/Adventure/HordeSpawner.cs data/enemies tests/Enemies && git commit -m "feat(enemies): archers kite, priests shield and mend, and one horde creature in eight is a bomber from floor 6"`

### Task 13: `--test-elites-and-bosses`, the docs

**Files:**
- Modify: `src/Core/DebugTools.cs` (flag beside `--test-safe-room` at `:327`, dispatch beside `:738`, a `RunElitesAndBossesTest()` modelled on `RunSafeRoomTest` at `:2685`), `docs/development.md` (the `--test-*` list after the `--test-safe-room` lines, and a new section "Elites, boss phases and the endless Heralds" after "Safe rooms")
- README: no key changed; no controls edit.

**Interfaces:**
- Consumes: everything above; `Game.Instance.JumpToFloor(int)` (`Game.cs:167`), `Main.SaveGame/LoadGame`, `PartyController.Teleport`, `DungeonBuilder.SpawnEnemy(..., elite:)`, `Enemy.ForceAggro/Kill/ReceiveAttack/WardPoints/Phase/State.Name`, `PortalSpawner` (find via `root.GetChildren().OfType<PortalSpawner>()`), `Trap.TrapStats`.
- Produces: the flag `--test-elites-and-bosses` (sets `NoAmbush = NoPortals = NoGathering = true`, `ForcedLethality ??= 1f`, `_seed ??= 8`, `_startFloor = 8`, `_floorGiven = true`, `_quitAfter = 240`), prints `[elites] ...` lines and `[elites] RESULT ok` or `[elites] RESULT FAIL (reasons)`.

- [ ] **Step 1: Write the flow** as a step machine (`_elStep`, `_elAt`, `_elFails`), each case guarded by elapsed time like `RunSafeRoomTest`:
  1. **Roster** (floor 8, after 1 s): the planned elites `root…OfType<Enemy>().Where(e => e.IsElite && e.SpawnIndex >= 0)`; expect exactly `EliteRules.CountFor(8, 0)` = 2; for each, assert `e.Profile.EliteName == EliteRules.Roll(Rng.FloorSeed(seed, 8), 8, e.SpawnIndex, faction).Name` and the same affixes; print names and `EliteRules.Describe`. Assert `Game.Instance.Floors[8]` is absent or has no `EliteSpawns` yet.
  2. **Save/load keeps the elites**: `SaveGame("elites")`, set `DungeonBuilder.ExtraElitesPerFloor = 2` (as if *Marked* had been taken after the save), `LoadGame("elites")`; after 1.5 s the same two names stand at the same spawn indices and no third elite exists; reset `ExtraElitesPerFloor = 0`. Then `Game.Instance.Floors[8].EliteSpawns` equals the two indices (capture wrote them).
  3. **Quiet floor**: `QueueFree` every enemy; teleport the party to a free cell of a big room (`PlanResolver.Anchor`).
  4. **Warded**: `SpawnEnemy("enemy.ghoul", 4 m away, 8, elite: new EliteRoll(new[] { Warded }, "Test, Warded"))`; after 0.2 s `WardPoints > 0` and `≈ min(0.25 × MaxHp, 200)`; hit it with `ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = 5, MaxDamage = 5 }, new HitContext { DefenderStunned = true }, rng, "test")` → `Hp == MaxHp` and `WardPoints` fell by about 5; hit it with 1000 → ward 0, Hp < MaxHp, log "ward shatters"; after `WardRegrowSeconds + 0.5` s `WardPoints > 0` again. `Kill()`.
  5. **Volatile**: spawn a Volatile ghoul 1.5 m from the lead; note the lead's Hp; `Kill()`; after 0.3 s the lead's Hp dropped and `State.Effects.Has(StatusKind.Burn)`. Then **hazard kill still detonates**: spawn another Volatile 1.5 m away and call `KillByEnvironment("the test press", HazardDamage.Crush)` → the lead takes damage again.
  6. **Thorned**: heal the lead; spawn a Thorned ghoul 1.2 m in front of the lead; drive the exact melee path `MeleeSwing` takes: `var r = thorned.ReceiveAttack(lead.State.Stats, lead.State.WeaponProfile.ToAttack(), new HitContext { DefenderStunned = true }, party.Rng, lead.State.Name); party.OnHitLanded(lead.State, thorned, r);` → the lead's Hp is below its start by about `0.3 × r.Amount` (armor-mitigated; assert a drop of at least `0.15 × r.Amount` and the log line "spines"). Then the same with a Hexed ghoul → `lead.State.Effects.Has(StatusKind.Slow)`. `Kill()` both.
  7. **Shrouded**: spawn a Shrouded ghoul 8 m away; loose an arrow at it directly: instantiate `res://scenes/Props/Projectile.tscn` under the floor root at the lead's chest, `Setup(party, lead.State, lead.State.WeaponProfile.ToAttack() with { Kind = AttackKind.Ranged }, dir * 20f, isArrow: true, Colors.White, lead)` → within 1 s the projectile is freed (`!IsInstanceValid`) and no `EnemyHurt` fired for the shrouded one (subscribe at step 1); teleport the lead inside the dome (2 m from it) and run the nova path: `Enemy.ShroudSeparates(lead.GlobalPosition, shrouded.GlobalPosition)` is false, and from 7 m outside it is true. `Kill()`.
  8. **Reward**: spawn a plain elite (`Swift`) 3 m away, note total party XP and the number of `LootDrop`s; kill it with a party hit (any `ReceiveAttack` from a member's stats, then `Party.OnHitLanded`) or `Kill()`; assert XP rose by `round(ScaledXp(8) × 2.5)` (use `Kill()` so the XP path is the only variable), a `LootDrop` appeared whose `Item.Rarity != Common`, coins were spawned (`LootSpawned` with `Coins > 0` or a coin `LootDrop`), and `EliteSlain` fired once (subscribe at step 1).
  9. **Kiting archer**: spawn `enemy.skeleton_archer` 2 m from the party, `ForceAggro`; within 3 s its state was "Kite" at least once and its distance exceeds 3.5 m.
  10. **Support caster**: spawn `enemy.cave_shaman` and an `enemy.ghoul` 3 m apart, 14 m from the party; wound the ghoul to 40% with `ReceiveAttack`; `ForceAggro` both; within 10 s the ghoul has `Effects.Has(StatusKind.Shield)` and the shaman's state was "Support" once; kill both.
  11. **Bomber**: spawn `enemy.horde_husk_bomber` 3 m from the lead; note Hp; `ForceAggro`; within 4 s the node is freed (`!IsInstanceValid`) and the lead's Hp dropped; no `LootDrop` appeared.
  12. **Boss floor 5**: `JumpToFloor(5)`; after 1.5 s find the boss (`IsBoss`), assert `Profile.SecondSignature == Summon`, `ArenaChange == Darken`, four `ArenaBrazier*` with visible `Fire`; teleport the party 6 m from it; hit it to 45% with one `ReceiveAttack(Trap.TrapStats, big, DefenderStunned)` → `State.Name == "Roar"`, `Untargetable`, a further `ReceiveAttack` returns `!Hit`; after 1.5 s `Phase == 2`, `EffectiveMoveSpeed ≈ Profile.MoveSpeed × 1.2` (no statuses), braziers' `Fire.Visible == false`; set `_signatureTimer = 0` via a test hook `Enemy.HurrySignature()` and watch `State.Name` for 20 s: both `Signature:Slam` and `Signature:Summon` seen, and at least one `enemy.horde_husk` summoned (floor 5 < 8: fodder, not brutes). Then **phase survives a load**: `SaveGame("boss")`, `LoadGame("boss")` → after 1.5 s the boss has `Phase == 2`, braziers dark, no "roars!" line since the load. `Kill()` the boss → `DownStairs.Locked == false`, `Game.Instance.Victory == false`.
  13. **Endless**: `JumpToFloor(23)` → no `IsBoss` enemy, stairs unlocked; `JumpToFloor(25)` → one boss named "the First Herald", `Profile.SecondSignature != None`, `ArenaChange == None`, stairs locked; `PortalSpawner` not silenced for the boss reason (its `SilencedWhy` is "--no-portals" here, so instead assert `PortalRules.BossFloorFor(25) == 25` and that `Game.Instance.Floors.TryGetValue(25)` has `BossDefeated == false`); `Kill()` it → `Floors[25].BossDefeated`, stairs unlocked, `Victory == false`; `JumpToFloor(27)` → the spawner's `RefreshSilence()` reason contains "floor 25" when `NoPortals` is cleared for the check (`NoPortals = false; portals.RefreshSilence(); assert; NoPortals = true`).
  14. Print `RESULT`.
- [ ] **Step 2: Run** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && timeout 280 godot --headless --path . -- --test-elites-and-bosses 2>&1 | grep -E "^\[elites\]|RESULT|ERROR"` → `[elites] RESULT ok`, no `ERROR` lines.
- [ ] **Step 3: Regression** — `cd tests && dotnet test --nologo -v q` → all PASS; `timeout 150 godot --headless --path . -- --test-safe-room 2>&1 | grep RESULT`, `timeout 60 godot --headless --path . -- --test-depth 2>&1 | grep RESULT`, `timeout 90 godot --headless --path . -- --test-horde 2>&1 | grep RESULT`, `timeout 120 godot --headless --path . -- --builtin --quit-after=6 2>&1 | grep -cE "adapted|rejected"` → ok, ok, ok, 0.
- [ ] **Step 4: Docs** — `docs/development.md`: add `godot --headless --path . -- --test-elites-and-bosses   # floor 8 seed 8: the two rolled elites by name, the same after a save/load, Warded absorbs and regrows, Volatile bursts (even under the Press), Thorned reflects, the Shrouded dome stops a bolt and an outside nova, x2.5 XP and a table.elite drop, a kiting archer, a shielding shaman, a bomber; floor 5: the Crypt Lord roars at half, speeds up, alternates Slam and Summon, the braziers go out, phase survives a load; floor 23 no boss, floor 25 the First Herald; prints [elites] RESULT` to the test list, and a section:

  > **Elites, boss phases and the endless Heralds.** From floor 2 one planned creature per floor is an elite (two from 8, three from 14; `EliteRules.CountFor`, plus whatever *Hostile Tenants* and *Marked* add through `DungeonBuilder.ExtraElitesPerFloor`), chosen and rolled in `PlanResolver` from the floor seed and the spawn index (`src/Enemies/Core/Elites/EliteRules.cs`, names from `EliteNames`), so peers and reloads agree; the save keeps the floor's `EliteSpawns` so a lifted curse never changes who stands there. Twelve affixes (table in the spec) each with a tell (`EliteVisuals`); Shrouded's dome is checked in `Projectile` and `Adventurer.Nova` (`ShroudRules`), Thorned and Hexed answer through `PartyController.OnHitLanded` → `Enemy.OnStruckBy`. Reward ×2.5 XP, `table.elite`, coins; events `EliteSeen` (once per enemy) and `EliteSlain`. The planner may name an elite (`MemberPlan.Elite`/`LonerPlan.Elite`); the validator fills and trims. Bosses: at half health a 1.2 s roar (untargetable), then speed ×1.2, the data's `SecondSignature` alternating with the first, and the `ArenaChange` (`BossArena`: braziers out, webs, moving embers, gravity well); a boss loaded below half is already in phase two. Endless floors 25, 30, … seat "the Nth Herald" (`TierRules.IsEndlessBossFloor`), whose death silences portals on its five floors (`PortalRules.BossFloorFor`). Archetype flags on `EnemyDef`: `Kites` (archers back off inside half their range), `Support` (`SupportKind` Shield or Regen on a hurt ally within 8 m every 8 s), `Bomber` (`horde_*_bomber`, one in eight of a stream from floor 6, 3 m blast ×3, no corpse).
- [ ] **Step 5: Commit** — `git add src/Core/DebugTools.cs docs/development.md && git commit -m "test(elites): the headless flow for elites, boss phases, Heralds and archetypes; docs"`

---

## Self-review notes

- Spec coverage (section 2): elite schedule, deterministic roll, affix counts, the twelve affixes with effects and tells, the Shrouded/Hexed exclusion, names from a syllable table (faction style for bandits), slim bar (combat-feel's, fed by `EliteSeen` + `DisplayName`), `EliteSeen` once per elite per floor, reward (XP ×2.5, `table.elite`, coins), `EliteSlain`, the planner's `Elite` field with fill/trim and the prompt line, boss phases (roar, speed, second signature, four arena changes, endless boss "none"), Heralds every five floors from 25 with the portal silence, the three archetype flags with their data edits, bomber share 1 in 8 from floor 6, resist clamps, save, net, headless flow, docs: all have a task. Mimic/Hunted elites spawn through `SpawnEnemy(..., elite:)` (Task 4) and ride `EnemySpawned` (Task 5); fortune owns their spawn sites.
- Type consistency: `EliteRoll(IReadOnlyList<EliteAffix> Affixes, string Name)` in Tasks 2–5, 13; `EliteRules.Roll(int floorSeed, int floor, int spawnIndex, string faction = "")` everywhere; `SpawnEnemy`'s optional `elite` is the last parameter in Tasks 4, 5, 13; `ArenaChange` enum lives in `BossRules.cs` and is used by `EnemyProfile`, `BossArena`, the data tests; `Enemy.WardPoints`, `Phase`, `Untargetable`, `SlowAuraRadius`, `OnStruckBy`, `OnDealtDamage`, `HurrySignature` named the same in Tasks 6–13.
- Shared-file members this plan adds (exact): `EventBus`: none beyond the contract's `EliteSeen`/`EliteSlain` (verified in Task 1). `Adventurer.cs`: `ReceiveAttack` returns `DamageResult` (Task 6) and one `continue` line in `Nova` (Task 7). `PartyController.cs`: one line in `OnHitLanded` (Task 7). `PromptBuilder.cs`: one sentence (Task 3). `AdventureDirector.cs`: one line `Elites++` (Task 4). `Hud.cs`: at most one subscription line if the HUD lacks it (Task 1). `Synth.cs`: `Horn()` (Task 6). `Projectile.cs`: the dome check and the `OnDealtDamage` call (Tasks 6–7). `SaveDtos.cs`: `FloorStateDto.EliteSpawns` (Task 4). `Messages.cs`/`Protocol.cs`: two optional DTO fields and the version bump (Task 5). `data/loot_tables/elite.tres` (Task 4; fortune lists it and finds it present).
- Spec points resolved by decision (flag to the owner): (1) "4 m dome" read as radius 4 m (`EliteRules.ShroudRadius`, one constant to tune). (2) Warded uses the existing `Shield` status so combat-feel's grey "Blocked" number and the HUD read it; `StatusEffectController.MaxShield` (200) caps it, which only matters for an elite above 800 HP. (3) "plus coins": elites always drop `CoinRules.ChestCoins(floor)` (regular enemies drop coins one time in four). (4) Dynamic enemies are not saved today (`FloorStateCapture.cs:26` keeps only `SpawnIndex >= 0`), so a dynamic elite's identity needs no save field; the only save addition is the floor's `EliteSpawns` list, which pins the roster against clause/curse changes and gives the client the same membership through `Welcome`/`FloorChange`. (5) Portals in the endless depths were permanently silent (`BossFloorFor` clamped to 20 and floor 20's boss is always dead there); the new rule gives each Herald its own five floors so "its death silences portals for five floors" means something. (6) Crypt Lord's "brutes from floor 8" can only matter on an endless Herald floor if a Herald rolled Summon with a horde id; the rule is kept in `BossRules.SummonFor` as written.
