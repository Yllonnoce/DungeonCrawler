# Phase 11: Lethal Traps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two lethal environmental hazards, the Press (a three-cell corridor whose walls close) and the Spike Bed (floor patches that impale), that kill enemies and down players and allies alike, placed fairly by the generator, synchronised by the server, with a rescue loop so a crushed player is a setback and not a spectator.

**Architecture:** Engine-free rules (`src/Dungeon/HazardRules.cs`, `src/Dungeon/Generation/HazardPlacement.cs`, `src/Party/Core/MangledRules.cs`) carry every number and every placement rule under xUnit. Thin Godot nodes (`src/Dungeon/Props/Press.cs`, `SpikeBed.cs`, `HazardPlate.cs`) run a server-owned state machine, write a per-cell danger value the pathfinder reads as added cost, and apply damage through one new `IHazardVictim` interface that `Adventurer`, `Enemy` and `Npc` all implement. Phases replicate as a new `HazardPhaseMsg`; clients animate, never kill. Save v9 adds hazard state and a `HazardsFromFloor` guard for old runs.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit 2.9.2, the existing headless `--test-*` harness in `src/Core/DebugTools.cs`.

**Spec:** `docs/superpowers/specs/2026-10-22-living-npcs-and-lethal-traps-design.md`, Part C (C1-C9) and D (D2, D3). The campaign bible `docs/superpowers/specs/2026-10-22-campaign-gullet-of-gorm.md` section 9 lists the arena asks folded in as Task 13.

## Global Constraints

