# Mine cart 8: throwing a switch from the seat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On floor 12, a seated, living player in the party cart gets "[E] Throw the switch" (or "Set the switch back") from 15 m before a throwable lever until the cart's front passes its points. In that window, E or the VR grip toggles the lever without aiming and never gets the player out. E then does nothing for 1 s after the window closes. A co-op client's throw is accepted by the server inside the window, except a set-back within 1 s of another rider's throw.

**Architecture:** An engine-free rule, `SeatedSwitchRules`, decides three things, and is unit-tested in `tests/`:
- whether a lever is offered, and which of two levers wins;
- the 1 s grace after the window closes;
- the server's refusal of a quick set-back.

`RailEncounterController` applies the window to the armed ride's levers. The local hero's `Adventurer.TickView` then sets `LookingAt` to the offered lever, or to nothing during the grace, instead of the look ray's collider. The ray is still cast, because the VR beam reads it.

Everything downstream follows from `LookingAt` with no change:
- the HUD prompt (`Hud` prints `[E] {LookingAt.Prompt}`);
- `PartyController.Interact(bool)`: a `TrackSwitch` offers no held use, so the press acts at once, and a null target does nothing;
- the VR grip (`VrRig` → `PartyController.VrUse` → the same dispatch);
- a client's `InteractIntent`.

The server accepts a lever's `InteractIntent` when the sender is in reach, as today, or when its own recompute says the sender is in the window.

**Tech Stack:** Godot 4.7.2 .NET, C# / .NET 8, xUnit (`tests/DungeonCrawler.Tests.csproj` links `src/Dungeon/Rails/Core/**/*.cs` by glob).

**Spec:** `docs/superpowers/specs/2026-11-19-floor-12-seated-switch-design.md` (binding). It is an addendum to `docs/superpowers/specs/2026-11-17-floor-12-mine-cart-chase-design.md` (the Switchyard and the Pour sections), which still binds everything it does not change. Read both. The rulings are in `.superpowers/sdd/2026-11-19-mine-cart-8-seated-switch/progress.md` (the lines after "Preflight:").

## Global Constraints

- Protocol stays **17**. No new `MsgKind`, no message changes shape, no new `NetRefs` kind. A client's seated throw is the existing `InteractIntent(lever.NetRef)`.
- Co-op is unchanged except for two things, both in the server's acceptance of a lever's `InteractIntent`: the window acceptance, and its 1 s set-back refusal.
- No new key binding, assets, keys or voice. Any new text is a log line or a test print.
- No change to what a thrown switch does, to lever placement, or to hitting a lever or using one on foot.
- `tests/` is NOT in git. Commit the named `src/` files, `docs/development.md`, `scripts/net_test.sh` and the `.cs.uid` Godot generates for the new `src/Dungeon/Rails/Core/SeatedSwitchRules.cs`. Check `git status` after the first headless run.
- Run `dotnet build DungeonCrawler.csproj` before any `godot --headless` run. Godot flags go after `--`.
- Never `pkill -f` your own pattern; use `--quit-after`. Run `RIDE=1 scripts/net_test.sh` alone, with nothing else heavy running.
- No installers, no dist builds, no push, no merge.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Base.** Plan 7's fix wave has landed (86415f7, 98ebd81, 59cd1e0). Line numbers here are hints; change each file by the behaviour described.

## Settled values (the spec's "Calls left to the builder", and the preflight rulings)

