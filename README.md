# DungeonCrawler

A first-person dungeon crawler: four heroes, twenty voiced floors, play alone or with up to four friends. No programming
needed to install it.

## Install in three steps

Pick your system. Each step is one thing to do.

### Windows 10 or 11

1. Open a Command Prompt (not PowerShell): press the **Windows key**, type `cmd`, press **Enter**.
2. Paste these three lines (right-click to paste), then press **Enter**:
   ```
   git clone https://github.com/Yllonnoce/DungeonCrawler.git
   cd DungeonCrawler
   build.bat
   ```
3. Wait (20 to 40 minutes the first time). When the menu appears, press **1** to install.

- No git on your PC? Paste this one line instead; it fetches git for you and does the three steps:
  ```
  curl.exe -fsSL -o build.bat https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.bat && build.bat
  ```
- Only want the finished game, no building? One line, done in minutes:
  ```
  curl.exe -fsSL -o get.bat https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.bat && get.bat
  ```
- First start: SmartScreen may warn because the game is unsigned. Click **More info**, then **Run anyway**.
- **Update later:** double-click `update.bat` in the `DungeonCrawler` folder. It pulls the newest code, rebuilds, reinstalls.

### macOS (Apple silicon and Intel)

1. Open Terminal: press **Command + Space**, type `Terminal`, press **Enter**.
2. Paste these three lines, then press **Enter** (if the Mac has no git, it offers to install it; accept, then paste again):
   ```
   git clone https://github.com/Yllonnoce/DungeonCrawler.git
   cd DungeonCrawler
   bash build.sh
   ```
3. Wait (20 to 40 minutes the first time). When the menu appears, press **1** to install.

- One line that does all three steps from anywhere:
  ```
  curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.sh | bash
  ```
- Only want the finished game, no building? One line, done in minutes:
  ```
  curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.sh | bash
  ```
- First start: if macOS refuses to open the app, right-click it and choose **Open** once.
- **Update later:** in the `DungeonCrawler` folder run `bash update.sh`. It pulls the newest code, rebuilds, reinstalls.

### Linux (x86_64)

1. Open a terminal.
2. Paste these three lines, then press **Enter** (no git? `sudo apt-get install git` first):
   ```
   git clone https://github.com/Yllonnoce/DungeonCrawler.git
   cd DungeonCrawler
   bash build.sh
   ```
3. Wait (20 to 40 minutes the first time). When the menu appears, press **1** to install.

- One line that does all three steps from anywhere:
  ```
  curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.sh | bash
  ```
- Only want the finished game, no building? One line, done in minutes:
  ```
  curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.sh | bash
  ```
- Start it from your app menu, or type `dungeoncrawler`.
- **Update later:** in the `DungeonCrawler` folder run `bash update.sh`. It pulls the newest code, rebuilds, reinstalls.

## Play with friends in three steps

1. One person runs the **Host** script that sits next to the setup program: `Host.bat` (Windows), `Host.command` (macOS),
   `host.sh` (Linux). Leave its window open.
2. It prints an address like `ws://192.168.1.20:7777`. Send it to your friends.
3. Friends start the game, pick **Join Server**, enter the address, choose a name and a class.

- Friends outside your home network need the second address the Host script prints, and your router must forward
  TCP port 7777 to your computer.
- Windows and macOS ask once to allow the game through the firewall: say yes.
- Closing the Host window stops the server. The game is saved and continues next time.
- Want a server that stays up when your computer is off? [docs/server-setup.md](docs/server-setup.md) has it step by step.

## Good to know

- **Two ways to install.** *Build it yourself* (the three steps) compiles the newest code on your machine; about 6 GB of
  disk, no administrator rights, the tools go in your own user folder. *The finished game* (the `get` line) downloads the
  newest release, about 500 MB, a few minutes.
- **Debugging on Windows.** The launcher's menu has a **Debug** choice: the game starts with `--verbose` and any flags you type (`--floor=3 --seed=1`, see `src/Core/DebugTools.cs`) in a console that shows its output. `Launch.bat debug --floor=3` from a Command Prompt does the same without the menu.
- **Updating.** Build route: `update.bat` or `bash update.sh`. Download route: on Windows the launcher offers each new release when you start the game; elsewhere run the `get` line again.
- **Uninstalling.** Run the setup program again (`Setup.bat`, `Setup.command`, `setup.sh`) and choose **Uninstall**.
- **Saves** live in your user data folder and survive updating and uninstalling.
- **Privacy.** Nothing phones home about you. On Windows the launcher's Play choice asks GitHub for the newest release (Debug does not). The campaign is complete on disk; the optional AI features only run with your own keys.
- **By hand.** Every release on the **[Releases page](../../releases/latest)** has three archives. Unpack yours and run the
  setup program inside; `INSTALL.txt` repeats the steps and `SHA256SUMS` checks the download.

