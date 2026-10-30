# DungeonCrawler

A first-person dungeon crawler for one player with three companions, or for up to four friends on a shared server.
Fight your way down twenty floors of a voiced, story-driven campaign, talk to the people you meet, free the ones in
chains, and face what waits at the bottom. Made with Godot 4.7 and C#.

## Download and play

No programming needed. Go to the **[latest release](../../releases/latest)**, download the file for your system, unpack
it, and run the setup program inside. It offers Install or Uninstall, and Install can put a launcher on your Desktop.

| System | File | Then |
|---|---|---|
| Linux (x86_64) | `DungeonCrawler-*-linux-x86_64.tar.gz` | `tar xzf` it, then run `./DungeonCrawler/setup.sh` |
| Windows (x86_64) | `DungeonCrawler-*-windows-x86_64.zip` | unzip, then double-click `Setup.bat` (SmartScreen may warn because the build is unsigned: "More info", then "Run anyway") |
| macOS (Apple silicon and Intel) | `DungeonCrawler-*-macos-universal.zip` | unzip, then double-click `Setup.command` (right-click, Open the first time) |

Each archive also carries `INSTALL.txt` with these steps. Your saves live in your user data folder and survive reinstalling.
Nothing phones home: the shipped campaign is complete on disk, and the optional AI features only run if you add your own keys in Settings.

## How to play

You control one of the four heroes at a time; the other three follow, fight and talk on their own.

| Key | Action |
|---|---|
| W A S D, Shift | Move, sprint (holding forward for two seconds sprints on its own) |
| Space | Jump (clears a pit at a sprint) |
| Ctrl (hold) | Crouch: slower, quieter, enemies notice you from closer |
| Mouse | Look |
| Left click | Attack or cast. Tap for a light swing; hold and release for a heavy one |
| Right mouse (hold) | Guard (blocking classes) |
| Q or mouse wheel | Cycle your active ability |
| 1 / 2 / 3 / 4, F | Take control of another hero |
| R | Cycle the companions' stance: Defensive, Aggressive, Hold |
| E | Interact: pick up loot, open doors and chests, talk, disarm traps, push secret walls |
| Tab or I | Inventory (drag to equip; drop or destroy items) |
| C, K, J | Character sheet, skill tree, journal |
| M | Full-floor map (the corner minimap is always on) |
| 1-4 in a conversation | Pick that reply (Esc leaves) |
| Esc | Pause menu: Resume, Save, Load, Settings, Quit |

Stairs ask whether to go down when you stand on the landing at the top of them. Keep an eye on the floor: hidden
plates, spike beds and presses kill, and a companion who falls to one can be pulled clear by the others.

## Playing together

Someone runs a dedicated server (any Linux machine or a small cloud VM; see [docs/server-setup.md](docs/server-setup.md)).
Everyone else starts the game, picks **Join Server** on the title screen, enters the address they were given, such as
`ws://203.0.113.10`, chooses a name and a class, and arrives beside the others. Each player controls one hero; whoever
takes the stairs takes the whole party down. Leave and rejoin with the same name to get your character back.

## For developers

The engineering manual is [docs/development.md](docs/development.md): building from source, the test suite, exports,
the AI campaign designer, voice, the server, and how every system fits together. Design documents live under
[docs/superpowers/specs](docs/superpowers/specs). Installers are made with `scripts/export_all.sh --no-ship` and
`scripts/package.sh`, and published with `scripts/publish_game.sh`; the server with `scripts/publish_server.sh`.