| What | Value | Where |
|---|---|---|
| Window start | The cart's front (`CartRun.FrontS`, party-line S) at or past `LeverS − 15` (`SeatedSwitchRules.WindowAhead = 15f`) | SeatedSwitchRules |
| Window end | The front reaches the points (`frontS >= PointsS`), or the lever is `Taken`, whichever comes first | SeatedSwitchRules |
| Who | Seated in the party cart (`hero.Cart == RailController.Cart`, so not a chaser, relief or spare), not downed, not parked | SeatedSwitchRules + RailEncounterController |
| Cart | `CartMode.Rolling` only. A Held cart (at a stop, or halted at the heap) never offers, so E keeps "Get out" and the cage hold there | SeatedSwitchRules |
| Which levers | The armed ride's pieces with a `Switch` (ride 3's `switch_out`; ride 4's spur lever, via `BuildSpur`'s `piece.Switch = spur.Lever`). `Fixed` levers (`switch_back`) are never offered | RailEncounterController |
| Two levers in the window | The one whose `LeverS` is nearest the front (`|LeverS − frontS|`); on a tie, the first in piece order | SeatedSwitchRules.Nearest |
| Prompt | The lever's own `TrackSwitch.Prompt`: "Throw the switch" / "Set the switch back", shown as `[E] …` by the existing HUD line | unchanged |
| After the window (ruled) | For **1.0 s** (`GraceSeconds`) after a seated rider's window closes, `LookingAt` is null: no prompt, and E does nothing (so no "Get out" for a player still mashing E past the points). Local to each player's own peer | SeatedSwitchRules.InGrace, Adventurer.TickView |
| Quick set-back (ruled) | Only in the server's window-only branch (out of reach, accepted by the window): refuse a press that would set a **thrown** lever back within **1.0 s** (`SetBackGuardSeconds`) of a toggle by a **different** actor. In-reach presses, solo and a rider's own double tap are unchanged | SeatedSwitchRules.RefuseSetBack, TrackSwitch, ServerWorld |
| Hold E in the window | Cannot meet the cage hold: the hold needs a Held cart at a stop, and the window needs `Rolling`. A `TrackSwitch` is not `IHoldInteractable`, so a press in the window acts at once and its release returns `InteractRelease.None` | confirmed by `InWindow_CartNotRolling_Never` |
| Server acceptance | `dist <= LeverReach(...)` as today, **or** `InSeatedWindow(body, lever)` recomputed from the server's own front S, mode and seat book, with no lag allowance, minus the quick set-back refusal | ServerWorld |
| Companion call | While a lever is still ahead, "Line's blocked — switch!" fires when the front comes within `WindowAhead` of that lever's `LeverS` (the window's start). With no lever ahead it keeps the 3 s `HeapLook` | RailEncounterController.TickHeap |
| Headless press | Tests call `party.VrUse(true); party.VrUse(false);`. That is `PartyController.Interact(bool)`, the dispatch both E and the grip run. Headless cannot inject keys: `MouseCaptured` is false, so `_UnhandledInput` returns early | DebugTools, NetTestFlow |

---

### Task 1: The seated window, the throw, the grace, the server's acceptance and the flows

**Deliverable:** In the window, the seated player's prompt is the lever's, and E or the grip toggles it from any aim and any distance. The player stays seated, and for 1 s after the window closes E does nothing. This works solo and as a co-op client. The server refuses a second rider's set-back within 1 s of a throw. The flows exercise it:
- `--test-crew` throws ride 3's lever from the seat;
- `--test-chase` sets ride 4's spur lever back and throws it again from the seat;
- in RIDE=1, the first client throws ride 3's lever from the seat beyond plain reach, and the joiner's quick set-back is refused.

Every existing flow passes.

**Files:**
- Create: `src/Dungeon/Rails/Core/SeatedSwitchRules.cs`
- Modify: `src/Dungeon/Rails/SetPieces/TrackSwitch.cs`:
  - add `LeverS` and `SeatedView`;
  - add `LastToggledAt` and `LastToggledBy`, set in `Toggle` and cleared in `Reset`.
- Modify: `src/Dungeon/Rails/SetPieces/SpurSwitch.cs`:
  - in `Build`, set `LeverS = leverS` on the lever it makes;
  - reword the `LeverLead` doc comment, which says "in reach of a rolling cart's seats", to say that the lever is thrown from the seat through the seated window.
- Modify: `src/Dungeon/Rails/RailEncounterController.Switchyard.cs`:
  - `BuildSwitch`: set `LeverS = leverS`;
  - add `SeatedLeverFor` and `InSeatedWindow`;
  - `LeverAheadOf` returns `p.Switch!.LeverS`;
  - `TickHeap`'s call distance;
  - remove `DebugThrowSwitches` if nothing calls it once --test-crew stops.
