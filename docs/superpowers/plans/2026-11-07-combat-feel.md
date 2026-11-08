# Combat Feel Implementation Plan (The Depths, pillar 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A fight reads without the log: enemies wind up before they swing and bosses draw their signature on the floor, every class has a defensive verb (dodge, perfect block, parry) with stagger as the reward, damage shows as numbers, bars and a boss bar, and statuses combine (Shatter, Ignite, Bleed, Vulnerable); all of it in solo and on the dedicated server.

**Architecture:** Engine-free rules hold every number: `DefenseRules`/`DefenseState` (dodge, parry, perfect block, stagger, remote grace) and `FeedbackRules`/`DamageNumberMerger` in `src/Combat/Core`, `TelegraphRules` in `src/Enemies/Core`, combos inside `HitResolver`. `Adventurer` owns one `DefenseState` per body and judges every incoming blow with it (`ReceiveAttack`); on the server a remote player's state is fed from two new `PoseFlags` bits plus the guard flag it already receives. `Enemy` gains a wind-up (`BeginWindup`), a stagger timer, and a signature telegraph with a `TelegraphDecal`; clients learn a telegraph through the existing, so far unused `EnemyStateMsg`. `EventBus.EnemyHurt` grows a `HitFlavor` and the striking member, which drives `DamageNumbers`, `HitMarker` and `EnemyBars`; the boss bar, vignette, level-up banner and loot toast are plain HUD controls fed by events both server and client already raise.

**Tech Stack:** Godot 4.7.2 .NET / C#, xUnit (`tests/`, dev machine only), headless `--test-*` flows in `src/Core/DebugTools.cs`.

**Spec:** `docs/superpowers/specs/2026-11-07-the-depths-update-design.md` section "1. Combat you can read" (binding; every number below is copied from it, `(tune)` marks the spec's guesses). Shared surface: the contract in the planning scratchpad (`EventBus` event shapes, `StatusKind.Vulnerable`, `Settings.Rebindable` "dodge").

## Global Constraints

- Engine-free rules live in `src/Combat/Core/` and `src/Enemies/Core/` (both linked into `tests/DungeonCrawler.Tests.csproj` by `../src/Combat/Core/**/*.cs` and `../src/Enemies/Core/**/*.cs`), never reference `Godot`, hold the spec's numbers as `const` fields of a `*Rules` class, and are tested with xUnit before the Godot side is written.
- Numbers, verbatim from the spec: damage numbers rise and fade 0.9 s, crit/perfect blow gold ×1.6, numbers within 0.15 s on one target merge; hit marker 0.12 s; enemy bar for anything hurt in the last 4 s, 1.2 m above its collider, none for hordes; boss bar tick at 50%; heavy hit received ≥15% max HP shakes; level-up portrait pulses gold 2 s; shooter hand glow for the wind-up, body tints to white for the last 0.2 s of a wind-up; signature delay 0.8→1.1 s, Charge 0.4→0.7 s (tune); dodge 0.25 s, 20 stamina, 0.6 s cooldown; perfect block = first 0.2 s of the guard, no stamina, negated, attacker staggered 1.2 s; parry 0.25 s window, 10 stamina per attempt, 0.8 s cooldown, melee only, attacker staggered 1.2 s; companions on Defensive/Aggressive dodge a telegraphed signature 50% of the time; hit chance `clamp(0.72 + (Acc − Eva) × 0.006, 0.35, 0.95)`; Shatter +50% and consumes Frost; Ignite 2 m for poison magnitude × 4, once; Bleed halves healing; Vulnerable +25% damage taken for 6 s; Thorns 4 on Bulwark tier 2; resists above 75 clamped to 75 in data.
- Dodge i-frames apply only to a creature's melee strike or projectile (anything that reaches `Adventurer.ReceiveAttack` with a non-null `attacker`, or an enemy `Projectile`): never to gas, flames, traps, falls or hazards (`attacker == null`, `ReceiveHazard`).
- Co-op is server-authoritative: the server resolves every hit; a client's dodge and parry windows reach it as `PoseFlags.Dodge`/`PoseFlags.Parry` bits in the pose it already sends at 30 Hz, the guard as the existing `PoseFlags.Guard`. Telegraphs reach clients through the existing `EnemyStateMsg`. `EnemyHurt` on the wire changes shape (flavor, striking member), so `Protocol.Version` becomes 7; no `MsgKind` is added, `MessageKindCount` stays 80.
- Save: this plan adds no DTO field and does not touch `SaveFormat`.
- Rate limits (anti-goal "noise"): numbers merge per target, hordes get no bars and no telegraph message, the loot toast fires for Rare and Legendary only, the vignette and shake are per-hit and decay.
- Build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before any `godot --headless` run; the flow runs with `--no-ambush --no-portals --no-gathering --lethality=1 --seed=7`.
- Collision discipline: this plan owns `src/Party/Adventurer.cs`, `src/Party/PartyController.cs` (combat paths only), `src/Party/Control/Control.cs` (`InputIntent`, `LocalInput`, `RemotePose`), `src/Combat/**`, `src/UI/Hud/**`, `scenes/UI/Hud.tscn`, the telegraph/stagger/decal parts of `src/Enemies/Enemy.cs` and `src/Enemies/Ai/AiState.cs`. Members it adds to files other plans own are listed in each task's Interfaces and summarised at the end.

## Review Focus

Spec-implied cases no task's main tests cover; each has its pin named in its owning task.

1. A dodging body must still be hurt by gas, flames, traps and falls; only a creature's blow or bolt is dodged. Pin: Task 3 `Judge_IgnoresHazards_WhateverTheState`, Task 11 flow step 8 (gas tick lands on a dodging body).
2. Holding guard for a long time and then being hit is an ordinary block (12 stamina, 75%), not a perfect one: the window is measured from the guard's start, not from the strike. Pin: Task 3 `PerfectBlock_OnlyInTheFirstWindow_ThenOrdinaryBlock`, flow step 3.
3. A parry never negates a projectile or a spell, and a parry attempt with no stamina does nothing and costs nothing (no free window). Pin: Task 3 `Judge_ParryIsMeleeOnly` and `TryParry_NeedsStamina_AndRespectsCooldown`.
4. A remote player's parry flag arrives late: the server still honours it within the grace, and a flag that stops arriving ends the window (no parry held open by a stale pose). Pin: Task 3 `Remote_FlagTailGrace_ThenEnds`.
5. Fifty horde bites and a poison ticking on ten enemies must not wallpaper the screen: numbers merge per target and hordes get no bars. Pin: Task 8 `Merger_MergesWithinWindow_PerTarget`, `Merger_TicksMergeWithTicksOnly`, flow step 9 (`EnemyBars.Showing` false for a horde body).
6. Heavy blows never stagger a boss (only a perfect block or a parry does) and a boss stagger cancels its signature and its decal. Pin: flow step 12 (`boss.Staggered` false after a heavy blow, true after a parry; no `TelegraphDecal` left behind).

---

### Task 1: Numbers: hit chance, Vulnerable, Bleed halves healing, Frost scales wind-up, resists clamped in data

**Files:**
- Modify: `src/Combat/Core/HitResolver.cs:42-43` (`HitChance`), `:8-26` (`Mitigate` gains an optional `StatusEffectController?`), `:67` (Vulnerable applied with the shield)
- Modify: `src/Combat/Core/StatusEffects.cs:5` (`StatusKind.Vulnerable`), `:15` (`IsDebuff`), new members `DamageTakenMultiplier`, `HealingMultiplier`
- Modify: `src/Characters/CharacterState.cs:114-123` (`Regenerate`, `Heal` apply `HealingMultiplier`)
- Create: `src/Enemies/Core/TelegraphRules.cs` (only `Scaled` this task; Task 6 fills the rest)
- Modify: `src/Enemies/Enemy.cs:90-91` (`WindupSeconds`/`RecoverSeconds` scaled by attack speed), `:424-425` (heal tick × `HealingMultiplier`, damage tick through `Mitigate(..., Effects)`)
- Modify: `src/Party/PartyController.cs:405` (`Mitigate(..., m.Effects)`)
- Modify data: `data/enemies/forge_golem.tres`, `bone_priest.tres`, `broodmother.tres`, `iron_colossus.tres`, `skeleton_archer.tres` (`ResistPoison = 80` → `75`)
- Test: `tests/Combat/CombatNumbersTests.cs` (new), `tests/Combat/HitResolverTests.cs:23-31` (update the pinned values)

**Interfaces:**
- Consumes: `Stats`, `StatusEffectController`, `TestClasses.Attacker/Dummy`.
- Produces: `DamagePipeline.HitChance(Stats attacker, Stats defender)` = `Math.Clamp(0.72f + (acc − eva) * 0.006f, 0.35f, 0.95f)`; `DamagePipeline.Mitigate(float raw, DamageType type, Stats defender, StatusEffectController? defenderEffects = null)` (multiplies by `defenderEffects.DamageTakenMultiplier` after armor and resist); `StatusKind.Vulnerable` (appended last, a debuff); `StatusEffectController.DamageTakenMultiplier => Has(Vulnerable) ? CombatRules.VulnerableMultiplier : 1f`; `StatusEffectController.HealingMultiplier => Has(Bleed) ? CombatRules.BleedHealingMultiplier : 1f`; `public static class CombatRules { public const float VulnerableMultiplier = 1.25f, VulnerableSeconds = 6f, BleedHealingMultiplier = 0.5f, MaxResist = 75f; }` (in `HitResolver.cs`); `TelegraphRules.Scaled(float seconds, float attackSpeed) => seconds / Math.Max(0.2f, attackSpeed)`; `Enemy.WindupSeconds => TelegraphRules.Scaled(Profile.WindupSeconds, Stats[Stat.AttackSpeed] * Effects.Multiplier(Stat.AttackSpeed))`, same for `RecoverSeconds`; `CharacterState.Heal(amount)` and the HP line of `Regenerate` multiply by `Effects.HealingMultiplier`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Combat/CombatNumbersTests.cs
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Enemies;
using DungeonCrawler.Tests.Characters;
using Xunit;

namespace DungeonCrawler.Tests.Combat;

public class CombatNumbersTests
{
    [Fact]
    public void HitChance_IsTheDepthsFormula()
    {
        Assert.Equal(0.72f, DamagePipeline.HitChance(TestClasses.Attacker(accuracy: 50), TestClasses.Dummy(evasion: 50)), 3);
        Assert.Equal(0.84f, DamagePipeline.HitChance(TestClasses.Attacker(accuracy: 70), TestClasses.Dummy(evasion: 50)), 3); // a 20-point gap is 12%
        Assert.Equal(0.95f, DamagePipeline.HitChance(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy()), 3);         // one miss in twenty stays
        Assert.Equal(0.35f, DamagePipeline.HitChance(TestClasses.Attacker(accuracy: 0), TestClasses.Dummy(evasion: 500)), 3);
    }

    [Fact]
    public void Vulnerable_IsADebuff_AndAddsAQuarter()
    {
        var c = new StatusEffectController();
        Assert.Equal(1f, c.DamageTakenMultiplier, 3);
        c.Apply(new StatusApplication(StatusKind.Vulnerable, CombatRules.VulnerableSeconds, 0));
        Assert.True(c.Get(StatusKind.Vulnerable)!.IsDebuff);
        Assert.Equal(1.25f, c.DamageTakenMultiplier, 3);
        Assert.Equal(12.5f, DamagePipeline.Mitigate(10, DamageType.Physical, TestClasses.Dummy(), c), 3);
        Assert.Equal(10f, DamagePipeline.Mitigate(10, DamageType.Physical, TestClasses.Dummy()), 3);
        var rng = new Random(2);
        var hit = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), c, new AttackProfile { MinDamage = 10, MaxDamage = 10 }, HitContext.None, rng);
        Assert.Equal(12.5f, hit.Amount, 3);
    }

    [Fact]
    public void Bleed_HalvesHealing()
    {
        var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
        c.Hp = 20;
        c.Heal(10); Assert.Equal(30f, c.Hp, 3);
        c.Effects.Apply(new StatusApplication(StatusKind.Bleed, 5f, 1f));
        Assert.Equal(0.5f, c.Effects.HealingMultiplier, 3);
        c.Heal(10); Assert.Equal(35f, c.Hp, 3);
    }

    [Fact]
    public void Frost_ScalesWindupByAttackSpeed()
    {
        Assert.Equal(0.5f / 0.7f, TelegraphRules.Scaled(0.5f, 0.7f), 4);
        Assert.Equal(0.5f, TelegraphRules.Scaled(0.5f, 1f), 4);
        Assert.Equal(2.5f, TelegraphRules.Scaled(0.5f, 0.01f), 4); // the floor keeps a frozen giant from winding up forever
    }

    [Fact]
    public void Resists_AreCappedAtSeventyFive()
    {
        Assert.Equal(75f, CombatRules.MaxResist);
        Assert.Equal(2.5f, DamagePipeline.Mitigate(10, DamageType.Poison, TestClasses.Dummy(resistAll: 80)), 3);
    }
}
```

In `tests/Combat/HitResolverTests.cs`, `Resolve_HitChanceUsesAccuracyVsEvasion`: `0.98f` → `0.95f`, `0.3f` → `0.35f`, and the 2000-roll band for accuracy 75 vs evasion 50 becomes `Assert.InRange(hits / 2000.0, 0.83, 0.91)` (0.72 + 0.15 = 0.87). (`CharacterState.Create(name, cls, rank)` is the factory at `src/Characters/CharacterState.cs:49`, as `tests/Characters/LevelingTests.cs:18` uses it.)

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter "CombatNumbersTests|HitResolverTests"` → build errors (`Vulnerable`, `DamageTakenMultiplier`, `TelegraphRules`, `CombatRules`), then the old 0.98 pin fails.
- [ ] **Step 3: Implement** as specified in Interfaces. `Mitigate` keeps its 75 clamp but names it `CombatRules.MaxResist`. `HitResolver.Resolve` calls `Mitigate(dmg, attack.Type, defender, defenderEffects)` (line 62). In `Enemy._PhysicsProcess` line 424: `Hp = Math.Min(MaxHp, Hp + tick.Amount * Effects.HealingMultiplier)`; line 425: `DamagePipeline.Mitigate(tick.Amount, tick.Type, Stats, Effects)`. `PartyController.cs:405`: pass `m.Effects`. Edit the five `.tres` files: `ResistPoison = 75`.
- [ ] **Step 4: Run to see them pass** — same filter → PASS; then `cd tests && dotnet test --nologo -v q` (whole suite: `StatusEffectsTests` may pin `IsDebuff` counts; update and say so in the commit).
- [ ] **Step 5: Build** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors.
- [ ] **Step 6: Commit** — `git add src/Combat/Core src/Characters/CharacterState.cs src/Enemies/Core/TelegraphRules.cs src/Enemies/Enemy.cs src/Party/PartyController.cs data/enemies && git commit -m "feat(combat): the Depths hit chance, Vulnerable, bleeding halves healing, frost slows a wind-up, resists capped in data"`

