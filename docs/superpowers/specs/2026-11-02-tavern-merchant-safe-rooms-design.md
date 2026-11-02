# Tavern, merchant and safe rooms: design

Date: 2026-11-02. Status: draft for the owner's approval. Builder: anyone who knows this codebase's conventions
(docs/development.md); the design says what is decided and why, and leaves the how to the plan.

## Intent

The owner's words, lightly ordered:

- A new game starts in a **tavern** above the dungeon, "a place outside the dungeon". The party listens to **the
  merchant**, who is paying them to clear the tunnels down to floor twenty because he is a coward and hires the work out.
  He hands over **seed money** and explains the deal: loot found below is sold to him, and he sells **weapons and
  supplies**, including unique weapons the dungeon does not drop.
- The town's story: the ground shakes and the quakes come more often. The merchant's angle: clear the monsters and
  turn the tunnels into high-end living space. **Hidden truth, told later in the campaign:** the evil below is building
  new levels upward to reach the town and the surface.
- Below, the merchant **comes to the party**: every floor has a **locked safe room** the monsters cannot enter, with
  him and his stock inside. The room is a **sandbox**: the party may heal in there as long as they like, but **the
  monsters mount their forces the longer they stay**.

## Goals

- A two-minute opening that sets the deal, the money loop and the public story before the first fight.
- A trade loop a player understands without reading: find, sell, buy better, go deeper.
- Unique weapons as the shop's prize, which also fills the gaps found on 2026-11-02 (no legendary sword, axe, mace,
  polearm or shortbow).
- One safe room per floor that is genuinely safe, genuinely tempting to rest in, and genuinely costly to linger in.
- The merchant as a character with an arc: bluster, then unease, then fear, never the reveal itself.
- All of it works in solo, in co-op on the dedicated server, in the shipped campaign and in AI or offline campaigns.

## Non-goals

- No town beyond the tavern's one room. No returning to the tavern after the first descent (the merchant comes down).
- No crafting, enchanting or item upgrading at the merchant: he buys and sells finished items only.
- No per-character money: the party already shares one bag.
- No new enemy types for the "mounting forces": existing enemies through existing spawn machinery.

## Anti-goals (what would make this a failure even if it technically works)

- A shop on every landing that feels like a vending machine: the room must read as a place, the merchant as a person.
- A safe room that is a free full heal: resting must change the floor the party walks back out into.
- The merchant giving away the campaign's truth, or contradicting the shipped campaign's own mystery.
- Dead settings: no field or stock entry that does nothing (see the 2026-11-02 audit).
- A tavern that blocks the player: talking to the merchant must be skippable without losing the seed money.

## Constraints

- Godot 4.7.2 .NET, C#; rules that can be engine-free live in `*/Core` namespaces and get xUnit tests in `tests/` (on
  the development machine only). Headless flows (`--test-*`) prove the Godot side.
- Co-op is server-authoritative: purse, stock, prices, door state and the pressure clock live on the server; clients
  send intents and receive syncs, as the bag does today (`BagMoveIntent` / `BagSync`).
- Save format gets a version bump with a migration: old saves load with an empty purse and fresh stock.
- Voice: the merchant's lines are rendered with the existing Runware bake like the rest of the cast; text and recorded
  voice must agree (memory: character sexes for voice). Windows scripts, if any, are `.bat`.
- Reuse before invention: vault rooms and `Door` for the safe room, `Reinforcements`/portal pressure for the mounting
  forces, `Npc` + `DialogueScreen` for the merchant, `InventoryScreen`'s slot controls for the shop, `Tremor` for the
  quakes, legendaries for unique weapons, the title screen's tavern tune for the tavern.

## Decisions

### 1. The tavern is floor 0

