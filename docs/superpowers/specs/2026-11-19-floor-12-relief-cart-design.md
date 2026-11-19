# Floor 12: the relief cart and the cage hold (design)

Approved by the owner 2026-11-19 after their first single-player play test of the mine cart chase. An addendum to
`2026-11-17-floor-12-mine-cart-chase-design.md`; that spec still binds everything this one does not change.

## Why

The owner played floor 12 solo and found it fun, then got stranded: they left the cart to free a cage, walked on, and no
cart ever came. The log of that run shows two causes:

- **Stop 1:** the gate lifted, the 5 s "All aboard" countdown ran, and the cart left with that stop's cage still locked.
  A companion picked the lock after the train had gone, so that crew had to walk.
- **Stop 2:** the player got out and went on foot. The cart never moves without a player, and in single player the spare
  carts are looks only, so nothing ever came for them.

In the owner's words: "I would think that if you jump from the cart you will get another one in a bit."

## Goals

1. **A player left behind gets a relief cart in a bit.** Jumping out still hurts and still costs time, but it never
   strands you.
2. **The cart does not leave a stop while that stop's cage is still locked**, unless the player chooses to leave the
   prisoners behind.

## Non-goals

- Co-op keeps its current spare cart, taken at a stop. A relief cart in co-op is a later option.
- No change to the run-over rules, the throw from a fast cart, the lethality setting, or the win rule.
- Nothing is saved: as with the co-op chase, a relief cart in flight is not saved, and a load starts clean.
- No HUD pointer to a waiting cart. It was floated but not approved.

## Anti-goals

- A relief cart that makes jumping free. The throw still hurts, and the wait is real.
- A relief cart that drives through the party's own train, floats over a pit, or passes the train.
- Any change to how co-op plays or syncs today.

## Approaches

- **A. Reuse the co-op chaser for the relief cart (chosen).** Plan 5's chaser already runs a second cart on the party
  line at the party cart's physics: it halts behind the train, hops a gap or fallen trestle, halts at a live ore heap,
  sweeps for run-overs and gives the bell warning. The relief cart is a chaser that is sent to the player instead of
  taken at a stop.
- **B. The party cart brakes and waits for a jumper.** Smaller, but your companions would sit on the line waiting, and it
  doesn't match the owner's picture of "another one in a bit".
- **C. Let solo players take the stop spares, as co-op does.** The cart doesn't come to you: you walk back to the stop
  behind, which is the stranding the owner hit.

## The design (the owner approved it as written)

### The relief cart (single player only)

- **Left behind** means: solo, on a railed floor whose line isn't done; the party cart isn't wrecked for good; the
  player isn't seated in any cart; and the train is ahead of the player on the party line. That happens when you jump
  out at speed, or the cart leaves without you.
- **When you're not left behind, no relief cart comes:**
  - You got out at a stop and the cart stands held there. It waits for you, as it does today.
  - You walked ahead of the train. The cart waits where you left it.
- **About 15 seconds after you're left behind**, a relief cart comes down the party line from the stop behind you,
  ringing its bell as party-lane carts do.
- **If you're beside the track or in a wall pocket**, it slows and stops with a seat next to you. Press E to get in.
  Companions and escorts on foot near you board with you, as they do into the chaser.
- **If you're standing on the rails**, it rolls you over as any cart does, then stops a little past you.
- **Once you're in**, it runs up behind the train and stops there, as the chaser does. You get out and climb back into
  your own cart.
- **If you don't get in**, it waits a while, then goes. If you're still left behind, another one comes. You can never
  be stranded for good.
- A relief cart never spends a stop's spare, and it is not saved.

### The cage hold (every mode)

- At a stop whose cage wagon is still locked, the "All aboard" countdown does not start, and the cart does not leave.
  "The cage here is still locked" is said, as today.
- A cage whose crew are all dead doesn't hold the cart.
- **A seated player can choose to leave the prisoners behind.** Pressing E on the cart still gets you out, as today;
  leaving them behind is a deliberate press (see the builder's calls). Leaving them starts the countdown as normal.

## Calls left to the builder (marked as mine)

- **The timings:**
  - "In a bit" is 15 s.
  - A relief cart that has stopped beside you waits 30 s, then goes.
  - The next one comes 15 s after that.
- **Where it starts and where it stops:**
  - It starts at the nearest stop behind the player on the party line.
  - If that point is already close to you, it simply waits there.
  - A seat "next to you" means at the nearest party-lane point to you, within the reach at which you can board.
- **The leave-them-behind press:**
  - My call is holding E (the interact button, in VR too) for about a second while seated, with the prompt saying so.
  - The planner confirms the input layer can do that. If it can't, they pick another deliberate gesture and say why.
- **The cage hold in co-op:** it goes through the server's existing state, so a client's prompt reads it. Protocol stays
  17, because the branch is unreleased.