- Engine-free code lives under `*/Core` or in the files the test project already links (`tests/DungeonCrawler.Tests.csproj` lines 12-33); never reference Godot types there.
- Cells are 4 m (`DungeonBuilder.CellSize = 4f`), levels 4 m apart (`LevelHeight = 4f`); grid cells are `GridPos(X, Y)`.
- `MsgKind` is append-only; new messages go after `ObjectiveDone`.
- Save version becomes 9; the migrator case is a no-op except for `HazardsFromFloor`.
- `Settings.Lethality` is a float 1..3 (`TierRules.MinLethality`/`MaxLethality`), default 3. A hazard is lethal to players and allies when `Lethality >= 2.5`; below that it deals `0.5 + 0.25 * (Lethality - 1)` of max HP. Enemies always die. Bosses take 35% max HP and a 2 s stun, never a kill.
- Press timeline: Warning 1.2 s, Closing 2.4 s (ease-in, gap 3.5 m to 0.8 m), Crush instant, Hold 1.5 s, Retract 4.0 s, Cooldown 6.0 s. Jammed by a Breakable in the footprint: slabs stop at 1.0 m, no crush, prop breaks on its second squeeze.
- Spike Bed: pressure variant Click 0.5 s (10 cm pre-raise), Up 1.0 s lethal, Retract 1.5 s, Rearm 4.0 s. Periodic variant Down 3.0 s, Click 0.5 s, Up 1.0 s, phase offset `(patchIndex * 37) mod period` in ticks at 60 Hz. A body with its feet off the ground at full raise is not hit.
- At most one Press per floor, none on labyrinth floors, none inside a boss arena. Hazards start on floor 3. Counts per floor by tier: Crypt 0-1 Press, 1 spike bed (pressure); Caves 1 Press, 1-2 beds (periodic allowed); Forge 1 Press, 2 beds; Abyss and endless 1 Press, 2-3 beds.
- Every hazard is dealt after `PlaceTraps` with `new Random(seed ^ 0x4A2D)`, so floors with zero hazards hash exactly as before.
- Danger weights on the A* step cost: companions and allies 50, smart enemies 12, horde and bandit factions 0.
- Co-op: the server owns every hazard state machine and every kill; clients animate from `HazardPhaseMsg` against the server tick.
- Commit after every task with the attribution line `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

1. A party member standing on the Press plate when the floor is restored from a save mid-Closing must not be crushed on the first frame after load; the restored phase resumes with its remaining time and the kill check runs only at the Crush transition. Test added to Task 8 (`Restore_MidClosing_ResumesRemainingTime`).
2. A spike bed under a cellar pit mouth or ledge ramp would kill a falling or climbing body with no tell; placement rule 3 forbids cells adjacent to ramps, pits and ledges. Test added to Task 4 (`SpikeBeds_NeverTouchRampsPitsOrLedges`).
3. A Press whose plate sits in a room doorway can seal a room while enemies pour out of a nest; rule 2 requires three straight corridor cells with no doors and no nest mouths. Test added to Task 4 (`Press_NeverOnDoorsOrNestMouths`).
4. Two humans in co-op, one crushed while the other is already Downed from combat, must not wipe the party when the second auto-revives after calm; a Mangled body does not count toward the wipe check while a rescuer is alive. Test added to Task 6 (`PartyWipe_IgnoresMangled_WhileAnyoneStands`).
5. Lethality 1 must still kill enemies that walk into the Press, or baiting stops working; `HazardRules.EnemyOutcome` ignores lethality. Test added to Task 1 (`Enemies_Die_AtEveryLethality`).

---

## File map

| File | Responsibility |
|---|---|
| `src/Dungeon/HazardRules.cs` (new, linked in tests) | enums, timings, lethality outcomes, phase advancement, periodic phase math |
| `src/Dungeon/Generation/HazardPlacement.cs` (new) | `HazardKind`, `HazardVariant`, `HazardPlacement` record, `HazardCounts`, `PlaceHazards`, critical path |
| `src/Dungeon/Generation/FloorLayout.cs` (modify) | `Hazards` list, `IsReserved`, `ContentHash` |
| `src/Dungeon/Generation/FloorGenerator.cs` (modify) | call `PlaceHazards` after `PlaceTraps` |
| `src/Dungeon/Generation/GridPathfinder.cs` (modify) | optional `danger` cost |
| `src/Party/Core/MangledRules.cs` (new) | rescue timing, wipe rule, bound-cell choice |
| `src/Combat/IHazardVictim.cs` (new) | `ReceiveHazard` |
| `src/Characters/CharacterState.cs` (modify) | `Mangled`, `MangledBy` |
| `src/Party/Adventurer.cs`, `src/Party/PartyController.cs` (modify) | victim, Mangled, rescue, companion Rescue action |
| `src/Characters/Companions/CompanionBrain.cs` (modify) | `ActionKind.Rescue` |
| `src/Enemies/Enemy.cs`, `src/Adventure/Npc.cs` (modify) | victim, environmental death, downs |
| `src/Dungeon/Props/HazardPlate.cs`, `Press.cs`, `SpikeBed.cs` (new) + `scenes/Props/Press.tscn`, `SpikeBed.tscn` | nodes, state machines, visuals, danger writes |
| `src/Dungeon/DungeonBuilder.cs` (modify) | spawn hazards, `DangerAt`/`SetDanger`, hazard refs |
| `src/Save/Core/SaveDtos.cs`, `SaveFormat.cs`, `src/Save/FloorStateCapture.cs` (modify) | `HazardStateDto`, v9, `HazardsFromFloor` |
| `src/Net/Core/Protocol.cs`, `Messages.cs`, `src/Net/ServerWorld.cs`, `ClientWorld.cs`, `NetRefResolver.cs` (modify) | `HazardPhaseMsg`, `RescueIntent`, `ShoutIntent` |
| `src/Core/EventBus.cs`, `src/Adventure/Core/Objectives/Taunts.cs`, `src/Adventure/AdventureDirector.cs` (modify) | `HazardPhase`, `MemberKilledByHazard`, `TauntTrigger.Crushed` |
| `src/Core/DebugTools.cs` (modify) | `--hazard=`, `--test-hazard=` |
| `tests/Dungeon/HazardRulesTests.cs`, `HazardPlacementTests.cs`, `tests/Party/MangledRulesTests.cs`, `tests/Net/NetCodecTests.cs`, `tests/Save/SaveFormatTests.cs` | tests |

---

### Task 1: Hazard rules (engine-free)

**Files:**
- Create: `src/Dungeon/HazardRules.cs`
- Modify: `tests/DungeonCrawler.Tests.csproj` (add `<Compile Include="../src/Dungeon/HazardRules.cs" Link="Game/Dungeon/HazardRules.cs" />` next to line 28)
- Test: `tests/Dungeon/HazardRulesTests.cs`

**Interfaces:**
- Produces:
  - `public enum HazardKind : byte { Press, SpikeBed }`
  - `public enum HazardVariant : byte { Pressure, Periodic }`
  - `public enum PressPhase : byte { Armed, Warning, Closing, Crush, Hold, Retract, Cooldown }`
  - `public enum SpikePhase : byte { Down, Click, Up, Retract, Rearm }`
  - `public enum HazardDamage : byte { Crush, Impale }`
  - `public enum VictimKind : byte { Member, Ally, QuestNpc, Enemy, Boss }`
  - `public readonly record struct HazardOutcome(bool Lethal, float HpFraction, float StunSeconds)`
  - `public static class HazardRules` with: `const float LethalFrom = 2.5f`; `static bool IsLethal(float lethality)`; `static float DamageFraction(float lethality)`; `static HazardOutcome Outcome(VictimKind victim, float lethality)` (Member and Ally: lethal or fraction; QuestNpc: `HpFraction = 1f` never lethal (downs); Enemy: `Lethal = true`; Boss: `(false, 0.35f, 2f)`); `static float PressDuration(PressPhase p)` (Armed returns `float.PositiveInfinity`); `static PressPhase NextPress(PressPhase p)` (Cooldown wraps to Armed; Crush goes to Hold); `static float PressGap(float t01)` (3.5 m to 0.8 m, ease-in `t*t`); `const float JamGap = 1.0f`; `static float SpikeDuration(SpikePhase p, HazardVariant v)` (Down is infinite for Pressure, 3.0 for Periodic; Rearm 4.0 for Pressure, 0 for Periodic); `static SpikePhase NextSpike(SpikePhase p)`; `static int PeriodicPeriodTicks = 270` (4.5 s at 60 Hz); `static SpikePhase PeriodicPhaseAt(int serverTick, int patchIndex, out float secondsIntoPhase)`; `const float SpikePreRaiseMetres = 0.10f`; `static bool CanDodgeSpikes(bool onFloor) => !onFloor`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class HazardRulesTests
{
    [Theory] [InlineData(3f, true)] [InlineData(2.5f, true)] [InlineData(2.49f, false)] [InlineData(1f, false)]
    public void Lethal_FromTwoAndAHalf(float lethality, bool lethal) => Assert.Equal(lethal, HazardRules.IsLethal(lethality));

    [Theory] [InlineData(1f, 0.5f)] [InlineData(2f, 0.75f)] [InlineData(2.49f, 0.8725f)]
    public void DamageFraction_Interpolates(float lethality, float expected) => Assert.Equal(expected, HazardRules.DamageFraction(lethality), 3);

    [Theory] [InlineData(1f)] [InlineData(2f)] [InlineData(3f)]
    public void Enemies_Die_AtEveryLethality(float lethality) => Assert.True(HazardRules.Outcome(VictimKind.Enemy, lethality).Lethal);

    [Fact] public void Boss_IsNeverKilled() { var o = HazardRules.Outcome(VictimKind.Boss, 3f); Assert.False(o.Lethal); Assert.Equal(0.35f, o.HpFraction); Assert.Equal(2f, o.StunSeconds); }
    [Fact] public void QuestNpc_IsDownedNotKilled() { var o = HazardRules.Outcome(VictimKind.QuestNpc, 3f); Assert.False(o.Lethal); Assert.Equal(1f, o.HpFraction); }
    [Fact] public void Member_AtDefaultLethality_IsLethal() => Assert.True(HazardRules.Outcome(VictimKind.Member, 3f).Lethal);

    [Fact] public void Press_Timeline_MatchesSpec()
    {
        Assert.Equal(1.2f, HazardRules.PressDuration(PressPhase.Warning));
        Assert.Equal(2.4f, HazardRules.PressDuration(PressPhase.Closing));
        Assert.Equal(1.5f, HazardRules.PressDuration(PressPhase.Hold));
        Assert.Equal(4.0f, HazardRules.PressDuration(PressPhase.Retract));
        Assert.Equal(6.0f, HazardRules.PressDuration(PressPhase.Cooldown));
        Assert.Equal(3.6f, HazardRules.PressDuration(PressPhase.Warning) + HazardRules.PressDuration(PressPhase.Closing), 3);
        Assert.Equal(PressPhase.Hold, HazardRules.NextPress(PressPhase.Crush));
        Assert.Equal(PressPhase.Armed, HazardRules.NextPress(PressPhase.Cooldown));
    }

    [Fact] public void PressGap_EasesIn() { Assert.Equal(3.5f, HazardRules.PressGap(0f)); Assert.Equal(0.8f, HazardRules.PressGap(1f), 3); Assert.True(HazardRules.PressGap(0.5f) > (3.5f + 0.8f) / 2f); }

    [Fact] public void Spikes_PressureTimeline() { Assert.Equal(0.5f, HazardRules.SpikeDuration(SpikePhase.Click, HazardVariant.Pressure)); Assert.Equal(1.0f, HazardRules.SpikeDuration(SpikePhase.Up, HazardVariant.Pressure)); Assert.Equal(1.5f, HazardRules.SpikeDuration(SpikePhase.Retract, HazardVariant.Pressure)); Assert.Equal(4.0f, HazardRules.SpikeDuration(SpikePhase.Rearm, HazardVariant.Pressure)); Assert.True(float.IsPositiveInfinity(HazardRules.SpikeDuration(SpikePhase.Down, HazardVariant.Pressure))); }

    [Fact] public void Periodic_TwoBeds_AreOutOfStep()
    {
        var a = HazardRules.PeriodicPhaseAt(0, 0, out _); var b = HazardRules.PeriodicPhaseAt(0, 3, out _);
        Assert.Equal(SpikePhase.Down, a); Assert.NotEqual(a, b);
        Assert.Equal(HazardRules.PeriodicPhaseAt(0, 0, out _), HazardRules.PeriodicPhaseAt(HazardRules.PeriodicPeriodTicks, 0, out _));
    }

    [Fact] public void Airborne_DodgesSpikes() { Assert.True(HazardRules.CanDodgeSpikes(onFloor: false)); Assert.False(HazardRules.CanDodgeSpikes(onFloor: true)); }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter HazardRulesTests`
Expected: build error, `HazardRules` not defined.

