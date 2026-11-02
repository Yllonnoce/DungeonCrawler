# Merchant Lines and Voice Implementation Plan (plan E)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the shipped campaign its merchant, Master Oswin Pellet, with every line he speaks (tavern briefing, twenty floors of visits, three gathering remarks) written, validated and voiced, and teach the planner and the offline designer to produce a merchant for every other campaign.

**Architecture:** Plan C already added the data model (`CastMember.Merchant`, `CampaignPlan.Merchant : MerchantPlan?`, `MerchantPlan`, `MerchantFloorLines`, `MerchantLineBank.Generic`) and the validator's one-merchant rule. This plan (1) threads the `Merchant` section through the voice machinery so its lines get ids, audio, budget picks and sweep protection like every other campaign line; (2) extends the Claude prompt and the offline designer; (3) writes the shipped data and the bible entry; (4) bakes the audio with Runware and ships it. Data is hand-written JSON in the campaign file; nothing here touches gameplay code beyond voice plumbing.

**Tech Stack:** Godot 4.7.2 .NET / C#, System.Text.Json plan DTOs (`PlanFormat`), xUnit tests in `tests/` (dev machine only), Runware TTS through `VoiceRenderer`, headless bake flags in `src/Core/DebugTools.cs`.

**Spec:** `docs/superpowers/specs/2026-11-02-tavern-merchant-safe-rooms-design.md` (section 6, calls 1 and 2). Contract: `docs/superpowers/plans/2026-11-02-tavern-shared-interfaces.md` section E (consumes section C).

## Global Constraints

