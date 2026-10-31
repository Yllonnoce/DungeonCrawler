#!/bin/bash
# Host a DungeonCrawler game for your friends from this Mac. Double-click it and leave the window open; Ctrl+C or
# closing the window stops it.
#   ./Host.command              port 7777, the shipped campaign, up to 4 players
#   ./Host.command 8000 6       another port, up to 6 players
# Friends start the game, choose Join Server and enter the address this prints. People on your home network use the
# first address; people elsewhere need the second one AND your router to forward TCP port 7777 to this Mac.
# The game is saved as it goes (slot "server"), so stopping and starting again continues where everyone left off.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
port=${1:-7777}; players=${2:-4}
for app in "$here/DungeonCrawler.app" "/Applications/DungeonCrawler.app" "$HOME/Applications/DungeonCrawler.app"; do
  [ -x "$app/Contents/MacOS/DungeonCrawler" ] && break
done
[ -x "$app/Contents/MacOS/DungeonCrawler" ] || { echo "DungeonCrawler.app is not next to this script and not in Applications."; exit 1; }
lan=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || true)
public=$(curl -s --max-time 3 https://api.ipify.org 2>/dev/null || true)
echo "DungeonCrawler server on port $port, up to $players players."
echo "  Same network:  ws://${lan:-<address of this Mac>}:$port"
[ -n "$public" ] && echo "  Internet:      ws://$public:$port   (forward TCP $port on your router to this Mac first)"
echo "macOS may ask whether to allow incoming connections: click Allow."
echo "Leave this window open while playing. Ctrl+C stops the server; the game is saved and continues next time."
echo
exec "$app/Contents/MacOS/DungeonCrawler" --headless -- --server --port="$port" --bind=0.0.0.0 --campaign=builtin --save=server --max-players="$players"