- [ ] **Step 3: Implement `src/Dungeon/HazardRules.cs`** with the signatures above in namespace `DungeonCrawler.Dungeon`. `PeriodicPhaseAt` lays the phases Down 180, Click 30, Up 60 ticks (total 270) and offsets by `(patchIndex * 37) % PeriodicPeriodTicks`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter HazardRulesTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dungeon/HazardRules.cs tests/Dungeon/HazardRulesTests.cs tests/DungeonCrawler.Tests.csproj
git commit -m "feat(hazards): engine-free hazard rules, timings and lethality outcomes"
```

---

### Task 2: Hazard victim interface and the Mangled state

**Files:**
- Create: `src/Combat/IHazardVictim.cs`, `src/Party/Core/MangledRules.cs`
- Modify: `src/Characters/CharacterState.cs`, `src/Party/Adventurer.cs` (near `ApplyTrap` line 800 and `OnDowned` 808), `src/Party/PartyController.cs` (`AutoRevive` 423, wipe check 408)
- Test: `tests/Party/MangledRulesTests.cs`

**Interfaces:**
- Produces:
  - `public interface IHazardVictim { VictimKind HazardVictimKind { get; } void ReceiveHazard(HazardDamage kind, HazardOutcome outcome, string sourceName); }`
  - `CharacterState.Mangled : bool` and `CharacterState.MangledBy : int` (hazard index, -1 none), both saved in `CharacterDto` (add fields; default false/-1 so old saves read clean).
  - `MangledRules` (engine-free, namespace `DungeonCrawler.Party`): `const float RescueSeconds = 3f`, `const float RescueRange = 2f`, `const float RescueInterruptMove = 1f`, `const float BoundAfterSeconds = 30f`, `const float ReviveHp = 1f`, `const float ReviveStress = 25f`; `static bool CanAutoRevive(bool mangled, bool hazardArmed) => !mangled`; `static bool IsWipe(IReadOnlyList<(bool Downed, bool Mangled)> members)` (wipe only when nobody is standing; Mangled counts as downed); `static bool RescueAllowed(bool hazardArmed, float distance, bool rescuerDowned) => hazardArmed && distance <= RescueRange && !rescuerDowned`.
  - `Adventurer : IHazardVictim` with `HazardVictimKind => VictimKind.Member`; `ReceiveHazard` applies `outcome.Lethal ? Hp = 0 + Mangled : Hp -= MaxHp * HpFraction` and raises `EventBus.MemberKilledByHazard(index, sourceName)` on lethal.
  - `PartyController.TryRescue(Adventurer target, Adventurer rescuer, Func<bool> hazardArmed) : bool` begins a rescue timer; `PartyController.RescueProgress(int index) : float`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class MangledRulesTests
{
    [Fact] public void Mangled_NeverAutoRevives() => Assert.False(MangledRules.CanAutoRevive(mangled: true, hazardArmed: true));
    [Fact] public void Downed_AutoRevives() => Assert.True(MangledRules.CanAutoRevive(mangled: false, hazardArmed: false));
    [Fact] public void PartyWipe_IgnoresMangled_WhileAnyoneStands() => Assert.False(MangledRules.IsWipe(new[] { (true, true), (false, false) }));
    [Fact] public void PartyWipe_WhenAllDown_OrMangled() => Assert.True(MangledRules.IsWipe(new[] { (true, true), (true, false) }));
    [Fact] public void Rescue_NeedsArmedHazard_AndRange() { Assert.True(MangledRules.RescueAllowed(true, 1.5f, false)); Assert.False(MangledRules.RescueAllowed(false, 1.5f, false)); Assert.False(MangledRules.RescueAllowed(true, 2.5f, false)); Assert.False(MangledRules.RescueAllowed(true, 1f, true)); }
    [Fact] public void Constants_MatchSpec() { Assert.Equal(3f, MangledRules.RescueSeconds); Assert.Equal(30f, MangledRules.BoundAfterSeconds); Assert.Equal(25f, MangledRules.ReviveStress); }
}
```

- [ ] **Step 2: Run to verify failure** (`--filter MangledRulesTests`, expected build error).

- [ ] **Step 3: Implement `MangledRules`, `IHazardVictim`, the `CharacterState` fields and the `CharacterDto` fields** (`SaveMapper` copies them both ways).

- [ ] **Step 4: Implement the engine side.** `Adventurer.ReceiveHazard` as above; `Adventurer.ShowDowned` sets `CollisionLayer = 8u` when `State.Mangled` so the interact ray finds the body, and `Adventurer` implements `IInteractable` with `Prompt => State.Mangled ? $"Rescue {State.Name}" : ""` and `Interact(party, actor) => party.TryRescue(this, actor, () => Main.Instance.Builder.HazardArmed(State.MangledBy))`. `PartyController.AutoRevive` skips members whose state is Mangled; the wipe check at line 408 uses `MangledRules.IsWipe`. `TryRescue` starts a timer per target; `_Process` advances it while the rescuer stays within `RescueRange` and has not moved more than `RescueInterruptMove` from where it started and has not taken damage; at `RescueSeconds` the member gets `Hp = ReviveHp`, `Mangled = false`, `Revive()`, log `"{name} is pulled free."`, `RaiseMemberRevived`. `Builder.HazardArmed(index)` is stubbed `true` until Task 6 fills it.

- [ ] **Step 5: Run the full suite** (`dotnet test tests/DungeonCrawler.Tests.csproj`), expected all pass, then build the game (`dotnet build DungeonCrawler.csproj`), expected no errors.

- [ ] **Step 6: Commit** `feat(hazards): IHazardVictim, Mangled state and rescue timer`.

---

### Task 3: Enemies, NPCs and bosses as hazard victims

**Files:**
- Modify: `src/Enemies/Enemy.cs` (near `Kill()` 732 and `OnDeath` 739), `src/Adventure/Npc.cs` (near `GoDown` 156, `Hazard` 439), `src/Core/EventBus.cs`

**Interfaces:**
- Consumes: `IHazardVictim`, `HazardRules.Outcome`.
- Produces: `Enemy : IHazardVictim` (`HazardVictimKind => IsBoss ? VictimKind.Boss : VictimKind.Enemy`); `Enemy.ReceiveHazard` kills via a new `KillByEnvironment(string sourceName)` that awards XP to the nearest living member within 10 m (`Party.AwardXp` with that member) and drops no loot (`OnDeath(loot: false)` overload); boss branch subtracts `MaxHp * 0.35f` and calls the existing stun path for 2 s. `Npc : IHazardVictim` (`HazardVictimKind => Mode == NpcMode.Fighter && Escorting ? VictimKind.Ally : VictimKind.QuestNpc`); lethal on an ally sets `Downed` plus a new `Npc.Mangled` flag; QuestNpc, Ritualist and Prisoner go `GoDown()`. `Npc.Hazard` (the escort-wound helper) calls `GoDown()` instead of `Die()` at zero HP. `EventBus.EnemyKilledByHazard(Enemy, string)` and `EventBus.MemberKilledByHazard(int, string)` events with `Raise*` helpers.

- [ ] **Step 1: Add the EventBus events and the interface implementations** as specified. There is no engine-free test here; the headless test in Task 6 covers it.

- [ ] **Step 2: Build** (`dotnet build DungeonCrawler.csproj`), expected clean; run `godot --headless --path . -- --smoke` and confirm `[smoke] REPORT` still prints.

- [ ] **Step 3: Commit** `feat(hazards): enemies die, bosses stagger and NPCs go down to hazards`.

---

### Task 4: Hazard placement in the generator

