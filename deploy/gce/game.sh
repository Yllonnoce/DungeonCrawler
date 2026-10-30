#!/usr/bin/env bash
# Day-to-day control of the DungeonCrawler dedicated server on the VM (installed by install.sh as a systemd service).
#   bash ~/DungeonCrawler/deploy/gce/game.sh            menu
#   bash ~/DungeonCrawler/deploy/gce/game.sh restart    restart the service; the game resumes from its save, players rejoin
#   bash ~/DungeonCrawler/deploy/gce/game.sh fresh      start a brand-new game from floor 1 (the old save is kept as server-<date>.json)
#   bash ~/DungeonCrawler/deploy/gce/game.sh update     fetch and install the latest published server build (deploy/gce/fetch.sh), then restart
#   bash ~/DungeonCrawler/deploy/gce/game.sh status     service state, the status file (floor, players) and the address players use
#   bash ~/DungeonCrawler/deploy/gce/game.sh logs       follow the server log (Ctrl-C to stop)
#   bash ~/DungeonCrawler/deploy/gce/game.sh stop       stop the service (the VM keeps running; stop the VM from gcloud to stop billing)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SERVICE=dungeoncrawler
DATA=/var/lib/dungeoncrawler
SAVES="$DATA/godot/app_userdata/DungeonCrawler/saves"
SLOT=$(systemctl show "$SERVICE" -p Environment 2>/dev/null | tr ' ' '\n' | sed -n 's/^SAVE=//p' | tail -1)
SLOT=${SLOT:-server}

status() {
  systemctl --no-pager --lines=0 status "$SERVICE" | sed -n '1,4p' || true
  echo
  if [ -f "$DATA/status.json" ]; then echo "status file: $(cat "$DATA/status.json")"; else echo "no status file yet"; fi
  echo "save slot: $SLOT $( [ -f "$SAVES/$SLOT.json" ] && echo "(saved game on disk)" || echo "(no save yet: next start is a new game)" )"
  local ip; ip=$(curl -s -H 'Metadata-Flavor: Google' http://metadata.google.internal/computeMetadata/v1/instance/network-interfaces/0/access-configs/0/external-ip 2>/dev/null || true)
  [ -n "$ip" ] && echo "players join with: ws://$ip"
}

restart() { sudo systemctl restart "$SERVICE"; sleep 3; status; }

fresh() {
  sudo systemctl stop "$SERVICE"
  if [ -f "$SAVES/$SLOT.json" ]; then
    local keep="$SAVES/$SLOT-$(date +%Y%m%d-%H%M%S).json"
    sudo mv "$SAVES/$SLOT.json" "$keep"
    sudo rm -f "$SAVES/$SLOT.json.bak"
    echo "old game kept as $keep"
  else echo "no saved game to set aside"; fi
  sudo systemctl start "$SERVICE"
  sleep 3; status
}

update() { bash "$HERE/fetch.sh" "${2:-}"; sleep 2; status; }   # fetch.sh installs and restarts the service itself

case "${1:-}" in
  restart) restart ;;
  fresh) fresh ;;
  update) update "$@" ;;
  status) status ;;
  logs) journalctl -u "$SERVICE" -f ;;
  stop) sudo systemctl stop "$SERVICE"; echo "stopped (the VM is still running)" ;;
  "")
    echo "DungeonCrawler server"
    echo "  1) Restart (resume the saved game)"
    echo "  2) Fresh game from floor 1 (old save kept)"
    echo "  3) Update to the latest published build"
    echo "  4) Status"
    echo "  5) Follow the log"
    echo "  6) Stop the server"
    read -r -p "Choose [1-6]: " pick
    case "$pick" in 1) restart ;; 2) fresh ;; 3) update ;; 4) status ;; 5) journalctl -u "$SERVICE" -f ;; 6) sudo systemctl stop "$SERVICE"; echo stopped ;; *) echo "Nothing done." ;; esac ;;
  *) echo "usage: $0 [restart|fresh|update [tag]|status|logs|stop]" >&2; exit 2 ;;
esac