### Task 2: Combos in the resolver: Shatter, Ignite, Deep Freeze leaves Vulnerable; HitFlavor

**Files:**
- Create: `src/Combat/Core/FeedbackRules.cs` (the `HitFlavor` enum and `FeedbackRules.FlavorOf`; the rest of the file is Task 8)
- Modify: `src/Combat/Core/AttackProfile.cs:33` (`DamageResult` gains `Shatter`, `IgniteBurst`), `src/Combat/Core/HitResolver.cs:47-79`
- Modify: `src/Enemies/Enemy.cs:743-763` (`ReceiveAttack`: the Ignite burst around an enemy), `src/Party/Adventurer.cs:896-937` (the burst around a member), `src/Party/PartyController.cs:544-554` (`LogResult` says "shatters")
- Test: `tests/Combat/ComboTests.cs`

**Interfaces:**
- Consumes: Task 1's `CombatRules`, `StatusEffectController`.
- Produces: `public enum HitFlavor : byte { Hit, Crit, PerfectBlow, Shatter, Blocked, Bleed, Poison, Burn, Frost }`; `DamageResult` gains `public bool Shatter { get; init; }` and `public float IgniteBurst { get; init; }` (0 = none) as init properties (positional constructor unchanged, so `Breakable.cs` and tests still compile); `CombatRules` gains `ShatterMultiplier = 1.5f`, `IgniteRadius = 2f`, `IgniteFactor = 4f`; `HitResolver.Resolve` rules, in this order after the base roll and the heavy/crit/sneak multipliers: (a) **Shatter**: `(crit || ctx.Heavy) && defenderEffects.Has(Frost)` → `dmg *= ShatterMultiplier`, `defenderEffects.Remove(Frost)`, `Shatter = true`; (b) mitigation, block, shield as today; (c) on-hit statuses as today, plus **Ignite**: when an on-hit `Burn` is applied and the defender had no Burn before this hit and `Has(Poison)` → `IgniteBurst = poison.Magnitude * IgniteFactor`; **Deep Freeze**: when an on-hit `Stun` is applied by an attack of `AttackKind.Spell` → also `defenderEffects.Apply(new StatusApplication(StatusKind.Vulnerable, CombatRules.VulnerableSeconds, 0))` (the Mage's Deep Freeze is the only spell that stuns; elites and shrines apply Vulnerable directly). `FeedbackRules.FlavorOf(DamageResult r, bool perfectBlow)`: `!r.Hit` → `Hit`; `r.Shatter` → `Shatter`; `perfectBlow` → `PerfectBlow`; `r.Hit && r.Amount <= 0` → `Blocked` (a shield ate it); `r.Crit` → `Crit`; else `Hit`. `FeedbackRules.TickFlavor(StatusKind k)` → `Bleed/Poison/Burn` for those kinds, `Frost` otherwise.
- Engine glue: `Enemy.ReceiveAttack` after the hit lands: `if (result.IgniteBurst > 0) IgniteAround(attackerStats, result.IgniteBurst, attackerName)`: every other living `Enemy` within `IgniteRadius` of this one takes `ReceiveAttack(attackerStats, new AttackProfile { MinDamage = burst, MaxDamage = burst, Type = DamageType.Fire, Kind = AttackKind.Spell }, new HitContext { DefenderStunned = true }, rng, "the ignition")` and `HitFx.SpellBurst(GetParent(), GlobalPosition + Vector3.Up * 0.9f, new Color("#ff8a20"))`; the log says `"{DisplayName} ignites!"`. `Adventurer.ReceiveAttack` does the same for `Party.AdventurersWithin(GlobalPosition, IgniteRadius)` excluding itself, each through `ReceiveAttack(attackerStats, fireAttack, "the ignition")` (no attacker, so it cannot be dodged or parried). `PartyController.LogResult`: `verb = r.Shatter ? "shatters" : r.Sneak ? ...` (shatter first).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Combat/ComboTests.cs
public class ComboTests
{
    private static readonly AttackProfile Sword = new() { MinDamage = 10, MaxDamage = 10 };
    private static Stats Sure => TestClasses.Attacker(accuracy: 500);

    [Fact]
    public void Shatter_HeavyOnFrost_AddsHalf_AndConsumesTheFrost()
    {
        var fx = new StatusEffectController();
        fx.Apply(new StatusApplication(StatusKind.Frost, 5f, 0));
        var r = HitResolver.Resolve(Sure, TestClasses.Dummy(), fx, Sword, new HitContext { Heavy = true, HeavyMultiplier = 2f }, new Random(1));
        Assert.True(r.Shatter);
        Assert.Equal(30f, r.Amount, 3);           // 10 x 2 heavy x 1.5 shatter
        Assert.False(fx.Has(StatusKind.Frost));
        Assert.Equal(HitFlavor.Shatter, FeedbackRules.FlavorOf(r, perfectBlow: false));
    }

    [Fact]
    public void Shatter_NeedsFrost_AndAHeavyOrACrit()
    {
        var r = HitResolver.Resolve(Sure, TestClasses.Dummy(), new StatusEffectController(), Sword, new HitContext { Heavy = true, HeavyMultiplier = 2f }, new Random(1));
        Assert.False(r.Shatter); Assert.Equal(20f, r.Amount, 3);
        var fx = new StatusEffectController(); fx.Apply(new StatusApplication(StatusKind.Frost, 5f, 0));
        var light = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500, crit: 0), TestClasses.Dummy(), fx, Sword, HitContext.None, new Random(1));
        Assert.False(light.Shatter); Assert.True(fx.Has(StatusKind.Frost));
    }

    [Fact]
    public void Ignite_BurnOnAPoisonedTarget_Bursts_Once()
    {
        var fx = new StatusEffectController();
        fx.Apply(new StatusApplication(StatusKind.Poison, 6f, 3f));
        var torch = Sword with { Type = DamageType.Fire, Kind = AttackKind.Spell, OnHit = new[] { new StatusApplication(StatusKind.Burn, 3f, 2f) } };
        var first = HitResolver.Resolve(Sure, TestClasses.Dummy(), fx, torch, HitContext.None, new Random(1));
        Assert.Equal(12f, first.IgniteBurst, 3);  // poison magnitude 3 x 4
        var again = HitResolver.Resolve(Sure, TestClasses.Dummy(), fx, torch, HitContext.None, new Random(1));
        Assert.Equal(0f, again.IgniteBurst, 3);   // the Burn was already on it
        Assert.True(fx.Has(StatusKind.Poison));   // the poison keeps ticking
    }

    [Fact]
    public void Ignite_NeedsPoison()
    {
        var torch = Sword with { Kind = AttackKind.Spell, OnHit = new[] { new StatusApplication(StatusKind.Burn, 3f, 2f) } };
        Assert.Equal(0f, HitResolver.Resolve(Sure, TestClasses.Dummy(), new StatusEffectController(), torch, HitContext.None, new Random(1)).IgniteBurst);
    }

    [Fact]
    public void DeepFreeze_ASpellStun_LeavesVulnerable_AMeleeStunDoesNot()
    {
        var fx = new StatusEffectController();
        var frost = Sword with { Kind = AttackKind.Spell, Type = DamageType.Frost, OnHit = new[] { new StatusApplication(StatusKind.Stun, 0.7f, 0) } };
        HitResolver.Resolve(Sure, TestClasses.Dummy(), fx, frost, HitContext.None, new Random(1));
        Assert.True(fx.Has(StatusKind.Stun));
        Assert.Equal(CombatRules.VulnerableSeconds, fx.Get(StatusKind.Vulnerable)!.Remaining, 3);
        var slam = Sword with { OnHit = new[] { new StatusApplication(StatusKind.Stun, 1.2f, 0) } };
        var other = new StatusEffectController();
        HitResolver.Resolve(Sure, TestClasses.Dummy(), other, slam, HitContext.None, new Random(1));
        Assert.False(other.Has(StatusKind.Vulnerable));
    }

    [Fact]
    public void Flavor_ShieldedHit_ReadsBlocked_CritReadsCrit()
    {
        var shield = new StatusEffectController(); shield.Apply(new StatusApplication(StatusKind.Shield, 10f, 200f));
        var eaten = HitResolver.Resolve(Sure, TestClasses.Dummy(), shield, Sword, HitContext.None, new Random(1));
        Assert.True(eaten.Hit); Assert.Equal(0f, eaten.Amount, 3);
        Assert.Equal(HitFlavor.Blocked, FeedbackRules.FlavorOf(eaten, false));
        var crit = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500, crit: 1f), TestClasses.Dummy(), null, Sword, HitContext.None, new Random(1));
        Assert.Equal(HitFlavor.Crit, FeedbackRules.FlavorOf(crit, false));
        Assert.Equal(HitFlavor.PerfectBlow, FeedbackRules.FlavorOf(crit, perfectBlow: true)); // Shatter > PerfectBlow > Blocked > Crit > Hit
        Assert.Equal(HitFlavor.Burn, FeedbackRules.TickFlavor(StatusKind.Burn));
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter ComboTests` → build errors.
- [ ] **Step 3: Implement** the resolver rules, `HitFlavor`, `FlavorOf`, `TickFlavor`, then the Godot glue (`IgniteAround` on both bodies, the "shatters" verb).
- [ ] **Step 4: Run to see them pass** — `cd tests && dotnet test --nologo -v q --filter "ComboTests|HitResolverTests|StatusEffectsTests"` → PASS; build → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Combat/Core src/Enemies/Enemy.cs src/Party && git commit -m "feat(combat): Shatter, Ignite and Vulnerable from a deep freeze; every hit has a flavor"`

### Task 3: DefenseRules and DefenseState (engine-free)

**Files:**
- Create: `src/Combat/Core/DefenseRules.cs`
- Test: `tests/Combat/DefenseStateTests.cs`