- The merchant **Master Oswin Pellet is a man**; his campaign `Persona.Voice` is `"Achird"`, which `RunwareVoices` maps to the Inworld voice **Oliver**; no other character uses Achird or Oliver. Every other cast voice stays exactly as it is: Maud Sorrel `Gacrux` (Wendy, old woman), Hesketh Crane `Umbriel` (man), Tobit Hale `Puck` (man), Grisel Thorn `Despina` (woman), Old Ulf `Orus` (man), the Verger `Sadaltager` (Conrad, man). The authority for every speaking character's sex is `/home/eric/.claude/projects/-mnt-Transfer-5FD1E1415AC80F29-dev-DungeonCrawler/memory/character-sexes-for-voice.md`; written lines are in the speaker's own person and the recorded voice matches it; the plan's last task adds Pellet to that file.
- He never states the campaign's truth. Forbidden in any of his lines, case-insensitive: `god`, `Gorm`, `Verger`, `soul`, `memorial`, `nineteen`. He speaks of the council's plumbing contract he financed, the fee he advances, property, investors, surveys and quakes.
- The briefing sits inside the shipped frame: the council hired "four inspectors" for plumbing and "paid you four in advance" (floor 1 intro and Crane's floor-1 lines stay word for word); Pellet financed that contract and the seed money is the council's advance passing through him, his cut taken. No existing voiced line is edited, so none is re-rendered.
- Alderman Crane's role, lines, floors (1, 4, 9, 17) and betrayal are untouched. `cast_pellet` never appears as a floor NPC (`CastId`) on any floor.
- Writing voice: "a Dunmarrow factor: fussy, money-minded, brave in sentences and nowhere else." Register as the existing cast: plain, wry, dark humour, period idiom, no modern slang, no fourth wall. Each line at most 220 characters; a `Delivery` only where he is not in his usual manner (`Persona.DefaultDelivery` is `"cheerful"`; fear floors use `weary`, `whisper`, `frantic`).
- Line ids come from `VoiceLineId.For(text, voice, style, model)` inside `VoiceRenderer.Enqueue`; a line that already has a `VoiceId` with audio keeps it. The bake is run with the owner's Runware key from `~/snap/code/current/.local/share/godot/app_userdata/DungeonCrawler/keys.cfg` (line `runware="..."`, quotes stripped, exported as `RUNWARE_API_KEY`, never printed), models `inworld:tts@1.5-max` for both main and minor.
- Build with `dotnet build DungeonCrawler.csproj --no-incremental` before any headless run. Tests run with `cd tests && dotnet test --nologo -v q [--filter ...]`.
- Floor 0 is the tavern (plan D): briefing lines are picked as floor 0 in the voice budget.

## Review Focus

1. A campaign with no `Merchant` section (any save or AI campaign made before plan C) must not crash the voice budget, the sweep or the director: Task 1's `Candidates_WithoutMerchantSection_ListsNothingExtra` pins it.
2. A briefing graph with a reply that bypasses `deal` would cost the player the seed money on the talk path: Task 3's `ShippedBriefing_EveryPathReachesDeal` pins it.
3. A line that leaks the truth undoes the campaign's mystery: Task 3's `ShippedMerchant_NeverSaysTheTruth` pins the forbidden-word list.
4. A rebake that re-renders already-voiced lines wastes the owner's money: Task 4's verification compares the rendered count against the merchant line count (67), not the whole cast.
5. A bank sweep that does not know the merchant's ids would delete his audio: Task 1's `VoiceRefs_IncludeMerchantLines` pins it.

---

### Task 1: The Merchant section is voiced, budgeted and swept like every other campaign line

**Files:**
- Modify: `src/Adventure/Core/Voice/VoiceBudget.cs` (`Candidates`, around line 64-96)
- Modify: `src/Adventure/AdventureDirector.cs` (`EnqueueCampaignVoice`, line 134)
- Modify: `src/Core/DebugTools.cs` (`--bake-cast-voice` wanted set, lines 767-790)
- Test: `tests/Adventure/VoiceBankSweepTests.cs`, `tests/Adventure/VoiceTests.cs`

**Interfaces:**
- Consumes (plan C): `CampaignPlan.Merchant` (`MerchantPlan?`), `MerchantPlan.Briefing` (`NpcScript`), `MerchantPlan.FloorLines` (`List<MerchantFloorLines>` with `Floor`, `Greeting`, `Progress`, `Trade`), `MerchantPlan.Gathering` (`List<Line>`), `CastMember.Merchant` (bool); `CampaignPlan.MerchantCast()` (a method, defined by plan C Task 1; do not redefine it).
- Produces: `static IEnumerable<Line> MerchantPlan.AllLines()` (briefing node lines, then floor lines in floor order greeting/progress/trade, then gathering) in `src/Adventure/Core/Plans/CampaignPlan.cs`; `VoiceBudget.Candidates` emits one `Pick` per merchant line with reasons `"merchant briefing"` (floor 0), `"merchant floor"` (its floor) and `"merchant gathering"` (floor 0), appended after the barks; `AdventureDirector.EnqueueMerchantVoice(CampaignPlan campaign)` (public, called from `EnqueueCampaignVoice`).

- [ ] **Step 1: Write the failing tests**

In `tests/Adventure/VoiceBankSweepTests.cs`:

```csharp
[Fact]
public void VoiceRefs_IncludeMerchantLines()
{
    var campaign = new CampaignPlan { Merchant = new MerchantPlan() };
    campaign.Merchant.Briefing.Nodes.Add(new DialogueNode { Id = "hire", Line = new Line("Sit.") { VoiceId = "aaaaaaaaaaaaaaa1" } });
    campaign.Merchant.FloorLines.Add(new MerchantFloorLines { Floor = 3, Greeting = new Line("In here.") { VoiceId = "aaaaaaaaaaaaaaa2" }, Progress = new Line("Well?") { VoiceId = "aaaaaaaaaaaaaaa3" }, Trade = new Line("Coin first.") { VoiceId = "aaaaaaaaaaaaaaa4" } });
    campaign.Merchant.Gathering.Add(new Line("Footsteps.") { VoiceId = "aaaaaaaaaaaaaaa5" });
    var ids = VoiceRefs.Ids(campaign, new Dictionary<int, FloorPlan>());
    foreach (var id in new[] { "aaaaaaaaaaaaaaa1", "aaaaaaaaaaaaaaa2", "aaaaaaaaaaaaaaa3", "aaaaaaaaaaaaaaa4", "aaaaaaaaaaaaaaa5" }) Assert.Contains(id, ids);
}
```

In `tests/Adventure/VoiceTests.cs`:

```csharp
[Fact]
public void Candidates_ListMerchantLinesLast_WithTheirFloors()
{
    var campaign = new CampaignPlan { Merchant = new MerchantPlan() };
    campaign.Merchant.Briefing.Nodes.Add(new DialogueNode { Id = "hire", Line = new Line("Sit.") });
    campaign.Merchant.FloorLines.Add(new MerchantFloorLines { Floor = 7, Greeting = new Line("a"), Progress = new Line("b"), Trade = new Line("c") });
    campaign.Merchant.Gathering.Add(new Line("Footsteps."));
    campaign.Barks.Add(new BarkSet { Member = "Brand", Lines = { new BarkLine { Trigger = BarkTrigger.Kill, Line = new Line("Down.") } } });
    var picks = VoiceBudget.Candidates(campaign, new Dictionary<int, FloorPlan>());
    var merchant = picks.Where(p => p.Reason.StartsWith("merchant")).ToList();
    Assert.Equal(5, merchant.Count);
    Assert.Equal(0, merchant.First(p => p.Reason == "merchant briefing").Floor);
    Assert.All(merchant.Where(p => p.Reason == "merchant floor"), p => Assert.Equal(7, p.Floor));
    Assert.True(picks.FindIndex(p => p.Reason.StartsWith("bark")) < picks.FindIndex(p => p.Reason.StartsWith("merchant")), "merchant lines come after the barks");
}

[Fact]
public void Candidates_WithoutMerchantSection_ListsNothingExtra()
{
    var campaign = new CampaignPlan();
    Assert.DoesNotContain(VoiceBudget.Candidates(campaign, new Dictionary<int, FloorPlan>()), p => p.Reason.StartsWith("merchant"));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~VoiceRefs_IncludeMerchantLines|FullyQualifiedName~Candidates_ListMerchantLinesLast|FullyQualifiedName~Candidates_WithoutMerchantSection"`
Expected: FAIL (the first two find no merchant picks; `Pick.Floor`/`Reason` exist already, so these fail on counts, not compile errors; if `MerchantPlan.AllLines` is referenced anywhere it is a compile error until Step 3).

- [ ] **Step 3: Implement**

`MerchantPlan.AllLines()` in `src/Adventure/Core/Plans/CampaignPlan.cs` yields `Briefing.Nodes.Select(n => n.Line)`, then for each `FloorLines` ordered by `Floor`: `Greeting`, `Progress`, `Trade`, then `Gathering`. In `VoiceBudget.Candidates`, after the barks loop: when `campaign.Merchant is { } m`, add `new Pick(line, "merchant briefing", 0)` per briefing node, `new Pick(l, "merchant floor", fl.Floor)` per floor line, `new Pick(l, "merchant gathering", 0)` per gathering line. `VoiceRefs.Ids` already walks `Candidates`, so it needs no change.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command. Expected: PASS (3 tests).

- [ ] **Step 5: Enqueue the merchant's lines at campaign start, and include them in the cast bake**

`AdventureDirector.EnqueueMerchantVoice(CampaignPlan campaign)`: return when `campaign.Merchant == null || campaign.MerchantCast() is not { } cast`; otherwise for each line in `campaign.Merchant.AllLines()` call `_voice.Enqueue(line, Voices.Npcs[0], Speaker.Npc, VoicePriority.Npc, major: true, persona: cast.Persona)` (the persona's `Voice` overrides the placeholder voice inside `Enqueue`; use the same `VoicePriority` the cast NPC lines use in `EnqueueFloorVoice`). Call it from `EnqueueCampaignVoice` after `EnqueueBarks(campaign)`. In `DebugTools` `--bake-cast-voice`, add every line of `castCampaign.Merchant?.AllLines()` to `wanted` and call `AdventureDirector.Instance.EnqueueMerchantVoice(castCampaign)` after the floor-plan enqueues, inside the same `Filter` window; the merged ids reach `DIR/cast-plans/campaign.json` through the existing `BuiltinCampaign.Write`.

- [ ] **Step 6: Build and run the whole suite**

Run: `dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly && cd tests && dotnet test --nologo -v q`
Expected: 0 errors; `Passed!` with no failures.

- [ ] **Step 7: Commit**

```bash
git add src/Adventure/Core/Voice/VoiceBudget.cs src/Adventure/Core/Plans/CampaignPlan.cs src/Adventure/AdventureDirector.cs src/Core/DebugTools.cs
git commit -m "feat(voice): the campaign's Merchant section is voiced, budgeted and kept by the sweep like every other line"
```

---

### Task 2: The planner prompt and the offline designer produce a merchant

**Files:**
- Modify: `src/Adventure/Core/Ai/PromptBuilder.cs` (the campaign task text around line 61: Cast and the new Merchant section)
- Modify: `src/Adventure/Core/Design/OfflineDesigner.cs` (`Outline`, line 84)
- Test: `tests/Adventure/AiPlannerTests.cs`, `tests/Adventure/OfflineDesignerTests.cs`

**Interfaces:**
- Consumes (plan C): `MerchantLineBank.Generic(string campaignTitle, string antagonistName) : MerchantPlan`; `CastMember.Merchant`.
- Produces: `OfflineDesigner.Outline(...)` returns a campaign with exactly one `Merchant == true` cast member (`Id = "cast_merchant"`, `Name` drawn from a small seeded list of factor names, `Role` "The merchant who financed the venture and follows it down, one safe room at a time", `Secret = ""`, `Persona` `{ VoiceStyle: "a fussy factor, money-minded, brave in sentences and nowhere else", DefaultDelivery: "cheerful", Voice: "Achird" }`) and `Merchant = MerchantLineBank.Generic(plan.Title, plan.Antagonist.Name)`; the campaign prompt text contains the word `Merchant` and the phrase `exactly one`.

- [ ] **Step 1: Write the failing tests**

In `tests/Adventure/OfflineDesignerTests.cs` (use the existing `Outline(...)` call pattern in that file for tiers/themes):

```csharp
[Fact]
public void Outline_HasExactlyOneMerchant_WithTwentyFloorsOfLines()
{
    var plan = OfflineDesigner.Outline(7319, new[] { TestAdventure.Crypt }); // the fixture the file's other Outline tests use
    var merchants = plan.Cast.Where(c => c.Merchant).ToList();
    Assert.Single(merchants);
    Assert.Equal("Achird", merchants[0].Persona!.Voice);
    Assert.Equal("", merchants[0].Secret);
    Assert.NotNull(plan.Merchant);
    Assert.Equal(Enumerable.Range(1, 20), plan.Merchant!.FloorLines.OrderBy(f => f.Floor).Select(f => f.Floor));
    Assert.Equal(3, plan.Merchant.Gathering.Count);
    Assert.Contains(plan.Merchant.Briefing.Nodes, n => n.Id == "deal");
}
```

In `tests/Adventure/AiPlannerTests.cs` (the outline request is `PromptBuilder.Outline(PromptTemplates.Default, "claude-opus-5", seed, tiers, roster, party, floor1Menu)`; reuse the roster, party and menu fixtures the file's `Floor` tests build; the task text lives in the system blocks and the user text, so search both):

```csharp
[Fact]
public void CampaignPrompt_AsksForTheMerchantSection_AndExactlyOneMerchant()
{
    var req = PromptBuilder.Outline(PromptTemplates.Default, "claude-opus-5", 7319, new[] { TestAdventure.Crypt }, roster, party, menu);
    var text = string.Join("\n", req.System.Select(b => b.Text)) + "\n" + req.User;
    Assert.Contains("Merchant", text);
    Assert.Contains("exactly one", text);
    Assert.Contains("Briefing", text);
    Assert.Contains("FloorLines", text);
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~Outline_HasExactlyOneMerchant|FullyQualifiedName~CampaignPrompt_AsksForTheMerchantSection"`
Expected: FAIL (no merchant in the outline; prompt lacks the words).

- [ ] **Step 3: Implement**

`OfflineDesigner.Outline`: after the cast is built, add the merchant cast member and set `plan.Merchant = MerchantLineBank.Generic(plan.Title, plan.Antagonist.Name)`. `PromptBuilder` campaign task (the `Campaign:` paragraph at line 61): extend the Cast sentence with "exactly one cast member has Merchant true: the merchant who financed the venture and follows the party down to trade; a man or a woman with a Voice of that sex; Secret empty; never placed as a floor NPC, never given a quest or a Betrayal" and add a `Merchant` paragraph naming the section's shape: `Briefing` (an NpcScript of four nodes with ids hire, quakes, deal, sendoff, two choices each, every path reaching deal; the merchant hires the party in a tavern above the dungeon, admits he will not go down himself, explains the fee and the trade, never states the Mystery's Truth), `FloorLines` (one entry per floor 1 to 20 with Greeting, Progress, Trade, each at most 220 characters, an arc from bluster to unease to fear that hints at the Mystery without stating it), `Gathering` (three lines about enemies gathering outside his door). Keep the planner's `[AiDescription]`s on the DTOs (plan C) in agreement with this wording.

- [ ] **Step 4: Run the tests to verify they pass**

Run: the Step 2 command. Expected: PASS (2 tests).

- [ ] **Step 5: Run the suite and commit**

Run: `cd tests && dotnet test --nologo -v q` Expected: no failures.

```bash
git add src/Adventure/Core/Ai/PromptBuilder.cs src/Adventure/Core/Design/OfflineDesigner.cs
git commit -m "feat(campaign): the planner is asked for exactly one merchant and his lines; the offline designer provides them"
```

---

### Task 3: Master Oswin Pellet and his lines in the shipped campaign

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (a sixth `Cast` entry; a new `Merchant` section)
- Modify: `docs/superpowers/specs/2026-10-22-campaign-gullet-of-gorm.md` (the cast table at line 67: add Pellet's row)
- Test: `tests/Adventure/GulletCampaignTests.cs`

**Interfaces:**
- Consumes (plan C): JSON shape of `MerchantPlan` as `PlanFormat` serialises it (property names `Briefing`, `FloorLines`, `Gathering`, `Floor`, `Greeting`, `Progress`, `Trade`; `NpcScript` as `{ "Start": "hire", "Nodes": [ { "Id", "Line": { "Text", "VoiceId": null, "Delivery": null }, "Choices": [ { "Text", "Next", "Effect": "None", "Flag": null, "RequiresFlag": null, "ForbidsFlag": null, "NextIfRefused": null } ] } ] }`, exactly as `floor_01.json` writes Crane's script).
- Produces: the cast entry `{ "Id": "cast_pellet", "Name": "Master Oswin Pellet", "Merchant": true, "Role": "The factor who financed the council's contract and follows the money down, one locked room at a time", "Secret": "", "VoiceId": null, "Persona": { "VoiceStyle": "a Dunmarrow factor: fussy, money-minded, brave in sentences and nowhere else", "DefaultDelivery": "cheerful", "Voice": "Achird", "IsEmpty": false } }` and a `Merchant` section with 4 briefing nodes, 20 `FloorLines`, 3 `Gathering` lines (67 lines, `VoiceId` null until Task 4).

- [ ] **Step 1: Write the failing tests** (append to `GulletCampaignTests`, which already loads `campaign.json` and `floor_NN.json` through `PlanFormat`)

```csharp
private static readonly string[] Forbidden = { "god", "gorm", "verger", "soul", "memorial", "nineteen" };

[Fact]
public void TheMerchant_IsOswinPellet_AMan_OnAchird_AndNobodyElseUsesIt()
{
    var c = Campaign();
    var m = Assert.Single(c.Cast, x => x.Merchant);
    Assert.Equal("cast_pellet", m.Id);
    Assert.Equal("Master Oswin Pellet", m.Name);
    Assert.Equal("Achird", m.Persona!.Voice);
    Assert.Equal("", m.Secret);
    Assert.DoesNotContain(c.Cast, x => !x.Merchant && x.Persona?.Voice == "Achird");
    Assert.Equal("male", RunwareVoices.Gender("inworld:tts@1.5-max", "Oliver"));
}

[Fact]
public void TheMerchant_HasGreetingProgressAndTrade_OnEveryFloor_AndThreeGatheringLines()
{
    var m = Campaign().Merchant!;
    Assert.Equal(Enumerable.Range(1, 20), m.FloorLines.OrderBy(f => f.Floor).Select(f => f.Floor));
    Assert.All(m.FloorLines, f => { foreach (var l in new[] { f.Greeting, f.Progress, f.Trade }) Assert.InRange(l.Text.Trim().Length, 8, 220); });
    Assert.Equal(3, m.Gathering.Count);
}

[Fact]
public void ShippedBriefing_EveryPathReachesDeal()
{
    var s = Campaign().Merchant!.Briefing;
    Assert.Equal(new[] { "hire", "quakes", "deal", "sendoff" }, s.Nodes.Select(n => n.Id));
    Assert.All(s.Nodes, n => Assert.Equal(2, n.Choices.Count));
    var byId = s.Nodes.ToDictionary(n => n.Id);
    void Walk(string id, HashSet<string> seen, bool sawDeal)
    {
        sawDeal |= id == "deal";
        var node = byId[id];
        if (node.Choices.All(ch => ch.Next == null)) { Assert.True(sawDeal, $"a path ends at {id} without passing deal"); return; }
        foreach (var ch in node.Choices) if (ch.Next != null) { Assert.True(seen.Add(ch.Next) || ch.Next == "deal", "the briefing loops"); Walk(ch.Next, new HashSet<string>(seen), sawDeal); }
    }
    Walk(s.Start, new HashSet<string> { s.Start }, false);
}

[Fact]
public void ShippedMerchant_NeverSaysTheTruth()
{
    var lines = Campaign().Merchant!.AllLines().Select(l => l.Text.ToLowerInvariant()).ToList();
    Assert.Equal(67, lines.Count);
    foreach (var word in Forbidden) Assert.DoesNotContain(lines, t => t.Contains(word));
}

[Fact]
public void TheMerchant_IsNeverAFloorNpc()
{
    for (var f = 1; f <= 20; f++) Assert.DoesNotContain(Floor(f).Npcs, n => n.CastId == "cast_pellet");
}

[Fact]
public void MerchantSection_RoundTripsThroughPlanFormat()
{
    var c = Campaign();
    var again = PlanFormat.Deserialize<CampaignPlan>(PlanFormat.Serialize(c));
    Assert.Equal(c.Merchant!.AllLines().Select(l => l.Text), again.Merchant!.AllLines().Select(l => l.Text));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~GulletCampaignTests"`
Expected: the six new tests FAIL (no merchant in the cast, `Merchant` null); the existing Gullet tests still PASS. If `RunwareVoices.Gender(..., "Oliver")` returns `"unknown"`, confirm Oliver on Inworld's voice catalogue (a male voice) and add him to `InworldMen` in `RunwareVoices.cs` so the sex check is explicit.

- [ ] **Step 3: Write the cast entry and the lines**

Add the `cast_pellet` entry above to `Cast` in `data/adventures/builtin/campaign.json` and the `Merchant` section. The implementer writes the words, in the voice given in Global Constraints, in the register of the existing cast lines (read Crane's and Ulf's for the wry tone; Pellet is fussier than Crane and far less brave). What each part must do:

- **Briefing** (`Start: "hire"`): `hire` — he has hired four to clear the Gullet to the bottom; he is paying because he will not be going down himself and says so; replies: sceptical / brisk. `quakes` — the town's version: the ground shakes, the quakes come oftener, the council calls it drainage; his version: clear the tunnels and he will make them into fine dry lodgings with a view of nothing; replies: ask about the quakes / ask about the money. `deal` — the council's advance, through him, less his cut, counted out now as seed money; whatever they find below they sell to him, whatever he has they may buy; he will be waiting behind a locked door on every floor, "I am paying for the door"; replies: take it / haggle (both continue). `sendoff` — the bread-in-the-cellar line belongs to Crane, so Pellet's is about receipts; replies: two farewells, `Next: null`, `Effect: "Farewell"`. Every path from `hire` passes through `deal` (hire → quakes → deal → sendoff, or hire → deal → sendoff).
- **FloorLines** 1..20, each with `Greeting` (said from behind the door as the party nears: "In here. Quickly, and mind the step." on floor 1), `Progress` (his answer to "How is it going?"), `Trade` (as the shop opens). Arc: floors 1 to 6 bluster and property, counting profit; 7 to 13 unease: a tunnel not on his survey, the quakes keeping time like a clock, his investors asking questions, a door he did not pay for; 14 to 19 fear: he has seen the digging himself, "they are not spreading out, they are coming up", he no longer talks of lodgings; 20 a send-off: he is going back up to count what is left and would be obliged if they came too. Fear floors carry `Delivery` `weary`, `whisper` or `frantic`.
- **Gathering** three lines, in order, as 2, 4 and 6 creatures gather outside: mild, worried, alarmed.
- Add Pellet's row to the cast table in `docs/superpowers/specs/2026-10-22-campaign-gullet-of-gorm.md` beside line 67: `| **Master Oswin Pellet** | The factor who financed the council's contract; sells and buys from a locked room on every floor | none; a coward, honestly | merchant | Fussy, Craven | tavern, every floor (safe room) |`.
- The owner may read the lines before the bake: print `AllLines()` with `python3 -c` or paste the section in the hand-over message.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~GulletCampaignTests"`
Expected: PASS, all of them. Then run the campaign validator on the shipped data the way `tests/Adventure/PlanValidatorTests.cs` does for a floor: `PlanValidator.Validate(layout, Floor(1), menu, campaign: Campaign())` must report no repair mentioning `merchant` (plan C's rule: one merchant, never a floor NPC).

- [ ] **Step 5: Run the suite and commit**

Run: `cd tests && dotnet test --nologo -v q` Expected: no failures.

```bash
git add data/adventures/builtin/campaign.json docs/superpowers/specs/2026-10-22-campaign-gullet-of-gorm.md tests/
git commit -m "feat(campaign): Master Oswin Pellet, the factor who financed the Gullet, and the 67 lines he speaks"
```

(`tests/` is git-ignored; the `git add tests/` is a no-op kept so the command is the same on a machine that tracks them.)

---

### Task 4: Bake the merchant's voice into the shipped bank

**Files:**
- Modify: `data/adventures/builtin/campaign.json` (the 67 `VoiceId`s, merged from the bake output)
- Create: `assets/voice/builtin/<id>.wav` (67 files) and their `.import` files
- Modify: `docs/development.md` (the Voice section: the cast bake now covers the `Merchant` section)
- Modify: `/home/eric/.claude/projects/-mnt-Transfer-5FD1E1415AC80F29-dev-DungeonCrawler/memory/character-sexes-for-voice.md`
- Test: `tests/Adventure/GulletCampaignTests.cs`

**Interfaces:**
- Consumes: Task 1's bake inclusion; the existing flags `--bake-cast-voice=DIR`, `--check-voice-bank`.
- Produces: every merchant line has a `VoiceId` whose wav is in `assets/voice/builtin`.

- [ ] **Step 1: Write the failing test** (append to `GulletCampaignTests`, modelled on `EveryHero_HasTwentyVoicedDanceLines_InTheShippedBank`)

```csharp
[Fact]
public void EveryMerchantLine_IsVoicedInTheShippedBank()
{
    var bank = Path.Combine(Root, "assets", "voice", "builtin");
    var lines = Campaign().Merchant!.AllLines().ToList();
    Assert.Equal(67, lines.Count);
    Assert.All(lines, l => { Assert.False(string.IsNullOrEmpty(l.VoiceId), $"no voice id: {l.Text}"); Assert.True(File.Exists(Path.Combine(bank, l.VoiceId + ".wav")), $"no audio: {l.Text}"); });
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd tests && dotnet test --nologo -v q --filter "FullyQualifiedName~EveryMerchantLine_IsVoicedInTheShippedBank"`
Expected: FAIL on the first line's missing `VoiceId`.

- [ ] **Step 3: Bake**

```bash
dotnet build DungeonCrawler.csproj --no-incremental -v q --nologo -clp:ErrorsOnly
export RUNWARE_API_KEY=$(grep '^runware=' ~/snap/code/current/.local/share/godot/app_userdata/DungeonCrawler/keys.cfg | cut -d'"' -f2)
godot --headless --path . -- --builtin --ai --voice-provider=runware --voice-model=inworld:tts@1.5-max --voice-model-minor=inworld:tts@1.5-max --bake-cast-voice=assets/voice/builtin 2>&1 | grep -E "^\[cast-voice\]|RESULT"
```

Expected: `[cast-voice] N cast and boss lines ...; 67 to render` (the already-voiced cast lines keep their ids and are not re-rendered; only the merchant's 67 are pending, give or take any line whose text changed), then `[cast-voice] rendered 67, failed 0`, `RESULT ok`, and `wrote the plans with their voice ids to assets/voice/builtin/cast-plans`. Never echo the key.

- [ ] **Step 4: Merge the ids and tidy**

Copy the `Merchant` section (and only it: the `Cast` entry's `VoiceId` stays null like the others) from `assets/voice/builtin/cast-plans/campaign.json` into `data/adventures/builtin/campaign.json` (a short `python3 -c` that loads both, replaces `Merchant`, and writes with the same indentation as the file uses); delete `assets/voice/builtin/cast-plans`; `git checkout -- data/adventures/builtin/floor_*.json` if the bake touched them; if a rendered wav landed in `~/.local/share/godot/app_userdata/DungeonCrawler/voice/` instead of the bank, copy it into `assets/voice/builtin/` by id; then `godot --headless --path . --import > /dev/null 2>&1` so the `.import` files exist, and
`godot --headless --path . -- --check-voice-bank 2>&1 | grep voice-bank`
Expected: `refers to N voice lines: N in the shipped bank, N with audio anywhere, N load` and `RESULT ok`, with N = the previous count + 67.

- [ ] **Step 5: Verify the sex of the new recordings**

Pick three merchant wavs and measure the median fundamental the way the 2026-10-30 check did (autocorrelation median f0 over the wav; a `python3` one-off reading the 16-bit PCM). Expected: 70 to 145 Hz, the men's band. A reading in the women's band (160 to 235 Hz) means the voice mapping is wrong: stop and fix `RunwareVoices` before anything ships.

- [ ] **Step 6: Run the test and the suite**

Run: `cd tests && dotnet test --nologo -v q` Expected: `EveryMerchantLine_IsVoicedInTheShippedBank` PASS and no failures.

- [ ] **Step 7: Docs and memory**

`docs/development.md`, Voice section: one sentence that `--bake-cast-voice` also renders the campaign's `Merchant` section (briefing, twenty floors, gathering lines) with the merchant cast member's voice. In the memory file `character-sexes-for-voice.md`, replace the "planned" bullet for Oswin Pellet with the fact: `Master Oswin Pellet = man (the shipped merchant; Gemini Achird, Inworld Oliver), 67 lines voiced 2026-11-0X, pitch checked`.

- [ ] **Step 8: Commit**

```bash
git add data/adventures/builtin/campaign.json assets/voice/builtin docs/development.md
git commit -m "feat(voice): the merchant's 67 lines voiced into the shipped bank"
```

---

## Self-review notes

- Spec coverage: section 6 (the `Merchant` section, the arc, no truth, voiced with the existing bake, generic bank for other campaigns, planner prompt) → Tasks 1 to 4; call 1 (Pellet, not Crane; Crane untouched) → Task 3's cast entry and `TheMerchant_IsNeverAFloorNpc`; call 2 (briefing inside the council frame, no re-render) → Global Constraints and Task 3's briefing brief, Task 4's rendered count.
- Type consistency: `MerchantPlan.AllLines()` is produced in Task 1 and consumed in Tasks 3 and 4; `CampaignPlan.MerchantCast()` comes from plan C and is only consumed here. Names of C's DTOs are used exactly as the contract gives them.
- Not in this plan: the generic bank's text (`MerchantLineBank.Generic`, plan C), the merchant NPC's placement and dialogue screen (plan C), the tavern scene that plays the briefing (plan D).