**Files:**
- Create: `src/Dungeon/Generation/HazardPlacement.cs`
- Modify: `src/Dungeon/Generation/FloorLayout.cs` (`Hazards` list beside `TrapSpawns` line 37; `IsReserved` line 68; `ContentHash` line 153), `src/Dungeon/Generation/FloorGenerator.cs` (after `PlaceTraps` at line 50: `HazardPlanner.PlaceHazards(spec, new Random(seed ^ 0x4A2D), layout)`), `src/Dungeon/Generation/FloorSpec.cs` (add `public int Floor { get; init; } = 1;` and `public bool HazardsEnabled { get; init; } = true;`; `FloorSpec` already carries `BossFloor`, `Layout` and `Objective`, and every caller that builds a spec for a numbered floor sets `Floor`: the two places that already set `BossFloor` for a numbered floor, `src/Dungeon/Defs/TierDef.cs:67` and `src/Dungeon/DungeonBuilder.cs:134`)
- Test: `tests/Dungeon/HazardPlacementTests.cs`; re-baseline in `tests/Dungeon/LayoutKindTests.cs` and `tests/Dungeon/FloorObjectiveTests.cs` only if a fixed-hash assertion breaks (spec D3; floors 1-2 are unaffected)

**Interfaces:**
- Produces:
  - `public sealed record HazardPlacement(int Index, HazardKind Kind, IReadOnlyList<GridPos> Cells, HazardVariant Variant, GridPos Plate, GridPos? GuardsCell, bool Horizontal)` (`Cells` is 3 corridor cells for a Press, 1 to 9 cells for a bed; `Plate` is the middle cell for a Press and the first cell for a bed).
  - `public static class HazardCounts { static (int Press, int BedsMin, int BedsMax, bool PeriodicAllowed) For(int floor); }` per the Global Constraints table; floors 1-2 return zeros.
  - `public static class HazardPlanner { static void PlaceHazards(FloorSpec spec, Random rng, FloorLayout layout); static HashSet<GridPos> CriticalPath(FloorLayout layout); static bool IsBridgeCell(FloorLayout layout, GridPos cell); }` (`PlaceHazards` reads `spec.Floor`, `spec.HazardsEnabled`, `spec.Layout`, `spec.BossFloor`).
  - `FloorLayout.Hazards : List<HazardPlacement>`; `IsReserved` includes hazard cells; `ContentHash` folds hazards in last.

- [ ] **Step 1: Write the failing tests**

```csharp
public class HazardPlacementTests
{
    private static FloorLayout Gen(int seed, int floor, LayoutKind kind = LayoutKind.Standard, FloorObjectiveKind obj = FloorObjectiveKind.None)
        => FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = obj, Floor = floor }, seed);

    [Fact] public void Floors1And2_HaveNoHazards() { for (var s = 1; s <= 40; s++) { Assert.Empty(Gen(s, 1).Hazards); Assert.Empty(Gen(s, 2).Hazards); } }

    [Fact] public void FloorsWithoutHazards_HashAsBefore() { for (var s = 1; s <= 20; s++) Assert.Equal(FloorGenerator.Generate(FloorSpec.Default with { Floor = 1, HazardsEnabled = false }, s).ContentHash(), Gen(s, 1).ContentHash()); }

    [Theory] [InlineData(3)] [InlineData(8)] [InlineData(13)] [InlineData(18)]
    public void Counts_FollowTheTierTable(int floor)
    {
        var (press, bedsMin, bedsMax, _) = HazardCounts.For(floor);
        for (var s = 1; s <= 30; s++)
        {
            var l = Gen(s, floor);
            Assert.InRange(l.Hazards.Count(h => h.Kind == HazardKind.Press), 0, press);
            Assert.InRange(l.Hazards.Count(h => h.Kind == HazardKind.SpikeBed), 0, bedsMax);
        }
    }

    [Fact] public void AtMostOnePress_NeverOnLabyrinth()
    {
        for (var s = 1; s <= 30; s++)
        {
            Assert.True(Gen(s, 12).Hazards.Count(h => h.Kind == HazardKind.Press) <= 1);
            Assert.Empty(Gen(s, 12, LayoutKind.Labyrinth).Hazards.Where(h => h.Kind == HazardKind.Press));
        }
    }

    [Fact] public void Press_NeverOnDoorsOrNestMouths()
    {
        for (var s = 1; s <= 60; s++) foreach (var h in Gen(s, 9).Hazards.Where(h => h.Kind == HazardKind.Press))
        {
            var l = Gen(s, 9);
            Assert.Equal(3, h.Cells.Count);
            Assert.All(h.Cells, c => { Assert.Equal(Cell.Floor, l[c]); Assert.Null(l.RoomAt(c)); Assert.False(l.IsNestMouth(c)); });
            Assert.True(h.Cells.All(c => c.X == h.Cells[0].X) || h.Cells.All(c => c.Y == h.Cells[0].Y));
        }
    }

    [Fact] public void Press_NeverSevers_StartFromStairs()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 9);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.Press))
                Assert.Contains(l.DownStairs, l.Reachable(l.UpStairs, c => h.Cells.Contains(c)));
        }
    }

    [Fact] public void PressureBeds_StayOffTheCriticalPath_PeriodicMay()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 14); var path = HazardPlanner.CriticalPath(l);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.SpikeBed && h.Variant == HazardVariant.Pressure))
                Assert.DoesNotContain(h.Cells, path.Contains);
        }
    }

    [Fact] public void SpikeBeds_NeverTouchRampsPitsOrLedges()
    {
        for (var s = 1; s <= 60; s++)
        {
            var l = Gen(s, 8);
            foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.SpikeBed)) foreach (var c in h.Cells)
            {
                Assert.NotEqual(Cell.Pit, l[c]); Assert.NotEqual(Cell.Ramp, l[c]);
                foreach (var n in c.Neighbours4()) { Assert.False(l.Ramps.ContainsKey(n)); Assert.NotEqual(Cell.Pit, l[n]); Assert.False(l.Ledges.Any(g => g.Cells.Contains(n))); }
            }
        }
    }

    [Fact] public void NoHazards_OnFlooded_Beds_OrBurning_Beds_OrArena()
    {
        for (var s = 1; s <= 30; s++)
        {
            Assert.Empty(Gen(s, 6, LayoutKind.Flooded).Hazards.Where(h => h.Kind == HazardKind.SpikeBed));
            Assert.Empty(Gen(s, 10, LayoutKind.Burning).Hazards.Where(h => h.Kind == HazardKind.SpikeBed));
            var boss = Gen(s, 10, LayoutKind.Ring);
            Assert.All(boss.Hazards, h => Assert.All(h.Cells, c => Assert.False(boss.BossArena!.Contains(c))));
        }
    }

    [Fact] public void Hazards_AreReserved_AndAwayFromStairs()
    {
        for (var s = 1; s <= 30; s++) { var l = Gen(s, 15); foreach (var h in l.Hazards) foreach (var c in h.Cells) { Assert.True(l.IsReserved(c)); Assert.True(c.ManhattanDistance(l.UpStairs) > 2 && c.ManhattanDistance(l.DownStairs) > 2); Assert.DoesNotContain(c, l.TrapSpawns); Assert.DoesNotContain(c, l.EnemySpawns); Assert.DoesNotContain(c, l.ChestSpawns); } }
    }

    [Fact] public void Deterministic_PerSeed() { for (var s = 1; s <= 10; s++) Assert.Equal(Gen(s, 13).ContentHash(), Gen(s, 13).ContentHash()); }

    [Fact] public void MostHazards_GuardSomething()
    {
        var guarded = 0; var total = 0;
        for (var s = 1; s <= 60; s++) foreach (var h in Gen(s, 13).Hazards) { total++; if (h.GuardsCell != null) guarded++; }
        Assert.True(total > 0 && guarded >= total * 0.4, $"{guarded}/{total} guarded");
    }
}
```

