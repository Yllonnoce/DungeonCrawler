# Floor 12: throwing a switch from the cart (design)

Approved by the owner 2026-11-19, after their solo play test: "Not sure how to change the tracks when I am passing by it."
This is an addendum to `2026-11-17-floor-12-mine-cart-chase-design.md`, which still binds everything this one does not
change.

## Why

To throw a switch from a moving cart today, a seated player must point their view straight at a thin lever post between
the tracks and press E within about 3.5 m. At ride speed that is about one second, aiming sideways while under attack.
Seated, E also means "Get out", so a miss at speed throws the player out of the cart. The switches are the ride's choices
(spec: "The Switchyard — choices"), and nobody can make them this way.

## Goals

- While a switch the player can throw is coming up, the screen shows **"E: throw the switch"** (or the lever's own prompt
  when it is already thrown, such as setting it back).
- During that window, **E throws (toggles) the switch, with no aiming, and never gets the player out of the cart.**

## The design (approved as written)

- **The window:** a seated, living (not downed) player in the party cart, while a throwable lever is coming up on their
  ride. It runs from about **15 m before the lever** until the lever's prompt goes, which is once the cart's front has
  passed the points.
- **The levers:**
  - Ride 3's `switch_out` lever (dodge the ore heap, cross onto the enemy line, ram a slag cart).
  - Ride 4's spur lever (send the golem down the dead-end spur).
  - The `switch_back` levers are fixed and untouchable, so they are never offered.
- **During the window, E or the VR grip throws the lever.** Aim does not matter and the look ray is not consulted.
  "Get out" is not offered and does not happen.
- **What stays the same:**
  - Hitting the lever, and throwing it on foot by looking at it, work as today.
  - A companion's "Line's blocked — switch!" call comes at about the start of the window.
- **Co-op:** a client's E sends the existing interact intent for that lever. The server accepts it when the sender is
  seated in the party cart and the lever is within that rider's window, as well as by the usual reach. Protocol stays 17,
  because the branch is unreleased.

## Non-goals

- No change to what a switch does once thrown.
- No change to the levers' placement, the heap, the ram or the golem.
- No change to on-foot use.
- No new key binding.
- No new voice lines. Any new line is a subtitle only.

## Calls left to the builder (mine)

- **The window is 15 m before the lever.** At the 6 m/s cruise that is about 2.5 s, close to the companion's existing
  3 s look-ahead call. The builder aligns the two.
- **Two levers in one window:** if two throwable levers are ever in the window at once, the nearer one is offered.
- **Holding E inside the window:** the cage hold only happens at a held stop, and the window only happens while rolling,
  so the two never meet. The builder confirms this.
