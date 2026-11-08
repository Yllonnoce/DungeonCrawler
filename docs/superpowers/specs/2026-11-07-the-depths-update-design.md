# The Depths update: design

Date: 2026-11-07. Status: decided by the game developer (Claude) under the owner's blanket mandate of 2026-11-07
("full control... make this the best game possible... do not prompt me"). Builder: anyone who knows this codebase's
conventions (docs/development.md). This document says what is decided and why; the plans under
docs/superpowers/plans/2026-11-07-* say how.

## Intent

The owner's words: the game is "pretty good" and should become compelling to "a more seasoned player"; make it "as
funny, interesting, and or compelling" as possible; "do research on other genre specific games and dig deep"; "go
extreme".

Six audits of the code (combat, items and progression, run structure, floor content, writing, and a genre survey of
Grimrock, Barony, Hades, Dead Cells, Slay the Spire, Risk of Rain 2, Darkest Dungeon, Diablo, Path of Exile,
Vermintide, Deep Rock Galactic and recent indie crawlers) agree on the diagnosis. The foundation is unusually deep:
twenty voiced floors, six floor shapes, four objective kinds, lethal traps, hordes, safe rooms, a merchant with an arc,
a villain who taunts, co-op on a dedicated server. What a veteran would find thin is everything that happens *between*
those systems and the player's hands:

- **Combat reads as text.** No damage numbers, no enemy or boss health bars, no telegraphs before a swing. The only
  defensive verb is the Warrior's block. Enemies have two behaviours (walk-and-hit, stand-and-shoot). No elites, no
  named rares, bosses have one move and no phases. Accuracy and evasion are pinned at the 98% cap on both sides.
- **Loot stops improving at floor 8** (affix tiers end at item level 8, bases are all unlocked by level 5, base damage
  never scales). Legendaries are stat blocks with no effects. Skill trees are twelve straight chains, half stat
  padding. The Rogue's Parry node can never fire. Gold has nothing to buy that matters past the shop uniques.
- **Floors vary in shape, not in content.** Every chest on a floor rolls the same table, so a vault, a secret alcove, a
  cellar and a corridor chest are identical. Locks are a time tax (unlimited free lockpick retries). There are no
  shrines, boons, curses, mimics, gambles or optional hard rooms. Room roles ("Shrine", "Treasury") are names only.
- **Every New Game is the same run** (seed 7319), nothing persists across runs, and a wipe costs nothing. Worse, a
  wipe on an unsaved floor rebuilds it as a first visit: respawned enemies, unopened chests, free XP and loot.
  Victory is a small panel with the wrong boss's name and is not saved.
- **The heroes never speak to each other.** Each has a few hundred good lines, but they are four parallel monologues,
  and the lines that react to real moments (hurt, loot, downed) run out after three uses. Nothing reacts to the
  specific thing that just happened.

The update is seven pillars that fix each of those while keeping everything that works. The guiding rule from the
genre survey: **veterans want variety and legible risk, not stat inflation.** Every addition widens the pool of things
that can happen, makes a risk visible before it is taken, or lets the party react to it.

## Goals

- A fight is readable without the log: you see the wind-up, the number, the bar, the crit, the phase.
- Every class has a defensive verb with a skill ceiling (perfect block, parry, dodge) and heavy hits matter (stagger).
- No two floors meet the same creatures: named elites with readable affixes, three new enemy behaviours, bosses with a
  second phase, bosses in the endless depths.
- Loot keeps improving to floor 20 and beyond; a legendary changes how you play; every chest kind has a reason to exist.
- A floor offers decisions that trade risk for power: shrines with boons and prices, cursed chests, mimics, gambles,
  secret rooms worth finding, locks that cost something to pick.
- Run two is a different run; dying has a cost and a story; the game remembers your deeds and widens the pool for them.
- The heroes are a party: they talk to each other, react to what just happened, run gags, answer a cheer, and get an
  epitaph when they fall.