`GridPos.ManhattanDistance` exists; add `GridPos.Neighbours4()` (the four orthogonal neighbours) beside it. `FloorLayout.IsNestMouth` is already used by `IsReserved`. `Room.Center` and `Room.Contains` exist; `Ledge.Cells` and `Structure.Cells` are the record fields.

- [ ] **Step 2: Run to verify failure** (`--filter HazardPlacementTests`).

- [ ] **Step 3: Implement.** `CriticalPath` = cells on `GridPathfinder.FindPath(layout, UpStairs, DownStairs)`. `IsBridgeCell(c)` = `!Reachable(UpStairs, x => x == c).Contains(DownStairs)`. Press candidates: runs of three `Cell.Floor` cells with `RoomAt == null`, both lateral neighbours of each cell being walls, no door or ramp or water or nest mouth in the run, no `Ramps` key or pit or ledge adjacent, no bridge cell in the run, at least three cells from either stairs, the start room and `Objective.Reserved`, and not within two cells of any `TrapSpawns`. Prefer runs whose far end leads (within 6 cells by path) to a `ChestSpawns`, `KeySpawns` or vault door cell and set `GuardsCell`. Spike bed candidates: a 2x2 or 3x3 block of room floor cells in rooms at least 5x5 (not arena, jail, shrine, cellar, under a ledge, not on Flooded or Burning floors) or a single corridor cell; pressure beds exclude critical path cells, periodic beds may include them; deeper half of the floor by path distance prefers Pressure. Variant for Caves and deeper only when `PeriodicAllowed`. `Index` counts from 0 per floor. Add hazard cells to `IsReserved` and fold `Hazards` into `ContentHash` after everything existing.

- [ ] **Step 4: Run the whole suite.** If `LayoutKindTests.StandardShape_Unchanged` or `FloorObjectiveTests.NoObjective_GeneratesExactlyAsBefore` fail, they compare two generations of the same floor against each other and must still pass (hazards are deterministic). A test that compares against a stored constant hash for floors 3 and up is re-baselined once and the commit message says so.

- [ ] **Step 5: Commit** `feat(hazards): deal the Press and Spike Beds after traps, reserved and hashed`.

---

### Task 5: Danger-aware pathing

**Files:**
- Modify: `src/Dungeon/Generation/GridPathfinder.cs` (both `FindPath` overloads line 12 and 15), `src/Dungeon/DungeonBuilder.cs` (near `IsCellBlocked` 169), `src/Enemies/Enemy.cs` (`MoveAlongPathToward` 544 and the straight-line shortcut under 5 m), `src/Party/PathMover.cs` (line 51), `src/Adventure/Core/NpcFlight.cs` (`PickFleeCell`)
- Test: add to `tests/Dungeon/VaultAndTrapTests.cs` (pathfinder tests live there at lines 76-92)

