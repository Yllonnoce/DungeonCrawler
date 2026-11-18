# Mine Cart Chase, Plan 5: online co-op, VR comfort and the tick-rate fix

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Floor 12's chase plays the same for every player in online co-op. Every player sees the same:
- carts, seats and slag carts with their riders;
- boarding hounds, gates, cages and switches;
- set pieces and crew.

Remote players can board, pump, brake and get out. They can use every prop and take a spare cart when they are left behind. A VR rider is comfortable. The hazard and set-piece timing no longer drifts when a headset runs physics at 72–120 Hz.

**Architecture:** The server keeps all authority. Two clocks reach clients:
- **Server clock.** `NetClock` learns the server's time from the existing Ping/Pong.
- **Floor clock.** The server publishes `(FloorSeconds, ServerTime)` pairs, and clients extrapolate from the latest pair.

Every timed thing is sent in server seconds: hazard phases, set-piece metronomes, bucket schedules and cart states.

How each part syncs:
- **Set pieces.** Clients build them as visual-only copies. Server state reaches those copies as `PropEvt` and `PropStateDto`.
- **Carts.** They sync as `CartStateMsg` events plus a transient 5 Hz `CartCorrectionMsg`. Clients run the same `CartPhysics` integrator in a `CartFollower` and close drift smoothly.
- **Seats.** They sync as `SeatMsg`. A seated rider's pose becomes seat-relative.
- **Boarders.** Boarding hounds sync as boarder lists plus `LeapMsg`.
- **Remote players.** They act through `NetRefs` kinds and a `RideIntent`.
- **VR.** A head-locked comfort vignette and a seated eye height.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (tests/ is local, not in git), the game's own WebSocket protocol (`src/Net`).

**Spec:** `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md`, sections:
- "Co-op and saving";
- "Feel" (VR);
- "Anti-goals";
- "Calls made after the builder check" (the spare cart for a co-op player left behind).

**Builds on:**
- Plans 1–4: rails, the cart, seats, run-over, stops, gates, wagons, crew, spares, the enemy line, traffic, the set pieces and `CartEvents`.
- Code map: `.superpowers/sdd/shared/plan5-code-map.md`.
- This plan's review and rulings: `.superpowers/sdd/shared/plan5-review.md`.
- Earlier rulings: `.superpowers/sdd/mine-cart-rulings.md`.

## Global Constraints

- **Single player behaves exactly as today at 60 Hz.**
  - `--test-ride`, `--test-ride-edges`, `--test-crew` (both branches) and `--test-chase` print RESULT ok after every task.
  - `FloorTick` gives the same sequence at 60 Hz as today.
- **The protocol becomes version 17 in Task 1, once.** Later tasks add kinds without bumping the version again. New kinds take these numbers, in this order:

  | Kind | Number | Task |
  |---|---|---|
  | `FloorClock` | 7 | 1 |
  | `CartState` | 8 | 3 |
  | `CartCorrection` | 9 | 3 |
  | `Seat` | 13 | 4 |
  | `Leap` | 14 | 4 |
  | `RideIntent` | 15 | 5 |

  `MessageKindCount` goes 108 → 109 (T1) → 111 (T3) → 113 (T4) → 114 (T5). The pins in `tests/Net/NetCodecTests.cs` (:248-252, :439) follow each change.
- **The server stays authoritative.** Clients never change gameplay state: they pose, animate and ask through intents. The server validates every intent: reach (3.5 m, as `InteractIntent`), seat ownership, and Held or Rolling where it matters.
- **The wire stays engine-free.** New records and DTOs live in `src/Net/Core`, and engine-free rules in `src/Dungeon/Rails/Core` or `src/Party/Core`. All of them use `NetVec` or plain numbers, so the test project compiles them (csproj lines 30, 34, 35).
- **Join queue.** A message a joiner must not replay gets its kind added to the `ServerWorld.Transient` predicate (`ServerWorld.cs:728`). State a joiner needs comes from `SendRailCatchUp` (Task 8).
- **Cadences run on elapsed time, never on `tick % n`**, on both server and client. This keeps a process at any physics rate on time.
- **No per-frame allocation** in the client cart, follower, seat and comfort paths. Decoding messages is exempt.
- **VR never shakes, rolls or forces pitch on the headset camera.** Comfort defaults to Recommended.
- **Repo rules.**
  - `tests/` is not in git.
  - Build (`dotnet build`) before any headless run; flags go after `--`.
  - No installers or dist builds.
  - No `pkill -f` of your own pattern; use `--quit-when-empty` or `--quit-after`.

## Review Focus

1. **A joiner mid-ride sees the right state within 1 s:** cart, seats, train, slag carts and riders, boarders, gates, props and crew. Test: RIDE pass, second client (Task 8).
2. **No rubber-banding.**
   - A client cart never visibly snaps backwards in normal play.
   - Under ±150 ms of arrival jitter, the rendered S never decreases while V ≥ 0.5 m/s.
   - Tests: `CartFollowerTests`, and the RIDE pass's S comparison (|ΔS| ≤ 1.5 m on ≥ 20 paired samples).
3. **A remote player can do everything a host player can:**
   - board and get out, including being thrown at speed;
   - pump and brake;
   - open cages, crank winches, throw switches and take the orc's keys;
   - break brake shoes, the heap and the valve;
   - revive crew;
   - take a stop's spare cart when left behind.

   Test: RIDE pass steps.
4. **Timing at 90 Hz matches 60 Hz.**
   - A hammer cycle and a periodic spike bed each last 4.5 s ±0.1 s of wall time at both rates.
   - A client's floor clock is within 50 ms of the server's after a 400 ms server stall, by the next pair.
   - Tests: `NetClockTests`, `FloorClockTests`, `HazardSyncTests`, `--test-tick-rate` at 60 and 90.
5. **Riding in a headset is comfortable.**
   - The vignette tightens only with acceleration and turning.
   - It never goes full black; Strong fades 60% for 0.4 s on a big drop.
   - The cart rim stays in view.
   - Snap turn works while seated.

   Test: `ComfortRulesTests`.

---

### Task 0: Single-player carry-overs from plan 4

The plan 4 fix wave found two single-player gaps; the controller ruled them into this plan. They touch no wire.

**Files:**
- Modify:
  - the companion and seated-hero target selection (`src/Party/Adventurer*.cs` and the companion AI that picks a target while seated);
  - `src/Dungeon/Rails/RailEncounterController.Leaps.cs` and `RailEncounterController.Kiln.cs` (Capture/Restore);
  - `src/Save/Core/SaveDtos.cs`;
  - `src/Core/DebugTools.Chase.cs`.

**Interfaces:**
- **Boarders on wagon roofs are targets.** A hound with `BoardTarget` set, on a coupled wagon's roof, counts as a valid target for seated companions and companions on foot. It is reachable when within their attack range of the seat; ranged attackers need sight.
  - Seated melee companions reach the first wagon's roof from the cart's rear seats. The roof spots sit about 2.4 m behind the cart, so measure and pick the reach that makes that true.
  - Companions prefer a boarder that is biting crew over one that is only riding.