- Co-op friends stop fighting over the stairs and can point at things.
- All of it works in solo, in co-op on the dedicated server, in the shipped campaign and in offline or AI campaigns,
  and every shipped floor still loads with no repair line.

## Non-goals

- No new 3D art beyond tints, scales, particles, decals and built meshes. Existing models only.
- No voice spend: new lines ship as subtitles until the owner bakes them (the bake flags exist; the cost is theirs).
- No crafting or enchanting at the merchant (owner's 2026-11-02 rule: he buys and sells finished items). Gambling is
  buying a finished item blind, so it is allowed.
- No set items, no sockets, no durability, no stash, no identification mechanic.
- No new objective kinds, no new floor shapes, no rewrite of the planner schemas beyond additive optional fields.
- No installers (dist/) and no server release in this update; the owner asks for those.

## Anti-goals (what would make this a failure even if it technically works)

- Noise: damage numbers or barks so frequent they become wallpaper. Every new feedback element has a rate limit or a
  rarity rule, and the subtitle strip never shows two speakers at once.
- Stat inflation dressed as content: an elite that is only "more HP", a legendary that is only "+8".
- Risk that is not legible: a cursed chest must say it is cursed; a mimic must have a tell for a Perceptive member;
  a pact must state its price in the tavern.
- Breaking the shipped campaign: any generator change is followed by the rehash-and-sweep in docs/development.md.
- Dead settings or dead nodes: anything added must be reachable in play and proven by a test.
- Humour that leaks: stock banks (used by AI and offline campaigns) must not name Gorm, the Verger or Ashmere.

## Constraints

- Godot 4.7.2 .NET, C#; engine-free rules in `*/Core` namespaces with xUnit tests in `tests/` (dev machine only, not
  in git); headless `--test-*` flows in `src/Core/DebugTools.cs` prove the Godot side. Baseline: 3757 tests pass.
- Co-op is server-authoritative. Anything a client sees that the server decides (an elite's affixes, a boon taken, a
  mimic springing, a pact) travels in an existing or new message in `src/Net/Core/Messages.cs`; floors stay built
  identically on every machine from seed and plan, so anything rolled at build time is rolled from `(GameSeed, floor,
  spawn index)` and never from `Random.Shared`.
- Save format is v10 today; this update bumps it once to v11 with a migration (`SaveMigrations`), adding: pacts,
  boons and curses, the elite roster of a floor, mimic and cursed-chest state, secrets-found counts, the run ledger,
  and `Victory`. A v10 save loads with none of them and plays on.
- Keys: new verbs take unbound keys and join `Settings.Rebindable`: dodge **Left Alt**, cheer **V**, ping **X**.
  Right mouse keeps guard on blocking classes and becomes parry on the others.
- Numbers below are the shipped defaults. Where a number is a guess it is marked (tune), and the plan gives it a
  constant in a `*Rules` class so a later tuning pass is one line.
- Text and recorded voice must agree (memory: character sexes for voice): Brand, Wren, Ilex are men; Sable is a woman.

## The pillars

### 1. Combat you can read (plan: combat-feel)

**Feedback.**
- Floating damage numbers over enemies (`Label3D`, pooled, 0.9 s rise and fade): white for a hit, gold and 1.6× size
  for a crit or a perfect blow, small and tinted for a status tick (red bleed, green poison, orange burn, blue frost),
  grey "Blocked" when a shield eats it. Numbers within 0.15 s on the same target merge into one. Setting "Damage
  numbers" (on by default). Driven by `EventBus.EnemyHurt` (already fires on server, solo and clients).
- Crosshair hit marker: four ticks flick outward for 0.12 s on `HitLanded`; a red X variant on a kill.
- Enemy health bars: a 3D billboard bar over any enemy hurt in the last 4 s, 1.2 m above its collider, with its name
  for elites and bosses. Hordes get none (cost and noise).
- Boss bar: top centre, name and title, HP, a tick at 50%, flashes at the phase change, hidden on death. Elites use a
  slimmer version while in sight. Driven by `BossSeen`, `EnemyHurt`, `BossHalf`, `BossDefeated`.
