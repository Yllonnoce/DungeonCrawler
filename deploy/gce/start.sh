#!/usr/bin/env bash
# Start the dedicated server from a clone of the repository, in the foreground (Ctrl+C stops it).
#   bash deploy/gce/start.sh                 # port 80, the shipped campaign, 4 players
#   PORT=7777 CAMPAIGN=offline MAX_PLAYERS=6 bash deploy/gce/start.sh
# Uses build/server if it is there (an export, or a tarball you copied in), otherwise downloads the latest
# server-* GitHub release (needs `gh auth login` once). For a service that survives logout and reboots use
# deploy/gce/install.sh instead; this script is for trying it out and for watching the log.
set -euo pipefail
cd "$(dirname "$0")/../.."
PORT=${PORT:-80}
CAMPAIGN=${CAMPAIGN:-builtin}
MAX_PLAYERS=${MAX_PLAYERS:-4}
SAVE=${SAVE:-server}
BIN=build/server/DungeonCrawler.x86_64

if [ ! -x "$BIN" ]; then
  if [ -f build/dungeoncrawler-server.tar.gz ]; then
    echo "unpacking build/dungeoncrawler-server.tar.gz"
    tar -C build -xzf build/dungeoncrawler-server.tar.gz
  else
    echo "no server build in build/server: fetching the latest server-* release from GitHub"
    command -v gh >/dev/null || { echo "install gh (sudo apt-get install -y gh) and run gh auth login, or copy build/server here"; exit 1; }
    TAG=$(gh release list --repo Yllonnoce/DungeonCrawler --limit 20 --json tagName --jq '.[] | select(.tagName | startswith("server-")) | .tagName' | head -1)
    [ -n "$TAG" ] || { echo "no server-* release published yet: run scripts/publish_server.sh on the dev machine"; exit 1; }
    mkdir -p build
    gh release download "$TAG" --repo Yllonnoce/DungeonCrawler --pattern dungeoncrawler-server.tar.gz --dir build --clobber
    tar -C build -xzf build/dungeoncrawler-server.tar.gz
  fi
fi
chmod +x "$BIN"

# a port below 1024 needs root or the bind capability; grant the capability to the binary once rather than running as root
if [ "$PORT" -lt 1024 ] && [ "$(id -u)" -ne 0 ] && ! getcap "$BIN" 2>/dev/null | grep -q cap_net_bind_service; then
  echo "port $PORT needs the bind capability; granting it to $BIN (asks for sudo once)"
  sudo setcap 'cap_net_bind_service=+ep' "$BIN"
fi

# saves, keys and the status file: user:// for this user (or XDG_DATA_HOME if you set it)
echo "starting: port $PORT, campaign $CAMPAIGN, up to $MAX_PLAYERS players, save slot $SAVE"
echo "players join with ws://$(curl -s --max-time 2 -H 'Metadata-Flavor: Google' http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip 2>/dev/null || hostname -I | awk '{print $1}')$([ "$PORT" = 80 ] || echo ":$PORT")"
exec "$BIN" --headless -- --server --port="$PORT" --bind=0.0.0.0 --campaign="$CAMPAIGN" --save="$SAVE" --max-players="$MAX_PLAYERS" --status-file="${STATUS_FILE:-/tmp/dungeoncrawler-status.json}"