- **The thrower's sent-once memory is saved.** The flag that makes a thrower's first spawn a full enemy and every later spawn a reinforcement moves into the encounter's Capture/Restore, as `FloorStateDto.ThrowersSent : List<string>?` (null reads as empty).

- [ ] **Step 1: Chase test.** On ride 2, with an idle lead at lethality 3, a hound that boards a wagon is attacked by a companion within 6 s. Trace who kills it. The test asserts no crewman goes down from a wagon boarder on ride 2.
- [ ] **Step 2: Save test.** A save taken after the ride-1 thrower is sent, then loaded, gives back a thrower that is a reinforcement.
- [ ] **Step 2b: One-pumper jump.** A second `--test-chase` jump pass forces exactly one pumper with three coupled wagons and asserts the jump is made. This proves `JumpSpeed` 6.1 at runtime, including the slope at the lip; the plan 4 re-review asked for it. If it fails, lower the lip slope or `JumpSpeed`, never below the unpumped speed of 6.0.
- [ ] **Step 3: Implement**, then build and run the full suite and every SP ride test (`--test-ride`, `--test-ride-edges`, `--test-crew` both branches, and `--test-chase` twice).
- [ ] **Step 4: Commit** `fix(rails): companions fight hounds on the wagons; a sent thrower stays sent after a load`.

### Task 1: The server clock and the floor clock on clients; rate-safe time everywhere

**Files:**
- Create:
  - `src/Net/Core/NetClock.cs`;
  - `src/Net/Core/FloorClock.cs`.
- Modify:
  - `src/Net/Core/Protocol.cs`: version 17, kind `FloorClock = 7`, count 109;
  - `src/Net/Core/Messages.cs`: Welcome, FloorChange, HazardPhaseMsg, the new `FloorClockMsg`;
  - `src/Net/Core/HazardSync.cs`;
  - `src/Net/ClientWorld.cs`;
  - `src/Net/ServerWorld.cs`;
  - `src/Net/ServerReadyCheck.cs` (:64);
  - `src/Dungeon/DungeonBuilder.cs` (:208-209, :338);
  - `src/Core/DebugTools.cs`: `--physics-hz=N` and `--test-tick-rate`.
- Test:
  - `tests/Net/NetClockTests.cs`;
  - `tests/Net/FloorClockTests.cs`;
  - `tests/Net/HazardSyncTests.cs` (rewrite for seconds);
  - `tests/Net/NetCodecTests.cs`.

**Interfaces:**
- **`public sealed class NetClock`** (engine-free):
  - `void Seed(double serverTime, double clientNow)`: one sample with no RTT.
  - `void OnPong(double clientSentAt, double clientNow, double serverTime)`:
    - each sample's offset is `serverTime − (clientSentAt + clientNow) / 2` and its RTT is `clientNow − clientSentAt`;
    - the clock uses the median offset of the last 9 samples whose RTT is ≤ 2 × the median RTT.
  - `double ServerNow(double clientNow)`, `double Rtt` (median), `bool Ready` (true after Seed or one sample).
  - `ClientWorld.Rtt` reads `NetClock.Rtt`.
- **Wire changes:**
  - `Welcome` gains trailing `double ServerTime = 0, double FloorSeconds = 0`. The client seeds `NetClock` from `ServerTime` and the floor clock from the pair. `Welcome.ServerTick` stays.
  - `FloorChange` gains trailing `double ServerTime = 0, double FloorSeconds = 0`.
  - `[Msg(MsgKind.FloorClock)] FloorClockMsg(int Floor, int Epoch, double FloorSeconds, double ServerTime)` is new:
    - the server broadcasts it every 1.0 s of elapsed time on a railed floor and every 2.0 s elsewhere, and sends it to a peer in `OnFloorReady` and in the join catch-up;
    - its kind is added to `Transient`;
    - clients drop one whose Floor or Epoch is not theirs (`WorldCheck.SameBuild`).
- **The floor clock:**
  - **Server and single player.** `DungeonBuilder.FloorSeconds` (double) sums the `delta` passed to `DungeonBuilder._PhysicsProcess`: 1/60 s per tick at 60 Hz, 1/90 s at 90 Hz. It resets in `Clear()`.
  - **Server pairs** are `(FloorSeconds, NetSession.Now)`.
  - **Clients:**
    - `FloorSeconds` is `FloorClock.Read(NetClock.ServerNow(NetSession.Now))`;
    - the clock is fed by every pair (Welcome, FloorChange, FloorClockMsg);
    - before the first pair it reads 0.
- **`public sealed class FloorClock`** (engine-free):
  - `void Apply(double floorSeconds, double serverTime)`.
  - `double Read(double serverNow)`:
    - The target is `pair.FloorSeconds + (serverNow − pair.ServerTime)`.
    - The read value advances by the elapsed server time × a rate in [0.9, 1.1] toward the target.
    - It snaps to the target when |target − read| > 0.25 s.
    - It never decreases except on a snap.
    - Calling it twice at the same `serverNow` returns the same value.
  - `static int TickOf(double seconds) => (int)Math.Floor(seconds * 60 + 1e-6)`.
  - `DungeonBuilder.FloorTick` becomes `FloorClock.TickOf(FloorSeconds)` on every role. SpikeBed, DropHammer, MoltenPour and the Kiln stay on `FloorTick` and become rate-safe and client-visible.
- **`HazardPhaseMsg(int Index, byte Hazard, byte Phase, double ServerTime, float DurationSeconds, bool Jammed, int Spent)`.**
  - `ServerTime` replaces `ServerTick`, and `DurationSeconds` replaces `DurationTicks`.
  - `HazardSync.DurationSeconds(float s)` returns 0 for a non-finite or ≤ 0 duration.
  - `HazardSync.SecondsIn(double serverNow, double sentServerTime, float durationSeconds)` is `max(0, serverNow − sent)`, clamped to the duration when the duration is > 0.
  - Drop `_serverTickOffset` (`ClientWorld.cs:190, 565`).
- **Cadences by elapsed time.** Each is an accumulator that keeps its remainder, so at 60 Hz it fires on the same ticks as today.
  - Server snapshot every 1/20 s, vitals every 1/10 s, `FlushSync` every 0.1 s, `ObjectiveStateMsg` every 2.0 s (`ServerWorld.cs:766-769`).
  - The ready-check broadcast every 1.0 s (`ServerReadyCheck.cs:64`).
  - The client pose send every 1/30 s, with the idle heartbeat every 0.5 s (`ClientWorld.cs:627-645`).
  - The client ping every 1.0 s (`ClientWorld.cs:647`).
- **Debug flags** (committed):
  - `--physics-hz=N` sets `Engine.PhysicsTicksPerSecond`.
  - `--test-tick-rate`:
    - waits on floor 1 and samples `SetPieceRules.Phase(builder.FloorTick, 0, …)` and `HazardRules.PeriodicPhaseAt(builder.FloorTick, 0, …)`;
    - measures the wall time (`Time.GetTicksMsec`) between two entries into `HammerPhase.Fall` and between two periodic cycle starts;
    - prints `[tick-rate] RESULT ok hammer=<s> spikes=<s>` when both are 4.5 ±0.1 s, else `RESULT fail`.