- Taking damage: a short red vignette scaled by the fraction lost, and the portrait flashes. A heavy hit received
  (≥15% max HP) shakes the camera briefly (`Tremor.ShakeOnly`).
- Level-up: a banner across the top ("Brand reaches level 7"), a chime, the portrait pulses gold for 2 s.
- Loot toast: a Rare or Legendary pickup prints a coloured toast beside the portraits. Legendary drops on the floor
  get the beam and a chime; Rare keeps its small light (the fanfare is the scarcity).

**Telegraphs.**
- A melee enemy entering `AttackState` plays its attack clip from the start of the wind-up, time-scaled to the
  wind-up, and its body tints toward white for the last 0.2 s; the damage lands where the clip's strike frame falls.
  Shooters glow at the hand for the wind-up.
- Boss signatures draw a ground decal for the telegraph delay: a red ring for Slam (its radius), a red lane for Charge,
  a pulsing circle at the boss for Volley and Summon. The delay rises from 0.8 to 1.1 s (Charge 0.4 to 0.7 s) so the
  decal can be read (tune).

**Verbs.**
- **Dodge** (Left Alt, every class): a 0.25 s burst in the movement direction (or back when still), 20 stamina, 0.6 s
  cooldown; the body is untargetable by melee strikes and projectiles for the burst. Companions dodge a telegraphed
  boss signature when on Defensive or Aggressive (50% of the time, so bosses still hurt).
- **Perfect block** (Warrior, existing guard): a hit that lands in the first 0.2 s of the guard costs no stamina, is
  negated, and staggers the attacker 1.2 s. `Riposte` and `SecondWind` fire on it as on a block.
- **Parry** (Right mouse on Rogue, Ranger, Mage): a 0.25 s window; a melee hit that lands in it is negated and the
  attacker is staggered 1.2 s; 10 stamina per attempt, 0.8 s cooldown; a missed parry is just the stamina. Projectiles
  are not parried. The Rogue's `Parry` node (Riposte 0.5) now fires on a parry, which makes the dead node live.
- **Stagger**: a heavy or perfect blow on a non-boss enemy resets its wind-up and plays HitReact (it cannot swing for
  the clip); a boss is staggered only by a perfect block or a parry. Elites with `Juggernaut` ignore stagger.

**Numbers and statuses.**
- Hit chance becomes `clamp(0.72 + (Acc − Eva) × 0.006, 0.35, 0.95)` so a 20-point gap is 12%, and the cap leaves a
  1-in-20 miss on both sides (tune).
