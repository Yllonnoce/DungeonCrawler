# Mine cart 7: the cage hold and the relief cart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On floor 12, the party cart no longer leaves a stop whose cage is still locked unless a seated player holds E to leave the prisoners behind (every mode). In single player, a player left behind gets a relief cart about 15 s later, so they are never stranded.

**Architecture:** Two engine-free rule sets, `CageHoldRules` and `ReliefRules` with a `ReliefClock`, are unit-tested in `tests/`. Godot code applies them.
- **The cage hold.** It lives in `RailController`: one new `CartFlags` bit tells clients. The leave-them-behind press is a generic hold-to-confirm on interact (`InteractHold`, `IHoldInteractable`). A client sends it as an `InteractIntent` whose ref is wrapped as `hold:<ref>`. No message changes shape.
- **The relief cart.** It reuses plan 5's co-op chaser (`RailController.Chase.cs`): a `Chase` with `Relief = true` and a phase. It is sent to the player rather than taken at a stop, and the chaser's limit, hops, heap halt, run-over sweep, bell and countdown all carry over unchanged.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (`tests/DungeonCrawler.Tests.csproj`, engine-free files linked from `src/**/Core`).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-relief-cart-design.md`. It is an addendum to `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md`, which still binds everything it does not change. Read both.

## Global Constraints

- Protocol stays **17**. `Protocol.MessageKindCount` stays **114**: no new `MsgKind` and no message changes shape. The cage hold rides on a new `CartFlags` bit (`CageHeld = 128`, the last free bit of the byte) and on a new `NetRefs` kind (`hold`) carried by the existing `InteractIntent`.
- Co-op behaves exactly as today except for the cage hold. The relief cart is single player only: every relief path is guarded by `!NetSession.IsNetworked`.
- No new assets. The relief cart is the spare cart's model and sound (`MineCart.AsChaser()`, `ChaserPlacedSound`, `FeedChaserSound`).
- New lines are subtitles and log lines only, with no voice spend. No new `Line` goes into a voiced bank, and Grisel gets no new line.
- Nothing new is saved. The relief cart, the relief clock and the stops whose prisoners were left behind all start clean on a load (`RailController.Restore`).
- `tests/` is NOT in git. Commit `src/`, `docs/`, `scripts/net_test.sh` and the `.cs.uid` Godot generates for each new `.cs` file under `src/`. Check `git status` after the first headless run.
- Run `dotnet build DungeonCrawler.csproj` before any `godot --headless` run. Godot flags go after `--`.
- Never `pkill -f` your own pattern; use `--quit-after`. Run `RIDE=1 scripts/net_test.sh` alone, with nothing else heavy running.
- No installers and no dist builds (`export_all`, `package.sh`). Do not push or merge.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Settled values (the spec's "Calls left to the builder")

| What | Value | Where |
|---|---|---|
| "In a bit" | 15 s left behind (`ReliefRules.FirstWait`) | ReliefRules |
| Wait beside you | 30 s, then it goes (`ReliefRules.WaitBeside`) | ReliefRules |
| Next one | 15 s after one goes (`ReliefRules.NextWait`) | ReliefRules |
| Left behind | The train's last body's back edge is more than **12 m** (`LeftBehindRange`, the same as `RailController.HudRange`) ahead of the player's party-line S | ReliefRules |
| Spawn | At the last stop at or behind the player's S (stop 0 if none), its front at least 1 m (`SpawnClear`) short of the player, never past the chaser's limit, never over a hole | ReliefRules, SpareRules.HoleLimit |
| "Already close" | The player is within **6 m** (`WaitAtStopRange`) of the spawn S along the line: it waits where it was placed | ReliefRules |
| Seat beside you | Its centre at the player's party-line S | ReliefRules |
| On the rails | Its back end stops **1.5 m** (`PastRunOver`) past the body (centre = playerS + 1.1 + 0.4 + 1.5) | ReliefRules |
| Seat target | Only moves forward (`Target = max(old, new)`), so a run-over cannot pull it back onto the body | ReliefRules |
| Leave-them-behind | Hold interact for **1.0 s** (`InteractHold.HoldSeconds`) while seated in the party cart at a stop whose cage holds it. Keyboard E and the VR grip both work. A tap still gets out, firing on release whenever a hold is offered | InteractHold |
| Seated prompt | `Get out (hold to leave the prisoners behind)` | CageHoldRules.SeatedPrompt |
| HUD | `Cart — cage locked`; the relief's bar reads `Relief cart — departs in N` / `Relief cart — after the train` / `Relief cart — behind the train` | RailHud |
| Win rule (ruled 2026-11-19) | The crew of a cage the player left behind count as **Lost**, as dead crew do: they never block the win, and their closed cage does not either. If the player later opens that cage on foot, its crew are Freed again and follow on foot as today. Without this, leaving them would seal the stairs for good | CrewRules, CrewRescueController |
| Seat over a hole (ruled) | A relief's seat never lies inside a jump gap or fallen-trestle span. It stops short of the near lip (`SpareRules.HoleLimit`), and the player walks a few metres to it | ReliefRules.SeatLimit |
| After a wreck (ruled) | A relief may come after a wreck whose spare took over at a stop ahead of the player. The player is left behind with the train ahead, and "never stranded" wins over the spec's silence. Nothing in `Wreck()` sets `_met` | RailController.Relief |
| "Met" at a build or a load (ruled) | `_met` starts **false**. `ReliefRules.Met` sets it on the first tick the player sits in the standing cart or stands within 12 m of the train, so a player still far behind after a load gets a relief | RailController.Relief |

**Why hold E is feasible.** `PartyController._UnhandledInput` sees the press and the release of `interact` (it already times `attack_primary` the same way for heavy swings). `VrRig.Read` already keeps `_useHeld`, so it can report both edges. The only cost is that "Get out" fires on release instead of on press, and only while a hold is offered.

---

### Task 1: The cage hold and the hold-to-confirm press

**Deliverable:** Seated at a stop whose cage wagon is locked with any living crew, the cart does not count down. Holding E for 1 s leaves the prisoners behind and starts the countdown. Solo and co-op both work, and so does a co-op client, through `CartFlags.CageHeld` and `hold:cart:0`. Every existing flow passes.

**Files:**
- Create `src/Dungeon/Rails/Core/CageHoldRules.cs`
- Create `src/Party/Core/InteractHold.cs`
- Modify `src/Party/IInteractable.cs` (add `IHoldInteractable`)
- Modify `src/Party/PartyController.cs` (input lines 333-358, `VrUse` 369, `_Process` 422+)
- Modify `src/Party/VrRig.cs:252`
- Modify `src/Party/Adventurer.cs` (`TryHoldInteract` beside `TryInteract`, 1211)
- Modify `src/Net/Core/NetRefs.cs`
- Modify `src/Net/Core/CartNet.cs` (`CartFlags`)
- Modify `src/Net/ServerWorld.cs` (`case InteractIntent`, 523)
- Modify `src/Dungeon/Rails/MineCart.cs`
- Modify `src/Dungeon/Rails/Core/CartEvents.cs`
- Modify `src/Dungeon/Rails/Core/RailHud.cs`
- Modify `src/Dungeon/Rails/RailController.cs` (`Setup`, `Restore`, `StartBoarding`, `PromptFor`, `Tick`, `HudState`)
- Modify `src/Dungeon/Rails/RailController.Net.cs` (`NetFlags`)
- Modify `src/Adventure/Objectives/CrewRescueController.cs` (`Build`, `_ExitTree`, `OnCartEvent`, `Judge`, `Tick`)
- Modify `src/Adventure/Core/Objectives/CrewRules.cs` (the win rule for a cage left behind)
- Modify `src/Core/DebugTools.Ride.cs`, `DebugTools.RideEdges.cs`, `DebugTools.Chase.cs`, `DebugTools.CartWatchdog.cs`, `DebugTools.Crew.cs`
- Modify `src/Net/NetTestFlow.cs` and `scripts/net_test.sh`
- Modify `docs/development.md`
- Tests: create `tests/Dungeon/Rails/CageHoldRulesTests.cs` and `tests/Party/InteractHoldTests.cs`; modify `tests/Dungeon/Rails/RailHudTests.cs`, `tests/Net/CartNetTests.cs` and `tests/Adventure/CrewRulesTests.cs`; add a NetRefs test to the existing NetRefs test file (find it with `grep -rl "NetRefs.TryParse" tests`), or to `tests/Net/CartNetTests.cs` if there is none

**Interfaces:**
- Produces:
  - **Used by Task 2:** `RailHudState(..., bool CageHeld = false)`. Task 2 appends after it.
  - **Used by the crew controller and the flows:**
    - `RailController.CageHeld` (bool)
    - `RailController.CageHolds` (`Func<int,bool>?`)
    - `RailController.CrewLeftAt` (`IReadOnlyCollection<int>`)
    - `MineCart.HoldFor` (`Func<Adventurer,bool>?`), `MineCart.OnHold` (`Action<Adventurer>?`), `MineCart.HoldOffered`, `MineCart.HoldInteract(PartyController, Adventurer)`
    - `NetRefs.Hold(string)`
    - `DebugTools.RideWaiveCages(Main, RailController, string)`
    - `CrewRules.JudgeCaged(bool leftBehind)` and `CrewRules.CagesBlocking(IReadOnlyList<(bool Open, bool LeftBehind)>)`

- [ ] **Step 1: Write the failing tests**

`tests/Dungeon/Rails/CageHoldRulesTests.cs`:
```csharp
[Fact] public void Holds_LockedWithLivingCrew() => Assert.True(CageHoldRules.Holds(cageOpen: false, livingCrew: 2));
[Fact] public void Holds_LockedWithEveryCrewDead_DoesNot() => Assert.False(CageHoldRules.Holds(false, 0)); // the spec: a cage whose crew are all dead doesn't hold the cart
[Fact] public void Holds_Open_DoesNot() => Assert.False(CageHoldRules.Holds(true, 2));
// Held(mode, halted, stopIndex, stopCount, waived, cageHolds)
[Fact] public void Held_AtAnIntermediateStop() => Assert.True(CageHoldRules.Held(CartMode.Held, false, 2, 5, false, true));
[Fact] public void Held_NotHaltedOnTheLine() => Assert.False(CageHoldRules.Held(CartMode.Held, true, 2, 5, false, true));
[Fact] public void Held_NotRolling() => Assert.False(CageHoldRules.Held(CartMode.Rolling, false, 2, 5, false, true));
[Fact] public void Held_NotWrecked() => Assert.False(CageHoldRules.Held(CartMode.Wrecked, false, 2, 5, false, true));
[Fact] public void Held_NotAtTheEndOfTheLine() => Assert.False(CageHoldRules.Held(CartMode.Held, false, 4, 5, false, true));
[Fact] public void Held_NotOnceLeftBehind() => Assert.False(CageHoldRules.Held(CartMode.Held, false, 2, 5, true, true));
[Fact] public void Held_NotWithoutALockedCage() => Assert.False(CageHoldRules.Held(CartMode.Held, false, 2, 5, false, false));
[Fact] public void MayLeaveBehind_SeatedAndUp() => Assert.True(CageHoldRules.MayLeaveBehind(seatedHere: true, downed: false, cageHeld: true));
[Fact] public void MayLeaveBehind_OnFoot_No() => Assert.False(CageHoldRules.MayLeaveBehind(false, false, true));
[Fact] public void MayLeaveBehind_Downed_No() => Assert.False(CageHoldRules.MayLeaveBehind(true, true, true));
[Fact] public void MayLeaveBehind_NoHold_No() => Assert.False(CageHoldRules.MayLeaveBehind(true, false, false));
[Fact] public void SeatedPrompt_Words() => Assert.Equal("Get out (hold to leave the prisoners behind)", CageHoldRules.SeatedPrompt);
```

`tests/Party/InteractHoldTests.cs` (times in seconds, `double`):
```csharp
[Fact] public void Press_NoHoldOffered_ActsAtOnce() { var h = new InteractHold(); Assert.True(h.Press(10, holdOffered: false)); Assert.False(h.Holding); Assert.False(h.Release(10.2)); }
[Fact] public void Press_HoldOffered_Waits() { var h = new InteractHold(); Assert.False(h.Press(10, true)); Assert.True(h.Holding); }
[Fact] public void Release_BeforeTheSecond_IsATap() { var h = new InteractHold(); h.Press(10, true); Assert.False(h.Tick(10.9)); Assert.True(h.Release(10.95)); Assert.False(h.Holding); }
[Fact] public void Tick_AtTheSecond_FiresOnce() { var h = new InteractHold(); h.Press(10, true); Assert.False(h.Tick(10.999)); Assert.True(h.Tick(11.0)); Assert.False(h.Tick(11.5)); }
[Fact] public void Release_AfterTheHoldFired_IsNoTap() { var h = new InteractHold(); h.Press(10, true); h.Tick(11.1); Assert.False(h.Release(11.2)); }
[Fact] public void Cancel_MidHold_NeitherTapNorHold() { var h = new InteractHold(); h.Press(10, true); h.Cancel(); Assert.False(h.Tick(12)); Assert.False(h.Release(12)); }
[Fact] public void HoldSeconds_IsOne() => Assert.Equal(1.0, InteractHold.HoldSeconds);
```

`RailHudTests` (use the existing `At()` helper):
```csharp
[Fact] public void CageLocked() => Assert.Equal("Cart — cage locked", RailHud.Text(At() with { CageHeld = true }));
[Fact] public void CageLocked_GateDownSaysGateFirst() => Assert.Equal("Cart — gate down", RailHud.Text(At() with { CageHeld = true, GateClear = false }));
[Fact] public void CageLocked_ACountdownShowsTheCountdown() => Assert.Equal("Cart — 100% — departs in 4", RailHud.Text(At() with { CageHeld = true, Boarding = true, BoardingLeft = 3.2f }));
```

`CartNetTests`:
```csharp
[Fact] public void CartFlags_CageHeld_IsTheLastBit_AndRoundTrips()
{
    Assert.Equal(128, (int)CartFlags.CageHeld);
    // round-trip a CartStateDto with Flags = (byte)(CartFlags.GateClear | CartFlags.CageHeld) the way the
    // file's existing round-trip test does, then assert d.Has(CartFlags.CageHeld) && d.Has(CartFlags.GateClear)
}
```

`tests/Adventure/CrewRulesTests.cs` (uses the file's own `C(...)` helper):
```csharp
[Fact] public void JudgeCaged_LeftBehind_IsLost() => Assert.Equal(CrewState.Lost, CrewRules.JudgeCaged(leftBehind: true));
[Fact] public void JudgeCaged_StillWaiting_IsCaged() => Assert.Equal(CrewState.Caged, CrewRules.JudgeCaged(false));
[Fact] public void CagesBlocking_ALeftCageDoesNotBlock() => Assert.Equal(0, CrewRules.CagesBlocking(new[] { (false, true), (true, false) }));
[Fact] public void CagesBlocking_ALockedCageStillBlocks() => Assert.Equal(1, CrewRules.CagesBlocking(new[] { (false, false), (false, true) }));
[Fact] public void Done_WithALeftCagesCrewLost() => Assert.True(CrewRules.Completed(C(CrewState.Home, CrewState.Home, CrewState.Lost, CrewState.Lost), CrewRules.CagesBlocking(new[] { (true, false), (false, true) })));
```

NetRefs test:
```csharp
[Fact] public void Hold_WrapsARef()
{
    Assert.Equal("hold:cart:0", NetRefs.Hold(NetRefs.Cart));
    Assert.True(NetRefs.TryParse(NetRefs.Hold(NetRefs.Cart), out var kind, out var id));
    Assert.Equal(NetRefs.HoldKind, kind);
    Assert.Equal("cart:0", id);
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~CageHoldRulesTests|FullyQualifiedName~InteractHoldTests|FullyQualifiedName~RailHudTests|FullyQualifiedName~CartNetTests|FullyQualifiedName~Hold_WrapsARef|FullyQualifiedName~CrewRulesTests"`
Expected: build errors (`CageHoldRules`, `InteractHold`, `CageHeld`, `NetRefs.Hold`, `CrewRules.JudgeCaged` not defined).

- [ ] **Step 3: The engine-free pieces**

- `src/Dungeon/Rails/Core/CageHoldRules.cs`, `public static class CageHoldRules`:
  - `const string SeatedPrompt` (the words above)
  - `static bool Holds(bool cageOpen, int livingCrew)`
  - `static bool Held(CartMode mode, bool halted, int stopIndex, int stopCount, bool waived, bool cageHolds)`: true only when the cart is Held, not halted, at a stop below `stopCount - 1`, not waived, and the cage holds. Stop 0 never has a wagon, so it returns false through `cageHolds`.
  - `static bool MayLeaveBehind(bool seatedHere, bool downed, bool cageHeld)`
- `src/Party/Core/InteractHold.cs`, `public sealed class InteractHold` (engine-free; namespace `DungeonCrawler.Party`):
  - `const double HoldSeconds = 1.0`
  - `bool Holding { get; }`
  - `bool Press(double now, bool holdOffered)`: true means act now.
  - `bool Release(double now)`: true means a tap, so act now.
  - `bool Tick(double now)`: true once, when the hold completes.
  - `void Cancel()`
- `CartFlags.CageHeld = 128`. Doc: "The party's cart stands at a stop whose cage wagon is locked with living crew, and nobody has chosen to leave them: no countdown."
- `NetRefs`: `public const string HoldKind = "hold";` and `public static string Hold(string inner) => $"{HoldKind}:{inner}";`. Doc: "A held interact (the leave-the-prisoners-behind press) on the wrapped ref."
- `CartEventKind`: append `CageHeld`. It is raised when a countdown would start at a stop but the cage holds the cart (`Stop` = that stop). It is not on the wire (`grep CartEventKind src/Net` is empty).
- `RailHudState`: append `bool CageHeld = false`. In `RailHud.Text`, after the gate-down line and before the boarding line: `if (s.Mode == CartMode.Held && s.CageHeld && !s.Boarding) return "Cart — cage locked";`
- `CrewRules`:
  - `public static CrewState JudgeCaged(bool leftBehind) => leftBehind ? CrewState.Lost : CrewState.Caged;`
  - `public static int CagesBlocking(IReadOnlyList<(bool Open, bool LeftBehind)> cages)`: counts the cages that are neither open nor left behind.
  - `Completed` is unchanged; its caller now passes `CagesBlocking` in place of every closed cage.

- [ ] **Step 4: Run Step 1's tests and see them pass**

Same command. Expected: PASS.

- [ ] **Step 5: The input layer**

- `IInteractable.cs`: add `public interface IHoldInteractable : IInteractable { bool HoldOffered { get; } void HoldInteract(PartyController party, Adventurer actor); }`, with a doc saying what holding interact for `InteractHold.HoldSeconds` does on it. Extending `IInteractable` lets `NetRefResolver.RefOf(target)` take it as is.
- `PartyController`: add a field `readonly InteractHold _hold = new()` and a private `void Interact(bool pressed)` that keyboard and VR share:
  - Pressed: `if (_hold.Press(_time, local.LookingAt is IHoldInteractable { HoldOffered: true })) local.TryInteract();`
  - Released: `if (_hold.Release(_time)) local.TryInteract();`
  - In `_UnhandledInput`, the `interact` press calls `Interact(true)`. Add `else if (@event.IsActionReleased("interact")) Interact(false);` to the chain.
  - `VrUse()` becomes `VrUse(bool pressed)` and calls `Interact(pressed)`.
  - In `_Process`, immediately after `_time += delta;` and **before** the `if (Net.NetSession.IsClient) { … return; }` block (`PartyController.cs:426-433`), so that a client's hold fires too:
    - If `!PartyAlive || InputLocked || !MouseCaptured && Local?.VrRig == null`, call `_hold.Cancel()`.
    - Otherwise, when `_hold.Tick(_time)` fires, call `Local?.TryHoldInteract()`.
  - `Possess` calls `_hold.Cancel()`, so a hold started as one hero never fires as another.
- `VrRig.Read`: replace `if (use && !_useHeld) party.VrUse();` with `if (use != _useHeld) party.VrUse(use);`.
- `Adventurer.TryHoldInteract()`:
  - Do nothing if `Downed` or `LookingAt is not IHoldInteractable { HoldOffered: true } target`.
  - On a client, send `new Net.InteractIntent(Net.NetRefs.Hold(netRef))`, where `netRef = Net.NetRefResolver.RefOf(target)` (do nothing when it is null).
  - Otherwise call `target.HoldInteract(Party, this)`.
- `ServerWorld` `case InteractIntent i`: first handle `NetRefs.TryParse(i.Ref, out var k, out var inner) && k == NetRefs.HoldKind`:
  - Resolve `inner` with `NetRefResolver.Resolve(root, inner)` and require `IHoldInteractable`.
  - Check reach as for a cart: `node is Dungeon.Rails.MineCart tub ? tub.TubDistance(body) : <the existing min of the node's distance to body and camera>` against `PropPromptRules.InteractReach`.
  - On a test server, print `[net-server] hold {inner} from {slot.Name}: {dist:0.0} m`.
  - Call `HoldInteract(party, body)` and `break`. Everything else is unchanged.
- `MineCart : IHoldInteractable`:
  - `Func<Adventurer, bool>? HoldFor`
  - `Action<Adventurer>? OnHold`
  - `HoldOffered => Main.Instance?.Party?.Local is { } me && HoldFor?.Invoke(me) == true`
  - `HoldInteract(party, actor) => OnHold?.Invoke(actor)`

- [ ] **Step 6: The hold in RailController and CrewRescueController**

`RailController`:
- `public Func<int, bool>? CageHolds { get; set; }`: set by the crew controller. "The stop's cage wagon is locked with living crew."
- `private readonly HashSet<int> _crewLeftAt = new();` (not saved) and `internal IReadOnlyCollection<int> CrewLeftAt => _crewLeftAt;`
- `private bool? _heldStartCall;` holds the `call` of the first countdown the cage held back. It is kept with `??=`, so a later start never changes it. It is cleared whenever a countdown starts, at a departure (`Departed`/`Resumed` in `Tick`) and in `Restore`.
- `public bool CageHeld`:
  - On a client: `(_netFlags & (byte)CartFlags.CageHeld) != 0`.
  - Otherwise: `CageHoldRules.Held(State.Mode, Halted, StopIndex, StopS.Length, _crewLeftAt.Contains(StopIndex), CageHolds?.Invoke(StopIndex) == true)`.
  - It does not depend on the gate. The HUD shows the gate first. A player may leave the prisoners while the gate is still down, and the gate's lift then starts the countdown as it does today.
- `StartBoarding(bool call)`, after the existing `Halted && LineBlocked` guard: `if (CageHeld) { _heldStartCall ??= call; Raise(new CartEvent(CartEventKind.CageHeld, Stop: StopIndex)); return; }`. On a start that goes through, set `_heldStartCall = null`.
- `Tick`, Held branch:
  - Add `|| CageHeld` to the cancel condition on line 718. When `CageHeld` is what cancels a running countdown, also set `_heldStartCall ??= false`, so the countdown restarts by itself once the hold ends. Today nothing starts a countdown under a held cage, so this is a guard.
  - Before that if/else, as its own statement: `if (_heldStartCall is { } held && playerAboard && seated > 0 && GateClear && !(Halted && LineBlocked) && !Boarding.Running && !CageHeld) StartBoarding(held);`. This covers a cage opened while players sit waiting (Grisel from the bench, a pick, the keys) and its last crew dying. The existing cancel/departure if/else then runs unchanged.
- `private bool HoldFor(Adventurer hero) => CageHoldRules.MayLeaveBehind(hero.Cart == Cart, hero.Downed, CageHeld);` is the one helper.
- `PromptFor`: `if (HoldFor(hero)) return CageHoldRules.SeatedPrompt;`. Otherwise keep today's `BoardPrompt`.
- `private void LeaveCrew(Adventurer actor)`, wired in `Setup` as `OnHold` (wrapped so it ends in `Flush()`, like `Use`):
  - Return if on a client, if `!Current`, if `!IsPlayer(actor)`, or if `!HoldFor(actor)`.
  - `_crewLeftAt.Add(StopIndex)`.
  - `EventBus.Instance.Log($"{actor.State.Name} leaves the prisoners behind.")` and `GD.Print($"[rails] the prisoners at stop {StopIndex} are left behind ({actor.State.Name})")`.
  - `if (!Boarding.Running && GateClear && !(Halted && LineBlocked) && PlayerAboard()) StartBoarding(_heldStartCall ?? true);`
  - `RefreshHud()`.
- `Restore`: `_crewLeftAt.Clear(); _heldStartCall = null;`.
- `HudState`: pass `CageHeld: CageHeld`.
- `NetFlags`: `if (CageHeld) f |= CartFlags.CageHeld;`. `NetSignature` already includes `NetFlags()`, so the change goes out at once.

`CrewRescueController`:
- In `Build`, after `_rails.CartEvents += OnCartEvent;`: `_rails.CageHolds = HoldsCart;`. In `_ExitTree`, set it to null when it is still ours.
- `private bool HoldsCart(int stop)`: for each wagon at `stop` that is not `Open`, count the crew with `WagonOf(c) == wagon && IsInstanceValid(c) && !c.Dead`. A downed crew member is living and still holds the cart. Return `CageHoldRules.Holds(wagon.Open, count)` for the first wagon at that stop.
- `OnCartEvent`: `if (e.Kind is CartEventKind.Boarding or CartEventKind.CageHeld) { WarnLocked(e.Stop); return; }`. "The cage here is still locked." is still said once per stop, now as the cart is held.
- **The win rule.** Add `private bool LeftBehind(CageWagon w) => _rails != null && IsInstanceValid(_rails) && _rails.CrewLeftAt.Contains(w.Stop);`.
  - In `Judge`, replace `if (WagonOf(npc) is { Open: false }) return CrewState.Caged;` with `if (WagonOf(npc) is { Open: false } w) return CrewRules.JudgeCaged(LeftBehind(w));`. The dead are still judged Lost first.
  - In `Tick`, pass `CrewRules.CagesBlocking(<each wagon's (Open, LeftBehind(w))>)` to `CrewRules.Completed` in place of `ClosedCages`. Build the list without allocating each tick (a reused list field). Leave the "waived" check above it on `ClosedCages`.
  - Opening a left cage later frees its crew through today's path (`OpenCage`, then "Let them out" on the siding).
  - `Capture` is unchanged: `LostCrew` still lists only the dead, because `Restore` kills whoever it lists. So the waiver stays unsaved: after a load the cage holds again and its crew are Caged. On a co-op client, the crew HUD shows a left cage's crew as caged, since it learns them from the server's `LostCrew`. That is accepted; there is no wire change.
- Grisel: no new line. `GriselNamesTheRide` is unchanged (on `Departed`).
- Companions' auto-boarding (`HopIn`, `HopInNearby`) is unchanged. Their `StartBoarding(call: true)` is held back like any other start; `??=` keeps the first start's call, so a hop-in never adds an "All aboard!".

- [ ] **Step 7: Build and run the unit suite**

Run: `dotnet build DungeonCrawler.csproj && dotnet test tests/DungeonCrawler.Tests.csproj`
Expected: build succeeds; every test passes (the ~7751 already there plus the new ones).

- [ ] **Step 8: Headless flows: the waive helper and the cage-hold check**

These flows depart intermediate stops with the cage still locked, so they would now stall: --test-ride (stop 1 after its load), --test-ride-edges (stop 1, its first departure and the one after the load), --test-chase section 2 (stop 2 after `Restore(2)`, stop 3 after the heap path), and --test-cart-watchdog (stop 3 after `Restore(3)`).
- Add `internal static void RideWaiveCages(Main main, RailController rc, string tag)` to `DebugTools.Ride.cs`:
  - When `rc.CageHeld` and `main.Party.Local is { } me && me.Cart == rc.Cart`, call `rc.Cart.HoldInteract(main.Party, me)`.
  - Print `[{tag}] leaves the prisoners at stop {k} behind (the cage hold is --test-crew's to check)` once per stop.
- Call it every frame in `RunRideTest`, `RunRideEdgesTest`, `RunChaseTest` and `RunCartWatchdogTest`, at the top of the frame once `rc != null`. `RunRideTest` has no early return on a null `rc`, so guard the call there. A waiver made at the arrival frame starts the countdown just as the arrival used to, with the same `call`, so these flows keep their timings and their AllAboard counts (--test-ride still expects 2).
- --test-crew main path: it opens each cage before it departs, so it needs no waiver. Its countdown now starts when the cage opens instead of when the gate lifts, and `CrewDepart`'s 8 s budget covers that.
- --test-crew section 2 (`RunCrewWreck`) is the single-player proof of the cage hold. The rebuilt floor rides through stop 1 with cage 1 locked. Insert three steps before today's case 1 and renumber the rest:
  - **Held at stop 1.** Wait (60 s budget) until the cart is Held at stop 1 with `rc.GateUp(1)` and the local hero seated, then note the time.
  - **Still held 7 s later** (`Boarding.Countdown + 2`). Fail if the cart rolls or `Boarding.Running` at any frame. At 7 s, assert:
    - `rc.CageHeld`
    - `rc.HudText == "Cart — cage locked"`
    - `rc.Cart.Prompt == CageHoldRules.SeatedPrompt` and `rc.Cart.HoldOffered`
    - the log line "The cage here is still locked." heard exactly once since the rebuild (hook `EventBus.Instance.Message`)

    Then call `rc.Cart.HoldInteract(party, lead)` and print `[crew] 8 held at stop 1 by its locked cage for 7 s (hud "…", prompt "…"); the lead leaves the prisoners behind`.
  - **Departs within 6 s.** Then assert:
    - wagon 1 is not coupled and still locked
    - `rc.CrewLeftAt` contains 1
    - its two crew are `CrewState.Lost` (the win rule)
    - `ctrl.HudText` contains `", 2 lost"`
    - `!ctrl.Completed`

    Print `[crew] 8 departed stop 1 without its prisoners: wagon 1 coupled=False, its crew Lost (hud "…")`.
  - Then today's thin-plating brute between stops 1 and 2.
- `NetTestFlow` (co-op, the RIDE pass):
  - **Phase 1, stop 1 (`NetTestFlow.cs:716`).** Cage 1 is locked here, so the client's prompt is now the hold prompt. Require `prompt == CageHoldRules.SeatedPrompt`, print `[net] ride: at stop 1 the cart offers "<prompt>" here (cage 1 locked)`, and still send the plain tap (`InteractIntent(cartRef)`) to get out.
  - **A helper `void RideLeaveCrew(NetSession net, RailController rc, Adventurer me, string who)`.** Once per stop, when `me.Cart == rc.Cart && rc.CageHeld && rc.Cart.Prompt == CageHoldRules.SeatedPrompt`, send `new InteractIntent(NetRefs.Hold(NetRefs.Cart))` and print `[net] ride: left the prisoners at stop {k} behind through the intent (the prompt read "<prompt>")`.
  - Call the helper every frame for the first client whenever it sits in `rc.Cart`: the RideProps phases 7 and 8, then its steps 52 and 53 (it stays seated at stop 3 for ride 4). Call it for the joiner while it sits in `rc.Cart` (steps 62, 63, 70 and 71). As in single player, the waiver goes out the moment the hold shows, so the stop 2 and stop 3 departures keep today's timing.
- `scripts/net_test.sh`, RIDE pass, new checks next to the stop-1 and stop-2 departure checks:
  - `grep -q "\[net\] ride: at stop 1 the cart offers \"Get out (hold to leave the prisoners behind)\" here (cage 1 locked)" client.log`
  - `grep -hq "\[net\] ride: left the prisoners at stop 2 behind through the intent" client.log joiner.log`
  - `grep -q "\[rails\] the prisoners at stop 2 are left behind (" server.log`
  - `grep -q "\[rails\] the prisoners at stop 3 are left behind (Rider)" server.log`

  Each fails with `NET TEST: FAIL (<what>)`, as the others do.

- [ ] **Step 9: Run every flow**

Run each command and check for its expected line:

| Command | Expected |
|---|---|
| `dotnet build DungeonCrawler.csproj`, then `godot --headless --path . -- --test-crew=condemned` | `[crew] RESULT ok (condemned)`, plus the two new `[crew] 8` lines |
| `godot --headless --path . -- --test-crew=forgiven` | `[crew] RESULT ok (forgiven)` |
| `godot --headless --path . -- --test-ride` | `[ride] RESULT ok` |
| `godot --headless --path . -- --test-ride-edges` | `[ride-edges] RESULT ok` |
| `godot --headless --path . -- --test-chase` | `[chase] RESULT ok` |
| `godot --headless --path . -- --test-cart-watchdog` | `[watchdog] RESULT ok` |
| `scripts/net_test.sh` | `NET TEST: ok` |
| `START_FLOOR=0 scripts/net_test.sh` | `NET TEST: ok` |
| `RIDE=1 scripts/net_test.sh` (alone) | `NET TEST: ok` |

A flow that stalls at a stop with the cage locked gets `RideWaiveCages` (single player) or `RideLeaveCrew` (co-op) at that point. Name each such place in the commit message.

- [ ] **Step 10: Docs**

In `docs/development.md`:
- **"Stops, cages and the crew".** Replace the sentence "A boarding that starts at a stop whose cage is still locked logs …" with a paragraph on the cage hold. Cover:
  - `CageHoldRules.Held`, and "Cart — cage locked" (the gate is shown first).
  - Living crew hold the cart (downed counts as living); dead crew do not.
  - Holding E for 1 s, or the VR grip, with the prompt "Get out (hold to leave the prisoners behind)".
  - The log line, and that the held-back countdown starts with its own call.
  - That this applies in every mode.
  - That the waiver is not saved.
  - **The win rule:** a left cage's crew count as lost and their cage does not block the stairs. Opening it later on foot frees them to follow on foot. After a load the cage holds again, because the waiver is not saved. Update "The win." paragraph to match.
- **"Riding together".** Add `CartFlags.CageHeld` (128) and the `hold:<ref>` intent (`NetRefs.HoldKind`). Note that the protocol is unchanged.
- **Controls table.** Add the row `E (hold 1 s, VR grip) | Seated at a stop whose cage is locked: leave the prisoners behind (a tap still gets out)`.
- **`--test-crew` paragraph.** Add the section-2 cage-hold check.
- **The RIDE net test section.** Add the new checks.

- [ ] **Step 11: Commit**

```bash
git add src/ docs/development.md scripts/net_test.sh   # include the new .cs.uid files Godot generated
git commit -m "feat(rails): the cage hold: no countdown at a stop whose cage is locked; hold E to leave the prisoners behind, who then count as lost (CartFlags.CageHeld, hold:<ref>)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The relief cart (single player)

**Deliverable:** Solo, a player left behind gets a relief cart about 15 s later.
- It comes from the stop behind them and stops beside them, or a little past them after running them over on the rails.
- It waits 30 s, then goes, and another comes 15 s later.
- Once boarded, it runs up behind the train and sets them down.
- It never comes online, and never comes while the cart stands where the player left it or the player is ahead of the train.

`--test-relief` proves it in four variants.

**Files:**
- Create `src/Dungeon/Rails/Core/ReliefRules.cs` (`ReliefPhase`, `ReliefView`, `ReliefRules`, `ReliefClock`)
- Create `src/Dungeon/Rails/RailController.Relief.cs`
- Create `src/Core/DebugTools.Relief.cs`
- Modify `src/Dungeon/Rails/RailController.Chase.cs` (the `Chase` class, `TickChase`, `ChaseRunOver`, `EndChase`)
- Modify `src/Dungeon/Rails/RailController.cs` (`Tick`, `Restore`, `ChaserHud`, `HudState`)
- Modify `src/Dungeon/Rails/Core/RailHud.cs`
- Modify `src/Dungeon/Rails/Core/RunOverRules.cs` (make `OnTheRails` public as `OnRails`)
- Modify `src/Dungeon/Rails/TrackSampler.cs` (`ClosestS`)
- Modify `src/Core/DebugTools.cs` (the flag, the doc list, the automation list, the dispatch) and `src/Core/DebugTools.RideEdges.cs`
- Modify `src/Net/NetTestFlow.cs` and `scripts/net_test.sh` (the online guard)
- Modify `docs/development.md`
- Tests: create `tests/Dungeon/Rails/ReliefRulesTests.cs`; modify `tests/Dungeon/Rails/RailHudTests.cs`. `OnTheRails` is private, so no test references it.

**Interfaces:**
- Consumes:
  - Task 1's `RailHudState` (append after `CageHeld`)
  - Plan 5's `SpareRules.ChaseLimit`, `ChaseRolls`, `HoleLimit`, `HeapLimit`, `Hop`, `ChaserPrompt`, `ChaseGap` and `HideSeconds`
  - `Boarding`, `RideInputRules.BoardPrompt` and `InBoardReach`
- Produces:
  - **`ReliefRules` and `ReliefClock`**, as below.
  - **Test hooks on `RailController`, all `internal`:**
    - `int ReliefsSent`
    - `int ReliefHits`: times a relief ran over the local player. Companions, crew and enemies are not counted.
    - `bool LeftBehindNow`
    - `ReliefPhase? ReliefPhaseNow`
    - `MineCart? ReliefCart`
    - `float ReliefRunS` (NaN when none)
    - `int ReliefSpawnStop` (−1 when none)
    - `float PlayerLineS` (NaN when none)

    `ReliefsSent`, `ReliefHits` and `ReliefSpawnStop` are reset in `Restore`. `ReliefSpawnStop` also goes back to −1 when a relief ends.
  - **`DebugTools.NoRelief`** (`public bool { get; set; }`)

- [ ] **Step 1: Write the failing tests** (`tests/Dungeon/Rails/ReliefRulesTests.cs`)

The helper: `static ReliefView V(bool solo = true, bool live = true, bool up = true, bool seated = false, bool standing = false, bool met = false, float behind = 40f) => new(solo, live, up, seated, standing, met, behind);`

```csharp
[Fact] public void Timings() { Assert.Equal(15f, ReliefRules.FirstWait); Assert.Equal(30f, ReliefRules.WaitBeside); Assert.Equal(15f, ReliefRules.NextWait); Assert.Equal(12f, ReliefRules.LeftBehindRange); }
[Fact] public void LeftBehind_SoloOnFootFarBehind() => Assert.True(ReliefRules.LeftBehind(V()));
[Fact] public void LeftBehind_JustOverTheRange() => Assert.True(ReliefRules.LeftBehind(V(behind: 12.01f)));
[Fact] public void LeftBehind_WithinTheRange_No() => Assert.False(ReliefRules.LeftBehind(V(behind: 12f)));
[Fact] public void LeftBehind_Online_No() => Assert.False(ReliefRules.LeftBehind(V(solo: false)));
[Fact] public void LeftBehind_LineDoneOrWreckedForGood_No() => Assert.False(ReliefRules.LeftBehind(V(live: false)));
[Fact] public void LeftBehind_Downed_No() => Assert.False(ReliefRules.LeftBehind(V(up: false)));
[Fact] public void LeftBehind_SeatedInAnyCart_No() => Assert.False(ReliefRules.LeftBehind(V(seated: true)));
[Fact] public void LeftBehind_AheadOfTheTrain_No() => Assert.False(ReliefRules.LeftBehind(V(behind: -30f)));
[Fact] public void LeftBehind_TheCartStandsWhereYouLeftIt_No() => Assert.False(ReliefRules.LeftBehind(V(standing: true, met: true)));
[Fact] public void LeftBehind_TheCartRolledOnWithoutYou_Yes() => Assert.True(ReliefRules.LeftBehind(V(standing: true, met: false))); // the jump: it stands at the next stop it never met you at
// LineLive(stopCount, stopIndex, mode, halted, hidden)
[Fact] public void LineLive_HeldAtTheLastStop_No() => Assert.False(ReliefRules.LineLive(5, 4, CartMode.Held, false, false));
[Fact] public void LineLive_RollingToTheLastStop() => Assert.True(ReliefRules.LineLive(5, 3, CartMode.Rolling, false, false));
[Fact] public void LineLive_Wrecked_No() => Assert.False(ReliefRules.LineLive(5, 2, CartMode.Wrecked, false, false));
[Fact] public void LineLive_HiddenWreck_No() => Assert.False(ReliefRules.LineLive(5, 2, CartMode.Held, false, true));
[Fact] public void LineLive_NoLine_No() => Assert.False(ReliefRules.LineLive(0, 0, CartMode.Held, false, false));
// Met(met, cartRolling, seatedInParty, behind)
[Fact] public void Met_ClearedWhileTheCartRolls() => Assert.False(ReliefRules.Met(true, true, false, 0f));
[Fact] public void Met_SeatedInTheStandingCart() => Assert.True(ReliefRules.Met(false, false, true, 50f));
[Fact] public void Met_WithinRangeOfTheStandingTrain() => Assert.True(ReliefRules.Met(false, false, false, 12f));
[Fact] public void Met_ACartThatArrivedWithoutYou_No() => Assert.False(ReliefRules.Met(false, false, false, 40f));
[Fact] public void Met_KeptWhileItStands() => Assert.True(ReliefRules.Met(true, false, false, 100f));
[Fact] public void Behind_IsTheLastBackEdgeMinusThePlayer() => Assert.Equal(30f, ReliefRules.Behind(lastBackS: 110f, playerS: 80f));
// stops at S = 0, 50, 120, 200, 260
[Theory, InlineData(80f, 1), InlineData(50f, 1), InlineData(49.9f, 0), InlineData(300f, 4), InlineData(-5f, 0)]
public void SpawnStop_TheLastStopAtOrBehindThePlayer(float playerS, int stop) => Assert.Equal(stop, ReliefRules.SpawnStop(new[] { 0f, 50f, 120f, 200f, 260f }, playerS));
[Fact] public void SpawnS_AtTheStop() => Assert.Equal(50f, ReliefRules.SpawnS(50f, 80f, 300f));
[Fact] public void SpawnS_ClearOfThePlayer() => Assert.Equal(48.5f, ReliefRules.SpawnS(50f, 51f, 300f), 3); // 51 − 1.1 − 0.4 − 1.0
[Fact] public void SpawnS_NeverPastTheChaseLimit() => Assert.Equal(40f, ReliefRules.SpawnS(50f, 80f, 40f));
[Fact] public void SpawnS_NeverBeforeTheLine() => Assert.Equal(0f, ReliefRules.SpawnS(0f, 1f, 300f));
[Fact] public void SeatS_BesideYou() => Assert.Equal(80f, ReliefRules.SeatS(50f, 80f, onRails: false));
[Fact] public void SeatS_OnTheRails_StopsPast() => Assert.Equal(83f, ReliefRules.SeatS(50f, 80f, onRails: true), 3); // 80 + 1.1 + 0.4 + 1.5
[Fact] public void SeatS_StopClose_WaitsThere() { Assert.Equal(50f, ReliefRules.SeatS(50f, 56f, false)); Assert.Equal(56.1f, ReliefRules.SeatS(50f, 56.1f, false), 3); }
[Fact] public void SeatS_StopClose_OnTheRails_WaitsThere() => Assert.Equal(50f, ReliefRules.SeatS(50f, 54f, true));
[Fact] public void Target_NeverMovesBack() { Assert.Equal(83f, ReliefRules.Target(83f, 80f)); Assert.Equal(85f, ReliefRules.Target(80f, 85f)); }
// Next(phase, held, playerAboard, waited)
[Fact] public void Next_ComingStops_Waits() => Assert.Equal(ReliefPhase.Waiting, ReliefRules.Next(ReliefPhase.Coming, true, false, 0f));
[Fact] public void Next_ComingRolls_Comes() => Assert.Equal(ReliefPhase.Coming, ReliefRules.Next(ReliefPhase.Coming, false, false, 0f));
[Fact] public void Next_WaitingBoarded_Rides() => Assert.Equal(ReliefPhase.Riding, ReliefRules.Next(ReliefPhase.Waiting, true, true, 5f));
[Fact] public void Next_WaitingUnder30s_Waits() => Assert.Equal(ReliefPhase.Waiting, ReliefRules.Next(ReliefPhase.Waiting, true, false, 29.9f));
[Fact] public void Next_Waiting30s_Goes() => Assert.Equal(ReliefPhase.Leaving, ReliefRules.Next(ReliefPhase.Waiting, true, false, 30f));
[Fact] public void Next_RidingGotOutStanding_WaitsAgain() => Assert.Equal(ReliefPhase.Waiting, ReliefRules.Next(ReliefPhase.Riding, true, false, 0f));
[Fact] public void Next_RidingRolling_Rides() => Assert.Equal(ReliefPhase.Riding, ReliefRules.Next(ReliefPhase.Riding, false, false, 0f));
[Fact] public void Next_RidingHeldWithThePlayer_Rides() => Assert.Equal(ReliefPhase.Riding, ReliefRules.Next(ReliefPhase.Riding, true, true, 0f)); // held short of the train at a heap face or a lip, rider aboard
// SeatLimit(limit, target, holes): the seat never stands over a gap or trestle span
[Fact] public void SeatLimit_ClearOfHoles_IsTheTarget() => Assert.Equal(106f, ReliefRules.SeatLimit(300f, 106f, new[] { (100f, 104f) }), 3); // back end 104.9 clears the far lip
[Fact] public void SeatLimit_OverAHole_StopsShortOfTheNearLip() => Assert.Equal(98.9f, ReliefRules.SeatLimit(300f, 104.5f, new[] { (100f, 104f) }), 3); // 100 − 1.1
[Fact] public void SeatLimit_TheTrainCloser_IsTheLimit() => Assert.Equal(90f, ReliefRules.SeatLimit(90f, 120f, new[] { (100f, 104f) }), 3);
[Fact] public void Next_Leaving_Stays() => Assert.Equal(ReliefPhase.Leaving, ReliefRules.Next(ReliefPhase.Leaving, true, true, 0f));
// Rolls(phase, boarded, s, limit)
[Fact] public void Rolls_ComingTowardYou() => Assert.True(ReliefRules.Rolls(ReliefPhase.Coming, false, 10f, 11f));
[Fact] public void Rolls_ComingAtItsSeat_No() => Assert.False(ReliefRules.Rolls(ReliefPhase.Coming, false, 10f, 10.04f));
[Fact] public void Rolls_Waiting_No() => Assert.False(ReliefRules.Rolls(ReliefPhase.Waiting, true, 10f, 50f));
[Fact] public void Rolls_RidingInTheCountdown_No() => Assert.False(ReliefRules.Rolls(ReliefPhase.Riding, false, 10f, 50f));
[Fact] public void Rolls_RidingBoarded() => Assert.True(ReliefRules.Rolls(ReliefPhase.Riding, true, 10f, 50f));
[Fact] public void Rolls_Leaving() => Assert.True(ReliefRules.Rolls(ReliefPhase.Leaving, false, 10f, 50f));
// Prompt(phase, held, seatedHere, onFootPlayer, inReach, downed, boardingLeft)
[Fact] public void Prompt_WaitingInReach_Board() => Assert.Equal("Board the cart", ReliefRules.Prompt(ReliefPhase.Waiting, true, false, true, true, false, -1f));
[Fact] public void Prompt_WaitingOutOfReach_Nothing() => Assert.Equal("", ReliefRules.Prompt(ReliefPhase.Waiting, true, false, true, false, false, -1f));
[Fact] public void Prompt_ComingOrLeaving_Nothing() { Assert.Equal("", ReliefRules.Prompt(ReliefPhase.Coming, false, false, true, true, false, -1f)); Assert.Equal("", ReliefRules.Prompt(ReliefPhase.Leaving, false, false, true, true, false, -1f)); }
[Fact] public void Prompt_SeatedStanding_GetOut() => Assert.Equal("Get out", ReliefRules.Prompt(ReliefPhase.Riding, true, true, false, true, false, 2.5f));
[Fact] public void Prompt_SeatedFirstTwoSeconds_Nothing() => Assert.Equal("", ReliefRules.Prompt(ReliefPhase.Riding, true, true, false, true, false, 4.5f));
[Fact] public void Prompt_SeatedRolling_Nothing() => Assert.Equal("", ReliefRules.Prompt(ReliefPhase.Riding, false, true, false, true, false, -1f));
// ReliefClock: Tick(dt, leftBehind, counting) at 1/60 s
[Fact] public void Clock_SendsFifteenSecondsAfterLeftBehind() { /* 899 ticks: never true; the 900th: true; Sent == 1 */ }
[Fact] public void Clock_RestartsWhenNoLongerLeftBehind() { /* 10 s left behind, 1 tick not, 10 s left behind: never true */ }
[Fact] public void Clock_DoesNotCountWhileAReliefIsOut() { /* counting false for 60 s: never true */ }
[Fact] public void Clock_NextFifteenSecondsAfterOneWent() { /* send one, Went(), 899 ticks false, the 900th true; Sent == 2 */ }
```

RailHudTests:
```csharp
[Fact] public void Relief_Countdown() => Assert.Equal("Relief cart — departs in 3", RailHud.Text(At() with { InChaser = true, ChaserRelief = true, ChaserBoardingLeft = 2.2f }));
[Fact] public void Relief_Rolling() => Assert.Equal("Relief cart — after the train", RailHud.Text(At() with { InChaser = true, ChaserRelief = true, ChaserMode = CartMode.Rolling }));
[Fact] public void Relief_Standing() => Assert.Equal("Relief cart — behind the train", RailHud.Text(At() with { InChaser = true, ChaserRelief = true }));
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~ReliefRulesTests|FullyQualifiedName~RailHudTests"`
Expected: build errors (`ReliefRules`, `ChaserRelief` not defined).

- [ ] **Step 3: Implement `src/Dungeon/Rails/Core/ReliefRules.cs`**

```csharp
public enum ReliefPhase : byte { Coming, Waiting, Riding, Leaving }
public readonly record struct ReliefView(bool Solo, bool LineLive, bool PlayerUp, bool PlayerSeated, bool CartStanding, bool Met, float Behind);
```

`public static class ReliefRules`:
- **Constants:** `FirstWait = 15f`, `WaitBeside = 30f`, `NextWait = 15f`, `LeftBehindRange = 12f`, `WaitAtStopRange = 6f`, `PastRunOver = 1.5f`, `SpawnClear = 1f`, and `BodyRadius = 0.4f`, with a doc saying it mirrors `RunOverSweep.BodyRadius` on the Godot side.
- **`LineLive(int stopCount, int stopIndex, CartMode mode, bool halted, bool hidden)`:** at least two stops, not hidden, not Wrecked, and not Held, unhalted, at the last stop.
- **`Behind(float lastBackS, float playerS) => lastBackS - playerS`**
- **`Met(bool met, bool cartRolling, bool seatedInParty, float behind)`:** `!cartRolling && (met || seatedInParty || behind <= LeftBehindRange)`. "The standing cart has met the player since it stopped: it waits for them."
- **`LeftBehind(in ReliefView v)`:** `Solo && LineLive && PlayerUp && !PlayerSeated && !(CartStanding && Met) && Behind > LeftBehindRange`
- **`SpawnStop(IReadOnlyList<float> stopS, float playerS)`**
- **`SpawnS(float stopS, float playerS, float chaseLimit)`:** `Math.Max(0, Math.Min(Math.Min(stopS, playerS - CartRules.HalfLength - BodyRadius - SpawnClear), chaseLimit))`
- **`SeatS(float spawnS, float playerS, bool onRails)`:** `spawnS` when `playerS - spawnS <= WaitAtStopRange`; otherwise `playerS + HalfLength + BodyRadius + PastRunOver` on the rails, else `playerS`.
- **`Target(float current, float seatS) => Math.Max(current, seatS)`**
- **`SeatLimit(float limit, float target, IReadOnlyList<(float From, float To)> holes) => SpareRules.HoleLimit(Math.Min(limit, target), holes)`:** the Coming relief's limit. A seat over a gap or trestle span is pulled back short of its near lip.
- **`Next(ReliefPhase phase, bool held, bool playerAboard, float waited)`:** follows the tests above.
- **`Rolls(ReliefPhase phase, bool boarded, float s, float limit)`:** Coming and Leaving use `SpareRules.ChaseRolls(true, s, limit)`; Riding uses `SpareRules.ChaseRolls(boarded, s, limit)`; Waiting is false.
- **`Prompt(ReliefPhase phase, bool held, bool seatedHere, bool onFootPlayer, bool inReach, bool downed, float boardingLeft)`:**
  - When seated here: `SpareRules.ChaserPrompt(true, held, downed, boardingLeft)`.
  - When Waiting with an on-foot player in reach: `RideInputRules.BoardPrompt(false, true, false, true, true)`.
  - Otherwise `""`.

`public sealed class ReliefClock`:
- `float Behind { get; }` and `int Sent { get; }`
- `bool Tick(float dt, bool leftBehind, bool counting)`:
  - When `!leftBehind`, set Behind to 0.
  - Otherwise, while `counting`, add dt.
  - At `Behind >= due - 1e-4f` (`FirstWait`, or `NextWait` after `Went()`), return true once, set Behind to 0 and increment Sent. The tolerance makes 900 ticks of 1/60 s count as 15 s.
- `void Went()`
- `void Reset()`, which also sets Sent to 0.

In `RailHud`: append `bool ChaserRelief = false` to `RailHudState`. In the `InChaser` branch, the name is `"Relief cart"` when `ChaserRelief`, else `"Spare cart"`.

- [ ] **Step 4: Run them and see them pass**

Same command as Step 2. Expected: PASS.

- [ ] **Step 5: The relief cart in the world**

**`Chase` gains:**
- `public bool Relief;`
- `public ReliefPhase Phase;`
- `public float Waited;` (seconds in Waiting)
- `public float SpawnS;`
- `public float Target;`

**Plan 5 code made relief-aware:**
- `EndChase`: `_spares.GiveBack` only when `!c.Relief`. A relief never spends or gives back a spare.
- `ChaseRunOver`: after the judge, `if (c.Relief)` add one to `ReliefHits` for each hit whose body is the local player (`Main.Instance.Party.Local`). Companions, crew and enemies do not count.
- `CollectChaser` and `ChaserPlaced`: a relief is never collected and never raises `ChaserPlaced`; it is solo only.
- `ChaserHud`: gains `out bool relief`. Thread it through `RefreshHud` into `HudState`, which passes `ChaserRelief`.

**`TrackSampler.ClosestS(Vector3 pos)`:** `Track.Curve.GetClosestOffset(Track.ToLocal(pos))`.

**`RunOverRules`:** rename the private `OnTheRails` to `public static bool OnRails(in TrackBody b)`; behaviour is unchanged.

**`src/Dungeon/Rails/RailController.Relief.cs`** (a partial). Fields: `ReliefClock _reliefClock = new()`, `bool _met` (starts **false**: see the settled values), `bool _playerOnRails` (the last tick's rails test). Methods:

- **`private void TickRelief(PartyController party, float dt)`.** It is called in `Tick` right after `TickChase`. It returns at once when `NetSession.IsNetworked`, when `DebugTools.Instance?.NoRelief == true`, or when `StopS.Length < 2`.
  - The player: `if (party.Local is not { } me || !IsInstanceValid(me)) { PlayerLineS = float.NaN; LeftBehindNow = false; return; }`.
  - Their line position: `PlayerLineS = Party.Track.ClosestS(me.GlobalPosition)`. Then `Party.Track.Measure(me.GlobalPosition, PlayerLineS, 2f, out var b)`, and `_playerOnRails = RunOverRules.OnRails(b with { Radius = RunOverSweep.BodyRadius })`.
  - The train's back: `lastBackS = SpareRules.TrainBackS(Party.BackS, _train.Count, WagonSpacing) + CartRules.HalfLength`, and `behind = ReliefRules.Behind(lastBackS, PlayerLineS)`.
  - `_met = ReliefRules.Met(_met, State.Mode == CartMode.Rolling, me.Cart == Cart, behind)`.
  - `LeftBehindNow = ReliefRules.LeftBehind(new ReliefView(true, ReliefRules.LineLive(StopS.Length, StopIndex, State.Mode, Halted, !Cart.Visible), !me.Downed && !me.Parked, me.Seated, State.Mode != CartMode.Rolling, _met, behind))`.
  - Counting runs while no chase is out, while the chase has ended, or while it is a relief in Leaving: `counting = _chase is null or { Ended: true } or { Relief: true, Phase: ReliefPhase.Leaving }`.
  - `if (_reliefClock.Tick(dt, LeftBehindNow, counting)) SendRelief(me, PlayerLineS, _playerOnRails);`

- **`private void SendRelief(Adventurer me, float playerS, bool onRails)`:**
  - `FreeChaser()` any chaser still standing.
  - `stop = ReliefRules.SpawnStop(StopS, playerS)`.
  - `s = SpareRules.HoleLimit(ReliefRules.SpawnS(StopS[stop], playerS, ChaseLimitNow()), holes)`, with the holes from `CollectPartyLaneHoles`.
  - `new MineCart { Name = $"ReliefCart{_reliefClock.Sent}", CartId = CartIds.Chaser(stop), PromptFor = ReliefPromptFor, OnUse = UseRelief, CollisionLayer = ChaserHeldLayer }.AsChaser()`.
    - The name is unique, because Godot would rename a second `ReliefCart` added while the last one is still queued for deletion.
    - It is created solid and usable (`ChaserHeldLayer`), as `StartChase` does. A relief that spawns within `WaitAtStopRange` never rolls, and the look ray sees only layer 8, so it could never be boarded otherwise.
    - Only the change to Rolling sets layer 0.
  - A `CartRun` Held at `s` with `Limit = s`; `PoseAt`, `Sync` and `ChaserPlacedSound`, as `StartChase` does.
  - `_chase = new Chase { Relief = true, Phase = ReliefPhase.Coming, Stop = stop, SpawnS = s, Target = ReliefRules.SeatS(s, playerS, onRails), Boarded = false }`.
  - Clear `_chaseHitAt` and `_chaseWarned`.
  - Set `ReliefsSent = _reliefClock.Sent` and `ReliefSpawnStop = stop`. The run-over source and `HopHoles`' print keep the chaser's "spare cart" words (ruled). The relief logs its own lines below and never co-op's "The spare cart rolls after the train.".
  - Log "A relief cart comes down the line." and `GD.Print($"[rails] relief {n}: sent from stop {stop} at S={s:0.0} for the player at S={playerS:0.0} ({(onRails ? "on the rails" : "beside the line")}), seat at S={target:0.0}")`.

- **`TickChase` relief branch.** It runs when `c.Relief && !c.Ended`, after the shared `limit` (train, heap, holes) is computed, and replaces the co-op `!c.Boarded` / `ChaseRolls` / `ChaseDone` block.
  - **Coming.** When `PlayerLineS` is not NaN, `c.Target = ReliefRules.Target(c.Target, ReliefRules.SeatS(c.SpawnS, PlayerLineS, _playerOnRails))`. These are the previous tick's values, because `TickRelief` runs after `TickChase`. Then `limit = ReliefRules.SeatLimit(limit, c.Target, _chaseHoles)`, so the seat never stands over a gap or trestle span.
  - **Waiting.** `c.Waited += dt`. It does not roll.
  - **Riding.** The chaser's countdown (`CountChasers`, `c.Boarding.Tick`, `c.Boarded`), **without** co-op's `if (seated == 0) { EndChase(atOnce: true); return; }`. A relief with no player in it goes back to Waiting; it never ends.
  - **Leaving.** It uses the chase limit.
  - **Rolling.** When `ReliefRules.Rolls(c.Phase, c.Boarded, s, limit)` holds: Rolling, `CollisionLayer = 0`, and `c.Departed = true` (that field is unused for a relief otherwise, because `GiveBack` is guarded). While it rolls it shares the co-op code: `run.Limit = max(limit, S)`, `Step`, `HopHoles`, `ChaseRunOver`, `WarnPartyLane`. Once it is Held: `Settle` and `CollisionLayer = ChaserHeldLayer`.
  - **The phase.** Then `c.Phase = ReliefRules.Next(c.Phase, held, PlayerIn(c.Cart), c.Waited)`. On a change:
    - **→ Waiting:** `c.Waited = 0`, `c.Boarded = false`, `c.Boarding.Cancel()`.
      - From Riding (no player left in it), `SetDownChasers(c.Cart)` sets down every rider still in it: escorts on the bench, a downed player, companions. The bench never rides up behind the train without the player.
      - Log "The relief cart waits for you." and GD.Print the relief's S, the player's S and its tub distance.
    - **→ Leaving:** `_reliefClock.Went()`, `c.Departed = false`, `CollisionLayer = 0`. Log "The relief cart rolls on without you."
  - **Riding: done.** When `SpareRules.ChaseDone(c.Boarded, held, State.Mode == Held, s, trainLimit)` holds, do what the co-op chase does: `LastChaseGap`, `ChasesDone++`, and `EndChase(atOnce: false)`. Log "The relief cart pulls up behind the train."
  - **Leaving: done.** Evaluate this after the roll, never in the tick it turned Leaving. End it with `EndChase(atOnce: false)` (hidden after `HideSeconds`) only when `c.Phase == Leaving && c.Departed && held`. A Leaving relief that is Held and cannot roll (`!ReliefRules.Rolls(...)`: its limit is not ahead) ends at once.
  - **On any end:** `ReliefSpawnStop = -1`.

- **`private string ReliefPromptFor(Adventurer hero)`:** `ReliefRules.Prompt(c.Phase, held, hero.Cart == c.Cart, onFootPlayer: IsPlayer(hero) && !hero.Seated && !hero.Downed, inReach: RideInputRules.InBoardReach(c.Cart.TubDistance(hero)), hero.Downed, ChaserBoardingLeft)`. Return `""` when there is no live relief.

- **`private void UseRelief(Adventurer actor)`** (ends in `Flush()`). Re-check the prompt first.
  - **"Board the cart":**
    - `SeatIn(cart, actor)`.
    - The actor's own companions on foot within `CompanionBoardRange`, as `StartChase` seats them.
    - `SeatBench(cart, actor.GlobalPosition)`.
    - `c.Boarding.Start()`, `c.Phase = ReliefPhase.Riding`.
    - Log "{name} climbs into the relief cart."
  - **"Get out":** `SetDownFromChaser` for the actor and their own companions. With no player left in it, the relief goes back to Waiting through `Next` on the next tick, which sets every remaining rider down. It never ends the chase or gives a spare back.

- **`Restore`:** next to the existing `EndChase(atOnce: true); FreeChaser();` near its top, before the client and empty-line returns, add `_reliefClock.Reset(); _met = false; ReliefsSent = 0; ReliefHits = 0; ReliefSpawnStop = -1;`. `EndChase` and `FreeChaser` already remove a relief.
- **After a wreck (ruled):** nothing in `Wreck()` touches `_met`. A spare that takes over at a stop more than 12 m ahead of the set-down player leaves them left behind, and a relief comes.

- **Test hooks:**
  - `ReliefPhaseNow => _chase is { Relief: true, Ended: false } c ? c.Phase : null`
  - `ReliefCart`
  - `ReliefRunS`

**`DebugTools.cs` and `DebugTools.RideEdges.cs`:**
- `DebugTools.NoRelief`: --test-ride-edges sets it to `true` just before its throw (step 6). That flow pins the party cart's own run-over ("hit once, not again") and waits up to 66 s afterwards with the thrown human on the line, so a relief would run them over and break the check. The relief has its own flow. Set it back to false at RESULT.
- **The audit of the other solo flows.** A flow needs `NoRelief` if it leaves the local player up, on foot and more than 12 m behind a standing cart it has not met (or a rolling one) for 15 s:
  - --test-ride: its RESULT comes about 1 s after its wreck.
  - --test-crew: the player rides or stands at the line's end (`LineLive` false), and section 2 ends 0.5 s after its last wreck.
  - --test-chase: the lead leaves the seat only to swing at the heap beside the halted cart it met. Its traffic heroes are companions. Each `Restore(k)` shortcut boards again within 1 s.
  - --test-cart-watchdog: seated throughout. `Restore(3)` boards again 1 s later.

  None of these needs `NoRelief`. The Step 8 rerun is the safety net: grep each flow's output for `[rails] relief`. A flow that trips a relief either asserts it or sets `NoRelief`, and the commit message names it.

- [ ] **Step 6: Build and run the unit suite**

Run: `dotnet build DungeonCrawler.csproj && dotnet test tests/DungeonCrawler.Tests.csproj`
Expected: build succeeds, all pass.

- [ ] **Step 7: Write `--test-relief[=beside|rails|refuse|ahead]` (`src/Core/DebugTools.Relief.cs`)**

**Parsing (in `DebugTools.cs`, next to `--test-ride-edges`):**
- The variant defaults to `beside`. An unknown variant prints an error and falls back to `beside`.
- The flag sets `_builtin = true; _startFloor = 12; _floorGiven = true; ForcedLethality ??= 1f; NoAmbush = true; NoPortals = true; NoGathering = true; TraceAi = false; _quitAfter = 200;`.
- Add `_testRelief` to the automation list (line 455).
- Dispatch with `if (_testRelief && !ScriptStalled("relief", _relStep)) RunReliefTest();`.
- Add a doc line to the header list.

**Every frame**, cull enemies as --test-ride-edges does. Print `[relief]` lines and end with `[relief] RESULT ok (<variant>)`. A failure prints `[relief] RESULT FAIL: …` and exits with code 1.

**The common opening** (beside, rails, refuse):
- `RideBoard` at the landing.
- Fail if `StopS[1] - StopS[0] < 45`.
- When the cart is Rolling with `S >= StopS[0] + 25` and `V >= 4`, call `rc.Cart.Interact(party, me)`, which throws the player.
- Then place the player:
  - **beside / refuse:** teleport to `rc.StandBesideLane(me.GlobalPosition)`.
  - **rails:** teleport onto the line at their S (`Party.Track.Point(rc.PlayerLineS)`, +0.05 up).
- Set HP to max.
- **R1.** `rc.LeftBehindNow` within 6 s; note `t0`.
- **R2.** `rc.ReliefsSent` becomes 1 at `t0 + 15 s ± 0.5 s`, with `rc.ReliefSpawnStop == 0`.

**beside:**
- **R3.** Within 25 s of the send, `rc.ReliefPhaseNow == Waiting`, with:
  - `|rc.ReliefRunS - rc.PlayerLineS| <= 0.6`
  - `rc.ReliefCart.TubDistance(me) <= RideInputRules.BoardReach`
  - `rc.ReliefCart.Prompt == "Board the cart"`
  - `rc.ReliefHits == 0`, and HP unchanged
- **R4.** `rc.ReliefCart.Interact(party, me)` gives `me.Cart == rc.ReliefCart`, phase Riding, and within 1 s `rc.HudText` starting with `"Relief cart — departs in"`.
- **R5.** Within 90 s, all of these hold:
  - `rc.ChasesDone == 1`
  - `!me.Seated`, not on a rail cell
  - `rc.LastChaseGap` in `[0, 3]` (the RIDE pass's bound)
  - the party cart Held at stop 1

  Then `rc.ReliefCart == null` within 3 s.
- **R6.** `!rc.LeftBehindNow` within 2 s. `RideBoard` puts `me` in `rc.Cart`. 2 s later `rc.ReliefsSent == 1` still.

**rails:**
- **R3'.** Within 25 s of the send, all of these hold:
  - `rc.ReliefHits >= 1`
  - the player lost `HazardRules.DamageFraction(1f)` ± 0.02 of max HP, once, and is not downed
  - phase Waiting with `rc.ReliefRunS >= hitS + CartRules.HalfLength + ReliefRules.BodyRadius + ReliefRules.PastRunOver - 0.1`, where `hitS` is `rc.PlayerLineS` at the frame `ReliefHits` first rose
- Then board as R4 (teleport beside the relief first when it is out of reach) and finish as R5.

**refuse:**
- R3 as in beside, then do not board.
- **R7.** At Waiting + 30 s ± 0.5 s, phase Leaving with the relief rolling and `rc.ReliefHits == 0`.
- **R8.** At Leaving + 15 s ± 0.5 s, `rc.ReliefsSent == 2`; the second reaches Waiting beside the player within 25 s, as R3.
- At every frame there is at most one MineCart named `ReliefCart*` that is not queued for deletion under the floor root.
- **R9 (Review Focus 1).** While the second waits, `main.SaveGame("relieftest")` then `main.LoadGame("relieftest")`. After 1 s, re-read `RailController.Instance` (a load may rebuild the floor) and check:
  - no `ReliefCart*` node
  - `!me.Seated`
  - `rc.ReliefsSent == 0`
  - the cart Held at a stop
- **R10.** The player is still more than 12 m behind the train after the load, and `_met` starts false, so `rc.ReliefsSent == 1` at the load + 15 s ± 0.5 s. A load never strands the player.

**ahead:**
- Board at the landing and ride to stop 1, seated (no jump). Fail if `StopS[2] - StopS[1] < 40` or `StopS[1] - StopS[0] < 40`.
- Held at stop 1: `rc.Cart.Interact(party, me)` (a tap gets out).
- Teleport to `rc.StandBesideLane(Party.Track.Point(StopS[1] + 30))`. For 20 s: `rc.ReliefsSent == 0` and `!rc.LeftBehindNow`.
- Teleport to `rc.StandBesideLane(Party.Track.Point(StopS[1] - 30))`. For 20 s: `rc.ReliefsSent == 0`. The cart stands where you left it.

- [ ] **Step 8: Run the relief flows, then every flow again**

| Command | Expected |
|---|---|
| `dotnet build DungeonCrawler.csproj` | — |
| `godot --headless --path . -- --test-relief=beside` | `[relief] RESULT ok (beside)` |
| `godot --headless --path . -- --test-relief=rails` | `[relief] RESULT ok (rails)` |
| `godot --headless --path . -- --test-relief=refuse` | `[relief] RESULT ok (refuse)` |
| `godot --headless --path . -- --test-relief=ahead` | `[relief] RESULT ok (ahead)` |
| Task 1 Step 9's six single-player flows (crew ×2, ride, ride-edges, chase, watchdog) | each prints its `RESULT ok` line, and its output has no `[rails] relief` line unless the flow asserts it |

- [ ] **Step 9: The online guard in the RIDE pass**

- In `NetTestFlow`'s server half, in the `chase-done` print block (`NetTestFlow.cs:447`), add `GD.Print($"[net-server] ride: relief carts sent online: {rc.ReliefsSent}")`.
- In `scripts/net_test.sh` (RIDE pass), add: `grep -q "\[net-server\] ride: relief carts sent online: 0" server.log || { echo "NET TEST: FAIL (a relief cart was sent online)"; exit 1; }`
- This is cheap insurance (ruled): a tripwire for someone who removes the `!NetSession.IsNetworked` guard, not proof of the guard.

Run in turn, each alone, and expect `NET TEST: ok` from each:
- `scripts/net_test.sh`
- `START_FLOOR=0 scripts/net_test.sh`
- `RIDE=1 scripts/net_test.sh`

- [ ] **Step 10: Docs**

In `docs/development.md`:
- **New `### The relief cart (single player)` under Rails,** after "Stops, cages and the crew". It covers:
  - the left-behind rule (12 m, the "met" rule, and when none comes)
  - the 15 / 30 / 15 s timings
  - where it spawns and where it stops (6 m, on the rails 1.5 m past, the seat target only moving forward)
  - that it reuses the chaser (limit, hops, heap, sweep, bell, countdown, HUD line)
  - the log lines
  - not saved, no spare spent, solo only
  - a relief may come after a wreck whose spare took over ahead of the player
  - a seat over a gap or trestle stops short of the near lip
  - at the default lethality 3, a relief rolling over a player on the rails mangles them as any cart does; the next relief comes only after they are back up
  - `--test-relief` and its four variants
- **The `--test-ride-edges` sentence:** it turns the relief off from its throw.
- **The headless list in "Build, test, run":** add `godot --headless --path . -- --test-relief[=beside|rails|refuse|ahead]   # floor 12 solo, lethality 1: …; prints [relief] RESULT`.
- **The RIDE net test section:** the online guard line.

- [ ] **Step 11: Commit**

```bash
git add src/ docs/development.md scripts/net_test.sh   # with the new .cs.uid files
git commit -m "feat(rails): the relief cart: a solo player left behind gets one in 15 s (the co-op chaser sent to them), --test-relief

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Review Focus

1. **A save and a load while a relief cart is out.** The relief must be gone, nobody seated in it, the clock reset, and no spare spent; a player still left behind gets a new relief 15 s later. Pinned by Task 2, --test-relief=refuse, R9 and R10.
2. **The menu opens or the mouse is freed in the middle of a hold.** Neither the get-out tap nor the leave-behind may fire. Pinned by `InteractHoldTests.Cancel_MidHold_NeitherTapNorHold` and the cancel in `PartyController._Process` (Task 1).
3. **E held past the second, or released right after it fired.** The prisoners are left behind once and the player stays seated: no stray "Get out" on release. Pinned by `InteractHoldTests.Release_AfterTheHoldFired_IsNoTap` (Task 1).
4. **A cage whose crew are downed but alive, or who all died while the party sat waiting.**
   - Downed crew still hold the cart.
   - The last crew member's death releases a cart that is already waiting, and its held-back countdown starts.

   Pinned by `CageHoldRulesTests.Holds_LockedWithEveryCrewDead_DoesNot` and `HoldsCart` counting `!Dead` (Task 1). The release path is the same `_heldStartCall` path the --test-crew main path exercises when a cage opens.
5. **A player standing on the rails right at the stop the relief comes from.** It must not be placed on top of them: it waits there, clear of them. Pinned by `ReliefRulesTests.SpawnS_ClearOfThePlayer` and `SeatS_StopClose_OnTheRails_WaitsThere` (Task 2).