- [ ] **Step 1: Write the tests.** RED, then GREEN.
  - `NetClock`:
    - symmetric 80 ms RTT samples around a fixed offset converge to within 1 ms after 9 samples;
    - one sample with a 1 s RTT moves the offset by less than 1 ms;
    - samples taken at 60 Hz and at 90 Hz cadences give ServerNow values within 1 ms of each other.
  - `FloorClock`:
    - with no stall, Read equals server FloorSeconds within 1 ms over 60 s;
    - a pair 0.4 s behind (a server stall) snaps at Apply, so Read is within 50 ms right after it;
    - a 0.1 s error closes to within 10 ms after 1.5 s, and Read never decreases while it does;
    - `TickOf` over 600 steps of 1/60 gives exactly 600; over 900 steps of 1/90 it gives exactly 600; each step is non-decreasing.
  - `HazardSync`:
    - elapsed time is clamped to the duration;
    - an open-ended phase (duration 0) grows;
    - an infinite duration maps to 0.
  - Codec: round trips for `FloorClockMsg`, the new Welcome, FloorChange and HazardPhaseMsg fields; pins at version 17 and count 109.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.**
  - `dotnet build`.
  - `--test-tick-rate` at the default rate, then with `--physics-hz=90`: both print RESULT ok.
  - The full suite, `scripts/net_test.sh` (it runs a press hazard) and `START_FLOOR=0`.
  - The four SP ride tests.
- [ ] **Step 4: Commit** `fix(net): clients keep the server's clock and the floor's; hazards and set pieces run on time at any rate`.

### Task 2: Set pieces on clients as visual copies; rail props on the wire; the RIDE net test

**Files:**
- Modify:
  - `src/Dungeon/Rails/RailEncounterController.cs` (:82) and its partials (`SetPieces`, `Switchyard`, `Kiln`): build on every role, add `static Instance`, raise PropEvts;
  - `src/Dungeon/Rails/RailGate.cs`, `src/Dungeon/Rails/StopGates.cs`, `src/Dungeon/Rails/SpareYard.cs`;
  - `src/Dungeon/Rails/SetPieces/*.cs`: client `Show*` methods;
  - `src/Adventure/Objectives/CrewRescueController.cs`: PropEvts for cages and keys;
  - `src/Net/Core/NetRefs.cs`, `src/Net/NetRefResolver.cs`;
  - `src/Net/Core/PropCheck.cs`: `PropStateDto.Rail`;
  - `src/Net/PropSync.cs`;
  - `src/Net/ClientWorld.cs` (`OnProp`);
  - `src/Net/NetTestFlow.cs`, `scripts/net_test.sh`: the `RIDE=1` pass.
- Test:
  - `tests/Net/NetRefsTests.cs`;
  - `tests/Net/PropCheckTests.cs`.

**Interfaces:**
- **Clients build the set pieces.** `BuildSetPieces()` runs on every role, including `CutGaps`. On clients the copies are visual only:
  - every `Tick*`, `Arm*`, `Clear*`, judge and damage path stays server-only (existing guards: `RailEncounterController.cs:98, 239, 369`; `OreHeap.cs:92`; `MoltenPour.cs:159`; `TrackSwitch.cs:77`);
  - collision layers stay as on the server, so walkers collide with posts, heaps and buffers alike everywhere.
- **Piece index.** A piece's index is its plan's index in `FloorContents.RailSetPieces`, which is the same on every peer.
- **New `NetRefs` kinds, all letters only:**

  | Ref | Names |
  |---|---|
  | `gate:{stop}` | a stop's gate |
  | `winch:{stop}` | a stop's winch |
  | `wagon:{stop}` | a stop's cage wagon |
  | `spare:{stop}` | a stop's spare cart |
  | `switch:{piece}` | a track switch |
  | `spur:{piece}` | a spur switch |
  | `heap:{piece}` | an ore heap |
  | `valve:{piece}` | the quench valve |
  | `trestle:{piece}` | a trestle |
  | `buffer:{piece}` | a buffer stop |
  | `keyring:0` | the orc's keyring |
  | `cart:0` | the party cart |

  - `NetRefResolver.Resolve` finds them through `RailController.Instance`, `RailEncounterController.Instance` and `ObjectiveController.Instance` (CrewRescue), not by scanning direct children.
  - `WinchPost` becomes `internal`.
- **PropEvt (the existing `EventBus.RaisePropChanged` → `PropEvt` path).** The server raises one at every state change, with these values of `What`:

  | Ref | Values |
  |---|---|
  | `gate` | "up" |
  | `switch`, `spur` | "thrown", "straight" (a player set it back), "reset" (an end-of-ride reset; silent on clients) |
  | `heap` | "cleared", "rebuilt" |
  | `valve` | "broken", "rebuilt" |
  | `trestle` | "collapsed", "rebuilt" |
  | `buffer` | "smashed", "restored" |
  | `spare` | "used", "restored" |
  | `wagon` | "opened" |
  | `keyring` | "taken" |

  - `ClientWorld.OnProp` gains a case per pair, calling a silent client method (`ShowUp`, `ShowThrown`, `ShowCleared`, …).
  - The collapse, smash and burst play their cosmetic debris on clients too.
- **`PropStateDto.Rail : RailPropsDto?`** (null off railed floors), with these lists:
  - `int` lists: `GatesUp`, `ThrownSwitches`, `ThrownSpurs`, `ClearedHeaps`, `BrokenValves`, `CollapsedTrestles`, `SmashedBuffers`, `SparesUsed`, `OpenCages`;
  - `bool KeysTaken`.
  - `PropSync.Capture` and `ApplyLive` handle it.
  - `PropCheck.Signature` appends the rail part only when `Rail != null`, so every existing hash is unchanged.
  - `PropCheck.Differences` lists it.
  - Joiners get it in the existing `SendPropState`. The 4 s prop check repairs drift.
  - `FloorStateDto` and the save are unchanged.
- **`RIDE=1 scripts/net_test.sh`:**
  - **Server:** `--test-net-server --campaign=builtin --seed=4242 --lethality=1 --quit-when-empty`. Its NetTestFlow calls `Game.Instance.JumpToFloor(12)` once a client is Ready.
  - **One client.**
  - **Steps this task adds:**
    - the client has `Beam_*`, `Switch_*`, `Hammer*` and `Pour*` nodes;
    - its prop hash equals the server's (no `prop-check` trace within 10 s).
  - The script greps `[net] RESULT ok`, `[net-server] RESULT ok` and the marker lines. The default and `START_FLOOR=0` passes stay as they are.