- Frost scales enemy wind-up and recovery by its attack-speed factor (`AttackSpeed` is read where `WindupSeconds` is).
- Combos: **Shatter** (a crit or heavy on a Frosted target deals +50% and consumes the Frost; the number says
  "Shatter"); **Ignite** (Burn applied to a Poisoned target bursts 2 m for the poison's magnitude × 4, once); **Bleed**
  halves healing received. New status **Vulnerable** (+25% damage taken, 6 s) from elite curses, shrine boons and the
  Mage's Deep Freeze on a stun.
- `TriggerKind.Thorns` is granted by the Warrior's Bulwark tier 2 (Second Wind becomes Second Wind + Thorns 4).
- Resists above 75 in data are clamped in data too (80 → 75), so the files say what happens.

### 2. Creatures worth meeting (plan: elites-and-bosses)

**Elites.** From floor 2, one planned (non-horde, non-reinforcement) creature per floor is an elite; two from floor 8,
three from floor 14 (tune). The choice and the affixes are rolled from `(GameSeed, floor, spawn index)` in the
resolver, so every peer agrees and a reload gives the same elite. An elite has 1 affix on floors 2–7, 1–2 on 8–13,
2–3 from 14, drawn without conflicts from:

| Affix | Effect | Tell |
|---|---|---|
| Swift | move ×1.35, wind-up ×0.8 | yellow tint, dust at the feet |
| Hulking | HP ×2.2, scale ×1.3, damage ×1.25 | size |
| Warded | a 25% max-HP shield that regrows 10 s after it breaks | blue shell |
| Vampiric | heals 40% of damage dealt | red glow on hit |
| Volatile | explodes on death: 4 m, 60% of its hit, Burn | orange pulsing |
| Splitting | on death spawns two of the tier's horde brute | green seam |
| Thorned | melee attackers take 30% back | spines (built mesh) |
| Cursing | hits apply Vulnerable | purple trail |
| Shrouded | a 4 m dome: projectiles and spells fizzle at its edge; melee only | translucent dome |
| Hexed | melee attackers are Slowed 2 s on hit; shoot it | violet rings |
| Alarmist | on noticing the party, wakes everything on the floor within 25 m and shouts | a horn sound |
| Juggernaut | immune to stagger and stun, armor +20 | iron tint |

Shrouded and Hexed never roll together. An elite is named ("Grukk the Swift", "Mother Hask, Warded and Vampiric")
from a syllable table seeded like its affixes, prefixed by the campaign faction's style where the plan gives one. It
wears the slim boss bar with its affixes spelled out while in sight, and the first sight of each elite on a floor raises
`EliteSeen` (a hero reacts, see pillar 6). Kill reward: XP ×2.5 and one roll of `table.elite` (no Common) plus coins.
A dead elite raises `EliteSlain` for the villain and the ledger. The AI planner may name an elite and pick its affixes
through an optional `Elite` field on a roster entry; the validator fills or trims it.

**Boss phases.** Every tier guardian has two kits. At 50% HP (`BossHalf`, already an event): a 1.2 s roar (untargetable,
the decal flashes), then phase two: speed ×1.2, the second signature joins the first on a shared cooldown, and the
arena changes once:

| Boss | Phase 1 | Phase 2 adds | Arena change |
|---|---|---|---|
| Crypt Lord | Slam | Summon (2 horde husks… brutes from floor 8) | four braziers go out: darker arena |
| Broodmother | Volley | Summon (cave spiders, cap 4) | webs: three Slow patches on the floor |
| Iron Colossus | Charge | Slam | the ground glows: two Burn patches move slowly |
| Abyss Herald | Summon | Volley | gravity well: Slow aura 6 m around the boss |
| An endless-depth boss | the tier boss of the floor's shape | a random second signature | none |

Endless floors (21+) get a boss every five floors (25, 30, …): the Abyss Herald re-skinned by the floor's layout tint,
HP and damage scaled by the endless rule, named "the Nth Herald". Its death silences portals for five floors.

**Archetypes.** Three flags on `EnemyDef`, applied by data to existing creatures:
- `Kites`: a shooter that retreats to PreferredRange when the party is within half of it (skeleton_archer,
  abyss_archer, bandit_archer).
- `Support`: a caster that, with an ally within 8 m below 70% HP, casts a 30-point Shield or 3/s Regen on it every 8 s
  instead of attacking; attacks when alone (cave_shaman shields, forge_priest heals, bone_priest shields).
- `Bomber`: a horde creature that rushes and detonates (3 m, its damage ×3, no corpse): one per tier
  (`horde_*_bomber`, red-tinted, hissing), from floor 6 at 1 in 8 of a stream (tune).

### 3. Fortune and misfortune (plan: fortune)

**Boons and curses** are run-long modifiers on the party or a member, kept in the save (`AdventureDto.Boons`,
`Curses`), applied as `StatModifier`s, `AbilityUpgrade`s and `TriggerKind`s through the same pipeline skills use. The
registry is engine-free (`Adventure/Core/Fortune/Boons.cs`): 24 boons in four houses, in the voice of the world (the
dungeon is a drowned god's plumbing, so the houses are **the Ember** (fire, damage), **the Cold** (frost, control),
**the Toll** (gold, luck, loot) and **the Stone** (defence, endurance). Examples: *Ember's Due* (Burn you apply lasts
+3 s), *Kindled Blades* (melee hits have 20% to Burn), *Cold Comfort* (Frost on you is halved), *Toll Road* (+30%
gold found), *Pennies for the Ferryman* (dying members keep 1 HP once per floor), *Stone Lungs* (gas half as thick),
*Second Skin* (first hit on a floor is negated), *Hungry Edge* (kills restore 6 stamina). Each house has a **crown**
(offered only when the party holds two of its boons): *Ember Crown* turns Ignite into a 4 m nova; *Cold Crown* makes
Shatter chain to one more Frosted foe; *Toll Crown* doubles elite rewards; *Stone Crown* lets a perfect block or parry
heal 10.

Curses (8): *Hollow* (−20% max HP), *Miser's Mark* (prices ×1.4), *Loud* (detect range ×1.3), *Brittle* (traps and
hazards ×1.5), *Butterfingers* (lockpicks always fail), *Dim* (perception −20), *Marked* (one extra elite per floor),
*Thirst* (potions heal half). A curse lasts the run unless lifted at a shrine for 80 + 20×floor gold.

