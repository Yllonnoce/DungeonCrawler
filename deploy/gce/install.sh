#!/usr/bin/env bash
# Install or update the dedicated server on a Debian/Ubuntu VM (Google Compute Engine or any other).
# Run ON THE VM from the copied folder:  sudo bash deploy/gce/install.sh /path/to/build/server
# The server build comes from scripts/export_server.sh on the dev machine (build/server/).
set -euo pipefail
SRC=${1:-$(dirname "$0")/../../build/server}
[ -x "$SRC/DungeonCrawler.x86_64" ] || { echo "no server build at $SRC (export it with scripts/export_server.sh and copy build/server here)"; exit 1; }
HERE=$(cd "$(dirname "$0")" && pwd)

# the libraries a headless Godot .NET export still links against
apt-get update
apt-get install -y --no-install-recommends ca-certificates libfontconfig1 libgl1 libxcursor1 libxinerama1 libxrandr2 libxi6 libasound2 libpulse0 libudev1

id -u dungeon >/dev/null 2>&1 || useradd --system --home /var/lib/dungeoncrawler --shell /usr/sbin/nologin dungeon
install -d -o dungeon -g dungeon /var/lib/dungeoncrawler
# a running server holds its files open: stop it before replacing them
systemctl stop dungeoncrawler 2>/dev/null || true
install -d /opt/dungeoncrawler
cp -a "$SRC"/. /opt/dungeoncrawler/
chmod +x /opt/dungeoncrawler/DungeonCrawler.x86_64
chown -R root:root /opt/dungeoncrawler
install -m 644 "$HERE/dungeoncrawler.service" /etc/systemd/system/dungeoncrawler.service
systemctl daemon-reload
systemctl enable --now dungeoncrawler
sleep 3
systemctl --no-pager --lines=15 status dungeoncrawler || true
echo
echo "Server installed. Logs: journalctl -u dungeoncrawler -f   Status file: /var/lib/dungeoncrawler/status.json"
echo "Change the port or campaign: sudo systemctl edit dungeoncrawler (override the Environment= lines), then sudo systemctl restart dungeoncrawler"
