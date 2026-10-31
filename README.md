# DungeonCrawler

A first-person dungeon crawler for one player with three companions, or for up to four friends on a shared server.
Fight your way down twenty floors of a voiced, story-driven campaign, talk to the people you meet, free the ones in
chains, and face what waits at the bottom. Made with Godot 4.7 and C#.

## Get the game

There are two ways to get the game onto your computer. Neither needs any programming.

| | Download the finished game | Build it yourself |
|---|---|---|
| Takes | a few minutes (about 500 MB) | 20 to 40 minutes the first time, a few minutes after that |
| Disk | about 1 GB | about 6 GB (the source, the tools and the build) |
| Gives you | the newest **release** | the newest **code**, built on your own machine |
| Needs | an internet connection | an internet connection; no administrator rights, the tools go in your own user folder |

Both end in the same setup program, which offers **Install** or **Uninstall** and asks whether to put a launcher on your
Desktop. Run either one again later to update. Your saves live in your user data folder and survive reinstalling and
uninstalling. Nothing phones home: the shipped campaign is complete on disk, and the optional AI features only run if
you add your own keys in Settings.

### Windows 10 or 11

**Download the finished game.** Open a Command Prompt (press the Windows key, type `cmd`, press Enter), paste this line
and press Enter:

```
curl -fsSL -o get.bat https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.bat && get.bat
```

It downloads the game into `Downloads\DungeonCrawler`, checks the file, unpacks it and shows the setup menu. Choose
**1** to install. When the game first starts, SmartScreen may warn you because the build is not signed: click
**More info**, then **Run anyway**.

**Build it yourself.** Same thing with `build.bat`:

```
curl -fsSL -o build.bat https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.bat && build.bat
```

It puts the source in `%USERPROFILE%\DungeonCrawler`, fetches Godot, its export templates and the .NET SDK into your
user folder, builds the game and shows the same setup menu. If you already have a copy of this repository, just
double-click `build.bat` in it instead.

These are ordinary `.bat` files for the classic Command Prompt. They do not use PowerShell, so no permission or policy
changes are needed.

### macOS (Apple silicon and Intel)

Open Terminal (Spotlight, type `Terminal`), paste one line and press Enter.

**Download the finished game:**

```
curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.sh | bash
```

**Build it yourself:**

```
curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.sh | bash
```

The setup menu appears at the end; choose **1** to install `DungeonCrawler.app` into Applications. The app is signed
ad hoc, not notarized, and the setup program clears the download quarantine for you. If macOS still refuses to open it
the first time, right-click the app and choose **Open** once. If you already have a copy of this repository, run
`bash build.sh` inside it.

### Linux (x86_64)

Open a terminal, paste one line and press Enter.

**Download the finished game:**

```
curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.sh | bash
```

**Build it yourself:**

```
curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.sh | bash
```

The setup menu appears at the end; choose **1** to install into `~/.local/opt/DungeonCrawler` with an app-menu entry
and the `dungeoncrawler` command. Nothing needs root. If Godot cannot start on a very bare system, the script prints the
one `apt-get` line that installs the missing libraries. If you already have a copy of this repository, run `bash build.sh`
inside it.

### Doing it by hand instead

Every release on the **[Releases page](../../releases/latest)** carries three archives. Download the one for your
system, unpack it, and run the setup program inside: `setup.sh` (Linux), `Setup.bat` (Windows) or `Setup.command`
(macOS). `INSTALL.txt` in the release repeats these steps and `SHA256SUMS` lets you check a download.

## Playing together

Each player controls one hero; whoever takes the stairs takes the whole party down. Leave and rejoin with the same
name to get your character back. A game needs one server, and there are two ways to have one.

**Host from your own computer.** Next to the setup program you ran is a Host script: `Host.bat` on Windows,
`Host.command` on macOS, `host.sh` on Linux (on Windows and Linux it is also in the installed folder). Run it and
leave its window open. It prints two addresses:

- the one for friends on the same home network, such as `ws://192.168.1.20:7777`;
- the one for friends elsewhere, which also needs your router to forward TCP port 7777 to your computer.

Windows and macOS ask once whether to allow the game through the firewall; say yes. Closing the window stops the
server. The game is saved as it goes and continues where everyone left off the next time you host.

**A server that runs around the clock.** A small cloud machine, or any Linux box that stays on, can run the dedicated
server so friends can play when you are not there. [docs/server-setup.md](docs/server-setup.md) walks through it from a
fresh Google Cloud account, step by step, and covers the day-to-day afterwards.

**Joining.** Start the game, pick **Join Server** on the title screen, enter the address you were given, choose a name
and a class, and you arrive beside the others.

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

## For developers

The engineering manual is [docs/development.md](docs/development.md): building from source, the test suite, exports,
the AI campaign designer, voice, the server, and how every system fits together. Design documents live under
[docs/superpowers/specs](docs/superpowers/specs). The player scripts are `get.sh` / `get.bat` (download the latest
release) and `build.sh` / `build.bat` (build from source), and the setup and Host programs they ship live in
`installer/`. Installers are made with `scripts/export_all.sh --no-ship` and `scripts/package.sh`, and published with
`scripts/publish_game.sh`; the server with `scripts/publish_server.sh`.