**Shrines.** `RoomRole.Shrine` becomes real: the generator reserves one shrine cell per floor from floor 2 (never the
start, exit, arena, safe room, jail or ritual shrine; the altar prop already exists). Interacting opens a choice card:
three boons (house-weighted by what the party holds, a crown when earned) and a **skip** that gives 40 + 10×floor gold.
One in three shrines asks a price before the card: **blood** (every living member loses 25% current HP), **gold**
(60 + 15×floor), or **a curse** (the card then shows four boons). The price is written on the altar before you touch
it. In co-op the server resolves the first player's pick and everyone sees the card close; the shrine is one use.
Taking a boon or curse raises `ShrineBoon` / `ShrineCurse` (barks, the villain's `ShrineUsed`).

**Treasure that differs.** New loot tables: `table.vault` (0 Common, Rare 55, Legendary 20, 3 rolls), `table.secret`
(Magic 40, Rare 45, Legendary 15, 2 rolls, one consumable), `table.cellar` (chest odds, +1 roll), `table.elite` (0
Common, Rare 50, Legendary 12, 1 roll), `table.boss` (Rare 50, Legendary 35, 3 rolls, item level +2). Vault, secret
alcove, cellar and ledge chests use theirs. Legendary drops get the beam.

**Mimics.** From floor 4, each ordinary chest has a 10% chance (20% from floor 12) to be a mimic, rolled from the
chest's spawn seed. A member whose Perception reaches it sees "the lock is breathing" in the prompt. Opening it spawns
the tier's brute at the chest with Hulking and 2× XP, the chest lid as a lure gone; the chest then yields its roll
when the mimic dies. Raises `MimicSprung`.

**Cursed chests.** One chest per floor from floor 5 is **bound**: an iron-banded chest with a purple seam and the
prompt "Bound chest: open and be Hunted?". Opening gives a `table.vault` roll and 60 s of *Hunted*: Vulnerable on the
party and an elite (Marked affix set) that spawns at the nearest room and comes. Never in the boss arena's room.

**Secrets worth finding.** Half of a floor's secret alcoves become secret rooms: `TryCarveSideRoom` behind the secret
wall holds a `table.secret` chest, a lore plaque (the campaign's mystery clue text when the floor carries one, else a
line from a stock bank of 40 dungeon epigraphs) and, on one floor per tier, a hidden merchant stall (the merchant's
stock rerolled at Rare odds, "I was never here"). The floor card (pillar 5) shows "Secrets 1/3", so nobody pixel-hunts.

**Locks that cost.** A failed lockpick makes noise that wakes unaware creatures within 10 m; the third failure jams the
lock (a key still opens it). Keys matter again.

**Gambling.** The merchant's shop gains a **Lucky Dip** tab: three cloth-wrapped bundles per floor (weapon, armour,
jewellery) at a fixed price (120 + 30×floor), rolled at item level floor + 2 with chest odds and a 10% legendary
floor; the bundle is revealed on purchase (its roll is seeded by floor and slot so a reload cannot reroll it). Raises
`Gamble` with win or dud.

