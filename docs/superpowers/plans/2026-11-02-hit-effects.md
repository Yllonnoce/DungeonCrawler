# Hit Effects Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sword hits bleed, arrows stay stuck in the enemy they hit, spells burst where they land (enemy or wall), and enemies grunt when hurt and when they die, in solo and in co-op.

**Architecture:** One static helper class `HitFx` builds cheap one-shot effects (particles that free themselves, a thin arrow mesh under the enemy's model). The game's existing hit sites call it: `Enemy.ReceiveAttack` for blood and the hurt grunt, `Enemy.OnDeath` / `ClientDie` for the death grunt, `Projectile.OnBodyEntered` for arrows and spell bursts. Grunt clips are CC0 files registered in `AudioManager` under `grunt_hurt_<set>` / `grunt_death_<set>`, the set chosen by an engine-free rule from the enemy's size. Co-op: spell bursts are local on every machine (each machine already has the bolt); blood and stuck arrows travel as one new `HitFxEvt` message from the server; grunts travel through the existing `SfxMsg` relay.

**Tech Stack:** Godot 4.7.2 .NET (C#), GpuParticles3D with ParticleProcessMaterial built in code (precedent: `src/Dungeon/Props/Fire.cs`), xUnit for the engine-free rules, headless `--test-hit-fx` flow for everything in-engine.

**Spec:** No section of `docs/superpowers/specs/2026-11-02-tavern-merchant-safe-rooms-design.md` covers this. The requirements are the owner's requests of 2026-11-02 ("some animation when the mage hits the a object", "blood from the sword attacks and the arrow stick into the enemy", "voices for the minions grunts if possible when they get hit and die") and the contract in `docs/superpowers/plans/2026-11-02-tavern-shared-interfaces.md`, section F, which this plan follows verbatim.

## Global Constraints

- Godot 4.7.2 .NET, C#; build with `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` before every headless run (the shared NTFS folder has clock skew; incremental builds skip compiles).
- Engine-free code goes in `src/Enemies/Core/` (already linked by `tests/DungeonCrawler.Tests.csproj` line 31) and is tested in `tests/Enemies/`; the test project lives only on the development machine (never `git add tests`).
- Effects are cheap: `GpuParticles3D` with `OneShot = true`, `Explosiveness = 1`, lifetime ≤ 0.6 s, freed by their own countdown; no per-frame allocations; at most 6 stuck arrows visible per enemy.
- Audio sources are CC0 only and are listed in `assets/LICENSES.md`; the game keeps working with no grunt files present.
- Co-op is server-authoritative; clients never roll game state, only play what they are told. New messages append to `MsgKind` (never reorder) and are declared in `src/Net/Core/Messages.cs` with `[Msg]`.
- Existing headless flows (`--test-abilities --no-ambush --no-portals`, `scripts/net_test.sh`) must still print `RESULT ok`.
- Engine-free tests cover only the voice-set rule and the net kinds; the headless flow carries the rest of the proof, by design.

## Review Focus

- An enemy restored from a save with 40 embedded arrows must show at most 6 arrow meshes and run at full speed (Task 5 headless check: `EmbeddedArrows = 40` on `RestoreFrom` → 6 children named `StuckArrow`).
- Floor change while blood or a burst is still alive must not throw: the effect is a child of the floor root and its countdown must tolerate being freed early (Task 4 headless check: spawn, change floor at once, no error line).
- A voice set with no clips on disk falls back to the old `enemy_hurt` / `enemy_die` cues rather than silence (Task 3 headless check with a bogus `VoiceSet`).
- A `HitFxEvt` for an enemy the client has not spawned yet, or that is already freed, is ignored without an exception (Task 6: `TryGetValue` + `IsInstanceValid` guard, checked by the net test's trace staying clean).
- A client must not show a spell burst twice (its own cosmetic bolt and a relayed event): bursts are never relayed; the `HitFxKind` enum has only `Blood` and `Arrow` (Task 6 unit test pins the enum).

---

## File structure

- Create `src/Enemies/Core/VoiceSets.cs`: the size → set rule and cue names (engine-free).
- Modify `src/Enemies/Core/EnemyProfile.cs`, `src/Enemies/Defs/EnemyDef.cs`: `VoiceSet` field.
- Create `scripts/fetch_grunts.py`; modify `assets/LICENSES.md`; new files under `assets/audio/grunts/<set>/`.
- Modify `src/Audio/AudioManager.cs`: grunt cues discovered from disk, per-set pitch, `Has(cue)`.
- Create `src/Combat/Fx/HitFx.cs` (helpers) and `src/Combat/Fx/OneShotFx.cs` (a `GpuParticles3D` that frees itself).
- Modify `src/Enemies/Enemy.cs` (hooks, `Model` accessor, stuck-arrow restore), `src/Combat/Projectile.cs` (hooks).
- Create `src/Net/Core/HitFxKind.cs` (engine-free enum); modify `src/Net/Core/Protocol.cs`, `src/Net/Core/Messages.cs`, `src/Net/ServerWorld.cs`, `src/Net/ClientWorld.cs`, `src/Core/EventBus.cs`.
- Modify `src/Core/DebugTools.cs` (`--test-hit-fx`), `docs/development.md`.
- Tests: `tests/Enemies/VoiceSetsTests.cs`, `tests/Net/HitFxKindTests.cs` (create `tests/Net/` if missing; `src/Net/Core` is linked at csproj line 34).

---

### Task 1: Voice sets (engine-free)

**Files:**
- Create: `src/Enemies/Core/VoiceSets.cs`
- Modify: `src/Enemies/Core/EnemyProfile.cs:37-39` (add a property), `src/Enemies/Defs/EnemyDef.cs:38-41` and its `ToProfile()`
- Test: `tests/Enemies/VoiceSetsTests.cs`

**Interfaces:**
- Produces: `public static class VoiceSets { public const string Small = "small", Medium = "medium", Large = "large", Boss = "boss"; public static readonly string[] All; public static string DefaultFor(float scale, bool isBoss); public static string Resolve(string voiceSet, float scale, bool isBoss); public static string HurtCue(string set) => $"grunt_hurt_{set}"; public static string DeathCue(string set) => $"grunt_death_{set}"; public static float PitchOffset(string set); }`
  Rules: `isBoss` → `Boss`; `scale < 0.85f` → `Small`; `scale >= 1.25f` → `Large`; else `Medium`. `Resolve` returns `voiceSet` when it is non-empty, else `DefaultFor`. `PitchOffset`: small +0.25, medium 0, large −0.25, boss −0.4, unknown 0.
- Produces: `EnemyProfile.VoiceSet` (`string`, init, default `""`); `EnemyDef.VoiceSet` (`[Export] string`, default `""`, copied in `ToProfile()`).

- [ ] **Step 1: Write the failing tests**

```csharp
public class VoiceSetsTests
{
    [Theory]
    [InlineData(0.6f, false, "small")] [InlineData(0.8f, false, "small")] [InlineData(0.85f, false, "medium")]
    [InlineData(1.0f, false, "medium")] [InlineData(1.3f, false, "large")] [InlineData(2.0f, false, "large")]
    [InlineData(1.0f, true, "boss")] [InlineData(0.6f, true, "boss")]
    public void DefaultFor_FollowsScaleAndBossFlag(float scale, bool boss, string expected) => Assert.Equal(expected, VoiceSets.DefaultFor(scale, boss));

    [Fact] public void Resolve_PrefersAnExplicitSet() { Assert.Equal("large", VoiceSets.Resolve("large", 0.6f, false)); Assert.Equal("small", VoiceSets.Resolve("", 0.6f, false)); }
    [Fact] public void Cues_AreNamedBySet() { Assert.Equal("grunt_hurt_small", VoiceSets.HurtCue("small")); Assert.Equal("grunt_death_boss", VoiceSets.DeathCue("boss")); }
    [Fact] public void Pitch_DropsWithSize() { Assert.True(VoiceSets.PitchOffset("small") > VoiceSets.PitchOffset("medium")); Assert.True(VoiceSets.PitchOffset("medium") > VoiceSets.PitchOffset("large")); Assert.True(VoiceSets.PitchOffset("large") > VoiceSets.PitchOffset("boss")); Assert.Equal(0f, VoiceSets.PitchOffset("nonsense")); }
    [Fact] public void All_ListsTheFourSets() => Assert.Equal(new[] { "small", "medium", "large", "boss" }, VoiceSets.All);
    [Fact] public void ShippedEnemies_SplitAcrossTheSets() { /* read data/enemies/*.tres Scale and IsBoss like tests/Items/DataFiles.cs does; assert every set gets at least one enemy */ }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~VoiceSets"`
Expected: build error, `VoiceSets` does not exist.

- [ ] **Step 3: Implement `VoiceSets` and the `VoiceSet` property on `EnemyProfile` and `EnemyDef`**

- [ ] **Step 4: Run to verify it passes**

Run: the same command. Expected: all pass. Also `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Enemies/Core/VoiceSets.cs src/Enemies/Core/EnemyProfile.cs src/Enemies/Defs/EnemyDef.cs
git commit -m "feat(enemies): a voice set per enemy, chosen by size unless the definition names one"
```

---

### Task 2: Grunt clips on disk and in the audio table

**Files:**
- Create: `scripts/fetch_grunts.py`
- Modify: `assets/LICENSES.md` (new section "Enemy grunts (CC0)"), `src/Audio/AudioManager.cs:20-70` (table), `:120-135` (`Play`)
- Create (by the script): `assets/audio/grunts/{small,medium,large,boss}/hurt_N.ogg`, `death_N.ogg` (N from 1), plus `assets/audio/grunts/README.md` saying where they came from.

**Interfaces:**
- Consumes: `VoiceSets.All`, `VoiceSets.PitchOffset`.
- Produces: `AudioManager.Has(string cue) : bool`; cues `grunt_hurt_<set>` and `grunt_death_<set>` registered at startup for every set whose folder holds at least one matching file (`DirAccess.Open("res://assets/audio/grunts/<set>")`, files matching `hurt_*.ogg` / `death_*.ogg`, also `.wav`); `Play(cue, volumeDb)` adds `VoiceSets.PitchOffset(set)` to the pitch for grunt cues (parse the set from the cue name), keeping the existing random jitter.

- [ ] **Step 1: Write `scripts/fetch_grunts.py`** after the pattern of `scripts/fetch_music.py` (download, skip what is present, print `have`/`fetched`/`FAILED`, exit 1 on failure, append to `assets/LICENSES.md` only lines not already there). Source, in this order of preference, the implementer verifying each URL before relying on it: (1) Kenney "Voiceover Pack: Fighter" (`https://kenney.nl/assets/voiceover-pack-fighter`, CC0; zip link scraped from the page as the other Kenney packs were) for `medium` (as is), `small` (same files; the pitch offset makes them small) and `large`; (2) any other CC0 creature/monster hurt and death set on OpenGameArt whose licence page says CC0, for `large` and `boss` if found, otherwise `large`/`boss` reuse the fighter files and rely on the pitch offset. Copy as `hurt_1.ogg..hurt_N.ogg` (N ≤ 6) and `death_1.ogg..death_N.ogg` (N ≤ 4) per set, converting with `ffmpeg` if the source is `.wav`/`.mp3` (print how to install it if missing). If no CC0 source at all can be reached, the script must say so and exit 1; the game then falls back (Task 3). Also write `assets/audio/grunts/README.md` listing each file's origin.

- [ ] **Step 2: Run the script and import**

Run: `python3 -I scripts/fetch_grunts.py && godot --headless --path . --import > /dev/null 2>&1; ls assets/audio/grunts/*/ | head`
Expected: four folders with `hurt_*.ogg` and `death_*.ogg`, `assets/LICENSES.md` gains the section.

- [ ] **Step 3: Register the cues in `AudioManager`**: in `_Ready`, after the static table is loaded, scan the four folders and add `grunt_hurt_<set>` / `grunt_death_<set>` entries; add `public bool Has(string cue)`; in `Play`, for cues starting with `grunt_`, `PitchScale = 0.92f + jitter + VoiceSets.PitchOffset(set)` clamped to [0.5, 1.6].

- [ ] **Step 4: Verify**

Run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly; timeout 60 godot --headless --path . -- --quit-after=4 2>&1 | grep -E "\[Audio\]|ERROR" | head`
Expected: a line the implementer adds once at startup, `[Audio] grunt sets: small=6/4 medium=6/4 large=6/4 boss=6/4` (hurt/death counts), and no ERROR lines.

- [ ] **Step 5: Commit**

```bash
git add scripts/fetch_grunts.py assets/LICENSES.md assets/audio/grunts src/Audio/AudioManager.cs
git commit -m "feat(audio): CC0 grunt and death clips per enemy voice set, pitched by size"
```

---

### Task 3: Enemies grunt when hurt and when they die

**Files:**
- Modify: `src/Enemies/Enemy.cs:694` (hurt), `:776-777` (`OnDeath`), `:326-335` (`ClientDie`), the horde/hazard death path at `:330` if it is a separate method
- Modify: `src/Core/DebugTools.cs` (new flag `--test-hit-fx`, new method `RunHitFxTest`, dispatched beside `_testAbilities` at `:706`; flag at `:240` sets `_quitAfter = 40` and `NoPortals = true`, no ambushes)

**Interfaces:**
- Consumes: `VoiceSets.Resolve/HurtCue/DeathCue`, `AudioManager.Instance.Has(cue)`.
- Produces: `Enemy.VoiceSet : string` (resolved once in `_Ready` from `Profile.VoiceSet`, `Profile.Scale`, `IsBoss`); `Enemy.HurtCue`, `Enemy.DeathCue` (the grunt cue when `Has` is true, else `"enemy_hurt"` / `"enemy_die"`). The boss keeps its `"boss"` bell as well: `OnDeath` plays `DeathCue` and, for a boss, also `"boss"`.
- Produces: the headless flow skeleton `RunHitFxTest` with steps driven by `_elapsed`, printing `[hitfx] ...` lines and a final `[hitfx] RESULT ok|FAIL (n)`; stage a fight the way `RunAbilitiesTest` does (`DebugTools.cs:2696-2760`: teleport the party to the largest room's centre, spawn `b.RegularEnemies(floor)[0]` 1.5 m ahead, pre-stun it, park the other heroes, possess Brand). Record cues through a test hook: `EventBus.Sfx` subscription collecting cue names into a list during the step.

- [ ] **Step 1: Write the headless check (fails first)**: step A spawns the enemy and makes Brand attack once (`party.Attack(0)`); 0.6 s later assert the collected cues contain `enemy.HurtCue` and that `HurtCue` starts with `grunt_hurt_` when `AudioManager.Instance.Has(...)`; step B sets the enemy's `Hp` to 1 through another attack or `Kill()`, 0.6 s later assert `DeathCue` was played; step C spawns a second enemy, sets `VoiceSet` to `"nonsense"` before `_Ready` (spawn, then set `Profile`… if that is not possible, add a test-only `Enemy.OverrideVoiceSet(string)`), hits it, asserts the fallback `enemy_hurt` was played. Print `[hitfx] grunts: hurt=<cue> death=<cue> fallback=<cue>`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly; timeout 90 godot --headless --path . -- --test-hit-fx 2>&1 | grep "^\[hitfx\]"`
Expected: FAIL lines: hurt cue is `enemy_hurt`, no `grunt_` cue.

- [ ] **Step 3: Implement** `VoiceSet` resolution in `_Ready`, `HurtCue`/`DeathCue`, replace the three `PlaySfx` calls; keep `ClientDie` symmetrical (it plays `DeathCue` locally today's way; check `ServerWorld.LocalCues` at `src/Net/ServerWorld.cs:50` and add the grunt cues to it if `enemy_die` is there, so clients do not hear a death twice).

- [ ] **Step 4: Run to verify it passes**

Run: the same command. Expected: `[hitfx] RESULT ok` with `hurt=grunt_hurt_medium death=grunt_death_medium fallback=enemy_hurt` (set depends on the floor's first regular enemy). Also run `timeout 240 godot --headless --path . -- --test-abilities --no-ambush --no-portals 2>&1 | grep RESULT` → `RESULT ok`.

- [ ] **Step 5: Commit**

```bash
git add src/Enemies/Enemy.cs src/Core/DebugTools.cs src/Net/ServerWorld.cs
git commit -m "feat(enemies): grunts on hurt and death from the enemy's voice set, with the old cues as fallback"
```

---

### Task 4: Blood on melee hits, bursts where spells land

**Files:**
- Create: `src/Combat/Fx/OneShotFx.cs`, `src/Combat/Fx/HitFx.cs`
- Modify: `src/Enemies/Enemy.cs:683-700` (`ReceiveAttack`), `src/Combat/Projectile.cs:126-172` (`OnBodyEntered`: enemy, `ICombatTarget`, default branches; the `Cosmetic` branch), `src/Core/DebugTools.cs` (`RunHitFxTest` steps D and E)

**Interfaces:**
- Produces: `public partial class OneShotFx : GpuParticles3D { public float Ttl { get; set; } = 1.0f; }` whose `_Process` counts `Ttl` down and calls `QueueFree()` at 0; `Emitting = true`, `OneShot = true`, `Explosiveness = 1f` set by the helper.
- Produces: `public static class HitFx { public static void Blood(Node parent, Vector3 at, Vector3 towardAttacker); public static void SpellBurst(Node parent, Vector3 at, Color color); public static void StickArrow(Enemy enemy, Vector3 worldAt, Vector3 direction); public static Texture2D Puff(); }`
  `Blood`: an `OneShotFx` at `at`, `Amount = 24`, `Lifetime = 0.5`, `Ttl = 1.0`, process material: direction `towardAttacker` with `Spread = 35`, `InitialVelocityMin/Max = 2.5/4.5`, gravity `(0, -9.8, 0)`, colour ramp dark red (0.55, 0.02, 0.02) to transparent, `EmissionShape = Sphere` radius 0.12, quad mesh 0.10 × 0.10 with the shared `Puff()` texture (`Dungeon.Props.Fire.Puff()` is public static; reuse it through `HitFx.Puff`), unshaded alpha-blend billboard material. `SpellBurst`: same shape with `Amount = 32`, velocity 1.5/3.5, no gravity, `Spread = 180`, colour ramp from `color` (alpha 0.9) to transparent, additive blend, plus an `OmniLight3D { LightColor = color, LightEnergy = 3, OmniRange = 4 }` child that the `OneShotFx` fades to 0 over its first 0.25 s. `StickArrow`: Task 5.
- Hooks: in `Enemy.ReceiveAttack`, after a hit with `attack.Type == DamageType.Physical && attack.Kind == AttackKind.Melee`: `HitFx.Blood(GetParent(), GlobalPosition + Vector3.Up * 0.9f * Profile.Scale, towardAttacker)` where `towardAttacker` is the direction to the party's nearest adventurer (or `-GlobalTransform.Basis.Z` if none) — the enemy knows no attacker position today; use `Party?.Lead.GlobalPosition` as the approximation. In `Projectile.OnBodyEntered`, for a non-arrow bolt that is spent on the enemy, `ICombatTarget` or default branch: `HitFx.SpellBurst(GetParent(), GlobalPosition, Color)`; in the `Cosmetic` branch: the same burst when `!IsArrow`.

- [ ] **Step 1: Extend the headless check (fails first)**: step D: Brand attacks the stunned enemy once; next frame count `root.GetChildren().OfType<OneShotFx>()` == 1 (blood) and 1.2 s later == 0; print `[hitfx] blood: spawned=1 gone=true`. Step E: possess Ilex, select Fireball, attack; after 0.4 s at least one `OneShotFx` with an `OmniLight3D` child exists (burst) and the enemy was hurt; then face the wall exactly as `RunAbilitiesTest`'s wall step does and cast again; after 0.6 s a burst exists near the wall face (within 0.6 m of the ray's hit point) and no `Projectile` remains. Step F: cast Fireball then call `Game.Instance.JumpToFloor(current + 1)` within the same frame; next floor loads with no `ERROR` line (reviewer focus 2).

- [ ] **Step 2: Run to verify it fails**: `grep "^\[hitfx\]"` shows `blood: spawned=0` FAIL.

- [ ] **Step 3: Implement `OneShotFx`, `HitFx.Blood`, `HitFx.SpellBurst` and the hooks.**

- [ ] **Step 4: Run to verify it passes** (`--test-hit-fx` → `RESULT ok`; `--test-abilities --no-ambush --no-portals` → `RESULT ok`; `grep -c ERROR` on the output → 0).

- [ ] **Step 5: Commit**

```bash
git add src/Combat/Fx src/Enemies/Enemy.cs src/Combat/Projectile.cs src/Core/DebugTools.cs
git commit -m "feat(fx): blood on melee hits and a burst where a spell lands, one-shot particles that free themselves"
```

---

### Task 5: Arrows stay in the enemy

**Files:**
- Modify: `src/Combat/Fx/HitFx.cs` (`StickArrow`), `src/Enemies/Enemy.cs:219-244` (expose the model), `:732-736` (`RestoreFrom`), `src/Combat/Projectile.cs:140-141` (the `IsArrow && result.Hit` line), `src/Core/DebugTools.cs` (steps G and H)

**Interfaces:**
- Consumes: `Enemy.EmbeddedArrows` (exists, saved in `EnemyDto.EmbeddedArrows`, `SaveDtos.cs:251`).
- Produces: `Enemy.Model : Node3D?` (the `_model` node, null for a stock capsule); `public const int MaxStuckArrows = 6`; `Enemy.StuckArrowCount` (children of `Model` named `StuckArrow`).
- Produces: `HitFx.StickArrow(Enemy enemy, Vector3 worldAt, Vector3 direction)`: no-op when `enemy.Model == null` or `enemy.StuckArrowCount >= MaxStuckArrows`; otherwise a `Node3D` named `StuckArrow` under `enemy.Model` holding a shaft (`BoxMesh 0.025 × 0.025 × 0.55`, brown 0.35/0.22/0.10) and fletching (`BoxMesh 0.07 × 0.012 × 0.09` at the tail, grey-white), positioned at `enemy.Model.ToLocal(worldAt - direction * 0.18f)` (the head buried) and oriented with `LookAt` along `direction`; scale compensates `enemy.Model.Scale` so the arrow is 0.55 m in world units.
- `Enemy.RestoreFrom`: after setting `EmbeddedArrows`, re-create `Math.Min(EmbeddedArrows, MaxStuckArrows)` arrows at seeded torso points: `var rng = new Random(NetId)`; point = `GlobalPosition + Vector3.Up * (0.6f + rng.NextSingle() * 0.7f) * Profile.Scale + horizontal offset of radius 0.25 × Scale at a random angle`, direction = the horizontal unit vector from that angle pointing inward, tilted down 15°. This runs after the model exists (call it from `_Ready` when `EmbeddedArrows > 0`, since `RestoreFrom` may run before `_Ready`; the implementer checks the order and places the call where `Model` is non-null).
- `Projectile`: where `EmbeddedArrows++` happens, also `HitFx.StickArrow(enemy, GlobalPosition, Velocity.Normalized())`.

- [ ] **Step 1: Extend the headless check (fails first)**: step G: possess Wren, basic bow attack at the stunned enemy three times (0.5 s apart); assert `enemy.StuckArrowCount == 3` and `EmbeddedArrows == 3`; then set `EmbeddedArrows = 40` and call the restore path on a freshly spawned enemy (`RestoreFrom(new EnemyDto { EmbeddedArrows = 40, Hp = ..., X/Y/Z = ... })` then let a frame pass) → `StuckArrowCount == 6`. Step H: kill the first enemy and assert its arrows are still children (corpses keep them) and that `(EmbeddedArrows + 1) / 2` arrow pickups dropped as before.

- [ ] **Step 2: Run to verify it fails** (`StuckArrowCount` does not exist → build error; stub it to 0 → FAIL lines).

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run to verify it passes** (`--test-hit-fx` → `RESULT ok`; `--test-save` still prints `RESULT match`).

- [ ] **Step 5: Commit**

```bash
git add src/Combat/Fx/HitFx.cs src/Enemies/Enemy.cs src/Combat/Projectile.cs src/Core/DebugTools.cs
git commit -m "feat(fx): arrows stay stuck in the enemy they hit, up to six, and come back on load"
```

---

### Task 6: Co-op sees blood and arrows

**Files:**
- Create: `src/Net/Core/HitFxKind.cs`
- Modify: `src/Net/Core/Protocol.cs:44-52` (append `HitFxEvt` after `DialogueAnswer`), `src/Net/Core/Messages.cs` (the record), `src/Core/EventBus.cs` (event + raise), `src/Net/ServerWorld.cs:50-60` (broadcast), `src/Net/ClientWorld.cs:69-83` (handle), `src/Enemies/Enemy.cs` and `src/Combat/Projectile.cs` (raise at the hook sites), `src/Net/NetTestFlow.cs` (trace)
- Test: `tests/Net/HitFxKindTests.cs`

**Interfaces:**
- Produces: `public enum HitFxKind : byte { Blood = 1, Arrow = 2 }` (engine-free; no burst kind on purpose).
- Produces: `[Msg(MsgKind.HitFxEvt)] public sealed record HitFxEvt(byte Kind, NetVec Pos, NetVec Dir, int EnemyNetId, uint Color) : NetMessage;` (`Color` is reserved, always 0 for now).
- Produces: `EventBus.HitFx : event Action<HitFxKind, Vector3, Vector3, int>` and `RaiseHitFx(kind, pos, dir, enemyNetId)`; raised by `Enemy.ReceiveAttack` beside `HitFx.Blood` and by `Projectile` beside `HitFx.StickArrow` (server/solo only, i.e. where those hooks already run). `ServerWorld` broadcasts `HitFxEvt` when `!Quiet`. `ClientWorld`: `case HitFxEvt fx:` → `Blood` at `Pos` under `Root`, or `StickArrow` on `_enemies[EnemyNetId]` when `TryGetValue` and `IsInstanceValid` succeed, else ignore; `Trace.Add("hitfx")`.
- Spell bursts are not relayed: every machine bursts its own bolt (Task 4's `Cosmetic` branch).

- [ ] **Step 1: Write the failing test**

```csharp
public class HitFxKindTests
{
    [Fact] public void OnlyBloodAndArrow_AreRelayed() => Assert.Equal(new[] { "Blood", "Arrow" }, Enum.GetNames<HitFxKind>());
    [Fact] public void Values_AreStable() { Assert.Equal(1, (byte)HitFxKind.Blood); Assert.Equal(2, (byte)HitFxKind.Arrow); }
}
```

- [ ] **Step 2: Run to verify it fails**: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~HitFxKind"` → build error.

- [ ] **Step 3: Implement** the enum, message, event, broadcast and client handling; the codec picks the record up from its `[Msg]` attribute (see how `EmoteEvt` was added in `Messages.cs`/`Protocol.cs`).

- [ ] **Step 4: Verify**: the unit test passes; `scripts/net_test.sh` prints `RESULT ok` on both sides and the client log shows at least one `hitfx` trace during the kill step (the test's kill goes through an arrow or a swing; if the kill step uses a spell, add one melee swing to the flow's attack so a `Blood` event is produced).

- [ ] **Step 5: Commit**

```bash
git add src/Net src/Core/EventBus.cs src/Enemies/Enemy.cs src/Combat/Projectile.cs
git commit -m "feat(net): blood and stuck arrows reach co-op clients as one HitFxEvt; spell bursts stay local"
```

---

### Task 7: Document and close

**Files:**
- Modify: `docs/development.md` (the headless flows list near line 238: add `godot --headless --path . -- --test-hit-fx   # blood, bursts, stuck arrows, grunts; prints RESULT`; the Assets section: enemy grunts and `scripts/fetch_grunts.py`; a short paragraph under combat: what each hit shows and sounds like, the six-arrow cap, bursts local in co-op)

- [ ] **Step 1: Edit the manual as above.**
- [ ] **Step 2: Final verification**: `dotnet build … --no-incremental` → 0 errors; `cd tests && dotnet test --nologo -v q` → all pass; `--test-hit-fx` and `--test-abilities --no-ambush --no-portals` → `RESULT ok`; `scripts/net_test.sh` → `RESULT ok`.
- [ ] **Step 3: Commit**

```bash
git add docs/development.md
git commit -m "docs: hit effects, enemy grunts and the --test-hit-fx flow"
```

---

## Self-review notes

- Coverage: owner's three visual requests → Tasks 4 and 5; grunts → Tasks 1 to 3; co-op → Task 6; contract F's `HitFxEvt` record, `VoiceSet` default by size, `fetch_grunts.py`, cue names, fallback to `enemy_hurt`, pitch by set, six-arrow cap all appear by name.
- Type consistency: `VoiceSets.*` used in Tasks 2 and 3 as declared in Task 1; `HitFx.*` signatures identical in Tasks 4, 5 and 6; `OneShotFx` is the only particle node type the headless flow counts.
- Contract deviations, stated: the contract's `HitFxEvt` includes `Color` for a burst; this plan never relays bursts (each machine has the bolt) and keeps `Color` at 0, which avoids a double burst on clients (Review Focus 5). The contract's `Blood(Node parent, Vector3 at, Vector3 towardAttacker)` is kept; `Enemy` approximates `towardAttacker` with the party lead's direction because `ReceiveAttack` receives no attacker position.
