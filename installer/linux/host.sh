#!/usr/bin/env bash
# Host a DungeonCrawler game for your friends from this computer. Run it and leave the window open; Ctrl+C stops it.
#   ./host.sh              port 7777, the shipped campaign, up to 4 players
#   ./host.sh 8000 6       another port, up to 6 players
# Friends start the game, choose Join Server and enter the address this prints. People on your home network use the
# first address; people elsewhere need the second one AND your router to forward TCP port 7777 to this computer.
# The game is saved as it goes (slot "server"), so stopping and starting again continues where everyone left off.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
port=${1:-7777}; players=${2:-4}
game="$here/DungeonCrawler.x86_64"
[ -x "$game" ] || game="$HOME/.local/opt/DungeonCrawler/DungeonCrawler.x86_64"
[ -x "$game" ] || { echo "DungeonCrawler.x86_64 is not next to this script and the game is not installed."; exit 1; }
lan=$(hostname -I 2>/dev/null | awk '{print $1}')
public=$(curl -s --max-time 3 https://api.ipify.org 2>/dev/null || true)
echo "DungeonCrawler server on port $port, up to $players players."
echo "  Same network:  ws://${lan:-<address of this computer>}:$port"
[ -n "$public" ] && echo "  Internet:      ws://$public:$port   (forward TCP $port on your router to this computer first)"
echo "Leave this window open while playing. Ctrl+C stops the server; the game is saved and continues next time."
echo
exec "$game" --headless -- --server --port="$port" --bind=0.0.0.0 --campaign=builtin --save=server --max-players="$players"