- [ ] **Step 1: Tests.**
  - `NetRefs` round trips for each new kind.
  - `PropCheck`: an empty `Rail` changes the hash; `Rail == null` keeps every existing pinned hash; `Differences` names a thrown switch.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.**
  - Build.
  - The full suite.
  - `scripts/net_test.sh`, `START_FLOOR=0`, and `RIDE=1` (each prints `NET TEST: ok`).
  - The SP ride tests.
- [ ] **Step 4: Commit** `feat(net): clients build the set pieces and see their state`.

### Task 3: Cart state on the wire; the follower; client posing, HUD and train

**Files:**
- Create:
  - `src/Net/Core/CartNet.cs`: `CartKind`, `CartIds`, `CartFlags`, `CartStateDto`, `CartStateMsg`, `CartCorrectionMsg`;
  - `src/Dungeon/Rails/Core/CartFollower.cs` (engine-free);
  - `src/Dungeon/Rails/RailNet.cs`: the server sender node and the client applier.
- Modify:
  - `src/Net/Core/Protocol.cs`: kinds 8 and 9, count 111;
  - `src/Net/ServerWorld.cs`: `Transient` gains `CartCorrection`;
  - `src/Net/ClientWorld.cs`;
  - `src/Dungeon/Rails/RailController.cs`, `RailController.Lanes.cs`, `RailController.Jump.cs`: client apply methods, client posing and the HUD;
  - `src/Dungeon/Rails/CartTrain.cs`;
  - `src/Dungeon/Rails/RailEncounterController*.cs`: cart ids and dirty marks;
  - `src/Dungeon/Rails/SlagCart.cs`, `src/Dungeon/Rails/RunawayWagon.cs`: client construction;
  - `src/Enemies/Enemy.cs`: client carried mode;
  - `src/Net/NetTestFlow.cs`, `scripts/net_test.sh`.
- Test:
  - `tests/Net/CartNetTests.cs`;
  - `tests/Dungeon/Rails/CartFollowerTests.cs`.

**Interfaces:**
- **Ids and kinds:**
  - `CartKind : byte { Party, Slag, Traffic, Runaway, Golem, Chaser }`.
  - `CartIds`: `Party = 0`; slag, traffic, runaway and golem cart ids count up from 1 per floor build (server-assigned, < 1000); `WagonBase = 1000` (+ stop); `ChaserBase = 2000` (+ stop).
- **`CartStateDto`:**
  - identity: `int Cart`, `byte Kind`, `byte Track` (0 party line, 1 enemy line, 2+k the k-th spur), `sbyte Dir`;
  - motion: `float S, V`, `byte Mode` (a `CartMode`), `float Limit`;
  - the integrator's input: `byte Pumpers`, `bool Brake`, `byte CrewWagons`, `float TargetOverride`;
  - condition: `float Plating`, `int StopIndex`;
  - `byte Flags` (`CartFlags`): Halted 1, Wrecked 2, Hidden 4, LineBlocked 8, GateClear 16, Derailed 32;
  - **party only:**
    - `float CrossOut, CrossBack, GapFrom, GapTo, JumpV` (NaN = none);
    - `float BoardingLeft` (−1 = no countdown);
    - `List<int>? Train` (coupled wagon stops, nearest first);
  - **slag and traffic:** `float Offset`, `string? EncounterId`;
  - **slag, traffic and golem:** `List<int>? RiderNetIds` (carried enemies by spot);
  - `double ServerTime`.
- **Messages:**
  - `[Msg(MsgKind.CartState)] CartStateMsg(int Floor, int Epoch, List<CartStateDto> Carts)` carries the carts that changed. It is not Transient.
  - `[Msg(MsgKind.CartCorrection)] CartCorrectionMsg(int Floor, int Epoch, List<CartStateDto> Carts)` carries every live cart. It is Transient. Any live cart missing from it is freed on the client.
  - Clients drop both for another Floor or Epoch.
- **When they are sent.** `RailNet` (a server node, `ProcessPriority = 100`, so it runs after the rail controllers) sends one `CartStateMsg` per physics tick that marked any cart dirty. A cart is marked dirty by:
  - every `CartEvent`;
  - a lane, gap, train, halt or boarding change;
  - a slag, traffic or runaway cart's spawn, derail or free;
  - the golem's spawn, a spur taken, or the runner done.

  It never sends from inside a `CartEvents` handler, so the wagons coupled by `CrewRescueController`'s `Departed` handler are already in `Train`. It also sends a `CartCorrectionMsg` every 0.2 s of elapsed time while any cart is live.
- **`CartFollower`** (engine-free, no allocation):
  - `void Apply(in CartStateDto s, double serverNow)` takes the authoritative state. It steps a copy with `CartPhysics.Step` from `s.ServerTime` to `serverNow` (fixed 1/60 s steps, at most 30), using `s`'s input and Limit.
  - `float Advance(float dt)` returns the rendered S:
    - The authoritative copy steps on by `dt` with `CartPhysics.Step` (through a `CartClock`) and is clamped to Limit in Dir.
    - The rendered speed is `V + clamp(2·e, −c, +c)`, where `e` is the authoritative S minus the rendered S and `c = max(0.15·V, 0.5 m/s)`.
    - The rendered S never moves against Dir while V ≥ 0.5 m/s. Below that it may ease back at up to 0.5 m/s.
    - It snaps to the authoritative S when |e| > 3 m, when Mode becomes Held or Wrecked, or when Track changes. Held snaps to the DTO's S: a Halt sits short of any stop.
  - `float V` (rendered speed), `CartMode Mode`, `byte Track`.
- **Client `RailController`:**
  - The party cart follows a follower. Each rendered frame it sets `Party.State` from the follower, calls `PoseAt(rendered S)` and `_train.Pose(s)`, and calls `FollowSeat` on every rider.
  - It sets `Cart.Speed = follower.V` and `Cart.Curvature = Dir·Track.Curvature(s)`, which feeds `RideFeel` and `Velocity`.
  - Client-only methods apply the synced fields, bypassing the IsClient guards at `RailController.cs:88`, `Lanes.cs:82` and `Jump.cs:40`, and set no server state:
    - `ApplyLaneNet(crossOut, crossBack)`;
    - `ApplyGapNet(from, to, jumpV)`;
    - `ApplyTrain(List<int> stops)`, which couples and uncouples wagons through `CartTrain`.
  - `RefreshHud` runs on clients, using `Plating`, `Mode`, `StopIndex` and the flags, and showing "departs in N" from `BoardingLeft`.
  - The Wrecked flag's rising edge plays `Tumble()`, visual only.
    - **A spare taking over in the same tick.** The server still sends one `CartStateMsg` carrying Wrecked (from the `Wrecked` CartEvent) before the takeover state, so clients always see the wreck.
    - **Test:** a codec/sender unit test, or a RIDE net step, proves this. Hidden hides the cart and its coupled wagons; clearing it shows them again.
- **Client slag, traffic, runaway and golem carts.**
  - They are built from the DTO with the server's constructors, as dressing. The client never runs a run-over sweep, a pace or a warning (the server-only Tick).
  - Each follows its own follower on its own track.
  - The Derailed flag plays the slag cart's tumble before the node is freed.
  - The brake shoe keeps its layer, so client hit effects line up. Its `TakeHit` still returns Miss on clients (`SlagCart.cs:306`).