- `Game.CurrentFloor` may be 0. A new game begins on floor 0; `ChangeFloor(+1)` from 0 builds floor 1 exactly as
  today; floor 1 still refuses "up". Saves on floor 0 work like any floor. Co-op: the server starts on floor 0 and
  players join there until the first descent. The briefing is per-screen, not lead-only: each player is greeted at 4 m
  and briefed on their own machine; each one's deal node reaches the server, which pays the shared purse once; a late
  joiner is briefed too.
- The tavern is **one hand-dressed room**, not a generated floor: a `LayoutKind.Tavern` that the generator turns into a
  single room with the down stairs in a corner and no enemies, chests, traps or secrets. It uses the GridMap and a new
  theme `tavern` (wood plank floor, timber-and-plaster walls, beamed ceiling; Poly Haven CC0 textures through the
  existing fetch script). Dressing is the builder's: tables and benches, barrels and crates (existing models, not
  breakable here), a hearth using the `Fire` prop, lanterns, the tavern tune from the title screen, warm fog tint.
- The merchant stands at a table between the party's spawn and the stairs. Walking within 4 m the first time starts his
  briefing (`Npc.Converse` greeting, then the dialogue opens as it does for other NPCs). The briefing is a short
  dialogue: the hire and his admission of cowardice; the quakes and his property plan; the deal (seed money, sell to
  me, buy from me); the send-off. Each node has two replies so the player can be sceptical or brisk; none changes
  the outcome. **The purse is credited when the dialogue reaches the deal node, or on the first descent if the player
  never talked**, so the money cannot be missed and the tavern cannot block anyone.
- The stairs prompt reads "Go down into the tunnels?" on floor 0.
- **The briefing fits the shipped campaign's frame**, so no shipped line changes and nothing already voiced is
  re-rendered: the merchant speaks of the council's plumbing contract he has financed, the fee he is advancing, his own
  property hopes, and the quakes; the dungeon's own first lines ("Nobody said god. I said plumbing.") then follow
  naturally.

### 2. Money

- One shared party purse, whole coins, called **gold** in text and HUD ("120 gold"). Shown beside the bag's weight or
  slot count in the inventory and as a one-line HUD note when it changes ("+14 gold").
- **Seed money: 150 gold** (owner may change).
- Coins in the dungeon: a chest always holds coins, `10 + 6 × floor` to `25 + 10 × floor`; an ordinary enemy drops coins
  one time in four, `2 + floor` to `6 + 2 × floor`; a tier boss drops `150 × tier`. Coins are a loot drop that walks
  into the purse on pickup (no bag slot). Companions pick coins up as they pick up loot.
- **Selling:** an item's value is `base × rarity × (1 + 0.15 × itemLevel)` with base by kind (weapon 20, body armour
  18, other armour 10, offhand 12, ring/amulet 15, consumable 4, arrow bundle of 10 = 3), rarity Common 1, Magic 2.2,
  Rare 4.5, Legendary 12, rounded down. **Buying** costs `2.5 × value`. Keys and quest items cannot be sold; the Tome
  of Unlearning can. The formula is engine-free (`Items/Core/Prices.cs`) and unit tested.

### 3. The safe room

- Every floor from 1 to 20 has exactly one safe room. The generator picks a dead-end room that is **not** the arrival
  or departure stairs room, not a vault, not a nest, and whose door cell lies **within 10 cells of the shortest route**
  between the two stairs, preferring the room nearest the midpoint of that route. Rooms are tagged in the layout
  (`FloorLayout.SafeRoom`), so the choice is seeded and identical on every peer and reload.
- The door is a new `VaultKind.SafeDoor`: shown from the start (not hidden in the wall), an iron-banded door with a lit
  lamp beside it. It **opens when a living party member comes within 2.5 m and closes 2 s after the last member is
  more than 3 m away**; enemies never open it, never path through it (safe room cells are blocked in the enemy A*
  grid like prop cells) and are never placed, ambushed, portalled or horded inside it.
- The room is **sealed against rising gas**: on Toxic floors its cells never gas (`GasField` exempts them) and the
  HUD shows clean air inside; the merchant stands in clean air. On floors where the objective moves the stairs, the
  route used for placing the room is the one between the arrival stairs and the down stairs as they stand when the
  floor is built.