**Interfaces:**
- Produces: `GridPathfinder.FindPath(FloorLayout, GridPos, GridPos, Func<GridPos,bool>? blocked = null, int maxExpansions = 6000, Func<GridPos,bool>? passable = null, Func<GridPos,float>? danger = null, float dangerWeight = 0f)`; step cost `1 + danger(n) * dangerWeight`. `DungeonBuilder.DangerAt(GridPos) : float`, `SetDanger(GridPos, float)`, `ClearDanger(IEnumerable<GridPos>)`, `const float DangerWeightCompanion = 50f, DangerWeightSmart = 12f, DangerWeightMindless = 0f`; `Enemy.DangerWeight => Profile.IsHorde || Profile.IsBandit ? 0f : 12f`; `NpcFlight.PickFleeCell` gains `Func<GridPos,float>? danger` and rejects cells with danger > 0.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Pathfinder_AvoidsDangerWhenWeighted_IgnoresItAtZero()
{
    var layout = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Standard }, 7);
    var path = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs)!;
    var mid = path[path.Count / 2];
    float Danger(GridPos p) => p == mid ? 1f : 0f;
    var weighted = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, danger: Danger, dangerWeight: 50f)!;
    var mindless = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, danger: Danger, dangerWeight: 0f)!;
    Assert.Equal(path, mindless);
    if (!layout.Reachable(layout.UpStairs, c => c == mid).Contains(layout.DownStairs)) return; // mid is a bridge; nothing to detour through
    Assert.DoesNotContain(mid, weighted);
}
```

- [ ] **Step 2: Run to verify failure** (`--filter Pathfinder_AvoidsDanger`).

- [ ] **Step 3: Implement** the cost term in both overloads (the heuristic stays Manhattan). In `DungeonBuilder` keep a `Dictionary<GridPos,float> _danger`. `Enemy.MoveAlongPathToward` passes `builder.DangerAt` and `DangerWeight`, and the under-5 m straight-line shortcut is skipped when any cell along the segment has `DangerAt > 0` and `DangerWeight > 0`. `PathMover.Next` passes `DangerWeightCompanion`. `NpcFlight` callers pass `builder.DangerAt`.

- [ ] **Step 4: Run the suite and `godot --headless --path . -- --test-path`**, expected both pass.

- [ ] **Step 5: Commit** `feat(hazards): danger cost in A*, weighted by faction`.

---

### Task 6: The Press node

**Files:**
- Create: `src/Dungeon/Props/HazardPlate.cs`, `src/Dungeon/Props/Press.cs`, `scenes/Props/Press.tscn`
- Modify: `src/Dungeon/DungeonBuilder.cs` (spawn from `layout.Hazards` in `PlaceProps` 1008; `HazardArmed(int)`; `_hazards` list; `BuildGridMap` leaves the two side-wall cubes of each Press cell out so the slabs replace them), `src/Core/EventBus.cs` (`HazardPhase(int index, byte phase)`), `src/Core/DebugTools.cs` (`--hazard=press|spikes` forces one near the start cell on floor 1; `--test-hazard=press`)

**Interfaces:**
- Consumes: `HazardRules`, `IHazardVictim`, `HazardPlacement`, `DangerAt/SetDanger`.
- Produces: `HazardPlate : Area3D` (layer 0, mask 2|4, `Pressed` event fired for any `Adventurer`, `Enemy`, `Npc`, `Projectile` (arrow) or `Trap` spring entering; ignores bodies whose owner is Downed); `Press : Node3D` with `int Index`, `PressPhase Phase`, `float PhaseTime`, `bool Jammed`, `bool IsServer`, `void Trigger()`, `void ApplyPhase(PressPhase phase, float secondsIn)` (client), `event Action<Press, PressPhase> PhaseChanged`, two `AnimatableBody3D` children `SlabA`, `SlabB` (full height, 0.5 m thick, theme wall material with grooves via a second darker stripe material), an `Area3D Footprint` covering the three cells, decal `MeshInstance3D` toggled by `Spent > 0`. `DungeonBuilder.HazardArmed(int index) : bool` and `Hazards : IReadOnlyList<Node3D>`.

- [ ] **Step 1: Write the headless test in `DebugTools.RunHazardTest("press")`** that, on floor 1 with `--hazard=press --seed=4242 --lethality=3`: teleports the party to the plate cell; asserts `Phase` goes Armed, Warning, Closing within 3.7 s; teleports an enemy (`SpawnEnemy` of the tier's first regular) into the middle cell at Warning and asserts it is dead after Crush with `EnemyKilledByHazard` raised; holds the lead in the run through Crush and asserts `State.Mangled` is true, HP 0, and `MemberKilledByHazard` raised; asserts auto-revive does not fire after 5 s of calm; moves a companion within 2 m and calls `TryRescue`, asserts the member stands after 3 s with HP 1; asserts the Press is Armed again after Hold, Retract and Cooldown; places a Breakable in the run, triggers again and asserts `Jammed` with the gap at `JamGap` and no crush; finally saves with F5 semantics (`Game.Save`) and prints `[hazard] RESULT ok` or `FAIL: reason`. Wire the flag like `--test-tremor` (`_quitAfter = 40`).

- [ ] **Step 2: Run it to verify failure**: `godot --headless --path . -- --test-hazard=press` prints a FAIL (no Press exists yet).

- [ ] **Step 3: Implement `HazardPlate`, `Press` and the builder spawn.** Server tick: `PhaseTime += dt`; when `PhaseTime >= HazardRules.PressDuration(Phase)` advance with `NextPress`; on entering Closing move slabs each frame to `PressGap(PhaseTime / duration)`, stop at `JamGap` and set `Jammed` if a `Breakable` is inside the footprint (break it on the second jam); on entering Crush collect every `IHazardVictim` whose global position is inside the footprint and call `ReceiveHazard(HazardDamage.Crush, HazardRules.Outcome(v.HazardVictimKind, TierRules.Lethality), "The press")`, then push each Downed or Mangled body to the nearest walkable cell outside the footprint (`CellToWorld` of the cell with the smallest distance that is not in `Cells`), set `Spent++`, show the decal, raise `EventBus.RaiseHazardPhase`. `SetDanger` on the three cells: Armed 0, Warning ramps 0 to 1 over its duration, Closing and Crush and Hold 1, Retract 0.3, Cooldown 0. The plate is disabled during every phase but Armed. Clients only run `ApplyPhase` from Task 9 and animate.

- [ ] **Step 4: Run the headless test**, expected `[hazard] RESULT ok`; run `--test-floors` and confirm the hole count is unchanged (slabs at Armed leave the corridor walkable).

- [ ] **Step 5: Commit** `feat(hazards): the Press, a crushing corridor that kills anything and jams on a barrel`.

---

### Task 7: The Spike Bed node

**Files:**
- Create: `src/Dungeon/Props/SpikeBed.cs`, `scenes/Props/SpikeBed.tscn`
- Modify: `src/Dungeon/DungeonBuilder.cs` (spawn beds), `src/Core/DebugTools.cs` (`--test-hazard=spikes`)

**Interfaces:**
- Consumes: `HazardRules.SpikeDuration/NextSpike/PeriodicPhaseAt/CanDodgeSpikes`, `HazardPlate`, `IHazardVictim`.
- Produces: `SpikeBed : Node3D` with `Index`, `Variant`, `SpikePhase Phase`, `float PhaseTime`, `IReadOnlyList<GridPos> Cells`, a holed floor tile mesh per cell (dark holes, periodic variant adds a faint emissive pulse during Click), a spike mesh per cell that rises from -0.4 m to +1.0 m through Click (10 cm) and Up (full), `void Trigger()` (pressure plate on any cell), `ApplyPhase(SpikePhase, float)`, `event Action<SpikeBed, SpikePhase> PhaseChanged`. At the Down to Up transition the server collects every `IHazardVictim` whose feet cell is in `Cells` and whose body `IsOnFloor()` (for `Adventurer` and `Enemy`; `Npc` always on floor) and calls `ReceiveHazard(HazardDamage.Impale, ...)`, then pushes Downed bodies out of the patch. Danger: Down 0 (Pressure) or ramps 0 to 1 over the last second before Click (Periodic, derived from `PeriodicPhaseAt`), Click and Up 1, Retract 0.3, Rearm 0.

- [ ] **Step 1: Write `RunHazardTest("spikes")`**: with `--hazard=spikes --seed=4242`: a pressure bed near the start; walk the lead onto a cell, assert Click then Up within 1.6 s and the lead Mangled; rescue as in Task 6; spawn an enemy on the bed and trigger it, assert the enemy dies; set the bed to Periodic through a debug setter, step the server tick and assert the phase sequence matches `PeriodicPhaseAt`; teleport the lead above the bed with `Velocity.Y = 4` just before Up and assert no damage (`CanDodgeSpikes`). Print `[hazard] RESULT ok`.

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** per the interface; the periodic variant does not keep its own timer but derives phase from `NetSession.Tick` (server) via `PeriodicPhaseAt(tick, Index, out secondsIn)`.

- [ ] **Step 4: Run `--test-hazard=spikes`**, expected ok; run `--test-floors`, expected unchanged.

- [ ] **Step 5: Commit** `feat(hazards): spike beds, pressure and periodic, jumpable`.

---

### Task 8: Save state, v9 and old runs

**Files:**
- Modify: `src/Save/Core/SaveDtos.cs` (`HazardStateDto { int Index; byte Phase; float PhaseTime; int Spent; bool Jammed; }`, `FloorStateDto.Hazards : List<HazardStateDto>`, `AdventureDto.HazardsFromFloor : int` default 1, `CharacterDto.Mangled`, `MangledBy`), `src/Save/Core/SaveFormat.cs` (`CurrentVersion = 9`; migration case 8 sets `Adventure.HazardsFromFloor = CurrentFloor + 1` when the root has a current floor, else 1), `src/Save/FloorStateCapture.cs` (capture at line 44 region, restore at 89 region: a restored Press or bed resumes `Phase` with `PhaseTime`, and the builder skips spawning hazards on floors below `HazardsFromFloor`), `src/Dungeon/DungeonBuilder.cs` (honour `HazardsFromFloor`)
- Test: `tests/Save/SaveFormatTests.cs`

**Interfaces:**
- Consumes: `Press.Phase/PhaseTime/Spent/Jammed`, `SpikeBed.Phase/PhaseTime`.
- Produces: the DTOs above; `FloorStateCapture.Capture` fills `Hazards`; `Restore` applies them before the first physics tick.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void V8_To_V9_SetsHazardsFromFloor_AfterCurrentFloor()
{
    var root = JsonNode.Parse("{\"Version\":8,\"CurrentFloor\":6,\"Adventure\":{}}")!.AsObject();
    var migrated = SaveMigrations.Migrate(root);
    Assert.Equal(9, migrated["Version"]!.GetValue<int>());
    Assert.Equal(7, migrated["Adventure"]!["HazardsFromFloor"]!.GetValue<int>());
}

[Fact] public void HazardState_RoundTrips()
{
    var dto = new FloorStateDto { Hazards = { new HazardStateDto { Index = 0, Phase = (byte)PressPhase.Closing, PhaseTime = 1.1f, Spent = 2, Jammed = false } } };
    var back = JsonSerializer.Deserialize<FloorStateDto>(JsonSerializer.Serialize(dto, SaveFormat.Options), SaveFormat.Options)!;
    Assert.Equal(1.1f, back.Hazards[0].PhaseTime); Assert.Equal(2, back.Hazards[0].Spent);
}

[Fact] public void Restore_MidClosing_ResumesRemainingTime()
{
    // engine-free check of the resume arithmetic the node uses
    var remaining = HazardRules.PressDuration(PressPhase.Closing) - 1.1f;
    Assert.Equal(1.3f, remaining, 3);
    Assert.True(remaining > 0, "a restored Closing phase must still have time before Crush");
}
```