- **Carried enemies on clients.**
  - `RiderNetIds` are bound lazily: by NetId on every DTO applied and on every `EnemySpawned`. The cart message may arrive before the batched spawn (`ServerWorld.cs:770-774`).
  - A bound rider is `Carry`'d at its spot on the client's own cart. While carried, `Enemy.ClientTick` skips snapshot positions.
- **RIDE pass, the steps this task adds:**
  - A server test hook, `RailController.DebugDepart()` (internal; `--test-net-server` only), departs the cart empty for this pass. Task 5 replaces it with a real boarding.
  - The server prints `[net-server] ride t=<Now> S=<S>` every 0.5 s, and the client prints `[net] ride t=<ServerNow> S=<rendered S>`.
  - The script pairs lines whose t values are within 50 ms. It needs ≥ 20 pairs, all with |ΔS| ≤ 1.5 m.
  - On ride 3, a slag cart is on the client within 2 m of the server's (paired the same way).

- [ ] **Step 1: Tests.**
  - Codec round trips for `CartStateMsg` and `CartCorrectionMsg` (NaN fields survive). Pins at count 111.
  - `CartFollower`:
    - holds the exact track at constant V;
    - a correction 0.5 m late is absorbed within 1 s with no backwards step;
    - snaps on a 5 m error;
    - Held snaps to the DTO's S, including a Halt 4 m short of the stop;
    - with ±150 ms arrival jitter at 6 m/s over 30 s, S never decreases;
    - a cart 1 m behind at V = 0 closes within 2 s;
    - extrapolation never passes Limit;
    - a braking cart (Brake = true) is predicted within 0.1 m of the server after 0.2 s;
    - a Track change snaps.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.** Build; the full suite; the three net_test passes; the SP ride tests.
- [ ] **Step 4: Commit** `feat(net): carts ride the same on every client`.

### Task 4: Seats, seat-relative poses, boarders and leaps

**Files:**
- Modify:
  - `src/Net/Core/Protocol.cs`: kinds 13 and 14, count 113;
  - `src/Net/Core/CartNet.cs`: `SeatMsg`, `LeapMsg`, boarder fields;
  - `src/Net/Core/Messages.cs`, `src/Net/Core/Snapshot.cs`: `Pose`, `PlayerPoseState`, `PoseFlags2`, `NpcState.FlagSeated`;
  - `src/Net/Core/PoseBuffer.cs`;
  - `src/Party/Control/Control.cs` (`RemotePose`);
  - `src/Dungeon/Rails/Core/CartSeats.cs` (`Put`);
  - `src/Net/ServerWorld.cs`: `ApplyPose`, snapshots, `AttackIntent`;
  - `src/Net/ClientWorld.cs`;
  - `src/Party/Adventurer.cs`;
  - `src/Adventure/Npc.cs`;
  - `src/Enemies/Enemy.cs`;
  - `src/Dungeon/Rails/RailNet.cs`, `src/Dungeon/Rails/RailController.cs`, `src/Dungeon/Rails/RailEncounterController.Leaps.cs`, `src/Dungeon/Rails/CageWagon.cs`.
- Test:
  - `tests/Net/NetCodecTests.cs`;
  - `tests/Net/PoseBufferTests.cs`;
  - `tests/Dungeon/Rails/CartSeatsTests.cs`.

**Interfaces:**
- **`[Msg(MsgKind.Seat)] SeatMsg(int RiderKind, string RiderId, int Cart, int Seat, bool Seated, float X, float Y, float Z)`.**
  - `RiderKind` is 0 Hero (`RiderId` = member index) or 1 Npc (`RiderId` = `Npc.Id`).
  - `Cart` is a `CartIds` id: `WagonBase + stop` for a cage wagon.
  - `X, Y, Z` is the landing spot when `Seated` is false.
  - `RailNet` sends one for every seat and unseat: the party cart (`OnRiderChanged`), each cage wagon's `RiderChanged` (crew) and the chaser (Task 9). It sends them after the tick's `CartStateMsg`.
- **On the client:**
  - `CartSeats.Put(int occupant, int seat)` books the server's seat index. It returns false if the seat is taken by another occupant; the client then frees that occupant first.
  - Then the client calls `SitIn(cart, seat)` on its copy, or `GetOut(at, 0)` for an unseat.
  - A seated Npc skips its `ClientTick` position, and so does any Npc whose `NpcState.FlagSeated` (128) is set; the cart's owner calls `FollowSeat` each rendered frame.
- **Boarders.**
  - The party DTO gains `List<int>? Boarders` (by spot; 0 = free).
  - It also gains `List<int>? WagonBoarders` (one per coupled wagon in train order; 0 = none).
  - `[Msg(MsgKind.Leap)] LeapMsg(int NetId, int FromCart, int ToCart, int Spot, bool Miss, double ServerTime)` is sent when a leap enters its Air phase. `ToCart` is `Party` or `WagonBase + stop`.
  - The client flies the hound itself, starting `serverNow − ServerTime` into the leap: an arc of `LeapSeconds` (0.6 s) and apex `LeapApex` (1.2 m) to the destination spot. It then `Carry`s it there, or, on Miss, releases it to the snapshots.
  - While carried or leaping, `Enemy.ClientTick` skips snapshot positions.
- **`Pose(int Tick, int Member, NetVec Position, float Yaw, float Pitch, NetVec Velocity, PoseFlags Flags, float SeatYaw = 0, PoseFlags2 Flags2 = 0)`**, where `PoseFlags2 : byte { None = 0, Seated = 1, Pumping = 2 }`.
  - The binary layout appends `SeatYaw` (float) and `Flags2` (byte): 45 bytes with the kind byte, up from 40.
  - `PlayerPoseState` appends the same two fields, in the snapshot's binary layout too.
  - `PoseBuffer.Sample` gains both fields, and `At` interpolates `SeatYaw` with `LerpAngle`.
  - `RemotePose` gains `SeatYaw`, `Seated` and `Pumping`.