**Small fills.** Mine drifts hold a cache (a breakable crate with `table.breakable`, one in three a locked chest) or an
ambusher. Junk sells for 2 gold and companions still pick it up.

### 4. Builds and loot that keep going (plan: items-and-builds)

- **Item level matters**: every affix gains tiers at item levels 12, 16 and 20 (values continue the curve); base
  weapon damage and armour scale by +4% per item level above the base's own level (`LootRoller.Generate`); new
  affixes for CritMultiplier (*Cruel*), ManaRegen (*of Reverie*), StaminaRegen (*of Breath*), ResistArcane (*of
  Dimming*), Perception (*of the Owl*), Cooldown ×0.9/0.85/0.8 (*of Alacrity*, suffix, rare weight).
- **Smart loot**: 60% of rolled bases are ones a living party member can wield or wear.
- **Legendary effects**: `LegendaryDef` gains `Trigger`, `TriggerMagnitude` and `Upgrades` (the `AbilityUpgrade` list
  skills use), and `PartyController.Trigger` sums equipped items with learned skills. Twelve new legendaries in the
  drop pool, each with an effect and a line of flavour, and the Warrior gap closed (a drop-pool sword, axe and mace):
  *Gorm's Molar* (mace: Stun 1 s on heavy, Shatter also stuns), *The Alderman's Seal* (ring: +25% gold, shop sells +20%),
  *Gerald* (amulet, "a puddle, bottled": below 30% HP once per floor, Frost Nova around you), *Pellet's Receipt* (ring:
  the Lucky Dip costs 30% less, +2 LCK), *Lantern-Jaw* (axe: kills heal 8, Bloodlust), *Levy Steel* (sword: perfect
  blows Cleave), *Deacon's Patience* (staff: Mend also shields 15), *Hush* (dagger: sneak attacks from Frosted targets
  ×1.5 and never wake kin), *Sister's Thimble* (gloves: parry window +0.1 s), *Old Ulf's Chin* (helm: stagger
  immunity 3 s after being downed… and +20 HP), *Toll Road Boots* (feet: +12% move, Haste on kill), *Candle of the
  Congregation* (focus: Burn you apply spreads to one neighbour).
- **Skill trees**: every branch's tier 6 becomes a **keystone** that trades something (e.g. Warrior Berserker:
  *Red Mist*: +30% melee damage, cannot guard; Mage Frost: *Absolute Zero*: Frost lasts twice as long, Fireball costs
  double); one **duo node** per class at tier 3 requires a node from a second branch (an OR on the prerequisite list,
  the UI draws it between columns). Beastcraft is renamed **Wild Hunt** (there is no beast) and its *Alpha* gives
  Shatter on arrows instead of a second Rampage. `Parry` fires on a parry (pillar 1). Capstone heals rise to 15.
- **XP curve**: `25L² + 50L`, cap 30: a campaign ends around level 18–20 (tune), so 17–19 skill points buy two
  branches and a keystone. Existing saves keep their level and XP.
- **Shop and bag UI**: shop tooltips compare like bag tooltips; a cell shows a green ▲ when the item beats what the
  focused member wears and a small class glyph row for who can use it; stacks are priced by count (the bug); buyback of
  the last three sales at sell price.

### 5. Descents that differ, deaths that count (plan: run-structure)

**Title screen**: *Story* (the shipped campaign, as today), *New Descent* (a random seed through the offline designer,
or the AI planner when a key and the option are set), *Daily Descent* (seed = the UTC date; two fixed clauses; one
attempt per day recorded), *Load*, *Join Server*, *Records*, *Settings*, *Quit*.