## How to play

You control one of the four heroes at a time; the other three follow, fight and talk on their own.

| Key | Action |
|---|---|
| W A S D, Shift | Move, sprint (holding forward for two seconds sprints on its own) |
| Space | Jump (clears a pit at a sprint) |
| Ctrl (hold) | Crouch: slower, quieter, enemies notice you from closer |
| Left Alt | Dodge: a short burst you cannot be hit through, 20 stamina |
| Mouse | Look |
| Left click | Attack or cast. Tap for a light swing; hold and release for a heavy one |
| Right mouse | Guard (Warrior; a block in its first moment is perfect and staggers the attacker) or parry (the others: a melee blow in the window is turned and the attacker staggers) |
| Q or mouse wheel | Cycle your active ability |
| 1 / 2 / 3 / 4, F | Take control of another hero |
| R | Cycle the companions' stance: Defensive, Aggressive, Hold |
| V | Cheer: your hero rallies, the others answer |
| E | Interact: pick up loot, open doors and chests, talk, disarm traps, push secret walls |
| Tab or I | Inventory (drag to equip; drop or destroy items) |
| C, K, J | Character sheet, skill tree, journal |
| M | Full-floor map (the corner minimap is always on) |
| 1-4 in a conversation | Pick that reply (Esc leaves) |
| Esc | Pause menu: Resume, Save, Load, Settings, Quit |

Stairs ask whether to go down when you stand on the landing at the top of them. Keep an eye on the floor: hidden
plates, spike beds and presses kill, and a companion who falls to one can be pulled clear by the others.

## Ways to play

- **Story** is the voiced twenty-floor campaign, the same every time, saved as you go.
- **New Descent** rolls a fresh dungeon from a new seed: different floors, different elites, different shrines. Before you go
  down, the factor in the tavern offers the **contract**: ten clauses, each worth some *heat*. Heat raises the gold and
  experience you find by a tenth per point; the clauses are the price (prices up by forty per cent, a quarter more creatures,
  one more elite a floor, no safe rooms, a fifth more of every blow, and the **Death Clause**: a wipe ends the run and leaves
  a memorial in the save list).
- **Daily Descent** is one seed and two clauses for everyone, the same for the whole day, scored once. Your best shows on
  the button.
- **Records** keeps your deepest floor, clears, dailies, elites slain, the last twenty runs and twenty-four **deeds**. Deeds
  unlock clauses, legendaries, new banter and new lines from the hidden merchant.

Every descent prints a floor card when you take the stairs: who killed what, who was hurt most, what nearly ended you. A wipe
without the Death Clause costs a fifth of your gold and scars the party until the next floor; the death card says what did it
and who was last standing. Beating the final boss prints a recap that names the boss by name, and the victory is saved.

Down there, from floor 2 on, one creature a floor is an **elite** with a name and a tell: a swift one, a hulking one, one
shelled in a ward, one that heals on your blood, one that bursts, one that splits, one you cannot hit from range or at all
from outside its dome. Bosses roar at half health and change the room. Every floor from the second has a **shrine** that
offers three boons for a price (your blood, your gold, or a curse); a party that holds two of a house is offered its crown.
Some chests breathe. One chest a floor from floor 5 is bound: open it and something hunts you for a minute. Half the secret
alcoves open into rooms with a plaque, a chest worth the search and, once a tier, a merchant who was never there. The
merchant's third tab sells three cloth-wrapped bundles a floor, sight unseen.

## For developers

The engineering manual is [docs/development.md](docs/development.md): building from source, the test suite, exports,
the AI campaign designer, voice, the server, and how every system fits together. Design documents live under
[docs/superpowers/specs](docs/superpowers/specs). The player scripts are `get.sh` / `get.bat` (download the latest
release), `build.sh` / `build.bat` (clone if needed, build from source, install) and `update.sh` / `update.bat`
(pull, rebuild, reinstall); the setup and Host programs they ship live in `installer/`. Installers are made with `scripts/export_all.sh --no-ship` and `scripts/package.sh`, and published with
`scripts/publish_game.sh`; the server with `scripts/publish_server.sh`.