- **The client's Pose** sets `Flags2.Seated` and `SeatYaw` while seated. The server ignores the client's `Pumping` bit: `RideIntent` (Task 5) is authoritative.
- **Server `ApplyPose`:**
  - For a body the server has seated (`body.Seated`; never the client's flag), it takes only `SeatYaw` (clamped to ±150°), `Pitch` and the flags. It skips the walkable and press checks and ignores `Position`.
  - After the server unseats a member, it drops that member's poses carrying `Flags2.Seated` until one without the flag arrives.
- **Snapshots** carry each player's `SeatYaw`, plus `Flags2` with `Seated` and the server's `hero.Pumping`. `RemoteTick` sets `SeatYaw` while seated and plays the pump from `Pumping`.
- **Every server-side unseat of a Remote hero** sends `ForcePose(landing)` to that peer, along with the `SeatMsg`. This covers stops, set-downs, wrecks, getting out with the party, and throws.
- **`AttackIntent` from a seated body:** `SeatYaw = wrap(a.Yaw − seat heading)`. It sets no body rotation and skips the 1.5 m nudge (`ServerWorld.cs:462`).
- **VR seated yaw.** While `_vr != null`, `FollowSeat` sets `_seatYaw = clamp(wrap(head yaw − seat heading))`, so the pose, aim and attack yaw follow the head.
- **RIDE pass, the steps this task adds.** The test hook `RailController.DebugSeat(member)` seats the remote hero on the server. Then:
  - the client's local body is Seated in seat 0..3;
  - the crew are Seated in their wagons on the client (from the catch-up `SeatMsg`s);
  - a server `GetOut` puts the client body within 0.5 m of the server's landing spot within 0.5 s.

- [ ] **Step 1: Tests.**
  - Codec round trips: the new `Pose` (45 bytes), `PlayerPoseState` in a snapshot, `SeatMsg`, `LeapMsg`, and `NpcState` bit 128. Pins at count 113.
  - `PoseBuffer` interpolates `SeatYaw` across ±π.
  - `CartSeats.Put`: books the given index; refuses a taken seat; keeps `Take` unchanged.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.** Build; the full suite; the three net_test passes; the SP ride tests.
- [ ] **Step 4: Commit** `feat(net): seats, boarders and leaps sync; riders are posed in their carts on every client`.

### Task 5: Remote players ride — board, get out, pump, brake, leave

**Files:**
- Create `src/Dungeon/Rails/Core/RideInputRules.cs`.
- Modify:
  - `src/Net/Core/Protocol.cs`: kind 15, count 114;
  - `src/Net/Core/CartNet.cs` (`RideIntent`);
  - `src/Net/NetRefResolver.cs`: `RefOf` for the party cart;
  - `src/Dungeon/Rails/RailController.cs`;
  - `src/Net/ServerWorld.cs`;
  - `src/Party/Adventurer.cs`.
- Test `tests/Dungeon/Rails/RideInputRulesTests.cs`.

**Interfaces:**
- **`RefOf`:** `MineCart c when c == RailController.Instance?.Cart` resolves to `cart:0`. Spares and `RunawayWagon` never match.
- **The client's `PromptFor`** mirrors the server's rules from the synced state:
  - "Get out" when the local hero is seated in the cart;
  - "Board the cart" when the cart is Held, its stop is not the last, and a hero seat is free.
- **Server `UseCart`:**
  - It accepts Remote actors, with the same checks as for a local player: Held, gate clear, not the last stop, a seat free.
  - When a player boards (LocalHuman, or Remote), these hop in, as `UseCart` does today: the companions within `CompanionBoardRange` that are that player's own (single player: all companions; online: the bots whose `PlayerSlot.BotOwner` is the boarder's name). The bench is filled too.
  - Getting out at speed throws the Remote rider: the run-over, the set-aside behind the cart, then the `ForcePose` from Task 4.
- **`PlayerAboard()`** counts LocalHuman and Remote heroes who are seated, not downed and not parked. `SetDownRiders` and the hop-in rules use it unchanged. A Remote player is never hopped in automatically.
- **`RideIntent(bool Pump, bool Brake)`:**
  - The client sends it on any change while seated, and every 0.25 s of elapsed time while either is held.
  - The server stores it per member. It clears it on unseat or disconnect, and when 1.0 s passes with no RideIntent from a member who was holding.
- **`RideInputRules.Inputs(int playersPumping, bool anyPlayerBrakes, bool companionsWant, int companionsAvailable) → (int pumpers, bool brake)`:**
  - a brake by any seated player cancels all pumping;
  - players pump first;
  - companions fill up to 2 pumpers in total.

  `RailController.Inputs()` uses it, with the local `Intent.Move` for LocalHuman and the stored `RideIntent` for Remote.
- **A player who disconnects while seated** is unseated quietly, by `RailController.UnseatQuietly(Adventurer)`, called in `OnPlayerLeft` before `Park`:
  - the body is set at `ExitCellNear(cart, atStop: true, useTaken: false)`;
  - no run-over, no "jumps from the moving cart" log, and the seat is freed;
  - the `SeatMsg` is broadcast;
  - the slot's parked pose is that spot.
- **RIDE pass, the steps this task adds:**
  - It replaces `DebugDepart`: the client boards through the intent; the server traces `aboard:<name>`;
  - pumping through RideIntent leaves the stop with server `Pumpers ≥ 1`;
  - the client brakes, and the server's V drops;
  - the client gets out at a stop and ends within 0.5 m of the server's spot.

- [ ] **Step 1: Tests.** `RideInputRules`:
  - two players pumping plus a willing companion → 2;
  - one player braking with two pumping → (0, true);
  - no players and companions willing → 2;
  - one player pumping and companions not wanting → 1.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.** Build; the full suite; the three net_test passes; the SP ride tests.
- [ ] **Step 4: Commit** `feat(net): remote players board, ride, pump and brake`.

### Task 6: Remote players use the props — cages, winches, switches, keys

**Files:**
- Modify:
  - `src/Net/NetRefResolver.cs` (`RefOf`);
  - `src/Dungeon/Rails/SetPieces/TrackSwitch.cs` (:58), `src/Dungeon/Rails/SetPieces/SpurSwitch.cs`;
  - `src/Dungeon/Rails/RailGate.cs` (:91);
  - `src/Dungeon/Rails/CageWagon.cs`;
  - `src/Adventure/Objectives/CrewRescueController.cs` (:162, the cage and key prompts);
  - `src/Adventure/Objectives/OrcKeyring.cs`.

