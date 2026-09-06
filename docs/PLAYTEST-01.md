# Playtest 01 — gray-box loop feedback

Run `dotnet build DungeonCrawler.csproj && godot --path .` and play for 5–10 minutes. Try to clear one floor and descend.
Answer whatever you noticed; skip what you have no opinion on. Numbers in brackets are the current values.

## 1. Movement and camera
- Walk speed [4 m/s] and sprint [x1.6]: too slow / about right / too fast?
- Mouse sensitivity [0.0022]: too sluggish / about right / too twitchy?
- Acceleration [14]: does starting and stopping feel floaty, snappy, or fine?
- Field of view [80°]: comfortable, or do you want wider/narrower?
- Corridor scale [4 m wide, 4 m tall]: do rooms and halls feel cramped, right, or cavernous?

## 2. Attacking
- Melee reach [about 1.8 m sphere in front of the camera]: do you have to stand too close, or can you hit from too far?
- Cooldowns [Brand 0.8 s, Sable 0.55 s, Wren 0.8 s, Ilex 0.8 s]: does pressing 1-4 in sequence feel like a rhythm, or like spam?
- Would you rather: (a) left click swings the whole party in sequence, (b) left click is member 1 only and 1-4 pick members, (c) click the portrait like Grimrock?
- Should the back rank (Wren, Ilex) be blocked from melee now, or only once ranged and spells exist?

## 3. Enemies
- Ghoul speed [3 m/s] vs your walk speed [4 m/s]: can you kite it, and should you be able to?
- Detection range [14 m] and give-up time [4 s]: did enemies notice you too early, too late, or about right?
- Attack windup [0.5 s] and recovery [0.9 s]: did you ever feel you could react to an incoming hit, or did hits feel instant?
- Damage [8 per hit] against party HP [60/45/40/32]: how many enemies did it take to feel in danger? What number would feel right?
- Enemies per floor [5 on floor 1, +1 per floor]: too sparse, right, too dense?

## 4. HUD and feedback
- Portraits: is the bottom-centre strip in the way, or fine? Bigger, smaller?
- Message log: useful, noisy, or not noticed? Should hits show as floating numbers in the world instead?
- Interaction prompt (`[E] Pick up ...`): clear enough?
- What information did you want on screen that was missing?

## 5. Dungeon layout
- Floor size [48x48 cells, 8-15 rooms]: did floors take too long to explore, or feel too small?
- Corridors: too many dead-straight L-shaped halls? Want doors, wider halls, or more loops?
- Did you ever get lost? Would a minimap or map screen help now, or later?

## 6. Anything else
- The single most annoying thing:
- The single thing that already felt good:
- Bugs or oddities (what you did, what happened):
