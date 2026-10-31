#!/usr/bin/env bash
# Start the dedicated server from a clone of the repository, in the foreground (Ctrl+C stops it).
#   bash deploy/gce/start.sh                 # port 80, the shipped campaign, 4 players
#   PORT=7777 CAMPAIGN=offline MAX_PLAYERS=6 bash deploy/gce/start.sh
# Uses build/server if it is there (an export, or a tarball you copied in), else downloads the latest server-*
# GitHub release (the repository is public, no login needed), else runs from source when Godot is installed (deploy/gce/setup_source.sh).
# For a service that survives logout and reboots use deploy/gce/install.sh instead; this script is for trying it
# out and for watching the log.
set -euo pipefail
cd "$(dirname "$0")/../.."
PORT=${PORT:-80}
CAMPAIGN=${CAMPAIGN:-builtin}
MAX_PLAYERS=${MAX_PLAYERS:-4}
SAVE=${SAVE:-server}
BIN=build/server/DungeonCrawler.x86_64

export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
FROM_SOURCE=""
if [ ! -x "$BIN" ]; then
  if [ -f build/dungeoncrawler-server.tar.gz ]; then
    echo "unpacking build/dungeoncrawler-server.tar.gz"
    tar -C build -xzf build/dungeoncrawler-server.tar.gz
  else
    API="https://api.github.com/repos/${DUNGEONCRAWLER_REPO:-Yllonnoce/DungeonCrawler}/releases"
    TAG=$(curl -fsSL -H 'User-Agent: DungeonCrawler-start' "$API?per_page=50" 2>/dev/null | grep -o '"tag_name": *"server-[^"]*"' | head -1 | sed 's/.*"\(server-[^"]*\)"/\1/' || true)
    URL=""
    [ -n "$TAG" ] && URL=$(curl -fsSL -H 'User-Agent: DungeonCrawler-start' "$API/tags/$TAG" | grep -o '"browser_download_url": *"[^"]*dungeoncrawler-server.tar.gz"' | sed 's/.*"\(https\?:[^"]*\)"/\1/' | head -1 || true)
    if [ -n "$URL" ]; then
      echo "no server build in build/server: fetching release $TAG from GitHub"
      mkdir -p build
      curl -fL --progress-bar -o build/dungeoncrawler-server.tar.gz "$URL"
      tar -C build -xzf build/dungeoncrawler-server.tar.gz
    elif command -v godot >/dev/null; then
      echo "no server build and no published release: running from source with $(godot --version | head -1)"
      FROM_SOURCE=1
    else
      echo "no server build, no published release, and no Godot here."
      echo "Either: bash deploy/gce/setup_source.sh (installs Godot + .NET, runs from this clone)"
      echo "or: scripts/publish_server.sh on the dev machine, then run this again"
      exit 1
    fi
  fi
fi
if [ -n "$FROM_SOURCE" ]; then
  # the import is trusted only when it finished; a crash (or a killed run) leaves a half-filled folder that must be redone
  if [ ! -f .godot/import-ok ]; then
    echo "importing assets (first time, a few minutes; log in /tmp/godot-import.log)"
    rm -rf .godot/imported
    if godot --headless --import --path . > /tmp/godot-import.log 2>&1 && ! grep -q 'FATAL' /tmp/godot-import.log; then touch .godot/import-ok
    else echo "asset import failed; see /tmp/godot-import.log"; tail -5 /tmp/godot-import.log; exit 1; fi
  fi
  echo "building (the first build takes 10-20 minutes on a small VM; later ones are quick)"
  dotnet build DungeonCrawler.csproj -v m --nologo -clp:NoSummary
  BIN=$(readlink -f "$(command -v godot)")
  RUN=(godot --headless --path . --)
else
  chmod +x "$BIN"
  RUN=("$BIN" --headless --)
fi

# a port below 1024 needs root or the bind capability; grant the capability to the binary once rather than running as root
if [ "$PORT" -lt 1024 ] && [ "$(id -u)" -ne 0 ] && ! getcap "$BIN" 2>/dev/null | grep -q cap_net_bind_service; then
  echo "port $PORT needs the bind capability; granting it to $BIN (asks for sudo once)"
  sudo setcap 'cap_net_bind_service=+ep' "$BIN"
fi

# saves, keys and the status file: user:// for this user (or XDG_DATA_HOME if you set it)
echo "starting: port $PORT, campaign $CAMPAIGN, up to $MAX_PLAYERS players, save slot $SAVE"
echo "players join with ws://$(curl -s --max-time 2 -H 'Metadata-Flavor: Google' http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip 2>/dev/null || hostname -I | awk '{print $1}')$([ "$PORT" = 80 ] || echo ":$PORT")"
exec "${RUN[@]}" --server --port="$PORT" --bind=0.0.0.0 --campaign="$CAMPAIGN" --save="$SAVE" --max-players="$MAX_PLAYERS" --status-file="${STATUS_FILE:-/tmp/dungeoncrawler-status.json}"