**Interfaces:**
- **`RefOf`** gains `TrackSwitch` (`switch:{piece}`, or `spur:{piece}` for the spur's lever), `WinchPost` (`winch:{stop}`), `CageWagon` (`wagon:{stop}`) and `OrcKeyring` (`keyring:0`). On a client, `TryInteract` then sends `InteractIntent` by itself (`Adventurer.cs:1137`); no `Interact` changes are needed.
- **Client prompts** mirror the server's rules from the synced state, instead of returning "" on clients:
  - switch: `Thrown`, `Taken` and `Fixed`;
  - winch: gate up, and no holder in range (the gate's synced state);
  - cage: `Open` and `OrcKeys`;
  - keyring: `OrcKeys`.
- **Server.** The prop methods accept Remote actors wherever a `Control == LocalHuman` or `!= Remote` check refuses them today. The server re-checks every rule on arrival (reach 3.5 m from `InteractIntent`).
- **Hits** need nothing new: brake shoes, the heap, the valve, and levers thrown by a hit. `AttackIntent` runs the server melee, and Task 4 fixed the seated aim.
- **RIDE pass, the steps this task adds.** The server kills stop 1's mobs (test hook), then:
  - `gate:1` "up" reaches the client;
  - the client opens cage 1 through the intent;
  - on ride 3 the client throws a switch through the intent; the server traces `switch:<piece>` as thrown, and the client cart crosses (CrossOut set).

- [ ] **Step 1: Implement.** Add a `NetRefsTests` case: `RefOf` maps a spare `MineCart` to null.
- [ ] **Step 2: Check.** Build; the full suite; the three net_test passes; the SP tests.
- [ ] **Step 3: Commit** `feat(net): remote players open cages, crank winches, throw switches and take the keys`.

### Task 7: Crew, gates and set-piece visuals on clients; shakes for remote riders

**Files:**
- Modify:
  - `src/Adventure/Objectives/CrewRescueController.cs` (`ApplyNet` :590-603);
  - `src/Save/Core/SaveDtos.cs`: `ObjectiveStateDto.CrewDownedLeft`;
  - `src/Adventure/Npc.cs`: the client bleed label;
  - `src/Dungeon/Rails/SetPieces/OreBuckets.cs`, `src/Dungeon/Rails/SetPieces/LowBeam.cs`, `src/Dungeon/Rails/SetPieces/OreHeap.cs`;
  - `src/Dungeon/Rails/RailEncounterController.Kiln.cs`: the golem's windup telegraph;
  - `src/Net/Core/CartNet.cs`: party `ArmedRide`, `ArmedAt`;
  - `src/Party/Adventurer.cs` (`Shake`);
  - `src/Net/ClientWorld.cs` (`ShakeEvt`).

**Interfaces:**
- **Crew.**
  - `ApplyNet` applies `GatesUp` (`RailController.RestoreGates`).
  - Crew seats come from `SeatMsg` (Task 4), not from the objective.
  - `ObjectiveStateDto.CrewDownedLeft : Dictionary<string, float>?` carries the seconds left for each downed crew member. Clients count the label down locally, refreshed by each objective broadcast (2 s).
- **Buckets.**
  - The party DTO gains `int ArmedRide` (−1 when none) and `double ArmedAt` (the server time the ride armed).
  - Client buckets run the server's schedule from `serverNow − ArmedAt`: bucket n creaks at `n · Every` (3 s) and pours `Creak` seconds later. Each pour spawns a visual-only `CinderPatch` that lives `PatchSeconds`.
  - Hits on the cart stay server-judged.
- **Hammers and the pour** already animate from `FloorTick` (Task 1). Their glow and stream are checked on the client in the RIDE pass.
- **Client-local presentation**, from the follower, each with one rule:
  - `LowBeam` ducks the seated desktop camera as the client's cart front passes (same S window as the server's);
  - the heap's lantern blinks under the server's `TickHeap` rule (cart rolling, heap ahead on the party lane, within `V·3 s`), without the bark.
- **The golem's windup** raises `EventBus.EnemyTelegraph` (today it does not), so clients get the existing `EnemyStateMsg`.
- **Shakes.** A server `Adventurer.Shake` on a Remote body sends `ShakeEvt(amplitude, seconds)` to that member's peer only. `ClientWorld` applies it to the local body, which is ignored in VR.
- **RIDE pass, the steps this task adds:**
  - the client sees the wagon coupled within 0.5 s of departure (from `Train`);
  - a downed crew member's label counts down on the client;
  - on ride 4 the client's hammer reaches Fall within 50 ms of the server's (both log `t=<server time>` at Fall entry).

- [ ] **Step 1: Implement.** Add a SaveDtos round-trip test: `CrewDownedLeft` survives JSON, and null is the default.
- [ ] **Step 2: Check.** Build; the full suite; the three net_test passes; the SP tests.
- [ ] **Step 3: Commit** `feat(net): the crew, the gates and the set pieces match on every client`.

### Task 8: Joiners mid-ride, resyncs, and the cart watchdog

**Files:**
- Create `src/Dungeon/Rails/Core/CartWatchdog.cs`.
- Modify:
  - `src/Net/ServerWorld.cs` (`SendCatchUp` :330, `OnFloorReady` :155);
  - `src/Dungeon/Rails/RailNet.cs`;
  - `src/Dungeon/Rails/RailController.cs`, `src/Dungeon/Rails/RailEncounterController.Traffic.cs`;
  - `src/Net/NetTestFlow.cs`, `scripts/net_test.sh`: a second client.
- Test `tests/Dungeon/Rails/CartWatchdogTests.cs`.

**Interfaces:**
- **`RailNet.SendRailCatchUp(int peer)`** sends:
  - a `CartStateMsg` with every live cart;
  - a `SeatMsg` for every seated rider (party cart, wagons, chaser);
  - every in-flight leap as a `LeapMsg`;
  - a `FloorClockMsg`.

  It is called from both `SendCatchUp` (a join) and `OnFloorReady` (a floor change, a `FloorResyncIntent` rebuild, a wipe reload). Rail props already ride in `SendPropState`.
- **`CartWatchdog`** (engine-free): `bool Stuck(float progress10s, float targetProgress10s, bool watched)`, with a 10 s window and a 0.5 m threshold.
  - **The party cart** is watched only while Rolling and not Halted. It is also not watched while any seated rider (player or companion) holds the brake, because a player who stops the cart on purpose is not stuck.
  - **When the party cart is stuck,** it is moved to its next stop as an arrival (`Arrive()`), with the log line "The cart grinds free."
  - **The watchdog never moves a cart across the jump gap.** If the next stop lies past an unjudged `RailGap`, the cart is instead set to the gap's near lip and given `JumpSpeed`, so the jump is judged normally. The plan 4 final review ruled this.
  - **A pacing slag cart** is watched only while the party cart is Rolling and not Halted. It is stuck when its target S moved ≥ 0.5 m but the cart moved < 0.5 m; it is then freed.
  - **Traffic carts and runaways** are judged on their own progress.
  - **The golem runner** is never watched; its script ends it.
- **RIDE pass, the steps this task adds:**
  - a second client joins during ride 2;
  - within 1 s of its Ready it has: the cart within 1.5 m (paired as in Task 3), the same seats, the same `Train`, and the same prop hash;
  - it traces `joined-mid-ride ok`.

- [ ] **Step 1: Tests.** `CartWatchdog`:
  - a party cart halted for 30 s is never stuck;
  - a Rolling cart with 0.3 m of progress in 10 s is stuck;
  - a Rolling cart held at 0 m/s by a braking rider for 30 s is not stuck;
  - a slag cart beside a halted party cart for 30 s is not stuck;
  - a slag cart that cannot follow a moving target is stuck.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.** Build; the full suite; the three net_test passes; the SP tests.
- [ ] **Step 4: Commit** `feat(rails): joiners and resyncs mid-ride see the ride as it is; a cart watchdog`.

### Task 9: A spare cart for a co-op player left behind

**Files:**
- Create `src/Dungeon/Rails/RailController.Chase.cs`.
- Modify:
  - `src/Dungeon/Rails/Core/SpareRules.cs`;
  - `src/Dungeon/Rails/SpareYard.cs`;
  - `src/Net/NetRefResolver.cs`;
  - `src/Dungeon/Rails/RailNet.cs`;
  - `src/Net/NetTestFlow.cs`.
- Test `tests/Dungeon/Rails/SpareRulesTests.cs`.

**Interfaces:**
- **`SpareRules.MayChase(int spareStop, bool spareUsed, int trainStop, bool trainHeldAtSpare, bool chaseLive) → bool`:** true when:
  - the spare is unused;
  - the train has left that stop (`trainStop > spareStop`, or Rolling out of it);
  - no chase is live (one at a time: "no second train").
- **`SpareRules.ChaseLimit(float trainBackS, float nextStopS) => min(nextStopS, trainBackS − 1.5f)`.** `trainBackS` is the back of the last coupled wagon: `Party.BackS − Train.Count · WagonSpacing − HalfLength`.
- **The spare's prompt** is "Take the spare cart" for a player hero on foot (LocalHuman or Remote, not downed) within 3 m, when `MayChase` holds. The client mirrors it from the synced `SparesUsed` and the cart state. `RefOf(spare MineCart) → spare:{stop}`.
- **Taking it:**
  - The spare is spent: `SparesUsed` gains the stop, and the PropEvt `spare` "used" is raised.
  - A chaser cart, a `MineCart` with `Kind = Chaser` and id `ChaserBase + stop`, is placed on the party line at `StopS[stop]` and Held.
  - The actor and their own companions within `CompanionBoardRange` are seated, and a 5 s boarding countdown starts.
  - The chaser then runs with `CartRun` on the party track at the party cart's physics, with no pumping, no set pieces judged, and the run-over sweep on. `Limit` is recomputed every tick with `ChaseLimit`, so it halts behind the train and never passes it.
- **When it ends:** once the chaser is Held and the train is Held, at a stop or Halted:
  - the riders are set down beside the lane (as at a stop);
  - the chaser is hidden after 2 s;
  - the chase ends.

  A wreck, a load (`Restore`) or `OnFoot` ends the chase at once, with the riders set down beside the chaser. The chase is not saved; its spare stays spent.
- **Sync.** `CartKind.Chaser` rides in `CartStateMsg`; its seats go as `SeatMsg` with `Cart = ChaserBase + stop`.
- **RIDE pass, the steps this task adds.** The second client stays at stop 1 while the train leaves with the first. Then:
  - it takes spare 1 through the intent;
  - its chaser halts within 3 m behind the train's last body;
  - it traces `chase-done`.

- [ ] **Step 1: Tests.**
  - `MayChase`: refused when used, when the train is held at that stop, and while a chase is live; allowed otherwise.
  - `ChaseLimit`: never past `trainBackS − 1.5`; capped at the next stop.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Check.** Build; the full suite; the three net_test passes; the SP tests. Single player is unchanged: there is no chase without a second player, and spares are not boardable while the train is Held at their stop.
- [ ] **Step 4: Commit** `feat(rails): a co-op player left behind takes the spare cart after the train`.

### Task 10: VR comfort

**Files:**
- Create:
  - `src/Party/Core/ComfortRules.cs` (engine-free: `ComfortLevel`, `ComfortState`);
  - `src/Party/ComfortVignette.cs` (a quad parented to the `XRCamera3D`);
  - `assets/shaders/comfort_vignette.gdshader`.
- Modify:
  - `src/Core/Settings.cs`: `VrComfort : ComfortLevel`, default `Recommended`, saved and loaded with the other settings;
  - `src/UI/Menus/SettingsScreen.cs`: a three-way option;
  - `src/Party/VrRig.cs`: attach the vignette; the seated lift;
  - `src/Party/Adventurer.cs`: feed the comfort inputs while seated.
- Test `tests/Party/ComfortRulesTests.cs`.

**Interfaces:**
- **`ComfortState Step(ComfortState s, float dt, float accel, float yawRateDeg, float verticalAccel, ComfortLevel level)`.** `ComfortState` holds `float Radius` (1 is open), `float Fade` (0..1) and `float FadeLeft`.
  - **The target radius:**
    - Off: always 1.
    - Otherwise `t = max(ramp(|accel|, 1.5, 4.0), ramp(|yawRateDeg|, 30, 90))`, where `ramp(x, a, b) = clamp((x − a)/(b − a), 0, 1)`, and the target is `1 − t·(1 − floor)`. The floor is 0.55 for Recommended and 0.35 for Strong.
  - **Easing:** `Radius += (target − Radius)·(1 − exp(−dt/0.25))`.
  - **The fade** happens only in Strong. When `verticalAccel < −4` m/s², `FadeLeft = 0.4`. `Fade` is 0.6 while `FadeLeft > 0`; after that it eases to 0 with a 0.1 s time constant.
  - The vignette is never full black.
- **Inputs, while seated in VR:**
  - `accel` is d(cart V)/dt, smoothed over 0.1 s;
  - `yawRateDeg` is the seat heading's change per second;
  - `verticalAccel` is the second difference of the seat's Y.

  They come from `FollowSeat`, with no allocation.
- **Seated eye height.** While seated, `RideAlong` and `AfterMove` lift the room by `_lift − (EyeHeight − Adventurer.SeatedEyeHeight)`, which is 0.45 m lower. Standing restores it, and nothing is re-measured. The eye then sits about 1.15 m above the seat, with the rim and front board in the lower view.
- **No camera shake, roll or pitch in VR**, which is already true. Snap turn while seated also already works; Step 3 checks it.

- [ ] **Step 1: ComfortRules tests.**
  - Off is always 1, at any input.
  - Recommended never goes below 0.55.
  - Strong reaches ≤ 0.36 after 2 s at maximum input.
  - The radius moves monotonically toward a constant target.
  - The fade: only in Strong, only when `verticalAccel < −4`, lasting 0.4 s ± one step, peaking at 0.6.
- [ ] **Step 2: Implement.** Settings persist across a restart.
- [ ] **Step 3: Check.**
  - Build; the full suite.
  - A headless screenshot of the vignette at Strong in a forced state. Use a local, uncommitted flag that draws the quad on the desktop camera for the screenshot only.
  - `--test-ride` passes. `VrRig` snap turn while seated still turns the room: read the code and add a unit check of `TurnBy` if it is extracted.
- [ ] **Step 4: Commit** `feat(vr): a comfort vignette for the ride; seated eye height keeps the cart in view`.

### Task 11: Regressions and docs

**Files:**
- Modify `docs/development.md`: a "Riding together" section under Rails.

- [ ] **Step 1: Run the regressions.**
  - The full suite.
  - `scripts/net_test.sh`, `START_FLOOR=0`, `RIDE=1`.
  - `--test-ride`, `--test-ride-edges`, `--test-chase`, `--test-crew` (both branches).
  - `--test-tick-rate` at 60 and 90.
- [ ] **Step 2: Docs.** Cover:
  - the two clocks;
  - client set pieces and rail props;
  - the cart state, corrections and the follower;
  - seats, poses, boarders and leaps;
  - remote riding and props;
  - the spare chase;
  - catch-up and resyncs;
  - the watchdog;
  - VR comfort;
  - the RIDE pass and its markers.
- [ ] **Step 3: Commit** `docs: riding together`.
