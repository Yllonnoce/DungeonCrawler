# Mine Cart Chase, Plan 16: the stop trap, the alcove shooters and the frame rate (floor 12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each cage stop on floor 12 becomes a trap: portal waves and wall ambushers attack while the crew are freed, and enemies climb into an unattended train and roll it backwards. Trackside shooters fire from the alcoves during the rides. The frame rate is measured in the player's own game, and the enemy tick is put on a diet.

**Architecture:**
- **Measure first.** A `PerfMonitor` writes a perf line every 10 s, plus a line for each frame over 50 ms. Settings gain "Show FPS", a render scale with FSR, and shadows on/off. A windowed `--test-perf` flow takes a baseline. The enemy tick diet comes next and is re-measured against that baseline:
  - a static live-enemy list, so separation allocates nothing;
  - no per-tick allocations;
  - far, still, unaware enemies run their state at a quarter rate.
- **One portal system, two modes.**
  - `PortalSpawner` gains a stop mode (`PortalSpawner.Stop.cs`) with a list of pending portals. Its numbers come from an engine-free `StopSiegeRules`.
  - A new `StopTrapController` turns the stop mode on and off from the train's `CartEvents`. It also springs the stop-wall ambushers (seeded `StopAmbushRules` points appended to `AmbushSpawner`, so `SprungAmbushes` saves them) and tags the hijackers.
- **The reverse roll keeps facing apart from the way of travel.**
  - `CartRun.Reversing` gives `Dir = -1` with `Facing = +1`. The wagons stay on the lower-S side, the cart stays unturned, and the run-over and warning are judged from the train's leading face.
  - `RailController.Hijack.cs` runs the phases outside the Held and Rolling branches. It never raises `Departed`/`Arrived` and never calls the watchdog.
  - The wire gains one byte, `CartStateDto.Hijack`, and the driver rides as `Boarders[2]`, so Protocol.Version goes from 17 to 18.
- **Alcove shooters are code-placed**: an `AlcoveRules` table and a pinned `AlcoveState`, spawned when a ride arms and freed when it clears.

**Tech Stack:** Godot 4.7.2 .NET, C# (.NET 8), xUnit (`tests/`, not in git).

**Spec:** `docs/superpowers/specs/2026-11-24-floor-12-stop-trap-and-fps-design.md` (f5b2c9a), including "Rulings after the builder check" (spec Rulings 1-6). Its (call) numbers are the values below.

Recon (facts with file:line): `.superpowers/sdd/stop-trap-recon/stops-portals.md`, `train-reverse.md`, `alcoves-perf.md`. In `docs/development.md` read "Running flows side by side", "Stops, cages and the crew", "The five rides", "Riding together" (the RIDE net test, Known gaps). Line numbers drift: find code by name.

**Measured on the shipped floor** (docs and recon):
- `StopS=[16.0,283.2,488.8,770.1,1022.5,1231.7]`, so stops 1-4 are the cage stops.
- The train is the cart plus four wagons, 2.6 m apart (`TrainRules.Spacing`), with `CartRules.HalfLength` 1.1. The leading face in a reverse is S − 11.5.
- Ride 3's break gap is at about S 714.7, and ride 4's bridge spans 903.1-991.1.
- Baseline fps (spec 5): `--test-chase` windowed, median 238, worst second 35.

**Planner's rulings (an index).** Each task's "Rulings this task needs" carries the full text of the rulings it uses.
1. The reverse's representation and the one protocol bump (T5)
2. Which switch gates what, and the RIDE server (T3, T4, T8, T9)
3. The restart grace replaces the first wave's delay (T3)
4. One alive list and one cap at a time (T3)
5. The siege keeps the floor-wide portals' silences (T3)
6. The siege roster's rolls (T3, T4)
7. The stop-wall ambush: its roster, armed only on Arrived, sprung once ever (T4)
8. Any seated hero guards the train (T6)
9. A hit hijacker fights back for 3 s (T6)
10. The roll-back limit, and stops with too little roll (T5, T7)
11. Before T7, a train at its limit brakes and comes back (T5, T6)
12. Boarding the rolling-back train (T6)
13. The throw-off order (T7)
14. The relief rolls in from the limit (T7)
15. The thrown crew at the 30 s cap (T7)
16. A save during a hijack (T5, T7)
17. The alcove placement's tiers, spacing and short rides (T8)
18. The perf line's counts and the slow-frame rate limit (T1, T2)
19. The render scale in cfg and in VR (T1)
20. Only still enemies sleep (T2)
21. The RIDE pass's hijack steps (T9)
22. The flow flags (T1, T3-T8)
23. Two new server-side CartEvent kinds (T5)

## Global Constraints

- **No track change.** No track, layout, pocket, generator or LayoutHash change. Floor 12's `LayoutHash` stays 10116286907070122254. Every pinned layout test (`RailLineTests`, `ChasmTests`, `TrackStationsTests`, `RailEncounterPlanTests`, `TrackDepthRules`) stays unchanged and green. `data/` is not edited (spec Ruling 4).
- **No new voice spend.** New lines are subtitles only (`Line.VoiceId` null) and never reach the planner. Sexes stay right: Sable is a woman; Brand, Wren and Ilex are men.
- **CC0 assets only** (the repo is public). The new looks reuse existing visuals: `ShowPortal`, `ShowBurst`, `TelegraphFx.HandGlow`.
- **The protocol.** Protocol.Version stays 17 until T5. T5 sets it to 18, and it stays 18 after T5. `MessageKindCount` stays 114 (no new message kind).
- **Server authority.** Everything that spawns, judges or moves runs on the server or in solo (`NetSession.IsClient` returns early). Clients follow `CartStateDto`, `EnemySpawned`, `PortalOpenedEvt` and `AmbushSprungEvt`.
- **Running godot.**
  - Run `dotnet build DungeonCrawler.csproj` before headless godot. Engine flags go before `--`, game flags after it.
  - Never `pkill -f` a pattern that matches your own command. End runs with `--quit-after=N`, or kill by PID.
  - Every run gets its own `XDG_DATA_HOME=$(mktemp -d)`.
- **Tests.** tests/ is not in git: never `git add tests/`. Commit only `src/`, `docs/` and `scripts/` paths, staged by name. The full suite is `dotnet test tests/DungeonCrawler.Tests.csproj`, and it is green at the end of every task.
- **Existing floor-12 flows.** They keep `NoPortals`/`NoAmbush` and stay green. With those flags the siege, the wall ambushers, the hijack and the alcove shooters never appear in them. The new behaviours run only in the new flows that opt in (Ruling 22).
- **The RIDE pass** (`RIDE=1 scripts/net_test.sh`) is the controller's only, run alone under `.superpowers/sdd/shared/RIDE.lock`. Implementers never run it, and nothing builds or runs while the lock exists. Plain `scripts/net_test.sh` stays green in every task.
- **A flow passes** only on exit 0, its own `[stoptrap] RESULT ok (variant)` (or `[perf] RESULT ok`) line and no `ERROR` line. Run non-network flows through `scripts/flows.sh`, longest first.
- **Commits** end with a blank line and then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. No push, merge, installers or dist builds.
- **Engine-free rules** live in `*Rules` classes under folders the test project already compiles (`src/Adventure/Core/**`, `src/Dungeon/Rails/Core/**`, `src/Enemies/Core/**`). They use `System.Numerics`, never Godot. A new engine-free file elsewhere needs its own `<Compile Include>` line in `tests/DungeonCrawler.Tests.csproj`.
- **The (call) numbers are tunables.** Each lives as a named `const` in its rules class, pinned by a unit test. A task that retunes one names the old and new values in its commit message.

## Review Focus

- **A hero lies downed in a cart or wagon seat while a hijacker reaches the cart.** No hijack starts: any seated hero, up or downed, guards the train (Ruling 8).
  - Pinned by `HijackRulesTests.ADownedHeroInASeat_StillGuardsTheTrain` (T6).
- **The game is saved, or the party wipes, mid-roll-back, and the save is loaded.** The train stands whole at its stop, facing forward, with the wagons on the lower-S side, `Hijack` None and no driver. The siege restarts from wave 1 after its grace.
  - Pinned by the load step of `--test-stop-trap=roll` (T5) and by the mid-roll save in `=gotaway` (T7).
- **A stop has a hole right behind the train** (stop 3's break gap, or stop 4's gone bridge span after ride 4). The leading face never stops within 5 m of the hole. A stop left with less than 5 m of roll gets no hijacker tagged at all.
  - Pinned by `HijackRulesTests.TheLimit_KeepsTheLeadingFaceFiveMetresShortOfAHole` and `ALimitUnderFiveMetres_AllowsNoHijack` (T5), and by `ShippedLimits` (T7).
- **The party crowds the stop room so that no cell is 6 m clear of every hero.** The wave waits and retries every 2 s. It never opens on a hero, the rails, the platform or a pocket, and it never throws.
  - Pinned by `StopSiegeRulesTests.Sites_NoneWhenEveryCellIsNearAHero` and `Sites_NeverOnRailPlatformPocketOrLedge` (T3).
- **A `settings.cfg` from an older build, or hand-edited**, has no `render_scale`, or holds 0.6, 2.0 or NaN. The scale snaps to one of 100/85/70/50 % and the game starts.
  - Pinned by `GraphicsRulesTests.SnapScale_AnyValue_LandsOnAListedStep` (T1).

---

### Task 1: Perf instrumentation (the log line, Show FPS, graphics quality, the windowed perf flow, the baseline)

**Files:**
- Create:
  - `src/Core/FrameStats.cs` (engine-free: `FrameStats`, `PerfRules`) and `src/Core/GraphicsRules.cs` (engine-free);
  - `src/Core/PerfMonitor.cs` (a Node built under Main when a floor is entered; every role);
  - `src/Core/DebugTools.Perf.cs`.