**Interfaces:**
- Consumes: `AttackKind`.
- Produces:
```csharp
namespace DungeonCrawler.Combat;
public static class DefenseRules
{
    public const float DodgeSeconds = 0.25f, DodgeStamina = 20f, DodgeCooldown = 0.6f;
    public const float DodgeSpeed = 11f;                 // (tune) metres per second for the burst: 2.75 m
    public const float ParrySeconds = 0.25f, ParryStamina = 10f, ParryCooldown = 0.8f;
    public const float PerfectBlockSeconds = 0.2f;
    public const float BlockStamina = 12f;               // PartyController.BlockStaminaCost moves here; the old const stays and reads this
    public const float StaggerSeconds = 1.2f;
    public const float RemoteGraceSeconds = 0.1f;        // (tune) how long the server keeps a remote window open after the flag drops
}
public enum DefenseOutcome { None, Dodged, Parried, PerfectBlock, Blocked }
public sealed class DefenseState
{
    public float DodgeLeft { get; private set; }  public float DodgeCooldownLeft { get; private set; }
    public float ParryLeft { get; private set; }  public float ParryCooldownLeft { get; private set; }
    /// <summary>Seconds the guard has been held; -1 when not guarding.</summary>
    public float GuardHeld { get; private set; } = -1f;
    public bool Dodging => DodgeLeft > 0f;  public bool Parrying => ParryLeft > 0f;
    public bool InPerfectWindow => GuardHeld >= 0f && GuardHeld <= DefenseRules.PerfectBlockSeconds;
    public bool CanDodge(float stamina) => !Dodging && DodgeCooldownLeft <= 0f && stamina >= DefenseRules.DodgeStamina;
    public bool CanParry(float stamina) => !Parrying && ParryCooldownLeft <= 0f && stamina >= DefenseRules.ParryStamina;
    public bool TryDodge(float stamina);   // starts DodgeLeft = DodgeSeconds, DodgeCooldownLeft = DodgeCooldown; false when CanDodge is false
    public bool TryParry(float stamina);   // same with the parry numbers
    public void Update(float dt, bool guarding);  // timers count down; GuardHeld: -1 -> 0 on the first guarding tick, then += dt; back to -1 when not guarding
    public DefenseOutcome Judge(AttackKind kind, bool fromCreature, bool canBlock, bool guarding, float stamina);
    /// <summary>Server side, for a remote body: mirror the flags the client sent. Returns which windows began this call.</summary>
    public (bool dodgeStarted, bool parryStarted) ApplyRemote(bool dodgeFlag, bool parryFlag, float dt);
    public void CancelDodge(); public void CancelParry();   // the server refusing a start it cannot pay for
}
```
`Judge`, in order: `!fromCreature` → `None`; `Dodging` → `Dodged`; `kind == AttackKind.Melee && Parrying && !canBlock` → `Parried`; `canBlock && guarding && kind != AttackKind.Spell` → `InPerfectWindow ? PerfectBlock : stamina >= DefenseRules.BlockStamina ? Blocked : None`; else `None`. `ApplyRemote`: a rising `dodgeFlag` starts the dodge (`DodgeLeft = DodgeSeconds + RemoteGraceSeconds`, no cooldown check: the client already applied it) and reports `dodgeStarted`; while the flag stays up the window is held at least `RemoteGraceSeconds`; when it drops the window runs out on its own (`Update` still counts). Same for parry. Flags never extend a window past `Seconds + RemoteGraceSeconds` from the rise.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Combat/DefenseStateTests.cs
public class DefenseStateTests
{
    private static DefenseState Run(DefenseState d, float seconds, bool guarding = false, float step = 0.05f) { for (var t = 0f; t < seconds - 1e-4f; t += step) d.Update(step, guarding); return d; }

    [Fact]
    public void Dodge_LastsItsBurst_CostsStamina_AndCoolsDown()
    {
        var d = new DefenseState();
        Assert.False(d.TryDodge(DefenseRules.DodgeStamina - 1f));
        Assert.True(d.TryDodge(DefenseRules.DodgeStamina));
        Assert.True(d.Dodging);
        Run(d, 0.2f); Assert.True(d.Dodging);
        Run(d, 0.1f); Assert.False(d.Dodging);
        Assert.False(d.CanDodge(100f));                     // 0.3 s in: still cooling down
        Run(d, DefenseRules.DodgeCooldown - 0.3f + 0.05f); Assert.True(d.CanDodge(100f));
    }

    [Fact]
    public void TryParry_NeedsStamina_AndRespectsCooldown()
    {
        var d = new DefenseState();
        Assert.False(d.TryParry(DefenseRules.ParryStamina - 1f)); Assert.False(d.Parrying);
        Assert.True(d.TryParry(DefenseRules.ParryStamina)); Assert.True(d.Parrying);
        Run(d, DefenseRules.ParrySeconds + 0.05f); Assert.False(d.Parrying);
        Assert.False(d.TryParry(100f));                      // cooldown
        Run(d, DefenseRules.ParryCooldown); Assert.True(d.TryParry(100f));
    }