- Modify: `src/Party/Adventurer.cs` (`TickView`, plus two fields for the grace)
- Modify: `src/Net/ServerWorld.cs` (`case InteractIntent`, the plain (non-hold) branch's reach check)
- Modify only if Step 7's checks fail: `src/Party/PartyController.cs`
- Modify: `src/Core/DebugTools.Crew.cs` (the per-frame ride-3 throw)
- Modify: `src/Core/DebugTools.Chase.cs` (`ChaseTrackRides`, step 14's check, the `Departed` reset, the header comment)
- Modify: `src/Net/NetTestFlow.cs` (client ride `case 8`, joiner `case 63`)
- Modify: `scripts/net_test.sh` (header line 13, RIDE pass greps)
- Modify: `docs/development.md`:
  - "Switches and the lane blend";
  - the heap's call sentence;
  - the golem paragraph's "within reach of a rolling cart's seats";
  - the `--test-crew` and `--test-chase` descriptions;
  - the RIDE pass list's item 5 ("the client throws switch 1 from its seat", near line 1998).
- Test: create `tests/Dungeon/Rails/SeatedSwitchRulesTests.cs`

**Interfaces:**
- Produces, in `namespace DungeonCrawler.Dungeon.Rails` (engine-free, `Core/SeatedSwitchRules.cs`):
  - `public readonly record struct SeatedLever(float LeverS, float PointsS, bool Fixed, bool Taken);`
  - `public static class SeatedSwitchRules`:
    - `public const float WindowAhead = 15f;`
    - `public const double GraceSeconds = 1.0;`
    - `public const double SetBackGuardSeconds = 1.0;`
    - `public static bool InWindow(bool seatedInPartyCart, bool riderUp, bool rolling, float frontS, SeatedLever lever)`: true iff all three bools are true, `!lever.Fixed && !lever.Taken`, `frontS >= lever.LeverS - WindowAhead` and `frontS < lever.PointsS`. Write the S tests so that a NaN `frontS` fails them.
    - `public static int Nearest(bool seatedInPartyCart, bool riderUp, bool rolling, float frontS, IReadOnlyList<SeatedLever> levers)`: the index of the in-window lever with the least `|LeverS − frontS|` (the first on a tie), or −1. Index the list with a `for` loop, not `foreach`: no allocation, since it runs every physics frame.
    - `public static bool InGrace(bool seated, double now, double windowClosedAt)`: `seated && now >= windowClosedAt && now - windowClosedAt < GraceSeconds`. "Never closed" is `double.NegativeInfinity`.
    - `public static bool RefuseSetBack(bool thrown, double now, double lastToggledAt, string? lastToggledBy, string actor)`: `thrown && lastToggledBy != null && !string.Equals(lastToggledBy, actor, StringComparison.Ordinal) && now - lastToggledAt < SetBackGuardSeconds`.
- Produces on `TrackSwitch`:
  - `public float LeverS { get; init; }`: the party line's S at the lever, set by both builders.
  - `public SeatedLever SeatedView => new(LeverS, PointsS, Fixed, Taken);`
  - `public double LastToggledAt { get; private set; } = double.NegativeInfinity;` and `public string? LastToggledBy { get; private set; }`. Both are server and solo only. `Toggle(who)` sets them (`NetSession.Instance?.Now ?? 0`, `who`) when it toggles. `Reset()` clears them. `DebugThrow` leaves them alone.
- Produces on `RailEncounterController` (every peer; a client's `_pieces`, `ArmedRide`, `Taken` and `RailController.Party.State` are the server's, as told):
  - `public TrackSwitch? SeatedLeverFor(Adventurer hero)`: null unless `Current`. Otherwise it applies `Nearest` to the switches of the pieces with `Plan.Ride == ArmedRide` and `Switch != null`, with these inputs:
    - `seatedInPartyCart = hero.Seated && hero.Cart == _rails.Cart`;
    - `riderUp = !hero.Downed && !hero.Parked`;
    - `rolling = _rails.State.Mode == CartMode.Rolling`;
    - `frontS = _rails.Party.FrontS`.

    Keep two reused lists (views and switches), cleared per call.
  - `public bool InSeatedWindow(Adventurer hero, TrackSwitch lever)`: the same inputs and ride filter, with `InWindow` for that one lever (not only the nearest).

- [ ] **Step 1: Write the failing tests** in `tests/Dungeon/Rails/SeatedSwitchRulesTests.cs` (namespace `DungeonCrawler.Tests.Dungeon.Rails`, xUnit, in the style of `PropPromptRulesTests`):

```csharp
private static readonly SeatedLever L = new(100f, 105f, false, false);
private static bool In(float front, SeatedLever lever) => SeatedSwitchRules.InWindow(true, true, true, front, lever);

[Fact] public void InWindow_FifteenMetresBeforeTheLever_Opens() { Assert.True(In(85f, L)); Assert.False(In(84.9f, L)); }
[Fact] public void InWindow_AtTheLever() => Assert.True(In(100f, L));
[Fact] public void InWindow_BetweenTheLeverAndThePoints() => Assert.True(In(104.9f, L));
[Fact] public void InWindow_PastThePoints_Closed() { Assert.False(In(105f, L)); Assert.False(In(130f, L)); }
[Fact] public void InWindow_Taken_Closed() => Assert.False(In(102f, L with { Taken = true }));
[Theory] [InlineData(85f)] [InlineData(100f)] [InlineData(104f)]
public void InWindow_FixedLever_Never(float front) => Assert.False(In(front, L with { Fixed = true }));
[Fact] public void InWindow_Downed_Never() => Assert.False(SeatedSwitchRules.InWindow(true, false, true, 100f, L));
[Fact] public void InWindow_NotSeatedInThePartyCart_Never() => Assert.False(SeatedSwitchRules.InWindow(false, true, true, 100f, L));
[Fact] public void InWindow_CartNotRolling_Never() => Assert.False(SeatedSwitchRules.InWindow(true, true, false, 100f, L)); // the cage hold's Held stop never meets the window
[Fact] public void InWindow_NaNFront_Never() => Assert.False(In(float.NaN, L));
[Fact] public void Nearest_TwoLevers_TheNearerToTheFront()
{
    var two = new[] { L, new SeatedLever(106f, 108f, false, false) };
    Assert.Equal(0, SeatedSwitchRules.Nearest(true, true, true, 95f, two)); // 5 m vs 11 m
    Assert.Equal(1, SeatedSwitchRules.Nearest(true, true, true, 104f, two)); // 4 m vs 2 m
}
[Fact] public void Nearest_Tie_TheFirst() => Assert.Equal(0, SeatedSwitchRules.Nearest(true, true, true, 103f, new[] { L, new SeatedLever(106f, 110f, false, false) }));
[Fact] public void Nearest_SkipsFixedAndTaken()
{
    var levers = new[] { L with { Fixed = true }, new SeatedLever(101f, 103f, false, true), new SeatedLever(108f, 110f, false, false) };
    Assert.Equal(2, SeatedSwitchRules.Nearest(true, true, true, 100f, levers));
}
[Fact] public void Nearest_NoneInWindow_MinusOne() => Assert.Equal(-1, SeatedSwitchRules.Nearest(true, true, true, 50f, new[] { L }));
[Fact] public void Nearest_NotSeatedDownedOrHeld_MinusOne()
{
    Assert.Equal(-1, SeatedSwitchRules.Nearest(false, true, true, 100f, new[] { L }));
    Assert.Equal(-1, SeatedSwitchRules.Nearest(true, false, true, 100f, new[] { L }));
    Assert.Equal(-1, SeatedSwitchRules.Nearest(true, true, false, 100f, new[] { L }));
}
[Fact] public void InGrace_ForASecondAfterTheWindowCloses()
{
    Assert.True(SeatedSwitchRules.InGrace(true, 10.0, 10.0));
    Assert.True(SeatedSwitchRules.InGrace(true, 10.99, 10.0));
    Assert.False(SeatedSwitchRules.InGrace(true, 11.0, 10.0));
}
[Fact] public void InGrace_NeverClosedOrNotSeated_No()
{
    Assert.False(SeatedSwitchRules.InGrace(true, 10.0, double.NegativeInfinity));
    Assert.False(SeatedSwitchRules.InGrace(false, 10.5, 10.0));
}
[Fact] public void RefuseSetBack_AnotherRiderWithinASecond_Refused() => Assert.True(SeatedSwitchRules.RefuseSetBack(true, 10.5, 10.0, "Brand", "Wren"));
[Fact] public void RefuseSetBack_AtOneSecond_Allowed() => Assert.False(SeatedSwitchRules.RefuseSetBack(true, 11.0, 10.0, "Brand", "Wren"));
[Fact] public void RefuseSetBack_TheSameRider_Allowed() => Assert.False(SeatedSwitchRules.RefuseSetBack(true, 10.2, 10.0, "Brand", "Brand")); // a rider's own double tap: the spec's toggle
[Fact] public void RefuseSetBack_AThrowNever() => Assert.False(SeatedSwitchRules.RefuseSetBack(false, 10.2, 10.0, "Brand", "Wren"));
[Fact] public void RefuseSetBack_NeverToggled_Allowed() => Assert.False(SeatedSwitchRules.RefuseSetBack(true, 10.0, double.NegativeInfinity, null, "Wren"));
```

- [ ] **Step 2: Run them and check they fail**

Run: `dotnet test tests/DungeonCrawler.Tests.csproj --filter "FullyQualifiedName~SeatedSwitchRulesTests"`
Expected: a build error, because `SeatedSwitchRules` and `SeatedLever` are not defined.

- [ ] **Step 3: Implement `SeatedSwitchRules`** (Interfaces above), with doc comments in the file's style that cite the spec's window and the two rulings.

- [ ] **Step 4: Run them and check they pass.** Same command. Expected: all pass.

- [ ] **Step 5: Give each lever its S and its last toggle.**
  - Add `LeverS`, `SeatedView`, `LastToggledAt` and `LastToggledBy` to `TrackSwitch` (Interfaces above).
  - Set `LeverS = leverS` in `BuildSwitch` (`piece.S − LeverLead`, clamped at 0, which it already computes) and in `SpurSwitch.Build` (`PointsS − SpurSwitch.LeverLead`, clamped, which it already computes). Floors are built on every peer, so a client's copies get it too.
  - Make `LeverAheadOf` return `p.Switch!.LeverS` and test `> front` on it, so the call and the window measure the same S.

- [ ] **Step 6: `SeatedLeverFor` and `InSeatedWindow`** on `RailEncounterController` (Interfaces above), in `RailEncounterController.Switchyard.cs` beside `LeverAheadOf`.

- [ ] **Step 7: Route the seated E through `LookingAt`.** In `Adventurer.TickView`, inside the `IsLocal && !Downed` branch, keep `_fpWeapon.SetGuard(Guarding)` and `_interactRay.ForceRaycastUpdate()` (`VrRig.ShowAim` reads the ray for the beam's length). Then choose `LookingAt`:
  1. If `Seated`, `RailEncounterController.Instance` is valid and `SeatedLeverFor(this)` returns a lever: `LookingAt = lever`, and remember `_inSwitchWindow = true`.
  2. Else, if `_inSwitchWindow` was true: set `_switchWindowClosedAt = Time.GetTicksMsec() / 1000.0` and `_inSwitchWindow = false`.
  3. If no lever was offered and `SeatedSwitchRules.InGrace(Seated, now, _switchWindowClosedAt)`: `LookingAt = null`.
  4. Otherwise: the ray's collider as today, including the empty-prompt filter.

  The field `_switchWindowClosedAt` starts at `double.NegativeInfinity`.

  At 59cd1e0, `PartyController.Interact(bool)` already does what this needs, so it should not change:
  - The press sets `_holdTarget` only for an `IHoldInteractable { HoldOffered: true }`, and `_hold.Press(_time, false)` returns true, so `local.TryInteract()` uses the lever at once. With `LookingAt` null, `TryInteract(null)` returns.
  - The release returns `InteractRelease.None`.
  - `VrUse(bool)` calls the same `Interact(bool)`.

  Re-read it and confirm all three. Touch `PartyController.cs` only if one fails, and then restore the dispatch rather than special-casing the lever. Do not touch `RailController.PromptFor`/`UseCart`: in the window the cart is simply not what E is aimed at.

- [ ] **Step 8: The server's acceptance and its quick set-back refusal.** In `ServerWorld`'s `case InteractIntent`, plain (non-hold) branch, for a `node` that is a `Dungeon.Rails.TrackSwitch lever`:
  1. Compute `window = RailEncounterController.Instance is { } enc && GodotObject.IsInstanceValid(enc) && enc.InSeatedWindow(body, lever)`.
  2. When `dist > reach`:
     - If `!window`, refuse as today.
     - If `SeatedSwitchRules.RefuseSetBack(lever.Thrown, NetSession.Instance.Now, lever.LastToggledAt, lever.LastToggledBy, body.State.Name)`, print (always, not only on a test server) `[net-server] interact {ref} from {slot.Name}: seated window: set back {now − LastToggledAt:0.00} s after {LastToggledBy} threw it, ignored`, then break.
     - Otherwise, accept.
  3. Extend the existing test-server print (`[net-server] interact {ref} from {name}: {dist} m (reach {reach}) …`) with ` seated window {(window ? "yes" : "no")}` for levers.

  Leave the hold branch, in-reach lever presses and every other prop's reach as they are. `TrackSwitch.Interact` still refuses a downed actor, and `Toggle` refuses a `Taken` or `Fixed` lever, so a press that arrives after the points is a no-op.

- [ ] **Step 9: Align the companion's call.** In `TickHeap`, when `LeverAheadOf(piece, front)` returns a lever S, return early while `aim − front > SeatedSwitchRules.WindowAhead`. With no lever ahead (`aim` is the heap's face), keep the `look` (`SetPieceRules.HeapLook`) test. Keep the existing `[rails] heap … called at front S=…` print.

- [ ] **Step 10: `--test-crew` throws ride 3 from the seat.** In `DebugTools.Crew.cs`, replace the per-frame block that calls `enc.DebugThrowSwitches(3)` while ride 3 is armed and rolling.
  - **When:** `ArmedRide == 3`, rolling, `enc.SetPieceNode("r3_switch_out") is TrackSwitch { Thrown: false, Taken: false } lever`, `party.Local` seated in `rc.Cart`, and `party.LookingAt == lever`.
  - **Do:**
    1. Record `dist = me.GlobalPosition.DistanceTo(lever.GlobalPosition)`, `ahead = lever.LeverS − rc.Party.FrontS` and the prompt.
    2. Call `party.VrUse(true); party.VrUse(false);`.
    3. Print `[crew] ride 3: the seated lead throws the switch_out lever from the seat (the seated window, prompt "{prompt}", {ahead:0.0} m before the lever, {dist:0.0} m off): thrown={lever.Thrown}, still seated={me.Cart == rc.Cart}`.
  - **Fail if:** the prompt is not `"Throw the switch"`; `dist <= PropPromptRules.InteractReach` (the ray could not have produced it); the lever is not thrown; or the lead is no longer seated in `rc.Cart`. Also fail with `ride 3's switch_out lever was never offered to the seated lead before its points` if, during ride 3, the lever turns `Taken` while still straight.
  - **Expect:** the test used to throw at the arming, so the heap call never fired. Now a `[rails] heap r3_heap called at front S=…` line appears, about 15 m before the lever. No check depends on it.
  - **Clean up:** remove `DebugThrowSwitches` if nothing else calls it.

- [ ] **Step 11: `--test-chase` sets ride 4's spur back and throws it again from the seat** (`DebugTools.Chase.cs`, **section 1 only**). These stay as they are: ride 3 is thrown by a hit, and ride 4's lever is thrown ahead on ride 2 by `DebugThrow` (the walker case).
  1. **Gate** (in `ChaseTrackRides`): `!_chSection2`, `ArmedRide == 4`, rolling, the local hero seated in `rc.Cart`, and `party.LookingAt == spur.Lever`.
  2. **Press 1** (`_chSeatSpur == 0`): require the prompt `"Set the switch back"`. Press with `VrUse(true/false)`. Then require `!spur.Lever.Thrown` and the hero still seated.
  3. **Press 2** (a later frame): require `"Throw the switch"`, press, then require `Thrown` and still seated.
  4. **Print** after each press: `[chase] 4 seated window: "{prompt}" pressed {ahead:0.0} m before the spur lever, {dist:0.0} m off: thrown={…}, still seated={…}`.
  5. **On a mismatch:** set the dedicated field `_chSeatSpurFail` (never the run's shared failure).
  6. **In section 1's step 14** (held at stop 4): fail on `_chSeatSpurFail`. Also fail with `the seated window did not set ride 4's spur lever back and throw it again (presses {n})` unless `_chSeatSpur == 2`.
  7. **Reset** `_chSeatSpur` on `Departed`, next to `_chOnSpur`, only while `!_chSection2`.

  The existing `!_chDiverted` check still proves that the re-thrown lever diverts the golem. Update the file's header comment.

- [ ] **Step 12: RIDE=1, the first client throws ride 3 from the seat through the window** (`NetTestFlow.cs`, client ride `case 8`). Replace the `dist > 3f` gate and the direct `net.Send`:
  1. **Wait for:** `me` seated, `rc.StopIndex == 2`, rolling, `!_rideThrowAsked`, and `Main.Instance.Party.LookingAt == lever`.
  2. **Fail if:**
     - `lever.Prompt != "Throw the switch"`;
     - `dist <= PropPromptRules.InteractReach + PropPromptRules.SeatedLagCap`, with `the seated window first offered ride 3's lever {dist:0.00} m off, inside the plain reach`.
  3. **Press:** `Main.Instance.Party.VrUse(true); Main.Instance.Party.VrUse(false);`. The client's `TryInteract` sends the intent.
  4. **Print:** `[net] ride: ride 3's lever offers "Throw the switch" in the seated window {ahead:0.0} m before the lever, {dist:0.00} m from our seat at V={v:0.00} m/s; thrown through the intent from the seat ({ref})`.

  The crossing branch, `_rideThrowDist` and the server's `takes ride 3's switch … thrown through the intent` line stay as they are.

- [ ] **Step 13: RIDE=1, the joiner's quick set-back is refused** (`NetTestFlow.cs`, joiner `case 63`, before its existing 2 s get-out).
  1. **Wait:** once, while seated on ride 3, when `main.Party.LookingAt` is ride 3's lever (`NetRefs.Switch(enc.PieceIndexOf("switch_out", 3))`) and `lever.Thrown`, i.e. the first client's throw has shown here.
  2. **Fail if:** the prompt is not `"Set the switch back"`.
  3. **Press:** `main.Party.VrUse(true); main.Party.VrUse(false);`.
  4. **Print:** `[net] ride: the joiner presses "Set the switch back" from the seat as the first client's throw shows here`.
  5. **Then:** let the existing get-out run. Its condition (2 s rolling, V ≥ 2.5) gets one more clause: the press has been sent, or the window has closed without one.

  On the server, the press arrives within 1 s of the first client's toggle, out of reach and in the window. The server prints the `seated window: set back … ignored` line, the lever stays thrown and the cart still crosses: the existing cross greps prove it.

  If the joiner's get-out now comes too late for its later steps (`case 67` asks for the floor again mid-ride 3), check the `[net]` lines before you change anything else. Report it rather than move the press outside the window.

- [ ] **Step 14: `scripts/net_test.sh`.**
  - **Header (line 13):** say that ride 3's switch is thrown from the seat through the seated window, and that the joiner's quick set-back is refused.
  - **RIDE pass, client grep:** tighten it to `\[net\] ride: ride 3's lever offers \"Throw the switch\" in the seated window .* thrown through the intent from the seat`.
  - **RIDE pass, new checks:** add three, beside the existing cross check:

| Log | Grep | FAIL message |
|---|---|---|
| `$TMP/server.log` | `\[net-server\] interact switch:[0-9]* from .* seated window yes` | `NET TEST: FAIL (the server did not accept ride 3's switch through the seated window)` |
| `$TMP/joiner.log` | `\[net\] ride: the joiner presses \"Set the switch back\" from the seat` | `NET TEST: FAIL (the joiner never pressed set-back from the seat)` |
| `$TMP/server.log` | `\[net-server\] interact switch:[0-9]* from .*: seated window: set back .* ignored` | `NET TEST: FAIL (the joiner's quick set-back was not refused)` |

  The existing "Rider jumps from the moving cart" check stays.

- [ ] **Step 15: Docs** (`docs/development.md`):
  - Under "Switches and the lane blend", add a short paragraph on throwing from the seat:
    - the window: 15 m before the lever until the front passes the points, for a seated, up rider in the party cart while it rolls; the armed ride's throwable levers only, the nearer of two;
    - `LookingAt` is the lever, so the HUD shows `[E] Throw the switch`, E or the grip toggles it from any aim and "Get out" is not offered;
    - E does nothing for 1 s after the window;
    - the server accepts the intent by reach or by `InSeatedWindow`, and refuses an out-of-reach set-back within 1 s of another rider's toggle.
  - Change the heap sentence: the call comes at the window's start while the lever is still ahead.
  - Note in the golem paragraph that the spur lever is thrown the same way.
  - Update the `--test-crew` and `--test-chase` descriptions, and the RIDE pass list's item 5.

- [ ] **Step 16: Build and run the whole suite.**

Run: `dotnet build DungeonCrawler.csproj && dotnet test tests/DungeonCrawler.Tests.csproj`
Expected: the build succeeds and every test passes.

- [ ] **Step 17: Run the headless flows**, one at a time, after the build:

| Command | Expected |
|---|---|
| `godot --headless --path . -- --test-crew=condemned` | `[crew] RESULT ok (condemned)` and the `[crew] ride 3: the seated lead throws …` line with `thrown=True, still seated=True` |
| `godot --headless --path . -- --test-crew=forgiven` | `[crew] RESULT ok (forgiven)` |
| `godot --headless --path . -- --test-chase` | `[chase] RESULT ok`, exactly two `[chase] 4 seated window:` lines (section 1; section 2 rides ride 4 with the lever straight and presses nothing), and the golem diverted |
| `godot --headless --path . -- --test-ride` | `[ride] RESULT ok` |
| `godot --headless --path . -- --test-ride-edges` | `[ride-edges] RESULT ok` |
| `godot --headless --path . -- --test-cart-watchdog` | `[watchdog] RESULT ok` |
| `godot --headless --path . -- --test-relief=beside` (and `rails`, `refuse`, `ahead`) | `[relief] RESULT ok (…)` |
| `scripts/net_test.sh` | `NET TEST: ok` |
| `START_FLOOR=0 scripts/net_test.sh` | `NET TEST: ok` |
| `RIDE=1 scripts/net_test.sh` (alone) | `NET TEST: ok`, with the new client, joiner and server lines in the logs |

- [ ] **Step 18: Commit.** Run `git status` and confirm that `src/Dungeon/Rails/Core/SeatedSwitchRules.cs.uid` exists. Stage named paths only (add `src/Party/PartyController.cs` only if Step 7 changed it):

```bash
git add src/Dungeon/Rails/Core/SeatedSwitchRules.cs src/Dungeon/Rails/Core/SeatedSwitchRules.cs.uid \
  src/Dungeon/Rails/SetPieces/TrackSwitch.cs src/Dungeon/Rails/SetPieces/SpurSwitch.cs \
  src/Dungeon/Rails/RailEncounterController.Switchyard.cs src/Party/Adventurer.cs src/Net/ServerWorld.cs \
  src/Core/DebugTools.Crew.cs src/Core/DebugTools.Chase.cs src/Net/NetTestFlow.cs scripts/net_test.sh docs/development.md
git commit -m "feat(rails): throw a switch from the seat: 15 m before the lever to its points, E or the grip toggles it with no aim and never gets out, then 1 s of nothing (SeatedSwitchRules); the server accepts it in the rider's window and refuses another rider's set-back within 1 s; the heap call at the window's start

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

## Review Focus

1. **E pressed just as the window closes.** The client still shows the lever, but the server's cart has already taken the points. The player expects nothing to happen and to stay seated: the server's `Toggle` refuses a `Taken` lever, and the client never sent the cart's ref. This is pinned only by `InWindow_PastThePoints_Closed`/`InWindow_Taken_Closed`. No flow presses at the closing edge.
2. **E mashed just past the points, looking down into the tub.** For 1 s no "Get out" may fire, because the ray's cart is not used. This is pinned by `InGrace_ForASecondAfterTheWindowCloses` (Task 1). No flow presses in the grace. The owner's play test should try it.
3. **Two co-op riders both pressing on the companion's call.** The second press is refused and the lever stays thrown. This is pinned by the `RefuseSetBack_*` tests and by RIDE=1's joiner press (Task 1, Steps 13–14).
4. **The cart Held at a stop or halted at the heap, with a lever within 15 m.** E must stay "Get out" and the cage hold. Pinned by `InWindow_CartNotRolling_Never` and `Nearest_NotSeatedDownedOrHeld_MinusOne` (Task 1).
5. **A co-op player in a chaser or relief cart, or on foot, near the lever.** They get no window, and the server accepts them only by reach, as today. Pinned by `InWindow_NotSeatedInThePartyCart_Never`; the server's `InSeatedWindow` uses the same `hero.Cart == _rails.Cart` (Task 1).