- Modify:
  - `src/Core/Settings.cs` (`ShowFps`, `RenderScale`, `Shadows`: fields, Load/Save in "display", Apply);
  - `src/UI/Menus/SettingsScreen.cs` (three rows after "Haze in the air");
  - `src/UI/Hud/Hud.cs` (the FPS label), `src/Party/Adventurer.cs` (`SetLocalView`'s torch shadow);
  - `src/Core/Main.cs` (adds the `PerfMonitor`), `src/Core/DebugTools.cs` (the `--test-perf` flag and its dispatch);
  - `docs/development.md` (a new "### Frame rate" under "Build, test, run").
- Test: `tests/Core/FrameStatsTests.cs`, `tests/Core/GraphicsRulesTests.cs`. Add `<Compile Include>` lines for `src/Core/FrameStats.cs` and `src/Core/GraphicsRules.cs` to `tests/DungeonCrawler.Tests.csproj`.

**Rulings this task needs:**
- **18 (this task's part).**
  - The perf line counts `enemies` (alive, in the "enemies" group) and `aware` (alive and `!State.Unaware`); T2 adds `asleep`.
  - The slow-frame line is printed at most once a second, and carries the count of slow frames since the last one.
  - The line runs on every role whenever `Main.InGame` (solo, client and server; a headless server's fps is its physics rate).
  - Cost if wrong: one line every 10 s in every log.
- **19.**
  - `RenderScale` is read from cfg through `GraphicsRules.SnapScale`: the nearest listed step, the higher one on a tie, 1.0 for NaN or a missing value.
  - Below 1.0 the root viewport uses `Viewport.Scaling3DModeEnum.Fsr`; at 1.0 it uses `Bilinear`.
  - Render scale is not applied while `Vr.Active` (the headset drives its own).
  - "Shadows" off sets the local hero's torch `ShadowEnabled = false`; it is the only shadowed light.
  - Cost: a VR player's slider does nothing.
- **22 (this task's part).** `--test-perf[=crowd|ride]` is windowed only, never headless and never in `flows.sh`. With no variant it runs crowd, then ride. T3 adds `=siege`.
  - It sets, in memory and never saved: VSync off, windowed 1280x720, Haze 0.5, RenderScale 1.0, Shadows on, ShowFps off.
  - It uses `ForcedLethality ??= TierRules.MinLethality`, tops every hero up to full HP each second, and sets `NoGathering`. It leaves `NoAmbush` and `NoPortals` off. It culls nothing.
  - `_quitAfter = 200`.
  - Cost: none.

**Interfaces:**
- Consumes: `Settings.Apply`, `Hud._Process`, `Adventurer.SetLocalView`, `Performance.GetMonitor`, `RailController.Restore`, `Engine.GetFramesPerSecond`.
- Produces:
  - **`FrameStats`**: `void Add(float frameSeconds)`, `int Frames`, `float MedianFps`, `float LowFps(float fraction = 0.01f)` (the fps of the slowest fraction of frames), `float WorstSecondFps` (the lowest frame count over complete seconds only), `float WorstFrameMs`, `void Reset()`.
  - **`PerfRules`**: `const float LineEvery = 10f`, `const float SlowFrameMs = 50f`, `const float SlowLineGap = 1f`, `static bool Slow(float frameMs)`.
  - **`GraphicsRules`**: `static readonly float[] RenderScales = { 1f, 0.85f, 0.7f, 0.5f }`, `static float SnapScale(float v)`, `static bool UsesFsr(float scale)` (true below 0.999).
  - **`Settings`**: `bool ShowFps` (false), `float RenderScale` (1f), `bool Shadows` (true). The cfg keys are `display/show_fps`, `display/render_scale` and `display/shadows`.
  - **The perf line** (every 10 s): `[perf] floor={f} cart={yes|no} fps avg={a:0} min={m:0} worst_ms={w:0.0} process_ms={p:0.00} physics_ms={q:0.00} draws={d} enemies={n} aware={k}`.
  - **The slow-frame line**: `[perf] slow frame {ms:0} ms (floor {f}, cart {yes|no}; {n} slow since the last line)`.
  - **`--test-perf`'s phases.** Each phase discards its first 5 s, measures 20 s, and prints `[perf] {phase}: {frames} frames over {s:0.0} s: median {fps:0} fps, 1% low {fps:0}, worst second {fps:0}, worst frame {ms:0.0} ms; process {ms:0.00} ms, physics {ms:0.00} ms, draws {n}; enemies {n} ({aware} aware)`. At the end it prints `[perf] RESULT ok ({phase}: median …; …)`.
    - **crowd.** `rc.Restore(4)`. The lead steps out onto the platform. 12 creatures (5 `enemy.horde_cinder`, 4 `enemy.ember_hound`, 3 `enemy.forge_priest`) are spawned 10-20 m around the platform with `ForceAggro`. The floor's own enemies stay alive.
    - **ride.** `rc.Restore(0)`. The party is seated, departs, and rides ride 1 with every set piece and enemy alive.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SixtyEvenFrames_ReadSixty() { var f = new FrameStats(); for (var i = 0; i < 120; i++) f.Add(1f / 60f); Assert.Equal(60f, f.MedianFps, 1); Assert.Equal(60f, f.WorstSecondFps, 1); Assert.Equal(16.7f, f.WorstFrameMs, 1); }
[Fact] public void OneLongFrame_IsTheWorstFrame_AndDragsItsSecond() { var f = new FrameStats(); for (var i = 0; i < 59; i++) f.Add(1f / 120f); f.Add(0.1f); for (var i = 0; i < 120; i++) f.Add(1f / 120f); Assert.Equal(100f, f.WorstFrameMs, 1); Assert.True(f.WorstSecondFps < 120f); Assert.Equal(120f, f.MedianFps, 1); }
[Fact] public void OverFiftyMs_IsSlow() { Assert.True(PerfRules.Slow(50.1f)); Assert.False(PerfRules.Slow(50f)); }
[Fact] public void SnapScale_AnyValue_LandsOnAListedStep()
{
    Assert.Equal(1f, GraphicsRules.SnapScale(float.NaN)); Assert.Equal(1f, GraphicsRules.SnapScale(2f));
    Assert.Equal(0.7f, GraphicsRules.SnapScale(0.6f)); Assert.Equal(0.5f, GraphicsRules.SnapScale(0.1f)); Assert.Equal(0.85f, GraphicsRules.SnapScale(0.86f));
    foreach (var v in new[] { -1f, 0f, 0.3f, 0.77f, 0.925f, 1.5f }) Assert.Contains(GraphicsRules.SnapScale(v), GraphicsRules.RenderScales);
}
[Fact] public void Fsr_OnlyBelowFull() { Assert.False(GraphicsRules.UsesFsr(1f)); Assert.True(GraphicsRules.UsesFsr(0.85f)); }
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~FrameStatsTests|FullyQualifiedName~GraphicsRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** `FrameStats`, `PerfRules` and `GraphicsRules`. `FrameStats` keeps every frame time of the phase; the 10 s monitor calls `Reset` after each line.
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 5: Implement the game side.**
  - The settings, the three rows ("Show FPS" a check; "Render scale" an OptionButton reading 100 % / 85 % (FSR) / 70 % (FSR) / 50 % (FSR); "Shadows" a check), and `Settings.Apply` (root viewport `Scaling3DMode`/`Scaling3DScale`, skipped while `Vr.Active`).
  - `SetLocalView`'s `Torch.ShadowEnabled = local && Settings.Shadows`, and `Adventurer.ApplySettings` re-applying it.
  - The Hud's top-right fps label (shown while `Settings.ShowFps`, its text refreshed 4 times a second).
  - `PerfMonitor` and `--test-perf`.
- [ ] **Step 6: Run the perf flow and record the baseline.**

  ```bash
  dotnet build DungeonCrawler.csproj && XDG_DATA_HOME=$(mktemp -d) timeout 300 godot --path . --print-fps --resolution 1280x720 -- --test-perf 2>&1 | tee /tmp/p16-perf-t1.txt | grep -E "^\[perf\]"
  ```

  - Expected: the two `[perf] crowd:` and `[perf] ride:` lines, at least two 10 s `[perf] floor=12` lines, and `[perf] RESULT ok`. Run it with the machine otherwise idle.
  - Then run `XDG_DATA_HOME=$(mktemp -d) timeout 60 godot --headless --path . -- --builtin --floor=12 --quit-after=25 2>&1 | grep "\[perf\] floor=12"`. Expected: at least two lines (the monitor runs headless too).
- [ ] **Step 7: Write "### Frame rate"** in `docs/development.md`. It covers the perf line and its fields, the slow-frame line, the three settings, the `--test-perf` command and phases, and the T1 baseline table (median, 1% low, worst second, worst frame, physics ms, enemies/aware per phase) with the date and the machine (RTX 5070, 240 Hz).
- [ ] **Step 8: Run the full suite and plain `scripts/net_test.sh`** (alone). Expected: all green, `NET TEST: ok`.
- [ ] **Step 9: Commit**

```bash
git add src/Core/FrameStats.cs src/Core/GraphicsRules.cs src/Core/PerfMonitor.cs src/Core/DebugTools.Perf.cs src/Core/DebugTools.cs src/Core/Settings.cs src/UI/Menus/SettingsScreen.cs src/UI/Hud/Hud.cs src/Party/Adventurer.cs src/Core/Main.cs docs/development.md
git commit -m "feat(perf): the perf line every 10 s and the slow-frame line, Show FPS, render scale with FSR and shadows in Settings, the windowed --test-perf flow and its baseline

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The enemy tick diet (and the perf flow re-measured)

**Files:**
- Create: `src/Enemies/Core/TickDietRules.cs` (engine-free), `src/Enemies/Enemy.Diet.cs` (partial: the live list and the sleep).
- Modify:
  - `src/Enemies/Enemy.cs` (`Separation`, `Retarget`, the `Effects.Update` call, the `State.Update` gate);
  - `src/Enemies/Enemy.Unstick.cs` (the probe shape, query and exclude array cached per enemy);
  - `src/Combat/Core/StatusEffects.cs` (a filling overload), `src/Party/PartyController.cs` (its `Effects.Update` call uses the overload);
  - `src/Core/PerfMonitor.cs` (`asleep=`), `docs/development.md` ("### Frame rate").
- Test: `tests/Enemies/TickDietRulesTests.cs`, `tests/Combat/StatusEffectsTests.cs` (one new test).

**Rulings this task needs:**
- **18 (this task's part).** The perf line and the `[perf] {phase}` lines gain `asleep={n}`, the enemies on their quarter rate this tick. Cost: none.
- **20. Only still enemies sleep.** An enemy sleeps only if all of these hold:
  - its state is unaware (`State.Unaware`) or a `HoldState`;
  - its horizontal speed last tick was under 0.1 m/s;
  - it is not `Carried` or `Leaping`, and its `SinceHurt` is over 1 s;
  - it is more than 40 m from every hero in `Party.Adventurers` (downed heroes included).

  A sleeping enemy runs `State.Update` only on the ticks where `(NetId & 3) == (physicsFrame & 3)`, with the dt it banked since its last run (its wind-up and telegraph timers bank the same way).

  Everything else runs every tick: water, unstick, animation, effects, tint, and the dying and death drop. A patrol, a walk home or an alarm run is never asleep, so no pinned timing moves.

  At 40 m with a 16 m `DetectRange`, a sleeper cannot see anyone, so no detection is delayed.

  Cost: a far guard turns to face a noise up to three ticks (50 ms) late.

**Interfaces:**
- Consumes: T1's `[perf]` lines and `--test-perf`.
- Produces:
  - **`TickDietRules`**: `const float FarMetres = 40f`, `const int Every = 4`, `const float StillSpeed = 0.1f`, `const float HurtAwake = 1f`, `static bool Sleeps(bool unawareOrHolding, float horizontalSpeed, bool carried, bool leaping, float sinceHurt, float nearestHeroMetres)`, `static bool RunsThisTick(int netId, ulong physicsFrame)`.
  - **`Enemy.Live`**: `public static IReadOnlyList<Enemy> Live`, kept by `_EnterTree`/`_ExitTree` with no allocation per tick. `Separation()` walks `Live`, skipping any with another parent, so the pushes are exactly today's.
  - **`Enemy.Asleep`** (`public bool Asleep { get; private set; }`), read by the perf line.
  - **`StatusEffects.Update(float dt, List<StatusTick> into)`**: it clears and fills `into`. The old `List<StatusTick> Update(float dt)` stays as a wrapper, so `StatusEffectsTests` is unchanged.
  - **`Retarget`**: iterates a reusable per-enemy key buffer instead of `_threat.Keys.ToList()`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AFarStillIdle_Sleeps() => Assert.True(TickDietRules.Sleeps(true, 0f, false, false, 5f, 40.5f));
[Fact] public void AnythingThatWakes_KeepsItAwake()
{
    Assert.False(TickDietRules.Sleeps(false, 0f, false, false, 5f, 80f));   // hunting
    Assert.False(TickDietRules.Sleeps(true, 1.5f, false, false, 5f, 80f));  // patrolling or walking home
    Assert.False(TickDietRules.Sleeps(true, 0f, true, false, 5f, 80f));     // carried
    Assert.False(TickDietRules.Sleeps(true, 0f, false, true, 5f, 80f));     // leaping
    Assert.False(TickDietRules.Sleeps(true, 0f, false, false, 0.5f, 80f));  // just hurt
    Assert.False(TickDietRules.Sleeps(true, 0f, false, false, 5f, 40f));    // within 40 m
}
[Fact] public void Sleepers_AreStaggeredByNetId()
{
    for (ulong f = 0; f < 8; f++) Assert.Equal(1, Enumerable.Range(200000, 4).Count(id => TickDietRules.RunsThisTick(id, f)));
    Assert.Equal(2, Enumerable.Range(0, 8).Count(f => TickDietRules.RunsThisTick(200001, (ulong)f)));
}
[Fact] public void Update_IntoABuffer_GivesTheSameTicks() { /* two equal StatusEffects with poison and regen; Update(1f) and Update(1f, buf) give equal Amount/Type/IsHeal sequences; buf reused twice holds only the second call's ticks */ }
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~TickDietRulesTests|FullyQualifiedName~StatusEffectsTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the rules, the overload, `Enemy.Live`, the cached unstick probe, the key buffer and the sleep gate (Ruling 20).
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 5: Run every floor-12 flow and the general flows**, which prove that no pinned behaviour moved:

  ```bash
  scripts/flows.sh "--test-chase --quit-after=750" "--test-crew --quit-after=570" "--test-boarders=groups" "--test-island" "--test-island=lift" "--test-cavern" "--test-pit" "--test-runaway=tail" "--test-split" "--test-switch-back" "--test-crevasse" "--test-relief" "--test-barricade" "--test-ride" "--test-ride-edges" "--test-cart-watchdog" "--test-track-view" "--test-tick-rate" "--test-abilities" "--test-party" "--test-hit-fx" "--test-vault-puzzle"
  ```

  Expected: every line PASS. A FAIL is a behaviour change: fix the diet, never the flow.
- [ ] **Step 6: Re-measure.** Run Step 6 of Task 1 into `/tmp/p16-perf-t2.txt`. Expected: `[perf] RESULT ok`, with `asleep=` present. Add the after-diet rows to the "Frame rate" table, beside T1's, with the change per phase.
- [ ] **Step 7: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok`.
- [ ] **Step 8: Commit**

```bash
git add src/Enemies/Core/TickDietRules.cs src/Enemies/Enemy.Diet.cs src/Enemies/Enemy.cs src/Enemies/Enemy.Unstick.cs src/Combat/Core/StatusEffects.cs src/Party/PartyController.cs src/Core/PerfMonitor.cs src/Core/DebugTools.Perf.cs docs/development.md
git commit -m "perf(enemies): the tick diet: separation over a live list, no per-tick allocations in effects, retarget and unstick, far still unaware enemies at a quarter rate staggered by NetId; the perf flow re-measured

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The stop siege

**Files:**
- Create:
  - `src/Adventure/Core/Design/StopSiegeRules.cs` (engine-free), `src/Adventure/Core/Voice/StopTrapLines.cs` (engine-free);
  - `src/Adventure/PortalSpawner.Stop.cs` (partial), `src/Dungeon/Rails/StopTrapController.cs`;
  - `src/Core/DebugTools.StopTrap.cs` (the flag, the shared helpers and the dispatch), `src/Core/DebugTools.StopTrap.Siege.cs`.
- Modify:
  - `src/Adventure/PortalSpawner.cs` (`partial`; the pending portals become a list; the floor-wide tick pauses during a siege; `RefreshSilence` gains the RIDE server);
  - `src/Dungeon/DungeonBuilder.cs` (`PlaceStopTrap` after `PlaceRailEncounters`), `src/Core/Main.cs` (`RoseAtCart`);
  - `src/Core/DebugTools.Perf.cs` (`=siege`), `src/Core/DebugTools.cs`.
- Test: `tests/Adventure/StopSiegeRulesTests.cs`, `tests/Adventure/StopTrapLinesTests.cs`

**Rulings this task needs:**
- **2 (this task's part).** The siege is silenced by everything that silences the floor-wide portals (Ruling 5), and additionally on the RIDE server (`NetSession.Instance.TestRide`). Cost: the RIDE pass never sees a wave on the wire; waves use the proven `PortalOpenedEvt`/`EnemySpawned` paths.
- **3. The restart.**
  - On a `Restored` event with the train Held at a sieged stop, the siege restarts at wave 1.
  - Its first wave comes at the grace's end: 15 s when `Main.RoseAtCart` (the build a wipe's rise made), else 10 s. The grace replaces the row's first-wave delay.
  - Nothing of the siege is saved.
  - Cost: none.
- **4. One alive list.** Siege creatures join `PortalSpawner`'s `_alive`, so the HUD's "Reinforcements: N hunting you" counts them. While a siege runs only its cap applies, and floor-wide leftovers count against it. Afterwards the floor cap applies, with stragglers counted. Cost: a lingering straggler delays the next floor-wide portal.
- **5.** The siege keeps every silence of `RefreshSilence` except "no creatures in the roster": Reinforcements ≤ 0.01, `--no-portals`, a dead tier boss and an active ritual all silence it. Silencing mid-siege drops its pending portals at once. Cost: none.
- **6. The roster's rolls.** `Pick(stop, bruteTaken, roll)`:
  - `roll < 0.35` gives `enemy.horde_cinder`; `< 0.70` `enemy.ember_hound`; `< 0.95` `enemy.forge_priest`;
  - otherwise `enemy.horde_cinder_brute` when `stop >= 3 && !bruteTaken`, else `enemy.horde_cinder`.
  - Never `enemy.forge_golem`.
  - Cost: none.
- **22 (this task's part).** `--test-stop-trap[=siege|ambush|roll|wonback|gotaway|limits|alcove]` (default `siege`; each variant is added by the task that builds it) runs on floor 12 with `--builtin`, `ForcedLethality ??= 1f`, `NoGathering` and `TraceAi` off, and tops every hero up to full HP each second.
  - `NoPortals` is false for `siege`, `wonback` and `gotaway`, true otherwise.
  - `NoAmbush` is false for `ambush` and `alcove`, true otherwise.
  - The flow's cull frees every enemy except siege creatures, stop guards, wall ambushers, hijackers and alcove shooters, as the variant needs.
  - `_quitAfter`: siege 330, ambush 200, roll 200, wonback 260, gotaway 300, limits 200, alcove 260.
  - It prints `[stoptrap] RESULT ok ({variant})` or `[stoptrap] RESULT FAIL ({variant}): {why}`.
  - Cost: none.

**Interfaces:**
- Consumes: `PortalSpawner` (`Open`, `TickSpawns`, `ShowPortal`, `RefreshSilence`), `RailController.CartEvents`, `RailController.State`/`StopIndex`/`StopS`, `layout.Rail.Stops[k]` (`Room`, `Platform`), `CrewRescueController.Cages` (`Stop`, `Cell`), `AdventureDirector.SayAs`.
- Produces:
  - **`StopSiegeRules`:**
    - `readonly record struct SiegeWave(float FirstAfter, float Gap, int Portals, int MinPerPortal, int MaxPerPortal, int AliveCap)`, and `static SiegeWave For(int stop)`:
      - 1 → (4, 14, 1, 2, 2, 6); 2 → (4, 12, 2, 2, 2, 8); 3 → (3, 10, 2, 3, 3, 10); 4 → (3, 8, 3, 2, 3, 12);
      - a stop past 4 reads row 4.
    - `static bool Sieges(int stop, int stopCount)` (1..stopCount−2).
    - `static int PerPortal(SiegeWave w, int humans, double roll)`: Min..Max by roll, plus 1 per human past the first.
    - `static int AliveCap(SiegeWave w, int humans)`: plus 2 per human past the first.
    - `static float Gap(SiegeWave w, float intensity)`: ×2/3 when intensity ≥ 1.5.
    - `static string Pick(int stop, bool bruteTaken, double roll)` (Ruling 6).
    - `const float LoadGrace = 10f, RiseGrace = 15f`, and `static float RestartGrace(bool rose)`.
    - `const float StragglerFar = 60f, StragglerUnseen = 10f`, and `static bool Culls(float nearestHeroMetres, float unseenSeconds)`.
    - `const float HeroClear = 6f, CageClear = 4f, PlatformClear = 4f, Spread = 8f, RetrySeconds = 2f`, `const int Around = 3`.
    - `readonly record struct SiegeHero(GridPos Cell, float FacingX, float FacingY)`.
    - `static List<GridPos> Sites(FloorLayout layout, Room room, RailStop stop, GridPos? cage, IReadOnlyList<SiegeHero> heroes, int count, Random rng)`. Distances are cell-centre metres (×4). Candidates:
      - walkable `Cell.Floor` cells in the room or within 3 cells of it;
      - not reserved (`IsReserved` excludes rail and pockets), not on the platform, not water, not a ledge or ramp cell;
      - at least 6 m from every hero, 4 m from the cage and 4 m from every platform cell.

      Picks are greedy, at least 8 m apart. The score is `(behind every hero ? 0 : 10) + rng`, where behind means `dot(site − hero, facing) < 0`. A wave may come back with fewer sites than asked, or none.
  - **`StopTrapLines`** (all `VoiceId` null): `SiegeFirstLog = "The air tears open around the platform!"`, `SiegeLaterLog = "A portal tears open behind you."`, `GriselSiegeWarning = "They're tearing through the air! Hold the platform!"`, `CrewSiegeWarning = "Something's coming through! Watch the platform!"`, and `static Line Say(string text)`. T6 and T7 append their lines here.
  - **`PortalSpawner` (stop mode):**
    - `int SiegeStop { get; private set; } = -1` and `int SiegeWaves { get; private set; }`.
    - `void StartSiege(int stop, float firstDelay, int humans)` prints `[portal] siege at stop {k}: on (first wave in {d:0.0} s, {h} human(s))`.
    - `void EndSiege(string why)` prints `[portal] siege at stop {k}: off ({why}) after {n} wave(s)`.
    - Each creature goes through `TickSpawns`' path: `IsReinforcement`, `RunBoost` 1.15, `ForceAggro`, added to `_alive`. Then `event Action<Enemy, int, int>? SiegeSpawned` (creature, stop, wave) is raised; T6 subscribes to it.
    - `humans` = the heroes a human drives: solo 1; on a server, each connected player's hero.
    - `internal void DebugSiegeNow()`: the next wave is due in 0.5 s.
    - Each wave prints `[portal] siege stop {k} wave {w}: {p} portal(s) x {c} at [{cells}] (alive {a}/{cap})`. When it cannot open it prints `[portal] siege stop {k} wave {w} waits ({alive a/cap|no site})` and retries in 2 s.
    - The first wave of each siege logs `SiegeFirstLog`; later waves log `SiegeLaterLog`. No taunt.
    - The floor-wide `PressureSeconds` and `_timer` do not advance while `SiegeStop >= 1`.
    - Stragglers are checked at 1 Hz (one sight ray to the nearest hero) and freed quietly: `[portal] straggler {name} freed ({d:0} m from every hero, unseen {t:0} s)`.
  - **`StopTrapController`** (a Node on every railed floor whose line has 3 stops or more; server and solo tick).
    - It subscribes to `CartEvents`:
      - `Arrived` at a sieged stop → `StartSiege(stop, For(stop).FirstAfter, humans)`;
      - `Departed`, `Wrecked`, `OnFoot` → `EndSiege`;
      - `Restored` → `EndSiege`, then the restart (Ruling 3).
    - On a stop's first siege it has Grisel say `GriselSiegeWarning` (through `SayAs`, key `siege:{stop}`), or, when she is absent, dead or downed, the nearest up freed crew member say `CrewSiegeWarning`.
    - `static StopTrapController? Instance`.
  - **`Main.RoseAtCart`** (`bool`, set for the build a wipe's rise made, false otherwise).
  - **`--test-perf=siege`**: as `crowd`, with the real siege at stop 4 (`DebugSiegeNow`) instead of the 12 spawned creatures, measured from wave 3.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheTable_IsTheSpecs() { var w = StopSiegeRules.For(4); Assert.Equal((3f, 8f, 3, 2, 3, 12), (w.FirstAfter, w.Gap, w.Portals, w.MinPerPortal, w.MaxPerPortal, w.AliveCap)); Assert.Equal(6, StopSiegeRules.For(1).AliveCap); Assert.Equal(w, StopSiegeRules.For(7)); }
[Fact] public void OnlyTheCageStops_AreSieged() { Assert.False(StopSiegeRules.Sieges(0, 6)); Assert.True(StopSiegeRules.Sieges(1, 6)); Assert.True(StopSiegeRules.Sieges(4, 6)); Assert.False(StopSiegeRules.Sieges(5, 6)); }
[Fact] public void CoOp_AddsOnePerPortal_AndTwoToTheCap() { var w = StopSiegeRules.For(2); Assert.Equal(4, StopSiegeRules.PerPortal(w, 3, 0.0)); Assert.Equal(12, StopSiegeRules.AliveCap(w, 3)); Assert.Equal(2, StopSiegeRules.PerPortal(w, 1, 0.99)); }
[Fact] public void Heavy_ShortensTheGapsByAThird() { Assert.Equal(8f, StopSiegeRules.Gap(StopSiegeRules.For(2), 2f), 3); Assert.Equal(12f, StopSiegeRules.Gap(StopSiegeRules.For(2), 1f), 3); }
[Fact] public void TheRoster_NeverAGolem_OneBruteFromStopThree()
{
    Assert.Equal("enemy.horde_cinder", StopSiegeRules.Pick(2, false, 0.97)); Assert.Equal("enemy.horde_cinder_brute", StopSiegeRules.Pick(3, false, 0.97));
    Assert.Equal("enemy.horde_cinder", StopSiegeRules.Pick(3, true, 0.97)); Assert.Equal("enemy.forge_priest", StopSiegeRules.Pick(1, false, 0.8));
    for (var r = 0.0; r < 1.0; r += 0.01) Assert.NotEqual("enemy.forge_golem", StopSiegeRules.Pick(4, false, r));
}
[Fact] public void TheRestart_WaitsLongerAfterARise() { Assert.Equal(10f, StopSiegeRules.RestartGrace(false)); Assert.Equal(15f, StopSiegeRules.RestartGrace(true)); }
[Fact] public void Stragglers_FarAndUnseen_AreFreed() { Assert.True(StopSiegeRules.Culls(60.1f, 10f)); Assert.False(StopSiegeRules.Culls(59f, 30f)); Assert.False(StopSiegeRules.Culls(80f, 9.9f)); }
[Fact] public void Sites_KeepTheirDistances_AndSpread() { /* a 9x7 room with a 2-cell platform on its south row, a cage cell, two heroes: every site is ≥ 6 m from each hero, ≥ 4 m from the cage and every platform cell, and sites are ≥ 8 m apart; asked 3, given 3 */ }
[Fact] public void Sites_PreferBehindTheHeroes() { /* one hero facing +X in a long room: with two cells equally clear, the one at lower X is picked first */ }
[Fact] public void Sites_NoneWhenEveryCellIsNearAHero() { /* a 3x3 room, a hero in its centre cell: Sites returns an empty list (no throw) */ }
[Fact] public void Sites_NeverOnRailPlatformPocketOrLedge() { /* a room crossed by a rail cell, with a pocket, a ledge and the platform: none of them is ever a site, over 50 seeds */ }
[Fact] public void EveryLine_IsSubtitleOnly() { foreach (var t in new[] { StopTrapLines.SiegeFirstLog, StopTrapLines.GriselSiegeWarning, StopTrapLines.CrewSiegeWarning }) { Assert.False(string.IsNullOrWhiteSpace(t)); Assert.Null(StopTrapLines.Say(t).VoiceId); } }
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~StopSiegeRulesTests|FullyQualifiedName~StopTrapLinesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** `StopSiegeRules` and `StopTrapLines`.
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 5: Implement** the stop mode, the pending-portal list (the floor-wide `Open` adds one entry), `StopTrapController`, `PlaceStopTrap`, `RoseAtCart` and `--test-perf=siege`.
- [ ] **Step 6: Write `--test-stop-trap=siege`.** It fails unless, in order:
  - **(a) The load restart.** After `rc.Restore(1)` no wave opens for 10 s. Wave 1 opens at 10 ± 0.5 s: 1 portal × 2 creatures, `SiegeFirstLog` logged, and the warning said once.
    - Every site is in stop 1's room or within 3 cells of it, 6 m or more from every hero, 4 m or more from the cage and the platform, and never a rail or pocket cell.
    - `PortalSpawner.PressureSeconds` does not change for as long as the siege runs.
  - **(b) Gaps and cap.** With nobody fighting (every companion's `DebugHoldFire` set, the lead idle on the platform), wave 2 opens at +14 ± 0.5 s and wave 3 at +28. Wave 4 prints `waits (alive 6/6)`, and the siege's alive count never exceeds 6.
  - **(c) The gate.** `rc.DebugKillHolders(1)` lifts stop 1's gate within 1 s, with siege creatures alive.
  - **(d) Departure.** The lead boards and the train departs. `siege at stop 1: off (the train left)` is printed, and no wave opens during ride 2. Every siege creature still alive is freed as a straggler, or dies, before the arrival at stop 2. (The flow culls ride 2's own enemies, and the train outruns the stragglers.)
  - **(e) The real arrival.** At stop 2, wave 1 opens 4 ± 0.5 s after `Arrived`, with 2 portals at least 8 m apart and 2 creatures each. Wave 2 comes 12 ± 0.5 s later.
  - **(f) Heavy.** `Settings.Reinforcements = 2` and `RefreshSilence()`: the next gap is 8 ± 0.5 s.
  - **(g) Save and load at stop 2.** No wave opens for 10 s, then wave 1 (2 portals) and `SiegeWaves` 1.
  - **(h) The wipe.** The flow takes every hero to 0 HP. After the rise at stop 2 (`RoseAtCart` true), no wave opens for 15 s, then wave 1.
  - **(i) Off.** `Settings.Reinforcements = 0` and `RefreshSilence()`: `siege at stop 2: off (reinforcements are off in Settings)` within 2 s, and no portal opens for 20 s.
- [ ] **Step 7: Run it, and the floor-12 set.** Run `scripts/flows.sh "--test-stop-trap=siege" "--test-chase --quit-after=750" "--test-crew --quit-after=570" "--test-ride" "--test-relief" "--test-boarders=groups"`. Expected: every line PASS.
- [ ] **Step 8: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok`.
- [ ] **Step 9: Commit**

```bash
git add src/Adventure/Core/Design/StopSiegeRules.cs src/Adventure/Core/Voice/StopTrapLines.cs src/Adventure/PortalSpawner.cs src/Adventure/PortalSpawner.Stop.cs src/Dungeon/Rails/StopTrapController.cs src/Dungeon/DungeonBuilder.cs src/Core/Main.cs src/Core/DebugTools.cs src/Core/DebugTools.StopTrap.cs src/Core/DebugTools.StopTrap.Siege.cs src/Core/DebugTools.Perf.cs
git commit -m "feat(stops): the stop siege: portal waves around the cage stops until the train leaves (StopSiegeRules: table, co-op, heavy, sites, roster), the floor-wide portals paused meanwhile, the restart after a load or a rise, stragglers freed, --test-stop-trap=siege

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The stop-wall ambushers

**Files:**
- Create: `src/Adventure/Core/Design/StopAmbushRules.cs` (engine-free), `src/Dungeon/Rails/StopTrapController.Ambush.cs`, `src/Core/DebugTools.StopTrap.Ambush.cs`.
- Modify:
  - `src/Adventure/Core/Design/Reinforcements.cs` (`AmbushPoint.Stop`);
  - `src/Adventure/AmbushSpawner.cs` (`AddStopPoints`, `SpringStop`, the proximity tick skips stop points);
  - `src/Dungeon/Rails/StopTrapController.cs` (it builds the points after the cages, on every role).
- Test: `tests/Adventure/StopAmbushRulesTests.cs`

**Rulings this task needs:**
- **2 (this task's part).** The stop points exist only when the corridor ambushes do (`Settings.Ambushes && !NoAmbush`), and never on the RIDE server. Cost: none.
- **6.** The ambusher's kind is `StopSiegeRules.Pick(stop, bruteTaken, roll)` from the placement's own rng. Cost: none.
- **7.**
  - The points arm only on a real `Arrived` at stops 1-4 (a spare's takeover's arrival included), never on `Restored`.
  - They spring 1.5 s after the first hero at that stop is `Unseated` while Held, or 6 s after the arrival if nobody steps off.
  - Each point is sprung once ever. Its index lives in `SprungAmbushes`, as a corridor ambush's does, so a revisit, a load or a wipe-rise does not re-arm it.
  - Cost: a revisited floor's stop walls stay quiet (the siege still runs).

**Interfaces:**
- Consumes: `AmbushRules`' cover test, `AmbushSpawner.Spring`/`ShowBurst`/`Sprung`, `AmbushSprungEvt`, `CrewRescueController.Cages`, T3's `StopTrapController` and `StopSiegeRules.Pick`.
- Produces:
  - **`AmbushPoint.Stop`** (`int`, init, −1 for a corridor point).
  - **`StopAmbushRules`:**
    - `static readonly int[] Counts = { 0, 2, 3, 3, 4 }` and `static int Count(int stop)` (0 outside 1..4).
    - `const float SpringAfterStepOff = 1.5f, SpringAfterArrival = 6f, CageClear = 4f`, `const int Apart = 3`, `const int Salt = 0x5709A`.
    - `static bool Due(float sinceArrival, float sinceFirstStepOff)` (NaN: nobody stepped off).
    - `static List<AmbushPoint> Place(FloorLayout layout, IReadOnlyList<RailStop> stops, IReadOnlyDictionary<int, GridPos> cages, int seed)`. The rng is `seed ^ Salt`. The points come in stop order, each with `Stop` set. A trigger cell:
      - is `Cell.Floor`, walkable and inside the stop's room;
      - is not reserved, not water, not a platform cell and not `IsUnder`;
      - has a 4-neighbour cover: rubble, preferred, else a non-walkable, non-secret wall at a level ≤ its own (as `AmbushRules`);
      - is at least 4 m from the cage and at least 3 cells from the other points.

      A room with too few candidates gets fewer points.
  - **`AmbushSpawner`:** `int AddStopPoints(IReadOnlyList<AmbushPoint> points)` (it appends and returns the first index), and `int SpringStop(int stop)` (each unsprung point of that stop through `Spring(i)`; it returns how many).
  - **`StopTrapController`** prints `[ambush] stop {k}: {n} burst from the walls {t:0.0} s after {the first step-off|the arrival}`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheCounts_AreTwoThreeThreeFour() { Assert.Equal(new[] { 2, 3, 3, 4 }, Enumerable.Range(1, 4).Select(StopAmbushRules.Count)); Assert.Equal(0, StopAmbushRules.Count(5)); }
[Fact] public void TheSpring_AfterAStepOff_OrSixSecondsAfterArrival() { Assert.True(StopAmbushRules.Due(2f, 1.5f)); Assert.False(StopAmbushRules.Due(2f, 1.4f)); Assert.True(StopAmbushRules.Due(6f, float.NaN)); Assert.False(StopAmbushRules.Due(5.9f, float.NaN)); }
[Fact] public void Place_IsSeeded_AndIdenticalTwice() { /* the same synthetic layout and seed twice: equal lists; another seed: a different list */ }
[Fact] public void Place_KeepsTheRules() { /* every point: inside its stop's room, beside rubble or a solid wall, ≥ 4 m from the cage, ≥ 3 cells from the others, never a platform or rail cell, Stop set; rubble taken before a plain wall */ }
[Fact] public void ThePlacement_NeverMovesTheCorridorPoints() { /* AmbushRules.Place's list is equal with and without stop points appended after it (the indices of the saved SprungAmbushes stay valid) */ }
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~StopAmbushRulesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the rules, `AmbushPoint.Stop`, `AddStopPoints`, `SpringStop` and the controller's arming.
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 5: Write `--test-stop-trap=ambush`.** It fails unless:
  - **(a) Placement.** The flow prints the points: stop 1 has 2 and stop 2 has 3, and they equal a fresh `StopAmbushRules.Place` on the same layout.
  - **(b) The step-off.** `rc.Restore(1)` arms nothing. The flow then rides from stop 0 to stop 1 seated (`rc.Restore(0)`, then a departure). At the `Arrived` at stop 1 the lead gets out. Exactly 2 creatures burst 1.5 ± 0.2 s after its `Unseated`, each with `burst from the wall` or `rubble`, an `AmbushSprung` raised per point, and `ForceAggro` set.
  - **(c) Once only.** A save and a load at stop 1, then the lead gets out and in twice over 10 s: no burst.
  - **(d) Nobody steps off.** The flow kills the holders, the lead stays seated and rides to stop 2: 3 creatures burst 6 ± 0.2 s after `Arrived`.
- [ ] **Step 6: Run** `scripts/flows.sh "--test-stop-trap=ambush" "--test-stop-trap=siege" "--test-ride" "--test-crew --quit-after=570"`. Expected: every line PASS.
- [ ] **Step 7: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok`.
- [ ] **Step 8: Commit**

```bash
git add src/Adventure/Core/Design/StopAmbushRules.cs src/Adventure/Core/Design/Reinforcements.cs src/Adventure/AmbushSpawner.cs src/Dungeon/Rails/StopTrapController.cs src/Dungeon/Rails/StopTrapController.Ambush.cs src/Core/DebugTools.StopTrap.cs src/Core/DebugTools.StopTrap.Ambush.cs
git commit -m "feat(stops): ambushers burst from the stop walls (2/3/3/4) after the first step-off or 6 s after arrival, seeded on every peer, sprung once and saved with SprungAmbushes; --test-stop-trap=ambush

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The reverse roll (physics, facing, wagons, run-over, warning, wire 18, a debug roll)

**Files:**
- Create: `src/Dungeon/Rails/Core/HijackRules.cs` (engine-free, with `HijackPhase`), `src/Dungeon/Rails/RailController.Hijack.cs`, `src/Core/DebugTools.StopTrap.Roll.cs`.
- Modify:
  - Cart motion:
    - `src/Dungeon/Rails/Core/CartPhysics.cs` (`CartInput.DriveAccel`);
    - `src/Dungeon/Rails/CartRun.cs` (`Reversing`, `Facing`; `PoseAt` and `Sync` use `Facing`);
    - `src/Dungeon/Rails/CartTrain.cs` (`Pose` and `Sync` use the lead's `Facing`).
  - Run-over and warning: `src/Dungeon/Rails/Core/RunOverRules.cs` (`Hits` and `InWarning` take `lead`), `src/Dungeon/Rails/RunOverSweep.cs` (`Judge` takes `lead`), `src/Dungeon/Rails/RailWarn.cs` (`Ahead` takes `dir` and `lead`).
  - Events: `src/Dungeon/Rails/Core/CartEvents.cs` (two kinds appended).
  - `src/Dungeon/Rails/RailController.cs`:
    - `Tick` routes to `TickHijack` while `Hijack != None`;
    - `Restore`, `Wreck` and a floor change reset the hijack.
    - Boarding while rolling back is T6's.
  - `src/Dungeon/Rails/RailController.Chase.cs`: no spare is offered or taken while a hijack is live.
  - The driver and the wire:
    - `src/Dungeon/Rails/MineCart.cs` (the driver spot);
    - `src/Net/Core/CartNet.cs` (`CartStateDto.Hijack`), `src/Net/Core/Protocol.cs` (18);
    - `src/Dungeon/Rails/Core/CartFollower.cs` (`DriveAccel` from the phase);
    - `src/Dungeon/Rails/RailController.Net.cs` (capture and apply: `Hijack`, `Reversing`, `Boarders[2]`);
    - `src/Dungeon/Rails/RailNet.Leaps.cs` (a client carries `Boarders[2]` at the driver spot).
  - Hazards: `src/Dungeon/Rails/RailEncounterController.cs` (`CollectRollBackHazards`).
  - Flows: `src/Core/DebugTools.StopTrap.cs`.
- Test:
  - `tests/Dungeon/Rails/HijackRulesTests.cs`;
  - `tests/Dungeon/Rails/RunOverRulesTests.cs`, `CartPhysicsTests.cs`, `CartFollowerTests.cs` (new tests);
  - `tests/Net/CartNetTests.cs`, and `tests/Net/NetCodecTests.cs` (the pin 17 → 18 and its comment).

**Rulings this task needs:**
- **1. The reverse's representation.**
  - The party run rolls back with `Dir = -1` (the way of travel) and `Reversing = true`, which gives `Facing = +1`. `CartRun.Facing => Reversing ? -Dir : Dir`, so every other cart keeps `Facing == Dir`.
  - `PoseAt`, `CartTrain.Pose` and `CartTrain.Sync` use `Facing`. The wagons stay at S − 2.6k, and the cart is unturned.
  - The leading face in a reverse is the last wagon's back: `lead = wagons × TrainRules.Spacing`, beyond the cart's own `HalfLength`.
  - The wire:
    - `CartStateDto.Hijack` (a byte, `HijackPhase`); a client sets `Reversing = HijackRules.Reversing(phase)`;
    - `CartInput.DriveAccel` (negative = `CartRules.DriveAccel`), which a client takes from `HijackRules.DriveAccelFor(phase)`;
    - the driver as `Boarders[2]` (the party cart's list grows to 3).

    Protocol.Version goes to 18 here, once for the whole plan.
  - Cost: none.
- **10. The limit.**
  - `LimitS(stopS, wagons, hazards) = max(stopS − 40, max over hazards with From < stopS of (To + 5 + wagons·Spacing + HalfLength))`.
  - `hazards` = `CollectPartyLaneHoles`, plus each live heap's face (`S`, `S`), plus a lingering ride runaway's span, as `CollectRollBackHazards` gathers them.
  - A stop where `stopS − LimitS < 5` allows no hijack.
  - Cost: a stop with a hole right behind it is never hijacked.
- **11. Before T7.** A train that reaches its limit brakes and returns to its stop (`OnLimit` → `BrakeRollBack("the limit")`), so it never stands past the limit. T7 replaces `OnLimit` with the got-away. Cost: none.
- **16 (this task's part).** The hijack is not saved. `Capture` keeps `StopIndex`, so a load or a rise stands the train whole at its stop: `Restore` resets `Hijack`, `Reversing`, `Dir = +1` and the driver. Cost: none.
- **23.** `CartEventKind` gains `Hijacked` (the roll starts, `Stop` = the stop) and `HijackOver` (held at the stop again). Both are appended, server-side and not on the wire. No `Departed`, `Arrived` or `Resumed` is raised by a hijack, so no ride arms, no snapshot is taken and no stop is counted. Cost: none.

**Interfaces:**
- Consumes: `CartRun.Step` (its mirrored stepping), `CartPhysics.Step`, `RunOverSweep`, `RailWarn`, `CartTrain`, `CollectPartyLaneHoles`, `LiveHeapFaceAhead`'s heap list, the ride runaway, T3's `StopTrapController`.
- Produces:
  - **`enum HijackPhase : byte { None, Glow, RollingBack, Braking, Returning, Gone, ReliefIn }`.**
  - **`HijackRules`:**
    - Constants: `RollBackAccel = 0.5f`, `RollBackMaxSpeed = 4f`, `BrakeDecel = CartRules.BrakeDecel` (6), `ReturnSpeed = 2f`, `RollBackReach = 40f`, `HoleClear = 5f`, `MinRoll = 5f`, `BrakeGlow = 3f`.
    - `static bool Reversing(HijackPhase p)` (RollingBack or Braking).
    - `static float DriveAccelFor(HijackPhase p)` (RollingBack → 0.5, else −1).
    - `static CartInput InputFor(HijackPhase p, float slope, int wagons, float limit)`: RollingBack is TargetOverride 4 with `DriveAccel` 0.5; Braking is `Brake`; Returning is TargetOverride 2; ReliefIn is TargetOverride 4.
    - `static float LimitS(float stopS, int wagons, IReadOnlyList<(float From, float To)> hazards)` and `static bool RollRoom(float stopS, float limitS)`.
  - **`CartInput`** gains `float DriveAccel = -1f`. `CartPhysics.Step` uses it in place of `CartRules.DriveAccel` when it is ≥ 0. Every existing caller and test is unchanged.
  - **`RunOverRules.Hits(float sPrev, float sNow, in TrackBody b, int dir = 1, float lead = 0f)`**: the face is `S + dir·(HalfLength + lead)`. Also `InWarning(float sNow, float v, in TrackBody b, float seconds = 3f, int dir = 1, float lead = 0f)`, which looks along `dir` from the face.
  - **`RunOverSweep.Judge(..., float lead = 0f)`** and **`RailWarn.Ahead(TrackSampler track, float s, float v, Vector3 pos, int dir = 1, float lead = 0f)`.**
  - **`RailController` (`.Hijack.cs`):**
    - `HijackPhase Hijack { get; private set; }`, `bool HijackLive`, `float HijackLimitS`, and `float HijackLead` (wagons × Spacing).
    - `internal void BeginRollBack(Enemy? driver)`: it raises `Hijacked`, sets `Party.Dir = -1`, `Reversing`, the Limit `LimitS` and `Rolling`, and prints `[rails] hijack: the train rolls back from stop {k} (limit S={l:0.0}, {m:0.0} m of roll{; cut by …})`.
    - `internal void BrakeRollBack(string why)`.
    - The return: at V 0 the run flips to `Dir +1`, `Reversing` false, the Limit `StopS[k]`, and the Returning phase. Held there, it raises `HijackOver` and prints `[rails] hijack over at stop {k}: the train held again ({why})`; with a player seated and the gate clear, `StartBoarding()`.
    - `TickHijack(PartyController party, float dt)` steps `InputFor(...)`, judges `RunOver` with `lead = HijackLead` while `Reversing`, and warns with `RailWarn.Ahead(..., Party.Dir, lead)`. It never calls `HitBlock`, `JudgeJump`, `Arrive` or `Watch`.
    - Test hooks: `internal bool DebugRollBack()` (false unless Held at stops 1..N−2 with `RollRoom`) and `internal void DebugEndRollBack()`.
  - **`MineCart`**: `Enemy? Driver`, `Transform3D DriverSpotTransform()` (a third spot, mid-tub at the front rim, `BoarderLift` above it), and `void FreeDriver()`.
  - **`RailEncounterController.CollectRollBackHazards(List<(float From, float To)> into)`.**
  - **The chaser.** While `HijackLive`, `spare:{k}` prompts "" and a take prints `[rails] spare {k}: refused (a hijack is live)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheRollBack_GainsHalfAMetrePerSecond_UpToFour()
{
    var c = new CartState(500f, 0f, CartMode.Rolling, 100f); var i = HijackRules.InputFor(HijackPhase.RollingBack, 0f, 4, 10000f); // a forward cart with its limit far off stands in for the mirrored one
    for (var t = 0; t < 120; t++) c = CartPhysics.Step(c, i, 1f / 60f);  // 2 s
    Assert.InRange(c.V, 0.9f, 1.05f);
    for (var t = 0; t < 1200; t++) { c = CartPhysics.Step(c, i, 1f / 60f); Assert.True(c.V <= HijackRules.RollBackMaxSpeed + 0.01f); }
    Assert.InRange(c.V, 3.9f, 4.01f);
}
[Fact] public void DefaultDriveAccel_IsUnchanged() { /* the same input with DriveAccel −1 and with DriveAccel 2.5 give equal states over 600 steps */ }
[Fact] public void TheBrake_StopsFourMetresASecondInUnderOneSecond() { /* InputFor(Braking, 0f, 4, 10000f) from V 4: V 0 within 0.75 s */ }
[Fact] public void OnlyTheRollAndItsBrake_Reverse() { Assert.True(HijackRules.Reversing(HijackPhase.RollingBack)); Assert.True(HijackRules.Reversing(HijackPhase.Braking)); Assert.False(HijackRules.Reversing(HijackPhase.Returning)); Assert.False(HijackRules.Reversing(HijackPhase.ReliefIn)); Assert.Equal(-1f, HijackRules.DriveAccelFor(HijackPhase.Returning)); }
[Fact] public void TheLimit_IsFortyMetresWithNothingBehind() => Assert.Equal(243.2f, HijackRules.LimitS(283.2f, 4, Array.Empty<(float, float)>()), 3);
[Fact] public void TheLimit_KeepsTheLeadingFaceFiveMetresShortOfAHole()
{
    var l = HijackRules.LimitS(770.1f, 4, new[] { (710.7f, 718.7f) });
    Assert.Equal(718.7f + 5f + 4 * TrainRules.Spacing + CartRules.HalfLength, l, 3);  // the last wagon's back face 5 m short of the gap's far edge
    Assert.Equal(983.1f + 16.5f, HijackRules.LimitS(1022.5f, 4, new[] { (903.1f, 983.1f), (1030f, 1040f) }), 3); // a hazard ahead of the stop is ignored
}
[Fact] public void ALimitUnderFiveMetres_AllowsNoHijack() { Assert.False(HijackRules.RollRoom(1022.5f, 1018f)); Assert.True(HijackRules.RollRoom(1022.5f, 1017.5f)); }
[Fact] public void RunOver_InReverse_IsJudgedFromTheLastWagonsBack()
{
    var lead = 4 * TrainRules.Spacing; var body = new TrackBody(S: 488.0f, Lateral: 0f, Height: 0f, Radius: 0.4f);
    Assert.False(RunOverRules.Hits(501.0f, 500.9f, body, -1));            // the cart's own back is far from it
    Assert.True(RunOverRules.Hits(499.55f, 499.45f, body, -1, lead));     // the face 499.55−11.5 = 488.05 → 487.95: swept
    Assert.True(RunOverRules.InWarning(501f, 4f, body with { S = 480f }, 3f, -1, lead));
    Assert.False(RunOverRules.InWarning(501f, 4f, body with { S = 505f }, 3f, -1, lead)); // behind the way of travel
}
[Fact] public void TheDto_CarriesTheHijack_AndTheDriver() { /* CartStateDto { Hijack = (byte)HijackPhase.RollingBack, Dir = -1, Boarders = [-1, -1, 200017] } round-trips through NetCodec equal */ }
[Fact] public void TheFollower_RollsBackAsTheServerDoes() { /* CartFollower.Apply(dto with Dir −1, Rolling, V 1, Limit 243.2, TargetOverride 4, Hijack RollingBack) then Advance 2 s: S within 0.3 of a server CartRun stepped mirrored with InputFor(RollingBack) */ }
// NetCodecTests: Assert.Equal(18, Protocol.Version); // v18: the hijack's reverse roll (CartStateDto.Hijack, the driver as Boarders[2]); v17: …
```

  (Use `TrackBody`'s real constructor; the names above stand for its S, lateral, height and radius.)

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~HijackRulesTests|FullyQualifiedName~RunOverRulesTests|FullyQualifiedName~CartPhysicsTests|FullyQualifiedName~CartFollowerTests|FullyQualifiedName~CartNetTests|FullyQualifiedName~NetCodecTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the rules, the `DriveAccel` input, `Facing`, the `lead` parameters, the new kinds, `RailController.Hijack.cs`, the driver spot, the wire, the follower, the spare refusal and `CollectRollBackHazards`.
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS, with every existing `RunOverRulesTests`, `CartPhysicsTests`, `CartFollowerTests`, `RideLinesTests` and `CartNetTests` row unchanged.
- [ ] **Step 5: Write `--test-stop-trap=roll`.**
  - Setup:
    - `rc.Restore(2)` and `rc.DebugKillHolders(2)`; the lead gets out (its companions are set down).
    - One companion is put on the party lane 15 m behind the last wagon's back face. An `enemy.horde_cinder` with `DebugHoldHp` is put on the rails 25 m behind it.
    - The flow counts every `CartEvent`.
  - It fails unless:
    - **Start.** `rc.DebugRollBack()` returns true and prints `rolls back from stop 2`.
    - **Speed.** V at 2 s is 1.0 ± 0.3, and V never goes above 4.05.
    - **Facing and wagons.** On every tick, the cart's forward · the track tangent > 0.9, and each Wk stands at the cart's S − 2.6k ± 0.05 and faces the cart's way.
    - **The warning.** The companion gets "Cart coming!" while the leading face is 3 s from it, and never from the cart's own back.
    - **The run-over.** The cinder is hit when the cart's S is within 0.4 of its S + 11.5.
    - **Nothing confused.** No `Departed`, `Arrived` or `Resumed` is raised, `StopIndex` stays 2, `ArmedRide` stays −1, no `watchdog:` line and no gate change are printed, and `spare:2` prompts "".
    - **The return.** `DebugEndRollBack()` at 8 s: V reaches 0 within 0.8 s, the train returns at 2 ± 0.1 m/s and is Held at `StopS[2]` ± 0.05, and `hijack over at stop 2` and `HijackOver` appear.
    - **The limit.** A second `DebugRollBack()`, left alone, reaches `HijackLimitS` ± 0.1, brakes and returns (Ruling 11).
    - **A load mid-roll** (Review Focus 2). A third roll is saved at 4 s and loaded: Held at `StopS[2]`, `Hijack` None, `Facing` +1, every Wk at S − 2.6k, `Cart.Driver` null.
    - **A real departure.** The lead boards and the countdown starts; `Departed` arms ride 3.
- [ ] **Step 6: Run** `scripts/flows.sh "--test-chase --quit-after=750" "--test-crew --quit-after=570" "--test-stop-trap=roll" "--test-boarders=groups" "--test-island=lift" "--test-runaway=tail" "--test-split" "--test-cavern" "--test-pit" "--test-relief" "--test-ride" "--test-cart-watchdog"`. Expected: every line PASS.
- [ ] **Step 7: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok` (both ends are at 18).
- [ ] **Step 8: Commit**

```bash
git add src/Dungeon/Rails/Core/HijackRules.cs src/Dungeon/Rails/RailController.Hijack.cs src/Dungeon/Rails/Core/CartPhysics.cs src/Dungeon/Rails/CartRun.cs src/Dungeon/Rails/CartTrain.cs src/Dungeon/Rails/Core/RunOverRules.cs src/Dungeon/Rails/RunOverSweep.cs src/Dungeon/Rails/RailWarn.cs src/Dungeon/Rails/Core/CartEvents.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailController.Chase.cs src/Dungeon/Rails/MineCart.cs src/Net/Core/CartNet.cs src/Net/Core/Protocol.cs src/Dungeon/Rails/Core/CartFollower.cs src/Dungeon/Rails/RailController.Net.cs src/Dungeon/Rails/RailNet.Leaps.cs src/Dungeon/Rails/RailEncounterController.cs src/Core/DebugTools.StopTrap.cs src/Core/DebugTools.StopTrap.Roll.cs
git commit -m "feat(rails): the reverse roll: the train rolls back with its facing kept apart from its way of travel on every peer (wagons on the lower-S side), run-over and warning from the leading face, no arrival, departure or watchdog, the limit short of holes, protocol 18 (CartStateDto.Hijack, the driver as Boarders[2]); --test-stop-trap=roll

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The hijack, won back

**Files:**
- Create: `src/Dungeon/Rails/StopTrapController.Hijack.cs`, `src/Enemies/Ai/RunForCartState.cs`, `src/Core/DebugTools.StopTrap.Hijack.cs`.
- Modify:
  - `src/Dungeon/Rails/Core/HijackRules.cs` (start, tag and board rules);
  - `src/Dungeon/Rails/RailController.Hijack.cs` (`TakeDriver`, the glow, the driver's grip and knock-off);
  - `src/Dungeon/Rails/RailController.cs` (`UseCart` and the prompt while rolling back);
  - `src/Dungeon/Rails/RailEncounterController.Leaps.cs` (`BoardWagonAtStop`, `Leap.Spared`; `ClearLeaps(bool wrecked, bool all)`), `src/Dungeon/Rails/RailEncounterController.cs` (`ClearRide`'s `all` for Wrecked, Restored and OnFoot);
  - `src/Adventure/Core/Voice/StopTrapLines.cs`.
- Test: `tests/Dungeon/Rails/HijackRulesTests.cs`, `tests/Adventure/StopTrapLinesTests.cs`

**Rulings this task needs:**
- **8. Any seated hero guards the train.** `MayStart` counts every hero seated in the cart or a wagon, up or downed; one is enough to stop a hijack starting. A hero who sits during the glow does not stop the roll (the driver has the brake already). Cost: a downed body in a seat guards the cart.
- **9. A hit hijacker fights back.** For 3 s after a hit it is in `ChaseState` against its attacker. Then, while `MayStart` holds and it is still tagged, it runs for the train again. A climb under way is cancelled by a hit. Cost: none.
- **11.** As in Task 5. Cost: none.
- **12. Boarding the rolling-back train.** The cart can be boarded on foot while `MayBoard(phase, V)` holds: RollingBack, Braking or Returning, at V ≤ 4.05. It has the Held boarding's rules otherwise (a hero seat free, short of the last stop). A remote player's intent takes the same path. Cost: none.
- **Spec Ruling 2, verbatim.**
  - No hijacker is tagged in a stop's first wave, and only one hijack is live at a time.
  - After a hijack ends (won back, or the relief arrived), the next tag waits 20 s.
  - At most one tag per wave, and only a `enemy.horde_cinder` or an `enemy.ember_hound`.
  - A tag is refused while the stop has no roll room (`RollRoom` false).
  - A second tagged creature, while the cart is held or claimed, goes for a wagon as a spared boarder.

**Interfaces:**
- Consumes:
  - T3's `SiegeSpawned`, `StopTrapController` and `StopTrapLines`;
  - T5's `BeginRollBack`, `BrakeRollBack`, `MineCart.Driver`/`DriverSpotTransform`, `HijackPhase` and `RollRoom`;
  - `BoarderRules` (`Grip`, `GripTaken`, `KnockedOff`, `Strikes`, `Counts`), `EventBus.EnemyHurt`, `TelegraphFx.HandGlow`, `TelegraphRules.BoarderThrow`, `Enemy.Carry`/`Drop`/`DeathDrop`, and `DebugBoard`'s pattern.
- Produces:
  - **`HijackRules`:**
    - Constants: `ClimbReach = 2f`, `ClimbSeconds = 0.8f`, `FightBackSeconds = 3f`, `NextTagAfter = 20f`.
    - `static bool MayStart(int stop, int stopCount, bool held, bool halted, bool standingPast, int heroesSeatedInTrain, bool chaseLive, bool risingGrace, bool hijackLive, bool rollRoom)`.
    - `static bool MayTag(int wave, bool hijackLive, float sinceLastHijackEnded, bool taggedThisWave, string enemyId, bool rollRoom)`.
    - `static bool MayBoard(HijackPhase p, float v)`.
  - **`StopTrapLines`**: `CrewCartTaken = "They've got the cart!"`, `BrakeOffLog = "{0} lets the brake off! The train rolls back!"`, `KnockedOffCartLog = "{0} knocks the {1} off the cart!"` and `ReturnLog = "The train rolls back to the platform."`.
  - **`RunForCartState(Func<Vector3> goal)`**: `MoveAlongPathToward(goal())`, never `Unaware`.
  - **`StopTrapController` (hijack):**
    - `IReadOnlyList<Enemy> Runners`. A tag prints `[hijack] stop {k} wave {w}: {name} tagged`.
    - Each runner is ticked at 10 Hz: the cart's side when the cart is free, else the nearest coupled wagon with a free boarder spot (crewed first). Within `ClimbReach` it climbs (`[hijack] {name} climbs into {the cart|wagon n}`), and after `ClimbSeconds` it calls `rc.TakeDriver(e)` or `enc.BoardWagonAtStop(e, w)`.
    - A `Restored`, `Wrecked`, `OnFoot` or `Departed` releases every runner to `ChaseState`, untagged.
    - The 20 s clock starts at `HijackOver`.
    - `internal Enemy? DebugTagNext()`: the next siege spawn of an allowed kind is tagged, whatever its wave (for flows and the RIDE hook).
  - **`RailController.TakeDriver(Enemy e)`:**
    - It carries `e` at `DriverSpotTransform`, sets `DeathDrop` beside the lane, enters the Glow phase, runs `TelegraphFx.HandGlow(e, 3f)` with the `BoarderThrow` telegraph to clients, and has the nearest freed, up crew member say `CrewCartTaken` (`SayAs`, key `hijack:{stop}:{n}`).
    - 3 s later it logs `BrakeOffLog` and `BeginRollBack(e)`.
  - **The driver's grip** (its own `EnemyHurt` handler): a counted, striking hit (on foot always; seated only from car 0) takes `GripTaken`.
    - At `KnockedOff` the driver drops beside the lane, alive and aggro'd, and `KnockedOffCartLog` is logged.
    - It prints `[rails] hijack: {name} knocked off the cart by {by} after {n} hit(s)`, then `BrakeRollBack("the driver is knocked off")`, which logs `ReturnLog` when the return starts.
    - A driver's death drops it and brakes the same way.
    - `internal int DriverGrip` is a test hook.
  - **`RailEncounterController.BoardWagonAtStop(Enemy e, TrainWagon w)`** (bool): `DebugBoard`'s steps with the real enemy and `Spared = true`. A spared leap is kept by `ClearLeaps(false, all: false)` (`Departed`/`Arrived`) and freed on `Wrecked`, `Restored` and `OnFoot`.

- [ ] **Step 1: Write the failing tests**

```csharp
static bool Start(int seated = 0, bool held = true, bool chase = false, bool rising = false, bool live = false, int stop = 2, bool room = true)
    => HijackRules.MayStart(stop, 6, held, halted: false, standingPast: false, seated, chase, rising, live, room);
[Fact] public void AnUnattendedHeldTrain_MayBeHijacked() => Assert.True(Start());
[Fact] public void ADownedHeroInASeat_StillGuardsTheTrain() => Assert.False(Start(seated: 1)); // the count is up and downed alike
[Fact] public void NeverOnARide_AChase_ARise_TheLandingOrTheIsland_OrTwice()
{ Assert.False(Start(held: false)); Assert.False(Start(chase: true)); Assert.False(Start(rising: true)); Assert.False(Start(live: true)); Assert.False(Start(stop: 0)); Assert.False(Start(stop: 5)); Assert.False(Start(room: false)); }
[Fact] public void NoTagInTheFirstWave_NorWithinTwentySeconds_NorOfAPriest()
{
    Assert.False(HijackRules.MayTag(1, false, 99f, false, "enemy.horde_cinder", true)); Assert.True(HijackRules.MayTag(2, false, 99f, false, "enemy.ember_hound", true));
    Assert.False(HijackRules.MayTag(3, false, 19.9f, false, "enemy.horde_cinder", true)); Assert.False(HijackRules.MayTag(3, false, 99f, true, "enemy.horde_cinder", true));
    Assert.False(HijackRules.MayTag(3, false, 99f, false, "enemy.forge_priest", true)); Assert.False(HijackRules.MayTag(3, false, 99f, false, "enemy.horde_cinder_brute", true));
}
[Fact] public void TheRollingBackTrain_MayBeBoarded_AtWalkingSpeeds()
{ Assert.True(HijackRules.MayBoard(HijackPhase.RollingBack, 4f)); Assert.True(HijackRules.MayBoard(HijackPhase.Returning, 2f)); Assert.False(HijackRules.MayBoard(HijackPhase.RollingBack, 4.2f)); Assert.False(HijackRules.MayBoard(HijackPhase.Glow, 0f)); Assert.False(HijackRules.MayBoard(HijackPhase.None, 0f)); }
// StopTrapLinesTests: CrewCartTaken, BrakeOffLog, KnockedOffCartLog, ReturnLog non-empty, VoiceId null, the two logs format with names
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~HijackRulesTests|FullyQualifiedName~StopTrapLinesTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the rules, `RunForCartState`, the tagging and runners, `TakeDriver`, the glow, the grip, `BoardWagonAtStop`, `Spared`, `ClearRide`'s `all`, and the boarding while rolling back.
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 5: Write `--test-stop-trap=wonback`.**
  - Setup: `rc.Restore(2)`. The lead steps out and stands by the cage (locked); the companions are set down. The siege restarts after its 10 s grace, and the flow calls `DebugSiegeNow` between waves.
  - It fails unless:
    - **Tagging.** Wave 1 tags nobody, and wave 2 tags exactly one cinder or hound.
    - **The run.** The runner's distance to the cart falls over 3 s. Within 2 m it climbs, and 0.8 ± 0.1 s later it is `Cart.Driver`. The glow telegraph is told, and `CrewCartTaken` is said (or logged) once.
    - **The roll.** It starts 3 ± 0.1 s after the climb, with the `BrakeOffLog` line.
    - **Won back on foot.** With every companion's `DebugHoldFire` set, the lead, teleported beside the cart's side, strikes the driver with `DebugStrikeAt` three times. After the third (`DriverGrip` 1, 2, then off) the driver stands beside the lane, alive, not carried and aggro'd. V reaches 0 within 0.8 s, and the train is Held at `StopS[2]` ± 0.05 with `hijack over at stop 2`.
    - **Nothing undone.** The gate, the cage's lock and `StopIndex` are as before, and no crew are counted lost.
    - **The 20 s wait.** No tag for 20 s after `HijackOver`; then the next wave tags one.
    - **Won back from the seat.** The second hijacker becomes the driver and the roll starts. The lead boards the rolling cart (`UseCart` at V ≤ 4: seated), strikes the driver from seat 0 three times, and it is knocked off; brake, return, Held.
    - **A wagon boarder.** With the cart's driver aboard (a third hijack, via `DebugTagNext`), a second `DebugTagNext` runner goes for a wagon and rides W1's boarder spot as a spared leap. It is still aboard after `HijackOver`, and still there after the departure's `Departed`.
    - **A Restore under way.** A Restore while a runner climbs leaves `Cart.Driver` null, `Hijack` None and the runner in `ChaseState`.
- [ ] **Step 6: Run** `scripts/flows.sh "--test-chase --quit-after=750" "--test-crew --quit-after=570" "--test-stop-trap=wonback" "--test-stop-trap=roll" "--test-stop-trap=siege" "--test-boarders" "--test-boarders=knock" "--test-boarders=groups" "--test-ride"`. Expected: every line PASS.
- [ ] **Step 7: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok`.
- [ ] **Step 8: Commit**

```bash
git add src/Dungeon/Rails/Core/HijackRules.cs src/Dungeon/Rails/StopTrapController.Hijack.cs src/Enemies/Ai/RunForCartState.cs src/Dungeon/Rails/RailController.Hijack.cs src/Dungeon/Rails/RailController.cs src/Dungeon/Rails/RailEncounterController.Leaps.cs src/Dungeon/Rails/RailEncounterController.cs src/Adventure/Core/Voice/StopTrapLines.cs src/Core/DebugTools.StopTrap.cs src/Core/DebugTools.StopTrap.Hijack.cs
git commit -m "feat(stops): the hijack, won back: siege hijackers tagged with the solo pacing (none in wave 1, one live, 20 s after), the run for the cart, the driver spot and its 3 s glow, the roll back, the driver struck from foot or from the rolling cart, knocked off, the brake and the return to the stop; wagon hijackers as spared boarders; --test-stop-trap=wonback

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The hijack, got away (the limit pinned, throw-offs, the relief train, the wait for the thrown crew)

**Files:**
- Create: `src/Adventure/Objectives/CrewRescueController.Hijack.cs`.
- Modify:
  - `src/Dungeon/Rails/Core/HijackRules.cs` (throws, relief and the wait), `src/Dungeon/Rails/Core/TrainRules.cs` (`KeepsBooking` and `SeatsAtOnce` with a reach);
  - `src/Dungeon/Rails/RailController.Hijack.cs` (`OnLimit` → Gone, the throws, the hide and uncouple, the relief);
  - `src/Adventure/Objectives/CrewRescueController.cs` (`TickBoarding`'s reach for thrown crew; `HoldsDeparture` with the thrown hold);
  - `src/Adventure/Core/Voice/StopTrapLines.cs`, `src/Core/DebugTools.StopTrap.Hijack.cs` (`=gotaway`), `src/Core/DebugTools.StopTrap.cs`.
- Test: `tests/Dungeon/Rails/HijackRulesTests.cs`, `tests/Dungeon/Rails/TrainRulesTests.cs`

**Rulings this task needs:**
- **10 (the pins).** Step 1 measures each stop's limit on the shipped layout, with four wagons, after ride 4's bridge has gone. `ShippedLimits` pins it, with the hazard spans the flow printed. Cost: none.
- **13. The throw-off order.**
  - The first throw comes at 3 s of RollingBack, then one every 3 s.
  - Wagons go from the last coupled one forward, and within a wagon seat 5 then seat 4 (`BoarderRules.ThrowSeat`).
  - The thrown crew member is set down alive at `StandBesideLane(wagon)` and starts following the party; it is never `ThrowCrew`.
  - At the limit, the crew still aboard are set down the same way.
  - Cost: none.
- **14. The relief rolls in from the limit.**
  - At Gone, the driver and every wagon boarder are dropped beside the lane, alive and aggro'd. A hero seated in the train is set down alive. The cart is hidden (`CartFlags.Hidden`, not solid) and every wagon is uncoupled and hidden.
  - 12 s later the cart is shown again at `HijackLimitS` with full plating and `CoupleFresh("a relief train after the hijack")`. It rolls forward (Dir +1, the ReliefIn phase, TargetOverride 4, judged and warned as any forward run) and is Held at `StopS[k]`. This is not an `Arrive()`.
  - `HijackOver` is raised, and `ReliefLog` and the crew's `CrewRelief` are said.
  - No spare is used, and none is needed.
  - Cost: none.
- **15. The wait.**
  - Crew a hijack set down are "thrown" until they sit, die, or the floor changes. Their booking reach is 60 m.
  - The departure waits while any thrown crew member is on the way and under 30 s have passed since the train was held again (`HijackOver`).
  - At 30 s, thrown crew within 60 m sit in at once; farther ones stay on foot as freed crew.
  - Crew who die are lost as in any fight.
  - Cost: a sit-in from up to 60 m away.
- **16 (this task's part).** A save during a hijack records the crew as they stand. After a load, thrown crew on foot are ordinary freed crew (25 m reach), and the train stands whole at the stop (Task 5). Cost: one thrown far back walks closer before it books.

**Interfaces:**
- Consumes:
  - T5's phases, `HijackLimitS` and `Hidden`;
  - T6's driver, runners and spared leaps;
  - `CoupleFresh`, `_train.UncoupleAll`, `StandBesideLane`, `TrainRules.BoardReach`, `HopReach` and `BoardMax`;
  - `Boarding`, `CrewBoardingHolds`.
- Produces:
  - **`HijackRules`:**
    - Constants: `ThrowEvery = 3f`, `ReliefAfter = 12f`, `ThrownBoardReach = 60f` and `ThrownWaitMax = 30f`.
    - `static bool ThrowDue(float rollingSeconds, int thrown)`.
    - `static (int Wagon, int Seat)? NextThrow(IReadOnlyList<(int Wagon, bool Seat4, bool Seat5)> train)`: the train is in coupling order, and the last wagon goes first.
    - `static bool ReliefDue(float goneSeconds)`.
    - `static bool HoldsForThrown(int thrownOnTheWay, float sinceHeldAgain)` and `static bool SeatsThrownAtOnce(float sinceHeldAgain, float distance)`.
  - **`TrainRules.KeepsBooking(bool boardsHere, bool up, float distance, float reach)`** and **`SeatsAtOnce(float elapsed, float distance, float reach)`**: overloads. The old signatures pass `BoardReach`.
  - **`StopTrapLines`**: `GoneLog = "The train rolls on into the dark."`, `ReliefLog = "A relief train rolls up to the platform."` and `CrewRelief = "Another train! Get us aboard!"`.
  - **`CrewRescueController`**:
    - `internal void PutOffAlive(Npc npc, TrainWagon wagon)` prints `[CrewRescue] {name} put off wagon {n} alive by the hijack`.
    - `internal int ThrownOnTheWay(int stop)`.
    - `internal IReadOnlyCollection<string> ThrownIds`, a test hook.
  - **`RailController`** prints `[rails] hijack: {n} crew put off on the way, {m} set down at the limit; the train is gone at S={s:0.0}` and `[rails] hijack: the relief train is held at stop {k} ({t:0.0} s after the train went)`.
  - **`--test-stop-trap=limits`.** It prints `[hijack] stop {k}: limit S={l:0.0} ({m:0.0} m of roll; {the 40 m cap|cut by {from:0.0}..{to:0.0}})` for stops 1-4.
    - Stops 1-3 are read after `rc.Restore(k)` with four wagons.
    - Stop 4 is read after a real ride 4 from stop 3, so the bridge is gone. It is read once the ride runaway's linger is over, because a lingering runaway cuts the limit by design (every enemy culled; heroes topped up).
    - It prints `[stoptrap] RESULT ok (limits)`, with `_quitAfter` 200.

- [ ] **Step 1: Measure the limits** (lock absent). Run `dotnet build DungeonCrawler.csproj && XDG_DATA_HOME=$(mktemp -d) timeout 320 godot --headless --path . -- --test-stop-trap=limits 2>&1 | grep -E "\[hijack\] stop|RESULT"`. Expected: four lines, about S 243.2 (the cap), 448.8 (the cap), about 735 (cut by ride 3's gap) and about 1008 (cut by the gone span), with stop 4 at 5 m of roll or more. Record the exact values and spans for Step 2. A stop under 5 m of roll is recorded as such: it allows no hijack (Ruling 10).
- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void ShippedLimits() { /* LimitS for stops 1-4 with the spans Step 1 printed equals the printed S to 0.1 each (the pins), and each stop's RollRoom as printed */ }
[Fact] public void TheThrows_EveryThreeSeconds_FromTheRearSeatOfTheLastWagon()
{
    Assert.False(HijackRules.ThrowDue(2.9f, 0)); Assert.True(HijackRules.ThrowDue(3f, 0)); Assert.False(HijackRules.ThrowDue(5.9f, 1)); Assert.True(HijackRules.ThrowDue(6f, 1));
    Assert.Equal((2, 5), HijackRules.NextThrow(new[] { (1, true, true), (2, true, true) }));
    Assert.Equal((2, 4), HijackRules.NextThrow(new[] { (1, true, true), (2, true, false) }));
    Assert.Equal((1, 5), HijackRules.NextThrow(new[] { (1, true, true), (2, false, false) }));
    Assert.Null(HijackRules.NextThrow(new[] { (1, false, false) }));
}
[Fact] public void TheRelief_ComesTwelveSecondsAfter() { Assert.False(HijackRules.ReliefDue(11.9f)); Assert.True(HijackRules.ReliefDue(12f)); }
[Fact] public void TheDeparture_WaitsForThrownCrew_ThirtySecondsAtMost()
{ Assert.True(HijackRules.HoldsForThrown(1, 29.9f)); Assert.False(HijackRules.HoldsForThrown(1, 30f)); Assert.False(HijackRules.HoldsForThrown(0, 1f));
  Assert.True(HijackRules.SeatsThrownAtOnce(30f, 59f)); Assert.False(HijackRules.SeatsThrownAtOnce(30f, 61f)); Assert.False(HijackRules.SeatsThrownAtOnce(29f, 10f)); }
[Fact] public void ThrownCrew_BookFromSixtyMetres_OthersFromTwentyFive()
{ Assert.True(TrainRules.KeepsBooking(true, true, 55f, HijackRules.ThrownBoardReach)); Assert.False(TrainRules.KeepsBooking(true, true, 55f)); Assert.True(TrainRules.KeepsBooking(true, true, 24f)); }
```

- [ ] **Step 3: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~HijackRulesTests|FullyQualifiedName~TrainRulesTests"`. Expected: a compile error.
- [ ] **Step 4: Implement** the rules, the overloads, `PutOffAlive`, the thrown reach and hold, the throws, Gone, the relief and the lines.
- [ ] **Step 5: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 6: Write `--test-stop-trap=gotaway`.**
  - Setup: `rc.Restore(3)` with stop 1's and stop 2's crew seated in W1 and W2 (`DebugSeatCrew`). The lead and the companions step out and are held 30 m up the platform's far side (`ForcedMoveInput` null, teleported). The siege runs. The hijack comes through `DebugTagNext` and the real run.
  - It fails unless:
    - **The throws.** The crew are put off at 3, 6, 9 and 12 s of RollingBack ± 0.2, in the order W2 seat 5, W2 seat 4, W1 seat 5, W1 seat 4 (or fewer, as the limit allows). Each is alive, beside the lane, not on a rail cell, and following.
    - **Gone.** At `HijackLimitS` ± 0.1 the train is Gone. The driver stands beside the lane alive, the cart and every wagon are hidden and not solid, the crew still aboard are set down there alive, and `GoneLog` is logged. No crew member is Lost.
    - **The relief.** 12 ± 0.3 s later the cart is shown at the limit with W1..W4 coupled fresh and empty, rolls forward at 4 ± 0.2 m/s and is Held at `StopS[3]` ± 0.05. `ReliefLog` appears, `HijackOver` is raised, no `Arrived` is raised, `StopIndex` stays 3, and the spare count is unchanged.
    - **The crew come back.** The lead boards and the countdown starts. The departure waits until every thrown crew member is seated, or until 30 s after the relief's hold. Every thrown crew member within 60 m books (`ThrownIds`) and is seated before the train leaves, and the departure's `Departed` arms ride 4.
    - **A death on the way.** In a second pass (`rc.Restore(3)`, the same setup), one thrown crew member is killed on the way. It is counted lost, and the departure waits only for the living.
    - **Save mid-roll** (Review Focus 2). In a third pass, a save at 5 s of RollingBack is loaded: the train is whole and Held at stop 3 with `Hijack` None, the crew put off before the save are on foot, and the siege's first wave comes at 10 ± 0.5 s.
- [ ] **Step 7: Run** `scripts/flows.sh "--test-chase --quit-after=750" "--test-crew --quit-after=570" "--test-crew=forgiven --quit-after=570" "--test-stop-trap=gotaway" "--test-stop-trap=wonback" "--test-stop-trap=roll" "--test-stop-trap=limits" "--test-island" "--test-island=onfoot" "--test-boarders=groups" "--test-relief"`. Expected: every line PASS.
- [ ] **Step 8: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok`.
- [ ] **Step 9: Commit** (put the measured limits in the message)

```bash
git add src/Dungeon/Rails/Core/HijackRules.cs src/Dungeon/Rails/Core/TrainRules.cs src/Dungeon/Rails/RailController.Hijack.cs src/Adventure/Objectives/CrewRescueController.cs src/Adventure/Objectives/CrewRescueController.Hijack.cs src/Adventure/Core/Voice/StopTrapLines.cs src/Core/DebugTools.StopTrap.cs src/Core/DebugTools.StopTrap.Hijack.cs
git commit -m "feat(stops): the hijack, got away: crew put off alive one every 3 s from the rear, the train gone at its limit (pinned per stop: …), a relief train 12 s later from behind, and the departure waiting up to 30 s for the thrown crew (60 m reach); --test-stop-trap=gotaway and =limits

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: The alcove shooters

**Files:**
- Create:
  - `src/Dungeon/Rails/Core/AlcoveRules.cs` (engine-free), `src/Enemies/Core/LeadAim.cs` (engine-free, System.Numerics);
  - `src/Enemies/Ai/AlcoveState.cs`, `src/Dungeon/Rails/RailEncounterController.Alcoves.cs`, `src/Core/DebugTools.StopTrap.Alcove.cs`.
- Modify:
  - `src/Enemies/Enemy.cs` (`Pinned`; `ChangeState` refuses everything but `DeadState` while pinned; `FireAt(Vector3 aim)`);
  - `src/Dungeon/Rails/RailEncounterController.cs` (`Arm` arms the alcoves; `ClearRide` clears them);
  - `src/Dungeon/Rails/RailEncounterController.Traffic.cs` (`SeekRefuge` skips a shooter's pocket);
  - `src/Core/DebugTools.StopTrap.cs`.
- Test: `tests/Dungeon/Rails/AlcoveRulesTests.cs`, `tests/Enemies/LeadAimTests.cs`

**Rulings this task needs:**
- **2 (this task's part).** The shooters are placed only when `Settings.Ambushes && !NoAmbush`, never on the RIDE server, and only on a railed floor whose line has `AlcoveRules.RideCount + 1` (6) stops. Cost: turning ambushers off in Settings also turns the shooters off.
- **Spec Ruling 4, verbatim.** The shooters are placed by code, not by floor_12.json data (an `AlcoveRules` table of counts and stretches per ride, applied on any railed floor with this ride count). No validator change.
- **17. The placement.**
  - The "busy" spans are every piece's and encounter's S (a point, or its span), the switch windows, the dark span, the cavern span, and each stop's S. Each is widened by 30 m on both sides.
  - Candidates are the ride's pockets outside every busy span and not in `claimed` (pockets a seeker holds now). They go in tiers:
    1. enemy lane inside the stretch;
    2. enemy lane outside it;
    3. party lane inside;
    4. party lane outside.

    Each tier is shuffled by the seeded rng (`seed ^ 0xA1C0 ^ ride`). Picks are at least 8 m apart in S, stopping at the count.
  - A ride places as many as fit. Ride 4 may get none: the dark, the split and the bridge cover most of it. That is printed, and pinned in the flow.
  - The kind is seeded 50/50 `enemy.forge_priest`/`enemy.bandit_archer`.
  - The shooter waits at the back of its pocket with no visibility toggle; the dark niche hides it, and a client sees the same.
  - A refuge seeker never picks a pocket with a live shooter.
  - Cost: ride 4 may stay without shooters, and a sharp-eyed rider may glimpse one early.

**Interfaces:**
- Consumes:
  - the pockets, through `EnsurePockets`' cells (S = the party track's closest offset to the pocket's surface, as `PlaceCage` measures a platform);
  - `_pieces`, `_encounters`/`RideS`, the dark and cavern spans;
  - `_builder.SpawnEnemy`, `ClearStanders`' quiet free, `RailController.Party` (`FrontS`), T5's `SpareRules.TrainBackS`, and `ICartBody.Velocity`.
- Produces:
  - **`AlcoveRules`:**
    - `const int RideCount = 5`, and `readonly record struct AlcoveRide(int Count, float From, float To)` with `static AlcoveRide For(int ride)`: 1 → (4, 0.5, 0.75); 2 → (5, 0.55, 0.65); 3 → (5, 0.4, 0.7); 4 → (3, 0, 1); 5 → (4, 0.05, 0.18).
    - Constants: `Clear = 30f`, `Spacing = 8f`, `WakeAt = 35f`, `ShootRange = 25f`, `ShootEvery = 1.6f`, `GoneBehind = 30f`.
    - `readonly record struct Pocket(int Index, float S, bool EnemyLane)`.
    - `static List<int> Place(int ride, float rideFromS, float rideToS, IReadOnlyList<Pocket> pockets, IReadOnlyList<(float From, float To)> busy, IReadOnlySet<int> claimed, int seed)` (pocket indices).
    - `static string Kind(int seed, int ride, int k)`.
    - `enum AlcovePhase { Hidden, StepOut, Shooting, Done }` and `static AlcovePhase Next(AlcovePhase p, float frontToPost, float backPastPost)`:
      - Hidden → StepOut when the front is within 35 m before the post;
      - StepOut → Shooting after 0.5 s (the state's own timer);
      - any phase → Done once the back is 30 m past.
  - **`LeadAim.Point(Vector3 from, Vector3 target, Vector3 targetVelocity, float projectileSpeed) -> Vector3`**: the earliest intercept, or `target` when none.
  - **`AlcovePost`** (a class in `AlcoveState.cs`): `Vector3 Back`, `Vector3 Mouth`, `Func<float> FrontToPost`, `Func<float> BackPastPost` and `Func<Adventurer?> Target` (the nearest seated hero in range and sight, or null). The controller fills it.
  - **`AlcoveState(AlcovePost post)`.** It is pinned and never `Unaware`.
    - Hidden: a 0.25 s distance check only.
    - StepOut: it moves from the post's back (the pocket centre 1.2 m away from the lane) to its mouth (0.8 m toward the lane).
    - Shooting: every 1.6 s it winds up as its kind and `FireAt(LeadAim.Point(...) + up 1.2)` at the nearest seated hero (cart or wagon) within 25 m in line of sight. It never targets crew.
    - Done: the controller frees it quietly.
  - **`Enemy.Pinned`** (`bool`) and **`Enemy.FireAt(Vector3 aim)`**.
  - **`RailEncounterController`:**
    - `ArmAlcoves(int ride)` prints `[alcove] ride {r}: {n} of {want} placed at S=[…] ({e} enemy-lane, {p} party-lane)`.
    - `internal IReadOnlyList<Enemy> Alcoves` (test hook).
    - Each shooter prints `[alcove] {name} steps out at S={s:0.0} (front {d:0.0} m)` and `[alcove] {name} gone (the train's back {d:0.0} m past)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void TheTable_IsTheSpecs() => Assert.Equal(new[] { 4, 5, 5, 3, 4 }, Enumerable.Range(1, 5).Select(r => AlcoveRules.For(r).Count));
[Fact] public void Place_EnemyLaneFirst_InTheStretch_EightMetresApart_ThirtyClear() { /* a 200 m ride with enemy and party pockets every 12 m, one busy point at 0.3: the picks are enemy-lane, inside 0.5-0.75 first, ≥ 8 m apart, none within 30 m of the busy point or of either stop; the count is met */ }
[Fact] public void Place_FallsBackToThePartyLane_OnlyWhenTooFew() { /* with two clear enemy pockets and a count of 4: two enemy-lane picks, then party-lane ones */ }
[Fact] public void Place_TakesNoClaimedPocket_AndNoneWhenAllIsBusy() { /* claimed {3}: index 3 never picked; a busy span over the whole ride: an empty list */ }
[Fact] public void Place_IsSeeded() { /* equal twice; another seed shuffles a tier differently */ }
[Fact] public void ThePhases_WakeAtThirtyFive_AndGoThirtyPast()
{ Assert.Equal(AlcovePhase.Hidden, AlcoveRules.Next(AlcovePhase.Hidden, 36f, -40f)); Assert.Equal(AlcovePhase.StepOut, AlcoveRules.Next(AlcovePhase.Hidden, 35f, -40f));
  Assert.Equal(AlcovePhase.Done, AlcoveRules.Next(AlcovePhase.Shooting, -45f, 30f)); Assert.Equal(AlcovePhase.Shooting, AlcoveRules.Next(AlcovePhase.Shooting, -10f, 29f)); }
[Fact] public void LeadAim_StandingTarget_IsTheTarget() => Assert.Equal(new Vector3(8, 0, 0), LeadAim.Point(Vector3.Zero, new Vector3(8, 0, 0), Vector3.Zero, 16f));
[Fact] public void LeadAim_SideOn_LeadsByTheFlight()
{ var p = LeadAim.Point(Vector3.Zero, new Vector3(8, 0, 0), new Vector3(0, 0, 6), 16f); Assert.Equal(8f, p.X, 3); Assert.Equal(6f * 8f / MathF.Sqrt(16 * 16 - 6 * 6), p.Z, 2); }
[Fact] public void LeadAim_TooFast_AimsAtTheTarget() => Assert.Equal(new Vector3(8, 0, 0), LeadAim.Point(Vector3.Zero, new Vector3(8, 0, 0), new Vector3(20, 0, 0), 16f));
```

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~AlcoveRulesTests|FullyQualifiedName~LeadAimTests"`. Expected: a compile error.
- [ ] **Step 3: Implement** the rules, `LeadAim`, `Pinned`/`FireAt`, `AlcoveState`, `ArmAlcoves`, the clear, and the refuge skip.
- [ ] **Step 4: Run the tests.** Run the filter, then the full suite. Expected: PASS.
- [ ] **Step 5: Write `--test-stop-trap=alcove`.**
  - Setup:
    - `rc.Restore(0)`; the lead and a companion are seated in the cart, and a companion in W1.
    - Before the departure the flow claims ride 1's first planned pocket for a seeker (a companion's `SeekSpot` there).
    - The flow's cull spares the shooters. The heroes are topped up.
    - It rides ride 1 to stop 1, then (`rc.Restore(k−1)` for each) rides 2, 4 and 5.
  - It fails unless:
    - **Placement.** Each ride prints its placed line. Ride 1 places 4, none in the claimed pocket. Every placed S is at least 30 m from every busy span and stop. Ride 4's count equals the count measured in this step (record it), and rides 2 and 5 meet theirs, or the flow prints why.
    - **Pinned.** Every shooter stays within 0.3 m of its back or mouth spot for the whole ride.
    - **Waking.** Each steps out with the front 35 ± 2 m away.
    - **Firing.** It fires only within 25 m, at a seated hero, at intervals of 1.6 ± 0.25 s.
    - **Lead aim pays.** At least one bolt over rides 1-2 hits a seated hero side-on (the flow counts hits on seated riders where the bolt's path is more than 45° off the track).
    - **No crew.** No bolt targets a crew member.
    - **Gone.** Each is gone when the back is 30 ± 2 m past it, and none is left after `Arrived`.
    - **Refuge.** A companion put on the enemy lane before a traffic cart never seeks a shooter's pocket.
- [ ] **Step 6: Run** `scripts/flows.sh "--test-chase --quit-after=750" "--test-crew --quit-after=570" "--test-stop-trap=alcove" "--test-ride" "--test-barricade" "--test-runaway=tail" "--test-cavern" "--test-pit" "--test-boarders=groups"`. Expected: every line PASS. The `NoAmbush` flows place no shooter.
- [ ] **Step 7: Run plain `scripts/net_test.sh`** (alone). Expected: `NET TEST: ok`.
- [ ] **Step 8: Commit**

```bash
git add src/Dungeon/Rails/Core/AlcoveRules.cs src/Enemies/Core/LeadAim.cs src/Enemies/Ai/AlcoveState.cs src/Enemies/Enemy.cs src/Dungeon/Rails/RailEncounterController.Alcoves.cs src/Dungeon/Rails/RailEncounterController.cs src/Dungeon/Rails/RailEncounterController.Traffic.cs src/Core/DebugTools.StopTrap.cs src/Core/DebugTools.StopTrap.Alcove.cs
git commit -m "feat(rails): alcove shooters: code-placed per ride (4/5/5/3/4, enemy lane first, 30 m clear of every piece and stop), pinned in their pockets, waking at 35 m, shooting seated riders with lead aim within 25 m, gone 30 m behind; --test-stop-trap=alcove

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: The RIDE pass's hijack steps, the docs, the final perf measurement

**Files:**
- Modify:
  - `src/Net/NetTestFlow.cs`, `scripts/net_test.sh` (the steps, the header, the timings);
  - `src/Dungeon/Rails/RailController.Hijack.cs` (the RIDE server's hooks and traces), `src/Dungeon/Rails/RailNet.Carts.cs` (the client's hijack trace);
  - `docs/development.md`.
  - Only if Step 5 needs it: `src/Adventure/Core/Design/StopSiegeRules.cs` (row 4's cap) and its test.

**Rulings this task needs:**
- **21. The RIDE pass's hijack steps.** Both run at stop 3, before "Joiner boarded again at stop 3":
  - **Won back.** The joiner is on foot beside the cart, put there after its throw. The Rider gets out through its intent (`cart:0`). The server's hook `DebugHijack()` seats a hijacker on the driver spot at once (no siege on the RIDE server). It glows 3 s and rolls back. The joiner presses Board through its intent (`InteractIntent(cart:0)`) while it rolls back. The server lands three blows on the driver for the joiner (`DebugStrikeDriver(member)`, 0.5 s apart, counted as that party member's), and the driver is knocked off. Then the brake, the return and the hold.
  - **Got away.** With both on foot, a second `DebugHijack()` (no 20 s wait for a hook) rolls to the limit and is Gone; the relief comes 12 s later. Both board, the thrown crew are waited for, and the train departs stop 3 with 2 players, so the existing step holds.
  - The budgets grow by 100 s: the server's `--quit-after` 660 → 760 and the clients' 540 → 640, with the header comment's timings updated.
  - Cost: the blows are the server's; the wire carries everything else.
- **2 (this task's part).** The RIDE server has no siege, wall ambush or shooters (Tasks 3, 4, 8). Cost: as stated there.
- **Spec Ruling 5, verbatim.**
  - The target is measured with the windowed perf flow at 1280x720, VSync off, Haze 0.5 and render scale 100 %, on this laptop.
  - If stop 4's siege misses 120 fps after the tick diet, lower its alive cap (12 to 9) before anything else, then record the gap in docs Known gaps.
  - The perf log line is the real answer to the owner's "constant low": it shows on their next session whether the cost is enemies, the GPU or something else.

**Interfaces:**
- Consumes: Tasks 5-7's phases, `TakeDriver`, the grip, `MayBoard`, Gone, the relief, the thrown wait; the RIDE pass's stop-3 sequence and ride books.
- Produces:
  - **The server's hooks** (`TestServer && TestRide` only, as `DebugWreck`):
    - `internal Enemy? DebugHijack()` (an `enemy.horde_cinder` made the driver now);
    - `internal void DebugStrikeDriver(int member)` (one melee blow counted as that member's).
  - **The traces.** Every 0.5 s while `Hijack != None`, the server prints `[net-server] ride: hijack {phase} S={s:0.00} t={t:0.000} wagons={W1 S,…} facing=+1`, and each client prints `[net] ride: hijack {phase} here S={s:0.00} t={serverNow:0.000} wagons={…} facing={+1|-1}`.
  - **The checks**, each with its own `NET TEST: FAIL (…)`:
    - **The joiner boards the rolling train.** `[net-server] ride: Joiner boarded the rolling-back cart through the intent (V={v:0.0})`, with V ≤ 4.05. The joiner prints `[net] ride: boarded the rolling-back cart here`.
    - **The driver on the wire.** The joiner sees the driver at the driver spot (`[net] ride: the driver {id} aboard the cart here`) and sees it gone within 1 s of the server's knock-off line.
    - **The return.** Both peers print `hijack over at stop 3` / `[net] ride: held again at stop 3 here`.
    - **Got away.** The cart is hidden on both clients within 1 s of the server's Gone. The relief is held at `StopS[3]` on both within 1 s of the server, and the thrown crew booked and seated before the departure.
    - **Agreement.** Pairing the traces by t (nearest 0.25 s): at every pair, |ΔS| ≤ 1.5, each Wk's S below the cart's on both peers, and `facing=+1` on the client for every RollingBack and Braking trace. An awk check prints `--- hijack pairs: {n} pair(s), worst {d:0.00} m` and fails under 8 pairs or over 1.5 m.

- [ ] **Step 1: Add the hooks, traces and checks**, in ride order, plus the header comment and budgets.
- [ ] **Step 2: Run plain `scripts/net_test.sh`** (alone, lock absent). Expected: `NET TEST: ok`.
- [ ] **Step 3: Hand the RIDE pass to the controller.** Stop and report the new lines expected.
  - The controller runs `RIDE=1 scripts/net_test.sh` alone, twice, under `.superpowers/sdd/shared/RIDE.lock`, on a profile other than `low-power`.
  - Expected each time: `NET TEST: ok`, with every line above, every older line, and `wipe-rise watch: ok`.
  - A failure is investigated from its kept logs (`KEEP_TMP=1`), never rerun to green.
- [ ] **Step 4: Run the final perf measurement** (machine idle).
  - Run `dotnet build DungeonCrawler.csproj && XDG_DATA_HOME=$(mktemp -d) timeout 300 godot --path . --print-fps --resolution 1280x720 -- --test-perf=siege 2>&1 | tee /tmp/p16-perf-siege.txt | grep "^\[perf\]"`, then the same with `--test-perf=ride` (ride 1 now has its 4 shooters) and `--test-perf=crowd`.
  - Expected: `[perf] RESULT ok` for each.
- [ ] **Step 5: Judge the 120 fps target** (spec Ruling 5).
  - If the siege's median holds 120 or more: record it and go on.
  - Otherwise set `StopSiegeRules.For(4).AliveCap` to 9. Update `TheTable_IsTheSpecs` (old 12, new 9, both named in the commit) and run the full suite, `--test-stop-trap=siege` and Step 4's `=siege` again.
  - If it still misses, record the gap (the measured median, worst second, physics ms and enemies/awake/asleep) in Known gaps. Change nothing else.
- [ ] **Step 6: Write the docs** in `docs/development.md`:
  - **Frame rate**: the final rows (siege, ride with shooters, crowd), the target, its outcome, and the fallback applied or not.
  - **Stops, cages and the crew**, a new "The stop trap" subsection:
    - the siege's table, co-op, heavy, sites, roster, the floor-wide pause, the restart graces, stragglers, lines, and the RIDE server's silence;
    - the wall ambushers (counts, placement, spring, once ever, `SprungAmbushes` indices after the corridor ones);
    - the hijack (conditions, pacing, runners, driver spot, glow, the reverse, boarding, the grip, the return, the limit per stop as pinned, the throws, Gone, the relief, the thrown crew's wait, saves).
  - **The five rides**: the alcove shooters (the table, the busy spans, the tiers, the placed counts per ride as measured, the state, lead aim).
  - **Riding together**: protocol 18, `CartStateDto.Hijack`, `Reversing`/`Facing`, `DriveAccel`, the driver as `Boarders[2]`, and the RIDE pass's new steps with the controller's measured numbers.
  - **Known gaps**: the siege not seen over the wire by the RIDE pass; the driver's blows dealt by the server's hook; ride 4's shooters if under 3; a shooter glimpsed in its niche; a far guard's three-tick wake; the perf gap if any.
  - **Flows**: the `--test-stop-trap` variants and `--test-perf` (windowed).
  - Every number with where it came from.
- [ ] **Step 7: Run the full sweep.**
  - The full suite.
  - Through `scripts/flows.sh`, longest first:
    - `--test-chase --quit-after=750`; `--test-crew --quit-after=570`; `--test-crew=forgiven --quit-after=570`;
    - every `--test-stop-trap` variant (siege, ambush, roll, wonback, gotaway, limits, alcove);
    - `--test-island` and its nine other variants; `--test-pit` and its four other variants;
    - `--test-boarders` and its six other variants;
    - `--test-cavern` and its nine other variants;
    - `--test-runaway=tail`, `=ram`; `--test-split`, `=early`, `=hold`, `=stall`, `=lone`;
    - `--test-switch-back` and its four other variants;
    - `--test-crevasse`, `=trestle`, `=survivor`;
    - `--test-relief`, `=rails`, `=refuse`, `=ahead`;
    - `--test-barricade`, `--test-ride`, `--test-ride-edges`, `--test-cart-watchdog`, `--test-track-view`, `--test-vault-puzzle`, `--test-tick-rate`.
  - Then plain `scripts/net_test.sh`, alone.
  - Expected: every one PASS, and `NET TEST: ok`.
- [ ] **Step 8: Commit** (after the controller's two RIDE passes)

```bash
git add src/Net/NetTestFlow.cs scripts/net_test.sh src/Dungeon/Rails/RailController.Hijack.cs src/Dungeon/Rails/RailNet.Carts.cs docs/development.md
git commit -m "test(net): the RIDE pass's hijack steps at stop 3 (the joiner boards the rolling-back cart and the driver is knocked off; got away with the relief train; both peers within 1.5 m); docs: the stop trap, the alcove shooters, protocol 18, the frame rate against the 120 fps target

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

  (Add `src/Adventure/Core/Design/StopSiegeRules.cs` if Step 5 lowered the cap.)