**Clauses** (the pact system, in the merchant's voice: the contract you sign in the tavern). Chosen on a card the
merchant offers after the briefing ("Read the clauses?"), or skipped. Each clause has a heat value and a plain
description; the total heat multiplies gold and XP found by `1 + 0.1 × heat` (tune) and is the score's multiplier.
Clauses are saved in `AdventureDto.Clauses` and shown in the pause menu. The server's `--clauses=a,b` flag sets them
for a co-op game.

| Clause | Heat | Effect |
|---|---|---|
| 1. Death Clause | 3 | Hardcore: a wipe ends the run (death card, slot marked fallen) |
| 2. Convenience Fee | 1 | shop prices ×1.4 |
| 3. Inventory Audit | 2 | the lowest-level equipped item on each member loses an affix at each tier gate |
| 4. Full Occupancy | 1–2 | +25% creatures per rank |
| 5. Hostile Tenants | 1–2 | +1 elite per floor per rank |
| 6. Expedited Delivery | 1–2 | portals start at 45 s and shrink twice as fast, per rank |
| 7. Hazard Pay Waived | 1–3 | +20% damage taken per rank (stacks with lethality) |
| 8. No Rest for the Hired | 2 | no safe rooms (the merchant stands in the open by the stairs) |
| 9. Open-Door Policy | 1 | ambushes doubled |
| 10. Fine Print | 1 | shrines offer two boons, not three |

The first clear at each heat total per mode writes a record; heat 5 and 10 unlock cosmetics for the records page.

**Stakes.** On a wipe the floor is captured first (`FloorStateCapture`) so nothing respawns; the party loses 20% of its
gold and takes the *Scar* curse (−10% max HP) until the next floor (tune); the villain gloats; a **death card** shows
the epitaph (pillar 6), the killer, the floor and the wipe count, then "Rise" returns the party to the up stairs as
today. Under the Death Clause the card's button is "To the Records" and the save slot becomes a memorial that Load
shows greyed with the epitaph. Solo autosaves to the `auto` slot on every floor change and every three minutes.
Quickload is refused under the Death Clause.

**Cards.**
- **Floor card** on every descent (4 s, Esc to dismiss, never blocks input): floor name, time, kills, elites, secrets
  found/total, downs, gold found, the worst moment ("Sable fell to the Press"), and the party's reaction line.
- **Run recap** on victory and on a Death Clause end: floor by floor, per hero (kills, damage, downs, rescues,
  possession time), elites slain, boons taken, clauses, deepest floor, score, and in co-op a role line per player
  ("Most rescues: Ash"). Victory names the campaign's own boss and antagonist; `Victory` is saved.
- **Records** page from the title: best depth, fastest story clear, highest heat clear, daily results, elites slain,
  deeds. Endless depth is the score for the daily.

**Profile and deeds.** `user://profile.json` (atomic write like saves) holds records, the last twenty runs and the
deeds. Twenty-four deeds with in-world names ("Plumber": finish the story; "Jammed It": a barrel stops the Press;
"Gerald's Widow": Ilex names a tenth puddle; "Load-Bearing": kill an elite with Shatter; "Bang on Schedule": wipe four
times on one floor…). A deed unlocks something that widens the pool, never raw power: a clause, a legendary into the
drop pool, a banter set, a hidden-merchant stock line. Deeds pop as a toast and are listed on the Records page.

### 6. A party that talks (plan: writing)

- **Banter**: an engine-free `Banter` bank of exchanges (2–4 lines, speakers by roster name, with `MinFloor`, `Tier`,
  `RequiresFlag`, `OncePerRun`), played by `AdventureDirector` in place of one travel grumble in three, in a safe
  room after 8 s of rest, and in the tavern. Forty-five exchanges ship: every pair of heroes at least five, running
  gags with counters (`{count}`: Gerald I, II, "Gerald's widow"; Brand's door count; Sable's tally of empty rooms),
  callbacks to what the party has done this run (a mimic, a wipe, a shrine, the Lucky Dip). Lines queue through
  `PlaySoon` with a 1.4 s gap and stop if a fight starts. Marked said through `BarkRepeat` by exchange id.
- **New triggers** (`BarkTrigger`): TrapSprung, Fell, PerfectBlow, Parried, Mimic, SecretFound, EliteSeen, Broke
  (cannot afford), ShrineBoon, ShrineCurse, GambleWin, GambleDud, LowHp (first time under 25% on a floor), Cheer,
  CheerAnswer. Eight lines per hero per trigger, five per class and five generic as fallbacks, in each hero's voice.
  Hurt, Loot, Downed, Revived, BossSeen and LevelUp grow to eight per hero and use `ChooseOrOldest`.
- **Cheer** (V): the lead says a cheer line, every other living hero answers within 2 s with a `CheerAnswer` (one
  each, 1.2 s apart), the bodies play Cheer; 20 s cooldown; in co-op the server relays it (`EmoteEvt`) and each
  player's hero answers on their own screen. Deep Rock's salute, in a drowned church.
- **Epitaphs**: a wipe's death card draws from a bank keyed by cause (gas, press, spikes, boss, horde, elite, fall,
  mimic, bomber, generic) and escalates with the wipe count on that floor ("Fourth time on floor nine. He has started
  using pencil."). The villain's `PartyWiped` taunt plays over it.
- **Villain**: new `TauntTrigger`s EliteSlain, ShrineUsed, MimicSprung, AllyDead, AllyLeft, Gamble; two stock lines
  each in the generic bank, and the shipped Verger gets his own (written here, voiced later).
- **Elite introductions**: the name card plus a hero reaction (`EliteSeen`), and a line from the villain on its death.
- **Aside hook**: `DialogueNode.Aside { Member, Line }` plays a companion's remark after an NPC's line; the shipped
  campaign gets six asides (Sable after Crane's "plumbing": "He means us.").
- **Fixes**: floor 3's press line moves from MemberDowned to Crushed; the Possess lines are rewritten for a plain hero
  swap; stock banks lose their Gorm nouns; barks show in the subtitle strip with the speaker's name (one at a time,
  narration wins).

### 7. Friends (plan: co-op)

- **Stairs ready-check**: on a server with two or more players, a Yes on the landing starts "Descend? 1/3 ready" on
  every HUD; the floor changes when all living players are ready or 15 s pass with a majority; a No cancels. Solo
  and one-player servers behave as today.
- **Ping** (X): a marker where the lead is looking (2 s, on the minimap and in the world as a `Label3D` with the
  player's name), relayed as `PingEvt`, 1 s cooldown.
- Chat opens in solo and on the host too (it writes to the log).
- The recap's per-player lines (pillar 5) are the scoreboard; it praises roles, never ranks damage.

## What is left to the builder

- Exact prop meshes for decals, dome, spines and the bound chest's seam (built from the theme's materials like Rubble).
- The syllable table for elite names and the forty epigraphs' text (write them well; the register is dry).
- Where the floor card's "worst moment" is computed from (the ledger has downs, damage and hazard deaths).
- Whether the boon card is a `DialogueScreen` variant or its own control; it must work with mouse and 1–4.
- The order of the plans is fixed below; the task order within a plan is the plan author's.

## Plan order and files that collide

1. **combat-feel**, **items-and-builds**, **writing** in parallel (worktrees). Collisions: `PartyController.Trigger`
   (items adds equipped-item triggers; combat adds the parry and perfect-block trigger sites), `Adventurer.cs` (combat
   owns it this round; items only touches `Equipment`), `EventBus.cs` (writing adds the new events; combat raises
   HitLanded variants). Each plan lists the exact members it adds so a merge is mechanical.
2. **elites-and-bosses** and **fortune** in parallel after 1. Elites owns `Enemy.cs`, `AiState.cs`, `EnemyProfile`;
   fortune owns `Chest.cs`, `FloorGenerator*.cs`, `ShopRules`, `Boons`. Both add to `PlanResolver` (elite roll, shrine
   cell): fortune appends after elites.
3. **run-structure**, then **co-op**. Run-structure owns `Main.cs`, `MainMenu`, `Hud` cards, `SaveDtos`, the profile.
4. **sweep**: rehash the shipped plans, load all twenty floors with no repair line, fix by hand, run every `--test-*`
   flow and `scripts/net_test.sh`, update docs/development.md and README controls, commit, merge to main.