Adapt the migration entry point name to whatever `SaveFormat.cs` line 54 exposes (`Migrate(JsonObject)` on the class that holds the version switch).

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** DTOs, version bump, migration, capture and restore. `--test-hazard=press` already saves at its end; extend it to load (`Game.Load` of the same slot) and assert the Press phase and `Spent` survive.

- [ ] **Step 4: Run the suite and `--test-save`, `--test-hazard=press`**, expected pass.

- [ ] **Step 5: Commit** `feat(hazards): save v9 with hazard state; old runs get hazards only on unvisited floors`.

---

### Task 9: Co-op synchronisation

**Files:**
- Modify: `src/Net/Core/Protocol.cs` (append `HazardPhase, RescueIntent, ShoutIntent` to `MsgKind` after `ObjectiveDone`), `src/Net/Core/Messages.cs` (`[Msg(MsgKind.HazardPhase)] record HazardPhaseMsg(int Index, byte Kind, byte Phase, int ServerTick, int DurationTicks, bool Jammed, int Spent) : NetMessage`; `[Msg(MsgKind.RescueIntent)] record RescueIntent(int Member) : NetMessage`; `[Msg(MsgKind.ShoutIntent)] record ShoutIntent : NetMessage`), `src/Net/ServerWorld.cs` (subscribe `EventBus.HazardPhase`, broadcast; handle `RescueIntent` by calling `Party.TryRescue` with the sender's body; handle `ShoutIntent` by raising an Alert bark `"{name} needs help!"` through `EventBus.Log` and a `LogMsg`; `ApplyPose` rejects a pose inside a Press footprint while its phase is Hold), `src/Net/ClientWorld.cs` (`case HazardPhaseMsg` resolves the node by index and kind and calls `ApplyPhase(phase, secondsIn)` where `secondsIn = (NetSession.Tick - ServerTick + clockOffsetTicks) / 60f` using the existing Ping/Pong offset; `Adventurer.Interact` on a client with a Mangled target sends `RescueIntent`), `src/Net/NetRefResolver.cs` (`NetRefs.Hazard(int)` = `"hazard:{i}"`), `src/Net/NetTestFlow.cs` (client step: after the stairs step, trigger the forced Press and assert a `HazardPhaseMsg` with Phase Warning arrives; server trace gains `hazard:`)
- Test: `tests/Net/NetCodecTests.cs`

**Interfaces:**
- Consumes: `Press.ApplyPhase`, `SpikeBed.ApplyPhase`, `PartyController.TryRescue`.
- Produces: the three messages; `HazardPhaseMsg.Kind` is `(byte)HazardKind`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void HazardMessages_RoundTrip()
{
    var h = RoundTrip(new HazardPhaseMsg(2, (byte)HazardKind.Press, (byte)PressPhase.Closing, 1234, 144, false, 1));
    Assert.Equal(2, h.Index); Assert.Equal((byte)PressPhase.Closing, h.Phase); Assert.Equal(1234, h.ServerTick); Assert.Equal(144, h.DurationTicks);
    Assert.Equal(3, RoundTrip(new RescueIntent(3)).Member);
    _ = RoundTrip(new ShoutIntent());
}
```

`EveryMessageType_HasAUniqueKind` already guards the enum.

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** as specified. Clients never run the server tick of a hazard node: `Press._PhysicsProcess` returns early when `NetSession.IsClient` and only animates toward the phase it was last told, using `secondsIn` to start the slab interpolation at the right point.

- [ ] **Step 4: Run the suite and `scripts/net_test.sh`**, expected `NET TEST: ok`.

- [ ] **Step 5: Commit** `feat(hazards): server-owned hazard phases replicated to clients; rescue and shout intents`.

---

### Task 10: Co-op bound respawn and spectate

**Files:**
- Modify: `src/Party/Core/MangledRules.cs` (`static GridPos? BoundCell(FloorLayout layout, IReadOnlySet<GridPos> explored, IEnumerable<GridPos> livingCells, HashSet<GridPos> criticalPath)`: the first critical-path cell inside an explored room beyond the furthest living member's path index; null when none), `src/Net/ServerWorld.cs` (a per-member `MangledSince` clock; after `MangledRules.BoundAfterSeconds` with no rescue, move the body to `BoundCell`, set the existing Captured flag on the member's `PoseFlags`, log `"{name} is bound in the dark ahead."`; any living player's `InteractIntent` on that body frees it through `TryRescue` with the hazard check skipped), `src/Party/Adventurer.cs` (`SpectateTarget`: a Mangled local body on a client parents its camera to the nearest living teammate at 2 m behind and 1 m up, restored on revive), `src/UI/Hud/Hud.cs` (a one-line banner "Crushed. Rescue in Ns, or wait to be found" while Mangled on a client)
- Test: `tests/Party/MangledRulesTests.cs`

**Interfaces:**
- Consumes: `HazardPlanner.CriticalPath`, `ExplorationMap` explored cells, `PoseFlags`.
- Produces: `MangledRules.BoundCell`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public void BoundCell_IsAheadOfTheParty_AndExplored()
{
    var layout = FloorGenerator.Generate(FloorSpec.Default, 11);
    var path = HazardPlanner.CriticalPath(layout);
    var ordered = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs)!;
    var explored = new HashSet<GridPos>(ordered.Take(ordered.Count / 2).SelectMany(c => layout.RoomAt(c)?.Cells ?? new[] { c }));
    var living = new[] { ordered[ordered.Count / 4] };
    var cell = MangledRules.BoundCell(layout, explored, living, path);
    Assert.NotNull(cell);
    Assert.Contains(cell!.Value, explored);
    Assert.True(ordered.IndexOf(cell.Value) > ordered.IndexOf(living[0]));
    Assert.NotNull(layout.RoomAt(cell.Value));
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** the rule and the server and client behaviour. In single-player nothing changes (the local body is rescued by a companion, Task 11).

- [ ] **Step 4: Run the suite; run `scripts/net_test.sh`.**

- [ ] **Step 5: Commit** `feat(hazards): bound respawn ahead and spectate for a crushed co-op player`.

---

### Task 11: Companions rescue, taunts and alerts

**Files:**
- Modify: `src/Characters/Companions/CompanionBrain.cs` (`ActionKind.Rescue`; `CompanionContext.MangledMemberId : int = -1`, `MangledDistance : float`, `NearestEnemyDistance : float`; `Decide` returns `Rescue` when `MangledMemberId >= 0 && NearestEnemyDistance > 12f && Stance != Stance.Hold`, before `Follow`), `src/Party/Adventurer.cs` (`Think` fills the new context fields; `Rescue` walks to within `MangledRules.RescueRange` of the member and calls `Party.TryRescue`), `src/Adventure/Core/Objectives/Taunts.cs` (`TauntTrigger.Crushed` appended; two stock lines with clean twins: `"Oh, good. The Press works. Nobody tells me anything down here."` / same, and `"Pressed and filed. Thank you for your custom, and for the spine, you poor bugger."` / `"Pressed and filed. Thank you for your custom."`), `src/Adventure/AdventureDirector.cs` (subscribe `MemberKilledByHazard` to `Taunt(TauntTrigger.Crushed)` and `EnemyKilledByHazard` to a `Kill` bark for the nearest member), `src/UI/Hud/Hud.cs` (the interact prompt shows `Rescue {name}` from `Adventurer.Prompt`)
- Test: `tests/Characters/CompanionBrainTests.cs`, `tests/Adventure/TauntTests.cs`

**Interfaces:**
- Consumes: `MangledRules`, `PartyController.TryRescue`.
- Produces: `ActionKind.Rescue`, `TauntTrigger.Crushed`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Companion_Rescues_WhenCalm_AndSomeoneIsMangled()
{
    var ctx = new CompanionContext { MangledMemberId = 2, MangledDistance = 6f, NearestEnemyDistance = 30f, Enemies = Array.Empty<EnemyView>() };
    Assert.Equal(ActionKind.Rescue, CompanionBrain.Decide(ctx).Kind);
    Assert.Equal(2, CompanionBrain.Decide(ctx).TargetId);
}
[Fact] public void Companion_DoesNotRescue_UnderFire() => Assert.NotEqual(ActionKind.Rescue, CompanionBrain.Decide(new CompanionContext { MangledMemberId = 2, NearestEnemyDistance = 5f }).Kind);
[Fact] public void Companion_OnHold_DoesNotRescue() => Assert.NotEqual(ActionKind.Rescue, CompanionBrain.Decide(new CompanionContext { MangledMemberId = 2, NearestEnemyDistance = 30f, Stance = Stance.Hold }).Kind);

[Fact] public void Stock_HasCrushedTaunts_WithCleanTwins()
{
    var campaign = new CampaignPlan(); TauntBank.Fill(campaign);
    var crushed = campaign.Taunts.Where(t => t.Trigger == nameof(TauntTrigger.Crushed)).ToList();
    Assert.True(crushed.Count >= 2);
    Assert.All(crushed, t => Assert.False(string.IsNullOrWhiteSpace(t.CleanLine?.Text)));
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**; extend `--test-hazard=press` so the rescue step is done by a companion on Defensive with no enemy near, instead of a scripted `TryRescue` call, and assert the Crushed taunt was raised (`EventBus.Narration` seen with a line from the bank).

- [ ] **Step 4: Run the suite, `--test-hazard=press`, `--test-party`, `--test-objective=ritual`** (the taunt bank change must not disturb objective taunts), expected pass.

- [ ] **Step 5: Commit** `feat(hazards): companions pull the crushed free; the villain gloats`.

---

### Task 12: Settings, README and the shipped campaign

**Files:**
- Modify: `src/UI/Menus/SettingsScreen.cs` (under "Danger" at line 78: the Lethality slider's tooltip gains "2.5 and above: presses and spikes kill outright"), `README.md` (a "Lethal traps" paragraph in the dungeon section: what the Press and Spike Beds are, the tells, the jam, the rescue, the flags `--hazard=`, `--test-hazard=`, the save v9 note and `HazardsFromFloor`), `data/adventures/builtin/floor_03.json`, `floor_18.json`, `floor_19.json` are untouched (placement comes from the generator), but run `godot --headless --path . -- --builtin --seed=7319 --quit-after=14` and `--test-plan` to confirm the shipped plans still resolve with hazard cells reserved.

- [ ] **Step 1: Edit the tooltip and the README.**
- [ ] **Step 2: Run `--builtin --seed=7319 --quit-after=14`, `--test-plan`, `--test-floors`, `--smoke`, and the full `dotnet test`**, expected all pass and `[builtin] RESULT ok`.
- [ ] **Step 3: Commit** `docs(hazards): settings tooltip and README for the Press and Spike Beds`.

---

### Task 13: Boss arenas that use the floor (from the campaign bible, section 9)

**Files:**
- Modify: `src/Dungeon/Generation/FloorGenerator.Kinds.cs` (`PlaceFires` on a Burning boss floor biases fire cells into a ring 1.5 to 2.5 cells around the arena centre: the Hatchery), `src/Dungeon/Generation/FloorGenerator.cs` (`PlaceStructures` allows a Ruin inside a Ring boss arena when `spec.Floor % 5 == 0 && spec.Floor >= 20`: the Colonnade; `Verticality` allows one Ledge on a Ring boss arena wall when `spec.Floor == 5`: the Gallery), `src/Dungeon/Generation/HazardPlacement.cs` (on floor 15 a Press may sit in a spoke corridor adjoining the arena: the Gauntlet; the arena interior stays forbidden), `src/Dungeon/Props/Press.cs` (boss victims use `HazardRules.Outcome(VictimKind.Boss, ...)`, already handled in Task 3)
- Test: `tests/Dungeon/HazardPlacementTests.cs` and `tests/Dungeon/StructureTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: no new public surface; generator behaviour on floors 5, 10, 15, 20.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Floor15_MayPlaceAPressInAnArenaSpoke_NeverInside()
{
    var any = false;
    for (var s = 1; s <= 40; s++)
    {
        var l = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Ring, Floor = 15 }, s);
        foreach (var h in l.Hazards.Where(h => h.Kind == HazardKind.Press)) { any = true; Assert.All(h.Cells, c => Assert.False(l.BossArena!.Contains(c))); }
    }
    Assert.True(any);
}
[Fact] public void Floor10_Burning_RingsTheArenaCentreWithFire()
{
    var l = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Burning, Floor = 10 }, 3);
    var centre = l.BossArena!.Center;
    var ring = l.Fires.Count(f => { var d = f.ManhattanDistance(centre); return d >= 1 && d <= 3; });
    Assert.True(ring >= 4);
    Assert.DoesNotContain(centre, l.Fires);
}
[Fact] public void Floor20_Ring_HasARuinInTheArena()
{
    var l = FloorGenerator.Generate(FloorSpec.Default with { Layout = LayoutKind.Ring, Floor = 20 }, 5);
    Assert.Contains(l.Structures, s => s.Kind == StructureKind.Ruin && s.Cells.Any(c => l.BossArena!.Contains(c)));
}
```

`Room.Center` is the existing helper.

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** the four biases. The Gallery ledge on floor 5 is a placement rule inside `Verticality.PlaceLedges` (one ledge on an arena wall, archer spawn as usual); add a test in `tests/Dungeon/VerticalityTests.cs` asserting floor 5 Ring has a ledge touching the arena.

- [ ] **Step 4: Run the suite, `--test-structures`, `--test-floors`, and `--builtin --seed=7319 --floor=5 --quit-after=12`, `--floor=10`, `--floor=15`, `--floor=20`**, expected pass and the floors build.

- [ ] **Step 5: Commit** `feat(arenas): the Gallery, the Hatchery, the Gauntlet and the Colonnade`.

---

## Self-review notes

- Spec coverage: C2 (Tasks 1-3), C3 (6), C4 (7), C5 (4, 13), C6 (2, 10, 11), C7 (5), C8 (9), C9 (11), D2 tests (each task), D3 (4 step 4, 8). The bible's arena asks are Task 13; its other asks (cast-as-ritualist, twin plates, fighter-left flag) belong to Phase 12 and are not here.
- Names used consistently: `HazardKind`, `HazardVariant`, `PressPhase`, `SpikePhase`, `HazardOutcome`, `VictimKind`, `IHazardVictim.ReceiveHazard`, `HazardPlacement`, `HazardPlanner.CriticalPath`, `MangledRules`, `DangerAt/SetDanger`, `HazardPhaseMsg`, `TryRescue`.
- `FloorSpec` has no floor index today (it has `BossFloor`, `Layout`, `Objective`); Task 4 adds `Floor` and `HazardsEnabled` and wires the callers. `FloorSpec.Default` exists.
- Proportion: 13 tasks, about 330 lines against a 600-line spec; test code is the bulk by design.