- Inside: the merchant, his table, a bedroll and a lantern; the party's HUD shows "Safe room" while any member is
  inside. From the corridor, the lamp and a line from the merchant when the party first comes within 8 m ("In here.
  Quickly, and mind the step.") announce him.
- **Rest:** while inside, every party member's HP, stamina and mana regenerate at **six times** the normal rate.
  Downed members are not revived by the room (the existing revive rules apply).
- **Mounting forces:** a pressure clock runs while any member is inside. After **20 s of grace**, every **40 s** one
  reinforcement group of one or two floor-appropriate enemies arrives through the existing portal machinery at the
  corridor nearest the door but out of its sight, and **holds position** there, awake. Cap: **8 alive** from this
  source per floor; above the cap the clock keeps running but spawns nothing. The clock resets to grace when the room
  is empty for 30 s. Leaving the room means walking into whatever gathered. The merchant remarks at 2, 4 and 6
  gathered ("That is a lot of footsteps for a corridor."). Numbers live in an engine-free `SafeRoomRules` and are
  tested; the builder may tune them by playtest within ±50%. The clock is **independent of the Settings
  "Reinforcement pressure" slider**: turning portals off does not make the safe room a free heal; the room's price is
  always paid. A debug flag `--no-gathering` switches it off for tests that need a quiet floor.

### 4. The merchant

- He is a campaign **Cast** member flagged `Merchant: true`, and he is **nobody else's story**: the merchant never
  carries a Secret, a quest or a Betray choice and is never placed as a story NPC on a floor; the campaign validator
  enforces this and the planner prompt states it. In the shipped campaign he is a **new sixth cast member, Master
  Oswin Pellet**, a Dunmarrow factor and property speculator who has bought the council's "plumbing inspection"
  contract as its financier: the council's advance passes through him (his cut taken) as the seed money, which keeps
  every shipped line about the council having "paid you four in advance" true, and the property plan is his private
  sideline. He is a man; his voice is `Achird` (Runware: Oliver), unused by the rest of the cast. Alderman Crane keeps
  his existing role untouched (floors 1, 4, 9 and 17, the memorial stone, the betrayal). The merchant always wears the
  `npc_merchant` body; any other cast member who would match that body (Crane's "alderman", "plump") wears
  `npc_old_man` instead, one shopkeeper look per campaign. The offline designer invents a merchant for offline
  campaigns; the planner prompt asks Claude for one in AI campaigns. Name, voice and persona come from the cast entry;
  the tavern briefing and his floor lines from the new campaign section `Merchant` (see 6).
- E on him opens a dialogue with three choices: **Trade**, **How is it going?** (his floor line), **Leave**. Trade
  opens the shop screen. While the shop is open he faces the player and talks (the existing talk pose rules apply).

### 5. The shop

- A screen with his stock on the left and the party bag on the right, the purse between them. Click an item on his
  side to buy (or drag it into the bag); click an item in the bag to sell (or drag it onto his side). Prices under each
  item; a confirmation only for Legendary items. Buying needs the gold and a free bag slot; selling moves the item out
  of the bag and the gold in. Co-op: `ShopBuyIntent(stockIndex)` / `ShopSellIntent(bagSlot)` to the server,
  `ShopSync(stock, purse)` back; refusals are logged like bag refusals today.
- **Stock per floor** is rolled once from `gameSeed ^ floor` so every peer and reload agrees, and is depleted by
  purchases (a bought item is gone until the next floor). Each floor's stock: two weapons, one piece of armour and one
  offhand or jewellery, all rolled by `LootRoller` at item level `floor + 2` with rarity Magic or better; five health
  potions, three mana potions, two antidotes, two arrow bundles of 20, one scroll, one trap kit; and **on floors 3, 6,
  9, 12, 15 and 18 one unique weapon** (a Legendary), and one more on floor 20: seven unique slots for the seven wieldable kinds
  (Sword, Axe, Mace, Dagger, Polearm, Bow, Staff), each kind offered exactly once per run in a seeded order. The
  merchant buys anything sellable at any floor.
- **New unique weapons:** seven hand-written legendaries in `data/legendaries`, one each for sword, axe, mace,
  polearm and shortbow, a second dagger and a second staff, each with a name, a flavour line, fixed powers in the
  spirit of the existing five (two or three modifiers and usually an on-hit) and an `ItemLevelMin` at or below the
  floor it is first offered. They are **shop-only**: a `ShopOnly` flag on the legendary definition keeps them out of
  the dungeon's legendary roll, so the shop's prize stays the shop's (the owner's "weapons the dungeon does not drop").
  The existing five legendaries keep dropping and are never in the shop's unique slot. Names and powers are the
  builder's, within those constraints.

### 6. The merchant's lines and the story arc

- New campaign section `Merchant` with: `Briefing` (the tavern dialogue, four nodes with two replies each),
  `FloorLines` (per floor 1 to 20: a greeting, a progress line for "How is it going?", a trade line) and `Gathering`
  (three lines for the pressure thresholds). The shipped campaign's section is **hand-written** in Crane's voice and
  arc: floors 1 to 6 bluster and property talk; 7 to 13 unease (a tunnel not on his survey, the quakes keeping time,
  his investors asking questions); 14 to 19 fear (he has seen the digging, "they are not spreading out, they are
  coming up"); floor 20 a send-off. He never states the truth: in the shipped campaign the truth stays the Verger's
  contract and the god trying to wake, and the quakes are that god stirring; the "digging upward" is what Crane can
  see of it.
- Offline and AI campaigns: a generic line bank in code with the campaign's title and antagonist substituted, used
  when the campaign has no `Merchant` section; the planner prompt gains the section so Claude writes it for AI
  campaigns.
- All shipped lines are voiced with the existing bake (`--bake-cast-voice` covers cast lines; the builder extends it to
  the `Merchant` section). Roughly 70 lines.

### 7. Saves, net, tests

- Save: purse, per-floor stock remaining (ids of sold-out slots), per-floor gathered count, floor 0 allowed. Version
  bump + migration.
- Net: purse and stock in `Welcome` and in `BagSync`-style syncs; the two shop intents; door state through
  `PropChanged`; pressure and rest are server-side with the results reaching clients through `PartySync`.
- Engine-free tests: prices, coin ranges, stock rolling (deterministic, depletion, unique-weapon coverage across a
  run), safe room choice rules, door rule, pressure clock, purse credit on deal-or-descent. Headless flows:
  `--test-tavern` (new game lands on floor 0, briefing credits 150 gold, descend to floor 1), `--test-safe-room`
  (find the room, door opens for the party and not for an enemy walked at it, rest regen rate, pressure spawns after
  grace, buy and sell change purse and bag), and the co-op flow in `scripts/net_test.sh` gains a buy and a sell.

## What is left to the builder

Tavern dressing and theme textures; shop screen layout; the six unique weapons' names and powers; the exact lines
(the owner may want to read them before the voice bake); pressure numbers within the stated range; where exactly the
merchant's remarks fire; whether the HUD shows the gathered count or only the log line.

## Calls made after the builder check (owner asked for judgment; veto any)

1. The shipped merchant is a new cast member (Oswin Pellet), not Crane, whose betrayal arc stays as written.
2. The briefing is framed inside the council contract; no shipped voiced line changes.
3. Seven unique slots (floors 3, 6, 9, 12, 15, 18, 20) for seven weapon kinds; seven new shop-only legendaries.
4. The safe room is sealed against gas on Toxic floors.
5. The safe room's gathering clock ignores the Reinforcement pressure setting (a debug flag quiets it for tests).
6. Seed money 150 gold; the coin name is "gold".