    [Fact]
    public void Judge_IgnoresHazards_WhateverTheState()
    {
        var d = new DefenseState(); d.TryDodge(100f); d.TryParry(100f); d.Update(0.01f, guarding: true);
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Melee, fromCreature: false, canBlock: true, guarding: true, stamina: 100f));
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Spell, fromCreature: false, canBlock: false, guarding: false, stamina: 100f));
    }

    [Fact]
    public void Judge_DodgeBeatsEverythingFromACreature()
    {
        var d = new DefenseState(); d.TryDodge(100f);
        Assert.Equal(DefenseOutcome.Dodged, d.Judge(AttackKind.Melee, true, canBlock: true, guarding: true, stamina: 100f));
        Assert.Equal(DefenseOutcome.Dodged, d.Judge(AttackKind.Ranged, true, false, false, 0f));
        Assert.Equal(DefenseOutcome.Dodged, d.Judge(AttackKind.Spell, true, false, false, 0f));
    }

    [Fact]
    public void Judge_ParryIsMeleeOnly_AndOnlyForClassesThatCannotBlock()
    {
        var d = new DefenseState(); d.TryParry(100f);
        Assert.Equal(DefenseOutcome.Parried, d.Judge(AttackKind.Melee, true, canBlock: false, guarding: false, 0f));
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Ranged, true, canBlock: false, guarding: false, 0f));
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Spell, true, canBlock: false, guarding: false, 0f));
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Melee, true, canBlock: true, guarding: false, 100f)); // a Warrior's right mouse is a guard, never a parry
    }

    [Fact]
    public void PerfectBlock_OnlyInTheFirstWindow_ThenOrdinaryBlock()
    {
        var d = new DefenseState();
        d.Update(0.05f, guarding: true);
        Assert.Equal(DefenseOutcome.PerfectBlock, d.Judge(AttackKind.Melee, true, canBlock: true, guarding: true, stamina: 0f)); // no stamina needed
        Run(d, DefenseRules.PerfectBlockSeconds + 0.05f, guarding: true);
        Assert.Equal(DefenseOutcome.Blocked, d.Judge(AttackKind.Melee, true, true, true, DefenseRules.BlockStamina));
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Melee, true, true, true, DefenseRules.BlockStamina - 1f));
        Assert.Equal(DefenseOutcome.None, d.Judge(AttackKind.Spell, true, true, true, 100f));                       // spells ignore the shield
        d.Update(0.05f, guarding: false); d.Update(0.05f, guarding: true);                                             // let go and raise again: a new window
        Assert.Equal(DefenseOutcome.PerfectBlock, d.Judge(AttackKind.Ranged, true, true, true, 0f));
    }

    [Fact]
    public void Remote_FlagTailGrace_ThenEnds()
    {
        var d = new DefenseState();
        var (dodge, parry) = d.ApplyRemote(dodgeFlag: false, parryFlag: true, 0.033f);
        Assert.False(dodge); Assert.True(parry); Assert.True(d.Parrying);
        for (var i = 0; i < 6; i++) Assert.False(d.ApplyRemote(false, true, 0.033f).parryStarted);  // the flag held: no second start
        Run(d, DefenseRules.ParrySeconds - 0.033f * 7 + 0.05f);                                     // the client's window is over...
        Assert.True(d.Parrying);                                                                     // ...but the grace keeps it open on the server
        Run(d, DefenseRules.RemoteGraceSeconds);
        Assert.False(d.Parrying);                                                                    // and nothing holds it past the grace
        Assert.True(d.ApplyRemote(false, false, 0.033f) == (false, false));
        Assert.True(d.ApplyRemote(true, false, 0.033f).dodgeStarted); d.CancelDodge(); Assert.False(d.Dodging);
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter DefenseStateTests` → build errors.
- [ ] **Step 3: Implement `DefenseRules`, `DefenseOutcome`, `DefenseState`** as specified. `PartyController.BlockStaminaCost` (line 27) becomes `= DefenseRules.BlockStamina`.
- [ ] **Step 4: Run to see them pass** — same filter → PASS.
- [ ] **Step 5: Commit** — `git add src/Combat/Core/DefenseRules.cs src/Party/PartyController.cs && git commit -m "feat(combat): dodge, parry and perfect-block windows as engine-free DefenseState"`

### Task 4: The verbs in the world: dodge, parry, perfect block, stagger; the new EventBus events

**Files:**
- Modify: `src/Party/Control/Control.cs:19-30` (`InputIntent.DodgePressed`, `ParryPressed`), `:35-48` (`LocalInput.Read`), `:52-61` (`RemotePose.Parry`, `Dodge`)
- Modify: `project.godot` `[input]` (a `dodge` action, Left Alt), `src/Core/Settings.cs:81-88` (`("dodge", "Dodge")` appended to `Rebindable`)
- Modify: `src/Core/EventBus.cs` (six events, see Interfaces)
- Modify: `src/Party/Adventurer.cs` (`Defense`, the local branch of `_PhysicsProcess:341-357`, `RemoteTick:451-462`, `ReceiveAttack:896-937`, `MeleeSwing:849-866`, `ApplyTrap:939`, `OnHardLanding:519`, `FellOut:508`, `DebugIntent`)
- Modify: `src/Party/PartyController.cs:377` (`local.Intent = local.DebugIntent ?? LocalInput.Read(...)`)
- Modify: `src/Enemies/Enemy.cs` (`Stagger`, `Staggered`, `StaggerImmune`, the stagger gate at `:434`), `src/Combat/Projectile.cs:124-132` (a dodging body is passed through)
- Modify: `src/Party/FirstPersonWeapon.cs` (`PlayParry()`: a quick outward flick, 0.25 s)

**Interfaces:**
- Consumes: Task 3 `DefenseState`, Task 2 `FeedbackRules`.
- Produces:
  - `InputIntent`: `public bool DodgePressed; public bool ParryPressed;` — `LocalInput.Read`: `DodgePressed = live && Input.IsActionJustPressed("dodge")`, `ParryPressed = live && Input.IsActionJustPressed("block")` (the same right mouse: a blocking class guards, the others parry; `Adventurer` decides by `State.Class.CanBlock`). `RemotePose`: `public bool Parry; public bool Dodge;`.
  - `project.godot`: after `block={...}` add `dodge={ "deadzone": 0.2, "events": [Object(InputEventKey, ... "physical_keycode":4194328 ...)] }` (copy the `crouch` entry's `InputEventKey` text and change the keycode; 4194328 is `KEY_ALT`). `Settings.Rebindable` gains `("dodge", "Dodge")` after `("crouch", "Crouch")`.
  - `EventBus` (exact contract shapes): `public event Action<int>? PerfectBlow; public void RaisePerfectBlow(int member)`; `Action<int>? Parried; RaiseParried(int member)`; `Action<int>? PerfectBlock; RaisePerfectBlock(int member)`; `Action<int>? MemberFell; RaiseMemberFell(int member)`; `Action<int>? TrapSprung; RaiseTrapSprung(int member)`; `Action<int>? LowHp; RaiseLowHp(int member)`.
  - `Adventurer`: `public DefenseState Defense { get; } = new();` `public bool Dodging => Defense.Dodging; public bool Parrying => Defense.Parrying;` `public InputIntent? DebugIntent { get; set; }` (a flow's stand-in for the keyboard); `public float? ForceDodgeRoll { get; set; }` (Task 7 uses it); `public int DodgesMade { get; private set; }`.
    - Local branch: `Defense.Update(dt, Guarding)` every tick (also in `RemoteTick` and for companions); `if (input.DodgePressed && Defense.TryDodge(State.Stamina)) StartDodge(wish == Vector3.Zero ? Transform.Basis.Z : wish)` where `StartDodge(Vector3 dir)`: `_dodgeDir = dir.Normalized()`, `DodgesMade++`, stamina `-= DefenseRules.DodgeStamina` unless `Net.NetSession.IsClient` (the server pays from the flag), `EventBus.PlaySfx("swing")` quietly via `Audio.AudioManager.Instance?.Play("footstep", -4f)`; while `Dodging` the movement target is `_dodgeDir * DefenseRules.DodgeSpeed` (sprint, crouch and guard ignored); `if (input.ParryPressed && !State.Class.CanBlock && Defense.TryParry(State.Stamina)) { stamina -= ParryStamina (not on a client); _fpWeapon.PlayParry(); }`.
    - `RemoteTick`: `var (d, p) = Defense.ApplyRemote(pose.Dodge, pose.Parry, dt); if (d) { if (State.Stamina >= DodgeStamina) State.Stamina -= DodgeStamina; else Defense.CancelDodge(); } if (p) { same with parry, and a parry is refused when State.Class.CanBlock }`.
    - `ReceiveAttack(Stats attackerStats, AttackProfile attack, string attackerName, ICombatTarget? attacker = null)`: first `var outcome = Defense.Judge(attack.Kind, attacker != null, m.Class.CanBlock, Guarding, m.Stamina);` then:
      `Dodged` → `Log($"{m.Name} dodges {attackerName}.")`, `PlaySfx("miss")`, return.
      `Parried` → `Log($"{m.Name} parries {attackerName}!")`, `PlaySfx("block")`, `RaiseParried(Index)`, `StaggerAttacker(attacker)`, `Riposte(attacker)`, return.
      `PerfectBlock` → `Log($"{m.Name} perfectly blocks {attackerName}!")`, `PlaySfx("block")`, `RaisePerfectBlock(Index)`, `StaggerAttacker(attacker)`, `SecondWind()` then `Riposte(attacker)`, return (no stamina, no damage, `_flash` untouched).
      `Blocked` → the existing path with `DefenderBlocking = true` (stamina −12, SecondWind, Riposte as today, lines 904, 920-930).
      `None` → the existing path with `DefenderBlocking = false`.
      Helpers (private): `StaggerAttacker(ICombatTarget? a) { if (a is Enemy e) e.Stagger(DefenseRules.StaggerSeconds, fromDefense: true); }`; `Riposte(ICombatTarget? a)` = the existing lines 923-930 as a method; `SecondWind()` = line 920 as a method. After `m.TakeDamage` (line 905) and in `ReceiveHazard` (line 968): `if (m.IsAlive && m.Hp < m.Stats[Stat.MaxHp] * 0.25f && !_lowHpSaid) { _lowHpSaid = true; EventBus.Instance.RaiseLowHp(Index); }`; `_lowHpSaid` resets in a `FloorChanged` handler subscribed in `_Ready`, unsubscribed in `_ExitTree`.
    - `MeleeSwing`: after `Party.OnHitLanded(m, target, result)`: `if (result.Hit && target is Enemy e2) { if (heavy || perfect) e2.Stagger(e2.HitReactSeconds, fromDefense: false); }` and `if (perfect && result.Hit) EventBus.Instance.RaisePerfectBlow(Index);` (once per swing, before the loop ends: track `var perfectSaid`).
    - `ApplyTrap`: first line `EventBus.Instance.RaiseTrapSprung(Index);`. `OnHardLanding` and `FellOut`: `EventBus.Instance.RaiseMemberFell(Index);` before the damage.
  - `Enemy`: `public bool Staggered => _staggerLeft > 0f; public bool StaggerImmune { get; set; }` (false; the elites plan sets it for Juggernaut); `public float HitReactSeconds => ClipLength("HitReact") ?? TelegraphRules.DefaultHitReactSeconds` (`ClipLength(string key)`: the first clip of `Clips[key]` the model has, its `Length`, else null); `public void Stagger(float seconds, bool fromDefense)`: `if (!IsAlive || StaggerImmune) return; if (IsBoss && !fromDefense) return; _staggerLeft = Math.Max(_staggerLeft, seconds); PlayAnim("HitReact", force: true); WindupLeft = -1f; if (State is SignatureState) ChangeState(new ChaseState()); else if (State is AttackState or ShootState) State.Enter(this);` (Enter resets the wind-up timers; the attack cycle starts over after the stagger). `_PhysicsProcess` line 434: `if (IsAlive && (Effects.IsStunned || _staggerLeft > 0f)) { _staggerLeft -= dt; MoveToward(GlobalPosition, dt); } else State.Update(this, dt);`.
  - `Projectile.OnBodyEntered`, enemy branch: `if (body is Adventurer adv) { if (adv.Dodging) return; ... }` (the bolt is not spent and flies on; `Sweep` then moves it to `to`).
  - `FirstPersonWeapon.PlayParry()`: a tween like the light swing but mirrored and 0.25 s total.

- [ ] **Step 1: Implement** (Godot-side; the engine-free rules are tested in Task 3; Task 11's flow pins this task: steps 1-9).
- [ ] **Step 2: Build** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors.
- [ ] **Step 3: Smoke** — `timeout 60 godot --headless --path . -- --seed=7 --quit-after=6 --no-ambush --no-portals --no-gathering --lethality=1 2>&1 | grep -E "ERROR|SCRIPT" ` → nothing; `cd tests && dotnet test --nologo -v q` → PASS.
- [ ] **Step 4: Commit** — `git add project.godot src/Core/Settings.cs src/Core/EventBus.cs src/Party src/Enemies/Enemy.cs src/Combat/Projectile.cs && git commit -m "feat(combat): dodge on Left Alt, parry on right mouse for the classes that cannot block, perfect blocks, and the stagger they earn"`

### Task 5: The dead Parry node lives; Thorns on Bulwark tier 2

**Files:**
- Modify: `src/Characters/Defs/SkillDef.cs:37-40,56` (`Trigger2`, `Trigger2Magnitude`), `src/Characters/Skills/SkillProfile.cs:54-55` (`Trigger2`, `Trigger2Magnitude`), `src/Characters/Skills/SkillRules.cs:146-147` (`TriggerMagnitude` sums both)
- Modify data: `data/skills/warrior_bulwark_2_second_wind.tres` (`Trigger2 = "Thorns"`, `Trigger2Magnitude = 4`, description "Blocking a hit restores 10 stamina; attackers who hit you take 4 damage."), `data/skills/rogue_duelist_3_parry.tres` (description "A parry counters for 50% weapon damage.")
- Test: `tests/Characters/SkillRulesTests.cs` (add)

**Interfaces:**
- Produces: `SkillProfile.Trigger2` (`TriggerKind`, default `None`), `SkillProfile.Trigger2Magnitude` (float); `SkillDef.Trigger2` (string, "None"), `SkillDef.Trigger2Magnitude`; `SkillRules.TriggerMagnitude(c, cat, kind)` = the sum over learned skills of `TriggerMagnitude` where `Trigger == kind` plus `Trigger2Magnitude` where `Trigger2 == kind`. The parry itself fires `Riposte` through Task 4's `ReceiveAttack` (`Parried` → `Riposte(attacker)`), which is what makes `skill.rogue.duelist.parry` live; nothing in the data changes but its text.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void TriggerMagnitude_SumsASecondTrigger()
{
    var cat = new SkillCatalog();
    cat.Skills["skill.test.wind"] = new SkillProfile { Id = "skill.test.wind", ClassId = "class.warrior", Trigger = TriggerKind.SecondWind, TriggerMagnitude = 10, Trigger2 = TriggerKind.Thorns, Trigger2Magnitude = 4 };
    var c = CharacterState.Create("Brand", TestClasses.Warrior, Rank.Front);
    c.LearnedSkills.Add("skill.test.wind");
    Assert.Equal(10f, SkillRules.TriggerMagnitude(c, cat, TriggerKind.SecondWind), 3);
    Assert.Equal(4f, SkillRules.TriggerMagnitude(c, cat, TriggerKind.Thorns), 3);
    Assert.Equal(0f, SkillRules.TriggerMagnitude(c, cat, TriggerKind.Riposte), 3);
}
```

- [ ] **Step 2: Run to see it fail** — `cd tests && dotnet test --nologo -v q --filter SkillRulesTests` → build error on `Trigger2`.
- [ ] **Step 3: Implement** the three code changes and the two data edits.
- [ ] **Step 4: Run to see it pass**; build → 0 errors; `timeout 60 godot --headless --path . -- --test-skills 2>&1 | grep RESULT` → still ok.
- [ ] **Step 5: Commit** — `git add src/Characters data/skills && git commit -m "feat(skills): Second Wind also grows thorns; the Parry node now describes a parry"`

### Task 6: Telegraphs: wind-up clips, the white tint, the hand glow, the signature decal

**Files:**
- Modify: `src/Enemies/Core/TelegraphRules.cs` (the rest of the constants)
- Create: `src/Combat/Fx/TelegraphDecal.cs`, `src/Combat/Fx/TelegraphFx.cs` (`HandGlow`)
- Modify: `src/Enemies/Ai/AiState.cs:57-87` (`AttackState`), `:89-121` (`ShootState`), `:123-166` (`SignatureState`)
- Modify: `src/Enemies/Enemy.cs:323-345` (`PlayAnim` speed, `UpdateAnimation` keeps the wind-up speed), `:440-461` (`UpdateTint` white for the last 0.2 s), `:653-681` (`StrikeParty`/`FireAtParty` no longer start the clip), `:683-702` (`TrySignature`, `AnnounceSignature`), new members below
- Modify: `src/Core/EventBus.cs` (`EnemyTelegraph`)
- Test: `tests/Enemies/TelegraphRulesTests.cs`

**Interfaces:**
- Produces (`TelegraphRules`, `namespace DungeonCrawler.Enemies`):
```csharp
public const float SlamDelay = 1.1f, VolleyDelay = 1.1f, SummonDelay = 1.1f, ChargeDelay = 0.7f;   // (tune) was 0.8 / 0.4
public const float SignatureRecover = 0.8f;      // the state ends Delay + Recover after it began (was 1.6 total)
public const float ChargeRun = 1.6f;             // a charge gives up this long after its wind-up (was 2.0 total)
public const float TintSeconds = 0.2f;
public const float StrikeFraction = 0.55f;       // (tune) where the blow falls in the attack clip
public const float SlamRadius = 4.5f, ChargeLaneLength = 9f, ChargeLaneWidth = 1.6f, PulseRadius = 1.5f;
public const float DefaultHitReactSeconds = 0.6f;
public static float Delay(Signature s) => s == Signature.Charge ? ChargeDelay : s == Signature.Volley ? VolleyDelay : s == Signature.Summon ? SummonDelay : SlamDelay;
public static float ClipSpeed(float clipSeconds, float windupSeconds) => clipSeconds * StrikeFraction / Math.Max(0.05f, windupSeconds);
public static float TintAmount(float windupLeft) => windupLeft < 0f ? 0f : Math.Clamp(1f - windupLeft / TintSeconds, 0f, 1f);
```
- `Enemy`: `public float WindupLeft { get; private set; } = -1f;` `public int TelegraphId { get; private set; } = -1;` (counts signatures; -1 when none is telegraphed) `public float TelegraphRemaining { get; private set; } = -1f;` `public string CurrentAnim => _currentAnim;` `public void PlayAnim(string name, bool force = false, float speed = 1f)` (sets `_anim.SpeedScale = speed`; `UpdateAnimation` line 338 keeps the scale of a playing one-shot instead of forcing 1); `public void BeginWindup(float seconds)`: `WindupLeft = seconds; PlayAnim("Punch", force: true, TelegraphRules.ClipSpeed(ClipLength("Punch") ?? seconds / TelegraphRules.StrikeFraction, seconds)); if (Kind != EnemyKind.Melee) TelegraphFx.HandGlow(this, seconds); if (!Net.NetSession.IsClient) EventBus.Instance.RaiseEnemyTelegraph(this, State.Name);` `public void BeginSignatureTelegraph(Signature sig)`: `TelegraphId++; TelegraphRemaining = TelegraphRules.Delay(sig); TelegraphDecal.Show(this, sig, TelegraphRemaining); AnnounceSignature(); if (!IsClient) RaiseEnemyTelegraph(this, State.Name)`; `public void EndTelegraph()`: `TelegraphRemaining = -1f; TelegraphDecal.Hide(this);` `public void ClientTelegraph(string state)` (client only): `"Attack"`/`"Shoot"` → `BeginWindup(WindupSeconds)`; a name starting `"Signature:"` → `BeginSignatureTelegraph(Enum.Parse<Signature>(name[10..]))`. `_PhysicsProcess` and `ClientTick`: `if (WindupLeft > 0f) WindupLeft -= dt; if (TelegraphRemaining > 0f) TelegraphRemaining -= dt;`. `UpdateTint`: `tint = tint.Lerp(Colors.White, TelegraphRules.TintAmount(WindupLeft))` before the status tints are applied to `_material`/`_modelMaterials`. `StrikeParty` no longer calls `PlayAnim` (the clip is running); `FireAtParty() => Fire(Attack, 0f, playClip: false)` and `Fire(attack, yaw, bool playClip = true)` plays only when asked (Volley keeps it). `public void ForceSignature()` (debug): `_signatureTimer = 0f; TrySignature();`.
- `AiState`: `AttackState.Enter` → `enemy.BeginWindup(enemy.WindupSeconds)`; the cycle reset at line 83 (`_timer = 0f; _swung = false;`) also calls `enemy.BeginWindup(enemy.WindupSeconds)`; `ShootState` the same (Enter and line 117). `SignatureState.Enter`: `enemy.BeginSignatureTelegraph(_sig)` replaces `AnnounceSignature()`; `Update`: Slam/Volley/Summon perform at `TelegraphRules.Delay(_sig)` and leave at `Delay + SignatureRecover`; Charge winds up for `ChargeDelay` and gives up at `ChargeDelay + ChargeRun`; `PerformSignature` is preceded by `enemy.EndTelegraph()`; `SignatureState.Exit` → `enemy.EndTelegraph()` (a stagger mid-telegraph removes the decal).
- `EventBus`: `public event Action<Enemies.Enemy, string>? EnemyTelegraph; public void RaiseEnemyTelegraph(Enemies.Enemy enemy, string state)` (new; Task 10 relays it).
- `TelegraphDecal : Node3D` (`namespace DungeonCrawler.Combat.Fx`): `public static void Show(Enemy boss, Signature sig, float seconds)` adds one child named `TelegraphDecal` to the boss's parent, following `boss.GlobalPosition` each frame at floor height + 0.03 m, unshaded, `Transparency = Alpha`, red `#e03030`: Slam → a `CylinderMesh` disc radius `SlamRadius` (alpha 0.18) plus a `TorusMesh` ring (inner `SlamRadius − 0.2`, outer `SlamRadius`, scaled 0.1 in Y, alpha 0.7); Charge → a `BoxMesh` lane `ChargeLaneWidth × 0.02 × ChargeLaneLength` from the boss toward `boss.TargetPosition`, direction fixed at `Show`; Volley/Summon → a disc radius `PulseRadius` whose alpha pulses `0.25 + 0.2 * sin(t * 8)`. All free themselves at `seconds` or on `Hide(boss)`. `TelegraphFx.HandGlow(Enemy e, float seconds)`: an `OmniLight3D` (range 2, energy 1.5, the profile colour lightened 0.5) and a small emissive `SphereMesh` (radius 0.08) at `Up * 1.3f * Scale − Basis.Z * 0.4f`, children of the enemy, freed after `seconds`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Enemies/TelegraphRulesTests.cs
public class TelegraphRulesTests
{
    [Fact] public void Delays_AreTheDepthsNumbers() { Assert.Equal(1.1f, TelegraphRules.Delay(Signature.Slam)); Assert.Equal(1.1f, TelegraphRules.Delay(Signature.Volley)); Assert.Equal(1.1f, TelegraphRules.Delay(Signature.Summon)); Assert.Equal(0.7f, TelegraphRules.Delay(Signature.Charge)); }
    [Fact] public void ClipSpeed_PutsTheStrikeFrameAtTheEndOfTheWindup() { Assert.Equal(1.1f, TelegraphRules.ClipSpeed(1f, 0.5f), 3); Assert.Equal(0.55f, TelegraphRules.ClipSpeed(1f, 1f), 3); Assert.True(TelegraphRules.ClipSpeed(1f, 0f) < 12f); }
    [Fact] public void Tint_OnlyInTheLastFifthOfASecond() { Assert.Equal(0f, TelegraphRules.TintAmount(0.5f)); Assert.Equal(0f, TelegraphRules.TintAmount(0.2f), 3); Assert.Equal(0.5f, TelegraphRules.TintAmount(0.1f), 3); Assert.Equal(1f, TelegraphRules.TintAmount(0f), 3); Assert.Equal(0f, TelegraphRules.TintAmount(-1f)); }
    [Fact] public void SlamRadius_MatchesTheSlam() => Assert.Equal(4.5f, TelegraphRules.SlamRadius);
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter TelegraphRulesTests` → build errors.
- [ ] **Step 3: Implement** the rules, then the Godot side as specified (use `TelegraphRules.SlamRadius` in `PerformSignature` line 710 in place of the literal 4.5f).
- [ ] **Step 4: Run to see them pass**; build → 0 errors; `cd tests && dotnet test --nologo -v q` PASS.
- [ ] **Step 5: Commit** — `git add src/Enemies src/Combat/Fx src/Core/EventBus.cs && git commit -m "feat(enemies): attacks wind up in the open: the clip from the start, a white flash before the blow, a glow at a shooter's hand, and a red decal under a boss signature"`

### Task 7: Companions dodge a telegraphed signature

**Files:**
- Modify: `src/Characters/Companions/CompanionBrain.cs:14-48` (context, `ActionKind.Dodge`), `:59-85` (`Decide`)
- Modify: `src/Party/Adventurer.cs:640-702` (`Think`: the telegraph fields, the Dodge action)
- Test: `tests/Characters/CompanionBrainTests.cs` (add)

**Interfaces:**
- Consumes: `Enemy.TelegraphId`, `Enemy.TelegraphRemaining` (Task 6), `Adventurer.Defense`, `StartDodge` (Task 4), `Party.Rng`.
- Produces: `CompanionContext` gains `public int TelegraphId { get; init; } = -1; public float TelegraphRemaining { get; init; } = -1f; public float TelegraphDistance { get; init; } = float.MaxValue; public int LastTelegraphDecided { get; init; } = -1; public bool DodgeReady { get; init; }; public float DodgeRoll { get; init; } = 1f;` `ActionKind.Dodge` (appended). `CompanionBrain`: `public const float DodgeChance = 0.5f; public const float DodgeLead = 0.35f; public const float DodgeReach = 6f;` `public static bool DodgeDecisionDue(CompanionContext ctx) => ctx.TelegraphId >= 0 && ctx.TelegraphId != ctx.LastTelegraphDecided && ctx.TelegraphRemaining >= 0f && ctx.TelegraphRemaining <= DodgeLead && ctx.TelegraphDistance <= DodgeReach && ctx.Stance != Stance.Hold;` `Decide`: right after the potion and rescue lines: `if (DodgeDecisionDue(ctx) && ctx.DodgeReady && ctx.DodgeRoll < DodgeChance) return new CompanionAction(ActionKind.Dodge);` (a lost roll falls through to the usual fight).
- `Adventurer.Think`: find `var tele = enemies' sources .Where(e => e.TelegraphId >= 0).OrderBy(distance).FirstOrDefault()`; fill the six fields (`DodgeRoll = ForceDodgeRoll ?? (float)Party.Rng.NextDouble()`, `DodgeReady = Defense.CanDodge(State.Stamina)`); after `Decide`: `if (CompanionBrain.DodgeDecisionDue(ctx)) _lastTelegraphDecided = ctx.TelegraphId;` (one roll per signature, win or lose); `case ActionKind.Dodge: StartDodge(away from the telegraphing enemy, flat); _action = CompanionAction.Idle; break;`.

- [ ] **Step 1: Write the failing tests**

```csharp
private static CompanionContext Telegraphed(float roll, Stance stance = Stance.Defensive, float remaining = 0.3f, float distance = 3f, int last = -1, bool ready = true)
    => new() { TelegraphId = 4, TelegraphRemaining = remaining, TelegraphDistance = distance, LastTelegraphDecided = last, DodgeRoll = roll, DodgeReady = ready, Stance = stance,
               Enemies = new[] { new EnemyView(0, distance, true, true, 1f) }, Abilities = new[] { V(0, Basic, 1.8f) } };

[Fact] public void Companion_Dodges_ATelegraphedSignature_OnAWinningRoll() => Assert.Equal(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0.3f)).Kind);
[Fact] public void Companion_FightsOn_WhenTheRollLoses() => Assert.NotEqual(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0.7f)).Kind);
[Fact] public void Companion_OnHold_NeverDodges() => Assert.NotEqual(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0.0f, Stance.Hold)).Kind);
[Fact] public void Companion_Aggressive_Dodges() => Assert.Equal(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0.0f, Stance.Aggressive)).Kind);
[Fact] public void Companion_WaitsForTheLastMoment() { Assert.NotEqual(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0f, remaining: 0.9f)).Kind); Assert.False(CompanionBrain.DodgeDecisionDue(Telegraphed(0f, remaining: 0.9f))); }
[Fact] public void Companion_DecidesOncePerSignature() => Assert.False(CompanionBrain.DodgeDecisionDue(Telegraphed(0f, last: 4)));
[Fact] public void Companion_OutOfReach_OrOutOfStamina_DoesNotDodge() { Assert.NotEqual(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0f, distance: 9f)).Kind); Assert.NotEqual(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0f, ready: false)).Kind); }
[Fact] public void Companion_NoTelegraph_NoDodge() => Assert.NotEqual(ActionKind.Dodge, CompanionBrain.Decide(Telegraphed(0f) with { TelegraphId = -1 }).Kind);
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter CompanionBrainTests` → build errors.
- [ ] **Step 3: Implement** the brain, then the `Think` wiring.
- [ ] **Step 4: Run to see them pass**; build → 0 errors.
- [ ] **Step 5: Commit** — `git add src/Characters/Companions/CompanionBrain.cs src/Party/Adventurer.cs && git commit -m "feat(companions): half the time a companion dodges the signature it saw coming"`

### Task 8: Damage numbers, hit marker, enemy bars; the "Damage numbers" setting

**Files:**
- Modify: `src/Combat/Core/FeedbackRules.cs` (constants, colours, `DamageNumberMerger`)
- Modify: `src/Core/EventBus.cs:82,157` (`EnemyHurt` carries the flavor and the striking member)
- Modify: `src/Enemies/Enemy.cs:743-763` (`ReceiveAttack` raises with flavor and member), `:420-429` (status ticks raise `EnemyHurt`), `:824-836` (`ReceiveHazard` raises `Hit`, member -1), `:365-372` (`ClientHurt(float amount, float hp, HitFlavor flavor, int byMember)` raises `EnemyHurt` on the client)
- Modify: `src/Net/ServerWorld.cs:58`, `src/Net/ClientWorld.cs:87` (compile only: the new signature; Task 10 finishes the wire)
- Create: `src/UI/Hud/DamageNumbers.cs`, `src/UI/Hud/HitMarker.cs`, `src/UI/Hud/EnemyBars.cs`
- Modify: `src/UI/Hud/Hud.cs:62-176` (create, subscribe, free), `src/Core/Settings.cs` (`DamageNumbers`), `src/UI/Menus/SettingsScreen.cs:74-77` (a check under Display)
- Test: `tests/Combat/FeedbackRulesTests.cs`

**Interfaces:**
- Produces (`FeedbackRules`): `public const float NumberSeconds = 0.9f, MergeSeconds = 0.15f, CritScale = 1.6f, TickScale = 0.7f, BarLingerSeconds = 4f, BarHeight = 1.2f, HitMarkerSeconds = 0.12f, HeavyHitFraction = 0.15f, ShakeAmplitude = 0.06f, ShakeSeconds = 0.3f, VignetteSeconds = 0.5f, LevelUpPulseSeconds = 2f, BannerSeconds = 2.5f, ToastSeconds = 3f;` `public static string ColorHex(HitFlavor f)` (Hit `#ffffff`; Crit/PerfectBlow/Shatter `#ffd24a`; Blocked `#9a9a9a`; Bleed `#e03030`; Poison `#50d050`; Burn `#ff8a20`; Frost `#60a8ff`); `public static float Scale(HitFlavor f)` (`CritScale` for Crit/PerfectBlow/Shatter, `TickScale` for the four ticks, 1 otherwise); `public static bool IsTick(HitFlavor f)`; `public static string Text(HitFlavor f, float amount)` (`"Blocked"`, `"Shatter {amount:0}"`, else `"{amount:0}"`); `public static int BarPoolSize = 12, NumberPoolSize = 24` (consts).
  `public sealed class DamageNumberMerger { public sealed class Entry { public int Target; public float Amount; public HitFlavor Flavor; public float Born; public float Age; public bool Dirty; } public IReadOnlyList<Entry> Entries; public Entry Add(int target, float amount, HitFlavor flavor, float now); public void Update(float dt); }`: `Add` merges into the newest entry of that target when `now - entry.Born <= MergeSeconds` and `IsTick(flavor) == IsTick(entry.Flavor)` and neither is `Blocked` (sum the amounts, keep the louder flavor by the order Shatter > PerfectBlow > Crit > Hit, mark `Dirty`), else appends; `Update` ages entries and drops those past `NumberSeconds`.
- `EventBus`: `public event Action<Enemies.Enemy, float, Combat.HitFlavor, int>? EnemyHurt;` `public void RaiseEnemyHurt(Enemies.Enemy enemy, float amount, Combat.HitFlavor flavor, int byMember)` (replaces the bool overload; `byMember` is the party index of the striker or -1). `Enemy.ReceiveAttack`: `var by = Party == null ? -1 : Array.FindIndex(Party.Members, x => x.Name == attackerName)` (the same name lookup `ServerWorld.cs:73` uses for barks; "Brand's thorns" and "the ignition" give -1), flavor `FeedbackRules.FlavorOf(result, ctx.PerfectBlow)`: add `public bool PerfectBlow { get; init; }` to `HitContext` (`AttackProfile.cs:20-31`) and set it in `Adventurer.MeleeSwing` beside the 1.25 multiplier. Status ticks (line 425): `RaiseEnemyHurt(this, mitigated, FeedbackRules.TickFlavor(tick.Kind), -1)`. `ReceiveHazard`: `HitFlavor.Hit, -1`. `ClientHurt(amount, hp, flavor, byMember)`: as today plus `EventBus.Instance.RaiseEnemyHurt(this, amount, flavor, byMember)`.
- `Settings.DamageNumbers` (bool, true; `[display] damage_numbers`); `SettingsScreen`: `Check(v, "Damage numbers over enemies", Settings.DamageNumbers, x => Settings.DamageNumbers = x);` after the VSync check.
- `DamageNumbers : Node3D` (`namespace DungeonCrawler.UI.Hud`): a pool of `NumberPoolSize` `Label3D` (billboard, `NoDepthTest`, `FixedSize = false`, font size 48, outline 8), `public int ActiveCount`, `public void Show(Enemy e, float amount, HitFlavor flavor)` (ignored when `!Settings.DamageNumbers`): merges through a `DamageNumberMerger` keyed by `e.NetId` (dynamic ids are unique per floor), each entry a label at `e.GlobalPosition + Up * (1.8f * e.Profile.Scale + 0.2f)` rising 0.6 m over `NumberSeconds` and fading; colour `ColorHex`, `PixelSize` × `Scale(flavor)`; text `Text(flavor, amount)`. Subscribed to `EventBus.EnemyHurt` by the Hud.
- `HitMarker : Control` drawn in `_Draw`: four 10 px ticks around the crosshair that flick 6 px outward over `HitMarkerSeconds`; `public bool Active`; `public void Flick(bool kill)` (kill: a red X for 0.25 s). The Hud calls `Flick(e.Hp <= 0)` on `EnemyHurt` when `byMember == _party.LocalIndex`.
- `EnemyBars : Node3D`: a pool of `BarPoolSize` bar nodes (two unshaded billboard `QuadMesh` 0.9 × 0.08: dark back, red fill scaled by `Hp/MaxHp`, `NoDepthTest`; a `Label3D` name shown when `e.IsBoss`), `public bool Showing(Enemy e)`; on `EnemyHurt` for a non-horde living enemy records `_lastHurt[e] = now`; `_Process` places a bar at `e.GlobalPosition + Up * (0.9f * e.Profile.Scale + FeedbackRules.BarHeight)` for every tracked enemy hurt within `BarLingerSeconds`, drops dead or invalid ones.
- `Hud._Ready`: `_numbers = new DamageNumbers(); _bars = new EnemyBars(); Main.Instance.AddChild(_numbers); Main.Instance.AddChild(_bars);` (3D nodes beside the Hud, not under a Control), `_hitMarker = new HitMarker { Name = "HitMarker" }` centred like the crosshair; `public DamageNumbers DamageNumbers`, `public HitMarker HitMarker`, `public EnemyBars EnemyBars` for the flow; `_ExitTree` frees the two 3D nodes.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Combat/FeedbackRulesTests.cs
public class FeedbackRulesTests
{
    [Fact] public void Colours_AndScales_FollowTheSpec()
    {
        Assert.Equal("#ffffff", FeedbackRules.ColorHex(HitFlavor.Hit)); Assert.Equal("#9a9a9a", FeedbackRules.ColorHex(HitFlavor.Blocked));
        Assert.Equal(1.6f, FeedbackRules.Scale(HitFlavor.Crit)); Assert.Equal(1.6f, FeedbackRules.Scale(HitFlavor.PerfectBlow)); Assert.Equal(1f, FeedbackRules.Scale(HitFlavor.Hit));
        Assert.True(FeedbackRules.Scale(HitFlavor.Poison) < 1f); Assert.True(FeedbackRules.IsTick(HitFlavor.Burn)); Assert.False(FeedbackRules.IsTick(HitFlavor.Shatter));
        Assert.Equal("Blocked", FeedbackRules.Text(HitFlavor.Blocked, 0)); Assert.Equal("Shatter 42", FeedbackRules.Text(HitFlavor.Shatter, 42.4f)); Assert.Equal("7", FeedbackRules.Text(HitFlavor.Hit, 7.2f));
        Assert.Equal(0.9f, FeedbackRules.NumberSeconds); Assert.Equal(0.15f, FeedbackRules.MergeSeconds); Assert.Equal(4f, FeedbackRules.BarLingerSeconds); Assert.Equal(1.2f, FeedbackRules.BarHeight); Assert.Equal(0.12f, FeedbackRules.HitMarkerSeconds); Assert.Equal(0.15f, FeedbackRules.HeavyHitFraction); Assert.Equal(2f, FeedbackRules.LevelUpPulseSeconds);
    }

    [Fact] public void Merger_MergesWithinWindow_PerTarget()
    {
        var m = new DamageNumberMerger();
        m.Add(1, 10, HitFlavor.Hit, now: 0f);
        m.Add(1, 5, HitFlavor.Crit, now: 0.1f);     // same target, inside 0.15 s: one number, 15, gold
        m.Add(2, 3, HitFlavor.Hit, now: 0.1f);      // another target: its own number
        Assert.Equal(2, m.Entries.Count);
        Assert.Equal(15f, m.Entries[0].Amount, 3); Assert.Equal(HitFlavor.Crit, m.Entries[0].Flavor); Assert.True(m.Entries[0].Dirty);
        m.Add(1, 4, HitFlavor.Hit, now: 0.3f);      // too late: a new number
        Assert.Equal(3, m.Entries.Count);
    }

    [Fact] public void Merger_TicksMergeWithTicksOnly_AndBlockedNever()
    {
        var m = new DamageNumberMerger();
        m.Add(1, 10, HitFlavor.Hit, 0f); m.Add(1, 2, HitFlavor.Poison, 0.05f); m.Add(1, 2, HitFlavor.Bleed, 0.1f);
        Assert.Equal(2, m.Entries.Count); Assert.Equal(4f, m.Entries[1].Amount, 3);
        m.Add(1, 0, HitFlavor.Blocked, 0.12f); m.Add(1, 0, HitFlavor.Blocked, 0.13f);
        Assert.Equal(4, m.Entries.Count);
    }

    [Fact] public void Merger_DropsNumbersAfterTheirRise()
    {
        var m = new DamageNumberMerger(); m.Add(1, 10, HitFlavor.Hit, 0f);
        m.Update(0.5f); Assert.Single(m.Entries); m.Update(0.45f); Assert.Empty(m.Entries);
    }
}
```

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter FeedbackRulesTests` → build errors.
- [ ] **Step 3: Implement** the rules and the merger; then the EventBus signature and its raise sites. Keep the wire record as it is for now: `ServerWorld.cs:58` becomes `(e, amount, flavor, by) => { if (!Quiet) Broadcast(new EnemyHurt(e.NetId, amount, flavor == HitFlavor.Crit, e.Hp)); }` and `ClientWorld.cs:87` calls `hurt.ClientHurt(eh.Amount, eh.Hp, eh.Crit ? HitFlavor.Crit : HitFlavor.Hit, -1)` (Task 10 gives the wire the flavor and the striker). Then the three controls and the setting.
- [ ] **Step 4: Run to see them pass**; build → 0 errors; `timeout 60 godot --headless --path . -- --test-hit-fx 2>&1 | grep RESULT` → ok (it listens to hurt cues, not to `EnemyHurt`).
- [ ] **Step 5: Commit** — `git add src/Combat/Core/FeedbackRules.cs src/Core src/Enemies/Enemy.cs src/Net src/UI && git commit -m "feat(hud): damage numbers that merge, a hit marker, and health bars over what you hurt"`

### Task 9: Boss bar, vignette and shake, level-up banner, loot toast, the legendary beam

**Files:**
- Create: `src/UI/Hud/BossBar.cs`, `src/UI/Hud/Vignette.cs`, `src/UI/Hud/LevelUpBanner.cs`, `src/UI/Hud/LootToast.cs`
- Modify: `src/UI/Hud/Hud.cs` (create and wire), `src/UI/Hud/PortraitPanel.cs` (`Flash()`, `Pulse(Color, float)`), `src/Items/LootDrop.cs:28-70` (`_Ready`: the Legendary beam and chime)

**Interfaces:**
- Consumes: `EventBus.MemberDamaged(int, float)`, `LevelUp(int)`, `ItemPickedUp(ItemInstance)`, `Tremor.ShakeOnly`, `RarityInfo.ColorHex`, `Audio.AudioManager.Instance.Play(cue, db)`.
- Produces:
  - `BossBar : Control` (top centre, 520 × 44): polls every 0.25 s rather than listening (both the server and a client have the enemies; a client never raises `BossSeen`): the boss is the nearest `Enemy` with `IsBoss && IsAlive && !IsUnaware` within 40 m of `_party.Anchor`; shows `DisplayName` (title line: the floor's `Contents.Plan.Boss.Name` when it differs), a `ProgressBar` of `Hp/MaxHp` with a 1 px tick drawn at 50%; `public bool Flashing` for 0.6 s when the fraction first crosses 0.5 (the phase change); hides 0.5 s after the boss dies or none is in sight. `public string ShownName`. `public void ShowSlim(Enemy elite)` / `HideSlim()`: a 320 × 24 variant under the main bar for an elite in sight (its `DisplayName` carries the affixes); the elites plan calls these; until then nothing does.
  - `Vignette : ColorRect` (full rect, `MouseFilter = Ignore`, a radial `GradientTexture2D` red → transparent): `public float Alpha`; `public void Hit(float fraction)` sets `Alpha = Math.Clamp(Alpha + fraction * 1.5f, 0f, 0.85f)`, decays to 0 over `VignetteSeconds`. The Hud, on `MemberDamaged(i, amount)` for `i == _party.LocalIndex`: `Hit(amount / MaxHp)`, `_panels[i].Flash()`, and when `amount >= MaxHp * HeavyHitFraction` → `Tremor.ShakeOnly(ShakeAmplitude, ShakeSeconds)`; on any member: `_panels[i].Flash()`.
  - `LevelUpBanner : Label` (top centre, font 28, gold): `public void Show(string text)` for `BannerSeconds`; on `LevelUp(i)`: `Show($"{name} reaches level {level}")`, `_panels[i].Pulse(new Color(1f, 0.85f, 0.3f), LevelUpPulseSeconds)` (the chime `level_up` already plays in `PartyController.AwardXp:565` and reaches clients as an `SfxMsg`).
  - `LootToast : Label` beside the portraits (right of them, font 18): on `ItemPickedUp(item)` with `item.Rarity >= Rarity.Rare`: `Show(item.DisplayName, new Color(RarityInfo.ColorHex(item.Rarity)))` for `ToastSeconds`.
  - `PortraitPanel.Flash()`: `Modulate` to `(1, 0.6, 0.6)` easing back over 0.3 s; `Pulse(Color c, float seconds)`: `Modulate` oscillates toward `c` for `seconds`. Both run in `Refresh`/`_Process` of the panel (add `_Process`).
  - `LootDrop._Ready`: when `Item.Rarity == Rarity.Legendary` and `Coins == 0`: a child `MeshInstance3D` named `Beam` (`CylinderMesh` radius 0.12, height 6, unshaded, `Transparency = Alpha`, the rarity colour at alpha 0.35, additive blend), and `Audio.AudioManager.Instance?.Play("level_up", -6f)` (local playback, so a client's own `LootDrop` chimes once and the server does not relay it). Rare keeps its small light (line 67-68).
  - `Hud`: `public BossBar BossBar; public Vignette Vignette; public LevelUpBanner LevelUpBanner; public LootToast LootToast;` subscriptions in `_Ready`/`_ExitTree` to `MemberDamaged`, `LevelUp`, `ItemPickedUp`.

- [ ] **Step 1: Implement** (Godot-side; Task 11's flow pins it: steps 12-13).
- [ ] **Step 2: Build** → 0 errors; `timeout 60 godot --headless --path . -- --seed=7 --quit-after=5 --no-ambush --no-portals --no-gathering 2>&1 | grep -E "ERROR|SCRIPT"` → nothing.
- [ ] **Step 3: Commit** — `git add src/UI src/Items/LootDrop.cs && git commit -m "feat(hud): the boss bar with its half tick, a red vignette and a shake when it hurts, a level-up banner, a loot toast, and a beam over a legendary"`

### Task 10: Co-op: windows in the pose, flavoured hurt on the wire, telegraphs relayed

**Files:**
- Modify: `src/Net/Core/Snapshot.cs:15` (`PoseFlags.Parry = 64, Dodge = 128`), `src/Net/Core/Messages.cs:89` (`EnemyHurt` shape), `src/Net/Core/Protocol.cs:6` (`Version = 7`)
- Modify: `src/Net/ServerWorld.cs:58` (`EnemyHurt` relay), new relay for `EnemyTelegraph`, `:474-481` (`ApplyPose` copies the two flags)
- Modify: `src/Net/ClientWorld.cs:87` (`EnemyHurt` handler), new `EnemyStateMsg` handler, `:346-358` (`ApplyVitals` raises `MemberDamaged` on a drop), `:383-399` (the pose carries the two flags)
- Modify: `src/Enemies/Enemy.cs` (`ApplyNetState`: a horde body entering the Attack state on the snapshot winds up)
- Test: `tests/Net/NetCodecTests.cs` (add; update the `Protocol.Version` pin at line 232 to 7)

**Interfaces:**
- Produces: `[Flags] public enum PoseFlags : byte { None = 0, Crouch = 1, Sprint = 2, Guard = 4, OnFloor = 8, Downed = 16, Bound = 32, Parry = 64, Dodge = 128 }`; `[Msg(MsgKind.EnemyHurt)] public sealed record EnemyHurt(int NetId, float Amount, byte Flavor, float Hp, int ByMember) : NetMessage;` `Protocol.Version = 7` (the shape changed; `MessageKindCount` stays 80).
- Server: `bus.EnemyHurt += (e, amount, flavor, by) => { if (!Quiet) Broadcast(new EnemyHurt(e.NetId, amount, (byte)flavor, e.Hp, by)); };` `bus.EnemyTelegraph += (e, state) => { if (!Quiet && !e.IsHorde) Broadcast(new EnemyStateMsg(e.NetId, state)); };` `ApplyPose`: `body.Remote.Parry = pose.Flags.HasFlag(PoseFlags.Parry); body.Remote.Dodge = pose.Flags.HasFlag(PoseFlags.Dodge);`.
- Client: `case EnemyHurt eh: ... hurt.ClientHurt(eh.Amount, eh.Hp, (HitFlavor)eh.Flavor, eh.ByMember)`; `case EnemyStateMsg es: if (_enemies.TryGetValue(es.NetId, out var tele) && GodotObject.IsInstanceValid(tele)) tele.ClientTelegraph(es.State); break;` `ApplyVitals`: before assigning, `if (m.IsAlive && mv.Hp < m.Hp - 0.5f) EventBus.Instance.RaiseMemberDamaged(mv.Member, m.Hp - mv.Hp);` (the vignette, the portrait flash and the shake on a client). Pose send: `if (local.Defense.Parrying) flags |= PoseFlags.Parry; if (local.Defense.Dodging) flags |= PoseFlags.Dodge;` and the coalescing condition also sends when `flags != _lastSentFlags` (a window that opens while standing still must not wait half a second). `Enemy.ApplyNetState`: `if (IsHorde && s.State == EnemyStateCodes.Encode("Attack") && _netState != s.State) BeginWindup(WindupSeconds);` before `_netState = s.State`.
- Review item 4 (a stale flag): the server's `DefenseState.ApplyRemote` (Task 3) closes the window on its own; nothing here extends it.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void EnemyHurt_CarriesFlavorAndStriker()
{
    var eh = RoundTrip(new EnemyHurt(7, 12.5f, (byte)HitFlavor.Shatter, 30f, 2));
    Assert.Equal(7, eh.NetId); Assert.Equal(12.5f, eh.Amount, 3); Assert.Equal((byte)HitFlavor.Shatter, eh.Flavor); Assert.Equal(30f, eh.Hp, 3); Assert.Equal(2, eh.ByMember);
}

[Fact]
public void Pose_CarriesParryAndDodgeBits()
{
    var p = RoundTrip(new Pose(5, 1, new NetVec(1, 2, 3), 0.1f, 0.2f, NetVec.Zero, PoseFlags.Guard | PoseFlags.Parry | PoseFlags.Dodge));
    Assert.True(p.Flags.HasFlag(PoseFlags.Parry)); Assert.True(p.Flags.HasFlag(PoseFlags.Dodge)); Assert.True(p.Flags.HasFlag(PoseFlags.Guard));
    Assert.Equal(64, (byte)PoseFlags.Parry); Assert.Equal(128, (byte)PoseFlags.Dodge);
}

[Fact] public void EnemyState_RoundTrips_ASignatureName() => Assert.Equal("Signature:Slam", RoundTrip(new EnemyStateMsg(100000, "Signature:Slam")).State);
```

and `MessageKindCount_MatchesTheEnum_BumpVersionWhenItChanges`: `Assert.Equal(7, Protocol.Version)`.

- [ ] **Step 2: Run to see them fail** — `cd tests && dotnet test --nologo -v q --filter NetCodecTests` → build errors / the version pin.
- [ ] **Step 3: Implement** the six modifications.
- [ ] **Step 4: Run to see them pass**; build → 0 errors; `scripts/net_test.sh` → both RESULT lines ok (the server and client build from the same tree, so protocol 7 on both).
- [ ] **Step 5: Commit** — `git add src/Net src/Enemies/Enemy.cs && git commit -m "feat(net): parry and dodge ride the pose, hurt carries its flavor and striker (protocol 7), telegraphs reach clients"`

### Task 11: `--test-combat-feel`

**Files:**
- Modify: `src/Core/DebugTools.cs` (flag parsing near line 327, the seed/lethality defaults near line 366, the dispatch near line 738, the flow `RunCombatFeelTest` modelled on `RunHitFxTest` at line 3570 and `RunSafeRoomTest` at line 2685)

**Interfaces:**
- Consumes: everything above; `AbilitiesTakeControl(hero)`, `AbilitiesRefill(m)`, `b.SpawnEnemy(id, pos, floor)`, `b.RegularEnemies(floor)`, `b.Roster` (every `EnemyProfile`), `Enemy.ForceAggro/ForceSignature/Staggered/WindupLeft/WindupSeconds/TelegraphRemaining/CurrentAnim`, `Adventurer.DebugIntent/Defense/ForceDodgeRoll/DodgesMade`, `Hud.DamageNumbers/HitMarker/EnemyBars/BossBar/Vignette/LevelUpBanner/LootToast`, `SkillRules.Learn`, `Game.Instance.Skills.Get`, `Game.Instance.Catalog`.
- Produces: `--test-combat-feel` (sets `NoAmbush = NoPortals = NoGathering = true`, `TraceAi = false`, `_seed ??= 7`, `ForcedLethality ??= 1f`, `_quitAfter = 75`; times out with `RESULT FAIL (timed out at step N)` at 70 s); prints `[combat-feel] ...` lines and `[combat-feel] RESULT ok|FAIL (why)`.

- [ ] **Step 1: Write the flow** as a step machine (`_cfStep`, `_cfNext`, `Fail`, `Finish` like `RunHitFxTest`). Counters from the bus (subscribed in step 0, unsubscribed in `Finish`): `_cfPerfectBlocks` (`PerfectBlock`), `_cfParried` (`Parried`), `_cfPerfectBlows` (`PerfectBlow`), `_cfLowHp` (`LowHp`), `_cfLog` (`Message` lines since the last step). Steps:
  0. `_elapsed >= 1.0`: free every enemy; `AbilitiesTakeControl(brand)`; Brand gets `+1000 Accuracy` (as `RunHitFxTest` does); teleport the party to the largest room's centre `stand`; **data**: for every profile in `b.Roster.Values` assert `ResistFire/Frost/Poison/Arcane <= CombatRules.MaxResist` (print the offenders). Spawn `E1 = b.SpawnEnemy(b.RegularEnemies(floor).First(id => b.EnemyProfile(id)!.Kind == EnemyKind.Melee), stand − Up*0.2 + Forward*1.5, floor)`, `E1.ForceAggro()`. Print `[combat-feel] floor seed E1 windup`.
  1. wait for `E1.State is AttackState` (fail after 5 s): assert `E1.WindupLeft >= E1.WindupSeconds − 0.1` and `E1.CurrentAnim == "Punch"`; record Brand's hp/stamina; `brand.DebugIntent = new InputIntent { Guard = true, Live = true }`.
  2. after `E1.WindupSeconds + 0.25`: hp and stamina unchanged, `_cfPerfectBlocks == 1`, `E1.Staggered`, a log line contains "perfectly blocks". Keep the guard held.
  3. wait for the next swing (`E1` leaves then re-enters its wind-up; fail after 5 s) then `WindupSeconds + 0.25`: stamina fell by exactly `DefenseRules.BlockStamina` (±0.5 allowing regen: guard stops stamina regen), hp fell by more than 0 and at most `E1.Attack.MaxDamage * 0.25 + 1`, `_cfPerfectBlocks` still 1, a line contains "(blocked)". `DebugIntent = null`; stun E1 for 30 s (`E1.Effects.Apply(Stun)`).
  4. `AbilitiesTakeControl(sable)`; `sable.State.SkillPoints += 4`; learn `skill.rogue.duelist.footwork`, `.flurry`, `.venom_mastery`, `.parry` in order through `SkillRules.Learn(state, Game.Instance.Skills.Get(id)!, Game.Instance.Skills)` (fail if any refuses); `AbilitiesRefill`; teleport Sable 1.5 m in front of E1 facing it; `E1.Effects.Remove(Stun)`; wait for `AttackState` (5 s): record hp/stamina/E1.Hp; `sable.DebugIntent = new InputIntent { ParryPressed = true, Live = true }` and clear it next step.
  5. after `WindupSeconds + 0.25`: Sable's hp unchanged, stamina fell by `ParryStamina` (±0.5), `_cfParried == 1`, `E1.Staggered`, `E1.Hp < before` and a line contains "ripostes" (Riposte 0.5 through the Parry node). Stun E1 again.
  6. parry economy: `AbilitiesRefill(sable)`; press parry with nothing coming (`DebugIntent { ParryPressed = true }` one tick): stamina −10, no event; press again 0.2 s later: stamina unchanged (cooldown); print both.
  7. dodge: un-stun E1, wait for `AttackState`, `sable.DebugIntent = new InputIntent { DodgePressed = true, Move = new Vector2(0, 1), Live = true }` one tick: after `WindupSeconds + 0.25` hp unchanged, stamina fell by `DodgeStamina` (±0.5), a line contains "dodges", `sable.DodgesMade == 1`, Sable moved ≥ 1.5 m from where she stood; a second `DodgePressed` 0.1 s after the first was refused (`DodgesMade` still 1). Stun E1.
  8. **a dodge never saves you from the dungeon**: `DebugIntent { DodgePressed = true }` one tick, then while `sable.Dodging`: `sable.ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = 5, MaxDamage = 5 }, "The gas")` → hp fell by 5 (±mitigation: compare to `DamagePipeline.Mitigate(5, Physical, sable.State.Stats)` ±0.5).
  9. stagger by a blow: `AbilitiesTakeControl(brand)`, Brand in front of E1, un-stun, wait for `AttackState` with `E1.WindupLeft > 0.3`: `party.Attack(brand, heavy: true)` → next tick `E1.Staggered` and `E1.WindupLeft >= E1.WindupSeconds − 0.05` (the wind-up reset); `hud.DamageNumbers.ActiveCount >= 1`, `hud.HitMarker.Active`, `hud.EnemyBars.Showing(E1)`; then `AbilitiesRefill(brand)`, `party.Attack(brand, heavy: true, perfect: true)` → `_cfPerfectBlows == 1`, a line contains "perfectly smashes". Spawn one horde body (`E1.Profile.Id` via `b.SpawnEnemy`, call `SetSwarmDetail()`), hit it with `ReceiveAttack` directly → `hud.EnemyBars.Showing(horde) == false`; free it.
  10. `Settings.DamageNumbers = false`; wait 1.0 s (the pool empties); `party.Attack(brand)` → `ActiveCount == 0`; `Settings.DamageNumbers = true`.
  11. statuses: `E1.Effects.Apply(Frost 5 s)`; `party.Attack(brand, heavy: true)` → a line contains "shatters", `!E1.Effects.Has(Frost)`. `E1.Effects.Apply(Poison 6 s, 3)`; spawn `E2` 1.5 m from E1 (stunned); `var r = E1.ReceiveAttack(brand.State.Stats, new AttackProfile { MinDamage = 1, MaxDamage = 1, Type = Fire, Kind = Spell, OnHit = [Burn 3 s, 2] }, new HitContext { DefenderStunned = true }, party.Rng, "test")` → `r.IgniteBurst == 12` and `E2.Hp < E2.MaxHp`. Bleed: `brand.State.Hp = 50; brand.State.Effects.Apply(Bleed 5 s, 1); brand.State.Heal(10)` → 55. Vulnerable: `E1.ReceiveAttack(... Kind = Spell, OnHit = [Stun 0.7])` → `E1.Effects.Has(Vulnerable)`. Frost wind-up: `E1.Effects.Apply(Frost)` → `E1.WindupSeconds ≈ E1.Profile.WindupSeconds / 0.7` (±0.01). Free E1, E2.
  12. boss: spawn `boss = b.SpawnEnemy("enemy.crypt_lord", stand + Forward*5, floor)`; every companion unparked (`SetControl(Companion)`), `Stance = Aggressive`, teleported to `boss.GlobalPosition ± 2 m`, `ForceDodgeRoll = 0f`, stamina refilled; `boss.ForceAggro(); boss.ForceSignature()` → `boss.State.Name == "Signature:Slam"`, `boss.TelegraphRemaining` within 0.1 of `TelegraphRules.SlamDelay`, a node named `TelegraphDecal` exists under the floor root, `hud.BossBar.Visible && hud.BossBar.ShownName == boss.DisplayName`. After 1.4 s: a line contains "slam" (the strike) or "no one is in reach", `DodgesMade` summed over companions ≥ 1, no `TelegraphDecal` remains. Heavy blow on the boss (`party.Attack(brand, heavy: true)` with Brand beside it) → `!boss.Staggered`. `ForceDodgeRoll = 1f`, `boss.ForceSignature()` → after 1.4 s no new dodge (`DodgesMade` unchanged). Then Sable parries the boss's next melee swing (as step 4-5) → `boss.Staggered` (a boss is staggered by a parry). Hit the boss to below half (`boss.ReceiveAttack` with `MinDamage = MaxDamage = boss.MaxHp * 0.6`) → within 0.5 s `hud.BossBar.Flashing`; `boss.Kill()` → within 1.0 s `!hud.BossBar.Visible`.
  13. feedback: `brand.ReceiveAttack(E1.Stats, new AttackProfile { MinDamage = MaxDamage = brand max HP × 0.2 }, "test")` (no attacker) → `hud.Vignette.Alpha > 0` and `party.Lead.Shaking`; `_cfLowHp` ≥ 1 after setting hp to 20% first; `party.AwardXp(100000)` → `hud.LevelUpBanner.Visible`; `party.PickUp(Game.Instance.Catalog.Create("item.longsword") with Rarity = Rare` (use `LootRoller`/the catalog's affix path the `--drop=` flag uses to make a Rare) → `hud.LootToast.Visible`; a Legendary `LootDrop` spawned with `party.SpawnLoot(legendary, at)` has a child named `Beam`. `Finish()`.
- [ ] **Step 2: Run** — `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && timeout 120 godot --headless --path . -- --test-combat-feel 2>&1 | grep -E "^\[combat-feel\]|RESULT|ERROR"` → `[combat-feel] RESULT ok`. Steps that depend on an enemy's rhythm have a 5 s wait and print what they saw; a flake is fixed in the flow, not retried blindly.
- [ ] **Step 3: Everything else still passes** — `cd tests && dotnet test --nologo -v q` → PASS; `timeout 150 godot --headless --path . -- --test-abilities --no-ambush --no-portals 2>&1 | grep RESULT`, `--test-hit-fx`, `--test-party`, `--test-safe-room`, `--test-hazard=press` → ok (the hit-chance change can move a flaky fight: if `--test-abilities` fails on a miss, raise its heroes' Accuracy in the flow and say so).
- [ ] **Step 4: Commit** — `git add src/Core/DebugTools.cs && git commit -m "test(combat): --test-combat-feel: perfect block, block, parry, riposte, dodge, hazards through a dodge, stagger, numbers, bars, combos, the boss telegraph and companion dodges, the HUD"`

### Task 12: Docs and controls

**Files:**
- Modify: `docs/development.md` (the `--test-*` list near line 293; a new section "Combat feel" after "Hit effects" at line 562-568; the Controls table at line 325-350: Right mouse row, a Left Alt row; the Multiplayer section: protocol 7, `PoseFlags.Parry/Dodge`, `EnemyStateMsg`)
- Modify: `README.md:106-122` (controls: Right mouse "Guard (Warrior) or parry (the others)"; "Left Alt | Dodge: a short burst you cannot be hit through, 20 stamina")

- [ ] **Step 1: Write** — test list: `godot --headless --path . -- --test-combat-feel   # perfect block, block, parry and riposte, dodge (never through gas), stagger, damage numbers and bars, Shatter/Ignite/Bleed/Vulnerable, the boss's decal and the companions' dodge, boss bar, vignette, banner, toast (seed 7, lethality 1); prints [combat-feel] RESULT`. Section "Combat feel": what each verb is, the numbers (one line each, pointing at `DefenseRules`, `TelegraphRules`, `FeedbackRules`, `CombatRules`), how a remote player's windows reach the server (pose bits, `RemoteGraceSeconds`), how clients learn a telegraph (`EnemyStateMsg`, hordes by snapshot), what `Settings.DamageNumbers` hides, that elites hook `BossBar.ShowSlim` and `Enemy.StaggerImmune`.
- [ ] **Step 2: Commit** — `git add docs/development.md README.md && git commit -m "docs: combat feel: the verbs, the telegraphs, the numbers, and how they travel in co-op"`

---

## Self-review notes

- Spec coverage: Feedback (numbers, merge, setting, hit marker with kill X, enemy bars minus hordes, boss bar with tick/flash/hide, elite slim bar as a hook, vignette, portrait flash, shake, level-up banner and pulse, loot toast, legendary beam and chime, Rare keeps its light): Tasks 8-9. Telegraphs (clip from the wind-up, time-scaled, white tint 0.2 s, strike at the clip's frame, hand glow, decals per signature, delays 1.1/0.7): Task 6, clients Task 10. Verbs (dodge numbers and i-frames vs strikes and projectiles only, companions 50% on Defensive/Aggressive, perfect block, parry numbers and melee-only, Riposte/SecondWind sites, the Parry node live, stagger rules incl. boss-only-by-defense and the Juggernaut hook): Tasks 3-5, 7. Numbers and statuses (hit chance, Frost scales wind-up and recovery, Shatter, Ignite, Bleed halves healing, Vulnerable from Deep Freeze, Thorns on Bulwark 2, resist data clamp): Tasks 1-2, 5.
- Type consistency: `DefenseState.Judge(AttackKind, bool, bool, bool, float)` is called with that order in Task 4; `RaiseEnemyHurt(Enemy, float, HitFlavor, int)` is used by Tasks 8 and 10 and the wire record field order `(NetId, Amount, Flavor, Hp, ByMember)` matches Task 10's test; `TelegraphRules` lives in `DungeonCrawler.Enemies` (it names `Signature`), `DefenseRules`/`FeedbackRules`/`CombatRules` in `DungeonCrawler.Combat`.
- Contract deviations, stated: (1) the contract's "`StatusKind.Vulnerable` + its Modifiers entry" cannot be a `StatModifier` (there is no damage-taken stat); it is `StatusEffectController.DamageTakenMultiplier`, read by `DamagePipeline.Mitigate`. (2) `EventBus.EnemyHurt` changes signature (flavor and striking member replace the crit bool) and one event not in the contract is added, `EnemyTelegraph(Enemy, string)`; both listed below. (3) The hit marker is driven by `EnemyHurt` filtered to the local member rather than `HitLanded`, because `HitLanded` never fires on a client and `EnemyHurt` now carries the striker. (4) `Protocol.Version` is bumped to 7 for a shape change with no new `MsgKind` (Protocol.cs's own rule). (5) The elite slim bar is built as `BossBar.ShowSlim/HideSlim` for the elites plan to call; nothing calls it in this plan.
- Shared-file members this plan adds (exact): `EventBus`: `event Action<int>? PerfectBlow` + `RaisePerfectBlow(int member)`, `Parried` + `RaiseParried(int)`, `PerfectBlock` + `RaisePerfectBlock(int)`, `MemberFell` + `RaiseMemberFell(int)`, `TrapSprung` + `RaiseTrapSprung(int)`, `LowHp` + `RaiseLowHp(int)`, `event Action<Enemies.Enemy, string>? EnemyTelegraph` + `RaiseEnemyTelegraph(Enemies.Enemy, string)`, and `EnemyHurt` becomes `Action<Enemies.Enemy, float, Combat.HitFlavor, int>` with `RaiseEnemyHurt(Enemies.Enemy, float, Combat.HitFlavor, int)`. `StatusKind.Vulnerable` (appended). `Settings.Rebindable` += `("dodge", "Dodge")`; `Settings.DamageNumbers`. `SkillDef.Trigger2`/`Trigger2Magnitude`, `SkillProfile.Trigger2`/`Trigger2Magnitude`, `SkillRules.TriggerMagnitude` sums them. `HitContext.PerfectBlow`. `DamageResult.Shatter`, `DamageResult.IgniteBurst`. `PoseFlags.Parry = 64`, `PoseFlags.Dodge = 128`; `EnemyHurt(int NetId, float Amount, byte Flavor, float Hp, int ByMember)`; `Protocol.Version = 7`. `ServerWorld`: the `EnemyHurt` relay's new shape, an `EnemyTelegraph` relay, two lines in `ApplyPose`. `ClientWorld`: the `EnemyHurt` handler's new call, an `EnemyStateMsg` case, two lines in `ApplyVitals`, two flag lines and the flags-changed condition in the pose send. `CharacterState.Heal`/`Regenerate` multiply by `Effects.HealingMultiplier`. `PartyController`: `BlockStaminaCost = DefenseRules.BlockStamina`, `Mitigate(..., m.Effects)` at line 405, `DebugIntent` in `_PhysicsProcess`, "shatters" in `LogResult`. `Enemy`: `Stagger`, `Staggered`, `StaggerImmune`, `HitReactSeconds`, `WindupLeft`, `TelegraphId`, `TelegraphRemaining`, `CurrentAnim`, `BeginWindup`, `BeginSignatureTelegraph`, `EndTelegraph`, `ClientTelegraph`, `ForceSignature`, `PlayAnim(name, force, speed)`, `ClientHurt(amount, hp, flavor, byMember)`, the stagger gate, the tick raises, the Ignite burst. `LootDrop._Ready`: the beam and chime. `project.godot`: the `dodge` action.
