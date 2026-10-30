# Running a DungeonCrawler server

A dedicated server is the game itself started headless with `--server`. Players join it from the title screen
(Join Server) with a name and a class, each controlling one adventurer. This guide takes a fresh Google Cloud
account to a running server and covers the day-to-day afterwards. Any other Linux machine works the same from
step 4 onward; the Render route (Docker behind a WebSocket front door) is described in [development.md](development.md) under Multiplayer.

Placeholders used below: `PROJECT_ID` (your Google Cloud project), `ZONE` (for example `us-central1-f`),
`VM` (the instance name, for example `dungeoncrawler`). Replace them as you go.

## 1. One-time setup on your own machine

1. Install the Google Cloud CLI (`gcloud`) and sign in: `gcloud auth login`.
2. Install the GitHub CLI (`gh`) and sign in: `gh auth login`. The server build is published as a GitHub release of the
   game's repository, which is private, so the VM will also need a GitHub login (step 5) or a copy of the build (step 6b).
3. From the repository, make sure a server build exists or publish one:

```
scripts/export_server.sh                 # build/server/DungeonCrawler.x86_64 (Linux, headless)
scripts/publish_server.sh                # exports and publishes a GitHub release tagged server-<date>-<time>
```

`publish_server.sh` prints the release tag. `SKIP_EXPORT=1 scripts/publish_server.sh` publishes an export you already made.

## 2. Create the project and enable Compute Engine

```
gcloud projects create PROJECT_ID
gcloud config set project PROJECT_ID
gcloud services enable compute.googleapis.com
```

Billing must be linked to the project in the Cloud Console before the next step will succeed. An `e2-micro` in a US
region falls under Google's free tier for one instance; everything else on the VM (the 20 GB disk, outbound traffic
beyond the free allowance, a reserved address while the VM is stopped) is billed at normal rates.

## 3. Create the VM and open the port

```
gcloud compute instances create VM --zone ZONE --machine-type e2-micro \
  --image-family debian-12 --image-project debian-cloud --boot-disk-size 20GB --tags dungeoncrawler
gcloud compute firewall-rules create dungeoncrawler --allow tcp:80 --target-tags dungeoncrawler
```

The server listens on port 80 by default so players join with a bare address and no port suffix. If you would rather use
another port, open that one instead and change the service's `PORT` later (step 7).

A VM gets a new external address every time it is stopped and started. To keep one address:

```
gcloud compute addresses create dungeoncrawler-ip --region REGION      # REGION is the zone without its last letter, e.g. us-central1
gcloud compute instances delete-access-config VM --zone ZONE --access-config-name "External NAT"
gcloud compute instances add-access-config VM --zone ZONE --access-config-name "External NAT" \
  --address $(gcloud compute addresses describe dungeoncrawler-ip --region REGION --format='value(address)')
```

A reserved address is free while attached to a running VM and billed while the VM is stopped.

## 4. Connect to the VM

```
gcloud compute ssh VM --zone ZONE
```

The first connection creates an SSH key for you (you may give it a passphrase) and registers it with the project.
Everything from here on runs on the VM.

## 5. Install the tools and the repository on the VM

```
sudo apt-get update && sudo apt-get install -y git gh
gh auth login            # GitHub.com, HTTPS, then follow the prompt; read access to the repository is enough
git clone https://github.com/<owner>/DungeonCrawler.git ~/DungeonCrawler
```

Use the repository's real owner in the clone URL (the same one `scripts/publish_server.sh` publishes to).

## 6. Install the server

### 6a. From the published release (the usual way)

```
bash ~/DungeonCrawler/deploy/gce/fetch.sh
```

This pulls the repository, downloads the latest `server-*` release, installs the runtime libraries, creates a `dungeon`
system user, copies the build to `/opt/dungeoncrawler`, puts saves and the status file under `/var/lib/dungeoncrawler`,
installs the `dungeoncrawler` systemd service and starts it. Run the same command for every later update; the service is
stopped and started around the copy and keeps its saves. `fetch.sh server-20261030-1915` installs a specific release.

### 6b. From a copy of the build (no GitHub login on the VM)

On your own machine:

```
gcloud compute scp --recurse build/server deploy VM:~/dc --zone ZONE
```

On the VM:

```
sudo bash ~/dc/deploy/gce/install.sh ~/dc/server
```

## 7. Check it, and change settings if you want

```
bash ~/DungeonCrawler/deploy/gce/game.sh status
```

This prints the service state, the status file (current floor, players online) and the address players join with.
Players enter `ws://<address>` on the Join Server screen (add `:PORT` if you changed the port).

Settings live in the systemd unit as environment variables: `PORT` (80), `CAMPAIGN` (`builtin`, the shipped story),
`MAX_PLAYERS` (4), `SAVE` (the save slot, `server`). To change one:

```
sudo systemctl edit dungeoncrawler
```

Add, for example:

```
[Service]
Environment=PORT=7777
Environment=MAX_PLAYERS=6
```

then `sudo systemctl restart dungeoncrawler` (and open the new port in the firewall if you changed it).

## 8. Day to day

`deploy/gce/game.sh` on the VM is the control panel; run it with no argument for a menu, or:

| Command | What it does |
|---|---|
| `game.sh restart` | restarts the service; the saved game resumes, players rejoin with the same name |
| `game.sh fresh` | starts a new game from floor 1; the old save is kept beside it as `server-<date>.json` |
| `game.sh update` | fetches and installs the latest published build, then restarts |
| `game.sh status` | service state, status file, join address |
| `game.sh logs` | follows the server log (Ctrl-C to stop) |
| `game.sh stop` | stops the service; the VM keeps running |

Saves are JSON files under `/var/lib/dungeoncrawler/godot/app_userdata/DungeonCrawler/saves/`.

To stop paying for the VM when nobody plays, stop the VM itself from your own machine:

```
gcloud compute instances stop VM --zone ZONE
gcloud compute instances start VM --zone ZONE      # later; the service starts with the VM
```

## 9. If something is wrong

- `journalctl -u dungeoncrawler -n 50 --no-pager` shows why the service failed to start. On a 1 GB machine the usual
  cause is memory; `free -m` shows it.
- Players cannot connect: check `game.sh status` shows the service active, the firewall rule exists for the port in use
  (`gcloud compute firewall-rules list`), and the VM carries the rule's target tag (`dungeoncrawler` above).
- A client that joins sees an old version: clients and server must come from the same build; rebuild and reinstall the
  game on the players' machines (`scripts/export_all.sh --no-ship` then `scripts/package.sh` make the installers).
- `fetch.sh` says no `server-*` release: publish one from the repository with `scripts/publish_server.sh`.
- `gh` on the VM says not logged in: `gh auth login` again; tokens expire.

## 10. Keys for the AI features (optional)

The shipped campaign plays without any key. If you want the server to plan new campaigns or voice new lines, put the
keys in the server's own key file, never in the repository:

```
sudo -u dungeon mkdir -p /var/lib/dungeoncrawler/godot/app_userdata/DungeonCrawler
sudo -u dungeon nano /var/lib/dungeoncrawler/godot/app_userdata/DungeonCrawler/keys.cfg
```

with lines such as `claude="..."` and `runware="..."`, then `game.sh restart`.
